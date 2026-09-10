using Overburden.Core.Fluid;
using Overburden.Core.Hydrology;
using Overburden.Core.Lighting;
using Overburden.Core.Voxels;
using Xunit.Abstractions;

namespace Overburden.Core.Tests;

/// <summary>
/// M8c-2's Core twin: Tier A over an analytic bowl, Tier B over a dug terrain with rock of two
/// permeabilities, the seep index between them, and the two-tier identity — now with
/// <c>seeped</c> as its own term beside <c>drawn</c> — held exactly across every tick.
/// </summary>
/// <remarks>
/// <para>
/// The headline is <c>ARoomUnderALakeFillsBySeepageAndTheLakeDropsByExactlyWhatArrived</c>. The
/// identity is Tier A's <c>held == injected − spilled − drawn − seeped</c> and Tier B's
/// <c>held == added − removed − displaced</c>, joined by <c>seeped × 1,000 == the part of added
/// that came through faces</c>. A room dug wholly under the bowl's bed has no portal, so every
/// litre in it came through rock.
/// </para>
/// <para>
/// The bowl is <c>PortalDrainTests</c>' — floor voxel 30 (plane 31) over voxels 48..79, rim 40 —
/// so the two seams are tested over the same lake.
/// </para>
/// </remarks>
public class SeepDrainTests(ITestOutputHelper output)
{
    private const ulong Seed = 0x5EEDUL;
    private const int Cell = HeightField.DefaultCellVoxels;
    private const int Pitch = 128 / Cell;
    private const int Floor = 30;
    private const int Rim = 40;
    private const float Stone = 0.02f;
    private const float Dirt = 0.35f;

    private static ISurfaceHeights Bowl() => new AnalyticSurface((x, z) =>
        x >= 48 && x < 80 && z >= 48 && z < 80 ? Floor : Rim);

    /// <summary>Dug voxels and open air above the seed surface inside a loaded box; rock is stone, or dirt within two of the surface.</summary>
    private sealed class DugTerrain(ISurfaceHeights seed) : ISeepTerrain
    {
        public HashSet<VoxelPos> Dug { get; } = [];

        public HashSet<ChunkPos> Loaded { get; } = [];

        public Dictionary<VoxelPos, float> Placed { get; } = [];

        public bool CanHold(VoxelPos v) =>
            Loaded.Contains(v.Chunk) && !Placed.ContainsKey(v)
            && (Dug.Contains(v) || v.Y > seed.SurfaceHeightAt(v.X, v.Z));

        public bool TryGetRock(VoxelPos v, out float permeability)
        {
            if (!Loaded.Contains(v.Chunk) || CanHold(v))
            {
                permeability = 0f;
                return false;
            }

            permeability = Placed.TryGetValue(v, out float placed) ? placed
                : v.Y >= seed.SurfaceHeightAt(v.X, v.Z) - 1 ? Dirt : Stone;
            return true;
        }
    }

    private sealed class Twin
    {
        public Twin(double scale = SeepIndex.DefaultSeepageScale)
        {
            Seed = Bowl();
            Terrain = new DugTerrain(Seed);
            Water = new WaterSystem(Seed, SeepDrainTests.Seed, new BasinLedger(), Pitch);
            Water.Install(Water.AnalyseFor(64, 64));
            Fluid = new FluidSimulation(Terrain);
            Portals = new PortalIndex(Seed, Terrain);
            Seeps = new SeepIndex(Seed, Terrain);
            Tracker = new PortalTracker(Portals, Seeps);
            PortalDrain = new PortalDrain(Portals, Water, Fluid);
            Drain = new SeepDrain(Seeps, Water, Fluid, scale);

            // Chunk Y 0 and 1 (voxels 0..63) over the bowl and its walls.
            for (int cz = 1; cz <= 2; cz++)
            {
                for (int cx = 1; cx <= 2; cx++)
                {
                    for (int cy = 0; cy <= 1; cy++)
                    {
                        var chunk = new ChunkPos(cx, cy, cz);
                        Terrain.Loaded.Add(chunk);
                        Tracker.OnVoxelsReady(chunk, hasEdits: false);
                    }
                }
            }
        }

        public ISurfaceHeights Seed { get; }

        public DugTerrain Terrain { get; }

        public WaterSystem Water { get; }

        public FluidSimulation Fluid { get; }

        public PortalIndex Portals { get; }

        public SeepIndex Seeps { get; }

        public PortalTracker Tracker { get; }

        public PortalDrain PortalDrain { get; }

        public SeepDrain Drain { get; }

        public int Basin => Water.BasinAtSample(48 / Cell, 48 / Cell);

        public void Dig(VoxelPos v)
        {
            Terrain.Dug.Add(v);
            Tracker.OnVoxelChanged(v);
        }

        public void DigBox(int x0, int y0, int z0, int x1, int y1, int z1)
        {
            for (int y = y0; y <= y1; y++)
            {
                for (int z = z0; z <= z1; z++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        Dig(new VoxelPos(x, y, z));
                    }
                }
            }
        }

        /// <summary>One game tick: the flush, both draws at the boundary, then the automaton.</summary>
        public long Tick()
        {
            Tracker.Flush();
            PortalDrain.Draw();
            long seeped = Drain.Draw();
            Fluid.Step();
            return seeped;
        }

        /// <summary>Both tiers' identities and the two seams between them, exactly.</summary>
        public void AssertIdentity()
        {
            long a = Water.StoredLitres;
            Assert.Equal(Water.InjectedLitres - Water.SpilledLitres - Water.DrawnLitres - Water.SeepedLitres, a);

            long b = Fluid.TotalMillilitres;
            Assert.Equal(Fluid.AddedMillilitres - Fluid.RemovedMillilitres - Fluid.DisplacedMillilitres, b);

            Assert.Equal(Water.DrawnLitres * FluidConstants.MillilitresPerLitre, PortalDrain.DrawnMillilitres);
            Assert.Equal(Water.SeepedLitres * FluidConstants.MillilitresPerLitre, Drain.SeepedMillilitres);
            Assert.Equal(PortalDrain.DrawnMillilitres + Drain.SeepedMillilitres, Fluid.AddedMillilitres);
        }
    }

    /// <summary>
    /// An 8 × 4 × 8 voxel room wholly under the bed: voxels 56..63, Y 20..23, 56..63. No portal.
    /// 256 edits is past the tracker's burst threshold, so the seep index is stale until the
    /// flush — which the first fixture forgot, and read a coefficient over the first 64 edits.
    /// </summary>
    private static void DigRoom(Twin twin)
    {
        twin.DigBox(56, 20, 56, 63, 23, 63);
        Assert.Equal(1, twin.Tracker.PendingRescans);
        twin.Tracker.Flush();
    }

    [Fact]
    public void ARoomUnderALakeFillsBySeepageAndTheLakeDropsByExactlyWhatArrived()
    {
        // At the shipped scale the room owes 0.04 L a tick; swept up so litres cross every tick
        // and the rate can be read against the coefficient inside a few hundred ticks.
        var twin = new Twin(scale: 50);
        DigRoom(twin);
        Assert.Equal(0, twin.Portals.Count);
        Assert.True(twin.Seeps.Count > 0);

        twin.Water.Inject(twin.Basin, 100_000);
        double level = twin.Water.LevelAt(twin.Basin)!.Value;
        Assert.True(level > Floor + 1);

        // What the first pass owes: the chunk's coefficient at this level, at the shipped scale,
        // for half a second. The room is one chunk's worth of faces.
        ChunkPos room = new VoxelPos(56, 20, 56).Chunk;
        double coefficient = twin.Seeps.Coefficient(room, twin.Water);
        double litresPerTick = SeepIndex.LitresPerSecond(coefficient, twin.Drain.SeepageScale) / 2;
        Assert.True(litresPerTick > 1, $"only {litresPerTick:F3} L a tick; the fixture is too dry to test");

        // A hundred ticks: the rate is the coefficient's, to within the remainder carried and
        // the level falling under it.
        long ticks = 0;
        long firstSeep = 0;
        while (ticks < 100)
        {
            long seeped = twin.Tick();
            ticks++;
            twin.AssertIdentity();
            if (firstSeep == 0 && seeped > 0)
            {
                firstSeep = ticks;
            }
        }

        Assert.Equal(1, firstSeep);
        Assert.InRange(twin.Water.SeepedLitres, (long)(litresPerTick * ticks * 0.9), (long)(litresPerTick * ticks * 1.05));
        long atHundred = twin.Water.SeepedLitres;

        // Then the room fills — 256 cells, 4,000 L — and the chunk goes full: seepage stops
        // short of the room's capacity by the sub-litre room the whole-litre deliveries leave,
        // and the chunk is skipped from then on.
        while (ticks < 400)
        {
            twin.Tick();
            ticks++;
            twin.AssertIdentity();
        }

        Assert.Equal(0, twin.Water.DrawnLitres);
        Assert.Equal(100_000 - twin.Water.SeepedLitres, twin.Water.StoredLitres);
        Assert.Equal(1, twin.Drain.FullChunks);
        Assert.InRange(twin.Water.SeepedLitres, 256 * 15, 256 * 16);
        Assert.True(twin.Water.SeepedLitres > atHundred);

        // Every litre is in the room, and only in the room.
        long inRoom = 0;
        foreach (VoxelPos cell in twin.Fluid.Grid.HeldCells())
        {
            Assert.InRange(cell.X, 56, 63);
            Assert.InRange(cell.Y, 20, 23);
            Assert.InRange(cell.Z, 56, 63);
            inRoom += twin.Fluid.Grid.AmountAt(cell);
        }

        Assert.Equal(twin.Fluid.TotalMillilitres, inRoom);

        output.WriteLine(
            $"{ticks} ticks: {twin.Water.SeepedLitres:N0} L seeped through {twin.Seeps.Count} faces "
            + $"({atHundred:N0} L by tick 100 against {litresPerTick * 100:F0} L owed), level {level:F2} → "
            + $"{twin.Water.LevelAt(twin.Basin):F2}, {twin.Fluid.ActiveCellCount} awake at the end, "
            + $"full after {twin.Drain.Passes - SeepDrain.FullRecheckPasses:N0} passes at most");
    }

    [Fact]
    public void TheFirstLitreArrivesAtTheLowestFaceAndItIsAFloor()
    {
        // Decision 5: a floor spring before a wall weep. The room's lowest faces are its floor
        // at Y 20; the first delivery lands in the lowest, first-in-order floor cell and nowhere
        // else, before the automaton has moved anything.
        var twin = new Twin(scale: 50);
        DigRoom(twin);
        twin.Water.Inject(twin.Basin, 100_000);

        twin.Tracker.Flush();
        long seeped = twin.Drain.Draw();
        Assert.True(seeped > 0);

        List<VoxelPos> wet = twin.Fluid.Grid.HeldCells().ToList();
        Assert.All(wet, cell => Assert.Equal(20, cell.Y));
        SeepFace first = twin.Seeps.FacesIn(new VoxelPos(56, 20, 56).Chunk)[0];
        Assert.True(first.IsFloor);
        Assert.Contains(first.Inside, wet);
        Assert.Equal(first.Inside, wet.OrderBy(c => c.X).ThenBy(c => c.Z).First());
    }

    [Fact]
    public void AFaceAboveTheTableIsDryAndNoLakeMeansNoSeepage()
    {
        // A tunnel into the rim at Y 31 (surface 40) in the room's own chunk is under the table
        // only while the lake stands above 31.5; the room under the bed always is. The chunk's
        // pooled rate must land in the room and never in the dry tunnel, however low it sorts.
        // Take the lake away and both stop.
        var twin = new Twin(scale: 50);
        DigRoom(twin);

        // Wholly inside the rim's rock: the first fixture ran the tunnel to the bank at x 47,
        // where its end opened onto the lake's air at x 48 — a portal, which the portal drain
        // filled from the lake and read as the tunnel "seeping".
        twin.DigBox(40, 31, 60, 43, 31, 60);
        ChunkPos room = new VoxelPos(56, 20, 56).Chunk;
        Assert.Equal(room, new VoxelPos(40, 31, 60).Chunk);
        Assert.Contains(twin.Seeps.FacesIn(room), f => f.Y == 31);
        Assert.Equal(0, twin.Portals.Count);

        // 4,000 L is a quarter voxel over the plane: level 31.25, under the tunnel's centre.
        twin.Water.Inject(twin.Basin, 4_000);
        double low = twin.Water.LevelAt(twin.Basin)!.Value;
        Assert.True(low < 31.5 && low > 31, $"level {low}");
        double coefficient = twin.Seeps.Coefficient(room, twin.Water);
        Assert.True(coefficient > 0);

        for (int i = 0; i < 20; i++) twin.Tick();
        Assert.Equal(1, twin.Drain.ChunksSeepingLastPass);
        Assert.True(twin.Water.SeepedLitres > 0);
        Assert.Empty(twin.Fluid.Grid.HeldCells().Where(c => c.Y == 31));
        twin.AssertIdentity();

        // With the room full and the lake still under the tunnel, the pooled litres have
        // nowhere below the table to go — and must not go to the dry tunnel above it. The chunk
        // goes full instead. (Filled by hand, so the seam identity is not asserted past here.)
        for (int y = 20; y <= 23; y++)
        {
            for (int z = 56; z <= 63; z++)
            {
                for (int x = 56; x <= 63; x++)
                {
                    twin.Fluid.Add(new VoxelPos(x, y, z), FluidConstants.CellCapacityMillilitres);
                }
            }
        }

        for (int i = 0; i < 5; i++) twin.Tick();
        Assert.Empty(twin.Fluid.Grid.HeldCells().Where(c => c.Y == 31));
        Assert.Equal(1, twin.Drain.FullChunks);

        // Raise the lake over the tunnel: its faces join the chunk's rate — and the water still
        // arrives at the room's floor, because the chunk's rate is pooled and lands lowest first
        // (decision 5). The tunnel gets wet only once everything below it is full.
        twin.Water.Inject(twin.Basin, 96_000);
        Assert.True(twin.Water.LevelAt(twin.Basin)!.Value > 36);
        Assert.True(twin.Seeps.Coefficient(room, twin.Water) > coefficient);
        long seepedAtRaise = twin.Water.SeepedLitres;
        for (int i = 0; i < SeepDrain.FullRecheckPasses + 40; i++) twin.Tick();
        Assert.True(twin.Water.SeepedLitres > seepedAtRaise);

        // The room is full, so now the tunnel — under the table at last — is the lowest face
        // with room, and it takes the water.
        Assert.NotEmpty(twin.Fluid.Grid.HeldCells().Where(c => c.Y == 31));

        // No lake: the whole basin taken off the top by hand (Tier A's own removal, so the
        // portal seam's identity is not asserted past here), and nothing seeps.
        long left = twin.Water.StoredLitres;
        Assert.Equal(left, twin.Water.Water!.Remove(twin.Basin, left));
        Assert.Null(twin.Water.LevelAt(twin.Basin));
        long seepedBefore = twin.Water.SeepedLitres;
        for (int i = 0; i < 10; i++) twin.Tick();
        Assert.Equal(seepedBefore, twin.Water.SeepedLitres);
        Assert.Equal(0, twin.Drain.ChunksSeepingLastPass);
        Assert.Equal(twin.Water.SeepedLitres * FluidConstants.MillilitresPerLitre, twin.Drain.SeepedMillilitres);
    }

    [Fact]
    public void AFaceUnderADryBasinReceivesNothingHoweverLowItLies()
    {
        // The pooled delivery goes lowest first, but only into faces that stand under a table:
        // a hollow in the same chunk whose column drains to a basin holding nothing is dry rock
        // however far below the room it lies. Found rather than placed, because which rim sample
        // drains where is the watershed's business.
        var twin = new Twin(scale: 50);
        DigRoom(twin);
        ChunkPos room = new VoxelPos(56, 20, 56).Chunk;
        int bowl = twin.Basin;

        VoxelPos? dry = null;
        for (int z = 32; z < 64 && dry is null; z += Cell)
        {
            for (int x = 32; x < 64; x += Cell)
            {
                (int sx, int sz) = twin.Water.SampleOf(x, z);
                if (twin.Water.BasinAtSample(sx, sz) != bowl)
                {
                    dry = new VoxelPos(x + 1, 18, z + 1);
                    break;
                }
            }
        }

        Assert.True(dry is not null, "every sample of the room's chunk drains to the bowl; the fixture cannot separate the arms");
        twin.Dig(dry!.Value);
        Assert.Equal(room, dry.Value.Chunk);
        Assert.Contains(twin.Seeps.FacesIn(room), f => f.Inside == dry.Value);
        Assert.Equal(dry.Value, twin.Seeps.FacesIn(room)[0].Inside);

        twin.Water.Inject(twin.Basin, 100_000);
        Assert.Null(twin.Water.TableAt(twin.Water.SampleOf(dry.Value.X, dry.Value.Z).X, twin.Water.SampleOf(dry.Value.X, dry.Value.Z).Z));
        for (int i = 0; i < 30; i++) twin.Tick();
        Assert.True(twin.Water.SeepedLitres > 0);
        Assert.Equal(0, twin.Fluid.Grid.AmountAt(dry.Value));
        Assert.All(twin.Fluid.Grid.HeldCells(), c => Assert.InRange(c.Y, 20, 23));
        twin.AssertIdentity();
    }

    [Fact]
    public void AFullChunkIsSkippedUntilItsFacesChangeOrTheRecheckComesRound()
    {
        // One dug voxel under the bed holds 15.625 L. A big lake over it fills it inside a few
        // passes; after that the chunk is marked full and skipped — not walked — until a dig
        // in it moves the index's revision, or FullRecheckPasses passes go by, which is how
        // water taken out by a route that files no face (the relay's Alt+R) is noticed.
        var twin = new Twin(scale: 500);
        var cell = new VoxelPos(60, 24, 60);
        twin.Dig(cell);
        twin.Water.Inject(twin.Basin, 200_000);

        int pass = 0;
        while (twin.Drain.FullChunks == 0 && pass < 50)
        {
            twin.Tick();
            pass++;
        }

        Assert.Equal(1, twin.Drain.FullChunks);
        Assert.True(twin.Fluid.Grid.FreeAt(cell) < FluidConstants.MillilitresPerLitre, "full to the litre");
        long seepedWhenFull = twin.Water.SeepedLitres;
        long visitsWhenFull = twin.Drain.Passes;

        // Skipped: seeping nothing, not even counted as seeping.
        for (int i = 0; i < 5; i++)
        {
            twin.Tick();
            Assert.Equal(1, twin.Drain.ChunksFullLastPass);
            Assert.Equal(0, twin.Drain.ChunksSeepingLastPass);
        }

        Assert.Equal(seepedWhenFull, twin.Water.SeepedLitres);
        Assert.InRange(twin.Seeps.RemainderMillilitres(cell.Chunk), 0, FluidConstants.CellCapacityMillilitres);

        // A dig beside it changes the faces: looked at again on the next pass, and the new
        // voxel takes water.
        twin.Dig(cell with { X = 61 });
        twin.Tick();
        Assert.Equal(0, twin.Drain.ChunksFullLastPass);
        Assert.True(twin.Water.SeepedLitres > seepedWhenFull);
        twin.AssertIdentity();

        // Full again; then water taken out without an edit is not seen until the recheck.
        while (twin.Drain.FullChunks == 0) twin.Tick();
        long full = twin.Water.SeepedLitres;
        long markedAt = twin.Drain.Passes;
        Assert.True(twin.Fluid.Remove(cell, FluidConstants.CellCapacityMillilitres) > 0);
        while (twin.Drain.Passes - markedAt < SeepDrain.FullRecheckPasses - 1)
        {
            twin.Tick();
            Assert.Equal(full, twin.Water.SeepedLitres);
        }

        twin.Tick();
        Assert.True(twin.Water.SeepedLitres > full, "the recheck never came round");
        twin.AssertIdentity();
        output.WriteLine($"full after {pass} pass(es) at {visitsWhenFull}; rechecked after {SeepDrain.FullRecheckPasses}");
    }

    [Fact]
    public void ALakeThatRunsDryMidPassIsNotAFullChunk()
    {
        // A small lake over a big room: the pass asks for more than the lake holds, gets less,
        // and must not mark the room full — its faces have room, the lake has none, and the
        // next rain would otherwise wait 16 passes to be noticed.
        var twin = new Twin(scale: 10_000);
        DigRoom(twin);
        twin.Water.Inject(twin.Basin, 300);
        twin.Tick();
        Assert.Equal(0, twin.Water.StoredLitres);
        Assert.Equal(300, twin.Water.SeepedLitres);
        Assert.Equal(0, twin.Drain.FullChunks);
        twin.AssertIdentity();

        twin.Water.Inject(twin.Basin, 300);
        twin.Tick();
        Assert.Equal(600, twin.Water.SeepedLitres);
        twin.AssertIdentity();
    }

    [Fact]
    public void SeepedIsItsOwnTermBesideDrawnAndBothIdentitiesHold()
    {
        // A shaft from the bed down into the room: the mouth is a portal, the room's walls are
        // seep faces, and the two seams draw from the same lake into the same chunk, counted
        // apart and summed in the identity.
        var twin = new Twin();
        DigRoom(twin);
        twin.DigBox(60, 24, 60, 60, Floor, 60);
        Assert.Equal(1, twin.Portals.Count);
        twin.Water.Inject(twin.Basin, 100_000);

        for (int i = 0; i < 60; i++)
        {
            twin.Tick();
            twin.AssertIdentity();
        }

        Assert.True(twin.Water.DrawnLitres > 0);
        Assert.True(twin.Water.SeepedLitres > 0);
        output.WriteLine($"{twin.Water.DrawnLitres:N0} L drawn through the shaft, {twin.Water.SeepedLitres:N0} L seeped through the walls");
    }

    [Fact]
    public void ARateUnderALitreATickIsCarriedAsARemainderAndDeliveredWhole()
    {
        // A single voxel under the bed owes a few millilitres a tick (0.6 mL at the shipped
        // scale, swept to ~20). Nothing crosses until a whole litre is owed, then exactly one does.
        var twin = new Twin(scale: 2.0);
        var cell = new VoxelPos(60, 26, 60);
        twin.Dig(cell);
        twin.Water.Inject(twin.Basin, 100_000);

        double owedPerTick = SeepIndex.LitresPerSecond(
            twin.Seeps.Coefficient(cell.Chunk, twin.Water), twin.Drain.SeepageScale) / 2;
        Assert.True(owedPerTick < 1 && owedPerTick > 0.001, $"{owedPerTick} L a tick");

        int ticks = 0;
        long lastSeeped = 0;
        var deliveries = new List<(int Tick, long Litres)>();
        while (ticks < 200)
        {
            twin.Tick();
            ticks++;
            long remainder = twin.Seeps.RemainderMillilitres(cell.Chunk);
            Assert.InRange(remainder, 0, FluidConstants.MillilitresPerLitre - 1);
            if (twin.Water.SeepedLitres != lastSeeped)
            {
                deliveries.Add((ticks, twin.Water.SeepedLitres - lastSeeped));
                lastSeeped = twin.Water.SeepedLitres;
            }
        }

        Assert.NotEmpty(deliveries);
        Assert.All(deliveries, d => Assert.Equal(1, d.Litres));
        int expectedEvery = (int)Math.Floor(1 / owedPerTick);
        Assert.InRange(deliveries[0].Tick, expectedEvery, expectedEvery + 1);
        twin.AssertIdentity();
        output.WriteLine($"{owedPerTick * 1000:F1} mL a tick: {deliveries.Count} litre(s) over {ticks} ticks, first at tick {deliveries[0].Tick}");
    }

    [Fact]
    public void SaturationIsTheHeadOverTheVoxelByItsColumnsTableOrZero()
    {
        // M8c-3: the query M9 reads. Under the bowl's lake a voxel is under head; a voxel at the
        // level, above it, or under a sample that drains to a dry basin is dry — and the number
        // is the one the seep coefficient multiplies, read the same way.
        var twin = new Twin();
        twin.Water.Inject(twin.Basin, 100_000);
        double level = twin.Water.LevelAt(twin.Basin)!.Value;

        var deep = new VoxelPos(60, 20, 60);
        Assert.Equal((level - 20.5) * VoxelConstants.VoxelSize, Saturation.HeadMetresAt(deep, twin.Water), 9);
        Assert.True(Saturation.IsSaturated(deep, twin.Water));
        Assert.Equal(0, Saturation.HeadMetresAt(new VoxelPos(60, (int)Math.Floor(level), 60), twin.Water));
        Assert.Equal(0, Saturation.HeadMetresAt(new VoxelPos(60, 45, 60), twin.Water));
        Assert.False(Saturation.IsSaturated(new VoxelPos(60, 45, 60), twin.Water));

        // The room's chunk under the table: the coefficient is permeability × this head × area,
        // summed — the same quantity, so one voxel's face agrees to the metre.
        var cell = new VoxelPos(60, 24, 60);
        twin.Dig(cell);
        double coefficient = twin.Seeps.Coefficient(cell.Chunk, twin.Water);
        double head = Saturation.HeadMetresAt(cell, twin.Water);
        double perms = twin.Seeps.FacesIn(cell.Chunk).Sum(f => (double)f.Permeability);
        Assert.Equal(perms * head * SeepIndex.FaceAreaSquareMetres, coefficient, 9);

        // No lake, no head — and a sample off every basin reads dry too.
        long left = twin.Water.StoredLitres;
        twin.Water.Water!.Remove(twin.Basin, left);
        Assert.Equal(0, Saturation.HeadMetresAt(deep, twin.Water));
        Assert.Equal(0, Saturation.HeadMetresAt(new VoxelPos(-500, -100, -500), twin.Water));
    }

    [Fact]
    public void ScaleZeroSeepsNothingAndWalksNothing()
    {
        var twin = new Twin(scale: 0);
        DigRoom(twin);
        twin.Water.Inject(twin.Basin, 100_000);
        for (int i = 0; i < 10; i++) twin.Tick();
        Assert.Equal(0, twin.Water.SeepedLitres);
        Assert.Equal(0, twin.Drain.ChunksLastPass);
        Assert.Equal(100_000, twin.Water.StoredLitres);
    }
}
