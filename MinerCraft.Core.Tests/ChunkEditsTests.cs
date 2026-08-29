using MinerCraft.Core.Streaming;
using MinerCraft.Core.Voxels;

namespace MinerCraft.Core.Tests;

/// <summary>
/// Which chunks one voxel edit makes stale. Pure, so it is tested without a pipeline, a
/// thread or a mesher anywhere near it.
/// </summary>
public class ChunkEditsTests
{
    private const int Last = VoxelConstants.ChunkSize - 1;

    [Fact]
    public void AVoxelInTheInteriorDirtiesOnlyItsOwnChunk()
    {
        Assert.Equal(
            [new ChunkPos(2, -3, 4)],
            Dirty(VoxelPos.FromChunkLocal(new ChunkPos(2, -3, 4), 5, 17, 28)));
    }

    [Fact]
    public void AVoxelOnAFaceDirtiesTheNeighbourThroughThatFace()
    {
        var owner = new ChunkPos(0, 0, 0);

        Assert.Equal(
            [owner, new ChunkPos(-1, 0, 0)],
            Dirty(VoxelPos.FromChunkLocal(owner, 0, 10, 10)));

        Assert.Equal(
            [owner, new ChunkPos(0, 1, 0)],
            Dirty(VoxelPos.FromChunkLocal(owner, 10, Last, 10)));
    }

    [Fact]
    public void AVoxelInACornerDirtiesEightChunks()
    {
        // The case that is easy to get wrong by handling axes independently and stopping at
        // three neighbours. The mesher's padded copy reaches diagonally too, so the chunk
        // sharing only a corner with this voxel reads it as well.
        var owner = new ChunkPos(5, 5, 5);
        ChunkPos[] dirty = Dirty(VoxelPos.FromChunkLocal(owner, 0, 0, 0));

        Assert.Equal(ChunkEdits.MaxDirtyChunks, dirty.Length);
        Assert.Contains(new ChunkPos(4, 4, 4), dirty);
        Assert.Contains(new ChunkPos(5, 5, 5), dirty);
        Assert.Contains(new ChunkPos(4, 5, 4), dirty);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(3, -7, 11)]
    [InlineData(-1, -1, -1)]
    [InlineData(-9, 4, -2)]
    public void TheDirtySetIsExactlyTheChunksWhosePaddedRegionContainsTheVoxel(int cx, int cy, int cz)
    {
        // Fuzzed against the definition rather than against a second implementation of the
        // same trick: a chunk's mesh is built from voxels -1..32 of its own space, so the
        // chunks made stale by an edit are precisely those whose padded region covers it.
        // Negative chunk coordinates are in the cases on purpose — that is where a floor or
        // a sign error hides.
        var chunk = new ChunkPos(cx, cy, cz);

        for (int lz = 0; lz < VoxelConstants.ChunkSize; lz += 7)
        for (int ly = 0; ly < VoxelConstants.ChunkSize; ly += 5)
        for (int lx = 0; lx < VoxelConstants.ChunkSize; lx += 3)
        {
            // Sweep includes 0 and, via the explicit additions, the far face too.
            foreach (int x in new[] { lx, Last })
            foreach (int y in new[] { ly, Last })
            foreach (int z in new[] { lz, Last })
            {
                VoxelPos voxel = VoxelPos.FromChunkLocal(chunk, x, y, z);
                Assert.Equal(BruteForce(voxel), Dirty(voxel).ToHashSet());
            }
        }
    }

    /// <summary>Every chunk within one step whose 34³ padded region contains the voxel.</summary>
    private static HashSet<ChunkPos> BruteForce(VoxelPos voxel)
    {
        var dirty = new HashSet<ChunkPos>();

        for (int dz = -1; dz <= 1; dz++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            ChunkPos candidate = voxel.Chunk.Offset(dx, dy, dz);
            VoxelPos origin = candidate.OriginVoxel;

            if (Covers(origin.X, voxel.X) && Covers(origin.Y, voxel.Y) && Covers(origin.Z, voxel.Z))
            {
                dirty.Add(candidate);
            }
        }

        return dirty;

        static bool Covers(int origin, int coordinate) =>
            coordinate >= origin - 1 && coordinate <= origin + VoxelConstants.ChunkSize;
    }

    private static ChunkPos[] Dirty(VoxelPos voxel)
    {
        Span<ChunkPos> buffer = stackalloc ChunkPos[ChunkEdits.MaxDirtyChunks];
        int count = ChunkEdits.DirtyChunks(voxel, buffer);
        return buffer[..count].ToArray();
    }
}
