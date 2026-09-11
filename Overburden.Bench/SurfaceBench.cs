using System.Diagnostics;
using System.Globalization;
using Overburden.Core.Fluid;
using Overburden.Core.Voxels;
using Overburden.Core.Worldgen;

namespace Overburden.Bench;

/// <summary>
/// M8d-1's arm: the Tier B mesh against the debug surface it replaces, on the played lake's shape
/// and on a flood, priced as quads, bytes and milliseconds of worker time.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two shapes, because the merge's cost has a sign that depends on the shape.</b> A lake at
/// rest is the case the merge is for: one level, one depth, one history, so a panel's floor is
/// one quad. A flood in progress is the case it is not: every cell at its own fill, sides on every
/// step of the front, a history that differs cell by cell. The plan's split named both
/// (<c>--lake</c>, <c>--front</c>), and this prints both beside the debug view's one quad per
/// surface cell, which is what every recorded install figure was taken against.
/// </para>
/// <para>
/// <b>Each shape is built twice</b>, because the first build has no previous surface and pays
/// nothing for the lookup, and the second pays it for every surface cell — the steady state a
/// play runs in. The debug arm is the walk plus a quad per cell, timed the way the game timed it.
/// </para>
/// </remarks>
internal static class SurfaceBench
{
    internal static int Run(WorldGenerator generator, string machineControl, bool isDebug, int lakeChambers, long floodLitres)
    {
        Console.WriteLine("Surface bench — M8d-1's mesh against the debug surface, on a lake at rest and a flood");
        if (isDebug)
        {
            Console.WriteLine("*** DEBUG BUILD — these timings are meaningless. Re-run with -c Release. ***");
        }

        Console.WriteLine();
        Console.WriteLine(machineControl);
        Console.WriteLine();
        Console.WriteLine(
            "  'debug' is the walk plus one quad per surface cell, as the game drew until M8d-1; 'mesh' is "
            + "FluidSurfaceMesher over the same grid, first build (no previous surface) and second (the steady "
            + "state). Bytes are the arrays handed to the install. Debug quads equal surface cells by construction.");
        Console.WriteLine();
        Console.WriteLine(
            "  shape                          surface     debug     debug   |   tops     sides   bottoms   quads     vertices   KiB      asks     first    second");
        Console.WriteLine(
            "                                 cells       quads     ms      |                                                             ms       ms");
        Console.WriteLine(
            "  ----------------------------   ---------   -------   -----   |   -------   -----   -------   -------   --------   ------   ------   ------   ------");

        // Warm by call count on a small lake so the first timed build is not tier-0 code.
        {
            var warm = new ChamberTerrain(generator, 0, 0, shaftVoxels: 1, depthMetres: 2, chambers: 2);
            var sim = new FluidSimulation(warm);
            FillFloor(sim, warm, 8_000);
            var mesher = new FluidSurfaceMesher();
            for (int i = 0; i < 20; i++)
            {
                mesher.Build(sim.Grid, warm);
                foreach (var _ in sim.Grid.SurfaceCells()) { }
            }
        }

        // The lake at rest: the played lake's shape (--lake's fixture).
        {
            var terrain = new ChamberTerrain(generator, 0, 0, shaftVoxels: 1, depthMetres: 2, chambers: lakeChambers);
            var sim = new FluidSimulation(terrain);
            FillFloor(sim, terrain, 8_000);
            Row($"lake at rest, {lakeChambers}x{lakeChambers} chambers", sim, terrain);
            Row("  the same lake again", sim, terrain);
        }

        // The same lake, filled a layer and a half deep so the depth attribute carries and the
        // top layer is a step of two levels: the stepped floor the play saw.
        {
            var terrain = new ChamberTerrain(generator, 0, 0, shaftVoxels: 1, depthMetres: 2, chambers: lakeChambers);
            var sim = new FluidSimulation(terrain);
            FillFloor(sim, terrain, FluidConstants.CellCapacityMillilitres);
            int half = terrain.HalfSpan;
            for (int x = -half; x < half; x++)
            {
                for (int z = -half; z < half; z++)
                {
                    sim.Grid.Fill(new VoxelPos(x, terrain.FloorY + 1, z), (x < 0 ? 4_000 : 12_000) + ((x + z) & 1));
                }
            }

            Row("lake, two levels, a step", sim, terrain);
        }

        // The flood: the relay's ball on the chamber block's floor, meshed at checkpoints while it
        // spreads (--front's fixture), and at rest.
        {
            var terrain = new ChamberTerrain(generator, 0, 0, shaftVoxels: 1, depthMetres: 2, chambers: 3);
            int half = terrain.HalfSpan;
            for (int x = -half - 1; x <= half; x++)
            {
                for (int z = -half - 1; z <= half; z++)
                {
                    terrain.CanHold(new VoxelPos(x, terrain.FloorY, z));
                }
            }

            var sim = new FluidSimulation(terrain);
            long ml = floodLitres * FluidConstants.MillilitresPerLitre;
            int radius = SpillBench.RadiusVoxelsFor(ml);
            var at = new VoxelPos(0, terrain.FloorY + 1 + radius, 0);
            long pending = ml;
            var mesher = new FluidSurfaceMesher();
            int[] checkpoints = [2, 5, 20, 100];
            int tick = 0;
            foreach (int checkpoint in checkpoints)
            {
                while (tick < checkpoint)
                {
                    if (pending > 0)
                    {
                        pending = SpillBench.Pour(sim, at, radius, pending);
                    }

                    sim.Step();
                    tick++;

                    // The mesher runs every tick in the game; keep its previous surface honest.
                    mesher.Build(sim.Grid, terrain);
                }

                Row($"flood of {floodLitres:N0} L at tick {tick}", sim, terrain, mesher);
            }

            int cap = 20_000;
            while (sim.ActiveCellCount > 0 && tick < cap)
            {
                sim.Step();
                tick++;
            }

            mesher.Build(sim.Grid, terrain);
            Row($"flood at rest, tick {tick}", sim, terrain, mesher);
        }

        // A roof falling in: the whole 2 x 2 x 2 block open under the lake, the largest tick there
        // is (M8b-1), meshed twenty ticks in — the flood the M8b-4b install figure came from.
        {
            var terrain = new ChamberTerrain(generator, 0, 0, shaftVoxels: 64, depthMetres: 2, chambers: 2);
            var sim = new FluidSimulation(terrain);
            VoxelPos[] mouth = [.. terrain.Mouth()];
            long pending = 9_000_000L * FluidConstants.MillilitresPerLitre;
            var mesher = new FluidSurfaceMesher();
            for (int tick = 0; tick < 20; tick++)
            {
                foreach (VoxelPos cell in mouth)
                {
                    if (pending == 0) break;
                    pending = sim.Add(cell, pending);
                }

                sim.Step();
                mesher.Build(sim.Grid, terrain);
            }

            Row("roof open, 2x2x2 block, tick 20", sim, terrain, mesher);
        }

        Console.WriteLine();
        Console.WriteLine(
            "  'asks' is the terrain questions the mesher put, all about dry neighbours of wet cells. On the "
            + "job arm the mesh is built on the worker and the game pays only the install of 'KiB'.");
        return 0;
    }

    /// <summary>
    /// A level sheet the way the settle leaves one: level to within a millilitre, not to zero.
    /// The jitter is the point — a mesh keyed on raw amounts would split this into its contours.
    /// </summary>
    private static void FillFloor(FluidSimulation sim, ChamberTerrain terrain, int millilitres)
    {
        var random = new Random(8_000);
        int half = terrain.HalfSpan;
        for (int x = -half; x < half; x++)
        {
            for (int z = -half; z < half; z++)
            {
                int jitter = millilitres == FluidConstants.CellCapacityMillilitres ? 0 : random.Next(0, 2);
                sim.Grid.Fill(new VoxelPos(x, terrain.FloorY, z), millilitres + jitter);
            }
        }

        for (int x = -half - 1; x <= half; x++)
        {
            for (int z = -half - 1; z <= half; z++)
            {
                terrain.CanHold(new VoxelPos(x, terrain.FloorY, z));
            }
        }
    }

    private static void Row(string shape, FluidSimulation sim, IFluidTerrain terrain, FluidSurfaceMesher? warmMesher = null)
    {
        var clock = Stopwatch.StartNew();
        int debugQuads = 0;
        foreach (var _ in sim.Grid.SurfaceCells())
        {
            debugQuads++;
        }

        double debugMillis = clock.Elapsed.TotalMilliseconds;

        // First build on a fresh mesher: no previous surface. Second on the same: the steady state.
        // A mesher that has been running every tick already has a previous, so its first build here
        // is a steady-state build too, and both columns then read the same.
        var fresh = new FluidSurfaceMesher();
        clock.Restart();
        FluidSurfaceMesh first = fresh.Build(sim.Grid, terrain);
        double firstMillis = clock.Elapsed.TotalMilliseconds;

        FluidSurfaceMesher steady = warmMesher ?? fresh;
        clock.Restart();
        FluidSurfaceMesh second = steady.Build(sim.Grid, terrain);
        double secondMillis = clock.Elapsed.TotalMilliseconds;

        // The two builds may differ in quads — the second has a history and merges on it — but
        // both cover the walk's cells.
        if (first.SurfaceCells != debugQuads || second.SurfaceCells != debugQuads)
        {
            Console.WriteLine($"  *** {shape}: the mesh disagrees with the walk — {first.SurfaceCells} / {second.SurfaceCells} cells against {debugQuads} ***");
        }

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  {shape,-28}   {first.SurfaceCells,9:N0}   {debugQuads,7:N0}   {debugMillis,5:F2}   |   "
            + $"{second.Tops,7:N0}   {second.Sides,5:N0}   {second.Bottoms,7:N0}   {second.QuadCount,7:N0}   {second.VertexCount,8:N0}   "
            + $"{second.Bytes / 1024.0,6:N0}   {second.TerrainAsks,6:N0}   {firstMillis,6:F2}   {secondMillis,6:F2}"));
    }
}
