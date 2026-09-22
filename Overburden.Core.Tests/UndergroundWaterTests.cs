using Overburden.Core.Fluid;
using Overburden.Core.Lighting;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// M10d-1: the mine's share of <c>held</c> — a panel wholly under the seed surface over its
/// footprint counts, a panel that straddles it does not, and the memo is capped.
/// </summary>
public class UndergroundWaterTests
{
    /// <summary>A flat seed surface at one height, with an optional dip of one column.</summary>
    private sealed class Seed(int height, (int X, int Z, int Y)? dip = null) : ISurfaceHeights
    {
        public int Reads { get; private set; }

        public int SurfaceHeightAt(int worldX, int worldZ)
        {
            Reads++;
            return dip is { } d && d.X == worldX && d.Z == worldZ ? d.Y : height;
        }
    }

    [Fact]
    public void APanelWhollyBelowTheSeedSurfaceCountsAndOneAboveDoesNot()
    {
        // Seed at y = 40: chunk Y 0 (top 31) is underground, chunk Y 1 (top 63) is not, and the
        // chunk whose top is exactly the seed height (a seed at 31) is not either — strictly below.
        var grid = new FluidGrid();
        grid.Fill(new VoxelPos(5, 10, 5), 3_000);
        grid.Fill(new VoxelPos(6, 10, 5), 4_000);
        grid.Fill(new VoxelPos(5, 40, 5), 5_000);
        var underground = new UndergroundWater(new Seed(40));

        (long millilitres, int panels) = underground.Measure(grid);

        Assert.Equal(7_000, millilitres);
        Assert.Equal(1, panels);
        Assert.False(new UndergroundWater(new Seed(31)).IsUnderground(new ChunkPos(0, 0, 0)));
        Assert.True(new UndergroundWater(new Seed(32)).IsUnderground(new ChunkPos(0, 0, 0)));
    }

    [Fact]
    public void APanelStraddlingTheSurfaceReadsAsAbove()
    {
        // A hollow one column deep in a chunk's footprint takes the seed to 20 there; the chunk
        // at Y 0 has its top at 31 and is no longer wholly below — the stated cost.
        var grid = new FluidGrid();
        grid.Fill(new VoxelPos(5, 10, 5), 3_000);
        var straddling = new UndergroundWater(new Seed(40, dip: (17, 9, 20)));

        Assert.Equal((0L, 0), straddling.Measure(grid));
        Assert.Equal((3_000L, 1), new UndergroundWater(new Seed(40, dip: (17, 9, 32))).Measure(grid));
    }

    [Fact]
    public void TheFootprintIsReadOnceAndTheMemoIsCapped()
    {
        var seed = new Seed(40);
        var underground = new UndergroundWater(seed);
        var grid = new FluidGrid();
        grid.Fill(new VoxelPos(5, 10, 5), 1_000);
        grid.Fill(new VoxelPos(5, 42, 5), 1_000);

        underground.Measure(grid);
        underground.Measure(grid);

        // Two chunks in one column: 32 × 32 reads, once.
        Assert.Equal(VoxelConstants.ChunkSize * VoxelConstants.ChunkSize, seed.Reads);
        Assert.Equal(1, underground.MemoisedColumns);

        for (int i = 1; i <= UndergroundWater.MaxColumns; i++)
        {
            underground.IsUnderground(new ChunkPos(i, 0, 0));
        }

        Assert.Equal(1, underground.MemoClears);
        Assert.True(underground.MemoisedColumns <= UndergroundWater.MaxColumns);
    }
}
