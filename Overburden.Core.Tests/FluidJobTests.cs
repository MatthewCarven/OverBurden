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
// The simulations here run the one-cell fall (fallCells: 1), the arm every fixture in this file
// was built on: they catch water in mid-fall — at a chunk's face, in a chunk whose row is pending,
// in a chunk that leaves — and M8d-3's rule lands it in one tick. The fall has its own tests
// (FluidFallTests), including the drop that meets an unloaded chunk on the way down.
public class FluidJobTests
{
    private const int Cap = FluidConstants.CellCapacityMillilitres;

    private static FluidSimulation Pour(IFluidTerrain terrain)
    {
        var sim = new FluidSimulation(terrain, fallCells: 1);
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
        sim.Publish(exactAwake: true);
        FluidPublished p = sim.Published;
        Assert.Equal(sim.TotalMillilitres, p.TotalMillilitres);
        Assert.Equal(sim.Grid.HeldCellCount, p.HeldCells);
        Assert.Equal(sim.AwakeHeldCells().Count(), p.AwakeHeld);
        Assert.True(p.IdentityExact);

        // The cheap count is queued-held-or-not plus paused: never under the walk's figure.
        sim.Publish();
        Assert.True(sim.Published.AwakeHeld >= p.AwakeHeld);

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

        // A move lands at the next begin rather than under the worker — and so does a machine
        // column named while the worker is out.
        sim.SetNearField(new ChunkPos(3, 0, 3), 2);
        sim.SetNearColumns([(7, 7)]);
        Assert.Equal(0, sim.NearRings);
        Assert.Equal(0, sim.NearColumnCount);

        job.Run(CancellationToken.None);
        job.OnResult(JobOutcome.Completed, null);
        Assert.False(sim.JobInFlight);
        Assert.Equal(0, sim.NearRings);
        Assert.Equal(0, sim.NearColumnCount);
        FluidTickJob next = sim.BeginTick(terrain, ChunkPos.Zero);
        Assert.Equal(2, sim.NearRings);
        Assert.Equal(new ChunkPos(3, 0, 3), sim.NearCentre);
        Assert.True(sim.IsNearColumn(7, 7));
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

        // At rest on the floor, then one floor cell emptied, so the job's visits move water and
        // leave something awake for the solve at its tail, which then finds the floor's sheet.
        FluidAssert.SettlesAndRests(sim);
        Assert.True(sim.Remove(new VoxelPos(10, 0, 10), Cap) > 0);
        Assert.True(sim.ActiveCellCount > 0);

        int liveBefore = live.Asked;
        FluidTickJob job = sim.BeginTick(captured, ChunkPos.Zero, settle: (8, 0));
        job.Run(CancellationToken.None);
        Assert.True(captured.Asked > 0, "the worker asked the captured terrain nothing");
        Assert.NotNull(job.Settle);
        Assert.True(job.Settle.Value.Sheets > 0, "the worker's solve found no sheet, so its terrain reads went unexercised");
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
        var sim = new FluidSimulation(terrain, fallCells: 1);
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

    // ---------------------------------------------------------------- slice 3c: the skip's levers

    [Fact]
    public void TheSettleOnTheWorkerEqualsTheSettleAtTheBoundaryCellForCell()
    {
        // The twin again, with the settle: the job runs the solve at its tail with the tick number
        // the boundary will have; the sync arm runs it at the boundary after Step(). Same seeds,
        // same quiet count, same water — and the order settle → next tick is the same on both.
        var terrain = new BoxTerrain(0, 0, 0, 23, 23, 23);
        FluidSimulation viaJob = Pour(terrain);
        FluidSimulation viaStep = Pour(terrain);
        var settle = new SheetSettle(viaStep, settleAfterTicks: 2, settleShare: 8);
        int solvedOnWorker = 0;

        for (int tick = 0; tick < 60; tick++)
        {
            FluidTickJob job = viaJob.BeginTick(terrain, ChunkPos.Zero, settle: settle.ForWorker);
            job.Run(CancellationToken.None);
            job.OnResult(JobOutcome.Completed, null);
            if (job.Settle is { } r && r.Sheets > 0) solvedOnWorker++;

            viaStep.Step();
            SheetLevelResult? atBoundary = settle.AtBoundary();

            // The worker solves exactly when the boundary would have — with something awake —
            // and tests quiet against the same tick number the boundary has.
            Assert.Equal(atBoundary is not null, job.Settle is not null);
            if (job.Settle is not null)
            {
                Assert.Equal(viaStep.LastSolveTick, viaJob.LastSolveTick);
                Assert.Equal(viaStep.Ticks, viaJob.LastSolveTick);
            }

            AssertSameWater(viaJob, viaStep, $"after tick {tick}");
        }

        Assert.True(solvedOnWorker > 0, "the worker never found a sheet to solve");
        FluidAssert.SettlesAndRests(viaJob);
        FluidAssert.SettlesAndRests(viaStep);
        AssertSameWater(viaJob, viaStep, "at rest");
    }

    [Fact]
    public void ACappedSyncBakesUpToTheCapAndListsWhatItBakesLate()
    {
        var inner = new LoadableTerrain(new BoxTerrain(0, 0, 0, 191, 15, 15));
        var field = new FluidField(inner);
        ChunkPos[] six = [.. Enumerable.Range(0, 6).Select(x => new ChunkPos(x, 0, 0))];
        inner.Unload(six[5]);
        var late = new List<ChunkPos>();

        // The cap is met before the unloaded chunk is reached, so it is capped rather than
        // declined; either way it is owed.
        field.Sync(six, maxBuilds: 2, late);
        Assert.Equal(2, field.Chunks);
        Assert.Equal(4L, field.Capped);
        Assert.Equal(0L, field.Declined);
        Assert.Equal(4, field.Waiting);
        Assert.Empty(late);

        field.Sync(six, maxBuilds: 2, late);
        Assert.Equal(4, field.Chunks);
        Assert.Equal(2, late.Count);
        Assert.Equal(2, field.Waiting);

        // The declined chunk loads: it is owed too, and comes out on the late list.
        inner.Load(six[5]);
        late.Clear();
        field.Sync(six, maxBuilds: 8, late);
        Assert.Equal(6, field.Chunks);
        Assert.Equal(2, late.Count);
        Assert.Contains(six[5], late);
        Assert.Equal(0, field.Waiting);

        // A decline on its own, under the cap: owed, and late when the world can answer.
        var lone = new FluidField(inner);
        var nine = new ChunkPos(9, 0, 0);
        inner.Unload(nine);
        late.Clear();
        lone.Sync([nine], 8, late);
        Assert.Equal(1L, lone.Declined);
        Assert.Equal(1, lone.Waiting);
        Assert.Empty(late);
        inner.Load(nine);
        lone.Sync([nine], 8, late);
        Assert.Equal([nine], late);
        Assert.Equal(0, lone.Waiting);

        // What was owed and is no longer reachable is no longer owed.
        ChunkPos seven = new(7, 0, 0), eight = new(8, 0, 0);
        field.Sync([six[0], seven, eight], maxBuilds: 1, late);
        Assert.Equal(1, field.Waiting);
        field.Sync([six[0]], maxBuilds: 1, late);
        Assert.Equal(0, field.Waiting);
    }

    [Fact]
    public void AFieldForAChunkThatLeftIsDroppedAtTheSyncSoTheWorkerPausesItsCells()
    {
        // The hole the race battery found: a departed chunk keeps its panel and so stays
        // reachable; its field stayed too, so a worker's view kept moving cells the sync arm
        // pauses, and the eviction released them unlisted. The sync drops the field of a chunk
        // the world no longer answers for, and the next tick pauses its cells as it should.
        var inner = new LoadableTerrain(new BoxTerrain(0, 0, 0, 63, 15, 15));
        var field = new FluidField(inner);
        ChunkPos west = new(0, 0, 0), east = new(1, 0, 0);
        var sim = new FluidSimulation(field, fallCells: 1);
        sim.Add(new VoxelPos(40, 12, 8), Cap);
        field.Sync([west, east]);
        Assert.Equal(2, field.Chunks);

        // An invalidation landing on the departed chunk first — the edit hook's path — drops the
        // field the same way rather than baking "cannot answer" as rock.
        inner.Unload(east);
        field.Invalidate(new VoxelPos(33, 3, 3));
        Assert.Equal(1, field.Chunks);
        Assert.Equal(1L, field.InvalidationsDeclined);
        Assert.Equal(1, field.Waiting);
        inner.Load(east);
        field.Sync([west, east]);
        Assert.Equal(2, field.Chunks);

        inner.Unload(east);
        field.Sync([west, east]);
        Assert.Equal(1, field.Chunks);
        Assert.Equal(1L, field.Departed);
        Assert.Equal(1, field.Waiting);
        Assert.False(field.Capture().Covers(east));

        FluidTickJob job = sim.BeginTick(field.Capture(), ChunkPos.Zero);
        job.Run(CancellationToken.None);
        job.OnResult(JobOutcome.Completed, null);
        Assert.Equal(1, sim.PausedCellCount);
        Assert.Equal(Cap, sim.Grid.AmountAt(new VoxelPos(40, 12, 8)));
        Assert.Equal(0L, sim.TickRefusalsOfArrivedChunks);

        // Back: baked late, woken as an arrival, and the water falls.
        inner.Load(east);
        var late = new List<ChunkPos>();
        field.Sync([west, east], 8, late);
        Assert.Equal([east], late);
        sim.ChunkArrived(east);
        FluidTickJob next = sim.BeginTick(field.Capture(), ChunkPos.Zero);
        next.Run(CancellationToken.None);
        next.OnResult(JobOutcome.Completed, null);
        Assert.Equal(Cap, sim.Grid.AmountAt(new VoxelPos(40, 11, 8)));
    }

    [Fact]
    public void ACellPausedForAnUnbakedChunkWakesWhenTheChunkArrivesToTheWater()
    {
        // The mechanism a late bake relies on: a tick against a view that does not cover the
        // water's chunk refuses it and pauses the cells; ChunkArrived — what Main calls for a
        // late-baked chunk — wakes them, and the next tick moves them.
        var inner = new LoadableTerrain(new BoxTerrain(0, 0, 0, 63, 15, 15));
        var field = new FluidField(inner);
        ChunkPos west = new(0, 0, 0), east = new(1, 0, 0);
        var sim = new FluidSimulation(field, fallCells: 1);
        sim.Add(new VoxelPos(40, 12, 8), Cap);

        // The water's own chunk is reachable and past a cap of one: refused this tick, owed.
        var late = new List<ChunkPos>();
        field.Sync([west, east], 1, late);
        Assert.True(field.Chunks == 1 && field.Waiting == 1);
        FluidTickJob job = sim.BeginTick(field.Capture(), ChunkPos.Zero);
        job.Run(CancellationToken.None);
        job.OnResult(JobOutcome.Completed, null);
        Assert.Equal(1, sim.PausedCellCount);
        Assert.Equal(0, sim.ActiveCellCount);
        Assert.True(sim.TickRefusals > 0);
        sim.Publish();
        Assert.Equal(1, sim.Published.AwakeHeld);

        // Counted as the cost it is: the chunk was loaded all along, only unbaked.
        Assert.Equal(1L, sim.TickRefusalsOfArrivedChunks);
        Assert.Equal(Cap, sim.Grid.AmountAt(new VoxelPos(40, 12, 8)));

        field.Sync([west, east], 8, late);
        Assert.Equal([east], late);
        sim.ChunkArrived(east);
        Assert.Equal(0, sim.PausedCellCount);
        Assert.Equal(1, sim.ActiveCellCount);

        FluidTickJob next = sim.BeginTick(field.Capture(), ChunkPos.Zero);
        next.Run(CancellationToken.None);
        next.OnResult(JobOutcome.Completed, null);
        Assert.Equal(0, sim.Grid.AmountAt(new VoxelPos(40, 12, 8)));
        Assert.Equal(Cap, sim.Grid.AmountAt(new VoxelPos(40, 11, 8)));
        FluidAssert.Conserves(sim);
    }

    // ---------------------------------------------------------------- slice 3c: the race battery

    [Fact]
    public void MutationsLandingUnderARunningWorkerLeaveEveryIdentityExact()
    {
        const bool doMoves = true, doInvalidate = true, doDisturb = true;
        // The battery's stress arm. A pour large enough that a tick runs for milliseconds on a
        // real worker, and while it runs the sim thread does everything the game can do between
        // frames: edits (a disturbance deferred and the live field invalidated under the view),
        // the box moving away and back (evictions deferred, wet arrivals parked), pours and saves
        // that must be refused. The runtime reports no data race, so the oracle is what the design
        // promises: no fault, the captured view untouched by the edits, every identity exact at
        // every collection, nothing displaced, and a real rest with everything back.
        var store = new InMemoryWorldStore();
        using var session = new FluidEvictionTests.Session(store);
        ViewerId viewer = session.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
        session.Harness.Settle();

        long held = 0;
        for (int x = 8; x < 24; x++)
        {
            for (int z = 8; z < 24; z++)
            {
                for (int up = 0; up < 2; up++)
                {
                    var cell = new VoxelPos(x, 44 + up, z);
                    Assert.Equal(0L, session.Sim.Add(cell, Cap));
                    held += Cap;
                }
            }
        }

        var terrain = new VoxelFluidTerrain(session.Streamer, FluidEvictionTests.Materials());
        var field = new FluidField(terrain);
        var reachable = new HashSet<ChunkPos>();
        var random = new Random(0x3C);
        int refusedSaves = 0, refusedPours = 0, disturbances = 0, invalidations = 0, moves = 0, viewChecks = 0;
        long worstVisits = 0;

        var late = new List<ChunkPos>();
        int lateWakes = 0;
        for (int tick = 0; tick < 40; tick++)
        {
            session.Sim.ReachableChunks(reachable);
            late.Clear();
            field.Sync(reachable, 8, late);

            // Main's late wake. Without it this test strands a front: a cell paused for a chunk
            // past the cap has no arrival coming, and "the automaton stopped with moves still
            // available" is what the first run of this test said.
            foreach (ChunkPos chunk in late)
            {
                session.Sim.ChunkArrived(chunk);
                lateWakes++;
            }

            FluidFieldView view = field.Capture();
            FluidTickJob job = session.Sim.BeginTick(view, ChunkPos.Zero);
            worstVisits = Math.Max(worstVisits, job.Batch);
            session.Pipeline.SubmitDetached(job);

            // Everything below lands while the worker may be running.
            var probe = new VoxelPos(random.Next(0, 32), random.Next(32, 64), random.Next(0, 32));
            bool probeBefore = view.CanHold(probe);
            for (int i = 0; i < 6 && job.Outcome is null; i++)
            {
                session.Pipeline.Update();
                var voxel = new VoxelPos(random.Next(0, 32), random.Next(32, 64), random.Next(0, 32));
                if (doDisturb) session.Sim.Disturb(voxel);
                if (doInvalidate) field.Invalidate(voxel);
                disturbances++;
                invalidations++;

                // Only while the worker still owns the grid: the Update above may have collected
                // a short tick, after which a pour and a save are simply allowed.
                if (session.Sim.GridOwnedByTick)
                {
                    Assert.Throws<InvalidOperationException>(() => session.Sim.Add(voxel, Cap));
                    Assert.Throws<InvalidOperationException>(() => session.Pipeline.SaveAll());
                    refusedPours++;
                    refusedSaves++;
                }

                if (doMoves && tick % 10 == 3 && i == 1)
                {
                    session.Streamer.MoveViewer(viewer, FluidEvictionTests.FarAway);
                    session.Pipeline.Update();
                    session.Streamer.MoveViewer(viewer, ChunkPos.Zero);
                    moves++;
                }

                Thread.Yield();
            }

            // The view the worker read never changed under it.
            Assert.Equal(probeBefore, view.CanHold(probe));
            viewChecks++;

            session.PumpUntil(() => job.Outcome is not null, pumpTicks: false);
            Assert.Equal(JobOutcome.Completed, job.Outcome);
            Assert.True(session.Sim.Published.IdentityExact, $"identity broken at tick {tick}");
            Assert.Equal(0L, session.Sim.DisplacedMillilitres);
            Assert.Equal(0, session.Sim.DeferredCount);
        }

        session.Harness.Settle();
        FluidAssert.Conserves(session.Sim);
        session.Sim.Settle();
        if (session.Sim.FirstCellWithAMove() is { } strandedNow)
        {
            VoxelPos below = strandedNow.Cell.Offset(0, -1, 0);
            StreamedChunk? c = session.Streamer.Get(strandedNow.Cell.Chunk);
            Assert.Fail($"stranded: {strandedNow.Cell} — {strandedNow.Why}; chunk tracked {c is not null}, pending {c?.FluidPending}, view {c?.ViewCount}, "
                + $"field says below {field.ClassAt(below)} against terrain {terrain.CanHold(below)}, field chunks {field.Chunks}, waiting {field.Waiting}, declined invalidations {field.InvalidationsDeclined}, "
                + $"refusals {session.Sim.TickRefusals}/{session.Sim.TickRefusalsOfArrivedChunks}");
        }

        Assert.True(disturbances > 0 && invalidations > 0 && refusedPours > 0 && refusedSaves > 0 && viewChecks == 40);
        Assert.True(!doMoves || moves > 0);
        Assert.True(lateWakes > 0, "no chunk was ever baked late, so the cap's wake was not exercised");
        Assert.True(worstVisits > 256, $"the largest batch was {worstVisits}: the worker was not running long enough to race");
        Assert.Equal(40L, session.Sim.JobTicks);
        Assert.Equal(0L, session.Sim.JobTicksNotCompleted);
        Assert.True(held > 0);
        FluidAssert.Conserves(session.Sim);
        FluidAssert.SettlesAndRests(session.Sim);
        Assert.Equal(0, session.Sim.PausedCellCount);
    }

    [Fact]
    public void AQuitMidJobCancelsWaitsWritesAndTheNextOpenHasTheWaterWithTheRequeuedCellsAwake()
    {
        // The game's quit with a worker out: cancel the job, pump until it reports, write — and
        // the rows the next open restores hold the water cell for cell, listing the cells the
        // cancelled tick gave back as awake.
        var store = new InMemoryWorldStore();
        Dictionary<VoxelPos, int> atQuit;
        int awakeAtQuit, requeued;
        long total;
        using (var first = new FluidEvictionTests.Session(store))
        {
            first.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
            first.Harness.Settle();
            for (int x = 12; x < 20; x++)
            {
                for (int z = 12; z < 20; z++)
                {
                    Assert.Equal(0L, first.Sim.Add(new VoxelPos(x, 44, z), Cap));
                }
            }

            first.Sim.Step();
            first.Sim.Step();

            var terrain = new VoxelFluidTerrain(first.Streamer, FluidEvictionTests.Materials());
            var field = new FluidField(terrain);
            var reachable = new HashSet<ChunkPos>();
            first.Sim.ReachableChunks(reachable);
            field.Sync(reachable);

            using var gate = new ManualResetEventSlim(false);
            first.Sim.WorkerGate = gate;
            using var cts = new CancellationTokenSource();
            FluidTickJob job = first.Sim.BeginTick(field.Capture(), ChunkPos.Zero);
            Assert.True(job.Batch > 0);
            first.Pipeline.SubmitDetached(job, cts.Token);
            first.Pipeline.Update();
            Assert.True(first.Sim.GridOwnedByTick);
            Assert.Throws<InvalidOperationException>(() => first.Pipeline.SaveAll());

            // The quit: cancel, let the worker go, wait for its report.
            cts.Cancel();
            gate.Set();
            Assert.True(first.Pipeline.WaitForDetached(timeoutSeconds: 10));
            Assert.Equal(JobOutcome.Dropped, job.Outcome);
            requeued = job.Requeued;
            Assert.Equal(job.Batch, requeued);
            Assert.False(first.Sim.GridOwnedByTick);

            total = first.Sim.TotalMillilitres;
            atQuit = Cells(first.Sim);
            awakeAtQuit = first.Sim.AwakeHeldCells().Count();
            Assert.True(awakeAtQuit > 0, "nothing is listed awake at the quit");

            // Disposing the session is the write.
        }

        Assert.True(store.FluidWrites > 0, "the quit wrote no panel");

        using var second = new FluidEvictionTests.Session(store);
        second.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
        second.Harness.Settle();

        Assert.Equal(total, second.Sim.LoadedMillilitres);
        Dictionary<VoxelPos, int> restored = Cells(second.Sim);
        Assert.True(
            atQuit.Count == restored.Count && atQuit.All(kv => restored.GetValueOrDefault(kv.Key) == kv.Value),
            "the water did not come back cell for cell");
        Assert.Equal(awakeAtQuit, second.Sim.ResumedCells);
        FluidAssert.SettlesAndRests(second.Sim);
        Assert.Equal(0L, second.Sim.DisplacedMillilitres);
        FluidAssert.Conserves(second.Sim);
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

        var sim = new FluidSimulation(field, fallCells: 1);
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

        // One more with the surface meshed on the worker (M8d-1): the geometry it hands back is
        // the grid's own surface as it stands at collection — the same arrays a fresh mesher
        // builds from the grid now, over every one of the walk's surface cells.
        session.Sim.ReachableChunks(reachable);
        field.Sync(reachable);
        FluidTickJob walked = session.Sim.BeginTick(field.Capture(), ChunkPos.Zero, walkSurface: true);
        session.Pipeline.SubmitDetached(walked);
        session.PumpUntil(() => walked.Outcome is not null, pumpTicks: false);
        Assert.NotNull(walked.Surface);
        Assert.Equal(session.Sim.Grid.SurfaceCells().Count(), walked.Surface.SurfaceCells);
        Assert.True(walked.Surface.QuadCount > 0, "the mesh found no surface over the pour");
        FluidSurfaceMesh again = new FluidSurfaceMesher().Build(session.Sim.Grid, field);
        Assert.Equal(again.Positions, walked.Surface.Positions);
        Assert.Equal(again.Indices, walked.Surface.Indices);

        session.Harness.Settle();
        Assert.Equal(61L, session.Sim.JobTicks);
        Assert.Equal(0L, session.Sim.JobTicksNotCompleted);

        // Refusals here are the box leaving under a held job: the departed chunks' fields are
        // dropped at the next sync and the worker pauses their cells. They are back by the
        // collection but still parked — not loaded to the water — so they do not count as
        // arrived; the parking's release wakes them.
        Assert.True(session.Sim.TickRefusals > 0, "the box left under a held job and nothing was refused for it");
        Assert.True(parkedSeen > 0, "no arrival was parked while a real worker held the grid");
        Assert.True(session.Pipeline.FluidArrivalsParked > 0);
        Assert.Equal(0, session.Pipeline.FluidArrivalsWaiting);
        Assert.Equal(held, session.Sim.TotalMillilitres);
        FluidAssert.Conserves(session.Sim);
        FluidAssert.SettlesAndRests(session.Sim);
        Assert.Equal(0, session.Sim.PausedCellCount);
    }
}
