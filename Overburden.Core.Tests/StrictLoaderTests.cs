using Overburden.Core.Content;
using Overburden.Core.Machines;

namespace Overburden.Core.Tests;

/// <summary>
/// The strict loader (IM-1; IDEAL-MACHINES-PLAN decisions 4 and 6, and IM-1's pass, decisions 6–8):
/// an unknown key at any level refused naming the file, its path and the nearest key; <c>$</c> keys
/// as notes everywhere; a key given twice refused; <c>format</c>; and <c>programmer</c>.
/// </summary>
public class StrictLoaderTests
{
    private const string Mod = """{ "id": "strict", "name": "Strict", "version": "1" }""";

    private const string Cell = """
        { "id": "cell", "hardness": 3.0, "supportDistance": 8, "unit": "cells",
          "machine": { "kind": "storage", "capacityJoules": 5000000000,
            "links": { "charge": { "carries": "power", "role": "sink", "via": "channel", "port": "channel" } },
            "ports": { "channel": { "default": 0, "min": 0, "max": 15 } } } }
        """;

    private const string Drill = """{ "id": "drill", "radiusMetres": 0.5, "hardnessThreshold": 4.0, "power": { "via": "channel", "joulesPerVoxel": 50 } }""";

    private static InMemoryContentSource Source(
        string mod = Mod,
        (string File, string Json)[]? materials = null,
        (string File, string Json)[]? tools = null,
        string? extras = null)
    {
        InMemoryContentSource source = TestContent.BaseGame().Add("strict", "mod.json", mod);
        foreach ((string file, string json) in materials ?? [])
        {
            source.Add("strict", $"materials/{file}", json);
        }

        foreach ((string file, string json) in tools ?? [])
        {
            source.Add("strict", $"tools/{file}", json);
        }

        if (extras is not null)
        {
            source.Add("strict", "extras.json", extras);
        }

        return source;
    }

    private static (GameContent Content, LoadResult Result) Load(InMemoryContentSource source, MachineExtra served = MachineExtras.Served)
    {
        var content = new GameContent(served);
        return (content, content.Load(source));
    }

    public static TheoryData<string> Levels => new()
    {
        "manifest", "material", "machine", "link", "port", "tool", "tool power", "an extra's object", "the table",
    };

    [Theory]
    [MemberData(nameof(Levels))]
    public void AMisspeltKeyAtEveryLevelIsRefusedNamingTheFileThePathAndTheNearestKey(string level)
    {
        // Decision 4: "a misspelt ceiling key that loads silently is a ceiling that isn't there". Before
        // IM-1 every one of these loaded as if the key were absent, without a word.
        (InMemoryContentSource source, string file, string path, string? near) = level switch
        {
            "manifest" => (Source(mod: """{ "id": "strict", "name": "Strict", "verison": "1" }"""),
                "mod.json", "verison", "version"),
            "material" => (Source(materials: [("rock.json", """{ "id": "rock", "hardnes": 2.0 }""")]),
                "rock.json", "hardnes", "hardness"),
            "machine" => (Source(materials: [("cell.json", Cell.Replace("\"capacityJoules\"", "\"capacityJoule\""))]),
                "cell.json", "machine.capacityJoule", "capacityJoules"),
            "link" => (Source(materials: [("cell.json", Cell.Replace("\"carries\"", "\"carrys\""))]),
                "cell.json", "machine.links.charge.carrys", "carries"),
            "port" => (Source(materials: [("cell.json", Cell.Replace("\"default\"", "\"defualt\""))]),
                "cell.json", "machine.ports.channel.defualt", "default"),
            "tool" => (Source(tools: [("drill.json", Drill.Replace("\"radiusMetres\"", "\"radiusMetre\""))]),
                "drill.json", "radiusMetre", "radiusMetres"),
            "tool power" => (Source(tools: [("drill.json", Drill.Replace("\"joulesPerVoxel\"", "\"joulesPerVoxl\""))]),
                "drill.json", "power.joulesPerVoxl", "joulesPerVoxel"),
            "an extra's object" => (Source(
                    materials: [("cell.json", Cell.Replace("\"kind\": \"storage\",", "\"kind\": \"storage\", \"extras\": { \"telemetry\": { \"below\": 10 } },"))],
                    extras: """{ "telemetry": { "channel": "state" } }"""),
                "cell.json", "machine.extras.telemetry.below", null),
            "the table" => (Source(extras: """{ "sence": { "channel": "switch" } }"""),
                "extras.json", "sence", "sense"),
            _ => throw new ArgumentOutOfRangeException(nameof(level)),
        };

        (GameContent content, LoadResult result) = Load(source, MachineExtras.All);

        Assert.False(result.Success, $"{level}: the misspelt key loaded");
        string error = Assert.Single(result.Errors, e => e.Contains(file));
        Assert.Contains($"'{path}'", error);
        if (near is not null)
        {
            Assert.Contains($"did you mean '{near}'?", error);
        }
        else
        {
            Assert.Contains("takes no keys yet", error);
        }

        // Refused whole: nothing of the file is registered.
        Assert.False(content.Materials.Contains(Identifier.Parse("strict:rock")));
        Assert.False(content.Materials.Contains(Identifier.Parse("strict:cell")));
        Assert.False(content.Tools.Contains(Identifier.Parse("strict:drill")));
        if (level == "the table")
        {
            Assert.Equal(0, content.Extras.Count);
        }
    }

    [Fact]
    public void DollarKeysAreNotesAtEveryLevelMapsIncluded()
    {
        // Decision 4: "Keys starting $ are allowed". The probe (fact 4) found a $comment inside a
        // name-keyed map throws under the plain serialiser, because it is read as a link.
        const string mod = """{ "$comment": "m", "id": "strict", "name": "Strict", "version": "1", "format": 1 }""";
        const string cell = """
            { "$comment": "material", "id": "cell", "hardness": 3.0, "unit": "cells",
              "machine": { "$comment": "machine", "kind": "storage", "capacityJoules": 5000000000,
                "extras": { "$comment": "the map", "telemetry": { "$comment": "an extra" } },
                "links": { "$comment": "the map", "charge": { "$comment": "a link", "carries": "power", "role": "sink", "via": "channel", "port": "channel" } },
                "ports": { "$comment": "the map", "channel": { "$comment": "a port", "default": 3, "min": 0, "max": 15 } } } }
            """;
        const string drill = """{ "$comment": "t", "id": "drill", "power": { "$comment": "p", "via": "channel", "joulesPerVoxel": 50 } }""";
        const string extras = """{ "$comment": "the table", "telemetry": { "$comment": "a row", "channel": "state" } }""";

        (GameContent content, LoadResult result) = Load(
            Source(mod: mod, materials: [("cell.json", cell)], tools: [("drill.json", drill)], extras: extras),
            MachineExtras.All);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        MachineDefinition machine = content.Materials.Get(Identifier.Parse("strict:cell")).Machine!;
        Assert.Single(machine.Links);
        Assert.Equal(new PortDefinition("channel", 3, 0, 15), Assert.Single(machine.Ports));
        Assert.Equal(ExtraForm.State, machine.FormOf(MachineExtra.Telemetry));
        Assert.Equal(50, content.Tools.Get(Identifier.Parse("strict:drill")).Power!.JoulesPerVoxel);
        Assert.Equal(1, content.Extras.Count);
    }

    [Theory]
    [InlineData("""{ "id": "rock", "hardness": 1.0, "hardness": 2.0 }""", "'hardness'")]
    [InlineData("""{ "id": "rock", "hardness": 1.0, "Hardness": 2.0 }""", "'Hardness'")]
    [InlineData("""{ "id": "rock", "id": "stone2" }""", "'id'")]
    public void AKeyGivenTwiceIsRefusedWhateverItsCase(string json, string key)
    {
        // Fact 4: the serialiser loads the second silently, "kind" against "Kind" included.
        (GameContent content, LoadResult result) = Load(Source(materials: [("rock.json", json)]));

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("rock.json") && e.Contains(key) && e.Contains("given twice"));
        Assert.False(content.Materials.Contains(Identifier.Parse("strict:rock")));
    }

    [Fact]
    public void ANameGivenTwiceInAMapIsRefused()
    {
        string cell = Cell.Replace(
            "\"ports\": {",
            "\"ports\": { \"channel\": { \"default\": 1, \"min\": 0, \"max\": 15 },");

        (_, LoadResult result) = Load(Source(materials: [("cell.json", cell)]));

        Assert.Contains(result.Errors, e => e.Contains("cell.json") && e.Contains("'machine.ports.channel'") && e.Contains("given twice"));
    }

    [Fact]
    public void KeysStillBindWhateverTheirCase()
    {
        // Strict about names, not their case: a mod that loads today on a capitalised key keeps loading.
        (GameContent content, LoadResult result) = Load(Source(materials: [("rock.json", """{ "Id": "rock", "Hardness": 2.5 }""")]));

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(2.5f, content.Materials.Get(Identifier.Parse("strict:rock")).Hardness);
    }

    [Fact]
    public void AKeyFarFromEveryKeyGetsNoGuessAndTheKeysItCouldHaveBeen()
    {
        (_, LoadResult result) = Load(Source(materials: [("rock.json", """{ "id": "rock", "zzzzzzzz": 1 }""")]));

        string error = Assert.Single(result.Errors);
        Assert.DoesNotContain("did you mean", error);
        Assert.Contains("not a key this build reads here", error);
        Assert.Contains("hardness", error);
    }

    [Fact]
    public void EveryUnknownKeyInAFileIsReportedNotJustTheFirst()
    {
        // The built-in strict mode stops at the first (fact 4); a modder fixing one at a time pays a
        // restart a typo.
        (_, LoadResult result) = Load(Source(materials: [("rock.json", """{ "id": "rock", "hardnes": 1, "opaqe": true, "unitt": "count" }""")]));

        Assert.Equal(3, result.Errors.Count(e => e.Contains("rock.json") && e.Contains("unknown key")));
    }

    [Theory]
    [InlineData("""{ "id": "rock", "hardness": "hard" }""")]
    [InlineData("""{ "$comment": "cleaned first", "id": "rock", "hardness": "hard" }""")]
    public void AValueOfTheWrongShapeIsStillRefusedWithItsPath(string json)
    {
        // The walk leaves values to the serialiser, whose message names the path — with or without a
        // note dropped first.
        (_, LoadResult result) = Load(Source(materials: [("rock.json", json)]));

        Assert.Contains(result.Errors, e => e.Contains("rock.json") && e.Contains("not valid JSON") && e.Contains("hardness"));
    }

    // ---------------------------------------------------------------- format (decision 7)

    [Fact]
    public void ANewerFormatFailsInOneLineWithNoneOfItsFilesRead()
    {
        // Decision 4: "a mod written for a newer build fails with one line, not forty". Every file
        // here would be refused key by key if it were read.
        InMemoryContentSource source = TestContent.BaseGame()
            .Add("future", "mod.json", """{ "id": "future", "name": "F", "version": "9", "format": 2, "signing": "x", "tiers": [] }""")
            .Add("future", "extras.json", """{ "shield": { "fibre": "wall" } }""");
        for (int i = 0; i < 5; i++)
        {
            source.Add("future", $"materials/m{i}.json", $$"""{ "id": "m{{i}}", "grade": "god", "mass": 4 }""");
        }

        (GameContent content, LoadResult result) = Load(source);

        string error = Assert.Single(result.Errors);
        Assert.Contains("future", error);
        Assert.Contains("content format 2", error);
        Assert.Contains($"this build reads format {ModManifest.CurrentFormat}", error);
        Assert.DoesNotContain(result.Mods, m => m.Id == "future");
        Assert.False(content.Materials.Contains(Identifier.Parse("future:m0")));
    }

    [Fact]
    public void ADependantOfASkippedModIsToldItWasSkippedNotThatItIsMissing()
    {
        InMemoryContentSource source = TestContent.BaseGame()
            .Add("future", "mod.json", """{ "id": "future", "name": "F", "version": "9", "format": 2 }""")
            .Add("needy", "mod.json", """{ "id": "needy", "name": "N", "version": "1", "dependencies": ["future"] }""");

        (_, LoadResult result) = Load(source);

        string error = Assert.Single(result.Errors, e => e.Contains("needy"));
        Assert.Contains("skipped", error);
        Assert.Contains("content format 2", error);
        Assert.DoesNotContain("not installed", error);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("\"1\"")]
    [InlineData("true")]
    public void AFormatThatIsNotAWholeNumberOfOneOrMoreIsRefused(string format)
    {
        InMemoryContentSource source = Source(
            mod: $$"""{ "id": "strict", "name": "Strict", "version": "1", "format": {{format}} }""",
            materials: [("rock.json", """{ "id": "rock" }""")]);

        (GameContent content, LoadResult result) = Load(source);

        Assert.Contains(result.Errors, e => e.Contains("strict") && e.Contains("format must be a whole number"));
        Assert.False(content.Materials.Contains(Identifier.Parse("strict:rock")));
    }

    [Theory]
    [InlineData("""{ "id": "strict", "name": "Strict", "version": "1" }""")]
    [InlineData("""{ "id": "strict", "name": "Strict", "version": "1", "format": 1 }""")]
    public void AnAbsentFormatReadsAsOneAndOneLoads(string mod)
    {
        (GameContent content, LoadResult result) = Load(Source(mod: mod, materials: [("rock.json", """{ "id": "rock" }""")]));

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(1, Assert.Single(result.Mods, m => m.Id == "strict").Format);
        Assert.True(content.Materials.Contains(Identifier.Parse("strict:rock")));
    }

    // ---------------------------------------------------------------- programmer (decision 8)

    [Fact]
    public void OnlyAProgrammerOpensPanelsAndWaterAndPartsAreNotOnes()
    {
        // Decision 6: "Today any non-placeable item opens machine panels, so water, parts and ingots
        // would." The table answers the verb now, not "an item that is not a pack".
        (GameContent content, LoadResult result) = Load(Source(materials:
        [
            ("computer.json", """{ "id": "computer", "unit": "count", "placeable": false, "programmer": true }"""),
            ("gear.json", """{ "id": "gear", "unit": "count", "placeable": false }"""),
            ("water.json", """{ "id": "water", "fluid": true, "opaque": false, "placeable": false }"""),
        ]));
        Assert.True(result.Success, string.Join("; ", result.Errors));

        MaterialTable table = MaterialTable.From(content.Materials);
        int Id(string path) => content.Materials.GetNumericId(Identifier.Parse($"strict:{path}"));

        Assert.True(table.IsProgrammer(Id("computer")));
        Assert.False(table.IsProgrammer(Id("gear")));
        Assert.False(table.IsProgrammer(Id("water")));
        Assert.False(table.IsProgrammer(content.Materials.GetNumericId(Identifier.Parse("overburden:stone"))));
        Assert.True(content.Materials.Get(Identifier.Parse("strict:computer")).IsProgrammer);
    }

    [Theory]
    [InlineData("""{ "id": "tablet", "programmer": true }""", "must be \"placeable\": false")]
    [InlineData("""{ "id": "tablet", "programmer": true, "placeable": true }""", "must be \"placeable\": false")]
    [InlineData("""
        { "id": "tablet", "unit": "count", "placeable": false, "programmer": true,
          "machine": { "kind": "storage", "carried": true, "capacityJoules": 1000,
            "links": { "charge": { "carries": "power", "role": "sink", "via": "channel", "port": "channel" } },
            "ports": { "channel": { "default": 0, "min": 0, "max": 15 } } } }
        """, "a machine cannot be")]
    public void AProgrammerIsAnItemAndNeverABlockOrAMachine(string json, string mentioning)
    {
        (GameContent content, LoadResult result) = Load(Source(materials: [("tablet.json", json)]));

        Assert.Contains(result.Errors, e => e.Contains("tablet.json") && e.Contains(mentioning));
        Assert.False(content.Materials.Contains(Identifier.Parse("strict:tablet")));
    }
}
