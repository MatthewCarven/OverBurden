namespace Overburden.Core.Jobs;

/// <summary>
/// What the queue's sweeps have done: how many there were, how far apart, how much each one
/// touched, and how long each held the lock. One consistent picture, read under the queue's lock.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> Every document here says the queue "re-sorts every 200 ms", and that
/// was folklore: the sweep is lazy, it runs inside a worker's <see cref="JobQueue.TryTake"/> when
/// the interval has elapsed, so how often it <em>actually</em> fires is a function of when workers
/// wake — on a submission, or on their idle backstop — and not of the interval at all. A queue
/// nobody takes from is never swept, and a queue eight idle workers poll is swept on the dot.
/// Only a count can say which of those a run was.
/// </para>
/// <para>
/// <b>Every count is a lifetime total, and a window is <see cref="Since"/> of two snapshots.</b>
/// The two peaks and the longest gap are the exception: they stay lifetime through
/// <see cref="Since"/>, because the sweep that saw the most entries is the fill's first one and
/// that fact is wanted whichever window is being read. A print of these says so.
/// </para>
/// </remarks>
/// <param name="Sweeps">Sweeps that ran, due or forced.</param>
/// <param name="ForcedSweeps">Of those, the ones asked for by name rather than by the interval.</param>
/// <param name="EmptySweeps">Of those, the ones that found nothing queued and did nothing but take the lock.</param>
/// <param name="EntriesReevaluated">Priorities recomputed, summed over every sweep.</param>
/// <param name="EntriesDropped">Cancelled entries a sweep removed and reported.</param>
/// <param name="MostEntriesInOneSweep">Lifetime peak of the list's length at the start of a sweep.</param>
/// <param name="TakeCalls">Every <see cref="JobQueue.TryTake"/>: the chances a sweep had to fire.</param>
/// <param name="TakesThatFoundWork">Of those, the ones that handed a job to a worker.</param>
/// <param name="SweepMillisTotal">Wall clock spent inside sweeps, with the lock held.</param>
/// <param name="SweepMillisMax">Lifetime peak of one sweep's wall clock.</param>
/// <param name="GapMillisTotal">Sum, on the queue's clock, of the interval each sweep found since the previous one.</param>
/// <param name="GapMillisMax">Lifetime peak of that interval: the longest priorities went stale.</param>
public readonly record struct SweepStatistics(
    long Sweeps,
    long ForcedSweeps,
    long EmptySweeps,
    long EntriesReevaluated,
    long EntriesDropped,
    long MostEntriesInOneSweep,
    long TakeCalls,
    long TakesThatFoundWork,
    double SweepMillisTotal,
    double SweepMillisMax,
    long GapMillisTotal,
    long GapMillisMax)
{
    /// <summary>
    /// The additive terms of this snapshot less those of an earlier one; the three peaks are
    /// carried across unchanged, because a peak has no window.
    /// </summary>
    public SweepStatistics Since(SweepStatistics earlier) => new(
        Sweeps - earlier.Sweeps,
        ForcedSweeps - earlier.ForcedSweeps,
        EmptySweeps - earlier.EmptySweeps,
        EntriesReevaluated - earlier.EntriesReevaluated,
        EntriesDropped - earlier.EntriesDropped,
        MostEntriesInOneSweep,
        TakeCalls - earlier.TakeCalls,
        TakesThatFoundWork - earlier.TakesThatFoundWork,
        SweepMillisTotal - earlier.SweepMillisTotal,
        SweepMillisMax,
        GapMillisTotal - earlier.GapMillisTotal,
        GapMillisMax);

    /// <summary>Mean interval between sweeps on the queue's clock, or zero with no sweep.</summary>
    public double MeanGapMillis => Sweeps == 0 ? 0.0 : (double)GapMillisTotal / Sweeps;

    /// <summary>Mean wall clock per sweep, or zero with no sweep.</summary>
    public double MeanSweepMillis => Sweeps == 0 ? 0.0 : SweepMillisTotal / Sweeps;

    /// <summary>Mean priorities recomputed per sweep, or zero with no sweep.</summary>
    public double MeanEntriesPerSweep => Sweeps == 0 ? 0.0 : (double)EntriesReevaluated / Sweeps;
}
