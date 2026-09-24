using System.Diagnostics;
using System.Globalization;
using Overburden.Core.Fluid;
using Overburden.Core.Voxels;
using Overburden.Core.Worldgen;

namespace Overburden.Bench;

/// <summary>
/// The spread rule's own arm: the relay's ball on a flat floor, and what its front looks like tick
/// by tick under each <see cref="SpreadRule"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The number the play complained about is a shape, not a rate.</b> Matthew's <i>"the amount
/// of water that spreads into empty cells is a little low"</i> is about what a pool's edge looks
/// like a few ticks after a stab of <c>R</c>: how many cells out the water reaches, and how much
/// stands in each. <c>--fluid</c> and <c>--spill</c> price a rule by ticks and visits to rest;
/// this prints the edge itself — the wet radius at each checkpoint and the profile along one
/// axis from the centre — so the arithmetic in the worklog (a fifth at a straight front is a
/// 3,125 → 625 mL skirt) is a measurement rather than a derivation.
/// </para>
/// <para>
/// <b>A flat floor on purpose, and the trap it is named for.</b> A hand-built basin makes the
/// automaton's easy case its only case, which is why every other arm runs on real terrain. Here
/// the flat floor is the question: a front's shape is only readable where the ground does not
/// shape it, and the chamber block's floor is the flat floor the play actually has (the bitten
/// mine of 2026-09-03). The block is <see cref="ChamberTerrain"/>'s, so the caves under it are
/// the generator's own, and the row says where every litre ended.
/// </para>
/// </remarks>
internal static class FrontBench
{
    private const double TicksPerSecond = 2.0;
    private static readonly int[] Checkpoints = [1, 2, 3, 5, 10, 20, 50, 100, 200, 500, 1_000, 2_000];
    private static readonly int[] ProfileTicks = [5, 20, 100];

    internal static int Run(
        WorldGenerator generator, string machineControl, bool isDebug, long litres, int tickCap, SpreadRule[] spreads,
        int fallCells = FluidSimulation.DefaultFallCells, int[]? advances = null, bool fed = false)
    {
        Console.WriteLine("Front bench — the spread rule at a pool's edge: the relay's ball on a flat floor");
        if (isDebug)
        {
            Console.WriteLine("*** DEBUG BUILD — these timings are meaningless. Re-run with -c Release. ***");
        }

        Console.WriteLine();
        Console.WriteLine(machineControl);
        Console.WriteLine();

        var example = new ChamberTerrain(generator, 0, 0, shaftVoxels: 1, depthMetres: 2, chambers: 3);
        long ml = litres * FluidConstants.MillilitresPerLitre;
        int radius = SpillBench.RadiusVoxelsFor(ml);
        Console.WriteLine(
            $"  Fixture: the floor of {example.Describe()} — {2 * example.HalfSpan} voxels square; "
            + $"{litres:N0} L in a ball of radius {radius} voxels dropped at its centre, as R drops it, "
            + $"poured at each boundary until delivered; ticked to rest or to a cap of {tickCap:N0} ticks.");
        Console.WriteLine(
            "  'wet' is cells holding anything, 'visible' cells holding a litre or more (the spread floor),"
            + " 'radius' the furthest wet cell from the centre in voxels (Chebyshev).");
        Console.WriteLine($"  Fall: {fallCells} cell(s) a visit (--fallcells; {FluidSimulation.DefaultFallCells} is the default, 1 the pre-M8d-3 arm).");
        Console.WriteLine();

        // Warm by call count on a small pour, both rules, so the first timed tick is tier-1.
        foreach (SpreadRule spread in spreads)
        {
            var warm = new FluidSimulation(example, spread: spread, fallCells: fallCells);
            warm.Add(new VoxelPos(0, example.FloorY + 2, 0), FluidConstants.CellCapacityMillilitres);
            for (int i = 0; i < 40; i++)
            {
                warm.Step();
            }
        }

        if (fed)
        {
            // Slice 2c (FRONT-ADVANCE-PLAN): a source that keeps pouring, so the pool is fed at every
            // boundary and slice 1 never advances it. The control is fed advance off; the band's
            // depth is swept at N to 6N rings (4N ships).
            int rings = advances is { Length: > 0 } ? advances[0] : SheetSettle.DefaultFrontAdvance;
            Console.WriteLine(
                $"  Fed arm: {FedLitresPerTick * TicksPerSecond:N0} L/s poured onto the floor's centre for {FedTicks} ticks "
                + $"({FedTicks / TicksPerSecond:N0} s), the solve after {SheetSettle.DefaultSettleAfterTicks} quiet tick(s), share 1 in "
                + $"{SheetSettle.DefaultSettleShare}, fed share {FluidSimulation.DefaultFedSharePercent}%, advance {rings} ring(s). "
                + "'back' counts pairs along ±X and ±Z from the centre in which the outer cell gives to the inner — water "
                + "flowing toward the source — summed over the checkpoints; the automaton's own is the control's.");
            Console.WriteLine();
            RunFedArm(generator, spreads[0], fallCells, rings, fedAdvance: false, bandPerRing: 0);
            foreach (int band in FedBands)
            {
                RunFedArm(generator, spreads[0], fallCells, rings, fedAdvance: true, bandPerRing: band);
            }

            // The stream the quiet rule exists for: a dug corridor a metre wide, fed at one end.
            Console.WriteLine(
                $"  Corridor: 4 voxels wide, {CorridorLength} long, fed at x 0..3 at the same rate; 'reach' is the furthest "
                + "wet cell along it, 'back' pairs on its centreline flowing toward the source, summed over the checkpoints.");
            Console.WriteLine();
            RunFedCorridor(spreads[0], fallCells, rings, fedAdvance: false, bandPerRing: 0);
            foreach (int band in FedBands)
            {
                RunFedCorridor(spreads[0], fallCells, rings, fedAdvance: true, bandPerRing: band);
            }

            return 0;
        }

        // Each rule twice: the automaton alone, and with M8b-3b's sheet solve at every boundary
        // (--settleafter=0's case, under inflow). The divisor is one lever on how far a pool
        // reaches in N ticks; the solve is the other, because a front advances one cell a tick at
        // most and only when the edge cell holds a divisor's worth of surplus, which a levelled
        // sheet keeps it holding. Both are printed so the play's question has both answers.
        if (advances is not null)
        {
            // The front advance's sweep (docs/FRONT-ADVANCE-PLAN.md), at the game's own quiet
            // interval rather than --settleafter=0: under the quiet rule a sheet still being poured
            // into is not levelled, so it is not advanced, exactly as in play. Advance 0 in the
            // same arm is the control.
            Console.WriteLine(
                $"  Front advance sweep: the solve after {SheetSettle.DefaultSettleAfterTicks} quiet tick(s), share 1 in "
                + $"{SheetSettle.DefaultSettleShare}, advance {string.Join(", ", advances)} ring(s) a pass.");
            Console.WriteLine();
            foreach (SpreadRule spread in spreads)
            {
                foreach (int advance in advances)
                {
                    RunArm(generator, ml, radius, tickCap, spread, solve: true, fallCells, advance);
                    Console.WriteLine();
                }
            }

            return 0;
        }

        foreach (SpreadRule spread in spreads)
        {
            RunArm(generator, ml, radius, tickCap, spread, solve: false, fallCells);
            Console.WriteLine();
            RunArm(generator, ml, radius, tickCap, spread, solve: true, fallCells);
            Console.WriteLine();
        }

        return 0;
    }

    private static void RunArm(
        WorldGenerator generator, long ml, int radius, int tickCap, SpreadRule spread, bool solve, int fallCells,
        int advance = -1)
    {
        var terrain = new ChamberTerrain(generator, 0, 0, shaftVoxels: 1, depthMetres: 2, chambers: 3);
        int half = terrain.HalfSpan;
        int floorY = terrain.FloorY;

        // Bake every column the flood can reach before anything is timed (the lake arm's rule).
        for (int x = -half - 1; x <= half; x++)
        {
            for (int z = -half - 1; z <= half; z++)
            {
                terrain.CanHold(new VoxelPos(x, floorY, z));
            }
        }

        var sim = new FluidSimulation(terrain, spread: spread, fallCells: fallCells);
        // advance < 0 is the original arm (--settleafter=0's case); advance >= 0 is the sweep's.
        SheetSettle? settle = !solve
            ? null
            : advance < 0
                ? new SheetSettle(sim, settleAfterTicks: 0, settleShare: SheetSettle.DefaultSettleShare)
                : new SheetSettle(sim, SheetSettle.DefaultSettleAfterTicks, SheetSettle.DefaultSettleShare, solveCap: 0, frontAdvance: advance);
        var at = new VoxelPos(0, floorY + 1 + radius, 0);
        long pending = ml;
        int deliveredAt = -1;
        int ticks = 0;
        long visits = 0;
        int peak = 0;
        double tickMillis = 0;
        var clock = new Stopwatch();
        var lines = new List<string>();
        int restTick = -1;

        while (ticks < tickCap)
        {
            // The game's boundary order: the settle first, then the pour, then the tick.
            settle?.AtBoundary();

            if (pending == 0 && sim.ActiveCellCount == 0)
            {
                restTick = ticks;
                break;
            }

            if (pending > 0)
            {
                pending = SpillBench.Pour(sim, at, radius, pending);
                if (pending == 0)
                {
                    deliveredAt = ticks;
                }
            }

            clock.Restart();
            FluidTickResult result = sim.Step();
            tickMillis += clock.Elapsed.TotalMilliseconds;
            ticks++;
            visits += result.CellsVisited;
            peak = Math.Max(peak, result.CellsVisited);

            if (Array.IndexOf(Checkpoints, ticks) >= 0)
            {
                (int wet, int visible, int reach) = Front(sim);
                string toPour = pending > 0
                    ? string.Create(CultureInfo.InvariantCulture, $"   ({pending / 1000.0:N0} L still to pour)")
                    : string.Empty;
                lines.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"             tick {ticks,5:N0}: active {result.ActiveAfter,6:N0}   wet {wet,6:N0}   visible {visible,6:N0}   radius {reach,3}{toPour}"));
            }

            if (Array.IndexOf(ProfileTicks, ticks) >= 0)
            {
                lines.Add(Profile(sim, floorY, half, $"profile at tick {ticks}"));
            }
        }

        bool rested = restTick >= 0;
        bool quiescent = rested && sim.VerifyQuiescent();
        bool conserved = sim.TotalMillilitres == sim.AddedMillilitres - sim.RemovedMillilitres - sim.DisplacedMillilitres;

        Console.WriteLine(
            $"  {spread.ToString().ToLowerInvariant()}{(!solve ? string.Empty : advance < 0 ? " + solve at every boundary" : $" + solve, advance {advance}")}: delivered by tick "
            + $"{(deliveredAt < 0 ? "never" : deliveredAt.ToString("N0", CultureInfo.InvariantCulture))}; "
            + (rested
                ? $"rested after {restTick:N0} ticks ({restTick / TicksPerSecond:F1} s of world time)"
                : $"HIT THE CAP at {ticks:N0} ticks with {sim.ActiveCellCount:N0} awake")
            + $"; {(quiescent ? "rest is real" : rested ? "*** STOPPED WITH MOVES AVAILABLE ***" : "not at rest")}; "
            + $"conserved {(conserved ? "exact" : "*** NO ***")}, displaced {sim.DisplacedMillilitres / 1000.0:N1} L");
        Console.WriteLine(
            $"           peak active {peak:N0}, {visits:N0} visits in all, "
            + $"{tickMillis:F1} ms of CPU ({(ticks == 0 ? 0 : tickMillis / ticks):F3} ms/tick); "
            + $"ended in {sim.Grid.HeldCellCount:N0} cell(s), {Front(sim).Visible:N0} of them visible, radius {Front(sim).Reach}");
        if (settle is not null)
        {
            Console.WriteLine(
                $"           solve: {settle.Settles:N0} pass(es), {settle.SheetsLevelled:N0} levelled / {settle.SheetsPacked:N0} packed / "
                + $"{settle.SheetsSkipped:N0} left alone / {settle.SheetsUnderShare:N0} under share, {settle.CellsVisited:N0} cells visited, "
                + $"{settle.CellsWritten:N0} written, {settle.CellsAdvanced:N0} advanced, longest pass {settle.LongestMillis:F2} ms, {settle.TotalMillis:F0} ms in all");
        }

        foreach (string line in lines)
        {
            Console.WriteLine(line);
        }

        Console.WriteLine(Profile(sim, floorY, half, rested ? "profile at rest" : "profile at the cap"));
    }

    private static readonly int[] FedBands = [1, 2, 3, 4, 6];
    private const int CorridorLength = 400;

    private static void RunFedCorridor(SpreadRule spread, int fallCells, int rings, bool fedAdvance, int bandPerRing)
    {
        // Floor at y = 1 over rock, x 0..CorridorLength−1, z 0..3; the source tops up x 0..3.
        var terrain = new CorridorTerrain();
        var sim = new FluidSimulation(terrain, spread: spread, fallCells: fallCells)
        {
            FedSharePercent = FluidSimulation.DefaultFedSharePercent,
            FedAdvance = fedAdvance,
            FedBandRingsPerAdvance = Math.Max(1, bandPerRing),
        };
        var settle = new SheetSettle(sim, SheetSettle.DefaultSettleAfterTicks, SheetSettle.DefaultSettleShare, solveCap: 0, frontAdvance: rings);

        long pending = 0, poured = 0, back = 0;
        var reaches = new List<string>();
        for (int tick = 1; tick <= FedTicks; tick++)
        {
            settle.AtBoundary();
            pending += FedLitresPerTick * FluidConstants.MillilitresPerLitre;
            for (int x = 0; x < 4 && pending > 0; x++)
            {
                for (int z = 0; z < 4 && pending > 0; z++)
                {
                    long offered = Math.Min(pending, FluidConstants.CellCapacityMillilitres);
                    long taken = offered - sim.Add(new VoxelPos(x, 1, z), offered);
                    poured += taken;
                    pending -= taken;
                }
            }

            sim.Step();
            if (Array.IndexOf(FedCheckpoints, tick) >= 0)
            {
                int reach = 0;
                foreach (VoxelPos cell in sim.Grid.HeldCells())
                {
                    reach = Math.Max(reach, cell.X);
                }

                for (int x = 0; x < reach; x++)
                {
                    if (FluidRules.HorizontalTransfer(sim.Grid.AmountAt(new VoxelPos(x + 1, 1, 1)), sim.Grid.AmountAt(new VoxelPos(x, 1, 1))) > 0)
                    {
                        back++;
                    }
                }

                reaches.Add(string.Create(CultureInfo.InvariantCulture, $"{tick}: {reach}"));
            }
        }

        var line = new List<string>();
        for (int x = 0; x < CorridorLength; x += 4)
        {
            int amount = sim.Grid.AmountAt(new VoxelPos(x, 1, 1));
            if (amount == 0)
            {
                break;
            }

            line.Add(amount.ToString(CultureInfo.InvariantCulture));
        }

        bool conserved = sim.TotalMillilitres == sim.AddedMillilitres - sim.RemovedMillilitres - sim.DisplacedMillilitres;
        string label = fedAdvance ? $"fed advance, band {bandPerRing}N" : "fed advance off (control)";
        Console.WriteLine(
            $"  {label}: poured {poured / 1000.0:N0} L; reach at tick {string.Join(", ", reaches)}; back {back:N0}; "
            + $"{settle.FedSheetsAdvanced:N0} fed advanced ({settle.FedCellsAdvanced:N0} cells, band {settle.FedBandCells:N0}), "
            + $"solve {settle.TotalMillis:F0} ms; conserved {(conserved ? "exact" : "*** NO ***")}");
        Console.WriteLine($"             every 4th cell on the centreline (mL): {string.Join(" ", line)}");
        Console.WriteLine();
    }

    private const long FedLitresPerTick = 50;
    private const int FedTicks = 120;
    private static readonly int[] FedCheckpoints = [5, 10, 20, 40, 60, 80, 100, 120];

    private static void RunFedArm(
        WorldGenerator generator, SpreadRule spread, int fallCells, int rings, bool fedAdvance, int bandPerRing)
    {
        var terrain = new ChamberTerrain(generator, 0, 0, shaftVoxels: 1, depthMetres: 2, chambers: 3);
        int half = terrain.HalfSpan;
        int floorY = terrain.FloorY;
        for (int x = -half - 1; x <= half; x++)
        {
            for (int z = -half - 1; z <= half; z++)
            {
                terrain.CanHold(new VoxelPos(x, floorY, z));
            }
        }

        var sim = new FluidSimulation(terrain, spread: spread, fallCells: fallCells)
        {
            FedSharePercent = FluidSimulation.DefaultFedSharePercent,
            FedAdvance = fedAdvance,
            FedBandRingsPerAdvance = Math.Max(1, bandPerRing),
        };
        var settle = new SheetSettle(sim, SheetSettle.DefaultSettleAfterTicks, SheetSettle.DefaultSettleShare, solveCap: 0, frontAdvance: rings);

        long pending = 0;
        long poured = 0;
        long back = 0;
        double tickMillis = 0;
        var clock = new Stopwatch();
        var radii = new List<string>();
        for (int tick = 1; tick <= FedTicks; tick++)
        {
            settle.AtBoundary();
            pending = FeedPour(sim, floorY, pending + (FedLitresPerTick * FluidConstants.MillilitresPerLitre), ref poured);

            clock.Restart();
            sim.Step();
            tickMillis += clock.Elapsed.TotalMilliseconds;

            if (Array.IndexOf(FedCheckpoints, tick) >= 0)
            {
                (int wet, int visible, int reach) = Front(sim);
                back += FlowingBack(sim, floorY, half);
                radii.Add(string.Create(CultureInfo.InvariantCulture, $"{tick}: r{reach} ({visible:N0} visible)"));
            }
        }

        bool conserved = sim.TotalMillilitres == sim.AddedMillilitres - sim.RemovedMillilitres - sim.DisplacedMillilitres;
        string label = fedAdvance ? $"fed advance, band {bandPerRing}N" : "fed advance off (control)";
        Console.WriteLine(
            $"  {label}: poured {poured / 1000.0:N0} L; radius at tick {string.Join(", ", radii)}; back {back:N0}; "
            + $"conserved {(conserved ? "exact" : "*** NO ***")}");
        Console.WriteLine(
            $"           solve {settle.Settles:N0} pass(es), {settle.SheetsLevelled:N0} levelled / {settle.SheetsNoisy:N0} left fed / "
            + $"{settle.FedSheetsAdvanced:N0} fed advanced ({settle.FedCellsAdvanced:N0} cells, band {settle.FedBandCells:N0}), "
            + $"{settle.CellsWritten:N0} written, {settle.TotalMillis:F0} ms in all, longest {settle.LongestMillis:F2}; "
            + $"ticks {tickMillis:F0} ms; inflow tested 10%+ {settle.InflowTenPercentPlus:N0}, 1-10% {settle.InflowUnderTenPercent:N0}, "
            + $"<1% {settle.InflowUnderOnePercent + settle.InflowUnderTenthPercent:N0}");
        Console.WriteLine(Profile(sim, floorY, half, "profile at the end"));
        Console.WriteLine();
    }

    /// <summary>
    /// A 3 × 3 source over the floor's centre: each cell topped up from the floor upward, so the
    /// water lands in the floor's own chunk and stamps it (the fed test reads the chunk). Returns
    /// what did not fit, carried to the next tick.
    /// </summary>
    private static long FeedPour(FluidSimulation sim, int floorY, long millilitres, ref long poured)
    {
        for (int dy = 0; dy < 4 && millilitres > 0; dy++)
        {
            for (int dx = -1; dx <= 1 && millilitres > 0; dx++)
            {
                for (int dz = -1; dz <= 1 && millilitres > 0; dz++)
                {
                    var cell = new VoxelPos(dx, floorY + dy, dz);
                    if (cell.Chunk != new VoxelPos(0, floorY, 0).Chunk)
                    {
                        continue;
                    }

                    long offered = Math.Min(millilitres, FluidConstants.CellCapacityMillilitres);
                    long left = sim.Add(cell, offered);
                    poured += offered - left;
                    millilitres -= offered - left;
                }
            }
        }

        return millilitres;
    }

    /// <summary>Pairs along the four axes from the centre in which the outer cell gives to the inner one.</summary>
    private static int FlowingBack(FluidSimulation sim, int floorY, int half)
    {
        int count = 0;
        foreach ((int dx, int dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
        {
            for (int k = 1; k < half - 1; k++)
            {
                int inner = sim.Grid.AmountAt(new VoxelPos(dx * k, floorY, dz * k));
                int outer = sim.Grid.AmountAt(new VoxelPos(dx * (k + 1), floorY, dz * (k + 1)));
                if (FluidRules.HorizontalTransfer(outer, inner) > 0)
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>Wet cells, cells at or over the spread floor, and the furthest wet cell from the centre.</summary>
    private static (int Wet, int Visible, int Reach) Front(FluidSimulation sim)
    {
        int wet = 0;
        int visible = 0;
        int reach = 0;
        foreach (VoxelPos cell in sim.Grid.HeldCells())
        {
            wet++;
            if (sim.Grid.AmountAt(cell) >= FluidConstants.MinimumSpreadMillilitres)
            {
                visible++;
            }

            reach = Math.Max(reach, Math.Max(Math.Abs(cell.X), Math.Abs(cell.Z)));
        }

        return (wet, visible, reach);
    }

    /// <summary>The floor's contents along +X from the centre, in millilitres, to two cells past the last wet one.</summary>
    private static string Profile(FluidSimulation sim, int floorY, int half, string label)
    {
        var amounts = new List<int>();
        int lastWet = -1;
        for (int x = 0; x < half; x++)
        {
            int amount = sim.Grid.AmountAt(new VoxelPos(x, floorY, 0));
            amounts.Add(amount);
            if (amount > 0)
            {
                lastWet = x;
            }
        }

        int shown = Math.Min(amounts.Count, lastWet + 3);
        var parts = new List<string>();
        for (int x = 0; x < shown; x++)
        {
            parts.Add(amounts[x].ToString(CultureInfo.InvariantCulture));
        }

        // The layer above the floor along the same line, so a mound reads as a mound.
        var above = new List<string>();
        int lastAbove = -1;
        for (int x = 0; x < shown; x++)
        {
            int amount = sim.Grid.AmountAt(new VoxelPos(x, floorY + 1, 0));
            above.Add(amount.ToString(CultureInfo.InvariantCulture));
            if (amount > 0)
            {
                lastAbove = x;
            }
        }

        string second = lastAbove < 0
            ? string.Empty
            : Environment.NewLine + $"                 one up: {string.Join(" ", above.Take(lastAbove + 1))}";
        return $"             {label}, +X from the centre (mL): {string.Join(" ", parts)}{second}";
    }
}

/// <summary>The fed arm's corridor: a floor at y = 1, a metre wide, over rock.</summary>
internal sealed class CorridorTerrain : IFluidTerrain
{
    public bool CanHold(VoxelPos v) => v.Y == 1 && v.X >= 0 && v.X < 400 && v.Z >= 0 && v.Z < 4;
}
