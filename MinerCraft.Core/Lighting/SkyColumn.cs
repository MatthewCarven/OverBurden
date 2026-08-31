using MinerCraft.Core.Content;
using MinerCraft.Core.Voxels;

namespace MinerCraft.Core.Lighting;

/// <summary>Anything that can say how high the unedited terrain reaches.</summary>
/// <remarks>
/// <see cref="Worldgen.WorldGenerator"/> is the only implementation. The interface exists so a
/// <see cref="SkyColumn"/> test can state a flat world in one line rather than reverse-engineer
/// a seed that produces one.
/// </remarks>
public interface ISurfaceHeights
{
    /// <summary>Y of the topmost solid voxel of an unedited column.</summary>
    int SurfaceHeightAt(int worldX, int worldZ);
}

/// <summary>
/// For one chunk <b>column</b> (cx, cz), the Y above which each of its 32×32 voxel columns is
/// open to the sky.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every voxel game floods skylight down from the top of the world. This one has no top.</b>
/// The vertical view radius is 2–3 chunks — 16–24 m, a measured and locked decision — so the
/// chunks above are usually not loaded and there is nothing to flood from. The seed is analytic
/// instead: <see cref="ISurfaceHeights.SurfaceHeightAt"/> is already a pure function of the seed
/// and already gives the topmost solid voxel, so a cell above it is full skylight by
/// construction with no propagation and no chunks above.
/// </para>
/// <para>
/// That is exact until the player digs, and digging is the entire game — hence this structure.
/// It is a <b>second refcount, on a different axis and a different object</b>, and M2c's
/// recorded "don't add a second refcount to fix the margin" is not this: the whole point here is
/// surviving the vertical unload the chunk refcount performs as the player descends. The
/// lifetime half belongs to M5c; what lives here are the rules, which are a pure function.
/// </para>
/// <para>
/// <b>This is also exactly the structure M7's rain occlusion needs</b> — "kill particles where
/// skylight is occluded, so rain stops at the mine entrance". It is built a milestone early
/// because skylight cannot be correct without it, not because M7 asked.
/// </para>
/// <para>
/// Its lifetime deliberately matches an edited chunk's: lost on horizontal unload today,
/// persisted at M6. That is consistent with M3's already-recorded limitation that an edited
/// chunk which unloads loses its edits — a shaft whose chunks were regenerated from seed has no
/// shaft to light.
/// </para>
/// </remarks>
public sealed class SkyColumn
{
    private const int Size = VoxelConstants.ChunkSize;

    /// <summary>
    /// Per (x, z): the lowest Y at which this column is open to the sky all the way up.
    /// </summary>
    /// <remarks>
    /// <c>short</c> rather than <c>int</c> halves the structure to 2 KB per column — 338 KB at
    /// r6h's 169 columns — and bounds the world at ±32,767 voxels, which is ±8 km. M4a's float
    /// analysis already caps the usable world at a few kilometres, so this is not the binding
    /// constraint; if a floating origin ever lifts that, this widens with it.
    /// </remarks>
    private readonly short[] _openAboveY = new short[Size * Size];

    /// <param name="chunkX">Chunk X of the column. Chunk Y is deliberately absent.</param>
    public SkyColumn(int chunkX, int chunkZ)
    {
        ChunkX = chunkX;
        ChunkZ = chunkZ;
    }

    public int ChunkX { get; }

    public int ChunkZ { get; }

    /// <summary>
    /// At least one column's open height has moved away from its analytic seed, so this
    /// structure can no longer be reconstructed from the world seed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The mirror of <see cref="Streaming.StreamedChunk.HasEdits"/>, and exactly the set M6
    /// has to write. <b>Set inside <see cref="OnVoxelChanged"/> rather than by its caller</b>,
    /// because a caller that forgets loses a shaft's lighting with nothing failing to say so —
    /// the same argument that puts <c>HasEdits</c> inside <c>SetVoxel</c>.
    /// </para>
    /// <para>
    /// <b>Its test is "the height moved", not "a voxel changed", and that is the tight one.</b>
    /// <see cref="OnVoxelChanged"/> returns true only when the open height actually shifts, so a
    /// column dug out well below its top is still identical to what <see cref="Seed"/> would
    /// produce and is correctly never written. Digging is most of this game, so that distinction
    /// is most of the columns.
    /// </para>
    /// <para>
    /// Never cleared here. Whether this column is in step with what is on disk is the store's
    /// question and a different flag, for the reason M6-PLAN records: one flag carrying both
    /// "differs from the seed" and "differs from disk" loses exactly the last edits before an
    /// unload.
    /// </para>
    /// </remarks>
    public bool HasEdits { get; private set; }

    /// <summary>Marks this column as edit-derived. For M6's restore, which rebuilds one.</summary>
    /// <remarks>
    /// A restored column is edit-derived by definition — it was only written because it had
    /// moved — but it arrives through <see cref="SetOpenAboveY"/>, which is a plain setter used
    /// by tests as well and has no business inferring intent from being called.
    /// </remarks>
    public void MarkEdited() => HasEdits = true;

    /// <summary>How deep an unsupported downward scan may go before giving up.</summary>
    /// <remarks>
    /// <b>Not a correctness bound — the loaded region already is one</b>, because an unloaded
    /// voxel reads as opaque and stops the scan, exactly as it stops the collider. This is the
    /// backstop for a caller handing over a world that answers "air" forever, which a test
    /// fixture easily does, and it keeps a mistake a wrong number rather than a hang.
    /// </remarks>
    public const int MaxScanVoxels = 4096;

    private static int Index(int localX, int localZ) => (localZ * Size) + localX;

    /// <summary>Seeds every column from the unedited surface height.</summary>
    public void Seed(ISurfaceHeights heights)
    {
        int originX = ChunkX << VoxelConstants.ChunkSizeLog2;
        int originZ = ChunkZ << VoxelConstants.ChunkSizeLog2;

        for (int lz = 0; lz < Size; lz++)
        {
            for (int lx = 0; lx < Size; lx++)
            {
                // SurfaceHeightAt is the topmost *solid* voxel, so the sky starts one above it.
                _openAboveY[Index(lx, lz)] =
                    checked((short)(heights.SurfaceHeightAt(originX + lx, originZ + lz) + 1));
            }
        }
    }

    /// <summary>The Y above which this column is open, in world voxel coordinates.</summary>
    public int OpenAboveY(int localX, int localZ) => _openAboveY[Index(localX, localZ)];

    /// <summary>Sets a column's open height directly. For tests and for M6's restore.</summary>
    public void SetOpenAboveY(int localX, int localZ, int y) =>
        _openAboveY[Index(localX, localZ)] = checked((short)y);

    /// <summary>
    /// Re-derives one column's open height after the voxel at <paramref name="changed"/> became
    /// opaque or stopped being. Returns true if the height moved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two cases and nothing in between. Placing something opaque at or above the open height
    /// closes the column down to it, which needs no scan. Removing the column's <em>topmost</em>
    /// opaque voxel opens it further, which does — down to the next opaque voxel.
    /// </para>
    /// <para>
    /// <b>An unloaded voxel counts as opaque, which is what bounds the scan.</b> Same rule the
    /// collider uses and for the same reason: not knowing is not the same as knowing there is
    /// nothing there, and guessing "open" would run the scan out of the world and report a
    /// column lit from below.
    /// </para>
    /// </remarks>
    public bool OnVoxelChanged(
        VoxelPos changed, IVoxelWorld world, MaterialTable materials)
    {
        var (lx, _, lz) = changed.Local;
        int index = Index(lx, lz);
        int openAbove = _openAboveY[index];

        bool opaque = IsOpaque(world, materials, changed);

        if (opaque)
        {
            if (changed.Y < openAbove) return false;

            _openAboveY[index] = checked((short)(changed.Y + 1));
            HasEdits = true;
            return true;
        }

        // Only the topmost opaque voxel matters; anything below it is already in shadow.
        if (changed.Y != openAbove - 1) return false;

        int y = changed.Y;
        int limit = y - MaxScanVoxels;

        while (y > limit && !IsOpaque(world, materials, new VoxelPos(changed.X, y - 1, changed.Z)))
        {
            y--;
        }

        _openAboveY[index] = checked((short)y);

        if (y == openAbove) return false;

        HasEdits = true;
        return true;
    }

    /// <summary>
    /// The skylight a cell is seeded with: full if every voxel in it is open to the sky, else
    /// nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Every column the cell covers, not any of them.</b> A cell straddling a cliff edge is
    /// half open sky and half rock; seeding it full would push daylight into the rock through
    /// the cell's own faces, and the value is per cell so nothing downstream could undo that.
    /// Requiring all sixteen can only ever under-seed, and under-seeding is recoverable — the
    /// cell above is a source, the face between them conducts through the open half, and
    /// skylight falls through it at full value with no attenuation. So the cliff face is lit
    /// anyway, by propagation rather than by seeding.
    /// </para>
    /// <para>
    /// On flat and gently terraced ground the two rules agree, because the sixteen columns are
    /// within a voxel of each other.
    /// </para>
    /// </remarks>
    public bool IsCellOpenToSky(LightCellPos cell)
    {
        VoxelPos min = cell.MinVoxel;
        int baseX = min.X & VoxelConstants.ChunkMask;
        int baseZ = min.Z & VoxelConstants.ChunkMask;

        for (int dz = 0; dz < LightCellPos.VoxelsPerCell; dz++)
        {
            for (int dx = 0; dx < LightCellPos.VoxelsPerCell; dx++)
            {
                if (min.Y < _openAboveY[Index(baseX + dx, baseZ + dz)]) return false;
            }
        }

        return true;
    }

    private static bool IsOpaque(IVoxelWorld world, MaterialTable materials, VoxelPos voxel) =>
        !world.TryGetMaterial(voxel, out int material) || materials.IsOpaque(material);
}
