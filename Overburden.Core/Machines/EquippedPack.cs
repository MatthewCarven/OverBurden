namespace Overburden.Core.Machines;

/// <summary>
/// The pack the player carries: a storage with no position, charged from its channel's pool at
/// the boundary and drained by the tool that draws on it (M10c, decision 4).
/// </summary>
/// <remarks>
/// <para>
/// <b>Not a row.</b> A row has a cell and an origin order, and the pool's write-back fills rows in
/// that order; a pack has neither. It rides the world header (c-3) and lives here as the object
/// the tick recharges and the controllers debit. <b>It is a consumer and never a supply</b>: its
/// charge is not in any channel's pool, or the relay would drink the miner's backpack in one
/// boundary. It draws <em>last</em>, after every placed consumer, bounded by its material's
/// <see cref="MachineDefinition.RechargeWatts"/>.
/// </para>
/// <para>
/// The counters are the identity's: what it held when it was handed over, what the tool took,
/// what the tick gave. <c>Σ rows + pack == entering − leaving + generated − drawn by pumps −
/// clamped + initial − taken</c> is the pool's identity with the pack inside it, and every term
/// is a long in millijoules.
/// </para>
/// <para>
/// <b>A window holds nothing</b> (IDEAL-MACHINES-PLAN decisions 7 and 8): a pack whose <c>charge</c>
/// extra takes the <c>window</c> form has no capacity and no charge of its own, and a tool paying
/// through it draws straight out of its channel's cells — the <see cref="Bank"/> the tick attaches
/// when it is equipped. The cells fall by what the pack's <see cref="TakenMillijoules"/> rises by,
/// so the pool's identity needs no new term. The recharge, the debug fill and <see cref="Give"/>
/// give a window nothing.
/// </para>
/// </remarks>
public sealed class EquippedPack
{
    public EquippedPack(int materialId, MachineDefinition definition, long chargeMillijoules, int channel)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentOutOfRangeException.ThrowIfNegative(materialId);
        if (definition.Kind != MachineKind.Storage || !definition.Carried)
        {
            throw new ArgumentException("A pack is a carried storage.", nameof(definition));
        }

        MaterialId = materialId;
        Definition = definition;
        ChargeMillijoules = Math.Clamp(chargeMillijoules, 0, definition.CapacityMillijoules);
        Channel = Math.Clamp(channel, 0, MachineTick.Channels - 1);
        InitialMillijoules = ChargeMillijoules;
    }

    /// <summary>The pack's material, through the world's table.</summary>
    public int MaterialId { get; }

    /// <summary>The material's machine block — the capacity and the recharge bound.</summary>
    public MachineDefinition Definition { get; }

    /// <summary>Whether the pack is a window onto its channel's cells, holding nothing itself (decision 7).</summary>
    public bool IsWindow => Definition.IsWindow;

    /// <summary>
    /// What a window draws on: the tick it is equipped on, attached and detached by
    /// <see cref="MachineTick.EquipPack"/>. Null for a pack that holds its own charge, and for a
    /// window nobody has equipped, which buys nothing.
    /// </summary>
    internal IChannelBank? Bank { get; set; }

    /// <summary>What it holds now, in millijoules, never over the capacity.</summary>
    public long ChargeMillijoules { get; private set; }

    /// <summary>The channel it recharges from — a port on the header, not on a row.</summary>
    public int Channel { get; set; }

    /// <summary>
    /// What a tool paying through the pack can spend now: its own charge, or for a window the bank
    /// its channel's enabled cells hold (decision 8) — the one number the gate, the log and the HUD read.
    /// </summary>
    public long SpendableMillijoules => IsWindow ? Bank?.BankMillijoules(Channel) ?? 0 : ChargeMillijoules;

    public long CapacityMillijoules => Definition.CapacityMillijoules;

    /// <summary>What it held when it was constructed — a session's <c>loaded</c> term for the pack.</summary>
    public long InitialMillijoules { get; }

    /// <summary>Millijoules the tool took, over the session.</summary>
    public long TakenMillijoules { get; private set; }

    /// <summary>Millijoules the tick gave, over the session.</summary>
    public long GivenMillijoules { get; private set; }

    /// <summary>Takes up to <paramref name="millijoules"/>; returns what came out.</summary>
    /// <remarks>A window takes it out of its channel's cells (decision 8), and counts it here as any pack does.</remarks>
    public long Take(long millijoules)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(millijoules);
        if (IsWindow)
        {
            long drawn = Bank?.Draw(Channel, millijoules) ?? 0;
            TakenMillijoules += drawn;
            return drawn;
        }

        long taken = Math.Min(millijoules, ChargeMillijoules);
        ChargeMillijoules -= taken;
        TakenMillijoules += taken;
        return taken;
    }

    /// <summary>Gives up to <paramref name="millijoules"/>, to the capacity; returns what went in. A window takes nothing.</summary>
    public long Give(long millijoules)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(millijoules);
        if (IsWindow)
        {
            return 0;
        }

        long given = Math.Min(millijoules, CapacityMillijoules - ChargeMillijoules);
        ChargeMillijoules += given;
        GivenMillijoules += given;
        return given;
    }

    /// <summary>
    /// How many voxels the charge buys at <paramref name="millijoulesPerVoxel"/> — the budget a
    /// carve is handed (M10c-3, decision 6). Unbounded at a cost of nothing.
    /// </summary>
    /// <remarks>
    /// One definition of the division, here rather than in each controller, so the bite and the
    /// fill agree on what a joule buys: <c>charge ÷ cost</c>, floored, which is <em>takes what
    /// the charge buys and stops</em> — the last few kilojoules buy the last few voxels rather
    /// than nothing.
    /// </remarks>
    public int VoxelsAffordable(long millijoulesPerVoxel)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(millijoulesPerVoxel);
        if (millijoulesPerVoxel == 0)
        {
            return int.MaxValue;
        }

        return (int)Math.Min(int.MaxValue, SpendableMillijoules / millijoulesPerVoxel);
    }

    /// <summary>Pays for <paramref name="voxels"/> at <paramref name="millijoulesPerVoxel"/>; returns what was taken.</summary>
    /// <remarks>
    /// A caller that budgeted through <see cref="VoxelsAffordable"/> and pays for what
    /// <c>Removed</c> says came away can never be short here, and the return says so — a
    /// return under the bill is a caller that skipped the budget.
    /// </remarks>
    public long Pay(int voxels, long millijoulesPerVoxel)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(voxels);
        ArgumentOutOfRangeException.ThrowIfNegative(millijoulesPerVoxel);
        return Take(voxels * millijoulesPerVoxel);
    }

    public override string ToString() =>
        $"pack of material {MaterialId} on channel {Channel}: {ChargeMillijoules:N0} of {CapacityMillijoules:N0} mJ";
}
