using System.Diagnostics;
using Overburden.Core.Content;
using Overburden.Core.Fluid;
using Overburden.Core.Voxels;
using Overburden.Core.Worldgen;

namespace Overburden.Bench;

/// <summary>
/// What Tier B pays to ask the world <i>can water be here</i>, read the way the game reads it
/// against read the way a baked field would — the measurement WATER-THREAD-PLAN's slice 2 asks for
/// before any bitset is built.
/// </summary>
/// <remarks>
/// <para>
/// <b>The same flood, twice, over terrain that answers identically.</b> Arm one is
/// <see cref="DugTerrain"/>, the bitset every other fluid figure in this bench was taken over. Arm
/// two is the production <see cref="VoxelFluidTerrain"/> over real <see cref="VoxelChunk"/>s in a
/// dictionary — the game's own path: a hash lookup on a three-int key, a palette decode, and a
/// material-table read, up to six of them a visit. <b>The oracle is that the two arms visit the
/// same cells and land the same water</b>, which is what says the difference in the clock is the
/// read and nothing else; a run whose arms disagree by a millilitre has measured two floods.
/// </para>
/// <para>
/// <b>What it cannot tell you.</b> The gap is an upper bound on what slice 2 could win, not a
/// prediction: the bench's bitset is one column of bits per (x, z) and slice 2's would be one array
/// per chunk, so their locality differs. And a chunked world built here has every chunk resident and
/// warm, where the game's dictionary holds thousands and is walked by four other systems — so if
/// anything this arm <i>under</i>-states the game's cost.
/// </para>
/// </remarks>
internal static class TerrainReadBench
{
    /// <summary>An <see cref="IVoxelWorld"/> that is a dictionary of chunks, as the streamer is.</summary>
    private sealed class ChunkedWorld : IVoxelWorld
    {
        private readonly Dictionary<ChunkPos, VoxelChunk> _chunks = [];

        public int Chunks => _chunks.Count;

        /// <summary>
        /// The chunk at this position, created <b>solid</b> if it does not exist yet.
        /// </summary>
        /// <remarks>
        /// <b>Solid rather than air, and the arms-disagree oracle is what said so.</b> A chunk that
        /// straddles the fixture's bounds is created because part of it is inside; the part outside
        /// is never written, so an air default made it holdable where the baked arm calls it rock —
        /// 194,366 visits of difference on the first run. A fixture that answers a different
        /// question in each arm times two different floods.
        /// </remarks>
        public VoxelChunk Chunk(ChunkPos position, int solidId)
        {
            if (!_chunks.TryGetValue(position, out VoxelChunk? chunk))
            {
                chunk = new VoxelChunk(solidId);
                _chunks[position] = chunk;
            }

            return chunk;
        }

        /// <summary>Deliberately a copy of <c>ChunkStreamer.TryGetMaterial</c>, line for line.</summary>
        public bool TryGetMaterial(VoxelPos voxel, out int materialId)
        {
            if (!_chunks.TryGetValue(voxel.Chunk, out VoxelChunk? chunk))
            {
                materialId = MaterialIds.AirNumericId;
                return false;
            }

            (int localX, int localY, int localZ) = voxel.Local;
            materialId = chunk.Get(localX, localY, localZ);
            return true;
        }
    }

    internal static int Run(
        WorldGenerator generator,
        MaterialTable materials,
        int stoneId,
        string machineControl,
        bool isDebug,
        int chambers,
        long litres,
        int ticks)
    {
        Console.WriteLine("Terrain read bench — what a Tier B visit pays to ask the world");
        if (isDebug)
        {
            Console.WriteLine("*** DEBUG BUILD — these timings are meaningless. Re-run with -c Release. ***");
        }

        Console.WriteLine();
        Console.WriteLine(machineControl);
        Console.WriteLine();

        var baked = new ChamberTerrain(generator, 0, 0, shaftVoxels: 3, depthMetres: 2, chambers);
        Console.WriteLine($"  Fixture: {baked.Describe()}");
        Console.WriteLine(
            $"  {litres:N0} L poured into the mouth, {ticks:N0} ticks, both arms from the same state.");
        Console.WriteLine();

        // The chunked world is materialised from the baked one, so the two arms answer the same
        // question about the same rock by construction rather than by two derivations agreeing.
        var world = new ChunkedWorld();
        long open = Materialise(baked, world, stoneId);
        var chunked = new VoxelFluidTerrain(world, materials);

        Console.WriteLine(
            $"  Chunked arm: {world.Chunks:N0} chunk(s) resident, {open:N0} open voxel(s), "
            + $"{world.Chunks * 32L * 32 * 32 / 1024:N0} KiB of voxels at one byte a voxel.");
        Console.WriteLine();

        (long visits, long held, double millis) a = Flood(baked, baked, litres, ticks);
        (long visits, long held, double millis) b = Flood(chunked, baked, litres, ticks);

        if (a.visits != b.visits || a.held != b.held)
        {
            Console.Error.WriteLine(
                $"  ARMS DISAGREE — baked {a.visits:N0} visits / {a.held:N0} mL against chunked "
                + $"{b.visits:N0} / {b.held:N0}. The terrains are not the same terrain, so the "
                + "timings are of two different floods and mean nothing.");
            return 1;
        }

        Console.WriteLine($"  {"arm",-28}{"ms",12}{"µs/visit",12}{"against baked",15}");
        Row("baked bitset (this bench)", a.millis, a.visits, 1.0);
        Row("chunked (as the game reads)", b.millis, b.visits, b.millis / a.millis);
        Console.WriteLine();
        Console.WriteLine(
            $"  Same trajectory in both arms: {a.visits:N0} visits, {a.held:N0} mL held. "
            + "The clock is the only thing that differed.");
        Console.WriteLine();
        Console.WriteLine(
            "  Read it as an upper bound on slice 2, not a prediction: the bench's bitset is one "
            + "column of bits per (x, z) and slice 2's would be one array per chunk, so their "
            + "locality differs. Every chunk here is resident and warm, which if anything "
            + "under-states what the game pays.");

        return 0;

        static void Row(string name, double millis, long visits, double ratio) =>
            Console.WriteLine(
                $"  {name,-28}{millis,12:N1}{millis * 1000.0 / Math.Max(visits, 1),12:F3}{ratio,15:F2}x");
    }

    /// <summary>Writes the baked terrain's rock into real chunks. Returns the open voxels.</summary>
    private static long Materialise(ChamberTerrain baked, ChunkedWorld world, int stoneId)
    {
        (VoxelPos min, VoxelPos max) = baked.Bounds();
        long open = 0;

        for (int x = min.X; x <= max.X; x++)
        {
            for (int z = min.Z; z <= max.Z; z++)
            {
                for (int y = min.Y; y <= max.Y; y++)
                {
                    var voxel = new VoxelPos(x, y, z);
                    VoxelChunk chunk = world.Chunk(voxel.Chunk, stoneId);
                    if (!baked.CanHold(voxel))
                    {
                        continue;
                    }

                    open++;
                    (int lx, int ly, int lz) = voxel.Local;
                    chunk.Set(lx, ly, lz, MaterialIds.AirNumericId);
                }
            }
        }

        return open;
    }

    /// <summary>One flood, timed. The mouth is the baked terrain's, whichever arm answers CanHold.</summary>
    private static (long Visits, long Held, double Millis) Flood(
        IFluidTerrain terrain, ChamberTerrain shape, long litres, int ticks)
    {
        var sim = new FluidSimulation(terrain);
        var mouth = new List<VoxelPos>(shape.Mouth());
        long owed = litres * FluidConstants.MillilitresPerLitre;

        // Warm the arm on its own fixture before the clock starts: the chunked arm's first read of
        // a chunk is a dictionary miss and the baked arm's is a column evaluation, and neither is
        // what a flood pays. Same shape as --lake's bake.
        foreach (VoxelPos cell in mouth)
        {
            for (int dy = -8; dy <= 1; dy++)
            {
                terrain.CanHold(cell.Offset(0, dy, 0));
            }
        }

        var clock = Stopwatch.StartNew();
        for (int tick = 0; tick < ticks; tick++)
        {
            foreach (VoxelPos cell in mouth)
            {
                if (owed <= 0)
                {
                    break;
                }

                long room = Math.Min(owed, FluidConstants.CellCapacityMillilitres);
                owed -= room - sim.Add(cell, room);
            }

            sim.Step();
        }

        clock.Stop();
        return (sim.CellVisits, sim.TotalMillilitres, clock.Elapsed.TotalMilliseconds);
    }
}
