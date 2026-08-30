using MinerCraft.Core.Lighting;
using MinerCraft.Core.Voxels;

namespace MinerCraft.Core.Streaming;

/// <summary>
/// Which chunks a single voxel edit — or a single light cell moving — invalidates the mesh of.
/// </summary>
/// <remarks>
/// Its own class, and a pure function, because the rule is wider than it first looks and the
/// symptom of getting it wrong is subtle: a strip of stale geometry along a chunk seam, which
/// only appears when you mine near one — and mining near a seam is most mining.
/// </remarks>
public static class ChunkEdits
{
    /// <summary>The most chunks one voxel can dirty: its own, plus seven at a corner.</summary>
    public const int MaxDirtyChunks = 8;

    /// <summary>
    /// Writes the chunks whose mesh is invalidated by editing <paramref name="voxel"/> into
    /// <paramref name="into"/>, and returns how many there were.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A mesh is built from a 34³ padded copy that reaches one voxel into each neighbour, so
    /// a voxel on a chunk boundary is part of the neighbour's mesh as well as its own.
    /// Editing local <c>x == 0</c> therefore dirties the chunk at <c>-1</c> on X, local
    /// <c>x == 31</c> the chunk at <c>+1</c>, and a voxel in a corner dirties seven
    /// neighbours at once.
    /// </para>
    /// <para>
    /// Returned by span rather than by <c>IEnumerable</c> or a callback: a sphere carve calls
    /// this once per voxel, and neither an allocation nor a delegate invocation belongs in
    /// that loop.
    /// </para>
    /// </remarks>
    public static int DirtyChunks(VoxelPos voxel, Span<ChunkPos> into)
    {
        if (into.Length < MaxDirtyChunks)
        {
            throw new ArgumentException(
                $"Needs room for {MaxDirtyChunks} chunks, got {into.Length}.", nameof(into));
        }

        ChunkPos owner = voxel.Chunk;
        (int localX, int localY, int localZ) = voxel.Local;

        // 0 when the voxel is not against that face, otherwise the direction of the
        // neighbour it also belongs to. A voxel can be against at most one face per axis.
        int stepX = Step(localX, VoxelConstants.ChunkMask);
        int stepY = Step(localY, VoxelConstants.ChunkMask);
        int stepZ = Step(localZ, VoxelConstants.ChunkMask);

        return Spread(owner, stepX, stepY, stepZ, into);
    }

    /// <summary>
    /// Writes the chunks whose mesh is invalidated by the light in <paramref name="cell"/>
    /// changing into <paramref name="into"/>, and returns how many there were.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Exactly the voxel rule one resolution coarser, and for the identical reason.</b> A mesh
    /// samples light from a padded 10³ grid reaching one <em>cell</em> into each neighbour, so a
    /// cell on a chunk boundary is part of the neighbour's mesh as well as its own — and a cell
    /// in a corner is part of seven neighbours' meshes at once.
    /// </para>
    /// <para>
    /// <b>Marking only the owning chunk is the break-recipe, and the symptom is a light seam on
    /// the chunk grid</b> — the analogue of the stale strip the voxel rule exists to prevent, and
    /// the reason M3a's <c>Neighbourhood()</c> marks all 27.
    /// </para>
    /// <para>
    /// Shares <see cref="Spread"/> with the voxel rule rather than restating it. The two differ
    /// only in the mask that says what "against a face" means — 31 for voxels, 7 for cells — and
    /// two copies of this loop is two copies that can disagree about the corner case.
    /// </para>
    /// </remarks>
    public static int DirtyChunks(LightCellPos cell, Span<ChunkPos> into)
    {
        if (into.Length < MaxDirtyChunks)
        {
            throw new ArgumentException(
                $"Needs room for {MaxDirtyChunks} chunks, got {into.Length}.", nameof(into));
        }

        (int localX, int localY, int localZ) = cell.Local;

        return Spread(
            cell.Chunk,
            Step(localX, LightCellPos.CellMask),
            Step(localY, LightCellPos.CellMask),
            Step(localZ, LightCellPos.CellMask),
            into);
    }

    /// <summary>
    /// The owner plus every combination of the axes it is against — one, two or seven extras.
    /// </summary>
    private static int Spread(
        ChunkPos owner, int stepX, int stepY, int stepZ, Span<ChunkPos> into)
    {
        int count = 0;
        for (int dz = 0; dz <= 1; dz++)
        {
            if (dz == 1 && stepZ == 0)
            {
                continue;
            }

            for (int dy = 0; dy <= 1; dy++)
            {
                if (dy == 1 && stepY == 0)
                {
                    continue;
                }

                for (int dx = 0; dx <= 1; dx++)
                {
                    if (dx == 1 && stepX == 0)
                    {
                        continue;
                    }

                    into[count++] = owner.Offset(dx * stepX, dy * stepY, dz * stepZ);
                }
            }
        }

        return count;
    }

    /// <param name="mask">
    /// The largest local coordinate on the axis — <see cref="VoxelConstants.ChunkMask"/> for
    /// voxels, <see cref="LightCellPos.CellMask"/> for light cells.
    /// </param>
    private static int Step(int local, int mask) =>
        local == 0 ? -1
        : local == mask ? 1
        : 0;
}
