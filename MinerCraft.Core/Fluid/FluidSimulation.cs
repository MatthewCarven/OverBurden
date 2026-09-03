using MinerCraft.Core.Voxels;

namespace MinerCraft.Core.Fluid;

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
/// <b>Rate-free, exactly as <c>PlayerMotion.Step</c> is.</b> One call is one tick; how often ticks
/// happen is the caller's business and is M8b's. DESIGN's <i>slower than realtime is accuracy</i>
/// is what licenses that, and a tick that arrives late costs nothing here because nothing in this
/// class reads a clock.
/// </para>
/// </remarks>
public sealed class FluidSimulation
{
    // Order is fixed so a run is reproducible: +X, -X, +Z, -Z.
    private static readonly (int Dx, int Dz)[] Sideways = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    private readonly IFluidTerrain _terrain;
    private readonly FluidGrid _grid = new();

    // The active set, as a list for a deterministic visit order plus a set for deduplication.
    // A HashSet alone would work and would make the trajectory depend on hash ordering, which
    // turns "the same input settles the same way" from a property into a coincidence.
    private List<VoxelPos> _batch = [];
    private List<VoxelPos> _pending = [];
    private readonly HashSet<VoxelPos> _queued = [];

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

    public FluidSimulation(IFluidTerrain terrain)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        _terrain = terrain;
    }

    public FluidGrid Grid => _grid;

    /// <summary>Cells that will be examined on the next tick.</summary>
    public int ActiveCellCount => _pending.Count;

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
            Wake(voxel);
            WakeNeighboursOf(voxel);
        }

        return overflow;
    }

    /// <summary>Takes water out of a voxel. Returns what was actually removed.</summary>
    public long Remove(VoxelPos voxel, long millilitres)
    {
        long taken = _grid.Drain(voxel, millilitres);
        if (taken > 0)
        {
            RemovedMillilitres += taken;
            Wake(voxel);
            WakeNeighboursOf(voxel);
        }

        return taken;
    }

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
            Wake(cell);
        }
    }

    /// <summary>One tick over the active set, and nothing else.</summary>
    public FluidTickResult Step()
    {
        (_batch, _pending) = (_pending, _batch);
        _pending.Clear();
        _queued.Clear();

        // Measured on the set actually visited rather than on the one left behind, because the
        // peak is a cost figure and the cost was paid here.
        int visited = _batch.Count;
        if (visited > PeakActiveCells)
        {
            PeakActiveCells = visited;
        }

        int cellsMoved = 0;
        long moved = 0;

        foreach (VoxelPos cell in _batch)
        {
            long here = Visit(cell);
            if (here > 0)
            {
                cellsMoved++;
                moved += here;
            }
        }

        _batch.Clear();
        Ticks++;
        CellVisits += visited;

        return new FluidTickResult(visited, cellsMoved, moved, _pending.Count);
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

        while (_pending.Count > 0 && ticks < maxTicks)
        {
            FluidTickResult tick = Step();
            ticks++;
            if (tick.CellsVisited > peak)
            {
                peak = tick.CellsVisited;
            }
        }

        return new FluidSettleResult(ticks, _pending.Count == 0, peak, CellVisits - visitsBefore);
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
        foreach (VoxelPos cell in _grid.HeldCells())
        {
            if (!_terrain.CanHold(cell))
            {
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

            foreach ((int dx, int dz) in Sideways)
            {
                VoxelPos side = cell.Offset(dx, 0, dz);
                if (!_terrain.CanHold(side))
                {
                    continue;
                }

                if (FluidRules.HorizontalTransfer(amount, _grid.AmountAt(side)) > 0)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Moves what this cell can move, and returns how much that was.</summary>
    private long Visit(VoxelPos cell)
    {
        int amount = _grid.AmountAt(cell);
        if (amount == 0)
        {
            // Nothing here. Not re-queued, so it sleeps.
            return 0;
        }

        if (!_terrain.CanHold(cell))
        {
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

        foreach ((int dx, int dz) in Sideways)
        {
            VoxelPos side = cell.Offset(dx, 0, dz);
            if (!_terrain.CanHold(side))
            {
                continue;
            }

            // Against the snapshot taken before any of this cell's sideways moves, which is what
            // makes the split even between neighbours and what FluidRules' termination proof
            // assumes. Reading the live amount here would drain the source as it went.
            int want = FluidRules.HorizontalTransfer(amount, _grid.AmountAt(side));
            if (want <= 0)
            {
                continue;
            }

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
    /// <b>Both endpoints, and the six neighbours of the source only.</b> The source lost water, so
    /// a neighbour holding more can now give to it and the cell above it can now fall into it. The
    /// destination gained, which can only <i>remove</i> moves from its own neighbours — it has less
    /// room than before and is no lower than before — and it is woken itself in case it can now
    /// pass the water on.
    /// <para>
    /// <b>That argument is true only because <see cref="FluidRules.HorizontalTransfer"/> depends
    /// on one pair of cells and nothing else, and it was false before it did.</b> Dividing by the
    /// count of eligible neighbours made a cell's move depend on how many of its neighbours were
    /// lower, so a neighbour gaining could shrink the divisor and hand a nearby cell a move it did
    /// not have a moment ago — with nothing awake to take it. <see cref="VerifyQuiescent"/> caught
    /// it the first time it was asked, on four cells that were one millilitre out of level.
    /// <b>Halve this rule again and the same class of defect comes back, silently.</b>
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
    }

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

    private void Wake(VoxelPos cell)
    {
        // A cell that can hold water is worth a visit; so is one that cannot but still contains
        // some, because that is the only path by which displaced water gets out of new rock.
        if (!_terrain.CanHold(cell) && _grid.AmountAt(cell) == 0)
        {
            return;
        }

        if (_queued.Add(cell))
        {
            _pending.Add(cell);
        }
    }
}
