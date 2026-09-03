using Overburden.Core.Worldgen;

namespace Overburden.Core.Tests;

public class Hash64Tests
{
    /// <summary>
    /// The guard against the whole world quietly changing shape between launches.
    /// </summary>
    /// <remarks>
    /// If someone swaps the arithmetic in <see cref="Hash64"/> for
    /// <c>HashCode.Combine</c> — which is the obvious, tempting, and completely wrong
    /// thing to reach for — these constants stop matching, and they stop matching
    /// *differently on every run*, because .NET randomises that seed per process. Without a
    /// pinned expectation the bug is invisible inside any single session and only shows up
    /// as a player's world having changed since yesterday.
    /// </remarks>
    [Fact]
    public void KnownInputsHashToKnownOutputs()
    {
        // 0xE220A8397B1DCDAF is the published first output of splitmix64 from a zero seed,
        // so this doubles as a check that the mixer is the real algorithm and not a
        // plausible-looking rearrangement of it.
        Assert.Equal(0xE220A8397B1DCDAFUL, Hash64.Mix(0UL));
        Assert.Equal(0x910A2DEC89025CC1UL, Hash64.Mix(1UL));
        Assert.Equal(0x5CC60547776902BAUL, Hash64.Combine(0UL, 0, 0, 0));
        Assert.Equal(0x1BA5DF67C6AB18A4UL, Hash64.Combine(1234UL, 5, -6, 7));        
    }

    [Fact]
    public void DistinctCoordinatesHashDistinctly()
    {
        var seen = new HashSet<ulong>();
        for (int x = -8; x <= 8; x++)
        {
            for (int y = -8; y <= 8; y++)
            {
                for (int z = -8; z <= 8; z++)
                {
                    Assert.True(seen.Add(Hash64.Combine(99UL, x, y, z)), $"collision at {x},{y},{z}");
                }
            }
        }
    }

    [Fact]
    public void NegativeCoordinatesAreAsWellDistributedAsPositiveOnes()
    {
        // Casting through uint rather than sign-extending matters here: sign extension
        // fills the high bits with ones for every negative input, and the negative half of
        // the world ends up correlated with itself.
        Assert.NotEqual(Hash64.Combine(0UL, -1, 0, 0), Hash64.Combine(0UL, 1, 0, 0));
        Assert.NotEqual(Hash64.Combine(0UL, 0, -1, 0), Hash64.Combine(0UL, 0, 1, 0));
        Assert.NotEqual(Hash64.Combine(0UL, -1, -1, -1), Hash64.Combine(0UL, 1, 1, 1));
    }

    [Fact]
    public void CoordinateOrderMatters()
    {
        // A hash that merely sums its inputs would return the same value for all of these,
        // and the world would be symmetric across its diagonals.
        Assert.NotEqual(Hash64.Combine(0UL, 1, 2, 3), Hash64.Combine(0UL, 3, 2, 1));
        Assert.NotEqual(Hash64.Combine(0UL, 1, 2, 3), Hash64.Combine(0UL, 2, 1, 3));
    }

    [Fact]
    public void DifferentSeedsGiveDifferentWorlds()
    {
        Assert.NotEqual(Hash64.Combine(1UL, 4, 5, 6), Hash64.Combine(2UL, 4, 5, 6));
    }

    [Fact]
    public void UnitFloatStaysInTheHalfOpenUnitRange()
    {
        for (ulong i = 0; i < 4096; i++)
        {
            float value = Hash64.ToUnitFloat(Hash64.Mix(i));
            Assert.InRange(value, 0.0f, 0.99999994f);
        }

        // The top of the range must not round up to exactly 1.0, or callers indexing a
        // table by `(int)(u * length)` walk off the end.
        Assert.True(Hash64.ToUnitFloat(ulong.MaxValue) < 1.0f);
    }

    [Fact]
    public void SignedFloatStaysWithinMinusOneToOne()
    {
        for (ulong i = 0; i < 4096; i++)
        {
            Assert.InRange(Hash64.ToSignedFloat(Hash64.Mix(i)), -1.0f, 1.0f);
        }
    }
}

public class GradientNoiseTests
{
    private const ulong Seed = 0xABCDEF01UL;

    [Fact]
    public void SamplingIsDeterministic()
    {
        Assert.Equal(GradientNoise.Sample2D(Seed, 1.25f, -3.5f), GradientNoise.Sample2D(Seed, 1.25f, -3.5f));
        Assert.Equal(
            GradientNoise.Sample3D(Seed, 1.25f, -3.5f, 9.75f),
            GradientNoise.Sample3D(Seed, 1.25f, -3.5f, 9.75f));
    }

    [Fact]
    public void GradientNoiseIsZeroAtLatticePoints()
    {
        // The defining property, and what distinguishes this from value noise: the extremes
        // live between the lattice points, so the lattice does not show up as an
        // axis-aligned grid of hills on the heightmap.
        for (int x = -4; x <= 4; x++)
        {
            for (int y = -4; y <= 4; y++)
            {
                Assert.Equal(0.0f, GradientNoise.Sample2D(Seed, x, y), 5);
            }
        }
    }

    [Fact]
    public void OutputIsNotConstant()
    {
        var values = new HashSet<float>();
        for (int i = 0; i < 200; i++)
        {
            values.Add(GradientNoise.Sample2D(Seed, i * 0.37f, i * 0.11f));
        }

        Assert.True(values.Count > 100, $"noise looks degenerate: only {values.Count} distinct values");
    }

    [Fact]
    public void NoiseIsContinuous()
    {
        // Neighbouring samples must not jump. A discontinuity here becomes a cliff wall
        // running along a lattice boundary across the whole world.
        float previous = GradientNoise.Sample2D(Seed, 0.0f, 0.5f);
        for (int i = 1; i <= 400; i++)
        {
            float current = GradientNoise.Sample2D(Seed, i * 0.01f, 0.5f);
            Assert.True(Math.Abs(current - previous) < 0.2f, $"jump of {current - previous:F3} at step {i}");
            previous = current;
        }
    }

    [Fact]
    public void NegativeCoordinatesAreNotAMirrorOfPositiveOnes()
    {
        // Truncating instead of flooring makes the cell at -0.5 and +0.5 the same cell,
        // which mirrors the entire world across the origin.
        Assert.NotEqual(
            GradientNoise.Sample2D(Seed, -0.5f, 0.25f),
            GradientNoise.Sample2D(Seed, 0.5f, 0.25f));
    }

    [Fact]
    public void FbmStaysInRangeAndVaries()
    {
        float min = float.MaxValue;
        float max = float.MinValue;

        for (int i = 0; i < 2000; i++)
        {
            float value = GradientNoise.Fbm2D(Seed, i * 0.013f, i * 0.007f, octaves: 4, lacunarity: 2.0f, gain: 0.5f);
            min = Math.Min(min, value);
            max = Math.Max(max, value);
        }

        Assert.InRange(min, -1.5f, 0.0f);
        Assert.InRange(max, 0.0f, 1.5f);
        Assert.True(max - min > 0.3f, "fbm output barely varies");
    }

    [Fact]
    public void MoreOctavesStillProducesBoundedOutput()
    {
        for (int octaves = 1; octaves <= 8; octaves++)
        {
            float value = GradientNoise.Fbm2D(Seed, 12.3f, 45.6f, octaves, 2.0f, 0.5f);
            Assert.InRange(value, -1.5f, 1.5f);
        }
    }
}
