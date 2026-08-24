namespace MinerCraft.Core.Content;

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

        return new ContentLoader().Load(source, Materials);
    }
}
