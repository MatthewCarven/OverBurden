using Overburden.Core.Content;

namespace Overburden.Core.Tests;

/// <summary>
/// Tools arriving from <c>mods/*/tools/*.json</c> through the same loader materials use.
/// </summary>
/// <remarks>
/// The point of these is non-negotiable 5 rather than the numbers: if a tool tier cannot be
/// expressed the way a third-party mod would express it, the content format is wrong, and
/// the cheap moment to find that out is now rather than at M12 when someone wants a drill.
/// </remarks>
public class ToolContentTests
{
    [Fact]
    public void TheBaseGamesToolsLoadFromItsModDirectory()
    {
        var content = new GameContent();

        LoadResult result = content.Load(TestContent.BaseGame());

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(2, result.ToolsLoaded);

        var pick = content.Tools.Get(Identifier.Parse("overburden:hand_pick"));
        Assert.Equal(0.4f, pick.RadiusMetres);
        Assert.Equal(1.0f, pick.HardnessThreshold);
        Assert.Equal(0.3f, pick.CooldownSeconds);
        Assert.False(pick.IsUnknown);
    }

    [Fact]
    public void TheHandPicksThresholdSitsBetweenDirtAndStone()
    {
        // Not a numerology test. The base game's first tier has to leave stone standing in a
        // dirt bank or the mechanic never shows itself, and that is a property of the content
        // rather than of the code — so it belongs in a test that reads the content.
        var content = new GameContent();
        content.Load(TestContent.BaseGame());

        var pick = content.Tools.Get(Identifier.Parse("overburden:hand_pick"));
        var dirt = content.Materials.Get(Identifier.Parse("overburden:dirt"));
        var stone = content.Materials.Get(Identifier.Parse("overburden:stone"));

        Assert.True(dirt.Hardness <= pick.HardnessThreshold);
        Assert.True(stone.Hardness > pick.HardnessThreshold);
    }

    [Fact]
    public void AModsToolLandsInThatModsNamespace()
    {
        var content = new GameContent();

        LoadResult result = content.Load(TestContent.BaseGame().AddDeepDig());

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(3, result.ToolsLoaded);
        Assert.True(content.Tools.Contains(Identifier.Parse("deepdig:diamond_drill")));

        // A bare id must not be able to land in someone else's namespace by accident.
        Assert.False(content.Tools.Contains(Identifier.Parse("overburden:diamond_drill")));
    }

    [Fact]
    public void OmittedFieldsFallBackToDefaultsRatherThanToZero()
    {
        // A tool file that says only "id" is a legal, if dull, tool. Defaulting the radius to
        // zero instead would produce a tier that silently mines nothing.
        var source = TestContent.BaseGame()
            .Add("overburden", "tools/plain.json", """{ "id": "plain" }""");

        var content = new GameContent();
        content.Load(source);

        var plain = content.Tools.Get(Identifier.Parse("overburden:plain"));
        Assert.True(plain.RadiusMetres > 0f);
        Assert.True(plain.CooldownSeconds > 0f);
    }

    [Theory]
    [InlineData("""{ "id": "bad", "radiusMetres": -1 }""")]
    [InlineData("""{ "id": "bad", "radiusMetres": 1000 }""")]
    [InlineData("""{ "id": "bad", "hardnessThreshold": 1e400 }""")]
    [InlineData("""{ "id": "bad", "cooldownSeconds": -0.5 }""")]
    public void AToolWithAnUnusableNumberIsRejectedAtLoadAndTheRestStillLoad(string json)
    {
        // Rejected at the door rather than clamped, so every tool the registry hands out is
        // one a carve can be swung with unconditionally. A thousand-metre radius clamped to
        // something sensible would hide the typo; reaching the carve it would stop the game
        // inside a cubic loop, several layers from the file that caused it.
        var source = TestContent.BaseGame().Add("overburden", "tools/bad.json", json);

        var content = new GameContent();
        LoadResult result = content.Load(source);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("tools/bad.json"));
        Assert.False(content.Tools.Contains(Identifier.Parse("overburden:bad")));

        // One bad file does not stop the game from starting, which is what makes a bad mod
        // diagnosable rather than a black screen.
        Assert.Equal(2, result.ToolsLoaded);
        Assert.True(content.Tools.Contains(Identifier.Parse("overburden:hand_pick")));
    }

    // -------------------------------------------------------------------------------------
    // The power block (M10c-3, decision 5): a tool's draw, with a link's via rule and a bound.
    // -------------------------------------------------------------------------------------

    [Fact]
    public void ATooolsPowerBlockIsReadAndAToolWithoutOneIsFree()
    {
        var source = TestContent.BaseGame()
            .Add("overburden", "tools/miner.json",
                """{ "id": "miner", "hardnessThreshold": 5.0, "cooldownSeconds": 0.1, "power": { "via": "channel", "joulesPerVoxel": 100 } }""");

        var content = new GameContent();
        LoadResult result = content.Load(source);
        Assert.True(result.Success, string.Join("; ", result.Errors));

        var miner = content.Tools.Get(Identifier.Parse("overburden:miner"));
        Assert.NotNull(miner.Power);
        Assert.Equal("channel", miner.Power!.Via);
        Assert.Equal(100L, miner.Power.JoulesPerVoxel);
        Assert.Equal(100_000L, miner.Power.MillijoulesPerVoxel);
        Assert.False(miner.IsFree);

        // The picks declare none and are free by absence — two facts, kept apart.
        var pick = content.Tools.Get(Identifier.Parse("overburden:hand_pick"));
        Assert.Null(pick.Power);
        Assert.True(pick.IsFree);
    }

    [Theory]
    [InlineData("""{ "id": "bad", "power": { "joulesPerVoxel": 100 } }""", "via")]
    [InlineData("""{ "id": "bad", "power": { "via": "crank", "joulesPerVoxel": 100 } }""", "M10f")]
    [InlineData("""{ "id": "bad", "power": { "via": "copper", "joulesPerVoxel": 100 } }""", "M10e")]
    [InlineData("""{ "id": "bad", "power": { "via": "Not A Via", "joulesPerVoxel": 100 } }""", "via")]
    [InlineData("""{ "id": "bad", "power": { "via": "channel", "joulesPerVoxel": -1 } }""", "joulesPerVoxel")]
    [InlineData("""{ "id": "bad", "power": { "via": "channel", "joulesPerVoxel": 1000000001 } }""", "joulesPerVoxel")]
    public void APowerBlockThisBuildCannotServeIsRefusedNamingWhy(string json, string names)
    {
        // A via the tick does not serve is refused naming the part that will, as a link's is
        // (M10a's pass, decision 11): a tool written for a class M10e has not built would
        // otherwise load free, which is the gate silently open.
        var source = TestContent.BaseGame().Add("overburden", "tools/bad.json", json);

        var content = new GameContent();
        LoadResult result = content.Load(source);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("tools/bad.json") && error.Contains(names));
        Assert.False(content.Tools.Contains(Identifier.Parse("overburden:bad")));
        Assert.Equal(2, result.ToolsLoaded);
    }

    [Fact]
    public void APowerBlockOfNothingIsAToolOnThePoolThatDrawsNothing()
    {
        // Legal and dull, and distinct from no block at all: the block is there and reads zero.
        var source = TestContent.BaseGame()
            .Add("overburden", "tools/idle.json", """{ "id": "idle", "power": { "via": "channel" } }""");

        var content = new GameContent();
        Assert.True(content.Load(source).Success);

        var idle = content.Tools.Get(Identifier.Parse("overburden:idle"));
        Assert.NotNull(idle.Power);
        Assert.Equal(0L, idle.Power!.JoulesPerVoxel);
        Assert.True(idle.IsFree);
    }

    [Fact]
    public void AToolFileThatIsNotJsonIsReportedAndSkipped()
    {
        var source = TestContent.BaseGame().Add("overburden", "tools/broken.json", "{ nope");

        var content = new GameContent();
        LoadResult result = content.Load(source);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("tools/broken.json"));
        Assert.Equal(2, result.ToolsLoaded);
    }

    [Fact]
    public void AModWithNoToolsDirectoryIsFine()
    {
        // Zero tools is a playable world you cannot mine in, not a load failure — a mod that
        // only adds materials is legitimate content.
        var source = new InMemoryContentSource()
            .Add("overburden", "mod.json", """{ "id": "overburden" }""")
            .Add("overburden", "materials/air.json", """{ "id": "air", "opaque": false }""");

        var content = new GameContent();
        LoadResult result = content.Load(source);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(0, result.ToolsLoaded);
    }

    [Fact]
    public void TheToolRegistryIsFrozenAfterLoading()
    {
        // Same reason the material registry is: worker threads read content without
        // synchronisation, and that is only sound if nothing can register afterwards.
        var content = new GameContent();
        content.Load(TestContent.BaseGame());

        Assert.True(content.Tools.IsFrozen);
        Assert.Throws<InvalidOperationException>(() =>
            content.Tools.Register(
                Identifier.Parse("overburden:late"),
                new ToolDefinition { Id = Identifier.Parse("overburden:late") }));
    }

    [Fact]
    public void TwoToolsWithTheSameIdIsAnErrorRatherThanASilentOverwrite()
    {
        var source = TestContent.BaseGame()
            .Add("overburden", "tools/hand_pick_copy.json", """{ "id": "hand_pick" }""");

        var content = new GameContent();
        LoadResult result = content.Load(source);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("hand_pick"));
    }
}
