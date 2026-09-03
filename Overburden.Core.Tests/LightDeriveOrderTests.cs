using System.Diagnostics;
using Overburden.Core.Streaming;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// The derive queue drains nearest-first. The "ground is gone" fix (2026-09-03): in a played
/// world the stored chunks round the player arrive last, and a FIFO lit the player's own
/// neighbourhood last of all.
/// </summary>
/// <remarks>
/// <para>
/// <b>A zero light budget is what makes the order observable.</b> The step checks its deadline
/// after each build, never before, so a budget of zero derives exactly one chunk per update — and
/// the chunk that went is the one whose grid appeared. The oracle is then a boolean over the
/// state: the chunk derived this update is no farther from the viewer than any chunk still
/// waiting.
/// </para>
/// <para>
/// <b>Asserted only once the queue is full.</b> While generation is still landing, a chunk can
/// only be derived after it has voxels, and which chunk arrives first is the workers' business.
/// Once every chunk has voxels the only thing choosing the next derive is the queue.
/// </para>
/// </remarks>
public class LightDeriveOrderTests
{
    private static ChunkPos ViewerChunk(PipelineHarness harness, ViewerId viewer) =>
        harness.Pipeline.Streamer.GetViewer(viewer).Position;

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

    /// <summary>
    /// Runs one update and asserts the chunk it derived is no farther from
    /// <paramref name="centre"/> than any chunk still waiting. Returns how many were derived.
    /// </summary>
    private static int AssertNextDeriveIsNearest(PipelineHarness harness, ChunkPos centre)
    {
        ChunkStreamer streamer = harness.Pipeline.Streamer;

        // Chunks with voxels and no grid are the ones the queue can choose between.
        var waiting = streamer.Chunks
            .Where(c => c.HasVoxels && c.Light is null)
            .Select(c => c.Position)
            .ToHashSet();
        if (waiting.Count == 0) return 0;

        int nearestWaiting = waiting.Min(p => p.ChebyshevDistanceTo(centre));

        harness.Pipeline.Update();

        var derived = waiting.Where(p => streamer.Get(p)?.Light is not null).ToList();
        foreach (ChunkPos position in derived)
        {
            int distance = position.ChebyshevDistanceTo(centre);
            Assert.True(
                distance <= nearestWaiting,
                $"derived {position} at distance {distance} while a chunk at {nearestWaiting} was waiting");
        }

        return derived.Count;
    }

    [Fact]
    public void OnceTheQueueIsFullEveryDeriveIsTheNearestWaitingChunk()
    {
        using var harness = new PipelineHarness(lightBudget: TimeSpan.Zero);
        ChunkStreamer streamer = harness.Pipeline.Streamer;
        ViewerId viewer = streamer.AddViewer(new ChunkPos(3, 0, -2), 2, 1);

        PumpUntilFullyQueued(harness);
        ChunkPos centre = ViewerChunk(harness, viewer);

        int derived = 0;
        int steps = 0;
        while (harness.Pipeline.LightBuildBacklog > 0)
        {
            derived += AssertNextDeriveIsNearest(harness, centre);
            Assert.True(++steps < 10_000, "the queue never drained");
        }

        // The recipe this guards against is a queue that is a FIFO again, and a FIFO passes the
        // assertion above only if the arrivals happened to be in distance order — so the fixture
        // has to have derived a real spread of chunks after the queue filled.
        Assert.True(derived > 100, $"only {derived} chunks were derived after the queue filled");
        harness.Settle();
    }

    [Fact]
    public void AViewerMovingReordersWhatIsStillWaiting()
    {
        // The queue was sorted for the old position; the sort has to go stale when the viewer
        // moves, or a player walking across a room lights the side they came from.
        using var harness = new PipelineHarness(lightBudget: TimeSpan.Zero);
        ChunkStreamer streamer = harness.Pipeline.Streamer;
        ViewerId viewer = streamer.AddViewer(ChunkPos.Zero, 3, 1);

        PumpUntilFullyQueued(harness);

        // One derive at the old centre, so the sort has definitely run for it.
        AssertNextDeriveIsNearest(harness, ChunkPos.Zero);

        // Across the box: the old centre is now three chunks off, and most of the old box is
        // still loaded and still waiting.
        var moved = new ChunkPos(3, 0, 0);
        streamer.MoveViewer(viewer, moved);

        int derivedAfterMove = 0;
        int steps = 0;
        while (harness.Pipeline.LightBuildBacklog > 0 && steps++ < 60)
        {
            derivedAfterMove += AssertNextDeriveIsNearest(harness, moved);
        }

        Assert.True(derivedAfterMove >= 20, $"only {derivedAfterMove} derives were observed after the move");
        harness.Settle();
    }
}
