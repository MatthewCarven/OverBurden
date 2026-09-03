using System.Numerics;
using Overburden.Core.Content;
using Overburden.Core.Mining;
using Overburden.Core.Streaming;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// A swing against the real pipeline — real worldgen, real scheduler, real worker threads.
/// </summary>
/// <remarks>
/// <see cref="CarveTests"/> checks the geometry against a dictionary; these check the thing
/// the geometry is for. The failure they exist to catch is the one that looks like nothing
/// happening at all: voxels correctly rewritten and no mesh ever rebuilt to show it.
/// </remarks>
public class CarveThroughPipelineTests
{
    private const int Air = MaterialIds.AirNumericId;
    private const int Dirt = 2;
    private const int Stone = 3;

    /// <summary>Deep enough that the whole swing is below the lowest the surface can reach.</summary>
    private static readonly ChunkPos Deep = new(0, -3, 0);

    [Fact]
    public void ASwingRemovesTerrainAndRemeshesEveryChunkItTouchedExactlyOnce()
    {
        using var harness = new PipelineHarness();
        harness.Pipeline.Streamer.AddViewer(Deep, 1, 1);
        harness.Settle();

        // The corner eight chunks meet at, so one swing crosses every boundary there is.
        var centre = new Vector3(
            Deep.OriginVoxel.X * VoxelConstants.VoxelSize,
            Deep.OriginVoxel.Y * VoxelConstants.VoxelSize,
            Deep.OriginVoxel.Z * VoxelConstants.VoxelSize);

        var before = new Dictionary<ChunkPos, int>(harness.Deliveries);

        CarveResult result = Carve.Sphere(
            harness.Pipeline, PipelineHarness.Materials(), centre, 0.5f, 3.5f);

        Assert.True(result.Complete);
        Assert.Equal(0, result.TooHard);
        Assert.True(result.Removed > 0);
        Assert.Equal(result.Removed, harness.Pipeline.EditedVoxels);

        // Written, before a single frame of the pipeline has run. The edit path is
        // synchronous; only the mesh that shows it is not.
        Assert.True(harness.Pipeline.TryGetMaterial(new VoxelPos(0, Deep.OriginVoxel.Y, 0), out int material));
        Assert.Equal(Air, material);

        harness.Settle();

        Dictionary<ChunkPos, int> remeshed = Remeshes(harness, before);

        // Hundreds of voxels across eight chunks, and one remesh each. The count is the
        // coalescing claim — a remesh per voxel, or per dirty mark, would be hundreds — and
        // the fact that it is not *more* than eight is the dirty set not spilling outward.
        // Under-dirtying is not what this catches: the writes already span eight chunks by
        // themselves. That is ChunkEditsTests' job, and the corner case below.
        Assert.Equal(8, remeshed.Count);
        Assert.All(remeshed.Values, count => Assert.Equal(1, count));

        for (int dz = -1; dz <= 0; dz++)
        for (int dy = -1; dy <= 0; dy++)
        for (int dx = -1; dx <= 0; dx++)
        {
            Assert.Contains(Deep.Offset(dx, dy, dz), remeshed.Keys);
        }
    }

    [Fact]
    public void OneVoxelTakenAtAChunkCornerStillRemeshesEightChunks()
    {
        // The border rule end to end, and the reason this test is separate: exactly one voxel
        // is written, in exactly one chunk, and seven other chunks have to rebuild because
        // their padded copy reads it. Missing it leaves a strip of stale geometry along the
        // seam, which only shows when you mine near one — which is most mining.
        using var harness = new PipelineHarness();
        harness.Pipeline.Streamer.AddViewer(Deep, 1, 1);
        harness.Settle();

        (float x, float y, float z) = Deep.OriginVoxel.CentreMetres;
        var before = new Dictionary<ChunkPos, int>(harness.Deliveries);

        // A tenth of a voxel, so the aimed-at voxel is the only one whose centre can be
        // inside — its nearest neighbour's is a whole voxel away.
        CarveResult result = Carve.Sphere(
            harness.Pipeline, PipelineHarness.Materials(), new Vector3(x, y, z), 0.025f, 3.5f);

        Assert.Equal(1, result.Removed);
        harness.Settle();

        Dictionary<ChunkPos, int> remeshed = Remeshes(harness, before);

        Assert.Equal(8, remeshed.Count);
        Assert.All(remeshed.Values, count => Assert.Equal(1, count));
    }

    [Fact]
    public void OnlyEditRemeshesAreCountedAsEditRemeshes()
    {
        // Remeshes per swing is the number M3d is asking about, so it must not quietly
        // include the streaming meshes that fill the view in the first place — which
        // outnumber it by a hundred to one on any real run.
        using var harness = new PipelineHarness();
        harness.Pipeline.Streamer.AddViewer(Deep, 1, 1);
        harness.Settle();

        Assert.True(harness.MeshesDelivered > 0, "the view must have meshed something first");
        Assert.Equal(0, harness.Pipeline.EditRemeshes);

        var centre = new Vector3(
            Deep.OriginVoxel.X * VoxelConstants.VoxelSize,
            Deep.OriginVoxel.Y * VoxelConstants.VoxelSize,
            Deep.OriginVoxel.Z * VoxelConstants.VoxelSize);

        Carve.Sphere(harness.Pipeline, PipelineHarness.Materials(), centre, 0.5f, 3.5f);
        harness.Settle();

        // The eight chunks the corner swing touched, and nothing else.
        Assert.Equal(8, harness.Pipeline.EditRemeshes);
    }

    /// <summary>How many extra meshes each chunk has been handed since <paramref name="before"/>.</summary>
    private static Dictionary<ChunkPos, int> Remeshes(
        PipelineHarness harness, Dictionary<ChunkPos, int> before) =>
        harness.Deliveries
            .Where(entry => entry.Value > before.GetValueOrDefault(entry.Key))
            .ToDictionary(entry => entry.Key, entry => entry.Value - before.GetValueOrDefault(entry.Key));

    [Fact]
    public void ASecondSwingAtTerrainAlreadyRemovedTakesNothingAndRemeshesNothing()
    {
        using var harness = new PipelineHarness();
        harness.Pipeline.Streamer.AddViewer(Deep, 1, 1);
        harness.Settle();

        var centre = new Vector3(
            Deep.OriginVoxel.X * VoxelConstants.VoxelSize,
            Deep.OriginVoxel.Y * VoxelConstants.VoxelSize,
            Deep.OriginVoxel.Z * VoxelConstants.VoxelSize);

        MaterialTable materials = PipelineHarness.Materials();
        Carve.Sphere(harness.Pipeline, materials, centre, 0.5f, 3.5f);
        harness.Settle();

        var before = new Dictionary<ChunkPos, int>(harness.Deliveries);
        int clonesBefore = harness.Pipeline.ChunkClones;

        CarveResult again = Carve.Sphere(harness.Pipeline, materials, centre, 0.5f, 3.5f);
        harness.Settle();

        // A held mouse button is this case, hundreds of times a second. Every voxel in the
        // sphere is already air, so nothing is dirtied, nothing is cloned and nothing is
        // rebuilt — and if any of those three were untrue, mining would get more expensive
        // the longer you did it.
        Assert.Equal(0, again.Removed);
        Assert.Equal(0, again.TooHard);
        Assert.Equal(clonesBefore, harness.Pipeline.ChunkClones);
        Assert.Equal(before, harness.Deliveries);
    }

    [Fact]
    public void AViewerLeavingWhileAnEditRemeshIsInFlightStillGetsTheMeshHandedBack()
    {
        // The M3-shaped hole in the unload path, and it needs a carve to reach. Before the
        // Meshed -> Meshing edge existed, a chunk that was Meshing had never delivered a mesh,
        // so "has a live mesh" and "is Meshed" were the same question. With re-meshing they
        // are not, and a viewer leaving here would leave the pre-edit geometry drawn for
        // terrain that has been unloaded, until the player happened to come back to it.
        using var harness = new PipelineHarness();
        ChunkStreamer streamer = harness.Pipeline.Streamer;

        ViewerId id = streamer.AddViewer(Deep, 1, 1);
        harness.Settle();
        Assert.Contains(Deep, harness.MeshedPositions);

        // Well inside the chunk, so exactly one chunk is dirtied and goes round again.
        (float x, float y, float z) = VoxelPos.FromChunkLocal(Deep, 16, 16, 16).CentreMetres;
        CarveResult result = Carve.Sphere(
            harness.Pipeline, PipelineHarness.Materials(), new Vector3(x, y, z), 0.3f, 3.5f);

        Assert.True(result.Removed > 0);

        harness.Pipeline.Update();
        Assert.Equal(ChunkState.Meshing, streamer.Get(Deep)!.State);

        streamer.RemoveViewer(id);
        harness.Settle();

        Assert.Equal(0, streamer.TrackedCount);
        Assert.Empty(harness.MeshedPositions);
        Assert.Equal(harness.MeshesDelivered, harness.MeshesDiscarded);
    }

    [Fact]
    public void AWeakToolTakesTheDirtAndLeavesTheStoneStanding()
    {
        // The mechanic, end to end against generated terrain rather than a hand-built
        // fixture. Asserted by idempotence rather than by re-deriving the sphere: swing
        // again and nothing soft is left, swing with a stronger tier and exactly the refused
        // voxels come away. Neither assertion needs to know the shape.
        using var harness = new PipelineHarness();
        harness.Pipeline.Streamer.AddViewer(ChunkPos.Zero, 2, 2);
        harness.Settle();

        MaterialTable materials = PipelineHarness.Materials();
        Vector3 centre = FindDirtOverStone(harness);

        CarveResult weak = Carve.Sphere(harness.Pipeline, materials, centre, 0.5f, 1.0f);

        Assert.True(weak.Removed > 0, "the swing must take some dirt");
        Assert.True(weak.TooHard > 0, "the swing must be refused by some stone");
        Assert.True(weak.Complete);

        CarveResult weakAgain = Carve.Sphere(harness.Pipeline, materials, centre, 0.5f, 1.0f);

        Assert.Equal(0, weakAgain.Removed);
        Assert.Equal(weak.TooHard, weakAgain.TooHard);

        CarveResult strong = Carve.Sphere(harness.Pipeline, materials, centre, 0.5f, 3.5f);

        Assert.Equal(weak.TooHard, strong.Removed);
        Assert.Equal(0, strong.TooHard);

        harness.Settle();
    }

    [Fact]
    public void ASwingPastTheLoadedRegionReportsWhatItCouldNotReach()
    {
        // The whole reason CarveResult carries a third number. A swing at the edge of the
        // loaded world silently does less than it looks like it did, and the player has
        // nothing but the shape of the hole to tell them why.
        using var harness = new PipelineHarness();
        harness.Pipeline.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
        harness.Settle();

        CarveResult result = Carve.Sphere(
            harness.Pipeline,
            PipelineHarness.Materials(),
            new Vector3(4000f, 0f, 4000f),
            0.5f,
            3.5f);

        Assert.False(result.Complete);
        Assert.True(result.Unreachable > 0);
        Assert.Equal(0, result.Removed);
        Assert.Equal(0, harness.Pipeline.EditedVoxels);
    }

    /// <summary>
    /// The world centre of a dirt voxel sitting directly on stone, so a swing there straddles
    /// the boundary a weak tool is supposed to stop at.
    /// </summary>
    private static Vector3 FindDirtOverStone(PipelineHarness harness)
    {
        foreach (ChunkPos chunk in harness.Pipeline.Streamer.Chunks
                     .Where(c => c.State == ChunkState.Meshed)
                     .Select(c => c.Position))
        {
            for (int lx = 0; lx < VoxelConstants.ChunkSize; lx++)
            for (int lz = 0; lz < VoxelConstants.ChunkSize; lz++)
            for (int ly = 0; ly < VoxelConstants.ChunkSize; ly++)
            {
                VoxelPos voxel = VoxelPos.FromChunkLocal(chunk, lx, ly, lz);

                if (!harness.Pipeline.TryGetMaterial(voxel, out int here) || here != Dirt)
                {
                    continue;
                }

                if (harness.Pipeline.TryGetMaterial(voxel.Offset(0, -1, 0), out int below)
                    && below == Stone)
                {
                    (float x, float y, float z) = voxel.CentreMetres;
                    return new Vector3(x, y, z);
                }
            }
        }

        throw new InvalidOperationException(
            "No dirt-on-stone boundary in the loaded region; the strata table or the view " +
            "radius has changed and this test no longer sets up what it claims to.");
    }
}
