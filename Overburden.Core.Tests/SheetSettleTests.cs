using Overburden.Core.Fluid;
using Overburden.Core.Voxels;
using Overburden.Core.Worldgen;

namespace Overburden.Core.Tests;

/// <summary>
/// M8b-3b: the sheet solve and its trigger. The oracles are the automaton's own — a settled sheet
/// must be quiescent by <see cref="FluidRules"/>, and <see cref="FluidSimulation.VerifyQuiescent"/>
/// after the tick that follows a solve is what says the solve woke everything it gave a move.
/// </summary>
/// <remarks>
/// <b>Most fixtures put water down through <see cref="FluidGrid.Fill"/> rather than
/// <see cref="FluidSimulation.Add"/>, on purpose.</b> Add wakes the cell and its neighbours, so a
/// sheet built with it is awake before the solve runs and the tick after cannot tell the solve's
/// wake set from the pour's. Fill puts water in with nothing awake — unlevel water at rest, which
/// is the state the automaton's diffusion leaves behind for thousands of ticks and the state the
/// solve exists for — so whatever is awake afterwards, the solve woke.
/// </remarks>
public class SheetSettleTests
{
    private const int Cap = FluidConstants.CellCapacityMillilitres;
    private const int Packed = FluidSimulation.PackedCellMillilitres;

    private static VoxelPos At(int x, int y, int z) => new(x, y, z);

    /// <summary>
    /// Fills a run of floor cells to the brim beside dry ones, which is a sheet with somewhere to
    /// go and stays awake for tens of ticks.
    /// </summary>
    /// <remarks>
    /// <b>Add takes one cell's capacity and hands the rest back</b>, so <c>Add(cell, 200_000)</c>
    /// is a pour of 15,625 mL into one cell — a one-cell sheet, which LevelSheets skips by
    /// construction. Three trigger tests were written against exactly that and asserted nothing.
    /// </remarks>
    private static void PourRow(FluidSimulation sim, int x0, int z, int cells)
    {
        for (int i = 0; i < cells; i++)
        {
            sim.Add(At(x0 + i, 0, z), FluidConstants.CellCapacityMillilitres);
        }
    }

    /// <summary>Every sideways pair inside the set differs by at most the tolerance.</summary>
    private static void AssertPairsWithin(FluidSimulation sim, IReadOnlyCollection<VoxelPos> cells, int tolerance)
    {
        var set = new HashSet<VoxelPos>(cells);
        foreach (VoxelPos cell in cells)
        {
            int here = sim.Grid.AmountAt(cell);
            foreach ((int dx, int dz) in new[] { (1, 0), (0, 1) })
            {
                VoxelPos side = cell.Offset(dx, 0, dz);
                if (!set.Contains(side))
                {
                    continue;
                }

                int there = sim.Grid.AmountAt(side);
                Assert.True(
                    Math.Abs(here - there) <= tolerance,
                    $"{cell}={here} against {side}={there}, tolerance {tolerance}");
            }
        }
    }

    private static long Sum(FluidSimulation sim, IEnumerable<VoxelPos> cells)
    {
        long total = 0;
        foreach (VoxelPos cell in cells)
        {
            total += sim.Grid.AmountAt(cell);
        }

        return total;
    }

    private static List<VoxelPos> Floor(int minX, int maxX, int minZ, int maxZ, int y = 0)
    {
        var cells = new List<VoxelPos>();
        for (int x = minX; x <= maxX; x++)
        {
            for (int z = minZ; z <= maxZ; z++)
            {
                cells.Add(At(x, y, z));
            }
        }

        return cells;
    }

    /// <summary>The tick after a solve, then the claim the whole slice rests on.</summary>
    private static void AssertRestsAfterOneTick(FluidSimulation sim)
    {
        FluidTickResult tick = sim.Step();
        Assert.Equal(0, tick.ActiveAfter);
        Assert.True(sim.VerifyQuiescent(), "the solve left a move available");
    }

    // ---------------------------------------------------------------- the solve

    [Fact]
    public void AnUnlevelSheetIsLevelledToWithinAMillilitreInOnePassAndIsQuiescentAfterIt()
    {
        var sim = new FluidSimulation(BoxTerrain.Cube(8));
        List<VoxelPos> floor = Floor(0, 7, 0, 7);
        long total = 0;
        foreach (VoxelPos cell in floor)
        {
            // Plus 37 on one cell, so the share is not exact and the remainder has to go
            // somewhere: 467,237 over 64 is 7,300 with 37 left, one millilitre each to 37 cells.
            int amount = 1_000 + 200 * (cell.X + 8 * cell.Z) + (cell == At(5, 0, 2) ? 37 : 0);
            sim.Grid.Fill(cell, amount);
            total += amount;
        }

        SheetLevelResult result = sim.LevelSheets([At(3, 0, 3)]);

        Assert.Equal(1, result.Sheets);
        Assert.Equal(1, result.Levelled);
        Assert.Equal(64, result.LargestSheet);
        Assert.Equal(total, sim.TotalMillilitres);
        Assert.Equal(total, Sum(sim, floor));
        AssertPairsWithin(sim, floor, 1);
        int at7301 = 0;
        foreach (VoxelPos cell in floor)
        {
            int amount = sim.Grid.AmountAt(cell);
            Assert.True(amount is 7_300 or 7_301, $"{cell}={amount}");
            if (amount == 7_301) at7301++;
        }

        Assert.Equal(37, at7301);

        // A sealed floor has no front and nothing above it: nothing to wake, and every written cell
        // is at rest by the automaton's own rule, so the solve lists it as resting itself.
        Assert.Equal(64, result.CellsWritten);
        Assert.Equal(0, result.CellsWoken);
        Assert.Equal(0, sim.ActiveCellCount);
        Assert.Equal(64, sim.Rested.Count);
        Assert.True(sim.VerifyQuiescent());
    }

    [Fact]
    public void ALevelledSheetIsListedAsRestingSoTheHandOffCanTakeItAtTheSameBoundary()
    {
        // The interaction the relay measured: the hand-off reads the tick's record of resting
        // cells, and a sheet levelled and left asleep would never be in it — level, still, on the
        // ground, and never the basin's. So the solve lists what it levelled, minus the front,
        // which has a move and is woken for it, and minus anything it packed to nothing.
        var sim = new FluidSimulation(BoxTerrain.Cube(8));
        List<VoxelPos> wet = Floor(0, 3, 0, 7);
        foreach (VoxelPos cell in wet)
        {
            sim.Grid.Fill(cell, 2_000 + 100 * (cell.X + 4 * cell.Z));
        }

        sim.LevelSheets([At(0, 0, 0)]);

        var rested = new HashSet<VoxelPos>(sim.Rested);
        Assert.Equal(24, rested.Count);
        foreach (VoxelPos cell in wet)
        {
            if (cell.X == 3)
            {
                Assert.DoesNotContain(cell, rested); // the front, beside dry floor
            }
            else
            {
                Assert.Contains(cell, rested);
            }
        }

        var packed = new FluidSimulation(new BoxTerrain(0, 0, 0, 4, 0, 0));
        int[] amounts = [2_000, 100, 200, 100, 100];
        for (int x = 0; x < amounts.Length; x++)
        {
            packed.Grid.Fill(At(x, 0, 0), amounts[x]);
        }

        packed.LevelSheets([At(0, 0, 0)]);
        Assert.Equal([At(0, 0, 0), At(1, 0, 0), At(2, 0, 0)], packed.Rested.OrderBy(c => c.X));
    }

    [Fact]
    public void AFrontCellLeftAtItsShareIsStillWoken()
    {
        // Reachable only by an edit: the cell at x = 2 holds exactly the share, is not written,
        // and borders dry holdable floor at x = 3 — it has a move, and it is the only cell woken.
        var sim = new FluidSimulation(new BoxTerrain(0, 0, 0, 3, 0, 0));
        sim.Grid.Fill(At(0, 0, 0), 7_000);
        sim.Grid.Fill(At(1, 0, 0), 3_000);
        sim.Grid.Fill(At(2, 0, 0), 5_000);

        SheetLevelResult result = sim.LevelSheets([At(0, 0, 0)]);

        Assert.Equal(2, result.CellsWritten);
        Assert.Equal(1, result.CellsWoken);
        Assert.Equal([At(0, 0, 0), At(1, 0, 0)], sim.Rested.OrderBy(c => c.X));
        Assert.Equal(5_000, sim.Grid.AmountAt(At(2, 0, 0)));
        FluidTickResult tick = sim.Step();
        Assert.True(sim.Grid.AmountAt(At(3, 0, 0)) > 0, "the front did not spread");
        Assert.True(tick.CellsMoved >= 1);
        FluidAssert.SettlesAndRests(sim);
    }

    [Fact]
    public void ASecondPassOverALevelledSheetChangesNothing()
    {
        var sim = new FluidSimulation(BoxTerrain.Cube(8));
        foreach (VoxelPos cell in Floor(0, 7, 0, 7))
        {
            sim.Grid.Fill(cell, 1_000 + 200 * (cell.X + 8 * cell.Z));
        }

        sim.LevelSheets([At(0, 0, 0)]);
        SheetLevelResult again = sim.LevelSheets([At(7, 0, 7)]);

        Assert.True(again.Idle);
        Assert.Equal(1, again.Sheets);
        Assert.Equal(1, again.Skipped);
        Assert.Equal(0, again.CellsWoken);
    }

    [Fact]
    public void TwoPocketsAtOneHeightAreTwoSheetsAndNeitherTakesTheOthersWater()
    {
        // Two 4 x 4 floors with a wall of rock between them at x = 4, open above so that the
        // region is one connected volume: a solve over the region would see one pool.
        var terrain = new PredicateTerrain(v =>
            v.Y >= 0 && v.Y <= 3 && v.Z >= 0 && v.Z <= 3
            && ((v.X >= 0 && v.X <= 3) || (v.X >= 5 && v.X <= 8) || v.Y >= 2));
        var sim = new FluidSimulation(terrain);

        List<VoxelPos> left = Floor(0, 3, 0, 3);
        List<VoxelPos> right = Floor(5, 8, 0, 3);
        long leftTotal = 0, rightTotal = 0;
        foreach (VoxelPos cell in left)
        {
            leftTotal += 2_000 + 500 * cell.X;
            sim.Grid.Fill(cell, 2_000 + 500 * cell.X);
        }

        foreach (VoxelPos cell in right)
        {
            rightTotal += 9_000 + 300 * cell.Z;
            sim.Grid.Fill(cell, 9_000 + 300 * cell.Z);
        }

        SheetLevelResult result = sim.LevelSheets([At(0, 0, 0), At(8, 0, 3)]);

        Assert.Equal(2, result.Sheets);
        Assert.Equal(2, result.Levelled);
        Assert.Equal(leftTotal, Sum(sim, left));
        Assert.Equal(rightTotal, Sum(sim, right));
        AssertPairsWithin(sim, left, 1);
        AssertPairsWithin(sim, right, 1);
        AssertRestsAfterOneTick(sim);
    }

    [Fact]
    public void AHigherPocketNeverPoursIntoALowerOneItCannotReach()
    {
        // The case the plan's flood-fill would get wrong: a shelf at y = 5 whose lip runs out over
        // a pit with its floor at y = 0, the whole thing one connected volume. Sorted by Y and
        // poured from the bottom, the shelf's water would land on the pit's floor at once —
        // through rock, with no path the automaton could take. A sheet is one Y, so the pit
        // stays dry, and the water reaches it the way water does: over the lip and down.
        var terrain = new PredicateTerrain(v =>
            v.Z >= 0 && v.Z <= 3 && v.X >= 0 && v.X <= 9
            && (v.Y >= 6 && v.Y <= 9                     // the air over everything
                || (v.Y == 5 && v.X <= 5)                // the shelf, and its lip at x = 4..5
                || (v.Y >= 0 && v.Y <= 5 && v.X >= 6))); // the pit, open from its floor up
        var sim = new FluidSimulation(terrain);

        List<VoxelPos> shelf = Floor(0, 3, 0, 3, y: 5);
        List<VoxelPos> pit = Floor(6, 9, 0, 3);
        foreach (VoxelPos cell in shelf)
        {
            sim.Grid.Fill(cell, 3_000 + 700 * (cell.X + cell.Z));
        }

        long before = Sum(sim, shelf);
        SheetLevelResult result = sim.LevelSheets([At(0, 5, 0)]);

        Assert.Equal(1, result.Sheets);
        Assert.Equal(before, Sum(sim, shelf));
        Assert.Equal(0, Sum(sim, pit));
        AssertPairsWithin(sim, shelf, 1);

        // The share is exactly 5,100 and the four cells with x + z = 3 already hold it, so twelve
        // are written; the four at x = 3 border the dry lip and are woken for it. The ticks after
        // spread them along the lip and over its edge, so the water ends in the pit — by falling,
        // not by the solve.
        Assert.Equal(12, result.CellsWritten);
        Assert.Equal(4, result.CellsWoken);
        FluidAssert.SettlesAndRests(sim);
        Assert.Equal(before, sim.TotalMillilitres);
        Assert.True(Sum(sim, pit) > 0, "nothing went over the lip");
    }

    [Fact]
    public void TheDryHalfOfAFloorStaysDryAndOnlyTheFrontIsWoken()
    {
        var sim = new FluidSimulation(BoxTerrain.Cube(8));
        List<VoxelPos> wet = Floor(0, 3, 0, 7);
        List<VoxelPos> dry = Floor(4, 7, 0, 7);
        foreach (VoxelPos cell in wet)
        {
            sim.Grid.Fill(cell, 2_000 + 100 * (cell.X + 4 * cell.Z));
        }

        SheetLevelResult result = sim.LevelSheets([At(0, 0, 0)]);

        Assert.Equal(32, result.LargestSheet);
        Assert.Equal(0, Sum(sim, dry));
        AssertPairsWithin(sim, wet, 1);

        // Exactly the column at x = 3 borders dry floor; the interior has no move and is asleep.
        Assert.Equal(8, result.CellsWoken);
        FluidTickResult tick = sim.Step();
        Assert.Equal(8, tick.CellsVisited);
        Assert.Equal(8, tick.CellsMoved);
        Assert.True(Sum(sim, dry) > 0, "the front did not creep");

        FluidAssert.SettlesAndRests(sim);
    }

    [Fact]
    public void WaterAboveACellDrawnDownIsWokenAndFalls()
    {
        var sim = new FluidSimulation(BoxTerrain.Cube(8));
        sim.Grid.Fill(At(0, 0, 0), Cap);
        sim.Grid.Fill(At(1, 0, 0), 1_000);
        sim.Grid.Fill(At(0, 1, 0), 5_000); // standing on the full cell

        SheetLevelResult result = sim.LevelSheets([At(0, 0, 0)]);

        Assert.Equal(1, result.Sheets);
        Assert.Equal(8_313, sim.Grid.AmountAt(At(0, 0, 0)));
        Assert.Equal(8_312, sim.Grid.AmountAt(At(1, 0, 0)));
        Assert.Equal(5_000, sim.Grid.AmountAt(At(0, 1, 0)));

        // Both floor cells were written and the full one was drawn down under standing water:
        // three wakes, and the water above falls on the very next tick.
        Assert.Equal(3, result.CellsWoken);
        sim.Step();
        Assert.Equal(0, sim.Grid.AmountAt(At(0, 1, 0)));

        FluidAssert.SettlesAndRests(sim);
    }

    [Fact]
    public void AShallowSheetIsPackedIntoAsFewCellsAsHoldItTheCellsThatHeldMostFirst()
    {
        // 2,500 mL over five cells is under a litre each, and the first cell can still spread,
        // so the sheet is not at rest. (Five cells each under the threshold would be, and would
        // be left alone — see the skip test.)
        var sim = new FluidSimulation(new BoxTerrain(0, 0, 0, 4, 0, 0));
        int[] amounts = [2_000, 100, 200, 100, 100];
        for (int x = 0; x < amounts.Length; x++)
        {
            sim.Grid.Fill(At(x, 0, 0), amounts[x]);
        }

        SheetLevelResult result = sim.LevelSheets([At(0, 0, 0)]);

        Assert.Equal(1, result.Packed);
        Assert.Equal(0, result.Levelled);
        Assert.Equal(Packed, sim.Grid.AmountAt(At(0, 0, 0))); // held 2,000
        Assert.Equal(Packed, sim.Grid.AmountAt(At(2, 0, 0))); // held 200
        Assert.Equal(502, sim.Grid.AmountAt(At(1, 0, 0)));    // held 100, first of the ties, takes the remainder
        Assert.Equal(0, sim.Grid.AmountAt(At(3, 0, 0)));
        Assert.Equal(0, sim.Grid.AmountAt(At(4, 0, 0)));
        Assert.Equal(2_500, sim.TotalMillilitres);

        // A packed cell is one millilitre under the spread threshold: nothing beside it matters.
        AssertRestsAfterOneTick(sim);
    }

    [Fact]
    public void ThePackBoundaryIsExactlyWhatTheCellsHoldWithoutSpreading()
    {
        var packed = new FluidSimulation(new BoxTerrain(0, 0, 0, 3, 0, 0));
        packed.Grid.Fill(At(0, 0, 0), 3_000);
        packed.Grid.Fill(At(1, 0, 0), 500);
        packed.Grid.Fill(At(2, 0, 0), 400);
        packed.Grid.Fill(At(3, 0, 0), 96); // 3,996 = 999 x 4: packs into every cell
        SheetLevelResult packs = packed.LevelSheets([At(0, 0, 0)]);
        Assert.Equal(1, packs.Packed);
        for (int x = 0; x < 4; x++)
        {
            Assert.Equal(Packed, packed.Grid.AmountAt(At(x, 0, 0)));
        }

        AssertRestsAfterOneTick(packed);

        var levelled = new FluidSimulation(new BoxTerrain(0, 0, 0, 3, 0, 0));
        levelled.Grid.Fill(At(0, 0, 0), 3_000);
        levelled.Grid.Fill(At(1, 0, 0), 500);
        levelled.Grid.Fill(At(2, 0, 0), 400);
        levelled.Grid.Fill(At(3, 0, 0), 97); // one more: no packing holds it, so it levels
        SheetLevelResult levels = levelled.LevelSheets([At(0, 0, 0)]);
        Assert.Equal(1, levels.Levelled);
        Assert.Equal(1_000, levelled.Grid.AmountAt(At(0, 0, 0)));
        Assert.Equal(999, levelled.Grid.AmountAt(At(1, 0, 0)));
        Assert.Equal(999, levelled.Grid.AmountAt(At(2, 0, 0)));
        Assert.Equal(999, levelled.Grid.AmountAt(At(3, 0, 0)));

        // A cell at the threshold beside one a millilitre under it transfers 1 / 5 = 0.
        AssertRestsAfterOneTick(levelled);
    }

    [Fact]
    public void ASheetWithNoMoveInsideItIsLeftExactlyAsItWas()
    {
        // The automaton's own rest: neighbours up to four millilitres apart, and a cell under
        // the spread threshold beside anything. Re-levelling it to within one would write every
        // cell to move water nobody could see.
        var sim = new FluidSimulation(new BoxTerrain(0, 0, 0, 3, 0, 0));
        int[] amounts = [1_004, 1_000, 999, 400];
        for (int x = 0; x < amounts.Length; x++)
        {
            sim.Grid.Fill(At(x, 0, 0), amounts[x]);
        }

        SheetLevelResult result = sim.LevelSheets([At(0, 0, 0)]);

        Assert.Equal(1, result.Sheets);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.CellsWritten);
        Assert.Equal(0, result.CellsWoken);
        for (int x = 0; x < amounts.Length; x++)
        {
            Assert.Equal(amounts[x], sim.Grid.AmountAt(At(x, 0, 0)));
        }
    }

    [Fact]
    public void ASheetOfOneCellIsNothingToSolve()
    {
        var sim = new FluidSimulation(BoxTerrain.Cube(4));
        sim.Grid.Fill(At(1, 0, 1), 12_000);

        SheetLevelResult result = sim.LevelSheets([At(1, 0, 1)]);

        Assert.Equal(1, result.Sheets);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.CellsWritten);
        Assert.Equal(12_000, sim.Grid.AmountAt(At(1, 0, 1)));
    }

    [Fact]
    public void ACellWithRoomBelowIsFallingAndIsNotASheetCell()
    {
        var sim = new FluidSimulation(BoxTerrain.Cube(4));
        sim.Grid.Fill(At(1, 0, 1), 5_000);  // partial
        sim.Grid.Fill(At(1, 1, 1), 3_000);  // over a partial cell: falling
        sim.Grid.Fill(At(2, 1, 1), 3_000);  // over dry floor: falling

        SheetLevelResult fromAbove = sim.LevelSheets([At(1, 1, 1), At(2, 1, 1)]);
        Assert.Equal(0, fromAbove.Sheets);
        Assert.Equal(3_000, sim.Grid.AmountAt(At(1, 1, 1)));
        Assert.Equal(3_000, sim.Grid.AmountAt(At(2, 1, 1)));

        // And a falling cell beside a supported one is not the supported one's sheet either:
        // a full cell under (0, 1, 1) makes that a sheet of one, and its neighbour at (1, 1, 1),
        // with room under it, is left to fall rather than levelled with it.
        sim.Grid.Fill(At(0, 0, 1), Cap);
        sim.Grid.Fill(At(0, 1, 1), 9_000);
        SheetLevelResult beside = sim.LevelSheets([At(0, 1, 1), At(1, 1, 1)]);
        Assert.Equal(1, beside.Sheets);
        Assert.Equal(1, beside.Skipped);
        Assert.Equal(9_000, sim.Grid.AmountAt(At(0, 1, 1)));
        Assert.Equal(3_000, sim.Grid.AmountAt(At(1, 1, 1)));
    }

    [Fact]
    public void ACellThatCanNoLongerHoldWaterIsNotASheetCellAndSplitsTheSheet()
    {
        var solid = new HashSet<VoxelPos>();
        var terrain = new PredicateTerrain(v =>
            v.Y == 0 && v.Z == 0 && v.X >= 0 && v.X <= 2 && !solid.Contains(v));
        var sim = new FluidSimulation(terrain);
        sim.Grid.Fill(At(0, 0, 0), 9_000);
        sim.Grid.Fill(At(1, 0, 0), 5_000);
        sim.Grid.Fill(At(2, 0, 0), 1_000);

        // Rock placed into the middle cell: its water is the tick's to displace, not a sheet's.
        solid.Add(At(1, 0, 0));
        SheetLevelResult result = sim.LevelSheets([At(0, 0, 0), At(2, 0, 0)]);

        Assert.Equal(2, result.Sheets);
        Assert.Equal(2, result.Skipped);
        Assert.Equal(9_000, sim.Grid.AmountAt(At(0, 0, 0)));
        Assert.Equal(5_000, sim.Grid.AmountAt(At(1, 0, 0)));
        Assert.Equal(1_000, sim.Grid.AmountAt(At(2, 0, 0)));
    }

    [Fact]
    public void LowerSheetsAreSolvedFirstSoAHigherSheetIsFoundAgainstTheFloorTheyLeave()
    {
        // Two full cells with water standing on them, beside two cells that are nearly empty. The
        // floor sheet is unlevel; the sheet above it is level. Solving the floor first draws the
        // full cells down, which unsupports the water on them — so it is left out, woken, and
        // falls next tick, rather than being levelled as a sheet that no longer exists.
        var sim = new FluidSimulation(new BoxTerrain(0, 0, 0, 3, 3, 0));
        sim.Grid.Fill(At(0, 0, 0), Cap);
        sim.Grid.Fill(At(1, 0, 0), Cap);
        sim.Grid.Fill(At(2, 0, 0), 1_000);
        sim.Grid.Fill(At(3, 0, 0), 1_000);
        sim.Grid.Fill(At(0, 1, 0), 4_000);
        sim.Grid.Fill(At(1, 1, 0), 4_000);
        long total = sim.TotalMillilitres;

        SheetLevelResult result = sim.LevelSheets([At(0, 1, 0), At(1, 1, 0), At(3, 0, 0)]);

        Assert.Equal(1, result.Sheets);
        Assert.Equal(1, result.Levelled);
        Assert.Equal(4, result.LargestSheet);
        Assert.Equal(4_000, sim.Grid.AmountAt(At(0, 1, 0)));
        Assert.Equal(4_000, sim.Grid.AmountAt(At(1, 1, 0)));
        Assert.True(sim.Grid.AmountAt(At(0, 0, 0)) < Cap);

        // The two cells above were woken: the tick after, they fall.
        Assert.True(result.CellsWoken >= 2);
        sim.Step();
        Assert.True(sim.Grid.AmountAt(At(0, 1, 0)) < 4_000);
        Assert.True(sim.Grid.AmountAt(At(1, 1, 0)) < 4_000);

        FluidAssert.SettlesAndRests(sim);
        Assert.Equal(total, sim.TotalMillilitres);
    }

    [Fact]
    public void ASolveConservesVolumeAndTouchesNoLedgerTerm()
    {
        var sim = new FluidSimulation(BoxTerrain.Cube(8));
        sim.Add(At(4, 7, 4), 40_000);
        sim.Step();
        sim.Step();
        long total = sim.TotalMillilitres;
        long added = sim.AddedMillilitres;

        sim.LevelSheets();

        Assert.Equal(total, sim.TotalMillilitres);
        Assert.Equal(added, sim.AddedMillilitres);
        Assert.Equal(0, sim.RemovedMillilitres);
        Assert.Equal(0, sim.DisplacedMillilitres);
        Assert.Equal(0, sim.HandedMillilitres);
        FluidAssert.Conserves(sim);
    }

    [Fact]
    public void ASolveIsRefusedMidTick()
    {
        var sim = new FluidSimulation(BoxTerrain.Cube(8));
        sim.Add(At(4, 7, 4), 40_000);
        Assert.Null(sim.Resume(1));

        Assert.Throws<InvalidOperationException>(() => sim.LevelSheets());
    }

    [Fact]
    public void SolvingFromTheActiveSetTouchesOnlySheetsSomethingIsAwakeIn()
    {
        var terrain = new PredicateTerrain(v =>
            v.Y == 0 && v.Z >= 0 && v.Z <= 3 && ((v.X >= 0 && v.X <= 3) || (v.X >= 5 && v.X <= 8)));
        var sim = new FluidSimulation(terrain);
        List<VoxelPos> left = Floor(0, 3, 0, 3);
        List<VoxelPos> right = Floor(5, 8, 0, 3);
        foreach (VoxelPos cell in left)
        {
            sim.Grid.Fill(cell, 2_000 + 500 * cell.X);
        }

        foreach (VoxelPos cell in right)
        {
            sim.Grid.Fill(cell, 2_000 + 500 * cell.X);
        }

        // One millilitre into the left pocket is the only thing awake anywhere.
        sim.Add(At(0, 0, 0), 1);
        SheetLevelResult result = sim.LevelSheets();

        Assert.Equal(1, result.Sheets);
        AssertPairsWithin(sim, left, 1);
        for (int z = 0; z <= 3; z++)
        {
            Assert.Equal(2_000 + 500 * 8, sim.Grid.AmountAt(At(8, 0, z)));
        }
    }

    [Fact]
    public void TheSolveDoesNotDependOnTheOrderTheSeedsArrivedIn()
    {
        List<VoxelPos> floor = Floor(0, 7, 0, 7);
        FluidSimulation Build()
        {
            var sim = new FluidSimulation(BoxTerrain.Cube(8));
            foreach (VoxelPos cell in floor)
            {
                sim.Grid.Fill(cell, 1_000 + 137 * ((cell.X * 5 + cell.Z * 3) % 11));
            }

            return sim;
        }

        FluidSimulation forward = Build();
        FluidSimulation backward = Build();
        var reversed = new List<VoxelPos>(floor);
        reversed.Reverse();

        forward.LevelSheets(floor);
        backward.LevelSheets(reversed);

        foreach (VoxelPos cell in floor)
        {
            Assert.Equal(forward.Grid.AmountAt(cell), backward.Grid.AmountAt(cell));
        }
    }

    [Fact]
    public void ACorridorThatLevelsByDiffusionForThousandsOfTicksIsOneSolveAndNoTicksAtAll()
    {
        // M8a's finding with the settle applied: a corridor whose gradient the automaton spreads
        // by diffusion, O(n²) ticks for n cells, is level after one pass and quiescent at once.
        const int Length = 200;
        FluidSimulation Corridor()
        {
            var sim = new FluidSimulation(new BoxTerrain(0, 0, 0, Length - 1, 0, 0));
            for (int x = 0; x < Length; x++)
            {
                sim.Grid.Fill(At(x, 0, 0), 1_000 + 60 * x);
            }

            return sim;
        }

        FluidSimulation diffusing = Corridor();
        for (int x = 0; x < Length; x++)
        {
            diffusing.Disturb(At(x, 0, 0));
        }

        FluidSettleResult byDiffusion = diffusing.Settle(maxTicks: 2_000);
        Assert.False(byDiffusion.Settled, "the automaton levelled 200 cells inside 2,000 ticks");

        FluidSimulation solved = Corridor();
        var seeds = new List<VoxelPos>();
        for (int x = 0; x < Length; x++)
        {
            seeds.Add(At(x, 0, 0));
        }

        SheetLevelResult result = solved.LevelSheets(seeds);
        Assert.Equal(1, result.Levelled);
        Assert.Equal(Length, result.LargestSheet);
        AssertPairsWithin(solved, seeds, 1);

        // Nothing woken, nothing awake, and every cell listed as resting: no tick at all.
        Assert.Equal(0, result.CellsWoken);
        Assert.Equal(0, solved.ActiveCellCount);
        Assert.Equal(Length, solved.Rested.Count);
        Assert.True(solved.VerifyQuiescent());
        Assert.Equal(diffusing.TotalMillilitres, solved.TotalMillilitres);
    }

    // ---------------------------------------------------------------- the trigger

    [Fact]
    public void TheTriggerWaitsForKQuietTicksThenFiresAtEveryBoundaryUntilNothingIsAwake()
    {
        var sim = new FluidSimulation(BoxTerrain.Cube(8));
        var settle = new SheetSettle(sim, settleAfterTicks: 2);

        // The pour lands on the tick that follows, so that tick is not a quiet one. Since
        // 2026-09-09 the wait is a property of the sheet rather than of the world, so what says
        // "not yet" is a sheet left alone for standing in a fed chunk, not a counter.
        PourRow(sim, 0, 4, 4);
        sim.Step();
        SheetLevelResult first = settle.AtBoundary()!.Value;
        Assert.True(first.Noisy > 0, "the sheet was levelled on the tick the pour landed");
        Assert.Equal(0, first.Levelled + first.Packed);

        sim.Step();
        SheetLevelResult second = settle.AtBoundary()!.Value;
        Assert.True(second.Noisy > 0);
        Assert.Equal(0, second.Levelled + second.Packed);

        sim.Step();
        SheetLevelResult fired = settle.AtBoundary()!.Value;
        Assert.Equal(0, fired.Noisy);
        Assert.True(fired.Levelled + fired.Packed > 0);

        // Then at every boundary while anything is awake, and not once nothing is.
        int boundaries = 0;
        while (sim.ActiveCellCount > 0 && boundaries < 500)
        {
            sim.Step();
            settle.AtBoundary();
            boundaries++;
        }

        Assert.Equal(0, sim.ActiveCellCount);
        Assert.True(sim.VerifyQuiescent());
        Assert.Null(settle.AtBoundary());
        long settles = settle.Settles;
        sim.Step();
        Assert.Null(settle.AtBoundary());
        Assert.Equal(settles, settle.Settles);
        FluidAssert.Conserves(sim);
    }

    [Fact]
    public void ZeroSettlesUnderInflow()
    {
        var sim = new FluidSimulation(BoxTerrain.Cube(8));
        var settle = new SheetSettle(sim, settleAfterTicks: 0);

        sim.Add(At(4, 7, 4), 100_000);
        sim.Step();
        Assert.NotNull(settle.AtBoundary());

        sim.Add(At(4, 7, 4), 100_000);
        sim.Step();
        Assert.NotNull(settle.AtBoundary());
        Assert.Equal(2, settle.Settles);
    }

    [Fact]
    public void TheTriggerDoesNotFireWithNothingAwakeWhateverTheCount()
    {
        var sim = new FluidSimulation(BoxTerrain.Cube(8));
        var settle = new SheetSettle(sim, settleAfterTicks: 0);
        sim.Grid.Fill(At(0, 0, 0), 9_000);
        sim.Grid.Fill(At(1, 0, 0), 1_000);

        Assert.Null(settle.AtBoundary());
        Assert.Equal(0, settle.Settles);
    }

    [Fact]
    public void AnEditThatWakesWaterIsInflowAndOneThatWakesNothingIsNot()
    {
        // A pour that keeps spreading, so there is always a sheet to judge: with nothing awake
        // AtBoundary has nothing to say and every arm of this test would read the same null.
        var sim = new FluidSimulation(BoxTerrain.Cube(8));
        var settle = new SheetSettle(sim, settleAfterTicks: 1);
        PourRow(sim, 0, 4, 4);
        sim.Step();
        sim.Step();
        Assert.Equal(0, settle.AtBoundary()!.Value.Noisy);

        // Dry rock: nothing woke, so nothing was fed and the sheet is still eligible.
        sim.Disturb(At(0, 7, 0));
        sim.Step();
        Assert.Equal(0, settle.AtBoundary()!.Value.Noisy);

        // Water: the terrain under it moved, and the wait starts over.
        sim.Disturb(At(1, 0, 4));
        sim.Step();
        Assert.True(settle.AtBoundary()!.Value.Noisy > 0);
    }

    [Fact]
    public void WaterLeavingIsNotInflow()
    {
        var sim = new FluidSimulation(BoxTerrain.Cube(8));
        var settle = new SheetSettle(sim, settleAfterTicks: 1);
        PourRow(sim, 0, 4, 4);
        sim.Step();
        sim.Step();

        sim.Remove(At(1, 0, 4), 1_000);
        sim.Step();
        Assert.Equal(0, settle.AtBoundary()!.Value.Noisy);

        sim.HandOff(At(1, 0, 4), 1_000);
        sim.Step();

        // Water leaving is not inflow: a sheet drained from above has less to level, not more.
        Assert.Equal(0, settle.AtBoundary()!.Value.Noisy);
    }

    [Fact]
    public void ASheetIsFedIfAnyChunkItStandsInIs()
    {
        // A sheet that reaches into a chunk still receiving — a pool touching the stream that
        // feeds it — is fed however quiet its other end has been, or the solve levels water while
        // it is still arriving. <b>The battery is why this is a walk of the sheet's cells rather
        // than a field the flood accumulates</b>: as a field the answer depended on Union taking
        // the max, and no fixture can reliably put the fed half on the losing side of a merge,
        // because the survivor is whichever has more cells.
        var terrain = new BoxTerrain(0, 0, 0, 63, 31, 7);
        var sim = new FluidSimulation(terrain);
        var settle = new SheetSettle(sim, settleAfterTicks: 2);

        // One connected run across the chunk boundary at x = 32, with the fed end the short side
        // so the merge cannot pass by keeping the fed half as the survivor.
        PourRow(sim, 20, 4, 17);
        sim.Step();
        sim.Step();
        sim.Step();
        Assert.Equal(0, settle.AtBoundary()!.Value.Noisy);

        sim.Add(At(36, 0, 4), 4_000);
        sim.Step();

        SheetLevelResult result = settle.AtBoundary()!.Value;
        Assert.True(
            result.Noisy > 0,
            "a sheet spanning a fed chunk and a quiet one was levelled under its own inflow");
        FluidAssert.Conserves(sim);
    }

    [Fact]
    public void OneSheetBeingFedDoesNotPostponeAnother()
    {
        // The whole reason the wait moved off a world counter (2026-09-09): a played session ran
        // 340 ticks with 70,000 cells awake and ran the solve <b>not once</b>, because a portal
        // drew ~150 L into one cell every tick and one global counter never reached two.
        var terrain = new BoxTerrain(0, 0, 0, 127, 31, 7);
        var sim = new FluidSimulation(terrain);
        var settle = new SheetSettle(sim, settleAfterTicks: 2);

        // Two bodies, three chunks apart, each spreading.
        PourRow(sim, 4, 4, 4);
        PourRow(sim, 108, 4, 4);
        sim.Step();
        sim.Step();
        sim.Step();

        // A tap on the far one only, at every boundary — the portal drain's shape.
        for (int tick = 0; tick < 4; tick++)
        {
            sim.Add(At(108, 0, 4), 4_000);
            sim.Step();
        }

        SheetLevelResult result = settle.AtBoundary()!.Value;

        Assert.True(result.Noisy > 0, "the fed sheet was levelled under its own inflow");
        Assert.True(
            result.Levelled + result.Packed > 0,
            "nothing was levelled: the fed sheet is still postponing the quiet one");

        // And every cell of the levelled body really is level, which is the pass's own oracle.
        Assert.True(sim.TotalMillilitres > 0);
        FluidAssert.Conserves(sim);
    }

    // ---------------------------------------------------------------- real terrain

    [Fact]
    public void OnRealTerrainSolvingAtEveryBoundaryReachesARestThatIsRealAndLosesNothing()
    {
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

        var random = new Random(8_3);
        var sim = new FluidSimulation(terrain);
        var settle = new SheetSettle(sim, settleAfterTicks: 0);
        long rejected = 0;
        for (int pour = 0; pour < 6; pour++)
        {
            VoxelPos? top = terrain.TopOpening(random.Next(-16, 16), random.Next(-16, 16));
            if (top is null)
            {
                continue;
            }

            rejected += sim.Add(top.Value, 60_000);
            for (int tick = 0; tick < 3; tick++)
            {
                sim.Step();
                settle.AtBoundary();
            }
        }

        int ticks = 0;
        while (sim.ActiveCellCount > 0 && ticks < 3_000)
        {
            sim.Step();
            settle.AtBoundary();
            ticks++;
        }

        Assert.True(ticks < 3_000, $"still {sim.ActiveCellCount} awake after {ticks} ticks");
        Assert.True(sim.VerifyQuiescent(), "rest reached with a move still available");
        Assert.True(settle.Settles > 0);
        FluidAssert.Conserves(sim, rejected);
    }
}
