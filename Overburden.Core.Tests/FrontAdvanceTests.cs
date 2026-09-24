using Overburden.Core.Fluid;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// The front advance (docs/FRONT-ADVANCE-PLAN.md): a sheet the solve levels takes up to N whole
/// rings of dry, holdable, supported cells at its Y. The oracles are the definition checked by a
/// brute-force flood that shares no code with the subject, conservation with every other layer
/// unchanged, and the automaton's own quiescence once the water settles — the proof that the new
/// edge was woken.
/// </summary>
public class FrontAdvanceTests
{
    private const int Cap = FluidConstants.CellCapacityMillilitres;

    private static VoxelPos At(int x, int y, int z) => new(x, y, z);

    // A 40 × 40 floor at y = 1 over rock at y = 0, with a pit (open to y = 0) at x 30..31 and a
    // rock pillar at (20..21, 20..21): a lip and an obstacle inside the floor.
    private static bool Floor(VoxelPos v)
    {
        bool inFloor = v.X >= 0 && v.X < 40 && v.Z >= 0 && v.Z < 40;
        if (!inFloor)
        {
            return false;
        }

        if (v.X is >= 20 and <= 21 && v.Z is >= 20 and <= 21)
        {
            return false;
        }

        if (v.X is >= 30 and <= 31)
        {
            return v.Y is 0 or 1;
        }

        return v.Y == 1;
    }

    private static FluidSimulation Puddle(int x0, int z0, int side, int millilitres, int seed = 1)
    {
        var sim = new FluidSimulation(new PredicateTerrain(Floor));
        var random = new Random(seed);
        for (int x = x0; x < x0 + side; x++)
        {
            for (int z = z0; z < z0 + side; z++)
            {
                // Unlevel, so the pass levels it; Add wakes every cell, so every cell is a seed.
                sim.Add(At(x, 1, z), millilitres + random.Next(-1_500, 1_500));
            }
        }

        return sim;
    }

    private static Dictionary<VoxelPos, int> Wet(FluidSimulation sim)
    {
        var wet = new Dictionary<VoxelPos, int>();
        for (int x = -1; x <= 40; x++)
        {
            for (int z = -1; z <= 40; z++)
            {
                for (int y = 0; y <= 2; y++)
                {
                    int amount = sim.Grid.AmountAt(At(x, y, z));
                    if (amount > 0)
                    {
                        wet[At(x, y, z)] = amount;
                    }
                }
            }
        }

        return wet;
    }

    /// <summary>
    /// The definition, by a flood that is not the subject's: cells at y = 1, holdable, supported
    /// (rock under, or a full cell), within <paramref name="rings"/> sideways steps of the pre-pass
    /// wet cells through such cells.
    /// </summary>
    private static HashSet<VoxelPos> Reachable(FluidSimulation sim, IEnumerable<VoxelPos> from, int rings)
    {
        bool Ok(VoxelPos v) => Floor(v) && (!Floor(At(v.X, v.Y - 1, v.Z)) || sim.Grid.AmountAt(At(v.X, v.Y - 1, v.Z)) == Cap);
        var seen = new HashSet<VoxelPos>(from);
        var frontier = new List<VoxelPos>(from);
        for (int r = 0; r < rings; r++)
        {
            var next = new List<VoxelPos>();
            foreach (VoxelPos c in frontier)
            {
                foreach ((int dx, int dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    VoxelPos s = At(c.X + dx, c.Y, c.Z + dz);
                    if (Ok(s) && seen.Add(s))
                    {
                        next.Add(s);
                    }
                }
            }

            frontier = next;
        }

        return seen;
    }

    [Fact]
    public void AdvanceZeroIsTodaysPassToTheMillilitre()
    {
        FluidSimulation a = Puddle(5, 5, 6, 9_000);
        FluidSimulation b = Puddle(5, 5, 6, 9_000);

        SheetLevelResult ra = a.LevelSheets(share: 8, quietTicks: 0);
        SheetLevelResult rb = b.LevelSheets(share: 8, quietTicks: 0, cap: 0, advance: 0);

        Assert.Equal(0, rb.Advanced);
        Assert.Equal((ra.Sheets, ra.Levelled, ra.CellsVisited, ra.CellsWritten, ra.CellsWoken),
                     (rb.Sheets, rb.Levelled, rb.CellsVisited, rb.CellsWritten, rb.CellsWoken));
        Assert.Equal(Wet(a), Wet(b));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void EveryNewlyWetCellIsWithinNStepsOnSupportedFloorAndNothingCrossesAY(int rings)
    {
        FluidSimulation sim = Puddle(10, 10, 6, 12_000);
        Dictionary<VoxelPos, int> before = Wet(sim);
        long total = sim.TotalMillilitres;

        SheetLevelResult r = sim.LevelSheets(share: 8, quietTicks: 0, cap: 0, advance: rings);
        Dictionary<VoxelPos, int> after = Wet(sim);

        Assert.True(r.Advanced > 0, "nothing advanced");
        Assert.Equal(total, sim.TotalMillilitres);
        FluidAssert.Conserves(sim);
        HashSet<VoxelPos> allowed = Reachable(sim, before.Keys, rings);
        int newlyWet = 0;
        foreach (VoxelPos cell in after.Keys.Where(c => !before.ContainsKey(c)))
        {
            newlyWet++;
            Assert.Equal(1, cell.Y);
            Assert.Contains(cell, allowed);
        }

        Assert.Equal(r.Advanced, newlyWet);

        // Whole rings, sideways steps only: a 6 × 6 square's first ring is its 24 edge-adjacent
        // cells (a corner is two steps away), and each ring after is four longer — so N rings are
        // 24N + 2N(N − 1), cut only by what the floor refuses (nothing, this far from the pit).
        Assert.Equal((24 * rings) + (2 * rings * (rings - 1)), r.Advanced);
    }

    [Fact]
    public void AnAdvanceThatGrowsThePassPastItsRecordArraysIsWrittenIntoTheGrownOnes()
    {
        // The pass's per-cell records start at 256 and double. A 10 x 10 puddle is 100 cells and its
        // four rings are 184 more: the advance itself crosses 256, which the bench found and no
        // fixture above reached (an array read before the call that grew it).
        FluidSimulation sim = Puddle(5, 5, 10, 14_000);
        long total = sim.TotalMillilitres;
        SheetLevelResult r = sim.LevelSheets(share: 8, quietTicks: 0, cap: 0, advance: 4);
        Assert.Equal(184, r.Advanced);
        Assert.Equal(total, sim.TotalMillilitres);
        Assert.True(sim.Settle().Settled);
        Assert.True(sim.VerifyQuiescent(), $"a move was stranded: {sim.FirstCellWithAMove()}");
    }

    [Fact]
    public void ALipStopsARingAndTheCellAtTheLipIsWoken()
    {
        // The puddle's east edge is at x = 29; the pit starts at x = 30 with open air under it.
        FluidSimulation sim = Puddle(24, 10, 6, 12_000);
        sim.LevelSheets(share: 8, quietTicks: 0, cap: 0, advance: 4);

        for (int z = 0; z < 40; z++)
        {
            Assert.Equal(0, sim.Grid.AmountAt(At(30, 1, z)));
            Assert.Equal(0, sim.Grid.AmountAt(At(31, 1, z)));
        }

        var pending = new HashSet<VoxelPos>(sim.Pending);
        Assert.Contains(At(29, 1, 12), pending);
        Assert.True(sim.Grid.AmountAt(At(29, 1, 12)) > 0);
    }

    [Fact]
    public void TheAdvanceStopsBeforeTheShareFallsUnderTwoLitresACell()
    {
        // A 4 × 4 puddle's rings are 16, 20, 24, 28 cells; a ring is taken only while the total still
        // covers every cell at 2 L. The expectation is that rule, computed from the pour's own total.
        static int Expected(long total)
        {
            int cells = 16, added = 0;
            foreach (int ring in new[] { 16, 20, 24, 28 })
            {
                if (total < (long)FluidSimulation.AdvanceFloorMillilitres * (cells + ring))
                {
                    break;
                }

                cells += ring;
                added += ring;
            }

            return added;
        }

        FluidSimulation thin = Puddle(10, 10, 4, 3_000, seed: 2);
        Assert.Equal(0, Expected(thin.TotalMillilitres));
        Assert.Equal(0, thin.LevelSheets(share: 8, quietTicks: 0, cap: 0, advance: 4).Advanced);

        FluidSimulation more = Puddle(10, 10, 4, 9_000, seed: 2);
        int expected = Expected(more.TotalMillilitres);
        Assert.InRange(expected, 16, 60); // one or two rings, never all four: the stop is what is tested
        SheetLevelResult r = more.LevelSheets(share: 8, quietTicks: 0, cap: 0, advance: 4);
        Assert.Equal(expected, r.Advanced);
        Assert.All(Wet(more).Values, amount => Assert.True(amount >= FluidSimulation.AdvanceFloorMillilitres, $"{amount} mL"));
    }

    [Fact]
    public void TheNewEdgeIsWokenSoTheWaterSettlesWithNoMoveStranded()
    {
        FluidSimulation sim = Puddle(10, 10, 6, 14_000);
        SheetLevelResult r = sim.LevelSheets(share: 8, quietTicks: 0, cap: 0, advance: 4);
        Assert.True(r.Advanced > 0);

        FluidSettleResult settled = sim.Settle();
        Assert.True(settled.Settled, "the automaton did not come to rest");
        Assert.True(sim.VerifyQuiescent(), $"a move was stranded: {sim.FirstCellWithAMove()}");
        FluidAssert.Conserves(sim);
    }

    [Fact]
    public void AFrozenSheetAndAFedSheetAreNotAdvanced()
    {
        // Frozen: a 20 × 20 floor filled level and asleep, one cell disturbed — far under one cell
        // in eight unsettled.
        var frozen = new FluidSimulation(new PredicateTerrain(Floor));
        for (int x = 2; x < 22; x++)
        {
            for (int z = 2; z < 18; z++)
            {
                frozen.Grid.Fill(At(x, 1, z), 8_000);
            }
        }

        frozen.Add(At(10, 1, 10), 3_000);
        Assert.Equal(0, frozen.LevelSheets(share: 8, quietTicks: 0, cap: 0, advance: 4).Advanced);

        // Fed: a puddle poured this tick, with the quiet rule asking for two ticks without inflow.
        FluidSimulation fed = Puddle(10, 10, 6, 12_000);
        SheetLevelResult r = fed.LevelSheets(share: 8, quietTicks: 2, cap: 0, advance: 4);
        Assert.True(r.Noisy > 0);
        Assert.Equal(0, r.Advanced);
    }

    [Fact]
    public void TwoSheetsAcrossAGapMeetTheSameWayWhateverOrderTheyWereWokenIn()
    {
        Dictionary<VoxelPos, int> Run(bool reversed)
        {
            var sim = new FluidSimulation(new PredicateTerrain(Floor));
            var cells = new List<(VoxelPos, int)>();
            var random = new Random(9);
            foreach (int x0 in new[] { 4, 13 })
            {
                for (int x = x0; x < x0 + 5; x++)
                {
                    for (int z = 4; z < 9; z++)
                    {
                        cells.Add((At(x, 1, z), 10_000 + random.Next(-1_500, 1_500)));
                    }
                }
            }

            if (reversed)
            {
                cells.Reverse();
            }

            foreach ((VoxelPos cell, int ml) in cells)
            {
                sim.Add(cell, ml);
            }

            SheetLevelResult r = sim.LevelSheets(share: 8, quietTicks: 0, cap: 0, advance: 4);
            Assert.True(r.Advanced > 0);
            FluidAssert.Conserves(sim);
            return Wet(sim);
        }

        Dictionary<VoxelPos, int> a = Run(reversed: false);
        Assert.Equal(a, Run(reversed: true));

        // The four-cell gap between them (x 9..12) is wet: claimed from one side or both.
        for (int x = 9; x <= 12; x++)
        {
            Assert.True(a.ContainsKey(At(x, 1, 6)), $"gap cell {x} is dry");
        }
    }

    [Fact]
    public void ThePassCapCountsTheCellsTheAdvanceAdded()
    {
        // Two floors: y = 1 (the fixture's) and a sealed one at y = 5. Add wakes each poured cell's
        // neighbours too, so the lower floor offers some dozens of seeds; a cap of 60 is past what
        // its seeds and cells reach alone (the control arm says so) and inside what they reach with
        // the advance's cells counted.
        FluidSimulation Build()
        {
            var sim = new FluidSimulation(new PredicateTerrain(v => Floor(v) || (v.Y == 5 && v.X is >= 0 and < 3 && v.Z is >= 0 and < 3)));
            for (int x = 10; x < 13; x++)
            {
                for (int z = 10; z < 13; z++)
                {
                    sim.Add(At(x, 1, z), 14_000 - (100 * x));
                }
            }

            for (int x = 0; x < 3; x++)
            {
                for (int z = 0; z < 3; z++)
                {
                    sim.Add(At(x, 5, z), 5_000 + (100 * z));
                }
            }

            return sim;
        }

        Assert.False(Build().LevelSheets(share: 8, quietTicks: 0, cap: 60, advance: 0).Capped, "the cap is reached without the advance");

        FluidSimulation sim = Build();
        SheetLevelResult r = sim.LevelSheets(share: 8, quietTicks: 0, cap: 60, advance: 4);
        Assert.True(r.Advanced > 12, $"only {r.Advanced} advanced");
        Assert.True(r.Capped);
        Assert.Equal(5, sim.SolveCursorY);
    }

    [Fact]
    public void ASleepingPuddleBesideTheRingKeepsItsWaterAndMeetsTheEdgeThroughTheTick()
    {
        // A puddle nothing woke is not a sheet this pass found, so it is not in the index; it is not
        // dry either, and a ring does not take it. The advancing edge stops beside it and is woken,
        // and the automaton evens the two out.
        FluidSimulation sim = Puddle(10, 10, 4, 12_000);
        var sleeping = new List<VoxelPos>();
        for (int x = 16; x < 18; x++)
        {
            for (int z = 10; z < 14; z++)
            {
                sim.Grid.Fill(At(x, 1, z), 3_000);
                sleeping.Add(At(x, 1, z));
            }
        }

        long total = sim.TotalMillilitres; // Grid.Fill is behind the accounting, so the total is the oracle here
        SheetLevelResult r = sim.LevelSheets(share: 8, quietTicks: 0, cap: 0, advance: 4);
        Assert.True(r.Advanced > 0);
        Assert.All(sleeping, c => Assert.Equal(3_000, sim.Grid.AmountAt(c)));
        Assert.True(sim.Grid.AmountAt(At(15, 1, 11)) > 0, "the ring did not reach the sleeping puddle");
        Assert.Contains(At(15, 1, 11), new HashSet<VoxelPos>(sim.Pending));

        Assert.True(sim.Settle().Settled);
        Assert.True(sim.VerifyQuiescent(), $"a move was stranded: {sim.FirstCellWithAMove()}");
        Assert.Equal(total, sim.TotalMillilitres);
    }

    [Fact]
    public void TwoSheetsThatMeetInACorridorAtDifferentLevelsAreWokenAtTheSeam()
    {
        // A one-cell-wide corridor closed at both ends (x 0..15), so a seam between two sheets has
        // no other front for the tick's own wake cascade to start from; and the water put down
        // asleep, solved from hand-named seeds, so the solve's wake set is all that is awake. The
        // deep sheet's ring takes the gap cell beside the shallow sheet, and unless that cell and
        // the shallow sheet's edge are fronts of each other, the difference between them is a move
        // nothing is awake to make.
        var sim = new FluidSimulation(new PredicateTerrain(v => v.Y == 1 && v.Z == 5 && v.X >= 0 && v.X <= 15));
        var seeds = new List<VoxelPos>();
        for (int x = 4; x <= 8; x++)
        {
            sim.Grid.Fill(At(x, 1, 5), 12_000 + (x * 10));
            seeds.Add(At(x, 1, 5));
        }

        for (int x = 10; x <= 14; x++)
        {
            sim.Grid.Fill(At(x, 1, 5), 5_000 + (x * 10));
            seeds.Add(At(x, 1, 5));
        }

        long total = sim.TotalMillilitres;
        SheetLevelResult r = sim.LevelSheets(seeds, share: 0, quietTicks: 0, advance: 4);
        Assert.True(r.Advanced > 4);
        Assert.True(sim.Grid.AmountAt(At(9, 1, 5)) > 0, "the gap was not claimed");
        Assert.True(sim.Grid.AmountAt(At(15, 1, 5)) > 0, "the shallow sheet did not advance, so its edge was never recomputed");

        Assert.True(sim.Settle().Settled);
        Assert.True(sim.VerifyQuiescent(), $"a move was stranded: {sim.FirstCellWithAMove()}");
        Assert.Equal(total, sim.TotalMillilitres);
    }
}
