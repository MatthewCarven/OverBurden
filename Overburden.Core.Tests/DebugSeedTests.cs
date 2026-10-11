using System.Globalization;
using Overburden.Core.Content;
using Overburden.Core.Inventory;

namespace Overburden.Core.Tests;

/// <summary>
/// <c>P</c> (2026-10-12, his ask): a hundred of each item on every press, in the unit the hotbar counts
/// it in, the lithium-ion pack beside the lead-acid, and all or nothing.
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
}
