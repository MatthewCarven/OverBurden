using System.Buffers.Binary;
using System.Text;
using MinerCraft.Core.Content;
using MinerCraft.Core.Worldgen;

namespace MinerCraft.Core.Persistence;

/// <summary>
/// Everything a world needs before a single chunk can be interpreted.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the thing non-negotiable 2 has been describing since M1.</b> Content IDs are
/// namespaced strings resolved to numbers through a table <em>the world</em> owns;
/// <see cref="Registry{T}.ExportIdTable"/> and <see cref="Registry{T}.SeedIdTable"/> were built
/// then and have never had a caller. This is the caller.
/// </para>
/// <para>
/// <b>It carries the whole <see cref="WorldGenSettings"/> record, not just the seed</b>, and that
/// is a correctness requirement rather than tidiness. An unedited chunk is not stored — it is
/// regenerated — and it regenerates from every field, not only <see cref="WorldGenSettings.Seed"/>.
/// Change the default <see cref="WorldGenSettings.CaveLatticeStep"/> in a later build and the
/// unstored terrain around a saved base comes back a different shape, with the saved chunks
/// hanging in it. Thirteen fields, all of them load-bearing, all of them written.
/// </para>
/// </remarks>
public sealed record WorldHeader
{
    /// <summary>Everything that shapes unstored terrain. Persisted whole, for the reason above.</summary>
    public required WorldGenSettings Settings { get; init; }

    /// <summary>
    /// The material ID assignment, index = numeric ID. A <c>default</c> entry is a hole left by
    /// a removed mod and stays a hole.
    /// </summary>
    /// <remarks>
    /// Feeds straight into <see cref="GameContent.Load"/>'s <c>savedMaterialIds</c>, which is
    /// what makes stored numeric IDs mean the same thing across a change of mod set.
    /// </remarks>
    public required IReadOnlyList<Identifier> MaterialIdTable { get; init; }
}

/// <summary>The <see cref="WorldHeader"/> as stored bytes, and back.</summary>
/// <remarks>
/// <para>
/// <b>Explicit binary rather than JSON, and the reason is the failure mode rather than size.</b>
/// This is written once per world, so nothing here is about speed. But a JSON reader given a
/// document missing a field fills in the default and carries on — and a defaulted
/// <see cref="WorldGenSettings.CaveLatticeStep"/> is precisely the silent world-reshaping this
/// type exists to prevent. A length-checked binary layout refuses instead. Inspectability is the
/// real thing given up, and it is partly recovered by the payload staying uncompressed whenever
/// compression would not shrink it.
/// </para>
/// <para>
/// <b><see cref="SettingsBytes"/> is asserted against the record's actual property count by a
/// test.</b> Adding a fourteenth field to <see cref="WorldGenSettings"/> and forgetting to write
/// it here would otherwise be invisible: the header would still round-trip, and the field would
/// silently take its default on every reload.
/// </para>
/// </remarks>
public static class WorldHeaderBlob
{
    /// <summary>The only version this build writes or reads.</summary>
    /// <remarks>
    /// <b>Expect this to move during M6.</b> M6c adds the player's position and volumetric
    /// inventory, and that is a layout change. During a milestone the only person holding a saved
    /// world is the one building it, and the version byte turns "my world went strange" into
    /// "this build will not open that world" — which is the trade this whole format is making.
    /// </remarks>
    public const byte Version = 1;

    /// <summary>
    /// Bytes one <see cref="WorldGenSettings"/> occupies: 8 + 4×2 + 4×6 + 1 + 4×3.
    /// </summary>
    /// <remarks>
    /// A constant rather than a computed size, so that the test which counts the record's
    /// properties has something to compare against. See the class remarks.
    /// </remarks>
    internal const int SettingsBytes = 53;

    /// <summary>How many fields <see cref="WriteSettings"/> handles. Pinned by a test.</summary>
    internal const int SettingsFieldCount = 13;

    private static readonly Identifier Air = MaterialIds.Air;

    /// <summary>Serialises <paramref name="header"/> as it will be stored.</summary>
    public static byte[] Write(WorldHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);

        IReadOnlyList<Identifier> table = header.MaterialIdTable;

        if (table.Count == 0)
        {
            throw new ArgumentException(
                "A world's material table cannot be empty; entry 0 is always minercraft:air.",
                nameof(header));
        }

        if (table[0] != Air)
        {
            // Refused at write as well as at read, because a table this wrong reaching disk is a
            // bug upstream, and finding it here names the session that produced it rather than
            // the one that later failed to open the world.
            throw new ArgumentException(
                $"Material ID 0 is '{table[0]}', but {Air} is numeric ID 0 in every world, always. "
                + "See MaterialIds.AirNumericId.",
                nameof(header));
        }

        var encoded = new byte[table.Count][];
        int payloadLength = SettingsBytes + sizeof(int);

        for (int i = 0; i < table.Count; i++)
        {
            encoded[i] = table[i].IsValid ? Encoding.UTF8.GetBytes(table[i].ToString()) : [];

            if (encoded[i].Length > ushort.MaxValue)
            {
                throw new ArgumentException(
                    $"Identifier at ID {i} encodes to {encoded[i].Length} bytes, past the "
                    + $"{ushort.MaxValue} a length prefix can express.",
                    nameof(header));
            }

            payloadLength += sizeof(ushort) + encoded[i].Length;
        }

        var payload = new byte[payloadLength];
        WriteSettings(header.Settings, payload);

        int offset = SettingsBytes;
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(offset), table.Count);
        offset += sizeof(int);

        for (int i = 0; i < table.Count; i++)
        {
            // Length zero is a hole — an ID the world remembers and no installed mod defines.
            // It is not the same as a zero-length identifier, which Identifier forbids.
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(offset), (ushort)encoded[i].Length);
            offset += sizeof(ushort);
            encoded[i].CopyTo(payload.AsSpan(offset));
            offset += encoded[i].Length;
        }

        byte[]? compressed = BlobFormat.TryCompress(payload);
        ReadOnlySpan<byte> stored = compressed ?? payload;

        var blob = new byte[BlobFormat.FrameBytes + stored.Length];
        BlobFormat.WriteFrame(blob, Version, compressed is not null, payloadLength, stored.Length);
        stored.CopyTo(blob.AsSpan(BlobFormat.FrameBytes));

        return blob;
    }

    /// <summary>Rebuilds a world header, refusing anything it cannot interpret exactly.</summary>
    public static WorldHeader Read(ReadOnlySpan<byte> blob)
    {
        const string What = "World header";

        ReadOnlySpan<byte> payload =
            BlobFormat.ReadPayload(blob, Version, BlobFormat.FrameBytes, What, out _);

        if (payload.Length < SettingsBytes + sizeof(int))
        {
            throw new BlobFormatException(
                $"{What}: {payload.Length} bytes is too short to hold the worldgen settings and a table length.");
        }

        WorldGenSettings settings = ReadSettings(payload, What);

        int offset = SettingsBytes;
        int count = BinaryPrimitives.ReadInt32LittleEndian(payload[offset..]);
        offset += sizeof(int);

        if (count <= 0)
        {
            throw new BlobFormatException(
                $"{What}: material table holds {count} entries; every world has at least {Air}.");
        }

        var table = new Identifier[count];

        for (int i = 0; i < count; i++)
        {
            if (payload.Length - offset < sizeof(ushort))
            {
                throw new BlobFormatException(
                    $"{What}: ran out of bytes reading the length of material {i} of {count}.");
            }

            int length = BinaryPrimitives.ReadUInt16LittleEndian(payload[offset..]);
            offset += sizeof(ushort);

            if (payload.Length - offset < length)
            {
                throw new BlobFormatException(
                    $"{What}: material {i} declares {length} bytes but only "
                    + $"{payload.Length - offset} remain.");
            }

            if (length == 0)
            {
                // A hole, and it stays one. Numbers are never reused and the table is never
                // compacted, so re-installing the mod that owned this slot restores the world
                // exactly rather than reinterpreting its blocks as something else.
                table[i] = default;
            }
            else
            {
                string text = Encoding.UTF8.GetString(payload.Slice(offset, length));

                if (!Identifier.TryParse(text, Identifier.BaseNamespace, out table[i], out string? error))
                {
                    throw new BlobFormatException($"{What}: material {i} is not a valid identifier — {error}");
                }
            }

            offset += length;
        }

        if (offset != payload.Length)
        {
            throw new BlobFormatException(
                $"{What}: {payload.Length - offset} bytes left over after the material table.");
        }

        if (table[0] != Air)
        {
            // Non-negotiable 3, checked rather than assumed. MaterialIds.AirNumericId being 0 is
            // relied on by a zero-initialised array being a valid empty chunk and by the uniform
            // fast path testing emptiness against a constant. A world file where it is false does
            // not make the world wrong — it makes a constant the whole engine assumes into a lie,
            // and the symptom is stone-coloured air.
            throw new BlobFormatException(
                $"{What}: material ID 0 is '{table[0]}', but {Air} is numeric ID 0 in every world, "
                + "always. This world file was not written by this engine, or was edited by hand.");
        }

        return new WorldHeader { Settings = settings, MaterialIdTable = table };
    }

    private static void WriteSettings(WorldGenSettings settings, Span<byte> destination)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(destination, settings.Seed);
        BinaryPrimitives.WriteInt32LittleEndian(destination[8..], settings.SeaLevelVoxels);
        BinaryPrimitives.WriteSingleLittleEndian(destination[12..], settings.SurfaceAmplitudeVoxels);
        BinaryPrimitives.WriteSingleLittleEndian(destination[16..], settings.SurfaceFrequency);
        BinaryPrimitives.WriteInt32LittleEndian(destination[20..], settings.SurfaceOctaves);
        BinaryPrimitives.WriteSingleLittleEndian(destination[24..], settings.Lacunarity);
        BinaryPrimitives.WriteSingleLittleEndian(destination[28..], settings.Gain);
        destination[32] = settings.CavesEnabled ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteSingleLittleEndian(destination[33..], settings.CaveFrequency);
        BinaryPrimitives.WriteInt32LittleEndian(destination[37..], settings.CaveOctaves);
        BinaryPrimitives.WriteSingleLittleEndian(destination[41..], settings.CaveThreshold);
        BinaryPrimitives.WriteInt32LittleEndian(destination[45..], settings.CaveSurfaceMarginVoxels);
        BinaryPrimitives.WriteInt32LittleEndian(destination[49..], settings.CaveLatticeStep);
    }

    private static WorldGenSettings ReadSettings(ReadOnlySpan<byte> payload, string what)
    {
        byte caves = payload[32];
        if (caves > 1)
        {
            throw new BlobFormatException($"{what}: CavesEnabled is {caves}, which is neither 0 nor 1.");
        }

        return new WorldGenSettings
        {
            Seed = BinaryPrimitives.ReadUInt64LittleEndian(payload),
            SeaLevelVoxels = BinaryPrimitives.ReadInt32LittleEndian(payload[8..]),
            SurfaceAmplitudeVoxels = BinaryPrimitives.ReadSingleLittleEndian(payload[12..]),
            SurfaceFrequency = BinaryPrimitives.ReadSingleLittleEndian(payload[16..]),
            SurfaceOctaves = BinaryPrimitives.ReadInt32LittleEndian(payload[20..]),
            Lacunarity = BinaryPrimitives.ReadSingleLittleEndian(payload[24..]),
            Gain = BinaryPrimitives.ReadSingleLittleEndian(payload[28..]),
            CavesEnabled = caves != 0,
            CaveFrequency = BinaryPrimitives.ReadSingleLittleEndian(payload[33..]),
            CaveOctaves = BinaryPrimitives.ReadInt32LittleEndian(payload[37..]),
            CaveThreshold = BinaryPrimitives.ReadSingleLittleEndian(payload[41..]),
            CaveSurfaceMarginVoxels = BinaryPrimitives.ReadInt32LittleEndian(payload[45..]),
            CaveLatticeStep = BinaryPrimitives.ReadInt32LittleEndian(payload[49..]),
        };
    }
}
