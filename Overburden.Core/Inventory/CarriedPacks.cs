using Overburden.Core.Persistence;

namespace Overburden.Core.Inventory;

/// <summary>
/// The charges of the packs carried in the inventory, an annotation on the count — the thing
/// a count cannot hold on its own, kept so that a swapped-out pack keeps what it had.
/// </summary>
/// <remarks>
/// <para>
/// <b>M10c-3's rule was that a pack back in the count is a count, and a count has no charge</b>
/// (the ladder's decision 15), so a swap forfeited the outgoing pack's charge — a term in the
/// pool's identity, printed. The first play threw 2,650 kJ away on a swap and Matthew's verdict
/// was <i>"keep its charge as an item please"</i> (2026-09-18). This is the smallest object that
/// does it: the inventory's count of a pack material stays the truth of how many are carried,
/// and this list annotates <em>some</em> of them with a charge and a channel. A carried pack
/// with no entry here is empty on channel 0, which is what <c>P</c> seeds and what a fresh pack
/// is.
/// </para>
/// <para>
/// <b>A zero charge is not stored.</b> The only way a pack gains charge is being worn, so an
/// entry exists only for a pack that was worn and swapped out holding something; stowing an
/// empty pack adds nothing, and its channel goes with it — a pack on channel 3 swapped out at
/// 0 J comes back on channel 0, which is the cost of the count staying the truth. The
/// invariant the header checks is that a material's entries never outnumber its count.
/// </para>
/// <para>
/// <b>The equip takes the fullest.</b> A right-click with a pack in hand asks for one pack of
/// that material; with several carried the useful one is the one with the most charge, and a
/// rule that is always the same beats a rule that depends on which one was stowed last.
/// </para>
/// </remarks>
public sealed class CarriedPacks
{
    private readonly List<PackSave> _packs = [];

    /// <summary>Every charged pack carried, in the save form's order: by material, fullest first, then channel.</summary>
    /// <remarks>Sorted so the same packs always write the same bytes — <see cref="VolumetricInventory.Entries"/>'s argument.</remarks>
    public IReadOnlyList<PackSave> Entries =>
        _packs
            .OrderBy(p => p.MaterialId)
            .ThenByDescending(p => p.ChargeMillijoules)
            .ThenBy(p => p.Channel)
            .ToArray();

    /// <summary>How many charged packs of <paramref name="materialId"/> are carried.</summary>
    public int Count(int materialId) => _packs.Count(p => p.MaterialId == materialId);

    /// <summary>How many charged packs are carried in all.</summary>
    public int Count() => _packs.Count;

    /// <summary>The fullest carried pack of <paramref name="materialId"/>'s charge, or 0 with none charged.</summary>
    public long FullestMillijoules(int materialId)
    {
        long best = 0;
        foreach (PackSave pack in _packs)
        {
            if (pack.MaterialId == materialId && pack.ChargeMillijoules > best)
            {
                best = pack.ChargeMillijoules;
            }
        }

        return best;
    }

    /// <summary>
    /// Puts a pack's charge back into the inventory beside its count. Returns whether anything
    /// was kept — a pack holding nothing leaves no entry.
    /// </summary>
    public bool Stow(PackSave pack)
    {
        if (pack.ChargeMillijoules < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pack), pack.ChargeMillijoules, "A pack cannot hold a negative charge.");
        }

        if (pack.ChargeMillijoules == 0)
        {
            return false;
        }

        _packs.Add(pack);
        return true;
    }

    /// <summary>
    /// Takes the fullest charged pack of <paramref name="materialId"/> out of the list, or
    /// returns null when none carried of that material holds anything — the caller then equips
    /// an empty one, because the count may still hold plenty.
    /// </summary>
    public PackSave? TakeFullest(int materialId)
    {
        int best = -1;
        for (int i = 0; i < _packs.Count; i++)
        {
            if (_packs[i].MaterialId != materialId)
            {
                continue;
            }

            if (best < 0 || _packs[i].ChargeMillijoules > _packs[best].ChargeMillijoules)
            {
                best = i;
            }
        }

        if (best < 0)
        {
            return null;
        }

        PackSave taken = _packs[best];
        _packs.RemoveAt(best);
        return taken;
    }

    /// <summary>Replaces everything carried with <paramref name="packs"/>. The load path.</summary>
    public void Reset(IEnumerable<PackSave> packs)
    {
        ArgumentNullException.ThrowIfNull(packs);
        _packs.Clear();
        foreach (PackSave pack in packs)
        {
            Stow(pack);
        }
    }
}
