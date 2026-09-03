using Overburden.Core.Content;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

public class VoxelChunkTests
{
    private const int Volume = VoxelConstants.ChunkVolume;

    /// <summary>Sets voxel i to material i+1, giving a palette of <c>count + 1</c> with air.</summary>
    private static VoxelChunk WithDistinctMaterials(int count)
    {
        var chunk = new VoxelChunk();
        for (int i = 0; i < count; i++)
        {
            chunk.SetAt(i, i + 1);
        }

        return chunk;
    }

    [Fact]
    public void NewChunkIsUniformAirAndAllocatesNothing()
    {
        var chunk = new VoxelChunk();

        Assert.True(chunk.IsUniform);
        Assert.Equal(MaterialIds.AirNumericId, chunk.UniformValue);
        Assert.Equal(0, chunk.EstimatedBytes);
        Assert.Equal(1, chunk.PaletteSize);

        Assert.Equal(MaterialIds.AirNumericId, chunk.GetAt(0));
        Assert.Equal(MaterialIds.AirNumericId, chunk.GetAt(Volume - 1));
        Assert.Equal(MaterialIds.AirNumericId, chunk.Get(31, 31, 31));
    }

    [Fact]
    public void WritingTheValueAUniformChunkAlreadyHolds_ChangesNothing()
    {
        // Worldgen fills large volumes with what is already there; that must stay free.
        var chunk = new VoxelChunk(7);
        chunk.SetAt(1234, 7);

        Assert.True(chunk.IsUniform);
        Assert.Equal(0, chunk.EstimatedBytes);
    }

    [Fact]
    public void FirstDifferentWrite_PromotesToOneBitPalette()
    {
        var chunk = new VoxelChunk();
        chunk.Set(3, 4, 5, 1);

        Assert.Equal(VoxelChunk.StorageKind.Palettised, chunk.Kind);
        Assert.Equal(2, chunk.PaletteSize);
        Assert.Equal(1, chunk.Get(3, 4, 5));

        // Everything else still reads as what the chunk uniformly was.
        Assert.Equal(MaterialIds.AirNumericId, chunk.Get(3, 4, 6));
        Assert.Equal(MaterialIds.AirNumericId, chunk.GetAt(Volume - 1));

        // 32768 voxels at 1 bit = 4 KB, plus a 2-entry palette.
        Assert.Equal(Volume / 8 + 2 * sizeof(int), chunk.EstimatedBytes);
    }

    [Theory]
    // paletteSize (incl. air), expected bits — the exact widening boundaries
    [InlineData(1, 2, 1)]
    [InlineData(3, 4, 2)]
    [InlineData(4, 5, 4)]
    [InlineData(15, 16, 4)]
    [InlineData(16, 17, 8)]
    [InlineData(255, 256, 8)]
    public void PaletteWidensAtExactlyTheRightBoundaries(int distinct, int expectedPalette, int expectedBits)
    {
        var chunk = WithDistinctMaterials(distinct);

        Assert.Equal(VoxelChunk.StorageKind.Palettised, chunk.Kind);
        Assert.Equal(expectedPalette, chunk.PaletteSize);
        Assert.Equal(expectedBits, VoxelChunk.BitsForPaletteSize(expectedPalette));

        int expectedBytes = Volume / (8 / expectedBits) + expectedPalette * sizeof(int);
        Assert.Equal(expectedBytes, chunk.EstimatedBytes);
    }

    [Fact]
    public void PastTwoHundredAndFiftySixMaterials_FallsBackToDirect()
    {
        // A 16-bit palette index costs the same as the material ID itself, so the palette
        // tier stops paying for itself and direct storage is the honest answer.
        var chunk = WithDistinctMaterials(256);

        Assert.Equal(VoxelChunk.StorageKind.Direct, chunk.Kind);
        Assert.Equal(Volume * sizeof(ushort), chunk.EstimatedBytes);

        for (int i = 0; i < 256; i++)
        {
            Assert.Equal(i + 1, chunk.GetAt(i));
        }
    }

    [Fact]
    public void WideningPreservesEveryExistingVoxel()
    {
        var chunk = new VoxelChunk();
        for (int i = 0; i < 300; i++)
        {
            chunk.SetAt(i * 7, (i % 3) + 1);
        }

        // Force several widenings after the fact.
        for (int m = 1; m <= 20; m++)
        {
            chunk.SetAt(Volume - m, m + 100);
        }

        for (int i = 0; i < 300; i++)
        {
            Assert.Equal((i % 3) + 1, chunk.GetAt(i * 7));
        }

        for (int m = 1; m <= 20; m++)
        {
            Assert.Equal(m + 100, chunk.GetAt(Volume - m));
        }
    }

    [Fact]
    public void EveryVoxelIsIndependentlyAddressable()
    {
        // Catches packing bugs where neighbouring entries share or clobber bits.
        var chunk = new VoxelChunk();

        for (int i = 0; i < Volume; i++)
        {
            chunk.SetAt(i, (i % 15) + 1);
        }

        for (int i = 0; i < Volume; i++)
        {
            Assert.Equal((i % 15) + 1, chunk.GetAt(i));
        }
    }

    [Fact]
    public void Fill_ReturnsToUniformAndReleasesStorage()
    {
        var chunk = WithDistinctMaterials(40);
        Assert.True(chunk.EstimatedBytes > 0);

        chunk.Fill(3);

        Assert.True(chunk.IsUniform);
        Assert.Equal(3, chunk.UniformValue);
        Assert.Equal(0, chunk.EstimatedBytes);
        Assert.Equal(3, chunk.GetAt(9999));
    }

    [Fact]
    public void Compact_DemotesToUniformWhenOnlyOneMaterialSurvives()
    {
        var chunk = new VoxelChunk();
        chunk.SetAt(100, 5);
        Assert.False(chunk.IsUniform);

        // Mined back out again.
        chunk.SetAt(100, MaterialIds.AirNumericId);

        // Deliberately NOT eager: a pick swing must not pay for a 32,768-voxel scan.
        Assert.False(chunk.IsUniform);

        chunk.Compact();

        Assert.True(chunk.IsUniform);
        Assert.Equal(MaterialIds.AirNumericId, chunk.UniformValue);
        Assert.Equal(0, chunk.EstimatedBytes);
    }

    [Fact]
    public void Compact_NarrowsTheWidthWhenMaterialsLeave()
    {
        var chunk = WithDistinctMaterials(20); // palette 21 → 8 bits
        Assert.Equal(21, chunk.PaletteSize);

        // Erase all but two of them.
        for (int i = 2; i < 20; i++)
        {
            chunk.SetAt(i, MaterialIds.AirNumericId);
        }

        Assert.Equal(21, chunk.PaletteSize); // still carrying the dead entries

        chunk.Compact();

        Assert.Equal(3, chunk.PaletteSize); // air, 1, 2
        Assert.Equal(1, chunk.GetAt(0));
        Assert.Equal(2, chunk.GetAt(1));
        Assert.Equal(MaterialIds.AirNumericId, chunk.GetAt(5));
    }

    [Fact]
    public void Compact_BringsDirectStorageBackToAPalette()
    {
        var chunk = WithDistinctMaterials(256);
        Assert.Equal(VoxelChunk.StorageKind.Direct, chunk.Kind);

        for (int i = 3; i < 256; i++)
        {
            chunk.SetAt(i, MaterialIds.AirNumericId);
        }

        chunk.Compact();

        Assert.Equal(VoxelChunk.StorageKind.Palettised, chunk.Kind);
        Assert.Equal(4, chunk.PaletteSize);
        Assert.True(chunk.EstimatedBytes < Volume * sizeof(ushort));
    }

    [Fact]
    public void Compact_OnAUniformChunkIsANoOp()
    {
        var chunk = new VoxelChunk(4);
        chunk.Compact();

        Assert.True(chunk.IsUniform);
        Assert.Equal(4, chunk.UniformValue);
    }

    [Fact]
    public void CopyTo_MatchesVoxelByVoxel()
    {
        var chunk = WithDistinctMaterials(30);
        var buffer = new int[Volume];

        chunk.CopyTo(buffer);

        for (int i = 0; i < Volume; i++)
        {
            Assert.Equal(chunk.GetAt(i), buffer[i]);
        }
    }

    [Fact]
    public void CopyTo_FromAUniformChunkFillsWithoutTouchingPackedStorage()
    {
        var chunk = new VoxelChunk(6);
        var buffer = new int[Volume];

        chunk.CopyTo(buffer);

        Assert.All(buffer, v => Assert.Equal(6, v));
    }

    [Fact]
    public void CopyTo_RejectsAShortDestination()
    {
        var chunk = new VoxelChunk();
        Assert.Throws<ArgumentException>(() => chunk.CopyTo(new int[Volume - 1]));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(Volume)]
    [InlineData(int.MaxValue)]
    public void OutOfRangeIndexThrows(int index)
    {
        var chunk = new VoxelChunk();

        Assert.Throws<ArgumentOutOfRangeException>(() => chunk.GetAt(index));
        Assert.Throws<ArgumentOutOfRangeException>(() => chunk.SetAt(index, 1));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(ushort.MaxValue + 1)]
    public void MaterialIdOutsideSixteenBitsThrows(int materialId)
    {
        // Direct storage is ushort, so the whole class is bounded by it.
        var chunk = new VoxelChunk();
        Assert.Throws<ArgumentOutOfRangeException>(() => chunk.SetAt(0, materialId));
    }

    [Fact]
    public void MaximumEntropyChunkAtFourBitsCostsSixteenKilobytes()
    {
        // Every voxel independently random across 9 materials — the worst case the 4-bit
        // tier can be asked to hold, and nothing like a real chunk. Raw ushort storage of
        // the same thing would be 64 KB.
        var chunk = new VoxelChunk();
        var rng = new Random(99);

        for (int i = 0; i < Volume; i++)
        {
            chunk.SetAt(i, rng.Next(1, 9)); // 8 materials + air = palette of 9 → 4 bits
        }

        Assert.Equal(9, chunk.PaletteSize);
        Assert.Equal(4, VoxelChunk.BitsForPaletteSize(chunk.PaletteSize));
        Assert.Equal(Volume / 2 + 9 * sizeof(int), chunk.EstimatedBytes);
        Assert.True(chunk.EstimatedBytes < Volume * sizeof(ushort) / 3);
    }

    [Fact]
    public void TerrainLikeChunkIsCheaperStill()
    {
        // What an actual surface chunk looks like: stone below, a thin soil band, air
        // above. Four materials, so 2 bits.
        var chunk = new VoxelChunk();

        for (int z = 0; z < VoxelConstants.ChunkSize; z++)
        for (int x = 0; x < VoxelConstants.ChunkSize; x++)
        for (int y = 0; y < VoxelConstants.ChunkSize; y++)
        {
            int material = y switch
            {
                < 12 => 1,  // stone
                < 15 => 2,  // dirt
                15 => 3,    // grass
                _ => MaterialIds.AirNumericId,
            };

            chunk.Set(x, y, z, material);
        }

        Assert.Equal(4, chunk.PaletteSize);
        Assert.Equal(Volume / 4 + 4 * sizeof(int), chunk.EstimatedBytes);
    }

    [Fact]
    public void TheChunksThatDominateAWorldCostNothing()
    {
        // This is what actually makes the memory budget work: the overwhelming majority
        // of chunks are entirely air or entirely stone, and those allocate zero bytes.
        // The palette tier is for the thin shell in between.
        Assert.Equal(0, new VoxelChunk(MaterialIds.AirNumericId).EstimatedBytes);
        Assert.Equal(0, new VoxelChunk(1).EstimatedBytes);
    }

    /// <summary>
    /// The test this whole class is really validated by. Bit-packing bugs are invisible to
    /// inspection and obvious to a differential fuzzer, so random operations run against a
    /// naive <c>ushort[]</c> that cannot be wrong.
    /// </summary>
    [Theory]
    [InlineData(2, 1111)]    // 1 bit
    [InlineData(5, 2222)]    // 2 → 4 bits
    [InlineData(20, 3333)]   // 4 → 8 bits
    [InlineData(300, 4444)]  // forces the direct fallback
    public void DifferentialFuzz_AgainstANaiveReference(int materialPoolSize, int seed)
    {
        var chunk = new VoxelChunk();
        var reference = new ushort[Volume]; // starts all air, exactly like the chunk
        var rng = new Random(seed);

        for (int op = 0; op < 40_000; op++)
        {
            int index = rng.Next(Volume);
            int material = rng.Next(0, materialPoolSize);

            chunk.SetAt(index, material);
            reference[index] = (ushort)material;

            // Probe somewhere unrelated, so a clobbered neighbour surfaces early.
            int probe = rng.Next(Volume);
            Assert.Equal(reference[probe], chunk.GetAt(probe));

            // Compaction must be transparent — it may change representation, never content.
            if (op % 10_000 == 9_999)
            {
                chunk.Compact();
            }
        }

        for (int i = 0; i < Volume; i++)
        {
            Assert.Equal(reference[i], chunk.GetAt(i));
        }
    }

    [Fact]
    public void DifferentialFuzz_ThroughCopyToAsWell()
    {
        var chunk = new VoxelChunk();
        var reference = new ushort[Volume];
        var rng = new Random(555);

        for (int op = 0; op < 10_000; op++)
        {
            int index = rng.Next(Volume);
            int material = rng.Next(0, 40);
            chunk.SetAt(index, material);
            reference[index] = (ushort)material;
        }

        var copied = new int[Volume];
        chunk.CopyTo(copied);

        for (int i = 0; i < Volume; i++)
        {
            Assert.Equal(reference[i], copied[i]);
        }
    }
}
