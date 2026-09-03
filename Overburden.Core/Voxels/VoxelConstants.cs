namespace Overburden.Core.Voxels;

/// <summary>
/// The numbers everything else is derived from. See docs/DESIGN.md — voxel scale and
/// chunk size are locked decisions, not tuning knobs, because storage layout and the
/// mesher both bake them in.
/// </summary>
public static class VoxelConstants
{
    /// <summary>Voxels per metre along one axis. 4 → 0.25 m voxels.</summary>
    public const int VoxelsPerMetre = 4;

    /// <summary>Edge length of one voxel, in metres.</summary>
    public const float VoxelSize = 1.0f / VoxelsPerMetre;

    /// <summary>
    /// Voxels per chunk edge. Must stay a power of two: chunk/local decomposition uses
    /// shift and mask, which is what makes it correct for negative coordinates without
    /// a branch. <see cref="ChunkSizeLog2"/> and <see cref="ChunkMask"/> must agree.
    /// </summary>
    public const int ChunkSize = 32;

    /// <summary>log2(<see cref="ChunkSize"/>). Used for floor division by shifting.</summary>
    public const int ChunkSizeLog2 = 5;

    /// <summary>ChunkSize - 1. Used for the positive remainder by masking.</summary>
    public const int ChunkMask = ChunkSize - 1;

    /// <summary>Edge length of one chunk, in metres. 32 × 0.25 m = 8 m.</summary>
    public const float ChunkSizeMetres = ChunkSize * VoxelSize;

    /// <summary>Voxels in one chunk. 32³ = 32,768.</summary>
    public const int ChunkVolume = ChunkSize * ChunkSize * ChunkSize;
}
