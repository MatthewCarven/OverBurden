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

        var pick = content.Tools.Get(Identifier.Parse("minercraft:hand_pick"));
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

        var pick = content.Tools.Get(Identifier.Parse("minercraft:hand_pick"));
        var dirt = content.Materials.Get(Identifier.Parse("minercraft:dirt"));
        var stone = content.Materials.Get(Identifier.Parse("minercraft:stone"));

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
        Assert.False(content.Tools.Contains(Identifier.Parse("minercraft:diamond_drill")));
    }

    [Fact]
    public void OmittedFieldsFallBackToDefaultsRatherThanToZero()
    {
        // A tool file that says only "id" is a legal, if dull, tool. Defaulting the radius to
        // zero instead would produce a tier that silently mines nothing.
        var source = TestContent.BaseGame()
            .Add("minercraft", "tools/plain.json", """{ "id": "plain" }""");

        var content = new GameContent();
        content.Load(source);

        var plain = content.Tools.Get(Identifier.Parse("minercraft:plain"));
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
        var source = TestContent.BaseGame().Add("minercraft", "tools/bad.json", json);

        var content = new GameContent();
        LoadResult result = content.Load(source);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("tools/bad.json"));
        Assert.False(content.Tools.Contains(Identifier.Parse("minercraft:bad")));

        // One bad file does not stop the game from starting, which is what makes a bad mod
        // diagnosable rather than a black screen.
        Assert.Equal(2, result.ToolsLoaded);
        Assert.True(content.Tools.Contains(Identifier.Parse("minercraft:hand_pick")));
    }

    [Fact]
    public void AToolFileThatIsNotJsonIsReportedAndSkipped()
    {
        var source = TestContent.BaseGame().Add("minercraft", "tools/broken.json", "{ nope");

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
            .Add("minercraft", "mod.json", """{ "id": "minercraft" }""")
            .Add("minercraft", "materials/air.json", """{ "id": "air", "opaque": false }""");

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
                Identifier.Parse("minercraft:late"),
                new ToolDefinition { Id = Identifier.Parse("minercraft:late") }));
    }

    [Fact]
    public void TwoToolsWithTheSameIdIsAnErrorRatherThanASilentOverwrite()
    {
        var source = TestContent.BaseGame()
            .Add("minercraft", "tools/hand_pick_copy.json", """{ "id": "hand_pick" }""");

        var content = new GameContent();
        LoadResult result = content.Load(source);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Contains("hand_pick"));
    }
}
