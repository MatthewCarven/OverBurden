using Overburden.Core.Content;
using Overburden.Core.Lighting;
using Overburden.Core.Voxels;

namespace Overburden.Core.Fluid;

/// <summary>
/// Whether a column is walled off from the outside at a level: <b>the player's material encloses,
/// nature's does not</b> (Matthew, 2026-09-20 — the courtyard).
/// </summary>
/// <remarks>
/// <para>
/// <b>What it answers.</b> A walled pool on grass inside a basin is, to Tier A, part of that basin:
/// the watershed is analysed on the seed heights and edits never change it (M7's locked decision),
/// so the hand-off reads the seed plane and hands the pool to the lake outside, and once the lake
/// stands above the courtyard's floor its plane draws through the walls and
/// <see cref="LakeCeiling"/> puts the courtyard's air inside the lake. <i>"The water sheet appears
/// to have become attached to the one outside … limit this to sheets without rock/dirt or similar
/// between them."</i> Tier A cannot see a wall, so this is the one predicate its three readers ask
/// instead: a 4-neighbour flood over the voxel columns at one level from the column asked about,
/// in which <b>only the player's material is an obstacle</b> — a solid voxel above the seed
/// surface of its column, which is how it differs from the seed, or natural rock with such a
/// voxel standing on it, because a wall claims the ground under it — and natural rock with
/// nothing on it is walked through as air is. The flood is <i>enclosed</i> if it runs out of cells before reaching the bound, and
/// <i>open</i> if it reaches the bound, an unloaded voxel, or a cell more than
/// <see cref="BoundVoxels"/> from where it started.
/// </para>
/// <para>
/// <b>Costs stated.</b> A natural pit below the lake stays the lake's, by Tier A's own model — it
/// is underwater. Three placed walls and a hillside read as open, because the hill is nature's and
/// the flood walks through it; the fourth wall is the fix, a rule a player can learn — and it may
/// stand on the hill, since a wall claims its footing (the first version had no footing rule, and
/// the headless courtyard on the spawn's slope leaked at every level below its uphill wall's
/// ground). An enclosure wider than <c>2 × BoundVoxels</c> reads as open. A wall with a gap at
/// the water's level reads as open — the flood finds the gap, and so does the water. Below the
/// seed surface a placed voxel cannot be told from rock, so a wall built up from the floor of a
/// dug pit encloses only above grade — and a dug pit's water is never on the plane, so it was
/// never handed. And the answer is per level: a wall solid at the lake's top and open a metre
/// below it encloses at the top and not below, which is where the water would get in.
/// </para>
/// <para>
/// <b>Cached per (x, z, level), the flood's whole visited set sharing one answer</b>, and dropped
/// per chunk column by the pipeline's hooks: an edit at a level drops every region at that level
/// whose cells or walls stand in the edited chunk's column; a chunk arriving with edits, or one a
/// region reached as unloaded, drops the regions in its column; a forget drops them too. An open
/// flood is the expensive answer — up to <c>(2 × BoundVoxels + 1)²</c> cells — so a column with no
/// edited chunk within the bound is answered open with no flood and no cache: no edits, no walls,
/// nothing to enclose. That is every column of the r6 control and every oracle that places
/// nothing, and it is what keeps the predicate free where it has nothing to say.
/// </para>
/// <para>
/// <b>Not a fluid rule.</b> The automaton never asks this; it is asked at the boundary by the
/// hand-off (a rested cell at its own level), by the ceiling (a column at the lake's top) and by
/// the surface sweep (a sample at the lake's level), all on the sim thread, all through one
/// definition. <c>Overburden.Core</c> still references nothing of Godot.
/// </para>
/// </remarks>
public sealed class Enclosure
{
    /// <summary>
    /// How far a flood may run from its start before reading as open — 64 voxels, 16 m, so an
    /// enclosure up to 32 m across is seen whole. <b>An estimate</b>: the box it bounds is 16,641
    /// cells, a millisecond or two of reads on the open answer, and the widest thing anyone has
    /// walled is a courtyard.
    /// </summary>
    public const int BoundVoxels = 64;

    /// <summary>
    /// The most regions the cache holds before the oldest are evicted, ~3 KB each — 4,096 is
    /// ~12 MB. <b>An estimate, and load-bearing</b>: without a cap the first played session
    /// (2026-09-21) cached 5,884 regions — one per level the lake's top passed through and per
    /// level the surface sweep asked at, nothing but an edit ever dropping one — as 16k
    /// dictionary entries each, over 4 GB, and the process died of a null from Godot's
    /// allocator. A region is a bitset over its box now (2 KB) and the cap is by count; at
    /// 512k cells the first cap thrashed on the flooded world at 200 floods a second, because a
    /// lake needs a region per 32 m of its footprint at every level it stands at.
    /// </summary>
    public const int MaxRegions = 4096;

    /// <summary>A region's box is the bound either side of its start: this many cells a side.</summary>
    private const int BoxSide = (2 * BoundVoxels) + 1;

    /// <summary>Chunk columns either side of the start the pre-check looks at for edits: the bound in chunks, rounded up.</summary>
    private const int PrecheckChunks = (BoundVoxels + VoxelConstants.ChunkSize - 1) / VoxelConstants.ChunkSize;

    private readonly IVoxelWorld _world;
    private readonly MaterialTable _materials;
    private readonly ISurfaceHeights _seed;

    private readonly Dictionary<ChunkPos, bool> _edited = [];
    private readonly Dictionary<(int X, int Z), int> _editedColumns = [];
    private readonly Dictionary<(int X, int Z), (long Version, bool Quiet)> _quiet = [];
    private long _editVersion;

    private readonly Dictionary<int, Region> _regions = [];
    private readonly Dictionary<(int X, int Z, int Y), List<int>> _index = [];
    private readonly Dictionary<(int X, int Z), HashSet<int>> _byColumn = [];
    private readonly Queue<int> _order = new();
    private int _nextRegion = 1;

    // The flood's scratch, reused: a queue and the visited bits of one flood over its box.
    private readonly Queue<(int X, int Z)> _queue = new();
    private readonly System.Collections.BitArray _visited = new(BoxSide * BoxSide);
    private readonly HashSet<(int X, int Z)> _columns = [];
    private readonly HashSet<(int X, int Z)> _unloaded = [];
    private int _cells;

    public Enclosure(IVoxelWorld world, MaterialTable materials, ISurfaceHeights seed)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentNullException.ThrowIfNull(seed);
        _world = world;
        _materials = materials;
        _seed = seed;
    }

    /// <summary>
    /// Called with a chunk column and a level range whenever an answer in that column at a level
    /// in the range may have changed — an edit there, a chunk with edits arriving or leaving, a
    /// cached region dropped — so a reader that keeps derived state per chunk column (the
    /// ceiling's bands) can rebuild what the range touches and nothing else.
    /// </summary>
    public event Action<(int X, int Z), int, int>? Changed;

    /// <summary>Floods run, over the lifetime.</summary>
    public long Floods { get; private set; }

    /// <summary>
    /// Why the open floods opened, over the lifetime — the instrument a pond that will not fill
    /// wants (2026-09-22, *"water doesn't seem to be building up in my clay pond"*): reached
    /// <see cref="BoundVoxels"/> from where they started, and of those how many walked through
    /// natural ground at the level on the way (a hillside standing in for a wall) against only
    /// through air (a gap, or a pond wider than the bound); or reached a chunk that was not
    /// loaded. A flood counts once, under the first reason it met.
    /// </summary>
    public long OpenAtBound { get; private set; }

    public long OpenAtBoundThroughGround { get; private set; }

    public long OpenAtUnloaded { get; private set; }

    /// <summary>The furthest an open flood ran from its start before it opened, in voxels, over the lifetime.</summary>
    public int FarthestOpen { get; private set; }

    /// <summary>The furthest a closed flood reached from its start, in voxels, over the lifetime — how big the biggest pond was.</summary>
    public int FarthestClosed { get; private set; }

    private bool _throughGround;
    private int _reach;

    /// <summary>Of those, floods that ran out of cells — an enclosure found.</summary>
    public long EnclosedFloods { get; private set; }

    /// <summary>Cells visited by floods, over the lifetime.</summary>
    public long CellsVisited { get; private set; }

    /// <summary>Asks answered from a cached region.</summary>
    public long CacheHits { get; private set; }

    /// <summary>Asks answered open with no flood because no edited chunk stood within the bound.</summary>
    public long Quiet { get; private set; }

    /// <summary>Regions dropped by an edit, an arrival or a forget, over the lifetime.</summary>
    public long Dropped { get; private set; }

    /// <summary>Regions evicted for the cache's cap, oldest first, over the lifetime — nothing changed, so no reader is told.</summary>
    public long Evicted { get; private set; }

    /// <summary>Regions cached now.</summary>
    public int Regions => _regions.Count;

    /// <summary>Cells cached now, across every region.</summary>
    public long CachedCells { get; private set; }

    /// <summary>Chunks known to hold edits.</summary>
    public int EditedChunks => _edited.Count;

    /// <summary>
    /// True when the column at (x, z) is walled off from the outside at level <paramref name="y"/>
    /// — the voxel layer water at that level stands in. A start that is not air (rock, a placed
    /// block) is not enclosed: the question is about the air, and a block's own column keeps
    /// whatever the readers give it.
    /// </summary>
    public bool IsEnclosed(int x, int z, int y)
    {
        if (_index.TryGetValue((x >> VoxelConstants.ChunkSizeLog2, z >> VoxelConstants.ChunkSizeLog2, y), out List<int>? ids))
        {
            foreach (int id in ids)
            {
                if (_regions[id].Covers(x, z))
                {
                    CacheHits++;
                    return _regions[id].Enclosed;
                }
            }
        }

        if (IsQuiet(x >> VoxelConstants.ChunkSizeLog2, z >> VoxelConstants.ChunkSizeLog2))
        {
            Quiet++;
            return false;
        }

        if (!_world.TryGetMaterial(new VoxelPos(x, y, z), out int start) || start != MaterialIds.AirNumericId)
        {
            return false;
        }

        return Flood(x, z, y);
    }

    /// <summary>The pipeline's arrival hook: a chunk with edits may hold walls; one a region reached as unloaded changes its answer either way.</summary>
    public void ChunkArrived(ChunkPos chunk, bool hasEdits)
    {
        SetEdited(chunk, hasEdits);
        DropRegionsIn((chunk.X, chunk.Z), hasEdits ? null : chunk);
        if (hasEdits)
        {
            Changed?.Invoke((chunk.X, chunk.Z), chunk.OriginVoxel.Y, chunk.OriginVoxel.Y + VoxelConstants.ChunkSize - 1);
        }
    }

    /// <summary>The pipeline's forget hook: the column's regions read a world that is gone.</summary>
    public void ChunkForgotten(ChunkPos chunk)
    {
        bool hadEdits = _edited.ContainsKey(chunk);
        SetEdited(chunk, false);
        DropRegionsIn((chunk.X, chunk.Z), null);
        if (hadEdits)
        {
            Changed?.Invoke((chunk.X, chunk.Z), chunk.OriginVoxel.Y, chunk.OriginVoxel.Y + VoxelConstants.ChunkSize - 1);
        }
    }

    /// <summary>
    /// The pipeline's edit hook: the voxel's chunk holds edits now, and every region at its level
    /// standing in its column is stale — and, for a voxel that is its column's footing (the first
    /// above the seed), every region at any level below it too, because a wall's footing is what
    /// makes its ground an obstacle at every level under it.
    /// </summary>
    public void VoxelChanged(VoxelPos voxel)
    {
        SetEdited(voxel.Chunk, true);
        (int X, int Z) column = (voxel.Chunk.X, voxel.Chunk.Z);
        int seed = _seed.SurfaceHeightAt(voxel.X, voxel.Z);
        if (voxel.Y == seed + 1)
        {
            DropRegionsIn(column, null, yMin: int.MinValue, yMax: voxel.Y);
            Changed?.Invoke(column, int.MinValue, voxel.Y);
        }
        else
        {
            DropRegionsIn(column, null, yMin: voxel.Y, yMax: voxel.Y);
            Changed?.Invoke(column, voxel.Y, voxel.Y);
        }
    }

    /// <summary>Drops every cached region. For a test or a lattice change; the hooks do the incremental work.</summary>
    public void Clear()
    {
        foreach (int id in _regions.Keys.ToArray())
        {
            Drop(id);
        }
    }

    private void SetEdited(ChunkPos chunk, bool hasEdits)
    {
        bool was = _edited.TryGetValue(chunk, out bool edited) && edited;
        if (was == hasEdits)
        {
            return;
        }

        (int X, int Z) column = (chunk.X, chunk.Z);
        if (hasEdits)
        {
            _edited[chunk] = true;
            _editedColumns[column] = _editedColumns.GetValueOrDefault(column) + 1;
        }
        else
        {
            _edited.Remove(chunk);
            int left = _editedColumns.GetValueOrDefault(column) - 1;
            if (left <= 0)
            {
                _editedColumns.Remove(column);
            }
            else
            {
                _editedColumns[column] = left;
            }
        }

        _editVersion++;
    }

    /// <summary>No edited chunk in any column within the bound of this one: nothing can be a wall, so every answer is open without a flood.</summary>
    private bool IsQuiet(int cx, int cz)
    {
        if (_quiet.TryGetValue((cx, cz), out (long Version, bool Quiet) cached) && cached.Version == _editVersion)
        {
            return cached.Quiet;
        }

        bool quiet = true;
        for (int dz = -PrecheckChunks; dz <= PrecheckChunks && quiet; dz++)
        {
            for (int dx = -PrecheckChunks; dx <= PrecheckChunks; dx++)
            {
                if (_editedColumns.ContainsKey((cx + dx, cz + dz)))
                {
                    quiet = false;
                    break;
                }
            }
        }

        _quiet[(cx, cz)] = (_editVersion, quiet);
        return quiet;
    }

    private bool Flood(int startX, int startZ, int y)
    {
        Floods++;
        _queue.Clear();
        _visited.SetAll(false);
        _walls.Clear();
        _columns.Clear();
        _unloaded.Clear();
        _cells = 0;
        int originX = startX - BoundVoxels;
        int originZ = startZ - BoundVoxels;

        bool open = false;
        bool atBound = false, atUnloaded = false;
        _throughGround = false;
        _reach = 0;
        _queue.Enqueue((startX, startZ));
        _visited[Bit(originX, originZ, startX, startZ)] = true;
        while (_queue.TryDequeue(out (int X, int Z) cell))
        {
            _cells++;
            _columns.Add((cell.X >> VoxelConstants.ChunkSizeLog2, cell.Z >> VoxelConstants.ChunkSizeLog2));
            int reach = Math.Max(Math.Abs(cell.X - startX), Math.Abs(cell.Z - startZ));
            _reach = Math.Max(_reach, reach);
            if (reach >= BoundVoxels)
            {
                // The bound: whatever is past it, this flood cannot say the walls close.
                open = true;
                atBound = true;
                continue;
            }

            bool wasOpen = open;

            Step(cell.X + 1, cell.Z, y, originX, originZ, ref open);
            Step(cell.X - 1, cell.Z, y, originX, originZ, ref open);
            Step(cell.X, cell.Z + 1, y, originX, originZ, ref open);
            Step(cell.X, cell.Z - 1, y, originX, originZ, ref open);
            if (open && !wasOpen)
            {
                atUnloaded = true;
            }
        }

        CellsVisited += _cells;
        if (!open)
        {
            EnclosedFloods++;
            FarthestClosed = Math.Max(FarthestClosed, _reach);
        }
        else
        {
            FarthestOpen = Math.Max(FarthestOpen, _reach);
            if (atBound)
            {
                OpenAtBound++;
                if (_throughGround)
                {
                    OpenAtBoundThroughGround++;
                }
            }
            else if (atUnloaded)
            {
                OpenAtUnloaded++;
            }
        }

        // The region: every cell visited shares the answer, as a bitset over the flood's box;
        // every chunk column a cell or a wall stood in, and every column found unloaded, can
        // drop it. Walls were marked visited too, so the bits are cleared for them here: a wall's
        // own column is answered by the readers, never by a region.
        int id = _nextRegion++;
        var region = new Region(y, !open, originX, originZ, new System.Collections.BitArray(_visited), _cells, [.. _columns], [.. _unloaded]);
        foreach ((int X, int Z) wall in _walls)
        {
            region.Bits[Bit(originX, originZ, wall.X, wall.Z)] = false;
        }

        _regions[id] = region;
        CachedCells += _cells;
        for (int cz = originZ >> VoxelConstants.ChunkSizeLog2; cz <= (originZ + BoxSide - 1) >> VoxelConstants.ChunkSizeLog2; cz++)
        {
            for (int cx = originX >> VoxelConstants.ChunkSizeLog2; cx <= (originX + BoxSide - 1) >> VoxelConstants.ChunkSizeLog2; cx++)
            {
                if (!_index.TryGetValue((cx, cz, y), out List<int>? ids))
                {
                    ids = [];
                    _index[(cx, cz, y)] = ids;
                }

                ids.Add(id);
            }
        }

        foreach ((int X, int Z) column in region.Columns)
        {
            Index(column, id);
        }

        foreach ((int X, int Z) column in region.Unloaded)
        {
            Index(column, id);
        }

        _order.Enqueue(id);
        while (_regions.Count > MaxRegions && _order.TryDequeue(out int oldest))
        {
            if (oldest != id && _regions.ContainsKey(oldest))
            {
                Drop(oldest, notify: false);
                Evicted++;
            }
        }

        return !open;
    }

    private static int Bit(int originX, int originZ, int x, int z) => ((z - originZ) * BoxSide) + (x - originX);

    private readonly List<(int X, int Z)> _walls = [];

    private void Step(int x, int z, int y, int originX, int originZ, ref bool open)
    {
        int bit = Bit(originX, originZ, x, z);
        if (_visited[bit])
        {
            return;
        }

        _visited[bit] = true;

        if (!_world.TryGetMaterial(new VoxelPos(x, y, z), out int material))
        {
            // Unknown, not open air — but the walls cannot be known to close either, and that
            // is open. Remembered per column, so the arrival changes the answer.
            _unloaded.Add((x >> VoxelConstants.ChunkSizeLog2, z >> VoxelConstants.ChunkSizeLog2));
            open = true;
            return;
        }

        int seed = _seed.SurfaceHeightAt(x, z);
        bool solid = _materials.IsSolid(material);
        bool wall = solid && seed < y;
        if (!wall && seed >= y && solid)
        {
            // Natural rock at this level. Walked through — unless a wall stands on it: the
            // player's wall claims the ground under it, so on a slope the uphill wall holds the
            // water at every level below its own footing, as the ground under a real wall does.
            // One read, the first voxel above the seed, because a brush lands on the lattice and
            // fills every air voxel of its cell, so a wall on the ground is solid there. Placed
            // rock below the seed is nature's, stated.
            wall = _world.TryGetMaterial(new VoxelPos(x, seed + 1, z), out int footing) && _materials.IsSolid(footing);
        }

        if (wall)
        {
            // Not a cell of the region — its bit is cleared when the region is made — but its
            // column can drop the region, because taking the wall down is what re-opens it.
            _columns.Add((x >> VoxelConstants.ChunkSizeLog2, z >> VoxelConstants.ChunkSizeLog2));
            _walls.Add((x, z));
            return;
        }

        if (solid)
        {
            // Natural ground at the level with nothing on it: walked through, and remembered, so
            // an open flood can say whether a hillside was what it walked out through.
            _throughGround = true;
        }

        _queue.Enqueue((x, z));
    }

    private void Index((int X, int Z) column, int id)
    {
        if (!_byColumn.TryGetValue(column, out HashSet<int>? ids))
        {
            ids = [];
            _byColumn[column] = ids;
        }

        ids.Add(id);
    }

    /// <summary>
    /// Drops the regions indexed under a chunk column: all of them, or only those at one level,
    /// or — for an arrival with no edits — only those that reached the column as unloaded.
    /// </summary>
    private void DropRegionsIn((int X, int Z) column, ChunkPos? onlyIfUnloaded, int yMin = int.MinValue, int yMax = int.MaxValue)
    {
        if (!_byColumn.TryGetValue(column, out HashSet<int>? ids) || ids.Count == 0)
        {
            return;
        }

        foreach (int id in ids.ToArray())
        {
            Region region = _regions[id];
            if (region.Y < yMin || region.Y > yMax)
            {
                continue;
            }

            if (onlyIfUnloaded is not null && !region.Unloaded.Contains(column))
            {
                continue;
            }

            Drop(id);
        }
    }

    private void Drop(int id, bool notify = true)
    {
        Region region = _regions[id];
        _regions.Remove(id);
        if (notify)
        {
            Dropped++;
        }

        CachedCells -= region.CellCount;
        for (int cz = region.OriginZ >> VoxelConstants.ChunkSizeLog2; cz <= (region.OriginZ + BoxSide - 1) >> VoxelConstants.ChunkSizeLog2; cz++)
        {
            for (int cx = region.OriginX >> VoxelConstants.ChunkSizeLog2; cx <= (region.OriginX + BoxSide - 1) >> VoxelConstants.ChunkSizeLog2; cx++)
            {
                if (_index.TryGetValue((cx, cz, region.Y), out List<int>? ids))
                {
                    ids.Remove(id);
                    if (ids.Count == 0)
                    {
                        _index.Remove((cx, cz, region.Y));
                    }
                }
            }
        }

        foreach ((int X, int Z) column in region.Columns)
        {
            Unindex(column, id);
            if (notify)
            {
                Changed?.Invoke(column, region.Y, region.Y);
            }
        }

        foreach ((int X, int Z) column in region.Unloaded)
        {
            Unindex(column, id);
        }
    }

    private void Unindex((int X, int Z) column, int id)
    {
        if (_byColumn.TryGetValue(column, out HashSet<int>? ids))
        {
            ids.Remove(id);
            if (ids.Count == 0)
            {
                _byColumn.Remove(column);
            }
        }
    }

    /// <summary>One flood's answer: the level, the verdict, and a bitset over the box round its start of the cells that share it.</summary>
    private sealed record Region(int Y, bool Enclosed, int OriginX, int OriginZ, System.Collections.BitArray Bits, int CellCount, HashSet<(int X, int Z)> Columns, HashSet<(int X, int Z)> Unloaded)
    {
        public bool Covers(int x, int z)
        {
            int lx = x - OriginX, lz = z - OriginZ;
            return lx >= 0 && lx < BoxSide && lz >= 0 && lz < BoxSide && Bits[(lz * BoxSide) + lx];
        }
    }
}
