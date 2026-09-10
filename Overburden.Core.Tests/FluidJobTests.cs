using Overburden.Core.Fluid;
using Overburden.Core.Jobs;
using Overburden.Core.Persistence;
using Overburden.Core.Streaming;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// The water thread, slice 3b: the tick as a job. The batch is composed and completed on the sim
/// thread and a worker runs the visits in between; the worker reads a captured view of the field.
/// </summary>
/// <remarks>
/// <para>
/// <b>Break-recipes filed in the plan, each with the test that catches it</b>: the job path
/// visiting differently from the sync path —
/// <see cref="AJobRunTickEqualsASimThreadTickCellForCell"/>; a cancel that keeps or loses the
/// unvisited half — <see cref="ACancelledJobReturnsItsUnvisitedCellsAndConserves"/> and
/// <see cref="ACancelBeforeTheFirstVisitReturnsTheWholeBatch"/>; a read allowed through while a
/// worker owns the grid — <see cref="ReadsRefuseWhileAJobIsInFlight"/>; the view falling through
/// or answering loaded for a chunk it does not cover —
/// <see cref="ACapturedViewRefusesWhatItDoesNotCoverAndIsNotTornByAnEdit"/>; the arrival gate
/// under-approximating with a worker on the grid —
/// <see cref="MayHoldWaterOverApproximatesWhileAJobIsInFlight"/>; the pipeline treating a detached
/// job as a chunk's — <see cref="ADetachedJobIsRoutedToItsOwnResultAndTouchesNoChunk"/> and
/// <see cref="AFaultedDetachedJobIsReportedAndStillCollected"/>; and the whole thing on a real
/// worker through the real pipeline, with the box moving under it —
/// <see cref="AJobTickOnARealWorkerThroughThePipelineKeepsTheIdentity"/>.
/// </para>
/// </remarks>
public class FluidJobTests
{
    private const int Cap = FluidConstants.CellCapacityMillilitres;

    private static FluidSimulation Pour(IFluidTerrain terrain)
    {
        var sim = new FluidSimulation(terrain);
        for (int x = 4; x <= 19; x++)
        {
            for (int z = 4; z <= 19; z++)
            {
                sim.Add(new VoxelPos(x, 20, z), Cap);
            }
        }

        return sim;
    }

    /// <summary>One tick through the job path on the calling thread: begin, run, collect.</summary>
    private static FluidTickJob TickThroughJob(FluidSimulation sim, IFluidTerrain captured, Action? midTick = null)
    {
        FluidTickJob job = sim.BeginTick(captured, ChunkPos.Zero);
        midTick?.Invoke();
        job.Run(CancellationToken.None);
        job.OnResult(JobOutcome.Completed, null);
        return job;
    }

    /// <summary>Answers for another terrain and cancels a token on its Nth question, so a cancel lands mid-tick deterministically.</summary>
    private sealed class CancellingTerrain(IFluidTerrain inner, int after, CancellationTokenSource cts) : IFluidTerrain
    {
        private int _asked;

        public bool CanHold(VoxelPos v)
        {
            if (++_asked == after) cts.Cancel();
            return inner.CanHold(v);
        }

        public bool IsLoaded(VoxelPos v) => inner.IsLoaded(v);
    }

    private static Dictionary<VoxelPos, int> Cells(FluidSimulation sim)
    {
        var cells = new Dictionary<VoxelPos, int>();
        foreach (VoxelPos cell in sim.Grid.HeldCells()) cells[cell] = sim.Grid.AmountAt(cell);
        return cells;
    }

    private static void AssertSameWater(FluidSimulation a, FluidSimulation b, string when)
    {
        Dictionary<VoxelPos, int> x = Cells(a);
        Dictionary<VoxelPos, int> y = Cells(b);
        Assert.True(
            x.Count == y.Count && x.All(kv => y.GetValueOrDefault(kv.Key) == kv.Value),
            $"{when}: {x.Count} cells against {y.Count}, or an amount differs");
        Assert.Equal(a.TotalMillilitres, b.TotalMillilitres);
        Assert.Equal(a.ActiveCellCount, b.ActiveCellCount);
        Assert.Equal(a.CellVisits, b.CellVisits);
    }

    // ---------------------------------------------------------------- the oracle

    [Fact]
    public void AJobRunTickEqualsASimThreadTickCellForCell()
    {
        // Two simulations over the same box with the same pour: one ticks through the job path on
        // this thread, the other through Step(). The same disturbances land at the same boundaries
        // on both — one of them mid-tick, deferred on both arms — and after every tick the grids
        // are equal cell for cell, with the same visits paid.
        var terrain = new BoxTerrain(0, 0, 0, 23, 23, 23);
        FluidSimulation viaJob = Pour(terrain);
        FluidSimulation viaStep = Pour(terrain);

        for (int tick = 0; tick < 40; tick++)
        {
            // Disturbances aimed at a cell that holds water at that moment — the first version
            // aimed at fixed voxels the falling sheet had not reached, woke nothing on either arm,
            // and could not tell a collection that drops the deferred list from one that drains it.
            if (tick == 5)
            {
                VoxelPos held = viaStep.Grid.HeldCells().First();
                long before = viaStep.DisturbedCells;
                viaJob.Disturb(held);
                viaStep.Disturb(held);
                Assert.True(viaStep.DisturbedCells > before, "the disturbance woke nothing");
            }

            if (tick == 12)
            {
                // Mid-tick on both: after the batch is composed, before the visits.
                VoxelPos held = viaStep.Grid.HeldCells().First();
                long before = viaJob.DisturbedCells;
                TickThroughJob(viaJob, terrain, midTick: () => viaJob.Disturb(held));
                Assert.True(viaJob.DisturbedCells > before, "the deferred disturbance woke nothing at collection");
                Assert.Null(viaStep.Resume(1));
                viaStep.Disturb(held);
                viaStep.Step();
            }
            else
            {
                FluidTickJob job = TickThroughJob(viaJob, terrain);
                Assert.Equal(JobOutcome.Completed, job.Outcome);
                Assert.NotNull(job.Result);
                FluidTickResult stepped = viaStep.Step();
                Assert.Equal(stepped, job.Result.Value);
            }

            AssertSameWater(viaJob, viaStep, $"after tick {tick}");
            Assert.Equal(viaStep.Ticks, viaJob.Ticks);
            Assert.False(viaJob.JobInFlight);
        }

        Assert.Equal(40L, viaJob.JobTicks);
        Assert.Equal(0L, viaJob.JobTicksNotCompleted);
        FluidAssert.SettlesAndRests(viaJob);
        FluidAssert.SettlesAndRests(viaStep);
        AssertSameWater(viaJob, viaStep, "at rest");
    }

    [Fact]
    public void APublishedSnapshotIsTheBoundarysNumbersAndTheIdentityIsExactInIt()
    {
        var terrain = new BoxTerrain(0, 0, 0, 23, 23, 23);
        FluidSimulation sim = Pour(terrain);
        sim.Publish();
        FluidPublished p = sim.Published;
        Assert.Equal(sim.TotalMillilitres, p.TotalMillilitres);
        Assert.Equal(sim.Grid.HeldCellCount, p.HeldCells);
        Assert.Equal(sim.AwakeHeldCells().Count(), p.AwakeHeld);
        Assert.True(p.IdentityExact);

        // Taken again at collection: the tick's water moved, the snapshot moved with it.
        long before = p.Ticks;
        TickThroughJob(sim, terrain);
        Assert.Equal(before + 1, sim.Published.Ticks);
        Assert.Equal(sim.Grid.HeldCellCount, sim.Published.HeldCells);
        Assert.True(sim.Published.IdentityExact);
    }

    // ---------------------------------------------------------------- cancellation

    [Fact]
    public void ACancelledJobReturnsItsUnvisitedCellsAndConserves()
    {
        // A real worker: the job runs on a task, is cancelled once it has visited something, and
        // is collected as Dropped. Whatever the cursor had not reached is awake again, nothing is
        // lost, and the water still comes to a real rest.
        var terrain = new BoxTerrain(0, 0, 0, 23, 23, 23);
        FluidSimulation sim = Pour(terrain);
        sim.Step();
        sim.Step();
        long total = sim.TotalMillilitres;

        // The cancel comes from inside the run — the terrain the worker reads cancels the token on
        // its 300th question — because a token cancelled from outside races a tick that takes well
        // under a millisecond, and the first version of this test lost that race.
        using var cts = new CancellationTokenSource();
        var cancelling = new CancellingTerrain(terrain, after: 300, cts);
        FluidTickJob job = sim.BeginTick(cancelling, ChunkPos.Zero);
        int batch = job.Batch;
        Assert.True(batch > 4 * FluidSimulation.VisitsPerDeadlineCheck, $"a batch of {batch} cannot be cancelled mid-way");
        long visitsBefore = sim.CellVisits;

        var run = Task.Run(() => job.Run(cts.Token));
        var caught = Assert.ThrowsAny<OperationCanceledException>(() => run.GetAwaiter().GetResult());
        Assert.NotNull(caught);

        long visited = sim.CellVisits - visitsBefore;
        Assert.True(visited > 0, "the job was cancelled before it visited anything");
        Assert.True(sim.RemainingInTick > 0, "the job finished before the cancel landed, so nothing was left to return");
        int remaining = sim.RemainingInTick;

        job.OnResult(JobOutcome.Dropped, null);

        Assert.False(sim.JobInFlight);
        Assert.False(sim.TickInProgress);
        Assert.Equal(1L, sim.JobTicksNotCompleted);
        Assert.True(job.Requeued > 0 && job.Requeued <= remaining, $"requeued {job.Requeued} of {remaining}");
        Assert.True(sim.ActiveCellCount >= job.Requeued);
        Assert.Equal(total, sim.TotalMillilitres);
        FluidAssert.Conserves(sim);

        FluidAssert.SettlesAndRests(sim);
        Assert.Equal(total, sim.TotalMillilitres);
    }

    [Fact]
    public void ACancelBeforeTheFirstVisitReturnsTheWholeBatch()
    {
        var terrain = new BoxTerrain(0, 0, 0, 23, 23, 23);
        FluidSimulation sim = Pour(terrain);
        sim.Step();

        FluidTickJob job = sim.BeginTick(terrain, ChunkPos.Zero);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => job.Run(cts.Token));
        Assert.Equal(job.Batch, sim.RemainingInTick);

        job.OnResult(JobOutcome.Dropped, null);
        Assert.Equal(job.Batch, job.Requeued);
        Assert.Equal(job.Batch, sim.ActiveCellCount);
        FluidAssert.Conserves(sim);
    }

    // ---------------------------------------------------------------- the refusals

    [Fact]
    public void ReadsRefuseWhileAJobIsInFlight()
    {
        var terrain = new BoxTerrain(0, 0, 0, 23, 23, 23);
        FluidSimulation sim = Pour(terrain);
        sim.Step();

        FluidTickJob job = sim.BeginTick(terrain, ChunkPos.Zero);
        Assert.True(sim.JobInFlight);
        Assert.True(sim.GridOwnedByTick);
        Assert.Same(job, sim.Job);

        Assert.Throws<InvalidOperationException>(() => sim.Grid);
        Assert.Throws<InvalidOperationException>(() => sim.AwakeHeldCells().Count());
        Assert.Throws<InvalidOperationException>(() => sim.Rested);
        Assert.Throws<InvalidOperationException>(() => sim.Resume(1));
        Assert.Throws<InvalidOperationException>(() => sim.Step());
        Assert.Throws<InvalidOperationException>(() => sim.ReachableChunks([]));
        Assert.Throws<InvalidOperationException>(() => sim.LevelSheets());
        Assert.Throws<InvalidOperationException>(() => sim.TakeAllDirty([]));
        Assert.Throws<InvalidOperationException>(() => sim.Publish());
        Assert.Throws<InvalidOperationException>(() => sim.BeginTick(terrain, ChunkPos.Zero));
        Assert.Throws<InvalidOperationException>(() => sim.Add(new VoxelPos(1, 22, 1), Cap));

        // A move lands at the next begin rather than under the worker.
        sim.SetNearField(new ChunkPos(3, 0, 3), 2);
        Assert.Equal(0, sim.NearRings);

        job.Run(CancellationToken.None);
        job.OnResult(JobOutcome.Completed, null);
        Assert.False(sim.JobInFlight);
        Assert.Equal(0, sim.NearRings);
        FluidTickJob next = sim.BeginTick(terrain, ChunkPos.Zero);
        Assert.Equal(2, sim.NearRings);
        Assert.Equal(new ChunkPos(3, 0, 3), sim.NearCentre);
        next.Run(CancellationToken.None);
        next.OnResult(JobOutcome.Completed, null);

        _ = sim.Grid;
        _ = sim.AwakeHeldCells().Count();
        _ = sim.Rested;
        sim.Publish();
        sim.Step();
    }

    /// <summary>Counts the questions it is asked and answers for another terrain.</summary>
    private sealed class CountingTerrain(IFluidTerrain inner) : IFluidTerrain
    {
        public int Asked;

        public bool CanHold(VoxelPos v)
        {
            Asked++;
            return inner.CanHold(v);
        }

        public bool IsLoaded(VoxelPos v)
        {
            Asked++;
            return inner.IsLoaded(v);
        }
    }

    [Fact]
    public void TheWorkerReadsTheCapturedTerrainAndNotTheLiveOne()
    {
        // The simulation is built over one counting terrain and the job begun with another over
        // the same box: the visits ask the captured one and never the live one, and after the
        // collection a boundary call asks the live one again.
        var box = new BoxTerrain(0, 0, 0, 23, 23, 23);
        var live = new CountingTerrain(box);
        var captured = new CountingTerrain(box);
        FluidSimulation sim = Pour(live);
        sim.Step();

        int liveBefore = live.Asked;
        FluidTickJob job = sim.BeginTick(captured, ChunkPos.Zero);
        job.Run(CancellationToken.None);
        Assert.True(captured.Asked > 0, "the worker asked the captured terrain nothing");
        Assert.Equal(liveBefore, live.Asked);

        job.OnResult(JobOutcome.Completed, null);
        int capturedAfter = captured.Asked;
        sim.Add(new VoxelPos(2, 22, 2), Cap);
        Assert.True(live.Asked > liveBefore, "the boundary asked the live terrain nothing");
        Assert.Equal(capturedAfter, captured.Asked);
    }

    [Fact]
    public void AJobBegunWithNothingAwakeStillOwnsTheGrid()
    {
        // The fuzz's job arm found this on its first run: with nothing pending the batch is empty,
        // TickInProgress is false for the job's whole life, and a second job could begin under the
        // first. Ownership is the batch or the job.
        var terrain = new BoxTerrain(0, 0, 0, 7, 7, 7);
        var sim = new FluidSimulation(terrain);
        Assert.Equal(0, sim.ActiveCellCount);

        FluidTickJob job = sim.BeginTick(terrain, ChunkPos.Zero);
        Assert.Equal(0, job.Batch);
        Assert.False(sim.TickInProgress);
        Assert.True(sim.GridOwnedByTick);
        Assert.Throws<InvalidOperationException>(() => sim.BeginTick(terrain, ChunkPos.Zero));
        Assert.Throws<InvalidOperationException>(() => sim.Grid);
        Assert.Throws<InvalidOperationException>(() => sim.Add(new VoxelPos(1, 1, 1), Cap));

        job.Run(CancellationToken.None);
        job.OnResult(JobOutcome.Completed, null);
        Assert.False(sim.GridOwnedByTick);
        Assert.Equal(1L, sim.Ticks);
    }

    // ---------------------------------------------------------------- the captured view

    [Fact]
    public void ACapturedViewRefusesWhatItDoesNotCoverAndIsNotTornByAnEdit()
    {
        // The box runs three chunks; the field covers two, so the third is holdable to the source
        // and unknown to the view — which is what separates a refusal from the truth.
        var inner = new LoadableTerrain(new BoxTerrain(0, 0, 0, 95, 15, 15));
        var field = new FluidField(inner);
        ChunkPos west = new(0, 0, 0), east = new(1, 0, 0), beyond = new(2, 0, 0);
        field.Sync([west, east]);
        Assert.Equal(2, field.Chunks);

        FluidFieldView view = field.Capture();
        Assert.Equal(2, view.Chunks);
        Assert.True(view.Covers(west) && view.Covers(east) && !view.Covers(beyond));

        // Covered: the view's answer is the field's, voxel for voxel, and it costs no refusal.
        for (int x = 0; x < 64; x += 3)
        {
            for (int y = 0; y < 32; y += 5)
            {
                var v = new VoxelPos(x, y, 7);
                Assert.Equal(field.CanHold(v), view.CanHold(v));
                Assert.True(view.IsLoaded(v));
            }
        }

        Assert.Equal(0L, view.Refusals);

        // Not covered: a refusal on both questions, counted, and never the source's answer.
        var outside = new VoxelPos(70, 5, 5);
        Assert.True(inner.CanHold(outside), "the fixture's outside voxel is not holdable, so the refusal cannot be told from the truth");
        Assert.False(view.CanHold(outside));
        Assert.False(view.IsLoaded(outside));
        Assert.Equal(1L, view.Refusals);

        // An edit after the capture reaches the live field and not the view.
        var dug = new VoxelPos(40, 20, 5);
        Assert.False(field.CanHold(dug));
        Assert.True(inner.Dig(dug));
        field.Invalidate(dug);
        Assert.True(field.CanHold(dug));
        Assert.False(view.CanHold(dug));
        Assert.Equal(1L, view.Refusals);
    }

    [Fact]
    public void MayHoldWaterOverApproximatesWhileAJobIsInFlight()
    {
        // With a worker on the grid the question is answered from the panels at submission and
        // the captured set: a wet chunk yes, a captured dry chunk yes (a worker may create a panel
        // there, and it is loaded already), an uncaptured dry chunk no. After the collection the
        // captured dry chunk answers no again.
        var inner = new LoadableTerrain(new BoxTerrain(0, 0, 0, 95, 15, 15));
        var field = new FluidField(inner);
        ChunkPos wet = new(0, 0, 0), captured = new(1, 0, 0), elsewhere = new(2, 0, 0);
        field.Sync([wet, captured]);

        var sim = new FluidSimulation(field);
        sim.Add(new VoxelPos(5, 10, 5), Cap);
        sim.Step();

        Assert.True(sim.MayHoldWater(wet));
        Assert.False(sim.MayHoldWater(captured));
        Assert.False(sim.MayHoldWater(elsewhere));

        FluidTickJob job = sim.BeginTick(field.Capture(), ChunkPos.Zero);
        Assert.True(sim.MayHoldWater(wet));
        Assert.True(sim.MayHoldWater(captured));
        Assert.False(sim.MayHoldWater(elsewhere));

        job.Run(CancellationToken.None);
        job.OnResult(JobOutcome.Completed, null);
        Assert.True(sim.MayHoldWater(wet));
        Assert.False(sim.MayHoldWater(captured));
        Assert.False(sim.MayHoldWater(elsewhere));

        // A view that does not cover the wet chunk: the published panels answer for it, which is
        // the clause the captured set cannot stand in for.
        field.Sync([captured]);
        FluidTickJob again = sim.BeginTick(field.Capture(), ChunkPos.Zero);
        Assert.True(sim.MayHoldWater(wet));
        Assert.True(sim.MayHoldWater(captured));
        again.Run(CancellationToken.None);
        again.OnResult(JobOutcome.Completed, null);
    }

    // ---------------------------------------------------------------- the pipeline's seam

    private sealed class CountingJob(ChunkPos position, Action? body = null) : IDetachedJob
    {
        public JobKind Kind => JobKind.FluidTick;

        public ChunkPos Position => position;

        public int Runs;

        public List<(JobOutcome Outcome, Exception? Error)> Results { get; } = [];

        public void Run(CancellationToken cancellation)
        {
            Interlocked.Increment(ref Runs);
            body?.Invoke();
        }

        public void OnResult(JobOutcome outcome, Exception? error) => Results.Add((outcome, error));
    }

    [Fact]
    public void ADetachedJobIsRoutedToItsOwnResultAndTouchesNoChunk()
    {
        using var harness = new PipelineHarness();
        harness.Pipeline.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
        harness.Settle();

        // Named after a tracked chunk, which is exactly what the chunk-centric path would have
        // decremented OutstandingJobs on.
        StreamedChunk chunk = harness.Pipeline.Streamer.Get(ChunkPos.Zero)!;
        int outstanding = chunk.OutstandingJobs;
        int applied = harness.Pipeline.AppliedResults;

        var job = new CountingJob(ChunkPos.Zero);
        harness.Pipeline.SubmitDetached(job);
        Assert.Equal(1, harness.Pipeline.DetachedJobsInFlight);
        Assert.False(harness.Pipeline.IsQuiescent);

        harness.Settle();

        Assert.Equal(1, job.Runs);
        (JobOutcome outcome, Exception? error) = Assert.Single(job.Results);
        Assert.Equal(JobOutcome.Completed, outcome);
        Assert.Null(error);
        Assert.Equal(0, harness.Pipeline.DetachedJobsInFlight);
        Assert.Equal(0, harness.Pipeline.JobsInFlight);
        Assert.Equal(outstanding, chunk.OutstandingJobs);
        Assert.Equal(applied + 1, harness.Pipeline.AppliedResults);
        Assert.Empty(harness.Faults);
    }

    [Fact]
    public void AFaultedDetachedJobIsReportedAndStillCollected()
    {
        using var harness = new PipelineHarness();
        harness.Pipeline.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
        harness.Settle();

        var job = new CountingJob(ChunkPos.Zero, () => throw new InvalidOperationException("the tick broke"));
        harness.Pipeline.SubmitDetached(job);
        Assert.True(harness.Pipeline.WaitForDetached(timeoutSeconds: 10));

        (JobOutcome outcome, Exception? error) = Assert.Single(job.Results);
        Assert.Equal(JobOutcome.Faulted, outcome);
        Assert.Contains("the tick broke", error!.Message);
        Assert.Single(harness.Faults);
        Assert.Equal(1, harness.Pipeline.FaultedJobs);
        Assert.Equal(0, harness.Pipeline.JobsInFlight);
    }

    [Fact]
    public void WaitForDetachedReturnsOnceACancelledJobHasReported()
    {
        using var harness = new PipelineHarness();
        harness.Pipeline.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
        harness.Settle();

        using var gate = new ManualResetEventSlim(false);
        using var cts = new CancellationTokenSource();
        var job = new CountingJob(ChunkPos.Zero, () =>
        {
            gate.Wait();
            cts.Token.ThrowIfCancellationRequested();
        });

        harness.Pipeline.SubmitDetached(job, cts.Token);
        cts.Cancel();
        gate.Set();
        Assert.True(harness.Pipeline.WaitForDetached(timeoutSeconds: 10));

        (JobOutcome outcome, _) = Assert.Single(job.Results);
        Assert.Equal(JobOutcome.Dropped, outcome);
        Assert.Equal(0, harness.Pipeline.DetachedJobsInFlight);
    }

    // ---------------------------------------------------------------- a real worker, the real pipeline

    [Fact]
    public void AJobTickOnARealWorkerThroughThePipelineKeepsTheIdentity()
    {
        // The game's wiring: the automaton over the pipeline's streamer, a field over that, ticks
        // begun at boundaries and run by a scheduler worker, collected by the pipeline's own result
        // pass — and the box walked away and back while a tick is out, so the parking meets a real
        // worker. Every identity exact, nothing displaced, and the water at a real rest at the end.
        var store = new InMemoryWorldStore();
        using var session = new FluidEvictionTests.Session(store);
        ViewerId viewer = session.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
        session.Harness.Settle();
        long held = session.PourAtOrigin();
        held += session.PourAtOrigin(dx: 32) - held;

        var field = new FluidField(new VoxelFluidTerrain(session.Streamer, FluidEvictionTests.Materials()));
        var reachable = new HashSet<ChunkPos>();
        int parkedSeen = 0;
        using var gate = new ManualResetEventSlim(true);
        session.Sim.WorkerGate = gate;

        for (int tick = 0; tick < 60; tick++)
        {
            session.Sim.ReachableChunks(reachable);
            field.Sync(reachable);
            bool hold = tick is 10 or 11;
            if (hold) gate.Reset();
            FluidTickJob job = session.Sim.BeginTick(field.Capture(), ChunkPos.Zero);
            session.Pipeline.SubmitDetached(job);

            if (tick == 10)
            {
                // The worker is held at its gate, so the whole box leaves under a tick that is
                // really out: every forget's eviction is deferred behind a real job.
                session.Streamer.MoveViewer(viewer, FluidEvictionTests.FarAway);
                session.PumpUntil(() => !session.Streamer.IsTracked(FluidEvictionTests.Wet), pumpTicks: false);
                Assert.True(session.Sim.DeferredCount > 0, "nothing was deferred behind the worker");
            }

            if (tick == 11)
            {
                // And comes back under the next one: the wet chunks park behind a real job.
                session.Streamer.MoveViewer(viewer, ChunkPos.Zero);
                session.PumpUntil(() => session.Pipeline.FluidArrivalsWaiting > 0, pumpTicks: false);
                parkedSeen = session.Pipeline.FluidArrivalsWaiting;
                Assert.True(session.Sim.JobInFlight);
            }

            if (hold) gate.Set();
            session.PumpUntil(() => job.Outcome is not null, pumpTicks: false);
            Assert.Equal(JobOutcome.Completed, job.Outcome);
            Assert.False(session.Sim.JobInFlight);
            Assert.Null(job.Surface);
            Assert.True(session.Sim.Published.IdentityExact, $"identity broken at tick {tick}");
            Assert.Equal(0L, session.Sim.DisplacedMillilitres);
        }

        // One more with the surface walked on the worker: the cells it hands back are the grid's
        // own surface as it stands at collection.
        session.Sim.ReachableChunks(reachable);
        field.Sync(reachable);
        FluidTickJob walked = session.Sim.BeginTick(field.Capture(), ChunkPos.Zero, walkSurface: true);
        session.Pipeline.SubmitDetached(walked);
        session.PumpUntil(() => walked.Outcome is not null, pumpTicks: false);
        Assert.NotNull(walked.Surface);
        Assert.Equal(session.Sim.Grid.SurfaceCells().ToHashSet(), walked.Surface.ToHashSet());
        Assert.True(walked.Surface.Count > 0, "the walk found no surface over the pour");

        session.Harness.Settle();
        Assert.Equal(61L, session.Sim.JobTicks);
        Assert.Equal(0L, session.Sim.JobTicksNotCompleted);
        Assert.Equal(0L, session.Sim.TickRefusals);
        Assert.True(parkedSeen > 0, "no arrival was parked while a real worker held the grid");
        Assert.True(session.Pipeline.FluidArrivalsParked > 0);
        Assert.Equal(0, session.Pipeline.FluidArrivalsWaiting);
        Assert.Equal(held, session.Sim.TotalMillilitres);
        FluidAssert.Conserves(session.Sim);
        FluidAssert.SettlesAndRests(session.Sim);
        Assert.Equal(0, session.Sim.PausedCellCount);
    }
}
