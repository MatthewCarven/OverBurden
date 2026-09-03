using Overburden.Core.Lighting;
using Overburden.Core.Streaming;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

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

    // ------------------------------------------------------------------ light cells, M5c

    /// <summary>
    /// The same rule one resolution coarser: a light cell on a chunk boundary is sampled by the
    /// neighbour's mesh too, because the padded light grid reaches one cell past each face.
    /// </summary>
    /// <remarks>
    /// <b>The interesting difference is the mask, and it is the whole reason these share a
    /// helper.</b> A chunk is 32 voxels and 8 cells, so "against a face" is local 0 or 31 for a
    /// voxel and local 0 or 7 for a cell. Two copies of this loop would be two chances to write
    /// 31 where 7 belongs — which under-marks by a factor of four and shows up as a light seam
    /// on the chunk grid, in exactly the places a player is most likely to be mining.
    /// </remarks>
    [Fact]
    public void ALightCellInTheInteriorDirtiesOnlyItsOwnChunk()
    {
        var owner = new ChunkPos(2, -3, 4);

        Assert.Equal(
            [owner],
            Dirty(LightCellPos.FromChunkLocal(owner, 3, 4, 5)));
    }

    [Fact]
    public void ALightCellOnAFaceDirtiesTheNeighbourThroughThatFace()
    {
        var owner = new ChunkPos(0, 0, 0);
        const int last = LightCellPos.CellsPerChunk - 1;

        Assert.Equal(
            [owner, new ChunkPos(0, 0, -1)],
            Dirty(LightCellPos.FromChunkLocal(owner, 4, 4, 0)));

        Assert.Equal(
            [owner, new ChunkPos(1, 0, 0)],
            Dirty(LightCellPos.FromChunkLocal(owner, last, 4, 4)));
    }

    [Fact]
    public void ALightCellInACornerDirtiesEightChunks()
    {
        var owner = new ChunkPos(-1, 5, 2);
        const int last = LightCellPos.CellsPerChunk - 1;

        ChunkPos[] dirty = Dirty(LightCellPos.FromChunkLocal(owner, 0, last, 0));

        Assert.Equal(8, dirty.Length);
        Assert.Equal(8, dirty.Distinct().Count());
        Assert.Contains(owner, dirty);
        Assert.Contains(owner.Offset(-1, 1, -1), dirty);
    }

    /// <summary>
    /// Every cell of a chunk, against a brute-force statement of which padded grids cover it.
    /// </summary>
    /// <remarks>
    /// The reference asks the question the other way round — for each of the 27 candidate chunks,
    /// does its −1…8 sample window contain this cell — so it shares no arithmetic with the
    /// subject's "which faces is it against" formulation.
    /// </remarks>
    [Fact]
    public void EveryLightCellAgreesWithABruteForceSweepOfTheCandidateChunks()
    {
        var owner = new ChunkPos(3, -2, 7);

        for (int cz = 0; cz < LightCellPos.CellsPerChunk; cz++)
        for (int cx = 0; cx < LightCellPos.CellsPerChunk; cx++)
        for (int cy = 0; cy < LightCellPos.CellsPerChunk; cy++)
        {
            LightCellPos cell = LightCellPos.FromChunkLocal(owner, cx, cy, cz);

            var expected = new HashSet<ChunkPos>();
            for (int dz = -1; dz <= 1; dz++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                ChunkPos candidate = owner.Offset(dx, dy, dz);
                LightCellPos origin = LightCellPos.FromChunkLocal(candidate, 0, 0, 0);

                if (Covers(origin.X, cell.X) && Covers(origin.Y, cell.Y) && Covers(origin.Z, cell.Z))
                {
                    expected.Add(candidate);
                }
            }

            Assert.Equal(expected, Dirty(cell).ToHashSet());
        }

        static bool Covers(int origin, int coordinate) =>
            coordinate >= origin - 1 && coordinate <= origin + LightCellPos.CellsPerChunk;
    }

    private static ChunkPos[] Dirty(VoxelPos voxel)
    {
        Span<ChunkPos> buffer = stackalloc ChunkPos[ChunkEdits.MaxDirtyChunks];
        int count = ChunkEdits.DirtyChunks(voxel, buffer);
        return buffer[..count].ToArray();
    }

    private static ChunkPos[] Dirty(LightCellPos cell)
    {
        Span<ChunkPos> buffer = stackalloc ChunkPos[ChunkEdits.MaxDirtyChunks];
        int count = ChunkEdits.DirtyChunks(cell, buffer);
        return buffer[..count].ToArray();
    }
}
