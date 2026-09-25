using Overburden.Core.Fluid;
using Overburden.Core.Lighting;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// Matthew's water grid (docs/POND-PLAN.md, decision 10): per voxel column, where a lake stands in
/// cells the player has built in — the wall is the shore on both sides, whatever its thickness.
/// </summary>
public sealed class WaterGridTests
{
    private const int N = VoxelConstants.ChunkSize;
    private const int Cell = 8;
    private const int Surface = 40;          // seed plane 41
    private const double Pond = 45.5;
    private const int WallTop = 47;          // open height of a wall column

    /// <summary>Flat sky columns created on demand, with columns a test can leave unloaded.</summary>
    private sealed class World(ISurfaceHeights? seed = null) : ISkyColumns
    {
        private readonly ISurfaceHeights _seed = seed ?? new FlatSurface(Surface);
        private readonly Dictionary<(int, int), SkyColumn> _columns = [];
        public readonly HashSet<(int, int)> Unloaded = [];

        public SkyColumn? ColumnAt(int chunkX, int chunkZ)
        {
            if (Unloaded.Contains((chunkX, chunkZ))) return null;
            if (!_columns.TryGetValue((chunkX, chunkZ), out SkyColumn? column))
            {
                column = new SkyColumn(chunkX, chunkZ);
                column.Seed(_seed);
                _columns[(chunkX, chunkZ)] = column;
            }

            return column;
        }

        /// <summary>Sets a world voxel column's open height.</summary>
        public void Top(int worldX, int worldZ, int open)
        {
            int cx = Floor(worldX, N), cz = Floor(worldZ, N);
            ColumnAt(cx, cz)!.SetOpenAboveY(worldX - (cx * N), worldZ - (cz * N), open);
        }
    }

    private sealed class Table(Func<int, int, double?> at) : IWaterTable
    {
        public double? TableAt(int sampleX, int sampleZ) => at(sampleX, sampleZ);
    }

    private static int Floor(int value, int divisor) =>
        value >= 0 ? value / divisor : -((-value + divisor - 1) / divisor);

    private static double? Level(WaterColumns grid, int localX, int localZ) =>
        grid.TryGetLevel(localX, localZ, out double level) ? level : null;

    [Fact]
    public void NothingPlacedInTheChunkColumnBuildsNoGrid()
    {
        var world = new World();
        world.Top(40, 5, Surface + 5);   // a wall next door does not count
        Assert.Null(WaterGrid.Build(0, 0, world, new Table((_, _) => Pond), Cell));
    }

    [Fact]
    public void AnUnbuiltColumnAnswersItsSamplesTableExactly()
    {
        var world = new World();
        world.Top(30, 30, WallTop);      // one placed column, in cell (3, 3)
        var table = new Table((sx, sz) => sx == 0 ? null : 40.0 + sx + (0.25 * sz));

        WaterColumns grid = WaterGrid.Build(0, 0, world, table, Cell)!;

        for (int lz = 0; lz < N; lz++)
        {
            for (int lx = 0; lx < N; lx++)
            {
                if (lx / Cell == 3 && lz / Cell == 3) continue;
                Assert.Equal(table.TableAt(lx / Cell, lz / Cell), Level(grid, lx, lz));
            }
        }
    }

    /// <summary>
    /// A wall one voxel thick at world x = baseX + 12, the middle of the cell spanning
    /// baseX + 8 … baseX + 15, running the whole padded window so it has no end the flood can go
    /// round. The pond's table covers the samples west of it; the east has no lake.
    /// </summary>
    private static (World World, Table Table) WallAcrossACell(int baseX, int baseZ)
    {
        var world = new World();
        for (int z = baseZ - Cell; z < baseZ + N + Cell; z++)
        {
            world.Top(baseX + 12, z, WallTop);
        }

        int wallSample = Floor(baseX, Cell) + 1;
        return (world, new Table((sx, _) => sx <= wallSample ? Pond : null));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-2, -3)]
    public void AOneVoxelWallAcrossACellIsLakeInsideAndDryOutside(int chunkX, int chunkZ)
    {
        int baseX = chunkX * N, baseZ = chunkZ * N;
        (World world, Table table) = WallAcrossACell(baseX, baseZ);

        WaterColumns grid = WaterGrid.Build(chunkX, chunkZ, world, table, Cell)!;

        for (int lz = 0; lz < N; lz++)
        {
            for (int lx = 8; lx <= 11; lx++) Assert.Equal(Pond, Level(grid, lx, lz));
            Assert.Null(Level(grid, 12, lz));
            for (int lx = 13; lx <= 15; lx++) Assert.Null(Level(grid, lx, lz));
        }

        Assert.Equal(4 * N, grid.BuiltColumnsReached);
    }

    [Fact]
    public void AGapAtTheWaterLineLetsTheFloodThroughToTheOutsideStrip()
    {
        (World world, Table table) = WallAcrossACell(0, 0);
        world.Top(12, 10, Surface + 1);  // the wall taken down to the ground at one z

        WaterColumns grid = WaterGrid.Build(0, 0, world, table, Cell)!;

        Assert.Equal(Pond, Level(grid, 12, 10));
        Assert.Equal(Pond, Level(grid, 15, 0));
        Assert.Equal(Pond, Level(grid, 15, 31));
    }

    [Fact]
    public void AWallWithAnEndIsFloodedRoundItThroughThePad()
    {
        // The wall stops at z = 20: the flood goes round its end and the outside strip is lake,
        // which is where the water would go.
        var world = new World();
        for (int z = -Cell; z <= 20; z++) world.Top(12, z, WallTop);
        var table = new Table((sx, _) => sx <= 1 ? Pond : null);

        WaterColumns grid = WaterGrid.Build(0, 0, world, table, Cell)!;

        Assert.Equal(Pond, Level(grid, 15, 0));
        Assert.Null(Level(grid, 12, 5));
    }

    [Fact]
    public void AFullPondStopsAtItsWallTopExactly()
    {
        // A full pond's level is its wall's top plane: the flood enters only under the level, so
        // it does not carry the lake over the wall into the outside strip.
        (World world, _) = WallAcrossACell(0, 0);
        var full = new Table((sx, _) => sx <= 1 ? WallTop : null);

        WaterColumns grid = WaterGrid.Build(0, 0, world, full, Cell)!;

        Assert.Equal((double)WallTop, Level(grid, 11, 4));
        Assert.Null(Level(grid, 12, 4));
        Assert.Null(Level(grid, 14, 4));
    }

    [Fact]
    public void AWallOnAChunkSeamFindsItsSeedsInTheNeighbourAndReadsDryWhenItIsUnloaded()
    {
        // The wall at world x = 34 is in chunk column 1's first cell; the pond's open water is in
        // chunk column 0, which only the pad reaches.
        var world = new World();
        for (int z = -Cell; z < N + Cell; z++) world.Top(34, z, WallTop);
        var table = new Table((sx, _) => sx <= 4 ? Pond : null);

        WaterColumns grid = WaterGrid.Build(1, 0, world, table, Cell)!;
        Assert.Equal(Pond, Level(grid, 0, 7));
        Assert.Equal(Pond, Level(grid, 1, 7));
        Assert.Null(Level(grid, 3, 7));

        // The whole west side of the pad, diagonals too: with (0, 0) alone gone, the pond's water
        // still reaches the strip from (0, -1) and (0, 1), which is the truthful answer.
        world.Unloaded.Add((0, -1));
        world.Unloaded.Add((0, 0));
        world.Unloaded.Add((0, 1));
        WaterColumns cut = WaterGrid.Build(1, 0, world, table, Cell)!;
        Assert.Null(Level(cut, 0, 7));
        Assert.Null(Level(cut, 1, 7));
    }

    [Fact]
    public void TwoLakesMeetingInABuiltCellTakeTheLower()
    {
        // Cell (1, 0) is built in by one block under both lakes; the lake west stands at 45.5,
        // the lake east at 44. Both reach it, and the lower keeps every column.
        var world = new World();
        world.Top(12, 3, Surface + 2);
        var table = new Table((sx, _) => sx == 0 ? Pond : sx == 2 ? 44.0 : null);

        WaterColumns grid = WaterGrid.Build(0, 0, world, table, Cell)!;

        for (int lz = 0; lz < Cell; lz++)
        {
            for (int lx = 8; lx <= 15; lx++) Assert.Equal(44.0, Level(grid, lx, lz));
        }
    }

    [Fact]
    public void ABlockUnderTheWaterHasWaterOverItAndOneAboveItDoesNot()
    {
        var world = new World();
        world.Top(12, 12, Surface + 3);  // under 45.5
        world.Top(13, 12, 46);           // its top plane above the level
        var table = new Table((_, _) => Pond);

        WaterColumns grid = WaterGrid.Build(0, 0, world, table, Cell)!;

        Assert.Equal(Pond, Level(grid, 12, 12));
        Assert.Null(Level(grid, 13, 12));
        Assert.Equal(Pond, Level(grid, 14, 12));
    }

    [Fact]
    public void ANaturalRidgeExactlyAtTheLevelPassesNoWaterIntoABuiltCellBehindIt()
    {
        // West of x = 8 the ground is at 40 under a lake at 45.0; x 8..15 is a natural ridge
        // whose plane is exactly 45; behind it a built cell at the low ground. The ridge's
        // columns take their table as the band does, but they are not under the water, so they
        // seed nothing: the lake does not reach past a rim it only touches.
        var ridge = new AnalyticSurface((x, _) => x >= 8 && x < 16 ? 44 : Surface);
        var world = new World(ridge);
        world.Top(20, 4, Surface + 2);
        var table = new Table((sx, _) => sx <= 1 ? 45.0 : null);

        WaterColumns grid = WaterGrid.Build(0, 0, world, table, Cell)!;

        Assert.Equal(45.0, Level(grid, 10, 4));
        for (int lx = 16; lx < 24; lx++) Assert.Null(Level(grid, lx, 4));
    }
}
