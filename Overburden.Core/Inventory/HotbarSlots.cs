using Overburden.Core.Content;

namespace Overburden.Core.Inventory;

/// <summary>
/// The hotbar as nine slots the player arranges, each naming a material or nothing (alpha session
/// C; M10-LADDER-PLAN's inventory decision 2).
/// </summary>
/// <remarks>
/// <para>
/// <b>Its own record, because the inventory forgets a material at zero.</b> Until session C the
/// row was <see cref="VolumetricInventory.Entries"/>' first nine, so mining a new material or
/// spending the last of one shifted every slot after it. A slot here keeps its material at zero
/// (the HUD dims it), and the next bite of that material lands back in it.
/// </para>
/// <para>
/// <b>New things take the first empty slot, through <see cref="Offer"/>, which the inventory's one
/// door calls</b> (<see cref="VolumetricInventory.Add"/>), so every way a material arrives offers
/// it: the carve, a dismantle, P, an undo's refund, a pack handed back. A material already on the
/// row is never moved by an offer, and one that finds no empty slot is still carried — V reaches
/// it, and <see cref="Pin"/> puts it on the row.
/// </para>
/// <para>
/// <b>Not thread-safe</b>, as the inventory is not: everything that touches it runs on the sim
/// thread.
/// </para>
/// </remarks>
public sealed class HotbarSlots
{
    /// <summary>Slots on the row: one per number key, 1 to 9.</summary>
    public const int Count = 9;

    /// <summary>A slot naming nothing.</summary>
    public const int Empty = -1;

    /// <summary>Nine empty slots, the row of a world nobody has carried anything in.</summary>
    public static IReadOnlyList<int> EmptyRow { get; } = Array.AsReadOnly(NewEmptyRow());

    private readonly int[] _slots = NewEmptyRow();

    // The materials an offer found no room for this session, so the HUD says so once for each.
    private readonly HashSet<int> _turnedAway = [];
    private readonly List<int> _turnedAwaySinceDrain = [];

    /// <summary>Bumped on every change to the row, so a reader can tell it moved without comparing nine slots.</summary>
    public long Version { get; private set; }

    /// <summary>The material in <paramref name="slot"/>, zero-based, or <see cref="Empty"/>.</summary>
    public int MaterialInSlot(int slot) => (uint)slot < Count ? _slots[slot] : Empty;

    /// <summary>The slot holding <paramref name="materialId"/>, zero-based, or −1 when it is not on the row.</summary>
    public int SlotOf(int materialId)
    {
        if (materialId < 0)
        {
            return -1;
        }

        for (int slot = 0; slot < Count; slot++)
        {
            if (_slots[slot] == materialId)
            {
                return slot;
            }
        }

        return -1;
    }

    /// <summary>Whether <paramref name="materialId"/> is on the row.</summary>
    public bool Contains(int materialId) => SlotOf(materialId) >= 0;

    /// <summary>The first empty slot, zero-based, or −1 when the row is full.</summary>
    public int FirstEmpty()
    {
        for (int slot = 0; slot < Count; slot++)
        {
            if (_slots[slot] == Empty)
            {
                return slot;
            }
        }

        return -1;
    }

    /// <summary>The row as nine IDs, <see cref="Empty"/> for a slot naming nothing. The form the world file stores.</summary>
    public int[] ToArray() => [.. _slots];

    /// <summary>
    /// A material arriving: on the row already, nothing moves; otherwise it takes the first empty
    /// slot, or none when the row is full.
    /// </summary>
    /// <returns>The slot it is in now, zero-based, or −1 when the row had no room.</returns>
    public int Offer(int materialId)
    {
        if (materialId <= MaterialIds.AirNumericId)
        {
            throw new ArgumentOutOfRangeException(
                nameof(materialId), materialId, "Only a material can take a slot, and air is not one.");
        }

        int on = SlotOf(materialId);
        if (on >= 0)
        {
            return on;
        }

        int empty = FirstEmpty();
        if (empty < 0)
        {
            if (_turnedAway.Add(materialId))
            {
                _turnedAwaySinceDrain.Add(materialId);
            }

            return -1;
        }

        _slots[empty] = materialId;
        Version++;
        return empty;
    }

    /// <summary>
    /// Puts <paramref name="materialId"/> — what is in the player's hand — in <paramref name="slot"/>
    /// (alpha session C, decision C4: hold V and press a number).
    /// </summary>
    /// <remarks>
    /// Already on the row, the two slots swap, so nothing leaves it. Not on it, the slot's old
    /// material takes the first empty slot, or leaves the row — still carried, still reached by V.
    /// Pinning a material to the slot it is in, or with nothing in hand, changes nothing.
    /// </remarks>
    public PinOutcome Pin(int slot, int materialId)
    {
        if ((uint)slot >= Count)
        {
            throw new ArgumentOutOfRangeException(nameof(slot), slot, $"The row has slots 0..{Count - 1}.");
        }

        if (materialId == MaterialIds.AirNumericId)
        {
            throw new ArgumentOutOfRangeException(nameof(materialId), materialId, "Air cannot be pinned.");
        }

        if (materialId < 0)
        {
            return new PinOutcome(PinKind.NothingInHand, slot, Empty, -1);
        }

        int old = _slots[slot];
        if (old == materialId)
        {
            return new PinOutcome(PinKind.AlreadyThere, slot, Empty, -1);
        }

        int from = SlotOf(materialId);
        if (from >= 0)
        {
            _slots[from] = old;
            _slots[slot] = materialId;
            Version++;
            return old == Empty
                ? new PinOutcome(PinKind.Moved, slot, Empty, from)
                : new PinOutcome(PinKind.Swapped, slot, old, from);
        }

        _slots[slot] = materialId;
        Version++;

        if (old == Empty)
        {
            return new PinOutcome(PinKind.Placed, slot, Empty, -1);
        }

        int empty = FirstEmpty();
        if (empty >= 0)
        {
            _slots[empty] = old;
            return new PinOutcome(PinKind.Displaced, slot, old, empty);
        }

        return new PinOutcome(PinKind.LeftRow, slot, old, -1);
    }

    /// <summary>Empties <paramref name="slot"/>. The opener's door, for a slot naming a removed mod's material.</summary>
    public void Clear(int slot)
    {
        if ((uint)slot >= Count)
        {
            throw new ArgumentOutOfRangeException(nameof(slot), slot, $"The row has slots 0..{Count - 1}.");
        }

        if (_slots[slot] != Empty)
        {
            _slots[slot] = Empty;
            Version++;
        }
    }

    /// <summary>Replaces the row with <paramref name="slots"/>, the world file's. Refused whole if <see cref="Problem"/> finds one.</summary>
    public void Restore(IReadOnlyList<int> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        if (Problem(slots, int.MaxValue) is string problem)
        {
            throw new ArgumentException(problem, nameof(slots));
        }

        for (int slot = 0; slot < Count; slot++)
        {
            _slots[slot] = slots[slot];
        }

        Version++;
    }

    /// <summary>
    /// The materials an offer found no room for since the last call, each once a session — what
    /// the HUD says "has no slot" about.
    /// </summary>
    public IReadOnlyList<int> DrainTurnedAway()
    {
        if (_turnedAwaySinceDrain.Count == 0)
        {
            return [];
        }

        int[] drained = [.. _turnedAwaySinceDrain];
        _turnedAwaySinceDrain.Clear();
        return drained;
    }

    /// <summary>
    /// The row a world written before the slots existed showed: its first nine holdings in
    /// ascending ID order, the rest empty — so nothing moves on such a world's first open.
    /// </summary>
    public static int[] RowFor(IEnumerable<int> heldInIdOrder)
    {
        ArgumentNullException.ThrowIfNull(heldInIdOrder);
        int[] row = NewEmptyRow();
        int slot = 0;
        foreach (int materialId in heldInIdOrder)
        {
            if (slot == Count)
            {
                break;
            }

            row[slot++] = materialId;
        }

        return row;
    }

    /// <summary>
    /// What is wrong with <paramref name="slots"/> as a row, or null — shared by the world file's
    /// writer and reader, so they cannot disagree.
    /// </summary>
    /// <remarks>
    /// Exactly nine; each <see cref="Empty"/> or a material inside the table and not air; no
    /// material twice. <b>A slot may name a material that is not held</b> — zero is a slot's
    /// state, not its absence — and any material the table has, water included (IM-6's water on
    /// the row). A removed mod's hole passes, since the header has the table and not the
    /// definitions: the opener empties such a slot and says so. Slots are named 1 to 9 in a
    /// message, as the keys are.
    /// </remarks>
    public static string? Problem(IReadOnlyList<int> slots, int materialCount)
    {
        ArgumentNullException.ThrowIfNull(slots);
        if (slots.Count != Count)
        {
            return $"The hotbar holds {slots.Count} slots; it has exactly {Count}.";
        }

        for (int slot = 0; slot < Count; slot++)
        {
            int materialId = slots[slot];
            if (materialId == Empty)
            {
                continue;
            }

            if (materialId == MaterialIds.AirNumericId)
            {
                return $"Hotbar slot {slot + 1} names air, which is not a material anything can hold.";
            }

            if (materialId < Empty)
            {
                return $"Hotbar slot {slot + 1} is {materialId}, which is neither empty ({Empty}) nor a material.";
            }

            if (materialId >= materialCount)
            {
                return $"Hotbar slot {slot + 1} names material ID {materialId}, which the world's table of {materialCount} does not have.";
            }

            for (int earlier = 0; earlier < slot; earlier++)
            {
                if (slots[earlier] == materialId)
                {
                    return $"Hotbar slots {earlier + 1} and {slot + 1} both name material ID {materialId}; a material is on the row once.";
                }
            }
        }

        return null;
    }

    public override string ToString() =>
        string.Join(", ", _slots.Select((id, slot) => $"{slot + 1} {(id == Empty ? "—" : id.ToString(System.Globalization.CultureInfo.InvariantCulture))}"));

    private static int[] NewEmptyRow() => [Empty, Empty, Empty, Empty, Empty, Empty, Empty, Empty, Empty];
}

/// <summary>What a pin did.</summary>
public enum PinKind
{
    /// <summary>Nothing was in the hand; nothing moved.</summary>
    NothingInHand,

    /// <summary>The material was in that slot already; nothing moved.</summary>
    AlreadyThere,

    /// <summary>Into an empty slot, from off the row.</summary>
    Placed,

    /// <summary>Into an empty slot, from another slot, which is empty now.</summary>
    Moved,

    /// <summary>From another slot, and the slot's old material took that one.</summary>
    Swapped,

    /// <summary>From off the row, and the slot's old material took the first empty slot.</summary>
    Displaced,

    /// <summary>From off the row, the row was full, and the slot's old material left it (still carried).</summary>
    LeftRow,
}

/// <summary>A pin's result: what happened, the slot pinned to, the material it pushed aside (or −1), and where that went (or −1).</summary>
public readonly record struct PinOutcome(PinKind Kind, int Slot, int Displaced, int DisplacedTo);
