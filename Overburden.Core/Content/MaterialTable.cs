namespace Overburden.Core.Content;

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
    private readonly byte[] _lightEmission;
    private readonly float[] _permeability;

    private MaterialTable(
        bool[] isOpaque,
        bool[] isFluid,
        int[] textureLayer,
        float[] hardness,
        byte[] lightEmission,
        float[] permeability)
    {
        _isOpaque = isOpaque;
        _isFluid = isFluid;
        _textureLayer = textureLayer;
        _hardness = hardness;
        _lightEmission = lightEmission;
        _permeability = permeability;
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

    /// <summary>How brightly this material glows, 0..15.</summary>
    /// <remarks>
    /// Projected here for the same reason <see cref="Hardness"/> is: the light build asks it
    /// once per voxel across a whole chunk, and that is 32,768 registry lookups a chunk would
    /// not survive. Stored as a byte because the field it feeds is four bits wide.
    /// </remarks>
    public int LightEmission(int materialId) => _lightEmission[materialId];

    /// <summary>How readily groundwater passes through this material, 0 (sealed) to 1 (open).</summary>
    /// <remarks>
    /// <para>
    /// Projected here at M8c, the field's first reader since M1 wrote it into the format — the
    /// seep scan asks it once per rock face of a dug chunk, up to 196,608 times a chunk bite,
    /// which is <see cref="Hardness"/>'s argument again. MATERIALS-NOTES names the trap this
    /// closes: a milestone in a hurry writes <c>const float Permeability = 0.02f</c>, ships green,
    /// and the JSON field stays decorative while mods write against the constant.
    /// </para>
    /// <para>
    /// <b>Always inside [0, 1], and NaN is 0.</b> The loader refuses a value outside the range; the
    /// table clamps as well, because the loader is not the only way a definition gets here (M3c's
    /// carve-radius argument). NaN fails <i>closed</i>: every comparison with NaN is false, so a NaN
    /// rate multiplied through a seep coefficient would poison the chunk's whole sum, and a sealed
    /// wall is the failure that loses nothing.
    /// </para>
    /// </remarks>
    public float Permeability(int materialId) => _permeability[materialId];

    /// <summary>The permeability the table stores for a definition: clamped to [0, 1], NaN to 0.</summary>
    public static float BoundPermeability(float permeability) =>
        float.IsNaN(permeability) ? 0f : Math.Clamp(permeability, 0f, 1f);

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
        var lightEmission = new byte[count];
        var permeability = new float[count];

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

                // Sealed, like the named placeholder: a hole's rock leaks nothing, so a removed
                // mod cannot flood a mine that was dry when it was installed.
                permeability[id] = 0f;
                continue;
            }

            var definition = materials.Get(id);
            isOpaque[id] = definition.IsOpaque;
            isFluid[id] = definition.IsFluid;
            textureLayer[id] = definition.TextureLayer;
            hardness[id] = definition.Hardness;

            // Clamped as well as validated at load, because the loader is not the only way a
            // definition gets here — the same belt-and-braces argument as the carve radius.
            // A value past 15 would otherwise overflow the nibble and wrap a lamp to darkness.
            lightEmission[id] = (byte)Math.Clamp(
                definition.LightEmission, 0, MaterialDefinition.MaxLightEmission);
            permeability[id] = BoundPermeability(definition.Permeability);
        }

        return new MaterialTable(isOpaque, isFluid, textureLayer, hardness, lightEmission, permeability);
    }

    /// <summary>Builds a table directly, for tests and tools that have no registry.</summary>
    /// <param name="hardness">
    /// Required, not optional. An omitted array would default every material to zero, which
    /// is "any tool mines this", so a carve test that forgot the argument would pass for the
    /// wrong reason. Fail-open defaults in a test helper are how a suite quietly stops
    /// testing; a test with no interest in mining passes zeros and says so.
    /// </param>
    /// <param name="lightEmission">
    /// Optional, unlike <paramref name="hardness"/>, and the asymmetry is deliberate. Zero
    /// emission is "this material does not glow", which is the honest default for the hundred
    /// tests that predate lighting; zero hardness is "any tool mines this", which is a
    /// fail-<em>open</em> default a carve test could pass under for the wrong reason.
    /// </param>
    /// <param name="permeability">
    /// Optional for the reason <paramref name="lightEmission"/> is: zero is "this rock is
    /// sealed", the closed default, and a seep test that wants a wall to leak says so.
    /// Bounded exactly as <see cref="From"/> bounds it.
    /// </param>
    internal static MaterialTable ForTesting(
        bool[] isOpaque,
        bool[] isFluid,
        int[] textureLayer,
        float[] hardness,
        byte[]? lightEmission = null,
        float[]? permeability = null) =>
        new(isOpaque, isFluid, textureLayer, hardness,
            lightEmission ?? new byte[isOpaque.Length],
            permeability is null
                ? new float[isOpaque.Length]
                : Array.ConvertAll(permeability, BoundPermeability));
}
