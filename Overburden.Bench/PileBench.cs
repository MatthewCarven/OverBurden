using System.Diagnostics;
using System.Globalization;
using Overburden.Core.Content;
using Overburden.Core.Fluid;
using Overburden.Core.Jobs;
using Overburden.Core.Machines;
using Overburden.Core.Streaming;
using Overburden.Core.Structure;
using Overburden.Core.Voxels;
using Overburden.Core.Worldgen;

namespace Overburden.Bench;

/// <summary>
/// IM-5b's bench, decision 15: what writing a dump's pile into the world costs a boundary — N voxels
/// over K chunk columns, dry and wet — through the real write path and every observer the game hangs
/// on it.
/// </summary>
/// <remarks>
/// <para>
/// <b>A real <see cref="ChunkPipeline"/>, which no bench had built before.</b> Every other arm uses a
/// hand-made world, and that is right for them; but the pile's cost is the hook's, and the hook is the
/// pipeline's light, remesh and support marks plus the game's subscriber chain (Main.cs's
/// <c>onVoxelChanged</c>: the field's invalidation, <c>Disturb</c>, the portal tracker, the machine
/// set, the enclosure). The wiring here copies Main's, less what needs a <c>WaterSystem</c> (the lake
/// ceiling, the water grids), which a pile on dry generated ground never reaches.
/// </para>
/// <para>
/// <b>The re-read's fact 5 is why it sweeps chunks as well as voxels.</b> Most of what a write costs is
/// paid once a chunk a batch (light, remesh, the support derive, copy-on-write); only the field's
/// 4 KiB clone, the seep index and the portal index are paid per voxel. The wet arm puts one cell of
/// water in each chunk column first, so the field holds those chunks and every write clones.
/// </para>
/// <para>
/// A boundary is timed in the game's order: the portal tracker's <c>Flush</c> (the seep rescan the last
/// boundary's writes earned), the writes, then the field's <c>Sync</c>. Between boundaries the pipeline
/// is updated as frames would update it, and that time is reported apart — it is light and the mesh
/// apply on the sim thread, under the 16 ms light budget <c>start.cmd</c> passes. <b>What a Core bench
/// cannot see</b> is the game's mesh install on the main thread for each remeshed chunk; the remesh
/// count is printed so the in-game probe can be read against it.
/// </para>
/// </remarks>
internal static class PileBench
{
    // Not the origin: generated worlds are special at lattice points.
    private const int CentreX = 1037, CentreZ = 1037;
    private const int ViewHorizontal = 3, ViewVertical = 2;
    private const int FieldBakesPerBoundary = 8;
    private const double FrameMillis = 500.0;

    public static int Run(
        WorldGenerator generator, MaterialTable materials, int pileMaterial, string control, bool debug,
        int[] perBoundary, int[] chunkColumns, int boundaries)
    {
        Console.WriteLine("IM-5b, decision 15: a dump's pile written through the real pipeline and the game's hook");
        Console.WriteLine(control);
        if (debug)
        {
            Console.WriteLine("  DEBUG BUILD — 3-10x slow; quote only a Release run.");
        }

        Console.WriteLine($"  {boundaries} boundaries an arm, a fresh world each; the view r{ViewHorizontal}h/{ViewVertical}v around ({CentreX}, {CentreZ}); light budget 16 ms");
        Console.WriteLine("  boundary = flush + write + sync, the sim thread's share of the boundary; 'first' is boundary 1 (the portal tracker's first-edit scans), the rest are the mean and worst of 2..n");
        Console.WriteLine();

        foreach (bool wet in new[] { false, true })
        {
            foreach (int columns in chunkColumns)
            {
                foreach (int voxels in perBoundary)
                {
                    Arm(generator, materials, pileMaterial, wet, columns, voxels, boundaries);
                }
            }
        }

        return 0;
    }

    private static void Arm(WorldGenerator generator, MaterialTable materials, int pileMaterial, bool wet, int columns, int voxels, int boundaries)
    {
        int side = (int)Math.Round(Math.Sqrt(columns));
        if (side * side != columns)
        {
            throw new ArgumentException($"--pilecolumns takes squares (1, 4, 9, 16, …); {columns} is not one");
        }

        FluidField? field = null;
        FluidSimulation? fluid = null;
        PortalTracker? tracker = null;
        Enclosure? enclosure = null;
        var machines = new MachineSet(materials);
        ChunkPipeline? pipeline = null;
        var faults = new List<string>();
        int meshes = 0;

        var viewers = new ViewerSet();
        using var scheduler = new JobScheduler(viewers);
        pipeline = new ChunkPipeline(
            generator,
            materials,
            scheduler,
            viewers,
            onMeshReady: (_, _) => meshes++,
            onJobFaulted: (position, error) => faults.Add($"{position}: {error.Message}"),
            lightBudget: TimeSpan.FromMilliseconds(16),
            onVoxelChanged: (voxel, material) =>
            {
                field?.Invalidate(voxel);
                fluid?.Disturb(voxel);
                tracker?.OnVoxelChanged(voxel);
                machines.OnVoxelChanged(voxel, material);
                enclosure?.VoxelChanged(voxel);
            },
            onVoxelsReady: (position, hasEdits) =>
            {
                machines.ChunkArrived(position, hasEdits, pipeline?.Streamer.Get(position)?.Voxels);
                enclosure?.ChunkArrived(position, hasEdits);
            },
            onChunkForgotten: position =>
            {
                tracker?.OnChunkForgotten(position);
                enclosure?.ChunkForgotten(position);
            },
            onFluidReady: (position, hasEdits) =>
            {
                tracker?.OnVoxelsReady(position, hasEdits);
                fluid?.ChunkArrived(position);
            });

        try
        {
            var terrain = new VoxelFluidTerrain(pipeline.Streamer, materials);
            var seed = new StreamerSeedHeights(pipeline.Streamer, generator);
            enclosure = new Enclosure(pipeline.Streamer, materials, seed);
            field = new FluidField(terrain);
            fluid = new FluidSimulation(field);
            pipeline.Fluid = fluid;
            tracker = new PortalTracker(new PortalIndex(seed, terrain), new SeepIndex(seed, terrain));
            ChunkStreamer streamer = pipeline.Streamer;
            pipeline.Support = new SupportRegion(position => streamer.Get(position)?.Voxels, materials, SupportRule.Default);

            int surface = generator.SurfaceHeightAt(CentreX, CentreZ);
            var centre = new ChunkPos(FloorDiv(CentreX, VoxelConstants.ChunkSize), FloorDiv(surface, VoxelConstants.ChunkSize), FloorDiv(CentreZ, VoxelConstants.ChunkSize));
            streamer.AddViewer(centre, ViewHorizontal, ViewVertical);
            Settle(pipeline, faults);

            // The pile's columns: K chunk columns about the centre, each with its 1,024 voxel columns'
            // ground found once, from the top of the loaded box down.
            int top = (centre.Y + ViewVertical + 1) * VoxelConstants.ChunkSize - 1;
            int floor = (centre.Y - ViewVertical) * VoxelConstants.ChunkSize;
            int origin = -(side / 2);
            var sites = new List<Site>();
            for (int i = 0; i < side; i++)
            for (int j = 0; j < side; j++)
            {
                var site = new Site(centre.X + origin + i, centre.Z + origin + j);
                for (int c = 0; c < Site.Columns; c++)
                {
                    (int x, int z) = site.Column(c);
                    site.Ground[c] = GroundAt(streamer, x, z, top, floor);
                }

                sites.Add(site);
            }

            if (wet)
            {
                // One cell of water on the ground at each site's first column, which the pile skips:
                // the panel makes the chunk and its neighbours reachable, and the field holds them.
                foreach (Site site in sites)
                {
                    (int x, int z) = site.Column(0);
                    fluid.Add(new VoxelPos(x, site.Ground[0] + 1, z), FluidConstants.CellCapacityMillilitres);
                }

                var all = new HashSet<ChunkPos>();
                fluid.ReachableChunks(all);
                field.Sync(all);
            }

            var reach = new HashSet<ChunkPos>();
            var late = new List<ChunkPos>();
            var written = new HashSet<ChunkPos>();
            var flush = new double[boundaries];
            var write = new double[boundaries];
            var sync = new double[boundaries];
            var update = new double[boundaries];
            var worstFrame = new double[boundaries];
            var chunks = new int[boundaries];
            var allocated = new long[boundaries];
            int refused = 0;
            long light0 = pipeline.LightBuilds, remesh0 = pipeline.EditRemeshes + pipeline.RelightRemeshes, derive0 = pipeline.SupportDerivesApplied;
            double worker0 = pipeline.SupportWorkerMillis;
            int meshes0 = meshes, clones0 = pipeline.ChunkClones;

            for (int b = 0; b < boundaries; b++)
            {
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                long t0 = Stopwatch.GetTimestamp();
                tracker.Flush();
                long t1 = Stopwatch.GetTimestamp();

                written.Clear();
                int share = voxels / sites.Count, remainder = voxels % sites.Count;
                for (int s = 0; s < sites.Count; s++)
                {
                    Site site = sites[s];
                    for (int n = share + (s == 0 ? remainder : 0); n > 0; n--)
                    {
                        VoxelPos voxel = site.Next(wet);
                        if (pipeline.SetVoxel(voxel, pileMaterial))
                        {
                            written.Add(voxel.Chunk);
                        }
                        else
                        {
                            refused++;
                        }
                    }
                }

                long t2 = Stopwatch.GetTimestamp();
                reach.Clear();
                late.Clear();
                fluid.ReachableChunks(reach);
                field.Sync(reach, FieldBakesPerBoundary, late);
                long t3 = Stopwatch.GetTimestamp();
                allocated[b] = GC.GetAllocatedBytesForCurrentThread() - a0;

                flush[b] = Millis(t0, t1);
                write[b] = Millis(t1, t2);
                sync[b] = Millis(t2, t3);
                chunks[b] = written.Count;
                (update[b], worstFrame[b]) = Frames(pipeline);
            }

            var settle = Stopwatch.StartNew();
            Settle(pipeline, faults);
            double settleMillis = settle.Elapsed.TotalMilliseconds;

            double perVoxel = Mean(write, 1) * 1000.0 / voxels;
            string arm = $"{(wet ? "wet" : "dry"),-3}  K {columns,2}  N {voxels,5}";
            Console.WriteLine(
                $"{arm} | {Mean(chunks, 1):F1} chunk(s) written a boundary | boundary {Mean(flush, write, sync, 1):F2} ms mean / "
                + $"{Max(flush, write, sync, 1):F2} worst, first {flush[0] + write[0] + sync[0]:F2} | write {Mean(write, 1):F2} "
                + $"({perVoxel:F2} µs a voxel), flush {Mean(flush, 1):F2} / {Max(flush, 1):F2}, sync {Mean(sync, 1):F2} / {Max(sync, 1):F2} | "
                + $"{Mean(allocated, 1) / 1024.0:N0} KiB allocated");
            Console.WriteLine(
                $"{"",-17} | frames after: {Mean(update, 1):F2} ms of update a boundary, worst frame {Max(worstFrame, 1):F2} ms | "
                + $"a boundary: {(pipeline.LightBuilds - light0) / (double)boundaries:F1} light build(s), "
                + $"{(pipeline.EditRemeshes + pipeline.RelightRemeshes - remesh0) / (double)boundaries:F1} remesh mark(s), "
                + $"{(meshes - meshes0) / (double)boundaries:F1} mesh(es) delivered, "
                + $"{(pipeline.SupportDerivesApplied - derive0) / (double)boundaries:F1} support derive(s) "
                + $"({(pipeline.SupportWorkerMillis - worker0) / boundaries:F2} ms of worker), "
                + $"{(pipeline.ChunkClones - clones0) / (double)boundaries:F1} chunk clone(s) | settled {settleMillis:N0} ms after"
                + (refused > 0 ? $" | {refused} write(s) refused" : string.Empty));

            if (faults.Count > 0)
            {
                Console.WriteLine($"  FAULTED: {string.Join("; ", faults.Take(3))}");
            }
        }
        finally
        {
            pipeline.Dispose();
        }
    }

    /// <summary>A chunk column of the pile: its ground, and a layer fill over its 1,024 voxel columns.</summary>
    private sealed class Site(int chunkX, int chunkZ)
    {
        public const int Columns = VoxelConstants.ChunkSize * VoxelConstants.ChunkSize;

        private int _cursor;

        public int[] Ground { get; } = new int[Columns];

        public (int X, int Z) Column(int index) =>
            ((chunkX * VoxelConstants.ChunkSize) + (index % VoxelConstants.ChunkSize), (chunkZ * VoxelConstants.ChunkSize) + (index / VoxelConstants.ChunkSize));

        /// <summary>The next voxel: one layer over every column, then the next layer. The wet arm skips its water's column.</summary>
        public VoxelPos Next(bool wet)
        {
            int index = _cursor % Columns;
            if (wet && index == 0)
            {
                _cursor++;
                index = 1;
            }

            int layer = _cursor / Columns;
            _cursor++;
            (int x, int z) = Column(index);
            return new VoxelPos(x, Ground[index] + 1 + layer, z);
        }
    }

    private static int GroundAt(ChunkStreamer streamer, int x, int z, int top, int floor)
    {
        for (int y = top; y >= floor; y--)
        {
            if (streamer.TryGetMaterial(new VoxelPos(x, y, z), out int material) && material != MaterialIds.AirNumericId)
            {
                return y;
            }
        }

        return floor;
    }

    /// <summary>The frames until the next boundary: the pipeline updated as the game would, its time summed.</summary>
    private static (double Total, double Worst) Frames(ChunkPipeline pipeline)
    {
        double total = 0, worst = 0;
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed.TotalMilliseconds < FrameMillis)
        {
            long t0 = Stopwatch.GetTimestamp();
            pipeline.Update();
            double millis = Millis(t0, Stopwatch.GetTimestamp());
            total += millis;
            worst = Math.Max(worst, millis);
            Thread.Sleep(1);
        }

        return (total, worst);
    }

    private static void Settle(ChunkPipeline pipeline, List<string> faults)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            pipeline.Update();
            if (pipeline.IsQuiescent)
            {
                if (faults.Count > 0)
                {
                    throw new InvalidOperationException($"a job faulted: {faults[0]}");
                }

                return;
            }

            if (clock.ElapsedMilliseconds > 120_000)
            {
                throw new TimeoutException($"the pipeline did not settle: {pipeline.JobsInFlight} in flight, {pipeline.WorkListCount} queued");
            }

            Thread.Sleep(1);
        }
    }

    private static double Millis(long from, long to) => (to - from) * 1000.0 / Stopwatch.Frequency;

    private static double Mean(double[] values, int skip) => values.Skip(skip).DefaultIfEmpty(0).Average();

    private static double Mean(int[] values, int skip) => values.Skip(skip).Select(v => (double)v).DefaultIfEmpty(0).Average();

    private static double Mean(long[] values, int skip) => values.Skip(skip).Select(v => (double)v).DefaultIfEmpty(0).Average();

    private static double Mean(double[] a, double[] b, double[] c, int skip) =>
        Enumerable.Range(skip, Math.Max(0, a.Length - skip)).Select(i => a[i] + b[i] + c[i]).DefaultIfEmpty(0).Average();

    private static double Max(double[] values, int skip) => values.Skip(skip).DefaultIfEmpty(0).Max();

    private static double Max(double[] a, double[] b, double[] c, int skip) =>
        Enumerable.Range(skip, Math.Max(0, a.Length - skip)).Select(i => a[i] + b[i] + c[i]).DefaultIfEmpty(0).Max();

    private static int FloorDiv(int value, int divisor) => (int)Math.Floor(value / (double)divisor);
}
