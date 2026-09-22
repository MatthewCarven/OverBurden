using Overburden.Core.Fluid;
using Overburden.Core.Hydrology;
using Overburden.Core.Lighting;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// Rain on the player's material lands on it (2026-09-22): the accrual per chunk column, the
/// placed set against the sky column's revision, the cap, the forfeit, and the basin that pays.
/// </summary>
public sealed class RainOnPlacedTests
{
    private const int N = VoxelConstants.ChunkSize;
    private const long Cell = RainOnPlaced.NanometreColumnsPerCell;
    private const int Surface = 40;

    /// <summary>Sky columns by chunk column, seeded flat at <see cref="Surface"/>, with tops a test moves.</summary>
    private sealed class Columns : ISkyColumns
    {
        private readonly Dictionary<(int, int), SkyColumn> _columns = [];

        public SkyColumn Get(int chunkX, int chunkZ)
        {
            if (!_columns.TryGetValue((chunkX, chunkZ), out SkyColumn? column))
            {
                column = new SkyColumn(chunkX, chunkZ);
                column.Seed(new FlatSurface(Surface));
                _columns[(chunkX, chunkZ)] = column;
            }

            return column;
        }

        public void Remove(int chunkX, int chunkZ) => _columns.Remove((chunkX, chunkZ));

        public SkyColumn? ColumnAt(int chunkX, int chunkZ) =>
            _columns.TryGetValue((chunkX, chunkZ), out SkyColumn? column) ? column : null;
    }

    /// <summary>A floor placed on the ground over a square of one chunk column: the top is one voxel above the seed, the open height two.</summary>
    private static void Floor(SkyColumn column, int from, int to, int height = 1)
    {
        for (int lz = from; lz < to; lz++)
        {
            for (int lx = from; lx < to; lx++)
            {
                column.SetOpenAboveY(lx, lz, Surface + 1 + height);
            }
        }
    }

    [Fact]
    public void AFlooredSquareGetsItsRainToTheCell()
    {
        // 100 placed columns over a 10 × 10 floor, three cells' worth of depth each: 300 cells,
        // every one landing on the floor's top, none anywhere else, nothing left over.
        var columns = new Columns();
        Floor(columns.Get(0, 0), 4, 14);
        var rain = new RainOnPlaced(columns, new Random(1));
        var cells = new List<VoxelPos>();

        int yielded = rain.Advance(3 * Cell, new ChunkPos(0, 1, 0), rings: 1, verticalRings: 2, cells, capacity: 10_000);

        Assert.Equal(300, yielded);
        Assert.Equal(300, cells.Count);
        Assert.Equal(100, rain.PlacedColumns);
        Assert.Equal(1, rain.ChunkColumnsWithPlaced);
        Assert.Equal(0L, rain.Backlog);
        Assert.Equal(0L, rain.Forfeited);
        Assert.All(cells, c =>
        {
            Assert.Equal(Surface + 2, c.Y);
            Assert.InRange(c.X, 4, 13);
            Assert.InRange(c.Z, 4, 13);
        });

        // Spread over the floor, not one column: the picks are random over the placed set.
        Assert.True(cells.Select(c => (c.X, c.Z)).Distinct().Count() > 50, "the cells landed on too few columns to be a random spread");
    }

    [Fact]
    public void AnHourInOneStepEqualsAnHourInSteps()
    {
        // The accrual is exact integers per chunk column, so the cells an hour yields do not
        // depend on how the hour was cut — the oracle the whole tier rests on, one layer out.
        const long hour = 3 * Cell + 123_456_789;
        var one = new Columns();
        Floor(one.Get(0, 0), 0, 20);
        Floor(one.Get(1, 0), 5, 9);
        var oneShot = new RainOnPlaced(one, new Random(7));
        var oneCells = new List<VoxelPos>();
        oneShot.Advance(hour, new ChunkPos(0, 1, 0), 2, 2, oneCells, 100_000);

        var many = new Columns();
        Floor(many.Get(0, 0), 0, 20);
        Floor(many.Get(1, 0), 5, 9);
        var stepwise = new RainOnPlaced(many, new Random(7));
        var manyCells = new List<VoxelPos>();
        long stepped = 0;
        for (int step = 0; step < 60; step++)
        {
            long depth = step == 59 ? hour - stepped : hour / 60;
            stepped += depth;
            stepwise.Advance(depth, new ChunkPos(0, 1, 0), 2, 2, manyCells, 100_000);
        }

        Assert.Equal(hour, stepped);
        Assert.Equal(oneCells.Count, manyCells.Count);
        Assert.Equal(oneShot.Backlog, stepwise.Backlog);
        Assert.True(oneCells.Count > 400, $"only {oneCells.Count} cells fell");
    }

    [Fact]
    public void ADugColumnAndTheSeedGetNothing()
    {
        // The seed's own top is not the player's, and a dug column is nature's bowl — Tier A
        // delivers there through the portals. Only a top above the seed is this rule's.
        var columns = new Columns();
        SkyColumn column = columns.Get(0, 0);
        for (int lz = 0; lz < N; lz++)
        {
            for (int lx = 0; lx < N; lx++)
            {
                column.SetOpenAboveY(lx, lz, lx < 16 ? Surface - 5 : Surface + 1);
            }
        }

        var rain = new RainOnPlaced(columns, new Random(1));
        var cells = new List<VoxelPos>();
        Assert.Equal(0, rain.Advance(10 * Cell, new ChunkPos(0, 1, 0), 1, 2, cells, 10_000));
        Assert.Equal(0, rain.PlacedColumns);
        Assert.Empty(cells);
    }

    [Fact]
    public void AWallTopLandsOnTheWall()
    {
        // One column built up nine voxels: its rain lands on the wall's top and is the
        // automaton's to shed to either side.
        var columns = new Columns();
        columns.Get(0, 0).SetOpenAboveY(7, 9, Surface + 10);
        var rain = new RainOnPlaced(columns, new Random(1));
        var cells = new List<VoxelPos>();
        Assert.Equal(2, rain.Advance(2 * Cell, new ChunkPos(0, 1, 0), 1, 2, cells, 10_000));
        Assert.All(cells, c => Assert.Equal(new VoxelPos(7, Surface + 10, 9), c));
    }

    [Fact]
    public void TheCapLeavesABacklogThatIsDeliveredNext()
    {
        var columns = new Columns();
        Floor(columns.Get(0, 0), 4, 14);
        var rain = new RainOnPlaced(columns, new Random(1));
        var cells = new List<VoxelPos>();

        Assert.Equal(5, rain.Advance(3 * Cell, new ChunkPos(0, 1, 0), 1, 2, cells, capacity: 5));
        Assert.Equal(295L, rain.Backlog);

        cells.Clear();
        Assert.Equal(64, rain.Advance(0, new ChunkPos(0, 1, 0), 1, 2, cells, capacity: 64));
        Assert.Equal(231L, rain.Backlog);
        Assert.Equal(0L, rain.Forfeited);
    }

    [Fact]
    public void RainOverAColumnOutsideTheVerticalBoxWaitsAndThenForfeits()
    {
        // A tower's top far above the loaded box: nothing can be put there, so the rain waits —
        // and past the forfeit cap it goes to the basin under it, as it did before this rule.
        var columns = new Columns();
        columns.Get(0, 0).SetOpenAboveY(3, 3, Surface + 400);
        var rain = new RainOnPlaced(columns, new Random(1));
        var cells = new List<VoxelPos>();

        Assert.Equal(0, rain.Advance(10 * Cell, new ChunkPos(0, 1, 0), 1, verticalRings: 1, cells, 10_000));
        Assert.Equal(10L, rain.Backlog);
        Assert.Equal(0L, rain.Forfeited);

        rain.Advance(100 * Cell, new ChunkPos(0, 1, 0), 1, 1, cells, 10_000);
        Assert.Empty(cells);
        Assert.Equal(RainOnPlaced.ForfeitCells, rain.Backlog);
        Assert.Equal(110L - RainOnPlaced.ForfeitCells, rain.Forfeited);

        // The box reaches it: the waiting cells land, on the tower.
        Assert.Equal(RainOnPlaced.ForfeitCells, rain.Advance(0, new ChunkPos(0, 13, 0), 1, 1, cells, 10_000));
        Assert.All(cells, c => Assert.Equal(new VoxelPos(3, Surface + 400, 3), c));
        Assert.Equal(0L, rain.Backlog);
    }

    [Fact]
    public void AnEditRescansAndAColumnThatLeavesTheBoxIsDropped()
    {
        // The placed set is read once per revision of the sky column, not per step; a column
        // outside the box is forgotten, and comes back to a rescan.
        var columns = new Columns();
        SkyColumn column = columns.Get(0, 0);
        Floor(column, 0, 4);
        var rain = new RainOnPlaced(columns, new Random(1));
        var cells = new List<VoxelPos>();

        rain.Advance(Cell / 2, new ChunkPos(0, 1, 0), 1, 2, cells, 10_000);
        rain.Advance(Cell / 2, new ChunkPos(0, 1, 0), 1, 2, cells, 10_000);
        Assert.Equal(1L, rain.Rescans);
        Assert.Equal(16, rain.PlacedColumns);

        column.SetOpenAboveY(10, 10, Surface + 3);
        rain.Advance(0, new ChunkPos(0, 1, 0), 1, 2, cells, 10_000);
        Assert.Equal(2L, rain.Rescans);
        Assert.Equal(17, rain.PlacedColumns);

        // Away: the column is not in the box and nothing accrues for it; back: a fresh scan and a
        // fresh accumulator — what accrued while away was never diverted, so the basin has it.
        rain.Advance(Cell, new ChunkPos(20, 1, 20), 1, 2, cells, 10_000);
        Assert.Equal(0, rain.PlacedColumns);
        rain.Advance(0, new ChunkPos(0, 1, 0), 1, 2, cells, 10_000);
        Assert.Equal(3L, rain.Rescans);
        Assert.Equal(0L, rain.Backlog);

        // And a column whose chunks all left: no sky column, nothing.
        columns.Remove(0, 0);
        rain.Advance(Cell, new ChunkPos(0, 1, 0), 1, 2, cells, 10_000);
        Assert.Equal(0, rain.PlacedColumns);
    }

    [Fact]
    public void AFloorTakenUpStopsAccruing()
    {
        var columns = new Columns();
        SkyColumn column = columns.Get(0, 0);
        Floor(column, 0, 4);
        var rain = new RainOnPlaced(columns, new Random(1));
        var cells = new List<VoxelPos>();
        Assert.Equal(8, rain.Advance(Cell / 2, new ChunkPos(0, 1, 0), 1, 2, cells, 10_000));
        Assert.Equal(16, rain.PlacedColumns);

        cells.Clear();
        Floor(column, 0, 4, height: 0);
        rain.Advance(Cell, new ChunkPos(0, 1, 0), 1, 2, cells, 10_000);
        Assert.Equal(0, rain.PlacedColumns);
        Assert.Empty(cells);
    }
}
