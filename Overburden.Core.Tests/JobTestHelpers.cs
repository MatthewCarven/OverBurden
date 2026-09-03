using Overburden.Core.Jobs;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>A job that records that it ran, and optionally does something while running.</summary>
internal sealed class FakeJob(
    ChunkPos position,
    JobKind kind = JobKind.Mesh,
    Action<CancellationToken>? body = null) : IJob
{
    private int _runCount;

    public JobKind Kind { get; } = kind;

    public ChunkPos Position { get; } = position;

    /// <summary>How many times <see cref="Run"/> was entered. Safe to read after a join.</summary>
    public int RunCount => Volatile.Read(ref _runCount);

    public void Run(CancellationToken cancellation)
    {
        Interlocked.Increment(ref _runCount);
        body?.Invoke(cancellation);
    }

    public override string ToString() => $"FakeJob({Kind}, {Position})";
}

internal static class JobTestHelpers
{
    /// <summary>A job at (x, 0, 0), which is <c>x</c> chunks from the origin.</summary>
    public static FakeJob At(int x, JobKind kind = JobKind.Mesh) => new(new ChunkPos(x, 0, 0), kind);

    public static ViewerSet ViewerAt(int x, int y = 0, int z = 0)
    {
        var viewers = new ViewerSet();
        viewers.UpdateSingle(new ChunkPos(x, y, z));
        return viewers;
    }

    /// <summary>
    /// A queue whose sweep is driven by a caller-controlled clock, so tests never depend on
    /// wall time. Advance <paramref name="clock"/> past the interval to make a sweep due.
    /// </summary>
    public static JobQueue QueueWithClock(
        ViewerSet viewers,
        Func<long> clock,
        Action<JobQueue.Entry>? onDropped = null,
        int sweepIntervalMillis = 200) =>
        new(viewers,
            TimeSpan.FromMilliseconds(sweepIntervalMillis),
            onDropped ?? (static _ => { }),
            clock);
}
