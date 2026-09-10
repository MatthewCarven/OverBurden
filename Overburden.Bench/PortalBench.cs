using System.Diagnostics;
using System.Globalization;
using Overburden.Core.Fluid;
using Overburden.Core.Lighting;
using Overburden.Core.Voxels;
using Overburden.Core.Worldgen;

namespace Overburden.Bench;

/// <summary>
/// M8b-2b: what a chunk bite costs the portal index, with the seed surface as the array reads the
/// sky columns hold against the analytic function — the difference that decided where the seed
/// heights live.
/// </summary>
/// <remarks>
/// Two chunks: the one holding the surface under the bite's footprint, where every column's
/// surface voxel opens a pit and the terrain is read; and one two chunks below it, where all
/// 32,768 calls take the geometric exit. The plan priced a bite at ~3 ms with array reads and
/// called it an estimate; this is the number.
/// </remarks>
internal static class PortalBench
{
    /// <summary>The seed surface over a region as one array read, which is what <c>SkyColumn</c> holds.</summary>
    internal sealed class ArraySeed : ISurfaceHeights
    {
        private readonly WorldGenerator _generator;
        private readonly int _minX;
        private readonly int _minZ;
        private readonly int _span;
        private readonly int[] _surface;

        public ArraySeed(WorldGenerator generator, int minX, int minZ, int span)
        {
            _generator = generator;
            _minX = minX;
            _minZ = minZ;
            _span = span;
            _surface = new int[span * span];
            for (int z = 0; z < span; z++)
            {
                for (int x = 0; x < span; x++)
                {
                    _surface[(z * span) + x] = generator.SurfaceHeightAt(minX + x, minZ + z);
                }
            }
        }

        public int SurfaceHeightAt(int worldX, int worldZ)
        {
            int lx = worldX - _minX;
            int lz = worldZ - _minZ;
            if (lx < 0 || lz < 0 || lx >= _span || lz >= _span)
            {
                return _generator.SurfaceHeightAt(worldX, worldZ);
            }

            return _surface[(lz * _span) + lx];
        }
    }

    /// <summary>Holdability over a box: dug voxels, air above the surface, caves. Unloaded is solid.</summary>
    private sealed class BiteTerrain(
        WorldGenerator generator, ISurfaceHeights seed, int minX, int minY, int minZ, int span, int height)
        : IFluidTerrain
    {
        public HashSet<VoxelPos> Dug { get; } = [];

        public bool CanHold(VoxelPos v)
        {
            if (v.X < minX || v.Z < minZ || v.Y < minY
                || v.X >= minX + span || v.Z >= minZ + span || v.Y >= minY + height)
            {
                return false;
            }

            if (Dug.Contains(v))
            {
                return true;
            }

            int surface = seed.SurfaceHeightAt(v.X, v.Z);
            return v.Y > surface || generator.IsCaveAt(v.X, v.Y, v.Z, surface);
        }
    }

    internal static int Run(WorldGenerator generator, string machineControl, bool isDebug)
    {
        if (isDebug)
        {
            Console.WriteLine("WARNING: Debug build. A Debug figure is not a measurement; use -c Release.");
        }

        Console.WriteLine(machineControl);
        Console.WriteLine();

        int surfaceY = generator.SurfaceHeightAt(16, 16);
        var surfaceChunk = new ChunkPos(0, surfaceY >> VoxelConstants.ChunkSizeLog2, 0);
        var deepChunk = surfaceChunk with { Y = surfaceChunk.Y - 2 };

        const int span = 128;
        const int minX = -48;
        const int minZ = -48;
        int minY = deepChunk.OriginVoxel.Y - 8;
        int height = (surfaceChunk.OriginVoxel.Y + VoxelConstants.ChunkSize + 40) - minY;

        var arraySeed = new ArraySeed(generator, minX, minZ, span);

        Console.WriteLine(
            $"portal index over chunk {surfaceChunk} (surface {surfaceY} at its centre) and {deepChunk}");
        Console.WriteLine();
        Console.WriteLine("seed source    scan unedited   bite: surface chunk   bite: deep chunk   portals after");
        Console.WriteLine("-----------    -------------   -------------------   ----------------   -------------");

        foreach ((string name, ISurfaceHeights seed) in new (string, ISurfaceHeights)[]
                 {
                     ("array reads", arraySeed),
                     ("analytic", generator),
                 })
        {
            var terrain = new BiteTerrain(generator, seed, minX, minY, minZ, span, height);
            var index = new PortalIndex(seed, terrain);

            double scan = Best(3, () => index.Scan(surfaceChunk));
            double surfaceBite = TimeBite(index, terrain, surfaceChunk);
            double deepBite = TimeBite(index, terrain, deepChunk);

            Console.WriteLine(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0,-14} {1,10:F3} ms   {2,16:F3} ms   {3,13:F3} ms   {4,13:N0}",
                    name,
                    scan,
                    surfaceBite,
                    deepBite,
                    index.Count));
        }

        Console.WriteLine();
        Console.WriteLine(
            "A bite is 32,768 OnVoxelChanged calls; best of three, the first of which files the portals");
        Console.WriteLine(
            "and the others re-evaluate them, which is the same work. Divide by the machine control.");
        return 0;
    }

    private static double TimeBite(PortalIndex index, BiteTerrain terrain, ChunkPos chunk)
    {
        VoxelPos origin = chunk.OriginVoxel;
        for (int y = 0; y < VoxelConstants.ChunkSize; y++)
        {
            for (int z = 0; z < VoxelConstants.ChunkSize; z++)
            {
                for (int x = 0; x < VoxelConstants.ChunkSize; x++)
                {
                    terrain.Dug.Add(new VoxelPos(origin.X + x, origin.Y + y, origin.Z + z));
                }
            }
        }

        return Best(3, () =>
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
