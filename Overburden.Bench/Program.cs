using System.Diagnostics;
using System.Globalization;
using Overburden.Core.Content;
using Overburden.Core.Hydrology;
using Overburden.Core.Lighting;
using Overburden.Core.Persistence;
using Overburden.Persistence;
using Overburden.Core.Voxels;
using Overburden.Core.Worldgen;

namespace Overburden.Bench;

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
        Usage: dotnet run --project Overburden.Bench -c Release -- [options]

          --steps 1,2,4,8,16   cave lattice steps to measure (default 1,2,4,8,16)
          --seed  <ulong>      world seed (default: the game's own)
          --repeat <n>         timed passes per configuration, best wins (default 3)
          --light              time light derivation and propagation instead of worldgen
          --blob               time chunk serialisation, and price Compact() on save
          --store              time the SQLite read path against the generation it replaces
          --watershed          time the M7 watershed analysis over a sweep of window sizes
          --portal             time M8b-2b's portal index over a chunk bite, array seed against analytic
          --fluid              time M8's Tier B automaton draining a lake into a mine
          --fluidlitres <n>    the lake, in litres (default 9,000,000)
          --fluidticks  <n>    ticks of flow before the settle (default 2000)
          --fluidbores  1,3,5  breach widths to sweep, in voxels (default 1,3,5,9)
          --fluidspan   <m>    the mine's footprint, in metres (default 96)
          --fluiddepth  <m>    how far under the surface it sits (default 24 for a mine, 2 for a chamber)
          --fluidshape  <s>    mine (0.75 m bores) or chamber (bitten 8 m chunks; default mine)
          --fluidchambers <n>  chambers per axis for --fluidshape=chamber (default 1)
          --fluidsettle <n>    tick budget for the settle after the flow (default 100000)
          --fluidbudget <ms>   slice each flow tick under a per-frame budget (default 0: whole ticks, the control)
          --fluidsolve  <arm>  wait (the automaton alone, default), solve (M8b-3b's sheet solve at every settle boundary), or both
          --settleshare <n>    the solve re-levels a sheet with one cell in n unsettled (default 8; 0 is no threshold, the pre-slice arm); --fluid's solve arm, --spill's settle arm and --lake
          --lake               the share slice: a lake at rest with a draining edge, the settle priced at no threshold against --settleshare
          --lakechambers <n>   chambers per axis for the lake's floor (default 12: a 96 m floor, 147,456 cells)
          --laketicks <n>      ticks to run the lake for (default 60)
          --spill              M8b-3: a spill on real ground with and without the B -> A hand-off
          --spilllitres a,b    the spills to sweep, in litres (default 10000,50000)
          --spillticks <n>     tick cap per arm (default 20000)
        """;

    private static int Main(string[] args)
    {
        ulong seed = 0x4D494E455243UL;
        int[] steps = [1, 2, 4, 8, 16];
        int repeat = 3;
        bool light = false;
        bool blob = false;
        bool store = false;
        bool watershed = false;
        bool fluid = false;
        bool portal = false;
        long fluidLitres = 9_000_000;
        int fluidTicks = 2_000;
        int[] fluidBores = [1, 3, 5, 9];
        int fluidSpan = 96;
        int? fluidDepth = null;
        int fluidSettle = 100_000;
        string fluidShape = "mine";
        int fluidChambers = 1;
        double fluidBudget = 0;
        string fluidSolve = "wait";
        int settleShare = Overburden.Core.Fluid.SheetSettle.DefaultSettleShare;
        bool lake = false;
        int lakeChambers = 12;
        int lakeTicks = 60;
        bool spill = false;
        long[] spillLitres = [10_000, 50_000];
        int spillTicks = 20_000;

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
                    case "--blob":
                        blob = true;
                        break;
                    case "--store":
                        store = true;
                        break;
                    case "--watershed":
                        watershed = true;
                        break;
                    case "--fluid":
                        fluid = true;
                        break;
                    case "--portal":
                        portal = true;
                        break;
                    case "--fluidlitres":
                        fluidLitres = long.Parse(Next(args, ref i), CultureInfo.InvariantCulture);
                        break;
                    case "--fluidticks":
                        fluidTicks = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture);
                        break;
                    case "--fluidbores":
                        fluidBores = Next(args, ref i)
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Select(part => int.Parse(part, CultureInfo.InvariantCulture))
                            .ToArray();
                        break;
                    case "--fluidspan":
                        fluidSpan = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture);
                        break;
                    case "--fluiddepth":
                        fluidDepth = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture);
                        break;
                    case "--fluidsettle":
                        fluidSettle = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture);
                        break;
                    case "--fluidshape":
                        fluidShape = Next(args, ref i);
                        break;
                    case "--fluidchambers":
                        fluidChambers = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture);
                        break;
                    case "--fluidbudget":
                        fluidBudget = double.Parse(Next(args, ref i), CultureInfo.InvariantCulture);
                        break;
                    case "--fluidsolve":
                        fluidSolve = Next(args, ref i);
                        break;
                    case "--settleshare":
                        settleShare = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture);
                        break;
                    case "--lake":
                        lake = true;
                        break;
                    case "--lakechambers":
                        lakeChambers = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture);
                        break;
                    case "--laketicks":
                        lakeTicks = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture);
                        break;
                    case "--spill":
                        spill = true;
                        break;
                    case "--spilllitres":
                        spillLitres = Next(args, ref i)
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Select(part => long.Parse(part, CultureInfo.InvariantCulture))
                            .ToArray();
                        break;
                    case "--spillticks":
                        spillTicks = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture);
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

        if (lake)
        {
            var lakeGenerator = new WorldGenerator(new WorldGenSettings { Seed = seed }, strata);
            return FluidBench.RunLake(
                lakeGenerator, MachineControl(lakeGenerator), IsDebug(), lakeChambers, lakeTicks, settleShare);
        }

        if (fluid)
        {
            var fluidGenerator = new WorldGenerator(new WorldGenSettings { Seed = seed }, strata);
            return FluidBench.Run(
                fluidGenerator,
                MachineControl(fluidGenerator),
                IsDebug(),
                fluidLitres,
                fluidTicks,
                fluidBores,
                fluidSpan,
                fluidDepth,
                fluidSettle,
                fluidShape,
                fluidChambers,
                fluidBudget,
                fluidSolve,
                settleShare);
        }

        if (portal)
        {
            var portalGenerator = new WorldGenerator(new WorldGenSettings { Seed = seed }, strata);
            return PortalBench.Run(portalGenerator, MachineControl(portalGenerator), IsDebug());
        }

        if (spill)
        {
            var spillGenerator = new WorldGenerator(new WorldGenSettings { Seed = seed }, strata);
            return SpillBench.Run(
                spillGenerator, MachineControl(spillGenerator), IsDebug(), seed, spillLitres, spillTicks,
                settleShare);
        }

        if (watershed)
        {
            return WatershedBench(seed, repeat);
        }

        if (light)
        {
            return LightBench(seed, repeat);
        }

        if (blob)
        {
            return BlobBench(seed, repeat);
        }

        if (store)
        {
            return StoreBench(seed, repeat);
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
    // Opacity and emission come from mods/overburden/ through the real loader rather than from a
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
                new StrataLayer(content.Materials.GetNumericId(Identifier.Parse("overburden:grass")), 1),
                new StrataLayer(content.Materials.GetNumericId(Identifier.Parse("overburden:dirt")), 3),
            ],
            content.Materials.GetNumericId(Identifier.Parse("overburden:stone")));

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
            string candidate = Path.Combine(directory.FullName, "Overburden.Game", "mods");
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("could not find Overburden.Game/mods");
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

    // ---------------------------------------------------------------------------------------
    // M6a's one measurement, and it exists to settle a comment rather than to tune anything.
    //
    // VoxelChunk.Compact has said "this runs on serialisation" since M1 and has never had a
    // caller. That is a prediction, not a decision, so M6-PLAN put it on the list: measure it,
    // then keep it or correct the comment. What it costs is a full 32,768-voxel scan and a
    // repack per save; what it buys is dropped palette entries and a narrower packing width,
    // on a payload that is about to be compressed anyway.
    // ---------------------------------------------------------------------------------------

    /// <summary>Times chunk serialisation over the same fixed region, with and without Compact.</summary>
    private static int BlobBench(ulong seed, int repeat)
    {
        var content = new GameContent();
        LoadResult loaded = content.Load(new DirectoryContentSource(ModsDirectory()));
        if (!loaded.Success)
        {
            Console.Error.WriteLine($"content: {string.Join("; ", loaded.Errors)}");
            return 1;
        }

        // The real table, through the real loader, for the reason the light bench gives: a
        // fixture would measure a made-up world, and here the material *count* is what decides
        // whether a chunk is palettised at all.
        MaterialTable materials = MaterialTable.From(content.Materials);

        var strata = new StrataTable(
            [
                new StrataLayer(content.Materials.GetNumericId(Identifier.Parse("overburden:grass")), 1),
                new StrataLayer(content.Materials.GetNumericId(Identifier.Parse("overburden:dirt")), 3),
            ],
            content.Materials.GetNumericId(Identifier.Parse("overburden:stone")));

        var generator = new WorldGenerator(new WorldGenSettings { Seed = seed }, strata);
        ChunkPos[] region = Region();

        Console.WriteLine(
            $"Blob bench — {region.Length} chunks, seed {seed}, best of {repeat}, "
            + $"{content.Materials.Count} material(s)");

        if (IsDebug())
        {
            Console.WriteLine("*** DEBUG BUILD — these timings are meaningless. Re-run with -c Release. ***");
        }

        Console.WriteLine();

        // Generated once and reused. Every arm below clones before writing, because Compact
        // mutates and an arm that ran second would otherwise measure an already-compacted chunk.
        var chunks = new VoxelChunk[region.Length];
        for (int i = 0; i < region.Length; i++)
        {
            chunks[i] = generator.GenerateTerrain(region[i]);
        }

        // A pass of edits, because an untouched generated chunk is not what gets saved — only an
        // edited one is. A carve leaves exactly the dead palette entries Compact exists to drop,
        // so measuring pristine chunks would price Compact's best case and call it typical.
        var edited = new VoxelChunk[region.Length];
        var random = new Random(20260831);
        int airId = MaterialIds.AirNumericId;
        for (int i = 0; i < region.Length; i++)
        {
            VoxelChunk chunk = chunks[i].Clone();

            // A rough sphere, the shape a swing actually leaves.
            int cx = random.Next(8, 24), cy = random.Next(8, 24), cz = random.Next(8, 24);
            for (int z = cz - 4; z <= cz + 4; z++)
            for (int x = cx - 4; x <= cx + 4; x++)
            for (int y = cy - 4; y <= cy + 4; y++)
            {
                int dx = x - cx, dy = y - cy, dz = z - cz;
                if ((dx * dx) + (dy * dy) + (dz * dz) <= 16) chunk.Set(x, y, z, airId);
            }

            edited[i] = chunk;
        }

        // Compact's actual best case, and the carve above is *not* it — carving air into a chunk
        // that already has air leaves the palette exactly as it was. A dead entry appears only
        // when the last voxel of some material leaves the chunk, so this arm strips one outright.
        // Without it the bench prices Compact against a fixture that could not benefit, which is
        // a fixture degenerate in a way the numbers themselves cannot show.
        var stripped = new VoxelChunk[region.Length];
        int strippedChunks = 0;
        for (int i = 0; i < region.Length; i++)
        {
            VoxelChunk chunk = chunks[i].Clone();

            int victim = -1;
            for (int v = 0; v < VoxelConstants.ChunkVolume && victim < 0; v++)
            {
                if (chunk.GetAt(v) != airId) victim = chunk.GetAt(v);
            }

            if (victim >= 0 && !chunk.IsUniform)
            {
                for (int v = 0; v < VoxelConstants.ChunkVolume; v++)
                {
                    if (chunk.GetAt(v) == victim) chunk.SetAt(v, airId);
                }

                strippedChunks++;
            }

            stripped[i] = chunk;
        }

        var arms = new List<BlobRow>
        {
            MeasureBlobs("generated, compact", chunks, region, materials, repeat, compact: true),
            MeasureBlobs("generated, verbatim", chunks, region, materials, repeat, compact: false),
            MeasureBlobs("carved, compact", edited, region, materials, repeat, compact: true),
            MeasureBlobs("carved, verbatim", edited, region, materials, repeat, compact: false),
            MeasureBlobs("stripped, compact", stripped, region, materials, repeat, compact: true),
            MeasureBlobs("stripped, verbatim", stripped, region, materials, repeat, compact: false),
        };

        Console.WriteLine(
            $"{"arm",-22}{"write µs",10}{"read µs",10}{"raw B",10}{"stored B",10}"
            + $"{"ratio",8}{"uniform",9}{"B/varied",10}");
        Console.WriteLine(new string('-', 89));

        foreach (BlobRow row in arms)
        {
            Console.WriteLine(
                $"{row.Label,-22}"
                + $"{row.WriteMicros,10:F1}"
                + $"{row.ReadMicros,10:F1}"
                + $"{row.RawBytes,10:F0}"
                + $"{row.StoredBytes,10:F0}"
                + $"{row.RawBytes / Math.Max(1.0, row.StoredBytes),8:F1}"
                + $"{row.Uniform,9}"
                + $"{row.VariedBytes,10:F0}");
        }

        Console.WriteLine();
        Console.WriteLine($"{strippedChunks} of {region.Length} chunks had a material stripped out.");
        Console.WriteLine();

        Report("carved", arms[2], arms[3]);
        Report("stripped", arms[4], arms[5]);
        Console.WriteLine();

        // The number a save file is actually sized by: only edited chunks are stored, and an
        // edited chunk is usually not uniform.
        double perVaried = arms[2].VariedBytes;
        Console.WriteLine(
            $"At {perVaried:F0} B a varied chunk: 1,000 stored chunks is {perVaried * 1000 / 1024:F0} KiB, "
            + $"100,000 is {perVaried * 100000 / (1024 * 1024):F1} MiB.");

        return 0;

        static void Report(string what, BlobRow compacted, BlobRow verbatim)
        {
            double cost = compacted.WriteMicros - verbatim.WriteMicros;
            double saving = verbatim.StoredBytes - compacted.StoredBytes;

            Console.WriteLine(
                $"Compact on a {what} chunk costs {cost:F1} µs "
                + $"({cost / Math.Max(0.001, verbatim.WriteMicros):P0} of the write) "
                + $"and saves {saving:F1} stored bytes "
                + $"({saving / Math.Max(1.0, verbatim.StoredBytes):P1}).");
        }
    }

    private static BlobRow MeasureBlobs(
        string label,
        VoxelChunk[] source,
        ChunkPos[] region,
        MaterialTable materials,
        int repeat,
        bool compact)
    {
        double bestWrite = double.MaxValue;
        double bestRead = double.MaxValue;
        long stored = 0;
        long raw = 0;
        int uniform = 0;
        double varied = 0.0;

        // Pass -1 warms the JIT, for the reason the rest of this file gives: tiered compilation
        // makes the first pass a measurement of the interpreter.
        for (int pass = -1; pass < repeat; pass++)
        {
            var copies = new VoxelChunk[source.Length];
            for (int i = 0; i < source.Length; i++) copies[i] = source[i].Clone();

            var blobs = new byte[source.Length][];

            long writeStart = Stopwatch.GetTimestamp();
            for (int i = 0; i < copies.Length; i++)
            {
                blobs[i] = ChunkBlob.Write(region[i], copies[i], compact);
            }

            long writeEnd = Stopwatch.GetTimestamp();

            for (int i = 0; i < blobs.Length; i++)
            {
                ChunkBlob.Read(blobs[i], region[i], materials);
            }

            long readEnd = Stopwatch.GetTimestamp();

            if (pass < 0) continue;

            double writeMicros = Micros(writeStart, writeEnd) / source.Length;
            double readMicros = Micros(writeEnd, readEnd) / source.Length;

            if (writeMicros < bestWrite)
            {
                bestWrite = writeMicros;
                stored = blobs.Sum(b => (long)b.Length);
                raw = copies.Sum(c => (long)PayloadSize(c));

                // Counted from the *written* chunk, so compaction demoting one to uniform shows
                // up here rather than being invisible.
                uniform = copies.Count(c => c.IsUniform);

                long variedBytes = 0;
                int variedCount = 0;
                for (int i = 0; i < copies.Length; i++)
                {
                    if (copies[i].IsUniform) continue;
                    variedBytes += blobs[i].Length;
                    variedCount++;
                }

                varied = variedCount == 0 ? 0.0 : (double)variedBytes / variedCount;
            }

            bestRead = Math.Min(bestRead, readMicros);
        }

        return new BlobRow(
            label,
            bestWrite,
            bestRead,
            (double)raw / source.Length,
            (double)stored / source.Length,
            uniform,
            varied);
    }

    /// <summary>What the payload would be before compression. Mirrors ChunkBlob's layout.</summary>
    private static int PayloadSize(VoxelChunk chunk) => chunk.Kind switch
    {
        VoxelChunk.StorageKind.Uniform => 5,
        VoxelChunk.StorageKind.Palettised =>
            4 + (chunk.PaletteSize * 4) + (VoxelConstants.ChunkVolume / (64 / BitsOf(chunk)) * 8),
        _ => 1 + (VoxelConstants.ChunkVolume * 2),
    };

    private static int BitsOf(VoxelChunk chunk) => chunk.PaletteSize switch
    {
        <= 2 => 1,
        <= 4 => 2,
        <= 16 => 4,
        _ => 8,
    };

    private static double Micros(long from, long to) =>
        (to - from) * 1_000_000.0 / Stopwatch.Frequency;

    private sealed record BlobRow(
        string Label,
        double WriteMicros,
        double ReadMicros,
        double RawBytes,
        double StoredBytes,
        int Uniform,
        double VariedBytes);

    // ---------------------------------------------------------------------------------------
    // The half of M6's headline worry that M6-PLAN could only guess at.
    //
    // The plan's crossover table says a heavily-dug world could be *slower* to enter than one
    // generated from scratch, because loads run one at a time on the serial lane while
    // generation runs on eight workers. It assumed 0.1-0.3 ms a load. M6a measured the
    // deserialise half at 21 us; this measures the other half, which is SQLite's.
    //
    // Reference: generation fills r4h/2v in ~89 ms of wall clock (1,521 chunks x 0.47 ms
    // measured, across eight workers).
    // ---------------------------------------------------------------------------------------

    private const int FillChunks = 1521;

    /// <summary>Times the store's read path against the generation it would replace.</summary>
    private static int StoreBench(ulong seed, int repeat)
    {
        var content = new GameContent();
        LoadResult loaded = content.Load(new DirectoryContentSource(ModsDirectory()));
        if (!loaded.Success)
        {
            Console.Error.WriteLine($"content: {string.Join("; ", loaded.Errors)}");
            return 1;
        }

        MaterialTable materials = MaterialTable.From(content.Materials);

        var strata = new StrataTable(
            [
                new StrataLayer(content.Materials.GetNumericId(Identifier.Parse("overburden:grass")), 1),
                new StrataLayer(content.Materials.GetNumericId(Identifier.Parse("overburden:dirt")), 3),
            ],
            content.Materials.GetNumericId(Identifier.Parse("overburden:stone")));

        var generator = new WorldGenerator(new WorldGenSettings { Seed = seed }, strata);

        string directory = Path.Combine(Path.GetTempPath(), "overburden-bench", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "world.db");

        Console.WriteLine($"Store bench — {FillChunks} chunks, seed {seed}, best of {repeat}");
        if (IsDebug())
        {
            Console.WriteLine("*** DEBUG BUILD — these timings are meaningless. Re-run with -c Release. ***");
        }

        Console.WriteLine($"world: {path}");
        Console.WriteLine();

        try
        {
            // A dug-in world: every chunk edited, which is the case the crossover is about. An
            // unedited chunk is never stored, so filling with pristine terrain would measure a
            // world that cannot exist.
            var positions = new ChunkPos[FillChunks];
            var blobs = new (ChunkPos, byte[])[FillChunks];
            var random = new Random(20260831);

            for (int i = 0; i < FillChunks; i++)
            {
                var position = new ChunkPos((i % 39) - 19, ((i / 39) % 9) - 4, (i / 351) - 2);
                positions[i] = position;

                VoxelChunk chunk = generator.GenerateTerrain(position);
                int cx = random.Next(8, 24), cy = random.Next(8, 24), cz = random.Next(8, 24);
                for (int z = cz - 4; z <= cz + 4; z++)
                for (int x = cx - 4; x <= cx + 4; x++)
                for (int y = cy - 4; y <= cy + 4; y++)
                {
                    int dx = x - cx, dy = y - cy, dz = z - cz;
                    if ((dx * dx) + (dy * dy) + (dz * dz) <= 16)
                    {
                        chunk.Set(x, y, z, MaterialIds.AirNumericId);
                    }
                }

                blobs[i] = (position, ChunkBlob.Write(position, chunk));
            }

            long bytes;
            double writeMillis;

            using (var store = new SqliteWorldStore(path))
            {
                long start = Stopwatch.GetTimestamp();
                store.Write(blobs, []);
                store.Flush();
                writeMillis = Micros(start, Stopwatch.GetTimestamp()) / 1000.0;
            }

            bytes = new FileInfo(path).Length;

            double bestOpen = double.MaxValue;
            double bestIndex = double.MaxValue;
            double bestRead = double.MaxValue;
            double bestReadAndDecode = double.MaxValue;

            for (int pass = -1; pass < repeat; pass++)
            {
                long openStart = Stopwatch.GetTimestamp();
                using var store = new SqliteWorldStore(path);
                long opened = Stopwatch.GetTimestamp();

                IReadOnlyCollection<ChunkPos> index = store.LoadChunkIndex();
                var set = new HashSet<ChunkPos>(index);
                long indexed = Stopwatch.GetTimestamp();

                long total = 0;
                foreach (ChunkPos position in positions)
                {
                    total += store.ReadChunk(position)!.Length;
                }

                long read = Stopwatch.GetTimestamp();

                foreach (ChunkPos position in positions)
                {
                    ChunkBlob.Read(store.ReadChunk(position)!, position, materials);
                }

                long decoded = Stopwatch.GetTimestamp();

                if (pass < 0) continue;
                if (total == 0 || set.Count != FillChunks) throw new InvalidOperationException("bad fill");

                bestOpen = Math.Min(bestOpen, Micros(openStart, opened) / 1000.0);
                bestIndex = Math.Min(bestIndex, Micros(opened, indexed) / 1000.0);
                bestRead = Math.Min(bestRead, Micros(indexed, read) / FillChunks);
                bestReadAndDecode = Math.Min(bestReadAndDecode, Micros(read, decoded) / FillChunks);
            }

            Console.WriteLine($"file on disk              {bytes / 1024.0,10:F0} KiB  ({(double)bytes / FillChunks,0:F0} B a chunk)");
            Console.WriteLine($"write {FillChunks} chunks       {writeMillis,10:F1} ms");
            Console.WriteLine();
            Console.WriteLine($"open the world            {bestOpen,10:F2} ms");
            Console.WriteLine($"load the chunk index      {bestIndex,10:F2} ms   ({FillChunks} keys)");
            Console.WriteLine($"read one blob             {bestRead,10:F1} µs");
            Console.WriteLine($"read + deserialise one    {bestReadAndDecode,10:F1} µs");
            Console.WriteLine();

            // The comparison the plan asked for, in the plan's own terms.
            const double GenerateWallMillis = 89.0;
            double serialMillis = bestReadAndDecode * FillChunks / 1000.0;

            Console.WriteLine(
                $"Loading all {FillChunks} on one serial lane: {serialMillis:F0} ms, "
                + $"against ~{GenerateWallMillis:F0} ms to generate the same box on eight workers.");

            double crossover = GenerateWallMillis * 1000.0 / bestReadAndDecode;
            Console.WriteLine(
                crossover >= FillChunks
                    ? $"Crossover is {crossover:F0} chunks, past the whole {FillChunks}-chunk fill — "
                      + "loading never loses to generating at this radius."
                    : $"Crossover is {crossover:F0} of {FillChunks} chunks — past that, loading is "
                      + "the slower path.");

            return 0;
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                Console.Error.WriteLine($"could not remove {directory}");
            }
        }
    }

    // ------------------------------------------------------------ the watershed bench
    //
    // WHY THIS EXISTS. M7-PLAN names one number as the one most likely to have been got wrong:
    // how long a window analysis takes, and therefore whether an incremental watershed ever has
    // to be built. The estimate written into the plan was ~10-30 ms for a 1 km window at 4 m,
    // from 65,536 samples at an assumed 0.1-0.2 us each plus the flood. Under ~50 ms the question
    // is settled forever and the window is simply re-analysed wholesale when the player moves;
    // over ~200 ms and M7b's dependency on it needs rethinking before it is written.
    //
    // Sampling and analysis are timed apart, because the estimate assumed sampling dominates and
    // an estimate that is right in total for the wrong reason is the kind that misleads later.

    /// <summary>
    /// How fast this machine is today, against a figure the project already published.
    /// </summary>
    /// <remarks>
    /// <b>Print the denominator.</b> M7a-1's watershed figures were taken on a machine running
    /// 2.2x faster than the same machine an hour later — confirmed by running the worldgen bench,
    /// whose Release figure has been recorded since M2d-2, and finding it equally slower. Without
    /// a control in the same output, a reader cannot tell a regression from a warm afternoon, and
    /// two numbers taken on different days cannot be compared at all.
    /// </remarks>
    private static string MachineControl(WorldGenerator generator)
    {
        const double recordedMillisPerChunk = 0.47;
        ChunkPos[] control = Region().Take(256).ToArray();

        double best = double.MaxValue;
        for (int pass = -1; pass < 3; pass++)
        {
            var clock = Stopwatch.StartNew();
            foreach (ChunkPos position in control)
            {
                generator.GenerateTerrain(position);
            }

            if (pass >= 0)
            {
                best = Math.Min(best, clock.Elapsed.TotalMilliseconds / control.Length);
            }
        }

        double ratio = best / recordedMillisPerChunk;
        string verdict = ratio >= 1
            ? $"this run is {ratio:F2}x SLOWER than that reference"
            : $"this run is {1 / ratio:F2}x faster than that reference";
        return $"  machine control: worldgen {best:F2} ms/chunk against {recordedMillisPerChunk:F2} "
            + $"recorded at M2d-2 — {verdict}."
            + Environment.NewLine
            + "  Divide the times below by that factor to compare them with any figure in the "
            + "worklog.";
    }

    /// <summary>Times <see cref="HeightField.Sample"/> and <see cref="Watershed.Analyse"/>.</summary>
    private static int WatershedBench(ulong seed, int repeat)
    {
        var strata = new StrataTable([new StrataLayer(Grass, 1), new StrataLayer(Dirt, 3)], Stone);
        var generator = new WorldGenerator(new WorldGenSettings { Seed = seed }, strata);

        int cell = HeightField.DefaultCellVoxels;
        int[] pitches = [64, 128, 256, 512];

        Console.WriteLine(
            $"Watershed bench — seed {seed}, {cell / VoxelConstants.VoxelsPerMetre} m cells, "
            + $"best of {repeat}");
        if (IsDebug())
        {
            Console.WriteLine("*** DEBUG BUILD — these timings are meaningless. Re-run with -c Release. ***");
        }

        Console.WriteLine();
        Console.WriteLine(MachineControl(generator));
        Console.WriteLine();
        Console.WriteLine(
            "  window   samples   sample ms   flood ms   tree ms   total ms   ns/sample   "
            + "basins   nodes   curves MB   held ML   fill min");
        Console.WriteLine(
            "  ------   -------   ---------   --------   -------   --------   ---------   "
            + "------   -----   ---------   -------   --------");

        // PROMOTE THE CODE ONCE, ON A SMALL WINDOW, BEFORE ANY ROW IS TIMED — and the reason is a
        // trap this bench has now walked into twice. Tiered compilation promotes after about
        // thirty calls, so a warm-up capped by TIME silently under-warms the moment the thing
        // being measured gets slower: adding the merge tree to the warm-up loop cut it from a
        // dozen calls to four and doubled every column, including the sampling stage, which the
        // tree does not touch. Forty calls on a 64-sample window costs a tenth of a second and
        // promotes the same methods every row runs.
        for (int warm = 0; warm < 40; warm++)
        {
            MergeTree.Build(Watershed.Analyse(HeightField.Sample(generator, 0, 0, 64, cell)));
        }

        foreach (int pitch in pitches)
        {
            int samples = pitch * pitch;
            int metres = pitch * cell / VoxelConstants.VoxelsPerMetre;
            double bestSample = double.MaxValue;
            double bestFlood = double.MaxValue;
            double bestTree = double.MaxValue;
            int basins = 0;
            int nodes = 0;
            long curveCells = 0;
            long held = 0;
            double medianFillMinutes = 0;

            // Several untimed passes at THIS size, so the arrays a large window needs are already
            // on the heap when the clock starts. A PRECAUTION, NOT A DIAGNOSIS: it was added while
            // chasing a doubling that turned out to be the machine rather than the harness, and it
            // did not move the numbers. Kept because it costs nothing and removes one variable.
            int warmups = 0;
            var warmClock = Stopwatch.StartNew();
            while (warmups < 8 && warmClock.ElapsedMilliseconds < 3000)
            {
                MergeTree.Build(Watershed.Analyse(HeightField.Sample(generator, 0, 0, pitch, cell)));
                warmups++;
            }

            for (int pass = 0; pass < repeat; pass++)
            {
                var clock = Stopwatch.StartNew();
                HeightField field = HeightField.Sample(generator, 0, 0, pitch, cell);
                double sampleMillis = clock.Elapsed.TotalMilliseconds;

                clock.Restart();
                Watershed analysed = Watershed.Analyse(field);
                double floodMillis = clock.Elapsed.TotalMilliseconds;

                clock.Restart();
                MergeTree tree = MergeTree.Build(analysed);
                double treeMillis = clock.Elapsed.TotalMilliseconds;

                bestSample = Math.Min(bestSample, sampleMillis);
                bestFlood = Math.Min(bestFlood, floodMillis);
                bestTree = Math.Min(bestTree, treeMillis);

                basins = analysed.Basins.Count;
                nodes = tree.NodeCount;

                // Curve cells is the eager-composite-curve cost the plan named lazy building
                // against: every node keeps the elevations it can flood, so the total is O(n log n)
                // rather than O(n). Reported so the decision is a number rather than a worry.
                curveCells = 0;
                held = 0;
                for (int node = 0; node < tree.NodeCount; node++)
                {
                    curveCells += tree.Curve(node).CellCount;
                }

                // How long the median basin takes to fill at the heaviest weather this world
                // has, with no rainfall scaling — the number M7d has to react to. A rate of
                // nanometres per millisecond over a square metre is microlitres per millisecond,
                // so the arithmetic is capacity / (rate x catchment) in the same units throughout.
                var fillMinutes = new List<double>();
                foreach (Basin basin in analysed.Basins)
                {
                    held += basin.CapacityLitres;
                    double microlitresPerMillisecond =
                        (double)Weather.MaxRateNanometresPerMillisecond * basin.CatchmentSquareMetres;
                    fillMinutes.Add(basin.CapacityLitres * 1_000_000.0 / microlitresPerMillisecond
                        / 60_000.0);
                }

                fillMinutes.Sort();
                medianFillMinutes = fillMinutes.Count == 0 ? 0 : fillMinutes[fillMinutes.Count / 2];
            }

            double total = bestSample + bestFlood + bestTree;
            Console.WriteLine(
                $"  {metres,4} m   {samples,7}   {bestSample,9:F2}   {bestFlood,8:F2}   "
                + $"{bestTree,7:F2}   {total,8:F2}   {total * 1_000_000 / samples,9:F0}   "
                + $"{basins,6}   {nodes,5}   {curveCells * 4 / 1_000_000.0,9:F1}   "
                + $"{held / 1_000_000.0,7:F1}   {medianFillMinutes,8:F0}");
        }

        Console.WriteLine();
        Console.WriteLine(
            "  'held' is what every LEAF basin would contain at its own saddle — not what the "
            + "landscape holds, which is the");
        Console.WriteLine(
            "  root of the tree and much larger. 'curves MB' is what eager composite curves cost, "
            + "against building them lazily.");
        Console.WriteLine(
            "  'fill min' is how long the MEDIAN basin takes to fill at this world's heaviest "
            + "weather with no rainfall scaling —");
        Console.WriteLine(
            "  the number that says whether a pond filling can be watched or only inferred.");
        return 0;
    }
}
