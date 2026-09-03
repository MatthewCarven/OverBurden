using Overburden.Core.Content;

namespace Overburden.Core.Tests;

public class ContentLoaderTests
{
    [Fact]
    public void LoadsTheBaseGameThroughTheOrdinaryModPath()
    {
        // The dogfood rule from docs/DESIGN.md: if stone cannot be expressed the way a
        // third-party mod would express it, the format is wrong.
        var content = new GameContent();
        var result = content.Load(TestContent.BaseGame());

        Assert.True(result.Success, string.Join("\n", result.Errors));
        Assert.Equal(3, result.MaterialsLoaded); // air, dirt, stone

        var stone = content.Materials.Get(Identifier.Parse("overburden:stone"));
        Assert.Equal(3.0f, stone.Hardness);
        Assert.Equal(30, stone.SupportDistance);
        Assert.Equal(0.02f, stone.Permeability);
        Assert.True(stone.IsOpaque);
        Assert.False(stone.IsUnknown);
    }

    [Fact]
    public void AirIsAlwaysZero()
    {
        var content = new GameContent();
        var result = content.Load(TestContent.BaseGame());

        Assert.True(result.Success);
        Assert.Equal(MaterialIds.AirNumericId, content.Materials.GetNumericId(MaterialIds.Air));

        var air = content.Materials.Get(MaterialIds.AirNumericId);
        Assert.True(air.IsAir);
        Assert.False(air.IsOpaque);
        Assert.True(air.IsReplaceable);
    }

    [Fact]
    public void AirIsZeroEvenThoughItsFileIsNotLoadedFirst()
    {
        // Reserving air's number is separate from loading its definition, so its ID does
        // not depend on filename ordering.
        var source = TestContent.BaseGame()
            .Add("overburden", "materials/aaa_first.json", """{ "id": "aaa_first" }""");

        var content = new GameContent();
        content.Load(source);

        Assert.Equal(0, content.Materials.GetNumericId(MaterialIds.Air));
        Assert.NotEqual(0, content.Materials.GetNumericId(Identifier.Parse("overburden:aaa_first")));
    }

    [Fact]
    public void MissingAirDefinitionIsAnError()
    {
        var source = new InMemoryContentSource()
            .Add("overburden", "mod.json", """{ "id": "overburden", "name": "M", "version": "1" }""")
            .Add("overburden", "materials/stone.json", """{ "id": "stone" }""");

        var result = new GameContent().Load(source);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("overburden:air"));
    }

    [Fact]
    public void ModContentDefaultsToItsOwnNamespace()
    {
        var content = new GameContent();
        content.Load(TestContent.BaseGame().AddDeepDig());

        // "copper_ore" in deepdig's files became deepdig:copper_ore, not overburden:.
        Assert.True(content.Materials.Contains(Identifier.Parse("deepdig:copper_ore")));
        Assert.False(content.Materials.Contains(Identifier.Parse("overburden:copper_ore")));
    }

    [Fact]
    public void BaseGameLoadsBeforeOtherMods()
    {
        var content = new GameContent();
        content.Load(TestContent.BaseGame().AddDeepDig().AddAardvark());

        int stone = content.Materials.GetNumericId(Identifier.Parse("overburden:stone"));
        int burrow = content.Materials.GetNumericId(Identifier.Parse("aardvark:burrow_dirt"));

        Assert.True(stone < burrow, "base game content must be numbered before third-party content");
    }

    [Fact]
    public void MalformedJsonIsReportedButDoesNotStopTheLoad()
    {
        // One bad mod must not be a black screen — it must be a diagnosable message.
        var source = TestContent.BaseGame()
            .Add("brokenmod", "mod.json", """{ "id": "brokenmod", "name": "Broken", "version": "1" }""")
            .Add("brokenmod", "materials/oops.json", "{ this is not json");

        var content = new GameContent();
        var result = content.Load(source);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("oops.json"));

        // ...and the base game still loaded.
        Assert.True(content.Materials.Contains(Identifier.Parse("overburden:stone")));
    }

    [Fact]
    public void ModDirectoryMustMatchDeclaredId()
    {
        // Otherwise a mod's namespace is not predictable from its folder.
        var source = TestContent.BaseGame()
            .Add("folder_name", "mod.json", """{ "id": "different_id", "name": "X", "version": "1" }""");

        var result = new GameContent().Load(source);

        Assert.Contains(result.Errors, e => e.Contains("folder_name") && e.Contains("different_id"));
    }

    [Fact]
    public void MissingModJsonIsReported()
    {
        var source = TestContent.BaseGame().Add("nomanifest", "materials/x.json", """{ "id": "x" }""");

        var result = new GameContent().Load(source);

        Assert.Contains(result.Errors, e => e.Contains("nomanifest") && e.Contains("mod.json"));
    }

    [Fact]
    public void MissingDependencyIsReported()
    {
        var source = TestContent.BaseGame()
            .Add("needy", "mod.json",
                """{ "id": "needy", "name": "Needy", "version": "1", "dependencies": ["absent"] }""");

        var result = new GameContent().Load(source);

        Assert.Contains(result.Errors, e => e.Contains("needy") && e.Contains("absent"));
    }

    [Fact]
    public void AModWrittenAgainstTheRetiredNamespaceStillLoads()
    {
        // A third-party mod from before the rename: it depends on "minercraft" and patches
        // "minercraft:stone" by its old name. Both have to land on the base game as it is now,
        // and the patch is a duplicate of overburden:stone rather than a definition in a
        // namespace nothing reads.
        var source = TestContent.BaseGame()
            .Add("oldtimer", "mod.json",
                """{ "id": "oldtimer", "name": "Old", "version": "1", "dependencies": ["minercraft"] }""")
            .Add("oldtimer", "materials/stone.json", """{ "id": "minercraft:stone", "hardness": 999 }""");

        var content = new GameContent();
        var result = content.Load(source);

        Assert.DoesNotContain(result.Errors, e => e.Contains("requires"));
        Assert.Contains(result.Errors, e => e.Contains("overburden:stone"));
        Assert.Equal(3.0f, content.Materials.Get(Identifier.Parse("overburden:stone")).Hardness);
    }

    [Fact]
    public void DuplicateDefinitionIsReported()
    {
        // Two mods both claiming overburden:stone is a conflict, not a silent override.
        var source = TestContent.BaseGame()
            .Add("rude", "mod.json", """{ "id": "rude", "name": "Rude", "version": "1" }""")
            .Add("rude", "materials/stone.json", """{ "id": "overburden:stone", "hardness": 999 }""");

        var content = new GameContent();
        var result = content.Load(source);

        Assert.Contains(result.Errors, e => e.Contains("overburden:stone"));
        Assert.Equal(3.0f, content.Materials.Get(Identifier.Parse("overburden:stone")).Hardness);
    }

    [Fact]
    public void RegistryIsFrozenAfterLoading()
    {
        var content = new GameContent();
        content.Load(TestContent.BaseGame());

        Assert.True(content.Materials.IsFrozen);
    }

    [Fact]
    public void OmittedFieldsGetDefaults()
    {
        var source = TestContent.BaseGame()
            .Add("overburden", "materials/plain.json", """{ "id": "plain" }""");

        var content = new GameContent();
        content.Load(source);

        var plain = content.Materials.Get(Identifier.Parse("overburden:plain"));
        Assert.True(plain.IsOpaque);
        Assert.False(plain.IsFluid);
        Assert.False(plain.IsReplaceable);
        Assert.Equal(0, plain.TextureLayer);
    }

    [Fact]
    public void LoadingIsDeterministic()
    {
        // Numeric IDs for unseen content are assigned in load order, so a
        // non-deterministic order would hand out different IDs on different runs.
        var first = new GameContent();
        first.Load(TestContent.BaseGame().AddDeepDig().AddAardvark());

        var second = new GameContent();
        second.Load(TestContent.BaseGame().AddDeepDig().AddAardvark());

        Assert.Equal(first.Materials.ExportIdTable(), second.Materials.ExportIdTable());
    }
}
