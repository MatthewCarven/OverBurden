using System.Globalization;
using Overburden.Core.Fluid;
using Overburden.Core.Hydrology;
using Overburden.Core.Lighting;
using Overburden.Core.Voxels;

namespace Overburden.Bench;

/// <summary>
/// M8c's water-bodies measurement, taken where nothing else is awake: a room under a lake fed
/// by seepage alone, both delivery arms, the awake count tick by tick.
/// </summary>
/// <remarks>
/// <para>
/// The game's <c>awake at seeping boundaries</c> counts every awake cell in the world at a
/// boundary where something seeped — on the played world that is the loaded mine water, a portal
/// flood and whatever a fill displaced, and it reads the same with seepage off. This is the
/// number the plan asked for: what a drip does to the automaton, and whether it goes quiet
/// between deliveries. Two shapes and two arms — the test suite's 8 × 4 × 8 room and a 40 × 12 ×
/// 40 one, spring and sweat — at the shipped scale ×10 (the arm Matthew played) and at ×1.
/// </para>
/// <para>
/// Debug-safe, because the number is a count and not a time.
/// </para>
/// </remarks>
internal static class SeepAwakeBench
{
    private const int Floor = 30;
    private const int Rim = 40;

    private sealed class Bowl(int half) : ISurfaceHeights
    {
        public int SurfaceHeightAt(int worldX, int worldZ) =>
            worldX >= 64 - half && worldX < 64 + half && worldZ >= 64 - half && worldZ < 64 + half ? Floor : Rim;
    }

    private sealed class DugTerrain(ISurfaceHeights seed) : ISeepTerrain
    {
        public HashSet<VoxelPos> Dug { get; } = [];

        public HashSet<ChunkPos> Loaded { get; } = [];

        public bool CanHold(VoxelPos v) =>
            Loaded.Contains(v.Chunk) && (Dug.Contains(v) || v.Y > seed.SurfaceHeightAt(v.X, v.Z));

        public bool TryGetRock(VoxelPos v, out float permeability)
        {
            if (!Loaded.Contains(v.Chunk) || CanHold(v))
            {
                permeability = 0f;
                return false;
            }

            permeability = v.Y >= seed.SurfaceHeightAt(v.X, v.Z) - 1 ? 0.35f : 0.02f;
            return true;
        }
    }

    internal static int Run(int ticks)
    {
        Console.WriteLine("Seep awake bench — what a drip does to the automaton, with nothing else awake");
        Console.WriteLine();
        Console.WriteLine($"  A bowl 32 m across, floor plane 31, 2,000,000 L poured; a room dug wholly under its bed; {ticks} ticks");
        Console.WriteLine("  at 2 Hz with the settle at every boundary (--settleafter=2, share 8), the hand-off reading nothing (no seed ground in a room).");
        Console.WriteLine("  awake = FluidSimulation.ActiveCellCount after the tick; 'quiet' ticks had nothing awake at all.");
        Console.WriteLine();
        Console.WriteLine("  room (voxels)   scale   mode     seeped L   awake mean / p50 / max   quiet ticks   full at tick   wet cells at end");
        Console.WriteLine("  -------------   -----   ------   --------   ----------------------   -----------   ------------   ----------------");

        foreach ((int wide, int high) in new[] { (8, 4), (40, 12) })
        {
            foreach (double scale in new[] { SeepIndex.DefaultSeepageScale, SeepIndex.DefaultSeepageScale * 10 })
            {
                foreach (SeepMode mode in new[] { SeepMode.Spring, SeepMode.Sweat })
                {
                    Console.WriteLine(Arm(wide, high, scale, mode, ticks));
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine("  'full at tick' is when the drain first skipped the chunk for want of room; '—' means it never filled inside the run.");
        return 0;
    }

    private static string Arm(int wide, int high, double scale, SeepMode mode, int ticks)
    {
        const int half = 64;
        var seed = new Bowl(half);
        var terrain = new DugTerrain(seed);
        var water = new WaterSystem(seed, 0x5EEDUL, new BasinLedger(), 256 / HeightField.DefaultCellVoxels);
        water.Install(water.AnalyseFor(64, 64));
        var fluid = new FluidSimulation(terrain);
        var portals = new PortalIndex(seed, terrain);
        var seeps = new SeepIndex(seed, terrain);
        var tracker = new PortalTracker(portals, seeps);
        var drain = new SeepDrain(seeps, water, fluid, scale, mode: mode);
        var settle = new SheetSettle(fluid, SheetSettle.DefaultSettleAfterTicks, SheetSettle.DefaultSettleShare);

        for (int cz = 0; cz <= 4; cz++)
        {
            for (int cx = 0; cx <= 4; cx++)
            {
                for (int cy = 0; cy <= 1; cy++)
                {
                    var chunk = new ChunkPos(cx, cy, cz);
                    terrain.Loaded.Add(chunk);
                    tracker.OnVoxelsReady(chunk, hasEdits: false);
                }
            }
        }

        // The room: its roof six voxels under the bed at Y 30, centred under the bowl.
        int roofY = Floor - 7;
        int floorY = roofY - high + 1;
        int x0 = 64 - (wide / 2);
        for (int y = floorY; y <= roofY; y++)
        {
            for (int z = x0; z < x0 + wide; z++)
            {
                for (int x = x0; x < x0 + wide; x++)
                {
                    var v = new VoxelPos(x, y, z);
                    terrain.Dug.Add(v);
                    tracker.OnVoxelChanged(v);
                }
            }
        }

        tracker.Flush();
        int basin = water.BasinAtSample(64 / HeightField.DefaultCellVoxels, 64 / HeightField.DefaultCellVoxels);
        water.Inject(basin, 2_000_000);

        var awake = new List<int>(ticks);
        int quiet = 0;
        int fullAt = -1;
        for (int t = 0; t < ticks; t++)
        {
            settle.AtBoundary();
            tracker.Flush();
            drain.Draw();
            if (fullAt < 0 && drain.FullChunks > 0)
            {
                fullAt = t;
            }

            fluid.Step();
            int active = fluid.ActiveCellCount;
            awake.Add(active);
            if (active == 0)
            {
                quiet++;
            }
        }

        awake.Sort();
        double mean = awake.Average();
        int p50 = awake[awake.Count / 2];
        int max = awake[^1];
        int wet = fluid.Grid.HeldCells().Count();

        return string.Format(
            CultureInfo.InvariantCulture,
            "  {0,-15} {1,5:F2}   {2,-6}   {3,8:N0}   {4,7:F0} / {5,5:N0} / {6,6:N0}   {7,11:N0}   {8,12}   {9,16:N0}",
            $"{wide}x{high}x{wide}",
            scale,
            mode.ToString().ToLowerInvariant(),
            water.SeepedLitres,
            mean,
            p50,
            max,
            quiet,
            fullAt < 0 ? "—" : fullAt.ToString(CultureInfo.InvariantCulture),
            wet);
    }
}
