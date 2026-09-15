using Overburden.Core.Content;
using Overburden.Core.Machines;

namespace Overburden.Core.Tests;

/// <summary>
/// The <c>machine</c> block through the loader (M10a-2, decision 11): the whole grammar reads,
/// every rule refuses naming the file, and a <c>via</c> this build does not serve is refused
/// naming the part that will.
/// </summary>
public class MachineContentTests
{
    private const string Mod = """{ "id": "pumps", "name": "Pumps", "version": "1" }""";

    private const string Pump = """
        { "id": "pump", "hardness": 3.0, "supportDistance": 8, "unit": "cells",
          "machine": { "kind": "pump", "litresPerSecond": 100, "headMetres": null, "joulesPerLitre": 1000, "intakeRadiusCells": 1,
            "links": { "power": { "carries": "power", "role": "sink",   "via": "channel", "port": "channel" },
                       "water": { "carries": "fluid", "role": "source", "via": "channel", "port": "channel" } },
            "ports": { "channel": { "default": 0, "min": 0, "max": 15 }, "weight": { "default": 1, "min": 1, "max": 100 } } } }
        """;

    private const string Outlet = """
        { "id": "outlet", "hardness": 3.0, "supportDistance": 8, "unit": "cells",
          "machine": { "kind": "outlet",
            "links": { "water": { "carries": "fluid", "role": "sink", "via": "channel", "port": "channel" } },
            "ports": { "channel": { "default": 0, "min": 0, "max": 15 } } } }
        """;

    private static (GameContent Content, LoadResult Result) Load(params (string File, string Json)[] files)
    {
        InMemoryContentSource source = TestContent.BaseGame().Add("pumps", "mod.json", Mod);
        foreach ((string file, string json) in files)
        {
            source.Add("pumps", $"materials/{file}", json);
        }

        var content = new GameContent();
        return (content, content.Load(source));
    }

    private static void Refused(string file, string json, string mentioning)
    {
        (_, LoadResult result) = Load((file, json));

        Assert.False(result.Success, $"{file} loaded: {json}");
        Assert.Contains(result.Errors, e => e.Contains(file) && e.Contains(mentioning, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheRelayPairLoadsWithEveryFieldRead()
    {
        (GameContent content, LoadResult result) = Load(("pump.json", Pump), ("outlet.json", Outlet));

        Assert.True(result.Success, string.Join("; ", result.Errors));

        MaterialDefinition pump = content.Materials.Get(Identifier.Parse("pumps:pump"));
        Assert.True(pump.IsMachine);
        Assert.Equal(MaterialUnit.Cells, pump.Unit);
        MachineDefinition machine = pump.Machine!;
        Assert.Equal(MachineKind.Pump, machine.Kind);
        Assert.Equal(100_000L, machine.MillilitresPerSecond);
        Assert.Null(machine.HeadMetres);
        Assert.Equal(1000L, machine.JoulesPerLitre);
        Assert.Equal(1, machine.IntakeRadiusCells);
        Assert.Equal(2, machine.Links.Count);
        Assert.Equal(MachineRole.Source, machine.Link("water")!.Role);
        Assert.True(machine.Link("water")!.IsChannel);
        Assert.Equal("channel", machine.Link("power")!.Port);
        Assert.Equal(new PortDefinition("channel", 0, 0, 15), machine.Port("channel"));
        Assert.Equal(new PortDefinition("weight", 1, 1, 100), machine.Port("weight"));

        MaterialDefinition outlet = content.Materials.Get(Identifier.Parse("pumps:outlet"));
        Assert.Equal(MachineKind.Outlet, outlet.Machine!.Kind);
        Assert.Equal(0L, outlet.Machine.MillilitresPerSecond);

        MaterialTable table = MaterialTable.From(content.Materials);
        int pumpId = content.Materials.GetNumericId(Identifier.Parse("pumps:pump"));
        Assert.True(table.IsMachine(pumpId));
        Assert.Same(machine, table.Machine(pumpId));
        Assert.Equal(MaterialUnit.Cells, table.Unit(pumpId));
        Assert.False(table.IsMachine(content.Materials.GetNumericId(Identifier.Parse("overburden:stone"))));
        Assert.Equal(2, table.MachineIds.Count);
    }

    [Fact]
    public void AMaterialWithNoMachineBlockIsOnlyAVoxel()
    {
        (GameContent content, LoadResult result) = Load();
        Assert.True(result.Success);
        Assert.False(content.Materials.Get(Identifier.Parse("overburden:stone")).IsMachine);
        Assert.Equal(MaterialUnit.Litres, content.Materials.Get(Identifier.Parse("overburden:stone")).Unit);
        Assert.Empty(MaterialTable.From(content.Materials).MachineIds);
    }

    [Fact]
    public void AKindThisBuildDoesNotServeIsRefusedNamingThePart()
    {
        Refused("gen.json", """{ "id": "gen", "machine": { "kind": "generator", "links": {}, "ports": {} } }""", "M10c");
        Refused("station.json", """{ "id": "station", "machine": { "kind": "station", "links": {}, "ports": {} } }""", "M10g");
        Refused("odd.json", """{ "id": "odd", "machine": { "kind": "teleporter" } }""", "not a machine kind");
        Refused("none.json", """{ "id": "none", "machine": { } }""", "kind");
    }

    [Fact]
    public void AViaThisBuildDoesNotServeIsRefusedNamingThePart()
    {
        // crank: an engine rule, M10f. copper: a conductor class, M10e. Both are refused rather
        // than loaded to tick nothing — a lamp that does not glow, against a format mods write to.
        Refused("crank.json", """
            { "id": "crank", "machine": { "kind": "pump", "litresPerSecond": 1,
              "links": { "water": { "carries": "fluid", "role": "source", "via": "crank" } }, "ports": {} } }
            """, "M10f");
        Refused("adjacent.json", """
            { "id": "adjacent", "machine": { "kind": "pump", "litresPerSecond": 1,
              "links": { "water": { "carries": "fluid", "role": "source", "via": "adjacent:boiler" } }, "ports": {} } }
            """, "M10f");
        Refused("wired.json", """
            { "id": "wired", "machine": { "kind": "pump", "litresPerSecond": 1,
              "links": { "water": { "carries": "fluid", "role": "source", "via": "copper" } }, "ports": {} } }
            """, "M10e");
        Refused("badvia.json", """
            { "id": "badvia", "machine": { "kind": "pump", "litresPerSecond": 1,
              "links": { "water": { "carries": "fluid", "role": "source", "via": "Not A Rule" } }, "ports": {} } }
            """, "not a rule name");
    }

    [Fact]
    public void AChannelLinkNeedsADeclaredPortAndNoFace()
    {
        Refused("noport.json", """
            { "id": "noport", "machine": { "kind": "pump", "litresPerSecond": 1,
              "links": { "water": { "carries": "fluid", "role": "source", "via": "channel" } }, "ports": {} } }
            """, "names no declared port");
        Refused("wrongport.json", """
            { "id": "wrongport", "machine": { "kind": "pump", "litresPerSecond": 1,
              "links": { "water": { "carries": "fluid", "role": "source", "via": "channel", "port": "band" } },
              "ports": { "channel": { "min": 0, "max": 15 } } } }
            """, "names no declared port");
        Refused("faced.json", """
            { "id": "faced", "machine": { "kind": "pump", "litresPerSecond": 1,
              "links": { "water": { "carries": "fluid", "role": "source", "via": "channel", "port": "channel", "face": "top" } },
              "ports": { "channel": { "min": 0, "max": 15 } } } }
            """, "declares a face");
    }

    [Fact]
    public void AKindNeedsItsFluidLink()
    {
        Refused("dry.json", """
            { "id": "dry", "machine": { "kind": "pump", "litresPerSecond": 1,
              "links": { "power": { "carries": "power", "role": "sink", "via": "channel", "port": "channel" } },
              "ports": { "channel": { "min": 0, "max": 15 } } } }
            """, "fluid link with role source");
        Refused("mute.json", """
            { "id": "mute", "machine": { "kind": "outlet", "links": {}, "ports": {} } }
            """, "fluid link with role sink");
    }

    [Fact]
    public void TheNumbersAreBoundedAndRefusedNotClamped()
    {
        string pumpWith(string extra) => $$"""
            { "id": "p", "machine": { "kind": "pump", {{extra}}
              "links": { "water": { "carries": "fluid", "role": "source", "via": "channel", "port": "channel" } },
              "ports": { "channel": { "min": 0, "max": 15 } } } }
            """;

        Refused("p.json", pumpWith("\"litresPerSecond\": 0,"), "litresPerSecond");
        Refused("p.json", pumpWith("\"litresPerSecond\": 54001,"), "litresPerSecond");
        Refused("p.json", pumpWith("\"litresPerSecond\": 0.0001,"), "whole number of millilitres");
        Refused("p.json", pumpWith("\"litresPerSecond\": 1, \"intakeRadiusCells\": 3,"), "intakeRadiusCells");
        Refused("p.json", pumpWith("\"litresPerSecond\": 1, \"intakeRadiusCells\": 0,"), "intakeRadiusCells");
        Refused("p.json", pumpWith("\"litresPerSecond\": 1, \"headMetres\": 2000,"), "headMetres");
        Refused("p.json", pumpWith("\"litresPerSecond\": 1, \"headMetres\": 0,"), "headMetres");
        Refused("p.json", pumpWith("\"litresPerSecond\": 1, \"joulesPerLitre\": -1,"), "joulesPerLitre");

        // The ends of the ranges are inside them, and a fractional rate that is whole in
        // millilitres is exact.
        (GameContent content, LoadResult ok) = Load(("p.json", pumpWith("\"litresPerSecond\": 0.47, \"intakeRadiusCells\": 2, \"headMetres\": 1024,")));
        Assert.True(ok.Success, string.Join("; ", ok.Errors));
        Assert.Equal(470L, content.Materials.Get(Identifier.Parse("pumps:p")).Machine!.MillilitresPerSecond);
    }

    [Fact]
    public void PortsAreBoundedNamedAndAtMostSixteen()
    {
        string outletWith(string ports) => $$"""
            { "id": "o", "machine": { "kind": "outlet",
              "links": { "water": { "carries": "fluid", "role": "sink", "via": "channel", "port": "channel" } },
              "ports": { "channel": { "min": 0, "max": 15 }{{ports}} } } }
            """;

        Refused("o.json", outletWith(", \"Bad\": { \"min\": 0, \"max\": 1 }"), "not a port name");
        Refused("o.json", outletWith(", \"w\": { \"default\": 5, \"min\": 0, \"max\": 4 }"), "does not hold");
        Refused("o.json", outletWith(", \"w\": { \"min\": 3, \"max\": 1 }"), "does not hold");
        Refused("o.json", outletWith(", \"w\": { \"min\": 0 }"), "needs min and max");

        string sixteenMore = string.Concat(Enumerable.Range(0, 16).Select(i => $", \"p{i}\": {{ \"min\": 0, \"max\": 1 }}"));
        Refused("o.json", outletWith(sixteenMore), "at most");

        // Default absent means min.
        (GameContent content, LoadResult ok) = Load(("o.json", outletWith(", \"w\": { \"min\": 2, \"max\": 9 }")));
        Assert.True(ok.Success, string.Join("; ", ok.Errors));
        Assert.Equal(2L, content.Materials.Get(Identifier.Parse("pumps:o")).Machine!.Port("w")!.Value.Default);
    }

    [Fact]
    public void LinkShapeErrorsAreRefused()
    {
        Refused("l.json", """
            { "id": "l", "machine": { "kind": "outlet",
              "links": { "water": { "carries": "steam", "role": "sink", "via": "channel", "port": "channel" } },
              "ports": { "channel": { "min": 0, "max": 15 } } } }
            """, "carries must be");
        Refused("l.json", """
            { "id": "l", "machine": { "kind": "outlet",
              "links": { "water": { "carries": "fluid", "role": "both", "via": "channel", "port": "channel" } },
              "ports": { "channel": { "min": 0, "max": 15 } } } }
            """, "role must be");
        Refused("l.json", """
            { "id": "l", "machine": { "kind": "outlet",
              "links": { "water": { "carries": "fluid", "role": "sink" } },
              "ports": { "channel": { "min": 0, "max": 15 } } } }
            """, "names no via");
        Refused("l.json", """
            { "id": "l", "machine": { "kind": "outlet",
              "links": { "Water": { "carries": "fluid", "role": "sink", "via": "channel", "port": "channel" } },
              "ports": { "channel": { "min": 0, "max": 15 } } } }
            """, "not a link name");
    }

    [Fact]
    public void AFluidCannotBeAMachineAndAUnitMustBeOneOfThree()
    {
        Refused("wet.json", """{ "id": "wet", "fluid": true, "machine": { "kind": "outlet" } }""", "fluid cannot carry");
        Refused("u.json", """{ "id": "u", "unit": "kilograms" }""", "unit");
    }

    [Fact]
    public void TheTableDoesNotProjectABlockTheLoaderWouldRefuse()
    {
        // The loader is not the only way a definition reaches the table. A block built by hand
        // that fails validation is a voxel and nothing more — belt to the loader's braces.
        var registry = new Registry<MaterialDefinition>("materials", MaterialDefinition.Unknown);
        registry.Register(MaterialIds.Air, new MaterialDefinition { Id = MaterialIds.Air, IsOpaque = false, IsReplaceable = true });
        registry.Register(Identifier.Parse("overburden:bad"), new MaterialDefinition
        {
            Id = Identifier.Parse("overburden:bad"),
            Machine = new MachineDefinition { Kind = MachineKind.Pump, MillilitresPerSecond = 0 },
        });
        registry.Freeze();

        MaterialTable table = MaterialTable.From(registry);
        Assert.False(table.IsMachine(1));
        Assert.Null(table.Machine(1));
    }
}
