using Overburden.Core.Content;
using Overburden.Core.Voxels;

namespace Overburden.Core.Inventory;

/// <summary>
/// How much of each material the player is carrying, as a volume.
/// </summary>
/// <remarks>
/// <para>
/// <b>Litres, not block counts, and DESIGN says why:</b> a 0.25 m voxel is 0.015625 m³, so a
/// 1 m brush cube is 64 voxels — <b>exactly 1,000 litres, exactly one cubic metre</b>. That round
/// number is what makes the unit worth having: a player can look at a hole and at a stack and
/// compare them without arithmetic. It is also the argument DESIGN makes for volumetric inventory
/// existing at all, which is that big excavations should be meaningfully productive rather than
/// producing an inventory-management problem.
/// </para>
/// <para>
/// <b>Stored as voxel counts and presented in litres, which is not a contradiction.</b> A voxel
/// is the only granularity in which anything can currently enter or leave the world — mining
/// removes whole voxels, a brush places whole voxels — so counting them is exact, and every
/// litre figure is a presentation of an integer. Storing litres as a float instead would
/// accumulate rounding across a long session, and "a short inventory places what it can" would
/// stop being a clean integer answer.
/// </para>
/// <para>
/// <b>Revisit trigger:</b> the first thing that adds or removes a quantity which is not a whole
/// number of voxels. M8's fluids are the likely one — a volume-conserving cellular automaton
/// moves fractions of a cell — and a crafting yield of "0.3 m³" would do it too. At that point
/// this wants a fixed-point litre count rather than a voxel count, and the conversion is the
/// whole of the change.
/// </para>
/// <para>
/// <b>Unbounded at M6, deliberately, and the cost is stated rather than hidden.</b> Bounding it
/// now — with no containers, no conveyors and no crafting — means the player fills up and stops,
/// which is a constraint with nothing on the other side of it. The honest price is that
/// <b>M6 cannot answer whether volume is a real constraint</b>, so DESIGN's claim that volumetric
/// inventory makes big excavations productive stays argued rather than played. <b>Revisit
/// trigger: M10's containers or M11's crafting</b>, whichever first gives capacity somewhere to
/// go.
/// </para>
/// <para>
/// Not thread-safe, and it does not need to be: mining, placement and the save all run on the
/// sim thread.
/// </para>
/// </remarks>
public sealed class VolumetricInventory
{
    /// <summary>
    /// Litres in one voxel: 0.25³ m³ × 1,000. Exact in binary, and exactly 1,000 per brush cube.
    /// </summary>
    public const double LitresPerVoxel =
        VoxelConstants.VoxelSize * VoxelConstants.VoxelSize * VoxelConstants.VoxelSize * 1000.0;

    private readonly Dictionary<int, long> _voxels = [];

    // IM-4's pass, decision 8: the materials this pocket never holds — the table's fluids, water first.
    private HashSet<int>? _refused;

    /// <summary>
    /// Makes this pocket refuse <paramref name="materialIds"/> at its one door, <see cref="Add"/> — the
    /// table's fluids (IDEAL-MACHINES-PLAN decision 23: water lives in the store, never the pocket).
    /// </summary>
    /// <remarks>
    /// <b>One site for nine doors.</b> Every way a material enters the pocket — the carve's credit,
    /// <c>--stock=</c> (which seeds every material the table has), <c>P</c>, a withdrawal, an undo's refund,
    /// a dismantle, a pack handed back, a restore and the debug seeds — reaches <see cref="Add"/>, so a
    /// refusal here is decision 23's "by every path" made one test. Counted, not thrown: a seed that
    /// offers water is doing its job, and the count says it was turned away.
    /// </remarks>
    public void RefuseMaterials(IEnumerable<int> materialIds)
    {
        ArgumentNullException.ThrowIfNull(materialIds);
        _refused = [.. materialIds];
    }

    /// <summary>Whether this pocket refuses <paramref name="materialId"/>.</summary>
    public bool Refuses(int materialId) => _refused?.Contains(materialId) == true;

    // Alpha session C, decision C3: the materials that never take a slot by themselves — the items.
    private HashSet<int>? _offRow;

    /// <summary>The hotbar: nine slots the player arranges, kept here so that <see cref="Add"/> can offer each arrival a slot.</summary>
    public HotbarSlots Hotbar { get; } = new();

    /// <summary>
    /// Keeps <paramref name="materialIds"/> off the row unless pinned — the table's items, the
    /// computer and the packs (alpha session C, decision C3).
    /// </summary>
    /// <remarks>
    /// <b>Only placeable materials take a slot by themselves</b>, so P's kit fills an empty row with
    /// blocks and machines and leaves room for the first stone; an item is a V away, and a pin puts
    /// it on the row for good. The same shape as <see cref="RefuseMaterials"/>: set once at open
    /// from the table, consulted at the one door.
    /// </remarks>
    public void KeepOffTheRow(IEnumerable<int> materialIds)
    {
        ArgumentNullException.ThrowIfNull(materialIds);
        _offRow = [.. materialIds];
    }

    /// <summary>Voxels <see cref="Add"/> turned away because the pocket refuses their material, over its lifetime.</summary>
    public long RefusedVoxels { get; private set; }

    /// <summary>How many voxels' worth of <paramref name="materialId"/> is held.</summary>
    public long Voxels(int materialId) =>
        _voxels.TryGetValue(materialId, out long held) ? held : 0L;

    /// <summary>The same quantity in litres.</summary>
    public double Litres(int materialId) => Voxels(materialId) * LitresPerVoxel;

    /// <summary>Everything held, of every material, in voxels.</summary>
    public long TotalVoxels
    {
        get
        {
            long total = 0;
            foreach (long held in _voxels.Values) total += held;
            return total;
        }
    }

    /// <summary>Everything held, in litres.</summary>
    public double TotalLitres => TotalVoxels * LitresPerVoxel;

    /// <summary>Whether there is at least one voxel of <paramref name="materialId"/> to spend.</summary>
    public bool Has(int materialId) => Voxels(materialId) > 0L;

    /// <summary>How many distinct materials are held. Materials at zero are not held.</summary>
    public int MaterialCount => _voxels.Count;

    /// <summary>Credits <paramref name="voxels"/> voxels of <paramref name="materialId"/>.</summary>
    /// <remarks>
    /// <b>Air is refused rather than ignored.</b> Nothing can mine air — <c>Carve.Take</c> buckets
    /// it as <c>AlreadyAir</c> before the removal — so an air deposit is a caller that has lost
    /// track of what it removed, and silently accepting it would put a number in the save file
    /// that no amount of play could explain.
    /// <para>
    /// <b>And every credit offers the material a slot</b> (alpha session C, decision C2): one not on
    /// the row takes the first empty slot, or none when the row is full. One already on it — an
    /// undo's refund, a pack handed back after a swap — stays where it is, because a slot keeps
    /// its material at zero. <see cref="Reset"/>, the load path, offers nothing.
    /// </para>
    /// </remarks>
    public void Add(int materialId, long voxels) => Credit(materialId, voxels, offer: true);

    private void Credit(int materialId, long voxels, bool offer)
    {
        if (materialId == MaterialIds.AirNumericId)
        {
            throw new ArgumentOutOfRangeException(
                nameof(materialId),
                materialId,
                "Air is not a volume. Nothing can mine it, so nothing can deposit it.");
        }

        if (voxels < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(voxels), voxels, "Use Take to remove; Add never removes.");
        }

        if (voxels == 0) return;

        if (Refuses(materialId))
        {
            RefusedVoxels += voxels;
            return;
        }

        _voxels[materialId] = Voxels(materialId) + voxels;

        if (offer && _offRow?.Contains(materialId) != true)
        {
            Hotbar.Offer(materialId);
        }
    }

    /// <summary>
    /// Spends up to <paramref name="voxels"/> voxels of <paramref name="materialId"/>.
    /// </summary>
    /// <remarks>
    /// <b>Takes what it can rather than refusing the lot</b>, and reports how much that was — the
    /// partial answer is the useful one, because a brush that runs out halfway should still place
    /// what it could afford and say so. A material that reaches zero is dropped from the map, so
    /// the save file never accumulates rows for materials nobody is carrying.
    /// </remarks>
    /// <returns>How many voxels were actually taken; zero if none were held.</returns>
    public long Take(int materialId, long voxels)
    {
        if (voxels < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(voxels), voxels, "Use Add to credit; Take never credits.");
        }

        long held = Voxels(materialId);
        long taken = Math.Min(held, voxels);

        if (taken == 0) return 0L;

        if (held == taken)
        {
            _voxels.Remove(materialId);
        }
        else
        {
            _voxels[materialId] = held - taken;
        }

        return taken;
    }

    /// <summary>
    /// Everything held, ordered by numeric material ID. The form the world file stores.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Ordered, so that the same inventory always writes the same bytes.</b> A dictionary's
    /// enumeration order is not a contract, and a save whose bytes shuffle between runs makes
    /// every "did this change?" question — in a test, in a diff, in a bug report — unanswerable.
    /// </para>
    /// <para>
    /// <b>V's cycle walks it too</b> (<c>PlaceController.NextMaterial</c>). From M6d-3 to alpha
    /// session C it was also the hotbar, whose slots were its first nine; the row is
    /// <see cref="Hotbar"/> now, and this order survives in it only as the row a world written
    /// before the slots opens with (<see cref="HotbarSlots.RowFor"/>).
    /// </para>
    /// </remarks>
    public IReadOnlyList<(int MaterialId, long Voxels)> Entries()
    {
        var entries = new List<(int MaterialId, long Voxels)>(_voxels.Count);

        foreach ((int materialId, long voxels) in _voxels)
        {
            entries.Add((materialId, voxels));
        }

        entries.Sort(static (a, b) => a.MaterialId.CompareTo(b.MaterialId));
        return entries;
    }

    /// <summary>
    /// The material in hotbar slot <paramref name="slot"/>, zero-based, or −1 if empty.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The row the player arranged, read from <see cref="Hotbar"/></b> (alpha session C). Until
    /// then it was the carried materials in ascending ID order — so picking up a material shifted
    /// every slot after it, which M6d-3 accepted with a named fix: an assignment the player
    /// arranges, saved with the world. That is the hotbar now, in header v8.
    /// </para>
    /// <para>
    /// <b>A slot can name a material held at zero</b>: the slot keeps it, dimmed, so the next bite
    /// lands back where the player's hand expects it.
    /// </para>
    /// </remarks>
    public int MaterialInSlot(int slot) => Hotbar.MaterialInSlot(slot);

    /// <summary>Replaces everything held with <paramref name="entries"/>. The load path.</summary>
    /// <remarks>
    /// <b>Offers nothing and leaves the row alone</b>: the world file carries the row too, and
    /// re-crediting every holding through the offer would hand the empty slots out in ID order
    /// before the saved row could be put back. The opener restores <see cref="Hotbar"/> beside this.
    /// </remarks>
    public void Reset(IEnumerable<(int MaterialId, long Voxels)> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        _voxels.Clear();

        foreach ((int materialId, long voxels) in entries)
        {
            Credit(materialId, voxels, offer: false);
        }
    }

    public override string ToString() =>
        $"{MaterialCount} material(s), {TotalLitres:F0} L";
}
