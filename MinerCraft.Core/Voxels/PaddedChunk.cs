namespace MinerCraft.Core.Voxels;

/// <summary>
/// A chunk's voxels plus a one-voxel border copied from its six neighbours.
/// </summary>
/// <remarks>
/// <para>
/// The border is what lets a mesh job decide whether a face on the chunk boundary is
/// visible without looking anything up outside itself. Once built, the worker owns it
/// outright and touches no shared state.
/// </para>
/// <para>
/// <b>The copy is taken on the worker thread, not the sim thread.</b> M1b originally
/// specified the opposite; M2a reversed it after reading godot_voxel. A mesh job holds
/// references to the chunk and its neighbours and calls <see cref="CopyFrom"/> itself,
/// which keeps a 34³ copy per queued chunk off the thread we most want free, and skips it
/// entirely for jobs cancelled before they run. In C# holding a reference is already
/// enough to keep those chunks alive, so the lifetime half of the problem costs nothing.
/// The mutation half is real and is deferred: there is no concurrent writer until mining
/// at M3, which is the recorded revisit trigger. See docs/M2-PLAN.md.
/// </para>
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

    /// <summary>How many chunks a full neighbourhood holds — the centre plus its 26 neighbours.</summary>
    public const int NeighbourhoodSize = 27;

    /// <summary>
    /// Index into a neighbourhood array for the chunk at offset
    /// (<paramref name="dx"/>, <paramref name="dy"/>, <paramref name="dz"/>), each in -1..1.
    /// The centre chunk is <c>NeighbourIndex(0, 0, 0)</c>.
    /// </summary>
    /// <remarks>Z-major, then X, then Y — the same order as everything else here.</remarks>
    public static int NeighbourIndex(int dx, int dy, int dz) =>
        ((dz + 1) * 3 + (dx + 1)) * 3 + (dy + 1);

    /// <summary>
    /// Fills the interior from the centre chunk and the border from its neighbours, in one
    /// pass. A null entry contributes <paramref name="missingMaterial"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>All 26 neighbours, not just the 6 that share a face.</b> Face visibility alone
    /// only ever reads a border voxel with exactly one coordinate out of range, so the
    /// edges and corners of the border are dead weight today. They stop being dead weight
    /// at M5: vertex AO samples diagonally, and a corner voxel is exactly what it reads.
    /// Filling them costs 392 voxels on top of 6,144 — cheaper than revisiting the gather
    /// and the merge key later, and the merge key already carries the AO channel for the
    /// same reason.
    /// </para>
    /// <para>
    /// <c>&amp; 31</c> rather than a branch: it maps -1 to 31 and 32 to 0, which is exactly
    /// the wrap into the neighbour's local coordinates.
    /// </para>
    /// </remarks>
    public void CopyFrom(ReadOnlySpan<VoxelChunk?> neighbourhood, int missingMaterial)
    {
        if (neighbourhood.Length != NeighbourhoodSize)
        {
            throw new ArgumentException(
                $"Expected {NeighbourhoodSize} chunks, got {neighbourhood.Length}.",
                nameof(neighbourhood));
        }

        const int lo = -1;
        const int hi = VoxelConstants.ChunkSize;

        for (int z = lo; z <= hi; z++)
        {
            int dz = z < 0 ? -1 : z >= VoxelConstants.ChunkSize ? 1 : 0;

            for (int x = lo; x <= hi; x++)
            {
                int dx = x < 0 ? -1 : x >= VoxelConstants.ChunkSize ? 1 : 0;

                for (int y = lo; y <= hi; y++)
                {
                    int dy = y < 0 ? -1 : y >= VoxelConstants.ChunkSize ? 1 : 0;

                    VoxelChunk? source = neighbourhood[NeighbourIndex(dx, dy, dz)];
                    this[x, y, z] = source is null
                        ? missingMaterial
                        : source.Get(x & VoxelConstants.ChunkMask,
                                     y & VoxelConstants.ChunkMask,
                                     z & VoxelConstants.ChunkMask);
                }
            }
        }
    }

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
