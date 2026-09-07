using System.Diagnostics;
using System.Globalization;
using Overburden.Core.Fluid;
using Overburden.Core.Persistence;
using Overburden.Core.Voxels;
using Overburden.Core.Worldgen;

namespace Overburden.Bench;

/// <summary>
/// M8b-4c: a box walks across a flooded chamber block, evicting the panels it leaves and reading
/// back the ones it returns to, against a control that pauses and keeps every panel in RAM.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it prices</b>: the blob write and read on real lake shapes, per panel and in all; the
/// peak panel memory with and without eviction, which is the number the slice exists to move; the
/// visits, which should differ between the arms only by the arrival wakes; and at the end, with
/// everything loaded and settled, whether the automaton rests and the rest is real — the stranded
/// front is what the control arm cannot produce and the eviction arm must not.
/// </para>
/// <para>
/// No pipeline here: the box is a slab of chunk columns on a terrain that answers "not loaded"
/// outside it, the store is the in-memory one, and the sources are consulted in the pipeline's
/// order — the eviction window, then the store. The snapshot goes to the store the tick it is
/// taken, so the in-memory source is exercised only by a chunk back inside the same tick.
/// </para>
/// </remarks>
internal static class EvictBench
{
    private const double TicksPerSecond = 2.0;

    /// <summary>A terrain loaded only inside a slab of chunk columns.</summary>
    private sealed class SlabTerrain(DugTerrain inner) : IFluidTerrain
    {
        public int MinChunkX { get; set; } = int.MinValue;

        public int MaxChunkX { get; set; } = int.MaxValue;

        public bool CanHold(VoxelPos v) => IsLoaded(v) && inner.CanHold(v);

        public bool IsLoaded(VoxelPos v)
        {
            int cx = v.Chunk.X;
            return cx >= MinChunkX && cx <= MaxChunkX;
        }

        public bool IsChunkLoaded(ChunkPos chunk) => chunk.X >= MinChunkX && chunk.X <= MaxChunkX;
    }

    private sealed class ArmResult
    {
        public long Ticks, Visits, Evicted, Written, Restored, Reclaimed, BytesWritten, BytesRead, Filled;
        public double WriteMillis, ReadMillis, PeakPanelMiB, TickMillis;
        public int RestTicks;
        public bool Quiescent, Identity;
        public long TotalMillilitres, EvictedMillilitres, LoadedMillilitres;
    }

    internal static int Run(
        WorldGenerator generator, string machineControl, bool isDebug, int chambers, int width, int ticksPerStep, int layers)
    {
        var example = new ChamberTerrain(generator, 0, 0, shaftVoxels: 1, depthMetres: 2, chambers);
        int chunkMinX = (-example.HalfSpan) >> VoxelConstants.ChunkSizeLog2;
        int chunkMaxX = (example.HalfSpan - 1) >> VoxelConstants.ChunkSizeLog2;
        int wetCells = (2 * example.HalfSpan) * (2 * example.HalfSpan) * layers;

        Console.WriteLine("Evict bench — the panel leaves RAM with its chunk (M8b-4c), a box walking a flooded block");
        if (isDebug)
        {
            Console.WriteLine("*** DEBUG BUILD — these timings are meaningless. Re-run with -c Release. ***");
        }

        Console.WriteLine();
        Console.WriteLine(machineControl);
        Console.WriteLine();
        Console.WriteLine(
            $"  Fixture: {example.Describe()}, its bottom {layers} layer(s) full — {wetCells:N0} cells, "
            + $"{wetCells * (long)FluidConstants.CellCapacityMillilitres / 1_000_000.0:N1} kL — and a stack poured at "
            + $"the east end to keep a front moving. A box {width} chunk column(s) wide walks from west of the "
            + $"block to east of it and back, one column every {ticksPerStep} tick(s) at {TicksPerSecond:F0} Hz; "
            + $"columns {chunkMinX}..{chunkMaxX} are wet.");
        Console.WriteLine();
        Console.WriteLine(
            "  arm        ticks    visits      peak MiB   evicted   rows written   written KiB   write us/row   restored   reclaimed   read KiB   read us/panel   tick ms   rest ticks   quiescent   identity");
        Console.WriteLine(
            "  -------   ------   ---------   --------   -------   ------------   -----------   ------------   --------   ---------   --------   -------------   -------   ----------   ---------   --------");

        // Warm by call count on a small block, both arms — the watershed bench's finding, on the blob.
        foreach (bool evict in new[] { false, true })
        {
            RunArm(new ChamberTerrain(generator, 0, 0, shaftVoxels: 1, depthMetres: 2, chambers: 2), evict, width: 1, ticksPerStep: 1, layers: 1);
        }

        foreach (bool evict in new[] { false, true })
        {
            ArmResult r = RunArm(new ChamberTerrain(generator, 0, 0, shaftVoxels: 1, depthMetres: 2, chambers), evict, width, ticksPerStep, layers);
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {(evict ? "evict" : "keep"),-7}   {r.Ticks,6:N0}   {r.Visits,9:N0}   {r.PeakPanelMiB,8:F1}   {r.Evicted,7:N0}   {r.Written,12:N0}   "
                + $"{r.BytesWritten / 1024.0,11:N0}   {(r.Written == 0 ? 0 : r.WriteMillis * 1000.0 / r.Written),12:F1}   "
                + $"{r.Restored,8:N0}   {r.Reclaimed,9:N0}   {r.BytesRead / 1024.0,8:N0}   "
                + $"{(r.Restored == 0 ? 0 : r.ReadMillis * 1000.0 / r.Restored),13:F1}   "
                + $"{r.TickMillis / r.Ticks,7:F3}   {r.RestTicks,10:N0}   {(r.Quiescent ? "yes" : "*** NO ***"),9}   {(r.Identity ? "exact" : "*** NO ***"),8}"));
        }

        Console.WriteLine();
        Console.WriteLine(
            "  'peak MiB' is the most panel memory the arm held at any tick; the keep arm is the world's wet "
            + "chunks and the evict arm is the box's. 'evicted' is panels released; 'rows written' the ones "
            + "whose row was stale — a panel back on disk unchanged writes nothing. 'write us/row' is the "
            + "blob write plus the store's write, on the sim thread here and on the lane in the game; "
            + "'read us/panel' the store's read plus the blob read. 'quiescent' is asked with every chunk "
            + "loaded and the automaton settled: a no is a front stranded by the eviction. 'identity' is "
            + "held == filled + added + loaded − evicted.");
        return 0;
    }

    private static ArmResult RunArm(ChamberTerrain chamber, bool evict, int width, int ticksPerStep, int layers)
    {
        var terrain = new SlabTerrain(chamber);
        var sim = new FluidSimulation(terrain);
        var store = new InMemoryWorldStore();
        var result = new ArmResult();

        int half = chamber.HalfSpan;
        int floorY = chamber.FloorY;
        int chunkMinX = (-half) >> VoxelConstants.ChunkSizeLog2;
        int chunkMaxX = (half - 1) >> VoxelConstants.ChunkSizeLog2;

        // The wet chunks, for the arrivals: every chunk column the block spans, every chunk Y the
        // block's void spans, so a walk in either direction announces the same set.
        ChunkPos lo = new VoxelPos(-half, floorY, -half).Chunk;
        ChunkPos hi = new VoxelPos(half - 1, chamber.SurfaceY, half - 1).Chunk;
        var byColumn = new Dictionary<int, List<ChunkPos>>();
        for (int cx = lo.X; cx <= hi.X; cx++)
        {
            var column = new List<ChunkPos>();
            for (int cy = lo.Y; cy <= hi.Y; cy++)
            for (int cz = lo.Z; cz <= hi.Z; cz++)
            {
                column.Add(new ChunkPos(cx, cy, cz));
            }

            byColumn[cx] = column;
        }

        // Everything loaded to put the water down, asleep, bottom layers full; then a stack at
        // the east end so a front creeps for the whole walk.
        for (int x = -half; x < half; x++)
        for (int z = -half; z < half; z++)
        for (int layer = 0; layer < layers; layer++)
        {
            // Asleep, and outside the simulation's ledger: the identity below adds it back.
            result.Filled += FluidConstants.CellCapacityMillilitres
                - sim.Grid.Fill(new VoxelPos(x, floorY + layer, z), FluidConstants.CellCapacityMillilitres);
        }

        for (int x = -half - 1; x <= half; x++)
        for (int z = -half - 1; z <= half; z++)
        for (int layer = 0; layer <= layers; layer++)
        {
            chamber.CanHold(new VoxelPos(x, floorY + layer, z));
        }

        for (int up = 0; up < 4; up++)
        {
            sim.Add(new VoxelPos(half - 2, floorY + layers + 1 + up, 0), FluidConstants.CellCapacityMillilitres);
        }

        // The walk: the box's west column runs from west of the block to east of it, then back.
        var positions = new List<int>();
        for (int p = chunkMinX - width; p <= chunkMaxX + 1; p++) positions.Add(p);
        for (int p = chunkMaxX; p >= chunkMinX - width; p--) positions.Add(p);

        var clock = new Stopwatch();
        var pending = new Dictionary<ChunkPos, FluidPanelSnapshot>();
        var taken = new List<FluidPanelSnapshot>();

        void SetBox(int west)
        {
            int east = west + width - 1;
            var leaving = new List<ChunkPos>();
            var arriving = new List<ChunkPos>();
            foreach ((int cx, List<ChunkPos> column) in byColumn)
            {
                bool was = cx >= terrain.MinChunkX && cx <= terrain.MaxChunkX;
                bool now = cx >= west && cx <= east;
                if (was && !now) leaving.AddRange(column);
                if (!was && now) arriving.AddRange(column);
            }

            terrain.MinChunkX = west;
            terrain.MaxChunkX = east;

            if (evict)
            {
                foreach (ChunkPos chunk in leaving) sim.Evict(chunk);
            }

            foreach (ChunkPos chunk in arriving)
            {
                if (evict)
                {
                    if (sim.Reclaim(chunk))
                    {
                        result.Reclaimed++;
                    }
                    else if (pending.TryGetValue(chunk, out FluidPanelSnapshot? unwritten))
                    {
                        sim.Restore(unwritten);
                        result.Restored++;
                    }
                    else
                    {
                        clock.Restart();
                        byte[]? blob = store.ReadFluid(chunk);
                        if (blob is not null)
                        {
                            FluidPanelSnapshot snapshot = FluidPanelBlob.Read(blob, chunk);
                            result.ReadMillis += clock.Elapsed.TotalMilliseconds;
                            result.BytesRead += blob.Length;
                            sim.Restore(snapshot);
                            result.Restored++;
                        }
                    }
                }

                sim.ChunkArrived(chunk);
            }
        }

        var tickClock = new Stopwatch();
        var rows = new List<(ChunkPos, byte[])>();
        foreach (int west in positions)
        {
            SetBox(west);
            for (int t = 0; t < ticksPerStep; t++)
            {
                tickClock.Restart();
                FluidTickResult tick = sim.Step();
                result.TickMillis += tickClock.Elapsed.TotalMilliseconds;
                result.Ticks++;
                result.Visits += tick.CellsVisited;

                if (evict)
                {
                    taken.Clear();
                    sim.TakeEvicted(taken);
                    if (taken.Count > 0)
                    {
                        clock.Restart();
                        rows.Clear();
                        foreach (FluidPanelSnapshot panel in taken)
                        {
                            byte[] blob = FluidPanelBlob.Write(panel);
                            rows.Add((panel.Chunk, blob));
                            result.BytesWritten += blob.Length;
                        }

                        store.Write([], [], rows);
                        result.WriteMillis += clock.Elapsed.TotalMilliseconds;
                        result.Written += taken.Count;
                    }
                }

                double panelMiB = sim.Grid.PanelBytes / 1024.0 / 1024.0;
                if (panelMiB > result.PeakPanelMiB) result.PeakPanelMiB = panelMiB;
            }
        }

        // Everything back, then rest.
        SetBox(int.MinValue / 2);
        terrain.MinChunkX = int.MinValue;
        terrain.MaxChunkX = int.MaxValue;
        foreach (List<ChunkPos> column in byColumn.Values)
        foreach (ChunkPos chunk in column)
        {
            if (evict)
            {
                if (sim.Reclaim(chunk))
                {
                    result.Reclaimed++;
                }
                else if (store.ReadFluid(chunk) is { } blob)
                {
                    clock.Restart();
                    FluidPanelSnapshot snapshot = FluidPanelBlob.Read(blob, chunk);
                    result.ReadMillis += clock.Elapsed.TotalMilliseconds;
                    result.BytesRead += blob.Length;
                    sim.Restore(snapshot);
                    result.Restored++;
                }
            }

            sim.ChunkArrived(chunk);
        }

        FluidSettleResult settled = sim.Settle(20_000);
        result.RestTicks = settled.Settled ? settled.Ticks : -1;
        result.Quiescent = settled.Settled && sim.VerifyQuiescent();
        result.Evicted = sim.EvictedPanels;
        result.TotalMillilitres = sim.TotalMillilitres;
        result.EvictedMillilitres = sim.EvictedMillilitres;
        result.LoadedMillilitres = sim.LoadedMillilitres;
        result.Identity = sim.TotalMillilitres
            == result.Filled + sim.AddedMillilitres + sim.LoadedMillilitres
               - sim.EvictedMillilitres - sim.RemovedMillilitres - sim.DisplacedMillilitres;
        return result;
    }
}
