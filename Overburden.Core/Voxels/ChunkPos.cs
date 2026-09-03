namespace Overburden.Core.Voxels;

/// <summary>
/// Which chunk, in chunk units. Chunk (0,0,0) covers voxels 0..31 on each axis;
/// chunk (-1,0,0) covers voxels -32..-1 on X.
/// </summary>
public readonly record struct ChunkPos(int X, int Y, int Z)
{
    public static readonly ChunkPos Zero = new(0, 0, 0);

    /// <summary>The lowest-numbered voxel this chunk contains — its origin corner.</summary>
    public VoxelPos OriginVoxel => new(
        X << VoxelConstants.ChunkSizeLog2,
        Y << VoxelConstants.ChunkSizeLog2,
        Z << VoxelConstants.ChunkSizeLog2);

    /// <summary>Position of this chunk's origin corner in world metres.</summary>
    public (float X, float Y, float Z) OriginMetres => (
        X * VoxelConstants.ChunkSizeMetres,
        Y * VoxelConstants.ChunkSizeMetres,
        Z * VoxelConstants.ChunkSizeMetres);

    public ChunkPos Offset(int dx, int dy, int dz) => new(X + dx, Y + dy, Z + dz);

    /// <summary>
    /// Chebyshev distance in chunks. This is the right metric for "is this chunk inside
    /// the render distance", because the loaded region is a box, not a sphere.
    /// </summary>
    public int ChebyshevDistanceTo(ChunkPos other)
    {
        int dx = Math.Abs(X - other.X);
        int dy = Math.Abs(Y - other.Y);
        int dz = Math.Abs(Z - other.Z);
        return Math.Max(dx, Math.Max(dy, dz));
    }

    public override string ToString() => $"Chunk({X}, {Y}, {Z})";
}
