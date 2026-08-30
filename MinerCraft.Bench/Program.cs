using System.Diagnostics;
using System.Globalization;
using MinerCraft.Core.Content;
using MinerCraft.Core.Lighting;
using MinerCraft.Core.Voxels;
using MinerCraft.Core.Worldgen;

namespace MinerCraft.Bench;

/// <summary>
/// Times worldgen over a fixed region and reports what coarsening the cave lattice costs.
/// </summary>
/// <remarks>
/// <para>
/// Worldgen is pure Core — no Godot, no renderer, no scene — so measuring it through the
/// game would only add noise from streaming and drawing. This exists because M2d-1's lesson
/// was that a measurement cheap enough to repeat is what produces findings: the cave
/// optimisation is a trade of accuracy for speed, and quoting one half of that without the
/// other is how a regression gets shipped as a win.
/// </para>
/// <para>
/// The region is deliberately fixed at the one M2b measured — 16 × 8 × 16 chunks, 128 m ×
/// 64 m × 128 m, straddling the surface — so today's numbers can be read against that
/// entry in WORKLOG.md rather than only against each other.
/// </para>
/// </remarks>
internal static class Program
{
    private const int RegionX = 16;
    private const int RegionZ = 16;
    private const int RegionMinChunkY = -4;
    private const int RegionMaxChunkY = 3;

    // Any three distinct non-air ids do here. The bench counts air against not-air and
    // measures storage size, and neither depends on which numbers the registry handed out.
    private const int Grass = 1;
    private const int Dirt = 2;
    private const int Stone = 3;

    private const string Usage = """
        Usage: dotnet run --project MinerCraft.Bench -c Release -- [options]

          --steps 1,2,4,8,16   cave lattice steps to measure (default 1,2,4,8,16)
          --seed  <ulong>      world seed (default: the game's own)
          --repeat <n>         timed passes per configuration, best wins (default 3)
          --light              time light derivation and propagation instead of worldgen
        """;

    private static int Main(string[] args)
    {
        ulong seed = 0x4D494E455243UL;
        int[] steps = [1, 2, 4, 8, 16];
        int repeat = 3;
        bool light = false;

        try
        {
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--seed":
                        seed = ulong.Parse(Next(args, ref i), CultureInfo.InvariantCulture);
                        break;
                    case "--steps":
                        steps = Next(args, ref i)
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Select(part => int.Parse(part, CultureInfo.InvariantCulture))
                            .ToArray();
                        break;
                    case "--repeat":
                        repeat = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture);
                        break;
                    case "--light":
                        light = true;
                        break;
                    case "--help" or "-h":
                        Console.WriteLine(Usage);
                        return 0;
                    default:
                        throw new ArgumentException($"unknown argument '{args[i]}'");
                }
            }
        }
        catch (Exception error) when (error is ArgumentException or FormatException)
        {
            Console.Error.WriteLine(error.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(Usage);
            return 1;
        }

        if (!steps.Contains(1))
        {
            // Step 1 is per-voxel evaluation, so it is both the speed baseline and the
            // accuracy reference. Without it every other row has nothing to be measured
            // against, which is exactly the half-a-trade this bench exists to prevent.
            steps = [1, .. steps];
        }

        ChunkPos[] region = Region();
        var strata = new StrataTable([new StrataLayer(Grass, 1), new StrataLayer(Dirt, 3)], Stone);

        if (light)
        {
            return LightBench(seed, repeat);
        }

        Console.WriteLine(
            $"Worldgen bench — {region.Length} chunks "
            + $"({RegionX * VoxelConstants.ChunkSize / 4} m x "
            + $"{(RegionMaxChunkY - RegionMinChunkY + 1) * VoxelConstants.ChunkSize / 4} m x "
            + $"{RegionZ * VoxelConstants.ChunkSize / 4} m), seed {seed}, best of {repeat}");
        if (IsDebug())
        {
            // Loudly, because a Debug number quoted in the worklog is worse than no number:
            // it looks like a measurement and is off by a factor nobody records.
            Console.WriteLine("*** DEBUG BUILD — these timings are meaningless. Re-run with -c Release. ***");
        }

        Console.WriteLine();

        WorldGenSettings BaseSettings(int step, bool caves) => new()
        {
            Seed = seed,
            CavesEnabled = caves,
            CaveLatticeStep = step,
        };

        // Caves off first: it is the floor the cave cost is measured against, and it is what
        // turns "6.55 ms/chunk" into "caves are 80% of generation".
        Measurement noCaves = Measure(BaseSettings(1, caves: false), strata, region, repeat);
        long rockVoxels = CountSolid(noCaves.Chunks);

        var rows = new List<Row>
        {
            new("caves off", noCaves, Carved: 0.0, Lost: null, Gained: null),
        };

        VoxelChunk[]? reference = null;

        foreach (int step in steps)
        {
            Measurement run = Measure(BaseSettings(step, caves: true), strata, region, repeat);
            reference ??= run.Chunks;

            double carved = (double)(rockVoxels - CountSolid(run.Chunks)) / rockVoxels;
            double? lost = null;
            double? gained = null;

            if (!ReferenceEquals(run.Chunks, reference))
            {
                (long lostVoxels, long gainedVoxels, long other) = CountDiffering(reference, run.Chunks);
                lost = (double)lostVoxels / rockVoxels;
                gained = (double)gainedVoxels / rockVoxels;

                if (other != 0)
                {
                    // Caves only ever remove material, so rock disagreeing with different
                    // rock is not a resampling artefact — it is a bug somewhere else.
                    Console.Error.WriteLine(
                        $"step {step}: {other} voxels differ in material without either being air");
                }
            }

            rows.Add(new(
                $"step {step}{(step == 1 ? " (exact)" : string.Empty)}", run, carved, lost, gained));
        }

        Report(rows, noCaves.MillisPerChunk);
        return 0;
    }

    private static void Report(List<Row> rows, double noCavesMillis)
    {
        Console.WriteLine(
            $"{"config",-16}{"ms/chunk",10}{"caves",8}{"storage",12}{"uniform",9}"
            + $"{"carved",9}{"lost",9}{"gained",9}");

        foreach (Row row in rows)
        {
            bool noCaves = row.Lost is null && row.Carved == 0.0;
            double caveShare = row.Run.MillisPerChunk <= 0.0
                ? 0.0
                : (row.Run.MillisPerChunk - noCavesMillis) / row.Run.MillisPerChunk;

            Console.WriteLine(
                $"{row.Label,-16}"
                + $"{row.Run.MillisPerChunk,10:F2}"
                + $"{(noCaves ? "—" : caveShare.ToString("P0", CultureInfo.InvariantCulture)),8}"
                + $"{FormatBytes(row.Run.TotalBytes),12}"
                + $"{(double)row.Run.UniformChunks / row.Run.Chunks.Length,9:P0}"
                + $"{(noCaves ? "—" : row.Carved.ToString("P3", CultureInfo.InvariantCulture)),9}"
                + $"{Percent(row.Lost),9}"
                + $"{Percent(row.Gained),9}");
        }

        Console.WriteLine();
        Console.WriteLine("caves   — share of generation time spent on caves, against the caves-off row");
        Console.WriteLine("carved  — fraction of rock removed, so a step that shifts cave *volume* shows up");
        Console.WriteLine("lost    — rock that per-voxel evaluation carved and this step left solid");
        Console.WriteLine("gained  — rock that this step carved and per-voxel evaluation left solid");
    }

    /// <summary>Generates the region <paramref name="repeat"/> times and keeps the fastest.</summary>
    /// <remarks>
    /// Best-of rather than mean, because everything that perturbs a pass on a desktop — a
    /// background process, a GC, the scheduler — makes it slower and nothing makes it
    /// faster. The mean measures the machine's mood; the minimum measures the code.
    /// </remarks>
    private static Measurement Measure(
        WorldGenSettings settings, StrataTable strata, ChunkPos[] region, int repeat)
    {
        var generator = new WorldGenerator(settings, strata);

        // Warm the JIT. Tiered compilation means the first pass measures the interpreter
        // warming up rather than the code, and at 2,048 chunks that is not a rounding error.
        Generate(generator, region);

        VoxelChunk[] best = [];
        double bestMillis = double.MaxValue;

        for (int pass = 0; pass < repeat; pass++)
        {
            long start = Stopwatch.GetTimestamp();
            VoxelChunk[] chunks = Generate(generator, region);
            double millis = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;

            if (millis < bestMillis)
            {
                bestMillis = millis;
                best = chunks;
            }
        }

        long totalBytes = 0;
        int uniform = 0;
        foreach (VoxelChunk chunk in best)
        {
            totalBytes += chunk.EstimatedBytes;
            if (chunk.IsUniform)
            {
                uniform++;
            }
        }

        return new Measurement(best, bestMillis / region.Length, totalBytes, uniform);
    }

    private static VoxelChunk[] Generate(WorldGenerator generator, ChunkPos[] region)
    {
        var chunks = new VoxelChunk[region.Length];
        for (int i = 0; i < region.Length; i++)
        {
            chunks[i] = generator.GenerateTerrain(region[i]);
        }

        return chunks;
    }

    private static ChunkPos[] Region()
    {
        var region = new List<ChunkPos>();
        for (int cz = 0; cz < RegionZ; cz++)
        {
            for (int cx = 0; cx < RegionX; cx++)
            {
                for (int cy = RegionMinChunkY; cy <= RegionMaxChunkY; cy++)
                {
                    region.Add(new ChunkPos(cx, cy, cz));
                }
            }
        }

        return [.. region];
    }

    private static long CountSolid(VoxelChunk[] chunks)
    {
        long solid = 0;
        foreach (VoxelChunk chunk in chunks)
        {
            if (chunk.IsUniform)
            {
                if (chunk.UniformValue != MaterialIds.AirNumericId)
                {
                    solid += VoxelConstants.ChunkVolume;
                }

                continue;
            }

            for (int i = 0; i < VoxelConstants.ChunkVolume; i++)
            {
                if (chunk.GetAt(i) != MaterialIds.AirNumericId)
                {
                    solid++;
                }
            }
        }

        return solid;
    }

    /// <summary>
    /// Splits the disagreement between a reference world and a coarser one by direction.
    /// </summary>
    /// <remarks>
    /// Two numbers rather than one, because the direction is the interesting half. Linear
    /// interpolation between two samples can never exceed the larger of them, so a peak that
    /// sits inside a lattice cell gets clipped — and the cave threshold selects exactly those
    /// peaks. The prediction is that coarsening only ever <i>loses</i> caves and never invents
    /// one; a nonzero <c>Gained</c> would say that reasoning is wrong.
    /// </remarks>
    private static (long Lost, long Gained, long Other) CountDiffering(
        VoxelChunk[] expected, VoxelChunk[] actual)
    {
        long lost = 0;
        long gained = 0;
        long other = 0;

        for (int c = 0; c < expected.Length; c++)
        {
            VoxelChunk a = expected[c];
            VoxelChunk b = actual[c];

            // Two thirds of the region is uniform sky or uniform rock, so settling those in
            // one comparison rather than 32,768 is what keeps the diff pass off the clock.
            if (a.IsUniform && b.IsUniform && a.UniformValue == b.UniformValue)
            {
                continue;
            }

            for (int i = 0; i < VoxelConstants.ChunkVolume; i++)
            {
                int reference = a.GetAt(i);
                int coarse = b.GetAt(i);
                if (reference == coarse)
                {
                    continue;
                }

                if (reference == MaterialIds.AirNumericId)
                {
                    lost++;
                }
                else if (coarse == MaterialIds.AirNumericId)
                {
                    gained++;
                }
                else
                {
                    other++;
                }
            }
        }

        return (lost, gained, other);
    }

    private static string FormatBytes(long bytes) =>
        bytes >= 1024 * 1024
            ? $"{bytes / (1024.0 * 1024.0):F1} MiB"
            : $"{bytes / 1024.0:F1} KiB";

    private static string Next(string[] args, ref int index) =>
        ++index < args.Length ? args[index] : throw new ArgumentException($"'{args[index - 1]}' needs a value");


    // ---------------------------------------------------------------- the light bench
    //
    // WHY THIS EXISTS, AND WHY IT LOADS THE REAL MODS. M5c-1 costed light derivation in Release
    // and M5d then measured it inside the game, which builds Debug — and the two numbers differ
    // by nearly 6x, which is exactly the trap M2b fell into when it published a Debug
    // "6.55 ms/chunk" with no build beside it. A game-side figure alone cannot say whether light
    // is expensive or whether Debug is. This is the same instrument M2d-2 used to settle caves,
    // pointed at the other half of the pipeline.
    //
    // Opacity and emission come from mods/minercraft/ through the real loader rather than from a
    // fixture, because conductance is a function of which materials are opaque and a made-up
    // table would measure a made-up world.

    /// <summary>Times light derivation and propagation over the same fixed region.</summary>
    private static int LightBench(ulong seed, int repeat)
    {
        var content = new GameContent();
        LoadResult loaded = content.Load(new DirectoryContentSource(ModsDirectory()));
        if (!loaded.Success)
        {
            Console.Error.WriteLine($"content: {string.Join("; ", loaded.Errors)}");
            return 1;
        }

        MaterialTable materials = MaterialTable.From(content.Materials);

        // The shipped strata, by identifier, so the bench lights the material the game lights.
        var strata = new StrataTable(
            [
                new StrataLayer(content.Materials.GetNumericId(Identifier.Parse("minercraft:grass")), 1),
                new StrataLayer(content.Materials.GetNumericId(Identifier.Parse("minercraft:dirt")), 3),
            ],
            content.Materials.GetNumericId(Identifier.Parse("minercraft:stone")));

        var generator = new WorldGenerator(new WorldGenSettings { Seed = seed }, strata);
        ChunkPos[] region = Region();

        Console.WriteLine(
            $"Light bench — {region.Length} chunks, seed {seed}, best of {repeat}, "
            + $"{content.Materials.Count} material(s)");
        if (IsDebug())
        {
            Console.WriteLine("*** DEBUG BUILD — these timings are meaningless. Re-run with -c Release. ***");
        }

        Console.WriteLine();

        var world = new BenchLightWorld();
        foreach (ChunkPos position in region)
        {
            world.Voxels[position] = generator.GenerateTerrain(position);
        }

        // One sky column per (cx, cz), seeded analytically off the generator — the same seeding
        // the streamer does, and the reason there is no top of the world to flood down from.
        foreach (ChunkPos position in region)
        {
            var key = (position.X, position.Z);
            if (world.Sky.ContainsKey(key)) continue;

            var column = new SkyColumn(position.X, position.Z);
            column.Seed(generator);
            world.Sky[key] = column;
        }

        double bestDerive = double.MaxValue;
        double bestCopy = double.MaxValue;
        double bestDrain = double.MaxValue;
        long cells = 0;
        int uniform = 0;

        // Warm the JIT first, for the reason Measure gives: tiered compilation makes the first
        // pass a measurement of the interpreter.
        for (int pass = -1; pass < repeat; pass++)
        {
            world.Light.Clear();
            var propagator = new LightPropagator(world);

            var padded = new PaddedChunk();
            var neighbourhood = new VoxelChunk?[PaddedChunk.NeighbourhoodSize];
            var changed = new List<LightCellPos>();

            double copyMillis = 0.0;
            int uniformThisPass = 0;

            long deriveStart = Stopwatch.GetTimestamp();

            foreach (ChunkPos position in region)
            {
                var light = new ChunkLight();
                world.Light[position] = light;
                SkyColumn sky = world.Sky[(position.X, position.Z)];

                changed.Clear();

                if (world.TryUniformNeighbourhood(position, out int material))
                {
                    light.BuildDerivedUniform(material, materials, position, sky, changed);
                    uniformThisPass++;
                    continue;
                }

                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    neighbourhood[PaddedChunk.NeighbourIndex(dx, dy, dz)] =
                        world.Voxels.GetValueOrDefault(position.Offset(dx, dy, dz));
                }

                // Timed inside the derive rather than beside it, because the question M5c-1 left
                // open is what share of a derive the copy is — it named 60-75% and did not
                // publish the measurement that would let anyone check it later.
                long copyStart = Stopwatch.GetTimestamp();
                padded.CopyFrom(neighbourhood, MaterialIds.AirNumericId);
                copyMillis += (Stopwatch.GetTimestamp() - copyStart) * 1000.0 / Stopwatch.Frequency;

                light.BuildDerived(padded, materials, position, sky, changed);
            }

            double deriveMillis =
                (Stopwatch.GetTimestamp() - deriveStart) * 1000.0 / Stopwatch.Frequency;

            foreach (ChunkPos position in region)
            {
                propagator.SeedSources(position);
            }

            long drainStart = Stopwatch.GetTimestamp();
            long visited = propagator.Drain();
            double drainMillis =
                (Stopwatch.GetTimestamp() - drainStart) * 1000.0 / Stopwatch.Frequency;

            if (pass < 0) continue;   // the warm-up pass measures the JIT

            if (deriveMillis < bestDerive)
            {
                bestDerive = deriveMillis;
                bestCopy = copyMillis;
                uniform = uniformThisPass;
            }

            if (drainMillis < bestDrain)
            {
                bestDrain = drainMillis;
            }

            cells = propagator.CellsVisited;
        }

        double chunks = region.Length;

        Console.WriteLine($"derive    {bestDerive:F0} ms total, {bestDerive / chunks:F3} ms/chunk");
        Console.WriteLine(
            $"  copy    {bestCopy:F0} ms of that ({100.0 * bestCopy / bestDerive:F0}% of the derive), "
            + $"{bestCopy / chunks:F3} ms/chunk");
        Console.WriteLine(
            $"  uniform {uniform} / {region.Length} chunks took the closed form "
            + $"({100.0 * uniform / chunks:F0}%), skipping the copy entirely");
        Console.WriteLine(
            $"drain     {bestDrain:F0} ms total over {cells} cell visit(s), "
            + $"{(cells > 0 ? bestDrain * 1e6 / cells : 0.0):F0} ns/cell");
        Console.WriteLine(
            $"together  {(bestDerive + bestDrain) / chunks:F3} ms/chunk on one thread");
        Console.WriteLine();
        Console.WriteLine("derive  — conductance, emission and sky flags, the sim-thread half M5-PLAN did not cost");
        Console.WriteLine("copy    — PaddedChunk.CopyFrom, the lever M5c-1 named and deliberately did not build");
        Console.WriteLine("drain   — the BFS, which M5-PLAN costed at ~20 ns/cell");

        return 0;
    }

    /// <summary>Where the shipped mods live, relative to this repository.</summary>
    private static string ModsDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "MinerCraft.Game", "mods");
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("could not find MinerCraft.Game/mods");
    }

    /// <summary>
    /// The chunks the light bench owns, in the shape <see cref="LightPropagator"/> wants.
    /// </summary>
    /// <remarks>
    /// Deliberately not the streamer. The streamer is a thread pool, a refcount and a job queue,
    /// and a bench that dragged them in would be measuring them — the same argument that keeps
    /// worldgen's bench out of the game.
    /// </remarks>
    private sealed class BenchLightWorld : ILightChunks
    {
        public Dictionary<ChunkPos, VoxelChunk> Voxels { get; } = [];

        public Dictionary<ChunkPos, ChunkLight> Light { get; } = [];

        public Dictionary<(int X, int Z), SkyColumn> Sky { get; } = [];

        public ChunkLight? LightAt(ChunkPos chunk) => Light.GetValueOrDefault(chunk);

        /// <summary>
        /// The chunk and its six face neighbours all one material — light's dependency set, which
        /// is the faces and not the mesher's twenty-six.
        /// </summary>
        public bool TryUniformNeighbourhood(ChunkPos position, out int material)
        {
            material = 0;

            if (!Voxels.TryGetValue(position, out VoxelChunk? self) || !self.IsUniform)
            {
                return false;
            }

            material = self.UniformValue;

            foreach ((int dx, int dy, int dz) in Faces)
            {
                if (!Voxels.TryGetValue(position.Offset(dx, dy, dz), out VoxelChunk? neighbour)
                    || !neighbour.IsUniform
                    || neighbour.UniformValue != material)
                {
                    return false;
                }
            }

            return true;
        }

        private static readonly (int X, int Y, int Z)[] Faces =
            [(-1, 0, 0), (1, 0, 0), (0, -1, 0), (0, 1, 0), (0, 0, -1), (0, 0, 1)];
    }

    private static bool IsDebug()
    {
#if DEBUG
        return true;
#else
        return false;
#endif
    }

    private sealed record Measurement(
        VoxelChunk[] Chunks, double MillisPerChunk, long TotalBytes, int UniformChunks);

    private static string Percent(double? value) =>
        value?.ToString("P3", CultureInfo.InvariantCulture) ?? "—";

    private sealed record Row(
        string Label, Measurement Run, double Carved, double? Lost, double? Gained);
}
