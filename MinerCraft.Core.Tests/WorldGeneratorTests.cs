using MinerCraft.Core.Content;
using MinerCraft.Core.Voxels;
using MinerCraft.Core.Worldgen;

namespace MinerCraft.Core.Tests;

public class WorldGeneratorTests
{
    private const int Grass = 1;
    private const int Dirt = 2;
    private const int Stone = 3;
    private const int Size = VoxelConstants.ChunkSize;

    private static StrataTable Strata() =>
        new([new StrataLayer(Grass, 1), new StrataLayer(Dirt, 3)], Stone);

    private static WorldGenSettings Settings(bool caves = true) => new()
    {
        Seed = 0x5EEDUL,
        SeaLevelVoxels = 0,
        SurfaceAmplitudeVoxels = 48.0f,
        SurfaceFrequency = 0.004f,
        SurfaceOctaves = 4,
        CavesEnabled = caves,
    };

    private static WorldGenerator Generator(bool caves = true) => new(Settings(caves), Strata());

    [Fact]
    public void TheSameChunkGeneratesIdenticallyEveryTime()
    {
        WorldGenerator generator = Generator();
        var position = new ChunkPos(3, 0, -2);

        VoxelChunk first = generator.GenerateTerrain(position);
        VoxelChunk second = generator.GenerateTerrain(position);

        AssertSameContent(first, second);
    }

    [Fact]
    public void TwoGeneratorsWithTheSameSeedAgree()
    {
        // A fresh generator must reproduce the world exactly, or reloading a save regenerates
        // different terrain than the one that was saved.
        var position = new ChunkPos(-5, 0, 11);

        VoxelChunk first = new WorldGenerator(Settings(), Strata()).GenerateTerrain(position);
        VoxelChunk second = new WorldGenerator(Settings(), Strata()).GenerateTerrain(position);

        AssertSameContent(first, second);
    }

    [Fact]
    public void GenerationOrderDoesNotChangeTheResult()
    {
        // Guards against anyone introducing shared mutable state in the generator — a cache,
        // a reused buffer, a "last chunk" field. The moment that exists, chunks stop being
        // order-independent and streaming produces different terrain depending on which way
        // the player walked in.
        var positions = new[]
        {
            new ChunkPos(0, 0, 0), new ChunkPos(1, 0, 0), new ChunkPos(0, 0, 1), new ChunkPos(-1, 0, -1),
        };

        WorldGenerator forwards = Generator();
        var inOrder = positions.Select(forwards.GenerateTerrain).ToArray();

        WorldGenerator backwards = Generator();
        var reversed = positions.Reverse().Select(backwards.GenerateTerrain).Reverse().ToArray();

        for (int i = 0; i < positions.Length; i++)
        {
            AssertSameContent(inOrder[i], reversed[i]);
        }
    }

    [Fact]
    public void AdjacentChunksAgreeAcrossTheSeam()
    {
        // The seam bug is generating from local coordinates instead of world ones. It looks
        // fine inside any single chunk and produces a visible cliff at every boundary.
        WorldGenerator generator = Generator(caves: false);

        VoxelChunk left = generator.GenerateTerrain(new ChunkPos(0, 0, 0));
        VoxelChunk right = generator.GenerateTerrain(new ChunkPos(1, 0, 0));

        for (int lz = 0; lz < Size; lz++)
        {
            // Last column of the left chunk is world x = 31; first of the right is x = 32.
            AssertColumnMatchesSurface(generator, left, lx: Size - 1, lz, worldX: Size - 1, chunkY: 0);
            AssertColumnMatchesSurface(generator, right, lx: 0, lz, worldX: Size, chunkY: 0);
        }
    }

    [Fact]
    public void SurfaceHeightDependsOnlyOnHorizontalPosition()
    {
        WorldGenerator generator = Generator();

        // Same column, asked twice, from anywhere — the property two chunks meeting at a
        // boundary rely on without either knowing the other exists.
        Assert.Equal(generator.SurfaceHeightAt(100, -40), generator.SurfaceHeightAt(100, -40));

        // Distant columns, not adjacent ones. At 48 voxels of amplitude over a frequency of
        // 0.004, neighbouring columns routinely round to the same height — the terrain is
        // supposed to be smooth, and asserting otherwise tests the rounding, not the noise.
        Assert.NotEqual(generator.SurfaceHeightAt(100, -40), generator.SurfaceHeightAt(700, -40));
    }

    [Fact]
    public void ChunksFarAboveTheTerrainAreUniformAirAndAllocateNothing()
    {
        // The sky fast path. Most chunks in a world with sky above it take this route, and
        // it is the difference between an empty chunk costing one int and costing 8 KB.
        WorldGenerator generator = Generator();

        VoxelChunk sky = generator.GenerateTerrain(new ChunkPos(0, 40, 0));

        Assert.True(sky.IsUniform);
        Assert.Equal(MaterialIds.AirNumericId, sky.UniformValue);
        Assert.Equal(0, sky.EstimatedBytes);
    }

    [Fact]
    public void ChunksFarBelowTheTerrainCollapseToUniformStone()
    {
        // Not a special case in the generator — it fills the chunk and lets Compact notice.
        // Worth pinning anyway, because it is what stops deep chunks costing 8 KB each.
        WorldGenerator generator = Generator(caves: false);

        VoxelChunk deep = generator.GenerateTerrain(new ChunkPos(0, -40, 0));

        Assert.True(deep.IsUniform);
        Assert.Equal(Stone, deep.UniformValue);
    }

    [Fact]
    public void StrataAppearInOrderBelowTheSurface()
    {
        WorldGenerator generator = Generator(caves: false);

        // Find a column whose surface sits far enough inside a chunk that the strata below
        // it are in the same chunk. At the origin the surface happens to land exactly on a
        // chunk boundary, which puts the layers underneath in the chunk below — a real
        // situation the generator handles correctly, but not what this test is about.
        int lx = 0;
        int surface = 0;
        for (; lx < Size; lx++)
        {
            surface = generator.SurfaceHeightAt(lx, 0);
            int local = surface & VoxelConstants.ChunkMask;

            // Room for four layers below and one air voxel above, all inside this chunk.
            if (local is >= 5 and <= Size - 2)
            {
                break;
            }
        }

        Assert.True(lx < Size, "no suitable column found");

        int chunkY = surface >> VoxelConstants.ChunkSizeLog2;
        VoxelChunk chunk = generator.GenerateTerrain(new ChunkPos(0, chunkY, 0));
        int chunkMinY = chunkY << VoxelConstants.ChunkSizeLog2;

        Assert.Equal(MaterialIds.AirNumericId, Sample(chunk, lx, surface + 1, 0, chunkMinY));
        Assert.Equal(Grass, Sample(chunk, lx, surface, 0, chunkMinY));
        Assert.Equal(Dirt, Sample(chunk, lx, surface - 1, 0, chunkMinY));
        Assert.Equal(Dirt, Sample(chunk, lx, surface - 3, 0, chunkMinY));
        Assert.Equal(Stone, Sample(chunk, lx, surface - 4, 0, chunkMinY));
    }

    [Fact]
    public void CavesCarveAirOutOfSolidRock()
    {
        // Compare deep chunks with caves on and off. Two separate claims here, and they
        // need different scopes: "caves only ever remove material" must hold in *every*
        // chunk, but "caves exist" can only be asserted across a region. Thresholded fBm
        // clusters — at the default frequency a 32-voxel chunk spans less than one noise
        // lattice cell, so plenty of individual chunks legitimately contain no cave at all.
        WorldGenerator solidGen = Generator(caves: false);
        WorldGenerator carvedGen = Generator(caves: true);

        int totalRemoved = 0;
        int chunksWithCaves = 0;

        for (int cx = -2; cx <= 2; cx++)
        {
            for (int cz = -2; cz <= 2; cz++)
            {
                var position = new ChunkPos(cx, -3, cz);
                VoxelChunk solid = solidGen.GenerateTerrain(position);
                VoxelChunk carved = carvedGen.GenerateTerrain(position);

                int removedHere = 0;
                for (int i = 0; i < VoxelConstants.ChunkVolume; i++)
                {
                    int before = solid.GetAt(i);
                    int after = carved.GetAt(i);

                    if (before == MaterialIds.AirNumericId)
                    {
                        Assert.Equal(MaterialIds.AirNumericId, after);
                    }
                    else if (after == MaterialIds.AirNumericId)
                    {
                        removedHere++;
                    }
                    else
                    {
                        Assert.Equal(before, after);
                    }
                }

                totalRemoved += removedHere;
                if (removedHere > 0)
                {
                    chunksWithCaves++;
                }
            }
        }

        Assert.True(totalRemoved > 0, "caves were enabled but carved nothing across 25 chunks");
        Assert.True(chunksWithCaves < 25, "every single chunk had caves — threshold is too low");
    }

    [Fact]
    public void CavesDoNotBiteCratersOutOfTheSurface()
    {
        // Without a surface margin, cave noise crossing the threshold near the top of a
        // column removes the surface itself, which reads as damaged terrain rather than as
        // a cave mouth.
        WorldGenerator generator = Generator();

        for (int x = 0; x < 64; x++)
        {
            for (int z = 0; z < 64; z++)
            {
                int surface = generator.SurfaceHeightAt(x, z);
                Assert.False(
                    generator.IsCaveAt(x, surface, z, surface),
                    $"cave reached the surface at {x},{z}");
            }
        }
    }

    [Fact]
    public void DifferentSeedsProduceDifferentTerrain()
    {
        var position = new ChunkPos(0, 0, 0);
        VoxelChunk a = new WorldGenerator(Settings() with { Seed = 1 }, Strata()).GenerateTerrain(position);
        VoxelChunk b = new WorldGenerator(Settings() with { Seed = 2 }, Strata()).GenerateTerrain(position);

        Assert.True(DiffersSomewhere(a, b), "two seeds produced identical terrain");
    }

    [Fact]
    public void FeaturesStageIsAHookAndChangesNothingYet()
    {
        WorldGenerator generator = Generator();
        var position = new ChunkPos(1, 0, 1);

        VoxelChunk chunk = generator.GenerateTerrain(position);
        VoxelChunk reference = generator.GenerateTerrain(position);

        generator.ApplyFeatures(position, chunk);

        AssertSameContent(reference, chunk);
    }

    private static int Sample(VoxelChunk chunk, int lx, int worldY, int lz, int chunkMinY) =>
        chunk.Get(lx, worldY - chunkMinY, lz);

    private static void AssertColumnMatchesSurface(
        WorldGenerator generator, VoxelChunk chunk, int lx, int lz, int worldX, int chunkY)
    {
        int chunkMinY = chunkY << VoxelConstants.ChunkSizeLog2;
        int surface = generator.SurfaceHeightAt(worldX, lz);

        // Only meaningful where the surface actually falls inside this chunk.
        if (surface < chunkMinY || surface >= chunkMinY + Size)
        {
            return;
        }

        Assert.NotEqual(MaterialIds.AirNumericId, chunk.Get(lx, surface - chunkMinY, lz));
        if (surface - chunkMinY + 1 < Size)
        {
            Assert.Equal(MaterialIds.AirNumericId, chunk.Get(lx, surface - chunkMinY + 1, lz));
        }
    }

    private static void AssertSameContent(VoxelChunk expected, VoxelChunk actual)
    {
        for (int i = 0; i < VoxelConstants.ChunkVolume; i++)
        {
            if (expected.GetAt(i) != actual.GetAt(i))
            {
                (int x, int y, int z) = VoxelPos.FromIndex(i);
                Assert.Fail($"differs at local ({x},{y},{z}): {expected.GetAt(i)} vs {actual.GetAt(i)}");
            }
        }
    }

    private static bool DiffersSomewhere(VoxelChunk a, VoxelChunk b)
    {
        for (int i = 0; i < VoxelConstants.ChunkVolume; i++)
        {
            if (a.GetAt(i) != b.GetAt(i))
            {
                return true;
            }
        }

        return false;
    }
}

public class StrataTableTests
{
    [Fact]
    public void DepthZeroIsTheTopmostLayer()
    {
        var table = new StrataTable([new StrataLayer(1, 1), new StrataLayer(2, 3)], bottomMaterialId: 3);

        Assert.Equal(1, table.MaterialAtDepth(0));
        Assert.Equal(2, table.MaterialAtDepth(1));
        Assert.Equal(2, table.MaterialAtDepth(3));
        Assert.Equal(3, table.MaterialAtDepth(4));
        Assert.Equal(4, table.DeclaredDepth);
    }

    [Fact]
    public void BeyondTheDeclaredLayersTheBottomMaterialContinuesForever()
    {
        var table = new StrataTable([new StrataLayer(1, 2)], bottomMaterialId: 9);

        Assert.Equal(9, table.MaterialAtDepth(2));
        Assert.Equal(9, table.MaterialAtDepth(1_000_000));
    }

    [Fact]
    public void NegativeDepthFallsBackToTheBottomMaterialRatherThanCrashing()
    {
        // Reached only through a bug, but a generator that throws mid-chunk on a worker
        // thread is worse than one that produces a visibly wrong voxel.
        var table = new StrataTable([new StrataLayer(1, 2)], bottomMaterialId: 9);

        Assert.Equal(9, table.MaterialAtDepth(-1));
    }

    [Fact]
    public void AnEmptyTableIsAllBottomMaterial()
    {
        var table = new StrataTable([], bottomMaterialId: 7);

        Assert.Equal(0, table.DeclaredDepth);
        Assert.Equal(7, table.MaterialAtDepth(0));
    }
}
