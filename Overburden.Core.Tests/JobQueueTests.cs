using Overburden.Core.Jobs;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

public class JobQueueTests
{
    [Fact]
    public void EmptyQueueTakesNothing()
    {
        long clock = 0;
        JobQueue queue = JobTestHelpers.QueueWithClock(JobTestHelpers.ViewerAt(0), () => clock);

        Assert.Equal(0, queue.Count);
        Assert.False(queue.TryTake(out _));
    }

    [Fact]
    public void NearestWorkIsTakenFirst()
    {
        long clock = 0;
        ViewerSet viewers = JobTestHelpers.ViewerAt(0);
        JobQueue queue = JobTestHelpers.QueueWithClock(viewers, () => clock);

        FakeJob far = JobTestHelpers.At(50);
        FakeJob near = JobTestHelpers.At(1);
        queue.Enqueue(far);
        queue.Enqueue(near);
        queue.Sweep();

        Assert.True(queue.TryTake(out JobQueue.Entry taken));
        Assert.Same(near, taken.Job);
    }

    /// <summary>
    /// The test that a FIFO cannot pass, and the reason docs/DESIGN.md no longer specifies
    /// <c>System.Threading.Channels</c> for this queue.
    /// </summary>
    [Fact]
    public void MovingTheViewerReordersAlreadyQueuedWork()
    {
        long clock = 0;
        ViewerSet viewers = JobTestHelpers.ViewerAt(0);
        JobQueue queue = JobTestHelpers.QueueWithClock(viewers, () => clock);

        // `near` is submitted first, so any FIFO returns it first no matter what happens
        // afterwards.
        FakeJob near = JobTestHelpers.At(1);
        FakeJob far = JobTestHelpers.At(50);
        queue.Enqueue(near);
        queue.Enqueue(far);

        // The player crosses the map while both jobs sit in the queue.
        viewers.UpdateSingle(new ChunkPos(50, 0, 0));
        queue.Sweep();

        Assert.True(queue.TryTake(out JobQueue.Entry taken));
        Assert.Same(far, taken.Job);
    }

    [Fact]
    public void CancelledWorkIsDroppedBySweepAndStillReported()
    {
        // Cancellation is not deletion: the chunk that queued this is waiting to hear that
        // its job ended, and stays flagged as in-progress forever if it never does.
        long clock = 0;
        var dropped = new List<JobQueue.Entry>();
        JobQueue queue = JobTestHelpers.QueueWithClock(
            JobTestHelpers.ViewerAt(0), () => clock, dropped.Add);

        using var cts = new CancellationTokenSource();
        FakeJob doomed = JobTestHelpers.At(1);
        FakeJob survivor = JobTestHelpers.At(2);
        queue.Enqueue(doomed, cts.Token);
        queue.Enqueue(survivor);

        cts.Cancel();
        queue.Sweep();

        Assert.Single(dropped);
        Assert.Same(doomed, dropped[0].Job);
        Assert.Equal(1, queue.Count);

        Assert.True(queue.TryTake(out JobQueue.Entry taken));
        Assert.Same(survivor, taken.Job);
        Assert.Equal(0, doomed.RunCount);
    }

    [Fact]
    public void SweepDoesNotRunUntilTheIntervalElapses()
    {
        // Re-sorting on every push is the expensive mistake this queue exists to avoid.
        long clock = 0;
        ViewerSet viewers = JobTestHelpers.ViewerAt(0);
        JobQueue queue = JobTestHelpers.QueueWithClock(viewers, () => clock, sweepIntervalMillis: 200);

        FakeJob near = JobTestHelpers.At(1);
        FakeJob far = JobTestHelpers.At(50);
        queue.Enqueue(near);
        queue.Enqueue(far);

        // Establish a sorted order while the viewer is still at the origin. Without this
        // the queue is merely in arrival order — ordering comes from the sweep.
        queue.Sweep();

        // Move the viewer, but do not let enough time pass for the next sweep.
        viewers.UpdateSingle(new ChunkPos(50, 0, 0));
        clock = 199;

        Assert.True(queue.TryTake(out JobQueue.Entry stale));
        Assert.Same(near, stale.Job);

        // Now let the interval elapse; the queue notices the world moved.
        queue.Enqueue(near);
        clock = 500;

        Assert.True(queue.TryTake(out JobQueue.Entry fresh));
        Assert.Same(far, fresh.Job);
    }

    [Fact]
    public void SerialWorkDoesNotOverlapWithOtherSerialWork()
    {
        long clock = 0;
        JobQueue queue = JobTestHelpers.QueueWithClock(JobTestHelpers.ViewerAt(0), () => clock);

        queue.Enqueue(JobTestHelpers.At(1), serial: true);
        queue.Enqueue(JobTestHelpers.At(2), serial: true);

        Assert.True(queue.TryTake(out _));

        // The second serial entry is still queued, but not available while the first runs.
        Assert.Equal(1, queue.Count);
        Assert.False(queue.TryTake(out _));

        queue.ReleaseSerial();
        Assert.True(queue.TryTake(out _));
    }

    [Fact]
    public void SerialWorkDoesNotBlockParallelWork()
    {
        // The point of the serial lane is to stop disk work clogging every thread, not to
        // stop the pool.
        long clock = 0;
        JobQueue queue = JobTestHelpers.QueueWithClock(JobTestHelpers.ViewerAt(0), () => clock);

        // The serial job is nearer, so after a sweep it outranks the parallel one.
        FakeJob serialJob = JobTestHelpers.At(1);
        FakeJob parallelJob = JobTestHelpers.At(2);
        queue.Enqueue(serialJob, serial: true);
        queue.Enqueue(parallelJob);
        queue.Sweep();

        Assert.True(queue.TryTake(out JobQueue.Entry first));
        Assert.Same(serialJob, first.Job);

        // The serial lane is now occupied — and the parallel job is still available anyway.
        Assert.True(queue.TryTake(out JobQueue.Entry second));
        Assert.Same(parallelJob, second.Job);
    }

    [Fact]
    public void DropAllReportsEverythingStillQueued()
    {
        long clock = 0;
        var dropped = new List<JobQueue.Entry>();
        JobQueue queue = JobTestHelpers.QueueWithClock(
            JobTestHelpers.ViewerAt(0), () => clock, dropped.Add);

        queue.Enqueue(JobTestHelpers.At(1));
        queue.Enqueue(JobTestHelpers.At(2));
        queue.Enqueue(JobTestHelpers.At(3));

        queue.DropAll();

        Assert.Equal(3, dropped.Count);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void BatchEnqueueMatchesIndividualEnqueue()
    {
        long clock = 0;
        ViewerSet viewers = JobTestHelpers.ViewerAt(0);
        JobQueue queue = JobTestHelpers.QueueWithClock(viewers, () => clock);

        FakeJob near = JobTestHelpers.At(1);
        FakeJob far = JobTestHelpers.At(50);
        queue.EnqueueBatch([queue.MakeEntry(far), queue.MakeEntry(near)]);
        queue.Sweep();

        Assert.Equal(2, queue.Count);
        Assert.True(queue.TryTake(out JobQueue.Entry taken));
        Assert.Same(near, taken.Job);
    }

    [Fact]
    public void BeforeAnySweepTheQueueIsInArrivalOrder()
    {
        // Pins the documented contract rather than leaving it as folklore: sorting happens
        // in the sweep, never on insert, because per-insert ordering is the cost this
        // design exists to avoid. Newest-first is a fine approximation in the meantime —
        // the chunk queued most recently is usually the nearest one.
        long clock = 0;
        JobQueue queue = JobTestHelpers.QueueWithClock(JobTestHelpers.ViewerAt(0), () => clock);

        FakeJob near = JobTestHelpers.At(1);
        FakeJob far = JobTestHelpers.At(50);
        queue.Enqueue(near);
        queue.Enqueue(far);

        Assert.True(queue.TryTake(out JobQueue.Entry taken));
        Assert.Same(far, taken.Job);

        // And the very next sweep puts things right.
        queue.Enqueue(far);
        queue.Sweep();
        Assert.True(queue.TryTake(out JobQueue.Entry sorted));
        Assert.Same(near, sorted.Job);
    }

    // --- The sweep's account of itself: how often "every 200 ms" actually fires. ---
    //
    // The interval is a floor and not a schedule — the sweep runs inside a worker's take — so
    // the only way to know how often it fired is to have counted, and these pin the count.

    [Fact]
    public void ADueSweepCountsItselfAndWhatItTouched()
    {
        long clock = 0;
        JobQueue queue = JobTestHelpers.QueueWithClock(JobTestHelpers.ViewerAt(0), () => clock);

        using var cts = new CancellationTokenSource();
        queue.Enqueue(JobTestHelpers.At(1));
        queue.Enqueue(JobTestHelpers.At(2), cts.Token);
        queue.Enqueue(JobTestHelpers.At(3));
        cts.Cancel();

        Assert.Equal(0, queue.Statistics.Sweeps);

        clock = 200;
        Assert.True(queue.TryTake(out _));

        SweepStatistics stats = queue.Statistics;
        Assert.Equal(1, stats.Sweeps);
        Assert.Equal(0, stats.ForcedSweeps);
        Assert.Equal(0, stats.EmptySweeps);

        // The cancelled entry is dropped, not re-evaluated; the peak counts it, because it was
        // in the list the sweep walked.
        Assert.Equal(2, stats.EntriesReevaluated);
        Assert.Equal(1, stats.EntriesDropped);
        Assert.Equal(3, stats.MostEntriesInOneSweep);

        Assert.Equal(1, stats.TakeCalls);
        Assert.Equal(1, stats.TakesThatFoundWork);
        Assert.Equal(200, stats.GapMillisTotal);
        Assert.Equal(200, stats.GapMillisMax);
        Assert.True(stats.SweepMillisTotal >= stats.SweepMillisMax);
    }

    [Fact]
    public void ATakeInsideTheIntervalIsAChanceThatDidNotFire()
    {
        // TakeCalls is the denominator: every take is a chance for a sweep, and a run where the
        // count is high and the sweeps are few is workers polling a queue that is not due.
        long clock = 0;
        JobQueue queue = JobTestHelpers.QueueWithClock(JobTestHelpers.ViewerAt(0), () => clock);
        queue.Enqueue(JobTestHelpers.At(1));
        queue.Enqueue(JobTestHelpers.At(2));

        clock = 199;
        Assert.True(queue.TryTake(out _));
        Assert.True(queue.TryTake(out _));
        Assert.False(queue.TryTake(out _));

        SweepStatistics stats = queue.Statistics;
        Assert.Equal(0, stats.Sweeps);
        Assert.Equal(3, stats.TakeCalls);
        Assert.Equal(2, stats.TakesThatFoundWork);
    }

    [Fact]
    public void AForcedSweepIsCountedAsForcedAndAnEmptyOneAsEmpty()
    {
        // An empty sweep is a real event — it took the lock and moved the clock — and it is
        // what eight idle workers polling every 100 ms produce all day. Counting it apart from
        // the ones that did work is what lets a rate be read as cost.
        long clock = 0;
        JobQueue queue = JobTestHelpers.QueueWithClock(JobTestHelpers.ViewerAt(0), () => clock);

        queue.Sweep();
        clock = 200;
        Assert.False(queue.TryTake(out _));

        SweepStatistics stats = queue.Statistics;
        Assert.Equal(2, stats.Sweeps);
        Assert.Equal(1, stats.ForcedSweeps);
        Assert.Equal(2, stats.EmptySweeps);
        Assert.Equal(0, stats.EntriesReevaluated);
        Assert.Equal(0, stats.MostEntriesInOneSweep);
    }

    [Fact]
    public void TheLongestGapIsReadOffTheQueuesOwnClock()
    {
        // The gap is how stale priorities were allowed to go, and it can exceed the interval by
        // any amount: nothing sweeps until something takes.
        long clock = 0;
        JobQueue queue = JobTestHelpers.QueueWithClock(JobTestHelpers.ViewerAt(0), () => clock);
        queue.Enqueue(JobTestHelpers.At(1));
        queue.Enqueue(JobTestHelpers.At(2));
        queue.Enqueue(JobTestHelpers.At(3));

        clock = 250;
        Assert.True(queue.TryTake(out _));
        clock = 300;
        Assert.True(queue.TryTake(out _));
        clock = 1000;
        Assert.True(queue.TryTake(out _));

        SweepStatistics stats = queue.Statistics;
        Assert.Equal(2, stats.Sweeps);
        Assert.Equal(1000, stats.GapMillisTotal);
        Assert.Equal(750, stats.GapMillisMax);
        Assert.Equal(500.0, stats.MeanGapMillis);
    }

    [Fact]
    public void SinceSubtractsTheCountsAndCarriesThePeaks()
    {
        // A profile window is the difference of two snapshots, and a peak has no window: the
        // sweep that saw the most entries is the fill's first, and it is wanted whichever window
        // is being read.
        long clock = 0;
        JobQueue queue = JobTestHelpers.QueueWithClock(JobTestHelpers.ViewerAt(0), () => clock);
        queue.Enqueue(JobTestHelpers.At(1));
        queue.Enqueue(JobTestHelpers.At(2));
        queue.Enqueue(JobTestHelpers.At(3));

        clock = 200;
        Assert.True(queue.TryTake(out _));
        SweepStatistics earlier = queue.Statistics;

        clock = 400;
        Assert.True(queue.TryTake(out _));
        SweepStatistics window = queue.Statistics.Since(earlier);

        Assert.Equal(1, window.Sweeps);
        Assert.Equal(2, window.EntriesReevaluated);
        Assert.Equal(1, window.TakeCalls);
        Assert.Equal(1, window.TakesThatFoundWork);
        Assert.Equal(200, window.GapMillisTotal);
        Assert.Equal(3, window.MostEntriesInOneSweep);
        Assert.Equal(200, window.GapMillisMax);
    }
}
