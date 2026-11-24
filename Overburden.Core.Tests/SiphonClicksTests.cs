using Overburden.Core.Content;
using Overburden.Core.Machines;

namespace Overburden.Core.Tests;

/// <summary>
/// IM-6b: the clicks' water branch (IDEAL-MACHINES-PLAN, "IM-6 — the code-level pass", decision 11, fact 13),
/// built before any water tool shipped. A rock tool never reaches the siphon; a water tool reaches nothing else.
/// </summary>
public class SiphonClicksTests
{
    private static ToolDefinition Rock(float cooldown = 0.25f, long joulesPerVoxel = 50) => new()
    {
        Id = Identifier.Parse("test:pick"),
        Bite = ToolBite.Cube,
        HardnessThreshold = 5f,
        CooldownSeconds = cooldown,
        Power = new ToolPower(MachineLink.ChannelVia, joulesPerVoxel),
    };

    private static ToolDefinition Water(float cooldown = 0.25f) => new()
    {
        Id = Identifier.Parse("test:siphon"),
        Bite = ToolBite.Cube,
        Takes = ToolTakes.Water,
        CooldownSeconds = cooldown,
        Power = new ToolPower(MachineLink.ChannelVia, 0, JoulesPerLitre: 1_000),
    };

    [Fact]
    public void OnlyAToolThatTakesWaterIsRoutedToTheSiphon()
    {
        Assert.True(SiphonClicks.IsWaterTool(Water()));
        Assert.False(SiphonClicks.IsWaterTool(Rock()));

        // A free rock tool is still a rock tool: free is not what routes a click.
        Assert.False(SiphonClicks.IsWaterTool(Rock(joulesPerVoxel: 0)));
        Assert.False(SiphonClicks.IsWaterTool(null));
    }

    [Fact]
    public void ARockToolNeverQueuesAnOpWhateverIsPressed()
    {
        var clicks = new SiphonClicks();
        ToolDefinition pick = Rock();
        foreach (bool pressed in new[] { false, true })
        {
            foreach (bool held in new[] { false, true })
            {
                Assert.Null(clicks.Take(pick, pressed, held));
                Assert.Null(clicks.Pour(pick, pressed, itemInHand: false));
            }
        }

        Assert.Equal(0, clicks.Takes);
        Assert.Equal(0, clicks.Pours);

        // And it leaves the cooldown alone, so a siphon put on T next is ready at once.
        Assert.Equal(0.0, clicks.CooldownLeft);
    }

    [Fact]
    public void TheLeftClickTakesAndTheRightClickPours()
    {
        var clicks = new SiphonClicks();
        ToolDefinition siphon = Water(cooldown: 0.25f);

        Assert.Equal(SiphonVerb.Take, clicks.Take(siphon, pressed: true, held: false));
        clicks.Cool(0.25);
        Assert.Equal(SiphonVerb.Pour, clicks.Pour(siphon, pressed: true, itemInHand: false));
        Assert.Equal(1, clicks.Takes);
        Assert.Equal(1, clicks.Pours);
    }

    [Fact]
    public void NothingPressedQueuesNothing()
    {
        var clicks = new SiphonClicks();
        ToolDefinition siphon = Water();
        Assert.Null(clicks.Take(siphon, pressed: false, held: false));
        Assert.Null(clicks.Pour(siphon, pressed: false, itemInHand: false));
        Assert.Equal(0.0, clicks.CooldownLeft);
    }

    [Fact]
    public void QHeldTakesOnceEveryCooldownWithNoClick()
    {
        var clicks = new SiphonClicks();
        ToolDefinition siphon = Water(cooldown: 0.25f);

        // Two seconds of frames at 60 Hz with Q held: one take at the start and one each 0.25 s after.
        int takes = 0;
        for (int frame = 0; frame < 120; frame++)
        {
            clicks.Cool(1.0 / 60.0);
            if (clicks.Take(siphon, pressed: false, held: true) is SiphonVerb.Take)
            {
                takes++;
            }
        }

        // 0.25 s at 1/60 s a frame is fifteen frames, so 120 frames hold eight takes.
        Assert.InRange(takes, 8, 9);
        Assert.Equal(takes, clicks.Takes);
    }

    [Fact]
    public void OneCooldownServesBothVerbs()
    {
        var clicks = new SiphonClicks();
        ToolDefinition siphon = Water(cooldown: 0.5f);

        Assert.Equal(SiphonVerb.Take, clicks.Take(siphon, pressed: true, held: false));
        Assert.Null(clicks.Pour(siphon, pressed: true, itemInHand: false));
        Assert.Null(clicks.Take(siphon, pressed: true, held: false));

        clicks.Cool(0.49);
        Assert.Null(clicks.Pour(siphon, pressed: true, itemInHand: false));
        clicks.Cool(0.011);
        Assert.Equal(SiphonVerb.Pour, clicks.Pour(siphon, pressed: true, itemInHand: false));
        Assert.Equal(0.5, clicks.CooldownLeft, 6);
    }

    [Fact]
    public void AnItemInHandKeepsTheRightClickForItsOwnVerb()
    {
        var clicks = new SiphonClicks();
        ToolDefinition siphon = Water();

        // The computer opens a panel and a pack is worn on the right click; the pour waits for a material in hand.
        Assert.Null(clicks.Pour(siphon, pressed: true, itemInHand: true));
        Assert.Equal(0.0, clicks.CooldownLeft);

        // The take is the left click's, so an item in hand changes nothing for it.
        Assert.Equal(SiphonVerb.Take, clicks.Take(siphon, pressed: true, held: false));
    }

    [Fact]
    public void ANegativeOrZeroFrameRunsNoCooldownDown()
    {
        var clicks = new SiphonClicks();
        ToolDefinition siphon = Water(cooldown: 0.25f);
        clicks.Take(siphon, pressed: true, held: false);
        clicks.Cool(0);
        clicks.Cool(-1);
        Assert.Equal(0.25, clicks.CooldownLeft, 6);
    }

    [Fact]
    public void EveryShippedToolIsRoutedByWhatItTakes()
    {
        // The shipped content as it stands: every rock tool goes to the controllers, every water tool to the siphon.
        var content = new GameContent();
        LoadResult result = content.Load(new DirectoryContentSource(ShippedContentTests.ModsDirectory()));
        Assert.True(result.Success, string.Join("; ", result.Errors));
        foreach ((int _, Identifier _, ToolDefinition tool) in content.Tools.Entries())
        {
            Assert.Equal(tool.Takes == ToolTakes.Water, SiphonClicks.IsWaterTool(tool));
        }
    }
}
