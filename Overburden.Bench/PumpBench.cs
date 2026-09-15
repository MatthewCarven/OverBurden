using System.Diagnostics;
using Overburden.Core.Building;
using Overburden.Core.Content;
using Overburden.Core.Fluid;
using Overburden.Core.Machines;
using Overburden.Core.Voxels;
using Overburden.Core.Worldgen;

namespace Overburden.Bench;

/// <summary>
/// Bodies solid to the water over a dug fixture: what <c>Place.Machine</c> leaves in the game,
/// on a terrain that has no notion of a placed voxel.
/// </summary>
/// <remarks>
/// One dictionary lookup per <c>CanHold</c> on top of the bitset's, in every visit — which is not
/// what this bench measures (the tick is), and is noted because a <c>--fluid</c> figure is not
/// comparable with one taken through this wrapper.
/// </remarks>
internal sealed class BodiedTerrain : IFluidTerrain
{
    private readonly IFluidTerrain _inner;
    private readonly MachineSet _machines;
    private readonly WorldGenerator _generator;
    private readonly int _centreX, _centreZ, _halfSpan, _band;
    private readonly Dictionary<(int, int), int> _surface = [];

    /// <param name="band">Voxels of open air kept over the local seed surface inside the footprint, where the fixture's bitset stops at four over its centre's.</param>
    public BodiedTerrain(DugTerrain inner, MachineSet machines, WorldGenerator generator, int halfSpan, int band)
    {
        _inner = inner;
        _machines = machines;
        _generator = generator;
        _centreX = inner.CentreX;
        _centreZ = inner.CentreZ;
        _halfSpan = halfSpan;
        _band = band;
    }

    public bool CanHold(VoxelPos v)
    {
        if (_machines.TryGet(BrushCell.FromVoxel(v), out _))
        {
            return false;
        }

        if (_inner.CanHold(v))
        {
            return true;
        }

        if (Math.Abs(v.X - _centreX) > _halfSpan || Math.Abs(v.Z - _centreZ) > _halfSpan)
        {
            return false;
        }

        int surface = SurfaceAt(v.X, v.Z);
        return v.Y > surface && v.Y <= surface + _band;
    }

    /// <summary>The seed surface at a column, cached.</summary>
    public int SurfaceAt(int x, int z)
    {
        if (!_surface.TryGetValue((x, z), out int y))
        {
            y = _generator.SurfaceHeightAt(x, z);
            _surface[(x, z)] = y;
        }

        return y;
    }
}

/// <summary>
/// M10a-3: the relay pump's delivered rate — the number every pump cap in the ladder is fiction
/// until — and what the machine tick costs at 1, 100 and 500 pumps.
/// </summary>
/// <remarks>
/// <para>
/// <b>The delivered figure is the automaton's throughput into a 3 × 3 × 3 box, not the pump's
/// setting.</b> A pump takes its budget from the cells around its body; what it can take next
/// boundary is what the water around it managed to put back in one tick and one settle. The
/// nominal 100 L/s is 50 L a boundary, 3.2 cells; the box's floor ring is 8 cells × 16 voxels;
/// whether the sheet refills them at that rate is the measurement.
/// </para>
/// <para>
/// <b>The outlet on the surface is drained by a sink standing in for the hand-off</b>: every
/// boundary, everything held above the seed surface is removed (counted as <c>removed</c>, a
/// term of the identity), so the outlet never fills and the emitted water never spreads back
/// down the shaft. The loop arm is the opposite by design — the outlet inside the chamber, so
/// what it pours falls back toward the pump — and its oracle is the world's total unchanged to
/// the millilitre over every boundary.
/// </para>
/// <para>
/// <b>Read priced both ways</b>: the box through <c>AmountAt</c> — one panel lookup and one
/// array read a voxel, what the tick does — against a walk of the published wet bitset copy,
/// the read the ladder's decision 14 first proposed, on the same grid.
/// </para>
/// </remarks>
internal static class PumpBench
{
    private const long BoundaryMillis = MachineTick.DefaultBoundaryMillis;
    private const int Cap = FluidConstants.CellCapacityMillilitres;
    private const double Litre = FluidConstants.MillilitresPerLitre;

    public static int Run(
        WorldGenerator generator,
        MaterialTable materials,
        int pumpId,
        int widePumpId,
        int outletId,
        string machineControl,
        bool isDebug,
        long litres,
        double? rateLitresPerSecond,
        int boundaries)
    {
        MachineDefinition pump = materials.Machine(pumpId)!;
        long nominal = rateLitresPerSecond is { } r ? (long)Math.Round(r * Litre) : pump.MillilitresPerSecond;

        Console.WriteLine($"Pump bench — M10a-3: the relay pump on a flooded chamber, {nominal / Litre:N0} L/s nominal, {BoundaryMillis} ms a boundary");
        if (isDebug)
        {
            Console.WriteLine("*** DEBUG BUILD — these timings are meaningless. Re-run with -c Release. ***");
        }

        Console.WriteLine();
        Console.WriteLine(machineControl);
        Console.WriteLine();
        Console.WriteLine(
            $"  Fixture: one bitten 8 m chunk with a 3-voxel shaft to the surface, its roof 2 m under, flooded with {litres:N0} L "
            + "and settled; a relay pump on its floor's centre, a relay outlet on the surface with a sink under it standing in for the hand-off.");
        Console.WriteLine(
            "  Per boundary: the settle, the sink, the tick, one flow tick. 'delivered' is what the pump moved over the window against its nominal rate —");
        Console.WriteLine(
            "  the automaton's throughput into the intake box, which is the number every pump cap in the ladder is fiction until.");
        Console.WriteLine();

        Console.WriteLine("  radius   boundaries   first five boundaries: offered / room / moved (mL)                       delivered    nominal   moves   stalled          active   tick ms       empty");
        Console.WriteLine("  cells                                                                                      L/s          L/s               no water/room    peak     mean / max    after");
        Console.WriteLine("  ------   ----------   -------------------------------------------------------------------   ---------    -------   -----   -------------    ------   -----------   -----");

        RunDelivery(generator, materials, pumpId, outletId, litres, nominal, boundaries, radius: 1);
        RunDelivery(generator, materials, widePumpId, outletId, litres, nominal, boundaries, radius: 2);

        Console.WriteLine();
        PriceTheRead(generator, materials, pumpId, litres);

        Console.WriteLine();
        Console.WriteLine("  The tick's cost by pump count, 200 boundaries each, every pump at 2 L/s so eight outlets on the surface have the room:");
        Console.WriteLine();
        Console.WriteLine("  pumps   water        tick ms mean   tick ms max   moves/boundary   stalled (no water / no room)   intake voxels/boundary");
        Console.WriteLine("  -----   ----------   ------------   -----------   --------------   ----------------------------   ----------------------");
        RunCost(generator, materials, pumpId, outletId, pumps: 1, wet: true);
        RunCost(generator, materials, pumpId, outletId, pumps: 100, wet: true);
        RunCost(generator, materials, pumpId, outletId, pumps: 500, wet: true);
        RunCost(generator, materials, pumpId, outletId, pumps: 500, wet: false);

        Console.WriteLine();
        RunLoop(generator, materials, pumpId, outletId, litres, nominal, Math.Max(boundaries, 10_000));

        Console.WriteLine();
        Console.WriteLine("  'delivered' under the nominal is the box refilling slower than the pump drains it; the intake radius is the lever and its cost");
        Console.WriteLine("  is the 'intake voxels' column (124 cells against 26). The loop's oracle is the total: a pump and an outlet move water and make none.");
        return 0;
    }

    /// <summary>The chamber, flooded and settled, with a pump on the floor and an outlet on the surface.</summary>
    private sealed class Scene
    {
        public Scene(WorldGenerator generator, MaterialTable materials, long litres, int chambers = 1)
        {
            Terrain = new ChamberTerrain(generator, centreX: 0, centreZ: 0, shaftVoxels: 3, depthMetres: 2, chambers: chambers);
            Machines = new MachineSet(materials);
            Bodied = new BodiedTerrain(Terrain, Machines, generator, halfSpan: Terrain.HalfSpan + 4, band: 12);
            Generator = generator;
            Fluid = new FluidSimulation(Bodied);
            Settle = new SheetSettle(Fluid, SheetSettle.DefaultSettleAfterTicks, SheetSettle.DefaultSettleShare);
            Tick = new MachineTick(Machines, materials, Fluid, Bodied, boundaryMillis: BoundaryMillis);
            Materials = materials;
            Litres = litres;
        }

        public ChamberTerrain Terrain { get; }

        public WorldGenerator Generator { get; }

        public MachineSet Machines { get; }

        public BodiedTerrain Bodied { get; }

        public FluidSimulation Fluid { get; }

        public SheetSettle Settle { get; }

        public MachineTick Tick { get; }

        public MaterialTable Materials { get; }

        public long Litres { get; }

        public long SinkRemoved { get; private set; }

        /// <summary>The cell on the chamber's floor under its centre, where the shaft comes down.</summary>
        public BrushCell FloorCell(int dx = 0, int dz = 0) =>
            BrushCell.FromVoxel(new VoxelPos(Terrain.CentreX + dx, Terrain.FloorY, Terrain.CentreZ + dz));

        /// <summary>The cell holding the first air voxel over the local seed surface, <paramref name="dx"/> voxels off the shaft — the --pump arm's shape.</summary>
        public BrushCell SurfaceCell(int dx, int dz = 0)
        {
            int x = Terrain.CentreX + dx, z = Terrain.CentreZ + dz;
            return BrushCell.FromVoxel(new VoxelPos(x, Bodied.SurfaceAt(x, z) + 1, z));
        }

        public MachineRow Place(BrushCell cell, int material, int channel = 0)
        {
            var row = new MachineRow(
                cell, material, BrushFacing.PositiveX,
                [new KeyValuePair<string, long>(MachineTick.ChannelPort, channel), new KeyValuePair<string, long>("weight", 1)]);
            Machines.Add(row);
            return row;
        }

        /// <summary>Floods the block bottom-up until the litres are down, then settles it.</summary>
        public void Flood()
        {
            long remaining = Litres * FluidConstants.MillilitresPerLitre;
            int half = Terrain.HalfSpan;
            for (int y = Terrain.FloorY; remaining > 0 && y <= Terrain.SurfaceY; y++)
            for (int z = Terrain.CentreZ - half; z < Terrain.CentreZ + half && remaining > 0; z++)
            for (int x = Terrain.CentreX - half; x < Terrain.CentreX + half && remaining > 0; x++)
            {
                var v = new VoxelPos(x, y, z);
                if (!Bodied.CanHold(v))
                {
                    continue;
                }

                long give = Math.Min(Cap, remaining);
                long refused = Fluid.Add(v, give);
                remaining -= give - refused;
            }

            if (remaining > 0)
            {
                throw new InvalidOperationException($"the fixture could not hold {remaining / Litre:N0} L of the flood");
            }

            Fluid.Settle(2_000);
            Settle.Run();
        }

        /// <summary>Removes everything held above the seed surface — the hand-off's stand-in. Returns the millilitres.</summary>
        public long Sink()
        {
            _above.Clear();
            foreach (VoxelPos cell in Fluid.Grid.HeldCells())
            {
                if (cell.Y > Bodied.SurfaceAt(cell.X, cell.Z))
                {
                    _above.Add(cell);
                }
            }

            long removed = 0;
            foreach (VoxelPos cell in _above)
            {
                removed += Fluid.Remove(cell, Cap);
            }

            SinkRemoved += removed;
            return removed;
        }

        private readonly List<VoxelPos> _above = [];

        /// <summary>One boundary in the game's order: the settle, the sink, the machines, then one tick.</summary>
        public MachineTickReport Boundary(bool sink = true)
        {
            Settle.AtBoundary();
            if (sink)
            {
                Sink();
            }

            MachineTickReport report = Tick.Run();
            Fluid.Step();
            return report;
        }

        /// <summary>Millilitres in a pump's intake box.</summary>
        public long InBox(BrushCell cell, int radius)
        {
            long held = 0;
            foreach ((int dx, int dy, int dz) in MachineTick.IntakeCellsAt(radius))
            {
                BrushCell neighbour = cell.Offset(dx, dy, dz);
                for (int lz = 0; lz < BrushCell.Voxels; lz++)
                for (int lx = 0; lx < BrushCell.Voxels; lx++)
                for (int ly = 0; ly < BrushCell.Voxels; ly++)
                {
                    held += Fluid.Grid.AmountAt(neighbour.VoxelAt(lx, ly, lz));
                }
            }

            return held;
        }

        /// <summary>Free millilitres in the layer above an outlet's top where the terrain holds.</summary>
        public long RoomOver(BrushCell outlet)
        {
            long room = 0;
            for (int lz = 0; lz < BrushCell.Voxels; lz++)
            for (int lx = 0; lx < BrushCell.Voxels; lx++)
            {
                VoxelPos v = outlet.VoxelAt(lx, BrushCell.Voxels, lz);
                if (Bodied.CanHold(v))
                {
                    room += Fluid.Grid.FreeAt(v);
                }
            }

            return room;
        }

        public bool IdentityExact =>
            Fluid.TotalMillilitres == Fluid.AddedMillilitres + Fluid.EmittedMillilitres - Fluid.RemovedMillilitres
                - Fluid.DisplacedMillilitres - Fluid.PumpedMillilitres;
    }

    private static void RunDelivery(
        WorldGenerator generator, MaterialTable materials, int pumpId, int outletId, long litres, long nominal, int boundaries, int radius)
    {
        var scene = new Scene(generator, materials, litres);
        scene.Tick.RateOverrideMillilitresPerSecond = nominal;
        BrushCell pumpCell = scene.FloorCell();
        BrushCell outletCell = scene.SurfaceCell(dx: 12);
        scene.Place(pumpCell, pumpId);
        scene.Place(outletCell, outletId);
        scene.Flood();

        // Promote the tick before anything is timed: a fresh scene, a handful of boundaries.
        var warm = new Scene(generator, materials, Math.Min(litres, 20_000));
        warm.Tick.RateOverrideMillilitresPerSecond = nominal;
        warm.Place(warm.FloorCell(), pumpId);
        warm.Place(warm.SurfaceCell(dx: 12), outletId);
        warm.Flood();
        for (int i = 0; i < 40; i++)
        {
            warm.Boundary();
        }

        var first = new List<string>();
        long pumped = 0, moves = 0, noWater = 0, noRoom = 0;
        int peak = 0, emptyAfter = -1;
        double tickSum = 0, tickMax = 0;
        long start = scene.Fluid.TotalMillilitres;
        int ran = 0;
        for (int b = 0; b < boundaries; b++)
        {
            long offered = Math.Min(nominal * BoundaryMillis / 1000, scene.InBox(pumpCell, radius));
            long room = scene.RoomOver(outletCell);

            var clock = Stopwatch.StartNew();
            scene.Settle.AtBoundary();
            scene.Sink();
            long t0 = Stopwatch.GetTimestamp();
            MachineTickReport report = scene.Tick.Run();
            double tickMillis = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            FluidTickResult result = scene.Fluid.Step();
            _ = clock;

            if (!report.Exact)
            {
                throw new InvalidOperationException($"boundary {b}: {report}");
            }

            if (b < 5)
            {
                first.Add($"{offered:N0}/{room:N0}/{report.PumpedMillilitres:N0}");
            }

            pumped += report.PumpedMillilitres;
            moves += report.Moved;
            noWater += report.NoWater;
            noRoom += report.NoRoom;
            peak = Math.Max(peak, result.CellsVisited);
            tickSum += tickMillis;
            tickMax = Math.Max(tickMax, tickMillis);
            ran++;

            if (!scene.IdentityExact)
            {
                throw new InvalidOperationException($"boundary {b}: the identity is not exact");
            }

            if (scene.Fluid.TotalMillilitres == 0 || (report.NoWater > 0 && scene.InBox(pumpCell, radius) == 0 && scene.Fluid.ActiveCellCount == 0))
            {
                emptyAfter = b + 1;
                break;
            }
        }

        double seconds = ran * BoundaryMillis / 1000.0;
        double delivered = pumped / Litre / seconds;
        string left = emptyAfter < 0
            ? $"not in {ran:N0} ({scene.Fluid.TotalMillilitres / Litre:N0} L left)"
            : $"{emptyAfter:N0} ({scene.Fluid.TotalMillilitres / Litre:N0} L left)";
        Console.WriteLine(
            $"  {radius,-6}   {ran,10:N0}   {string.Join("  ", first),-67}   {delivered,9:F1}    {nominal / Litre,7:N0}   {moves,5:N0}   {noWater,6:N0}/{noRoom,-6:N0}   {peak,6:N0}   {tickSum / Math.Max(ran, 1),5:F3} / {tickMax,5:F2}   {left}");
        _ = start;
    }

    private static void PriceTheRead(WorldGenerator generator, MaterialTable materials, int pumpId, long litres)
    {
        var scene = new Scene(generator, materials, litres);
        BrushCell pumpCell = scene.FloorCell();
        scene.Place(pumpCell, pumpId);
        scene.Flood();

        // The published copy the ladder's decision 14 proposed reading: the wet bitsets as Publish
        // copies them out, one per panel.
        var published = new Dictionary<ChunkPos, ulong[]>();
        scene.Fluid.Grid.CollectWet(published);

        ReadOnlySpan<(int Dx, int Dy, int Dz)> cells = MachineTick.IntakeCellsAt(1);
        const int Reps = 20_000;
        long sink = 0;

        for (int pass = -1; pass < 3; pass++)
        {
            var amountClock = Stopwatch.StartNew();
            for (int rep = 0; rep < Reps; rep++)
            {
                sink += scene.InBox(pumpCell, 1);
            }

            double amountNs = amountClock.Elapsed.TotalMilliseconds * 1e6 / Reps;

            var bitsetClock = Stopwatch.StartNew();
            for (int rep = 0; rep < Reps; rep++)
            {
                int wet = 0;
                foreach ((int dx, int dy, int dz) in cells)
                {
                    BrushCell neighbour = pumpCell.Offset(dx, dy, dz);
                    for (int lz = 0; lz < BrushCell.Voxels; lz++)
                    for (int lx = 0; lx < BrushCell.Voxels; lx++)
                    for (int ly = 0; ly < BrushCell.Voxels; ly++)
                    {
                        if (FluidGrid.IsWetIn(published, neighbour.VoxelAt(lx, ly, lz)))
                        {
                            wet++;
                        }
                    }
                }

                sink += wet;
            }

            double bitsetNs = bitsetClock.Elapsed.TotalMilliseconds * 1e6 / Reps;

            var heldClock = Stopwatch.StartNew();
            for (int rep = 0; rep < Reps; rep++)
            {
                sink += scene.Fluid.Grid.HeldIn(pumpCell.OriginVoxel.Chunk);
            }

            double heldNs = heldClock.Elapsed.TotalMilliseconds * 1e6 / Reps;

            if (pass >= 0)
            {
                Console.WriteLine(
                    $"  The read, one pump, 26 cells = 1,664 voxels, pass {pass}: the box through AmountAt {amountNs / 1000:F2} µs "
                    + $"({amountNs / 1664:F1} ns a voxel) against the published bitset walk {bitsetNs / 1000:F2} µs ({bitsetNs / 1664:F1} ns a voxel); "
                    + $"one HeldIn {heldNs:F0} ns");
            }
        }

        _ = sink;
    }

    private static void RunCost(WorldGenerator generator, MaterialTable materials, int pumpId, int outletId, int pumps, bool wet)
    {
        // A 2 x 2 x 2 block, pumps on every other cell of its floor and the floors above, eight
        // outlets on the surface; 2 L/s each so the outlets have the room. The dry arm is the
        // same rows over a block with no water in it: the panel counts answer every intake.
        var scene = new Scene(generator, materials, litres: 3_500_000, chambers: 2);
        scene.Tick.RateOverrideMillilitresPerSecond = 2_000;
        int half = scene.Terrain.HalfSpan;
        int placed = 0;
        for (int cy = 0; placed < pumps && cy < 16; cy += 2)
        for (int cz = 0; placed < pumps && cz < 16; cz += 2)
        for (int cx = 0; placed < pumps && cx < 16; cx += 2)
        {
            var cell = BrushCell.FromVoxel(new VoxelPos(
                scene.Terrain.CentreX - half + (cx * BrushCell.Voxels),
                scene.Terrain.FloorY + (cy * BrushCell.Voxels),
                scene.Terrain.CentreZ - half + (cz * BrushCell.Voxels)));
            scene.Place(cell, pumpId);
            placed++;
        }

        for (int i = 0; i < 8; i++)
        {
            scene.Place(scene.SurfaceCell(dx: -28 + (i * 8), dz: 30), outletId);
        }

        if (wet)
        {
            scene.Flood();
        }

        for (int i = 0; i < 20; i++)
        {
            scene.Boundary();
        }

        const int Boundaries = 200;
        double sum = 0, max = 0;
        long moves = 0, noWater = 0, noRoom = 0;
        long voxelsBefore = scene.Tick.IntakeVoxelsRead;
        for (int b = 0; b < Boundaries; b++)
        {
            scene.Settle.AtBoundary();
            scene.Sink();
            long t0 = Stopwatch.GetTimestamp();
            MachineTickReport report = scene.Tick.Run();
            double millis = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            scene.Fluid.Step();
            sum += millis;
            max = Math.Max(max, millis);
            moves += report.Moved;
            noWater += report.NoWater;
            noRoom += report.NoRoom;
            if (!report.Exact)
            {
                throw new InvalidOperationException($"boundary {b}: {report}");
            }
        }

        Console.WriteLine(
            $"  {placed,5:N0}   {(wet ? "flooded" : "every panel dry"),-10}   {sum / Boundaries,12:F3}   {max,11:F3}   {moves / (double)Boundaries,14:F1}   {noWater,12:N0} / {noRoom,-12:N0}   {(scene.Tick.IntakeVoxelsRead - voxelsBefore) / (double)Boundaries,22:N0}");
    }

    private static void RunLoop(WorldGenerator generator, MaterialTable materials, int pumpId, int outletId, long litres, long nominal, int boundaries)
    {
        var scene = new Scene(generator, materials, litres);
        scene.Tick.RateOverrideMillilitresPerSecond = nominal;
        BrushCell pumpCell = scene.FloorCell();
        scene.Place(pumpCell, pumpId);

        // The outlet inside the chamber, two cells east of the pump and five up — its mouth in
        // the air over the flood, since a mouth under water has no room: what it pours falls
        // back toward the pump — the player's loop, not a rule's defect.
        scene.Place(scene.FloorCell(dx: 8).Offset(0, 5, 0), outletId);
        scene.Flood();

        long total = scene.Fluid.TotalMillilitres;
        long moved = 0;
        double tickSum = 0;
        var clock = Stopwatch.StartNew();
        for (int b = 0; b < boundaries; b++)
        {
            scene.Settle.AtBoundary();
            long t0 = Stopwatch.GetTimestamp();
            MachineTickReport report = scene.Tick.Run();
            tickSum += (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            scene.Fluid.Step();
            moved += report.PumpedMillilitres;
            if (!report.Exact || scene.Fluid.TotalMillilitres != total || !scene.IdentityExact)
            {
                throw new InvalidOperationException(
                    $"loop, boundary {b}: total {scene.Fluid.TotalMillilitres:N0} against {total:N0}, {report}");
            }
        }

        Console.WriteLine(
            $"  The loop: the outlet inside the chamber, {boundaries:N0} boundaries in {clock.Elapsed.TotalSeconds:F1} s — "
            + $"{moved / Litre:N0} L moved ({moved / Litre / (boundaries * BoundaryMillis / 1000.0):F1} L/s against {nominal / Litre:N0} nominal), "
            + $"the world's total {total / Litre:N0} L unchanged at every boundary, the identity exact at every boundary, "
            + $"tick {tickSum / boundaries:F3} ms mean, displaced {scene.Fluid.DisplacedMillilitres} mL");
    }
}
