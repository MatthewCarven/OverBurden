using Overburden.Core.Voxels;

namespace Overburden.Core.Jobs;

/// <summary>
/// Where the viewers are. Written by the sim thread, read by workers without a lock.
/// </summary>
/// <remarks>
/// <para>
/// Priority is a heuristic. A job scheduled from a slightly stale viewer position runs a
/// little sooner or later than ideal, and still runs — so paying for a lock to make a
/// heuristic exact is waste. godot_voxel reaches the same conclusion and says so in
/// <c>priority_dependency.h</c>.
/// </para>
/// <para>
/// They make the unlocked read safe with a pre-sized array that is never resized plus an
/// atomic count, which leaves individual position reads able to tear. We can do better for
/// free: reference assignment is atomic in .NET, so publishing a whole new array by
/// swapping one reference gives readers a consistent snapshot with no tearing at all.
/// Viewers move at most once per frame and there are a handful of them, so the allocation
/// is irrelevant.
/// </para>
/// <para>
/// A reader may observe the previous snapshot. That is the accepted staleness, and it is
/// bounded by one update.
/// </para>
/// </remarks>
public sealed class ViewerSet
{
    private static readonly ChunkPos[] Empty = [];

    private ChunkPos[] _snapshot = Empty;

    /// <summary>Number of viewers in the current snapshot.</summary>
    public int Count => Volatile.Read(ref _snapshot).Length;

    /// <summary>
    /// Replaces the viewer positions. Callers hand over a copy; the array is not retained
    /// by reference, so the caller may reuse its buffer.
    /// </summary>
    public void Update(ReadOnlySpan<ChunkPos> positions)
    {
        Volatile.Write(ref _snapshot, positions.IsEmpty ? Empty : positions.ToArray());
    }

    /// <summary>Convenience for the common single-viewer case.</summary>
    public void UpdateSingle(ChunkPos position)
    {
        Volatile.Write(ref _snapshot, [position]);
    }

    /// <summary>
    /// Chebyshev distance in chunks to the closest viewer, or <see cref="int.MaxValue"/>
    /// when there are no viewers at all.
    /// </summary>
    /// <remarks>
    /// No viewers means nothing is worth prioritising rather than everything being urgent,
    /// so the answer is "infinitely far" and not zero. Getting this backwards would make an
    /// empty world schedule its whole queue at maximum priority.
    /// </remarks>
    public int NearestChebyshevDistance(ChunkPos position)
    {
        // One volatile read, then work off the local. Re-reading the field per iteration
        // would let the snapshot change underneath the loop.
        ChunkPos[] viewers = Volatile.Read(ref _snapshot);
        if (viewers.Length == 0)
        {
            return int.MaxValue;
        }

        int nearest = int.MaxValue;
        foreach (ChunkPos viewer in viewers)
        {
            int distance = position.ChebyshevDistanceTo(viewer);
            if (distance < nearest)
            {
                nearest = distance;
            }
        }

        return nearest;
    }
}
