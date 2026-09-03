using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using Overburden.Core.Content;
using Overburden.Core.Hydrology;
using Overburden.Core.Voxels;
using Overburden.Core.Worldgen;

namespace Overburden.Core.Persistence;

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

    /// <summary>
    /// What the player is carrying, in voxels, by numeric material ID. Empty for a new world.
    /// </summary>
    /// <remarks>
    /// <b>Here rather than in a file of its own, for the reason the ID table is here:</b> an
    /// inventory keyed by numeric material ID is meaningless without the table that assigns those
    /// numbers, and two files can desynchronise while one cannot. Ordered by ID, so the same
    /// inventory always writes the same bytes.
    /// </remarks>
    public IReadOnlyList<InventoryEntry> Inventory { get; init; } = [];

    /// <summary>
    /// Simulated milliseconds this world has been running. Zero for a world nobody has entered.
    /// </summary>
    /// <remarks>
    /// <b>Nothing needed time for six milestones.</b> Worldgen is a pure function of position,
    /// light is a fixpoint, physics takes <c>dt</c> and remembers nothing. M7's rainfall is a pure
    /// function of (seed, time), so this field is half of what makes a fill resume where it left
    /// off — and M8's water and M9's collapse delay both want it too. <b>It advances only while
    /// the world is open</b>: see <see cref="WorldClock"/> for why, and for the trigger that would
    /// change it.
    /// </remarks>
    public long ElapsedMillis { get; init; }

    /// <summary>The watershed lattice this world's stored basin volumes are keyed on.</summary>
    /// <remarks>
    /// <b>A basin is identified by the world coordinate of its pit, and a pit is a sample on this
    /// lattice</b> — so changing the lattice renames every basin in the world. Stored here so a
    /// build that changes it can notice, rather than quietly reattaching stored water to whatever
    /// pit happens to be nearest. <b>The policy is to discard the volumes rather than refuse the
    /// world</b>: they refill in minutes of rain, and refusing to open a world over a derived
    /// quantity is the worse failure. Defaulted from the lattice itself, so there is one
    /// definition of what 4 m means.
    /// </remarks>
    public int WatershedCellMetres { get; init; } =
        HeightField.DefaultCellVoxels / VoxelConstants.VoxelsPerMetre;

    /// <summary>Where the player was and how they were moving. Null for a world nobody has entered.</summary>
    /// <remarks>
    /// <b>Optional rather than defaulted to the origin</b>, because "no saved position" and
    /// "saved at 0,0,0" are different facts and only the first should spawn a player at the
    /// surface. A world written before M6c-2 reads as null, which is exactly what it is.
    /// </remarks>
    public PlayerSave? Player { get; init; }
}

/// <summary>One material's holding, in voxels rather than litres.</summary>
/// <remarks>
/// Voxels because that is what <see cref="Inventory.VolumetricInventory"/> counts, and counting is
/// exact where a litre figure is a presentation of it. See that type for the whole argument and
/// for the trigger that would change it.
/// </remarks>
public readonly record struct InventoryEntry(int MaterialId, long Voxels);

/// <summary>Where the player was standing, and whether they were flying.</summary>
/// <param name="FeetMetres">
/// The point between the feet, not the centre of the body — the coordinate every other system in
/// this game means by "where the player is", and the one <c>Aabb.FromFeet</c> and
/// <c>PlayerState.AtFeet</c> both take. Naming the field for it is what stops a half-height offset
/// going missing at one of the call sites.
/// </param>
/// <param name="Flying">
/// Walk or fly. Saved because it is a mode rather than a position: reopening a world in the other
/// one is the kind of thing that reads as the save having failed.
/// </param>
/// <remarks>
/// <b>Deliberately not the facing, and not the held tier.</b> Both are cheap and neither was
/// agreed; adding a field later costs one more format version, which mid-milestone is the trade
/// this format already makes. <b>Revisit trigger: M6d plays it and reopening face-first into a
/// wall reads as wrong.</b>
/// </remarks>
public readonly record struct PlayerSave(Vector3 FeetMetres, bool Flying);

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
    /// <summary>The version this build writes.</summary>
    /// <remarks>
    /// <b>Moved to 2 at M6c-2</b> for the player's position and the volumetric inventory, and
    /// <b>to 3 at M7b-2</b> for the world clock and the watershed lattice. Both are strictly
    /// additive, so both migrate under the policy in <see cref="OldestVersion"/>. During a
    /// milestone the only person holding a saved world is the one building it.
    /// </remarks>
    public const byte Version = 3;

    /// <summary>The oldest layout this build can still read.</summary>
    /// <remarks>
    /// <para>
    /// <b>Older versions are migrated rather than refused, and the reason they are allowed to be
    /// is that every change so far has been strictly additive.</b> A v1 header is a v2 header
    /// without its two trailing sections and a v2 header is a v3 one without its clock and
    /// lattice, so reading either means supplying documented defaults — no inventory, no saved
    /// player, no time elapsed — and all of them are true statements about a world written before
    /// the field existed.
    /// </para>
    /// <para>
    /// <b>This is not the thing M6a argued against.</b> That was a reader filling in a field the
    /// writer meant to write, which is how a defaulted <see cref="WorldGenSettings.CaveLatticeStep"/>
    /// silently reshapes terrain. Here the reader knows exactly which layout it has and applies a
    /// rule written for it. The distinction is the policy: <b>an additive version migrates, a
    /// version that changes what a field means is refused.</b>
    /// </para>
    /// </remarks>
    public const byte OldestVersion = 1;

    /// <summary>Bytes one <see cref="InventoryEntry"/> occupies: an int32 ID and an int64 count.</summary>
    private const int InventoryEntryBytes = sizeof(int) + sizeof(long);

    /// <summary>Bytes a present <see cref="PlayerSave"/> occupies, after its presence byte.</summary>
    private const int PlayerBytes = (sizeof(float) * 3) + 1;

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
                $"A world's material table cannot be empty; entry 0 is always {Air}.",
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

        if (FindDuplicate(table) is var (firstId, secondId, twice))
        {
            throw new ArgumentException(
                $"Material IDs {firstId} and {secondId} are both '{twice}'; a table names each "
                + "material once.",
                nameof(header));
        }

        IReadOnlyList<InventoryEntry> inventory = ValidatedInventory(header);

        var encoded = new byte[table.Count][];
        if (header.ElapsedMillis < 0)
        {
            throw new ArgumentException(
                $"A world has been running for {header.ElapsedMillis} ms, which is not a length of "
                + "time. World time does not run backwards.",
                nameof(header));
        }

        if (header.WatershedCellMetres < 1)
        {
            throw new ArgumentException(
                $"A watershed cell of {header.WatershedCellMetres} m cannot hold a lattice, and "
                + "every stored basin is keyed on one.",
                nameof(header));
        }

        int payloadLength = SettingsBytes
                            + sizeof(int)
                            + sizeof(int)
                            + (inventory.Count * InventoryEntryBytes)
                            + 1
                            + (header.Player is null ? 0 : PlayerBytes)
                            + sizeof(long)
                            + sizeof(int);

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

        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(offset), inventory.Count);
        offset += sizeof(int);

        foreach (InventoryEntry entry in inventory)
        {
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(offset), entry.MaterialId);
            offset += sizeof(int);
            BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(offset), entry.Voxels);
            offset += sizeof(long);
        }

        if (header.Player is PlayerSave player)
        {
            payload[offset++] = 1;
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(offset), player.FeetMetres.X);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(offset + 4), player.FeetMetres.Y);
            BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(offset + 8), player.FeetMetres.Z);
            payload[offset + 12] = player.Flying ? (byte)1 : (byte)0;
            offset += PlayerBytes;
        }
        else
        {
            payload[offset++] = 0;
        }

        BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(offset), header.ElapsedMillis);
        offset += sizeof(long);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(offset), header.WatershedCellMetres);
        offset += sizeof(int);

        byte[]? compressed = BlobFormat.TryCompress(payload);
        ReadOnlySpan<byte> stored = compressed ?? payload;

        var blob = new byte[BlobFormat.FrameBytes + stored.Length];
        BlobFormat.WriteFrame(blob, Version, compressed is not null, payloadLength, stored.Length);
        stored.CopyTo(blob.AsSpan(BlobFormat.FrameBytes));

        return blob;
    }

    /// <summary>
    /// Checks the inventory against the table it is keyed by, and returns it in canonical order.
    /// </summary>
    /// <remarks>
    /// <b>Refused at write as well as at read</b>, for the reason the air check gives: a bad
    /// inventory reaching disk is a bug upstream, and catching it here names the session that
    /// produced it rather than the one that later cannot open the world. Sorted here rather than
    /// trusted from the caller, so that the same holding always produces the same bytes whatever
    /// order it arrived in.
    /// </remarks>
    private static IReadOnlyList<InventoryEntry> ValidatedInventory(WorldHeader header)
    {
        IReadOnlyList<InventoryEntry> inventory = header.Inventory;
        if (inventory.Count == 0) return inventory;

        var sorted = new List<InventoryEntry>(inventory);
        sorted.Sort(static (a, b) => a.MaterialId.CompareTo(b.MaterialId));

        for (int i = 0; i < sorted.Count; i++)
        {
            InventoryEntry entry = sorted[i];

            if (entry.MaterialId == MaterialIds.AirNumericId)
            {
                throw new ArgumentException(
                    "The inventory holds air. Nothing can mine air, so a holding of it is a "
                    + "bookkeeping bug rather than a quantity.",
                    nameof(header));
            }

            if ((uint)entry.MaterialId >= (uint)header.MaterialIdTable.Count)
            {
                throw new ArgumentException(
                    $"The inventory holds material ID {entry.MaterialId}, which the world's table "
                    + $"of {header.MaterialIdTable.Count} does not have.",
                    nameof(header));
            }

            if (entry.Voxels <= 0)
            {
                throw new ArgumentException(
                    $"Material ID {entry.MaterialId} holds {entry.Voxels} voxels. A material at "
                    + "zero is not held, and a negative holding is a bug.",
                    nameof(header));
            }

            if (i > 0 && sorted[i - 1].MaterialId == entry.MaterialId)
            {
                // Two rows for one material would be summed by one reader and overwritten by
                // another, and both are silent. Refuse instead of picking.
                throw new ArgumentException(
                    $"The inventory holds material ID {entry.MaterialId} twice.",
                    nameof(header));
            }
        }

        return sorted;
    }

    /// <summary>The first identifier that appears twice in <paramref name="table"/>, with both slots.</summary>
    /// <remarks>Holes are not duplicates: <c>default</c> marks a removed mod, and several may be removed.</remarks>
    private static (int First, int Second, Identifier Identifier)? FindDuplicate(IReadOnlyList<Identifier> table)
    {
        var seen = new Dictionary<Identifier, int>();

        for (int i = 0; i < table.Count; i++)
        {
            Identifier id = table[i];
            if (!id.IsValid) continue;

            if (seen.TryGetValue(id, out int first))
            {
                return (first, i, id);
            }

            seen[id] = i;
        }

        return null;
    }

    /// <summary>Rebuilds a world header, refusing anything it cannot interpret exactly.</summary>
    public static WorldHeader Read(ReadOnlySpan<byte> blob)
    {
        const string What = "World header";

        ReadOnlySpan<byte> payload = BlobFormat.ReadPayload(
            blob, OldestVersion, Version, BlobFormat.FrameBytes, What, out _, out byte version);

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

        if (FindDuplicate(table) is var (firstId, secondId, twice))
        {
            // Two entries meaning one material is what a build that predates a namespace alias
            // leaves behind when it opens a newer world: it does not recognise the current
            // spelling, registers its own under fresh numbers, and writes both on quit. The
            // 2026-09-03 rename produced exactly this on its first run against a stale binary.
            // Refused rather than repaired, because nothing here can know which number the
            // chunks saved in that session used.
            throw new BlobFormatException(
                $"{What}: material IDs {firstId} and {secondId} are both '{twice}' once retired "
                + "namespaces are applied. A world's table names each material once; this one was "
                + "last opened by a build older than the alias it needs. Restore the header from "
                + "before that session.");
        }

        // Version 1 stopped here, so a world written before M6c-2 carries no inventory and no
        // saved player — which is a true statement about it rather than a defaulted field. See
        // OldestVersion for why this migration is allowed where a defaulted setting is not.
        InventoryEntry[] inventory = [];
        PlayerSave? player = null;

        if (version >= 2)
        {
            inventory = ReadInventory(payload, count, ref offset);
            player = ReadPlayer(payload, ref offset);
        }

        // Version 2 stopped here, so a world written before M7b-2 has never had a clock and never
        // had a watershed. Zero elapsed is exactly true of it; the lattice default is the one this
        // build uses, which is right because such a world has no stored basins to be keyed on
        // anything else.
        long elapsedMillis = 0;
        int watershedCellMetres = HeightField.DefaultCellVoxels / VoxelConstants.VoxelsPerMetre;

        if (version >= 3)
        {
            if (payload.Length - offset < sizeof(long) + sizeof(int))
            {
                throw new BlobFormatException(
                    $"{What}: ran out of bytes reading the world clock and watershed lattice.");
            }

            elapsedMillis = BinaryPrimitives.ReadInt64LittleEndian(payload[offset..]);
            offset += sizeof(long);
            watershedCellMetres = BinaryPrimitives.ReadInt32LittleEndian(payload[offset..]);
            offset += sizeof(int);

            if (elapsedMillis < 0)
            {
                throw new BlobFormatException(
                    $"{What}: the world clock reads {elapsedMillis} ms, which is not a length of time.");
            }

            if (watershedCellMetres < 1)
            {
                throw new BlobFormatException(
                    $"{What}: a watershed cell of {watershedCellMetres} m cannot hold a lattice.");
            }
        }

        if (offset != payload.Length)
        {
            throw new BlobFormatException(
                $"{What}: {payload.Length - offset} bytes left over after the world header.");
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

        return new WorldHeader
        {
            Settings = settings,
            MaterialIdTable = table,
            Inventory = inventory,
            Player = player,
            ElapsedMillis = elapsedMillis,
            WatershedCellMetres = watershedCellMetres,
        };
    }

    /// <summary>Reads the inventory, checked against the table that gives its keys meaning.</summary>
    private static InventoryEntry[] ReadInventory(
        ReadOnlySpan<byte> payload, int materialCount, ref int offset)
    {
        const string What = "World header";

        if (payload.Length - offset < sizeof(int))
        {
            throw new BlobFormatException($"{What}: ran out of bytes reading the inventory length.");
        }

        int count = BinaryPrimitives.ReadInt32LittleEndian(payload[offset..]);
        offset += sizeof(int);

        if (count < 0 || count >= materialCount)
        {
            // At most one row per material, and air can never be one of them — so a count at or
            // past the table size is corrupt before a single entry is read.
            throw new BlobFormatException(
                $"{What}: inventory declares {count} entries against a table of {materialCount}.");
        }

        if (payload.Length - offset < count * InventoryEntryBytes)
        {
            throw new BlobFormatException(
                $"{What}: inventory declares {count} entries but only {payload.Length - offset} "
                + "bytes remain.");
        }

        var entries = new InventoryEntry[count];
        int previous = -1;

        for (int i = 0; i < count; i++)
        {
            int materialId = BinaryPrimitives.ReadInt32LittleEndian(payload[offset..]);
            long voxels = BinaryPrimitives.ReadInt64LittleEndian(payload[(offset + sizeof(int))..]);
            offset += InventoryEntryBytes;

            if (materialId == MaterialIds.AirNumericId)
            {
                throw new BlobFormatException(
                    $"{What}: the inventory holds air, which nothing can mine.");
            }

            if ((uint)materialId >= (uint)materialCount)
            {
                throw new BlobFormatException(
                    $"{What}: the inventory holds material ID {materialId}, past the "
                    + $"{materialCount} the table has.");
            }

            if (voxels <= 0)
            {
                throw new BlobFormatException(
                    $"{What}: material ID {materialId} holds {voxels} voxels.");
            }

            // Strictly increasing, which is both the canonical order the writer produces and the
            // check that no material appears twice — a duplicate would be summed by one reader
            // and overwritten by another, and both silently.
            if (materialId <= previous)
            {
                throw new BlobFormatException(
                    $"{What}: inventory entries are out of order or duplicated at ID {materialId}.");
            }

            previous = materialId;
            entries[i] = new InventoryEntry(materialId, voxels);
        }

        return entries;
    }

    /// <summary>Reads the saved player, if the world has one.</summary>
    private static PlayerSave? ReadPlayer(ReadOnlySpan<byte> payload, ref int offset)
    {
        const string What = "World header";

        if (payload.Length - offset < 1)
        {
            throw new BlobFormatException($"{What}: ran out of bytes reading the player flag.");
        }

        byte present = payload[offset++];
        if (present > 1)
        {
            throw new BlobFormatException(
                $"{What}: the player-present flag is {present}, which is neither 0 nor 1.");
        }

        if (present == 0) return null;

        if (payload.Length - offset < PlayerBytes)
        {
            throw new BlobFormatException($"{What}: ran out of bytes reading the player.");
        }

        var feet = new Vector3(
            BinaryPrimitives.ReadSingleLittleEndian(payload[offset..]),
            BinaryPrimitives.ReadSingleLittleEndian(payload[(offset + 4)..]),
            BinaryPrimitives.ReadSingleLittleEndian(payload[(offset + 8)..]));

        byte flying = payload[offset + 12];
        offset += PlayerBytes;

        if (flying > 1)
        {
            throw new BlobFormatException(
                $"{What}: the player's flying flag is {flying}, which is neither 0 nor 1.");
        }

        if (!float.IsFinite(feet.X) || !float.IsFinite(feet.Y) || !float.IsFinite(feet.Z))
        {
            // A non-finite spawn is unrecoverable in a way an absent one is not: the collider
            // refuses a non-finite displacement by name, so the player would be stuck at the first
            // step with nothing to say why.
            throw new BlobFormatException($"{What}: the player's saved position is {feet}.");
        }

        return new PlayerSave(feet, flying != 0);
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
