using Overburden.Core.Content;
using Overburden.Core.Lighting;
using Overburden.Core.Persistence;
using Overburden.Core.Voxels;
using Overburden.Core.Worldgen;
using Overburden.Persistence;

namespace Overburden.Core.Tests;

/// <summary>
/// One suite, run against every <see cref="IWorldStore"/> there is.
/// </summary>
/// <remarks>
/// <para>
/// <b>The in-memory store exists so M6b-2's pipeline tests need no disk, and that is exactly what
/// makes it dangerous.</b> M1 built <c>InMemoryContentSource</c> for the same reason and it
/// drifted — M3c found it had no grass, so a pile of content tests had been passing against a
/// world the game does not ship, and nothing said so until <c>ShippedContentTests</c> was written
/// to read the real files. A store that drifts is worse than a content source that drifts: the
/// tests it would quietly invalidate are the ones standing between a defect and permanent data
/// loss.
/// </para>
/// <para>
/// So both implementations answer the same questions here. A behaviour that only the fake has is
/// a fake that has stopped predicting the real thing.
/// </para>
/// </remarks>
public abstract class WorldStoreContractTests : IDisposable
{
    private readonly List<IDisposable> _open = [];

    protected abstract IWorldStore CreateStore();

    /// <summary>Reopens the same underlying world, or returns a fresh store if it cannot.</summary>
    /// <remarks>
    /// Only the SQLite store can genuinely reopen; the in-memory one has nothing to reopen from.
    /// <see cref="SurvivesAReopen"/> is therefore overridden rather than inherited, and it says so
    /// out loud instead of quietly passing on a store that cannot possibly fail it.
    /// </remarks>
    protected virtual bool CanReopen => false;

    protected virtual IWorldStore Reopen(IWorldStore store) =>
        throw new NotSupportedException("This store cannot be reopened.");

    protected IWorldStore Track(IWorldStore store)
    {
        _open.Add(store);
        return store;
    }

    public virtual void Dispose()
    {
        foreach (IDisposable open in _open) open.Dispose();
        GC.SuppressFinalize(this);
    }

    private static readonly ChunkPos A = new(3, -2, 7);
    private static readonly ChunkPos B = new(-9, 0, 4);

    protected static byte[] Bytes(params int[] values) => [.. values.Select(v => (byte)v)];

    // -----------------------------------------------------------------------------------------
    // The header.
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void ANewWorldHasNoHeader()
    {
        // Null is how "new world" is detected, and it is the only place that question is asked.
        // A store that returned an empty array instead would make every world look like an
        // existing one with a corrupt header.
        Assert.Null(Track(CreateStore()).ReadWorldHeader());
    }

    [Fact]
    public void TheHeaderRoundTrips()
    {
        IWorldStore store = Track(CreateStore());
        store.WriteWorldHeader(Bytes(1, 2, 3, 4, 5));

        Assert.Equal(Bytes(1, 2, 3, 4, 5), store.ReadWorldHeader());
    }

    [Fact]
    public void TheHeaderIsReplacedRatherThanAppended()
    {
        // A player installing a mod grows the ID table, and the header is rewritten at open.
        // Appending would leave two tables and the reader would take the first.
        IWorldStore store = Track(CreateStore());
        store.WriteWorldHeader(Bytes(1, 2, 3));
        store.WriteWorldHeader(Bytes(9, 9));

        Assert.Equal(Bytes(9, 9), store.ReadWorldHeader());
    }

    // -----------------------------------------------------------------------------------------
    // Chunks and columns.
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void AnAbsentChunkReadsAsNull()
    {
        IWorldStore store = Track(CreateStore());

        Assert.Null(store.ReadChunk(A));
        Assert.Null(store.ReadColumn(0, 0));
    }

    [Fact]
    public void ChunksAndColumnsRoundTrip()
    {
        IWorldStore store = Track(CreateStore());

        store.Write(
            [(A, Bytes(10, 11)), (B, Bytes(20, 21, 22))],
            [(1, 2, Bytes(30)), (-4, 5, Bytes(40, 41))]);

        Assert.Equal(Bytes(10, 11), store.ReadChunk(A));
        Assert.Equal(Bytes(20, 21, 22), store.ReadChunk(B));
        Assert.Equal(Bytes(30), store.ReadColumn(1, 2));
        Assert.Equal(Bytes(40, 41), store.ReadColumn(-4, 5));
    }

    [Fact]
    public void NegativeCoordinatesAreDistinctKeys()
    {
        // Half the world has them, and a key built by packing three ints into one with a bad
        // shift collapses (−9, 0, 4) onto something else. Cheap to check, and the symptom
        // otherwise is one chunk's terrain appearing in another's place.
        IWorldStore store = Track(CreateStore());

        var positions = new[]
        {
            new ChunkPos(1, 2, 3), new ChunkPos(-1, 2, 3), new ChunkPos(1, -2, 3),
            new ChunkPos(1, 2, -3), new ChunkPos(-1, -2, -3), new ChunkPos(0, 0, 0),
        };

        store.Write([.. positions.Select((p, i) => (p, Bytes(i)))], []);

        for (int i = 0; i < positions.Length; i++)
        {
            Assert.Equal(Bytes(i), store.ReadChunk(positions[i]));
        }

        Assert.Equal(positions.Length, store.LoadChunkIndex().Count);
    }

    [Fact]
    public void WritingTheSameKeyTwiceReplaces()
    {
        // The normal case: a chunk saved, revisited, edited and saved again. A store that
        // inserted a second row would either throw on the primary key or start handing back
        // whichever row the query happened to reach first.
        IWorldStore store = Track(CreateStore());

        store.Write([(A, Bytes(1))], [(0, 0, Bytes(1))]);
        store.Write([(A, Bytes(2, 2))], [(0, 0, Bytes(2, 2))]);

        Assert.Equal(Bytes(2, 2), store.ReadChunk(A));
        Assert.Equal(Bytes(2, 2), store.ReadColumn(0, 0));
        Assert.Single(store.LoadChunkIndex());
        Assert.Single(store.LoadColumnIndex());
    }

    [Fact]
    public void AnEmptyWriteIsHarmless()
    {
        // A quiet frame's flush. It has to be free rather than an error, because the caller does
        // not know whether anything was dirty until it looks.
        IWorldStore store = Track(CreateStore());

        store.Write([], []);

        Assert.Empty(store.LoadChunkIndex());
        Assert.Empty(store.LoadColumnIndex());
    }

    [Fact]
    public void ALargeBlobRoundTripsIntact()
    {
        // 64 KB is direct storage, the biggest a chunk can be, and it is past SQLite's default
        // page size — so this is the case that exercises overflow pages rather than an inline
        // value.
        IWorldStore store = Track(CreateStore());

        var big = new byte[VoxelConstants.ChunkVolume * sizeof(ushort)];
        new Random(7).NextBytes(big);

        store.Write([(A, big)], []);

        Assert.Equal(big, store.ReadChunk(A));
    }

    [Fact]
    public void AStoredBlobIsNotAliasedToTheCallersArray()
    {
        // A real store copies on the way in; an in-memory one that handed back the caller's own
        // array would let a later edit silently rewrite history, and every round-trip test would
        // still pass. This is the specific way a fake stops predicting the real thing.
        IWorldStore store = Track(CreateStore());

        var blob = Bytes(1, 2, 3);
        store.Write([(A, blob)], []);
        blob[0] = 99;

        Assert.Equal(Bytes(1, 2, 3), store.ReadChunk(A));

        byte[] read = store.ReadChunk(A)!;
        read[0] = 77;
        Assert.Equal(Bytes(1, 2, 3), store.ReadChunk(A));
    }

    // -----------------------------------------------------------------------------------------
    // The index, which is what keeps the generate path free of I/O.
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void TheIndexReportsExactlyWhatWasWritten()
    {
        IWorldStore store = Track(CreateStore());

        store.Write([(A, Bytes(1)), (B, Bytes(2))], [(1, 2, Bytes(3))]);

        // Ordered because neither store promises an enumeration order and neither should — the
        // caller builds a set out of this. B's X is −9, so it sorts first.
        Assert.Equal([B, A], store.LoadChunkIndex().OrderBy(p => p.X).ToArray());
        Assert.Equal([(1, 2)], store.LoadColumnIndex().ToArray());
    }

    [Fact]
    public void TheIndexIsEmptyForANewWorld()
    {
        IWorldStore store = Track(CreateStore());

        Assert.Empty(store.LoadChunkIndex());
        Assert.Empty(store.LoadColumnIndex());
    }

    [Fact]
    public void TheIndexAgreesWithWhatCanBeRead()
    {
        // The property that matters: consulting the index and then reading must never come back
        // empty-handed. A chunk in the index with no blob is the load path deciding not to
        // generate and then getting nothing — which is a chunk-shaped hole in the world.
        IWorldStore store = Track(CreateStore());

        var random = new Random(11);
        var written = new List<(ChunkPos, byte[])>();
        for (int i = 0; i < 50; i++)
        {
            written.Add((
                new ChunkPos(random.Next(-40, 40), random.Next(-8, 8), random.Next(-40, 40)),
                Bytes(i)));
        }

        store.Write(written, []);

        foreach (ChunkPos position in store.LoadChunkIndex())
        {
            Assert.NotNull(store.ReadChunk(position));
        }
    }

    // -----------------------------------------------------------------------------------------
    // Through the real blob format, so the store is exercised by what will actually use it.
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void ARealChunkAndItsColumnSurviveTheStore()
    {
        IWorldStore store = Track(CreateStore());

        var generator = new WorldGenerator(
            new WorldGenSettings { Seed = 12345 },
            new StrataTable([new StrataLayer(1, 1), new StrataLayer(2, 3)], 3));

        VoxelChunk chunk = generator.GenerateTerrain(A);
        chunk.Set(4, 5, 6, MaterialIds.AirNumericId);

        var column = new SkyColumn(A.X, A.Z);
        column.Seed(generator);
        column.SetOpenAboveY(0, 0, -77);

        store.Write(
            [(A, ChunkBlob.Write(A, chunk))],
            [(A.X, A.Z, SkyColumnBlob.Write(column))]);

        MaterialTable materials = MaterialTable.ForTesting(
            isOpaque: [false, true, true, true],
            isFluid: new bool[4],
            textureLayer: new int[4],
            hardness: [0f, 1f, 1f, 1f]);

        VoxelChunk restored = ChunkBlob.Read(store.ReadChunk(A)!, A, materials);
        SkyColumn restoredColumn = SkyColumnBlob.Read(store.ReadColumn(A.X, A.Z)!, A.X, A.Z);

        for (int i = 0; i < VoxelConstants.ChunkVolume; i++)
        {
            Assert.Equal(chunk.GetAt(i), restored.GetAt(i));
        }

        Assert.Equal(-77, restoredColumn.OpenAboveY(0, 0));
        Assert.True(restoredColumn.HasEdits);
    }

    // -----------------------------------------------------------------------------------------
    // The third list: fluid panels (M8b-4b).
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void ANewWorldHasNoFluid() => Assert.Empty(Track(CreateStore()).ReadFluid());

    [Fact]
    public void FluidPanelsRoundTripWholeAndKeyedByChunk()
    {
        IWorldStore store = Track(CreateStore());

        store.Write([], [], [(A, Bytes(1, 2, 3)), (B, Bytes(4))]);

        List<(ChunkPos Position, byte[] Blob)> rows = [.. store.ReadFluid().OrderBy(r => r.Position.X)];
        Assert.Equal(2, rows.Count);
        Assert.Equal(B, rows[0].Position);
        Assert.Equal(Bytes(4), rows[0].Blob);
        Assert.Equal(A, rows[1].Position);
        Assert.Equal(Bytes(1, 2, 3), rows[1].Blob);
    }

    [Fact]
    public void TheFluidIndexReportsExactlyWhatWasWrittenEmptyRowsIncluded()
    {
        // M8b-4c: the open reads the index, not the rows, and an empty row — a drained panel —
        // stays in it, because a load of one costs a point query and restores nothing.
        IWorldStore store = Track(CreateStore());
        Assert.Empty(store.LoadFluidIndex());

        store.Write([], [], [(A, Bytes(1, 2, 3)), (B, Bytes())]);

        Assert.Equal(new HashSet<ChunkPos> { A, B }, store.LoadFluidIndex().ToHashSet());
    }

    [Fact]
    public void AFluidRowReadsByChunkAndAnAbsentOneReadsAsNull()
    {
        IWorldStore store = Track(CreateStore());
        Assert.Null(store.ReadFluid(A));

        store.Write([], [], [(A, Bytes(7, 8)), (B, Bytes(9))]);

        Assert.Equal(Bytes(7, 8), store.ReadFluid(A));
        Assert.Equal(Bytes(9), store.ReadFluid(B));
        Assert.Null(store.ReadFluid(new ChunkPos(-3, 2, 11)));

        // A copy, not the stored array: the same rule as ReadChunk.
        byte[] first = store.ReadFluid(A)!;
        first[0] = 0;
        Assert.Equal(Bytes(7, 8), store.ReadFluid(A));
    }

    [Fact]
    public void WritingTheSameFluidKeyTwiceReplaces()
    {
        IWorldStore store = Track(CreateStore());

        store.Write([], [], [(A, Bytes(1))]);
        store.Write([], [], [(A, Bytes(2, 2))]);

        (ChunkPos position, byte[] blob) = Assert.Single(store.ReadFluid());
        Assert.Equal(A, position);
        Assert.Equal(Bytes(2, 2), blob);
    }

    [Fact]
    public void AStoredFluidBlobIsNotAliasedToTheCallersArray()
    {
        IWorldStore store = Track(CreateStore());

        byte[] blob = Bytes(9, 9);
        store.Write([], [], [(A, blob)]);
        blob[0] = 0;

        Assert.Equal(Bytes(9, 9), Assert.Single(store.ReadFluid()).Blob);
    }

    [Fact]
    public void AllThreeListsLandInOneWrite()
    {
        IWorldStore store = Track(CreateStore());

        store.Write([(A, Bytes(1))], [(1, 2, Bytes(2))], [(A, Bytes(3))]);

        Assert.Equal(Bytes(1), store.ReadChunk(A));
        Assert.Equal(Bytes(2), store.ReadColumn(1, 2));
        Assert.Equal(Bytes(3), Assert.Single(store.ReadFluid()).Blob);
    }

    [Fact]
    public void AWriteThatFailsOnItsFluidLeavesNoChunkBehind()
    {
        // The reason the fluid rides the chunk batch at all: a chunk written and its water not
        // is, after a crash between them, rock over a hole or water inside rock. A null blob is
        // the one bad entry both stores refuse — a NOT NULL column in SQLite, a check up front in
        // memory — and the chunk that went in ahead of it must not be there afterwards.
        IWorldStore store = Track(CreateStore());

        Assert.ThrowsAny<Exception>(() => store.Write([(A, Bytes(1))], [(1, 2, Bytes(2))], [(B, null!)]));

        Assert.Null(store.ReadChunk(A));
        Assert.Null(store.ReadColumn(1, 2));
        Assert.Empty(store.ReadFluid());
    }

    [Fact]
    public void AFluidPanelSurvivesAReopen()
    {
        if (!CanReopen) return;

        IWorldStore store = Track(CreateStore());
        store.Write([], [], [(A, Bytes(5, 6, 7))]);

        IWorldStore reopened = Track(Reopen(store));
        (ChunkPos position, byte[] blob) = Assert.Single(reopened.ReadFluid());
        Assert.Equal(A, position);
        Assert.Equal(Bytes(5, 6, 7), blob);
    }

    [Fact]
    public void ARealPanelSurvivesTheStore()
    {
        IWorldStore store = Track(CreateStore());

        var panel = new FluidPanelSnapshot(A, [(10, 100), (11, 15625), (4000, 3)], [11]);
        store.Write([], [], [(A, FluidPanelBlob.Write(panel))]);

        FluidPanelSnapshot restored = FluidPanelBlob.Read(Assert.Single(store.ReadFluid()).Blob, A);
        Assert.Equal(panel.Cells, restored.Cells);
        Assert.Equal(panel.Awake, restored.Awake);
    }

    // -----------------------------------------------------------------------------------------
    // Lifetime.
    // -----------------------------------------------------------------------------------------

    [Fact]
    public void BasinsRoundTrip()
    {
        IWorldStore store = Track(CreateStore());
        Assert.Empty(store.ReadBasins());

        store.WriteBasins(
            [
                new StoredBasin(16, -32, 4000),
                new StoredBasin(-48, 96, 1),
            ]);

        Assert.Equal(
            [
                new StoredBasin(-48, 96, 1),
                new StoredBasin(16, -32, 4000),
            ],
            store.ReadBasins().OrderBy(basin => basin.PitX).ToArray());
    }

    [Fact]
    public void WritingBasinsReplacesRatherThanMerges()
    {
        // The ledger in memory is the truth, so a write is the whole of it. Merging instead would
        // leave a basin that has drained holding water forever, and only on disk.
        IWorldStore store = Track(CreateStore());

        store.WriteBasins([new StoredBasin(0, 0, 1000), new StoredBasin(16, 0, 2000)]);
        store.WriteBasins([new StoredBasin(16, 0, 500)]);

        StoredBasin only = Assert.Single(store.ReadBasins());
        Assert.Equal(new StoredBasin(16, 0, 500), only);
    }

    [Fact]
    public void WritingNoBasinsClearsThem()
    {
        // Which is exactly what a world opened with a different watershed lattice asks for.
        IWorldStore store = Track(CreateStore());

        store.WriteBasins([new StoredBasin(32, 32, 9000)]);
        store.WriteBasins([]);

        Assert.Empty(store.ReadBasins());
    }

    [Fact]
    public void ABasinSurvivesAReopen()
    {
        if (!CanReopen)
        {
            return;
        }

        IWorldStore store = Track(CreateStore());
        store.WriteBasins([new StoredBasin(-16, 240, 123_456)]);
        store.Flush();

        IWorldStore reopened = Track(Reopen(store));
        StoredBasin only = Assert.Single(reopened.ReadBasins());
        Assert.Equal(new StoredBasin(-16, 240, 123_456), only);
    }

    // -----------------------------------------------------------------------------------------
    // The session on the batch (M10a-1): machines, basins and the header in one transaction.
    // -----------------------------------------------------------------------------------------

    private static readonly VoxelPos O1 = new(4, 8, -12);
    private static readonly VoxelPos O2 = new(-64, 0, 32);

    [Fact]
    public void ANewWorldHasNoMachines() => Assert.Empty(Track(CreateStore()).ReadMachines());

    [Fact]
    public void MachinesRoundTripWholeAndKeyedByOrigin()
    {
        IWorldStore store = Track(CreateStore());

        store.Write(new WorldBatch
        {
            Machines = [new StoredMachine(O1, 3, Bytes(1, 2)), new StoredMachine(O2, 5, Bytes(3))],
        });

        StoredMachine[] rows = [.. store.ReadMachines().OrderBy(row => row.Origin.X)];
        Assert.Equal(2, rows.Length);
        Assert.Equal(new StoredMachine(O2, 5, Bytes(3)), rows[0]);
        Assert.Equal(new StoredMachine(O1, 3, Bytes(1, 2)), rows[1]);
    }

    [Fact]
    public void WritingTheMachinesReplacesTheTable()
    {
        // The set in memory is the truth and a dismantled machine has to leave the disk: fewer
        // rows the second time deletes, and a row at the same origin is replaced, not merged.
        IWorldStore store = Track(CreateStore());

        store.Write(new WorldBatch
        {
            Machines = [new StoredMachine(O1, 3, Bytes(1)), new StoredMachine(O2, 5, Bytes(2))],
        });
        store.Write(new WorldBatch { Machines = [new StoredMachine(O1, 3, Bytes(9))] });

        StoredMachine only = Assert.Single(store.ReadMachines());
        Assert.Equal(new StoredMachine(O1, 3, Bytes(9)), only);
    }

    [Fact]
    public void WritingNoMachinesClearsThem()
    {
        IWorldStore store = Track(CreateStore());

        store.Write(new WorldBatch { Machines = [new StoredMachine(O1, 3, Bytes(1))] });
        store.Write(new WorldBatch { Machines = [] });

        Assert.Empty(store.ReadMachines());
    }

    [Fact]
    public void AStoredMachineBlobIsNotAliasedToTheCallersArray()
    {
        IWorldStore store = Track(CreateStore());
        byte[] blob = Bytes(1, 2, 3);

        store.Write(new WorldBatch { Machines = [new StoredMachine(O1, 3, blob)] });
        blob[0] = 99;

        Assert.Equal(Bytes(1, 2, 3), Assert.Single(store.ReadMachines()).Blob);
    }

    [Fact]
    public void ASessionlessBatchLeavesTheMachinesBasinsAndHeaderAlone()
    {
        // The unload path's batch carries chunks and panels and none of the session, and must not
        // touch what it does not have: null means leave it, and only an empty list clears.
        IWorldStore store = Track(CreateStore());

        store.Write(new WorldBatch
        {
            Header = Bytes(7),
            Basins = [new StoredBasin(0, 0, 100)],
            Machines = [new StoredMachine(O1, 3, Bytes(1))],
        });
        store.Write([(A, Bytes(1))], [(1, 2, Bytes(2))], [(A, Bytes(3))]);

        Assert.Equal(Bytes(7), store.ReadWorldHeader());
        Assert.Equal(100L, Assert.Single(store.ReadBasins()).Litres);
        Assert.Single(store.ReadMachines());
        Assert.Equal(Bytes(1), store.ReadChunk(A));
    }

    [Fact]
    public void AllSixListsLandInOneWrite()
    {
        IWorldStore store = Track(CreateStore());

        store.Write(new WorldBatch
        {
            Chunks = [(A, Bytes(1))],
            Columns = [(1, 2, Bytes(2))],
            Fluid = [(A, Bytes(3))],
            Header = Bytes(4),
            Basins = [new StoredBasin(8, 8, 5)],
            Machines = [new StoredMachine(O1, 6, Bytes(6))],
        });

        Assert.Equal(Bytes(1), store.ReadChunk(A));
        Assert.Equal(Bytes(2), store.ReadColumn(1, 2));
        Assert.Equal(Bytes(3), Assert.Single(store.ReadFluid()).Blob);
        Assert.Equal(Bytes(4), store.ReadWorldHeader());
        Assert.Equal(new StoredBasin(8, 8, 5), Assert.Single(store.ReadBasins()));
        Assert.Equal(new StoredMachine(O1, 6, Bytes(6)), Assert.Single(store.ReadMachines()));
    }

    [Fact]
    public void AWriteThatFailsOnItsMachinesLeavesTheHeaderBasinsAndChunksUnchanged()
    {
        // The store slice's first test. A recipe start moves voxels from the player's inventory
        // (the header) into a station's stock (a row), and a placement debits the header for a
        // body whose row is the machine table — so a crash after the header and before the rows
        // duplicates or deletes authored state. A null blob among the machines is the one bad
        // entry both stores refuse; the header that went in ahead of it, in the same batch, and
        // everything written before the batch, must read as if the batch never happened.
        IWorldStore store = Track(CreateStore());

        store.Write(new WorldBatch
        {
            Chunks = [(A, Bytes(1))],
            Header = Bytes(1),
            Basins = [new StoredBasin(0, 0, 1000)],
            Machines = [new StoredMachine(O1, 3, Bytes(1))],
        });

        Assert.ThrowsAny<Exception>(() => store.Write(new WorldBatch
        {
            Chunks = [(B, Bytes(2))],
            Header = Bytes(2),
            Basins = [new StoredBasin(0, 0, 2000)],
            Machines = [new StoredMachine(O1, 3, Bytes(2)), new StoredMachine(O2, 4, null!)],
        }));

        Assert.Equal(Bytes(1), store.ReadWorldHeader());
        Assert.Equal(1000L, Assert.Single(store.ReadBasins()).Litres);
        Assert.Equal(new StoredMachine(O1, 3, Bytes(1)), Assert.Single(store.ReadMachines()));
        Assert.Equal(Bytes(1), store.ReadChunk(A));
        Assert.Null(store.ReadChunk(B));
    }

    [Fact]
    public void TheHeaderAndBasinCallsAreTheBatchUnderneath()
    {
        // WriteWorldHeader at open and WriteBasins for the lattice clear are the same write, so
        // a store cannot have two header paths that disagree.
        IWorldStore store = Track(CreateStore());

        store.WriteWorldHeader(Bytes(1));
        store.WriteBasins([new StoredBasin(1, 1, 1)]);
        store.Write(new WorldBatch { Header = Bytes(2), Basins = [] });

        Assert.Equal(Bytes(2), store.ReadWorldHeader());
        Assert.Empty(store.ReadBasins());
    }

    [Fact]
    public void AMachineSurvivesAReopen()
    {
        if (!CanReopen)
        {
            return;
        }

        IWorldStore store = Track(CreateStore());
        store.Write(new WorldBatch { Machines = [new StoredMachine(O1, 3, Bytes(4, 5, 6))] });
        store.Flush();

        IWorldStore reopened = Track(Reopen(store));
        Assert.Equal(new StoredMachine(O1, 3, Bytes(4, 5, 6)), Assert.Single(reopened.ReadMachines()));
    }

    [Fact]
    public void FlushIsSafeOnAnEmptyWorld() => Track(CreateStore()).Flush();

    [Fact]
    public void SurvivesAReopen()
    {
        if (!CanReopen)
        {
            // Said out loud rather than skipped silently. An in-memory store has nothing to
            // reopen from, so passing here would be a pass for the wrong reason — and a
            // no-op test that reads as coverage is exactly what this suite exists to avoid.
            Assert.False(
                Track(CreateStore()) is SqliteWorldStore,
                "a durable store must override CanReopen");
            return;
        }

        IWorldStore store = Track(CreateStore());
        store.WriteWorldHeader(Bytes(1, 2, 3));
        store.Write([(A, Bytes(7, 7, 7))], [(1, 2, Bytes(8))]);
        store.Flush();

        IWorldStore reopened = Track(Reopen(store));

        Assert.Equal(Bytes(1, 2, 3), reopened.ReadWorldHeader());
        Assert.Equal(Bytes(7, 7, 7), reopened.ReadChunk(A));
        Assert.Equal(Bytes(8), reopened.ReadColumn(1, 2));
        Assert.Single(reopened.LoadChunkIndex());
    }
}

/// <summary>The contract, against the store M6b-2's pipeline tests will use.</summary>
public sealed class InMemoryWorldStoreTests : WorldStoreContractTests
{
    protected override IWorldStore CreateStore() => new InMemoryWorldStore();

    [Fact]
    public void DisposeIsObservable() =>
        Assert.True(((InMemoryWorldStore)Disposed()).IsDisposed);

    private static IWorldStore Disposed()
    {
        var store = new InMemoryWorldStore();
        store.Dispose();
        return store;
    }
}

/// <summary>The same contract, against the store that touches a disk.</summary>
public sealed class SqliteWorldStoreTests : WorldStoreContractTests
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "overburden-tests", Guid.NewGuid().ToString("N"));

    private string WorldPath => Path.Combine(_directory, "world.db");

    protected override bool CanReopen => true;

    protected override IWorldStore CreateStore()
    {
        Directory.CreateDirectory(_directory);
        return new SqliteWorldStore(WorldPath);
    }

    protected override IWorldStore Reopen(IWorldStore store)
    {
        store.Dispose();
        return new SqliteWorldStore(WorldPath);
    }

    public override void Dispose()
    {
        base.Dispose();

        try
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A temp directory that will not delete is not a test failure. Leaving it is
            // tidier than turning a green run red over the file system.
        }
    }

    [Fact]
    public void ANewerSchemaVersionIsRefusedRatherThanOpened()
    {
        // The same rule the blob format follows, one level up: a build that met a newer schema
        // would have no way to tell an added column from a corrupt one.
        Directory.CreateDirectory(_directory);

        using (var store = new SqliteWorldStore(WorldPath))
        {
            store.WriteWorldHeader([1]);
        }

        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={WorldPath}"))
        {
            connection.Open();
            using Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"PRAGMA user_version = {SqliteWorldStore.SchemaVersion + 1};";
            command.ExecuteNonQuery();
        }

        Assert.Throws<BlobFormatException>(() => new SqliteWorldStore(WorldPath));
    }

    [Fact]
    public void ASchemaTwoWorldOpensAndGainsTheFluidTable()
    {
        // The world Matthew plays is schema 2 — chunks, columns, basins, no fluid — and the
        // upgrade is additive: open it, find the table, stamp 3, lose nothing. Built the way the
        // old build built it, with the tables it had and the version it stamped.
        Directory.CreateDirectory(_directory);

        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={WorldPath}"))
        {
            connection.Open();
            using Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE world (key TEXT NOT NULL PRIMARY KEY, value BLOB NOT NULL) STRICT;
                CREATE TABLE chunks (cx INTEGER NOT NULL, cy INTEGER NOT NULL, cz INTEGER NOT NULL,
                    blob BLOB NOT NULL, PRIMARY KEY (cx, cy, cz)) STRICT;
                CREATE TABLE columns (cx INTEGER NOT NULL, cz INTEGER NOT NULL, blob BLOB NOT NULL,
                    PRIMARY KEY (cx, cz)) STRICT;
                CREATE TABLE basins (pit_x INTEGER NOT NULL, pit_z INTEGER NOT NULL, litres INTEGER NOT NULL,
                    PRIMARY KEY (pit_x, pit_z)) STRICT;
                INSERT INTO chunks VALUES (1, 2, 3, X'AB');
                INSERT INTO basins VALUES (4, 5, 6000);
                PRAGMA user_version = 2;
                """;
            command.ExecuteNonQuery();
        }

        using (var store = new SqliteWorldStore(WorldPath))
        {
            Assert.Empty(store.ReadFluid());
            Assert.Equal([new ChunkPos(1, 2, 3)], store.LoadChunkIndex().ToArray());
            Assert.Equal(6000L, Assert.Single(store.ReadBasins()).Litres);
            store.Write([], [], [(new ChunkPos(7, 8, 9), [1, 2])]);
        }

        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={WorldPath}"))
        {
            connection.Open();
            using Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            Assert.Equal((long)SqliteWorldStore.SchemaVersion, (long)command.ExecuteScalar()!);
        }

        using var again = new SqliteWorldStore(WorldPath);
        Assert.Single(again.ReadFluid());
    }

    [Fact]
    public void ASchemaThreeWorldOpensAndGainsTheMachinesTable()
    {
        // The world Matthew plays at M10a-1 is schema 3 — chunks, columns, basins, fluid, no
        // machines — and the upgrade is additive again: open it, find the table, stamp 4, lose
        // nothing, and a session write lands rows in it. Built as the M8b-4b build built it.
        Directory.CreateDirectory(_directory);

        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={WorldPath}"))
        {
            connection.Open();
            using Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE world (key TEXT NOT NULL PRIMARY KEY, value BLOB NOT NULL) STRICT;
                CREATE TABLE chunks (cx INTEGER NOT NULL, cy INTEGER NOT NULL, cz INTEGER NOT NULL,
                    blob BLOB NOT NULL, PRIMARY KEY (cx, cy, cz)) STRICT;
                CREATE TABLE columns (cx INTEGER NOT NULL, cz INTEGER NOT NULL, blob BLOB NOT NULL,
                    PRIMARY KEY (cx, cz)) STRICT;
                CREATE TABLE fluid (cx INTEGER NOT NULL, cy INTEGER NOT NULL, cz INTEGER NOT NULL,
                    blob BLOB NOT NULL, PRIMARY KEY (cx, cy, cz)) STRICT;
                CREATE TABLE basins (pit_x INTEGER NOT NULL, pit_z INTEGER NOT NULL, litres INTEGER NOT NULL,
                    PRIMARY KEY (pit_x, pit_z)) STRICT;
                INSERT INTO world VALUES ('header', X'01');
                INSERT INTO chunks VALUES (1, 2, 3, X'AB');
                INSERT INTO fluid VALUES (1, 2, 3, X'CD');
                INSERT INTO basins VALUES (4, 5, 6000);
                PRAGMA user_version = 3;
                """;
            command.ExecuteNonQuery();
        }

        using (var store = new SqliteWorldStore(WorldPath))
        {
            Assert.Empty(store.ReadMachines());
            Assert.Equal([new ChunkPos(1, 2, 3)], store.LoadChunkIndex().ToArray());
            Assert.Equal(Bytes(0xCD), Assert.Single(store.ReadFluid()).Blob);
            Assert.Equal(6000L, Assert.Single(store.ReadBasins()).Litres);
            Assert.Equal(Bytes(1), store.ReadWorldHeader());
            store.Write(new WorldBatch { Machines = [new StoredMachine(new VoxelPos(4, 8, 12), 7, Bytes(9))] });
        }

        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={WorldPath}"))
        {
            connection.Open();
            using Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            Assert.Equal(4L, (long)command.ExecuteScalar()!);
        }

        using var again = new SqliteWorldStore(WorldPath);
        StoredMachine row = Assert.Single(again.ReadMachines());
        Assert.Equal(new VoxelPos(4, 8, 12), row.Origin);
        Assert.Equal(7, row.MaterialId);
        Assert.Equal(6000L, Assert.Single(again.ReadBasins()).Litres);
    }

    [Fact]
    public void TheWorldIsOneFileAndItSurvivesBeingReopenedRepeatedly()
    {
        // Reopening is what a player does every session, and each open runs EnsureSchema against
        // an existing database. "IF NOT EXISTS plus a version stamp" has to be idempotent, or the
        // second launch of the game is the one that fails.
        Directory.CreateDirectory(_directory);

        for (int session = 0; session < 4; session++)
        {
            using var store = new SqliteWorldStore(WorldPath);
            store.Write([(new ChunkPos(session, 0, 0), [(byte)session])], [], []);
        }

        using var final = new SqliteWorldStore(WorldPath);
        Assert.Equal(4, final.LoadChunkIndex().Count);
    }

    [Fact]
    public void UsingAStoreAfterDisposeThrows()
    {
        Directory.CreateDirectory(_directory);
        var store = new SqliteWorldStore(WorldPath);
        store.Dispose();

        Assert.Throws<ObjectDisposedException>(() => store.ReadWorldHeader());
        Assert.Throws<ObjectDisposedException>(() => store.ReadChunk(new ChunkPos(0, 0, 0)));
        Assert.Throws<ObjectDisposedException>(() => store.Write([], [], []));
    }
}
