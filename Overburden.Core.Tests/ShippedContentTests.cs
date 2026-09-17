using Overburden.Core.Content;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// The real <c>mods/overburden/</c> on disk, loaded through the real loader.
/// </summary>
/// <remarks>
/// <para>
/// Every other content test builds an <see cref="InMemoryContentSource"/>, which means the
/// files the game actually ships have never been read by a test. That is the wrong gap to
/// have under non-negotiable 5: the base game is a mod, and "the loader works" is a different
/// claim from "the base game's own files load".
/// </para>
/// <para>
/// It is also the only thing that can catch the shipped content saying something the game
/// depends on and no longer means — a tool tier whose threshold has drifted past stone, or a
/// material whose hardness has been edited to a value that makes it unmineable. Those are
/// data bugs, and no amount of testing the code finds them.
/// </para>
/// </remarks>
public class ShippedContentTests
{
    [Fact]
    public void TheBaseGameLoadsFromDiskWithNoErrors()
    {
        var content = new GameContent();

        LoadResult result = content.Load(new DirectoryContentSource(ModsDirectory()));

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(MaterialIds.AirNumericId, content.Materials.GetNumericId(MaterialIds.Air));
    }

    [Fact]
    public void EveryFileOnDiskIsAccountedForInTheLoadResult()
    {
        // Counts against the directory rather than against a literal, so adding a material or
        // a tier does not need this test edited — but a file that silently fails to register
        // still fails it.
        string mods = ModsDirectory();
        var content = new GameContent();

        LoadResult result = content.Load(new DirectoryContentSource(mods));

        Assert.Equal(Count(mods, "materials"), result.MaterialsLoaded);
        Assert.Equal(Count(mods, "tools"), result.ToolsLoaded);
        Assert.True(result.ToolsLoaded > 0, "the base game must ship something to mine with");

        static int Count(string mods, string kind) =>
            Directory.GetFiles(Path.Combine(mods, Identifier.BaseNamespace, kind), "*.json").Length;
    }

    [Fact]
    public void TheShippedTiersStraddleTheShippedMaterialsSoTheHardnessGateIsVisible()
    {
        // The mechanic is a property of the *content*, not of the code: if every shipped tier
        // could take every shipped material, the hardness gate would be dead weight that
        // nothing demonstrates and nobody would notice regressing. Written against whatever
        // is on disk rather than against remembered numbers, so editing a JSON file to break
        // the mechanic fails here rather than in a playtest.
        var content = new GameContent();
        content.Load(new DirectoryContentSource(ModsDirectory()));

        var tiers = content.Tools.Entries().Select(e => e.Value).ToList();
        var solids = content.Materials.Entries()
            .Select(e => e.Value)
            .Where(m => !m.IsAir)
            .ToList();

        Assert.NotEmpty(tiers);
        Assert.NotEmpty(solids);

        float softest = solids.Min(m => m.Hardness);
        float hardest = solids.Max(m => m.Hardness);

        Assert.True(
            tiers.Any(t => t.HardnessThreshold >= softest && t.HardnessThreshold < hardest),
            "no shipped tier leaves anything standing, so nothing shows what a tool is for");

        Assert.True(
            tiers.Any(t => t.HardnessThreshold >= hardest),
            "no shipped tier can take the hardest material, so the deepest rock is unmineable");
    }

    [Fact]
    public void TheRelayPairShipsAsMachinesABodyCanBeDismantledFrom()
    {
        // M10a-2: both bodies load as machines counted in cells, and a shipped tier can take a
        // body — a machine nobody can mine is a machine nobody can dismantle, which is why the
        // relay ships at hardness 3.0 and not the ladder's 5.0.
        var content = new GameContent();
        LoadResult result = content.Load(new DirectoryContentSource(ModsDirectory()));
        Assert.True(result.Success, string.Join("; ", result.Errors));

        MaterialDefinition pump = content.Materials.Get(Identifier.Parse("overburden:relay_pump"));
        MaterialDefinition outlet = content.Materials.Get(Identifier.Parse("overburden:relay_outlet"));
        Assert.True(pump.IsMachine && outlet.IsMachine);
        Assert.Equal(Overburden.Core.Machines.MachineKind.Pump, pump.Machine!.Kind);
        Assert.Equal(Overburden.Core.Machines.MachineKind.Outlet, outlet.Machine!.Kind);
        Assert.Equal(MaterialUnit.Cells, pump.Unit);
        Assert.Equal(MaterialUnit.Cells, outlet.Unit);
        Assert.NotNull(pump.Machine.Port("channel"));
        Assert.NotNull(outlet.Machine.Port("channel"));

        float tallest = content.Tools.Entries().Max(e => e.Value.HardnessThreshold);
        Assert.True(pump.Hardness <= tallest, "no shipped tier can take a relay pump body, so none can be dismantled");
        Assert.True(outlet.Hardness <= tallest, "no shipped tier can take a relay outlet body, so none can be dismantled");

        MaterialTable table = MaterialTable.From(content.Materials);
        Assert.Equal(5, table.MachineIds.Count);

        // M10b: only a ring has weights, so the pump declares none and the outlet does; a port
        // the panel would list that nothing reads is the thing decision 11 refuses a via for.
        Assert.Null(pump.Machine.Port("weight"));
        Assert.Equal(new Overburden.Core.Machines.PortDefinition("weight", 1, 1, 100), outlet.Machine.Port("weight"));
    }

    [Fact]
    public void TheComputerShipsAsAnItemInTheHand()
    {
        // M10b, decision 1: overburden:computer, counted by the piece, never a block.
        var content = new GameContent();
        LoadResult result = content.Load(new DirectoryContentSource(ModsDirectory()));
        Assert.True(result.Success, string.Join("; ", result.Errors));

        MaterialDefinition computer = content.Materials.Get(Identifier.Parse("overburden:computer"));
        Assert.False(computer.IsPlaceable);
        Assert.False(computer.IsMachine);
        Assert.Equal(MaterialUnit.Count, computer.Unit);

        MaterialTable table = MaterialTable.From(content.Materials);
        int id = content.Materials.GetNumericId(Identifier.Parse("overburden:computer"));
        Assert.False(table.IsPlaceable(id));

        // Every shipped block is placeable; the computer and the pack are the two things that
        // are not (M10c-2 added the pack).
        int notPlaceable = 0;
        for (int i = 0; i < table.Count; i++)
        {
            if (!table.IsPlaceable(i)) notPlaceable++;
        }

        Assert.Equal(2, notPlaceable);
    }

    [Fact]
    public void ThePowerTrioShipsOnTheChannelClass()
    {
        // M10c-2: the panel and the cell are bodies, the pack is an item; all three on the
        // channel class until M10i re-classes the panel and the pack to dc.
        var content = new GameContent();
        LoadResult result = content.Load(new DirectoryContentSource(ModsDirectory()));
        Assert.True(result.Success, string.Join("; ", result.Errors));

        MaterialDefinition panel = content.Materials.Get(Identifier.Parse("overburden:solar_panel"));
        MaterialDefinition cell = content.Materials.Get(Identifier.Parse("overburden:vacuum_cell"));
        MaterialDefinition pack = content.Materials.Get(Identifier.Parse("overburden:lead_acid_pack"));

        Assert.Equal(Overburden.Core.Machines.MachineKind.Converter, panel.Machine!.Kind);
        Assert.Equal(500L, panel.Machine.RatedWatts);
        Assert.Equal(Overburden.Core.Machines.MachineDefinition.PerMille, panel.Machine.EfficiencyPerMille);
        Assert.Equal(Overburden.Core.Machines.MachineDefinition.SolarVia, panel.Machine.Link("sky")!.Via);
        Assert.True(panel.Machine.Link("out")!.IsChannel);
        Assert.True(panel.IsPlaceable);
        Assert.Equal(MaterialUnit.Cells, panel.Unit);

        Assert.Equal(Overburden.Core.Machines.MachineKind.Storage, cell.Machine!.Kind);
        Assert.Equal(5_000_000_000L * 1000L, cell.Machine.CapacityMillijoules);
        Assert.False(cell.Machine.Carried);
        Assert.True(cell.IsPlaceable);

        Assert.Equal(Overburden.Core.Machines.MachineKind.Storage, pack.Machine!.Kind);
        Assert.True(pack.Machine.Carried);
        Assert.Equal(3_600_000L * 1000L, pack.Machine.CapacityMillijoules);
        Assert.Equal(1000L, pack.Machine.RechargeWatts);
        Assert.False(pack.IsPlaceable);
        Assert.Equal(MaterialUnit.Count, pack.Unit);

        // The relay's bill against a panel's boundary: 200 panels to run it flat out, which is
        // the pass's fact 5 and the reason --pump places a charged cell.
        MaterialDefinition relay = content.Materials.Get(Identifier.Parse("overburden:relay_pump"));
        long relayBoundaryMillijoules = relay.Machine!.MillilitresPerSecond / 2 * relay.Machine.JoulesPerLitre;
        long panelBoundaryMillijoules = panel.Machine.RatedWatts * 500;
        Assert.Equal(200L, relayBoundaryMillijoules / panelBoundaryMillijoules);

        // Every shipped tier can take the panel and the cell, so both can be dismantled.
        float tallest = content.Tools.Entries().Max(e => e.Value.HardnessThreshold);
        Assert.True(panel.Hardness <= tallest);
        Assert.True(cell.Hardness <= tallest);
    }

    [Fact]
    public void EveryShippedTierIsUsable()
    {
        // A tier with no reach, or one that cannot take even the softest thing in the world,
        // loads perfectly happily and is simply a tool that does nothing.
        var content = new GameContent();
        content.Load(new DirectoryContentSource(ModsDirectory()));

        float softest = content.Materials.Entries()
            .Select(e => e.Value)
            .Where(m => !m.IsAir)
            .Min(m => m.Hardness);

        foreach (var tier in content.Tools.Entries().Select(e => e.Value))
        {
            Assert.InRange(tier.RadiusMetres, VoxelConstants.VoxelSize, ToolDefinition.MaxRadiusMetres);
            Assert.True(tier.CooldownSeconds > 0f, $"{tier.Id} swings infinitely fast");
            Assert.True(
                tier.HardnessThreshold >= softest,
                $"{tier.Id} cannot take even the softest material in the world");
        }
    }

    [Fact]
    public void SomethingShippedActuallyEmitsLight()
    {
        // The format having a `lightEmission` field is not the same claim as the content using
        // it. With nothing emitting, every block-light test in the suite passes against a world
        // that can never be lit, and the first person to notice is whoever plays M5d in a cave.
        var content = new GameContent();
        content.Load(new DirectoryContentSource(ModsDirectory()));

        var emitters = content.Materials.Entries()
            .Select(entry => entry.Value)
            .Where(material => material.LightEmission > 0)
            .ToList();

        Assert.True(emitters.Count > 0, "nothing the base game ships emits any light");

        foreach (var emitter in emitters)
        {
            Assert.InRange(emitter.LightEmission, 1, MaterialDefinition.MaxLightEmission);

            // An emitter that is not opaque would be a light you can see through, which is fine
            // — but one that is *air* is a light with no block to place, and the debug bind
            // would place nothing at all.
            Assert.False(emitter.IsAir, $"{emitter.Id} emits light and is air");
        }

        // ...and something reaches the top of the range, or the 0..15 decision is untested by
        // the content and the brightest lamp anyone can build is dimmer than the format allows.
        Assert.Contains(emitters, e => e.LightEmission == MaterialDefinition.MaxLightEmission);
    }

    [Fact]
    public void ShippedRockIsPermeableSomewhereAndSealedSomewhere()
    {
        // The seep index files a face only through rock whose permeability is above zero, so a
        // base game whose every solid material read 0 would have a seepage system that never
        // fires — and one whose every material leaked would have no way to line a shaft. Both
        // ends have to exist in the content for either half of M8c to be tested by play.
        var content = new GameContent();
        content.Load(new DirectoryContentSource(ModsDirectory()));

        var rocks = content.Materials.Entries()
            .Select(entry => entry.Value)
            .Where(material => !material.IsAir && !material.IsFluid)
            .ToList();

        Assert.NotEmpty(rocks);
        foreach (var rock in rocks)
        {
            Assert.InRange(rock.Permeability, 0f, 1f);
        }

        Assert.Contains(rocks, r => r.Permeability > 0f);
        Assert.Contains(rocks, r => r.Permeability == 0f);

        // Stone under dirt is the ordering the seep rate's estimate assumes: a room in stone
        // takes hours to wet, one in dirt minutes.
        float stone = content.Materials.Get(Identifier.Parse("overburden:stone")).Permeability;
        float dirt = content.Materials.Get(Identifier.Parse("overburden:dirt")).Permeability;
        Assert.True(stone > 0f && dirt > stone, $"stone {stone}, dirt {dirt}");
    }

    /// <summary>
    /// The <c>mods/</c> directory the game runs against, found by walking up to the solution.
    /// </summary>
    /// <remarks>
    /// Fails rather than skips when it cannot be found. A content test that quietly does
    /// nothing outside one particular layout is worse than no content test, because the suite
    /// still reports it as passing.
    /// </remarks>
    internal static string ModsDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "Overburden.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.True(
            directory is not null,
            $"no Overburden.slnx above {AppContext.BaseDirectory}");

        string mods = Path.Combine(directory!.FullName, "Overburden.Game", "mods");
        Assert.True(Directory.Exists(mods), $"no mods directory at {mods}");

        return mods;
    }
}
