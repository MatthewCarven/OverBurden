using System.Diagnostics;
using Overburden.Core.Fluid;
using Overburden.Core.Hydrology;
using Overburden.Core.Lighting;
using Overburden.Core.Voxels;
using Overburden.Core.Worldgen;
using Xunit.Abstractions;

namespace Overburden.Core.Tests;

/// <summary>
/// M8b-2b: where the analytic ground has a hole in it, kept up to date as the world is dug, built
/// on, loaded and forgotten.
/// </summary>
/// <remarks>
/// <para>
/// The headline is <c>IncrementalEqualsFromScratchAfterAnySequenceOfScansEditsAndForgets</c>,
/// which is M5b's oracle with a cheaper from-scratch: the definition evaluated over every voxel of
/// every scanned chunk and its one-voxel shell, against whatever the incremental bookkeeping
/// holds. The named tests around it each fix one clause of the definition, and each is the test a
/// break-recipe in the battery falls to.
/// </para>
/// <para>
/// <b>Heights here are the topmost solid voxel, as <c>ISurfaceHeights</c> reports them.</b> A
/// surface written as 40 means voxel 40 is rock and 41 is the first air, so the surface voxel of a
/// column is at Y 40 and its pit portal opens onto 41.
/// </para>
/// </remarks>
public class PortalIndexTests(ITestOutputHelper output)
{
    /// <summary>A world with a seed surface, a loaded set and edits, standing in for the streamer.</summary>
    private sealed class DugWorld(ISurfaceHeights seed) : IFluidTerrain
    {
        public ISurfaceHeights Seed { get; } = seed;

        public HashSet<ChunkPos> Loaded { get; } = [];

        public HashSet<VoxelPos> Dug { get; } = [];

        public HashSet<VoxelPos> Placed { get; } = [];

        public bool CanHold(VoxelPos v) =>
            Loaded.Contains(v.Chunk)
            && !Placed.Contains(v)
            && (Dug.Contains(v) || v.Y > Seed.SurfaceHeightAt(v.X, v.Z));

        public void Dig(PortalIndex index, VoxelPos v)
        {
            Placed.Remove(v);
            Dug.Add(v);
            index.OnVoxelChanged(v);
        }

        public void Place(PortalIndex index, VoxelPos v)
        {
            Dug.Remove(v);
            Placed.Add(v);
            index.OnVoxelChanged(v);
        }

        public void Unplace(PortalIndex index, VoxelPos v)
        {
            Placed.Remove(v);
            index.OnVoxelChanged(v);
        }

        public void Load(PortalIndex index, ChunkPos chunk)
        {
            Loaded.Add(chunk);
            index.Scan(chunk);
        }

        public void Unload(PortalIndex index, ChunkPos chunk)
        {
            Loaded.Remove(chunk);
            index.Forget(chunk);
        }
    }

    private static ISurfaceHeights Flat(int y) => new AnalyticSurface((_, _) => y);

    /// <summary>High ground west of <paramref name="atX"/>, low ground from it eastward.</summary>
    private static ISurfaceHeights Cliff(int atX, int high, int low) =>
        new AnalyticSurface((x, _) => x < atX ? high : low);

    /// <summary>The chunk column (0, 0)'s chunk holding Y 32..63, where every fixture's surface sits.</summary>
    private static readonly ChunkPos SurfaceChunk = new(0, 1, 0);

    [Fact]
    public void DiggingTheSurfaceVoxelOpensAPitAndDiggingDeeperOpensNoMore()
    {
        // The portal is the breach face, not the hole. A shaft has one, at its mouth: the voxel
        // below the mouth opens onto dug rock at or below the same surface, which is not the lake.
        var world = new DugWorld(Flat(40));
        var index = new PortalIndex(world.Seed, world);

        world.Load(index, SurfaceChunk);
        Assert.Equal(0, index.Count);

        world.Dig(index, new VoxelPos(5, 40, 5));
        Portal portal = Assert.Single(index.Under(0, 0));
        Assert.Equal(new Portal(new VoxelPos(5, 40, 5), new VoxelPos(5, 41, 5)), portal);
        Assert.True(portal.IsPit);

        world.Dig(index, new VoxelPos(5, 39, 5));
        world.Dig(index, new VoxelPos(5, 38, 5));
        Assert.Equal(1, index.Count);
        Assert.True(index.Contains(portal));
    }

    [Fact]
    public void ATunnelIntoABankIsAPortalAtTheFaceAndIsFiledUnderTheLakesSample()
    {
        // A tunnel driven west into a cliff at Y 35: the face voxel at x 15 opens east onto
        // x 16, which is above its own column's surface of 30 — the bank. The next voxel in
        // opens onto dug rock under a surface of 40, so it is not a portal. And the portal is
        // filed under the outside's sample (1, 0), where the water is, not the inside's (0, 0).
        var world = new DugWorld(Cliff(16, high: 40, low: 30));
        var index = new PortalIndex(world.Seed, world);

        world.Load(index, SurfaceChunk);
        Assert.Equal(0, index.Count);

        world.Dig(index, new VoxelPos(15, 35, 5));
        Portal portal = Assert.Single(index.Under(1, 0));
        Assert.Equal(new Portal(new VoxelPos(15, 35, 5), new VoxelPos(16, 35, 5)), portal);
        Assert.False(portal.IsPit);
        Assert.Empty(index.Under(0, 0));

        world.Dig(index, new VoxelPos(14, 35, 5));
        Assert.Equal(1, index.Count);
    }

    [Fact]
    public void AVoxelAboveItsColumnsSurfaceIsNeverAnInside()
    {
        var world = new DugWorld(Flat(40));
        var index = new PortalIndex(world.Seed, world);
        world.Load(index, SurfaceChunk);

        var found = new List<Portal>();
        index.OpeningsOf(new VoxelPos(5, 41, 5), found);
        Assert.Empty(found);

        world.Place(index, new VoxelPos(5, 45, 5));
        world.Unplace(index, new VoxelPos(5, 45, 5));
        Assert.Equal(0, index.Count);
    }

    [Fact]
    public void PlacingABlockClosesAPortalFromEitherSide()
    {
        var world = new DugWorld(Flat(40));
        var index = new PortalIndex(world.Seed, world);
        world.Load(index, SurfaceChunk);
        world.Dig(index, new VoxelPos(5, 40, 5));
        Assert.Equal(1, index.Count);

        // Capping the hole from above.
        world.Place(index, new VoxelPos(5, 41, 5));
        Assert.Equal(0, index.Count);
        world.Unplace(index, new VoxelPos(5, 41, 5));
        Assert.Equal(1, index.Count);

        // Filling the hole itself.
        world.Place(index, new VoxelPos(5, 40, 5));
        Assert.Equal(0, index.Count);
        Assert.Equal(0, index.SampleCount);
    }

    [Fact]
    public void APitUnderAChunkBoundaryIsFoundByWhicheverHalfArrivesSecond()
    {
        // The surface is the top row of the lower chunk, so the mouth is in one chunk and the
        // voxel it opens onto is in the chunk above. An unloaded voxel is solid, so the pair is
        // a portal only while both chunks are here — and the scan of whichever arrives second
        // has to find it, which is why a scan takes pairs touching the chunk from either side.
        var lower = new ChunkPos(0, 1, 0);
        var upper = new ChunkPos(0, 2, 0);
        var mouth = new VoxelPos(5, 63, 5);
        var expected = new Portal(mouth, new VoxelPos(5, 64, 5));

        var world = new DugWorld(Flat(63));
        var index = new PortalIndex(world.Seed, world);
        world.Load(index, lower);
        world.Dig(index, mouth);
        Assert.Equal(0, index.Count);

        world.Load(index, upper);
        Assert.True(index.Contains(expected));
        Assert.Equal(1, index.Count);

        world.Unload(index, upper);
        Assert.Equal(0, index.Count);
        world.Load(index, upper);
        Assert.Equal(1, index.Count);

        // The other order: the upper chunk is here first and the lower arrives already dug.
        var reversed = new DugWorld(Flat(63));
        var reversedIndex = new PortalIndex(reversed.Seed, reversed);
        reversed.Dug.Add(mouth);
        reversed.Load(reversedIndex, upper);
        Assert.Equal(0, reversedIndex.Count);
        reversed.Load(reversedIndex, lower);
        Assert.True(reversedIndex.Contains(expected));
        Assert.Equal(1, reversedIndex.Count);
    }

    [Fact]
    public void ForgettingEitherChunkOfABankPortalRemovesIt()
    {
        // The inside is the last column of chunk 0 and the outside the first of chunk 1, filed
        // under sample (2, 0). Forgetting the outside's chunk removes it — that voxel reads as
        // solid now — and so does forgetting the inside's.
        var west = new ChunkPos(0, 1, 0);
        var east = new ChunkPos(1, 1, 0);
        var portal = new Portal(new VoxelPos(31, 35, 5), new VoxelPos(32, 35, 5));

        var world = new DugWorld(Cliff(32, high: 40, low: 30));
        var index = new PortalIndex(world.Seed, world);
        world.Load(index, west);
        world.Load(index, east);
        world.Dig(index, portal.Inside);
        Assert.Equal([portal], index.Under(2, 0));

        world.Unload(index, east);
        Assert.Equal(0, index.Count);
        world.Load(index, east);
        Assert.Equal(1, index.Count);

        world.Unload(index, west);
        Assert.Equal(0, index.Count);
        Assert.Equal(0, index.SampleCount);
    }

    [Fact]
    public void ScanningTwiceAddsNothing()
    {
        var world = new DugWorld(Flat(40));
        var index = new PortalIndex(world.Seed, world);
        world.Load(index, SurfaceChunk);
        world.Dig(index, new VoxelPos(5, 40, 5));
        world.Dig(index, new VoxelPos(9, 40, 9));

        Assert.Equal(0, index.Scan(SurfaceChunk));
        Assert.Equal(2, index.Count);
    }

    [Fact]
    public void ANaturalCaveIsNeverAPitPortal()
    {
        // Worldgen keeps every cave CaveSurfaceMarginVoxels below its own column's surface, so no
        // cave voxel is ever the surface voxel and no unedited chunk can hold a pit. A cave mouth
        // on a cliff can be a bank, and the index does not know an edit from a cave — filtering
        // to dug chunks is the caller's rule (M8b-2 decision 1), stated in PortalIndex's remarks.
        //
        // THE FIXTURE HAS TO HAVE CAVES AT THE MARGIN, AND THE FIRST ONE DID NOT. At the shipped
        // threshold of 0.42 the cave field over this whole region peaks at 0.361 — not one cave
        // in 64 chunks, not even twelve voxels down — so "0 pits" was a scan of solid rock, and
        // removing the margin from the generator left it green. The threshold here is lowered
        // until caves sit directly under the margin, and that is asserted, so the margin is the
        // only thing between a cave and the surface.
        var settings = new WorldGenSettings
        {
            Seed = 0xC0FFEEUL,
            SeaLevelVoxels = 0,
            SurfaceAmplitudeVoxels = 48.0f,
            SurfaceFrequency = 0.004f,
            SurfaceOctaves = 4,
            CavesEnabled = true,
            CaveLatticeStep = 4,
            CaveThreshold = 0.2f,
        };
        var strata = new StrataTable([new StrataLayer(1, 1), new StrataLayer(2, 3)], 3);
        var generator = new WorldGenerator(settings, strata);

        int surface = generator.SurfaceHeightAt(0, 0);
        const int half = 40;
        var terrain = new GeneratedTerrain(
            generator, -64, surface - half, -64, 63, surface + half, 63);
        var index = new PortalIndex(generator, terrain);

        int cavesAtTheMargin = 0;
        for (int z = -64; z < 64; z++)
        {
            for (int x = -64; x < 64; x++)
            {
                int s = generator.SurfaceHeightAt(x, z);
                if (generator.IsCaveAt(x, s - settings.CaveSurfaceMarginVoxels, z, s))
                {
                    cavesAtTheMargin++;
                }
            }
        }

        Assert.True(cavesAtTheMargin > 0, "no cave sits directly under the margin, so the margin is untested");

        int scanned = 0;
        for (int cz = -2; cz < 2; cz++)
        {
            for (int cx = -2; cx < 2; cx++)
            {
                for (int cy = (surface - half) >> VoxelConstants.ChunkSizeLog2;
                     cy <= (surface + half) >> VoxelConstants.ChunkSizeLog2;
                     cy++)
                {
                    index.Scan(new ChunkPos(cx, cy, cz));
                    scanned++;
                }
            }
        }

        int banks = 0;
        foreach ((int X, int Z) sample in index.Samples)
        {
            foreach (Portal portal in index.Under(sample.X, sample.Z))
            {
                Assert.False(portal.IsPit, $"natural pit portal at {portal.Inside}");
                banks++;
            }
        }

        output.WriteLine(
            $"{scanned} unedited chunks scanned over {cavesAtTheMargin} column(s) with a cave directly "
            + $"under the margin: {banks} bank portal(s), 0 pits");
    }

    [Fact]
    public void IncrementalEqualsFromScratchAfterAnySequenceOfScansEditsAndForgets()
    {
        // THE ORACLE. From scratch is the definition (OpeningsOf) over every voxel of every
        // scanned chunk and the one-voxel shell around it — a pair touches a chunk from either
        // side — kept where both halves hold. Cheap enough to be the definition, so it is.
        var random = new Random(20260905);
        ChunkPos[] chunks =
        [
            new(0, 1, 0), new(1, 1, 0), new(0, 1, 1), new(1, 1, 1),
        ];

        int checks = 0;
        int checksWithBanks = 0;
        int checksWithPits = 0;
        int checksAfterUnload = 0;

        for (int trial = 0; trial < 3; trial++)
        {
            // A hill with 4 m terraces and a cliff line, all inside chunk Y 1 (32..63).
            int salt = trial * 7919;
            var seed = new AnalyticSurface((x, z) =>
                38 + (Hash((x >> 2) + salt, z >> 2) % 5) + (x >= 40 ? 0 : 4));

            var world = new DugWorld(seed);
            var index = new PortalIndex(seed, world);
            world.Load(index, chunks[0]);
            bool unloadedRecently = false;

            for (int op = 0; op < 150; op++)
            {
                int kind = random.Next(100);
                ChunkPos chunk = chunks[random.Next(chunks.Length)];
                int x = (chunk.X << VoxelConstants.ChunkSizeLog2) + random.Next(VoxelConstants.ChunkSize);
                int z = (chunk.Z << VoxelConstants.ChunkSizeLog2) + random.Next(VoxelConstants.ChunkSize);
                int s = seed.SurfaceHeightAt(x, z);

                if (kind < 50)
                {
                    if (world.Loaded.Contains(chunk))
                    {
                        world.Dig(index, new VoxelPos(x, s - random.Next(4), z));
                    }
                }
                else if (kind < 70)
                {
                    if (world.Loaded.Contains(chunk))
                    {
                        world.Place(index, new VoxelPos(x, s - 2 + random.Next(5), z));
                    }
                }
                else if (kind < 85)
                {
                    if (!world.Loaded.Contains(chunk))
                    {
                        world.Load(index, chunk);
                    }
                }
                else if (world.Loaded.Contains(chunk))
                {
                    world.Unload(index, chunk);
                    unloadedRecently = true;
                }

                if (op % 5 != 4)
                {
                    continue;
                }

                HashSet<Portal> expected = FromScratch(index, world);
                Assert.Equal(expected.Count, index.Count);
                foreach (Portal portal in expected)
                {
                    Assert.True(index.Contains(portal), $"missing {portal}");
                }

                int filed = 0;
                foreach ((int X, int Z) sample in index.Samples)
                {
                    foreach (Portal portal in index.Under(sample.X, sample.Z))
                    {
                        Assert.Contains(portal, expected);
                        Assert.Equal(sample, index.SampleOf(portal.Outside));
                        filed++;
                    }
                }

                Assert.Equal(expected.Count, filed);

                checks++;
                if (expected.Any(p => !p.IsPit)) checksWithBanks++;
                if (expected.Any(p => p.IsPit)) checksWithPits++;
                if (unloadedRecently && expected.Count > 0) checksAfterUnload++;
                unloadedRecently = false;
            }
        }

        output.WriteLine(
            $"{checks} checks: {checksWithPits} with pits, {checksWithBanks} with banks, "
            + $"{checksAfterUnload} non-empty after an unload");
        Assert.True(checksWithPits > 0 && checksWithBanks > 0, "the fixture never produced both kinds");
        Assert.True(checksAfterUnload > 0, "no check ever followed a forget with something left");
    }

    [Fact]
    public void AChunkBiteOfEditsIsTimed()
    {
        // The plan's estimate is ~3 ms a bite with the seed surface as array reads. This is Debug
        // against a lambda seed and a hash-set terrain, so it is a shape rather than a number; the
        // bench's --portal arm is the measurement. Two arms: a chunk holding the surface, where
        // every column's surface voxel opens a pit, and a chunk wholly below it, which is the
        // geometric exit 32,768 times.
        var world = new DugWorld(Flat(40));
        var index = new PortalIndex(world.Seed, world);
        world.Load(index, SurfaceChunk);
        var deep = new ChunkPos(0, 0, 0);
        world.Load(index, deep);

        double surfaceMillis = TimeBite(world, index, SurfaceChunk);
        Assert.Equal(VoxelConstants.ChunkSize * VoxelConstants.ChunkSize, index.Count);

        double deepMillis = TimeBite(world, index, deep);
        Assert.Equal(VoxelConstants.ChunkSize * VoxelConstants.ChunkSize, index.Count);

        output.WriteLine(
            $"bite of 32,768 edits: surface chunk {surfaceMillis:F2} ms, deep chunk {deepMillis:F2} ms "
            + "(Debug, lambda seed; the bench's --portal arm is the measurement)");
    }

    [Fact]
    public void ASkyColumnKeepsItsSeedSurfaceAndARestoreLeavesItAlone()
    {
        var surface = new AnalyticSurface((x, z) => 10 + (((x * 3) + (z * 5)) & 7));
        var column = new SkyColumn(2, -1);
        Assert.False(column.IsSeeded);
        Assert.Throws<InvalidOperationException>(() => column.SeedSurfaceY(0, 0));

        column.Seed(surface);
        Assert.True(column.IsSeeded);
        for (int lz = 0; lz < VoxelConstants.ChunkSize; lz++)
        {
            for (int lx = 0; lx < VoxelConstants.ChunkSize; lx++)
            {
                int expected = surface.SurfaceHeightAt(64 + lx, -32 + lz);
                Assert.Equal(expected, column.SeedSurfaceY(lx, lz));
                Assert.Equal(expected + 1, column.OpenAboveY(lx, lz));
            }
        }

        // A restore arrives as a blob-read column with open heights only, and copies those.
        var restored = new SkyColumn(2, -1);
        restored.SetOpenAboveY(3, 4, 5);
        restored.MarkEdited();
        column.CopyFrom(restored);
        Assert.Equal(5, column.OpenAboveY(3, 4));
        Assert.Equal(surface.SurfaceHeightAt(67, -28), column.SeedSurfaceY(3, 4));
    }

    private static double TimeBite(DugWorld world, PortalIndex index, ChunkPos chunk)
    {
        VoxelPos origin = chunk.OriginVoxel;
        for (int y = 0; y < VoxelConstants.ChunkSize; y++)
        {
            for (int z = 0; z < VoxelConstants.ChunkSize; z++)
            {
                for (int x = 0; x < VoxelConstants.ChunkSize; x++)
                {
                    world.Dug.Add(new VoxelPos(origin.X + x, origin.Y + y, origin.Z + z));
                }
            }
        }

        long started = Stopwatch.GetTimestamp();
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

        return Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }

    private static HashSet<Portal> FromScratch(PortalIndex index, DugWorld world)
    {
        var expected = new HashSet<Portal>();
        var found = new List<Portal>();

        foreach (ChunkPos chunk in world.Loaded)
        {
            VoxelPos origin = chunk.OriginVoxel;
            for (int y = -1; y <= VoxelConstants.ChunkSize; y++)
            {
                for (int z = -1; z <= VoxelConstants.ChunkSize; z++)
                {
                    for (int x = -1; x <= VoxelConstants.ChunkSize; x++)
                    {
                        found.Clear();
                        index.OpeningsOf(new VoxelPos(origin.X + x, origin.Y + y, origin.Z + z), found);
                        foreach (Portal portal in found)
                        {
                            if (portal.Inside.Chunk == chunk || portal.Outside.Chunk == chunk)
                            {
                                expected.Add(portal);
                            }
                        }
                    }
                }
            }
        }

        return expected;
    }

    private static int Hash(int a, int b)
    {
        unchecked
        {
            uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663);
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return (int)(h & 0x7fffffff);
        }
    }
}
