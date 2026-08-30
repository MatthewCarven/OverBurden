using System.Diagnostics;
using System.Runtime.InteropServices;
using MinerCraft.Core.Content;
using MinerCraft.Core.Jobs;
using MinerCraft.Core.Lighting;
using MinerCraft.Core.Meshing;
using MinerCraft.Core.Voxels;
using MinerCraft.Core.Worldgen;

namespace MinerCraft.Core.Streaming;

/// <summary>
/// Drives chunks through <c>Unloaded → Queued → Generating → Generated → Meshing → Meshed</c>
/// as viewers move: submits jobs, applies results, and cancels work nobody wants any more.
/// </summary>
/// <remarks>
/// <para>
/// <b>A work list, not a per-frame scan.</b> Deciding what each chunk needs by sweeping all
/// of them is O(n) per frame with n in the tens of thousands. Instead a chunk is pushed onto
/// a list exactly when something might have changed for it — it was viewed, its job
/// reported, or a neighbour advanced a stage — and the pump only looks at those. The dedup
/// flag lives on the chunk, so pushing the same chunk twice in a frame costs nothing.
/// </para>
/// <para>
/// <b>Everything here runs on the sim thread.</b> Workers only ever read the snapshot handed
/// to them and write their own result field; every state transition, refcount and dictionary
/// touch happens in <see cref="Update"/>.
/// </para>
/// </remarks>
public sealed class ChunkPipeline : IVoxelEditor, IDisposable
{
    /// <summary>How long one frame may spend applying finished jobs.</summary>
    /// <remarks>
    /// A frame at 60 Hz is 16.7 ms and the apply pass averages well under one, so this is
    /// not a throttle on normal operation — it clips the tail. Measured before it existed,
    /// applying results sat at 0.42 ms mean and 2.04 ms p99 while a single frame reached
    /// 27.6 ms, and that one frame is the entire problem.
    /// </remarks>
    public static readonly TimeSpan DefaultResultBudget = TimeSpan.FromMilliseconds(2.0);

    /// <summary>How long one frame may spend deriving and propagating light.</summary>
    /// <remarks>
    /// <para>
    /// The third of the pipeline's budgets, and the one the plan under-estimated. M5-PLAN costed
    /// only the propagation — ~779k cell visits, ~15 ms for the initial fill at r4h/2v — and
    /// <b>deriving is the larger half by an order of magnitude</b>.
    /// </para>
    /// <para>
    /// <b>Measured in Release, per chunk</b>, against the shipped seed with caves on:
    /// </para>
    /// <code>
    ///              copy      derive    total
    ///   surface    0.189     0.056     0.245 ms
    ///   deep       0.177     0.150     0.327 ms
    ///   open sky   0.155     0.049     0.204 ms
    /// </code>
    /// <para>
    /// So ~0.27 ms a chunk and ~0.41 s for the whole 1,521-chunk fill — against generation's
    /// 0.47 ms a chunk, except that this is one sim thread rather than eight workers.
    /// </para>
    /// <para>
    /// <b>4 ms because the standing start says so, swept headlessly at r4h/2v.</b> The question
    /// is when the world can first draw anything, since nothing meshes until its neighbourhood is
    /// lit. At the 5 s report:
    /// </para>
    /// <code>
    ///   1 ms    857 / 1521 derived, 664 queued,   0 drawn
    ///   2 ms   1467 / 1521 derived,  54 queued,   0 drawn
    ///   4 ms   1521 / 1521 derived,   0 queued, 165 drawn, settled
    ///   8 ms   identical to 4 ms
    /// </code>
    /// <para>
    /// So 4 ms restores the pre-M5c "settled inside 5 s" and 8 ms buys nothing, because light
    /// has stopped being the constraint. <b>It is a ceiling and not a reservation</b>: once the
    /// world is lit there is nothing queued and the step costs nothing, so the 24% of a 60 Hz
    /// frame this permits is spent during a load and never afterwards.
    /// </para>
    /// <para>
    /// <b>The lever, if M5d wants one, is <see cref="PaddedChunk.CopyFrom"/> and not the
    /// derivation</b> — the copy is 60–75% of every row above, and it recomputes the neighbour
    /// index per voxel where 27 block copies would do. That would speed up meshing too, since a
    /// mesh job builds one of these per chunk as well. <b>Not built yet, because raising the
    /// budget already bought back the whole regression.</b>
    /// </para>
    /// </remarks>
    public static readonly TimeSpan DefaultLightBudget = TimeSpan.FromMilliseconds(4.0);

    private static readonly double TicksPerMillisecond = Stopwatch.Frequency / 1000.0;

    private readonly ChunkStreamer _streamer;
    private readonly JobScheduler _scheduler;
    private readonly WorldGenerator _generator;
    private readonly MaterialTable _materials;
    private readonly Action<StreamedChunk, ChunkMesh>? _onMeshReady;
    private readonly Action<ChunkPos>? _onMeshDiscarded;
    private readonly Action<ChunkPos, Exception>? _onJobFaulted;

    private readonly Queue<ChunkPos> _workList = new();
    private readonly List<(IJob Job, CancellationToken Cancellation, bool Serial)> _batch = [];
    private readonly Func<long> _nowTicks;
    private readonly long _resultBudgetTicks;
    private readonly long _lightBudgetTicks;
    private readonly bool _ambientOcclusion;
    private readonly bool _light;
    private bool _disposed;

    private readonly LightPropagator _propagator;

    /// <summary>Chunks whose derived light data needs building or rebuilding.</summary>
    /// <remarks>
    /// Separate from <see cref="_workList"/> because it is budgeted separately: one derive is
    /// ~120k voxel reads, and a swing that dirties eight chunks would otherwise spend most of a
    /// frame inside the pump. The dedup flag lives on the chunk, the same as
    /// <see cref="StreamedChunk.InWorkList"/> and for the same reason.
    /// </remarks>
    private readonly Queue<ChunkPos> _lightBuilds = new();

    /// <summary>
    /// Chunks that have everything they need to be <see cref="GenerationStage.Lit"/> except a
    /// settled propagation.
    /// </summary>
    /// <remarks>
    /// <b>They cannot simply be re-examined when the drain finishes, because nothing would push
    /// them</b> — the work list is fed by things happening to a chunk, and "a global queue
    /// somewhere else became empty" happens to no chunk in particular. Scanning every chunk
    /// instead is the O(n)-per-frame sweep the work list exists to avoid.
    /// </remarks>
    private readonly List<ChunkPos> _awaitingLight = [];

    /// <summary>Cells the propagator moved this frame, and the chunks that sample them.</summary>
    /// <remarks>
    /// Both cleared every frame, which is what bounds them: a budgeted drain writes at most one
    /// frame's worth of cells however large the world is. The chunk set is a set rather than a
    /// list because a changed cell expands to ~2 chunks on average and the same chunk comes up
    /// again and again — deduping here turns tens of thousands of dictionary lookups into one
    /// per chunk actually touched.
    /// </remarks>
    private readonly List<LightCellPos> _lightChanges = [];

    private readonly HashSet<ChunkPos> _relitChunks = [];

    /// <summary>Cells whose derived data moved during one rebuild.</summary>
    private readonly List<LightCellPos> _derivedChanges = [];

    /// <summary>
    /// The 34³ voxel copy the derive reads, and its 27 references. Reused rather than allocated.
    /// </summary>
    /// <remarks>
    /// 157 KB allocated once. The first version of M5b's test fixture allocated one per chunk per
    /// edit and that was most of why a single test could run the host out of memory; the fixture
    /// records the lesson and this is where it is spent.
    /// </remarks>
    private readonly PaddedChunk _lightPadded = new();

    private readonly VoxelChunk?[] _lightNeighbourhood = new VoxelChunk?[PaddedChunk.NeighbourhoodSize];

    /// <summary>The 27 grids a mesh submission copies from. Reused; the copy is immediate.</summary>
    private readonly ChunkLight?[] _lightGrids = new ChunkLight?[PaddedChunk.NeighbourhoodSize];

    /// <param name="onMeshReady">
    /// A chunk's mesh is built and ready to draw. The Godot layer turns it into an
    /// <c>ArrayMesh</c> here; Core never sees a renderer.
    /// </param>
    /// <param name="onMeshDiscarded">
    /// A chunk that had a live mesh is gone. The Godot layer frees the RID here — see the
    /// M2d note about budgeting that, since freeing many at once stutters a frame or two
    /// later.
    /// </param>
    /// <param name="onJobFaulted">A job threw. Reported rather than swallowed.</param>
    /// <param name="resultBudget">
    /// How long one <see cref="Update"/> may spend applying finished jobs before leaving the
    /// rest for the next frame. Defaults to <see cref="DefaultResultBudget"/>. There is no
    /// "unbounded" value on purpose — pass something large if that is genuinely wanted, so
    /// it reads as a number someone chose.
    /// </param>
    /// <param name="nowTicks">
    /// Injectable clock in <see cref="Stopwatch"/> ticks. Tests drive the budget
    /// deterministically rather than racing a real one.
    /// </param>
    /// <param name="ambientOcclusion">
    /// Off restores the constant-brightness merge key every mesh used before M5a. A test seam
    /// for the quad-count A/B, carried here because the mesher is built inside the job.
    /// </param>
    public ChunkPipeline(
        WorldGenerator generator,
        MaterialTable materials,
        JobScheduler scheduler,
        ViewerSet? priorityViewers = null,
        Action<StreamedChunk, ChunkMesh>? onMeshReady = null,
        Action<ChunkPos>? onMeshDiscarded = null,
        Action<ChunkPos, Exception>? onJobFaulted = null,
        int loadMargin = ChunkStreamer.DefaultLoadMargin,
        TimeSpan? resultBudget = null,
        Func<long>? nowTicks = null,
        bool ambientOcclusion = true,
        TimeSpan? lightBudget = null,
        bool light = true)
    {
        _generator = generator;
        _materials = materials;
        _scheduler = scheduler;
        _onMeshReady = onMeshReady;
        _onMeshDiscarded = onMeshDiscarded;
        _onJobFaulted = onJobFaulted;
        _nowTicks = nowTicks ?? Stopwatch.GetTimestamp;
        _ambientOcclusion = ambientOcclusion;
        _light = light;
        _resultBudgetTicks =
            (long)((resultBudget ?? DefaultResultBudget).TotalMilliseconds * TicksPerMillisecond);
        _lightBudgetTicks =
            (long)((lightBudget ?? DefaultLightBudget).TotalMilliseconds * TicksPerMillisecond);

        _streamer = new ChunkStreamer(
            priorityViewers,
            onChunkViewed: OnChunkViewed,
            onChunkUnviewed: OnChunkUnviewed,
            loadMargin,
            // The generator is already ISurfaceHeights, and the skylight seed has to agree with
            // the terrain bit for bit — the same argument that keeps IsCaveAt and GenerateTerrain
            // reading one definition of the cave field.
            heights: generator);

        _propagator = new LightPropagator(_streamer);
    }

    /// <summary>The streaming policy. Viewers are added and moved through here.</summary>
    public ChunkStreamer Streamer => _streamer;

    /// <summary>Chunks waiting for the pump to decide what they need next.</summary>
    public int WorkListCount => _workList.Count;

    /// <summary>Jobs submitted and not yet reported.</summary>
    public int JobsInFlight { get; private set; }

    /// <summary>Jobs that ran to completion since this pipeline was built.</summary>
    public int CompletedJobs { get; private set; }

    /// <summary>
    /// Jobs cancelled or abandoned. High relative to <see cref="CompletedJobs"/> means work
    /// is being queued for terrain the viewer leaves before it is reached — worth watching
    /// at M2d while flying fast.
    /// </summary>
    public int DroppedJobs { get; private set; }

    /// <summary>Jobs that threw. Should be zero; anything else is a bug worth chasing.</summary>
    public int FaultedJobs { get; private set; }

    /// <summary>Results handed back to their chunks since this pipeline was built.</summary>
    public int AppliedResults { get; private set; }

    /// <summary>Voxels written through <see cref="SetVoxel"/> that landed.</summary>
    public int EditedVoxels { get; private set; }

    /// <summary>Chunks cloned so a worker's snapshot stayed valid.</summary>
    /// <remarks>
    /// The cost of copy-on-write, counted rather than assumed. M3-PLAN estimated one ~2 KB
    /// clone per chunk per edit-and-remesh cycle; this is how that estimate gets checked.
    /// </remarks>
    public int ChunkClones { get; private set; }

    /// <summary>Mesh jobs submitted because an edit invalidated a chunk, not because a
    /// viewer moved.</summary>
    /// <remarks>
    /// Remeshes per swing is the number M3d is really asking about — one carve dirties up to
    /// eight chunks, and each is a full 32³ sweep that has to finish between the click and
    /// the eye noticing. Counted here rather than derived from
    /// <see cref="CompletedJobs"/> because the two kinds of mesh job are indistinguishable
    /// once they are done, and only this one is on the critical path.
    /// </remarks>
    public int EditRemeshes { get; private set; }

    /// <summary>Mesh jobs submitted because light moved under an already-drawn chunk.</summary>
    /// <remarks>
    /// <b>The number that decides M5's fragment-shader fallback, alongside the quad count.</b>
    /// The recorded trigger is breaking into a lit space costing more than ~16 chunk remeshes at
    /// a rate mining can sustain. Counted apart from <see cref="EditRemeshes"/> because a relight
    /// remesh is not urgent and the two would otherwise be indistinguishable once done.
    /// </remarks>
    public int RelightRemeshes { get; private set; }

    /// <summary>Chunks whose derived light data has been built or rebuilt.</summary>
    /// <remarks>
    /// Above the chunk count means rebuilds are happening — a neighbour appearing where this
    /// chunk had assumed air, which is the load frontier moving outward. A ratio that climbs is
    /// the signal that <see cref="StreamedChunk.LightNeighbours"/>'s gain-only rule has stopped
    /// being enough.
    /// </remarks>
    public int LightBuilds { get; private set; }

    /// <summary>How many of those took the closed form rather than reading voxels.</summary>
    /// <remarks>
    /// The coverage of the uniform fast path, which M2b's "68% of chunks are uniform" predicts
    /// but does not settle — this one needs the six face neighbours uniform <em>and equal</em>,
    /// which is strictly rarer. Solid rock below and open sky above are the two places it pays,
    /// and they are most of a mining game's world.
    /// </remarks>
    public int UniformLightBuilds { get; private set; }

    /// <summary>Cells the propagator has visited. Its own running total, surfaced here.</summary>
    public long LightCellsVisited => _propagator.CellsVisited;

    /// <summary>What the last <see cref="Update"/>'s light step cost, in milliseconds.</summary>
    /// <remarks>
    /// <para>
    /// <b>A stopwatch around the call, and that is the fifth time this project has had to say
    /// so.</b> The headless frame-time trap has been walked into at M2d-1, at the mesh-apply
    /// budget, at M3d and at M4d, and every time the directly-timed number moved by a tenth of a
    /// millisecond while <c>frame ms</c> reported a spike that did not reproduce. Light is the
    /// first sim-thread cost since meshing that is large enough to be worth confusing with a
    /// frame, so it gets its own number rather than a share of somebody else's.
    /// </para>
    /// <para>
    /// Read off the same injectable clock the budget uses, so a run under
    /// <c>--lightbudget</c> is measuring the thing it is bounding. In a test with a fake clock
    /// this is a fake number; nothing asserts on it.
    /// </para>
    /// </remarks>
    public double LastLightStepMillis { get; private set; }

    /// <summary>Total milliseconds spent deriving conductance, emission and sky flags.</summary>
    /// <remarks>
    /// <b>Split from the walk because M5-PLAN costed the wrong half.</b> It estimated the
    /// propagation at ~779k cell visits and said nothing about deriving, and M5c-1 measured
    /// deriving as the larger of the two by an order of magnitude. One accumulator each is what
    /// makes that statement checkable in a real world rather than on a bench, and it is what
    /// would show <c>PaddedChunk.CopyFrom</c> being fixed.
    /// </remarks>
    public double LightDeriveMillis { get; private set; }

    /// <summary>Total milliseconds spent in the propagation walk.</summary>
    public double LightPropagateMillis { get; private set; }

    /// <summary>Chunks waiting for a derived light build.</summary>
    public int LightBuildBacklog => _lightBuilds.Count;

    /// <summary>Whether the light field is still being repaired.</summary>
    /// <remarks>
    /// False is the condition a chunk needs to reach <see cref="GenerationStage.Lit"/> — see
    /// that stage's remarks for why it is global.
    /// </remarks>
    public bool LightInFlight => _propagator.HasWork || _lightBuilds.Count > 0;

    /// <summary>
    /// Nothing is in flight, nothing is queued, and the light field has settled — the only
    /// moment the pipeline's state is worth asserting on.
    /// </summary>
    /// <remarks>
    /// <b>Owned here rather than assembled by callers, because M5c added two queues to it.</b>
    /// A caller testing only the job count and the work list would see quiescence while every
    /// chunk still sat one stage short of meshing, which is not a wrong answer so much as an
    /// out-of-date one — and it is exactly what a stalled light stage looks like from outside.
    /// </remarks>
    public bool IsQuiescent =>
        JobsInFlight == 0
        && _workList.Count == 0
        && !LightInFlight
        && _awaitingLight.Count == 0;

    /// <summary>Reads one voxel, or reports that its chunk is not there to read.</summary>
    /// <remarks>
    /// Delegated straight to the streamer, which is where the chunks live. It is on the
    /// pipeline as well so that <see cref="IVoxelEditor"/>'s read and write halves come from
    /// one object — a carve that read through <see cref="Streamer"/> and wrote through here
    /// would be two views of the world that a future change could let disagree.
    /// </remarks>
    public bool TryGetMaterial(VoxelPos voxel, out int materialId)
    {
        // Checked here as well as in SetVoxel. One interface whose two halves disagree about
        // whether they are still usable is worse than either answer.
        ObjectDisposedException.ThrowIf(_disposed, this);

        return _streamer.TryGetMaterial(voxel, out materialId);
    }

    /// <summary>
    /// Writes one voxel and marks every chunk whose mesh it invalidates for a rebuild.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Copy-on-write.</b> If the chunk's voxels have been handed to a job, they are cloned
    /// first and the clone is installed — see <see cref="StreamedChunk.VoxelsSharedWithJob"/>.
    /// Mutating in place would not merely show a worker a stale voxel: <see cref="VoxelChunk.Set"/>
    /// can promote the representation and swap the backing arrays under a reader mid-copy.
    /// </para>
    /// <para>
    /// <b>Remeshing is deferred to <see cref="Update"/>, not done here.</b> A carve writes
    /// hundreds of voxels across several chunks, and each one would otherwise queue a rebuild
    /// of work the next write invalidates again.
    /// </para>
    /// </remarks>
    /// <returns>
    /// False when the voxel is in a chunk that does not exist or has not generated yet. The
    /// caller is told rather than the write being dropped silently — a carve reaching past the
    /// loaded region needs to know it was clipped.
    /// </returns>
    public bool SetVoxel(VoxelPos voxel, int materialId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_streamer.TryGet(voxel.Chunk, out StreamedChunk chunk) || chunk.Voxels is null)
        {
            return false;
        }

        (int localX, int localY, int localZ) = voxel.Local;
        if (chunk.Voxels.Get(localX, localY, localZ) == materialId)
        {
            // Nothing changed, so nothing is stale. M3-PLAN justified this by "a sphere
            // carve rewrites air as air across most of its bounding box", which stopped
            // being true at M3c: Carve skips air itself before it ever reaches here. Keep it
            // anyway. It is what protects the *next* writer, and M6's placement is exactly
            // the shape of caller that rewrites what is already there. Deleting it because
            // it no longer fires would be measuring one caller.
            return true;
        }

        if (chunk.VoxelsSharedWithJob)
        {
            chunk.Voxels = chunk.Voxels.Clone();
            chunk.VoxelsSharedWithJob = false;
            ChunkClones++;
        }

        chunk.Voxels.Set(localX, localY, localZ, materialId);
        chunk.HasEdits = true;
        EditedVoxels++;

        RelightAfterEdit(chunk, voxel);

        Span<ChunkPos> dirty = stackalloc ChunkPos[ChunkEdits.MaxDirtyChunks];
        int count = ChunkEdits.DirtyChunks(voxel, dirty);
        for (int i = 0; i < count; i++)
        {
            MarkForRemesh(dirty[i]);
        }

        return true;
    }

    /// <summary>Marks a chunk stale <em>and</em> urgent, which is what an edit is.</summary>
    private void MarkForRemesh(ChunkPos position)
    {
        if (!_streamer.TryGet(position, out StreamedChunk chunk))
        {
            return;
        }

        chunk.NeedsRemesh = true;
        chunk.RemeshUrgent = true;
        Push(position);
    }

    /// <summary>
    /// Brings the light bookkeeping up to date after one voxel changed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The sky column is updated here, per voxel; the derived rebuild is queued, not done.</b>
    /// A carve writes hundreds of voxels across a handful of chunks and each rebuild is ~120k
    /// voxel reads — doing one per write would spend a swing's whole frame budget re-deriving
    /// data the next write invalidates. The column update is a single index and at worst one
    /// downward scan, so it stays inline where it can answer whether the scope widens.
    /// </para>
    /// <para>
    /// Exactly the sequence M5b's <c>LightTestWorld.Edit</c> spells out, which is what that
    /// fixture was written to be: write the voxel, update the sky column, rebuild the affected
    /// chunks collecting the cells that actually moved, invalidate those, drain. If the
    /// incremental result ever disagrees with a from-scratch flood, this is the sequence that is
    /// wrong.
    /// </para>
    /// </remarks>
    private void RelightAfterEdit(StreamedChunk chunk, VoxelPos voxel)
    {
        if (chunk.Sky is null)
        {
            return;
        }

        // The streamer is the world here, so an unloaded voxel reads as opaque and bounds the
        // scan — the same rule the collider uses, and for the same reason.
        bool skyMoved = chunk.Sky.OnVoxelChanged(voxel, _streamer, _materials);

        // Conductance reaches this chunk plus a neighbour across whichever faces the voxel
        // touches. That is the same eight-chunk rule the mesh border uses, because both ask the
        // same question: whose view of this voxel changed?
        Span<ChunkPos> dirty = stackalloc ChunkPos[ChunkEdits.MaxDirtyChunks];
        int count = ChunkEdits.DirtyChunks(voxel, dirty);
        for (int i = 0; i < count; i++)
        {
            QueueLightBuild(dirty[i]);
        }

        if (skyMoved)
        {
            QueueColumnLightBuild(voxel.Chunk);
        }
    }

    /// <summary>
    /// Results finished but not yet applied, because a frame ran out of budget.
    /// </summary>
    /// <remarks>
    /// The number that says whether the budget is right, the same way
    /// <c>MeshFreeQueue.PeakPending</c> does for the free path. A few deep during a fast
    /// flight is the budget working; a figure that climbs and stays up is a backlog.
    /// </remarks>
    public int PendingResults => _scheduler.PendingResults;

    /// <summary>
    /// One frame of pipeline work: apply what the workers finished, up to this frame's
    /// budget, then submit what the chunks need next.
    /// </summary>
    /// <remarks>
    /// Results first, deliberately. Applying them advances states and frees chunks, which
    /// means the submission pass in the same frame sees the newest picture rather than one a
    /// frame stale — and a chunk whose generate landed this frame can have its mesh queued
    /// immediately instead of waiting for the next.
    /// </remarks>
    /// <param name="maxSubmissions">
    /// Ceiling on jobs submitted this frame. Bounds the sim-thread cost of a viewer
    /// teleporting, which would otherwise queue the whole view volume in one go.
    /// </param>
    /// <returns>How many jobs were submitted.</returns>
    public int Update(int maxSubmissions = 256)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        ApplyResults();
        StepLight();
        return Pump(maxSubmissions);
    }

    /// <summary>
    /// One frame of light: derive what is waiting, propagate what that queued, and hand the
    /// chunks it settled back to the pump.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Between the applies and the pump, and both placements matter.</b> After the applies
    /// because that is where chunks are forgotten, and the propagator's chunk cache must not
    /// survive a grid disappearing underneath it — clearing it once here is sufficient precisely
    /// because nothing between this point and the end of <see cref="Update"/> adds or removes a
    /// chunk record. Before the pump so a chunk this frame settled can be meshed in the same
    /// frame rather than the next, which is the same reason the applies come first.
    /// </para>
    /// <para>
    /// <b>Derive before propagate, under one shared slice.</b> A grid that does not exist cannot
    /// propagate, so spending the whole budget on the walk while chunks queue up waiting to be
    /// derived would be the barrier holding on work that is not being done.
    /// </para>
    /// </remarks>
    private void StepLight()
    {
        _propagator.ForgetCache();

        long entered = _nowTicks();
        long deadline = entered + _lightBudgetTicks;

        // Collecting from before the first derive, not just across the drain. A rebuild's
        // Invalidate resets cells to their own source, and a cell that drops on the spot and is
        // never touched again by the walk would otherwise move with nothing reporting it.
        _lightChanges.Clear();
        _propagator.Changes = _lightChanges;

        // Checked after building and never before, so every frame makes progress — the same
        // shape as ApplyResults, and for the same reason: one derive costing more than the whole
        // budget would otherwise be deferred forever and the queue behind it with it.
        while (_lightBuilds.Count > 0)
        {
            BuildLight(_lightBuilds.Dequeue());
            if (_nowTicks() >= deadline) break;
        }

        long derived = _nowTicks();

        _propagator.Drain(_nowTicks, deadline);
        _propagator.Changes = null;

        long drained = _nowTicks();

        MarkRelitChunks();
        ReleaseLightWaiters();

        long left = _nowTicks();

        // Two halves and a total, and the two halves deliberately do not add up to it. What is
        // left over is marking the relit chunks and releasing the waiters, which is bookkeeping
        // proportional to the changed set rather than work belonging to either half — folding it
        // into the walk would inflate exactly the number M5-PLAN already overestimated.
        LightDeriveMillis += (derived - entered) / TicksPerMillisecond;
        LightPropagateMillis += (drained - derived) / TicksPerMillisecond;
        LastLightStepMillis = (left - entered) / TicksPerMillisecond;
    }

    /// <summary>
    /// Rebuilds one chunk's conductance, emission and sky flags, and queues a repair for every
    /// cell that moved.
    /// </summary>
    private void BuildLight(ChunkPos position)
    {
        if (!_streamer.TryGet(position, out StreamedChunk chunk))
        {
            return;
        }

        chunk.LightBuildQueued = false;

        // Re-checked rather than trusted from when it was queued. A neighbour can have been
        // forgotten in between, and deriving against a chunk that has since gone would record
        // conductance against voxels nobody can see.
        if (chunk.Sky is null || !_streamer.TryLightNeighbours(position, out uint present))
        {
            return;
        }

        bool fresh = chunk.Light is null;
        chunk.Light ??= new ChunkLight();

        if (fresh)
        {
            // A grid appearing where there was none invalidates whatever the cache is holding.
            _propagator.ForgetCache();

            // And its neighbours have to push into it again. Everything they sent while it had
            // no grid was dropped on the floor, and nothing else remembers having tried.
            _propagator.SpreadInto(position);
        }

        _derivedChanges.Clear();

        if (TryUniformNeighbourhood(position, out int uniform))
        {
            chunk.Light.BuildDerivedUniform(
                uniform, _materials, position, chunk.Sky, _derivedChanges);
            UniformLightBuilds++;
        }
        else
        {
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                _lightNeighbourhood[PaddedChunk.NeighbourIndex(dx, dy, dz)] =
                    _streamer.Get(position.Offset(dx, dy, dz))?.Voxels;
            }

            // Air for a missing neighbour, matching the mesh job. It cannot matter here: a chunk
            // that is not loaded has no light grid either, so the propagation stops at that face
            // whatever its conductance bit says.
            _lightPadded.CopyFrom(_lightNeighbourhood, MaterialIds.AirNumericId);

            chunk.Light.BuildDerived(
                _lightPadded, _materials, position, chunk.Sky, _derivedChanges);
        }

        chunk.LightNeighbours = present;
        LightBuilds++;

        // Every cell BuildDerived reported, and only those. Its contract is that the set is the
        // exact repair scope — conductance, emission and the sky flag all reach the propagation
        // the same way, so one list covers a tunnel opening, a lamp being placed and a shaft
        // breaking through to daylight with no case analysis. A face closing needs no expansion
        // to the six neighbours because conductance is recorded on both cells of a face, so a
        // changed face always reports both.
        foreach (LightCellPos cell in _derivedChanges)
        {
            _propagator.Invalidate(cell);
        }

        // Nothing else would look at this chunk again. A derive happens off the work list, on
        // its own budgeted queue, so the chunk it just made eligible for GenerationStage.Lit has
        // to be handed back — miss this and the pipeline stalls with every grid built, the
        // propagator idle and no chunk ever meshing.
        Push(position);
    }

    /// <summary>
    /// Whether this chunk and the six across its faces are all one uniform material.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Six, not twenty-six, and that is a property of light rather than an approximation.</b>
    /// Conductance reads one voxel layer across each cell face and emission reads only the
    /// chunk's own interior — nothing in the derivation ever looks at an edge or a corner. The
    /// mesher needs all 26 because vertex AO samples diagonally; light does not.
    /// </para>
    /// <para>
    /// A missing neighbour disqualifies rather than being treated as air, which costs the fast
    /// path at the very edge of the loaded region and keeps this a pure optimisation: the two
    /// build paths must agree, and "absent counts as air" is a rule only one of them would know.
    /// </para>
    /// </remarks>
    private bool TryUniformNeighbourhood(ChunkPos centre, out int material)
    {
        material = MaterialIds.AirNumericId;

        VoxelChunk? own = _streamer.Get(centre)?.Voxels;
        if (own is not { IsUniform: true })
        {
            return false;
        }

        material = own.UniformValue;

        for (int face = 0; face < VoxelFace.Count; face++)
        {
            int[] offset = VoxelFace.Offset[face];
            VoxelChunk? neighbour =
                _streamer.Get(centre.Offset(offset[0], offset[1], offset[2]))?.Voxels;

            if (neighbour is not { IsUniform: true } || neighbour.UniformValue != material)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Marks for rebuild every already-drawn chunk that samples a cell the drain moved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The owner plus, for a cell on a boundary, its neighbours</b> — because a mesh samples
    /// light from a padded grid reaching one cell past each face. Marking only the owner is the
    /// break-recipe and the symptom is a light seam on the chunk grid.
    /// </para>
    /// <para>
    /// The expansion is ~2 chunks per changed cell on average, not 8: an 8³ chunk is 42%
    /// interior, and an interior cell expands to itself alone.
    /// </para>
    /// </remarks>
    private void MarkRelitChunks()
    {
        if (_lightChanges.Count == 0)
        {
            return;
        }

        _relitChunks.Clear();
        Span<ChunkPos> dirty = stackalloc ChunkPos[ChunkEdits.MaxDirtyChunks];

        foreach (LightCellPos cell in _lightChanges)
        {
            int count = ChunkEdits.DirtyChunks(cell, dirty);
            for (int i = 0; i < count; i++)
            {
                _relitChunks.Add(dirty[i]);
            }
        }

        foreach (ChunkPos position in _relitChunks)
        {
            MarkForRelight(position);
        }
    }

    /// <summary>
    /// A chunk whose sampled light moved needs a new mesh — but only if it has drawn one.
    /// </summary>
    /// <remarks>
    /// <b>Not urgent, and only for a chunk that has actually drawn something.</b> A chunk that
    /// has never meshed is held by the barrier until its light exists and will read the current
    /// grid when it does, so marking it buys nothing and costs the priority ordering: during the
    /// initial fill every chunk's light changes, and an urgency inferred from that arrives the
    /// whole distance-ordered queue at band3. Nothing looks broken; the world loads in the wrong
    /// order.
    /// </remarks>
    private void MarkForRelight(ChunkPos position)
    {
        if (!_streamer.TryGet(position, out StreamedChunk chunk))
        {
            return;
        }

        // Meshing counts as drawn: a chunk part-way through a mesh is about to be, from a grid
        // that has since moved. Same shape as NeedsRemesh surviving a mesh completing.
        if (!chunk.HasLiveMesh && chunk.State != ChunkState.Meshing)
        {
            return;
        }

        if (!chunk.NeedsRemesh)
        {
            RelightRemeshes++;
        }

        chunk.NeedsRemesh = true;
        Push(position);
    }

    /// <summary>
    /// Hands back the chunks that were waiting only on the propagation, once it has settled.
    /// </summary>
    private void ReleaseLightWaiters()
    {
        if (_awaitingLight.Count == 0 || LightInFlight)
        {
            return;
        }

        foreach (ChunkPos position in _awaitingLight)
        {
            if (_streamer.TryGet(position, out StreamedChunk chunk))
            {
                chunk.AwaitingLight = false;
            }

            Push(position);
        }

        _awaitingLight.Clear();
    }

    /// <summary>Queues a derived light build, at most once per chunk per build.</summary>
    private void QueueLightBuild(ChunkPos position)
    {
        if (!_streamer.TryGet(position, out StreamedChunk chunk) || chunk.LightBuildQueued)
        {
            return;
        }

        chunk.LightBuildQueued = true;
        _lightBuilds.Enqueue(position);
    }

    /// <summary>
    /// Queues a derived rebuild for every chunk in one chunk column, for a sky height that moved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The two rebuild scopes after an edit are different shapes, and conflating them costs
    /// ~40×.</b> Conductance reaches only the edited chunk plus a neighbour across whichever
    /// faces the voxel actually touches — an interior voxel, 30 in 32, is one chunk. A change to
    /// the column's <em>sky height</em> is not local at all: it moves the sky flag the whole way
    /// up and down that chunk column, and nowhere else.
    /// </para>
    /// <para>
    /// <b>Walking outward until the column stops being tracked is exact, gaps included.</b> With
    /// one viewer the loaded region is a box and the column is contiguous. With two viewers at
    /// different heights it may have a hole — and stopping at the hole is right rather than a
    /// limitation, because a chunk with no light grid is where propagation stops anyway.
    /// </para>
    /// </remarks>
    private void QueueColumnLightBuild(ChunkPos origin)
    {
        for (int y = origin.Y; _streamer.IsTracked(new ChunkPos(origin.X, y, origin.Z)); y++)
        {
            QueueLightBuild(new ChunkPos(origin.X, y, origin.Z));
        }

        for (int y = origin.Y - 1; _streamer.IsTracked(new ChunkPos(origin.X, y, origin.Z)); y--)
        {
            QueueLightBuild(new ChunkPos(origin.X, y, origin.Z));
        }
    }

    /// <summary>
    /// Hands finished jobs back to the chunks that asked for them, for as long as this
    /// frame's budget lasts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Time-sliced, unlike the free path, because here the cost really is spent inside
    /// the call.</b> Applying a finished mesh builds an <c>ArrayMesh</c> in the callback, and
    /// a stopwatch around that measures the actual work. Freeing one does not — Godot defers
    /// the deallocation — which is why <c>MeshFreeQueue</c> counts instead of timing. Two
    /// budgets, two mechanisms, for a reason worth not collapsing.
    /// </para>
    /// <para>
    /// Whatever is left stays in the scheduler's channel and lands next frame. Nothing is
    /// dropped: the results are already paid for, and a result that never arrives leaves its
    /// chunk flagged as work-in-progress forever.
    /// </para>
    /// </remarks>
    private void ApplyResults()
    {
        long deadline = _nowTicks() + _resultBudgetTicks;

        while (_scheduler.TryTakeResult(out JobResult result))
        {
            Apply(result);

            // Checked after applying and never before, so every frame makes progress. A
            // result costing more than the whole budget would otherwise be deferred forever,
            // and the queue behind it with it.
            if (_nowTicks() >= deadline)
            {
                break;
            }
        }
    }

    private void Apply(JobResult result)
    {
        JobsInFlight--;
        AppliedResults++;

        ChunkPos position = result.Job.Position;
        if (!_streamer.TryGet(position, out StreamedChunk chunk))
        {
            // The chunk was forgotten before this landed. Only reachable if something
            // dropped a chunk with a job in flight, which ChunkStreamer.Unview exists to
            // prevent — so this is a guard, not a path.
            return;
        }

        chunk.OutstandingJobs--;
        chunk.Cancellation?.Dispose();
        chunk.Cancellation = null;

        switch (result.Outcome)
        {
            case JobOutcome.Completed:
                CompletedJobs++;
                Complete(chunk, result.Job);
                break;

            case JobOutcome.Faulted:
                // Reported and not retried. A fault is a bug in worldgen or meshing, and
                // bugs are deterministic, so retrying converts one exception into a spin
                // that saturates every worker. The chunk steps back, sits out of the work
                // list and renders as a hole — a visible hole plus a reported exception
                // beats a hung frame. Leaving and returning forgets and retries it.
                FaultedJobs++;
                _onJobFaulted?.Invoke(position, result.Exception!);
                StepBack(chunk, retry: false);
                break;

            default:
                // Dropped. Cancellation is not deletion — the chunk still needs the work
                // doing if anyone still wants it.
                DroppedJobs++;
                StepBack(chunk, retry: true);
                break;
        }

        // A chunk retained past its last viewer purely so this result had somewhere to
        // land is now safe to release.
        if (!chunk.HasJobInFlight)
        {
            _streamer.ForgetIfUnwanted(position);
        }
    }

    private void Complete(StreamedChunk chunk, IJob job)
    {
        switch (job)
        {
            case GenerateChunkJob generate:
                chunk.Voxels = generate.Result;
                chunk.Stage = GenerationStage.Terrain;
                chunk.TransitionTo(ChunkState.Generated);

                // Terrain arriving here may be the last thing 26 neighbours were waiting on
                // before they can run their own feature stage.
                PushWithNeighbours(chunk.Position);
                break;

            case MeshChunkJob mesh:
                chunk.TransitionTo(ChunkState.Meshed);
                if (mesh.Result is not null && chunk.ViewCount > 0)
                {
                    _onMeshReady?.Invoke(chunk, mesh.Result);
                    chunk.HasLiveMesh = true;
                }
                else if (chunk.HasLiveMesh)
                {
                    // A remesh whose chunk lost its last viewer while it ran. Nothing else
                    // will take the old mesh down: Advance refuses a chunk nobody views, and
                    // the record is about to be forgotten entirely.
                    Discard(chunk);
                }

                // An edit that landed while this job was running built its mesh from the
                // pre-edit snapshot, so what just arrived is already stale. Nothing else
                // would push this chunk again — Advance refused it the whole time a job was
                // in flight — and it would sit drawn and wrong until something else moved.
                if (chunk.NeedsRemesh)
                {
                    Push(chunk.Position);
                }

                break;
        }
    }

    /// <summary>
    /// Returns a chunk whose job did not complete to the state it was in before, and
    /// optionally asks for the work again.
    /// </summary>
    private void StepBack(StreamedChunk chunk, bool retry)
    {
        ChunkState back = chunk.State switch
        {
            ChunkState.Generating => ChunkState.Queued,

            // Only as far as Generated: a dropped mesh has not touched the voxels.
            ChunkState.Meshing => ChunkState.Generated,
            _ => chunk.State,
        };

        if (back != chunk.State)
        {
            chunk.TransitionTo(back);
        }

        if (retry && chunk.ViewCount > 0)
        {
            Push(chunk.Position);
        }
    }

    /// <summary>
    /// Decides what each chunk on the work list needs next, and submits it — taking the
    /// queue lock once for the whole frame rather than once per chunk.
    /// </summary>
    private int Pump(int maxSubmissions)
    {
        _batch.Clear();

        while (_batch.Count < maxSubmissions && _workList.Count > 0)
        {
            ChunkPos position = _workList.Dequeue();
            if (!_streamer.TryGet(position, out StreamedChunk chunk))
            {
                continue;
            }

            chunk.InWorkList = false;
            Advance(chunk);
        }

        if (_batch.Count == 0)
        {
            return 0;
        }

        _scheduler.SubmitBatch(CollectionsMarshal.AsSpan(_batch));
        JobsInFlight += _batch.Count;
        return _batch.Count;
    }

    /// <summary>
    /// The one place that decides what a chunk needs. A chunk that cannot proceed simply
    /// falls off the work list and is pushed back by whatever unblocks it.
    /// </summary>
    private void Advance(StreamedChunk chunk)
    {
        // Never two jobs for one chunk, and never work for a chunk nobody wants.
        if (chunk.HasJobInFlight || chunk.ViewCount == 0)
        {
            return;
        }

        // Ahead of the state machine, because light is a stage rather than a state: it has no
        // job, so it is not one of the things a chunk can be doing instead of something else.
        // A chunk with voxels derives them whatever state it is in, the outermost load ring
        // included — see AdvanceLight.
        AdvanceLight(chunk);

        switch (chunk.State)
        {
            case ChunkState.Queued:
                chunk.TransitionTo(ChunkState.Generating);
                Submit(chunk, new GenerateChunkJob(_generator, chunk.Position));
                break;

            case ChunkState.Generated when chunk.Stage == GenerationStage.Terrain:
                if (!_streamer.CanAdvanceToFeatures(chunk.Position))
                {
                    return;
                }

                // Stage 2 runs on the sim thread, not a worker. It writes into neighbouring
                // chunks by design, so running it concurrently with anything reading those
                // neighbours needs a lock over the whole 3×3×3 region — which is real work
                // for a stage that currently places nothing. The revisit trigger is the same
                // one already recorded for chunk storage: M3, the first concurrent writer.
                _generator.ApplyFeatures(chunk.Position, chunk.Voxels!);
                chunk.Stage = GenerationStage.Features;

                // Those 26 neighbours may now have everything they need to mesh.
                PushWithNeighbours(chunk.Position);
                break;

            case ChunkState.Generated when _streamer.CanMesh(chunk.Position):

            // The edit path's re-entry point, and the reason Meshed → Meshing is a legal
            // edge. A chunk that is already drawn goes round again when something it can see
            // changes — its own voxels, or a neighbour's within one voxel of the boundary.
            case ChunkState.Meshed when chunk.NeedsRemesh && _streamer.CanMesh(chunk.Position):
                SubmitMesh(chunk);
                break;
        }
    }

    /// <summary>
    /// Everything light-related a chunk might be ready for: deriving its cells, and the stage
    /// that lets its neighbours mesh.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Deriving asks for voxels, not for a stage, and that is what keeps light off the load
    /// margin.</b> Gating it behind <see cref="GenerationStage.Features"/> everywhere would add a
    /// third rung to the ladder that sets the margin and push it from 2 to 3. Instead the
    /// outermost loaded ring derives its cells and joins the propagation while never reaching
    /// <see cref="GenerationStage.Lit"/> and never meshing — which is exactly the arrangement
    /// that makes light exact inside the visible box at a 2-chunk margin.
    /// </para>
    /// <para>
    /// <b>Revisit trigger, and it is a real one: when <c>ApplyFeatures</c> starts placing
    /// anything, it must route through the relight path the way <see cref="SetVoxel"/> does.</b>
    /// Deriving at <see cref="GenerationStage.Terrain"/> is sound today only because the feature
    /// stage writes nothing; a feature that writes into a neighbour is an edit by another name,
    /// and the machinery to repair it already exists.
    /// </para>
    /// </remarks>
    private void AdvanceLight(StreamedChunk chunk)
    {
        if (!_streamer.TryLightNeighbours(chunk.Position, out uint present))
        {
            return;
        }

        // Never built, or built against a neighbour that was missing and has since arrived.
        // Gain only: a neighbour that goes away takes its grid with it, so propagation stops at
        // that face whatever the conductance bit says, and rebuilding for it would thrash the
        // boundary as a viewer moves back and forth.
        if (chunk.Light is null || (present & ~chunk.LightNeighbours) != 0)
        {
            QueueLightBuild(chunk.Position);
            return;
        }

        if (chunk.Stage != GenerationStage.Features || !chunk.Light.HasDerived)
        {
            return;
        }

        if (LightInFlight)
        {
            // Parked rather than dropped: nothing pushes a chunk when a queue somewhere else
            // goes empty, so without this it would wait for the next unrelated thing to happen
            // to it — which for an interior chunk of a stationary world is never.
            if (!chunk.AwaitingLight)
            {
                chunk.AwaitingLight = true;
                _awaitingLight.Add(chunk.Position);
            }

            return;
        }

        chunk.Stage = GenerationStage.Lit;

        // M2c-2's rule, and light is the case it was written for: light arriving may be the last
        // thing this whole neighbourhood was waiting on before it can mesh. Miss this push and
        // the pipeline stalls silently with everything idle and nothing obviously wrong.
        PushWithNeighbours(chunk.Position);
    }

    private void SubmitMesh(StreamedChunk chunk)
    {
        // Urgent exactly when an edit caused this — never when light merely moved. Streaming
        // meshes are ordered by distance and can wait; a remesh the player is looking at cannot,
        // or the rock they just broke stays on screen while the horizon loads. Light that is
        // 100 ms stale is invisible, which is why a relight does not claim the same band.
        bool urgent = chunk.RemeshUrgent;
        chunk.NeedsRemesh = false;
        chunk.RemeshUrgent = false;

        if (urgent)
        {
            EditRemeshes++;
        }

        chunk.TransitionTo(ChunkState.Meshing);
        Submit(
            chunk,
            new MeshChunkJob(
                chunk.Position,
                Neighbourhood(chunk.Position),
                _materials,
                urgent,
                _ambientOcclusion,
                GatherLight(chunk.Position)));
    }

    /// <summary>
    /// Copies the chunk's light cells and a border of its neighbours' for a mesh job.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A copy taken here, on the sim thread, and not 27 references the worker resolves
    /// later.</b> The voxel neighbourhood can be references because M3a made voxels
    /// copy-on-write; light has no such rule and cannot cheaply have one, since the propagator
    /// writes into those 512-byte arrays in place and carries on doing it while the job runs.
    /// One 1,331-byte copy per job against a clone per chunk per drain.
    /// </para>
    /// <para>
    /// Allocated fresh rather than taken from a reused buffer, because the worker owns it until
    /// the job completes. That is 1,331 bytes against the 157 KB padded voxel copy the worker
    /// still builds for itself — <b>0.8%</b>.
    /// </para>
    /// </remarks>
    private PaddedLight? GatherLight(ChunkPos centre)
    {
        if (!_light)
        {
            return null;
        }

        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
        {
            _lightGrids[PaddedChunk.NeighbourIndex(dx, dy, dz)] =
                _streamer.Get(centre.Offset(dx, dy, dz))?.Light;
        }

        var padded = new PaddedLight();
        padded.CopyFrom(_lightGrids);
        return padded;
    }

    /// <summary>
    /// Snapshots the chunk and its 26 neighbours for a mesh job. References only — the 34³
    /// copy is the worker's job.
    /// </summary>
    private VoxelChunk?[] Neighbourhood(ChunkPos centre)
    {
        var neighbourhood = new VoxelChunk?[PaddedChunk.NeighbourhoodSize];

        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
        {
            StreamedChunk? neighbour = _streamer.Get(centre.Offset(dx, dy, dz));
            neighbourhood[PaddedChunk.NeighbourIndex(dx, dy, dz)] = neighbour?.Voxels;

            // The moment these instances stop being the sim thread's to write to. Every one
            // of the 27, not just the centre, because the worker copies a one-voxel border
            // out of each neighbour and an edit there would change what it reads.
            if (neighbour is not null)
            {
                neighbour.VoxelsSharedWithJob = true;
            }
        }

        return neighbourhood;
    }

    private void Submit(StreamedChunk chunk, IJob job)
    {
        var cancellation = new CancellationTokenSource();
        chunk.Cancellation = cancellation;
        chunk.OutstandingJobs++;
        _batch.Add((job, cancellation.Token, false));
    }

    private void OnChunkViewed(StreamedChunk chunk)
    {
        // A chunk revived mid-job is already past Unloaded and keeps whatever state its
        // cancelled job left it in; the pump picks it up once that job reports.
        if (chunk.State == ChunkState.Unloaded)
        {
            chunk.TransitionTo(ChunkState.Queued);
        }

        Push(chunk.Position);
    }

    private void OnChunkUnviewed(StreamedChunk chunk)
    {
        // Stop the work rather than merely stop wanting it. Without this, running somewhere
        // and turning around leaves the queue full of terrain nobody will ever see, and
        // under continuous movement it never drains.
        chunk.Cancellation?.Cancel();

        // Not "is it Meshed?". A chunk part way through an *edit* remesh is Meshing and has
        // had a mesh drawn for it since before the edit, and that mesh is exactly the one
        // nothing else would ever take down.
        Discard(chunk);
    }

    /// <summary>Tells the renderer to drop this chunk's mesh, if it was ever handed one.</summary>
    private void Discard(StreamedChunk chunk)
    {
        if (!chunk.HasLiveMesh)
        {
            return;
        }

        chunk.HasLiveMesh = false;
        _onMeshDiscarded?.Invoke(chunk.Position);
    }

    private void Push(ChunkPos position)
    {
        if (!_streamer.TryGet(position, out StreamedChunk chunk) || chunk.InWorkList)
        {
            return;
        }

        chunk.InWorkList = true;
        _workList.Enqueue(position);
    }

    /// <summary>
    /// Re-examines a chunk and everything the stage barrier could have unblocked around it.
    /// </summary>
    private void PushWithNeighbours(ChunkPos centre)
    {
        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
        {
            Push(centre.Offset(dx, dy, dz));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (StreamedChunk chunk in _streamer.Chunks)
        {
            chunk.Cancellation?.Cancel();
            chunk.Cancellation?.Dispose();
            chunk.Cancellation = null;
        }
    }
}
