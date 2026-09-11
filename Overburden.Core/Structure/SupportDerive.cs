using Overburden.Core.Content;
using Overburden.Core.Meshing;
using Overburden.Core.Voxels;

namespace Overburden.Core.Structure;

/// <summary>What one derive found, beside the field it wrote.</summary>
/// <param name="Solid">Solid voxels in the chunk.</param>
/// <param name="AtRisk">Solid voxels with a negative budget.</param>
/// <param name="MaxDeficit">The deepest deficit, 0..K.</param>
/// <param name="Pops">Voxels settled by the search: its cost, in visits.</param>
/// <param name="Pushes">Tentative values written: pops plus every improvement that was later bettered.</param>
/// <param name="Trivial">True when the closed form answered and no search ran.</param>
public readonly record struct SupportDeriveResult(int Solid, int AtRisk, int MaxDeficit, int Pops, int Pushes, bool Trivial);

/// <summary>
/// The from-scratch derive of one chunk's support field from a padded copy of its voxels, the six
/// neighbours' face layers, and the chunk's cost table — M9-PLAN decision 4's unit of work.
/// </summary>
/// <remarks>
/// <para>
/// <b>Support is skylight upside down, and the derive is a shortest path rather than a
/// propagation.</b> The field is the max over paths from an anchor of the budget the path leaves,
/// with sideways spending, resting refunding and hanging spending double. Subtract the refund
/// per voxel of height — <c>B′ = B − r · y</c> — and every step becomes non-positive: an up step
/// is free, a sideways step costs its material's share, a down step costs at least a refund.
/// That is Dijkstra on a max-heap, one pop per voxel, and with 365 possible primed levels a
/// bucket queue makes it linear. The cap at <c>K</c> is a clamp that only lowers a value, and a
/// monotone non-increasing relaxation keeps the label-setting argument intact. The M9a pass's
/// item 2; the plan expected a label-correcting search and this is cheaper by construction.
/// </para>
/// <para>
/// <b>A path dies at <c>−K</c>, so a candidate at or below the floor is never pushed</b>
/// (<see cref="SupportRule.IsLive"/>), and a solid voxel nothing live reaches is
/// <see cref="ChunkSupport.Unreached"/> — the floor is neither a source nor a clamp, which is what
/// keeps every relaxation non-positive in primed space and the fixpoint unique. The first build
/// pushed every solid voxel at the floor and a chunk in mid-air held its top two layers; the
/// second flooded the floor from the anchors and a column across a chunk face settled at the
/// floor rather than unreached, each half justifying the other. See <see cref="ChunkSupport.Unreached"/>.
/// </para>
/// <para>
/// <b>Across a face the derive reads two things and trusts each for what it knows.</b> The padded
/// border says whether the voxel across is solid — or missing, which anchors, M4a's rule — and
/// the neighbour's layer says what it is worth, or is absent and anchors. A layer that calls a
/// voxel not solid where the border says solid is a neighbour derived before an edit; the voxel
/// counts as anchored until that neighbour runs again, which is the optimistic direction.
/// </para>
/// <para>
/// <b>Reused scratch, no allocation per derive.</b> A derive is a worker's job in M9b and 32,768
/// tentative values, costs and flags plus the bucket lists are the whole of its working set; one
/// instance per worker, never shared.
/// </para>
/// </remarks>
public sealed class SupportDerive
{
    private const int Size = VoxelConstants.ChunkSize;
    private const int Volume = VoxelConstants.ChunkVolume;

    private readonly int[] _tentative = new int[Volume];
    private readonly byte[] _cost = new byte[Volume];
    private readonly bool[] _done = new bool[Volume];
    private readonly byte[] _values = new byte[Volume];
    private List<int>[] _buckets = [];
    private int _levelOffset;
    private int _pushes;

    /// <summary>
    /// Derives the field and returns it. The field returned is new each call; the scratch is not.
    /// </summary>
    /// <param name="voxels">The chunk and its one-voxel border, <see cref="PaddedChunk.MissingMaterial"/> where a neighbour is not loaded.</param>
    /// <param name="materials">Solidity and support distance.</param>
    /// <param name="rule">The costs.</param>
    /// <param name="costs">The chunk's cost table over the water table.</param>
    /// <param name="faces">The neighbours' layers, or anchors.</param>
    public ChunkSupport Derive(
        PaddedChunk voxels,
        MaterialTable materials,
        SupportRule rule,
        SupportCostTable costs,
        SupportFaces faces,
        out SupportDeriveResult result) =>
        Derive(voxels, materials, rule, costs, faces, out result, closedForm: true);

    /// <summary>
    /// The derive with the closed form switchable off, so a test can hold the closed form to the
    /// search on the one input where both apply.
    /// </summary>
    internal ChunkSupport Derive(
        PaddedChunk voxels,
        MaterialTable materials,
        SupportRule rule,
        SupportCostTable costs,
        SupportFaces faces,
        out SupportDeriveResult result,
        bool closedForm)
    {
        ArgumentNullException.ThrowIfNull(voxels);
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(costs);
        ArgumentNullException.ThrowIfNull(faces);

        int refund = rule.Refund;
        EnsureBuckets(refund);
        _pushes = 0;

        // Pass 1: classify every voxel. Nothing is pushed yet: a solid voxel is unreached until
        // a live path arrives from the border.
        int solid = 0;
        bool allSolid = true;
        for (int z = 0; z < Size; z++)
        {
            for (int x = 0; x < Size; x++)
            {
                int column = VoxelPos.Index(x, 0, z);
                for (int y = 0; y < Size; y++)
                {
                    int index = column | y;
                    int material = voxels[x, y, z];
                    _done[index] = false;
                    if (!materials.IsSolid(material))
                    {
                        _cost[index] = 0;
                        _tentative[index] = int.MinValue;
                        allSolid = false;
                        continue;
                    }

                    solid++;
                    _cost[index] = (byte)costs.Cost(x, y, z, material);
                    _tentative[index] = ChunkSupport.UnreachedBudget;
                }
            }
        }

        if (solid == 0)
        {
            result = new SupportDeriveResult(0, 0, 0, 0, 0, Trivial: true);
            return ChunkSupport.Uniform(ChunkSupport.NotSolid);
        }

        // The closed form: all solid and resting on a full layer is K throughout, because every
        // voxel rests on a full one. Pinned against the search by a test, since two derivations
        // of one field are two that can drift.
        if (closedForm && allSolid && BottomIsFull(voxels, materials, faces))
        {
            result = new SupportDeriveResult(solid, 0, 0, 0, 0, Trivial: true);
            return ChunkSupport.Uniform(ChunkSupport.Full);
        }

        // Pass 2: the border, relaxed into the interior as sources.
        for (int face = 0; face < VoxelFace.Count; face++)
        {
            RelaxFace(face, voxels, materials, rule, faces, refund);
        }

        // Pass 3: the search, highest primed level first.
        int pops = 0;
        for (int level = _buckets.Length - 1; level >= 0; level--)
        {
            List<int> bucket = _buckets[level];
            while (bucket.Count > 0)
            {
                int index = bucket[^1];
                bucket.RemoveAt(bucket.Count - 1);
                if (_done[index])
                {
                    continue;
                }

                _done[index] = true;
                pops++;
                Relax(index, rule, refund);
            }
        }

        // Pass 4: encode.
        int atRisk = 0;
        int maxDeficit = 0;
        for (int index = 0; index < Volume; index++)
        {
            if (_cost[index] == 0)
            {
                _values[index] = ChunkSupport.NotSolid;
                continue;
            }

            int budget = _tentative[index];
            byte value = ChunkSupport.Encode(budget);
            _values[index] = value;
            if (budget < 0)
            {
                atRisk++;
                int deficit = ChunkSupport.Deficit(value);
                if (deficit > maxDeficit)
                {
                    maxDeficit = deficit;
                }
            }
        }

        result = new SupportDeriveResult(solid, atRisk, maxDeficit, pops, _pushes, Trivial: false);
        return ChunkSupport.Dense((byte[])_values.Clone());
    }

    /// <summary>True when every voxel across the bottom face is worth a full budget.</summary>
    private static bool BottomIsFull(PaddedChunk voxels, MaterialTable materials, SupportFaces faces)
    {
        const int bottom = 3;
        ReadOnlySpan<byte> layer = faces.Layer(bottom);
        for (int z = 0; z < Size; z++)
        {
            for (int x = 0; x < Size; x++)
            {
                int material = voxels[x, -1, z];
                if (material == PaddedChunk.MissingMaterial)
                {
                    continue;
                }

                if (!materials.IsSolid(material))
                {
                    return false;
                }

                if (!layer.IsEmpty && layer[ChunkSupport.LayerIndex(x, z)] != ChunkSupport.Full
                    && layer[ChunkSupport.LayerIndex(x, z)] != ChunkSupport.NotSolid)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Relaxes every interior voxel touching <paramref name="face"/> from the voxel across it.
    /// </summary>
    private void RelaxFace(
        int face, PaddedChunk voxels, MaterialTable materials, SupportRule rule, SupportFaces faces, int refund)
    {
        int axis = VoxelFace.Axis[face];
        int direction = VoxelFace.Direction[face];
        int inside = direction > 0 ? Size - 1 : 0;
        int outside = direction > 0 ? Size : -1;
        (int lo, int hi) = ChunkSupport.OtherAxes(axis);
        ReadOnlySpan<byte> layer = faces.Layer(face);
        Span<int> local = stackalloc int[3];

        for (int b = 0; b < Size; b++)
        {
            for (int a = 0; a < Size; a++)
            {
                local[axis] = outside;
                local[lo] = a;
                local[hi] = b;
                int across = voxels[local[0], local[1], local[2]];

                int budget;
                if (across == PaddedChunk.MissingMaterial)
                {
                    budget = SupportRule.MaxBudget;
                }
                else if (!materials.IsSolid(across))
                {
                    continue;
                }
                else if (layer.IsEmpty)
                {
                    budget = SupportRule.MaxBudget;
                }
                else
                {
                    // Reached: its value. Unreached: no source. Not solid where the border says
                    // solid: a neighbour derived before an edit, anchored until it runs again.
                    byte value = layer[ChunkSupport.LayerIndex(a, b)];
                    if (value == ChunkSupport.Unreached)
                    {
                        continue;
                    }

                    budget = ChunkSupport.IsSolid(value) ? ChunkSupport.Decode(value) : SupportRule.MaxBudget;
                }

                local[axis] = inside;
                int index = VoxelPos.Index(local[0], local[1], local[2]);
                int cost = _cost[index];
                if (cost == 0)
                {
                    continue;
                }

                // Which way the step runs is which face this is: across the bottom face the
                // interior voxel rests on the border; across the top it hangs; else it spans.
                int candidate = face switch
                {
                    3 => rule.Rest(budget),
                    2 => rule.Hang(budget, cost),
                    _ => rule.Span(budget, cost),
                };

                if (SupportRule.IsLive(candidate) && candidate > _tentative[index])
                {
                    Push(index, candidate, refund, local[1]);
                }
            }
        }
    }

    /// <summary>Relaxes the six interior neighbours of a settled voxel.</summary>
    private void Relax(int index, SupportRule rule, int refund)
    {
        int budget = _tentative[index];
        (int x, int y, int z) = VoxelPos.FromIndex(index);

        // Up: the neighbour rests on this voxel.
        if (y + 1 < Size)
        {
            Offer(index + 1, rule.Rest(budget), refund, y + 1);
        }

        // Down: the neighbour hangs from this voxel.
        if (y > 0)
        {
            int below = index - 1;
            int cost = _cost[below];
            if (cost != 0)
            {
                Offer(below, rule.Hang(budget, cost), refund, y - 1);
            }
        }

        if (x + 1 < Size)
        {
            Span(index + Size, budget, rule, refund, y);
        }

        if (x > 0)
        {
            Span(index - Size, budget, rule, refund, y);
        }

        if (z + 1 < Size)
        {
            Span(index + (Size * Size), budget, rule, refund, y);
        }

        if (z > 0)
        {
            Span(index - (Size * Size), budget, rule, refund, y);
        }
    }

    private void Span(int neighbour, int budget, SupportRule rule, int refund, int y)
    {
        int cost = _cost[neighbour];
        if (cost == 0)
        {
            return;
        }

        Offer(neighbour, rule.Span(budget, cost), refund, y);
    }

    private void Offer(int index, int candidate, int refund, int y)
    {
        if (_cost[index] == 0 || _done[index] || !SupportRule.IsLive(candidate) || candidate <= _tentative[index])
        {
            return;
        }

        Push(index, candidate, refund, y);
    }

    private void Push(int index, int budget, int refund, int y)
    {
        _pushes++;
        _tentative[index] = budget;
        _buckets[budget - (refund * y) + _levelOffset].Add(index);
    }

    /// <summary>
    /// Primed levels run from <c>−K − 31r</c> to <c>K</c>; the offset maps the lowest to zero.
    /// </summary>
    private void EnsureBuckets(int refund)
    {
        _levelOffset = SupportRule.MaxBudget + ((Size - 1) * refund);
        int levels = _levelOffset + SupportRule.MaxBudget + 1;
        if (_buckets.Length == levels)
        {
            return;
        }

        _buckets = new List<int>[levels];
        for (int level = 0; level < levels; level++)
        {
            _buckets[level] = [];
        }
    }
}
