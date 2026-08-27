using MinerCraft.Core.Voxels;

namespace MinerCraft.Core.Jobs;

/// <summary>A unit of chunk work that runs on a worker thread.</summary>
/// <remarks>
/// Jobs do not compute their own priority. Keeping that policy in
/// <see cref="JobPriority.Evaluate"/> means there is one place to change how work is
/// ordered, and one place to test it.
/// </remarks>
public interface IJob
{
    JobKind Kind { get; }

    /// <summary>Which chunk this job is about — the input to its priority.</summary>
    ChunkPos Position { get; }

    /// <summary>
    /// Does the work. Long jobs should poll <paramref name="cancellation"/> and may throw
    /// <see cref="OperationCanceledException"/>, which is reported as
    /// <see cref="JobOutcome.Dropped"/> rather than as a fault.
    /// </summary>
    void Run(CancellationToken cancellation);
}

/// <summary>How a job ended.</summary>
public enum JobOutcome
{
    /// <summary><see cref="IJob.Run"/> ran to completion.</summary>
    Completed,

    /// <summary>
    /// The job never ran, or abandoned itself partway. Cancelled before being picked up,
    /// or still queued when the scheduler shut down.
    /// </summary>
    Dropped,

    /// <summary><see cref="IJob.Run"/> threw. <see cref="JobResult.Exception"/> holds what.</summary>
    Faulted,
}

/// <summary>What comes back to the sim thread when a job ends.</summary>
/// <remarks>
/// <para>
/// <b>Every submitted job produces exactly one of these — never zero, never two.</b> A
/// cancelled job that vanishes silently leaves its chunk flagged as work-in-progress
/// forever, and that chunk then never updates again. Cancellation is not deletion; see
/// docs/M2-REFERENCE-NOTES.md, where godot_voxel reports dropped mesh tasks for the same
/// reason.
/// </para>
/// <para>This invariant is what <c>EverySubmittedJobReportsExactlyOnce</c> fuzzes.</para>
/// </remarks>
public readonly record struct JobResult(IJob Job, JobOutcome Outcome, Exception? Exception = null)
{
    public override string ToString() =>
        Exception is null ? $"{Outcome} {Job.Kind} {Job.Position}"
                          : $"{Outcome} {Job.Kind} {Job.Position}: {Exception.Message}";
}
