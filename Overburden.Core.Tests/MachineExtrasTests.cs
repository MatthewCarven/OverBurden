using Overburden.Core.Content;
using Overburden.Core.Machines;

namespace Overburden.Core.Tests;

/// <summary>
/// The six extras through the loader (IM-1; IDEAL-MACHINES-PLAN decisions 2 and 3, and IM-1's pass,
/// decisions 1–5): refused on a machine until the slice that reads each lands; with the served set,
/// a bit and a form on the table; media checked by the <c>via</c> rule; one owner a pair; the same
/// table in every listing order.
/// </summary>
public class MachineExtrasTests
{
    /// <summary>The base game's shipped row, verbatim in substance: the quantum form of each extra.</summary>
    internal const string QuantumRow = """
        {
          "$comment": "the quantum row",
          "sense":     { "channel": "switch" },
          "recall":    { "channel": "return" },
          "remote":    { "channel": "board" },
          "charge":    { "channel": "window" },
          "priority":  { "channel": "port" },
          "telemetry": { "channel": "state" }
        }
        """;

    private const string Mod = """{ "id": "machines", "name": "Machines", "version": "1" }""";

    /// <summary>A pump, with <c>{extras}</c> in its machine block and <c>{ports}</c> after its channel port.</summary>
    private static string Pump(string extras, string ports = "") => $$"""
        { "id": "pump", "hardness": 3.0, "unit": "cells",
          "machine": { "kind": "pump", "litresPerSecond": 100, "joulesPerLitre": 1000, {{extras}}
            "links": { "power": { "carries": "power", "role": "sink",   "via": "channel", "port": "channel" },
                       "water": { "carries": "fluid", "role": "source", "via": "channel", "port": "channel" } },
            "ports": { "channel": { "default": 0, "min": 0, "max": 15 }{{ports}} } } }
        """;

    private static string Pack(string extras) => $$"""
        { "id": "pack", "hardness": 1.0, "unit": "count", "placeable": false,
          "machine": { "kind": "storage", "carried": true, "capacityJoules": 3600000, {{extras}}
            "links": { "charge": { "carries": "power", "role": "sink", "via": "channel", "port": "channel" } },
            "ports": { "channel": { "default": 0, "min": 0, "max": 15 } } } }
        """;

    private static string Cell(string extras) => $$"""
        { "id": "cell", "hardness": 3.0, "unit": "cells",
          "machine": { "kind": "storage", "capacityJoules": 5000000000, {{extras}}
            "links": { "charge": { "carries": "power", "role": "sink", "via": "channel", "port": "channel" } },
            "ports": { "channel": { "default": 0, "min": 0, "max": 15 } } } }
        """;

    private const string OnSignal = """, "onsignal": { "default": 0, "min": 0, "max": 2 }""";

    private const string Priority = """, "priority": { "default": 5, "min": 0, "max": 9 }""";

    private static (GameContent Content, LoadResult Result) Load(
        MachineExtra served, bool quantumRow = true, params (string File, string Json)[] files)
    {
        InMemoryContentSource source = TestContent.BaseGame().Add("machines", "mod.json", Mod);
        if (quantumRow)
        {
            source.Add("overburden", "extras.json", QuantumRow);
        }

        foreach ((string file, string json) in files)
        {
            source.Add("machines", $"materials/{file}", json);
        }

        var content = new GameContent(served);
        return (content, content.Load(source));
    }

    public static TheoryData<string, string, string> EveryExtraAndItsSlice => new()
    {
        { "sense", "pump", "collapse f-1" },
        { "recall", "pump", "IM-9" },
        { "remote", "pump", "IM-9" },
        { "charge", "pack", "IM-2" },
        { "priority", "pump", "IM-2" },
        { "telemetry", "pump", "IM-9" },
    };

    [Theory]
    [MemberData(nameof(EveryExtraAndItsSlice))]
    public void EachExtraIsRefusedOnAMachineNamingTheSliceThatReadsIt(string extra, string machine, string slice)
    {
        // IM-1's pass, decision 2: every extra's reader is a later slice, so an opt-in today would
        // load and do nothing — the via precedent refuses it naming the part that will serve it.
        string json = machine == "pack"
            ? Pack($$"""
                "extras": { "{{extra}}": {} },
                """)
            : Pump($$"""
                "extras": { "{{extra}}": {} },
                """, OnSignal + Priority);

        (GameContent content, LoadResult result) = Load(MachineExtras.Served, true, ($"{machine}.json", json));

        string error = Assert.Single(result.Errors);
        Assert.Contains($"{machine}.json", error);
        Assert.Contains($"'{extra}' is not served in this build", error);
        Assert.Contains(slice, error);
        Assert.False(content.Materials.Contains(Identifier.Parse($"machines:{machine}")));
    }

    [Fact]
    public void WithTheServedSetEachBitAndFormLandsOnTheTable()
    {
        (GameContent content, LoadResult result) = Load(
            MachineExtras.All,
            true,
            ("pump.json", Pump("""
                "extras": { "sense": {}, "recall": {}, "remote": {}, "priority": {}, "telemetry": {} },
                """, OnSignal + Priority)),
            ("pack.json", Pack("""
                "extras": { "charge": {} },
                """)),
            ("cell.json", Cell(string.Empty)));
        Assert.True(result.Success, string.Join("; ", result.Errors));

        MaterialTable table = MaterialTable.From(content.Materials);
        int Id(string path) => content.Materials.GetNumericId(Identifier.Parse($"machines:{path}"));
        MachineDefinition pump = table.Machine(Id("pump"))!;
        MachineDefinition pack = table.Machine(Id("pack"))!;

        Assert.Equal(MachineExtras.All & ~MachineExtra.Charge, pump.Extras);
        Assert.Equal(ExtraForm.Switch, pump.FormOf(MachineExtra.Sense));
        Assert.Equal(ExtraForm.Return, pump.FormOf(MachineExtra.Recall));
        Assert.Equal(ExtraForm.Board, pump.FormOf(MachineExtra.Remote));
        Assert.Equal(ExtraForm.Port, pump.FormOf(MachineExtra.Priority));
        Assert.Equal(ExtraForm.State, pump.FormOf(MachineExtra.Telemetry));
        Assert.Equal(ExtraForm.None, pump.FormOf(MachineExtra.Charge));
        Assert.Equal(MachineExtra.Charge, pack.Extras);
        Assert.Equal(ExtraForm.Window, pack.FormOf(MachineExtra.Charge));

        foreach (MachineExtra extra in MachineExtras.Each)
        {
            Assert.Equal(extra != MachineExtra.Charge, table.HasExtra(Id("pump"), extra));
            Assert.Equal(extra == MachineExtra.Charge, table.HasExtra(Id("pack"), extra));
            Assert.False(table.HasExtra(Id("cell"), extra));
            Assert.False(table.HasExtra(content.Materials.GetNumericId(Identifier.Parse("overburden:stone")), extra));
        }
    }

    [Fact]
    public void AnUnknownExtraGetsADidYouMean()
    {
        (_, LoadResult result) = Load(MachineExtras.All, true, ("pump.json", Pump("""
            "extras": { "sence": {} },
            """, OnSignal)));

        Assert.Contains(result.Errors, e => e.Contains("pump.json") && e.Contains("'sence' is not an extra") && e.Contains("did you mean 'sense'?"));
    }

    [Theory]
    [InlineData("sense", "")]
    [InlineData("sense", """, "onsignal": { "default": 0, "min": 0, "max": 3 }""")]
    [InlineData("sense", """, "onsignal": { "default": 1, "min": 1, "max": 2 }""")]
    [InlineData("priority", "")]
    [InlineData("priority", """, "priority": { "default": 5, "min": 0, "max": 10 }""")]
    public void AnExtraThatReadsAPortNeedsItAtItsExactBounds(string extra, string ports)
    {
        // IM-1's pass, decisions 1 and 4: sense reads collapse f-1's onsignal, 0..2; priority its
        // port, 0..9 (decision 41). A mis-bounded port is a reader the slice would clamp wrongly.
        (_, LoadResult result) = Load(MachineExtras.All, true, ("pump.json", Pump($$"""
            "extras": { "{{extra}}": {} },
            """, ports)));

        Assert.Contains(result.Errors, e => e.Contains("pump.json") && e.Contains($"extra '{extra}' reads a port"));
    }

    [Fact]
    public void TheDefaultOfARequiredPortIsTheMachinesOwn()
    {
        // The quarry's onsignal defaults to 1 (decision 38); f-1's readers to 0. The bounds are the
        // rule, the default is the machine's.
        (GameContent content, LoadResult result) = Load(MachineExtras.All, true, ("pump.json", Pump("""
            "extras": { "sense": {} },
            """, """, "onsignal": { "default": 1, "min": 0, "max": 2 }""")));

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(1, content.Materials.Get(Identifier.Parse("machines:pump")).Machine!.Port("onsignal")!.Value.Default);
    }

    [Theory]
    [InlineData("cell", "charge", "is a carried storage's")]
    [InlineData("pack", "recall", "is a placed machine's")]
    [InlineData("pack", "telemetry", "is a placed machine's")]
    public void ChargeIsACarriedStoragesExtraAndTheOtherFiveAPlacedMachines(string machine, string extra, string mentioning)
    {
        string json = machine == "cell"
            ? Cell($$"""
                "extras": { "{{extra}}": {} },
                """)
            : Pack($$"""
                "extras": { "{{extra}}": {} },
                """);

        (_, LoadResult result) = Load(MachineExtras.All, true, ($"{machine}.json", json));

        Assert.Contains(result.Errors, e => e.Contains($"{machine}.json") && e.Contains(mentioning));
    }

    [Fact]
    public void AnExtraWithNoRowForItsMachinesMediumIsRefused()
    {
        (_, LoadResult result) = Load(MachineExtras.All, quantumRow: false, ("pump.json", Pump("""
            "extras": { "telemetry": {} },
            """)));

        Assert.Contains(result.Errors, e => e.Contains("pump.json") && e.Contains("no reach over 'channel'"));
    }

    [Fact]
    public void AnExtrasOptionsAreAnObject()
    {
        (_, LoadResult result) = Load(MachineExtras.All, true, ("pump.json", Pump("""
            "extras": { "telemetry": null },
            """)));

        Assert.Contains(result.Errors, e => e.Contains("pump.json") && e.Contains("must be an object"));
    }

    // ---------------------------------------------------------------- the table

    private static (GameContent Content, LoadResult Result) LoadTables(params (string Mod, string Json)[] tables)
    {
        InMemoryContentSource source = TestContent.BaseGame();
        foreach ((string mod, string json) in tables)
        {
            if (mod != "overburden")
            {
                source.Add(mod, "mod.json", $$"""{ "id": "{{mod}}", "name": "{{mod}}", "version": "1" }""");
            }

            source.Add(mod, "extras.json", json);
        }

        var content = new GameContent();
        return (content, content.Load(source));
    }

    [Fact]
    public void TheBaseRowLoadsAsSixPairsOverTheChannel()
    {
        (GameContent content, LoadResult result) = LoadTables(("overburden", QuantumRow));

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(6, result.ExtrasLoaded);
        Assert.All(content.Extras.Entries(), r => Assert.Equal("channel", r.Medium));
        Assert.Equal(
            [ExtraForm.Switch, ExtraForm.Return, ExtraForm.Board, ExtraForm.Window, ExtraForm.Port, ExtraForm.State],
            content.Extras.Entries().Select(r => r.Form));
    }

    [Theory]
    [InlineData("chanel", "is a conductor class, and the conductor index is M10e")]
    [InlineData("copper", "is a conductor class, and the conductor index is M10e")]
    [InlineData("Channel", "is not a rule name")]
    [InlineData("crank", "does not serve yet (M10f)")]
    [InlineData("adjacent:overburden:stone", "does not serve yet (M10f)")]
    [InlineData("solar", "is the sky")]
    public void AMediumIsCheckedByTheViaRuleAndTheFileIsRefusedWhole(string medium, string mentioning)
    {
        // IM-1's pass, decision 3: a misspelt medium is refused today by the rule that already refuses
        // a misspelt via, with no registry to add. The file's good row goes with it.
        (GameContent content, LoadResult result) = LoadTables(("tables", $$"""
            { "telemetry": { "channel": "state" }, "sense": { "{{medium}}": "switch" } }
            """));

        Assert.Contains(result.Errors, e => e.Contains("tables/extras.json") && e.Contains($"medium '{medium}'") && e.Contains(mentioning));
        Assert.Equal(0, content.Extras.Count);
    }

    [Fact]
    public void AMediumNearTheChannelIsNamed()
    {
        (_, LoadResult result) = LoadTables(("tables", """{ "sense": { "chanel": "switch" } }"""));

        Assert.Contains(result.Errors, e => e.Contains("conductor index is M10e — did you mean 'channel'?"));
    }

    [Theory]
    [InlineData("sense", "swich", "did you mean 'switch'?")]
    [InlineData("charge", "dock", "lower tier's charge, and the lower tiers' membership is M10e/f")]
    [InlineData("telemetry", "pin", "M10e/f")]
    [InlineData("sense", "window", "is charge's, not sense's")]
    [InlineData("recall", "", "names no form")]
    public void AFormIsOneOfItsExtrasOwn(string extra, string form, string mentioning)
    {
        (GameContent content, LoadResult result) = LoadTables(("tables", $$"""
            { "{{extra}}": { "channel": "{{form}}" } }
            """));

        Assert.Contains(result.Errors, e => e.Contains("tables/extras.json") && e.Contains(mentioning));
        Assert.Equal(0, content.Extras.Count);
    }

    [Fact]
    public void TwoModsGivingOnePairAreRefusedNamingBothFilesAndTheFirstKeepsIt()
    {
        // IM-1's pass, decision 5: one pair, one owner, as one ID has one definition — whatever form
        // the second gives. The second file's other rows stand.
        (GameContent content, LoadResult result) = LoadTables(
            ("alpha", """{ "sense": { "channel": "switch" }, "recall": { "channel": "return" } }"""),
            ("beta", """{ "sense": { "channel": "switch" }, "telemetry": { "channel": "state" } }"""));

        string error = Assert.Single(result.Errors);
        Assert.Contains("beta/extras.json", error);
        Assert.Contains("already given by alpha/extras.json", error);

        IReadOnlyList<ExtraReach> rows = content.Extras.Entries();
        Assert.Equal(3, rows.Count);
        Assert.Equal("alpha/extras.json", rows.Single(r => r.Extra == MachineExtra.Sense).File);
        Assert.Equal("beta/extras.json", rows.Single(r => r.Extra == MachineExtra.Telemetry).File);
    }

    [Fact]
    public void AThreeModFixtureLoadsTheSameInEveryListingOrder()
    {
        // The split's test: the union is a property of the mod set, never of the order a directory
        // listing happens to return. Load order is sorted, so the first owner of a pair is fixed.
        var mods = new (string Id, Action<InMemoryContentSource> Add)[]
        {
            ("overburden", s => s
                .Add("overburden", "mod.json", """{ "id": "overburden", "name": "O", "version": "1", "format": 1 }""")
                .Add("overburden", "materials/air.json", """{ "id": "air", "hardness": 0, "opaque": false, "replaceable": true }""")
                .Add("overburden", "materials/stone.json", """{ "id": "stone", "hardness": 3.0 }""")
                .Add("overburden", "extras.json", """{ "sense": { "channel": "switch" }, "priority": { "channel": "port" } }""")),
            ("alpha", s => s
                .Add("alpha", "mod.json", """{ "id": "alpha", "name": "A", "version": "1", "dependencies": ["overburden"] }""")
                .Add("alpha", "extras.json", """{ "sense": { "channel": "switch" }, "telemetry": { "channel": "state" } }""")
                .Add("alpha", "materials/pump.json", Pump("""
                    "extras": { "sense": {}, "telemetry": {} },
                    """, OnSignal))),
            ("beta", s => s
                .Add("beta", "mod.json", """{ "id": "beta", "name": "B", "version": "1" }""")
                .Add("beta", "extras.json", """{ "telemetry": { "channel": "state" }, "recall": { "channel": "return" } }""")
                .Add("beta", "materials/pump.json", Pump("""
                    "extras": { "priority": {}, "recall": {} },
                    """, Priority))),
            ("gamma", s => s
                .Add("gamma", "mod.json", """{ "id": "gamma", "name": "G", "version": "1" }""")
                .Add("gamma", "extras.json", """{ "remote": { "channel": "board" }, "priority": { "channel": "port" } }""")
                .Add("gamma", "materials/pump.json", Pump("""
                    "extras": { "remote": {}, "telemetry": {} },
                    """))),
        };

        string? first = null;
        int orders = 0;
        foreach (int[] order in Permutations([0, 1, 2, 3]))
        {
            var source = new InMemoryContentSource();
            foreach (int i in order) mods[i].Add(source);

            var content = new GameContent(MachineExtras.All);
            LoadResult result = content.Load(source);
            MaterialTable table = MaterialTable.From(content.Materials);

            string fingerprint = string.Join("\n",
                string.Join(",", content.Materials.ExportIdTable()),
                string.Join(",", content.Extras.Entries()),
                string.Join("|", result.Errors),
                string.Join(",", Enumerable.Range(0, table.Count).Select(id => table.Machine(id)?.Extras ?? MachineExtra.None)));

            first ??= fingerprint;
            Assert.Equal(first, fingerprint);
            orders++;
        }

        Assert.Equal(24, orders);

        // And the fixture exercises what it claims to: three conflicts (two against the base game, one
        // between two third-party mods), and three pumps whose extras come from three different files.
        var check = new InMemoryContentSource();
        foreach ((_, Action<InMemoryContentSource> add) in mods) add(check);
        var loaded = new GameContent(MachineExtras.All);
        LoadResult checkResult = loaded.Load(check);
        Assert.Equal(3, checkResult.Errors.Count);
        Assert.Contains(checkResult.Errors, e => e.StartsWith("alpha/extras.json") && e.Contains("sense over 'channel' is already given by overburden/extras.json"));
        Assert.Contains(checkResult.Errors, e => e.StartsWith("beta/extras.json") && e.Contains("telemetry over 'channel' is already given by alpha/extras.json"));
        Assert.Contains(checkResult.Errors, e => e.StartsWith("gamma/extras.json") && e.Contains("priority over 'channel' is already given by overburden/extras.json"));
        Assert.Equal(5, loaded.Extras.Count);
        Assert.Equal(5, checkResult.MaterialsLoaded);
    }

    private static IEnumerable<int[]> Permutations(int[] items)
    {
        if (items.Length <= 1)
        {
            yield return items;
            yield break;
        }

        for (int i = 0; i < items.Length; i++)
        {
            int[] rest = [.. items[..i], .. items[(i + 1)..]];
            foreach (int[] tail in Permutations(rest))
            {
                yield return [items[i], .. tail];
            }
        }
    }

    // ---------------------------------------------------------------- the validator, without the loader

    [Fact]
    public void TheValidatorHoldsADefinitionBuiltWithoutTheLoaderToTheSameRules()
    {
        MachineLink power = new("power", MachineMedium.Power, MachineRole.Sink, "channel", null, "channel");
        MachineLink water = new("water", MachineMedium.Fluid, MachineRole.Source, "channel", null, "channel");
        PortDefinition channel = new("channel", 0, 0, 15);
        PortDefinition onsignal = new("onsignal", 0, 0, 2);

        MachineDefinition Definition(Dictionary<MachineExtra, ExtraForm> forms, params PortDefinition[] ports) => new()
        {
            Kind = MachineKind.Pump,
            MillilitresPerSecond = 100_000,
            Links = [power, water],
            Ports = [channel, .. ports],
            ExtraForms = forms,
        };

        Assert.True(Definition(new() { [MachineExtra.Sense] = ExtraForm.Switch }, onsignal).TryValidate(out string? ok), ok);

        Assert.False(Definition(new() { [MachineExtra.Sense] = ExtraForm.Window }, onsignal).TryValidate(out string? form));
        Assert.Contains("not one of its own", form);

        Assert.False(Definition(new() { [MachineExtra.Sense] = ExtraForm.None }, onsignal).TryValidate(out _));

        Assert.False(Definition(new() { [MachineExtra.Sense] = ExtraForm.Switch }).TryValidate(out string? port));
        Assert.Contains("reads a port 'onsignal'", port);

        Assert.False(Definition(new() { [MachineExtra.Sense | MachineExtra.Recall] = ExtraForm.Switch }, onsignal).TryValidate(out string? two));
        Assert.Contains("not one extra", two);
    }

    [Fact]
    public void AMachinesMediumIsItsLinksViaTheSkyAside()
    {
        MachineLink sky = new("sky", MachineMedium.Power, MachineRole.Sink, "solar", null, null);
        MachineLink channel = new("out", MachineMedium.Power, MachineRole.Source, "channel", null, "channel");
        MachineLink copper = new("in", MachineMedium.Power, MachineRole.Sink, "copper", null, null);

        Assert.Equal("channel", MachineDefinition.MediumOf([sky, channel], out _));

        Assert.Null(MachineDefinition.MediumOf([sky], out string? none));
        Assert.Contains("names none", none);

        Assert.Null(MachineDefinition.MediumOf([], out _));

        Assert.Null(MachineDefinition.MediumOf([channel, copper], out string? two));
        Assert.Contains("name two", two);
    }
}
