using Overburden.Core.Content;
using Overburden.Core.Lighting;
using Overburden.Core.Voxels;
using Overburden.Core.Diagnostics;

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
/// <b>A column's water room is its lowest open run above the natural ground</b> (the overhang pass,
/// 2026-09-26): <c>[floor, ceil)</c>, from the first open voxel at or above the seed plane to the
/// first solid above that. Under an overhang that is the ground up to its underside, a wall's
/// starts at its top, a window's at its sill and a doorway's at the floor. A neighbour is entered
/// only where the two runs overlap below the level, so water never passes a solid block between
/// two stacked runs. Given no voxels (<c>voxels</c> null) a placed column's run is its top upward,
/// the rule before the pass: the overhang reads as a wall. Only the lowest run is modelled, so the
/// lake over a submerged roof above a sealed room is not drawn, which is water missing, never
/// water inside a wall. An unloaded voxel is solid: it ends a run.
/// </para>
/// <para>
/// <b>Water crosses a chunk column's edge between grids</b> (2026-09-26, his option 2, after a probe
/// on his pond found a 2 m strip dry beside a wet grid across the seam): a built column in the pad
/// that the neighbouring chunk column's grid holds wet is a seed here too. It carries that
/// water's source sample, at the source's live level, so a lake that drains takes it along. It
/// also counts one more <b>hop</b>, capped at <see cref="MaxHops"/>. Two grids can otherwise keep
/// each other wet after a wall cuts the lake off: each seeds from the other's last answer. The cap
/// ends that loop within that many rebuilds, the distance-vector bound. Within a level, lower hops
/// flood first, so water the window reaches for itself is never claimed second-hand.
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
    /// Chunk-column edges a lake may be handed across before it stops — 256 m of built ground from
    /// the water's own ground. An estimate; it also bounds how long a lake cut off by a wall can
    /// linger, in rebuilds.
    /// </summary>
    public const int MaxHops = 8;

    private static readonly int WatchBuild = CallWatch.Register("water grid: build a chunk column");

    /// <summary>
    /// Builds the water columns of chunk column (<paramref name="chunkX"/>, <paramref name="chunkZ"/>),
    /// or returns null when nothing is placed in it — its bands are today's and no grid is needed.
    /// With <paramref name="voxels"/> and <paramref name="materials"/> a placed column's run is read
    /// off its voxels (the overhang pass); without, it is its top upward. With
    /// <paramref name="neighbour"/> — a neighbouring chunk column's grid as last built, never a build —
    /// the water it holds at this grid's edge seeds this one.
    /// </summary>
    public static WaterColumns? Build(
        int chunkX, int chunkZ, ISkyColumns sky, IWaterTable table, int cellVoxels,
        IVoxelWorld? voxels = null, MaterialTable? materials = null,
        Func<int, int, WaterColumns?>? neighbour = null)
    {
        using CallWatch.Scope callWatch = CallWatch.Time(WatchBuild);
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
        var ceil = new int[side * side];
        Array.Fill(ceil, int.MaxValue);
        var built = new bool[side * side];
        var loaded = new bool[side * side];
        var level = new double[side * side];
        Array.Fill(level, double.NaN);
        var sourceX = new int[side * side];
        var sourceZ = new int[side * side];
        var hops = new int[side * side];

        // The highest level any sample in the pad could bring. A run above it can hold nothing, so
        // a walk stops there: a tower is a few reads a column, not its height (the overhang pass
        // measured 15 ms a build on his tall stone before this bound).
        var tables = new Dictionary<(int X, int Z), double?>();
        double highest = double.NegativeInfinity;
        for (int sz = FloorDiv(originZ, cellVoxels); sz <= FloorDiv(originZ + side - 1, cellVoxels); sz++)
        {
            for (int sx = FloorDiv(originX, cellVoxels); sx <= FloorDiv(originX + side - 1, cellVoxels); sx++)
            {
                double? t = table.TableAt(sx, sz);
                tables[(sx, sz)] = t;
                if (t is { } l && l > highest) highest = l;
            }
        }

        // The water the neighbouring grids hold in this pad, at each source's live level. Gathered
        // before the walks, because a handed-over level can be above every table in the pad and the
        // walks stop at the highest level in reach.
        var handed = new Dictionary<int, (double Level, int SourceX, int SourceZ, int Hops)>();
        if (neighbour is not null)
        {
            var grids = new Dictionary<(int X, int Z), WaterColumns?>();
            for (int z = 0; z < side; z++)
            {
                for (int x = 0; x < side; x++)
                {
                    int worldX = originX + x, worldZ = originZ + z;
                    int cx = FloorDiv(worldX, N), cz = FloorDiv(worldZ, N);
                    if (cx == chunkX && cz == chunkZ) continue;
                    if (!grids.TryGetValue((cx, cz), out WaterColumns? grid))
                    {
                        grid = neighbour(cx, cz);
                        grids[(cx, cz)] = grid;
                    }

                    int nlx = worldX - (cx * N), nlz = worldZ - (cz * N);
                    if (grid is null || !grid.TryGetSource(nlx, nlz, out int srcX, out int srcZ)) continue;
                    int h = grid.HopsAt(nlx, nlz) + 1;
                    if (h > MaxHops) continue;
                    if (!tables.TryGetValue((srcX, srcZ), out double? live))
                    {
                        live = table.TableAt(srcX, srcZ);
                        tables[(srcX, srcZ)] = live;
                    }

                    if (live is not { } l) continue;
                    handed[(z * side) + x] = (l, srcX, srcZ, h);
                    if (l > highest) highest = l;
                }
            }
        }

        int limit = double.IsNegativeInfinity(highest) ? int.MinValue : (int)Math.Ceiling(highest) + 1;

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
                if (open <= seedPlane)
                {
                    floor[i] = seedPlane;
                }
                else if (voxels is null || materials is null || seedPlane >= limit)
                {
                    floor[i] = open;
                }
                else
                {
                    (floor[i], ceil[i]) = Run(voxels, materials, worldX, worldZ, seedPlane, Math.Min(open, limit), open);
                }

                built[i] = CellBuilt(cellBuilt, column, FloorDiv(worldX, cellVoxels), FloorDiv(worldZ, cellVoxels), cellVoxels);
            }
        }

        // Unraised columns take their sample's table, as the band does; those under it are seeds.
        // A built pad column a neighbour's grid holds wet is a seed at the level it was handed.
        var seeds = new List<(double Level, int Hops, int Index)>();
        foreach ((int i, (double l, int srcX, int srcZ, int h)) in handed)
        {
            if (!loaded[i] || !built[i] || floor[i] >= l) continue;
            level[i] = l;
            sourceX[i] = srcX;
            sourceZ[i] = srcZ;
            hops[i] = h;
            seeds.Add((l, h, i));
        }

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
                    sourceX[i] = sample.Item1;
                    sourceZ[i] = sample.Item2;
                    if (floor[i] < l)
                    {
                        seeds.Add((l, 0, i));
                    }
                }
            }
        }

        // Ascending level, the first to reach a built-in column keeps it; within a level, fewer
        // hops first, so water the window reaches for itself is never claimed second-hand; the index
        // breaks ties so the arithmetic is the same every time.
        seeds.Sort(static (a, b) => a.Level != b.Level ? a.Level.CompareTo(b.Level)
            : a.Hops != b.Hops ? a.Hops.CompareTo(b.Hops) : a.Index.CompareTo(b.Index));
        var queue = new Queue<int>();
        int s = 0;
        while (s < seeds.Count)
        {
            double l = seeds[s].Level;
            int hop = seeds[s].Hops;
            for (; s < seeds.Count && seeds[s].Level == l && seeds[s].Hops == hop; s++)
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
                    if (!loaded[n] || !built[n] || !double.IsNaN(level[n])) return;

                    // The two runs must overlap below the level: water crosses where both columns
                    // are open at once, and never through a solid block between stacked runs.
                    int lo = Math.Max(floor[i], floor[n]);
                    if (lo >= Math.Min(ceil[i], ceil[n]) || lo >= l) return;
                    level[n] = l;
                    sourceX[n] = sourceX[i];
                    sourceZ[n] = sourceZ[i];
                    hops[n] = hops[i];
                    queue.Enqueue(n);
                }
            }
        }

        var result = new double[N * N];
        var resultX = new int[N * N];
        var resultZ = new int[N * N];
        var resultHops = new int[N * N];
        int reached = 0;
        for (int lz = 0; lz < N; lz++)
        {
            for (int lx = 0; lx < N; lx++)
            {
                int i = ((lz + pad) * side) + lx + pad;
                result[(lz * N) + lx] = level[i];
                resultX[(lz * N) + lx] = sourceX[i];
                resultZ[(lz * N) + lx] = sourceZ[i];
                resultHops[(lz * N) + lx] = hops[i];
                if (built[i] && !double.IsNaN(level[i])) reached++;
            }
        }

        return new WaterColumns(chunkX, chunkZ, result, resultX, resultZ, resultHops, reached);
    }

    /// <summary>
    /// A placed column's lowest open run above the natural ground: up through solid from the seed
    /// plane to the first open voxel, then up through open to the first solid. None below the top
    /// is its top upward, as a wall's is. Both walks stop at <paramref name="bound"/> — the column's
    /// top or the highest level in reach, whichever is lower — because above the level a run's
    /// start holds nothing and its end changes no overlap under the level.
    /// </summary>
    private static (int Floor, int Ceil) Run(IVoxelWorld voxels, MaterialTable materials, int x, int z, int seedPlane, int bound, int open)
    {
        int floor = seedPlane;
        while (floor < bound && !IsOpen(voxels, materials, x, floor, z))
        {
            floor++;
        }

        if (floor >= bound)
        {
            return (open, int.MaxValue);
        }

        int ceil = floor;
        while (ceil < bound && IsOpen(voxels, materials, x, ceil, z))
        {
            ceil++;
        }

        // Stopped by the bound rather than by a solid: past the level, so as good as unbounded.
        return (floor, ceil >= bound && bound < open ? int.MaxValue : ceil);
    }

    // Unloaded is not open: it ends a run, as it ends a fluid cell's move.
    private static bool IsOpen(IVoxelWorld voxels, MaterialTable materials, int x, int y, int z) =>
        voxels.TryGetMaterial(new VoxelPos(x, y, z), out int id) && !materials.IsSolid(id);

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
    private readonly int[] _sourceX;
    private readonly int[] _sourceZ;
    private readonly int[] _hops;

    internal WaterColumns(int chunkX, int chunkZ, double[] levels, int[] sourceX, int[] sourceZ, int[] hops, int reached)
    {
        _hops = hops;
        ChunkX = chunkX;
        ChunkZ = chunkZ;
        _levels = levels;
        _sourceX = sourceX;
        _sourceZ = sourceZ;
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

    /// <summary>
    /// The world-lattice sample whose table this column's level came from — its own for an unbuilt
    /// column, the seed the flood reached it from for a built-in one. The surface reads the live
    /// level there, so a lake rising inside a voxel is not drawn a step lower where it meets a
    /// wall; the grid's own level is the build's, correct to the voxel the band needs.
    /// </summary>
    public bool TryGetSource(int localX, int localZ, out int sampleX, out int sampleZ)
    {
        int i = (localZ * N) + localX;
        sampleX = _sourceX[i];
        sampleZ = _sourceZ[i];
        return !double.IsNaN(_levels[i]);
    }

    /// <summary>Chunk-column edges this column's water was handed across; 0 where its own window reached it.</summary>
    public int HopsAt(int localX, int localZ) => _hops[(localZ * N) + localX];

    /// <summary>
    /// The same water in every column as a neighbour reads it — what its key is allowed to see move.
    /// A level is compared by its floor and ceiling, as the grid's own key compares a table: every
    /// test the flood makes is an integer against the level, and the band keeps the floor, while
    /// the surface reads the live level through the source. Compared exactly, a lake rising inside
    /// a voxel moved every handed level every boundary and rebuilt the whole ring of grids each
    /// time (8,363 builds in 30 s on his pond, against ~1,400).
    /// </summary>
    public bool SameAs(WaterColumns? other)
    {
        if (other is null) return false;
        for (int i = 0; i < _levels.Length; i++)
        {
            bool wet = !double.IsNaN(_levels[i]);
            if (wet != !double.IsNaN(other._levels[i])) return false;
            if (!wet) continue;
            if (Math.Floor(_levels[i]) != Math.Floor(other._levels[i]) || Math.Ceiling(_levels[i]) != Math.Ceiling(other._levels[i])
                || _sourceX[i] != other._sourceX[i] || _sourceZ[i] != other._sourceZ[i] || _hops[i] != other._hops[i]) return false;
        }

        return true;
    }
}
