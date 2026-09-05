using System.Diagnostics;
using System.Globalization;
using Overburden.Core.Fluid;
using Overburden.Core.Hydrology;
using Overburden.Core.Voxels;
using Overburden.Core.Worldgen;

namespace Overburden.Bench;

/// <summary>
/// Real, unedited terrain over a wide footprint, open above the surface and in the caves, baked
/// into a bitset column by column the first time anything asks about the column.
/// </summary>
/// <remarks>
/// The surface twin of <see cref="DugTerrain"/>: nothing is dug, so every cell water can be in is
/// above the analytic ground. That is the terrain a spill from the relay sees in the game, and the
/// one M8b-3's hand-off is measured on.
/// </remarks>
internal sealed class SurfaceTerrain : IFluidTerrain
{
    private readonly WorldGenerator _generator;
    private readonly int _minX, _minY, _minZ;
    private readonly int _spanX, _spanY, _spanZ, _words;
    private readonly ulong[] _open;
    private readonly bool[] _built;

    public SurfaceTerrain(WorldGenerator generator, int centreX, int centreZ, int halfSpanVoxels, int minY, int maxY)
    {
        _generator = generator;
        _minX = centreX - halfSpanVoxels;
        _minZ = centreZ - halfSpanVoxels;
        _minY = minY;
        _spanX = (2 * halfSpanVoxels) + 1;
        _spanZ = (2 * halfSpanVoxels) + 1;
        _spanY = maxY - minY + 1;
        _words = (_spanY + 63) / 64;
        _open = new ulong[(long)_spanX * _spanZ * _words];
        _built = new bool[_spanX * _spanZ];
    }

    public bool CanHold(VoxelPos v)
    {
        int lx = v.X - _minX;
        int lz = v.Z - _minZ;
        int ly = v.Y - _minY;
        if ((uint)lx >= (uint)_spanX || (uint)lz >= (uint)_spanZ || (uint)ly >= (uint)_spanY)
        {
            return false;
        }

        int column = (lx * _spanZ) + lz;
        if (!_built[column])
        {
            Build(v.X, v.Z, column);
        }

        return (_open[((long)column * _words) + (ly >> 6)] & (1UL << (ly & 63))) != 0;
    }

    private void Build(int worldX, int worldZ, int column)
    {
        // Open above the surface and nowhere else. Worldgen keeps every cave
        // CaveSurfaceMarginVoxels under its own column's surface, so water on the ground can
        // never reach one without a dig, and evaluating the cave field for every column a spill
        // touches would time the generator rather than the automaton (the --fluid fixture's
        // first defect, M8a-2).
        int surface = _generator.SurfaceHeightAt(worldX, worldZ);
        long baseWord = (long)column * _words;
        for (int ly = Math.Max(0, surface + 1 - _minY); ly < _spanY; ly++)
        {
            _open[baseWord + (ly >> 6)] |= 1UL << (ly & 63);
        }

        _built[column] = true;
    }
}

/// <summary>
/// M8b-3's measurement: a spill on real ground, with and without the B → A hand-off, and how much
/// of the awake set the hand-off removes.
/// </summary>
/// <remarks>
/// <para>
/// <b>The number M8a's named fallback is waiting on.</b> M8a-2 found that a flood keeps its whole
/// wetted area awake until it is level, O(n²) in the length of it, and named a coarser rest
/// tolerance as the fallback — deliberately not built until M8b measured how much the hand-off to
/// Tier A removes. This is that measurement, on the case the game actually produces: the relay's
/// ball put down on a hillside, as <c>--injectlitres</c> puts it, over the same terrain and the
/// same analysed window the game would install.
/// </para>
/// <para>
/// <b>Two arms, same pour, same terrain, same ticks.</b> The control runs the automaton alone and
/// the water spreads until the tick cap or rest; the other runs <see cref="SurfaceHandoff"/> at
/// every tick boundary. Both print the active set's decay, the ticks to rest, the visits paid, and
/// where the litres ended — Tier B, Tier A, pending, spilled — with both tiers' identities and the
/// seam's equality checked at the end, so a pass that lost a litre between the tiers prints as one.
/// </para>
/// </remarks>
internal static class SpillBench
{
    private const double TicksPerSecond = 2.0;
    private const int Pitch = 64;
    private static readonly int[] Checkpoints = [1, 5, 10, 25, 50, 100, 250, 500, 1_000, 2_500, 5_000, 10_000];

    internal static int Run(
        WorldGenerator generator, string machineControl, bool isDebug, ulong seed, long[] litresArms, int tickCap)
    {
        Console.WriteLine("Spill bench — M8b-3: the B → A hand-off against a spill on real ground");
        if (isDebug)
        {
            Console.WriteLine("*** DEBUG BUILD — these timings are meaningless. Re-run with -c Release. ***");
        }

        Console.WriteLine();
        Console.WriteLine(machineControl);
        Console.WriteLine();

        const int centreX = 0;
        const int centreZ = 0;
        const int halfSpan = 64 * VoxelConstants.VoxelsPerMetre;
        int surface = generator.SurfaceHeightAt(centreX, centreZ);

        // The whole footprint's surface range, so the bitset is sealed well under the lowest
        // ground and well over the tallest ball.
        int lowest = int.MaxValue;
        int highest = int.MinValue;
        for (int x = centreX - halfSpan; x <= centreX + halfSpan; x += 4)
        {
            for (int z = centreZ - halfSpan; z <= centreZ + halfSpan; z += 4)
            {
                int h = generator.SurfaceHeightAt(x, z);
                lowest = Math.Min(lowest, h);
                highest = Math.Max(highest, h);
            }
        }

        var water = new WaterSystem(generator, seed, new BasinLedger(), Pitch);
        water.Install(water.AnalyseFor(centreX, centreZ));
        (int sx, int sz) = water.SampleOf(centreX, centreZ);
        int basinHere = water.BasinAtSample(sx, sz);
        Watershed shed = water.Window!.Watershed;

        Console.WriteLine(
            $"  Fixture: the relay's ball on the ground at ({centreX}, {centreZ}), surface {surface} "
            + $"(footprint {2 * halfSpan / VoxelConstants.VoxelsPerMetre} m square, ground {lowest}..{highest}); "
            + $"a {water.SpanMetres} m window of {shed.Basins.Count} basins installed around it.");
        Console.WriteLine(
            basinHere == Watershed.Outlet
                ? "  The spill's own sample drains off the window."
                : $"  The spill's sample belongs to basin {basinHere}, pit at ({shed.Basins[basinHere].PitX}, "
                  + $"{shed.Basins[basinHere].PitZ}) plane {shed.Basins[basinHere].PitHeight}, capacity "
                  + $"{shed.Basins[basinHere].CapacityLitres:N0} L, catchment {shed.Basins[basinHere].CatchmentSamples} samples.");
        Console.WriteLine(
            $"  Each arm pours at the tick boundary until the ball is delivered, then ticks to rest or "
            + $"to a cap of {tickCap:N0} ticks ({tickCap / TicksPerSecond / 60.0:F0} min of world time).");
        Console.WriteLine();

        foreach (long litres in litresArms)
        {
            foreach ((bool handoff, bool settle) in new[] { (false, false), (true, false), (true, true) })
            {
                RunArm(generator, seed, centreX, centreZ, halfSpan, lowest - 8, highest + 64, litres, handoff, settle, tickCap);
            }

            Console.WriteLine();
        }

        Console.WriteLine(
            "  'cell-ticks' is the area under the active-set curve — every visit the flood paid "
            + "until it rested or the cap ran out — which is what the hand-off removes.");
        return 0;
    }

    private static void RunArm(
        WorldGenerator generator, ulong seed, int centreX, int centreZ, int halfSpan, int minY, int maxY,
        long litres, bool withHandoff, bool withSettle, int tickCap)
    {
        var terrain = new SurfaceTerrain(generator, centreX, centreZ, halfSpan, minY, maxY);
        var water = new WaterSystem(generator, seed, new BasinLedger(), Pitch);
        water.Install(water.AnalyseFor(centreX, centreZ));
        var sim = new FluidSimulation(terrain);
        SurfaceHandoff? handoff = withHandoff ? new SurfaceHandoff(generator, water, sim) : null;

        // M8b-3b's settle beside the hand-off, at the game's default interval and in the game's
        // order at the boundary: settle, then absorb what it listed as resting, then pour.
        SheetSettle? settle = withSettle ? new SheetSettle(sim) : null;

        int surface = generator.SurfaceHeightAt(centreX, centreZ);
        long pending = litres * FluidConstants.MillilitresPerLitre;
        int radius = RadiusVoxelsFor(pending);
        var at = new VoxelPos(centreX, surface + 1 + radius, centreZ);

        // Promote the code first, on a small pour of its own, so the tick is tier-1 before the
        // clock starts — the watershed bench's rule.
        var warm = new FluidSimulation(terrain);
        warm.Add(at, FluidConstants.CellCapacityMillilitres);
        for (int i = 0; i < 40; i++)
        {
            warm.Step();
        }

        var checkpoints = new Dictionary<int, int>();
        long cellTicks = 0;
        int peak = 0;
        int ticks = 0;
        int deliveredAtTick = -1;
        double tickMillis = 0;
        double absorbMillis = 0;
        var clock = new Stopwatch();

        while (ticks < tickCap)
        {
            // The boundary: the hand-off first, as Main runs it. A hand-off wakes the neighbours
            // of what it took, so "at rest" is a boundary that handed nothing with nothing awake
            // and nothing left to pour — not an active set of zero on its own.
            settle?.AtBoundary();

            long handed = 0;
            if (handoff is not null)
            {
                clock.Restart();
                handed = handoff.Absorb();
                absorbMillis += clock.Elapsed.TotalMilliseconds;
            }

            if (pending == 0 && sim.ActiveCellCount == 0 && handed == 0)
            {
                break;
            }

            if (pending > 0)
            {
                pending = Pour(sim, at, radius, pending);
                if (pending == 0)
                {
                    deliveredAtTick = ticks;
                }
            }

            clock.Restart();
            FluidTickResult result = sim.Step();
            tickMillis += clock.Elapsed.TotalMilliseconds;
            ticks++;

            cellTicks += result.CellsVisited;
            peak = Math.Max(peak, result.CellsVisited);
            if (Array.IndexOf(Checkpoints, ticks) >= 0)
            {
                checkpoints[ticks] = result.ActiveAfter;
            }
        }

        bool rested = sim.ActiveCellCount == 0 && pending == 0;
        bool quiescent = rested && sim.VerifyQuiescent();

        long held = sim.TotalMillilitres;
        long handedAll = sim.HandedMillilitres;
        long received = water.ReceivedLitres;
        long pendingMl = handoff?.PendingMillilitres ?? 0;
        bool bIdentity = held == sim.AddedMillilitres - sim.RemovedMillilitres - sim.DisplacedMillilitres - handedAll;
        bool aIdentity = water.StoredLitres == water.ReceivedLitres + water.InjectedLitres - water.SpilledLitres - water.DrawnLitres;
        bool seam = (received * FluidConstants.MillilitresPerLitre) + pendingMl == handedAll
            && (handoff is null || handoff.HandedMillilitres == handedAll);
        bool whole = (water.StoredLitres + water.SpilledLitres) * FluidConstants.MillilitresPerLitre + pendingMl + held
            == sim.AddedMillilitres - sim.RemovedMillilitres - sim.DisplacedMillilitres;

        Console.WriteLine(
            $"  {litres:N0} L, hand-off {(withHandoff ? "ON " : "off")}{(withSettle ? " + settle" : string.Empty)}: ball radius {radius}, delivered by tick "
            + $"{(deliveredAtTick < 0 ? "never" : deliveredAtTick.ToString("N0", CultureInfo.InvariantCulture))}; "
            + $"{(rested ? $"rested after {ticks:N0} ticks" : $"HIT THE CAP at {ticks:N0} ticks with {sim.ActiveCellCount:N0} awake")}"
            + $" ({ticks / TicksPerSecond / 60.0:F1} min of world time); "
            + $"{(quiescent ? "rest is real" : rested ? "*** STOPPED WITH MOVES AVAILABLE ***" : "not at rest")}");
        Console.WriteLine(
            $"           peak active {peak:N0}, cell-ticks {cellTicks:N0}, {sim.CellVisits:N0} visits, "
            + $"tick {tickMillis:F1} ms in all ({(ticks == 0 ? 0 : tickMillis / ticks):F3} ms/tick)"
            + (handoff is null
                ? string.Empty
                : $", absorb {absorbMillis:F1} ms in all over {handoff.Handoffs:N0} hand-off(s)"));
        Console.WriteLine(
            "           active after tick: "
            + string.Join(", ", Checkpoints.Where(checkpoints.ContainsKey).Select(t => $"{t}→{checkpoints[t]:N0}")));
        Console.WriteLine(
            $"           ended: Tier B {held / 1000.0:N1} L in {sim.Grid.HeldCellCount:N0} cell(s) over {sim.Grid.PanelCount} panel(s); "
            + $"Tier A received {received:N0} L into {(handoff is null ? 0 : handoff.PendingBasins)} basin(s), holding "
            + $"{water.StoredLitres:N0} L, spilled {water.SpilledLitres:N0} L; pending {pendingMl:N0} mL; "
            + $"displaced {sim.DisplacedMillilitres / 1000.0:N1} L");
        Console.WriteLine(
            $"           identities: Tier B {(bIdentity ? "exact" : "*** NO ***")}, Tier A {(aIdentity ? "exact" : "*** NO ***")}, "
            + $"seam received×1000 + pending == handed {(seam ? "exact" : "*** NO ***")}, "
            + $"whole world {(whole ? "exact" : "*** NO ***")}");

        if (settle is not null)
        {
            Console.WriteLine(
                $"           settle: {settle.Settles:N0} pass(es) after {settle.SettleAfterTicks} quiet tick(s), "
                + $"{settle.SheetsLevelled:N0} levelled / {settle.SheetsPacked:N0} packed / {settle.SheetsSkipped:N0} left alone, "
                + $"{settle.CellsWritten:N0} cells written, {settle.CellsWoken:N0} woken, largest sheet {settle.LargestSheet:N0}, "
                + $"longest pass {settle.LongestMillis:F2} ms, {settle.TotalMillis:F0} ms in all");
        }
    }

    private static long Pour(FluidSimulation sim, VoxelPos at, int r, long pending)
    {
        int r2 = r * r;
        for (int dy = r; dy >= -r; dy--)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    if ((dx * dx) + (dy * dy) + (dz * dz) > r2)
                    {
                        continue;
                    }

                    pending = sim.Add(at.Offset(dx, dy, dz), pending);
                    if (pending == 0)
                    {
                        return 0;
                    }
                }
            }
        }

        return pending;
    }

    /// <summary>Main's rule for the relay's ball, copied so the bench pours what the game pours.</summary>
    private static int RadiusVoxelsFor(long millilitres)
    {
        double cells = millilitres / (double)FluidConstants.CellCapacityMillilitres;
        double radius = Math.Cbrt(3.0 * cells / (4.0 * Math.PI));
        return Math.Max(1, (int)Math.Ceiling(radius) + 1);
    }
}
