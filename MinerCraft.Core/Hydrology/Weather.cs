using MinerCraft.Core.Worldgen;

namespace MinerCraft.Core.Hydrology;

/// <summary>
/// How hard it is raining, as a pure function of the world seed and the world clock.
/// </summary>
/// <remarks>
/// <para>
/// <b>The weather is analytic, exactly as the terrain is</b>, and that is the decision this class
/// exists to hold. Time is chopped into fixed episodes; an episode's intensity is a hash of the
/// seed and its index; so the rain that fell over any interval is a closed-form sum rather than
/// something that has to be stepped through. Three things follow, and the first is the reason:
/// </para>
/// <para>
/// <b>Integrating forward becomes exact rather than approximate.</b> A player who flies away for
/// ten minutes and comes back finds the level the fill would have reached, without ten minutes of
/// ticks having run and <i>without the arithmetic differing by a litre</i>. That is the seam
/// DESIGN's Tier A promises, and an analytic weather function is what makes it defensible instead
/// of a cheat. <b>It is server-authoritative for free</b>, per non-negotiable 1 — two clients
/// evaluating the same function of the same seed at the same time get the same storm with nothing
/// synchronised. And <b>it is testable without a clock</b>, because it is a function of time rather
/// than a thing that has advanced through time.
/// </para>
/// <para>
/// <b>The units are integers all the way down, and they were chosen to make one multiplication
/// exact.</b> A rate is nanometres of rainfall depth per millisecond of world time — which is also
/// micrometres per second, and 1 of it is 3.6 mm/hour. Depth over an interval is therefore
/// <c>rate × milliseconds</c>, an exact integer, and <b>a nanometre of rain over a square metre is
/// exactly one microlitre</b>, so the volume arriving in a basin is an exact integer too. Nothing
/// in this chain rounds, which is what lets the accumulator's oracle assert equality to the litre
/// rather than to a tolerance.
/// </para>
/// <para>
/// <b>Rain starts and stops abruptly at an episode boundary.</b> Known, and left alone until a
/// person says it reads badly. <b>Revisit trigger: the play calls the onset jarring</b> — the shape
/// to add is a ramp inside the episode, whose integral is still closed form, so the exactness above
/// survives the fix.
/// </para>
/// </remarks>
public static class Weather
{
    /// <summary>How long one weather episode lasts.</summary>
    /// <remarks>
    /// Two minutes. Long enough that a shower is an event rather than a flicker, short enough that
    /// a session sees several. It is a constant rather than a setting because changing it changes
    /// every world's weather history, which is the same argument that keeps worldgen settings in
    /// the header rather than in a config file.
    /// </remarks>
    public const int EpisodeMillis = 120_000;

    /// <summary>Heaviest rain any episode can be, in nanometres per millisecond — 43 mm/hour.</summary>
    public const int MaxRateNanometresPerMillisecond = 12;

    /// <summary>Episodes in a hundred that are dry.</summary>
    /// <remarks>
    /// Dry spells are not filler: "recent rainfall" only means anything if rainfall varies, and a
    /// storm reads as an event only against the quiet before it. M7c's wet-surface term and the
    /// overcast dimming both take their meaning from this number being well above zero.
    /// </remarks>
    public const int DryEpisodesPerHundred = 55;

    /// <summary>How long ground stays wet after the rain that soaked it.</summary>
    /// <remarks>
    /// <b>Derived from <see cref="EpisodeMillis"/> rather than tuned to it</b>, and the equality is
    /// what makes the number mean something: with the soak window exactly one episode long, a
    /// single episode of the heaviest possible rain takes the ground from bone dry to fully
    /// saturated and no further, and the two minutes after it dry the ground out again. Any other
    /// value and full wetness is either unreachable or reached in the first few seconds of a
    /// drizzle. <b>Revisit trigger: an episode length that is no longer a sensible drying time</b>
    /// — they are the same number today because both are "how long a weather event lasts", and a
    /// world with hour-long storms would want them separated.
    /// </remarks>
    public const int SoakWindowMillis = EpisodeMillis;

    /// <summary>How far ahead of the rain the sky knows it is coming.</summary>
    /// <remarks>
    /// <b>Storm → dark → rain is most of what makes weather legible</b>, and a sky that darkens at
    /// the same instant the first drop falls reads as a renderer glitch rather than as weather.
    /// The overcast window therefore straddles now: it looks back one soak window, so the cloud
    /// clears at about the rate the ground dries, and forward this far, so it gathers first.
    /// </remarks>
    public const int CloudLeadMillis = EpisodeMillis / 2;

    private const ulong Salt = 0x7A1_4EA7_4E5UL;

    /// <summary>Nanometres of rain per millisecond at this instant.</summary>
    public static int RateAt(ulong seed, long timeMillis)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(timeMillis);
        return RateForEpisode(seed, timeMillis / EpisodeMillis);
    }

    /// <summary>
    /// Nanometres of rain that fell between two instants. Exactly additive across any split, which
    /// is the property the whole design rests on.
    /// </summary>
    public static long DepthNanometresOver(ulong seed, long fromMillis, long toMillis)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fromMillis);
        if (toMillis < fromMillis)
        {
            throw new ArgumentOutOfRangeException(
                nameof(toMillis), toMillis, "World time does not run backwards.");
        }

        long depth = 0;
        long cursor = fromMillis;
        while (cursor < toMillis)
        {
            long episode = cursor / EpisodeMillis;
            long episodeEnd = (episode + 1) * EpisodeMillis;
            long slice = Math.Min(episodeEnd, toMillis) - cursor;
            depth += RateForEpisode(seed, episode) * slice;
            cursor += slice;
        }

        return depth;
    }

    /// <summary>
    /// How wet the open ground is, 0 for bone dry and 1 for as saturated as this world's weather
    /// can make it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A box filter over the last <see cref="SoakWindowMillis"/>, which is to say
    /// <see cref="DepthNanometresOver"/> and a divide.</b> That is the whole implementation and it
    /// is deliberate: the one primitive whose exactness the milestone rests on is also the only
    /// arithmetic here, so there is no second model of what "recent rainfall" means to drift from
    /// the first. It rises linearly while it rains and falls linearly as the storm slides out of
    /// the window — an exponential decay would be more physical, and the difference is invisible
    /// because the tail of the fall is at the dry end where the term contributes nothing.
    /// </para>
    /// <para>
    /// <b>Pure in (seed, time), like everything else here, and that is the point rather than a
    /// nicety.</b> A player who flies away through a downpour and comes back finds the ground as
    /// wet as it should be, with nothing having ticked and nothing stored — the same claim
    /// <see cref="BasinRainfall"/> makes about volume, for free, because both are integrals of the
    /// same function.
    /// </para>
    /// <para>
    /// <b>A float, and this is where the integer rule stops.</b> Everything above returns integers
    /// because it feeds a volume that is asserted to the litre; this feeds a shader uniform and is
    /// never stored, never summed and never compared for equality. Computed by one IEEE division
    /// of exact integers, so it is still the same number on every machine — but nothing downstream
    /// needs it to be, and an int here would be exactness cargo-culted onto a look term.
    /// </para>
    /// <para>
    /// <b>A world in its first two minutes reads dry even in a downpour</b>, because the window is
    /// clamped at the start of time while the denominator is not. That is correct rather than an
    /// edge case — thirty seconds of rain has genuinely only wet the ground for thirty seconds —
    /// and normalising by the clamped span instead would report saturated ground one second after
    /// a world is created.
    /// </para>
    /// </remarks>
    public static float WetnessAt(ulong seed, long timeMillis)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(timeMillis);
        return MeanRateOver(seed, timeMillis - SoakWindowMillis, timeMillis, SoakWindowMillis);
    }

    /// <summary>
    /// How overcast the sky is, 0 for clear and 1 for as heavy as this world's weather can make it.
    /// </summary>
    /// <remarks>
    /// The same box filter as <see cref="WetnessAt"/>, over a window shifted forward by
    /// <see cref="CloudLeadMillis"/> so the cloud arrives before the rain does. <b>How dark that
    /// makes the world is not decided here</b> — this says how much cloud there is, and what a
    /// skyful of it does to the light is a look constant that lives with the other look constants
    /// in the renderer. Keeping the two apart is what lets the dimming be retuned without touching
    /// a function three other things integrate.
    /// </remarks>
    public static float OvercastAt(ulong seed, long timeMillis)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(timeMillis);
        return MeanRateOver(
            seed,
            timeMillis - SoakWindowMillis,
            timeMillis + CloudLeadMillis,
            SoakWindowMillis + CloudLeadMillis);
    }

    /// <summary>
    /// The mean rate over a window as a fraction of the heaviest possible, with the window clamped
    /// at the start of time and the denominator deliberately not clamped with it.
    /// </summary>
    private static float MeanRateOver(ulong seed, long fromMillis, long toMillis, long spanMillis)
    {
        long depth = DepthNanometresOver(seed, Math.Max(0, fromMillis), toMillis);
        return (float)((double)depth / (spanMillis * (double)MaxRateNanometresPerMillisecond));
    }

    /// <summary>The intensity of one episode. A hash, so it needs no state and no history.</summary>
    private static int RateForEpisode(ulong seed, long episode)
    {
        ulong hash = Hash64.Mix(seed ^ Salt ^ (ulong)episode);
        uint roll = (uint)(hash >> 40) % 100;
        if (roll < DryEpisodesPerHundred)
        {
            return 0;
        }

        // The wet range is spread linearly over what is left, so drizzle is as likely as a
        // downpour. Integer arithmetic rather than a float, because a rate is an input to the
        // exactness claim above and float rounding differs between platforms.
        uint wet = roll - DryEpisodesPerHundred;
        uint span = 100 - DryEpisodesPerHundred;
        return 1 + (int)(wet * (MaxRateNanometresPerMillisecond - 1) / (span - 1));
    }
}
