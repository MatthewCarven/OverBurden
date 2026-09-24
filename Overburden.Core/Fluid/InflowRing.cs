namespace Overburden.Core.Fluid;

/// <summary>
/// What one chunk received, tick by tick, over the last <see cref="Slots"/> ticks, and when it was
/// last disturbed — the volume half of the quiet rule (docs/FRONT-ADVANCE-PLAN.md, slice 2).
/// </summary>
/// <remarks>
/// A ring keyed by tick modulo its size: a slot holding an older tick is overwritten, so the sum over
/// a window is exact for any window shorter than the ring and nothing is ever cleared. The quiet
/// window ships at two ticks; a longer one than the ring cannot be answered and the caller keeps the
/// old any-inflow rule for it.
/// </remarks>
internal sealed class InflowRing
{
    /// <summary>Ticks the ring remembers.</summary>
    public const int Slots = 8;

    /// <summary>The volume passed for a disturbance — an edit or a fall — which is fed whatever its size.</summary>
    public const long Disturbance = -1;

    private readonly long[] _ticks = [long.MinValue, long.MinValue, long.MinValue, long.MinValue,
                                      long.MinValue, long.MinValue, long.MinValue, long.MinValue];
    private readonly long[] _millilitres = new long[Slots];
    private long _lastDisturbed = long.MinValue;

    public void Note(long tick, long millilitres)
    {
        if (millilitres == Disturbance)
        {
            _lastDisturbed = tick;
            return;
        }

        int slot = (int)(tick & (Slots - 1));
        if (_ticks[slot] != tick)
        {
            _ticks[slot] = tick;
            _millilitres[slot] = 0;
        }

        _millilitres[slot] += millilitres;
    }

    /// <summary>The millilitres received at ticks within <paramref name="window"/> of <paramref name="now"/>.</summary>
    public long Within(long now, int window, out bool disturbed)
    {
        disturbed = _lastDisturbed != long.MinValue && now - _lastDisturbed <= window;
        long sum = 0;
        for (int i = 0; i < Slots; i++)
        {
            if (_ticks[i] != long.MinValue && now - _ticks[i] <= window && _ticks[i] <= now)
            {
                sum += _millilitres[i];
            }
        }

        return sum;
    }
}
