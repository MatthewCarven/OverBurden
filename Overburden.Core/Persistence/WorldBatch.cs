using Overburden.Core.Voxels;

namespace Overburden.Core.Persistence;

/// <summary>
/// Everything one <see cref="IWorldStore.Write(WorldBatch)"/> lands in one transaction: the
/// chunk batch, and optionally the session — the machine rows, the basin ledger and the header.
/// </summary>
/// <remarks>
/// <para>
/// <b>A record rather than a sixth positional list, and the session rides the batch because an
/// autosave was three store calls (M10a-1).</b> Until this the header, the basins and the chunk
/// batch were written one after another, each in its own transaction — so a crash between the
/// second and the third left whatever the portal drain had moved in that interval in neither
/// tier, and a machine placed between the first and the second would have been a debit on the
/// inventory with no row, or a row with no debit. The ladder's decision 5 counted two calls; the
/// code had three. One transaction over all of it is the whole of the fix, and the record is so
/// the next part's list (M10e's conductor summaries) is a property rather than a seventh overload.
/// </para>
/// <para>
/// <b>Null means <em>leave that table alone</em>; an empty list means <em>clear it</em>.</b> The
/// unload path's asynchronous batch carries chunks and panels and no session, and must not touch
/// the machines, the basins or the header it does not have; the autosave and the quit carry the
/// session and replace all three whole, because the set in memory is the truth for each (the
/// basins' argument since M7b-2, the machines' since M10-PLAN decision 4). An empty basin list is
/// exactly what a world opened with a different watershed lattice asks for, and an empty machine
/// list is a world whose last machine was dismantled.
/// </para>
/// </remarks>
public sealed record WorldBatch
{
    /// <summary>Chunk blobs by position, replacing any stored under the same key.</summary>
    public IReadOnlyList<(ChunkPos Position, byte[] Blob)> Chunks { get; init; } = [];

    /// <summary>Sky column blobs by chunk (x, z), replacing any stored under the same key.</summary>
    public IReadOnlyList<(int X, int Z, byte[] Blob)> Columns { get; init; } = [];

    /// <summary>Fluid panel blobs by chunk, replacing any stored under the same key.</summary>
    public IReadOnlyList<(ChunkPos Position, byte[] Blob)> Fluid { get; init; } = [];

    /// <summary>The whole machine table, or null to leave it as it is.</summary>
    public IReadOnlyList<StoredMachine>? Machines { get; init; }

    /// <summary>The whole basin table, or null to leave it as it is.</summary>
    public IReadOnlyList<StoredBasin>? Basins { get; init; }

    /// <summary>The world header, or null to leave it as it is.</summary>
    public byte[]? Header { get; init; }

    /// <summary>True when nothing in the batch would change the store.</summary>
    public bool IsEmpty =>
        Chunks.Count == 0 && Columns.Count == 0 && Fluid.Count == 0
        && Machines is null && Basins is null && Header is null;

    /// <summary>True when the batch carries any part of the session.</summary>
    public bool HasSession => Machines is not null || Basins is not null || Header is not null;

    /// <summary>
    /// Refuses a null blob anywhere in the batch before a store keeps any of it.
    /// </summary>
    /// <remarks>
    /// The in-memory store's atomicity: every entry is checked before the first is kept, so a
    /// bad entry in the last list leaves the first untouched — the guarantee the SQLite store
    /// gets from its transaction, and the contract suite asks both stores for it.
    /// </remarks>
    public void Validate()
    {
        foreach ((_, byte[] blob) in Chunks) ArgumentNullException.ThrowIfNull(blob, nameof(Chunks));
        foreach ((_, _, byte[] blob) in Columns) ArgumentNullException.ThrowIfNull(blob, nameof(Columns));
        foreach ((_, byte[] blob) in Fluid) ArgumentNullException.ThrowIfNull(blob, nameof(Fluid));

        if (Machines is not null)
        {
            foreach (StoredMachine machine in Machines) ArgumentNullException.ThrowIfNull(machine.Blob, nameof(Machines));
        }
    }
}

/// <summary>
/// The session as one autosave or one quit writes it: the header's bytes, the whole basin
/// ledger and the whole machine table.
/// </summary>
/// <remarks>
/// Built by the caller that has all three — <c>Main</c> — and handed to
/// <see cref="Streaming.ChunkPipeline.SaveAll"/>, which puts it on the batch it was going to write
/// anyway. The pipeline knows nothing of headers, basins or rows; it carries bytes and records.
/// </remarks>
/// <param name="Header">The serialised <see cref="WorldHeader"/>.</param>
/// <param name="Basins">
/// Every basin holding water, keyed by pit — or null when the run has no water at all
/// (<c>--nowater</c>), which leaves the stored basins as they are rather than clearing them.
/// </param>
/// <param name="Machines">Every machine row, keyed by origin.</param>
public sealed record SessionSnapshot(
    byte[] Header,
    IReadOnlyList<StoredBasin>? Basins,
    IReadOnlyList<StoredMachine> Machines);

/// <summary>One machine's stored row: where its body's origin voxel is, what it is made of, and its blob.</summary>
/// <remarks>
/// <b>Keyed by the origin voxel, with the material beside the blob rather than inside it</b>, so
/// <c>sqlite3</c> on the command line can answer "what machines are in this world" without
/// decoding anything. The material is the world's numeric ID through the header's table — the
/// same number the chunk's palette holds for the body — so non-negotiable 2 holds for a row
/// exactly as it does for a voxel, and a removed mod leaves a row whose material is a hole.
/// </remarks>
public readonly record struct StoredMachine(VoxelPos Origin, int MaterialId, byte[] Blob)
{
    /// <summary>By the blob's bytes, not its reference — two reads of one row are one row.</summary>
    public bool Equals(StoredMachine other) =>
        Origin == other.Origin
        && MaterialId == other.MaterialId
        && (ReferenceEquals(Blob, other.Blob) || (Blob is not null && other.Blob is not null && Blob.AsSpan().SequenceEqual(other.Blob)));

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Origin);
        hash.Add(MaterialId);
        if (Blob is not null) hash.AddBytes(Blob);
        return hash.ToHashCode();
    }
}
