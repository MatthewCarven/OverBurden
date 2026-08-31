using MinerCraft.Core.Content;
using MinerCraft.Core.Jobs;
using MinerCraft.Core.Lighting;
using MinerCraft.Core.Persistence;
using MinerCraft.Core.Voxels;

namespace MinerCraft.Core.Streaming;

/// <summary>
/// Reads one stored chunk on a worker thread, in place of generating it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Submitted on the serial lane</b> — M2a built that lane for disk work and it has been unused
/// since. Measured at M6b-1: a SQLite point query through a prepared statement is 6 µs and the
/// deserialise 30, so the whole 1,521-chunk fill through one lane is 54 ms against 89 to generate
/// the same box on eight workers. The lane is not the bottleneck the plan feared.
/// </para>
/// <para>
/// A blob that will not read is a <see cref="BlobFormatException"/>, and it is left to fault the
/// job rather than being swallowed into a regenerate. Falling back to the seed would replace the
/// player's base with untouched hillside and report nothing — which is the exact failure this
/// milestone exists to prevent, arriving as a well-meant recovery.
/// </para>
/// </remarks>
public sealed class LoadChunkJob : IJob
{
    private readonly IWorldStore _store;
    private readonly MaterialTable _materials;

    public LoadChunkJob(IWorldStore store, MaterialTable materials, ChunkPos position)
    {
        _store = store;
        _materials = materials;
        Position = position;
    }

    public JobKind Kind => JobKind.Load;

    public ChunkPos Position { get; }

    /// <summary>The restored voxels, or null if the store had nothing after all.</summary>
    /// <remarks>
    /// Null is not an error and is not impossible: the in-memory index says which chunks were
    /// stored when the world opened, and a store handed a position it has never seen answers null.
    /// The pipeline treats it as "generate instead", which is the only honest recovery — unlike a
    /// blob that exists and will not parse.
    /// </remarks>
    public VoxelChunk? Result { get; private set; }

    public void Run(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();

        byte[]? blob = _store.ReadChunk(Position);
        if (blob is null)
        {
            return;
        }

        Result = ChunkBlob.Read(blob, Position, _materials);
    }
}

/// <summary>Reads one stored sky column, so a reloaded shaft is not lit from the pre-dig surface.</summary>
/// <remarks>
/// The restore lands after the column has already been seeded analytically, which is deliberate:
/// <c>ChunkStreamer.RetainColumn</c> runs inside a box-difference sweep on the sim thread, and a
/// one-chunk step at r16 creates ~37 new columns. Reading them there would put I/O in that sweep.
/// The price is a frame or two where a reloaded shaft is lit against the old surface — the same
/// category of transient as a chunk that has generated and not yet meshed.
/// </remarks>
public sealed class LoadColumnJob : IJob
{
    private readonly IWorldStore _store;

    public LoadColumnJob(IWorldStore store, int chunkX, int chunkZ)
    {
        _store = store;
        ChunkX = chunkX;
        ChunkZ = chunkZ;

        // A column spans every chunk Y, so there is no one chunk this is "about". Y zero is as
        // good as any for the distance band, and the band barely matters: column loads are rare
        // and cost 6 µs.
        Position = new ChunkPos(chunkX, 0, chunkZ);
    }

    public int ChunkX { get; }

    public int ChunkZ { get; }

    public JobKind Kind => JobKind.Load;

    public ChunkPos Position { get; }

    /// <summary>The restored column, or null if the store had nothing.</summary>
    public SkyColumn? Result { get; private set; }

    public void Run(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();

        byte[]? blob = _store.ReadColumn(ChunkX, ChunkZ);
        if (blob is null)
        {
            return;
        }

        Result = SkyColumnBlob.Read(blob, ChunkX, ChunkZ);
    }
}

/// <summary>
/// Serialises a batch of chunks and columns and commits them in one transaction.
/// </summary>
/// <remarks>
/// <para>
/// <b>A batch rather than a job per chunk, because the unload path hands over a whole shell at
/// once</b> and because a transaction per chunk is the difference between one commit and hundreds.
/// Chunks and their columns go together in the same transaction for the reason
/// <see cref="IWorldStore.Write"/> gives: a shaft stored without the column recording how far the
/// sky reaches down it reloads dark.
/// </para>
/// <para>
/// <b>It holds <see cref="VoxelChunk"/> references and needs no chunk retention.</b> A
/// <see cref="VoxelChunk"/> is an ordinary object: once handed over, the streamer may forget the
/// record freely and the object stays alive because this job references it. A chunk that is
/// <em>still</em> live is protected instead by <see cref="StreamedChunk.VoxelsSharedWithJob"/>,
/// set before submission — so an edit clones rather than mutating the array this job is reading.
/// Without that, <see cref="VoxelChunk.Set"/> can promote the representation and swap the backing
/// arrays under this thread, which is M3a's recorded <c>NullReferenceException</c> on a worker.
/// </para>
/// <para>
/// <b>This job must never be cancelled.</b> Every other job here is cancelled the instant nobody
/// wants its chunk, which is right for work that can be rebuilt; a cancelled save is a deleted
/// tunnel. <c>ChunkPipeline</c> submits it with <see cref="CancellationToken.None"/> and does not
/// route it through the per-chunk cancellation the unview path uses.
/// </para>
/// </remarks>
public sealed class SaveWorldJob : IJob
{
    private readonly IWorldStore _store;
    private readonly (ChunkPos Position, VoxelChunk Voxels)[] _chunks;
    private readonly (int X, int Z, SkyColumn Column)[] _columns;

    public SaveWorldJob(
        IWorldStore store,
        (ChunkPos Position, VoxelChunk Voxels)[] chunks,
        (int X, int Z, SkyColumn Column)[] columns)
    {
        _store = store;
        _chunks = chunks;
        _columns = columns;

        Position = chunks.Length > 0 ? chunks[0].Position
            : columns.Length > 0 ? new ChunkPos(columns[0].X, 0, columns[0].Z)
            : ChunkPos.Zero;
    }

    public JobKind Kind => JobKind.Save;

    /// <summary>
    /// Nominal, and it only feeds the distance band. A batch spans many chunks and this is the
    /// first of them.
    /// </summary>
    public ChunkPos Position { get; }

    /// <summary>Which chunks this job was asked to write. Read by the sim thread when it reports.</summary>
    public IReadOnlyList<(ChunkPos Position, VoxelChunk Voxels)> Chunks => _chunks;

    /// <summary>Which columns this job was asked to write.</summary>
    public IReadOnlyList<(int X, int Z, SkyColumn Column)> Columns => _columns;

    public int BytesWritten { get; private set; }

    public void Run(CancellationToken cancellation)
    {
        // Deliberately not checked. See the class remarks: a cancelled save is a deleted tunnel,
        // and the pipeline hands this None — the parameter exists because IJob has it.
        _ = cancellation;

        var chunks = new (ChunkPos, byte[])[_chunks.Length];
        var columns = new (int, int, byte[])[_columns.Length];

        int bytes = 0;

        for (int i = 0; i < _chunks.Length; i++)
        {
            byte[] blob = ChunkBlob.Write(_chunks[i].Position, _chunks[i].Voxels);
            chunks[i] = (_chunks[i].Position, blob);
            bytes += blob.Length;
        }

        for (int i = 0; i < _columns.Length; i++)
        {
            byte[] blob = SkyColumnBlob.Write(_columns[i].Column);
            columns[i] = (_columns[i].X, _columns[i].Z, blob);
            bytes += blob.Length;
        }

        _store.Write(chunks, columns);
        BytesWritten = bytes;
    }
}
