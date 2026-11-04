using Overburden.Core.Building;
using Overburden.Core.Content;

namespace Overburden.Core.Inventory;

/// <summary>What a press of <c>Ctrl+P</c> did (<see cref="DebugSeed.PressOnePack"/>).</summary>
public enum OnePackOutcome
{
    /// <summary>One pack went into the inventory.</summary>
    Given,

    /// <summary>Nothing given: that pack is already on the player's back.</summary>
    AlreadyWearing,

    /// <summary>Nothing given: this content has no such pack.</summary>
    NotInContent,
}

/// <summary>
/// What <c>P</c> puts in the inventory: a hundred of each item on every press, in the unit the hotbar
/// counts it in. And <c>Ctrl+P</c>'s lighter press, one lithium-ion pack.
/// </summary>
/// <remarks>
/// <para>
/// <b>A debug bind, retired by M10g's recipes.</b> It began as M10-PLAN decision 7's relay pair beside
/// <c>R</c> and grew a material at a time: the computer (M10b), the panel, the cell and the lead-acid
/// pack (M10c-2), clay (2026-09-20), glass (2026-09-25). Until 2026-10-12 a press gave one body of each
/// machine, 64 m³ of clay and of glass, and a computer and a pack once. His ask that day:
/// <i>"instead of giving 1 of every item including glass i want 100 of each item"</i>, with the
/// lithium-ion pack beside the lead-acid (<i>"Lead acid is so 20th century"</i>). Every press adds a
/// hundred, his choice over topping up to a hundred, so the computer and the packs are no longer once.
/// </para>
/// <para>
/// <b>A hundred in the hotbar's unit</b>, so the slot reads <c>× 100</c> or <c>100 m³</c>: a piece is
/// one voxel (<c>count</c>), a body is a cell of 64 (<c>cells</c>), and a volume's hundred is a hundred
/// cubic metres, which is also a cell of 64 voxels each (<see cref="VolumeText"/>).
/// </para>
/// <para>
/// <b>All or nothing.</b> A world whose content lacks any of them is seeded nothing and told which, as
/// before: a half seed reads as a bug in whatever is missing.
/// </para>
/// <para>
/// <b><c>Ctrl+P</c>, one pack</b> (2026-11-04, his ask: <i>"ctrl+p should give one lithium pack unless
/// you are already wearing one"</i>). It adds to the count and nothing else, so the pack is empty on
/// channel 0, as <see cref="CarriedPacks"/> reads a count with no entry beside it. That is why this
/// door needs no term: the pool's identity (<c>MachineTick.PowerExact</c>) sees the worn pack only and
/// the pocket has no identity of its own, so a carried count adds no energy, and wearing it brings
/// 0 mJ through <c>initial</c>.
/// </para>
/// </remarks>
public static class DebugSeed
{
    /// <summary>How many of each item one press adds.</summary>
    public const int Each = 100;

    /// <summary>Voxels in a brush cell: one machine body, and one cubic metre.</summary>
    public const long CellVoxels = (long)BrushCell.Voxels * BrushCell.Voxels * BrushCell.Voxels;

    /// <summary>The pack <c>Ctrl+P</c> gives.</summary>
    public static readonly Identifier OnePack = Identifier.Parse("overburden:lithium_ion_pack");

    /// <summary>
    /// The items, in the order they land on the hotbar's empty slots — the blocks and machines; the
    /// computer and the packs take a slot only by a pin (alpha session C, decision C3). So on the
    /// alpha's path, after the hand pick's grass and dirt, P fills slots 3 to 8 and leaves 9 for
    /// the first stone.
    /// </summary>
    public static readonly IReadOnlyList<Identifier> Materials =
    [
        Identifier.Parse("overburden:relay_pump"),
        Identifier.Parse("overburden:relay_outlet"),
        Identifier.Parse("overburden:solar_panel"),
        Identifier.Parse("overburden:vacuum_cell"),
        Identifier.Parse("overburden:clay"),
        Identifier.Parse("overburden:glass"),
        Identifier.Parse("overburden:computer"),
        Identifier.Parse("overburden:lead_acid_pack"),
        OnePack,
    ];

    /// <summary>Voxels in one of the hotbar's units: a piece, a body, or a cubic metre.</summary>
    public static long VoxelsPerUnit(MaterialUnit unit) => unit == MaterialUnit.Count ? 1L : CellVoxels;

    /// <summary>
    /// Each item's numeric ID and the voxels one press adds, or an empty list with
    /// <paramref name="missing"/> naming the first item this content lacks.
    /// </summary>
    public static IReadOnlyList<(int MaterialId, long Voxels)> Resolve(Registry<MaterialDefinition> materials, out Identifier? missing)
    {
        ArgumentNullException.ThrowIfNull(materials);
        var seed = new List<(int, long)>(Materials.Count);
        foreach (Identifier identifier in Materials)
        {
            if (!materials.TryGet(identifier, out MaterialDefinition? definition)
                || !materials.TryGetNumericId(identifier, out int id))
            {
                missing = identifier;
                return [];
            }

            seed.Add((id, Each * VoxelsPerUnit(definition.Unit)));
        }

        missing = null;
        return seed;
    }

    /// <summary>
    /// One press: a hundred of each added to <paramref name="inventory"/>, or nothing at all when
    /// <paramref name="missing"/> names an item this content lacks.
    /// </summary>
    public static bool Press(Registry<MaterialDefinition> materials, VolumetricInventory inventory, out Identifier? missing)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        IReadOnlyList<(int MaterialId, long Voxels)> seed = Resolve(materials, out missing);
        if (missing is not null)
        {
            return false;
        }

        foreach ((int materialId, long voxels) in seed)
        {
            inventory.Add(materialId, voxels);
        }

        return true;
    }

    /// <summary>
    /// One press of <c>Ctrl+P</c>: one <see cref="OnePack"/> added to <paramref name="inventory"/>, or
    /// nothing when <paramref name="wornMaterialId"/>, the pack on the player's back (null for none),
    /// is already one, or this content has no such pack.
    /// </summary>
    /// <remarks>
    /// Only the worn pack answers, as he asked: another kind on your back gives one, and so does any
    /// number of them carried, so every press adds another. One is a literal 1 whatever the unit,
    /// because that is what the equip verb takes off the count and hands back to it. A world keeps a
    /// removed mod's ID with an unknown placeholder behind it, which is no pack, so that refuses too.
    /// </remarks>
    public static OnePackOutcome PressOnePack(Registry<MaterialDefinition> materials, VolumetricInventory inventory, int? wornMaterialId)
    {
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentNullException.ThrowIfNull(inventory);
        if (!materials.TryGet(OnePack, out MaterialDefinition? pack) || pack.IsUnknown || !materials.TryGetNumericId(OnePack, out int id))
        {
            return OnePackOutcome.NotInContent;
        }

        if (wornMaterialId == id)
        {
            return OnePackOutcome.AlreadyWearing;
        }

        inventory.Add(id, 1);
        return OnePackOutcome.Given;
    }
}
