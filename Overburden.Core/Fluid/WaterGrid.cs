using Overburden.Core.Lighting;
using Overburden.Core.Voxels;

namespace Overburden.Core.Fluid;

/// <summary>
/// Matthew's water grid (docs/POND-PLAN.md, decision 10): per voxel column of one chunk column,
/// the level a Tier A lake stands at there — a <b>water column</b> — or none.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why.</b> Tier A answers a lake per 2 m sample, and a sample is also where the lake is drawn
/// and where <see cref="LakeCeiling"/> bands it. Where the player has built, a sample can be half
/// wall: the lake either stops a cell short of the wall or shows a cell past it. The grid answers
/// those cells per voxel column, so the shore is the wall on both sides, whatever its thickness.
/// It is derived, not simulated: Tier A keeps one level per basin, and the grid only says where
/// that level stands.
/// </para>
/// <para>
/// <b>Only where built.</b> A cell with no placed column (<see cref="Hydrology.RainOnPlaced"/>'s
/// predicate) answers today's band exactly: its sample's table, the level of the basin the sample
/// drains to. A chunk column with no placed column anywhere builds no grid at all
/// (<see cref="Build"/> returns null), so every chunk column nobody has built in costs one scan.
/// </para>
/// <para>
/// <b>How a built-in cell is answered: a flood.</b> The unraised columns of the padded window —
/// the chunk column and one cell around it — whose floor is under their table are the seeds,
/// each carrying its level. The flood goes 4-neighbour into built-in columns only, entering one
/// whose floor (its placed top where it has one, else the seed plane) is strictly under the
/// level. So a wall at or above the level stops it, a gap at the water line lets it through (the
/// water would go there too), and a block under the water has water over it. <b>Levels flood in
/// ascending order and the first to reach a column keeps it</b>, so two lakes meeting in a
/// built-in cell take the lower: the 2 m analysis could not see them join, and the lower cannot
/// draw water above where it can stand.
/// </para>
/// <para>
/// <b>Strict at the wall, as the surface is.</b> The band's rule for an unraised column is
/// <c>seed &lt; Y ≤ floor(level)</c>, which puts a voxel of lake over a rim exactly at a full
/// lake's level. The flood enters only under the level, the test <c>WaterSurface.IsWet</c> makes,
/// because a full pond's level is its wall's top exactly and the band's rule would carry it over
/// the wall.
/// </para>
/// <para>
/// <b>Stated costs.</b> A neighbouring chunk column that is not loaded contributes nothing to the
/// pad: no seeds and no passage. A built-in zone wider than the pad, with no unraised wet column
/// within a cell of the chunk column, finds no seeds and reads dry. Both are the conservative
/// failure — water missing, never water outside a wall.
/// </para>
/// </remarks>
public static class WaterGrid
{
    private const int N = VoxelConstants.ChunkSize;

    /// <summary>
    /// Builds the water columns of chunk column (<paramref name="chunkX"/>, <paramref name="chunkZ"/>),
    /// or returns null when nothing is placed in it — its bands are today's and no grid is needed.
    /// </summary>
    public static WaterColumns? Build(int chunkX, int chunkZ, ISkyColumns sky, IWaterTable table, int cellVoxels)
    {
        ArgumentNullException.ThrowIfNull(sky);
        ArgumentNullException.ThrowIfNull(table);
        if (cellVoxels <= 0 || N % cellVoxels != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cellVoxels), cellVoxels, "A cell must divide a chunk's width.");
        }

        SkyColumn? centre = sky.ColumnAt(chunkX, chunkZ);
        if (centre is null || !centre.IsSeeded || !AnyPlaced(centre))
        {
            return null;
        }

        int pad = cellVoxels;
        int side = N + (2 * pad);
        int originX = (chunkX * N) - pad;
        int originZ = (chunkZ * N) - pad;

        // Per padded column: its floor plane, whether its cell is built in, and whether it is
        // loaded at all. An unloaded column is neither a seed nor a passage.
        var floor = new int[side * side];
        var built = new bool[side * side];
        var loaded = new bool[side * side];
        var level = new double[side * side];
        Array.Fill(level, double.NaN);

        var cellBuilt = new Dictionary<(int X, int Z), bool>();
        for (int z = 0; z < side; z++)
        {
            for (int x = 0; x < side; x++)
            {
                int worldX = originX + x, worldZ = originZ + z;
                SkyColumn? column = sky.ColumnAt(FloorDiv(worldX, N), FloorDiv(worldZ, N));
                if (column is null || !column.IsSeeded)
                {
                    continue;
                }

                int i = (z * side) + x;
                int lx = worldX - (column.ChunkX * N), lz = worldZ - (column.ChunkZ * N);
                int seedPlane = column.SeedSurfaceY(lx, lz) + 1;
                int open = column.OpenAboveY(lx, lz);
                loaded[i] = true;
                floor[i] = open > seedPlane ? open : seedPlane;
                built[i] = CellBuilt(cellBuilt, column, FloorDiv(worldX, cellVoxels), FloorDiv(worldZ, cellVoxels), cellVoxels);
            }
        }

        // Unraised columns take their sample's table, as the band does; those under it are seeds.
        var seeds = new List<(double Level, int Index)>();
        var tables = new Dictionary<(int X, int Z), double?>();
        for (int z = 0; z < side; z++)
        {
            for (int x = 0; x < side; x++)
            {
                int i = (z * side) + x;
                if (!loaded[i] || built[i])
                {
                    continue;
                }

                var sample = (FloorDiv(originX + x, cellVoxels), FloorDiv(originZ + z, cellVoxels));
                if (!tables.TryGetValue(sample, out double? t))
                {
                    t = table.TableAt(sample.Item1, sample.Item2);
                    tables[sample] = t;
                }

                if (t is { } l)
                {
                    level[i] = l;
                    if (floor[i] < l)
                    {
                        seeds.Add((l, i));
                    }
                }
            }
        }

        // Ascending level, the first to reach a built-in column keeps it; the index breaks ties
        // so the arithmetic is the same every time.
        seeds.Sort(static (a, b) => a.Level != b.Level ? a.Level.CompareTo(b.Level) : a.Index.CompareTo(b.Index));
        var queue = new Queue<int>();
        int s = 0;
        while (s < seeds.Count)
        {
            double l = seeds[s].Level;
            for (; s < seeds.Count && seeds[s].Level == l; s++)
            {
                queue.Enqueue(seeds[s].Index);
            }

            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                int x = i % side, z = i / side;
                Enter(x - 1, z);
                Enter(x + 1, z);
                Enter(x, z - 1);
                Enter(x, z + 1);

                void Enter(int nx, int nz)
                {
                    if (nx < 0 || nz < 0 || nx >= side || nz >= side) return;
                    int n = (nz * side) + nx;
                    if (!loaded[n] || !built[n] || !double.IsNaN(level[n]) || floor[n] >= l) return;
                    level[n] = l;
                    queue.Enqueue(n);
                }
            }
        }

        var result = new double[N * N];
        int reached = 0;
        for (int lz = 0; lz < N; lz++)
        {
            for (int lx = 0; lx < N; lx++)
            {
                int i = ((lz + pad) * side) + lx + pad;
                result[(lz * N) + lx] = level[i];
                if (built[i] && !double.IsNaN(level[i])) reached++;
            }
        }

        return new WaterColumns(chunkX, chunkZ, result, reached);
    }

    private static bool AnyPlaced(SkyColumn column)
    {
        for (int lz = 0; lz < N; lz++)
        {
            for (int lx = 0; lx < N; lx++)
            {
                if (column.OpenAboveY(lx, lz) > column.SeedSurfaceY(lx, lz) + 1) return true;
            }
        }

        return false;
    }

    private static bool CellBuilt(Dictionary<(int X, int Z), bool> memo, SkyColumn column, int sampleX, int sampleZ, int cellVoxels)
    {
        if (memo.TryGetValue((sampleX, sampleZ), out bool answer)) return answer;

        // A cell never straddles a chunk column, because a cell divides a chunk: all of it is in
        // this column.
        int x0 = (sampleX * cellVoxels) - (column.ChunkX * N);
        int z0 = (sampleZ * cellVoxels) - (column.ChunkZ * N);
        answer = false;
        for (int lz = z0; lz < z0 + cellVoxels && !answer; lz++)
        {
            for (int lx = x0; lx < x0 + cellVoxels; lx++)
            {
                if (column.OpenAboveY(lx, lz) > column.SeedSurfaceY(lx, lz) + 1)
                {
                    answer = true;
                    break;
                }
            }
        }

        memo[(sampleX, sampleZ)] = answer;
        return answer;
    }

    private static int FloorDiv(int value, int divisor) =>
        value >= 0 ? value / divisor : -((-value + divisor - 1) / divisor);
}

/// <summary>One chunk column's water columns, frozen: per voxel column the lake's level, or none.</summary>
public sealed class WaterColumns
{
    private const int N = VoxelConstants.ChunkSize;
    private readonly double[] _levels;

    internal WaterColumns(int chunkX, int chunkZ, double[] levels, int reached)
    {
        ChunkX = chunkX;
        ChunkZ = chunkZ;
        _levels = levels;
        BuiltColumnsReached = reached;
    }

    public int ChunkX { get; }

    public int ChunkZ { get; }

    /// <summary>Built-in columns the flood reached — the grid's own work, beside today's band.</summary>
    public int BuiltColumnsReached { get; }

    /// <summary>The lake's level over this voxel column, if a lake stands over it.</summary>
    public bool TryGetLevel(int localX, int localZ, out double level)
    {
        level = _levels[(localZ * N) + localX];
        return !double.IsNaN(level);
    }
}
