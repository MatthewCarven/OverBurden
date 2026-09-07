using Overburden.Core.Fluid;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// The counted divisor (<see cref="SpreadRule.Counted"/>), brought back from M8a's rejection with
/// the wake it needs. Two things are asserted here and nowhere else: the arithmetic the rule was
/// asked for — half the surplus at a straight front, a pair level in one tick, the fixed rule
/// exactly against four dry sides — and that the automaton under it still stops with nothing
/// left to do, which is the property M8a's version lacked.
/// </summary>
/// <remarks>
/// <b>The oracle for the wake is <see cref="FluidSimulation.VerifyQuiescent"/>, and the fixtures
/// are chosen so that it can fail.</b> A neighbour gaining water is what shrinks a nearby cell's
/// divisor, and the cell it hands a move to is one that was asleep — so the shapes here are the
/// ones where most of a puddle is asleep while an edge still moves: a shallow spill on a wide
/// floor (M8a's own catch), pours landing beside resting water, and a floor with pillars in it.
/// The break-recipe that matters is dropping the destination's neighbourhood from
/// <c>FluidSimulation.Record</c>; the fuzz below fails on it, and the M8a floor does too.
/// </remarks>
public class SpreadRuleTests
{
    private const int Cap = FluidConstants.CellCapacityMillilitres;

    private static VoxelPos At(int x, int y, int z) => new(x, y, z);

    // ---------------------------------------------------------------- the arithmetic

    [Fact]
    public void TheCountedDivisorIsOnePartForTheCellAndOneForEachLowerSide()
    {
        for (int lower = 0; lower <= 4; lower++)
        {
            Assert.Equal(lower + 1, FluidRules.Divisor(SpreadRule.Counted, lower));
            Assert.Equal(FluidConstants.SpreadDivisor, FluidRules.Divisor(SpreadRule.Fixed, lower));
        }

        // Never more than the fixed divisor: the residue bound and the solve's step constant
        // both read FluidConstants.SpreadDivisor as the ceiling.
        Assert.Equal(FluidConstants.SpreadDivisor, FluidRules.Divisor(SpreadRule.Counted, 4));
    }

    [Fact]
    public void AgainstFourDrySidesTheTwoRulesAreTheSameRule()
    {
        // The plus shape from FluidSimulationTests.SpreadingIsEvenInEveryDirectionAtOnce, under
        // the counted rule: four lower sides make the count five, and 1,000 mL goes 200 each way.
        // A ball dropped in the open spreads no differently at its first tick.
        var open = new HashSet<VoxelPos>
        {
            At(0, 0, 0), At(1, 0, 0), At(-1, 0, 0), At(0, 0, 1), At(0, 0, -1),
        };
        var sim = new FluidSimulation(new PredicateTerrain(open.Contains), spread: SpreadRule.Counted);
        sim.Add(At(0, 0, 0), 1_000);

        sim.Step();

        foreach (VoxelPos cell in open)
        {
            Assert.Equal(200, sim.Grid.AmountAt(cell));
        }

        FluidAssert.Conserves(sim);
    }

    [Fact]
    public void AStraightFrontCarriesHalfTheSurplusInsteadOfAFifthAndAPairLevelsInOneTick()
    {
        // Two cells in a trench: one lower side, so the counted divisor is two. The fixed rule
        // gives 2,000 of 10,000 and then decays the difference by three fifths a tick — sixteen
        // or so ticks to rest, with a residue; the counted rule gives 5,000 and the pair is level.
        // This is the claim the play asked for, in one number each.
        static FluidSimulation Trench(SpreadRule rule)
        {
            var sim = new FluidSimulation(new BoxTerrain(0, 0, 0, 1, 0, 0), spread: rule);
            sim.Add(At(0, 0, 0), 10_000);
            return sim;
        }

        FluidSimulation fixedRule = Trench(SpreadRule.Fixed);
        fixedRule.Step();
        Assert.Equal(2_000, fixedRule.Grid.AmountAt(At(1, 0, 0)));

        FluidSimulation counted = Trench(SpreadRule.Counted);
        counted.Step();
        Assert.Equal(5_000, counted.Grid.AmountAt(At(1, 0, 0)));
        Assert.Equal(5_000, counted.Grid.AmountAt(At(0, 0, 0)));

        // The tick after finds nothing to do, and that is the whole settle: two ticks, one of
        // them the move. The fixed rule's is the sixteen M8a recorded, plus the quiet one.
        FluidSettleResult countedRest = FluidAssert.SettlesAndRests(counted);
        FluidSettleResult fixedRest = FluidAssert.SettlesAndRests(fixedRule);
        Assert.Equal(1, countedRest.Ticks);
        Assert.InRange(fixedRest.Ticks, 10, 30);
        Assert.Equal(5_000, fixedRule.Grid.AmountAt(At(0, 0, 0)) + fixedRule.Grid.AmountAt(At(1, 0, 0)) - 5_000);
        FluidAssert.Conserves(counted);
        FluidAssert.Conserves(fixedRule);
    }

    [Fact]
    public void AnEqualSideIsNotALowerOne()
    {
        // Three cells in a row, the first two full to the same height, the third empty. The
        // middle cell has one equal side and one lower side, so its divisor is two and the dry
        // cell gets half. Counting "not higher" instead of "lower" would make it three, and
        // 3,333 — the recipe this test is written against.
        var sim = new FluidSimulation(new BoxTerrain(0, 0, 0, 2, 0, 0), spread: SpreadRule.Counted);
        sim.Add(At(0, 0, 0), 10_000);
        sim.Add(At(1, 0, 0), 10_000);

        sim.Step();

        Assert.Equal(10_000, sim.Grid.AmountAt(At(0, 0, 0)));
        Assert.Equal(5_000, sim.Grid.AmountAt(At(1, 0, 0)));
        Assert.Equal(5_000, sim.Grid.AmountAt(At(2, 0, 0)));
    }

    [Fact]
    public void TheQuiescenceCheckSeesAMoveTheFixedRuleCannot()
    {
        // Four millilitres apart with one lower side: no move under a divisor of five, a move
        // of two under a divisor of two. Written straight into the grid so nothing is awake,
        // which is the state a missed wake would leave — so if the check under the counted
        // rule read the fixed rule, every wake recipe in this file would pass on broken code.
        static FluidSimulation Pair(SpreadRule rule)
        {
            var sim = new FluidSimulation(new BoxTerrain(0, 0, 0, 1, 0, 0), spread: rule);
            sim.Grid.Fill(At(0, 0, 0), 1_004);
            sim.Grid.Fill(At(1, 0, 0), 1_000);
            return sim;
        }

        Assert.True(Pair(SpreadRule.Fixed).VerifyQuiescent());
        Assert.False(Pair(SpreadRule.Counted).VerifyQuiescent());
    }

    // ---------------------------------------------------------------- the wake

    [Fact]
    public void TheCountedRuleRestsOnTheFloorThatCaughtItsFirstVersion()
    {
        // M8a's own catch: five litres on an 8x8 floor, where the variable divisor stopped four
        // cells short of level with everything asleep. Same floor, same litres, the rule with its
        // wake.
        var sim = new FluidSimulation(BoxTerrain.Cube(8), spread: SpreadRule.Counted);
        sim.Add(At(3, 7, 3), 5_000);

        FluidAssert.SettlesAndRests(sim);
        FluidAssert.Conserves(sim);
        Assert.Equal(5_000, sim.TotalMillilitres);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void PoursLandingBesideRestingWaterOnAnObstructedFloorStillComeToARealRest(int seed)
    {
        // The shape the defect needs: water asleep, and an edge still moving beside it. Pillars
        // make the count vary from cell to cell, and settling between rounds puts each new pour
        // down next to a resting puddle rather than into a moving one. VerifyQuiescent after
        // every round is the oracle; the recipe is dropping WakeNeighboursOf(to) from Record.
        var random = new Random(seed);
        var pillars = new HashSet<(int, int)>();
        for (int i = 0; i < 20; i++)
        {
            pillars.Add((random.Next(0, 12), random.Next(0, 12)));
        }

        var box = new BoxTerrain(0, 0, 0, 11, 3, 11);
        var terrain = new PredicateTerrain(v => box.CanHold(v) && !pillars.Contains((v.X, v.Z)));
        var sim = new FluidSimulation(terrain, spread: SpreadRule.Counted);

        long rejected = 0;
        int rounds = 0;
        for (int round = 0; round < 12; round++)
        {
            var at = At(random.Next(0, 12), 3, random.Next(0, 12));
            if (!terrain.CanHold(at))
            {
                continue;
            }

            rejected += sim.Add(at, random.Next(500, 3 * Cap));
            FluidAssert.SettlesAndRests(sim);
            rounds++;
        }

        FluidAssert.Conserves(sim, rejected);
        Assert.True(rounds >= 6, $"only {rounds} pours landed; the floor is mostly pillar");
        Assert.True(sim.TotalMillilitres > 0);
    }

    [Fact]
    public void ACellTurnedToRockUnderRestingWaterIsDisplacedAndItsNeighboursFindTheirNewMoves()
    {
        // Under the counted rule a side that stops being holdable leaves its neighbours' counts,
        // which shrinks their divisors and can hand them a move. Disturb wakes every wet neighbour
        // of the edit, which is the whole neighbourhood a divisor reads, so the rest after is real.
        var rock = new HashSet<VoxelPos>();
        var box = new BoxTerrain(0, 0, 0, 5, 2, 5);
        var terrain = new PredicateTerrain(v => box.CanHold(v) && !rock.Contains(v));
        var sim = new FluidSimulation(terrain, spread: SpreadRule.Counted);

        sim.Add(At(2, 2, 2), 2 * Cap);
        FluidAssert.SettlesAndRests(sim);

        VoxelPos wettest = sim.Grid.HeldCells().OrderByDescending(c => sim.Grid.AmountAt(c)).First();
        rock.Add(wettest);
        sim.Disturb(wettest);

        FluidAssert.SettlesAndRests(sim);
        FluidAssert.Conserves(sim);
        Assert.Equal(0, sim.Grid.AmountAt(wettest));
    }

    // ---------------------------------------------------------------- the rest of the machinery

    [Fact]
    public void TheSheetSolveLeavesARealRestUnderTheCountedRule()
    {
        // An unlevel sheet written asleep, one cell woken, the solve at the first boundary. The
        // solve's wake — fronts and the water above drawn-down cells — was argued complete for
        // the fixed rule; under the counted rule a levelled sheet's cells differ by at most one
        // millilitre, which is no move under any divisor of two or more, so the same wake holds.
        var sim = new FluidSimulation(BoxTerrain.Cube(8), spread: SpreadRule.Counted);
        for (int x = 0; x < 8; x++)
        {
            for (int z = 0; z < 8; z++)
            {
                sim.Grid.Fill(At(x, 0, z), 2_000 + (x * 700) + (z * 300));
            }
        }

        sim.Add(At(0, 0, 0), 1);
        var settle = new SheetSettle(sim, settleAfterTicks: 0, settleShare: 0);
        sim.Step();
        SheetLevelResult? pass = settle.AtBoundary();

        Assert.NotNull(pass);
        Assert.True(pass.Value.Levelled > 0, "the solve levelled nothing, so this proves nothing");
        FluidAssert.SettlesAndRests(sim);

        // Not FluidAssert.Conserves: the sheet went in through the grid, which the identity does
        // not count, so the check here is the grid's own total against what was written.
        long written = 1;
        for (int x = 0; x < 8; x++)
        {
            for (int z = 0; z < 8; z++)
            {
                written += 2_000 + (x * 700) + (z * 300);
            }
        }

        Assert.Equal(written, sim.TotalMillilitres);
    }

    [Fact]
    public void ATickRunInSlicesLandsTheSameWaterUnderTheCountedRule()
    {
        // The slicing is a cursor over the batch and the rule is inside the visit, so this is
        // belt and braces on the two being independent — and the one place a counted-rule tick
        // is compared cell for cell against itself.
        var box = new BoxTerrain(0, 0, 0, 9, 4, 9);
        var whole = new FluidSimulation(box, spread: SpreadRule.Counted);
        var sliced = new FluidSimulation(box, spread: SpreadRule.Counted);
        var random = new Random(7);

        for (int tick = 0; tick < 80; tick++)
        {
            if (tick % 9 == 0)
            {
                var at = At(random.Next(0, 10), 4, random.Next(0, 10));
                int millilitres = random.Next(1_000, Cap);
                whole.Add(at, millilitres);
                sliced.Add(at, millilitres);
            }

            FluidTickResult a = whole.Step();
            FluidTickResult? b;
            do
            {
                b = sliced.Resume(7);
            }
            while (b is null);

            Assert.Equal(a, b.Value);
            foreach (VoxelPos cell in whole.Grid.HeldCells())
            {
                Assert.Equal(whole.Grid.AmountAt(cell), sliced.Grid.AmountAt(cell));
            }

            Assert.Equal(whole.Grid.HeldCellCount, sliced.Grid.HeldCellCount);
        }

        Assert.True(whole.TotalMillilitres > 0);
    }

    [Fact]
    public void TheRuleIsFixedAtConstructionAndReported()
    {
        Assert.Equal(SpreadRule.Fixed, new FluidSimulation(BoxTerrain.Cube(2)).Spread);
        Assert.Equal(SpreadRule.Counted, new FluidSimulation(BoxTerrain.Cube(2), spread: SpreadRule.Counted).Spread);
    }
}
