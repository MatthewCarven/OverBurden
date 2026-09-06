using System.Diagnostics;
using Overburden.Core.Streaming;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// docs/ADAPTIVE-RADIUS-PLAN.md, tests 10 and 11: what the streamer does under a radius change,
/// and the backlog the policy reads. The policy itself is <see cref="ViewRadiusPolicyTests"/>.
/// </summary>
public class ViewRadiusStreamingTests
{
    private const long GenerousCap = 64L * 1024 * 1024;
    private static readonly ChunkPos Origin = new(0, 0, 0);

    private static int LoadedCount(int horizontal, int vertical, int margin)
    {
        int wide = (2 * (horizontal + margin)) + 1;
        int tall = (2 * (vertical + margin)) + 1;
        return wide * wide * tall;
    }

    private static Dictionary<ChunkPos, StreamedChunk> Records(ChunkStreamer streamer, ChunkBox box)
    {
        var records = new Dictionary<ChunkPos, StreamedChunk>();
        box.ForEach(position =>
            records[position] = streamer.Get(position)
                ?? throw new Xunit.Sdk.XunitException($"{position} is not tracked"));
        return records;
    }

    [Fact]
    public void AShrinkLeavesTheInnerBoxUntouchedAndPutsExactlyTheRingInTheCache()
    {
        // Recipe: rebuild the box (forget everything, view everything) instead of taking the
        // difference — fails on record identity, and on the cache holding the whole old box.
        using var harness = new PipelineHarness(chunkCacheBytes: GenerousCap);
        ChunkStreamer streamer = harness.Pipeline.Streamer;
        int margin = streamer.LoadMargin;
        ViewerId viewer = streamer.AddViewer(Origin, 3, 2);
        harness.Settle();

        int full = LoadedCount(3, 2, margin);
        int floor = LoadedCount(1, 1, margin);
        Assert.Equal(full, streamer.TrackedCount);

        ChunkBox innerLoadBox = ChunkBox.Around(Origin, 1 + margin, 1 + margin);
        Dictionary<ChunkPos, StreamedChunk> before = Records(streamer, innerLoadBox);
        int generatedBefore = harness.Pipeline.ChunksGenerated;

        streamer.UpdateViewer(viewer, new Viewer(Origin, 1, 1));

        Assert.Equal(floor, streamer.TrackedCount);
        Assert.Equal(full - floor, streamer.CachedChunkCount);
        foreach ((ChunkPos position, StreamedChunk record) in before)
        {
            Assert.Same(record, streamer.Get(position));
        }

        // And back: every chunk of the ring is a cache hit, and nothing is generated for it.
        streamer.UpdateViewer(viewer, new Viewer(Origin, 3, 2));
        Assert.Equal(full - floor, streamer.CacheHits);
        Assert.Equal(0, streamer.CachedChunkCount);
        Assert.Equal(full, streamer.TrackedCount);

        harness.Settle();
        Assert.Equal(generatedBefore, harness.Pipeline.ChunksGenerated);
    }

    [Fact]
    public void AGrowGeneratesExactlyTheRingAndAShrinkAfterItCachesExactlyTheRing()
    {
        // The other order, because the policy's own life starts at the floor.
        using var harness = new PipelineHarness(chunkCacheBytes: GenerousCap);
        ChunkStreamer streamer = harness.Pipeline.Streamer;
        int margin = streamer.LoadMargin;
        ViewerId viewer = streamer.AddViewer(Origin, 1, 1);
        harness.Settle();

        int floor = LoadedCount(1, 1, margin);
        int full = LoadedCount(3, 2, margin);
        Assert.Equal(floor, streamer.TrackedCount);
        int generatedAtFloor = harness.Pipeline.ChunksGenerated;

        ChunkBox innerLoadBox = ChunkBox.Around(Origin, 1 + margin, 1 + margin);
        Dictionary<ChunkPos, StreamedChunk> before = Records(streamer, innerLoadBox);

        streamer.UpdateViewer(viewer, new Viewer(Origin, 3, 2));
        harness.Settle();

        Assert.Equal(full, streamer.TrackedCount);
        Assert.Equal(full - floor, harness.Pipeline.ChunksGenerated - generatedAtFloor);
        Assert.Equal(0, streamer.CacheHits);
        foreach ((ChunkPos position, StreamedChunk record) in before)
        {
            Assert.Same(record, streamer.Get(position));
        }

        streamer.UpdateViewer(viewer, new Viewer(Origin, 1, 1));
        Assert.Equal(floor, streamer.TrackedCount);
        Assert.Equal(full - floor, streamer.CachedChunkCount);
    }

    [Fact]
    public void TheBacklogFallsByExactlyOnePerUpdateUnderAZeroBudget()
    {
        // The derive-queue slice's zero-budget oracle, pointed at the count the policy reads:
        // a budget of zero derives exactly one chunk per update, so the backlog is a countdown.
        // Recipe: count enqueues rather than what is pending.
        using var harness = new PipelineHarness(lightBudget: TimeSpan.Zero);
        harness.Pipeline.Streamer.AddViewer(Origin, 2, 1);
        PumpUntilFullyQueued(harness);

        int backlog = harness.Pipeline.LightBuildBacklog;
        Assert.True(backlog > 50, $"only {backlog} queued; the fixture never filled the queue");

        for (int i = 1; i <= 20; i++)
        {
            harness.Pipeline.Update();
            Assert.Equal(backlog - i, harness.Pipeline.LightBuildBacklog);
        }

        harness.Settle();
        Assert.Equal(0, harness.Pipeline.LightBuildBacklog);
    }

    /// <summary>Pumps until every tracked chunk has voxels and the underived ones are all queued.</summary>
    private static void PumpUntilFullyQueued(PipelineHarness harness)
    {
        var clock = Stopwatch.StartNew();
        ChunkStreamer streamer = harness.Pipeline.Streamer;

        while (true)
        {
            harness.Pipeline.Update();

            bool generated = harness.Pipeline.JobsInFlight == 0 && streamer.Chunks.All(c => c.HasVoxels);
            int underived = streamer.Chunks.Count(c => c.Light is null);
            if (generated && underived == harness.Pipeline.LightBuildBacklog && underived > 0)
            {
                return;
            }

            Assert.True(clock.ElapsedMilliseconds < 30_000, "the world never finished generating");
            Assert.True(harness.Pipeline.LightBuildBacklog > 0 || underived > 0, "everything derived before the queue was observed");
            Thread.Sleep(1);
        }
    }
}
