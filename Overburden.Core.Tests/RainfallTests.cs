using Overburden.Core.Hydrology;

namespace Overburden.Core.Tests;

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

    // ------------------------------------------------------- wetness and overcast

    [Fact]
    public void AtAnEpisodeBoundaryWetnessIsExactlyThatEpisodesShareOfTheMaximum()
    {
        // The soak window is one episode long, so a window ending on an episode boundary covers
        // exactly one episode and nothing else. That makes the answer a rational number the test
        // can state rather than approximate: wetness == rate / maxRate, for every episode.
        //
        // It is the tightest available assertion about the window's *length*. Widen or narrow
        // SoakWindowMillis by a millisecond and this fails everywhere the two neighbouring
        // episodes differ, which is most of them.
        for (int episode = 1; episode < 400; episode++)
        {
            long boundary = (long)episode * Weather.EpisodeMillis;
            float expected =
                Weather.RateAt(Seed, boundary - 1) / (float)Weather.MaxRateNanometresPerMillisecond;

            Assert.Equal(expected, Weather.WetnessAt(Seed, boundary), 6);
        }
    }

    [Fact]
    public void TheSkyDarkensBeforeTheFirstDropAndTheGroundIsStillDry()
    {
        // "Storm -> dark -> rain" is most of what makes weather legible, and it is the one
        // property that separates the overcast window from the soak window. The shape that shows
        // it is a dry episode followed by a wet one: at the boundary the ground has had nothing
        // to soak it, so wetness is exactly zero, while the cloud for the episode about to start
        // is already gathered.
        long boundary = FirstDryToWetBoundary();

        Assert.Equal(0f, Weather.WetnessAt(Seed, boundary));
        Assert.True(
            Weather.OvercastAt(Seed, boundary) > 0f,
            "the sky was clear at the instant the rain started");
    }

    [Fact]
    public void TheCloudIsStillThereAfterTheRainHasStopped()
    {
        // The other half of the same window, and the half a forward-only lead would fail. Clearing
        // takes about as long as the ground takes to dry, which is why the window looks back a
        // whole soak window rather than only forward.
        long boundary = FirstWetToDryBoundary();

        Assert.Equal(0, Weather.RateAt(Seed, boundary));
        Assert.True(
            Weather.OvercastAt(Seed, boundary) > 0f,
            "the sky cleared the instant the rain stopped");
    }

    [Fact]
    public void AWorldInItsFirstMomentsIsDryHoweverHardItIsRaining()
    {
        // The window is clamped at the start of time and the denominator deliberately is not, so
        // thirty seconds of rain reads as thirty seconds of rain rather than as saturated ground.
        // Normalising by the clamped span instead — the obvious "fix" — reports a fully soaked
        // world one second after it is created, and this is the only test that would notice.
        Assert.Equal(0f, Weather.WetnessAt(Seed, 0));

        ulong pouring = SeedWhoseFirstEpisodeIsHeavy();
        float early = Weather.WetnessAt(pouring, Weather.SoakWindowMillis / 4);

        Assert.True(early > 0f, "a quarter of a window of heavy rain wet nothing at all");
        Assert.True(early < 0.3f, $"a quarter of a window read as {early:F2} wet");
    }

    [Fact]
    public void BothTermsUseTheWholeRangeAndNeverLeaveIt()
    {
        // A normalisation that was wrong by a constant factor would still be smooth, still be
        // deterministic and still pass every identity above; what it would do is spend the whole
        // world in the bottom tenth of the range or clip permanently at the top. So the range is
        // the assertion: dry ground exists, saturated ground is reachable, and nothing escapes.
        float wettest = 0f;
        float cloudiest = 0f;
        bool everBoneDry = false;

        for (int episode = 1; episode < 20_000; episode++)
        {
            long boundary = (long)episode * Weather.EpisodeMillis;

            float wet = Weather.WetnessAt(Seed, boundary);
            float cloud = Weather.OvercastAt(Seed, boundary);

            Assert.InRange(wet, 0f, 1f);
            Assert.InRange(cloud, 0f, 1f);

            wettest = Math.Max(wettest, wet);
            cloudiest = Math.Max(cloudiest, cloud);
            everBoneDry |= wet == 0f;
        }

        Assert.True(everBoneDry, "no instant in 20,000 episodes had dry ground");
        Assert.True(wettest > 0.9f, $"the ground never got wetter than {wettest:F2}");
        Assert.True(cloudiest > 0.9f, $"the sky never got darker than {cloudiest:F2}");
    }

    [Fact]
    public void WetnessAndOvercastAreFunctionsOfSeedAndTimeAndNothingElse()
    {
        // The same argument as the rate's own determinism test: these feed a look rather than a
        // volume, but a player who flies away through a downpour and comes back has to find the
        // ground as wet as they left it, and that is this property rather than any stored state.
        const long instant = 7 * Weather.EpisodeMillis / 3;

        Assert.Equal(Weather.WetnessAt(Seed, instant), Weather.WetnessAt(Seed, instant));
        Assert.Equal(Weather.OvercastAt(Seed, instant), Weather.OvercastAt(Seed, instant));

        int differing = 0;
        for (ulong other = Seed + 1; other <= Seed + 40; other++)
        {
            if (Weather.WetnessAt(other, instant) != Weather.WetnessAt(Seed, instant)) differing++;
        }

        Assert.True(differing > 20, $"only {differing} of 40 other seeds had different weather");
    }

    [Fact]
    public void NeitherTermRunsBeforeTheWorldExisted()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Weather.WetnessAt(Seed, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Weather.OvercastAt(Seed, -1));
    }

    /// <summary>The first episode boundary at which a dry episode is followed by a wet one.</summary>
    private static long FirstDryToWetBoundary() => FirstBoundaryWhere(dry: true);

    /// <summary>The first episode boundary at which a wet episode is followed by a dry one.</summary>
    private static long FirstWetToDryBoundary() => FirstBoundaryWhere(dry: false);

    private static long FirstBoundaryWhere(bool dry)
    {
        for (int episode = 1; episode < 1000; episode++)
        {
            long boundary = (long)episode * Weather.EpisodeMillis;
            bool before = Weather.RateAt(Seed, boundary - 1) == 0;
            bool after = Weather.RateAt(Seed, boundary) == 0;

            if (before == dry && after != dry) return boundary;
        }

        throw new InvalidOperationException("the forecast never changed in 1,000 episodes");
    }

    /// <summary>A seed whose very first episode is at or near the heaviest this world gets.</summary>
    private static ulong SeedWhoseFirstEpisodeIsHeavy()
    {
        for (ulong seed = 1; seed < 10_000; seed++)
        {
            if (Weather.RateAt(seed, 0) == Weather.MaxRateNanometresPerMillisecond) return seed;
        }

        throw new InvalidOperationException("no seed in 10,000 opened with a downpour");
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
    public void RainOwedToTierBIsPaidBeforeALitreReachesABasin()
    {
        // A cell of 15,625 mL dropped from this sky (2026-09-19): the basins receive exactly
        // that much less than the same hour with nothing owed, the debt is paid in full, and
        // nothing was rounded away on the way.
        const long hour = 3_600_000;
        const long cell = 15_625_000;

        (Watershed shedA, BasinWater plain) = Bowl();
        var plainRain = new BasinRainfall(shedA, plain, Seed);
        plainRain.AdvanceTo(hour);

        (Watershed shedB, BasinWater owing) = Bowl();
        var owingRain = new BasinRainfall(shedB, owing, Seed);
        owingRain.Divert(cell);
        owingRain.AdvanceTo(hour);

        Assert.Equal(0L, owingRain.OwedMicrolitres);
        Assert.Equal(cell, owingRain.PaidMicrolitres);

        long plainTotal = plain.TotalStoredLitres * 1_000_000 + Pending(plainRain, shedA) + plainRain.SpilledLitres * 1_000_000;
        long owingTotal = owing.TotalStoredLitres * 1_000_000 + Pending(owingRain, shedB) + owingRain.SpilledLitres * 1_000_000;
        Assert.Equal(cell, plainTotal - owingTotal);
        Assert.True(plain.TotalStoredLitres > 0, "an hour of weather delivered nothing");

        static long Pending(BasinRainfall rain, Watershed shed)
        {
            long sum = 0;
            for (int basin = 0; basin < shed.Basins.Count; basin++)
            {
                sum += rain.PendingMicrolitres(basin);
            }

            return sum;
        }
    }

    [Fact]
    public void AnHourInOneStepEqualsAnHourInStepsWithRainOwed()
    {
        // The oracle again with a debt in it, because the debt is paid from the same microlitres
        // on both paths — a payment rule that depended on the step would break the property the
        // whole tier rests on, and this is what would say so.
        const long hour = 3_600_000;
        const long cell = 15_625_000;

        (Watershed watershed, BasinWater oneStep) = Bowl();
        var oneShot = new BasinRainfall(watershed, oneStep, Seed);
        oneShot.Divert(3 * cell);
        oneShot.AdvanceTo(hour);

        (Watershed stepwiseShed, BasinWater stepwise) = Bowl();
        var ticked = new BasinRainfall(stepwiseShed, stepwise, Seed);
        ticked.Divert(3 * cell);
        for (long second = 1; second <= 3600; second++)
        {
            ticked.AdvanceTo(second * 1000);
        }

        Assert.Equal(oneStep.TotalStoredLitres, stepwise.TotalStoredLitres);
        Assert.Equal(oneShot.SpilledLitres, ticked.SpilledLitres);
        Assert.Equal(oneShot.PaidMicrolitres, ticked.PaidMicrolitres);
        Assert.Equal(3 * cell, ticked.PaidMicrolitres);
        for (int basin = 0; basin < watershed.Basins.Count; basin++)
        {
            Assert.Equal(oneStep.StoredIn(basin), stepwise.StoredIn(basin));
            Assert.Equal(oneShot.PendingMicrolitres(basin), ticked.PendingMicrolitres(basin));
        }
    }

    [Fact]
    public void RainOwedWithNoRainToPayItStaysOwed()
    {
        // Diverted in a dry spell: nothing is invented to pay it, and the next rain does.
        (Watershed watershed, BasinWater water) = Bowl();
        var rain = new BasinRainfall(watershed, water, Seed);
        long wet = FirstWetEpisode();
        Assert.True(wet > 0, "this seed's first episode is wet, so there is no dry spell to test in");

        rain.Divert(15_625_000);
        rain.AdvanceTo(wet);
        Assert.Equal(15_625_000L, rain.OwedMicrolitres);
        Assert.Equal(0L, rain.PaidMicrolitres);
        Assert.Equal(0L, water.TotalStoredLitres);

        rain.AdvanceTo(wet + Weather.EpisodeMillis);
        Assert.Equal(0L, rain.OwedMicrolitres);
        Assert.Equal(15_625_000L, rain.PaidMicrolitres);
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
