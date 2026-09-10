using Overburden.Core.Fluid;
using Overburden.Core.Voxels;
using Overburden.Core.Worldgen;

namespace Overburden.Core.Tests;

/// <summary>
/// The automaton. Two oracles run through nearly every test here: volume is conserved with no
/// tolerance, and the resting state is really at rest.
/// </summary>
public class FluidSimulationTests
{
    private const int Cap = FluidConstants.CellCapacityMillilitres;

    /// <summary>
    /// The most two settled neighbours may differ by: one less than the divisor, because anything
    /// smaller floors to a transfer of zero. <b>It accumulates along a path</b> — see FluidRules.
    /// </summary>
    private const int Residue = FluidConstants.SpreadDivisor - 1;

    private static FluidSimulation InABox(int size = 8) =>
        new(BoxTerrain.Cube(size));

    /// <summary>Asserts the real invariant: no two adjacent cells differ by more than a residue.</summary>
    private static void AssertNeighboursAreLevel(
        FluidSimulation sim, int minX, int maxX, int y, int minZ, int maxZ)
    {
        for (int x = minX; x <= maxX; x++)
        {
            for (int z = minZ; z <= maxZ; z++)
            {
                int here = sim.Grid.AmountAt(new VoxelPos(x, y, z));
                if (x < maxX)
                {
                    int east = sim.Grid.AmountAt(new VoxelPos(x + 1, y, z));
                    Assert.True(
                        Math.Abs(here - east) <= Residue,
                        $"({x},{y},{z})={here} against ({x + 1},{y},{z})={east}");
                }

                if (z < maxZ)
                {
                    int south = sim.Grid.AmountAt(new VoxelPos(x, y, z + 1));
                    Assert.True(
                        Math.Abs(here - south) <= Residue,
                        $"({x},{y},{z})={here} against ({x},{y},{z + 1})={south}");
                }
            }
        }
    }

    // ---------------------------------------------------------------- falling and levelling

    [Fact]
    public void WaterFallsToTheFloor()
    {
        // A 1x1 shaft, so there is nowhere to spread and the answer is exact. In an open box the
        // same five litres becomes a puddle several cells across — see the test below.
        var sim = new FluidSimulation(new BoxTerrain(3, 0, 3, 3, 7, 3));
        sim.Add(new VoxelPos(3, 7, 3), 5_000);

        FluidAssert.SettlesAndRests(sim);

        Assert.Equal(5_000, sim.Grid.AmountAt(new VoxelPos(3, 0, 3)));
        Assert.Equal(0, sim.Grid.AmountAt(new VoxelPos(3, 7, 3)));
        FluidAssert.Conserves(sim);
    }

    [Fact]
    public void AShallowSpillSettlesAsAPuddleRatherThanAFilmAcrossTheWholeFloor()
    {
        // Two things at once. The film is what MinimumSpreadMillilitres exists to stop: without a
        // floor under the spreading rule, five litres reaches every one of the 64 floor cells at
        // 78 mL each and holds all of them awake for nothing.
        //
        // It is also the regression test for the wake defect this milestone opened with. A
        // spreading front over a wide floor is where a cell's move depended on how many of its
        // neighbours were lower, so a neighbour GAINING water handed a nearby cell a move with
        // nothing awake to take it — and the automaton stopped four cells short of level.
        var sim = InABox();
        sim.Add(new VoxelPos(3, 7, 3), 5_000);

        FluidAssert.SettlesAndRests(sim);
        FluidAssert.Conserves(sim);

        Assert.Equal(5_000, sim.TotalMillilitres);
        Assert.InRange(sim.Grid.HeldCellCount, 2, 32);
        foreach (VoxelPos cell in sim.Grid.HeldCells())
        {
            Assert.Equal(0, cell.Y);
        }
    }

    [Fact]
    public void AColumnFillsFromTheBottomUp()
    {
        var sim = new FluidSimulation(new BoxTerrain(0, 0, 0, 0, 7, 0));

        // Two and a half cells' worth, poured in from the top of the column.
        for (int y = 7; y >= 5; y--)
        {
            sim.Add(new VoxelPos(0, y, 0), y == 5 ? Cap / 2 : Cap);
        }

        FluidAssert.SettlesAndRests(sim);

        // A 1x1 shaft: nothing to spread into, so the answer is exact rather than within the
        // integer residue the sideways rule allows.
        Assert.Equal(Cap, sim.Grid.AmountAt(new VoxelPos(0, 0, 0)));
        Assert.Equal(Cap, sim.Grid.AmountAt(new VoxelPos(0, 1, 0)));
        Assert.Equal(Cap / 2, sim.Grid.AmountAt(new VoxelPos(0, 2, 0)));
        Assert.Equal(0, sim.Grid.AmountAt(new VoxelPos(0, 3, 0)));
        FluidAssert.Conserves(sim);
    }

    [Fact]
    public void ThreeCellsPouredDownOneColumnEndUpSpreadAcrossTheWholeFloor()
    {
        // A 5x5 tank. Everything enters through one column, so reaching a level floor is the
        // sideways rule doing its whole job rather than the water arriving pre-spread.
        var sim = new FluidSimulation(new BoxTerrain(0, 0, 0, 4, 9, 4));
        for (int y = 9; y >= 7; y--)
        {
            sim.Add(new VoxelPos(2, y, 2), Cap);
        }

        FluidAssert.SettlesAndRests(sim);
        FluidAssert.Conserves(sim);

        int min = int.MaxValue;
        int max = 0;
        for (int x = 0; x <= 4; x++)
        {
            for (int z = 0; z <= 4; z++)
            {
                int amount = sim.Grid.AmountAt(new VoxelPos(x, 0, z));
                min = Math.Min(min, amount);
                max = Math.Max(max, amount);
                Assert.Equal(0, sim.Grid.AmountAt(new VoxelPos(x, 1, z)));
            }
        }

        // 3 x 15,625 over 25 cells is 1,875 each.
        Assert.Equal(3L * Cap, sim.TotalMillilitres);

        // The bound is per ADJACENT PAIR, and it accumulates along a path — see FluidRules. Across
        // a 5x5 floor the two farthest cells are eight steps apart, so the surface may tilt by
        // eight residues even though no two neighbours differ by more than one. Asserting the
        // global figure instead is the mistake this comment exists to stop being made again.
        Assert.InRange(min, 1_875 - (8 * Residue), 1_875);
        Assert.InRange(max, 1_875, 1_875 + (8 * Residue));
        AssertNeighboursAreLevel(sim, 0, 4, 0, 0, 4);
    }

    [Fact]
    public void SpreadingIsEvenInEveryDirectionAtOnce()
    {
        // A plus shape one cell high with solid rock underneath, so the only move available is the
        // sideways one and the split can be read off in a single tick. Halving the difference with
        // each neighbour in turn would give 500, 250, 125, 62 and keep 63.
        var open = new HashSet<VoxelPos>
        {
            new(0, 0, 0), new(1, 0, 0), new(-1, 0, 0), new(0, 0, 1), new(0, 0, -1),
        };
        var sim = new FluidSimulation(new PredicateTerrain(open.Contains));
        sim.Add(new VoxelPos(0, 0, 0), 1_000);

        sim.Step();

        Assert.Equal(200, sim.Grid.AmountAt(new VoxelPos(0, 0, 0)));
        Assert.Equal(200, sim.Grid.AmountAt(new VoxelPos(1, 0, 0)));
        Assert.Equal(200, sim.Grid.AmountAt(new VoxelPos(-1, 0, 0)));
        Assert.Equal(200, sim.Grid.AmountAt(new VoxelPos(0, 0, 1)));
        Assert.Equal(200, sim.Grid.AmountAt(new VoxelPos(0, 0, -1)));
        FluidAssert.Conserves(sim);
    }

    // ---------------------------------------------------------------- what it deliberately cannot do

    [Fact]
    public void WaterDoesNotClimbIntoAConnectedShaftBecauseThereIsNoPressure()
    {
        // Two shafts joined only at the bottom. Real water would level between them; this water
        // fills the join and then piles up in the shaft it was poured into. DESIGN rules pressure
        // out, so this is the specified behaviour and it is the first thing that will look like a
        // bug — hence a test that states it rather than a comment that hopes.
        static bool Open(VoxelPos v) =>
            v.Z == 0
            && ((v.X == 0 && v.Y is >= 0 and <= 5)
                || (v.X == 2 && v.Y is >= 0 and <= 5)
                || (v.X == 1 && v.Y == 0));

        var sim = new FluidSimulation(new PredicateTerrain(Open));
        for (int y = 5; y >= 2; y--)
        {
            sim.Add(new VoxelPos(0, y, 0), Cap);
        }

        FluidAssert.SettlesAndRests(sim);
        FluidAssert.Conserves(sim);

        // The far shaft never rises above the join, which is the whole claim.
        Assert.Equal(0, sim.Grid.AmountAt(new VoxelPos(2, 1, 0)));
        Assert.Equal(Cap, sim.Grid.AmountAt(new VoxelPos(0, 1, 0)));

        // The join fills to within the accumulating residue: each cell of separation from the
        // source may sit one residue lower, so the far end is two of them short of the brim.
        Assert.Equal(Cap, sim.Grid.AmountAt(new VoxelPos(0, 0, 0)));
        Assert.InRange(sim.Grid.AmountAt(new VoxelPos(1, 0, 0)), Cap - Residue, Cap);
        Assert.InRange(sim.Grid.AmountAt(new VoxelPos(2, 0, 0)), Cap - (2 * Residue), Cap);
    }

    [Fact]
    public void WaterStopsAtTheEdgeOfTheLoadedWorld()
    {
        // Everything outside the box is solid, which is what the real adapter answers for a chunk
        // that is not loaded. Under-filling is the safe failure; leaking into an absent chunk
        // would put water inside rock the moment that chunk arrived.
        var sim = InABox(4);
        sim.Add(new VoxelPos(0, 3, 0), Cap);
        sim.Add(new VoxelPos(3, 3, 3), Cap);

        FluidAssert.SettlesAndRests(sim);
        FluidAssert.Conserves(sim);

        Assert.Equal(2L * Cap, sim.TotalMillilitres);
        foreach (VoxelPos cell in sim.Grid.HeldCells())
        {
            Assert.InRange(cell.X, 0, 3);
            Assert.InRange(cell.Y, 0, 3);
            Assert.InRange(cell.Z, 0, 3);
        }
    }

    [Fact]
    public void AddingToRockTakesNothingAndCountsNothing()
    {
        var sim = InABox(4);

        Assert.Equal(500, sim.Add(new VoxelPos(9, 9, 9), 500));
        Assert.Equal(0, sim.AddedMillilitres);
        Assert.Equal(0, sim.TotalMillilitres);
        Assert.Equal(0, sim.ActiveCellCount);
    }

    [Fact]
    public void WhatWillNotFitIsHandedBackRatherThanCounted()
    {
        var sim = InABox(4);

        Assert.Equal(500, sim.Add(new VoxelPos(0, 0, 0), Cap + 500));
        Assert.Equal(Cap, sim.AddedMillilitres);
        FluidAssert.Conserves(sim);
    }

    // ---------------------------------------------------------------- terrain moving under water

    [Fact]
    public void WaterSqueezedOutOfANewBlockGoesToANeighbour()
    {
        var filled = new HashSet<VoxelPos>();
        var sim = new FluidSimulation(new PredicateTerrain(
            v => v.Y is >= 0 and <= 3 && v.X is >= 0 and <= 3 && v.Z == 0 && !filled.Contains(v)));

        sim.Add(new VoxelPos(1, 0, 0), 4_000);
        FluidAssert.SettlesAndRests(sim);

        filled.Add(new VoxelPos(1, 0, 0));
        sim.Disturb(new VoxelPos(1, 0, 0));
        FluidAssert.SettlesAndRests(sim);

        Assert.Equal(0, sim.Grid.AmountAt(new VoxelPos(1, 0, 0)));
        Assert.Equal(0, sim.DisplacedMillilitres);
        Assert.Equal(4_000, sim.TotalMillilitres);
        FluidAssert.Conserves(sim);
    }

    [Fact]
    public void WaterWithNowhereToGoIsDestroyedAndSaysSo()
    {
        // A sealed one-cell pocket that then becomes rock. The water is genuinely gone, and the
        // number is reported rather than absorbed into the conservation check.
        var filled = new HashSet<VoxelPos>();
        var sim = new FluidSimulation(new PredicateTerrain(
            v => v == new VoxelPos(0, 0, 0) && !filled.Contains(v)));

        sim.Add(new VoxelPos(0, 0, 0), 3_000);
        FluidAssert.SettlesAndRests(sim);

        filled.Add(new VoxelPos(0, 0, 0));
        sim.Disturb(new VoxelPos(0, 0, 0));
        FluidAssert.SettlesAndRests(sim);

        Assert.Equal(3_000, sim.DisplacedMillilitres);
        Assert.Equal(0, sim.TotalMillilitres);
        FluidAssert.Conserves(sim);
    }

    [Fact]
    public void DiggingBesideRestingWaterWakesItAndDiggingDryRockWakesNothing()
    {
        // Water sealed in a one-cell pocket, with rock beside it that is about to be dug. The
        // opened voxel is empty, so waking it would find nothing; the water next to it is what has
        // to move, and it is asleep.
        var open = new HashSet<VoxelPos> { new(0, 0, 0) };
        var sim = new FluidSimulation(new PredicateTerrain(open.Contains));

        sim.Add(new VoxelPos(0, 0, 0), 4_000);
        FluidAssert.SettlesAndRests(sim);
        Assert.Equal(0, sim.ActiveCellCount);

        // A dig far from any water queues nothing at all.
        open.Add(new VoxelPos(10, 0, 0));
        sim.Disturb(new VoxelPos(10, 0, 0));
        Assert.Equal(0, sim.ActiveCellCount);

        // A dig beside the pocket wakes the water in it, and the water then spreads in.
        open.Add(new VoxelPos(1, 0, 0));
        sim.Disturb(new VoxelPos(1, 0, 0));
        Assert.True(sim.ActiveCellCount > 0, "the resting water beside the dig should have been woken");
        FluidAssert.SettlesAndRests(sim);

        Assert.True(sim.Grid.AmountAt(new VoxelPos(1, 0, 0)) > 0, "water should have spread into the opened voxel");
        Assert.Equal(4_000, sim.TotalMillilitres);
        FluidAssert.Conserves(sim);
    }

    [Fact]
    public void DisturbingADryWorldQueuesNothing()
    {
        // The case every edit is in until the first flood: the grid holds nothing, so an edit has
        // nothing to wake, and the check has to cost less than a lookup per voxel because a chunk
        // bite makes 32,768 of them.
        var sim = new FluidSimulation(new PredicateTerrain(_ => true));

        for (int x = 0; x < 32; x++)
        {
            sim.Disturb(new VoxelPos(x, 0, 0));
        }

        Assert.Equal(0, sim.ActiveCellCount);
        Assert.Equal(0, sim.Grid.PanelCount);
    }

    // ---------------------------------------------------------------- the active set

    [Fact]
    public void NothingIsVisitedOnceEverythingHasSettled()
    {
        var sim = InABox();
        sim.Add(new VoxelPos(3, 7, 3), 5_000);
        FluidAssert.SettlesAndRests(sim);

        FluidTickResult tick = sim.Step();

        Assert.Equal(0, tick.CellsVisited);
        Assert.Equal(0, tick.ActiveAfter);
    }

    [Fact]
    public void AFreshSimulationHasNothingToDo()
    {
        var sim = InABox();
        FluidTickResult tick = sim.Step();

        Assert.Equal(0, tick.CellsVisited);
        Assert.Equal(0, sim.PeakActiveCells);
    }

    [Fact]
    public void TheQuiescenceCheckIsCapableOfFailing()
    {
        // Water written straight into the grid, bypassing Add and therefore bypassing the wake.
        // If this passed, every SettlesAndRests in the file would be asserting nothing at all —
        // a check that cannot return false is not a check.
        var sim = InABox();
        sim.Grid.Fill(new VoxelPos(3, 7, 3), 5_000);

        Assert.False(sim.VerifyQuiescent());
        Assert.Equal(0, sim.ActiveCellCount);
    }

    [Fact]
    public void TheQuiescenceCheckSeesWaterPerchedOverAnEmptyCellWithNowhereSidewaysToGo()
    {
        // The arm above fails on the sideways clause as well as the vertical one, so deleting the
        // vertical clause left the whole suite green. A 1x1 shaft has no sideways clause to fall
        // back on, which makes this the only test in the file that can see the difference.
        var sim = new FluidSimulation(new BoxTerrain(0, 0, 0, 0, 5, 0));
        sim.Grid.Fill(new VoxelPos(0, 4, 0), 5_000);

        Assert.False(sim.VerifyQuiescent());
    }

    [Fact]
    public void TheQuiescenceCheckSeesWaterInsideRock()
    {
        // Displace always drains whatever it cannot push out, so no run of the automaton can leave
        // water inside rock — which made this clause unreachable and left it uncovered. Writing
        // through the grid is how the clause gets exercised at all, and it is worth keeping
        // because the seam M8b builds hands water to cells whose terrain it did not check.
        var sim = new FluidSimulation(new PredicateTerrain(_ => false));
        sim.Grid.Fill(new VoxelPos(0, 0, 0), 5_000);

        Assert.False(sim.VerifyQuiescent());
    }

    [Fact]
    public void TheSameWaterPouredTheSameWayLandsInTheSamePlace()
    {
        static FluidSimulation Run()
        {
            var sim = new FluidSimulation(new BoxTerrain(0, 0, 0, 6, 9, 6));
            for (int y = 9; y >= 6; y--)
            {
                sim.Add(new VoxelPos(4, y, 1), Cap);
            }

            sim.Settle();
            return sim;
        }

        FluidSimulation a = Run();
        FluidSimulation b = Run();

        List<VoxelPos> cells = [.. a.Grid.HeldCells()];
        Assert.Equal(cells.Count, b.Grid.HeldCellCount);
        foreach (VoxelPos cell in cells)
        {
            Assert.Equal(a.Grid.AmountAt(cell), b.Grid.AmountAt(cell));
        }

        Assert.Equal(a.Ticks, b.Ticks);
        Assert.Equal(a.CellVisits, b.CellVisits);
    }

    // ---------------------------------------------------------------- the slice: a tick resumable across frames

    /// <summary>
    /// A box with enough water falling in it that a tick's batch is several hundred cells: a
    /// 16 × 16 sheet of full cells dropped high in a 24-cell box, two ticks in. The first version
    /// was a 4 × 4 sheet in an 8-cell box and its batch was 64 — exactly one granule, which made
    /// the deadline tests unreachable.
    /// </summary>
    private static FluidSimulation WithATickWorthSlicing(Func<long>? nowTicks = null)
    {
        var sim = new FluidSimulation(BoxTerrain.Cube(24), nowTicks);
        for (int x = 4; x <= 19; x++)
        {
            for (int z = 4; z <= 19; z++)
            {
                sim.Add(new VoxelPos(x, 20, z), Cap);
            }
        }

        // Two whole ticks so the batch is the falling sheet rather than the seeded one.
        sim.Step();
        sim.Step();
        Assert.True(
            sim.ActiveCellCount > 4 * FluidSimulation.VisitsPerDeadlineCheck,
            $"the fixture's batch is only {sim.ActiveCellCount} cells");
        return sim;
    }

    /// <summary>
    /// Slices a tick to completion in pieces of <paramref name="sliceVisits"/>, and returns how many
    /// slices it took.
    /// </summary>
    /// <remarks>
    /// <b>Bounded, because the first battery hung instead of failing.</b> A cursor that resets on
    /// every slice never reaches the end of its batch, and a <c>do … while (null)</c> loop spins
    /// forever — which the M3a lesson says reads as infrastructure trouble rather than as a caught
    /// recipe. A tick cannot need more slices than it has cells plus one, so past that the loop
    /// is the slice failing to advance and the test says so.
    /// </remarks>
    private static (FluidTickResult Result, int Slices) SliceToCompletion(FluidSimulation sim, int sliceVisits)
    {
        int bound = sim.ActiveCellCount + 2;
        int slices = 0;
        FluidTickResult? done;
        do
        {
            done = sim.Resume(sliceVisits);
            slices++;
            Assert.True(slices <= bound, $"{slices} slices of {sliceVisits} and the tick has not ended");
        }
        while (done is null);

        return (done.Value, slices);
    }

    [Fact]
    public void ASliceOfOneVisitsExactlyOneCell()
    {
        FluidSimulation sim = WithATickWorthSlicing();
        int batch = sim.ActiveCellCount;
        Assert.True(batch > 8, $"the fixture's batch is only {batch} cells");

        long visitsBefore = sim.CellVisits;
        long ticksBefore = sim.Ticks;

        Assert.Null(sim.Resume(1));

        Assert.True(sim.TickInProgress);
        Assert.Equal(batch - 1, sim.RemainingInTick);
        Assert.Equal(visitsBefore + 1, sim.CellVisits);
        Assert.Equal(ticksBefore, sim.Ticks);
    }

    [Fact]
    public void TicksCountCompletedTicksNotSlices()
    {
        FluidSimulation sim = WithATickWorthSlicing();
        int batch = sim.ActiveCellCount;
        long ticksBefore = sim.Ticks;

        (FluidTickResult done, int slices) = SliceToCompletion(sim, 3);

        Assert.True(slices > 1, "the batch fitted in one slice, so nothing was sliced");
        Assert.Equal(ticksBefore + 1, sim.Ticks);
        Assert.Equal(batch, done.CellsVisited);
        Assert.False(sim.TickInProgress);
        Assert.Equal(0, sim.RemainingInTick);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(64)]
    [InlineData(1_000)]
    public void ATickRunInSlicesLandsTheSameWaterInTheSameCellsAsATickRunWhole(int sliceVisits)
    {
        // The property the per-frame budget rests on. Both simulations see the same terrain and
        // the same pours, in lockstep; one runs each tick whole and the other in slices of the
        // given size. After every tick the grids must agree cell for cell, and so must the tick's
        // own figures — a cursor that reset, a batch swapped mid-tick, or a queue cleared per slice
        // would each show up here as a cell holding a different number.
        var strata = new StrataTable([new StrataLayer(1, 1), new StrataLayer(2, 3)], 3);
        var generator = new WorldGenerator(
            new WorldGenSettings
            {
                Seed = 0xC0FFEEUL,
                SeaLevelVoxels = 0,
                SurfaceAmplitudeVoxels = 48.0f,
                SurfaceFrequency = 0.004f,
                SurfaceOctaves = 4,
                CavesEnabled = true,
                CaveLatticeStep = 4,
            },
            strata);
        int surface = generator.SurfaceHeightAt(0, 0);
        var terrain = new GeneratedTerrain(generator, -16, surface - 24, -16, 15, surface + 7, 15);

        var whole = new FluidSimulation(terrain);
        var sliced = new FluidSimulation(terrain);

        var random = new Random(0xC0FFEE);
        for (int drop = 0; drop < 24; drop++)
        {
            VoxelPos? top = terrain.TopOpening(random.Next(-16, 16), random.Next(-16, 16));
            if (top is null)
            {
                continue;
            }

            whole.Add(top.Value, Cap);
            sliced.Add(top.Value, Cap);
        }

        Assert.True(whole.AddedMillilitres > 0, "no water was ever poured");

        int ticks = 0;
        int slicedTicks = 0;
        while (whole.ActiveCellCount > 0 && ticks < 400)
        {
            FluidTickResult expected = whole.Step();
            ticks++;

            (FluidTickResult actual, int slices) = SliceToCompletion(sliced, sliceVisits);
            if (slices > 1)
            {
                slicedTicks++;
            }

            Assert.Equal(expected, actual);
            Assert.Equal(whole.Ticks, sliced.Ticks);
            Assert.Equal(whole.CellVisits, sliced.CellVisits);
            Assert.Equal(whole.ActiveCellCount, sliced.ActiveCellCount);
            AssertSameWater(whole, sliced);
        }

        Assert.True(ticks > 10, $"only {ticks} tick(s) — the fixture settled before it could slice");
        if (sliceVisits < 64)
        {
            Assert.True(slicedTicks > 0, "no tick was ever split, so nothing was compared");
        }
    }

    private static void AssertSameWater(FluidSimulation expected, FluidSimulation actual)
    {
        Assert.Equal(expected.TotalMillilitres, actual.TotalMillilitres);
        Assert.Equal(expected.Grid.HeldCellCount, actual.Grid.HeldCellCount);
        foreach (VoxelPos cell in expected.Grid.HeldCells())
        {
            Assert.True(
                expected.Grid.AmountAt(cell) == actual.Grid.AmountAt(cell),
                $"{cell}: whole {expected.Grid.AmountAt(cell)} against sliced {actual.Grid.AmountAt(cell)}");
        }
    }

    [Fact]
    public void StepFinishesTheTickInProgressRatherThanStartingANewOne()
    {
        FluidSimulation sim = WithATickWorthSlicing();
        int batch = sim.ActiveCellCount;
        long ticksBefore = sim.Ticks;

        Assert.Null(sim.Resume(2));
        FluidTickResult result = sim.Step();

        // One tick, and its figure is the whole batch's — the two cells the slice took included.
        Assert.Equal(ticksBefore + 1, sim.Ticks);
        Assert.Equal(batch, result.CellsVisited);
    }

    [Fact]
    public void SettleFinishesATickInProgressAndTheRestIsReal()
    {
        FluidSimulation sim = WithATickWorthSlicing();
        Assert.Null(sim.Resume(2));
        Assert.True(sim.TickInProgress);

        FluidSettleResult result = FluidAssert.SettlesAndRests(sim);
        FluidAssert.Conserves(sim);
        Assert.True(result.Ticks > 0);
    }

    [Fact]
    public void SettleFinishesATickInProgressEvenWhenItsFirstSliceWokeNothing()
    {
        // The battery's recipe F — Settle looping on the next tick's queue alone — passed the
        // test above, because a falling sheet's first sliced cells always wake something, so the
        // queue was never empty when it mattered. The shape that separates the arms is a body at
        // rest whose disturbed cell has no move: visit it, and the queue for the next tick stays
        // empty while the batch still has cells. A Settle that looked only at the queue would
        // return at once with the tick half done.
        var sim = new FluidSimulation(new BoxTerrain(0, 0, 0, 3, 3, 3));
        for (int x = 0; x <= 3; x++)
        {
            for (int z = 0; z <= 3; z++)
            {
                sim.Add(new VoxelPos(x, 2, z), Cap);
            }
        }

        FluidAssert.SettlesAndRests(sim);

        sim.Disturb(new VoxelPos(1, 0, 1));
        int batch = sim.ActiveCellCount;
        Assert.True(batch > 1, $"the disturbance woke only {batch} cell(s)");

        Assert.Null(sim.Resume(1));
        Assert.Equal(batch - 1, sim.ActiveCellCount);

        FluidSettleResult result = sim.Settle();
        Assert.True(result.Settled);
        Assert.Equal(1, result.Ticks);
        Assert.False(sim.TickInProgress);
        Assert.True(sim.VerifyQuiescent());
    }

    [Fact]
    public void ATicksFiguresAreItsOwnAndNotARunningTotal()
    {
        // Recipe N — the tick's moved counters never reset — was invisible to the sliced-against-
        // whole comparison because both arms share the mutation, and nothing in the game reads
        // MillilitresMoved. So the figure is pinned directly. One full cell dropped at the top of
        // a column falls two cells in its first tick (the cell below it is woken into the same
        // batch and visited after it) and one cell in each tick after that, so the second and
        // third ticks each move exactly one cell's worth, and the four falls sum to four cells.
        var sim = new FluidSimulation(new BoxTerrain(0, 0, 0, 0, 5, 0));
        sim.Add(new VoxelPos(0, 4, 0), Cap);

        FluidTickResult first = sim.Step();
        FluidTickResult second = sim.Step();
        FluidTickResult third = sim.Step();

        Assert.Equal(1, second.CellsMoved);
        Assert.Equal(Cap, second.MillilitresMoved);
        Assert.Equal(1, third.CellsMoved);
        Assert.Equal(Cap, third.MillilitresMoved);
        Assert.Equal(4L * Cap, first.MillilitresMoved + second.MillilitresMoved + third.MillilitresMoved);
        Assert.Equal(Cap, sim.Grid.AmountAt(new VoxelPos(0, 0, 0)));
    }

    [Fact]
    public void TheQuiescenceCheckRefusesToAnswerMidTick()
    {
        FluidSimulation sim = WithATickWorthSlicing();
        Assert.Null(sim.Resume(1));

        Assert.Throws<InvalidOperationException>(() => sim.VerifyQuiescent());

        // And answers again at the boundary.
        sim.Step();
        _ = sim.VerifyQuiescent();
    }

    [Fact]
    public void ActiveCellCountCountsTheUnvisitedHalfOfATickInProgress()
    {
        FluidSimulation sim = WithATickWorthSlicing();
        int batch = sim.ActiveCellCount;

        Assert.Null(sim.Resume(1));

        // The one visited cell may have woken a few for the next tick, so the count cannot fall
        // by more than one — and a count that reported only the next tick's queue would have
        // fallen by nearly the whole batch.
        Assert.True(
            sim.ActiveCellCount >= batch - 1,
            $"{batch} awake before the slice, {sim.ActiveCellCount} after visiting one cell");
        Assert.Equal(batch - 1, sim.RemainingInTick);
    }

    [Fact]
    public void ADeadlineAlreadyPastStillVisitsOneGranule()
    {
        // The light step's rule: checked after the work, never before, so a budget of zero makes
        // progress and a tick dearer than the whole budget still ends. A clock that is always past
        // any deadline is the sharpest form of that.
        FluidSimulation sim = WithATickWorthSlicing(nowTicks: () => long.MaxValue);
        int batch = sim.ActiveCellCount;
        Assert.True(batch > FluidSimulation.VisitsPerDeadlineCheck, $"batch of {batch} fits in one granule");

        long visitsBefore = sim.CellVisits;
        Assert.Null(sim.Step(deadlineTicks: 0));

        Assert.Equal(visitsBefore + FluidSimulation.VisitsPerDeadlineCheck, sim.CellVisits);
        Assert.Equal(batch - FluidSimulation.VisitsPerDeadlineCheck, sim.RemainingInTick);
    }

    [Fact]
    public void ADeadlineNeverReachedCompletesTheTickInOneCall()
    {
        // A counting clock with a deadline further away than the tick can possibly need — a tick
        // reads the clock once per granule, and a granule is at least one cell — rather than a
        // clock frozen at zero. The frozen clock was the first version, and against a slice that
        // stops advancing (the battery's "cursor reset on every slice") Step(deadline) is then an
        // infinite loop in the subject, which no bound in the test can reach. A clock that moves
        // makes the same claim and turns that hang into a failure.
        long now = 0;
        FluidSimulation sim = WithATickWorthSlicing(nowTicks: () => ++now);
        int batch = sim.ActiveCellCount;

        FluidTickResult? done = sim.Step(deadlineTicks: batch + 2);

        Assert.NotNull(done);
        Assert.Equal(batch, done.Value.CellsVisited);
        Assert.False(sim.TickInProgress);
        Assert.True(now < batch, $"the tick read the clock {now} times for {batch} cells");
    }

    [Fact]
    public void ADeadlineIsCheckedOncePerGranule()
    {
        // A clock that advances one tick per read against a deadline of three reads: exactly
        // three granules, then it stops — which pins the granule as the unit of overshoot.
        long now = 0;
        FluidSimulation sim = WithATickWorthSlicing(nowTicks: () => ++now);
        int batch = sim.ActiveCellCount;
        Assert.True(batch > 4 * FluidSimulation.VisitsPerDeadlineCheck, $"batch of {batch} is too small");

        long visitsBefore = sim.CellVisits;
        Assert.Null(sim.Step(deadlineTicks: 3));

        Assert.Equal(visitsBefore + (3 * FluidSimulation.VisitsPerDeadlineCheck), sim.CellVisits);
    }

    [Fact]
    public void APourOrATakeMidTickIsRefusedAndTheBoundaryTakesBoth()
    {
        // Slice 3a reverses the rule two earlier tests pinned: water could be added or removed
        // between slices, and every mutator woke into the next tick's set. Now the grid is the
        // tick's until it completes — Add and Remove return how much moved, so they cannot be
        // deferred and refuse instead; the boundary takes both, with nothing lost.
        FluidSimulation sim = WithATickWorthSlicing();
        Assert.Null(sim.Resume(3));
        Assert.True(sim.GridOwnedByTick);

        var corner = new VoxelPos(0, 23, 0);
        Assert.Throws<InvalidOperationException>(() => sim.Add(corner, Cap));
        Assert.Throws<InvalidOperationException>(() => sim.Remove(new VoxelPos(8, 20, 8), Cap));
        Assert.Throws<InvalidOperationException>(() => sim.Reclaim(corner.Chunk));
        Assert.Throws<InvalidOperationException>(() => sim.TakeEvicted([]));
        Assert.Equal(0, sim.Grid.AmountAt(corner));
        Assert.Equal(0, sim.DeferredCount);

        // Finishing the tick is the boundary: the same calls land, and the water rests.
        sim.Step();
        Assert.False(sim.GridOwnedByTick);
        Assert.Equal(0, sim.Add(corner, Cap));
        Assert.Equal(0, sim.Add(new VoxelPos(8, 20, 8), Cap / 2));

        FluidAssert.SettlesAndRests(sim);
        FluidAssert.Conserves(sim);
        Assert.Equal(0, sim.Grid.AmountAt(corner));
    }

    [Fact]
    public void ADisturbanceMidTickIsDeferredAndLandsWhenTheTickCompletes()
    {
        // Two layers at rest; wake one corner and slice into that tick. A disturbance at the far
        // corner mid-tick wakes nothing yet — the automaton's picture of the terrain is the one it
        // began the tick with — and lands, counted, the instant the tick completes.
        var sim = new FluidSimulation(new BoxTerrain(0, 0, 0, 3, 5, 3));
        for (int x = 0; x <= 3; x++)
        {
            for (int z = 0; z <= 3; z++)
            {
                sim.Add(new VoxelPos(x, 4, z), Cap);
                sim.Add(new VoxelPos(x, 5, z), Cap);
            }
        }

        FluidAssert.SettlesAndRests(sim);

        sim.Disturb(new VoxelPos(0, 0, 0));
        Assert.True(sim.ActiveCellCount > 1);
        Assert.Null(sim.Resume(1));
        Assert.True(sim.GridOwnedByTick);

        long disturbedBefore = sim.DisturbedCells;
        sim.Disturb(new VoxelPos(3, 0, 3));
        Assert.Equal(1, sim.DeferredCount);
        Assert.Equal(1L, sim.Deferrals);
        Assert.Equal(disturbedBefore, sim.DisturbedCells);

        // The far corner and its holding neighbours — above and two sideways — wake at completion.
        sim.Step();
        Assert.Equal(0, sim.DeferredCount);
        Assert.Equal(disturbedBefore + 4, sim.DisturbedCells);

        FluidAssert.SettlesAndRests(sim);
        FluidAssert.Conserves(sim);
        Assert.Equal(32L * Cap, sim.TotalMillilitres);
    }

    [Fact]
    public void RemovingWaterAtTheBoundaryAfterASlicedTickWakesWhatWasAboveIt()
    {
        // Two full layers at rest on a 4 x 4 floor. Wake the floor by disturbing it, slice into
        // that tick, finish it, then drain a floor cell at that boundary: the cell above it has to
        // notice and fall, so the drained cell is full again at rest. The pre-slice-3a version
        // drained mid-tick; the wake it asserts is the same.
        var sim = new FluidSimulation(new BoxTerrain(0, 0, 0, 3, 5, 3));
        for (int x = 0; x <= 3; x++)
        {
            for (int z = 0; z <= 3; z++)
            {
                sim.Add(new VoxelPos(x, 4, z), Cap);
                sim.Add(new VoxelPos(x, 5, z), Cap);
            }
        }

        FluidAssert.SettlesAndRests(sim);

        // The upper layer rests a residue or two under full — FluidRules' "12 mL left standing
        // one level above" — so the assertion is that it holds a layer, not that it is full.
        int aboveBefore = sim.Grid.AmountAt(new VoxelPos(2, 1, 2));
        Assert.True(aboveBefore > Cap / 2, $"the upper layer holds only {aboveBefore} mL");

        sim.Disturb(new VoxelPos(1, 0, 1));
        Assert.True(sim.ActiveCellCount > 1);
        Assert.Null(sim.Resume(1));
        sim.Step();

        Assert.Equal(Cap, sim.Remove(new VoxelPos(2, 0, 2), Cap));

        FluidAssert.SettlesAndRests(sim);
        FluidAssert.Conserves(sim);
        Assert.Equal(31L * Cap, sim.TotalMillilitres);

        // The cell above fell in and the floor neighbours topped it up to within a residue: a
        // wake lost at the boundary would have left it empty under a full cell, which is
        // also what SettlesAndRests' quiescence check would have refused.
        int refilled = sim.Grid.AmountAt(new VoxelPos(2, 0, 2));
        Assert.True(refilled >= Cap - Residue, $"the drained cell refilled only to {refilled} mL");
    }

    // ---------------------------------------------------------------- the fuzz

    [Theory]
    [InlineData(0x5EEDUL, SpreadRule.Fixed)]
    [InlineData(0xA11CEUL, SpreadRule.Fixed)]
    [InlineData(0xBEEFUL, SpreadRule.Fixed)]
    [InlineData(0xC0FFEEUL, SpreadRule.Fixed)]
    [InlineData(1UL, SpreadRule.Fixed)]
    [InlineData(0x5EEDUL, SpreadRule.Counted)]
    [InlineData(0xA11CEUL, SpreadRule.Counted)]
    [InlineData(0xBEEFUL, SpreadRule.Counted)]
    [InlineData(0xC0FFEEUL, SpreadRule.Counted)]
    [InlineData(1UL, SpreadRule.Counted)]
    public void OnRealTerrainItConservesAndItStops(ulong seed, SpreadRule spread)
    {
        // Irregular terrain from the real generator rather than a tank. A hand-built basin makes
        // the automaton's easy case its only case, and this project has been caught by that
        // fixture five times.
        var strata = new StrataTable([new StrataLayer(1, 1), new StrataLayer(2, 3)], 3);
        var generator = new WorldGenerator(
            new WorldGenSettings
            {
                Seed = seed,
                SeaLevelVoxels = 0,
                SurfaceAmplitudeVoxels = 48.0f,
                SurfaceFrequency = 0.004f,
                SurfaceOctaves = 4,
                CavesEnabled = true,
                CaveLatticeStep = 4,
            },
            strata);

        int surface = generator.SurfaceHeightAt(0, 0);
        var terrain = new GeneratedTerrain(
            generator, -16, surface - 24, -16, 15, surface + 7, 15);
        var sim = new FluidSimulation(terrain, spread: spread);

        var random = new Random((int)(seed & 0x7FFFFFFF));
        long rejected = 0;
        for (int drop = 0; drop < 24; drop++)
        {
            int x = random.Next(-16, 16);
            int z = random.Next(-16, 16);
            VoxelPos? top = terrain.TopOpening(x, z);
            if (top is null)
            {
                continue;
            }

            rejected += sim.Add(top.Value, Cap);
        }

        FluidSettleResult result = FluidAssert.SettlesAndRests(sim);
        FluidAssert.Conserves(sim, rejected);

        // A run that poured nothing proves nothing — the terrain could have been solid throughout.
        Assert.True(sim.AddedMillilitres > 0, "no water was ever poured");
        Assert.True(result.Ticks > 0, "nothing ever moved");
    }

    [Fact]
    public void RemovingWaterWakesWhatWasResting()
    {
        // The seam M8b builds runs this way round: Tier A takes litres out of a cell, and the
        // water above it has to notice. Nothing else in the file exercises a removal mid-flow.
        var sim = new FluidSimulation(new BoxTerrain(0, 0, 0, 0, 5, 0));
        sim.Add(new VoxelPos(0, 1, 0), Cap);
        sim.Add(new VoxelPos(0, 2, 0), Cap);
        FluidAssert.SettlesAndRests(sim);

        Assert.Equal(Cap, sim.Grid.AmountAt(new VoxelPos(0, 0, 0)));

        Assert.Equal(5_000, sim.Remove(new VoxelPos(0, 0, 0), 5_000));
        FluidAssert.SettlesAndRests(sim);

        Assert.Equal(Cap, sim.Grid.AmountAt(new VoxelPos(0, 0, 0)));
        Assert.Equal(Cap - 5_000, sim.Grid.AmountAt(new VoxelPos(0, 1, 0)));
        FluidAssert.Conserves(sim);
    }
}
