using Overburden.Core.Building;
using Overburden.Core.Content;

namespace Overburden.Core.Inventory;

/// <summary>
/// What <c>P</c> puts in the inventory: a hundred of each item on every press, in the unit the hotbar
/// counts it in.
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
/// </remarks>
public static class DebugSeed
{
    /// <summary>How many of each item one press adds.</summary>
    public const int Each = 100;

    /// <summary>Voxels in a brush cell: one machine body, and one cubic metre.</summary>
    public const long CellVoxels = (long)BrushCell.Voxels * BrushCell.Voxels * BrushCell.Voxels;

    /// <summary>The items, in the order they first land on the hotbar.</summary>
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
        Identifier.Parse("overburden:lithium_ion_pack"),
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
}
