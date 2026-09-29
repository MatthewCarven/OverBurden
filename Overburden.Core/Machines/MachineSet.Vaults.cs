using Overburden.Core.Building;
using Overburden.Core.Inventory;
using Overburden.Core.Voxels;

namespace Overburden.Core.Machines;

/// <summary>
/// The vaults' half of the set (IM-3; IDEAL-MACHINES-PLAN decisions 13–20, 22 and 27): the store as a
/// view over a channel's vaults, the pocket's two doors into it, the pick's question and its move,
/// the wreck, and the store's voxel identity.
/// </summary>
/// <remarks>
/// <para>
/// <b>Contents never leave the books with a row</b> (decision 17). Every path that removes a row
/// asks <see cref="HoldsAnything"/> first: the pick is refused unless the sisters can take everything
/// (<see cref="Refuses"/>), and moves it to them at its first voxel (<see cref="Taken"/>); a fall, and
/// both of the reconcile's remove branches, make a wreck instead; the undo refuses in its pre-flight.
/// So <see cref="VaultVoxelsLeaving"/> is zero in play, and a non-zero one is a path this list missed.
/// </para>
/// <para>
/// <b>The bytes are the state</b> (decision 20). Every operation here decodes a row, changes the
/// contents and writes them straight back; nothing holds a vault's contents between calls.
/// </para>
/// </remarks>
public sealed partial class MachineSet
{
    // ---- the store's identity: held + owed == entering − leaving + deposited − withdrawn (decision 19) ----

    /// <summary>Voxels that entered the set in vault rows — added or restored — over the session.</summary>
    public long VaultVoxelsEntering { get; private set; }

    /// <summary>Voxels that left the set in vault rows removed. Zero in play: every remove path yields to a vault holding anything.</summary>
    public long VaultVoxelsLeaving { get; private set; }

    /// <summary>Voxels moved from the pocket into a store or a crate, over the session.</summary>
    public long DepositedVoxels { get; private set; }

    /// <summary>Voxels moved from a store or a crate into the pocket, over the session.</summary>
    public long WithdrawnVoxels { get; private set; }

    // ---- the store's water line (IM-4a; decision 24): held + owed == entering − leaving + deposited − withdrawn ----

    /// <summary>Millilitres of water that entered the set in vault rows — added or restored — over the session.</summary>
    public long VaultWaterEntering { get; private set; }

    /// <summary>Millilitres of water that left the set in vault rows removed. Zero in play, as the voxels' is.</summary>
    public long VaultWaterLeaving { get; private set; }

    /// <summary>
    /// Millilitres put into a store through <see cref="DepositWater"/>, over the session — the siphon's
    /// take today; IM-4b's pump into a vault is a second door with a counter of its own.
    /// </summary>
    public long WaterDepositedMillilitres { get; private set; }

    /// <summary>Millilitres taken out of a store through <see cref="WithdrawWater"/>, over the session — the siphon's pour.</summary>
    public long WaterWithdrawnMillilitres { get; private set; }

    /// <summary>Water in vaults that are stores, over every channel.</summary>
    public long HeldWaterMillilitres => SumVaultWater(owed: false);

    /// <summary>Water owed to the world by wrecks and dumping vaults.</summary>
    public long OwedWaterMillilitres => SumVaultWater(owed: true);

    /// <summary>The store's water line: <c>held + owed == entering − leaving + deposited − withdrawn</c>; the dump adds its terms in IM-5.</summary>
    public bool StoreWaterExact =>
        HeldWaterMillilitres + OwedWaterMillilitres
        == VaultWaterEntering - VaultWaterLeaving + WaterDepositedMillilitres - WaterWithdrawnMillilitres;

    private long SumVaultWater(bool owed)
    {
        long sum = 0;
        foreach (MachineRow row in _rows.Values)
        {
            if (IsVault(row) && VaultState.TryRead(row, out VaultContents contents) && contents.Owes == owed)
            {
                sum += contents.WaterMillilitres;
            }
        }

        return sum;
    }

    private long KnownVaultWater(MachineRow row) =>
        IsVault(row) && VaultState.TryRead(row, out VaultContents contents) ? contents.WaterMillilitres : 0;

    /// <summary>What the channel's store can still take in water: the free room of every vault in it that holds water.</summary>
    public long WaterRoomOf(int channel)
    {
        long room = 0;
        foreach (MachineRow row in StoreOf(channel))
        {
            MachineDefinition definition = VaultDefinitionOf(row)!;
            if (definition.HoldsWater && VaultState.TryRead(row, out VaultContents contents))
            {
                room += FreeMillilitres(definition, contents);
            }
        }

        return room;
    }

    /// <summary>The water in the channel's store now.</summary>
    public long StoredWaterOf(int channel)
    {
        long held = 0;
        foreach (MachineRow row in StoreOf(channel))
        {
            if (VaultState.TryRead(row, out VaultContents contents))
            {
                held += contents.WaterMillilitres;
            }
        }

        return held;
    }

    /// <summary>
    /// Puts up to <paramref name="millilitres"/> of water into the channel's store, in fill order, into
    /// vaults that hold water (decision 14's order, the one deposits of blocks follow). Returns what went in.
    /// </summary>
    public long DepositWater(int channel, long millilitres)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(millilitres);
        long remaining = millilitres;
        foreach (MachineRow row in StoreOf(channel))
        {
            if (remaining == 0) break;
            MachineDefinition definition = VaultDefinitionOf(row)!;
            if (!definition.HoldsWater || !VaultState.TryRead(row, out VaultContents contents)) continue;
            long put = Math.Min(remaining, FreeMillilitres(definition, contents));
            if (put <= 0) continue;
            contents.WaterMillilitres += put;
            VaultState.Write(row, contents);
            remaining -= put;
        }

        long moved = millilitres - remaining;
        WaterDepositedMillilitres += moved;
        return moved;
    }

    /// <summary>Takes up to <paramref name="millilitres"/> of water out of the channel's store, last-filled first. Returns what came out.</summary>
    public long WithdrawWater(int channel, long millilitres)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(millilitres);
        long remaining = millilitres;
        IReadOnlyList<MachineRow> store = StoreOf(channel);
        for (int i = store.Count - 1; i >= 0 && remaining > 0; i--)
        {
            if (!VaultState.TryRead(store[i], out VaultContents contents) || contents.WaterMillilitres == 0) continue;
            long take = Math.Min(remaining, contents.WaterMillilitres);
            contents.WaterMillilitres -= take;
            VaultState.Write(store[i], contents);
            remaining -= take;
        }

        long moved = millilitres - remaining;
        WaterWithdrawnMillilitres += moved;
        return moved;
    }

    // ---- what the rules did, for the M10: line ----

    /// <summary>Vaults turned into wrecks — by a fall at the drain, or at a chunk's arrival.</summary>
    public long VaultsWrecked { get; private set; }

    /// <summary>Picks that moved a vault's contents to its sisters, and the voxels they moved.</summary>
    public long VaultMovesAtPick { get; private set; }

    public long VoxelsMovedAtPick { get; private set; }

    /// <summary>Body voxels a pick found too hard because their vault held something it could not move.</summary>
    public long PickVoxelsRefused { get; private set; }

    /// <summary>Deposits refused for want of room, and withdrawals short of what was asked, in voxels.</summary>
    public long DepositShort { get; private set; }

    private IMachineBodies? _atRange;

    /// <summary>
    /// The bodies seam for a tool that works at range (decision 17): it refuses every body voxel of a
    /// vault holding anything, whatever room its sisters have, so a stray bite 500 m away never moves
    /// a store. The pick's own seam is the set itself.
    /// </summary>
    public IMachineBodies AtRange => _atRange ??= new AtRangeBodies(this);

    /// <summary>The row's vault block, or null when it is not a vault or crate.</summary>
    public MachineDefinition? VaultDefinitionOf(MachineRow row) =>
        _materials?.Machine(row.MaterialId) is { Kind: MachineKind.Vault } definition ? definition : null;

    public bool IsVault(MachineRow row) => VaultDefinitionOf(row) is not null;

    /// <summary>
    /// Whether a vault or crate holds anything: contents, a wreck's owed contents, or bytes this build
    /// cannot read — which read as holding something to every rule (decision 8).
    /// </summary>
    public bool HoldsAnything(MachineRow row) =>
        IsVault(row) && (!VaultState.TryRead(row, out VaultContents contents) || !contents.IsEmpty || contents.Wreck);

    /// <summary>Voxels in vaults that are stores, over every channel and crate.</summary>
    public long HeldVoxels => SumVaultVoxels(owed: false);

    /// <summary>Voxels owed to the world by wrecks and dumping vaults.</summary>
    public long OwedVoxels => SumVaultVoxels(owed: true);

    /// <summary>The store's voxel identity (decision 19), global across channels; water has its own line (IM-4).</summary>
    public bool StoreExact =>
        HeldVoxels + OwedVoxels == VaultVoxelsEntering - VaultVoxelsLeaving + DepositedVoxels - WithdrawnVoxels;

    /// <summary>Millijoules in every vault's own charge (IM-3b) — inside the pool's <c>stored</c>, printed apart from the cells'.</summary>
    public long VaultOwnChargeMillijoules
    {
        get
        {
            long sum = 0;
            foreach (MachineRow row in _rows.Values)
            {
                if (IsVault(row) && VaultState.TryRead(row, out VaultContents contents))
                {
                    sum += contents.OwnChargeMillijoules;
                }
            }

            return sum;
        }
    }

    /// <summary>Vault rows whose bytes this build cannot read — kept, inert, outside the identity's sums.</summary>
    public int UnreadableVaults => _rows.Values.Count(row => IsVault(row) && !VaultState.TryRead(row, out _));

    private long SumVaultVoxels(bool owed)
    {
        long sum = 0;
        foreach (MachineRow row in _rows.Values)
        {
            if (IsVault(row) && VaultState.TryRead(row, out VaultContents contents) && contents.Owes == owed)
            {
                sum += contents.TotalVoxels;
            }
        }

        return sum;
    }

    private long KnownVaultVoxels(MachineRow row) =>
        IsVault(row) && VaultState.TryRead(row, out VaultContents contents) ? contents.TotalVoxels : 0;

    private static int ChannelOf(MachineRow row) =>
        row.TryGetPort(MachineTick.ChannelPort, out long channel) ? (int)Math.Clamp(channel, 0, MachineTick.Channels - 1) : 0;

    /// <summary>What a vault can still take, in millilitres; an overfull one takes nothing (decision 17).</summary>
    public static long FreeMillilitres(MachineDefinition definition, VaultContents contents) =>
        Math.Max(0, definition.InsideMillilitres - contents.UsedMillilitres);

    // ---------------------------------------------------------------------------------------
    // The store (decision 14): a view over a channel's vaults, never saved.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The channel's store: its vaults in fill order — the lowest block hold a cubic metre first (the
    /// highest grade), then position — leaving out a vault switched off (27), a dumping one (34), a
    /// wreck and one whose bytes this build cannot read. Deposits fill in this order; withdrawals
    /// take from the end.
    /// </summary>
    public IReadOnlyList<MachineRow> StoreOf(int channel) => StoreRows(channel, except: null, skipDamaged: false);

    private List<MachineRow> StoreRows(int channel, MachineRow? except, bool skipDamaged)
    {
        var rows = new List<(MachineRow Row, long Rate)>();
        foreach (MachineRow row in _rows.Values)
        {
            if (VaultDefinitionOf(row) is not { IsChannelVault: true } definition
                || ChannelOf(row) != channel
                || ReferenceEquals(row, except)
                || (skipDamaged && _damagedSet.Contains(row))
                || !MachineTick.IsEnabled(row)
                || !VaultState.TryRead(row, out VaultContents contents)
                || contents.Owes)
            {
                continue;
            }

            rows.Add((row, definition.BlockHoldMilliwattsPerCubicMetre));
        }

        rows.Sort(static (a, b) =>
        {
            int c = a.Rate.CompareTo(b.Rate);
            return c != 0 ? c : MachineTick.CompareOriginOf(a.Row, b.Row);
        });

        return rows.ConvertAll(r => r.Row);
    }

    /// <summary>Moves up to <paramref name="voxels"/> of a material from the pocket into the channel's store, in fill order. Returns what moved.</summary>
    public long Deposit(int channel, int materialId, long voxels, VolumetricInventory from)
    {
        ArgumentNullException.ThrowIfNull(from);
        return DepositInto(StoreOf(channel), materialId, voxels, from);
    }

    /// <summary>Moves up to <paramref name="voxels"/> of a material from the channel's store into the pocket, last-filled first. Returns what moved.</summary>
    public long Withdraw(int channel, int materialId, long voxels, VolumetricInventory to)
    {
        ArgumentNullException.ThrowIfNull(to);
        IReadOnlyList<MachineRow> store = StoreOf(channel);
        return WithdrawFrom([.. store.Reverse()], materialId, voxels, to);
    }

    /// <summary>
    /// Moves up to <paramref name="voxels"/> from the pocket into the one vault or crate at
    /// <paramref name="cell"/> — the crate's door, which has no store (decision 22). Refused for a
    /// vault that is switched off, dumping, a wreck or unreadable.
    /// </summary>
    public long DepositInto(BrushCell cell, int materialId, long voxels, VolumetricInventory from)
    {
        ArgumentNullException.ThrowIfNull(from);
        return _rows.TryGetValue(cell, out MachineRow? row) && IsOpenBox(row) ? DepositInto([row], materialId, voxels, from) : 0;
    }

    /// <summary>Moves up to <paramref name="voxels"/> from the one vault or crate at <paramref name="cell"/> into the pocket.</summary>
    public long WithdrawFrom(BrushCell cell, int materialId, long voxels, VolumetricInventory to)
    {
        ArgumentNullException.ThrowIfNull(to);
        return _rows.TryGetValue(cell, out MachineRow? row) && IsOpenBox(row) ? WithdrawFrom([row], materialId, voxels, to) : 0;
    }

    private bool IsOpenBox(MachineRow row) =>
        IsVault(row) && MachineTick.IsEnabled(row) && VaultState.TryRead(row, out VaultContents contents) && !contents.Owes;

    private long DepositInto(IReadOnlyList<MachineRow> rows, int materialId, long voxels, VolumetricInventory from)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(voxels);
        long wanted = Math.Min(voxels, from.Voxels(materialId));
        long remaining = wanted;
        foreach (MachineRow row in rows)
        {
            if (remaining == 0) break;
            MachineDefinition definition = VaultDefinitionOf(row)!;
            VaultState.TryRead(row, out VaultContents contents);
            long put = Math.Min(remaining, FreeMillilitres(definition, contents) / MachineDefinition.VoxelMillilitres);
            if (put <= 0) continue;
            contents.Add(materialId, put);
            VaultState.Write(row, contents);
            remaining -= put;
        }

        long moved = wanted - remaining;
        if (moved > 0)
        {
            from.Take(materialId, moved);
            DepositedVoxels += moved;
        }

        DepositShort += voxels - moved;
        return moved;
    }

    private long WithdrawFrom(IReadOnlyList<MachineRow> rows, int materialId, long voxels, VolumetricInventory to)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(voxels);
        long remaining = voxels;
        foreach (MachineRow row in rows)
        {
            if (remaining == 0) break;
            VaultState.TryRead(row, out VaultContents contents);
            long taken = contents.Take(materialId, remaining);
            if (taken <= 0) continue;
            VaultState.Write(row, contents);
            remaining -= taken;
        }

        long moved = voxels - remaining;
        if (moved > 0)
        {
            to.Add(materialId, moved);
            WithdrawnVoxels += moved;
        }

        DepositShort += remaining;
        return moved;
    }

    // ---------------------------------------------------------------------------------------
    // The pick (IM-3's pass, decisions 3 and 4).
    // ---------------------------------------------------------------------------------------

    /// <inheritdoc />
    /// <remarks>
    /// A vault or crate holding anything is too hard unless its sisters on its channel — stores that
    /// are not this one and not already damaged — can take everything it holds. Bytes this build
    /// cannot read, a wreck and a dumping vault are always too hard: what they hold is not the
    /// sisters' to take. Every other machine voxel is never refused here.
    /// </remarks>
    public bool Refuses(VoxelPos voxel, int materialId)
    {
        if (!TryVaultAt(voxel, materialId, out MachineRow row))
        {
            return false;
        }

        if (!VaultState.TryRead(row, out VaultContents contents))
        {
            PickVoxelsRefused++;
            return true;
        }

        if (contents.IsEmpty && !contents.Wreck)
        {
            return false;
        }

        if (PlanMove(row, contents) is null)
        {
            PickVoxelsRefused++;
            return true;
        }

        return false;
    }

    /// <summary>At range's question (decision 17): any vault holding anything is too hard.</summary>
    internal bool HoldsAnythingAt(VoxelPos voxel, int materialId)
    {
        if (!TryVaultAt(voxel, materialId, out MachineRow row) || !HoldsAnything(row))
        {
            return false;
        }

        PickVoxelsRefused++;
        return true;
    }

    private bool TryVaultAt(VoxelPos voxel, int materialId, out MachineRow row)
    {
        row = null!;
        return _byChunk.Count > 0
               && _rows.TryGetValue(BrushCell.FromVoxel(voxel), out MachineRow? found)
               && found.MaterialId == materialId
               && IsVault(found)
               && (row = found) is not null;
    }

    /// <summary>
    /// Where a vault's contents would go if it were taken apart now: into its sisters in fill order,
    /// water first to the ones that hold water, then each material. Null when anything would be left
    /// over, or when there are no sisters — a crate's case, or a vault that is dumping or a wreck.
    /// </summary>
    private Dictionary<MachineRow, VaultContents>? PlanMove(MachineRow row, VaultContents contents)
    {
        if (VaultDefinitionOf(row) is not { IsChannelVault: true } || contents.Owes)
        {
            return null;
        }

        List<MachineRow> sisters = StoreRows(ChannelOf(row), except: row, skipDamaged: true);
        var after = new Dictionary<MachineRow, VaultContents>(sisters.Count);
        var free = new Dictionary<MachineRow, long>(sisters.Count);
        foreach (MachineRow sister in sisters)
        {
            VaultState.TryRead(sister, out VaultContents held);
            after[sister] = held;
            free[sister] = FreeMillilitres(VaultDefinitionOf(sister)!, held);
        }

        long water = contents.WaterMillilitres;
        foreach (MachineRow sister in sisters)
        {
            if (water == 0) break;
            if (!VaultDefinitionOf(sister)!.HoldsWater) continue;
            long put = Math.Min(water, free[sister]);
            after[sister].WaterMillilitres += put;
            free[sister] -= put;
            water -= put;
        }

        if (water > 0)
        {
            return null;
        }

        foreach ((int material, long voxels) in contents.Voxels)
        {
            long remaining = voxels;
            foreach (MachineRow sister in sisters)
            {
                if (remaining == 0) break;
                long put = Math.Min(remaining, free[sister] / MachineDefinition.VoxelMillilitres);
                if (put <= 0) continue;
                after[sister].Add(material, put);
                free[sister] -= put * MachineDefinition.VoxelMillilitres;
                remaining -= put;
            }

            if (remaining > 0)
            {
                return null;
            }
        }

        return after;
    }

    /// <summary>
    /// The move at the pick's first voxel (IM-3's pass, decision 4): the contents go to the sisters at
    /// the instant the pick was allowed, so two vaults in one swing never count on each other's room,
    /// and the drain dismantles an empty vault whose journalled copy is empty too.
    /// </summary>
    private void MoveAtPick(MachineRow row)
    {
        if (!VaultState.TryRead(row, out VaultContents contents) || contents.IsEmpty || contents.Owes)
        {
            return;
        }

        if (PlanMove(row, contents) is not { } after)
        {
            // Refuses said yes a moment ago and nothing ran in between; if this ever fires, the
            // drain makes a wreck of the row, and the contents still never leave the books.
            return;
        }

        foreach ((MachineRow sister, VaultContents held) in after)
        {
            VaultState.Write(sister, held);
        }

        VaultMovesAtPick++;
        VoxelsMovedAtPick += contents.TotalVoxels;

        // IM-3b's pass, fact 4: the own charge stays on the row, and leaves with it at the drain as a
        // cell's charge does. A fresh VaultContents here would take it out of the books with no term.
        VaultState.Write(row, new VaultContents { OwnChargeMillijoules = contents.OwnChargeMillijoules });
    }

    /// <summary>Makes a wreck of a vault that holds what it holds: flagged, dumping, its contents owed (decision 17).</summary>
    private void MakeWreck(MachineRow row, VaultContents contents)
    {
        contents.Wreck = true;
        contents.Phase = VaultPhase.Dumping;
        contents.FuseMillisLeft = 0;
        VaultState.Write(row, contents);
        VaultsWrecked++;
        Version++;
    }

    /// <summary>
    /// What the drain and the reconcile do with a vault that holds anything instead of removing it:
    /// a known one becomes a wreck; one whose bytes this build cannot read is kept as it is.
    /// </summary>
    private void KeepAsWreck(MachineRow row)
    {
        if (VaultState.TryRead(row, out VaultContents contents))
        {
            if (!contents.Wreck)
            {
                MakeWreck(row, contents);
            }
        }
        else
        {
            VaultsWrecked++;
        }

        row.Lost = 0;
        row.Carved = 0;
    }

    /// <summary>Whether a row is a known wreck — the drain and the reconcile skip it (decision 5 of IM-3's pass).</summary>
    private bool IsKnownWreck(MachineRow row) =>
        IsVault(row) && VaultState.TryRead(row, out VaultContents contents) && contents.Wreck;
}

/// <summary>The bodies seam an at-range swing is handed (decision 17): any vault holding anything is too hard.</summary>
internal sealed class AtRangeBodies(MachineSet set) : IMachineBodies
{
    public bool Refuses(VoxelPos voxel, int materialId) => set.HoldsAnythingAt(voxel, materialId);

    public void Taken(VoxelPos voxel, int materialId) => set.Taken(voxel, materialId);
}
