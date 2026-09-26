using Overburden.Core.Fluid;
using Overburden.Core.Structure;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// COLLAPSE-PLAN part b-1: the director between the events and the world, on a fake clock — the
/// plug to <c>at risk 0</c> through the fuse, dry landings in the frame and wet ones at the
/// boundary, the water's way up to the room, one reservation set across events, the path re-check,
/// the edge term, the quit, the joins and the re-plan's trigger.
/// </summary>
public class CollapseDirectorTests
{
    private const int Air = SupportTestWorld.Air;
    private const int Stone = SupportTestWorld.Stone;
    private const int Rubble = SupportTestWorld.Rubble;
    private const long Cap = FluidConstants.CellCapacityMillilitres;
    private static readonly ChunkPos[] TwoByTwo = [new(0, 0, 0), new(1, 0, 0), new(0, 0, 1), new(1, 0, 1)];
    private static readonly ChunkPos Sky = new(0, 1, 0);

    // ---- The rig ------------------------------------------------------------------------------

    /// <summary>Water in a dictionary: a cell holds up to a capacity, only air and only a chunk that is here holds any.</summary>
    private sealed class FakeWater(SupportTestWorld world) : ICollapseWater
    {
        public Dictionary<VoxelPos, long> Cells { get; } = [];

        public HashSet<ChunkPos> NotReady { get; } = [];

        public long Displaced { get; private set; }

        public void Fill(VoxelPos min, VoxelPos max)
        {
            for (int y = min.Y; y <= max.Y; y++)
            for (int x = min.X; x <= max.X; x++)
            for (int z = min.Z; z <= max.Z; z++)
            {
                var v = new VoxelPos(x, y, z);
                if (world.Get(v.Chunk) is not null && world.Material(v) == Air)
                {
                    Cells[v] = Cap;
                }
            }
        }

        public long Total => Cells.Values.Sum();

        public long Lift(VoxelPos cell) => Cells.Remove(cell, out long held) ? held : 0;

        public long Return(VoxelPos cell, long millilitres)
        {
            if (millilitres == 0 || world.Get(cell.Chunk) is null || world.Material(cell) != Air)
            {
                return millilitres;
            }

            long held = Cells.GetValueOrDefault(cell);
            long taken = Math.Min(Cap - held, millilitres);
            if (taken > 0)
            {
                Cells[cell] = held + taken;
            }

            return millilitres - taken;
        }

        public void DisplaceLifted(long millilitres) => Displaced += millilitres;

        public bool IsReady(ChunkPos chunk) => !NotReady.Contains(chunk);
    }

    /// <summary>The test world's editor with the copy's wet question answered from the fake, and the landing oracle read at the write.</summary>
    private sealed class WetEditor(SupportTestWorld world, SupportRegion region) : IVoxelEditor
    {
        private readonly SupportTestWorld.Editor _inner = new(world, region);

        public FakeWater? Water { get; set; }

        public long RubbleOnSolid { get; private set; }

        public bool TryGetMaterial(VoxelPos voxel, out int materialId) => _inner.TryGetMaterial(voxel, out materialId);

        public bool SetVoxel(VoxelPos voxel, int materialId)
        {
            if (materialId == Rubble && _inner.TryGetMaterial(voxel, out int was) && was != Air)
            {
                RubbleOnSolid++;
            }

            return _inner.SetVoxel(voxel, materialId);
        }

        public bool HoldsWater(VoxelPos voxel) => Water?.Cells.ContainsKey(voxel) ?? false;
    }

    private sealed class Rig
    {
        public required SupportTestWorld World { get; init; }

        public required SupportRegion Region { get; init; }

        public required WetEditor Editor { get; init; }

        public required CollapseDirector Director { get; init; }

        public FakeWater? Water { get; init; }

        public CollapseScheduler? Scheduler { get; init; }

        public long Now { get; set; }

        public long LastSettle { get; set; }

        public bool Dirty { get; set; }

        public List<CollapseEventSummary> Ended { get; } = [];

        /// <summary>Frames at 16 ms up to <paramref name="untilMillis"/>: a boundary (fuse, wet landings) every 31st, a settle at most every 250 ms after a write, the identity at every frame.</summary>
        public void Run(long untilMillis, bool settle = true, bool boundaries = true)
        {
            for (; Now <= untilMillis; Now += 16)
            {
                bool boundary = boundaries && (Now / 16) % 31 == 0;
                if (boundary)
                {
                    Scheduler?.Sweep(Now);
                }

                Dirty |= Director.Frame(Now).Wrote;
                if (boundary)
                {
                    Dirty |= Director.Boundary(Now).Wrote;
                }

                Ended.AddRange(Director.TakeEnded());
                Assert.Equal(Director.ReleasedVoxels, Director.LandedVoxels + Director.InFlightVoxels + Director.LostVoxels);
                if (settle && Dirty && Now - LastSettle >= 250)
                {
                    Region.Settle();
                    Dirty = false;
                    LastSettle = Now;
                }
            }
        }
    }

    /// <remarks>The frame budget is wall-clock, so the rig lifts it — a cold JIT would otherwise decide which releases a test sees. <see cref="PastTheBudgetOneSolveIsLetThroughAFrame"/> pins the rule at zero, which is deterministic.</remarks>
    private static Rig NewRig(SupportTestWorld world, IEnumerable<ChunkPos> dug, bool water = false, bool scheduler = false, double budgetMillis = double.PositiveInfinity)
    {
        SupportRegion region = world.NewRegion();
        foreach (ChunkPos chunk in dug)
        {
            region.MarkDug(chunk);
        }

        region.Settle();
        var editor = new WetEditor(world, region);
        FakeWater? fake = water ? new FakeWater(world) : null;
        editor.Water = fake;
        var director = new CollapseDirector(region, editor, world.Materials, Rubble, seed: 7, fake) { BudgetMillis = budgetMillis };
        CollapseScheduler? fuse = scheduler
            ? new CollapseScheduler(region, editor, world.Materials, Rubble, 7, new CollapseWindow { MinSeconds = 0, MaxSeconds = 0 }, fake) { Director = director }
            : null;
        return new Rig { World = world, Region = region, Editor = editor, Director = director, Water = fake, Scheduler = fuse };
    }

    /// <summary>A stone floor (chunks −1..1 at Y 0, its top at Y 31) under air, with a stone slab hanging in the air chunk above the origin.</summary>
    private static Rig Slab(VoxelPos min, VoxelPos max, bool water = false)
    {
        var world = new SupportTestWorld();
        world.FillBox(new ChunkPos(-1, 0, -1), new ChunkPos(1, 0, 1), Stone);
        world.FillBox(new ChunkPos(-1, 1, -1), new ChunkPos(1, 2, 1), Air);
        world.SetBox(min, max, Stone);
        Rig rig = NewRig(world, [Sky], water);
        Assert.Equal((max.X - min.X + 1) * (max.Y - min.Y + 1) * (max.Z - min.Z + 1), rig.Region.SettledAtRiskVoxels);
        return rig;
    }

    private static CollapseEvent Fire(Rig rig, ChunkPos chunk)
    {
        Assert.True(rig.Region.TryReadSettled(chunk, out ChunkSupport field));
        CollapseEvent ev = rig.Director.Fire(chunk, field, rig.Now)!;
        Assert.NotNull(ev);
        return ev;
    }

    // ---- The plug, through the fuse -----------------------------------------------------------

    [Fact]
    public void ThePlugComesDownInPiecesThroughTheFuseToAtRiskZero()
    {
        var world = new SupportTestWorld();
        world.FillBox(new ChunkPos(-2, -2, -2), new ChunkPos(3, 2, 3), Stone);
        foreach (ChunkPos bite in TwoByTwo)
        {
            world.Fill(bite, Air);
        }

        Rig rig = NewRig(world, TwoByTwo, scheduler: true);
        Assert.Equal(20, rig.Region.SettledAtRiskVoxels);
        (long solidBefore, long sumBefore) = world.Census();

        rig.Run(60_000);

        CollapseDirector d = rig.Director;
        Assert.Equal(20, d.ReleasedVoxels);
        Assert.Equal(20, d.LandedVoxels);
        Assert.Equal(0, d.LostVoxels);
        Assert.Equal(20, world.Count(Rubble));
        Assert.Equal(0, rig.Editor.RubbleOnSolid);
        Assert.Equal(0, rig.Region.SettledAtRiskVoxels);
        Assert.Empty(d.Events);
        Assert.Equal(0, rig.Scheduler!.Armed);
        Assert.Equal(d.Started, d.Ended);
        Assert.Equal(d.Started, rig.Ended.Count);
        Assert.Equal(20, rig.Ended.Sum(e => e.Voxels));
        (long solidAfter, long sumAfter) = world.Census();
        Assert.Equal(solidBefore, solidAfter);
        Assert.True(sumAfter < sumBefore);

        // Pieces, not a sweep: more releases than the four chunks, and a member that joined
        // rather than lit its own fuse.
        Assert.True(d.Released > 4, $"{d.Released} piece(s)");
        Assert.True(d.Joined > 0);
    }

    [Fact]
    public void WithFiringOffTheFuseArmsAndNoEventStarts()
    {
        var world = new SupportTestWorld();
        world.FillBox(new ChunkPos(-2, -2, -2), new ChunkPos(3, 2, 3), Stone);
        foreach (ChunkPos bite in TwoByTwo)
        {
            world.Fill(bite, Air);
        }

        Rig rig = NewRig(world, TwoByTwo, scheduler: true);
        rig.Scheduler!.FireEnabled = false;
        rig.Run(5_000);

        Assert.Equal(4, rig.Scheduler.Armed);
        Assert.Equal(0, rig.Director.Started);
        Assert.Equal(0, rig.Director.Joined);
        Assert.Equal(0, rig.Director.Released);
    }

    [Fact]
    public void AMemberNeverArmsAndAnArmedNeighbourJoinsTheOlderEvent()
    {
        var world = new SupportTestWorld();
        world.FillBox(new ChunkPos(-2, -2, -2), new ChunkPos(3, 2, 3), Stone);
        foreach (ChunkPos bite in TwoByTwo)
        {
            world.Fill(bite, Air);
        }

        Rig rig = NewRig(world, TwoByTwo);
        // The shipped window, not the rig's instant one: a member that armed would sit armed.
        var fuse = new CollapseScheduler(rig.Region, rig.Editor, world.Materials, Rubble, 7) { Director = rig.Director };
        ChunkPos first = TwoByTwo[0].Offset(0, 1, 0);
        ChunkPos diagonal = TwoByTwo[3].Offset(0, 1, 0);
        CollapseEvent older = Fire(rig, first);
        CollapseEvent younger = Fire(rig, diagonal);
        Assert.NotSame(older, younger);

        // The two left are face-adjacent to both; each joins the older, and nothing stays armed —
        // the members never arm at all.
        fuse.Sweep(0);
        Assert.Equal(0, fuse.Armed);
        Assert.Equal(2, rig.Director.Joined);
        Assert.Same(older, rig.Director.EventOf(TwoByTwo[1].Offset(0, 1, 0)));
        Assert.Same(older, rig.Director.EventOf(TwoByTwo[2].Offset(0, 1, 0)));
        Assert.Same(younger, rig.Director.EventOf(diagonal));
        Assert.Equal(3, older.Members.Count);
    }

    // ---- Dry and wet --------------------------------------------------------------------------

    [Fact]
    public void ADryLandingIsWrittenInTheFrameItIsDue()
    {
        Rig rig = Slab(new VoxelPos(8, 50, 8), new VoxelPos(9, 50, 9));
        Fire(rig, Sky);
        rig.Run(10_000, boundaries: false);

        Assert.Equal(4, rig.Director.LandedVoxels);
        Assert.Equal(0, rig.Director.WetLandings);
        Assert.Equal(4, rig.World.Count(Rubble));
        Assert.Equal(32, Rubbles(rig).Min(v => v.Y));
    }

    [Fact]
    public void AWetLandingWaitsForTheBoundaryAndLiftsWhatItLandsIn()
    {
        Rig rig = Slab(new VoxelPos(8, 50, 8), new VoxelPos(9, 50, 9), water: true);
        rig.Water!.Fill(new VoxelPos(-12, 32, -12), new VoxelPos(28, 35, 28));
        long before = rig.Water.Total;
        Fire(rig, Sky);

        // Frames only: the piece falls, is due, and hovers — reserved, unwritten.
        rig.Run(10_000, boundaries: false);
        Assert.Equal(0, rig.Director.LandedVoxels);
        Assert.Equal(1, rig.Director.WetWaiting);
        Assert.Equal(4, rig.Director.InFlightVoxels);
        Assert.Equal(0, rig.World.Count(Rubble));

        // A chunk not ready for water holds it at the boundary too.
        rig.Water.NotReady.Add(Sky);
        rig.Director.Boundary(rig.Now);
        Assert.Equal(0, rig.Director.LandedVoxels);
        Assert.Equal(1, rig.Director.WetWaiting);

        rig.Water.NotReady.Clear();
        rig.Director.Boundary(rig.Now);
        Assert.Equal(4, rig.Director.LandedVoxels);
        Assert.Equal(0, rig.Director.WetWaiting);
        Assert.Equal(4 * Cap, rig.Director.LiftedMillilitres);
        Assert.Equal(rig.Director.LiftedMillilitres, rig.Director.ReturnedMillilitres);
        Assert.Equal(0, rig.Director.DisplacedMillilitres);
        Assert.Equal(before, rig.Water.Total);
        Assert.All(Rubbles(rig), v => Assert.False(rig.Water.Cells.ContainsKey(v)));

        // Into the box round the landing, first: the pool's cells over it had room, so the dry
        // cavity the slab left and the column above stay as they were.
        Assert.All(new[] { new VoxelPos(8, 50, 8), new VoxelPos(9, 50, 8), new VoxelPos(8, 50, 9), new VoxelPos(9, 50, 9) },
            v => Assert.False(rig.Water.Cells.ContainsKey(v)));
        Assert.Equal(0, rig.Director.ClimbedMillilitres);
        Assert.Equal(4 * Cap, rig.Water.Cells.Where(kv => kv.Key.Y == 36).Sum(kv => kv.Value));
    }

    [Fact]
    public void WhenTheBoxAndTheCavityAreFullTheWaterGoesUpToTheRoom()
    {
        // A lake to Y 60 over the landing: the box grown by a brush cell is full, the cavity the
        // slab left is full (the lake filled it while it fell), and the room is at the surface.
        Rig rig = Slab(new VoxelPos(8, 50, 8), new VoxelPos(9, 50, 9), water: true);
        Fire(rig, Sky);
        rig.Run(0, boundaries: false);
        Assert.Equal(4, rig.Director.InFlightVoxels);
        rig.Water!.Fill(new VoxelPos(-32, 32, -32), new VoxelPos(63, 60, 63));
        long before = rig.Water.Total;

        rig.Run(10_000);

        Assert.Equal(4, rig.Director.LandedVoxels);
        Assert.Equal(4 * Cap, rig.Director.LiftedMillilitres);
        Assert.Equal(4 * Cap, rig.Director.ReturnedMillilitres);
        Assert.Equal(4 * Cap, rig.Director.ClimbedMillilitres);
        Assert.Equal(0, rig.Director.DisplacedMillilitres);
        Assert.Equal(before, rig.Water.Total);
        Assert.Equal(4 * Cap, rig.Water.Cells.Where(kv => kv.Key.Y > 60).Sum(kv => kv.Value));
    }

    [Fact]
    public void WhenTheBoxIsFullTheCavityTakesTheWaterBeforeTheColumn()
    {
        // A pool to Y 45: the box round the landing is full, the slab's cavity at Y 50 is dry and
        // above the pool, and the landing column has room from Y 46. The cavity comes first.
        Rig rig = Slab(new VoxelPos(8, 50, 8), new VoxelPos(9, 50, 9), water: true);
        Fire(rig, Sky);
        rig.Run(0, boundaries: false);
        VoxelPos[] cavity = [.. rig.Director.Events[0].InFlight[0].Piece.Voxels];
        rig.Water!.Fill(new VoxelPos(-32, 32, -32), new VoxelPos(63, 45, 63));

        rig.Run(10_000);

        Assert.Equal(4, rig.Director.LandedVoxels);
        Assert.Equal(4 * Cap, rig.Director.ReturnedMillilitres);
        Assert.Equal(0, rig.Director.ClimbedMillilitres);
        Assert.All(cavity, v => Assert.Equal(Cap, rig.Water.Cells.GetValueOrDefault(v)));
    }

    [Fact]
    public void WaterWithNoRoomUpToTheLoadedTopIsDisplacedAndCounted()
    {
        // The same lake, filled to the top of the loaded world: the climb stops at the unloaded
        // chunk above, and what it lifted has nowhere to go.
        Rig rig = Slab(new VoxelPos(8, 50, 8), new VoxelPos(9, 50, 9), water: true);
        Fire(rig, Sky);
        rig.Run(0, boundaries: false);
        rig.Water!.Fill(new VoxelPos(-32, 32, -32), new VoxelPos(63, 95, 63));

        rig.Run(10_000, settle: false);

        Assert.Equal(4, rig.Director.LandedVoxels);
        Assert.Equal(4 * Cap, rig.Director.LiftedMillilitres);
        Assert.Equal(0, rig.Director.ReturnedMillilitres);
        Assert.Equal(4 * Cap, rig.Director.DisplacedMillilitres);
        Assert.Equal(4 * Cap, rig.Water.Displaced);
    }

    // ---- One reservation set ------------------------------------------------------------------

    /// <summary>One voxel in each of two diagonal chunks, and a one-voxel pit in the floor both can steer to.</summary>
    private static (Rig Rig, VoxelPos A, VoxelPos B, VoxelPos Pit) TwoVoxelsOverAPit(double budgetMillis = double.PositiveInfinity)
    {
        var world = new SupportTestWorld();
        world.FillBox(new ChunkPos(-1, 0, -1), new ChunkPos(2, 0, 2), Stone);
        world.FillBox(new ChunkPos(-1, 1, -1), new ChunkPos(2, 2, 2), Air);
        var a = new VoxelPos(31, 40, 31);
        var b = new VoxelPos(32, 40, 32);
        var pit = new VoxelPos(31, 31, 32);
        world.Set(a, Stone);
        world.Set(b, Stone);
        world.Set(pit, Air);
        Rig rig = NewRig(world, [a.Chunk, b.Chunk], budgetMillis: budgetMillis);
        Assert.Equal(2, rig.Region.SettledAtRiskVoxels);
        return (rig, a, b, pit);
    }

    [Fact]
    public void TwoEventsNeverRestInTheSameCell()
    {
        // The first release takes the pit; the second must rest elsewhere.
        (Rig rig, VoxelPos a, VoxelPos b, VoxelPos pit) = TwoVoxelsOverAPit();
        CollapseEvent first = Fire(rig, a.Chunk);
        CollapseEvent second = Fire(rig, b.Chunk);
        Assert.NotSame(first, second);
        rig.Run(0, boundaries: false);

        Assert.Single(first.InFlight);
        Assert.Single(second.InFlight);
        VoxelPos restA = first.InFlight[0].Plan.Rest;
        VoxelPos restB = second.InFlight[0].Plan.Rest;
        Assert.Equal(pit, restA);
        Assert.NotEqual(restA, restB);
        Assert.Equal(32, restB.Y);

        rig.Run(10_000, boundaries: false);
        Assert.Equal(2, rig.World.Count(Rubble));
        Assert.Equal(0, rig.Editor.RubbleOnSolid);
    }

    [Fact]
    public void PastTheBudgetOneSolveIsLetThroughAFrame()
    {
        // A budget of zero is spent before the first release: one solve a frame, across events.
        (Rig rig, VoxelPos a, VoxelPos b, _) = TwoVoxelsOverAPit(budgetMillis: 0);
        CollapseEvent first = Fire(rig, a.Chunk);
        CollapseEvent second = Fire(rig, b.Chunk);
        rig.Run(0, boundaries: false);
        Assert.Equal(1, rig.Director.Solves);
        Assert.Single(first.InFlight);
        Assert.Empty(second.InFlight);

        rig.Run(16, boundaries: false);
        Assert.Equal(2, rig.Director.Solves);
        Assert.Single(second.InFlight);
    }

    // ---- The path re-check --------------------------------------------------------------------

    [Fact]
    public void ABlockPlacedInAFlightsPathIsLandedOn()
    {
        Rig rig = Slab(new VoxelPos(8, 50, 8), new VoxelPos(9, 50, 9));
        Fire(rig, Sky);
        rig.Run(500, boundaries: false);
        CollapseFlight flight = rig.Director.Events[0].InFlight[0];
        VoxelPos rest = flight.Plan.Rest;
        Assert.Equal(32, rest.Y);
        Assert.True(flight.Anchor.Y > 36, $"the piece is at {flight.Anchor} already");

        // A block three voxels high standing on the floor over its rest — on the floor, or it
        // would be at risk itself and come down with the event — written the way a brush writes.
        // The steer is over by then (it steers a mino a tick while it falls, eight at most), so
        // the last of its path runs straight down into the block.
        for (int x = rest.X - 1; x <= rest.X + 3; x++)
        for (int z = rest.Z - 1; z <= rest.Z + 3; z++)
        for (int y = 32; y <= 34; y++)
        {
            rig.Editor.SetVoxel(new VoxelPos(x, y, z), Stone);
        }

        rig.Run(10_000, boundaries: false);

        Assert.Equal(1, rig.Director.Blocked);
        Assert.Equal(0, rig.Director.LiftedByABlock);
        Assert.Equal(4, rig.Director.LandedVoxels);
        List<VoxelPos> rubble = Rubbles(rig);
        Assert.Equal(4, rubble.Count);
        Assert.Equal(35, rubble.Min(v => v.Y));
        Assert.Equal(0, rig.Editor.RubbleOnSolid);
    }

    [Fact]
    public void AFlightBlockedWhereItStandsRisesClearAndLandsOnTheBlock()
    {
        Rig rig = Slab(new VoxelPos(8, 50, 8), new VoxelPos(9, 50, 9));
        Fire(rig, Sky);
        rig.Run(0, boundaries: false);
        CollapseFlight flight = rig.Director.Events[0].InFlight[0];
        VoxelPos at = flight.Anchor;

        // Stone into the cells the piece is drawn in, and everything round and under them to the
        // floor: no first step is clear, sideways or down, and the start is taken.
        for (int x = at.X - 10; x <= at.X + 11; x++)
        for (int z = at.Z - 10; z <= at.Z + 11; z++)
        for (int y = 32; y <= at.Y; y++)
        {
            rig.Editor.SetVoxel(new VoxelPos(x, y, z), Stone);
        }

        rig.Run(10_000, boundaries: false);

        Assert.Equal(1, rig.Director.Blocked);
        Assert.Equal(1, rig.Director.LiftedByABlock);
        Assert.Equal(4, rig.Director.LandedVoxels);
        Assert.Equal(0, rig.Editor.RubbleOnSolid);
        Assert.Equal(at.Y + 1, Rubbles(rig).Min(v => v.Y));
    }

    // ---- The edge and the quit ----------------------------------------------------------------

    [Fact]
    public void ALandingPastTheLoadedEdgeIsLostAndCounted()
    {
        Rig rig = Slab(new VoxelPos(8, 50, 8), new VoxelPos(9, 50, 9));
        Fire(rig, Sky);
        rig.Run(0, boundaries: false);
        Assert.Equal(4, rig.Director.InFlightVoxels);

        rig.World.Depart(Sky);
        rig.World.Depart(Sky.Offset(0, 1, 0));
        rig.Run(10_000, settle: false, boundaries: false);

        Assert.Equal(4, rig.Director.LostVoxels);
        Assert.Equal(0, rig.Director.LandedVoxels);
        Assert.Equal(0, rig.Director.InFlightVoxels);
        Assert.Empty(rig.Director.Reserved);
        Assert.Equal(rig.Director.ReleasedVoxels, rig.Director.LandedVoxels + rig.Director.LostVoxels);
    }

    [Fact]
    public void TheQuitLandsEveryFlightWhereItWouldHaveRested()
    {
        Rig rig = Slab(new VoxelPos(8, 50, 8), new VoxelPos(9, 50, 9), water: true);
        rig.Water!.Fill(new VoxelPos(-12, 32, -12), new VoxelPos(28, 32, 28));
        Fire(rig, Sky);
        rig.Run(0, boundaries: false);
        VoxelPos rest = rig.Director.Events[0].InFlight[0].Plan.Rest;

        CollapseStep quit = rig.Director.LandAll(rig.Now);

        Assert.Equal(1, quit.Landed);
        Assert.Equal(4, rig.Director.LandedVoxels);
        Assert.Equal(0, rig.Director.InFlightVoxels);
        Assert.Empty(rig.Director.Reserved);
        Assert.Contains(rest, Rubbles(rig));
        Assert.Equal(rig.Director.LiftedMillilitres, rig.Director.ReturnedMillilitres + rig.Director.DisplacedMillilitres);
        Assert.True(rig.Director.LiftedMillilitres > 0);
    }

    // ---- The re-plan --------------------------------------------------------------------------

    [Fact]
    public void TheQueueIsRecutOnlyWhenAMembersSettledSetChanges()
    {
        Rig rig = Slab(new VoxelPos(8, 50, 8), new VoxelPos(15, 50, 15));
        Fire(rig, Sky);

        // The first frame cuts and releases; the next release is a quarter second or more away.
        rig.Run(0, settle: false, boundaries: false);
        Assert.Equal(1, rig.Director.Replans);
        Assert.Equal(1, rig.Director.Released);

        // A settle over the released cells changes the set: one more cut.
        rig.Region.Settle();
        rig.Run(16, settle: false, boundaries: false);
        Assert.Equal(2, rig.Director.Replans);

        // A settle that changes nothing — a write and its undo — is a new snapshot with the same
        // set: no cut.
        var far = new VoxelPos(20, 60, 20);
        ChunkSupport before = rig.Region.SettledField(Sky)!;
        rig.Editor.SetVoxel(far, Stone);
        rig.Editor.SetVoxel(far, Air);
        rig.Region.Settle();
        Assert.NotSame(before, rig.Region.SettledField(Sky));
        rig.Run(32, settle: false, boundaries: false);
        Assert.Equal(1, rig.Director.Released);
        Assert.Equal(2, rig.Director.Replans);

        // And frames with no settle at all cut nothing, releases or not.
        rig.Run(3_000, settle: false, boundaries: false);
        Assert.True(rig.Director.Released > 1);
        Assert.Equal(2, rig.Director.Replans);
    }

    [Fact]
    public void AShoreRescuesTheQueueAtTheNextSettle()
    {
        Rig rig = Slab(new VoxelPos(8, 50, 8), new VoxelPos(11, 50, 11));
        Fire(rig, Sky);
        rig.Run(0, boundaries: false);
        CollapseFlight gone = rig.Director.Events[0].InFlight[0];
        Assert.Equal(4, gone.Piece.Voxels.Length);

        // A stone column from the floor under every slab voxel the first piece left.
        var left = new List<VoxelPos>();
        for (int x = 8; x <= 11; x++)
        for (int z = 8; z <= 11; z++)
        {
            var v = new VoxelPos(x, 50, z);
            if (!gone.Piece.Voxels.Contains(v))
            {
                left.Add(v);
                for (int y = 32; y < 50; y++)
                {
                    rig.Editor.SetVoxel(v with { Y = y }, Stone);
                }
            }
        }

        rig.Region.Settle();
        rig.Run(10_000, boundaries: false);

        Assert.Equal(4, rig.Director.ReleasedVoxels);
        Assert.Equal(12, rig.Director.RescuedVoxels);
        Assert.All(left, v => Assert.Equal(Stone, rig.World.Material(v)));
        Assert.Equal(4, rig.Director.LandedVoxels);
        Assert.Equal(0, rig.Editor.RubbleOnSolid);
    }

    // ---- The end, and a voxel mined from the queue ------------------------------------------

    [Fact]
    public void AnEventEndsTheGraceAfterItsMembersSettleAtRest()
    {
        Rig rig = Slab(new VoxelPos(8, 50, 8), new VoxelPos(9, 50, 9));
        Fire(rig, Sky);

        // Landed, and nothing settles: the member is unsettled since the rubble went in, so the
        // event stays however long it waits.
        rig.Run(10_000, settle: false, boundaries: false);
        Assert.Equal(4, rig.Director.LandedVoxels);
        Assert.False(rig.Region.IsSettled(Sky));
        Assert.Single(rig.Director.Events);

        // Settled at rest: the grace, then the end.
        rig.Region.Settle();
        long settledAt = rig.Now;
        rig.Run(settledAt + CollapseDirector.DefaultGraceMillis - 100, settle: false, boundaries: false);
        Assert.Single(rig.Director.Events);
        rig.Run(settledAt + CollapseDirector.DefaultGraceMillis + 100, settle: false, boundaries: false);
        Assert.Empty(rig.Director.Events);
        CollapseEventSummary ended = Assert.Single(rig.Ended);
        Assert.Equal(4, ended.Voxels);
        Assert.Equal(1, ended.Members);
    }

    [Fact]
    public void AQueuedVoxelMinedBeforeItsReleaseIsRescuedNotConjured()
    {
        // Mined between two settles: the snapshot still marks it, the world does not have it, and
        // the release reads the world — no piece of air falls and lands as rubble.
        Rig rig = Slab(new VoxelPos(8, 50, 8), new VoxelPos(11, 50, 11));
        Fire(rig, Sky);
        rig.Run(0, boundaries: false);
        CollapsePiece gone = rig.Director.Events[0].InFlight[0].Piece;
        for (int x = 8; x <= 11; x++)
        for (int z = 8; z <= 11; z++)
        {
            var v = new VoxelPos(x, 50, z);
            if (!gone.Voxels.Contains(v))
            {
                rig.Editor.SetVoxel(v, Air);
            }
        }

        rig.Run(10_000, settle: false, boundaries: false);

        Assert.Equal(4, rig.Director.ReleasedVoxels);
        Assert.Equal(12, rig.Director.RescuedVoxels);
        Assert.Equal(4, rig.World.Count(Rubble));
    }

    // ---- The cut on a worker (b-2's first step) ---------------------------------------------

    /// <summary>A runner the test completes by hand: the async path, deterministic.</summary>
    private sealed class HeldCuts
    {
        private readonly List<(Func<CollapseCut> Work, TaskCompletionSource<CollapseCut> Done)> _held = [];

        public int Held => _held.Count;

        public Task<CollapseCut> Run(Func<CollapseCut> work)
        {
            var done = new TaskCompletionSource<CollapseCut>();
            _held.Add((work, done));
            return done.Task;
        }

        public void CompleteAll()
        {
            foreach ((Func<CollapseCut> work, TaskCompletionSource<CollapseCut> done) in _held)
            {
                done.SetResult(work());
            }

            _held.Clear();
        }
    }

    [Fact]
    public void OnAWorkerTheOldQueueKeepsReleasingAndTheNewCutDropsWhatFellMeanwhile()
    {
        Rig rig = Slab(new VoxelPos(8, 50, 8), new VoxelPos(15, 50, 15));
        var cuts = new HeldCuts();
        rig.Director.CutRunner = cuts.Run;
        Fire(rig, Sky);

        // The first cut is on the worker: nothing to release until it lands.
        rig.Run(0, settle: false, boundaries: false);
        Assert.Equal(1, cuts.Held);
        Assert.Equal(0, rig.Director.Released);
        cuts.CompleteAll();
        rig.Run(16, settle: false, boundaries: false);
        Assert.Equal(1, rig.Director.Released);

        // A settle changes the set: a second cut goes to the worker and is held there while the
        // old queue goes on releasing.
        rig.Region.Settle();
        rig.Run(32, settle: false, boundaries: false);
        Assert.Equal(1, cuts.Held);
        rig.Run(2_000, settle: false, boundaries: false);
        long releasedMeanwhile = rig.Director.Released;
        Assert.True(releasedMeanwhile > 2, $"{releasedMeanwhile} released while the cut was held");

        // It lands: what fell while it ran is out of it, so nothing falls twice and nothing that
        // fell reads as rescued.
        cuts.CompleteAll();
        rig.Run(2_016, settle: false, boundaries: false);
        Assert.True(rig.Director.ReleasedDuringCuts > 0);
        for (long until = 2_032; until < 30_000; until += 16)
        {
            cuts.CompleteAll();
            rig.Run(until);
        }

        Assert.Equal(64, rig.Director.ReleasedVoxels);
        Assert.Equal(64, rig.Director.LandedVoxels);
        Assert.Equal(0, rig.Director.RescuedVoxels);
        Assert.Equal(64, rig.World.Count(Rubble));
        Assert.Equal(0, rig.Editor.RubbleOnSolid);
        Assert.Equal(rig.Director.CutsStarted, rig.Director.CutsApplied);
    }

    [Fact]
    public void ATriggerWhileACutRunsIsHeldAndCutOnceItLands()
    {
        Rig rig = Slab(new VoxelPos(8, 50, 8), new VoxelPos(15, 50, 15));
        var cuts = new HeldCuts();
        rig.Director.CutRunner = cuts.Run;
        Fire(rig, Sky);
        rig.Run(0, settle: false, boundaries: false);

        // A voxel added beside the slab grows the set while the first cut is held: one cut at a
        // time, the trigger kept. (Nothing has been released yet — the queue is on the worker —
        // so a settle alone would change nothing.)
        rig.Editor.SetVoxel(new VoxelPos(16, 50, 8), Stone);
        rig.Region.Settle();
        rig.Run(16, settle: false, boundaries: false);
        Assert.Equal(1, cuts.Held);
        Assert.Equal(1, rig.Director.CutsStarted);

        cuts.CompleteAll();
        rig.Run(32, settle: false, boundaries: false);
        Assert.Equal(1, rig.Director.CutsApplied);
        Assert.Equal(2, rig.Director.CutsStarted);
        Assert.Equal(1, cuts.Held);
    }

    [Fact]
    public void AnEventWithACutInFlightIsNotAtRest()
    {
        Rig rig = Slab(new VoxelPos(8, 50, 8), new VoxelPos(9, 50, 9));
        var cuts = new HeldCuts();
        rig.Director.CutRunner = cuts.Run;
        Fire(rig, Sky);
        rig.Run(0, settle: false, boundaries: false);
        cuts.CompleteAll();
        rig.Run(10_000, settle: false, boundaries: false);
        Assert.Equal(4, rig.Director.LandedVoxels);

        // Settled at rest, and the re-cut of the empty set is held on the worker: past the grace,
        // the event stays, because a cut in flight could still hold something.
        rig.Region.Settle();
        rig.Run(15_000, settle: false, boundaries: false);
        Assert.Equal(1, cuts.Held);
        Assert.Single(rig.Director.Events);

        cuts.CompleteAll();
        rig.Run(15_000 + CollapseDirector.DefaultGraceMillis + 100, settle: false, boundaries: false);
        Assert.Empty(rig.Director.Events);
    }

    [Fact]
    public void TheMinoGrowingWhileACutRunsIsCutOnceAtTheNewMino()
    {
        // A slab big enough to still be falling at 20 s, where the mino doubles.
        Rig rig = Slab(new VoxelPos(4, 50, 4), new VoxelPos(27, 50, 27));
        var cuts = new HeldCuts();
        rig.Director.CutRunner = cuts.Run;
        CollapseEvent ev = Fire(rig, Sky);
        for (long until = 0; until < 20_000; until += 16)
        {
            cuts.CompleteAll();
            rig.Run(until);
        }

        // At 20 s the mino grows: the cut that lands is swapped in, and the growth starts one cut
        // at the new mino — held on the worker.
        cuts.CompleteAll();
        rig.Run(20_000, settle: false, boundaries: false);
        Assert.Equal(1, ev.PlannedMino);
        Assert.Equal(2, ev.MinoFor(20_000));
        Assert.Equal(1, cuts.Held);
        long started = rig.Director.CutsStarted;

        // While it runs, the old queue at the old mino is not a reason for another.
        rig.Run(20_500, settle: false, boundaries: false);
        Assert.Equal(1, cuts.Held);
        Assert.Equal(started, rig.Director.CutsStarted);

        // It lands at the mino it was cut at, and nothing asks for it again.
        cuts.CompleteAll();
        rig.Run(20_532, settle: false, boundaries: false);
        Assert.Equal(2, ev.PlannedMino);
        Assert.Equal(0, cuts.Held);
        Assert.Equal(started, rig.Director.CutsStarted);
    }

    [Fact]
    public void ACutThatThrowsOnTheWorkerThrowsOnTheSimThreadWhenItLands()
    {
        Rig rig = Slab(new VoxelPos(8, 50, 8), new VoxelPos(9, 50, 9));
        rig.Director.CutRunner = _ => Task.FromException<CollapseCut>(new InvalidOperationException("the cutter's own refusal"));
        Fire(rig, Sky);
        rig.Director.Frame(0);
        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() => rig.Director.Frame(16));
        Assert.Equal("the cutter's own refusal", thrown.Message);
    }

    [Fact]
    public void ThePlugComesDownTheSameWithItsCutsOnRealThreads()
    {
        var world = new SupportTestWorld();
        world.FillBox(new ChunkPos(-2, -2, -2), new ChunkPos(3, 2, 3), Stone);
        foreach (ChunkPos bite in TwoByTwo)
        {
            world.Fill(bite, Air);
        }

        Rig rig = NewRig(world, TwoByTwo, scheduler: true);
        rig.Director.CutRunner = work => Task.Run(work);
        (long solidBefore, _) = world.Census();

        // Frames, and a real thread under each cut: the frame after a cut starts may or may not
        // see it landed, and the end state may not care.
        for (long until = 0; until < 60_000; until += 16)
        {
            rig.Run(until);
            if (rig.Director.CutsStarted > rig.Director.CutsApplied)
            {
                Thread.Sleep(1);   // give the worker a moment; the frame after may still miss it
            }
        }

        Assert.Equal(20, rig.Director.ReleasedVoxels);
        Assert.Equal(20, rig.Director.LandedVoxels);
        Assert.Equal(0, rig.Director.RescuedVoxels);
        Assert.Equal(20, world.Count(Rubble));
        Assert.Equal(0, rig.Region.SettledAtRiskVoxels);
        Assert.Empty(rig.Director.Events);
        Assert.Equal(solidBefore, world.Census().Solid);
        Assert.Equal(rig.Director.CutsStarted, rig.Director.CutsApplied);
    }

    // ---- Helpers ------------------------------------------------------------------------------

    private static List<VoxelPos> Rubbles(Rig rig)
    {
        var found = new List<VoxelPos>();
        for (int x = -32; x < 64; x++)
        for (int z = -32; z < 64; z++)
        for (int y = 0; y < 96; y++)
        {
            var v = new VoxelPos(x, y, z);
            if (rig.World.Material(v) == Rubble)
            {
                found.Add(v);
            }
        }

        return found;
    }
}
