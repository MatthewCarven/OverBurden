using System.Numerics;
using MinerCraft.Core.Building;
using MinerCraft.Core.Inventory;
using MinerCraft.Core.Mining;
using MinerCraft.Core.Voxels;
using static MinerCraft.Core.Tests.PhysicsTestHelpers;

namespace MinerCraft.Core.Tests;

/// <summary>
/// The volumetric inventory, and the two verbs that move material through it.
/// </summary>
/// <remarks>
/// <b>Mining credits it per material and placement debits it per voxel</b>, which is the pairing
/// worth testing rather than either half alone: a swing through a dirt bank with a stone seam in
/// it takes two materials, and <see cref="CarveResult.Removed"/> is one number — so anything that
/// credited from the result rather than from the removal would have to guess which.
/// </remarks>
public class InventoryTests
{
    /// <summary>A tool that takes anything, so a test can be about the inventory.</summary>
    private const float TakesAnything = 100.0f;

    private static PlaceResult Accounted(PlaceResult result)
    {
        Assert.True(result.IsAccounted, $"buckets do not add up: {result}");
        return result;
    }

    // -------------------------------------------------------------------------- the unit

    [Fact]
    public void ABrushCubeIsExactlyOneThousandLitres()
    {
        // The round number the unit exists for: 64 voxels of 0.25 m is one cubic metre, so a
        // player can compare a hole with a stack without arithmetic. If this ever stops being
        // exact, the unit has stopped being worth having.
        var inventory = new VolumetricInventory();
        inventory.Add(Stone, 64);

        Assert.Equal(1000.0, inventory.Litres(Stone));
        Assert.Equal(15.625, VolumetricInventory.LitresPerVoxel);
        Assert.Equal(1000.0, inventory.TotalLitres);
    }

    [Fact]
    public void TakingMoreThanIsHeldTakesWhatThereWasAndSaysSo()
    {
        var inventory = new VolumetricInventory();
        inventory.Add(Stone, 10);

        Assert.Equal(10, inventory.Take(Stone, 25));
        Assert.Equal(0, inventory.Voxels(Stone));

        // And a material at zero is not held: the save file never accumulates rows for materials
        // nobody is carrying.
        Assert.Equal(0, inventory.MaterialCount);
        Assert.Empty(inventory.Entries());
    }

    [Fact]
    public void EntriesComeBackInMaterialOrderWhateverOrderTheyArrivedIn()
    {
        // So that the same holding always writes the same bytes. A dictionary's enumeration order
        // is not a contract, and a save whose bytes shuffle between runs makes every "did this
        // change?" question unanswerable.
        var inventory = new VolumetricInventory();
        inventory.Add(Stone, 3);
        inventory.Add(Dirt, 5);
        inventory.Add(Water, 7);

        Assert.Equal(
            [(Dirt, 5L), (Water, 7L), (Stone, 3L)],
            inventory.Entries().OrderBy(e => e.MaterialId).ToArray());

        Assert.Equal(
            inventory.Entries().Select(e => e.MaterialId).ToArray(),
            inventory.Entries().Select(e => e.MaterialId).Order().ToArray());
    }

    [Fact]
    public void AirIsNotAVolumeAndIsRefused()
    {
        // Nothing can mine air — Carve buckets it as AlreadyAir before the removal — so an air
        // deposit is a caller that has lost track of what it removed, and accepting it would put
        // a number in the save file that no amount of play could explain.
        var inventory = new VolumetricInventory();

        Assert.Throws<ArgumentOutOfRangeException>(() => inventory.Add(Air, 1));
    }

    [Fact]
    public void AddNeverRemovesAndTakeNeverCredits()
    {
        var inventory = new VolumetricInventory();

        Assert.Throws<ArgumentOutOfRangeException>(() => inventory.Add(Stone, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => inventory.Take(Stone, -1));
    }

    // ------------------------------------------------------------------------ mining credits

    [Fact]
    public void ASwingThroughTwoMaterialsCreditsEachOfThemSeparately()
    {
        // The case that decides where the credit happens. Removed is one number; the bite took
        // two materials. Anything crediting from the result would have to guess.
        TestWorld world = new() { Background = v => v.X < 20 ? Dirt : Stone };
        var inventory = new VolumetricInventory();

        (float x, float y, float z) = new VoxelPos(20, 0, 0).CentreMetres;
        CarveResult carved = Carve.Sphere(
            world, Table, new Vector3(x, y, z), 0.5f, TakesAnything, inventory);

        Assert.True(inventory.Voxels(Dirt) > 0, "the swing should have taken some dirt");
        Assert.True(inventory.Voxels(Stone) > 0, "the swing should have taken some stone");
        Assert.Equal(carved.Removed, inventory.TotalVoxels);
    }

    [Fact]
    public void AVoxelTheToolCannotTakeIsNotCredited()
    {
        // The hardness gate seen from the inventory: a tier that leaves the seam standing must
        // not be paid for it. Dirt is 0.8 and stone is 3.0 in the test table.
        TestWorld world = new() { Background = v => v.X < 20 ? Dirt : Stone };
        var inventory = new VolumetricInventory();

        (float x, float y, float z) = new VoxelPos(20, 0, 0).CentreMetres;
        CarveResult carved = Carve.Sphere(
            world, Table, new Vector3(x, y, z), 0.5f, hardnessThreshold: 1.0f, inventory);

        Assert.True(carved.TooHard > 0, "the fixture left no stone standing; the test is vacuous");
        Assert.Equal(0, inventory.Voxels(Stone));
        Assert.Equal(carved.Removed, inventory.Voxels(Dirt));
    }

    [Fact]
    public void AirInsideTheBiteIsNotCredited()
    {
        // A sphere centred on a surface is roughly half air, so this is the common case rather
        // than an edge one — and air passes every hardness threshold there is.
        TestWorld world = FlatGround();
        var inventory = new VolumetricInventory();

        CarveResult carved = Carve.Sphere(
            world, Table, new Vector3(2.0f, 1.0f, 2.0f), 0.5f, TakesAnything, inventory);

        Assert.True(carved.AlreadyAir > 0, "the fixture found no air; the test is vacuous");
        Assert.Equal(carved.Removed, inventory.TotalVoxels);
        Assert.Equal(0, inventory.Voxels(Air));
    }

    [Fact]
    public void MiningIntoNothingIsStillMining()
    {
        // The null inventory is the default and every pre-M6c caller uses it. It must not change
        // what a carve does, only where the material goes.
        TestWorld world = SolidStone();

        CarveResult withInventory = Carve.Sphere(
            world, Table, new Vector3(4.0f, 4.0f, 4.0f), 0.5f, TakesAnything, new VolumetricInventory());

        TestWorld other = SolidStone();
        CarveResult without = Carve.Sphere(
            other, Table, new Vector3(4.0f, 4.0f, 4.0f), 0.5f, TakesAnything);

        Assert.Equal(without, withInventory);
    }

    // ---------------------------------------------------------------------- placement debits

    [Fact]
    public void APlacedBrushIsPaidForVoxelByVoxel()
    {
        TestWorld world = new();
        var inventory = new VolumetricInventory();
        inventory.Add(Stone, 100);

        PlaceResult result = Accounted(
            Place.Cube(world, Table, new BrushCell(0, 0, 0), Stone, body: null, from: inventory));

        Assert.Equal(64, result.Placed);
        Assert.Equal(0, result.OutOfStock);
        Assert.Equal(36, inventory.Voxels(Stone));
    }

    [Fact]
    public void AShortInventoryPlacesWhatItCanAndReportsTheRest()
    {
        // A partial brush rather than none: running out halfway through a wall leaves the part
        // you could afford, and the bucket itself says what you were short.
        TestWorld world = new();
        var inventory = new VolumetricInventory();
        inventory.Add(Stone, 40);

        PlaceResult result = Accounted(
            Place.Cube(world, Table, new BrushCell(0, 0, 0), Stone, body: null, from: inventory));

        Assert.Equal(40, result.Placed);
        Assert.Equal(24, result.OutOfStock);
        Assert.Equal(64, result.Candidates);
        Assert.Equal(0, inventory.Voxels(Stone));
        Assert.Equal(40, world.Writes.Count);
    }

    [Fact]
    public void AnEmptyInventoryPlacesNothingAtAll()
    {
        TestWorld world = new();

        PlaceResult result = Accounted(Place.Cube(
            world, Table, new BrushCell(0, 0, 0), Stone, body: null, from: new VolumetricInventory()));

        Assert.Equal(64, result.OutOfStock);
        Assert.Equal(0, result.Placed);
        Assert.Empty(world.Writes);
    }

    [Fact]
    public void VoxelsTheBrushCouldNotFillAreNotChargedFor()
    {
        // Stock is consulted last on purpose. Charging before the voxel is known to be fillable
        // would mean a wall built against a cliff cost the volume of the cliff.
        //
        // **Exactly enough stock for the half that is air**, which is the only fixture that
        // separates the two orders — the battery found that with plenty in hand, consulting the
        // stock first changes nothing and the test passed either way. With exactly 32, checking
        // first empties the inventory partway through and the occupied voxels the loop meets
        // after that come back OutOfStock instead of Occupied.
        var cell = new BrushCell(0, 0, 0);
        TestWorld world = new() { Background = v => v.X >= cell.VoxelAt(2, 0, 0).X ? Stone : Air };
        var inventory = new VolumetricInventory();
        inventory.Add(Dirt, 32);

        PlaceResult result = Accounted(
            Place.Cube(world, Table, cell, Dirt, body: null, from: inventory));

        Assert.Equal(32, result.Placed);
        Assert.Equal(32, result.Occupied);
        Assert.Equal(0, result.OutOfStock);
        Assert.Equal(0, inventory.Voxels(Dirt));
    }

    [Fact]
    public void AWriteThatFailsCostsNothing()
    {
        // The debit follows the write rather than preceding it, so a brush that reached past the
        // loaded world is not paid for.
        TestWorld world = new();
        var cell = new BrushCell(0, 0, 0);
        world.RefuseWrites(cell.OriginVoxel.Chunk);

        var inventory = new VolumetricInventory();
        inventory.Add(Stone, 64);

        PlaceResult result = Accounted(
            Place.Cube(world, Table, cell, Stone, body: null, from: inventory));

        Assert.Equal(64, result.Unreachable);
        Assert.Equal(64, inventory.Voxels(Stone));
    }

    [Fact]
    public void MiningAndThenBuildingWithWhatYouMinedComesOutEven()
    {
        // The round trip the whole feature is for, and the one number that ties the two verbs
        // together: a cubic metre out of the world is a cubic metre back into it.
        TestWorld world = SolidStone();
        var inventory = new VolumetricInventory();

        var cell = new BrushCell(1, 1, 1);
        for (int lz = 0; lz < BrushCell.Voxels; lz++)
        for (int lx = 0; lx < BrushCell.Voxels; lx++)
        for (int ly = 0; ly < BrushCell.Voxels; ly++)
        {
            Carve.Voxel(world, Table, cell.VoxelAt(lx, ly, lz), TakesAnything, inventory);
        }

        Assert.Equal(64, inventory.Voxels(Stone));
        Assert.Equal(1000.0, inventory.Litres(Stone));

        PlaceResult refilled = Accounted(
            Place.Cube(world, Table, cell, Stone, body: null, from: inventory));

        Assert.Equal(64, refilled.Placed);
        Assert.Equal(0, inventory.TotalVoxels);
    }

    private static TestWorld SolidStone() => new() { Background = _ => Stone };
}
