using Overburden.Core.Voxels;

namespace Overburden.Core.Fluid;

/// <summary>
/// Tier B's water as geometry (M8d-1): merged tops, a side wherever a wet cell meets air or a
/// lower fill, a bottom under a cell falling through air, and per vertex the water's depth and
/// where the surface stood at the previous build.
/// </summary>
/// <remarks>
/// <para>
/// <b>It replaces the debug surface's one quad per surface cell, and covers the same cells.</b>
/// The debug view drew a top on every held cell with nothing above it, at the cell's fill height,
/// and M8d's pass named three absences in that: no vertical face where water meets air, no depth
/// in the colour, no motion between ticks. This is the mesh with all three supplied as geometry
/// and vertex data; what the shader does with them is M8d-2's. The tops still land on exactly the
/// cells <see cref="FluidGrid.SurfaceCells"/> yields — a test enumerates the merged quads back to
/// cells and matches them against the walk — so the debug view's oracle survives the merge.
/// </para>
/// <para>
/// <b>Tops merge greedily per panel and per layer on a key of (fill, depth, previous height).</b>
/// M5's rule: everything a vertex carries is in the merge key, because a merged quad only
/// interpolates its four corners, and the three are constant across a run so the interpolation is
/// exact to <see cref="ToleranceMillilitres"/> — 2 mm — because a levelled sheet is level to a
/// millilitre, not to zero, and a key on the raw amount would split the played lake into its
/// millilitre contours and draw a sliver between every pair of them. A lake at rest
/// over a flat floor is one quad a panel; a lake over a stepped floor merges where the depth is
/// equal and breaks where it is not; a rising lake merges while it rises level.
/// That is the cost stated in the plan — far fewer quads on a sheet, more on a stair — and the
/// bench prints both shapes.
/// </para>
/// <para>
/// <b>Sides are over every wet cell, not only surface cells.</b> A falling column, a front at a
/// cliff's edge and water paused at the loaded world's edge all have wet cells with air beside
/// them and water above them, and a surface-only rule would draw a waterfall as its topmost cell.
/// A side is drawn from the cell that holds <i>more</i>, between the neighbour's fill height and
/// its own, so two cells at equal fill share no face and a step of one millilitre draws a sliver.
/// A neighbour that is rock, or that the terrain cannot answer for, gets no face: the rock's own
/// face is there already and the two would fight for the pixel. A cell with water above it is
/// drawn full — the automaton holds a falling cell part-full with air above, and drawn as held a
/// falling column is a stack of slabs with the ground showing through the slots. Sides merge along
/// the run of their wall; bottoms do not merge, being a drop's underside and transient.
/// </para>
/// <para>
/// <b>A cell under a lake cell has no top either (M8d-5).</b> A bite in a lake's bed is below the
/// seed surface, so Tier B fills it only to the seed, and the cells from there to the level are
/// the lake's — Tier A's water, drawn by Tier A's plane. The water goes on upward, so the cell is
/// drawn full with no surface, exactly as a cell with Tier B water above it is; the first
/// screenshot of a chunk bitten out of a lake's centre had Tier B's top sitting a metre down inside
/// a hole in the plane, dark for its eight metres of column. Asked of the terrain's
/// <see cref="IFluidTerrain.InLake"/>, for the cell above a would-be surface cell only.
/// </para>
/// <para>
/// <b>The previous height is looked up in the previous build's surface, with one honest hole.</b>
/// A surface cell's previous height is where its column's surface stood last build: the same cell
/// if it was a surface then, the nearest surface above it within <see cref="FallSearchCells"/> if
/// the surface fell into this one (M8d-3's rule falls up to sixteen cells a tick, and a fallen
/// surface should come down from where it was), the cell below if it rose out of that one, and
/// the cell's own floor otherwise — so a cell that has just got wet rises from its floor, which is
/// the plan's stated default. A cell that emptied has no vertex and vanishes at the tick. The previous surface is kept per panel in index order, so the tops read it with a
/// cursor and only the sides search it; its cost is on the bench beside the walk's.
/// </para>
/// <para>
/// <b>The depth is the column's, not the optical path</b> (decision 2): contiguous wet cells under
/// the vertex, carried across panel boundaries by building panels bottom-up, capped at
/// <see cref="MaxDepthCells"/>. A pool over a flooded cave reads as its column.
/// </para>
/// <para>
/// <b>A panel is rebuilt only when something it reads has moved (2026-09-21).</b> Until then every
/// build walked every held panel and handed back one mesh for the whole wet surface — 40 MiB of
/// arrays four times a second on a 50 ML world, whether or not anything moved, the largest
/// main-thread term and the garbage the crash log's managed heap was climbing on. <see cref="Update"/>
/// keeps a record per panel and rebuilds a panel when: its own <see cref="FluidGrid"/> stamp
/// moved (an amount written to a new value, or a <see cref="FluidGrid.Touch"/> for terrain that
/// changed beside it); a face neighbour's did, or a neighbour was released — the sides at the
/// shared face are drawn from whichever cell holds more, and the cell above the top layer and
/// below the bottom one decide a top and a bottom; a panel in the column below it did, up to
/// <see cref="ColumnReach"/> panels down — the depth attribute is the contiguous wet cells under
/// a vertex, carried across panel floors, so a change at the bottom of a flooded shaft darkens
/// the top; the lake band over its column was rebuilt (<see cref="IFluidTerrain.LakeStamp"/>) —
/// a lake draining off bed water exposes a top no cell write announces; or a previous surface it
/// reads changed at the last build — its own, so a panel whose water moved is built once more
/// with its previous equal to its current and the <c>tick_phase</c> lerp settles instead of
/// replaying at every install; or a neighbour's, because a side's bottom edge and a fallen
/// surface look up where the water stood in the panels beside and above. (Two rules were tried
/// before this one: <i>a vertex with its previous different from its current</i>, which a side
/// against a dry cell under a surface never satisfies, so a stepped lake rebuilt forever; and
/// <i>the arrays differ from the build before</i>, which is true of the settle build itself and
/// so built every change twice.) Everything else keeps the mesh it has. The rule is exact, not a heuristic: a mesher with
/// <see cref="ForceFull"/> set is the pre-2026-09-21 arm, and a test holds the two equal quad for
/// quad, attributes included, across a flood with bites, drains, a lake moving and a chunk leaving.
/// </para>
/// <para>
/// Stateful in the previous surface, the per-panel records and its scratch buffers, so one
/// instance serves one simulation and is used by one thread at a time — the worker on the job
/// arm, the sim thread on the other, never both, which is the grid's own rule.
/// </para>
/// </remarks>
public sealed class FluidSurfaceMesher
{
    /// <summary>
    /// Panels below a changed one that are rebuilt for the depth carry: enough to cover
    /// <see cref="MaxDepthCells"/> wet cells in a column, so a change that far down still reaches
    /// the top's attribute exactly.
    /// </summary>
    public const int ColumnReach = (MaxDepthCells + VoxelConstants.ChunkSize - 1) / VoxelConstants.ChunkSize;

    /// <summary>Wet cells under a vertex are counted up to this and no further; 63.75 m of water is as dark as it gets.</summary>
    public const int MaxDepthCells = 255;

    /// <summary>
    /// How far up a column the previous surface is looked for when a cell has none of its own:
    /// the most a visit can fall (M8d-3), so a surface that fell lerps down from where it was.
    /// </summary>
    public const int FallSearchCells = FluidSimulation.MaxFallCells;

    /// <summary>
    /// The merge's tolerance in millilitres: 2 mm of height. A top run extends over a cell whose
    /// fill (and previous fill) is within this of the run's first cell, and every cell in the run
    /// is drawn at the first cell's height. So the millilitre the settle leaves between neighbours,
    /// and the four the automaton's own rest does, do not break a run — where a key on the raw
    /// amount, or on any fixed rounding of it, splits a sheet along whichever contour the level
    /// happens to sit on. Sides are then drawn between <i>drawn</i> heights, exactly: two runs a
    /// millimetre apart get a sliver, because a crack that thin is still a pixel wide at a grazing
    /// angle. Cost: a run's drawn height is its first cell's, within this of every other.
    /// </summary>
    public const int ToleranceMillilitres = 125;

    private const int N = VoxelConstants.ChunkSize;
    private const int LayerCells = N * N;
    private const int Cap = FluidConstants.CellCapacityMillilitres;
    private const float Size = VoxelConstants.VoxelSize;

    // Index strides in a panel's [z][x][y] storage.
    private const int StrideX = N;
    private const int StrideZ = N * N;

    // Per-panel scratch. _topKeys is written for surface cells and zeroed as the greedy pass
    // consumes them, so it never needs clearing; _depthBelow is written for every wet cell and
    // read only for wet cells of the same panel, so neither does it.
    private readonly long[] _topKeys = new long[VoxelConstants.ChunkVolume];
    private readonly byte[] _depthBelow = new byte[VoxelConstants.ChunkVolume];

    // What each wet cell is drawn at: its own amount, or for a surface cell the amount and
    // previous of the run that took it — written in pass 1, overwritten in pass 2, read in pass
    // 3, so a side meets the top it borders exactly. A non-surface cell's previous is the
    // sentinel and is looked up when a side needs it.
    private readonly ushort[] _drawnAmount = new ushort[VoxelConstants.ChunkVolume];
    private readonly int[] _drawnPrev = new int[VoxelConstants.ChunkVolume];
    private const int NoPrev = int.MinValue;
    private readonly bool[] _layerHasSurface = new bool[N];
    private readonly bool[] _layerWet = new bool[N];
    private readonly int[] _depthCarry = new int[LayerCells];

    // The previous build's surface, per panel, as parallel arrays of local index and amount in
    // index order — which is pass 1's own order, so the tops read it with a cursor that only
    // advances, and only the sides (the perimeter) pay a binary search. A dictionary of cells was
    // the first version and cost more than the walk itself on the played lake's shape: 147,456
    // inserts and as many lookups a tick, against two sequential arrays.
    private sealed class PanelSurface
    {
        public int[] Index = new int[256];
        public ushort[] Amount = new ushort[256];
        public int Count;

        public void Add(int index, int amount)
        {
            if (Count == Index.Length)
            {
                Array.Resize(ref Index, Count * 2);
                Array.Resize(ref Amount, Count * 2);
            }

            Index[Count] = index;
            Amount[Count] = (ushort)amount;
            Count++;
        }

        /// <summary>Whether this surface is the other, entry for entry.</summary>
        public bool SameAs(PanelSurface other)
        {
            if (other.Count != Count)
            {
                return false;
            }

            for (int i = 0; i < Count; i++)
            {
                if (Index[i] != other.Index[i] || Amount[i] != other.Amount[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>The amount at a local index, or −1. Binary search over the sorted indices.</summary>
        public int Find(int index)
        {
            int at = Array.BinarySearch(Index, 0, Count, index);
            return at < 0 ? -1 : Amount[at];
        }

        /// <summary>
        /// The first entry with a local index in [<paramref name="from"/>, <paramref name="to"/>],
        /// as (index, amount), or (−1, −1). The nearest previous surface above a cell in its
        /// column, since a column is contiguous in index.
        /// </summary>
        public (int Index, int Amount) FirstIn(int from, int to)
        {
            int at = Array.BinarySearch(Index, 0, Count, from);
            if (at < 0)
            {
                at = ~at;
            }

            return at < Count && Index[at] <= to ? (Index[at], Amount[at]) : (-1, -1);
        }
    }

    // Each panel's surface as of its own last build — kept across updates for the panels an
    // update leaves alone, replaced from _built after the pass for the ones it rebuilt, so a
    // rebuilt panel's neighbours read what is on the screen and never a surface from this pass.
    private readonly Dictionary<ChunkPos, PanelSurface> _previous = [];
    private readonly Dictionary<ChunkPos, PanelSurface> _built = [];
    private readonly Stack<PanelSurface> _pool = new();
    private int _previousCells;

    // One record per held panel: the stamps it was last built at, its counts (so the whole
    // surface's totals are a sum without a walk), the depth carry leaving its top layer (so a
    // panel above can be rebuilt alone), and whether its last build drew motion.
    private sealed class PanelRecord
    {
        public long Stamp = long.MinValue;
        public long LakeStamp;
        public long StampNow;
        public long LakeStampNow;
        public ushort[] Cells = [];
        public bool IsNew = true;
        public bool Dirty;
        public int Seen;
        public byte[]? CarryOut;
        public int Tops, Sides, Bottoms, SurfaceCells, UnderLake, Quads, Vertices;
        public long Bytes;

        public void Reset()
        {
            Stamp = long.MinValue;
            LakeStamp = 0;
            IsNew = true;
            Dirty = false;
            Seen = 0;
            Tops = Sides = Bottoms = SurfaceCells = UnderLake = Quads = Vertices = 0;
            Bytes = 0;
        }
    }

    private readonly Dictionary<ChunkPos, PanelRecord> _records = [];
    private readonly Stack<PanelRecord> _recordPool = new();
    private readonly Stack<byte[]> _carryPool = new();
    private readonly List<ChunkPos> _changed = [];
    private readonly List<ChunkPos> _removed = [];

    // Panels whose entry in _previous changed at the last pass's swap — a new surface, a moved
    // one, or one dropped — so the panels that read it are rebuilt at this pass.
    private readonly List<ChunkPos> _previousMoved = [];
    private readonly List<FluidSurfaceMesh> _rebuilt = [];
    private int _pass;
    private int _totalTops, _totalSides, _totalBottoms, _totalSurfaceCells, _totalUnderLake, _totalQuads, _totalVertices;
    private long _totalBytes;

    // Build()'s whole-surface arm keeps every panel's mesh so it can hand back one mesh the way it
    // always has; Update() never fills this, so the game holds each panel's arrays once.
    private readonly Dictionary<ChunkPos, FluidSurfaceMesh> _retained = [];
    private bool _retaining;

    /// <summary>
    /// Rebuild every held panel on every build — the pre-2026-09-21 arm, the other mesher in the
    /// incremental rule's oracle, and the bench's full column. Off, a panel is rebuilt only when
    /// something it reads has moved.
    /// </summary>
    public bool ForceFull { get; set; }

    /// <summary>Panels with a record: the held panels as of the last build.</summary>
    public int PanelCount => _records.Count;

    /// <summary>What the last <see cref="Build"/> or <see cref="Update"/> rebuilt, for a report or a test reading through the whole-surface arm.</summary>
    public FluidSurfaceUpdate? LastUpdate { get; private set; }

    // Pass 1's view of the previous surface for the panel in hand: its own list with a cursor,
    // and the lists above and below it for the two boundary layers.
    private PanelSurface? _prevSame, _prevAbove, _prevBelow;
    private int _prevCursor;

    // The panel whose previous list the sides last asked for, so a run of questions about one
    // panel costs one dictionary lookup.
    private ChunkPos _prevCacheKey;
    private PanelSurface? _prevCache;
    private bool _prevCacheValid;

    private readonly List<float> _positions = [];
    private readonly List<float> _normals = [];
    private readonly List<float> _attributes = [];
    private readonly List<int> _indices = [];
    private readonly List<ChunkPos> _order = [];

    private int _tops, _sides, _bottoms, _surfaceCells, _terrainAsks, _underLake;

    /// <summary>Surface cells of the previous build, for tests and reports.</summary>
    public int PreviousSurfaceCells => _previousCells;

    /// <summary>
    /// The pre-M8d-1 view, as an arm: one quad on top of every surface cell, unmerged, no sides,
    /// no bottoms. The attributes are still written, so a shader reading them draws the same
    /// water either way. <c>--fluidsurface=debug</c> in the game; the control for every screenshot
    /// M8d-2 takes, since a look A/B needs the arm every M8b and M8c picture was taken in.
    /// </summary>
    public bool DebugView
    {
        get => !Merge && !Sides && !Bottoms;
        set => Merge = Sides = Bottoms = !value;
    }

    /// <summary>Whether tops merge. Off is one quad a surface cell.</summary>
    public bool Merge { get; set; } = true;

    /// <summary>Whether sides are built. Each face kind is a switch so a screenshot can say which one an artefact belongs to.</summary>
    public bool Sides { get; set; } = true;

    /// <summary>Whether bottoms are built.</summary>
    public bool Bottoms { get; set; } = true;

    /// <summary>Whether tops are emitted. Off leaves the sides and bottoms alone on the screen, which is how an artefact is attributed.</summary>
    public bool Tops { get; set; } = true;

    /// <summary>
    /// Builds the whole surface as one mesh — every held panel's geometry concatenated, in chunk
    /// order — for the tests and the bench, which read one set of arrays. Incremental underneath:
    /// the panels rebuilt are <see cref="Update"/>'s, and the rest come from the meshes this arm
    /// keeps; the first call, or the first after an <see cref="Update"/>, rebuilds everything so
    /// what it keeps is complete. <paramref name="terrain"/> is asked only about the dry
    /// neighbours of wet cells, and <c>IsLoaded</c> before <c>CanHold</c>, so a captured view
    /// counts no refusal for the mesher's questions.
    /// </summary>
    public FluidSurfaceMesh Build(FluidGrid grid, IFluidTerrain terrain)
    {
        bool force = ForceFull || !_retaining;
        _retaining = true;
        UpdateCore(grid, terrain, force);
        return FluidSurfaceMesh.Concatenate(_retained.Values);
    }

    /// <summary>
    /// Rebuilds the panels whose water, neighbours, column, lake or lerp moved since the last
    /// build and hands back only those, with the panels that no longer hold anything — what the
    /// game installs. The counts on the update are the whole surface's. See the class remarks for
    /// the rule.
    /// </summary>
    public FluidSurfaceUpdate Update(FluidGrid grid, IFluidTerrain terrain) => UpdateCore(grid, terrain, ForceFull);

    private FluidSurfaceUpdate UpdateCore(FluidGrid grid, IFluidTerrain terrain, bool force)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(terrain);

        _pass++;
        _terrainAsks = 0;
        _changed.Clear();
        _removed.Clear();
        _rebuilt.Clear();
        _order.Clear();
        int own = 0, fresh = 0, neighbour = 0, column = 0, lake = 0, previous = 0;

        // 1. What is held, against the records: a panel seen now is held; one not seen is gone.
        foreach ((ChunkPos chunk, ushort[] cells, long stamp) in grid.HeldPanels())
        {
            if (!_records.TryGetValue(chunk, out PanelRecord? record))
            {
                record = _recordPool.Count > 0 ? _recordPool.Pop() : new PanelRecord();
                record.Reset();
                _records[chunk] = record;
            }

            record.Seen = _pass;
            record.Dirty = false;
            record.Cells = cells;
            record.StampNow = stamp;
            record.LakeStampNow = terrain.LakeStamp(chunk);
            _order.Add(chunk);
        }

        foreach ((ChunkPos chunk, PanelRecord record) in _records)
        {
            if (record.Seen != _pass)
            {
                _removed.Add(chunk);
            }
        }

        foreach (ChunkPos chunk in _removed)
        {
            PanelRecord record = _records[chunk];
            _records.Remove(chunk);
            Subtract(record);
            if (record.CarryOut is { } carry)
            {
                _carryPool.Push(carry);
                record.CarryOut = null;
            }

            record.Cells = [];
            _recordPool.Push(record);
            _retained.Remove(chunk);

            // Its previous surface stays for this pass: a surface that fell out of the panel into
            // the one below lerps down from where it was, and the search for where it was reads
            // the panel it left. Dropped after the pass with the rest of the previous.
        }

        // 2. The panel's own reasons, in the order they are reported: new, its stamp, its lake's
        // stamp. A stamp that moved is also a source for its neighbours.
        foreach (ChunkPos chunk in _order)
        {
            PanelRecord record = _records[chunk];
            bool changed = force || record.IsNew || record.Stamp != record.StampNow;
            if (changed)
            {
                record.Dirty = true;
                _changed.Add(chunk);
                if (record.IsNew) fresh++;
                else if (record.Stamp != record.StampNow) own++;
            }
            else if (record.LakeStamp != record.LakeStampNow)
            {
                record.Dirty = true;
                lake++;
            }
        }

        // The previous surfaces that moved at the last pass: every panel whose build reads one
        // of them — the panel itself (the settle), the panels above and below it, its four
        // sideways neighbours and theirs above and below (a fallen surface is looked for in the
        // panel over the cell, and a side's dry neighbour's previous under it).
        foreach (ChunkPos chunk in _previousMoved)
        {
            previous += MarkIfHeld(chunk);
            previous += MarkIfHeld(chunk.Offset(0, 1, 0));
            previous += MarkIfHeld(chunk.Offset(0, -1, 0));
            for (int dy = -1; dy <= 1; dy++)
            {
                previous += MarkIfHeld(chunk.Offset(1, dy, 0));
                previous += MarkIfHeld(chunk.Offset(-1, dy, 0));
                previous += MarkIfHeld(chunk.Offset(0, dy, 1));
                previous += MarkIfHeld(chunk.Offset(0, dy, -1));
            }
        }

        _previousMoved.Clear();

        // 3. What a change reaches: the six face neighbours (the shared face's sides, the cell
        // above a top, the cell below a bottom) and the column above (the depth carry). A panel
        // released reaches the same set, because its neighbours' faces against it were not drawn.
        foreach (ChunkPos chunk in _changed)
        {
            neighbour += MarkFaces(chunk);
            column += MarkColumn(chunk);
        }

        foreach (ChunkPos chunk in _removed)
        {
            neighbour += MarkFaces(chunk);
            column += MarkColumn(chunk);
        }

        // 4. Build the dirty panels bottom-up through each column, so a rebuilt panel above a
        // rebuilt one reads its fresh carry; a panel above one left alone reads the carry that
        // panel kept from its own last build, which the column rule keeps current.
        int keep = 0;
        for (int i = 0; i < _order.Count; i++)
        {
            if (_records[_order[i]].Dirty)
            {
                _order[keep++] = _order[i];
            }
        }

        _order.RemoveRange(keep, _order.Count - keep);
        _order.Sort(static (a, b) =>
        {
            int c = a.X.CompareTo(b.X);
            if (c != 0) return c;
            c = a.Z.CompareTo(b.Z);
            return c != 0 ? c : a.Y.CompareTo(b.Y);
        });

        foreach (ChunkPos chunk in _order)
        {
            PanelRecord record = _records[chunk];
            if (_records.TryGetValue(chunk.Offset(0, -1, 0), out PanelRecord? below) && below.Seen == _pass && below.CarryOut is { } carryIn)
            {
                for (int i = 0; i < LayerCells; i++)
                {
                    _depthCarry[i] = carryIn[i];
                }
            }
            else
            {
                Array.Clear(_depthCarry);
            }

            _positions.Clear();
            _normals.Clear();
            _attributes.Clear();
            _indices.Clear();
            _tops = _sides = _bottoms = _surfaceCells = _underLake = 0;
            int asksBefore = _terrainAsks;

            BuildPanel(grid, terrain, chunk, record.Cells);

            bool carries = false;
            for (int i = 0; i < LayerCells; i++)
            {
                if (_depthCarry[i] != 0)
                {
                    carries = true;
                    break;
                }
            }

            if (carries)
            {
                byte[] carryOut = record.CarryOut ?? (_carryPool.Count > 0 ? _carryPool.Pop() : new byte[LayerCells]);
                for (int i = 0; i < LayerCells; i++)
                {
                    carryOut[i] = (byte)Math.Min(_depthCarry[i], MaxDepthCells);
                }

                record.CarryOut = carryOut;
            }
            else if (record.CarryOut is { } spare)
            {
                _carryPool.Push(spare);
                record.CarryOut = null;
            }

            var mesh = new FluidSurfaceMesh(
                _positions.ToArray(),
                _normals.ToArray(),
                _attributes.ToArray(),
                _indices.ToArray(),
                _tops,
                _sides,
                _bottoms,
                _surfaceCells,
                _terrainAsks - asksBefore,
                _underLake,
                chunk);

            Subtract(record);
            record.Tops = mesh.Tops;
            record.Sides = mesh.Sides;
            record.Bottoms = mesh.Bottoms;
            record.SurfaceCells = mesh.SurfaceCells;
            record.UnderLake = mesh.UnderLake;
            record.Quads = mesh.QuadCount;
            record.Vertices = mesh.VertexCount;
            record.Bytes = mesh.Bytes;
            Add(record);

            record.Stamp = record.StampNow;
            record.LakeStamp = record.LakeStampNow;
            record.IsNew = false;
            _rebuilt.Add(mesh);
            if (_retaining)
            {
                _retained[chunk] = mesh;
            }
        }

        // 5. This pass's surfaces are the next pass's previous, for the panels it rebuilt; the
        // rest keep theirs. A surface that is not what it was is what the next pass rebuilds
        // its readers for.
        foreach ((ChunkPos chunk, PanelSurface surface) in _built)
        {
            bool had = _previous.Remove(chunk, out PanelSurface? spent);
            bool moved = had ? !spent!.SameAs(surface) : surface.Count > 0;
            if (had)
            {
                spent!.Count = 0;
                _pool.Push(spent);
            }

            if (surface.Count > 0)
            {
                _previous[chunk] = surface;
            }
            else
            {
                _pool.Push(surface);
            }

            if (moved)
            {
                _previousMoved.Add(chunk);
            }
        }

        _built.Clear();
        foreach (ChunkPos chunk in _removed)
        {
            if (_previous.Remove(chunk, out PanelSurface? spent))
            {
                spent.Count = 0;
                _pool.Push(spent);
                _previousMoved.Add(chunk);
            }
        }

        _previousCells = _totalSurfaceCells;
        _prevCacheValid = false;

        long rebuiltBytes = 0;
        foreach (FluidSurfaceMesh mesh in _rebuilt)
        {
            rebuiltBytes += mesh.Bytes;
        }

        return LastUpdate = new FluidSurfaceUpdate(
            [.. _rebuilt],
            [.. _removed],
            _records.Count,
            _rebuilt.Count,
            own,
            fresh,
            neighbour,
            column,
            lake,
            previous,
            _totalQuads,
            _totalVertices,
            _totalTops,
            _totalSides,
            _totalBottoms,
            _totalSurfaceCells,
            _totalUnderLake,
            _terrainAsks,
            rebuiltBytes,
            _totalBytes);
    }

    private int MarkFaces(ChunkPos chunk)
    {
        int marked = 0;
        marked += MarkIfHeld(chunk.Offset(1, 0, 0));
        marked += MarkIfHeld(chunk.Offset(-1, 0, 0));
        marked += MarkIfHeld(chunk.Offset(0, 1, 0));
        marked += MarkIfHeld(chunk.Offset(0, -1, 0));
        marked += MarkIfHeld(chunk.Offset(0, 0, 1));
        marked += MarkIfHeld(chunk.Offset(0, 0, -1));
        return marked;
    }

    /// <summary>The column above, while it is held: a gap in the column ends the carry.</summary>
    private int MarkColumn(ChunkPos chunk)
    {
        int marked = 0;
        for (int dy = 1; dy <= ColumnReach; dy++)
        {
            ChunkPos up = chunk.Offset(0, dy, 0);
            if (!_records.TryGetValue(up, out PanelRecord? record) || record.Seen != _pass)
            {
                break;
            }

            if (!record.Dirty)
            {
                record.Dirty = true;
                marked++;
            }
        }

        return marked;
    }

    private int MarkIfHeld(ChunkPos chunk)
    {
        if (_records.TryGetValue(chunk, out PanelRecord? record) && record.Seen == _pass && !record.Dirty)
        {
            record.Dirty = true;
            return 1;
        }

        return 0;
    }

    private void Add(PanelRecord record)
    {
        _totalTops += record.Tops;
        _totalSides += record.Sides;
        _totalBottoms += record.Bottoms;
        _totalSurfaceCells += record.SurfaceCells;
        _totalUnderLake += record.UnderLake;
        _totalQuads += record.Quads;
        _totalVertices += record.Vertices;
        _totalBytes += record.Bytes;
    }

    private void Subtract(PanelRecord record)
    {
        _totalTops -= record.Tops;
        _totalSides -= record.Sides;
        _totalBottoms -= record.Bottoms;
        _totalSurfaceCells -= record.SurfaceCells;
        _totalUnderLake -= record.UnderLake;
        _totalQuads -= record.Quads;
        _totalVertices -= record.Vertices;
        _totalBytes -= record.Bytes;
    }

    private void BuildPanel(FluidGrid grid, IFluidTerrain terrain, ChunkPos chunk, ushort[] cells)
    {
        VoxelPos origin = chunk.OriginVoxel;
        Array.Clear(_layerHasSurface);
        Array.Clear(_layerWet);

        _previous.TryGetValue(chunk, out _prevSame);
        _previous.TryGetValue(chunk.Offset(0, 1, 0), out _prevAbove);
        _previous.TryGetValue(chunk.Offset(0, -1, 0), out _prevBelow);
        _prevCursor = 0;
        PanelSurface current = _pool.Count > 0 ? _pool.Pop() : new PanelSurface();

        // Pass 1: columns, bottom-up. Surface keys, depth per wet cell, bottoms.
        for (int lz = 0; lz < N; lz++)
        {
            for (int lx = 0; lx < N; lx++)
            {
                int column = VoxelPos.Index(lx, 0, lz);
                int carry = _depthCarry[(lz * N) + lx];
                int wetBelow = carry;

                int x = origin.X + lx;
                int z = origin.Z + lz;

                // The two cells outside the panel, asked through the grid because the panels
                // above and below may not exist.
                int aboveTop = grid.AmountAt(new VoxelPos(x, origin.Y + N, z));
                int below = carry > 0 ? 1 : grid.AmountAt(new VoxelPos(x, origin.Y - 1, z));

                for (int ly = 0; ly < N; ly++)
                {
                    int index = column + ly;
                    int amount = cells[index];
                    if (amount == 0)
                    {
                        wetBelow = 0;
                        below = 0;
                        continue;
                    }

                    int y = origin.Y + ly;
                    _layerWet[ly] = true;
                    _depthBelow[index] = (byte)Math.Min(wetBelow, MaxDepthCells);
                    _drawnPrev[index] = NoPrev;

                    // A cell with water above it is drawn full. At rest it is full; falling, the
                    // automaton holds it part-full with its water at the floor and air above, and
                    // drawing that is a stack of slabs with slots between — the first screenshot
                    // of the relay's ball was striped with the ground seen through them. The
                    // surface cell keeps its fill, so the column's top is still where the water is.
                    int above = ly == N - 1 ? aboveTop : cells[index + 1];
                    if (above == 0 && terrain.InLake(new VoxelPos(x, y + 1, z)))
                    {
                        // The lake's water continues above this cell: full, and no surface.
                        _terrainAsks++;
                        _underLake++;
                        above = Cap;
                    }

                    _drawnAmount[index] = (ushort)(above > 0 ? Cap : amount);
                    if (above == 0)
                    {
                        int prev = PreviousRelativeInPass(index, ly);
                        current.Add(index, amount);
                        _surfaceCells++;
                        _topKeys[index] = PackTop(amount, Math.Min(wetBelow, MaxDepthCells), prev);
                        _drawnPrev[index] = prev;
                        _layerHasSurface[ly] = true;
                    }

                    if (below == 0 && Bottoms)
                    {
                        var under = new VoxelPos(x, y - 1, z);
                        if (IsAir(terrain, under))
                        {
                            AddBottom(x, y, z, amount);
                        }
                    }

                    wetBelow++;
                    below = amount;
                }

                _depthCarry[(lz * N) + lx] = wetBelow;
            }
        }

        // Pass 2: the tops, greedy per layer. Writes what each surface cell is drawn at.
        for (int ly = 0; ly < N; ly++)
        {
            if (_layerHasSurface[ly])
            {
                MergeTops(origin, ly);
            }
        }

        // The previous surface the next build reads is what was drawn, not what was held, so a
        // cell that stays in its run lerps from the run's height and not from its own millilitre.
        for (int i = 0; i < current.Count; i++)
        {
            current.Amount[i] = _drawnAmount[current.Index[i]];
        }

        // Recorded empty as well as full, so the swap after the pass can drop a previous surface
        // this build no longer has.
        _built[chunk] = current;

        // Pass 3: the sides, runs along the wall. ±Z faces run along X; ±X faces run along Z.
        for (int ly = 0; ly < N; ly++)
        {
            if (!_layerWet[ly] || !Sides)
            {
                continue;
            }

            SidesAlongX(grid, terrain, origin, cells, ly, dz: -1);
            SidesAlongX(grid, terrain, origin, cells, ly, dz: +1);
            SidesAlongZ(grid, terrain, origin, cells, ly, dx: -1);
            SidesAlongZ(grid, terrain, origin, cells, ly, dx: +1);
        }
    }

    private void MergeTops(VoxelPos origin, int ly)
    {
        for (int lz = 0; lz < N; lz++)
        {
            for (int lx = 0; lx < N; lx++)
            {
                int index = VoxelPos.Index(lx, ly, lz);
                long key = _topKeys[index];
                if (key == 0)
                {
                    continue;
                }

                (int amount, int wetBelow, int prev) = UnpackTop(key);
                int width = 1;
                while (Merge && lx + width < N && WithinTop(_topKeys[index + (width * StrideX)], amount, wetBelow, prev))
                {
                    width++;
                }

                int depth = 1;
                while (Merge && lz + depth < N)
                {
                    int row = index + (depth * StrideZ);
                    bool whole = true;
                    for (int i = 0; i < width; i++)
                    {
                        if (!WithinTop(_topKeys[row + (i * StrideX)], amount, wetBelow, prev))
                        {
                            whole = false;
                            break;
                        }
                    }

                    if (!whole)
                    {
                        break;
                    }

                    depth++;
                }

                for (int j = 0; j < depth; j++)
                {
                    for (int i = 0; i < width; i++)
                    {
                        int taken = index + (j * StrideZ) + (i * StrideX);
                        _topKeys[taken] = 0;
                        _drawnAmount[taken] = (ushort)amount;
                        _drawnPrev[taken] = prev;
                    }
                }

                if (Tops)
                {
                    AddTop(origin.X + lx, origin.Y + ly, origin.Z + lz, width, depth, amount, wetBelow, prev);
                }

                lx += width - 1;
            }
        }
    }

    /// <summary>The ±Z walls of a layer, each run along X.</summary>
    private void SidesAlongX(FluidGrid grid, IFluidTerrain terrain, VoxelPos origin, ushort[] cells, int ly, int dz)
    {
        int y = origin.Y + ly;
        for (int lz = 0; lz < N; lz++)
        {
            int nz = lz + dz;
            bool inside = (uint)nz < N;
            int z = origin.Z + lz;

            SideKey run = default;
            int runStart = 0, runLength = 0;
            for (int lx = 0; lx <= N; lx++)
            {
                SideKey key = default;
                if (lx < N)
                {
                    int index = VoxelPos.Index(lx, ly, lz);
                    if (cells[index] > 0)
                    {
                        int x = origin.X + lx;
                        var neighbour = new VoxelPos(x, y, z + dz);
                        key = SideBetween(grid, terrain, cells, index, inside ? index + (dz * StrideZ) : -1, new VoxelPos(x, y, z), neighbour);
                    }
                }

                if (runLength > 0 && key.A > 0 && key == run)
                {
                    runLength++;
                    continue;
                }

                if (runLength > 0)
                {
                    AddSideZ(origin.X + runStart, y, z, runLength, dz, run);
                }

                run = key;
                runStart = lx;
                runLength = key.A > 0 ? 1 : 0;
            }
        }
    }

    /// <summary>The ±X walls of a layer, each run along Z.</summary>
    private void SidesAlongZ(FluidGrid grid, IFluidTerrain terrain, VoxelPos origin, ushort[] cells, int ly, int dx)
    {
        int y = origin.Y + ly;
        for (int lx = 0; lx < N; lx++)
        {
            int nx = lx + dx;
            bool inside = (uint)nx < N;
            int x = origin.X + lx;

            SideKey run = default;
            int runStart = 0, runLength = 0;
            for (int lz = 0; lz <= N; lz++)
            {
                SideKey key = default;
                if (lz < N)
                {
                    int index = VoxelPos.Index(lx, ly, lz);
                    if (cells[index] > 0)
                    {
                        int z = origin.Z + lz;
                        var neighbour = new VoxelPos(x + dx, y, z);
                        key = SideBetween(grid, terrain, cells, index, inside ? index + (dx * StrideX) : -1, new VoxelPos(x, y, z), neighbour);
                    }
                }

                if (runLength > 0 && key.A > 0 && key == run)
                {
                    runLength++;
                    continue;
                }

                if (runLength > 0)
                {
                    AddSideX(x, y, origin.Z + runStart, runLength, dx, run);
                }

                run = key;
                runStart = lz;
                runLength = key.A > 0 ? 1 : 0;
            }
        }
    }

    private bool IsAir(IFluidTerrain terrain, VoxelPos voxel)
    {
        _terrainAsks++;
        return terrain.IsLoaded(voxel) && terrain.CanHold(voxel);
    }

    /// <summary>
    /// Where this cell's surface stood at the previous build, in millilitres relative to the
    /// cell's own floor: the cell itself, the cell above (the surface fell into this one), the
    /// cell below (it rose out of that one), or the floor.
    /// </summary>
    private int PreviousRelative(VoxelPos cell)
    {
        if (_previous.Count == 0)
        {
            return 0;
        }

        int same = PreviousAt(cell);
        if (same >= 0)
        {
            return same;
        }

        int above = PreviousAbove(cell);
        if (above >= 0)
        {
            return above;
        }

        int below = PreviousAt(cell.Offset(0, -1, 0));
        if (below >= 0)
        {
            return below - Cap;
        }

        return 0;
    }

    /// <summary>
    /// The nearest previous surface above a cell in its column within <see cref="FallSearchCells"/>,
    /// relative to the cell's floor in millilitres, or −1. A fall of several cells in one tick
    /// (M8d-3) lands a surface that far under where it was, and the lerp should bring it down
    /// from there rather than up from its floor. Two panels at most: the cell's own to its top
    /// layer, then the one above for the rest of the reach.
    /// </summary>
    private int PreviousAbove(VoxelPos cell)
    {
        (int lx, int ly, int lz) = cell.Local;
        int column = VoxelPos.Index(lx, 0, lz);
        int index = column + ly;
        int reach = Math.Min(FallSearchCells, N - 1 - ly);
        if (reach > 0)
        {
            PanelSurface? own = PanelOf(cell.Chunk);
            if (own is not null)
            {
                (int found, int amount) = own.FirstIn(index + 1, index + reach);
                if (found >= 0)
                {
                    return ((found - index) * Cap) + amount;
                }
            }
        }

        int rest = FallSearchCells - (N - 1 - ly);
        if (rest > 0)
        {
            PanelSurface? up = PanelOf(cell.Chunk.Offset(0, 1, 0));
            if (up is not null)
            {
                (int found, int amount) = up.FirstIn(column, column + Math.Min(rest, N) - 1);
                if (found >= 0)
                {
                    return ((N - ly + (found - column)) * Cap) + amount;
                }
            }
        }

        return -1;
    }

    private PanelSurface? PanelOf(ChunkPos chunk)
    {
        if (!_prevCacheValid || _prevCacheKey != chunk)
        {
            _previous.TryGetValue(chunk, out _prevCache);
            _prevCacheKey = chunk;
            _prevCacheValid = true;
        }

        return _prevCache;
    }

    /// <summary>The previous surface's amount at a cell, or −1: one panel lookup, cached, and a binary search.</summary>
    private int PreviousAt(VoxelPos cell) => PanelOf(cell.Chunk)?.Find(cell.LocalIndex) ?? -1;

    /// <summary>
    /// <see cref="PreviousRelative"/> for pass 1, which visits surface cells in index order: the
    /// panel's own previous list is read with a cursor that only advances, so the whole panel
    /// costs one pass over both lists. The cell above at the top layer and the cell below at the
    /// bottom one are in other panels and are searched.
    /// </summary>
    private int PreviousRelativeInPass(int index, int ly)
    {
        if (_previous.Count == 0)
        {
            return 0;
        }

        // The same cell and the cell below are index and index − 1 in the sorted list, and the
        // nearest previous surface above within reach is the first entry past index that is
        // still in this column; advance to the first entry at or past index − 1 and scan.
        int same = -1, above = -1, below = -1;
        int reach = Math.Min(FallSearchCells, N - 1 - ly);
        if (_prevSame is { } list)
        {
            int[] indices = list.Index;
            int count = list.Count;
            while (_prevCursor < count && indices[_prevCursor] < index - 1)
            {
                _prevCursor++;
            }

            for (int at = _prevCursor; at < count; at++)
            {
                int found = indices[at];
                if (found == index) { same = list.Amount[at]; break; }
                if (found == index - 1) { if (ly > 0) below = list.Amount[at]; continue; }
                if (found > index)
                {
                    if (found <= index + reach)
                    {
                        above = ((found - index) * Cap) + list.Amount[at];
                    }

                    break;
                }
            }
        }

        if (same >= 0)
        {
            return same;
        }

        if (above < 0)
        {
            int rest = FallSearchCells - (N - 1 - ly);
            if (rest > 0 && _prevAbove is { } up)
            {
                int column = index - ly;
                (int found, int amount) = up.FirstIn(column, column + Math.Min(rest, N) - 1);
                if (found >= 0)
                {
                    above = ((N - ly + (found - column)) * Cap) + amount;
                }
            }
        }

        if (above >= 0)
        {
            return above;
        }

        if (ly == 0 && _prevBelow is { } down)
        {
            below = down.Find(index + (N - 1));
        }

        return below >= 0 ? below - Cap : 0;
    }

    /// <summary>
    /// The face a wet cell shows its neighbour, or none: drawn when the cell is drawn higher than
    /// the neighbour — the neighbour's own drawn height when it is in this panel, its held amount
    /// when it is not — and the neighbour is water or air. Exact, not within the tolerance: two
    /// runs drawn a millimetre apart leave a crack the width of a pixel at a grazing angle, and
    /// the first screenshot was streaked with them; a sliver closes it. Two cells in one run are
    /// drawn equal and show no face.
    /// </summary>
    private SideKey SideBetween(FluidGrid grid, IFluidTerrain terrain, ushort[] cells, int index, int neighbourIndex, VoxelPos cell, VoxelPos neighbour)
    {
        int a = _drawnAmount[index];
        int b = neighbourIndex >= 0 ? _drawnAmount[neighbourIndex] : grid.AmountAt(neighbour);
        if (neighbourIndex >= 0 && cells[neighbourIndex] == 0)
        {
            // Dry, so nothing wrote its drawn amount this panel.
            b = 0;
        }

        if (b >= a || (b == 0 && !IsAir(terrain, neighbour)))
        {
            return default;
        }

        int prevA = _drawnPrev[index];
        if (prevA == NoPrev)
        {
            prevA = PreviousRelative(cell);
        }

        int prevB;
        if (neighbourIndex >= 0 && b > 0 && _drawnPrev[neighbourIndex] != NoPrev)
        {
            prevB = _drawnPrev[neighbourIndex];
        }
        else
        {
            prevB = PreviousRelative(neighbour);
        }

        return new SideKey(a, b, _depthBelow[index], prevA, prevB);
    }

    private static bool Near(int a, int b) => Math.Abs(a - b) <= ToleranceMillilitres;

    /// <summary>Whether a top's key may join a run begun by a cell with these values.</summary>
    private static bool WithinTop(long key, int amount, int wetBelow, int prev)
    {
        if (key == 0)
        {
            return false;
        }

        (int a, int d, int p) = UnpackTop(key);
        return d == wetBelow && Near(a, amount) && Near(p, prev);
    }

    // amount < 2^14, depth < 2^8, prev + Cap < 2^21 (a previous up to FallSearchCells cells
    // above is (64 + 1) × 15,625 < 2^21): 43 bits, never zero because amount ≥ 1.
    private static long PackTop(int amount, int depth, int prev) =>
        (long)(uint)amount | ((long)depth << 14) | ((long)(prev + Cap) << 22);

    private static (int Amount, int Depth, int Prev) UnpackTop(long key) =>
        ((int)(key & 0x3FFF), (int)((key >> 14) & 0xFF), (int)((key >> 22) & 0x1FFFFF) - Cap);

    private readonly record struct SideKey(int A, int B, int Depth, int PrevA, int PrevB);

    private static float Height(int y, int millilitres) => (y + (millilitres / (float)Cap)) * Size;

    private static float DepthMetres(int wetBelow, int millilitres) => (wetBelow + (millilitres / (float)Cap)) * Size;

    private void AddTop(int x, int y, int z, int width, int depth, int amount, int wetBelow, int prev)
    {
        float x0 = x * Size, x1 = (x + width) * Size;
        float z0 = z * Size, z1 = (z + depth) * Size;
        float h = Height(y, amount);
        float prevH = Height(y, prev);
        float d = DepthMetres(wetBelow, amount);

        // Corner order such that (p1 − p0) × (p3 − p0) is opposite the normal; the index pattern
        // in AddQuad reverses it. Same as the debug view's top.
        AddQuad(
            x0, h, z0, x1, h, z0, x1, h, z1, x0, h, z1,
            0f, 1f, 0f,
            prevH, d, prevH, d, prevH, d, prevH, d);
        _tops++;
    }

    private void AddBottom(int x, int y, int z, int amount)
    {
        float x0 = x * Size, x1 = (x + 1) * Size;
        float z0 = z * Size, z1 = (z + 1) * Size;
        float h = y * Size;

        // The underside of a cell with air beneath it: nothing is wet below, so its depth is
        // what the cell holds, and its previous height is the floor it is drawn on.
        float d = DepthMetres(0, amount);
        AddQuad(
            x0, h, z0, x0, h, z1, x1, h, z1, x1, h, z0,
            0f, -1f, 0f,
            h, d, h, d, h, d, h, d);
        _bottoms++;
    }

    /// <summary>A wall facing ±Z, <paramref name="length"/> cells along X from <paramref name="x"/>.</summary>
    private void AddSideZ(int x, int y, int z, int length, int dz, SideKey key)
    {
        float x0 = x * Size, x1 = (x + length) * Size;
        float zf = (dz < 0 ? z : z + 1) * Size;
        float ht = Height(y, key.A), hb = Height(y, key.B);
        float pt = Height(y, key.PrevA), pb = Height(y, key.PrevB);
        float dt = DepthMetres(key.Depth, key.A), db = DepthMetres(key.Depth, key.B);

        if (dz < 0)
        {
            AddQuad(
                x0, hb, zf, x1, hb, zf, x1, ht, zf, x0, ht, zf,
                0f, 0f, -1f,
                pb, db, pb, db, pt, dt, pt, dt);
        }
        else
        {
            AddQuad(
                x0, hb, zf, x0, ht, zf, x1, ht, zf, x1, hb, zf,
                0f, 0f, 1f,
                pb, db, pt, dt, pt, dt, pb, db);
        }

        _sides++;
    }

    /// <summary>A wall facing ±X, <paramref name="length"/> cells along Z from <paramref name="z"/>.</summary>
    private void AddSideX(int x, int y, int z, int length, int dx, SideKey key)
    {
        float z0 = z * Size, z1 = (z + length) * Size;
        float xf = (dx < 0 ? x : x + 1) * Size;
        float ht = Height(y, key.A), hb = Height(y, key.B);
        float pt = Height(y, key.PrevA), pb = Height(y, key.PrevB);
        float dt = DepthMetres(key.Depth, key.A), db = DepthMetres(key.Depth, key.B);

        if (dx < 0)
        {
            AddQuad(
                xf, hb, z0, xf, ht, z0, xf, ht, z1, xf, hb, z1,
                -1f, 0f, 0f,
                pb, db, pt, dt, pt, dt, pb, db);
        }
        else
        {
            AddQuad(
                xf, hb, z0, xf, hb, z1, xf, ht, z1, xf, ht, z0,
                1f, 0f, 0f,
                pb, db, pb, db, pt, dt, pt, dt);
        }

        _sides++;
    }

    private void AddQuad(
        float x0, float y0, float z0,
        float x1, float y1, float z1,
        float x2, float y2, float z2,
        float x3, float y3, float z3,
        float nx, float ny, float nz,
        float p0, float d0, float p1, float d1, float p2, float d2, float p3, float d3)
    {
        int first = _positions.Count / 3;

        _positions.Add(x0); _positions.Add(y0); _positions.Add(z0);
        _positions.Add(x1); _positions.Add(y1); _positions.Add(z1);
        _positions.Add(x2); _positions.Add(y2); _positions.Add(z2);
        _positions.Add(x3); _positions.Add(y3); _positions.Add(z3);

        for (int i = 0; i < 4; i++)
        {
            _normals.Add(nx); _normals.Add(ny); _normals.Add(nz);
        }

        _attributes.Add(p0); _attributes.Add(d0);
        _attributes.Add(p1); _attributes.Add(d1);
        _attributes.Add(p2); _attributes.Add(d2);
        _attributes.Add(p3); _attributes.Add(d3);

        // Front faces are Godot's clockwise. The corners here have (p1 − p0) × (p3 − p0) against
        // the normal — the debug view's order, which drew cull_disabled and never had to be right
        // — so the triangles walk the corners forward, (0, 1, 2), (0, 2, 3), where the terrain
        // mesher's corners (cross along the normal) walk them as (0, 2, 1), (0, 3, 2). M8d-2 culls
        // back faces to make depth writing sort the water, and a screenshot from above is what
        // confirmed the tops face up.
        _indices.Add(first);
        _indices.Add(first + 1);
        _indices.Add(first + 2);
        _indices.Add(first);
        _indices.Add(first + 2);
        _indices.Add(first + 3);
    }
}

/// <summary>
/// One build of the Tier B surface: flat arrays a renderer installs as they are, plus the counts a
/// report prints. Positions and normals are three floats a vertex, attributes two — the height
/// the surface stood at on the previous build, in metres, and the depth of water under the
/// vertex, in metres — and indices are six a quad.
/// </summary>
public sealed class FluidSurfaceMesh(
    float[] positions,
    float[] normals,
    float[] attributes,
    int[] indices,
    int tops,
    int sides,
    int bottoms,
    int surfaceCells,
    int terrainAsks,
    int underLake,
    ChunkPos? chunk = null)
{
    /// <summary>The panel this mesh is of, or null for a whole surface (<see cref="Concatenate"/>).</summary>
    public ChunkPos? Chunk { get; } = chunk;

    public float[] Positions { get; } = positions;

    public float[] Normals { get; } = normals;

    /// <summary>Per vertex: previous surface height (m), then wet depth under the vertex (m).</summary>
    public float[] Attributes { get; } = attributes;

    public int[] Indices { get; } = indices;

    public int VertexCount => Positions.Length / 3;

    public int QuadCount => Indices.Length / 6;

    /// <summary>Merged top quads.</summary>
    public int Tops { get; } = tops;

    /// <summary>Side runs.</summary>
    public int Sides { get; } = sides;

    /// <summary>Undersides of cells falling through air.</summary>
    public int Bottoms { get; } = bottoms;

    /// <summary>Surface cells the tops cover, which the debug view drew one quad each.</summary>
    public int SurfaceCells { get; } = surfaceCells;

    /// <summary>Questions asked of the terrain, all about dry neighbours of wet cells and the cell above a would-be surface.</summary>
    public int TerrainAsks { get; } = terrainAsks;

    /// <summary>Wet cells drawn full with no top because a lake's cell is above them (M8d-5).</summary>
    public int UnderLake { get; } = underLake;

    /// <summary>Bytes the arrays occupy: what a job carries back.</summary>
    public long Bytes => (long)sizeof(float) * (Positions.Length + Normals.Length + Attributes.Length) + (long)sizeof(int) * Indices.Length;

    /// <summary>
    /// One mesh from many panels', in chunk order — (x, z, y), the mesher's own build order — so
    /// the same panels give the same arrays whatever order they arrived in. Indices are offset
    /// by the vertices before them; the counts are summed.
    /// </summary>
    public static FluidSurfaceMesh Concatenate(IEnumerable<FluidSurfaceMesh> panels)
    {
        ArgumentNullException.ThrowIfNull(panels);
        var ordered = new List<FluidSurfaceMesh>(panels);
        ordered.Sort(static (a, b) =>
        {
            ChunkPos ca = a.Chunk ?? ChunkPos.Zero, cb = b.Chunk ?? ChunkPos.Zero;
            int c = ca.X.CompareTo(cb.X);
            if (c != 0) return c;
            c = ca.Z.CompareTo(cb.Z);
            return c != 0 ? c : ca.Y.CompareTo(cb.Y);
        });

        int vertices = 0, indices = 0, tops = 0, sides = 0, bottoms = 0, cells = 0, asks = 0, underLake = 0;
        foreach (FluidSurfaceMesh panel in ordered)
        {
            vertices += panel.VertexCount;
            indices += panel.Indices.Length;
            tops += panel.Tops;
            sides += panel.Sides;
            bottoms += panel.Bottoms;
            cells += panel.SurfaceCells;
            asks += panel.TerrainAsks;
            underLake += panel.UnderLake;
        }

        var positions = new float[vertices * 3];
        var normals = new float[vertices * 3];
        var attributes = new float[vertices * 2];
        var index = new int[indices];
        int v = 0, i = 0;
        foreach (FluidSurfaceMesh panel in ordered)
        {
            Array.Copy(panel.Positions, 0, positions, v * 3, panel.Positions.Length);
            Array.Copy(panel.Normals, 0, normals, v * 3, panel.Normals.Length);
            Array.Copy(panel.Attributes, 0, attributes, v * 2, panel.Attributes.Length);
            int[] from = panel.Indices;
            for (int k = 0; k < from.Length; k++)
            {
                index[i + k] = from[k] + v;
            }

            v += panel.VertexCount;
            i += from.Length;
        }

        return new FluidSurfaceMesh(positions, normals, attributes, index, tops, sides, bottoms, cells, asks, underLake);
    }
}

/// <summary>
/// What one <see cref="FluidSurfaceMesher.Update"/> changed: the panels rebuilt, each as its own
/// mesh, and the panels that hold nothing any more — the game installs the first and frees the
/// second and touches nothing else. The counts are the whole surface's after the update, so a
/// report reads the same numbers it always did; <see cref="RebuiltBytes"/> is what the job carried
/// back and the install paid for, <see cref="SurfaceBytes"/> what every panel's arrays add up to.
/// </summary>
public sealed class FluidSurfaceUpdate(
    IReadOnlyList<FluidSurfaceMesh> rebuilt,
    IReadOnlyList<ChunkPos> removed,
    int panelsHeld,
    int panelsRebuilt,
    int rebuiltForOwnChange,
    int rebuiltNew,
    int rebuiltForNeighbour,
    int rebuiltForColumn,
    int rebuiltForLake,
    int rebuiltForPrevious,
    int quadCount,
    int vertexCount,
    int tops,
    int sides,
    int bottoms,
    int surfaceCells,
    int underLake,
    int terrainAsks,
    long rebuiltBytes,
    long surfaceBytes)
{
    /// <summary>The panels rebuilt this update, each mesh with its <see cref="FluidSurfaceMesh.Chunk"/> set; a panel with no quads is one to draw nothing for.</summary>
    public IReadOnlyList<FluidSurfaceMesh> Rebuilt { get; } = rebuilt;

    /// <summary>Panels that held water at the last update and hold none now: their meshes go.</summary>
    public IReadOnlyList<ChunkPos> Removed { get; } = removed;

    /// <summary>Panels holding water after this update.</summary>
    public int PanelsHeld { get; } = panelsHeld;

    /// <summary>Panels this update rebuilt, by every cause together.</summary>
    public int PanelsRebuilt { get; } = panelsRebuilt;

    /// <summary>Rebuilt because a cell of their own was written to a new value, or the terrain beside it touched.</summary>
    public int RebuiltForOwnChange { get; } = rebuiltForOwnChange;

    /// <summary>Rebuilt because they held nothing at the last update.</summary>
    public int RebuiltNew { get; } = rebuiltNew;

    /// <summary>Rebuilt for a face neighbour that changed or was released, and no reason of their own.</summary>
    public int RebuiltForNeighbour { get; } = rebuiltForNeighbour;

    /// <summary>Rebuilt for a change in the column under them, and no reason of their own.</summary>
    public int RebuiltForColumn { get; } = rebuiltForColumn;

    /// <summary>Rebuilt because the lake band over their column was rebuilt or dropped.</summary>
    public int RebuiltForLake { get; } = rebuiltForLake;

    /// <summary>
    /// Rebuilt because a previous surface they read moved at the last build, and no reason of
    /// their own: their own, so the lerp settles — a panel whose water moved is built once more
    /// with its previous equal to its current — or a neighbour's.
    /// </summary>
    public int RebuiltForPrevious { get; } = rebuiltForPrevious;

    public int QuadCount { get; } = quadCount;

    public int VertexCount { get; } = vertexCount;

    public int Tops { get; } = tops;

    public int Sides { get; } = sides;

    public int Bottoms { get; } = bottoms;

    public int SurfaceCells { get; } = surfaceCells;

    public int UnderLake { get; } = underLake;

    /// <summary>Terrain questions this update put, over the panels it rebuilt.</summary>
    public int TerrainAsks { get; } = terrainAsks;

    /// <summary>Bytes of the rebuilt panels' arrays: what the job carried back and the install marshals.</summary>
    public long RebuiltBytes { get; } = rebuiltBytes;

    /// <summary>Bytes of every held panel's arrays: the surface as it stands.</summary>
    public long SurfaceBytes { get; } = surfaceBytes;
}
