using System.Diagnostics;
using System.Globalization;
using Overburden.Core.Content;
using Overburden.Core.Fluid;
using Overburden.Core.Voxels;
using Overburden.Core.Worldgen;

namespace Overburden.Bench;

/// <summary>
/// M8c-1: what the seep index costs and what it says — a room dug under a lake in real strata,
/// read through the game's own <see cref="VoxelFluidTerrain"/>, swept by head and by rock.
/// </summary>
/// <remarks>
/// <para>
/// Three numbers the plan estimated and this replaces: the scan of a dug chunk at arrival (the
/// plan's 1.5–2 ms, Release, priced against the portal scan's 1.2 ms), the edit path under a chunk
/// bite deep in a mine (which the portal hook priced at 0.9 ms with a geometric exit the seep hook
/// does not have), and the rate — a 10 × 10 × 3 m stone room under 5 m of head at the shipped
/// scale, the plan's "about a litre a second".
/// </para>
/// <para>
/// The room is dug into generated chunks held in a dictionary and read through
/// <see cref="VoxelFluidTerrain"/> — the game's path, as <c>--terrain</c> argued — with the real
/// mod's permeabilities, so a stone room is stone because <c>stone.json</c> says 0.02. The dirt arm
/// answers every rock as dirt through a wrapper, because the shipped dirt band is three voxels
/// thick and no 3 m room fits in it; it is "the same room, if the ground were dirt".
/// </para>
/// </remarks>
internal static class SeepBench
{
    /// <summary>Generated chunks in a dictionary. Deliberately <c>ChunkStreamer.TryGetMaterial</c>'s shape.</summary>
    private sealed class ChunkedWorld : IVoxelWorld
    {
        private readonly Dictionary<ChunkPos, VoxelChunk> _chunks = [];

        public int Chunks => _chunks.Count;

        public void Add(ChunkPos position, VoxelChunk chunk) => _chunks[position] = chunk;

        public bool Has(ChunkPos position) => _chunks.ContainsKey(position);

        public void Set(VoxelPos voxel, int materialId)
        {
            (int lx, int ly, int lz) = voxel.Local;
            _chunks[voxel.Chunk].Set(lx, ly, lz, materialId);
        }

        public bool TryGetMaterial(VoxelPos voxel, out int materialId)
        {
            if (!_chunks.TryGetValue(voxel.Chunk, out VoxelChunk? chunk))
            {
                materialId = MaterialIds.AirNumericId;
                return false;
            }

            (int localX, int localY, int localZ) = voxel.Local;
            materialId = chunk.Get(localX, localY, localZ);
            return true;
        }
    }

    /// <summary>The same holdability, every rock answered as one material.</summary>
    private sealed class OneRock(ISeepTerrain inner, float permeability) : ISeepTerrain
    {
        public bool CanHold(VoxelPos voxel) => inner.CanHold(voxel);

        public bool IsLoaded(VoxelPos voxel) => inner.IsLoaded(voxel);

        public bool TryGetRock(VoxelPos voxel, out float perm)
        {
            bool rock = inner.TryGetRock(voxel, out _);
            perm = rock ? permeability : 0f;
            return rock;
        }
    }

    private sealed class FlatTable(double? level) : IWaterTable
    {
        public double? TableAt(int sampleX, int sampleZ) => level;
    }

    internal static int Run(
        WorldGenerator generator,
        MaterialTable materials,
        float stonePermeability,
        float dirtPermeability,
        string machineControl,
        bool isDebug)
    {
        Console.WriteLine("Seep bench — M8c-1's index over a room under a lake");
        if (isDebug)
        {
            Console.WriteLine("*** DEBUG BUILD — these timings are meaningless. Re-run with -c Release. ***");
        }

        Console.WriteLine();
        Console.WriteLine(machineControl);
        Console.WriteLine();

        // The room: 10 x 10 x 3 m, its roof six metres under the surface at the origin.
        const int roomVoxels = 40;
        const int roomHeight = 12;
        const int depthVoxels = 24;
        int surface = generator.SurfaceHeightAt(16, 16);
        int roofY = surface - depthVoxels;
        int floorY = roofY - roomHeight + 1;
        var min = new VoxelPos(0, floorY, 0);
        var max = new VoxelPos(roomVoxels - 1, roofY, roomVoxels - 1);

        var world = new ChunkedWorld();
        ChunkPos lowChunk = min.Offset(-1, -1, -1).Chunk;
        ChunkPos highChunk = max.Offset(1, 1, 1).Chunk;
        for (int cz = lowChunk.Z; cz <= highChunk.Z; cz++)
        {
            for (int cy = lowChunk.Y; cy <= highChunk.Y; cy++)
            {
                for (int cx = lowChunk.X; cx <= highChunk.X; cx++)
                {
                    var position = new ChunkPos(cx, cy, cz);
                    world.Add(position, generator.GenerateTerrain(position));
                }
            }
        }

        var roomChunks = new HashSet<ChunkPos>();
        int dug = 0;
        for (int z = min.Z; z <= max.Z; z++)
        {
            for (int y = min.Y; y <= max.Y; y++)
            {
                for (int x = min.X; x <= max.X; x++)
                {
                    var voxel = new VoxelPos(x, y, z);
                    world.Set(voxel, MaterialIds.AirNumericId);
                    roomChunks.Add(voxel.Chunk);
                    dug++;
                }
            }
        }

        var terrain = new VoxelFluidTerrain(world, materials);

        // The seed surface as the game reads it: array reads off the sky columns, not the noise.
        // M8b-2b measured the difference at 1.2 ms against 10.3 ms a bite, and the seep hook
        // reads the surface of every edited voxel's column too.
        var seed = new PortalBench.ArraySeed(generator, -64, -64, 256);
        Console.WriteLine(
            $"  Room: {roomVoxels / 4} x {roomVoxels / 4} x {roomHeight / 4.0:F0} m, {dug:N0} voxels dug, floor at Y {floorY}, "
            + $"roof {depthVoxels / 4.0:F1} m under the seed surface ({surface} at the origin); "
            + $"{roomChunks.Count} chunk(s) hold it, {world.Chunks} generated around it.");
        Console.WriteLine();

        // Arm 1: the real strata.
        var index = new SeepIndex(seed, terrain);
        double scanMillis = double.MaxValue;
        for (int rep = 0; rep < 3; rep++)
        {
            foreach (ChunkPos chunk in roomChunks)
            {
                index.Forget(chunk);
            }

            long started = Stopwatch.GetTimestamp();
            foreach (ChunkPos chunk in roomChunks)
            {
                index.Scan(chunk);
            }

            scanMillis = Math.Min(scanMillis, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        // And one chunk of solid rock beside the room, which is what most of a dug mine's
        // neighbours are: 32,768 holdability reads that each exit at once.
        ChunkPos rockChunk = new VoxelPos(-20, floorY, 16).Chunk;
        var rockIndex = new SeepIndex(seed, terrain);
        double rockScanMillis = Best(3, () =>
        {
            rockIndex.Forget(rockChunk);
            rockIndex.Scan(rockChunk);
        });

        // The read itself, so the scan's cost can be read as reads rather than as a mystery.
        VoxelPos rockOrigin = rockChunk.OriginVoxel;
        int held = 0;
        double readMillis = Best(3, () =>
        {
            held = 0;
            for (int y = 0; y < VoxelConstants.ChunkSize; y++)
            {
                for (int z = 0; z < VoxelConstants.ChunkSize; z++)
                {
                    for (int x = 0; x < VoxelConstants.ChunkSize; x++)
                    {
                        if (terrain.CanHold(new VoxelPos(rockOrigin.X + x, rockOrigin.Y + y, rockOrigin.Z + z))) held++;
                    }
                }
            }
        });

        int groups = roomChunks.Sum(c => index.GroupsIn(c).Count);
        int floors = roomChunks.Sum(c => index.FacesIn(c).Count(f => f.IsFloor));
        int roofs = roomChunks.Sum(c => index.FacesIn(c).Count(f => f.IsCeiling));
        Console.WriteLine(
            $"  Scan (the arrival path): {index.Count:N0} face(s) in {index.ChunkCount} chunk(s), "
            + $"{groups} group(s), {floors:N0} floor / {index.Count - floors - roofs:N0} wall / {roofs:N0} roof, "
            + $"{scanMillis:F2} ms for the {roomChunks.Count} chunks, best of three");
        Console.WriteLine(
            $"  Scan of a chunk of solid rock beside it ({rockChunk}): {rockScanMillis:F2} ms, {rockIndex.Count} face(s); "
            + $"32,768 CanHold reads on that chunk alone: {readMillis:F2} ms ({held} holdable)");

        // The delivery order the drain will walk: the first face handed out, per chunk.
        foreach (ChunkPos chunk in roomChunks.OrderBy(c => c.Y).ThenBy(c => c.X).ThenBy(c => c.Z))
        {
            IReadOnlyList<SeepFace> faces = index.FacesIn(chunk);
            if (faces.Count == 0) continue;
            int lowest = faces.Min(f => f.Y);
            SeepFace first = faces[0];
            Console.WriteLine(
                $"    chunk {chunk}: first face handed out at Y {first.Y} ({(first.IsFloor ? "floor" : first.IsCeiling ? "roof" : "wall")}), "
                + $"lowest face Y {lowest} — lowest first: {(first.Y == lowest ? "yes" : "NO")}, floor first: {(first.IsFloor || floors == 0 ? "yes" : "NO")}");
        }

        Console.WriteLine();

        // The rate, swept by head over the floor voxel's centre, for the real strata and for the
        // same room if every rock were dirt.
        var dirtIndex = new SeepIndex(seed, new OneRock(terrain, dirtPermeability));
        var stoneIndex = new SeepIndex(seed, new OneRock(terrain, stonePermeability));
        foreach (ChunkPos chunk in roomChunks)
        {
            dirtIndex.Scan(chunk);
            stoneIndex.Scan(chunk);
        }

        const double ankleDeepLitres = 25_000;
        Console.WriteLine(
            $"  Rate at seepageScale {SeepIndex.DefaultSeepageScale} L/(m²·m·s), 2 Hz ticks; ankle-deep is {ankleDeepLitres:N0} L on this floor.");
        Console.WriteLine();
        Console.WriteLine("  head over floor   real strata L/s   L/tick   h to ankle-deep   all stone L/s   all dirt L/s   coefficient ms");
        Console.WriteLine("  ---------------   ---------------   ------   ---------------   -------------   ------------   --------------");
        foreach (double headMetres in new[] { 0.5, 1, 2, 5, 10, 20 })
        {
            double level = floorY + 0.5 + (headMetres * VoxelConstants.VoxelsPerMetre);
            var table = new FlatTable(level);

            double coefficient = 0;
            double coefficientMillis = Best(5, () =>
            {
                coefficient = 0;
                foreach (ChunkPos chunk in roomChunks)
                {
                    coefficient += index.Coefficient(chunk, table);
                }
            });

            double stone = roomChunks.Sum(c => stoneIndex.Coefficient(c, table));
            double dirt = roomChunks.Sum(c => dirtIndex.Coefficient(c, table));
            double perSecond = SeepIndex.LitresPerSecond(coefficient, SeepIndex.DefaultSeepageScale);
            double perTick = perSecond / 2;
            double hours = perSecond > 0 ? ankleDeepLitres / perSecond / 3600 : double.PositiveInfinity;
            Console.WriteLine(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "  {0,13:F1} m   {1,15:F3}   {2,6:F3}   {3,15:F2}   {4,13:F3}   {5,12:F3}   {6,14:F4}",
                    headMetres,
                    perSecond,
                    perTick,
                    hours,
                    SeepIndex.LitresPerSecond(stone, SeepIndex.DefaultSeepageScale),
                    SeepIndex.LitresPerSecond(dirt, SeepIndex.DefaultSeepageScale),
                    coefficientMillis));
        }

        Console.WriteLine();
        Console.WriteLine("  A face contributes permeability × head at its own centre × 0.0625 m²; a face at or above the table is dry.");
        Console.WriteLine();

        // The edit path: a chunk bite deep under the lake, 32,768 OnVoxelChanged calls with no
        // geometric exit, against the scan of the same chunk from scratch — the arrival path.
        ChunkPos bite = new VoxelPos(80, floorY, 80).Chunk;
        for (int cz = bite.Z - 1; cz <= bite.Z + 1; cz++)
        {
            for (int cy = bite.Y - 1; cy <= bite.Y + 1; cy++)
            {
                for (int cx = bite.X - 1; cx <= bite.X + 1; cx++)
                {
                    var position = new ChunkPos(cx, cy, cz);
                    if (!world.Has(position))
                    {
                        world.Add(position, generator.GenerateTerrain(position));
                    }
                }
            }
        }

        VoxelPos origin = bite.OriginVoxel;
        int solidBefore = 0;
        for (int y = 0; y < VoxelConstants.ChunkSize; y++)
        {
            for (int z = 0; z < VoxelConstants.ChunkSize; z++)
            {
                for (int x = 0; x < VoxelConstants.ChunkSize; x++)
                {
                    var voxel = new VoxelPos(origin.X + x, origin.Y + y, origin.Z + z);
                    if (!terrain.CanHold(voxel)) solidBefore++;
                    world.Set(voxel, MaterialIds.AirNumericId);
                }
            }
        }

        double biteMillis = Best(3, () =>
        {
            for (int y = 0; y < VoxelConstants.ChunkSize; y++)
            {
                for (int z = 0; z < VoxelConstants.ChunkSize; z++)
                {
                    for (int x = 0; x < VoxelConstants.ChunkSize; x++)
                    {
                        index.OnVoxelChanged(new VoxelPos(origin.X + x, origin.Y + y, origin.Z + z));
                    }
                }
            }
        });
        int biteFaces = index.FacesIn(bite).Count;

        var fresh = new SeepIndex(seed, terrain);
        double biteScanMillis = double.MaxValue;
        for (int rep = 0; rep < 3; rep++)
        {
            fresh.Forget(bite);
            long started = Stopwatch.GetTimestamp();
            fresh.Scan(bite);
            biteScanMillis = Math.Min(biteScanMillis, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        // The analytic seed for the same bite, so the surface read's share is a number rather
        // than a memory of M8b-2b's.
        var analytic = new SeepIndex(generator, terrain);
        double analyticBiteMillis = Best(3, () =>
        {
            for (int y = 0; y < VoxelConstants.ChunkSize; y++)
            {
                for (int z = 0; z < VoxelConstants.ChunkSize; z++)
                {
                    for (int x = 0; x < VoxelConstants.ChunkSize; x++)
                    {
                        analytic.OnVoxelChanged(new VoxelPos(origin.X + x, origin.Y + y, origin.Z + z));
                    }
                }
            }
        });

        Console.WriteLine(
            $"  Chunk bite at {bite} ({solidBefore:N0} of 32,768 voxels were rock, {VoxelConstants.ChunkSize * VoxelConstants.ChunkSize * 6:N0} faces at most):");
        Console.WriteLine(
            $"    edit path, 32,768 OnVoxelChanged calls: {biteMillis:F2} ms, {biteFaces:N0} face(s) filed "
            + $"(the first pass files them and the others re-evaluate them, which is the same reads)");
        Console.WriteLine(
            $"    arrival path, Scan of the bitten chunk: {biteScanMillis:F2} ms, {fresh.FacesIn(bite).Count:N0} face(s)");
        Console.WriteLine(
            $"    edit path with the analytic seed instead of array reads: {analyticBiteMillis:F2} ms");
        Console.WriteLine();
        Console.WriteLine("  Divide every millisecond by the machine control before comparing with the worklog.");
        return 0;
    }

    private static double Best(int reps, Action work)
    {
        double best = double.MaxValue;
        for (int rep = 0; rep < reps; rep++)
        {
            long started = Stopwatch.GetTimestamp();
            work();
            best = Math.Min(best, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        return best;
    }
}
