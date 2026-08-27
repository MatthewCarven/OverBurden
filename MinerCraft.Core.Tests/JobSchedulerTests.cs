using MinerCraft.Core.Jobs;
using MinerCraft.Core.Voxels;

namespace MinerCraft.Core.Tests;

public class JobSchedulerTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Drains after disposal, which is deterministic: <see cref="JobScheduler.Dispose"/>
    /// joins every worker and drops whatever is still queued, so by the time it returns
    /// every submitted job has reported.
    /// </summary>
    private static List<JobResult> RunToCompletion(JobScheduler scheduler)
    {
        scheduler.Dispose();
        var results = new List<JobResult>();
        scheduler.DrainResults(results.Add);
        return results;
    }

    [Fact]
    public void DefaultThreadCountLeavesRoomForTheRendererAndTheSimThread()
    {
        int cores = Environment.ProcessorCount;

        Assert.InRange(JobScheduler.DefaultThreadCount, 1, Math.Max(cores - 1, 1));
    }

    [Fact]
    public void SubmittedWorkRunsAndReportsCompleted()
    {
        var scheduler = new JobScheduler(JobTestHelpers.ViewerAt(0), threadCount: 2);
        var done = new CountdownEvent(3);
        var jobs = new List<FakeJob>();

        for (int i = 0; i < 3; i++)
        {
            var job = new FakeJob(new ChunkPos(i, 0, 0), JobKind.Generate, _ => done.Signal());
            jobs.Add(job);
            scheduler.Submit(job);
        }

        Assert.True(done.Wait(Patience), "jobs did not run");
        List<JobResult> results = RunToCompletion(scheduler);

        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.Equal(JobOutcome.Completed, r.Outcome));
        Assert.All(jobs, j => Assert.Equal(1, j.RunCount));
    }

    [Fact]
    public void AThrowingJobFaultsWithoutTakingTheWorkerDownWithIt()
    {
        // One worker, so if the exception killed the thread the second job could never
        // report Completed — it would come back Dropped from the shutdown sweep instead.
        var scheduler = new JobScheduler(JobTestHelpers.ViewerAt(0), threadCount: 1);
        var survivorRan = new ManualResetEventSlim();

        var thrower = new FakeJob(
            new ChunkPos(1, 0, 0), body: _ => throw new InvalidOperationException("boom"));
        var survivor = new FakeJob(
            new ChunkPos(2, 0, 0), body: _ => survivorRan.Set());

        scheduler.Submit(thrower);
        scheduler.Submit(survivor);

        Assert.True(survivorRan.Wait(Patience), "the worker thread died with the exception");
        List<JobResult> results = RunToCompletion(scheduler);

        JobResult faulted = Assert.Single(results, r => ReferenceEquals(r.Job, thrower));
        Assert.Equal(JobOutcome.Faulted, faulted.Outcome);
        Assert.IsType<InvalidOperationException>(faulted.Exception);

        JobResult ok = Assert.Single(results, r => ReferenceEquals(r.Job, survivor));
        Assert.Equal(JobOutcome.Completed, ok.Outcome);
    }

    [Fact]
    public void AJobCancelledBeforeItRunsNeverRunsAndReportsDropped()
    {
        var scheduler = new JobScheduler(JobTestHelpers.ViewerAt(0), threadCount: 1);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        FakeJob doomed = JobTestHelpers.At(1);
        scheduler.Submit(doomed, cts.Token);

        List<JobResult> results = RunToCompletion(scheduler);

        JobResult result = Assert.Single(results);
        Assert.Same(doomed, result.Job);
        Assert.Equal(JobOutcome.Dropped, result.Outcome);
        Assert.Equal(0, doomed.RunCount);
    }

    [Fact]
    public void WorkStillQueuedAtShutdownReportsDropped()
    {
        // Queue far more work than one worker can clear in the moment before disposal.
        var scheduler = new JobScheduler(JobTestHelpers.ViewerAt(0), threadCount: 1);
        for (int i = 0; i < 50; i++)
        {
            scheduler.Submit(new FakeJob(new ChunkPos(i, 0, 0), body: _ => Thread.Sleep(2)));
        }

        List<JobResult> results = RunToCompletion(scheduler);

        Assert.Equal(50, results.Count);
        Assert.Contains(results, r => r.Outcome == JobOutcome.Dropped);
    }

    /// <summary>
    /// The conservation invariant, fuzzed. Threading makes exact orderings untestable, so
    /// assert the property that actually matters instead: no job is ever lost, and no job
    /// is ever reported twice.
    /// </summary>
    /// <remarks>
    /// A lost result leaves its chunk flagged as work-in-progress forever, so it never
    /// updates again. A duplicate result would apply a mesh twice. Both are the kind of
    /// bug that surfaces days later as "some chunks just stop working".
    /// </remarks>
    [Fact]
    public void EverySubmittedJobReportsExactlyOnce()
    {
        const int JobCount = 400;
        var rng = new Random(20260827);
        var viewers = JobTestHelpers.ViewerAt(0);
        var scheduler = new JobScheduler(viewers, threadCount: 4, sweepInterval: TimeSpan.FromMilliseconds(1));
        var sources = new List<CancellationTokenSource>();

        for (int i = 0; i < JobCount; i++)
        {
            var cts = new CancellationTokenSource();
            sources.Add(cts);

            int roll = rng.Next(6);
            Action<CancellationToken>? body = roll switch
            {
                0 => _ => throw new InvalidOperationException("fuzz"),
                1 => _ => Thread.Sleep(1),
                2 => token => token.ThrowIfCancellationRequested(),
                _ => null,
            };

            var job = new FakeJob(new ChunkPos(rng.Next(-60, 60), 0, 0), JobKind.Mesh, body);
            scheduler.Submit(job, cts.Token, serial: rng.Next(10) == 0);

            // Cancel some of what was just queued, and move the viewer around, so the
            // sweep is reprioritising and dropping while the workers are draining.
            if (rng.Next(4) == 0)
            {
                sources[rng.Next(sources.Count)].Cancel();
            }

            if (rng.Next(20) == 0)
            {
                viewers.UpdateSingle(new ChunkPos(rng.Next(-60, 60), 0, 0));
            }
        }

        // Let the workers actually drain, so this exercises concurrent running, sweeping
        // and cancelling rather than mostly the shutdown-drop path. Disposing straight
        // after submission still satisfies the invariant, but proves much less.
        DateTime deadline = DateTime.UtcNow + Patience;
        while (scheduler.PendingCount > 0 && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(1);
        }

        List<JobResult> results = RunToCompletion(scheduler);

        Assert.Equal(JobCount, results.Count);

        // The point of draining first: most of these should have actually executed.
        int ran = results.Count(r => r.Outcome is JobOutcome.Completed or JobOutcome.Faulted);
        Assert.True(ran > JobCount / 2, $"expected most jobs to run, only {ran} of {JobCount} did");

        var perJob = new Dictionary<IJob, int>(ReferenceEqualityComparer.Instance);
        foreach (JobResult result in results)
        {
            perJob[result.Job] = perJob.GetValueOrDefault(result.Job) + 1;
        }

        Assert.Equal(JobCount, perJob.Count);
        Assert.All(perJob.Values, count => Assert.Equal(1, count));

        // A job that ran must have run exactly once, never twice.
        Assert.All(perJob.Keys, job => Assert.InRange(((FakeJob)job).RunCount, 0, 1));

        foreach (CancellationTokenSource cts in sources)
        {
            cts.Dispose();
        }
    }

    [Fact]
    public void DisposeIsIdempotent()
    {
        var scheduler = new JobScheduler(JobTestHelpers.ViewerAt(0), threadCount: 1);
        scheduler.Dispose();
        scheduler.Dispose();
    }

    [Fact]
    public void SubmittingAfterDisposeThrows()
    {
        var scheduler = new JobScheduler(JobTestHelpers.ViewerAt(0), threadCount: 1);
        scheduler.Dispose();

        Assert.Throws<ObjectDisposedException>(() => scheduler.Submit(JobTestHelpers.At(1)));
    }
}
