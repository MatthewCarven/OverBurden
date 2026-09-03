using System.Diagnostics;
using System.Runtime.InteropServices;
using Overburden.Core.Content;
using Overburden.Core.Jobs;
using Overburden.Core.Lighting;
using Overburden.Core.Persistence;
using Overburden.Core.Meshing;
using Overburden.Core.Voxels;
using Overburden.Core.Worldgen;

namespace Overburden.Core.Streaming;

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

    /// <summary>
    /// How long <see cref="SaveAll"/> will wait for the serial lane to come out of the store.
    /// </summary>
    /// <remarks>
    /// A safety valve rather than a budget: the wait it exists for is a single job of a few
    /// microseconds, and anything approaching this number means the lane is wedged. Deliberately
    /// long enough that no ordinary run reaches it, and there is no test for the expiry because
    /// exercising it costs five seconds of suite time to prove a branch that only runs when
    /// something else is already broken.
    /// </remarks>
    private const double DrainTimeoutSeconds = 5.0;

    private static readonly double TicksPerMillisecond = Stopwatch.Frequency / 1000.0;

    private readonly ChunkStreamer _streamer;
    private readonly JobScheduler _scheduler;
    private readonly WorldGenerator _generator;
    private readonly MaterialTable _materials;
    private readonly Action<StreamedChunk, ChunkMesh>? _onMeshReady;
    private readonly Action<ChunkPos>? _onMeshDiscarded;
    private readonly Action<ChunkPos, Exception>? _onJobFaulted;
    private readonly Action<VoxelPos, int>? _onVoxelChanged;

    private readonly Queue<ChunkPos> _workList = new();
    private readonly List<(IJob Job, CancellationToken Cancellation, bool Serial)> _batch = [];
    private readonly Func<long> _nowTicks;
    private readonly long _resultBudgetTicks;
    private readonly long _lightBudgetTicks;
    private readonly bool _ambientOcclusion;
    private readonly bool _light;
    private bool _disposed;

    private readonly LightPropagator _propagator;

    /// <summary>Where saved chunks live, or null for a world that is never written.</summary>
    /// <remarks>
    /// <b>Nullable, and that is what keeps every pre-M6 test passing unedited.</b> Six hundred of
    /// them construct a pipeline with no store, and a suite edited in the same pass as the feature
    /// it guards has stopped guarding it.
    /// </remarks>
    private readonly IWorldStore? _store;

    /// <summary>
    /// Which chunks had a stored blob when the world opened, plus everything written since.
    /// </summary>
    /// <remarks>
    /// <b>In memory, read once, because "does this chunk have a blob?" is asked on the sim thread
    /// once per chunk entering the load box</b> — a 37×37 shell at r16. ~5 MB at 100,000 edited
    /// chunks. The rejected alternative is a point query per chunk: 1,521 of them down one serial
    /// lane to fill r4h/2v, nearly all answering no.
    /// </remarks>
    private readonly HashSet<ChunkPos> _savedChunks = [];

    /// <summary>Which sky columns had a stored blob. Same argument as <see cref="_savedChunks"/>.</summary>
    private readonly HashSet<(int X, int Z)> _savedColumns = [];

    /// <summary>Chunks serialised and written by the next save job.</summary>
    /// <remarks>
    /// <b>Holds the <see cref="VoxelChunk"/> itself, not the chunk's position.</b> A chunk queued
    /// here may be forgotten before the job runs — that is the normal case, since this is filled
    /// on unview — and holding the object keeps it alive without any of the retention machinery a
    /// per-chunk job needs.
    /// </remarks>
    private readonly List<(ChunkPos Position, VoxelChunk Voxels)> _pendingChunkSaves = [];

    private readonly List<(int X, int Z, SkyColumn Column)> _pendingColumnSaves = [];

    /// <summary>Columns whose stored heights have been asked for but not yet applied.</summary>
    private readonly HashSet<(int X, int Z)> _columnRestoresInFlight = [];

    /// <summary>At most one save job at a time, so batches are as large as they can be.</summary>
    private bool _saveInFlight;

    /// <summary>Jobs on the serial lane that are inside the store, queued or running.</summary>
    /// <remarks>
    /// <para>
    /// <b>The store belongs to the serial lane, and this is what lets the sim thread know when
    /// the lane is out of it.</b> <see cref="IWorldStore"/>'s contract is one caller at a time,
    /// argued at M6b-1 from every call arriving on that lane — and <see cref="SaveAll"/> is the
    /// one caller that does not. Three job types touch the store: the save, the chunk load and
    /// the column load. <b>The loads are the common case rather than the rare one</b>: re-entering
    /// a dug-in region queues one per chunk, so "is anything in the store right now?" is usually
    /// yes while the player is moving.
    /// </para>
    /// <para>
    /// A plain <c>int</c> and not interlocked, because both ends are the sim thread: submission
    /// happens in <see cref="Pump"/>, <see cref="SubmitSaves"/> and <see cref="OnColumnCreated"/>,
    /// and the decrement happens in <see cref="Apply"/>.
    /// </para>
    /// </remarks>
    private int _storeJobsInFlight;

    /// <summary>Chunks whose derived light data needs building or rebuilding.</summary>
    /// <remarks>
    /// Separate from <see cref="_workList"/> because it is budgeted separately: one derive is
    /// ~120k voxel reads, and a swing that dirties eight chunks would otherwise spend most of a
    /// frame inside the pump. The dedup flag lives on the chunk, the same as
    /// <see cref="StreamedChunk.InWorkList"/> and for the same reason.
    /// </remarks>
    /// <remarks>
    /// <para>
    /// <b>A list drained nearest-first, not a FIFO — the "ground is gone" fix (2026-09-03).</b> In
    /// a played world the chunks round the player are the stored ones, and they arrive last: one
    /// at a time through the serial lane, after the generated ring has landed from eight workers.
    /// A FIFO then derives the far ring first and the player's own neighbourhood last, at two or
    /// three derives a frame in Debug, and nothing meshes until its neighbourhood is lit — so the
    /// centre of a mined-out room is the last thing to appear. Draining by distance to the
    /// nearest viewer is the job queue's own rule, applied to the one queue that did not have it.
    /// </para>
    /// <para>
    /// Sorted lazily: farthest first, so the nearest entry is popped from the end in O(1), and
    /// re-sorted at the next step only when something was added or a viewer moved
    /// (<see cref="ChunkStreamer.ViewerVersion"/>). The whole r6 box is ~2,600 entries, which
    /// sorts in well under a millisecond, and during a fill the sort runs once a frame.
    /// </para>
    /// </remarks>
    private readonly List<ChunkPos> _lightBuilds = [];

    private bool _lightBuildsUnsorted;

    private int _lightBuildsSortedAtViewerVersion = -1;

    /// <summary>
    /// False restores the FIFO the queue was until 2026-09-03 — the control arm for measuring
    /// the order, the way <c>--cavestep=1</c> restores per-voxel caves. Not a setting.
    /// </summary>
    private readonly bool _distanceOrderedDerives;

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
    /// <param name="onVoxelChanged">
    /// Called after a voxel has been written and its light bookkeeping done, with the material
    /// now there. <b>A push, for the reason <c>ChunkLight.Invalidate</c> is one</b>: the fluid
    /// automaton cannot see edits, and a cell nobody pushes never runs. Not called when the
    /// material was already there, because then nothing changed and nothing has anything to do.
    /// Called on the sim thread, once per voxel — a chunk bite is 32,768 calls, so the subscriber
    /// has to be cheap when there is nothing near the voxel to wake.
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
        bool light = true,
        IWorldStore? store = null,
        Action<VoxelPos, int>? onVoxelChanged = null,
        long chunkCacheBytes = 0,
        bool distanceOrderedDerives = true)
    {
        _distanceOrderedDerives = distanceOrderedDerives;
        _generator = generator;
        _materials = materials;
        _scheduler = scheduler;
        _onMeshReady = onMeshReady;
        _onMeshDiscarded = onMeshDiscarded;
        _onJobFaulted = onJobFaulted;
        _onVoxelChanged = onVoxelChanged;
        _nowTicks = nowTicks ?? Stopwatch.GetTimestamp;
        _ambientOcclusion = ambientOcclusion;
        _light = light;
        _resultBudgetTicks =
            (long)((resultBudget ?? DefaultResultBudget).TotalMilliseconds * TicksPerMillisecond);
        _lightBudgetTicks =
            (long)((lightBudget ?? DefaultLightBudget).TotalMilliseconds * TicksPerMillisecond);

        _store = store;

        _streamer = new ChunkStreamer(
            priorityViewers,
            onChunkViewed: OnChunkViewed,
            onChunkUnviewed: OnChunkUnviewed,
            loadMargin,
            // The generator is already ISurfaceHeights, and the skylight seed has to agree with
            // the terrain bit for bit — the same argument that keeps IsCaveAt and GenerateTerrain
            // reading one definition of the cave field.
            heights: generator,
            onColumnCreated: OnColumnCreated,
            onColumnReleased: OnColumnReleased,
            // Zero by default, so every test written before the cache runs the code it was
            // written against; Main passes the cap, and --chunkcache=0 is the control arm.
            chunkCacheBytes: chunkCacheBytes,
            onChunkEvicted: OnChunkEvicted);

        _propagator = new LightPropagator(_streamer);

        if (_store is not null)
        {
            // Read once, at construction, before a single viewer exists. Everything after this
            // point asks the sets rather than the disk.
            foreach (ChunkPos position in _store.LoadChunkIndex()) _savedChunks.Add(position);
            foreach ((int X, int Z) key in _store.LoadColumnIndex()) _savedColumns.Add(key);
        }
    }

    /// <summary>How many chunks the store held when this pipeline opened, plus any written since.</summary>
    public int SavedChunkCount => _savedChunks.Count;

    /// <summary>How many sky columns the store holds.</summary>
    public int SavedColumnCount => _savedColumns.Count;

    /// <summary>Chunks read from the store rather than generated.</summary>
    public int ChunksLoaded { get; private set; }

    /// <summary>Chunks whose voxels came from the seed.</summary>
    /// <remarks>
    /// The third source's counter, beside the store's, so that a turnaround can be read as
    /// "how many chunks came back through the cache" by what the other two did <em>not</em> do.
    /// Until the cache there was no third way for a chunk to acquire voxels, so the generate
    /// count was <see cref="CompletedJobs"/> less the meshes and nobody needed it on its own.
    /// </remarks>
    public int ChunksGenerated { get; private set; }

    /// <summary>Chunk blobs handed to the store.</summary>
    public int ChunksSaved { get; private set; }

    /// <summary>Sky column blobs handed to the store.</summary>
    public int ColumnsSaved { get; private set; }

    /// <summary>Sky columns whose stored heights were applied over the analytic seed.</summary>
    public int ColumnsRestored { get; private set; }

    /// <summary>Bytes the store has been given. Not the file size — blobs replace each other.</summary>
    public long BytesSaved { get; private set; }

    /// <summary>
    /// Save jobs that did not land, whose work was put back on the queue.
    /// </summary>
    /// <remarks>
    /// <b>Re-queued rather than dropped, and that is the opposite of what a faulted generate
    /// does.</b> M2a's rule is that a fault is a deterministic bug and retrying turns one
    /// exception into a spin — true, and the right call when the lost work is a mesh that can be
    /// rebuilt. A dropped save is a deleted tunnel. So this retries, at most once per frame
    /// because saves submit once per frame, and reports every time rather than going quiet.
    /// </remarks>
    public int FailedSaves { get; private set; }

    /// <summary>Chunks and columns waiting for a save job.</summary>
    public int PendingSaves => _pendingChunkSaves.Count + _pendingColumnSaves.Count;

    /// <summary>Save batches handed to the serial lane.</summary>
    /// <remarks>
    /// <b>Batches rather than chunks, because the batch is the unit of transaction</b> — and
    /// dividing <see cref="BytesSaved"/> by chunks while dividing the write time by batches is
    /// how a per-chunk cost gets quoted for something that was never paid per chunk.
    /// </remarks>
    public int SaveBatches { get; private set; }

    /// <summary>The most chunks and columns any one batch carried.</summary>
    /// <remarks>
    /// <b>This is the burst M6's plan was worried about.</b> <c>Forget</c> becoming a write runs
    /// inside <c>ChunkBox.ForEachDifference</c>, which at r16 hands the sim thread a 37x37 shell
    /// in one frame; the mean batch size says what a normal frame costs and only the maximum says
    /// what the worst one did. Reporting the mean alone is the trap the streaming profiler
    /// records for frame time.
    /// </remarks>
    public int LargestSaveBatch { get; private set; }

    /// <summary>
    /// Sim-thread milliseconds spent queueing and submitting saves, cumulative.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The number that says whether the design answer worked.</b> M6-PLAN's headline risk is
    /// that unloading stopped being free: <c>Forget</c> is called synchronously on the sim thread
    /// from inside a box-difference sweep. The answer was to make the write a job, leaving the sim
    /// thread a flag and a list append per chunk — so this should stay near zero however large the
    /// shell, and <see cref="SaveWriteMillis"/> should carry the whole of the cost. If the two ever
    /// converge, the write has come back onto the sim thread.
    /// </para>
    /// <para>
    /// Counted inside <see cref="Update"/>, so it is already part of <c>update ms</c> rather than
    /// beside it — the same relationship <c>light ms</c> has, and adding them double-counts.
    /// </para>
    /// </remarks>
    public double SaveQueueMillis { get; private set; }

    /// <summary>Worker-thread milliseconds spent serialising and committing, cumulative.</summary>
    /// <remarks>
    /// Reported beside <see cref="SaveQueueMillis"/> and never added to it: this is time on a
    /// worker and that is time on the sim thread. The pair is the measurement, because the whole
    /// claim being checked is that the second is small <em>because</em> the first is not.
    /// </remarks>
    public double SaveWriteMillis { get; private set; }

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

    /// <summary>
    /// Edits that moved a column's open-to-the-sky height — breaking into daylight, or sealing it
    /// out again.
    /// </summary>
    /// <remarks>
    /// <b>Built at M7c-2 with its consumer, which is the rain's occlusion field.</b> That field is
    /// a texture of sky heights around the player, and it has exactly two reasons to be stale: the
    /// player walked to a different chunk, or somebody dug through a roof. The first is already
    /// observable; this is the second, and without it the alternative is rebuilding every frame or
    /// rain that keeps falling into a shaft after it has been filled in.
    /// <para>
    /// <b>It counts height <i>moves</i>, not voxel writes</b>, which is most of the value:
    /// <see cref="Lighting.SkyColumn.OnVoxelChanged"/> returns true only when the open height
    /// actually shifts, so a tunnel dug well below the surface — most mining — never touches this
    /// at all, and the field is not rebuilt for it.
    /// </para>
    /// </remarks>
    public int SkyHeightChanges { get; private set; }

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

    /// <summary>
    /// Whether every tracked chunk within <paramref name="radius"/> chunks (Chebyshev) of
    /// <paramref name="centre"/> has derived light.
    /// </summary>
    /// <remarks>
    /// The instrument for the derive order. <c>first draw</c> cannot see the player's own
    /// neighbourhood deriving last — inside a mined-out void nothing near the player has geometry
    /// — and <c>light settled</c> is the whole box. This is the moment the player's surroundings
    /// are lit, whatever the rest of the box is doing. Untracked positions count as done, so a
    /// viewer at the edge of the world is not waiting on chunks nobody asked for.
    /// </remarks>
    public bool AllDerivedWithin(ChunkPos centre, int radius)
    {
        for (int dz = -radius; dz <= radius; dz++)
        for (int dx = -radius; dx <= radius; dx++)
        for (int dy = -radius; dy <= radius; dy++)
        {
            if (_streamer.TryGet(centre.Offset(dx, dy, dz), out StreamedChunk chunk)
                && chunk.Light is not { HasDerived: true })
            {
                return false;
            }
        }

        return true;
    }

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
        && _awaitingLight.Count == 0

        // M6b adds a third queue to this, for the reason M5c added the first two: a caller that
        // asked only about jobs and the work list would see quiescence with a chunk's bytes still
        // sitting in the pending batch, and a test that settled there would be asserting about a
        // world that had not finished being written.
        && PendingSaves == 0;

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

        // Two flags, and they answer different questions — see StreamedChunk.DirtySinceSave.
        // HasEdits is permanent and says this chunk cannot come from the seed; DirtySinceSave is
        // transient and says it does not match what is on disk.
        chunk.HasEdits = true;
        chunk.DirtySinceSave = true;
        EditedVoxels++;

        RelightAfterEdit(chunk, voxel);

        Span<ChunkPos> dirty = stackalloc ChunkPos[ChunkEdits.MaxDirtyChunks];
        int count = ChunkEdits.DirtyChunks(voxel, dirty);
        for (int i = 0; i < count; i++)
        {
            MarkForRemesh(dirty[i]);
        }

        // Last, so a subscriber that reads the world back sees the voxel it was told about. The
        // fluid adapter's CanHold is exactly such a read.
        _onVoxelChanged?.Invoke(voxel, materialId);

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
            SkyHeightChanges++;
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
        SubmitSaves();
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
        // Nearest first. Sorted once here rather than per pop, and a build can queue nothing, so
        // the order holds for the whole drain. The FIFO arm pops from the front of the unsorted
        // list — O(n) a pop, and it is the control arm, so that is the cost of being comparable.
        if (_distanceOrderedDerives)
        {
            SortLightBuilds();
        }

        while (_lightBuilds.Count > 0)
        {
            int index = _distanceOrderedDerives ? _lightBuilds.Count - 1 : 0;
            ChunkPos next = _lightBuilds[index];
            _lightBuilds.RemoveAt(index);
            BuildLight(next);
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
        _lightBuilds.Add(position);
        _lightBuildsUnsorted = true;
    }

    /// <summary>
    /// Puts the derive queue in farthest-first order, if anything has changed since it last was.
    /// </summary>
    private void SortLightBuilds()
    {
        int version = _streamer.ViewerVersion;
        if (!_lightBuildsUnsorted && version == _lightBuildsSortedAtViewerVersion)
        {
            return;
        }

        // Descending, so the nearest is at the end and pops in O(1). Ties are left in whatever
        // order the sort gives; nothing depends on the order within one distance.
        _lightBuilds.Sort((a, b) =>
            _streamer.NearestViewerDistance(b).CompareTo(_streamer.NearestViewerDistance(a)));

        _lightBuildsUnsorted = false;
        _lightBuildsSortedAtViewerVersion = version;
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

        // Whatever the outcome. A dropped load has left the store exactly as surely as a
        // completed one, and a counter that only came down on success would wedge DrainStoreLane
        // for its whole timeout at every quit.
        if (result.Job is SaveWorldJob or LoadChunkJob or LoadColumnJob)
        {
            _storeJobsInFlight--;
        }

        // Ahead of the chunk lookup, because neither of these is *about* one chunk. A save spans a
        // batch and a column spans every chunk Y, so decrementing OutstandingJobs on whatever
        // record their nominal Position happens to name would corrupt the accounting that keeps a
        // chunk from being forgotten with a worker still holding it.
        switch (result.Job)
        {
            case SaveWorldJob save:
                ApplySave(save, result.Outcome, result.Exception);
                return;

            case LoadColumnJob column:
                ApplyColumnRestore(column, result.Outcome);
                return;
        }

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
                ChunksGenerated++;

                // Terrain arriving here may be the last thing 26 neighbours were waiting on
                // before they can run their own feature stage.
                PushWithNeighbours(chunk.Position);
                break;

            case LoadChunkJob load:
                if (load.Result is null)
                {
                    // The index promised a blob and the store had none. Drop the claim and let the
                    // next pass generate — the only honest recovery, and distinct from a blob that
                    // exists and will not parse, which faults rather than quietly becoming
                    // hillside where a base used to be.
                    _savedChunks.Remove(chunk.Position);
                    chunk.TransitionTo(ChunkState.Queued);
                    Push(chunk.Position);
                    break;
                }

                chunk.Voxels = load.Result;
                ChunksLoaded++;

                // It came from the store, so by definition it does not match the seed — but it
                // does match the disk, so it is not dirty.
                chunk.HasEdits = true;
                chunk.DirtySinceSave = false;

                // Features, not Terrain, so ApplyFeatures does not run over it a second time.
                //
                // Revisit trigger, and it is real the moment the feature stage places anything:
                // a chunk that A's features wrote into, but which was never itself edited, is not
                // stored — so it regenerates from seed and loses what A gave it, while A comes
                // back at Features and never re-runs. Re-running instead is not the fix either,
                // because a feature re-applied over material the player has since mined would
                // undo their edit, which is the failure this whole milestone exists to prevent.
                // The two candidate answers are saving feature-affected neighbours, or recording
                // per chunk which features have run; neither can be chosen against a stage that
                // currently places nothing.
                chunk.Stage = GenerationStage.Features;
                chunk.TransitionTo(ChunkState.Generated);

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
            // A dropped load returns to Queued exactly as a dropped generate does — both leave the
            // chunk needing its voxels from somewhere, and Advance re-decides which route.
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

                // Memory, then the index, then the seed. Getting this order wrong is the only
                // failure in the load path that matters: regenerating a chunk that had a blob is a
                // silent, total loss of everything in it.
                if (_store is not null && _savedChunks.Contains(chunk.Position))
                {
                    Submit(
                        chunk,
                        new LoadChunkJob(_store, _materials, chunk.Position),
                        serial: true);
                    _storeJobsInFlight++;
                }
                else
                {
                    Submit(chunk, new GenerateChunkJob(_generator, chunk.Position));
                }

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

    private void Submit(StreamedChunk chunk, IJob job, bool serial = false)
    {
        var cancellation = new CancellationTokenSource();
        chunk.Cancellation = cancellation;
        chunk.OutstandingJobs++;
        _batch.Add((job, cancellation.Token, serial));
    }

    private void OnChunkViewed(StreamedChunk chunk)
    {
        // A chunk revived mid-job is already past Unloaded and keeps whatever state its
        // cancelled job left it in; the pump picks it up once that job reports.
        if (chunk.State == ChunkState.Unloaded)
        {
            if (chunk.HasVoxels)
            {
                // A cache hit: the streamer kept this chunk after its last viewer left, voxels
                // and persistence flags intact, so it re-enters where a load would have landed
                // it. Its light is derived afresh; the streamer dropped the grid, and Retain put
                // the stage back to Features so CanMesh waits for it.
                //
                // With its neighbours, for the reason a load's completion pushes them. Recorded
                // from the break-recipe battery: pushing only the chunk itself is currently the
                // identity, because reaching Lit pushes the 26 anyway and the ring's feature
                // advance does too. Kept because it states the intent, and because anything that
                // later lets a revived chunk skip the derive makes the difference real.
                chunk.TransitionTo(ChunkState.Generated);
                PushWithNeighbours(chunk.Position);
                return;
            }

            chunk.TransitionTo(ChunkState.Queued);
        }

        Push(chunk.Position);
    }

    /// <summary>
    /// A cached chunk is being dropped to make room while it still differs from the disk.
    /// </summary>
    /// <remarks>
    /// Unreachable today, and kept because of what it guards. A chunk enters the cache after
    /// its unview queued its save, and nothing edits a chunk that is not tracked — so the flag
    /// cannot be set while cached. If anything ever does set it, this is the difference between
    /// a cache and a place tunnels go to die.
    /// </remarks>
    private void OnChunkEvicted(StreamedChunk chunk) => QueueSave(chunk);

    private void OnChunkUnviewed(StreamedChunk chunk)
    {
        // Before the cancel, because this is the last moment the chunk is guaranteed to hold its
        // voxels. ChunkStreamer.Forget runs immediately after unless a worker still owes a result,
        // and a chunk that reached that point unsaved has lost whatever the player did to it.
        QueueSave(chunk);

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


    // ---------------------------------------------------------------------------------------
    // Persistence. Everything here is M6b-2, and every race it handles already had a documented
    // answer in this file written for meshes — the work was transferring each one across on
    // purpose, because where a mesh race leaves stale geometry the same race loses a tunnel.
    // ---------------------------------------------------------------------------------------

    /// <summary>Queues a chunk for the next save batch, if it has anything worth writing.</summary>
    private void QueueSave(StreamedChunk chunk)
    {
        if (_store is null || !chunk.DirtySinceSave || chunk.Voxels is null)
        {
            return;
        }

        // Timed after the early-out on purpose. The overwhelming majority of an unloading shell
        // is untouched terrain that leaves through the line above, and folding those into the
        // measurement would price the burst at the cost of not saving.
        long entered = _nowTicks();

        // The moment these voxels stop being the sim thread's to write to — the same flag and the
        // same reason as a mesh job's 27-chunk gather. A chunk queued here is usually about to be
        // forgotten, but it can be revived and mined before the job runs, and VoxelChunk.Set can
        // promote the representation and swap the backing arrays under the serialiser.
        chunk.VoxelsSharedWithJob = true;

        // Cleared here rather than when the job lands. An edit arriving while the save is in
        // flight sets it again and the chunk is written a second time; clearing on completion
        // would swallow exactly those edits. Same shape as NeedsRemesh surviving a mesh.
        chunk.DirtySinceSave = false;

        _pendingChunkSaves.Add((chunk.Position, chunk.Voxels));

        SaveQueueMillis += (_nowTicks() - entered) / TicksPerMillisecond;
    }

    /// <summary>Queues a sky column, if its heights have moved off the analytic seed.</summary>
    private void QueueSave(SkyColumn column)
    {
        // DirtySinceSave, not HasEdits. HasEdits is permanent — it says this column cannot be
        // rebuilt from the seed — so gating on it would rewrite every edited column on every
        // save, forever. Chunks got this split deliberately; columns got it after a test.
        if (_store is null || !column.DirtySinceSave)
        {
            return;
        }

        long entered = _nowTicks();

        column.MarkSaved();
        _pendingColumnSaves.Add((column.ChunkX, column.ChunkZ, column));

        SaveQueueMillis += (_nowTicks() - entered) / TicksPerMillisecond;
    }

    /// <summary>
    /// Hands the pending batch to a worker, at most one batch in flight at a time.
    /// </summary>
    /// <remarks>
    /// <b>One at a time so batches grow rather than multiply.</b> The serial lane would run them
    /// sequentially anyway; letting several queue up would only turn one transaction into several
    /// for the same bytes.
    /// </remarks>
    private void SubmitSaves()
    {
        if (_store is null || _saveInFlight) return;
        if (_pendingChunkSaves.Count == 0 && _pendingColumnSaves.Count == 0) return;

        long entered = _nowTicks();

        SaveBatches++;
        int batch = _pendingChunkSaves.Count + _pendingColumnSaves.Count;
        if (batch > LargestSaveBatch) LargestSaveBatch = batch;

        var job = new SaveWorldJob(
            _store,
            [.. _pendingChunkSaves],
            [.. _pendingColumnSaves]);

        // Claimed at submission rather than on completion, which looks optimistic. A chunk
        // revived before its save lands consults this index and submits a load, and the two are
        // kept apart by the *serial lane*: both are serial, so they cannot overlap, and a load
        // issued while the save is running waits for it. That is the load-bearing half, and
        // TheSerialLaneKeepsALoadBehindTheSaveItWouldRace pins it.
        //
        // Priority is the weaker half and is deliberately not claimed as correctness: when both
        // sit queued, Save's higher band means the write goes first, but if it did not the load
        // would read nothing, fall back to generating, and the chunk would be regenerated for
        // this session while the save still lands — wrong on screen, recoverable on disk.
        foreach ((ChunkPos position, _) in _pendingChunkSaves) _savedChunks.Add(position);
        foreach ((int x, int z, _) in _pendingColumnSaves) _savedColumns.Add((x, z));

        _pendingChunkSaves.Clear();
        _pendingColumnSaves.Clear();
        _saveInFlight = true;

        // CancellationToken.None, deliberately. Every other job here is cancelled the instant
        // nobody wants its chunk; a cancelled save is a deleted tunnel. Serial, because it is disk
        // work and contends on the one file — M2a's lane, unused until now.
        _batch.Add((job, CancellationToken.None, true));
        JobsInFlight++;
        _storeJobsInFlight++;
        _scheduler.SubmitBatch(CollectionsMarshal.AsSpan(_batch)[^1..]);
        _batch.RemoveAt(_batch.Count - 1);

        SaveQueueMillis += (_nowTicks() - entered) / TicksPerMillisecond;
    }

    private void ApplySave(SaveWorldJob job, JobOutcome outcome, Exception? error)
    {
        _saveInFlight = false;

        if (outcome == JobOutcome.Completed)
        {
            ChunksSaved += job.Chunks.Count;
            ColumnsSaved += job.Columns.Count;
            BytesSaved += job.BytesWritten;
            SaveWriteMillis += job.ElapsedMillis;
            return;
        }

        // Faulted or dropped. Put the work back rather than losing it — the opposite of what a
        // faulted generate does, and the asymmetry is the point: a lost mesh is rebuilt from data
        // that still exists, and a lost save is gone. Reported every time, because a store that
        // keeps failing is something the player has to be told about rather than a counter nobody
        // reads.
        FailedSaves++;

        _pendingChunkSaves.AddRange(job.Chunks);
        _pendingColumnSaves.AddRange(job.Columns);

        // Put the columns' dirty flags back, so that a later SaveAll walking the live set finds
        // them again rather than trusting the pending list alone.
        foreach ((_, _, SkyColumn column) in job.Columns) column.MarkDirty();

        // The index claimed these before the write; take the claim back, or a chunk that never
        // reached disk would be loaded from a blob that is not there.
        foreach ((ChunkPos position, _) in job.Chunks) _savedChunks.Remove(position);
        foreach ((int x, int z, _) in job.Columns) _savedColumns.Remove((x, z));

        if (error is not null)
        {
            _onJobFaulted?.Invoke(job.Position, error);
        }
    }

    /// <summary>A column has come into existence; ask the store whether it has heights for it.</summary>
    private void OnColumnCreated(SkyColumn column)
    {
        if (_store is null) return;

        var key = (column.ChunkX, column.ChunkZ);
        if (!_savedColumns.Contains(key) || !_columnRestoresInFlight.Add(key))
        {
            return;
        }

        // Submitted rather than read here. This runs inside ChunkStreamer's box-difference sweep,
        // and a one-chunk step at r16 creates ~37 columns — reading them on the sim thread would
        // put an estimated 0.74 ms of I/O in one frame, unamortised, against a streaming budget
        // M2d measured plateauing near 3% of a core.
        _scheduler.Submit(new LoadColumnJob(_store, column.ChunkX, column.ChunkZ), serial: true);
        JobsInFlight++;
        _storeJobsInFlight++;
    }

    private void ApplyColumnRestore(LoadColumnJob job, JobOutcome outcome)
    {
        var key = (job.ChunkX, job.ChunkZ);
        _columnRestoresInFlight.Remove(key);

        if (outcome != JobOutcome.Completed || job.Result is null)
        {
            return;
        }

        // Live first, then the cache. A column released while its read was in flight is kept
        // by the streamer if it had nothing to write — which an unrestored column never has —
        // and the revival hands that object back without asking for a restore, so the heights
        // have to land in it here or the shaft comes back seeded from the pre-dig surface.
        SkyColumn? live = _streamer.ColumnAt(job.ChunkX, job.ChunkZ)
                          ?? _streamer.CachedColumnAt(job.ChunkX, job.ChunkZ);
        if (live is null)
        {
            // The column went away while the read was in flight and was not kept. Nothing to
            // correct, and the next time it is created the restore is asked for again.
            return;
        }

        live.CopyFrom(job.Result);
        ColumnsRestored++;

        // The heights moved, so every chunk in this column has to re-derive its sky flags. Exactly
        // what M5c-1 built this for — "this column's sky heights moved" — and the reason a
        // reloaded shaft ends up lit rather than dark.
        QueueColumnLightBuild(new ChunkPos(job.ChunkX, 0, job.ChunkZ));
    }

    /// <summary>A column's last chunk has gone. Its only chance to be written.</summary>
    private void OnColumnReleased(SkyColumn column) => QueueSave(column);

    /// <summary>
    /// Waits until nothing on the serial lane is inside the store.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Because <see cref="SaveAll"/> is a sim-thread caller of a store whose contract says one
    /// caller at a time.</b> M6b-1 stated that contract and justified it by the lane; M6b-3 is
    /// where the second caller arrives, and the overlap it can produce is not a stale read — it is
    /// two threads binding parameters on one shared prepared statement inside one SQLite
    /// connection. The most likely moment is the most ordinary one: an autosave firing while the
    /// player walks back into a region they dug, which is a chunk load per chunk.
    /// </para>
    /// <para>
    /// <b>This terminates because nothing it does submits.</b> Applying a result can push a chunk
    /// onto the work list or queue a light build; jobs only ever reach the scheduler from
    /// <see cref="Pump"/>, <see cref="SubmitSaves"/> and <see cref="OnColumnCreated"/>, and none
    /// of those runs from here. So the lane empties and stays empty.
    /// </para>
    /// <para>
    /// <b>Bounded on wall clock rather than on <c>_nowTicks</c></b>, which is injected by tests
    /// and is free never to advance. This is a safety valve against a wedged lane rather than a
    /// budget, and the wait it is actually built for is one job of a few microseconds.
    /// </para>
    /// </remarks>
    /// <returns>Whether the lane really is clear. False means the timeout expired.</returns>
    private bool DrainStoreLane()
    {
        if (_storeJobsInFlight == 0) return true;

        long deadline = Stopwatch.GetTimestamp() + (long)(DrainTimeoutSeconds * Stopwatch.Frequency);

        while (_storeJobsInFlight > 0)
        {
            if (_scheduler.TryTakeResult(out JobResult result))
            {
                Apply(result);
                continue;
            }

            if (Stopwatch.GetTimestamp() > deadline)
            {
                return false;
            }

            // Yield rather than sleep: the job being waited on is microseconds long, and a
            // Sleep(1) here would cost a frame to save a few spins.
            Thread.Yield();
        }

        return true;
    }

    /// <summary>
    /// Writes everything dirty, synchronously, on the calling thread.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not through the scheduler, because quitting is exactly when the scheduler stops.</b>
    /// <see cref="Jobs.JobResult"/>'s contract is that a job still queued at shutdown reports
    /// <see cref="JobOutcome.Dropped"/> — so a world left to the normal save path at quit loses
    /// whatever had not yet been picked up. That is the single most likely way to ship a data-loss
    /// bug in this milestone, and it is this method.
    /// </para>
    /// <para>
    /// Safe to do inline once the lane is clear: nothing else writes voxels from another thread,
    /// and the whole 1,521-chunk fill measured 13 ms including serialisation. <b>The lane being
    /// clear is not free, though</b> — see <see cref="DrainStoreLane"/>, which this waits on
    /// first and which is the reason this can be called from an autosave timer at all.
    /// </para>
    /// </remarks>
    /// <returns>How many chunks and columns were written.</returns>
    public (int Chunks, int Columns) SaveAll()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_store is null) return (0, 0);

        if (!DrainStoreLane())
        {
            // Refused rather than written beside whatever is in there. What giving up costs is
            // this session's unsaved edits; what writing anyway costs is the file. A lane that has
            // not moved in five seconds is a bug somewhere else, and it is reported as one.
            FailedSaves++;
            _onJobFaulted?.Invoke(
                ChunkPos.Zero,
                new TimeoutException(
                    $"The store's serial lane still had {_storeJobsInFlight} job(s) after "
                    + $"{DrainTimeoutSeconds:F0} s, so nothing was written. The store takes one "
                    + "caller at a time."));
            return (0, 0);
        }

        // Everything still loaded and dirty, on top of whatever a failed or unsubmitted batch left
        // behind. Walking the live set rather than trusting the pending list is what makes this a
        // backstop rather than a second copy of the same bookkeeping.
        foreach (StreamedChunk chunk in _streamer.Chunks) QueueSave(chunk);
        foreach (SkyColumn column in _streamer.Columns) QueueSave(column);

        if (_pendingChunkSaves.Count == 0 && _pendingColumnSaves.Count == 0)
        {
            _store.Flush();
            return (0, 0);
        }

        var job = new SaveWorldJob(_store, [.. _pendingChunkSaves], [.. _pendingColumnSaves]);

        foreach ((ChunkPos position, _) in _pendingChunkSaves) _savedChunks.Add(position);
        foreach ((int x, int z, _) in _pendingColumnSaves) _savedColumns.Add((x, z));

        int chunks = _pendingChunkSaves.Count;
        int columns = _pendingColumnSaves.Count;

        _pendingChunkSaves.Clear();
        _pendingColumnSaves.Clear();

        try
        {
            job.Run(CancellationToken.None);
        }
        catch (Exception error)
        {
            // The same restore ApplySave does for the asynchronous twin, and it was missing here.
            // The flags and the pending lists are cleared before the write — deliberately, so an
            // edit arriving during it is not swallowed — so a throw after that point would have
            // left the world marked as saved and never written. The synchronous path is the one
            // an autosave timer and a quit both go through, which is where it would have shown.
            FailedSaves++;

            _pendingChunkSaves.AddRange(job.Chunks);
            _pendingColumnSaves.AddRange(job.Columns);

            foreach ((_, _, SkyColumn column) in job.Columns) column.MarkDirty();

            foreach ((ChunkPos position, _) in job.Chunks) _savedChunks.Remove(position);
            foreach ((int x, int z, _) in job.Columns) _savedColumns.Remove((x, z));

            _onJobFaulted?.Invoke(ChunkPos.Zero, error);
            return (0, 0);
        }

        ChunksSaved += chunks;
        ColumnsSaved += columns;
        BytesSaved += job.BytesWritten;

        _store.Flush();
        return (chunks, columns);
    }

    /// <summary>Writes the world header, which must happen before any chunk reaches the store.</summary>
    /// <remarks>
    /// Ordering is what makes the ID table safe without a world-spanning transaction: numeric IDs
    /// are only allocated during content load, which happens once at open, so by the time any
    /// chunk is written the table it references is already on disk. See <see cref="IWorldStore"/>.
    /// </remarks>
    /// <returns>False when the lane could not be drained, so nothing was written.</returns>
    public bool WriteWorldHeader(WorldHeader header)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(header);

        if (_store is null) return false;

        // The same wait SaveAll does, and for the same reason: this is a sim-thread caller of a
        // store whose contract is one caller at a time. It was safe without it while the only
        // call was at open, before a single job existed — and M6c-3 gives it a second caller, on
        // the autosave timer, where a chunk load is very likely to be on the lane.
        if (!DrainStoreLane())
        {
            FailedSaves++;
            _onJobFaulted?.Invoke(
                ChunkPos.Zero,
                new TimeoutException(
                    $"The store's serial lane still had {_storeJobsInFlight} job(s) after "
                    + $"{DrainTimeoutSeconds:F0} s, so the world header was not written."));
            return false;
        }

        _store.WriteWorldHeader(WorldHeaderBlob.Write(header));
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // Before the flag, because SaveAll refuses a disposed pipeline — and before cancelling
        // anything, because a save queued and not yet picked up reports Dropped at shutdown and
        // would be lost. This is the one place the milestone's most likely data-loss bug lives.
        if (_store is not null)
        {
            try
            {
                SaveAll();
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                // Reported, never rethrown. Dispose running during a crash must not replace the
                // original exception with a storage one.
                _onJobFaulted?.Invoke(ChunkPos.Zero, error);
            }
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
