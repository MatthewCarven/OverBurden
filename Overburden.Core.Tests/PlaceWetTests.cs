using Overburden.Core.Building;
using Overburden.Core.Fluid;
using Overburden.Core.Inventory;
using Overburden.Core.Jobs;
using Overburden.Core.Voxels;
using static Overburden.Core.Tests.PhysicsTestHelpers;

namespace Overburden.Core.Tests;

/// <summary>
/// M9d, decisions 3 and 4: a fill refuses a voxel holding water, through one gate every shape
/// shares; and the answer comes from a published copy of the automaton's wet bitsets, never the
/// grid, so a keypress mid-tick is answered rather than refused.
/// </summary>
public class PlaceWetTests
{
    private const int Cap = FluidConstants.CellCapacityMillilitres;

    private static PlaceResult Accounted(PlaceResult result)
    {
        Assert.True(result.IsAccounted, $"buckets do not add up: {result}");
        return result;
    }

    // ------------------------------------------------------------------ the gate

    [Fact]
    public void ACubeLeavesTheWetVoxelsAndFillsTheDryOnesRoundThem()
    {
        var world = new TestWorld();
        var cell = new BrushCell(1, 2, 3);
        for (int i = 0; i < 10; i++)
        {
            world.Wet.Add(cell.VoxelAt(i % 4, 0, i / 4));
        }

        PlaceResult result = Accounted(Place.Cube(world, Table, cell, Stone));

        Assert.Equal(64, result.Candidates);
        Assert.Equal(54, result.Placed);
        Assert.Equal(10, result.Wet);
        Assert.Equal(0, result.Occupied);
        Assert.True(result.Complete);
        Assert.Empty(world.WrittenVoxels.Intersect(world.Wet));
        foreach (VoxelPos wet in world.Wet)
        {
            Assert.Equal(Air, world[wet]);
        }
    }

    [Fact]
    public void EveryShapeRefusesTheSameWetVoxel()
    {
        // One Put holds the gate: the precision voxel, the brushes and the chunk fill all answer
        // Wet for the same cell, so no shape can be the one that deletes water.
        var wet = new VoxelPos(5, 9, 6);
        PlaceResult voxel = Accounted(Place.Voxel(Wet(wet), Table, wet, Stone));
        PlaceResult cube = Accounted(Place.Cube(Wet(wet), Table, BrushCell.FromVoxel(wet), Stone));
        PlaceResult slab = Accounted(Place.Slab(Wet(wet), Table, BrushCell.FromVoxel(wet), Stone));
        PlaceResult chunk = Accounted(Place.Chunk(Wet(wet), Table, wet.Chunk, Stone));

        Assert.Equal((1, 0), (voxel.Wet, voxel.Placed));
        Assert.Equal(1, cube.Wet);
        Assert.Equal(63, cube.Placed);
        Assert.Equal(1, chunk.Wet);
        Assert.Equal(VoxelConstants.ChunkVolume - 1, chunk.Placed);
        // The slab may or may not cover the wet voxel's row; whichever, the sum is the brush.
        Assert.Equal(slab.Candidates, slab.Placed + slab.Wet);

        static TestWorld Wet(VoxelPos voxel)
        {
            var world = new TestWorld();
            world.Wet.Add(voxel);
            return world;
        }
    }

    [Fact]
    public void AWetRockIsOccupiedNotWet()
    {
        // The order: the material read comes first, so a contradiction the automaton never
        // writes — water in rock — reads as the rock, and a wet voxel is only ever air.
        var world = new TestWorld();
        var voxel = new VoxelPos(2, 2, 2);
        world.Set(voxel, Stone);
        world.Wet.Add(voxel);

        PlaceResult result = Accounted(Place.Voxel(world, Table, voxel, Stone));

        Assert.Equal(1, result.Occupied);
        Assert.Equal(0, result.Wet);
    }

    [Fact]
    public void AWetVoxelCostsNothingFromTheInventory()
    {
        // Wet before the stock, for the reason Occupied is: nothing is charged for a voxel the
        // brush would not fill, so a brush over a pool costs the dry cells alone.
        var world = new TestWorld();
        var cell = new BrushCell(0, 0, 0);
        for (int i = 0; i < 4; i++)
        {
            world.Wet.Add(cell.VoxelAt(i, 0, 0));
        }

        var inventory = new VolumetricInventory();
        inventory.Add(Stone, 60);

        PlaceResult result = Accounted(Place.Cube(world, Table, cell, Stone, body: null, from: inventory));

        Assert.Equal(60, result.Placed);
        Assert.Equal(4, result.Wet);
        Assert.Equal(0, result.OutOfStock);
        Assert.Equal(0, inventory.Voxels(Stone));
    }

    [Fact]
    public void ADryEditorNeverAnswersWet()
    {
        // The interface's default, which is what every carve and brush fixture runs against.
        var world = new TestWorld();
        PlaceResult result = Accounted(Place.Cube(world, Table, new BrushCell(0, 0, 0), Stone));
        Assert.Equal(0, result.Wet);
        Assert.Equal(64, result.Placed);
    }

    // ------------------------------------------------------------------ the bitset

    [Fact]
    public void TheGridsWetBitsFollowEveryWriteAcrossZero()
    {
        var grid = new FluidGrid();
        var a = new VoxelPos(3, 4, 5);
        var b = new VoxelPos(4, 4, 5);

        Assert.False(grid.IsWet(a));
        grid.Fill(a, Cap);
        Assert.True(grid.IsWet(a));
        Assert.False(grid.IsWet(b));

        grid.Transfer(a, b, Cap / 2);
        Assert.True(grid.IsWet(a));
        Assert.True(grid.IsWet(b));

        grid.Drain(a, Cap);
        Assert.False(grid.IsWet(a));
        Assert.True(grid.IsWet(b));

        // A partial drain keeps the bit: wet means anything at all, the held count's rule.
        grid.Drain(b, 1);
        Assert.True(grid.IsWet(b));
        grid.Drain(b, Cap);
        Assert.False(grid.IsWet(b));
        Assert.Equal(0, grid.PanelCount);
    }

    [Fact]
    public void CollectWetCopiesTouchedPanelsOnlyAndDropsReleasedOnes()
    {
        var grid = new FluidGrid();
        var published = new Dictionary<ChunkPos, ulong[]>();
        var a = new VoxelPos(1, 1, 1);
        var b = new VoxelPos(40, 1, 1);
        grid.Fill(a, Cap);
        grid.Fill(b, Cap);

        Assert.Equal(2, grid.CollectWet(published));
        Assert.True(FluidGrid.IsWetIn(published, a));
        Assert.True(FluidGrid.IsWetIn(published, b));
        Assert.False(FluidGrid.IsWetIn(published, a.Offset(1, 0, 0)));

        // Nothing touched: nothing copied, and the copies stand.
        Assert.Equal(0, grid.CollectWet(published));
        Assert.True(FluidGrid.IsWetIn(published, a));

        // A write in one panel copies that panel alone; the copy is a snapshot, not a view.
        grid.Fill(a.Offset(1, 0, 0), Cap);
        Assert.False(FluidGrid.IsWetIn(published, a.Offset(1, 0, 0)));
        Assert.Equal(1, grid.CollectWet(published));
        Assert.True(FluidGrid.IsWetIn(published, a.Offset(1, 0, 0)));

        // A panel drained to nothing is released, and the next collect drops its copy.
        grid.Drain(b, Cap);
        Assert.Equal(0, grid.PanelCount - 1);
        grid.CollectWet(published);
        Assert.False(published.ContainsKey(b.Chunk));
        Assert.False(FluidGrid.IsWetIn(published, b));

        // Released and refilled between collects: the copy wins over the drop.
        grid.Drain(a, Cap);
        grid.Drain(a.Offset(1, 0, 0), Cap);
        Assert.Equal(0, grid.PanelCount);
        grid.Fill(a, Cap);
        grid.CollectWet(published);
        Assert.True(FluidGrid.IsWetIn(published, a));
        Assert.False(FluidGrid.IsWetIn(published, a.Offset(1, 0, 0)));

        // Evicted, not drained: the copy goes too.
        grid.Evict(a.Chunk);
        grid.CollectWet(published);
        Assert.False(published.ContainsKey(a.Chunk));
    }

    // ------------------------------------------------------------------ the published answer

    [Fact]
    public void HoldsWaterReadsThePublishedCopyAndAnswersWhileAJobOwnsTheGrid()
    {
        // The whole of decision 4 in one fixture: the answer is the last publish's, it never
        // touches the grid, and it is stale by at most one tick in both directions.
        var terrain = new PredicateTerrain(v => v.Y >= 8);
        var sim = new FluidSimulation(terrain, fallCells: 1);
        var top = new VoxelPos(10, 12, 10);
        sim.Add(top, Cap);

        // Added at a boundary and not yet published: dry to the fill until Publish.
        Assert.False(sim.HoldsWater(top));
        sim.Publish();
        Assert.True(sim.HoldsWater(top));
        Assert.False(sim.HoldsWater(top.Offset(0, -1, 0)));
        Assert.Equal(1, sim.WetPanelsPublished);

        // A job on the grid: the grid refuses, the copy answers — the same answer as before the
        // tick began, whatever the worker has done since.
        FluidTickJob job = sim.BeginTick(terrain, ChunkPos.Zero);
        Assert.Throws<InvalidOperationException>(() => sim.Grid);
        Assert.True(sim.HoldsWater(top));
        job.Run(CancellationToken.None);
        Assert.True(sim.HoldsWater(top), "the cell fell during the tick, and the copy still says wet until the collection publishes");
        Assert.False(sim.HoldsWater(top.Offset(0, -1, 0)));

        // Collected: the tick published, the water is lower (one cell a tick, and a cell woken
        // beside the source is visited later in the same tick — M8b-4a), and the copy says so.
        job.OnResult(JobOutcome.Completed, null);
        Assert.False(sim.HoldsWater(top));
        VoxelPos landed = Assert.Single(sim.Grid.HeldCells());
        Assert.True(landed.Y < top.Y);
        Assert.True(sim.HoldsWater(landed));
        Assert.Equal(sim.Grid.IsWet(top), sim.HoldsWater(top));
    }

    [Fact]
    public void HoldsWaterAgreesWithTheGridAtEveryBoundaryOfAPour()
    {
        // Over a whole pour spreading on a floor, the published copy equals the grid's bitset
        // for every held cell and for a ring of dry ones round them, at every boundary.
        var terrain = new PredicateTerrain(v => v.Y >= 8);
        var sim = new FluidSimulation(terrain, fallCells: 1);
        for (int x = 4; x <= 11; x++)
        for (int z = 4; z <= 11; z++)
        {
            sim.Add(new VoxelPos(x, 14, z), Cap);
        }

        for (int tick = 0; tick < 40; tick++)
        {
            sim.Step();
            sim.Publish();
            foreach (VoxelPos held in sim.Grid.HeldCells())
            {
                Assert.True(sim.HoldsWater(held));
                foreach (VoxelPos side in new[] { held.Offset(1, 0, 0), held.Offset(-1, 0, 0), held.Offset(0, 1, 0), held.Offset(0, -1, 0) })
                {
                    Assert.Equal(sim.Grid.IsWet(side), sim.HoldsWater(side));
                }
            }
        }
    }
}
