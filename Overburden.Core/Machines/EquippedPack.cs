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

    /// <summary>What it holds now, in millijoules, never over the capacity.</summary>
    public long ChargeMillijoules { get; private set; }

    /// <summary>The channel it recharges from — a port on the header, not on a row.</summary>
    public int Channel { get; set; }

    public long CapacityMillijoules => Definition.CapacityMillijoules;

    /// <summary>What it held when it was constructed — a session's <c>loaded</c> term for the pack.</summary>
    public long InitialMillijoules { get; }

    /// <summary>Millijoules the tool took, over the session.</summary>
    public long TakenMillijoules { get; private set; }

    /// <summary>Millijoules the tick gave, over the session.</summary>
    public long GivenMillijoules { get; private set; }

    /// <summary>Takes up to <paramref name="millijoules"/>; returns what came out.</summary>
    public long Take(long millijoules)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(millijoules);
        long taken = Math.Min(millijoules, ChargeMillijoules);
        ChargeMillijoules -= taken;
        TakenMillijoules += taken;
        return taken;
    }

    /// <summary>Gives up to <paramref name="millijoules"/>, to the capacity; returns what went in.</summary>
    public long Give(long millijoules)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(millijoules);
        long given = Math.Min(millijoules, CapacityMillijoules - ChargeMillijoules);
        ChargeMillijoules += given;
        GivenMillijoules += given;
        return given;
    }

    public override string ToString() =>
        $"pack of material {MaterialId} on channel {Channel}: {ChargeMillijoules:N0} of {CapacityMillijoules:N0} mJ";
}
