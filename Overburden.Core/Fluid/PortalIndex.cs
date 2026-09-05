using Overburden.Core.Hydrology;
using Overburden.Core.Lighting;
using Overburden.Core.Voxels;

namespace Overburden.Core.Fluid;

/// <summary>
/// A hole in the analytic ground, as one face: the holdable voxel at or below its column's seed
/// surface, and the face neighbour above <i>its own</i> column's surface that it opens onto.
/// </summary>
/// <remarks>
/// <see cref="Inside"/> is where Tier B's water arrives; <see cref="Outside"/> is where Tier A's
/// water is, or will be, and it is the voxel the covered test asks about — a lake whose level
/// stands above <c>Outside.Y</c> has water against this face. Two faces of one dug voxel are two
/// portals, because they can open onto two different samples and therefore two different basins.
/// </remarks>
public readonly record struct Portal(VoxelPos Inside, VoxelPos Outside)
{
    /// <summary>True when the outside voxel is the one above: a hole in a floor rather than a bank.</summary>
    public bool IsPit => Outside.Y == Inside.Y + 1;
}

/// <summary>
/// Every portal in the scanned world, filed under the 4 m watershed sample its outside voxel
/// sits in, so that a tick walks only the samples that are wet.
/// </summary>
/// <remarks>
/// <para>
/// <b>The definition is <see cref="OpeningsOf"/>, and everything else here is bookkeeping that
/// has to agree with it.</b> The oracle is M5b's: after any sequence of scans, edits and forgets
/// the index equals a from-scratch evaluation of the definition over every voxel of every
/// scanned chunk — and here from-scratch is cheap enough to be the definition, so the test runs
/// it. A portal's geometry (which column, which Y, which face) never changes; only holdability
/// does, so a pair is in the index iff its geometry is right <i>and</i> both voxels hold now.
/// </para>
/// <para>
/// <b>Filed under the outside voxel's sample, not the inside's.</b> A tunnel driven under a ridge
/// into a lake bank has its inside under the dry hillside's sample and its outside under the
/// lake's; the tick asks "which samples are wet" first, so filing by the inside would skip the
/// one portal a bank breach is.
/// </para>
/// <para>
/// <b>A scan takes every pair touching the chunk, inside or outside</b>, because a pit whose
/// mouth is the top row of one chunk opens onto the bottom row of the chunk above, and whichever
/// half arrives second has to find it. The same rule makes <see cref="Forget"/> remove pairs
/// with either half in the chunk — an outside voxel that has unloaded reads as solid, so the pair
/// is not a portal until it is back, and the scan on its return finds it again.
/// </para>
/// <para>
/// <b>An edit re-evaluates ten pairs, not one voxel.</b> A change at <c>p</c> can open or close a
/// portal with <c>p</c> inside (five candidate outsides) or with <c>p</c> outside (the voxel
/// below and the four beside it, as insides). M8b-1's rule for <c>Disturb</c> applies: a chunk
/// bite is 32,768 of these, so the geometric exits — a voxel above its column's surface is never
/// inside, a voxel at or below its own surface is never outside — come before any terrain read,
/// and the five seed surfaces are read once per call. In the game the seed surface has to be
/// array reads (<see cref="SkyColumn.SeedSurfaceY"/>) rather than the analytic function, or a
/// deep bite pays five noise evaluations per voxel for nothing. <b>Lever, not built: a chunk
/// wholly below its footprint's surface band can skip the hook entirely</b>, which is most of a
/// deep mine.
/// </para>
/// <para>
/// This is where DESIGN's "the breach is detected at voxel resolution on the cell face" lives —
/// M5b's rule with "water" for "light" — and it is why a 0.75 m bore drains a lake that a 4 m
/// re-analysis of the heights would never see. The index does not know what an edit is: M8b-2's
/// "a hole is only what was dug" is the caller's filter on which chunks it scans, and a chunk
/// scanned with a natural cave mouth on a cliff will file it. A natural cave can never be a pit,
/// because worldgen keeps every cave <c>CaveSurfaceMarginVoxels</c> below its own column's
/// surface, and that is a test.
/// </para>
/// </remarks>
public sealed class PortalIndex
{
    private static readonly (int X, int Z)[] Sides = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    private readonly ISurfaceHeights _seed;
    private readonly IFluidTerrain _terrain;
    private readonly Dictionary<(int X, int Z), List<Portal>> _bySample = [];
    private readonly HashSet<Portal> _portals = [];

    /// <param name="seed">The unedited surface — the world generator, or the sky columns' copy of it.</param>
    /// <param name="terrain">Holdability now, unloaded reading as solid.</param>
    /// <param name="cellVoxels">The watershed lattice, which is what the index is keyed on.</param>
    public PortalIndex(
        ISurfaceHeights seed, IFluidTerrain terrain, int cellVoxels = HeightField.DefaultCellVoxels)
    {
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentOutOfRangeException.ThrowIfLessThan(cellVoxels, 1);

        _seed = seed;
        _terrain = terrain;
        CellVoxels = cellVoxels;
    }

    public int CellVoxels { get; }

    /// <summary>Portals in the index.</summary>
    public int Count => _portals.Count;

    /// <summary>Samples with at least one portal filed under them.</summary>
    public int SampleCount => _bySample.Count;

    /// <summary>Every sample with a portal, on the world lattice. What a tick walks.</summary>
    public IEnumerable<(int X, int Z)> Samples => _bySample.Keys;

    /// <summary>The portals whose outside voxel sits in this sample, in discovery order.</summary>
    public IReadOnlyList<Portal> Under(int sampleX, int sampleZ) =>
        _bySample.TryGetValue((sampleX, sampleZ), out List<Portal>? list) ? list : [];

    public bool Contains(Portal portal) => _portals.Contains(portal);

    /// <summary>The world-lattice sample a voxel sits in.</summary>
    public (int X, int Z) SampleOf(VoxelPos voxel) =>
        (FloorDiv(voxel.X, CellVoxels), FloorDiv(voxel.Z, CellVoxels));

    /// <summary>
    /// The definition: every portal whose inside is <paramref name="inside"/>, appended to
    /// <paramref name="into"/>. Zero to five of them.
    /// </summary>
    /// <remarks>
    /// A voxel above its column's seed surface is never inside. At the surface it opens upward
    /// onto the first open voxel, which is above its own surface by construction. At or below the
    /// surface it opens sideways onto any neighbour that is above <i>that</i> column's surface.
    /// The voxel below never qualifies: it would have to be above its surface while this one is
    /// at or below the same surface, and it is lower.
    /// </remarks>
    public void OpeningsOf(VoxelPos inside, List<Portal> into)
    {
        ArgumentNullException.ThrowIfNull(into);

        int surface = _seed.SurfaceHeightAt(inside.X, inside.Z);
        if (inside.Y > surface || !_terrain.CanHold(inside))
        {
            return;
        }

        if (inside.Y == surface)
        {
            VoxelPos above = inside with { Y = surface + 1 };
            if (_terrain.CanHold(above))
            {
                into.Add(new Portal(inside, above));
            }
        }

        foreach ((int dx, int dz) in Sides)
        {
            var beside = new VoxelPos(inside.X + dx, inside.Y, inside.Z + dz);
            if (inside.Y > _seed.SurfaceHeightAt(beside.X, beside.Z) && _terrain.CanHold(beside))
            {
                into.Add(new Portal(inside, beside));
            }
        }
    }

    /// <summary>
    /// Re-evaluates every pair the voxel at <paramref name="changed"/> can be half of, adding the
    /// ones that are portals now and removing the ones that no longer are.
    /// </summary>
    public void OnVoxelChanged(VoxelPos changed)
    {
        int surface = _seed.SurfaceHeightAt(changed.X, changed.Z);
        Span<int> beside = stackalloc int[Sides.Length];
        for (int i = 0; i < Sides.Length; i++)
        {
            beside[i] = _seed.SurfaceHeightAt(changed.X + Sides[i].X, changed.Z + Sides[i].Z);
        }

        // Read lazily: most of a deep bite never needs it, and that is the cost the plan priced.
        bool? holds = null;
        bool Holds() => holds ??= _terrain.CanHold(changed);

        // As the inside of a pair. Geometry first, terrain second.
        if (changed.Y <= surface)
        {
            if (changed.Y == surface)
            {
                VoxelPos above = changed with { Y = surface + 1 };
                Reconcile(new Portal(changed, above), Holds() && _terrain.CanHold(above));
            }

            for (int i = 0; i < Sides.Length; i++)
            {
                if (changed.Y > beside[i])
                {
                    var outside = new VoxelPos(changed.X + Sides[i].X, changed.Y, changed.Z + Sides[i].Z);
                    Reconcile(new Portal(changed, outside), Holds() && _terrain.CanHold(outside));
                }
            }
        }

        // As the outside of a pair: only a voxel above its own surface ever is.
        if (changed.Y > surface)
        {
            if (changed.Y == surface + 1)
            {
                VoxelPos below = changed with { Y = surface };
                Reconcile(new Portal(below, changed), Holds() && _terrain.CanHold(below));
            }

            for (int i = 0; i < Sides.Length; i++)
            {
                if (changed.Y <= beside[i])
                {
                    var inside = new VoxelPos(changed.X + Sides[i].X, changed.Y, changed.Z + Sides[i].Z);
                    Reconcile(new Portal(inside, changed), Holds() && _terrain.CanHold(inside));
                }
            }
        }
    }

    /// <summary>
    /// Adds every portal touching this chunk — inside or outside in it — that is not already
    /// filed. Returns how many were added. Idempotent.
    /// </summary>
    /// <remarks>
    /// Per column of the footprint and the one-voxel ring around it: the surface voxel, and the
    /// bank face from just above the lowest neighbouring surface up to this column's own — the
    /// only Ys the definition allows an inside to have. A few thousand pair evaluations a chunk,
    /// most of them geometric exits.
    /// </remarks>
    public int Scan(ChunkPos chunk)
    {
        VoxelPos origin = chunk.OriginVoxel;
        int y0 = origin.Y;
        int y1 = y0 + VoxelConstants.ChunkSize - 1;
        int added = 0;
        var found = new List<Portal>(5);

        for (int lz = -1; lz <= VoxelConstants.ChunkSize; lz++)
        {
            for (int lx = -1; lx <= VoxelConstants.ChunkSize; lx++)
            {
                int x = origin.X + lx;
                int z = origin.Z + lz;
                int surface = _seed.SurfaceHeightAt(x, z);
                if (surface < y0 - 1)
                {
                    // Every inside in this column is at or below its surface, so below the chunk
                    // and below anything the chunk could be outside of.
                    continue;
                }

                int lowest = surface;
                foreach ((int dx, int dz) in Sides)
                {
                    lowest = Math.Min(lowest, _seed.SurfaceHeightAt(x + dx, z + dz));
                }

                // Insides at Y in (lowest, surface], clipped to one voxel under the chunk (whose
                // outside above can be the chunk's floor) and to the chunk's top.
                int from = Math.Max(y0 - 1, lowest + 1);
                int to = Math.Min(y1, surface);
                if (surface >= y0 - 1 && surface <= y1)
                {
                    from = Math.Min(from, surface);
                }

                for (int y = from; y <= to; y++)
                {
                    found.Clear();
                    OpeningsOf(new VoxelPos(x, y, z), found);
                    foreach (Portal portal in found)
                    {
                        if (Touches(portal, chunk) && _portals.Add(portal))
                        {
                            File(portal);
                            added++;
                        }
                    }
                }
            }
        }

        return added;
    }

    /// <summary>Removes every portal with either half in this chunk. Returns how many.</summary>
    public int Forget(ChunkPos chunk)
    {
        VoxelPos origin = chunk.OriginVoxel;
        int removed = 0;

        // An outside is a face neighbour of an inside in the chunk, so it sits at most one voxel
        // outside the footprint — one sample either side covers every key a touching pair can be
        // filed under.
        int kx0 = FloorDiv(origin.X - 1, CellVoxels);
        int kx1 = FloorDiv(origin.X + VoxelConstants.ChunkSize, CellVoxels);
        int kz0 = FloorDiv(origin.Z - 1, CellVoxels);
        int kz1 = FloorDiv(origin.Z + VoxelConstants.ChunkSize, CellVoxels);

        for (int kz = kz0; kz <= kz1; kz++)
        {
            for (int kx = kx0; kx <= kx1; kx++)
            {
                if (!_bySample.TryGetValue((kx, kz), out List<Portal>? list))
                {
                    continue;
                }

                removed += list.RemoveAll(portal => Touches(portal, chunk) && _portals.Remove(portal));
                if (list.Count == 0)
                {
                    _bySample.Remove((kx, kz));
                }
            }
        }

        return removed;
    }

    private static bool Touches(Portal portal, ChunkPos chunk) =>
        portal.Inside.Chunk == chunk || portal.Outside.Chunk == chunk;

    private void Reconcile(Portal portal, bool isPortal)
    {
        if (isPortal)
        {
            if (_portals.Add(portal))
            {
                File(portal);
            }
        }
        else if (_portals.Remove(portal))
        {
            Unfile(portal);
        }
    }

    private void File(Portal portal)
    {
        (int X, int Z) key = SampleOf(portal.Outside);
        if (!_bySample.TryGetValue(key, out List<Portal>? list))
        {
            list = [];
            _bySample[key] = list;
        }

        list.Add(portal);
    }

    private void Unfile(Portal portal)
    {
        (int X, int Z) key = SampleOf(portal.Outside);
        List<Portal> list = _bySample[key];
        list.Remove(portal);
        if (list.Count == 0)
        {
            _bySample.Remove(key);
        }
    }

    private static int FloorDiv(int value, int divisor)
    {
        int quotient = value / divisor;
        return value % divisor != 0 && (value < 0) != (divisor < 0) ? quotient - 1 : quotient;
    }
}
