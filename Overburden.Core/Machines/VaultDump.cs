using Overburden.Core.Building;
using Overburden.Core.Fluid;
using Overburden.Core.Hydrology;
using Overburden.Core.Lighting;
using Overburden.Core.Voxels;

namespace Overburden.Core.Machines;

/// <summary>What <see cref="MachineSet.ReleaseWater"/> left a vault as.</summary>
public enum DumpEnd : byte
{
    /// <summary>It still owes something: water to come, or (until IM-5b) voxels.</summary>
    Owing,

    /// <summary>A standing vault emptied: back to <see cref="VaultPhase.Holding"/> (IM-5's pass, decision 6).</summary>
    Holding,

    /// <summary>A wreck emptied, for the caller to remove after its walk.</summary>
    SpentWreck,
}

/// <summary>What one <see cref="VaultDump.Run"/> did.</summary>
/// <param name="Owing">Vaults that owed water at the start of the boundary.</param>
/// <param name="Admitted">Of those, vaults owing for the first time this session, checked against the ceiling.</param>
/// <param name="ToLakes">Vaults whose mouth is above the seed surface, released into a basin this boundary.</param>
/// <param name="ToCells">Vaults whose mouth is in the workings, poured into cells this boundary.</param>
/// <param name="Unloaded">Vaults in the workings whose mouth's chunk is not loaded to the water: owed.</param>
/// <param name="Unanalysed">Vaults on the surface whose sample is off the window: owed.</param>
/// <param name="NoRoom">Vaults in the workings resting after a whole pass of their ball found no room.</param>
public readonly record struct DumpReport(
    int Owing,
    int Admitted,
    int ToLakes,
    int ToCells,
    int Unloaded,
    int Unanalysed,
    int NoRoom,
    int CellsVisited,
    long DumpedLitres,
    long SpilledLitres,
    long DumpedMillilitres,
    long DecoheredMillilitres,
    int Ended,
    int WrecksRemoved)
{
    public bool DidAnything =>
        Admitted + Ended + WrecksRemoved + CellsVisited > 0 || DumpedLitres + DumpedMillilitres + DecoheredMillilitres > 0;

    public override string ToString() =>
        $"{Owing} owing ({Admitted} new), {ToLakes} into lakes, {ToCells} into cells over {CellsVisited:N0} cell(s) visited; "
        + $"dumped {DumpedLitres:N0} L ({SpilledLitres:N0} L spilled) + {DumpedMillilitres:N0} mL, decohered {DecoheredMillilitres:N0} mL; "
        + $"owed: {Unloaded} unloaded, {Unanalysed} unanalysed, {NoRoom} no room; {Ended} back to holding, {WrecksRemoved} wreck(s) removed";
}

/// <summary>
/// The dump's water (IM-5a; IDEAL-MACHINES-PLAN decisions 30–35, as IM-5's pass took them, decisions 2–10):
/// what a vault whose fuse burnt out, or a wreck, owes the world, released at the boundary into the lake
/// its mouth stands over or into the cells around it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Where it goes</b> (decision 2, with the re-read of 2026-10-09). The mouth is the cell above the vault's
/// top, as an outlet's. Above the seed surface, inside the window, the water is Tier A's, loaded or not: a
/// lake's band floor is the seed surface plus one, so "in a lake" never says more than "above the seed". It
/// goes to the basin of the vault's own sample, asked every boundary because an install renumbers basins,
/// which since the raised watershed is where the vault's own bump drains. At or under the seed surface (a
/// vault in the workings) it is Tier B's once the mouth's chunk is loaded to the water. Otherwise it is
/// owed, and waits on the row.
/// </para>
/// <para>
/// <b>A lake's share is paced by the release clock</b> (decision 3): the fuse's field, set to
/// <see cref="ReleaseMillis"/> when the fuse burns out or a wreck is made. Each boundary pays
/// <c>ceil(owed litres ÷ boundaries left)</c>, so any size lands in about ten seconds, through
/// <see cref="WaterSystem.Dump"/>; what is under a litre at the end is decohered.
/// </para>
/// <para>
/// <b>The cells' share is a ball</b> (decision 4): around the mouth, sized to hold what is owed and never under
/// <see cref="MinBallRadius"/>, its chunk columns' lake bands covered when a pass starts, filled bottom up layer
/// by layer through <see cref="FluidSimulation.RoomFor"/> and <see cref="FluidSimulation.Dump"/>, its cursor
/// kept between boundaries. A cell whose chunk is not loaded to the water is passed over, never asked: the
/// field arm answers <c>CanHold</c> from baked bits for a chunk that has gone. <see cref="CellsPerBoundary"/>
/// cells are visited a boundary across every ball, shared round-robin by position with the remainder to the
/// first. A whole pass that lands nothing rests the ball for <see cref="NoRoomWaitMillis"/>, so a vault in a
/// full room costs nothing while it waits.
/// </para>
/// <para>
/// <b>The ceiling</b> (decision 5): the vaults in the workings together owe the cells at most
/// <see cref="CeilingMillilitres"/>. A vault is checked the first boundary this session it is seen owing, in
/// position order, and what it would add past the ceiling is decohered at once, whole. Lakes have no
/// ceiling. The check is in memory, and idempotent across a reopen: what was admitted fitted, and owed water
/// only falls.
/// </para>
/// <para>
/// <b>Its own doors and its own <see cref="Exact"/></b> (decision 10), the siphon's pattern: Tier A's
/// <c>dumped</c>, Tier B's <c>dumped</c>, and the store's <c>dumped</c> and <c>decohered</c>, each checked
/// against this class's counters, with the store's water line beside them.
/// </para>
/// </remarks>
public sealed class VaultDump
{
    /// <summary>The release clock a lake's share is paced by: ten seconds, the fuse's shortest (decision 3).</summary>
    public const int ReleaseMillis = 10_000;

    /// <summary>Tier B cells visited a boundary across every ball: the dump's own budget, the siphon's figure (decision 4).</summary>
    public const int CellsPerBoundary = 3_200;

    /// <summary>What the vaults in the workings may owe the cells together: 10 ML (decision 5; IM-0 measured 10 ML carried at 2 Hz).</summary>
    public const long CeilingMillilitres = 10_000_000L * FluidConstants.MillilitresPerLitre;

    /// <summary>The smallest ball, in voxels: two metres, so a small dump's ball reaches the floor around its vault's own cell.</summary>
    public const int MinBallRadius = 8;

    /// <summary>How long a ball rests after a whole pass found no room, in boundary milliseconds.</summary>
    public const int NoRoomWaitMillis = 10_000;

    private readonly MachineSet _machines;
    private readonly FluidSimulation _fluid;
    private readonly IFluidTerrain _terrain;
    private readonly ISurfaceHeights? _seed;
    private readonly WaterSystem? _water;
    private readonly LakeCeiling? _ceiling;
    private readonly long _boundaryMillis;
    private readonly Action<VoxelPos>? _submerged;

    private readonly HashSet<BrushCell> _admitted = [];
    private readonly Dictionary<BrushCell, Ball> _balls = [];
    private readonly Dictionary<ChunkPos, bool> _ready = [];
    private readonly List<MachineRow> _owing = [];
    private readonly List<MachineRow> _empty = [];
    private readonly List<MachineRow> _toCells = [];
    private readonly List<MachineRow> _spent = [];
    private readonly HashSet<BrushCell> _seen = [];
    private readonly Dictionary<BrushCell, int> _visitedBy = [];

    private readonly long _dumpedBBase, _dumpedABase, _storeDumpedBase, _storeDecoheredBase;

    /// <summary>A ball's pass, kept between boundaries: its centre and radius, fixed at the pass's start, and the cursor.</summary>
    private sealed class Ball
    {
        public bool Started { get; set; }

        public VoxelPos Centre { get; set; }

        public int Radius { get; set; }

        public int Dy { get; set; }

        public int Dx { get; set; }

        public int Dz { get; set; }

        public bool LandedThisPass { get; set; }

        public long RestMillis { get; set; }
    }

    /// <param name="machines">The rows; the dump releases through the store's own door and removes spent wrecks.</param>
    /// <param name="fluid">Tier B, at a boundary.</param>
    /// <param name="terrain">The live simulation terrain, the tick's: whether a chunk is loaded to the water.</param>
    /// <param name="seed">The seed surface, which decides a mouth's tier; null sends every vault to the cells.</param>
    /// <param name="water">Tier A; null leaves a vault on the surface owing.</param>
    /// <param name="ceiling">The lake bands a ball covers before it pours; null with <paramref name="water"/>.</param>
    /// <param name="boundaryMillis">How much of a second one boundary is — what the release clock runs down by.</param>
    /// <param name="submerged">What a cover wakes, as the relay's and the siphon's do.</param>
    public VaultDump(
        MachineSet machines,
        FluidSimulation fluid,
        IFluidTerrain terrain,
        ISurfaceHeights? seed = null,
        WaterSystem? water = null,
        LakeCeiling? ceiling = null,
        long boundaryMillis = MachineTick.DefaultBoundaryMillis,
        Action<VoxelPos>? submerged = null)
    {
        _machines = machines ?? throw new ArgumentNullException(nameof(machines));
        _fluid = fluid ?? throw new ArgumentNullException(nameof(fluid));
        _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(boundaryMillis);
        _seed = seed;
        _water = water;
        _ceiling = ceiling;
        _boundaryMillis = boundaryMillis;
        _submerged = submerged;

        _dumpedBBase = fluid.DumpedMillilitres;
        _dumpedABase = water?.DumpedLitres ?? 0;
        _storeDumpedBase = machines.WaterDumpedMillilitres;
        _storeDecoheredBase = machines.WaterDecoheredMillilitres;
    }

    // ---- the dump's own counters, which Exact checks every twin against ----

    /// <summary>Whole litres released into lakes, spilled ones included.</summary>
    public long DumpedLitres { get; private set; }

    /// <summary>Of <see cref="DumpedLitres"/>, litres a basin spilled out of the world — <see cref="WaterSystem.SpilledLitres"/> counts them too.</summary>
    public long SpilledLitres { get; private set; }

    /// <summary>Millilitres poured into cells.</summary>
    public long DumpedMillilitres { get; private set; }

    /// <summary>Millilitres decohered: past the ceiling, and under a litre at the end of a lake's release.</summary>
    public long DecoheredMillilitres { get; private set; }

    /// <summary>Of <see cref="DecoheredMillilitres"/>, what the ceiling took.</summary>
    public long CeilingDecoheredMillilitres { get; private set; }

    /// <summary>Vaults checked against the ceiling: one per vault seen owing for the first time this session.</summary>
    public long Admitted { get; private set; }

    /// <summary>Tier B cells visited, over the lifetime.</summary>
    public long CellsVisited { get; private set; }

    /// <summary>Ball passes that landed nothing and rested, over the lifetime.</summary>
    public long NoRoomRests { get; private set; }

    /// <summary>Vault-boundaries owed for an unloaded mouth, and for a sample off the window.</summary>
    public long OwedUnloaded { get; private set; }

    public long OwedUnanalysed { get; private set; }

    /// <summary>Boundaries run.</summary>
    public long Boundaries { get; private set; }

    /// <summary>The last boundary's report.</summary>
    public DumpReport Last { get; private set; }

    /// <summary>The cells each ball visited at the last boundary, by vault — the round-robin's shares, for a test.</summary>
    internal IReadOnlyDictionary<BrushCell, int> VisitedLast => _visitedBy;

    /// <summary>
    /// Every twin against the dump's own counters, since construction, and the store's water line: Tier B's
    /// <c>dumped</c>, Tier A's, the store's <c>dumped</c> (both tiers, a thousand to the litre) and its
    /// <c>decohered</c>. The machine layer's rule in the dump's shape.
    /// </summary>
    public bool Exact =>
        _fluid.DumpedMillilitres - _dumpedBBase == DumpedMillilitres
        && (_water?.DumpedLitres ?? 0) - _dumpedABase == DumpedLitres
        && _machines.WaterDumpedMillilitres - _storeDumpedBase == DumpedMillilitres + (DumpedLitres * FluidConstants.MillilitresPerLitre)
        && _machines.WaterDecoheredMillilitres - _storeDecoheredBase == DecoheredMillilitres
        && _machines.StoreWaterExact;

    /// <summary>
    /// One boundary (decision 9: after the machine tick, so a fuse that burns out releases in the same
    /// boundary): every owing vault by position, the new ones checked against the ceiling, then each lake's
    /// share, then the balls within the budget; last, the wrecks this left empty removed.
    /// </summary>
    public DumpReport Run()
    {
        Boundaries++;
        _visitedBy.Clear();
        _owing.Clear();
        _empty.Clear();
        _toCells.Clear();
        _spent.Clear();
        _ready.Clear();
        _seen.Clear();

        long dumpedA = DumpedLitres, spilled = SpilledLitres, dumpedB = DumpedMillilitres, decohered = DecoheredMillilitres;
        long ended = _machines.DumpsEnded;
        int admitted = 0, toLakes = 0, toCells = 0, unloaded = 0, unanalysed = 0, noRoom = 0, visited = 0;

        foreach (MachineRow row in _machines.Rows)
        {
            if (!_machines.IsVault(row) || !VaultState.TryRead(row, out VaultContents contents) || !contents.Owes)
            {
                continue;
            }

            if (contents.WaterMillilitres > 0)
            {
                _owing.Add(row);
                _seen.Add(row.Cell);
            }
            else if (contents.IsEmpty)
            {
                // Owing nothing at all: no path leaves a vault so, but a row read from disk is healed
                // rather than left dumping for ever — ended here, and counted with the rest.
                _empty.Add(row);
            }
        }

        foreach (MachineRow row in _empty)
        {
            Release(row, dumped: 0, decohere: 0, clock: 0);
        }

        int owing = _owing.Count;
        _owing.Sort(MachineTick.CompareOriginOf);
        _admitted.IntersectWith(_seen);
        foreach (BrushCell gone in _balls.Keys.Where(cell => !_seen.Contains(cell)).ToList())
        {
            _balls.Remove(gone);
        }

        // Decision 5: the ceiling, for each vault seen owing for the first time, in position order.
        long inCells = 0;
        foreach (MachineRow row in _owing)
        {
            if (_admitted.Contains(row.Cell) && BoundForCells(row))
            {
                inCells += WaterOf(row);
            }
        }

        foreach (MachineRow row in _owing)
        {
            if (!_admitted.Add(row.Cell))
            {
                continue;
            }

            admitted++;
            Admitted++;
            if (!BoundForCells(row))
            {
                continue;
            }

            long water = WaterOf(row);
            long excess = Math.Max(0, water - Math.Max(0, CeilingMillilitres - inCells));
            inCells += water - excess;
            if (excess > 0)
            {
                Release(row, dumped: 0, decohere: excess, ClockOf(row));
                CeilingDecoheredMillilitres += excess;
            }
        }

        // Decisions 2, 3 and 7: each vault's tier, its lake share, or its wait.
        foreach (MachineRow row in _owing)
        {
            if (!VaultState.TryRead(row, out VaultContents contents) || !contents.Owes || contents.WaterMillilitres == 0)
            {
                continue;
            }

            int clock = contents.FuseMillisLeft;
            int nextClock = (int)Math.Max(0, clock - _boundaryMillis);
            if (!BoundForCells(row))
            {
                if (TryBasinOf(row, out int basin))
                {
                    ReleaseToLake(row, contents.WaterMillilitres, basin, clock, nextClock);
                    toLakes++;
                }
                else
                {
                    unanalysed++;
                    OwedUnanalysed++;
                    Tick(row, clock, nextClock);
                }

                continue;
            }

            if (!Ready(MouthOf(row).Chunk))
            {
                unloaded++;
                OwedUnloaded++;
                Tick(row, clock, nextClock);
                continue;
            }

            Ball ball = BallOf(row);
            if (ball.RestMillis > 0)
            {
                ball.RestMillis = Math.Max(0, ball.RestMillis - _boundaryMillis);
                noRoom++;
                Tick(row, clock, nextClock);
                continue;
            }

            _toCells.Add(row);
        }

        // Decision 4: the balls, within one budget, round-robin by position with the remainder to the first.
        if (_toCells.Count > 0)
        {
            int share = CellsPerBoundary / _toCells.Count;
            int remainder = CellsPerBoundary % _toCells.Count;
            for (int i = 0; i < _toCells.Count; i++)
            {
                MachineRow row = _toCells[i];
                int cells = PourBall(row, share + (i == 0 ? remainder : 0), ref noRoom);
                _visitedBy[row.Cell] = cells;
                visited += cells;
                toCells++;
            }
        }

        CellsVisited += visited;

        // Decision 6: the wrecks this boundary emptied, after the walk.
        int removed = 0;
        foreach (MachineRow row in _spent)
        {
            if (_machines.RemoveSpentWreck(row))
            {
                removed++;
            }

            _admitted.Remove(row.Cell);
            _balls.Remove(row.Cell);
        }

        Last = new DumpReport(
            owing, admitted, toLakes, toCells, unloaded, unanalysed, noRoom, visited,
            DumpedLitres - dumpedA, SpilledLitres - spilled, DumpedMillilitres - dumpedB, DecoheredMillilitres - decohered,
            (int)(_machines.DumpsEnded - ended), removed);
        return Last;
    }

    /// <summary>A lake's share this boundary: <c>ceil(owed litres ÷ boundaries left)</c>, the sub-litre rest decohered once the litres are paid.</summary>
    private void ReleaseToLake(MachineRow row, long owed, int basin, int clock, int nextClock)
    {
        long litres = owed / FluidConstants.MillilitresPerLitre;
        long left = Math.Max(1, (clock + _boundaryMillis - 1) / _boundaryMillis);
        long pay = (litres + left - 1) / left;
        if (pay > 0)
        {
            long spilled = _water!.Dump(basin, pay);
            DumpedLitres += pay;
            SpilledLitres += spilled;
        }

        long rest = owed - (pay * FluidConstants.MillilitresPerLitre);
        long decohere = rest < FluidConstants.MillilitresPerLitre ? rest : 0;
        Release(row, pay * FluidConstants.MillilitresPerLitre, decohere, nextClock);
    }

    /// <summary>One ball's share of the budget: cells visited from its cursor, bottom up, each poured as far as it takes. Returns the cells visited.</summary>
    private int PourBall(MachineRow row, int budget, ref int noRoom)
    {
        Ball ball = BallOf(row);
        VaultState.TryRead(row, out VaultContents contents);
        long remaining = contents.WaterMillilitres;
        int clock = contents.FuseMillisLeft;
        int nextClock = (int)Math.Max(0, clock - _boundaryMillis);

        if (!ball.Started)
        {
            StartPass(ball, row, remaining);
        }

        long landed = 0;
        int visited = 0;
        while (visited < budget && remaining > 0)
        {
            VoxelPos cell = ball.Centre.Offset(ball.Dx, ball.Dy, ball.Dz);
            bool passEnded = !Advance(ball);
            visited++;
            if (Ready(cell.Chunk))
            {
                long room = _fluid.RoomFor(cell);
                if (room > 0)
                {
                    long give = Math.Min(room, remaining);
                    long accepted = give - _fluid.Dump(cell, give);
                    landed += accepted;
                    remaining -= accepted;
                }
            }

            if (passEnded)
            {
                ball.Started = false;
                if (!ball.LandedThisPass && landed == 0)
                {
                    // A whole pass found no room: the ball rests, so a vault in a full room costs nothing while it waits.
                    ball.RestMillis = NoRoomWaitMillis;
                    NoRoomRests++;
                    noRoom++;
                }

                break;
            }
        }

        if (landed > 0)
        {
            ball.LandedThisPass = true;
            DumpedMillilitres += landed;
            Release(row, landed, decohere: 0, nextClock);
        }
        else
        {
            Tick(row, clock, nextClock);
        }

        return visited;
    }

    /// <summary>
    /// A new pass: the radius that holds what is owed (never under <see cref="MinBallRadius"/>), the centre at
    /// the mouth's bottom layer, and the box's chunk columns covered so a cell in a lake reads as no room.
    /// </summary>
    private void StartPass(Ball ball, MachineRow row, long owed)
    {
        long cells = (owed + FluidConstants.CellCapacityMillilitres - 1) / FluidConstants.CellCapacityMillilitres;
        int radius = (int)Math.Ceiling(Math.Cbrt(cells * 3.0 / (4.0 * Math.PI)));
        ball.Radius = Math.Max(MinBallRadius, radius);
        ball.Centre = MouthOf(row).Offset(BrushCell.Voxels / 2, 0, BrushCell.Voxels / 2);
        ball.Started = true;
        ball.LandedThisPass = false;
        ball.Dy = -ball.Radius;
        ball.Dx = -HalfWidth(ball.Radius, ball.Dy);
        ball.Dz = -HalfWidth(ball.Radius, ball.Dy, ball.Dx);

        int r = ball.Radius;
        _ceiling?.Cover(ball.Centre.Offset(-r, -r, -r), ball.Centre.Offset(r, r, r), _submerged);
    }

    /// <summary>Moves the cursor one cell through the ball: Z, then X, then up a layer. False when the pass is done.</summary>
    private static bool Advance(Ball ball)
    {
        int r = ball.Radius;
        if (ball.Dz < HalfWidth(r, ball.Dy, ball.Dx))
        {
            ball.Dz++;
            return true;
        }

        if (ball.Dx < HalfWidth(r, ball.Dy))
        {
            ball.Dx++;
            ball.Dz = -HalfWidth(r, ball.Dy, ball.Dx);
            return true;
        }

        if (ball.Dy < r)
        {
            ball.Dy++;
            ball.Dx = -HalfWidth(r, ball.Dy);
            ball.Dz = -HalfWidth(r, ball.Dy, ball.Dx);
            return true;
        }

        return false;
    }

    /// <summary>The largest <c>w</c> with <c>w² ≤ r² − a² − b²</c>: the ball's half-width along the next axis.</summary>
    private static int HalfWidth(int r, int a, int b = 0)
    {
        long rest = ((long)r * r) - ((long)a * a) - ((long)b * b);
        if (rest < 0)
        {
            return -1;
        }

        long w = (long)Math.Sqrt(rest);
        while (w * w > rest) w--;
        while ((w + 1) * (w + 1) <= rest) w++;
        return (int)w;
    }

    private Ball BallOf(MachineRow row)
    {
        if (!_balls.TryGetValue(row.Cell, out Ball? ball))
        {
            ball = new Ball();
            _balls[row.Cell] = ball;
        }

        return ball;
    }

    /// <summary>The store's door, and a spent wreck noted for after the walk.</summary>
    private void Release(MachineRow row, long dumped, long decohere, int clock)
    {
        if (_machines.ReleaseWater(row, dumped, decohere, clock) == DumpEnd.SpentWreck)
        {
            _spent.Add(row);
        }

        DecoheredMillilitres += decohere;
    }

    /// <summary>The release clock run down a boundary on a vault that moved nothing; no write once it is at zero.</summary>
    private void Tick(MachineRow row, int clock, int nextClock)
    {
        if (clock != nextClock)
        {
            Release(row, dumped: 0, decohere: 0, nextClock);
        }
    }

    /// <summary>
    /// Whether the vault's water is the cells' (decision 2): its mouth at or under the seed surface at its
    /// origin, or no seed surface to ask — the unloaded-outlet rule's line, <c>RoomAt</c>'s.
    /// </summary>
    private bool BoundForCells(MachineRow row)
    {
        if (_seed is null)
        {
            return true;
        }

        VoxelPos origin = row.Origin;
        return MouthOf(row).Y <= _seed.SurfaceHeightAt(origin.X, origin.Z);
    }

    /// <summary>The basin under the vault's own sample, asked now; false off the window or with no window installed.</summary>
    private bool TryBasinOf(MachineRow row, out int basin)
    {
        basin = Watershed.Outlet;
        if (_water?.Water is null)
        {
            return false;
        }

        VoxelPos origin = row.Origin;
        (int sampleX, int sampleZ) = _water.SampleOf(origin.X, origin.Z);
        return _water.TryBasinAtSample(sampleX, sampleZ, out basin);
    }

    /// <summary>Loaded to the water, asked once a chunk a boundary.</summary>
    private bool Ready(ChunkPos chunk)
    {
        if (!_ready.TryGetValue(chunk, out bool ready))
        {
            ready = _terrain.IsLoaded(chunk.OriginVoxel);
            _ready[chunk] = ready;
        }

        return ready;
    }

    private static VoxelPos MouthOf(MachineRow row) => row.Cell.Offset(0, 1, 0).OriginVoxel;

    private static long WaterOf(MachineRow row) =>
        VaultState.TryRead(row, out VaultContents contents) ? contents.WaterMillilitres : 0;

    private static int ClockOf(MachineRow row) =>
        VaultState.TryRead(row, out VaultContents contents) ? contents.FuseMillisLeft : 0;
}
