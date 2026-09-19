using Overburden.Core.Building;
using Overburden.Core.Fluid;
using Overburden.Core.Inventory;
using Overburden.Core.Voxels;
using static Overburden.Core.Tests.PhysicsTestHelpers;

namespace Overburden.Core.Tests;

/// <summary>
/// A fill into water at the boundary displaces it (2026-09-19, <see cref="FillWater"/>): the wet
/// voxels a shape writes are lifted first, the shape lands, and the water is returned bottom-up
/// over the shape's box grown by one cell, the rest counted displaced. Every shape, one gate.
/// </summary>
public class PlaceDisplacingTests
{
    private const long Each = FluidConstants.CellCapacityMillilitres;

    private static PlaceResult Accounted(PlaceResult result)
    {
        Assert.True(result.IsAccounted, $"buckets do not add up: {result}");
        return result;
    }

    private static (TestWorld World, FakeWater Water) Pool(BrushCell cell, int rows = 1)
    {
        // A pool filling the bottom rows of a cell, one capacity a voxel.
        var world = new TestWorld();
        var water = new FakeWater(world);
        for (int lz = 0; lz < BrushCell.Voxels; lz++)
        for (int lx = 0; lx < BrushCell.Voxels; lx++)
        for (int ly = 0; ly < rows; ly++)
        {
            water.Put(cell.VoxelAt(lx, ly, lz), Each);
        }

        return (world, water);
    }

    [Fact]
    public void ACubeIntoAPoolLiftsTheWaterFillsEveryVoxelAndReturnsItAround()
    {
        var cell = new BrushCell(1, 2, 3);
        (TestWorld world, FakeWater water) = Pool(cell);
        var displacing = new FillWater(water);

        PlaceResult result = Accounted(Place.Cube(world, Table, cell, Stone, displacing: displacing));

        Assert.Equal(64, result.Placed);
        Assert.Equal(0, result.Wet);
        Assert.True(displacing.IsAccounted);
        Assert.Equal(16 * Each, displacing.Lifted);
        Assert.Equal(16 * Each, displacing.Returned);
        Assert.Equal(0L, displacing.Displaced);

        // Nothing stayed inside the cube, everything landed in the box one cell around it, and
        // the world still holds every millilitre.
        foreach ((VoxelPos voxel, long held) in water.Cells)
        {
            Assert.NotEqual(cell, BrushCell.FromVoxel(voxel));
            Assert.True(held > 0);
            Assert.InRange(voxel.X, cell.OriginVoxel.X - 4, cell.OriginVoxel.X + 7);
            Assert.InRange(voxel.Y, cell.OriginVoxel.Y - 4, cell.OriginVoxel.Y + 7);
            Assert.InRange(voxel.Z, cell.OriginVoxel.Z - 4, cell.OriginVoxel.Z + 7);
        }

        Assert.Equal(16 * Each, water.Cells.Values.Sum());
    }

    [Fact]
    public void TheWaterGoesBackBottomUp()
    {
        // The level rises: the first cell the water is returned to is in the lowest row of the
        // grown box, which is a full cell below the shape.
        var cell = new BrushCell(0, 5, 0);
        var world = new TestWorld();
        var water = new FakeWater(world);
        water.Put(cell.VoxelAt(2, 2, 2), 1000);
        var displacing = new FillWater(water);

        Accounted(Place.Cube(world, Table, cell, Stone, displacing: displacing));

        Assert.Equal(1000L, displacing.Returned);
        (VoxelPos voxel, long held) = Assert.Single(water.Cells);
        Assert.Equal(1000L, held);
        Assert.Equal(cell.OriginVoxel.Y - BrushCell.Voxels, voxel.Y);
        Assert.Equal(water.Returns[0], voxel);
    }

    [Fact]
    public void WithNoWaterInHandAWetVoxelIsStillRefused()
    {
        // The click's path is unchanged: M9d's refusal, the rest of the brush lands.
        var cell = new BrushCell(1, 2, 3);
        (TestWorld world, FakeWater water) = Pool(cell);

        PlaceResult result = Accounted(Place.Cube(world, Table, cell, Stone));

        Assert.Equal(48, result.Placed);
        Assert.Equal(16, result.Wet);
        Assert.Equal(16 * Each, water.Cells.Values.Sum());
        Assert.Empty(water.Returns);
    }

    [Fact]
    public void ARampLeavesItsOwnAirForTheWaterToComeBackInto()
    {
        // The shape's own voxels are not skipped on the return: a ramp fills 40 of 64, and the
        // 24 it leaves as air are the nearest room there is.
        // Rock everywhere but the cell itself, so the ramp's own air is the only room there is;
        // the water sits in a voxel the ramp writes (lx 0 is one voxel tall facing +X).
        var cell = new BrushCell(0, 8, 0);
        var world = new TestWorld { Background = v => BrushCell.FromVoxel(v) == cell ? Air : Stone };
        var water = new FakeWater(world);
        water.Put(cell.VoxelAt(0, 0, 0), 500);
        var displacing = new FillWater(water);

        PlaceResult result = Accounted(Place.Ramp(world, Table, cell, Stone, BrushFacing.PositiveX, displacing: displacing));

        Assert.Equal(40, result.Placed);
        Assert.Equal(0, result.Wet);
        Assert.Equal(500L, displacing.Lifted);
        Assert.Equal(500L, displacing.Returned);
        (VoxelPos landed, _) = Assert.Single(water.Cells);
        Assert.Equal(Air, world[landed]);
        Assert.Equal(cell, BrushCell.FromVoxel(landed));
    }

    [Fact]
    public void IntoAFullBoxUnderARoofTheFillsWorthIsDisplaced()
    {
        // The honest case the old rule refused outright: every cell around already full and
        // rock beyond, so nothing has room and the displaced term carries all of it.
        var cell = new BrushCell(2, 2, 2);
        var world = new TestWorld
        {
            Background = v =>
                v.X >= cell.OriginVoxel.X - 4 && v.X <= cell.OriginVoxel.X + 7
                && v.Y >= cell.OriginVoxel.Y - 4 && v.Y <= cell.OriginVoxel.Y + 7
                && v.Z >= cell.OriginVoxel.Z - 4 && v.Z <= cell.OriginVoxel.Z + 7
                    ? Air
                    : Stone,
        };
        var water = new FakeWater(world);
        for (int x = cell.OriginVoxel.X - 4; x <= cell.OriginVoxel.X + 7; x++)
        for (int y = cell.OriginVoxel.Y - 4; y <= cell.OriginVoxel.Y + 7; y++)
        for (int z = cell.OriginVoxel.Z - 4; z <= cell.OriginVoxel.Z + 7; z++)
        {
            water.Put(new VoxelPos(x, y, z), Each);
        }

        var displacing = new FillWater(water);
        PlaceResult result = Accounted(Place.Cube(world, Table, cell, Stone, displacing: displacing));

        Assert.Equal(64, result.Placed);
        Assert.Equal(64 * Each, displacing.Lifted);
        Assert.Equal(0L, displacing.Returned);
        Assert.Equal(64 * Each, displacing.Displaced);
        Assert.Equal(64 * Each, water.Displaced);
        Assert.True(displacing.IsAccounted);
    }

    [Fact]
    public void ARefusedWritePutsTheWaterStraightBackWhereItWas()
    {
        // Out of stock after the lift: the voxel is still air, so its own water goes back into
        // it and nothing is moved by a shape that placed nothing.
        var cell = new BrushCell(1, 2, 3);
        (TestWorld world, FakeWater water) = Pool(cell);
        var inventory = new VolumetricInventory();
        var displacing = new FillWater(water);

        PlaceResult result = Accounted(Place.Cube(world, Table, cell, Stone, from: inventory, displacing: displacing));

        Assert.Equal(64, result.OutOfStock);
        Assert.Equal(0, result.Placed);
        Assert.Equal(16 * Each, displacing.Lifted);
        Assert.Equal(16 * Each, displacing.Returned);
        Assert.Equal(0L, displacing.Displaced);
        Assert.Equal(16, water.Cells.Count);
        Assert.All(water.Cells.Keys, v => Assert.Equal(cell, BrushCell.FromVoxel(v)));
        Assert.All(water.Cells.Keys, v => Assert.Equal(0, v.Y - cell.OriginVoxel.Y));
    }

    [Fact]
    public void EveryShapeDisplacesTheSameWetVoxel()
    {
        // One gate in Put, so the voxel, the cube, the slab, the ramp, the sphere and the chunk
        // all lift the same water and return it — none of them refuses it.
        // A voxel every shape writes: inside the slab's two rows, the +X ramp's second step
        // and the 0.6 m sphere (the corner voxel is 0.65 m from the centre and outside it).
        var cell = new BrushCell(1, 2, 3);
        VoxelPos wet = cell.VoxelAt(1, 1, 1);

        var shapes = new (string Name, Func<TestWorld, FillWater, PlaceResult> Run)[]
        {
            ("voxel", (w, d) => Place.Voxel(w, Table, wet, Stone, displacing: d)),
            ("cube", (w, d) => Place.Cube(w, Table, cell, Stone, displacing: d)),
            ("slab", (w, d) => Place.Slab(w, Table, cell, Stone, displacing: d)),
            ("ramp", (w, d) => Place.Ramp(w, Table, cell, Stone, BrushFacing.PositiveX, displacing: d)),
            ("sphere", (w, d) => Place.Sphere(w, Table, cell, 0.6f, Stone, displacing: d)),
            ("chunk", (w, d) => Place.Chunk(w, Table, wet.Chunk, Stone, displacing: d)),
        };

        foreach ((string name, Func<TestWorld, FillWater, PlaceResult> run) in shapes)
        {
            var world = new TestWorld();
            var water = new FakeWater(world);
            water.Put(wet, 777);
            var displacing = new FillWater(water);

            PlaceResult result = Accounted(run(world, displacing));

            Assert.True(result.Placed > 0, name);
            Assert.Equal(0, result.Wet);
            Assert.Equal(777L, displacing.Lifted);
            Assert.Equal(777L, displacing.Returned);
            Assert.Equal(Stone, world[wet]);
            Assert.Equal(777L, water.Cells.Values.Sum());
        }
    }

    [Fact]
    public void AFillWithNothingWetLiftsNothingAndSettlesNothing()
    {
        var cell = new BrushCell(1, 2, 3);
        var world = new TestWorld();
        var water = new FakeWater(world);
        var displacing = new FillWater(water);

        Accounted(Place.Cube(world, Table, cell, Stone, displacing: displacing));

        Assert.Equal(0L, displacing.Lifted);
        Assert.Empty(water.Returns);
        Assert.Equal(0L, water.Displaced);
    }

    /// <summary>
    /// Millilitres per voxel with the automaton's capacity and the world's holdability, kept in
    /// step with the world's wet set so a lift is what the fill's gate then reads.
    /// </summary>
    private sealed class FakeWater(TestWorld world) : ICollapseWater
    {
        public Dictionary<VoxelPos, long> Cells { get; } = [];

        public List<VoxelPos> Returns { get; } = [];

        public long Displaced { get; private set; }

        public void Put(VoxelPos voxel, long millilitres)
        {
            Cells[voxel] = millilitres;
            world.Wet.Add(voxel);
        }

        public long Lift(VoxelPos cell)
        {
            world.Wet.Remove(cell);
            return Cells.Remove(cell, out long held) ? held : 0;
        }

        public long Return(VoxelPos cell, long millilitres)
        {
            if (millilitres == 0 || world[cell] != Air) return millilitres;
            long held = Cells.GetValueOrDefault(cell);
            long room = FluidConstants.CellCapacityMillilitres - held;
            long taken = Math.Min(room, millilitres);
            if (taken > 0)
            {
                Cells[cell] = held + taken;
                world.Wet.Add(cell);
                Returns.Add(cell);
            }

            return millilitres - taken;
        }

        public void DisplaceLifted(long millilitres) => Displaced += millilitres;

        public bool IsReady(ChunkPos chunk) => true;
    }
}
