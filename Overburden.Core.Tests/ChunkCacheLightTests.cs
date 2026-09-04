using Overburden.Core.Content;
using Overburden.Core.Lighting;
using Overburden.Core.Meshing;
using Overburden.Core.Persistence;
using Overburden.Core.Streaming;
using Overburden.Core.Voxels;
using Overburden.Core.Worldgen;

namespace Overburden.Core.Tests;

/// <summary>
/// The retained-chunk cache, slice 2: a chunk keeps its derived light through the cache, so a
/// turnaround costs no derive either. docs/CHUNK-CACHE-PLAN.md, §5 and test 7.
/// </summary>
/// <remarks>
/// <para>
/// <b>The derived data is a function of three things — the chunk's voxels, its six face
/// neighbours' voxels and its sky column — and the chunk's own voxels cannot change while it is
/// out.</b> So the whole of slice 2 is two invalidations: an edit at a cached chunk's face drops
/// its grid, and a column that moved under it — by an edit, by a restore, or by being replaced —
/// is caught at revival through <see cref="SkyColumn.Revision"/>. The propagated values are never
/// kept; a revival reseeds them.
/// </para>
/// <para>
/// <b>M5b's fixpoint check is not the oracle here, on its own.</b> It takes each cell's source and
/// conductance as given and asks whether the values agree with them, and stale derived data
/// satisfies it perfectly. So every test here also asks the sharper question — does each tracked
/// chunk's derived data equal a fresh derivation from the voxels and the column as they are
/// <em>now</em> — and that is the check a kept grid can fail.
/// </para>
/// </remarks>
public class ChunkCacheLightTests
{
    private const int Air = MaterialIds.AirNumericId;
    private const int Stone = 3;
    private const int Lamp = PipelineHarness.Lamp;
    private const long GenerousCap = 64L * 1024 * 1024;

    /// <summary>Far enough that nothing of the first box is still loaded.</summary>
    private static readonly ChunkPos FarAway = new(200, 0, 200);

    // -----------------------------------------------------------------------------------------
    // The oracle.
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// Every tracked chunk's derived light equals a fresh derivation from the voxels and the
    /// column as they are now.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The general build path only, so this shares nothing with the uniform closed form the
    /// pipeline takes where it can; the two are pinned equal elsewhere.
    /// </para>
    /// <para>
    /// <b>One deliberate blind spot, and it is the existing gain-only rule rather than the
    /// cache</b>: a face toward a chunk that is not loaded was derived against whatever was there
    /// at the time — real rock if that neighbour was loaded then, air if it was not — and a fresh
    /// derivation sees air. That bit cannot matter, because a chunk with no grid stops
    /// propagation at the face whatever it says, so it is masked. Every other bit — the interior
    /// faces, the faces toward loaded neighbours, the sky flag and the emission — must match.
    /// </para>
    /// </remarks>
    internal static void AssertDerivedFresh(PipelineHarness harness)
    {
        ChunkStreamer streamer = harness.Pipeline.Streamer;
        MaterialTable materials = PipelineHarness.Materials();
        var padded = new PaddedChunk();
        var neighbourhood = new VoxelChunk?[PaddedChunk.NeighbourhoodSize];
        var fresh = new ChunkLight();
        int checkedChunks = 0;

        foreach (StreamedChunk chunk in streamer.Chunks)
        {
            if (chunk.Light is not { HasDerived: true } light || chunk.Sky is null) continue;

            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                neighbourhood[PaddedChunk.NeighbourIndex(dx, dy, dz)] =
                    streamer.Get(chunk.Position.Offset(dx, dy, dz))?.Voxels;
            }

            padded.CopyFrom(neighbourhood, Air);
            fresh.Reset();
            fresh.BuildDerived(padded, materials, chunk.Position, chunk.Sky);

            for (int index = 0; index < LightCellPos.Volume; index++)
            {
                var (lx, ly, lz) = LightCellPos.FromIndex(index);
                var cell = LightCellPos.FromChunkLocal(chunk.Position, lx, ly, lz);

                for (int face = 0; face < VoxelFace.Count; face++)
                {
                    LightCellPos across = LightTestWorld.Neighbour(cell, face);
                    if (across.Chunk != chunk.Position && streamer.Get(across.Chunk)?.Voxels is null)
                    {
                        continue;
                    }

                    Assert.True(
                        light.Conducts(index, face) == fresh.Conducts(index, face),
                        $"{cell} face {face}: the derived conductance is stale");
                }

                foreach (LightChannel channel in new[] { LightChannel.Sky, LightChannel.Block })
                {
                    Assert.True(
                        light.Source(index, channel) == fresh.Source(index, channel),
                        $"{cell} {channel}: the derived source is stale — holds "
                        + $"{light.Source(index, channel)}, a fresh derivation says "
                        + $"{fresh.Source(index, channel)}");
                }
            }

            checkedChunks++;
        }

        Assert.True(checkedChunks > 0, "no tracked chunk had derived light, so nothing was checked");
    }

    private static void PumpUntil(PipelineHarness harness, Func<bool> condition, int timeoutMillis = 20_000)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            harness.Pipeline.Update();
            Assert.True(clock.ElapsedMilliseconds < timeoutMillis, "the pipeline never reached the state the test needs");
            Thread.Sleep(1);
        }
    }

    // -----------------------------------------------------------------------------------------
    // The headline: a turnaround costs no derive.
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void ATurnaroundBringsTheGridBackAndCostsNoDerive()
    {
        // The slice's number: derives across a turnaround, zero for the chunks that were cached.
        // The grid that comes back is the object that left, holding its derived data and none of
        // its values; after the reseed its values are what they were, the fixpoint and the fresh-
        // derivation oracle both hold, and the mesh comes exactly once with no relight — or the
        // chunk drew from zeros and was repaired after, which is the dark flash slice 1's battery
        // found for a chunk revived at Lit.
        using var harness = new PipelineHarness(chunkCacheBytes: GenerousCap);
        ChunkStreamer streamer = harness.Pipeline.Streamer;
        ViewerId viewer = streamer.AddViewer(ChunkPos.Zero, 1, 1);
        harness.Settle();

        var probe = ChunkPos.Zero;
        ChunkLight before = streamer.Get(probe)!.Light!;
        int[] valuesBefore = LightPipelineTests.CellValues(before);
        int box = streamer.TrackedCount;
        int deliveredBefore = harness.Deliveries[probe];
        int relightsBefore = harness.Pipeline.RelightRemeshes;

        streamer.MoveViewer(viewer, FarAway);
        harness.Settle();

        StreamedChunk waiting = streamer.CachedChunks.Single(chunk => chunk.Position == probe);
        Assert.Same(before, waiting.Light);
        Assert.True(before.HasDerived, "the derived data is what the cache keeps");
        Assert.All(LightPipelineTests.CellValues(before), value => Assert.Equal(0, value));
        Assert.Equal(GenerationStage.Features, waiting.Stage);
        Assert.Equal(box, streamer.CachedChunksWithLight);

        int derives = harness.Pipeline.LightBuilds;
        streamer.MoveViewer(viewer, ChunkPos.Zero);
        harness.Settle();

        StreamedChunk revived = streamer.Get(probe)!;
        Assert.Same(before, revived.Light);
        Assert.Equal(derives, harness.Pipeline.LightBuilds);
        Assert.Equal(box, streamer.CacheHitsWithLight);
        Assert.Equal(box, harness.Pipeline.LightRevivals);
        Assert.Equal(0, streamer.CachedLightDroppedByEdit + streamer.CachedLightDroppedByColumn);
        Assert.Equal(GenerationStage.Lit, revived.Stage);
        Assert.Equal(valuesBefore, LightPipelineTests.CellValues(revived.Light!));
        Assert.Contains(probe, harness.MeshedPositions);
        Assert.Equal(deliveredBefore + 1, harness.Deliveries[probe]);
        Assert.Equal(relightsBefore, harness.Pipeline.RelightRemeshes);
        LightPipelineTests.AssertFixpoint(harness);
        AssertDerivedFresh(harness);
    }

    // -----------------------------------------------------------------------------------------
    // The face push.
    // -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnEditAgainstACachedChunksFaceDropsItsGridAndAnInteriorEditDoesNot(bool atTheFace)
    {
        // r=1 at margin 0 is a 3³ box, and a two-chunk step along X leaves x = −1 and x = 0 in the
        // cache with x = 1 still loaded — so the face between (0,0,0) and (1,0,0) has a cached
        // chunk on one side and a loaded one on the other. An edit in (1,0,0) at local x = 0 is
        // against that face; one at local x = 16 is fifteen voxels from it and reaches nothing.
        using var harness = new PipelineHarness(loadMargin: 0, chunkCacheBytes: GenerousCap);
        ChunkStreamer streamer = harness.Pipeline.Streamer;
        ViewerId viewer = streamer.AddViewer(ChunkPos.Zero, 1, 1);
        harness.Settle();

        var cached = ChunkPos.Zero;
        var loaded = new ChunkPos(1, 0, 0);
        ChunkLight grid = streamer.Get(cached)!.Light!;

        streamer.MoveViewer(viewer, new ChunkPos(2, 0, 0));
        harness.Settle();
        Assert.True(streamer.IsCached(cached));
        Assert.True(streamer.IsTracked(loaded));
        Assert.Equal(18, streamer.CachedChunksWithLight);

        // Whatever is there becomes the other thing, so the write is never a no-op.
        var voxel = new VoxelPos(32 + (atTheFace ? 0 : 16), 16, 16);
        Assert.Equal(loaded, voxel.Chunk);
        Assert.True(harness.Pipeline.TryGetMaterial(voxel, out int was));
        Assert.True(harness.Pipeline.SetVoxel(voxel, was == Air ? Stone : Air));
        harness.Settle();

        StreamedChunk waiting = streamer.CachedChunks.Single(chunk => chunk.Position == cached);
        if (atTheFace)
        {
            Assert.Null(waiting.Light);
            Assert.Equal(0u, waiting.LightNeighbours);
            Assert.Equal(1, streamer.CachedLightDroppedByEdit);
            Assert.Equal(17, streamer.CachedChunksWithLight);
        }
        else
        {
            Assert.Same(grid, waiting.Light);
            Assert.Equal(0, streamer.CachedLightDroppedByEdit);
            Assert.Equal(18, streamer.CachedChunksWithLight);
        }

        // The charge for a dropped grid goes back, or the cap admits less than it says.
        ChunkCacheTests.AssertCacheConsistent(streamer);

        streamer.MoveViewer(viewer, ChunkPos.Zero);
        harness.Settle();

        StreamedChunk revived = streamer.Get(cached)!;
        if (atTheFace)
        {
            Assert.NotSame(grid, revived.Light);
            Assert.Equal(17, streamer.CacheHitsWithLight);
        }
        else
        {
            Assert.Same(grid, revived.Light);
            Assert.Equal(18, streamer.CacheHitsWithLight);
        }

        Assert.True(revived.Light!.HasDerived);
        LightPipelineTests.AssertFixpoint(harness);
        AssertDerivedFresh(harness);
    }

    // -----------------------------------------------------------------------------------------
    // The column, three ways: an edit under the same column, a restore, and a replacement.
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void AHeightRaisedAboveACachedChunkDropsItsGridAtRevival()
    {
        // Same column object, moved by an edit. Chunk y = 1 is open sky above the harness's
        // surface (sea level 0, one octave), so its cells are sky sources. While it is cached, a
        // stone voxel placed in the loaded chunk above it raises the column's open height past
        // every cell in that footprint — a change to the cached chunk's derived data that no
        // edit in the cached chunk itself could make, and the sharp direction: digging down from
        // above stops at the unloaded chunk and changes nothing below it. The fresh-derivation
        // oracle is what says the re-derive was necessary rather than merely conservative.
        using var harness = new PipelineHarness(loadMargin: 0, chunkCacheBytes: GenerousCap);
        ChunkStreamer streamer = harness.Pipeline.Streamer;
        ViewerId viewer = streamer.AddViewer(new ChunkPos(0, 1, 0), 1, 1);
        harness.Settle();

        var cached = new ChunkPos(0, 1, 0);
        var above = new ChunkPos(0, 2, 0);
        var cell = LightCellPos.FromVoxel(new VoxelPos(16, 48, 16));
        Assert.Equal(cached, cell.Chunk);

        ChunkLight grid = streamer.Get(cached)!.Light!;
        Assert.Equal(
            ChunkLight.MaxLevel,
            grid.Source(cell.LocalIndex, LightChannel.Sky));

        streamer.MoveViewer(viewer, new ChunkPos(0, 3, 0));
        harness.Settle();
        Assert.True(streamer.IsCached(cached));
        Assert.True(streamer.IsTracked(above));
        Assert.Same(streamer.Get(above)!.Sky, streamer.ColumnAt(0, 0));

        var pillar = new VoxelPos(16, 72, 16);
        Assert.Equal(above, pillar.Chunk);
        int heightMoves = harness.Pipeline.SkyHeightChanges;
        Assert.True(harness.Pipeline.SetVoxel(pillar, Stone));
        harness.Settle();
        Assert.Equal(heightMoves + 1, harness.Pipeline.SkyHeightChanges);

        // Nothing reaches a cached chunk at the change; the check is at revival.
        Assert.Same(grid, streamer.CachedChunks.Single(chunk => chunk.Position == cached).Light);

        streamer.MoveViewer(viewer, new ChunkPos(0, 1, 0));
        harness.Settle();

        // Both cached chunks of column (0, 0) went; the other sixteen came back with their grids.
        StreamedChunk revived = streamer.Get(cached)!;
        Assert.Equal(2, streamer.CachedLightDroppedByColumn);
        Assert.Equal(16, streamer.CacheHitsWithLight);
        Assert.NotSame(grid, revived.Light);
        Assert.Equal(0, revived.Light!.Source(cell.LocalIndex, LightChannel.Sky));
        LightPipelineTests.AssertFixpoint(harness);
        AssertDerivedFresh(harness);
    }

    [Fact]
    public void ARestoreLandingInACachedColumnDropsTheGridsCachedUnderIt()
    {
        // Same column object, moved by a restore. Session one places a stone voxel in the air two
        // chunks above the surface and saves the column it raised. Session two views the all-air
        // chunk between the surface and that voxel with the column's read parked inside the
        // store; the chunk is generated rather than loaded, so it does not wait behind the parked
        // read, and it derives against the analytic seed — every cell open sky. It leaves, cached
        // with that grid; the restore lands in the cached column; and its revival has to notice,
        // or the chunk comes back saying the sky is open under a roof.
        var store = new PersistenceTests.SlowColumnStore();
        var between = new ChunkPos(0, 1, 0);
        var pillar = new VoxelPos(16, 72, 16);
        var cell = LightCellPos.FromVoxel(new VoxelPos(16, 48, 16));
        Assert.Equal(between, cell.Chunk);

        using (var first = new PipelineHarness(loadMargin: 0, store: store))
        {
            first.Pipeline.Streamer.AddViewer(pillar.Chunk, 1, 1);
            first.Settle();
            Assert.True(first.Pipeline.SetVoxel(pillar, Stone));
            first.Settle();
            Assert.Equal(73, first.Pipeline.Streamer.Get(pillar.Chunk)!.Sky!.OpenAboveY(16, 16));
        }

        Assert.Contains((0, 0), store.LoadColumnIndex());
        Assert.DoesNotContain(between, store.LoadChunkIndex());

        using var harness = new PipelineHarness(loadMargin: 0, store: store, chunkCacheBytes: GenerousCap);
        ChunkStreamer streamer = harness.Pipeline.Streamer;

        store.HoldColumns();
        ViewerId viewer = streamer.AddViewer(between, 0, 0);
        PumpUntil(harness, () => streamer.Get(between)?.Light is { HasDerived: true });

        StreamedChunk chunk = streamer.Get(between)!;
        ChunkLight grid = chunk.Light!;
        Assert.Equal(
            ChunkLight.MaxLevel,
            grid.Source(cell.LocalIndex, LightChannel.Sky));

        streamer.RemoveViewer(viewer);
        Assert.True(streamer.IsCached(between));
        Assert.Same(grid, chunk.Light);
        SkyColumn? column = streamer.CachedColumnAt(0, 0);
        Assert.NotNull(column);

        store.ReleaseColumns();
        harness.Settle();
        Assert.True(harness.Pipeline.ColumnsRestored > 0);
        Assert.Equal(73, column.OpenAboveY(16, 16));

        // The restore reached the column and not the chunk, which still holds the seed's grid.
        Assert.Same(grid, chunk.Light);

        streamer.AddViewer(between, 0, 0);
        harness.Settle();

        StreamedChunk revived = streamer.Get(between)!;
        Assert.Same(chunk, revived);
        Assert.Same(column, revived.Sky);
        Assert.Equal(1, streamer.CachedLightDroppedByColumn);
        Assert.Equal(0, streamer.CacheHitsWithLight);
        Assert.NotSame(grid, revived.Light);
        Assert.Equal(0, revived.Light!.Source(cell.LocalIndex, LightChannel.Sky));
        AssertDerivedFresh(harness);
    }

    [Fact]
    public void AColumnReplacedWhileTheChunkWasOutDropsItsGridAtRevival()
    {
        // A different column object. Without a store a dug column is dirty forever, so it is
        // dropped rather than cached when its last chunk leaves, and comes back as a new object
        // seeded from the unedited surface. The chunks of that column were derived against the
        // shaft; the revision they recorded belongs to an object that no longer exists, so they
        // derive again — against the seed, which is the honest no-store answer: the voxels kept
        // the shaft and the column lost it, and the derived data has to say what the column says.
        // The plan's push down the column could not have caught this; a push at the change has
        // no object to name the chunks against.
        using var harness = new PipelineHarness(loadMargin: 0, chunkCacheBytes: GenerousCap);
        ChunkStreamer streamer = harness.Pipeline.Streamer;
        ViewerId viewer = streamer.AddViewer(ChunkPos.Zero, 1, 1);
        harness.Settle();

        ChunkCacheTests.DigAShaft(harness, ChunkPos.Zero);
        SkyColumn dug = streamer.Get(ChunkPos.Zero)!.Sky!;
        Assert.True(dug.DirtySinceSave);

        streamer.MoveViewer(viewer, FarAway);
        harness.Settle();
        Assert.Null(streamer.CachedColumnAt(0, 0));
        Assert.Equal(27, streamer.CachedChunksWithLight);

        streamer.MoveViewer(viewer, ChunkPos.Zero);
        harness.Settle();

        Assert.NotSame(dug, streamer.Get(ChunkPos.Zero)!.Sky);
        Assert.Equal(3, streamer.CachedLightDroppedByColumn);
        Assert.Equal(24, streamer.CacheHitsWithLight);
        LightPipelineTests.AssertFixpoint(harness);
        AssertDerivedFresh(harness);
    }

    // -----------------------------------------------------------------------------------------
    // The values are never kept.
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void ALampRemovedBesideAChunkWhileItWasOutLeavesNoStaleLightWhenItComesBack()
    {
        // The case that separates keeping the values from not: a lamp in the loaded chunk next
        // door lit the cached chunk's cells across the face, then went out while the chunk was
        // out. The un-light walk stopped at the face — there was no grid to walk into — so a kept
        // value would be light with no source anywhere, and nothing would ever darken it. The lamp
        // sits one voxel in from the face, so its removal is an interior edit and the cached grid
        // is kept: this is the kept grid with reseeded values, not a re-derive.
        using var harness = new PipelineHarness(loadMargin: 0, chunkCacheBytes: GenerousCap);
        ChunkStreamer streamer = harness.Pipeline.Streamer;
        ViewerId viewer = streamer.AddViewer(ChunkPos.Zero, 1, 1);
        harness.Settle();

        var lamp = new VoxelPos(33, 24, 16);
        var across = LightCellPos.FromVoxel(new VoxelPos(31, 24, 16));
        Assert.Equal(new ChunkPos(1, 0, 0), lamp.Chunk);
        Assert.Equal(ChunkPos.Zero, across.Chunk);
        Assert.True(
            harness.Pipeline.TryGetMaterial(lamp, out int was) && was == Air,
            "fixture: the lamp needs air around it to light anything");

        Assert.True(harness.Pipeline.SetVoxel(lamp, Lamp));
        harness.Settle();

        ChunkLight grid = streamer.Get(ChunkPos.Zero)!.Light!;
        Assert.True(
            grid.Get(across.LocalIndex, LightChannel.Block) > 0,
            "fixture: the lamp's light never crossed the face");

        streamer.MoveViewer(viewer, new ChunkPos(2, 0, 0));
        harness.Settle();
        Assert.True(streamer.IsCached(ChunkPos.Zero));
        Assert.True(streamer.IsTracked(lamp.Chunk));
        Assert.Equal(0, grid.Get(across.LocalIndex, LightChannel.Block));

        Assert.True(harness.Pipeline.SetVoxel(lamp, Air));
        harness.Settle();
        Assert.Equal(0, streamer.CachedLightDroppedByEdit);

        streamer.MoveViewer(viewer, ChunkPos.Zero);
        harness.Settle();

        StreamedChunk revived = streamer.Get(ChunkPos.Zero)!;
        Assert.Same(grid, revived.Light);
        Assert.Equal(0, grid.Get(across.LocalIndex, LightChannel.Block));
        Assert.Equal(GenerationStage.Lit, revived.Stage);
        LightPipelineTests.AssertFixpoint(harness);
        AssertDerivedFresh(harness);
    }

    // -----------------------------------------------------------------------------------------
    // Test 7: incremental equals from scratch with the streamer cycling chunks.
    // -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0L)]
    [InlineData(GenerousCap)]
    public void IncrementalLightEqualsAFreshDerivationWithChunksCyclingThroughTheCache(long cap)
    {
        // M5b's fuzz — edits at chunk faces, sky heights broken through and raised, lamps placed
        // and removed — with the streamer walking its box round a small region, so that chunks
        // leave and come back constantly: through the cache when it is on, through the store and
        // the seed when it is not. After every settle every tracked grid equals a fresh
        // derivation and the whole tracked set is at the fixpoint. The cap-0 arm is the control:
        // the same walk with nothing cached says the oracle is about the cache, not the walk.
        //
        // The first thing this walk found was in both arms: the whole tracked set was not a
        // fixpoint and never had been. A chunk that unloaded took its grid, and the cells next
        // door it was lighting kept what they held — harmless at margin 2 while it stayed away,
        // and wrong once it came back with different sources, because the kept values then had
        // no source anywhere and the returning grid imported them. LightPropagator.RetractFrom is
        // the repair, and this walk is what says the tracked set is a fixpoint again.
        var random = new Random(0x5EED2);
        using var harness = new PipelineHarness(store: new InMemoryWorldStore(), chunkCacheBytes: cap);
        ChunkStreamer streamer = harness.Pipeline.Streamer;
        var position = ChunkPos.Zero;
        ViewerId viewer = streamer.AddViewer(position, 1, 1);
        harness.Settle();

        var lamps = new List<VoxelPos>();
        int faceEdits = 0;
        int heightMoves = 0;
        int lampsPlaced = 0;
        int lampsRemoved = 0;

        for (int step = 0; step < 18; step++)
        {
            for (int edit = random.Next(2, 5); edit > 0; edit--)
            {
                switch (random.Next(5))
                {
                    case 0:
                    case 1:
                        if (EditAgainstAnUnloadedFace(harness, random)) faceEdits++;
                        break;
                    case 2:
                        if (MoveASkyHeight(harness, random)) heightMoves++;
                        break;
                    case 3:
                        if (PlaceALamp(harness, random, lamps)) lampsPlaced++;
                        break;
                    default:
                        if (RemoveALamp(harness, random, lamps)) lampsRemoved++;
                        break;
                }
            }

            harness.Settle();

            // A one-chunk step most of the time, and every sixth step a jump the width of the
            // box, so whole shells leave and come back rather than only the trailing face.
            position = step % 6 == 5
                ? new ChunkPos(position.X < 0 ? 4 : -4, position.Y, position.Z)
                : new ChunkPos(
                    Math.Clamp(position.X + random.Next(-1, 2), -2, 2),
                    Math.Clamp(position.Y + random.Next(-1, 2), -1, 1),
                    Math.Clamp(position.Z + random.Next(-1, 2), -2, 2));
            streamer.MoveViewer(viewer, position);
            harness.Settle();

            AssertDerivedFresh(harness);
            LightPipelineTests.AssertFixpoint(harness);
            ChunkCacheTests.AssertCacheConsistent(streamer);
        }

        Assert.Equal(0, harness.Pipeline.FaultedJobs);

        // Denominators, because a pass over a walk that never reached the interesting paths looks
        // exactly like a pass over one that did.
        Assert.True(faceEdits > 3, $"only {faceEdits} edit(s) landed against an unloaded face");
        Assert.True(heightMoves > 3, $"only {heightMoves} edit(s) moved a sky height");
        Assert.True(lampsPlaced > 3 && lampsRemoved > 2, $"{lampsPlaced} lamp(s) placed, {lampsRemoved} removed");

        Assert.True(harness.Pipeline.LightRetractions > 0, "no grid ever left the field");

        if (cap > 0)
        {
            Assert.True(streamer.CacheHitsWithLight > 0, "no chunk ever came back with its grid");
            Assert.True(harness.Pipeline.LightRevivals > 0, "no revived grid was ever reseeded");
            Assert.True(streamer.CachedLightDroppedByEdit > 0, "no face edit ever reached a cached grid");
            Assert.True(streamer.CachedLightDroppedByColumn > 0, "no moved column ever caught a cached grid");
        }
        else
        {
            Assert.Equal(0, streamer.CacheHits);
        }
    }

    /// <summary>
    /// Flips a voxel in the layer of a loaded chunk against a face whose neighbour is not loaded,
    /// which is where a cached chunk can be.
    /// </summary>
    private static bool EditAgainstAnUnloadedFace(PipelineHarness harness, Random random)
    {
        ChunkStreamer streamer = harness.Pipeline.Streamer;
        StreamedChunk[] loaded = streamer.Chunks.Where(chunk => chunk.HasVoxels).ToArray();
        if (loaded.Length == 0) return false;

        for (int attempt = 0; attempt < 32; attempt++)
        {
            StreamedChunk chunk = loaded[random.Next(loaded.Length)];
            int face = random.Next(VoxelFace.Count);
            int[] offset = VoxelFace.Offset[face];
            if (streamer.IsTracked(chunk.Position.Offset(offset[0], offset[1], offset[2]))) continue;

            int lx = random.Next(VoxelConstants.ChunkSize);
            int ly = random.Next(VoxelConstants.ChunkSize);
            int lz = random.Next(VoxelConstants.ChunkSize);
            if (offset[0] != 0) lx = offset[0] > 0 ? VoxelConstants.ChunkMask : 0;
            if (offset[1] != 0) ly = offset[1] > 0 ? VoxelConstants.ChunkMask : 0;
            if (offset[2] != 0) lz = offset[2] > 0 ? VoxelConstants.ChunkMask : 0;

            int was = chunk.Voxels!.Get(lx, ly, lz);
            return harness.Pipeline.SetVoxel(
                VoxelPos.FromChunkLocal(chunk.Position, lx, ly, lz), was == Air ? Stone : Air);
        }

        return false;
    }

    /// <summary>Takes a column's topmost opaque voxel, or puts one in the air above it.</summary>
    private static bool MoveASkyHeight(PipelineHarness harness, Random random)
    {
        ChunkStreamer streamer = harness.Pipeline.Streamer;
        StreamedChunk[] loaded = streamer.Chunks.Where(chunk => chunk.HasVoxels && chunk.Sky is not null).ToArray();
        if (loaded.Length == 0) return false;

        int before = harness.Pipeline.SkyHeightChanges;
        for (int attempt = 0; attempt < 32 && harness.Pipeline.SkyHeightChanges == before; attempt++)
        {
            StreamedChunk chunk = loaded[random.Next(loaded.Length)];
            int lx = random.Next(VoxelConstants.ChunkSize);
            int lz = random.Next(VoxelConstants.ChunkSize);
            int open = chunk.Sky!.OpenAboveY(lx, lz);
            int worldX = (chunk.Position.X << VoxelConstants.ChunkSizeLog2) + lx;
            int worldZ = (chunk.Position.Z << VoxelConstants.ChunkSizeLog2) + lz;

            bool down = random.Next(2) == 0;
            var voxel = new VoxelPos(worldX, down ? open - 1 : open + random.Next(1, 12), worldZ);
            harness.Pipeline.SetVoxel(voxel, down ? Air : Stone);
        }

        return harness.Pipeline.SkyHeightChanges > before;
    }

    private static bool PlaceALamp(PipelineHarness harness, Random random, List<VoxelPos> lamps)
    {
        ChunkStreamer streamer = harness.Pipeline.Streamer;
        StreamedChunk[] loaded = streamer.Chunks.Where(chunk => chunk.HasVoxels).ToArray();
        if (loaded.Length == 0) return false;

        for (int attempt = 0; attempt < 32; attempt++)
        {
            StreamedChunk chunk = loaded[random.Next(loaded.Length)];
            int lx = random.Next(VoxelConstants.ChunkSize);
            int ly = random.Next(VoxelConstants.ChunkSize);
            int lz = random.Next(VoxelConstants.ChunkSize);
            if (chunk.Voxels!.Get(lx, ly, lz) != Air) continue;

            VoxelPos voxel = VoxelPos.FromChunkLocal(chunk.Position, lx, ly, lz);
            if (!harness.Pipeline.SetVoxel(voxel, Lamp)) continue;

            lamps.Add(voxel);
            return true;
        }

        return false;
    }

    private static bool RemoveALamp(PipelineHarness harness, Random random, List<VoxelPos> lamps)
    {
        for (int attempt = 0; attempt < 8 && lamps.Count > 0; attempt++)
        {
            int index = random.Next(lamps.Count);
            if (!harness.Pipeline.SetVoxel(lamps[index], Air)) continue;

            lamps.RemoveAt(index);
            return true;
        }

        return false;
    }
}
