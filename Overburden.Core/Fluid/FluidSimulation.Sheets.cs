using Overburden.Core.Voxels;

namespace Overburden.Core.Fluid;

/// <summary>What one pass of <see cref="FluidSimulation.LevelSheets()"/> did.</summary>
/// <param name="Sheets">Sheets found from the seeds, including the ones left alone.</param>
/// <param name="Levelled">Sheets distributed evenly, to within a millilitre a pair.</param>
/// <param name="Packed">Sheets whose share was under a litre a cell, packed into as few cells as hold it.</param>
/// <param name="Skipped">Sheets with no move available inside them, left exactly as they were.</param>
/// <param name="LargestSheet">Cells in the largest sheet found. The cost figure.</param>
/// <param name="CellsWritten">Cells whose contents changed.</param>
/// <param name="MillilitresMoved">Millilitres transferred between cells of a sheet. Not a change in the world's water.</param>
/// <param name="CellsWoken">Cells queued for the next tick because the solve gave them something to do.</param>
public readonly record struct SheetLevelResult(
    int Sheets, int Levelled, int Packed, int Skipped, int LargestSheet,
    int CellsWritten, long MillilitresMoved, int CellsWoken)
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
/// <b>Bottom-up, because levelling a lower sheet can unsupport a higher one.</b> Seeds are taken in
/// ascending Y, so a sheet at Y is found against the grid as the sheets below it left it: a cell
/// whose floor was drawn down is not supported any more, is left out, and falls next tick. Volume
/// is conserved by construction — every move is a <see cref="FluidGrid.Transfer"/> between two
/// cells of one sheet — so the conservation identity needs no new term.
/// </para>
/// </remarks>
public sealed partial class FluidSimulation
{
    // Scratch for the solve, kept across passes so a pass over a flooded mine allocates nothing.
    private readonly HashSet<VoxelPos> _sheetVisited = [];
    private readonly List<VoxelPos> _sheetSeeds = [];
    private readonly List<VoxelPos> _sheet = [];
    private readonly List<int> _sheetAmounts = [];
    private readonly List<int> _sheetTargets = [];
    private readonly List<int> _sheetOrder = [];
    private readonly List<bool> _sheetIsFront = [];
    private readonly List<int> _sheetGivers = [];
    private readonly List<int> _sheetTakers = [];

    /// <summary>The most a packed cell holds: one millilitre under the spread threshold.</summary>
    public const int PackedCellMillilitres = FluidConstants.MinimumSpreadMillilitres - 1;

    /// <summary>
    /// Cells queued for the next tick. Valid at a tick boundary, which is the only time the
    /// question means anything; the seeds of <see cref="LevelSheets()"/>.
    /// </summary>
    public IReadOnlyList<VoxelPos> Pending => _pending;

    /// <summary>
    /// Levels every sheet the active set touches. At a tick boundary only.
    /// </summary>
    /// <remarks>
    /// Seeded from <see cref="Pending"/> rather than from every held cell, because a sheet with no
    /// awake cell has no move inside it — its cells were each visited and found nothing to do —
    /// and would be skipped anyway. The corridor that stays awake for hours is a few thousand
    /// cells found from a few hundred seeds; a flooded mine's floor is one sheet found from its
    /// front.
    /// </remarks>
    public SheetLevelResult LevelSheets() => LevelSheets(_pending);

    /// <summary>
    /// Levels every sheet reachable from the given cells. At a tick boundary only.
    /// </summary>
    /// <remarks>
    /// The unload-time method: M8b-4 runs it over the cells of a departing chunk so that what the
    /// store writes is at rest. A seed that holds nothing, cannot hold, or has room below is not a
    /// sheet cell and starts nothing.
    /// </remarks>
    public SheetLevelResult LevelSheets(IReadOnlyList<VoxelPos> seeds)
    {
        ArgumentNullException.ThrowIfNull(seeds);
        if (TickInProgress)
        {
            throw new InvalidOperationException(
                $"LevelSheets asked mid-tick, with {RemainingInTick} cell(s) still to visit; "
                + "finish the tick with Step() first.");
        }

        // A copy, because waking appends to the pending list this is usually a view of; and a
        // total order on position rather than the caller's order, so the same water in the same
        // cells solves the same way whichever cell happened to wake first.
        _sheetSeeds.Clear();
        _sheetSeeds.AddRange(seeds);
        _sheetSeeds.Sort(static (a, b) =>
            a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X != b.X ? a.X.CompareTo(b.X) : a.Z.CompareTo(b.Z));
        _sheetVisited.Clear();

        int sheets = 0, levelled = 0, packed = 0, skipped = 0, largest = 0, written = 0, woken = 0;
        long moved = 0;

        foreach (VoxelPos seed in _sheetSeeds)
        {
            if (_sheetVisited.Contains(seed) || !IsSheetCell(seed))
            {
                continue;
            }

            long total = Flood(seed, out bool unsettled);
            sheets++;
            int count = _sheet.Count;
            largest = Math.Max(largest, count);

            if (count == 1 || !unsettled)
            {
                skipped++;
                continue;
            }

            if (total <= (long)PackedCellMillilitres * count)
            {
                PackTargets(total);
                packed++;
            }
            else
            {
                LevelTargets(total);
                levelled++;
            }

            moved += Apply(out int cellsWritten);
            written += cellsWritten;
            woken += WakeAfterSolve();
            ListRested();
        }

        return new SheetLevelResult(sheets, levelled, packed, skipped, largest, written, moved, woken);
    }

    /// <summary>Wet, holdable, and standing on something: a cell a sheet can be made of.</summary>
    private bool IsSheetCell(VoxelPos cell) =>
        _grid.AmountAt(cell) > 0 && _terrain.CanHold(cell) && IsSupported(cell);

    private bool IsSupported(VoxelPos cell)
    {
        VoxelPos below = Below(cell);
        return !_terrain.CanHold(below) || RoomAt(below) == 0;
    }

    /// <summary>
    /// Fills <see cref="_sheet"/>, <see cref="_sheetAmounts"/> and <see cref="_sheetIsFront"/>
    /// from one seed, and returns the sheet's total. <paramref name="unsettled"/> is whether any
    /// pair inside it could still transfer — the automaton's own rule, asked of every edge once.
    /// </summary>
    private long Flood(VoxelPos seed, out bool unsettled)
    {
        _sheet.Clear();
        _sheetAmounts.Clear();
        _sheetIsFront.Clear();
        _sheet.Add(seed);
        _sheetVisited.Add(seed);

        long total = 0;
        unsettled = false;

        for (int i = 0; i < _sheet.Count; i++)
        {
            VoxelPos cell = _sheet[i];
            int amount = _grid.AmountAt(cell);
            _sheetAmounts.Add(amount);
            total += amount;

            bool front = false;
            foreach ((int dx, int dz) in Sideways)
            {
                VoxelPos side = cell.Offset(dx, 0, dz);
                int beside = _grid.AmountAt(side);

                if (_sheetVisited.Contains(side))
                {
                    // A member found from another cell, or this cell's own discoverer. Same sheet
                    // either way: two sheets at one Y that touch are one sheet.
                    unsettled |= CanTransfer(amount, beside);
                    continue;
                }

                if (beside > 0 && IsSheetCell(side))
                {
                    _sheet.Add(side);
                    _sheetVisited.Add(side);
                    unsettled |= CanTransfer(amount, beside);
                }
                else if (_terrain.CanHold(side))
                {
                    // Dry, or wet and falling: not the sheet's, but somewhere this cell could
                    // spread once it holds more — the front.
                    front = true;
                }
            }

            _sheetIsFront.Add(front);
        }

        return total;
    }

    private static bool CanTransfer(int a, int b) =>
        FluidRules.HorizontalTransfer(a, b) > 0 || FluidRules.HorizontalTransfer(b, a) > 0;

    /// <summary>Even shares in flood order, the remainder one millilitre each to the first cells.</summary>
    private void LevelTargets(long total)
    {
        int count = _sheet.Count;
        int share = (int)(total / count);
        int remainder = (int)(total % count);

        _sheetTargets.Clear();
        for (int i = 0; i < count; i++)
        {
            _sheetTargets.Add(share + (i < remainder ? 1 : 0));
        }
    }

    /// <summary>
    /// As few cells as hold the total at <see cref="PackedCellMillilitres"/> each, the cells that
    /// held most first, the last of them taking what is left.
    /// </summary>
    private void PackTargets(long total)
    {
        int count = _sheet.Count;
        int cells = (int)((total + PackedCellMillilitres - 1) / PackedCellMillilitres);

        // Stable: by amount descending, then by flood order, so a sheet already packed maps to
        // itself and the choice does not depend on the caller.
        _sheetOrder.Clear();
        for (int i = 0; i < count; i++)
        {
            _sheetOrder.Add(i);
        }

        List<int> amounts = _sheetAmounts;
        _sheetOrder.Sort((a, b) =>
            amounts[a] != amounts[b] ? amounts[b].CompareTo(amounts[a]) : a.CompareTo(b));

        _sheetTargets.Clear();
        for (int i = 0; i < count; i++)
        {
            _sheetTargets.Add(0);
        }

        long left = total;
        for (int rank = 0; rank < cells; rank++)
        {
            int target = (int)Math.Min(PackedCellMillilitres, left);
            _sheetTargets[_sheetOrder[rank]] = target;
            left -= target;
        }
    }

    /// <summary>
    /// Moves water from the cells above their target to the cells below it, through
    /// <see cref="FluidGrid.Transfer"/> only. Returns the millilitres moved.
    /// </summary>
    private long Apply(out int cellsWritten)
    {
        _sheetGivers.Clear();
        _sheetTakers.Clear();
        for (int i = 0; i < _sheet.Count; i++)
        {
            int delta = _sheetAmounts[i] - _sheetTargets[i];
            if (delta > 0)
            {
                _sheetGivers.Add(i);
            }
            else if (delta < 0)
            {
                _sheetTakers.Add(i);
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
            int shifted = _grid.Transfer(_sheet[_sheetGivers[g]], _sheet[_sheetTakers[t]], amount);
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

    private int Surplus(int i) => _sheetAmounts[i] - _sheetTargets[i];

    private int Deficit(int i) => _sheetTargets[i] - _sheetAmounts[i];

    /// <summary>
    /// Wakes what the solve gave a move: the front, and the water above every cell drawn down.
    /// </summary>
    private int WakeAfterSolve()
    {
        int woken = 0;
        for (int i = 0; i < _sheet.Count; i++)
        {
            if (_sheetIsFront[i] && Wake(_sheet[i]))
            {
                woken++;
            }

            if (_sheetTargets[i] < _sheetAmounts[i])
            {
                VoxelPos above = Above(_sheet[i]);
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
    private void ListRested()
    {
        for (int i = 0; i < _sheet.Count; i++)
        {
            if (_sheetTargets[i] != _sheetAmounts[i] && _sheetTargets[i] > 0 && !_sheetIsFront[i])
            {
                _rested.Add(_sheet[i]);
            }
        }
    }
}
