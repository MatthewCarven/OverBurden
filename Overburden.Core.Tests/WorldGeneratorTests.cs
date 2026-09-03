using Overburden.Core.Content;
using Overburden.Core.Voxels;
using Overburden.Core.Worldgen;

namespace Overburden.Core.Tests;

public class WorldGeneratorTests
{
    private const int Grass = 1;
    private const int Dirt = 2;
    private const int Stone = 3;
    private const int Size = VoxelConstants.ChunkSize;

    private static StrataTable Strata() =>
        new([new StrataLayer(Grass, 1), new StrataLayer(Dirt, 3)], Stone);

    private static WorldGenSettings Settings(bool caves = true, int latticeStep = 4) => new()
    {
        Seed = 0x5EEDUL,
        SeaLevelVoxels = 0,
        SurfaceAmplitudeVoxels = 48.0f,
        SurfaceFrequency = 0.004f,
        SurfaceOctaves = 4,
        CavesEnabled = caves,
        CaveLatticeStep = latticeStep,
    };

    private static WorldGenerator Generator(bool caves = true, int latticeStep = 4) =>
        new(Settings(caves, latticeStep), Strata());

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

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(64)]
    public void ALatticeStepThatDoesNotDivideTheChunkRefusesToConstruct(int step)
    {
        // A step that does not divide the chunk size leaves chunk boundaries between lattice
        // points, so the per-chunk lattice stops coinciding with the global one and two
        // neighbours interpolate from different corners. Nothing throws at the time and the
        // damage is a crack in the terrain, so the constructor is the only place to catch it.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new WorldGenerator(Settings(latticeStep: step), Strata()));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void ACoarseLatticeIsExactOnItsOwnPoints(int step)
    {
        // Every step samples the same underlying field, so a coarse lattice must agree with
        // per-voxel evaluation exactly wherever it has a sample — no tolerance, the same
        // fBm call with the same arguments. This is what makes chunk boundaries seamless:
        // they are multiples of 32, therefore lattice points at every legal step.
        var reference = new WorldGenerator(Settings(latticeStep: 1), Strata());
        var coarse = new WorldGenerator(Settings(latticeStep: step), Strata());

        var seen = new HashSet<float>();

        for (int i = -3; i <= 3; i++)
        {
            for (int j = -3; j <= 3; j++)
            {
                int x = i * step;
                int y = j * step;
                int z = (i - j) * step;

                float expected = reference.CaveNoiseAt(x, y, z);
                Assert.Equal(expected, coarse.CaveNoiseAt(x, y, z));
                seen.Add(expected);
            }
        }

        Assert.True(seen.Count > 1, "the field returned one constant, so agreeing about it proves nothing");
    }

    [Fact]
    public void IsCaveAtAgreesWithTheChunkItGenerated()
    {
        // One definition of the cave field, not two. GenerateTerrain reads a buffered
        // lattice and IsCaveAt resamples eight fresh corners; the moment those disagree, a
        // query says "rock" where the player is standing in open air. Negative chunk
        // coordinates are in here on purpose — an index or a floor-division bug agrees
        // happily around the origin and drifts everywhere else.
        WorldGenerator carved = Generator(caves: true);
        WorldGenerator solid = Generator(caves: false);

        ChunkPos[] positions = [new(0, -3, 0), new(-1, -3, 2), new(3, -4, -5), new(-7, -2, -9)];
        int carvedVoxels = 0;

        foreach (ChunkPos position in positions)
        {
            VoxelChunk withCaves = carved.GenerateTerrain(position);
            VoxelChunk withoutCaves = solid.GenerateTerrain(position);
            int chunkMinY = position.Y << VoxelConstants.ChunkSizeLog2;
            int originX = position.X << VoxelConstants.ChunkSizeLog2;
            int originZ = position.Z << VoxelConstants.ChunkSizeLog2;

            for (int lz = 0; lz < Size; lz++)
            {
                for (int lx = 0; lx < Size; lx++)
                {
                    int surface = carved.SurfaceHeightAt(originX + lx, originZ + lz);

                    for (int ly = 0; ly < Size; ly++)
                    {
                        // Only voxels that would have been solid without caves say anything
                        // about caves; the rest is sky either way.
                        if (withoutCaves.Get(lx, ly, lz) == MaterialIds.AirNumericId)
                        {
                            continue;
                        }

                        bool carvedHere = withCaves.Get(lx, ly, lz) == MaterialIds.AirNumericId;
                        bool queried = carved.IsCaveAt(
                            originX + lx, chunkMinY + ly, originZ + lz, surface);

                        Assert.Equal(carvedHere, queried);

                        if (carvedHere)
                        {
                            carvedVoxels++;
                        }
                    }
                }
            }
        }

        Assert.True(carvedVoxels > 0, "nothing was carved, so agreeing about caves proves nothing");
    }

    [Fact]
    public void CoarseningTheLatticeBarelyChangesTheTerrain()
    {
        // The accuracy half of M2d-2, pinned so a later change to the noise or the step
        // cannot quietly wreck it. These chunks sit well below the surface, so every voxel
        // compared is underground and cave-eligible rather than sky agreeing with sky.
        // The bound is deliberately loose against the measured figure in WORKLOG.md: this
        // is a regression alarm, not the measurement.
        var exact = new WorldGenerator(Settings(latticeStep: 1), Strata());
        var coarse = new WorldGenerator(Settings(latticeStep: 4), Strata());

        int compared = 0;
        int differing = 0;

        for (int cx = -1; cx <= 1; cx++)
        {
            for (int cz = -1; cz <= 1; cz++)
            {
                var position = new ChunkPos(cx, -3, cz);
                VoxelChunk a = exact.GenerateTerrain(position);
                VoxelChunk b = coarse.GenerateTerrain(position);

                for (int i = 0; i < VoxelConstants.ChunkVolume; i++)
                {
                    compared++;
                    if (a.GetAt(i) != b.GetAt(i))
                    {
                        differing++;
                    }
                }
            }
        }

        double fraction = (double)differing / compared;
        Assert.True(fraction < 0.02, $"step 4 moved {fraction:P2} of voxels, expected well under 2%");
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
