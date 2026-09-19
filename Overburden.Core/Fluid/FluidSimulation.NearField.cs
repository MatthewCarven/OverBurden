using Overburden.Core.Voxels;

namespace Overburden.Core.Fluid;

/// <summary>
/// The near/far order: water around the viewer is visited every tick, water further off is
/// deferred past the boundary and admitted under a quota.
/// </summary>
/// <remarks>
/// <para>
/// <b>The order alone would deliver nothing, because a tick does not end until its batch does.</b>
/// <see cref="FluidSimulation.Complete"/> fires when the cursor reaches the end of the batch, and
/// the frame budget lapses <i>inside</i> a tick and never between one and the next — so sorting the
/// batch near-first visits near cells in the first frame of the flood-on-open's 188-frame tick and
/// then not again for three seconds. What makes near water tick at 2 Hz is far cells being
/// <b>deferred past the boundary</b>, which is what this file does.
/// </para>
/// <para>
/// <b>Admission is a function of the sets and never of the clock</b>, which is what keeps
/// <i>a tick run whole lands the same water as a tick run in slices</i> — asserted cell for cell
/// since M8b's tick budget slice. A far floor expressed as a share of each frame's budget would
/// interleave far visits between near ones under clock control and cost exactly that property, so
/// the floor here is a number of <b>cells admitted per tick</b>.
/// </para>
/// <para>
/// <b>Throttling rather than scheduling: with no near cell awake the tick is today's tick.</b>
/// The quota is unbounded when nothing near is awake, so the change's blast radius is exactly the
/// case it exists for, and <see cref="NearRings"/> of zero — the default, and what every fixture
/// and bench runs — skips this file altogether. The price is a cliff at the first near cell: one
/// drip beside you drops a 110,000-cell far flood to the floor's rate. Named rather than smoothed,
/// because every floor has that edge and each alternative buys it back with a second constant.
/// </para>
/// <para>
/// <b>The deferred cells stay out of the swap, and that is a cost decision with a number.</b>
/// Deferring into <c>_pending</c> would have the next <see cref="FluidSimulation.Begin"/> swap them
/// back in and partition all of them again to admit the floor — a 1.3 MiB streaming walk on the
/// flood-on-open's 110,000 cells, ~2 ms in Debug, every tick, to schedule 1.5 ms of visits. **The
/// bookkeeping would exceed the far water it admits**, and it would scale with the backlog being
/// ignored rather than with the work being done. So the carry is its own list, admitted from its
/// front, and the O(backlog) walk happens only when the viewer's chunk changes — the one event that
/// can reclassify anything.
/// </para>
/// <para>
/// <b>The near field has two kinds of viewer since 2026-09-19: the player, and every machine
/// that moves water.</b> A far outlet's mouth layer (250 L, sixteen voxels) filled in five
/// boundaries and was visited once in ~260 ticks under the quota — the pump behind it stalled
/// <c>no room</c> for the rest of the session, the hand-off never saw a rested cell there, and
/// <c>handed</c> moved only while Matthew stood on the surface (<i>"the water processing cannot
/// keep up with the 'dump' rate of the pumps"</i>). A machine reads its own water every boundary
/// and is exactly what "near" means; so <see cref="SetNearColumns"/> takes the chunk columns
/// under every pump's intake box and every outlet's mouth layer, and <see cref="IsNear"/> is the
/// rings <i>or</i> those columns. The set changes when a machine is placed or dismantled, and
/// that is the one other event that re-classifies the carry. Cost: the awake water in those
/// columns every tick — a mound draining or a sheet creeping into an intake is a few hundred cells
/// a machine — and one bigger tick when the set changes.
/// </para>
/// <para>
/// <b>The solve is seeded from what changed, not from the whole carry every pass (2026-09-19).</b>
/// <see cref="Pending"/> lists the carry so a wholly far sheet is not invisible to the solve, and
/// on the played worlds that was the worker's whole cost: 456k carried cells offered as seeds
/// every boundary, each flooded three layers before the share could freeze its sheet, 294k cells a
/// pass to level twenty sheets, the tick at 1.2 Hz with 1,261 of 1,375 boundaries waiting on it.
/// A carried cell is offered <i>once</i> — the first solve after it enters the carry — and again
/// only when it is woken again, which is the one event that can change its sheet's answer: an
/// unsettled pair needs a cell that moved, and a move wakes both cells of every pair it makes.
/// The offered mark is the bit in <c>_queued</c>, cleared by <see cref="Wake"/> on a re-wake and
/// by admission, so a re-classification keeps it. What this costs in accuracy, stated: a sheet
/// the share left alone keeps its old unsettled pairs uncounted until something near them moves,
/// so a far sheet is levelled by the solve less eagerly and by the visits under the quota
/// otherwise — the share rule's own trade, one pass further. <see cref="SolveSeedsLast"/> and
/// <see cref="SolveCarrySeedsLast"/> are the count, on the <c>settle</c> term.
/// </para>
/// <para>
/// <b>The carry stays inside <c>_queued</c>, and that is the load-bearing part.</b>
/// <see cref="AwakeHeldCells"/> walks <c>_queued</c>, and it is what the store writes and the
/// eviction lists — so a backlog held in a list the dedupe set did not know about would be a lake
/// that quietly stopped being saved. Four readers do have to know about the carry, and each has a
/// break-recipe: <see cref="ActiveCellCount"/>, <see cref="FluidTickResult.ActiveAfter"/>,
/// <see cref="Settle"/>'s loop condition, and <see cref="Pending"/>, which
/// <see cref="LevelSheets(int)"/> seeds from — a far sheet whose cells are all carried would
/// otherwise be invisible to the solve, and the share rule's early stop assumes every unsettled
/// cell neighbours a seed.
/// </para>
/// </remarks>
public sealed partial class FluidSimulation
{
    /// <summary>
    /// The least the far side is admitted per tick while any near cell is awake: 512 cells, eight
    /// granules of <see cref="VisitsPerDeadlineCheck"/>.
    /// </summary>
    /// <remarks>
    /// <b>An estimate, labelled as one, and <c>--fluidfar=</c> is the sweep</b>; a very large value
    /// is a second control arm. Eight granules is where M8b's <i>"one granule in eight"</i> ends up
    /// once it is measured in the quantity that can actually be admitted. At 2 Hz it is 1,024 far
    /// visits a second, ~2.5% of what the Debug frame budget buys.
    /// </remarks>
    public const int DefaultFarFloorCells = 512;

    // Where the viewer is and how far "near" reaches, in chunks. Rings 0 is no order at all.
    private ChunkPos _nearCentre;
    private int _nearRings;

    // Chunk columns near wherever the viewer stands: the machines' (2026-09-19). Read only at
    // Begin, on the sim thread, like the centre.
    private readonly HashSet<(int X, int Z)> _nearColumns = [];

    private int _farFloorCells = DefaultFarFloorCells;

    // Far cells deferred past a boundary, oldest first, consumed from _farHead. Never swapped,
    // and every cell in it is in _queued.
    private readonly List<VoxelPos> _farCarry = [];
    private int _farHead;

    // Set when the viewer's chunk moved, because that is the only event that can change a carried
    // cell's class. The next Begin then re-classifies the carry in the same pass it classifies the
    // tick's wakes, which is the once-per-move O(backlog) walk this design pays for.
    private bool _reclassifyCarry;

    // Scratch for the classification pass, reused so a tick allocates nothing.
    private readonly List<VoxelPos> _nearScratch = [];
    private readonly List<VoxelPos> _farScratch = [];

    // Seeds for LevelSheets when a carry exists: _pending plus the carry, in that order.
    private readonly List<VoxelPos> _seedScratch = [];

    // The carry's offered prefix: cells at [_farHead, _farOfferedEnd) have been offered to a solve
    // since they entered; those from _farOfferedEnd on are new. Carried cells woken again since
    // they were offered are listed here until the next solve takes them.
    private int _farOfferedEnd;
    private List<VoxelPos> _farRewoken = [];

    // What the solve in progress was given from the carry, marked offered when it returns: the
    // carry's count at the time (the carry grows only at Begin) and the re-woken list it took —
    // swapped out, so a cell the solve itself wakes again lands in a fresh list for the next one.
    private int _farOfferPendingTo;
    private List<VoxelPos> _farRewokenTaken = [];

    /// <summary>Seeds the last solve from <see cref="Pending"/>'s path was given.</summary>
    public int SolveSeedsLast { get; private set; }

    /// <summary>Of those, cells from the far carry — new since the last solve, or woken again.</summary>
    public int SolveCarrySeedsLast { get; private set; }

    // How much of the batch in progress is near. The batch is near-first, so the split is an index.
    private int _batchNearCount;

    /// <summary>Near cells admitted to the tick in progress, or to the last one completed.</summary>
    public int NearAdmitted => _batchNearCount;

    /// <summary>Far cells admitted to the tick in progress, or to the last one completed.</summary>
    public int FarAdmitted { get; private set; }

    /// <summary>Far cells deferred past the boundary and waiting for a later tick's quota.</summary>
    public int FarBacklog => _farCarry.Count - _farHead;

    /// <summary>The deferred cells themselves, oldest first — the carry as the quota will admit it. Valid at a boundary.</summary>
    public IEnumerable<VoxelPos> DeferredCells
    {
        get
        {
            for (int i = _farHead; i < _farCarry.Count; i++)
            {
                yield return _farCarry[i];
            }
        }
    }

    /// <summary>Near cells visited over this simulation's lifetime.</summary>
    public long NearCellsVisited { get; private set; }

    /// <summary>Far cells visited over this simulation's lifetime.</summary>
    public long FarCellsVisited { get; private set; }

    /// <summary>Ticks that deferred at least one far cell past their boundary.</summary>
    public long ThrottledTicks { get; private set; }

    /// <summary>How many chunk rings around the viewer are near. Zero is no order.</summary>
    public int NearRings => _nearRings;

    /// <summary>The chunk the near field is centred on. Meaningless while <see cref="NearRings"/> is zero.</summary>
    public ChunkPos NearCentre => _nearCentre;

    /// <summary>Chunk columns near wherever the viewer stands — the machines' (2026-09-19). Meaningless while <see cref="NearRings"/> is zero.</summary>
    public int NearColumnCount => _nearColumns.Count;

    /// <summary>True when the chunk column at (<paramref name="chunkX"/>, <paramref name="chunkZ"/>) is one <see cref="SetNearColumns"/> named.</summary>
    public bool IsNearColumn(int chunkX, int chunkZ) => _nearColumns.Contains((chunkX, chunkZ));

    /// <summary>
    /// The least far cells admitted per tick while any near cell is awake.
    /// </summary>
    /// <remarks>
    /// Settable rather than a constructor argument because it is a cost knob and not a rule: unlike
    /// <see cref="SpreadRule"/>, nothing about the wake rules depends on it, so changing it under
    /// resting water leaves no move asleep.
    /// </remarks>
    public int FarFloorCells
    {
        get => _farFloorCells;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _farFloorCells = value;
        }
    }

    /// <summary>
    /// Where the viewer is, and how many chunk rings around it are visited every tick.
    /// </summary>
    /// <param name="centre">The viewer's chunk.</param>
    /// <param name="rings">
    /// Chebyshev rings on X and Z, the chunk column's <b>full height</b> — vertical is nearly free
    /// at r6h/2v and water is vertical. Zero turns the order off, which is the default, the control
    /// arm, and what every Core fixture and every bench arm runs.
    /// </param>
    /// <remarks>
    /// Called by the game once a frame; safe to call with an unchanged centre, which costs a
    /// comparison. <b>A changed centre does not move any water</b> — it marks the carry for
    /// re-classification at the next tick boundary, so the reclassification lands where every other
    /// mutation of the active set lands.
    /// </remarks>
    public void SetNearField(ChunkPos centre, int rings)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rings);

        // The worker classifies visits against the centre, so a move lands at the next BeginTick
        // (slice 3b). The last one wins, which is what a moving viewer means.
        if (_job is not null)
        {
            _nearFieldPending = (centre, rings);
            return;
        }

        bool changed = rings != _nearRings || centre != _nearCentre;
        _nearCentre = centre;
        _nearRings = rings;

        // Turning the order off has to drain the carry too, and the same flag does it: the next
        // composition classifies every carried cell, and with rings zero they are all near. The
        // backlog term is what keeps a caller that passes zero every frame — the control arm — off
        // the classification path entirely, however much the viewer moves.
        if (changed && (rings > 0 || FarBacklog > 0))
        {
            _reclassifyCarry = true;
        }
    }

    /// <summary>
    /// The chunk columns that are near wherever the viewer stands — the machines' second kind of
    /// viewer (2026-09-19): every pump's intake box and every outlet's mouth layer, as
    /// <c>MachineTick.NearColumns</c> lists them.
    /// </summary>
    /// <param name="columns">Chunk (X, Z) columns; the whole column's height is near, as the viewer's rings are.</param>
    /// <remarks>
    /// Called by the game once a frame beside <see cref="SetNearField"/>; an unchanged set costs a
    /// comparison. <b>A changed set does not move any water</b> — it marks the carry for
    /// re-classification at the next tick boundary, the viewer's move rule, and the walk is
    /// O(backlog) once per placement rather than per frame. Under a job the set lands at the next
    /// <c>BeginTick</c>, like the centre.
    /// </remarks>
    public void SetNearColumns(IReadOnlyCollection<(int X, int Z)> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);

        if (_job is not null)
        {
            // A copy, because the tick rebuilds its set when a machine lands and the pending
            // one must be what was asked for at the time.
            _nearColumnsPending = [.. columns];
            return;
        }

        if (_nearColumns.Count == columns.Count && _nearColumns.SetEquals(columns))
        {
            return;
        }

        _nearColumns.Clear();
        foreach ((int x, int z) in columns)
        {
            _nearColumns.Add((x, z));
        }

        // The viewer-move rule: with the order on, or a carry to drain, the next composition
        // re-classifies every carried cell against the new set.
        if (_nearRings > 0 || FarBacklog > 0)
        {
            _reclassifyCarry = true;
        }
    }

    /// <summary>True when this cell's chunk is within the near rings of the viewer's, or in a machine's column.</summary>
    /// <remarks>
    /// Horizontal only. A chunk column is near or far as a whole, so a shaft under your feet is
    /// near for its whole depth, which is the point: water is vertical and the box is short.
    /// </remarks>
    private bool IsNear(VoxelPos cell)
    {
        if (_nearRings <= 0)
        {
            // No order: everything is near, which is what makes a composition that runs only to
            // drain a leftover carry produce exactly the batch the unordered arm would have.
            return true;
        }

        ChunkPos chunk = cell.Chunk;
        int dx = chunk.X - _nearCentre.X;
        int dz = chunk.Z - _nearCentre.Z;
        if (dx < 0) dx = -dx;
        if (dz < 0) dz = -dz;
        if ((dx > dz ? dx : dz) <= _nearRings)
        {
            return true;
        }

        // The arithmetic first, the lookup only for a cell the rings refused: on a reclassify
        // over a large carry that is one hash probe per far cell, once per placement.
        return _nearColumns.Count > 0 && _nearColumns.Contains((chunk.X, chunk.Z));
    }

    /// <summary>
    /// The seeds a solve from the active set takes: the pending list, the carry's cells not yet
    /// offered, and the carried cells woken again since they were. <see cref="Pending"/> is the
    /// whole set; this is the part of it that can change a sheet's answer.
    /// </summary>
    private IReadOnlyList<VoxelPos> SolveSeeds()
    {
        if (FarBacklog == 0)
        {
            // A re-wake listed while the carry still stood is a cell since admitted and visited;
            // nothing to offer, nothing to mark.
            _farRewoken.Clear();
            SolveSeedsLast = _pending.Count;
            SolveCarrySeedsLast = 0;
            return _pending;
        }

        _seedScratch.Clear();
        _seedScratch.AddRange(_pending);
        int from = Math.Max(_farOfferedEnd, _farHead);
        for (int i = from; i < _farCarry.Count; i++)
        {
            _seedScratch.Add(_farCarry[i]);
        }

        _seedScratch.AddRange(_farRewoken);
        SolveSeedsLast = _seedScratch.Count;
        SolveCarrySeedsLast = _seedScratch.Count - _pending.Count;

        _farOfferPendingTo = _farCarry.Count;
        (_farRewoken, _farRewokenTaken) = (_farRewokenTaken, _farRewoken);
        _farRewoken.Clear();
        return _seedScratch;
    }

    /// <summary>After a solve from <see cref="SolveSeeds"/>: the carry's cells it was given are marked offered and the re-woken list it took is spent.</summary>
    private void MarkCarryOffered()
    {
        if (FarBacklog == 0 && _farRewokenTaken.Count == 0)
        {
            return;
        }

        int from = Math.Max(_farOfferedEnd, _farHead);
        int to = Math.Min(_farOfferPendingTo, _farCarry.Count);
        for (int i = from; i < to; i++)
        {
            _queued[_farCarry[i]] = true;
        }

        foreach (VoxelPos cell in _farRewokenTaken)
        {
            // Still carried — a cell admitted to a batch since it was re-woken is not queued
            // any more, and a cell that left and came back is pending and unmarked by index.
            if (_queued.ContainsKey(cell))
            {
                _queued[cell] = true;
            }
        }

        _farOfferedEnd = Math.Max(_farOfferedEnd, to);
        _farRewokenTaken.Clear();
    }

    /// <summary>
    /// Turns the cells woken since the last boundary into this tick's batch: near first, then as
    /// much of the far carry as the quota admits, with the rest deferred.
    /// </summary>
    /// <remarks>
    /// Leaves <c>_queued</c> holding exactly the deferred carry, which is the invariant every other
    /// reader of the active set depends on.
    /// </remarks>
    private void ComposeBatch()
    {
        if (_nearRings <= 0 && !_reclassifyCarry && FarBacklog == 0)
        {
            // The unordered arm, and it must stay the cheapest path there is: one Clear, no
            // classification, no copy. Every figure recorded before this slice was taken here.
            _batchNearCount = _batch.Count;
            FarAdmitted = 0;
            _queued.Clear();
            return;
        }

        Classify();

        // Unbounded when nothing near is awake: throttling rather than scheduling, so a viewer
        // standing in a dry field ticks the flood exactly as the unordered arm would.
        int quota = _nearScratch.Count == 0 ? int.MaxValue : Math.Max(_farFloorCells, _nearScratch.Count);

        _batch.Clear();
        _batch.AddRange(_nearScratch);
        _batchNearCount = _batch.Count;
        FarAdmitted = AdmitFromCarry(quota);

        if (FarBacklog > 0)
        {
            ThrottledTicks++;
            // _queued must end holding exactly the carry, so the batch's own cells come out of it
            // one at a time. Cheaper than clearing and re-adding the backlog, which is the cost
            // this whole arrangement exists to avoid.
            foreach (VoxelPos cell in _batch)
            {
                _queued.Remove(cell);
            }
        }
        else
        {
            _queued.Clear();
        }
    }

    /// <summary>
    /// Splits the woken cells — and the carry, when the viewer moved — into near and far, keeping
    /// each side's relative order.
    /// </summary>
    private void Classify()
    {
        _nearScratch.Clear();
        _farScratch.Clear();

        // Carried cells first, so that a cell deferred earlier is admitted before one woken since:
        // the round-robin the carry's fairness rests on.
        if (_reclassifyCarry)
        {
            // The offered prefix survives the walk: the cells that stay far keep their order, so
            // those of the old prefix that stay are a prefix of the new carry.
            int offeredEnd = Math.Max(_farOfferedEnd, _farHead);
            int offeredKept = 0;
            for (int i = _farHead; i < _farCarry.Count; i++)
            {
                VoxelPos cell = _farCarry[i];
                if (IsNear(cell))
                {
                    _nearScratch.Add(cell);
                }
                else
                {
                    _farScratch.Add(cell);
                    if (i < offeredEnd)
                    {
                        offeredKept++;
                    }
                }
            }

            _farCarry.Clear();
            _farHead = 0;
            _farOfferedEnd = offeredKept;
            _reclassifyCarry = false;
        }

        foreach (VoxelPos cell in _batch)
        {
            (IsNear(cell) ? _nearScratch : _farScratch).Add(cell);
        }

        CompactCarry();
        _farCarry.AddRange(_farScratch);
    }

    /// <summary>Appends up to <paramref name="quota"/> cells from the front of the carry to the batch.</summary>
    private int AdmitFromCarry(int quota)
    {
        int take = Math.Min(quota, FarBacklog);
        for (int i = 0; i < take; i++)
        {
            _batch.Add(_farCarry[_farHead + i]);
        }

        _farHead += take;
        if (_farHead == _farCarry.Count)
        {
            _farCarry.Clear();
            _farHead = 0;
            _farOfferedEnd = 0;
        }

        return take;
    }

    /// <summary>Drops the consumed prefix once it is at least half the list, so the head is amortised O(1).</summary>
    private void CompactCarry()
    {
        if (_farHead > 0 && _farHead * 2 >= _farCarry.Count)
        {
            _farCarry.RemoveRange(0, _farHead);
            _farOfferedEnd = Math.Max(0, _farOfferedEnd - _farHead);
            _farHead = 0;
        }
    }

    /// <summary>Counts a slice's visits as near or far, from where the cursor was.</summary>
    private void CountVisits(int from, int to)
    {
        int near = Math.Clamp(_batchNearCount, from, to) - from;
        NearCellsVisited += near;
        FarCellsVisited += to - from - near;
    }
}
