using MinerCraft.Core.Voxels;

namespace MinerCraft.Core.Persistence;

/// <summary>
/// Where a world's saved bytes live.
/// </summary>
/// <remarks>
/// <para>
/// <b>Named for the world rather than for chunks, correcting M6-PLAN.</b> The plan called this
/// <c>IChunkStore</c>, which was accurate until it turned out to own three things: the chunk
/// blobs, the sky columns, and the world header carrying the ID table. A store that holds the
/// material table is not a chunk store.
/// </para>
/// <para>
/// <b>It deals in bytes, not in <see cref="VoxelChunk"/>s</b>, so that storage and format stay
/// separable: <see cref="ChunkBlob"/> and friends own what the bytes mean, and an implementation
/// of this needs to know nothing about voxels, palettes or light. That is what lets the SQLite
/// implementation live in its own assembly and keeps Core free of a native dependency.
/// </para>
/// <para>
/// <b>Not thread-safe, and that is a contract rather than an omission.</b> Every call arrives on
/// the job scheduler's serial lane, which M2a built for exactly this and which runs one at a time
/// because disk work contends on the same file anyway. An implementation is free to hold prepared
/// statements and reuse buffers on that basis.
/// </para>
/// </remarks>
public interface IWorldStore : IDisposable
{
    /// <summary>The world header's bytes, or null for a world that has never been written.</summary>
    /// <remarks>
    /// Null is how "new world" is detected, and it is the only place that question is asked.
    /// </remarks>
    byte[]? ReadWorldHeader();

    /// <summary>
    /// Replaces the world header.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Written at open, before any chunk, and that ordering is what makes the ID table safe
    /// without a world-spanning transaction.</b> Numeric IDs are only ever allocated during
    /// content load, which happens once at open — so by the time any chunk is written, the table
    /// it references is already the one on disk. M6-PLAN asked for "the same transaction as the
    /// blob that first used it"; ordering delivers the same guarantee for free.
    /// </para>
    /// </remarks>
    void WriteWorldHeader(byte[] blob);

    /// <summary>Every chunk position that has a stored blob.</summary>
    /// <remarks>
    /// <para>
    /// <b>Read once at open, and the answer lives in memory afterwards.</b> "Does this chunk have
    /// a blob?" is asked on the sim thread once per chunk entering the load box — a 37×37 shell at
    /// r16 — so it cannot be a query. At 12 bytes plus set overhead this is ~5 MB for 100,000
    /// edited chunks, which is a heavily played world.
    /// </para>
    /// <para>
    /// The rejected alternative is asking the database per chunk: 1,521 point queries down one
    /// serial lane to fill r4h/2v, nearly all of them answering no.
    /// </para>
    /// </remarks>
    IReadOnlyCollection<ChunkPos> LoadChunkIndex();

    /// <summary>Every sky column that has a stored blob, by chunk (x, z).</summary>
    IReadOnlyCollection<(int X, int Z)> LoadColumnIndex();

    /// <summary>One chunk's stored bytes, or null if it has none.</summary>
    byte[]? ReadChunk(ChunkPos position);

    /// <summary>One sky column's stored bytes, or null if it has none.</summary>
    byte[]? ReadColumn(int chunkX, int chunkZ);

    /// <summary>
    /// Writes chunks and sky columns together, atomically.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One call rather than two, because a chunk and its column are not independent.</b> A
    /// shaft's chunks stored without the column that records how far the sky now reaches down it
    /// reload with the column seeded analytically — from the <em>pre-dig</em> surface — so every
    /// cell in the shaft fails <see cref="Lighting.SkyColumn.IsCellOpenToSky"/> and the shaft is
    /// dark for a reason nothing on screen can explain. Splitting this into two calls would make
    /// that state reachable by a crash between them.
    /// </para>
    /// <para>
    /// Batched rather than per chunk because the unload path hands over a whole shell at once, and
    /// because a transaction per chunk is the difference between one fsync and hundreds.
    /// </para>
    /// </remarks>
    void Write(
        IReadOnlyList<(ChunkPos Position, byte[] Blob)> chunks,
        IReadOnlyList<(int X, int Z, byte[] Blob)> columns);

    /// <summary>
    /// Pushes everything written so far into the main file rather than leaving it in a sidecar.
    /// </summary>
    /// <remarks>
    /// Under SQLite's write-ahead log a committed write lives in a <c>-wal</c> file until a
    /// checkpoint, so the main <c>.db</c> can sit at its initial size while holding a world's
    /// worth of committed edits. Durable either way — the WAL is part of the database — but
    /// surprising enough when inspecting the file by hand that it is worth an explicit call at
    /// quit.
    /// </remarks>
    void Flush();
}

/// <summary>An <see cref="IWorldStore"/> that keeps everything in memory.</summary>
/// <remarks>
/// <para>
/// <b>For tests, and it is deliberately kept honest by the same contract suite the SQLite one
/// runs.</b> M1 built <c>InMemoryContentSource</c> for the same reason and it drifted — M3c found
/// it had no grass, so every content test was passing against a world the game does not ship.
/// A store that drifts is worse: M6b-2's pipeline tests would all be exercising a fiction, and
/// they are the tests standing between a defect and permanent data loss.
/// </para>
/// <para>
/// It copies on the way in and on the way out, because a real store does. Handing back the caller's
/// own array would let a test mutate what it thinks it stored and pass for the wrong reason.
/// </para>
/// </remarks>
public sealed class InMemoryWorldStore : IWorldStore
{
    private readonly Dictionary<ChunkPos, byte[]> _chunks = [];
    private readonly Dictionary<(int X, int Z), byte[]> _columns = [];
    private byte[]? _header;

    /// <summary>How many times <see cref="Write"/> has been called. For tests about batching.</summary>
    public int WriteCalls { get; private set; }

    /// <summary>How many chunk blobs have been written, counting rewrites.</summary>
    public int ChunkWrites { get; private set; }

    /// <summary>How many column blobs have been written, counting rewrites.</summary>
    public int ColumnWrites { get; private set; }

    public int FlushCalls { get; private set; }

    public bool IsDisposed { get; private set; }

    public byte[]? ReadWorldHeader() => Copy(_header);

    public void WriteWorldHeader(byte[] blob)
    {
        ArgumentNullException.ThrowIfNull(blob);
        _header = Copy(blob);
    }

    public IReadOnlyCollection<ChunkPos> LoadChunkIndex() => [.. _chunks.Keys];

    public IReadOnlyCollection<(int X, int Z)> LoadColumnIndex() => [.. _columns.Keys];

    public byte[]? ReadChunk(ChunkPos position) =>
        _chunks.TryGetValue(position, out byte[]? blob) ? Copy(blob) : null;

    public byte[]? ReadColumn(int chunkX, int chunkZ) =>
        _columns.TryGetValue((chunkX, chunkZ), out byte[]? blob) ? Copy(blob) : null;

    public void Write(
        IReadOnlyList<(ChunkPos Position, byte[] Blob)> chunks,
        IReadOnlyList<(int X, int Z, byte[] Blob)> columns)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(columns);

        WriteCalls++;

        foreach ((ChunkPos position, byte[] blob) in chunks)
        {
            _chunks[position] = Copy(blob)!;
            ChunkWrites++;
        }

        foreach ((int x, int z, byte[] blob) in columns)
        {
            _columns[(x, z)] = Copy(blob)!;
            ColumnWrites++;
        }
    }

    public void Flush() => FlushCalls++;

    public void Dispose() => IsDisposed = true;

    private static byte[]? Copy(byte[]? source) => source is null ? null : [.. source];
}
