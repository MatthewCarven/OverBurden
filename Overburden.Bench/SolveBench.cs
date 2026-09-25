using System.Globalization;
using Overburden.Core.Fluid;
using Overburden.Core.Voxels;
using Overburden.Core.Worldgen;

namespace Overburden.Bench;

/// <summary>
/// M10d-3, decision 2: the sheet solve's flood against a wide active set — the load test's shape,
/// which <c>--lake</c> (one quiet sheet and a draining edge) does not have. Prices the flood's
/// index, the dictionary it was against the two-layer <see cref="FluidSimulation.HashedFloodIndex"/>
/// replacement, in one process, arms interleaved round by round because this machine has been
/// seen running three times faster for a minute with nothing changed.
/// </summary>
/// <remarks>
/// <para>
/// <b>The fixture</b>: the floor of a chamber block full to the brim (a layer the flood visits and
/// leaves alone), a layer of noisy water over it with one cell in <i>n</i> dry so it breaks into
/// sheets of many sizes (the layer that is levelled), and a noisy layer over that standing on water
/// that is not full — unsupported, so every one of its cells is a seed the flood refuses, the
/// falling water every awake world is full of. Every wet cell is a seed, shuffled, so the sort has
/// work. Each pass re-pours the same noise from the same draw in both arms.
/// </para>
/// <para>
/// <b>The oracle is that both arms land the same water</b>: a checksum over every cell after every
/// pass, and the pass's counts, compared arm against arm. A run whose arms disagree has timed two
/// different solves and says so instead of printing a ratio.
/// </para>
/// </remarks>
internal static class SolveBench
{
    private const int Cap = FluidConstants.CellCapacityMillilitres;

    internal static int Run(
        WorldGenerator generator, string machineControl, bool isDebug, int chambers, int rounds, int holeEvery)
    {
        HoleEvery = holeEvery;
        Console.WriteLine("Solve bench — the sheet flood's index against a wide active set (M10d-3, decision 2)");
        if (isDebug)
        {
            Console.WriteLine("*** DEBUG BUILD — these timings are meaningless. Re-run with -c Release. ***");
        }

        Console.WriteLine();
        Console.WriteLine(machineControl);
        Console.WriteLine();

        // Warm both arms by call count on a small block before anything is timed.
        for (int i = 0; i < 3; i++)
        {
            RunArm(Fixture(generator, 2), hashed: true, passes: 20, seed: 1);
            RunArm(Fixture(generator, 2), hashed: false, passes: 20, seed: 1);
        }

        (ChamberTerrain terrain, List<VoxelPos> floor) = Fixture(generator, chambers);
        Console.WriteLine(
            $"  Fixture: {terrain.Describe()}; its floor full ({floor.Count:N0} cells), a noisy layer over it with "
            + $"one cell in {holeEvery} dry, an unsupported noisy layer over that; every wet cell a seed, shuffled. "
            + $"{rounds} round(s), the arms interleaved, 5 passes an arm a round.");
        Console.WriteLine();
        Console.WriteLine("  arm          seeds   visited   written    sheets   sort ms   flood ms   level ms   solve ms   flood ns/item   slots");
        Console.WriteLine("  ----------   -------   -------   -------   ------   -------   --------   --------   --------   -------------   -----");

        var totals = new Dictionary<bool, ArmTotals> { [true] = new(), [false] = new() };
        bool agree = true;
        for (int round = 0; round < rounds; round++)
        {
            foreach (bool hashed in round % 2 == 0 ? new[] { true, false } : new[] { false, true })
            {
                ArmResult arm = RunArm((terrain, floor), hashed, passes: 5, seed: 1000 + round);
                totals[hashed].Add(arm);
            }

            // The same round's two arms solved the same noise: they must have landed the same water.
            ArmTotals h = totals[true], l = totals[false];
            if (h.LastChecksum != l.LastChecksum || h.LastCounts != l.LastCounts)
            {
                agree = false;
                Console.WriteLine($"  round {round}: THE ARMS DISAGREE — hashed {h.LastCounts} / {h.LastChecksum:X}, layered {l.LastCounts} / {l.LastChecksum:X}");
            }
        }

        foreach (bool hashed in new[] { true, false })
        {
            ArmTotals t = totals[hashed];
            double n = t.Passes;
            double items = (t.Seeds + t.Visited) / n;
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {(hashed ? "dictionary" : "two-layer"),-10}   {t.Seeds / n,7:N0}   {t.Visited / n,7:N0}   {t.Written / n,7:N0}   {t.Sheets / n,6:N0}   "
                + $"{t.Sort / n,7:F2}   {t.Flood / n,8:F2}   {t.Level / n,8:F2}   {(t.Gather + t.Sort + t.Flood + t.Level) / n,8:F2}   "
                + $"{t.Flood / n * 1e6 / items,13:F1}   {t.Slots,5:N0}"));
        }

        Console.WriteLine();
        Console.WriteLine(agree
            ? $"  Both arms landed the same water on every pass (checksum and counts); the last round's checksum {totals[false].LastChecksum:X16}, counts {totals[false].LastCounts}."
            : "  *** The arms disagreed; the timings above compare two different solves. ***");
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  Flood, two-layer against dictionary: {totals[false].Flood / totals[true].Flood:F2}x the time; "
            + $"the solve {(totals[false].Total / totals[true].Total):F2}x. 'flood ns/item' is the flood's time over seeds plus cells visited."));
        return agree ? 0 : 1;
    }

    private static (ChamberTerrain Terrain, List<VoxelPos> Floor) Fixture(WorldGenerator generator, int chambers)
    {
        var terrain = new ChamberTerrain(generator, 0, 0, shaftVoxels: 1, depthMetres: 2, chambers);
        int half = terrain.HalfSpan;
        var floor = new List<VoxelPos>();
        for (int x = -half; x < half; x++)
        {
            for (int z = -half; z < half; z++)
            {
                floor.Add(new VoxelPos(x, terrain.FloorY, z));
            }
        }

        // Bake every column the flood reads, the ring round the floor included (the --lake lesson:
        // a lazily baked column is paid inside the first timed pass).
        for (int x = -half - 1; x <= half; x++)
        {
            for (int z = -half - 1; z <= half; z++)
            {
                for (int dy = -1; dy <= 3; dy++)
                {
                    terrain.CanHold(new VoxelPos(x, terrain.FloorY + dy, z));
                }
            }
        }

        return (terrain, floor);
    }

    private static ArmResult RunArm((ChamberTerrain Terrain, List<VoxelPos> Floor) fixture, bool hashed, int passes, int seed)
    {
        var sim = new FluidSimulation(fixture.Terrain) { HashedFloodIndex = hashed };
        var random = new Random(seed);
        var result = new ArmResult();
        var seeds = new List<VoxelPos>();
        foreach (VoxelPos cell in fixture.Floor)
        {
            sim.Grid.Fill(cell, Cap);
        }

        for (int pass = 0; pass < passes; pass++)
        {
            seeds.Clear();
            foreach (VoxelPos cell in fixture.Floor)
            {
                seeds.Add(cell);
                for (int dy = 1; dy <= 2; dy++)
                {
                    VoxelPos above = new(cell.X, cell.Y + dy, cell.Z);
                    sim.Grid.Drain(above, Cap);
                    bool dry = dy == 1 && random.Next(HoleEvery) == 0;
                    int amount = dry ? 0 : random.Next(2_000, 14_000);
                    if (amount > 0)
                    {
                        sim.Grid.Fill(above, amount);
                        seeds.Add(above);
                    }
                }
            }

            for (int i = seeds.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (seeds[i], seeds[j]) = (seeds[j], seeds[i]);
            }

            SheetLevelResult r = sim.LevelSheets(seeds, share: SheetSettle.DefaultSettleShare);
            result.Add(r);
            result.LastChecksum = Checksum(sim, fixture.Floor);
        }

        result.Slots = sim.FloodIndexSlots;
        return result;
    }

    /// <summary>The noise's hole rate; set from the command line before a run.</summary>
    internal static int HoleEvery { get; set; } = 7;

    private static ulong Checksum(FluidSimulation sim, List<VoxelPos> floor)
    {
        ulong sum = 0;
        foreach (VoxelPos cell in floor)
        {
            for (int dy = 0; dy <= 2; dy++)
            {
                int amount = sim.Grid.AmountAt(new VoxelPos(cell.X, cell.Y + dy, cell.Z));
                sum = (sum * 1_000_003UL) + (ulong)amount;
            }
        }

        return sum;
    }

    private sealed class ArmResult
    {
        public long Seeds, Visited, Written, Sheets;
        public double Gather, Sort, Flood, Level;
        public int Passes;
        public ulong LastChecksum;
        public string LastCounts = string.Empty;
        public int Slots;

        public void Add(SheetLevelResult r)
        {
            Passes++;
            Seeds += r.Seeds;
            Visited += r.CellsVisited;
            Written += r.CellsWritten;
            Sheets += r.Sheets;
            Gather += r.GatherMillis;
            Sort += r.SortMillis;
            Flood += r.FloodMillis;
            Level += r.LevelMillis;
            LastCounts = $"{r.Sheets}/{r.Levelled}/{r.Packed}/{r.Skipped}/{r.UnderShare}/{r.CellsVisited}/{r.CellsWritten}";
        }
    }

    private sealed class ArmTotals
    {
        public long Seeds, Visited, Written, Sheets;
        public double Gather, Sort, Flood, Level;
        public int Passes, Slots;
        public ulong LastChecksum;
        public string LastCounts = string.Empty;

        public double Total => Gather + Sort + Flood + Level;

        public void Add(ArmResult a)
        {
            Seeds += a.Seeds;
            Visited += a.Visited;
            Written += a.Written;
            Sheets += a.Sheets;
            Gather += a.Gather;
            Sort += a.Sort;
            Flood += a.Flood;
            Level += a.Level;
            Passes += a.Passes;
            Slots = Math.Max(Slots, a.Slots);
            LastChecksum = a.LastChecksum;
            LastCounts = a.LastCounts;
        }
    }
}
