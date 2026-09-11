using Overburden.Core.Content;
using Overburden.Core.Fluid;
using Overburden.Core.Streaming;
using Overburden.Core.Voxels;
using Overburden.Core.Worldgen;

namespace Overburden.Core.Tests;

/// <summary>
/// M8b-4a: a cell whose chunk is not loaded is paused, not displaced, and its chunk's arrival
/// wakes it and the faces beside it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The play's number is the oracle: 2.9 ML of 7.2 ML drawn in one session was displaced, every
/// jump of it on a window move.</b> An awake cell whose chunk left the box was visited, found the
/// terrain answering "cannot hold", and was squeezed into neighbours that were unloaded too. Every
/// test here asserts <c>DisplacedMillilitres == 0</c> across a chunk leaving and returning, and
/// the lifecycle fuzz asserts it across eighty rounds of random ones.
/// </para>
/// <para>
/// <b>Break-recipes filed before the code, each with the test that catches it</b>: pause without
/// remembering — <see cref="AnAwakeCellWhoseChunkLeavesIsPausedInPlaceAndWokenWhenItReturns"/>,
/// whose paused cell is mid-air and on no face, so only the paused list reaches it; wake the
/// paused cells and not the arriving chunk's own faces —
/// <see cref="ALevelledSheetBesideAMissingChunkIsWokenAcrossTheFaceWhenItArrives"/>, where the
/// sheet solve lowers a cell without waking across the boundary; wake this side's faces and not
/// the neighbour's — <see cref="WaterRestingAgainstAMissingChunkFlowsInWhenItArrives"/>, where the
/// arriving chunk holds nothing; treat unloaded as air —
/// <see cref="TheRealAdapterAnswersUnknownRatherThanAirForAChunkWithoutVoxels"/>, against the real
/// streamer; and <c>VerifyQuiescent</c> still reading unloaded cells — the first test's check
/// while the chunk is away, and every round of the fuzz.
/// </para>
/// </remarks>
// The simulations here run the one-cell fall (fallCells: 1), the arm every fixture in this file
// was built on: they catch water in mid-fall — at a chunk's face, in a chunk whose row is pending,
// in a chunk that leaves — and M8d-3's rule lands it in one tick. The fall has its own tests
// (FluidFallTests), including the drop that meets an unloaded chunk on the way down.
public class FluidPauseTests
{
    private const int Cap = FluidConstants.CellCapacityMillilitres;

    private static readonly ChunkPos West = new(0, 0, 0);
    private static readonly ChunkPos East = new(1, 0, 0);

    /// <summary>
    /// The floor and the lane of every corridor here: interior to the chunk on Y and Z, so that
    /// only a chunk's ±X faces touch the water. The first draft put the corridor at y = 0 and
    /// z = 0 — the chunk's −Y and −Z face layers — and the face wake reached every cell in it,
    /// which made "only the paused list can reach this cell" false in the fixture rather than the
    /// code, and counted nine face wakes where the boundary has two.
    /// </summary>
    private const int Floor = 8;
    private const int Lane = 5;

    /// <summary>A cell on the corridor's floor, or <paramref name="up"/> cells above it.</summary>
    private static VoxelPos At(int x, int up = 0) => new(x, Floor + up, Lane);

    /// <summary>A one-wide corridor along X on the floor, straddling the boundary at x = 32.</summary>
    private static LoadableTerrain Corridor(int minX = 24, int maxX = 39, int height = 4) =>
        new(new BoxTerrain(minX, Floor, Lane, maxX, Floor + height - 1, Lane));

    // ---------------------------------------------------------------- the pause

    [Fact]
    public void AnAwakeCellWhoseChunkLeavesIsPausedInPlaceAndWokenWhenItReturns()
    {
        // A tall corridor so the water is still falling when its chunk goes: mid-air, mid-flow,
        // and on no face of the chunk — the one cell only the paused list can reach again.
        LoadableTerrain terrain = Corridor(0, 63, height: 8);
        var sim = new FluidSimulation(terrain, fallCells: 1);
        sim.Add(At(40, up: 6), Cap);
        sim.Step();

        // Wherever the tick left it: a cell woken beside the source is visited in the same tick,
        // so it falls more than one cell. Mid-air, in the east, and on no face of the chunk.
        VoxelPos falling = Assert.Single(sim.Grid.HeldCells());
        Assert.Equal(Cap, sim.Grid.AmountAt(falling));
        Assert.True(falling.Y > Floor, "the water should still be falling");
        Assert.Equal(East, falling.Chunk);
        Assert.True(sim.ActiveCellCount > 0, "the water should still be falling");

        terrain.Unload(East);
        FluidSettleResult away = sim.Settle();

        // It stops, it stays, and nothing is destroyed. Before this slice the visit displaced the
        // cell into neighbours that were unloaded too, and drained it as "displaced".
        Assert.True(away.Settled, "a paused cell must not keep the automaton awake");
        Assert.Equal(Cap, sim.Grid.AmountAt(falling));
        Assert.Equal(0L, sim.DisplacedMillilitres);
        Assert.Equal(1, sim.PausedCellCount);
        Assert.Equal(1, sim.PausedChunkCount);
        Assert.DoesNotContain(falling, sim.Rested);
        Assert.True(sim.VerifyQuiescent(), "a paused cell is not a move the check may report");
        FluidAssert.Conserves(sim);

        terrain.Load(East);
        sim.ChunkArrived(East);
        Assert.Equal(0, sim.PausedCellCount);
        Assert.Equal(0, sim.PausedChunkCount);
        Assert.Equal(1L, sim.ResumedCells);

        FluidAssert.SettlesAndRests(sim);
        FluidAssert.Conserves(sim);
        Assert.Equal(0L, sim.DisplacedMillilitres);
        Assert.Equal(Cap, sim.TotalMillilitres);
        Assert.True(sim.Grid.AmountAt(At(40)) > 0, "the water never reached the floor");
        Assert.All(sim.Grid.HeldCells(), cell => Assert.Equal(Floor, cell.Y));
    }

    [Fact]
    public void NothingInAnUnloadedChunkChangesWhileItIsAway()
    {
        // Water at rest across the boundary, then the east chunk goes and the west keeps working:
        // a pour beside the boundary, a dig under it, a removal, and ticks. Not one east cell moves.
        LoadableTerrain terrain = Corridor();
        var sim = new FluidSimulation(terrain, fallCells: 1);
        sim.Add(At(28), Cap);
        sim.Add(At(35), Cap);
        FluidAssert.SettlesAndRests(sim);

        Dictionary<VoxelPos, int> before = Snapshot(sim, East);
        Assert.True(before.Count > 0, "the fixture put no water in the east chunk");

        terrain.Unload(East);
        sim.Add(At(31, up: 1), Cap);
        sim.Step();
        sim.Step();
        sim.Remove(At(30), 500);
        sim.Step();
        terrain.Dig(At(31, up: -1));
        sim.Disturb(At(31, up: -1));
        FluidSettleResult away = sim.Settle();
        Assert.True(away.Settled);

        Assert.Equal(before, Snapshot(sim, East));
        Assert.Equal(0L, sim.DisplacedMillilitres);
        Assert.True(sim.VerifyQuiescent());
        FluidAssert.Conserves(sim);
    }

    // ---------------------------------------------------------------- the arrival

    [Fact]
    public void WaterRestingAgainstAMissingChunkFlowsInWhenItArrives()
    {
        // The east chunk is absent from the start, so the water piles against the boundary as
        // against a wall and rests there. The arriving chunk holds nothing, so the only thing that
        // can wake the west cells is the arrival looking across its own face at the neighbour's.
        LoadableTerrain terrain = Corridor();
        terrain.Unload(East);
        var sim = new FluidSimulation(terrain, fallCells: 1);
        sim.Add(At(28), Cap);
        sim.Add(At(26), Cap);
        FluidAssert.SettlesAndRests(sim);

        Assert.Equal(0, sim.PausedCellCount);
        Assert.True(sim.Grid.AmountAt(At(31)) > 0);
        Assert.Equal(0, sim.Grid.AmountAt(At(32)));

        terrain.Load(East);
        sim.ChunkArrived(East);
        Assert.Equal(1, sim.ActiveCellCount);
        Assert.Equal(1L, sim.FaceWakes);

        FluidAssert.SettlesAndRests(sim);
        FluidAssert.Conserves(sim);
        Assert.True(sim.Grid.AmountAt(At(32)) > 0, "nothing crossed the boundary");
        Assert.True(sim.Grid.AmountAt(At(39)) > 0, "the water never reached the far end");
        Assert.Equal(0L, sim.DisplacedMillilitres);
    }

    [Fact]
    public void ALevelledSheetBesideAMissingChunkIsWokenAcrossTheFaceWhenItArrives()
    {
        // Every change a loaded neighbour makes wakes across the boundary — a pour, a draw, a
        // dig, a flow — and the woken cell is paused and remembered. The sheet solve is the one
        // that does not: a levelled cell beside a neighbour that cannot hold is not a front, so
        // it is written and left asleep, and the cell across the face gains a move with nobody
        // to tell it. Only the arriving chunk's own face wake reaches it.
        LoadableTerrain terrain = Corridor();
        var sim = new FluidSimulation(terrain, fallCells: 1);
        sim.Add(At(28), Cap);
        sim.Add(At(35), Cap);
        FluidAssert.SettlesAndRests(sim);

        terrain.Unload(East);
        Assert.Equal(1_000L, sim.Remove(At(24), 1_000));
        SheetLevelResult levelled = sim.LevelSheets();
        Assert.True(levelled.CellsWritten > 0, "the solve levelled nothing, so the fixture is wrong");
        FluidSettleResult away = sim.Settle();
        Assert.True(away.Settled);

        // The paused list never saw the east cell: it was asleep when the chunk left and nothing
        // woke it since. The move exists all the same.
        var eastEdge = At(32);
        var westEdge = At(31);
        int eastBefore = sim.Grid.AmountAt(eastEdge);
        Assert.Equal(0, sim.PausedCellCount);
        Assert.True(
            FluidRules.HorizontalTransfer(eastBefore, sim.Grid.AmountAt(westEdge)) > 0,
            "the fixture left the east edge without a move into the levelled sheet");
        Assert.True(sim.VerifyQuiescent());

        terrain.Load(East);
        sim.ChunkArrived(East);
        FluidAssert.SettlesAndRests(sim);
        FluidAssert.Conserves(sim);
        Assert.True(sim.Grid.AmountAt(eastEdge) < eastBefore, "the east edge never gave to the sheet");
        Assert.Equal(0L, sim.DisplacedMillilitres);
    }

    [Fact]
    public void ADigBesideAMissingChunkLandsInThePausedListAndIsSeenWhenTheChunkArrives()
    {
        // A wall at x = 31 with water resting behind it in the east; the east chunk leaves; the
        // wall is dug from the west. Disturb wakes the wet cell across the boundary, the visit
        // pauses it, and the arrival lets it through the hole.
        var terrain = new LoadableTerrain(new PredicateTerrain(v =>
            v.X >= 24 && v.X <= 39 && v.Y >= Floor && v.Y <= Floor + 3 && v.Z == Lane && v.X != 31));
        var sim = new FluidSimulation(terrain, fallCells: 1);
        sim.Add(At(35), Cap);
        FluidAssert.SettlesAndRests(sim);
        Assert.True(sim.Grid.AmountAt(At(32)) > 0);

        terrain.Unload(East);
        var wall = At(31);
        Assert.True(terrain.Dig(wall));
        sim.Disturb(wall);
        Assert.Equal(1L, sim.DisturbedCells);
        FluidSettleResult away = sim.Settle();
        Assert.True(away.Settled);
        Assert.Equal(1, sim.PausedCellCount);
        Assert.Equal(0, sim.Grid.AmountAt(wall));
        Assert.Equal(0L, sim.DisplacedMillilitres);

        terrain.Load(East);
        sim.ChunkArrived(East);
        FluidAssert.SettlesAndRests(sim);
        FluidAssert.Conserves(sim);
        Assert.True(sim.Grid.AmountAt(wall) > 0, "the water never came through the hole");
        Assert.Equal(0L, sim.DisplacedMillilitres);
    }

    [Fact]
    public void AnArrivalWakesTheHeldCellsOnItsFacesAndNothingElse()
    {
        // The 32,768-visit trap, the other way round: a chunk arriving beside a lake must not
        // wake the lake, and a chunk arriving in dry rock must not wake anything at all.
        LoadableTerrain terrain = Corridor();
        var sim = new FluidSimulation(terrain, fallCells: 1);
        sim.Add(At(28), Cap);
        sim.Add(At(35), Cap);
        FluidAssert.SettlesAndRests(sim);
        int held = sim.Grid.HeldCellCount;
        Assert.True(held > 4, "the fixture holds too little water to tell a face from a chunk");

        // A re-arrival — the cache revival's case, a chunk that never lost its water announcing
        // itself again — wakes the two edge cells, one each side of the boundary, and no more.
        sim.ChunkArrived(East);
        Assert.Equal(2, sim.ActiveCellCount);
        Assert.Equal(2L, sim.FaceWakes);
        FluidSettleResult after = sim.Settle();
        Assert.Equal(1, after.Ticks);
        Assert.True(sim.VerifyQuiescent());

        sim.ChunkArrived(new ChunkPos(5, 5, 5));
        Assert.Equal(0, sim.ActiveCellCount);
        Assert.Equal(2L, sim.FaceWakes);

        var empty = new FluidSimulation(Corridor(), fallCells: 1);
        empty.ChunkArrived(East);
        Assert.Equal(0, empty.ActiveCellCount);
    }

    [Fact]
    public void AnArrivalLeavesAnUnloadedNeighboursCellsAlone()
    {
        // Both chunks away, water resting in both, and only the east comes back: the west's face
        // cells would only be paused again if woken, so they are not, and the count says so.
        LoadableTerrain terrain = Corridor();
        var sim = new FluidSimulation(terrain, fallCells: 1);
        sim.Add(At(28), Cap);
        sim.Add(At(35), Cap);
        FluidAssert.SettlesAndRests(sim);

        terrain.Unload(West);
        terrain.Unload(East);
        terrain.Load(East);
        sim.ChunkArrived(East);

        Assert.Equal(1, sim.ActiveCellCount);
        Assert.Equal(1L, sim.FaceWakes);
        FluidAssert.SettlesAndRests(sim);
        Assert.Equal(0, sim.PausedCellCount);
    }

    // ---------------------------------------------------------------- the real adapter

    [Fact]
    public void TheRealAdapterAnswersUnknownRatherThanAirForAChunkWithoutVoxels()
    {
        // Against the streamer, because the fixtures above answer from predicates. Tracked with
        // voxels is loaded; tracked and still generating is not, and neither is untracked — and
        // "not loaded" must come with "cannot hold", or a flood leaks into the void.
        var streamer = new ChunkStreamer(loadMargin: 0);
        streamer.AddViewer(ChunkPos.Zero, 2, 1);
        const int stone = 2;
        streamer.Get(new ChunkPos(0, 0, 0))!.Voxels = new VoxelChunk(MaterialIds.AirNumericId);
        streamer.Get(new ChunkPos(1, 0, 0))!.Voxels = new VoxelChunk(stone);
        Assert.NotNull(streamer.Get(new ChunkPos(2, 0, 0)));
        Assert.Null(streamer.Get(new ChunkPos(9, 0, 0)));

        var terrain = new VoxelFluidTerrain(streamer, MaterialTable.ForTesting(
            isOpaque: [false, true, true, true],
            isFluid: [false, false, false, false],
            textureLayer: [0, 1, 2, 3],
            hardness: [0f, 0.8f, 3.0f, 50f]));

        var air = new VoxelPos(5, 5, 5);
        var rock = new VoxelPos(40, 5, 5);
        var generating = new VoxelPos(70, 5, 5);
        var absent = new VoxelPos(300, 5, 5);

        Assert.True(terrain.CanHold(air));
        Assert.True(terrain.IsLoaded(air));
        Assert.False(terrain.CanHold(rock));
        Assert.True(terrain.IsLoaded(rock));
        Assert.False(terrain.CanHold(generating));
        Assert.False(terrain.IsLoaded(generating));
        Assert.False(terrain.CanHold(absent));
        Assert.False(terrain.IsLoaded(absent));
    }

    // ---------------------------------------------------------------- the lifecycle fuzz

    [Theory]
    [InlineData(0x5EEDUL)]
    [InlineData(0xA11CEUL)]
    [InlineData(0xBEEFUL)]
    [InlineData(0xC0FFEEUL)]
    [InlineData(1UL)]
    public void AcrossRandomUnloadsNothingMovesWhileAwayAndEverythingRestsWhenBack(ulong seed)
    {
        // M5's lifecycle fuzz for water: random pours, digs, ticks and sheet solves over real
        // terrain straddling four chunk columns, with chunks taken away and brought back at
        // random, against a twin that is driven identically and never loses one. While a chunk is
        // away not a millilitre in it moves; nothing is ever displaced, because nothing turns to
        // rock; and with everything back both arms rest, and the rest is real.
        WorldGenerator generator = Generator(seed);
        int surface = generator.SurfaceHeightAt(0, 0);
        const int minX = -16, maxX = 15, minZ = -16, maxZ = 15;
        int minY = surface - 24, maxY = surface + 7;

        var toggledInner = new GeneratedTerrain(generator, minX, minY, minZ, maxX, maxY, maxZ);
        var toggled = new LoadableTerrain(toggledInner);
        var control = new LoadableTerrain(new GeneratedTerrain(generator, minX, minY, minZ, maxX, maxY, maxZ));
        var sim = new FluidSimulation(toggled, fallCells: 1);
        var twin = new FluidSimulation(control, fallCells: 1);

        ChunkPos lo = new VoxelPos(minX, minY, minZ).Chunk;
        ChunkPos hi = new VoxelPos(maxX, maxY, maxZ).Chunk;
        var chunks = new List<ChunkPos>();
        for (int cx = lo.X; cx <= hi.X; cx++)
        {
            for (int cy = lo.Y; cy <= hi.Y; cy++)
            {
                for (int cz = lo.Z; cz <= hi.Z; cz++)
                {
                    chunks.Add(new ChunkPos(cx, cy, cz));
                }
            }
        }

        Assert.True(chunks.Count >= 4, "the region must straddle chunk boundaries");

        var random = new Random((int)(seed & 0x7FFFFFFF));
        var away = new Dictionary<ChunkPos, Dictionary<VoxelPos, int>>();
        long offered = 0, rejected = 0, twinRejected = 0;
        int maxPaused = 0, unloadsWithWater = 0, arrivals = 0;

        for (int round = 0; round < 80; round++)
        {
            if (random.Next(3) != 0)
            {
                int x = random.Next(minX, maxX + 1);
                int z = random.Next(minZ, maxZ + 1);
                VoxelPos? top = toggledInner.TopOpening(x, z);
                if (top is { } cell && toggled.IsLoaded(cell))
                {
                    // The same offer to both arms; what each accepts depends on what the cell
                    // already held, so the identity is on what was offered.
                    offered += Cap;
                    rejected += sim.Add(cell, Cap);
                    twinRejected += twin.Add(cell, Cap);
                }
            }

            if (random.Next(2) == 0)
            {
                var v = new VoxelPos(
                    random.Next(minX, maxX + 1), random.Next(minY, maxY + 1), random.Next(minZ, maxZ + 1));
                if (toggled.IsLoaded(v) && !toggledInner.CanHold(v) && toggled.Dig(v))
                {
                    control.Dig(v);
                    sim.Disturb(v);
                    twin.Disturb(v);
                }
            }

            int ticks = random.Next(1, 9);
            for (int t = 0; t < ticks; t++)
            {
                sim.Step();
                twin.Step();
            }

            if (random.Next(4) == 0)
            {
                sim.LevelSheets();
                twin.LevelSheets();
            }

            Dictionary<ChunkPos, Dictionary<VoxelPos, int>> now = ByChunk(sim);
            foreach ((ChunkPos chunk, Dictionary<VoxelPos, int> snapshot) in away)
            {
                Dictionary<VoxelPos, int> current = now.GetValueOrDefault(chunk) ?? [];
                Assert.True(
                    snapshot.Count == current.Count && snapshot.All(kv => current.GetValueOrDefault(kv.Key) == kv.Value),
                    $"seed {seed:X}, round {round}: water in unloaded {chunk} changed");
            }

            Assert.Equal(0L, sim.DisplacedMillilitres);
            Assert.Equal(0L, twin.DisplacedMillilitres);
            Assert.Equal(0, twin.PausedCellCount);
            maxPaused = Math.Max(maxPaused, sim.PausedCellCount);
            FluidAssert.Conserves(sim, rejected);
            FluidAssert.Conserves(twin, twinRejected);

            // Toggle one chunk, leaning toward the ones that hold water, because an unload of dry
            // rock proves nothing.
            List<ChunkPos> wet = now.Keys.Where(chunks.Contains).ToList();
            ChunkPos target = wet.Count > 0 && random.Next(10) < 7
                ? wet[random.Next(wet.Count)]
                : chunks[random.Next(chunks.Count)];
            if (toggled.IsChunkLoaded(target))
            {
                toggled.Unload(target);
                Dictionary<VoxelPos, int> snapshot = now.GetValueOrDefault(target) ?? [];
                away[target] = snapshot;
                if (snapshot.Count > 0)
                {
                    unloadsWithWater++;
                }
            }
            else
            {
                toggled.Load(target);
                away.Remove(target);
                sim.ChunkArrived(target);
                arrivals++;
            }
        }

        foreach (ChunkPos chunk in away.Keys.ToList())
        {
            toggled.Load(chunk);
            sim.ChunkArrived(chunk);
            arrivals++;
        }

        away.Clear();

        FluidAssert.SettlesAndRests(sim);
        FluidAssert.SettlesAndRests(twin);
        FluidAssert.Conserves(sim, rejected);
        FluidAssert.Conserves(twin, twinRejected);
        Assert.Equal(0, sim.PausedCellCount);
        Assert.Equal(0, sim.PausedChunkCount);
        Assert.Equal(0L, sim.DisplacedMillilitres);
        Assert.Equal(offered, sim.AddedMillilitres + rejected);
        Assert.Equal(offered, twin.AddedMillilitres + twinRejected);

        // A run that never paused anything, or never unloaded water, proves nothing.
        Assert.True(sim.AddedMillilitres > 0, "no water was ever poured");
        Assert.True(unloadsWithWater > 0, $"seed {seed:X}: no chunk was ever unloaded with water in it");
        Assert.True(maxPaused > 0, $"seed {seed:X}: nothing was ever paused");
        Assert.True(arrivals > 0, $"seed {seed:X}: no chunk ever arrived");
    }

    private static WorldGenerator Generator(ulong seed)
    {
        var strata = new StrataTable([new StrataLayer(1, 1), new StrataLayer(2, 3)], 3);
        return new WorldGenerator(
            new WorldGenSettings
            {
                Seed = seed,
                SeaLevelVoxels = 0,
                SurfaceAmplitudeVoxels = 48.0f,
                SurfaceFrequency = 0.004f,
                SurfaceOctaves = 4,
                CavesEnabled = true,
                CaveLatticeStep = 4,
            },
            strata);
    }

    private static Dictionary<VoxelPos, int> Snapshot(FluidSimulation sim, ChunkPos chunk) =>
        ByChunk(sim).GetValueOrDefault(chunk) ?? [];

    private static Dictionary<ChunkPos, Dictionary<VoxelPos, int>> ByChunk(FluidSimulation sim)
    {
        var result = new Dictionary<ChunkPos, Dictionary<VoxelPos, int>>();
        foreach (VoxelPos cell in sim.Grid.HeldCells())
        {
            if (!result.TryGetValue(cell.Chunk, out Dictionary<VoxelPos, int>? cells))
            {
                cells = [];
                result[cell.Chunk] = cells;
            }

            cells[cell] = sim.Grid.AmountAt(cell);
        }

        return result;
    }
}
