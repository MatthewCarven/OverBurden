using Overburden.Core.Content;

namespace Overburden.Core.Tests;

public class RegistryTests
{
    private static Registry<MaterialDefinition> NewRegistry() =>
        new("materials", MaterialDefinition.Unknown);

    private static MaterialDefinition Material(string id) =>
        new() { Id = Identifier.Parse(id) };

    [Fact]
    public void Register_AssignsSequentialIds()
    {
        var registry = NewRegistry();

        Assert.Equal(0, registry.Register(Identifier.Parse("overburden:air"), Material("overburden:air")));
        Assert.Equal(1, registry.Register(Identifier.Parse("overburden:stone"), Material("overburden:stone")));
        Assert.Equal(2, registry.Register(Identifier.Parse("overburden:dirt"), Material("overburden:dirt")));
        Assert.Equal(3, registry.Count);
    }

    [Fact]
    public void Register_RejectsDuplicates()
    {
        var registry = NewRegistry();
        var stone = Identifier.Parse("overburden:stone");
        registry.Register(stone, Material("overburden:stone"));

        Assert.Throws<InvalidOperationException>(() => registry.Register(stone, Material("overburden:stone")));
    }

    [Fact]
    public void Freeze_BlocksFurtherRegistration()
    {
        // Registration order determines numeric IDs, so anything registering after the
        // load phase would hand out numbers no world has agreed to. Fail loudly there
        // rather than corrupting chunks three systems downstream.
        var registry = NewRegistry();
        registry.Register(Identifier.Parse("overburden:stone"), Material("overburden:stone"));
        registry.Freeze();

        Assert.True(registry.IsFrozen);
        Assert.Throws<InvalidOperationException>(
            () => registry.Register(Identifier.Parse("overburden:dirt"), Material("overburden:dirt")));
        Assert.Throws<InvalidOperationException>(
            () => registry.ReserveId(Identifier.Parse("overburden:dirt")));
    }

    [Fact]
    public void ReserveId_AllocatesTheNumberWithoutADefinition()
    {
        var registry = NewRegistry();
        var air = Identifier.Parse("overburden:air");

        int id = registry.ReserveId(air);

        Assert.Equal(0, id);
        Assert.True(registry.Contains(air));

        // The definition can still arrive later, and must land on the same number.
        Assert.Equal(0, registry.Register(air, Material("overburden:air")));
    }

    [Fact]
    public void SeedIdTable_RestoresTheSavedAssignment()
    {
        var registry = NewRegistry();
        registry.SeedIdTable(
        [
            Identifier.Parse("overburden:air"),
            Identifier.Parse("deepdig:copper_ore"),
            Identifier.Parse("overburden:stone"),
        ]);

        // Registering in a completely different order must not move anything.
        registry.Register(Identifier.Parse("overburden:stone"), Material("overburden:stone"));
        registry.Register(Identifier.Parse("overburden:air"), Material("overburden:air"));

        Assert.Equal(0, registry.GetNumericId(Identifier.Parse("overburden:air")));
        Assert.Equal(1, registry.GetNumericId(Identifier.Parse("deepdig:copper_ore")));
        Assert.Equal(2, registry.GetNumericId(Identifier.Parse("overburden:stone")));
    }

    [Fact]
    public void SeedIdTable_UnderTheRetiredNamespace_KeepsEveryNumber()
    {
        // The shape of every world saved before the rename: a table spelled minercraft:, and
        // content that now registers as overburden:. Same numbers, or every stored chunk's
        // palette points at the wrong material - the alias renames and never renumbers.
        var registry = NewRegistry();
        registry.SeedIdTable(
        [
            Identifier.Parse("minercraft:air"),
            Identifier.Parse("minercraft:stone"),
            Identifier.Parse("deepdig:copper_ore"),
            Identifier.Parse("minercraft:dirt"),
        ]);

        Assert.Equal(1, registry.Register(Identifier.Parse("overburden:stone"), Material("overburden:stone")));
        Assert.Equal(3, registry.Register(Identifier.Parse("overburden:dirt"), Material("overburden:dirt")));
        Assert.Equal(0, registry.Register(Identifier.Parse("overburden:air"), Material("overburden:air")));
        registry.Freeze();

        Assert.Equal(4, registry.Count);
        Assert.False(registry.IsPlaceholder(1));
        Assert.False(registry.IsPlaceholder(3));
        Assert.Equal(Identifier.Parse("overburden:stone"), registry.GetIdentifier(1));
        Assert.DoesNotContain(registry.ExportIdTable(), id => id.ToString().Contains("minercraft"));
    }

    [Fact]
    public void SeedIdTable_RefusesATableNamingOneMaterialTwice()
    {
        // Reachable only through the alias: a saved table cannot repeat a spelling, but it can
        // hold two spellings that now mean one thing, which is what a build older than the alias
        // writes after opening a newer world.
        var registry = NewRegistry();

        var error = Assert.Throws<InvalidOperationException>(() => registry.SeedIdTable(
        [
            Identifier.Parse("minercraft:air"),
            Identifier.Parse("overburden:stone"),
            Identifier.Parse("overburden:air"),
        ]));

        Assert.Contains("0 and 2", error.Message);
        Assert.Contains("overburden:air", error.Message);
    }

    [Fact]
    public void SeedIdTable_AfterRegistering_Throws()
    {
        var registry = NewRegistry();
        registry.Register(Identifier.Parse("overburden:stone"), Material("overburden:stone"));

        Assert.Throws<InvalidOperationException>(
            () => registry.SeedIdTable([Identifier.Parse("overburden:air")]));
    }

    [Fact]
    public void SeedIdTable_PreservesHolesLeftByRemovedMods()
    {
        var registry = NewRegistry();
        registry.SeedIdTable(
        [
            Identifier.Parse("overburden:air"),
            default, // a hole
            Identifier.Parse("overburden:stone"),
        ]);
        registry.Freeze();

        Assert.Equal(3, registry.Count);
        Assert.Equal(2, registry.GetNumericId(Identifier.Parse("overburden:stone")));
        Assert.False(registry.GetIdentifier(1).IsValid);
    }

    [Fact]
    public void Freeze_FillsUndefinedIdsWithAPlaceholderThatKeepsItsIdentifier()
    {
        var registry = NewRegistry();
        var missing = Identifier.Parse("deepdig:copper_ore");
        registry.SeedIdTable([Identifier.Parse("overburden:air"), missing]);
        registry.Register(Identifier.Parse("overburden:air"), Material("overburden:air"));
        registry.Freeze();

        var definition = registry.Get(1);

        Assert.True(definition.IsUnknown);
        Assert.True(registry.IsPlaceholder(1));

        // Retaining the identifier is the whole point: it is what makes re-installing
        // the mod restore the world rather than reinterpret it.
        Assert.Equal(missing, definition.Id);
    }

    [Fact]
    public void PlaceholderIsSolid_NotAir()
    {
        // Turning a removed mod's blocks into air would quietly punch holes through
        // every structure built from them.
        var placeholder = MaterialDefinition.Unknown(Identifier.Parse("deepdig:copper_ore"));

        Assert.True(placeholder.IsOpaque);
        Assert.False(placeholder.IsReplaceable);
        Assert.False(placeholder.IsAir);
    }

    [Fact]
    public void Get_ByIdentifierAndByNumber_Agree()
    {
        var registry = NewRegistry();
        var stone = Identifier.Parse("overburden:stone");
        int id = registry.Register(stone, Material("overburden:stone"));
        registry.Freeze();

        Assert.Same(registry.Get(stone), registry.Get(id));
        Assert.Equal(stone, registry.GetIdentifier(id));
    }

    [Fact]
    public void Get_OutOfRange_Throws()
    {
        var registry = NewRegistry();
        registry.Register(Identifier.Parse("overburden:stone"), Material("overburden:stone"));
        registry.Freeze();

        Assert.Throws<ArgumentOutOfRangeException>(() => registry.Get(7));
        Assert.Throws<ArgumentOutOfRangeException>(() => registry.Get(-1));
    }

    [Fact]
    public void TryGet_MissesCleanly()
    {
        var registry = NewRegistry();
        registry.Freeze();

        Assert.False(registry.TryGet(Identifier.Parse("deepdig:copper_ore"), out _));
        Assert.False(registry.TryGetNumericId(Identifier.Parse("deepdig:copper_ore"), out _));
        Assert.Throws<KeyNotFoundException>(() => registry.GetNumericId(Identifier.Parse("deepdig:copper_ore")));
    }

    [Fact]
    public void ExportIdTable_RoundTripsThroughSeed()
    {
        var first = NewRegistry();
        first.Register(Identifier.Parse("overburden:air"), Material("overburden:air"));
        first.Register(Identifier.Parse("overburden:stone"), Material("overburden:stone"));
        first.Freeze();

        var second = NewRegistry();
        second.SeedIdTable(first.ExportIdTable());
        second.Freeze();

        Assert.Equal(first.ExportIdTable(), second.ExportIdTable());
    }
}
