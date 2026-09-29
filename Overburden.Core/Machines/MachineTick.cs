using Overburden.Core.Building;
using Overburden.Core.Content;
using Overburden.Core.Fluid;
using Overburden.Core.Hydrology;
using Overburden.Core.Lighting;
using Overburden.Core.Voxels;
using Overburden.Core.Diagnostics;
using Overburden.Core.Worldgen;

namespace Overburden.Core.Machines;

/// <summary>Why a pump moved nothing at a boundary. One reason a pump a boundary, the first one it met.</summary>
public enum PumpStall : byte
{
    None,

    /// <summary>No outlet on the pump's channel.</summary>
    NoOutlet,

    /// <summary>Nothing in the intake box, or the lake the mouth stands in is dry.</summary>
    NoWater,

    /// <summary>Every outlet it reached is loaded and has no room in the layer above its top.</summary>
    NoRoom,

    /// <summary>A chunk of the intake box is not loaded to the water, or an outlet is unloaded and underground.</summary>
    Unloaded,

    /// <summary>The outlet, or the lake, is on ground the watershed has not analysed — off the window.</summary>
    Unanalysed,

    /// <summary>Water, room and an outlet, and not a litre's worth of joules on its channel (M10c). <c>no water</c> beats it: a dry pump is not a starved one.</summary>
    Unpowered,
}

/// <summary>What one <see cref="MachineTick.Run"/> did.</summary>
/// <param name="GeneratedMillijoules">This boundary's generation over every channel (M10c).</param>
/// <param name="DrawnMillijoules">What the pumps paid this boundary.</param>
/// <param name="ClampedMillijoules">What no storage row could hold after the write-back — reported, never tolerated.</param>
/// <param name="PackGivenMillijoules">What the equipped pack took from its channel, last.</param>
/// <param name="HeldMillijoules">What vault bills took this boundary, from the pools and the vaults' own charges together.</param>
/// <param name="HeldFromOwnMillijoules">Of that, what the vaults' own charges paid (IM-3b).</param>
/// <param name="OwnGivenMillijoules">What the spare put into the vaults' own charges, after the consumers.</param>
/// <param name="OwnReturnedMillijoules">What own charges over a minute of their bills gave back to the pools, before the bills.</param>
public readonly record struct MachineTickReport(
    int Pumps,
    int Outlets,
    int Moved,
    long PumpedMillilitres,
    long PumpedLitres,
    long EmittedMillilitres,
    long EmittedLitres,
    int NoOutlet,
    int NoWater,
    int NoRoom,
    int Unloaded,
    int Unanalysed,
    int Unpowered = 0,
    long GeneratedMillijoules = 0,
    long DrawnMillijoules = 0,
    long ClampedMillijoules = 0,
    long PackGivenMillijoules = 0,
    long HeldMillijoules = 0,
    long HeldFromOwnMillijoules = 0,
    long OwnGivenMillijoules = 0,
    long OwnReturnedMillijoules = 0)
{
    /// <summary>Everything a pump took this boundary, in millilitres, both tiers.</summary>
    public long PumpedInAll => PumpedMillilitres + (PumpedLitres * FluidConstants.MillilitresPerLitre);

    /// <summary>Everything an outlet gave this boundary, in millilitres, both tiers.</summary>
    public long EmittedInAll => EmittedMillilitres + (EmittedLitres * FluidConstants.MillilitresPerLitre);

    /// <summary>The machine layer's own identity, per boundary: nothing is in flight after the call.</summary>
    public bool Exact => PumpedInAll == EmittedInAll;

    public override string ToString() =>
        $"{Pumps} pump(s) over {Outlets} outlet(s), {Moved} moved; pumped {PumpedMillilitres:N0} mL + {PumpedLitres:N0} L, "
        + $"emitted {EmittedMillilitres:N0} mL + {EmittedLitres:N0} L ({(Exact ? "exact" : "*** NOT EXACT ***")}); "
        + $"stalled: {NoOutlet} no outlet, {NoWater} no water, {NoRoom} no room, {Unloaded} unloaded, {Unanalysed} unanalysed, {Unpowered} unpowered; "
        + $"power: generated {GeneratedMillijoules:N0} mJ, drawn {DrawnMillijoules:N0}, clamped {ClampedMillijoules:N0}, pack given {PackGivenMillijoules:N0}, held {HeldMillijoules:N0} ({HeldFromOwnMillijoules:N0} from own charges), own given {OwnGivenMillijoules:N0}, own returned {OwnReturnedMillijoules:N0}";
}

/// <summary>
/// The machine tick (M10a-3): once a boundary, every pump moves water from its intake to an
/// outlet on its channel, end to end inside one call, with nothing in flight afterwards.
/// </summary>
/// <remarks>
/// <para>
/// <b>Connection is membership and movement is a ledger transfer at the boundary</b> — the
/// ladder's one idea. A pump and an outlet on the same channel share a pool; there is no pipe,
/// no solver and no water in transit. Per pump: the budget is <c>litres a second × the boundary
/// interval</c>; the intake is the box of cells around the body at <see cref="MachineDefinition.IntakeRadiusCells"/>,
/// or the lake its mouth stands in (M10a's pass, decision 5 — <see cref="IFluidTerrain.InLake"/>,
/// never the water table, which is a level and would let a pump in a dry pit drink a lake
/// through the rock); the offer is the smaller of the budget and what is there; and from the
/// channel's cursor over its outlets, each outlet takes the smaller of its room and what is
/// left of the offer, the intake is drained by exactly that, the outlet is given exactly that.
/// <c>pumped == emitted</c> holds per pump per boundary by construction — the room is read, then
/// exactly that much moves — and the world's total is a constant of this layer.
/// </para>
/// <para>
/// <b>The cheap read first, then the cells</b> (decision 7): up to eight <see cref="IFluidTerrain.IsLoaded"/>
/// on the box's chunks — any unloaded and the pump stalls whole, because an unloaded panel reads
/// dry and that is the conditional no that froze the water once — then up to eight
/// <see cref="FluidGrid.HeldIn"/>, all zero and it stalls <c>no water</c> having read nothing
/// else; only then the 26 cells' voxels, nearest cells first, in <c>[z][x][y]</c> order. Every
/// row ticks every boundary; a sleep set is a lever and not built, its trigger the
/// <c>machines</c> term on the boundary line.
/// </para>
/// <para>
/// <b>Whole litres cross a tier, millilitres move within Tier B</b> (decision 5). A lake intake
/// and a basin outlet count litres; a pump under two litres a second on a lake keeps an owed
/// counter that is a want and not water — <see cref="MachineRow.OwedMillilitres"/>, never
/// persisted, a quit loses a fraction of one boundary's pumping and never a litre.
/// </para>
/// <para>
/// <b>An outlet the box has not loaded is Tier A's if it stands on the analytic ground</b> (the
/// unloaded-outlet rule, M10-PLAN's third fact): its mouth above the seed surface and its
/// sample inside the window, and the litres go to the basin under it, as the hand-off would send
/// them if the chunk were loaded and the water had rested; unloaded underground it accepts
/// nothing (<c>unloaded</c>); off the window it accepts nothing (<c>unanalysed</c>) — fact 6's
/// two answers, kept apart by <see cref="WaterSystem.TryBasinAtSample"/>.
/// </para>
/// <para>
/// <b>The cursor is the channel's, in memory, reset at open</b> (decision 9): sixteen integers,
/// advanced once per pump that moved anything, so two pumps on one channel reach two outlets in
/// one boundary and one pump with two outlets alternates. Determinism is within a session from
/// the same open — pumps by priority then origin order (decision 41), outlets per channel in origin order, both rebuilt when
/// the set's version moves.
/// </para>
/// <para>
/// <b>The pool is this tick's, per channel, and nothing persists per channel</b> (M10c, decision 1;
/// the ladder's decision 9). Before the pumps: every converter's output this boundary and every
/// placed storage row's charge are summed per channel; a pump's offer is capped at what the channel
/// can pay for — <c>joulesPerLitre</c> is a millijoule a millilitre, so the bill is one integer
/// division — and a pump with water, room and an outlet but not a litre's worth of joules stalls
/// <c>unpowered</c>; after the pumps the equipped pack draws last, bounded by its recharge; then
/// what remains is written back into the channel's storage rows in origin order up to capacity,
/// and what no row holds is <c>clamped</c>, reported. Solar is one integer product (decision 2):
/// <c>ratedWatts × boundary × open / 16 × (256 − overcast) / 256 × efficiency / 1000</c>, the
/// openness sampled off <see cref="ISolarSky"/> when the column is loaded and kept on the row
/// (<see cref="ConverterState"/>) when it is not. <see cref="PowerOff"/> is the pre-M10c arm —
/// no generation, no bill, no write-back — the game's <c>--nogate</c>.
/// </para>
/// <para>
/// <b>A window is paid out of the cells between boundaries</b> (IDEAL-MACHINES-PLAN decisions 7
/// and 8): the quantum backpack holds nothing, the recharge skips it, and a tool spending through
/// it draws on its channel's enabled cells through <see cref="IChannelBank"/>, last-filled first.
/// The run sums the pool from the cells as the draws left them, so the hand's spend has already
/// happened when the consumers are paid, by priority then position (decisions 9 and 41).
/// </para>
/// <para>
/// <b>A vault carries a minute of its own bill</b> (IM-3b). Its own charge's capacity is its bill as
/// it stands for <see cref="MachineDefinition.OwnChargeMillis"/>; a charge over that goes back to the
/// pool before the bills. A bill is paid from the pool as far as it goes and from the own charge for
/// the rest, all or nothing, and unpaid only when both together fall short. After the consumers,
/// what the pool has left refills the own charges in fill order, before the pack. A paid boundary
/// pauses a lit fuse, and only an own charge that is whole again puts it out, so a fuse follows the
/// channel's average and not its best boundary: under IM-3's rule a cell holding the unpaid pool paid
/// one boundary in twenty and held a vault on a twentieth of its bill for ever.
/// </para>
/// <para>
/// <b>Weights are the ring, not a second rule</b> (M10b, decision 5). A channel's ring is its
/// outlets in origin order expanded by their <c>weight</c> port through the smooth weighted
/// round-robin — each turn picks the outlet with the largest running credit, every credit grows by
/// its weight each turn and the pick's falls by the total — so weights 5 : 1 : 1 read
/// <c>A A B A C A A</c> rather than <c>A A A A A B C</c>, and weights all one are the plain ring
/// entry for entry: the round-robin is the all-equal case (M10-NOTES piece 2). The cursor indexes
/// the ring; a pump walking it skips an outlet it already reached this boundary, so a weight of a
/// hundred is one room read, not a hundred.
/// </para>
/// </remarks>
public sealed class MachineTick : IChannelBank
{
    /// <summary>The port every channel link reads.</summary>
    public const string ChannelPort = MachineLink.ChannelVia;

    /// <summary>Channels 0..15, the ladder's rule.</summary>
    public const int Channels = 16;

    /// <summary>The boundary's length in world milliseconds — what a watt is multiplied by.</summary>
    public long BoundaryMillis => _boundaryMillis;

    /// <summary>The game's boundary interval: the automaton's 2 Hz.</summary>
    public const long DefaultBoundaryMillis = 500;

    /// <summary>Per radius, the cells of the intake box in nearest-first order — faces, then edges, then corners.</summary>
    private static readonly (int Dx, int Dy, int Dz)[][] IntakeCells = BuildIntakeCells();

    private readonly MachineSet _machines;
    private readonly MaterialTable _materials;
    private readonly FluidSimulation _fluid;
    private readonly IFluidTerrain _terrain;
    private readonly ISurfaceHeights? _seed;
    private readonly WaterSystem? _water;
    private readonly long _boundaryMillis;

    /// <summary>The <c>weight</c> port a ring reads; an outlet whose material declares none weighs one.</summary>
    public const string WeightPort = "weight";

    /// <summary>
    /// The <c>enabled</c> port a storage row may declare (Matthew, 2026-09-23 — a power toggle on
    /// the vacuum cell, for debugging). Zero takes the row out of its channel's pool: it neither
    /// supplies nor charges, and its charge stands where it was. A row whose material declares
    /// none is always on.
    /// </summary>
    public const string EnabledPort = "enabled";

    /// <summary>
    /// The port a consumer with the <c>priority</c> extra declares, 0–9, higher paid first
    /// (IDEAL-MACHINES-PLAN decision 41).
    /// </summary>
    public const string PriorityPort = MachineExtras.PriorityPort;

    /// <summary>
    /// What a consumer without the extra reads, and what every shipped one defaults to: the middle,
    /// so equal priorities are today's position order and no M10 oracle moves.
    /// </summary>
    public const long DefaultPriority = 5;

    private long _builtAtVersion = -1;

    // Decision 8: a window draws between boundaries, never inside the run; the draw asks this.
    private bool _running;
    private readonly List<(MachineRow Row, MachineDefinition Definition)> _pumps = [];
    private readonly List<MachineRow>?[] _outlets = new List<MachineRow>?[Channels];
    private readonly int[] _outletCount = new int[Channels];
    private readonly int[] _cursor = new int[Channels];
    private readonly List<ChunkPos> _boxChunks = new(8);
    private readonly HashSet<MachineRow> _reached = [];

    // The chunk columns the tick reads or writes water in, rebuilt with the rings (2026-09-19).
    private readonly HashSet<(int X, int Z)> _nearColumns = [];

    // M10c: the pool's parts, rebuilt with the rings.
    private readonly List<(MachineRow Row, MachineDefinition Definition, int Channel)> _converters = [];
    private readonly List<(MachineRow Row, MachineDefinition Definition)>?[] _storage = new List<(MachineRow, MachineDefinition)>?[Channels];
    private readonly long[] _available = new long[Channels];

    // IM-3: each channel's vaults in fill order, whose bills are paid before any consumer (decision 9).
    private readonly List<(MachineRow Row, MachineDefinition Definition)>?[] _vaults = new List<(MachineRow, MachineDefinition)>?[Channels];

    /// <param name="machines">The rows; the tick reads them and never writes a row.</param>
    /// <param name="materials">Which rows are pumps and outlets, and at what rate.</param>
    /// <param name="fluid">Tier B, at a boundary — the grid is the caller's.</param>
    /// <param name="terrain">
    /// The live simulation terrain — the field wrapped in the lake ceiling in the game, never a
    /// captured view, because <see cref="IFluidTerrain.IsLoaded"/> and <see cref="IFluidTerrain.InLake"/>
    /// are the two questions the tick turns on and a view refuses what it does not cover.
    /// </param>
    /// <param name="seed">The seed surface, for the unloaded-outlet rule; null makes every unloaded outlet <c>unloaded</c>.</param>
    /// <param name="water">Tier A; null makes every lake and basin answer a stall.</param>
    /// <param name="boundaryMillis">How much of a second one boundary is — what a rate is multiplied by.</param>
    public MachineTick(
        MachineSet machines,
        MaterialTable materials,
        FluidSimulation fluid,
        IFluidTerrain terrain,
        ISurfaceHeights? seed = null,
        WaterSystem? water = null,
        long boundaryMillis = DefaultBoundaryMillis)
    {
        ArgumentNullException.ThrowIfNull(machines);
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentNullException.ThrowIfNull(fluid);
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(boundaryMillis);

        _machines = machines;
        _materials = materials;
        _fluid = fluid;
        _terrain = terrain;
        _seed = seed;
        _water = water;
        _boundaryMillis = boundaryMillis;
    }

    /// <summary>
    /// Every pump's rate, in millilitres a second, in place of its definition's — the game's
    /// <c>--pump=&lt;L/s&gt;</c> and the bench's <c>--pumprate</c>, a sweep seam over the content's
    /// number and never the content's number. Null reads the definition.
    /// </summary>
    public long? RateOverrideMillilitresPerSecond { get; set; }

    /// <summary>
    /// The pre-M10c arm: no generation, no bill, no write-back, every pump free — the game's
    /// <c>--nogate</c>. Off by default; a caller that has no pool yet (c-1's game) turns it on
    /// and says so.
    /// </summary>
    public bool PowerOff { get; set; }

    /// <summary>What a solar converter asks at the boundary; null makes every panel generate from its last sample (dark, if it never had one).</summary>
    public ISolarSky? Sky { get; set; }

    /// <summary>The overcast as a byte, 0 clear to 255, taken once a boundary by the caller — the one site the float enters the pool (decision 10).</summary>
    public byte OvercastByte { get; set; }

    /// <summary>The pack the player carries, recharged last from its channel each boundary; null with none equipped.</summary>
    /// <remarks>Set through <see cref="EquipPack"/>, so a swap keeps the identity exact — the outgoing pack's terms stay counted.</remarks>
    public EquippedPack? Pack { get; private set; }

    /// <summary>
    /// Equips <paramref name="pack"/> — or nothing — in place of whatever was carried, and returns
    /// what the outgoing pack held, which leaves the pool: <em>stowed</em>, back in the inventory
    /// with the pack (2026-09-18, Matthew's <i>"keep its charge as an item"</i>; until then a pack
    /// back in the count was a count with no charge and the term was <c>forfeited</c>).
    /// </summary>
    /// <remarks>
    /// The outgoing pack's <c>initial</c> and <c>taken</c> move into the session's sums and its
    /// charge into <see cref="PackStowedMillijoules"/>, so <see cref="PowerExact"/> holds across
    /// any number of swaps: every millijoule the pool ever gave a pack is either in the pack
    /// carried now, was taken by a tool, or was stowed — and all three are terms. A stowed pack
    /// equipped again comes back through <c>initial</c>, the way the world file's pack does, so
    /// the round trip is two terms that cancel rather than a charge that reappears.
    /// </remarks>
    public long EquipPack(EquippedPack? pack)
    {
        long stowed = 0;
        if (Pack is { } outgoing)
        {
            stowed = outgoing.ChargeMillijoules;
            _packInitialSum += outgoing.InitialMillijoules;
            _packTakenSum += outgoing.TakenMillijoules;
            PackStowedMillijoules += stowed;
            PacksUnequipped++;

            // An unequipped window draws on nothing: a stale reference would be a door into the
            // pool whose taken no longer lands in the sums above.
            outgoing.Bank = null;
        }

        Pack = pack;
        if (pack is { IsWindow: true })
        {
            pack.Bank = this;
        }

        return stowed;
    }

    /// <summary>Millijoules stowed by packs unequipped this session — what left the pool with them, printed, never a tolerance.</summary>
    public long PackStowedMillijoules { get; private set; }

    /// <summary>
    /// Millijoules the debug fill gave the equipped pack this session — <c>Alt+P</c>'s pack half,
    /// charge from outside the pool, a term in <see cref="PowerExact"/> as a cell's
    /// <see cref="MachineSet.SetCharge"/> is through <c>entering</c>.
    /// </summary>
    /// <remarks>
    /// Found by the fresh world's first play (2026-09-19): the cells' debug fill has been in the
    /// identity since c-2 and the pack's went straight through <see cref="EquippedPack.Give"/>,
    /// the pool's own recharge path, so a pack filled from 7 kJ broke the pool identity by
    /// 3,593 kJ on every boundary for the rest of the session — 1,403 error lines. The
    /// morning's pack was full at open and the fill gave it nothing, which is why the identity
    /// read exact there.
    /// </remarks>
    public long PackDebugMillijoules { get; private set; }

    /// <summary>
    /// The pack's debug door: gives the equipped pack up to <paramref name="millijoules"/> from
    /// outside the pool and counts it, so the identity holds. Returns what went in; −1 with no pack.
    /// </summary>
    public long FillPack(long millijoules)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(millijoules);
        if (Pack is null)
        {
            return -1;
        }

        // Decision 7: a window holds nothing, so the debug fill gives it nothing — Alt+P fills
        // the cells, never the window.
        if (Pack.IsWindow)
        {
            return 0;
        }

        long given = Pack.Give(millijoules);
        PackDebugMillijoules += given;
        return given;
    }

    /// <summary>Packs unequipped this session, for the stowed term's denominator.</summary>
    public long PacksUnequipped { get; private set; }

    private long _packInitialSum;
    private long _packTakenSum;

    /// <summary>What every pack this session was handed over holding, the one carried now included — the pack's <c>loaded</c>.</summary>
    public long PackInitialMillijoules => _packInitialSum + (Pack?.InitialMillijoules ?? 0);

    /// <summary>What the tool took from every pack this session, the one carried now included.</summary>
    public long PackTakenMillijoules => _packTakenSum + (Pack?.TakenMillijoules ?? 0);

    /// <summary>Boundaries run.</summary>
    public long Boundaries { get; private set; }

    /// <summary>The shortest a vault's fuse burns: ten seconds, his collapse fuse's (decision 28).</summary>
    public const long FuseMinMillis = 10_000;

    /// <summary>The longest, at a shortfall near zero; a channel that pays nothing pulls it halfway to the minimum.</summary>
    public const long FuseMaxMillis = 20_000;

    /// <summary>
    /// What a fuse's draw is seeded from — the world's seed, set by the game — so a fuse is a property
    /// of the world and the boundary, never of <c>Random.Shared</c> (M10-PLAN D5). Zero in a test.
    /// </summary>
    public ulong FuseSeed { get; set; }

    /// <summary>Millijoules vault bills took from their channels' pools, over the lifetime — the pool identity's <c>held</c> term (decision 16).</summary>
    public long HeldMillijoules { get; private set; }

    /// <summary>Of <see cref="HeldMillijoules"/>, what the vaults' own charges paid (IM-3b's pass, decision 4) — printed, never a term of its own.</summary>
    public long HeldFromOwnMillijoules { get; private set; }

    /// <summary>Millijoules the spare put into vaults' own charges, over the lifetime (decision 6) — a transfer inside <c>stored</c>.</summary>
    public long OwnChargeGivenMillijoules { get; private set; }

    /// <summary>Millijoules own charges over a minute of their bills gave back to their pools, over the lifetime (decision 2) — a transfer too.</summary>
    public long OwnChargeReturnedMillijoules { get; private set; }

    /// <summary>Vault-boundaries a lit fuse waited on because the bill was paid (decision 5).</summary>
    public long FusesPaused { get; private set; }

    /// <summary>Millijoules in every vault's own charge now — inside <see cref="StoredMillijoules"/>, printed apart.</summary>
    public long VaultOwnChargeMillijoules => _machines.VaultOwnChargeMillijoules;

    /// <summary>Vault-boundaries whose bill was paid in full, and those that were not.</summary>
    public long VaultBillsPaid { get; private set; }

    public long VaultBillsUnpaid { get; private set; }

    /// <summary>Fuses lit by an unpaid bill, put out by an own charge whole again (IM-3b), and burned through to the dump.</summary>
    public long FusesArmed { get; private set; }

    public long FusesDisarmed { get; private set; }

    public long FusesBurnt { get; private set; }

    /// <summary>Millijoules every converter generated, over the lifetime.</summary>
    public long GeneratedMillijoules { get; private set; }

    /// <summary>Millijoules the pumps paid, over the lifetime.</summary>
    public long DrawnByPumpsMillijoules { get; private set; }

    /// <summary>Millijoules no storage row could hold after a write-back, over the lifetime.</summary>
    public long ClampedMillijoules { get; private set; }

    /// <summary>Millijoules the equipped pack took from its channel, over the lifetime.</summary>
    public long PackGivenMillijoules { get; private set; }

    /// <summary>Pump-boundaries that stalled for want of a litre's worth of joules.</summary>
    public long StalledUnpowered { get; private set; }

    /// <summary>Converter-boundaries whose openness was sampled off the sky, and those that used the row's last sample.</summary>
    public long SolarSampled { get; private set; }

    public long SolarUnsampled { get; private set; }

    /// <summary>Open columns summed over every converter-boundary, for a mean against <see cref="SolarSampled"/> + <see cref="SolarUnsampled"/>.</summary>
    public long OpenColumnsSum { get; private set; }

    /// <summary>Converters and placed storage rows as the last rebuild left them.</summary>
    public int Converters => _converters.Count;

    /// <summary>Placed storage rows switched off at the last rebuild — out of the pool, their charge still in <see cref="StoredMillijoules"/>.</summary>
    public int StorageRowsOff { get; private set; }

    /// <summary>Placed storage rows in the pool at the last rebuild; <see cref="StorageRowsOff"/> are the rest.</summary>
    public int StorageRows
    {
        get
        {
            int n = 0;
            foreach (List<(MachineRow, MachineDefinition)>? rows in _storage)
            {
                n += rows?.Count ?? 0;
            }

            return n;
        }
    }

    /// <summary>Millijoules on every placed storage row now, through the set's own definition of a charge.</summary>
    public long StoredMillijoules
    {
        get
        {
            long sum = 0;
            foreach (MachineRow row in _machines.Rows)
            {
                sum += _machines.ChargeOf(row);
            }

            return sum;
        }
    }

    /// <summary>
    /// The pool's identity over the session: every millijoule on a row (a cell's charge or a vault's
    /// own) or in the pack now is one
    /// that entered on a row, was generated, or was in a pack at hand-over, less what left on a
    /// row, what the pumps paid, what was clamped, what the tool took and what a swap stowed.
    /// No term is in flight.
    /// </summary>
    public bool PowerExact =>
        StoredMillijoules + (Pack?.ChargeMillijoules ?? 0)
        == _machines.StoredMillijoulesEntering - _machines.StoredMillijoulesLeaving
            + GeneratedMillijoules - DrawnByPumpsMillijoules - ClampedMillijoules - HeldMillijoules
            + PackInitialMillijoules + PackDebugMillijoules - PackTakenMillijoules - PackStowedMillijoules;

    /// <summary>Pump-boundaries that moved anything.</summary>
    public long Moves { get; private set; }

    /// <summary>Millilitres pumps took out of Tier B cells, over the lifetime. Equals <see cref="FluidSimulation.PumpedMillilitres"/>' growth since construction.</summary>
    public long PumpedMillilitres { get; private set; }

    /// <summary>Whole litres pumps took out of lakes, over the lifetime. Equals <see cref="WaterSystem.PumpedLitres"/>' growth.</summary>
    public long PumpedLitres { get; private set; }

    /// <summary>Millilitres outlets put into Tier B cells, over the lifetime. Equals <see cref="FluidSimulation.EmittedMillilitres"/>' growth.</summary>
    public long EmittedMillilitres { get; private set; }

    /// <summary>Whole litres outlets delivered to basins, over the lifetime. Equals <see cref="WaterSystem.EmittedLitres"/>' growth.</summary>
    public long EmittedLitres { get; private set; }

    /// <summary>Of those, litres delivered to an outlet the box had not loaded, standing on the ground — the unloaded-outlet rule.</summary>
    public long EmittedLitresToUnloadedOutlets { get; private set; }

    /// <summary>Of the litres emitted, litres delivered to an outlet whose mouth stands in a lake.</summary>
    public long EmittedLitresIntoLakes { get; private set; }

    /// <summary>Pump-boundaries that found no outlet on their channel.</summary>
    public long StalledNoOutlet { get; private set; }

    /// <summary>Pump-boundaries that found nothing to take.</summary>
    public long StalledNoWater { get; private set; }

    /// <summary>Pump-boundaries whose outlets had no room.</summary>
    public long StalledNoRoom { get; private set; }

    /// <summary>Pump-boundaries that stalled on an unloaded chunk — the intake's, or an outlet's underground.</summary>
    public long StalledUnloaded { get; private set; }

    /// <summary>Pump-boundaries that stalled on ground nobody has analysed.</summary>
    public long StalledUnanalysed { get; private set; }

    /// <summary>Intake voxels read, over the lifetime — the cost decision 7 bounds.</summary>
    public long IntakeVoxelsRead { get; private set; }

    /// <summary>Pumps whose box held nothing at all, answered from the panels' counts without a cell read.</summary>
    public long IntakesSkippedDry { get; private set; }

    /// <summary>Outlet layers read for room, over the lifetime — one per outlet a pump reached, whatever its weight.</summary>
    public long OutletRoomReads { get; private set; }

    /// <summary>The last boundary's report.</summary>
    public MachineTickReport Last { get; private set; }

    /// <summary>
    /// The machine layer's own identity over the lifetime: everything pumped, on either tier,
    /// equals everything emitted, on either tier. Nothing is in flight after a call.
    /// </summary>
    public bool CrossTierExact =>
        PumpedMillilitres + (PumpedLitres * FluidConstants.MillilitresPerLitre)
        == EmittedMillilitres + (EmittedLitres * FluidConstants.MillilitresPerLitre);

    /// <summary>The cursor on <paramref name="channel"/>: which ring entry the next pump that moves anything reaches first.</summary>
    public int CursorOn(int channel) => _cursor[channel];

    /// <summary>The ring on <paramref name="channel"/> as the last rebuild left it — the outlets expanded by weight, in turn order.</summary>
    public IReadOnlyList<MachineRow> RingOn(int channel)
    {
        if (_builtAtVersion != _machines.Version)
        {
            Rebuild();
        }

        return _outlets[channel] ?? (IReadOnlyList<MachineRow>)[];
    }

    /// <summary>The intake cells around a body at <paramref name="radiusCells"/>, nearest first — the order the read and the drain walk.</summary>
    public static ReadOnlySpan<(int Dx, int Dy, int Dz)> IntakeCellsAt(int radiusCells) =>
        IntakeCells[radiusCells];

    /// <summary>
    /// The chunk columns the tick moves water in — every pump's intake box at its radius and the
    /// box one cell around every outlet, where its mouth layer is and where the mound spills —
    /// as the set stands. The near field's second kind of viewer (2026-09-19): the game hands
    /// this to <see cref="FluidSimulation.SetNearColumns"/> so a machine's water is visited every
    /// tick wherever the player is, which is what lets a far outlet's mound drain and a far
    /// intake refill. Converters and storage read no water and list nothing.
    /// </summary>
    public IReadOnlyCollection<(int X, int Z)> NearColumns
    {
        get
        {
            if (_builtAtVersion != _machines.Version)
            {
                Rebuild();
            }

            return _nearColumns;
        }
    }

    private static readonly int WatchMachines = CallWatch.Register("machines: tick");

    /// <summary>One boundary: every pump, by priority and then in origin order (IDEAL-MACHINES-PLAN decision 41).</summary>
    /// <remarks>
    /// Refuses mid-tick through the simulation's own guard on the first <c>Pump</c> or
    /// <c>Emit</c>; a caller that reaches here inside a tick has not been taught to wait, and
    /// the grid read before the refusal would be a worker's.
    /// </remarks>
    public MachineTickReport Run()
    {
        if (_running)
        {
            throw new InvalidOperationException("The machine tick is already running; a boundary does not nest.");
        }

        _running = true;
        try
        {
            return RunBoundary();
        }
        finally
        {
            _running = false;
        }
    }

    private MachineTickReport RunBoundary()
    {
        using CallWatch.Scope callWatch = CallWatch.Time(WatchMachines);
        Boundaries++;
        if (_builtAtVersion != _machines.Version)
        {
            Rebuild();
        }

        int pumps = _pumps.Count;
        int outlets = 0;
        foreach (int count in _outletCount)
        {
            outlets += count;
        }

        int moved = 0;
        long pumpedB = 0, pumpedA = 0, emittedB = 0, emittedA = 0;
        int noOutlet = 0, noWater = 0, noRoom = 0, unloaded = 0, unanalysed = 0, unpowered = 0;

        // M10c: the pool before the pumps — what every channel can pay this boundary.
        long generated = PowerOff ? 0 : BeginPower();

        // IM-3: every vault's hold before any consumer, in fill order, each all or nothing (decision 9);
        // IM-3b: an own charge over a minute of its bill back to the pool first.
        long held = PowerOff ? 0 : PayHolds();

        foreach ((MachineRow row, MachineDefinition definition) in _pumps)
        {
            long b0 = PumpedMillilitres, a0 = PumpedLitres, eb0 = EmittedMillilitres, ea0 = EmittedLitres;
            PumpStall stall = TickPump(row, definition);
            pumpedB += PumpedMillilitres - b0;
            pumpedA += PumpedLitres - a0;
            emittedB += EmittedMillilitres - eb0;
            emittedA += EmittedLitres - ea0;

            // What the panel reads beside the number the player typed (M10b, decision 6).
            row.LastStall = stall;
            row.LastMovedMillilitres = (PumpedMillilitres - b0) + ((PumpedLitres - a0) * FluidConstants.MillilitresPerLitre);

            switch (stall)
            {
                case PumpStall.None:
                    moved++;
                    Moves++;
                    break;
                case PumpStall.NoOutlet:
                    noOutlet++;
                    StalledNoOutlet++;
                    break;
                case PumpStall.NoWater:
                    noWater++;
                    StalledNoWater++;
                    break;
                case PumpStall.NoRoom:
                    noRoom++;
                    StalledNoRoom++;
                    break;
                case PumpStall.Unloaded:
                    unloaded++;
                    StalledUnloaded++;
                    break;
                case PumpStall.Unanalysed:
                    unanalysed++;
                    StalledUnanalysed++;
                    break;
                case PumpStall.Unpowered:
                    unpowered++;
                    StalledUnpowered++;
                    break;
            }
        }

        // IM-3b: the vaults' own charges from the spare, after the consumers (decision 6); then, as
        // M10c had it, the pack last, the write-back and the clamp.
        long drawn = 0, clamped = 0, packGiven = 0, ownGiven = 0;
        if (!PowerOff)
        {
            drawn = _drawnThisBoundary;
            ownGiven = RechargeVaults();
            packGiven = RechargePack();
            clamped = WriteBack();
        }

        Last = new MachineTickReport(
            pumps, outlets, moved, pumpedB, pumpedA, emittedB, emittedA,
            noOutlet, noWater, noRoom, unloaded, unanalysed, unpowered,
            generated, drawn, clamped, packGiven, held, _heldFromOwnThisBoundary, ownGiven, _ownReturnedThisBoundary);
        return Last;
    }

    private long _drawnThisBoundary;
    private long _heldFromOwnThisBoundary;
    private long _ownReturnedThisBoundary;

    /// <summary>
    /// Every converter's output and every storage row's charge, summed per channel into what the
    /// channel can pay this boundary. Returns the generation.
    /// </summary>
    private long BeginPower()
    {
        Array.Clear(_available);
        _drawnThisBoundary = 0;
        long generated = 0;
        foreach ((MachineRow row, MachineDefinition definition, int channel) in _converters)
        {
            long output = SolarOutput(row, definition);
            _available[channel] += output;
            generated += output;
        }

        GeneratedMillijoules += generated;

        for (int channel = 0; channel < Channels; channel++)
        {
            List<(MachineRow Row, MachineDefinition Definition)>? rows = _storage[channel];
            if (rows is null)
            {
                continue;
            }

            foreach ((MachineRow row, _) in rows)
            {
                _available[channel] += StorageState.Read(row);
            }
        }

        return generated;
    }

    /// <summary>
    /// A panel's millijoules this boundary — decision 2's one integer product, the openness
    /// sampled when the sky can answer and the row's last sample otherwise.
    /// </summary>
    private long SolarOutput(MachineRow row, MachineDefinition definition)
    {
        int open;
        if (Sky is not null && Sky.TryOpenColumnsAbove(row.Cell, out open))
        {
            ConverterState.WriteOpenColumns(row, open);
            SolarSampled++;
        }
        else
        {
            open = ConverterState.ReadOpenColumns(row);
            SolarUnsampled++;
        }

        OpenColumnsSum += open;
        return SolarMillijoules(definition, open, OvercastByte, _boundaryMillis);
    }

    /// <summary>
    /// What a converter row would generate at a boundary under the overcast the tick holds, from
    /// its last-sampled openness — the panel's line, off the one definition the tick uses.
    /// </summary>
    public long SolarOutputOf(MachineRow row)
    {
        MachineDefinition? definition = _materials.Machine(row.MaterialId);
        return definition is { Kind: MachineKind.Converter }
            ? SolarMillijoules(definition, ConverterState.ReadOpenColumns(row), OvercastByte, _boundaryMillis)
            : 0;
    }

    /// <summary>Decision 2's one integer product.</summary>
    /// <remarks>
    /// Multiply then divide, each factor in turn, so 500 W under a byte of 255 reads 976 mJ and
    /// not 0: the storm dims a panel to a 256th, never off. Every factor is bounded at load so
    /// the product stays inside a long.
    /// </remarks>
    public static long SolarMillijoules(MachineDefinition definition, int openColumns, byte overcastByte, long boundaryMillis)
    {
        long millijoules = definition.RatedWatts * boundaryMillis;
        millijoules = millijoules * openColumns / ConverterState.Columns;
        millijoules = millijoules * (256 - overcastByte) / 256;
        millijoules = millijoules * definition.EfficiencyPerMille / MachineDefinition.PerMille;
        return millijoules;
    }

    /// <summary>
    /// A vault's bill for one boundary, in millijoules (decision 16; IM-3's pass, decision 6): water
    /// and blocks at their rates a cubic metre, for the boundary's milliseconds, rounded up so a vault
    /// holding anything always costs something, in 128-bit arithmetic because a full god vault's
    /// numerator is past a long.
    /// </summary>
    public static long BillOf(MachineDefinition definition, VaultContents contents, long boundaryMillis)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(contents);

        // Milliwatts a cubic metre × millilitres × milliseconds ÷ (10⁶ mL a m³ × 10³ ms a s) = millijoules.
        Int128 blocks = (Int128)definition.BlockHoldMilliwattsPerCubicMetre * contents.TotalVoxels * MachineDefinition.VoxelMillilitres;
        Int128 water = (Int128)definition.WaterHoldMilliwattsPerCubicMetre * contents.WaterMillilitres;
        Int128 numerator = (blocks + water) * boundaryMillis;
        Int128 denominator = MachineDefinition.MillilitresPerCubicMetre * 1000;
        Int128 bill = (numerator + denominator - 1) / denominator;
        return bill > long.MaxValue ? long.MaxValue : (long)bill;
    }

    /// <summary>
    /// A vault's own charge's capacity, in millijoules (IM-3b's pass, decision 2): a minute of its bill
    /// as it stands — <see cref="BillOf"/> for <see cref="MachineDefinition.OwnChargeMillis"/>, the
    /// same rounded-up arithmetic — so every vault on a dead channel holds for its minute, full or nearly
    /// empty. An empty vault's is zero; a crate's is zero.
    /// </summary>
    public static long OwnChargeCapacity(MachineDefinition definition, VaultContents contents)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return definition.OwnChargeMillis == 0 ? 0 : BillOf(definition, contents, definition.OwnChargeMillis);
    }

    /// <summary>
    /// Every vault on every channel, in fill order (decisions 27, 28 and 34, as IM-3b's pass amended
    /// them). First an own charge over a minute of its bill goes back to the pool. Then each bill is paid
    /// from the pool as far as it goes and from the vault's own charge for the rest, all or nothing: a
    /// paid bill pauses a lit fuse; an unpaid one lights it, or burns it down by the boundary's
    /// milliseconds, and a fuse burned through leaves the vault dumping with its contents owed. The fuse
    /// goes out at the recharge, never here (<see cref="RechargeVaults"/>). A wreck and a dumping vault pay
    /// nothing and are charged nothing; an empty vault costs nothing and cannot fuse. Returns what was paid.
    /// </summary>
    private long PayHolds()
    {
        long paid = 0;
        _heldFromOwnThisBoundary = 0;
        _ownReturnedThisBoundary = 0;
        for (int channel = 0; channel < Channels; channel++)
        {
            List<(MachineRow Row, MachineDefinition Definition)>? vaults = _vaults[channel];
            if (vaults is null)
            {
                continue;
            }

            // Decision 2: a withdrawal lowered the capacity; the excess is the pool's before any bill.
            foreach ((MachineRow row, MachineDefinition definition) in vaults)
            {
                if (!VaultState.TryRead(row, out VaultContents contents) || contents.Owes)
                {
                    continue;
                }

                long excess = contents.OwnChargeMillijoules - OwnChargeCapacity(definition, contents);
                if (excess > 0)
                {
                    contents.OwnChargeMillijoules -= excess;
                    VaultState.Write(row, contents);
                    _available[channel] += excess;
                    _ownReturnedThisBoundary += excess;
                }
            }

            foreach ((MachineRow row, MachineDefinition definition) in vaults)
            {
                if (!VaultState.TryRead(row, out VaultContents contents) || contents.Owes)
                {
                    row.LastHoldPaid = false;
                    continue;
                }

                // Decision 4: the pool as far as it goes, then the own charge for the rest; so the
                // first-filled take the pool in a shortage and a consumer never drinks it while a
                // vault is spending its own minute.
                long bill = BillOf(definition, contents, _boundaryMillis);
                long fromPool = Math.Min(_available[channel], bill);
                long fromOwn = bill - fromPool;
                if (fromOwn <= contents.OwnChargeMillijoules)
                {
                    _available[channel] -= fromPool;
                    paid += bill;
                    row.LastHoldPaid = true;
                    if (bill > 0) VaultBillsPaid++;

                    // Decision 5: a paid boundary pauses a lit fuse; only a whole own charge puts it out.
                    if (contents.Phase == VaultPhase.Fusing) FusesPaused++;
                    if (fromOwn > 0)
                    {
                        contents.OwnChargeMillijoules -= fromOwn;
                        _heldFromOwnThisBoundary += fromOwn;
                        VaultState.Write(row, contents);
                    }

                    continue;
                }

                // Unpaid: both together fall short, and neither is touched.
                row.LastHoldPaid = false;
                VaultBillsUnpaid++;
                if (contents.Phase == VaultPhase.Holding)
                {
                    contents.Phase = VaultPhase.Fusing;
                    contents.FuseMillisLeft = DrawFuseMillis(row, bill - fromPool - contents.OwnChargeMillijoules, bill);
                    FusesArmed++;
                }
                else
                {
                    contents.FuseMillisLeft = (int)Math.Max(0, contents.FuseMillisLeft - _boundaryMillis);
                    if (contents.FuseMillisLeft == 0)
                    {
                        contents.Phase = VaultPhase.Dumping;
                        FusesBurnt++;
                    }
                }

                VaultState.Write(row, contents);
            }
        }

        HeldMillijoules += paid;
        HeldFromOwnMillijoules += _heldFromOwnThisBoundary;
        OwnChargeReturnedMillijoules += _ownReturnedThisBoundary;
        return paid;
    }

    /// <summary>
    /// IM-3b's pass, decisions 5 and 6: what each channel's pool has left after the consumers tops its
    /// vaults' own charges up to a minute of their bills, in fill order, with no rate cap — the cells'
    /// charge counts as spare, so a vault that spent its minute at night refills beside a full cell.
    /// A lit fuse whose vault is whole again goes out here, the only place one does. Before the pack,
    /// which still draws last. Returns what went in.
    /// </summary>
    private long RechargeVaults()
    {
        long given = 0;
        for (int channel = 0; channel < Channels; channel++)
        {
            List<(MachineRow Row, MachineDefinition Definition)>? vaults = _vaults[channel];
            if (vaults is null)
            {
                continue;
            }

            foreach ((MachineRow row, MachineDefinition definition) in vaults)
            {
                if (!VaultState.TryRead(row, out VaultContents contents) || contents.Owes)
                {
                    continue;
                }

                long capacity = OwnChargeCapacity(definition, contents);
                long give = Math.Min(_available[channel], capacity - contents.OwnChargeMillijoules);
                bool changed = false;
                if (give > 0)
                {
                    contents.OwnChargeMillijoules += give;
                    _available[channel] -= give;
                    given += give;
                    changed = true;
                }

                if (contents.Phase == VaultPhase.Fusing && contents.OwnChargeMillijoules >= capacity)
                {
                    contents.Phase = VaultPhase.Holding;
                    contents.FuseMillisLeft = 0;
                    FusesDisarmed++;
                    changed = true;
                }

                if (changed)
                {
                    VaultState.Write(row, contents);
                }
            }
        }

        OwnChargeGivenMillijoules += given;
        return given;
    }

    /// <summary>
    /// A fuse's length, by the collapse scheduler's own formula (IM-3's pass, decision 7):
    /// <c>min + (max − min) × u × (1 − share / 2)</c>, the share the part of the bill the pool and the
    /// own charge together could not pay, so a vault that has nothing draws 10–15 s and one just short
    /// 10–20 s.
    /// </summary>
    private int DrawFuseMillis(MachineRow row, long shortfall, long bill)
    {
        double share = Math.Clamp(shortfall / (double)bill, 0.0, 1.0);
        float unit = Hash64.ToUnitFloat(Hash64.Combine(FuseSeed ^ (ulong)Boundaries, row.Cell.X, row.Cell.Y, row.Cell.Z));
        return (int)(FuseMinMillis + Math.Round((FuseMaxMillis - FuseMinMillis) * unit * (1.0 - (share / 2.0))));
    }

    /// <summary>The pack draws last from its channel, to its capacity and its recharge bound. Returns what it took.</summary>
    private long RechargePack()
    {
        // A window takes no charge (decision 7): it holds nothing, and its channel's cells are the bank.
        if (Pack is null || Pack.IsWindow)
        {
            return 0;
        }

        int channel = Math.Clamp(Pack.Channel, 0, Channels - 1);
        long bound = Pack.Definition.RechargeWatts is { } watts ? watts * _boundaryMillis : long.MaxValue;
        long give = Math.Min(_available[channel], Math.Min(Pack.CapacityMillijoules - Pack.ChargeMillijoules, bound));
        if (give <= 0)
        {
            return 0;
        }

        long given = Pack.Give(give);
        _available[channel] -= given;
        PackGivenMillijoules += given;
        return given;
    }

    /// <summary>
    /// What each channel still has goes back into its storage rows in origin order, each to its
    /// capacity; what no row holds is clamped. Returns the boundary's clamp.
    /// </summary>
    private long WriteBack()
    {
        long clamped = 0;
        for (int channel = 0; channel < Channels; channel++)
        {
            long remaining = _available[channel];
            List<(MachineRow Row, MachineDefinition Definition)>? rows = _storage[channel];
            if (rows is not null)
            {
                foreach ((MachineRow row, MachineDefinition definition) in rows)
                {
                    long put = Math.Min(remaining, definition.CapacityMillijoules);
                    StorageState.Write(row, put);
                    remaining -= put;
                }
            }

            clamped += remaining;
            _available[channel] = 0;
        }

        ClampedMillijoules += clamped;
        return clamped;
    }

    /// <summary>What <paramref name="channel"/>'s enabled cells hold now — the bank a window spends (decision 8).</summary>
    long IChannelBank.BankMillijoules(int channel)
    {
        if (_builtAtVersion != _machines.Version)
        {
            Rebuild();
        }

        long bank = 0;
        List<(MachineRow Row, MachineDefinition Definition)>? rows = _storage[Math.Clamp(channel, 0, Channels - 1)];
        if (rows is not null)
        {
            foreach ((MachineRow row, _) in rows)
            {
                bank += StorageState.Read(row);
            }
        }

        return bank;
    }

    /// <summary>
    /// Decision 8's draw: up to <paramref name="millijoules"/> out of the channel's enabled cells,
    /// between boundaries, last-filled first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Last-filled first</b> is the write-back read backwards: it fills the cells in position
    /// order, each to capacity, so the last cells in that order are the emptiest, and a draw that
    /// takes from them keeps the first ones full. The next boundary sums them all again, so the
    /// order only matters to a cell removed before it — whose charge leaves as <c>leaving</c>.
    /// </para>
    /// <para>
    /// <b>The charge is written directly</b>, never through <see cref="MachineSet.SetCharge"/>,
    /// which would count the fall as <c>leaving</c> too and the caller's term would count it twice.
    /// And <b>never inside the run</b>: the pool is summed at its start and written back at its end,
    /// so a draw in between would be spent twice or lost to the write-back.
    /// </para>
    /// </remarks>
    long IChannelBank.Draw(int channel, long millijoules)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(millijoules);
        if (_running)
        {
            throw new InvalidOperationException("A window draws between boundaries, never inside the machine tick's run (decision 8).");
        }

        if (_builtAtVersion != _machines.Version)
        {
            Rebuild();
        }

        List<(MachineRow Row, MachineDefinition Definition)>? rows = _storage[Math.Clamp(channel, 0, Channels - 1)];
        if (rows is null || millijoules == 0)
        {
            return 0;
        }

        long remaining = millijoules;
        for (int i = rows.Count - 1; i >= 0 && remaining > 0; i--)
        {
            MachineRow row = rows[i].Row;
            long held = StorageState.Read(row);
            long take = Math.Min(held, remaining);
            if (take <= 0)
            {
                continue;
            }

            StorageState.Write(row, held - take);
            remaining -= take;
        }

        return millijoules - remaining;
    }

    /// <summary>
    /// A consumer's priority (decision 41): its <see cref="PriorityPort"/> clamped to 0–9 when its
    /// material has the <c>priority</c> extra, else <see cref="DefaultPriority"/> — the extra's one
    /// reading site.
    /// </summary>
    public static long PriorityOf(MachineRow row, MachineDefinition definition) =>
        definition.Has(MachineExtra.Priority) && row.TryGetPort(PriorityPort, out long priority)
            ? Math.Clamp(priority, 0, 9)
            : DefaultPriority;

    /// <summary>The pumps in the order they are paid and ticked, as the last rebuild left them — priority, then position.</summary>
    internal IReadOnlyList<MachineRow> PumpOrder
    {
        get
        {
            if (_builtAtVersion != _machines.Version)
            {
                Rebuild();
            }

            return _pumps.Select(p => p.Row).ToList();
        }
    }

    /// <summary>The pumps by priority then origin order, and the per-channel outlets in origin order, from the set as it stands.</summary>
    private void Rebuild()
    {
        _pumps.Clear();
        _converters.Clear();
        StorageRowsOff = 0;
        _nearColumns.Clear();
        Array.Clear(_outlets);
        Array.Clear(_storage);
        Array.Clear(_vaults);

        foreach (MachineRow row in _machines.Rows)
        {
            MachineDefinition? definition = _materials.Machine(row.MaterialId);
            if (definition is null)
            {
                // A hole, or a material with no machine block: the row is kept and ticks nothing.
                continue;
            }

            switch (definition.Kind)
            {
                case MachineKind.Pump:
                    _pumps.Add((row, definition));
                    AddNearColumns(row.Cell, definition.IntakeRadiusCells);
                    break;
                case MachineKind.Outlet:
                    int channel = ChannelOf(row);
                    (_outlets[channel] ??= []).Add(row);
                    AddNearColumns(row.Cell, 1);
                    break;
                case MachineKind.Converter:
                    _converters.Add((row, definition, ChannelOf(row)));
                    break;
                case MachineKind.Storage when !definition.Carried:
                    // A carried storage is the pack, never a row; one that reached the set is
                    // kept and ticks nothing, like a hole. A row switched off is out of the pool
                    // the same way — never read, never written back — and still counted by
                    // StoredMillijoules, so the identity sees its charge standing still.
                    if (!IsEnabled(row))
                    {
                        StorageRowsOff++;
                        break;
                    }

                    (_storage[ChannelOf(row)] ??= []).Add((row, definition));
                    break;
                case MachineKind.Vault when definition.IsChannelVault:
                    // A crate has no channel and no bill; a channel vault's bill is read each
                    // boundary from its bytes, so a wreck or a dump in the list is simply skipped.
                    (_vaults[ChannelOf(row)] ??= []).Add((row, definition));
                    break;
            }
        }

        foreach (List<(MachineRow Row, MachineDefinition Definition)>? vaults in _vaults)
        {
            vaults?.Sort(static (a, b) =>
            {
                int byRate = a.Definition.BlockHoldMilliwattsPerCubicMetre.CompareTo(b.Definition.BlockHoldMilliwattsPerCubicMetre);
                return byRate != 0 ? byRate : CompareOrigin(a.Row, b.Row);
            });
        }

        // Decision 9 and 41: consumers are paid by priority, higher first, then by position (X, then
        // Y, then Z). Equal priorities are position order, which is every pump before IM-2.
        _pumps.Sort(static (a, b) =>
        {
            int byPriority = PriorityOf(b.Row, b.Definition).CompareTo(PriorityOf(a.Row, a.Definition));
            return byPriority != 0 ? byPriority : CompareOrigin(a.Row, b.Row);
        });
        _converters.Sort(static (a, b) => CompareOrigin(a.Row, b.Row));
        foreach (List<(MachineRow Row, MachineDefinition Definition)>? rows in _storage)
        {
            rows?.Sort(static (a, b) => CompareOrigin(a.Row, b.Row));
        }

        for (int channel = 0; channel < Channels; channel++)
        {
            List<MachineRow>? outlets = _outlets[channel];
            _outletCount[channel] = outlets?.Count ?? 0;
            if (outlets is null)
            {
                _cursor[channel] = 0;
                continue;
            }

            outlets.Sort(CompareOrigin);
            _outlets[channel] = ExpandByWeight(outlets);

            // A cursor past the end of a shrunk ring wraps rather than points at nothing.
            _cursor[channel] %= _outlets[channel]!.Count;
        }

        _builtAtVersion = _machines.Version;
    }

    /// <summary>
    /// The smooth weighted round-robin over outlets in origin order: Σweight turns, each picking
    /// the outlet with the largest running credit (first in origin order on a tie), every credit
    /// grown by its weight before the pick and the pick's shrunk by the total after it. Weights
    /// all one give the outlets in origin order, once each.
    /// </summary>
    private static List<MachineRow> ExpandByWeight(List<MachineRow> outlets)
    {
        int n = outlets.Count;
        var weight = new long[n];
        long total = 0;
        for (int i = 0; i < n; i++)
        {
            weight[i] = WeightOf(outlets[i]);
            total += weight[i];
        }

        var ring = new List<MachineRow>((int)Math.Min(total, 100_000));
        var credit = new long[n];
        for (long turn = 0; turn < total; turn++)
        {
            int pick = 0;
            for (int i = 0; i < n; i++)
            {
                credit[i] += weight[i];
                if (credit[i] > credit[pick]) pick = i;
            }

            credit[pick] -= total;
            ring.Add(outlets[pick]);
        }

        return ring;
    }

    /// <summary>The outlet's weight: its port, clamped to a positive number, or one where the material declares none.</summary>
    /// <summary>Whether the row is on: its <see cref="EnabledPort"/> non-zero, or on where the material declares none.</summary>
    public static bool IsEnabled(MachineRow row) =>
        !row.TryGetPort(EnabledPort, out long enabled) || enabled != 0;

    private static long WeightOf(MachineRow row) =>
        row.TryGetPort(WeightPort, out long weight) ? Math.Max(1, weight) : 1;

    /// <summary>Position order, X then Y then Z — the tick's tie-break, shared with the store's fill order.</summary>
    internal static int CompareOriginOf(MachineRow a, MachineRow b) => CompareOrigin(a, b);

    private static int CompareOrigin(MachineRow a, MachineRow b)
    {
        int c = a.Origin.X.CompareTo(b.Origin.X);
        if (c != 0) return c;
        c = a.Origin.Y.CompareTo(b.Origin.Y);
        return c != 0 ? c : a.Origin.Z.CompareTo(b.Origin.Z);
    }

    /// <summary>The row's channel, clamped into the ring — the restore clamps too, so this is belt and braces.</summary>
    private static int ChannelOf(MachineRow row) =>
        row.TryGetPort(ChannelPort, out long channel) ? (int)Math.Clamp(channel, 0, Channels - 1) : 0;

    private PumpStall TickPump(MachineRow row, MachineDefinition definition)
    {
        int channel = ChannelOf(row);
        List<MachineRow>? outlets = _outlets[channel];
        if (outlets is null || outlets.Count == 0)
        {
            return PumpStall.NoOutlet;
        }

        long budget = (RateOverrideMillilitresPerSecond ?? definition.MillilitresPerSecond) * _boundaryMillis / 1000;

        // The intake question is InLake at the mouth — the cell above the body's top — never the
        // table (M10a's pass, fact 5).
        VoxelPos mouth = row.Cell.Offset(0, 1, 0).OriginVoxel;
        bool lake = _water is not null && _terrain.InLake(mouth);
        int lakeBasin = Watershed.Outlet;
        long offer;
        if (lake)
        {
            (int sx, int sz) = _water!.SampleOf(mouth.X, mouth.Z);
            if (!_water.TryBasinAtSample(sx, sz, out lakeBasin) || lakeBasin == Watershed.Outlet)
            {
                return PumpStall.Unanalysed;
            }

            // Whole litres, and a want rather than water: the remainder under a litre is owed to
            // the next boundary and never persisted.
            long owed = row.OwedMillilitres + budget;
            long litres = owed / FluidConstants.MillilitresPerLitre;
            row.OwedMillilitres = (int)(owed - (litres * FluidConstants.MillilitresPerLitre));
            offer = litres * FluidConstants.MillilitresPerLitre;
            if (offer == 0)
            {
                return PumpStall.NoWater;
            }
        }
        else
        {
            int radius = definition.IntakeRadiusCells;
            if (!BoxIsLoaded(row.Cell, radius))
            {
                return PumpStall.Unloaded;
            }

            if (!BoxHoldsAnything())
            {
                IntakesSkippedDry++;
                return PumpStall.NoWater;
            }

            long inBox = ReadBox(row.Cell, radius);
            offer = Math.Min(budget, inBox);
            if (offer == 0)
            {
                return PumpStall.NoWater;
            }
        }

        // M10c: the bill. A joule a litre is a millijoule a millilitre, so what the channel can
        // pay for is one division; a lake pump's offer stays whole litres. After no water, by
        // design — a dry pump is not a starved one. (A Tier B pump pouring into a basin with
        // under a litre's worth of joules rounds its take to nothing at the outlet and reads
        // no room there; the pool accumulates and the next boundary pays, so the label is
        // one boundary's, not the stall.)
        long bill = definition.JoulesPerLitre;
        if (!PowerOff && bill > 0)
        {
            long affordable = _available[channel] / bill;
            if (lake)
            {
                affordable -= affordable % FluidConstants.MillilitresPerLitre;
            }

            if (affordable == 0)
            {
                return PumpStall.Unpowered;
            }

            offer = Math.Min(offer, affordable);
        }

        long moved = 0;
        PumpStall first = PumpStall.None;
        int count = outlets.Count;
        int start = _cursor[channel];
        _reached.Clear();
        for (int i = 0; i < count && moved < offer; i++)
        {
            MachineRow outlet = outlets[(start + i) % count];
            if (!_reached.Add(outlet))
            {
                // The ring lists a weighted outlet more than once; one room read a boundary.
                continue;
            }

            OutletRoomReads++;
            long room = RoomAt(outlet, out bool toBasin, out int basin, out bool intoLake, out PumpStall reason);
            if (room == 0)
            {
                if (first == PumpStall.None) first = reason;
                continue;
            }

            long take = Math.Min(room, offer - moved);
            if (lake || toBasin)
            {
                // Whole litres cross a tier: a cell holds 15,625 mL and a basin counts litres.
                take -= take % FluidConstants.MillilitresPerLitre;
                if (take == 0)
                {
                    if (first == PumpStall.None) first = PumpStall.NoRoom;
                    continue;
                }
            }

            // The intake is drained by exactly take — or, from a lake, by exactly what the lake
            // handed back, which may be fewer whole litres.
            if (lake)
            {
                long pumped = _water!.Pump(lakeBasin, take / FluidConstants.MillilitresPerLitre);
                PumpedLitres += pumped;
                take = pumped * FluidConstants.MillilitresPerLitre;
                if (take == 0)
                {
                    // The lake had less than a litre for it: the boundary's want is spent.
                    if (first == PumpStall.None) first = PumpStall.NoWater;
                    break;
                }
            }
            else
            {
                DrainBox(row.Cell, definition.IntakeRadiusCells, take);
                PumpedMillilitres += take;
            }

            // And the outlet is given exactly that.
            if (toBasin)
            {
                long litres = take / FluidConstants.MillilitresPerLitre;
                _water!.Emit(basin, litres);
                EmittedLitres += litres;
                if (intoLake) EmittedLitresIntoLakes += litres;
                else EmittedLitresToUnloadedOutlets += litres;
            }
            else
            {
                EmitLayer(outlet, take);
                EmittedMillilitres += take;
            }

            moved += take;
        }

        if (moved > 0)
        {
            // Paid after the move, for exactly what moved: the offer was capped at what the
            // channel could pay, so the bill can never exceed what is there.
            if (!PowerOff && bill > 0)
            {
                long cost = moved * bill;
                _available[channel] -= cost;
                _drawnThisBoundary += cost;
                DrawnByPumpsMillijoules += cost;
            }

            _cursor[channel] = (start + 1) % count;
            return PumpStall.None;
        }

        return first == PumpStall.None ? PumpStall.NoRoom : first;
    }

    /// <summary>
    /// What an outlet can take this boundary, and where it goes: the free millilitres in the
    /// layer above its top when that layer is loaded and not in a lake; whole litres to the basin
    /// under it when the layer is in a lake, or when it is unloaded with the body standing on the
    /// analytic ground inside the window; nothing otherwise, with the reason.
    /// </summary>
    private long RoomAt(MachineRow outlet, out bool toBasin, out int basin, out bool intoLake, out PumpStall reason)
    {
        toBasin = false;
        intoLake = false;
        basin = Watershed.Outlet;
        reason = PumpStall.None;

        VoxelPos mouth = outlet.Cell.Offset(0, 1, 0).OriginVoxel;
        if (_terrain.IsLoaded(mouth))
        {
            if (_water is not null && _terrain.InLake(mouth))
            {
                (int sx, int sz) = _water.SampleOf(mouth.X, mouth.Z);
                if (!_water.TryBasinAtSample(sx, sz, out basin))
                {
                    reason = PumpStall.Unanalysed;
                    return 0;
                }

                toBasin = true;
                intoLake = true;
                return long.MaxValue;
            }

            long room = 0;
            FluidGrid grid = _fluid.Grid;
            for (int lz = 0; lz < BrushCell.Voxels; lz++)
            for (int lx = 0; lx < BrushCell.Voxels; lx++)
            {
                VoxelPos voxel = outlet.Cell.VoxelAt(lx, BrushCell.Voxels, lz);
                if (_terrain.CanHold(voxel))
                {
                    room += grid.FreeAt(voxel);
                }
            }

            if (room == 0)
            {
                reason = PumpStall.NoRoom;
            }

            return room;
        }

        // Unloaded: Tier A's if it stands on the analytic ground inside the window — the
        // hand-off's rule for water that rested there, applied before it rests — and nothing
        // underground, where an outlet would be pouring into rock the world cannot see. "On the
        // ground" is the mouth's test, not the origin's: a body placed on the surface sits up to
        // three voxels into it when the surface is not on the 1 m lattice (the --pump arm carves
        // its cell; a brush refuses one and lands the cell above), and what matters is that the
        // layer the pour lands in is natural air over the seed.
        if (_water is null || _seed is null)
        {
            reason = PumpStall.Unloaded;
            return 0;
        }

        VoxelPos origin = outlet.Origin;
        if (mouth.Y <= _seed.SurfaceHeightAt(origin.X, origin.Z))
        {
            reason = PumpStall.Unloaded;
            return 0;
        }

        (int sampleX, int sampleZ) = _water.SampleOf(origin.X, origin.Z);
        if (!_water.TryBasinAtSample(sampleX, sampleZ, out basin))
        {
            reason = PumpStall.Unanalysed;
            return 0;
        }

        toBasin = true;
        return long.MaxValue;
    }

    /// <summary>The chunk columns the box at <paramref name="radius"/> around <paramref name="cell"/> covers — up to four at a chunk corner.</summary>
    private void AddNearColumns(BrushCell cell, int radius)
    {
        ChunkPos lo = cell.Offset(-radius, 0, -radius).OriginVoxel.Chunk;
        ChunkPos hi = cell.Offset(radius, 0, radius).VoxelAt(BrushCell.Voxels - 1, 0, BrushCell.Voxels - 1).Chunk;
        for (int cz = lo.Z; cz <= hi.Z; cz++)
        for (int cx = lo.X; cx <= hi.X; cx++)
        {
            _nearColumns.Add((cx, cz));
        }
    }

    /// <summary>Up to eight <c>IsLoaded</c>: the chunks the box at <paramref name="radius"/> covers, and whether every one is loaded to the water.</summary>
    private bool BoxIsLoaded(BrushCell cell, int radius)
    {
        VoxelPos min = cell.Offset(-radius, -radius, -radius).OriginVoxel;
        VoxelPos max = cell.Offset(radius, radius, radius).VoxelAt(BrushCell.Voxels - 1, BrushCell.Voxels - 1, BrushCell.Voxels - 1);
        ChunkPos lo = min.Chunk, hi = max.Chunk;

        _boxChunks.Clear();
        for (int cz = lo.Z; cz <= hi.Z; cz++)
        for (int cx = lo.X; cx <= hi.X; cx++)
        for (int cy = lo.Y; cy <= hi.Y; cy++)
        {
            var chunk = new ChunkPos(cx, cy, cz);
            if (!_terrain.IsLoaded(chunk.OriginVoxel))
            {
                return false;
            }

            _boxChunks.Add(chunk);
        }

        return true;
    }

    /// <summary>Up to eight <c>HeldIn</c> over the chunks <see cref="BoxIsLoaded"/> listed.</summary>
    private bool BoxHoldsAnything()
    {
        FluidGrid grid = _fluid.Grid;
        foreach (ChunkPos chunk in _boxChunks)
        {
            if (grid.HeldIn(chunk) > 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Millilitres in the box: every cell's voxels, nearest cells first, <c>[z][x][y]</c> within a cell.</summary>
    private long ReadBox(BrushCell cell, int radius)
    {
        FluidGrid grid = _fluid.Grid;
        long held = 0;
        foreach ((int dx, int dy, int dz) in IntakeCells[radius])
        {
            BrushCell neighbour = cell.Offset(dx, dy, dz);
            for (int lz = 0; lz < BrushCell.Voxels; lz++)
            for (int lx = 0; lx < BrushCell.Voxels; lx++)
            for (int ly = 0; ly < BrushCell.Voxels; ly++)
            {
                held += grid.AmountAt(neighbour.VoxelAt(lx, ly, lz));
            }
        }

        IntakeVoxelsRead += (long)IntakeCells[radius].Length * BrushCell.Voxels * BrushCell.Voxels * BrushCell.Voxels;
        return held;
    }

    /// <summary>Takes exactly <paramref name="millilitres"/> out of the box, in the order <see cref="ReadBox"/> read it.</summary>
    private void DrainBox(BrushCell cell, int radius, long millilitres)
    {
        FluidGrid grid = _fluid.Grid;
        long remaining = millilitres;
        foreach ((int dx, int dy, int dz) in IntakeCells[radius])
        {
            BrushCell neighbour = cell.Offset(dx, dy, dz);
            for (int lz = 0; lz < BrushCell.Voxels && remaining > 0; lz++)
            for (int lx = 0; lx < BrushCell.Voxels && remaining > 0; lx++)
            for (int ly = 0; ly < BrushCell.Voxels && remaining > 0; ly++)
            {
                VoxelPos voxel = neighbour.VoxelAt(lx, ly, lz);
                int amount = grid.AmountAt(voxel);
                if (amount == 0)
                {
                    continue;
                }

                long want = Math.Min(amount, remaining);
                long taken = _fluid.Pump(voxel, want);
                if (taken != want)
                {
                    throw new InvalidOperationException(
                        $"Pump at {cell}: {voxel} handed back {taken} mL of the {want} it held when the box was read this boundary.");
                }

                remaining -= taken;
            }

            if (remaining == 0)
            {
                return;
            }
        }

        throw new InvalidOperationException(
            $"Pump at {cell}: asked to drain {millilitres} mL and found {millilitres - remaining}; nothing may take from the box between the read and the drain.");
    }

    /// <summary>Puts exactly <paramref name="millilitres"/> into the layer above the outlet's top, where <see cref="RoomAt"/> found the room.</summary>
    private void EmitLayer(MachineRow outlet, long millilitres)
    {
        FluidGrid grid = _fluid.Grid;
        long remaining = millilitres;
        for (int lz = 0; lz < BrushCell.Voxels && remaining > 0; lz++)
        for (int lx = 0; lx < BrushCell.Voxels && remaining > 0; lx++)
        {
            VoxelPos voxel = outlet.Cell.VoxelAt(lx, BrushCell.Voxels, lz);
            if (!_terrain.CanHold(voxel))
            {
                continue;
            }

            long free = grid.FreeAt(voxel);
            if (free == 0)
            {
                continue;
            }

            long give = Math.Min(free, remaining);
            long refused = _fluid.Emit(voxel, give);
            if (refused != 0)
            {
                // The portal drain's rule: the room was read this boundary and nothing has taken
                // it since, so a refusal is the wiring being wrong, and a refund would hide it.
                throw new InvalidOperationException(
                    $"Outlet at {outlet.Cell}: {voxel} refused {refused} mL it had room for when the layer was read this boundary.");
            }

            remaining -= give;
        }

        if (remaining != 0)
        {
            throw new InvalidOperationException(
                $"Outlet at {outlet.Cell}: {remaining} mL of {millilitres} found no room in a layer read as holding it this boundary.");
        }
    }

    private static (int Dx, int Dy, int Dz)[][] BuildIntakeCells()
    {
        var tables = new (int, int, int)[MachineDefinition.MaxIntakeRadiusCells + 1][];
        tables[0] = [];
        for (int radius = 1; radius <= MachineDefinition.MaxIntakeRadiusCells; radius++)
        {
            var cells = new List<(int Dx, int Dy, int Dz)>();
            for (int dz = -radius; dz <= radius; dz++)
            for (int dx = -radius; dx <= radius; dx++)
            for (int dy = -radius; dy <= radius; dy++)
            {
                if (dx != 0 || dy != 0 || dz != 0)
                {
                    cells.Add((dx, dy, dz));
                }
            }

            // Nearest first at no sort per boundary: faces (one step) before edges (two) before
            // corners (three), and within a distance the [z][x][y] order the grid is laid out in.
            cells.Sort(static (a, b) =>
            {
                int c = (Math.Abs(a.Dx) + Math.Abs(a.Dy) + Math.Abs(a.Dz)).CompareTo(Math.Abs(b.Dx) + Math.Abs(b.Dy) + Math.Abs(b.Dz));
                if (c != 0) return c;
                c = a.Dz.CompareTo(b.Dz);
                if (c != 0) return c;
                c = a.Dx.CompareTo(b.Dx);
                return c != 0 ? c : a.Dy.CompareTo(b.Dy);
            });

            tables[radius] = [.. cells];
        }

        return tables;
    }
}
