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
    // Regions. A much slower second noise field decides how far between a floor and a ceiling
    // each column's amplitude sits, so a world has plains in one place and mountains in
    // another. Uniform by default, because every recorded figure in this project is a
    // statement about the landscape the defaults produce.
    //
    // This replaced a multiplier centred on 1 (header v6, alive for one day). The tests that
    // matter here are the two that killed it: something has to be flat, and something has to
    // be mountainous, in the same world.
    // -------------------------------------------------------------------------------------

    /// <summary>The shipped shape for a new world: a low floor, a high ceiling, mountains uncommon.</summary>
    private static WorldGenSettings Shaped(float baseMul = 0.4f, float peakMul = 3.0f, float rarity = 0.25f) =>
        Settings() with
        {
            SurfaceAmplitudeVoxels = 48.0f * peakMul,
            ReliefBaseVoxels = 48.0f * baseMul,
            ReliefRarity = rarity,
        };

    [Fact]
    public void ReliefIsUniformByDefault()
    {
        // The test that protects every measurement in the repo. The oracle is the formula spelled
        // out here rather than a comparison against another generator, because two generators
        // both consulting a relief field would agree with each other perfectly while the ground
        // had moved under the r6 control, the keystone's at-risk count and the profile seed's
        // dry first forty seconds.
        var defaults = new WorldGenSettings { Seed = 0x5EEDUL };
        Assert.Equal(defaults.SurfaceAmplitudeVoxels, defaults.ReliefBaseVoxels);
        Assert.Equal(0.0f, defaults.ReliefRarity);

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
                Assert.Equal(defaults.SurfaceAmplitudeVoxels, generator.SurfaceAmplitudeAt(x, z));
            }
        }
    }

    [Fact]
    public void TwoWorldsDoNotLookTheSameWhereEveryoneStarts()
    {
        // The defect this caught, in a sentence Matthew found by playing: several new worlds in a
        // row felt alike, and the reason was that they were -- at spawn, exactly.
        //
        // Gradient noise is *exactly zero* at its lattice points and world origin is one, so
        // before the offset the relief sample at (0, 0) was 0 whatever the seed, and four worlds
        // on four seeds all read an amplitude at spawn of 34.6 voxels to the digit. The one place
        // a player judges a world from was the one place that could not differ.
        //
        // Measured with the offset in: 30 distinct values of 32, spanning 81% of the floor-to-
        // ceiling range, from a genuine plain to near the summit. The bounds here are well under
        // that, because this is standing over "the origin is special" and not over the hash.
        var spawns = new List<float>();
        for (ulong seed = 1; seed <= 32; seed++)
        {
            spawns.Add(new WorldGenerator(Shaped() with { Seed = seed }, Strata())
                .SurfaceAmplitudeAt(0, 0));
        }

        WorldGenSettings shape = Shaped();
        float span = shape.SurfaceAmplitudeVoxels - shape.ReliefBaseVoxels;
        float spread = spawns.Max() - spawns.Min();

        Assert.True(
            spawns.Distinct().Count() >= 24,
            $"only {spawns.Distinct().Count()} of 32 seeds start somewhere different — if this is "
            + "1, the origin is sitting on the relief field's lattice point again.");

        Assert.True(
            spread >= span * 0.5f,
            $"spawn amplitudes span {spread:0.0} voxels of a possible {span:0.0} — new worlds all "
            + "start in the same kind of place.");
    }

    [Fact]
    public void AShapedWorldHasSomewhereFlatAndSomewhereMountainous()
    {
        // Matthew's sentence as a test: "I kind of want them somewhere but not everywhere."
        //
        // **This is the test the multiplier could not pass at any setting**, which is why the
        // shape changed. Under `amplitude x (1 + vary x n)` the flattest 200 m window in 32 km
        // still had 21.8 m of relief at --relief=3 --reliefvary=0.5, and 15.0 m at vary=1,
        // because Perlin noise rarely reaches its extremes so the factor sits near 1 almost
        // everywhere. A floor and a ceiling cannot have that problem.
        //
        // The window and the span are derived from the region's own wavelength, not written as
        // metres: an earlier version fixed them at 128 m over a 1 km span and measured a field
        // whose period was 500 m, so every window straddled a peak and a trough and the sample
        // could not have seen the effect whatever the setting was.
        var settings = Shaped();
        var generator = new WorldGenerator(settings, Strata());

        int period = (int)(WorldGenerator.ReliefFrequencyRatio / settings.SurfaceFrequency);
        int window = period / 16;
        int step = period / 8;
        int span = period * 4;

        int flattest = int.MaxValue;
        int tallest = 0;

        for (int originZ = -span; originZ < span; originZ += step)
        {
            for (int originX = -span; originX < span; originX += step)
            {
                int relief = ReliefWithin(generator, originX, originZ, window);
                flattest = Math.Min(flattest, relief);
                tallest = Math.Max(tallest, relief);
            }
        }

        // Somewhere genuinely flat: under 8 m of relief across 200 m of ground.
        Assert.True(
            flattest < 32,
            $"the flattest place in this world has {flattest} voxels ({flattest * 0.25:0.0} m) of "
            + "relief over a 200 m window — nowhere is actually flat, which is the defect the "
            + "floor exists to prevent.");

        // And somewhere that is not: over 20 m.
        Assert.True(
            tallest > 80,
            $"the tallest place in this world has {tallest} voxels ({tallest * 0.25:0.0} m) of "
            + "relief — the ceiling never gets reached, so the mountains are not there.");
    }

    [Theory]
    [InlineData(0.0f, 0.25f)]
    [InlineData(0.25f, 0.5f)]
    [InlineData(0.5f, 0.75f)]
    [InlineData(0.75f, 1.0f)]
    public void RaisingTheRarityLeavesFewerMountains(float commoner, float rarer)
    {
        // The dial's own property, and the only one worth asserting about it: whichever way the
        // threshold and the ramp are arranged inside, more rarity must mean less mountain. The
        // arms share a seed and a span, so nothing but the dial differs.
        Assert.True(MountainousShare(commoner) > MountainousShare(rarer));
    }

    [Fact]
    public void TheRarestSettingStillLeavesTheSummitsReachable()
    {
        // The trap this shape exists to avoid, as a test. The obvious mask -- max(0, n)^k --
        // puts the threshold outside the noise field's own range once k is 2 or more, because 2D
        // gradient noise spans about +-0.7 rather than +-1. The sweep then reads "0% hilly",
        // which looks exactly like a plausible rarity setting and is in fact the feature being
        // switched off. At rarity 1 mountains must be rare; they must not be absent.
        Assert.True(MountainousShare(1.0f) > 0.0);
    }

    [Fact]
    public void TheAmplitudeStaysBetweenTheFloorAndTheCeiling()
    {
        // Sample2D is documented as only *roughly* in [-1, 1], so the clamp inside is what keeps
        // this true. Without it a column could be handed an amplitude outside the pair the caller
        // asked for -- and below the floor means below zero, which mirrors the surface: a valley
        // where the neighbouring region has a hill, with a cliff along the join.
        var settings = Shaped(baseMul: 0.1f, peakMul: 6.0f, rarity: 0.0f);
        var generator = new WorldGenerator(settings, Strata());

        float lowest = float.MaxValue;
        float highest = float.MinValue;

        for (int z = -8192; z <= 8192; z += 64)
        {
            for (int x = -8192; x <= 8192; x += 64)
            {
                float amplitude = generator.SurfaceAmplitudeAt(x, z);
                lowest = MathF.Min(lowest, amplitude);
                highest = MathF.Max(highest, amplitude);
            }
        }

        Assert.True(lowest >= settings.ReliefBaseVoxels, $"amplitude reached {lowest}, under the floor");
        Assert.True(highest <= settings.SurfaceAmplitudeVoxels, $"amplitude reached {highest}, over the ceiling");
    }

    [Fact]
    public void RegionsDoNotShowUpAsASeamBetweenChunks()
    {
        // The seam test again with the relief field on. It holds by construction -- the field
        // reads the same two world coordinates the surface does -- and it is pinned because the
        // way to break it is to reach for the chunk you are in, which is exactly the temptation
        // a regional feature carries. Invisible until it is in a screenshot.
        var generator = new WorldGenerator(
            Shaped() with { CavesEnabled = false },
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
        WorldGenSettings settings = Shaped();
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
    public void AReliefRarityOutsideItsRangeRefusesToConstruct(float rarity)
    {
        // NaN is the one that matters and the one a range check written the obvious way lets
        // through: every comparison with NaN is false, so `rarity > 1` says no. M3c's NaN
        // hardness threshold, which inverted a gate from "takes nothing" to "takes everything".
        var error = Assert.Throws<ArgumentOutOfRangeException>(
            () => new WorldGenerator(Settings() with { ReliefRarity = rarity }, Strata()));

        Assert.Contains(nameof(WorldGenSettings.ReliefRarity), error.Message);
    }

    [Theory]
    [InlineData(-1.0f)]
    [InlineData(float.NaN)]
    [InlineData(48.001f)]
    [InlineData(float.PositiveInfinity)]
    public void AFloorThatIsNotUnderItsOwnCeilingRefusesToConstruct(float floor)
    {
        // A floor above the ceiling is not a world with inverted mountains -- the ramp would run
        // backwards and the flat places would be the tall ones -- it is the two being passed the
        // wrong way round. Refused rather than sorted, because sorting them hides the mistake.
        var error = Assert.Throws<ArgumentOutOfRangeException>(
            () => new WorldGenerator(Settings() with { ReliefBaseVoxels = floor }, Strata()));

        Assert.Contains(nameof(WorldGenSettings.ReliefBaseVoxels), error.Message);
    }

    /// <summary>What share of 200 m windows over several regions are mountainous, at one rarity.</summary>
    private static double MountainousShare(float rarity)
    {
        WorldGenSettings settings = Shaped(rarity: rarity);
        var generator = new WorldGenerator(settings, Strata());

        int period = (int)(WorldGenerator.ReliefFrequencyRatio / settings.SurfaceFrequency);
        int window = period / 16;
        int step = period / 8;
        int span = period * 4;

        int windows = 0;
        int mountainous = 0;

        for (int originZ = -span; originZ < span; originZ += step)
        {
            for (int originX = -span; originX < span; originX += step)
            {
                windows++;
                if (ReliefWithin(generator, originX, originZ, window) >= 80)
                {
                    mountainous++;
                }
            }
        }

        return (double)mountainous / windows;
    }

    /// <summary>Highest minus lowest surface, in voxels, over a square of that many voxels.</summary>
    private static int ReliefWithin(WorldGenerator generator, int originX, int originZ, int size)
    {
        int low = int.MaxValue;
        int high = int.MinValue;

        for (int z = originZ; z < originZ + size; z += 16)
        {
            for (int x = originX; x < originX + size; x += 16)
            {
                int height = generator.SurfaceHeightAt(x, z);
                low = Math.Min(low, height);
                high = Math.Max(high, height);
            }
        }

        return high - low;
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
