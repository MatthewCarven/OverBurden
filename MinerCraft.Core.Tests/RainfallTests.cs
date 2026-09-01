using MinerCraft.Core.Hydrology;

namespace MinerCraft.Core.Tests;

/// <summary>
/// The clock, the weather and the accumulator — and the oracle they exist to satisfy.
/// </summary>
/// <remarks>
/// <c>AnHourInOneStepEqualsAnHourInSteps</c> is the headline, and it is the same category as M5b's
/// incremental-equals-from-scratch: <b>the assertion and the feature are the same statement</b>,
/// because that property is what "flew away mid-fill and came back" is made of. It asserts equality
/// with no tolerance, which integer litres and an exactly additive rainfall depth make possible.
/// </remarks>
public class RainfallTests
{
    private const ulong Seed = 0x4D494E455243UL;

    /// <summary>Two bowls of different sizes, so the catchment multiplier has something to do.</summary>
    private static (Watershed Watershed, BasinWater Water) TwoBowls()
    {
        var samples = new int[40, 40];
        for (int z = 0; z < 40; z++)
        {
            for (int x = 0; x < 40; x++)
            {
                samples[z, x] = 30;
            }
        }

        Dig(samples, 8, 8, radius: 5, depth: 20);
        Dig(samples, 17, 16, radius: 3, depth: 12);

        var field = HeightField.Sample(new SampleGrid(samples, 30), 0, 0, pitch: 24);
        var watershed = Watershed.Analyse(field);
        return (watershed, new BasinWater(MergeTree.Build(watershed)));

        static void Dig(int[,] into, int centreX, int centreZ, int radius, int depth)
        {
            for (int z = 0; z < 40; z++)
            {
                for (int x = 0; x < 40; x++)
                {
                    double distance = Math.Sqrt(
                        ((x - centreX) * (x - centreX)) + ((z - centreZ) * (z - centreZ)));
                    if (distance < radius)
                    {
                        into[z, x] = Math.Min(
                            into[z, x], 30 - (int)Math.Round(depth * (1.0 - (distance / radius))));
                    }
                }
            }
        }
    }

    /// <summary>A bowl with a wide catchment, so rain has somewhere to go.</summary>
    private static (Watershed Watershed, BasinWater Water) Bowl(int pitch = 24)
    {
        var samples = new int[40, 40];
        for (int z = 0; z < 40; z++)
        {
            for (int x = 0; x < 40; x++)
            {
                double distance = Math.Sqrt(((x - 12) * (x - 12)) + ((z - 12) * (z - 12)));
                samples[z, x] = distance >= 8 ? 30 : 30 - (int)Math.Round(24 * (1.0 - (distance / 8)));
            }
        }

        var field = HeightField.Sample(new SampleGrid(samples, 30), 0, 0, pitch);
        var watershed = Watershed.Analyse(field);
        return (watershed, new BasinWater(MergeTree.Build(watershed)));
    }

    /// <summary>The start of the first episode with rain in it.</summary>
    private static long FirstWetEpisode()
    {
        for (int episode = 0; episode < 500; episode++)
        {
            long start = (long)episode * Weather.EpisodeMillis;
            if (Weather.RateAt(Seed, start) > 0)
            {
                return start;
            }
        }

        throw new InvalidOperationException("no rain in the first 500 episodes");
    }

    // ------------------------------------------------------------------ the clock

    [Fact]
    public void TheClockCarriesTheFractionOfAFrame()
    {
        // 60 Hz is 16.66 ms, so a clock that floored each frame would lose 40 ms a second — two
        // and a half minutes a day, and a different amount at every frame rate.
        var clock = new WorldClock();
        for (int frame = 0; frame < 600; frame++)
        {
            clock.Advance(1.0 / 60.0);
        }

        Assert.InRange(clock.ElapsedMillis, 9_999, 10_001);
    }

    [Fact]
    public void TheClockNeverRunsBackwards()
    {
        var clock = new WorldClock(5_000);
        Assert.Equal(5_000, clock.ElapsedMillis);
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(-0.001));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorldClock(-1));
    }

    // ---------------------------------------------------------------- the weather

    [Fact]
    public void RainfallIsExactlyAdditiveAcrossAnySplit()
    {
        // The property everything else rests on. Fuzzed over splits that land inside episodes, on
        // their boundaries, and across many of them at once.
        var random = new Random(808);
        for (int trial = 0; trial < 500; trial++)
        {
            long from = random.NextInt64(0, 3_000_000);
            long middle = from + random.NextInt64(0, 500_000);
            long to = middle + random.NextInt64(0, 500_000);

            Assert.Equal(
                Weather.DepthNanometresOver(Seed, from, to),
                Weather.DepthNanometresOver(Seed, from, middle)
                    + Weather.DepthNanometresOver(Seed, middle, to));
        }
    }

    [Fact]
    public void AnEpisodeDeliversItsOwnRateForItsWholeLength()
    {
        for (int episode = 0; episode < 20; episode++)
        {
            long start = (long)episode * Weather.EpisodeMillis;
            int rate = Weather.RateAt(Seed, start);

            Assert.Equal(
                rate * (long)Weather.EpisodeMillis,
                Weather.DepthNanometresOver(Seed, start, start + Weather.EpisodeMillis));

            // And the rate is the episode's, at both ends of it.
            Assert.Equal(rate, Weather.RateAt(Seed, start + Weather.EpisodeMillis - 1));
        }
    }

    [Fact]
    public void TheWeatherIsAFunctionOfSeedAndTimeAndNothingElse()
    {
        for (int i = 0; i < 50; i++)
        {
            long time = i * 37_000L;
            Assert.Equal(Weather.RateAt(Seed, time), Weather.RateAt(Seed, time));
        }

        // A different world is a different forecast, or the seed is not reaching the hash.
        int same = 0;
        for (int episode = 0; episode < 200; episode++)
        {
            long time = (long)episode * Weather.EpisodeMillis;
            if (Weather.RateAt(Seed, time) == Weather.RateAt(Seed ^ 0x9E37, time))
            {
                same++;
            }
        }

        Assert.True(same < 180, $"two seeds agreed on {same} of 200 episodes");
    }

    [Fact]
    public void ItIsSometimesDryAndSometimesPouring()
    {
        // A hash that always returned the same bucket would pass every test above. Distribution is
        // the thing they cannot see, and dry spells are load-bearing rather than filler: "recent
        // rainfall" only means something if rainfall varies.
        int dry = 0;
        int heavy = 0;
        for (int episode = 0; episode < 1000; episode++)
        {
            int rate = Weather.RateAt(Seed, (long)episode * Weather.EpisodeMillis);
            Assert.InRange(rate, 0, Weather.MaxRateNanometresPerMillisecond);

            if (rate == 0)
            {
                dry++;
            }
            else if (rate >= Weather.MaxRateNanometresPerMillisecond - 1)
            {
                heavy++;
            }
        }

        Assert.InRange(dry, 450, 650);
        Assert.True(heavy > 20, $"only {heavy} of 1000 episodes were a downpour");
    }

    // ------------------------------------------------------------ the accumulator

    [Fact]
    public void AnHourInOneStepEqualsAnHourInSteps()
    {
        // THE ORACLE. No tolerance: depth is exactly additive and litres are integers, so the two
        // paths are either identical or one of them is wrong.
        const long hour = 3_600_000;

        (Watershed watershed, BasinWater oneStep) = Bowl();
        var oneShot = new BasinRainfall(watershed, oneStep, Seed);
        oneShot.AdvanceTo(hour);

        (Watershed stepwiseShed, BasinWater stepwise) = Bowl();
        var ticked = new BasinRainfall(stepwiseShed, stepwise, Seed);
        for (long second = 1; second <= 3600; second++)
        {
            ticked.AdvanceTo(second * 1000);
        }

        Assert.Equal(oneStep.TotalStoredLitres, stepwise.TotalStoredLitres);
        Assert.Equal(oneShot.SpilledLitres, ticked.SpilledLitres);

        for (int basin = 0; basin < watershed.Basins.Count; basin++)
        {
            Assert.Equal(oneStep.StoredIn(basin), stepwise.StoredIn(basin));
            Assert.Equal(oneShot.PendingMicrolitres(basin), ticked.PendingMicrolitres(basin));
        }

        // And it actually rained, or the test above is comparing two empty worlds.
        Assert.True(oneStep.TotalStoredLitres > 0, "an hour of weather delivered nothing");
    }

    [Fact]
    public void ADrizzleTooSmallForOneLitreAStepStillFillsTheBasin()
    {
        // The case that fails without the carried remainder, and it fails in the direction nothing
        // notices: every step rounds to zero litres, forever, so the basin simply never fills while
        // the same time integrated in one call fills it properly.
        (Watershed watershed, BasinWater water) = Bowl();
        var rain = new BasinRainfall(watershed, water, Seed);

        // Advance in 20 ms slices, which at the heaviest rate this world has is a few hundred
        // microlitres per square metre of catchment — well under a litre for a small basin.
        long time = 0;
        while (time < 600_000)
        {
            time += 20;
            rain.AdvanceTo(time);
        }

        (Watershed shed2, BasinWater water2) = Bowl();
        new BasinRainfall(shed2, water2, Seed).AdvanceTo(600_000);

        Assert.Equal(water2.TotalStoredLitres, water.TotalStoredLitres);
        Assert.True(water.TotalStoredLitres > 0, "ten minutes of weather delivered nothing");
    }

    [Fact]
    public void EveryLitreThatFellIsHeldOrGone()
    {
        // Conservation against the weather rather than against a made-up pour: what the sky
        // delivered is what the catchments collected, and it is either in the ground or over the
        // window edge — with the sub-litre remainder counted, because it is real water.
        (Watershed watershed, BasinWater water) = Bowl();
        var rain = new BasinRainfall(watershed, water, Seed);
        rain.AdvanceTo(7_200_000);

        long depth = Weather.DepthNanometresOver(Seed, 0, 7_200_000);
        long expectedMicrolitres = 0;
        long pending = 0;
        for (int basin = 0; basin < watershed.Basins.Count; basin++)
        {
            expectedMicrolitres += depth * watershed.Basins[basin].CatchmentSquareMetres;
            pending += rain.PendingMicrolitres(basin);
        }

        long delivered = water.TotalStoredLitres + rain.SpilledLitres;
        Assert.Equal(expectedMicrolitres, (delivered * 1_000_000) + pending);
    }

    [Fact]
    public void WhatABasinCollectsIsItsCatchmentTimesTheRain()
    {
        // Rain falls on the catchment, not on the puddle — which is why CatchmentSquareMetres is
        // the multiplier and the lake's own area is not. Asserted as an exact identity per basin
        // rather than as a ratio, which is both stronger and immune to a fixture where one basin
        // happens to overflow into another.
        // Episode 0 of this seed is dry, so the window has to be chosen rather than assumed —
        // which is the fixture stating that it really tested something, the same guard that caught
        // the single-basin version of this test.
        long wetStart = FirstWetEpisode();
        long wetEnd = wetStart + 60_000;

        (Watershed watershed, BasinWater water) = TwoBowls();
        var rain = new BasinRainfall(watershed, water, Seed, wetStart);
        rain.AdvanceTo(wetEnd);

        Assert.Equal(0, rain.SpilledLitres);
        Assert.True(watershed.Basins.Count >= 2, "the fixture needs two basins");
        Assert.True(
            watershed.Basins.Select(b => b.CatchmentSquareMetres).Distinct().Count() >= 2,
            "the fixture needs two DIFFERENT catchments, or the multiplier is untested");

        long depth = Weather.DepthNanometresOver(Seed, wetStart, wetEnd);
        Assert.True(depth > 0, "the fixture rained nothing");

        foreach (Basin basin in watershed.Basins)
        {
            long collected = (water.StoredIn(basin.Index) * 1_000_000)
                + rain.PendingMicrolitres(basin.Index);
            Assert.Equal(depth * basin.CatchmentSquareMetres, collected);
        }
    }

    [Fact]
    public void WhatLeavesTheWorldIsCountedAcrossEveryCall()
    {
        // FOUND BY THE BREAK-RECIPE BATTERY: at real rainfall none of the fixtures above ever
        // overflows, so every assertion any of them makes about spill compares zero with zero.
        // Making the accumulator report only its last call's spill left the whole suite green.
        // Scaling the rain drowns the bowl on purpose, which is the only way the accounting gets
        // exercised at all.
        const int scale = 500;

        (Watershed first, BasinWater oneStepWater) = Bowl();
        var oneShot = new BasinRainfall(first, oneStepWater, Seed, 0, scale);
        oneShot.AdvanceTo(3_600_000);

        (Watershed second, BasinWater steppedWater) = Bowl();
        var ticked = new BasinRainfall(second, steppedWater, Seed, 0, scale);
        for (long minute = 1; minute <= 60; minute++)
        {
            ticked.AdvanceTo(minute * 60_000);
        }

        Assert.True(oneShot.SpilledLitres > 0, "the fixture never overflowed, so this tests nothing");
        Assert.Equal(oneShot.SpilledLitres, ticked.SpilledLitres);
        Assert.Equal(oneStepWater.TotalStoredLitres, steppedWater.TotalStoredLitres);

        // And the world is full, so what fell after that is exactly what left.
        Assert.Equal(0, oneStepWater.FreeIn(oneStepWater.Tree.NodeCount - 1));
    }

    [Fact]
    public void ARainfallScaleBelowOneIsRefused()
    {
        (Watershed watershed, BasinWater water) = Bowl();
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BasinRainfall(watershed, water, Seed, 0, 0));
    }

    [Fact]
    public void TimeDoesNotRunBackwards()
    {
        (Watershed watershed, BasinWater water) = Bowl();
        var rain = new BasinRainfall(watershed, water, Seed);
        rain.AdvanceTo(1000);
        Assert.Throws<ArgumentOutOfRangeException>(() => rain.AdvanceTo(999));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Weather.DepthNanometresOver(Seed, 500, 499));
    }

    [Fact]
    public void ADryStretchChangesNothingAtAll()
    {
        // Finding a dry episode and asserting it is genuinely free, because M8 will want to skip
        // ticks entirely and the seam has to exist before it is relied on.
        long dryStart = -1;
        for (int episode = 0; episode < 200 && dryStart < 0; episode++)
        {
            long start = (long)episode * Weather.EpisodeMillis;
            if (Weather.RateAt(Seed, start) == 0)
            {
                dryStart = start;
            }
        }

        Assert.True(dryStart >= 0, "no dry episode in the first 200");

        (Watershed watershed, BasinWater water) = Bowl();
        var rain = new BasinRainfall(watershed, water, Seed, dryStart);
        Assert.Equal(0, rain.AdvanceTo(dryStart + Weather.EpisodeMillis));
        Assert.Equal(0, water.TotalStoredLitres);
    }
}
