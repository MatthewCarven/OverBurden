using System.Buffers.Binary;
using System.Text;
using Overburden.Core.Building;
using Overburden.Core.Machines;
using Overburden.Core.Voxels;

namespace Overburden.Core.Persistence;

/// <summary>
/// A <see cref="MachineRow"/> as stored bytes, and back.
/// </summary>
/// <remarks>
/// <para>
/// <b>Version 1, M10a-1.</b> After <see cref="BlobFormat"/>'s frame and the origin voxel as a key
/// check: the facing (one byte), the port count (one byte), each port as a length-prefixed UTF-8
/// name and an int64, then the kind's state as a length-prefixed byte string. <b>Ports by name,
/// never by declaration order</b> — a mod that reorders the ports in its JSON would otherwise
/// rebind every stored value silently. <b>The kind's state carries its own version byte</b>, so
/// every later part can add state for its kind with no bump here and no schema bump.
/// </para>
/// <para>
/// <b>The reader validates the shape and nothing about the meaning.</b> A row whose material is a
/// hole — a removed mod — still reads, ports and state verbatim, and sits inert in the set; what
/// a declared port's bounds say about a stored value is <see cref="MachineSet.Restore"/>'s
/// business, because the declaration lives in content and the blob does not know it.
/// </para>
/// </remarks>
public static class MachineRowBlob
{
    /// <summary>The version this build writes.</summary>
    public const byte Version = 1;

    private const int HeaderBytes = BlobFormat.FrameBytes + (3 * sizeof(int));

    /// <summary>Serialises <paramref name="row"/> as it will be stored, keyed by its origin.</summary>
    public static byte[] Write(MachineRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        int size = 2;
        foreach ((string name, _) in row.Ports)
        {
            size += 1 + Encoding.UTF8.GetByteCount(name) + sizeof(long);
        }

        size += sizeof(int) + row.KindState.Length;

        var payload = new byte[size];
        int offset = 0;

        payload[offset++] = checked((byte)row.Facing);
        payload[offset++] = checked((byte)row.Ports.Count);

        foreach ((string name, long value) in row.Ports)
        {
            int nameBytes = Encoding.UTF8.GetBytes(name, payload.AsSpan(offset + 1));
            payload[offset] = checked((byte)nameBytes);
            offset += 1 + nameBytes;
            BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(offset), value);
            offset += sizeof(long);
        }

        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(offset), row.KindState.Length);
        offset += sizeof(int);
        row.KindState.CopyTo(payload.AsSpan(offset));

        // Rows are tens of bytes; compression never pays and TryCompress says so by returning
        // null, which keeps the blob inspectable. The frame still carries both lengths.
        byte[]? compressed = BlobFormat.TryCompress(payload);
        ReadOnlySpan<byte> stored = compressed ?? payload;

        var blob = new byte[HeaderBytes + stored.Length];
        BlobFormat.WriteFrame(blob, Version, compressed is not null, payload.Length, stored.Length);
        VoxelPos origin = row.Origin;
        BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(BlobFormat.FrameBytes), origin.X);
        BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(BlobFormat.FrameBytes + 4), origin.Y);
        BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(BlobFormat.FrameBytes + 8), origin.Z);
        stored.CopyTo(blob.AsSpan(HeaderBytes));

        return blob;
    }

    /// <summary>
    /// Decodes a row stored under <paramref name="origin"/> with <paramref name="materialId"/>,
    /// refusing anything it cannot account for.
    /// </summary>
    public static MachineRow Read(ReadOnlySpan<byte> blob, VoxelPos origin, int materialId)
    {
        string what = $"Machine row blob at {origin}";

        ReadOnlySpan<byte> payload = BlobFormat.ReadPayload(blob, Version, HeaderBytes, what, out _);

        var storedOrigin = new VoxelPos(
            BinaryPrimitives.ReadInt32LittleEndian(blob[BlobFormat.FrameBytes..]),
            BinaryPrimitives.ReadInt32LittleEndian(blob[(BlobFormat.FrameBytes + 4)..]),
            BinaryPrimitives.ReadInt32LittleEndian(blob[(BlobFormat.FrameBytes + 8)..]));

        if (storedOrigin != origin)
        {
            throw new BlobFormatException(
                $"{what}: the blob says it is at {storedOrigin}. It was filed under the wrong key, "
                + "or the store handed back the wrong row.");
        }

        BrushCell cell = BrushCell.FromVoxel(origin);
        if (cell.OriginVoxel != origin)
        {
            throw new BlobFormatException(
                $"{what}: the origin is not on the 1 m brush lattice, so no body can stand there.");
        }

        if (materialId < 0)
        {
            throw new BlobFormatException($"{what}: material {materialId} is not a numeric ID.");
        }

        int offset = 0;
        Need(payload, offset, 2, what, "the facing and the port count");

        byte facingByte = payload[offset++];
        if (!Enum.IsDefined((BrushFacing)facingByte))
        {
            throw new BlobFormatException($"{what}: facing {facingByte} is not a facing.");
        }

        int portCount = payload[offset++];
        if (portCount > PortDefinition.MaxPorts)
        {
            throw new BlobFormatException(
                $"{what}: declares {portCount} ports; a machine holds at most {PortDefinition.MaxPorts}.");
        }

        var ports = new List<KeyValuePair<string, long>>(portCount);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < portCount; i++)
        {
            Need(payload, offset, 1, what, $"port {i}'s name length");
            int nameBytes = payload[offset++];
            Need(payload, offset, nameBytes + sizeof(long), what, $"port {i}'s name and value");

            string name = Encoding.UTF8.GetString(payload.Slice(offset, nameBytes));
            offset += nameBytes;

            if (!PortDefinition.IsValidName(name))
            {
                throw new BlobFormatException($"{what}: '{name}' is not a port name.");
            }

            if (!seen.Add(name))
            {
                throw new BlobFormatException($"{what}: port '{name}' appears twice.");
            }

            long value = BinaryPrimitives.ReadInt64LittleEndian(payload[offset..]);
            offset += sizeof(long);
            ports.Add(new KeyValuePair<string, long>(name, value));
        }

        Need(payload, offset, sizeof(int), what, "the kind state's length");
        int stateLength = BinaryPrimitives.ReadInt32LittleEndian(payload[offset..]);
        offset += sizeof(int);

        if (stateLength < 0)
        {
            throw new BlobFormatException($"{what}: declares a negative kind state length ({stateLength}).");
        }

        Need(payload, offset, stateLength, what, "the kind state");
        byte[] state = payload.Slice(offset, stateLength).ToArray();
        offset += stateLength;

        if (offset != payload.Length)
        {
            throw new BlobFormatException(
                $"{what}: {payload.Length - offset} byte(s) follow the kind state. The blob has been appended to.");
        }

        return new MachineRow(cell, materialId, (BrushFacing)facingByte, ports, state);
    }

    private static void Need(ReadOnlySpan<byte> payload, int offset, int bytes, string what, string forWhat)
    {
        if (payload.Length - offset < bytes)
        {
            throw new BlobFormatException(
                $"{what}: payload ends {bytes - (payload.Length - offset)} byte(s) short of {forWhat}.");
        }
    }
}
