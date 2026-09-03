namespace Overburden.Core.Hydrology;

/// <summary>
/// How much water a basin holds at a given surface level, and the level a given volume reaches.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is what "solved, not simulated" means.</b> DESIGN's Tier A adds volume to an
/// accumulator and asks for the level; nothing iterates cells, nothing flows. The curve is the
/// sorted elevations of the cells the lake can cover, plus their prefix sums, so
///
/// <code>litres(L) = Σ over cells with h &lt; L of (L − h) × litresPerVoxelOfDepth</code>
///
/// is a binary search and one multiply, and the inverse is the same search run backwards. Exact,
/// monotonic and invertible — which is what lets a player fly away mid-fill and come back to the
/// level the fill would have reached, without the ticks having run.
/// </para>
/// <para>
/// <b>Capped at the saddle by construction.</b> A leaf basin cannot hold more than its lowest rim
/// allows; above that the water is the merge tree's problem, not the curve's. So the cells here
/// are the ones strictly below the saddle, and <see cref="CapacityLitres"/> is the volume at it.
/// </para>
/// <para>
/// Levels are <c>double</c> voxels and volumes are <c>long</c> litres, and that asymmetry is
/// deliberate. Volume is the thing that is stored, added to and compared, so it is an integer of
/// the smallest real unit — M6c-2's argument, where a float inventory would drift and "how much do
/// I carry" would stop having one answer. A level is derived on demand and never accumulated, so a
/// float costs nothing and buys a surface that rises smoothly rather than in 0.25 m steps.
/// </para>
/// </remarks>
public sealed class VolumeCurve
{
    private readonly int[] _heights;
    private readonly long[] _prefix;
    private readonly long[] _litresAtHeight;
    private readonly long _litresPerVoxelOfDepth;

    private VolumeCurve(
        int[] heights, long[] prefix, long[] litresAtHeight, long litresPerVoxelOfDepth,
        int saddleHeight)
    {
        _heights = heights;
        _prefix = prefix;
        _litresAtHeight = litresAtHeight;
        _litresPerVoxelOfDepth = litresPerVoxelOfDepth;
        SaddleHeight = saddleHeight;
    }

    /// <summary>Voxel Y of the deepest point — the level an empty basin sits at.</summary>
    public int FloorHeight => _heights.Length == 0 ? SaddleHeight : _heights[0];

    /// <summary>Voxel Y the lake overtops at.</summary>
    public int SaddleHeight { get; }

    /// <summary>Litres at <see cref="SaddleHeight"/>. Beyond this the basin spills.</summary>
    public long CapacityLitres { get; private set; }

    /// <summary>Cells the lake can cover — those strictly below the saddle.</summary>
    public int CellCount => _heights.Length;

    /// <summary>Builds a curve from the elevations of the cells below <paramref name="saddleHeight"/>.</summary>
    /// <remarks>The span is sorted in place; the caller's order is not preserved.</remarks>
    public static VolumeCurve Build(Span<int> cellHeights, int saddleHeight, long litresPerVoxelOfDepth)
    {
        if (litresPerVoxelOfDepth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(litresPerVoxelOfDepth), litresPerVoxelOfDepth, null);
        }

        foreach (int height in cellHeights)
        {
            if (height >= saddleHeight)
            {
                throw new ArgumentException(
                    "A curve holds only the cells strictly below the saddle. A cell at or above it "
                    + "is never underwater before the basin spills, and including one would report "
                    + "capacity the basin cannot reach.",
                    nameof(cellHeights));
            }
        }

        cellHeights.Sort();
        int[] heights = cellHeights.ToArray();

        // prefix[i] is the sum of the first i heights, so the volume formula needs no loop.
        var prefix = new long[heights.Length + 1];
        for (int i = 0; i < heights.Length; i++)
        {
            prefix[i + 1] = prefix[i] + heights[i];
        }

        var curve = new VolumeCurve(
            heights, prefix, new long[heights.Length], litresPerVoxelOfDepth, saddleHeight);

        // The volume at each cell's own elevation, which is what LevelAt binary-searches. Filled
        // through the same accessor the public API uses rather than by a second copy of the
        // formula — a divergent copy of one definition is the failure this project has caught
        // most often, and it is what `IsCaveAt` against `GenerateTerrain` exists to prevent.
        for (int i = 0; i < heights.Length; i++)
        {
            curve._litresAtHeight[i] = curve.LitresAt(heights[i]);
        }

        curve.CapacityLitres = curve.LitresAt(saddleHeight);
        return curve;
    }

    /// <summary>Litres held when the surface sits at <paramref name="levelVoxels"/>.</summary>
    public long LitresAt(double levelVoxels)
    {
        if (_heights.Length == 0)
        {
            return 0;
        }

        double level = Math.Clamp(levelVoxels, _heights[0], SaddleHeight);

        // Cells at exactly the level contribute zero depth, so counting them in or out gives the
        // same answer — which is why an upper bound is safe here and duplicate elevations need no
        // special case.
        int covered = UpperBound(_heights, level);
        double voxelDepth = (covered * level) - _prefix[covered];
        return (long)Math.Round(voxelDepth * _litresPerVoxelOfDepth, MidpointRounding.AwayFromZero);
    }

    /// <summary>The surface level <paramref name="litres"/> reaches, clamped at the saddle.</summary>
    public double LevelAt(long litres)
    {
        if (_heights.Length == 0 || litres <= 0)
        {
            return FloorHeight;
        }

        if (litres >= CapacityLitres)
        {
            return SaddleHeight;
        }

        // The last cell elevation the water has fully risen past. Everything below it is covered,
        // and the level sits somewhere between it and the next elevation up.
        int index = LastAtOrBelow(_litresAtHeight, litres);
        int covered = UpperBound(_heights, _heights[index]);

        double voxelDepth = (double)litres / _litresPerVoxelOfDepth;
        double level = (voxelDepth + _prefix[covered]) / covered;
        return Math.Clamp(level, _heights[0], SaddleHeight);
    }

    /// <summary>Number of entries less than or equal to <paramref name="value"/>.</summary>
    private static int UpperBound(int[] sorted, double value)
    {
        int low = 0;
        int high = sorted.Length;
        while (low < high)
        {
            int mid = (low + high) >> 1;
            if (sorted[mid] <= value)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }

    /// <summary>Index of the last entry at or below <paramref name="value"/>; 0 if none is.</summary>
    private static int LastAtOrBelow(long[] sorted, long value)
    {
        int low = 0;
        int high = sorted.Length - 1;
        int best = 0;
        while (low <= high)
        {
            int mid = (low + high) >> 1;
            if (sorted[mid] <= value)
            {
                best = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return best;
    }
}
