using Overburden.Core.Inventory;
using Overburden.Core.Persistence;

namespace Overburden.Core.Tests;

/// <summary>
/// IM-6c: the test keys' gate (IDEAL-MACHINES-PLAN decision 52, amended by IM-6's pass, decision 24), and the mode's words
/// (decision 21's <c>--mode=</c>).
/// </summary>
public class DebugKeyGateTests
{
    [Fact]
    public void TheGateRulesOnExactlyDecisionTwentyFoursTenKeys()
    {
        Assert.Equal(
            ["P", "Alt+P", "Ctrl+P", "R", "Ctrl+R", "Alt+R", "Alt+G", "H", "Alt+H", "L"],
            DebugKeyGate.All.Select(DebugKeyGate.Label));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryTestKeyIsOffInACreativeWorldWhateverDevSays(bool dev)
    {
        foreach (DebugKey key in DebugKeyGate.All)
        {
            Assert.False(DebugKeyGate.Allowed(key, WorldMode.Creative, dev), $"{DebugKeyGate.Label(key)} answered in creative with --dev={dev}");
            Assert.Contains("off in a creative world", DebugKeyGate.Refusal(key, WorldMode.Creative, dev));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASurvivalModeIfOneEverOpensAnswersNoTestKey(bool dev)
    {
        Assert.All(DebugKeyGate.All, key => Assert.False(DebugKeyGate.Allowed(key, WorldMode.Survival, dev)));
    }

    [Fact]
    public void SandboxKeepsTodaysKeysExactly()
    {
        // ALPHA-PLAN B7's three developer keys answer only under --dev; the rest always do.
        DebugKey[] developer = [.. DebugKeyGate.All.Where(DebugKeyGate.DeveloperOnly)];
        Assert.Equal([DebugKey.CtrlR, DebugKey.H, DebugKey.AltH], developer);

        foreach (DebugKey key in DebugKeyGate.All)
        {
            Assert.True(DebugKeyGate.Allowed(key, WorldMode.Sandbox, dev: true));
            Assert.Equal(!developer.Contains(key), DebugKeyGate.Allowed(key, WorldMode.Sandbox, dev: false));
        }

        Assert.Null(DebugKeyGate.Refusal(DebugKey.P, WorldMode.Sandbox, dev: false));
        Assert.Contains("start with --dev", DebugKeyGate.Refusal(DebugKey.H, WorldMode.Sandbox, dev: false));
    }

    [Theory]
    [InlineData("creative", WorldMode.Creative)]
    [InlineData("CREATIVE", WorldMode.Creative)]
    [InlineData(" sandbox ", WorldMode.Sandbox)]
    public void ModeReadsSandboxAndCreative(string value, WorldMode expected)
    {
        Assert.True(WorldModes.TryParse(value, out WorldMode mode, out string? error));
        Assert.Equal(expected, mode);
        Assert.Null(error);
    }

    [Theory]
    [InlineData("survival", "reserved")]
    [InlineData("creatve", "not a mode")]
    [InlineData("", "not a mode")]
    [InlineData(null, "not a mode")]
    public void ModeRefusesSurvivalAndAnythingElseAndStaysSandbox(string? value, string expected)
    {
        Assert.False(WorldModes.TryParse(value, out WorldMode mode, out string? error));
        Assert.Equal(WorldMode.Sandbox, mode);
        Assert.Contains(expected, error);
    }

    [Fact]
    public void TheModesAreNamedAsTheOpenLineSpellsThem()
    {
        Assert.Equal("sandbox", WorldModes.Name(WorldMode.Sandbox));
        Assert.Equal("creative", WorldModes.Name(WorldMode.Creative));
        Assert.Equal("survival", WorldModes.Name(WorldMode.Survival));
    }
}
