using Overburden.Core.Voxels;

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
public readonly record struct SheetLevelResult(
    int Sheets, int Levelled, int Packed, int Skipped, int UnderShare, int Stepped, int LargestSheet,
    int CellsVisited, int CellsWritten, long MillilitresMoved, int CellsWoken, int Noisy)
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

    private readonly List<Sheet> _sheets = [];
    private int _sheetCount;
    private List<int> _layer = [];
    private List<int> _nextLayer = [];
    private readonly List<VoxelPos> _sheetSeeds = [];
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
    private sealed class Sheet
    {
        public int Parent;
        public readonly List<int> Cells = [];
        public readonly List<int> Frontier = [];
        public long Total;
        public int Unsettled;
        public bool Frozen;
        public int BorderLow;
        public int BorderHigh;

        public void Reset(int index)
        {
            Parent = index;
            Cells.Clear();
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
    public SheetLevelResult LevelSheets(int share = 0, int quietTicks = 0)
    {
        RefuseMidTick(nameof(LevelSheets));
        return LevelSheets(Pending, share, quietTicks);
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
        IReadOnlyList<VoxelPos> seeds, int share = 0, int quietTicks = 0)
    {
        ArgumentNullException.ThrowIfNull(seeds);
        if (TickInProgress)
        {
            throw new InvalidOperationException(
                $"LevelSheets asked mid-tick, with {RemainingInTick} cell(s) still to visit; "
                + "finish the tick with Step() first.");
        }

        return Solve(seeds, share, quietTicks, Ticks);
    }

    /// <summary>
    /// The solve at the tail of a job, on the worker (slice 3c): over the next tick's set as the
    /// visits left it, told the tick number the boundary will have, so the quiet test and the
    /// order — settle, then hand-off, drain, pour, the next tick — are the sync arm's exactly.
    /// </summary>
    internal SheetLevelResult SolveOnWorker(int share, int quietTicks) =>
        Solve(Pending, share, quietTicks, Ticks + 1);

    /// <summary>The tick number the last solve tested quiet against. Tests only: it pins the worker's +1.</summary>
    internal long LastSolveTick => _solveTick;

    private SheetLevelResult Solve(IReadOnlyList<VoxelPos> seeds, int share, int quietTicks, long nowTick)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(share);
        ArgumentOutOfRangeException.ThrowIfNegative(quietTicks);
        _quietTicks = quietTicks;
        _solveTick = nowTick;

        // A copy, because waking appends to the pending list this is usually a view of; and a
        // total order on position rather than the caller's order, so the same water in the same
        // cells solves the same way whichever cell happened to wake first.
        _sheetSeeds.Clear();
        _sheetSeeds.AddRange(seeds);
        _sheetSeeds.Sort(static (a, b) =>
            a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X != b.X ? a.X.CompareTo(b.X) : a.Z.CompareTo(b.Z));

        int sheets = 0, levelled = 0, packed = 0, skipped = 0, underShare = 0, stepped = 0, noisy = 0;
        int largest = 0, visited = 0, written = 0, woken = 0;
        long moved = 0;

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

            Flood(groupStart, groupEnd, share);
            visited += _cellCount;

            for (int s = 0; s < _sheetCount; s++)
            {
                Sheet sheet = _sheets[s];
                if (sheet.Parent != s)
                {
                    continue;
                }

                sheets++;
                int count = sheet.Cells.Count;
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
                    noisy++;
                    continue;
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
                woken += WakeAfterSolve(sheet);
                ListRested(sheet);
            }

            groupStart = groupEnd;
        }

        return new SheetLevelResult(
            sheets, levelled, packed, skipped, underShare, stepped, largest, visited, written, moved,
            woken, noisy);
    }

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
    private bool IsBeingFed(Sheet sheet)
    {
        if (_quietTicks <= 0)
        {
            return false;
        }

        foreach (int record in sheet.Cells)
        {
            long inflow = InflowTickAt(_cellPos[record].Chunk);
            if (inflow != NeverInflowed && _solveTick - inflow <= _quietTicks)
            {
                return true;
            }
        }

        return false;
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

    /// <summary>
    /// Floods, layer by layer, from every seed of one Y at once, freezing a sheet the moment it
    /// is known to be under the share and joining floods that meet. Fills the records and the
    /// sheets for <see cref="LevelSheets(IReadOnlyList{VoxelPos}, int)"/> to judge.
    /// </summary>
    private void Flood(int seedStart, int seedEnd, int share)
    {
        _cellCount = 0;
        _cellIndex.Clear();
        _sheetCount = 0;
        _layer.Clear();
        _nextLayer.Clear();

        for (int i = seedStart; i < seedEnd; i++)
        {
            VoxelPos seed = _sheetSeeds[i];
            if (_cellIndex.ContainsKey(seed) || !IsSheetCell(seed))
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
                        && !WorthLevelling(share, sheet.Cells.Count, sheet.Unsettled))
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
            if (_cellIndex.TryGetValue(side, out int sideRecord))
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
        }

        int record = _cellCount++;
        int amount = _grid.AmountAt(cell);
        _cellPos[record] = cell;
        _cellAmount[record] = amount;
        _cellSheet[record] = sheet;
        _cellFlags[record] = 0;
        _cellIndex[cell] = record;

        Sheet owner = _sheets[sheet];
        owner.Cells.Add(record);
        owner.Total += amount;
        return record;
    }

    private int NewSheet()
    {
        if (_sheetCount == _sheets.Count)
        {
            _sheets.Add(new Sheet());
        }

        _sheets[_sheetCount].Reset(_sheetCount);
        return _sheetCount++;
    }

    private int Find(int sheet)
    {
        int root = sheet;
        while (_sheets[root].Parent != root)
        {
            root = _sheets[root].Parent;
        }

        while (_sheets[sheet].Parent != root)
        {
            int next = _sheets[sheet].Parent;
            _sheets[sheet].Parent = root;
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
        if (_sheets[a].Cells.Count < _sheets[b].Cells.Count)
        {
            (a, b) = (b, a);
        }

        Sheet big = _sheets[a];
        Sheet small = _sheets[b];
        small.Parent = a;
        big.Cells.AddRange(small.Cells);
        big.Total += small.Total;
        big.Unsettled += small.Unsettled;
        big.BorderLow = Math.Min(big.BorderLow, small.BorderLow);
        big.BorderHigh = Math.Max(big.BorderHigh, small.BorderHigh);
        _nextLayer.AddRange(big.Frontier);
        _nextLayer.AddRange(small.Frontier);
        big.Frontier.Clear();
        small.Cells.Clear();
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

    /// <summary>Even shares in flood order, the remainder one millilitre each to the first cells.</summary>
    private void LevelTargets(Sheet sheet)
    {
        List<int> cells = sheet.Cells;
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
        List<int> cells = sheet.Cells;
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

    /// <summary>
    /// Moves water from the cells above their target to the cells below it, through
    /// <see cref="FluidGrid.Transfer"/> only. Returns the millilitres moved.
    /// </summary>
    private long Apply(Sheet sheet, out int cellsWritten)
    {
        List<int> cells = sheet.Cells;
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
        foreach (int record in sheet.Cells)
        {
            if ((_cellFlags[record] & CellFront) != 0 && Wake(_cellPos[record]))
            {
                woken++;
            }

            if (_cellTarget[record] < _cellAmount[record])
            {
                VoxelPos above = Above(_cellPos[record]);
                if (_grid.AmountAt(above) > 0 && Wake(above))
                {
                    woken++;
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
        foreach (int record in sheet.Cells)
        {
            if (_cellTarget[record] != _cellAmount[record] && _cellTarget[record] > 0
                && (_cellFlags[record] & CellFront) == 0)
            {
                _rested.Add(_cellPos[record]);
            }
        }
    }
}
