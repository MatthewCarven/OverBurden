using Godot;

namespace MinerCraft.Game.Rendering;

/// <summary>
/// Placeholder material layers for M0. These become registry-driven at M1 — the layer
/// index will come from a material definition loaded out of <c>mods/</c> rather than
/// from this enum.
/// </summary>
public enum PlaceholderLayer
{
    Stone = 0,
    Dirt = 1,
    Grass = 2,
    Lamp = 3,
}

/// <summary>
/// Builds the one material every chunk mesh shares: a <see cref="Texture2DArray"/> of
/// block textures plus the voxel shader.
/// </summary>
/// <remarks>
/// Textures are generated in code for now so there are no binary assets in the repo
/// this early. At M1 this loads real images through the content registry instead.
/// </remarks>
public static class VoxelMaterial
{
    private const int TextureSize = 16;

    /// <summary>
    /// The base colour of each texture layer, indexed by layer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One definition, shared by the block and by the hotbar swatch that stands for it.</b>
    /// M6d-3 needed a colour per material for the UI, and the obvious way to get one — a short
    /// palette in the HUD — is two definitions of what stone looks like. They agree on the day
    /// they are written and drift the first time either is retuned, and the symptom is a swatch
    /// that no longer matches the block it places. Same argument as <c>IsCaveAt</c> and
    /// <c>GenerateTerrain</c> sharing one definition of the cave field.
    /// </para>
    /// <para>
    /// Order must match <see cref="PlaceholderLayer"/>, which is what the material JSON's
    /// <c>textureLayer</c> indexes into.
    /// </para>
    /// </remarks>
    public static readonly Color[] LayerColour =
    [
        new Color(0.50f, 0.50f, 0.52f),   // stone
        new Color(0.42f, 0.30f, 0.20f),   // dirt
        new Color(0.28f, 0.55f, 0.24f),   // grass

        // M5b's lamp. Pale and near-white so it reads as a source rather than as a yellow
        // block, and so the block light it emits is what colours the room.
        new Color(0.96f, 0.92f, 0.78f),
    ];

    /// <summary>The colour for a layer, or a flat grey for one this build has no texture for.</summary>
    /// <remarks>
    /// <b>Clamped rather than indexed, because the layer comes from a mod's JSON.</b>
    /// <c>"textureLayer": 9</c> in a third-party material is a content error and not a reason to
    /// take the frame down — the same shape of argument as <c>ToolDefinition.MaxRadiusMetres</c>
    /// bounding a radius at load *and* at use.
    /// </remarks>
    public static Color ColourForLayer(int layer) =>
        layer >= 0 && layer < LayerColour.Length ? LayerColour[layer] : new Color(0.6f, 0.6f, 0.6f);

    /// <param name="skyBrightness">
    /// Scales the sky channel only. <b>1 is daylight and 0 is the middle of the night</b>, with
    /// nothing driving it yet — it exists so a diurnal cycle can be a uniform rather than a
    /// reason to reflood the light grid every frame, and it doubles as the A/B that isolates
    /// block light from skylight without a rebuild.
    /// </param>
    public static ShaderMaterial Create(float skyBrightness = 1f)
    {
        var shader = GD.Load<Shader>("res://Rendering/Voxel.gdshader");
        if (shader is null)
        {
            GD.PushError("MinerCraft: could not load res://Rendering/Voxel.gdshader");
        }

        // Jitter per layer, beside the shared colour rather than folded into it: the HUD wants
        // the flat colour and the block wants the speckle, and they are the same decision only
        // as far as the hue.
        float[] jitter = [0.08f, 0.12f, 0.10f, 0.04f];

        var layers = new Godot.Collections.Array<Image>();
        for (int layer = 0; layer < LayerColour.Length; layer++)
        {
            layers.Add(Speckled(LayerColour[layer], jitter[layer], seed: layer + 1));
        }

        var array = new Texture2DArray();
        array.CreateFromImages(layers);

        var material = new ShaderMaterial { Shader = shader };
        material.SetShaderParameter("albedo", array);
        material.SetShaderParameter("sky_brightness", skyBrightness);
        return material;
    }

    /// <summary>
    /// Flat colour with per-pixel brightness jitter — enough texture to read as a
    /// surface and to make face orientation obvious while debugging the mesher.
    /// Deterministic, so screenshots are comparable between runs.
    /// </summary>
    private static Image Speckled(Color baseColour, float jitter, int seed)
    {
        var image = Image.CreateEmpty(TextureSize, TextureSize, false, Image.Format.Rgba8);
        var rng = new System.Random(seed);

        for (int y = 0; y < TextureSize; y++)
        {
            for (int x = 0; x < TextureSize; x++)
            {
                float delta = (float)(rng.NextDouble() * 2.0 - 1.0) * jitter;
                image.SetPixel(x, y, new Color(
                    Mathf.Clamp(baseColour.R + delta, 0f, 1f),
                    Mathf.Clamp(baseColour.G + delta, 0f, 1f),
                    Mathf.Clamp(baseColour.B + delta, 0f, 1f)));
            }
        }

        return image;
    }
}
