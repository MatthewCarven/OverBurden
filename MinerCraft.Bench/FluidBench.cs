using System.Diagnostics;
using System.Globalization;
using MinerCraft.Core.Fluid;
using MinerCraft.Core.Voxels;
using MinerCraft.Core.Worldgen;

namespace MinerCraft.Bench;

/// <summary>
/// Real terrain with a mine cut into it: one vertical bore from the surface down to a grid of
/// tunnels.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not a tank, and not SCALE's settling cave.</b> M8-PLAN names the case to measure as a lake
/// draining into a shaft, because that is a <i>sustained flow</i> rather than a body coming to
/// rest, and because it is what the play will actually produce. A hand-built basin makes the
/// automaton's easy case its only case, which is the fixture trap this project keeps meeting.
/// </para>
/// <para>
/// <b>The tunnels are 0.75 m bores because that is what <c>hand_pick</c> leaves</b>, and they stay
/// that width in every arm so the only thing the sweep moves is the breach.
/// </para>
/// </remarks>
internal sealed class MineTerrain : IFluidTerrain
{
    private readonly WorldGenerator _generator;

    private readonly int _shaftX, _shaftZ, _shaftHalf;
    private readonly int _floorY, _roofY;
    private readonly int _halfSpan, _spacing, _bore;
    private readonly int _minX, _minY, _minZ;
    private readonly int _spanX, _spanY, _spanZ, _words;

    // One bitset column per (x, z), built the first time anything asks about that column.
    //
    // THE FIRST VERSION OF THIS FIXTURE ANSWERED CanHold BY CALLING IsCaveAt, AND IT MEASURED
    // ITSELF. A visit cost 3.86 microseconds against the light propagator's 0.457, because six
    // neighbour queries a visit each evaluated eight octaves of gradient noise. In the real world
    // CanHold is an index into a chunk's voxel array. Baking the terrain first is what makes the
    // number below a measurement of the automaton rather than of the generator.
    private readonly ulong[] _open;
    private readonly bool[] _built;

    public MineTerrain(
        WorldGenerator generator,
        int shaftX,
        int shaftZ,
        int shaftVoxels,
        int depthMetres,
        int spanMetres,
        int spacingMetres,
        int boreVoxels,
        int tunnelHeightVoxels)
    {
        _generator = generator;
        _shaftX = shaftX;
        _shaftZ = shaftZ;
        _shaftHalf = shaftVoxels / 2;
        _bore = boreVoxels / 2;
        _halfSpan = spanMetres * VoxelConstants.VoxelsPerMetre / 2;
        _spacing = spacingMetres * VoxelConstants.VoxelsPerMetre;

        SurfaceY = generator.SurfaceHeightAt(shaftX, shaftZ);
        _floorY = SurfaceY - (depthMetres * VoxelConstants.VoxelsPerMetre);
        _roofY = _floorY + tunnelHeightVoxels - 1;

        // Sealed above and below: an unloaded voxel is solid, which is the answer the real adapter
        // gives at the edge of the loaded world.
        _minX = shaftX - _halfSpan - 4;
        _minZ = shaftZ - _halfSpan - 4;
        _minY = _floorY - 4;
        _spanX = (2 * _halfSpan) + 9;
        _spanZ = (2 * _halfSpan) + 9;
        _spanY = SurfaceY + 4 - _minY + 1;
        _words = ((_spanY + 63) / 64);

        _open = new ulong[(long)_spanX * _spanZ * _words];
        _built = new bool[_spanX * _spanZ];
    }

    /// <summary>Y of the surface over the shaft. The mouth sits at it.</summary>
    public int SurfaceY { get; }

    /// <summary>The cells a lake pours into: the top layer of the shaft.</summary>
    public IEnumerable<VoxelPos> Mouth()
    {
        for (int dx = -_shaftHalf; dx <= _shaftHalf; dx++)
        {
            for (int dz = -_shaftHalf; dz <= _shaftHalf; dz++)
            {
                yield return new VoxelPos(_shaftX + dx, SurfaceY, _shaftZ + dz);
            }
        }
    }

    /// <summary>Cells the mine holds when flooded to the roof. The denominator.</summary>
    public long TunnelCells()
    {
        long cells = 0;
        for (int x = _shaftX - _halfSpan; x <= _shaftX + _halfSpan; x++)
        {
            for (int z = _shaftZ - _halfSpan; z <= _shaftZ + _halfSpan; z++)
            {
                if (InTunnelPlan(x, z))
                {
                    cells += _roofY - _floorY + 1;
                }
            }
        }

        return cells;
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

    /// <summary>Evaluates one whole column of terrain, once.</summary>
    private void Build(int worldX, int worldZ, int column)
    {
        int surface = _generator.SurfaceHeightAt(worldX, worldZ);
        bool shaft = Math.Abs(worldX - _shaftX) <= _shaftHalf
            && Math.Abs(worldZ - _shaftZ) <= _shaftHalf;
        bool tunnel = Math.Abs(worldX - _shaftX) <= _halfSpan
            && Math.Abs(worldZ - _shaftZ) <= _halfSpan
            && InTunnelPlan(worldX, worldZ);

        long baseWord = (long)column * _words;
        for (int ly = 0; ly < _spanY; ly++)
        {
            int y = _minY + ly;
            bool open =
                (shaft && y >= _floorY && y <= SurfaceY)
                || (tunnel && y >= _floorY && y <= _roofY)
                || y > surface
                || _generator.IsCaveAt(worldX, y, worldZ, surface);

            if (open)
            {
                _open[baseWord + (ly >> 6)] |= 1UL << (ly & 63);
            }
        }

        _built[column] = true;
    }

    private bool InTunnelPlan(int x, int z) =>
        Math.Abs(Mod(x - _shaftX + _bore, _spacing) - _bore) <= _bore
        || Math.Abs(Mod(z - _shaftZ + _bore, _spacing) - _bore) <= _bore;

    private static int Mod(int value, int modulus)
    {
        int r = value % modulus;
        return r < 0 ? r + modulus : r;
    }
}

/// <summary>
/// Times M8a's automaton on the case M8-PLAN named: a lake draining into a mine.
/// </summary>
/// <remarks>
/// <para>
/// <b>The sweep is the width of the breach, and that is deliberate.</b> M7's play asked
/// <i>"unless I make a hole 4x4 it isn't going to drain into eventually?"</i>, and this is that
/// question with a number against it: how fast does a lake actually empty through a bore of each
/// width, and what does the flood cost while it does. The tunnels stay 0.75 m in every arm, so the
/// breach is the only thing moving.
/// </para>
/// <para>
/// <b>The lake pours in as fast as the hole will take it and no faster.</b> Whatever the mouth
/// refuses stays in the lake and is offered again next tick, which is what makes the achieved rate
/// a measurement rather than a setting. A fixed inflow would have measured the number the harness
/// chose.
/// </para>
/// </remarks>
internal static class FluidBench
{
    /// <summary>The tick rate M8-PLAN costs the active set against.</summary>
    private const double TicksPerSecond = 2.0;

    /// <summary>What the light budget already licenses, from M5c-1. The denominator to print.</summary>
    private const long LightBudgetVisitsPerSecond = 520_000;

    internal static int Run(
        WorldGenerator generator,
        string machineControl,
        bool isDebug,
        long lakeLitres,
        int ticks,
        int[] bores,
        int spanMetres,
        int depthMetres,
        int settleCap)
    {
        Console.WriteLine(
            $"Fluid bench — Tier B, 0.25 m cells, {FluidConstants.CellCapacityMillilitres:N0} mL "
            + $"({FluidConstants.CellCapacityLitres:F3} L) per cell");
        if (isDebug)
        {
            Console.WriteLine("*** DEBUG BUILD — these timings are meaningless. Re-run with -c Release. ***");
        }

        Console.WriteLine();
        Console.WriteLine(machineControl);
        Console.WriteLine();
        Console.WriteLine(
            $"  Fixture: a {spanMetres} m x {spanMetres} m grid of 0.75 m tunnels 2 m high, on an "
            + $"8 m pitch, {depthMetres} m under the surface, fed by one bore from a "
            + $"{lakeLitres:N0} L lake.");
        Console.WriteLine(
            $"  The lake pours in as fast as the bore accepts it; what the mouth refuses is offered "
            + $"again next tick. {ticks:N0} ticks of flow at {TicksPerSecond:F0} Hz, then settle.");
        Console.WriteLine();

        Console.WriteLine(
            "  bore     tunnel   delivered   drained   L/tick   L/s     hours to   peak     "
            + "visits   visits/s   ms/tick   ms/tick   held      panels   MiB    settle   conserved");
        Console.WriteLine(
            "  voxels   cells    L           %         mean     mean    drain      active   /tick "
            + "   @2Hz       mean      max       cells                    ticks");
        Console.WriteLine(
            "  ------   ------   ---------   -------   ------   -----   --------   ------   "
            + "------   --------   -------   -------   -------   ------   ----   ------   ---------");

        foreach (int bore in bores)
        {
            RunArm(generator, lakeLitres, ticks, bore, spanMetres, depthMetres, settleCap);
        }

        Console.WriteLine();
        Console.WriteLine(
            $"  visits/s is measured against the ~{LightBudgetVisitsPerSecond:N0} cells/s M5c-1's "
            + "light budget already licenses on the same thread.");
        Console.WriteLine(
            "  'settle ticks' is the flow stopping and everything coming to rest. It is a "
            + "CORRECTNESS figure, not a cost one:");
        Console.WriteLine(
            "  Tier B's condition is that it terminates, so a run that never settles is wrong "
            + "rather than slow.");
        return 0;
    }

    private static string Ticks(int value) =>
        value < 0 ? "never" : value.ToString("N0", CultureInfo.InvariantCulture);

    private static void RunArm(
        WorldGenerator generator, long lakeLitres, int ticks, int bore, int spanMetres,
        int depthMetres, int settleCap)
    {
        var terrain = new MineTerrain(
            generator,
            shaftX: 0,
            shaftZ: 0,
            shaftVoxels: bore,
            depthMetres: depthMetres,
            spanMetres: spanMetres,
            spacingMetres: 8,
            boreVoxels: 3,
            tunnelHeightVoxels: 8);

        long tunnelCells = terrain.TunnelCells();
        VoxelPos[] mouth = [.. terrain.Mouth()];

        var sim = new FluidSimulation(terrain);
        long lake = lakeLitres * FluidConstants.MillilitresPerLitre;
        long offered = lake;

        // Promote the tick before anything is timed. Same reason the watershed bench warms by call
        // count rather than by wall clock: tiered compilation promotes after about thirty calls,
        // and a warm-up capped by time under-warms exactly when the subject gets slower.
        var warm = new FluidSimulation(terrain);
        foreach (VoxelPos cell in mouth)
        {
            warm.Add(cell, FluidConstants.CellCapacityMillilitres);
        }

        for (int i = 0; i < 40; i++)
        {
            warm.Step();
        }

        double totalMillis = 0;
        double worstMillis = 0;
        long totalVisits = 0;
        int peakActive = 0;
        int flowTicks = 0;

        for (int tick = 0; tick < ticks && lake > 0; tick++)
        {
            long remaining = lake;
            foreach (VoxelPos cell in mouth)
            {
                if (remaining == 0)
                {
                    break;
                }

                remaining = sim.Add(cell, remaining);
            }

            lake = remaining;

            var clock = Stopwatch.StartNew();
            FluidTickResult result = sim.Step();
            double millis = clock.Elapsed.TotalMilliseconds;

            totalMillis += millis;
            worstMillis = Math.Max(worstMillis, millis);
            totalVisits += result.CellsVisited;
            peakActive = Math.Max(peakActive, result.CellsVisited);
            flowTicks++;
        }

        long delivered = offered - lake;

        // The flow stops here and everything comes to rest.
        //
        // THE CAP IS A BUDGET, NOT A CORRECTNESS STATEMENT, and the distinction matters because
        // the two look identical in a report. FluidRules carries a proof that this terminates, so
        // hitting the cap means levelling is SLOW rather than that it oscillates: the automaton
        // spreads by diffusion, and diffusion across a corridor of n cells takes on the order of
        // n squared ticks. What the cap costs is stated in the row — the active set still awake
        // when the clock ran out.
        //
        // Reported as a DECAY CURVE rather than as one number, because the two halves of it are
        // different quantities. "The flood stops costing" happens quickly and is a budget
        // question; "every last millilitre is level" is diffusive and is a correctness question
        // that M8b's handoff to Tier A is the real answer to.
        int quietThreshold = Math.Max(64, peakActive / 10);
        int ticksToQuiet = -1;
        int ticksToNearlyStill = -1;
        int settleTicks = 0;
        long settleVisits = 0;
        int settlePeak = 0;

        var settleClock = Stopwatch.StartNew();
        while (sim.ActiveCellCount > 0 && settleTicks < settleCap)
        {
            FluidTickResult tick = sim.Step();
            settleTicks++;
            settleVisits += tick.CellsVisited;
            settlePeak = Math.Max(settlePeak, tick.CellsVisited);

            if (ticksToQuiet < 0 && tick.ActiveAfter <= quietThreshold)
            {
                ticksToQuiet = settleTicks;
            }

            if (ticksToNearlyStill < 0 && tick.ActiveAfter <= 64)
            {
                ticksToNearlyStill = settleTicks;
            }
        }

        double settleMillis = settleClock.Elapsed.TotalMilliseconds;
        var settled = new FluidSettleResult(
            settleTicks, sim.ActiveCellCount == 0, settlePeak, settleVisits);

        // Conservation and quiescence are reported apart, and mixing them was an instrument
        // defect caught on the first full run: a settle that hits its tick budget is not
        // quiescent, which made a perfectly conserved arm print "*** NO ***" against the column
        // headed 'conserved'. Two properties, two columns.
        bool conserved =
            sim.TotalMillilitres
                == sim.AddedMillilitres - sim.RemovedMillilitres - sim.DisplacedMillilitres
            && sim.AddedMillilitres == delivered;

        double meanVisits = flowTicks == 0 ? 0 : totalVisits / (double)flowTicks;
        double litresPerTick = flowTicks == 0
            ? 0
            : delivered / (double)FluidConstants.MillilitresPerLitre / flowTicks;
        double hoursToDrain = litresPerTick <= 0
            ? double.PositiveInfinity
            : lakeLitres / litresPerTick / TicksPerSecond / 3600.0;

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  {bore,6}   {tunnelCells,6:N0}   "
            + $"{delivered / FluidConstants.MillilitresPerLitre,9:N0}   "
            + $"{100.0 * delivered / offered,7:F1}   {litresPerTick,6:F1}   "
            + $"{litresPerTick * TicksPerSecond,5:F0}   {hoursToDrain,8:F2}   {peakActive,6:N0}   "
            + $"{meanVisits,6:F0}   {meanVisits * TicksPerSecond,8:F0}   "
            + $"{(flowTicks == 0 ? 0 : totalMillis / flowTicks),7:F3}   {worstMillis,7:F3}   "
            + $"{sim.Grid.HeldCellCount,7:N0}   {sim.Grid.PanelCount,6:N0}   "
            + $"{sim.Grid.PanelBytes / 1024.0 / 1024.0,4:F1}   "
            + $"{(settled.Settled ? settled.Ticks.ToString("N0", CultureInfo.InvariantCulture) : "NEVER"),6}   "
            + $"{(conserved ? "exact" : "*** NO ***"),9}"));

        string verdict = settled.Settled
            ? (sim.VerifyQuiescent() ? "reached rest, and the rest is real." : "*** STOPPED WITH MOVES AVAILABLE ***")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"HIT THE CAP with {sim.ActiveCellCount:N0} still awake — diffusive levelling "
                + $"rather than an oscillation; see FluidRules' termination proof.");

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"           settle: down to {quietThreshold:N0} active in "
            + $"{Ticks(ticksToQuiet)} ticks, to 64 in {Ticks(ticksToNearlyStill)}; "
            + $"{settled.Ticks:N0} ticks total "
            + $"({settled.Ticks / TicksPerSecond / 60.0:F1} min of world time), "
            + $"{settleMillis:F0} ms of CPU, {settled.CellVisits:N0} visits — {verdict}"));
    }
}
