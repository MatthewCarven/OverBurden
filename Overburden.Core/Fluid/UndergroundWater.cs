using Overburden.Core.Lighting;
using Overburden.Core.Voxels;

namespace Overburden.Core.Fluid;

/// <summary>
/// How much of Tier B's water is underground — the mine's share of <c>held</c>, which is the
/// number "pumped dry" reads off (M10d-1, decision 2).
/// </summary>
/// <remarks>
/// <para>
/// <b><c>held</c> is every Tier B litre and nothing split it.</b> A mine being pumped, the mounds
/// its outlets build, a tank on a placed floor and a hillside spill are one number on the
/// <c>M8b:</c> line, and the pumps' <c>no water</c> count is the pumps' view rather than the
/// mine's — a wet room with no pump in it reads dry through it. This is the mine's view.
/// </para>
/// <para>
/// <b>A panel is a chunk, and a chunk whose top is below the least seed height over its
/// footprint is underground.</b> The seed surface is the unedited terrain, immutable for a
/// world, so a chunk under it is under the ground whatever was dug; a chunk with any of its
/// footprint's seed surface at or below its top straddles — a hillside beside a hollow — and
/// counts as above, which is the stated cost. The mine at 24–64 m has no straddling panel and a
/// mound on the surface has none below. <see cref="ISurfaceHeights"/> is the one definition of
/// the seed surface; in the game it is <see cref="Streaming.StreamerSeedHeights"/>, one array
/// read a column off the loaded sky columns and the function where a column is not.
/// </para>
/// <para>
/// <b>The footprint's least height is memoised per chunk column and the memo is capped</b>, on
/// the 2026-09-20 rule that a cache keyed by what a periodic sweep asks grows with the asking:
/// 1,024 reads a column once, and the map is cleared whole past <see cref="MaxColumns"/> —
/// a played world's walk is tens of thousands of columns, and rebuilding one costs microseconds.
/// Without the memo the load test's 2,393 panels would be 2.4 M reads a publish, twice a second.
/// </para>
/// </remarks>
public sealed class UndergroundWater
{
    /// <summary>Chunk columns memoised before the map is cleared whole. 16,384 × 16 B is a quarter megabyte.</summary>
    public const int MaxColumns = 16_384;

    private readonly ISurfaceHeights _seed;
    private readonly Dictionary<(int X, int Z), int> _leastSeedY = new();

    public UndergroundWater(ISurfaceHeights seed)
    {
        _seed = seed ?? throw new ArgumentNullException(nameof(seed));
    }

    /// <summary>Columns memoised now.</summary>
    public int MemoisedColumns => _leastSeedY.Count;

    /// <summary>Times the memo was cleared for reaching <see cref="MaxColumns"/>.</summary>
    public int MemoClears { get; private set; }

    /// <summary>Whether every cell of <paramref name="chunk"/> is strictly below the seed surface over its footprint.</summary>
    public bool IsUnderground(ChunkPos chunk)
    {
        int top = (chunk.Y << VoxelConstants.ChunkSizeLog2) + VoxelConstants.ChunkMask;
        return top < LeastSeedY(chunk.X, chunk.Z);
    }

    /// <summary>Millilitres and panels of <paramref name="grid"/> wholly under the seed surface.</summary>
    public (long Millilitres, int Panels) Measure(FluidGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        long millilitres = 0;
        int panels = 0;
        foreach ((ChunkPos chunk, long total) in grid.PanelTotals())
        {
            if (IsUnderground(chunk))
            {
                millilitres += total;
                panels++;
            }
        }

        return (millilitres, panels);
    }

    private int LeastSeedY(int chunkX, int chunkZ)
    {
        if (_leastSeedY.TryGetValue((chunkX, chunkZ), out int least))
        {
            return least;
        }

        if (_leastSeedY.Count >= MaxColumns)
        {
            _leastSeedY.Clear();
            MemoClears++;
        }

        least = int.MaxValue;
        int baseX = chunkX << VoxelConstants.ChunkSizeLog2;
        int baseZ = chunkZ << VoxelConstants.ChunkSizeLog2;
        for (int z = 0; z < VoxelConstants.ChunkSize; z++)
        {
            for (int x = 0; x < VoxelConstants.ChunkSize; x++)
            {
                int y = _seed.SurfaceHeightAt(baseX + x, baseZ + z);
                if (y < least)
                {
                    least = y;
                }
            }
        }

        _leastSeedY[(chunkX, chunkZ)] = least;
        return least;
    }
}
