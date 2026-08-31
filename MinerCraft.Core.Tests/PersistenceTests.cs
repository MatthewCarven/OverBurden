using MinerCraft.Core.Content;
using MinerCraft.Core.Lighting;
using MinerCraft.Core.Persistence;
using MinerCraft.Core.Streaming;
using MinerCraft.Core.Voxels;

namespace MinerCraft.Core.Tests;

/// <summary>
/// The tests M6 exists for: an edited chunk that unloads comes back edited.
/// </summary>
/// <remarks>
/// <para>
/// <b>The oracle is the whole milestone stated as a boolean</b> — dig, fly away far enough that
/// the chunk unloads, come back, and the hole is still there. No reference implementation to get
/// wrong, the same category as M4a's sealed room.
/// </para>
/// <para>
/// Every race below already had a documented answer in <see cref="ChunkPipeline"/>, written for
/// meshes. That is the good news and the trap together: where a mesh race leaves stale geometry on
/// screen, the same race on the save path loses a tunnel, so each one is tested for its own sake
/// rather than assumed to have come across.
/// </para>
/// </remarks>
public class PersistenceTests
{
    private const int Air = MaterialIds.AirNumericId;
    private const int Lamp = 4;

    /// <summary>Far enough that nothing of the first box is still loaded.</summary>
    private static readonly ChunkPos FarAway = new(200, 0, 200);

    private static VoxelPos SomeVoxelIn(ChunkPos chunk) =>
        new((chunk.X << 5) + 16, (chunk.Y << 5) + 16, (chunk.Z << 5) + 16);

    /// <summary>
    /// Makes one guaranteed edit, and fails loudly if it turned out not to be one.
    /// </summary>
    /// <remarks>
    /// <b>A lamp, because carving air is not reliably an edit.</b> <see cref="ChunkPipeline.SetVoxel"/>
    /// returns early when the material is already there, and the voxel these tests reach for is
    /// often air already — so the first version of this suite quietly made no edit at all and four
    /// tests failed on a fixture rather than on the code. Nothing generates a lamp, so placing one
    /// always changes something; the assertion is here so that if it ever stops being true, the
    /// fixture says so instead of the assertions going vacuous.
    /// </remarks>
    private static VoxelPos EditOneVoxel(ChunkPipeline pipeline, ChunkPos chunk)
    {
        VoxelPos at = SomeVoxelIn(chunk);
        Assert.True(pipeline.SetVoxel(at, Lamp), "the write should have reached a loaded chunk");
        Assert.True(
            pipeline.Streamer.Get(chunk)!.DirtySinceSave,
            "the fixture made no actual edit; the assertions below would be vacuous");

        return at;
    }

    /// <summary>Digs a recognisable shape and returns what the chunk should read back as.</summary>
    private static int[] Excavate(ChunkPipeline pipeline, ChunkPos chunk)
    {
        VoxelPos origin = SomeVoxelIn(chunk);

        for (int i = 0; i < 8; i++)
        {
            pipeline.SetVoxel(new VoxelPos(origin.X + i, origin.Y, origin.Z), Air);
            pipeline.SetVoxel(new VoxelPos(origin.X, origin.Y + i, origin.Z), Air);

            // Lamps as well as holes, and they are what make this reliably an edit: the voxels
            // above are often air already, in which case SetVoxel correctly does nothing. A shape
            // made only of air would also let a bug that wrote air everywhere look like a pass.
            pipeline.SetVoxel(new VoxelPos(origin.X + i, origin.Y + 4, origin.Z + 1), Lamp);
        }

        Assert.True(
            pipeline.Streamer.Get(chunk)!.DirtySinceSave,
            "the fixture made no actual edit; every assertion after this would be vacuous");

        return Snapshot(pipeline, chunk);
    }

    private static int[] Snapshot(ChunkPipeline pipeline, ChunkPos chunk)
    {
        StreamedChunk streamed = pipeline.Streamer.Get(chunk)!;
        var voxels = new int[VoxelConstants.ChunkVolume];
        streamed.Voxels!.CopyTo(voxels);
        return voxels;
    }

    private static void AssertSame(int[] expected, int[] actual)
    {
        for (int i = 0; i < expected.Length; i++)
        {
            if (expected[i] == actual[i]) continue;

            var (x, y, z) = VoxelPos.FromIndex(i);
            Assert.Fail($"Voxel {i} ({x}, {y}, {z}): expected {expected[i]}, got {actual[i]}.");
        }
    }

    // -----------------------------------------------------------------------------------------
    // The oracle.
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void AnEditedChunkThatUnloadsComesBackEdited()
    {
        // The one sentence this milestone is for. Until M6 this test could not pass: the chunk
        // regenerated from the seed and the tunnel was solid rock again.
        var store = new InMemoryWorldStore();
        using var harness = new PipelineHarness(store: store);

        var target = ChunkPos.Zero;
        ViewerId viewer = harness.Pipeline.Streamer.AddViewer(target, 1, 1);
        harness.Settle();

        int[] expected = Excavate(harness.Pipeline, target);
        harness.Settle();

        // Fly away far enough that nothing of the old box survives, then come back.
        harness.Pipeline.Streamer.MoveViewer(viewer, FarAway);
        harness.Settle();
        Assert.False(harness.Pipeline.Streamer.IsTracked(target), "the chunk should have unloaded");

        harness.Pipeline.Streamer.MoveViewer(viewer, target);
        harness.Settle();

        AssertSame(expected, Snapshot(harness.Pipeline, target));
        Assert.True(harness.Pipeline.ChunksLoaded > 0, "it should have come from the store");
    }

    [Fact]
    public void AnEditedChunkSurvivesTheWholePipelineBeingTornDownAndRebuilt()
    {
        // Quitting the game and starting it again, which is the other half of "play stops losing
        // work" and the one a moving viewer cannot test.
        var store = new InMemoryWorldStore();
        var target = ChunkPos.Zero;
        int[] expected;

        using (var first = new PipelineHarness(store: store))
        {
            first.Pipeline.Streamer.AddViewer(target, 1, 1);
            first.Settle();
            expected = Excavate(first.Pipeline, target);
            first.Settle();
        }

        using var second = new PipelineHarness(store: store);
        second.Pipeline.Streamer.AddViewer(target, 1, 1);
        second.Settle();

        AssertSame(expected, Snapshot(second.Pipeline, target));
    }

    [Fact]
    public void AnUneditedChunkIsNeverWritten()
    {
        // The whole point of HasEdits being a bit rather than a guess. A world that stored every
        // chunk it loaded would grow without bound while adding nothing a seed cannot reproduce.
        var store = new InMemoryWorldStore();
        using var harness = new PipelineHarness(store: store);

        ViewerId viewer = harness.Pipeline.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
        harness.Settle();

        harness.Pipeline.Streamer.MoveViewer(viewer, FarAway);
        harness.Settle();

        Assert.Empty(store.LoadChunkIndex());
        Assert.Equal(0, harness.Pipeline.ChunksSaved);
    }

    [Fact]
    public void OnlyTheEditedChunksAreWritten()
    {
        var store = new InMemoryWorldStore();
        using var harness = new PipelineHarness(store: store);

        ViewerId viewer = harness.Pipeline.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
        harness.Settle();

        Excavate(harness.Pipeline, ChunkPos.Zero);
        harness.Settle();

        harness.Pipeline.Streamer.MoveViewer(viewer, FarAway);
        harness.Settle();

        // The dig spans one chunk's interior, so exactly one chunk should have been written —
        // not the eight a border edit would touch, and not the whole box.
        Assert.Equal([ChunkPos.Zero], store.LoadChunkIndex().ToArray());
    }

    // -----------------------------------------------------------------------------------------
    // The two flags, and the race between them.
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void EditingSetsBothFlagsAndSavingClearsOnlyOne()
    {
        var store = new InMemoryWorldStore();
        using var harness = new PipelineHarness(store: store);

        harness.Pipeline.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
        harness.Settle();

        StreamedChunk chunk = harness.Pipeline.Streamer.Get(ChunkPos.Zero)!;
        Assert.False(chunk.HasEdits);
        Assert.False(chunk.DirtySinceSave);

        EditOneVoxel(harness.Pipeline, ChunkPos.Zero);

        Assert.True(chunk.HasEdits);
        Assert.True(chunk.DirtySinceSave);

        harness.Pipeline.SaveAll();

        // HasEdits is permanent: this chunk can never again come from the seed. DirtySinceSave is
        // transient and has been satisfied. One flag carrying both meanings would have to clear
        // here, and the next unload would then skip the chunk entirely.
        Assert.True(chunk.HasEdits);
        Assert.False(chunk.DirtySinceSave);
    }

    [Fact]
    public void AnEditDuringASaveIsNotSwallowed()
    {
        // The reason DirtySinceSave clears at submission rather than on completion, and the exact
        // shape of NeedsRemesh surviving a mesh job. A save in flight was built from a snapshot
        // taken before this edit; if completion cleared the flag, the edit would never be written.
        var store = new BlockingWorldStore();
        using var harness = new PipelineHarness(store: store);

        var target = ChunkPos.Zero;
        ViewerId viewer = harness.Pipeline.Streamer.AddViewer(target, 1, 1);
        harness.Settle();

        EditOneVoxel(harness.Pipeline, target);
        harness.Settle();

        // Save what is there, then edit again before anything else runs. The chunk must come back
        // dirty: the blob just written was built from the pre-edit snapshot.
        harness.Pipeline.SaveAll();

        VoxelPos second = new(SomeVoxelIn(target).X + 3, SomeVoxelIn(target).Y + 3, SomeVoxelIn(target).Z);
        Assert.True(harness.Pipeline.SetVoxel(second, Lamp));

        StreamedChunk chunk = harness.Pipeline.Streamer.Get(target)!;
        Assert.True(chunk.DirtySinceSave, "an edit after a save must re-dirty the chunk");

        harness.Pipeline.Streamer.MoveViewer(viewer, FarAway);
        harness.Settle();

        harness.Pipeline.Streamer.MoveViewer(viewer, target);
        harness.Settle();

        Assert.Equal(Lamp, harness.Pipeline.Streamer.Get(target)!.Voxels!.Get(
            second.Local.X, second.Local.Y, second.Local.Z));
    }

    [Fact]
    public void AChunkHandedToASaveIsClonedRatherThanMutated()
    {
        // M3a's copy-on-write, reached by a route it was not written for. A chunk queued for
        // saving is usually about to be forgotten, but it can be revived and mined before the job
        // runs — and VoxelChunk.Set can promote the representation and swap the backing arrays
        // under the serialiser, which is a NullReferenceException on a worker thread rather than
        // merely a stale voxel.
        var store = new InMemoryWorldStore();
        using var harness = new PipelineHarness(store: store);

        harness.Pipeline.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
        harness.Settle();

        EditOneVoxel(harness.Pipeline, ChunkPos.Zero);

        StreamedChunk chunk = harness.Pipeline.Streamer.Get(ChunkPos.Zero)!;

        // Queue it without letting the job run.
        harness.Pipeline.Streamer.RemoveViewer(new ViewerId(1));

        Assert.True(chunk.VoxelsSharedWithJob, "the save must claim the voxels first");
    }

    // -----------------------------------------------------------------------------------------
    // The load path.
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void AChunkTheIndexClaimsButTheStoreLacksFallsBackToGenerating()
    {
        // Not impossible and not an error: the index is a snapshot taken at open. Falling back to
        // the seed is the only honest recovery here — and it is deliberately *not* what happens to
        // a blob that exists and will not parse, which faults instead of quietly becoming
        // hillside where a base used to be.
        var store = new ForgetfulWorldStore();
        using var harness = new PipelineHarness(store: store);

        var target = ChunkPos.Zero;
        ViewerId viewer = harness.Pipeline.Streamer.AddViewer(target, 1, 1);
        harness.Settle();

        Excavate(harness.Pipeline, target);
        harness.Settle();

        harness.Pipeline.Streamer.MoveViewer(viewer, FarAway);
        harness.Settle();

        // The store loses the blob but the pipeline's index still claims it.
        store.LoseEverything();

        harness.Pipeline.Streamer.MoveViewer(viewer, target);
        harness.Settle();

        Assert.Empty(harness.Faults);
        Assert.True(harness.Pipeline.Streamer.Get(target)!.HasVoxels);
    }

    [Fact]
    public void ACorruptBlobFaultsRatherThanSilentlyRegenerating()
    {
        // The failure that must be loud. A world whose blobs will not parse has a real problem,
        // and replacing the player's base with untouched hillside while reporting nothing is the
        // exact failure this milestone exists to prevent, arriving as a well-meant recovery.
        var store = new InMemoryWorldStore();
        using var harness = new PipelineHarness(store: store);

        var target = ChunkPos.Zero;
        ViewerId viewer = harness.Pipeline.Streamer.AddViewer(target, 1, 1);
        harness.Settle();

        Excavate(harness.Pipeline, target);
        harness.Settle();

        harness.Pipeline.Streamer.MoveViewer(viewer, FarAway);
        harness.Settle();

        // Rewrite the stored blob as nonsense, keeping the key.
        store.Write([(target, [ChunkBlob.Version, 0, 0, 0, 0, 0, 0, 0, 0, 0])], []);

        harness.Pipeline.Streamer.MoveViewer(viewer, target);

        // Settle asserts no faults, so pump by hand and look for one.
        for (int i = 0; i < 2000 && harness.Faults.Count == 0; i++)
        {
            harness.Pipeline.Update();
            Thread.Sleep(1);
        }

        Assert.NotEmpty(harness.Faults);
    }

    // -----------------------------------------------------------------------------------------
    // Sky columns — the one lighting structure that has to persist.
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void ADugColumnComesBackWithItsSkyHeightsRatherThanTheAnalyticSeed()
    {
        // Skipped, a reloaded shaft seeds from the *pre-dig* surface, every cell fails
        // IsCellOpenToSky, and the shaft is dark for a reason nothing on screen can explain.
        var store = new InMemoryWorldStore();
        using var harness = new PipelineHarness(store: store);

        var target = ChunkPos.Zero;
        ViewerId viewer = harness.Pipeline.Streamer.AddViewer(target, 2, 2);
        harness.Settle();

        // Dig a shaft straight down through the surface, which is what moves a sky height.
        SkyColumn column = harness.Pipeline.Streamer.Get(target)!.Sky!;
        int localX = 16, localZ = 16;
        int worldX = (target.X << 5) + localX;
        int worldZ = (target.Z << 5) + localZ;
        int before = column.OpenAboveY(localX, localZ);

        for (int y = before - 1; y > before - 20; y--)
        {
            harness.Pipeline.SetVoxel(new VoxelPos(worldX, y, worldZ), Air);
        }

        harness.Settle();

        int dug = column.OpenAboveY(localX, localZ);
        Assert.True(dug < before, "the shaft should have lowered the column's open height");
        Assert.True(column.HasEdits);

        harness.Pipeline.Streamer.MoveViewer(viewer, FarAway);
        harness.Settle();

        // Not SkyColumnCount == 0: the viewer has landed somewhere else and has a fresh box of
        // columns there. What matters is that *this* column went.
        Assert.Null(harness.Pipeline.Streamer.ColumnAt(target.X, target.Z));
        Assert.True(harness.Pipeline.ColumnsSaved > 0, "the column should have been written");

        harness.Pipeline.Streamer.MoveViewer(viewer, target);
        harness.Settle();

        Assert.Equal(dug, harness.Pipeline.Streamer.Get(target)!.Sky!.OpenAboveY(localX, localZ));
        Assert.True(harness.Pipeline.ColumnsRestored > 0);
    }

    [Fact]
    public void AReloadedShaftIsActuallyLitAndNotJustGeometricallyRestored()
    {
        // The hole the break-recipe battery found: removing QueueColumnLightBuild from the restore
        // path failed nothing, because the column test asserted the *heights* came back and
        // stopped there. Restoring heights nobody relights is the classic voxel bug with extra
        // steps — a shaft that is open to the sky and dark anyway.
        //
        // M5c-2's lesson exactly: every part of the path was tested and the wire between them was
        // not.
        var store = new InMemoryWorldStore();
        using var harness = new PipelineHarness(store: store);

        var target = ChunkPos.Zero;
        ViewerId viewer = harness.Pipeline.Streamer.AddViewer(target, 2, 2);
        harness.Settle();

        SkyColumn column = harness.Pipeline.Streamer.Get(target)!.Sky!;
        const int LocalX = 16;
        const int LocalZ = 16;
        int worldX = (target.X << 5) + LocalX;
        int worldZ = (target.Z << 5) + LocalZ;
        int surface = column.OpenAboveY(LocalX, LocalZ);

        // A shaft wide enough that a whole light cell is open to the sky — a one-voxel bore never
        // is, which is the trap M5c-1 recorded about its own shaft test.
        for (int y = surface - 1; y > surface - 16; y--)
        {
            for (int dx = 0; dx < 4; dx++)
            {
                for (int dz = 0; dz < 4; dz++)
                {
                    harness.Pipeline.SetVoxel(new VoxelPos(worldX + dx, y, worldZ + dz), Air);
                }
            }
        }

        harness.Settle();

        var deep = new VoxelPos(worldX + 1, surface - 12, worldZ + 1);
        int litBefore = SkyLightAt(harness.Pipeline, deep);
        Assert.True(litBefore > 0, "the freshly dug shaft should be carrying skylight down it");

        harness.Pipeline.Streamer.MoveViewer(viewer, FarAway);
        harness.Settle();
        harness.Pipeline.Streamer.MoveViewer(viewer, target);
        harness.Settle();

        // The assertion the battery said was missing: not "the heights came back" but "the light
        // did".
        //
        // It does not isolate QueueColumnLightBuild, and the battery said so: removing that call
        // leaves this green. See ASlowColumnReadHoldsTheChunkLoadsBehindIt for why the relight is
        // belt and braces rather than load-bearing, and why no test here can make it otherwise.
        Assert.Equal(litBefore, SkyLightAt(harness.Pipeline, deep));
    }

    [Fact]
    public void ASlowColumnReadHoldsTheChunkLoadsBehindIt()
    {
        // This began as an attempt to force the ordering QueueColumnLightBuild guards against —
        // chunks deriving their sky flags against the analytic seed, and the stored column
        // arriving afterwards — and it cannot be done, for a reason worth keeping.
        //
        // A column read and a chunk load are both on the serial lane, so holding one holds the
        // other. The column job is submitted when the column is created, during the streamer's
        // box sweep; a chunk's load job is submitted later, in the same Update's Pump. So the
        // restore is already running, or already queued ahead, by the time any chunk of that
        // column can ask for its voxels.
        //
        // That makes the relight belt and braces rather than load-bearing — M4a's zeroable
        // contact epsilon again, and this time the mechanism a test would have to defeat is the
        // same mechanism that makes the case rare. What is pinned instead is the structural
        // property itself: the column comes back before any chunk of it has light.
        var store = new SlowColumnStore();
        using var harness = new PipelineHarness(store: store);

        var target = ChunkPos.Zero;
        ViewerId viewer = harness.Pipeline.Streamer.AddViewer(target, 2, 2);
        harness.Settle();

        SkyColumn column = harness.Pipeline.Streamer.Get(target)!.Sky!;
        const int LocalX = 16;
        const int LocalZ = 16;
        int worldX = (target.X << 5) + LocalX;
        int worldZ = (target.Z << 5) + LocalZ;
        int surface = column.OpenAboveY(LocalX, LocalZ);

        for (int y = surface - 1; y > surface - 16; y--)
        {
            for (int dx = 0; dx < 4; dx++)
            {
                for (int dz = 0; dz < 4; dz++)
                {
                    harness.Pipeline.SetVoxel(new VoxelPos(worldX + dx, y, worldZ + dz), Air);
                }
            }
        }

        harness.Settle();

        var deep = new VoxelPos(worldX + 1, surface - 12, worldZ + 1);
        int litBefore = SkyLightAt(harness.Pipeline, deep);
        Assert.True(litBefore > 0);

        harness.Pipeline.Streamer.MoveViewer(viewer, FarAway);
        harness.Settle();

        // Hold every column read, then come back.
        store.HoldColumns();
        harness.Pipeline.Streamer.MoveViewer(viewer, target);

        for (int i = 0; i < 300; i++)
        {
            harness.Pipeline.Update();
            Thread.Sleep(1);
        }

        // The chunk cannot have loaded: its load job is behind the held column read on the same
        // lane. This is the property, and it is what makes the restore-before-derive ordering
        // structural rather than lucky.
        Assert.Null(harness.Pipeline.Streamer.Get(target)?.Voxels);
        Assert.Equal(0, harness.Pipeline.ChunksLoaded);

        store.ReleaseColumns();
        harness.Settle();

        Assert.True(harness.Pipeline.ChunksLoaded > 0);
        Assert.Equal(litBefore, SkyLightAt(harness.Pipeline, deep));
    }

    private static int SkyLightAt(ChunkPipeline pipeline, VoxelPos voxel)
    {
        LightCellPos cell = LightCellPos.FromVoxel(voxel);
        ChunkLight light = pipeline.Streamer.LightAt(cell.Chunk)
            ?? throw new InvalidOperationException($"no light grid at {cell.Chunk}");

        return light.Get(cell.LocalIndex, LightChannel.Sky);
    }

    [Fact]
    public void TheSerialLaneKeepsALoadBehindTheSaveItWouldRace()
    {
        // The index claims a chunk at submission, before its bytes are on disk. A chunk revived in
        // that window submits a load for a blob that is not there yet — and what stops the two
        // overlapping is that both are serial, not that the timing happens to work out.
        //
        // The battery found this claim untested: making either job non-serial failed nothing.
        var store = new BlockingWorldStore();
        using var harness = new PipelineHarness(store: store);

        var target = ChunkPos.Zero;
        ViewerId viewer = harness.Pipeline.Streamer.AddViewer(target, 1, 1);
        harness.Settle();

        int[] expected = Excavate(harness.Pipeline, target);
        harness.Settle();

        // Hold the writer inside the store, then unload so the save is submitted and blocks.
        store.Block();
        harness.Pipeline.Streamer.MoveViewer(viewer, FarAway);

        for (int i = 0; i < 500 && !store.IsWriting; i++)
        {
            harness.Pipeline.Update();
            Thread.Sleep(1);
        }

        Assert.True(store.IsWriting, "the save should be inside the store");

        // Come back while the write is still in flight. The load this queues must not run until
        // the save has finished.
        harness.Pipeline.Streamer.MoveViewer(viewer, target);

        for (int i = 0; i < 50; i++)
        {
            harness.Pipeline.Update();
            Thread.Sleep(1);
        }

        store.Release();
        harness.Settle();

        AssertSame(expected, Snapshot(harness.Pipeline, target));
    }

    [Fact]
    public void AnUntouchedColumnIsNeverWritten()
    {
        // The tight half of the column's dirty rule. Digging below a column's top leaves the open
        // height exactly where Seed put it, so the column is still reproducible and correctly
        // never written — and digging is most of this game.
        var store = new InMemoryWorldStore();
        using var harness = new PipelineHarness(store: store);

        ViewerId viewer = harness.Pipeline.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
        harness.Settle();

        // Deep underground: well below the surface, so no sky height moves.
        harness.Pipeline.SetVoxel(new VoxelPos(16, -60, 16), Air);
        harness.Settle();

        harness.Pipeline.Streamer.MoveViewer(viewer, FarAway);
        harness.Settle();

        Assert.Empty(store.LoadColumnIndex());
    }

    [Fact]
    public void TheRestoreIsCopiedIntoTheLiveColumnRatherThanReplacingIt()
    {
        // Every StreamedChunk in the column caches the SkyColumn object, so installing a different
        // instance would leave already-loaded chunks pointing at the old one — a shaft lit
        // correctly at the bottom and from the pre-dig surface at the top.
        var a = new SkyColumn(2, -3);
        var b = new SkyColumn(2, -3);
        b.SetOpenAboveY(1, 1, -42);
        b.MarkEdited();

        a.CopyFrom(b);

        Assert.Equal(-42, a.OpenAboveY(1, 1));
        Assert.True(a.HasEdits);

        // And a restore addressed at the wrong column is refused rather than writing another
        // shaft's floor into this one.
        Assert.Throws<ArgumentException>(() => a.CopyFrom(new SkyColumn(9, 9)));
    }

    // -----------------------------------------------------------------------------------------
    // Shutdown, which is the single most likely way to ship a data-loss bug here.
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void DisposingThePipelineWritesWhatWasStillDirty()
    {
        // JobResult's contract is that a job still queued at shutdown reports Dropped. A world
        // left to the normal save path at quit loses whatever had not been picked up, so Dispose
        // saves synchronously before it cancels anything.
        var store = new InMemoryWorldStore();

        using (var harness = new PipelineHarness(store: store))
        {
            harness.Pipeline.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
            harness.Settle();

            EditOneVoxel(harness.Pipeline, ChunkPos.Zero);

            // Deliberately no Settle: the edit has not been through a single Update, so nothing
            // has had a chance to save it the ordinary way.
            Assert.Empty(store.LoadChunkIndex());
        }

        Assert.Single(store.LoadChunkIndex());
    }

    [Fact]
    public void SaveAllIsIdempotentAndCheapWhenNothingIsDirty()
    {
        var store = new InMemoryWorldStore();
        using var harness = new PipelineHarness(store: store);

        harness.Pipeline.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
        harness.Settle();
        EditOneVoxel(harness.Pipeline, ChunkPos.Zero);

        (int Chunks, int Columns) first = harness.Pipeline.SaveAll();
        Assert.Equal(1, first.Chunks);

        // A column comes with it, and finding that out was worth the failing assertion: the edit
        // places an opaque lamp *above* the surface, which closes the sky column down to it and
        // marks the column edited. That is M6c's placement case arriving a part early — building a
        // roof moves a sky height exactly as digging a shaft does — and it means a built structure
        // persists its lighting rather than only a dug one.
        Assert.Equal(1, first.Columns);

        // The second call has nothing left to do, which is what makes the flags right rather than
        // merely present.
        Assert.Equal((0, 0), harness.Pipeline.SaveAll());
        Assert.True(store.FlushCalls >= 2);
    }

    [Fact]
    public void SaveAllWaitsForTheSerialLaneRatherThanEnteringTheStoreBesideIt()
    {
        // M6b-1 wrote IWorldStore's "one caller at a time" contract and justified it by every call
        // arriving on the serial lane. SaveAll is the second caller, and M6b-3's autosave timer is
        // what turns the overlap from theoretical into ordinary: a timer firing while the player
        // walks back into a region they dug, which is one chunk load per chunk. Against SQLite an
        // overlap is two threads binding parameters on one shared prepared statement, so the
        // failure is the file rather than a stale read.
        var store = new ConcurrencyWatchingStore();
        using var harness = new PipelineHarness(store: store);

        var target = ChunkPos.Zero;
        ViewerId viewer = harness.Pipeline.Streamer.AddViewer(target, 1, 1);
        harness.Settle();

        Excavate(harness.Pipeline, target);
        harness.Settle();

        // Away and back, so that coming back is a *load* rather than a generate — the load is what
        // puts the serial lane inside the store while the sim thread is free to call SaveAll.
        harness.Pipeline.Streamer.MoveViewer(viewer, FarAway);
        harness.Settle();

        store.HoldReads();
        harness.Pipeline.Streamer.MoveViewer(viewer, target);

        for (int i = 0; i < 500 && !store.IsReading; i++)
        {
            harness.Pipeline.Update();
            Thread.Sleep(1);
        }

        Assert.True(store.IsReading, "a chunk load should be parked inside the store");

        // Something dirty for SaveAll to write, in a chunk that generated rather than loaded —
        // otherwise the call has nothing to do and would pass without ever reaching the store.
        var neighbour = new ChunkPos(1, 0, 0);
        for (int i = 0; i < 500 && harness.Pipeline.Streamer.Get(neighbour)?.Voxels is null; i++)
        {
            harness.Pipeline.Update();
            Thread.Sleep(1);
        }

        EditOneVoxel(harness.Pipeline, neighbour);

        // Released from another thread, because SaveAll is supposed to block until the lane is
        // clear — so the test has to be the thing that lets it go.
        var releaser = new Thread(() =>
        {
            Thread.Sleep(100);
            store.ReleaseReads();
        });

        releaser.Start();

        (int chunks, _) = harness.Pipeline.SaveAll();
        releaser.Join();

        Assert.True(chunks > 0, "the save should have written once the lane was clear");
        Assert.False(store.SawOverlap, "SaveAll entered the store beside the serial lane");
    }

    [Fact]
    public void ASynchronousSaveThatThrowsKeepsTheWorkRatherThanMarkingItSaved()
    {
        // The asynchronous twin has restored a failed batch since M6b-2 and this path had not,
        // which matters because this is the path an autosave timer and a quit both take. The
        // dirty flags clear *before* the write — deliberately, so an edit arriving during it is
        // not swallowed — so a throw after that point left the world marked saved and unwritten.
        var store = new ThrowingWorldStore();
        using var harness = new PipelineHarness(store: store);

        harness.Pipeline.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
        harness.Settle();
        EditOneVoxel(harness.Pipeline, ChunkPos.Zero);

        Assert.Equal((0, 0), harness.Pipeline.SaveAll());
        Assert.Equal(1, harness.Pipeline.FailedSaves);
        Assert.Empty(store.LoadChunkIndex());

        // Nothing re-dirties the chunk, so the only way this can land is if the failed batch was
        // kept — the same end-state oracle as the asynchronous twin, for the same reason.
        store.Recover();

        (int chunks, _) = harness.Pipeline.SaveAll();

        Assert.Equal(1, chunks);
        Assert.Single(store.LoadChunkIndex());
    }

    [Fact]
    public void AFailedSaveIsRetriedUntilItLands()
    {
        // The opposite of what a faulted generate does, and the asymmetry is the point: a lost
        // mesh is rebuilt from data that still exists, a lost save is gone.
        //
        // Asserting that the work is "back on the queue" turned out to be asserting on a state
        // that lasts less than one frame — ApplySave runs inside ApplyResults and SubmitSaves runs
        // later in the *same* Update, so by the time anything can look, the retry is already in
        // flight. What actually matters is the end state: a store that fails and then recovers
        // ends up holding the data.
        var store = new ThrowingWorldStore();
        using var harness = new PipelineHarness(store: store);

        ViewerId viewer = harness.Pipeline.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
        harness.Settle();

        EditOneVoxel(harness.Pipeline, ChunkPos.Zero);
        harness.Pipeline.Streamer.MoveViewer(viewer, FarAway);

        for (int i = 0; i < 500 && harness.Pipeline.FailedSaves == 0; i++)
        {
            harness.Pipeline.Update();
            Thread.Sleep(1);
        }

        Assert.True(harness.Pipeline.FailedSaves > 0, "the save should have faulted");

        // The store really has nothing. Not asserted on the in-memory index: ApplySave takes the
        // claim back and SubmitSaves re-makes it later in the *same* Update, so the index spends
        // less than a frame in the state this would be checking. What the index claims is safe
        // anyway — see SubmitSaves, where the serial lane's ordering is the reason.
        Assert.Empty(store.LoadChunkIndex());

        // The disk comes back. Nothing re-dirties the chunk — it is not even loaded any more — so
        // the only way this can land is if the failed batch was kept.
        store.Recover();

        for (int i = 0; i < 500 && harness.Pipeline.ChunksSaved == 0; i++)
        {
            harness.Pipeline.Update();
            Thread.Sleep(1);
        }

        Assert.Equal(1, harness.Pipeline.ChunksSaved);
        Assert.Single(store.LoadChunkIndex());
    }

    // -----------------------------------------------------------------------------------------
    // Stores that misbehave on purpose.
    // -----------------------------------------------------------------------------------------

    /// <summary>Holds column reads open, so a restore can be made to land last.</summary>
    private sealed class SlowColumnStore : InMemoryWorldStoreBase
    {
        private readonly ManualResetEventSlim _gate = new(initialState: true);

        public void HoldColumns() => _gate.Reset();

        public void ReleaseColumns() => _gate.Set();

        public override byte[]? ReadColumn(int chunkX, int chunkZ)
        {
            _gate.Wait(TimeSpan.FromSeconds(20));
            return base.ReadColumn(chunkX, chunkZ);
        }
    }

    private sealed class ForgetfulWorldStore : InMemoryWorldStoreBase
    {
        private bool _forgotten;

        public void LoseEverything() => _forgotten = true;

        public override byte[]? ReadChunk(ChunkPos position) =>
            _forgotten ? null : base.ReadChunk(position);
    }

    private sealed class ThrowingWorldStore : InMemoryWorldStoreBase
    {
        private volatile bool _broken = true;

        public void Recover() => _broken = false;

        public override void Write(
            IReadOnlyList<(ChunkPos Position, byte[] Blob)> chunks,
            IReadOnlyList<(int X, int Z, byte[] Blob)> columns)
        {
            if (_broken) throw new IOException("the disk is on fire");
            base.Write(chunks, columns);
        }
    }

    /// <summary>Notices two callers inside the store at once, and can park a reader there.</summary>
    /// <remarks>
    /// <b>The overlap is recorded rather than inferred from a crash</b>, because the in-memory
    /// store's dictionaries only sometimes notice being used by two threads — and "sometimes"
    /// is what a race looks like right up until it is a corrupt world file.
    /// </remarks>
    private sealed class ConcurrencyWatchingStore : InMemoryWorldStoreBase
    {
        private readonly ManualResetEventSlim _reads = new(initialState: true);
        private int _inside;

        /// <summary>Whether two callers were ever inside this store at the same moment.</summary>
        public volatile bool SawOverlap;

        /// <summary>True while a reader is parked inside <see cref="ReadChunk"/>.</summary>
        public volatile bool IsReading;

        public void HoldReads() => _reads.Reset();

        public void ReleaseReads() => _reads.Set();

        public override byte[]? ReadChunk(ChunkPos position) =>
            Guarded(() =>
            {
                IsReading = true;
                try
                {
                    _reads.Wait(TimeSpan.FromSeconds(10));
                    return base.ReadChunk(position);
                }
                finally
                {
                    IsReading = false;
                }
            });

        public override byte[]? ReadColumn(int chunkX, int chunkZ) =>
            Guarded(() => base.ReadColumn(chunkX, chunkZ));

        public override void Write(
            IReadOnlyList<(ChunkPos Position, byte[] Blob)> chunks,
            IReadOnlyList<(int X, int Z, byte[] Blob)> columns) =>
            Guarded<object?>(() =>
            {
                base.Write(chunks, columns);
                return null;
            });

        public override void Flush() =>
            Guarded<object?>(() =>
            {
                base.Flush();
                return null;
            });

        private T Guarded<T>(Func<T> body)
        {
            if (Interlocked.Increment(ref _inside) > 1)
            {
                SawOverlap = true;
            }

            try
            {
                return body();
            }
            finally
            {
                Interlocked.Decrement(ref _inside);
            }
        }
    }

    private sealed class BlockingWorldStore : InMemoryWorldStoreBase
    {
        private readonly ManualResetEventSlim _gate = new(initialState: true);

        /// <summary>True while a writer is parked inside <see cref="Write"/>.</summary>
        /// <remarks>
        /// Observable rather than inferred from a sleep, so the test waits for the state it needs
        /// instead of hoping a duration was long enough — which is the difference between a test
        /// and a source of intermittent failures.
        /// </remarks>
        public volatile bool IsWriting;

        public void Block() => _gate.Reset();

        public void Release() => _gate.Set();

        public override void Write(
            IReadOnlyList<(ChunkPos Position, byte[] Blob)> chunks,
            IReadOnlyList<(int X, int Z, byte[] Blob)> columns)
        {
            IsWriting = true;
            try
            {
                _gate.Wait(TimeSpan.FromSeconds(10));
                base.Write(chunks, columns);
            }
            finally
            {
                IsWriting = false;
            }
        }
    }

    /// <summary>
    /// A subclassable in-memory store, so a misbehaving one overrides a method rather than
    /// reimplementing storage.
    /// </summary>
    /// <remarks>
    /// Deliberately delegating to the shipped <see cref="InMemoryWorldStore"/> rather than being a
    /// second implementation. A hand-rolled fake here would be a third thing to keep in step with
    /// the contract suite, and the whole reason that suite exists is that fakes drift.
    /// </remarks>
    private abstract class InMemoryWorldStoreBase : IWorldStore
    {
        protected readonly InMemoryWorldStore Inner = new();

        public virtual byte[]? ReadWorldHeader() => Inner.ReadWorldHeader();

        public virtual void WriteWorldHeader(byte[] blob) => Inner.WriteWorldHeader(blob);

        public virtual IReadOnlyCollection<ChunkPos> LoadChunkIndex() => Inner.LoadChunkIndex();

        public virtual IReadOnlyCollection<(int X, int Z)> LoadColumnIndex() => Inner.LoadColumnIndex();

        public virtual byte[]? ReadChunk(ChunkPos position) => Inner.ReadChunk(position);

        public virtual byte[]? ReadColumn(int chunkX, int chunkZ) => Inner.ReadColumn(chunkX, chunkZ);

        public virtual void Write(
            IReadOnlyList<(ChunkPos Position, byte[] Blob)> chunks,
            IReadOnlyList<(int X, int Z, byte[] Blob)> columns) => Inner.Write(chunks, columns);

        public virtual void Flush() => Inner.Flush();

        public void Dispose() => Inner.Dispose();
    }
}
