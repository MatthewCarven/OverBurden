using Godot;
using MinerCraft.Core.Voxels;
using MinerCraft.Game.Rendering;

namespace MinerCraft.Game;

/// <summary>
/// M0 entry point: stand up the voxel material, build one culled block mesh, and hand it
/// to the RenderingServer. Everything here gets replaced by the chunk manager at M2.
/// </summary>
public partial class Main : Node3D
{
    private VoxelRenderInstance? _block;

    public override void _Ready()
    {
        var material = VoxelMaterial.Create();

        // One cubic metre of voxels — 4×4×4 at 0.25 m. Small enough to eyeball, and it
        // makes the working scale legible: this is the unit a placement brush will drop.
        var mesh = VoxelMeshBuilder.BuildSolidBlock(VoxelConstants.VoxelsPerMetre, material);

        _block = new VoxelRenderInstance(GetWorld3D(), mesh, Transform3D.Identity);

        int faces = (int)(mesh.GetFaces().Length / 3);
        GD.Print(
            $"M0: built a {VoxelConstants.VoxelsPerMetre}³ voxel block " +
            $"({VoxelConstants.VoxelsPerMetre * VoxelConstants.VoxelSize} m per side), " +
            $"{faces} triangles after interior culling " +
            $"(a naive mesh would be {VoxelConstants.VoxelsPerMetre * VoxelConstants.VoxelsPerMetre * VoxelConstants.VoxelsPerMetre * 12}).");
    }

    public override void _ExitTree()
    {
        _block?.Dispose();
        _block = null;
    }
}
