namespace Overburden.Core.Jobs;

/// <summary>
/// A job that is not about a chunk: its result goes to <see cref="OnResult"/> rather than to a
/// chunk record (the water thread, slice 3b).
/// </summary>
/// <remarks>
/// <para>
/// <b>Exists because <c>ChunkPipeline.Apply</c> is chunk-centric.</b> Every job before this one
/// was either about one chunk — generate, mesh, load — or handled by name ahead of the chunk
/// lookup (the save batch, the column restore). A fluid tick is neither, and letting it fall
/// through would decrement <c>OutstandingJobs</c> on whatever chunk its nominal position named,
/// which is the accounting that keeps a chunk from being forgotten with a worker still holding it.
/// </para>
/// <para>
/// <b>Runs on the shared scheduler on purpose.</b> That is what buys it <i>every submitted job
/// reports exactly one result</i>, cancellation at quit and the profiler's counters for free; a
/// second scheduler or a dedicated thread would forfeit all three. <see cref="IJob.Position"/> is
/// still read for priority — the queue orders by distance inside a kind — so a detached job names
/// where the player is.
/// </para>
/// </remarks>
public interface IDetachedJob : IJob
{
    /// <summary>Called on the sim thread, once, when the scheduler reports this job's result.</summary>
    void OnResult(JobOutcome outcome, Exception? error);
}
