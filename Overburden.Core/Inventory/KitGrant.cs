using Overburden.Core.Content;
using Overburden.Core.Persistence;

namespace Overburden.Core.Inventory;

/// <summary>
/// The union of every kit (IM-6's pass, decision 22) resolved against the world being created: what the pocket is given,
/// the row, and the pack worn.
/// </summary>
/// <param name="Voxels">Each material's numeric ID and the voxels granted, summed over the kits, in ID order.</param>
/// <param name="Row">The row a kit sets, nine numeric IDs with <see cref="HotbarSlots.Empty"/> for an empty slot, or null.</param>
/// <param name="Pack">The pack a kit puts on — its numeric ID and channel — or null.</param>
/// <param name="Kits">The kits it came from, by ID, in load order, for the printed line.</param>
public sealed record KitGrantPlan(
    IReadOnlyList<(int MaterialId, long Voxels)> Voxels,
    IReadOnlyList<int>? Row,
    (int MaterialId, int Channel)? Pack,
    IReadOnlyList<Identifier> Kits)
{
    /// <summary>Every voxel the pocket is given — the <c>granted</c> term the <c>M6c:</c> line prints.</summary>
    public long TotalVoxels => Voxels.Sum(entry => entry.Voxels);

    /// <summary>Nothing to grant: a content set with no kits.</summary>
    public bool IsEmpty => Voxels.Count == 0 && Row is null && Pack is null;
}

/// <summary>
/// The kit's door into the pocket (IDEAL-MACHINES-PLAN decision 53; IM-6's pass, decision 22): once, at a creative world's
/// creation, every kit's materials as real counts, the row and the worn pack.
/// </summary>
/// <remarks>
/// <para>
/// <b>It mints, by design, and says so</b>: the voxels are printed as <c>granted</c> on that session's <c>M6c:</c> line, and
/// the header's bit 0 (<see cref="Persistence.WorldFlags.KitGranted"/>) is set in the write that carries them, so a reopen
/// never grants twice. The pocket has no identity of its own (<see cref="DebugSeed"/>'s remarks), so the term is the
/// printed number and the flag; the undo then refunds and charges real counts.
/// </para>
/// <para>
/// <b>The pack is worn empty</b>: a fresh pack holds nothing, and the quantum backpack is a window that holds nothing in any
/// case. Wearing it brings 0 mJ through the pool's <c>initial</c> term, so the energy identity needs nothing new here.
/// </para>
/// </remarks>
public static class KitGrant
{
    /// <summary>
    /// The union of <paramref name="kits"/> against <paramref name="materials"/>, the world's registry: each material's
    /// voxels summed, the one row, the one pack. The loader has already checked every name, so a name this registry
    /// lacks is a bug and throws.
    /// </summary>
    public static KitGrantPlan Plan(KitTable kits, Registry<MaterialDefinition> materials)
    {
        ArgumentNullException.ThrowIfNull(kits);
        ArgumentNullException.ThrowIfNull(materials);

        var voxels = new SortedDictionary<int, long>();
        int[]? row = null;
        (int, int)? pack = null;
        var ids = new List<Identifier>(kits.Count);

        foreach (KitDefinition kit in kits.Kits)
        {
            ids.Add(kit.Id);
            foreach (KitMaterial granted in kit.Materials)
            {
                int id = materials.GetNumericId(granted.Material);
                long each = DebugSeed.VoxelsPerUnit(materials.Get(id).Unit);
                voxels[id] = checked(voxels.GetValueOrDefault(id) + (granted.Count * each));
            }

            if (kit.Row is { } named)
            {
                row = [.. named.Select(slot => slot is { } material ? materials.GetNumericId(material) : HotbarSlots.Empty)];
            }

            if (kit.Pack is { } worn)
            {
                pack = (materials.GetNumericId(worn.Material), worn.Channel);
            }
        }

        return new KitGrantPlan([.. voxels.Select(entry => (entry.Key, entry.Value))], row, pack, ids);
    }

    /// <summary>
    /// The once-only rule (decision 22): a creative world whose header's bit 0 is clear is granted the union now and
    /// returns its flags with bit 0 set, for the header write that carries the voxels. Anything else — a sandbox world,
    /// a creative world already granted — is granted nothing and its flags come back as they went in.
    /// </summary>
    /// <remarks>
    /// <b>Bit 0 is set even when the union is empty</b>, so "once, at creation" holds for a mod installed later: its kit
    /// never arrives on a reopen. The worn pack is in <paramref name="plan"/> for the caller to put on.
    /// </remarks>
    public static WorldFlags GrantIfDue(
        WorldMode mode,
        WorldFlags flags,
        KitTable kits,
        Registry<MaterialDefinition> materials,
        VolumetricInventory inventory,
        out KitGrantPlan? plan)
    {
        plan = null;
        if (mode != WorldMode.Creative || (flags & WorldFlags.KitGranted) != 0)
        {
            return flags;
        }

        plan = Plan(kits, materials);
        Apply(plan, inventory);
        return flags | WorldFlags.KitGranted;
    }

    /// <summary>
    /// Grants <paramref name="plan"/>'s voxels into <paramref name="inventory"/> and sets its row. The pack is the caller's
    /// to wear, since wearing is the tick's door. Returns the voxels the pocket took.
    /// </summary>
    /// <remarks>
    /// The row last, so a kit's row replaces whatever slots the credits were offered — a kit's row is a set of pins, and
    /// may pin a container that would otherwise stay off the row.
    /// </remarks>
    public static long Apply(KitGrantPlan plan, VolumetricInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(inventory);

        long refusedBefore = inventory.RefusedVoxels;
        foreach ((int materialId, long voxels) in plan.Voxels)
        {
            inventory.Add(materialId, voxels);
        }

        if (plan.Row is { } row)
        {
            inventory.Hotbar.Restore(row);
        }

        return plan.TotalVoxels - (inventory.RefusedVoxels - refusedBefore);
    }
}
