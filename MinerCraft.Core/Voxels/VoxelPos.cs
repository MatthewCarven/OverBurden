namespace MinerCraft.Core.Voxels;

/// <summary>
/// A voxel in global voxel coordinates — unbounded, and negative on three sides of the
/// origin. One unit is <see cref="VoxelConstants.VoxelSize"/> metres.
/// </summary>
/// <remarks>
/// Every conversion here is floor-based, never truncation-based. Truncating division
/// puts voxel -1 in chunk 0 alongside voxel 0, which produces a seam one chunk wide
/// through the origin that is miserable to diagnose later. Because
/// <see cref="VoxelConstants.ChunkSize"/> is a power of two, the correct floor division
/// is an arithmetic right shift and the correct positive remainder is a mask — both
/// branch-free and both already right for negatives.
/// </remarks>
public readonly record struct VoxelPos(int X, int Y, int Z)
{
    public static readonly VoxelPos Zero = new(0, 0, 0);

    /// <summary>The chunk containing this voxel.</summary>
    public ChunkPos Chunk => new(
        X >> VoxelConstants.ChunkSizeLog2,
        Y >> VoxelConstants.ChunkSizeLog2,
        Z >> VoxelConstants.ChunkSizeLog2);

    /// <summary>
    /// Coordinates within the containing chunk, always 0..<see cref="VoxelConstants.ChunkMask"/>.
    /// </summary>
    public (int X, int Y, int Z) Local => (
        X & VoxelConstants.ChunkMask,
        Y & VoxelConstants.ChunkMask,
        Z & VoxelConstants.ChunkMask);

    /// <summary>Index of this voxel in its chunk's flat storage array.</summary>
    public int LocalIndex
    {
        get
        {
            var (lx, ly, lz) = Local;
            return Index(lx, ly, lz);
        }
    }

    /// <summary>
    /// Flat storage order for a chunk: Y-major, then Z, then X. X is contiguous so the
    /// mesher can sweep a row along X with sequential reads, and Y-major keeps a
    /// horizontal slab together for the lighting column pass.
    /// </summary>
    public static int Index(int localX, int localY, int localZ) =>
        (localY << (VoxelConstants.ChunkSizeLog2 * 2))
        | (localZ << VoxelConstants.ChunkSizeLog2)
        | localX;

    /// <summary>Inverse of <see cref="Index"/>.</summary>
    public static (int X, int Y, int Z) FromIndex(int index) => (
        index & VoxelConstants.ChunkMask,
        index >> (VoxelConstants.ChunkSizeLog2 * 2),
        (index >> VoxelConstants.ChunkSizeLog2) & VoxelConstants.ChunkMask);

    /// <summary>Rebuild a global position from a chunk and coordinates inside it.</summary>
    public static VoxelPos FromChunkLocal(ChunkPos chunk, int localX, int localY, int localZ)
    {
        var origin = chunk.OriginVoxel;
        return new VoxelPos(origin.X + localX, origin.Y + localY, origin.Z + localZ);
    }

    /// <summary>
    /// The voxel containing a world-space point, in metres. Floors, so points with a
    /// negative coordinate land in the voxel below rather than snapping toward zero.
    /// </summary>
    public static VoxelPos FromMetres(float x, float y, float z) => new(
        (int)MathF.Floor(x * VoxelConstants.VoxelsPerMetre),
        (int)MathF.Floor(y * VoxelConstants.VoxelsPerMetre),
        (int)MathF.Floor(z * VoxelConstants.VoxelsPerMetre));

    /// <summary>This voxel's minimum corner in world metres.</summary>
    public (float X, float Y, float Z) MinCornerMetres => (
        X * VoxelConstants.VoxelSize,
        Y * VoxelConstants.VoxelSize,
        Z * VoxelConstants.VoxelSize);

    /// <summary>This voxel's centre in world metres.</summary>
    public (float X, float Y, float Z) CentreMetres => (
        (X + 0.5f) * VoxelConstants.VoxelSize,
        (Y + 0.5f) * VoxelConstants.VoxelSize,
        (Z + 0.5f) * VoxelConstants.VoxelSize);

    public VoxelPos Offset(int dx, int dy, int dz) => new(X + dx, Y + dy, Z + dz);

    public override string ToString() => $"Voxel({X}, {Y}, {Z})";
}
