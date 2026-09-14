using Overburden.Core.Fluid;
using Overburden.Core.Hydrology;
using Overburden.Core.Lighting;
using Overburden.Core.Voxels;
using Overburden.Core.Worldgen;
using Xunit.Abstractions;

namespace Overburden.Core.Tests;

/// <summary>
/// M8b-3a's Core twin: Tier B water resting on the analytic ground becomes Tier A litres, the two
/// tiers' identities and the seam between them exact after every boundary.
/// </summary>
/// <remarks>
/// <para>
/// The headline is <c>ASpillOnTheSlopeEndsEntirelyInTheBasinWithTierBHoldingNothing</c>: the
/// relay's ball on a ramp above the bowl, and every millilitre of it is in the basin or in the
/// per-pit remainder when the automaton stops. The whole-world identity is
/// <c>A_held + A_spilled + pending + B_held == A_injected + B_added − B_removed − B_displaced</c>
/// in millilitres, and the seam's own is <c>received × 1,000 + pending == handed</c>.
/// </para>
/// <para>
/// Heights are the topmost solid voxel; the plane the water stands on is one above. A bowl floor
/// written as 30 is a lake bed at plane 31, and a cell at Y 31 is <i>on the ground</i>.
/// </para>
/// </remarks>
public class SurfaceHandoffTests(ITestOutputHelper output)
{
    private const ulong Seed = 0x5EA7UL;

    /// <summary>The lattice, and a window 128 voxels on a side whatever it is — the fixture is in world voxels.</summary>
    private const int Cell = HeightField.DefaultCellVoxels;
    private const int Pitch = 128 / Cell;
    private const int Cap = FluidConstants.CellCapacityMillilitres;

    /// <summary>The bowl's floor voxel and its rim; planes 31 and 41.</summary>
    private const int Floor = 30;
    private const int Rim = 40;

    /// <summary>
    /// A square 8 m bowl over voxels 48..79, a ramp rising east of it from the floor
    /// to the rim over voxels 80..99, and the rim plateau everywhere else.
    /// </summary>
    private static ISurfaceHeights BowlAndRamp() => new AnalyticSurface((x, z) =>
    {
        if (x >= 48 && x < 80 && z >= 48 && z < 80)
        {
            return Floor;
        }

        if (x >= 80 && x < 100 && z >= 48 && z < 80)
        {
            return Math.Min(Rim, Floor + ((x - 80) / 2) + 1);
        }

        return Rim;
    });

    /// <summary>Holdability: open above the seed surface, less what was placed, plus what was dug, inside the loaded box.</summary>
    private sealed class EditedTerrain(ISurfaceHeights seed) : IFluidTerrain
    {
        public HashSet<VoxelPos> Dug { get; } = [];

        public HashSet<VoxelPos> Placed { get; } = [];

        public HashSet<ChunkPos> Loaded { get; } = [];

        public bool CanHold(VoxelPos v) =>
            Loaded.Contains(v.Chunk)
            && !Placed.Contains(v)
            && (Dug.Contains(v) || v.Y > seed.SurfaceHeightAt(v.X, v.Z));
    }

    private sealed class Twin
    {
        public Twin(bool installWindow = true)
        {
            Seed = BowlAndRamp();
            Terrain = new EditedTerrain(Seed);
            Water = new WaterSystem(Seed, SurfaceHandoffTests.Seed, new BasinLedger(), Pitch);
            if (installWindow)
            {
                Water.Install(Water.AnalyseFor(64, 64));
            }

            Fluid = new FluidSimulation(Terrain);
            Handoff = new SurfaceHandoff(Seed, Water, Fluid);

            // Chunks X −1..3, Y 0..1, Z 1..2: voxels −32..127 by 0..63 by 32..95, which covers
            // the bowl, the ramp, a stretch of rim on every side and a column outside the window.
            for (int cz = 1; cz <= 2; cz++)
            {
                for (int cx = -1; cx <= 3; cx++)
                {
                    for (int cy = 0; cy <= 1; cy++)
                    {
                        Terrain.Loaded.Add(new ChunkPos(cx, cy, cz));
                    }
                }
            }
        }

        public ISurfaceHeights Seed { get; }

        public EditedTerrain Terrain { get; }

        public WaterSystem Water { get; }

        public FluidSimulation Fluid { get; }

        public SurfaceHandoff Handoff { get; }

        public int Basin => Water.BasinAtSample(48 / Cell, 48 / Cell);

        /// <summary>
        /// One tick and the boundary after it: the automaton, then the hand-off over what it found
        /// at rest. Returns what was handed. (Main runs the boundary before the tick; the pairing
        /// is the same, and this way "add, tick, handed" reads in order.)
        /// </summary>
        public long Tick()
        {
            Fluid.Step();
            return Handoff.Absorb();
        }

        /// <summary>Ticks until the automaton has nothing to do and the boundary after it hands nothing.</summary>
        public int RunToRest(int maxTicks = 5_000)
        {
            int ticks = 0;
            while (ticks < maxTicks)
            {
                long handed = Handoff.Absorb();
                if (Fluid.ActiveCellCount == 0 && handed == 0)
                {
                    break;
                }

                Fluid.Step();
                ticks++;
            }

            Assert.True(ticks < maxTicks, $"still moving after {ticks} ticks");
            Assert.True(Fluid.VerifyQuiescent(), "the automaton stopped with moves available");
            return ticks;
        }

        /// <summary>Both tiers' identities, the seam's, and the whole world's, exactly.</summary>
        public void AssertIdentity()
        {
            long a = Water.StoredLitres;
            Assert.Equal(
                Water.ReceivedLitres + Water.InjectedLitres - Water.SpilledLitres - Water.DrawnLitres, a);

            long b = Fluid.TotalMillilitres;
            Assert.Equal(
                Fluid.AddedMillilitres - Fluid.RemovedMillilitres - Fluid.DisplacedMillilitres
                    - Fluid.HandedMillilitres,
                b);

            Assert.Equal(Fluid.HandedMillilitres, Handoff.HandedMillilitres);
            Assert.Equal(Water.ReceivedLitres, Handoff.ReceivedLitres);
            Assert.Equal(
                (Handoff.ReceivedLitres * FluidConstants.MillilitresPerLitre) + Handoff.PendingMillilitres,
                Handoff.HandedMillilitres);

            // Drawn leaves the world here: this twin has no portal drain to receive it.
            Assert.Equal(
                ((Water.InjectedLitres - Water.SpilledLitres - Water.DrawnLitres) * FluidConstants.MillilitresPerLitre)
                    + Fluid.AddedMillilitres - Fluid.RemovedMillilitres - Fluid.DisplacedMillilitres,
                (a * FluidConstants.MillilitresPerLitre) + Handoff.PendingMillilitres + b);
        }
    }

    [Fact]
    public void TheFixtureIsOneBasinAndTheRampDrainsIntoIt()
    {
        var twin = new Twin();
        Assert.NotEqual(Watershed.Outlet, twin.Basin);
        Assert.Equal(twin.Basin, twin.Water.BasinAtSample(64 / Cell, 64 / Cell));
        Assert.Equal(twin.Basin, twin.Water.BasinAtSample(80 / Cell, 64 / Cell));
        Assert.Equal(twin.Basin, twin.Water.BasinAtSample(96 / Cell, 64 / Cell));
        Assert.Equal(Watershed.Outlet, twin.Water.BasinAtSample(-1, 48 / Cell));
        Assert.Equal(Floor + 1, twin.Water.Window!.Watershed.Basins[twin.Basin].PitHeight);

        // The rim is a flat plateau that touches the window edge, and the edge is an outlet: the
        // plateau's far corner drains off the window, which is the shape the outlet test needs
        // and the reason the ramp tests keep their water between the bowl's own walls.
        Assert.Equal(Watershed.Outlet, twin.Water.BasinAtSample(Pitch - 1, Pitch - 1));

        // The ramp rises a plane every two voxels from the bowl's floor to the rim.
        Assert.Equal(Floor + 1, twin.Seed.SurfaceHeightAt(80, 60));
        Assert.Equal(Floor + 5, twin.Seed.SurfaceHeightAt(88, 60));
        Assert.Equal(Rim, twin.Seed.SurfaceHeightAt(99, 60));
        Assert.Equal(Rim, twin.Seed.SurfaceHeightAt(100, 60));
    }

    [Fact]
    public void ACellRestingOnTheGroundHandsItsWholeContentsAndTheBasinReceivesTheWholeLitres()
    {
        // One full cell on the bowl's floor spreads into a puddle, every cell of which is on the
        // ground; each hands off the tick it rests, and the basin ends with the fifteen whole
        // litres, the remainder pending, Tier B holding nothing.
        var twin = new Twin();
        Assert.Equal(0, twin.Water.StoredLitres);

        twin.Fluid.Add(new VoxelPos(60, Floor + 1, 60), Cap);
        int ticks = twin.RunToRest();

        Assert.Equal(0, twin.Fluid.TotalMillilitres);
        Assert.Equal(0, twin.Fluid.Grid.HeldCellCount);
        Assert.Equal(15, twin.Water.StoredLitres);
        Assert.Equal(15, twin.Water.ReceivedLitres);
        Assert.Equal(625, twin.Handoff.PendingMillilitres);
        Assert.Equal(625, twin.Handoff.PendingFor(twin.Basin));
        Assert.Equal(1, twin.Handoff.PendingBasins);
        Assert.Equal(Cap, twin.Handoff.HandedMillilitres);
        Assert.True(twin.Water.LevelAt(twin.Basin) > Floor + 1);
        twin.AssertIdentity();
        output.WriteLine($"puddle handed off in {ticks} ticks over {twin.Handoff.Handoffs} cell(s)");
    }

    [Fact]
    public void ASubLitreCellIsCarriedAsARemainderAndDeliveredWhenItMakesALitre()
    {
        // Under a litre cannot cross to Tier A on its own and cannot spread on its own either, so
        // without the remainder it would sit on the ground for ever as a film. It is taken whole,
        // and the litre is delivered when the pit's remainder reaches one.
        var twin = new Twin();
        var cell = new VoxelPos(60, Floor + 1, 60);

        twin.Fluid.Add(cell, 500);
        Assert.Equal(500, twin.Tick());
        Assert.Equal(0, twin.Fluid.TotalMillilitres);
        Assert.Equal(0, twin.Water.ReceivedLitres);
        Assert.Equal(500, twin.Handoff.PendingMillilitres);
        twin.AssertIdentity();

        twin.Fluid.Add(cell, 600);
        Assert.Equal(600, twin.Tick());
        Assert.Equal(1, twin.Water.ReceivedLitres);
        Assert.Equal(1, twin.Water.StoredLitres);
        Assert.Equal(100, twin.Handoff.PendingMillilitres);
        twin.AssertIdentity();
    }

    [Fact]
    public void ASpillOnTheSlopeEndsEntirelyInTheBasinWithTierBHoldingNothing()
    {
        // THE ORACLE. The relay's ball on the ramp: it falls, runs down the steps into the bowl,
        // spreads, and every cell hands off where it comes to rest. Nothing is left in Tier B, the
        // basin holds the whole litres, the remainder is pending, and the rest is real.
        var twin = new Twin();
        const long litres = 10_000;
        long pending = litres * FluidConstants.MillilitresPerLitre;
        var at = new VoxelPos(92, twin.Seed.SurfaceHeightAt(92, 64) + 1 + 4, 64);

        int ticks = 0;
        while (pending > 0 && ticks < 200)
        {
            twin.Handoff.Absorb();
            pending = Pour(twin.Fluid, at, 4, pending);
            twin.Fluid.Step();
            ticks++;
            twin.AssertIdentity();
        }

        Assert.Equal(0, pending);
        ticks += twin.RunToRest();

        Assert.Equal(0, twin.Fluid.TotalMillilitres);
        Assert.Equal(0, twin.Fluid.Grid.PanelCount);
        Assert.Equal(litres * FluidConstants.MillilitresPerLitre, twin.Handoff.HandedMillilitres);
        Assert.Equal(litres, twin.Water.ReceivedLitres + (twin.Handoff.PendingMillilitres / 1000));
        Assert.Equal(0, twin.Water.SpilledLitres);
        Assert.True(twin.Water.StoredLitres >= litres - 1);
        Assert.Equal(1, twin.Handoff.PendingBasins);
        twin.AssertIdentity();
        output.WriteLine(
            $"{litres:N0} L on the ramp rested in {ticks} ticks: {twin.Water.ReceivedLitres:N0} L received over "
            + $"{twin.Handoff.Handoffs:N0} hand-off(s), {twin.Handoff.PendingMillilitres} mL pending, "
            + $"level {twin.Water.LevelAt(twin.Basin):F2}");
    }

    [Fact]
    public void WaterInAPitIsNeverHandedAndWaterOnTheGroundAboveItIs()
    {
        // A shaft dug through the bowl's floor. What falls into it is below the plane and stays
        // Tier B; what stands on the plane above the full shaft rests there and is handed. So the
        // shaft ends full and the rest is the basin's — Tier A never learns the hole exists,
        // from either side.
        var twin = new Twin();
        for (int y = Floor - 10; y <= Floor; y++)
        {
            twin.Terrain.Dug.Add(new VoxelPos(60, y, 60));
        }

        var mouth = new VoxelPos(60, Floor + 1, 60);
        long poured = 0;
        for (int i = 0; i < 25; i++)
        {
            long refused = twin.Fluid.Add(mouth, Cap);
            poured += Cap - refused;
            twin.Tick();
            twin.AssertIdentity();
        }

        twin.RunToRest();

        long shaft = 11L * Cap;
        Assert.Equal(shaft, twin.Fluid.TotalMillilitres);
        for (int y = Floor - 10; y <= Floor; y++)
        {
            Assert.Equal(Cap, twin.Fluid.Grid.AmountAt(new VoxelPos(60, y, 60)));
        }

        Assert.Equal(0, twin.Fluid.Grid.AmountAt(mouth));
        Assert.Equal(poured - shaft, twin.Handoff.HandedMillilitres);
        Assert.True(twin.Water.ReceivedLitres > 0);
        twin.AssertIdentity();
    }

    [Fact]
    public void ACellRestingOnAPlacedBlockIsNotOnTheGroundAndIsNotHanded()
    {
        // The seam reads the seed surface, never the edited one: a block placed on the ground
        // lifts the water a voxel off the plane, and water standing on a block is a player's,
        // not the basin's. It rests there, awake for nothing, for ever.
        var twin = new Twin();
        var block = new VoxelPos(60, Floor + 1, 60);
        twin.Terrain.Placed.Add(block);
        var on = block with { Y = Floor + 2 };

        twin.Fluid.Add(on, 500);
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(0, twin.Tick());
        }

        Assert.Equal(500, twin.Fluid.Grid.AmountAt(on));
        Assert.Equal(0, twin.Water.ReceivedLitres);
        Assert.Equal(0, twin.Handoff.PendingMillilitres);
        Assert.Equal(0, twin.Fluid.ActiveCellCount);
        twin.AssertIdentity();
    }

    [Fact]
    public void ATankWhoseFloorIsTheNaturalGroundLeaksIntoTheBasinExactlyAsRainWould()
    {
        // The stated limit, pinned so that a change to it is deliberate. Four walls on the bowl's
        // floor and water inside: the water stands on the plane, so it is handed to the basin the
        // moment it rests — Tier A has no representation of a walled-off patch of catchment, and
        // rain falling inside the same walls goes to the pit too. Revisit trigger: the first play
        // that builds one and reads the leak as wrong.
        var twin = new Twin();
        for (int d = -2; d <= 2; d++)
        {
            for (int y = Floor + 1; y <= Floor + 3; y++)
            {
                twin.Terrain.Placed.Add(new VoxelPos(60 + d, y, 58));
                twin.Terrain.Placed.Add(new VoxelPos(60 + d, y, 62));
                twin.Terrain.Placed.Add(new VoxelPos(58, y, 60 + d));
                twin.Terrain.Placed.Add(new VoxelPos(62, y, 60 + d));
            }
        }

        long poured = 0;
        for (int x = 59; x <= 61; x++)
        {
            for (int z = 59; z <= 61; z++)
            {
                poured += Cap - twin.Fluid.Add(new VoxelPos(x, Floor + 1, z), Cap);
            }
        }

        Assert.Equal(9L * Cap, poured);
        twin.RunToRest();

        Assert.Equal(0, twin.Fluid.TotalMillilitres);
        Assert.Equal(poured, twin.Handoff.HandedMillilitres);
        Assert.Equal(poured / 1000, twin.Water.ReceivedLitres);
        twin.AssertIdentity();
    }

    [Fact]
    public void WaterRestingOnAnOutletSampleLeavesTheWorldAndCountsOnBothTerms()
    {
        // A column outside the window: its sample drains off the edge, so what rests there is
        // received and spilled in the same breath, and the basin is untouched. Kept in Tier B
        // instead, it would sleep on the ground until an edit happened to touch it.
        var twin = new Twin();
        var cell = new VoxelPos(-4, Rim + 1, 60);
        Assert.Equal(Watershed.Outlet, twin.Water.BasinAtSample(twin.Water.SampleOf(-4, 60).X, twin.Water.SampleOf(-4, 60).Z));
        Assert.True(twin.Terrain.CanHold(cell));

        twin.Fluid.Add(cell, 500);
        Assert.Equal(500, twin.Tick());
        Assert.Equal(0, twin.Water.ReceivedLitres);
        Assert.Equal(500, twin.Handoff.PendingMillilitres);
        Assert.Equal(500, twin.Handoff.PendingFor(Watershed.Outlet));

        twin.Fluid.Add(cell, 700);
        Assert.Equal(700, twin.Tick());
        Assert.Equal(1, twin.Water.ReceivedLitres);
        Assert.Equal(1, twin.Water.SpilledLitres);
        Assert.Equal(0, twin.Water.StoredLitres);
        Assert.Equal(200, twin.Handoff.PendingMillilitres);
        Assert.Equal(0, twin.Fluid.TotalMillilitres);
        twin.AssertIdentity();
    }

    [Fact]
    public void AWindowMoveKeepsTheRemainderBecauseItIsKeyedByThePit()
    {
        // The basin's index renumbers when the window moves; its pit does not. A remainder keyed
        // by index would reattach to whatever basin got that number, or be lost.
        var twin = new Twin();
        twin.Fluid.Add(new VoxelPos(60, Floor + 1, 60), 625);
        Assert.Equal(625, twin.Tick());
        Assert.Equal(625, twin.Handoff.PendingFor(twin.Basin));
        Basin before = twin.Water.Window!.Watershed.Basins[twin.Basin];

        twin.Water.Install(twin.Water.AnalyseFor(112, 112));
        int moved = twin.Water.BasinAtSample(48 / Cell, 48 / Cell);
        Assert.NotEqual(Watershed.Outlet, moved);
        Basin after = twin.Water.Window!.Watershed.Basins[moved];
        Assert.Equal((before.PitX, before.PitZ), (after.PitX, after.PitZ));
        Assert.Equal(625, twin.Handoff.PendingFor(moved));
        Assert.Equal(625, twin.Handoff.PendingMillilitres);

        twin.Fluid.Add(new VoxelPos(60, Floor + 1, 60), 400);
        Assert.Equal(400, twin.Tick());
        Assert.Equal(1, twin.Water.ReceivedLitres);
        Assert.Equal(1, twin.Water.StoredLitres);
        Assert.Equal(25, twin.Handoff.PendingMillilitres);
        twin.AssertIdentity();
    }

    [Fact]
    public void BeforeAWindowLandsGroundCellsAreWokenRatherThanLeftAsleepAndHandedOnceItHas()
    {
        // The first analysis takes tens of milliseconds on a worker, and a cell that rests on the
        // ground in that gap has nowhere to go. Left asleep it would sit there until an edit
        // touched it; woken, it rests again next tick and is listed again, and the first boundary
        // with a window takes it.
        var twin = new Twin(installWindow: false);
        var cell = new VoxelPos(60, Floor + 1, 60);
        twin.Fluid.Add(cell, 500);

        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(0, twin.Tick());
            Assert.Equal(1, twin.Handoff.OnGroundLastPass);
            Assert.Equal(500, twin.Fluid.Grid.AmountAt(cell));

            // Awake: the cell itself, which Disturb wakes because it holds water. (Disturb wakes
            // nothing else here — the six neighbours hold nothing.)
            Assert.Equal(1, twin.Fluid.ActiveCellCount);
        }

        twin.Water.Install(twin.Water.AnalyseFor(64, 64));
        Assert.Equal(500, twin.Tick());
        Assert.Equal(0, twin.Fluid.TotalMillilitres);
        Assert.Equal(500, twin.Handoff.PendingMillilitres);

        // The hand-off woke the neighbours of what it took; they find nothing and sleep.
        twin.Fluid.Step();
        Assert.Equal(0, twin.Handoff.Absorb());
        Assert.Equal(0, twin.Fluid.ActiveCellCount);
        twin.AssertIdentity();
    }

    [Fact]
    public void TheRestedListIsTheCellsThatHeldWaterAndMovedNothingAndIsClearedWhenATickBegins()
    {
        // The automaton's own record of rest. A full cell on a floor of a 1x1 shaft and a
        // half-full cell above it: both rest. An empty cell visited is not listed, and the list
        // is the last tick's until the next begins.
        var sim = new FluidSimulation(new BoxTerrain(0, 0, 0, 0, 5, 0));
        Assert.Empty(sim.Rested);

        sim.Add(new VoxelPos(0, 0, 0), Cap);
        sim.Add(new VoxelPos(0, 1, 0), Cap / 2);
        sim.Step();
        Assert.Equal(
            [new VoxelPos(0, 0, 0), new VoxelPos(0, 1, 0)],
            sim.Rested.OrderBy(v => v.Y).ToArray());

        // An add two cells up wakes it and its neighbours, not the floor: the drop falls into
        // the half-full cell, which then rests and is listed; the cell it fell out of is empty
        // and is not; the floor cell was never visited this tick and is not either. A record
        // of visits, not of the state.
        sim.Add(new VoxelPos(0, 2, 0), 100);
        sim.Step();
        Assert.Equal([new VoxelPos(0, 1, 0)], sim.Rested);
        Assert.Equal(Cap / 2 + 100, sim.Grid.AmountAt(new VoxelPos(0, 1, 0)));

        // A slice that begins the next tick clears it, and it is the new tick's from then on.
        sim.Add(new VoxelPos(0, 3, 0), 100);
        Assert.Single(sim.Rested);
        sim.Resume(1);
        Assert.True(sim.TickInProgress);
        Assert.Empty(sim.Rested.Where(v => v == new VoxelPos(0, 1, 0)));
        sim.Step();
        Assert.False(sim.TickInProgress);
        Assert.Contains(new VoxelPos(0, 1, 0), sim.Rested);

        // And a cell that moved is not listed even though it still holds water: a full cell on
        // an open floor spreads to its neighbours and keeps three fifths, and "moved something"
        // is the whole of what separates resting water from moving water. The battery's recipe
        // that listed movers too passed every hand-off test, because a spill handed off mid-flow
        // still ends in the basin; this is the assertion that sees it.
        // (The neighbours Add woke are in the same batch and spread on in their turn, so more
        // than one cell moves; none of them rests.)
        var floor = new FluidSimulation(BoxTerrain.Cube(5));
        floor.Add(new VoxelPos(2, 0, 2), Cap);
        FluidTickResult spread = floor.Step();
        Assert.True(spread.CellsMoved >= 1);
        Assert.True(floor.Grid.AmountAt(new VoxelPos(2, 0, 2)) > 0);
        Assert.Empty(floor.Rested);
    }

    [Fact]
    public void WaterStillSpreadingOnTheGroundIsNotHandedUntilItRests()
    {
        // DESIGN's word is *stable* cells merge. A full cell put on the bowl's floor spreads for
        // several ticks before any cell of the puddle rests, and nothing is handed until one does
        // — the pour is seen spreading before it soaks away, rather than vanishing where it lands.
        var twin = new Twin();
        twin.Fluid.Add(new VoxelPos(60, Floor + 1, 60), Cap);

        Assert.Equal(0, twin.Tick());
        Assert.Equal(Cap, twin.Fluid.TotalMillilitres);
        Assert.Equal(0, twin.Handoff.OnGroundLastPass);
        Assert.True(twin.Fluid.Grid.HeldCellCount >= 5, "the first tick did not spread");

        // The first boundary handed nothing (asserted above); count the ticks until one does.
        int ticksOfSpreading = 1;
        while (twin.Tick() == 0)
        {
            ticksOfSpreading++;
            Assert.True(ticksOfSpreading < 100, "nothing ever rested");
        }

        Assert.True(twin.Handoff.HandedMillilitres > 0);
        Assert.True(twin.Fluid.TotalMillilitres > 0, "everything was handed the moment anything rested");
        twin.AssertIdentity();
        output.WriteLine($"first hand-off after {ticksOfSpreading} tick(s) of spreading");
    }

    [Fact]
    public void HandOffCountsOnItsOwnTermAndWakesWhatWasAbove()
    {
        // The B → A mutator: Remove with a different counter, and the same wake, so the water
        // above a handed cell falls into the space it left.
        var sim = new FluidSimulation(new BoxTerrain(0, 0, 0, 0, 5, 0));
        sim.Add(new VoxelPos(0, 1, 0), Cap);
        sim.Add(new VoxelPos(0, 2, 0), Cap);
        FluidAssert.SettlesAndRests(sim);

        Assert.Equal(Cap, sim.HandOff(new VoxelPos(0, 0, 0), Cap));
        Assert.Equal(Cap, sim.HandedMillilitres);
        Assert.Equal(0, sim.RemovedMillilitres);
        Assert.True(sim.ActiveCellCount > 0);
        FluidAssert.SettlesAndRests(sim);

        Assert.Equal(Cap, sim.Grid.AmountAt(new VoxelPos(0, 0, 0)));
        Assert.Equal(0, sim.Grid.AmountAt(new VoxelPos(0, 1, 0)));
        Assert.Equal(Cap, sim.TotalMillilitres);
        Assert.Equal(
            sim.AddedMillilitres - sim.RemovedMillilitres - sim.DisplacedMillilitres - sim.HandedMillilitres,
            sim.TotalMillilitres);
    }

    [Theory]
    [InlineData(0x5EEDUL)]
    [InlineData(0xA11CEUL)]
    [InlineData(0xBEEFUL)]
    public void OnRealTerrainASpillEndsInTierAAndTheAutomatonStops(ulong seed)
    {
        // The bench's case as a test: the real generator, a window analysed over it, drops on the
        // ground, and nothing left in Tier B when the automaton stops — with the loaded box deep
        // enough that every column's ground plane is inside it, or water resting on the box's
        // floor would be resting on nothing this seam recognises.
        var strata = new StrataTable([new StrataLayer(1, 1), new StrataLayer(2, 3)], 3);
        var generator = new WorldGenerator(
            new WorldGenSettings
            {
                Seed = seed,
                SeaLevelVoxels = 0,
                SurfaceAmplitudeVoxels = 48.0f,
                SurfaceFrequency = 0.004f,
                SurfaceOctaves = 4,
                CavesEnabled = false,
            },
            strata);

        int lowest = int.MaxValue;
        int highest = int.MinValue;
        for (int x = -24; x <= 24; x++)
        {
            for (int z = -24; z <= 24; z++)
            {
                int h = generator.SurfaceHeightAt(x, z);
                lowest = Math.Min(lowest, h);
                highest = Math.Max(highest, h);
            }
        }

        var terrain = new GeneratedTerrain(generator, -24, lowest - 1, -24, 24, highest + 8, 24);
        var water = new WaterSystem(generator, seed, new BasinLedger(), 16);
        water.Install(water.AnalyseFor(0, 0));
        var sim = new FluidSimulation(terrain);
        var handoff = new SurfaceHandoff(generator, water, sim);

        var random = new Random((int)(seed & 0x7FFFFFFF));
        long added = 0;
        for (int drop = 0; drop < 24; drop++)
        {
            int x = random.Next(-16, 16);
            int z = random.Next(-16, 16);
            VoxelPos? top = terrain.TopOpening(x, z);
            if (top is null)
            {
                continue;
            }

            added += Cap - sim.Add(top.Value, Cap);
        }

        Assert.True(added > 0, "no water was ever poured");

        int ticks = 0;
        while (ticks < 5_000)
        {
            long handed = handoff.Absorb();
            if (sim.ActiveCellCount == 0 && handed == 0)
            {
                break;
            }

            sim.Step();
            ticks++;
        }

        Assert.True(ticks < 5_000, $"still moving after {ticks} ticks");
        Assert.True(sim.VerifyQuiescent());
        Assert.Equal(0, sim.TotalMillilitres);
        Assert.Equal(added, sim.HandedMillilitres);
        Assert.Equal(
            added,
            (water.ReceivedLitres * FluidConstants.MillilitresPerLitre) + handoff.PendingMillilitres);
        Assert.Equal(
            water.ReceivedLitres - water.SpilledLitres, water.StoredLitres);
        output.WriteLine(
            $"seed {seed:X}: {added / 1000.0:N1} L in {ticks} ticks, {water.ReceivedLitres} L received into "
            + $"{handoff.PendingBasins} basin(s), {water.SpilledLitres} L off the window, "
            + $"{handoff.PendingMillilitres} mL pending, peak active {sim.PeakActiveCells}");
    }

    private static long Pour(FluidSimulation sim, VoxelPos at, int r, long pending)
    {
        int r2 = r * r;
        for (int dy = r; dy >= -r; dy--)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    if ((dx * dx) + (dy * dy) + (dz * dz) > r2)
                    {
                        continue;
                    }

                    pending = sim.Add(at.Offset(dx, dy, dz), pending);
                    if (pending == 0)
                    {
                        return 0;
                    }
                }
            }
        }

        return pending;
    }
}
