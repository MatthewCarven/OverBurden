using Overburden.Core.Streaming;
using Overburden.Core.Voxels;
using Overburden.Core.Worldgen;

namespace Overburden.Core.Tests;

public class StreamedChunkTests
{
    [Fact]
    public void AChunkStartsUnloadedAndEmpty()
    {
        StreamedChunk chunk = NewChunk();

        Assert.Equal(ChunkState.Unloaded, chunk.State);
        Assert.Equal(GenerationStage.None, chunk.Stage);
        Assert.False(chunk.HasVoxels);

        // Already 1: a chunk record only exists because a viewer asked for it, so there is
        // no moment at which a live chunk is viewed by nobody.
        Assert.Equal(1, chunk.ViewCount);
    }

    [Fact]
    public void TheHappyPathRunsEndToEnd()
    {
        StreamedChunk chunk = NewChunk();

        foreach (ChunkState next in new[]
                 {
                     ChunkState.Queued, ChunkState.Generating, ChunkState.Generated,
                     ChunkState.Meshing, ChunkState.Meshed,
                 })
        {
            Assert.True(chunk.CanTransitionTo(next));
            chunk.TransitionTo(next);
        }

        Assert.Equal(ChunkState.Meshed, chunk.State);
    }

    /// <summary>
    /// The reason this is an enum with a guard rather than three bools. Skipping a step is
    /// exactly the bug that leaves a chunk waiting on a job nobody ever submitted, and the
    /// cheapest place to find it is the call that did it.
    /// </summary>
    [Fact]
    public void SkippingAStepThrows()
    {
        StreamedChunk chunk = NewChunk();

        Assert.False(chunk.CanTransitionTo(ChunkState.Generating));
        Assert.Throws<InvalidOperationException>(() => chunk.TransitionTo(ChunkState.Generating));

        chunk.TransitionTo(ChunkState.Queued);
        Assert.Throws<InvalidOperationException>(() => chunk.TransitionTo(ChunkState.Meshing));
    }

    [Fact]
    public void ReEnteringTheSameStateIsRejected()
    {
        StreamedChunk chunk = NewChunk();
        chunk.TransitionTo(ChunkState.Queued);

        // Almost always a double-submit, and never meaningful.
        Assert.False(chunk.CanTransitionTo(ChunkState.Queued));
    }

    /// <summary>A viewer can leave at any point, including mid-job.</summary>
    [Theory]
    [InlineData(ChunkState.Queued)]
    [InlineData(ChunkState.Generating)]
    [InlineData(ChunkState.Generated)]
    [InlineData(ChunkState.Meshing)]
    [InlineData(ChunkState.Meshed)]
    public void EveryStateCanUnload(ChunkState from)
    {
        StreamedChunk chunk = AdvancedTo(from);

        chunk.TransitionTo(ChunkState.Unloaded);
        Assert.Equal(ChunkState.Unloaded, chunk.State);
    }

    /// <summary>
    /// Cancellation is not deletion. A dropped job hands the chunk back needing the same
    /// work again — and a dropped <em>mesh</em> goes back only as far as Generated, because
    /// the voxels it was reading are still perfectly good.
    /// </summary>
    [Fact]
    public void DroppedJobsStepBackExactlyOneStage()
    {
        StreamedChunk generating = AdvancedTo(ChunkState.Generating);
        generating.TransitionTo(ChunkState.Queued);
        Assert.Equal(ChunkState.Queued, generating.State);

        StreamedChunk meshing = AdvancedTo(ChunkState.Meshing);
        meshing.TransitionTo(ChunkState.Generated);
        Assert.Equal(ChunkState.Generated, meshing.State);

        // But not two: a dropped mesh must not discard the voxels.
        Assert.False(meshing.CanTransitionTo(ChunkState.Queued));
    }

    /// <summary>Not needed until M3, but it is the same edge and it should exist now.</summary>
    [Fact]
    public void AMeshedChunkCanBeReMeshed()
    {
        StreamedChunk chunk = AdvancedTo(ChunkState.Meshed);

        chunk.TransitionTo(ChunkState.Meshing);
        Assert.Equal(ChunkState.Meshing, chunk.State);
    }

    private static StreamedChunk NewChunk() =>
        new ChunkStreamer(loadMargin: 0).AddViewerAndTake();

    private static StreamedChunk AdvancedTo(ChunkState state)
    {
        StreamedChunk chunk = NewChunk();

        foreach (ChunkState next in new[]
                 {
                     ChunkState.Queued, ChunkState.Generating, ChunkState.Generated,
                     ChunkState.Meshing, ChunkState.Meshed,
                 })
        {
            chunk.TransitionTo(next);
            if (next == state)
            {
                break;
            }
        }

        return chunk;
    }
}

internal static class StreamedChunkTestHelpers
{
    /// <summary>
    /// Produces a single bare chunk. <see cref="StreamedChunk"/>'s constructor is internal
    /// to the streamer on purpose — chunks exist because a viewer wants them — so tests get
    /// one the same way the game does.
    /// </summary>
    internal static StreamedChunk AddViewerAndTake(this ChunkStreamer streamer)
    {
        streamer.AddViewer(ChunkPos.Zero, horizontalRadius: 0, verticalRadius: 0);
        return streamer.Get(ChunkPos.Zero)!;
    }
}
