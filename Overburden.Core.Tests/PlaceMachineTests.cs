using Overburden.Core.Building;
using Overburden.Core.Content;
using Overburden.Core.Fluid;
using Overburden.Core.Inventory;
using Overburden.Core.Machines;
using Overburden.Core.Physics;
using Overburden.Core.Voxels;
using static Overburden.Core.Tests.PhysicsTestHelpers;

namespace Overburden.Core.Tests;

/// <summary>
/// <see cref="Place.Machine"/> and <see cref="Place.MachineDisplacing"/> (M10a-2): all 64 voxels
/// and the row, or nothing at all; into water, the water lifted, the body written, the water
/// returned bottom-up around it.
/// </summary>
public class PlaceMachineTests
{
    private const int Pump = 4;
    private const int Cell64 = BrushCell.Voxels * BrushCell.Voxels * BrushCell.Voxels;

    /// <summary>The physics table plus a machine, so a machine test and a brush test share stone and water.</summary>
    private static readonly MaterialTable Materials = MaterialTable.ForTesting(
        //             air    dirt   water  stone  pump
        isOpaque: [false, true, false, true, true],
        isFluid: [false, false, true, false, false],
        textureLayer: [0, 1, 2, 3, 4],
        hardness: [0f, 0.8f, 0f, 3.0f, 3.0f],
        machine: [null, null, null, null, PipelineHarness.PumpDefinition],
        unit: [default, default, default, default, MaterialUnit.Cells]);

    private static readonly BrushCell Cell = new(2, 3, 2);

    private static VolumetricInventory Stock(long voxels = Cell64)
    {
        var inventory = new VolumetricInventory();
        if (voxels > 0) inventory.Add(Pump, voxels);
        return inventory;
    }

    private static PlaceResult Accounted(PlaceResult result)
    {
        Assert.True(result.IsAccounted, $"buckets do not add up: {result}");
        return result;
    }

    [Fact]
    public void AMachinePlacesAllSixtyFourVoxelsAndItsRowInOneCall()
    {
        var world = new TestWorld();
        var machines = new MachineSet(Materials);
        VolumetricInventory stock = Stock();

        PlaceResult result = Accounted(Place.Machine(world, Materials, Cell, Pump, BrushFacing.NegativeX, machines, from: stock));

        Assert.Equal(Cell64, result.Placed);
        Assert.Equal(Cell64, world.Writes.Count);
        Assert.All(world.Writes, write => Assert.Equal(Pump, write.Material));
        Assert.True(machines.TryGet(Cell, out MachineRow row));
        Assert.Equal(Pump, row.MaterialId);
        Assert.Equal(BrushFacing.NegativeX, row.Facing);
        Assert.Equal(0L, row.Ports["channel"]);
        Assert.Equal(1L, row.Ports["weight"]);
        Assert.Equal(0L, stock.Voxels(Pump));
        Assert.Equal(1, machines.Placed);
    }

    [Fact]
    public void ARockVoxelAmongTheSixtyFourRefusesTheWholeCell()
    {
        // A half-placed body is a body with no row or a row with no body, and the reconcile would
        // delete it a frame later having debited the inventory. So one occupied voxel is 64
        // Occupied, nothing written, no row, no debit.
        var world = new TestWorld();
        world.Set(Cell.VoxelAt(3, 3, 3), Stone);
        var machines = new MachineSet(Materials);
        VolumetricInventory stock = Stock();

        PlaceResult result = Accounted(Place.Machine(world, Materials, Cell, Pump, BrushFacing.PositiveX, machines, from: stock));

        Assert.Equal(Cell64, result.Occupied);
        Assert.Equal(0, result.Placed);
        Assert.Empty(world.Writes);
        Assert.Equal(0, machines.Count);
        Assert.Equal(Cell64, stock.Voxels(Pump));
    }

    [Fact]
    public void ACellInsideThePlayerRefusesTheWholeCell()
    {
        var world = new TestWorld();
        var machines = new MachineSet(Materials);
        Aabb body = Aabb.FromFeet(Cell.CentreMetres with { Y = Cell.CentreMetres.Y - 0.5f }, 0.5f, 1.8f);

        PlaceResult result = Accounted(Place.Machine(world, Materials, Cell, Pump, BrushFacing.PositiveX, machines, body, Stock()));

        Assert.Equal(Cell64, result.InsidePlayer);
        Assert.Empty(world.Writes);
        Assert.Equal(0, machines.Count);
    }

    [Fact]
    public void AnUnloadedCellRefusesTheWholeCell()
    {
        var world = new TestWorld();
        world.Unload(Cell.OriginVoxel.Chunk);
        var machines = new MachineSet(Materials);

        PlaceResult result = Accounted(Place.Machine(world, Materials, Cell, Pump, BrushFacing.PositiveX, machines, from: Stock()));

        Assert.Equal(Cell64, result.Unreachable);
        Assert.Equal(0, machines.Count);
    }

    [Fact]
    public void AWetVoxelRefusesTheWholeCellAndWritesNothing()
    {
        // 64 Wet is the controller's cue to queue the placement to the boundary; the instant
        // path itself never writes over water.
        var world = new TestWorld();
        world.Wet.Add(Cell.VoxelAt(0, 0, 0));
        var machines = new MachineSet(Materials);
        VolumetricInventory stock = Stock();

        PlaceResult result = Accounted(Place.Machine(world, Materials, Cell, Pump, BrushFacing.PositiveX, machines, from: stock));

        Assert.Equal(Cell64, result.Wet);
        Assert.Empty(world.Writes);
        Assert.Equal(0, machines.Count);
        Assert.Equal(Cell64, stock.Voxels(Pump));
    }

    [Fact]
    public void SixtyThreeVoxelsInStockPlacesNothing()
    {
        // A machine is a unit: 63 voxels of pump is no pump, not most of one.
        var world = new TestWorld();
        var machines = new MachineSet(Materials);
        VolumetricInventory stock = Stock(Cell64 - 1);

        PlaceResult result = Accounted(Place.Machine(world, Materials, Cell, Pump, BrushFacing.PositiveX, machines, from: stock));

        Assert.Equal(Cell64, result.OutOfStock);
        Assert.Empty(world.Writes);
        Assert.Equal(0, machines.Count);
        Assert.Equal(Cell64 - 1, stock.Voxels(Pump));
    }

    [Fact]
    public void ASecondMachineAtTheSameCellIsOccupied()
    {
        var world = new TestWorld();
        var machines = new MachineSet(Materials);
        Place.Machine(world, Materials, Cell, Pump, BrushFacing.PositiveX, machines, from: Stock());

        PlaceResult again = Accounted(Place.Machine(world, Materials, Cell, Pump, BrushFacing.PositiveX, machines, from: Stock()));

        Assert.Equal(Cell64, again.Occupied);
        Assert.Equal(1, machines.Count);
    }

    [Fact]
    public void AWriteThatFailsMidWayRollsBackAndMakesNoRow()
    {
        // The read succeeded and the write refused — an editor whose halves disagree — on the
        // forty-somethingth voxel, so there is something to roll back: refusing the whole chunk
        // fails the first write and makes "no rollback" an identity, which the battery found.
        // The voxels already written go back to air, no row is made, nothing is debited.
        var world = new TestWorld();
        world.RefuseWrite(Cell.VoxelAt(2, 2, 2));
        var machines = new MachineSet(Materials);
        VolumetricInventory stock = Stock();

        PlaceResult result = Accounted(Place.Machine(world, Materials, Cell, Pump, BrushFacing.PositiveX, machines, from: stock));

        Assert.Equal(Cell64, result.Unreachable);
        Assert.Equal(0, machines.Count);
        Assert.Equal(Cell64, stock.Voxels(Pump));
        Assert.True(world.Writes.Count(w => w.Material == Pump) > 1, "the fixture failed on the first write and tests nothing");
        for (int lz = 0; lz < BrushCell.Voxels; lz++)
        for (int lx = 0; lx < BrushCell.Voxels; lx++)
        for (int ly = 0; ly < BrushCell.Voxels; ly++)
        {
            Assert.Equal(Air, world[Cell.VoxelAt(lx, ly, lz)]);
        }
    }

    [Fact]
    public void ANonMachineMaterialIsRefusedByTheVerb() =>
        Assert.Throws<ArgumentException>(
            () => Place.Machine(new TestWorld(), Materials, Cell, Stone, BrushFacing.PositiveX, new MachineSet(Materials)));

    // ----------------------------------------------------------------------- the displacing path

    [Fact]
    public void IntoWaterTheCellIsLiftedTheBodyWrittenAndTheWaterReturnedAroundIt()
    {
        var world = new TestWorld();
        var water = new FakeWater(world);
        long each = FluidConstants.CellCapacityMillilitres;
        for (int lz = 0; lz < BrushCell.Voxels; lz++)
        for (int lx = 0; lx < BrushCell.Voxels; lx++)
        for (int ly = 0; ly < BrushCell.Voxels; ly++)
        {
            water.Cells[Cell.VoxelAt(lx, ly, lz)] = each;
        }

        var machines = new MachineSet(Materials);
        VolumetricInventory stock = Stock();

        MachinePlacement placed = Place.MachineDisplacing(
            world, Materials, Cell, Pump, BrushFacing.PositiveX, machines, water, from: stock);

        Assert.True(placed.IsAccounted);
        Assert.Equal(Cell64, placed.Result.Placed);
        Assert.Equal(Cell64 * each, placed.Lifted);
        Assert.Equal(Cell64 * each, placed.Returned);
        Assert.Equal(0L, placed.Displaced);
        Assert.True(machines.TryGet(Cell, out _));
        Assert.Equal(0L, stock.Voxels(Pump));

        // Nothing stayed inside the body, and everything went into the box around it.
        foreach ((VoxelPos voxel, long held) in water.Cells)
        {
            Assert.NotEqual(Cell, BrushCell.FromVoxel(voxel));
            Assert.True(held > 0);
            Assert.InRange(voxel.X, Cell.OriginVoxel.X - 4, Cell.OriginVoxel.X + 7);
            Assert.InRange(voxel.Y, Cell.OriginVoxel.Y - 4, Cell.OriginVoxel.Y + 7);
            Assert.InRange(voxel.Z, Cell.OriginVoxel.Z - 4, Cell.OriginVoxel.Z + 7);
        }

        Assert.Equal(Cell64 * each, water.Cells.Values.Sum());
    }

    [Fact]
    public void TheWaterGoesBackBottomUp()
    {
        // The level in the box rises by the body's volume — the returned cells are the lowest
        // rows of the box first, which is where a submerged body sends water: up from the bottom.
        var world = new TestWorld();
        var water = new FakeWater(world);
        water.Cells[Cell.VoxelAt(1, 1, 1)] = 1000;

        MachinePlacement placed = Place.MachineDisplacing(
            world, Materials, Cell, Pump, BrushFacing.PositiveX, new MachineSet(Materials), water, from: Stock());

        Assert.Equal(1000L, placed.Returned);
        (VoxelPos voxel, long held) = Assert.Single(water.Cells);
        Assert.Equal(1000L, held);
        Assert.Equal(Cell.OriginVoxel.Y - BrushCell.Voxels, voxel.Y);
        Assert.Equal(water.Returns[0], voxel);
    }

    [Fact]
    public void IntoAFullBoxUnderARoofTheWholeBodysWorthIsDisplaced()
    {
        // The honest case: every cell around the body already full and rock above, so nothing
        // has room and the displaced term carries all of it — printed, never lost silently.
        var world = new TestWorld { Background = _ => Stone };
        var water = new FakeWater(world);
        long each = FluidConstants.CellCapacityMillilitres;
        VoxelPos o = Cell.OriginVoxel;
        for (int dz = -4; dz < 8; dz++)
        for (int dx = -4; dx < 8; dx++)
        for (int dy = -4; dy < 8; dy++)
        {
            var voxel = new VoxelPos(o.X + dx, o.Y + dy, o.Z + dz);
            world.Set(voxel, Air);
            water.Cells[voxel] = each;
        }

        MachinePlacement placed = Place.MachineDisplacing(
            world, Materials, Cell, Pump, BrushFacing.PositiveX, new MachineSet(Materials), water, from: Stock());

        Assert.True(placed.IsAccounted);
        Assert.Equal(Cell64, placed.Result.Placed);
        Assert.Equal(Cell64 * each, placed.Lifted);
        Assert.Equal(0L, placed.Returned);
        Assert.Equal(Cell64 * each, placed.Displaced);
        Assert.Equal(Cell64 * each, water.Displaced);
    }

    [Fact]
    public void ARefusedDisplacingPlacementLiftsNothing()
    {
        var world = new TestWorld();
        world.Set(Cell.VoxelAt(0, 0, 0), Stone);
        var water = new FakeWater(world);
        water.Cells[Cell.VoxelAt(1, 1, 1)] = 500;

        MachinePlacement placed = Place.MachineDisplacing(
            world, Materials, Cell, Pump, BrushFacing.PositiveX, new MachineSet(Materials), water, from: Stock());

        Assert.Equal(Cell64, placed.Result.Occupied);
        Assert.Equal(0L, placed.Lifted);
        Assert.Equal(500L, water.Cells[Cell.VoxelAt(1, 1, 1)]);
    }

    [Fact]
    public void ADisplacingWriteThatFailsPutsTheWaterBackWhereItWas()
    {
        var world = new TestWorld();
        world.RefuseWrite(Cell.VoxelAt(3, 3, 3));
        var water = new FakeWater(world);
        VoxelPos wet = Cell.VoxelAt(2, 2, 2);
        water.Cells[wet] = 4000;

        MachinePlacement placed = Place.MachineDisplacing(
            world, Materials, Cell, Pump, BrushFacing.PositiveX, new MachineSet(Materials), water, from: Stock());

        Assert.Equal(Cell64, placed.Result.Unreachable);
        Assert.Equal(4000L, placed.Lifted);
        Assert.Equal(4000L, placed.Returned);
        Assert.Equal(4000L, water.Cells.Values.Sum());
        Assert.True(water.Cells.Keys.All(v => BrushCell.FromVoxel(v) == Cell));
    }

    [Fact]
    public void AgainstTheRealAutomatonThePlacementKeepsTheIdentityExact()
    {
        // The fake above says where the water went; the real FluidSimulation says the identity
        // survived it — Lift and Return are outside `held == added − removed − displaced`, and
        // DisplaceLifted is inside it, so a placement into a flooded room moves the total by
        // exactly what it displaced and nothing else. A room three cells wide with the pump in
        // the middle: the body's water goes into the ring around it.
        var world = new TestWorld { Background = _ => Stone };
        VoxelPos o = Cell.OriginVoxel;
        for (int dz = -4; dz < 8; dz++)
        for (int dx = -4; dx < 8; dx++)
        for (int dy = -4; dy < 8; dy++)
        {
            world.Set(new VoxelPos(o.X + dx, o.Y + dy, o.Z + dz), Air);
        }

        var sim = new FluidSimulation(new PredicateTerrain(v => world[v] == Air));
        long each = FluidConstants.CellCapacityMillilitres / 2;
        for (int lz = 0; lz < BrushCell.Voxels; lz++)
        for (int lx = 0; lx < BrushCell.Voxels; lx++)
        for (int ly = 0; ly < BrushCell.Voxels; ly++)
        {
            Assert.Equal(0L, sim.Add(Cell.VoxelAt(lx, ly, lz), each));
        }

        long before = sim.TotalMillilitres;
        var machines = new MachineSet(Materials);

        MachinePlacement placed = Place.MachineDisplacing(
            world, Materials, Cell, Pump, BrushFacing.PositiveX, machines, sim, from: Stock());

        Assert.True(placed.IsAccounted);
        Assert.Equal(Cell64, placed.Result.Placed);
        Assert.Equal(Cell64 * each, placed.Lifted);
        Assert.Equal(Cell64 * each, placed.Returned);
        Assert.Equal(0L, placed.Displaced);
        Assert.Equal(before, sim.TotalMillilitres);
        Assert.Equal(placed.Lifted, sim.LiftedMillilitres);
        Assert.Equal(placed.Returned, sim.ReturnedMillilitres);
        FluidAssert.Conserves(sim);

        // And nothing of it inside the body, which the terrain now refuses.
        for (int lz = 0; lz < BrushCell.Voxels; lz++)
        for (int lx = 0; lx < BrushCell.Voxels; lx++)
        for (int ly = 0; ly < BrushCell.Voxels; ly++)
        {
            Assert.Equal(0, sim.Grid.AmountAt(Cell.VoxelAt(lx, ly, lz)));
        }
    }

    /// <summary>
    /// A grid of millilitres per voxel with the automaton's capacity and the world's holdability,
    /// so a test can see where every lifted millilitre went.
    /// </summary>
    private sealed class FakeWater(TestWorld world) : ICollapseWater
    {
        public Dictionary<VoxelPos, long> Cells { get; } = [];

        public List<VoxelPos> Returns { get; } = [];

        public long Displaced { get; private set; }

        public long Lift(VoxelPos cell)
        {
            if (!Cells.Remove(cell, out long held)) return 0;
            return held;
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
                Returns.Add(cell);
            }

            return millilitres - taken;
        }

        public void DisplaceLifted(long millilitres) => Displaced += millilitres;

        public bool IsReady(ChunkPos chunk) => true;
    }
}
