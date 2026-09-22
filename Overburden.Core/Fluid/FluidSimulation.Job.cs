using System.Diagnostics;
using Overburden.Core.Jobs;
using Overburden.Core.Voxels;

namespace Overburden.Core.Fluid;

/// <summary>
/// The tick as a scheduler job (the water thread, slice 3b): the batch is composed and completed
/// on the sim thread, and a worker runs the visits in between.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing changes what sets <see cref="GridOwnedByTick"/>.</b> <see cref="TickInProgress"/>
/// is <i>batch composed and not yet cleared</i>; <see cref="BeginTick"/> composes it at submission
/// and <see cref="CollectTick"/> clears it at collection, both on the sim thread, so the flag 3a
/// built spans the job's whole life and every rule 3a exercised synchronously — mutations queue,
/// reads refuse, the pipeline parks a wet arrival — holds unchanged with a worker in the middle.
/// The worker runs the visit loop and the surface mesh and nothing else: not the composition, not
/// the deferred list, not the near field, not a boundary pass.
/// </para>
/// <para>
/// <b>The worker reads a captured terrain, and the boundary keeps the live one.</b> The visit path
/// — <c>Visit</c>, <c>ReadSides</c>, <c>Displace</c> — asks <see cref="_visitTerrain"/>, which a
/// job sets to the view it was begun with (a <see cref="FluidFieldView"/> in the game, in which an
/// unknown chunk is a refusal); every other site — <see cref="Add"/>, the sheet solve,
/// <see cref="ChunkArrived"/>'s face wake, <see cref="Wake"/> — asks the live terrain, because it
/// runs at a boundary on the sim thread. On the synchronous path the two are the same object.
/// </para>
/// <para>
/// <b>What the worker writes and who reads it.</b> Cell amounts and panel creation in the grid,
/// the cursor, the rested list, the paused set, the wake queue, the inflow stamps and the counters.
/// All of them are read on the sim thread at a boundary today, with three exceptions the pass
/// named: the report line and the HUD now read <see cref="Published"/>, a snapshot taken at every
/// collection and refreshed by the caller after its boundary passes; <see cref="Grid"/>,
/// <see cref="AwakeHeldCells"/>, <see cref="Rested"/>, <see cref="Resume"/> and the sheet solve
/// refuse while a job is in flight; and <see cref="MayHoldWater"/> reads the published panel set
/// and the captured set instead of the dictionary the worker is writing — an over-approximation,
/// because a worker can create a panel only inside the captured set, and a chunk in that set is
/// loaded already and therefore not arriving.
/// </para>
/// <para>
/// <b>A cancelled or faulted tick gives its unvisited cells back.</b> <c>Complete</c> clears the
/// batch; a tick that stopped at the cursor would otherwise leave every awake cell past it asleep
/// and unvisited — a stranded front on every quit. Collection wakes them into the pending set
/// first, so the row a quit writes lists them awake, exact.
/// </para>
/// </remarks>
public sealed partial class FluidSimulation
{
    // The terrain the visit path asks. The live terrain until a job captures one; see the class
    // remarks. Assigned in the constructor beside _terrain.
    private IFluidTerrain _visitTerrain = null!;

    private FluidTickJob? _job;
    private FluidFieldView? _capturedView;
    private HashSet<ChunkPos> _publishedPanels = [];
    private (ChunkPos Centre, int Rings)? _nearFieldPending;
    private List<(int X, int Z)>? _nearColumnsPending;

    // M9d, decision 4: the wet bitsets as the last Publish copied them, read by HoldsWater on the
    // sim thread whether or not a worker owns the grid. One path: never the grid.
    private readonly Dictionary<ChunkPos, ulong[]> _publishedWet = [];

    /// <summary>Panels whose wet bitset <see cref="Publish"/> has copied out, over the run.</summary>
    public long WetPanelsPublished { get; private set; }

    /// <summary>Chunks with a published wet copy — the footprint, 4 KiB each.</summary>
    public int PublishedWetPanels => _publishedWet.Count;

    /// <summary>
    /// Whether the voxel held water at the last <see cref="Publish"/> — the fill's refusal
    /// (<c>IVoxelEditor.HoldsWater</c>), answered mid-tick without touching the grid.
    /// </summary>
    /// <remarks>
    /// Stale by at most one tick, and in both directions: a cell that went wet during the tick in
    /// flight is not refused and its water is displaced at the next visit, counted; a cell that
    /// dried is refused until the next publish. A keypress cannot wait for a boundary and a fill
    /// is a keypress, so the copy is the answer rather than a deferral.
    /// </remarks>
    public bool HoldsWater(VoxelPos voxel) => FluidGrid.IsWetIn(_publishedWet, voxel);

    /// <summary>True while a worker owns the visit loop: between <see cref="BeginTick"/> and <see cref="CollectTick"/>.</summary>
    public bool JobInFlight => _job is not null;

    /// <summary>The job in flight, or null.</summary>
    public FluidTickJob? Job => _job;

    /// <summary>Ticks run through a job, over the lifetime.</summary>
    public long JobTicks { get; private set; }

    /// <summary>Job ticks that did not complete — cancelled at a quit, or faulted — and gave their cells back.</summary>
    public long JobTicksNotCompleted { get; private set; }

    /// <summary>Cells returned to the queue by ticks that did not complete.</summary>
    public long CellsRequeued { get; private set; }

    /// <summary>
    /// Questions a worker asked of a chunk its captured view did not cover, answered as refusals.
    /// Most are the live terrain's own answer — a chunk that is not loaded cannot hold — and cost
    /// nothing; <see cref="TickRefusalsOfArrivedChunks"/> is the part that did.
    /// </summary>
    public long TickRefusals { get; private set; }

    /// <summary>
    /// Of the chunks refused during a tick, how many the live terrain answered <i>loaded</i> for
    /// at collection: chunks that arrived mid-tick, whose cells the sync arm would have moved a
    /// boundary earlier. <b>The number that measures decision 7's cost</b>; the played world's
    /// open was the case where the plain count read as an alarm and was not one.
    /// </summary>
    public long TickRefusalsOfArrivedChunks { get; private set; }

    /// <summary>The boundary's numbers, for the report line and the HUD. See <see cref="Publish"/>.</summary>
    public FluidPublished Published { get; private set; }

    /// <summary>
    /// The mine's share of <c>held</c>, measured at every <see cref="Publish"/> when set — M10d-1's
    /// "dry" oracle. Null publishes zero and the line says nothing.
    /// </summary>
    public UndergroundWater? Underground { get; set; }

    /// <summary>
    /// Composes the next tick's batch and hands it to a job. At a boundary only; the caller submits
    /// the job and collects it through <see cref="FluidTickJob.OnResult"/>.
    /// </summary>
    /// <param name="captured">
    /// The terrain the worker reads — a <see cref="FluidFieldView"/> in the game. Never the live
    /// streamer, which the worker cannot read; a test on one thread may pass its own terrain.
    /// </param>
    /// <param name="position">Where the player is, for the queue's distance band.</param>
    /// <param name="walkSurface">Whether the worker walks the surface after the visits.</param>
    public FluidTickJob BeginTick(IFluidTerrain captured, ChunkPos position, bool walkSurface = false, (int Share, int QuietTicks)? settle = null)
    {
        ArgumentNullException.ThrowIfNull(captured);
        if (GridOwnedByTick)
        {
            throw new InvalidOperationException(
                $"BeginTick asked with a tick in progress, {RemainingInTick} cell(s) still to visit"
                + (_job is not null ? " and a job out" : string.Empty)
                + "; a job begins at a boundary and nowhere else.");
        }

        ApplyPendingNearField();
        Begin();

        // The panels as they stand when the worker starts: with the captured set, the whole of
        // what MayHoldWater may answer with until collection.
        _publishedPanels = [.. _grid.PanelChunks()];

        _visitTerrain = captured;

        // The view's own counters, through M8d-5's wrapper when the lakes are on.
        _capturedView = (captured as LakeBoundedTerrain)?.Inner as FluidFieldView ?? captured as FluidFieldView;
        _job = new FluidTickJob(this, position, walkSurface, _batch.Count, settle);
        return _job;
    }

    /// <summary>
    /// The grid and the awake set without the refusal. Tests only, on one thread, with a job begun
    /// and deliberately not run: the fuzz inspects the world between a slice and its finish, which
    /// on the sync arm is a mid-tick read and on the job arm is a read under a held job that no
    /// worker is touching. Never call with a worker running.
    /// </summary>
    internal FluidGrid GridUnchecked => _grid;

    internal IEnumerable<VoxelPos> AwakeHeldCellsUnchecked()
    {
        FluidTickJob? job = _job;
        _job = null;
        try
        {
            foreach (VoxelPos cell in AwakeHeldCells()) yield return cell;
        }
        finally
        {
            _job = job;
        }
    }

    /// <summary>A gate the worker waits at before its first visit. Tests only: it is how a box is moved under a tick that is really out.</summary>
    internal ManualResetEventSlim? WorkerGate { get; set; }

    /// <summary>The worker's half: every remaining visit in granules, polling cancellation between them, then the walk.</summary>
    internal void RunOnWorker(FluidTickJob job, CancellationToken cancellation)
    {
        WorkerGate?.Wait(cancellation);
        long started = Stopwatch.GetTimestamp();
        while (_cursor < _batch.Count)
        {
            cancellation.ThrowIfCancellationRequested();
            Advance(VisitsPerDeadlineCheck);
        }

        long visited = Stopwatch.GetTimestamp();
        job.VisitMillis = (visited - started) * 1000.0 / Stopwatch.Frequency;

        // The settle's solve, where the boundary would have run it (slice 3c): after every visit
        // and before the hand-off reads what it levelled as resting. Only with something awake,
        // which is SheetSettle.AtBoundary's own test.
        if (job.SettleWith is { } settle && _pending.Count + FarBacklog > 0)
        {
            job.Settle = SolveOnWorker(settle.Share, settle.QuietTicks);
            long settled = Stopwatch.GetTimestamp();
            job.SettleMillis = (settled - visited) * 1000.0 / Stopwatch.Frequency;
            visited = settled;
        }

        if (job.WalkSurface)
        {
            // M8d-1: the mesh, not the cells — the arrays are built here so the main thread pays
            // only the install; and since 2026-09-21 only the panels that moved, so it pays for
            // those. The captured view answers the mesher's questions about dry neighbours,
            // IsLoaded first, so it counts no refusal for them.
            job.Surface = _surfaceMesher.Update(_grid, _visitTerrain);
            job.WalkMillis = (Stopwatch.GetTimestamp() - visited) * 1000.0 / Stopwatch.Frequency;
        }
    }

    private readonly FluidSurfaceMesher _surfaceMesher = new();

    /// <summary>The mesher both arms build the surface with, for its <see cref="FluidSurfaceMesher.DebugView"/> switch.</summary>
    public FluidSurfaceMesher SurfaceMesher => _surfaceMesher;

    /// <summary>
    /// The surface as geometry, built here and now (the sim-thread arm; the job arm builds it on
    /// the worker and hands it back as <see cref="FluidTickJob.Surface"/>): the panels that moved
    /// since the last build, and the ones that emptied. Refuses while a job owns the grid. One
    /// mesher serves both arms, so the previous-height attribute is continuous across a build
    /// wherever it ran.
    /// </summary>
    public FluidSurfaceUpdate BuildSurface()
    {
        RefuseMidJob(nameof(BuildSurface));
        return _surfaceMesher.Update(_grid, _visitTerrain);
    }

    /// <summary>
    /// Takes the job's result on the sim thread: gives back what a tick that did not complete left
    /// unvisited, completes the tick — which drains the deferred list — and publishes.
    /// </summary>
    internal FluidTickResult CollectTick(FluidTickJob job, JobOutcome outcome)
    {
        if (!ReferenceEquals(job, _job))
        {
            throw new InvalidOperationException("CollectTick given a job that is not the one in flight.");
        }

        _job = null;
        _visitTerrain = _terrain;
        if (_capturedView is not null)
        {
            TickRefusals += _capturedView.Refusals;
            foreach (ChunkPos refused in _capturedView.RefusedChunks)
            {
                if (_terrain.IsLoaded(refused.OriginVoxel))
                {
                    TickRefusalsOfArrivedChunks++;
                }
            }

            _capturedView = null;
        }

        JobTicks++;
        if (outcome != JobOutcome.Completed)
        {
            // Wake refuses a cell that neither holds nor can hold, which is the one kind the batch
            // may contain that has nothing left to do; everything else goes back awake.
            int requeued = 0;
            for (int i = _cursor; i < _batch.Count; i++)
            {
                if (Wake(_batch[i]))
                {
                    requeued++;
                }
            }

            job.Requeued = requeued;
            CellsRequeued += requeued;
            JobTicksNotCompleted++;
        }

        FluidTickResult result = Complete();
        Publish();
        return result;
    }

    /// <summary>
    /// Snapshots the numbers the report line and the HUD read. Taken at every collection; call
    /// again after the boundary passes so the pour and the settle are in it.
    /// </summary>
    /// <param name="exactAwake">
    /// Walk the paused and queued sets for the held-only, deduplicated count — 14.9 ms at 150,000
    /// active on the play, which is why the periodic call does not. False counts queued cells held
    /// or not, plus paused, without the walk: the number a report wants, and the one a quit's
    /// "awake or paused" line is not — that line asks for the walk once.
    /// </param>
    public void Publish(bool exactAwake = false)
    {
        RefuseMidJob(nameof(Publish));
        WetPanelsPublished += _grid.CollectWet(_publishedWet);

        int awake;
        if (exactAwake)
        {
            awake = 0;
            foreach (VoxelPos _ in AwakeHeldCells())
            {
                awake++;
            }
        }
        else
        {
            awake = _pending.Count + FarBacklog + _pausedCells;
        }

        (long underground, int undergroundPanels) = Underground?.Measure(_grid) ?? (0, 0);
        Published = new FluidPublished(
            Ticks,
            _grid.TotalMillilitres,
            _grid.HeldCellCount,
            _grid.PanelCount,
            _grid.PanelBytes,
            awake,
            PausedCellCount,
            AddedMillilitres,
            LoadedMillilitres,
            RemovedMillilitres,
            DisplacedMillilitres,
            HandedMillilitres,
            EvictedMillilitres,
            LiftedMillilitres,
            ReturnedMillilitres,
            PumpedMillilitres,
            EmittedMillilitres,
            underground,
            undergroundPanels);
    }

    /// <summary>Refuses a read of state a worker is writing.</summary>
    private void RefuseMidJob(string what)
    {
        if (_job is not null)
        {
            throw new InvalidOperationException(
                $"{what} asked with a tick job in flight, {RemainingInTick} cell(s) still to visit; "
                + "a worker owns the grid until the job is collected. Read Published, or wait for the boundary.");
        }
    }

    private void ApplyPendingNearField()
    {
        if (_nearFieldPending is { } pending)
        {
            _nearFieldPending = null;
            SetNearField(pending.Centre, pending.Rings);
        }

        if (_nearColumnsPending is { } columns)
        {
            _nearColumnsPending = null;
            SetNearColumns(columns);
        }
    }
}

/// <summary>The boundary's numbers, taken by <see cref="FluidSimulation.Publish"/>.</summary>
/// <remarks>
/// A second copy of six grid numbers and the identity's terms, report-only, because the live ones
/// are a worker's mid-trajectory values while a job runs — and <c>TotalMillilitres</c> against the
/// counters could read as a broken identity across a displacement and its counter. The identity is
/// evaluated here from one consistent snapshot.
/// </remarks>
public readonly record struct FluidPublished(
    long Ticks,
    long TotalMillilitres,
    int HeldCells,
    int Panels,
    long PanelBytes,
    int AwakeHeld,
    int Paused,
    long Added,
    long Loaded,
    long Removed,
    long Displaced,
    long Handed,
    long Evicted,
    long Lifted,
    long Returned,
    long Pumped,
    long Emitted,
    long UndergroundMillilitres = 0,
    int UndergroundPanels = 0)
{
    /// <summary><c>held == injected + drawn + loaded + emitted − removed − displaced − handed − evicted − pumped</c>, from one snapshot.</summary>
    public bool IdentityExact => TotalMillilitres == Added + Loaded + Emitted - Removed - Displaced - Handed - Evicted - Pumped;
}

/// <summary>One tick of the automaton as a job on the scheduler. See <see cref="FluidSimulation.BeginTick"/>.</summary>
public sealed class FluidTickJob : IDetachedJob
{
    private readonly FluidSimulation _sim;

    internal FluidTickJob(FluidSimulation sim, ChunkPos position, bool walkSurface, int batch, (int Share, int QuietTicks)? settle)
    {
        _sim = sim;
        Position = position;
        WalkSurface = walkSurface;
        Batch = batch;
        SettleWith = settle;
    }

    /// <summary>The settle's parameters when the worker is to run the solve at the tail; null on the sim-thread settle.</summary>
    public (int Share, int QuietTicks)? SettleWith { get; }

    /// <summary>What the solve did, when it ran on the worker; null when nothing was awake or the settle was not asked for.</summary>
    public SheetLevelResult? Settle { get; internal set; }

    /// <summary>Wall time of the solve on the worker.</summary>
    public double SettleMillis { get; internal set; }

    /// <inheritdoc />
    public JobKind Kind => JobKind.FluidTick;

    /// <inheritdoc />
    public ChunkPos Position { get; }

    /// <summary>Whether the worker walks the surface after the visits. See <see cref="Surface"/>.</summary>
    public bool WalkSurface { get; }

    /// <summary>Cells in the batch the job was begun with.</summary>
    public int Batch { get; }

    /// <summary>The surface as the worker left the grid, as geometry (M8d-1) — the panels that moved and the ones that emptied — when asked for; the caller installs it.</summary>
    public FluidSurfaceUpdate? Surface { get; internal set; }

    /// <summary>Wall time of the visits on the worker.</summary>
    public double VisitMillis { get; internal set; }

    /// <summary>Wall time of the surface walk on the worker.</summary>
    public double WalkMillis { get; internal set; }

    /// <summary>Cells a tick that did not complete gave back to the queue.</summary>
    public int Requeued { get; internal set; }

    /// <summary>How the scheduler reported the job, once it has.</summary>
    public JobOutcome? Outcome { get; private set; }

    /// <summary>The tick's result, once collected.</summary>
    public FluidTickResult? Result { get; private set; }

    /// <summary>Called on the sim thread after the tick is collected, with the scheduler's outcome and any exception.</summary>
    public Action<FluidTickJob, JobOutcome, Exception?>? Collected { get; set; }

    /// <inheritdoc />
    public void Run(CancellationToken cancellation) => _sim.RunOnWorker(this, cancellation);

    /// <inheritdoc />
    public void OnResult(JobOutcome outcome, Exception? error)
    {
        Outcome = outcome;
        Result = _sim.CollectTick(this, outcome);
        Collected?.Invoke(this, outcome, error);
    }
}
