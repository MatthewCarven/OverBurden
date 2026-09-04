using Overburden.Core.Hydrology;
using Overburden.Core.Lighting;

namespace Overburden.Core.Tests;

/// <summary>Terrain stated as a literal grid of sample heights, indexed in absolute samples.</summary>
/// <remarks>
/// <para>
/// Absolute rather than window-relative, so terrain analysed through two different window origins
/// is literally the same terrain. That is what the pit-identity test needs, and stating it any
/// other way would make that test pass for the wrong reason.
/// </para>
/// <para>
/// <b>A grid entry is the topmost solid voxel, exactly as <c>ISurfaceHeights</c> reports it, and
/// the field samples the plane above.</b> So a pit written as 2 has a floor plane at 3, a ridge
/// written as 5 is a ridge plane at 6, and every pit, saddle, spill height and level a test reads
/// back is one higher than the grid. M8b-2a's convention; the helper deliberately does not hide it.
/// </para>
/// </remarks>
internal sealed class SampleGrid(int[,] samples, int outside) : ISurfaceHeights
{
    public int SurfaceHeightAt(int worldX, int worldZ)
    {
        int cell = HeightField.DefaultCellVoxels;
        if (worldX % cell != 0 || worldZ % cell != 0)
        {
            throw new InvalidOperationException("the analysis sampled off its own lattice");
        }

        int sx = worldX / cell;
        int sz = worldZ / cell;
        bool inside = sx >= 0 && sz >= 0
            && sz < samples.GetLength(0) && sx < samples.GetLength(1);
        return inside ? samples[sz, sx] : outside;
    }
}

/// <summary>Terrain from a function, for shapes a literal grid cannot state.</summary>
internal sealed class AnalyticSurface(Func<int, int, int> height) : ISurfaceHeights
{
    public int SurfaceHeightAt(int worldX, int worldZ) => height(worldX, worldZ);
}
