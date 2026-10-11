using Overburden.Core.Content;
using Overburden.Core.Machines;
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

        // The tiers are the T cycle — every tool, in the cycle's own order, since the quantum
        // tiers joined it (2026-09-19; M10c-3's decision 5 kept the one costing tool off it). The
        // "leaves something standing" claim is made of the *free* tiers, because the quantum
        // ones take anything and would make it true of the picks and untested of the cycle.
        var tiers = ToolTiers.Order(content.Tools.Entries().Select(e => e.Value));
        var free = tiers.Where(t => t.IsFree).ToList();
        var solids = content.Materials.Entries()
            .Select(e => e.Value)
            .Where(m => !m.IsAir)
            .ToList();

        Assert.NotEmpty(free);
        Assert.NotEmpty(solids);

        float softest = solids.Min(m => m.Hardness);
        float hardest = solids.Max(m => m.Hardness);

        Assert.True(
            free.Any(t => t.HardnessThreshold >= softest && t.HardnessThreshold < hardest),
            "no shipped free tier leaves anything standing, so nothing shows what a tool is for");

        Assert.True(
            tiers.Any(t => t.HardnessThreshold >= hardest),
            "no shipped tier can take the hardest material, so the deepest rock is unmineable");

        // A fresh world starts on a free tier that takes stone: the first swing must do something
        // and must not cost from an empty pack.
        ToolDefinition start = ToolTiers.Default(tiers);
        Assert.True(start.IsFree, $"{start.Id} is the starting tier and draws power");
        Assert.Equal(ToolBite.Sphere, start.Bite);
        Assert.True(start.HardnessThreshold >= softest, $"{start.Id} is the starting tier and takes nothing shipped");
    }

    /// <summary>The two quantum tiers (2026-09-19): small scale is the cube, large scale the chunk, both at range and both costing.</summary>
    private const string QuantumMinerId = "overburden:quantum_miner";
    private const string QuantumExcavatorId = "overburden:quantum_excavator";

    [Fact]
    public void TheQuantumTunnellerShipsBetweenTheMinerAndTheExcavator()
    {
        // Matthew, 2026-09-23: "a quantum tunneller that does about 2x2 meters to go between the
        // quantum miner, and quantum excavator" — a 2 m cube, a bite a click, Q held at 0.25 s.
        var content = new GameContent();
        LoadResult result = content.Load(new DirectoryContentSource(ModsDirectory()));
        Assert.True(result.Success, string.Join("; ", result.Errors));

        ToolDefinition tunneller = content.Tools.Get(Identifier.Parse("overburden:quantum_tunneller"));
        Assert.Equal(ToolBite.Tunnel, tunneller.Bite);
        Assert.True(tunneller.AtRange);
        Assert.Equal(0.25f, tunneller.CooldownSeconds);
        Assert.Equal(5.0f, tunneller.HardnessThreshold);
        Assert.Equal(50L, tunneller.Power!.JoulesPerVoxel);

        string[] cycle = ToolTiers.Order(content.Tools.Entries().Select(e => e.Value).Where(t => !t.IsUnknown))
            .Select(t => t.Id.ToString()).ToArray();
        int miner = Array.IndexOf(cycle, QuantumMinerId);
        int tunnel = Array.IndexOf(cycle, "overburden:quantum_tunneller");
        int excavator = Array.IndexOf(cycle, QuantumExcavatorId);
        Assert.True(miner >= 0 && tunnel == miner + 1 && excavator == tunnel + 1, string.Join(", ", cycle));
    }

    [Fact]
    public void TheQuantumTiersShipAsTwoCostingToolsOnTheCycleThatTakeAnything()
    {
        // Matthew's IDs and his split — "quantum (small scale) and quantum (large scale) … tiers
        // like in hand/steel". A threshold of 5.0 so either reclaims a machine body at range as a
        // pick does (the ladder's rule), the cube at 0.1 s and the chunk at 0.25 s as the files'
        // cooldowns, and one price a voxel — the tier is the shape, the voxel is the price.
        var content = new GameContent();
        LoadResult result = content.Load(new DirectoryContentSource(ModsDirectory()));
        Assert.True(result.Success, string.Join("; ", result.Errors));

        ToolDefinition miner = content.Tools.Get(Identifier.Parse(QuantumMinerId));
        ToolDefinition excavator = content.Tools.Get(Identifier.Parse(QuantumExcavatorId));
        Assert.Equal(ToolBite.Cube, miner.Bite);
        Assert.Equal(ToolBite.Chunk, excavator.Bite);
        Assert.True(miner.AtRange);
        Assert.True(excavator.AtRange);
        Assert.Equal(0.1f, miner.CooldownSeconds);
        Assert.Equal(0.25f, excavator.CooldownSeconds);

        float hardest = content.Materials.Entries().Select(e => e.Value).Where(m => !m.IsAir).Max(m => m.Hardness);
        foreach (ToolDefinition tool in new[] { miner, excavator })
        {
            Assert.Equal(5.0f, tool.HardnessThreshold);
            Assert.NotNull(tool.Power);
            Assert.Equal(Overburden.Core.Machines.MachineLink.ChannelVia, tool.Power!.Via);
            Assert.Equal(50L, tool.Power.JoulesPerVoxel);
            Assert.False(tool.IsFree);
            Assert.True(tool.HardnessThreshold >= hardest);
        }

        // They are the last three on the cycle — miner, tunneller (2026-09-23), excavator — and
        // none is the start.
        var tiers = ToolTiers.Order(content.Tools.Entries().Select(e => e.Value));
        Assert.Equal(QuantumMinerId, tiers[^3].Id.ToString());
        Assert.Equal("overburden:quantum_tunneller", tiers[^2].Id.ToString());
        Assert.Equal(QuantumExcavatorId, tiers[^1].Id.ToString());
        Assert.True(ToolTiers.Default(tiers).IsFree);

        // The arithmetic the play feels, at 50 J a voxel (100 until 2026-09-18's first play read
        // 56 s as "you don't get far", 20 for an hour after): a full lead-acid pack is 1,125 cube
        // bites — 1.9 min at ten a second — and two chunk bites, so the excavator is a
        // two-chunks-a-pack tool until M10i's battery, which is the ladder's "only once you have
        // the latest tier battery will it be as effective as it is now" as a number.
        MaterialDefinition pack = content.Materials.Get(Identifier.Parse("overburden:lead_acid_pack"));
        long cubeMillijoules = miner.Power.MillijoulesPerVoxel * Overburden.Core.Mining.Carve.CubeVoxels
                               * Overburden.Core.Mining.Carve.CubeVoxels * Overburden.Core.Mining.Carve.CubeVoxels;
        long chunkMillijoules = excavator.Power.MillijoulesPerVoxel * Overburden.Core.Voxels.VoxelConstants.ChunkVolume;
        Assert.Equal(1125L, pack.Machine!.CapacityMillijoules / cubeMillijoules);
        Assert.Equal(2L, pack.Machine!.CapacityMillijoules / chunkMillijoules);

        // The tunneller between them: 512 voxels a bite, 25.6 kJ, 140 bites a pack.
        long tunnelMillijoules = miner.Power.MillijoulesPerVoxel * Overburden.Core.Mining.Carve.TunnelVoxels
                                 * Overburden.Core.Mining.Carve.TunnelVoxels * Overburden.Core.Mining.Carve.TunnelVoxels;
        Assert.Equal(140L, pack.Machine!.CapacityMillijoules / tunnelMillijoules);
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
        Assert.Equal(7, table.MachineIds.Count); // IM-2 added the quantum backpack, 2026-10-12 the lithium-ion pack

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

        // Every shipped block is placeable; the computer, the three packs and water are the things
        // that are not (M10c-2 added the lead-acid pack, IM-2 the quantum backpack, IM-4a water,
        // 2026-10-12 the lithium-ion pack).
        int notPlaceable = 0;
        for (int i = 0; i < table.Count; i++)
        {
            if (!table.IsPlaceable(i)) notPlaceable++;
        }

        Assert.Equal(5, notPlaceable);
    }

    [Fact]
    public void TheQuantumBackpackShipsAsAWindowAndTheRelayPumpWithAPriority()
    {
        // IM-2: decision 7's JSON, pinned — a carried storage with the charge extra's window form,
        // no capacity, no recharge, not a programmer — and decision 41's port on the one shipped
        // consumer, at the middle so every played world keeps its order.
        var content = new GameContent();
        LoadResult result = content.Load(new DirectoryContentSource(ModsDirectory()));
        Assert.True(result.Success, string.Join("; ", result.Errors));

        MaterialDefinition backpack = content.Materials.Get(Identifier.Parse("overburden:quantum_backpack"));
        Assert.False(backpack.IsPlaceable);
        Assert.False(backpack.IsProgrammer);
        Assert.Equal(MaterialUnit.Count, backpack.Unit);
        Assert.Equal(13, backpack.TextureLayer);
        MachineDefinition window = backpack.Machine!;
        Assert.True(window.IsWindow);
        Assert.True(window.Carried);
        Assert.Equal(0L, window.CapacityMillijoules);
        Assert.Null(window.RechargeWatts);
        Assert.True(window.Link("charge")!.IsChannel);

        MachineDefinition relay = content.Materials.Get(Identifier.Parse("overburden:relay_pump")).Machine!;
        Assert.Equal(ExtraForm.Port, relay.FormOf(MachineExtra.Priority));
        Assert.Equal(new PortDefinition(MachineTick.PriorityPort, MachineTick.DefaultPriority, 0, 9), relay.Port(MachineTick.PriorityPort));

        // The lead-acid pack is unchanged (decision 7): it still holds its own charge.
        Assert.False(content.Materials.Get(Identifier.Parse("overburden:lead_acid_pack")).Machine!.IsWindow);
    }

    [Fact]
    public void TheComputerIsTheOneShippedProgrammer()
    {
        // IDEAL-MACHINES-PLAN decision 6: the verb is keyed by "programmer", so the pack — the other
        // shipped item — and every item to come (water, parts) never opens a panel.
        var content = new GameContent();
        LoadResult result = content.Load(new DirectoryContentSource(ModsDirectory()));
        Assert.True(result.Success, string.Join("; ", result.Errors));

        MaterialTable table = MaterialTable.From(content.Materials);
        int computer = content.Materials.GetNumericId(Identifier.Parse("overburden:computer"));
        Assert.True(table.IsProgrammer(computer));
        Assert.Equal([computer], Enumerable.Range(0, table.Count).Where(table.IsProgrammer));
    }

    [Fact]
    public void TheBaseGameShipsFormatOneAndTheQuantumRowAndOptsIntoOnlyServedExtras()
    {
        // IM-1's pass: the manifest declares the format it was written for (decision 7); the table
        // ships the quantum row now (decision 3); and a machine opts into an extra only once the
        // slice that reads it has landed (decision 2) — IM-2's window and the pump's priority.
        string mods = ModsDirectory();
        var content = new GameContent();
        LoadResult result = content.Load(new DirectoryContentSource(mods));
        Assert.True(result.Success, string.Join("; ", result.Errors));

        using (var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(mods, Identifier.BaseNamespace, "mod.json"))))
        {
            Assert.Equal(ModManifest.CurrentFormat, manifest.RootElement.GetProperty("format").GetInt32());
        }

        Assert.Equal(6, result.ExtrasLoaded);
        Assert.Equal(
            [
                new ExtraReach(MachineExtra.Sense, "channel", ExtraForm.Switch, "overburden/extras.json"),
                new ExtraReach(MachineExtra.Recall, "channel", ExtraForm.Return, "overburden/extras.json"),
                new ExtraReach(MachineExtra.Remote, "channel", ExtraForm.Board, "overburden/extras.json"),
                new ExtraReach(MachineExtra.Charge, "channel", ExtraForm.Window, "overburden/extras.json"),
                new ExtraReach(MachineExtra.Priority, "channel", ExtraForm.Port, "overburden/extras.json"),
                new ExtraReach(MachineExtra.Telemetry, "channel", ExtraForm.State, "overburden/extras.json"),
            ],
            content.Extras.Entries());

        MaterialTable table = MaterialTable.From(content.Materials);
        Assert.All(table.MachineIds, id => Assert.Equal(MachineExtra.None, table.Machine(id)!.Extras & ~MachineExtras.Served));

        int Id(string path) => content.Materials.GetNumericId(Identifier.Parse($"overburden:{path}"));
        Assert.Equal(MachineExtra.Charge, table.Machine(Id("quantum_backpack"))!.Extras);
        Assert.Equal(MachineExtra.Priority, table.Machine(Id("relay_pump"))!.Extras);
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
        Assert.Equal(250L, pack.Machine.RechargeWatts);
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
    public void TheLithiumIonPackHoldsFourLeadAcidsAndFillsInHalfTheTime()
    {
        // 2026-10-12, his ask ("Lead acid is so 20th century") and his two numbers: 4 kWh in a pack
        // the lead-acid's size, the real density ratio at its cautious end, and 2 kW, full in two
        // hours where lead-acid takes four, li-ion's real advantage. Otherwise a pack like lead-acid:
        // carried, holding its own charge, on the channel class until M10i, never a block.
        var content = new GameContent();
        LoadResult result = content.Load(new DirectoryContentSource(ModsDirectory()));
        Assert.True(result.Success, string.Join("; ", result.Errors));

        MaterialDefinition lithium = content.Materials.Get(Identifier.Parse("overburden:lithium_ion_pack"));
        MachineDefinition lead = content.Materials.Get(Identifier.Parse("overburden:lead_acid_pack")).Machine!;
        MachineDefinition pack = lithium.Machine!;

        Assert.Equal(MachineKind.Storage, pack.Kind);
        Assert.True(pack.Carried);
        Assert.False(pack.IsWindow);
        Assert.False(lithium.IsPlaceable);
        Assert.False(lithium.IsProgrammer);
        Assert.Equal(MaterialUnit.Count, lithium.Unit);
        Assert.Equal(14, lithium.TextureLayer);
        Assert.True(pack.Link("charge")!.IsChannel);
        Assert.Equal(new PortDefinition("channel", 0, 0, 15), pack.Port("channel"));
        Assert.Equal(14_400_000L * 1000L, pack.CapacityMillijoules);
        Assert.Equal(2_000L, pack.RechargeWatts);

        // Four lead-acids in the tank, and half their time to fill: two hours against four.
        static long SecondsToFill(MachineDefinition p) => p.CapacityMillijoules / 1000L / p.RechargeWatts!.Value;
        Assert.Equal(4 * lead.CapacityMillijoules, pack.CapacityMillijoules);
        Assert.Equal(2 * 3600L, SecondsToFill(pack));
        Assert.Equal(2 * SecondsToFill(pack), SecondsToFill(lead));

        // What the play feels, at the shipped 50 J a voxel: 4,500 cube bites (7.5 min of Q held at
        // ten a second), 562 tunneller bites and 8 excavator chunks a charge; and once it is empty,
        // a charged channel keeps up a cube bite every 1.6 s, against lead-acid's 12.8.
        ToolDefinition miner = content.Tools.Get(Identifier.Parse(QuantumMinerId));
        ToolDefinition tunneller = content.Tools.Get(Identifier.Parse("overburden:quantum_tunneller"));
        ToolDefinition excavator = content.Tools.Get(Identifier.Parse(QuantumExcavatorId));
        const long Cube = Overburden.Core.Mining.Carve.CubeVoxels;
        const long Tunnel = Overburden.Core.Mining.Carve.TunnelVoxels;
        long cubeMillijoules = miner.Power!.MillijoulesPerVoxel * Cube * Cube * Cube;
        long tunnelMillijoules = tunneller.Power!.MillijoulesPerVoxel * Tunnel * Tunnel * Tunnel;
        long chunkMillijoules = excavator.Power!.MillijoulesPerVoxel * VoxelConstants.ChunkVolume;
        Assert.Equal(4_500L, pack.CapacityMillijoules / cubeMillijoules);
        Assert.Equal(562L, pack.CapacityMillijoules / tunnelMillijoules);
        Assert.Equal(8L, pack.CapacityMillijoules / chunkMillijoules);
        Assert.Equal(1_600L, cubeMillijoules / pack.RechargeWatts!.Value);   // millijoules ÷ watts = milliseconds
        Assert.Equal(12_800L, cubeMillijoules / lead.RechargeWatts!.Value);
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

    [Fact]
    public void ClayIsTheSealedRockAPlayerCanLineARoomWith()
    {
        // Every machine body ships at permeability 0 too, so the sealed end of the test above
        // was satisfied before clay existed — by bodies nobody lines a wall with. Clay (Matthew,
        // 2026-09-20: "should we have a material type that rejects water?") is the first sealed
        // material that is a plain block: placeable, no machine, no item, so a room lined with
        // it has no seep face and the sweat stops at the lining. The support curve is dirt's
        // shape steepened one metre — MATERIALS-NOTES' "swells when wet and loads the shoring"
        // as a number, an estimate until a play says otherwise.
        var content = new GameContent();
        content.Load(new DirectoryContentSource(ModsDirectory()));

        var clay = content.Materials.Get(Identifier.Parse("overburden:clay"));
        var dirt = content.Materials.Get(Identifier.Parse("overburden:dirt"));

        Assert.Equal(0f, clay.Permeability);
        Assert.True(clay.IsPlaceable, "clay is a block, not an item");
        Assert.False(clay.IsMachine, "clay is a block, not a body");
        Assert.False(clay.IsAir);
        Assert.False(clay.IsFluid);
        Assert.Equal(MaterialUnit.Litres, clay.Unit);
        Assert.Equal(dirt.Hardness, clay.Hardness);
        Assert.Equal(dirt.SupportDistance, clay.SupportDistance);
    }

    [Fact]
    public void GlassIsTheOneSolidBlockYouCanSeeThrough()
    {
        // docs/GLASS-PLAN.md, 2026-09-25: see-through (not opaque, so the mesher draws what is
        // behind it and light crosses it) and solid (so it holds water, carries a roof and closes
        // the sky column to rain). Fragile — under dirt's hardness — and spanning less than dirt:
        // estimates, his to move. Sealed, like clay, so a glass wall has no seep face.
        var content = new GameContent();
        content.Load(new DirectoryContentSource(ModsDirectory()));

        var glass = content.Materials.Get(Identifier.Parse("overburden:glass"));
        var dirt = content.Materials.Get(Identifier.Parse("overburden:dirt"));
        MaterialTable table = MaterialTable.From(content.Materials);
        int id = content.Materials.GetNumericId(Identifier.Parse("overburden:glass"));

        Assert.False(glass.IsOpaque);
        Assert.True(table.IsSolid(id), "glass holds water and closes the sky column");
        Assert.False(glass.IsFluid);
        Assert.True(glass.IsPlaceable);
        Assert.False(glass.IsMachine);
        Assert.Equal(0f, glass.Permeability);
        Assert.True(glass.Hardness < dirt.Hardness, $"glass {glass.Hardness}, dirt {dirt.Hardness}");
        Assert.True(glass.SupportDistance < dirt.SupportDistance, $"glass {glass.SupportDistance}, dirt {dirt.SupportDistance}");

        // The only shipped solid that is not opaque — the fact the sky column's switch to
        // IsSolid rests on: every stored column reads the same as before under every other
        // material.
        foreach ((_, _, MaterialDefinition material) in content.Materials.Entries())
        {
            if (material.IsAir || material.IsFluid || material.Id == glass.Id) continue;
            Assert.True(material.IsOpaque, $"{material.Id} is solid and not opaque; the sky column's rule changes what it reads");
        }
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
