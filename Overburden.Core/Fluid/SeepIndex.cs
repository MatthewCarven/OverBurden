using Overburden.Core.Hydrology;
using Overburden.Core.Lighting;
using Overburden.Core.Voxels;

namespace Overburden.Core.Fluid;

/// <summary>
/// One face of an excavation that groundwater can come through: the holdable voxel the water
/// arrives in, the rock it comes through, and how readily that rock passes it.
/// </summary>
/// <remarks>
/// The seep twin of <see cref="Portal"/>. A portal is a hole in the analytic ground — dug voxel
/// against the open air above a neighbour's surface, where the lake is; a seep face is a dug voxel
/// against rock, where the lake's head is felt through the wall. Six faces of one dug voxel can be
/// six seep faces, through six rocks of six permeabilities. <see cref="Permeability"/> is part of
/// the value, so a face whose rock changed material is a different face, and the index reconciles
/// it as one removed and one added.
/// </remarks>
public readonly record struct SeepFace(VoxelPos Inside, VoxelPos Rock, float Permeability)
{
    /// <summary>The Y the water arrives at, which is what the delivery order sorts on.</summary>
    public int Y => Inside.Y;

    /// <summary>Through the floor: the rock is directly below. A floor spring, in the plan's words.</summary>
    public bool IsFloor => Rock.Y == Inside.Y - 1;

    /// <summary>Through the roof: the rock is directly above.</summary>
    public bool IsCeiling => Rock.Y == Inside.Y + 1;
}

/// <summary>
/// The faces of one chunk that share a column sample and a Y, with their permeabilities summed —
/// what a tick boundary iterates, because every face in a group sees the same head.
/// </summary>
public readonly record struct SeepGroup(int SampleX, int SampleZ, int Y, double Permeability);

/// <summary>
/// The water table over a column, as the level of whatever lake stands over its sample.
/// </summary>
/// <remarks>
/// <b>M8c's decision 1: the water table under a lake is the lake, and there is no field.</b> DESIGN's
/// Tier C is a coarse saturation grid, rain-fed and draining downhill; the quantity that grid exists
/// to produce is <i>how much head stands over this voxel</i>, and for a column in a basin's
/// catchment that is the basin's solved level, which Tier A computes from volume and terrain,
/// banks per pit and persists. So the table is derived here and stored nowhere. The cost, stated:
/// a tunnel under a divide into a dry basin's catchment is dry however close the lake on the other
/// side is. Revisit trigger, in M8-PLAN: that play, exactly.
/// </remarks>
public interface IWaterTable
{
    /// <summary>
    /// The surface of the lake the basin under this world-lattice sample is part of, in voxel Y —
    /// the plane the water stands on, as <see cref="WaterSystem.LevelAt"/> reports it — or null
    /// when nothing stands over it.
    /// </summary>
    double? TableAt(int sampleX, int sampleZ);
}

/// <summary>
/// Every face through which a lake could seep into an excavation, filed under the chunk the water
/// would arrive in, lowest first.
/// </summary>
/// <remarks>
/// <para>
/// <b>The definition is <see cref="FacesOf"/>, and everything else here has to agree with it</b> —
/// <see cref="PortalIndex"/>'s rule, with the same oracle: after any sequence of scans, edits and
/// forgets the index equals the definition evaluated over every voxel of every scanned chunk and
/// its shell, and the test runs it. A seep face is a holdable voxel at or below its column's seed
/// surface with a face neighbour that is loaded rock of non-zero permeability. Above the seed
/// surface is the lake's own volume, Tier A's and not an excavation's; a sealed rock is not a
/// face at all, so a room lined with something impermeable seeps nothing and says so by having
/// no faces rather than by a coefficient of zero.
/// </para>
/// <para>
/// <b>The table is not in the definition.</b> Whether a lake stands over a face changes every tick
/// as lakes rise and drain, and nothing edits the world when it does; so the index holds every
/// candidate face and <see cref="Coefficient"/> asks the table at the boundary — per
/// <see cref="SeepGroup"/> rather than per face, because every face in a column sample at one Y
/// sees the same head, and a bitten-out chunk's floor is one Y across sixteen samples rather than
/// a thousand faces. A chunk under a dry basin costs its groups' table reads and nothing else.
/// </para>
/// <para>
/// <b>Filed under the inside's chunk, because that is where the water arrives and the remainder
/// pools.</b> A face whose rock is in the neighbouring chunk is indexed a second way, by the rock's
/// chunk, so that forgetting either chunk finds it without walking six neighbours' stores — a box
/// sweep forgets a slab of chunks in one frame, and a dug mine's chunk holds up to six thousand
/// faces. As with portals, a scan takes every face touching the chunk from either side, and a face
/// against an unloaded voxel does not exist until the chunk arrives and the scan of whichever half
/// arrived second finds it.
/// </para>
/// <para>
/// <b>An edit re-evaluates twelve pairs, and the edit path is deliberately a second copy of the
/// geometry</b> (M8b-2b's rule, which is what let its fuzz catch a mutation): six with the voxel as
/// the inside and six with it as the rock. What the voxel became decides which six need a terrain
/// read — a voxel that now holds water cannot be anyone's rock, so those six are dictionary
/// removals; a voxel that is now rock cannot be anyone's inside. A chunk bite deep in a mine is
/// 32,768 of these with no geometric exit, unlike the portal hook, so the bench's <c>--seep</c>
/// arm prices it rather than the plan's 1.5–2 ms estimate.
/// </para>
/// <para>
/// <b>Only dug chunks seep</b> — M8c's decision 3, the portal rule — and here the rule is on the
/// inside's chunk, not on the pair: a face's inside must lie in a chunk <see cref="IsDug"/> says
/// yes to. The portal index files every pair touching a dug chunk and gets away with it because a
/// natural cave is never a pit and a bank needs a cliff; a seep face needs only rock beside a
/// hollow, and caves are everywhere underground, so the looser rule wet a cave beside the first
/// shaft the wire check bit and kept seeping into it after the shaft's own pit had drained.
/// <see cref="PortalTracker"/> owns what dug means and binds it here; the index alone says yes to
/// everything. Nothing here is stored: the faces are derived from the voxels and the seed surface,
/// the table is Tier A's, and the one piece of state, the sub-litre <see cref="RemainderMillilitres"/>
/// a chunk carries between ticks, is bounded at one cell and lost with the chunk — stated as a
/// bound and asserted, decision 7.
/// </para>
/// </remarks>
public sealed class SeepIndex
{
    /// <summary>
    /// The seepage rate the game ships with, in litres per second, per square metre of face, per
    /// metre of head. <b>An estimate</b>, placed at the nuisance end M8's settled decision asks for:
    /// a 10 × 10 × 3 m stone room under five metres of head takes about a litre a second through
    /// ~160 m² of wall, seven hours of world time to stand ankle-deep. Dirt is seventeen times
    /// faster. <c>--seepscale=</c> is the sweep and M8d's play picks inside it, as
    /// <c>rainfallScale</c> was picked.
    /// </summary>
    public const double DefaultSeepageScale = 0.06;

    /// <summary>One voxel face, in square metres. What a face's permeability × head is scaled by.</summary>
    public const double FaceAreaSquareMetres = (double)VoxelConstants.VoxelSize * VoxelConstants.VoxelSize;

    private static readonly (int X, int Y, int Z)[] Faces =
    [
        (0, -1, 0), (1, 0, 0), (-1, 0, 0), (0, 0, 1), (0, 0, -1), (0, 1, 0),
    ];

    private sealed class ChunkSeeps
    {
        public readonly Dictionary<(VoxelPos Inside, VoxelPos Rock), float> Faces = [];
        public readonly List<SeepFace> Sorted = [];
        public readonly List<SeepGroup> Groups = [];
        public bool Stale = true;
        public long RemainderMillilitres;
        public long Revision;
    }

    private readonly SeepNeighbourhood _block = new();

    private readonly ISurfaceHeights _seed;
    private readonly ISeepTerrain _terrain;
    private readonly Dictionary<ChunkPos, ChunkSeeps> _byChunk = [];
    private readonly Dictionary<ChunkPos, HashSet<(VoxelPos Inside, VoxelPos Rock)>> _byRockChunk = [];
    private readonly Dictionary<(int X, int Z), double?> _tableCache = [];
    private readonly Dictionary<(int Y, int X, int Z), double> _groupSums = [];
    private int _count;
    private long _revision;

    /// <param name="seed">The unedited surface — the world generator, or the sky columns' copy of it.</param>
    /// <param name="terrain">Holdability and rock now, unloaded reading as neither.</param>
    /// <param name="cellVoxels">The watershed lattice, which is what the table is keyed on.</param>
    public SeepIndex(
        ISurfaceHeights seed, ISeepTerrain terrain, int cellVoxels = HeightField.DefaultCellVoxels)
    {
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentOutOfRangeException.ThrowIfLessThan(cellVoxels, 1);

        _seed = seed;
        _terrain = terrain;
        CellVoxels = cellVoxels;
    }

    public int CellVoxels { get; }

    /// <summary>
    /// Whether a chunk counts as dug, so that a holdable voxel in it may be an inside. Everything
    /// by default; <see cref="PortalTracker"/> binds its own edited set. Part of the definition,
    /// so the oracle that evaluates <see cref="FacesOf"/> from scratch sees the same rule.
    /// </summary>
    public Func<ChunkPos, bool> IsDug { get; set; } = static _ => true;

    /// <summary>Faces in the index.</summary>
    public int Count => _count;

    /// <summary>Chunks with at least one face filed under them.</summary>
    public int ChunkCount => _byChunk.Count;

    /// <summary>Every chunk with a face. What a tick walks.</summary>
    public IEnumerable<ChunkPos> Chunks => _byChunk.Keys;

    /// <summary>Litres a second through a chunk, from its coefficient and the shipped or swept scale.</summary>
    public static double LitresPerSecond(double coefficient, double seepageScale) => coefficient * seepageScale;

    /// <summary>The world-lattice sample a voxel sits in.</summary>
    public (int X, int Z) SampleOf(VoxelPos voxel) =>
        (FloorDiv(voxel.X, CellVoxels), FloorDiv(voxel.Z, CellVoxels));

    /// <summary>
    /// The faces whose inside is in this chunk, lowest first — the delivery order — and within
    /// one Y by X, Z and then floor, walls, roof. Empty for a chunk with none.
    /// </summary>
    public IReadOnlyList<SeepFace> FacesIn(ChunkPos chunk)
    {
        if (!_byChunk.TryGetValue(chunk, out ChunkSeeps? seeps))
        {
            return [];
        }

        Refresh(seeps);
        return seeps.Sorted;
    }

    /// <summary>The chunk's faces summed per (sample, Y), in the same order as <see cref="FacesIn"/>.</summary>
    public IReadOnlyList<SeepGroup> GroupsIn(ChunkPos chunk)
    {
        if (!_byChunk.TryGetValue(chunk, out ChunkSeeps? seeps))
        {
            return [];
        }

        Refresh(seeps);
        return seeps.Groups;
    }

    /// <summary>
    /// A number that changes whenever a face is filed or removed under this chunk, drawn from one
    /// counter over the whole index so it never repeats. The drain records it when it finds every
    /// face full, and looks again when it has moved. Zero for a chunk with no faces.
    /// </summary>
    public long RevisionOf(ChunkPos chunk) =>
        _byChunk.TryGetValue(chunk, out ChunkSeeps? seeps) ? seeps.Revision : 0;

    /// <summary>True when this exact face — geometry and permeability — is filed.</summary>
    public bool Contains(SeepFace face) =>
        _byChunk.TryGetValue(face.Inside.Chunk, out ChunkSeeps? seeps)
        && seeps.Faces.TryGetValue((face.Inside, face.Rock), out float permeability)
        && permeability == face.Permeability;

    /// <summary>
    /// The chunk's seepage under this table: the sum over its faces of permeability × head in
    /// metres × face area, in cubic metres. Multiply by a seepage scale for litres a second.
    /// Zero for a chunk with no face under a lake.
    /// </summary>
    /// <remarks>
    /// Head is the table over the face's column less the centre of the inside voxel, and a face at
    /// or above the table contributes nothing — it is where the water would stand if the wall were
    /// not there, and a wall between equal levels passes nothing either way. Per group, with the
    /// table read once per sample: a chunk's footprint is at most sixteen samples at the 2 m cell.
    /// </remarks>
    public double Coefficient(ChunkPos chunk, IWaterTable table)
    {
        ArgumentNullException.ThrowIfNull(table);

        if (!_byChunk.TryGetValue(chunk, out ChunkSeeps? seeps))
        {
            return 0;
        }

        Refresh(seeps);
        _tableCache.Clear();

        double sumVoxelHead = 0;
        foreach (SeepGroup group in seeps.Groups)
        {
            (int X, int Z) key = (group.SampleX, group.SampleZ);
            if (!_tableCache.TryGetValue(key, out double? level))
            {
                level = table.TableAt(group.SampleX, group.SampleZ);
                _tableCache[key] = level;
            }

            if (level is null)
            {
                continue;
            }

            double head = level.Value - (group.Y + 0.5);
            if (head > 0)
            {
                sumVoxelHead += group.Permeability * head;
            }
        }

        return sumVoxelHead * VoxelConstants.VoxelSize * FaceAreaSquareMetres;
    }

    /// <summary>
    /// The sub-litre seepage a chunk is owed from previous ticks, in millilitres. Zero for a chunk
    /// with no faces, and lost with the chunk.
    /// </summary>
    public long RemainderMillilitres(ChunkPos chunk) =>
        _byChunk.TryGetValue(chunk, out ChunkSeeps? seeps) ? seeps.RemainderMillilitres : 0;

    /// <summary>
    /// Carries a remainder for a chunk that has faces. Bounded at one cell — the drain stops owing
    /// a chunk whose faces have nowhere to put it — and refused for a chunk with nothing to seep
    /// through, because a remainder is a debt against faces and a chunk with none cannot owe one.
    /// </summary>
    public void SetRemainderMillilitres(ChunkPos chunk, long millilitres)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(millilitres);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(millilitres, FluidConstants.CellCapacityMillilitres);

        if (!_byChunk.TryGetValue(chunk, out ChunkSeeps? seeps))
        {
            throw new InvalidOperationException(
                $"Chunk {chunk} has no seep faces, so it cannot carry a remainder of {millilitres} mL.");
        }

        seeps.RemainderMillilitres = millilitres;
    }

    /// <summary>
    /// The definition: every seep face whose inside is <paramref name="inside"/>, appended to
    /// <paramref name="into"/>. Zero to six of them.
    /// </summary>
    /// <remarks>
    /// A voxel above its column's seed surface is never an inside — that volume is the lake's. At
    /// or below it, and holdable, it has a face for every neighbour that is loaded rock with a
    /// permeability above zero, in <see cref="Faces"/> order: floor, the four walls, the roof.
    /// </remarks>
    public void FacesOf(VoxelPos inside, List<SeepFace> into)
    {
        ArgumentNullException.ThrowIfNull(into);

        if (inside.Y > _seed.SurfaceHeightAt(inside.X, inside.Z) || !IsDug(inside.Chunk) || !_terrain.CanHold(inside))
        {
            return;
        }

        foreach ((int dx, int dy, int dz) in Faces)
        {
            var rock = new VoxelPos(inside.X + dx, inside.Y + dy, inside.Z + dz);
            if (_terrain.TryGetRock(rock, out float permeability) && permeability > 0f)
            {
                into.Add(new SeepFace(inside, rock, permeability));
            }
        }
    }

    /// <summary>
    /// Re-evaluates every pair the voxel at <paramref name="changed"/> can be half of, adding the
    /// ones that are faces now and removing the ones that no longer are.
    /// </summary>
    public void OnVoxelChanged(VoxelPos changed)
    {
        int surface = _seed.SurfaceHeightAt(changed.X, changed.Z);

        if (_terrain.CanHold(changed))
        {
            // Water can be here now, so nothing seeps through it: every face with this voxel as
            // the rock is gone, and no terrain read says otherwise.
            foreach ((int dx, int dy, int dz) in Faces)
            {
                Remove((new VoxelPos(changed.X + dx, changed.Y + dy, changed.Z + dz), changed));
            }

            // As the inside. Geometry first: above its surface it is never one, and outside a
            // dug chunk neither (a natural cave in an unedited chunk).
            if (changed.Y <= surface && IsDug(changed.Chunk))
            {
                foreach ((int dx, int dy, int dz) in Faces)
                {
                    var rock = new VoxelPos(changed.X + dx, changed.Y + dy, changed.Z + dz);
                    bool isFace = _terrain.TryGetRock(rock, out float permeability) && permeability > 0f;
                    Reconcile((changed, rock), isFace, permeability);
                }
            }

            return;
        }

        // Rock, sealed rock, or not loaded: nothing arrives in it, so every face with it as the
        // inside is gone, and it is a rock for its neighbours only if it passes water.
        foreach ((int dx, int dy, int dz) in Faces)
        {
            Remove((changed, new VoxelPos(changed.X + dx, changed.Y + dy, changed.Z + dz)));
        }

        bool isRock = _terrain.TryGetRock(changed, out float rockPermeability) && rockPermeability > 0f;
        foreach ((int dx, int dy, int dz) in Faces)
        {
            var inside = new VoxelPos(changed.X + dx, changed.Y + dy, changed.Z + dz);
            if (!isRock)
            {
                Remove((inside, changed));
                continue;
            }

            // The vertical neighbours share this voxel's column, and its surface was read once.
            int insideSurface = dx == 0 && dz == 0 ? surface : _seed.SurfaceHeightAt(inside.X, inside.Z);
            bool isFace = inside.Y <= insideSurface && IsDug(inside.Chunk) && _terrain.CanHold(inside);
            Reconcile((inside, changed), isFace, rockPermeability);
        }
    }

    /// <summary>
    /// Adds every face touching this chunk — inside or rock in it — that is not already filed.
    /// Returns how many were added. Idempotent.
    /// </summary>
    /// <remarks>
    /// The chunk's own voxels with all six faces, and the six slabs of the one-voxel shell with the
    /// single face pointing into the chunk — a shell voxel's other five faces touch nothing in it.
    /// A column whose seed surface lies below the chunk is skipped whole: every inside is at or
    /// below its surface, so there is none in it. Below the surface there is no geometric exit,
    /// which is what makes this scan dearer than the portal scan of the same chunk.
    /// </remarks>
    public int Scan(ChunkPos chunk)
    {
        if (_terrain.TryReadNeighbourhood(chunk, _block))
        {
            return ScanBlock(chunk, _block);
        }

        VoxelPos origin = chunk.OriginVoxel;
        int y0 = origin.Y;
        int y1 = y0 + VoxelConstants.ChunkSize - 1;
        int added = 0;
        var found = new List<SeepFace>(Faces.Length);

        for (int lz = -1; lz <= VoxelConstants.ChunkSize; lz++)
        {
            bool zInside = lz >= 0 && lz < VoxelConstants.ChunkSize;
            for (int lx = -1; lx <= VoxelConstants.ChunkSize; lx++)
            {
                bool xInside = lx >= 0 && lx < VoxelConstants.ChunkSize;
                if (!xInside && !zInside)
                {
                    // A shell edge: no face neighbour in the chunk.
                    continue;
                }

                int x = origin.X + lx;
                int z = origin.Z + lz;
                int surface = _seed.SurfaceHeightAt(x, z);
                if (surface < y0 - 1)
                {
                    continue;
                }

                if (xInside && zInside)
                {
                    int to = Math.Min(y1, surface);
                    for (int y = y0; y <= to; y++)
                    {
                        found.Clear();
                        FacesOf(new VoxelPos(x, y, z), found);
                        foreach (SeepFace face in found)
                        {
                            if (Add(face))
                            {
                                added++;
                            }
                        }
                    }

                    // The shell voxel below, through the chunk's floor row; the one above,
                    // through its roof row.
                    if (TryFace(new VoxelPos(x, y0 - 1, z), surface, 0, 1, 0, out SeepFace below) && Add(below))
                    {
                        added++;
                    }

                    if (y1 + 1 <= surface
                        && TryFace(new VoxelPos(x, y1 + 1, z), surface, 0, -1, 0, out SeepFace above)
                        && Add(above))
                    {
                        added++;
                    }
                }
                else
                {
                    // A side slab of the shell: its one face into the chunk.
                    int dx = lx < 0 ? 1 : lx >= VoxelConstants.ChunkSize ? -1 : 0;
                    int dz = lz < 0 ? 1 : lz >= VoxelConstants.ChunkSize ? -1 : 0;
                    int to = Math.Min(y1, surface);
                    for (int y = y0; y <= to; y++)
                    {
                        if (TryFace(new VoxelPos(x, y, z), surface, dx, 0, dz, out SeepFace face) && Add(face))
                        {
                            added++;
                        }
                    }
                }
            }
        }

        return added;
    }

    /// <summary>
    /// The scan over a classified block: the definition again, as array reads — an inside is a
    /// holdable cell at or below its surface, a face is a rock neighbour with permeability above
    /// zero, and the shell contributes its one face into the chunk. The same faces the per-voxel
    /// scan files, which is a test in both directions.
    /// </summary>
    private int ScanBlock(ChunkPos chunk, SeepNeighbourhood block)
    {
        VoxelPos origin = chunk.OriginVoxel;
        int y0 = origin.Y;
        int y1 = y0 + VoxelConstants.ChunkSize - 1;
        int added = 0;

        // The dug rule per chunk, read once each: the footprint, the chunk below, the chunk above,
        // and the four beside.
        bool dug = IsDug(chunk);
        bool dugBelow = IsDug(chunk.Offset(0, -1, 0));
        bool dugAbove = IsDug(chunk.Offset(0, 1, 0));

        for (int lz = -1; lz <= VoxelConstants.ChunkSize; lz++)
        {
            bool zInside = lz >= 0 && lz < VoxelConstants.ChunkSize;
            for (int lx = -1; lx <= VoxelConstants.ChunkSize; lx++)
            {
                bool xInside = lx >= 0 && lx < VoxelConstants.ChunkSize;
                if (!xInside && !zInside)
                {
                    continue;
                }

                int x = origin.X + lx;
                int z = origin.Z + lz;
                int surface = _seed.SurfaceHeightAt(x, z);
                if (surface < y0 - 1)
                {
                    continue;
                }

                if (xInside && zInside)
                {
                    int to = dug ? Math.Min(y1, surface) : y0 - 1;
                    for (int y = y0; y <= to; y++)
                    {
                        int ly = y - y0;
                        if (block[lx, ly, lz] != SeepNeighbourhood.Holdable)
                        {
                            continue;
                        }

                        var inside = new VoxelPos(x, y, z);
                        foreach ((int dx, int dy, int dz) in Faces)
                        {
                            float rock = block[lx + dx, ly + dy, lz + dz];
                            if (rock > 0f && Add(new SeepFace(inside, inside.Offset(dx, dy, dz), rock)))
                            {
                                added++;
                            }
                        }
                    }

                    // The shell voxel below, through the chunk's floor row; the one above,
                    // through its roof row.
                    if (dugBelow && block[lx, -1, lz] == SeepNeighbourhood.Holdable)
                    {
                        float rock = block[lx, 0, lz];
                        var inside = new VoxelPos(x, y0 - 1, z);
                        if (rock > 0f && Add(new SeepFace(inside, inside with { Y = y0 }, rock)))
                        {
                            added++;
                        }
                    }

                    if (dugAbove && y1 + 1 <= surface && block[lx, VoxelConstants.ChunkSize, lz] == SeepNeighbourhood.Holdable)
                    {
                        float rock = block[lx, VoxelConstants.ChunkSize - 1, lz];
                        var inside = new VoxelPos(x, y1 + 1, z);
                        if (rock > 0f && Add(new SeepFace(inside, inside with { Y = y1 }, rock)))
                        {
                            added++;
                        }
                    }
                }
                else
                {
                    int dx = lx < 0 ? 1 : lx >= VoxelConstants.ChunkSize ? -1 : 0;
                    int dz = lz < 0 ? 1 : lz >= VoxelConstants.ChunkSize ? -1 : 0;
                    if (!IsDug(chunk.Offset(-dx, 0, -dz)))
                    {
                        continue;
                    }

                    int to = Math.Min(y1, surface);
                    for (int y = y0; y <= to; y++)
                    {
                        int ly = y - y0;
                        if (block[lx, ly, lz] != SeepNeighbourhood.Holdable)
                        {
                            continue;
                        }

                        float rock = block[lx + dx, ly, lz + dz];
                        var inside = new VoxelPos(x, y, z);
                        if (rock > 0f && Add(new SeepFace(inside, inside.Offset(dx, 0, dz), rock)))
                        {
                            added++;
                        }
                    }
                }
            }
        }

        return added;
    }

    /// <summary>Removes every face with either half in this chunk, and the chunk's remainder. Returns how many.</summary>
    public int Forget(ChunkPos chunk)
    {
        int removed = 0;

        if (_byChunk.Remove(chunk, out ChunkSeeps? seeps))
        {
            foreach ((VoxelPos Inside, VoxelPos Rock) pair in seeps.Faces.Keys)
            {
                ChunkPos rockChunk = pair.Rock.Chunk;
                if (rockChunk != chunk)
                {
                    Unlist(rockChunk, pair);
                }
            }

            removed += seeps.Faces.Count;
            _count -= seeps.Faces.Count;
        }

        if (_byRockChunk.Remove(chunk, out HashSet<(VoxelPos Inside, VoxelPos Rock)>? crossing))
        {
            foreach ((VoxelPos Inside, VoxelPos Rock) pair in crossing)
            {
                ChunkSeeps store = _byChunk[pair.Inside.Chunk];
                store.Faces.Remove(pair);
                store.Stale = true;
                store.Revision = ++_revision;
                _count--;
                removed++;
                if (store.Faces.Count == 0)
                {
                    _byChunk.Remove(pair.Inside.Chunk);
                }
            }
        }

        return removed;
    }

    private bool TryFace(VoxelPos inside, int surface, int dx, int dy, int dz, out SeepFace face)
    {
        face = default;
        if (inside.Y > surface || !IsDug(inside.Chunk) || !_terrain.CanHold(inside))
        {
            return false;
        }

        var rock = new VoxelPos(inside.X + dx, inside.Y + dy, inside.Z + dz);
        if (!_terrain.TryGetRock(rock, out float permeability) || permeability <= 0f)
        {
            return false;
        }

        face = new SeepFace(inside, rock, permeability);
        return true;
    }

    private void Reconcile((VoxelPos Inside, VoxelPos Rock) pair, bool isFace, float permeability)
    {
        if (isFace)
        {
            Add(new SeepFace(pair.Inside, pair.Rock, permeability));
        }
        else
        {
            Remove(pair);
        }
    }

    /// <summary>Files a face; true if it was new or its permeability changed.</summary>
    private bool Add(SeepFace face)
    {
        (VoxelPos Inside, VoxelPos Rock) pair = (face.Inside, face.Rock);
        ChunkPos chunk = face.Inside.Chunk;
        if (!_byChunk.TryGetValue(chunk, out ChunkSeeps? seeps))
        {
            seeps = new ChunkSeeps();
            _byChunk[chunk] = seeps;
        }

        if (seeps.Faces.TryGetValue(pair, out float existing))
        {
            if (existing == face.Permeability)
            {
                return false;
            }

            seeps.Faces[pair] = face.Permeability;
            seeps.Stale = true;
            seeps.Revision = ++_revision;
            return true;
        }

        seeps.Faces[pair] = face.Permeability;
        seeps.Stale = true;
        seeps.Revision = ++_revision;
        _count++;

        ChunkPos rockChunk = face.Rock.Chunk;
        if (rockChunk != chunk)
        {
            if (!_byRockChunk.TryGetValue(rockChunk, out HashSet<(VoxelPos Inside, VoxelPos Rock)>? crossing))
            {
                crossing = [];
                _byRockChunk[rockChunk] = crossing;
            }

            crossing.Add(pair);
        }

        return true;
    }

    private void Remove((VoxelPos Inside, VoxelPos Rock) pair)
    {
        ChunkPos chunk = pair.Inside.Chunk;
        if (!_byChunk.TryGetValue(chunk, out ChunkSeeps? seeps) || !seeps.Faces.Remove(pair))
        {
            return;
        }

        seeps.Stale = true;
        seeps.Revision = ++_revision;
        _count--;
        if (seeps.Faces.Count == 0)
        {
            _byChunk.Remove(chunk);
        }

        ChunkPos rockChunk = pair.Rock.Chunk;
        if (rockChunk != chunk)
        {
            Unlist(rockChunk, pair);
        }
    }

    private void Unlist(ChunkPos rockChunk, (VoxelPos Inside, VoxelPos Rock) pair)
    {
        if (_byRockChunk.TryGetValue(rockChunk, out HashSet<(VoxelPos Inside, VoxelPos Rock)>? crossing))
        {
            crossing.Remove(pair);
            if (crossing.Count == 0)
            {
                _byRockChunk.Remove(rockChunk);
            }
        }
    }

    private void Refresh(ChunkSeeps seeps)
    {
        if (!seeps.Stale)
        {
            return;
        }

        seeps.Sorted.Clear();
        foreach (((VoxelPos inside, VoxelPos rock), float permeability) in seeps.Faces)
        {
            seeps.Sorted.Add(new SeepFace(inside, rock, permeability));
        }

        seeps.Sorted.Sort(static (a, b) =>
        {
            int c = a.Inside.Y.CompareTo(b.Inside.Y);
            if (c != 0) return c;
            c = a.Inside.X.CompareTo(b.Inside.X);
            if (c != 0) return c;
            c = a.Inside.Z.CompareTo(b.Inside.Z);
            if (c != 0) return c;
            c = a.Rock.Y.CompareTo(b.Rock.Y);
            if (c != 0) return c;
            c = a.Rock.X.CompareTo(b.Rock.X);
            return c != 0 ? c : a.Rock.Z.CompareTo(b.Rock.Z);
        });

        // Summed in sorted order, so the same faces give the same double twice; a sample's
        // faces at one Y are not contiguous in the sort (Z varies inside a sample), so the sum
        // goes through a dictionary and the groups come out in the sort's own order.
        _groupSums.Clear();
        foreach (SeepFace face in seeps.Sorted)
        {
            (int sx, int sz) = SampleOf(face.Inside);
            (int, int, int) key = (face.Y, sx, sz);
            _groupSums[key] = (_groupSums.TryGetValue(key, out double sum) ? sum : 0) + face.Permeability;
        }

        seeps.Groups.Clear();
        foreach (((int y, int sx, int sz), double permeability) in _groupSums)
        {
            seeps.Groups.Add(new SeepGroup(sx, sz, y, permeability));
        }

        seeps.Groups.Sort(static (a, b) =>
        {
            int c = a.Y.CompareTo(b.Y);
            if (c != 0) return c;
            c = a.SampleX.CompareTo(b.SampleX);
            return c != 0 ? c : a.SampleZ.CompareTo(b.SampleZ);
        });

        seeps.Stale = false;
    }

    private static int FloorDiv(int value, int divisor)
    {
        int quotient = value / divisor;
        return value % divisor != 0 && (value < 0) != (divisor < 0) ? quotient - 1 : quotient;
    }
}
