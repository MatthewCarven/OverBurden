using System.Runtime.InteropServices;
using Godot;
using Overburden.Core.Fluid;
using Overburden.Core.Voxels;

namespace Overburden.Game.Rendering;

/// <summary>
/// Draws Tier B's moving water: the mesh <see cref="FluidSurfaceMesher"/> builds — merged tops,
/// sides where water meets air, bottoms under falling cells — installed as it comes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Until M8d-1 this was a debug view, one quad on top of every surface cell and no merging</b>,
/// built because M8b's whole question was whether water arrives where you dug and a flood nobody
/// can see cannot be judged. Its install grew to the largest main-thread term (7.8 ms for 20,196
/// quads at slice 3c's play; 144,559 quads in 119.7 ms under a flood at M8b-4b's), which is what
/// the merge is for. The geometry is Core's now; this class owns the instance, the material and
/// the install, and nothing about what the water looks like. It shares
/// <see cref="WaterSurface"/>'s shader until M8d-2 gives it its own.
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

    /// <summary>Surface cells the last build covered with its tops — what the debug view drew one quad each.</summary>
    public int CellsWalked { get; private set; }

    /// <summary>Merged top quads, side runs and bottoms in the last build. Their sum is <see cref="QuadCount"/>.</summary>
    public int Tops { get; private set; }

    public int Sides { get; private set; }

    public int Bottoms { get; private set; }

    /// <summary>Bytes of arrays the last build handed over, which on the job arm is what the job carried back.</summary>
    public long LastMeshBytes { get; private set; }

    /// <summary>
    /// The two halves of a rebuild, split because only one of them can leave the main thread.
    /// </summary>
    /// <remarks>
    /// Since M8d-1 the walk is the mesher — every panel scanned, tops merged, sides run, the
    /// arrays built — and runs on the worker on the job arm; the install is the marshalling and
    /// <c>AddSurfaceFromArrays</c>, and has to happen where the renderer is. The install was the
    /// largest main-thread term at slice 3c's play (7.8 ms for 20,196 quads); this is the number
    /// that says what the merge did to it.
    /// </remarks>
    public double LastWalkMillis { get; private set; }

    public double LastInstallMillis { get; private set; }

    private void Rebuild(FluidSimulation fluid)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        FluidSurfaceMesh mesh = fluid.BuildSurface();
        double walk = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        Rebuild(mesh, walk);
    }

    /// <summary>
    /// Installs a mesh a worker built (slice 3b, M8d-1): the walk and the geometry already
    /// happened on the tick job, so only the install is paid here.
    /// </summary>
    public void InstallFrom(FluidSurfaceMesh mesh, double walkMillisOnWorker)
    {
        System.ArgumentNullException.ThrowIfNull(mesh);
        _sinceRebuild = 0.0;
        Rebuild(mesh, walkMillisOnWorker);
    }

    private void Rebuild(FluidSurfaceMesh mesh, double walkMillis)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();

        CellsWalked = mesh.SurfaceCells;
        Tops = mesh.Tops;
        Sides = mesh.Sides;
        Bottoms = mesh.Bottoms;
        LastMeshBytes = mesh.Bytes;
        LastWalkMillis = walkMillis;

        Install(mesh);

        LastInstallMillis =
            (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0
            / System.Diagnostics.Stopwatch.Frequency;
        QuadCount = mesh.QuadCount;
        Rebuilds++;
        LastRebuildMillis = walkMillis + LastInstallMillis;
    }

    private void Install(FluidSurfaceMesh mesh)
    {
        ArrayMesh? previous = _mesh;
        _mesh = null;

        if (mesh.Indices.Length == 0)
        {
            RenderingServer.InstanceSetBase(_instance, default);
            previous?.Dispose();
            return;
        }

        // The mesher's flat floats are Godot's structs laid end to end — Vector3 is three
        // sequential floats and Vector2 two — so the marshalling is one copy each rather than a
        // loop constructing a struct per vertex. Checked against the mesher's vertex count.
        Vector3[] vertices = MemoryMarshal.Cast<float, Vector3>(mesh.Positions).ToArray();
        Vector3[] normals = MemoryMarshal.Cast<float, Vector3>(mesh.Normals).ToArray();
        Vector2[] attributes = MemoryMarshal.Cast<float, Vector2>(mesh.Attributes).ToArray();
        if (vertices.Length != mesh.VertexCount || attributes.Length != mesh.VertexCount)
        {
            GD.PushError($"Overburden: fluid surface arrays disagree — {vertices.Length} positions, {attributes.Length} attributes, {mesh.VertexCount} vertices");
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        // UV carries (previous height, depth): float32 with no compress flag, as M5a probed for
        // UV2. The shader reads UV.x and UV.y once M8d-2 teaches it to.
        arrays[(int)Mesh.ArrayType.TexUV] = attributes;
        arrays[(int)Mesh.ArrayType.Index] = mesh.Indices;

        var built = new ArrayMesh();
        built.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        built.SurfaceSetMaterial(0, _material);

        _mesh = built;
        RenderingServer.InstanceSetBase(_instance, built.GetRid());
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
