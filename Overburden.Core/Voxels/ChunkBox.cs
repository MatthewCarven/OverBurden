namespace Overburden.Core.Voxels;

/// <summary>
/// A half-open box of chunk positions: <see cref="Min"/> is included, <see cref="Max"/> is
/// not.
/// </summary>
/// <remarks>
/// <para>
/// Half-open rather than inclusive-both-ends because every operation here is arithmetic on
/// the bounds — size is <c>Max - Min</c>, an empty box is <c>Max &lt;= Min</c>, and
/// <see cref="Difference"/> slices without a single off-by-one. Inclusive maxima put a
/// <c>+1</c> or <c>-1</c> in each of those and one of them is always wrong.
/// </para>
/// <para>
/// <see cref="Max"/> is a <see cref="ChunkPos"/> for convenience and type safety, but it
/// names the first chunk <em>past</em> the box rather than a chunk inside it.
/// </para>
/// </remarks>
public readonly record struct ChunkBox(ChunkPos Min, ChunkPos Max)
{
    public static readonly ChunkBox Empty = new(ChunkPos.Zero, ChunkPos.Zero);

    /// <summary>
    /// The box of chunks within <paramref name="horizontalRadius"/> on X and Z and
    /// <paramref name="verticalRadius"/> on Y of <paramref name="centre"/>, inclusive.
    /// </summary>
    /// <remarks>
    /// <b>Vertical extent is a separate radius, not a ratio of the horizontal one.</b> A
    /// mining game's content is below the player rather than around them, so the useful
    /// shape is a tall narrow column — the opposite of godot_voxel's default, which shrinks
    /// vertically to avoid loading sky. Collapsing these two into one "view distance"
    /// scalar is a change that later reaches every call site; see docs/M2-PLAN.md.
    /// </remarks>
    public static ChunkBox Around(ChunkPos centre, int horizontalRadius, int verticalRadius)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(horizontalRadius);
        ArgumentOutOfRangeException.ThrowIfNegative(verticalRadius);

        return new ChunkBox(
            new ChunkPos(
                centre.X - horizontalRadius,
                centre.Y - verticalRadius,
                centre.Z - horizontalRadius),
            new ChunkPos(
                centre.X + horizontalRadius + 1,
                centre.Y + verticalRadius + 1,
                centre.Z + horizontalRadius + 1));
    }

    /// <summary>Builds a box from raw half-open bounds.</summary>
    public static ChunkBox FromBounds(int minX, int minY, int minZ, int maxX, int maxY, int maxZ) =>
        new(new ChunkPos(minX, minY, minZ), new ChunkPos(maxX, maxY, maxZ));

    /// <summary>True when the box contains no chunks at all.</summary>
    /// <remarks>
    /// Any axis being degenerate empties the whole box, so a box may be empty without
    /// equalling <see cref="Empty"/>. Compare against this, never against
    /// <see cref="Empty"/>.
    /// </remarks>
    public bool IsEmpty => Max.X <= Min.X || Max.Y <= Min.Y || Max.Z <= Min.Z;

    public int SizeX => Math.Max(0, Max.X - Min.X);

    public int SizeY => Math.Max(0, Max.Y - Min.Y);

    public int SizeZ => Math.Max(0, Max.Z - Min.Z);

    /// <summary>How many chunks the box contains. <c>long</c> because R³ overflows early.</summary>
    public long Volume => (long)SizeX * SizeY * SizeZ;

    /// <summary>
    /// The box grown by <paramref name="chunks"/> on every side. An empty box stays empty.
    /// </summary>
    public ChunkBox Expand(int chunks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(chunks);

        if (IsEmpty || chunks == 0)
        {
            return this;
        }

        return new ChunkBox(
            new ChunkPos(Min.X - chunks, Min.Y - chunks, Min.Z - chunks),
            new ChunkPos(Max.X + chunks, Max.Y + chunks, Max.Z + chunks));
    }

    public bool Contains(ChunkPos position) =>
        position.X >= Min.X && position.X < Max.X &&
        position.Y >= Min.Y && position.Y < Max.Y &&
        position.Z >= Min.Z && position.Z < Max.Z;

    /// <summary>The overlap of two boxes, or an empty box when they do not touch.</summary>
    public ChunkBox Intersect(ChunkBox other)
    {
        var min = new ChunkPos(
            Math.Max(Min.X, other.Min.X),
            Math.Max(Min.Y, other.Min.Y),
            Math.Max(Min.Z, other.Min.Z));
        var max = new ChunkPos(
            Math.Min(Max.X, other.Max.X),
            Math.Min(Max.Y, other.Max.Y),
            Math.Min(Max.Z, other.Max.Z));

        var result = new ChunkBox(min, max);
        return result.IsEmpty ? Empty : result;
    }

    /// <summary>
    /// Visits every chunk position in the box, in the same Z-major, X, Y-contiguous order
    /// as chunk storage.
    /// </summary>
    public void ForEach(Action<ChunkPos> visit)
    {
        for (int z = Min.Z; z < Max.Z; z++)
        for (int x = Min.X; x < Max.X; x++)
        for (int y = Min.Y; y < Max.Y; y++)
        {
            visit(new ChunkPos(x, y, z));
        }
    }

    /// <summary>
    /// Decomposes "this box, minus <paramref name="other"/>" into at most six disjoint
    /// sub-boxes and hands each to <paramref name="visit"/>. Emits nothing when
    /// <paramref name="other"/> covers this box entirely.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is what makes streaming O(R²) per boundary crossing instead of O(R³).</b>
    /// When a viewer steps one chunk sideways, only the shell that entered and the shell
    /// that left have changed; re-walking the whole view volume to find them is the naive
    /// version. At a 16-chunk radius that is roughly a thousand chunks touched per crossing
    /// rather than thirty-five thousand.
    /// </para>
    /// <para>
    /// The decomposition slices axis by axis: peel the X slabs outside
    /// <paramref name="other"/> at full Y and Z extent, then peel the Y slabs from what
    /// remains, then the Z slabs from what remains of that. Each slice is taken at the
    /// extent left over from the previous axis, which is what keeps the six pieces
    /// disjoint.
    /// </para>
    /// <para>
    /// The disjoint case is handled up front by intersecting first. That is not just a fast
    /// path — it is what lets the slicing arithmetic assume the overlap lies inside this
    /// box on every axis, so no clamping is needed anywhere below. Written the other way
    /// round, a non-overlapping box emits slabs with inverted bounds.
    /// </para>
    /// </remarks>
    public void Difference(ChunkBox other, Action<ChunkBox> visit)
    {
        if (IsEmpty)
        {
            return;
        }

        ChunkBox overlap = Intersect(other);
        if (overlap.IsEmpty)
        {
            visit(this);
            return;
        }

        // X slabs, at full Y and Z.
        if (overlap.Min.X > Min.X)
        {
            visit(FromBounds(Min.X, Min.Y, Min.Z, overlap.Min.X, Max.Y, Max.Z));
        }

        if (overlap.Max.X < Max.X)
        {
            visit(FromBounds(overlap.Max.X, Min.Y, Min.Z, Max.X, Max.Y, Max.Z));
        }

        // Y slabs, narrowed to the X range the slabs above did not already take.
        if (overlap.Min.Y > Min.Y)
        {
            visit(FromBounds(overlap.Min.X, Min.Y, Min.Z, overlap.Max.X, overlap.Min.Y, Max.Z));
        }

        if (overlap.Max.Y < Max.Y)
        {
            visit(FromBounds(overlap.Min.X, overlap.Max.Y, Min.Z, overlap.Max.X, Max.Y, Max.Z));
        }

        // Z slabs, narrowed on both X and Y for the same reason.
        if (overlap.Min.Z > Min.Z)
        {
            visit(FromBounds(
                overlap.Min.X, overlap.Min.Y, Min.Z, overlap.Max.X, overlap.Max.Y, overlap.Min.Z));
        }

        if (overlap.Max.Z < Max.Z)
        {
            visit(FromBounds(
                overlap.Min.X, overlap.Min.Y, overlap.Max.Z, overlap.Max.X, overlap.Max.Y, Max.Z));
        }
    }

    /// <summary>
    /// Visits every chunk that is in this box and not in <paramref name="other"/>.
    /// </summary>
    public void ForEachDifference(ChunkBox other, Action<ChunkPos> visit)
    {
        Difference(other, box => box.ForEach(visit));
    }

    public override string ToString() =>
        IsEmpty ? "ChunkBox(empty)" : $"ChunkBox({Min.X},{Min.Y},{Min.Z} .. {Max.X},{Max.Y},{Max.Z})";
}
