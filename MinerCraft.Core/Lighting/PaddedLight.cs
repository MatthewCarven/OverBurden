using MinerCraft.Core.Voxels;

namespace MinerCraft.Core.Lighting;

/// <summary>
/// One chunk's light cells plus a border of the neighbours', in the shape a mesh job samples.
/// </summary>
/// <remarks>
/// <para>
/// <b>Gathered on the sim thread at submission, unlike <see cref="PaddedChunk"/>, which the
/// worker builds for itself.</b> Holding 27 <c>VoxelChunk</c> references is safe because M3a
/// made voxels copy-on-write; light has no such rule and cannot cheaply have one, since the
/// propagator writes into those 512-byte arrays in place and goes on doing it while a mesh job
/// runs. One copy of this per job against thousands of clones per drain. See docs/M5-PLAN.md.
/// </para>
/// <para>
/// <b>11³ covering −1…9, not the 10³ over −1…8 the plan derived.</b> That derivation is right
/// for a sample taken <em>at</em> the vertex: a vertex at the chunk's far corner sits at cell
/// coordinate 8.0, the interior centres run 0.5…7.5, so it needs cells 7 and 8. But the sample
/// is taken half a cell along the face normal — which is not a refinement, it is what stops a
/// lit floor rendering at half brightness, because a vertex sitting exactly on the boundary
/// between bright air and dark rock otherwise interpolates equally between them. That offset
/// pushes the far face's sample to 8.5, which reaches cell 9.
/// </para>
/// <para>
/// <b>Cell 9's weight is always exactly zero, and it is stored anyway.</b> The offset sample
/// along the face axis is <c>plane / 4 ± 0.5</c> with <c>plane</c> an integer, so at the one
/// place cell 9 is reached — <c>plane == 32</c> — the interpolation weight is 8.0 − 8 = 0,
/// exactly, in binary. Clamping the index instead would be correct today and silently wrong the
/// first time the cell size or the offset changes, for 331 bytes.
/// </para>
/// </remarks>
public sealed class PaddedLight
{
    /// <summary>Lowest cell coordinate held, relative to the chunk.</summary>
    public const int Low = -1;

    /// <summary>Highest cell coordinate held, relative to the chunk.</summary>
    public const int High = LightCellPos.CellsPerChunk + 1;

    /// <summary>11 — the chunk's eight cells plus one below and two above.</summary>
    public const int Size = High - Low + 1;

    public const int Volume = Size * Size * Size;

    /// <summary>Half a cell, in cell units. The offset along the face normal.</summary>
    /// <remarks>
    /// <b>Not cosmetic.</b> A face sits on the boundary between a solid voxel and an open one,
    /// so a vertex on it interpolates half from the lit cell and half from the dark one: a flat,
    /// fully lit floor would render at 7.5 of 15. Offsetting outward puts the sample at the
    /// centre plane of the cell in front of the face, which is the continuous version of every
    /// voxel game's "shade a face by the light of the block in front of it".
    /// </remarks>
    public const float NormalOffsetCells = 0.5f;

    /// <summary>Low nibble skylight, high nibble block light — <see cref="ChunkLight"/>'s byte.</summary>
    private readonly byte[] _cells = new byte[Volume];

    private static int Index(int x, int y, int z) =>
        (((z - Low) * Size) + (x - Low)) * Size + (y - Low);

    /// <summary>One cell's packed value, in chunk-relative cell coordinates.</summary>
    public byte this[int x, int y, int z] => _cells[Index(x, y, z)];

    /// <summary>
    /// Fills every cell from the centre chunk and its 26 neighbours' light grids.
    /// </summary>
    /// <remarks>
    /// A missing grid contributes zero, which is darkness — the same direction
    /// <see cref="PaddedChunk"/> takes with a missing chunk, and unreachable for the same
    /// reason: a chunk whose neighbourhood is incomplete fails the barrier and never meshes.
    /// </remarks>
    public void CopyFrom(ReadOnlySpan<ChunkLight?> neighbourhood)
    {
        if (neighbourhood.Length != PaddedChunk.NeighbourhoodSize)
        {
            throw new ArgumentException(
                $"Expected {PaddedChunk.NeighbourhoodSize} grids, got {neighbourhood.Length}.",
                nameof(neighbourhood));
        }

        for (int z = Low; z <= High; z++)
        {
            int dz = z >> LightCellPos.CellsPerChunkLog2;

            for (int x = Low; x <= High; x++)
            {
                int dx = x >> LightCellPos.CellsPerChunkLog2;

                for (int y = Low; y <= High; y++)
                {
                    int dy = y >> LightCellPos.CellsPerChunkLog2;

                    ChunkLight? source = neighbourhood[PaddedChunk.NeighbourIndex(dx, dy, dz)];

                    _cells[Index(x, y, z)] = source is null
                        ? (byte)0
                        : source.Packed(LightCellPos.Index(
                            x & LightCellPos.CellMask,
                            y & LightCellPos.CellMask,
                            z & LightCellPos.CellMask));
                }
            }
        }
    }

    /// <summary>Sets one cell directly. For tests, which state a light field rather than flood one.</summary>
    internal void Set(int x, int y, int z, int sky, int block) =>
        _cells[Index(x, y, z)] = (byte)((sky & 0xF) | ((block & 0xF) << 4));

    /// <summary>Fills every cell with one value.</summary>
    public void Fill(int sky, int block) =>
        Array.Fill(_cells, (byte)((sky & 0xF) | ((block & 0xF) << 4)));

    /// <summary>
    /// Trilinearly samples both channels at a point in chunk-relative cell coordinates,
    /// returning each as a fraction of full brightness.
    /// </summary>
    /// <remarks>
    /// <b>Cell centres, not cell corners.</b> Cell <c>i</c> occupies <c>[i, i+1)</c> and its
    /// value is the whole cell's, so the value is placed at <c>i + 0.5</c> and the interpolation
    /// runs between centres. Sampling as though the value sat at <c>i</c> shifts the whole light
    /// field half a metre and shows up as light leaking round the wrong side of a corner.
    /// </remarks>
    public void Sample(float x, float y, float z, out float sky, out float block)
    {
        float tx = x - 0.5f;
        float ty = y - 0.5f;
        float tz = z - 0.5f;

        int x0 = (int)MathF.Floor(tx);
        int y0 = (int)MathF.Floor(ty);
        int z0 = (int)MathF.Floor(tz);

        float fx = tx - x0;
        float fy = ty - y0;
        float fz = tz - z0;

        float skySum = 0f;
        float blockSum = 0f;

        for (int dz = 0; dz <= 1; dz++)
        {
            float wz = dz == 0 ? 1f - fz : fz;
            if (wz == 0f) continue;

            for (int dx = 0; dx <= 1; dx++)
            {
                float wx = dx == 0 ? 1f - fx : fx;
                if (wx == 0f) continue;

                for (int dy = 0; dy <= 1; dy++)
                {
                    float wy = dy == 0 ? 1f - fy : fy;
                    if (wy == 0f) continue;

                    byte packed = _cells[Index(x0 + dx, y0 + dy, z0 + dz)];
                    float weight = wx * wy * wz;

                    skySum += weight * (packed & 0xF);
                    blockSum += weight * ((packed >> 4) & 0xF);
                }
            }
        }

        sky = skySum / ChunkLight.MaxLevel;
        block = blockSum / ChunkLight.MaxLevel;
    }
}
