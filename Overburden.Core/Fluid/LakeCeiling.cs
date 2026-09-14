using Overburden.Core.Hydrology;
using Overburden.Core.Voxels;
using Overburden.Core.Lighting;

namespace Overburden.Core.Fluid;

/// <summary>
/// Where Tier A's lakes are, as the automaton sees them: per voxel column, the band of natural air
/// from the seed ground up to the lake's top that Tier B may not enter (M8d-5).
/// </summary>
/// <remarks>
/// <para>
/// <b>A lake is a body Tier B cannot enter.</b> M8c's spine — <i>the water table under a lake is
/// the lake</i> — applied to the automaton. Before this, Tier A and Tier B held two models of one
/// column that neither could see: to Tier A the column was full to the level, to Tier B it held
/// a cell or two on the bed. Draining one lake into another produced both at once, the plane at
/// the level over a stepped sheet running along the bed, and the bank rule that kept them apart
/// (M8d-3, decision 8) had been built to cut a treadmill that only existed because the bed under
/// a lake was, to Tier B, dry ground it could spread onto. With the band unholdable the bed
/// cannot be leaked onto, water resting on the lake's top is handed to the basin as water on the
/// ground is, and nothing of Tier B exists under a Tier A plane on natural ground.
/// </para>
/// <para>
/// <b>Per voxel column, from the seed surface.</b> A cell is in the lake iff
/// <c>seed(x, z) &lt; Y ≤ floor(table(sample(x, z)))</c>. The seed surface and not the dug one,
/// because a dug voxel is below the seed by definition and is where the lake pours <i>into</i>
/// through the portals; and per column rather than per 2 m sample because the seed varies inside
/// a sample on any slope. The table is <see cref="WaterSystem.TableAt"/>, Tier C, so there is one
/// definition of "the lake over here" between the seep, the collapse and the automaton.
/// </para>
/// <para>
/// <b>Refreshed at the tick boundary, never on the hot path, and the arrays are replaced rather
/// than mutated.</b> A visit reads two ints per holdable cell through
/// <see cref="LakeBoundedTerrain"/>; a boundary recomputes sixteen sample tops per reachable
/// chunk column and rebuilds an array only where they moved. M3a's copy-on-write, so a worker's
/// <see cref="LakeCeilingView"/> keeps reading one consistent lake a boundary out of date while
/// the live ceiling moves under it. The band is deliberately not in <see cref="FluidField"/>'s
/// bits: there it would conflate rock with lake behind one <c>false</c>, and a rain at ×20 moves
/// some basin's <c>floor(level)</c> every few seconds across hundreds of basins, each a chunk
/// column of rebakes.
/// </para>
/// <para>
/// <b>A rise wakes what it submerges.</b> A cell asleep on a dry bed that the level then climbs
/// past is inside the lake with nothing to visit it; the refresh <c>Disturb</c>s every cell of
/// every band that grew, and a chunk column newly reachable counts as grown from nothing — which
/// is how an old save's bed water migrates by itself. Woken, visited, listed as submerged, handed
/// at the next boundary: two boundaries from a rise to the lake owning the water.
/// </para>
/// </remarks>
public sealed class LakeCeiling
{
    /// <summary>The <c>top</c> value of a column with no band.</summary>
    public const int NoLake = int.MinValue;

    private const int Columns = VoxelConstants.ChunkSize * VoxelConstants.ChunkSize;

    private readonly ISurfaceHeights _seed;
    private readonly WaterSystem _water;
    private readonly Dictionary<(int X, int Z), ColumnBand> _bands = [];
    private readonly HashSet<(int X, int Z)> _seen = [];
    private readonly List<(int X, int Z)> _gone = [];

    public LakeCeiling(ISurfaceHeights seed, WaterSystem water)
    {
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(water);
        _seed = seed;
        _water = water;
        Live = new LakeCeilingView(_bands);
    }

    /// <summary>
    /// The bands as they stand, readable — what <see cref="LakeBoundedTerrain"/> reads on the
    /// sim-thread arm. One sealed read structure for the live ceiling and a capture alike, so the
    /// hot path is a non-virtual call and one cached lookup: the first version's wrapper over a
    /// live-or-captured branch read 2.4–3.2x the tick on <c>--spill</c>.
    /// </summary>
    public LakeCeilingView Live { get; }

    /// <summary>Chunk columns with a band.</summary>
    public int ChunkColumns => _bands.Count;

    /// <summary>Bytes held by the bands.</summary>
    public long Bytes => (long)_bands.Count * ColumnBand.Bytes;

    /// <summary>Refreshes over the lifetime.</summary>
    public long Refreshes { get; private set; }

    /// <summary>Chunk columns whose band array was rebuilt because a sample top moved, over the lifetime.</summary>
    public long Rebuilt { get; private set; }

    /// <summary>Cells the refresh woke for having been submerged by a rise, over the lifetime — including dry ones the automaton found nothing in.</summary>
    public long WokenSubmerged { get; private set; }

    /// <summary>Chunk columns rebuilt on the last refresh.</summary>
    public int RebuiltLastPass { get; private set; }

    /// <summary>
    /// True when the voxel is inside a lake: natural air above the seed ground at or under the
    /// lake's top. Solid rock is never in a lake — a placed block above the seed is the caller's
    /// business, and <see cref="LakeBoundedTerrain"/> asks the inner terrain first.
    /// </summary>
    public bool InLake(VoxelPos voxel) => Live.InLake(voxel);

    /// <summary>
    /// The plane water stands on at this column: the lake's top where there is one, else the seed
    /// ground. A cell resting at <c>plane + 1</c> is on the lake or on the ground, and the hand-off
    /// hands it either way.
    /// </summary>
    public int PlaneAt(int worldX, int worldZ)
    {
        int seed = _seed.SurfaceHeightAt(worldX, worldZ);
        ColumnBand? band = Live.Band(worldX >> VoxelConstants.ChunkSizeLog2, worldZ >> VoxelConstants.ChunkSizeLog2);
        if (band is null)
        {
            return seed;
        }

        int top = band.TopAt(worldX & VoxelConstants.ChunkMask, worldZ & VoxelConstants.ChunkMask);
        return top == NoLake ? seed : Math.Max(seed, top);
    }

    /// <summary>
    /// Recomputes the band of every chunk column any of <paramref name="reachable"/> stands in,
    /// drops the bands of columns no longer reachable, and calls <paramref name="submerged"/> for
    /// every cell a band grew to cover. Call at the tick boundary, after the passes that move
    /// litres between the tiers and before the field is synced and captured.
    /// </summary>
    public void Refresh(IReadOnlyCollection<ChunkPos> reachable, Action<VoxelPos>? submerged)
    {
        ArgumentNullException.ThrowIfNull(reachable);
        Refreshes++;
        RebuiltLastPass = 0;
        _seen.Clear();

        foreach (ChunkPos chunk in reachable)
        {
            (int X, int Z) key = (chunk.X, chunk.Z);
            if (!_seen.Add(key))
            {
                continue;
            }

            Refresh(key, submerged);
        }

        _gone.Clear();
        foreach ((int X, int Z) key in _bands.Keys)
        {
            if (!_seen.Contains(key))
            {
                _gone.Add(key);
            }
        }

        foreach ((int X, int Z) key in _gone)
        {
            _bands.Remove(key);
        }

        Live.Invalidate();
    }

    /// <summary>
    /// Wakes every cell of this chunk's band, as a refresh does for a band that grew from nothing.
    /// For a chunk whose stored water landed after its column was covered: the arrival is the
    /// pipeline's, and a panel arrives after the chunk it belongs to.
    /// </summary>
    public void WakeBand(ChunkPos chunk, Action<VoxelPos> submerged)
    {
        ArgumentNullException.ThrowIfNull(submerged);
        ColumnBand? band = Live.Band(chunk.X, chunk.Z);
        if (band is null)
        {
            return;
        }

        int y0 = chunk.OriginVoxel.Y;
        int y1 = y0 + VoxelConstants.ChunkSize - 1;
        for (int lz = 0; lz < VoxelConstants.ChunkSize; lz++)
        {
            for (int lx = 0; lx < VoxelConstants.ChunkSize; lx++)
            {
                WokenSubmerged += band.Wake(lx, lz, y0 - 1, y1, chunk.OriginVoxel.X + lx, chunk.OriginVoxel.Z + lz, submerged);
            }
        }
    }

    /// <summary>The live ceiling frozen for a worker: the band arrays as they stand, never mutated after this.</summary>
    public LakeCeilingView Capture() => new(new Dictionary<(int X, int Z), ColumnBand>(_bands));

    private void Refresh((int X, int Z) key, Action<VoxelPos>? submerged)
    {
        int cell = _water.CellVoxels;
        int samplesPerChunk = VoxelConstants.ChunkSize / cell;
        int originX = key.X << VoxelConstants.ChunkSizeLog2;
        int originZ = key.Z << VoxelConstants.ChunkSizeLog2;

        // The sixteen sample tops, compared before anything is rebuilt: a lake standing still is
        // sixteen table reads and no allocation.
        Span<int> tops = stackalloc int[samplesPerChunk * samplesPerChunk];
        bool any = false;
        for (int sz = 0; sz < samplesPerChunk; sz++)
        {
            for (int sx = 0; sx < samplesPerChunk; sx++)
            {
                (int sampleX, int sampleZ) = _water.SampleOf(originX + (sx * cell), originZ + (sz * cell));
                double? table = _water.TableAt(sampleX, sampleZ);
                int top = table is { } t ? (int)Math.Floor(t) : NoLake;
                tops[(sz * samplesPerChunk) + sx] = top;
                any |= top != NoLake;
            }
        }

        _bands.TryGetValue(key, out ColumnBand? existing);
        if (!any)
        {
            if (existing is not null)
            {
                _bands.Remove(key);
            }

            return;
        }

        if (existing is not null && existing.SameTops(tops))
        {
            return;
        }

        var band = new ColumnBand(existing, originX, originZ, _seed, tops, samplesPerChunk, cell);
        _bands[key] = band;
        Rebuilt++;
        RebuiltLastPass++;
        Live.Invalidate();

        if (submerged is not null)
        {
            WokenSubmerged += band.WakeRisen(existing, originX, originZ, submerged);
        }
    }

    /// <summary>One chunk column's band: per voxel column the plane above the seed and the lake's top.</summary>
    internal sealed class ColumnBand
    {
        public const int Bytes = Columns * sizeof(int) * 2;

        private readonly int[] _floor;
        private readonly int[] _top;
        private readonly int[] _sampleTops;

        public ColumnBand(ColumnBand? previous, int originX, int originZ, ISurfaceHeights seed, ReadOnlySpan<int> tops, int samplesPerChunk, int cell)
        {
            // The seed never moves, so a rebuild keeps the previous floor rather than reading a
            // thousand surface heights again.
            _floor = previous?._floor ?? SeedFloor(originX, originZ, seed);
            _sampleTops = tops.ToArray();
            _top = new int[Columns];
            for (int lz = 0; lz < VoxelConstants.ChunkSize; lz++)
            {
                for (int lx = 0; lx < VoxelConstants.ChunkSize; lx++)
                {
                    int index = Index(lx, lz);
                    int top = tops[((lz / cell) * samplesPerChunk) + (lx / cell)];
                    _top[index] = top != NoLake && top >= _floor[index] ? top : NoLake;
                }
            }
        }

        public bool Contains(VoxelPos voxel)
        {
            int index = Index(voxel.X & VoxelConstants.ChunkMask, voxel.Z & VoxelConstants.ChunkMask);
            int top = _top[index];
            return top != NoLake && voxel.Y <= top && voxel.Y >= _floor[index];
        }

        public int TopAt(int lx, int lz) => _top[Index(lx, lz)];

        public bool SameTops(ReadOnlySpan<int> tops) => tops.SequenceEqual(_sampleTops);

        /// <summary>Calls back for every cell in this band that the previous band did not cover.</summary>
        public long WakeRisen(ColumnBand? previous, int originX, int originZ, Action<VoxelPos> submerged)
        {
            long woken = 0;
            for (int lz = 0; lz < VoxelConstants.ChunkSize; lz++)
            {
                for (int lx = 0; lx < VoxelConstants.ChunkSize; lx++)
                {
                    int index = Index(lx, lz);
                    int top = _top[index];
                    if (top == NoLake)
                    {
                        continue;
                    }

                    // From just above the previous top, or from the floor if there was none:
                    // (old, new], never [old, new) — the old top was covered already.
                    int oldTop = previous?._top[index] ?? NoLake;
                    int from = oldTop == NoLake ? _floor[index] : oldTop + 1;
                    for (int y = from; y <= top; y++)
                    {
                        submerged(new VoxelPos(originX + lx, y, originZ + lz));
                        woken++;
                    }
                }
            }

            return woken;
        }

        /// <summary>Calls back for every cell of this voxel column's band with <c>yAbove &lt; Y ≤ yMax</c>.</summary>
        public long Wake(int lx, int lz, int yAbove, int yMax, int worldX, int worldZ, Action<VoxelPos> submerged)
        {
            int index = Index(lx, lz);
            int top = _top[index];
            if (top == NoLake)
            {
                return 0;
            }

            int from = Math.Max(_floor[index], yAbove + 1);
            int to = Math.Min(top, yMax);
            for (int y = from; y <= to; y++)
            {
                submerged(new VoxelPos(worldX, y, worldZ));
            }

            return Math.Max(0, to - from + 1);
        }

        private static int[] SeedFloor(int originX, int originZ, ISurfaceHeights seed)
        {
            var floor = new int[Columns];
            for (int lz = 0; lz < VoxelConstants.ChunkSize; lz++)
            {
                for (int lx = 0; lx < VoxelConstants.ChunkSize; lx++)
                {
                    floor[Index(lx, lz)] = seed.SurfaceHeightAt(originX + lx, originZ + lz) + 1;
                }
            }

            return floor;
        }

        private static int Index(int lx, int lz) => (lz * VoxelConstants.ChunkSize) + lx;
    }
}

/// <summary>
/// The bands of a <see cref="LakeCeiling"/>, readable: the live set through
/// <see cref="LakeCeiling.Live"/>, or a copy frozen at a tick boundary through
/// <see cref="LakeCeiling.Capture"/> — what a worker reads. Answers for the columns it holds and
/// nothing else; a column with no band is not in a lake.
/// </summary>
/// <remarks>
/// <b>Misses are cached as well as hits.</b> Most columns have no band, and a miss that is not
/// cached is a dictionary lookup on every <c>CanHold</c>.
/// </remarks>
public sealed class LakeCeilingView
{
    private readonly Dictionary<(int X, int Z), LakeCeiling.ColumnBand> _bands;
    private int _cacheX, _cacheZ;
    private LakeCeiling.ColumnBand? _cache;
    private bool _cached;

    internal LakeCeilingView(Dictionary<(int X, int Z), LakeCeiling.ColumnBand> bands)
    {
        _bands = bands;
    }

    /// <summary>Chunk columns with a band.</summary>
    public int ChunkColumns => _bands.Count;

    /// <summary>See <see cref="LakeCeiling.InLake"/>.</summary>
    public bool InLake(VoxelPos voxel)
    {
        int cx = voxel.X >> VoxelConstants.ChunkSizeLog2;
        int cz = voxel.Z >> VoxelConstants.ChunkSizeLog2;
        if (!_cached || _cacheX != cx || _cacheZ != cz)
        {
            _bands.TryGetValue((cx, cz), out _cache);
            _cacheX = cx;
            _cacheZ = cz;
            _cached = true;
        }

        return _cache is not null && _cache.Contains(voxel);
    }

    internal LakeCeiling.ColumnBand? Band(int chunkX, int chunkZ)
    {
        if (!_cached || _cacheX != chunkX || _cacheZ != chunkZ)
        {
            _bands.TryGetValue((chunkX, chunkZ), out _cache);
            _cacheX = chunkX;
            _cacheZ = chunkZ;
            _cached = true;
        }

        return _cache;
    }

    /// <summary>The live set changed under this view: forget the cached column.</summary>
    internal void Invalidate() => _cached = false;
}

/// <summary>
/// An <see cref="IFluidTerrain"/> over an <see cref="IFluidTerrain"/> that keeps Tier B out of Tier
/// A's lakes — the field's own shape, which is why nothing else changes: the simulation, every
/// fixture and every bench arm take a terrain and cannot tell which they were given.
/// </summary>
/// <remarks>
/// Wraps the live terrain on the sim-thread arm, the <see cref="FluidField"/> on the field arm,
/// and a captured <see cref="FluidFieldView"/> with a captured <see cref="LakeCeilingView"/> on
/// the job arm — the inner is asked first on every question, so the bits are baked without the
/// band and a level moving a voxel costs no rebake. <see cref="Inner"/> is exposed for the one
/// caller that wants the view's own counters at collection.
/// </remarks>
public sealed class LakeBoundedTerrain : IFluidTerrain
{
    private readonly LakeCeilingView _bands;

    public LakeBoundedTerrain(IFluidTerrain inner, LakeCeiling ceiling)
        : this(inner, (ceiling ?? throw new ArgumentNullException(nameof(ceiling))).Live)
    {
    }

    public LakeBoundedTerrain(IFluidTerrain inner, LakeCeilingView ceiling)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(ceiling);
        Inner = inner;
        _bands = ceiling;
    }

    /// <summary>The terrain under the lakes.</summary>
    public IFluidTerrain Inner { get; }

    public bool CanHold(VoxelPos voxel) => Inner.CanHold(voxel) && !_bands.InLake(voxel);

    public bool IsLoaded(VoxelPos voxel) => Inner.IsLoaded(voxel);

    /// <summary>In the lake: the inner terrain would hold water here and the band covers it.</summary>
    public bool InLake(VoxelPos voxel) => Inner.CanHold(voxel) && _bands.InLake(voxel);
}
