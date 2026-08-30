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

    public static ShaderMaterial Create()
    {
        var shader = GD.Load<Shader>("res://Rendering/Voxel.gdshader");
        if (shader is null)
        {
            GD.PushError("MinerCraft: could not load res://Rendering/Voxel.gdshader");
        }

        var layers = new Godot.Collections.Array<Image>
        {
            // Order must match PlaceholderLayer.
            Speckled(new Color(0.50f, 0.50f, 0.52f), 0.08f, seed: 1),
            Speckled(new Color(0.42f, 0.30f, 0.20f), 0.12f, seed: 2),
            Speckled(new Color(0.28f, 0.55f, 0.24f), 0.10f, seed: 3),

            // M5b's lamp. Pale and near-white so it reads as a source rather than as a
            // yellow block, and so the block light it emits is what colours the room.
            Speckled(new Color(0.96f, 0.92f, 0.78f), 0.04f, seed: 4),
        };

        var array = new Texture2DArray();
        array.CreateFromImages(layers);

        var material = new ShaderMaterial { Shader = shader };
        material.SetShaderParameter("albedo", array);
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
