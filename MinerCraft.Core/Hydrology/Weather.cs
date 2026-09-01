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
