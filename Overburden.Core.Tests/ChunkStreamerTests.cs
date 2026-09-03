using Overburden.Core.Jobs;
using Overburden.Core.Streaming;
using Overburden.Core.Voxels;
using Overburden.Core.Worldgen;

namespace Overburden.Core.Tests;

public class ChunkStreamerTests
{
    [Fact]
    public void AddingAViewerLoadsItsBoxPlusTheMargin()
    {
        var streamer = new ChunkStreamer(loadMargin: 2);
        streamer.AddViewer(ChunkPos.Zero, horizontalRadius: 3, verticalRadius: 1);

        // Visible box is 7×3×7; loaded is that grown by two on every side.
        Assert.Equal(11 * 7 * 11, streamer.ViewedCount);
        Assert.True(streamer.IsViewed(new ChunkPos(5, 3, 5)));
        Assert.False(streamer.IsViewed(new ChunkPos(6, 0, 0)));
    }

    [Fact]
    public void EveryLoadedChunkStartsUnloadedAndUnviewedByNobodyElse()
    {
        var streamer = new ChunkStreamer(loadMargin: 0);
        streamer.AddViewer(ChunkPos.Zero, 2, 2);

        Assert.All(streamer.Chunks, c =>
        {
            Assert.Equal(ChunkState.Unloaded, c.State);
            Assert.Equal(GenerationStage.None, c.Stage);
            Assert.Equal(1, c.ViewCount);
        });
    }

    /// <summary>
    /// Chunk quantisation is the hysteresis. A viewer that moves within the chunk it is
    /// already in produces the same box, so nothing loads and nothing unloads — a player
    /// standing on a boundary cannot thrash.
    /// </summary>
    [Fact]
    public void MovingWithinTheSameChunkDoesNothing()
    {
        var viewed = new List<ChunkPos>();
        var unviewed = new List<ChunkPos>();
        var streamer = new ChunkStreamer(
            onChunkViewed: c => viewed.Add(c.Position),
            onChunkUnviewed: c => unviewed.Add(c.Position));

        ViewerId id = streamer.AddViewer(new ChunkPos(4, 0, 4), 3, 3);
        int loadedAfterAdd = viewed.Count;

        streamer.MoveViewer(id, new ChunkPos(4, 0, 4));

        Assert.Equal(loadedAfterAdd, viewed.Count);
        Assert.Empty(unviewed);
    }

    [Fact]
    public void MovingOneChunkTouchesOnlyTheShells()
    {
        var viewed = new List<ChunkPos>();
        var unviewed = new List<ChunkPos>();
        var streamer = new ChunkStreamer(
            onChunkViewed: c => viewed.Add(c.Position),
            onChunkUnviewed: c => unviewed.Add(c.Position),
            loadMargin: 0);

        const int radius = 4;
        const int side = 2 * radius + 1;

        ViewerId id = streamer.AddViewer(ChunkPos.Zero, radius, radius);
        viewed.Clear();

        streamer.MoveViewer(id, new ChunkPos(1, 0, 0));

        Assert.Equal(side * side, viewed.Count);
        Assert.Equal(side * side, unviewed.Count);
        Assert.All(viewed, p => Assert.Equal(radius + 1, p.X));
        Assert.All(unviewed, p => Assert.Equal(-radius, p.X));
        Assert.Equal(side * side * side, streamer.ViewedCount);
    }

    /// <summary>
    /// The refcount, which is the whole reason this is not a <c>HashSet</c> of positions.
    /// Two viewers standing near each other share chunks, and one of them walking away must
    /// not take the other's terrain with it.
    /// </summary>
    [Fact]
    public void AChunkSharedByTwoViewersSurvivesOneOfThemLeaving()
    {
        var unviewed = new List<ChunkPos>();
        var streamer = new ChunkStreamer(
            onChunkUnviewed: c => unviewed.Add(c.Position), loadMargin: 0);

        ViewerId a = streamer.AddViewer(ChunkPos.Zero, 2, 2);
        streamer.AddViewer(new ChunkPos(1, 0, 0), 2, 2);

        var shared = new ChunkPos(1, 0, 0);
        Assert.Equal(2, streamer.ViewCountOf(shared));

        // Only viewer `a` reaches this one.
        var aOnly = new ChunkPos(-2, 0, 0);
        Assert.Equal(1, streamer.ViewCountOf(aOnly));

        streamer.RemoveViewer(a);

        Assert.True(streamer.IsViewed(shared));
        Assert.Equal(1, streamer.ViewCountOf(shared));
        Assert.False(streamer.IsViewed(aOnly));
        Assert.Contains(aOnly, unviewed);
        Assert.DoesNotContain(shared, unviewed);
    }

    [Fact]
    public void RemovingTheLastViewerUnloadsEverything()
    {
        var streamer = new ChunkStreamer(loadMargin: 1);
        ViewerId id = streamer.AddViewer(ChunkPos.Zero, 3, 3);

        Assert.True(streamer.ViewedCount > 0);
        streamer.RemoveViewer(id);

        Assert.Equal(0, streamer.ViewedCount);
        Assert.Equal(0, streamer.ViewerCount);
    }

    [Fact]
    public void ChangingRadiusLoadsAndUnloadsTheDifference()
    {
        var streamer = new ChunkStreamer(loadMargin: 0);
        ViewerId id = streamer.AddViewer(ChunkPos.Zero, 1, 1);
        Assert.Equal(27, streamer.ViewedCount);

        streamer.UpdateViewer(id, new Viewer(ChunkPos.Zero, 2, 2));
        Assert.Equal(125, streamer.ViewedCount);

        streamer.UpdateViewer(id, new Viewer(ChunkPos.Zero, 0, 0));
        Assert.Equal(1, streamer.ViewedCount);
    }

    /// <summary>
    /// Streaming and job priority must never disagree about where the player is, so the
    /// streamer owns publishing to the <see cref="ViewerSet"/> the queue sorts against
    /// rather than leaving two call sites to be kept in step by hand.
    /// </summary>
    [Fact]
    public void PriorityViewersFollowTheStreamingViewers()
    {
        var priority = new ViewerSet();
        var streamer = new ChunkStreamer(priority, loadMargin: 0);

        Assert.Equal(0, priority.Count);

        ViewerId id = streamer.AddViewer(new ChunkPos(10, 0, 0), 1, 1);
        Assert.Equal(1, priority.Count);
        Assert.Equal(0, priority.NearestChebyshevDistance(new ChunkPos(10, 0, 0)));

        streamer.MoveViewer(id, new ChunkPos(40, 0, 0));
        Assert.Equal(30, priority.NearestChebyshevDistance(new ChunkPos(10, 0, 0)));

        streamer.RemoveViewer(id);
        Assert.Equal(0, priority.Count);
    }

    [Fact]
    public void TheStageBarrierNeedsEveryNeighbour()
    {
        var streamer = new ChunkStreamer(loadMargin: 0);
        streamer.AddViewer(ChunkPos.Zero, 1, 1);

        // Nothing generated yet.
        Assert.False(streamer.CanAdvanceToFeatures(ChunkPos.Zero));

        SetStageEverywhere(streamer, GenerationStage.Terrain);
        Assert.True(streamer.CanAdvanceToFeatures(ChunkPos.Zero));

        // A single neighbour falling behind is enough to hold the centre back.
        streamer.Get(new ChunkPos(0, 1, 0))!.Stage = GenerationStage.None;
        Assert.False(streamer.CanAdvanceToFeatures(ChunkPos.Zero));
    }

    [Fact]
    public void AChunkOnTheEdgeOfTheLoadedRegionCannotAdvance()
    {
        var streamer = new ChunkStreamer(loadMargin: 0);
        streamer.AddViewer(ChunkPos.Zero, 2, 2);
        SetStageEverywhere(streamer, GenerationStage.Terrain);

        // Its outward neighbours were never loaded, so there is nothing to write into.
        Assert.False(streamer.CanAdvanceToFeatures(new ChunkPos(2, 0, 0)));
        Assert.True(streamer.CanAdvanceToFeatures(new ChunkPos(1, 0, 0)));
    }

    /// <summary>
    /// <b>The property that pays for the load margin.</b> Run the whole staged pipeline to
    /// a standstill and the set of meshable chunks lands exactly on the viewer's visible
    /// box — no wider, so nothing is drawn from voxels a neighbour could still change, and
    /// no narrower, so there is no hole at the edge of view.
    /// </summary>
    /// <remarks>
    /// Nothing tracks which shell a chunk is in. The margin generates and never meshes
    /// purely because it fails the barrier, which is why there is one refcount here and not
    /// two.
    /// </remarks>
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    public void TheMeshableSetIsExactlyTheVisibleBox(int radius)
    {
        var streamer = new ChunkStreamer(loadMargin: ChunkStreamer.DefaultLoadMargin);
        var centre = new ChunkPos(3, -1, 2);
        streamer.AddViewer(centre, radius, radius);

        // Stage 1 needs no neighbours, so everything loaded reaches it.
        SetStageEverywhere(streamer, GenerationStage.Terrain);

        // Stage 2 until it stops making progress.
        while (AdvanceFeaturesOnce(streamer) > 0)
        {
        }

        // Stage 3. Every chunk that reached Features can reach Lit — deriving a chunk's light
        // needs its neighbours' *voxels*, and reaching Features already required all 26 of them
        // to have terrain. That is precisely why light adds no ring to the load margin, and it
        // is what this test would notice stopping being true: a third ring would show up here as
        // the meshable set falling one chunk short of the visible box on every axis.
        AdvanceLitEverywhere(streamer);

        var meshable = streamer.Chunks
            .Where(c => streamer.CanMesh(c.Position))
            .Select(c => c.Position)
            .ToHashSet();

        var expected = new HashSet<ChunkPos>();
        ChunkBox.Around(centre, radius, radius).ForEach(p => expected.Add(p));

        Assert.Equal(expected, meshable);
        Assert.All(meshable, p => Assert.True(p.ChebyshevDistanceTo(centre) <= radius));
    }

    /// <summary>
    /// The fuzzable invariant from docs/M2-PLAN.md: after any sequence of viewer moves,
    /// additions and removals, the loaded set is exactly the union of the viewer boxes and
    /// every view count is exactly the number of viewers that reach it.
    /// </summary>
    /// <remarks>
    /// A count that drifts high is a chunk that never unloads — the leak that reads as a
    /// memory leak, isn't one, and is miserable to find by hand. A count that drifts low
    /// unloads terrain somebody is still standing on. Checking counts rather than just set
    /// membership is what separates the two.
    /// </remarks>
    [Fact]
    public void LoadedChunksAlwaysMatchTheUnionOfViewerBoxes()
    {
        var random = new Random(2026_08_27);
        var streamer = new ChunkStreamer(loadMargin: 1);
        var live = new List<ViewerId>();

        for (int step = 0; step < 400; step++)
        {
            switch (random.Next(4))
            {
                case 0 when live.Count < 4:
                    live.Add(streamer.AddViewer(RandomPos(random), random.Next(0, 4), random.Next(0, 3)));
                    break;

                case 1 when live.Count > 0:
                    ViewerId doomed = live[random.Next(live.Count)];
                    streamer.RemoveViewer(doomed);
                    live.Remove(doomed);
                    break;

                case 2 when live.Count > 0:
                    streamer.MoveViewer(live[random.Next(live.Count)], RandomPos(random));
                    break;

                case 3 when live.Count > 0:
                    ViewerId resized = live[random.Next(live.Count)];
                    streamer.UpdateViewer(
                        resized,
                        streamer.GetViewer(resized) with { HorizontalRadius = random.Next(0, 4) });
                    break;
            }

            AssertMatchesViewers(streamer, live);
        }

        foreach (ViewerId id in live)
        {
            streamer.RemoveViewer(id);
        }

        Assert.Equal(0, streamer.ViewedCount);
    }

    private static void AssertMatchesViewers(ChunkStreamer streamer, List<ViewerId> live)
    {
        var expected = new Dictionary<ChunkPos, int>();
        foreach (ViewerId id in live)
        {
            streamer.LoadBoxOf(streamer.GetViewer(id))
                .ForEach(p => expected[p] = expected.GetValueOrDefault(p) + 1);
        }

        Assert.Equal(expected.Count, streamer.ViewedCount);
        foreach ((ChunkPos position, int count) in expected)
        {
            Assert.Equal(count, streamer.ViewCountOf(position));
        }
    }

    private static ChunkPos RandomPos(Random random) =>
        new(random.Next(-5, 6), random.Next(-3, 4), random.Next(-5, 6));

    private static void SetStageEverywhere(ChunkStreamer streamer, GenerationStage stage)
    {
        foreach (StreamedChunk chunk in streamer.Chunks)
        {
            chunk.Stage = stage;
        }
    }

    /// <summary>
    /// One pass of the stage-2 barrier, returning how many chunks advanced.
    /// </summary>
    /// <remarks>
    /// Eligibility is collected before anything is written, so a pass sees a single
    /// consistent snapshot rather than letting a chunk advance on the strength of a
    /// neighbour that advanced moments earlier in the same sweep.
    /// </remarks>
    private static int AdvanceFeaturesOnce(ChunkStreamer streamer)
    {
        List<StreamedChunk> ready = streamer.Chunks
            .Where(c => streamer.CanAdvanceToFeatures(c.Position))
            .ToList();

        foreach (StreamedChunk chunk in ready)
        {
            chunk.Stage = GenerationStage.Features;
        }

        return ready.Count;
    }

    /// <summary>
    /// The light stage, as the pipeline reaches it: every chunk at
    /// <see cref="GenerationStage.Features"/> goes to <see cref="GenerationStage.Lit"/>.
    /// </summary>
    /// <remarks>
    /// <b>Unlike the two below it, this rung has no neighbour condition</b>, and that is the
    /// decision it encodes rather than an omission. A chunk derives its light cells from voxels
    /// — its own and its neighbours' — and every neighbour of a chunk at
    /// <see cref="GenerationStage.Features"/> already has terrain by that stage's own barrier.
    /// The condition the real pipeline adds is that the global propagation has settled, which is
    /// not a property of any chunk and so has nothing to say here.
    /// </remarks>
    private static void AdvanceLitEverywhere(ChunkStreamer streamer)
    {
        foreach (StreamedChunk chunk in streamer.Chunks)
        {
            if (chunk.Stage == GenerationStage.Features)
            {
                chunk.Stage = GenerationStage.Lit;
            }
        }
    }
}
