using Overburden.Core.Content;
using Overburden.Core.Fluid;
using Overburden.Core.Persistence;
using Overburden.Core.Streaming;
using Overburden.Core.Voxels;
using Overburden.Core.Worldgen;

namespace Overburden.Core.Tests;

/// <summary>
/// M8b-4b: the water survives a quit — the grid's dirty bit, the simulation's snapshot and
/// restore, the pipeline carrying the panels on the chunk batch, and the twin oracle across a
/// quit: the same millilitres in the same cells, and the same cells awake.
/// </summary>
/// <remarks>
/// <para>
/// <b>Break-recipes filed before the code, each with the test that catches it</b>: a write that
/// does not dirty the panel — <see cref="APanelIsDirtyFromItsFirstWriteUntilSnapshotted"/>; a
/// snapshot that does not clear it, or a restore that dirties — the same test and
/// <see cref="ARestoredPanelIsCleanAndCountedAsLoaded"/>; a drained panel whose row keeps the
/// lake — <see cref="ADrainedPanelWritesItselfEmpty"/>; the awake list missing the queued or the
/// unvisited half of a tick — <see cref="TakeAllDirtyListsEveryAwakeHeldCellExactlyOnce"/>; a
/// restore that wakes nothing or wakes into an unloaded chunk —
/// <see cref="ARestoredPanelsAwakeCellsArePausedUntilTheChunkArrives"/>; the panels written on
/// their own call rather than the chunk's batch — <see cref="APanelRidesTheChunkBatchAtUnview"/>;
/// a quit that skips <c>TakeAllDirty</c> — <see cref="SaveAllTakesEveryPanelWorthWriting"/>; a
/// failed save losing the snapshots — <see cref="AFailedSaveKeepsThePanels"/>; and every one of
/// them at once across the fuzz, whose restored grid must equal the one before the quit cell for
/// cell.
/// </para>
/// </remarks>
public class FluidPersistenceTests
{
    private const int Cap = FluidConstants.CellCapacityMillilitres;

    private static readonly ChunkPos West = new(0, 0, 0);
    private static readonly ChunkPos East = new(1, 0, 0);

    private const int Floor = 8;
    private const int Lane = 5;

    private static VoxelPos At(int x, int up = 0) => new(x, Floor + up, Lane);

    private static LoadableTerrain Corridor(int minX = 24, int maxX = 39, int height = 4) =>
        new(new BoxTerrain(minX, Floor, Lane, maxX, Floor + height - 1, Lane));

    private static Dictionary<VoxelPos, int> Cells(FluidSimulation sim)
    {
        var cells = new Dictionary<VoxelPos, int>();
        foreach (VoxelPos cell in sim.Grid.HeldCells()) cells[cell] = sim.Grid.AmountAt(cell);
        return cells;
    }

    private static void AssertSameWater(FluidSimulation expected, FluidSimulation actual, string when)
    {
        Dictionary<VoxelPos, int> before = Cells(expected);
        Dictionary<VoxelPos, int> after = Cells(actual);
        Assert.True(
            before.Count == after.Count && before.All(kv => after.GetValueOrDefault(kv.Key) == kv.Value),
            $"{when}: {before.Count} cells before against {after.Count} after, or an amount differs");
        Assert.Equal(expected.TotalMillilitres, actual.TotalMillilitres);
    }

    /// <summary>Writes every panel worth writing to the store, the way a quit does.</summary>
    private static int Quit(FluidSimulation sim, IWorldStore store)
    {
        var pending = new List<FluidPanelSnapshot>();
        sim.TakeAllDirty(pending);
        store.Write([], [], [.. pending.Select(p => (p.Chunk, FluidPanelBlob.Write(p)))]);
        return pending.Count;
    }

    /// <summary>Reads every row back into a fresh simulation, the way an open does.</summary>
    private static FluidSimulation Reopen(IWorldStore store, IFluidTerrain terrain)
    {
        var sim = new FluidSimulation(terrain);
        foreach ((ChunkPos position, byte[] blob) in store.ReadFluid())
        {
            sim.Restore(FluidPanelBlob.Read(blob, position));
        }

        return sim;
    }

    // ---------------------------------------------------------------- the grid

    [Fact]
    public void APanelIsDirtyFromItsFirstWriteUntilSnapshotted()
    {
        var grid = new FluidGrid();
        Assert.False(grid.IsDirty(West));
        Assert.Empty(grid.DirtyChunks());

        grid.Fill(At(30), 100);
        Assert.True(grid.IsDirty(West));
        Assert.Equal([West], grid.DirtyChunks());

        (ushort, ushort)[] cells = grid.TakeSnapshot(West);
        Assert.Equal([((ushort)At(30).LocalIndex, (ushort)100)], cells);
        Assert.False(grid.IsDirty(West));
        Assert.Empty(grid.DirtyChunks());

        // A second snapshot of a clean panel is still the panel — the pipeline may ask for a
        // chunk's panel again on a later unview — and does not dirty it.
        Assert.Equal(cells, grid.TakeSnapshot(West));
        Assert.False(grid.IsDirty(West));

        grid.Transfer(At(30), At(31), 40);
        Assert.True(grid.IsDirty(West));
        Assert.Equal(2, grid.TakeSnapshot(West).Length);
    }

    [Fact]
    public void ASnapshotIsAscendingAndCopiedRatherThanAliased()
    {
        var grid = new FluidGrid();
        grid.Fill(At(29), 7);
        grid.Fill(At(26), 9);
        grid.Fill(At(30, 2), 11);

        (ushort Index, ushort Millilitres)[] cells = grid.TakeSnapshot(West);
        Assert.Equal(3, cells.Length);
        Assert.True(cells[0].Index < cells[1].Index && cells[1].Index < cells[2].Index);

        // The tick writes on. The copy the save job holds must not move with it.
        grid.Drain(At(26), 9);
        Assert.Contains(cells, c => c.Index == At(26).LocalIndex && c.Millilitres == 9);
    }

    [Fact]
    public void ADrainedPanelWritesItselfEmpty()
    {
        // A lake stored, then drained: the panel is released, and without this the row on disk
        // would still hold the lake and the next open would put it back — water from nothing,
        // and the identity would not see it because loaded would rise with held.
        var grid = new FluidGrid();
        grid.Fill(At(30), 500);
        grid.TakeSnapshot(West);

        grid.Drain(At(30), 500);
        Assert.Equal(0, grid.PanelCount);
        Assert.True(grid.IsDirty(West), "a released panel that had been stored must be written empty");
        Assert.Equal([West], grid.DirtyChunks());

        Assert.Empty(grid.TakeSnapshot(West));
        Assert.False(grid.IsDirty(West));
        Assert.Empty(grid.DirtyChunks());
    }

    [Fact]
    public void APanelReleasedBeforeItWasEverStoredIsNotRemembered()
    {
        // Water that came and went between two saves has no row to empty. Writing one would be
        // harmless and pointless; not writing one is what keeps the fluid table to chunks that
        // have actually held water at a save.
        var grid = new FluidGrid();
        grid.Fill(At(30), 500);
        grid.Drain(At(30), 500);
        Assert.False(grid.IsDirty(West));
        Assert.Empty(grid.DirtyChunks());
    }

    [Fact]
    public void ARestoredPanelIsCleanAndCountedAsLoaded()
    {
        var grid = new FluidGrid();
        long restored = grid.Restore(West, [((ushort)At(30).LocalIndex, 100), ((ushort)At(31).LocalIndex, Cap)]);

        Assert.Equal(100L + Cap, restored);
        Assert.Equal(100L + Cap, grid.TotalMillilitres);
        Assert.Equal(2, grid.HeldCellCount);
        Assert.Equal(100, grid.AmountAt(At(30)));
        Assert.Equal(Cap, grid.AmountAt(At(31)));

        // What came off the disk is on the disk: a restore that changed nothing costs no row.
        Assert.False(grid.IsDirty(West));

        // But a restored panel that later drains must empty its row, so it counts as stored.
        grid.Drain(At(30), 100);
        grid.Drain(At(31), Cap);
        Assert.True(grid.IsDirty(West));
    }

    [Fact]
    public void ARestoreIntoAChunkThatHoldsWaterIsRefused()
    {
        var grid = new FluidGrid();
        grid.Fill(At(30), 1);
        Assert.Throws<InvalidOperationException>(() => grid.Restore(West, [((ushort)At(31).LocalIndex, 1)]));
    }

    // ---------------------------------------------------------------- the simulation

    [Fact]
    public void TakeDirtyListsThePausedCellsAndOnlyThem()
    {
        LoadableTerrain terrain = Corridor();
        var sim = new FluidSimulation(terrain);

        sim.Add(At(33, 2), Cap);
        sim.Step();
        terrain.Unload(East);
        sim.Step(); // pauses what is awake in the east

        Assert.True(sim.PausedCellCount > 0, "the fixture paused nothing");

        FluidPanelSnapshot? east = sim.TakeDirty(East);
        Assert.NotNull(east);
        Assert.Equal(sim.PausedCellCount, east.Awake.Length);
        foreach (ushort index in east.Awake)
        {
            Assert.Contains(east.Cells, c => c.Index == index);
        }

        Assert.Null(sim.TakeDirty(East));
        Assert.Null(sim.TakeDirty(new ChunkPos(5, 5, 5)));
    }

    [Fact]
    public void TakeAllDirtyListsEveryAwakeHeldCellExactlyOnce()
    {
        // Three places an awake cell can be — paused, queued for the next tick, unvisited in the
        // tick in progress — and a cell can be in two at once. Each clause is made load-bearing
        // by a cell that is in that place only: the east is paused only, because a paused cell is
        // never re-queued; the west's spreading water is put mid-tick with one visit made, so all
        // but the cells that one visit woke are unvisited only; and a pour after the tick began is
        // queued only. The cells the one visit woke are both unvisited and queued, and must be
        // listed once.
        LoadableTerrain terrain = Corridor();
        var sim = new FluidSimulation(terrain);

        sim.Add(At(37, 2), Cap);
        for (int i = 0; i < 3; i++) sim.Step();
        terrain.Unload(East);
        sim.Step();
        Assert.True(sim.PausedCellCount > 0, "the fixture paused nothing");
        Assert.Equal(0, sim.ActiveCellCount);

        sim.Add(At(24), Cap);
        for (int i = 0; i < 3; i++) sim.Step();
        Assert.True(sim.ActiveCellCount > 2, "the fixture has too little in motion");

        Assert.Null(sim.Resume(1));
        Assert.True(sim.RemainingInTick > 0, "the fixture left nothing unvisited");
        sim.Add(At(28, 3), Cap);

        // The oracle: every held cell in the west is in motion (a point pour three ticks old has
        // no cell at rest yet), so awake held = every west held cell plus the paused east ones.
        var expected = new HashSet<VoxelPos>(sim.Grid.HeldCells());
        Assert.Contains(At(28, 3), expected);
        Assert.True(expected.Count(c => c.Chunk == East) == sim.PausedCellCount);

        var into = new List<FluidPanelSnapshot>();
        sim.TakeAllDirty(into);

        var listed = new HashSet<VoxelPos>();
        foreach (FluidPanelSnapshot panel in into)
        {
            for (int i = 1; i < panel.Awake.Length; i++)
            {
                Assert.True(panel.Awake[i] > panel.Awake[i - 1], "awake indices must strictly ascend");
            }

            VoxelPos origin = panel.Chunk.OriginVoxel;
            foreach (ushort index in panel.Awake)
            {
                Assert.Contains(panel.Cells, c => c.Index == index);
                (int lx, int ly, int lz) = VoxelPos.FromIndex(index);
                Assert.True(listed.Add(new VoxelPos(origin.X + lx, origin.Y + ly, origin.Z + lz)));
            }
        }

        Assert.Equal(expected, listed);
        Assert.Equal(expected.Count, sim.AwakeHeldCells().Count());

        // And the tick in progress was left exactly where it was.
        Assert.True(sim.TickInProgress);
    }

    [Fact]
    public void ARestoredPanelsAwakeCellsArePausedUntilTheChunkArrives()
    {
        // The restore wakes nothing: at open no chunk is loaded, so a woken cell would only be
        // paused again at its next visit, at the cost of the visit. The arrival is what wakes it,
        // through the hook that wakes a paused flood — one mechanism for the load and the revival.
        LoadableTerrain terrain = Corridor();
        terrain.Unload(West);
        terrain.Unload(East);
        var sim = new FluidSimulation(terrain);

        ushort top = (ushort)At(33, 3).LocalIndex;
        ushort floor = (ushort)At(33).LocalIndex;
        var panel = new FluidPanelSnapshot(East, [.. new[] { (floor, (ushort)Cap), (top, (ushort)1000) }.OrderBy(c => c.Item1)], [top]);
        sim.Restore(panel);

        Assert.Equal(1000L + Cap, sim.TotalMillilitres);
        Assert.Equal(1000L + Cap, sim.LoadedMillilitres);
        Assert.Equal(1, sim.RestoredPanels);
        Assert.Equal(0, sim.ActiveCellCount);
        Assert.Equal(1, sim.PausedCellCount);
        Assert.Equal(1, sim.PausedChunkCount);
        Assert.Equal(0L, sim.AddedMillilitres);

        // Nothing moves while the chunk is away, however many ticks run.
        for (int i = 0; i < 5; i++) sim.Step();
        Assert.Equal(1000, sim.Grid.AmountAt(At(33, 3)));
        Assert.Equal(0L, sim.DisplacedMillilitres);

        terrain.Load(East);
        terrain.Load(West);
        sim.ChunkArrived(East);
        sim.ChunkArrived(West);
        Assert.Equal(0, sim.PausedCellCount);
        Assert.True(sim.ActiveCellCount > 0, "the arrival woke nothing");

        FluidAssert.SettlesAndRests(sim);
        Assert.Equal(0, sim.Grid.AmountAt(At(33, 3)));
        FluidAssert.Conserves(sim);
        Assert.Equal(1000L + Cap, sim.TotalMillilitres);
    }

    [Fact]
    public void ARestoredPanelStoredAsleepStaysAsleepAndIsNotWrittenAgain()
    {
        // The played lake: a million cells at rest. Restoring them must not wake them, and the
        // next save must not rewrite them — a restore is not a write.
        // A corridor that ends at the chunk face, so a full floor is a level sheet with nowhere
        // to go; a corridor running on into the east would make the "lake" a wall of water.
        LoadableTerrain terrain = Corridor(24, 31);
        terrain.Unload(West);
        var sim = new FluidSimulation(terrain);

        var cells = new List<(ushort, ushort)>();
        for (int x = 24; x < 32; x++) cells.Add(((ushort)At(x).LocalIndex, (ushort)Cap));
        cells.Sort();
        sim.Restore(new FluidPanelSnapshot(West, [.. cells], []));

        terrain.Load(West);
        sim.ChunkArrived(West);

        // The face wake reaches the cells on the chunk's ±X faces only, by design; those visits
        // find rest and write nothing.
        FluidAssert.SettlesAndRests(sim);

        var into = new List<FluidPanelSnapshot>();
        sim.TakeAllDirty(into);
        Assert.Empty(into);
    }

    // ---------------------------------------------------------------- the pipeline

    /// <summary>A fluid source the pipeline tests drive by hand.</summary>
    private sealed class FakeFluid : IFluidPersistence
    {
        public Dictionary<ChunkPos, FluidPanelSnapshot> Dirty { get; } = [];

        public List<ChunkPos> TakeDirtyCalls { get; } = [];

        public int TakeAllDirtyCalls { get; private set; }

        public List<ChunkPos> Evicted { get; } = [];

        public List<FluidPanelSnapshot> Restored { get; } = [];

        public HashSet<ChunkPos> Home { get; } = [];

        /// <summary>Marks the chunk; the fake releases at the next <see cref="TakeEvicted"/>, with no tick to wait for.</summary>
        public void Evict(ChunkPos chunk)
        {
            Evicted.Add(chunk);
            TakeDirtyCalls.Add(chunk);
        }

        public void TakeEvicted(List<FluidPanelSnapshot> into)
        {
            foreach (ChunkPos chunk in Evicted)
            {
                if (Dirty.Remove(chunk, out FluidPanelSnapshot? panel)) into.Add(panel);
            }

            Evicted.Clear();
        }

        public bool Reclaim(ChunkPos chunk) => Evicted.Remove(chunk) || Home.Contains(chunk);

        public void Restore(FluidPanelSnapshot panel)
        {
            Restored.Add(panel);
            Home.Add(panel.Chunk);
        }

        public void TakeAllDirty(List<FluidPanelSnapshot> into)
        {
            TakeAllDirtyCalls++;
            into.AddRange(Dirty.Values);
            Dirty.Clear();
        }
    }

    private static FluidPanelSnapshot SomePanel(ChunkPos chunk) =>
        new(chunk, [(100, 500), (200, Cap)], [200]);

    private static readonly ChunkPos FarAway = new(200, 0, 200);

    [Fact]
    public void APanelRidesTheChunkBatchAtUnview()
    {
        var store = new InMemoryWorldStore();
        using var harness = new PipelineHarness(store: store);
        var fluid = new FakeFluid();
        harness.Pipeline.Fluid = fluid;

        ViewerId viewer = harness.Pipeline.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
        harness.Settle();

        // An edit, so the chunk's own blob is written, and a dirty panel for the same chunk.
        VoxelPos at = new(16, 16, 16);
        Assert.True(harness.Pipeline.SetVoxel(at, 4));
        fluid.Dirty[ChunkPos.Zero] = SomePanel(ChunkPos.Zero);

        harness.Pipeline.Streamer.MoveViewer(viewer, FarAway);
        harness.Settle();

        // Asked once per leaving chunk, and the one dirty panel went out in the same batch as
        // the chunk blob: one write call carrying both.
        Assert.Contains(ChunkPos.Zero, fluid.TakeDirtyCalls);
        Assert.Equal(1, store.WriteCalls);
        Assert.Equal(1, store.ChunkWrites);
        Assert.Equal(1, store.FluidWrites);
        Assert.Equal(1, harness.Pipeline.PanelsSaved);

        (ChunkPos position, byte[] blob) = Assert.Single(store.ReadFluid());
        Assert.Equal(ChunkPos.Zero, position);
        FluidPanelSnapshot read = FluidPanelBlob.Read(blob, ChunkPos.Zero);
        Assert.Equal(SomePanel(ChunkPos.Zero).Cells, read.Cells);
        Assert.Equal([200], read.Awake);
    }

    [Fact]
    public void SaveAllTakesEveryPanelWorthWriting()
    {
        var store = new InMemoryWorldStore();
        using var harness = new PipelineHarness(store: store);
        var fluid = new FakeFluid();
        harness.Pipeline.Fluid = fluid;

        harness.Pipeline.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
        harness.Settle();

        // Panels for chunks that are not even loaded — a quit writes the paused lake too.
        fluid.Dirty[new ChunkPos(50, 0, 50)] = SomePanel(new ChunkPos(50, 0, 50));
        fluid.Dirty[new ChunkPos(51, 0, 50)] = SomePanel(new ChunkPos(51, 0, 50));

        Assert.Equal((0, 0), harness.Pipeline.SaveAll());
        Assert.Equal(1, fluid.TakeAllDirtyCalls);
        Assert.Equal(2, harness.Pipeline.PanelsSaved);
        Assert.Equal(2, store.FluidWrites);
        Assert.Equal(2, store.ReadFluid().Count);

        // Nothing dirty: nothing written, and the store not touched beyond a flush.
        Assert.Equal((0, 0), harness.Pipeline.SaveAll());
        Assert.Equal(2, store.FluidWrites);
        Assert.Equal(1, store.WriteCalls);
    }

    [Fact]
    public void DisposingThePipelineWritesThePanels()
    {
        var store = new InMemoryWorldStore();
        var fluid = new FakeFluid();
        using (var harness = new PipelineHarness(store: store))
        {
            harness.Pipeline.Fluid = fluid;
            harness.Pipeline.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
            harness.Settle();
            fluid.Dirty[new ChunkPos(9, 0, 9)] = SomePanel(new ChunkPos(9, 0, 9));
        }

        Assert.Equal(1, store.FluidWrites);
    }

    private sealed class ThrowingStore : PersistenceTests.InMemoryWorldStoreBase
    {
        public bool Broken { get; set; } = true;

        public override void Write(
            IReadOnlyList<(ChunkPos Position, byte[] Blob)> chunks,
            IReadOnlyList<(int X, int Z, byte[] Blob)> columns,
            IReadOnlyList<(ChunkPos Position, byte[] Blob)> fluid)
        {
            if (Broken) throw new IOException("the disk is on fire");
            base.Write(chunks, columns, fluid);
        }
    }

    [Fact]
    public void AFailedSaveKeepsThePanels()
    {
        // The synchronous path: SaveAll throws, and the snapshots — copies nobody can re-dirty,
        // because the grid already cleared its bit at the copy — must stay on the pending list
        // or the water is gone with the failure.
        var store = new ThrowingStore();
        using var harness = new PipelineHarness(store: store);
        var fluid = new FakeFluid();
        harness.Pipeline.Fluid = fluid;

        harness.Pipeline.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
        harness.Settle();

        fluid.Dirty[new ChunkPos(9, 0, 9)] = SomePanel(new ChunkPos(9, 0, 9));
        Assert.Equal((0, 0), harness.Pipeline.SaveAll());
        Assert.Equal(1, harness.Pipeline.FailedSaves);
        Assert.Equal(1, harness.Pipeline.PendingSaves);
        Assert.Equal(0, harness.Pipeline.PanelsSaved);

        store.Broken = false;
        harness.Pipeline.SaveAll();
        Assert.Equal(1, harness.Pipeline.PanelsSaved);
        Assert.Equal(0, harness.Pipeline.PendingSaves);
        Assert.Single(store.ReadFluid());
    }

    [Fact]
    public void AFailedBatchSaveRetriesThePanels()
    {
        // The asynchronous twin: a batch on the serial lane that faults puts its panels back,
        // and they land once the store recovers, with nothing re-dirtying them.
        var store = new ThrowingStore();
        using var harness = new PipelineHarness(store: store);
        var fluid = new FakeFluid();
        harness.Pipeline.Fluid = fluid;

        ViewerId viewer = harness.Pipeline.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
        harness.Settle();
        fluid.Dirty[ChunkPos.Zero] = SomePanel(ChunkPos.Zero);

        harness.Pipeline.Streamer.MoveViewer(viewer, FarAway);
        for (int i = 0; i < 500 && harness.Pipeline.FailedSaves == 0; i++)
        {
            harness.Pipeline.Update();
            Thread.Sleep(1);
        }

        Assert.True(harness.Pipeline.FailedSaves > 0, "the save should have faulted");
        Assert.Empty(store.ReadFluid());

        store.Broken = false;
        for (int i = 0; i < 500 && harness.Pipeline.PanelsSaved == 0; i++)
        {
            harness.Pipeline.Update();
            Thread.Sleep(1);
        }

        Assert.Equal(1, harness.Pipeline.PanelsSaved);
        Assert.Single(store.ReadFluid());
    }

    [Fact]
    public void WithoutAFluidSourceNothingWetIsEverWritten()
    {
        var store = new InMemoryWorldStore();
        using var harness = new PipelineHarness(store: store);
        ViewerId viewer = harness.Pipeline.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
        harness.Settle();
        Assert.True(harness.Pipeline.SetVoxel(new VoxelPos(16, 16, 16), 4));
        harness.Pipeline.Streamer.MoveViewer(viewer, FarAway);
        harness.Settle();
        harness.Pipeline.SaveAll();

        Assert.Equal(0, store.FluidWrites);
        Assert.Empty(store.ReadFluid());
    }

    // ---------------------------------------------------------------- end to end, real terrain

    [Fact]
    public void WaterPouredIntoTheRealStreamerSurvivesAQuitAndIsWokenByTheArrival()
    {
        // Main's path in Core's terms: the automaton over the real streamer, a pour, the chunk
        // leaves, the pipeline is disposed (which writes), a second pipeline over the same store,
        // the rows restored before any chunk arrives, and the arrival waking what was stored
        // awake. The wire between the pipeline's arrival hook and the restore is the join every
        // other test here trusts.
        var store = new InMemoryWorldStore();
        MaterialTable materials = MaterialTable.ForTesting(
            isOpaque: [false, true, true, true],
            isFluid: [false, false, false, false],
            textureLayer: [0, 1, 2, 3],
            hardness: [0f, 0.8f, 3.0f, 50f]);

        long held;
        int awake;
        VoxelPos poured;
        FluidSimulation? first = null;
        using (var harness = new PipelineHarness(store: store, onFluidReady: (p, _) => first?.ChunkArrived(p)))
        {
            first = new FluidSimulation(new VoxelFluidTerrain(harness.Pipeline.Streamer, materials));
            harness.Pipeline.Fluid = first;

            ViewerId viewer = harness.Pipeline.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
            harness.Settle();

            // Somewhere holdable: the air above the surface of the chunk column at the origin.
            StreamedChunk top = harness.Pipeline.Streamer.Get(new ChunkPos(0, 1, 0))!;
            Assert.NotNull(top.Voxels);
            poured = new VoxelPos(16, 40, 16);
            Assert.True(first.Grid.TotalMillilitres == 0);
            for (int up = 0; up < 4; up++)
            {
                Assert.Equal(0L, first.Add(poured.Offset(0, up, 0), Cap));
            }
            first.Step();
            Assert.True(first.ActiveCellCount > 0);

            held = first.TotalMillilitres;
            harness.Pipeline.Streamer.MoveViewer(viewer, FarAway);
            harness.Settle();
            first.Step(); // pauses the awake water in the chunks that left
            awake = first.PausedCellCount;
            Assert.True(awake > 0, "the walk paused nothing");
            Assert.Equal(0L, first.DisplacedMillilitres);
        }

        Assert.True(store.FluidWrites > 0, "the dispose wrote no panel");

        // M8b-4c: the open reads nothing but the index; each row arrives with its chunk, off the
        // lane, and the arrival wake fires only once it has (onFluidReady, not onVoxelsReady).
        FluidSimulation? second = null;
        int arrivals = 0;
        using (var harness = new PipelineHarness(store: store, onFluidReady: (p, _) => { arrivals++; second?.ChunkArrived(p); }))
        {
            second = new FluidSimulation(new VoxelFluidTerrain(harness.Pipeline.Streamer, materials));
            harness.Pipeline.Fluid = second;

            Assert.Equal(0L, second.TotalMillilitres);
            Assert.True(harness.Pipeline.SavedFluidCount > 0, "the open found no fluid rows to index");

            harness.Pipeline.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
            harness.Settle();
            Assert.True(arrivals > 0);
            Assert.True(harness.Pipeline.FluidLoads > 0, "no row came off the lane");
            Assert.Equal(held, second.LoadedMillilitres);
            Assert.Equal(awake, second.ResumedCells);
            Assert.Equal(0, second.PausedCellCount);
            Assert.True(second.ActiveCellCount > 0, "the arrival woke nothing");

            FluidAssert.SettlesAndRests(second);
            Assert.Equal(held, second.TotalMillilitres);
            Assert.Equal(0L, second.DisplacedMillilitres);
            FluidAssert.Conserves(second);
        }
    }

    // ---------------------------------------------------------------- the twin across a quit

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
    [InlineData(0x5EEDUL)]
    [InlineData(0xA11CEUL)]
    [InlineData(0xBEEFUL)]
    [InlineData(0xC0FFEEUL)]
    [InlineData(1UL)]
    public void AcrossRandomUnloadsAndQuitsTheWaterComesBackCellForCell(ulong seed)
    {
        // M8b-4a's lifecycle fuzz with quits in it: every few rounds the simulation is written to
        // a store the way a quit writes it, a fresh simulation is opened from the store with
        // every chunk away, and the chunks that were loaded arrive. Right after the restore the
        // grid must equal the one before the quit cell for cell, with the same number of cells
        // awake; at the end, with everything back, both arms rest and the rest is real, and the
        // sum of every quit's loaded water accounts for the identity. The twin never quits.
        WorldGenerator generator = Generator(seed);
        int surface = generator.SurfaceHeightAt(0, 0);
        const int minX = -16, maxX = 15, minZ = -16, maxZ = 15;
        int minY = surface - 24, maxY = surface + 7;

        var inner = new GeneratedTerrain(generator, minX, minY, minZ, maxX, maxY, maxZ);
        var toggled = new LoadableTerrain(inner);
        var control = new LoadableTerrain(new GeneratedTerrain(generator, minX, minY, minZ, maxX, maxY, maxZ));
        var sim = new FluidSimulation(toggled);
        var twin = new FluidSimulation(control);
        var store = new InMemoryWorldStore();

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
        long offered = 0, rejected = 0, twinRejected = 0;
        int quits = 0, quitsWithAwake = 0, quitsWithAway = 0, panelsWritten = 0;

        for (int round = 0; round < 80; round++)
        {
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

            // Sometimes mid-tick, because an autosave can land there and the quit path finishes
            // nothing first.
            if (random.Next(6) == 0)
            {
                sim.Resume(1);
            }

            Assert.Equal(0L, sim.DisplacedMillilitres);
            Assert.Equal(0L, twin.DisplacedMillilitres);
            FluidAssert.Conserves(sim, rejected);
            FluidAssert.Conserves(twin, twinRejected);

            // Toggle a chunk, leaning toward wet ones.
            List<ChunkPos> wet = sim.Grid.HeldCells().Select(c => c.Chunk).Distinct().Where(chunks.Contains).ToList();
            ChunkPos target = wet.Count > 0 && random.Next(10) < 7 ? wet[random.Next(wet.Count)] : chunks[random.Next(chunks.Count)];
            if (toggled.IsChunkLoaded(target))
            {
                toggled.Unload(target);
            }
            else
            {
                toggled.Load(target);
                sim.ChunkArrived(target);
            }

            if (random.Next(5) == 0)
            {
                // The quit. Finish nothing: whatever is mid-tick stays mid-tick in the counts.
                int awakeHeld = AwakeHeld(sim);
                List<ChunkPos> loaded = chunks.Where(toggled.IsChunkLoaded).ToList();
                bool anyAway = loaded.Count < chunks.Count;

                int wrote = Quit(sim, store);
                panelsWritten += wrote;

                foreach (ChunkPos chunk in chunks) toggled.Unload(chunk);
                FluidSimulation reopened = Reopen(store, toggled);

                AssertSameWater(sim, reopened, $"seed {seed:X}, round {round}, quit {quits}");
                Assert.Equal(awakeHeld, reopened.PausedCellCount);
                Assert.Equal(0, reopened.ActiveCellCount);
                Assert.Equal(reopened.TotalMillilitres, reopened.LoadedMillilitres);

                foreach (ChunkPos chunk in loaded)
                {
                    toggled.Load(chunk);
                    reopened.ChunkArrived(chunk);
                }

                sim = reopened;
                rejected = 0;
                quits++;
                if (awakeHeld > 0) quitsWithAwake++;
                if (anyAway) quitsWithAway++;
            }
        }

        foreach (ChunkPos chunk in chunks)
        {
            if (!toggled.IsChunkLoaded(chunk))
            {
                toggled.Load(chunk);
                sim.ChunkArrived(chunk);
            }
        }

        FluidAssert.SettlesAndRests(sim);
        FluidAssert.SettlesAndRests(twin);
        FluidAssert.Conserves(sim, rejected);
        FluidAssert.Conserves(twin, twinRejected);
        Assert.Equal(0, sim.PausedCellCount);
        Assert.Equal(0L, sim.DisplacedMillilitres);
        Assert.Equal(offered, twin.AddedMillilitres + twinRejected);

        // A final quit and reopen with everything at rest: nothing awake, the same cells, and the
        // next save writes nothing at all.
        Quit(sim, store);
        foreach (ChunkPos chunk in chunks) toggled.Unload(chunk);
        FluidSimulation final = Reopen(store, toggled);
        AssertSameWater(sim, final, $"seed {seed:X}, final");
        Assert.Equal(0, final.PausedCellCount);
        var nothing = new List<FluidPanelSnapshot>();
        final.TakeAllDirty(nothing);
        Assert.Empty(nothing);

        Assert.True(sim.TotalMillilitres > 0, "no water survived to the end");
        Assert.True(quits >= 3, $"seed {seed:X}: only {quits} quit(s)");
        Assert.True(quitsWithAwake > 0, $"seed {seed:X}: no quit ever had awake water");
        Assert.True(quitsWithAway > 0, $"seed {seed:X}: no quit ever had a chunk away");
        Assert.True(panelsWritten > 0, $"seed {seed:X}: no panel was ever written");

        // Then drain one stored chunk dry and quit again: its row must come back empty rather
        // than holding the water the earlier quit stored — deterministic here rather than left to
        // the rounds above, where a seed can go eighty rounds without draining a stored panel.
        foreach (ChunkPos chunk in chunks) toggled.Load(chunk);
        ChunkPos drained = final.Grid.HeldCells().First().Chunk;
        long taken = 0;
        foreach (VoxelPos cell in final.Grid.HeldCells().Where(c => c.Chunk == drained).ToList())
        {
            taken += final.Remove(cell, Cap);
        }

        Assert.True(taken > 0);
        Assert.Equal(0, final.Grid.HeldCells().Count(c => c.Chunk == drained));
        Quit(final, store);

        (ChunkPos position, byte[] blob) = Assert.Single(store.ReadFluid(), r => r.Position == drained);
        Assert.Empty(FluidPanelBlob.Read(blob, position).Cells);
        foreach (ChunkPos chunk in chunks) toggled.Unload(chunk);
        FluidSimulation afterDrain = Reopen(store, toggled);
        AssertSameWater(final, afterDrain, $"seed {seed:X}, after the drain");
        Assert.Equal(0, afterDrain.Grid.HeldCells().Count(c => c.Chunk == drained));
    }

    /// <summary>What the blob must list awake, by the simulation's own enumeration; the round trip is what is under test.</summary>
    private static int AwakeHeld(FluidSimulation sim) => sim.AwakeHeldCells().Count();
}
