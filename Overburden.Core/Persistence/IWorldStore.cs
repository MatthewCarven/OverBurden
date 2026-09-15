using Overburden.Core.Voxels;

namespace Overburden.Core.Persistence;

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
/// <para>
/// <b>There is exactly one caller that is not on that lane, and M6b-3 is where it arrived.</b>
/// <see cref="Streaming.ChunkPipeline.SaveAll"/> writes inline on the sim thread — for an autosave
/// timer and for quit — so it waits for the lane to come out of the store before it touches it.
/// The reason that wait is not decorative is the <em>load</em> rather than the save: a chunk load
/// is submitted once per stored chunk entering the view box, so walking back into a region that
/// has been dug means the lane is usually inside the store. Anything else that ever calls a store
/// from off the lane has to do the same.
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
    void WriteWorldHeader(byte[] blob) => Write(new WorldBatch { Header = blob });

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
    /// Writes everything in the batch, atomically: chunks, sky columns and fluid panels, and —
    /// when the batch carries them — the machine table, the basin table and the header.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One call rather than several, because none of these is independent of the others.</b> A
    /// shaft's chunks stored without the column that records how far the sky now reaches down it
    /// reload with the column seeded analytically — from the <em>pre-dig</em> surface — so every
    /// cell in the shaft fails <see cref="Lighting.SkyColumn.IsCellOpenToSky"/> and the shaft is
    /// dark for a reason nothing on screen can explain (M6b). A chunk's voxels written and its
    /// water not is, after a crash between them, water inside rock or rock over a hole (M8b-4b).
    /// And the session — header, basins, machines — written in three calls is, after a crash
    /// between any two, a lake's litres in neither tier or a machine's row with no debit for its
    /// body (M10a-1). Every one of those is a state a crash between two transactions can reach and
    /// one transaction cannot. See <see cref="WorldBatch"/> for what null and empty mean.
    /// </para>
    /// <para>
    /// Batched rather than per chunk because the unload path hands over a whole shell at once, and
    /// because a transaction per chunk is the difference between one fsync and hundreds.
    /// </para>
    /// </remarks>
    void Write(WorldBatch batch);

    /// <summary>Chunks, columns and fluid with no session — the unload path's batch.</summary>
    void Write(
        IReadOnlyList<(ChunkPos Position, byte[] Blob)> chunks,
        IReadOnlyList<(int X, int Z, byte[] Blob)> columns,
        IReadOnlyList<(ChunkPos Position, byte[] Blob)> fluid) =>
        Write(new WorldBatch { Chunks = chunks, Columns = columns, Fluid = fluid });

    /// <summary>Chunks and columns with no fluid, for a caller that has none.</summary>
    /// <remarks>
    /// <b>The fluid rides the chunk batch as a third list rather than its own call, for the reason
    /// the column does (M8b-4b)</b>: a chunk's voxels written in one transaction and its water in
    /// another is, after a crash between them, water inside rock or rock over a hole. This
    /// overload exists so a caller with nothing wet — the store bench, most tests — does not
    /// have to say so, and it is the batch write underneath.
    /// </remarks>
    void Write(
        IReadOnlyList<(ChunkPos Position, byte[] Blob)> chunks,
        IReadOnlyList<(int X, int Z, byte[] Blob)> columns) =>
        Write(new WorldBatch { Chunks = chunks, Columns = columns });

    /// <summary>Every stored machine row, whole.</summary>
    /// <remarks>
    /// Read once at open beside the basins and for the same reason: rows are hundreds, not
    /// millions, and every one of them ticks whether or not its chunk is loaded (M10-PLAN
    /// decision 4), so the set lives in memory and the disk is a mirror written whole at every
    /// session save. There is no per-row read and no index to keep.
    /// </remarks>
    IReadOnlyList<StoredMachine> ReadMachines();

    /// <summary>Every stored fluid panel, whole.</summary>
    /// <remarks>
    /// The whole table in one call, which the open read until M8b-4c, when the panel started
    /// leaving RAM with its chunk and the open began reading <see cref="LoadFluidIndex"/> instead.
    /// Kept for the tests and the tools: a row is one chunk's held cells, and an empty row is a
    /// chunk whose water drained after it was first written, kept so the row says so.
    /// </remarks>
    IReadOnlyList<(ChunkPos Position, byte[] Blob)> ReadFluid();

    /// <summary>Every chunk position that has a stored fluid row, empty rows included.</summary>
    /// <remarks>
    /// Read once at open beside <see cref="LoadChunkIndex"/> and for the same reason: "does this
    /// chunk have water on disk?" is asked on the sim thread at every arrival, and a query there
    /// would put the lane inside a box sweep. An empty row stays in the index; loading one costs
    /// a point query and restores nothing.
    /// </remarks>
    IReadOnlyCollection<ChunkPos> LoadFluidIndex();

    /// <summary>One chunk's stored fluid row, or null if it has none.</summary>
    byte[]? ReadFluid(ChunkPos position);

    /// <summary>Every basin holding water, by the world coordinate of its pit.</summary>
    /// <remarks>
    /// Read once at open, like the chunk index and for the same reason: a basin's water is asked
    /// for whenever the watershed window moves, which is far too often to be a query.
    /// </remarks>
    IReadOnlyList<StoredBasin> ReadBasins();

    /// <summary>
    /// Replaces every stored basin volume with <paramref name="basins"/>, on its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A replacement rather than an upsert, and the cost is stated rather than hidden.</b> The
    /// ledger in memory is the truth — it holds every basin the player has ever rained on, not
    /// just the ones in view — so writing it whole is the shape that cannot desynchronise. It also
    /// means an autosave rewrites rows that did not change: at 1,376 basins per square kilometre
    /// explored, a heavily travelled world is tens of thousands of rows and tens of milliseconds.
    /// <b>Revisit trigger: the basin write becomes visible next to M6b-3's 13 ms autosave.</b> The
    /// fix is dirty-tracking in the ledger, which is why the ledger and not the store owns
    /// identity.
    /// </para>
    /// <para>
    /// Passing an empty list therefore <em>clears</em> the table, which is exactly what a world
    /// opened with a different watershed lattice needs — and since M10a-1 that clear at open is
    /// this method's only caller: an autosave and a quit put the basins on the batch through
    /// <see cref="Write(WorldBatch)"/>, in the chunk transaction, so the litres the portal drain
    /// moved between the two are in one tier or the other after a crash and never in neither.
    /// </para>
    /// </remarks>
    void WriteBasins(IReadOnlyList<StoredBasin> basins) => Write(new WorldBatch { Basins = basins });

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

/// <summary>One basin's stored water, keyed by the world coordinate of its pit.</summary>
/// <remarks>
/// <b>The pit rather than an index, and that is the whole reason this type exists.</b> The
/// watershed window is centred on the player, so every basin's array position renumbers when they
/// walk far enough; volume keyed on one would silently reattach to a different basin, draining a
/// lake and flooding the valley next door with nothing failing anywhere. A pit is a property of
/// the terrain — deterministic from the seed, identical across sessions and machines.
/// </remarks>
public readonly record struct StoredBasin(int PitX, int PitZ, long Litres);

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
    private readonly Dictionary<ChunkPos, byte[]> _fluid = [];
    private readonly Dictionary<(int X, int Z), long> _basins = [];
    private readonly Dictionary<VoxelPos, StoredMachine> _machines = [];
    private byte[]? _header;

    /// <summary>How many times <see cref="Write"/> has been called. For tests about batching.</summary>
    public int WriteCalls { get; private set; }

    /// <summary>How many chunk blobs have been written, counting rewrites.</summary>
    public int ChunkWrites { get; private set; }

    /// <summary>How many column blobs have been written, counting rewrites.</summary>
    public int ColumnWrites { get; private set; }

    /// <summary>How many fluid panel blobs have been written, counting rewrites.</summary>
    public int FluidWrites { get; private set; }

    /// <summary>How many times the basin table has been replaced.</summary>
    public int BasinWrites { get; private set; }

    /// <summary>How many times the machine table has been replaced.</summary>
    public int MachineWrites { get; private set; }

    /// <summary>How many times the header has been written.</summary>
    public int HeaderWrites { get; private set; }

    /// <summary>How many batches carried any part of the session.</summary>
    public int SessionWrites { get; private set; }

    public int FlushCalls { get; private set; }

    public bool IsDisposed { get; private set; }

    public byte[]? ReadWorldHeader() => Copy(_header);

    public void WriteWorldHeader(byte[] blob)
    {
        ArgumentNullException.ThrowIfNull(blob);
        Write(new WorldBatch { Header = blob });
    }

    public IReadOnlyList<StoredBasin> ReadBasins() =>
        [.. _basins.Select(entry => new StoredBasin(entry.Key.X, entry.Key.Z, entry.Value))];

    public void WriteBasins(IReadOnlyList<StoredBasin> basins)
    {
        ArgumentNullException.ThrowIfNull(basins);
        Write(new WorldBatch { Basins = basins });
    }

    public IReadOnlyList<StoredMachine> ReadMachines() =>
        [.. _machines.Values.Select(row => row with { Blob = Copy(row.Blob)! })];

    public IReadOnlyCollection<ChunkPos> LoadChunkIndex() => [.. _chunks.Keys];

    public IReadOnlyCollection<(int X, int Z)> LoadColumnIndex() => [.. _columns.Keys];

    public byte[]? ReadChunk(ChunkPos position) =>
        _chunks.TryGetValue(position, out byte[]? blob) ? Copy(blob) : null;

    public byte[]? ReadColumn(int chunkX, int chunkZ) =>
        _columns.TryGetValue((chunkX, chunkZ), out byte[]? blob) ? Copy(blob) : null;

    public IReadOnlyList<(ChunkPos Position, byte[] Blob)> ReadFluid() =>
        [.. _fluid.Select(entry => (entry.Key, Copy(entry.Value)!))];

    public IReadOnlyCollection<ChunkPos> LoadFluidIndex() => [.. _fluid.Keys];

    public byte[]? ReadFluid(ChunkPos position) =>
        _fluid.TryGetValue(position, out byte[]? blob) ? Copy(blob) : null;

    /// <summary>The three-list write, kept on the class so a caller typed to it still compiles.</summary>
    public void Write(
        IReadOnlyList<(ChunkPos Position, byte[] Blob)> chunks,
        IReadOnlyList<(int X, int Z, byte[] Blob)> columns,
        IReadOnlyList<(ChunkPos Position, byte[] Blob)> fluid) =>
        Write(new WorldBatch { Chunks = chunks, Columns = columns, Fluid = fluid });

    public void Write(WorldBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        // Every blob checked before any is kept, so a bad entry in the last list leaves the
        // others unwritten — the atomicity the SQLite store gets from its transaction, and the
        // contract suite asks both for it. The session parts too: a null machine blob after a
        // header must leave the header as it was.
        batch.Validate();

        WriteCalls++;

        foreach ((ChunkPos position, byte[] blob) in batch.Chunks)
        {
            _chunks[position] = Copy(blob)!;
            ChunkWrites++;
        }

        foreach ((int x, int z, byte[] blob) in batch.Columns)
        {
            _columns[(x, z)] = Copy(blob)!;
            ColumnWrites++;
        }

        foreach ((ChunkPos position, byte[] blob) in batch.Fluid)
        {
            _fluid[position] = Copy(blob)!;
            FluidWrites++;
        }

        if (batch.HasSession) SessionWrites++;

        if (batch.Machines is not null)
        {
            MachineWrites++;
            _machines.Clear();
            foreach (StoredMachine machine in batch.Machines)
            {
                _machines[machine.Origin] = machine with { Blob = Copy(machine.Blob)! };
            }
        }

        if (batch.Basins is not null)
        {
            BasinWrites++;
            _basins.Clear();
            foreach (StoredBasin basin in batch.Basins)
            {
                _basins[(basin.PitX, basin.PitZ)] = basin.Litres;
            }
        }

        if (batch.Header is not null)
        {
            HeaderWrites++;
            _header = Copy(batch.Header);
        }
    }

    public void Flush() => FlushCalls++;

    public void Dispose() => IsDisposed = true;

    private static byte[]? Copy(byte[]? source) => source is null ? null : [.. source];
}
