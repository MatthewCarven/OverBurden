using System.Diagnostics;
using MinerCraft.Core.Content;
using MinerCraft.Core.Jobs;
using MinerCraft.Core.Meshing;
using MinerCraft.Core.Streaming;
using MinerCraft.Core.Voxels;
using MinerCraft.Core.Worldgen;

namespace MinerCraft.Core.Tests;

/// <summary>
/// The pipeline against a real <see cref="JobScheduler"/> on real threads. These are
/// integration tests on purpose — the bugs M2-PLAN.md warned about only exist in the
/// interaction between refcounting, cancellation and async completion, and a fake scheduler
/// that completes jobs inline would have none of it.
/// </summary>
public class ChunkPipelineTests
{
    [Fact]
    public void AViewerGeneratesAndMeshesItsVisibleBox()
    {
        using var harness = new PipelineHarness();
        harness.Pipeline.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
        harness.Settle();

        Assert.Equal(0, harness.Pipeline.FaultedJobs);

        // Everything visible is meshed and has voxels.
        foreach (ChunkPos position in Box(ChunkPos.Zero, 1))
        {
            StreamedChunk chunk = harness.Pipeline.Streamer.Get(position)!;
            Assert.Equal(ChunkState.Meshed, chunk.State);
            // Lit, not Features: a drawn chunk is one whose light has settled, which since M5c
            // is the last rung of the barrier rather than the second.
            Assert.Equal(GenerationStage.Lit, chunk.Stage);
            Assert.True(chunk.HasVoxels);
        }
    }

    /// <summary>
    /// The M2c-1 property, now proved through the real pipeline rather than by driving the
    /// stages by hand: meshes are delivered for exactly the visible box.
    /// </summary>
    [Fact]
    public void MeshesAreDeliveredForExactlyTheVisibleBoxAndTheMarginOnlyGenerates()
    {
        using var harness = new PipelineHarness();
        harness.Pipeline.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
        harness.Settle();

        Assert.Equal(Box(ChunkPos.Zero, 1).ToHashSet(), harness.MeshedPositions.ToHashSet());

        // The margin did real work — it generated — it just never meshed.
        StreamedChunk margin = harness.Pipeline.Streamer.Get(new ChunkPos(3, 0, 0))!;
        Assert.True(margin.HasVoxels);
        Assert.Equal(ChunkState.Generated, margin.State);
        Assert.DoesNotContain(new ChunkPos(3, 0, 0), harness.MeshedPositions);
    }

    [Fact]
    public void EverythingIsReleasedWhenTheLastViewerLeaves()
    {
        using var harness = new PipelineHarness();
        ViewerId id = harness.Pipeline.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
        harness.Settle();

        Assert.True(harness.Pipeline.Streamer.TrackedCount > 0);

        harness.Pipeline.Streamer.RemoveViewer(id);
        harness.Settle();

        Assert.Equal(0, harness.Pipeline.Streamer.ViewedCount);

        // The one that matters: no chunk records survive their last viewer. A gap here that
        // does not close is a job that never reported.
        Assert.Equal(0, harness.Pipeline.Streamer.TrackedCount);

        // Every mesh that was handed over was handed back.
        Assert.Equal(harness.MeshesDelivered, harness.MeshesDiscarded);
    }

    /// <summary>
    /// The named test from docs/M2-PLAN.md. Leaving and returning while the load is still in
    /// flight must not lose the chunk, duplicate the load, or orphan a job.
    /// </summary>
    [Fact]
    public void LeavingAndReturningMidLoadLosesNothing()
    {
        using var harness = new PipelineHarness();
        ChunkStreamer streamer = harness.Pipeline.Streamer;

        ViewerId id = streamer.AddViewer(ChunkPos.Zero, 1, 1);

        // One pump submits the first wave, then the viewer changes its mind before any of it
        // has had a chance to land.
        harness.Pipeline.Update();
        Assert.True(harness.Pipeline.JobsInFlight > 0);

        streamer.MoveViewer(id, new ChunkPos(100, 100, 100));
        harness.Pipeline.Update();
        streamer.MoveViewer(id, ChunkPos.Zero);

        harness.Settle();

        Assert.Equal(0, harness.Pipeline.FaultedJobs);
        foreach (ChunkPos position in Box(ChunkPos.Zero, 1))
        {
            Assert.Equal(ChunkState.Meshed, streamer.Get(position)!.State);
        }

        // No orphans: the far-away chunks the viewer briefly wanted are all gone.
        Assert.Equal(streamer.ViewedCount, streamer.TrackedCount);
    }

    /// <summary>
    /// Running somewhere and turning around must <em>stop</em> the work, not merely stop
    /// caring about it. Without cancellation the queue never drains under continuous
    /// movement.
    /// </summary>
    [Fact]
    public void MovingAwayCancelsWorkRatherThanLettingItRun()
    {
        using var harness = new PipelineHarness();
        ChunkStreamer streamer = harness.Pipeline.Streamer;

        ViewerId id = streamer.AddViewer(ChunkPos.Zero, 2, 2);
        harness.Pipeline.Update();

        // Teleport far enough that not one chunk of the old box is still wanted.
        streamer.MoveViewer(id, new ChunkPos(500, 0, 0));
        harness.Settle();

        Assert.True(
            harness.Pipeline.DroppedJobs > 0,
            "work queued for the abandoned region should have been cancelled, not completed");
        Assert.Equal(streamer.ViewedCount, streamer.TrackedCount);
    }

    [Fact]
    public void ARevivedChunkIsReusedRatherThanDuplicated()
    {
        using var harness = new PipelineHarness();
        ChunkStreamer streamer = harness.Pipeline.Streamer;

        ViewerId a = streamer.AddViewer(ChunkPos.Zero, 0, 0);
        harness.Pipeline.Update();

        StreamedChunk before = streamer.Get(ChunkPos.Zero)!;

        // A second viewer arrives while the first one's work is still in flight, then the
        // first leaves. The chunk is wanted throughout and must never be rebuilt.
        streamer.AddViewer(ChunkPos.Zero, 0, 0);
        streamer.RemoveViewer(a);
        harness.Settle();

        Assert.Same(before, streamer.Get(ChunkPos.Zero));
        Assert.Equal(ChunkState.Meshed, before.State);
    }

    /// <summary>
    /// The fuzzable invariant from docs/M2-PLAN.md, in its strongest form: after any sequence
    /// of viewer movement, <b>no chunk is left waiting on a job that will never complete</b>,
    /// and the tracked set has collapsed back onto the viewed set.
    /// </summary>
    /// <remarks>
    /// The move sequence deliberately interleaves <see cref="ChunkPipeline.Update"/> calls
    /// with viewer changes at every phase of a chunk's life — mid-submit, mid-flight,
    /// mid-apply — because a state-machine bug here is a bug about ordering, and a test that
    /// only moves between quiet moments will never see one.
    /// </remarks>
    [Fact]
    public void NoChunkIsLeftWaitingOnAJobThatWillNeverComplete()
    {
        var random = new Random(2026_08_27);
        using var harness = new PipelineHarness();
        ChunkStreamer streamer = harness.Pipeline.Streamer;

        ViewerId id = streamer.AddViewer(ChunkPos.Zero, 1, 1);

        for (int step = 0; step < 60; step++)
        {
            streamer.MoveViewer(id, new ChunkPos(
                random.Next(-3, 4), random.Next(-2, 3), random.Next(-3, 4)));

            // Pump a random number of times, sometimes zero, so moves land at every phase.
            for (int pump = random.Next(0, 3); pump > 0; pump--)
            {
                harness.Pipeline.Update();
            }
        }

        harness.Settle();

        Assert.Equal(0, harness.Pipeline.FaultedJobs);
        Assert.Equal(0, harness.Pipeline.JobsInFlight);

        // Proof the run was not trivial. Measured at this seed: 394 completed against 3,693
        // dropped — under movement this fast, nine jobs in ten are cancelled before they run,
        // which is cancellation doing its job rather than a fault. A real player crossing one
        // boundary at a time turns over a 49-chunk shell, not a 343-chunk box.
        Assert.True(
            harness.Pipeline.DroppedJobs > harness.Pipeline.CompletedJobs,
            "the fuzz should be cancelling far more work than it completes");

        // Nothing retained: every job reported, and every chunk that lost its last viewer was
        // released once it did.
        Assert.Equal(streamer.ViewedCount, streamer.TrackedCount);
        Assert.All(streamer.Chunks, c => Assert.Equal(0, c.OutstandingJobs));

        // And the work actually finished rather than merely stopping.
        ChunkPos centre = streamer.GetViewer(id).Position;
        foreach (ChunkPos position in Box(centre, 1))
        {
            Assert.Equal(ChunkState.Meshed, streamer.Get(position)!.State);
        }

        // Mesh handover balances: every mesh delivered and not since discarded is a chunk
        // that is meshed right now.
        int live = streamer.Chunks.Count(c => c.State == ChunkState.Meshed);
        Assert.Equal(live, harness.MeshesDelivered - harness.MeshesDiscarded);
    }

    [Fact]
    public void ApplyingResultsStopsAtTheFrameBudget()
    {
        // A clock that advances a millisecond every time it is read, so "5 ms of budget"
        // means exactly five results and the assertion is arithmetic rather than a race
        // against a real one.
        long millisecondTicks = Stopwatch.Frequency / 1000;
        long now = 0;

        using var harness = new PipelineHarness(
            resultBudget: TimeSpan.FromMilliseconds(5),
            nowTicks: () => now += millisecondTicks);

        harness.Pipeline.Streamer.AddViewer(ChunkPos.Zero, 2, 2);
        harness.Pipeline.Update();
        harness.WaitForPendingResults(20);

        int before = harness.Pipeline.AppliedResults;
        harness.Pipeline.Update();

        Assert.Equal(5, harness.Pipeline.AppliedResults - before);
        Assert.True(harness.Pipeline.PendingResults > 0, "the rest should still be waiting, not lost");
    }

    [Fact]
    public void TheLightStepsTwoHalvesAreContainedInItsWhole()
    {
        // M5d needed light timed directly — M5-PLAN says so, and the headless frame-time trap
        // has been walked into four times — and a stopwatch is exactly the kind of addition that
        // nothing would ever fail on. What is pinned here is not a duration: it is that the
        // three brackets enclose the regions they claim to. Deriving and propagating are
        // disjoint, both sit inside the step, and what neither covers is the bookkeeping after
        // the drain.
        //
        // A clock that advances a millisecond per read, the same seam ApplyingResultsStopsAtThe-
        // FrameBudget uses, so the arithmetic is arithmetic rather than a race against a real
        // one. Monotonic is all the invariant needs, which is why it holds under a fake clock
        // and under a real one alike.
        //
        // BREAK IT BY: timing the drain from `entered` instead of from `derived`, which
        // double-counts the derive and makes the halves exceed the whole; or by swapping
        // `derived` and `drained`, which makes one half negative.
        long millisecondTicks = Stopwatch.Frequency / 1000;
        long now = 0;

        using var harness = new PipelineHarness(nowTicks: () => now += millisecondTicks);
        harness.Pipeline.Streamer.AddViewer(ChunkPos.Zero, 1, 1);

        double totalDerive = 0.0;
        var clock = Stopwatch.StartNew();

        while (!harness.Pipeline.IsQuiescent && clock.ElapsedMilliseconds < 60_000)
        {
            double beforeDerive = harness.Pipeline.LightDeriveMillis;
            double beforePropagate = harness.Pipeline.LightPropagateMillis;

            harness.Pipeline.Update();

            double derive = harness.Pipeline.LightDeriveMillis - beforeDerive;
            double propagate = harness.Pipeline.LightPropagateMillis - beforePropagate;

            Assert.True(derive >= 0.0, $"deriving went backwards by {-derive} ms");
            Assert.True(propagate >= 0.0, $"propagating went backwards by {-propagate} ms");
            Assert.True(
                derive + propagate <= harness.Pipeline.LastLightStepMillis,
                $"{derive} + {propagate} ms of halves exceeds a "
                + $"{harness.Pipeline.LastLightStepMillis} ms step");

            totalDerive += derive;
            Thread.Sleep(1);
        }

        // The companion assertion, because every one above is inside a loop and a loop that
        // never ran passes all of them. This is the third outing for that rule in M5.
        Assert.True(
            harness.Pipeline.LightBuilds > 0,
            "no light was derived, so every assertion above passed vacuously");
        Assert.True(totalDerive > 0.0, "deriving cost nothing at all, which cannot be right");
    }

    [Fact]
    public void AFrameAlwaysAppliesAtLeastOneResult()
    {
        // Budget zero, and a clock already overrun by the time it is read a second time.
        // Checking the deadline *after* applying rather than before is what stops a result
        // costlier than the whole budget from being deferred forever — and the queue behind
        // it with it, which would be a livelock that looks like the world quietly stopping.
        long now = 0;
        using var harness = new PipelineHarness(
            resultBudget: TimeSpan.Zero,
            nowTicks: () => now += Stopwatch.Frequency);

        harness.Pipeline.Streamer.AddViewer(ChunkPos.Zero, 2, 2);
        harness.Pipeline.Update();
        harness.WaitForPendingResults(10);

        int before = harness.Pipeline.AppliedResults;
        harness.Pipeline.Update();

        Assert.Equal(1, harness.Pipeline.AppliedResults - before);
    }

    [Fact]
    public void TheTightestPossibleBudgetStillReachesTheSameWorld()
    {
        // One result per frame — the most restrictive budget there is — and the pipeline must
        // still converge on exactly the world an unbudgeted one produces. Deferring is not
        // dropping: a result that never lands leaves its chunk flagged work-in-progress
        // forever, and nothing else would ever notice.
        using var harness = new PipelineHarness(resultBudget: TimeSpan.Zero);
        harness.Pipeline.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
        harness.Settle();

        Assert.Equal(Box(ChunkPos.Zero, 1).ToHashSet(), harness.MeshedPositions.ToHashSet());
        Assert.Equal(0, harness.Pipeline.PendingResults);
        Assert.Equal(0, harness.Pipeline.JobsInFlight);
        Assert.Equal(0, harness.Pipeline.FaultedJobs);
    }

    [Fact]
    public void AnEditIsWrittenAndTheChunkIsRemeshed()
    {
        using var harness = new PipelineHarness();
        harness.Pipeline.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
        harness.Settle();

        // Somewhere genuinely buried — see FindSolidInterior for why that word had to start
        // being enforced rather than asserted in a comment.
        VoxelPos target = FindSolidInterior(harness, ChunkPos.Zero);
        int before = harness.Deliveries[ChunkPos.Zero];
        int indicesBefore = harness.LastMesh[ChunkPos.Zero].Opaque.IndexCount;

        Assert.True(harness.Pipeline.SetVoxel(target, MaterialIds.AirNumericId));
        harness.Settle();

        (int lx, int ly, int lz) = target.Local;
        Assert.Equal(
            MaterialIds.AirNumericId,
            harness.Pipeline.Streamer.Get(ChunkPos.Zero)!.Voxels!.Get(lx, ly, lz));

        Assert.Equal(before + 1, harness.Deliveries[ChunkPos.Zero]);
        Assert.True(
            harness.LastMesh[ChunkPos.Zero].Opaque.IndexCount > indicesBefore,
            "carving a cavity out of solid rock should add faces to the mesh");
        Assert.True(harness.Pipeline.Streamer.Get(ChunkPos.Zero)!.HasEdits);
    }

    [Fact]
    public void EditingAChunkHandedToAJobLeavesThatJobsSnapshotAlone()
    {
        // Copy-on-write, at the level the pipeline is responsible for: once a mesh job has
        // captured the reference, the instance it captured stops changing and a fresh one
        // takes its place.
        using var harness = new PipelineHarness();
        harness.Pipeline.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
        harness.Settle();

        StreamedChunk chunk = harness.Pipeline.Streamer.Get(ChunkPos.Zero)!;
        VoxelChunk handedOut = chunk.Voxels!;
        Assert.True(chunk.VoxelsSharedWithJob, "meshing should have marked the voxels shared");

        VoxelPos target = FindSolidInterior(harness, ChunkPos.Zero);
        (int lx, int ly, int lz) = target.Local;
        int original = handedOut.Get(lx, ly, lz);

        harness.Pipeline.SetVoxel(target, MaterialIds.AirNumericId);

        Assert.NotSame(handedOut, chunk.Voxels);
        Assert.Equal(original, handedOut.Get(lx, ly, lz));
        Assert.Equal(MaterialIds.AirNumericId, chunk.Voxels!.Get(lx, ly, lz));
        Assert.Equal(1, harness.Pipeline.ChunkClones);
    }

    [Fact]
    public void ManyEditsInOneFrameCostOneCloneAndOneRemesh()
    {
        // Coalescing, and the reason the dirty set is drained in Update rather than acted on
        // per write: a carve writes hundreds of voxels, and remeshing per write would rebuild
        // work the next write invalidates.
        using var harness = new PipelineHarness();
        harness.Pipeline.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
        harness.Settle();

        int before = harness.Deliveries[ChunkPos.Zero];

        // Interior only, so this stays one chunk's business and the remesh count is exact.
        VoxelChunk voxels = harness.Pipeline.Streamer.Get(ChunkPos.Zero)!.Voxels!;
        var targets = new List<VoxelPos>();

        for (int ly = 1; ly < VoxelConstants.ChunkSize - 1 && targets.Count < 200; ly++)
        for (int lz = 1; lz < VoxelConstants.ChunkSize - 1 && targets.Count < 200; lz++)
        for (int lx = 1; lx < VoxelConstants.ChunkSize - 1 && targets.Count < 200; lx++)
        {
            if (voxels.Get(lx, ly, lz) != MaterialIds.AirNumericId)
            {
                targets.Add(VoxelPos.FromChunkLocal(ChunkPos.Zero, lx, ly, lz));
            }
        }

        Assert.True(targets.Count > 20, "the test needs a decent number of real edits to be meaningful");

        foreach (VoxelPos target in targets)
        {
            Assert.True(harness.Pipeline.SetVoxel(target, MaterialIds.AirNumericId));
        }
        Assert.Equal(1, harness.Pipeline.ChunkClones);

        harness.Settle();
        Assert.Equal(before + 1, harness.Deliveries[ChunkPos.Zero]);
    }

    [Fact]
    public void AnEditOnAChunkBoundaryRemeshesTheNeighbourToo()
    {
        // The border rule arriving through the pipeline rather than in isolation. Without it
        // the neighbour keeps drawing a face against material that is no longer there.
        using var harness = new PipelineHarness();
        harness.Pipeline.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
        harness.Settle();

        var owner = ChunkPos.Zero;
        var neighbour = new ChunkPos(-1, 0, 0);
        VoxelPos target = FindSolidOnFace(harness, owner);

        int ownerBefore = harness.Deliveries[owner];
        int neighbourBefore = harness.Deliveries[neighbour];

        Assert.True(harness.Pipeline.SetVoxel(target, MaterialIds.AirNumericId));
        harness.Settle();

        Assert.Equal(ownerBefore + 1, harness.Deliveries[owner]);
        Assert.Equal(neighbourBefore + 1, harness.Deliveries[neighbour]);
    }

    [Fact]
    public void AnEditRemeshOutranksStreamingWork()
    {
        // Band3, which M2a reserved and nothing has used until now. Same chunk position, so
        // the distance band is identical and only the override separates them.
        var viewers = new ViewerSet();
        viewers.UpdateSingle(ChunkPos.Zero);

        var neighbourhood = new VoxelChunk?[PaddedChunk.NeighbourhoodSize];
        var materials = MaterialTable.ForTesting(
            [false, true], [false, false], [0, 1], hardness: [0f, 0f]);

        JobPriority streaming = JobPriority.Evaluate(
            JobKind.Mesh,
            ChunkPos.Zero,
            viewers,
            new MeshChunkJob(ChunkPos.Zero, neighbourhood, materials).Urgency);

        JobPriority edit = JobPriority.Evaluate(
            JobKind.Mesh,
            new ChunkPos(40, 0, 0),
            viewers,
            new MeshChunkJob(new ChunkPos(40, 0, 0), neighbourhood, materials, urgent: true).Urgency);

        // The edit is 40 chunks further away and still wins, which is the whole point.
        Assert.True(edit > streaming);
        Assert.Equal(0, streaming.Band3);
        Assert.Equal(MeshChunkJob.EditUrgency, edit.Band3);
    }

    [Fact]
    public void EditingAChunkThatDoesNotExistReportsFailureRatherThanDroppingTheWrite()
    {
        // A carve reaching past the loaded region has to be told it was clipped. Returning
        // void here would make the difference between "mined" and "silently ignored"
        // invisible to the caller.
        using var harness = new PipelineHarness();
        harness.Pipeline.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
        harness.Settle();

        VoxelPos far = VoxelPos.FromChunkLocal(new ChunkPos(500, 0, 500), 1, 1, 1);

        Assert.False(harness.Pipeline.SetVoxel(far, MaterialIds.AirNumericId));
        Assert.Equal(0, harness.Pipeline.EditedVoxels);
    }

    [Fact]
    public void WritingTheMaterialThatIsAlreadyThereChangesNothing()
    {
        // A sphere carve rewrites air as air across most of its bounding box. Treating those
        // as edits would clone a chunk and dirty its neighbours for no reason at all.
        using var harness = new PipelineHarness();
        harness.Pipeline.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
        harness.Settle();

        VoxelPos target = FindSolidInterior(harness, ChunkPos.Zero);
        (int lx, int ly, int lz) = target.Local;
        int existing = harness.Pipeline.Streamer.Get(ChunkPos.Zero)!.Voxels!.Get(lx, ly, lz);

        Assert.True(harness.Pipeline.SetVoxel(target, existing));

        Assert.Equal(0, harness.Pipeline.EditedVoxels);
        Assert.Equal(0, harness.Pipeline.ChunkClones);
        Assert.False(harness.Pipeline.Streamer.Get(ChunkPos.Zero)!.HasEdits);
    }

    /// <summary>A solid voxel away from every face, so an edit stays one chunk's business.</summary>
    /// <summary>
    /// A solid voxel whose six neighbours are all solid too, so carving it opens a cavity.
    /// </summary>
    /// <remarks>
    /// <b>"Interior" used to mean "not on the chunk boundary", and the caller's comment
    /// claimed it meant buried.</b> The first solid voxel in scan order sits at a
    /// terrain-or-cave boundary and typically has air on three sides, so carving it removes
    /// three of its own faces and exposes three on its neighbours — a change of exactly zero
    /// quads. The face-count assertion passed anyway until M5a's merge key shifted how the
    /// surrounding quads merged and the two numbers came out equal. Nothing was wrong with the
    /// pipeline; the fixture had never set up the situation its own comment described.
    /// </remarks>
    private static VoxelPos FindSolidInterior(PipelineHarness harness, ChunkPos chunk)
    {
        VoxelChunk voxels = harness.Pipeline.Streamer.Get(chunk)!.Voxels!;

        for (int ly = 1; ly < VoxelConstants.ChunkSize - 1; ly++)
        for (int lz = 1; lz < VoxelConstants.ChunkSize - 1; lz++)
        for (int lx = 1; lx < VoxelConstants.ChunkSize - 1; lx++)
        {
            if (Buried(voxels, lx, ly, lz))
            {
                return VoxelPos.FromChunkLocal(chunk, lx, ly, lz);
            }
        }

        throw new InvalidOperationException($"{chunk} has no buried voxel to carve.");
    }

    /// <summary>Solid, and solid on all six sides. Every material here but air is opaque.</summary>
    private static bool Buried(VoxelChunk voxels, int x, int y, int z) =>
        voxels.Get(x, y, z) != MaterialIds.AirNumericId
        && voxels.Get(x - 1, y, z) != MaterialIds.AirNumericId
        && voxels.Get(x + 1, y, z) != MaterialIds.AirNumericId
        && voxels.Get(x, y - 1, z) != MaterialIds.AirNumericId
        && voxels.Get(x, y + 1, z) != MaterialIds.AirNumericId
        && voxels.Get(x, y, z - 1) != MaterialIds.AirNumericId
        && voxels.Get(x, y, z + 1) != MaterialIds.AirNumericId;

    /// <summary>A solid voxel on the −X face, which belongs to the neighbour's mesh too.</summary>
    private static VoxelPos FindSolidOnFace(PipelineHarness harness, ChunkPos chunk)
    {
        VoxelChunk voxels = harness.Pipeline.Streamer.Get(chunk)!.Voxels!;

        for (int ly = 0; ly < VoxelConstants.ChunkSize; ly++)
        for (int lz = 0; lz < VoxelConstants.ChunkSize; lz++)
        {
            if (voxels.Get(0, ly, lz) != MaterialIds.AirNumericId)
            {
                return VoxelPos.FromChunkLocal(chunk, 0, ly, lz);
            }
        }

        throw new InvalidOperationException($"{chunk} has no solid voxel on its −X face.");
    }

    private static IEnumerable<ChunkPos> Box(ChunkPos centre, int radius)
    {
        var positions = new List<ChunkPos>();
        ChunkBox.Around(centre, radius, radius).ForEach(positions.Add);
        return positions;
    }
}

/// <summary>
/// A pipeline wired to a real scheduler, with worldgen tuned for test speed rather than for
/// looking like terrain.
/// </summary>
internal sealed class PipelineHarness : IDisposable
{
    private const int Grass = 1;
    private const int Dirt = 2;
    private const int Stone = 3;

    private readonly JobScheduler _scheduler;

    /// <summary>
    /// A light slice wide enough that this suite never pays for it.
    /// </summary>
    /// <remarks>
    /// <b>The production default is 2 ms and these tests are about the state machine, not the
    /// budget.</b> Deriving a chunk's light is ~0.27 ms in Release and several times that in the
    /// Debug build the suite runs, and <see cref="Settle"/> sleeps a millisecond per pass — so a
    /// realistic slice turns every pipeline test into a few hundred extra round trips. Measured:
    /// the whole suite went 8 s → 83 s at 1 ms, and back again with this. A test that actually
    /// wants a starved light budget passes its own.
    /// </remarks>
    public static readonly TimeSpan GenerousLightBudget = TimeSpan.FromMilliseconds(500);

    public PipelineHarness(
        int loadMargin = ChunkStreamer.DefaultLoadMargin,
        TimeSpan? resultBudget = null,
        Func<long>? nowTicks = null,
        TimeSpan? lightBudget = null)
    {
        var viewers = new ViewerSet();
        _scheduler = new JobScheduler(viewers, threadCount: 4, sweepInterval: TimeSpan.FromMilliseconds(5));

        Pipeline = new ChunkPipeline(
            Generator(),
            Materials(),
            _scheduler,
            viewers,
            onMeshReady: (chunk, mesh) =>
            {
                MeshesDelivered++;
                MeshedPositions.Add(chunk.Position);
                Deliveries[chunk.Position] = Deliveries.GetValueOrDefault(chunk.Position) + 1;
                LastMesh[chunk.Position] = mesh;
            },
            onMeshDiscarded: position =>
            {
                MeshesDiscarded++;
                MeshedPositions.Remove(position);
            },
            onJobFaulted: (position, error) => Faults.Add($"{position}: {error}"),
            loadMargin: loadMargin,
            resultBudget: resultBudget,
            nowTicks: nowTicks,
            lightBudget: lightBudget ?? GenerousLightBudget);
    }

    public ChunkPipeline Pipeline { get; }

    public int MeshesDelivered { get; private set; }

    public int MeshesDiscarded { get; private set; }

    /// <summary>Positions currently holding a delivered mesh, from the renderer's point of view.</summary>
    public HashSet<ChunkPos> MeshedPositions { get; } = [];

    /// <summary>How many times each chunk has been meshed. Remeshes show up here.</summary>
    public Dictionary<ChunkPos, int> Deliveries { get; } = [];

    /// <summary>The most recent mesh handed over for each chunk.</summary>
    public Dictionary<ChunkPos, ChunkMesh> LastMesh { get; } = [];

    public List<string> Faults { get; } = [];

    /// <summary>
    /// Pumps until nothing is in flight and nothing is waiting, which is the only moment the
    /// pipeline's state is worth asserting on.
    /// </summary>
    /// <remarks>
    /// A chunk can neither be pushed nor completed once both are zero, so this is genuine
    /// quiescence rather than a snapshot. The timeout exists to fail loudly instead of
    /// hanging the suite if the work list ever stops converging.
    /// </remarks>
    /// <summary>
    /// Blocks until at least <paramref name="atLeast"/> finished jobs are queued but not yet
    /// applied, so a budget test has something to be budgeted.
    /// </summary>
    public void WaitForPendingResults(int atLeast, int timeoutMillis = 30_000)
    {
        var clock = Stopwatch.StartNew();

        while (Pipeline.PendingResults < atLeast)
        {
            if (clock.ElapsedMilliseconds > timeoutMillis)
            {
                throw new TimeoutException(
                    $"only {Pipeline.PendingResults} results arrived, wanted {atLeast}");
            }

            Thread.Sleep(1);
        }
    }

    public void Settle(int timeoutMillis = 60_000)
    {
        var clock = Stopwatch.StartNew();

        while (true)
        {
            Pipeline.Update();
            if (Pipeline.IsQuiescent)
            {
                Assert.Empty(Faults);
                return;
            }

            if (clock.ElapsedMilliseconds > timeoutMillis)
            {
                throw new TimeoutException(
                    $"Pipeline did not settle: {Pipeline.JobsInFlight} in flight, " +
                    $"{Pipeline.WorkListCount} queued, {Pipeline.FaultedJobs} faulted, " +
                    $"light {(Pipeline.LightInFlight ? "in flight" : "settled")} " +
                    $"with {Pipeline.LightBuildBacklog} derives queued.");
            }

            Thread.Sleep(1);
        }
    }

    /// <summary>
    /// Caves off and a single octave. Caves are 80% of generation cost, and this suite cares
    /// about the state machine rather than about what the terrain looks like.
    /// </summary>
    private static WorldGenerator Generator() => new(
        new WorldGenSettings
        {
            Seed = 0x5EEDUL,
            SurfaceOctaves = 1,
            CavesEnabled = false,
        },
        new StrataTable([new StrataLayer(Grass, 1), new StrataLayer(Dirt, 3)], Stone));

    /// <summary>An opaque, full-strength emitter. Nothing generates it; tests place it.</summary>
    public const int Lamp = 4;

    /// <summary>
    /// The base game's own hardnesses, so a carve driven through this harness gates the same
    /// way the shipped content does.
    /// </summary>
    public static MaterialTable Materials() => MaterialTable.ForTesting(
        //             air    grass dirt  stone lamp
        isOpaque: [false, true, true, true, true],
        isFluid: [false, false, false, false, false],
        textureLayer: [0, 1, 2, 3, 4],
        hardness: [0f, 0.9f, 0.8f, 3.0f, 0.5f],
        lightEmission: [0, 0, 0, 0, 15]);

    public void Dispose()
    {
        Pipeline.Dispose();
        _scheduler.Dispose();
    }
}
