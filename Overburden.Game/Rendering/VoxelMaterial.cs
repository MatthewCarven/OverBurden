using Godot;

namespace Overburden.Game.Rendering;

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

        // M9c-1's rubble: a grey-brown coarser than stone, so a fallen roof reads as a pile
        // and not as more wall. A screenshot's call, like the rest of this table.
        new Color(0.44f, 0.38f, 0.32f),

        // M10a-2's relay pump and outlet: a dark iron blue for the body that takes water and
        // a brass for the one that gives it, so the pair reads as a pair from across a room.
        // Unscreenshotted; a look call for M10d's play.
        new Color(0.22f, 0.28f, 0.40f),
        new Color(0.72f, 0.55f, 0.22f),

        // M10b's computer: an item in the hand, never a block, so this colour is only ever a
        // hotbar swatch — a slate with a hint of green, the one thing here that is not rock.
        new Color(0.30f, 0.36f, 0.34f),

        // M10c-2's power: a solar panel in a deep glassy blue so a field reads as a field on
        // grass; a vacuum cell in a near-black with a violet cast, the one body that holds
        // something nobody can see; and the lead-acid pack, a hotbar swatch only, in a dull
        // battery grey. Unscreenshotted; look calls for M10d's play.
        new Color(0.12f, 0.20f, 0.42f),
        new Color(0.16f, 0.12f, 0.22f),
        new Color(0.40f, 0.40f, 0.38f),

        // Clay (2026-09-20): the one rock a player lines a room with, at permeability 0. A
        // warm grey-tan between dirt's brown and stone's grey, so a lining reads as a lining
        // and not as more dirt. Unscreenshotted; a look call for the next play.
        new Color(0.56f, 0.47f, 0.38f),

        // Glass (2026-09-25, docs/GLASS-PLAN.md): a pale blue-grey, drawn at the glass shader's
        // alpha, so a pane reads as a pane over grass and over stone alike. Near-flat: a speckle
        // on something you look through reads as dirt on it. A screenshot's call.
        new Color(0.78f, 0.88f, 0.92f),

        // The quantum backpack (IM-2, IDEAL-MACHINES-PLAN decision 7): a hotbar swatch only, in the
        // entanglement's cyan (decision 29, "cyan is entanglement at work") on a dark body, so the
        // window reads apart from the lead-acid pack's battery grey. Unscreenshotted.
        new Color(0.16f, 0.52f, 0.56f),

        // The lithium-ion pack (2026-10-12, his ask: "Lead acid is so 20th century"): a hotbar swatch
        // only, in lithium's crimson (its flame test), so it reads apart from the lead-acid pack's grey
        // and the backpack's cyan. Unscreenshotted; a look call for his next play.
        new Color(0.62f, 0.10f, 0.18f),
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
            GD.PushError("Overburden: could not load res://Rendering/Voxel.gdshader");
        }

        // Jitter per layer, beside the shared colour rather than folded into it: the HUD wants
        // the flat colour and the block wants the speckle, and they are the same decision only
        // as far as the hue.
        // The relay pair (M10a-2) is smoother than rock — a cast body, not a face of it — and
        // the table is asserted against the colours so a sixth colour cannot ship without a sixth
        // jitter: the r6 control threw an IndexOutOfRange from here on the night the pair was added.
        float[] jitter = [0.08f, 0.12f, 0.10f, 0.04f, 0.16f, 0.03f, 0.03f, 0.02f, 0.02f, 0.02f, 0.03f, 0.06f, 0.01f, 0.02f, 0.02f];
        if (jitter.Length != LayerColour.Length)
        {
            GD.PushError($"Overburden: {LayerColour.Length} layer colours but {jitter.Length} jitters; the two tables have drifted");
        }

        var layers = new Godot.Collections.Array<Image>();
        for (int layer = 0; layer < LayerColour.Length; layer++)
        {
            layers.Add(Speckled(LayerColour[layer], layer < jitter.Length ? jitter[layer] : 0.08f, seed: layer + 1));
        }

        var array = new Texture2DArray();
        array.CreateFromImages(layers);

        var material = new ShaderMaterial { Shader = shader };
        material.SetShaderParameter("albedo", array);
        material.SetShaderParameter("sky_brightness", skyBrightness);
        return material;
    }

    /// <summary>
    /// The material for the chunk mesh's transparent surface — glass (docs/GLASS-PLAN.md) — sharing
    /// the rock material's texture array, so the layer a pane's JSON names is the same layer.
    /// The caller keeps the shared parameters (sky brightness, wetness, the tint's space) in step
    /// on both; <c>Main.SetVoxelParameter</c> is the one place that does.
    /// </summary>
    public static ShaderMaterial CreateGlass(ShaderMaterial solid)
    {
        System.ArgumentNullException.ThrowIfNull(solid);
        var shader = GD.Load<Shader>("res://Rendering/VoxelGlass.gdshader");
        if (shader is null)
        {
            GD.PushError("Overburden: could not load res://Rendering/VoxelGlass.gdshader");
        }

        var material = new ShaderMaterial { Shader = shader };
        material.SetShaderParameter("albedo", solid.GetShaderParameter("albedo"));
        material.SetShaderParameter("sky_brightness", solid.GetShaderParameter("sky_brightness"));
        return material;
    }

    /// <summary>
    /// The same texture array and sky under another voxel shader — a falling piece's
    /// (<c>VoxelPiece.gdshader</c>, COLLAPSE-PLAN part c) — kept in step by the caller like glass.
    /// </summary>
    public static ShaderMaterial CreateVariant(ShaderMaterial solid, string shaderPath)
    {
        System.ArgumentNullException.ThrowIfNull(solid);
        var shader = GD.Load<Shader>(shaderPath);
        if (shader is null)
        {
            GD.PushError($"Overburden: could not load {shaderPath}");
        }

        var material = new ShaderMaterial { Shader = shader };
        material.SetShaderParameter("albedo", solid.GetShaderParameter("albedo"));
        material.SetShaderParameter("sky_brightness", solid.GetShaderParameter("sky_brightness"));
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
