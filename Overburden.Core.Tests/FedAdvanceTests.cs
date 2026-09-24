using Overburden.Core.Fluid;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// The front advance's slice 2c (docs/FRONT-ADVANCE-PLAN.md, "Slice 2c — the planning pass"): a sheet
/// still being fed takes the same rings as a levelled one and levels only its tip — the band of its
/// own cells nearest its dry edge — so its body keeps its gradient. The fixture is the pass's own
/// worked channel: one cell wide along X at z = 5, y = 1, from 10 L at x = 0 down to 1 L at x = 9,
/// put down asleep through the grid; the stream is fed from a shaft at x = −1 whose water is falling
/// (its cell has room below), so x = 0 is a front beside falling water and x = 9 is the dry edge.
/// </summary>
public class FedAdvanceTests
{
    private const int Z = 5;

    // Eight cells in, so the shaft at x = −1 is in the channel's chunk and its pour stamps it.
    private const int X0 = 8;

    private static VoxelPos At(int x, int y = 1) => new(X0 + x, y, Z);

    // The channel floor: y = 1, z = 5, x 0..19 over rock; and the shaft beside it at x = −1, open at
    // y = 1 and y = 0, so water poured into (−1, 1) is falling and never part of the sheet.
    private static bool Channel(VoxelPos v) =>
        v.Z == Z && ((v.Y == 1 && v.X - X0 is >= 0 and < 20) || (v.X - X0 == -1 && v.Y is 0 or 1));

    /// <summary>The stream: <paramref name="profile"/> from x = 0, asleep, and the source falling beside it. The band is the pass's worked 2N, not the shipped depth, so the arithmetic stays the plan's.</summary>
    private static FluidSimulation Stream(int[] profile, int source = 6_000, int bandPerRing = 2, bool fedAdvance = true, Func<VoxelPos, bool>? terrain = null)
    {
        var sim = new FluidSimulation(new PredicateTerrain(terrain ?? Channel))
        {
            FedSharePercent = 1.0,
            FedAdvance = fedAdvance,
            FedBandRingsPerAdvance = bandPerRing,
        };

        for (int x = 0; x < profile.Length; x++)
        {
            sim.Grid.Fill(At(x), profile[x]);
        }

        if (source > 0)
        {
            // The inflow: stamps the chunk with its volume, a tenth of the stream and more.
            Assert.Equal(0L, sim.Add(At(-1), source));
        }

        return sim;
    }

    // 10 L falling to 1 L over ten cells: 55 L.
    private static readonly int[] Taper = [10_000, 9_000, 8_000, 7_000, 6_000, 5_000, 4_000, 3_000, 2_000, 1_000];

    private static List<VoxelPos> Seeds(int length) => Enumerable.Range(0, length).Select(x => At(x)).ToList();

    private static Dictionary<VoxelPos, int> Snapshot(FluidSimulation sim) =>
        sim.Grid.HeldCells().ToDictionary(c => c, c => sim.Grid.AmountAt(c));

    private static SheetLevelResult Solve(FluidSimulation sim, int length = 10, int advance = 4) =>
        sim.LevelSheets(Seeds(length), share: 0, quietTicks: 2, advance: advance);

    [Fact]
    public void AFedStreamLevelsItsTipAndLeavesItsBodyToTheMillilitre()
    {
        // The pass's worked example: D = 2N = 8 rings from the dry edge is x 2..9, 36 L; four rings
        // are taken (36 L covers twelve cells at 2 L), and the tip stands at 3 L behind a 9 L cell.
        FluidSimulation sim = Stream(Taper);
        long before = sim.TotalMillilitres;

        SheetLevelResult r = Solve(sim);

        Assert.Equal(1, r.FedAdvanced);
        Assert.Equal(4, r.FedAdvancedCells);
        Assert.Equal(8, r.FedBandCells);
        Assert.Equal(0, r.Noisy);
        Assert.Equal(0, r.Levelled);

        Assert.Equal(10_000, sim.Grid.AmountAt(At(0)));
        Assert.Equal(9_000, sim.Grid.AmountAt(At(1)));
        for (int x = 2; x <= 13; x++)
        {
            Assert.Equal(3_000, sim.Grid.AmountAt(At(x)));
        }

        Assert.Equal(0, sim.Grid.AmountAt(At(14)));
        Assert.Equal(before, sim.TotalMillilitres);
        Assert.Equal(6_000, sim.Grid.AmountAt(At(-1))); // the falling source, another Y's business
    }

    [Fact]
    public void TheBandsRemainderGoesAMillilitreACellAndNoneIsLost()
    {
        // 36,003 mL over twelve cells: 3,000 each and three of them 3,001 — every fixture above divides
        // exactly, which would leave the remainder's rule untested.
        int[] profile = (int[])Taper.Clone();
        profile[9] = 1_003;
        FluidSimulation sim = Stream(profile);
        long before = sim.TotalMillilitres;

        Solve(sim);

        int[] tip = Enumerable.Range(2, 12).Select(x => sim.Grid.AmountAt(At(x))).ToArray();
        Assert.All(tip, a => Assert.InRange(a, 3_000, 3_001));
        Assert.Equal(3, tip.Count(a => a == 3_001));
        Assert.Equal(before, sim.TotalMillilitres);
    }

    [Fact]
    public void NoPairFlowsTowardTheSourceAfterThePass()
    {
        // The defect pro rata would have had (the pass's fact 1): a ridge ahead of the old edge,
        // water flowing back toward the source. Down the channel, nothing may give to its upstream
        // neighbour.
        FluidSimulation sim = Stream(Taper);
        Solve(sim);

        for (int x = 0; x < 19; x++)
        {
            int upstream = sim.Grid.AmountAt(At(x));
            int downstream = sim.Grid.AmountAt(At(x + 1));
            Assert.True(
                FluidRules.HorizontalTransfer(downstream, upstream) == 0,
                $"x {x + 1} ({downstream} mL) flows back into x {x} ({upstream} mL)");
        }
    }

    [Fact]
    public void AShallowBandTakesOnlyTheRingsItsWaterCovers()
    {
        // D = N = 4: x 6..9 hold 10 L, which covers the band and one new cell at 2 L exactly and not
        // two — the floor's boundary, taken on the equality.
        FluidSimulation sim = Stream(Taper, bandPerRing: 1);

        SheetLevelResult r = Solve(sim);

        Assert.Equal(1, r.FedAdvancedCells);
        Assert.Equal(4, r.FedBandCells);
        for (int x = 6; x <= 10; x++)
        {
            Assert.Equal(2_000, sim.Grid.AmountAt(At(x)));
        }

        Assert.Equal(5_000, sim.Grid.AmountAt(At(5)));
        Assert.Equal(0, sim.Grid.AmountAt(At(11)));
    }

    [Fact]
    public void ABandTooThinForOneRingWritesNothing()
    {
        // Every cell 1.5 L: the band's water never covers band + 1 at 2 L, so the sheet is left fed.
        int[] thin = Enumerable.Repeat(1_500, 10).ToArray();
        thin[0] = 3_000; // a move inside it, so the pass judges it at all
        FluidSimulation sim = Stream(thin);
        Dictionary<VoxelPos, int> before = Snapshot(sim);

        SheetLevelResult r = Solve(sim);

        Assert.Equal(0, r.FedAdvanced);
        Assert.Equal(1, r.Noisy);
        Assert.Equal(before, Snapshot(sim));
    }

    [Fact]
    public void AFrontBesideFallingWaterDoesNotPullTheSourceIntoTheBand()
    {
        // x = 0 is a front (its side at x = −1 is holdable and not the sheet's) but not a dry edge.
        // Were the band measured from every front, x 0..7 would join it from that end and the whole
        // stream would be flattened; the body's first two cells are the witness.
        FluidSimulation sim = Stream(Taper);
        Solve(sim);

        Assert.Equal(10_000, sim.Grid.AmountAt(At(0)));
        Assert.Equal(9_000, sim.Grid.AmountAt(At(1)));
    }

    [Fact]
    public void AFedAdvanceListsNothingAsRestingAndLeavesNoSleepingCellWithAMove()
    {
        // The trap the pass exists for: ListRested and WakeAfterSolve both assume a level sheet. The
        // stream was put down asleep, so the body's x = 1 — bordering the band, thicker than it — has
        // a move only this pass can wake; x = 0 was woken by the source's pour beside it.
        FluidSimulation sim = Stream(Taper);
        Solve(sim);

        Assert.Empty(sim.Rested);
        (VoxelPos Cell, string Why)? stranded = sim.FirstSleepingCellWithAMove();
        Assert.True(stranded is null, $"asleep with a move after the pass: {stranded?.Cell} — {stranded?.Why}");
    }

    [Fact]
    public void WithTheSourceStoppedTheStreamSettlesAndRests()
    {
        FluidSimulation sim = Stream(Taper);
        long before = sim.TotalMillilitres;
        Solve(sim);

        FluidAssert.SettlesAndRests(sim);
        Assert.Equal(before, sim.TotalMillilitres);
    }

    [Fact]
    public void OffIsTodaysPassToTheMillilitre()
    {
        // FedAdvance false: the fed sheet is left, as before 2c. And an advance of zero with it on.
        FluidSimulation off = Stream(Taper, fedAdvance: false);
        Dictionary<VoxelPos, int> before = Snapshot(off);
        SheetLevelResult r = Solve(off);
        Assert.Equal(1, r.Noisy);
        Assert.Equal(0, r.FedAdvanced);
        Assert.Equal(before, Snapshot(off));

        FluidSimulation noRings = Stream(Taper);
        before = Snapshot(noRings);
        Assert.Equal(1, Solve(noRings, advance: 0).Noisy);
        Assert.Equal(before, Snapshot(noRings));
    }

    [Fact]
    public void AnUnfedSheetIsLevelledTheSameWithTheFlagOnOrOff()
    {
        // No source: the stream is not fed and slice 1's level and rings apply, untouched by 2c.
        FluidSimulation on = Stream(Taper, source: 0);
        FluidSimulation off = Stream(Taper, source: 0, fedAdvance: false);

        SheetLevelResult a = Solve(on);
        SheetLevelResult b = Solve(off);

        Assert.Equal(1, a.Levelled);
        Assert.Equal(0, a.FedAdvanced);
        Assert.Equal(b.Advanced, a.Advanced);
        Assert.Equal(Snapshot(off), Snapshot(on));
    }

    [Fact]
    public void ADisturbedSheetIsNotAdvanced()
    {
        // An edit under water is fed whatever its volume; it is terrain changing, not a stream.
        FluidSimulation sim = Stream(Taper, source: 0);
        sim.Disturb(At(4));
        Dictionary<VoxelPos, int> before = Snapshot(sim);

        SheetLevelResult r = Solve(sim);

        Assert.Equal(1, r.InflowDisturbed);
        Assert.Equal(0, r.FedAdvanced);
        Assert.Equal(1, r.Noisy);
        Assert.Equal(before, Snapshot(sim));
    }

    [Fact]
    public void AFedSheetShorterThanItsBandIsLevelledWhole()
    {
        // Stated behaviour, pinned: four cells, all within eight rings of the dry edge — the band is
        // the whole sheet, which is levelled with its four new cells (20 L over eight cells).
        FluidSimulation sim = Stream([8_000, 6_000, 4_000, 2_000], source: 3_000);

        SheetLevelResult r = Solve(sim, length: 4);

        Assert.Equal(1, r.FedAdvanced);
        Assert.Equal(4, r.FedBandCells);
        Assert.Equal(4, r.FedAdvancedCells);
        for (int x = 0; x < 8; x++)
        {
            Assert.Equal(2_500, sim.Grid.AmountAt(At(x)));
        }
    }

    [Fact]
    public void FallingWaterAsleepBesideTheTipIsWoken()
    {
        // A short fed sheet is all band, so its x = 0 is written; beside it the shaft holds water put
        // down asleep with room below it — a move the band's write did not make but must not strand.
        // Under a litre, so it cannot spread and falling is its only move (a spreadable amount hid
        // the room-below rule behind the sideways one). The inflow is poured into a pocket elsewhere
        // in the chunk, because a pour wakes its neighbours and one at the shaft's foot woke it.
        VoxelPos pocket = new(X0, 1, Z + 10);
        FluidSimulation sim = Stream([8_000, 6_000, 4_000, 2_000], source: 0, terrain: v => Channel(v) || v == pocket);
        sim.Grid.Fill(At(-1), 800);
        Assert.Equal(0L, sim.Add(pocket, 3_000));

        SheetLevelResult r = Solve(sim, length: 4);

        Assert.Equal(1, r.FedAdvanced);
        (VoxelPos Cell, string Why)? stranded = sim.FirstSleepingCellWithAMove();
        Assert.True(stranded is null, $"asleep with a move after the pass: {stranded?.Cell} — {stranded?.Why}");
    }

    [Fact]
    public void WaterStandingOnABandCellIsWokenWhenTheCellIsDrawnDown()
    {
        // x = 2 is full, so the cell over it holds water that stands; the band draws x = 2 down to its
        // level, and the water over it now has room to fall. Nothing else would wake it.
        VoxelPos loft = At(2, y: 2);
        int[] profile = [10_000, 15_625, 15_625, 7_000, 6_000, 5_000, 4_000, 3_000, 2_000, 1_000];
        FluidSimulation sim = Stream(profile, terrain: v => Channel(v) || v == loft);
        sim.Grid.Fill(loft, 5_000);

        SheetLevelResult r = Solve(sim);

        Assert.Equal(1, r.FedAdvanced);
        Assert.True(sim.Grid.AmountAt(At(2)) < 15_625, "the fixture's band did not draw x = 2 down");
        (VoxelPos Cell, string Why)? stranded = sim.FirstSleepingCellWithAMove();
        Assert.True(stranded is null, $"asleep with a move after the pass: {stranded?.Cell} — {stranded?.Why}");
    }
}
