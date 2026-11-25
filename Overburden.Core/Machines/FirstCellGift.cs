using Overburden.Core.Building;
using Overburden.Core.Persistence;

namespace Overburden.Core.Machines;

/// <summary>
/// Creative's first-cell gift (IDEAL-MACHINES-PLAN decision 53; IM-6's pass, decision 23): the first placed storage row
/// in a creative world arrives holding 1 GJ, once.
/// </summary>
/// <remarks>
/// <para>
/// <b>A door of its own</b>, counted <see cref="MachineSet.StoredMillijoulesGranted"/>, a term in
/// <see cref="MachineTick.PowerExact"/>, and never through <see cref="MachineSet.SetCharge"/>, where it would hide in
/// <c>entering</c> beside restores and <c>Alt+P</c>. The Game arms it only in a creative world whose header's bit 1
/// (<see cref="Persistence.WorldFlags.FirstCellGranted"/>) is clear, and writes the bit with the next session write,
/// which carries the row's charge in the same transaction.
/// </para>
/// <para>
/// <b>Priced</b> (the pass's table): it happens once; picking the cell takes the charge out through <c>leaving</c> and
/// Ctrl+Z of the pick brings it back through <c>entering</c>, as for any cell; Ctrl+Z of the placement removes the row
/// and its 1 GJ through <c>leaving</c>, and the gift stays given, so it is gone. Clearing the gift when the granted row
/// leaves whole would be a door the undo can see twice (IM-0b's lesson), so it is not built.
/// </para>
/// </remarks>
public sealed class FirstCellGift
{
    /// <summary>One gigajoule, in the pool's millijoules.</summary>
    public const long GiftMillijoules = 1_000_000_000L * MachineDefinition.PerMille;

    /// <param name="alreadyGiven">The header's bit 1: true leaves the gift spent from the start.</param>
    public FirstCellGift(bool alreadyGiven = false)
    {
        Given = alreadyGiven;
    }

    /// <summary>
    /// The gift a world of <paramref name="mode"/> carries: one in a creative world, already spent when its header's bit 1
    /// says so, and none at all in a sandbox world, so a sandbox placement never meets the door.
    /// </summary>
    public static FirstCellGift? For(WorldMode mode, WorldFlags flags) =>
        mode == WorldMode.Creative ? new FirstCellGift((flags & WorldFlags.FirstCellGranted) != 0) : null;

    /// <summary>
    /// <paramref name="flags"/> with bit 1 set once the gift has gone — what the next session write carries, in the same
    /// transaction as the row holding the charge.
    /// </summary>
    public WorldFlags Stamp(WorldFlags flags) => Given ? flags | WorldFlags.FirstCellGranted : flags;

    /// <summary>Whether the gift has gone: the header's bit 1, or a row given it this session.</summary>
    public bool Given { get; private set; }

    /// <summary>The cell the gift went to this session, or null.</summary>
    public BrushCell? GivenTo { get; private set; }

    /// <summary>What that row was given this session (1 GJ unless its capacity is less), zero before.</summary>
    public long GivenMillijoules { get; private set; }

    /// <summary>
    /// A row was placed: if the gift is still to give and the row is a placed storage row, it is given. Anything else —
    /// a pump, a panel, a vault, the gift already given — passes untouched.
    /// </summary>
    internal void Offer(MachineSet machines, MachineRow row)
    {
        if (Given)
        {
            return;
        }

        long landed = machines.Grant(row.Cell, GiftMillijoules);
        if (landed < 0)
        {
            return;
        }

        Given = true;
        GivenTo = row.Cell;
        GivenMillijoules = landed;
    }
}
