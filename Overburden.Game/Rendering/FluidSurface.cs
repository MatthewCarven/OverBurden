using System.Collections.Generic;
using Godot;
using Overburden.Core.Fluid;
using Overburden.Core.Voxels;

namespace Overburden.Game.Rendering;

/// <summary>
/// Draws Tier B's moving water: one quad on top of every cell that has nothing above it, at the
/// height the cell is filled to.
/// </summary>
/// <remarks>
/// <para>
/// <b>A debug view, and named as one so that nobody polishes it.</b> M8d owns the real transparent
/// pass — side faces, sorting, a surface that reads as one liquid rather than as a field of tiles.
/// This exists because M8b's whole question is whether water arrives where you dug, and a flood
/// nobody can see cannot be judged, screenshotted or reported as broken. It is
/// <see cref="WaterSurface"/>'s shape over held cells instead of basin samples, and it shares the
/// same shader.
/// </para>
/// <para>
/// <b>Top faces only, and no merging.</b> The number of top cells is an area — one per wetted
/// column — so it is bounded the way the active set is, and a rebuild is a scan of the held cells
/// rather than of the world. <see cref="QuadCount"/> and <see cref="LastRebuildMillis"/> are
/// printed so the cost is in the report rather than in somebody's head; if a flooded chamber makes
/// this the dearest thing on the sim thread, that is M8d's argument for doing it properly, not a
/// reason to merge here.
/// </para>
/// <para>
/// <b>Rebuilt on the Tier A surface's timer and only when the simulation has moved</b>, because a
/// tick at 2 Hz changes nothing between ticks and a resting flood changes nothing at all.
/// </para>
/// </remarks>
public sealed class FluidSurface
{
    private const double RebuildIntervalSeconds = 0.25;

    private readonly ShaderMaterial _material;
    private readonly Rid _instance;

    private ArrayMesh? _mesh;
    private double _sinceRebuild = RebuildIntervalSeconds;
    private long _builtAtTick = -1;
    private long _builtAtTotal = -1;

    public FluidSurface(World3D world)
    {
        System.ArgumentNullException.ThrowIfNull(world);

        var shader = GD.Load<Shader>("res://Rendering/Water.gdshader");
        if (shader is null)
        {
            GD.PushError("Overburden: could not load res://Rendering/Water.gdshader");
        }

        _material = new ShaderMaterial { Shader = shader };

        _instance = RenderingServer.InstanceCreate();
        RenderingServer.InstanceSetScenario(_instance, world.Scenario);
    }

    /// <summary>Quads drawn as of the last rebuild. One per top cell.</summary>
    public int QuadCount { get; private set; }

    /// <summary>Milliseconds the last rebuild took on the sim thread.</summary>
    public double LastRebuildMillis { get; private set; }

    public int Rebuilds { get; private set; }

    public void SetBrightness(float brightness) =>
        _material.SetShaderParameter("sky_brightness", brightness);

    /// <summary>Rebuilds if it is due and the water has moved since the last time.</summary>
    public void Update(double delta, FluidSimulation fluid)
    {
        System.ArgumentNullException.ThrowIfNull(fluid);

        _sinceRebuild += delta;
        if (_sinceRebuild < RebuildIntervalSeconds)
        {
            return;
        }

        // Ticks and total together: a tick moves water without changing the total, and the relay
        // changes the total without a tick.
        if (fluid.Ticks == _builtAtTick && fluid.TotalMillilitres == _builtAtTotal)
        {
            return;
        }

        _sinceRebuild = 0.0;
        _builtAtTick = fluid.Ticks;
        _builtAtTotal = fluid.TotalMillilitres;
        Rebuild(fluid);
    }

    private void Rebuild(FluidSimulation fluid)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var indices = new List<int>();

        FluidGrid grid = fluid.Grid;
        const float size = VoxelConstants.VoxelSize;

        foreach (VoxelPos cell in grid.HeldCells())
        {
            if (grid.AmountAt(cell.Offset(0, 1, 0)) > 0)
            {
                continue;
            }

            float fill = grid.AmountAt(cell) / (float)FluidConstants.CellCapacityMillilitres;
            float x0 = cell.X * size;
            float z0 = cell.Z * size;
            float y = (cell.Y + fill) * size;

            AddQuad(vertices, normals, indices, x0, x0 + size, z0, z0 + size, y);
        }

        Install(vertices, normals, indices);

        QuadCount = indices.Count / 6;
        Rebuilds++;
        LastRebuildMillis =
            (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0
            / System.Diagnostics.Stopwatch.Frequency;
    }

    private static void AddQuad(
        List<Vector3> vertices,
        List<Vector3> normals,
        List<int> indices,
        float x0, float x1, float z0, float z1, float y)
    {
        int first = vertices.Count;

        vertices.Add(new Vector3(x0, y, z0));
        vertices.Add(new Vector3(x1, y, z0));
        vertices.Add(new Vector3(x1, y, z1));
        vertices.Add(new Vector3(x0, y, z1));

        for (int i = 0; i < 4; i++)
        {
            normals.Add(Vector3.Up);
        }

        // Same winding as WaterSurface: visible face up, and the material is cull_disabled for
        // the case where you are under it.
        indices.Add(first);
        indices.Add(first + 2);
        indices.Add(first + 1);
        indices.Add(first);
        indices.Add(first + 3);
        indices.Add(first + 2);
    }

    private void Install(List<Vector3> vertices, List<Vector3> normals, List<int> indices)
    {
        ArrayMesh? previous = _mesh;
        _mesh = null;

        if (indices.Count == 0)
        {
            RenderingServer.InstanceSetBase(_instance, default);
            previous?.Dispose();
            return;
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(0, _material);

        _mesh = mesh;
        RenderingServer.InstanceSetBase(_instance, mesh.GetRid());
        RenderingServer.InstanceSetTransform(_instance, Transform3D.Identity);

        previous?.Dispose();
    }

    public void Dispose()
    {
        RenderingServer.FreeRid(_instance);
        _mesh?.Dispose();
        _material.Dispose();
    }
}
