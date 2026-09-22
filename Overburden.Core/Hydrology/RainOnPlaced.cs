using Overburden.Core.Fluid;
using Overburden.Core.Lighting;
using Overburden.Core.Voxels;

namespace Overburden.Core.Hydrology;

/// <summary>
/// Rain that falls on the player's material lands on it (2026-09-22): a voxel column whose top is
/// above its seed surface — a placed floor, a wall's top, rubble on the ground — gets the rain
/// over it as Tier B cells at its open height, instead of that rain being credited to the basin
/// of the seed terrain under the floor.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why.</b> Matthew's clay pond was not filling — <i>"water doesnt seem to be building up in my
/// clay pond"</i> — and it was not leaking: its floor is placed, so its water is never on the seed
/// plane and holds. 98% of the rain is Tier A's and goes to the basins of the seed terrain, which
/// cannot see a placed floor, and the 2% that falls as cells is scattered at random over the whole
/// box. <i>"rain on my stuff should land in it."</i> The planning pass is in M8-PLAN.
/// </para>
/// <para>
/// <b>Accrued per chunk column, not per voxel column.</b> Each loaded chunk column in the box
/// keeps one accumulator in nanometre-columns — the depth that fell times the placed columns it
/// holds, exact integers — and each whole cell (<see cref="NanometreColumnsPerCell"/>: a cell is
/// 15,625,000 µL and a voxel column is 1/16 m², so 250,000,000 nm over one column) is dropped at
/// one of its placed columns picked at random, so the total over the footprint is exact and the
/// state is one record a chunk column rather than 173k. The placed set is cached against the
/// <see cref="SkyColumn.Revision"/>, so the 1,024-column scan runs only after an edit moved a
/// height there. An hour in one step yields the same cells as an hour in steps, per chunk column.
/// </para>
/// <para>
/// <b>A dug column is nature's bowl.</b> Below the seed Tier A still delivers — the basin's lake
/// rises and the water arrives through the portals and the lake ceiling, one number a basin —
/// so only a top <i>above</i> the seed is this rule's. A column whose landing voxel is outside
/// the loaded vertical box cannot be rained on and waits, up to <see cref="ForfeitCells"/> cells;
/// past that the rain is forfeited to the basin under it, which is where it went before this rule
/// existed, and counted. What the caller drops it owes to the basin under each cell through
/// <see cref="WaterSystem.DivertRain(int, long)"/>.
/// </para>
/// </remarks>
public sealed class RainOnPlaced
{
    /// <summary>Nanometres of depth over one voxel column that make one cell: 16 columns to the square metre, 15,625,000 µL a cell.</summary>
    public const long NanometreColumnsPerCell = 16L * FluidConstants.CellCapacityMillilitres * 1000L;

    /// <summary>Cells a chunk column may hold accrued while its rain cannot be dropped, before the rest is forfeited to the basin.</summary>
    public const int ForfeitCells = 64;

    private const int N = VoxelConstants.ChunkSize;

    private sealed class ChunkColumnRain
    {
        public long Revision = long.MinValue;
        public int[] Placed = [];
        public int Count;
        public long AccruedNanometreColumns;
        public int Seen;
    }

    private readonly ISkyColumns _sky;
    private readonly Random _random;
    private readonly Dictionary<(int X, int Z), ChunkColumnRain> _columns = [];
    private readonly List<(int X, int Z)> _gone = [];
    private int _pass;

    public RainOnPlaced(ISkyColumns sky, Random random)
    {
        ArgumentNullException.ThrowIfNull(sky);
        ArgumentNullException.ThrowIfNull(random);
        _sky = sky;
        _random = random;
    }

    /// <summary>Placed columns over the box at the last <see cref="Advance"/>.</summary>
    public int PlacedColumns { get; private set; }

    /// <summary>Chunk columns with at least one placed column at the last advance.</summary>
    public int ChunkColumnsWithPlaced { get; private set; }

    /// <summary>Scans of a chunk column's 1,024 heights, over the lifetime — one per revision change.</summary>
    public long Rescans { get; private set; }

    /// <summary>Cells yielded to the caller, over the lifetime.</summary>
    public long CellsYielded { get; private set; }

    /// <summary>Whole cells accrued and not yet yielded at the last advance — the cap's backlog and the columns the box does not reach.</summary>
    public long Backlog { get; private set; }

    /// <summary>Cells forfeited to the basin under them because their column could not be rained on for <see cref="ForfeitCells"/> cells, over the lifetime.</summary>
    public long Forfeited { get; private set; }

    /// <summary>
    /// Accrues <paramref name="depthNanometres"/> of rain over every placed column in the box —
    /// chunk columns within <paramref name="rings"/> of the viewer's, landing voxels within the
    /// vertical box — and yields whole cells into <paramref name="into"/>, at most
    /// <paramref name="capacity"/> in all. Returns how many it yielded.
    /// </summary>
    public int Advance(long depthNanometres, ChunkPos viewer, int rings, int verticalRings, List<VoxelPos> into, int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(depthNanometres);
        ArgumentNullException.ThrowIfNull(into);
        _pass++;
        int yielded = 0;
        int placed = 0, chunkColumns = 0;
        long backlog = 0;
        int yMin = (viewer.Y - verticalRings) * N;
        int yMax = ((viewer.Y + verticalRings + 1) * N) - 1;

        for (int cz = viewer.Z - rings; cz <= viewer.Z + rings; cz++)
        {
            for (int cx = viewer.X - rings; cx <= viewer.X + rings; cx++)
            {
                SkyColumn? column = _sky.ColumnAt(cx, cz);
                if (column is null || !column.IsSeeded)
                {
                    continue;
                }

                if (!_columns.TryGetValue((cx, cz), out ChunkColumnRain? record))
                {
                    record = new ChunkColumnRain();
                    _columns[(cx, cz)] = record;
                }

                record.Seen = _pass;
                if (record.Revision != column.Revision)
                {
                    Rescan(record, column);
                }

                if (record.Count == 0)
                {
                    // Nothing placed here any more: what was accrued for a floor that is gone
                    // was never diverted, so the basin has it already.
                    record.AccruedNanometreColumns = 0;
                    continue;
                }

                placed += record.Count;
                chunkColumns++;
                record.AccruedNanometreColumns += (long)record.Count * depthNanometres;

                bool outside = false;
                while (record.AccruedNanometreColumns >= NanometreColumnsPerCell && into.Count < capacity)
                {
                    // A placed column at random; one whose top is outside the loaded vertical box
                    // cannot be rained on now, so try a few more before calling the chunk column
                    // out of reach — a floor half in the box still gets its rain.
                    int lx = 0, lz = 0, y = int.MinValue;
                    for (int attempt = 0; attempt < 8; attempt++)
                    {
                        int index = record.Placed[_random.Next(record.Count)];
                        lx = index & (N - 1);
                        lz = index >> VoxelConstants.ChunkSizeLog2;
                        y = column.OpenAboveY(lx, lz);
                        if (y >= yMin && y <= yMax)
                        {
                            break;
                        }

                        y = int.MinValue;
                    }

                    if (y == int.MinValue)
                    {
                        outside = true;
                        break;
                    }

                    into.Add(new VoxelPos((cx * N) + lx, y, (cz * N) + lz));
                    record.AccruedNanometreColumns -= NanometreColumnsPerCell;
                    yielded++;
                }

                if (outside)
                {
                    // Out of the loaded vertical box: the rain waits for the box, up to the
                    // forfeit cap; past it the basin under the column has the rain, as it did
                    // before this rule. The caller's cap is not this — what it leaves is kept.
                    long excess = record.AccruedNanometreColumns - (ForfeitCells * NanometreColumnsPerCell);
                    if (excess > 0)
                    {
                        long cells = excess / NanometreColumnsPerCell;
                        record.AccruedNanometreColumns -= cells * NanometreColumnsPerCell;
                        Forfeited += cells;
                    }
                }

                backlog += record.AccruedNanometreColumns / NanometreColumnsPerCell;
            }
        }

        _gone.Clear();
        foreach ((var key, ChunkColumnRain record) in _columns)
        {
            if (record.Seen != _pass)
            {
                _gone.Add(key);
            }
        }

        foreach ((int X, int Z) key in _gone)
        {
            _columns.Remove(key);
        }

        PlacedColumns = placed;
        ChunkColumnsWithPlaced = chunkColumns;
        CellsYielded += yielded;
        Backlog = backlog;
        return yielded;
    }

    private void Rescan(ChunkColumnRain record, SkyColumn column)
    {
        Rescans++;
        int count = 0;
        for (int lz = 0; lz < N; lz++)
        {
            for (int lx = 0; lx < N; lx++)
            {
                if (column.OpenAboveY(lx, lz) > column.SeedSurfaceY(lx, lz) + 1)
                {
                    count++;
                }
            }
        }

        if (record.Placed.Length < count)
        {
            record.Placed = new int[Math.Max(count, 16)];
        }

        int at = 0;
        for (int lz = 0; lz < N; lz++)
        {
            for (int lx = 0; lx < N; lx++)
            {
                if (column.OpenAboveY(lx, lz) > column.SeedSurfaceY(lx, lz) + 1)
                {
                    record.Placed[at++] = (lz << VoxelConstants.ChunkSizeLog2) | lx;
                }
            }
        }

        record.Count = count;
        record.Revision = column.Revision;
    }
}
