using Overburden.Core.Fluid;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// The share slice of M8b-3b: a sheet with a move in it is re-levelled only when at least one
/// cell in <i>share</i> of it is unsettled or a step stands in it, and a sheet under the share
/// has its flood stopped early. The oracle for the rule is a flood written here over the whole
/// floor, with the automaton's own pair test; the oracle for the wake set is
/// <see cref="FluidSimulation.VerifyQuiescent"/> after the automaton has run to rest.
/// </summary>
/// <remarks>
/// <b>Water goes down through <see cref="FluidGrid.Fill"/> and is woken through
/// <see cref="FluidSimulation.Disturb"/></b>, which wakes a wet cell and its wet neighbours the
/// way a transfer would — so the seeds are the awake set a tick boundary would have, and every
/// unsettled cell neighbours a seed, which is the invariant the early stop rests on.
/// </remarks>
public class SheetShareTests
{
    private static VoxelPos At(int x, int y, int z) => new(x, y, z);

    /// <summary>A sealed box with a floor of the given footprint at Y = 0 and room above it.</summary>
    private static BoxTerrain Box(int width, int depth) => new(0, 0, 0, width - 1, 3, depth - 1);

    private static List<VoxelPos> Floor(int minX, int maxX, int minZ, int maxZ)
    {
        var cells = new List<VoxelPos>();
        for (int x = minX; x <= maxX; x++)
        {
            for (int z = minZ; z <= maxZ; z++)
            {
                cells.Add(At(x, 0, z));
            }
        }

        return cells;
    }

    private static void FillFloor(FluidSimulation sim, IEnumerable<VoxelPos> cells, Func<VoxelPos, int> amount)
    {
        foreach (VoxelPos cell in cells)
        {
            sim.Grid.Fill(cell, amount(cell));
        }
    }

    private static void AssertLevel(FluidSimulation sim, IEnumerable<VoxelPos> cells, int tolerance = 1)
    {
        int low = int.MaxValue, high = int.MinValue;
        foreach (VoxelPos cell in cells)
        {
            int amount = sim.Grid.AmountAt(cell);
            low = Math.Min(low, amount);
            high = Math.Max(high, amount);
        }

        Assert.True(high - low <= tolerance, $"sheet spans {low}..{high}, tolerance {tolerance}");
    }

    private static Dictionary<VoxelPos, int> Snapshot(FluidSimulation sim, IEnumerable<VoxelPos> cells) =>
        cells.ToDictionary(cell => cell, cell => sim.Grid.AmountAt(cell));

    private static void AssertUnchanged(FluidSimulation sim, Dictionary<VoxelPos, int> before)
    {
        foreach ((VoxelPos cell, int amount) in before)
        {
            Assert.Equal(amount, sim.Grid.AmountAt(cell));
        }
    }

    /// <summary>The automaton to rest, then the claim the whole slice rests on.</summary>
    private static void AssertRestsAndTheRestIsReal(FluidSimulation sim, long total)
    {
        FluidSettleResult settled = sim.Settle(50_000);
        Assert.True(settled.Settled, $"still {sim.ActiveCellCount} awake after {settled.Ticks} ticks");
        Assert.True(sim.VerifyQuiescent(), "the automaton stopped with a move available");
        Assert.Equal(total, sim.TotalMillilitres);
    }

    // ------------------------------------------------------------ the share rule

    [Fact]
    public void ALocalDisturbanceOnALevelSheetIsLeftToTheAutomatonAndItsFloodStopsEarly()
    {
        // The play's shape: a 1,024-cell lake, level, with one cell drawn down at its edge. Five
        // cells are unsettled — the low cell and the four that can give to it — and at one in
        // eight that is worth a solve only on a sheet of forty cells or fewer.
        var sim = new FluidSimulation(Box(32, 32));
        List<VoxelPos> floor = Floor(0, 31, 0, 31);
        FillFloor(sim, floor, cell => cell == At(5, 0, 5) ? 7_000 : 8_000);
        sim.Disturb(At(5, 0, 5));
        Assert.Equal(5, sim.ActiveCellCount);
        long total = sim.TotalMillilitres;

        SheetLevelResult result = sim.LevelSheets(share: 8);

        Assert.Equal(1, result.Sheets);
        Assert.Equal(1, result.UnderShare);
        Assert.Equal(0, result.Levelled);
        Assert.Equal(0, result.Stepped);
        Assert.Equal(0, result.CellsWritten);
        Assert.Equal(0, result.CellsWoken);

        // A sheet whose flood stopped was not found whole, and the largest-sheet figure is the
        // cost of one solve, so it does not count.
        Assert.Equal(0, result.LargestSheet);

        // The flood stopped once the sheet was known to be under the share: forty cells plus the
        // layer that crossed the line and the frontier it discovered, nowhere near the lake.
        Assert.True(result.CellsVisited >= 40, $"visited {result.CellsVisited}");
        Assert.True(result.CellsVisited <= 128, $"visited {result.CellsVisited} of {floor.Count}");

        // And the automaton does the local levelling itself, waking nothing behind the wave.
        AssertRestsAndTheRestIsReal(sim, total);
        Assert.Equal(8_000, sim.Grid.AmountAt(At(20, 0, 20)));
    }

    [Fact]
    public void ADisturbanceThatIsAShareOfTheSheetIsLevelledWhole()
    {
        // A quarter of the lake unlevel — every cell of a 16 x 16 checkerboard has a move — is
        // the wide disturbance diffusion would take O(n²) ticks over, and the solve takes it.
        var sim = new FluidSimulation(Box(32, 32));
        List<VoxelPos> floor = Floor(0, 31, 0, 31);
        FillFloor(sim, floor, cell => cell.X < 16 && cell.Z < 16 ? ((cell.X + cell.Z) % 2 == 0 ? 8_000 : 6_000) : 8_000);
        foreach (VoxelPos cell in Floor(0, 15, 0, 15))
        {
            sim.Disturb(cell);
        }

        long total = sim.TotalMillilitres;
        SheetLevelResult result = sim.LevelSheets(share: 8);

        Assert.Equal(1, result.Sheets);
        Assert.Equal(1, result.Levelled);
        Assert.Equal(0, result.UnderShare);
        Assert.Equal(floor.Count, result.CellsVisited);
        AssertLevel(sim, floor);
        AssertRestsAndTheRestIsReal(sim, total);
    }

    [Fact]
    public void TheRuleIsAtLeastOneCellInNAndTheBoundaryIsInclusive()
    {
        // Twelve unsettled cells on a 48-cell floor: four corners drawn down, each with its two
        // neighbours. One in four is exactly met and levels; one in three is not, and the
        // automaton is left the corners.
        static FluidSimulation Fixture()
        {
            var sim = new FluidSimulation(Box(6, 8));
            FillFloor(sim, Floor(0, 5, 0, 7), cell => cell.X is 0 or 5 && cell.Z is 0 or 7 ? 6_000 : 8_000);
            foreach (VoxelPos corner in new[] { At(0, 0, 0), At(5, 0, 0), At(0, 0, 7), At(5, 0, 7) })
            {
                sim.Disturb(corner);
            }

            return sim;
        }

        FluidSimulation met = Fixture();
        SheetLevelResult atFour = met.LevelSheets(share: 4);
        Assert.Equal(1, atFour.Levelled);
        Assert.Equal(0, atFour.UnderShare);
        Assert.Equal(0, atFour.Stepped);
        AssertLevel(met, Floor(0, 5, 0, 7));

        FluidSimulation missed = Fixture();
        SheetLevelResult atThree = missed.LevelSheets(share: 3);
        Assert.Equal(0, atThree.Levelled);
        Assert.Equal(1, atThree.UnderShare);
        Assert.Equal(6_000, missed.Grid.AmountAt(At(0, 0, 0)));

        // Zero is no threshold: the pre-slice behaviour, and the arm every earlier test runs.
        FluidSimulation control = Fixture();
        Assert.Equal(1, control.LevelSheets(share: 0).Levelled);
    }

    // ------------------------------------------------------------ the step

    [Fact]
    public void AStepBetweenTwoSettledPlateausIsLevelledWhateverTheShare()
    {
        // Two puddles levelled apart that have met: 32 unsettled cells along the line, on a
        // 512-cell sheet — one in sixteen, under the share — but the settled cells bordering the
        // line stand at 9,000 on one side and 5,000 on the other.
        var sim = new FluidSimulation(Box(32, 16));
        List<VoxelPos> floor = Floor(0, 31, 0, 15);
        FillFloor(sim, floor, cell => cell.X < 16 ? 9_000 : 5_000);
        for (int z = 0; z < 16; z++)
        {
            sim.Disturb(At(15, 0, z));
        }

        long total = sim.TotalMillilitres;
        SheetLevelResult result = sim.LevelSheets(share: 8);

        Assert.Equal(1, result.Sheets);
        Assert.Equal(1, result.Levelled);
        Assert.Equal(1, result.Stepped);
        Assert.Equal(0, result.UnderShare);
        AssertLevel(sim, floor);
        Assert.Equal(7_000, sim.Grid.AmountAt(At(0, 0, 0)));
        AssertRestsAndTheRestIsReal(sim, total);
    }

    [Fact]
    public void ACreepingEdgeIsNotAStepBecauseItsBorderAgreesAboutTheLevel()
    {
        // The wave a front leaves behind it: 6,000, 7,700, 7,996 and then the interior at 8,000 —
        // with the resting tilt the automaton leaves, four millilitres between neighbouring
        // rows, so the bordering settled cells disagree by exactly what rest allows and no more.
        // Three columns are unsettled, 48 of 512 cells; one plateau, no step.
        var sim = new FluidSimulation(Box(32, 16));
        List<VoxelPos> floor = Floor(0, 31, 0, 15);
        FillFloor(sim, floor, cell => cell.X switch { 0 => 6_000, 1 => 7_700, 2 => 7_996, _ => 8_000 + 4 * (cell.Z % 2) });
        for (int z = 0; z < 16; z++)
        {
            sim.Disturb(At(0, 0, z));
        }

        long total = sim.TotalMillilitres;
        Dictionary<VoxelPos, int> before = Snapshot(sim, floor);
        SheetLevelResult result = sim.LevelSheets(share: 8);

        Assert.Equal(1, result.UnderShare);
        Assert.Equal(0, result.Stepped);
        Assert.Equal(0, result.Levelled);
        AssertUnchanged(sim, before);
        Assert.True(result.CellsVisited < floor.Count, $"visited {result.CellsVisited} of {floor.Count}");

        AssertRestsAndTheRestIsReal(sim, total);
    }

    // ------------------------------------------------------------ the early stop

    [Fact]
    public void AFrozenSheetReachedByALiveFloodIsThawedAndLevelledWhole()
    {
        // A level puddle with one low corner — its flood stops after a few layers — joined along
        // one side by fresh, unlevel water whose flood is worth finishing. They are one sheet,
        // so the puddle's flood has to be taken up again from where it stopped: the cells it had
        // discovered and not examined are the ones whose front flags would otherwise never be
        // set, and the level here is above the puddle's, so those on its dry edge rise and must
        // be woken.
        var sim = new FluidSimulation(Box(32, 32));
        List<VoxelPos> puddle = Floor(0, 15, 0, 15);
        List<VoxelPos> fresh = Floor(16, 23, 0, 15);
        FillFloor(sim, puddle, cell => cell == At(0, 0, 15) ? 7_000 : 8_000);
        FillFloor(sim, fresh, cell => (cell.X + cell.Z) % 2 == 0 ? 12_000 : 10_000);
        sim.Disturb(At(0, 0, 15));
        foreach (VoxelPos cell in fresh)
        {
            sim.Disturb(cell);
        }

        long total = sim.TotalMillilitres;
        SheetLevelResult result = sim.LevelSheets(share: 8);

        Assert.Equal(1, result.Sheets);
        Assert.Equal(1, result.Levelled);
        Assert.Equal(0, result.UnderShare);
        Assert.Equal(puddle.Count + fresh.Count, result.LargestSheet);
        AssertLevel(sim, puddle.Concat(fresh));
        Assert.True(sim.Grid.AmountAt(At(0, 0, 0)) > 8_000, "the puddle's far corner did not rise");

        // The intermediate, because the automaton would mask a lost front a tick later: the low
        // corner sits on the puddle's dry edge, so the stopped flood's frontier runs along that
        // edge, and every cell of the joined sheet beside dry floor must be awake now.
        var pending = new HashSet<VoxelPos>(sim.Pending);
        foreach (VoxelPos cell in puddle.Concat(fresh))
        {
            bool front = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }.Any(d =>
            {
                VoxelPos side = cell.Offset(d.Item1, 0, d.Item2);
                return side.X is >= 0 and < 32 && side.Z is >= 0 and < 32 && sim.Grid.AmountAt(side) == 0;
            });
            if (front)
            {
                Assert.True(pending.Contains(cell), $"{cell} is on the front and was not woken");
            }
        }

        // The front is real here — dry floor on three sides — so the water spreads before it
        // rests, and the rest is what says every raised edge cell was woken.
        AssertRestsAndTheRestIsReal(sim, total);
    }

    [Fact]
    public void TwoSeedsInOneSheetFindOneSheetAndTwoSheetsAtOneHeightStayTwo()
    {
        var sim = new FluidSimulation(Box(32, 8));
        List<VoxelPos> left = Floor(0, 13, 0, 7);
        List<VoxelPos> right = Floor(18, 31, 0, 7);
        FillFloor(sim, left, cell => 8_000 + 100 * cell.X);
        FillFloor(sim, right, cell => 4_000 + 100 * cell.Z);
        sim.Disturb(At(0, 0, 0));
        sim.Disturb(At(13, 0, 7));
        sim.Disturb(At(18, 0, 0));
        sim.Disturb(At(31, 0, 7));

        SheetLevelResult result = sim.LevelSheets(share: 4);

        Assert.Equal(2, result.Sheets);
        Assert.Equal(2, result.Levelled);
        AssertLevel(sim, left);
        AssertLevel(sim, right);
        Assert.NotEqual(sim.Grid.AmountAt(At(0, 0, 0)), sim.Grid.AmountAt(At(31, 0, 0)));
    }

    // ------------------------------------------------------------ the oracle

    [Fact]
    public void TheShareDecisionAgreesWithAFloodOverTheWholeFloorOnRandomWater()
    {
        // The reference: every sheet of the floor found whole, its unsettled cells counted by the
        // automaton's pair rule, its bordering settled cells' span taken — and the rule applied.
        // The subject must level exactly the sheets the reference says and touch none of the rest,
        // whatever its early stop did.
        var random = new Random(0x5EED_5A1E);
        for (int round = 0; round < 60; round++)
        {
            int width = random.Next(4, 25);
            int depth = random.Next(4, 25);
            int share = new[] { 0, 3, 4, 8, 16 }[random.Next(5)];
            var sim = new FluidSimulation(Box(width, depth));
            List<VoxelPos> floor = Floor(0, width - 1, 0, depth - 1);

            int level = random.Next(4_000, 14_001);
            var amounts = floor.ToDictionary(cell => cell, _ => level);
            var woken = new HashSet<VoxelPos>();

            if (random.Next(3) == 0)
            {
                // A second plateau: a step along a line, the case the share alone gets wrong.
                int split = random.Next(1, width);
                int other = Math.Clamp(level + random.Next(-3_000, 3_001), 1_200, FluidConstants.CellCapacityMillilitres);
                foreach (VoxelPos cell in floor.Where(c => c.X >= split))
                {
                    amounts[cell] = other;
                }

                for (int z = 0; z < depth; z++)
                {
                    woken.Add(At(split - 1, 0, z));
                }
            }

            int patches = random.Next(0, 4);
            for (int p = 0; p < patches; p++)
            {
                int x0 = random.Next(width), z0 = random.Next(depth);
                int x1 = Math.Min(width - 1, x0 + random.Next(0, 6)), z1 = Math.Min(depth - 1, z0 + random.Next(0, 6));
                int delta = random.Next(-2_000, 2_001);
                foreach (VoxelPos cell in Floor(x0, x1, z0, z1))
                {
                    amounts[cell] = Math.Clamp(amounts[cell] + delta, 1_200, FluidConstants.CellCapacityMillilitres);
                    woken.Add(cell);
                }
            }

            foreach ((VoxelPos cell, int amount) in amounts)
            {
                sim.Grid.Fill(cell, amount);
            }

            foreach (VoxelPos cell in woken)
            {
                sim.Disturb(cell);
            }

            long total = sim.TotalMillilitres;
            List<(HashSet<VoxelPos> Cells, bool Expected)> reference = Reference(amounts, share);
            SheetLevelResult result = sim.LevelSheets(share);

            string tag = $"round {round}, {width}x{depth}, share {share}";
            int expectedLevelled = 0;
            foreach ((HashSet<VoxelPos> cells, bool expected) in reference)
            {
                if (expected)
                {
                    expectedLevelled++;
                    AssertLevel(sim, cells);
                }
                else
                {
                    foreach (VoxelPos cell in cells)
                    {
                        Assert.True(amounts[cell] == sim.Grid.AmountAt(cell), $"{tag}: {cell} changed with no solve due");
                    }
                }
            }

            Assert.True(expectedLevelled == result.Levelled + result.Packed, $"{tag}: expected {expectedLevelled} solved, got {result.Levelled + result.Packed}");
            AssertRestsAndTheRestIsReal(sim, total);
        }
    }

    /// <summary>
    /// The whole floor is one sheet (every cell wet, supported, connected). Its unsettled cells
    /// are those with a transferable pair; its border the settled cells beside one.
    /// </summary>
    private static List<(HashSet<VoxelPos>, bool)> Reference(Dictionary<VoxelPos, int> amounts, int share)
    {
        static bool CanTransfer(int a, int b) =>
            FluidRules.HorizontalTransfer(a, b) > 0 || FluidRules.HorizontalTransfer(b, a) > 0;

        var unsettled = new HashSet<VoxelPos>();
        foreach ((VoxelPos cell, int amount) in amounts)
        {
            foreach ((int dx, int dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                if (amounts.TryGetValue(cell.Offset(dx, 0, dz), out int beside) && CanTransfer(amount, beside))
                {
                    unsettled.Add(cell);
                }
            }
        }

        int low = int.MaxValue, high = int.MinValue;
        foreach (VoxelPos cell in amounts.Keys)
        {
            if (unsettled.Contains(cell))
            {
                continue;
            }

            foreach ((int dx, int dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                if (unsettled.Contains(cell.Offset(dx, 0, dz)))
                {
                    low = Math.Min(low, amounts[cell]);
                    high = Math.Max(high, amounts[cell]);
                }
            }
        }

        bool stepped = low != int.MaxValue && high - low > FluidSimulation.StepMillilitres;
        bool worth = share == 0 || (long)unsettled.Count * share >= amounts.Count;
        bool expected = amounts.Count > 1 && unsettled.Count > 0 && (worth || stepped);
        return [(new HashSet<VoxelPos>(amounts.Keys), expected)];
    }
}
