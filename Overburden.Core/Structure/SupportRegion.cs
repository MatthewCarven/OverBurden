using Overburden.Core.Content;
using Overburden.Core.Fluid;
using Overburden.Core.Meshing;
using Overburden.Core.Voxels;

namespace Overburden.Core.Structure;

/// <summary>Why a chunk was queued to derive — the causes the <c>M9:</c> line prints apart.</summary>
public enum SupportTrigger : byte
{
    /// <summary>An edit landed in the chunk, or it was marked dug.</summary>
    Edit,

    /// <summary>The chunk arrived, or a face neighbour did and its border is to be re-read.</summary>
    Arrival,

    /// <summary>A neighbour's face layer moved — the join.</summary>
    Neighbour,

    /// <summary>A face neighbour left the loaded world and anchors now.</summary>
    Departure,

    /// <summary>The water table over an analysed chunk moved.</summary>
    Table,
}

/// <summary>
/// What a derive needs, gathered on the sim thread at submission (M9b, decision 1): the faces
/// copied from the neighbours' current fields and the cost table built from the live water
/// table. The 27 voxel references are the pipeline's to gather, because the copy-on-write flag
/// they need is the pipeline's.
/// </summary>
public sealed class SupportDeriveRequest
{
    internal SupportDeriveRequest(ChunkPos chunk, SupportFaces faces, SupportCostTable costs, bool urgent)
    {
        Chunk = chunk;
        Faces = faces;
        Costs = costs;
        Urgent = urgent;
    }

    public ChunkPos Chunk { get; }

    /// <summary>The neighbours' layers as they stood at submission; owned by the derive from here.</summary>
    public SupportFaces Faces { get; }

    /// <summary>The chunk's cost table over the water table as it stood at submission.</summary>
    public SupportCostTable Costs { get; }

    /// <summary>True when an edit is among the causes — the derive takes the remesh's own urgency band.</summary>
    public bool Urgent { get; }
}

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
/// will hold — and decision 7's barrier is what keeps a collapse from reading a transient.
/// </para>
/// <para>
/// <b>Two ways to run it, one join (M9b).</b> <see cref="Settle"/> derives in a loop on the
/// calling thread — the bench's and the suite's arm. The game pumps the same queue through
/// <see cref="TryTakeNext"/> / <see cref="Applied"/>: a derive leaves as a request gathered on
/// the sim thread, runs on a worker, and lands as a field; between the two the chunk is <em>in
/// flight</em>, and a trigger that reaches it then marks it stale so the landing requeues it —
/// <c>NeedsRemesh</c> surviving a mesh completing, third outing. That mark is also the whole of
/// the coalescing: at most one derive in flight and one queued per chunk, however many edits land.
/// </para>
/// <para>
/// <b>Settled is per chunk over its seven</b> — the chunk and its analysed face neighbours all
/// neither queued nor in flight — and only a settled field reaches the mesher
/// (<see cref="SettledField"/>). The join climbs, so a chunk derived before its neighbour reads at
/// risk where it will hold; a tint reading that would flash for a round. Decision 7's barrier,
/// applied to the tint as well as to the collapse.
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
    private readonly Dictionary<ChunkPos, ChunkSupport> _settled = [];
    private readonly Queue<ChunkPos> _queue = [];
    private readonly HashSet<ChunkPos> _queued = [];
    private readonly HashSet<ChunkPos> _urgent = [];
    private readonly HashSet<ChunkPos> _inFlight = [];
    private readonly HashSet<ChunkPos> _staleInFlight = [];
    private readonly SupportDerive _derive = new();
    private readonly PaddedChunk _padded = new();
    private readonly VoxelChunk?[] _neighbourhood = new VoxelChunk?[PaddedChunk.NeighbourhoodSize];
    private readonly byte[] _before = new byte[ChunkSupport.LayerSize];
    private readonly byte[] _after = new byte[ChunkSupport.LayerSize];
    private readonly long[] _triggers = new long[5];
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

    /// <summary>The rule the fields are derived under.</summary>
    public SupportRule Rule => _rule;

    /// <summary>Derives run so far, both arms.</summary>
    public long Derives { get; private set; }

    /// <summary>Voxels the search settled, over every derive.</summary>
    public long Pops { get; private set; }

    /// <summary>Tentative values the search wrote, over every derive: pops plus bettered improvements.</summary>
    public long Pushes { get; private set; }

    /// <summary>Derives answered by the closed form.</summary>
    public long TrivialDerives { get; private set; }

    /// <summary>Stopwatch ticks spent gathering padded copies and face layers, over every derive (the loop arm).</summary>
    public long GatherTicks { get; private set; }

    /// <summary>Stopwatch ticks spent in the derive itself, over every derive (the loop arm).</summary>
    public long DeriveTicks { get; private set; }

    /// <summary>Derives whose result landed after a trigger reached the chunk in flight, so it went round again.</summary>
    public long RequeuedStale { get; private set; }

    /// <summary>Derives whose chunk had left the loaded world by the time the result landed.</summary>
    public long DroppedDeparted { get; private set; }

    /// <summary>Requests taken that found the chunk not loaded and derived nothing.</summary>
    public long SkippedUnloaded { get; private set; }

    /// <summary>The chunks the rule applies to.</summary>
    public IReadOnlyCollection<ChunkPos> Analysed => _analysed;

    /// <summary>The chunks marked dug.</summary>
    public IReadOnlyCollection<ChunkPos> Dug => _dug;

    /// <summary>Chunks waiting to derive.</summary>
    public int Pending => _queue.Count;

    /// <summary>Chunks whose derive has been taken and not yet landed.</summary>
    public int InFlight => _inFlight.Count;

    /// <summary>Fields held.</summary>
    public int FieldCount => _fields.Count;

    /// <summary>Solid voxels with a negative budget across every derived field.</summary>
    public int AtRiskVoxels => _fields.Values.Sum(field => field.AtRiskCount);

    /// <summary>Chunks whose field has at least one voxel at risk.</summary>
    public int AtRiskChunks => _fields.Values.Count(field => field.AtRiskCount > 0);

    /// <summary>Solid voxels at risk across the <em>settled</em> fields — what the tint shows and what a collapse may read.</summary>
    public int SettledAtRiskVoxels => _settled.Values.Sum(field => field.AtRiskCount);

    /// <summary>Settled chunks with at least one voxel at risk.</summary>
    public int SettledAtRiskChunks => _settled.Values.Count(field => field.AtRiskCount > 0);

    /// <summary>Chunks with a field that the barrier is holding back: some of their seven is queued or in flight.</summary>
    public int Unsettled => _fields.Keys.Count(chunk => !IsSettled(chunk));

    /// <summary>What the fields cost in memory.</summary>
    public long FieldBytes => _fields.Values.Sum(field => (long)field.EstimatedBytes);

    /// <summary>
    /// Queue entries by cause, over the lifetime: counted when a trigger changes something — a
    /// chunk newly queued, or newly marked stale in flight — so a bite's 32,768 edit hooks count
    /// as the handful of chunks they queue. A chunk queued for two reasons counts under both.
    /// </summary>
    public long Triggered(SupportTrigger trigger) => _triggers[(int)trigger];

    public bool IsAnalysed(ChunkPos chunk) => _analysed.Contains(chunk);

    public bool IsDug(ChunkPos chunk) => _dug.Contains(chunk);

    public ChunkSupport? FieldAt(ChunkPos chunk) => _fields.GetValueOrDefault(chunk);

    /// <summary>
    /// The field the mesher may read: the chunk's field as it stood the last time the chunk and
    /// its six were all at rest, or null when it has never settled.
    /// </summary>
    public ChunkSupport? SettledField(ChunkPos chunk) => _settled.GetValueOrDefault(chunk);

    /// <summary>
    /// The field a collapse may read (M9c-1, decision 2): the chunk's field only when the chunk is
    /// settled <em>now</em>, in which case it is the live field, derived from voxels no edit has
    /// touched since. False for a chunk with no field, and for one whose snapshot is the last
    /// settle's — a chunk mined since its timer armed is unsettled, and a fall reading its stale
    /// snapshot would put rubble at a landing for a voxel that is air.
    /// </summary>
    public bool TryReadSettled(ChunkPos chunk, out ChunkSupport field)
    {
        if (_settled.TryGetValue(chunk, out ChunkSupport? settled) && IsSettled(chunk))
        {
            field = settled;
            return true;
        }

        field = null!;
        return false;
    }

    /// <summary>The settled snapshots by chunk — what the collapse sweep walks. Not a copy: do not mutate the region while enumerating.</summary>
    public IReadOnlyDictionary<ChunkPos, ChunkSupport> SettledFields => _settled;

    /// <summary>Whether the chunk and its analysed face neighbours are all neither queued nor in flight.</summary>
    public bool IsSettled(ChunkPos chunk)
    {
        if (IsPendingOrInFlight(chunk))
        {
            return false;
        }

        for (int face = 0; face < VoxelFace.Count; face++)
        {
            ChunkPos neighbour = Across(chunk, face);
            if (_analysed.Contains(neighbour) && IsPendingOrInFlight(neighbour))
            {
                return false;
            }
        }

        return true;
    }

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

        Enqueue(chunk, SupportTrigger.Edit);
        for (int face = 0; face < VoxelFace.Count; face++)
        {
            Enqueue(Across(chunk, face), SupportTrigger.Edit);
        }
    }

    /// <summary>An edit landed in a chunk. Only what was dug is edited, so this is <see cref="MarkDug"/>.</summary>
    /// <remarks>
    /// Per voxel — a bite is 32,768 calls — so the common case, a chunk already dug and already
    /// waiting urgently, is three set lookups and out. The per-chunk dirty list the pipeline keeps
    /// is the lever if a bite ever hitches on this (M9b, decision 5).
    /// </remarks>
    public void Edited(ChunkPos chunk)
    {
        if (_dug.Contains(chunk) && _urgent.Contains(chunk) && (_queued.Contains(chunk) || _staleInFlight.Contains(chunk)))
        {
            return;
        }

        MarkDug(chunk);
    }

    /// <summary>A chunk left the loaded world: its field goes, and its neighbours anchor to it.</summary>
    public void Departed(ChunkPos chunk)
    {
        _fields.Remove(chunk);
        _settled.Remove(chunk);
        if (_queued.Remove(chunk))
        {
            // Left in the queue as a position; TryTakeNext finds it unloaded and skips it.
            _urgent.Remove(chunk);
        }

        for (int face = 0; face < VoxelFace.Count; face++)
        {
            ChunkPos neighbour = Across(chunk, face);
            if (_analysed.Contains(neighbour))
            {
                Enqueue(neighbour, SupportTrigger.Departure);
            }
        }
    }

    /// <summary>A chunk arrived — first time or a return: it derives if analysed, and its neighbours re-read its border.</summary>
    public void Arrived(ChunkPos chunk)
    {
        if (_analysed.Contains(chunk))
        {
            Enqueue(chunk, SupportTrigger.Arrival);
        }

        for (int face = 0; face < VoxelFace.Count; face++)
        {
            ChunkPos neighbour = Across(chunk, face);
            if (_analysed.Contains(neighbour))
            {
                Enqueue(neighbour, SupportTrigger.Arrival);
            }
        }
    }

    /// <summary>
    /// The pipeline's arrival hook: a chunk that arrives edited is dug — the portal rule read
    /// off the same event <c>PortalTracker</c> reads it from — and then it arrives.
    /// </summary>
    public void Arrived(ChunkPos chunk, bool hasEdits)
    {
        if (hasEdits)
        {
            MarkDug(chunk);
        }

        Arrived(chunk);
    }

    /// <summary>The water table moved: every analysed chunk derives again. Decision 4's fourth trigger, unrefined.</summary>
    public void TableMoved()
    {
        foreach (ChunkPos chunk in _analysed)
        {
            Enqueue(chunk, SupportTrigger.Table);
        }
    }

    /// <summary>
    /// Derives until nothing is queued, or <paramref name="maxDerives"/> have run. Returns the
    /// rounds it took — a round is the queue as it stood when the round began.
    /// </summary>
    /// <remarks>
    /// The loop arm, on the calling thread, through the same take / land pair the game pumps —
    /// so the bench and the fuzz exercise the join the pipeline runs, not a twin of it.
    /// </remarks>
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
                if (!TryTakeOne(out ChunkPos chunk, out SupportDeriveRequest? request))
                {
                    break;
                }

                if (request is null)
                {
                    // Not loaded: skipped, and the position was consumed from this round.
                    continue;
                }

                long started = System.Diagnostics.Stopwatch.GetTimestamp();
                GatherNeighbourhood(chunk);
                long gathered = System.Diagnostics.Stopwatch.GetTimestamp();
                GatherTicks += gathered - started;

                ChunkSupport field = _derive.Derive(_padded, _materials, _rule, request.Costs, request.Faces, out SupportDeriveResult result);
                DeriveTicks += System.Diagnostics.Stopwatch.GetTimestamp() - gathered;

                Applied(chunk, field, result, settled: null);
                run++;
            }
        }

        return rounds;
    }

    /// <summary>
    /// Takes the next chunk to derive, marking it in flight, with its faces and cost table
    /// gathered now. False when nothing is queued. A queued chunk that is not loaded is
    /// consumed and skipped — its field is dropped and its neighbours anchor to it.
    /// </summary>
    public bool TryTakeNext(out SupportDeriveRequest request)
    {
        while (TryTakeOne(out _, out SupportDeriveRequest? taken))
        {
            if (taken is not null)
            {
                request = taken;
                return true;
            }
        }

        request = null!;
        return false;
    }

    /// <summary>
    /// One dequeue: false when the queue is empty; true with a null request when the chunk was
    /// not loaded and nothing is to derive.
    /// </summary>
    private bool TryTakeOne(out ChunkPos chunk, out SupportDeriveRequest? request)
    {
        request = null;
        if (_queue.Count == 0)
        {
            chunk = default;
            return false;
        }

        chunk = _queue.Dequeue();
        _queued.Remove(chunk);
        bool urgent = _urgent.Remove(chunk);

        if (!_analysed.Contains(chunk))
        {
            return true;
        }

        if (_voxels(chunk) is null)
        {
            // Not loaded: no field, and the neighbours anchor to it through the missing border.
            // Its seven may have been waiting on this entry to settle.
            SkippedUnloaded++;
            _fields.Remove(chunk);
            _settled.Remove(chunk);
            SettleAround(chunk, settled: null);
            return true;
        }

        var faces = new SupportFaces();
        GatherFaces(chunk, faces);
        SupportCostTable costs = _table is null
            ? _dry ??= SupportCostTable.Dry(_materials, _rule)
            : SupportCostTable.Build(chunk, _table, _materials, _rule);

        _inFlight.Add(chunk);
        request = new SupportDeriveRequest(chunk, faces, costs, urgent);
        return true;
    }

    /// <summary>
    /// A derive landed: the field is kept, neighbours across a moved layer are queued, a chunk
    /// that was triggered while in flight goes round again, and every chunk of its seven that is
    /// now at rest is settled and listed in <paramref name="settled"/>.
    /// </summary>
    /// <remarks>
    /// A result for a chunk that has left is dropped — the region has no field to keep for a
    /// chunk it cannot read — and one for a chunk that left and came back is kept and requeued,
    /// because the return marked it stale.
    /// </remarks>
    public void Applied(ChunkPos chunk, ChunkSupport field, SupportDeriveResult result, List<ChunkPos>? settled)
    {
        ArgumentNullException.ThrowIfNull(field);

        _inFlight.Remove(chunk);
        bool stale = _staleInFlight.Remove(chunk);

        Derives++;
        Pops += result.Pops;
        Pushes += result.Pushes;
        if (result.Trivial)
        {
            TrivialDerives++;
        }

        if (!_analysed.Contains(chunk) || _voxels(chunk) is null)
        {
            DroppedDeparted++;
            _fields.Remove(chunk);
            _settled.Remove(chunk);
            SettleAround(chunk, settled);
            return;
        }

        ChunkSupport? previous = _fields.GetValueOrDefault(chunk);
        _fields[chunk] = field;

        for (int face = 0; face < VoxelFace.Count; face++)
        {
            ChunkPos neighbour = Across(chunk, face);
            if (!_analysed.Contains(neighbour) || !LayerMoved(previous, field, face))
            {
                continue;
            }

            Enqueue(neighbour, SupportTrigger.Neighbour);
        }

        if (stale)
        {
            RequeuedStale++;
            Enqueue(chunk, SupportTrigger.Edit, countTrigger: false);
        }

        SettleAround(chunk, settled);
    }

    /// <summary>A derive that did not land: the chunk is no longer in flight, and goes round again if asked.</summary>
    public void Abandoned(ChunkPos chunk, bool requeue)
    {
        _inFlight.Remove(chunk);
        bool stale = _staleInFlight.Remove(chunk);
        if ((requeue || stale) && _analysed.Contains(chunk))
        {
            Enqueue(chunk, SupportTrigger.Edit, countTrigger: false);
        }

        SettleAround(chunk, settled: null);
    }

    /// <summary>Settles whichever of a chunk's seven is now at rest, listing each whose settled field changed.</summary>
    private void SettleAround(ChunkPos chunk, List<ChunkPos>? settled)
    {
        TrySettle(chunk, settled);
        for (int face = 0; face < VoxelFace.Count; face++)
        {
            TrySettle(Across(chunk, face), settled);
        }
    }

    private void TrySettle(ChunkPos chunk, List<ChunkPos>? settled)
    {
        if (!_fields.TryGetValue(chunk, out ChunkSupport? field) || !IsSettled(chunk))
        {
            return;
        }

        if (_settled.TryGetValue(chunk, out ChunkSupport? already) && ReferenceEquals(already, field))
        {
            return;
        }

        _settled[chunk] = field;
        settled?.Add(chunk);
    }

    private bool IsPendingOrInFlight(ChunkPos chunk) => _queued.Contains(chunk) || _inFlight.Contains(chunk);

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

    private void GatherNeighbourhood(ChunkPos chunk)
    {
        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    _neighbourhood[PaddedChunk.NeighbourIndex(dx, dy, dz)] = _voxels(chunk.Offset(dx, dy, dz));
                }
            }
        }

        _padded.CopyFrom(_neighbourhood, PaddedChunk.MissingMaterial);
    }

    /// <summary>The six faces as the join sees them now: anchored, a neighbour's layer, or nothing yet.</summary>
    private void GatherFaces(ChunkPos chunk, SupportFaces faces)
    {
        for (int face = 0; face < VoxelFace.Count; face++)
        {
            ChunkPos neighbour = Across(chunk, face);
            if (!_analysed.Contains(neighbour))
            {
                faces.Anchor(face);
            }
            else if (_fields.TryGetValue(neighbour, out ChunkSupport? field))
            {
                faces.From(face, field);
            }
            else if (_voxels(neighbour) is null)
            {
                faces.Anchor(face);
            }
            else
            {
                faces.Unreached(face);
            }
        }
    }

    private void Enqueue(ChunkPos chunk, SupportTrigger trigger, bool countTrigger = true)
    {
        if (trigger == SupportTrigger.Edit)
        {
            _urgent.Add(chunk);
        }

        bool changed;
        if (_inFlight.Contains(chunk))
        {
            // The derive out on a worker read a snapshot older than this: its landing requeues.
            changed = _staleInFlight.Add(chunk);
        }
        else
        {
            changed = _queued.Add(chunk);
            if (changed)
            {
                _queue.Enqueue(chunk);
            }
        }

        if (changed && countTrigger)
        {
            _triggers[(int)trigger]++;
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
