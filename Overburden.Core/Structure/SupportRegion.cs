using Overburden.Core.Content;
using Overburden.Core.Fluid;
using Overburden.Core.Meshing;
using Overburden.Core.Voxels;

namespace Overburden.Core.Structure;

/// <summary>
/// The support fields of an analysed set of chunks, joined across their faces by re-deriving
/// whoever's input changed — M9-PLAN decisions 3 and 4 as one object, with no thread in it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The analysed set is every dug chunk and the chunk across each of its six faces</b>, the
/// portal rule's — a bitten chunk is all air and the rock over it is the undug chunk above. A
/// voxel across a face into a chunk outside the set, or not loaded, is anchored through the
/// solid voxels of its border. Nothing else is analysed, so a natural cave is stable until a
/// chunk touching it is dug, at the cost decision 3 states.
/// </para>
/// <para>
/// <b>The join is a worklist, and it climbs.</b> A derive writes a fresh field; each of its six
/// face layers is compared with the layer the previous field had — or, for a first derive, with
/// the nothing its neighbours assumed — and every neighbour across a layer that moved is queued.
/// The fixpoint is unique (decision 1's proof), so the iteration reaches it from either side;
/// <b>it starts from below</b> — an analysed neighbour that has not derived contributes nothing
/// through its face, an unloaded or unanalysed one anchors as always — because from below every
/// round carries live paths one chunk face further and the diameter of the analysed set bounds
/// the rounds, where from above (decision 4's optimistic start, the first build) a body across a
/// face loses one refund a round and a 2 × 2 room took 33 rounds and 156 derives against 2 and 34, a 4 × 4 room 61 and 724 against 3 and 128.
/// The transient is the other way — a chunk derived before its neighbour reads at risk where it
/// will hold — and decision 7's barrier is what keeps a collapse from reading a transient. In the
/// game (M9b) each derive is a chunk job and this object's queue is the pipeline's; here it is a
/// loop, which is what the bench and the fuzz need.
/// </para>
/// <para>
/// <b>Not thread-safe, and not the game's home for the fields.</b> The fields live here so the
/// suite can hold the whole region to M5b's oracle: every solid voxel of every field satisfies
/// the rule against its real neighbours, which with uniqueness is complete correctness with no
/// second propagation to get wrong.
/// </para>
/// </remarks>
public sealed class SupportRegion
{
    private readonly Func<ChunkPos, VoxelChunk?> _voxels;
    private readonly MaterialTable _materials;
    private readonly SupportRule _rule;
    private readonly IWaterTable? _table;
    private readonly HashSet<ChunkPos> _dug = [];
    private readonly HashSet<ChunkPos> _analysed = [];
    private readonly Dictionary<ChunkPos, ChunkSupport> _fields = [];
    private readonly Queue<ChunkPos> _queue = [];
    private readonly HashSet<ChunkPos> _queued = [];
    private readonly SupportDerive _derive = new();
    private readonly PaddedChunk _padded = new();
    private readonly VoxelChunk?[] _neighbourhood = new VoxelChunk?[PaddedChunk.NeighbourhoodSize];
    private readonly SupportFaces _faces = new();
    private readonly byte[] _before = new byte[ChunkSupport.LayerSize];
    private readonly byte[] _after = new byte[ChunkSupport.LayerSize];
    private SupportCostTable? _dry;

    /// <param name="voxels">The world's chunks: null for one that is not loaded.</param>
    /// <param name="table">The water table, or null for dry everywhere.</param>
    public SupportRegion(Func<ChunkPos, VoxelChunk?> voxels, MaterialTable materials, SupportRule rule, IWaterTable? table = null)
    {
        _voxels = voxels ?? throw new ArgumentNullException(nameof(voxels));
        _materials = materials ?? throw new ArgumentNullException(nameof(materials));
        _rule = rule ?? throw new ArgumentNullException(nameof(rule));
        _table = table;
    }

    /// <summary>Derives run so far.</summary>
    public long Derives { get; private set; }

    /// <summary>Voxels the search settled, over every derive.</summary>
    public long Pops { get; private set; }

    /// <summary>Tentative values the search wrote, over every derive: pops plus bettered improvements.</summary>
    public long Pushes { get; private set; }

    /// <summary>Derives answered by the closed form.</summary>
    public long TrivialDerives { get; private set; }

    /// <summary>Stopwatch ticks spent gathering padded copies and face layers, over every derive.</summary>
    public long GatherTicks { get; private set; }

    /// <summary>Stopwatch ticks spent in the derive itself, over every derive.</summary>
    public long DeriveTicks { get; private set; }

    /// <summary>The chunks the rule applies to.</summary>
    public IReadOnlyCollection<ChunkPos> Analysed => _analysed;

    /// <summary>The chunks marked dug.</summary>
    public IReadOnlyCollection<ChunkPos> Dug => _dug;

    /// <summary>Chunks waiting to derive.</summary>
    public int Pending => _queue.Count;

    /// <summary>Solid voxels with a negative budget across every derived field.</summary>
    public int AtRiskVoxels => _fields.Values.Sum(field => field.AtRiskCount);

    /// <summary>Chunks whose field has at least one voxel at risk.</summary>
    public int AtRiskChunks => _fields.Values.Count(field => field.AtRiskCount > 0);

    /// <summary>What the fields cost in memory.</summary>
    public long FieldBytes => _fields.Values.Sum(field => (long)field.EstimatedBytes);

    public bool IsAnalysed(ChunkPos chunk) => _analysed.Contains(chunk);

    public ChunkSupport? FieldAt(ChunkPos chunk) => _fields.GetValueOrDefault(chunk);

    /// <summary>
    /// Marks a chunk dug — edited — which puts it and its six face neighbours into the analysed
    /// set, and queues it and every analysed neighbour. Idempotent for a chunk already dug.
    /// </summary>
    /// <remarks>
    /// The neighbours are queued whether or not they are new to the set, because an edit at a
    /// chunk's face changes what a neighbour's padded copy holds — solidity across the face — and
    /// the join compares budget layers, not voxels. A neighbour new to the set was an anchor to
    /// the analysed chunks beside it until now; its first derive's layers, compared with that
    /// anchor, are what queue them.
    /// </remarks>
    public void MarkDug(ChunkPos chunk)
    {
        if (_dug.Add(chunk))
        {
            _analysed.Add(chunk);
            for (int face = 0; face < VoxelFace.Count; face++)
            {
                _analysed.Add(Across(chunk, face));
            }
        }

        Enqueue(chunk);
        for (int face = 0; face < VoxelFace.Count; face++)
        {
            Enqueue(Across(chunk, face));
        }
    }

    /// <summary>An edit landed in a chunk. Only what was dug is edited, so this is <see cref="MarkDug"/>.</summary>
    public void Edited(ChunkPos chunk) => MarkDug(chunk);

    /// <summary>A chunk left the loaded world: its field goes, and its neighbours anchor to it.</summary>
    public void Departed(ChunkPos chunk)
    {
        _fields.Remove(chunk);
        _queued.Remove(chunk);
        for (int face = 0; face < VoxelFace.Count; face++)
        {
            ChunkPos neighbour = Across(chunk, face);
            if (_analysed.Contains(neighbour))
            {
                Enqueue(neighbour);
            }
        }
    }

    /// <summary>A chunk arrived — first time or a return: it derives if analysed, and its neighbours re-read its border.</summary>
    public void Arrived(ChunkPos chunk)
    {
        if (_analysed.Contains(chunk))
        {
            Enqueue(chunk);
        }

        for (int face = 0; face < VoxelFace.Count; face++)
        {
            ChunkPos neighbour = Across(chunk, face);
            if (_analysed.Contains(neighbour))
            {
                Enqueue(neighbour);
            }
        }
    }

    /// <summary>The water table moved: every analysed chunk derives again. Decision 4's fourth trigger, unrefined.</summary>
    public void TableMoved()
    {
        foreach (ChunkPos chunk in _analysed)
        {
            Enqueue(chunk);
        }
    }

    /// <summary>
    /// Derives until nothing is queued, or <paramref name="maxDerives"/> have run. Returns the
    /// rounds it took — a round is the queue as it stood when the round began.
    /// </summary>
    public int Settle(int maxDerives = int.MaxValue)
    {
        int rounds = 0;
        int run = 0;
        while (_queue.Count > 0 && run < maxDerives)
        {
            rounds++;
            int inRound = _queue.Count;
            for (int i = 0; i < inRound && run < maxDerives; i++)
            {
                ChunkPos chunk = _queue.Dequeue();
                _queued.Remove(chunk);
                DeriveOne(chunk);
                run++;
            }
        }

        return rounds;
    }

    /// <summary>One derive, and the join: neighbours across a moved layer are queued.</summary>
    private void DeriveOne(ChunkPos chunk)
    {
        if (!_analysed.Contains(chunk))
        {
            return;
        }

        VoxelChunk? own = _voxels(chunk);
        if (own is null)
        {
            // Not loaded: no field, and the neighbours anchor to it through the missing border.
            _fields.Remove(chunk);
            return;
        }

        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        Gather(chunk, own);
        long gathered = System.Diagnostics.Stopwatch.GetTimestamp();
        GatherTicks += gathered - started;

        ChunkSupport? previous = _fields.GetValueOrDefault(chunk);
        SupportCostTable costs = _table is null
            ? _dry ??= SupportCostTable.Dry(_materials, _rule)
            : SupportCostTable.Build(chunk, _table, _materials, _rule);

        ChunkSupport field = _derive.Derive(_padded, _materials, _rule, costs, _faces, out SupportDeriveResult result);
        DeriveTicks += System.Diagnostics.Stopwatch.GetTimestamp() - gathered;
        Derives++;
        Pops += result.Pops;
        Pushes += result.Pushes;
        if (result.Trivial)
        {
            TrivialDerives++;
        }

        _fields[chunk] = field;

        for (int face = 0; face < VoxelFace.Count; face++)
        {
            ChunkPos neighbour = Across(chunk, face);
            if (!_analysed.Contains(neighbour) || !LayerMoved(previous, field, face))
            {
                continue;
            }

            Enqueue(neighbour);
        }
    }

    /// <summary>
    /// Whether the layer a neighbour reads across <paramref name="face"/> differs from what it
    /// read last time — the previous field's, or the anchor it assumed when there was none.
    /// </summary>
    private bool LayerMoved(ChunkSupport? previous, ChunkSupport current, int face)
    {
        current.CopyLayer(face, _after);
        if (previous is null)
        {
            // The neighbour assumed nothing came through this face: any reached voxel moved.
            foreach (byte value in _after)
            {
                if (ChunkSupport.IsReached(value))
                {
                    return true;
                }
            }

            return false;
        }

        previous.CopyLayer(face, _before);
        return !_before.AsSpan().SequenceEqual(_after);
    }

    private void Gather(ChunkPos chunk, VoxelChunk own)
    {
        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    _neighbourhood[PaddedChunk.NeighbourIndex(dx, dy, dz)] =
                        dx == 0 && dy == 0 && dz == 0 ? own : _voxels(chunk.Offset(dx, dy, dz));
                }
            }
        }

        _padded.CopyFrom(_neighbourhood, PaddedChunk.MissingMaterial);

        for (int face = 0; face < VoxelFace.Count; face++)
        {
            ChunkPos neighbour = Across(chunk, face);
            if (!_analysed.Contains(neighbour))
            {
                _faces.Anchor(face);
            }
            else if (_fields.TryGetValue(neighbour, out ChunkSupport? field))
            {
                _faces.From(face, field);
            }
            else if (_voxels(neighbour) is null)
            {
                _faces.Anchor(face);
            }
            else
            {
                _faces.Unreached(face);
            }
        }
    }

    private void Enqueue(ChunkPos chunk)
    {
        if (_queued.Add(chunk))
        {
            _queue.Enqueue(chunk);
        }
    }

    private static string Name(int budget) => budget == ChunkSupport.UnreachedBudget ? "unreached" : budget.ToString();

    /// <summary>The chunk across <paramref name="face"/>.</summary>
    public static ChunkPos Across(ChunkPos chunk, int face)
    {
        int[] offset = VoxelFace.Offset[face];
        return chunk.Offset(offset[0], offset[1], offset[2]);
    }

    /// <summary>
    /// The oracle: every solid voxel of every derived field equals the rule applied to its real
    /// neighbours — across faces too — with the floor and the anchors as sources. Returns each
    /// violation as text; empty is a pass.
    /// </summary>
    /// <remarks>
    /// Unique fixpoint, so this is complete: a field that passes cell for cell is <em>the</em>
    /// field, with no from-scratch pass to compare against and no propagation in the check.
    /// Reads voxels and fields only, through a per-voxel lookup that shares nothing with the
    /// derive's padded copy, bucket queue or layer indexing — which is the point.
    /// </remarks>
    public List<string> VerifyFixpoint(int maxViolations = 16)
    {
        var violations = new List<string>();
        foreach ((ChunkPos chunk, ChunkSupport field) in _fields)
        {
            VoxelChunk? own = _voxels(chunk);
            if (own is null)
            {
                violations.Add($"{chunk}: a field for a chunk that is not loaded");
                continue;
            }

            SupportCostTable costs = _table is null
                ? _dry ??= SupportCostTable.Dry(_materials, _rule)
                : SupportCostTable.Build(chunk, _table, _materials, _rule);

            for (int z = 0; z < VoxelConstants.ChunkSize && violations.Count < maxViolations; z++)
            {
                for (int x = 0; x < VoxelConstants.ChunkSize && violations.Count < maxViolations; x++)
                {
                    for (int y = 0; y < VoxelConstants.ChunkSize && violations.Count < maxViolations; y++)
                    {
                        int material = own.Get(x, y, z);
                        byte stored = field.Value(x, y, z);
                        if (!_materials.IsSolid(material))
                        {
                            if (stored != ChunkSupport.NotSolid)
                            {
                                violations.Add($"{chunk} ({x},{y},{z}): not solid but stored {stored}");
                            }

                            continue;
                        }

                        if (!ChunkSupport.IsSolid(stored))
                        {
                            violations.Add($"{chunk} ({x},{y},{z}): solid but stored not-solid");
                            continue;
                        }

                        int expected = Expected(chunk, x, y, z, costs.Cost(x, y, z, material));
                        int actual = ChunkSupport.Decode(stored);
                        if (expected != actual)
                        {
                            violations.Add(
                                $"{chunk} ({x},{y},{z}): stored {Name(actual)}, rule says {Name(expected)}");
                        }
                    }
                }
            }
        }

        return violations;
    }

    /// <summary>
    /// The rule at one voxel, from its six neighbours as the world and the fields hold them:
    /// the best live step from a reached neighbour, or unreached when there is none.
    /// </summary>
    private int Expected(ChunkPos chunk, int x, int y, int z, int cost)
    {
        int best = ChunkSupport.UnreachedBudget;
        VoxelPos voxel = VoxelPos.FromChunkLocal(chunk, x, y, z);
        for (int face = 0; face < VoxelFace.Count; face++)
        {
            int[] offset = VoxelFace.Offset[face];
            VoxelPos across = voxel.Offset(offset[0], offset[1], offset[2]);
            int? budget = BudgetAt(across);
            if (budget is null || budget == ChunkSupport.UnreachedBudget)
            {
                continue;
            }

            int candidate = face switch
            {
                3 => _rule.Rest(budget.Value),
                2 => _rule.Hang(budget.Value, cost),
                _ => _rule.Span(budget.Value, cost),
            };

            if (SupportRule.IsLive(candidate) && candidate > best)
            {
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>
    /// What a neighbouring voxel is worth: null for air, a full budget for an anchor — not
    /// loaded, not analysed, not derived — <see cref="ChunkSupport.UnreachedBudget"/> for a
    /// floating voxel, else its field's value.
    /// </summary>
    private int? BudgetAt(VoxelPos voxel)
    {
        ChunkPos chunk = voxel.Chunk;
        VoxelChunk? voxels = _voxels(chunk);
        if (voxels is null)
        {
            return SupportRule.MaxBudget;
        }

        (int lx, int ly, int lz) = voxel.Local;
        if (!_materials.IsSolid(voxels.Get(lx, ly, lz)))
        {
            return null;
        }

        if (!_analysed.Contains(chunk) || !_fields.TryGetValue(chunk, out ChunkSupport? field))
        {
            return SupportRule.MaxBudget;
        }

        byte value = field.Value(lx, ly, lz);
        return ChunkSupport.IsSolid(value) ? ChunkSupport.Decode(value) : SupportRule.MaxBudget;
    }
}
