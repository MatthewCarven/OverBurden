using Overburden.Core.Machines;

namespace Overburden.Core.Content;

/// <summary>
/// Everything the simulation knows about one kind of voxel.
/// </summary>
/// <remarks>
/// Several of these properties have no consumer yet — <see cref="SupportDistance"/> is
/// for M9's structural integrity and <see cref="Permeability"/> for M8's groundwater
/// seepage. They are here now because they belong to the material and cost nothing to
/// carry, whereas adding fields to a format mods have already written against is a
/// migration.
/// </remarks>
public sealed class MaterialDefinition
{
    public required Identifier Id { get; init; }

    /// <summary>Relative effort to mine. Higher is slower; air and fluids are 0.</summary>
    public float Hardness { get; init; }

    /// <summary>
    /// How far, in voxels, this material carries unsupported load before it collapses.
    /// Stone carries far, dirt less, sand barely at all. Consumed by M9.
    /// </summary>
    /// <remarks>
    /// M9-PLAN decision 1 (taken 2026-09-11): the lateral reach at a roof's underside; each voxel
    /// of rock above buys a constant's worth more (the arch), and a <em>hanging</em> voxel spends
    /// double, so a material hangs half as far as it spans. Capped at 48 by the loader at M9a,
    /// because the field is a byte and a mod's 1000 would be a roof that never falls.
    /// </remarks>
    public int SupportDistance { get; init; }

    /// <summary>
    /// The most <see cref="SupportDistance"/> may be: 48 voxels, 12 m. Refused above it by the
    /// loader, the way <c>ToolDefinition.MaxRadiusMetres</c> refuses a radius.
    /// </summary>
    /// <remarks>
    /// The field is a byte with a budget of <c>SupportRule.MaxBudget</c> (120), so a step costs
    /// <c>round(120 / distance)</c>: exact for every shipped distance, within 10% to 32 and within
    /// 20% to 48. A mod's 1000 would round to zero — a roof that never falls — which is why it is
    /// refused rather than clamped: whoever wrote it believed the range was wider.
    /// </remarks>
    public const int MaxSupportDistance = 48;

    /// <summary>
    /// How <see cref="SupportDistance"/> falls as the head of water over the voxel rises — M9-PLAN
    /// decision 5, the saturation curve. Empty means dry everywhere.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Points of (head in metres, support distance in voxels), strictly increasing in head and
    /// non-increasing in distance, with <see cref="SupportDistance"/> the implied point at zero
    /// head; linear between points, constant past the last. The loader refuses anything else, so a
    /// reader may assume the shape. The head comes from <c>Saturation.HeadMetresAt</c> — the
    /// quantity M8c defined for this reader — and the curve is the half of MATERIALS-NOTES'
    /// decision 1 that M8c-1 left for M9.
    /// </para>
    /// <para>
    /// A curve, per material, in the JSON, because it is the one interaction DESIGN calls the
    /// thing that ties the game together, and a constant here would be the trap MATERIALS-NOTES
    /// names — shipping green with the field decorative.
    /// </para>
    /// </remarks>
    public IReadOnlyList<SupportPoint> SupportUnderHead { get; init; } = [];

    /// <summary>
    /// How readily groundwater passes through, 0 (sealed) to 1 (open). Consumed by M8;
    /// this is the property that decides whether a tunnel stays dry.
    /// </summary>
    public float Permeability { get; init; }

    /// <summary>Kilograms per cubic metre. Feeds collapse and volumetric inventory.</summary>
    public float Density { get; init; }

    /// <summary>Layer index into the voxel <c>Texture2DArray</c>.</summary>
    public int TextureLayer { get; init; }

    /// <summary>
    /// How brightly this material glows, 0 (dark) to <see cref="MaxLightEmission"/>.
    /// </summary>
    /// <remarks>
    /// <b>0..15 matches the propagated field with no scaling</b>, which means the brightest lamp
    /// any mod can define reaches exactly 15 m — a game-balance number wearing a storage costume,
    /// the same shape as terminal velocity being a streaming parameter in a physics one. Settled
    /// 2026-08-30 with a revisit trigger in Matthew's words: <i>"unless we come up with better
    /// lighting later"</i>. A wider field or coloured light would take the range with it.
    /// </remarks>
    public int LightEmission { get; init; }

    /// <summary>The brightest a material may be. Matches the 4-bit light field exactly.</summary>
    public const int MaxLightEmission = 15;

    /// <summary>
    /// Whether this material hides the faces behind it. The mesher's face-culling test
    /// is exactly this, so it is the single most performance-relevant flag here.
    /// </summary>
    public bool IsOpaque { get; init; } = true;

    /// <summary>Flows, and renders in the transparent pass. Water and gases.</summary>
    public bool IsFluid { get; init; }

    /// <summary>Can be built over without mining first — air, fluids, loose rubble.</summary>
    public bool IsReplaceable { get; init; }

    /// <summary>
    /// True for the stand-in given to an ID whose defining mod is not installed. It keeps
    /// the original <see cref="Id"/>, so re-installing the mod restores the world rather
    /// than reinterpreting its blocks.
    /// </summary>
    public bool IsUnknown { get; init; }

    /// <summary>
    /// The <c>machine</c> block, or null for a material that is only a voxel. A body of this
    /// material is one brush cell with a row (M10-PLAN decision 1; M10a-2).
    /// </summary>
    public MachineDefinition? Machine { get; init; }

    /// <summary>Whether a body of this material is a machine.</summary>
    public bool IsMachine => Machine is not null;

    /// <summary>
    /// Whether the brush may put this material into the world. True for every block; false for
    /// an item that lives in the hand — the computer (M10b), the ladder's parts (M10g). A material
    /// that is not placeable is still carried, counted and shown on the hotbar; selecting it and
    /// clicking is the item's own verb, never a placement.
    /// </summary>
    public bool IsPlaceable { get; init; } = true;

    /// <summary>
    /// How the hotbar counts this material: litres (the default), one voxel a part
    /// (<c>count</c>), or 64 voxels a body (<c>cells</c>). Presentation only — the inventory
    /// counts voxels whatever the unit (the ladder's decision 6).
    /// </summary>
    public MaterialUnit Unit { get; init; } = MaterialUnit.Litres;

    /// <summary>Nothing here — the emptiness test throughout the engine.</summary>
    public bool IsAir => Id == MaterialIds.Air;

    /// <summary>
    /// Hardness given to a material whose defining mod is not installed.
    /// </summary>
    /// <remarks>
    /// Harder than stone, so a removed mod's blocks resist an ordinary tool, but finite —
    /// a player who has walled themselves in behind a mod they uninstalled can still dig
    /// out. Named because <see cref="MaterialTable"/> needs the same number for the holes
    /// that have no identifier left at all.
    /// </remarks>
    public const float UnknownHardness = 5f;

    /// <summary>
    /// The placeholder for a missing mod. Solid and opaque on purpose: silently turning
    /// a removed mod's blocks into air would let the world quietly collapse into holes.
    /// </summary>
    public static MaterialDefinition Unknown(Identifier id) => new()
    {
        Id = id,
        IsUnknown = true,
        Hardness = UnknownHardness,
        SupportDistance = 8,
        Permeability = 0f,
        Density = 2000f,
        TextureLayer = 0,
        IsOpaque = true,
    };

    public override string ToString() => IsUnknown ? $"{Id} (unknown)" : Id.ToString();
}

/// <summary>One point on a material's saturation curve: at this head, this support distance.</summary>
public readonly record struct SupportPoint(float HeadMetres, float Distance);

/// <summary>How a material is counted on the hotbar. See <see cref="MaterialDefinition.Unit"/>.</summary>
public enum MaterialUnit : byte
{
    Litres,
    Count,
    Cells,
}
