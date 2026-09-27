using System.Collections.Generic;
using Godot;
using Overburden.Core.Hydrology;
using Overburden.Core.Voxels;
using Overburden.Core.Diagnostics;

namespace Overburden.Game.Rendering;

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
/// <b>The shoreline is clipped by the terrain's own depth, not by the analysis grid — Matthew's
/// rule, 2026-09-10.</b> The surface is depth-tested against the terrain, so inside a wet cell the
/// shore was always exact to the voxel: the quad runs under the whole footprint and any ground
/// standing above the level covers it. What stair-stepped was the <em>missing</em> quads — a cell
/// whose corner sample sits just above the level draws nothing, though half its footprint dips
/// under. So a dry cell with a wet neighbour draws that neighbour's plane too
/// (<see cref="DilateShore"/>), and the ground does the clipping for free: water shows exactly
/// where the terrain is under the level, at 0.25 m, at any lattice, with no height read beyond
/// the one the sweep already makes. The cost is the ring's quads, mostly hidden. The error it
/// admits is the one a wet cell already has — a dip behind a 0.25 m ridge inside the footprint
/// shows water — one cell further out and bounded there; <b>and only into the same lake</b>,
/// because across a divide the far slope can be under the level and the first screenshot hung
/// a slab over a saddle. A plane over the whole catchment would have been simpler still and
/// wrong in every excavation below the lake's level in it, because the terrain that clips is
/// the dug terrain and the level is the seed's. M7d-2's play called the 4 m step tolerable
/// before this; the grid is 2 m since the same day for the curve's sake.
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

    /// <summary>How far under the solved level the plane is drawn, in metres.</summary>
    /// <remarks>
    /// <b>One centimetre, Matthew's number (2026-09-10), so a plane flush with a voxel's top face
    /// loses to it.</b> The shore dilation draws the plane over cells whose ground sits exactly at
    /// the level, and two coplanar faces z-fight — the water won in ragged patches across a
    /// brim-full pond. A voxel of depth is 25 cm, so nothing a litre deep is hidden by this; only
    /// the flush face is. A look constant, not a physics one: the level Tier A holds and hands
    /// across the seam is untouched.
    /// </remarks>
    private const float SurfaceBiasMetres = 0.01f;

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
            GD.PushError("Overburden: could not load res://Rendering/Water.gdshader");
        }

        _material = new ShaderMaterial { Shader = shader };

        _instance = RenderingServer.InstanceCreate();
        RenderingServer.InstanceSetScenario(_instance, world.Scenario);
    }

    /// <summary>Quads in the surface as it stands. Zero on a dry world.</summary>
    public int QuadCount { get; private set; }

    /// <summary>Basins with a surface drawn.</summary>
    public int WetBasins { get; private set; }

    /// <summary>
    /// Whether a dry sample beside a wet one draws the wet one's plane, so the terrain clips the
    /// shore to the voxel. Off is the pre-2026-09-10 arm, <c>--shore=0</c>.
    /// </summary>
    public bool DilateShore { get; set; } = true;

    /// <summary>Dry samples that drew a neighbour's plane on the last rebuild.</summary>
    public int RingSamples { get; private set; }

    /// <summary>Milliseconds the last rebuild took on the sim thread.</summary>
    public double LastRebuildMillis { get; private set; }

    public int Rebuilds { get; private set; }

    /// <summary>Dims the water with the sky, so a pond is not brighter than the ground beside it.</summary>
    /// <summary>
    /// The courtyard (2026-09-20): a sample whose column the player's walls close around at the
    /// lake's level draws no plane, and the ring never dilates into one. Null draws every wet
    /// sample, the pre-2026-09-20 arm.
    /// </summary>
    public Overburden.Core.Fluid.Enclosure? Enclosure { get; set; }

    /// <summary>Wet or ring samples left undrawn on the last rebuild for being enclosed.</summary>
    public int EnclosedSamples { get; private set; }

    /// <summary>
    /// The lake over a quarry (2026-09-27): a sample that is mostly a dug pit not yet full to the
    /// seed draws no plane, and the ring never dilates into one — the pit's own water shows at its
    /// true height instead of a sheet over dry floor. Null draws the plane over every bite, the
    /// pre-2026-09-27 arm, <c>--lakeoverbite=1</c>.
    /// </summary>
    public Overburden.Core.Fluid.BiteFill? BiteFill { get; set; }

    /// <summary>Wet or ring samples left undrawn on the last rebuild for standing over an unfilled bite.</summary>
    public int UnfilledBiteSamples { get; private set; }

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

    private static readonly int WatchRebuild = CallWatch.Register("Tier A surface: rebuild");

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
        using CallWatch.Scope callWatch = CallWatch.Time(WatchRebuild);
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

        // One label and level per sample: the sample's own if it is wet, else - with the shore
        // dilated - the highest-standing wet neighbour's, else none.
        int samples = pitch * pitch;
        var drawLabel = new int[samples];
        var drawLevel = new double[samples];
        EnclosedSamples = 0;
        UnfilledBiteSamples = 0;
        int cell = field.CellVoxels;
        for (int sz = 0; sz < pitch; sz++)
        {
            for (int sx = 0; sx < pitch; sx++)
            {
                int i = (sz * pitch) + sx;
                bool wet = IsWet(watershed, field, levels, sx, sz, out int label, out double level);
                if (wet && Enclosure is not null && Enclosure.IsEnclosed(field.WorldXOf(sx), field.WorldZOf(sz), (int)System.Math.Floor(level)))
                {
                    // Walled in at the lake's level: not the lake's, whatever the level says.
                    wet = false;
                    EnclosedSamples++;
                }

                if (wet && BiteFill is not null && BiteFill.IsUnfilled(field.WorldXOf(sx) / cell, field.WorldZOf(sz) / cell))
                {
                    // Over a pit the lake has not filled yet: the plane assumes a full bite.
                    wet = false;
                    UnfilledBiteSamples++;
                }

                drawLabel[i] = wet ? label : Watershed.Outlet;
                drawLevel[i] = level;
            }
        }

        RingSamples = 0;
        if (DilateShore)
        {
            // Outward from the wet cells rather than inward from the dry ones: the window is
            // 65,536 samples and the wet ones a few thousand, and the first version's scan of
            // every dry cell's eight neighbours cost 4 ms Debug on the sim thread four times a
            // second. Cloned so a ring cell set here does not seed a second ring.
            var wetLabel = (int[])drawLabel.Clone();
            var wetLevel = (double[])drawLevel.Clone();
            for (int sz = 0; sz < pitch; sz++)
            {
                for (int sx = 0; sx < pitch; sx++)
                {
                    int i = (sz * pitch) + sx;
                    if (wetLabel[i] == Watershed.Outlet)
                    {
                        continue;
                    }

                    double level = wetLevel[i];

                    // Eight neighbours, because the sample is the cell's corner and a diagonal
                    // neighbour's footprint is reached as readily as a side's.
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        int nz = sz + dz;
                        if (nz < 0 || nz >= pitch) continue;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = sx + dx;
                            if (nx < 0 || nx >= pitch || (dx == 0 && dz == 0)) continue;
                            int n = (nz * pitch) + nx;
                            if (wetLabel[n] != Watershed.Outlet)
                            {
                                continue;
                            }

                            // Only into a cell of the same lake. A dry cell whose own basin
                            // stands at this level is shore: its sample is above the water and
                            // its footprint's low side is under it. A dry cell of another basin
                            // is across a divide, and its footprint's far side can be below the
                            // level on the *other* slope - the first screenshot had a lake at
                            // 108% of its capacity hanging a slab over the saddle. Same lake
                            // rather than same basin, because a lake merges basins and its level
                            // is one number across them. Two lakes cannot both claim a cell under
                            // this rule, so the first to reach it keeps it.
                            int own = watershed.LabelAt(nx, nz);
                            if (own < 0 || own >= levels.Length || levels[own] != level)
                            {
                                continue;
                            }

                            // Never into a walled-in column: the shore stops at the wall.
                            if (Enclosure is not null && Enclosure.IsEnclosed(field.WorldXOf(nx), field.WorldZOf(nz), (int)System.Math.Floor(level)))
                            {
                                EnclosedSamples++;
                                continue;
                            }

                            // Nor over an unfilled bite: the shore stops at the pit's rim.
                            if (BiteFill is not null && BiteFill.IsUnfilled(field.WorldXOf(nx) / cell, field.WorldZOf(nz) / cell))
                            {
                                UnfilledBiteSamples++;
                                continue;
                            }

                            if (drawLabel[n] == Watershed.Outlet)
                            {
                                drawLabel[n] = wetLabel[i];
                                drawLevel[n] = level;
                                RingSamples++;
                            }
                        }
                    }
                }
            }
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
                int i = (sz * pitch) + sx;
                int label = drawLabel[i];
                if (label == Watershed.Outlet)
                {
                    sx++;
                    continue;
                }

                double level = drawLevel[i];
                int run = 1;
                while (sx + run < pitch
                       && drawLabel[i + run] == label
                       && drawLevel[i + run] == level)
                {
                    run++;
                }

                float x0 = field.WorldXOf(sx) * VoxelConstants.VoxelSize;
                float z0 = field.WorldZOf(sz) * VoxelConstants.VoxelSize;
                float x1 = x0 + (run * cellMetres);
                float z1 = z0 + cellMetres;
                float y = (float)(level * VoxelConstants.VoxelSize) - SurfaceBiasMetres;

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
    /// height, which since M8b-2a is the plane the ground's top face sits on rather than the solid
    /// voxel's index — and a surface flush with the ground is a surface nobody can see, drawn at
    /// exactly the depth where it would z-fight with the terrain it sits on. Before M8b-2a a basin
    /// had to hold a whole slab per sample before this test passed anywhere.
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

        // Disposed when the surface has its copy: a Godot array holds its packed arrays natively
        // until it is disposed or finalised, and the finaliser is the GC's business — the Tier B
        // surface was 40 MiB a rebuild four times a second on 2026-09-21 when the process ran out
        // of memory inside AddSurfaceFromArrays.
        using var arrays = new Godot.Collections.Array();
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
