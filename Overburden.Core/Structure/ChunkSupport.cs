using Overburden.Core.Meshing;
using Overburden.Core.Voxels;

namespace Overburden.Core.Structure;

/// <summary>
/// One chunk's support field: a byte per voxel, the budget with an offset, or <i>not solid</i>.
/// </summary>
/// <remarks>
/// <para>
/// <b>32 KiB per analysed solid chunk</b> — M9-PLAN decision 2's cost, taken over the light grid's
/// 512 B because the thing that collapses is voxels and a one-metre collapse in a quarter-metre
/// world is the mismatch M8's minimum-drainable-hole question was about. The order is
/// <see cref="VoxelPos.Index"/>'s, <c>[z][x][y]</c>, so a column is contiguous: every rule here
/// steps vertically more often than not.
/// </para>
/// <para>
/// <b>Two kinds, like <see cref="VoxelChunk"/>: dense, and uniform with no array at all.</b> An
/// all-air chunk holds no field; an all-solid chunk resting on a full face is <c>K</c>
/// throughout and needs no search to say so. That is decision 2's "a chunk with no void within
/// reach holds a flag", and it is what keeps a mine's field to tens of chunks of bytes rather
/// than hundreds.
/// </para>
/// <para>
/// <b>The encoding is this type's and nobody else's.</b> A live budget <c>B</c> in
/// <c>(−K, K]</c> is stored as <c>B + K</c>, <c>1..240</c>; <see cref="Unreached"/> is 0 — solid,
/// and no live path from an anchor reaches it, the plan's <c>−∞</c> and every deficit past
/// <c>K</c> alike; <see cref="NotSolid"/> is 255; the codes between are unused. At risk is
/// <c>value &lt; K</c> — a budget below zero, or unreached — and a face layer handed to a
/// neighbour is these bytes verbatim, so the join compares bytes.
/// </para>
/// </remarks>
public sealed class ChunkSupport
{
    /// <summary>The offset: a stored value is the budget plus this.</summary>
    public const int Offset = SupportRule.MaxBudget;

    /// <summary>The stored value of a full budget — an anchor's, or a voxel resting on one.</summary>
    public const byte Full = (byte)(SupportRule.MaxBudget + Offset);

    /// <summary>The stored value of a budget of exactly zero: held, with nothing to spare.</summary>
    public const byte Zero = (byte)Offset;

    /// <summary>
    /// Solid, and no live path from an anchor reaches it: a body floating free, or one so far
    /// past every wall that its deficit is past <c>K</c>. At risk with the deficit saturated,
    /// and a source of nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The M9a build's finding, in two steps. With the floor a source at every solid voxel, a
    /// chunk placed in mid-air recovered one refund a layer off its own floor and held its top
    /// two layers, 30,720 at risk of 32,768. With the floor a source only where an anchor reached,
    /// a column straddling a chunk face converged to the floor rather than to unreached — each
    /// half's floor justifying the other's — which is a second fixpoint the oracle cannot tell
    /// from the first. So there is no floor: <b>a path dies when its budget reaches <c>−K</c></b>,
    /// only a live path reaches anything, and a voxel no live path reaches is this. The
    /// max-over-live-paths rule has one fixpoint again, and it needs no flood and no clamp.
    /// </para>
    /// <para>
    /// The cost, stated: a deficit past <c>K</c> is not counted further. The exact rule would
    /// carry a wide roof's centre at <c>−136</c> and recover it thirty-four layers up; this
    /// carries nothing there, and the layers above it are reached from the anchored top face or
    /// not at all. The saturation point is <c>K / r = 30</c> layers, two short of the analysed
    /// shell's own cap, and decision 8's <c>deficit / K</c> saturates at the same place.
    /// </para>
    /// </remarks>
    public const byte Unreached = 0;

    /// <summary>What <see cref="Decode"/> returns for <see cref="Unreached"/>: below every budget.</summary>
    public const int UnreachedBudget = int.MinValue;

    /// <summary>Air or a fluid: carries nothing, needs nothing.</summary>
    public const byte NotSolid = 255;

    /// <summary>Cells along one face of the chunk: 32 × 32.</summary>
    public const int LayerSize = VoxelConstants.ChunkSize * VoxelConstants.ChunkSize;

    /// <summary>What a dense field is charged: the array plus an estimate for the object.</summary>
    public const int DenseBytes = VoxelConstants.ChunkVolume + 48;

    private readonly byte[]? _values;
    private readonly byte _uniform;

    private ChunkSupport(byte[]? values, byte uniform, int atRisk, int maxDeficit)
    {
        _values = values;
        _uniform = uniform;
        AtRiskCount = atRisk;
        MaxDeficit = maxDeficit;
    }

    /// <summary>A field holding one value everywhere.</summary>
    public static ChunkSupport Uniform(byte value) => new(
        null,
        value,
        IsAtRisk(value) ? VoxelConstants.ChunkVolume : 0,
        Deficit(value));

    /// <summary>A dense field over a filled array, which it takes ownership of.</summary>
    /// <remarks>
    /// The counts are taken here rather than trusted, because a field whose at-risk count
    /// disagrees with its bytes is the kind of thing only a from-scratch pass could ever notice.
    /// </remarks>
    public static ChunkSupport Dense(byte[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Length != VoxelConstants.ChunkVolume)
        {
            throw new ArgumentException($"A field is {VoxelConstants.ChunkVolume} bytes, not {values.Length}.", nameof(values));
        }

        int atRisk = 0;
        int maxDeficit = 0;
        foreach (byte value in values)
        {
            if (!IsAtRisk(value))
            {
                continue;
            }

            atRisk++;
            int deficit = Deficit(value);
            if (deficit > maxDeficit)
            {
                maxDeficit = deficit;
            }
        }

        return new ChunkSupport(values, 0, atRisk, maxDeficit);
    }

    /// <summary>True when one value stands for every voxel and no array is held.</summary>
    public bool IsUniform => _values is null;

    /// <summary>The value a uniform field holds. Meaningless for a dense one.</summary>
    public byte UniformValue => _uniform;

    /// <summary>How many voxels have a negative budget.</summary>
    public int AtRiskCount { get; }

    /// <summary>How far below zero the worst voxel is, 0..K. Decision 8's input.</summary>
    public int MaxDeficit { get; }

    /// <summary>What this field costs in memory.</summary>
    public int EstimatedBytes => _values is null ? 48 : DenseBytes;

    /// <summary>The stored byte at a voxel index.</summary>
    public byte Value(int index) => _values is null ? _uniform : _values[index];

    /// <summary>The stored byte at chunk-local coordinates.</summary>
    public byte Value(int localX, int localY, int localZ) => Value(VoxelPos.Index(localX, localY, localZ));

    /// <summary>The budget at a voxel index, <see cref="UnreachedBudget"/> for a floating voxel, or null for one that is not solid.</summary>
    public int? Budget(int index)
    {
        byte value = Value(index);
        return value == NotSolid ? null : Decode(value);
    }

    /// <summary>Whether the voxel at this index is solid with a negative budget.</summary>
    public bool IsAtRisk(int index) => IsAtRisk(Value(index));

    /// <summary>Whether a stored byte is a solid voxel with a negative budget, or unreached.</summary>
    public static bool IsAtRisk(byte value) => value < Offset || value == Unreached;

    /// <summary>Whether a stored byte is a solid voxel at all.</summary>
    public static bool IsSolid(byte value) => value != NotSolid;

    /// <summary>Whether a stored byte is a solid voxel a live path reaches: a source to its neighbours.</summary>
    public static bool IsReached(byte value) => value is > Unreached and <= Full;

    /// <summary>The deficit a stored byte carries, 0..K: zero for a held voxel, K for unreached.</summary>
    public static int Deficit(byte value) => value == NotSolid ? 0 : Math.Max(0, Offset - value);

    /// <summary>The stored byte for a budget: <see cref="Unreached"/> for anything at or below the floor.</summary>
    public static byte Encode(int budget)
    {
        if (budget <= SupportRule.Floor)
        {
            return Unreached;
        }

        if (budget > SupportRule.MaxBudget)
        {
            throw new ArgumentOutOfRangeException(nameof(budget), budget, "A budget is at most K.");
        }

        return (byte)(budget + Offset);
    }

    /// <summary>The budget a stored byte holds, <see cref="UnreachedBudget"/> for <see cref="Unreached"/>. Throws for <see cref="NotSolid"/>.</summary>
    public static int Decode(byte value)
    {
        if (value == Unreached)
        {
            return UnreachedBudget;
        }

        if (value == NotSolid || value > Full)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Not a budget.");
        }

        return value - Offset;
    }

    /// <summary>
    /// Copies the layer of this chunk's voxels touching <paramref name="face"/> into a 1,024-byte
    /// span, in <see cref="LayerIndex"/> order.
    /// </summary>
    /// <remarks>
    /// Faces are <see cref="VoxelFace"/>'s six, in its order, for the reason
    /// <c>ChunkLight.Conducts</c> shares the table: a layer is a statement about a chunk face and
    /// two tables of the same six directions are two that can disagree. The copy is what a
    /// neighbour's derive reads across the face, and what the join compares to decide whether
    /// that neighbour must run again.
    /// </remarks>
    public void CopyLayer(int face, Span<byte> into)
    {
        if (into.Length != LayerSize)
        {
            throw new ArgumentException($"A layer is {LayerSize} bytes.", nameof(into));
        }

        if (_values is null)
        {
            into.Fill(_uniform);
            return;
        }

        int axis = VoxelFace.Axis[face];
        int along = VoxelFace.Direction[face] > 0 ? VoxelConstants.ChunkSize - 1 : 0;
        Span<int> local = stackalloc int[3];
        (int lo, int hi) = OtherAxes(axis);

        for (int b = 0; b < VoxelConstants.ChunkSize; b++)
        {
            for (int a = 0; a < VoxelConstants.ChunkSize; a++)
            {
                local[axis] = along;
                local[lo] = a;
                local[hi] = b;
                into[LayerIndex(a, b)] = _values[VoxelPos.Index(local[0], local[1], local[2])];
            }
        }
    }

    /// <summary>
    /// Where a voxel on a face layer lands in the 1,024-byte span: <paramref name="a"/> runs along
    /// the lower-numbered of the face's two in-plane axes, <paramref name="b"/> the higher.
    /// </summary>
    public static int LayerIndex(int a, int b) => (b * VoxelConstants.ChunkSize) + a;

    /// <summary>The two axes in a face's plane, lower-numbered first.</summary>
    public static (int Lo, int Hi) OtherAxes(int axis) => axis switch
    {
        0 => (1, 2),
        1 => (0, 2),
        _ => (0, 1),
    };

    /// <summary>
    /// Whether this field marks exactly the voxels <paramref name="other"/> marks at risk — null
    /// standing for a field with nothing at risk, which is what a mesh built with no field read.
    /// </summary>
    /// <remarks>
    /// The strain tint's remesh test (M9b, decision 3): a settled field is worth a remesh only
    /// when the faces it would tint differ from the faces the last mesh tinted. A compare rather
    /// than a hash, because a collision would be a stale tint and 32 KiB compares in ~10 µs. The
    /// counts go first because two fields with different counts cannot agree, and most settles
    /// on a stable roof change nothing.
    /// </remarks>
    public bool SameAtRiskSet(ChunkSupport? other)
    {
        if (other is null)
        {
            return AtRiskCount == 0;
        }

        if (AtRiskCount != other.AtRiskCount)
        {
            return false;
        }

        if (AtRiskCount == 0 || (_values is null && other._values is null))
        {
            return true;
        }

        for (int index = 0; index < VoxelConstants.ChunkVolume; index++)
        {
            if (IsAtRisk(index) != other.IsAtRisk(index))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A copy of the bytes, dense or expanded, for tests and the bench that compare whole fields.</summary>
    public byte[] Snapshot()
    {
        if (_values is not null)
        {
            return (byte[])_values.Clone();
        }

        var expanded = new byte[VoxelConstants.ChunkVolume];
        Array.Fill(expanded, _uniform);
        return expanded;
    }
}
