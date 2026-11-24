using Overburden.Core.Content;
using Overburden.Core.Machines;

namespace Overburden.Core.Tests;

/// <summary>
/// IM-6b's content (IDEAL-MACHINES-PLAN, "IM-6 — the code-level pass", decisions 6 and 11): the five vault grades,
/// the crate and the three siphons, loaded from the shipped files through the real loader and pinned to the grade
/// table, so a number edited in a JSON file cannot quietly move what a vault bills or a siphon costs.
/// </summary>
public class StoreContentTests
{
    private static GameContent Shipped()
    {
        var content = new GameContent();
        LoadResult result = content.Load(new DirectoryContentSource(ShippedContentTests.ModsDirectory()));
        Assert.True(result.Success, string.Join("; ", result.Errors));
        return content;
    }

    public static TheoryData<string, long, long, long, int> Grades => new()
    {
        // grade, inside m³, water mW a m³, blocks mW a m³, texture layer — the grade table over its volumes.
        { "low", 100, 25_000, 2_500, 15 },
        { "medium", 1_000, 10_000, 1_000, 16 },
        { "high", 10_000, 4_000, 400, 17 },
        { "mythical", 100_000, 1_600, 160, 18 },
        { "god", 1_000_000, 640, 64, 19 },
    };

    [Theory]
    [MemberData(nameof(Grades))]
    public void EachVaultGradeShipsAtTheGradeTable(string grade, long insideCubicMetres, long waterMilliwatts, long blockMilliwatts, int layer)
    {
        GameContent content = Shipped();
        MaterialDefinition material = content.Materials.Get(Identifier.Parse($"overburden:quantum_vault/{grade}"));
        Assert.True(material.IsPlaceable);
        Assert.Equal(MaterialUnit.Cells, material.Unit);
        Assert.Equal(layer, material.TextureLayer);

        MachineDefinition vault = material.Machine!;
        Assert.Equal(MachineKind.Vault, vault.Kind);
        Assert.True(vault.IsChannelVault);
        Assert.True(vault.HoldsWater);
        Assert.Equal(insideCubicMetres * MachineDefinition.MillilitresPerCubicMetre, vault.InsideMillilitres);
        Assert.Equal(waterMilliwatts, vault.WaterHoldMilliwattsPerCubicMetre);
        Assert.Equal(blockMilliwatts, vault.BlockHoldMilliwattsPerCubicMetre);

        // Blocks a tenth of water (decision 16), and his minute (IM-3b, decision 1).
        Assert.Equal(waterMilliwatts, blockMilliwatts * 10);
        Assert.Equal(60, vault.OwnChargeSeconds);
        Assert.Equal(new PortDefinition("channel", 0, 0, 15), vault.Port("channel"));
        Assert.Equal(new PortDefinition(MachineTick.EnabledPort, 1, 0, 1), vault.Port(MachineTick.EnabledPort));

        // No weight port, so a vault weighs 1 in a pump's ring (IM-4b).
        Assert.Null(vault.Port(MachineTick.WeightPort));
    }

    [Fact]
    public void EachGradeHoldsTenTimesMoreForFourTimesTheBill()
    {
        // The table's rule (decision 18): a full vault of water bills 2.5 kW, 10 kW, 40 kW, 160 kW, 640 kW.
        GameContent content = Shipped();
        long[] fullOfWater = [.. new[] { "low", "medium", "high", "mythical", "god" }.Select(grade =>
        {
            MachineDefinition vault = content.Materials.Get(Identifier.Parse($"overburden:quantum_vault/{grade}")).Machine!;
            return vault.InsideMillilitres / MachineDefinition.MillilitresPerCubicMetre * vault.WaterHoldMilliwattsPerCubicMetre;
        })];
        Assert.Equal([2_500_000L, 10_000_000L, 40_000_000L, 160_000_000L, 640_000_000L], fullOfWater);
    }

    [Fact]
    public void TheCrateIsAVaultWithNoChannelNoBillAndNoWater()
    {
        GameContent content = Shipped();
        MaterialDefinition material = content.Materials.Get(Identifier.Parse("overburden:crate"));
        Assert.True(material.IsPlaceable);
        Assert.Equal(20, material.TextureLayer);

        MachineDefinition crate = material.Machine!;
        Assert.Equal(MachineKind.Vault, crate.Kind);
        Assert.False(crate.IsChannelVault);
        Assert.False(crate.HoldsWater);
        Assert.Empty(crate.Links);
        Assert.Equal(MachineDefinition.MillilitresPerCubicMetre, crate.InsideMillilitres);
        Assert.Equal(0, crate.WaterHoldMilliwattsPerCubicMetre);
        Assert.Equal(0, crate.BlockHoldMilliwattsPerCubicMetre);

        // The hand pick can take it: the ladder's bottom rung is a hand tier's box.
        float hand = content.Tools.Get(Identifier.Parse("overburden:hand_pick")).HardnessThreshold;
        Assert.True(material.Hardness <= hand);
    }

    [Fact]
    public void TheSixContainersEachHaveTheirOwnLayerAfterTheBackpacks()
    {
        // Layers 15 to 20, one each (decision 6's "brighter by grade"), none shared with anything shipped before.
        GameContent content = Shipped();
        string[] containers = ["quantum_vault/low", "quantum_vault/medium", "quantum_vault/high", "quantum_vault/mythical", "quantum_vault/god", "crate"];
        int[] layers = [.. containers.Select(path => content.Materials.Get(Identifier.Parse($"overburden:{path}")).TextureLayer)];
        Assert.Equal([15, 16, 17, 18, 19, 20], layers);

        int[] others = [.. content.Materials.Entries()
            .Where(e => !containers.Contains(e.Identifier.Path))
            .Select(e => e.Value.TextureLayer)];
        Assert.All(layers, layer => Assert.DoesNotContain(layer, others));
    }

    public static TheoryData<string, ToolBite, float, string> Siphons => new()
    {
        // file, bite, cooldown (the rock tool of the same bite's), the rock tool it matches.
        { "quantum_siphon_cube", ToolBite.Cube, 0.10f, "quantum_miner" },
        { "quantum_siphon_tunnel", ToolBite.Tunnel, 0.25f, "quantum_tunneller" },
        { "quantum_siphon_chunk", ToolBite.Chunk, 0.25f, "quantum_excavator" },
    };

    [Theory]
    [MemberData(nameof(Siphons))]
    public void EachSiphonTakesWaterAtAThousandJoulesALitreInItsRockToolsBite(string path, ToolBite bite, float cooldown, string rockTool)
    {
        GameContent content = Shipped();
        ToolDefinition siphon = content.Tools.Get(Identifier.Parse($"overburden:{path}"));
        Assert.Equal(ToolTakes.Water, siphon.Takes);
        Assert.Equal(bite, siphon.Bite);
        Assert.True(siphon.AtRange);
        Assert.Equal(cooldown, siphon.CooldownSeconds);
        Assert.NotNull(siphon.Power);
        Assert.Equal(MachineLink.ChannelVia, siphon.Power!.Via);
        Assert.Equal(1_000, siphon.Power.JoulesPerLitre);
        Assert.Equal(0, siphon.Power.JoulesPerVoxel);
        Assert.False(siphon.IsFree);

        // Routed to the siphon, never the carve (fact 13), and in the matching rock tool's size and rate.
        Assert.True(SiphonClicks.IsWaterTool(siphon));
        ToolDefinition rock = content.Tools.Get(Identifier.Parse($"overburden:{rockTool}"));
        Assert.Equal(rock.Bite, siphon.Bite);
        Assert.Equal(rock.CooldownSeconds, siphon.CooldownSeconds);
    }

    [Fact]
    public void TheSevenStopCycleStillStartsOnTheHandPick()
    {
        GameContent content = Shipped();
        List<ToolDefinition> tiers = ToolTiers.Order(content.Tools.Entries().Select(e => e.Value));
        Assert.Equal(7, tiers.Count);
        Assert.Equal("overburden:hand_pick", ToolTiers.Default(tiers).Id.ToString());
        Assert.All(tiers.Take(4), tool => Assert.Equal(ToolTakes.Rock, tool.Takes));
        Assert.All(tiers.Skip(4), tool => Assert.Equal(ToolTakes.Water, tool.Takes));
    }

    [Theory]
    [InlineData("overburden:quantum_vault/god", "quantum vault (god)")]
    [InlineData("overburden:quantum_vault/low", "quantum vault (low)")]
    [InlineData("overburden:crate", "crate")]
    [InlineData("overburden:hand_pick", "hand pick")]
    [InlineData("overburden:quantum_siphon_cube", "quantum siphon cube")]
    [InlineData("mod:a/b/c_d", "a (b c d)")]
    public void ALabelReadsUnderscoresAsSpacesAndAGradeInBrackets(string id, string label)
    {
        Assert.Equal(label, ContentLabel.Of(Identifier.Parse(id)));
    }

    [Fact]
    public void ATrailingSlashLeavesNoEmptyBrackets()
    {
        Assert.Equal("vault", ContentLabel.OfPath("vault/"));
        Assert.Equal("vault", ContentLabel.OfPath("vault"));
    }
}
