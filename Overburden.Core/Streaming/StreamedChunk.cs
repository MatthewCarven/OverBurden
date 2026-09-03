using Overburden.Core.Lighting;
using Overburden.Core.Voxels;
using Overburden.Core.Worldgen;

namespace Overburden.Core.Streaming;

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
    /// The current <see cref="Voxels"/> instance has been handed to a job, so it must not be
    /// written to again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Set when a mesh job captures the reference — for this chunk and for all 26 of its
    /// neighbours, since a mesh reads a 3×3×3 neighbourhood. An edit that finds this true
    /// clones the chunk, writes to the clone and installs it, leaving the worker's reference
    /// pointing at something nobody will write to again.
    /// </para>
    /// <para>
    /// It is never cleared while a job is outstanding — only by installing a fresh instance —
    /// so a stale <c>true</c> costs one unnecessary clone and a stale <c>false</c> is
    /// impossible. That asymmetry is deliberate: the cheap failure is the one that can happen.
    /// </para>
    /// </remarks>
    public bool VoxelsSharedWithJob { get; internal set; }

    /// <summary>The mesh no longer matches what it was built from.</summary>
    /// <remarks>
    /// <para>
    /// Survives a mesh completing, because an edit that lands <em>while</em> a mesh job is in
    /// flight produces a mesh built from the pre-edit snapshot. Clearing this on completion
    /// rather than on submission would leave that stale mesh on screen with nothing left to
    /// trigger a rebuild.
    /// </para>
    /// <para>
    /// <b>Staleness only. Whether the player is waiting on it is <see cref="RemeshUrgent"/>.</b>
    /// This flag carried both meanings until M5c, which was right while an edit was the only
    /// thing that could invalidate a mesh — light changing is the second thing, and it is the
    /// one that is stale without being urgent.
    /// </para>
    /// </remarks>
    public bool NeedsRemesh { get; internal set; }

    /// <summary>The player is waiting on the rebuild, so it outranks the streaming queue.</summary>
    /// <remarks>
    /// <para>
    /// Set by an edit and never by a relight. Rock the player just broke has to come off the
    /// screen now; light that is 100 ms stale is invisible, and M5-PLAN names ordinary priority
    /// for relights as the cheap mitigation to try before anything structural.
    /// </para>
    /// <para>
    /// <b>A separate flag rather than an inference, because during the initial fill every chunk's
    /// light changes.</b> Deriving urgency from "the light moved" arrives the entire
    /// distance-ordered streaming queue at band3, and M2a's ordering is silently gone — nothing
    /// looks broken, the world just loads in the wrong order.
    /// </para>
    /// </remarks>
    public bool RemeshUrgent { get; internal set; }

    /// <summary>
    /// This chunk's light cells, once anything has built them. Null until then.
    /// </summary>
    /// <remarks>
    /// <b>Owned here and mutated in place by the sim thread's propagator</b>, which is why it is
    /// never handed to a worker: unlike <see cref="Voxels"/> there is no copy-on-write, so a mesh
    /// job gets a 1,000-byte padded copy taken at submission instead. See docs/M5-PLAN.md.
    /// </remarks>
    public ChunkLight? Light { get; internal set; }

    /// <summary>
    /// The 32×32 column of sky heights this chunk sits in, shared with every chunk above and
    /// below it.
    /// </summary>
    /// <remarks>
    /// Refcounted separately from the chunk, on a different axis and over a different object —
    /// it has to survive the vertical unload the chunk refcount performs as the player descends.
    /// The streamer owns the count; this is the cached reference so lighting a chunk is not a
    /// dictionary lookup.
    /// </remarks>
    public SkyColumn? Sky { get; internal set; }

    /// <summary>
    /// Which of the 27 neighbourhood slots had voxels when this chunk's derived light data was
    /// last built, or 0 if it never has been.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Derived light data is built from voxels, not from a stage, and that is what keeps light
    /// off the load margin.</b> Requiring a further stage barrier would add a third margin ring;
    /// reading what voxels exist means the outermost loaded ring builds its data, participates in
    /// propagation, and never meshes — which is exactly the arrangement that makes light exact
    /// inside the visible box at a 2-chunk margin.
    /// </para>
    /// <para>
    /// <b>The rebuild trigger is a neighbour <em>gained</em>, never one lost</b>, and the
    /// asymmetry is the point. A neighbour that appears where this chunk assumed air can seal a
    /// face that was recorded as conducting, and nothing else would ever notice — the symptom is
    /// light leaking through a wall at wherever the load margin happened to be. A neighbour that
    /// goes away takes its light grid with it, so propagation stops at that face whatever the
    /// conductance bit says, and rebuilding for it would only thrash the boundary as a viewer
    /// moves back and forth.
    /// </para>
    /// <para>
    /// <b>Recorded from M5c-1's break-recipe battery: weakening this to <c>present != …</c> is
    /// currently unreachable, and nothing catches it.</b> Nothing pushes a chunk when a
    /// neighbour unloads — <c>OnChunkUnviewed</c> cancels and discards, and <c>Forget</c> does
    /// not push either — so the loss case never reaches this line. The gain case does, because a
    /// neighbour arriving finishes a generate and that pushes all 26. Kept in this form anyway,
    /// because it states the intent and because anything that later pushes a chunk after an
    /// unload makes the difference real. Same shape as M4a's zeroable contact epsilon.
    /// </para>
    /// </remarks>
    internal uint LightNeighbours { get; set; }

    /// <summary>
    /// This chunk has been edited, so regenerating it from the seed would not reproduce it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Set on the first write and <b>never cleared</b>. It cannot be reconstructed afterwards — a
    /// chunk that has already been unloaded and regenerated looks untouched — and it is what says
    /// this chunk must come from the store rather than from the seed.
    /// </para>
    /// <para>
    /// <b>"Differs from the seed" and "differs from disk" are two questions, and this answers only
    /// the first.</b> See <see cref="DirtySinceSave"/>.
    /// </para>
    /// </remarks>
    public bool HasEdits { get; internal set; }

    /// <summary>This chunk no longer matches what is stored for it.</summary>
    /// <remarks>
    /// <para>
    /// <b>The second flag, and the <see cref="NeedsRemesh"/> problem for a third time.</b>
    /// <see cref="HasEdits"/> means "differs from the seed" and is permanent;  this means "differs
    /// from what is on disk" and is transient. One flag carrying both meanings would have to be
    /// cleared on save, and clearing it would then say the chunk matches the seed — so the next
    /// unload would skip it and the edits would go. Where conflating stale and urgent cost a
    /// stale mesh at M5c, conflating these costs a tunnel.
    /// </para>
    /// <para>
    /// <b>Cleared at submission, not on completion, and restored if the job does not land.</b>
    /// Clearing at submission is what makes an edit arriving <em>during</em> the save re-dirty the
    /// chunk — the same reasoning that makes <see cref="NeedsRemesh"/> survive a mesh completing.
    /// Clearing on completion instead would swallow exactly those edits.
    /// </para>
    /// </remarks>
    public bool DirtySinceSave { get; internal set; }

    /// <summary>
    /// The renderer has been handed a mesh for this chunk and has not been told to drop it.
    /// </summary>
    /// <remarks>
    /// A flag rather than an inference from <see cref="State"/>, because the inference stopped
    /// working at M3. Before the <see cref="ChunkState.Meshed"/> — <see cref="ChunkState.Meshing"/>
    /// edge existed, a chunk that was <see cref="ChunkState.Meshing"/> had never delivered
    /// anything, so "has a live mesh" and "is Meshed" were the same question. With re-meshing
    /// they are not: a viewer leaving while an edit remesh is in flight would find the chunk
    /// in <see cref="ChunkState.Meshing"/>, skip the discard, and leave the pre-edit geometry
    /// drawn for terrain that has been unloaded — until the player happens to come back.
    /// </remarks>
    public bool HasLiveMesh { get; internal set; }

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

    /// <summary>Whether this chunk is already sitting in the pipeline's light-build queue.</summary>
    /// <remarks>
    /// The same dedup device as <see cref="InWorkList"/>, and it matters for the same reason one
    /// resolution up: a single carve writes hundreds of voxels into the same handful of chunks,
    /// and each write would otherwise queue another ~120k-read rebuild of data the next write
    /// invalidates again.
    /// </remarks>
    internal bool LightBuildQueued { get; set; }

    /// <summary>
    /// This chunk has everything it needs to be <see cref="GenerationStage.Lit"/> except a
    /// settled propagation.
    /// </summary>
    /// <remarks>
    /// A flag rather than a set membership, on the thing it describes — godot_voxel's
    /// <c>is_in_update_list</c> argument, and the same one <see cref="InWorkList"/> makes.
    /// </remarks>
    internal bool AwaitingLight { get; set; }

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

            // A cache hit. A chunk the streamer kept after its last viewer left still holds its
            // voxels, so it re-enters the pipeline where a load or a generate would have put it
            // rather than where a new record starts — see ChunkStreamer.Forget. The pipeline
            // takes this edge only when HasVoxels is true, and it is the only way a chunk goes
            // straight from Unloaded to having something to mesh.
            (ChunkState.Unloaded, ChunkState.Generated) => true,

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
