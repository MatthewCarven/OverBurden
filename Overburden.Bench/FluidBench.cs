using System.Diagnostics;
using System.Globalization;
using Overburden.Core.Fluid;
using Overburden.Core.Voxels;
using Overburden.Core.Worldgen;

namespace Overburden.Bench;

/// <summary>
/// Real terrain with something dug into it, baked into a bitset before anything is timed.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not a tank, and not SCALE's settling cave.</b> M8-PLAN names the case to measure as a lake
/// draining into a shaft, because that is a <i>sustained flow</i> rather than a body coming to
/// rest, and because it is what the play will actually produce. A hand-built basin makes the
/// automaton's easy case its only case, which is the fixture trap this project keeps meeting.
/// </para>
/// <para>
/// <b>Two shapes, because the play changed what the normal excavation is.</b> <see cref="MineTerrain"/>
/// is a grid of 0.75 m bores, which is what <c>hand_pick</c> leaves and what M8-PLAN's cost model
/// was derived from. <see cref="ChamberTerrain"/> is a block of 8 m voids — bitten chunks — which is
/// what the entangled miner leaves and what the played world of 2026-09-03 is made of: 4,562 of
/// its 4,584 stored chunks are uniform air. One bitten chunk is 32,768 cells of void where a bore
/// is a few hundred, and the tick budget M8b settles has to be priced against both.
/// </para>
/// <para>
/// One bitset column per (x, z), built the first time anything asks about that column.
/// <b>THE FIRST VERSION OF THIS FIXTURE ANSWERED CanHold BY CALLING IsCaveAt, AND IT MEASURED
/// ITSELF.</b> A visit cost 3.86 microseconds against the light propagator's 0.457, because six
/// neighbour queries a visit each evaluated eight octaves of gradient noise. In the real world
/// CanHold is an index into a chunk's voxel array. Baking the terrain first is what makes the
/// number below a measurement of the automaton rather than of the generator.
/// </para>
/// </remarks>
internal abstract class DugTerrain : IFluidTerrain
{
    private readonly WorldGenerator _generator;

    private readonly int _minX, _minY, _minZ;
    private readonly int _spanX, _spanY, _spanZ, _words;

    private readonly ulong[] _open;
    private readonly bool[] _built;

    /// <param name="halfSpanVoxels">Half the footprint of the excavation, in voxels, around the centre.</param>
    /// <param name="floorY">The lowest dug voxel. The bitset is sealed four voxels below it.</param>
    protected DugTerrain(
        WorldGenerator generator, int centreX, int centreZ, int halfSpanVoxels, int floorY)
    {
        _generator = generator;
        CentreX = centreX;
        CentreZ = centreZ;
        SurfaceY = generator.SurfaceHeightAt(centreX, centreZ);

        // Sealed above and below: an unloaded voxel is solid, which is the answer the real adapter
        // gives at the edge of the loaded world.
        _minX = centreX - halfSpanVoxels - 4;
        _minZ = centreZ - halfSpanVoxels - 4;
        _minY = floorY - 4;
        _spanX = (2 * halfSpanVoxels) + 9;
        _spanZ = (2 * halfSpanVoxels) + 9;
        _spanY = SurfaceY + 4 - _minY + 1;
        _words = (_spanY + 63) / 64;

        _open = new ulong[(long)_spanX * _spanZ * _words];
        _built = new bool[_spanX * _spanZ];
    }

    /// <summary>The box the bitset covers, inclusive. What --terrain materialises into chunks.</summary>
    public (VoxelPos Min, VoxelPos Max) Bounds() => (
        new VoxelPos(_minX, _minY, _minZ),
        new VoxelPos(_minX + _spanX - 1, _minY + _spanY - 1, _minZ + _spanZ - 1));

    public int CentreX { get; }

    public int CentreZ { get; }

    /// <summary>Y of the surface over the centre. The mouth sits at it.</summary>
    public int SurfaceY { get; }

    /// <summary>One line saying what was dug, for the report.</summary>
    public abstract string Describe();

    /// <summary>The cells a lake pours into: the top layer of the shaft.</summary>
    public abstract IEnumerable<VoxelPos> Mouth();

    /// <summary>Cells the excavation holds when flooded to its roof. The denominator.</summary>
    public abstract long VoidCells();

    /// <summary>Whether this voxel was dug. The terrain's own openings are added on top.</summary>
    protected abstract bool IsDug(int x, int y, int z);

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

        long baseWord = (long)column * _words;
        for (int ly = 0; ly < _spanY; ly++)
        {
            int y = _minY + ly;
            bool open =
                IsDug(worldX, y, worldZ)
                || y > surface
                || _generator.IsCaveAt(worldX, y, worldZ, surface);

            if (open)
            {
                _open[baseWord + (ly >> 6)] |= 1UL << (ly & 63);
            }
        }

        _built[column] = true;
    }

    protected static int Mod(int value, int modulus)
    {
        int r = value % modulus;
        return r < 0 ? r + modulus : r;
    }
}

/// <summary>
/// One vertical bore from the surface down to a grid of tunnels.
/// </summary>
/// <remarks>
/// <b>The tunnels are 0.75 m bores because that is what <c>hand_pick</c> leaves</b>, and they stay
/// that width in every arm so the only thing the sweep moves is the breach.
/// </remarks>
internal sealed class MineTerrain : DugTerrain
{
    private readonly int _shaftHalf;
    private readonly int _floorY, _roofY;
    private readonly int _halfSpan, _spacing, _bore;
    private readonly int _spanMetres, _depthMetres;

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
        : base(
            generator,
            shaftX,
            shaftZ,
            spanMetres * VoxelConstants.VoxelsPerMetre / 2,
            generator.SurfaceHeightAt(shaftX, shaftZ) - (depthMetres * VoxelConstants.VoxelsPerMetre))
    {
        _shaftHalf = shaftVoxels / 2;
        _bore = boreVoxels / 2;
        _halfSpan = spanMetres * VoxelConstants.VoxelsPerMetre / 2;
        _spacing = spacingMetres * VoxelConstants.VoxelsPerMetre;
        _spanMetres = spanMetres;
        _depthMetres = depthMetres;

        _floorY = SurfaceY - (depthMetres * VoxelConstants.VoxelsPerMetre);
        _roofY = _floorY + tunnelHeightVoxels - 1;
    }

    public override string Describe() =>
        $"a {_spanMetres} m x {_spanMetres} m grid of 0.75 m tunnels 2 m high, on an 8 m pitch, "
        + $"{_depthMetres} m under the surface";

    public override IEnumerable<VoxelPos> Mouth()
    {
        for (int dx = -_shaftHalf; dx <= _shaftHalf; dx++)
        {
            for (int dz = -_shaftHalf; dz <= _shaftHalf; dz++)
            {
                yield return new VoxelPos(CentreX + dx, SurfaceY, CentreZ + dz);
            }
        }
    }

    public override long VoidCells()
    {
        long cells = 0;
        for (int x = CentreX - _halfSpan; x <= CentreX + _halfSpan; x++)
        {
            for (int z = CentreZ - _halfSpan; z <= CentreZ + _halfSpan; z++)
            {
                if (InTunnelPlan(x, z))
                {
                    cells += _roofY - _floorY + 1;
                }
            }
        }

        return cells;
    }

    protected override bool IsDug(int x, int y, int z)
    {
        bool shaft = Math.Abs(x - CentreX) <= _shaftHalf && Math.Abs(z - CentreZ) <= _shaftHalf;
        bool tunnel = Math.Abs(x - CentreX) <= _halfSpan
            && Math.Abs(z - CentreZ) <= _halfSpan
            && InTunnelPlan(x, z);

        return (shaft && y >= _floorY && y <= SurfaceY)
            || (tunnel && y >= _floorY && y <= _roofY);
    }

    private bool InTunnelPlan(int x, int z) =>
        Math.Abs(Mod(x - CentreX + _bore, _spacing) - _bore) <= _bore
        || Math.Abs(Mod(z - CentreZ + _bore, _spacing) - _bore) <= _bore;
}

/// <summary>
/// A block of bitten chunks — <i>n</i> × <i>n</i> × <i>n</i> voids of 8 m — with a shaft from the
/// surface into the middle of its roof.
/// </summary>
/// <remarks>
/// <para>
/// <b>The shape the play made normal and the cost model never priced.</b> M8-PLAN's ~14,400 cells
/// and ~50,000-cell revisit trigger come from SCALE's cave; <see cref="MineTerrain"/> is bores by
/// construction. One bitten chunk under a lake is 32,768 cells of void, and the entangled miner
/// takes one every quarter of a second.
/// </para>
/// <para>
/// <b>A shaft as wide as the block is the lake bed falling in.</b> Pass a bore of 32 voxels or
/// more and the mouth is the whole roof — 1,024 cells per chunk of footprint — which is the case
/// where a chunk is bitten out from directly under standing water. A narrow bore is the other
/// case: a chamber dug from a tunnel and then breached.
/// </para>
/// </remarks>
internal sealed class ChamberTerrain : DugTerrain
{
    private readonly int _chambers;
    private readonly int _halfSpan;
    private readonly int _shaftHalf;
    private readonly int _floorY, _roofY;
    private readonly int _depthMetres;

    public ChamberTerrain(
        WorldGenerator generator, int centreX, int centreZ, int shaftVoxels, int depthMetres, int chambers)
        : base(
            generator,
            centreX,
            centreZ,
            chambers * VoxelConstants.ChunkSize / 2,
            generator.SurfaceHeightAt(centreX, centreZ)
                - (depthMetres * VoxelConstants.VoxelsPerMetre)
                - (chambers * VoxelConstants.ChunkSize) + 1)
    {
        _chambers = chambers;
        _halfSpan = chambers * VoxelConstants.ChunkSize / 2;
        _shaftHalf = Math.Min(shaftVoxels, 2 * _halfSpan) / 2;
        _depthMetres = depthMetres;

        _roofY = SurfaceY - (depthMetres * VoxelConstants.VoxelsPerMetre);
        _floorY = _roofY - (chambers * VoxelConstants.ChunkSize) + 1;
    }

    public override string Describe() =>
        $"a {_chambers} x {_chambers} x {_chambers} block of bitten 8 m chunks "
        + $"({_chambers * 8} m on a side), its roof {_depthMetres} m under the surface";

    /// <summary>The Y of the block's floor, and half its width in voxels: the lake arm's sheet.</summary>
    public int FloorY => _floorY;

    public int HalfSpan => _halfSpan;

    public override IEnumerable<VoxelPos> Mouth()
    {
        // The same footprint IsDug gives the shaft: the whole block when the roof is open, else
        // the odd-width bore centred on the block.
        int width = ShaftWidth;
        int start = width == 2 * _halfSpan ? -_halfSpan : -(width / 2);
        for (int dx = 0; dx < width; dx++)
        {
            for (int dz = 0; dz < width; dz++)
            {
                yield return new VoxelPos(CentreX + start + dx, SurfaceY, CentreZ + start + dz);
            }
        }
    }

    public override long VoidCells() =>
        (long)_chambers * _chambers * _chambers * VoxelConstants.ChunkVolume
        + ((long)ShaftWidth * ShaftWidth * (SurfaceY - _roofY));

    /// <summary>Voxels across the shaft: odd for a bore, the block's full width for the open roof.</summary>
    private int ShaftWidth => 2 * _shaftHalf + 1 == 2 * _halfSpan ? 2 * _halfSpan : (2 * _shaftHalf) + 1;

    protected override bool IsDug(int x, int y, int z)
    {
        // The block is chunk-aligned in size, so it is [centre - half, centre + half) on each axis:
        // exactly chambers * 32 voxels, never one more.
        bool inBlock = x >= CentreX - _halfSpan && x < CentreX + _halfSpan
            && z >= CentreZ - _halfSpan && z < CentreZ + _halfSpan
            && y >= _floorY && y <= _roofY;
        if (inBlock)
        {
            return true;
        }

        int half = ShaftWidth / 2;
        bool inShaft = ShaftWidth == 2 * _halfSpan
            ? x >= CentreX - half && x < CentreX + half && z >= CentreZ - half && z < CentreZ + half
            : Math.Abs(x - CentreX) <= half && Math.Abs(z - CentreZ) <= half;
        return inShaft && y > _roofY && y <= SurfaceY;
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
        int? depthMetres,
        int settleCap,
        string shape,
        int chambers,
        double budgetMillis,
        string solve,
        int settleShare,
        SpreadRule[] spreads)
    {
        if (solve is not ("wait" or "solve" or "both"))
        {
            Console.Error.WriteLine($"--fluidsolve must be wait, solve or both, not '{solve}'.");
            return 2;
        }

        // Each shape has its own sensible depth: a mine sits well under the surface, a bitten chunk
        // under a lake has its roof a couple of metres down, so the lake bed is the thing that
        // falls in rather than a bore.
        int depth = depthMetres ?? (shape == "chamber" ? 2 : 24);
        if (shape is not ("mine" or "chamber"))
        {
            Console.Error.WriteLine($"--fluidshape must be mine or chamber, not '{shape}'.");
            return 2;
        }

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
        DugTerrain example = Make(generator, shape, bores[0], depth, spanMetres, chambers);
        Console.WriteLine(
            $"  Fixture: {example.Describe()}, fed by one bore from a {lakeLitres:N0} L lake.");
        Console.WriteLine(
            $"  The lake pours in as fast as the bore accepts it; what the mouth refuses is offered "
            + $"again next tick. {ticks:N0} ticks of flow at {TicksPerSecond:F0} Hz, then settle.");
        if (budgetMillis > 0)
        {
            Console.WriteLine(
                $"  Each flow tick is sliced under a {budgetMillis:F2} ms budget, one slice per "
                + "synthetic frame, back to back: slices/tick is the frames a tick would span in "
                + "the game, and the worst slice is the budget plus at most one granule of "
                + $"{FluidSimulation.VisitsPerDeadlineCheck} visits.");
        }

        if (solve != "wait")
        {
            Console.WriteLine(
                "  The 'solve' arm runs M8b-3b's sheet solve at every tick boundary of the settle "
                + "(--settleafter=0's case): the flow is identical, the settle is what changes. "
                + $"A sheet is re-levelled with one cell in {settleShare} unsettled"
                + (settleShare == 0 ? " (no threshold: the pre-slice arm)." : ", or a step in it."));
        }

        Console.WriteLine();

        Console.WriteLine(
            "  bore     void     delivered   drained   L/tick   L/s     hours to   peak     "
            + "visits   visits/s   ms/tick   ms/tick   held      panels   MiB    settle   conserved   arm");
        Console.WriteLine(
            "  voxels   cells    L           %         mean     mean    drain      active   /tick "
            + "   @2Hz       mean      max       cells                    ticks");
        Console.WriteLine(
            "  ------   ------   ---------   -------   ------   -----   --------   ------   "
            + "------   --------   -------   -------   -------   ------   ----   ------   ---------   -----");

        foreach (int bore in bores)
        {
            foreach (SpreadRule spread in spreads)
            {
                if (solve != "solve")
                {
                    RunArm(
                        Make(generator, shape, bore, depth, spanMetres, chambers),
                        lakeLitres, ticks, bore, settleCap, budgetMillis, solve: false, settleShare, spread);
                }

                if (solve != "wait")
                {
                    RunArm(
                        Make(generator, shape, bore, depth, spanMetres, chambers),
                        lakeLitres, ticks, bore, settleCap, budgetMillis, solve: true, settleShare, spread);
                }
            }
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

    /// <summary>
    /// The share slice's own arm: the play's shape, which no draining fixture reproduces. A lake
    /// at rest — a chamber block's floor filled level and asleep — with a few litres a tick drawn
    /// off one edge, the way the loaded edge of the played world's mine drains into the unloaded
    /// world. The settle at no threshold re-levels the whole lake at every boundary for that
    /// edge; at the share it stops its flood at the edge's disturbance. Both arms print what a
    /// pass costs, which is the number the play reported as a quarter-second hitch twice a second.
    /// </summary>
    internal static int RunLake(
        WorldGenerator generator, string machineControl, bool isDebug, int chambers, int ticks, int settleShare,
        SpreadRule[] spreads)
    {
        var example = new ChamberTerrain(generator, 0, 0, shaftVoxels: 1, depthMetres: 2, chambers);
        int cells = (2 * example.HalfSpan) * (2 * example.HalfSpan);
        Console.WriteLine("Lake bench — the settle's share against a lake at rest with a draining edge");
        if (isDebug)
        {
            Console.WriteLine("*** DEBUG BUILD — these timings are meaningless. Re-run with -c Release. ***");
        }

        Console.WriteLine();
        Console.WriteLine(machineControl);
        Console.WriteLine();
        Console.WriteLine(
            $"  Fixture: the floor of {example.Describe()} — one sheet of {cells:N0} cells, filled level "
            + $"at 8,000 mL a cell and asleep; {LakeDrainCells} cells along its west edge each give up "
            + $"{LakeDrainMillilitres} mL a tick ({LakeDrainCells * LakeDrainMillilitres / 1000.0:F1} L a tick), "
            + $"for {ticks:N0} ticks at {TicksPerSecond:F0} Hz. The settle runs at the game's interval "
            + $"({SheetSettle.DefaultSettleAfterTicks} quiet ticks; a draw is not inflow).");
        Console.WriteLine();
        Console.WriteLine(
            "  spread    share    passes   levelled   under    stepped   visited      visited    written      written    longest   solve     automaton   awake");
        Console.WriteLine(
            "                                         share              cells        /pass      cells        /pass      pass ms   ms/tick   visits      mean");
        Console.WriteLine(
            "  -------   ------   ------   --------   ------   -------   ----------   --------   ----------   --------   -------   -------   ---------   -------");

        // Warm by call count, on a small lake, both paths: the first pass over a 147,456-cell lake
        // otherwise runs in tier-0 code being promoted mid-flood, and read 1,411 ms against a
        // warm 24 — the watershed bench's finding, on the solve.
        foreach (SpreadRule spread in spreads)
        {
            foreach (int share in new[] { 0, settleShare })
            {
                RunLakeArm(new ChamberTerrain(generator, 0, 0, shaftVoxels: 1, depthMetres: 2, chambers: 2), 40, share, spread, print: false);
            }
        }

        foreach (SpreadRule spread in spreads)
        {
            foreach (int share in settleShare == 0 ? [0] : new[] { 0, settleShare })
            {
                RunLakeArm(new ChamberTerrain(generator, 0, 0, shaftVoxels: 1, depthMetres: 2, chambers), ticks, share, spread, print: true);
            }
        }

        Console.WriteLine();
        Console.WriteLine(
            "  'visited' is the cells the solve read; at no threshold it is the lake at every pass, and "
            + "at the share it is the share times the edge's disturbance. 'solve ms/tick' is the pass "
            + "cost averaged over every tick, passes or not.");
        return 0;
    }

    private const int LakeDrainCells = 10;
    private const int LakeDrainMillilitres = 800;

    private static void RunLakeArm(ChamberTerrain terrain, int ticks, int share, SpreadRule spread, bool print)
    {
        var sim = new FluidSimulation(terrain, spread: spread);
        int half = terrain.HalfSpan;
        int floorY = terrain.FloorY;
        for (int x = -half; x < half; x++)
        {
            for (int z = -half; z < half; z++)
            {
                sim.Grid.Fill(new VoxelPos(x, floorY, z), 8_000);
            }
        }

        // Bake every column the flood will read before anything is timed — the floor and the
        // ring of wall around it, which the flood asks about from the floor's edge. The fixture
        // evaluates the cave field for every voxel of a column on its first CanHold, and the first
        // pass over the lake otherwise pays for the columns inside the pass: 1,385 ms with none
        // baked and 480 ms with the ring left out, both reading as the solve's longest pass.
        for (int x = -half - 1; x <= half; x++)
        {
            for (int z = -half - 1; z <= half; z++)
            {
                terrain.CanHold(new VoxelPos(x, floorY, z));
            }
        }

        var settle = new SheetSettle(sim, SheetSettle.DefaultSettleAfterTicks, share);
        long visits = 0;
        long awake = 0;
        var clock = new Stopwatch();
        double tickMillis = 0;
        var firstPasses = new List<double>();
        for (int tick = 0; tick < ticks; tick++)
        {
            for (int i = 0; i < LakeDrainCells; i++)
            {
                sim.Remove(new VoxelPos(-half, floorY, -LakeDrainCells / 2 + i), LakeDrainMillilitres);
            }

            clock.Restart();
            FluidTickResult result = sim.Step();
            long passesBefore = settle.Settles;
            settle.AtBoundary();
            tickMillis += clock.Elapsed.TotalMilliseconds;
            visits += result.CellsVisited;
            awake += result.ActiveAfter;
            if (settle.Settles > passesBefore && firstPasses.Count < 5)
            {
                firstPasses.Add(settle.LastMillis);
            }
        }

        if (!print)
        {
            return;
        }

        // The first passes by name, because the first one over a lake pays for what it grows
        // into and a "longest pass" column cannot say whether that is the solve or a one-off.
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"           first passes: {string.Join(", ", firstPasses.Select(ms => ms.ToString("F2", CultureInfo.InvariantCulture)))} ms"));

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  {spread.ToString().ToLowerInvariant(),-7}   {share,6}   {settle.Settles,6:N0}   {settle.SheetsLevelled,8:N0}   {settle.SheetsUnderShare,6:N0}   "
            + $"{settle.SheetsStepped,7:N0}   {settle.CellsVisited,10:N0}   "
            + $"{(settle.Settles == 0 ? 0 : settle.CellsVisited / (double)settle.Settles),8:N0}   "
            + $"{settle.CellsWritten,10:N0}   "
            + $"{(settle.Settles == 0 ? 0 : settle.CellsWritten / (double)settle.Settles),8:N0}   "
            + $"{settle.LongestMillis,7:F2}   {settle.TotalMillis / ticks,7:F3}   {visits,9:N0}   {awake / (double)ticks,7:N0}"));
    }

    private static string Ticks(int value) =>
        value < 0 ? "never" : value.ToString("N0", CultureInfo.InvariantCulture);

    private static DugTerrain Make(
        WorldGenerator generator, string shape, int bore, int depth, int spanMetres, int chambers) =>
        shape == "chamber"
            ? new ChamberTerrain(
                generator, centreX: 0, centreZ: 0, shaftVoxels: bore, depthMetres: depth,
                chambers: chambers)
            : new MineTerrain(
                generator,
                shaftX: 0,
                shaftZ: 0,
                shaftVoxels: bore,
                depthMetres: depth,
                spanMetres: spanMetres,
                spacingMetres: 8,
                boreVoxels: 3,
                tunnelHeightVoxels: 8);

    private static void RunArm(
        DugTerrain terrain, long lakeLitres, int ticks, int bore, int settleCap, double budgetMillis,
        bool solve, int settleShare, SpreadRule spread)
    {
        long budgetTicks = budgetMillis > 0
            ? FluidSimulation.BudgetTicks(TimeSpan.FromMilliseconds(budgetMillis))
            : 0;

        long tunnelCells = terrain.VoidCells();
        VoxelPos[] mouth = [.. terrain.Mouth()];

        var sim = new FluidSimulation(terrain, spread: spread);
        long lake = lakeLitres * FluidConstants.MillilitresPerLitre;
        long offered = lake;

        // Promote the tick before anything is timed. Same reason the watershed bench warms by call
        // count rather than by wall clock: tiered compilation promotes after about thirty calls,
        // and a warm-up capped by time under-warms exactly when the subject gets slower.
        var warm = new FluidSimulation(terrain, spread: spread);
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

        // The budgeted arm's own figures: how many slices the ticks needed and what the dearest
        // slice cost, which is the overshoot the granule bounds.
        long totalSlices = 0;
        int longestTickSlices = 0;
        int slicedTicks = 0;
        double worstSliceMillis = 0;

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
            FluidTickResult result;
            if (budgetTicks > 0)
            {
                // Sliced the way Main slices it: one Step(deadline) per frame until the tick
                // completes. No frame between slices here, so the tick's wall clock is the sum
                // of its slices and the row's ms/tick stays comparable with the whole-tick arm.
                FluidTickResult? done;
                int slices = 0;
                do
                {
                    long sliceStart = Stopwatch.GetTimestamp();
                    done = sim.Step(sliceStart + budgetTicks);
                    double sliceMillis =
                        (Stopwatch.GetTimestamp() - sliceStart) * 1000.0 / Stopwatch.Frequency;
                    worstSliceMillis = Math.Max(worstSliceMillis, sliceMillis);
                    slices++;
                }
                while (done is null);

                result = done.Value;
                totalSlices += slices;
                longestTickSlices = Math.Max(longestTickSlices, slices);
                if (slices > 1)
                {
                    slicedTicks++;
                }
            }
            else
            {
                result = sim.Step();
            }

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

        // The analytic arm: the sheet solve at every boundary, which is --settleafter=0 with no
        // inflow, and the settle phase has none. Same sim, same state at the end of the flow.
        SheetSettle? sheets = solve ? new SheetSettle(sim, settleAfterTicks: 0, settleShare) : null;

        var settleClock = Stopwatch.StartNew();
        while (sim.ActiveCellCount > 0 && settleTicks < settleCap)
        {
            FluidTickResult tick = sim.Step();
            sheets?.AtBoundary();
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
            + $"{(conserved ? "exact" : "*** NO ***"),9}   {(solve ? "solve" : "wait")}/{spread.ToString().ToLowerInvariant()}"));

        string verdict = settled.Settled
            ? (sim.VerifyQuiescent() ? "reached rest, and the rest is real." : "*** STOPPED WITH MOVES AVAILABLE ***")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"HIT THE CAP with {sim.ActiveCellCount:N0} still awake — diffusive levelling "
                + $"rather than an oscillation; see FluidRules' termination proof.");

        if (budgetTicks > 0)
        {
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"           slice:  {budgetMillis:F2} ms budget — {totalSlices:N0} slice(s) over "
                + $"{flowTicks:N0} flow tick(s), {slicedTicks:N0} tick(s) needed more than one, "
                + $"longest {longestTickSlices:N0} slice(s), worst slice {worstSliceMillis:F3} ms"));
        }

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"           settle: down to {quietThreshold:N0} active in "
            + $"{Ticks(ticksToQuiet)} ticks, to 64 in {Ticks(ticksToNearlyStill)}; "
            + $"{settled.Ticks:N0} ticks total "
            + $"({settled.Ticks / TicksPerSecond / 60.0:F1} min of world time), "
            + $"{settleMillis:F0} ms of CPU, {settled.CellVisits:N0} visits — {verdict}"));

        if (sheets is not null)
        {
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"           solve:  {sheets.Settles:N0} pass(es) at share 1 in {sheets.SettleShare}, "
                + $"{sheets.SheetsLevelled:N0} levelled / "
                + $"{sheets.SheetsPacked:N0} packed / {sheets.SheetsSkipped:N0} left alone / "
                + $"{sheets.SheetsUnderShare:N0} left under share / {sheets.SheetsStepped:N0} levelled for a step, "
                + $"{sheets.CellsVisited:N0} cells visited, "
                + $"{sheets.CellsWritten:N0} written, {sheets.CellsWoken:N0} woken, largest sheet "
                + $"{sheets.LargestSheet:N0} cells, longest pass {sheets.LongestMillis:F2} ms, "
                + $"{sheets.TotalMillis:F0} ms in all"));
        }
    }
}
