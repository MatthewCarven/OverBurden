using System.Diagnostics;
using Overburden.Core.Persistence;
using Overburden.Core.Voxels;

namespace Overburden.Core.Fluid;

/// <summary>What one tick did.</summary>
/// <param name="CellsVisited">Cells taken off the active set and examined.</param>
/// <param name="CellsMoved">Of those, how many actually shifted water.</param>
/// <param name="MillilitresMoved">Total moved between cells. Not a change in the world's water.</param>
/// <param name="ActiveAfter">Cells queued for the next tick. Zero means quiescent.</param>
public readonly record struct FluidTickResult(
    int CellsVisited, int CellsMoved, long MillilitresMoved, int ActiveAfter);

/// <summary>What running to rest took.</summary>
/// <param name="Ticks">Ticks actually run.</param>
/// <param name="Settled">
/// False when the step limit ran out first — <b>which is a failure, not a timeout</b>. See the
/// remarks on <see cref="FluidSimulation"/>.
/// </param>
/// <param name="PeakActiveCells">The largest active set seen. The number M8a exists to take.</param>
/// <param name="CellVisits">Total cells examined, which is the CPU cost.</param>
public readonly record struct FluidSettleResult(
    int Ticks, bool Settled, int PeakActiveCells, long CellVisits);

/// <summary>
/// Tier B: volume-conserving water that is actually moving, over an explicit active-cell set.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the first state in this project that is a trajectory rather than a value.</b>
/// Worldgen is a function of the seed; light is a fixpoint of the voxels, so from-scratch <i>is</i>
/// the definition; Tier A water is a function of volume and terrain, so pouring in N litres gives
/// the same level however the litres arrived. Where <i>this</i> water is depends on where it came
/// from, and there is no closed form to check it against.
/// </para>
/// <para>
/// <b>So its correctness condition is that it terminates.</b> A cell that never sleeps is not slow,
/// it is wrong, and it will read as a performance problem right up until somebody notices it is a
/// termination problem — <b>a cell oscillating by one millilitre forever passes every
/// volume-conservation assertion there is</b>. <see cref="FluidRules"/> carries the proof that it
/// cannot; <see cref="Settle"/> reporting <c>Settled: false</c> is that proof failing in practice
/// and should be treated as a defect rather than as a budget.
/// </para>
/// <para>
/// <b>The two oracles, and neither has a reference implementation.</b> Volume conservation is by
/// construction — <see cref="FluidGrid.Transfer"/> is the only mutator the tick may call, and it
/// subtracts and adds the same number — so the test is the arithmetic identity
/// <c>held == added − removed − displaced</c>, with no tolerance, in integers, in the spirit of
/// M7b's "an hour in one call equals 3,600 calls of one second, to the litre". The second is
/// <see cref="VerifyQuiescent"/>: <b>after the automaton says nothing is left to do, a full scan
/// of every held cell must agree.</b> That is what catches a missing wake, which is the failure
/// mode a volume assertion is structurally blind to.
/// </para>
/// <para>
/// <b>No pressure, and therefore no siphons.</b> Water falls and spreads; it never rises. DESIGN
/// rules out "water pressure, flow velocity, anything a real fluid solver does", so a U-bend does
/// not fill its far arm and a sealed pocket under a column does not level with it. <b>Filling a
/// mine still works</b>, because water arriving from above reaches each side passage at that
/// passage's own height and spreads in there. Named because it is the first thing that will look
/// like a bug and is not one.
/// </para>
/// <para>
/// <b>Rate-free, exactly as <c>PlayerMotion.Step</c> is.</b> <see cref="Step()"/> is one tick; how
/// often ticks happen is the caller's business and is M8b's. DESIGN's <i>slower than realtime is
/// accuracy</i> is what licenses that, and a tick that arrives late costs nothing here.
/// </para>
/// <para>
/// <b>And a tick is resumable across frames, because one tick can be a whole second of CPU.</b>
/// A chunk bitten out from under a lake wakes 43,374 cells in one tick and a 2 × 2 × 2 block
/// 302,182 — 73.5 ms in Release and ~0.7 s in the Debug build people play (M8b-1's chamber
/// fixture) — and an ordinary 6,000-cell flood ticks at 17.7 ms in Debug, a frame and a half twice
/// a second. No tick <i>rate</i> bounds that, because one tick is the unit and it arrives whole.
/// So the tick is a cursor over its batch: <see cref="Resume"/> visits up to N cells and stops,
/// <see cref="Step(long)"/> resumes in granules until a deadline, and the tick completes when the
/// cursor reaches the end, however many calls that took. <b>The trajectory is unchanged by the
/// slicing</b> — the same cells are visited in the same order against the same live grid — which
/// is asserted cell for cell, and which is what makes the per-frame budget a cost decision rather
/// than a physics one. What slicing changes is only <i>when</i> a tick ends, and a tick ending
/// late is the design (DESIGN, <i>slower than realtime is accuracy</i>).
/// </para>
/// <para>
/// <b>Water may be added, removed or disturbed between slices, and that is the game's case</b>
/// — the relay pours and edits land between frames. Every public mutator wakes into the
/// <i>next</i> tick's set, so a cell touched mid-tick is revisited whether or not this tick
/// already passed it, and conservation holds by construction as it always did. What is not
/// allowed mid-tick is asking whether the automaton was right to stop: <see cref="VerifyQuiescent"/>
/// refuses, because the question is about a stopped automaton.
/// </para>
/// <para>
/// <b>And a cell whose chunk is not loaded is paused, not displaced (M8b-4a).</b> The terrain
/// answers "cannot hold" for an unloaded voxel, which is right for flow — water must not enter a
/// chunk that does not exist — and was, until this slice, also what a visited cell got when its
/// own chunk left the box: squeezed into neighbours that were unloaded too, and drained as
/// displaced. A visit now asks <see cref="IFluidTerrain.IsLoaded"/> second, on that branch only,
/// and a cell the world cannot answer for sleeps in place and is remembered; its chunk arriving
/// wakes it through <see cref="ChunkArrived"/>, with the held cells on either side of the chunk's
/// faces. No water moves while a chunk is away, so <i>the same millilitres in the same cells</i>
/// holds by construction across a walk, and <c>displaced</c> on the <c>M8b:</c> line is the
/// play's oracle: it stays at zero while nothing turns to rock.
/// </para>
/// </remarks>
public sealed partial class FluidSimulation : IFluidPersistence
{
    // Order is fixed so a run is reproducible: +X, -X, +Z, -Z.
    private static readonly (int Dx, int Dz)[] Sideways = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    private static readonly double TicksPerMillisecond = Stopwatch.Frequency / 1000.0;

    /// <summary>
    /// How much of a frame the tick may take by default, when the caller slices it with
    /// <see cref="Step(long)"/>: 2 ms, the mesh-apply budget's number.
    /// </summary>
    /// <remarks>
    /// <b>An estimate, labelled as one, and <c>--fluidbudget=</c> is the sweep.</b> The arithmetic
    /// that picked it: in Debug a visit is 2.9 µs, so 2 ms is ~690 visits a frame and ~41,000 a
    /// second at 60 fps, which is the widest-breach flood's 20,737-cell peak still ticking at 2 Hz;
    /// in Release (0.25 µs) it is ~480,000 a second, which is the whole light budget. The roof-open
    /// case — 302,182 cells — then takes ~37 frames in Release and ~350 in Debug rather than
    /// stalling one frame for 73 ms or 0.7 s: the water arrives over six seconds instead of the game
    /// stopping. <b>A ceiling, not a reservation</b>: a tick that fits costs what it costs, and a
    /// quiet grid costs nothing. Sibling of <c>ChunkPipeline.DefaultLightBudget</c> (4 ms) and the
    /// mesh-apply budget (2 ms), and like them checked <i>after</i> a granule of work and never
    /// before, so a budget of zero still visits one granule a frame and the tick still ends.
    /// </remarks>
    public static readonly TimeSpan DefaultTickBudget = TimeSpan.FromMilliseconds(2.0);

    /// <summary>
    /// Cells visited between two reads of the clock by <see cref="Step(long)"/>. The overshoot
    /// past a deadline is bounded by one of these.
    /// </summary>
    /// <remarks>
    /// Sixty-four visits is ~16 µs in Release and ~190 µs in Debug, so the worst overshoot is a
    /// fifth of a millisecond in the build that needs the budget most, and the clock read is
    /// amortised to under a nanosecond a visit. Per-visit checking would be a 10–15% tax in
    /// Release for a guarantee nobody can see.
    /// </remarks>
    public const int VisitsPerDeadlineCheck = 64;

    private readonly IFluidTerrain _terrain;
    private readonly FluidGrid _grid = new();
    private readonly Func<long> _nowTicks;

    // The active set, as a list for a deterministic visit order plus a set for deduplication.
    // A HashSet alone would work and would make the trajectory depend on hash ordering, which
    // turns "the same input settles the same way" from a property into a coincidence.
    private List<VoxelPos> _batch = [];
    private List<VoxelPos> _pending = [];
    private readonly HashSet<VoxelPos> _queued = [];

    // Cells the tick in progress visited and found nothing to do for. See Rested.
    private readonly List<VoxelPos> _rested = [];

    // The tick at which each chunk last had water put into it or its rock disturbed. Read by the
    // sheet solve, which will not level a sheet that touches a chunk still receiving.
    //
    // WHY PER CHUNK AND NOT ONE COUNTER FOR THE WORLD. SheetSettle counted inflow globally until
    // 2026-09-09, so one portal drawing 150 L into one cell postponed levelling for every sheet
    // in the world — 0 passes in 340 ticks of a played session, with 70,000 cells awake. The
    // disturbance is local and the counter has to be too. A chunk is the resolution because the
    // grid is already keyed by it and the flood can read it once per cell for the price of a
    // cached lookup; per cell would be a timestamp on every voxel.
    private readonly Dictionary<ChunkPos, long> _inflowTick = [];
    private ChunkPos _inflowCacheKey;
    private long _inflowCacheValue = NeverInflowed;
    private bool _inflowCacheValid;

    /// <summary>A chunk nothing has ever poured into. Not a tick number, and never arithmetic.</summary>
    internal const long NeverInflowed = long.MinValue;

    // Cells visited while their chunk was not loaded, asleep in place until it arrives. Keyed by
    // chunk because an arrival is what wakes them, and a set per chunk because a loaded neighbour
    // moving water beside the boundary wakes the same paused cell again every tick it does.
    private readonly Dictionary<ChunkPos, HashSet<VoxelPos>> _paused = [];
    private int _pausedCells;

    // Chunks whose row on disk lists awake cells, as of the last snapshot of each. A cell that
    // rests without a write leaves its panel clean and its row saying "awake", and a row that is
    // never rewritten would wake those cells on every open — the played lake, a million visits
    // a session, for ever. So a save rewrites such a row once, with the list it has now.
    private readonly HashSet<ChunkPos> _storedAwake = [];

    // The tick in progress: how far through its batch the cursor is, and what it has done so far.
    // A batch with anything in it is a tick in progress; completing one empties it.
    private int _cursor;
    private int _tickCellsMoved;
    private long _tickMillilitresMoved;

    /// <summary>Which way this fluid falls, as a sign on Y. Water falls down.</summary>
    /// <remarks>
    /// <b>One field rather than six literal offsets, and it exists because of a decision rather than
    /// a caller.</b> Tier B is one fluid per grid (settled 2026-09-03, see MATERIALS-NOTES): a second
    /// fluid is a second <see cref="FluidSimulation"/> over its own <see cref="FluidGrid"/>, and a
    /// lighter-than-air one is this automaton with this sign reversed — heavy gas is water's rule
    /// verbatim. Nothing sets it today, because every caller constructs water; what it buys today is
    /// that the tick has one notion of down instead of six copies to keep in agreement.
    /// </remarks>
    private readonly int _fallSign = -1;

    private readonly SpreadRule _spread;

    /// <param name="terrain">The one question the automaton asks the world.</param>
    /// <param name="nowTicks">
    /// The clock <see cref="Step(long)"/> reads, in <see cref="Stopwatch"/> ticks. Injected the way
    /// <c>ChunkPipeline</c> injects its own, so a test can drive a deadline deterministically; null
    /// is <see cref="Stopwatch.GetTimestamp"/>. Nothing but the deadline check ever reads it.
    /// </param>
    /// <param name="spread">
    /// What a cell divides its sideways surplus by. Fixed at construction because the wake rule
    /// depends on it, and a wake rule that changed under resting water would leave moves asleep.
    /// </param>
    public FluidSimulation(IFluidTerrain terrain, Func<long>? nowTicks = null, SpreadRule spread = SpreadRule.Fixed)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        _terrain = terrain;
        _nowTicks = nowTicks ?? Stopwatch.GetTimestamp;
        _spread = spread;
    }

    /// <summary>The sideways rule this simulation runs. See <see cref="SpreadRule"/>.</summary>
    public SpreadRule Spread => _spread;

    /// <summary>Converts a budget to the clock's units, for a caller computing a deadline.</summary>
    public static long BudgetTicks(TimeSpan budget) =>
        (long)(budget.TotalMilliseconds * TicksPerMillisecond);

    public FluidGrid Grid => _grid;

    /// <summary>
    /// Cells still awake: those this tick has not yet visited, plus those queued for the next.
    /// </summary>
    /// <remarks>
    /// At a tick boundary that is exactly the next tick's set, which is what every figure recorded
    /// before the slice existed read. Mid-tick the unvisited half is counted too, because a report
    /// taken between frames that showed only the next tick's queue would read a flood in progress
    /// as nearly asleep.
    /// <para>
    /// <b>And it counts <see cref="FarBacklog"/>, which is the whole reason the near/far order is
    /// safe.</b> Deferred far cells are awake — they simply have not been admitted — so a count
    /// that omitted them would report a throttled flood as quiescent, and <see cref="Settle"/>
    /// would stop with water still moving. The term is zero on the unordered arm.
    /// </para>
    /// </remarks>
    public int ActiveCellCount => _pending.Count + FarBacklog + RemainingInTick;

    /// <summary>True between a tick beginning and its cursor reaching the end of its batch.</summary>
    public bool TickInProgress => _batch.Count > 0;

    /// <summary>Cells the tick in progress has still to visit. Zero at a tick boundary.</summary>
    public int RemainingInTick => _batch.Count - _cursor;

    /// <summary>Millilitres held. The left-hand side of the conservation oracle.</summary>
    public long TotalMillilitres => _grid.TotalMillilitres;

    /// <summary>
    /// Millilitres that have entered through <see cref="Add"/> since this simulation began.
    /// </summary>
    /// <remarks>
    /// <b>Reported rather than hidden, and M8-PLAN names the reason.</b> The debug relay that makes
    /// this milestone testable creates water from nothing, which is precisely what the conservation
    /// oracle forbids — so the injected total is a term in the identity rather than something
    /// subtracted out by whoever is trying to make the check pass.
    /// </remarks>
    public long AddedMillilitres { get; private set; }

    /// <summary>Millilitres taken out through <see cref="Remove"/>.</summary>
    public long RemovedMillilitres { get; private set; }

    /// <summary>Millilitres taken out through <see cref="HandOff"/> — water that became Tier A's.</summary>
    /// <remarks>
    /// A term of its own rather than part of <see cref="RemovedMillilitres"/>, because the two
    /// answer different questions: removed water left the world through the relay, handed water
    /// is still in the world as litres in a basin, and Tier A's <c>received</c> has to equal this
    /// to the litre. The B → A seam reported from Tier B's end, as <c>drawn</c> is A → B's.
    /// </remarks>
    public long HandedMillilitres { get; private set; }

    /// <summary>
    /// Millilitres destroyed because the voxel holding them stopped being able to hold them and
    /// no neighbour had room.
    /// </summary>
    /// <remarks>
    /// <b>The only place this milestone destroys water on purpose, and it is a reported number for
    /// the same reason <see cref="AddedMillilitres"/> is.</b> It happens when something places a
    /// block into standing water. Squeezing it into neighbours first means the usual case costs
    /// nothing; a block placed into a sealed full pocket has genuinely nowhere to put it, and the
    /// alternative — refusing the placement — makes a building verb depend on the fluid tick.
    /// A figure that climbs during a run nobody was building in is a defect.
    /// </remarks>
    public long DisplacedMillilitres { get; private set; }

    /// <summary>
    /// Cells holding water that an edit touched through <see cref="Disturb"/>, over the lifetime.
    /// </summary>
    /// <remarks>
    /// The terrain-changed half of "inflow" for <see cref="SheetSettle"/>: a dig into standing
    /// water gives the automaton work the way a pour does, and a settle that fired on the tick
    /// after it would level a sheet whose floor was still moving. Counts wet cells touched rather
    /// than calls, because a chunk bite far from any water is 32,768 calls and no work; and
    /// counts them whether or not they were already awake, because an edit under moving water
    /// is a disturbance too.
    /// </remarks>
    public long DisturbedCells { get; private set; }

    /// <summary>
    /// Cells asleep in place because their chunk is not loaded, remembered so that its arrival
    /// can wake them. The number the <c>M8b:</c> line prints beside <c>displaced</c>.
    /// </summary>
    /// <remarks>
    /// <b>Paused is not resting and not displaced.</b> A paused cell holds water the world cannot
    /// answer for — its chunk left the box, or has not generated yet — and the automaton neither
    /// moves it nor lists it as rested, because a cell the hand-off cannot see the ground under
    /// is not a cell the hand-off may take. Before M8b-4a such a cell was displaced into
    /// neighbours that were unloaded too, and drained: 2.9 ML of 7.2 ML drawn in one played
    /// session, every jump of it on a window move. A figure here that never comes down in a run
    /// where chunks are arriving is a chunk whose arrival nobody announced.
    /// </remarks>
    public int PausedCellCount => _pausedCells;

    /// <summary>Chunks with at least one paused cell.</summary>
    public int PausedChunkCount => _paused.Count;

    /// <summary>Paused cells woken by their chunk arriving, over the lifetime.</summary>
    public long ResumedCells { get; private set; }

    /// <summary>
    /// Millilitres put back from the world file at open, through <see cref="Restore"/>. A term of
    /// the identity: held equals added plus loaded, less removed, displaced and handed.
    /// </summary>
    public long LoadedMillilitres { get; private set; }

    /// <summary>Panels restored from the world file at open.</summary>
    public int RestoredPanels { get; private set; }

    /// <summary>
    /// Held cells woken at an arrival for standing on one of its faces, on either side, over the
    /// lifetime. The cost of the boundary rule, printed so the pause and the faces can be told apart.
    /// </summary>
    public long FaceWakes { get; private set; }

    /// <summary>Ticks run.</summary>
    public long Ticks { get; private set; }

    /// <summary>Cells examined across every tick. The cost, against the light budget's ~520,000/s.</summary>
    public long CellVisits { get; private set; }

    /// <summary>The largest active set seen. The number M8a exists to take.</summary>
    public int PeakActiveCells { get; private set; }

    /// <summary>
    /// Puts water into a voxel and wakes it. Returns what would not fit, which the caller owns.
    /// </summary>
    public long Add(VoxelPos voxel, long millilitres)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(millilitres);

        if (!_terrain.CanHold(voxel))
        {
            return millilitres;
        }

        // Through RoomAt as well as through Fill's own clamp. For water the two are the same
        // number; see RoomAt for the fluid they are not.
        long offered = Math.Min(millilitres, RoomAt(voxel));
        long overflow = millilitres - offered + _grid.Fill(voxel, offered);
        long accepted = millilitres - overflow;
        if (accepted > 0)
        {
            AddedMillilitres += accepted;
            NoteInflow(voxel);
            Wake(voxel);
            WakeNeighboursOf(voxel);
        }

        return overflow;
    }

    /// <summary>Takes water out of a voxel. Returns what was actually removed.</summary>
    public long Remove(VoxelPos voxel, long millilitres)
    {
        long taken = Take(voxel, millilitres);
        RemovedMillilitres += taken;
        return taken;
    }

    /// <summary>
    /// Takes water out of a voxel on Tier A's behalf — the B → A seam. Returns what was taken,
    /// which the caller must deliver to a basin or the two tiers' identities stop agreeing.
    /// </summary>
    /// <remarks>
    /// <see cref="Remove"/> with a different counter and nothing else: the water is not gone, it
    /// has changed representation, and <see cref="HandedMillilitres"/> is the number a Tier A
    /// <c>received</c> has to match. Which cells hand off, and when, is the seam's decision
    /// (<see cref="SurfaceHandoff"/>) and not the automaton's, for the reason the portal index
    /// does not know what an edit is: the automaton has no opinion about the analytic ground.
    /// </remarks>
    public long HandOff(VoxelPos voxel, long millilitres)
    {
        long taken = Take(voxel, millilitres);
        HandedMillilitres += taken;
        return taken;
    }

    private long Take(VoxelPos voxel, long millilitres)
    {
        long taken = _grid.Drain(voxel, millilitres);
        if (taken > 0)
        {
            Wake(voxel);
            WakeNeighboursOf(voxel);
        }

        return taken;
    }

    /// <summary>
    /// The cells the last completed tick visited and found nothing to do for — resting water, as
    /// far as one tick can see it. Valid from a tick's completion until the next tick begins.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A rested cell holds water, could hold it, and could neither fall nor spread when it was
    /// visited.</b> That is the automaton's own definition of rest — the visit that found no move
    /// is the visit that would have made one — and it is what DESIGN's <i>stable cells sleep and
    /// merge into a Tier A body</i> needs: the sleep is this list, and the merge is
    /// <see cref="SurfaceHandoff"/>'s pass over it at the tick boundary, with the analytic ground
    /// as the test the automaton cannot make.
    /// </para>
    /// <para>
    /// <b>A cell that rested early in the tick and was given water by a later visit is still
    /// listed</b>, because the list is a record of visits and not a promise about the state at the
    /// boundary; the consumer re-reads what a cell holds. It is a list rather than a set for the
    /// reason the active set is, and it is cleared when a tick begins, so a consumer that wants it
    /// reads it between <c>Complete</c> and the next <c>Begin</c> — which in the game is the tick
    /// boundary, before the next <c>Step</c>. <b>One thing other than a visit appends to it</b>:
    /// <see cref="LevelSheets()"/>, at that same boundary, lists the cells it levelled and left
    /// with no move, because a visit to any of them would find exactly that and the hand-off
    /// would otherwise never see a levelled sheet — so the settle runs before the hand-off.
    /// </para>
    /// </remarks>
    public IReadOnlyList<VoxelPos> Rested => _rested;

    /// <summary>
    /// Wakes whatever water an edit at this voxel could have given something to do.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The automaton cannot see edits, so whatever performs them has to say so. It is the same
    /// contract <c>ChunkLight.Invalidate</c> puts on its caller, and for the same reason: this is
    /// a push, and a cell nobody pushes never runs.
    /// </para>
    /// <para>
    /// <b>Only cells that hold water are woken — the edited voxel if it does, and each of its six
    /// neighbours that does — never an empty opened voxel.</b> A voxel dug out of dry rock can only
    /// receive water from a neighbour that has some, and waking that neighbour is what makes it
    /// flow; waking the empty voxel as well costs a visit that finds nothing. Harmless at one voxel
    /// a swing, and not at a chunk bite: 32,768 opened voxels times seven wakes is 229,000 visits
    /// landing in one tick, nearly all of them dry rock nowhere near any water. A voxel that
    /// becomes rock while holding water is woken so that <see cref="Visit"/> can displace it. And
    /// a grid holding nothing at all returns before looking, because that is every edit in a world
    /// that has not flooded yet.
    /// </para>
    /// </remarks>
    public void Disturb(VoxelPos voxel)
    {
        if (_grid.HeldCellCount == 0)
        {
            return;
        }

        WakeIfHolding(voxel);
        WakeIfHolding(Above(voxel));
        WakeIfHolding(Below(voxel));
        foreach ((int dx, int dz) in Sideways)
        {
            WakeIfHolding(voxel.Offset(dx, 0, dz));
        }
    }

    private void WakeIfHolding(VoxelPos cell)
    {
        if (_grid.AmountAt(cell) > 0)
        {
            // Counted whether or not the cell was already awake: an edit under water that is
            // still moving is as much a disturbance as one under water at rest.
            Wake(cell);
            NoteInflow(cell);
            DisturbedCells++;
        }
    }

    /// <summary>
    /// Visits up to <paramref name="maxVisits"/> cells of the tick in progress, beginning one if
    /// none is. Returns the tick's result when this call completed it, and null while it has cells
    /// left.
    /// </summary>
    /// <remarks>
    /// <b>The deterministic primitive under both <see cref="Step()"/> overloads.</b> It reads no
    /// clock, so <i>a tick run in slices lands the same water in the same cells as a tick run
    /// whole</i> is a property a test can assert exactly rather than a claim about timing. A
    /// slice that begins a tick swaps in the pending set as the batch and clears the queue, so
    /// anything woken from here on is the <i>next</i> tick's business — including a cell of this
    /// batch the cursor has not reached, which is then visited twice, as it always was when a
    /// whole tick woke it.
    /// </remarks>
    public FluidTickResult? Resume(int maxVisits)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxVisits);

        if (!TickInProgress)
        {
            Begin();
        }

        // Remaining first, then the minimum: Step() passes int.MaxValue, and adding that to a
        // cursor past zero overflows negative — which returned null from a whole-tick call that
        // was finishing a sliced tick, and the suite's first run is what said so.
        int visited = Math.Min(_batch.Count - _cursor, maxVisits);
        int end = _cursor + visited;
        for (; _cursor < end; _cursor++)
        {
            VoxelPos cell = _batch[_cursor];
            long here = Visit(cell, out bool paused);
            if (here > 0)
            {
                _tickCellsMoved++;
                _tickMillilitresMoved += here;
            }
            else if (!paused && _grid.AmountAt(cell) > 0)
            {
                // Held water with no move: at rest, by the automaton's own test. An empty cell
                // visited is not resting water, a cell Displace emptied is not either, and a
                // paused cell is not — the hand-off reads this list, and water whose ground the
                // world cannot answer for is not the basin's.
                _rested.Add(cell);
            }
        }

        // Per slice rather than per tick, because a profile window can close mid-tick and the
        // visits it paid for are the ones it should count.
        CellVisits += visited;
        CountVisits(end - visited, end);

        return _cursor < _batch.Count ? null : Complete();
    }

    /// <summary>
    /// Resumes the tick in progress, or begins one, and works on it until it completes or the
    /// clock passes <paramref name="deadlineTicks"/>. Returns the result when the tick completed
    /// in this call, null otherwise.
    /// </summary>
    /// <remarks>
    /// <b>The deadline is checked after each granule of <see cref="VisitsPerDeadlineCheck"/>
    /// visits and never before</b>, so every call makes progress and a deadline already in the
    /// past — a budget of zero — still advances the tick by one granule. The same rule as
    /// <c>ChunkPipeline</c>'s apply and light slices, for the same reason: a tick that costs more
    /// than the whole budget would otherwise be deferred forever, and the water with it. The
    /// overshoot past the deadline is bounded by one granule's cost.
    /// </remarks>
    public FluidTickResult? Step(long deadlineTicks)
    {
        while (true)
        {
            FluidTickResult? done = Resume(VisitsPerDeadlineCheck);
            if (done is not null || _nowTicks() >= deadlineTicks)
            {
                return done;
            }
        }
    }

    /// <summary>One whole tick over the active set, and nothing else.</summary>
    /// <remarks>
    /// Finishes a tick in progress rather than starting a new one, so a caller that sliced and
    /// then wants the boundary gets the boundary of the tick it was in.
    /// </remarks>
    public FluidTickResult Step() => Resume(int.MaxValue)!.Value;

    private void Begin()
    {
        (_batch, _pending) = (_pending, _batch);
        _pending.Clear();
        _rested.Clear();
        _cursor = 0;
        _tickCellsMoved = 0;
        _tickMillilitresMoved = 0;

        // Near first, far under a quota, the rest deferred past the boundary — and _queued left
        // holding exactly what was deferred. With no near field set this is one Clear and nothing
        // else, which is the arm every recorded figure was taken in. See FluidSimulation.NearField.
        ComposeBatch();

        // Measured on the set actually visited rather than on the one left behind, because the
        // peak is a cost figure and the cost is paid here.
        if (_batch.Count > PeakActiveCells)
        {
            PeakActiveCells = _batch.Count;
        }
    }

    private FluidTickResult Complete()
    {
        int visited = _batch.Count;
        _batch.Clear();
        _cursor = 0;
        Ticks++;
        return new FluidTickResult(
            visited, _tickCellsMoved, _tickMillilitresMoved, _pending.Count + FarBacklog);
    }

    /// <summary>
    /// Ticks until nothing is left to do, or until the limit runs out.
    /// </summary>
    /// <param name="maxTicks">
    /// A bound, not a budget. <b>Reaching it is a failure of the termination argument</b>, so make
    /// it generous and treat <c>Settled: false</c> as a defect rather than as "needs longer".
    /// </param>
    public FluidSettleResult Settle(int maxTicks = 100_000)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxTicks);

        long visitsBefore = CellVisits;
        int ticks = 0;
        int peak = 0;

        // A tick in progress is finished first and counts as one of the ticks: the bound is on
        // ticks completed, and a caller that sliced before settling is still inside the first.
        while ((TickInProgress || _pending.Count + FarBacklog > 0) && ticks < maxTicks)
        {
            FluidTickResult tick = Step();
            ticks++;
            if (tick.CellsVisited > peak)
            {
                peak = tick.CellsVisited;
            }
        }

        return new FluidSettleResult(ticks, ActiveCellCount == 0, peak, CellVisits - visitsBefore);
    }

    /// <summary>
    /// Scans every held cell and asks whether the automaton was right to stop.
    /// </summary>
    /// <remarks>
    /// <b>This is the oracle a volume assertion cannot be.</b> Conservation holds by construction
    /// whether or not the sleep rules are correct, so a missed wake leaves water perched above an
    /// empty cell forever and every total still adds up. This walks the grid rather than the active
    /// set — the one place in the milestone that is allowed to — and returns false if any cell
    /// still has a move available to it.
    /// </remarks>
    public bool VerifyQuiescent()
    {
        if (TickInProgress)
        {
            // Not "false": a tick half-way through its batch has moves available by definition,
            // and a caller asking here has confused a frame boundary with a tick boundary. That
            // is a defect in the caller, and a boolean would let it read as a defect in the
            // sleep rules.
            throw new InvalidOperationException(
                $"VerifyQuiescent asked mid-tick, with {RemainingInTick} cell(s) still to visit; "
                + "finish the tick with Step() first.");
        }

        Span<int> sides = stackalloc int[4];
        foreach (VoxelPos cell in _grid.HeldCells())
        {
            if (!_terrain.CanHold(cell))
            {
                if (!_terrain.IsLoaded(cell))
                {
                    // Paused, or asleep in a chunk that left: the world cannot answer for it, so
                    // neither can this check. Whether it has a move is its chunk's arrival's
                    // question, and reading it here would fail every walk for the wrong reason.
                    continue;
                }

                return false;
            }

            int amount = _grid.AmountAt(cell);

            VoxelPos below = Below(cell);
            if (_terrain.CanHold(below) && RoomAt(below) > 0)
            {
                return false;
            }

            if (!FluidRules.CanSpread(amount))
            {
                continue;
            }

            int divisor = ReadSides(cell, amount, sides);
            for (int k = 0; k < sides.Length; k++)
            {
                if (sides[k] >= 0 && FluidRules.HorizontalTransfer(amount, sides[k], divisor) > 0)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Reads a cell's four sides — the amount held, or −1 where the side cannot hold water —
    /// and returns the divisor its sideways moves use, under this simulation's rule.
    /// </summary>
    /// <remarks>
    /// <b>One read of the sides serves both rules, and under the fixed rule it changes nothing
    /// to the visit.</b> The visit used to read each side at its turn; the sides are distinct
    /// cells and the visit writes only into them, never into a side it has yet to read, so a
    /// side's amount at its turn is its amount before the first transfer — the snapshot the
    /// termination proof assumes. Reading them first makes that explicit, and gives the counted
    /// rule the count it needs before anything moves. Shared with <see cref="VerifyQuiescent"/>
    /// for the reason <see cref="FluidRules"/> is: the check is independent of the wake
    /// bookkeeping, not of the arithmetic.
    /// </remarks>
    private int ReadSides(VoxelPos cell, int amount, Span<int> sides)
    {
        int lower = 0;
        for (int k = 0; k < Sideways.Length; k++)
        {
            (int dx, int dz) = Sideways[k];
            VoxelPos side = cell.Offset(dx, 0, dz);
            if (!_terrain.CanHold(side))
            {
                sides[k] = -1;
                continue;
            }

            int beside = _grid.AmountAt(side);
            sides[k] = beside;
            if (beside < amount)
            {
                lower++;
            }
        }

        return FluidRules.Divisor(_spread, lower);
    }

    /// <summary>
    /// Moves what this cell can move, and returns how much that was. <paramref name="paused"/>
    /// is true when the cell was neither moved nor rested but put to sleep in an unloaded chunk.
    /// </summary>
    private long Visit(VoxelPos cell, out bool paused)
    {
        paused = false;
        int amount = _grid.AmountAt(cell);
        if (amount == 0)
        {
            // Nothing here. Not re-queued, so it sleeps.
            return 0;
        }

        if (!_terrain.CanHold(cell))
        {
            if (!_terrain.IsLoaded(cell))
            {
                // Unknown rather than rock: the chunk left the box or has not generated yet.
                // Asked second, so the hot path — a held cell in a loaded chunk — never asks it.
                Pause(cell);
                paused = true;
                return 0;
            }

            return Displace(cell, amount);
        }

        long moved = 0;

        VoxelPos below = Below(cell);
        if (_terrain.CanHold(below))
        {
            int fell = _grid.Transfer(cell, below, Math.Min(amount, RoomAt(below)));
            if (fell > 0)
            {
                Record(cell, below);
                moved += fell;
                amount -= fell;
            }

            if (amount == 0)
            {
                return moved;
            }

            // Anything still here means Transfer was clamped by the room below, so the cell below
            // is now full and spreading sideways is the right next move. No explicit "is it full"
            // test, because that is what having water left over means.
        }

        if (!FluidRules.CanSpread(amount))
        {
            return moved;
        }

        // Against the snapshot taken before any of this cell's sideways moves — the source's
        // amount and every side's — which is what makes the split even between neighbours and
        // what FluidRules' termination proof assumes. Reading the live source here would drain it
        // as it went; the sides are read once, up front, so the counted rule's divisor is a
        // property of the state the tick found and not of the order it visited in.
        Span<int> sides = stackalloc int[4];
        int divisor = ReadSides(cell, amount, sides);
        for (int k = 0; k < sides.Length; k++)
        {
            if (sides[k] < 0)
            {
                continue;
            }

            int want = FluidRules.HorizontalTransfer(amount, sides[k], divisor);
            if (want <= 0)
            {
                continue;
            }

            VoxelPos side = cell.Offset(Sideways[k].Dx, 0, Sideways[k].Dz);
            int sideways = _grid.Transfer(cell, side, Math.Min(want, RoomAt(side)));
            if (sideways > 0)
            {
                Record(cell, side);
                moved += sideways;
            }
        }

        return moved;
    }

    /// <summary>
    /// Pushes water out of a voxel that has stopped being able to hold it.
    /// </summary>
    private long Displace(VoxelPos cell, int amount)
    {
        long moved = 0;

        VoxelPos below = Below(cell);
        if (_terrain.CanHold(below))
        {
            moved += Squeeze(cell, below, ref amount);
        }

        foreach ((int dx, int dz) in Sideways)
        {
            if (amount == 0)
            {
                break;
            }

            VoxelPos side = cell.Offset(dx, 0, dz);
            if (_terrain.CanHold(side))
            {
                moved += Squeeze(cell, side, ref amount);
            }
        }

        VoxelPos above = Above(cell);
        if (amount > 0 && _terrain.CanHold(above))
        {
            // The one place water goes up, and it is displacement rather than flow: something took
            // this voxel away, so the water is pushed rather than lifted.
            moved += Squeeze(cell, above, ref amount);
        }

        if (amount > 0)
        {
            long lost = _grid.Drain(cell, amount);
            DisplacedMillilitres += lost;
            moved += lost;
        }

        return moved;
    }

    private int Squeeze(VoxelPos from, VoxelPos to, ref int amount)
    {
        int shifted = _grid.Transfer(from, to, Math.Min(amount, RoomAt(to)));
        if (shifted > 0)
        {
            Record(from, to);
            amount -= shifted;
        }

        return shifted;
    }

    /// <summary>
    /// Records a transfer and wakes whatever it could have given something to do.
    /// </summary>
    /// <remarks>
    /// <b>Both endpoints, the six neighbours of the source, and under the counted rule the four
    /// sides of the destination too.</b> The source lost water, so a neighbour holding more
    /// can now give to it and the cell above it can now fall into it. Under the fixed rule the
    /// destination's gain can only <i>remove</i> moves from its own neighbours — it has less room
    /// than before and is no lower than before — so it is woken itself, in case it can now pass
    /// the water on, and nothing else.
    /// <para>
    /// <b>That argument is true only because the fixed rule's transfer depends on one pair of
    /// cells and nothing else, and it was false before it did.</b> Dividing by the count of
    /// eligible neighbours made a cell's move depend on how many of its neighbours were lower, so
    /// a neighbour gaining could shrink the divisor and hand a nearby cell a move it did not have
    /// a moment ago — with nothing awake to take it. <see cref="VerifyQuiescent"/> caught it the
    /// first time it was asked, on four cells that were one millilitre out of level.
    /// <b><see cref="SpreadRule.Counted"/> is that rule, brought back with the wake it needs</b>:
    /// a cell's move reads its own amount, the room below it and its four sides, so the cells
    /// that read the destination are the destination, the cell above it and its four sides —
    /// and of those only a side's count can shrink on a gain, so the sides are woken and the
    /// rest is complete by construction. The fixed rule keeps the narrower wake because it is
    /// enough for it and each extra wake is a visit; the bench prices the difference.
    /// <b>Drop the destination's sides under the counted rule and the M8a defect comes back,
    /// silently</b> — the recipe in <c>SpreadRuleTests</c> is the one that says so. <b>Widen it
    /// to the destination's six and a one-wide shaft drains at half the rate</b>, which is the
    /// other recipe, and the one only the bench can see.
    /// </para>
    /// <para>
    /// <b><see cref="Wake"/>(<paramref name="to"/>) is today an identity — removing it breaks
    /// nothing and cannot</b>, because every destination the automaton moves water to is a face
    /// neighbour of the source and is therefore already woken by the line below it. It is kept
    /// because <b>the debug relay is a transfer between cells that are not adjacent</b>, and the
    /// moment that exists this line is the only thing waking the far end. Named rather than
    /// deleted, so the next person does not have to rediscover which of the two it is.
    /// </para>
    /// </remarks>
    private void Record(VoxelPos from, VoxelPos to)
    {
        Wake(from);
        Wake(to);
        WakeNeighboursOf(from);
        if (_spread == SpreadRule.Counted)
        {
            // The four sides of the destination and nothing else. A cell's move reads its own
            // amount, the room below it and its four sides; of the cells that read `to`, the one
            // above it reads only its room, which shrank, and the cell below it does not read it
            // at all — only a side's count can shrink on a gain. The first version woke all six,
            // and in a one-wide shaft the two extra wakes reordered the column so that it fell
            // half a cell a tick instead of a whole one (--fluid at bore 1: 7.8 L/tick against
            // 14.3, both shapes, to the litre). Nothing sideways there at all, so the physics was
            // identical and the order was the whole of the difference.
            foreach ((int dx, int dz) in Sideways)
            {
                Wake(to.Offset(dx, 0, dz));
            }
        }
    }

    private void Pause(VoxelPos cell)
    {
        if (!_paused.TryGetValue(cell.Chunk, out HashSet<VoxelPos>? cells))
        {
            cells = [];
            _paused[cell.Chunk] = cells;
        }

        if (cells.Add(cell))
        {
            _pausedCells++;
        }
    }

    /// <summary>
    /// Puts a stored panel back, asleep, with its awake cells on the paused list for the chunk's
    /// arrival to wake. The open's one call per row of the <c>fluid</c> table.
    /// </summary>
    /// <remarks>
    /// <b>Restored water is paused water, and that is what makes one mechanism serve the load and
    /// the revival.</b> At open no chunk is loaded, so a restored cell is in exactly the state a
    /// cell whose chunk left the box is in: held, asleep, in a chunk the world cannot answer for.
    /// The awake list goes on the paused list, and <see cref="ChunkArrived"/> wakes it through
    /// the same hook it wakes a paused flood through when the chunk generates, loads, or comes
    /// back from the cache. Nothing is woken here — waking a cell in an unloaded chunk would only
    /// pause it again at its next visit, at the cost of the visit. The millilitres are counted on
    /// <see cref="LoadedMillilitres"/> and nowhere else, so the identity stays exact from the first
    /// frame: the water was not added, it was already there.
    /// </remarks>
    public void Restore(FluidPanelSnapshot panel)
    {
        ArgumentNullException.ThrowIfNull(panel);

        long restored = _grid.Restore(panel.Chunk, panel.Cells);
        LoadedMillilitres += restored;
        if (restored == 0)
        {
            return;
        }

        RestoredPanels++;

        if (panel.Awake.Length > 0)
        {
            _storedAwake.Add(panel.Chunk);
        }

        VoxelPos origin = panel.Chunk.OriginVoxel;
        foreach (ushort index in panel.Awake)
        {
            (int lx, int ly, int lz) = VoxelPos.FromIndex(index);
            Pause(new VoxelPos(origin.X + lx, origin.Y + ly, origin.Z + lz));
        }
    }

    /// <inheritdoc />
    public FluidPanelSnapshot? TakeDirty(ChunkPos chunk)
    {
        if (!_grid.IsDirty(chunk))
        {
            return null;
        }

        (ushort, ushort)[] cells = _grid.TakeSnapshot(chunk);
        ushort[] awake = _paused.TryGetValue(chunk, out HashSet<VoxelPos>? paused)
            ? AwakeIndices(paused.Select(cell => (ushort)cell.LocalIndex))
            : [];

        NoteStoredAwake(chunk, awake);
        return new FluidPanelSnapshot(chunk, cells, awake);
    }

    private void NoteStoredAwake(ChunkPos chunk, ushort[] awake)
    {
        if (awake.Length > 0) _storedAwake.Add(chunk);
        else _storedAwake.Remove(chunk);
    }

    /// <summary>
    /// Every held cell that is not asleep: paused, queued for the next tick, or not yet visited
    /// by the tick in progress. A cell can be more than one of those; it is yielded once.
    /// </summary>
    /// <remarks>
    /// <b>What the world file lists as awake, and the one walk of the active set a save makes.</b>
    /// Only held cells, because an awake cell in the blob is a held cell: the automaton queues
    /// holdable empty cells too, and those are woken again by whatever fills them. O(paused +
    /// active), so a report or a save may call it and the tick never does.
    /// </remarks>
    public IEnumerable<VoxelPos> AwakeHeldCells()
    {
        var seen = new HashSet<VoxelPos>();

        foreach ((_, HashSet<VoxelPos> cells) in _paused)
        {
            foreach (VoxelPos cell in cells)
            {
                if (_grid.AmountAt(cell) > 0 && seen.Add(cell)) yield return cell;
            }
        }

        foreach (VoxelPos cell in _queued)
        {
            if (_grid.AmountAt(cell) > 0 && seen.Add(cell)) yield return cell;
        }

        for (int i = _cursor; i < _batch.Count; i++)
        {
            VoxelPos cell = _batch[i];
            if (_grid.AmountAt(cell) > 0 && seen.Add(cell)) yield return cell;
        }
    }

    /// <inheritdoc />
    public void TakeAllDirty(List<FluidPanelSnapshot> into)
    {
        ArgumentNullException.ThrowIfNull(into);

        // Every awake held cell grouped by chunk, in one walk, plus every dirty panel: a panel
        // with an awake cell in it is a panel whose row is stale even if nothing in it moved
        // since the last snapshot, because the awake list is state.
        var awakeByChunk = new Dictionary<ChunkPos, List<ushort>>();

        foreach (VoxelPos cell in AwakeHeldCells()) Note(cell);

        var chunks = new HashSet<ChunkPos>(_grid.DirtyChunks());
        chunks.UnionWith(awakeByChunk.Keys);
        chunks.UnionWith(_storedAwake);

        foreach (ChunkPos chunk in chunks)
        {
            (ushort, ushort)[] cells = _grid.TakeSnapshot(chunk);
            ushort[] awake = awakeByChunk.TryGetValue(chunk, out List<ushort>? indices)
                ? AwakeIndices(indices)
                : [];
            NoteStoredAwake(chunk, awake);
            into.Add(new FluidPanelSnapshot(chunk, cells, awake));
        }

        void Note(VoxelPos cell)
        {
            if (!awakeByChunk.TryGetValue(cell.Chunk, out List<ushort>? list))
            {
                list = [];
                awakeByChunk[cell.Chunk] = list;
            }

            list.Add((ushort)cell.LocalIndex);
        }
    }

    /// <summary>Ascending and without repeats, which is what the blob requires.</summary>
    /// <remarks>
    /// The <c>Distinct</c> is belt and braces: both callers already hand over a set — the paused
    /// set itself, or <see cref="AwakeHeldCells"/>, which deduplicates a cell that is both paused
    /// and queued — so removing it is an identity today (M8b-4b's battery, one recipe green). It
    /// stays because the blob reader refuses a repeat, and a third caller would find out on disk.
    /// </remarks>
    private static ushort[] AwakeIndices(IEnumerable<ushort> indices)
    {
        ushort[] sorted = [.. indices.Distinct()];
        Array.Sort(sorted);
        return sorted;
    }

    /// <summary>
    /// A chunk's voxels are here — generated, loaded, or back from the cache. Wakes what its
    /// absence paused, and everything on either side of its six faces that holds water.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The other half of the pause, and the pipeline's <c>onVoxelsReady</c> is the one caller</b>
    /// — the same hook the portal tracker takes all three arrival paths from, so a chunk that
    /// re-enters from the cache announces itself here as a generated one does. A paused cell is
    /// woken as any cell is: into the next tick's set, where its visit finds the chunk loaded and
    /// moves what it can, or displaces it if an edit before the chunk left turned its voxel to
    /// rock and the tick never reached it — which is what would have happened had the chunk stayed.
    /// </para>
    /// <para>
    /// <b>The faces are woken too, held cells only, on both sides, because a boundary change while
    /// the chunk was away is the one thing a pause cannot have seen.</b> On the neighbour's side
    /// the case is plain: a cell that rested against the missing chunk found it solid, and nothing
    /// tells it the wall has become an opening — without this wake it stays asleep beside an empty
    /// holdable cell, which <see cref="VerifyQuiescent"/> calls a defect. On the arriving chunk's
    /// own side the case is the sheet solve: <see cref="LevelSheets()"/> writes a levelled cell
    /// beside an unloaded neighbour without waking anything across the boundary, since a neighbour
    /// that cannot hold is not a front, so the cell across it can gain a move with nobody to tell
    /// it. Every other change a neighbour can make — a pour, a draw, a dig, a flow — wakes across
    /// the boundary already and the woken cell is paused and remembered; the face wake is the
    /// belt to that brace, and each half has a test that fails without it. Bounded by six layers of
    /// 1,024 cells, held cells only, through <see cref="FluidGrid.HeldCellsOnFace"/>, so a chunk
    /// arriving in dry rock costs six dictionary lookups; a neighbour that is itself not loaded is
    /// left alone, because waking its cells would only pause them again.
    /// </para>
    /// </remarks>
    public void ChunkArrived(ChunkPos chunk)
    {
        if (_paused.Remove(chunk, out HashSet<VoxelPos>? cells))
        {
            foreach (VoxelPos cell in cells)
            {
                Wake(cell);
            }

            _pausedCells -= cells.Count;
            ResumedCells += cells.Count;
        }

        if (_grid.HeldCellCount == 0)
        {
            return;
        }

        foreach ((int dx, int dy, int dz) in Faces)
        {
            foreach (VoxelPos cell in _grid.HeldCellsOnFace(chunk, (dx, dy, dz)))
            {
                if (Wake(cell))
                {
                    FaceWakes++;
                }
            }

            ChunkPos neighbour = chunk.Offset(dx, dy, dz);
            foreach (VoxelPos cell in _grid.HeldCellsOnFace(neighbour, (-dx, -dy, -dz)))
            {
                if (_terrain.IsLoaded(cell) && Wake(cell))
                {
                    FaceWakes++;
                }
            }
        }
    }

    private static readonly (int X, int Y, int Z)[] Faces =
    [
        (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1),
    ];

    private void WakeNeighboursOf(VoxelPos cell)
    {
        Wake(Above(cell));
        Wake(Below(cell));
        foreach ((int dx, int dz) in Sideways)
        {
            Wake(cell.Offset(dx, 0, dz));
        }
    }

    private VoxelPos Below(VoxelPos cell) => cell.Offset(0, _fallSign, 0);

    private VoxelPos Above(VoxelPos cell) => cell.Offset(0, -_fallSign, 0);

    /// <summary>Millilitres this cell could still take of <em>this</em> fluid.</summary>
    /// <remarks>
    /// <b>For water it is the grid's own free space, and the method exists for the fluid that is
    /// not water.</b> Under one grid per fluid kind, a cell's room for a lighter fluid is its
    /// capacity less what every denser fluid already holds there — which is what makes the densest
    /// fluid settle as if alone and each lighter one settle against a fixed field, so
    /// <see cref="FluidRules"/>' termination proof holds per grid, ordered by density. Every
    /// transfer the tick makes is clamped through here as well as through
    /// <see cref="FluidGrid.Transfer"/>'s own clamp. For water the two agree exactly, and a clamp
    /// here that is wrong shows up as cells that will not fill — <c>AColumnFillsFromTheBottomUp</c>
    /// is the test that sees it.
    /// </remarks>
    private int RoomAt(VoxelPos cell) => _grid.FreeAt(cell);

    /// <summary>
    /// Every chunk a tick could read: those holding water or paused cells, and their face
    /// neighbours.
    /// </summary>
    /// <remarks>
    /// <b>What <see cref="FluidField.Sync"/> is given at a boundary.</b> A visit reads its own cell
    /// and its six face neighbours and nothing further, so one ring of chunks around the wet set
    /// covers a tick — an awake empty cell is one a wet neighbour woke, so it is inside that ring
    /// already. <b>The claim is checked rather than trusted</b>: a question outside the built set
    /// falls through to the source and is counted as a <see cref="FluidField.Misses"/>, and slice 3
    /// cannot start while that counter moves.
    /// </remarks>
    public void ReachableChunks(HashSet<ChunkPos> into)
    {
        ArgumentNullException.ThrowIfNull(into);

        into.Clear();
        foreach (ChunkPos chunk in _grid.PanelChunks())
        {
            into.Add(chunk);
        }

        foreach ((ChunkPos chunk, _) in _paused)
        {
            into.Add(chunk);
        }

        // Two rings of face neighbours, not one. <b>One was wrong and the miss counter said so</b>
        // — 51,336 fall-throughs in a 40 s flood: an awake cell need not hold water, so it can sit
        // in the first ring, and visiting it reads *its* face neighbours, which are in the second.
        // Manhattan rather than Chebyshev because every read is along a face: 25 chunks a seed
        // rather than 125.
        _reachScratch.Clear();
        _reachScratch.AddRange(into);
        for (int ring = 0; ring < 2; ring++)
        {
            int start = ring == 0 ? 0 : _reachRingStart;
            _reachRingStart = _reachScratch.Count;
            for (int i = start; i < _reachRingStart; i++)
            {
                ChunkPos chunk = _reachScratch[i];
                foreach ((int dx, int dy, int dz) in Faces)
                {
                    ChunkPos neighbour = chunk.Offset(dx, dy, dz);
                    if (into.Add(neighbour))
                    {
                        _reachScratch.Add(neighbour);
                    }
                }
            }
        }
    }

    private readonly List<ChunkPos> _reachScratch = [];
    private int _reachRingStart;

    /// <summary>Records that this voxel's chunk received water or an edit on this tick.</summary>
    private void NoteInflow(VoxelPos voxel)
    {
        ChunkPos chunk = voxel.Chunk;
        _inflowTick[chunk] = Ticks;
        if (_inflowCacheValid && _inflowCacheKey == chunk)
        {
            _inflowCacheValue = Ticks;
        }
    }

    /// <summary>
    /// The tick this chunk last received on, or <see cref="NeverInflowed"/>.
    /// </summary>
    /// <remarks>
    /// One-entry cache, because the sheet flood asks per cell and a sheet's cells arrive in
    /// spatial order — the same reason <c>FluidGrid</c> caches its panel lookup. Without it a
    /// 162,572-cell sheet is that many dictionary probes to <i>decide</i> whether to level.
    /// </remarks>
    internal long InflowTickAt(ChunkPos chunk)
    {
        if (_inflowCacheValid && _inflowCacheKey == chunk)
        {
            return _inflowCacheValue;
        }

        _inflowCacheKey = chunk;
        _inflowCacheValue = _inflowTick.TryGetValue(chunk, out long tick) ? tick : NeverInflowed;
        _inflowCacheValid = true;
        return _inflowCacheValue;
    }

    /// <summary>Drops a chunk's inflow stamp. Called where the grid drops the chunk.</summary>
    private void ForgetInflow(ChunkPos chunk)
    {
        _inflowTick.Remove(chunk);
        if (_inflowCacheValid && _inflowCacheKey == chunk)
        {
            _inflowCacheValid = false;
        }
    }

    /// <summary>Queues a cell for the next tick. True when it was not queued already.</summary>
    private bool Wake(VoxelPos cell)
    {
        // A cell that can hold water is worth a visit; so is one that cannot but still contains
        // some, because that is the only path by which displaced water gets out of new rock.
        if (!_terrain.CanHold(cell) && _grid.AmountAt(cell) == 0)
        {
            return false;
        }

        if (_queued.Add(cell))
        {
            _pending.Add(cell);
            return true;
        }

        return false;
    }
}
