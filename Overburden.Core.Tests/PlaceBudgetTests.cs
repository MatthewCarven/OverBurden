using System.Numerics;
using Overburden.Core.Building;
using Overburden.Core.Inventory;
using Overburden.Core.Physics;
using Overburden.Core.Voxels;
using static Overburden.Core.Tests.PhysicsTestHelpers;

namespace Overburden.Core.Tests;

/// <summary>
/// The fill's voxel budget (2026-09-20): the bite's rule taken by the other verb. A shape at
/// range lands what the pack can pay for and stops, the rest in
/// <see cref="PlaceResult.Unpowered"/>, on every shape through the one <c>Put</c>. Until this
/// date the fill was refused whole under the shape's cost (M10c-3, decision 6), which for the
/// excavator's chunk was 1.64 MJ or nothing.
/// </summary>
/// <remarks>
/// <b>The budget is the last refusal</b>, after the stock — a voxel the brush would not fill
/// for any other reason never reaches it, so the pack pays for what landed and nothing else.
/// <see cref="StockIsAskedBeforeTheBudgetAndTheFixtureThatSeparatesThemHasBothRunOutAtOnce"/>
/// pins the order with the one fixture that can see it.
/// </remarks>
public class PlaceBudgetTests
{
    private const int Cell = BrushCell.Voxels * BrushCell.Voxels * BrushCell.Voxels;

    private static PlaceResult Accounted(PlaceResult result)
    {
        Assert.True(result.IsAccounted, $"buckets do not add up: {result}");
        return result;
    }

    [Fact]
    public void ABudgetOfFortyOnACubeLandsFortyAndTheRestIsUnpowered()
    {
        // The OutOfStock rule a second time: a partial shape rather than none, the bucket saying
        // what it was short, and the inventory debited for exactly what landed.
        var world = new TestWorld();
        var inventory = new VolumetricInventory();
        inventory.Add(Stone, Cell);

        PlaceResult result = Accounted(
            Place.Cube(world, Table, new BrushCell(0, 0, 0), Stone, from: inventory, voxelBudget: 40));

        Assert.Equal(40, result.Placed);
        Assert.Equal(24, result.Unpowered);
        Assert.Equal(0, result.OutOfStock);
        Assert.Equal(Cell, result.Candidates);
        Assert.Equal(40, world.Writes.Count);
        Assert.Equal(Cell - 40, inventory.Voxels(Stone));
    }

    [Fact]
    public void ABudgetOfZeroPlacesNothingAndEveryVoxelIsUnpowered()
    {
        // A click with nothing in the pack still runs the shape — a click is a click — and
        // writes nothing.
        var world = new TestWorld();

        PlaceResult result = Accounted(Place.Cube(world, Table, new BrushCell(0, 0, 0), Stone, voxelBudget: 0));

        Assert.Equal(Cell, result.Unpowered);
        Assert.Equal(0, result.Placed);
        Assert.Empty(world.Writes);
    }

    [Fact]
    public void NoBudgetIsTheOldBehaviourToTheVoxel()
    {
        // Every caller that sets none — the brush inside the arm's reach, every test before this
        // file — places as it did, with Unpowered at zero.
        var world = new TestWorld();

        PlaceResult result = Accounted(Place.Cube(world, Table, new BrushCell(0, 0, 0), Stone));

        Assert.Equal(Cell, result.Placed);
        Assert.Equal(0, result.Unpowered);
    }

    [Fact]
    public void OccupiedAndInsideThePlayerCostNothingAgainstTheBudget()
    {
        // Half the cell is rock and a budget of exactly 32 buys the air half — the one fixture
        // that separates "the budget is asked last" from "the budget is asked first": asked
        // first, the occupied voxels the loop meets after the 32nd would spend nothing but the
        // air voxels before them would have read Unpowered once the rock had eaten the count.
        var cell = new BrushCell(0, 0, 0);
        TestWorld world = new() { Background = v => v.X >= cell.VoxelAt(2, 0, 0).X ? Stone : Air };

        PlaceResult result = Accounted(Place.Cube(world, Table, cell, Dirt, voxelBudget: 32));

        Assert.Equal(32, result.Placed);
        Assert.Equal(32, result.Occupied);
        Assert.Equal(0, result.Unpowered);

        // And the body: a cube around a body standing in its bottom row, budget exactly the
        // voxels outside the body, lands all of them and reports none unpowered.
        var open = new TestWorld();
        var body = new Aabb(new Vector3(0.25f, 0.0f, 0.25f), new Vector3(0.5f, 0.5f, 0.5f));
        PlaceResult around = Accounted(Place.Cube(open, Table, cell, Dirt, body: body));
        int outside = around.Placed;
        Assert.InRange(outside, 1, Cell - 1);

        var again = new TestWorld();
        PlaceResult budgeted = Accounted(Place.Cube(again, Table, cell, Dirt, body: body, voxelBudget: outside));
        Assert.Equal(outside, budgeted.Placed);
        Assert.Equal(around.InsidePlayer, budgeted.InsidePlayer);
        Assert.Equal(0, budgeted.Unpowered);
    }

    [Fact]
    public void StockIsAskedBeforeTheBudgetAndTheFixtureThatSeparatesThemHasBothRunOutAtOnce()
    {
        // Stock 5 and budget 5: after the fifth voxel both are spent, and the sixth is refused
        // by whichever is asked first. The stock is — the cheaper question, and the one that
        // has nothing to do with the pack — so the rest reads OutOfStock and none Unpowered.
        // With either alone larger the two orders give the same buckets, so this is the fixture.
        var world = new TestWorld();
        var inventory = new VolumetricInventory();
        inventory.Add(Stone, 5);

        PlaceResult result = Accounted(
            Place.Cube(world, Table, new BrushCell(0, 0, 0), Stone, from: inventory, voxelBudget: 5));

        Assert.Equal(5, result.Placed);
        Assert.Equal(Cell - 5, result.OutOfStock);
        Assert.Equal(0, result.Unpowered);
    }

    [Fact]
    public void EveryShapeTakesTheBudget()
    {
        // A budget of zero on every shape in an empty world: nothing lands and every candidate
        // is Unpowered. This is the recipe that catches one Put call site left without its
        // ref — the shape would place its whole self while the others stop.
        var cell = new BrushCell(2, 3, 4);
        var chunk = new ChunkPos(1, 1, 1);

        var shapes = new (string Name, Func<TestWorld, PlaceResult> Run)[]
        {
            ("voxel", w => Place.Voxel(w, Table, cell.VoxelAt(1, 1, 1), Stone, voxelBudget: 0)),
            ("cube", w => Place.Cube(w, Table, cell, Stone, voxelBudget: 0)),
            ("slab", w => Place.Slab(w, Table, cell, Stone, voxelBudget: 0)),
            ("ramp", w => Place.Ramp(w, Table, cell, Stone, BrushFacing.PositiveX, voxelBudget: 0)),
            ("sphere", w => Place.Sphere(w, Table, cell, 1.0f, Stone, voxelBudget: 0)),
            ("chunk", w => Place.Chunk(w, Table, chunk, Stone, voxelBudget: 0)),
        };

        foreach ((string name, Func<TestWorld, PlaceResult> run) in shapes)
        {
            var world = new TestWorld();
            PlaceResult result = Accounted(run(world));
            Assert.True(result.Candidates > 0, name);
            Assert.True(result.Placed == 0, $"{name}: {result}");
            Assert.True(result.Unpowered == result.Candidates, $"{name}: {result}");
            Assert.Empty(world.Writes);
        }
    }

    [Fact]
    public void TheBudgetIsSpentInTheWalksOwnOrderSoARepeatedFillContinuesWhereItStopped()
    {
        // Two clicks of 32 land the same 64 voxels one click of 64 does, because the second
        // click's first 32 candidates are Occupied — cost nothing — and its budget buys the
        // next 32. The excavator's chunk in two packs, at cell scale.
        var cell = new BrushCell(0, 0, 0);
        var whole = new TestWorld();
        Accounted(Place.Cube(whole, Table, cell, Stone));

        var halves = new TestWorld();
        PlaceResult first = Accounted(Place.Cube(halves, Table, cell, Stone, voxelBudget: 32));
        PlaceResult second = Accounted(Place.Cube(halves, Table, cell, Stone, voxelBudget: 32));

        Assert.Equal(32, first.Placed);
        Assert.Equal(32, first.Unpowered);
        Assert.Equal(32, second.Placed);
        Assert.Equal(32, second.Occupied);
        Assert.Equal(0, second.Unpowered);
        Assert.Equal(whole.WrittenVoxels, halves.WrittenVoxels);
    }

    [Fact]
    public void TheResultNamesUnpoweredOnlyWhenItIsNonZero()
    {
        // The string every profile line and test failure prints: unchanged for every result
        // that has no budget, the new term appended when it has.
        Assert.DoesNotContain("unpowered", new PlaceResult(64, 64, 0, 0, 0).ToString());
        Assert.Contains("unpowered 3", new PlaceResult(64, 61, 0, 0, 0, Unpowered: 3).ToString());
        Assert.False(new PlaceResult(64, 61, 0, 0, 0).IsAccounted);
        Assert.True(new PlaceResult(64, 61, 0, 0, 0, Unpowered: 3).IsAccounted);
    }
}
