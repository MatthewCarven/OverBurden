using Overburden.Core.Lighting;
using Overburden.Core.Voxels;

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
    private readonly int _cellsPerChunk;
    private readonly int _keyLength;
    private readonly Dictionary<(int X, int Z), Entry> _entries = [];
    private long[] _scratch;

    private sealed class Entry
    {
        public required long[] Key;
        public WaterColumns? Grid;
    }

    public WaterGrids(ISkyColumns sky, IWaterTable table, int cellVoxels)
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
        _cellsPerChunk = N / cellVoxels;
        int samples = _cellsPerChunk + 2;
        _keyLength = 9 + (2 * samples * samples);
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

    /// <summary>The water grid of a chunk column, or null when nothing is placed in it.</summary>
    public WaterColumns? Get(int chunkX, int chunkZ)
    {
        FillKey(chunkX, chunkZ, _scratch);
        if (_entries.TryGetValue((chunkX, chunkZ), out Entry? entry) && entry.Key.AsSpan().SequenceEqual(_scratch))
        {
            Hits++;
            return entry.Grid;
        }

        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        WaterColumns? grid = WaterGrid.Build(chunkX, chunkZ, _sky, _table, _cellVoxels);
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
        entry.Grid = grid;
        return grid;
    }

    private void FillKey(int chunkX, int chunkZ, long[] key)
    {
        int k = 0;
        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                SkyColumn? column = _sky.ColumnAt(chunkX + dx, chunkZ + dz);
                key[k++] = column is null || !column.IsSeeded ? Absent : column.Revision;
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
