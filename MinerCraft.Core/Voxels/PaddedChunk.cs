namespace MinerCraft.Core.Voxels;

/// <summary>
/// A chunk's voxels plus a one-voxel border copied from its six neighbours.
/// </summary>
/// <remarks>
/// The border is what lets a mesh job decide whether a face on the chunk boundary is
/// visible without looking anything up outside itself. That means mesh jobs touch no
/// shared state and need no locking — the copy is taken on the sim thread when the job
/// is queued, and after that the worker owns it outright.
/// <para>
/// Coordinates run -1..32 on each axis: -1 and 32 are the borrowed border, 0..31 is the
/// chunk proper.
/// </para>
/// </remarks>
public sealed class PaddedChunk
{
    /// <summary>34 — the chunk plus one voxel of border on each side.</summary>
    public const int Size = VoxelConstants.ChunkSize + 2;

    public const int Volume = Size * Size * Size;

    private readonly int[] _voxels = new int[Volume];

    /// <summary>Same Z-major, X, Y-contiguous order as <see cref="VoxelPos.Index"/>.</summary>
    public static int Index(int x, int y, int z) =>
        ((z + 1) * Size + (x + 1)) * Size + (y + 1);

    public int this[int x, int y, int z]
    {
        get => _voxels[Index(x, y, z)];
        set => _voxels[Index(x, y, z)] = value;
    }

    /// <summary>Fills the 32³ interior, leaving the border untouched.</summary>
    public void CopyInteriorFrom(VoxelChunk chunk)
    {
        for (int z = 0; z < VoxelConstants.ChunkSize; z++)
        for (int x = 0; x < VoxelConstants.ChunkSize; x++)
        for (int y = 0; y < VoxelConstants.ChunkSize; y++)
        {
            this[x, y, z] = chunk.Get(x, y, z);
        }
    }

    /// <summary>Sets every voxel, border included.</summary>
    public void Fill(int materialId) => Array.Fill(_voxels, materialId);

    /// <summary>
    /// Fills only the border, for the common case of a chunk whose neighbours are all the
    /// same thing — open sky, or solid rock.
    /// </summary>
    public void FillBorder(int materialId)
    {
        const int lo = -1;
        const int hi = VoxelConstants.ChunkSize;

        for (int z = lo; z <= hi; z++)
        for (int x = lo; x <= hi; x++)
        for (int y = lo; y <= hi; y++)
        {
            if (x is > lo and < hi && y is > lo and < hi && z is > lo and < hi) continue;
            this[x, y, z] = materialId;
        }
    }
}
