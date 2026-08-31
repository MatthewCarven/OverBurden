using System.Buffers;
using System.Buffers.Binary;
using MinerCraft.Core.Content;
using MinerCraft.Core.Voxels;

namespace MinerCraft.Core.Persistence;

/// <summary>
/// One <see cref="VoxelChunk"/> as stored bytes, and back.
/// </summary>
/// <remarks>
/// <para>
/// <b>The packed representation is written verbatim, palette and all.</b> It is 4× smaller than
/// the voxels before compression and needs no per-voxel loop in either direction, which is the
/// whole reason <see cref="VoxelChunk"/> has a palette tier at all. The alternative — writing
/// 32,768 material IDs and rebuilding the representation on load — would be representation-
/// independent, and would spend a full promote-and-repack cycle on every chunk read.
/// </para>
/// <para>
/// <b>The palette is written as world numeric IDs.</b> Those are stable across a change of mod
/// set because the world owns the table that produced them; per-chunk palette <em>indices</em>
/// are not stable across anything, and a blob holding them would round-trip perfectly inside one
/// session and decode to the wrong materials the moment a mod moved. That is the exact failure
/// non-negotiable 2 exists to prevent, and this is the one door it could arrive through.
/// </para>
/// <para>
/// <b>The chunk's own position is in the header and is checked on read.</b> It duplicates the
/// key the store filed the blob under, which is the point: a blob written under the wrong key
/// becomes a loud refusal instead of terrain appearing somewhere it does not belong. Twelve
/// bytes, and it makes a blob self-describing under <c>sqlite3</c> on the command line — which
/// is half of why DESIGN calls SQLite the pragmatic choice.
/// </para>
/// </remarks>
public static class ChunkBlob
{
    /// <summary>
    /// The only version this build writes or reads.
    /// </summary>
    /// <remarks>
    /// Bumped whenever the layout below changes in any way. A reader that met a newer blob would
    /// have no way to tell a changed field from a corrupt one, so <see cref="BlobFormat"/>
    /// refuses outright — see <see cref="BlobFormatException"/> for why repairing is the wrong
    /// instinct here.
    /// </remarks>
    public const byte Version = 1;

    private const byte KindUniform = 0;
    private const byte KindPalettised = 1;
    private const byte KindDirect = 2;

    /// <summary>Frame, then cx, cy, cz. All uncompressed, so a refusal costs no inflate.</summary>
    private const int HeaderBytes = BlobFormat.FrameBytes + (3 * sizeof(int));

    /// <summary>Largest payload any chunk can produce — direct storage, which never compresses well.</summary>
    private const int MaxPayloadBytes = 1 + (VoxelConstants.ChunkVolume * sizeof(ushort));

    /// <summary>Serialises <paramref name="chunk"/> as it will be stored.</summary>
    /// <param name="compact">
    /// Run <see cref="VoxelChunk.Compact"/> first, dropping palette entries the chunk no longer
    /// contains and narrowing the packing width to suit.
    /// <para>
    /// <b>Off by default, and that is a measurement rather than a preference.</b> <c>Compact</c>
    /// carried the comment "this runs on serialisation" from M1 with no caller at all, so M6a
    /// priced it over 2,048 real chunks in Release before writing the default down. On a carved
    /// chunk it costs <b>13× the entire write</b> (271 µs against 20) and saves <b>nothing</b> —
    /// carving air into a chunk that already holds air leaves the palette exactly as it was. On
    /// the case it exists for, a chunk that has lost the last voxel of some material, it costs
    /// <b>10× the write</b> and saves <b>7.5%</b> of a blob already compressed 22×. Roughly
    /// 100 µs of CPU for five bytes.
    /// </para>
    /// <para>
    /// True is kept as the A/B arm the bench measures, and as the answer for a caller that
    /// really does want the smallest representation. See <c>MinerCraft.Bench --blob</c>.
    /// </para>
    /// </param>
    public static byte[] Write(ChunkPos position, VoxelChunk chunk, bool compact = false)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        if (compact)
        {
            chunk.Compact();
        }

        byte[] scratch = ArrayPool<byte>.Shared.Rent(MaxPayloadBytes);

        try
        {
            int payloadLength = WritePayload(chunk, scratch);
            ReadOnlySpan<byte> payload = scratch.AsSpan(0, payloadLength);

            byte[]? compressed = BlobFormat.TryCompress(payload);
            ReadOnlySpan<byte> stored = compressed ?? payload;

            var blob = new byte[HeaderBytes + stored.Length];
            BlobFormat.WriteFrame(blob, Version, compressed is not null, payloadLength, stored.Length);
            BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(BlobFormat.FrameBytes), position.X);
            BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(BlobFormat.FrameBytes + 4), position.Y);
            BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(BlobFormat.FrameBytes + 8), position.Z);
            stored.CopyTo(blob.AsSpan(HeaderBytes));

            return blob;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(scratch);
        }
    }

    /// <summary>
    /// Rebuilds the chunk stored at <paramref name="expected"/>, refusing anything it cannot
    /// interpret exactly.
    /// </summary>
    /// <param name="materials">
    /// Validated against, not optional. Every material the blob names must exist in this world's
    /// table.
    /// <para>
    /// <b>Required rather than a separate call the caller might skip.</b> M3c recorded the
    /// hazard — <c>MaterialTable</c> indexes raw, so an ID past its <c>Count</c> throws
    /// <see cref="IndexOutOfRangeException"/> from inside the mesher on a worker thread — and
    /// noted the fix was validating chunk contents at load. A validation step that can be
    /// forgotten is the shape M5c-2 found twice: every layer tested, the wire between them not.
    /// </para>
    /// </param>
    public static VoxelChunk Read(ReadOnlySpan<byte> blob, ChunkPos expected, MaterialTable materials)
    {
        ArgumentNullException.ThrowIfNull(materials);

        string what = $"Chunk blob for {expected}";

        ReadOnlySpan<byte> payload =
            BlobFormat.ReadPayload(blob, Version, HeaderBytes, what, out _);

        var stored = new ChunkPos(
            BinaryPrimitives.ReadInt32LittleEndian(blob[BlobFormat.FrameBytes..]),
            BinaryPrimitives.ReadInt32LittleEndian(blob[(BlobFormat.FrameBytes + 4)..]),
            BinaryPrimitives.ReadInt32LittleEndian(blob[(BlobFormat.FrameBytes + 8)..]));

        if (stored != expected)
        {
            throw new BlobFormatException(
                $"{what}: the blob says it is {stored}. It was filed under the wrong key, or the "
                + "store handed back the wrong row.");
        }

        if (payload.IsEmpty)
        {
            throw new BlobFormatException($"{what}: payload is empty.");
        }

        return payload[0] switch
        {
            KindUniform => ReadUniform(payload[1..], materials, what),
            KindPalettised => ReadPalettised(payload[1..], materials, what),
            KindDirect => ReadDirect(payload[1..], materials, what),
            _ => throw new BlobFormatException($"{what}: unknown storage kind {payload[0]}."),
        };
    }

    private static int WritePayload(VoxelChunk chunk, Span<byte> destination)
    {
        switch (chunk.Kind)
        {
            case VoxelChunk.StorageKind.Uniform:
                destination[0] = KindUniform;
                BinaryPrimitives.WriteInt32LittleEndian(destination[1..], chunk.UniformValue);
                return 1 + sizeof(int);

            case VoxelChunk.StorageKind.Palettised:
            {
                ReadOnlySpan<int> palette = chunk.PaletteEntries;
                ReadOnlySpan<ulong> words = chunk.PackedWords;

                destination[0] = KindPalettised;

                // Count is written as the value, not the value minus one, even though 256 does
                // not fit a byte and 0 is impossible. Off-by-one encodings save one byte here
                // and cost an afternoon the first time somebody reads the format by eye.
                BinaryPrimitives.WriteUInt16LittleEndian(destination[1..], (ushort)palette.Length);
                destination[3] = (byte)chunk.BitsPerIndex;

                int offset = 4;
                foreach (int material in palette)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(destination[offset..], material);
                    offset += sizeof(int);
                }

                foreach (ulong word in words)
                {
                    BinaryPrimitives.WriteUInt64LittleEndian(destination[offset..], word);
                    offset += sizeof(ulong);
                }

                return offset;
            }

            default:
            {
                ReadOnlySpan<ushort> voxels = chunk.DirectVoxels;
                destination[0] = KindDirect;

                int offset = 1;
                foreach (ushort material in voxels)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(destination[offset..], material);
                    offset += sizeof(ushort);
                }

                return offset;
            }
        }
    }

    private static VoxelChunk ReadUniform(ReadOnlySpan<byte> body, MaterialTable materials, string what)
    {
        Require(body.Length == sizeof(int), what, $"uniform payload is {body.Length} bytes, expected 4");

        int material = BinaryPrimitives.ReadInt32LittleEndian(body);
        RequireKnownMaterial(material, materials, what);
        return new VoxelChunk(material);
    }

    private static VoxelChunk ReadPalettised(ReadOnlySpan<byte> body, MaterialTable materials, string what)
    {
        Require(body.Length >= 3, what, "palettised payload is too short to hold its own header");

        int count = BinaryPrimitives.ReadUInt16LittleEndian(body);
        int bits = body[2];

        Require(count > 0, what, "palette is empty");
        Require(bits is 1 or 2 or 4 or 8, what, $"packing width {bits} is not 1, 2, 4 or 8");

        int words = VoxelChunk.WordsFor(bits);
        int expected = 3 + (count * sizeof(int)) + (words * sizeof(ulong));
        Require(
            body.Length == expected,
            what,
            $"palettised payload is {body.Length} bytes, expected {expected} "
            + $"for {count} entries at {bits} bits");

        var palette = new int[count];
        int offset = 3;
        for (int i = 0; i < count; i++)
        {
            palette[i] = BinaryPrimitives.ReadInt32LittleEndian(body[offset..]);
            RequireKnownMaterial(palette[i], materials, what);
            offset += sizeof(int);
        }

        var packed = new ulong[words];
        for (int i = 0; i < words; i++)
        {
            packed[i] = BinaryPrimitives.ReadUInt64LittleEndian(body[offset..]);
            offset += sizeof(ulong);
        }

        // Everything past this point — index range, palette duplicates, width sufficiency — is
        // VoxelChunk's to check, because it is the thing that has to survive being read from.
        try
        {
            return VoxelChunk.FromPalettised(palette, bits, packed);
        }
        catch (Exception error) when (error is ArgumentException or ArgumentOutOfRangeException)
        {
            throw new BlobFormatException($"{what}: {error.Message}");
        }
    }

    private static VoxelChunk ReadDirect(ReadOnlySpan<byte> body, MaterialTable materials, string what)
    {
        int expected = VoxelConstants.ChunkVolume * sizeof(ushort);
        Require(body.Length == expected, what, $"direct payload is {body.Length} bytes, expected {expected}");

        var voxels = new ushort[VoxelConstants.ChunkVolume];
        for (int i = 0; i < voxels.Length; i++)
        {
            voxels[i] = BinaryPrimitives.ReadUInt16LittleEndian(body[(i * sizeof(ushort))..]);
            RequireKnownMaterial(voxels[i], materials, what);
        }

        return VoxelChunk.FromDirect(voxels);
    }

    private static void RequireKnownMaterial(int material, MaterialTable materials, string what)
    {
        if ((uint)material >= (uint)materials.Count)
        {
            throw new BlobFormatException(
                $"{what}: material ID {material} is past this world's table, which holds "
                + $"{materials.Count}. The world file's ID table and its chunks disagree.");
        }
    }

    private static void Require(bool condition, string what, string problem)
    {
        if (!condition)
        {
            throw new BlobFormatException($"{what}: {problem}.");
        }
    }
}
