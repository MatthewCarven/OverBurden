using System.Diagnostics;
using System.Globalization;
using Overburden.Core.Fluid;
using Overburden.Core.Voxels;

namespace Overburden.Bench;

/// <summary>
/// Water bodies part a's clean fixture (2026-09-28): a walled box filled once, ticked at the game's
/// own settle until nothing is awake. Does the automaton rest a pool at all, and how long does it take?
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a box and not his world.</b> His TajMahal pool never rests (WATER-BODIES-PLAN, "The
/// falsifier"). But it mixes three things: a lake feeding it at ~1.4 kL/s, 235 terraces, and a
/// still interior. This fixture has none of the first two. If it rests in reasonable time, his pool's
/// awake set comes from its feed and its terraces, and the rule is about waking less. If it does
/// not, the automaton's rest itself is the defect.
/// </para>
/// <para>
/// <b>Three fills, the same volume.</b> <i>level</i> puts the water down already level, the control:
/// it should rest in a tick or two, and if it does not, the rule is broken for water that has nothing
/// to do. <i>block</i> stands the volume as a block in the middle and lets it slump: a dam break,
/// the mounds his pour stood in. <i>edge</i> pours it in along one wall (x 0..3, the whole of z) at
/// the lake's rate until delivered, then stops: his feed, ended. (A 4 x 4 corner was the first
/// inlet, and it choked: water leaves a column that narrow at ~120 L/tick, not 700.)
/// </para>
/// <para>
/// No worker. No near/far order unless <c>--restnear &lt;rings&gt;</c> gives one: then the viewer
/// stands in chunk (0, 0, 0), the box's corner, and water outside the rings is admitted under the
/// game's quota and queued past the boundary. The queue counts as awake. The settle is the game's:
/// after 2 quiet ticks, share 1 in 8, advance 4, fed share 1%.
/// </para>
/// </remarks>
internal static class RestBench
{
    private const double TicksPerSecond = 2.0;

    /// <summary>His valley lake's feed into the quarry, ~1.4 kL/s at 2 Hz (WORKLOG 2026-09-28).</summary>
    private const long FeedLitresPerTick = 700;

    private static readonly int[] Checkpoints =
        [1, 2, 5, 10, 20, 50, 100, 200, 500, 1_000, 2_000, 3_600, 5_000, 7_200, 10_000, 14_400, 20_000, 28_800];

    internal static int Run(string machineControl, bool isDebug, int[] widths, int depth, int fillPercent, int tickCap, string[] fills, int nearRings = 0)
    {
        Console.WriteLine("Rest bench — a walled box filled once: does the pool come to rest, and when?");
        if (isDebug)
        {
            Console.WriteLine("*** DEBUG BUILD — these timings are meaningless. Re-run with -c Release. ***");
        }

        Console.WriteLine();
        Console.WriteLine(machineControl);
        Console.WriteLine();
        Console.WriteLine(
            $"  Fixture: a box {depth} voxels deep ({depth / 4.0:F1} m) with a flat floor, filled {fillPercent}% "
            + $"({depth * fillPercent / 100} layers' worth); widths {string.Join(", ", widths)} voxels; ticked to rest "
            + $"or {tickCap:N0} ticks ({tickCap / TicksPerSecond / 60:F0} min of world time). The settle is the game's: "
            + $"after {SheetSettle.DefaultSettleAfterTicks} quiet ticks, share 1 in {SheetSettle.DefaultSettleShare}, "
            + $"advance {SheetSettle.DefaultFrontAdvance}, fed share {FluidSimulation.DefaultFedSharePercent}%.");
        Console.WriteLine(
            $"  Fills: level (put down level, the control); block (a block at the centre, half the width, that slumps); "
            + $"edge (poured along one wall, x 0..3, at {FeedLitresPerTick} L/tick, the lake's rate, then stopped).");
        Console.WriteLine(nearRings > 0
            ? $"  Near/far order ON: the viewer in chunk (0, 0, 0), {nearRings} ring(s) near; far water admitted at max({FluidSimulation.DefaultFarFloorCells}, near awake) a tick while anything near is awake, the rest queued (and counted awake)."
            : "  Near/far order off (--restnear 0), as every recorded water oracle.");
        Console.WriteLine(
            "  At each checkpoint: awake cells, and of them full / surface / over room; the surface's Y levels; "
            + "its awake cells by largest difference to a side (<5 mL is level by the rule); the window's visits by move.");
        Console.WriteLine();

        // Warm by call count so the first timed tick is tier-1.
        {
            var warmBox = new BoxTerrain(16, 8);
            var warm = new FluidSimulation(warmBox);
            var warmSettle = new SheetSettle(warm, SheetSettle.DefaultSettleAfterTicks, SheetSettle.DefaultSettleShare, 0, SheetSettle.DefaultFrontAdvance);
            FillBlock(warm, warmBox, 200 * FluidConstants.CellCapacityMillilitres);
            for (int i = 0; i < 200; i++)
            {
                warmSettle.AtBoundary();
                warm.Step();
            }
        }

        foreach (int width in widths)
        {
            foreach (string fill in fills)
            {
                RunArm(width, depth, fillPercent, tickCap, fill, nearRings);
                Console.WriteLine();
            }
        }

        return 0;
    }

    private static void RunArm(int width, int depth, int fillPercent, int tickCap, string fill, int nearRings)
    {
        var box = new BoxTerrain(width, depth);
        long volume = (long)width * width * depth * fillPercent / 100 * FluidConstants.CellCapacityMillilitres;
        var sim = new FluidSimulation(box)
        {
            FedSharePercent = FluidSimulation.DefaultFedSharePercent,
            FedAdvance = FluidSimulation.DefaultFedAdvance,
            FedBandRingsPerAdvance = FluidSimulation.DefaultFedBandRingsPerAdvance,
        };
        var settle = new SheetSettle(sim, SheetSettle.DefaultSettleAfterTicks, SheetSettle.DefaultSettleShare, 0, SheetSettle.DefaultFrontAdvance);
        if (nearRings > 0)
        {
            sim.SetNearField(ChunkPos.Zero, nearRings);
        }

        long pending = 0;
        switch (fill)
        {
            case "level":
                FillLevel(sim, box, volume);
                break;
            case "block":
                FillBlock(sim, box, volume);
                break;
            case "edge":
                pending = volume;
                break;
            default:
                throw new ArgumentException($"--restfill must be level, block or edge, not '{fill}'");
        }

        Console.WriteLine(
            $"  {fill}, {width} x {width} ({width / 4.0:F0} m square), {volume / 1000.0:N0} L in "
            + $"{volume / FluidConstants.CellCapacityMillilitres:N0} cells' worth:");

        long[] visitsAtLast = new long[FluidSimulation.MoveBuckets];
        long[] millilitresAtLast = new long[FluidSimulation.MoveBuckets];
        var clock = new Stopwatch();
        double tickMillis = 0;
        int ticks = 0;
        int restTick = -1;
        int deliveredAt = fill == "edge" ? -1 : 0;
        int underTenth = -1, underHundredth = -1;
        int peak = 0;

        while (ticks < tickCap)
        {
            // The game's boundary order: the settle first, then the pour, then the tick.
            settle.AtBoundary();
            if (pending == 0 && sim.ActiveCellCount == 0)
            {
                restTick = ticks;
                break;
            }

            if (pending > 0)
            {
                pending = PourEdge(sim, box, pending, FeedLitresPerTick * FluidConstants.MillilitresPerLitre);
                if (pending == 0)
                {
                    deliveredAt = ticks;
                }
            }

            clock.Restart();
            FluidTickResult result = sim.Step();
            tickMillis += clock.Elapsed.TotalMilliseconds;
            ticks++;
            peak = Math.Max(peak, result.ActiveAfter);

            int held = sim.Grid.HeldCellCount;
            if (pending == 0 && underTenth < 0 && result.ActiveAfter * 10 <= held)
            {
                underTenth = ticks;
            }

            if (pending == 0 && underHundredth < 0 && result.ActiveAfter * 100 <= held)
            {
                underHundredth = ticks;
            }

            if (Array.IndexOf(Checkpoints, ticks) >= 0)
            {
                Console.WriteLine(Checkpoint(sim, box, ticks, pending, visitsAtLast, millilitresAtLast));
            }
        }

        bool rested = restTick >= 0;
        bool quiescent = rested && sim.VerifyQuiescent();
        bool conserved = sim.TotalMillilitres == sim.AddedMillilitres - sim.RemovedMillilitres - sim.DisplacedMillilitres;
        if (!rested && Array.IndexOf(Checkpoints, ticks) < 0)
        {
            Console.WriteLine(Checkpoint(sim, box, ticks, pending, visitsAtLast, millilitresAtLast));
        }

        string Tick(int t) => t < 0 ? "never" : string.Create(CultureInfo.InvariantCulture, $"{t:N0} ({t / TicksPerSecond / 60:F1} min)");
        Console.WriteLine(
            $"    => delivered at tick {Tick(deliveredAt)}; awake under 10% of held at {Tick(underTenth)}, under 1% at {Tick(underHundredth)}; "
            + (rested
                ? $"RESTED at tick {Tick(restTick)}"
                : $"HIT THE CAP at {ticks:N0} with {sim.ActiveCellCount:N0} awake of {sim.Grid.HeldCellCount:N0} held")
            + $"; {(quiescent ? "rest is real" : rested ? "*** STOPPED WITH MOVES AVAILABLE ***" : "not at rest")}; "
            + $"conserved {(conserved ? "exact" : "*** NO ***")}");
        Console.WriteLine(
            $"       peak awake {peak:N0}; {sim.CellVisits:N0} visits ({sim.NearCellsVisited:N0} near, {sim.FarCellsVisited:N0} far, {sim.ThrottledTicks:N0} tick(s) throttled), {tickMillis:F0} ms of ticks ({(ticks == 0 ? 0 : tickMillis / ticks):F2} ms/tick); "
            + $"solve {settle.Settles:N0} pass(es), {settle.SheetsLevelled:N0} levelled / {settle.SheetsNoisy:N0} left fed / "
            + $"{settle.SheetsSkipped:N0} left alone, {settle.CellsWritten:N0} written, {settle.TotalMillis:F0} ms");
    }

    private static string Checkpoint(FluidSimulation sim, BoxTerrain box, int tick, long pending, long[] visitsAtLast, long[] millilitresAtLast)
    {
        WaterComponents.Kinds k = WaterComponents.Classify(sim.Grid, sim.AwakeHeldCells(), box.CanHold, box.CanHold);
        long awake = k.Awake[0] + k.Awake[1] + k.Awake[2];
        long window = 0;
        long[] v = new long[FluidSimulation.MoveBuckets];
        for (int i = 0; i < v.Length; i++)
        {
            v[i] = sim.VisitsByMove[i] - visitsAtLast[i];
            window += v[i];
            visitsAtLast[i] = sim.VisitsByMove[i];
            millilitresAtLast[i] = sim.MillilitresByMove[i];
        }

        string Share(long n) => window == 0 ? "-" : string.Create(CultureInfo.InvariantCulture, $"{100.0 * n / window:F0}%");
        string queued = sim.NearRings > 0
            ? string.Create(CultureInfo.InvariantCulture, $"; far queue {sim.FarBacklog:N0}, admitted near {sim.NearAdmitted:N0} / far {sim.FarAdmitted:N0}")
            : string.Empty;
        string toPour = pending > 0 ? string.Create(CultureInfo.InvariantCulture, $" ({pending / 1000.0:N0} L to pour)") : string.Empty;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"    tick {tick,6:N0}: awake {awake,7:N0} of {sim.Grid.HeldCellCount,7:N0} (full {k.Awake[0]:N0} / surface {k.Awake[1]:N0} / over room {k.Awake[2]:N0}); "
            + $"surface at {k.SurfaceLevels} level(s), awake by diff <5 mL {k.SurfaceDiffAwake[0]:N0} / 5-99 {k.SurfaceDiffAwake[1]:N0} / "
            + $"0.1-1 L {k.SurfaceDiffAwake[2]:N0} / >=1 L {k.SurfaceDiffAwake[3]:N0}; visits {window:N0}: rested {Share(v[0])}, "
            + $"<10 mL {Share(v[1])}, 10-99 {Share(v[2])}, 0.1-1 L {Share(v[3])}, >=1 L {Share(v[4] + v[5])}, empty {Share(v[6])}{queued}{toPour}");
    }

    /// <summary>Full cells from the floor up, then the remainder spread evenly over the top layer, within a millilitre.</summary>
    private static void FillLevel(FluidSimulation sim, BoxTerrain box, long volume)
    {
        int cap = FluidConstants.CellCapacityMillilitres;
        long area = (long)box.Width * box.Width;
        long fullLayers = volume / (area * cap);
        long rest = volume - (fullLayers * area * cap);
        for (int y = 0; y < fullLayers; y++)
        {
            for (int x = 0; x < box.Width; x++)
            {
                for (int z = 0; z < box.Width; z++)
                {
                    sim.Add(new VoxelPos(x, y, z), cap);
                }
            }
        }

        long each = rest / area;
        long extra = rest - (each * area);
        for (int x = 0; x < box.Width; x++)
        {
            for (int z = 0; z < box.Width; z++)
            {
                long amount = each + (extra-- > 0 ? 1 : 0);
                if (amount > 0)
                {
                    sim.Add(new VoxelPos(x, (int)fullLayers, z), amount);
                }
            }
        }
    }

    /// <summary>The volume as a block of full cells over the centre, half the width square, as tall as it needs.</summary>
    private static void FillBlock(FluidSimulation sim, BoxTerrain box, long volume)
    {
        int cap = FluidConstants.CellCapacityMillilitres;
        int side = Math.Max(1, box.Width / 2);
        int lo = (box.Width - side) / 2;
        for (int y = 0; y < box.Depth && volume > 0; y++)
        {
            for (int x = lo; x < lo + side && volume > 0; x++)
            {
                for (int z = lo; z < lo + side && volume > 0; z++)
                {
                    long amount = Math.Min(volume, cap);
                    volume -= amount - sim.Add(new VoxelPos(x, y, z), amount);
                }
            }
        }

        // Taller than the box: the rest goes down level around it, so every arm holds the same volume.
        for (int y = 0; y < box.Depth && volume > 0; y++)
        {
            for (int x = 0; x < box.Width && volume > 0; x++)
            {
                for (int z = 0; z < box.Width && volume > 0; z++)
                {
                    long room = cap - sim.Grid.AmountAt(new VoxelPos(x, y, z));
                    if (room > 0)
                    {
                        long amount = Math.Min(volume, room);
                        volume -= amount - sim.Add(new VoxelPos(x, y, z), amount);
                    }
                }
            }
        }
    }

    /// <summary>Up to <paramref name="rate"/> into the strip along the x = 0 wall, from the top of the box down. Returns what is left to pour.</summary>
    private static long PourEdge(FluidSimulation sim, BoxTerrain box, long pending, long rate)
    {
        long budget = Math.Min(pending, rate);
        long poured = 0;
        for (int y = box.Depth - 1; y >= 0 && poured < budget; y--)
        {
            for (int x = 0; x < 4 && poured < budget; x++)
            {
                for (int z = 0; z < box.Width && poured < budget; z++)
                {
                    var cell = new VoxelPos(x, y, z);
                    long room = FluidConstants.CellCapacityMillilitres - sim.Grid.AmountAt(cell);
                    if (room <= 0)
                    {
                        continue;
                    }

                    long amount = Math.Min(budget - poured, room);
                    poured += amount - sim.Add(cell, amount);
                }
            }
        }

        return pending - poured;
    }
}

/// <summary>A box with a flat floor at y = 0 and walls to <see cref="Depth"/>, open at neither end: water above the walls has nowhere to be.</summary>
internal sealed class BoxTerrain(int width, int depth) : IFluidTerrain
{
    public int Width { get; } = width;

    public int Depth { get; } = depth;

    public bool CanHold(VoxelPos v) => v.Y >= 0 && v.Y < Depth && v.X >= 0 && v.X < Width && v.Z >= 0 && v.Z < Width;
}
