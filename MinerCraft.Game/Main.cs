using Godot;
using MinerCraft.Core.Content;
using MinerCraft.Core.Voxels;
using MinerCraft.Game.Rendering;

namespace MinerCraft.Game;

/// <summary>
/// M0/M1a entry point: load content from <c>mods/</c>, then build one culled block mesh
/// whose textures come from that content and hand it to the RenderingServer. Replaced by
/// the chunk manager at M2.
/// </summary>
public partial class Main : Node3D
{
    private VoxelRenderInstance? _block;

    public GameContent Content { get; } = new();

    public override void _Ready()
    {
        LoadContent();

        var material = VoxelMaterial.Create();

        // One cubic metre of voxels — 4×4×4 at 0.25 m. Small enough to eyeball, and it
        // makes the working scale legible: this is the unit a placement brush will drop.
        // The texture layers now come from mods/minercraft/materials/*.json rather than
        // from a hardcoded enum, which is the point of M1a.
        var mesh = VoxelMeshBuilder.BuildSolidBlock(
            VoxelConstants.VoxelsPerMetre,
            material,
            topLayer: TextureLayerOf("minercraft:grass"),
            sideLayer: TextureLayerOf("minercraft:dirt"),
            bottomLayer: TextureLayerOf("minercraft:stone"));

        _block = new VoxelRenderInstance(GetWorld3D(), mesh, Transform3D.Identity);

        int triangles = mesh.GetFaces().Length / 3;
        int naive = VoxelConstants.VoxelsPerMetre
                    * VoxelConstants.VoxelsPerMetre
                    * VoxelConstants.VoxelsPerMetre * 12;

        GD.Print(
            $"M0: {VoxelConstants.VoxelsPerMetre}³ voxel block " +
            $"({VoxelConstants.VoxelsPerMetre * VoxelConstants.VoxelSize} m per side), " +
            $"{triangles} triangles after interior culling (naive would be {naive}).");
    }

    /// <summary>
    /// Loads <c>res://mods/</c> through the same path a third-party mod takes.
    /// </summary>
    /// <remarks>
    /// GlobalizePath gives a real filesystem path in the editor and in debug builds. In an
    /// exported build res:// lives inside the .pck and this will not resolve — packaging
    /// loose mod content is a real problem, deferred until there is something to export.
    /// </remarks>
    private void LoadContent()
    {
        string modsPath = ProjectSettings.GlobalizePath("res://mods");
        var result = Content.Load(new DirectoryContentSource(modsPath));

        foreach (string error in result.Errors)
        {
            GD.PushError($"Content: {error}");
        }

        GD.Print($"Content: {result}");
        foreach (var (numericId, id, definition) in Content.Materials.Entries())
        {
            GD.Print(
                $"  [{numericId}] {id,-20} hardness={definition.Hardness,-5} " +
                $"support={definition.SupportDistance,-3} perm={definition.Permeability,-5} " +
                $"layer={definition.TextureLayer}");
        }
    }

    private int TextureLayerOf(string identifier)
    {
        // A missing material resolves to the unknown placeholder rather than throwing,
        // so a broken mod set still renders something rather than failing to start.
        return Content.Materials.TryGet(Identifier.Parse(identifier), out var definition)
            ? definition.TextureLayer
            : 0;
    }

    public override void _ExitTree()
    {
        _block?.Dispose();
        _block = null;
    }
}
