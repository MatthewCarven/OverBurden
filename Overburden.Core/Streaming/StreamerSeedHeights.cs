using Overburden.Core.Lighting;
using Overburden.Core.Voxels;

namespace Overburden.Core.Streaming;

/// <summary>
/// The unedited surface as the loaded sky columns hold it — one array read — falling back to the
/// analytic function for a column that is not loaded.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists is a measured number.</b> The portal index reads five seed surfaces per
/// edited voxel, and a chunk bite is 32,768 edited voxels: with the analytic function that is
/// 10.3 ms a bite in Release, with the columns' arrays 1.2 ms (`--portal`, 2026-09-05). The
/// columns compute the same value at <see cref="SkyColumn.Seed"/> and keep it for this.
/// </para>
/// <para>
/// <b>The two sources are one definition.</b> A column's seed surface is
/// <see cref="ISurfaceHeights.SurfaceHeightAt"/> evaluated at seed time, so the fallback and the
/// array agree bit for bit and which one answers is a cost question only. A column that exists
/// but was never seeded cannot happen for a live column — the streamer seeds on creation — and
/// would throw rather than answer, which is the right failure for a plane every portal test
/// would believe.
/// </para>
/// </remarks>
public sealed class StreamerSeedHeights(ChunkStreamer streamer, ISurfaceHeights fallback) : ISurfaceHeights
{
    private readonly ChunkStreamer _streamer = streamer ?? throw new ArgumentNullException(nameof(streamer));
    private readonly ISurfaceHeights _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));

    /// <summary>Answers served from a loaded column rather than the function.</summary>
    public long ColumnReads { get; private set; }

    /// <summary>Answers that had to evaluate the function.</summary>
    public long FallbackReads { get; private set; }

    public int SurfaceHeightAt(int worldX, int worldZ)
    {
        SkyColumn? column = _streamer.ColumnAt(
            worldX >> VoxelConstants.ChunkSizeLog2, worldZ >> VoxelConstants.ChunkSizeLog2);
        if (column is { IsSeeded: true })
        {
            ColumnReads++;
            return column.SeedSurfaceY(worldX & VoxelConstants.ChunkMask, worldZ & VoxelConstants.ChunkMask);
        }

        FallbackReads++;
        return _fallback.SurfaceHeightAt(worldX, worldZ);
    }
}
