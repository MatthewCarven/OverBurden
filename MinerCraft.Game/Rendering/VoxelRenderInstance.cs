using System;
using Godot;

namespace MinerCraft.Game.Rendering;

/// <summary>
/// One drawable mesh registered directly with the <see cref="RenderingServer"/>, with no
/// <see cref="MeshInstance3D"/> node behind it.
/// </summary>
/// <remarks>
/// The node path costs a Node, a NodePath entry and per-frame tree overhead per chunk,
/// which stops being free at the several-hundred-to-thousand chunk counts 0.25 m voxels
/// imply. Going through RenderingServer from the start means the chunk renderer at M2
/// does not have to be rewritten. Whether the difference actually matters at our real
/// chunk count is still on the TODO to measure rather than assume.
/// </remarks>
public sealed class VoxelRenderInstance : IDisposable
{
    private Rid _instance;

    /// <summary>
    /// Held deliberately. The instance's base is the mesh's RID, and if the managed
    /// ArrayMesh were collected the RID would be freed underneath us, leaving this
    /// instance pointing at nothing. Keeping the reference is what prevents that.
    /// </summary>
    private readonly ArrayMesh _mesh;

    public VoxelRenderInstance(World3D world, ArrayMesh mesh, Transform3D transform)
    {
        _mesh = mesh;
        _instance = RenderingServer.InstanceCreate();
        RenderingServer.InstanceSetScenario(_instance, world.Scenario);
        RenderingServer.InstanceSetBase(_instance, mesh.GetRid());
        RenderingServer.InstanceSetTransform(_instance, transform);
    }

    public void SetTransform(Transform3D transform)
    {
        if (_instance.IsValid)
        {
            RenderingServer.InstanceSetTransform(_instance, transform);
        }
    }

    public void SetVisible(bool visible)
    {
        if (_instance.IsValid)
        {
            RenderingServer.InstanceSetVisible(_instance, visible);
        }
    }

    /// <summary>
    /// RenderingServer RIDs are not garbage collected — every InstanceCreate needs a
    /// matching FreeRid or the instance leaks for the lifetime of the process.
    /// </summary>
    public void Dispose()
    {
        if (_instance.IsValid)
        {
            RenderingServer.FreeRid(_instance);
            _instance = default;
        }
    }
}
