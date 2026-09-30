using Overburden.Core.Voxels;
using Overburden.Core.Worldgen;

namespace Overburden.Core.Structure;

/// <summary>A piece on its way down: its plan, and how many of the plan's ticks it has fallen.</summary>
public sealed class CollapseFlight
{
    internal CollapseFlight(CollapsePiece piece, LandingPlan plan, long launchedAtMillis, int[]? materials = null)
    {
        Piece = piece;
        Plan = plan;
        Materials = materials ?? [];
        LaunchedAtMillis = launchedAtMillis;
        AdvancedToMillis = launchedAtMillis;
    }

    /// <summary>World time its progress is counted to: its own, from its launch, so a piece launched between two advances is not credited the time before it.</summary>
    internal long AdvancedToMillis { get; set; }

    /// <summary>The piece.</summary>
    public CollapsePiece Piece { get; }

    /// <summary>Where it rests and how it gets there — replaced when its path is found blocked (part b, decision 8).</summary>
    public LandingPlan Plan { get; internal set; }

    /// <summary>World time at release.</summary>
    public long LaunchedAtMillis { get; }

    /// <summary>
    /// What each of the piece's voxels was when it let go, in <see cref="CollapsePiece.Voxels"/>'s
    /// order — the picture's materials (part c, decision 2); empty when the launcher gave none.
    /// Where each is drawn is <see cref="LandingPlan.VoxelCells"/>.
    /// </summary>
    public int[] Materials { get; }

    /// <summary>The cells the piece is drawn in now: the shape at <see cref="Anchor"/>.</summary>
    public IEnumerable<VoxelPos> DrawnCells
    {
        get
        {
            VoxelPos at = Anchor;
            foreach ((int X, int Y, int Z) s in Plan.Shape)
            {
                yield return at.Offset(s.X, s.Y, s.Z);
            }
        }
    }

    /// <summary>Ticks fallen, fractional: the event's pace moves it, a tick at a time.</summary>
    public double Progress { get; internal set; }

    /// <summary>Whether it has fallen every tick of its plan — due to be written where it rests.</summary>
    public bool IsDue => Progress >= Plan.Steps;

    /// <summary>The anchor it is drawn at now: the plan's path at the whole tick fallen.</summary>
    public VoxelPos Anchor => Plan.Path[Math.Min((int)Progress, Plan.Steps)];
}

/// <summary>
/// One collapse (COLLAPSE-PLAN decisions 3, 4 and 5): an origin, an age, member chunks, the queue of
/// pieces cut from their at-risk sets, and the pieces in flight. <b>It writes nothing</b> — the
/// caller gathers the at-risk voxels, writes a released piece's voxels to air, solves its landing
/// and writes the rubble; the event decides when, in what order, and keeps the count.
/// </summary>
/// <remarks>
/// <para>
/// <b>Stored nowhere</b>, like the timers (M9-PLAN decision 7): a quit forgets the event and its
/// age. The caller lands everything in flight before a save (decision 6).
/// </para>
/// <para>
/// <b>The rescue is the re-plan.</b> The queue is cut from one settled snapshot; the caller re-plans
/// when a settle lands or the mino grows, and the voxels that were queued and are not in the new
/// set left without falling — shored, or mined — and are counted as rescued. A piece the caller
/// finds no longer at risk at its release is dropped the same way.
/// </para>
/// <para>
/// The identity: <c>released voxels == landed voxels + voxels in flight + voxels lost at the edge</c>,
/// printed by the caller.
/// </para>
/// <para>
/// <b>The reservations can be shared</b> (part b, decision 5): the director hands every event one
/// set, so two events near each other never solve two pieces into the same cells. An event made
/// on its own keeps its own.
/// </para>
/// </remarks>
public sealed class CollapseEvent
{
    private readonly List<CollapsePiece> _queue = [];
    private readonly List<CollapseFlight> _inFlight = [];
    private readonly HashSet<VoxelPos> _reserved;
    private readonly HashSet<ChunkPos> _members = [];
    private int _next;
    private long _nextReleaseMillis;
    private int _plannedMino;

    /// <summary>A new event, started at <paramref name="startMillis"/> from <paramref name="origin"/>.</summary>
    public CollapseEvent(int id, VoxelPos origin, long startMillis, CollapsePace pace, ulong worldSeed)
        : this(id, origin, startMillis, pace, worldSeed, [])
    {
    }

    /// <summary>A new event reserving its landings in <paramref name="reserved"/>, which other events share.</summary>
    internal CollapseEvent(int id, VoxelPos origin, long startMillis, CollapsePace pace, ulong worldSeed, HashSet<VoxelPos> reserved)
    {
        ArgumentNullException.ThrowIfNull(pace);
        ArgumentNullException.ThrowIfNull(reserved);
        _reserved = reserved;
        Id = id;
        Origin = origin;
        StartMillis = startMillis;
        Pace = pace;
        Seed = Hash64.Mix(worldSeed ^ (ulong)startMillis ^ Hash64.Combine(worldSeed, origin.X, origin.Y, origin.Z));
        _nextReleaseMillis = startMillis;
        _members.Add(origin.Chunk);
    }

    /// <summary>The event's number, for the log.</summary>
    public int Id { get; }

    /// <summary>Where it started: the front spreads from here, and debris spreads away from it.</summary>
    public VoxelPos Origin { get; }

    /// <summary>World time at the start.</summary>
    public long StartMillis { get; }

    /// <summary>The pace table.</summary>
    public CollapsePace Pace { get; }

    /// <summary>The event's own seed: the cut, the jitter and the solver's last tie break on it.</summary>
    public ulong Seed { get; }

    /// <summary>The chunks whose at-risk sets are this event's.</summary>
    public IReadOnlySet<ChunkPos> Members => _members;

    /// <summary>Where pieces in flight will rest — solid to the next piece's solve.</summary>
    public IReadOnlySet<VoxelPos> Reserved => _reserved;

    /// <summary>The pieces falling now.</summary>
    public IReadOnlyList<CollapseFlight> InFlight => _inFlight;

    /// <summary>Pieces still to release.</summary>
    public int Queued => _queue.Count - _next;

    /// <summary>The next <paramref name="count"/> pieces to release, in order — the corruption the renderer draws on the queue (part c, decision 7).</summary>
    public IReadOnlyList<CollapsePiece> Peek(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        int n = Math.Min(count, _queue.Count - _next);
        return n <= 0 ? [] : _queue.GetRange(_next, n);
    }

    /// <summary>Pieces released.</summary>
    public long Released { get; private set; }

    /// <summary>Voxels released.</summary>
    public long ReleasedVoxels { get; private set; }

    /// <summary>Pieces landed.</summary>
    public long Landed { get; private set; }

    /// <summary>Voxels landed.</summary>
    public long LandedVoxels { get; private set; }

    /// <summary>Voxels in flight now.</summary>
    public long InFlightVoxels => _inFlight.Sum(f => (long)f.Piece.Voxels.Length);

    /// <summary>Pieces whose landing had left the loaded world by the time they were due.</summary>
    public long Lost { get; private set; }

    /// <summary>Voxels in those pieces — the identity's fourth term, expected zero.</summary>
    public long LostVoxels { get; private set; }

    /// <summary>Voxels that were queued and left without falling: shored, or mined.</summary>
    public long RescuedVoxels { get; private set; }

    /// <summary>Releases that found no descending placement and waited.</summary>
    public long Waited { get; private set; }

    /// <summary>The mino the queue was cut at.</summary>
    public int PlannedMino => _plannedMino;

    /// <summary>Seconds since the start.</summary>
    public double AgeSeconds(long nowMillis) => Math.Max(0, nowMillis - StartMillis) / 1000.0;

    /// <summary>Adds a chunk to the event: its at-risk set is the event's from the next re-plan.</summary>
    public bool Join(ChunkPos chunk) => _members.Add(chunk);

    /// <summary>Whether the mino the queue was cut at is no longer the pace's — the caller re-plans.</summary>
    public bool MinoChanged(long nowMillis) => _plannedMino != 0 && Pace.MinoVoxels(AgeSeconds(nowMillis)) != _plannedMino;

    /// <summary>
    /// Cuts the queue afresh from the members' at-risk voxels as they stand, at the pace's mino
    /// for this age. Queued voxels not in the new set are counted rescued. Voxels in flight must
    /// not be in the set (they are air in the world, so a caller reading solid voxels has none).
    /// </summary>
    public void Replan(IReadOnlyCollection<VoxelPos> atRisk, long nowMillis)
    {
        ArgumentNullException.ThrowIfNull(atRisk);
        var fresh = atRisk as IReadOnlySet<VoxelPos> ?? new HashSet<VoxelPos>(atRisk);
        int mino = MinoFor(nowMillis);
        ApplyCut(fresh, PieceCutter.Cut(fresh, Origin, mino, Seed), mino);
    }

    /// <summary>The mino the pace gives this age — what a cut started now is cut at.</summary>
    public int MinoFor(long nowMillis) => Pace.MinoVoxels(AgeSeconds(nowMillis));

    /// <summary>
    /// Swaps in a queue cut elsewhere — on a worker (part b-2) — from <paramref name="fresh"/> at
    /// <paramref name="mino"/>. Queued voxels not in <paramref name="fresh"/> are counted rescued,
    /// as at a re-plan; the caller has taken out of both whatever was released while it was cut.
    /// </summary>
    internal void ApplyCut(IReadOnlySet<VoxelPos> fresh, List<CollapsePiece> pieces, int mino)
    {
        ArgumentNullException.ThrowIfNull(fresh);
        ArgumentNullException.ThrowIfNull(pieces);
        for (int i = _next; i < _queue.Count; i++)
        {
            foreach (VoxelPos v in _queue[i].Voxels)
            {
                if (!fresh.Contains(v))
                {
                    RescuedVoxels++;
                }
            }
        }

        _plannedMino = mino;
        _queue.Clear();
        _queue.AddRange(pieces);
        _next = 0;
    }

    /// <summary>
    /// The next piece, when one is due at this time: pieces the caller says are no longer at risk
    /// are dropped as rescued on the way. Sets the next release by the pace at this age, spread by
    /// a seeded draw.
    /// </summary>
    public bool TryRelease(long nowMillis, Func<CollapsePiece, bool> stillAtRisk, out CollapsePiece piece)
    {
        ArgumentNullException.ThrowIfNull(stillAtRisk);
        piece = null!;
        if (nowMillis < _nextReleaseMillis)
        {
            return false;
        }

        while (_next < _queue.Count)
        {
            CollapsePiece candidate = _queue[_next++];
            if (!stillAtRisk(candidate))
            {
                RescuedVoxels += candidate.Voxels.Length;
                continue;
            }

            float unit = Hash64.ToUnitFloat(Hash64.Combine(Seed, (int)Released, 0, 0));
            double interval = Pace.ReleaseIntervalSeconds(AgeSeconds(nowMillis), unit);
            // From the last due time rather than from now, so frame granularity does not slow the
            // pace; but never more than one interval behind now, so a stall does not bank a burst.
            long step = Math.Max(1, (long)Math.Round(interval * 1000.0));
            _nextReleaseMillis = Math.Max(_nextReleaseMillis, nowMillis - step) + step;

            piece = candidate;
            return true;
        }

        return false;
    }

    /// <summary>A released piece found no descending placement: it goes back to the front of the queue.</summary>
    public void Wait(CollapsePiece piece)
    {
        ArgumentNullException.ThrowIfNull(piece);
        Waited++;
        _next--;
        if (_next < 0 || !ReferenceEquals(_queue[_next], piece))
        {
            throw new InvalidOperationException("Only the piece just released can wait.");
        }
    }

    /// <summary>A released piece, its voxels now air, is falling along its plan.</summary>
    public CollapseFlight Launch(CollapsePiece piece, LandingPlan plan, long nowMillis, int[]? materials = null)
    {
        ArgumentNullException.ThrowIfNull(piece);
        ArgumentNullException.ThrowIfNull(plan);
        var flight = new CollapseFlight(piece, plan, nowMillis, materials);
        _inFlight.Add(flight);
        foreach (VoxelPos v in plan.RestVoxels)
        {
            _reserved.Add(v);
        }

        Released++;
        ReleasedVoxels += piece.Voxels.Length;
        return flight;
    }

    /// <summary>
    /// Moves every piece in flight by the time since the last call, at the pace for this age —
    /// scaled per flight by <paramref name="speed"/> when given (water, part c decision 8) — and
    /// adds the ones now due to <paramref name="due"/>. A due piece stays in flight — reserved
    /// — until the caller has written it and calls <see cref="Land"/>.
    /// </summary>
    public void Advance(long nowMillis, List<CollapseFlight> due, Func<CollapseFlight, double>? speed = null)
    {
        ArgumentNullException.ThrowIfNull(due);
        double age = AgeSeconds(nowMillis);
        foreach (CollapseFlight flight in _inFlight)
        {
            double dt = Math.Max(0, nowMillis - flight.AdvancedToMillis) / 1000.0;
            flight.AdvancedToMillis = Math.Max(flight.AdvancedToMillis, nowMillis);
            if (!flight.IsDue)
            {
                double factor = speed?.Invoke(flight) ?? 1.0;
                flight.Progress = Math.Min(flight.Plan.Steps, flight.Progress + (dt * factor / Pace.StepSeconds(age, flight.Piece.MinoVoxels)));
            }

            if (flight.IsDue)
            {
                due.Add(flight);
            }
        }
    }

    /// <summary>A due piece has been written where it rests.</summary>
    public void Land(CollapseFlight flight)
    {
        ArgumentNullException.ThrowIfNull(flight);
        if (!flight.IsDue || !_inFlight.Remove(flight))
        {
            throw new InvalidOperationException("Only a due piece in flight can land.");
        }

        foreach (VoxelPos v in flight.Plan.RestVoxels)
        {
            _reserved.Remove(v);
        }

        Landed++;
        LandedVoxels += flight.Piece.Voxels.Length;
    }

    /// <summary>
    /// A flight's path was found blocked: it goes on along <paramref name="plan"/> from its start,
    /// with its reservation moved to the new rest. Its own clock is kept.
    /// </summary>
    internal void Replot(CollapseFlight flight, LandingPlan plan)
    {
        ArgumentNullException.ThrowIfNull(flight);
        ArgumentNullException.ThrowIfNull(plan);
        if (!_inFlight.Contains(flight))
        {
            throw new InvalidOperationException("Only a piece in flight can be re-plotted.");
        }

        foreach (VoxelPos v in flight.Plan.RestVoxels)
        {
            _reserved.Remove(v);
        }

        foreach (VoxelPos v in plan.RestVoxels)
        {
            _reserved.Add(v);
        }

        flight.Plan = plan;
        flight.Progress = 0;
    }

    /// <summary>A due piece whose landing left the loaded world: out of flight, unreserved, and counted lost.</summary>
    internal void Lose(CollapseFlight flight)
    {
        ArgumentNullException.ThrowIfNull(flight);
        if (!_inFlight.Remove(flight))
        {
            throw new InvalidOperationException("Only a piece in flight can be lost.");
        }

        foreach (VoxelPos v in flight.Plan.RestVoxels)
        {
            _reserved.Remove(v);
        }

        Lost++;
        LostVoxels += flight.Piece.Voxels.Length;
    }

    /// <summary>Lands everything in flight at once — the quit's rule (decision 6) — adding each to <paramref name="landed"/> for the caller to write.</summary>
    public void LandAll(List<CollapseFlight> landed)
    {
        ArgumentNullException.ThrowIfNull(landed);
        foreach (CollapseFlight flight in _inFlight.ToList())
        {
            flight.Progress = flight.Plan.Steps;
            landed.Add(flight);
            Land(flight);
        }
    }

    /// <summary>Whether nothing is queued and nothing is in flight.</summary>
    public bool IsIdle => Queued == 0 && _inFlight.Count == 0;
}
