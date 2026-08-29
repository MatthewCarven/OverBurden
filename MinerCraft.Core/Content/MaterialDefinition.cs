namespace MinerCraft.Core.Content;

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
    public int SupportDistance { get; init; }

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
