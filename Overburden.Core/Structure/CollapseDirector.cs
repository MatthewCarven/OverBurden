using System.Diagnostics;
using Overburden.Core.Building;
using Overburden.Core.Content;
using Overburden.Core.Fluid;
using Overburden.Core.Meshing;
using Overburden.Core.Voxels;
using Overburden.Core.Worldgen;

namespace Overburden.Core.Structure;

/// <summary>One event, once it has ended — the <c>M9: event</c> line's numbers.</summary>
public readonly record struct CollapseEventSummary(
    int Id,
    VoxelPos Origin,
    int Members,
    long Pieces,
    long Voxels,
    long RescuedVoxels,
    long LostVoxels,
    double AgeSeconds,
    double PeakMultiplier,
    int PeakMino);

/// <summary>A cut made on a worker: the pieces, and the worker's milliseconds.</summary>
public readonly record struct CollapseCut(List<CollapsePiece> Pieces, double Millis);

/// <summary>What one <see cref="CollapseDirector.Frame"/> or <see cref="CollapseDirector.Boundary"/> did.</summary>
/// <param name="Written">Chunks written this call — the director's own set, overwritten by the next call.</param>
public readonly record struct CollapseStep(int Released, int Landed, int Voxels, IReadOnlyCollection<ChunkPos> Written)
{
    /// <summary>Whether anything was written — the undo's cue (part b, decision 10).</summary>
    public bool Wrote => Voxels > 0;
}

/// <summary>
/// The collapse in pieces, between the events and the world (COLLAPSE-PLAN part b, decisions 1–9):
/// it starts and joins events at the fuse's word, re-plans them from the members' settled fields,
/// releases their pieces to air, solves each landing, moves the flights, re-checks their paths,
/// and lands them as rubble — dry ones in <see cref="Frame"/>, the frame they are due, and wet ones
/// in <see cref="Boundary"/>, where the grid is the frame's. The bench's <c>--pieces</c> drives it
/// too, so the bench's oracles are about the code the game runs.
/// </summary>
/// <remarks>
/// <para>
/// <b>One reservation set for every event</b> (decision 5): the solver reads it, so no two pieces
/// rest in the same cell whichever events they came from. Events never merge; a chunk touching two
/// joins the older, whose pace is higher.
/// </para>
/// <para>
/// <b>The re-plan reads the last settled snapshot</b> (decision 7). A member's snapshot that is a new
/// object with a different at-risk set, a mino the pace has grown past, or a member just joined
/// re-cuts the queue from every member's snapshot, each voxel read back as solid from the world.
/// <see cref="CollapseEvent.TryRelease"/>'s check reads the same snapshots: the kept snapshot is a
/// plan, and the world read is the gate. A shore registers at the next settle, and a member
/// written every frame may not settle until its queue runs dry — the event then goes in waves.
/// </para>
/// <para>
/// <b>The cut can run on a worker</b> (part b-2's first step — decision 7's named lever, its trigger
/// fired on b-1's bench at 29 ms a re-plan, 133 ms at worst). The gather stays on the sim thread,
/// because it reads the world; the cut is pure and goes to <see cref="CutRunner"/>. While it runs the
/// old queue keeps releasing, and what it releases is taken out of the new queue and the new set when
/// the cut lands, so nothing falls twice and nothing released reads as rescued. One cut in flight an
/// event; a trigger meanwhile is held and cut again once it lands. An event with a cut in flight is
/// not at rest. A cut that throws throws on the sim thread when it lands, as a synchronous one would.
/// </para>
/// <para>
/// <b>A flight re-checks its path at every whole step</b> (decision 8), against the world only —
/// pieces in flight are pictures to each other. Blocked, it is re-plotted straight down from the
/// last clear anchor; blocked where it stands, it rises to the first clear anchor above, counted.
/// </para>
/// <para>
/// <b>A wet landing's water goes up to the room</b> (decision 4): the rest box grown by a brush
/// cell, bottom-up; the piece's own vacated cells; up each landing column to the first solid voxel;
/// and only then <c>displaced</c>. The cavity is not beside the landing any more — a lake fills it
/// while the piece falls — so <c>displaced 0</c> is a measurement here, not a construction.
/// </para>
/// <para>
/// The identity, over the lifetime: <c>released == landed + in flight + lost</c>, in voxels.
/// </para>
/// </remarks>
public sealed class CollapseDirector
{
    /// <summary>Milliseconds of releases a frame before only one more solve is let through.</summary>
    public const double DefaultBudgetMillis = 2.0;

    /// <summary>How long an event stays after everything in it reads at rest (decision 3's grace, an estimate).</summary>
    public const long DefaultGraceMillis = 2_000;

    /// <summary>How far a blocked piece may be lifted before it is given up as lost.</summary>
    public const int MaxLiftVoxels = 64;

    /// <summary>How far up a landing column lifted water looks for room.</summary>
    public const int MaxClimbVoxels = 256;

    private sealed class Tracked(CollapseEvent ev)
    {
        public CollapseEvent Event { get; } = ev;

        public Dictionary<ChunkPos, ChunkSupport?> PlannedFrom { get; } = [];

        public bool NeedsReplan { get; set; } = true;

        public long IdleSinceMillis { get; set; } = -1;

        public double PeakMultiplier { get; set; }

        public int PeakMino { get; set; }

        /// <summary>The cut on the worker, if one is.</summary>
        public Task<CollapseCut>? Cut { get; set; }

        /// <summary>The set that cut was made from, and its mino.</summary>
        public HashSet<VoxelPos>? Gathered { get; set; }

        public int CutMino { get; set; }

        /// <summary>Voxels released while that cut ran — taken out of it when it lands.</summary>
        public HashSet<VoxelPos> ReleasedDuringCut { get; } = [];
    }

    private readonly SupportRegion _region;
    private readonly IVoxelEditor _world;
    private readonly MaterialTable _materials;
    private readonly int _rubbleId;
    private readonly ulong _seed;
    private readonly ICollapseWater? _water;
    private readonly HashSet<VoxelPos> _reserved = [];
    private readonly List<Tracked> _tracked = [];
    private readonly List<CollapseEvent> _events = [];
    private readonly Dictionary<ChunkPos, Tracked> _memberOf = [];
    private readonly Dictionary<CollapseFlight, Tracked> _wetWaiting = [];
    private readonly List<CollapseEventSummary> _ended = [];
    private readonly HashSet<ChunkPos> _written = [];
    private readonly List<CollapseFlight> _due = [];
    private readonly List<(CollapseFlight Flight, int Step)> _steps = [];
    private int _nextId = 1;
    private long _endedRescued;
    private int _stepReleased;
    private int _stepLanded;
    private int _stepVoxels;

    public CollapseDirector(
        SupportRegion region,
        IVoxelEditor world,
        MaterialTable materials,
        int rubbleId,
        ulong seed,
        ICollapseWater? water = null)
    {
        _region = region ?? throw new ArgumentNullException(nameof(region));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _materials = materials ?? throw new ArgumentNullException(nameof(materials));
        if (!materials.IsSolid(rubbleId))
        {
            throw new ArgumentException("Rubble has to be a solid material.", nameof(rubbleId));
        }

        _rubbleId = rubbleId;
        _seed = seed;
        _water = water;
    }

    /// <summary>The pace table every event runs on.</summary>
    public CollapsePace Pace { get; init; } = CollapsePace.Default;

    /// <summary>How far a piece may steer, each way — <c>--collapsespread=</c>.</summary>
    public float SpreadMetres { get; init; } = LandingSolver.DefaultSpreadMetres;

    /// <summary>Milliseconds of releases a frame — <c>--collapsebudget=</c>.</summary>
    public double BudgetMillis { get; init; } = DefaultBudgetMillis;

    /// <summary>The grace before an event at rest ends.</summary>
    public long GraceMillis { get; init; } = DefaultGraceMillis;

    /// <summary>
    /// Where a cut runs: null cuts on the calling thread, inside <see cref="Frame"/> — the bench and
    /// the tests; the game passes <c>work => Task.Run(work)</c>. A test passes a runner it completes
    /// by hand, which is the async path made deterministic.
    /// </summary>
    public Func<Func<CollapseCut>, Task<CollapseCut>>? CutRunner { get; set; }

    /// <summary>Cuts handed to the runner, and cuts that have landed.</summary>
    public long CutsStarted { get; private set; }

    /// <inheritdoc cref="CutsStarted"/>
    public long CutsApplied { get; private set; }

    /// <summary>Queued voxels a landed cut dropped because they had been released while it ran.</summary>
    public long ReleasedDuringCuts { get; private set; }

    /// <summary>When set, told every solve with the placements it simulated — the bench's oracle input.</summary>
    public Action<CollapsePiece, LandingPlan, IReadOnlyList<LandingCandidate>>? Observer { get; set; }

    /// <summary>The live events, oldest first — what the collapse sensor reads (decision 13).</summary>
    public IReadOnlyList<CollapseEvent> Events => _events;

    /// <summary>Where every piece in flight will rest.</summary>
    public IReadOnlySet<VoxelPos> Reserved => _reserved;

    /// <summary>Events started, over the lifetime.</summary>
    public long Started { get; private set; }

    /// <summary>Events ended, over the lifetime.</summary>
    public long Ended { get; private set; }

    /// <summary>Chunks that joined an event rather than starting one, over the lifetime.</summary>
    public long Joined { get; private set; }

    /// <summary>Pieces released, over the lifetime.</summary>
    public long Released { get; private set; }

    /// <summary>Voxels released, over the lifetime.</summary>
    public long ReleasedVoxels { get; private set; }

    /// <summary>Pieces landed, over the lifetime.</summary>
    public long Landed { get; private set; }

    /// <summary>Voxels landed, over the lifetime.</summary>
    public long LandedVoxels { get; private set; }

    /// <summary>Voxels falling now, across every event.</summary>
    public long InFlightVoxels => _events.Sum(e => e.InFlightVoxels);

    /// <summary>Pieces falling now.</summary>
    public int InFlight => _events.Sum(e => e.InFlight.Count);

    /// <summary>Voxels whose landing had left the loaded world, or that no lift could clear, over the lifetime. Expected zero.</summary>
    public long LostVoxels { get; private set; }

    /// <summary>Voxels queued that left without falling — shored, mined, or propped by the pile — over the lifetime.</summary>
    public long RescuedVoxels => _endedRescued + _events.Sum(e => e.RescuedVoxels);

    /// <summary>Released voxels that were rubble already — the tumble — over the lifetime.</summary>
    public long RefallVoxels { get; private set; }

    /// <summary>Releases that found no descending placement and waited, over the lifetime.</summary>
    public long Waited { get; private set; }

    /// <summary>Queues re-cut, over the lifetime.</summary>
    public long Replans { get; private set; }

    /// <summary>Milliseconds spent gathering the members' at-risk voxels for a re-plan, summed, and the worst one.</summary>
    public double GatherMillisSum { get; private set; }

    /// <inheritdoc cref="GatherMillisSum"/>
    public double GatherMillisMax { get; private set; }

    /// <summary>Milliseconds spent cutting, summed, and the worst one.</summary>
    public double CutMillisSum { get; private set; }

    /// <inheritdoc cref="CutMillisSum"/>
    public double CutMillisMax { get; private set; }

    /// <summary>Solves run, their milliseconds summed, and the worst one.</summary>
    public long Solves { get; private set; }

    /// <inheritdoc cref="Solves"/>
    public double SolveMillisSum { get; private set; }

    /// <inheritdoc cref="Solves"/>
    public double SolveMillisMax { get; private set; }

    /// <summary>Flights whose path was found blocked and re-plotted, over the lifetime.</summary>
    public long Blocked { get; private set; }

    /// <summary>Of those, flights blocked where they stood and lifted clear.</summary>
    public long LiftedByABlock { get; private set; }

    /// <summary>Landings sent to the boundary because the copy read water in them, over the lifetime.</summary>
    public long WetLandings { get; private set; }

    /// <summary>Wet landings waiting for the boundary now.</summary>
    public int WetWaiting => _wetWaiting.Count;

    /// <summary>Millilitres lifted from landing cells, returned, and displaced, over the lifetime.</summary>
    public long LiftedMillilitres { get; private set; }

    /// <inheritdoc cref="LiftedMillilitres"/>
    public long ReturnedMillilitres { get; private set; }

    /// <inheritdoc cref="LiftedMillilitres"/>
    public long DisplacedMillilitres { get; private set; }

    /// <summary>Of the returned, millilitres that only found room up the landing column (decision 4's third step).</summary>
    public long ClimbedMillilitres { get; private set; }

    /// <summary>Whether the chunk's at-risk set is an event's.</summary>
    public bool IsMember(ChunkPos chunk) => _memberOf.ContainsKey(chunk);

    /// <summary>The event a chunk belongs to, if any.</summary>
    public CollapseEvent? EventOf(ChunkPos chunk) => _memberOf.TryGetValue(chunk, out Tracked? t) ? t.Event : null;

    /// <summary>Events that ended since the last call, for the caller's line.</summary>
    public IReadOnlyList<CollapseEventSummary> TakeEnded()
    {
        CollapseEventSummary[] ended = [.. _ended];
        _ended.Clear();
        return ended;
    }

    /// <summary>
    /// Joins the chunk to the oldest event with a member across one of its faces (decision 6). False
    /// when no member is adjacent, or the chunk is a member already.
    /// </summary>
    public bool TryJoin(ChunkPos chunk)
    {
        if (_memberOf.ContainsKey(chunk))
        {
            return false;
        }

        Tracked? oldest = null;
        for (int face = 0; face < VoxelFace.Count; face++)
        {
            if (_memberOf.TryGetValue(SupportRegion.Across(chunk, face), out Tracked? t)
                && (oldest is null || t.Event.Id < oldest.Event.Id))
            {
                oldest = t;
            }
        }

        if (oldest is null)
        {
            return false;
        }

        Admit(oldest, chunk);
        Joined++;
        return true;
    }

    /// <summary>
    /// The fuse went off on a chunk (decision 3): it joins an adjacent event, or starts one with its
    /// origin at the voxel with the deepest deficit in <paramref name="field"/> — the lowest, then
    /// hashed, on a tie. Null when nothing the field marks is solid in the world.
    /// </summary>
    public CollapseEvent? Fire(ChunkPos chunk, ChunkSupport field, long nowMillis)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (_memberOf.TryGetValue(chunk, out Tracked? member))
        {
            return member.Event;
        }

        if (TryJoin(chunk))
        {
            return _memberOf[chunk].Event;
        }

        if (!TryOrigin(chunk, field, out VoxelPos origin))
        {
            return null;
        }

        var ev = new CollapseEvent(_nextId++, origin, nowMillis, Pace, _seed, _reserved);
        var tracked = new Tracked(ev);
        _tracked.Add(tracked);
        _events.Add(ev);
        Admit(tracked, chunk);
        Started++;
        return ev;
    }

    /// <summary>
    /// One frame (decision 2): re-plans, releases within the budget, advances every flight with
    /// its path re-checked, lands the dry ones, and ends events at rest past the grace.
    /// </summary>
    public CollapseStep Frame(long nowMillis)
    {
        BeginStep();
        long started = Stopwatch.GetTimestamp();

        foreach (Tracked t in _tracked)
        {
            TryApplyCut(t);
            CheckReplan(t, nowMillis);
        }

        // Past the budget, one more solve a frame across every event — counted when a piece is
        // released, not when an event is asked, or an event with nothing due spends it for one
        // that has a piece waiting.
        bool solvedPastBudget = false;
        foreach (Tracked t in _tracked)
        {
            while (true)
            {
                bool over = Stopwatch.GetElapsedTime(started).TotalMilliseconds >= BudgetMillis;
                if (over && solvedPastBudget)
                {
                    break;
                }

                if (!t.Event.TryRelease(nowMillis, StillAtRisk, out CollapsePiece piece))
                {
                    break;
                }

                solvedPastBudget |= over;
                if (!Release(t, piece, nowMillis))
                {
                    break;
                }
            }
        }

        foreach (Tracked t in _tracked)
        {
            AdvanceAndCheck(t, nowMillis);
            _due.Clear();
            foreach (CollapseFlight flight in t.Event.InFlight)
            {
                if (flight.IsDue && !_wetWaiting.ContainsKey(flight))
                {
                    _due.Add(flight);
                }
            }

            foreach (CollapseFlight flight in _due)
            {
                TryLand(t, flight, atBoundary: false);
            }

            double age = t.Event.AgeSeconds(nowMillis);
            t.PeakMultiplier = Math.Max(t.PeakMultiplier, Pace.Multiplier(age));
            t.PeakMino = Math.Max(t.PeakMino, t.Event.PlannedMino);
        }

        EndAtRest(nowMillis);
        return EndStep();
    }

    /// <summary>
    /// The fluid boundary (decision 3): lands the flights that were due with water in their rest,
    /// lifting it, writing the rubble and returning it up to the room. Nothing to do with no water.
    /// </summary>
    public CollapseStep Boundary(long nowMillis)
    {
        BeginStep();
        if (_water is not null)
        {
            foreach ((CollapseFlight flight, Tracked t) in _wetWaiting.ToList())
            {
                _wetWaiting.Remove(flight);
                if (t.Event.InFlight.Contains(flight))
                {
                    TryLand(t, flight, atBoundary: true);
                }
            }
        }

        return EndStep();
    }

    /// <summary>
    /// The quit's rule (decision 11): every piece in flight lands at once where it would have, the
    /// water in hand. The caller has the grid; a chunk not ready for water gets its rubble anyway.
    /// </summary>
    public CollapseStep LandAll(long nowMillis)
    {
        BeginStep();
        _wetWaiting.Clear();
        foreach (Tracked t in _tracked)
        {
            foreach (CollapseFlight flight in t.Event.InFlight.ToList())
            {
                for (int attempt = 0; attempt < 3 && t.Event.InFlight.Contains(flight); attempt++)
                {
                    flight.Progress = flight.Plan.Steps;
                    TryLand(t, flight, atBoundary: true, force: true);
                }

                if (t.Event.InFlight.Contains(flight))
                {
                    LoseFlight(t, flight);
                }
            }
        }

        return EndStep();
    }

    // ---------------------------------------------------------------- the plan

    private void Admit(Tracked t, ChunkPos chunk)
    {
        t.Event.Join(chunk);
        _memberOf[chunk] = t;
        t.PlannedFrom[chunk] = null;
        t.NeedsReplan = true;
        t.IdleSinceMillis = -1;
    }

    private bool TryOrigin(ChunkPos chunk, ChunkSupport field, out VoxelPos origin)
    {
        origin = default;
        (int Deficit, int NegY, ulong Tie) best = (-1, 0, 0);
        VoxelPos o = chunk.OriginVoxel;
        for (int i = 0; i < VoxelConstants.ChunkVolume; i++)
        {
            byte value = field.Value(i);
            if (!ChunkSupport.IsAtRisk(value))
            {
                continue;
            }

            (int lx, int ly, int lz) = VoxelPos.FromIndex(i);
            var v = new VoxelPos(o.X + lx, o.Y + ly, o.Z + lz);
            if (!SolidInWorld(v))
            {
                continue;
            }

            (int, int, ulong) score = (ChunkSupport.Deficit(value), -v.Y, ~Hash64.Combine(0, v.X, v.Y, v.Z));
            if (score.CompareTo(best) > 0)
            {
                best = score;
                origin = v;
            }
        }

        return best.Deficit >= 0;
    }

    private void CheckReplan(Tracked t, long nowMillis)
    {
        bool need = t.NeedsReplan
            || (t.Cut is null ? t.Event.MinoChanged(nowMillis) : t.Event.MinoFor(nowMillis) != t.CutMino);
        foreach (ChunkPos member in t.Event.Members)
        {
            ChunkSupport? now = _region.SettledField(member);
            ChunkSupport? was = t.PlannedFrom.GetValueOrDefault(member);
            if (ReferenceEquals(now, was))
            {
                continue;
            }

            if (now is null || was is null || !now.SameAtRiskSet(was))
            {
                need = true;
            }

            t.PlannedFrom[member] = now;
        }

        if (!need)
        {
            return;
        }

        if (t.Cut is not null)
        {
            // One cut an event at a time: this one is held, and cut once the one running lands.
            t.NeedsReplan = true;
            return;
        }

        long started = Stopwatch.GetTimestamp();
        int capacity = 0;
        foreach (ChunkPos member in t.Event.Members)
        {
            capacity += t.PlannedFrom.GetValueOrDefault(member)?.AtRiskCount ?? 0;
        }

        var atRisk = new HashSet<VoxelPos>(capacity);
        foreach (ChunkPos member in t.Event.Members)
        {
            ChunkSupport? field = t.PlannedFrom.GetValueOrDefault(member);
            if (field is null || field.AtRiskCount == 0)
            {
                continue;
            }

            VoxelPos o = member.OriginVoxel;
            for (int i = 0; i < VoxelConstants.ChunkVolume; i++)
            {
                if (!field.IsAtRisk(i))
                {
                    continue;
                }

                (int lx, int ly, int lz) = VoxelPos.FromIndex(i);
                var v = new VoxelPos(o.X + lx, o.Y + ly, o.Z + lz);
                if (SolidInWorld(v))
                {
                    atRisk.Add(v);
                }
            }
        }

        double gather = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        GatherMillisSum += gather;
        GatherMillisMax = Math.Max(GatherMillisMax, gather);
        t.NeedsReplan = false;
        if (CutRunner is null)
        {
            started = Stopwatch.GetTimestamp();
            t.Event.Replan(atRisk, nowMillis);
            Replans++;
            NoteCut(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return;
        }

        CollapseEvent ev = t.Event;
        int mino = ev.MinoFor(nowMillis);
        VoxelPos origin = ev.Origin;
        ulong seed = ev.Seed;
        t.Gathered = atRisk;
        t.CutMino = mino;
        t.ReleasedDuringCut.Clear();
        t.Cut = CutRunner(() =>
        {
            long began = Stopwatch.GetTimestamp();
            List<CollapsePiece> pieces = PieceCutter.Cut(atRisk, origin, mino, seed);
            return new CollapseCut(pieces, Stopwatch.GetElapsedTime(began).TotalMilliseconds);
        });
        CutsStarted++;
    }

    /// <summary>
    /// Lands a finished cut: what was released while it ran comes out of its pieces, and the
    /// event's queue is swapped for it at the mino it was cut at. A cut that threw throws here.
    /// </summary>
    private void TryApplyCut(Tracked t)
    {
        if (t.Cut is not { IsCompleted: true } task)
        {
            return;
        }

        t.Cut = null;
        HashSet<VoxelPos> fresh = t.Gathered!;
        t.Gathered = null;
        CollapseCut cut = task.GetAwaiter().GetResult();
        List<CollapsePiece> pieces = cut.Pieces;
        if (t.ReleasedDuringCut.Count > 0)
        {
            // Out of the pieces only: the set is read for the old queue's rescued count, and what
            // was released is no longer queued, so it cannot count there either way (the battery's
            // recipe that left it in the set was an identity).
            pieces = new List<CollapsePiece>(cut.Pieces.Count);
            foreach (CollapsePiece piece in cut.Pieces)
            {
                if (!piece.Voxels.Any(t.ReleasedDuringCut.Contains))
                {
                    pieces.Add(piece);
                    continue;
                }

                VoxelPos[] kept = piece.Voxels.Where(v => !t.ReleasedDuringCut.Contains(v)).ToArray();
                ReleasedDuringCuts += piece.Voxels.Length - kept.Length;
                if (kept.Length == 0)
                {
                    continue;
                }

                int shift = piece.MinoVoxels == 1 ? 0 : piece.MinoVoxels == 2 ? 1 : 2;
                (int X, int Y, int Z)[] minos = kept.Select(v => (v.X >> shift, v.Y >> shift, v.Z >> shift)).Distinct().ToArray();
                pieces.Add(piece with { Voxels = kept, Minos = minos });
            }

            t.ReleasedDuringCut.Clear();
        }

        t.Event.ApplyCut(fresh, pieces, t.CutMino);
        Replans++;
        CutsApplied++;
        NoteCut(cut.Millis);
    }

    private void NoteCut(double millis)
    {
        CutMillisSum += millis;
        CutMillisMax = Math.Max(CutMillisMax, millis);
    }

    private bool StillAtRisk(CollapsePiece piece)
    {
        foreach (VoxelPos v in piece.Voxels)
        {
            if (!SolidInWorld(v))
            {
                return false;
            }

            // Belt and braces: the frame re-cut from these same snapshots before releasing, so a
            // queued voxel is marked here by construction — b-1's battery could not make this
            // half decide anything. The world read above is the live gate (a voxel mined since).
            ChunkSupport? field = _region.SettledField(v.Chunk);
            if (field is null || !field.IsAtRisk(v.LocalIndex))
            {
                return false;
            }
        }

        return true;
    }

    // ---------------------------------------------------------------- the release and the flight

    private bool Release(Tracked t, CollapsePiece piece, long nowMillis)
    {
        List<LandingCandidate>? seen = Observer is null ? null : [];
        long started = Stopwatch.GetTimestamp();
        LandingPlan? plan = LandingSolver.Solve(piece, _world, _materials, _reserved, t.Event.Origin, SpreadMetres, t.Event.Seed, seen);
        double millis = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        Solves++;
        SolveMillisSum += millis;
        SolveMillisMax = Math.Max(SolveMillisMax, millis);
        if (plan is null)
        {
            t.Event.Wait(piece);
            Waited++;
            return false;
        }

        Observer?.Invoke(piece, plan, seen!);
        foreach (VoxelPos v in piece.Voxels)
        {
            if (_world.TryGetMaterial(v, out int m) && m == _rubbleId)
            {
                RefallVoxels++;
            }

            _world.SetVoxel(v, MaterialIds.AirNumericId);
            _written.Add(v.Chunk);
        }

        if (t.Cut is not null)
        {
            t.ReleasedDuringCut.UnionWith(piece.Voxels);
        }

        t.Event.Launch(piece, plan, nowMillis);
        Released++;
        ReleasedVoxels += piece.Voxels.Length;
        _stepReleased++;
        _stepVoxels += piece.Voxels.Length;
        t.IdleSinceMillis = -1;
        return true;
    }

    /// <summary>Advances the event's flights, then reads the shape at every whole step each one crossed.</summary>
    private void AdvanceAndCheck(Tracked t, long nowMillis)
    {
        _steps.Clear();
        foreach (CollapseFlight flight in t.Event.InFlight)
        {
            _steps.Add((flight, Math.Min((int)flight.Progress, flight.Plan.Steps)));
        }

        _due.Clear();
        t.Event.Advance(nowMillis, _due);
        foreach ((CollapseFlight flight, int was) in _steps)
        {
            LandingPlan plan = flight.Plan;
            int now = Math.Min((int)flight.Progress, plan.Steps);
            for (int step = was + 1; step <= now; step++)
            {
                if (!ShapeClearInWorld(plan.Shape, plan.Path[step]))
                {
                    Replot(t, flight, plan.Path[step - 1]);
                    break;
                }
            }
        }
    }

    /// <summary>A blocked flight goes on straight down from <paramref name="from"/>; blocked there too, it rises clear first.</summary>
    private void Replot(Tracked t, CollapseFlight flight, VoxelPos from)
    {
        Blocked++;
        LandingPlan old = flight.Plan;
        var own = new HashSet<VoxelPos>(old.RestVoxels);
        bool Solid(VoxelPos v) => (_reserved.Contains(v) && !own.Contains(v)) || !_world.TryGetMaterial(v, out int m) || _materials.IsSolid(m);
        bool Clear(VoxelPos at)
        {
            foreach ((int X, int Y, int Z) s in old.Shape)
            {
                if (Solid(at.Offset(s.X, s.Y, s.Z)))
                {
                    return false;
                }
            }

            return true;
        }

        VoxelPos start = from;
        int up = 0;
        while (!Clear(start) && up < MaxLiftVoxels)
        {
            start = start.Offset(0, 1, 0);
            up++;
        }

        if (!Clear(start))
        {
            LoseFlight(t, flight);
            return;
        }

        if (up > 0)
        {
            LiftedByABlock++;
        }

        int mino = flight.Piece.MinoVoxels;
        int drop = 0;
        while (drop < LandingSolver.MaxTicks && Clear(start.Offset(0, -(drop + 1), 0)))
        {
            drop++;
        }

        var path = new List<VoxelPos> { start };
        for (int fallen = mino; fallen <= drop; fallen += mino)
        {
            path.Add(start.Offset(0, -fallen, 0));
        }

        if (drop % mino != 0)
        {
            path.Add(start.Offset(0, -drop, 0));
        }

        t.Event.Replot(flight, old with { Start = start, Rest = start.Offset(0, -drop, 0), Path = path, Candidates = 0 });
    }

    // ---------------------------------------------------------------- the landing

    /// <summary>Lands a due flight if it can: lost past the loaded edge, re-plotted if its rest is taken, sent to the boundary if wet.</summary>
    private void TryLand(Tracked t, CollapseFlight flight, bool atBoundary, bool force = false)
    {
        LandingPlan plan = flight.Plan;
        bool wet = false;
        foreach (VoxelPos v in plan.RestVoxels)
        {
            if (!_world.TryGetMaterial(v, out int m))
            {
                LoseFlight(t, flight);
                return;
            }

            if (_materials.IsSolid(m))
            {
                Replot(t, flight, plan.Rest);
                return;
            }

            if (_water is not null && _world.HoldsWater(v))
            {
                wet = true;
            }
        }

        if (wet && !atBoundary)
        {
            _wetWaiting[flight] = t;
            WetLandings++;
            return;
        }

        bool lift = wet && _water is not null;
        if (lift && !force)
        {
            foreach (VoxelPos v in plan.RestVoxels)
            {
                if (!_water!.IsReady(v.Chunk))
                {
                    _wetWaiting[flight] = t;
                    return;
                }
            }
        }

        long lifted = 0;
        if (lift)
        {
            foreach (VoxelPos v in plan.RestVoxels)
            {
                if (_water!.IsReady(v.Chunk))
                {
                    lifted += _water.Lift(v);
                }
            }
        }

        foreach (VoxelPos v in plan.RestVoxels)
        {
            _world.SetVoxel(v, _rubbleId);
            _written.Add(v.Chunk);
        }

        if (lifted > 0)
        {
            ReturnUpToTheRoom(flight, lifted);
        }

        t.Event.Land(flight);
        Landed++;
        LandedVoxels += flight.Piece.Voxels.Length;
        _stepLanded++;
        _stepVoxels += flight.Piece.Voxels.Length;
    }

    /// <summary>Decision 4's order: the box grown by a brush cell, the vacated cells, up each landing column, then displaced.</summary>
    private void ReturnUpToTheRoom(CollapseFlight flight, long lifted)
    {
        ICollapseWater water = _water!;
        LandingPlan plan = flight.Plan;
        long remaining = lifted;
        VoxelPos[] rest = [.. plan.RestVoxels];
        int minX = rest.Min(v => v.X), minY = rest.Min(v => v.Y), minZ = rest.Min(v => v.Z);
        int maxX = rest.Max(v => v.X), maxY = rest.Max(v => v.Y), maxZ = rest.Max(v => v.Z);
        int grow = BrushCell.Voxels;
        for (int y = minY - grow; y <= maxY + grow && remaining > 0; y++)
        for (int x = minX - grow; x <= maxX + grow && remaining > 0; x++)
        for (int z = minZ - grow; z <= maxZ + grow && remaining > 0; z++)
        {
            remaining = water.Return(new VoxelPos(x, y, z), remaining);
        }

        foreach (VoxelPos v in flight.Piece.Voxels)
        {
            if (remaining == 0)
            {
                break;
            }

            remaining = water.Return(v, remaining);
        }

        long beforeClimb = remaining;
        foreach (IGrouping<(int X, int Z), VoxelPos> column in rest.GroupBy(v => (v.X, v.Z)).OrderBy(g => g.Key.X).ThenBy(g => g.Key.Z))
        {
            int top = column.Max(v => v.Y);
            for (int y = top + 1; y <= top + MaxClimbVoxels && remaining > 0; y++)
            {
                var cell = new VoxelPos(column.Key.X, y, column.Key.Z);
                if (!_world.TryGetMaterial(cell, out int m) || _materials.IsSolid(m))
                {
                    break;
                }

                remaining = water.Return(cell, remaining);
            }

            if (remaining == 0)
            {
                break;
            }
        }

        ClimbedMillilitres += beforeClimb - remaining;
        if (remaining > 0)
        {
            water.DisplaceLifted(remaining);
        }

        LiftedMillilitres += lifted;
        ReturnedMillilitres += lifted - remaining;
        DisplacedMillilitres += remaining;
    }

    private void LoseFlight(Tracked t, CollapseFlight flight)
    {
        _wetWaiting.Remove(flight);
        t.Event.Lose(flight);
        LostVoxels += flight.Piece.Voxels.Length;
    }

    // ---------------------------------------------------------------- the end

    private void EndAtRest(long nowMillis)
    {
        for (int i = _tracked.Count - 1; i >= 0; i--)
        {
            Tracked t = _tracked[i];
            if (!t.Event.IsIdle || t.Cut is not null || t.NeedsReplan || !MembersAtRest(t))
            {
                t.IdleSinceMillis = -1;
                continue;
            }

            if (t.IdleSinceMillis < 0)
            {
                t.IdleSinceMillis = nowMillis;
            }

            if (nowMillis - t.IdleSinceMillis < GraceMillis)
            {
                continue;
            }

            CollapseEvent ev = t.Event;
            _tracked.RemoveAt(i);
            _events.Remove(ev);
            foreach (ChunkPos member in ev.Members)
            {
                _memberOf.Remove(member);
            }

            _endedRescued += ev.RescuedVoxels;
            Ended++;
            _ended.Add(new CollapseEventSummary(
                ev.Id, ev.Origin, ev.Members.Count, ev.Released, ev.ReleasedVoxels, ev.RescuedVoxels, ev.LostVoxels,
                ev.AgeSeconds(nowMillis), t.PeakMultiplier, t.PeakMino));
        }
    }

    /// <summary>Every member settled now and planned from — nothing new can come from them without an edit.</summary>
    private bool MembersAtRest(Tracked t)
    {
        foreach (ChunkPos member in t.Event.Members)
        {
            ChunkSupport? field = _region.SettledField(member);
            if (field is null)
            {
                continue;
            }

            if (!_region.IsSettled(member) || !ReferenceEquals(field, t.PlannedFrom.GetValueOrDefault(member)))
            {
                return false;
            }
        }

        return true;
    }

    // ---------------------------------------------------------------- small reads

    private void BeginStep()
    {
        _written.Clear();
        _stepReleased = 0;
        _stepLanded = 0;
        _stepVoxels = 0;
    }

    private CollapseStep EndStep() => new(_stepReleased, _stepLanded, _stepVoxels, _written);

    private bool SolidInWorld(VoxelPos v) => _world.TryGetMaterial(v, out int m) && _materials.IsSolid(m);

    private bool ShapeClearInWorld((int X, int Y, int Z)[] shape, VoxelPos at)
    {
        foreach ((int X, int Y, int Z) s in shape)
        {
            VoxelPos v = at.Offset(s.X, s.Y, s.Z);
            if (!_world.TryGetMaterial(v, out int m) || _materials.IsSolid(m))
            {
                return false;
            }
        }

        return true;
    }
}
