using System.Globalization;
using Overburden.Core.Content;
using Overburden.Core.Inventory;

namespace Overburden.Core.Tests;

/// <summary>
/// <c>P</c> (2026-10-12, his ask): a hundred of each item on every press, in the unit the hotbar counts
/// it in, the lithium-ion pack beside the lead-acid, and all or nothing. And <c>Ctrl+P</c>'s one pack.
/// </summary>
public class DebugSeedTests
{
    private static Registry<MaterialDefinition> Shipped()
    {
        var content = new GameContent();
        LoadResult result = content.Load(new DirectoryContentSource(ShippedContentTests.ModsDirectory()));
        Assert.True(result.Success, string.Join("; ", result.Errors));
        return content.Materials;
    }

    private static int Id(Registry<MaterialDefinition> materials, string path) =>
        materials.GetNumericId(Identifier.Parse($"overburden:{path}"));

    [Fact]
    public void APressReadsAHundredOfEachOnTheHotbar()
    {
        Registry<MaterialDefinition> materials = Shipped();
        var inventory = new VolumetricInventory();

        Assert.True(DebugSeed.Press(materials, inventory, out Identifier? missing));
        Assert.Null(missing);
        Assert.Equal(DebugSeed.Materials.Count, inventory.MaterialCount);

        foreach (Identifier identifier in DebugSeed.Materials)
        {
            MaterialDefinition definition = materials.Get(identifier);
            long voxels = inventory.Voxels(materials.GetNumericId(identifier));
            switch (definition.Unit)
            {
                case MaterialUnit.Count:
                    Assert.Equal(100L, voxels);
                    break;
                case MaterialUnit.Cells:
                    Assert.Equal(100L * 64L, voxels);
                    break;
                default:
                    CultureInfo before = CultureInfo.CurrentCulture;
                    CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                    try
                    {
                        Assert.Equal("100 m³", VolumeText.OfVoxels(voxels));
                    }
                    finally
                    {
                        CultureInfo.CurrentCulture = before;
                    }

                    break;
            }
        }

        // By name, so a unit edited in a JSON file cannot quietly move what P gives.
        Assert.Equal(100L, inventory.Voxels(Id(materials, "lithium_ion_pack")));
        Assert.Equal(100L, inventory.Voxels(Id(materials, "lead_acid_pack")));
        Assert.Equal(100L, inventory.Voxels(Id(materials, "computer")));
        Assert.Equal(6_400L, inventory.Voxels(Id(materials, "relay_pump")));
        Assert.Equal(6_400L, inventory.Voxels(Id(materials, "relay_outlet")));
        Assert.Equal(6_400L, inventory.Voxels(Id(materials, "solar_panel")));
        Assert.Equal(6_400L, inventory.Voxels(Id(materials, "vacuum_cell")));
        Assert.Equal(6_400L, inventory.Voxels(Id(materials, "clay")));
        Assert.Equal(6_400L, inventory.Voxels(Id(materials, "glass")));
    }

    [Fact]
    public void EveryPressAddsAHundredMore()
    {
        // His choice over topping up: the computer and the packs are no longer once.
        Registry<MaterialDefinition> materials = Shipped();
        var inventory = new VolumetricInventory();

        Assert.True(DebugSeed.Press(materials, inventory, out _));
        inventory.Take(Id(materials, "glass"), 64);
        Assert.True(DebugSeed.Press(materials, inventory, out _));

        Assert.Equal(200L, inventory.Voxels(Id(materials, "lithium_ion_pack")));
        Assert.Equal(200L, inventory.Voxels(Id(materials, "computer")));
        Assert.Equal(200L * 64L, inventory.Voxels(Id(materials, "relay_pump")));
        Assert.Equal(199L * 64L, inventory.Voxels(Id(materials, "glass")));
    }

    [Fact]
    public void AContentMissingAnyItemSeedsNothing()
    {
        // All or nothing, whichever item is missing: a half seed reads as a bug in the missing one.
        for (int skip = 0; skip < DebugSeed.Materials.Count; skip++)
        {
            var registry = new Registry<MaterialDefinition>("materials", MaterialDefinition.Unknown);
            registry.Register(MaterialIds.Air, new MaterialDefinition { Id = MaterialIds.Air, IsOpaque = false, IsReplaceable = true });
            for (int i = 0; i < DebugSeed.Materials.Count; i++)
            {
                if (i != skip)
                {
                    registry.Register(DebugSeed.Materials[i], new MaterialDefinition { Id = DebugSeed.Materials[i] });
                }
            }

            registry.Freeze();
            var inventory = new VolumetricInventory();

            Assert.False(DebugSeed.Press(registry, inventory, out Identifier? missing));
            Assert.Equal(DebugSeed.Materials[skip], missing);
            Assert.Equal(0, inventory.MaterialCount);
        }
    }

    [Fact]
    public void AHundredIsAPieceABodyOrACubicMetreEach()
    {
        Assert.Equal(1L, DebugSeed.VoxelsPerUnit(MaterialUnit.Count));
        Assert.Equal(64L, DebugSeed.VoxelsPerUnit(MaterialUnit.Cells));
        Assert.Equal(64L, DebugSeed.VoxelsPerUnit(MaterialUnit.Litres));
        Assert.Equal(100, DebugSeed.Each);
    }

    /// <summary>
    /// The alpha's own path (session C's pass, fact 6): the hand pick's grass and dirt, then P. The
    /// blocks and machines land in the empty slots in P's order, the computer and the packs stay
    /// off the row (decision C3), and the first stone a quantum tool takes still finds slot 9.
    /// </summary>
    [Fact]
    public void APressFillsAnEmptyRowWithItsBlocksAndMachinesInOrderAndLeavesSlotNineForStone()
    {
        Registry<MaterialDefinition> materials = Shipped();
        var inventory = new VolumetricInventory();
        inventory.KeepOffTheRow(MaterialTable.From(materials).Items());

        inventory.Add(Id(materials, "grass"), 12);
        inventory.Add(Id(materials, "dirt"), 30);
        Assert.True(DebugSeed.Press(materials, inventory, out _));

        string[] row = [.. Enumerable.Range(0, HotbarSlots.Count)
            .Select(inventory.MaterialInSlot)
            .Select(id => id < 0 ? "-" : materials.GetIdentifier(id).Path)];
        Assert.Equal(["grass", "dirt", "relay_pump", "relay_outlet", "solar_panel", "vacuum_cell", "clay", "glass", "-"], row);

        inventory.Add(Id(materials, "stone"), 64);
        Assert.Equal(Id(materials, "stone"), inventory.MaterialInSlot(8));

        foreach (string item in new[] { "computer", "lead_acid_pack", "lithium_ion_pack" })
        {
            Assert.Equal(100, inventory.Voxels(Id(materials, item)));
            Assert.False(inventory.Hotbar.Contains(Id(materials, item)), $"{item} took a slot by itself");
        }
    }

    /// <summary>
    /// <c>Ctrl+P</c> (2026-11-04, his ask): <i>"ctrl+p should give one lithium pack unless you are
    /// already wearing one"</i>. One of it, and nothing else.
    /// </summary>
    [Fact]
    public void CtrlPGivesExactlyOneLithiumPackAndNothingElse()
    {
        Registry<MaterialDefinition> materials = Shipped();
        var inventory = new VolumetricInventory();

        Assert.Equal(OnePackOutcome.Given, DebugSeed.PressOnePack(materials, inventory, wornMaterialId: null));

        Assert.Equal(1L, inventory.Voxels(Id(materials, "lithium_ion_pack")));
        Assert.Equal(1L, inventory.TotalVoxels);
        Assert.Equal(1, inventory.MaterialCount);
    }

    [Fact]
    public void CtrlPGivesNothingWhileALithiumPackIsWorn()
    {
        Registry<MaterialDefinition> materials = Shipped();
        var inventory = new VolumetricInventory();
        int lithium = Id(materials, "lithium_ion_pack");

        Assert.Equal(OnePackOutcome.AlreadyWearing, DebugSeed.PressOnePack(materials, inventory, lithium));
        Assert.Equal(0, inventory.MaterialCount);

        // A repeat press refuses again, and refusing leaves what's carried alone.
        inventory.Add(lithium, 2);
        Assert.Equal(OnePackOutcome.AlreadyWearing, DebugSeed.PressOnePack(materials, inventory, lithium));
        Assert.Equal(OnePackOutcome.AlreadyWearing, DebugSeed.PressOnePack(materials, inventory, lithium));
        Assert.Equal(2L, inventory.Voxels(lithium));
    }

    [Theory]
    [InlineData("lead_acid_pack")]
    [InlineData("quantum_backpack")]
    public void CtrlPGivesOneWhileAnotherKindIsWorn(string worn)
    {
        Registry<MaterialDefinition> materials = Shipped();
        var inventory = new VolumetricInventory();

        Assert.Equal(OnePackOutcome.Given, DebugSeed.PressOnePack(materials, inventory, Id(materials, worn)));

        Assert.Equal(1L, inventory.Voxels(Id(materials, "lithium_ion_pack")));
        Assert.Equal(0L, inventory.Voxels(Id(materials, worn)));
    }

    [Fact]
    public void ACarriedLithiumPackIsNotAWornOneSoEveryPressAddsAnother()
    {
        // Only the back counts: P's hundred in the pocket doesn't stop Ctrl+P, nor does the last press.
        Registry<MaterialDefinition> materials = Shipped();
        var inventory = new VolumetricInventory();
        int lithium = Id(materials, "lithium_ion_pack");

        Assert.True(DebugSeed.Press(materials, inventory, out _));
        for (int press = 1; press <= 3; press++)
        {
            Assert.Equal(OnePackOutcome.Given, DebugSeed.PressOnePack(materials, inventory, wornMaterialId: null));
            Assert.Equal(100L + press, inventory.Voxels(lithium));
        }
    }

    [Fact]
    public void AGivenPackStaysOffTheRow()
    {
        Registry<MaterialDefinition> materials = Shipped();
        var inventory = new VolumetricInventory();
        inventory.KeepOffTheRow(MaterialTable.From(materials).Items());

        Assert.Equal(OnePackOutcome.Given, DebugSeed.PressOnePack(materials, inventory, wornMaterialId: null));

        Assert.False(inventory.Hotbar.Contains(Id(materials, "lithium_ion_pack")));
    }

    [Fact]
    public void ACarriedPackHoldsItsChargeThroughAGivenOne()
    {
        // The given pack is a bare count: the equip still takes the charged one, and the given one is
        // what's left, empty on channel 0.
        Registry<MaterialDefinition> materials = Shipped();
        var inventory = new VolumetricInventory();
        var carried = new CarriedPacks();
        int lithium = Id(materials, "lithium_ion_pack");
        inventory.Add(lithium, 1);
        Assert.True(carried.Stow(new Persistence.PackSave(lithium, 5_000, 3)));

        Assert.Equal(OnePackOutcome.Given, DebugSeed.PressOnePack(materials, inventory, wornMaterialId: null));

        Assert.Equal(2L, inventory.Voxels(lithium));
        Assert.Equal(5_000L, carried.TakeFullest(lithium)?.ChargeMillijoules);
        Assert.Null(carried.TakeFullest(lithium));
    }

    [Fact]
    public void AContentWithoutTheLithiumPackGivesNothing()
    {
        var registry = new Registry<MaterialDefinition>("materials", MaterialDefinition.Unknown);
        registry.Register(MaterialIds.Air, new MaterialDefinition { Id = MaterialIds.Air, IsOpaque = false, IsReplaceable = true });
        foreach (Identifier identifier in DebugSeed.Materials.Where(i => i != DebugSeed.OnePack))
        {
            registry.Register(identifier, new MaterialDefinition { Id = identifier });
        }

        registry.Freeze();
        var inventory = new VolumetricInventory();

        Assert.Equal(OnePackOutcome.NotInContent, DebugSeed.PressOnePack(registry, inventory, wornMaterialId: null));
        Assert.Equal(0, inventory.MaterialCount);
    }

    [Fact]
    public void AHoleWhereTheLithiumPackWasGivesNothing()
    {
        // The world's table keeps a removed mod's numeric ID, and Freeze puts a placeholder behind it.
        var registry = new Registry<MaterialDefinition>("materials", MaterialDefinition.Unknown);
        registry.Register(MaterialIds.Air, new MaterialDefinition { Id = MaterialIds.Air, IsOpaque = false, IsReplaceable = true });
        registry.ReserveId(DebugSeed.OnePack);
        registry.Freeze();
        var inventory = new VolumetricInventory();

        Assert.Equal(OnePackOutcome.NotInContent, DebugSeed.PressOnePack(registry, inventory, wornMaterialId: null));
        Assert.Equal(0, inventory.MaterialCount);
    }

    [Fact]
    public void TheKitStillHoldsThePackCtrlPGives()
    {
        Assert.Contains(DebugSeed.OnePack, DebugSeed.Materials);
        Assert.Equal(Identifier.Parse("overburden:lithium_ion_pack"), DebugSeed.OnePack);
    }

    /// <summary>What keeps off the row by itself in the shipped content: the five materials nothing places.</summary>
    [Fact]
    public void TheShippedItemsAreTheComputerThePacksAndWater()
    {
        Registry<MaterialDefinition> materials = Shipped();

        string[] items = [.. MaterialTable.From(materials).Items().Select(id => materials.GetIdentifier(id).Path).Order()];

        Assert.Equal(["computer", "lead_acid_pack", "lithium_ion_pack", "quantum_backpack", "water"], items);
    }
}
