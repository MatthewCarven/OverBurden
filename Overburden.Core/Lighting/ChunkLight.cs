using Overburden.Core.Content;
using Overburden.Core.Meshing;
using Overburden.Core.Voxels;

namespace Overburden.Core.Lighting;

/// <summary>
/// One chunk's 8×8×8 light cells: the two propagated values, and the three things derived from
/// the voxels that the propagation reads.
/// </summary>
/// <remarks>
/// <para>
/// 512 cells of one byte is the propagated field — 4 bits skylight, 4 bits block light, <b>512
/// bytes per chunk</b>, against ~2 KB of voxel storage and ~46 KiB of mesh. Light is not where
/// the memory goes. The derived side costs another 1 KB as a <c>ushort</c> per cell, which
/// M5-PLAN budgeted at 512 bytes for conductance alone; the extra is the two <em>intrinsic
/// source</em> fields, and they are not optional. The un-light pass zeroes cells as it walks,
/// and it has to know which of them are sources in their own right — otherwise the walk erases
/// the very lamp it is walking away from and only a from-scratch pass could put it back.
/// </para>
/// <para>
/// <b>Everything derived here comes from a <see cref="PaddedChunk"/>, which meshing already
/// gathers.</b> Conductance needs the voxel one past each chunk face, which is exactly the
/// border that copy holds.
/// </para>
/// </remarks>
public sealed class ChunkLight
{
    /// <summary>Brightest a cell can be. 4 bits, so light reaches 15 m.</summary>
    /// <remarks>
    /// <b>This number and the 2-chunk load margin are exactly compatible, and that binds
    /// them.</b> Light reaches 15 m; the margin is 16 m; so a source outside the loaded region
    /// contributes <c>max(0, 15 − 16) = 0</c> at the visible box, and light computed over the
    /// loaded region is <em>exact</em> inside the drawn one rather than approximate. The margin
    /// now has a third load-bearing reason and this is the tightest of the three.
    /// <b>Revisit trigger: 5-bit light reaches 31 m and needs a 4-chunk margin.</b>
    /// </remarks>
    public const int MaxLevel = 15;

    /// <summary>What one grid is charged against the retained-chunk cache's cap.</summary>
    /// <remarks>
    /// The two arrays are exact — 512 bytes of values and 1,024 of derived data — and the rest is
    /// an estimate for three object headers, in the spirit of the cache's other charges.
    /// </remarks>
    public const int EstimatedBytes = (LightCellPos.Volume * (sizeof(byte) + sizeof(ushort))) + 72;

    private const int SkyShift = 0;
    private const int BlockShift = 4;
    private const int LevelMask = 0xF;

    private const int ConductanceShift = 0;
    private const int ConductanceMask = 0x3F;
    private const int SkySourceBit = 1 << 6;
    private const int EmissionShift = 7;

    /// <summary>Low nibble skylight, high nibble block light.</summary>
    private readonly byte[] _light = new byte[LightCellPos.Volume];

    /// <summary>Bits 0–5 conductance per face, bit 6 sky source, bits 7–10 emission.</summary>
    private readonly ushort[] _derived = new ushort[LightCellPos.Volume];

    /// <summary>True once <see cref="BuildDerived"/> has run against real voxels.</summary>
    /// <remarks>
    /// A grid whose derived data has never been built conducts nothing and sources nothing, so
    /// it propagates as solid rock rather than as a hole. That is the safe direction — but it is
    /// also indistinguishable from genuinely solid rock, so the flag exists to let M5c's barrier
    /// tell "not lit yet" from "lit, and dark".
    /// </remarks>
    public bool HasDerived { get; private set; }

    public int Get(int cellIndex, LightChannel channel) =>
        (_light[cellIndex] >> Shift(channel)) & LevelMask;

    /// <summary>Both channels as the stored byte — low nibble sky, high nibble block.</summary>
    /// <remarks>
    /// For <see cref="PaddedLight"/>, which copies whole cells rather than reading channels: one
    /// byte moved instead of two shifts and two masks, across 1,331 cells per mesh submission.
    /// The packing is this type's, so the accessor is too — a caller reconstructing it from two
    /// <see cref="Get"/> calls would be a second copy of the layout.
    /// </remarks>
    public byte Packed(int cellIndex) => _light[cellIndex];

    public void Set(int cellIndex, LightChannel channel, int level)
    {
        int shift = Shift(channel);
        _light[cellIndex] = (byte)((_light[cellIndex] & ~(LevelMask << shift))
                                   | ((level & LevelMask) << shift));
    }

    /// <summary>
    /// What this cell is worth with no neighbours at all: its emission, or full sky.
    /// </summary>
    public int Source(int cellIndex, LightChannel channel) => channel == LightChannel.Sky
        ? ((_derived[cellIndex] & SkySourceBit) != 0 ? MaxLevel : 0)
        : (_derived[cellIndex] >> EmissionShift) & LevelMask;

    /// <summary>Whether light crosses this cell's face in direction <paramref name="face"/>.</summary>
    /// <remarks>
    /// <para>
    /// Faces are <see cref="VoxelFace"/>'s six, in its order. The table is shared rather than
    /// restated because conductance <em>is</em> a statement about voxel faces — two tables of
    /// the same six directions is two tables that can disagree.
    /// </para>
    /// <para>
    /// <b>This cell's half of the answer.</b> The propagator crosses a face only when the cell on
    /// the other side agrees, because the two halves are derived separately and can disagree for
    /// a few frames after a neighbour arrives — see <see cref="LightPropagator"/>.
    /// </para>
    /// </remarks>
    public bool Conducts(int cellIndex, int face) =>
        (_derived[cellIndex] & (1 << face)) != 0;

    /// <summary>Zeroes the propagated values, leaving the derived data alone.</summary>
    public void ClearLight() => Array.Clear(_light);

    /// <summary>Zeroes everything, including the derived data.</summary>
    public void Reset()
    {
        Array.Clear(_light);
        Array.Clear(_derived);
        HasDerived = false;
    }

    /// <summary>
    /// Recomputes conductance, emission and the sky-source flag for every cell.
    /// </summary>
    /// <remarks>
    /// Costs 49,152 voxel-pair tests for conductance plus 32,768 lookups for emission per
    /// chunk — comparable to one mesher sweep, and paid once per chunk rather than once per
    /// relight. The propagated values are left untouched on purpose: an edit rebuilds this and
    /// then <em>repairs</em> the light incrementally, and clearing here would turn every edit
    /// into a from-scratch flood.
    /// </remarks>
    /// <param name="changed">
    /// Every cell whose derived data actually moved is appended here. <b>That set is the exact
    /// repair scope</b> — conductance, emission and the sky flag all reach the propagation the
    /// same way, so one list covers an edit that opened a tunnel, an edit that placed a lamp and
    /// an edit that broke through to daylight, with no case analysis anywhere.
    /// </param>
    public void BuildDerived(
        PaddedChunk voxels,
        MaterialTable materials,
        ChunkPos position,
        SkyColumn sky,
        ICollection<LightCellPos>? changed = null)
    {
        for (int cz = 0; cz < LightCellPos.CellsPerChunk; cz++)
        for (int cx = 0; cx < LightCellPos.CellsPerChunk; cx++)
        for (int cy = 0; cy < LightCellPos.CellsPerChunk; cy++)
        {
            Store(
                position,
                cx, cy, cz,
                Conductance(voxels, materials, cx, cy, cz)
                    | (Emission(voxels, materials, cx, cy, cz) << EmissionShift),
                sky,
                changed);
        }

        HasDerived = true;
    }

    /// <summary>
    /// The same derivation for a chunk whose voxels and whose six face neighbours' voxels are
    /// all one material — which has a closed form and needs no voxel copy at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Light's dependency set is the six face neighbours, not the mesher's twenty-six.</b>
    /// Conductance reads one voxel layer across each cell face and emission reads only the
    /// chunk's own interior, so nothing here ever looks at an edge or a corner. That is what
    /// makes the uniform case checkable at all: seven chunk headers rather than twenty-seven.
    /// </para>
    /// <para>
    /// <b>Uniform storage is 68% of chunks, measured at M2b</b> — solid rock below and open air
    /// above — and the general path costs ~120k voxel reads against this one's 512 cells. The
    /// closed form is small enough to state in one line each: every face conducts iff the
    /// material is not opaque, and every cell emits whatever the material emits.
    /// </para>
    /// <para>
    /// <b>Two derivations of one field is two derivations that can drift</b>, which is the
    /// <c>IsCaveAt</c>/<c>GenerateTerrain</c> problem again, so they share <see cref="Store"/>
    /// and an equality test pins them together against a fresh <see cref="PaddedChunk"/>.
    /// </para>
    /// </remarks>
    public void BuildDerivedUniform(
        int material,
        MaterialTable materials,
        ChunkPos position,
        SkyColumn sky,
        ICollection<LightCellPos>? changed = null)
    {
        int conductance = materials.IsOpaque(material) ? 0 : ConductanceMask;
        int emission = Math.Min(materials.LightEmission(material), MaxLevel);
        int value = conductance | (emission << EmissionShift);

        for (int cz = 0; cz < LightCellPos.CellsPerChunk; cz++)
        for (int cx = 0; cx < LightCellPos.CellsPerChunk; cx++)
        for (int cy = 0; cy < LightCellPos.CellsPerChunk; cy++)
        {
            Store(position, cx, cy, cz, value, sky, changed);
        }

        HasDerived = true;
    }

    /// <summary>
    /// Writes one cell's derived word, adds the sky flag, and reports it if it moved.
    /// </summary>
    /// <remarks>
    /// Shared by both build paths on purpose. The <paramref name="changed"/> contract — that the
    /// reported set is the exact repair scope — is the thing the propagation depends on, and it
    /// is worth exactly one implementation.
    /// </remarks>
    private void Store(
        ChunkPos position,
        int cx,
        int cy,
        int cz,
        int value,
        SkyColumn sky,
        ICollection<LightCellPos>? changed)
    {
        int index = LightCellPos.Index(cx, cy, cz);
        var cell = LightCellPos.FromChunkLocal(position, cx, cy, cz);

        if (sky.IsCellOpenToSky(cell)) value |= SkySourceBit;

        if (changed is not null && _derived[index] != (ushort)value) changed.Add(cell);

        _derived[index] = (ushort)value;
    }


    /// <summary>
    /// Which of a cell's six faces light crosses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Conductance is a property of the face, not of the cell, and that is forced by the
    /// numbers.</b> A 1 m cell holds 64 voxels and <c>hand_pick</c>'s bore is 0.75 m, so the
    /// tunnels this game is about are narrower than one cell. "Any opaque voxel → the cell is
    /// opaque" means a tunnel never conducts and torches light nothing; "any air voxel → the
    /// cell is open" means a 0.25 m wall leaks, and 0.25 m is a <em>normal</em> wall here.
    /// </para>
    /// <para>
    /// So: light crosses a face iff some voxel pair across the shared 4×4 boundary is non-opaque
    /// on both sides. A 0.75 m bore conducts along its axis; a wall one voxel thick does not.
    /// </para>
    /// <para>
    /// <b>The honest cost, recorded rather than discovered:</b> the propagated <em>value</em> is
    /// still per cell, so rock within a metre of a lit tunnel samples that tunnel's light and
    /// glows slightly. Sampling half a cell along the face normal at mesh time hides most of it.
    /// If it still reads badly the lever is a 0.5 m cell, which costs 8× the propagation and
    /// nothing else — <see cref="LightCellPos"/> already permits it.
    /// </para>
    /// </remarks>
    private static int Conductance(
        PaddedChunk voxels, MaterialTable materials, int cx, int cy, int cz)
    {
        const int span = LightCellPos.VoxelsPerCell;

        Span<int> origin = [cx * span, cy * span, cz * span];
        Span<int> here = stackalloc int[3];
        int bits = 0;

        for (int face = 0; face < VoxelFace.Count; face++)
        {
            int axis = VoxelFace.Axis[face];
            int[] offset = VoxelFace.Offset[face];

            // The two axes in the face's plane, which the 4x4 sweep walks.
            int uAxis = axis == 0 ? 1 : 0;
            int vAxis = axis == 2 ? 1 : 2;

            // This cell's voxel layer touching the face. The layer across it is one further.
            int alongAxis = origin[axis] + (VoxelFace.Direction[face] > 0 ? span - 1 : 0);

            for (int v = 0; v < span && (bits & (1 << face)) == 0; v++)
            {
                for (int u = 0; u < span; u++)
                {
                    here[axis] = alongAxis;
                    here[uAxis] = origin[uAxis] + u;
                    here[vAxis] = origin[vAxis] + v;

                    if (materials.IsOpaque(voxels[here[0], here[1], here[2]])) continue;

                    if (materials.IsOpaque(voxels[
                            here[0] + offset[0], here[1] + offset[1], here[2] + offset[2]]))
                    {
                        continue;
                    }

                    bits |= 1 << face;
                    break;
                }
            }
        }

        return (bits & ConductanceMask) << ConductanceShift;
    }

    /// <summary>The brightest emitter anywhere in the cell.</summary>
    /// <remarks>
    /// Max rather than sum: light does not add up in this model, and a cell packed with lamps
    /// would otherwise overflow the nibble and wrap to darkness.
    /// </remarks>
    private static int Emission(
        PaddedChunk voxels, MaterialTable materials, int cx, int cy, int cz)
    {
        const int span = LightCellPos.VoxelsPerCell;
        int best = 0;

        for (int z = cz * span; z < (cz + 1) * span; z++)
        for (int x = cx * span; x < (cx + 1) * span; x++)
        for (int y = cy * span; y < (cy + 1) * span; y++)
        {
            int emission = materials.LightEmission(voxels[x, y, z]);
            if (emission > best) best = emission;
        }

        return Math.Min(best, MaxLevel);
    }

    /// <summary>A copy of the propagated bytes, for tests that compare whole grids.</summary>
    /// <remarks>
    /// The oracle is an array comparison — no tolerance, no sampling — so it needs the array.
    /// A copy rather than the array itself, because a snapshot that moves under the comparison
    /// is not a snapshot.
    /// </remarks>
    internal byte[] SnapshotLight() => (byte[])_light.Clone();

    private static int Shift(LightChannel channel) =>
        channel == LightChannel.Sky ? SkyShift : BlockShift;
}
