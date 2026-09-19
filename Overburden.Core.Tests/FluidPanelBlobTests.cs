using System.Buffers.Binary;
using Overburden.Core.Fluid;
using Overburden.Core.Persistence;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// M8b-4b: one chunk's water as bytes, and back, in M6a's shape — a round trip, a hand-decoded
/// write, a hand-built read, truncation, corruption, and every validation the reader makes.
/// </summary>
/// <remarks>
/// <b>The round trip is the weak test and the hand-built halves are the strong ones</b>, for the
/// reason M6a gave: a round trip runs the same writer and reader, so a symmetric mistake — both
/// halves agreeing on the wrong layout — passes it. <see cref="AWrittenBlobDecodesByHand"/> pins
/// the layout independently of the reader and <see cref="AHandBuiltBlobIsRead"/> independently
/// of the writer.
/// </remarks>
public class FluidPanelBlobTests
{
    private const int Cap = FluidConstants.CellCapacityMillilitres;
    private const int Cells = VoxelConstants.ChunkVolume;

    private static readonly ChunkPos At = new(3, -2, 7);

    /// <summary>A few hundred cells along a corridor, some awake — the tunnel shape.</summary>
    private static FluidPanelSnapshot Tunnel()
    {
        var cells = new List<(ushort, ushort)>();
        var awake = new List<ushort>();
        for (int x = 0; x < 32; x++)
        {
            for (int z = 4; z < 12; z++)
            {
                int index = VoxelPos.Index(x, 8, z);
                cells.Add(((ushort)index, (ushort)(1 + ((x * 7 + z * 13) % Cap))));
                if ((x + z) % 5 == 0) awake.Add((ushort)index);
            }
        }

        cells.Sort();
        awake.Sort();
        return new FluidPanelSnapshot(At, [.. cells], [.. awake]);
    }

    /// <summary>Every cell full but a stepped top, a handful awake — the lake shape.</summary>
    private static FluidPanelSnapshot Lake()
    {
        var cells = new List<(ushort, ushort)>();
        for (int index = 0; index < Cells; index++)
        {
            (int x, int y, int z) = VoxelPos.FromIndex(index);
            if (y == 31 && (x + z) % 3 == 0) continue;
            cells.Add(((ushort)index, (ushort)(y == 31 ? 4_000 + x : Cap)));
        }

        cells.Sort();
        ushort[] awake = [(ushort)VoxelPos.Index(0, 31, 1), (ushort)VoxelPos.Index(5, 31, 2), (ushort)VoxelPos.Index(31, 31, 31)];
        return new FluidPanelSnapshot(At, [.. cells], awake);
    }

    private static void AssertSame(FluidPanelSnapshot expected, FluidPanelSnapshot actual)
    {
        Assert.Equal(expected.Chunk, actual.Chunk);
        Assert.Equal(expected.Cells.Length, actual.Cells.Length);
        for (int i = 0; i < expected.Cells.Length; i++)
        {
            Assert.Equal(expected.Cells[i], actual.Cells[i]);
        }

        Assert.Equal(expected.Awake, actual.Awake);
        Assert.Equal(expected.TotalMillilitres, actual.TotalMillilitres);
    }

    // -------------------------------------------------------------------------------------
    // Round trips, both forms, both shapes.
    // -------------------------------------------------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(null)]
    public void ATunnelRoundTrips(bool? dense) =>
        AssertSame(Tunnel(), FluidPanelBlob.Read(FluidPanelBlob.Write(Tunnel(), dense), At));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(null)]
    public void ALakeRoundTrips(bool? dense) =>
        AssertSame(Lake(), FluidPanelBlob.Read(FluidPanelBlob.Write(Lake(), dense), At));

    [Fact]
    public void AnEmptyPanelRoundTripsAsEmpty()
    {
        // What a drained panel writes, so the row on disk empties with it: a chunk stored with
        // a lake in it and drained since must come back holding nothing, not the lake.
        FluidPanelSnapshot restored = FluidPanelBlob.Read(FluidPanelBlob.Write(FluidPanelSnapshot.Empty(At)), At);
        Assert.Empty(restored.Cells);
        Assert.Empty(restored.Awake);
        Assert.Equal(0L, restored.TotalMillilitres);
    }

    [Fact]
    public void TheWriterPicksTheDenseFormOnlyPastTheCrossover()
    {
        // Kind is the first payload byte. Uncompressed here would be simplest, but both shapes
        // compress, so the kind is read back through the reader's own decompression by comparing
        // against the forced forms byte for byte.
        Assert.Equal(FluidPanelBlob.Write(Tunnel(), dense: false), FluidPanelBlob.Write(Tunnel()));
        Assert.Equal(FluidPanelBlob.Write(Lake(), dense: true), FluidPanelBlob.Write(Lake()));
        Assert.NotEqual(FluidPanelBlob.Write(Lake(), dense: false), FluidPanelBlob.Write(Lake()));
    }

    // -------------------------------------------------------------------------------------
    // The two halves, each checked without the other.
    // -------------------------------------------------------------------------------------

    [Fact]
    public void AWrittenBlobDecodesByHand()
    {
        // Small enough that zlib cannot shrink it, so the payload is stored verbatim and can be
        // read straight off the frame. Three cells at chosen indices, one awake.
        var panel = new FluidPanelSnapshot(
            new ChunkPos(1, -2, 3),
            [(5, 100), (300, Cap), (32767, 1)],
            [300]);

        byte[] blob = FluidPanelBlob.Write(panel, dense: false);

        Assert.Equal(FluidPanelBlob.Version, blob[0]);
        Assert.Equal(0, blob[1] & 0x01); // not compressed, or nothing below is the layout

        int payloadLength = BinaryPrimitives.ReadInt32LittleEndian(blob.AsSpan(2));
        int storedLength = BinaryPrimitives.ReadInt32LittleEndian(blob.AsSpan(6));
        Assert.Equal(payloadLength, storedLength);
        Assert.Equal(1 + 4 + (3 * 4) + 4 + 2, payloadLength);

        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(blob.AsSpan(10)));
        Assert.Equal(-2, BinaryPrimitives.ReadInt32LittleEndian(blob.AsSpan(14)));
        Assert.Equal(3, BinaryPrimitives.ReadInt32LittleEndian(blob.AsSpan(18)));

        int p = 22;
        Assert.Equal(0, blob[p++]); // sparse
        Assert.Equal(3, BinaryPrimitives.ReadInt32LittleEndian(blob.AsSpan(p)));
        p += 4;

        Assert.Equal(5, BinaryPrimitives.ReadUInt16LittleEndian(blob.AsSpan(p)));
        Assert.Equal(100, BinaryPrimitives.ReadUInt16LittleEndian(blob.AsSpan(p + 2)));
        Assert.Equal(300, BinaryPrimitives.ReadUInt16LittleEndian(blob.AsSpan(p + 4)));
        Assert.Equal(Cap, BinaryPrimitives.ReadUInt16LittleEndian(blob.AsSpan(p + 6)));
        Assert.Equal(32767, BinaryPrimitives.ReadUInt16LittleEndian(blob.AsSpan(p + 8)));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(blob.AsSpan(p + 10)));
        p += 12;

        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(blob.AsSpan(p)));
        Assert.Equal(300, BinaryPrimitives.ReadUInt16LittleEndian(blob.AsSpan(p + 4)));
        Assert.Equal(p + 6, blob.Length);
    }

    /// <summary>An uncompressed sparse blob for <see cref="At"/>, built without the writer.</summary>
    private static byte[] HandBuilt(
        (ushort Index, ushort Millilitres)[] cells, ushort[] awake, byte kind = 0, int? declaredCount = null)
    {
        int payloadLength = 1 + 4 + (cells.Length * 4) + 4 + (awake.Length * 2);
        var blob = new byte[22 + payloadLength];
        blob[0] = FluidPanelBlob.Version;
        blob[1] = 0;
        BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(2), payloadLength);
        BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(6), payloadLength);
        BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(10), At.X);
        BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(14), At.Y);
        BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(18), At.Z);

        int p = 22;
        blob[p++] = kind;
        BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(p), declaredCount ?? cells.Length);
        p += 4;
        foreach ((ushort index, ushort millilitres) in cells)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(p), index);
            BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(p + 2), millilitres);
            p += 4;
        }

        BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(p), awake.Length);
        p += 4;
        foreach (ushort index in awake)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(blob.AsSpan(p), index);
            p += 2;
        }

        return blob;
    }

    [Fact]
    public void AHandBuiltBlobIsRead()
    {
        FluidPanelSnapshot read = FluidPanelBlob.Read(HandBuilt([(7, 9), (8, Cap)], [8]), At);

        Assert.Equal(At, read.Chunk);
        Assert.Equal([(7, 9), (8, Cap)], read.Cells);
        Assert.Equal([8], read.Awake);
        Assert.Equal(9L + Cap, read.TotalMillilitres);
    }

    // -------------------------------------------------------------------------------------
    // What the reader refuses. Each is a state the automaton relies on never seeing.
    // -------------------------------------------------------------------------------------

    [Fact]
    public void ABlobFiledUnderTheWrongKeyIsRefused()
    {
        byte[] blob = FluidPanelBlob.Write(Tunnel());
        Assert.Throws<BlobFormatException>(() => FluidPanelBlob.Read(blob, new ChunkPos(At.X + 1, At.Y, At.Z)));
        Assert.Throws<BlobFormatException>(() => FluidPanelBlob.Read(blob, new ChunkPos(At.X, At.Y + 1, At.Z)));
        Assert.Throws<BlobFormatException>(() => FluidPanelBlob.Read(blob, new ChunkPos(At.X, At.Y, At.Z - 1)));
    }

    [Fact]
    public void AnUnknownVersionIsRefused()
    {
        byte[] blob = FluidPanelBlob.Write(Tunnel());
        blob[0] = FluidPanelBlob.Version + 1;
        Assert.Throws<BlobFormatException>(() => FluidPanelBlob.Read(blob, At));
    }

    [Fact]
    public void AnUnknownKindIsRefused() =>
        Assert.Throws<BlobFormatException>(() => FluidPanelBlob.Read(HandBuilt([(1, 1)], [], kind: 2), At));

    [Fact]
    public void ACellOverCapacityIsRefused()
    {
        // The termination proof bounds Σ(amount × Y) below by every cell holding at most its
        // capacity; a stored cell over it would be water the automaton can neither move nor keep.
        Assert.Throws<BlobFormatException>(() => FluidPanelBlob.Read(HandBuilt([(1, (ushort)(Cap + 1))], []), At));
    }

    [Fact]
    public void AnEmptyStoredCellIsRefused() =>
        Assert.Throws<BlobFormatException>(() => FluidPanelBlob.Read(HandBuilt([(1, 0)], []), At));

    [Fact]
    public void ARepeatedOrDescendingIndexIsRefused()
    {
        // A repeated index counts one cell's water twice against the identity, and the restore
        // would write the cell twice — the second write replacing the first and the ledger
        // keeping both.
        Assert.Throws<BlobFormatException>(() => FluidPanelBlob.Read(HandBuilt([(5, 1), (5, 1)], []), At));
        Assert.Throws<BlobFormatException>(() => FluidPanelBlob.Read(HandBuilt([(6, 1), (5, 1)], []), At));
    }

    [Fact]
    public void ACountBeyondTheChunkIsRefused() =>
        Assert.Throws<BlobFormatException>(() => FluidPanelBlob.Read(HandBuilt([(1, 1)], [], declaredCount: Cells + 1), At));

    [Fact]
    public void AnAwakeCellThatHoldsNothingIsDroppedAndCounted()
    {
        // Refused until 2026-09-19, when a played world's row named one and the refusal holed the
        // chunk for good (a faulted load is not retried). Wake refuses a cell that cannot hold and
        // holds nothing, so leaving the index out changes no water; the count is what says a
        // writer is still producing them.
        FluidPanelSnapshot read = FluidPanelBlob.Read(HandBuilt([(4, 1), (9, 1)], [4, 5, 9]), At, out int dropped);
        Assert.Equal(1, dropped);
        Assert.Equal(new ushort[] { 4, 9 }, read.Awake);
        Assert.Equal(2, read.Cells.Length);

        FluidPanelSnapshot clean = FluidPanelBlob.Read(HandBuilt([(4, 1), (9, 1)], [9]), At, out int none);
        Assert.Equal(0, none);
        Assert.Equal(new ushort[] { 9 }, clean.Awake);

        // The ascending rule is still the format's: a dropped index still has to be in order.
        Assert.Throws<BlobFormatException>(() => FluidPanelBlob.Read(HandBuilt([(4, 1), (9, 1)], [5, 3]), At));
    }

    [Fact]
    public void AnAwakeListOutOfOrderIsRefused() =>
        Assert.Throws<BlobFormatException>(() => FluidPanelBlob.Read(HandBuilt([(4, 1), (9, 1)], [9, 4]), At));

    [Fact]
    public void ATruncatedBlobIsRefused()
    {
        byte[] blob = FluidPanelBlob.Write(Tunnel());

        for (int length = 0; length < blob.Length; length++)
        {
            try
            {
                FluidPanelBlob.Read(blob[..length], At);
                Assert.Fail($"{length} of {blob.Length} bytes was accepted.");
            }
            catch (BlobFormatException)
            {
            }
        }
    }

    [Fact]
    public void AnUncompressedBlobTruncatedInsideItsListsIsRefused()
    {
        // The compressed path is caught by the stored length; this is the uncompressed one, where
        // only the reader's own arithmetic stands between a short awake list and an index read
        // off the end of the payload.
        byte[] whole = HandBuilt([(4, 1), (9, 1), (10, 3)], [4, 9]);
        for (int cut = 1; cut <= 12; cut++)
        {
            byte[] blob = whole[..^cut];
            BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(2), blob.Length - 22);
            BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(6), blob.Length - 22);
            Assert.Throws<BlobFormatException>(() => FluidPanelBlob.Read(blob, At));
        }
    }

    [Fact]
    public void CorruptionInsideAnIntactBlobIsRefused()
    {
        byte[] blob = FluidPanelBlob.Write(Lake());
        Assert.Equal(0x01, blob[1] & 0x01);

        int refused = 0;
        int candidates = 0;
        var random = new Random(4);
        for (int i = 22; i < blob.Length; i += 1 + random.Next(3))
        {
            byte[] damaged = [.. blob];
            damaged[i] ^= 0x40;
            candidates++;

            try
            {
                FluidPanelBlob.Read(damaged, At);
            }
            catch (BlobFormatException)
            {
                refused++;
            }
        }

        Assert.True(candidates > 20, $"only {candidates} bytes to damage; the fixture is too small");
        Assert.True(refused > candidates * 3 / 4, $"only {refused} of {candidates} single-bit corruptions were refused.");
    }
}
