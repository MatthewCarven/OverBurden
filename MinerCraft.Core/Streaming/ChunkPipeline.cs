using System.Diagnostics;
using System.Runtime.InteropServices;
using MinerCraft.Core.Content;
using MinerCraft.Core.Jobs;
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
public sealed class ChunkPipeline : IDisposable
{
    /// <summary>How long one frame may spend applying finished jobs.</summary>
    /// <remarks>
    /// A frame at 60 Hz is 16.7 ms and the apply pass averages well under one, so this is
    /// not a throttle on normal operation — it clips the tail. Measured before it existed,
    /// applying results sat at 0.42 ms mean and 2.04 ms p99 while a single frame reached
    /// 27.6 ms, and that one frame is the entire problem.
    /// </remarks>
    public static readonly TimeSpan DefaultResultBudget = TimeSpan.FromMilliseconds(2.0);

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
    private bool _disposed;

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
        Func<long>? nowTicks = null)
    {
        _generator = generator;
        _materials = materials;
        _scheduler = scheduler;
        _onMeshReady = onMeshReady;
        _onMeshDiscarded = onMeshDiscarded;
        _onJobFaulted = onJobFaulted;
        _nowTicks = nowTicks ?? Stopwatch.GetTimestamp;
        _resultBudgetTicks =
            (long)((resultBudget ?? DefaultResultBudget).TotalMilliseconds * TicksPerMillisecond);

        _streamer = new ChunkStreamer(
            priorityViewers,
            onChunkViewed: OnChunkViewed,
            onChunkUnviewed: OnChunkUnviewed,
            loadMargin);
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
            // Nothing changed, so nothing is stale. Worth checking because a sphere carve
            // rewrites air as air across most of its bounding box, and every one of those
            // would otherwise clone a chunk and dirty its neighbours for no reason.
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

        Span<ChunkPos> dirty = stackalloc ChunkPos[ChunkEdits.MaxDirtyChunks];
        int count = ChunkEdits.DirtyChunks(voxel, dirty);
        for (int i = 0; i < count; i++)
        {
            MarkForRemesh(dirty[i]);
        }

        return true;
    }

    private void MarkForRemesh(ChunkPos position)
    {
        if (!_streamer.TryGet(position, out StreamedChunk chunk))
        {
            return;
        }

        chunk.NeedsRemesh = true;
        Push(position);
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
        return Pump(maxSubmissions);
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

    private void SubmitMesh(StreamedChunk chunk)
    {
        // Urgent exactly when an edit caused this. Streaming meshes are ordered by distance
        // and can wait; a remesh the player is looking at cannot, or the rock they just broke
        // stays on screen while the horizon loads.
        bool urgent = chunk.NeedsRemesh;
        chunk.NeedsRemesh = false;

        chunk.TransitionTo(ChunkState.Meshing);
        Submit(
            chunk,
            new MeshChunkJob(chunk.Position, Neighbourhood(chunk.Position), _materials, urgent));
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

        if (chunk.State == ChunkState.Meshed)
        {
            _onMeshDiscarded?.Invoke(chunk.Position);
        }

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
