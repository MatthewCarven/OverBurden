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
        WorldGenerator generator, string machineControl, bool isDebug, long litres, int tickCap, SpreadRule[] spreads)
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
        Console.WriteLine();

        // Warm by call count on a small pour, both rules, so the first timed tick is tier-1.
        foreach (SpreadRule spread in spreads)
        {
            var warm = new FluidSimulation(example, spread: spread);
            warm.Add(new VoxelPos(0, example.FloorY + 2, 0), FluidConstants.CellCapacityMillilitres);
            for (int i = 0; i < 40; i++)
            {
                warm.Step();
            }
        }

        // Each rule twice: the automaton alone, and with M8b-3b's sheet solve at every boundary
        // (--settleafter=0's case, under inflow). The divisor is one lever on how far a pool
        // reaches in N ticks; the solve is the other, because a front advances one cell a tick at
        // most and only when the edge cell holds a divisor's worth of surplus, which a levelled
        // sheet keeps it holding. Both are printed so the play's question has both answers.
        foreach (SpreadRule spread in spreads)
        {
            RunArm(generator, ml, radius, tickCap, spread, solve: false);
            Console.WriteLine();
            RunArm(generator, ml, radius, tickCap, spread, solve: true);
            Console.WriteLine();
        }

        return 0;
    }

    private static void RunArm(WorldGenerator generator, long ml, int radius, int tickCap, SpreadRule spread, bool solve)
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

        var sim = new FluidSimulation(terrain, spread: spread);
        SheetSettle? settle = solve ? new SheetSettle(sim, settleAfterTicks: 0, settleShare: SheetSettle.DefaultSettleShare) : null;
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
            $"  {spread.ToString().ToLowerInvariant()}{(solve ? " + solve at every boundary" : string.Empty)}: delivered by tick "
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
                + $"{settle.CellsWritten:N0} written, longest pass {settle.LongestMillis:F2} ms, {settle.TotalMillis:F0} ms in all");
        }

        foreach (string line in lines)
        {
            Console.WriteLine(line);
        }

        Console.WriteLine(Profile(sim, floorY, half, rested ? "profile at rest" : "profile at the cap"));
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
