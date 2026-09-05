using Overburden.Core.Content;
using Overburden.Core.Fluid;
using Overburden.Core.Hydrology;
using Overburden.Core.Lighting;
using Overburden.Core.Persistence;
using Overburden.Core.Streaming;
using Overburden.Core.Voxels;
using Xunit.Abstractions;

namespace Overburden.Core.Tests;

/// <summary>
/// M8b-2c's wiring, in Core: the tracker that owns "a hole is only what was dug", the seed
/// surface read off the streamer's columns, and the pipeline's three arrival paths firing once
/// each.
/// </summary>
/// <remarks>
/// The headline is <c>TheTrackerEqualsFromScratchOverEditedChunksAfterAnySequence</c> — 2b's
/// oracle with decision 1 in it: the index equals the definition over every pair that touches an
/// edited, loaded chunk. The scenarios around it are the two cases the filter would get wrong on
/// its own, and each is the test a battery recipe falls to.
/// </remarks>
public class PortalTrackerTests(ITestOutputHelper output)
{
    /// <summary>A world with a seed surface, natural caves, a loaded set and edits.</summary>
    private sealed class DugWorld(ISurfaceHeights seed) : IFluidTerrain
    {
        public ISurfaceHeights Seed { get; } = seed;

        public HashSet<ChunkPos> Loaded { get; } = [];

        public HashSet<VoxelPos> Dug { get; } = [];

        public HashSet<VoxelPos> Placed { get; } = [];

        /// <summary>Holdable without being an edit — a natural cave.</summary>
        public HashSet<VoxelPos> Natural { get; } = [];

        public bool CanHold(VoxelPos v) =>
            Loaded.Contains(v.Chunk)
            && !Placed.Contains(v)
            && (Dug.Contains(v) || Natural.Contains(v) || v.Y > Seed.SurfaceHeightAt(v.X, v.Z));

        /// <summary>Whether the store would hand this chunk back as edited.</summary>
        public bool HasEdits(ChunkPos chunk) =>
            Dug.Any(v => v.Chunk == chunk) || Placed.Any(v => v.Chunk == chunk);

        public void Dig(PortalTracker tracker, VoxelPos v)
        {
            Placed.Remove(v);
            Dug.Add(v);
            tracker.OnVoxelChanged(v);
        }

        public void Place(PortalTracker tracker, VoxelPos v)
        {
            Dug.Remove(v);
            Placed.Add(v);
            tracker.OnVoxelChanged(v);
        }

        public void Load(PortalTracker tracker, ChunkPos chunk)
        {
            Loaded.Add(chunk);
            tracker.OnVoxelsReady(chunk, HasEdits(chunk));
        }

        public void Unload(PortalTracker tracker, ChunkPos chunk)
        {
            Loaded.Remove(chunk);
            tracker.OnChunkForgotten(chunk);
        }
    }

    private static ISurfaceHeights Flat(int y) => new AnalyticSurface((_, _) => y);

    private static ISurfaceHeights Cliff(int atX, int high, int low) =>
        new AnalyticSurface((x, _) => x < atX ? high : low);

    [Fact]
    public void AnUneditedChunkArrivingAboveADugMouthFindsThePitByRescanningTheDugChunk()
    {
        // Decision 1's sharp case. The mouth is the top row of the lower chunk, dug; the chunk
        // above arrives later and unedited. A filter that scans dug chunks only would never file
        // the pair; the tracker re-scans the dug neighbour on the unedited arrival, and files it.
        var lower = new ChunkPos(0, 1, 0);
        var upper = new ChunkPos(0, 2, 0);
        var mouth = new VoxelPos(5, 63, 5);

        var world = new DugWorld(Flat(63));
        var tracker = new PortalTracker(new PortalIndex(world.Seed, world));
        world.Load(tracker, lower);
        world.Dig(tracker, mouth);
        Assert.Equal(0, tracker.Index.Count);

        long scans = tracker.Scans;
        world.Load(tracker, upper);
        Assert.False(tracker.IsEdited(upper));
        Assert.True(tracker.Scans > scans, "the dug neighbour was not re-scanned");
        Assert.True(tracker.Index.Contains(new Portal(mouth, mouth with { Y = 64 })));

        world.Unload(tracker, upper);
        Assert.Equal(0, tracker.Index.Count);
        world.Load(tracker, upper);
        Assert.Equal(1, tracker.Index.Count);
    }

    [Fact]
    public void ANaturalBankIsNotAPortalUntilItsChunkIsDug()
    {
        // The decision itself: a cave mouth on a cliff under a lake is not a hole until someone
        // digs in that chunk — and the first dig scans the chunk, so it becomes one then, because
        // the invariant is "every pair touching a dug chunk", not "every pair someone dug".
        var world = new DugWorld(Cliff(16, high: 40, low: 30));
        var tracker = new PortalTracker(new PortalIndex(world.Seed, world));
        var cave = new VoxelPos(15, 35, 5);
        world.Natural.Add(cave);

        var chunk = new ChunkPos(0, 1, 0);
        world.Load(tracker, chunk);
        Assert.False(tracker.IsEdited(chunk));
        Assert.Equal(0, tracker.Index.Count);

        // Digging anywhere in the chunk, well away from the cave, makes the chunk dug.
        world.Dig(tracker, new VoxelPos(28, 40, 28));
        Assert.True(tracker.IsEdited(chunk));
        Assert.True(tracker.Index.Contains(new Portal(cave, new VoxelPos(16, 35, 5))));

        // And the chunk stays dug through a second edit without a second scan.
        long scans = tracker.Scans;
        world.Dig(tracker, new VoxelPos(28, 39, 28));
        Assert.Equal(scans, tracker.Scans);
    }

    [Fact]
    public void AChunkArrivingWithEditsIsScannedAndOneWithoutIsNot()
    {
        var world = new DugWorld(Flat(40));
        var tracker = new PortalTracker(new PortalIndex(world.Seed, world));
        var chunk = new ChunkPos(0, 1, 0);

        // A stored, edited chunk: the dig is already in the world when it arrives.
        world.Dug.Add(new VoxelPos(5, 40, 5));
        world.Load(tracker, chunk);
        Assert.True(tracker.IsEdited(chunk));
        Assert.Equal(1, tracker.Index.Count);
        Assert.Equal(1, tracker.Scans);

        // Forget it and bring it back unedited — as if it were a different, pristine chunk.
        world.Unload(tracker, chunk);
        world.Dug.Clear();
        world.Load(tracker, chunk);
        Assert.False(tracker.IsEdited(chunk));
        Assert.Equal(0, tracker.Index.Count);
        Assert.Equal(1, tracker.Scans);
    }

    [Fact]
    public void TheTrackerEqualsFromScratchOverEditedChunksAfterAnySequence()
    {
        // THE ORACLE, with decision 1 in it: after any sequence of arrivals (edited or not),
        // digs, placements and forgets, the index equals the definition over every pair whose
        // both halves hold now and which touches a loaded chunk the tracker counts as dug.
        // Natural caves are scattered in so that "touches a dug chunk" and "was dug" differ.
        var random = new Random(20260905);
        ChunkPos[] chunks =
        [
            new(0, 1, 0), new(1, 1, 0), new(0, 1, 1), new(1, 1, 1), new(0, 2, 0), new(1, 2, 1),
        ];

        int checks = 0;
        int checksWithNaturalPortals = 0;
        int checksWithUneditedLoaded = 0;

        for (int trial = 0; trial < 3; trial++)
        {
            int salt = trial * 7919;
            var seed = new AnalyticSurface((x, z) =>
                56 + (Hash((x >> 2) + salt, z >> 2) % 5) + (x >= 40 ? 0 : 4));

            var world = new DugWorld(seed);
            var tracker = new PortalTracker(new PortalIndex(seed, world));

            // Natural caves: a few holdable voxels just under cliff faces and surfaces.
            for (int i = 0; i < 40; i++)
            {
                int x = random.Next(0, 64);
                int z = random.Next(0, 64);
                int s = seed.SurfaceHeightAt(x, z);
                world.Natural.Add(new VoxelPos(x, s - random.Next(0, 3), z));
            }

            for (int op = 0; op < 160; op++)
            {
                int kind = random.Next(100);
                ChunkPos chunk = chunks[random.Next(chunks.Length)];
                int x = (chunk.X << VoxelConstants.ChunkSizeLog2) + random.Next(VoxelConstants.ChunkSize);
                int z = (chunk.Z << VoxelConstants.ChunkSizeLog2) + random.Next(VoxelConstants.ChunkSize);
                int s = seed.SurfaceHeightAt(x, z);

                if (kind < 40)
                {
                    if (world.Loaded.Contains(chunk))
                    {
                        world.Dig(tracker, new VoxelPos(x, s - random.Next(4), z));
                    }
                }
                else if (kind < 55)
                {
                    if (world.Loaded.Contains(chunk))
                    {
                        world.Place(tracker, new VoxelPos(x, s - 2 + random.Next(5), z));
                    }
                }
                else if (kind < 80)
                {
                    if (!world.Loaded.Contains(chunk))
                    {
                        world.Load(tracker, chunk);
                    }
                }
                else if (world.Loaded.Contains(chunk))
                {
                    world.Unload(tracker, chunk);
                }

                if (op % 5 != 4)
                {
                    continue;
                }

                HashSet<Portal> expected = FromScratch(tracker, world);
                Assert.Equal(expected.Count, tracker.Index.Count);
                foreach (Portal portal in expected)
                {
                    Assert.True(tracker.Index.Contains(portal), $"missing {portal}");
                }

                checks++;
                if (expected.Any(p => world.Natural.Contains(p.Inside))) checksWithNaturalPortals++;
                if (world.Loaded.Any(c => !tracker.IsEdited(c))) checksWithUneditedLoaded++;
            }
        }

        output.WriteLine(
            $"{checks} checks: {checksWithNaturalPortals} with a natural cave filed under a dug chunk, "
            + $"{checksWithUneditedLoaded} with an unedited chunk loaded");
        Assert.True(checksWithNaturalPortals > 0, "no natural cave ever sat in a dug chunk");
        Assert.True(checksWithUneditedLoaded > 0, "every loaded chunk was dug, so the filter did nothing");
    }

    [Fact]
    public void TheSeedSurfaceComesOffTheColumnsWhereTheyAreLoadedAndTheFunctionElsewhere()
    {
        // One definition, two sources: a column's seed surface is the function evaluated at seed
        // time, so the answer is the same either way and only the cost differs.
        var surface = new AnalyticSurface((x, z) => 20 + (((x * 5) + (z * 3)) & 7));
        var streamer = new ChunkStreamer(heights: surface);
        var heights = new StreamerSeedHeights(streamer, surface);

        streamer.AddViewer(ChunkPos.Zero, 0, 0);
        Assert.NotNull(streamer.ColumnAt(0, 0));

        for (int z = 0; z < VoxelConstants.ChunkSize; z += 7)
        {
            for (int x = 0; x < VoxelConstants.ChunkSize; x += 5)
            {
                Assert.Equal(surface.SurfaceHeightAt(x, z), heights.SurfaceHeightAt(x, z));
            }
        }

        Assert.True(heights.ColumnReads > 0);
        Assert.Equal(0, heights.FallbackReads);

        Assert.Equal(surface.SurfaceHeightAt(-3, 100), heights.SurfaceHeightAt(-3, 100));
        Assert.Equal(1, heights.FallbackReads);
    }

    [Fact]
    public void EveryArrivalFiresVoxelsReadyOnceAndAForgetFiresOnce()
    {
        // The three arrival paths through the real pipeline: generated, back from the cache, and
        // loaded from the store. Each fires exactly once per arrival, with whether the chunk came
        // edited, and a forget fires once when the chunk leaves — cached or dropped.
        var ready = new Dictionary<ChunkPos, List<bool>>();
        var forgotten = new Dictionary<ChunkPos, int>();
        var store = new InMemoryWorldStore();
        using var harness = new PipelineHarness(
            store: store,
            chunkCacheBytes: 64L * 1024 * 1024,
            onVoxelsReady: (position, hasEdits) =>
            {
                if (!ready.TryGetValue(position, out List<bool>? list))
                {
                    ready[position] = list = [];
                }

                list.Add(hasEdits);
            },
            onChunkForgotten: position => forgotten[position] = forgotten.GetValueOrDefault(position) + 1);

        ChunkStreamer streamer = harness.Pipeline.Streamer;
        var target = ChunkPos.Zero;
        var farAway = new ChunkPos(60, 0, 60);

        ViewerId viewer = streamer.AddViewer(target, 1, 1);
        harness.Settle();

        int tracked = streamer.TrackedCount;
        Assert.True(tracked > 0);
        Assert.Equal(tracked, ready.Count);
        Assert.All(ready.Values, arrivals => Assert.Equal([false], arrivals));

        // An edit, so that the target is stored and comes back edited by whichever path.
        EditSomethingIn(harness.Pipeline, target);
        harness.Settle();

        streamer.MoveViewer(viewer, farAway);
        harness.Settle();
        Assert.Equal(1, forgotten[target]);
        Assert.True(streamer.IsCached(target));

        // Back from the cache: a second arrival, this time edited, and no generate or load.
        int generated = harness.Pipeline.ChunksGenerated;
        int loaded = harness.Pipeline.ChunksLoaded;
        streamer.MoveViewer(viewer, target);
        harness.Settle();
        Assert.Equal([false, true], ready[target]);
        Assert.Equal(1, forgotten[target]);

        // And every chunk that came back from the cache fired again, exactly once more.
        foreach ((ChunkPos position, List<bool> arrivals) in ready)
        {
            if (streamer.IsTracked(position))
            {
                Assert.True(arrivals.Count is 1 or 2, $"{position} fired {arrivals.Count} times");
            }
        }

        output.WriteLine(
            $"{tracked} chunks generated, target revived: generated {generated} → {harness.Pipeline.ChunksGenerated}, "
            + $"loaded {loaded} → {harness.Pipeline.ChunksLoaded}");
    }

    [Fact]
    public void ALoadedChunkFiresVoxelsReadyOnceAsEdited()
    {
        // The store path, with the cache off so the chunk is dropped and has to be read back.
        var ready = new Dictionary<ChunkPos, List<bool>>();
        var store = new InMemoryWorldStore();
        using var harness = new PipelineHarness(
            store: store,
            chunkCacheBytes: 0,
            onVoxelsReady: (position, hasEdits) =>
            {
                if (!ready.TryGetValue(position, out List<bool>? list))
                {
                    ready[position] = list = [];
                }

                list.Add(hasEdits);
            });

        ChunkStreamer streamer = harness.Pipeline.Streamer;
        var target = ChunkPos.Zero;
        ViewerId viewer = streamer.AddViewer(target, 1, 1);
        harness.Settle();

        EditSomethingIn(harness.Pipeline, target);
        harness.Settle();
        streamer.MoveViewer(viewer, new ChunkPos(60, 0, 60));
        harness.Settle();
        Assert.False(streamer.IsCached(target));
        Assert.Contains(target, store.LoadChunkIndex());

        int loaded = harness.Pipeline.ChunksLoaded;
        streamer.MoveViewer(viewer, target);
        harness.Settle();

        Assert.True(harness.Pipeline.ChunksLoaded > loaded, "the chunk was not loaded from the store");
        Assert.Equal([false, true], ready[target]);
    }

    /// <summary>
    /// Writes a material that is not already there, so the chunk is really edited — a write of
    /// air into air is a no-op the pipeline does not count, and every assertion after it would
    /// be about a pristine chunk.
    /// </summary>
    private static void EditSomethingIn(ChunkPipeline pipeline, ChunkPos chunk)
    {
        const int stone = 3;
        VoxelPos origin = chunk.OriginVoxel;
        for (int i = 0; i < VoxelConstants.ChunkSize; i++)
        {
            var voxel = origin.Offset(i, i, i);
            if (pipeline.TryGetMaterial(voxel, out int material))
            {
                int write = material == stone ? MaterialIds.AirNumericId : stone;
                Assert.True(pipeline.SetVoxel(voxel, write));
                Assert.True(pipeline.Streamer.Get(chunk)!.DirtySinceSave, "the edit did not dirty the chunk");
                return;
            }
        }

        Assert.Fail("no loaded voxel to edit in the chunk");
    }

    private static HashSet<Portal> FromScratch(PortalTracker tracker, DugWorld world)
    {
        var expected = new HashSet<Portal>();
        var found = new List<Portal>();

        foreach (ChunkPos chunk in world.Loaded)
        {
            if (!tracker.IsEdited(chunk))
            {
                continue;
            }

            VoxelPos origin = chunk.OriginVoxel;
            for (int y = -1; y <= VoxelConstants.ChunkSize; y++)
            {
                for (int z = -1; z <= VoxelConstants.ChunkSize; z++)
                {
                    for (int x = -1; x <= VoxelConstants.ChunkSize; x++)
                    {
                        found.Clear();
                        tracker.Index.OpeningsOf(
                            new VoxelPos(origin.X + x, origin.Y + y, origin.Z + z), found);
                        foreach (Portal portal in found)
                        {
                            if (portal.Inside.Chunk == chunk || portal.Outside.Chunk == chunk)
                            {
                                expected.Add(portal);
                            }
                        }
                    }
                }
            }
        }

        return expected;
    }

    private static int Hash(int a, int b)
    {
        unchecked
        {
            uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663);
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return (int)(h & 0x7fffffff);
        }
    }
}
