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

    /// <param name="quadCount">
    /// Quads in the mesh behind this instance, carried so the profile can report drawn
    /// geometry without walking Godot's surface arrays back out. The AO A/B is a comparison
    /// of this number, summed.
    /// </param>
    /// <param name="vertexCount">Vertices in the same mesh. Four per quad, by construction.</param>
    public VoxelRenderInstance(
        World3D world,
        ArrayMesh mesh,
        Transform3D transform,
        int quadCount = 0,
        int vertexCount = 0)
    {
        _mesh = mesh;
        QuadCount = quadCount;
        VertexCount = vertexCount;
        _instance = RenderingServer.InstanceCreate();
        RenderingServer.InstanceSetScenario(_instance, world.Scenario);
        RenderingServer.InstanceSetBase(_instance, mesh.GetRid());
        RenderingServer.InstanceSetTransform(_instance, transform);
    }

    /// <summary>Quads in this instance's mesh.</summary>
    public int QuadCount { get; }

    /// <summary>Vertices in this instance's mesh.</summary>
    public int VertexCount { get; }

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
    /// Releases both the instance and the mesh behind it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// RenderingServer RIDs are not garbage collected — every <c>InstanceCreate</c> needs a
    /// matching <c>FreeRid</c> or the instance leaks for the lifetime of the process.
    /// </para>
    /// <para>
    /// <b>The mesh is disposed explicitly, and that is the half that actually costs.</b>
    /// Dropping the last reference to the <see cref="ArrayMesh"/> and letting the finaliser
    /// free its RID works, but hands the timing to the GC: the vertex buffers — which is
    /// what Godot is slow to deallocate — then come back in unpredictable bursts that no
    /// per-frame budget can shape. Disposing here makes the expensive part happen at a
    /// moment we choose, which is the whole point of <see cref="MeshFreeQueue"/>.
    /// </para>
    /// <para>
    /// Order matters: the instance's base is the mesh's RID, so the instance goes first.
    /// </para>
    /// </remarks>
    public void Dispose()
    {
        if (_instance.IsValid)
        {
            RenderingServer.FreeRid(_instance);
            _instance = default;
        }

        _mesh.Dispose();
    }
}
