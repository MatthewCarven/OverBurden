using MinerCraft.Core.Voxels;
using MinerCraft.Core.Worldgen;

namespace MinerCraft.Core.Streaming;

/// <summary>
/// Where a chunk is in the load pipeline.
/// </summary>
/// <remarks>
/// <para>
/// <b>One enum, not a scatter of <c>bool</c>s.</b> Three flags spanning
/// "queued"/"generating"/"meshing" can express eight combinations, of which two are
/// meaningful and six are bugs waiting to be represented. An enum can only ever hold one
/// of these.
/// </para>
/// <para>
/// The dedup flag lives here too. godot_voxel keeps an <c>is_in_update_list</c> bool on the
/// block rather than probing a <c>HashSet</c>, on the grounds that a flag living on the
/// thing it describes cannot drift out of sync with it. Same idea, taken further: "already
/// queued" is <see cref="Queued"/>, so it cannot disagree with the chunk's actual state at
/// all.
/// </para>
/// </remarks>
public enum ChunkState : byte
{
    /// <summary>Nothing has happened yet. The state a chunk is created in and returns to.</summary>
    Unloaded = 0,

    /// <summary>A generation job has been submitted but has not started.</summary>
    Queued,

    /// <summary>A worker is generating this chunk's voxels.</summary>
    Generating,

    /// <summary>Voxels exist. Not yet meshed, so nothing is drawn.</summary>
    Generated,

    /// <summary>A worker is building this chunk's mesh.</summary>
    Meshing,

    /// <summary>Mesh built and handed over. The steady state.</summary>
    Meshed,
}

/// <summary>
/// One chunk's streaming bookkeeping: who wants it, how far along it is, and its voxels
/// once they exist.
/// </summary>
public sealed class StreamedChunk
{
    internal StreamedChunk(ChunkPos position)
    {
        Position = position;
    }

    public ChunkPos Position { get; }

    /// <summary>
    /// How many viewers currently want this chunk. Zero means it should unload.
    /// </summary>
    /// <remarks>
    /// <b>Refcounted rather than owned by "the player".</b> Built at M2 with exactly one
    /// viewer, because it is a handful of lines now and a rewrite of the streaming policy
    /// later, and because it is the only thing stopping two players in the same area from
    /// unloading each other's terrain.
    /// </remarks>
    public int ViewCount { get; internal set; }

    public ChunkState State { get; private set; } = ChunkState.Unloaded;

    /// <summary>How far worldgen has taken this chunk. Advanced under the stage barrier.</summary>
    public GenerationStage Stage { get; internal set; } = GenerationStage.None;

    /// <summary>
    /// The voxels, once generation has produced them. Null until <see cref="ChunkState.Generated"/>.
    /// </summary>
    public VoxelChunk? Voxels { get; internal set; }

    /// <summary>True when the chunk has voxels a mesher could read.</summary>
    public bool HasVoxels => Voxels is not null;

    /// <summary>
    /// How many submitted jobs have yet to report a result for this chunk.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A chunk with this above zero cannot be forgotten</b>, however unwanted it is. A
    /// worker still holds it and will report, and a result arriving for a chunk the
    /// streamer has already dropped is either a lost update or a crash depending on how the
    /// handler is written.
    /// </para>
    /// <para>
    /// In practice this is only ever 0 or 1 — the states are exclusive, so a chunk cannot be
    /// generating and meshing at once — but it is a count rather than a bool because that
    /// assumption is the pipeline's to keep, not this type's to enforce.
    /// </para>
    /// </remarks>
    public int OutstandingJobs { get; internal set; }

    /// <summary>True while a worker still owes this chunk a result.</summary>
    public bool HasJobInFlight => OutstandingJobs > 0;

    /// <summary>
    /// Cancels the job currently in flight. Replaced per submission, null when idle.
    /// </summary>
    /// <remarks>
    /// Signalled when the view count reaches zero, so that running somewhere and turning
    /// around stops the work rather than merely stopping caring about it. Under continuous
    /// movement, without this, the queue never drains.
    /// </remarks>
    internal CancellationTokenSource? Cancellation { get; set; }

    /// <summary>
    /// Whether this chunk is already sitting in the pipeline's work list.
    /// </summary>
    /// <remarks>
    /// The dedup flag, kept on the chunk rather than as a <c>HashSet</c> probe — the same
    /// reasoning as <see cref="ChunkState"/>, and godot_voxel's <c>is_in_update_list</c>.
    /// It matters most at M3, where one carve dirties the same chunk several times in a
    /// frame and each of its neighbours once per touched face.
    /// </remarks>
    internal bool InWorkList { get; set; }

    /// <summary>Whether <see cref="TransitionTo"/> would accept <paramref name="next"/>.</summary>
    public bool CanTransitionTo(ChunkState next) => IsLegalTransition(State, next);

    /// <summary>
    /// Moves the chunk to <paramref name="next"/>, or throws if that edge does not exist.
    /// </summary>
    /// <remarks>
    /// Throwing rather than clamping or ignoring is the point. An illegal transition means
    /// the caller's model of the pipeline is wrong, and the cheapest moment to find that
    /// out is the instant it happens — not three seconds later when a chunk sits in
    /// <see cref="ChunkState.Meshing"/> forever and no mesh ever arrives.
    /// </remarks>
    internal void TransitionTo(ChunkState next)
    {
        if (!IsLegalTransition(State, next))
        {
            throw new InvalidOperationException(
                $"{Position} cannot go from {State} to {next}.");
        }

        State = next;
    }

    /// <summary>
    /// The edges of the pipeline <c>Unloaded → Queued → Generating → Generated → Meshing →
    /// Meshed</c>, plus the ones that are easy to forget.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three kinds of edge exist beyond the happy path, and leaving any of them out shows
    /// up as a chunk stuck mid-pipeline:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///     <b>Anything to <see cref="ChunkState.Unloaded"/>.</b> A viewer can leave at any
    ///     moment, including while a worker is mid-job.
    ///   </description></item>
    ///   <item><description>
    ///     <b>Back one step, when a job reports <c>Dropped</c>.</b> Cancellation is not
    ///     deletion — a dropped generate leaves the chunk needing generating again, so
    ///     <see cref="ChunkState.Generating"/> must be able to return to
    ///     <see cref="ChunkState.Queued"/>.
    ///   </description></item>
    ///   <item><description>
    ///     <b><see cref="ChunkState.Meshed"/> back to <see cref="ChunkState.Meshing"/>.</b>
    ///     Not needed until M3, when mining dirties an already-meshed chunk, but it is the
    ///     same edge and costs nothing to allow now.
    ///   </description></item>
    /// </list>
    /// <para>
    /// Self-transitions are rejected. Re-entering the state you are already in is never
    /// meaningful here and is usually a double-submit.
    /// </para>
    /// </remarks>
    private static bool IsLegalTransition(ChunkState from, ChunkState to)
    {
        if (from == to)
        {
            return false;
        }

        // A viewer can leave at any point in the pipeline.
        if (to == ChunkState.Unloaded)
        {
            return true;
        }

        return (from, to) switch
        {
            (ChunkState.Unloaded, ChunkState.Queued) => true,
            (ChunkState.Queued, ChunkState.Generating) => true,
            (ChunkState.Generating, ChunkState.Generated) => true,

            // A dropped or faulted generate job returns the chunk to the queue.
            (ChunkState.Generating, ChunkState.Queued) => true,

            (ChunkState.Generated, ChunkState.Meshing) => true,
            (ChunkState.Meshing, ChunkState.Meshed) => true,

            // A dropped or faulted mesh job leaves the voxels intact, so it goes back only
            // as far as Generated.
            (ChunkState.Meshing, ChunkState.Generated) => true,

            // Re-mesh after the voxels change. M3.
            (ChunkState.Meshed, ChunkState.Meshing) => true,

            _ => false,
        };
    }

    public override string ToString() =>
        $"{Position} {State} stage={Stage} views={ViewCount}";
}
