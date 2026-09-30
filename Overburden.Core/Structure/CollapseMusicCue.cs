namespace Overburden.Core.Structure;

/// <summary>
/// When the collapse's music plays and how fast (COLLAPSE-PLAN part e, the phonk): the rules
/// without the player, so they are testable in Core. The game's player reads <see cref="Playing"/>,
/// <see cref="Gain"/> and <see cref="Pitch"/> after each <see cref="Step"/> and starts the track
/// from the top when a step says so.
/// </summary>
/// <remarks>
/// <para>
/// <b>Decision 2, when:</b> the track starts from the top when a collapse starts with none live,
/// loops, and fades out over <see cref="FadeSeconds"/> once the last one ends. A collapse that
/// starts while it plays, or while it fades, does not restart it; a fade it interrupts comes back to
/// full.
/// </para>
/// <para>
/// <b>Decisions 3 and 4, how fast:</b> the fastest live collapse's pace multiplier sets the pitch,
/// which in Godot is pitch and tempo together, the way the old games sped their music up. Linear in
/// doublings, from 1 at ×1 to <see cref="MaxPitch"/> at the pace's cap: at the defaults
/// 1 + 0.0625 × log2(multiplier), so 1.25 at ×16.
/// </para>
/// </remarks>
public sealed class CollapseMusicCue
{
    /// <summary>The pitch at the pace's cap, decision 3 — his ear moves it (<c>--collapsemusicmax=</c>).</summary>
    public const double DefaultMaxPitch = 1.25;

    /// <summary>Seconds the fade takes once the last collapse ends, decision 2.</summary>
    public const double DefaultFadeSeconds = 2.0;

    /// <summary>The pitch at <see cref="CapMultiplier"/>; at least 1.</summary>
    public double MaxPitch { get; init; } = DefaultMaxPitch;

    /// <summary>The pace multiplier the pitch reaches <see cref="MaxPitch"/> at: the pace's own cap.</summary>
    public double CapMultiplier { get; init; } = 16;

    /// <inheritdoc cref="DefaultFadeSeconds"/>
    public double FadeSeconds { get; init; } = DefaultFadeSeconds;

    /// <summary>Whether the track is playing, fading included.</summary>
    public bool Playing { get; private set; }

    /// <summary>1 while a collapse is live, falling to 0 over the fade.</summary>
    public double Gain { get; private set; }

    /// <summary>The playback speed: pitch and tempo together. Held through the fade.</summary>
    public double Pitch { get; private set; } = 1.0;

    /// <summary>Times the track started from the top, and times it faded to silence, over the lifetime.</summary>
    public long Starts { get; private set; }

    /// <inheritdoc cref="Starts"/>
    public long Stops { get; private set; }

    /// <summary>The pitch for a pace multiplier: linear in doublings, 1 at ×1, <see cref="MaxPitch"/> at the cap and past it.</summary>
    public double PitchFor(double multiplier)
    {
        if (CapMultiplier <= 1 || !(multiplier > 1))
        {
            return 1.0;
        }

        double doublings = Math.Log2(multiplier) / Math.Log2(CapMultiplier);
        return 1.0 + ((Math.Max(1.0, MaxPitch) - 1.0) * Math.Min(1.0, doublings));
    }

    /// <summary>
    /// One frame: <paramref name="fastest"/> is the fastest live collapse's pace multiplier, or null
    /// when none is live; <paramref name="seconds"/> is real time since the last step. True when
    /// the track must start from the top this frame.
    /// </summary>
    public bool Step(double? fastest, double seconds)
    {
        if (fastest is { } multiplier)
        {
            Pitch = PitchFor(multiplier);
            Gain = 1.0;
            if (Playing)
            {
                return false;
            }

            Playing = true;
            Starts++;
            return true;
        }

        if (!Playing)
        {
            return false;
        }

        Gain = FadeSeconds > 0 ? Math.Max(0.0, Gain - (Math.Max(0.0, seconds) / FadeSeconds)) : 0.0;
        if (Gain <= 0)
        {
            Playing = false;
            Stops++;
        }

        return false;
    }
}
