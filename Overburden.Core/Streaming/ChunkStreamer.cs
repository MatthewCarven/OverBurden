using Overburden.Core.Content;
using Overburden.Core.Jobs;
using Overburden.Core.Lighting;
using Overburden.Core.Voxels;
using Overburden.Core.Worldgen;

namespace Overburden.Core.Streaming;

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
public sealed class ChunkStreamer : IVoxelWorld, ILightChunks
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
    private readonly Dictionary<(int X, int Z), SkyColumnEntry> _columns = [];
    private readonly ViewerSet? _priorityViewers;
    private readonly ISurfaceHeights? _heights;
    private readonly Action<StreamedChunk>? _onChunkViewed;
    private readonly Action<StreamedChunk>? _onChunkUnviewed;
    private readonly Action<SkyColumn>? _onColumnCreated;
    private readonly Action<SkyColumn>? _onColumnReleased;
    private readonly Action<StreamedChunk>? _onChunkEvicted;
    private readonly int _loadMargin;

    // The retained-chunk cache (docs/CHUNK-CACHE-PLAN.md). A chunk in _chunks is never in
    // _cachedChunks, and a column in _columns is never in _cachedColumns; both dictionaries index
    // one eviction list, oldest first, so the cap is one number over both kinds of entry.
    private readonly Dictionary<ChunkPos, LinkedListNode<CacheEntry>> _cachedChunks = [];
    private readonly Dictionary<(int X, int Z), LinkedListNode<CacheEntry>> _cachedColumns = [];
    private readonly LinkedList<CacheEntry> _cacheOrder = new();
    private readonly long _cacheCapacityBytes;
    private long _cacheBytes;

    private int _nextViewerId = 1;
    private int _viewedCount;

    /// <summary>
    /// What a retained chunk or column is charged against the cap.
    /// </summary>
    /// <remarks>
    /// <b>Estimates, and labelled as such.</b> A chunk's payload is exact
    /// (<see cref="VoxelChunk.EstimatedBytes"/>) and a column's is exact (32 × 32 shorts); the
    /// per-record constants stand in for the object headers, the dictionary slot and the list
    /// node, which the runtime does not report. A uniform chunk has no payload at all, so the
    /// record cost is what stops the cap from admitting an unbounded number of them.
    /// </remarks>
    private const int ChunkRecordOverheadBytes = 192;

    private const int ColumnCacheBytes = VoxelConstants.ChunkSize * VoxelConstants.ChunkSize * sizeof(short) + 96;

    /// <summary>One retained chunk or one retained column, and what it is charged.</summary>
    private sealed class CacheEntry
    {
        public StreamedChunk? Chunk;

        public SkyColumn? Column;

        public long Bytes;
    }

    /// <summary>
    /// A sky column and how many tracked chunks share it.
    /// </summary>
    /// <remarks>
    /// <b>A second refcount, on a different axis and over a different object</b>, and M2c's
    /// recorded "don't add a second refcount to fix the margin" is emphatically not this. A
    /// column spans every chunk Y at one (x, z), so it has to survive the vertical unload the
    /// chunk refcount performs as the player descends — which is precisely the thing a per-chunk
    /// count cannot express.
    /// </remarks>
    private sealed class SkyColumnEntry(SkyColumn column)
    {
        public SkyColumn Column { get; } = column;

        public int RefCount { get; set; }
    }

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
    /// <param name="onColumnCreated">
    /// Fired once when a sky column comes into existence, already seeded analytically. M6b's
    /// restore hangs off this: the stored heights are read on a worker and applied afterwards,
    /// because this runs inside a box-difference sweep on the sim thread and a one-chunk step at
    /// r16 creates ~37 columns.
    /// </param>
    /// <param name="onColumnReleased">
    /// Fired when a column's last chunk goes, <em>before</em> it is dropped, so a caller can still
    /// take a reference to it. This is a column's only chance to be written: unlike a chunk, whose
    /// unview fires while it is still tracked, a column has no separate "nobody wants this" event.
    /// </param>
    /// <param name="loadMargin">See <see cref="DefaultLoadMargin"/>.</param>
    /// <param name="heights">
    /// Seeds each chunk column's skylight analytically. Optional, because most of the streaming
    /// policy has nothing to do with light and its tests should not have to supply a world
    /// generator; a streamer without one hands out no sky columns and lights nothing.
    /// </param>
    /// <param name="chunkCacheBytes">
    /// How many bytes of forgotten chunks and columns to keep for a revival. <b>Zero is off</b>
    /// and is the behaviour every milestone before the cache measured: a forgotten chunk is
    /// dropped and comes back through a load or a generate. See <see cref="Forget"/>.
    /// </param>
    /// <param name="onChunkEvicted">
    /// Fired when a cached chunk is dropped to make room while it still says
    /// <see cref="StreamedChunk.DirtySinceSave"/>. Belt and braces: a chunk enters the cache
    /// <em>after</em> its unview queued its save, so this fires only for a chunk that was marked
    /// dirty again while cached, which nothing today does — but the one failure that rule guards
    /// against is a deleted tunnel, and the hook costs a flag test.
    /// </param>
    public ChunkStreamer(
        ViewerSet? priorityViewers = null,
        Action<StreamedChunk>? onChunkViewed = null,
        Action<StreamedChunk>? onChunkUnviewed = null,
        int loadMargin = DefaultLoadMargin,
        ISurfaceHeights? heights = null,
        Action<SkyColumn>? onColumnCreated = null,
        Action<SkyColumn>? onColumnReleased = null,
        long chunkCacheBytes = 0,
        Action<StreamedChunk>? onChunkEvicted = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(loadMargin);
        ArgumentOutOfRangeException.ThrowIfNegative(chunkCacheBytes);

        _priorityViewers = priorityViewers;
        _onChunkViewed = onChunkViewed;
        _onChunkUnviewed = onChunkUnviewed;
        _onColumnCreated = onColumnCreated;
        _onColumnReleased = onColumnReleased;
        _onChunkEvicted = onChunkEvicted;
        _loadMargin = loadMargin;
        _heights = heights;
        _cacheCapacityBytes = chunkCacheBytes;
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

    // ---------------------------------------------------------------------------------------
    // The retained-chunk cache's observable side. Every number here is what the profiler prints
    // and what the cache tests assert on; none of it is consulted by the streaming policy.
    // ---------------------------------------------------------------------------------------

    /// <summary>The cap the cache was built with. Zero means the cache is off.</summary>
    public long CacheCapacityBytes => _cacheCapacityBytes;

    /// <summary>Whether forgotten chunks are kept at all.</summary>
    public bool CacheEnabled => _cacheCapacityBytes > 0;

    /// <summary>What the cache currently holds, charged by <see cref="CachedBytesOf"/>.</summary>
    public long CachedBytes => _cacheBytes;

    public int CachedChunkCount => _cachedChunks.Count;

    public int CachedColumnCount => _cachedColumns.Count;

    /// <summary>Chunks that came back from the cache rather than from a load or a generate.</summary>
    public long CacheHits { get; private set; }

    /// <summary>Columns that came back from the cache rather than being seeded and restored.</summary>
    public long ColumnCacheHits { get; private set; }

    /// <summary>Chunks dropped from the cache to stay under the cap.</summary>
    public long ChunkEvictions { get; private set; }

    /// <summary>Columns dropped from the cache to stay under the cap.</summary>
    public long ColumnEvictions { get; private set; }

    /// <summary>Whether a forgotten chunk is being kept for a revival.</summary>
    /// <remarks>
    /// Never true for a tracked chunk: a chunk is in <see cref="Chunks"/> or in the cache, and
    /// the tests fuzz that it is never both.
    /// </remarks>
    public bool IsCached(ChunkPos position) => _cachedChunks.ContainsKey(position);

    public IEnumerable<ChunkPos> CachedPositions => _cachedChunks.Keys;

    public IEnumerable<StreamedChunk> CachedChunks => _cachedChunks.Values.Select(node => node.Value.Chunk!);

    public IEnumerable<SkyColumn> CachedColumns => _cachedColumns.Values.Select(node => node.Value.Column!);

    /// <summary>A forgotten column being kept for a revival, or null.</summary>
    /// <remarks>
    /// The one caller that needs this is a column restore landing after the column's last chunk
    /// left: the restore has to be copied into the cached object, or the revival would hand back
    /// the analytic seed with no restore ever asked for again.
    /// </remarks>
    public SkyColumn? CachedColumnAt(int chunkX, int chunkZ) =>
        _cachedColumns.TryGetValue((chunkX, chunkZ), out LinkedListNode<CacheEntry>? node)
            ? node.Value.Column
            : null;

    /// <summary>What <paramref name="chunk"/> is charged against the cap. An estimate.</summary>
    public static long CachedBytesOf(StreamedChunk chunk) =>
        (chunk.Voxels?.EstimatedBytes ?? 0) + ChunkRecordOverheadBytes;

    /// <summary>What one column is charged against the cap. An estimate.</summary>
    public static long CachedBytesOfColumn => ColumnCacheBytes;

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

    /// <summary>
    /// Bumped every time a viewer is added, moved or removed. A consumer that ordered something
    /// by distance to the viewers compares this to know whether its order has gone stale.
    /// </summary>
    public int ViewerVersion { get; private set; }

    /// <summary>
    /// Chebyshev distance in chunks from <paramref name="position"/> to the nearest viewer, or
    /// <see cref="int.MaxValue"/> with no viewers — the same metric and the same "infinitely far"
    /// answer the job queue's <see cref="ViewerSet"/> gives, so that the derive queue and the job
    /// queue agree about what near means.
    /// </summary>
    public int NearestViewerDistance(ChunkPos position)
    {
        int nearest = int.MaxValue;
        foreach (Viewer viewer in _viewers.Values)
        {
            int distance = position.ChebyshevDistanceTo(viewer.Position);
            if (distance < nearest)
            {
                nearest = distance;
            }
        }

        return nearest;
    }

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
        return chunk is { Stage: GenerationStage.Lit }
               && AllNeighboursAtLeast(position, GenerationStage.Lit);
    }

    /// <summary>This chunk's light cells, or null if it is not loaded or not yet lit.</summary>
    /// <remarks>
    /// <b>A null grid stops propagation, and that is correct rather than a fallback</b> — light
    /// reaches 15 m and the margin is 16 m, so a source outside the loaded region cannot
    /// contribute inside the visible box. See <see cref="ChunkLight.MaxLevel"/>.
    /// </remarks>
    public ChunkLight? LightAt(ChunkPos chunk) => Get(chunk)?.Light;

    /// <summary>How many sky columns are alive. The refcount's observable side.</summary>
    public int SkyColumnCount => _columns.Count;

    /// <summary>Every live sky column. What a full save has to walk.</summary>
    public IEnumerable<SkyColumn> Columns => _columns.Values.Select(entry => entry.Column);

    /// <summary>The live column at (x, z), or null.</summary>
    public SkyColumn? ColumnAt(int chunkX, int chunkZ) =>
        _columns.TryGetValue((chunkX, chunkZ), out SkyColumnEntry? entry) ? entry.Column : null;

    /// <summary>
    /// Whether <paramref name="position"/>'s derived light data can be built now, and from which
    /// of the 27 neighbourhood slots.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Voxels, not a stage — and that is what keeps light off the load margin.</b> Gating this
    /// behind <see cref="GenerationStage.Features"/> for all 26 neighbours would put a third rung
    /// on the ladder that sets the margin, and the margin would have to grow from 2 to 3. Reading
    /// what voxels exist instead means the outermost loaded ring builds its data and joins the
    /// propagation while never reaching <see cref="GenerationStage.Lit"/> and never meshing.
    /// </para>
    /// <para>
    /// <b>An untracked neighbour is skipped; a tracked one without voxels blocks.</b> The
    /// difference is whether it will ever contradict what we assumed. A chunk that is not loaded
    /// reads as air and has no light grid either, so propagation stops at that face whatever the
    /// conductance bit says — harmless, and it is what lets the outermost ring proceed. A chunk
    /// that is loaded but still generating <em>will</em> have voxels shortly, and recording a
    /// face as conducting against air it is about to fill is a wall that leaks light for as long
    /// as it exists.
    /// </para>
    /// </remarks>
    /// <param name="present">
    /// Bit <c>PaddedChunk.NeighbourIndex(dx, dy, dz)</c> set for each slot that had voxels.
    /// Stored on the chunk so that a neighbour appearing later triggers exactly one rebuild —
    /// see <see cref="StreamedChunk.LightNeighbours"/>.
    /// </param>
    public bool TryLightNeighbours(ChunkPos position, out uint present)
    {
        present = 0;

        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
        {
            StreamedChunk? neighbour = Get(position.Offset(dx, dy, dz));
            if (neighbour is null)
            {
                continue;
            }

            if (!neighbour.HasVoxels)
            {
                return false;
            }

            present |= 1u << PaddedChunk.NeighbourIndex(dx, dy, dz);
        }

        // The centre is one of the 27, so this also covers "this chunk has no voxels yet".
        return (present & (1u << PaddedChunk.NeighbourIndex(0, 0, 0))) != 0;
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
            // Memory, then the cache, then the store's index, then the seed. The cache goes
            // second because a cached chunk is the very object that was saved — it can never be
            // older than the disk — and because consulting it after the store would submit a
            // load for a chunk whose bytes are already here.
            if (!TryRevive(position, out chunk))
            {
                chunk = new StreamedChunk(position);
            }

            chunk.Sky = RetainColumn(position);
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

        // The light grid goes with the chunk, cached or not. This comment used to argue that a
        // returning chunk regenerates its voxels from the seed, so a kept grid would be the one
        // thing guaranteed to be re-derived anyway — and that premise has been false since M6: a
        // returning chunk has the identical voxels whether it comes from the cache, the store or
        // the seed, so derived light is stale only if a *neighbour* or the column changed while
        // it was out. Keeping it is the cache's second slice, which needs those two invalidation
        // pushes first (docs/CHUNK-CACHE-PLAN.md, §5). Until then a stale grid would be
        // indistinguishable from a fresh one, which is the bad direction, so it goes.
        chunk.Light = null;
        chunk.LightNeighbours = 0;
        chunk.Sky = null;
        ReleaseColumn(chunk.Position);

        // Only a chunk with voxels is worth keeping: the whole saving is the load or generate
        // that would otherwise produce them. A chunk forgotten between a viewer leaving and its
        // generate landing has nothing, and would cost a record for a revival that still has to
        // do all the work.
        if (CacheEnabled && chunk.HasVoxels)
        {
            Retain(chunk);
        }
    }

    // ---------------------------------------------------------------------------------------
    // The retained-chunk cache. Chunks and columns leaving the box are kept, capped by bytes and
    // evicted least-recently-forgotten first, so that a turnaround costs no load and no generate.
    // Nothing here is a source of truth: a chunk enters *after* its unview queued its save, so
    // eviction is a free drop. docs/CHUNK-CACHE-PLAN.md has the argument.
    // ---------------------------------------------------------------------------------------

    private void Retain(StreamedChunk chunk)
    {
        // The pipeline's queue flags describe membership of queues this chunk may still be
        // named in by position. Every one of those queues checks TryGet on dequeue and skips a
        // position that is not tracked, so a stale entry is harmless — but a stale *flag* is not:
        // Push and QueueLightBuild refuse a chunk whose flag is set, and a revived chunk carrying
        // a set flag would never be looked at again. A queue that still names the position after
        // the revival does one redundant, idempotent pass.
        chunk.InWorkList = false;
        chunk.LightBuildQueued = false;
        chunk.AwaitingLight = false;
        chunk.NeedsRemesh = false;
        chunk.RemeshUrgent = false;

        // Lit means "derived and the propagation settled", and the grid just went. Features is
        // where a load lands a chunk, and it is where a revival has to land one too, or CanMesh
        // would let it mesh from a grid that has not been built.
        if (chunk.Stage == GenerationStage.Lit)
        {
            chunk.Stage = GenerationStage.Features;
        }

        // VoxelsSharedWithJob, HasEdits and DirtySinceSave are deliberately untouched. The first
        // is what makes an edit after a revival clone rather than write under a save job still
        // reading these voxels; the other two are the persistence flags, and the cache holds the
        // object precisely so that they survive.
        Insert(new CacheEntry { Chunk = chunk, Bytes = CachedBytesOf(chunk) }, _cachedChunks, chunk.Position);
    }

    private bool TryRevive(ChunkPos position, out StreamedChunk chunk)
    {
        if (!_cachedChunks.Remove(position, out LinkedListNode<CacheEntry>? node))
        {
            chunk = null!;
            return false;
        }

        Remove(node);
        chunk = node.Value.Chunk!;
        CacheHits++;
        return true;
    }

    private void Insert<TKey>(CacheEntry entry, Dictionary<TKey, LinkedListNode<CacheEntry>> index, TKey key)
        where TKey : notnull
    {
        if (entry.Bytes > _cacheCapacityBytes)
        {
            // Would evict everything and still not fit. Dropped, as it would have been without a
            // cache; a cap that small is a configuration, not an error.
            return;
        }

        _cacheBytes += entry.Bytes;
        index[key] = _cacheOrder.AddLast(entry);

        // Oldest first, and never the entry just added: it fits on its own, so the total is
        // under the cap by the time the walk could reach it.
        while (_cacheBytes > _cacheCapacityBytes)
        {
            Evict(_cacheOrder.First!);
        }
    }

    private void Remove(LinkedListNode<CacheEntry> node)
    {
        _cacheOrder.Remove(node);
        _cacheBytes -= node.Value.Bytes;
    }

    private void Evict(LinkedListNode<CacheEntry> node)
    {
        Remove(node);
        CacheEntry entry = node.Value;

        if (entry.Chunk is { } chunk)
        {
            _cachedChunks.Remove(chunk.Position);
            ChunkEvictions++;

            // The one rule that keeps "eviction is a free drop" true: a chunk that was dirtied
            // while it sat here has a copy nobody else holds, and dropping it is a deleted
            // tunnel. Announced before the drop so the handler can still take the voxels.
            if (chunk.DirtySinceSave)
            {
                _onChunkEvicted?.Invoke(chunk);
            }
        }
        else
        {
            SkyColumn column = entry.Column!;
            _cachedColumns.Remove((column.ChunkX, column.ChunkZ));
            ColumnEvictions++;
        }
    }

    /// <summary>The sky column for a chunk's (x, z), created and seeded on first use.</summary>
    private SkyColumn? RetainColumn(ChunkPos position)
    {
        if (_heights is null)
        {
            return null;
        }

        (int X, int Z) key = (position.X, position.Z);

        if (!_columns.TryGetValue(key, out SkyColumnEntry? entry))
        {
            if (_cachedColumns.Remove(key, out LinkedListNode<CacheEntry>? node))
            {
                // Neither seeded nor announced: the cached column already holds whatever the
                // store had for it, and a restore in flight when it was released lands in it
                // through CachedColumnAt. A column can only change through an edit to a loaded
                // chunk in it, and none was loaded while it was here, so it is always valid.
                Remove(node);
                entry = new SkyColumnEntry(node.Value.Column!);
                _columns[key] = entry;
                ColumnCacheHits++;
            }
            else
            {
                var column = new SkyColumn(position.X, position.Z);

                // 1,024 surface evaluations, once per column rather than once per chunk — the
                // vertical radius is what makes that ratio worth having. M5d measures it.
                column.Seed(_heights);

                entry = new SkyColumnEntry(column);
                _columns[key] = entry;

                // Announced after seeding and after the entry exists, so a handler is free to
                // queue a restore against a column that is already usable. It stays usable
                // meanwhile — lit against the unedited surface, which is wrong for a shaft and
                // right for everywhere else, and is corrected when the restore lands.
                _onColumnCreated?.Invoke(column);
            }
        }

        entry.RefCount++;
        return entry.Column;
    }

    private void ReleaseColumn(ChunkPos position)
    {
        (int X, int Z) key = (position.X, position.Z);

        if (!_columns.TryGetValue(key, out SkyColumnEntry? entry))
        {
            return;
        }

        if (--entry.RefCount <= 0)
        {
            // Read before the announcement, which is where a dirty column is handed to the save
            // and marked clean. A column that was dirty here is about to be serialised on a
            // worker with no copy-on-write to protect it — a revival could then edit it under
            // the serialiser, which is the tear a chunk's VoxelsSharedWithJob exists to prevent
            // and a column has no equivalent of. So a dirty column is dropped as it always was
            // and comes back through its restore; only a column with nothing to write is kept.
            // In play that is nearly all of them, because the autosave marks the live set clean
            // every thirty seconds.
            bool clean = !entry.Column.DirtySinceSave;

            // Announced before the drop, because this is a column's only chance to be written.
            // A chunk gets OnChunkUnviewed while it is still tracked; a column has no equivalent
            // event, so a handler that needed one after the removal would be holding nothing.
            _onColumnReleased?.Invoke(entry.Column);

            _columns.Remove(key);

            if (CacheEnabled && clean)
            {
                Insert(new CacheEntry { Column = entry.Column, Bytes = ColumnCacheBytes }, _cachedColumns, key);
            }
        }
    }

    /// <summary>
    /// Republishes viewer positions to the <see cref="ViewerSet"/> the job queue prioritises
    /// against, so streaming and scheduling never disagree about where the player is.
    /// </summary>
    private void PublishViewerPositions()
    {
        ViewerVersion++;

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
