using System.Buffers.Binary;
using System.IO.Compression;
using MinerCraft.Core.Content;
using MinerCraft.Core.Persistence;
using MinerCraft.Core.Voxels;

namespace MinerCraft.Core.Tests;

/// <summary>
/// The M6a tests that decide whether a saved world comes back as the world that was saved.
/// </summary>
/// <remarks>
/// <para>
/// <b>The oracle is round-trip identity, voxel for voxel</b> — no reference implementation to get
/// wrong, the same category as M4a's sealed room and M5b's propagation invariant.
/// </para>
/// <para>
/// <b>And its limit is stated rather than trusted: a round trip runs the same writer and the same
/// reader, so a symmetric bug passes.</b> A field written to the wrong offset and read from the
/// wrong offset agrees with itself perfectly. So the round-trip fuzz is joined by two things that
/// break the symmetry — a written blob decoded by hand, and a hand-built blob given to the reader.
/// Neither shares any code with the half it is checking.
/// </para>
/// </remarks>
public class ChunkBlobTests
{
    private const int Air = 0;
    private const int Stone = 1;
    private const int Dirt = 2;

    private static readonly ChunkPos Somewhere = new(3, -2, 7);

    /// <summary>A table wide enough that no test needs to think about material validation.</summary>
    private static MaterialTable Materials(int count = 300)
    {
        var hardness = new float[count];
        for (int i = 1; i < count; i++) hardness[i] = 1.0f;

        var opaque = new bool[count];
        for (int i = 1; i < count; i++) opaque[i] = true;

        return MaterialTable.ForTesting(
            isOpaque: opaque,
            isFluid: new bool[count],
            textureLayer: new int[count],
            hardness: hardness);
    }

    private static void AssertSameVoxels(VoxelChunk expected, VoxelChunk actual)
    {
        for (int i = 0; i < VoxelConstants.ChunkVolume; i++)
        {
            if (expected.GetAt(i) != actual.GetAt(i))
            {
                var (x, y, z) = VoxelPos.FromIndex(i);
                Assert.Fail(
                    $"Voxel {i} ({x}, {y}, {z}): expected {expected.GetAt(i)}, got {actual.GetAt(i)}.");
            }
        }
    }

    private static VoxelChunk RoundTrip(VoxelChunk chunk, bool compact = false)
    {
        byte[] blob = ChunkBlob.Write(Somewhere, chunk, compact);
        return ChunkBlob.Read(blob, Somewhere, Materials());
    }

    // -------------------------------------------------------------------------------------
    // Round trip, across every storage representation.
    // -------------------------------------------------------------------------------------

    [Fact]
    public void AUniformChunkRoundTrips()
    {
        var chunk = new VoxelChunk(Stone);
        AssertSameVoxels(chunk, RoundTrip(chunk));
    }

    [Fact]
    public void AnAirChunkRoundTrips()
    {
        // The most common chunk in the world, and the one whose material ID is a constant the
        // engine assumes. Worth its own case rather than folding into the uniform one.
        var chunk = new VoxelChunk(Air);
        VoxelChunk restored = RoundTrip(chunk);

        AssertSameVoxels(chunk, restored);
        Assert.True(restored.IsUniform);
        Assert.Equal(Air, restored.UniformValue);
    }

    [Theory]
    [InlineData(2)]   // 1 bit
    [InlineData(3)]   // 2 bits, palette not a power of two — indices past the end are addressable
    [InlineData(4)]   // 2 bits, exactly full
    [InlineData(17)]  // 8 bits, the width the promotion path reaches last
    [InlineData(256)] // 8 bits, exactly full, one short of direct storage
    public void APalettisedChunkRoundTripsAtEveryPackingWidth(int materials)
    {
        var chunk = new VoxelChunk(Air);

        for (int i = 0; i < VoxelConstants.ChunkVolume; i++)
        {
            chunk.SetAt(i, i % materials);
        }

        Assert.Equal(VoxelChunk.StorageKind.Palettised, chunk.Kind);
        AssertSameVoxels(chunk, RoundTrip(chunk));
    }

    [Fact]
    public void ADirectChunkRoundTrips()
    {
        var chunk = new VoxelChunk(Air);

        // 257 distinct materials pushes past the palette's ceiling and into direct storage.
        for (int i = 0; i < VoxelConstants.ChunkVolume; i++)
        {
            chunk.SetAt(i, i % 257);
        }

        Assert.Equal(VoxelChunk.StorageKind.Direct, chunk.Kind);
        AssertSameVoxels(chunk, RoundTrip(chunk));
    }

    [Fact]
    public void TheOracleIsVoxelsAndNotRepresentation()
    {
        // A chunk that held a second material and no longer does is still palettised, with a dead
        // entry. Compacting on write demotes it to uniform, so what comes back is a *different
        // representation holding identical voxels* — and that is correct. A test asserting on
        // Kind would fail here on working code, which is M3b's shape of oracle: wrong in the
        // subject's favour.
        var chunk = new VoxelChunk(Stone);
        chunk.SetAt(0, Dirt);
        chunk.SetAt(0, Stone);

        Assert.Equal(VoxelChunk.StorageKind.Palettised, chunk.Kind);

        // compact: true explicitly — it is no longer the default, and this is the arm where the
        // representation is allowed to change under the round trip.
        VoxelChunk restored = RoundTrip(chunk.Clone(), compact: true);

        AssertSameVoxels(chunk, restored);
        Assert.True(restored.IsUniform);

        // And with the shipped default the representation survives, which is the other half of
        // the same claim: the oracle is the voxels either way.
        VoxelChunk verbatim = RoundTrip(chunk.Clone());
        AssertSameVoxels(chunk, verbatim);
        Assert.Equal(VoxelChunk.StorageKind.Palettised, verbatim.Kind);
    }

    [Fact]
    public void CompactingOrNotProducesTheSameVoxels()
    {
        var chunk = new VoxelChunk(Air);
        for (int i = 0; i < VoxelConstants.ChunkVolume; i++)
        {
            chunk.SetAt(i, i % 20);
        }

        // Leave dead palette entries behind: everything that was 19 becomes 1.
        for (int i = 0; i < VoxelConstants.ChunkVolume; i++)
        {
            if (chunk.GetAt(i) == 19) chunk.SetAt(i, 1);
        }

        // Clone first — Compact mutates, so the two arms must not share a chunk.
        VoxelChunk compacted = RoundTrip(chunk.Clone(), compact: true);
        VoxelChunk verbatim = RoundTrip(chunk.Clone(), compact: false);

        AssertSameVoxels(chunk, compacted);
        AssertSameVoxels(chunk, verbatim);
    }

    [Fact]
    public void RoundTripSurvivesAFuzzOfRandomChunks()
    {
        // Break-recipe: flipping any read/write pair in ChunkBlob.WritePayload fails this in
        // well under a second. Watched fail before being trusted — swapping the palette-count and
        // bits fields fails 9 tests including this one.
        var random = new Random(20260831);
        MaterialTable materials = Materials();

        for (int iteration = 0; iteration < 60; iteration++)
        {
            int distinct = 1 + random.Next(300);
            var chunk = new VoxelChunk(random.Next(distinct));

            // A mix of long runs and scatter, because deflate and the packing behave differently
            // on each and a bug that only shows on one would otherwise be a coin flip.
            for (int i = 0; i < VoxelConstants.ChunkVolume; i++)
            {
                if (random.Next(4) == 0) chunk.SetAt(i, random.Next(distinct));
            }

            var position = new ChunkPos(random.Next(-500, 500), random.Next(-40, 40), random.Next(-500, 500));
            byte[] blob = ChunkBlob.Write(position, chunk.Clone(), compact: random.Next(2) == 0);

            AssertSameVoxels(chunk, ChunkBlob.Read(blob, position, materials));
        }
    }

    // -------------------------------------------------------------------------------------
    // Breaking the writer/reader symmetry: one side at a time, no shared code.
    // -------------------------------------------------------------------------------------

    [Fact]
    public void AUniformBlobIsExactlyTheseBytes()
    {
        // The whole format, hand-checkable, because a uniform payload is five bytes and deflate
        // cannot shrink five bytes — so this arm also pins "store verbatim when compression does
        // not pay". Every other pinning test has to inflate first.
        byte[] blob = ChunkBlob.Write(new ChunkPos(3, -2, 7), new VoxelChunk(Stone));

        byte[] expected =
        [
            ChunkBlob.Version,          // version
            0x00,                       // flags: stored verbatim
            0x05, 0x00, 0x00, 0x00,     // payload length, uncompressed
            0x05, 0x00, 0x00, 0x00,     // stored length — equal, because it was not compressed
            0x03, 0x00, 0x00, 0x00,     // cx =  3
            0xFE, 0xFF, 0xFF, 0xFF,     // cy = -2
            0x07, 0x00, 0x00, 0x00,     // cz =  7
            0x00,                       // storage kind: uniform
            0x01, 0x00, 0x00, 0x00,     // material 1
        ];

        Assert.Equal(expected, blob);
    }

    [Fact]
    public void TheWriterPutsThePaletteAndWordsWhereTheFormatSaysItDoes()
    {
        // Decodes a written blob by hand, sharing no code with ChunkBlob. This is the arm that
        // catches the writer being self-consistently wrong — and specifically the one recipe the
        // type system cannot prevent: writing the palette as per-chunk indices (0,1,2,…) rather
        // than world material IDs. That mistake round-trips flawlessly inside one session and
        // decodes to the wrong materials the moment a mod moves.
        var chunk = new VoxelChunk(Air);
        chunk.SetAt(0, 40);
        chunk.SetAt(1, 41);
        chunk.SetAt(2, 42);

        // Written verbatim, so the palette is in the order the chunk built it: the material it
        // was uniformly holding, then each new one as it arrived.
        (int[] palette, ulong firstWord, int bits) = DecodePalettised(
            ChunkBlob.Write(Somewhere, chunk.Clone(), compact: false));

        Assert.Equal(2, bits); // 4 entries needs 2 bits
        Assert.Equal([Air, 40, 41, 42], palette);

        // World material IDs, not 0,1,2,3 — the one mistake here that the type system cannot
        // catch and that a round trip inside one session cannot see.
        Assert.Equal(1UL, (firstWord >> 0) & 0b11); // voxel 0 -> palette[1] = 40
        Assert.Equal(2UL, (firstWord >> 2) & 0b11);
        Assert.Equal(3UL, (firstWord >> 4) & 0b11);
        Assert.Equal(0UL, (firstWord >> 6) & 0b11); // voxel 3 -> palette[0] = air
    }

    [Fact]
    public void CompactingReordersThePaletteIntoScanOrder()
    {
        // Not an incidental detail — it is the observable difference between the two arms of the
        // `compact` switch, and the reason the test above pins the verbatim one. Compact rebuilds
        // the palette by walking voxels 0..32767 and taking materials as it first meets them, so
        // air moves from entry 0 to last: voxels 0, 1 and 2 hold 40, 41 and 42, and the first air
        // is voxel 3.
        var chunk = new VoxelChunk(Air);
        chunk.SetAt(0, 40);
        chunk.SetAt(1, 41);
        chunk.SetAt(2, 42);

        (int[] verbatim, _, _) = DecodePalettised(ChunkBlob.Write(Somewhere, chunk.Clone(), compact: false));
        (int[] compacted, _, _) = DecodePalettised(ChunkBlob.Write(Somewhere, chunk.Clone(), compact: true));

        Assert.Equal([Air, 40, 41, 42], verbatim);
        Assert.Equal([40, 41, 42, Air], compacted);

        // Different bytes, same voxels. Which is the whole reason the oracle is voxel-for-voxel.
        AssertSameVoxels(chunk, RoundTrip(chunk.Clone(), compact: false));
        AssertSameVoxels(chunk, RoundTrip(chunk.Clone(), compact: true));
    }

    /// <summary>Pulls the palette, packing width and first packed word out of a written blob.</summary>
    /// <remarks>Shares no code with <see cref="ChunkBlob"/> — that is the entire point of it.</remarks>
    private static (int[] Palette, ulong FirstWord, int Bits) DecodePalettised(byte[] blob)
    {
        byte[] payload = Payload(blob, headerBytes: 22);
        Assert.Equal(1, payload[0]); // palettised

        int count = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(1));
        int bits = payload[3];

        var palette = new int[count];
        for (int i = 0; i < count; i++)
        {
            palette[i] = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(4 + (i * 4)));
        }

        return (palette, BinaryPrimitives.ReadUInt64LittleEndian(payload.AsSpan(4 + (count * 4))), bits);
    }

    [Fact]
    public void TheReaderAcceptsAHandBuiltBlobItNeverWrote()
    {
        // The other direction: bytes assembled here, uncompressed, given to the reader. Nothing
        // ChunkBlob.Write produced was involved, so a writer and reader that are wrong together
        // cannot both pass this and the test above.
        var payload = new List<byte> { 1 };                     // palettised
        payload.AddRange([0x02, 0x00]);                         // palette count 2
        payload.Add(1);                                         // 1 bit per index
        payload.AddRange(BitConverter.GetBytes(Air));           // entry 0
        payload.AddRange(BitConverter.GetBytes(Dirt));          // entry 1

        int words = VoxelConstants.ChunkVolume / 64;
        for (int i = 0; i < words; i++)
        {
            // Every voxel in the first word is entry 1; the rest are entry 0.
            payload.AddRange(BitConverter.GetBytes(i == 0 ? ulong.MaxValue : 0UL));
        }

        var blob = new List<byte>
        {
            ChunkBlob.Version,
            0x00,
        };
        blob.AddRange(BitConverter.GetBytes(payload.Count));
        blob.AddRange(BitConverter.GetBytes(payload.Count)); // stored == payload, uncompressed
        blob.AddRange(BitConverter.GetBytes(Somewhere.X));
        blob.AddRange(BitConverter.GetBytes(Somewhere.Y));
        blob.AddRange(BitConverter.GetBytes(Somewhere.Z));
        blob.AddRange(payload);

        VoxelChunk chunk = ChunkBlob.Read(blob.ToArray(), Somewhere, Materials());

        for (int i = 0; i < 64; i++) Assert.Equal(Dirt, chunk.GetAt(i));
        for (int i = 64; i < VoxelConstants.ChunkVolume; i++) Assert.Equal(Air, chunk.GetAt(i));
    }

    // -------------------------------------------------------------------------------------
    // Refusals. Every one of these could be "repaired" instead, and every repair is worse.
    // -------------------------------------------------------------------------------------

    [Fact]
    public void AnUnknownVersionIsRefusedRatherThanRead()
    {
        byte[] blob = ChunkBlob.Write(Somewhere, new VoxelChunk(Stone));
        blob[0] = ChunkBlob.Version + 1;

        var error = Assert.Throws<BlobFormatException>(() => ChunkBlob.Read(blob, Somewhere, Materials()));
        Assert.Contains("version", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnUnknownFlagBitIsRefused()
    {
        // A flag added by a later build without bumping the version. Refusing means forgetting to
        // bump fails loudly rather than reading the payload under the wrong rules.
        byte[] blob = ChunkBlob.Write(Somewhere, new VoxelChunk(Stone));
        blob[1] |= 0x40;

        Assert.Throws<BlobFormatException>(() => ChunkBlob.Read(blob, Somewhere, Materials()));
    }

    [Fact]
    public void ATruncatedBlobIsRefused()
    {
        byte[] blob = ChunkBlob.Write(Somewhere, new VoxelChunk(Stone));

        for (int length = 0; length < blob.Length; length++)
        {
            byte[] cut = blob[..length];
            Assert.ThrowsAny<BlobFormatException>(() => ChunkBlob.Read(cut, Somewhere, Materials()));
        }
    }

    [Fact]
    public void ATruncatedCompressedPayloadIsRefused()
    {
        var chunk = new VoxelChunk(Air);
        for (int i = 0; i < VoxelConstants.ChunkVolume; i++) chunk.SetAt(i, i % 12);

        byte[] blob = ChunkBlob.Write(Somewhere, chunk);
        Assert.Equal(0x01, blob[1] & 0x01); // it really is deflated, or this proves nothing

        // Cut the compressed tail but leave the header's declared length intact, so only the
        // inflate-to-exactly-N check can catch it.
        byte[] cut = blob[..(blob.Length - 8)];
        Assert.Throws<BlobFormatException>(() => ChunkBlob.Read(cut, Somewhere, Materials()));
    }

    [Fact]
    public void ABlobFiledUnderTheWrongKeyIsRefused()
    {
        byte[] blob = ChunkBlob.Write(Somewhere, new VoxelChunk(Stone));

        var error = Assert.Throws<BlobFormatException>(
            () => ChunkBlob.Read(blob, Somewhere.Offset(0, 1, 0), Materials()));

        Assert.Contains("wrong key", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AMaterialPastThisWorldsTableIsRefused()
    {
        // M3c's recorded item: MaterialTable indexes raw, so an ID past Count throws
        // IndexOutOfRangeException from inside the mesher on a worker thread. "The fix is
        // validating chunk contents at load: M6." This is that.
        byte[] blob = ChunkBlob.Write(Somewhere, new VoxelChunk(99));

        var error = Assert.Throws<BlobFormatException>(
            () => ChunkBlob.Read(blob, Somewhere, Materials(count: 10)));

        Assert.Contains("past this world's table", error.Message);
    }

    [Fact]
    public void AMaterialPastTheTableIsRefusedInEveryStorageKind()
    {
        // The uniform case above is the easy one. A palette entry and a direct voxel reach the
        // check by different routes, and a validation added to only one of them would leave two
        // thirds of the hazard open.
        var palettised = new VoxelChunk(Air);
        palettised.SetAt(0, 99);

        var direct = new VoxelChunk(Air);
        for (int i = 0; i < VoxelConstants.ChunkVolume; i++) direct.SetAt(i, i % 257);

        Assert.Equal(VoxelChunk.StorageKind.Palettised, palettised.Kind);
        Assert.Equal(VoxelChunk.StorageKind.Direct, direct.Kind);

        foreach (VoxelChunk chunk in new[] { palettised, direct })
        {
            byte[] blob = ChunkBlob.Write(Somewhere, chunk);
            Assert.Throws<BlobFormatException>(() => ChunkBlob.Read(blob, Somewhere, Materials(count: 10)));
        }
    }

    [Fact]
    public void APaletteIndexPastTheEndOfThePaletteIsRefused()
    {
        // The expensive check, and the one that is easy to leave out. A 3-entry palette at 2 bits
        // leaves index 3 addressable; reading it is an IndexOutOfRangeException raised inside the
        // mesher, on a worker thread, a long way from the corrupt byte that caused it.
        var payload = new List<byte> { 1 };
        payload.AddRange([0x03, 0x00]);
        payload.Add(2);
        payload.AddRange(BitConverter.GetBytes(Air));
        payload.AddRange(BitConverter.GetBytes(Stone));
        payload.AddRange(BitConverter.GetBytes(Dirt));

        int words = VoxelConstants.ChunkVolume / 32;
        for (int i = 0; i < words; i++)
        {
            // 0b11 = index 3, one past a 3-entry palette.
            payload.AddRange(BitConverter.GetBytes(i == 0 ? ulong.MaxValue : 0UL));
        }

        var error = Assert.Throws<BlobFormatException>(
            () => ChunkBlob.Read(HandBuilt(payload), Somewhere, Materials()));

        Assert.Contains("palette index", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ADuplicatePaletteEntryIsRefused()
    {
        var payload = new List<byte> { 1 };
        payload.AddRange([0x02, 0x00]);
        payload.Add(1);
        payload.AddRange(BitConverter.GetBytes(Stone));
        payload.AddRange(BitConverter.GetBytes(Stone));

        for (int i = 0; i < VoxelConstants.ChunkVolume / 64; i++)
        {
            payload.AddRange(BitConverter.GetBytes(0UL));
        }

        var error = Assert.Throws<BlobFormatException>(
            () => ChunkBlob.Read(HandBuilt(payload), Somewhere, Materials()));

        Assert.Contains("twice", error.Message);
    }

    [Fact]
    public void AnImpossiblePackingWidthIsRefused()
    {
        var payload = new List<byte> { 1 };
        payload.AddRange([0x02, 0x00]);
        payload.Add(3); // not 1, 2, 4 or 8
        payload.AddRange(BitConverter.GetBytes(Air));
        payload.AddRange(BitConverter.GetBytes(Stone));

        Assert.Throws<BlobFormatException>(() => ChunkBlob.Read(HandBuilt(payload), Somewhere, Materials()));
    }

    [Fact]
    public void AnUnknownStorageKindIsRefused()
    {
        Assert.Throws<BlobFormatException>(
            () => ChunkBlob.Read(HandBuilt([9, 0, 0, 0, 0]), Somewhere, Materials()));
    }

    [Fact]
    public void AnEmptyPaletteIsRefused()
    {
        var payload = new List<byte> { 1 };
        payload.AddRange([0x00, 0x00]);
        payload.Add(1);

        Assert.Throws<BlobFormatException>(() => ChunkBlob.Read(HandBuilt(payload), Somewhere, Materials()));
    }

    // -------------------------------------------------------------------------------------
    // VoxelChunk.FromPalettised's own guards, reached directly.
    //
    // These exist because the break-recipe battery found the packing-width check green:
    // ChunkBlob.ReadPalettised validates the width before calling, so the factory's own check is
    // unreachable through the blob path. That is the M4a zeroable-contact-epsilon category —
    // belt and braces, not a hole — and both are kept, because a validating factory for
    // untrusted bytes must not depend on its caller having checked first. Testing it directly is
    // what turns "unreachable duplicate" into "tested duplicate".
    //
    // It is not decorative. SetWidth(3) computes TrailingZeroCount(3) = 0 and a value mask of 7,
    // which is a silently wrong packing rather than a crash.
    // -------------------------------------------------------------------------------------

    [Fact]
    public void TheFactoryRefusesAPackingWidthThatIsNotAPowerOfTwo()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => VoxelChunk.FromPalettised([Air, Stone], 3, new ulong[VoxelConstants.ChunkVolume / 21]));
    }

    [Fact]
    public void TheFactoryRefusesAWidthTooNarrowForItsPalette()
    {
        // 1 bit cannot index three entries. Sufficiency is required rather than exactness — see
        // FromPalettised — so this is the boundary that actually matters.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => VoxelChunk.FromPalettised([Air, Stone, Dirt], 1, new ulong[VoxelChunk.WordsFor(1)]));

        // ...and 2 bits can, so the check is not simply refusing everything.
        Assert.NotNull(VoxelChunk.FromPalettised([Air, Stone, Dirt], 2, new ulong[VoxelChunk.WordsFor(2)]));
    }

    [Fact]
    public void TheFactoryRefusesAWrongWordCount()
    {
        Assert.Throws<ArgumentException>(
            () => VoxelChunk.FromPalettised([Air, Stone], 1, new ulong[VoxelChunk.WordsFor(1) - 1]));
    }

    [Fact]
    public void TheFactoryRefusesAnOversizedPalette()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => VoxelChunk.FromPalettised(new int[257], 8, new ulong[VoxelChunk.WordsFor(8)]));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => VoxelChunk.FromPalettised([], 1, new ulong[VoxelChunk.WordsFor(1)]));
    }

    // -------------------------------------------------------------------------------------

    /// <summary>Wraps a payload in a valid, uncompressed frame for <see cref="Somewhere"/>.</summary>
    private static byte[] HandBuilt(IReadOnlyCollection<byte> payload)
    {
        var blob = new List<byte> { ChunkBlob.Version, 0x00 };
        blob.AddRange(BitConverter.GetBytes(payload.Count));
        blob.AddRange(BitConverter.GetBytes(payload.Count)); // stored == payload, uncompressed
        blob.AddRange(BitConverter.GetBytes(Somewhere.X));
        blob.AddRange(BitConverter.GetBytes(Somewhere.Y));
        blob.AddRange(BitConverter.GetBytes(Somewhere.Z));
        blob.AddRange(payload);
        return blob.ToArray();
    }

    /// <summary>Extracts a blob's payload, inflating if needed. Uses only the BCL.</summary>
    private static byte[] Payload(byte[] blob, int headerBytes)
    {
        int length = BinaryPrimitives.ReadInt32LittleEndian(blob.AsSpan(2));
        byte[] stored = blob[headerBytes..];

        if ((blob[1] & 0x01) == 0)
        {
            return stored;
        }

        using var input = new MemoryStream(stored);
        using var inflate = new ZLibStream(input, CompressionMode.Decompress);
        var output = new byte[length];
        inflate.ReadExactly(output);
        return output;
    }
}
