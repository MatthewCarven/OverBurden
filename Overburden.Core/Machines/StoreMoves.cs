using Overburden.Core.Building;
using Overburden.Core.Inventory;

namespace Overburden.Core.Machines;

/// <summary>Why a store move moved nothing (IM-6's pass, decision 4) — one reason, the first met.</summary>
public enum StoreRefusal : byte
{
    /// <summary>Not refused: the move moved what the room, the stock and the store allowed, which may be nothing.</summary>
    None,

    /// <summary>The pocket would refuse the material (fact 11): water and the table's other fluids. Nothing moves either way.</summary>
    PocketRefuses,

    /// <summary>No quantum backpack is worn: the store's moves go through its window (decision 21), as the siphon's do.</summary>
    NoBackpack,

    /// <summary>Nothing at the cell is a vault or a crate.</summary>
    NotABox,

    /// <summary>Its bytes are a version this build cannot read: it is kept as it is, and holds something to every rule (decision 8).</summary>
    Unreadable,

    /// <summary>A wreck: what it holds is owed to the world (decision 17).</summary>
    Wreck,

    /// <summary>Dumping: its fuse burned out and what it holds is owed (decision 34).</summary>
    Dumping,

    /// <summary>Switched off at its <c>enabled</c> port (decision 27).</summary>
    SwitchedOff,
}

/// <summary>What one store move did: the voxels it moved, and why it moved none if it was refused.</summary>
public readonly record struct StoreMove(long Voxels, StoreRefusal Refusal)
{
    public bool Refused => Refusal != StoreRefusal.None;
}

/// <summary>
/// A channel's store as a whole (IM-6's pass, decision 4): the millilitres its vaults use, blocks and water
/// together, and the millilitres they hold inside — the HUD's <c>store 68%</c>.
/// </summary>
public readonly record struct StoreFill(long UsedMillilitres, long InsideMillilitres);

/// <summary>
/// The store's moves (IDEAL-MACHINES-PLAN, "IM-6 — the code-level pass", decision 4): one Core seam that the
/// panel's stock section (decision 21) and M10g's screen both call, so the Game moves nothing itself.
/// </summary>
/// <remarks>
/// <para>
/// <b>Five moves</b>, each over the set's own doors, each returning what moved:
/// <see cref="ToStore"/> and <see cref="ToPocket"/> on the backpack's channel; <see cref="IntoBox"/> and
/// <see cref="OutOfBox"/> on one aimed vault or crate; and <see cref="OutOfCombined"/>, the screen's combined
/// box (M10-LADDER-PLAN, "His design for the screen's boxes", point 2), into an aimed box from the pocket
/// first and then the store, never out of the aimed box itself.
/// </para>
/// <para>
/// <b>A closed box says why</b> — switched off, dumping, a wreck — through <see cref="MachineSet.ClosedReason"/>,
/// the one definition the set's own doors read. <b>Nothing moves a material the pocket would refuse</b> (fact 11):
/// the pocket drops one into a counter only a test reads, so a move out of a vault for it would lose voxels
/// without a word. The set's <c>Withdraw</c> and <c>WithdrawFrom</c> carry the same guard.
/// </para>
/// <para>
/// <b>Priced</b>: a move to the store and back round-trips exactly, both halves counted (<c>deposited</c>,
/// <c>withdrawn</c>) in <see cref="MachineSet.StoreExact"/>; a move between two boxes is held to held and
/// moves no term. A move is not an undo step (decision 21, as IM-6's decision 9 has it).
/// </para>
/// </remarks>
public sealed class StoreMoves
{
    private readonly MachineSet _machines;
    private readonly VolumetricInventory _pocket;

    public StoreMoves(MachineSet machines, VolumetricInventory pocket)
    {
        ArgumentNullException.ThrowIfNull(machines);
        ArgumentNullException.ThrowIfNull(pocket);
        _machines = machines;
        _pocket = pocket;
    }

    /// <summary>Up to <paramref name="voxels"/> of a material from the pocket into the backpack's channel's store, in fill order.</summary>
    public StoreMove ToStore(EquippedPack? pack, int materialId, long voxels)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(voxels);
        if (Refusal(pack, materialId) is { } refused)
        {
            return refused;
        }

        return new StoreMove(_machines.Deposit(pack!.Channel, materialId, voxels, _pocket), StoreRefusal.None);
    }

    /// <summary>Up to <paramref name="voxels"/> of a material from the backpack's channel's store into the pocket, last-filled first.</summary>
    public StoreMove ToPocket(EquippedPack? pack, int materialId, long voxels)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(voxels);
        if (Refusal(pack, materialId) is { } refused)
        {
            return refused;
        }

        return new StoreMove(_machines.Withdraw(pack!.Channel, materialId, voxels, _pocket), StoreRefusal.None);
    }

    /// <summary>Up to <paramref name="voxels"/> of a material from the pocket into the one vault or crate at <paramref name="cell"/>.</summary>
    public StoreMove IntoBox(BrushCell cell, int materialId, long voxels)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(voxels);
        if (BoxRefusal(cell, materialId) is { } refused)
        {
            return refused;
        }

        return new StoreMove(_machines.DepositInto(cell, materialId, voxels, _pocket), StoreRefusal.None);
    }

    /// <summary>Up to <paramref name="voxels"/> of a material from the one vault or crate at <paramref name="cell"/> into the pocket.</summary>
    public StoreMove OutOfBox(BrushCell cell, int materialId, long voxels)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(voxels);
        if (BoxRefusal(cell, materialId) is { } refused)
        {
            return refused;
        }

        return new StoreMove(_machines.WithdrawFrom(cell, materialId, voxels, _pocket), StoreRefusal.None);
    }

    /// <summary>
    /// Up to <paramref name="voxels"/> of a material from the combined box — the pocket and, with the backpack
    /// worn, its channel's store — into the vault or crate at <paramref name="cell"/>: the pocket first, then the
    /// store, last-filled first and never out of that box, which is the container's own box on the screen.
    /// </summary>
    /// <remarks>Without a quantum backpack the combined box is the pocket alone, so this moves from the pocket only and refuses nothing for it.</remarks>
    public StoreMove OutOfCombined(EquippedPack? pack, BrushCell cell, int materialId, long voxels)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(voxels);
        if (BoxRefusal(cell, materialId) is { } refused)
        {
            return refused;
        }

        long fromPocket = _machines.DepositInto(cell, materialId, voxels, _pocket);
        long fromStore = fromPocket < voxels && pack is { IsWindow: true }
            ? _machines.MoveFromStoreInto(pack.Channel, cell, materialId, voxels - fromPocket)
            : 0;
        return new StoreMove(fromPocket + fromStore, StoreRefusal.None);
    }

    private StoreMove? Refusal(EquippedPack? pack, int materialId)
    {
        if (_pocket.Refuses(materialId))
        {
            return new StoreMove(0, StoreRefusal.PocketRefuses);
        }

        return pack is { IsWindow: true } ? null : new StoreMove(0, StoreRefusal.NoBackpack);
    }

    private StoreMove? BoxRefusal(BrushCell cell, int materialId)
    {
        if (_pocket.Refuses(materialId))
        {
            return new StoreMove(0, StoreRefusal.PocketRefuses);
        }

        StoreRefusal closed = _machines.ClosedReason(cell);
        return closed == StoreRefusal.None ? null : new StoreMove(0, closed);
    }
}
