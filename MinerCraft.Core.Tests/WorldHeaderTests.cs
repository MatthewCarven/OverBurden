using MinerCraft.Core.Content;
using MinerCraft.Core.Persistence;
using MinerCraft.Core.Worldgen;

namespace MinerCraft.Core.Tests;

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
    private static readonly Identifier Air = Identifier.Parse("minercraft:air");
    private static readonly Identifier Stone = Identifier.Parse("minercraft:stone");
    private static readonly Identifier Copper = Identifier.Parse("deepdig:copper_ore");

    /// <summary>Every field distinct and none at its default, so a forgotten one shows up.</summary>
    private static WorldGenSettings UnusualSettings() => new()
    {
        Seed = 0xFEEDFACECAFEBEEDUL,
        SeaLevelVoxels = -17,
        SurfaceAmplitudeVoxels = 33.5f,
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

        byte[] needle = System.Text.Encoding.UTF8.GetBytes("minercraft:air");
        int at = IndexOf(payload, needle);
        Assert.True(at >= 0, "the identifier should appear verbatim in the uncompressed payload");

        // Same length, so nothing else in the layout shifts.
        System.Text.Encoding.UTF8.GetBytes("minercraft:foo").CopyTo(payload.AsSpan(at));

        var error = Assert.Throws<BlobFormatException>(() => WorldHeaderBlob.Read(Reframe(payload)));
        Assert.Contains("minercraft:air", error.Message);
    }

    [Fact]
    public void ATableWhoseFirstEntryIsNotAirIsRefusedOnWrite()
    {
        // Refused at write as well, because a table this wrong reaching disk is a bug upstream and
        // finding it here names the session that produced it rather than the one that later
        // failed to open the world.
        var error = Assert.Throws<ArgumentException>(() => WorldHeaderBlob.Write(Header(Stone, Air)));
        Assert.Contains("minercraft:air", error.Message);
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
    private static byte[] Reframe(byte[] payload)
    {
        var blob = new byte[10 + payload.Length];
        blob[0] = WorldHeaderBlob.Version;
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
