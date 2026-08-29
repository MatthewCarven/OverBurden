using MinerCraft.Core.Content;
using MinerCraft.Core.Jobs;
using MinerCraft.Core.Voxels;
using MinerCraft.Core.Worldgen;

namespace MinerCraft.Core.Streaming;

/// <summary>
/// Decides which chunks exist. Viewers are added, moved and removed; chunks are refcounted
/// across them, and appear and disappear as that count leaves and reaches zero.
/// </summary>
/// <remarks>
/// <para>
/// <b>Sim thread only.</b> Nothing here is synchronised. Workers never touch the streamer —
/// they receive jobs and report results, and the sim thread applies those results. Adding a
/// lock here would be a sign the ownership has gone wrong rather than a fix.
/// </para>
/// <para>
/// The whole policy is Core data, so all of it is testable without Godot, without a
/// renderer and without threads.
/// </para>
/// </remarks>
public sealed class ChunkStreamer : IVoxelWorld
{
    /// <summary>
    /// How many chunks are loaded beyond what a viewer can actually see.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two, and the second one is not obvious.</b> The margin is set by how far
    /// information has to travel inward before a chunk's voxels are final:
    /// </para>
    /// <list type="number">
    ///   <item><description>
    ///     A chunk at radius <c>R</c> can only mesh once its 26 neighbours have finished
    ///     <see cref="GenerationStage.Features"/>, because a feature originating next door
    ///     writes into it. That needs chunks out to <c>R+1</c> at
    ///     <see cref="GenerationStage.Features"/>.
    ///   </description></item>
    ///   <item><description>
    ///     A chunk at <c>R+1</c> can only reach <see cref="GenerationStage.Features"/> once
    ///     <em>its</em> neighbours have finished <see cref="GenerationStage.Terrain"/>. That
    ///     needs chunks out to <c>R+2</c>.
    ///   </description></item>
    /// </list>
    /// <para>
    /// The greedy mesher's padded 34³ copy wants the same first ring for an entirely
    /// independent reason, so a margin of at least one was always going to be paid. Only
    /// the second ring is the stage pipeline's own cost.
    /// </para>
    /// <para>
    /// It is not free: at a 16-chunk radius the loaded volume goes from 33³ = 35,937 chunks
    /// to 37³ = 50,653, or 41% more. That is the honest price of seamless cross-chunk
    /// features.
    /// </para>
    /// <para>
    /// <b>Do not treat this as a tuning knob — measured at M2d, it is not one.</b> Lowering
    /// the margin by one does not buy cheaper terrain; it shrinks the <em>meshable</em>
    /// radius by one on every axis, because the barrier above is enforced whether or not
    /// stage 2 actually places anything. Margin 1 at r4h/2v measured identical to margin 2
    /// at r3h/1v on every number collected — 847 chunks tracked, 97 drawn, 1.37% of a core.
    /// The knob a player wants is view distance, and it already exists. This stays a
    /// parameter only so tests can set it to 0 and reason about a single shell.
    /// </para>
    /// </remarks>
    public const int DefaultLoadMargin = 2;

    private readonly Dictionary<ChunkPos, StreamedChunk> _chunks = [];
    private readonly Dictionary<ViewerId, Viewer> _viewers = [];
    private readonly ViewerSet? _priorityViewers;
    private readonly Action<StreamedChunk>? _onChunkViewed;
    private readonly Action<StreamedChunk>? _onChunkUnviewed;
    private readonly int _loadMargin;

    private int _nextViewerId = 1;
    private int _viewedCount;

    /// <param name="priorityViewers">
    /// Kept in step with the registered viewers, so job priority and streaming decisions
    /// read the same positions. Optional only so the policy can be tested on its own.
    /// </param>
    /// <param name="onChunkViewed">
    /// Fired when a chunk's view count rises from zero — it needs loading. Fires for a
    /// revived chunk as well as a new one, since both mean "this is wanted now".
    /// </param>
    /// <param name="onChunkUnviewed">
    /// Fired when a chunk's view count falls to zero — nobody wants it any more. The chunk
    /// is <em>still tracked</em> when this runs, because this is where the pipeline cancels
    /// the job in flight and frees the mesh, and both need the chunk. It is dropped
    /// immediately afterwards unless a worker still owes it a result.
    /// </param>
    /// <param name="loadMargin">See <see cref="DefaultLoadMargin"/>.</param>
    public ChunkStreamer(
        ViewerSet? priorityViewers = null,
        Action<StreamedChunk>? onChunkViewed = null,
        Action<StreamedChunk>? onChunkUnviewed = null,
        int loadMargin = DefaultLoadMargin)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(loadMargin);

        _priorityViewers = priorityViewers;
        _onChunkViewed = onChunkViewed;
        _onChunkUnviewed = onChunkUnviewed;
        _loadMargin = loadMargin;
    }

    public int ViewerCount => _viewers.Count;

    /// <summary>
    /// How many chunks at least one viewer currently wants. This is the number the streaming
    /// invariant is stated over: it equals the size of the union of the viewer load boxes.
    /// </summary>
    public int ViewedCount => _viewedCount;

    /// <summary>
    /// How many chunk records exist, including any being retained past their last viewer
    /// while a worker still owes them a result.
    /// </summary>
    /// <remarks>
    /// Never below <see cref="ViewedCount"/>, and equal to it whenever nothing is in flight.
    /// A gap that does not close is the symptom of a job that never reported — which is the
    /// exact failure the exactly-once invariant in <see cref="Jobs.JobResult"/> exists to
    /// make impossible.
    /// </remarks>
    public int TrackedCount => _chunks.Count;

    public IReadOnlyCollection<ChunkPos> TrackedPositions => _chunks.Keys;

    public IEnumerable<StreamedChunk> Chunks => _chunks.Values;

    /// <summary>The margin this streamer was built with.</summary>
    public int LoadMargin => _loadMargin;

    public ViewerId AddViewer(Viewer viewer)
    {
        var id = new ViewerId(_nextViewerId++);
        _viewers[id] = viewer;

        // Everything in the new viewer's box gains a view; there is no old box.
        LoadBoxOf(viewer).ForEach(View);
        PublishViewerPositions();
        return id;
    }

    /// <summary>Convenience for the common case of a viewer that only ever moves.</summary>
    public ViewerId AddViewer(ChunkPos position, int horizontalRadius, int verticalRadius) =>
        AddViewer(new Viewer(position, horizontalRadius, verticalRadius));

    public Viewer GetViewer(ViewerId id) => _viewers[id];

    public bool TryGetViewer(ViewerId id, out Viewer viewer) => _viewers.TryGetValue(id, out viewer);

    /// <summary>Moves a viewer, keeping its radii.</summary>
    public void MoveViewer(ViewerId id, ChunkPos position) =>
        UpdateViewer(id, _viewers[id] with { Position = position });

    /// <summary>
    /// Replaces a viewer's position and radii, loading and unloading only the difference.
    /// </summary>
    public void UpdateViewer(ViewerId id, Viewer viewer)
    {
        Viewer previous = _viewers[id];
        ChunkBox oldBox = LoadBoxOf(previous);
        ChunkBox newBox = LoadBoxOf(viewer);

        _viewers[id] = viewer;

        if (oldBox != newBox)
        {
            // Only the shells that entered and left are touched — the overlap, which is
            // nearly all of it after a one-chunk step, is never visited. The two shells are
            // disjoint by construction, so the order of these two lines does not affect the
            // resulting counts.
            newBox.ForEachDifference(oldBox, View);
            oldBox.ForEachDifference(newBox, Unview);
        }

        PublishViewerPositions();
    }

    public void RemoveViewer(ViewerId id)
    {
        Viewer viewer = _viewers[id];
        _viewers.Remove(id);

        LoadBoxOf(viewer).ForEach(Unview);
        PublishViewerPositions();
    }

    public bool TryGet(ChunkPos position, out StreamedChunk chunk) =>
        _chunks.TryGetValue(position, out chunk!);

    public StreamedChunk? Get(ChunkPos position) =>
        _chunks.TryGetValue(position, out StreamedChunk? chunk) ? chunk : null;

    /// <summary>Reads one voxel, if the chunk holding it exists and has generated.</summary>
    /// <remarks>
    /// False means "not loaded", which a caller must not read as "air" — see
    /// <see cref="RayOutcome.Blocked"/>. A chunk that is tracked but still
    /// <see cref="ChunkState.Generating"/> answers false for the same reason: it has no
    /// voxels yet, and guessing at them is how a ray walks through terrain that is about to
    /// exist.
    /// </remarks>
    public bool TryGetMaterial(VoxelPos voxel, out int materialId)
    {
        if (!_chunks.TryGetValue(voxel.Chunk, out StreamedChunk? chunk) || chunk.Voxels is null)
        {
            materialId = MaterialIds.AirNumericId;
            return false;
        }

        (int localX, int localY, int localZ) = voxel.Local;
        materialId = chunk.Voxels.Get(localX, localY, localZ);
        return true;
    }

    /// <summary>Whether a record exists — viewed, or retained for a job in flight.</summary>
    public bool IsTracked(ChunkPos position) => _chunks.ContainsKey(position);

    /// <summary>Whether at least one viewer currently wants this chunk.</summary>
    public bool IsViewed(ChunkPos position) => ViewCountOf(position) > 0;

    public int ViewCountOf(ChunkPos position) =>
        _chunks.TryGetValue(position, out StreamedChunk? chunk) ? chunk.ViewCount : 0;

    /// <summary>The chunks a viewer wants loaded, margin included.</summary>
    public ChunkBox LoadBoxOf(Viewer viewer) => viewer.Box.Expand(_loadMargin);

    /// <summary>
    /// Whether <paramref name="position"/> may advance from
    /// <see cref="GenerationStage.Terrain"/> to <see cref="GenerationStage.Features"/>.
    /// </summary>
    /// <remarks>
    /// The barrier from docs/M2-PLAN.md: a chunk may advance to stage N+1 only once all its
    /// neighbours have completed stage N. Stage 2 writes into its neighbours, so those
    /// neighbours must already have terrain for it to write into — otherwise the two halves
    /// of an ore vein or a ravine disagree and the seam shows.
    /// </remarks>
    public bool CanAdvanceToFeatures(ChunkPos position)
    {
        StreamedChunk? chunk = Get(position);
        return chunk is { Stage: GenerationStage.Terrain }
               && AllNeighboursAtLeast(position, GenerationStage.Terrain);
    }

    /// <summary>
    /// Whether <paramref name="position"/>'s voxels are final and may be meshed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Stricter than "this chunk finished generating". A neighbour still at
    /// <see cref="GenerationStage.Terrain"/> has yet to run its feature stage, and that
    /// stage may write into this chunk — meshing first would draw a vein that grows a
    /// second later.
    /// </para>
    /// <para>
    /// <b>This predicate is also what confines meshing to the visible box, with no second
    /// refcount to maintain.</b> A chunk out in the load margin has neighbours beyond the
    /// margin that were never loaded at all, so it fails here automatically. The margin
    /// generates and never meshes because of the barrier itself, not because anything
    /// tracks which shell it is in.
    /// </para>
    /// </remarks>
    public bool CanMesh(ChunkPos position)
    {
        StreamedChunk? chunk = Get(position);
        return chunk is { Stage: GenerationStage.Features }
               && AllNeighboursAtLeast(position, GenerationStage.Features);
    }

    /// <summary>True when all 26 surrounding chunks are loaded and have reached <paramref name="stage"/>.</summary>
    public bool AllNeighboursAtLeast(ChunkPos position, GenerationStage stage)
    {
        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
        {
            if (dx == 0 && dy == 0 && dz == 0)
            {
                continue;
            }

            StreamedChunk? neighbour = Get(position.Offset(dx, dy, dz));
            if (neighbour is null || neighbour.Stage < stage)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Drops a chunk that nobody wants and no worker still owes a result for. Returns
    /// whether it went.
    /// </summary>
    /// <remarks>
    /// Called by the pipeline after a job reports, which is the moment a chunk retained by
    /// <see cref="Unview"/> becomes safe to release. Idempotent, and refuses to drop a
    /// chunk somebody has since viewed again.
    /// </remarks>
    public bool ForgetIfUnwanted(ChunkPos position)
    {
        if (!_chunks.TryGetValue(position, out StreamedChunk? chunk))
        {
            return false;
        }

        if (chunk.ViewCount > 0 || chunk.HasJobInFlight)
        {
            return false;
        }

        Forget(chunk);
        return true;
    }

    private void View(ChunkPos position)
    {
        if (!_chunks.TryGetValue(position, out StreamedChunk? chunk))
        {
            chunk = new StreamedChunk(position);
            _chunks[position] = chunk;
        }

        chunk.ViewCount++;
        if (chunk.ViewCount != 1)
        {
            return;
        }

        // Zero to one, whether the chunk was just created or is being revived after being
        // retained for a job in flight. Both mean the same thing to the pipeline — this
        // chunk is wanted — and a revived chunk that did not announce itself would sit at
        // whatever state its cancelled job left it in and never be looked at again.
        _viewedCount++;
        _onChunkViewed?.Invoke(chunk);
    }

    private void Unview(ChunkPos position)
    {
        if (!_chunks.TryGetValue(position, out StreamedChunk? chunk))
        {
            // Nothing to release. Reachable only if a viewer box changed without going
            // through this class, which the tests exist to catch.
            return;
        }

        chunk.ViewCount--;
        if (chunk.ViewCount > 0)
        {
            return;
        }

        chunk.ViewCount = 0;
        _viewedCount--;

        // Fired while the chunk is still tracked, because this is where the pipeline
        // cancels the job in flight and it needs the chunk to do it.
        _onChunkUnviewed?.Invoke(chunk);

        // A chunk a worker still owes a result for is kept — see
        // StreamedChunk.OutstandingJobs. It stays in whatever state its job left it in, so
        // that the result has something coherent to land on, and ForgetIfUnwanted collects
        // it once that happens.
        if (!chunk.HasJobInFlight)
        {
            Forget(chunk);
        }
    }

    private void Forget(StreamedChunk chunk)
    {
        _chunks.Remove(chunk.Position);

        // Guarded because self-transitions are rejected, and a chunk that was viewed and
        // released without ever being queued has sat in Unloaded the whole time.
        if (chunk.State != ChunkState.Unloaded)
        {
            chunk.TransitionTo(ChunkState.Unloaded);
        }

        chunk.Cancellation?.Dispose();
        chunk.Cancellation = null;
    }

    /// <summary>
    /// Republishes viewer positions to the <see cref="ViewerSet"/> the job queue prioritises
    /// against, so streaming and scheduling never disagree about where the player is.
    /// </summary>
    private void PublishViewerPositions()
    {
        if (_priorityViewers is null)
        {
            return;
        }

        var positions = new ChunkPos[_viewers.Count];
        int i = 0;
        foreach (Viewer viewer in _viewers.Values)
        {
            positions[i++] = viewer.Position;
        }

        _priorityViewers.Update(positions);
    }
}
