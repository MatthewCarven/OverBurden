using Overburden.Core.Building;
using Overburden.Core.Content;
using Overburden.Core.Machines;
using Overburden.Core.Voxels;
using static Overburden.Core.Tests.PhysicsTestHelpers;

namespace Overburden.Core.Tests;

/// <summary>
/// <c>placeable: false</c> (M10b, decision 2): an item in the hand is carried and counted but
/// never a block, and <c>Place</c> refuses it the way it refuses air — a caller error, because
/// the controller routes an item to its verb before reaching here.
/// </summary>
public class PlaceItemTests
{
    private const int Computer = 4;
    private const int Pump = 5;

    /// <summary>The physics table plus an item and a machine, so the refusal is tested beside a body that places.</summary>
    private static readonly MaterialTable Materials = MaterialTable.ForTesting(
        //             air    dirt   water  stone  item   pump
        isOpaque: [false, true, false, true, true, true],
        isFluid: [false, false, true, false, false, false],
        textureLayer: [0, 1, 2, 3, 4, 5],
        hardness: [0f, 0.8f, 0f, 3.0f, 1.0f, 3.0f],
        machine: [null, null, null, null, null, PipelineHarness.PumpDefinition],
        unit: [default, default, default, default, MaterialUnit.Count, MaterialUnit.Cells],
        placeable: [true, true, true, true, false, true]);

    private static readonly BrushCell Cell = new(2, 3, 2);

    [Fact]
    public void ATableProjectsPlaceableAndDefaultsItTrue()
    {
        Assert.False(Materials.IsPlaceable(Computer));
        Assert.True(Materials.IsPlaceable(Dirt));
        Assert.True(Materials.IsPlaceable(Pump));
        Assert.True(Table.IsPlaceable(Stone));
    }

    [Fact]
    public void EveryShapeRefusesAnItemBeforeWritingAnything()
    {
        var world = new TestWorld();

        Assert.Throws<ArgumentOutOfRangeException>(() => Place.Cube(world, Materials, Cell, Computer));
        Assert.Throws<ArgumentOutOfRangeException>(() => Place.Slab(world, Materials, Cell, Computer));
        Assert.Throws<ArgumentOutOfRangeException>(() => Place.Ramp(world, Materials, Cell, Computer, BrushFacing.PositiveX));
        Assert.Throws<ArgumentOutOfRangeException>(() => Place.Sphere(world, Materials, Cell, 0.5f, Computer));
        Assert.Throws<ArgumentOutOfRangeException>(() => Place.Voxel(world, Materials, Cell.OriginVoxel, Computer));
        Assert.Throws<ArgumentOutOfRangeException>(() => Place.Chunk(world, Materials, Cell.OriginVoxel.Chunk, Computer));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Place.Machine(world, Materials, Cell, Computer, BrushFacing.PositiveX, new MachineSet(Materials)));

        Assert.Empty(world.Writes);
    }

    [Fact]
    public void ABlockBesideTheItemStillPlaces()
    {
        // The refusal is the item's, not the table's: the same table places dirt and a pump.
        var world = new TestWorld();
        Assert.Equal(64, Place.Cube(world, Materials, Cell, Dirt).Placed);
        Assert.Equal(64, Place.Machine(world, Materials, Cell.Offset(2, 0, 0), Pump, BrushFacing.PositiveX, new MachineSet(Materials)).Placed);
    }
}
