using System.Diagnostics;
using Overburden.Core.Content;
using Overburden.Core.Lighting;
using Overburden.Core.Persistence;
using Overburden.Core.Streaming;
using Overburden.Core.Voxels;
using Overburden.Core.Worldgen;

namespace Overburden.Core.Tests;

/// <summary>
/// The retained-chunk cache, slice 1: voxels and sky columns survive leaving the box, so a
/// turnaround costs no load and no generate. docs/CHUNK-CACHE-PLAN.md.
/// </summary>
/// <remarks>
/// <para>
/// <b>The cache is a fourth source of voxels and it goes second</b> — memory, then the cache,
/// then the store's index, then the seed — and M6's rule that the only load-path failure that
/// matters is consulting the sources in the wrong order is the whole correctness argument here.
/// The oracle is therefore M6's oracle with the new source in the middle: dig, walk away through
/// the cache, come back, and the edit is there <em>and the store was written anyway</em>.
/// </para>
/// <para>
/// Every pipeline test here passes a cap; the harness default is zero, which is the committed
/// behaviour, so the six hundred tests written before the cache still run the code they were
/// written against.
/// </para>
/// </remarks>
public class ChunkCacheTests
{
    private const int Air = MaterialIds.AirNumericId;
    private const int Lamp = PipelineHarness.Lamp;
    private const long GenerousCap = 64L * 1024 * 1024;

    /// <summary>Far enough that nothing of the first box is still loaded.</summary>
    private static readonly ChunkPos FarAway = new(200, 0, 200);

    private static VoxelPos SomeVoxelIn(ChunkPos chunk) =>
        new((chunk.X << 5) + 16, (chunk.Y << 5) + 16, (chunk.Z << 5) + 16);

    /// <summary>Digs a recognisable shape and returns what the chunk should read back as.</summary>
    /// <remarks>
    /// Lamps as well as holes, for PersistenceTests' reason: the voxels are often air already,
    /// and a shape made only of air would let a bug that wrote air everywhere look like a pass.
    /// </remarks>
    private static int[] Excavate(ChunkPipeline pipeline, ChunkPos chunk)
    {
        VoxelPos origin = SomeVoxelIn(chunk);

        for (int i = 0; i < 8; i++)
        {
            pipeline.SetVoxel(new VoxelPos(origin.X + i, origin.Y, origin.Z), Air);
            pipeline.SetVoxel(new VoxelPos(origin.X + i, origin.Y + 4, origin.Z + 1), Lamp);
        }

        Assert.True(
            pipeline.Streamer.Get(chunk)!.DirtySinceSave,
            "the fixture made no actual edit; every assertion after this would be vacuous");

        return Snapshot(pipeline, chunk);
    }

    private static int[] Snapshot(ChunkPipeline pipeline, ChunkPos chunk)
    {
        var voxels = new int[VoxelConstants.ChunkVolume];
        pipeline.Streamer.Get(chunk)!.Voxels!.CopyTo(voxels);
        return voxels;
    }

    // -----------------------------------------------------------------------------------------
    // The oracle.
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void ATurnaroundBringsTheEditBackFromTheCacheAndTheStoreWasWrittenAnyway()
    {
        // M6's headline scenario with the third source in the middle. Both halves matter: the
        // edit coming back proves the cache is consulted, and the store being written proves the
        // cache never held the only copy — eviction has to be a free drop.
        var store = new InMemoryWorldStore();
        using var harness = new PipelineHarness(store: store, chunkCacheBytes: GenerousCap);
        ChunkStreamer streamer = harness.Pipeline.Streamer;

        var target = ChunkPos.Zero;
        ViewerId viewer = streamer.AddViewer(target, 1, 1);
        harness.Settle();

        int[] expected = Excavate(harness.Pipeline, target);
        harness.Settle();

        streamer.MoveViewer(viewer, FarAway);
        harness.Settle();

        Assert.False(streamer.IsTracked(target), "the chunk should have left the box");
        Assert.True(streamer.IsCached(target), "the chunk should be waiting in the cache");
        Assert.True(store.ChunkWrites > 0, "the save has to run before the chunk enters the cache");
        Assert.Contains(target, store.LoadChunkIndex());

        int loadsBefore = harness.Pipeline.ChunksLoaded;
        streamer.MoveViewer(viewer, target);
        harness.Settle();

        Assert.Equal(expected, Snapshot(harness.Pipeline, target));
        Assert.False(streamer.IsCached(target), "a revived chunk leaves the cache");
        Assert.Equal(loadsBefore, harness.Pipeline.ChunksLoaded);
        Assert.True(streamer.CacheHits > 0);
        Assert.True(harness.MeshedPositions.Contains(target), "a revived chunk still meshes");
    }

    [Fact]
    public void AHitHasItsVoxelsTheInstantItIsViewedEvenWhileItsSaveIsStillInFlight()
    {
        // The break-recipe for the ordering: consult the store's index before the cache and a
        // chunk edited, forgotten and revived before its save lands submits a load for bytes the
        // store does not have yet. With the save parked inside the store, the only way this chunk
        // has voxels synchronously at View time is the cache.
        var store = new PersistenceTests.BlockingWorldStore();
        using var harness = new PipelineHarness(store: store, chunkCacheBytes: GenerousCap);
        ChunkStreamer streamer = harness.Pipeline.Streamer;

        var target = ChunkPos.Zero;
        ViewerId viewer = streamer.AddViewer(target, 1, 1);
        harness.Settle();

        int[] expected = Excavate(harness.Pipeline, target);
        harness.Settle();

        store.Block();
        streamer.MoveViewer(viewer, FarAway);

        // Pump until the save is parked inside the store, then no further.
        var clock = Stopwatch.StartNew();
        while (!store.IsWriting)
        {
            harness.Pipeline.Update();
            Assert.True(clock.ElapsedMilliseconds < 10_000, "the save never reached the store");
            Thread.Sleep(1);
        }

        try
        {
            Assert.True(streamer.IsCached(target));

            streamer.MoveViewer(viewer, target);

            StreamedChunk revived = streamer.Get(target)!;
            Assert.NotNull(revived.Voxels);
            Assert.Equal(ChunkState.Generated, revived.State);
            Assert.True(revived.HasEdits);
            Assert.False(revived.DirtySinceSave, "it matches what the save is writing");

            var voxels = new int[VoxelConstants.ChunkVolume];
            revived.Voxels.CopyTo(voxels);
            Assert.Equal(expected, voxels);
        }
        finally
        {
            store.Release();
        }

        harness.Settle();
        Assert.Equal(0, harness.Pipeline.ChunksLoaded);
    }

    [Fact]
    public void AHitSubmitsNoLoadAndNoGenerate()
    {
        // The whole point, stated as two counters that must not move. Without a store every
        // returning chunk would be a generate; with the edited one in the store it would be a
        // load. Neither happens, and the hit count says how many chunks came back the third way.
        var store = new InMemoryWorldStore();
        using var harness = new PipelineHarness(store: store, chunkCacheBytes: GenerousCap);
        ChunkStreamer streamer = harness.Pipeline.Streamer;

        var target = ChunkPos.Zero;
        ViewerId viewer = streamer.AddViewer(target, 1, 1);
        harness.Settle();
        Excavate(harness.Pipeline, target);
        harness.Settle();

        int box = streamer.TrackedCount;
        streamer.MoveViewer(viewer, FarAway);
        harness.Settle();
        Assert.Equal(box, streamer.CachedChunkCount);

        int generated = harness.Pipeline.ChunksGenerated;
        int loaded = harness.Pipeline.ChunksLoaded;

        streamer.MoveViewer(viewer, target);
        harness.Settle();

        Assert.Equal(generated, harness.Pipeline.ChunksGenerated);
        Assert.Equal(loaded, harness.Pipeline.ChunksLoaded);
        Assert.Equal((long)box, streamer.CacheHits);
        Assert.Equal(box, streamer.TrackedCount);

        // The far box is what is waiting now — same size, forgotten by the return — and none of
        // the revived chunks is still in there.
        Assert.Equal(box, streamer.CachedChunkCount);
        Assert.All(streamer.Chunks, chunk => Assert.False(streamer.IsCached(chunk.Position)));
        Assert.True(harness.MeshedPositions.Count > 0, "the revived box still draws");
    }

    // -----------------------------------------------------------------------------------------
    // Accounting and the invariant.
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// A streamer whose chunks acquire voxels the moment they are viewed, so the cache has
    /// something to keep without a pipeline or a worker. Some are palettised so the byte
    /// accounting has more than one size to get wrong.
    /// </summary>
    private static ChunkStreamer VoxelledStreamer(long cap, bool withColumns = false, bool palettise = true) => new(
        onChunkViewed: chunk =>
        {
            if (chunk.Voxels is not null) return;

            var voxels = new VoxelChunk(3);
            if (palettise && (chunk.Position.X + chunk.Position.Z) % 3 == 0)
            {
                voxels.Set(1, 1, 1, Air);
                voxels.Set(2, 2, 2, Lamp);
            }

            chunk.Voxels = voxels;
            chunk.Stage = GenerationStage.Features;
        },
        loadMargin: 0,
        heights: withColumns ? new FlatSurface(40) : null,
        chunkCacheBytes: cap);

    internal static void AssertCacheConsistent(ChunkStreamer streamer)
    {
        long sum = 0;
        foreach (StreamedChunk chunk in streamer.CachedChunks)
        {
            Assert.False(streamer.IsTracked(chunk.Position), $"{chunk.Position} is tracked and cached");
            Assert.Equal(0, chunk.ViewCount);
            Assert.NotNull(chunk.Voxels);
            sum += ChunkStreamer.CachedBytesOf(chunk);
        }

        sum += streamer.CachedColumnCount * ChunkStreamer.CachedBytesOfColumn;

        Assert.Equal(sum, streamer.CachedBytes);
        Assert.True(streamer.CachedBytes <= streamer.CacheCapacityBytes);
    }

    [Fact]
    public void ByteAccountingIsExactAndTheCacheNeverHoldsATrackedChunk()
    {
        // Fuzzed with the box walking: after every move the charged total equals the sum over
        // the entries, the total is under the cap, and nothing is in both dictionaries. The cap
        // is small enough that eviction runs constantly, which is the recipe "skip the
        // subtraction on evict" needs to drift.
        var random = new Random(0xCAC4E);
        ChunkStreamer streamer = VoxelledStreamer(cap: 24 * 1024, withColumns: true);
        var position = ChunkPos.Zero;
        ViewerId viewer = streamer.AddViewer(position, 1, 1);

        long evictionsSeen = 0;
        for (int step = 0; step < 300; step++)
        {
            position = new ChunkPos(
                position.X + random.Next(-2, 3),
                position.Y + random.Next(-1, 2),
                position.Z + random.Next(-2, 3));
            streamer.MoveViewer(viewer, position);

            AssertCacheConsistent(streamer);
            evictionsSeen = streamer.ChunkEvictions + streamer.ColumnEvictions;
        }

        Assert.True(evictionsSeen > 0, "the cap never bit, so eviction was never exercised");
        Assert.True(streamer.CacheHits > 0, "the walk never revisited anything, so revival was never exercised");
        Assert.True(streamer.ColumnCacheHits > 0);
    }

    [Fact]
    public void EvictionIsLeastRecentlyForgottenFirstAndARevivalLeavesTheCache()
    {
        // Uniform chunks only, so every entry costs the same and a cap of exactly three records
        // holds exactly three. The single-chunk box walks along X, which forgets chunks in a
        // known order; a policy that evicted the *most* recently forgotten would fail the last
        // assertion.
        ChunkStreamer streamer = VoxelledStreamer(cap: 3 * ChunkStreamer.CachedBytesOf(new StreamedChunk(ChunkPos.Zero) { Voxels = new VoxelChunk(3) }), palettise: false);
        ViewerId viewer = streamer.AddViewer(ChunkPos.Zero, 0, 0);

        for (int x = 1; x <= 4; x++)
        {
            streamer.MoveViewer(viewer, new ChunkPos(x, 0, 0));
        }

        // Forgotten 0, 1, 2, 3; three fit, so 0 went.
        Assert.Equal(3, streamer.CachedChunkCount);
        Assert.False(streamer.IsCached(ChunkPos.Zero));
        Assert.Equal(1, streamer.ChunkEvictions);

        // Revive 2 and forget 4.
        streamer.MoveViewer(viewer, new ChunkPos(2, 0, 0));
        Assert.False(streamer.IsCached(new ChunkPos(2, 0, 0)));
        Assert.Equal(1, streamer.CacheHits);
        Assert.Equal(3, streamer.CachedChunkCount);

        // Forget 2 again: the oldest entry is now 1, and it is the one that goes — not 2, which
        // was forgotten most recently, and not 4.
        streamer.MoveViewer(viewer, new ChunkPos(5, 0, 0));
        Assert.False(streamer.IsCached(new ChunkPos(1, 0, 0)));
        Assert.True(streamer.IsCached(new ChunkPos(2, 0, 0)));
        Assert.True(streamer.IsCached(new ChunkPos(3, 0, 0)));
        Assert.True(streamer.IsCached(new ChunkPos(4, 0, 0)));
        AssertCacheConsistent(streamer);
    }

    [Fact]
    public void NothingIsCachedAtCapZeroAndAnEntryLargerThanTheCapIsDropped()
    {
        ChunkStreamer off = VoxelledStreamer(cap: 0);
        ViewerId viewer = off.AddViewer(ChunkPos.Zero, 1, 1);
        off.MoveViewer(viewer, FarAway);
        Assert.False(off.CacheEnabled);
        Assert.Equal(0, off.CachedChunkCount);
        Assert.Equal(0, off.CachedBytes);

        // A cap smaller than one palettised chunk: the uniform ones fit, the palettised do not,
        // and the ones that do not fit are dropped rather than evicting everything for nothing.
        ChunkStreamer tiny = VoxelledStreamer(cap: 1024);
        viewer = tiny.AddViewer(ChunkPos.Zero, 1, 1);
        tiny.MoveViewer(viewer, FarAway);
        Assert.True(tiny.CachedChunkCount > 0);
        Assert.All(tiny.CachedChunks, chunk => Assert.Equal(0, chunk.Voxels!.EstimatedBytes));
        AssertCacheConsistent(tiny);
    }

    [Fact]
    public void AChunkWithoutVoxelsIsNotCached()
    {
        // A chunk forgotten between a viewer leaving and its generate landing has nothing worth
        // keeping. This streamer never gives its chunks voxels at all.
        var streamer = new ChunkStreamer(loadMargin: 0, chunkCacheBytes: GenerousCap);
        ViewerId viewer = streamer.AddViewer(ChunkPos.Zero, 1, 1);
        streamer.MoveViewer(viewer, FarAway);

        Assert.Equal(0, streamer.CachedChunkCount);
        Assert.Equal(0, streamer.CachedBytes);
    }

    [Fact]
    public void ACachedChunkIsNeverTrackedWhileThePipelineIsMidFlight()
    {
        // The same invariant through the real pipeline, with results landing between moves
        // rather than the world settling first — which is the only way ForgetIfUnwanted's route
        // into the cache (a chunk retained past its viewer for a job in flight) ever runs.
        var random = new Random(0x5EED);
        using var harness = new PipelineHarness(loadMargin: 0, chunkCacheBytes: GenerousCap);
        ChunkStreamer streamer = harness.Pipeline.Streamer;

        var position = ChunkPos.Zero;
        ViewerId viewer = streamer.AddViewer(position, 1, 1);

        for (int step = 0; step < 150; step++)
        {
            position = new ChunkPos(position.X + random.Next(-1, 2), 0, position.Z + random.Next(-1, 2));
            streamer.MoveViewer(viewer, position);

            for (int pump = random.Next(0, 4); pump > 0; pump--)
            {
                harness.Pipeline.Update();
                Thread.Sleep(1);
            }

            foreach (ChunkPos cached in streamer.CachedPositions)
            {
                Assert.False(streamer.IsTracked(cached), $"{cached} is tracked and cached");
            }

            AssertCacheConsistent(streamer);
        }

        harness.Settle();
        AssertCacheConsistent(streamer);
        Assert.True(streamer.CacheHits > 0, "the walk never revived anything");

        // Quiescence cannot see a stranded chunk. A chunk revived with a stale queue flag is
        // refused by Push or QueueLightBuild, sits with no grid and no job, and the pipeline
        // reports itself settled around it — so the end state has to be asserted directly. Every
        // tracked chunk has voxels and a derived grid (deriving asks for voxels, not a stage, so
        // the ring with untracked neighbours derives too); the one chunk whose 26 neighbours are
        // all tracked — the centre, at margin 0 — reached Lit. The ring cannot: the feature
        // barrier holds it at Terrain, which is the arrangement M2c's margin exists to hide.
        Assert.All(streamer.Chunks, chunk =>
        {
            Assert.NotNull(chunk.Voxels);
            Assert.NotNull(chunk.Light);
            Assert.True(chunk.Light.HasDerived, $"{chunk.Position} never derived");
        });

        // Lit, not meshed: meshing needs the 26 neighbours Lit as well, and at margin 0 the ring
        // never gets there.
        StreamedChunk centre = streamer.Get(position)!;
        Assert.Equal(GenerationStage.Lit, centre.Stage);
    }

    // -----------------------------------------------------------------------------------------
    // Eviction and the persistence flags.
    // -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public void EvictionIsAFreeDropUnlessTheChunkWasDirtiedWhileCached(bool dirtyWhileCached, int expectedWrites)
    {
        // Clean: the save ran at unview, so the eviction touches the store not at all. Dirty: the
        // one rule that keeps "free drop" true — a chunk that somehow differs from the disk while
        // cached is handed to the save before it goes, or the cache is where a tunnel dies. The
        // flag is set by hand because nothing today can set it on a cached chunk, and the rule
        // is kept for the day something does.
        var store = new InMemoryWorldStore();
        using var harness = new PipelineHarness(loadMargin: 0, store: store, chunkCacheBytes: 32 * 1024);
        ChunkStreamer streamer = harness.Pipeline.Streamer;

        var target = ChunkPos.Zero;
        ViewerId viewer = streamer.AddViewer(target, 0, 0);
        harness.Settle();
        Excavate(harness.Pipeline, target);
        harness.Settle();

        StreamedChunk chunk = streamer.Get(target)!;
        streamer.MoveViewer(viewer, new ChunkPos(0, 0, 3));
        harness.Settle();
        Assert.True(streamer.IsCached(target));
        Assert.Equal(1, store.ChunkWrites);

        if (dirtyWhileCached)
        {
            chunk.DirtySinceSave = true;
        }

        // Walk until the cap pushes the target out.
        for (int z = 4; streamer.IsCached(target); z++)
        {
            streamer.MoveViewer(viewer, new ChunkPos(0, 0, z));
            harness.Settle();
            Assert.True(z < 200, "the cap never evicted the target");
        }

        Assert.True(streamer.ChunkEvictions > 0);
        Assert.Equal(expectedWrites, store.ChunkWrites);
        Assert.False(chunk.DirtySinceSave);
    }

    [Fact]
    public void VoxelsSharedWithJobSurvivesTheCacheSoAnEditAfterRevivalClones()
    {
        // M3a's copy-on-write reached by the cache's route: the save job is still reading these
        // voxels when the chunk comes back and is mined. Clearing the flag on cache entry would
        // let the edit write under the serialiser — a torn blob at best, a NullReferenceException
        // on a worker when Set promotes the representation at worst.
        var store = new PersistenceTests.BlockingWorldStore();
        using var harness = new PipelineHarness(store: store, chunkCacheBytes: GenerousCap);
        ChunkStreamer streamer = harness.Pipeline.Streamer;

        var target = ChunkPos.Zero;
        ViewerId viewer = streamer.AddViewer(target, 1, 1);
        harness.Settle();
        Excavate(harness.Pipeline, target);
        harness.Settle();

        StreamedChunk chunk = streamer.Get(target)!;
        VoxelChunk handedToTheSave = chunk.Voxels!;

        store.Block();
        streamer.MoveViewer(viewer, FarAway);

        var clock = Stopwatch.StartNew();
        while (!store.IsWriting)
        {
            harness.Pipeline.Update();
            Assert.True(clock.ElapsedMilliseconds < 10_000, "the save never reached the store");
            Thread.Sleep(1);
        }

        try
        {
            streamer.MoveViewer(viewer, target);
            Assert.Same(chunk, streamer.Get(target));
            Assert.True(chunk.VoxelsSharedWithJob, "the flag must survive the cache");

            Assert.True(harness.Pipeline.SetVoxel(SomeVoxelIn(target), Lamp));

            Assert.NotSame(handedToTheSave, chunk.Voxels);
            Assert.True(chunk.DirtySinceSave, "the edit after the save re-dirties the chunk");
        }
        finally
        {
            store.Release();
        }

        harness.Settle();
    }

    // -----------------------------------------------------------------------------------------
    // Light. Slice 1 dropped the grid and a revival derived afresh; slice 2 keeps it, and its
    // tests are ChunkCacheLightTests.
    // -----------------------------------------------------------------------------------------

    // -----------------------------------------------------------------------------------------
    // Sky columns.
    // -----------------------------------------------------------------------------------------

    internal static (int WorldX, int WorldZ, int Dug) DigAShaft(PipelineHarness harness, ChunkPos target)
    {
        SkyColumn column = harness.Pipeline.Streamer.Get(target)!.Sky!;
        const int LocalX = 16;
        const int LocalZ = 16;
        int worldX = (target.X << 5) + LocalX;
        int worldZ = (target.Z << 5) + LocalZ;
        int before = column.OpenAboveY(LocalX, LocalZ);

        for (int y = before - 1; y > before - 20; y--)
        {
            harness.Pipeline.SetVoxel(new VoxelPos(worldX, y, worldZ), Air);
        }

        harness.Settle();

        int dug = column.OpenAboveY(LocalX, LocalZ);
        Assert.True(dug < before, "the shaft should have lowered the column's open height");
        Assert.True(column.DirtySinceSave);
        return (worldX, worldZ, dug);
    }

    [Fact]
    public void ADirtyColumnIsNotCachedAndComesBackThroughItsRestore()
    {
        // A column released dirty is handed to the save with no copy-on-write to protect it, so
        // it is dropped as it always was and restored on return. The behaviour PersistenceTests
        // pins, re-asserted with the cache on, plus the assertion that it did not go through it.
        var store = new InMemoryWorldStore();
        using var harness = new PipelineHarness(store: store, chunkCacheBytes: GenerousCap);
        ChunkStreamer streamer = harness.Pipeline.Streamer;

        var target = ChunkPos.Zero;
        ViewerId viewer = streamer.AddViewer(target, 2, 2);
        harness.Settle();
        (_, _, int dug) = DigAShaft(harness, target);

        streamer.MoveViewer(viewer, FarAway);
        harness.Settle();

        Assert.Null(streamer.ColumnAt(target.X, target.Z));
        Assert.Null(streamer.CachedColumnAt(target.X, target.Z));
        Assert.True(harness.Pipeline.ColumnsSaved > 0);

        streamer.MoveViewer(viewer, target);
        harness.Settle();

        Assert.Equal(dug, streamer.Get(target)!.Sky!.OpenAboveY(16, 16));
        Assert.True(harness.Pipeline.ColumnsRestored > 0);
    }

    [Fact]
    public void ACleanColumnComesBackFromTheCacheWithoutASeedOrARestore()
    {
        // The autosave marks the live set clean, so in play nearly every column is clean when it
        // is released — and a clean edited column is exactly the one whose restore is a serial
        // load the cache saves.
        var store = new InMemoryWorldStore();
        using var harness = new PipelineHarness(store: store, chunkCacheBytes: GenerousCap);
        ChunkStreamer streamer = harness.Pipeline.Streamer;

        var target = ChunkPos.Zero;
        ViewerId viewer = streamer.AddViewer(target, 2, 2);
        harness.Settle();
        (_, _, int dug) = DigAShaft(harness, target);

        // The autosave, standing in for the timer.
        harness.Pipeline.SaveAll();
        SkyColumn column = streamer.Get(target)!.Sky!;
        Assert.False(column.DirtySinceSave);

        streamer.MoveViewer(viewer, FarAway);
        harness.Settle();
        Assert.Same(column, streamer.CachedColumnAt(target.X, target.Z));

        int restores = harness.Pipeline.ColumnsRestored;
        streamer.MoveViewer(viewer, target);
        harness.Settle();

        Assert.Same(column, streamer.Get(target)!.Sky);
        Assert.Equal(dug, column.OpenAboveY(16, 16));
        Assert.Equal(restores, harness.Pipeline.ColumnsRestored);
        Assert.True(streamer.ColumnCacheHits > 0);
    }

    [Fact]
    public void ARestoreLandingAfterTheColumnWasReleasedReachesTheCachedColumn()
    {
        // A column created, released before its read lands, and cached — because an unrestored
        // column is clean. The restore then has to find it in the cache, or the revival would
        // hand back the analytic seed and never ask for the restore again: a shaft dark for a
        // reason nothing on screen can explain, arriving only in a played world with a slow disk.
        var store = new PersistenceTests.SlowColumnStore();
        var target = ChunkPos.Zero;
        int dug;

        using (var first = new PipelineHarness(store: store))
        {
            first.Pipeline.Streamer.AddViewer(target, 2, 2);
            first.Settle();
            (_, _, dug) = DigAShaft(first, target);
        }

        Assert.Contains((target.X, target.Z), store.LoadColumnIndex());

        using var harness = new PipelineHarness(store: store, chunkCacheBytes: GenerousCap);
        ChunkStreamer streamer = harness.Pipeline.Streamer;

        // The restore is submitted straight from the column's creation, not from the pump, so
        // it is parked inside the store before a single chunk job exists — which is what lets
        // the viewer leave with nothing retained and the column's refcount reach zero.
        store.HoldColumns();
        ViewerId viewer = streamer.AddViewer(target, 0, 0);

        // Released while the read is parked; the column was never dirty, so it is kept.
        streamer.RemoveViewer(viewer);
        SkyColumn? cached = streamer.CachedColumnAt(target.X, target.Z);
        Assert.NotNull(cached);
        Assert.NotEqual(dug, cached.OpenAboveY(16, 16));

        store.ReleaseColumns();
        harness.Settle();

        Assert.Equal(dug, cached.OpenAboveY(16, 16));
        Assert.True(harness.Pipeline.ColumnsRestored > 0);

        viewer = streamer.AddViewer(target, 0, 0);
        harness.Settle();
        Assert.Same(cached, streamer.Get(target)!.Sky);
        Assert.Equal(dug, streamer.Get(target)!.Sky!.OpenAboveY(16, 16));
    }
}
