using Overburden.Core.Content;

namespace Overburden.Core.Tests;

/// <summary>
/// The flattened material properties the hot paths read.
/// </summary>
public class MaterialTableTests
{
    [Fact]
    public void HardnessReachesTheTableFromTheModJson()
    {
        // The whole distance from a mod file to the carve's inner loop, because
        // MaterialDefinition has carried Hardness since M1 and nothing projected it — a
        // property that loads and is never read looks exactly like a property that works.
        var content = new GameContent();
        content.Load(TestContent.BaseGame());

        MaterialTable table = MaterialTable.From(content.Materials);

        Assert.Equal(0f, table.Hardness(MaterialIds.AirNumericId));
        Assert.Equal(0.8f, table.Hardness(content.Materials.GetNumericId(Id("dirt"))));
        Assert.Equal(3.0f, table.Hardness(content.Materials.GetNumericId(Id("stone"))));
    }

    [Fact]
    public void PermeabilityReachesTheTableFromTheModJsonAndNaNIsSealed()
    {
        // MATERIALS-NOTES' trap: the field loads and nothing reads it, which looks exactly like
        // the field working. The whole distance from the JSON to the seep scan's read, and the
        // two bounds the table applies on its own because the loader is not the only way in —
        // NaN fails closed, because every comparison with NaN is false and a NaN term would
        // poison a chunk's whole coefficient.
        var content = new GameContent();
        content.Load(TestContent.BaseGame());

        MaterialTable table = MaterialTable.From(content.Materials);

        Assert.Equal(1.0f, table.Permeability(MaterialIds.AirNumericId));
        Assert.Equal(0.35f, table.Permeability(content.Materials.GetNumericId(Id("dirt"))));
        Assert.Equal(0.02f, table.Permeability(content.Materials.GetNumericId(Id("stone"))));

        Assert.Equal(0f, MaterialTable.BoundPermeability(float.NaN));
        Assert.Equal(0f, MaterialTable.BoundPermeability(-3f));
        Assert.Equal(1f, MaterialTable.BoundPermeability(7f));
        Assert.Equal(0.5f, MaterialTable.BoundPermeability(0.5f));

        MaterialTable bounded = MaterialTable.ForTesting(
            [false, true, true], [false, false, false], [0, 0, 0], [0f, 1f, 1f],
            permeability: [1f, float.NaN, 4f]);
        Assert.Equal(0f, bounded.Permeability(1));
        Assert.Equal(1f, bounded.Permeability(2));
    }

    [Fact]
    public void AMaterialFromARemovedModIsHardRatherThanFree()
    {
        // The placeholder is solid so the world does not develop gaps; if it were also
        // hardness zero, a bare hand would tunnel through a removed mod's blocks faster than
        // through dirt. Same rule as MaterialDefinition.Unknown, and the same number.
        var first = new GameContent();
        first.Load(TestContent.BaseGame().AddDeepDig());
        var savedTable = first.Materials.ExportIdTable().ToList();
        int copperId = first.Materials.GetNumericId(Id("copper_ore", "deepdig"));

        var second = new GameContent();
        second.Load(TestContent.BaseGame(), savedTable);

        Assert.True(second.Materials.IsPlaceholder(copperId));
        Assert.Equal(
            MaterialDefinition.UnknownHardness,
            MaterialTable.From(second.Materials).Hardness(copperId));

        // And sealed: a removed mod's blocks cannot flood a mine that was dry with it installed.
        Assert.Equal(0f, MaterialTable.From(second.Materials).Permeability(copperId));
    }

    [Fact]
    public void AHoleWithNoIdentifierLeftIsSolidAndHard()
    {
        // A saved table can carry a slot whose identifier is gone entirely, not merely
        // undefined. It has no definition to read properties off, so the table has to supply
        // them — and "opaque, hard" is the pair that cannot destroy a world.
        var content = new GameContent();
        content.Load(TestContent.BaseGame(), [MaterialIds.Air, default]);

        MaterialTable table = MaterialTable.From(content.Materials);

        Assert.True(table.IsOpaque(1));
        Assert.Equal(MaterialDefinition.UnknownHardness, table.Hardness(1));
        Assert.Equal(0f, table.Permeability(1));
    }

    private static Identifier Id(string path, string ns = Identifier.BaseNamespace) => new(ns, path);
}
