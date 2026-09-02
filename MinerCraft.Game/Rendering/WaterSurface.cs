using System.Collections.Generic;
using Godot;
using MinerCraft.Core.Hydrology;
using MinerCraft.Core.Voxels;

namespace MinerCraft.Game.Rendering;

/// <summary>
/// Draws the standing water: one flat surface per basin that holds anything, at the level the
/// accumulator solved.
/// </summary>
/// <remarks>
/// <para>
/// <b>Solved, not simulated, and this class is where that stops being a slogan.</b> Nothing here
/// iterates water or steps a fluid — it asks <see cref="WaterSystem.LevelAt"/> for a number per
/// basin and emits a horizontal quad wherever the terrain sample is below it. DESIGN's Tier A is
/// exactly that claim, and the whole renderer is a sweep over the analysis grid.
/// </para>
/// <para>
/// <b>The shoreline stair-steps on the 4 m analysis grid against 0.25 m terrain, and that is
/// accepted for M7 rather than overlooked.</b> It is the most-looked-at part of a pond, and M8's
/// Tier B is what replaces it — refining shore cells now would be building the thing M8 deletes.
/// <b>M7d-2's play is what says whether it is tolerable until then.</b>
/// </para>
/// <para>
/// <b>Rebuilt on a timer rather than every frame, because a level rises continuously and a mesh
/// does not have to.</b> At a few hundred quads a rebuild is comparable with one chunk remesh, and
/// four a second is far below what the eye can resolve on water rising a centimetre a minute.
/// </para>
/// </remarks>
public sealed class WaterSurface
{
    /// <summary>How often the surface is rebuilt, at most.</summary>
    /// <remarks>
    /// <b>A quarter of a second, and the number is chosen against how fast water can move rather
    /// than against a frame budget.</b> At the shipped rainfall scale a basin fills in about ten
    /// minutes, so a level moves well under a millimetre in this interval — far below a pixel at
    /// any distance you can see the pond from. Rebuilding every frame would be the same picture
    /// sixty times.
    /// </remarks>
    private const double RebuildIntervalSeconds = 0.25;

    private readonly ShaderMaterial _material;
    private readonly Rid _instance;
    private readonly World3D _world;

    private ArrayMesh? _mesh;
    private double _sinceRebuild = RebuildIntervalSeconds;

    public WaterSurface(World3D world)
    {
        System.ArgumentNullException.ThrowIfNull(world);

        _world = world;

        var shader = GD.Load<Shader>("res://Rendering/Water.gdshader");
        if (shader is null)
        {
            GD.PushError("MinerCraft: could not load res://Rendering/Water.gdshader");
        }

        _material = new ShaderMaterial { Shader = shader };

        _instance = RenderingServer.InstanceCreate();
        RenderingServer.InstanceSetScenario(_instance, world.Scenario);
    }

    /// <summary>Quads in the surface as it stands. Zero on a dry world.</summary>
    public int QuadCount { get; private set; }

    /// <summary>Basins with a surface drawn.</summary>
    public int WetBasins { get; private set; }

    /// <summary>Milliseconds the last rebuild took on the sim thread.</summary>
    public double LastRebuildMillis { get; private set; }

    public int Rebuilds { get; private set; }

    /// <summary>Dims the water with the sky, so a pond is not brighter than the ground beside it.</summary>
    public void SetBrightness(float brightness) =>
        _material.SetShaderParameter("sky_brightness", brightness);

    /// <summary>Rebuilds the surface if it is due. Cheap and does nothing most frames.</summary>
    public void Update(double delta, WaterSystem water)
    {
        System.ArgumentNullException.ThrowIfNull(water);

        _sinceRebuild += delta;
        if (_sinceRebuild < RebuildIntervalSeconds || water.Window is null)
        {
            return;
        }

        _sinceRebuild = 0.0;
        Rebuild(water);
    }

    /// <summary>
    /// Sweeps the analysis grid and emits a horizontal quad for every sample under water.
    /// </summary>
    /// <remarks>
    /// <b>Runs merged along X, which is the greedy mesher's argument at a coarser grid.</b> A pond
    /// is a run of consecutive wet samples in a row far more often than not, so merging them costs
    /// one comparison per sample and typically removes most of the quads. It merges on
    /// <em>(basin, level)</em> rather than on wetness alone: two neighbouring basins at different
    /// levels are two lakes, and a quad spanning both would be a surface that is at neither.
    /// </remarks>
    private void Rebuild(WaterSystem water)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();

        Watershed watershed = water.Window!.Watershed;
        HeightField field = watershed.Field;
        int pitch = field.Pitch;

        // Solved once per basin rather than once per sample: LevelAt walks the merge tree to find
        // whichever lake this basin is part of, and a pond is hundreds of samples.
        int basinCount = watershed.Basins.Count;
        var levels = new double[basinCount];
        WetBasins = 0;
        for (int basin = 0; basin < basinCount; basin++)
        {
            double? level = water.LevelAt(basin);
            levels[basin] = level ?? double.NegativeInfinity;
            if (level is not null) WetBasins++;
        }

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var indices = new List<int>();

        float cellMetres = field.CellVoxels * VoxelConstants.VoxelSize;

        for (int sz = 0; sz < pitch; sz++)
        {
            int sx = 0;
            while (sx < pitch)
            {
                if (!IsWet(watershed, field, levels, sx, sz, out int label, out double level))
                {
                    sx++;
                    continue;
                }

                int run = 1;
                while (sx + run < pitch
                       && IsWet(watershed, field, levels, sx + run, sz, out int next, out _)
                       && next == label)
                {
                    run++;
                }

                float x0 = field.WorldXOf(sx) * VoxelConstants.VoxelSize;
                float z0 = field.WorldZOf(sz) * VoxelConstants.VoxelSize;
                float x1 = x0 + (run * cellMetres);
                float z1 = z0 + cellMetres;
                float y = (float)(level * VoxelConstants.VoxelSize);

                AddQuad(vertices, normals, indices, x0, x1, z0, z1, y);
                sx += run;
            }
        }

        Install(vertices, normals, indices);

        QuadCount = indices.Count / 6;
        Rebuilds++;
        LastRebuildMillis =
            (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0
            / System.Diagnostics.Stopwatch.Frequency;
    }

    /// <summary>Whether this sample's terrain is under its basin's solved level.</summary>
    /// <remarks>
    /// <b>Strictly under, so a basin whose water exactly reaches a sample's height draws no quad
    /// there.</b> Both sides are the same quantity — the level is in voxel Y and so is the sampled
    /// height — and a surface flush with the ground is a surface nobody can see, drawn at exactly
    /// the depth where it would z-fight with the terrain it sits on.
    /// </remarks>
    private static bool IsWet(
        Watershed watershed,
        HeightField field,
        double[] levels,
        int sx,
        int sz,
        out int label,
        out double level)
    {
        label = watershed.LabelAt(sx, sz);
        level = 0.0;

        if (label < 0 || label >= levels.Length)
        {
            return false;
        }

        level = levels[label];
        return level > field.HeightAt(sx, sz);
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

        // Wound so the visible face points up. The material is cull_disabled anyway - you can be
        // under the surface, standing in a flooded pit - but a correct winding is what makes the
        // shader's own normal usable rather than something to take the absolute value of.
        indices.Add(first);
        indices.Add(first + 2);
        indices.Add(first + 1);
        indices.Add(first);
        indices.Add(first + 3);
        indices.Add(first + 2);
    }

    private void Install(List<Vector3> vertices, List<Vector3> normals, List<int> indices)
    {
        // The old mesh is freed rather than left to the GC, which M2d-1 found the expensive way:
        // ArrayMesh holds a RID and nothing releases it on collection. Four rebuilds a second makes
        // that a leak with a rate rather than a leak.
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
