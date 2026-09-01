using Microsoft.Data.Sqlite;
using MinerCraft.Core.Persistence;
using MinerCraft.Core.Voxels;

namespace MinerCraft.Persistence;

/// <summary>
/// An <see cref="IWorldStore"/> backed by one SQLite file.
/// </summary>
/// <remarks>
/// <para>
/// DESIGN locks this: <c>(cx, cy, cz) → blob</c>, region files being the proper answer and SQLite
/// the pragmatic one for a solo project. What "pragmatic" buys concretely is atomic transactions —
/// a crash mid-save leaves the previous state rather than a half-written chunk — plus a free
/// index, no free-list management of our own, and <c>sqlite3</c> on the command line as a
/// debugging tool.
/// </para>
/// <para>
/// <b>Not thread-safe by design.</b> One connection, prepared statements held for the lifetime of
/// the store and reused. Every call arrives on the job scheduler's serial lane, which runs one at
/// a time; that is what makes reusing a command with bound parameters safe, and it is most of why
/// the per-read cost can be kept near the deserialise cost rather than dominated by statement
/// preparation.
/// </para>
/// <para>
/// <b>Two callers here would not be a stale read — they would be two threads binding parameters on
/// one shared <c>SqliteCommand</c> inside one connection.</b> That is why the one caller that is
/// not on the lane, <see cref="MinerCraft.Core.Streaming.ChunkPipeline.SaveAll"/>, waits for the
/// lane to empty first. See <see cref="IWorldStore"/>.
/// </para>
/// </remarks>
public sealed class SqliteWorldStore : IWorldStore
{
    /// <summary>
    /// The schema this build understands, distinct from any blob's format version.
    /// </summary>
    /// <remarks>
    /// Two versions rather than one because they change for unrelated reasons: adding a table is
    /// not a statement about how a chunk packs its palette. Stored in SQLite's own
    /// <c>user_version</c> pragma, which costs no table and no row.
    ///
    /// <para>
    /// <b>Moved to 2 at M7b-2</b> for the <c>basins</c> table. An older file gains it on open —
    /// every statement here is <c>IF NOT EXISTS</c> — so the bump records what happened rather
    /// than gating anything, and a file written by a <em>newer</em> build is still refused.
    /// </para>
    /// </remarks>
    public const int SchemaVersion = 2;

    private const string HeaderKey = "header";

    private readonly SqliteConnection _connection;
    private readonly SqliteCommand _readHeader;
    private readonly SqliteCommand _writeHeader;
    private readonly SqliteCommand _readChunk;
    private readonly SqliteCommand _readColumn;
    private readonly SqliteCommand _writeChunk;
    private readonly SqliteCommand _writeColumn;
    private readonly SqliteCommand _writeBasin;

    private bool _disposed;

    /// <summary>Opens or creates a world at <paramref name="path"/>.</summary>
    public SqliteWorldStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        Path = path;

        _connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());

        _connection.Open();

        Configure();
        EnsureSchema();

        _readHeader = Prepare("SELECT value FROM world WHERE key = $key;", "$key");
        _writeHeader = Prepare(
            "INSERT INTO world (key, value) VALUES ($key, $value) "
            + "ON CONFLICT(key) DO UPDATE SET value = excluded.value;",
            "$key", "$value");

        _readChunk = Prepare(
            "SELECT blob FROM chunks WHERE cx = $x AND cy = $y AND cz = $z;", "$x", "$y", "$z");
        _readColumn = Prepare(
            "SELECT blob FROM columns WHERE cx = $x AND cz = $z;", "$x", "$z");

        _writeChunk = Prepare(
            "INSERT INTO chunks (cx, cy, cz, blob) VALUES ($x, $y, $z, $blob) "
            + "ON CONFLICT(cx, cy, cz) DO UPDATE SET blob = excluded.blob;",
            "$x", "$y", "$z", "$blob");

        _writeColumn = Prepare(
            "INSERT INTO columns (cx, cz, blob) VALUES ($x, $z, $blob) "
            + "ON CONFLICT(cx, cz) DO UPDATE SET blob = excluded.blob;",
            "$x", "$z", "$blob");

        _writeBasin = Prepare(
            "INSERT INTO basins (pit_x, pit_z, litres) VALUES ($x, $z, $litres);",
            "$x", "$z", "$litres");
    }

    /// <summary>The file this store was opened on.</summary>
    public string Path { get; }

    public byte[]? ReadWorldHeader()
    {
        ThrowIfDisposed();

        _readHeader.Parameters["$key"].Value = HeaderKey;
        return _readHeader.ExecuteScalar() as byte[];
    }

    public void WriteWorldHeader(byte[] blob)
    {
        ArgumentNullException.ThrowIfNull(blob);
        ThrowIfDisposed();

        _writeHeader.Parameters["$key"].Value = HeaderKey;
        _writeHeader.Parameters["$value"].Value = blob;
        _writeHeader.ExecuteNonQuery();
    }

    public IReadOnlyCollection<ChunkPos> LoadChunkIndex()
    {
        ThrowIfDisposed();

        var index = new List<ChunkPos>();

        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "SELECT cx, cy, cz FROM chunks;";

        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            index.Add(new ChunkPos(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2)));
        }

        return index;
    }

    public IReadOnlyCollection<(int X, int Z)> LoadColumnIndex()
    {
        ThrowIfDisposed();

        var index = new List<(int X, int Z)>();

        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "SELECT cx, cz FROM columns;";

        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            index.Add((reader.GetInt32(0), reader.GetInt32(1)));
        }

        return index;
    }

    public byte[]? ReadChunk(ChunkPos position)
    {
        ThrowIfDisposed();

        _readChunk.Parameters["$x"].Value = position.X;
        _readChunk.Parameters["$y"].Value = position.Y;
        _readChunk.Parameters["$z"].Value = position.Z;

        return _readChunk.ExecuteScalar() as byte[];
    }

    public byte[]? ReadColumn(int chunkX, int chunkZ)
    {
        ThrowIfDisposed();

        _readColumn.Parameters["$x"].Value = chunkX;
        _readColumn.Parameters["$z"].Value = chunkZ;

        return _readColumn.ExecuteScalar() as byte[];
    }

    public void Write(
        IReadOnlyList<(ChunkPos Position, byte[] Blob)> chunks,
        IReadOnlyList<(int X, int Z, byte[] Blob)> columns)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(columns);
        ThrowIfDisposed();

        if (chunks.Count == 0 && columns.Count == 0)
        {
            // An empty flush is the common case on a quiet frame, and an empty transaction still
            // costs a commit.
            return;
        }

        // One transaction over both, because a chunk and its column are not independent — see
        // IWorldStore.Write. It is also the difference between one fsync and hundreds.
        using SqliteTransaction transaction = _connection.BeginTransaction();

        _writeChunk.Transaction = transaction;
        _writeColumn.Transaction = transaction;

        try
        {
            foreach ((ChunkPos position, byte[] blob) in chunks)
            {
                _writeChunk.Parameters["$x"].Value = position.X;
                _writeChunk.Parameters["$y"].Value = position.Y;
                _writeChunk.Parameters["$z"].Value = position.Z;
                _writeChunk.Parameters["$blob"].Value = blob;
                _writeChunk.ExecuteNonQuery();
            }

            foreach ((int x, int z, byte[] blob) in columns)
            {
                _writeColumn.Parameters["$x"].Value = x;
                _writeColumn.Parameters["$z"].Value = z;
                _writeColumn.Parameters["$blob"].Value = blob;
                _writeColumn.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        finally
        {
            _writeChunk.Transaction = null;
            _writeColumn.Transaction = null;
        }
    }

    public IReadOnlyList<StoredBasin> ReadBasins()
    {
        ThrowIfDisposed();

        var basins = new List<StoredBasin>();

        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "SELECT pit_x, pit_z, litres FROM basins;";

        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            basins.Add(new StoredBasin(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetInt64(2)));
        }

        return basins;
    }

    public void WriteBasins(IReadOnlyList<StoredBasin> basins)
    {
        ArgumentNullException.ThrowIfNull(basins);
        ThrowIfDisposed();

        // Delete and insert inside one transaction, so a world is never briefly a world with no
        // water in it. The empty case still runs, because clearing is exactly what a world opened
        // with a different watershed lattice asks for.
        using SqliteTransaction transaction = _connection.BeginTransaction();

        using (SqliteCommand clear = _connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = "DELETE FROM basins;";
            clear.ExecuteNonQuery();
        }

        _writeBasin.Transaction = transaction;

        try
        {
            foreach (StoredBasin basin in basins)
            {
                _writeBasin.Parameters["$x"].Value = basin.PitX;
                _writeBasin.Parameters["$z"].Value = basin.PitZ;
                _writeBasin.Parameters["$litres"].Value = basin.Litres;
                _writeBasin.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        finally
        {
            _writeBasin.Transaction = null;
        }
    }

    public void Flush()
    {
        ThrowIfDisposed();

        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _readHeader.Dispose();
        _writeHeader.Dispose();
        _readChunk.Dispose();
        _readColumn.Dispose();
        _writeChunk.Dispose();
        _writeColumn.Dispose();
        _writeBasin.Dispose();

        // Checkpointed on the way out so the main file holds what was committed. Not required for
        // durability — the WAL is part of the database — but a .db that stays at its initial size
        // while holding a world's worth of edits is a genuinely confusing thing to inspect.
        try
        {
            using SqliteCommand command = _connection.CreateCommand();
            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            command.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // A checkpoint is tidiness, not durability. Failing to take one on the way out must
            // not throw from Dispose and mask whatever actually went wrong.
        }

        _connection.Dispose();
    }

    private void Configure()
    {
        using SqliteCommand command = _connection.CreateCommand();

        // WAL: readers do not block the writer, which matters the moment M6b's load path runs
        // beside a save. NORMAL: fsync at checkpoints rather than at every commit — safe against
        // a process crash, which is the failure that actually happens, and the trade every game
        // save makes against a power cut mid-write.
        command.CommandText =
            "PRAGMA journal_mode = WAL;"
            + "PRAGMA synchronous = NORMAL;"
            + "PRAGMA foreign_keys = ON;";

        command.ExecuteNonQuery();
    }

    private void EnsureSchema()
    {
        using SqliteCommand read = _connection.CreateCommand();
        read.CommandText = "PRAGMA user_version;";
        int version = Convert.ToInt32(read.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);

        if (version > SchemaVersion)
        {
            throw new BlobFormatException(
                $"World '{Path}' has schema version {version}, but this build understands "
                + $"{SchemaVersion}. Refusing rather than guessing at the layout.");
        }

        using SqliteCommand create = _connection.CreateCommand();

        // STRICT so a blob column cannot quietly hold text. IF NOT EXISTS so opening an existing
        // world is the same code path as creating one — there is no "create world" mode, and a
        // world that exists is simply one whose header read comes back non-null.
        create.CommandText =
            """
            CREATE TABLE IF NOT EXISTS world (
                key   TEXT NOT NULL PRIMARY KEY,
                value BLOB NOT NULL
            ) STRICT;

            CREATE TABLE IF NOT EXISTS chunks (
                cx   INTEGER NOT NULL,
                cy   INTEGER NOT NULL,
                cz   INTEGER NOT NULL,
                blob BLOB    NOT NULL,
                PRIMARY KEY (cx, cy, cz)
            ) STRICT;

            CREATE TABLE IF NOT EXISTS columns (
                cx   INTEGER NOT NULL,
                cz   INTEGER NOT NULL,
                blob BLOB    NOT NULL,
                PRIMARY KEY (cx, cz)
            ) STRICT;

            CREATE TABLE IF NOT EXISTS basins (
                pit_x  INTEGER NOT NULL,
                pit_z  INTEGER NOT NULL,
                litres INTEGER NOT NULL,
                PRIMARY KEY (pit_x, pit_z)
            ) STRICT;
            """;

        create.ExecuteNonQuery();

        if (version < SchemaVersion)
        {
            using SqliteCommand stamp = _connection.CreateCommand();
            stamp.CommandText = $"PRAGMA user_version = {SchemaVersion};";
            stamp.ExecuteNonQuery();
        }
    }

    private SqliteCommand Prepare(string sql, params string[] parameters)
    {
        SqliteCommand command = _connection.CreateCommand();
        command.CommandText = sql;

        foreach (string name in parameters)
        {
            command.Parameters.Add(new SqliteParameter(name, null));
        }

        command.Prepare();
        return command;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
