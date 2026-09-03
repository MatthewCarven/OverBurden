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

    private static byte[] Bytes(params int[] values) => [.. values.Select(v => (byte)v)];

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
        Path.Combine(Path.GetTempPath(), "minercraft-tests", Guid.NewGuid().ToString("N"));

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
    public void TheWorldIsOneFileAndItSurvivesBeingReopenedRepeatedly()
    {
        // Reopening is what a player does every session, and each open runs EnsureSchema against
        // an existing database. "IF NOT EXISTS plus a version stamp" has to be idempotent, or the
        // second launch of the game is the one that fails.
        Directory.CreateDirectory(_directory);

        for (int session = 0; session < 4; session++)
        {
            using var store = new SqliteWorldStore(WorldPath);
            store.Write([(new ChunkPos(session, 0, 0), [(byte)session])], []);
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
        Assert.Throws<ObjectDisposedException>(() => store.Write([], []));
    }
}
