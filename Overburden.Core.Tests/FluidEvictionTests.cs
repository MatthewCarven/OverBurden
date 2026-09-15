using Overburden.Core.Content;
using Overburden.Core.Fluid;
using Overburden.Core.Jobs;
using Overburden.Core.Persistence;
using Overburden.Core.Streaming;
using Overburden.Core.Voxels;
using Overburden.Core.Worldgen;

namespace Overburden.Core.Tests;

/// <summary>
/// M8b-4c: the panel leaves RAM with its chunk — one tick after it — and comes back with it, from
/// memory, from a snapshot not yet written, or off the serial lane; and a chunk whose row is still
/// to be read is not loaded, to the water.
/// </summary>
/// <remarks>
/// <para>
/// <b>Break-recipes filed in the plan, each with the test that catches it</b>: release at the mark
/// rather than after the tick (a stranded front) —
/// <see cref="EvictionMarkedBetweenTicksReleasesWhenTheNextTickCompletes"/> and
/// <see cref="EvictionMarkedMidTickWaitsForTheTickAfterThisOne"/>, strictly, and the fuzz's
/// <c>VerifyQuiescent</c>; evict without clearing the stored-awake set (the lake deleted by the
/// next save) — <see cref="AnEvictedChunkIsForgottenByTheStoredAwakeSet"/>; evict into the
/// emptied set — <see cref="AnEvictedPanelDoesNotWriteItselfEmpty"/>; the gate missing on the
/// world — <see cref="AChunkWhoseRowIsPendingIsNotLoadedToTheWater"/>; the arrival wake fired
/// before the restore — <see cref="AWetChunkIsLoadedToTheWaterOnlyOnceItsRowIsRestored"/>; the
/// flag not surviving the cache, or a dropped load not retried —
/// <see cref="AFluidLoadInterruptedByTheViewerLeavingIsRetriedAtRevival"/>; the store consulted
/// before memory (the past restored) — <see cref="ASnapshotNotYetOnDiskIsTheSourceAheadOfTheStore"/>;
/// a chunk back inside the window loaded anyway (the restore throws) —
/// <see cref="AChunkBackInsideTheEvictionWindowKeepsItsPanel"/>; a bad row played past —
/// <see cref="AFluidRowThatWillNotReadFaultsRatherThanPlayingBesideAMissingLake"/>; and
/// <c>evicted</c> missing from the identity — every <c>Conserves</c> here.
/// </para>
/// </remarks>
// The simulations here run the one-cell fall (fallCells: 1), the arm every fixture in this file
// was built on: they catch water in mid-fall — at a chunk's face, in a chunk whose row is pending,
// in a chunk that leaves — and M8d-3's rule lands it in one tick. The fall has its own tests
// (FluidFallTests), including the drop that meets an unloaded chunk on the way down.
public class FluidEvictionTests
{
    private const int Cap = FluidConstants.CellCapacityMillilitres;

    private static readonly ChunkPos West = new(0, 0, 0);
    private static readonly ChunkPos East = new(1, 0, 0);

    private const int Floor = 8;
    private const int Lane = 5;

    private static VoxelPos At(int x, int up = 0) => new(x, Floor + up, Lane);

    /// <summary>A one-voxel-wide corridor four high from x 24 to 39, across the West | East face at x = 32.</summary>
    private static LoadableTerrain Corridor() =>
        new(new BoxTerrain(24, Floor, Lane, 39, Floor + 3, Lane));

    private static Dictionary<VoxelPos, int> CellsIn(FluidSimulation sim, Func<ChunkPos, bool> which)
    {
        var cells = new Dictionary<VoxelPos, int>();
        // Unchecked: the fuzz reads between a slice and its finish, where the job arm holds a job
        // that no worker is running.
        foreach (VoxelPos cell in sim.GridUnchecked.HeldCells())
        {
            if (which(cell.Chunk)) cells[cell] = sim.GridUnchecked.AmountAt(cell);
        }

        return cells;
    }

    private static long WaterIn(FluidSimulation sim, ChunkPos chunk) =>
        CellsIn(sim, c => c == chunk).Values.Sum(v => (long)v);

    private static HashSet<VoxelPos> AwakeIn(FluidSimulation sim, ChunkPos chunk) =>
        sim.AwakeHeldCellsUnchecked().Where(c => c.Chunk == chunk).ToHashSet();

    private static HashSet<VoxelPos> ListedAwake(FluidPanelSnapshot snapshot)
    {
        VoxelPos origin = snapshot.Chunk.OriginVoxel;
        var cells = new HashSet<VoxelPos>();
        foreach (ushort index in snapshot.Awake)
        {
            (int lx, int ly, int lz) = VoxelPos.FromIndex(index);
            cells.Add(new VoxelPos(origin.X + lx, origin.Y + ly, origin.Z + lz));
        }

        return cells;
    }

    /// <summary>East's face toward West: the only cells of East a neighbour can still wake once East is gone.</summary>
    private static bool OnWestFaceOfEast(VoxelPos cell) => cell.Chunk == East && cell.Local.X == 0;

    /// <summary>A stack poured at the east end of the corridor and one tick to get it moving, with a front queued in East.</summary>
    private static (FluidSimulation Sim, LoadableTerrain Terrain, HashSet<VoxelPos> QueuedInEast) Flowing()
    {
        LoadableTerrain terrain = Corridor();
        var sim = new FluidSimulation(terrain, fallCells: 1);
        for (int up = 0; up < 4; up++)
        {
            Assert.Equal(0L, sim.Add(At(38, up), Cap));
        }

        sim.Step();
        HashSet<VoxelPos> queued = AwakeIn(sim, East);
        Assert.True(queued.Count > 0, "the fixture queued nothing in East");
        return (sim, terrain, queued);
    }

    // ---------------------------------------------------------------- the grid

    [Fact]
    public void EvictingAPanelTakesItsWaterOutOfTheTotalsAndLeavesNoRowOwed()
    {
        var grid = new FluidGrid();
        Assert.False(grid.HasPanel(East));

        grid.Fill(At(35), 1_000);
        grid.Fill(At(36), 2_000);
        Assert.True(grid.HasPanel(East));
        grid.TakeSnapshot(East); // stored once, so a drain would owe an empty row

        Assert.Equal(3_000L, grid.Evict(East));

        Assert.False(grid.HasPanel(East));
        Assert.Equal(0L, grid.TotalMillilitres);
        Assert.Equal(0, grid.HeldCellCount);
        Assert.Equal(0, grid.PanelCount);
        Assert.Equal(0, grid.AmountAt(At(35)));

        // Not a drain: nothing is owed to the row, which is the truth now.
        Assert.False(grid.IsDirty(East));
        Assert.Empty(grid.DirtyChunks());

        // And nothing to evict twice.
        Assert.Equal(0L, grid.Evict(East));
    }

    // ---------------------------------------------------------------- the simulation

    [Fact]
    public void EvictionMarkedBetweenTicksReleasesWhenTheNextTickCompletes()
    {
        (FluidSimulation sim, LoadableTerrain terrain, HashSet<VoxelPos> queuedAtMark) = Flowing();
        long eastWater = WaterIn(sim, East);

        terrain.Unload(East);
        sim.Evict(East);

        // Marked, not released: the tick has not run.
        var taken = new List<FluidPanelSnapshot>();
        sim.TakeEvicted(taken);
        Assert.Empty(taken);
        Assert.True(sim.Grid.HasPanel(East));
        Assert.Equal(1, sim.EvictingPanelCount);

        // The tick begun after the mark visits every cell queued at it and pauses each one.
        sim.Step();
        sim.TakeEvicted(taken);

        FluidPanelSnapshot snapshot = Assert.Single(taken);
        Assert.Equal(East, snapshot.Chunk);
        Assert.Equal(eastWater, snapshot.TotalMillilitres);

        HashSet<VoxelPos> listed = ListedAwake(snapshot);
        Assert.True(queuedAtMark.IsSubsetOf(listed), "a cell queued at the mark is not listed awake");
        Assert.True(
            listed.All(cell => queuedAtMark.Contains(cell) || OnWestFaceOfEast(cell)),
            "a cell listed awake was neither queued at the mark nor on the face a neighbour can reach");

        Assert.False(sim.Grid.HasPanel(East));
        Assert.Equal(0, sim.EvictingPanelCount);
        Assert.Equal(eastWater, sim.EvictedMillilitres);
        Assert.Equal(1, sim.EvictedPanels);
        Assert.Equal(0, sim.PausedCellCount);
        Assert.Equal(0L, sim.DisplacedMillilitres);
        FluidAssert.Conserves(sim);
    }

    [Fact]
    public void EvictionMarkedMidTickWaitsForTheTickAfterThisOne()
    {
        (FluidSimulation sim, LoadableTerrain terrain, _) = Flowing();

        // Mid-tick: one cell visited, its neighbours queued for the *next* tick, the rest of this
        // one still to visit.
        Assert.Null(sim.Resume(1));
        Assert.True(sim.TickInProgress);
        HashSet<VoxelPos> queuedAtMark = AwakeIn(sim, East);
        long eastWater = WaterIn(sim, East);

        terrain.Unload(East);
        sim.Evict(East);

        // Mid-tick the mark itself waits (slice 3a): nothing is marked until the tick completes,
        // and the release refuses to look. Finishing this tick is then not enough either: what it
        // woke is queued for the next one and not paused yet. Strictly — the fixture is built so
        // that some of East's queued cells are in the next tick's set, and releasing here would
        // list them nowhere.
        Assert.Equal(1, sim.DeferredCount);
        Assert.Equal(0, sim.EvictingPanelCount);
        Assert.Throws<InvalidOperationException>(() => sim.TakeEvicted([]));
        sim.Step();
        Assert.Equal(0, sim.DeferredCount);
        Assert.Equal(1, sim.EvictingPanelCount);
        var taken = new List<FluidPanelSnapshot>();
        sim.TakeEvicted(taken);
        Assert.Empty(taken);
        Assert.True(sim.Grid.HasPanel(East));
        int pausedAfterOne = sim.PausedCellCount;
        Assert.True(pausedAfterOne < queuedAtMark.Count, "every queued cell was already paused after one tick, so the fixture cannot see a release that comes one tick early");

        sim.Step();
        sim.TakeEvicted(taken);
        FluidPanelSnapshot snapshot = Assert.Single(taken);

        HashSet<VoxelPos> listed = ListedAwake(snapshot);
        Assert.True(queuedAtMark.IsSubsetOf(listed), "a cell queued at the mark is not listed awake");
        Assert.True(listed.All(cell => queuedAtMark.Contains(cell) || OnWestFaceOfEast(cell)));
        Assert.Equal(eastWater, snapshot.TotalMillilitres);
        Assert.False(sim.Grid.HasPanel(East));
        Assert.Equal(0, sim.PausedCellCount);
        FluidAssert.Conserves(sim);
    }

    [Fact]
    public void AnEvictedChunkIsForgottenByTheStoredAwakeSet()
    {
        // The deadly recipe: a chunk whose row lists awake cells is remembered so a later save can
        // rewrite the row once they rest. Evicted and still remembered, the next TakeAllDirty
        // finds no panel and writes the row *empty* — the lake deleted by the autosave, with the
        // identity exact.
        (FluidSimulation sim, LoadableTerrain terrain, _) = Flowing();
        var quit = new List<FluidPanelSnapshot>();
        sim.TakeAllDirty(quit);
        Assert.Contains(quit, p => p.Chunk == East && p.Awake.Length > 0);

        terrain.Unload(East);
        sim.Evict(East);
        sim.Step();
        var evicted = new List<FluidPanelSnapshot>();
        sim.TakeEvicted(evicted);
        Assert.Contains(evicted, p => p.Chunk == East && p.Cells.Length > 0);

        var again = new List<FluidPanelSnapshot>();
        sim.TakeAllDirty(again);
        Assert.DoesNotContain(again, p => p.Chunk == East);
    }

    [Fact]
    public void AnEvictedPanelDoesNotWriteItselfEmpty()
    {
        // A drained panel writes an empty row; an evicted one must not, because the row is the
        // truth. A resting, stored, clean panel is evicted with nothing to write and nothing owed.
        LoadableTerrain terrain = Corridor();
        var sim = new FluidSimulation(terrain, fallCells: 1);
        sim.Add(At(38), 4_000);
        FluidAssert.SettlesAndRests(sim);
        var stored = new List<FluidPanelSnapshot>();
        sim.TakeAllDirty(stored);
        Assert.Contains(stored, p => p.Chunk == East);
        long eastWater = WaterIn(sim, East);
        Assert.True(eastWater > 0);

        terrain.Unload(East);
        sim.Evict(East);
        sim.Step();
        var evicted = new List<FluidPanelSnapshot>();
        sim.TakeEvicted(evicted);

        // Row current: nothing written, panel gone, water counted as evicted.
        Assert.Empty(evicted);
        Assert.False(sim.Grid.HasPanel(East));
        Assert.Equal(eastWater, sim.EvictedMillilitres);
        Assert.False(sim.Grid.IsDirty(East));

        var again = new List<FluidPanelSnapshot>();
        sim.TakeAllDirty(again);
        Assert.DoesNotContain(again, p => p.Chunk == East);
        FluidAssert.Conserves(sim);
    }

    [Fact]
    public void ReclaimingAnEvictingChunkKeepsItsPanelAndCancelsTheRelease()
    {
        (FluidSimulation sim, LoadableTerrain terrain, HashSet<VoxelPos> queued) = Flowing();
        long eastWater = WaterIn(sim, East);

        terrain.Unload(East);
        sim.Evict(East);
        Assert.Equal(1, sim.EvictingPanelCount);

        terrain.Load(East);
        Assert.True(sim.Reclaim(East));
        Assert.Equal(1, sim.ReclaimedPanels);
        Assert.Equal(0, sim.EvictingPanelCount);

        sim.Step();
        var taken = new List<FluidPanelSnapshot>();
        sim.TakeEvicted(taken);
        Assert.Empty(taken);
        Assert.True(sim.Grid.HasPanel(East));
        Assert.Equal(0L, sim.EvictedMillilitres);

        // A chunk with nothing here answers no, and so the caller reads the store.
        Assert.False(sim.Reclaim(new ChunkPos(7, 7, 7)));

        FluidAssert.SettlesAndRests(sim);
        Assert.Equal(4L * Cap, sim.TotalMillilitres);
        Assert.True(eastWater > 0 && queued.Count > 0);
    }

    [Fact]
    public void AChunkWhoseRowIsPendingIsNotLoadedToTheWater()
    {
        // The gate, on the real world: a chunk with voxels answers loaded to everything but the
        // water while its row is still to be read.
        var streamer = new ChunkStreamer(loadMargin: 0);
        streamer.AddViewer(ChunkPos.Zero, 1, 1);
        StreamedChunk chunk = streamer.Get(new ChunkPos(0, 0, 0))!;
        chunk.Voxels = new VoxelChunk(MaterialIds.AirNumericId);

        var terrain = new VoxelFluidTerrain(streamer, MaterialTable.ForTesting(
            isOpaque: [false, true, true, true],
            isFluid: [false, false, false, false],
            textureLayer: [0, 1, 2, 3],
            hardness: [0f, 0.8f, 3.0f, 50f]));

        var air = new VoxelPos(5, 5, 5);
        Assert.True(terrain.CanHold(air));
        Assert.True(terrain.IsLoaded(air));

        chunk.FluidPending = true;
        Assert.False(terrain.CanHold(air));
        Assert.False(terrain.IsLoaded(air));
        Assert.True(streamer.TryGetMaterial(air, out _), "the flag leaked out of the water into the world");

        chunk.FluidPending = false;
        Assert.True(terrain.CanHold(air));
        Assert.True(terrain.IsLoaded(air));
    }

    // ---------------------------------------------------------------- the pipeline

    internal static readonly ChunkPos FarAway = new(200, 0, 200);
    internal static readonly ChunkPos Wet = new(0, 1, 0);
    private static readonly VoxelPos Pour = new(16, 40, 16);

    internal static MaterialTable Materials() => MaterialTable.ForTesting(
        isOpaque: [false, true, true, true],
        isFluid: [false, false, false, false],
        textureLayer: [0, 1, 2, 3],
        hardness: [0f, 0.8f, 3.0f, 50f]);

    /// <summary>The automaton over a harness's streamer, wired the way Main wires it.</summary>
    internal sealed class Session : IDisposable
    {
        public Session(
            IWorldStore store,
            long chunkCacheBytes = 0,
            Action<ChunkPos, bool>? onVoxelsReady = null,
            Action<ChunkPos>? onFluidReady = null)
        {
            Harness = new PipelineHarness(
                store: store,
                chunkCacheBytes: chunkCacheBytes,
                onVoxelsReady: onVoxelsReady,
                onFluidReady: (position, _) =>
                {
                    onFluidReady?.Invoke(position);
                    Sim?.ChunkArrived(position);
                });
            Sim = new FluidSimulation(new VoxelFluidTerrain(Harness.Pipeline.Streamer, Materials()), fallCells: 1);
            Harness.Pipeline.Fluid = Sim;
        }

        public PipelineHarness Harness { get; }

        public FluidSimulation Sim { get; }

        public ChunkPipeline Pipeline => Harness.Pipeline;

        public ChunkStreamer Streamer => Harness.Pipeline.Streamer;

        /// <summary>Four cells of water in the air over the origin, one tick in. Returns what the automaton holds.</summary>
        public long PourAtOrigin(int dx = 0, int dy = 0, int dz = 0)
        {
            Assert.NotNull(Streamer.Get(Wet)?.Voxels);
            for (int up = 0; up < 4; up++)
            {
                Assert.Equal(0L, Sim.Add(Pour.Offset(dx, dy + up, dz), Cap));
            }

            Sim.Step();
            Assert.True(Sim.ActiveCellCount > 0);
            return Sim.TotalMillilitres;
        }

        /// <summary>Pumps the pipeline and the automaton together until <paramref name="until"/> holds.</summary>
        public void PumpUntil(Func<bool> until, int timeoutMillis = 30_000, bool pumpTicks = true)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (!until())
            {
                Pipeline.Update();
                if (pumpTicks) Sim.Step();
                if (clock.ElapsedMilliseconds > timeoutMillis)
                {
                    throw new TimeoutException("the session did not reach the state it was pumped toward");
                }

                Thread.Sleep(1);
            }
        }

        public void Dispose() => Harness.Dispose();
    }

    [Fact]
    public void AnEvictedPanelLeavesRamAndComesBackOffTheLaneInOneSession()
    {
        var store = new InMemoryWorldStore();
        using var session = new Session(store);

        ViewerId viewer = session.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
        session.Harness.Settle();
        long held = session.PourAtOrigin();

        session.Streamer.MoveViewer(viewer, FarAway);
        session.Harness.Settle();

        // Marked at the forget; released by the tick after; written by the next batch.
        Assert.True(session.Sim.EvictingPanelCount > 0, "the forget marked nothing");
        session.PumpUntil(() => session.Sim.Grid.PanelCount == 0);
        Assert.Equal(held, session.Sim.EvictedMillilitres);
        Assert.Equal(0L, session.Sim.TotalMillilitres);
        Assert.Equal(0, session.Sim.PausedCellCount);
        FluidAssert.Conserves(session.Sim);

        session.Harness.Settle();
        Assert.True(store.FluidWrites > 0, "the eviction wrote no row");
        Assert.Equal(0, session.Pipeline.UnwrittenFluidCount);
        Assert.True(session.Pipeline.SavedFluidCount > 0);

        // Back. No cache, so the chunk generates again and its row comes off the lane.
        session.Streamer.MoveViewer(viewer, ChunkPos.Zero);
        session.Harness.Settle();

        Assert.True(session.Pipeline.FluidLoads >= 1, "no row was read");
        Assert.Equal(0, session.Pipeline.FluidLoadsEmpty);
        Assert.Equal(held, session.Sim.LoadedMillilitres);
        Assert.Equal(held, session.Sim.TotalMillilitres);
        Assert.Equal(0L, session.Sim.DisplacedMillilitres);
        Assert.False(session.Streamer.Get(Wet)!.FluidPending);
        Assert.Contains(Wet, session.Harness.MeshedPositions);
        FluidAssert.Conserves(session.Sim);
        FluidAssert.SettlesAndRests(session.Sim);
    }

    [Fact]
    public void AWetChunkIsLoadedToTheWaterOnlyOnceItsRowIsRestored()
    {
        // Across a quit, the way Main does it: the chunk's voxels arrive, the automaton sees the
        // chunk as not loaded, the row comes off the lane, and only then does the arrival wake
        // fire — so the chunk's own face wake sees the restored cells.
        var store = new InMemoryWorldStore();
        long held;
        using (var first = new Session(store))
        {
            ViewerId viewer = first.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
            first.Harness.Settle();
            held = first.PourAtOrigin();
            first.Streamer.MoveViewer(viewer, FarAway);
            first.Harness.Settle();
            first.PumpUntil(() => first.Sim.Grid.PanelCount == 0);
            first.Harness.Settle();
        }

        Assert.True(store.FluidWrites > 0);

        bool? pendingAtVoxels = null;
        bool? pendingAtFluid = null;
        var order = new List<string>();
        Session? second = null;
        second = new Session(
            store,
            onVoxelsReady: (position, _) =>
            {
                if (position != Wet) return;
                order.Add("voxels");
                pendingAtVoxels = second!.Streamer.Get(Wet)!.FluidPending;
            },
            onFluidReady: position =>
            {
                if (position != Wet) return;
                order.Add("fluid");
                pendingAtFluid = second!.Streamer.Get(Wet)!.FluidPending;
                // The restore landed before this fired: the water is here and still paused.
                Assert.Equal(held, second.Sim.TotalMillilitres);
            });

        using (second)
        {
            second.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
            second.Harness.Settle();

            Assert.Equal(["voxels", "fluid"], order);
            Assert.False(pendingAtVoxels, "the flag was not set at the voxels' arrival — the gate was open");
            Assert.False(pendingAtFluid);
            Assert.Equal(1, second.Pipeline.FluidLoads);
            Assert.Equal(held, second.Sim.LoadedMillilitres);
            Assert.Equal(held, second.Sim.TotalMillilitres);
            Assert.True(second.Sim.ResumedCells > 0, "the arrival woke nothing");
            Assert.Equal(0, second.Sim.PausedCellCount);
            Assert.Contains(Wet, second.Harness.MeshedPositions);

            FluidAssert.SettlesAndRests(second.Sim);
            Assert.Equal(held, second.Sim.TotalMillilitres);
            Assert.Equal(0L, second.Sim.DisplacedMillilitres);
            FluidAssert.Conserves(second.Sim);
        }
    }

    [Fact]
    public void AChunkWithNoRowIsReadyToTheWaterWhenItsVoxelsAre()
    {
        var voxels = new List<ChunkPos>();
        var fluid = new List<ChunkPos>();
        using var session = new Session(
            new InMemoryWorldStore(),
            onVoxelsReady: (position, _) => voxels.Add(position),
            onFluidReady: fluid.Add);

        session.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
        session.Harness.Settle();

        Assert.True(voxels.Count > 0);
        Assert.Equal(voxels, fluid);
        Assert.Equal(0, session.Pipeline.FluidLoads);
        Assert.All(session.Streamer.Chunks, chunk => Assert.False(chunk.FluidPending));
    }

    /// <summary>A store whose fluid reads and writes can be held at a gate, so a job can be caught in flight.</summary>
    private sealed class GatedStore : PersistenceTests.InMemoryWorldStoreBase
    {
        public ManualResetEventSlim ReadGate { get; } = new(initialState: true);

        public ManualResetEventSlim WriteGate { get; } = new(initialState: true);

        public int ReadsWaiting;

        public override byte[]? ReadFluid(ChunkPos position)
        {
            Interlocked.Increment(ref ReadsWaiting);
            ReadGate.Wait();
            Interlocked.Decrement(ref ReadsWaiting);
            return base.ReadFluid(position);
        }

        public override void Write(WorldBatch batch)
        {
            WriteGate.Wait();
            base.Write(batch);
        }

        public int FluidWrites => Inner.FluidWrites;
    }

    [Fact]
    public void AFluidLoadInterruptedByTheViewerLeavingIsRetriedAtRevival()
    {
        // The flag survives the cache and the job slot's machinery retries: the viewer leaves while
        // the row is being read, the result lands on a chunk nobody views and restores nothing,
        // the chunk goes into the cache still owing its row, and the revival reads it again.
        var store = new GatedStore();
        long held;
        using (var first = new Session(store))
        {
            ViewerId viewer = first.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
            first.Harness.Settle();
            held = first.PourAtOrigin();
            first.Streamer.MoveViewer(viewer, FarAway);
            first.Harness.Settle();
            first.PumpUntil(() => first.Sim.Grid.PanelCount == 0);
            first.Harness.Settle();
        }

        using var second = new Session(store, chunkCacheBytes: 64L * 1024 * 1024);
        store.ReadGate.Reset();

        ViewerId again = second.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
        try
        {
            second.PumpUntil(() => Volatile.Read(ref store.ReadsWaiting) > 0);
            Assert.True(second.Streamer.Get(Wet)!.FluidPending);

            // Leave with the read in flight. The job cannot be cancelled mid-run; it lands on a
            // chunk with no viewer, which restores nothing and keeps the flag.
            second.Streamer.MoveViewer(again, FarAway);
        }
        finally
        {
            // A worker blocked at the gate would block the scheduler's dispose for ever, and the
            // failure would read as a hang rather than as the assertion that failed.
            store.ReadGate.Set();
        }

        second.Harness.Settle();

        Assert.Equal(0L, second.Sim.LoadedMillilitres);
        Assert.Equal(1, second.Pipeline.FluidLoads);
        Assert.False(second.Streamer.IsTracked(Wet));
        Assert.True(second.Streamer.IsCached(Wet), "the chunk was not cached, so the revival path is not the one under test");

        second.Streamer.MoveViewer(again, ChunkPos.Zero);
        second.Harness.Settle();

        Assert.Equal(2, second.Pipeline.FluidLoads);
        Assert.Equal(held, second.Sim.LoadedMillilitres);
        Assert.Equal(held, second.Sim.TotalMillilitres);
        Assert.False(second.Streamer.Get(Wet)!.FluidPending);
        Assert.Contains(Wet, second.Harness.MeshedPositions);
        FluidAssert.SettlesAndRests(second.Sim);
        FluidAssert.Conserves(second.Sim);
    }

    // ---------------------------------------------------------------- slice 3a: the tick owns the grid

    [Fact]
    public void AChunkThatLeftInsideTheTickComesBackAtTheBoundaryAndAFreshOneAtOnce()
    {
        // The pipeline's side of slice 3a. The whole box leaves and comes back inside one tick:
        // every forget's eviction is deferred, so the two wet panels are still in RAM and every
        // dry chunk carries a mark still in the queue — and Reclaim, which would clear it, refuses
        // mid-tick. So all 343 come back parked, not loaded to the water. A column of chunks that
        // never left then arrives with the tick still in flight and is ready and drawn at once. The
        // boundary releases the parked ones, each reclaimed with no row read, and the water is
        // exactly what it was.
        var store = new InMemoryWorldStore();
        using var session = new Session(store);
        ChunkPos poured = new(1, 1, 0);

        ViewerId viewer = session.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
        session.Harness.Settle();
        long held = session.PourAtOrigin();
        held += session.PourAtOrigin(dx: 32) - held;
        Assert.Null(session.Sim.Resume(1));
        Assert.True(session.Sim.GridOwnedByTick);
        Dictionary<VoxelPos, int> before = CellsIn(session.Sim, _ => true);

        session.Streamer.MoveViewer(viewer, FarAway);
        session.Harness.Settle();
        Assert.True(session.Sim.DeferredCount >= 343, "the forget deferred nothing");
        Assert.True(session.Sim.Grid.HasPanel(Wet) && session.Sim.Grid.HasPanel(poured), "a panel left RAM mid-tick");
        Assert.Equal(0, store.FluidWrites);

        session.Streamer.MoveViewer(viewer, ChunkPos.Zero);
        session.Harness.Settle();

        Assert.True(session.Sim.GridOwnedByTick, "the pipeline finished the tick, so nothing here was parked");
        Assert.Equal(343, session.Pipeline.FluidArrivalsWaiting);
        Assert.True(session.Streamer.Get(Wet)!.FluidPending);
        Assert.True(session.Streamer.Get(poured)!.FluidPending);
        Assert.True(session.Streamer.Get(new ChunkPos(-1, -1, -1))!.FluidPending);
        Assert.Equal(0, session.Pipeline.FluidLoads);
        Assert.Equal(0, session.Pipeline.FluidReclaims);
        int meshedBefore = session.Harness.MeshedPositions.Count;

        // Three columns of fresh chunks, never loaded before: no panel, no mark, no row.
        ChunkPos fresh = new(4, 0, 0);
        Assert.False(session.Streamer.IsTracked(fresh));
        session.Streamer.MoveViewer(viewer, new ChunkPos(3, 0, 0));
        session.Harness.Settle();

        Assert.True(session.Sim.GridOwnedByTick);
        Assert.False(session.Streamer.Get(fresh)!.FluidPending, "a fresh dry chunk waited for the boundary");
        Assert.Contains(fresh, session.Harness.MeshedPositions);
        Assert.True(session.Harness.MeshedPositions.Count > meshedBefore, "nothing drew under the tick");
        Assert.True(session.Streamer.Get(Wet)!.FluidPending);
        Assert.Equal(0, session.Pipeline.FluidReclaims);

        // The boundary: the deferred evictions land, then the parked arrivals reclaim them; the
        // parked chunks that left again in the meantime are dropped rather than reclaimed.
        session.Sim.Step();
        Assert.Equal(0, session.Sim.DeferredCount);
        session.Harness.Settle();

        Assert.Equal(0, session.Pipeline.FluidArrivalsWaiting);
        Assert.True(session.Pipeline.FluidReclaims >= 2, "the wet chunks were not reclaimed");
        Assert.Equal(0, session.Pipeline.FluidLoads);
        Assert.Equal(0L, session.Sim.LoadedMillilitres);
        Assert.Equal(0L, session.Sim.EvictedMillilitres);
        Assert.Equal(0, session.Sim.EvictingPanelCount);
        Assert.False(session.Streamer.Get(Wet)!.FluidPending);
        Assert.False(session.Streamer.Get(poured)!.FluidPending);
        Assert.False(session.Streamer.Get(new ChunkPos(0, -1, -1))!.FluidPending);

        Dictionary<VoxelPos, int> after = CellsIn(session.Sim, _ => true);
        Assert.True(
            before.Count == after.Count && before.All(kv => after.GetValueOrDefault(kv.Key) == kv.Value),
            "water moved while its chunks were away inside one tick");
        Assert.Equal(held, session.Sim.TotalMillilitres);
        Assert.Equal(0L, session.Sim.DisplacedMillilitres);
        FluidAssert.Conserves(session.Sim);
        FluidAssert.SettlesAndRests(session.Sim);
        Assert.Equal(0, session.Sim.PausedCellCount);
    }

    /// <summary>
    /// A session with Wet's row parked: read off the lane while the grid was the tick's, waiting
    /// with the arrival for the boundary. Returns the litres the row holds and the viewer.
    /// </summary>
    private static (Session Second, long Held, ViewerId Viewer) WithARowParked(GatedStore store, long chunkCacheBytes = 0)
    {
        long held;
        using (var first = new Session(store))
        {
            ViewerId viewer = first.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
            first.Harness.Settle();
            held = first.PourAtOrigin();
            first.Streamer.MoveViewer(viewer, FarAway);
            first.Harness.Settle();
            first.PumpUntil(() => first.Sim.Grid.PanelCount == 0);
            first.Harness.Settle();
        }

        Assert.True(store.FluidWrites > 0);

        var second = new Session(store, chunkCacheBytes: chunkCacheBytes);
        store.ReadGate.Reset();
        ChunkPos poured = new(1, 1, 0);

        ViewerId again = second.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
        try
        {
            second.PumpUntil(() =>
                Volatile.Read(ref store.ReadsWaiting) > 0
                && second.Streamer.Get(poured) is { HasVoxels: true, FluidPending: false });
            Assert.True(second.Streamer.Get(Wet)!.FluidPending);

            // The grid becomes the tick's while the read is held at the gate.
            second.PourAtOrigin(dx: 32);
            Assert.Null(second.Sim.Resume(1));
            Assert.True(second.Sim.GridOwnedByTick);
        }
        finally
        {
            store.ReadGate.Set();
        }

        second.Harness.Settle();

        Assert.True(second.Sim.GridOwnedByTick);
        Assert.Equal(1, second.Pipeline.FluidLoads);
        Assert.Equal(1, second.Pipeline.FluidArrivalsWaiting);
        Assert.Equal(0L, second.Sim.LoadedMillilitres);
        Assert.True(second.Streamer.Get(Wet)!.FluidPending);
        Assert.DoesNotContain(Wet, second.Harness.MeshedPositions);
        Assert.Equal(0, second.Sim.RestoredPanels);
        return (second, held, again);
    }

    [Fact]
    public void ARowLandingWhileTheTickOwnsTheGridIsParkedWithItAndRestoredAtTheBoundary()
    {
        // The other arrival path: the row comes off the lane while the grid is the tick's. It is
        // parked with the arrival — read once, restored at the boundary — and the chunk stays not
        // loaded to the water until then, so nothing can flow into a chunk whose stored water has
        // yet to land.
        var store = new GatedStore();
        (Session second, long held, _) = WithARowParked(store);
        using Session owned = second;

        second.Sim.Step();
        second.Harness.Settle();

        Assert.Equal(1, second.Pipeline.FluidLoads);
        Assert.Equal(0, second.Pipeline.FluidArrivalsWaiting);
        Assert.Equal(held, second.Sim.LoadedMillilitres);
        Assert.False(second.Streamer.Get(Wet)!.FluidPending);
        Assert.Contains(Wet, second.Harness.MeshedPositions);
        Assert.True(second.Sim.ResumedCells > 0, "the arrival woke nothing");

        FluidAssert.SettlesAndRests(second.Sim);
        Assert.Equal(0L, second.Sim.DisplacedMillilitres);
        FluidAssert.Conserves(second.Sim);
    }

    [Fact]
    public void AParkedRowForAChunkThatLeftIsDroppedNotRestored()
    {
        // The parked chunk leaves before the boundary. Its row stays the truth on disk and the
        // release drops the copy rather than restoring water into a chunk nobody views — which
        // would then be evicted and written back unchanged, a round trip with the identity exact
        // and a needless write. Coming back reads the row again. With the cache on, because that
        // was the arm expected to reach the release's "still viewed?" clause — and it does not: a
        // cached chunk is out of the streamer's tracked set, so the release finds nothing either
        // way. The clause is reachable only for a chunk retained by a job in flight, the window the
        // LoadFluidJob "leaving" branch guards, and it is belt and braces here (M4a's zeroable
        // epsilon category); what this test pins is the drop and the re-read.
        var store = new GatedStore();
        (Session second, long held, ViewerId viewer) = WithARowParked(store, chunkCacheBytes: 64L * 1024 * 1024);
        using Session owned = second;
        int writes = store.FluidWrites;

        second.Streamer.MoveViewer(viewer, FarAway);
        second.Harness.Settle();
        Assert.False(second.Streamer.IsTracked(Wet));
        Assert.True(second.Streamer.IsCached(Wet), "the chunk was not retained, so the release could not restore into it anyway");

        second.Sim.Step();
        second.Harness.Settle();

        Assert.Equal(0, second.Pipeline.FluidArrivalsWaiting);
        Assert.Equal(0, second.Sim.RestoredPanels);
        Assert.Equal(0L, second.Sim.LoadedMillilitres);
        Assert.Equal(0, second.Sim.EvictedPanels);
        Assert.Equal(writes, store.FluidWrites);

        second.Streamer.MoveViewer(viewer, ChunkPos.Zero);
        second.Harness.Settle();
        second.PumpUntil(() => second.Sim.LoadedMillilitres == held);

        Assert.Equal(2, second.Pipeline.FluidLoads);
        Assert.False(second.Streamer.Get(Wet)!.FluidPending);
        FluidAssert.SettlesAndRests(second.Sim);
        Assert.Equal(0L, second.Sim.DisplacedMillilitres);
        FluidAssert.Conserves(second.Sim);
    }

    [Fact]
    public void ASnapshotNotYetOnDiskIsTheSourceAheadOfTheStore()
    {
        // An older row on disk, a newer snapshot in a batch that has not landed, and the chunk
        // comes back: the newer one wins, with no lane trip. The store-first order would restore
        // the past with the identity exact.
        var store = new GatedStore();
        using var session = new Session(store);

        ViewerId viewer = session.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
        session.Harness.Settle();
        long first = session.PourAtOrigin();

        session.Streamer.MoveViewer(viewer, FarAway);
        session.Harness.Settle();
        session.PumpUntil(() => session.Sim.Grid.PanelCount == 0);
        session.Harness.Settle();
        Assert.Equal(0, session.Pipeline.UnwrittenFluidCount);
        Assert.True(store.FluidWrites > 0);

        // Back, loaded off the lane, and more water poured: RAM is now ahead of the row.
        session.Streamer.MoveViewer(viewer, ChunkPos.Zero);
        session.Harness.Settle();
        Assert.Equal(first, session.Sim.LoadedMillilitres);
        long second = session.PourAtOrigin(dx: 4, dy: 8, dz: 4); // beside the first, not into it
        Assert.True(second > first);

        // Leave with the write held at the gate: the snapshot is pending or in flight, not on disk.
        int wetChunks = session.Sim.Grid.PanelCount;
        Assert.True(wetChunks > 0);
        long evictedBefore = session.Sim.EvictedMillilitres;
        store.WriteGate.Reset();
        try
        {
            session.Streamer.MoveViewer(viewer, FarAway);
            session.PumpUntil(() => session.Sim.Grid.PanelCount == 0 && session.Pipeline.UnwrittenFluidCount == wetChunks);
            Assert.Equal(second, session.Sim.EvictedMillilitres - evictedBefore);

            int loadsBefore = session.Pipeline.FluidLoads;
            session.Streamer.MoveViewer(viewer, ChunkPos.Zero);
            session.PumpUntil(() => session.Sim.TotalMillilitres == second);

            Assert.Equal(wetChunks, session.Pipeline.FluidLoadsFromMemory);
            Assert.Equal(loadsBefore, session.Pipeline.FluidLoads);
            Assert.Equal(second, session.Sim.TotalMillilitres);
            Assert.Equal(first + second, session.Sim.LoadedMillilitres);
        }
        finally
        {
            // The dispose writes synchronously through this gate; left shut, a failed assertion
            // above would read as a hang.
            store.WriteGate.Set();
        }

        session.Harness.Settle();
        Assert.Equal(0, session.Pipeline.UnwrittenFluidCount);
        FluidAssert.Conserves(session.Sim);
        FluidAssert.SettlesAndRests(session.Sim);
    }

    [Fact]
    public void WaterWaitsAtTheFaceOfAChunkWhoseRowIsPending()
    {
        // The gate on the real pipeline: while the wet chunk's row is held on the lane, water
        // poured in the chunk above it cannot fall in — the chunk is not loaded, to the water —
        // and when the row lands the arrival's face wake lets it. Without the gate the water
        // falls into the chunk's air first and the restore meets it, which throws.
        var store = new GatedStore();
        long held;
        using (var first = new Session(store))
        {
            ViewerId viewer = first.Streamer.AddViewer(Wet, 1, 1);
            first.Harness.Settle();
            held = first.PourAtOrigin();
            first.Streamer.MoveViewer(viewer, FarAway);
            first.Harness.Settle();
            first.PumpUntil(() => first.Sim.Grid.PanelCount == 0);
            first.Harness.Settle();
        }

        ChunkPos above = new(0, 2, 0);
        using var second = new Session(store);
        store.ReadGate.Reset();
        long poured;
        try
        {
            second.Streamer.AddViewer(Wet, 1, 1);
            second.PumpUntil(() => Volatile.Read(ref store.ReadsWaiting) > 0);
            Assert.True(second.Streamer.Get(Wet)!.FluidPending);
            Assert.NotNull(second.Streamer.Get(above)?.Voxels);

            // Four cells at the bottom of the chunk above, over the pending one.
            VoxelPos over = new(16, 64, 16);
            poured = 0;
            for (int up = 0; up < 4; up++)
            {
                Assert.Equal(0L, second.Sim.Add(over.Offset(0, up, 0), Cap));
                poured += Cap;
            }

            for (int tick = 0; tick < 12; tick++)
            {
                second.Sim.Step();
                second.Pipeline.Update();
            }

            Assert.True(second.Streamer.Get(Wet)!.FluidPending, "the row landed before the gate opened");
            Assert.Equal(0L, WaterIn(second.Sim, Wet));
            Assert.False(second.Sim.Grid.HasPanel(Wet));
            Assert.Equal(poured, WaterIn(second.Sim, above));
            Assert.Equal(poured, second.Sim.TotalMillilitres);
            Assert.Equal(0L, second.Sim.DisplacedMillilitres);
        }
        finally
        {
            store.ReadGate.Set();
        }

        second.Harness.Settle();
        Assert.Equal(held, second.Sim.LoadedMillilitres);
        Assert.Equal(held + poured, second.Sim.TotalMillilitres);

        // The water above is what the gate held up, and the air under it is all in the wet chunk:
        // once the row has landed none of it stays where it was. Not "more water in the wet
        // chunk" — what the restore put there was mid-flow and drains out of it once woken.
        Assert.Equal(poured, WaterIn(second.Sim, above));
        FluidAssert.SettlesAndRests(second.Sim);
        Assert.Equal(0L, WaterIn(second.Sim, above));
        Assert.Equal(0L, second.Sim.DisplacedMillilitres);
        FluidAssert.Conserves(second.Sim);
    }

    [Fact]
    public void AChunkBackInsideTheEvictionWindowKeepsItsPanel()
    {
        using var session = new Session(new InMemoryWorldStore(), chunkCacheBytes: 64L * 1024 * 1024);

        ViewerId viewer = session.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
        session.Harness.Settle();
        long held = session.PourAtOrigin();

        // Away and straight back, with no tick in between: the panel never left.
        session.Streamer.MoveViewer(viewer, FarAway);
        session.Harness.Settle();
        Assert.True(session.Sim.EvictingPanelCount > 0);
        session.Streamer.MoveViewer(viewer, ChunkPos.Zero);
        session.Harness.Settle();

        Assert.Equal(1, session.Pipeline.FluidReclaims);
        Assert.Equal(0, session.Pipeline.FluidLoads);
        Assert.Equal(0, session.Sim.EvictingPanelCount);
        Assert.Equal(0L, session.Sim.EvictedMillilitres);
        Assert.Equal(held, session.Sim.TotalMillilitres);
        Assert.True(session.Sim.Grid.HasPanel(Wet));
        Assert.False(session.Streamer.Get(Wet)!.FluidPending);

        // And the tick after finds nothing to release.
        session.Sim.Step();
        session.Pipeline.Update();
        Assert.True(session.Sim.Grid.HasPanel(Wet));
        FluidAssert.SettlesAndRests(session.Sim);
        FluidAssert.Conserves(session.Sim);
    }

    [Fact]
    public void AFluidRowThatWillNotReadFaultsRatherThanPlayingBesideAMissingLake()
    {
        var store = new InMemoryWorldStore();
        store.Write([], [], [(Wet, [1, 2, 3])]);

        using var session = new Session(store);
        session.Streamer.AddViewer(ChunkPos.Zero, 1, 1);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (session.Pipeline.FaultedJobs == 0)
        {
            session.Pipeline.Update();
            Assert.True(clock.ElapsedMilliseconds < 30_000, "no job faulted");
            Thread.Sleep(1);
        }

        // Let everything else land, without the harness's own no-faults assertion.
        while (!session.Pipeline.IsQuiescent && clock.ElapsedMilliseconds < 30_000)
        {
            session.Pipeline.Update();
            Thread.Sleep(1);
        }

        // Reported every time it is tried: a faulted job is not retried by itself, but a neighbour
        // landing pushes the chunk again, as it does a faulted generate. Every report is the same
        // row refusing to read.
        Assert.NotEmpty(session.Harness.Faults);
        Assert.All(session.Harness.Faults, fault => Assert.Contains("BlobFormatException", fault));
        Assert.Equal(0L, session.Sim.LoadedMillilitres);
        Assert.True(session.Streamer.Get(Wet)!.FluidPending, "the chunk was let through without its water");
        Assert.DoesNotContain(Wet, session.Harness.MeshedPositions);
        Assert.True(session.Harness.MeshedPositions.Count > 0, "nothing else meshed either");
    }

    // ---------------------------------------------------------------- the lifecycle fuzz, third arm

    private static WorldGenerator Generator(ulong seed) =>
        new(
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
            new StrataTable([new StrataLayer(1, 1), new StrataLayer(2, 3)], 3));

    [Theory]
    [InlineData(0x5EEDUL, false)]
    [InlineData(0xA11CEUL, false)]
    [InlineData(0xBEEFUL, false)]
    [InlineData(0xC0FFEEUL, false)]
    [InlineData(1UL, false)]
    [InlineData(0x5EEDUL, true)]
    [InlineData(0xA11CEUL, true)]
    [InlineData(0xBEEFUL, true)]
    [InlineData(0xC0FFEEUL, true)]
    [InlineData(1UL, true)]
    public void AcrossRandomUnloadsWithEvictionTheWaterComesBackCellForCellAndNoFrontIsStranded(ulong seed, bool viaJob)
    {
        // M8b-4a's lifecycle fuzz with the panels leaving: a chunk toggled out is evicted, its
        // snapshot goes to a pending set and, at random, to the store; a chunk toggled back is
        // reclaimed, restored from the pending set, or restored from the store, in that order;
        // quits write everything and reopen only the loaded chunks. What was in a chunk when it
        // left is what is in it when it is back, cell for cell; nothing is displaced; and with
        // everything back the automaton rests and the rest is real — which is the assertion the
        // stranded front fails. The twin is the 4a arm: the same toggles, no eviction.
        WorldGenerator generator = Generator(seed);
        int surface = generator.SurfaceHeightAt(0, 0);
        const int minX = -16, maxX = 15, minZ = -16, maxZ = 15;
        int minY = surface - 24, maxY = surface + 7;

        var inner = new GeneratedTerrain(generator, minX, minY, minZ, maxX, maxY, maxZ);
        var toggled = new LoadableTerrain(inner);

        // viaJob drives every tick of the subject through the job path on this thread (slice 3b):
        // a "slice" is a tick begun and not yet run, and finishing it is running and collecting
        // it, so every seam the sync arm reaches mid-tick is reached with a job out instead.
        FluidTickJob? held = null;
        void Tick(FluidSimulation s)
        {
            if (!viaJob) { s.Step(); return; }
            if (held is not null) { held.Run(CancellationToken.None); held.OnResult(JobOutcome.Completed, null); held = null; return; }
            FluidTickJob job = s.BeginTick(toggled, ChunkPos.Zero);
            job.Run(CancellationToken.None);
            job.OnResult(JobOutcome.Completed, null);
        }

        void Slice(FluidSimulation s)
        {
            if (!viaJob) { s.Resume(1); return; }
            held = s.BeginTick(toggled, ChunkPos.Zero);
        }

        void Finish(FluidSimulation s)
        {
            if (s.GridOwnedByTick) Tick(s);
        }

        var twinInner = new GeneratedTerrain(generator, minX, minY, minZ, maxX, maxY, maxZ);
        var twinToggled = new LoadableTerrain(twinInner);
        var sim = new FluidSimulation(toggled, fallCells: 1);
        var twin = new FluidSimulation(twinToggled, fallCells: 1);
        var store = new InMemoryWorldStore();
        var pending = new Dictionary<ChunkPos, FluidPanelSnapshot>();

        ChunkPos lo = new VoxelPos(minX, minY, minZ).Chunk;
        ChunkPos hi = new VoxelPos(maxX, maxY, maxZ).Chunk;
        var chunks = new List<ChunkPos>();
        for (int cx = lo.X; cx <= hi.X; cx++)
        for (int cy = lo.Y; cy <= hi.Y; cy++)
        for (int cz = lo.Z; cz <= hi.Z; cz++)
        {
            chunks.Add(new ChunkPos(cx, cy, cz));
        }

        var random = new Random((int)(seed & 0x7FFFFFFF));
        var away = new Dictionary<ChunkPos, Dictionary<VoxelPos, int>>();
        long offered = 0, rejected = 0, twinRejected = 0;
        int unloadsWithAwake = 0, evictions = 0, reclaims = 0, fromPending = 0, fromStore = 0, quits = 0;

        void Flush()
        {
            if (pending.Count == 0) return;
            store.Write([], [], [.. pending.Values.Select(p => (p.Chunk, FluidPanelBlob.Write(p)))]);
            pending.Clear();
        }

        void Bring(FluidSimulation into, ChunkPos chunk)
        {
            toggled.Load(chunk);
            if (into.Reclaim(chunk))
            {
                reclaims++;
            }
            else if (pending.TryGetValue(chunk, out FluidPanelSnapshot? unwritten))
            {
                into.Restore(unwritten);
                fromPending++;
            }
            else if (store.ReadFluid(chunk) is { } blob)
            {
                into.Restore(FluidPanelBlob.Read(blob, chunk));
                fromStore++;
            }

            into.ChunkArrived(chunk);

            if (away.Remove(chunk, out Dictionary<VoxelPos, int>? expected))
            {
                Dictionary<VoxelPos, int> actual = CellsIn(into, c => c == chunk);
                Assert.True(
                    expected.Count == actual.Count && expected.All(kv => actual.GetValueOrDefault(kv.Key) == kv.Value),
                    $"seed {seed:X}: {chunk} came back with different water than it left with");
            }
        }

        // Chunks whose arrival came while the grid was the tick's, the way the pipeline parks one
        // (slice 3a): not loaded to the water until the boundary, brought on the first frame after.
        var parked = new HashSet<ChunkPos>();
        int parkedArrivals = 0, deferredEvicts = 0;

        for (int round = 0; round < 80; round++)
        {
            // A pour is a boundary call (slice 3a): a round that left a tick in flight finishes
            // it first, the way the game's relay waits for the boundary.
            Finish(sim);
            if (twin.TickInProgress) twin.Step();

            if (random.Next(3) != 0)
            {
                int x = random.Next(minX, maxX + 1);
                int z = random.Next(minZ, maxZ + 1);
                VoxelPos? top = inner.TopOpening(x, z);
                if (top is { } cell && toggled.IsLoaded(cell))
                {
                    offered += Cap;
                    rejected += sim.Add(cell, Cap);
                    twinRejected += twin.Add(cell, Cap);
                }
            }

            if (random.Next(2) == 0)
            {
                var v = new VoxelPos(random.Next(minX, maxX + 1), random.Next(minY, maxY + 1), random.Next(minZ, maxZ + 1));
                if (toggled.IsLoaded(v) && !inner.CanHold(v) && toggled.Dig(v))
                {
                    twinToggled.Dig(v);
                    sim.Disturb(v);
                    twin.Disturb(v);
                }
            }

            int ticks = random.Next(1, 9);
            for (int t = 0; t < ticks; t++)
            {
                Tick(sim);
                twin.Step();
            }

            if (random.Next(4) == 0)
            {
                sim.LevelSheets();
                twin.LevelSheets();
            }

            if (random.Next(6) == 0)
            {
                Slice(sim);
                twin.Resume(1);
            }

            // The pipeline's frame: parked arrivals first, then whatever tick has passed for a
            // marked chunk — neither while a tick is in flight, which is the pipeline's own rule
            // (slice 3a), so a mid-tick round leaves both for the next.
            var evicted = new List<FluidPanelSnapshot>();
            if (!sim.GridOwnedByTick)
            {
                foreach (ChunkPos chunk in parked) Bring(sim, chunk);
                parked.Clear();

                sim.TakeEvicted(evicted);
                foreach (FluidPanelSnapshot panel in evicted)
                {
                    pending[panel.Chunk] = panel;
                    evictions++;
                    Assert.False(sim.GridUnchecked.HasPanel(panel.Chunk));
                }
            }

            if (random.Next(2) == 0) Flush();

            // While away, in RAM or not, not a millilitre moves.
            foreach ((ChunkPos chunk, Dictionary<VoxelPos, int> snapshot) in away)
            {
                if (!sim.GridUnchecked.HasPanel(chunk))
                {
                    continue;
                }

                Dictionary<VoxelPos, int> current = CellsIn(sim, c => c == chunk);
                Assert.True(
                    snapshot.Count == current.Count && snapshot.All(kv => current.GetValueOrDefault(kv.Key) == kv.Value),
                    $"seed {seed:X}, round {round}: water in unloaded {chunk} changed while its panel lingered");
            }

            Assert.Equal(0L, sim.DisplacedMillilitres);
            Assert.Equal(0L, twin.DisplacedMillilitres);
            FluidAssert.Conserves(sim, rejected);
            FluidAssert.Conserves(twin, twinRejected);

            List<ChunkPos> wet = sim.GridUnchecked.HeldCells().Select(c => c.Chunk).Distinct().Where(chunks.Contains).ToList();
            ChunkPos target = wet.Count > 0 && random.Next(10) < 7 ? wet[random.Next(wet.Count)] : chunks[random.Next(chunks.Count)];
            if (toggled.IsChunkLoaded(target))
            {
                if (AwakeIn(sim, target).Count > 0) unloadsWithAwake++;
                away[target] = CellsIn(sim, c => c == target);
                toggled.Unload(target);
                twinToggled.Unload(target);
                if (sim.GridOwnedByTick) deferredEvicts++;
                sim.Evict(target);
            }
            else if (sim.GridOwnedByTick)
            {
                // The pipeline's parking: the chunk stays unloaded to the water until the boundary.
                if (parked.Add(target)) parkedArrivals++;
                twinToggled.Load(target);
                twin.ChunkArrived(target);
            }
            else
            {
                Bring(sim, target);
                twinToggled.Load(target);
                twin.ChunkArrived(target);
            }

            if (random.Next(5) == 0)
            {
                // The quit: the tick in flight finished first, as Main does (slice 3a); then
                // everything pending and everything in RAM to the store; a fresh simulation; only
                // the loaded chunks come back, each from its row. A parked chunk is not loaded to
                // the water and is not one of them — its row is the truth, unread.
                Finish(sim);
                parked.Clear();
                Flush();
                List<ChunkPos> loaded = chunks.Where(toggled.IsChunkLoaded).ToList();
                int awakeLoaded = sim.AwakeHeldCells().Count(c => loaded.Contains(c.Chunk));
                Dictionary<VoxelPos, int> before = CellsIn(sim, loaded.Contains);

                var all = new List<FluidPanelSnapshot>();
                sim.TakeAllDirty(all);
                store.Write([], [], [.. all.Select(p => (p.Chunk, FluidPanelBlob.Write(p)))]);

                // What was evicting is on disk now and its RAM copy goes with the old simulation.
                foreach (ChunkPos chunk in chunks) toggled.Unload(chunk);
                var reopened = new FluidSimulation(toggled, fallCells: 1);
                foreach (ChunkPos chunk in loaded)
                {
                    toggled.Load(chunk);
                    if (store.ReadFluid(chunk) is { } blob) reopened.Restore(FluidPanelBlob.Read(blob, chunk));
                }

                Dictionary<VoxelPos, int> after = CellsIn(reopened, loaded.Contains);
                Assert.True(
                    before.Count == after.Count && before.All(kv => after.GetValueOrDefault(kv.Key) == kv.Value),
                    $"seed {seed:X}, round {round}: the loaded chunks came back from the quit with different water");
                Assert.Equal(awakeLoaded, reopened.PausedCellCount);

                foreach (ChunkPos chunk in loaded) reopened.ChunkArrived(chunk);

                sim = reopened;
                held = null;
                rejected = 0;
                quits++;
            }
        }

        Finish(sim);
        foreach (ChunkPos chunk in chunks)
        {
            if (!toggled.IsChunkLoaded(chunk))
            {
                Bring(sim, chunk);
                twinToggled.Load(chunk);
                twin.ChunkArrived(chunk);
            }
        }

        Assert.Empty(away);
        Assert.True(parkedArrivals + deferredEvicts > 0, $"seed {seed:X}: no chunk was ever toggled mid-tick");

        // The stranded-front oracle: with everything back both arms rest, and the rest is real.
        FluidAssert.SettlesAndRests(sim);
        FluidAssert.SettlesAndRests(twin);
        FluidAssert.Conserves(sim, rejected);
        FluidAssert.Conserves(twin, twinRejected);
        Assert.Equal(0, sim.PausedCellCount);
        Assert.Equal(0L, sim.DisplacedMillilitres);
        Assert.Equal(offered, twin.AddedMillilitres + twinRejected);

        Assert.True(sim.TotalMillilitres > 0, "no water survived to the end");
        Assert.True(evictions > 0, $"seed {seed:X}: nothing was ever evicted");
        Assert.True(unloadsWithAwake > 0, $"seed {seed:X}: no chunk ever left with a front in it");
        Assert.True(fromPending + fromStore > 0, $"seed {seed:X}: nothing was ever restored");
        Assert.True(quits >= 1, $"seed {seed:X}: no quit");
        Assert.True(reclaims >= 0);
    }
}
