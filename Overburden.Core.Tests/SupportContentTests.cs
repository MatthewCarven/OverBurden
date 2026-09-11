using Overburden.Core.Content;
using Overburden.Core.Structure;

namespace Overburden.Core.Tests;

/// <summary>
/// M9a's content half: the support distance reaching the table, the cap, and the saturation curve
/// — its loader, its refusals and its interpolation.
/// </summary>
public class SupportContentTests
{
    private const string Mod = """{ "id": "rockmod", "name": "Rock", "version": "1" }""";

    [Fact]
    public void SupportDistanceReachesTheTableFromTheModJson()
    {
        // MATERIALS-NOTES' trap, the same one permeability had: the field has loaded since M1
        // and nothing read it. The whole distance from the JSON to the derive's cost.
        var content = new GameContent();
        content.Load(TestContent.BaseGame());

        MaterialTable table = MaterialTable.From(content.Materials);

        Assert.Equal(30, table.SupportDistance(content.Materials.GetNumericId(Identifier.Parse("overburden:stone"))));
        Assert.Equal(8, table.SupportDistance(content.Materials.GetNumericId(Identifier.Parse("overburden:dirt"))));
        Assert.False(table.IsSolid(MaterialIds.AirNumericId));
        Assert.True(table.IsSolid(content.Materials.GetNumericId(Identifier.Parse("overburden:stone"))));
    }

    [Fact]
    public void ASupportDistancePastTheCapIsRefusedRatherThanClamped()
    {
        // A byte's budget of 120 makes round(120 / 1000) zero — a roof that never falls — and
        // whoever wrote 1000 believed the range was wider. The cap and a negative are refused;
        // the cap itself loads.
        foreach (string value in new[] { "49", "1000", "-1" })
        {
            var source = TestContent.BaseGame()
                .Add("rockmod", "mod.json", Mod)
                .Add("rockmod", "materials/granite.json", $$"""{ "id": "granite", "supportDistance": {{value}} }""");

            var result = new GameContent().Load(source);

            Assert.False(result.Success, $"supportDistance {value} loaded");
            Assert.Contains(result.Errors, e => e.Contains("granite.json") && e.Contains("supportDistance"));
        }

        var edge = TestContent.BaseGame()
            .Add("rockmod", "mod.json", Mod)
            .Add("rockmod", "materials/granite.json", $$"""{ "id": "granite", "supportDistance": {{MaterialDefinition.MaxSupportDistance}} }""");
        var content = new GameContent();
        var ok = content.Load(edge);
        Assert.True(ok.Success, string.Join("; ", ok.Errors));
        Assert.Equal(MaterialDefinition.MaxSupportDistance, content.Materials.Get(Identifier.Parse("rockmod:granite")).SupportDistance);
    }

    [Fact]
    public void ACurveThatCannotBeInterpolatedIsRefused()
    {
        // Heads not increasing, a distance rising with head, a distance above the dry one, a
        // point that is not a pair: each is a curve a reader could only guess at, and the one
        // that rises is water shoring a roof up.
        foreach ((string curve, string why) in new[]
                 {
                     ("[[2, 24], [2, 12]]", "equal heads"),
                     ("[[5, 24], [2, 12]]", "heads decreasing"),
                     ("[[0, 24]]", "a head of zero"),
                     ("[[2, 12], [10, 24]]", "distance rising with head"),
                     ("[[2, 40]]", "distance above the dry distance"),
                     ("[[2, -1]]", "a negative distance"),
                     ("[[2, 24, 1]]", "a triple"),
                     ("[[2]]", "a single"),
                 })
        {
            var source = TestContent.BaseGame()
                .Add("rockmod", "mod.json", Mod)
                .Add("rockmod", "materials/shale.json", $$"""{ "id": "shale", "supportDistance": 30, "supportUnderHead": {{curve}} }""");

            var result = new GameContent().Load(source);

            Assert.False(result.Success, $"{why}: {curve} loaded");
            Assert.Contains(result.Errors, e => e.Contains("shale.json") && e.Contains("supportUnderHead"));
        }
    }

    [Fact]
    public void AGoodCurveLoadsAndInterpolatesLinearlyBetweenItsPoints()
    {
        // The shipped stone curve: 30 dry, 24 at 2 m, 12 at 10 m, 12 past it. Halfway between
        // points is halfway between distances; below the first point it descends from the dry
        // value; NaN and a negative head are dry.
        var source = TestContent.BaseGame()
            .Add("rockmod", "mod.json", Mod)
            .Add("rockmod", "materials/shale.json",
                """{ "id": "shale", "supportDistance": 30, "supportUnderHead": [[2, 24], [10, 12]] }""");
        var content = new GameContent();
        var result = content.Load(source);
        Assert.True(result.Success, string.Join("; ", result.Errors));

        MaterialDefinition shale = content.Materials.Get(Identifier.Parse("rockmod:shale"));
        Assert.Equal([new SupportPoint(2f, 24f), new SupportPoint(10f, 12f)], shale.SupportUnderHead);

        MaterialTable table = MaterialTable.From(content.Materials);
        int id = content.Materials.GetNumericId(Identifier.Parse("rockmod:shale"));
        Assert.Equal(30f, table.SupportDistanceAt(id, 0));
        Assert.Equal(27f, table.SupportDistanceAt(id, 1));
        Assert.Equal(24f, table.SupportDistanceAt(id, 2));
        Assert.Equal(18f, table.SupportDistanceAt(id, 6));
        Assert.Equal(12f, table.SupportDistanceAt(id, 10));
        Assert.Equal(12f, table.SupportDistanceAt(id, 50));
        Assert.Equal(30f, table.SupportDistanceAt(id, -1));
        Assert.Equal(30f, table.SupportDistanceAt(id, double.NaN));

        // A material with no curve is its dry distance at any head.
        int stone = content.Materials.GetNumericId(Identifier.Parse("overburden:stone"));
        Assert.Equal(30f, table.SupportDistanceAt(stone, 100));
    }

    [Fact]
    public void AMaterialFromARemovedModCarriesAsThePlaceholderDoes()
    {
        // Neither eternal nor the first thing to fall: the named placeholder's 8, and the same
        // for a hole with no identifier left.
        var first = new GameContent();
        first.Load(TestContent.BaseGame().AddDeepDig());
        var savedTable = first.Materials.ExportIdTable().ToList();
        int copperId = first.Materials.GetNumericId(Identifier.Parse("deepdig:copper_ore"));

        var second = new GameContent();
        second.Load(TestContent.BaseGame(), savedTable);

        Assert.True(second.Materials.IsPlaceholder(copperId));
        Assert.Equal(
            MaterialDefinition.Unknown(Identifier.Parse("deepdig:copper_ore")).SupportDistance,
            MaterialTable.From(second.Materials).SupportDistance(copperId));
    }

    [Fact]
    public void TheShippedMaterialsCarrySomethingAndTheLampSurvivesHanging()
    {
        // The content's numbers, not the code's: every solid material the base game ships has a
        // support distance inside the byte's range, stone carries the farthest, and the lamp's
        // rose from 4 to 8 with decision 1 because hanging costs double and a 1 m cube under a
        // ceiling would otherwise shed its lower half.
        var content = new GameContent();
        content.Load(new DirectoryContentSource(ShippedContentTests.ModsDirectory()));
        MaterialTable table = MaterialTable.From(content.Materials);

        var solids = content.Materials.Entries().Where(e => table.IsSolid(e.NumericId)).ToList();
        Assert.NotEmpty(solids);
        foreach (var (_, _, material) in solids)
        {
            Assert.InRange(material.SupportDistance, 1, MaterialDefinition.MaxSupportDistance);
        }

        int lamp = content.Materials.GetNumericId(Identifier.Parse("overburden:lamp"));
        int cost = SupportRule.Default.Cost(table.SupportDistance(lamp));
        Assert.True(4 * SupportRule.Default.HangCost(cost) <= SupportRule.MaxBudget, "a lamp cube hanging from full rock loses its lower half");

        int stone = content.Materials.GetNumericId(Identifier.Parse("overburden:stone"));
        Assert.Equal(solids.Max(s => s.Value.SupportDistance), table.SupportDistance(stone));

        // And the keystone is in the content: stone and the soils carry less under water.
        Assert.True(table.SupportDistanceAt(stone, 10) < table.SupportDistance(stone), "stone has no saturation curve");
        int dirt = content.Materials.GetNumericId(Identifier.Parse("overburden:dirt"));
        Assert.True(table.SupportDistanceAt(dirt, 3) < table.SupportDistance(dirt), "dirt has no saturation curve");
    }
}
