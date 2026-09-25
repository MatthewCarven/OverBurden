using System.Collections.Generic;
using Godot;
using Overburden.Core.Hydrology;
using Overburden.Core.Voxels;

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

    /// <summary>
    /// The water grids and the raise map (docs/POND-PLAN.md, decisions 9 and 10): a sample the
    /// player has built in whose chunk column has a grid is drawn per voxel column from the grid —
    /// the water meets the wall inside and stops at it outside — and the shore ring never spreads
    /// into it. Both null is the pre-2026-09-25 arm. A raised sample with no grid (its chunk column
    /// not loaded) is drawn as any other sample.
    /// </summary>
    public Overburden.Core.Fluid.WaterGrids? Grids { get; set; }

    /// <inheritdoc cref="Grids"/>
    public RaisedSurface? Raised { get; set; }

    /// <summary>Samples drawn from a water grid on the last rebuild.</summary>
    public int GridSamples { get; private set; }

    /// <summary>Quads the grid samples drew on the last rebuild.</summary>
    public int GridQuads { get; private set; }

    /// <summary>Wet or ring samples left undrawn on the last rebuild for being enclosed.</summary>
    public int EnclosedSamples { get; private set; }

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

        // One label and level per sample: the sample's own if it is wet, else - with the shore
        // dilated - the highest-standing wet neighbour's, else none.
        int samples = pitch * pitch;
        var drawLabel = new int[samples];
        var drawLevel = new double[samples];
        EnclosedSamples = 0;
        bool[]? gridCell = GridCells(field, samples);
        for (int sz = 0; sz < pitch; sz++)
        {
            for (int sx = 0; sx < pitch; sx++)
            {
                int i = (sz * pitch) + sx;
                bool wet = IsWet(watershed, field, levels, sx, sz, out int label, out double level);
                if (gridCell is not null && gridCell[i])
                {
                    // Drawn from its grid below, per voxel column.
                    wet = false;
                }
                else if (wet && Enclosure is not null && Enclosure.IsEnclosed(field.WorldXOf(sx), field.WorldZOf(sz), (int)System.Math.Floor(level)))
                {
                    // Walled in at the lake's level: not the lake's, whatever the level says.
                    wet = false;
                    EnclosedSamples++;
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
                            if (wetLabel[n] != Watershed.Outlet || (gridCell is not null && gridCell[n]))
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

        GridQuads = 0;
        if (gridCell is not null)
        {
            int before = indices.Count;
            DrawGridCells(water, field, gridCell, vertices, normals, indices);
            GridQuads = (indices.Count - before) / 6;
        }

        Install(vertices, normals, indices);

        QuadCount = indices.Count / 6;
        Rebuilds++;
        LastRebuildMillis =
            (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0
            / System.Diagnostics.Stopwatch.Frequency;
    }

    /// <summary>Marks the window's raised samples whose chunk column has a grid, or returns null when there are none.</summary>
    private bool[]? GridCells(HeightField field, int samples)
    {
        GridSamples = 0;
        if (Grids is null || Raised is null)
        {
            return null;
        }

        bool[]? cells = null;
        var asked = new Dictionary<(int X, int Z), bool>();
        int cell = field.CellVoxels;
        int pitch = field.Pitch;
        foreach (KeyValuePair<(int X, int Z), int> raised in Raised.Samples)
        {
            int sx = ((raised.Key.X * cell) - field.OriginX) / cell;
            int sz = ((raised.Key.Z * cell) - field.OriginZ) / cell;
            if (sx < 0 || sz < 0 || sx >= pitch || sz >= pitch)
            {
                continue;
            }

            var chunk = ((raised.Key.X * cell) >> VoxelConstants.ChunkSizeLog2, (raised.Key.Z * cell) >> VoxelConstants.ChunkSizeLog2);
            if (!asked.TryGetValue(chunk, out bool hasGrid))
            {
                hasGrid = Grids.Get(chunk.Item1, chunk.Item2) is not null;
                asked[chunk] = hasGrid;
            }

            if (hasGrid)
            {
                cells ??= new bool[samples];
                cells[(sz * pitch) + sx] = true;
                GridSamples++;
            }
        }

        return cells;
    }

    /// <summary>
    /// Draws every grid sample per voxel column: a run along X of columns sharing a source sample,
    /// at that source's live level, so a lake rising inside a voxel meets the wall at the height
    /// it stands at everywhere else.
    /// </summary>
    private void DrawGridCells(
        WaterSystem water,
        HeightField field,
        bool[] gridCell,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<int> indices)
    {
        int cell = field.CellVoxels;
        int pitch = field.Pitch;
        for (int sz = 0; sz < pitch; sz++)
        {
            for (int sx = 0; sx < pitch; sx++)
            {
                if (!gridCell[(sz * pitch) + sx])
                {
                    continue;
                }

                int worldX0 = field.WorldXOf(sx);
                int worldZ0 = field.WorldZOf(sz);
                int chunkX = worldX0 >> VoxelConstants.ChunkSizeLog2;
                int chunkZ = worldZ0 >> VoxelConstants.ChunkSizeLog2;
                Overburden.Core.Fluid.WaterColumns? grid = Grids!.Get(chunkX, chunkZ);
                if (grid is null)
                {
                    continue;
                }

                int lx0 = worldX0 - (chunkX << VoxelConstants.ChunkSizeLog2);
                int lz0 = worldZ0 - (chunkZ << VoxelConstants.ChunkSizeLog2);
                for (int dz = 0; dz < cell; dz++)
                {
                    int dx = 0;
                    while (dx < cell)
                    {
                        if (!grid.TryGetSource(lx0 + dx, lz0 + dz, out int srcX, out int srcZ)
                            || water.TableAt(srcX, srcZ) is not { } level)
                        {
                            dx++;
                            continue;
                        }

                        int run = 1;
                        while (dx + run < cell
                               && grid.TryGetSource(lx0 + dx + run, lz0 + dz, out int nextX, out int nextZ)
                               && nextX == srcX && nextZ == srcZ)
                        {
                            run++;
                        }

                        float x0 = (worldX0 + dx) * VoxelConstants.VoxelSize;
                        float z0 = (worldZ0 + dz) * VoxelConstants.VoxelSize;
                        float y = (float)(level * VoxelConstants.VoxelSize) - SurfaceBiasMetres;
                        AddQuad(vertices, normals, indices, x0, x0 + (run * VoxelConstants.VoxelSize), z0, z0 + VoxelConstants.VoxelSize, y);
                        dx += run;
                    }
                }
            }
        }
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
