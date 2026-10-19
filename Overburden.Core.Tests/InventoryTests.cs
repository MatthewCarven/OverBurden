using System.Numerics;
using Overburden.Core.Building;
using Overburden.Core.Content;
using Overburden.Core.Inventory;
using Overburden.Core.Mining;
using Overburden.Core.Voxels;
using static Overburden.Core.Tests.PhysicsTestHelpers;

namespace Overburden.Core.Tests;

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

    // ------------------------------------------- the hotbar (M6d-3; slots that stay put, alpha C)

    /// <summary>Materials past the helpers' three, for a row longer than they can fill. The inventory never reads the table.</summary>
    private const int Clay = 4, Glass = 5, Grass = 6, Pump = 7, Outlet = 8, Cell = 9, Panel = 10, Rubble = 11, Lamp = 12;

    /// <summary>The row as nine IDs, for asserting a whole row at once.</summary>
    private static int[] Row(VolumetricInventory inventory) =>
        [.. Enumerable.Range(0, HotbarSlots.Count).Select(inventory.MaterialInSlot)];

    [Fact]
    public void AnEmptyInventoryHasNoSlots()
    {
        var inventory = new VolumetricInventory();

        Assert.Equal(-1, inventory.MaterialInSlot(0));
        Assert.Equal(-1, inventory.MaterialInSlot(8));
    }

    /// <summary>
    /// A slot past the end, and a negative slot, are empty rather than an exception.
    /// </summary>
    /// <remarks>
    /// The caller is a key press. Pressing 9 while carrying two materials is an ordinary thing a
    /// player does, not a caller error, and a throw there would take the frame down.
    /// </remarks>
    [Fact]
    public void ASlotOutsideTheRowIsEmptyRatherThanAThrow()
    {
        var inventory = new VolumetricInventory();
        inventory.Add(Stone, 1);

        Assert.Equal(-1, inventory.MaterialInSlot(1));
        Assert.Equal(-1, inventory.MaterialInSlot(-1));
        Assert.Equal(-1, inventory.MaterialInSlot(int.MaxValue));
    }

    /// <summary>
    /// A new material takes the first empty slot, in the order things arrive — not ID order, which
    /// was M6d-3's row and shifted every slot after a newcomer.
    /// </summary>
    [Fact]
    public void ANewMaterialTakesTheFirstEmptySlotInTheOrderItArrives()
    {
        var inventory = new VolumetricInventory();

        inventory.Add(Stone, 10);
        inventory.Add(Dirt, 5);

        Assert.Equal(Stone, inventory.MaterialInSlot(0));
        Assert.Equal(Dirt, inventory.MaterialInSlot(1));
        Assert.Equal(-1, inventory.MaterialInSlot(2));
    }

    /// <summary>
    /// Spending the last of a material keeps its slot: nothing above it shifts, and the next bite
    /// lands back in it.
    /// </summary>
    /// <remarks>
    /// <b>The inversion of M6d-3's <c>EmptyingAMaterialShiftsEverySlotAboveIt</c></b>, which asserted
    /// the shift as the documented cost of the ID-order row and said that if it were ever "fixed"
    /// the rule had changed. It has: alpha session C, decision C2.
    /// </remarks>
    [Fact]
    public void SpendingTheLastOfAMaterialKeepsItsSlotAndTheNextBiteLandsBackInIt()
    {
        var inventory = new VolumetricInventory();
        inventory.Add(Dirt, 1);
        inventory.Add(Stone, 1);

        inventory.Take(Dirt, 1);

        Assert.False(inventory.Has(Dirt));
        Assert.Equal(Dirt, inventory.MaterialInSlot(0));
        Assert.Equal(Stone, inventory.MaterialInSlot(1));

        inventory.Add(Glass, 3);
        Assert.Equal(Glass, inventory.MaterialInSlot(2));

        inventory.Add(Dirt, 2);
        Assert.Equal([Dirt, Stone, Glass, -1, -1, -1, -1, -1, -1], Row(inventory));
    }

    /// <summary>
    /// The swap's 1 → 0 → 1: wearing your last pack of a kind and taking the worn one back leaves
    /// the kind where it was. Under the ID-order row the same call shifted every slot after it and
    /// shifted them back.
    /// </summary>
    [Fact]
    public void ASwapThatSpendsTheLastPackAndHandsOneBackLeavesItsSlotWhereItWas()
    {
        var inventory = new VolumetricInventory();
        inventory.Add(Stone, 64);
        inventory.Add(Pump, 1);
        inventory.Add(Glass, 64);
        int[] before = Row(inventory);

        Assert.Equal(1, inventory.Take(Pump, 1));
        Assert.Equal(before, Row(inventory));

        inventory.Add(Pump, 1);
        Assert.Equal(before, Row(inventory));
    }

    /// <summary>
    /// A full row turns a newcomer away and still carries it, and says so once a session for each.
    /// </summary>
    [Fact]
    public void AFullRowCarriesANewcomerOffTheRowAndNamesItOnce()
    {
        var inventory = new VolumetricInventory();
        foreach (int material in new[] { Dirt, Clay, Glass, Grass, Pump, Outlet, Cell, Panel, Rubble })
        {
            inventory.Add(material, 1);
        }

        Assert.Equal(-1, inventory.Hotbar.FirstEmpty());
        inventory.Hotbar.DrainTurnedAway();

        inventory.Add(Stone, 640);

        Assert.Equal(640, inventory.Voxels(Stone));
        Assert.False(inventory.Hotbar.Contains(Stone));
        Assert.Equal([Stone], inventory.Hotbar.DrainTurnedAway());

        inventory.Add(Stone, 1);
        inventory.Add(Lamp, 1);
        Assert.Equal([Lamp], inventory.Hotbar.DrainTurnedAway());
    }

    /// <summary>
    /// The items — the computer, the packs — take a slot only by a pin (decision C3), so P's kit
    /// fills an empty row with blocks and machines and leaves room for the first stone.
    /// </summary>
    [Fact]
    public void AnItemKeptOffTheRowIsCarriedButTakesNoSlotUntilPinned()
    {
        const int Computer = 13, Pack = 14;
        var inventory = new VolumetricInventory();
        inventory.KeepOffTheRow([Computer, Pack]);

        inventory.Add(Computer, 100);
        inventory.Add(Pack, 100);
        inventory.Add(Glass, 6400);

        Assert.Equal([Glass, -1, -1, -1, -1, -1, -1, -1, -1], Row(inventory));
        Assert.Equal(100, inventory.Voxels(Computer));
        Assert.Empty(inventory.Hotbar.DrainTurnedAway());

        inventory.Hotbar.Pin(4, Pack);
        Assert.Equal(Pack, inventory.MaterialInSlot(4));

        // And once pinned it is on the row like anything else: worn to zero and handed back, it
        // stays in its slot.
        inventory.Take(Pack, 100);
        inventory.Add(Pack, 1);
        Assert.Equal(Pack, inventory.MaterialInSlot(4));
    }

    /// <summary>A material the pocket refuses (water) is never offered: the refusal comes first.</summary>
    [Fact]
    public void ARefusedMaterialIsNeverOfferedASlot()
    {
        var inventory = new VolumetricInventory();
        inventory.RefuseMaterials([Water]);

        inventory.Add(Water, 1000);

        Assert.Equal(HotbarSlots.EmptyRow, Row(inventory));
        Assert.Empty(inventory.Hotbar.DrainTurnedAway());
    }

    /// <summary>
    /// The load path offers nothing: re-crediting every holding through the offer would hand the
    /// empty slots out in ID order before the saved row could be put back.
    /// </summary>
    [Fact]
    public void AResetOffersNothingAndLeavesTheRowAlone()
    {
        var inventory = new VolumetricInventory();
        inventory.Hotbar.Restore([Glass, -1, Stone, -1, -1, -1, -1, -1, -1]);

        inventory.Reset([(Dirt, 5), (Stone, 10), (Glass, 7)]);

        Assert.Equal([Glass, -1, Stone, -1, -1, -1, -1, -1, -1], Row(inventory));
        Assert.Equal(5, inventory.Voxels(Dirt));
    }

    /// <summary>
    /// The row survives a save and reload with the holdings it names, including one held at zero,
    /// through the world file's own bytes.
    /// </summary>
    [Fact]
    public void TheRowIsUnchangedAcrossASaveAndReload()
    {
        var played = new VolumetricInventory();
        played.Add(Stone, 10);
        played.Add(Dirt, 5);
        played.Add(Glass, 3);
        played.Take(Dirt, 5);
        played.Hotbar.Pin(8, Glass);

        int[] before = Row(played);
        Assert.Equal([Stone, Dirt, -1, -1, -1, -1, -1, -1, Glass], before);

        Identifier[] table =
        [
            Overburden.Core.Content.MaterialIds.Air, .. Enumerable.Range(1, 12).Select(i => Identifier.Parse($"test:m{i}")),
        ];
        byte[] blob = Overburden.Core.Persistence.WorldHeaderBlob.Write(new Overburden.Core.Persistence.WorldHeader
        {
            Settings = new Overburden.Core.Worldgen.WorldGenSettings { Seed = 7 },
            MaterialIdTable = table,
            Inventory = [.. played.Entries().Select(e => new Overburden.Core.Persistence.InventoryEntry(e.MaterialId, e.Voxels))],
            Hotbar = played.Hotbar.ToArray(),
        });
        Overburden.Core.Persistence.WorldHeader read = Overburden.Core.Persistence.WorldHeaderBlob.Read(blob);

        var reopened = new VolumetricInventory();
        reopened.Reset(read.Inventory.Select(e => (e.MaterialId, e.Voxels)));
        reopened.Hotbar.Restore(read.Hotbar);

        Assert.Equal(before, Row(reopened));
        Assert.False(reopened.Has(Dirt));
    }

    private static TestWorld SolidStone() => new() { Background = _ => Stone };
}
