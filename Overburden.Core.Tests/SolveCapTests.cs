using Overburden.Core.Fluid;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// M10d-3, decision 4 — Matthew's word 2026-09-24: a solve pass stops at its cap, at a height
/// boundary, and the next pass resumes from that height. The oracles: a capped pass levels exactly
/// the heights it reached and nothing above; a cap the pass never reaches is the uncapped pass to
/// the millilitre; and a carried cell the pass did not reach is offered to the next one rather
/// than lost to the carry's offered-once rule.
/// </summary>
public class SolveCapTests
{
    private const int Cap = FluidConstants.CellCapacityMillilitres;
    private static readonly int[] Heights = [0, 4, 8, 12];

    // Four separate 10 × 10 floors, each with rock under it, so every floor is a supported sheet of
    // its own and levelling one cannot unsupport another.
    private static IFluidTerrain Floors() => new PredicateTerrain(v =>
        v.X >= 0 && v.X < 10 && v.Z >= 0 && v.Z < 10 && Array.IndexOf(Heights, v.Y) >= 0);

    private static FluidSimulation Poured(int seed)
    {
        var sim = new FluidSimulation(Floors());
        var random = new Random(seed);
        foreach (int y in Heights)
        {
            for (int x = 0; x < 10; x++)
            {
                for (int z = 0; z < 10; z++)
                {
                    // Add wakes the cell, so every cell is a seed of the active set.
                    sim.Add(new VoxelPos(x, y, z), random.Next(2_000, 14_000));
                }
            }
        }

        return sim;
    }

    private static bool IsLevel(FluidSimulation sim, int y)
    {
        int lo = int.MaxValue, hi = int.MinValue;
        for (int x = 0; x < 10; x++)
        {
            for (int z = 0; z < 10; z++)
            {
                int amount = sim.Grid.AmountAt(new VoxelPos(x, y, z));
                lo = Math.Min(lo, amount);
                hi = Math.Max(hi, amount);
            }
        }

        return hi - lo <= 1;
    }

    private static int[] Snapshot(FluidSimulation sim)
    {
        var amounts = new List<int>();
        foreach (int y in Heights)
        {
            for (int x = 0; x < 10; x++)
            {
                for (int z = 0; z < 10; z++)
                {
                    amounts.Add(sim.Grid.AmountAt(new VoxelPos(x, y, z)));
                }
            }
        }

        return [.. amounts];
    }

    [Fact]
    public void ACappedPassLevelsTheHeightsItReachedAndTheNextResumesWhereItStopped()
    {
        // Each floor is 100 seeds and 100 cells: 200 items. A cap of 150 is passed inside the
        // first floor, so the pass stops at the boundary after it.
        FluidSimulation sim = Poured(seed: 1);
        int[] before = Snapshot(sim);

        for (int pass = 0; pass < Heights.Length; pass++)
        {
            SheetLevelResult r = sim.LevelSheets(share: 0, quietTicks: 0, cap: 150);
            bool last = pass == Heights.Length - 1;
            Assert.Equal(!last, r.Capped);
            Assert.Equal(last ? int.MinValue : Heights[pass + 1], sim.SolveCursorY);
            Assert.Equal(1, r.Levelled);
            Assert.Equal(300, r.SeedsDeferred);

            // Levelled up to this height; untouched above it, to the millilitre.
            int[] now = Snapshot(sim);
            for (int h = 0; h < Heights.Length; h++)
            {
                if (h <= pass)
                {
                    Assert.True(IsLevel(sim, Heights[h]), $"pass {pass}: floor {Heights[h]} is not level");
                }
                else
                {
                    Assert.Equal(before.Skip(h * 100).Take(100), now.Skip(h * 100).Take(100));
                }
            }
        }

        FluidAssert.Conserves(sim);
    }

    [Fact]
    public void ACapThePassNeverReachesIsTheUncappedPassToTheMillilitre()
    {
        FluidSimulation capped = Poured(seed: 2);
        FluidSimulation uncapped = Poured(seed: 2);

        SheetLevelResult a = capped.LevelSheets(share: 8, quietTicks: 0, cap: 1_000_000);
        SheetLevelResult b = uncapped.LevelSheets(share: 8, quietTicks: 0, cap: 0);

        Assert.False(a.Capped);
        Assert.Equal(0, a.SeedsDeferred);
        Assert.Equal(int.MinValue, capped.SolveCursorY);
        Assert.Equal(
            (b.Sheets, b.Levelled, b.Packed, b.Skipped, b.UnderShare, b.CellsVisited, b.CellsWritten, b.MillilitresMoved, b.CellsWoken),
            (a.Sheets, a.Levelled, a.Packed, a.Skipped, a.UnderShare, a.CellsVisited, a.CellsWritten, a.MillilitresMoved, a.CellsWoken));
        Assert.Equal(Snapshot(uncapped), Snapshot(capped));
        Assert.Equal(4, a.Levelled);
    }

    private const int NearX = 8, FarX = 200;

    /// <summary>The near/far fixture: a far pocket poured 25 cells deep and carried under a near tap.</summary>
    private static FluidSimulation Carried()
    {
        var terrain = new PredicateTerrain(v =>
            v.Z == 5 && v.Y >= 8 && v.Y <= 40
            && ((v.X >= NearX && v.X < NearX + 32) || (v.X >= FarX && v.X < FarX + 32)));
        var sim = new FluidSimulation(terrain);
        sim.SetNearField(new ChunkPos(0, 0, 0), 1);
        sim.FarFloorCells = 4;
        for (int i = 0; i < 200; i++)
        {
            sim.Add(new VoxelPos(FarX + (i % 8), 8 + (i / 8), 5), Cap);
        }

        for (int i = 0; i < 4; i++)
        {
            sim.Add(new VoxelPos(NearX + (i % 8), 8 + (i / 8), 5), Cap);
        }

        sim.Step();
        Assert.True(sim.FarBacklog > 0, "nothing was deferred");
        return sim;
    }

    [Fact]
    public void ACarriedCellWokenAgainAboveWhereACappedPassStoppedIsOfferedToTheNextPass()
    {
        // The re-woken half of the carry's bookkeeping: a carried cell already offered and then
        // woken again is re-listed for one solve. A capped pass that does not reach its height
        // must list it again rather than mark it offered.
        FluidSimulation sim = Carried();
        sim.LevelSheets(share: 8, quietTicks: 0, cap: 0); // every carried cell offered once

        int top = sim.DeferredCells.Max(c => c.Y);
        List<VoxelPos> high = [.. sim.DeferredCells.Where(c => c.Y >= top - 2 && sim.Grid.AmountAt(c) > 0).Take(6)];
        Assert.True(high.Count >= 3, $"only {high.Count} wet carried cell(s) near the top");
        foreach (VoxelPos cell in high)
        {
            sim.Remove(cell, 1); // a draw wakes the cell: carried and offered, so re-woken
        }

        SheetLevelResult first = sim.LevelSheets(share: 8, quietTicks: 0, cap: 1);
        Assert.True(first.Capped);
        Assert.True(sim.SolveCursorY <= top - 2, "the capped pass reached the re-woken cells");

        sim.LevelSheets(share: 8, quietTicks: 0, cap: 1);
        Assert.True(
            sim.SolveCarrySeedsLast >= high.Count,
            $"{sim.SolveCarrySeedsLast} carry seed(s) offered to the second pass, {high.Count} re-woken above the stop");
    }

    [Fact]
    public void ACursorWithNothingLeftAtOrAboveItStartsTheNextPassFromTheBottom()
    {
        // Two floors; the capped pass levels the lower and stops at the upper. The upper is then
        // emptied without a wake and a tick lets the awake set go quiet, and a pour on the lower
        // floor is all that is awake. A cursor kept at the upper floor's height would defer every
        // seed and level nothing.
        var sim = new FluidSimulation(new PredicateTerrain(v =>
            v.X >= 0 && v.X < 10 && v.Z >= 0 && v.Z < 10 && (v.Y == 0 || v.Y == 4)));
        var random = new Random(3);
        foreach (int y in new[] { 0, 4 })
        {
            for (int x = 0; x < 10; x++)
            {
                for (int z = 0; z < 10; z++)
                {
                    sim.Add(new VoxelPos(x, y, z), random.Next(2_000, 14_000));
                }
            }
        }

        Assert.True(sim.LevelSheets(share: 0, quietTicks: 0, cap: 150).Capped);
        Assert.Equal(4, sim.SolveCursorY);
        for (int x = 0; x < 10; x++)
        {
            for (int z = 0; z < 10; z++)
            {
                sim.Grid.Drain(new VoxelPos(x, 4, z), Cap);
            }
        }

        sim.Step();
        Assert.Equal(0, sim.ActiveCellCount);
        sim.Remove(new VoxelPos(5, 0, 5), 3_000);

        SheetLevelResult r = sim.LevelSheets(share: 0, quietTicks: 0, cap: 150);
        Assert.True(r.Seeds > 0);
        Assert.Equal(0, r.SeedsDeferred);
        Assert.Equal(int.MinValue, sim.SolveCursorY);
    }

    [Fact]
    public void AHandPickedSolveLeavesTheCappedCursorWhereItWas()
    {
        // The unload method levels a departing chunk's cells with a hand-picked, uncapped solve;
        // it must not send the capped passes back to the bottom.
        FluidSimulation sim = Poured(seed: 4);
        sim.LevelSheets(share: 0, quietTicks: 0, cap: 150);
        Assert.Equal(4, sim.SolveCursorY);

        sim.LevelSheets([new VoxelPos(0, 12, 0)]);
        Assert.Equal(4, sim.SolveCursorY);
    }

    [Fact]
    public void ACarriedCellAboveWhereACappedPassStoppedIsOfferedToTheNextPass()
    {
        // The carry offers a cell to the solve once (2026-09-19) and marks a prefix of the carry
        // offered by index. A capped pass that marked the whole prefix would lose every carried
        // cell above where it stopped until something woke it again — a far lake never levelled.
        FluidSimulation sim = Carried();

        SheetLevelResult first = sim.LevelSheets(share: 8, quietTicks: 0, cap: 1);
        Assert.True(first.Capped, "the fixture's seeds were all at one height");
        int stoppedAt = sim.SolveCursorY;
        int above = sim.DeferredCells.Count(c => c.Y >= stoppedAt);
        Assert.True(above > 20, $"only {above} carried cell(s) above the stop");

        sim.LevelSheets(share: 8, quietTicks: 0, cap: 1);
        Assert.True(
            sim.SolveCarrySeedsLast >= above,
            $"{sim.SolveCarrySeedsLast} carry seed(s) offered to the second pass, {above} carried above where the first stopped");
    }
}
