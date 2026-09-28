using System.Diagnostics;
using Overburden.Core.Voxels;
using Overburden.Core.Diagnostics;

namespace Overburden.Core.Fluid;

/// <summary>What one pass of <see cref="FluidSimulation.LevelSheets()"/> did.</summary>
/// <param name="Sheets">Sheets found from the seeds, including the ones left alone.</param>
/// <param name="Levelled">Sheets distributed evenly, to within a millilitre a pair.</param>
/// <param name="Packed">Sheets whose share was under a litre a cell, packed into as few cells as hold it.</param>
/// <param name="Skipped">Sheets with no move available inside them, left exactly as they were.</param>
/// <param name="UnderShare">
/// Sheets with a move inside them but fewer unsettled cells than one in <i>share</i>, left to the
/// automaton — most of them without their flood finished. The lever's own count.
/// </param>
/// <param name="Stepped">
/// Sheets levelled for a step between settled plateaus although they were under the share.
/// </param>
/// <param name="LargestSheet">Cells in the largest sheet found whole. The cost of one solve.</param>
/// <param name="CellsVisited">
/// Cells the pass read, over every sheet it found, whole or not. The cost of the pass, and the
/// number the share bounds.
/// </param>
/// <param name="CellsWritten">Cells whose contents changed.</param>
/// <param name="MillilitresMoved">Millilitres transferred between cells of a sheet. Not a change in the world's water.</param>
/// <param name="CellsWoken">Cells queued for the next tick because the solve gave them something to do.</param>
/// <param name="Seeds">Cells the pass was seeded with, before any was tested as a sheet cell.</param>
/// <param name="GatherMillis">Building the seed list from the pending list and the carry.</param>
/// <param name="SortMillis">Copying and ordering the seeds by (Y, X, Z).</param>
/// <param name="FloodMillis">The floods, every Y: finding the sheets and judging the share.</param>
/// <param name="LevelMillis">The rest: the fed test, the targets, the transfers, the wake and the rested list.</param>
/// <param name="Capped">The pass stopped at its cap and left the heights above for the next pass.</param>
/// <param name="SeedsDeferred">Seeds at heights this pass did not reach — below where it resumed, or above where it stopped.</param>
/// <param name="Advanced">Dry cells the front advance added to the sheets it levelled (FRONT-ADVANCE-PLAN).</param>
/// <param name="InflowUnderTenthPercent">Sheets tested for feeding whose inflow in the window was under 0.1% of them.</param>
/// <param name="InflowUnderOnePercent">… 0.1% to under 1%.</param>
/// <param name="InflowUnderTenPercent">… 1% to under 10%.</param>
/// <param name="InflowTenPercentPlus">… 10% or more.</param>
/// <param name="InflowDisturbed">… disturbed in the window — an edit or a fall — which is fed whatever its volume.</param>
/// <param name="TrickleLevelled">Sheets with inflow under the fed share, levelled rather than left: what slice 2 recovers.</param>
/// <param name="FedAdvanced">Sheets still being fed whose tip was advanced (slice 2c) rather than left alone.</param>
/// <param name="FedAdvancedCells">Dry cells those sheets took.</param>
/// <param name="FedBandCells">Old cells of those sheets levelled with the new ones — the tips' band.</param>
/// <remarks>
/// The four phases exist because M10d-3's first number (2026-09-24, a copy of the retired spread
/// world) read 810 ms a pass from 1.58 M seeds against 486 k cells visited — and the recorded
/// levers all assumed the cost was the sheets. A phase split says which it is before one is chosen.
/// </remarks>
public readonly record struct SheetLevelResult(
    int Sheets, int Levelled, int Packed, int Skipped, int UnderShare, int Stepped, int LargestSheet,
    int CellsVisited, int CellsWritten, long MillilitresMoved, int CellsWoken, int Noisy,
    int Seeds = 0, double GatherMillis = 0, double SortMillis = 0, double FloodMillis = 0,
    double LevelMillis = 0, bool Capped = false, int SeedsDeferred = 0, int Advanced = 0,
    int InflowUnderTenthPercent = 0, int InflowUnderOnePercent = 0, int InflowUnderTenPercent = 0,
    int InflowTenPercentPlus = 0, int InflowDisturbed = 0, int TrickleLevelled = 0,
    int FedAdvanced = 0, int FedAdvancedCells = 0, int FedBandCells = 0)
{
    /// <summary>A pass that found nothing to change.</summary>
    public bool Idle => CellsWritten == 0;
}

/// <summary>
/// The analytic settle: what the automaton would converge to, solved in one pass instead of
/// reached by diffusion.
/// </summary>
/// <remarks>
/// <para>
/// <b>A solve of <i>sheets</i>, not of the region, because the region's flood-fill would teleport
/// water.</b> "Flood the connected open cells, sort by Y, pour from the bottom" fills the lowest
/// reachable cell first whatever water can reach it — two pockets off one junction, one high and
/// one low, would have the high one's water moved into the low one through rock, with no path
/// the automaton could take, and visibly (M8-PLAN, M8b-3 finding 5). The no-siphons rule is the
/// constraint. What the automaton converges to, and what its O(n²) is spent on, is a
/// <b>sheet</b>: the wet cells at one Y that are sideways-connected and each supported — the cell
/// below full or unholdable. A sheet's rest state is level, so the solve distributes its total
/// evenly across its own cells, exact to a millilitre, along a path the sideways rule already
/// uses. Components are found by a flood over wet cells at one Y, O(n) a sheet, no sort.
/// </para>
/// <para>
/// <b>The sheet is the wet cells only, never the dry cells it could still spread into.</b> A
/// settle that snapped water across a floor the frontier had not reached would put water where
/// nothing had carried it; the frontier keeps creeping at the automaton's own pace, the sheet
/// behind it is level at once, and a pass a few ticks later moves the front on again. Cells that
/// can no longer hold water are being displaced and are not a sheet's; cells with room below are
/// falling and are not either — both are the automaton's business the next tick.
/// </para>
/// <para>
/// <b>A sheet whose even share would be under a litre a cell is packed, not spread.</b> Its water
/// goes into as few of its own cells as will hold it at one millilitre under the spread threshold
/// each, the cells that held most first — because an even film over every cell is the puddle
/// regime <see cref="FluidConstants.MinimumSpreadMillilitres"/> was designed against, and the
/// automaton's own answer there is a puddle. A cell at 999 mL cannot spread, so a packed sheet is
/// quiescent whatever stands beside it. A film every cell of which is already under the threshold
/// has no move in it and is not packed either — it is at rest by the automaton's rule, and the
/// next paragraph applies.
/// </para>
/// <para>
/// <b>Quiescent by <see cref="FluidRules"/>, and a sheet with no move inside it is left alone.</b>
/// Two settled neighbours may legitimately differ by up to four millilitres, and re-levelling
/// such a sheet to within one would write every cell to move water nobody could see; so a sheet
/// in which no pair can transfer is skipped, written nowhere and woken nowhere. That is what
/// makes a second pass over a levelled sheet a no-op, and what keeps a quiescent puddle a
/// puddle. <b>What the solve wakes is exactly what it gave a move</b>: the cells of a changed
/// sheet that border a holdable cell outside it (the front, which may now spread), and the cell
/// above any cell it drew down (which may now fall). The interior of a level sheet has no move
/// and is not woken — that is the whole saving — and it is <i>listed as resting</i> instead, so
/// the surface hand-off, which reads that list, can take it at the same boundary; see
/// <see cref="ListRested"/>. <see cref="VerifyQuiescent"/> is the oracle that says the wake set
/// is complete.
/// </para>
/// <para>
/// <b>A sheet with a move inside it is re-levelled only when the disturbance is a share of it, and
/// that is the lever the first play fired.</b> The played world's mine became one 162,572-cell
/// lake, and its edge — a few hundred cells draining into the unloaded world — made one pair
/// unsettled at every boundary, for which the whole sheet was flooded and written twice a second
/// for as long as the edge crept. The automaton levels a local disturbance itself: a transfer
/// wakes its neighbourhood, the wave inward decays by a fifth a cell and dies within five, and
/// nothing behind it is woken. What diffusion cannot do in reasonable time is a disturbance that
/// is <i>wide</i>, and width is what the share measures — a sheet is worth a solve when at least
/// one cell in <i>share</i> of it is unsettled, meaning it has a transferable pair inside the
/// sheet. Zero is no threshold, the behaviour before the lever and the control arm.
/// </para>
/// <para>
/// <b>The share is decided without flooding the sheet, and the wake rule is why that is
/// sound.</b> A transfer wakes both of its cells and every neighbour of the source
/// (<see cref="Record"/>), a draw wakes the cell and its neighbours (<see cref="Take"/>), and a
/// pour wakes what it lands on — so at a tick boundary every cell with a move has an awake cell
/// beside it or is one. The seeds are the awake cells; a flood from all of them at once has met
/// every unsettled cell of every sheet by its second layer, and from there the unsettled count
/// only stands still while the cell count grows. A sheet whose count has reached <i>share</i>
/// times its unsettled cells is under the share whatever the rest of it holds, and its flood is
/// stopped there — <b>frozen</b>, its frontier kept — so the cost of leaving a lake alone is
/// <i>share</i> times its disturbance rather than the lake. Two floods that meet are one sheet:
/// they are joined, a frozen one is thawed and expanded again, and the joined sheet is judged
/// on the joined counts. The bound is exactly as sound as the wake rule; the one thing that
/// would break it is an unsettled cell no awake cell neighbours, which is the defect
/// <see cref="VerifyQuiescent"/> exists to catch and would then miss a solve rather than water.
/// <b>Hand-picked seeds do not carry that guarantee</b> — a caller naming one cell of a sheet
/// (M8b-4's unload method) must pass a share of zero and take the whole flood.
/// </para>
/// <para>
/// <b>A step between two settled plateaus is levelled whatever the share, because it is the one
/// disturbance that is narrow and not local.</b> Two puddles levelled apart, at different
/// heights, creep and meet along a line — a few cells of a big sheet, and the cell share would
/// leave it to diffuse for minutes, the O(n²) the settle exists against. The settled cells that
/// border the disturbance tell the two cases apart: a creeping edge is bordered on one side by an
/// interior that agrees about the level, and a merge is bordered on both sides by interiors that
/// do not. A sheet whose bordering settled cells span more than
/// <see cref="StepMillilitres"/> is levelled whole. That constant is an estimate: at rest two
/// neighbours differ by at most four millilitres, so eight pairs' worth is far more than any
/// resting tilt across the width of a disturbance and far less than any step a person could see.
/// </para>
/// <para>
/// <b>Bottom-up, because levelling a lower sheet can unsupport a higher one.</b> Seeds are taken in
/// ascending Y, so a sheet at Y is found against the grid as the sheets below it left it: a cell
/// whose floor was drawn down is not supported any more, is left out, and falls next tick. Volume
/// is conserved by construction — every move is a <see cref="FluidGrid.Transfer"/> between two
/// cells of one sheet — so the conservation identity needs no new term.
/// </para>
/// </remarks>
public sealed partial class FluidSimulation
{
    /// <summary>The most a packed cell holds: one millilitre under the spread threshold.</summary>
    public const int PackedCellMillilitres = FluidConstants.MinimumSpreadMillilitres - 1;

    /// <summary>
    /// How far the settled cells bordering a disturbance may disagree before the sheet is
    /// levelled for the step whatever its share. Eight resting pairs' worth; an estimate.
    /// </summary>
    public const int StepMillilitres = 8 * (FluidConstants.SpreadDivisor - 1);

    private const byte CellPopped = 1;
    private const byte CellUnsettled = 2;
    private const byte CellFront = 4;

    // One record per cell a pass reads, in discovery order, kept across passes so a pass over a
    // flooded mine allocates nothing. A sheet holds indices into these.
    private VoxelPos[] _cellPos = new VoxelPos[256];
    private int[] _cellAmount = new int[256];
    private int[] _cellSheet = new int[256];
    private int[] _cellTarget = new int[256];
    private byte[] _cellFlags = new byte[256];
    private int _cellCount;
    private readonly Dictionary<VoxelPos, int> _cellIndex = [];
    private readonly FloodIndex _floodIndex = new();

    /// <summary>
    /// The union-find's parents, flat and indexed by sheet — the classic shape, and Matthew's
    /// direct-indexed table applied to the sheets rather than the cells. <c>Find</c> walked
    /// <c>Sheet</c> objects out of a list until 2026-09-26, 12% of the flood's profile.
    /// </summary>
    private int[] _parent = new int[64];

    /// <summary>Per flood record, the next cell of its sheet's chain, or -1.</summary>
    private int[] _cellNext = new int[256];

    /// <summary>A sheet's cells as a list, for the two passes that address them by position.</summary>
    private readonly List<int> _sheetCells = [];

    /// <summary>The sheet's chain as an indexable list, in flood order. Reused; valid until the next call.</summary>
    private List<int> CellsOf(Sheet sheet)
    {
        _sheetCells.Clear();
        for (int record = sheet.Head; record >= 0; record = _cellNext[record])
        {
            _sheetCells.Add(record);
        }

        return _sheetCells;
    }
    private bool _hashedThisPass;

    private readonly List<Sheet> _sheets = [];
    private int _sheetCount;
    private List<int> _layer = [];
    private List<int> _nextLayer = [];
    private readonly List<VoxelPos> _sheetSeeds = [];
    private long[] _seedKeys = new long[256];

    // The pass cap (M10d-3, decision 4): the height a capped pass stopped at and the next resumes
    // from, int.MinValue for the bottom; and the heights the last pass covered, [lo, hi), which is
    // what MarkCarryOffered may mark offered.
    private int _solveCursorY = int.MinValue;
    private int _processedLo = int.MinValue;
    private int _processedHi = int.MaxValue;

    // The front advance (docs/FRONT-ADVANCE-PLAN.md): rings a levelled sheet may take this pass,
    // set for the length of one pass; and the ring flood's scratch.
    private int _advanceRings;
    private List<VoxelPos> _ring = [];
    private List<VoxelPos> _ringNext = [];
    private readonly HashSet<VoxelPos> _ringSeen = [];

    /// <summary>
    /// The thinnest a sheet may be spread by the front advance: twice the spread threshold, so a
    /// pour ends as a puddle whose edge can still creep, never as a film the next pass packs.
    /// An estimate (FRONT-ADVANCE-PLAN decision 3).
    /// </summary>
    public const int AdvanceFloorMillilitres = 2 * FluidConstants.MinimumSpreadMillilitres;

    /// <summary>
    /// The fed share the game ships with (FRONT-ADVANCE-PLAN slice 2, Matthew's word 2026-09-24): a
    /// sheet is still being fed when the water that entered its chunks within the quiet window is
    /// at least this percent of the sheet. <b>An estimate</b> until the play's inflow split says
    /// otherwise.
    /// </summary>
    public const double DefaultFedSharePercent = 1.0;

    /// <summary>
    /// The fed share in percent; zero is the rule before slice 2 — any inflow at all, a rain drop or
    /// a sweated litre, makes every sheet in that chunk fed — and is the default, so every figure
    /// recorded before it reproduces. The game sets <see cref="DefaultFedSharePercent"/>. Set once,
    /// before any tick; the worker's solve reads it.
    /// </summary>
    public double FedSharePercent { get; set; }

    // Scratch for IsBeingFed: the chunks of one sheet counted once each.
    private readonly HashSet<ChunkPos> _fedChunks = [];

    // Whether the sheet IsBeingFed last judged was fed by a disturbance, which 2c never advances.
    private bool _fedDisturbed;

    /// <summary>
    /// Whether a sheet still being fed takes the front advance's rings by levelling its tip
    /// (FRONT-ADVANCE-PLAN slice 2c). False is the rule before it — a fed sheet is left to the
    /// automaton — and the default, so every figure recorded before it reproduces; the game sets
    /// <see cref="DefaultFedAdvance"/>. Needs a front advance to do anything. Set once, before any
    /// tick; the worker's solve reads it.
    /// </summary>
    public bool FedAdvance { get; set; }

    /// <summary>What the game ships (Matthew's word, 2026-09-24): fed sheets advance.</summary>
    public const bool DefaultFedAdvance = true;

    /// <summary>
    /// How deep a fed sheet's tip band is, in rings from its dry edge, per ring of advance: 6, so
    /// four rings level the last twenty-four — six metres. <b>Matthew's play of 2026-09-24 on his
    /// breach pit at <c>--fedband=6</c>: <i>"that hits deep i like it a lot will have to do more
    /// load testing like but i think its the one"</i></b> — shipped 4 an hour before it, the middle
    /// of the sweep below; the revisit trigger is his load testing. <b>The pass's 2 was an estimate and the
    /// bench (<c>--front --frontfed</c>, 2026-09-24) said it was shallow</b>: a natural stream's tip
    /// is thin, so the band has to reach back into thicker water before the floor lets it take a
    /// ring. At 100 L/s for 60 s, the open floor's radius went 17 (off) / 18 (2) / 21 (3) / 24 (4) /
    /// 30 (6), the corridor's reach at 10 s 8 / 12 / 17 / 20 / 30 cells, nothing flowing back at
    /// 3 and deeper. Deeper kept paying; how flat a tip may look is the eye's, which is why the
    /// game has <c>--fedband=</c>.
    /// </summary>
    public const int DefaultFedBandRingsPerAdvance = 6;

    /// <summary>The band's depth per ring of advance; <c>--fedband=</c>. Set once, before any tick.</summary>
    public int FedBandRingsPerAdvance { get; set; } = DefaultFedBandRingsPerAdvance;

    private const byte CellBand = 8;
    private readonly List<int> _band = [];
    private List<int> _bandRing = [];
    private List<int> _bandNext = [];

    // The inflow split of the pass in progress (slice 2a): under 0.1%, 1%, 10%, over, disturbed,
    // and levelled under the share.
    private int _inflowTenth, _inflowOne, _inflowTen, _inflowOver, _inflowDisturbed, _trickleLevelled;
    private readonly List<int> _sheetOrder = [];
    private readonly List<int> _sheetGivers = [];
    private readonly List<int> _sheetTakers = [];

    // How many ticks a sheet's chunks must have gone without receiving before it may be levelled.
    // Zero levels under inflow too. Set for the length of one pass.
    private int _quietTicks;

    // The tick "now" for the quiet test. Ticks at a boundary; on the worker the solve runs before
    // Complete's Ticks++, so it is told Ticks + 1 and the quiet count reads as the sync arm's.
    private long _solveTick;

    /// <summary>
    /// One flood's bookkeeping. <see cref="Parent"/> is union-find: a sheet whose parent is not
    /// itself was joined to another and its lists are empty.
    /// </summary>
    /// <summary>
    /// Cells a solve's writes woke that were not awake already, over the lifetime (2026-09-28, water
    /// bodies part a: is the solve under the near/far order waking the pool it levels?). Levelled
    /// and packed sheets only; the fed advance's wakes are not in it.
    /// </summary>
    public long SolveCellsWoken { get; private set; }

    /// <summary>Sheets levelled or packed whose first cell is far from the viewer, over the lifetime. Zero with no near/far order.</summary>
    public long SolveFarSheetsLevelled { get; private set; }

    /// <summary>The part of <see cref="SolveCellsWoken"/> woken by those far sheets.</summary>
    public long SolveFarCellsWoken { get; private set; }

    private sealed class Sheet
    {
        /// <summary>
        /// The sheet's cells as a chain through <c>_cellNext</c>, in flood order: first, last and
        /// how many. A chain rather than a list so a merge is one link rather than a copy of the
        /// smaller sheet's cells (the flood's profile, 2026-09-26: ~10% of it was that copy).
        /// </summary>
        public int Head = -1;
        public int Tail = -1;
        public int Count;
        public readonly List<int> Frontier = [];
        public long Total;
        public int Unsettled;
        public bool Frozen;
        public int BorderLow;
        public int BorderHigh;

        public void Reset()
        {
            Head = -1;
            Tail = -1;
            Count = 0;
            Frontier.Clear();
            Total = 0;
            Unsettled = 0;
            Frozen = false;
            BorderLow = int.MaxValue;
            BorderHigh = int.MinValue;
        }

        public bool Stepped => BorderLow != int.MaxValue && BorderHigh - BorderLow > StepMillilitres;

        /// <summary>Notes a settled cell bordering the disturbance.</summary>
        public void Border(int amount)
        {
            BorderLow = Math.Min(BorderLow, amount);
            BorderHigh = Math.Max(BorderHigh, amount);
        }
    }

    /// <summary>
    /// Cells queued for the next tick. Valid at a tick boundary, which is the only time the
    /// question means anything; the seeds of <see cref="LevelSheets()"/>.
    /// </summary>
    /// <remarks>
    /// <b>Includes the far cells the near/far order deferred past the boundary</b>, because they
    /// are queued for a later tick rather than settled: a far sheet whose every cell is carried
    /// would otherwise be invisible to the solve, and the share rule's early stop assumes each
    /// unsettled cell neighbours a seed. Costs nothing while nothing is deferred, which is the
    /// unordered arm and every bench figure.
    /// </remarks>
    public IReadOnlyList<VoxelPos> Pending
    {
        get
        {
            if (FarBacklog == 0)
            {
                return _pending;
            }

            _seedScratch.Clear();
            _seedScratch.AddRange(_pending);
            for (int i = _farHead; i < _farCarry.Count; i++)
            {
                _seedScratch.Add(_farCarry[i]);
            }

            return _seedScratch;
        }
    }

    /// <summary>
    /// Levels every sheet the active set touches whose disturbance is at least one cell in
    /// <paramref name="share"/> of it, or a step. At a tick boundary only. Zero is no threshold.
    /// </summary>
    /// <remarks>
    /// Seeded from <see cref="Pending"/> rather than from every held cell, because a sheet with no
    /// awake cell has no move inside it — its cells were each visited and found nothing to do —
    /// and would be skipped anyway. The corridor that stays awake for hours is a few thousand
    /// cells found from a few hundred seeds; a flooded mine's floor is one sheet found from its
    /// front.
    /// </remarks>
    public SheetLevelResult LevelSheets(int share = 0, int quietTicks = 0, int cap = 0, int advance = 0)
    {
        RefuseMidTick(nameof(LevelSheets));
        if (TickInProgress)
        {
            throw new InvalidOperationException(
                $"LevelSheets asked mid-tick, with {RemainingInTick} cell(s) still to visit; "
                + "finish the tick with Step() first.");
        }

        return SolveFromActiveSet(share, quietTicks, Ticks, cap, advance);
    }

    /// <summary>
    /// Levels every sheet reachable from the given cells, under the same share rule. At a tick
    /// boundary only.
    /// </summary>
    /// <remarks>
    /// The unload-time method: M8b-4 runs it over the cells of a departing chunk so that what the
    /// store writes is at rest. A seed that holds nothing, cannot hold, or has room below is not a
    /// sheet cell and starts nothing. <b>With seeds that are not the awake set, pass a share of
    /// zero</b>: the early stop assumes every unsettled cell neighbours a seed, which only the
    /// wake rule guarantees.
    /// </remarks>
    public SheetLevelResult LevelSheets(
        IReadOnlyList<VoxelPos> seeds, int share = 0, int quietTicks = 0, int advance = 0)
    {
        ArgumentNullException.ThrowIfNull(seeds);
        if (TickInProgress)
        {
            throw new InvalidOperationException(
                $"LevelSheets asked mid-tick, with {RemainingInTick} cell(s) still to visit; "
                + "finish the tick with Step() first.");
        }

        return Solve(seeds, share, quietTicks, Ticks, cap: 0, advance);
    }

    /// <summary>
    /// The solve at the tail of a job, on the worker (slice 3c): over the next tick's set as the
    /// visits left it, told the tick number the boundary will have, so the quiet test and the
    /// order — settle, then hand-off, drain, pour, the next tick — are the sync arm's exactly.
    /// </summary>
    internal SheetLevelResult SolveOnWorker(int share, int quietTicks, int cap = 0, int advance = 0) =>
        SolveFromActiveSet(share, quietTicks, Ticks + 1, cap, advance);

    /// <summary>
    /// The solve over the active set: seeded from the pending list and the carry's changed part
    /// (see the near field's remarks — a carried cell is offered once, and again when woken
    /// again), the carry marked offered after.
    /// </summary>
    private SheetLevelResult SolveFromActiveSet(int share, int quietTicks, long nowTick, int cap, int advance)
    {
        long before = Stopwatch.GetTimestamp();
        IReadOnlyList<VoxelPos> seeds = SolveSeeds();
        double gather = Millis(before);
        SheetLevelResult result = Solve(seeds, share, quietTicks, nowTick, cap, advance);
        MarkCarryOffered();
        return result with { GatherMillis = gather };
    }

    /// <summary>The tick number the last solve tested quiet against. Tests only: it pins the worker's +1.</summary>
    internal long LastSolveTick => _solveTick;

    private static readonly int WatchSolve = CallWatch.Register("sheet solve: a pass");

    private SheetLevelResult Solve(
        IReadOnlyList<VoxelPos> seeds, int share, int quietTicks, long nowTick, int cap, int advance)
    {
        using CallWatch.Scope callWatch = CallWatch.Time(WatchSolve);
        ArgumentOutOfRangeException.ThrowIfNegative(advance);
        _advanceRings = advance;
        ArgumentOutOfRangeException.ThrowIfNegative(share);
        ArgumentOutOfRangeException.ThrowIfNegative(quietTicks);
        ArgumentOutOfRangeException.ThrowIfNegative(cap);
        _quietTicks = quietTicks;
        _solveTick = nowTick;
        long started = Stopwatch.GetTimestamp();

        // A copy, because waking appends to the pending list this is usually a view of; and a
        // total order on position rather than the caller's order, so the same water in the same
        // cells solves the same way whichever cell happened to wake first.
        // Counted now: the seeds are usually a view of the pending list, which the wakes below append to.
        int offered = seeds.Count;
        int fromY = cap > 0 ? ResumeHeight(seeds) : int.MinValue;
        SortSeeds(seeds, fromY);
        int deferred = offered - _sheetSeeds.Count;
        bool capped = false;
        int stopY = int.MaxValue;
        long items = 0;

        int sheets = 0, levelled = 0, packed = 0, skipped = 0, underShare = 0, stepped = 0, noisy = 0;
        int largest = 0, visited = 0, written = 0, woken = 0, advanced = 0;
        int fedAdvanced = 0, fedAdvancedCells = 0, fedBand = 0;
        _inflowTenth = _inflowOne = _inflowTen = _inflowOver = _inflowDisturbed = _trickleLevelled = 0;
        long moved = 0;
        double sortMillis = Millis(started);
        long floodTicks = 0;

        // One flood per Y, bottom-up: the seeds of a higher sheet are tested as sheet cells only
        // after every sheet below has been applied.
        int groupStart = 0;
        while (groupStart < _sheetSeeds.Count)
        {
            int y = _sheetSeeds[groupStart].Y;
            int groupEnd = groupStart;
            while (groupEnd < _sheetSeeds.Count && _sheetSeeds[groupEnd].Y == y)
            {
                groupEnd++;
            }

            long floodStart = Stopwatch.GetTimestamp();
            Flood(groupStart, groupEnd, share);
            floodTicks += Stopwatch.GetTimestamp() - floodStart;
            visited += _cellCount;

            for (int s = 0; s < _sheetCount; s++)
            {
                Sheet sheet = _sheets[s];
                if (_parent[s] != s)
                {
                    continue;
                }

                sheets++;
                int count = sheet.Count;
                if (sheet.Frozen)
                {
                    underShare++;
                    continue;
                }

                largest = Math.Max(largest, count);
                if (count == 1 || sheet.Unsettled == 0)
                {
                    skipped++;
                    continue;
                }

                bool wideEnough = WorthLevelling(share, count, sheet.Unsettled);
                if (!wideEnough && !sheet.Stepped)
                {
                    underShare++;
                    continue;
                }

                if (!wideEnough)
                {
                    stepped++;
                }

                // Still being fed? Asked here rather than earlier so the share keeps its early-out
                // — the flood is not paid to decide — and as one walk of the sheet's own cells
                // rather than a field the flood maintains. <b>An accumulated field made the answer
                // depend on Union taking the max, and a battery recipe that dropped it could not be
                // caught</b>: which half of a merge survives is the larger cell count, so a fixture
                // cannot reliably put the fed half on the losing side. There is no merge to get
                // wrong now.
                if (IsBeingFed(sheet))
                {
                    // Slice 2c: a stream still fed takes the same rings, and levels only its tip —
                    // the band nearest its dry edge with the new cells — leaving its body's
                    // gradient to the millilitre. Never a disturbance: that is terrain changing.
                    if (FedAdvance && _advanceRings > 0 && !_fedDisturbed
                        && AdvanceFed(s, sheet, out int band) is int fedCells and > 0)
                    {
                        fedAdvanced++;
                        fedAdvancedCells += fedCells;
                        fedBand += band;
                        moved += Apply(sheet, out int fedWritten);
                        written += fedWritten;
                        woken += WakeAfterFedAdvance(sheet);

                        // Nothing listed as resting: a fed sheet is not at rest, and the hand-off
                        // reads that list (FRONT-ADVANCE-PLAN 2c, decision 5).
                        continue;
                    }

                    noisy++;
                    continue;
                }

                // The front advance, on a sheet this pass is levelling anyway: rings of dry,
                // supported floor beside it join it before its targets are set, so the level
                // below spreads over them too. Never for a pack — a sheet that thin has no
                // water to lend a ring.
                if (_advanceRings > 0 && sheet.Total >= (long)AdvanceFloorMillilitres * (count + 1))
                {
                    advanced += Advance(s, sheet, sheet.Total, count);
                    count = sheet.Count;
                }

                if (sheet.Total <= (long)PackedCellMillilitres * count)
                {
                    PackTargets(sheet);
                    packed++;
                }
                else
                {
                    LevelTargets(sheet);
                    levelled++;
                }

                moved += Apply(sheet, out int cellsWritten);
                written += cellsWritten;
                int sheetWoken = WakeAfterSolve(sheet);
                woken += sheetWoken;
                SolveCellsWoken += sheetWoken;
                if (_nearRings > 0 && !IsNear(_cellPos[sheet.Head]))
                {
                    SolveFarSheetsLevelled++;
                    SolveFarCellsWoken += sheetWoken;
                }

                ListRested(sheet);
            }

            items += (groupEnd - groupStart) + _cellCount;
            groupStart = groupEnd;
            if (cap > 0 && items >= cap && groupStart < _sheetSeeds.Count)
            {
                // Stop at a height, never inside one: a sheet is one Y and its flood takes every
                // seed of that Y at once, which is what the share's early stop relies on.
                capped = true;
                stopY = _sheetSeeds[groupStart].Y;
                deferred += _sheetSeeds.Count - groupStart;
                break;
            }
        }

        _processedLo = fromY;
        _processedHi = stopY;
        if (cap > 0)
        {
            _solveCursorY = capped ? stopY : int.MinValue;
        }

        double floodMillis = floodTicks * 1000.0 / Stopwatch.Frequency;
        return new SheetLevelResult(
            sheets, levelled, packed, skipped, underShare, stepped, largest, visited, written, moved,
            woken, noisy, offered, 0, sortMillis, floodMillis,
            Math.Max(0, Millis(started) - sortMillis - floodMillis), capped, deferred, advanced,
            _inflowTenth, _inflowOne, _inflowTen, _inflowOver, _inflowDisturbed, _trickleLevelled,
            fedAdvanced, fedAdvancedCells, fedBand);
    }

    /// <summary>
    /// Fills <see cref="_sheetSeeds"/> with the seeds in (Y, X, Z) order: a primitive sort over
    /// packed keys, or the comparator for a pass holding a position the key cannot carry.
    /// </summary>
    private void SortSeeds(IReadOnlyList<VoxelPos> seeds, int fromY = int.MinValue)
    {
        int count = 0;
        if (_seedKeys.Length < seeds.Count)
        {
            _seedKeys = new long[Math.Max(seeds.Count, _seedKeys.Length * 2)];
        }

        for (int i = 0; i < seeds.Count; i++)
        {
            VoxelPos seed = seeds[i];
            if (seed.Y < fromY)
            {
                continue;
            }

            if (!SeedOrder.Fits(seed))
            {
                SeedSortFallbacks++;
                _sheetSeeds.Clear();
                foreach (VoxelPos s in seeds)
                {
                    if (s.Y >= fromY)
                    {
                        _sheetSeeds.Add(s);
                    }
                }

                _sheetSeeds.Sort(SeedOrder.Compare);
                return;
            }

            _seedKeys[count++] = SeedOrder.Pack(seed);
        }

        Array.Sort(_seedKeys, 0, count);
        _sheetSeeds.Clear();
        _sheetSeeds.EnsureCapacity(count);
        for (int i = 0; i < count; i++)
        {
            _sheetSeeds.Add(SeedOrder.Unpack(_seedKeys[i]));
        }
    }

    /// <summary>
    /// Where a capped pass starts: the height the last one stopped at, or the bottom when nothing
    /// is left at or above it — so a cursor over water that has since drained wraps at once
    /// rather than spending a pass on nothing.
    /// </summary>
    private int ResumeHeight(IReadOnlyList<VoxelPos> seeds)
    {
        if (_solveCursorY == int.MinValue)
        {
            return int.MinValue;
        }

        for (int i = 0; i < seeds.Count; i++)
        {
            if (seeds[i].Y >= _solveCursorY)
            {
                return _solveCursorY;
            }
        }

        _solveCursorY = int.MinValue;
        return int.MinValue;
    }

    /// <summary>Whether the last pass covered this height — what the carry may be marked offered for.</summary>
    private bool CoveredLastPass(int y) => y >= _processedLo && y < _processedHi;

    /// <summary>The height the next capped pass resumes from; <see cref="int.MinValue"/> for the bottom. Tests only.</summary>
    internal int SolveCursorY => _solveCursorY;

    /// <summary>
    /// Passes whose seeds held a position outside <see cref="SeedOrder"/>'s range and were
    /// sorted by the comparator instead. Zero on any world a body can reach.
    /// </summary>
    public long SeedSortFallbacks { get; private set; }

    private static double Millis(long since) =>
        (Stopwatch.GetTimestamp() - since) * 1000.0 / Stopwatch.Frequency;

    /// <summary>
    /// Whether any chunk this sheet stands in has received within the wait.
    /// </summary>
    /// <remarks>
    /// <b>Age greater than K, not at least K</b>, because that is what the world counter this
    /// replaced meant: inflow during a tick made that boundary's count zero, so K = 2 fired on the
    /// third boundary after a pour. <c>--settleafter=</c> has recorded play figures against it —
    /// 80 s at 2 against 12 s at 0 — and a rule that quietly moved the number by one would have
    /// invalidated them. Levelling a sheet under inflow is what makes a stream down a tunnel rise
    /// as a flat sheet instead of flowing, which is the arm <c>--settleafter=0</c> keeps.
    /// </remarks>
    /// <remarks>
    /// <para>
    /// <b>Slice 2 (FRONT-ADVANCE-PLAN): fed means the inflow is a share of the sheet.</b> Until then any
    /// stamp on any of the sheet's chunks within the window made it fed — chunk-granular and
    /// volume-blind, so one rain drop or one sweated litre anywhere in an 8 m chunk left every sheet
    /// in it unlevelled, and the play's settle line read 184,380 fed against 39,735 levelled. Now the
    /// litres that entered the sheet's distinct chunks within the window are summed and compared with
    /// <see cref="FedSharePercent"/> of its total; a disturbance (an edit, a fall's re-pour) is fed
    /// whatever its volume, as a changed terrain always was. Zero is the old rule. A window longer
    /// than the ring keeps the old rule too, because the ring cannot say what came in before it.
    /// </para>
    /// <para>
    /// Walks the whole sheet rather than stopping at the first fed cell, because the answer is a sum
    /// now; the flood has already paid for every cell, and consecutive cells share a chunk.
    /// </para>
    /// </remarks>
    private bool IsBeingFed(Sheet sheet)
    {
        if (_quietTicks <= 0)
        {
            return false;
        }

        _fedDisturbed = false;
        _fedChunks.Clear();
        long inflow = 0;
        bool any = false, disturbed = false;
        for (int record = sheet.Head; record >= 0; record = _cellNext[record])
        {
            ChunkPos chunk = _cellPos[record].Chunk;
            long stamp = InflowTickAt(chunk);
            if (stamp == NeverInflowed || _solveTick - stamp > _quietTicks || !_fedChunks.Add(chunk))
            {
                continue;
            }

            any = true;
            inflow += InflowWithin(chunk, _solveTick, _quietTicks, out bool d);
            disturbed |= d;
        }

        if (!any)
        {
            return false;
        }

        double percent = sheet.Total > 0 ? inflow * 100.0 / sheet.Total : double.PositiveInfinity;
        _fedDisturbed = disturbed;
        if (disturbed) _inflowDisturbed++;
        else if (percent < 0.1) _inflowTenth++;
        else if (percent < 1.0) _inflowOne++;
        else if (percent < 10.0) _inflowTen++;
        else _inflowOver++;

        if (FedSharePercent <= 0 || disturbed || _quietTicks >= InflowRing.Slots)
        {
            return true;
        }

        bool fed = percent >= FedSharePercent;
        if (!fed)
        {
            _trickleLevelled++;
        }

        return fed;
    }

    /// <summary>The share rule, in one place: at least one cell in <paramref name="share"/> unsettled.</summary>
    private static bool WorthLevelling(int share, int count, int unsettled) =>
        share == 0 || (long)unsettled * share >= count;

    /// <summary>Wet, holdable, and standing on something: a cell a sheet can be made of.</summary>
    private bool IsSheetCell(VoxelPos cell) =>
        _grid.AmountAt(cell) > 0 && _visitTerrain.CanHold(cell) && IsSupported(cell);

    private bool IsSupported(VoxelPos cell)
    {
        VoxelPos below = Below(cell);
        return !_visitTerrain.CanHold(below) || RoomAt(below) == 0;
    }

    private static readonly int WatchFlood = CallWatch.Register("sheet solve: flood one Y");

    /// <summary>
    /// Floods, layer by layer, from every seed of one Y at once, freezing a sheet the moment it
    /// is known to be under the share and joining floods that meet. Fills the records and the
    /// sheets for <see cref="LevelSheets(IReadOnlyList{VoxelPos}, int)"/> to judge.
    /// </summary>
    private void Flood(int seedStart, int seedEnd, int share)
    {
        using CallWatch.Scope callWatch = CallWatch.Time(WatchFlood);
        _cellCount = 0;
        _hashedThisPass = HashedFloodIndex;
        if (_hashedThisPass)
        {
            _cellIndex.Clear();
        }
        else
        {
            _floodIndex.Clear();
        }

        _sheetCount = 0;
        _layer.Clear();
        _nextLayer.Clear();

        for (int i = seedStart; i < seedEnd; i++)
        {
            VoxelPos seed = _sheetSeeds[i];
            if (IndexTryGet(seed, out _) || !IsSheetCell(seed))
            {
                continue;
            }

            int sheet = NewSheet();
            _layer.Add(Discover(seed, sheet));
        }

        int layer = 0;
        while (_layer.Count > 0)
        {
            _nextLayer.Clear();
            foreach (int record in _layer)
            {
                Pop(record);
            }

            layer++;

            // Layers 0 and 1 hold every unsettled cell and layer 2 every settled cell bordering
            // one, so from here a sheet's unsettled count and its border are final and only its
            // size grows. A frontier that belongs to a sheet under the share is held rather than
            // expanded, and given back if a live flood reaches the sheet.
            if (share > 0 && layer >= 3)
            {
                int kept = 0;
                for (int i = 0; i < _nextLayer.Count; i++)
                {
                    int record = _nextLayer[i];
                    Sheet sheet = _sheets[Find(_cellSheet[record])];
                    if (!sheet.Frozen && !sheet.Stepped
                        && !WorthLevelling(share, sheet.Count, sheet.Unsettled))
                    {
                        sheet.Frozen = true;
                    }

                    if (sheet.Frozen)
                    {
                        sheet.Frontier.Add(record);
                    }
                    else
                    {
                        _nextLayer[kept++] = record;
                    }
                }

                _nextLayer.RemoveRange(kept, _nextLayer.Count - kept);
            }

            (_layer, _nextLayer) = (_nextLayer, _layer);
        }
    }

    /// <summary>
    /// Examines one discovered cell: joins it to any sheet it touches, discovers its unvisited
    /// sheet neighbours into the next layer, and settles its own flags.
    /// </summary>
    private void Pop(int record)
    {
        VoxelPos cell = _cellPos[record];
        int amount = _cellAmount[record];
        int root = Find(_cellSheet[record]);
        bool unsettled = false;
        bool front = false;
        Span<int> sides = stackalloc int[4];

        for (int k = 0; k < Sideways.Length; k++)
        {
            (int dx, int dz) = Sideways[k];
            VoxelPos side = cell.Offset(dx, 0, dz);
            if (IndexTryGet(side, out int sideRecord))
            {
                // A member found from another cell, or this cell's own discoverer. Same sheet
                // either way: two floods at one Y that touch are one sheet.
                int sideRoot = Find(_cellSheet[sideRecord]);
                if (sideRoot != root)
                {
                    root = Union(root, sideRoot);
                }

                unsettled |= CanTransfer(amount, _cellAmount[sideRecord]);
                sides[k] = sideRecord;
                continue;
            }

            sides[k] = -1;
            int beside = _grid.AmountAt(side);
            if (beside > 0 && IsSheetCell(side))
            {
                _nextLayer.Add(Discover(side, root));
                unsettled |= CanTransfer(amount, beside);
            }
            else if (_visitTerrain.CanHold(side))
            {
                // Dry, or wet and falling: not the sheet's, but somewhere this cell could
                // spread once it holds more — the front.
                front = true;
            }
        }

        byte flags = CellPopped;
        if (unsettled)
        {
            flags |= CellUnsettled;
        }

        if (front)
        {
            flags |= CellFront;
        }

        _cellFlags[record] = flags;
        Sheet sheet = _sheets[root];
        if (unsettled)
        {
            sheet.Unsettled++;
        }

        // The settled cells bordering the disturbance, seen from whichever side of a pair was
        // examined second, so the order two neighbours are reached in cannot lose one.
        for (int k = 0; k < 4; k++)
        {
            int sideRecord = sides[k];
            if (sideRecord < 0 || (_cellFlags[sideRecord] & CellPopped) == 0)
            {
                continue;
            }

            bool sideUnsettled = (_cellFlags[sideRecord] & CellUnsettled) != 0;
            if (unsettled && !sideUnsettled)
            {
                sheet.Border(_cellAmount[sideRecord]);
            }
            else if (!unsettled && sideUnsettled)
            {
                sheet.Border(amount);
            }
        }
    }

    private int Discover(VoxelPos cell, int sheet)
    {
        if (_cellCount == _cellPos.Length)
        {
            int size = _cellCount * 2;
            Array.Resize(ref _cellPos, size);
            Array.Resize(ref _cellAmount, size);
            Array.Resize(ref _cellSheet, size);
            Array.Resize(ref _cellTarget, size);
            Array.Resize(ref _cellFlags, size);
            Array.Resize(ref _cellNext, size);
        }

        int record = _cellCount++;
        int amount = _grid.AmountAt(cell);
        _cellPos[record] = cell;
        _cellAmount[record] = amount;
        _cellSheet[record] = sheet;
        _cellFlags[record] = 0;
        if (_hashedThisPass)
        {
            _cellIndex[cell] = record;
        }
        else
        {
            _floodIndex.Add(cell, record);
        }

        Sheet owner = _sheets[sheet];
        _cellNext[record] = -1;
        if (owner.Head < 0)
        {
            owner.Head = record;
        }
        else
        {
            _cellNext[owner.Tail] = record;
        }

        owner.Tail = record;
        owner.Count++;
        owner.Total += amount;
        return record;
    }

    private bool IndexTryGet(VoxelPos cell, out int record) =>
        _hashedThisPass ? _cellIndex.TryGetValue(cell, out record) : _floodIndex.TryGet(cell, out record);

    /// <summary>
    /// The flood's index as the dictionary it was before M10d-3's decision 2 — the measurement arm
    /// the two-layer <see cref="FloodIndex"/> is priced against, read at the start of each flood.
    /// Both arms find the same records in the same order, so the water is the same either way.
    /// </summary>
    public bool HashedFloodIndex { get; set; }

    /// <summary>Chunk columns the two-layer index holds a slot for at its widest — 8 KiB each.</summary>
    public int FloodIndexSlots => _floodIndex.SlotsAllocated;

    private int NewSheet()
    {
        if (_sheetCount == _sheets.Count)
        {
            _sheets.Add(new Sheet());
        }

        _sheets[_sheetCount].Reset();
        if (_sheetCount == _parent.Length)
        {
            Array.Resize(ref _parent, _parent.Length * 2);
        }

        _parent[_sheetCount] = _sheetCount;
        return _sheetCount++;
    }

    private int Find(int sheet)
    {
        int root = sheet;
        int[] parent = _parent;
        while (parent[root] != root)
        {
            root = parent[root];
        }

        while (parent[sheet] != root)
        {
            int next = parent[sheet];
            parent[sheet] = root;
            sheet = next;
        }

        return root;
    }

    /// <summary>
    /// Joins two sheets, the smaller into the larger, and returns the survivor. A frozen sheet
    /// that is reached by a live flood is thawed: its held frontier goes back into the next
    /// layer, and the joined sheet is judged again on the joined counts.
    /// </summary>
    private int Union(int a, int b)
    {
        if (_sheets[a].Count < _sheets[b].Count)
        {
            (a, b) = (b, a);
        }

        Sheet big = _sheets[a];
        Sheet small = _sheets[b];
        _parent[b] = a;

        // The smaller chain spliced on after the bigger one's last cell: the same flood order
        // AddRange gave, so levelling's "first cells take the remainder" and packing's ties are
        // unchanged to the millilitre.
        if (small.Count > 0)
        {
            if (big.Head < 0)
            {
                big.Head = small.Head;
            }
            else
            {
                _cellNext[big.Tail] = small.Head;
            }

            big.Tail = small.Tail;
            big.Count += small.Count;
        }

        big.Total += small.Total;
        big.Unsettled += small.Unsettled;
        big.BorderLow = Math.Min(big.BorderLow, small.BorderLow);
        big.BorderHigh = Math.Max(big.BorderHigh, small.BorderHigh);
        _nextLayer.AddRange(big.Frontier);
        _nextLayer.AddRange(small.Frontier);
        big.Frontier.Clear();
        small.Head = -1;
        small.Tail = -1;
        small.Count = 0;
        small.Frontier.Clear();
        big.Frozen = false;
        return a;
    }

    /// <summary>
    /// Whether a sideways pair has a move between it, at the largest divisor there is.
    /// </summary>
    /// <remarks>
    /// The pair-local form on purpose, under either rule: five is the ceiling, so a difference
    /// that moves at five moves at any divisor, and the detector is exact under the fixed rule
    /// and conservative under the counted one — a pair two to four millilitres apart with few
    /// lower sides has a move the detector does not see, and the automaton makes it, at a
    /// millilitre or two a tick, inside the residue the solve leaves anyway. Reading the real
    /// divisor here would need each cell's four sides at flood time for a count the levelling
    /// never uses.
    /// </remarks>
    private static bool CanTransfer(int a, int b) =>
        FluidRules.HorizontalTransfer(a, b) > 0 || FluidRules.HorizontalTransfer(b, a) > 0;

    /// <summary>
    /// The front advance (FRONT-ADVANCE-PLAN): up to <see cref="_advanceRings"/> whole rings of
    /// dry, holdable, supported cells at the sheet's Y, flooded sideways from its front, each taken
    /// only while the sheet's total still covers every cell at <see cref="AdvanceFloorMillilitres"/>.
    /// Returns the cells added; they are the sheet's from here, and <c>Apply</c> fills them.
    /// </summary>
    /// <remarks>
    /// <b>Whole rings</b>, so the new edge is a contour rather than a patch that depends on the
    /// flood's order. <b>A ring stops where the sheet's own definition stops</b> — rock, a lake's
    /// band and an unloaded chunk by <c>CanHold</c>, a lip by <see cref="IsSupported"/> — so water
    /// goes over a drop through the fall rule, never across it. <b>A cell of another sheet at this Y
    /// is not dry</b>: it is in the index, so a gap between two sheets is claimed by whichever is
    /// levelled first and the two are one sheet at the next pass. The front is then recomputed for
    /// the cells whose answer can have changed — the old front and the new cells — so the wake
    /// reaches the new edge and the old edge, now interior, is listed as resting.
    /// </remarks>
    /// <param name="root">The sheet's index.</param>
    /// <param name="sheet">The sheet.</param>
    /// <param name="total">The water the floor is judged on: the sheet's, or its tip band's (2c).</param>
    /// <param name="covered">The cells <paramref name="total"/> already covers.</param>
    private int Advance(int root, Sheet sheet, long total, int covered)
    {
        int start = _cellCount;
        _ring.Clear();
        _ringSeen.Clear();
        for (int record = sheet.Head; record >= 0; record = _cellNext[record])
        {
            if ((_cellFlags[record] & CellFront) != 0)
            {
                _ring.Add(_cellPos[record]);
            }
        }

        for (int r = 0; r < _advanceRings && _ring.Count > 0; r++)
        {
            _ringNext.Clear();
            foreach (VoxelPos from in _ring)
            {
                foreach ((int dx, int dz) in Sideways)
                {
                    VoxelPos side = from.Offset(dx, 0, dz);
                    if (_ringSeen.Contains(side) || IndexTryGet(side, out _)
                        || _grid.AmountAt(side) != 0 || !_visitTerrain.CanHold(side) || !IsSupported(side))
                    {
                        continue;
                    }

                    _ringSeen.Add(side);
                    _ringNext.Add(side);
                }
            }

            if (_ringNext.Count == 0
                || total < (long)AdvanceFloorMillilitres * (covered + (_cellCount - start) + _ringNext.Count))
            {
                break;
            }

            foreach (VoxelPos cell in _ringNext)
            {
                // Two statements, not _cellFlags[Discover(...)]: C# reads the array before the call,
                // and Discover grows the arrays, so the one-liner writes into the old one.
                int record = Discover(cell, root);
                _cellFlags[record] = CellPopped;
            }

            (_ring, _ringNext) = (_ringNext, _ring);
        }

        int added = _cellCount - start;
        if (added == 0)
        {
            return 0;
        }

        for (int record = sheet.Head; record >= 0; record = _cellNext[record])
        {
            if (record < start && (_cellFlags[record] & CellFront) == 0)
            {
                continue;
            }

            _cellFlags[record] = IsFrontOf(_cellPos[record], root)
                ? (byte)(_cellFlags[record] | CellFront)
                : (byte)(_cellFlags[record] & ~CellFront);
        }

        return added;
    }

    /// <summary>
    /// Pop's front rule after an advance: a holdable side that is not this sheet's — dry, falling,
    /// or a cell of another sheet this pass reached across a gap.
    /// </summary>
    private bool IsFrontOf(VoxelPos cell, int root)
    {
        foreach ((int dx, int dz) in Sideways)
        {
            VoxelPos side = cell.Offset(dx, 0, dz);
            if (!_visitTerrain.CanHold(side))
            {
                continue;
            }

            if (!IndexTryGet(side, out int other) || Find(_cellSheet[other]) != root)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Slice 2c: a sheet still being fed takes the front advance's rings and levels them with its
    /// tip — the band of its own cells within <c>rings × FedBandRingsPerAdvance</c> rings of its dry
    /// edge — every other cell's target its own amount. Returns the cells added, zero when nothing
    /// was taken (and then nothing is written); <paramref name="bandCells"/> is the old cells levelled.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why the tip and not the whole sheet (the pass's fact 1).</b> Pro rata — every cell scaled,
    /// the new ones given the mean — puts a ridge in front of any stream that tapers toward its edge,
    /// because the mean is more than the edge holds; a flat extension of the edge advances nothing
    /// under the floor. The band's level is its mean, which is no more than its thickest cell, its
    /// innermost, which is no more than the interior cell behind it: the stream still falls toward
    /// its edge, and its body keeps its gradient to the millilitre.
    /// </para>
    /// <para>
    /// <b>The band floods from the dry edge only</b> — a front cell with a dry, holdable side — not
    /// from a front beside falling water, which is where a stream fed from a shaft beside it meets
    /// its own source. <b>The floor is the band's</b>: a ring is taken while the band's water still
    /// covers the band and the new cells at <see cref="AdvanceFloorMillilitres"/>.
    /// </para>
    /// </remarks>
    private int AdvanceFed(int root, Sheet sheet, out int bandCells)
    {
        bandCells = 0;
        _band.Clear();
        _bandRing.Clear();
        for (int record = sheet.Head; record >= 0; record = _cellNext[record])
        {
            if ((_cellFlags[record] & CellFront) != 0 && BordersDry(_cellPos[record]))
            {
                _cellFlags[record] |= CellBand;
                _band.Add(record);
                _bandRing.Add(record);
            }
        }

        if (_band.Count == 0)
        {
            return 0;
        }

        int depth = _advanceRings * FedBandRingsPerAdvance;
        for (int r = 1; r < depth && _bandRing.Count > 0; r++)
        {
            _bandNext.Clear();
            foreach (int record in _bandRing)
            {
                foreach ((int dx, int dz) in Sideways)
                {
                    if (IndexTryGet(_cellPos[record].Offset(dx, 0, dz), out int other)
                        && (_cellFlags[other] & CellBand) == 0
                        && Find(_cellSheet[other]) == root)
                    {
                        _cellFlags[other] |= CellBand;
                        _band.Add(other);
                        _bandNext.Add(other);
                    }
                }
            }

            (_bandRing, _bandNext) = (_bandNext, _bandRing);
        }

        long bandTotal = 0;
        foreach (int record in _band)
        {
            bandTotal += _cellAmount[record];
        }

        int added = 0;
        int start = _cellCount;
        if (bandTotal >= (long)AdvanceFloorMillilitres * (_band.Count + 1))
        {
            added = Advance(root, sheet, bandTotal, _band.Count);
        }

        if (added == 0)
        {
            foreach (int record in _band)
            {
                _cellFlags[record] &= unchecked((byte)~CellBand);
            }

            return 0;
        }

        for (int record = start; record < _cellCount; record++)
        {
            _cellFlags[record] |= CellBand;
        }

        // The band and the new cells level together, the remainder a millilitre each in flood
        // order; every other cell's target is what it holds, so Apply writes the tip alone.
        int count = _band.Count + added;
        int share = (int)(bandTotal / count);
        int remainder = (int)(bandTotal % count);
        int i = 0;
        for (int record = sheet.Head; record >= 0; record = _cellNext[record])
        {
            _cellTarget[record] = (_cellFlags[record] & CellBand) != 0
                ? share + (i++ < remainder ? 1 : 0)
                : _cellAmount[record];
        }

        bandCells = _band.Count;
        return added;
    }

    /// <summary>A dry, holdable sideways cell beside this one: the edge a stream advances into.</summary>
    private bool BordersDry(VoxelPos cell)
    {
        foreach ((int dx, int dz) in Sideways)
        {
            VoxelPos side = cell.Offset(dx, 0, dz);
            if (_grid.AmountAt(side) == 0 && _visitTerrain.CanHold(side))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The wake after a fed advance, derived from the write rather than from "level" (the pass's
    /// decision 5): every written cell and every held sideways neighbour of one that now has a move,
    /// and the water over every cell drawn down. <see cref="WakeAfterSolve"/> assumes a level sheet —
    /// its interior has no move — and a fed sheet's band borders its thicker body.
    /// </summary>
    private int WakeAfterFedAdvance(Sheet sheet)
    {
        int woken = 0;
        Span<int> sides = stackalloc int[4];
        for (int record = sheet.Head; record >= 0; record = _cellNext[record])
        {
            if (_cellTarget[record] == _cellAmount[record])
            {
                continue;
            }

            VoxelPos cell = _cellPos[record];
            if (_cellTarget[record] < _cellAmount[record])
            {
                VoxelPos above = Above(cell);
                if (_grid.AmountAt(above) > 0 && Wake(above))
                {
                    woken++;
                }
            }

            if (HasMove(cell, sides) && Wake(cell))
            {
                woken++;
            }

            foreach ((int dx, int dz) in Sideways)
            {
                VoxelPos side = cell.Offset(dx, 0, dz);
                if (_grid.AmountAt(side) > 0 && HasMove(side, sides) && Wake(side))
                {
                    woken++;
                }
            }
        }

        return woken;
    }

    /// <summary>Even shares in flood order, the remainder one millilitre each to the first cells.</summary>
    private void LevelTargets(Sheet sheet)
    {
        List<int> cells = CellsOf(sheet);
        int count = cells.Count;
        int share = (int)(sheet.Total / count);
        int remainder = (int)(sheet.Total % count);

        for (int i = 0; i < count; i++)
        {
            _cellTarget[cells[i]] = share + (i < remainder ? 1 : 0);
        }
    }

    /// <summary>
    /// As few cells as hold the total at <see cref="PackedCellMillilitres"/> each, the cells that
    /// held most first, the last of them taking what is left.
    /// </summary>
    private void PackTargets(Sheet sheet)
    {
        List<int> cells = CellsOf(sheet);
        int count = cells.Count;
        int packedCells = (int)((sheet.Total + PackedCellMillilitres - 1) / PackedCellMillilitres);

        // Stable: by amount descending, then by flood order, so a sheet already packed maps to
        // itself and the choice does not depend on the caller.
        _sheetOrder.Clear();
        for (int i = 0; i < count; i++)
        {
            _sheetOrder.Add(i);
        }

        int[] amounts = _cellAmount;
        _sheetOrder.Sort((a, b) =>
            amounts[cells[a]] != amounts[cells[b]]
                ? amounts[cells[b]].CompareTo(amounts[cells[a]])
                : a.CompareTo(b));

        for (int i = 0; i < count; i++)
        {
            _cellTarget[cells[i]] = 0;
        }

        long left = sheet.Total;
        for (int rank = 0; rank < packedCells; rank++)
        {
            int target = (int)Math.Min(PackedCellMillilitres, left);
            _cellTarget[cells[_sheetOrder[rank]]] = target;
            left -= target;
        }
    }

    private static readonly int WatchApply = CallWatch.Register("sheet solve: apply a sheet");

    /// <summary>
    /// Moves water from the cells above their target to the cells below it, through
    /// <see cref="FluidGrid.Transfer"/> only. Returns the millilitres moved.
    /// </summary>
    private long Apply(Sheet sheet, out int cellsWritten)
    {
        using CallWatch.Scope callWatch = CallWatch.Time(WatchApply);
        List<int> cells = CellsOf(sheet);
        _sheetGivers.Clear();
        _sheetTakers.Clear();
        foreach (int record in cells)
        {
            int delta = _cellAmount[record] - _cellTarget[record];
            if (delta > 0)
            {
                _sheetGivers.Add(record);
            }
            else if (delta < 0)
            {
                _sheetTakers.Add(record);
            }
        }

        cellsWritten = _sheetGivers.Count + _sheetTakers.Count;

        long moved = 0;
        int g = 0, t = 0;
        int give = _sheetGivers.Count > 0 ? Surplus(_sheetGivers[0]) : 0;
        int take = _sheetTakers.Count > 0 ? Deficit(_sheetTakers[0]) : 0;
        while (g < _sheetGivers.Count && t < _sheetTakers.Count)
        {
            int amount = Math.Min(give, take);
            int shifted = _grid.Transfer(_cellPos[_sheetGivers[g]], _cellPos[_sheetTakers[t]], amount);
            if (shifted != amount)
            {
                // Cannot happen: a giver holds its surplus and a taker's target is at most a
                // cell, so neither clamp bites. Stated as a throw rather than a silent shortfall,
                // because a shortfall here would leave the sheet unlevel and the pass claiming
                // otherwise.
                throw new InvalidOperationException(
                    $"The sheet solve moved {shifted} mL of {amount} between two cells of one sheet.");
            }

            moved += amount;
            give -= amount;
            take -= amount;
            if (give == 0 && ++g < _sheetGivers.Count)
            {
                give = Surplus(_sheetGivers[g]);
            }

            if (take == 0 && ++t < _sheetTakers.Count)
            {
                take = Deficit(_sheetTakers[t]);
            }
        }

        return moved;
    }

    private int Surplus(int record) => _cellAmount[record] - _cellTarget[record];

    private int Deficit(int record) => _cellTarget[record] - _cellAmount[record];

    /// <summary>
    /// Wakes what the solve gave a move: the front, and the water above every cell drawn down.
    /// </summary>
    private int WakeAfterSolve(Sheet sheet)
    {
        int woken = 0;
        for (int record = sheet.Head; record >= 0; record = _cellNext[record])
        {
            if ((_cellFlags[record] & CellFront) != 0 && Wake(_cellPos[record]))
            {
                woken++;
                PromoteSolveWake(_cellPos[record]);
            }

            if (_cellTarget[record] < _cellAmount[record])
            {
                VoxelPos above = Above(_cellPos[record]);
                if (_grid.AmountAt(above) > 0 && Wake(above))
                {
                    woken++;
                    PromoteSolveWake(above);
                }
            }
        }

        return woken;
    }

    /// <summary>
    /// Lists every cell the solve wrote and left with water and no move as resting, so that the
    /// hand-off can take it at this boundary.
    /// </summary>
    /// <remarks>
    /// <b>The one place a cell is called resting without a visit, and the hand-off is why.</b>
    /// <see cref="Rested"/> is what <see cref="SurfaceHandoff"/> reads; a sheet the solve
    /// levelled and left asleep would be level, still, on the ground and never handed to the
    /// basin under it — the puddle held for ever that the hand-off exists to prevent. Waking each
    /// written cell for the visit that would list it was the first answer, and it cost ten times
    /// the visits on a flooded mine (47.8 M against 4.6 M, 96 m at bore 9) for a fact the solve
    /// already knows: a written cell that is not a front is level with its sheet, stands on
    /// something, and borders nothing it could spread into — a visit would find exactly nothing.
    /// A front is left off, because it has a move and is woken for it; a cell packed to nothing is
    /// left off, because it holds no water to rest. Valid until the next tick begins, like the
    /// rest of the list, which is why the settle runs before the hand-off at the boundary.
    /// </remarks>
    private void ListRested(Sheet sheet)
    {
        for (int record = sheet.Head; record >= 0; record = _cellNext[record])
        {
            if (_cellTarget[record] != _cellAmount[record] && _cellTarget[record] > 0
                && (_cellFlags[record] & CellFront) == 0)
            {
                _rested.Add(_cellPos[record]);
            }
        }
    }
}
