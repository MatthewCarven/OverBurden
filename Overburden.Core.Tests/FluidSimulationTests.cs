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

    // ---------------------------------------------------------------- the fuzz

    [Theory]
    [InlineData(0x5EEDUL)]
    [InlineData(0xA11CEUL)]
    [InlineData(0xBEEFUL)]
    [InlineData(0xC0FFEEUL)]
    [InlineData(1UL)]
    public void OnRealTerrainItConservesAndItStops(ulong seed)
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
        var sim = new FluidSimulation(terrain);

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
