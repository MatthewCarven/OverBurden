using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using Overburden.Core.Content;
using Overburden.Core.Hydrology;
using Overburden.Core.Persistence;
using Overburden.Core.Voxels;
using Overburden.Core.Worldgen;

namespace Overburden.Core.Tests;

/// <summary>
/// The world header is where non-negotiable 2 stops being a claim.
/// </summary>
/// <remarks>
/// <see cref="Registry{T}.ExportIdTable"/> and <see cref="Registry{T}.SeedIdTable"/> were built at
/// M1, documented as "the ID table to write into the world file", and have had no caller for five
/// milestones. These are the tests that make the round trip real rather than promised.
/// </remarks>
public class WorldHeaderTests
{
    private static readonly Identifier Air = Identifier.Parse("overburden:air");
    private static readonly Identifier Stone = Identifier.Parse("overburden:stone");
    private static readonly Identifier Copper = Identifier.Parse("deepdig:copper_ore");

    /// <summary>Every field distinct and none at its default, so a forgotten one shows up.</summary>
    private static WorldGenSettings UnusualSettings() => new()
    {
        Seed = 0xFEEDFACECAFEBEEDUL,
        SeaLevelVoxels = -17,
        SurfaceAmplitudeVoxels = 33.5f,
        ReliefVariation = 0.45f,
        SurfaceFrequency = 0.00725f,
        SurfaceOctaves = 6,
        Lacunarity = 2.75f,
        Gain = 0.375f,
        CavesEnabled = false,
        CaveFrequency = 0.031f,
        CaveOctaves = 5,
        CaveThreshold = 0.61f,
        CaveSurfaceMarginVoxels = 13,
        CaveLatticeStep = 8,
    };

    private static WorldHeader Header(params Identifier[] table) => new()
    {
        Settings = UnusualSettings(),
        MaterialIdTable = table.Length == 0 ? [Air] : table,
    };

    // -------------------------------------------------------------------------------------
    // The retired namespace. The base mod was minercraft: until 2026-09-03 and every world saved
    // before then carries that spelling in its ID table. Non-negotiable 2 says the table is never
    // edited, so the rename is an alias at read: minercraft:stone *is* overburden:stone, forever.
    // -------------------------------------------------------------------------------------

    [Fact]
    public void APlayedWorldSavedUnderTheOldNamespaceReadsUnderTheNewOne()
    {
        // The fixture is the real header of the world Matthew played on 2026-09-03, captured from
        // its SQLite file before the rename - not a blob the current writer produced, because the
        // current writer cannot spell the old name and a round trip would test nothing.
        WorldHeader world = WorldHeaderBlob.Read(PlayedWorldV3Header());

        Assert.Equal(MaterialIds.Air, world.MaterialIdTable[0]);
        Assert.All(world.MaterialIdTable.Where(id => id.IsValid),
            id => Assert.Equal(Identifier.BaseNamespace, id.Namespace));
        Assert.Contains(new Identifier(Identifier.BaseNamespace, "stone"), world.MaterialIdTable);
        Assert.True(world.MaterialIdTable.Count >= 4, "the played world carried more than air");
    }

    [Fact]
    public void ReSavingAnOldWorldWritesTheNewNamesAndKeepsEveryNumber()
    {
        WorldHeader world = WorldHeaderBlob.Read(PlayedWorldV3Header());

        byte[] rewritten = WorldHeaderBlob.Write(world);
        WorldHeader again = WorldHeaderBlob.Read(rewritten);

        // Same table, entry for entry: an alias renames and never renumbers, or every stored
        // chunk's palette would point at the wrong material.
        Assert.Equal(world.MaterialIdTable, again.MaterialIdTable);
        Assert.Equal(world.Inventory, again.Inventory);
        string text = Encoding.UTF8.GetString(Inflate(rewritten));
        Assert.DoesNotContain("minercraft", text);
        Assert.Contains("overburden:air", text);
    }

    /// <summary>The header's payload with the frame stripped and the zlib undone, as text.</summary>
    private static byte[] Inflate(byte[] blob) =>
        BlobFormat.ReadPayload(blob, WorldHeaderBlob.OldestVersion, WorldHeaderBlob.Version,
            BlobFormat.FrameBytes, "test", out _, out _).ToArray();

    [Fact]
    public void AWorldLastOpenedByABuildOlderThanTheAliasIsRefusedByName()
    {
        // The real artefact of the rename's first run: a stale binary that still knew the base
        // game as minercraft: opened the copied world, loaded mods/overburden/ as a stranger, gave
        // its five materials fresh numbers 5-9, and wrote all ten on quit. Once the alias applies
        // the table says overburden:air twice, and nothing can know which number that session's
        // chunks used - so it is refused, and the refusal names the two slots and the cause.
        var error = Assert.Throws<BlobFormatException>(
            () => WorldHeaderBlob.Read(Fixture("downgraded-world-v3-header.bin")));

        Assert.Contains("0 and 5", error.Message);
        Assert.Contains("overburden:air", error.Message);
        Assert.Contains("older than the alias", error.Message);
    }

    [Fact]
    public void ATableNamingOneMaterialTwiceIsRefusedAtWrite()
    {
        // The two spellings are one value, so this table has stone at 1 and at 2.
        var header = Header(Air, Stone, Identifier.Parse("minercraft:stone"));

        var error = Assert.Throws<ArgumentException>(() => WorldHeaderBlob.Write(header));

        Assert.Contains("1 and 2", error.Message);
    }

    [Fact]
    public void HolesAreNotDuplicates()
    {
        // Several removed mods leave several default entries, and that is a valid table.
        WorldHeader restored = WorldHeaderBlob.Read(WorldHeaderBlob.Write(Header(Air, default, default, Stone)));

        Assert.Equal(4, restored.MaterialIdTable.Count);
        Assert.False(restored.MaterialIdTable[1].IsValid);
        Assert.False(restored.MaterialIdTable[2].IsValid);
    }

    private static byte[] PlayedWorldV3Header() => Fixture("played-world-v3-header.bin");

    private static byte[] Fixture(string name)
    {
        using Stream stream = typeof(WorldHeaderTests).Assembly.GetManifestResourceStream(
            "Overburden.Core.Tests.Fixtures." + name)
            ?? throw new InvalidOperationException("fixture missing: " + name);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    // -------------------------------------------------------------------------------------
    // Worldgen settings: all thirteen fields, because an unstored chunk regenerates from all
    // thirteen and not only from the seed.
    // -------------------------------------------------------------------------------------

    [Fact]
    public void EveryWorldgenSettingSurvivesTheRoundTrip()
    {
        WorldHeader restored = WorldHeaderBlob.Read(WorldHeaderBlob.Write(Header()));

        // Record equality compares every property, so this is the whole check rather than a
        // field-by-field list that could quietly omit the one that was forgotten.
        Assert.Equal(UnusualSettings(), restored.Settings);
    }

    [Fact]
    public void TheSettingsLayoutCoversEveryPropertyOnTheRecord()
    {
        // The guard that makes the test above stay honest. Adding a fourteenth field to
        // WorldGenSettings and forgetting to write it would leave the round trip green — record
        // equality would compare a default against a default — while every reload silently reset
        // it. Since CaveLatticeStep changes the shape of unstored terrain, that is a world that
        // comes back subtly different around a base that did not move.
        int properties = typeof(WorldGenSettings)
            .GetProperties()
            .Count(p => p.Name != "EqualityContract");

        Assert.Equal(WorldHeaderBlob.SettingsFieldCount, properties);
    }

    [Fact]
    public void ChangingOneSettingChangesTheBytes()
    {
        // A field written as a constant rather than from the record would pass the round trip if
        // the test value happened to match. This says each one actually reaches the payload.
        byte[] baseline = WorldHeaderBlob.Write(Header());

        WorldGenSettings tweaked = UnusualSettings() with { CaveLatticeStep = 4 };
        byte[] other = WorldHeaderBlob.Write(new WorldHeader
        {
            Settings = tweaked,
            MaterialIdTable = [Air],
        });

        Assert.NotEqual(baseline, other);
        Assert.Equal(4, WorldHeaderBlob.Read(other).Settings.CaveLatticeStep);
    }

    // -------------------------------------------------------------------------------------
    // The ID table.
    // -------------------------------------------------------------------------------------

    [Fact]
    public void TheMaterialTableRoundTrips()
    {
        WorldHeader restored = WorldHeaderBlob.Read(WorldHeaderBlob.Write(Header(Air, Stone, Copper)));
        Assert.Equal([Air, Stone, Copper], restored.MaterialIdTable);
    }

    [Fact]
    public void HolesStayHoles()
    {
        // A number belonging to a removed mod whose identifier is no longer known. Numbers are
        // never reused and the table is never compacted, so the hole has to survive the write —
        // collapsing it would renumber everything after it, which is the exact failure the table
        // exists to prevent.
        WorldHeader restored =
            WorldHeaderBlob.Read(WorldHeaderBlob.Write(Header(Air, default, Stone)));

        Assert.Equal(3, restored.MaterialIdTable.Count);
        Assert.False(restored.MaterialIdTable[1].IsValid);
        Assert.Equal(Stone, restored.MaterialIdTable[2]);
    }

    [Fact]
    public void ATableWhoseFirstEntryIsNotAirIsRefusedOnRead()
    {
        // Non-negotiable 3, checked rather than assumed. Two things depend on air being 0: a
        // zero-initialised array is a valid empty chunk, and the uniform fast path tests emptiness
        // against a constant. A world file where entry 0 is stone does not make the world wrong —
        // it makes a constant the whole engine assumes into a lie.
        // Write refuses this table, so the only way to reach Read's check is to corrupt a valid
        // blob the way a hand-edited or damaged world file would be corrupt.
        byte[] payload = PayloadOf(WorldHeaderBlob.Write(Header(Air, Stone)));

        byte[] needle = System.Text.Encoding.UTF8.GetBytes("overburden:air");
        int at = IndexOf(payload, needle);
        Assert.True(at >= 0, "the identifier should appear verbatim in the uncompressed payload");

        // Same length, so nothing else in the layout shifts.
        System.Text.Encoding.UTF8.GetBytes("overburden:foo").CopyTo(payload.AsSpan(at));

        var error = Assert.Throws<BlobFormatException>(() => WorldHeaderBlob.Read(Reframe(payload)));
        Assert.Contains("overburden:air", error.Message);
    }

    [Fact]
    public void ATableWhoseFirstEntryIsNotAirIsRefusedOnWrite()
    {
        // Refused at write as well, because a table this wrong reaching disk is a bug upstream and
        // finding it here names the session that produced it rather than the one that later
        // failed to open the world.
        var error = Assert.Throws<ArgumentException>(() => WorldHeaderBlob.Write(Header(Stone, Air)));
        Assert.Contains("overburden:air", error.Message);
    }

    [Fact]
    public void AnEmptyTableIsRefused() =>
        Assert.Throws<ArgumentException>(() => WorldHeaderBlob.Write(new WorldHeader
        {
            Settings = UnusualSettings(),
            MaterialIdTable = [],
        }));

    [Fact]
    public void AnUnknownVersionIsRefusedRatherThanRead()
    {
        byte[] blob = WorldHeaderBlob.Write(Header(Air, Stone));
        blob[0] = WorldHeaderBlob.Version + 1;

        Assert.Throws<BlobFormatException>(() => WorldHeaderBlob.Read(blob));
    }

    [Fact]
    public void ATruncatedHeaderIsRefused()
    {
        byte[] blob = WorldHeaderBlob.Write(Header(Air, Stone, Copper));

        for (int length = 0; length < blob.Length; length++)
        {
            byte[] cut = blob[..length];

            try
            {
                WorldHeaderBlob.Read(cut);
                Assert.Fail($"{length} of {blob.Length} bytes was accepted.");
            }
            catch (BlobFormatException)
            {
            }
        }
    }

    [Fact]
    public void TrailingBytesAfterTheTableAreRefused()
    {
        // A table length that under-counts leaves bytes nobody claimed. Accepting them would mean
        // a header could hold entries the reader silently ignored — and an ignored material is a
        // renumbering waiting to happen.
        // Built uncompressed and by hand, so that the four extra bytes reach the table reader
        // rather than being stopped by the frame's own length check — which is a different
        // refusal, already covered by ATruncatedHeaderIsRefused.
        byte[] longer = Reframe([.. PayloadOf(WorldHeaderBlob.Write(Header(Air, Stone))), 0, 0, 0, 0]);

        Assert.Throws<BlobFormatException>(() => WorldHeaderBlob.Read(longer));
    }

    // -------------------------------------------------------------------------------------
    // The whole point, end to end through the real registry.
    // -------------------------------------------------------------------------------------

    [Fact]
    public void AWorldSurvivesAModBeingRemovedAndReinstalled()
    {
        // IdStabilityTests already proves Registry can do this in memory. What this adds is the
        // trip through disk bytes — the table is exported, serialised, read back and seeded, and
        // the numeric IDs baked into every stored voxel still mean what they meant.
        var session1 = new GameContent();
        session1.Load(TestContent.BaseGame().AddDeepDig());

        int copperId = session1.Materials.GetNumericId(Copper);
        int stoneId = session1.Materials.GetNumericId(Stone);

        byte[] world = WorldHeaderBlob.Write(new WorldHeader
        {
            Settings = UnusualSettings(),
            MaterialIdTable = session1.Materials.ExportIdTable(),
        });

        // Session 2: the mod is gone, and the world is opened from those bytes alone.
        WorldHeader restored = WorldHeaderBlob.Read(world);
        var session2 = new GameContent();
        session2.Load(TestContent.BaseGame(), restored.MaterialIdTable);

        Assert.Equal(copperId, session2.Materials.GetNumericId(Copper));
        Assert.Equal(stoneId, session2.Materials.GetNumericId(Stone));
        Assert.True(session2.Materials.IsPlaceholder(copperId));
        Assert.Equal(Copper, session2.Materials.Get(copperId).Id);

        // Session 3: reinstalled, again through the bytes session 2 would have written.
        byte[] world2 = WorldHeaderBlob.Write(new WorldHeader
        {
            Settings = restored.Settings,
            MaterialIdTable = session2.Materials.ExportIdTable(),
        });

        var session3 = new GameContent();
        session3.Load(TestContent.BaseGame().AddDeepDig(), WorldHeaderBlob.Read(world2).MaterialIdTable);

        Assert.Equal(copperId, session3.Materials.GetNumericId(Copper));
        Assert.False(session3.Materials.IsPlaceholder(copperId));
    }

    [Fact]
    public void AirKeepsNumericIdZeroThroughTheRoundTrip()
    {
        var content = new GameContent();
        content.Load(TestContent.BaseGame().AddDeepDig());

        WorldHeader restored = WorldHeaderBlob.Read(WorldHeaderBlob.Write(new WorldHeader
        {
            Settings = UnusualSettings(),
            MaterialIdTable = content.Materials.ExportIdTable(),
        }));

        var reopened = new GameContent();
        reopened.Load(TestContent.BaseGame(), restored.MaterialIdTable);

        Assert.Equal(MaterialIds.AirNumericId, reopened.Materials.GetNumericId(MaterialIds.Air));
    }

    // -------------------------------------------------------------------------------------
    // M6c-2: the inventory and the player, and the version bump that carries them.
    // -------------------------------------------------------------------------------------

    /// <summary>Bytes one inventory row occupies in the payload: an int32 ID and an int64 count.</summary>
    /// <summary>
    /// The clock and the watershed lattice M7b-2 appended, which every offset counted back from
    /// the end of the payload has to step over.
    /// </summary>
    private const int ClockAndLatticeBytes = sizeof(long) + sizeof(int);

    /// <summary>The pack section M10c-3 appended: one presence byte with no pack equipped.</summary>
    private const int PackAbsentBytes = 1;

    /// <summary>Bytes a present pack occupies after its flag: an int32 ID, an int64 charge, an int32 channel — and one carried pack's row.</summary>
    private const int PackPayloadBytes = 16;

    /// <summary>The carried packs' section v5 appended (2026-09-18): an int32 count, zero with none charged.</summary>
    private const int CarriedAbsentBytes = 4;

    /// <summary>
    /// Everything after the inventory rows in a header with no player, no pack and no carried
    /// charge: the player flag, the clock and lattice, the pack flag, the carried count. <b>Every
    /// offset counted back from the end goes through this</b>, because the v4 bump found two
    /// tests that had been indexing into the clock since v3 and passing on the wrong refusal (the
    /// player flag read 9, or 1 with no player behind it) — <c>ClockAndLatticeBytes</c> happens
    /// to equal <c>EntryBytes</c>.
    /// </summary>
    private const int TrailerBytes = 1 + ClockAndLatticeBytes + PackAbsentBytes + CarriedAbsentBytes;

    private const int EntryBytes = 12;

    /// <summary>The payload offset of inventory row <paramref name="row"/> of <paramref name="count"/>, in a header with no player and no pack.</summary>
    private static int RowOffset(byte[] payload, int row, int count) =>
        payload.Length - TrailerBytes - (EntryBytes * (count - row));

    private static WorldHeader WithInventory(params (int MaterialId, long Voxels)[] entries) =>
        Header(Air, Stone, Copper) with
        {
            Inventory = [.. entries.Select(e => new InventoryEntry(e.MaterialId, e.Voxels))],
        };

    [Fact]
    public void TheInventoryAndThePlayerSurviveTheRoundTrip()
    {
        WorldHeader header = WithInventory((1, 64), (2, 1_500_000)) with
        {
            Player = new PlayerSave(new Vector3(12.5f, -3.25f, 900.75f), Flying: true),
        };

        WorldHeader read = WorldHeaderBlob.Read(WorldHeaderBlob.Write(header));

        Assert.Equal(header.Inventory, read.Inventory);
        Assert.Equal(header.Player, read.Player);
    }

    [Fact]
    public void AWorldNobodyHasEnteredHasNoPlayerRatherThanOneAtTheOrigin()
    {
        // Two different facts, and only the first should spawn a player at the surface.
        WorldHeader read = WorldHeaderBlob.Read(WorldHeaderBlob.Write(Header(Air, Stone)));

        Assert.Null(read.Player);
        Assert.Empty(read.Inventory);
    }

    [Fact]
    public void TheInventoryIsWrittenInMaterialOrderWhateverOrderItArrivedIn()
    {
        // So the same holding always writes the same bytes. Otherwise "did this change?" is
        // unanswerable in a test, in a diff and in a bug report alike.
        byte[] ascending = WorldHeaderBlob.Write(WithInventory((1, 5), (2, 7)));
        byte[] descending = WorldHeaderBlob.Write(WithInventory((2, 7), (1, 5)));

        Assert.Equal(ascending, descending);
    }

    [Fact]
    public void AWorldWrittenBeforeTheInventoryExistedStillOpens()
    {
        // The migration, and the reason it is allowed to be one: version 1 is version 2 without
        // its two trailing sections, so reading one means supplying two defaults that are true
        // statements about it — no inventory, no saved player. A version that *changed* what a
        // field means would be refused instead; see WorldHeaderBlob.OldestVersion.
        byte[] payload = PayloadOf(WorldHeaderBlob.Write(Header(Air, Stone, Copper)));

        // An empty inventory is a four-byte zero count and no player is a single zero byte; the
        // clock and the watershed lattice that M7b-2 appended are eight more and four more, and
        // the pack flag M10c-3 appended one more. Dropping exactly those eighteen bytes is what
        // a version 1 payload was.
        byte[] older = payload[..^(sizeof(int) + TrailerBytes)];

        WorldHeader read = WorldHeaderBlob.Read(Reframe(WithoutReliefVariation(older), version: 1));

        Assert.Equal([Air, Stone, Copper], read.MaterialIdTable);
        Assert.Empty(read.Inventory);
        Assert.Null(read.Player);
    }

    [Fact]
    public void AWorldWrittenBeforeTheClockExistedStillOpens()
    {
        // The second migration, under the same policy: version 2 is version 3 without its clock
        // and its watershed lattice, and both defaults are true statements about such a world —
        // it has never run for any length of time and has no stored basins to be keyed on any
        // lattice at all.
        var inventory = new[] { new InventoryEntry(1, 64) };
        var player = new PlayerSave(new Vector3(1.5f, 70.25f, -3.5f), Flying: true);
        byte[] payload = PayloadOf(WorldHeaderBlob.Write(
            Header(Air, Stone, Copper) with { Inventory = inventory, Player = player }));

        byte[] older = payload[..^(ClockAndLatticeBytes + PackAbsentBytes + CarriedAbsentBytes)];
        WorldHeader read = WorldHeaderBlob.Read(Reframe(WithoutReliefVariation(older), version: 2));

        Assert.Equal(0, read.ElapsedMillis);
        // A v2 world has no stored basins to be keyed on any lattice, so it takes this build's —
        // which is what the reader says, and what this asserts rather than a literal 4.
        Assert.Equal(
            HeightField.DefaultCellVoxels / VoxelConstants.VoxelsPerMetre, read.WatershedCellMetres);
        Assert.Equal(inventory, read.Inventory);
        Assert.Equal(player, read.Player);
    }

    [Fact]
    public void TheClockAndTheWatershedLatticeSurviveTheRoundTrip()
    {
        WorldHeader written = Header(Air, Stone) with
        {
            ElapsedMillis = 1_234_567_890,
            WatershedCellMetres = 8,
        };

        WorldHeader read = WorldHeaderBlob.Read(WorldHeaderBlob.Write(written));

        Assert.Equal(1_234_567_890, read.ElapsedMillis);
        Assert.Equal(8, read.WatershedCellMetres);
    }

    [Fact]
    public void AWorldClockThatRanBackwardsIsRefused()
    {
        Assert.Throws<ArgumentException>(
            () => WorldHeaderBlob.Write(Header(Air, Stone) with { ElapsedMillis = -1 }));

        // And on read, because a header can arrive from a file rather than from this process.
        byte[] blob = WorldHeaderBlob.Write(Header(Air, Stone));
        byte[] payload = PayloadOf(blob);
        BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(payload.Length - ClockAndLatticeBytes - PackAbsentBytes - CarriedAbsentBytes), -1);

        Assert.Throws<BlobFormatException>(
            () => WorldHeaderBlob.Read(Reframe(payload, WorldHeaderBlob.Version)));
    }

    [Fact]
    public void AWatershedLatticeThatCannotHoldASampleIsRefused()
    {
        Assert.Throws<ArgumentException>(
            () => WorldHeaderBlob.Write(Header(Air, Stone) with { WatershedCellMetres = 0 }));

        byte[] payload = PayloadOf(WorldHeaderBlob.Write(Header(Air, Stone)));
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(payload.Length - sizeof(int) - PackAbsentBytes - CarriedAbsentBytes), 0);

        Assert.Throws<BlobFormatException>(
            () => WorldHeaderBlob.Read(Reframe(payload, WorldHeaderBlob.Version)));
    }

    [Fact]
    public void AVersionThisBuildDoesNotWriteYetIsStillRefused()
    {
        // Reading version 1 is a migration, not leniency: anything past what this build writes is
        // a layout it has never seen, and guessing at it is the failure the version byte exists
        // to prevent.
        byte[] blob = WorldHeaderBlob.Write(Header(Air, Stone));
        blob[0] = WorldHeaderBlob.Version + 1;

        Assert.Throws<BlobFormatException>(() => WorldHeaderBlob.Read(blob));
    }

    [Fact]
    public void AnInventoryHoldingAirIsRefusedOnWrite() =>
        Assert.Throws<ArgumentException>(() => WorldHeaderBlob.Write(WithInventory((0, 5))));

    [Fact]
    public void AnInventoryHoldingAMaterialTheTableLacksIsRefusedOnWrite() =>
        Assert.Throws<ArgumentException>(() => WorldHeaderBlob.Write(WithInventory((9, 5))));

    [Fact]
    public void AnInventoryRowAtZeroIsRefusedOnWrite() =>
        // A material at zero is not held, so a row for it is a bookkeeping bug rather than a
        // quantity — and a reader that accepted it would carry the row forever.
        Assert.Throws<ArgumentException>(() => WorldHeaderBlob.Write(WithInventory((1, 0))));

    [Fact]
    public void TheSameInventoryMaterialTwiceIsRefusedOnWrite() =>
        Assert.Throws<ArgumentException>(() => WorldHeaderBlob.Write(WithInventory((1, 5), (1, 7))));

    [Fact]
    public void AnInventoryHoldingAirIsRefusedOnRead()
    {
        // The reader checks what the writer checks, because a file can be edited by hand or
        // damaged. **Row 0 rather than row 1**, so that the entries stay in ascending order and
        // the ordering check cannot be what fires: the battery found this test passing for the
        // wrong reason when it edited whichever row was convenient.
        byte[] payload = PayloadOf(WorldHeaderBlob.Write(WithInventory((1, 5), (2, 7))));

        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
            payload.AsSpan(RowOffset(payload, 0, 2)), 0);

        var error = Assert.Throws<BlobFormatException>(() => WorldHeaderBlob.Read(Reframe(payload)));
        Assert.Contains("air", error.Message);
    }

    [Fact]
    public void AnInventoryHoldingAMaterialTheTableLacksIsRefusedOnRead()
    {
        // **Row 1 rather than row 0**, for the mirror of the reason above: raising the last ID
        // keeps the rows ascending, so the range check is the only one that can fire.
        byte[] payload = PayloadOf(WorldHeaderBlob.Write(WithInventory((1, 5), (2, 7))));

        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
            payload.AsSpan(RowOffset(payload, 1, 2)), 9);

        // The refusal it names, not the first one that happens to fire: until M10c-3 this offset
        // was the player flag and the test passed on "the player-present flag is 9".
        var error = Assert.Throws<BlobFormatException>(() => WorldHeaderBlob.Read(Reframe(payload)));
        Assert.Contains("material ID 9", error.Message);
    }

    [Fact]
    public void DuplicateInventoryRowsAreRefusedOnRead()
    {
        // One reader would sum them and another would let the last win, and both silently. The
        // strictly-increasing order the writer produces is what makes this checkable at all.
        byte[] payload = PayloadOf(WorldHeaderBlob.Write(WithInventory((1, 5), (2, 7))));

        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(
            payload.AsSpan(RowOffset(payload, 1, 2)), 1);

        // As above: this offset was the player flag until M10c-3, and a 1 there read a player out
        // of the clock's bytes and ran out — a refusal, and the wrong one.
        var error = Assert.Throws<BlobFormatException>(() => WorldHeaderBlob.Read(Reframe(payload)));
        Assert.Contains("out of order or duplicated", error.Message);
    }

    [Fact]
    public void APlayerPresentFlagThatIsNeitherZeroNorOneIsRefused()
    {
        // The same shape as CavesEnabled's check. A byte holding 2 means the writer and the
        // reader disagree about the layout, which is the one thing this format refuses to guess at.
        //
        // **Written with a player present**, so there really are thirteen bytes behind the flag.
        // Against a header with no player, removing the check leaves "ran out of bytes reading the
        // player" to throw instead — which the battery caught: the test passed while covering
        // nothing.
        WorldHeader header = Header(Air, Stone) with
        {
            Player = new PlayerSave(new Vector3(1.0f, 2.0f, 3.0f), Flying: false),
        };

        byte[] payload = PayloadOf(WorldHeaderBlob.Write(header));
        payload[^(PlayerPayloadBytes + 1 + ClockAndLatticeBytes + PackAbsentBytes + CarriedAbsentBytes)] = 2;

        Assert.Throws<BlobFormatException>(() => WorldHeaderBlob.Read(Reframe(payload)));
    }

    /// <summary>Bytes a present player occupies after its flag: three floats and a bool.</summary>
    private const int PlayerPayloadBytes = 13;

    [Fact]
    public void ANonFinitePlayerPositionIsRefused()
    {
        // Unrecoverable in a way an absent position is not: VoxelCollider refuses a non-finite
        // displacement by name, so the player would be stuck on the first step with nothing on
        // screen to say why.
        WorldHeader header = Header(Air, Stone) with
        {
            Player = new PlayerSave(new Vector3(1.0f, float.NaN, 1.0f), Flying: false),
        };

        Assert.Throws<BlobFormatException>(() => WorldHeaderBlob.Read(WorldHeaderBlob.Write(header)));
    }

    [Fact]
    public void AnInventoryCountWithNoRowsBehindItIsRefused()
    {
        // Caught by arithmetic before any row is read, which is the same argument the frame's
        // stored length makes: a length that disagrees with the bytes is a truncation, and
        // reading rows out of whatever follows would invent a holding.
        //
        // **The count's other arm — "at or past the table's size" — is belt and braces and has no
        // test, deliberately.** It is unreachable in isolation: at most one row per material and
        // never air, so any count large enough to trip it forces a payload whose rows are either
        // duplicated, out of order or out of range, and one of those checks fires first. Removing
        // it fails nothing, which is M4a's zeroable-contact-epsilon category rather than a hole.
        // It is kept because it fails earlier and more cheaply than running out of bytes does.
        byte[] payload = PayloadOf(WorldHeaderBlob.Write(WithInventory((1, 5))));

        BinaryPrimitives.WriteInt32LittleEndian(
            payload.AsSpan(RowOffset(payload, 0, 1) - sizeof(int)),
            2);

        Assert.Throws<BlobFormatException>(() => WorldHeaderBlob.Read(Reframe(payload)));
    }

    // -------------------------------------------------------------------------------------
    // Version 4: the equipped pack (M10c-3). The player's shape — a presence byte and a fixed
    // payload after the lattice — and the inventory's checks against the table.
    // -------------------------------------------------------------------------------------

    [Fact]
    public void TheEquippedPackSurvivesTheRoundTrip()
    {
        var pack = new PackSave(MaterialId: 2, ChargeMillijoules: 2_100_000_000, Channel: 7);
        WorldHeader written = Header(Air, Stone, Copper) with { Pack = pack };

        WorldHeader read = WorldHeaderBlob.Read(WorldHeaderBlob.Write(written));

        Assert.Equal(pack, read.Pack);
    }

    [Fact]
    public void AWorldWithNoPackEquippedReadsNoneRatherThanAnEmptyOne()
    {
        WorldHeader read = WorldHeaderBlob.Read(WorldHeaderBlob.Write(Header(Air, Stone)));

        Assert.Null(read.Pack);
    }

    [Fact]
    public void AWorldWrittenBeforeThePackExistedOpensWithNoneEquipped()
    {
        // The third migration, under the same policy: version 3 is version 4 without its pack
        // section, and "no pack equipped" is exactly true of a world nothing could equip one in.
        var inventory = new[] { new InventoryEntry(1, 64) };
        var player = new PlayerSave(new Vector3(1.5f, 70.25f, -3.5f), Flying: false);
        byte[] payload = PayloadOf(WorldHeaderBlob.Write(
            Header(Air, Stone, Copper) with { Inventory = inventory, Player = player, ElapsedMillis = 42 }));

        byte[] older = payload[..^(PackAbsentBytes + CarriedAbsentBytes)];
        WorldHeader read = WorldHeaderBlob.Read(Reframe(WithoutReliefVariation(older), version: 3));

        Assert.Null(read.Pack);
        Assert.Equal(42, read.ElapsedMillis);
        Assert.Equal(inventory, read.Inventory);
        Assert.Equal(player, read.Player);
    }

    [Fact]
    public void ThePlayedWorldsRealV3HeaderStillOpensWithNoPack()
    {
        // The real fixture again, now as the v3 → v4 case: the bytes on disk are what a build
        // before M10c-3 wrote, and they read with no pack and everything else intact.
        WorldHeader read = WorldHeaderBlob.Read(PlayedWorldV3Header());

        Assert.Null(read.Pack);
        Assert.True(read.MaterialIdTable.Count > 1);
    }

    [Fact]
    public void APackNamingAMaterialTheTableLacksIsRefusedOnWriteAndOnReadNamingIt()
    {
        var error = Assert.Throws<ArgumentException>(
            () => WorldHeaderBlob.Write(Header(Air, Stone) with { Pack = new PackSave(9, 1, 0) }));
        Assert.Contains("material ID 9", error.Message);

        // And on read: a valid pack rewritten to an ID past the table. The material sits four
        // bytes after the pack flag, sixteen before the carried count that ends the payload.
        byte[] payload = PayloadOf(WorldHeaderBlob.Write(Header(Air, Stone) with { Pack = new PackSave(1, 1, 0) }));
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(payload.Length - PackPayloadBytes - CarriedAbsentBytes), 9);

        var refused = Assert.Throws<BlobFormatException>(() => WorldHeaderBlob.Read(Reframe(payload)));
        Assert.Contains("material ID 9", refused.Message);
    }

    [Fact]
    public void APackThatIsAirIsRefused() =>
        Assert.Throws<ArgumentException>(
            () => WorldHeaderBlob.Write(Header(Air, Stone) with { Pack = new PackSave(0, 1, 0) }));

    [Fact]
    public void APackWithANegativeChargeIsRefusedOnRead()
    {
        // Write refuses it too; the read is the arm a damaged file reaches. The charge sits
        // twelve bytes before the carried count that ends the payload.
        byte[] payload = PayloadOf(WorldHeaderBlob.Write(Header(Air, Stone) with { Pack = new PackSave(1, 1, 0) }));
        BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(payload.Length - 12 - CarriedAbsentBytes), -1);

        Assert.Throws<BlobFormatException>(() => WorldHeaderBlob.Read(Reframe(payload)));
        Assert.Throws<ArgumentException>(
            () => WorldHeaderBlob.Write(Header(Air, Stone) with { Pack = new PackSave(1, -1, 0) }));
    }

    [Fact]
    public void APackOffTheChannelLadderIsRefused()
    {
        Assert.Throws<ArgumentException>(
            () => WorldHeaderBlob.Write(Header(Air, Stone) with { Pack = new PackSave(1, 1, 16) }));

        byte[] payload = PayloadOf(WorldHeaderBlob.Write(Header(Air, Stone) with { Pack = new PackSave(1, 1, 0) }));
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(payload.Length - 4 - CarriedAbsentBytes), 16);

        Assert.Throws<BlobFormatException>(() => WorldHeaderBlob.Read(Reframe(payload)));
    }

    [Fact]
    public void APackPresentFlagThatIsNeitherZeroNorOneIsRefused()
    {
        // Written with a pack present, for the reason the player's twin records: with none, the
        // check removed leaves "ran out of bytes reading the pack" to fire instead.
        byte[] payload = PayloadOf(WorldHeaderBlob.Write(Header(Air, Stone) with { Pack = new PackSave(1, 1, 0) }));
        payload[^(PackPayloadBytes + 1 + CarriedAbsentBytes)] = 2;

        Assert.Throws<BlobFormatException>(() => WorldHeaderBlob.Read(Reframe(payload)));
    }

    // -------------------------------------------------------------------------------------
    // Version 5: the carried packs' charges (2026-09-18, "keep its charge as an item"). A count
    // and a pack row per entry after the equipped pack, checked against the table and the
    // inventory's own counts — a charge for a pack the inventory does not hold is refused.
    // -------------------------------------------------------------------------------------

    [Fact]
    public void TheCarriedPacksChargesSurviveTheRoundTripInTheirOrder()
    {
        var carried = new[]
        {
            new PackSave(2, 2_650_600_000, 3),
            new PackSave(2, 1_000_000, 0),
            new PackSave(1, 5, 15),
        };
        WorldHeader written = WithInventory((1, 3), (2, 2)) with
        {
            Pack = new PackSave(2, 7, 0),
            CarriedPacks = carried,
        };

        WorldHeader read = WorldHeaderBlob.Read(WorldHeaderBlob.Write(written));

        Assert.Equal(carried, read.CarriedPacks);
        Assert.Equal(new PackSave(2, 7, 0), read.Pack);
        Assert.Equal(written.Inventory, read.Inventory);
    }

    [Fact]
    public void AWorldWrittenBeforeCarriedChargesExistedOpensWithNone()
    {
        // The fourth migration, under the same policy: version 4 is version 5 without its carried
        // section, and "no carried pack holds a charge" is exactly true of a world whose every
        // swap forfeited it.
        var pack = new PackSave(2, 99, 1);
        byte[] payload = PayloadOf(WorldHeaderBlob.Write(WithInventory((2, 2)) with { Pack = pack, ElapsedMillis = 42 }));

        byte[] older = payload[..^CarriedAbsentBytes];
        WorldHeader read = WorldHeaderBlob.Read(Reframe(WithoutReliefVariation(older), version: 4));

        Assert.Empty(read.CarriedPacks);
        Assert.Equal(pack, read.Pack);
        Assert.Equal(42, read.ElapsedMillis);
    }

    [Fact]
    public void AWorldWrittenBeforeRegionsExistedOpensWithEvenLumpiness()
    {
        // The fifth migration, and the first that is not a truncation. Every earlier bump appended
        // a section, so an older header was the payload minus its tail; the relief variation sits
        // at the end of the *settings block*, in the middle of the payload, so a v5 header is this
        // one with four bytes cut out of its middle and everything after them shifted down. That
        // is the whole reason SettingsBytesFor exists, and reading the block at the new length
        // would silently take the material table's first four bytes as a float.
        var pack = new PackSave(2, 99, 1);
        byte[] payload = PayloadOf(WorldHeaderBlob.Write(
            WithInventory((2, 2)) with { Pack = pack, ElapsedMillis = 42 }));

        WorldHeader read = WorldHeaderBlob.Read(
            Reframe(WithoutReliefVariation(payload), version: 5));

        Assert.Equal(0.0f, read.Settings.ReliefVariation);

        // Everything past the settings block still lands where the reader expects it, which is
        // the half of this that a wrong offset would break.
        Assert.Equal(pack, read.Pack);
        Assert.Equal(42, read.ElapsedMillis);
        Assert.Equal([new InventoryEntry(2, 2)], read.Inventory);
        Assert.Equal(UnusualSettings() with { ReliefVariation = 0.0f }, read.Settings);
    }

    [Fact]
    public void ThePlayedWorldsRealV3HeaderOpensWithEvenLumpiness()
    {
        // The real file again. Every world written before 2026-09-23 is a world of even
        // lumpiness, and this is one of them rather than a blob the current writer produced.
        Assert.Equal(0.0f, WorldHeaderBlob.Read(PlayedWorldV3Header()).Settings.ReliefVariation);
    }

    [Fact]
    public void ThePlayedWorldsRealV3HeaderOpensWithNoCarriedCharge()
    {
        WorldHeader read = WorldHeaderBlob.Read(PlayedWorldV3Header());

        Assert.Empty(read.CarriedPacks);
    }

    [Fact]
    public void ACarriedPackTheInventoryDoesNotHoldIsRefusedOnWriteAndOnReadNamingTheCount()
    {
        // Two charged packs of material 2 against an inventory holding one: a session that lost
        // count. The count is the truth and the charges annotate it.
        var error = Assert.Throws<ArgumentException>(() => WorldHeaderBlob.Write(
            WithInventory((2, 1)) with { CarriedPacks = [new PackSave(2, 1, 0), new PackSave(2, 2, 0)] }));
        Assert.Contains("2 carried pack(s)", error.Message);
        Assert.Contains("holds 1", error.Message);

        // And with none of it at all — the inventory has no row for the material.
        Assert.Throws<ArgumentException>(() => WorldHeaderBlob.Write(
            WithInventory((1, 1)) with { CarriedPacks = [new PackSave(2, 1, 0)] }));

        // On read: written honestly with two held and two charged, then the inventory row's
        // count rewritten to one. The row sits before the trailer: the player flag, the clock and
        // lattice, the pack flag, the carried count and two carried rows.
        byte[] payload = PayloadOf(WorldHeaderBlob.Write(
            WithInventory((2, 2)) with { CarriedPacks = [new PackSave(2, 1, 0), new PackSave(2, 2, 0)] }));
        int trailer = 1 + ClockAndLatticeBytes + PackAbsentBytes + CarriedAbsentBytes + (2 * PackPayloadBytes);
        int row = payload.Length - trailer - EntryBytes;
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(row)));
        Assert.Equal(2L, BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(row + sizeof(int))));
        BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(row + sizeof(int)), 1);

        var refused = Assert.Throws<BlobFormatException>(() => WorldHeaderBlob.Read(Reframe(payload)));
        Assert.Contains("holds 1", refused.Message);
    }

    [Fact]
    public void ACarriedPackHoldingNothingIsRefusedBecauseAnEmptyPackIsACount()
    {
        Assert.Throws<ArgumentException>(() => WorldHeaderBlob.Write(
            WithInventory((2, 1)) with { CarriedPacks = [new PackSave(2, 0, 0)] }));

        // On read: the one carried row's charge sits twelve bytes before the end.
        byte[] payload = PayloadOf(WorldHeaderBlob.Write(WithInventory((2, 1)) with { CarriedPacks = [new PackSave(2, 1, 0)] }));
        BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(payload.Length - 12), 0);

        var refused = Assert.Throws<BlobFormatException>(() => WorldHeaderBlob.Read(Reframe(payload)));
        Assert.Contains("holds nothing", refused.Message);
    }

    [Fact]
    public void ACarriedPackIsHeldToTheEquippedPacksThreeRulesNamingWhichEntry()
    {
        var past = Assert.Throws<ArgumentException>(() => WorldHeaderBlob.Write(
            WithInventory((2, 1)) with { CarriedPacks = [new PackSave(9, 1, 0)] }));
        Assert.Contains("Carried pack 0", past.Message);
        Assert.Contains("material ID 9", past.Message);

        Assert.Throws<ArgumentException>(() => WorldHeaderBlob.Write(
            WithInventory((2, 1)) with { CarriedPacks = [new PackSave(2, 1, 16)] }));
        Assert.Throws<ArgumentException>(() => WorldHeaderBlob.Write(
            WithInventory((2, 1)) with { CarriedPacks = [new PackSave(2, -1, 0)] }));

        // On read the channel is the last four bytes of the one carried row.
        byte[] payload = PayloadOf(WorldHeaderBlob.Write(WithInventory((2, 1)) with { CarriedPacks = [new PackSave(2, 1, 0)] }));
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(payload.Length - 4), 16);
        var refused = Assert.Throws<BlobFormatException>(() => WorldHeaderBlob.Read(Reframe(payload)));
        Assert.Contains("Carried pack 0", refused.Message);
    }

    [Fact]
    public void ACarriedCountPastTheBytesIsRefusedBeforeAnyRowIsRead()
    {
        byte[] payload = PayloadOf(WorldHeaderBlob.Write(WithInventory((2, 1)) with { CarriedPacks = [new PackSave(2, 1, 0)] }));
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(payload.Length - PackPayloadBytes - sizeof(int)), 2);

        var refused = Assert.Throws<BlobFormatException>(() => WorldHeaderBlob.Read(Reframe(payload)));
        Assert.Contains("bytes remain", refused.Message);
    }

    /// <summary>The blob's payload, decompressed if it was compressed. BCL only.</summary>
    private static byte[] PayloadOf(byte[] blob)
    {
        int length = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(blob.AsSpan(2));
        byte[] stored = blob[10..];

        if ((blob[1] & 0x01) == 0) return stored;

        using var input = new MemoryStream(stored);
        using var inflate = new System.IO.Compression.ZLibStream(
            input, System.IO.Compression.CompressionMode.Decompress);
        var output = new byte[length];
        inflate.ReadExactly(output);
        return output;
    }

    /// <summary>Wraps a payload back into a valid, uncompressed frame.</summary>
    /// <summary>
    /// The same payload as a build before v6 would have written it: the relief variation's four
    /// bytes cut out of the end of the settings block, and everything after them shifted down.
    /// </summary>
    /// <remarks>
    /// <b>Every migration test needs this now, and that is what makes v6 different from the four
    /// bumps before it.</b> Those appended a section, so an older header was this one minus its
    /// tail and a test could build one by truncating. This one grew a block in the middle, so a
    /// payload written by today's writer and simply relabelled v4 is not a v4 payload — it is a
    /// v4 payload with four bytes of float wedged in front of its material table, which is how
    /// all four of those tests failed the moment the field was added. They were right to.
    /// </remarks>
    private static byte[] WithoutReliefVariation(byte[] payload)
    {
        const int ReliefOffset = 53;

        var older = new byte[payload.Length - sizeof(float)];
        payload.AsSpan(0, ReliefOffset).CopyTo(older);
        payload.AsSpan(ReliefOffset + sizeof(float)).CopyTo(older.AsSpan(ReliefOffset));
        return older;
    }

    private static byte[] Reframe(byte[] payload, byte version = WorldHeaderBlob.Version)
    {
        var blob = new byte[10 + payload.Length];
        blob[0] = version;
        blob[1] = 0x00;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(2), payload.Length);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(6), payload.Length);
        payload.CopyTo(blob.AsSpan(10));
        return blob;
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i + needle.Length <= haystack.Length; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle)) return i;
        }

        return -1;
    }
}
