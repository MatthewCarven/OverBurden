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

    /// <summary>
    /// The one test that reads <see cref="WorldGenSettings"/>'s own defaults rather than stating
    /// its terrain, because those defaults are what the game ships.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Every other fixture in this repo pins the shape it needs, and after 2026-09-23 that
    /// includes the pipeline harness — so without this, nothing anywhere would notice the shipped
    /// world becoming a flat plain or losing its caves.</b> That is the honest cost of pinning
    /// fixtures: it buys immunity from tuning, and it has to be paid back by one test that is
    /// deliberately not immune.
    /// </para>
    /// <para>
    /// <b>Bands rather than numbers, over several seeds, because this must survive the tuning it
    /// exists to guard.</b> It asserts the three things that make a world playable rather than a
    /// parameter set — there is relief to walk over, there is a surface with air above it and
    /// rock below, and there is cave void underground to find — and it should pass for any
    /// setting a person would plausibly ship. A failure here means a knob went somewhere nobody
    /// would want to play, not that a landscape moved.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(0x5EEDUL)]
    [InlineData(0x4D494E455243UL)]
    [InlineData(690930704UL)]
    public void TheShippedDefaultsMakeAWorldYouCanStandOnAndDigInto(ulong seed)
    {
        var defaults = new WorldGenSettings { Seed = seed };
        var generator = new WorldGenerator(defaults, Strata());

        // Relief, over a span wide enough to cross several noise lattice cells.
        int lowest = int.MaxValue;
        int highest = int.MinValue;
        for (int z = -256; z <= 256; z += 8)
        {
            for (int x = -256; x <= 256; x += 8)
            {
                int height = generator.SurfaceHeightAt(x, z);
                lowest = Math.Min(lowest, height);
                highest = Math.Max(highest, height);
            }
        }

        int relief = highest - lowest;
        Assert.True(
            relief >= 8,
            $"the shipped world is a plain: {relief} voxels of relief over 512 m, heights "
            + $"{lowest}..{highest}. A world with nothing to walk over is a settings mistake, "
            + "not a landscape.");

        // Something to stand on: the chunk holding the surface has both rock and sky in it.
        int surface = generator.SurfaceHeightAt(0, 0);
        VoxelChunk band = generator.GenerateTerrain(
            new ChunkPos(0, surface >> VoxelConstants.ChunkSizeLog2, 0));

        Assert.False(
            band.IsUniform,
            $"the chunk holding the surface at y {surface} came out uniform — there is no ground "
            + "to stand on where the player spawns.");

        // Something to dig into: cave void well below the surface margin, and not so much that
        // the rock is foam.
        // Spread over a kilometre rather than one hillside. The first version of this probed a
        // 128 m square and read 0.16% under a tall-and-lumpy arm — not because that world has no
        // caves, but because one mountain's worth of the field happened to sit under the
        // threshold. A share is a claim about a world; sample it like one.
        int probed = 0;
        int carved = 0;
        for (int z = -512; z <= 512; z += 16)
        {
            for (int x = -512; x <= 512; x += 16)
            {
                int top = generator.SurfaceHeightAt(x, z);
                for (int y = top - 64; y <= top - 16; y += 4)
                {
                    probed++;
                    if (generator.IsCaveAt(x, y, z, top))
                    {
                        carved++;
                    }
                }
            }
        }

        double share = (double)carved / probed;
        Assert.True(
            share is >= 0.002 and <= 0.30,
            $"underground is {share:P2} cave void over {probed} samples in the 64 m below the "
            + "surface margin. Outside 0.2%..30% the rock is either solid or foam — either way "
            + "nobody is digging through it for fun.");
    }


    // -------------------------------------------------------------------------------------
    // Regions. A second, much slower noise field multiplies the surface amplitude per column,
    // so a world has mountains in one place and plains in another rather than one lumpiness
    // everywhere. Neutral by default, because every recorded figure in this project is a
    // statement about the landscape the defaults produce.
    // -------------------------------------------------------------------------------------

    [Fact]
    public void ReliefVariationIsNeutralByDefault()
    {
        // The test that protects every measurement in the repo. The oracle is the formula spelled
        // out here rather than a comparison against another generator, because two generators
        // both consulting a relief field would agree with each other perfectly while the ground
        // had moved under the r6 control, the keystone's at-risk count and the profile seed's
        // dry first forty seconds.
        var defaults = new WorldGenSettings { Seed = 0x5EEDUL };
        Assert.Equal(0.0f, defaults.ReliefVariation);

        var generator = new WorldGenerator(defaults, Strata());

        for (int z = -192; z <= 192; z += 24)
        {
            for (int x = -192; x <= 192; x += 24)
            {
                float noise = GradientNoise.Fbm2D(
                    Hash64.Mix(defaults.Seed ^ 0x5EA51DE0UL),
                    x * defaults.SurfaceFrequency,
                    z * defaults.SurfaceFrequency,
                    defaults.SurfaceOctaves,
                    defaults.Lacunarity,
                    defaults.Gain);

                int expected = defaults.SeaLevelVoxels
                    + (int)MathF.Round(noise * defaults.SurfaceAmplitudeVoxels);

                Assert.Equal(expected, generator.SurfaceHeightAt(x, z));
                Assert.Equal(1.0f, generator.ReliefFactorAt(x, z));
            }
        }
    }

    [Fact]
    public void ReliefVariationGivesAWorldSomewhereFlatAndSomewhereMountainous()
    {
        // What "regions" has to mean to be worth the field: not that heights differ — one noise
        // field already does that — but that *how much they differ* differs from place to place.
        // So this measures local relief in windows and asks whether the calmest window and the
        // roughest one are a different kind of place.
        //
        // **The window and the span are derived from the region's own wavelength**, not written
        // as metres, because the first version of this test fixed them at 128 m over a 1 km span
        // and measured a relief field whose period was 500 m — every window straddled a peak and
        // a trough, so it reported 2.7x and read as a failure of the feature when it was a
        // failure of the sample. A window has to be small against a region and the span has to
        // cover several of them. That measurement is what moved ReliefFrequencyRatio to 32.
        var settings = Settings() with { ReliefVariation = 0.75f };
        var generator = new WorldGenerator(settings, Strata());

        int period = (int)(WorldGenerator.ReliefFrequencyRatio / settings.SurfaceFrequency);
        int window = period / 16;
        int step = period / 4;
        int span = period * 4;

        int calmest = int.MaxValue;
        int roughest = 0;

        for (int originZ = -span; originZ < span; originZ += step)
        {
            for (int originX = -span; originX < span; originX += step)
            {
                int low = int.MaxValue;
                int high = int.MinValue;

                for (int z = originZ; z < originZ + window; z += 16)
                {
                    for (int x = originX; x < originX + window; x += 16)
                    {
                        int height = generator.SurfaceHeightAt(x, z);
                        low = Math.Min(low, height);
                        high = Math.Max(high, height);
                    }
                }

                calmest = Math.Min(calmest, high - low);
                roughest = Math.Max(roughest, high - low);
            }
        }

        Assert.True(
            roughest >= calmest * 4,
            $"over {window} voxels the roughest place in this world has {roughest} voxels of "
            + $"relief and the calmest {calmest} — under four times, this reads as one landscape "
            + "rather than as somewhere to walk to.");
    }

    [Fact]
    public void TheReliefFieldNeverInvertsTheSurface()
    {
        // At variation 1 the factor runs to the edges of [0, 2], and Sample2D is documented as
        // only *roughly* in [-1, 1] — so without the clamp a factor could go negative, which does
        // not read as flat. It mirrors the surface: a valley where the neighbouring region has a
        // hill, with a cliff along the join. The clamp is what this is standing over.
        var generator = new WorldGenerator(Settings() with { ReliefVariation = 1.0f }, Strata());

        float lowest = float.MaxValue;
        float highest = float.MinValue;

        for (int z = -4096; z <= 4096; z += 64)
        {
            for (int x = -4096; x <= 4096; x += 64)
            {
                float factor = generator.ReliefFactorAt(x, z);
                lowest = MathF.Min(lowest, factor);
                highest = MathF.Max(highest, factor);
            }
        }

        Assert.True(lowest >= 0.0f, $"the relief factor reached {lowest}, which mirrors the terrain");
        Assert.True(highest <= 2.0f, $"the relief factor reached {highest}, past twice the amplitude");
    }

    [Fact]
    public void RegionsDoNotShowUpAsASeamBetweenChunks()
    {
        // The seam test again with the relief field on. It holds by construction — the field
        // reads the same two world coordinates the surface does — and it is pinned because the
        // way to break it is to reach for the chunk you are in, which is exactly the temptation
        // a regional feature carries. Invisible until it is in a screenshot.
        var generator = new WorldGenerator(
            Settings(caves: false) with { ReliefVariation = 0.8f },
            Strata());

        VoxelChunk left = generator.GenerateTerrain(new ChunkPos(0, 0, 0));
        VoxelChunk right = generator.GenerateTerrain(new ChunkPos(1, 0, 0));

        for (int lz = 0; lz < Size; lz++)
        {
            AssertColumnMatchesSurface(generator, left, lx: Size - 1, lz, worldX: Size - 1, chunkY: 0);
            AssertColumnMatchesSurface(generator, right, lx: 0, lz, worldX: Size, chunkY: 0);
        }
    }

    [Fact]
    public void RegionsSurviveTheOrderChunksAreGeneratedIn()
    {
        // The relief field is a second thing SurfaceHeightAt consults, so it is a second thing
        // that could become a buffer reused across a chunk. It is not one today; this is what
        // notices if it becomes one.
        var settings = Settings() with { ReliefVariation = 0.8f };
        ChunkPos[] positions =
        [
            new(0, 0, 0), new(3, 0, -2), new(-1, 0, 1), new(7, 0, 5),
        ];

        var forwards = new WorldGenerator(settings, Strata());
        var backwards = new WorldGenerator(settings, Strata());

        var expected = positions.Select(forwards.GenerateTerrain).ToArray();
        VoxelChunk[] reversed = positions.Reverse()
            .Select(backwards.GenerateTerrain)
            .Reverse()
            .ToArray();

        for (int i = 0; i < positions.Length; i++)
        {
            AssertSameContent(expected[i], reversed[i]);
        }
    }

    [Theory]
    [InlineData(-0.001f)]
    [InlineData(1.001f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void AReliefVariationOutsideItsRangeRefusesToConstruct(float variation)
    {
        // NaN is the one that matters and the one a range check written the obvious way lets
        // through: every comparison with NaN is false, so `variation > 1` says no. M3c's NaN
        // hardness threshold, which inverted a gate from "takes nothing" to "takes everything".
        var error = Assert.Throws<ArgumentOutOfRangeException>(
            () => new WorldGenerator(Settings() with { ReliefVariation = variation }, Strata()));

        Assert.Contains("ReliefVariation", error.Message);
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
