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
    private readonly float[] _hardness;

    private MaterialTable(bool[] isOpaque, bool[] isFluid, int[] textureLayer, float[] hardness)
    {
        _isOpaque = isOpaque;
        _isFluid = isFluid;
        _textureLayer = textureLayer;
        _hardness = hardness;
    }

    public int Count => _isOpaque.Length;

    public bool IsOpaque(int materialId) => _isOpaque[materialId];

    public bool IsFluid(int materialId) => _isFluid[materialId];

    public int TextureLayer(int materialId) => _textureLayer[materialId];

    /// <summary>Relative effort to mine. A carve removes a voxel only when a tool's
    /// threshold reaches this.</summary>
    /// <remarks>
    /// Here rather than through the registry for the same reason the other three are: the
    /// sphere carve asks it once per candidate voxel, and a swing has thousands of those.
    /// </remarks>
    public float Hardness(int materialId) => _hardness[materialId];

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
        var hardness = new float[count];

        for (int id = 0; id < count; id++)
        {
            if (!materials.GetIdentifier(id).IsValid)
            {
                // A hole left by a removed mod whose identifier is no longer known. Treat
                // it as solid so the world does not develop gaps where it used to be, and
                // as hard as the named placeholder so a hole is not quietly the easiest
                // thing in the world to mine through.
                isOpaque[id] = true;
                hardness[id] = MaterialDefinition.UnknownHardness;
                continue;
            }

            var definition = materials.Get(id);
            isOpaque[id] = definition.IsOpaque;
            isFluid[id] = definition.IsFluid;
            textureLayer[id] = definition.TextureLayer;
            hardness[id] = definition.Hardness;
        }

        return new MaterialTable(isOpaque, isFluid, textureLayer, hardness);
    }

    /// <summary>Builds a table directly, for tests and tools that have no registry.</summary>
    /// <param name="hardness">
    /// Required, not optional. An omitted array would default every material to zero, which
    /// is "any tool mines this", so a carve test that forgot the argument would pass for the
    /// wrong reason. Fail-open defaults in a test helper are how a suite quietly stops
    /// testing; a test with no interest in mining passes zeros and says so.
    /// </param>
    internal static MaterialTable ForTesting(
        bool[] isOpaque,
        bool[] isFluid,
        int[] textureLayer,
        float[] hardness) =>
        new(isOpaque, isFluid, textureLayer, hardness);
}
