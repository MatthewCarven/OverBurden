namespace MinerCraft.Core.Content;

/// <summary>
/// Material properties flattened into arrays indexed by numeric ID.
/// </summary>
/// <remarks>
/// The mesher asks "is this opaque?" tens of millions of times a second. Going through
/// the registry's dictionary for that would dominate the profile, so the handful of
/// properties the hot paths need are flattened once, after the registry freezes, into
/// plain arrays. Immutable afterwards, so worker threads read it without synchronisation.
/// </remarks>
public sealed class MaterialTable
{
    private readonly bool[] _isOpaque;
    private readonly bool[] _isFluid;
    private readonly int[] _textureLayer;

    private MaterialTable(bool[] isOpaque, bool[] isFluid, int[] textureLayer)
    {
        _isOpaque = isOpaque;
        _isFluid = isFluid;
        _textureLayer = textureLayer;
    }

    public int Count => _isOpaque.Length;

    public bool IsOpaque(int materialId) => _isOpaque[materialId];

    public bool IsFluid(int materialId) => _isFluid[materialId];

    public int TextureLayer(int materialId) => _textureLayer[materialId];

    public static MaterialTable From(Registry<MaterialDefinition> materials)
    {
        if (!materials.IsFrozen)
        {
            throw new InvalidOperationException(
                "Flatten the material table only after the registry is frozen, or it will " +
                "miss content registered afterwards.");
        }

        int count = materials.Count;
        var isOpaque = new bool[count];
        var isFluid = new bool[count];
        var textureLayer = new int[count];

        for (int id = 0; id < count; id++)
        {
            if (!materials.GetIdentifier(id).IsValid)
            {
                // A hole left by a removed mod whose identifier is no longer known. Treat
                // it as solid so the world does not develop gaps where it used to be.
                isOpaque[id] = true;
                continue;
            }

            var definition = materials.Get(id);
            isOpaque[id] = definition.IsOpaque;
            isFluid[id] = definition.IsFluid;
            textureLayer[id] = definition.TextureLayer;
        }

        return new MaterialTable(isOpaque, isFluid, textureLayer);
    }

    /// <summary>Builds a table directly, for tests and tools that have no registry.</summary>
    internal static MaterialTable ForTesting(bool[] isOpaque, bool[] isFluid, int[] textureLayer) =>
        new(isOpaque, isFluid, textureLayer);
}
