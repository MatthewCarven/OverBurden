using System.Buffers.Binary;
using Overburden.Core.Fluid;
using Overburden.Core.Voxels;

namespace Overburden.Core.Persistence;

/// <summary>
/// One chunk's Tier B water as a value: the held cells and which of them are awake.
/// </summary>
/// <remarks>
/// <para>
/// <b>A copy taken on the sim thread, because the tick writes the panel while the serial lane
/// would be serialising it.</b> A chunk's voxels have <c>VoxelsSharedWithJob</c> and a clone on
/// the next write; a panel has nothing of the kind, and giving it copy-on-write would put a
/// branch on the automaton's one mutator. Copying the cells that matter — ~4 KB for a tunnel
/// chunk, 128 KB for a chunk that is all lake — is the M8b-4 planning pass's answer, and the
/// snapshot is immutable from then on, so a save job can hold it for as long as the lane takes.
/// </para>
/// <para>
/// <b>The awake list is state, not a hint.</b> A flood mid-flow that is stored asleep comes back as
/// a still lake with a wall of water hanging in it, and nothing wakes it until an edit lands
/// beside it. Every cell listed here is put on the paused list at restore and woken when its
/// chunk arrives, as a paused cell is; a cell listed awake that turns out to be at rest costs one
/// visit, so the list may over-approximate and must not under-approximate.
/// </para>
/// </remarks>
/// <param name="Chunk">Which chunk.</param>
/// <param name="Cells">Every held cell as (local index, millilitres), ascending by index.</param>
/// <param name="Awake">Local indices of the cells that were awake or paused, ascending. A subset of <paramref name="Cells"/>.</param>
public sealed record FluidPanelSnapshot(
    ChunkPos Chunk,
    (ushort Index, ushort Millilitres)[] Cells,
    ushort[] Awake)
{
    /// <summary>Millilitres in the snapshot.</summary>
    public long TotalMillilitres
    {
        get
        {
            long total = 0;
            foreach ((_, ushort millilitres) in Cells) total += millilitres;
            return total;
        }
    }

    /// <summary>A snapshot with no water in it: what a drained panel writes so the row on disk empties too.</summary>
    public static FluidPanelSnapshot Empty(ChunkPos chunk) => new(chunk, [], []);
}

/// <summary>
/// One <see cref="FluidPanelSnapshot"/> as stored bytes, and back.
/// </summary>
/// <remarks>
/// <para>
/// <b>On the sky column's frame, keyed by chunk, in one of two forms the writer picks per panel.</b>
/// A tunnel chunk holds a few hundred cells and is written as (index, millilitres) pairs; anything
/// wetter is written as the whole 32 × 32 × 32 panel. <b>The plan's crossover was bytes and the
/// measured one is time</b> (<c>--blob</c>, Release, 2026-09-07): zlib takes the dense panel to
/// 320–690 bytes at every shape, smaller than the pairs even for a tunnel, while the pairs cost
/// ~0.14 µs a cell to compress against the panel's flat ~120–170 µs — an all-lake panel is 6.4 ms
/// as pairs and 0.12 ms as a panel, and the played lake is 1,254 of them. The sparse form is
/// kept for the tunnel, where it is 30 µs against 82, and the reader takes both, so the threshold
/// is the writer's and can move without a format version.
/// </para>
/// <para>
/// <b>The reader validates everything the automaton relies on</b>, the way <see cref="ChunkBlob.Read"/>
/// validates against the material table: an index inside the chunk, indices strictly ascending,
/// an amount between one millilitre and a cell's capacity, and an awake list that is a subset of
/// the held cells. A blob that fails any of those is refused, because a cell holding more than
/// its capacity would break the termination proof's bound and a duplicate index would count one
/// cell's water twice against the identity.
/// </para>
/// </remarks>
public static class FluidPanelBlob
{
    /// <summary>The only version this build writes or reads. Independent of the other blobs' versions.</summary>
    public const byte Version = 1;

    private const byte KindSparse = 0;
    private const byte KindDense = 1;

    /// <summary>Frame, then cx, cy, cz.</summary>
    private const int HeaderBytes = BlobFormat.FrameBytes + (3 * sizeof(int));

    private const int Capacity = FluidConstants.CellCapacityMillilitres;
    private const int Cells = VoxelConstants.ChunkVolume;

    /// <summary>Above this many held cells the whole panel is the cheaper form to write.</summary>
    /// <remarks>
    /// <b>An estimate off the bench, not a measured knee</b>: sparse ≈ 20 µs + 0.14 µs a cell and
    /// dense ≈ 120–170 µs flat in Release, which cross near a thousand cells. Below it a tunnel
    /// panel saves ~50 µs; above it the pairs only get dearer. The bench's "tunnel, 3%" row sits
    /// on it.
    /// </remarks>
    public const int DenseAbove = 1024;

    /// <summary>Serialises <paramref name="panel"/> as it will be stored.</summary>
    /// <param name="panel">What to write.</param>
    /// <param name="dense">
    /// Forces the form: true for the whole panel, false for pairs, null to let the cell count
    /// decide. The bench's seam; the game passes null.
    /// </param>
    public static byte[] Write(FluidPanelSnapshot panel, bool? dense = null)
    {
        ArgumentNullException.ThrowIfNull(panel);

        bool useDense = dense ?? panel.Cells.Length > DenseAbove;

        int bodyBytes = useDense
            ? Cells * sizeof(ushort)
            : sizeof(int) + (panel.Cells.Length * 2 * sizeof(ushort));
        int payloadBytes = 1 + bodyBytes + sizeof(int) + (panel.Awake.Length * sizeof(ushort));

        var payload = new byte[payloadBytes];
        int offset = 0;

        payload[offset++] = useDense ? KindDense : KindSparse;

        if (useDense)
        {
            // The array is already zero, so only the held cells are written.
            foreach ((ushort index, ushort millilitres) in panel.Cells)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(
                    payload.AsSpan(offset + (index * sizeof(ushort))), millilitres);
            }

            offset += Cells * sizeof(ushort);
        }
        else
        {
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(offset), panel.Cells.Length);
            offset += sizeof(int);

            foreach ((ushort index, ushort millilitres) in panel.Cells)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(offset), index);
                BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(offset + sizeof(ushort)), millilitres);
                offset += 2 * sizeof(ushort);
            }
        }

        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(offset), panel.Awake.Length);
        offset += sizeof(int);

        foreach (ushort index in panel.Awake)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(offset), index);
            offset += sizeof(ushort);
        }

        byte[]? compressed = BlobFormat.TryCompress(payload);
        ReadOnlySpan<byte> stored = compressed ?? payload;

        var blob = new byte[HeaderBytes + stored.Length];
        BlobFormat.WriteFrame(blob, Version, compressed is not null, payloadBytes, stored.Length);
        BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(BlobFormat.FrameBytes), panel.Chunk.X);
        BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(BlobFormat.FrameBytes + 4), panel.Chunk.Y);
        BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(BlobFormat.FrameBytes + 8), panel.Chunk.Z);
        stored.CopyTo(blob.AsSpan(HeaderBytes));

        return blob;
    }

    /// <summary>Rebuilds the panel stored for <paramref name="chunk"/>, validating every cell.</summary>
    public static FluidPanelSnapshot Read(ReadOnlySpan<byte> blob, ChunkPos chunk)
    {
        string what = $"Fluid panel blob for {chunk}";

        ReadOnlySpan<byte> payload = BlobFormat.ReadPayload(blob, Version, HeaderBytes, what, out _);

        int storedX = BinaryPrimitives.ReadInt32LittleEndian(blob[BlobFormat.FrameBytes..]);
        int storedY = BinaryPrimitives.ReadInt32LittleEndian(blob[(BlobFormat.FrameBytes + 4)..]);
        int storedZ = BinaryPrimitives.ReadInt32LittleEndian(blob[(BlobFormat.FrameBytes + 8)..]);

        if (storedX != chunk.X || storedY != chunk.Y || storedZ != chunk.Z)
        {
            throw new BlobFormatException(
                $"{what}: the blob says it is ({storedX}, {storedY}, {storedZ}). It was filed under "
                + "the wrong key, or the store handed back the wrong row.");
        }

        if (payload.Length < 1 + sizeof(int))
        {
            throw new BlobFormatException($"{what}: payload is {payload.Length} bytes, too short for a kind and a count.");
        }

        int offset = 0;
        byte kind = payload[offset++];

        (ushort Index, ushort Millilitres)[] cells;

        if (kind == KindSparse)
        {
            int count = BinaryPrimitives.ReadInt32LittleEndian(payload[offset..]);
            offset += sizeof(int);

            if (count < 0 || count > Cells)
            {
                throw new BlobFormatException($"{what}: declares {count} cells; a chunk has {Cells}.");
            }

            Require(payload, offset + (count * 2 * sizeof(ushort)) + sizeof(int), what, "the cells");

            cells = new (ushort, ushort)[count];
            int previous = -1;
            for (int i = 0; i < count; i++)
            {
                ushort index = BinaryPrimitives.ReadUInt16LittleEndian(payload[offset..]);
                ushort millilitres = BinaryPrimitives.ReadUInt16LittleEndian(payload[(offset + sizeof(ushort))..]);
                offset += 2 * sizeof(ushort);

                if (index >= Cells)
                {
                    throw new BlobFormatException($"{what}: cell {i} has index {index}, outside 0..{Cells - 1}.");
                }

                if (index <= previous)
                {
                    throw new BlobFormatException(
                        $"{what}: cell {i} has index {index} after {previous}; indices must strictly ascend, "
                        + "or one cell's water is counted twice.");
                }

                if (millilitres == 0 || millilitres > Capacity)
                {
                    throw new BlobFormatException(
                        $"{what}: cell {index} holds {millilitres} mL; a stored cell holds 1..{Capacity}.");
                }

                cells[i] = (index, millilitres);
                previous = index;
            }
        }
        else if (kind == KindDense)
        {
            Require(payload, offset + (Cells * sizeof(ushort)) + sizeof(int), what, "the panel");

            int held = 0;
            for (int index = 0; index < Cells; index++)
            {
                ushort millilitres = BinaryPrimitives.ReadUInt16LittleEndian(
                    payload[(offset + (index * sizeof(ushort)))..]);
                if (millilitres > Capacity)
                {
                    throw new BlobFormatException(
                        $"{what}: cell {index} holds {millilitres} mL; a cell holds at most {Capacity}.");
                }

                if (millilitres != 0) held++;
            }

            cells = new (ushort, ushort)[held];
            int at = 0;
            for (int index = 0; index < Cells; index++)
            {
                ushort millilitres = BinaryPrimitives.ReadUInt16LittleEndian(
                    payload[(offset + (index * sizeof(ushort)))..]);
                if (millilitres != 0)
                {
                    cells[at++] = ((ushort)index, millilitres);
                }
            }

            offset += Cells * sizeof(ushort);
        }
        else
        {
            throw new BlobFormatException($"{what}: kind {kind} is neither sparse (0) nor dense (1).");
        }

        int awakeCount = BinaryPrimitives.ReadInt32LittleEndian(payload[offset..]);
        offset += sizeof(int);

        if (awakeCount < 0 || awakeCount > cells.Length)
        {
            throw new BlobFormatException(
                $"{what}: declares {awakeCount} awake cells against {cells.Length} held; "
                + "an awake cell is a held cell.");
        }

        if (payload.Length != offset + (awakeCount * sizeof(ushort)))
        {
            throw new BlobFormatException(
                $"{what}: payload is {payload.Length} bytes but the awake list ends at "
                + $"{offset + (awakeCount * sizeof(ushort))}.");
        }

        var awake = new ushort[awakeCount];
        int cursor = 0;
        int last = -1;
        for (int i = 0; i < awakeCount; i++)
        {
            ushort index = BinaryPrimitives.ReadUInt16LittleEndian(payload[offset..]);
            offset += sizeof(ushort);

            if (index <= last)
            {
                throw new BlobFormatException(
                    $"{what}: awake cell {i} has index {index} after {last}; the list must strictly ascend.");
            }

            // Both lists ascend, so the subset check is one merge rather than a search per entry.
            while (cursor < cells.Length && cells[cursor].Index < index) cursor++;
            if (cursor == cells.Length || cells[cursor].Index != index)
            {
                throw new BlobFormatException(
                    $"{what}: awake cell {index} holds nothing; an awake cell is a held cell.");
            }

            awake[i] = index;
            last = index;
        }

        return new FluidPanelSnapshot(chunk, cells, awake);
    }

    private static void Require(ReadOnlySpan<byte> payload, int needed, string what, string forWhat)
    {
        if (payload.Length < needed)
        {
            throw new BlobFormatException(
                $"{what}: payload is {payload.Length} bytes, but {forWhat} need at least {needed}.");
        }
    }
}

/// <summary>
/// What the save path asks of the fluid simulation: a copy of every panel worth writing.
/// </summary>
/// <remarks>
/// <para>
/// <b>An interface rather than the simulation itself, because the pipeline is constructed before
/// the simulation can be.</b> The automaton's terrain is the pipeline's own streamer, so the
/// pipeline cannot take it in its constructor; it takes this through a property once the
/// simulation exists, and the pipeline's tests take a fake.
/// </para>
/// <para>
/// <b>"Dirty" is the grid's word — a panel written since it was last snapshotted, or a panel
/// released since — and "worth writing" adds every panel with an awake cell in it</b>, because
/// the awake list is state (see <see cref="FluidPanelSnapshot"/>) and a panel whose cells are
/// about to move is a panel whose row on disk is stale. Taking a snapshot clears the panel's
/// dirty bit, <c>DirtySinceSave</c>'s rule: cleared at submission, so a write landing during the
/// save marks it again and it is written twice rather than not at all.
/// </para>
/// </remarks>
public interface IFluidPersistence
{
    /// <summary>
    /// A snapshot of one chunk's panel if it is dirty, else null. For the unload path, where the
    /// chunk is one of a shell leaving in one frame.
    /// </summary>
    /// <remarks>
    /// Lists as awake only what is <em>paused</em> in the chunk, not what is queued, because
    /// finding a chunk's queued cells means walking the whole active set and this is called once
    /// per chunk of an unloading shell. The chunk's queued cells are paused by the next tick and
    /// listed by the next <see cref="TakeAllDirty"/>, which walks the active set once — so the
    /// snapshot taken here can under-list the awake cells for at most one autosave interval,
    /// and the autosave's own crash window already covers that.
    /// </remarks>
    FluidPanelSnapshot? TakeDirty(ChunkPos chunk);

    /// <summary>
    /// Snapshots of every panel worth writing — dirty, released, holding an awake cell, or whose
    /// row lists awake cells that have since rested — with the awake list exact. For the
    /// autosave and the quit.
    /// </summary>
    /// <remarks>
    /// The last of those is what keeps the awake list from over-approximating for ever: a cell
    /// that rests writes nothing, so its panel is clean while its row still says awake, and
    /// without a rewrite every open would wake it again. After this the rows list exactly the
    /// cells that are awake now.
    /// </remarks>
    void TakeAllDirty(List<FluidPanelSnapshot> into);
}
