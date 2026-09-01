namespace MinerCraft.Core.Hydrology;

/// <summary>
/// How long this world has been running, in simulated milliseconds.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing in this project has ever needed time.</b> Worldgen is a pure function of position,
/// light is a fixpoint, physics takes <c>dt</c> as an argument and remembers nothing between
/// steps. M7 is the first system whose state depends on <i>when</i>, and everything after it needs
/// the same clock: M8's fill in progress, M9's delay between a ceiling becoming unsafe and coming
/// down.
/// </para>
/// <para>
/// <b>Integer milliseconds, not a double of seconds</b>, because this number is the input to a
/// determinism claim. Rainfall is a pure function of (seed, time) so that integrating an hour in
/// one step equals stepping it a second at a time — and a clock that drifts by an ulp makes the
/// two answers differ for a reason no test would ever localise. The fractional part of a frame is
/// carried here rather than lost, so a 60 Hz session and a 144 Hz one reach the same time.
/// </para>
/// <para>
/// <b>Time advances only while the world is open.</b> Quitting for a week must not flood the map:
/// the surprise is unbounded, arrives before the player can see it, and makes the integrate-forward
/// interval unbounded too. <b>Revisit trigger: the first mechanic that wants the world to have
/// moved on while you were away</b> — crops, or a pump that runs offline.
/// </para>
/// </remarks>
public sealed class WorldClock
{
    private double _carriedMillis;

    public WorldClock(long startMillis = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startMillis);
        ElapsedMillis = startMillis;
    }

    /// <summary>Simulated milliseconds since this world was created.</summary>
    public long ElapsedMillis { get; private set; }

    public double ElapsedSeconds => ElapsedMillis / 1000.0;

    /// <summary>Advances by one frame's worth of time, carrying the remainder.</summary>
    public void Advance(double seconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(seconds);

        _carriedMillis += seconds * 1000.0;
        if (_carriedMillis < 1.0)
        {
            return;
        }

        long whole = (long)_carriedMillis;
        _carriedMillis -= whole;
        ElapsedMillis += whole;
    }
}
