using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// These tests exist almost entirely to pin down negative-coordinate behaviour. Every
/// one of them passes trivially for positive coordinates; the whole point is the other
/// three octants, where truncating division silently does the wrong thing.
/// </summary>
public class VoxelPosTests
{
    [Theory]
    // Positive side: chunk 0 holds voxels 0..31, chunk 1 holds 32..63.
    [InlineData(0, 0)]
    [InlineData(31, 0)]
    [InlineData(32, 1)]
    [InlineData(63, 1)]
    [InlineData(64, 2)]
    // Negative side: chunk -1 holds voxels -32..-1. Truncating division would put
    // -1 in chunk 0 and -32 in chunk -1, splitting one chunk across two indices.
    [InlineData(-1, -1)]
    [InlineData(-32, -1)]
    [InlineData(-33, -2)]
    [InlineData(-64, -2)]
    [InlineData(-65, -3)]
    public void ChunkForVoxel_FloorsTowardNegativeInfinity(int voxel, int expectedChunk)
    {
        Assert.Equal(expectedChunk, new VoxelPos(voxel, 0, 0).Chunk.X);
        Assert.Equal(expectedChunk, new VoxelPos(0, voxel, 0).Chunk.Y);
        Assert.Equal(expectedChunk, new VoxelPos(0, 0, voxel).Chunk.Z);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(31, 31)]
    [InlineData(32, 0)]
    // A positive remainder is required, so -1 is local 31, not -1.
    [InlineData(-1, 31)]
    [InlineData(-32, 0)]
    [InlineData(-33, 31)]
    public void LocalCoordinate_IsAlwaysInRange(int voxel, int expectedLocal)
    {
        Assert.Equal(expectedLocal, new VoxelPos(voxel, 0, 0).Local.X);
    }

    [Theory]
    [InlineData(-100)]
    [InlineData(-33)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(31)]
    [InlineData(4095)]
    public void LocalCoordinate_IsNeverOutsideTheChunk(int coordinate)
    {
        var (lx, ly, lz) = new VoxelPos(coordinate, coordinate, coordinate).Local;
        Assert.InRange(lx, 0, VoxelConstants.ChunkMask);
        Assert.InRange(ly, 0, VoxelConstants.ChunkMask);
        Assert.InRange(lz, 0, VoxelConstants.ChunkMask);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(5, 9, 17)]
    [InlineData(-70, 3, -1)]
    [InlineData(int.MinValue / 2, -12345, 987654)]
    public void ChunkAndLocal_RoundTripToTheOriginalVoxel(int x, int y, int z)
    {
        var original = new VoxelPos(x, y, z);
        var (lx, ly, lz) = original.Local;

        var rebuilt = VoxelPos.FromChunkLocal(original.Chunk, lx, ly, lz);

        Assert.Equal(original, rebuilt);
    }

    [Fact]
    public void ChunkOriginVoxel_IsLocalZeroOfThatChunk()
    {
        foreach (var chunk in new[] { new ChunkPos(0, 0, 0), new ChunkPos(3, -2, 7), new ChunkPos(-1, -1, -1) })
        {
            var origin = chunk.OriginVoxel;
            Assert.Equal(chunk, origin.Chunk);
            Assert.Equal((0, 0, 0), origin.Local);
        }
    }

    [Fact]
    public void Index_RoundTripsForEveryVoxelInAChunk()
    {
        var seen = new bool[VoxelConstants.ChunkVolume];

        for (int y = 0; y < VoxelConstants.ChunkSize; y++)
        for (int z = 0; z < VoxelConstants.ChunkSize; z++)
        for (int x = 0; x < VoxelConstants.ChunkSize; x++)
        {
            int index = VoxelPos.Index(x, y, z);

            Assert.InRange(index, 0, VoxelConstants.ChunkVolume - 1);
            Assert.False(seen[index], $"index {index} produced twice, at ({x},{y},{z})");
            seen[index] = true;

            Assert.Equal((x, y, z), VoxelPos.FromIndex(index));
        }

        // Every slot filled exactly once — the mapping is a bijection, so no voxel is
        // unreachable and none aliases another.
        Assert.DoesNotContain(false, seen);
    }

    [Fact]
    public void Index_IsContiguousAlongY()
    {
        // Y is the fast axis on purpose: skylight, falling water, support propagation and
        // groundwater drainage all walk columns, and they are the hot paths that care.
        for (int y = 0; y < VoxelConstants.ChunkSize - 1; y++)
        {
            Assert.Equal(VoxelPos.Index(4, y, 9) + 1, VoxelPos.Index(4, y + 1, 9));
        }
    }

    [Fact]
    public void Index_StridesAreZxy()
    {
        // Pins the exact layout, so a well-meaning "optimisation" that reorders the axes
        // has to come here and argue with the reasoning first.
        Assert.Equal(1, VoxelPos.Index(0, 1, 0) - VoxelPos.Index(0, 0, 0));
        Assert.Equal(VoxelConstants.ChunkSize, VoxelPos.Index(1, 0, 0) - VoxelPos.Index(0, 0, 0));
        Assert.Equal(
            VoxelConstants.ChunkSize * VoxelConstants.ChunkSize,
            VoxelPos.Index(0, 0, 1) - VoxelPos.Index(0, 0, 0));
    }

    [Theory]
    [InlineData(0.0f, 0)]
    [InlineData(0.1f, 0)]
    [InlineData(0.25f, 1)]
    [InlineData(0.9f, 3)]
    [InlineData(1.0f, 4)]
    // Below the origin must floor, not truncate toward zero: -0.1 m is in voxel -1.
    [InlineData(-0.1f, -1)]
    [InlineData(-0.25f, -1)]
    [InlineData(-0.26f, -2)]
    [InlineData(-1.0f, -4)]
    public void FromMetres_Floors(float metres, int expectedVoxel)
    {
        Assert.Equal(expectedVoxel, VoxelPos.FromMetres(metres, 0, 0).X);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(7, 13, -5)]
    [InlineData(-129, 400, -1)]
    public void VoxelCentre_MapsBackToTheSameVoxel(int x, int y, int z)
    {
        var voxel = new VoxelPos(x, y, z);
        var (cx, cy, cz) = voxel.CentreMetres;

        Assert.Equal(voxel, VoxelPos.FromMetres(cx, cy, cz));
    }

    [Fact]
    public void VoxelSize_MatchesTheDesignDecision()
    {
        // Guard against someone "tuning" the scale without reading docs/DESIGN.md.
        Assert.Equal(0.25f, VoxelConstants.VoxelSize);
        Assert.Equal(32, VoxelConstants.ChunkSize);
        Assert.Equal(8.0f, VoxelConstants.ChunkSizeMetres);
        Assert.Equal(32_768, VoxelConstants.ChunkVolume);

        // ChunkSize must be a power of two, and the shift and mask must agree with it,
        // because the chunk/local split relies on all three lining up.
        Assert.Equal(0, VoxelConstants.ChunkSize & VoxelConstants.ChunkMask);
        Assert.Equal(VoxelConstants.ChunkSize, 1 << VoxelConstants.ChunkSizeLog2);
        Assert.Equal(VoxelConstants.ChunkSize - 1, VoxelConstants.ChunkMask);
    }
}
