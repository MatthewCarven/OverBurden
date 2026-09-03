using System.Buffers;
using System.Buffers.Binary;
using Overburden.Core.Lighting;
using Overburden.Core.Voxels;

namespace Overburden.Core.Persistence;

/// <summary>
/// One <see cref="SkyColumn"/> as stored bytes, and back.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the only derived structure in the engine that has to be persisted, and the reason
/// is not its size.</b> Light itself is 512 bytes a chunk recomputed in microseconds and is never
/// written. A column's open height is the topmost opaque voxel <em>across all Y</em>, so
/// re-deriving it needs every chunk between the analytic surface and the current height — and the
/// vertical view radius is 2–3 chunks, so those chunks are not loaded. That is the same absence
/// <see cref="SkyColumn"/> exists to work around: there is no top of the world to flood down from.
/// </para>
/// <para>
/// <b>The failure if it is skipped is the classic voxel bug arriving by a new route.</b> A shaft
/// reloads, its column seeds from <see cref="ISurfaceHeights.SurfaceHeightAt"/> at the
/// <em>pre-dig</em> surface, every cell in the shaft fails
/// <see cref="SkyColumn.IsCellOpenToSky"/>, and the shaft is dark for a reason nothing on screen
/// can explain. Loading a chunk without its column is worse than loading neither.
/// </para>
/// <para>
/// 2,048 bytes of <c>short</c> before compression, and the flat regions of it compress to almost
/// nothing — a column over untouched ground is 1,024 near-identical values, and one over a shaft
/// is that with a hole in it.
/// </para>
/// </remarks>
public static class SkyColumnBlob
{
    /// <summary>The only version this build writes or reads. Independent of <see cref="ChunkBlob.Version"/>.</summary>
    public const byte Version = 1;

    private const int Size = VoxelConstants.ChunkSize;
    private const int Entries = Size * Size;

    /// <summary>Frame, then cx and cz. Chunk Y is deliberately absent — a column spans all of them.</summary>
    private const int HeaderBytes = BlobFormat.FrameBytes + (2 * sizeof(int));

    private const int PayloadBytes = Entries * sizeof(short);

    /// <summary>Serialises <paramref name="column"/> as it will be stored.</summary>
    public static byte[] Write(SkyColumn column)
    {
        ArgumentNullException.ThrowIfNull(column);

        byte[] scratch = ArrayPool<byte>.Shared.Rent(PayloadBytes);

        try
        {
            Span<byte> payload = scratch.AsSpan(0, PayloadBytes);

            int offset = 0;
            for (int lz = 0; lz < Size; lz++)
            {
                for (int lx = 0; lx < Size; lx++)
                {
                    BinaryPrimitives.WriteInt16LittleEndian(
                        payload[offset..], checked((short)column.OpenAboveY(lx, lz)));
                    offset += sizeof(short);
                }
            }

            byte[]? compressed = BlobFormat.TryCompress(payload);
            ReadOnlySpan<byte> stored = compressed ?? payload;

            var blob = new byte[HeaderBytes + stored.Length];
            BlobFormat.WriteFrame(blob, Version, compressed is not null, PayloadBytes, stored.Length);
            BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(BlobFormat.FrameBytes), column.ChunkX);
            BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(BlobFormat.FrameBytes + 4), column.ChunkZ);
            stored.CopyTo(blob.AsSpan(HeaderBytes));

            return blob;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(scratch);
        }
    }

    /// <summary>
    /// Rebuilds the column stored at (<paramref name="chunkX"/>, <paramref name="chunkZ"/>).
    /// </summary>
    /// <remarks>
    /// The result comes back with <see cref="SkyColumn.HasEdits"/> already set. A column is only
    /// ever written because its heights moved away from the analytic seed, so a restored one is
    /// edit-derived by definition — and a restore that came back looking pristine would be
    /// dropped again unwritten the next time it unloaded, losing the shaft on the second visit
    /// rather than the first.
    /// </remarks>
    public static SkyColumn Read(ReadOnlySpan<byte> blob, int chunkX, int chunkZ)
    {
        string what = $"Sky column blob for ({chunkX}, {chunkZ})";

        ReadOnlySpan<byte> payload =
            BlobFormat.ReadPayload(blob, Version, HeaderBytes, what, out _);

        int storedX = BinaryPrimitives.ReadInt32LittleEndian(blob[BlobFormat.FrameBytes..]);
        int storedZ = BinaryPrimitives.ReadInt32LittleEndian(blob[(BlobFormat.FrameBytes + 4)..]);

        if (storedX != chunkX || storedZ != chunkZ)
        {
            throw new BlobFormatException(
                $"{what}: the blob says it is ({storedX}, {storedZ}). It was filed under the wrong "
                + "key, or the store handed back the wrong row.");
        }

        if (payload.Length != PayloadBytes)
        {
            throw new BlobFormatException(
                $"{what}: payload is {payload.Length} bytes, expected {PayloadBytes} "
                + $"for {Entries} columns.");
        }

        var column = new SkyColumn(chunkX, chunkZ);

        int offset = 0;
        for (int lz = 0; lz < Size; lz++)
        {
            for (int lx = 0; lx < Size; lx++)
            {
                column.SetOpenAboveY(lx, lz, BinaryPrimitives.ReadInt16LittleEndian(payload[offset..]));
                offset += sizeof(short);
            }
        }

        column.MarkEdited();
        return column;
    }
}
