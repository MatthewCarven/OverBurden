using MinerCraft.Core.Jobs;
using MinerCraft.Core.Voxels;

namespace MinerCraft.Core.Tests;

public class JobPriorityTests
{
    [Fact]
    public void BandsRoundTripThroughThePackedWord()
    {
        var priority = JobPriority.FromBands(band0: 1, band1: 2, band2: 3, band3: 4);

        Assert.Equal(1, priority.Band0);
        Assert.Equal(2, priority.Band1);
        Assert.Equal(3, priority.Band2);
        Assert.Equal(4, priority.Band3);
    }

    [Fact]
    public void HigherBandsOutrankLowerOnes()
    {
        // The whole point of the packing: a single unsigned compare has to reproduce the
        // band precedence. A band3 of 1 beats a maxed-out everything below it.
        var high = JobPriority.FromBands(band0: 0, band1: 0, band2: 0, band3: 1);
        var low = JobPriority.FromBands(band0: 255, band1: 255, band2: 255, band3: 0);

        Assert.True(high > low);
        Assert.True(low < high);
    }

    [Theory]
    [InlineData(0, 255)]
    [InlineData(1, 254)]
    [InlineData(200, 55)]
    [InlineData(255, 0)]
    [InlineData(1000, 0)]
    public void CloserChunksGetHigherDistanceBands(int distanceInChunks, int expectedBand0)
    {
        ViewerSet viewers = JobTestHelpers.ViewerAt(0);
        var position = new ChunkPos(distanceInChunks, 0, 0);

        JobPriority priority = JobPriority.Evaluate(JobKind.Mesh, position, viewers);

        Assert.Equal(expectedBand0, priority.Band0);
    }

    [Fact]
    public void GenerationOutranksMeshingAtEqualDistance()
    {
        // A mesh job cannot run before the data it reads exists, so scheduling it first
        // only burns a worker slot.
        ViewerSet viewers = JobTestHelpers.ViewerAt(0);
        var position = new ChunkPos(5, 0, 0);

        JobPriority mesh = JobPriority.Evaluate(JobKind.Mesh, position, viewers);
        JobPriority generate = JobPriority.Evaluate(JobKind.Generate, position, viewers);

        Assert.True(generate > mesh);
    }

    [Fact]
    public void JobClassOutranksDistance()
    {
        // Band2 sits above band0, so a distant generate still beats a nearby mesh.
        ViewerSet viewers = JobTestHelpers.ViewerAt(0);

        JobPriority nearMesh = JobPriority.Evaluate(JobKind.Mesh, new ChunkPos(1, 0, 0), viewers);
        JobPriority farGenerate = JobPriority.Evaluate(JobKind.Generate, new ChunkPos(100, 0, 0), viewers);

        Assert.True(farGenerate > nearMesh);
    }
}

public class ViewerSetTests
{
    [Fact]
    public void NoViewersMeansInfinitelyFar()
    {
        // Not zero. An empty world must not schedule its entire queue at maximum priority,
        // which is what "nearest distance is 0" would mean.
        var viewers = new ViewerSet();

        Assert.Equal(0, viewers.Count);
        Assert.Equal(int.MaxValue, viewers.NearestChebyshevDistance(ChunkPos.Zero));
        Assert.Equal(0, JobPriority.Evaluate(JobKind.Mesh, ChunkPos.Zero, viewers).Band0);
    }

    [Fact]
    public void DistanceIsToTheNearestOfSeveralViewers()
    {
        var viewers = new ViewerSet();
        viewers.Update([new ChunkPos(100, 0, 0), new ChunkPos(3, 0, 0), new ChunkPos(-50, 0, 0)]);

        Assert.Equal(3, viewers.Count);
        Assert.Equal(1, viewers.NearestChebyshevDistance(new ChunkPos(4, 0, 0)));
    }

    [Fact]
    public void DistanceIsChebyshevBecauseTheLoadedRegionIsABox()
    {
        ViewerSet viewers = JobTestHelpers.ViewerAt(0);

        // Euclidean would call this ~1.73 chunks; Chebyshev calls it 1, which is what
        // "inside a box of radius 1" actually means.
        Assert.Equal(1, viewers.NearestChebyshevDistance(new ChunkPos(1, 1, 1)));
    }

    [Fact]
    public void UpdatingViewersIsVisibleToSubsequentReads()
    {
        ViewerSet viewers = JobTestHelpers.ViewerAt(0);
        Assert.Equal(10, viewers.NearestChebyshevDistance(new ChunkPos(10, 0, 0)));

        viewers.UpdateSingle(new ChunkPos(10, 0, 0));

        Assert.Equal(0, viewers.NearestChebyshevDistance(new ChunkPos(10, 0, 0)));
    }

    [Fact]
    public void CallerMayReuseTheBufferItPassedIn()
    {
        // Update copies. If it retained the caller's array by reference, mutating the
        // buffer afterwards would silently teleport every viewer.
        var viewers = new ViewerSet();
        var buffer = new ChunkPos[] { new(5, 0, 0) };
        viewers.Update(buffer);

        buffer[0] = new ChunkPos(999, 0, 0);

        Assert.Equal(0, viewers.NearestChebyshevDistance(new ChunkPos(5, 0, 0)));
    }
}
