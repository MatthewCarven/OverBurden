using Overburden.Core.Content;
using Overburden.Core.Lighting;
using Overburden.Core.Voxels;
using Overburden.Core.Diagnostics;

namespace Overburden.Core.Fluid;

/// <summary>
/// The water grids of the chunk columns that have been asked about, rebuilt only when an input
/// moved — one cache for the two readers, the lake ceiling and the surface (docs/POND-PLAN.md,
/// decision 10).
/// </summary>
/// <remarks>
/// <para>
/// <b>Dirtied by its inputs, never by its output.</b> A grid reads the nine sky columns under its
/// padded window and the table of the thirty-six samples in it, so its key is exactly those: each
/// column's <see cref="SkyColumn.Revision"/> (or its absence), and each sample's table as its
/// floor and ceiling. Floor and ceiling rather than the level, because the flood's test is an
/// integer floor against the level (it changes only when the level crosses a whole voxel) and the
/// band keeps the floor; a lake rising inside a voxel therefore costs thirty-six reads and no
/// build, and the surface reads the live level through <see cref="WaterColumns.TryGetSource"/>.
/// </para>
/// <para>
/// <b>And each column's edit count</b> (the overhang pass, 2026-09-26): a grid read with voxels
/// reads below a column's top, where an edit moves no <see cref="SkyColumn.Revision"/> — a block
/// placed under an overhang, a doorway cut under a higher roof. <see cref="VoxelChanged"/>,
/// <see cref="ChunkArrived"/> and <see cref="ChunkForgotten"/> count them per chunk column, from
/// the pipeline's own hooks, and the nine counts are in the key.
/// </para>
/// <para>
/// <b>And the eight neighbours' versions</b> (2026-09-26): a grid seeds from the water its
/// neighbours' grids hold at its edge, so it rebuilds when one of them changed. A version moves
/// only when a rebuild changed the water (<see cref="WaterColumns.SameAs"/>), never on a rebuild
/// alone, or two neighbours would rebuild each other forever. Asked every boundary, a chain of
/// grids converges a grid a boundary.
/// </para>
/// <para>
/// <b>A chunk column with nothing placed is cached too</b>, as a null against its key, so an
/// unbuilt column costs its key and not a scan of 1,024 columns each time it is asked.
/// </para>
/// <para>
/// <b>Bounded by count</b> (<see cref="MaxEntries"/>), cleared whole past it: a grid is 16 KB and
/// the entries are cheap to rebuild, so the simplest bound that cannot grow with the asking is the
/// right one — the enclosure's cache taught that (2026-09-21).
/// </para>
/// </remarks>
public sealed class WaterGrids
{
    /// <summary>Entries held before the cache is cleared — about 40 MB of grids at most. An estimate.</summary>
    public const int MaxEntries = 2048;

    private const int N = VoxelConstants.ChunkSize;
    private const long Absent = long.MinValue;
    private const long NoTable = long.MinValue + 1;

    private readonly ISkyColumns _sky;
    private readonly IWaterTable _table;
    private readonly int _cellVoxels;
    private readonly IVoxelWorld? _voxels;
    private readonly MaterialTable? _materials;
    private readonly Dictionary<(int X, int Z), long> _edits = [];
    private readonly int _cellsPerChunk;
    private readonly int _keyLength;
    private readonly Dictionary<(int X, int Z), Entry> _entries = [];
    private long[] _scratch;
    private long _versions;

    private sealed class Entry
    {
        public required long[] Key;
        public WaterColumns? Grid;
        public long Version;
    }

    public WaterGrids(ISkyColumns sky, IWaterTable table, int cellVoxels, IVoxelWorld? voxels = null, MaterialTable? materials = null)
    {
        ArgumentNullException.ThrowIfNull(sky);
        ArgumentNullException.ThrowIfNull(table);
        if (cellVoxels <= 0 || N % cellVoxels != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cellVoxels), cellVoxels, "A cell must divide a chunk's width.");
        }

        _sky = sky;
        _table = table;
        _cellVoxels = cellVoxels;
        _voxels = voxels;
        _materials = materials;
        _cellsPerChunk = N / cellVoxels;
        int samples = _cellsPerChunk + 2;
        _keyLength = 18 + 8 + (2 * samples * samples);
        _scratch = new long[_keyLength];
    }

    /// <summary>Grids built over the lifetime.</summary>
    public long Builds { get; private set; }

    /// <summary>Asks answered from the cache over the lifetime.</summary>
    public long Hits { get; private set; }

    /// <summary>Chunk columns with a grid at the moment.</summary>
    public int Grids
    {
        get
        {
            int count = 0;
            foreach (Entry entry in _entries.Values) if (entry.Grid is not null) count++;
            return count;
        }
    }

    /// <summary>Built-in columns the current grids' floods reached, summed.</summary>
    public int BuiltColumnsReached
    {
        get
        {
            int count = 0;
            foreach (Entry entry in _entries.Values) count += entry.Grid?.BuiltColumnsReached ?? 0;
            return count;
        }
    }

    /// <summary>Milliseconds spent building over the lifetime.</summary>
    public double BuildMillis { get; private set; }

    /// <summary>A voxel was written: the grids that read its chunk column rebuild when next asked.</summary>
    public void VoxelChanged(VoxelPos voxel) => Touch(FloorDiv(voxel.X, N), FloorDiv(voxel.Z, N));

    /// <summary>A chunk's voxels arrived: what a walk read as unloaded may be open now.</summary>
    public void ChunkArrived(ChunkPos chunk) => Touch(chunk.X, chunk.Z);

    /// <summary>A chunk left: what a walk read as open is unloaded now.</summary>
    public void ChunkForgotten(ChunkPos chunk) => Touch(chunk.X, chunk.Z);

    private void Touch(int chunkX, int chunkZ)
    {
        _edits.TryGetValue((chunkX, chunkZ), out long count);
        _edits[(chunkX, chunkZ)] = count + 1;
    }

    private static int FloorDiv(int value, int divisor) =>
        value >= 0 ? value / divisor : -((-value + divisor - 1) / divisor);

    private static readonly int WatchGet = CallWatch.Register("water grid: get (cached)");

    /// <summary>The water grid of a chunk column, or null when nothing is placed in it.</summary>
    public WaterColumns? Get(int chunkX, int chunkZ)
    {
        using CallWatch.Scope callWatch = CallWatch.Time(WatchGet);
        FillKey(chunkX, chunkZ, _scratch);
        if (_entries.TryGetValue((chunkX, chunkZ), out Entry? entry) && entry.Key.AsSpan().SequenceEqual(_scratch))
        {
            Hits++;
            return entry.Grid;
        }

        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        WaterColumns? grid = WaterGrid.Build(chunkX, chunkZ, _sky, _table, _cellVoxels, _voxels, _materials, Neighbour);
        BuildMillis += (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        Builds++;

        if (entry is null)
        {
            if (_entries.Count >= MaxEntries)
            {
                _entries.Clear();
            }

            entry = new Entry { Key = new long[_keyLength] };
            _entries[(chunkX, chunkZ)] = entry;
        }

        _scratch.CopyTo(entry.Key, 0);
        bool same = grid is null ? entry.Grid is null && entry.Version != 0 : grid.SameAs(entry.Grid);
        if (!same)
        {
            entry.Version = ++_versions;
        }

        entry.Grid = grid;
        return grid;
    }

    /// <summary>A neighbour's grid as last built — never a build, or asking would recurse round the ring.</summary>
    private WaterColumns? Neighbour(int chunkX, int chunkZ) =>
        _entries.TryGetValue((chunkX, chunkZ), out Entry? entry) ? entry.Grid : null;

    private void FillKey(int chunkX, int chunkZ, long[] key)
    {
        int k = 0;
        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                SkyColumn? column = _sky.ColumnAt(chunkX + dx, chunkZ + dz);
                key[k++] = column is null || !column.IsSeeded ? Absent : column.Revision;
                key[k++] = _edits.TryGetValue((chunkX + dx, chunkZ + dz), out long edits) ? edits : 0;
                if (dx != 0 || dz != 0)
                {
                    key[k++] = _entries.TryGetValue((chunkX + dx, chunkZ + dz), out Entry? near) ? near.Version : 0;
                }
            }
        }

        int x0 = (chunkX * _cellsPerChunk) - 1;
        int z0 = (chunkZ * _cellsPerChunk) - 1;
        int samples = _cellsPerChunk + 2;
        for (int sz = 0; sz < samples; sz++)
        {
            for (int sx = 0; sx < samples; sx++)
            {
                double? table = _table.TableAt(x0 + sx, z0 + sz);
                if (table is { } level)
                {
                    key[k++] = (long)Math.Floor(level);
                    key[k++] = (long)Math.Ceiling(level);
                }
                else
                {
                    key[k++] = NoTable;
                    key[k++] = NoTable;
                }
            }
        }
    }
}
