using MinerCraft.Core.Content;

namespace MinerCraft.Core.Tests;

public class IdentifierTests
{
    [Theory]
    [InlineData("minercraft:stone", "minercraft", "stone")]
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
        var id = Identifier.Parse("minercraft:stone", "deepdig");

        Assert.Equal("minercraft", id.Namespace);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData(":")]
    [InlineData(":stone")]
    [InlineData("minercraft:")]
    [InlineData("a:b:c")]
    [InlineData("Minercraft:stone")]  // uppercase namespace
    [InlineData("minercraft:Stone")]  // uppercase path
    [InlineData("miner craft:stone")] // space
    [InlineData("minercraft:sto ne")]
    [InlineData("miner/craft:stone")] // '/' is path-only
    [InlineData("minercraft:stone!")]
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
        Assert.Throws<ArgumentException>(() => new Identifier("MinerCraft", "stone"));
    }

    [Fact]
    public void Equality_IsByValue()
    {
        var a = new Identifier("minercraft", "stone");
        var b = Identifier.Parse("minercraft:stone");
        var c = Identifier.Parse("minercraft:dirt");

        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.NotEqual(a, c);
        Assert.True(a != c);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
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
            [Identifier.Parse("minercraft:stone")] = 1,
        };

        Assert.Equal(1, map[new Identifier("minercraft", "stone")]);
    }
}
