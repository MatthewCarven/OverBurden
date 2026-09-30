using Overburden.Core.Machines;

namespace Overburden.Core.Content;

/// <summary>
/// Every registry the game has, and the one call that fills them.
/// </summary>
/// <remarks>
/// Items, tools, recipes, worldgen features and biomes all land here as they arrive.
/// Keeping them behind one object means the load phase has exactly one entry point, and
/// the "seed from the world's saved tables, then load, then freeze" sequence lives in
/// one place rather than being re-implemented per registry.
/// </remarks>
public sealed class GameContent
{
    public Registry<MaterialDefinition> Materials { get; } =
        new("materials", MaterialDefinition.Unknown);

    /// <summary>Mining tool tiers.</summary>
    /// <remarks>
    /// No <c>savedToolIds</c> counterpart to <see cref="Load"/>, and that is not an
    /// oversight. A material's numeric ID is baked into every stored voxel, so it has to
    /// survive a change of mod set; a tool's is referenced by nothing that is written to
    /// disk yet. When M6's inventory starts saving a held tool, that is the moment this
    /// registry needs seeding too — and it will be the same three lines.
    /// </remarks>
    public Registry<ToolDefinition> Tools { get; } = new("tools", ToolDefinition.Unknown);

    /// <summary>Every mod's <c>extras.json</c>, unioned (IDEAL-MACHINES-PLAN decision 3): which form each extra takes over each medium.</summary>
    public ExtrasTable Extras { get; } = new();

    private readonly MachineExtra _servedExtras;

    /// <param name="servedExtras">
    /// The extras a machine may opt into — this build's <see cref="MachineExtras.Served"/>. The game
    /// never passes it; a test does, to reach an extra whose reader is a later slice.
    /// </param>
    public GameContent(MachineExtra servedExtras = MachineExtras.Served)
    {
        _servedExtras = servedExtras;
    }

    /// <summary>
    /// Loads content from <paramref name="source"/>, optionally restoring a world's saved
    /// numeric ID assignment first.
    /// </summary>
    /// <param name="savedMaterialIds">
    /// The material ID table from the world file, index = numeric ID. Null for a new
    /// world. Passing this is what makes IDs stable across sessions and across changes to
    /// the installed mod set.
    /// </param>
    public LoadResult Load(IContentSource source, IReadOnlyList<Identifier>? savedMaterialIds = null)
    {
        if (savedMaterialIds is { Count: > 0 })
        {
            Materials.SeedIdTable(savedMaterialIds);
        }

        return new ContentLoader(_servedExtras).Load(source, Materials, Tools, Extras);
    }
}
