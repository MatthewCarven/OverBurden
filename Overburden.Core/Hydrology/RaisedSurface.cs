using Overburden.Core.Lighting;
using Overburden.Core.Persistence;
using Overburden.Core.Voxels;
using Overburden.Core.Diagnostics;

namespace Overburden.Core.Hydrology;

/// <summary>
/// Where the player's material stands above the seed surface, per watershed sample: the plane a
/// sample's water would have to rise past to cross it (docs/POND-PLAN.md, 2026-09-25).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why.</b> The watershed samples the seed terrain, so a wall on a pond's rim was invisible to
/// it and every litre poured in spilled at the natural rim — Matthew's stone-edged pond on
/// <c>Regions16</c>. A sample the player has built in answers the highest placed top in its cell
/// instead, and nothing here ever lowers a sample: a dug column is Tier B's.
/// </para>
/// <para>
/// <b>The cell's maximum, not a point.</b> <see cref="HeightField.Sample"/> reads one voxel column
/// per cell, at its corner, so a wall a metre thick misses it three times in four. A raise is the
/// highest <see cref="SkyColumn.OpenAboveY"/> over every placed column in the cell's footprint —
/// "placed" being <see cref="RainOnPlaced"/>'s predicate, a top above its seed surface plus one —
/// so a wall of any thickness raises its cell. Stated costs (the pass's decision 2): a gap
/// narrower than a cell does not leak for Tier A, and a roof or a bridge raises the cells under it,
/// because an open height cannot tell a wall from a roof.
/// </para>
/// <para>
/// <b>Kept after a column unloads.</b> What a column held when it was last seen is what the store
/// holds for it, so the raise survives walking away (decision 1): a pond does not drop because
/// its wall left the box. At open the map is derived from the stored columns
/// (<see cref="DeriveFrom"/>), so it needs no table of its own.
/// </para>
/// <para>
/// <b>Versioned, and read by a worker through a snapshot.</b> The analysis runs off the sim
/// thread, so it reads an immutable copy (<see cref="Snapshot"/>); <see cref="Version"/> moves
/// whenever a sample's raise does, which is what the caller re-analyses on.
/// </para>
/// </remarks>
public sealed class RaisedSurface : IRaisedSamples
{
    private const int N = VoxelConstants.ChunkSize;

    private readonly int _cellVoxels;
    private readonly int _cellsPerChunk;
    private readonly Dictionary<(int X, int Z), int> _raised = [];
    private readonly Dictionary<(int X, int Z), long> _revisions = [];

    /// <param name="cellVoxels">The watershed's lattice. Must divide a chunk's width.</param>
    public RaisedSurface(int cellVoxels = HeightField.DefaultCellVoxels)
    {
        if (cellVoxels <= 0 || N % cellVoxels != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cellVoxels),
                cellVoxels,
                $"A watershed cell must divide a chunk's {N} voxels, so a chunk column holds whole cells.");
        }

        _cellVoxels = cellVoxels;
        _cellsPerChunk = N / cellVoxels;
    }

    /// <summary>Moves whenever any sample's raise appears, changes or goes.</summary>
    public long Version { get; private set; }

    /// <summary>Samples currently raised.</summary>
    public int Count => _raised.Count;

    /// <summary>Chunk-column scans over the lifetime — one per revision a column was seen at.</summary>
    public long Scans { get; private set; }

    /// <summary>The raised plane of a world-lattice sample, if the player has built in it.</summary>
    public bool TryGetRaised(int sampleX, int sampleZ, out int plane) =>
        _raised.TryGetValue((sampleX, sampleZ), out plane);

    private static readonly int WatchObserve = CallWatch.Register("raise: observe a column");

    /// <summary>
    /// Reads one chunk column's placed tops, if its heights have moved since it was last seen, and
    /// updates its cells. Returns whether any sample's raise changed.
    /// </summary>
    public bool Observe(SkyColumn column)
    {
        using CallWatch.Scope callWatch = CallWatch.Time(WatchObserve);
        ArgumentNullException.ThrowIfNull(column);
        if (!column.IsSeeded)
        {
            // An unseeded column has no seed surface, so "placed" has no answer. The streamer
            // seeds every live column; only a blob-read temporary is unseeded, and DeriveFrom
            // seeds those itself.
            return false;
        }

        var key = (column.ChunkX, column.ChunkZ);
        if (_revisions.TryGetValue(key, out long seen) && seen == column.Revision)
        {
            return false;
        }

        _revisions[key] = column.Revision;
        Scans++;
        bool changed = false;
        int baseSampleX = column.ChunkX * _cellsPerChunk;
        int baseSampleZ = column.ChunkZ * _cellsPerChunk;

        for (int cz = 0; cz < _cellsPerChunk; cz++)
        {
            for (int cx = 0; cx < _cellsPerChunk; cx++)
            {
                int top = int.MinValue;
                for (int lz = cz * _cellVoxels; lz < (cz + 1) * _cellVoxels; lz++)
                {
                    for (int lx = cx * _cellVoxels; lx < (cx + 1) * _cellVoxels; lx++)
                    {
                        int open = column.OpenAboveY(lx, lz);
                        if (open > column.SeedSurfaceY(lx, lz) + 1 && open > top)
                        {
                            top = open;
                        }
                    }
                }

                var sample = (baseSampleX + cx, baseSampleZ + cz);
                if (top == int.MinValue)
                {
                    changed |= _raised.Remove(sample);
                }
                else if (!_raised.TryGetValue(sample, out int was) || was != top)
                {
                    _raised[sample] = top;
                    changed = true;
                }
            }
        }

        if (changed)
        {
            Version++;
        }

        return changed;
    }

    private static readonly int WatchDerive = CallWatch.Register("raise: derive at open");

    /// <summary>
    /// Derives the map from every column the store holds — the open's half of decision 1. Returns
    /// how many columns it read.
    /// </summary>
    /// <remarks>
    /// A stored column is only its open heights, so each is seeded from the seed surface and has
    /// the stored heights copied over it, exactly as the streamer restores one. A column the store
    /// cannot read is skipped rather than thrown on: the world opens without that column's raise,
    /// and its chunk's arrival observes it again.
    /// </remarks>
    public int DeriveFrom(IWorldStore store, ISurfaceHeights seed)
    {
        using CallWatch.Scope callWatch = CallWatch.Time(WatchDerive);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(seed);
        int read = 0;
        foreach ((int x, int z) in store.LoadColumnIndex())
        {
            byte[]? blob = store.ReadColumn(x, z);
            if (blob is null)
            {
                continue;
            }

            SkyColumn stored = SkyColumnBlob.Read(blob, x, z);
            var column = new SkyColumn(x, z);
            column.Seed(seed);
            column.CopyFrom(stored);
            Observe(column);
            read++;
        }

        return read;
    }

    /// <summary>Every raised sample and its plane — what the surface walks instead of asking all 65,536 samples of a window.</summary>
    public IEnumerable<KeyValuePair<(int X, int Z), int>> Samples => _raised;

    /// <summary>An immutable copy for a worker's analysis.</summary>
    public RaisedSamples Snapshot() => new(new Dictionary<(int X, int Z), int>(_raised));
}

/// <summary>What the watershed reads a raise through: a world-lattice sample to its raised plane.</summary>
public interface IRaisedSamples
{
    /// <summary>The raised plane of a world-lattice sample, if the player has built in it.</summary>
    bool TryGetRaised(int sampleX, int sampleZ, out int plane);
}

/// <summary>A frozen copy of <see cref="RaisedSurface"/>, safe to read from a worker.</summary>
public sealed class RaisedSamples : IRaisedSamples
{
    private readonly Dictionary<(int X, int Z), int> _raised;

    internal RaisedSamples(Dictionary<(int X, int Z), int> raised) => _raised = raised;

    /// <summary>A snapshot with nothing raised.</summary>
    public static RaisedSamples Empty { get; } = new([]);

    public int Count => _raised.Count;

    public bool TryGetRaised(int sampleX, int sampleZ, out int plane) =>
        _raised.TryGetValue((sampleX, sampleZ), out plane);
}
