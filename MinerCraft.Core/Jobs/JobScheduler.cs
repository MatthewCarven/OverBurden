using System.Threading.Channels;

namespace MinerCraft.Core.Jobs;

/// <summary>
/// Owns the worker threads and the pending-job queue, and hands results back to the sim
/// thread.
/// </summary>
/// <remarks>
/// <para>
/// <b>Dedicated threads, not the .NET thread pool.</b> These are long-lived workers that
/// pull from <see cref="JobQueue"/>. Handing jobs to <c>Task.Run</c> would put ordering
/// back in the hands of a scheduler that knows nothing about voxel priority, which is the
/// entire thing being built here.
/// </para>
/// <para>
/// <b>One pool, two lanes.</b> Disk work is marked serial and runs one at a time, because
/// it contends on the same file anyway; a second pool would only make it compete in a
/// separate queue that is ignorant of everything else. Nothing uses the serial lane until
/// M6 persistence — the flag exists now because retrofitting it means touching every
/// submission site.
/// </para>
/// <para>
/// Results travel back through a <see cref="Channel{T}"/>, which <em>is</em> the right tool
/// for that direction: hand-back really is FIFO and really does not need reprioritising.
/// It was only ever wrong for the pending queue.
/// </para>
/// </remarks>
public sealed class JobScheduler : IDisposable
{
    /// <summary>
    /// Backstop wake-up. Submissions post the semaphore, so this is not the main path —
    /// it exists so a worker blocked behind the serial lane, or waiting on a sweep that
    /// has since come due, retries without needing an event of its own.
    /// </summary>
    private const int IdleWaitMillis = 100;

    private readonly JobQueue _queue;
    private readonly Channel<JobResult> _results;
    private readonly Thread[] _workers;
    private readonly SemaphoreSlim _workAvailable = new(0);
    private readonly CancellationTokenSource _shutdown = new();

    private bool _disposed;

    /// <summary>
    /// Half the cores, never all of them, never zero. Godot's renderer and our sim thread
    /// need room, and a pool sized to <see cref="Environment.ProcessorCount"/> starves them.
    /// </summary>
    public static int DefaultThreadCount { get; } = ComputeDefaultThreadCount();

    public JobScheduler(ViewerSet viewers, int? threadCount = null, TimeSpan? sweepInterval = null)
    {
        _results = Channel.CreateUnbounded<JobResult>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        _queue = new JobQueue(
            viewers,
            sweepInterval ?? TimeSpan.FromMilliseconds(200),
            onDropped: entry => Post(new JobResult(entry.Job, JobOutcome.Dropped)));

        int count = Math.Max(1, threadCount ?? DefaultThreadCount);
        _workers = new Thread[count];
        for (int i = 0; i < count; i++)
        {
            var thread = new Thread(WorkerLoop)
            {
                Name = $"MinerCraft job {i}",
                IsBackground = true,
            };
            _workers[i] = thread;
            thread.Start();
        }
    }

    public int ThreadCount => _workers.Length;

    /// <summary>Jobs waiting to run. Excludes those currently executing.</summary>
    public int PendingCount => _queue.Count;

    public void Submit(IJob job, CancellationToken cancellation = default, bool serial = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _queue.Enqueue(job, cancellation, serial);
        _workAvailable.Release();
    }

    /// <summary>
    /// Submits a whole frame's jobs, taking the queue lock once rather than once per chunk.
    /// </summary>
    public void SubmitBatch(ReadOnlySpan<(IJob Job, CancellationToken Cancellation, bool Serial)> jobs)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (jobs.IsEmpty)
        {
            return;
        }

        var entries = new JobQueue.Entry[jobs.Length];
        for (int i = 0; i < jobs.Length; i++)
        {
            (IJob job, CancellationToken cancellation, bool serial) = jobs[i];
            entries[i] = _queue.MakeEntry(job, cancellation, serial);
        }

        _queue.EnqueueBatch(entries);
        _workAvailable.Release(entries.Length);
    }

    /// <summary>
    /// Hands every finished job to <paramref name="handler"/> and returns how many there
    /// were. Call once per frame from the sim thread.
    /// </summary>
    public int DrainResults(Action<JobResult> handler)
    {
        int count = 0;
        while (_results.Reader.TryRead(out JobResult result))
        {
            handler(result);
            count++;
        }

        return count;
    }

    /// <summary>Forces a priority recompute now instead of waiting for the timer.</summary>
    public void SweepNow() => _queue.Sweep();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shutdown.Cancel();

        // Wake every worker that is parked, so none of them sits out its full timeout.
        _workAvailable.Release(_workers.Length);
        foreach (Thread worker in _workers)
        {
            worker.Join();
        }

        // Only once the workers have stopped, so a job cannot be taken and dropped at the
        // same time. Anything still queued reports its one result here.
        _queue.DropAll();

        _results.Writer.TryComplete();
        _shutdown.Dispose();
        _workAvailable.Dispose();
    }

    private void WorkerLoop()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            if (_queue.TryTake(out JobQueue.Entry entry))
            {
                RunEntry(entry);
                continue;
            }

            try
            {
                _workAvailable.Wait(IdleWaitMillis, _shutdown.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }
    }

    private void RunEntry(JobQueue.Entry entry)
    {
        JobOutcome outcome;
        Exception? error = null;

        try
        {
            // Re-check: the job may have been cancelled between the last sweep and now.
            if (entry.Cancellation.IsCancellationRequested)
            {
                outcome = JobOutcome.Dropped;
            }
            else
            {
                entry.Job.Run(entry.Cancellation);
                outcome = JobOutcome.Completed;
            }
        }
        catch (OperationCanceledException)
        {
            outcome = JobOutcome.Dropped;
        }
        catch (Exception ex)
        {
            // A job that throws must not take the worker thread down with it, and must
            // still report — otherwise its chunk waits forever on a result that never comes.
            outcome = JobOutcome.Faulted;
            error = ex;
        }
        finally
        {
            if (entry.Serial)
            {
                _queue.ReleaseSerial();
                _workAvailable.Release();
            }
        }

        Post(new JobResult(entry.Job, outcome, error));
    }

    private void Post(JobResult result) => _results.Writer.TryWrite(result);

    private static int ComputeDefaultThreadCount()
    {
        int cores = Environment.ProcessorCount;
        int half = (int)Math.Round(cores * 0.5, MidpointRounding.AwayFromZero);
        return Math.Clamp(half, 1, Math.Max(cores - 1, 1));
    }
}
