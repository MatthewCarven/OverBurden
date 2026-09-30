using System.Numerics;
using Overburden.Core.Building;
using Overburden.Core.Content;
using Overburden.Core.Fluid;
using Overburden.Core.Hydrology;
using Overburden.Core.Voxels;

namespace Overburden.Core.Machines;

/// <summary>What a siphon click asks for: water out of the world into the store, or back.</summary>
public enum SiphonVerb
{
    /// <summary>The left click: water at the crosshair into the store, 1,000 J a litre (decision 25).</summary>
    Take,

    /// <summary>The right click: water from the store at the crosshair, free.</summary>
    Pour,
}

/// <summary>Why a click was not queued. <see cref="None"/> when it was.</summary>
public enum SiphonRefusal
{
    None,

    /// <summary>The tool in hand takes rock.</summary>
    NotAWaterTool,

    /// <summary>No quantum backpack worn: the store is its channel's, and the lead-acid pack has none (decision 26).</summary>
    NoBackpack,

    /// <summary>A whole boundary's budget is already waiting (IM-4's pass, decision 6).</summary>
    Busy,
}

/// <summary>What one <see cref="Siphon.Apply"/> did.</summary>
public readonly record struct SiphonReport(
    int Completed,
    int Missed,
    int Waiting,
    int CellsVisited,
    long TakenMillilitres,
    long TakenLitres,
    long PouredMillilitres,
    long PouredLitres,
    long SpilledLitres,
    long PaidMillijoules)
{
    public bool DidAnything => Completed + Missed + CellsVisited > 0 || TakenLitres + PouredLitres > 0;

    public override string ToString() =>
        $"{Completed} done, {Missed} missed, {Waiting} waiting, {CellsVisited:N0} cell(s) visited; "
        + $"took {TakenMillilitres:N0} mL + {TakenLitres:N0} L, poured {PouredMillilitres:N0} mL + {PouredLitres:N0} L "
        + $"({SpilledLitres:N0} L spilled), paid {PaidMillijoules:N0} mJ";
}

/// <summary>
/// The siphon (IM-4a; IDEAL-MACHINES-PLAN decisions 23–26, as IM-4's pass took them): water at the
/// crosshair into the store at 1,000 J a litre, and back out of it for free, in the quantum tools'
/// three sizes.
/// </summary>
/// <remarks>
/// <para>
/// <b>A click only enqueues; everything moves at the boundary</b> (decision 2 of the pass). Not only
/// the joules and the store: the aim too. A worker owns the Tier B grid between boundaries, and the
/// grid refuses reads while it does, so which water a click meant cannot be answered at the click.
/// The op keeps the ray — origin, direction, reach — and the boundary casts it with the grid in hand.
/// A quit before then moves nothing and pays nothing.
/// </para>
/// <para>
/// <b>Twins on both engines, never the doors the machine tick uses</b> (decision 3):
/// <see cref="FluidSimulation.Siphon"/> and <see cref="FluidSimulation.Pour"/> in millilitres,
/// <see cref="WaterSystem.Siphon"/> and <see cref="WaterSystem.Pour"/> in whole litres, the store's
/// <see cref="MachineSet.DepositWater"/> and <see cref="MachineSet.WithdrawWater"/>, and the bank's
/// <see cref="MachineTick.PaySiphon"/>. <see cref="Exact"/> checks every one against this class's own
/// counters: Tier A's identity is never evaluated in the game, so this is what checks the siphon's side
/// of it.
/// </para>
/// <para>
/// <b>Never take what the store can't hold, and never on credit</b> (decision 7): a take is cut to the
/// free room of the backpack's channel and to what its bank can pay, a partial move rather than a
/// refusal of the whole; a pour takes the last-filled vault's water first, and what does not fit where it
/// lands stays in the store.
/// </para>
/// <para>
/// <b>A budget of Tier B cells a boundary</b> (decision 6): <see cref="CellsPerBoundary"/>, the dump's
/// figure, visited cells counted. A Tier A op is one number and lands whole. A chunk-sized Tier B take
/// spreads over up to eleven boundaries and pays as it moves; a click is refused
/// <see cref="SiphonRefusal.Busy"/> only while a whole boundary's worth is already waiting.
/// </para>
/// </remarks>
public sealed class Siphon
{
    /// <summary>Tier B cells visited a boundary, across every op — the dump's budget (decision 33), measured at 2–4 ms by IM-0; an estimate for the siphon.</summary>
    public const int CellsPerBoundary = 3_200;

    private readonly FluidSimulation _fluid;
    private readonly WaterSystem? _water;
    private readonly LakeCeiling? _ceiling;
    private readonly IVoxelWorld _world;
    private readonly MachineSet _machines;
    private readonly MachineTick _tick;
    private readonly Action<VoxelPos>? _submerged;
    private readonly Queue<Op> _queue = new();

    private readonly long _siphonedBase, _pouredBase, _siphonedLitresBase, _pouredLitresBase, _depositedBase, _withdrawnBase, _paidBase;

    private enum Tier : byte
    {
        Unresolved,
        A,
        B,
    }

    private sealed class Op(SiphonVerb verb, Vector3 origin, Vector3 direction, float reach, ToolDefinition tool, int channel)
    {
        public SiphonVerb Verb { get; } = verb;
        public Vector3 Origin { get; } = origin;
        public Vector3 Direction { get; } = direction;
        public float Reach { get; } = reach;
        public ToolDefinition Tool { get; } = tool;
        public int Channel { get; } = channel;
        public Tier Tier { get; set; }
        public int Basin { get; set; }
        public VoxelPos ShapeOrigin { get; set; }
        public int Edge { get; set; } = EdgeOf(tool.Bite);
        public int Next { get; set; }

        public int Cells => Edge * Edge * Edge;

        /// <summary>The worst case this op still owes the budget: every cell of its shape until it is resolved.</summary>
        public int Waiting => Tier == Tier.A ? 0 : Cells - Next;
    }

    /// <param name="fluid">Tier B, at a boundary.</param>
    /// <param name="water">Tier A; null makes every lake invisible to the aim.</param>
    /// <param name="ceiling">The lake bands the aim asks, covered first; null with <paramref name="water"/>.</param>
    /// <param name="world">The voxels the ray walks: it stops at the first solid.</param>
    /// <param name="machines">The store.</param>
    /// <param name="tick">The bank a take pays from.</param>
    /// <param name="submerged">What a cover wakes, as the relay's does — the grid's wake.</param>
    public Siphon(
        FluidSimulation fluid,
        WaterSystem? water,
        LakeCeiling? ceiling,
        IVoxelWorld world,
        MachineSet machines,
        MachineTick tick,
        Action<VoxelPos>? submerged = null)
    {
        _fluid = fluid ?? throw new ArgumentNullException(nameof(fluid));
        _water = water;
        _ceiling = ceiling;
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _machines = machines ?? throw new ArgumentNullException(nameof(machines));
        _tick = tick ?? throw new ArgumentNullException(nameof(tick));
        _submerged = submerged;

        _siphonedBase = fluid.SiphonedMillilitres;
        _pouredBase = fluid.PouredMillilitres;
        _siphonedLitresBase = water?.SiphonedLitres ?? 0;
        _pouredLitresBase = water?.PouredLitres ?? 0;
        _depositedBase = machines.WaterDepositedMillilitres;
        _withdrawnBase = machines.WaterWithdrawnMillilitres;
        _paidBase = tick.SiphonPaidMillijoules;
    }

    // ---- the siphon's own counters, which Exact checks every twin against ----

    /// <summary>Millilitres taken out of Tier B cells into the store.</summary>
    public long TakenMillilitres { get; private set; }

    /// <summary>Whole litres taken out of lakes into the store.</summary>
    public long TakenLitres { get; private set; }

    /// <summary>Millilitres poured from the store into Tier B cells.</summary>
    public long PouredMillilitres { get; private set; }

    /// <summary>Whole litres poured from the store into lakes, spilled ones included.</summary>
    public long PouredLitres { get; private set; }

    /// <summary>Of <see cref="PouredLitres"/>, litres a basin spilled out of the world — <see cref="WaterSystem.SpilledLitres"/> counts them too.</summary>
    public long SpilledLitres { get; private set; }

    /// <summary>Millijoules paid from banks for what was taken.</summary>
    public long PaidMillijoules { get; private set; }

    /// <summary>Clicks queued, and those refused, by reason.</summary>
    public long Enqueued { get; private set; }

    public long RefusedNotWater { get; private set; }

    public long RefusedNoBackpack { get; private set; }

    public long RefusedBusy { get; private set; }

    /// <summary>Ops finished, and those whose aim found nothing to act on.</summary>
    public long Completed { get; private set; }

    public long Missed { get; private set; }

    /// <summary>Ops a <see cref="Clear"/> dropped before they moved anything — a quit's.</summary>
    public long Dropped { get; private set; }

    /// <summary>Tier B cells visited, over the lifetime.</summary>
    public long CellsVisited { get; private set; }

    /// <summary>Ops queued and not yet finished.</summary>
    public int Waiting => _queue.Count;

    /// <summary>
    /// Every twin against the siphon's own counters, since construction: the two Tier B counters, the two
    /// Tier A ones, the store's two doors and the bank's term. The machine layer's rule, <c>pumped ==
    /// emitted</c>, in the siphon's shape.
    /// </summary>
    public bool Exact =>
        _fluid.SiphonedMillilitres - _siphonedBase == TakenMillilitres
        && _fluid.PouredMillilitres - _pouredBase == PouredMillilitres
        && (_water?.SiphonedLitres ?? 0) - _siphonedLitresBase == TakenLitres
        && (_water?.PouredLitres ?? 0) - _pouredLitresBase == PouredLitres
        && _machines.WaterDepositedMillilitres - _depositedBase == TakenMillilitres + (TakenLitres * FluidConstants.MillilitresPerLitre)
        && _machines.WaterWithdrawnMillilitres - _withdrawnBase == PouredMillilitres + (PouredLitres * FluidConstants.MillilitresPerLitre)
        && _tick.SiphonPaidMillijoules - _paidBase == PaidMillijoules;

    /// <summary>
    /// Queues a click. Nothing moves until <see cref="Apply"/>. Refused when the tool takes rock, when no
    /// quantum backpack is worn, or when a whole boundary's budget is already waiting.
    /// </summary>
    /// <param name="origin">The eye, in metres.</param>
    /// <param name="direction">Where it looks; need not be normalised.</param>
    /// <param name="reachMetres">How far the ray may walk — the tool's at-range reach.</param>
    public SiphonRefusal Enqueue(SiphonVerb verb, Vector3 origin, Vector3 direction, float reachMetres, ToolDefinition tool, EquippedPack? pack)
    {
        ArgumentNullException.ThrowIfNull(tool);
        if (tool.Takes != ToolTakes.Water)
        {
            RefusedNotWater++;
            return SiphonRefusal.NotAWaterTool;
        }

        if (pack is not { IsWindow: true })
        {
            RefusedNoBackpack++;
            return SiphonRefusal.NoBackpack;
        }

        int waiting = 0;
        foreach (Op queued in _queue)
        {
            waiting += queued.Waiting;
        }

        if (waiting >= CellsPerBoundary)
        {
            RefusedBusy++;
            return SiphonRefusal.Busy;
        }

        _queue.Enqueue(new Op(verb, origin, direction, reachMetres, tool, Math.Clamp(pack.Channel, 0, MachineTick.Channels - 1)));
        Enqueued++;
        return SiphonRefusal.None;
    }

    /// <summary>Drops every queued op, having moved and paid nothing for them — what a quit does to the queue.</summary>
    public void Clear()
    {
        Dropped += _queue.Count;
        _queue.Clear();
    }

    /// <summary>
    /// Runs the queue at a boundary, in the window where the fluid's ownership has cleared and before the
    /// machine tick (the bank's draw throws inside the tick's run): each op's aim resolved, then its water
    /// moved, stored and paid for, within <see cref="CellsPerBoundary"/>.
    /// </summary>
    public SiphonReport Apply()
    {
        int budget = CellsPerBoundary;
        int completed = 0, missed = 0, visited = 0;
        long takenB = TakenMillilitres, takenA = TakenLitres, pouredB = PouredMillilitres, pouredA = PouredLitres;
        long spilled = SpilledLitres, paid = PaidMillijoules;

        while (_queue.Count > 0 && budget > 0)
        {
            Op op = _queue.Peek();
            bool done;
            if (op.Tier == Tier.Unresolved && !Resolve(op))
            {
                missed++;
                Missed++;
                done = true;
            }
            else if (op.Tier == Tier.A)
            {
                if (op.Verb == SiphonVerb.Take) TakeA(op);
                else PourA(op);
                done = true;
            }
            else
            {
                int cells = op.Verb == SiphonVerb.Take ? TakeB(op, budget, out done) : PourB(op, budget, out done);
                budget -= cells;
                visited += cells;
            }

            if (!done)
            {
                break;
            }

            _queue.Dequeue();
            completed++;
            Completed++;
        }

        CellsVisited += visited;
        return new SiphonReport(
            completed, missed, _queue.Count, visited,
            TakenMillilitres - takenB, TakenLitres - takenA, PouredMillilitres - pouredB, PouredLitres - pouredA,
            SpilledLitres - spilled, PaidMillijoules - paid);
    }

    /// <summary>
    /// Casts the op's ray with the grid in hand (decision 4): it stops at the first voxel holding Tier B
    /// water or lying in a lake, covering each chunk column's lake band as it enters it, and gives up at the
    /// first solid. A take anchors on the water; a pour on the water, or failing that on the air before the
    /// surface. False when there is nothing to act on.
    /// </summary>
    private bool Resolve(Op op)
    {
        (int X, int Z)? covered = null;
        bool IsWater(VoxelPos voxel)
        {
            if (_ceiling is not null)
            {
                var column = (voxel.X >> VoxelConstants.ChunkSizeLog2, voxel.Z >> VoxelConstants.ChunkSizeLog2);
                if (covered != column)
                {
                    _ceiling.Cover(voxel, voxel, _submerged);
                    covered = column;
                }

                if (_ceiling.InLake(voxel))
                {
                    return true;
                }
            }

            return _fluid.HeldAt(voxel) > 0;
        }

        RayOutcome outcome = VoxelRay.Cast(_world, op.Origin, op.Direction, op.Reach, IsWater, out VoxelHit hit);
        if (outcome != RayOutcome.Hit)
        {
            return false;
        }

        bool water = _world.TryGetMaterial(hit.Voxel, out int material) && material == MaterialIds.AirNumericId;
        VoxelPos anchor;
        if (water)
        {
            anchor = hit.Voxel;
        }
        else if (op.Verb == SiphonVerb.Pour)
        {
            anchor = hit.Previous;
        }
        else
        {
            return false;
        }

        if (water && _water is not null && _ceiling is not null && _ceiling.InLake(anchor)
            && _water.TryBasinAtVoxel(anchor.X, anchor.Z, out int basin) && basin != Watershed.Outlet)
        {
            op.Tier = Tier.A;
            op.Basin = basin;
            return true;
        }

        op.Tier = Tier.B;
        op.ShapeOrigin = ShapeOriginOf(op.Tool.Bite, anchor);
        op.Next = 0;
        return true;
    }

    /// <summary>A lake's take: the shape's volume in whole litres, cut to the store's room and the bank.</summary>
    private void TakeA(Op op)
    {
        long want = ShapeLitres(op);
        long room = _machines.WaterRoomOf(op.Channel) / FluidConstants.MillilitresPerLitre;
        long price = PriceOf(op);
        long affordable = price == 0 ? long.MaxValue : _tick.BankOf(op.Channel) / (price * FluidConstants.MillilitresPerLitre);
        long ask = Math.Min(want, Math.Min(room, affordable));
        long taken = ask > 0 ? _water!.Siphon(op.Basin, ask) : 0;
        if (taken == 0)
        {
            return;
        }

        Store(op, taken * FluidConstants.MillilitresPerLitre);
        TakenLitres += taken;
    }

    /// <summary>A pour into a lake: the shape's volume in whole litres, cut to what the store holds; a spill counts as an outlet's does.</summary>
    private void PourA(Op op)
    {
        long litres = Math.Min(ShapeLitres(op), _machines.StoredWaterOf(op.Channel) / FluidConstants.MillilitresPerLitre);
        if (litres == 0)
        {
            return;
        }

        Unstore(op, litres * FluidConstants.MillilitresPerLitre);
        long spilled = _water!.Pour(op.Basin, litres);
        PouredLitres += litres;
        SpilledLitres += spilled;
    }

    /// <summary>A take from the shape's cells, top down, within the budget. Returns the cells visited.</summary>
    private int TakeB(Op op, int budget, out bool done)
    {
        long price = PriceOf(op);
        long room = _machines.WaterRoomOf(op.Channel);
        long affordable = price == 0 ? long.MaxValue : _tick.BankOf(op.Channel) / price;
        long cap = Math.Min(room, affordable);
        long taken = 0;
        int visited = 0;
        while (op.Next < op.Cells && visited < budget && taken < cap)
        {
            VoxelPos cell = CellAt(op, op.Next, topDown: true);
            op.Next++;
            visited++;
            long held = _fluid.HeldAt(cell);
            if (held > 0)
            {
                taken += _fluid.Siphon(cell, Math.Min(held, cap - taken));
            }
        }

        if (taken > 0)
        {
            Store(op, taken);
            TakenMillilitres += taken;
        }

        // Finished when the shape is walked, or when the store or the bank ran out — a partial take,
        // never one on credit; unfinished only when the budget did.
        done = op.Next >= op.Cells || taken >= cap;
        return visited;
    }

    /// <summary>A pour into the shape's cells, bottom up so the level rises, within the budget. Returns the cells visited.</summary>
    private int PourB(Op op, int budget, out bool done)
    {
        long cap = Math.Min(_machines.StoredWaterOf(op.Channel), (long)op.Cells * FluidConstants.CellCapacityMillilitres);
        long poured = 0;
        int visited = 0;
        while (op.Next < op.Cells && visited < budget && poured < cap)
        {
            VoxelPos cell = CellAt(op, op.Next, topDown: false);
            op.Next++;
            visited++;
            long room = _fluid.RoomFor(cell);
            if (room > 0)
            {
                long give = Math.Min(room, cap - poured);
                poured += give - _fluid.Pour(cell, give);
            }
        }

        if (poured > 0)
        {
            Unstore(op, poured);
            PouredMillilitres += poured;
        }

        done = op.Next >= op.Cells || poured >= cap;
        return visited;
    }

    /// <summary>Puts a take in the store and pays for it; the room and the bank were asked first, so both land whole.</summary>
    private void Store(Op op, long millilitres)
    {
        long stored = _machines.DepositWater(op.Channel, millilitres);
        long owed = millilitres * PriceOf(op);
        long drawn = owed > 0 ? _tick.PaySiphon(op.Channel, owed) : 0;
        PaidMillijoules += drawn;
        if (stored != millilitres || drawn != owed)
        {
            throw new InvalidOperationException(
                $"Siphon: {millilitres:N0} mL taken on channel {op.Channel}, the store took {stored:N0} and the bank paid {drawn:N0} of {owed:N0} mJ; "
                + "the room and the bank were asked first, so a shortfall is a wiring fault.");
        }
    }

    /// <summary>Takes a pour's water out of the store; the store was asked first, so it comes out whole.</summary>
    private void Unstore(Op op, long millilitres)
    {
        long withdrawn = _machines.WithdrawWater(op.Channel, millilitres);
        if (withdrawn != millilitres)
        {
            throw new InvalidOperationException(
                $"Siphon: {millilitres:N0} mL poured on channel {op.Channel} and the store gave {withdrawn:N0}; it was asked first, so a shortfall is a wiring fault.");
        }
    }

    private static long PriceOf(Op op) => op.Tool.Power?.MillijoulesPerMillilitre ?? 0;

    private static long ShapeLitres(Op op) => (long)op.Cells * FluidConstants.CellCapacityMillilitres / FluidConstants.MillilitresPerLitre;

    /// <summary>The edge of a bite's lattice cell in voxels: the miner's 1 m, the tunneller's 2 m, the excavator's chunk.</summary>
    private static int EdgeOf(ToolBite bite) => bite switch
    {
        ToolBite.Cube => BrushCell.Voxels,
        ToolBite.Tunnel => TunnelLattice.Voxels,
        ToolBite.Chunk => VoxelConstants.ChunkSize,
        _ => throw new ArgumentOutOfRangeException(nameof(bite), bite, "A water tool takes a lattice shape; the loader refuses the sphere."),
    };

    /// <summary>The origin of the bite's lattice cell holding <paramref name="anchor"/> — the carve's snap, so a siphon tiles as a miner does.</summary>
    private static VoxelPos ShapeOriginOf(ToolBite bite, VoxelPos anchor) => bite switch
    {
        ToolBite.Cube => BrushCell.FromVoxel(anchor).OriginVoxel,
        ToolBite.Tunnel => TunnelLattice.OriginOf(anchor),
        ToolBite.Chunk => anchor.Chunk.OriginVoxel,
        _ => throw new ArgumentOutOfRangeException(nameof(bite), bite, "A water tool takes a lattice shape; the loader refuses the sphere."),
    };

    /// <summary>The op's <paramref name="index"/>th cell: layer by layer, top down for a take and bottom up for a pour.</summary>
    private static VoxelPos CellAt(Op op, int index, bool topDown)
    {
        int layer = op.Edge * op.Edge;
        int y = index / layer;
        int rest = index % layer;
        int x = rest / op.Edge;
        int z = rest % op.Edge;
        return new VoxelPos(
            op.ShapeOrigin.X + x,
            op.ShapeOrigin.Y + (topDown ? op.Edge - 1 - y : y),
            op.ShapeOrigin.Z + z);
    }
}
