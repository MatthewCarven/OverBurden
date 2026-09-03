using Overburden.Core.Content;

namespace Overburden.Core.Tests;

public class IdentifierTests
{
    [Theory]
    [InlineData("overburden:stone", "overburden", "stone")]
    [InlineData("deepdig:copper_ore", "deepdig", "copper_ore")]
    [InlineData("a:b", "a", "b")]
    [InlineData("mod.name:blocks/ore", "mod.name", "blocks/ore")]
    [InlineData("mod-name:ore-1", "mod-name", "ore-1")]
    public void Parse_SplitsOnTheColon(string text, string expectedNamespace, string expectedPath)
    {
        var id = Identifier.Parse(text);

        Assert.Equal(expectedNamespace, id.Namespace);
        Assert.Equal(expectedPath, id.Path);
        Assert.Equal(text, id.ToString());
    }

    [Fact]
    public void Parse_WithoutColon_UsesTheDefaultNamespace()
    {
        // This is what lets a mod write "id": "copper_ore" in its own files and land in
        // its own namespace, so it cannot accidentally claim someone else's.
        var id = Identifier.Parse("copper_ore", "deepdig");

        Assert.Equal("deepdig", id.Namespace);
        Assert.Equal("copper_ore", id.Path);
    }

    [Fact]
    public void Parse_WithColon_CanDeliberatelyTargetAnotherNamespace()
    {
        // A compatibility patch overriding another mod's content is legitimate, so the
        // explicit form has to win over the default.
        var id = Identifier.Parse("overburden:stone", "deepdig");

        Assert.Equal("overburden", id.Namespace);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData(":")]
    [InlineData(":stone")]
    [InlineData("overburden:")]
    [InlineData("a:b:c")]
    [InlineData("Overburden:stone")]  // uppercase namespace
    [InlineData("overburden:Stone")]  // uppercase path
    [InlineData("miner craft:stone")] // space
    [InlineData("overburden:sto ne")]
    [InlineData("miner/craft:stone")] // '/' is path-only
    [InlineData("overburden:stone!")]
    public void TryParse_RejectsMalformed(string? text)
    {
        Assert.False(Identifier.TryParse(text, Identifier.BaseNamespace, out _, out string? error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void UppercaseIsRejectedOnPurpose()
    {
        // Case-insensitive comparison across cultures is a real source of bugs (Turkish
        // dotless i being the classic), so uppercase is forbidden rather than folded.
        Assert.Throws<ArgumentException>(() => new Identifier("Overburden", "stone"));
    }

    [Fact]
    public void Equality_IsByValue()
    {
        var a = new Identifier("overburden", "stone");
        var b = Identifier.Parse("overburden:stone");
        var c = Identifier.Parse("overburden:dirt");

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.NotEqual(a, c);
        Assert.True(a != c);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    // ---------------------------------------------------------------------------------
    // The retired namespace. "minercraft" was the base game until 2026-09-03; every world
    // saved before then spells its table that way, and non-negotiable 2 forbids editing it.
    // ---------------------------------------------------------------------------------

    [Theory]
    [InlineData("minercraft:stone")]
    [InlineData("minercraft:air")]
    public void ARetiredNamespaceParsesAsTheCurrentOne(string text)
    {
        var id = Identifier.Parse(text);

        Assert.Equal(Identifier.BaseNamespace, id.Namespace);
        Assert.Equal(text[(text.IndexOf(':') + 1)..], id.Path);
        Assert.DoesNotContain("minercraft", id.ToString());
    }

    [Fact]
    public void TheOldAndNewSpellingsAreTheSameValue()
    {
        // Equality is the whole point: a registry keyed by the old spelling and a lookup by the
        // new one must meet, or every stored block in an old world becomes a placeholder.
        var old = new Identifier("minercraft", "stone");
        var now = new Identifier("overburden", "stone");

        Assert.Equal(now, old);
        Assert.Equal(now.GetHashCode(), old.GetHashCode());
        Assert.Equal(1, new Dictionary<Identifier, int> { [old] = 1 }[now]);
    }

    [Fact]
    public void TheDefaultNamespaceIsAliasedToo()
    {
        // A mod file read under the retired default lands in the current namespace, so a
        // loader that still passes the old name cannot register into a namespace nothing reads.
        Assert.Equal(Identifier.BaseNamespace, Identifier.Parse("stone", "minercraft").Namespace);
    }

    [Fact]
    public void ARetiredNamespaceIsStillValidText()
    {
        // Validity is about characters, not meaning: the old spelling has to pass validation
        // to reach the alias at all. Refusing it here would refuse every old world at read.
        Assert.True(Identifier.IsValidNamespace("minercraft"));
        Assert.Equal("overburden", Identifier.CanonicalNamespace("minercraft"));
        Assert.Equal("deepdig", Identifier.CanonicalNamespace("deepdig"));
    }

    [Fact]
    public void TheAliasTableIsNeverEmptied()
    {
        // Entries are only added. If this fails, someone removed the one alias that every
        // world saved before 2026-09-03 depends on.
        Assert.Equal(Identifier.BaseNamespace, Identifier.RetiredNamespaces["minercraft"]);
    }

    [Fact]
    public void Default_IsInvalidRatherThanThrowing()
    {
        // default(Identifier) is reachable through arrays and struct fields, and it marks
        // a hole in a world's ID table. It must be inspectable without blowing up.
        Identifier empty = default;

        Assert.False(empty.IsValid);
        Assert.Equal("<invalid>", empty.ToString());
    }

    [Fact]
    public void UsableAsADictionaryKey()
    {
        var map = new Dictionary<Identifier, int>
        {
            [Identifier.Parse("overburden:stone")] = 1,
        };

        Assert.Equal(1, map[new Identifier("overburden", "stone")]);
    }
}
