using Overburden.Core.Voxels;

namespace Overburden.Core.Lighting;

/// <summary>Which of the two propagated light fields a value belongs to.</summary>
/// <remarks>
/// <b>Two channels, never one pre-multiplied value.</b> DESIGN's M7 wet-surface term is driven
/// by skylight on its own, and a diurnal cycle is a uniform scaling the sky channel — neither
/// works if the two are combined before they reach the shader. The same split is why the vertex
/// carries two shade bytes; see <c>CornerShade</c>.
/// </remarks>
public enum LightChannel
{
    /// <summary>Daylight, seeded analytically from the surface height.</summary>
    Sky = 0,

    /// <summary>Light from emissive materials.</summary>
    Block = 1,
}

/// <summary>
/// A cell of the light grid, in global cell coordinates. One cell is one metre.
/// </summary>
/// <remarks>
/// <para>
/// <b>1 m is a DESIGN-locked number, settled again on 2026-08-30.</b> Light is low-frequency,
/// and propagating at voxel resolution would be 64× the work for a difference nobody sees. The
/// revisit trigger is M5d playing a <c>hand_pick</c> tunnel and the light reading as blobby
/// across a bore narrower than one cell.
/// </para>
/// <para>
/// <b><see cref="VoxelsPerCell"/> must be a power of two dividing the chunk size</b>, which is
/// the same constraint <c>CaveLatticeStep</c> carries and for the same reason: it makes a chunk
/// boundary always a cell boundary, which is what lets two independently lit chunks agree. The
/// static constructor throws otherwise — this is a correctness constraint, not a tuning knob,
/// and a violation shows up as a seam that is invisible until it is in a screenshot.
/// </para>
/// </remarks>
public readonly record struct LightCellPos(int X, int Y, int Z)
{
    /// <summary>Edge length of a light cell, in metres. DESIGN's number.</summary>
    public const int CellMetres = 1;

    /// <summary>Voxels along one cell edge. 4 at 0.25 m voxels and a 1 m cell.</summary>
    public const int VoxelsPerCell = CellMetres * VoxelConstants.VoxelsPerMetre;

    /// <summary>log2(<see cref="VoxelsPerCell"/>), for floor division by shifting.</summary>
    public const int VoxelsPerCellLog2 = 2;

    /// <summary>Cells along one chunk edge. 8.</summary>
    public const int CellsPerChunk = VoxelConstants.ChunkSize / VoxelsPerCell;

    /// <summary>log2(<see cref="CellsPerChunk"/>).</summary>
    public const int CellsPerChunkLog2 = VoxelConstants.ChunkSizeLog2 - VoxelsPerCellLog2;

    /// <summary>CellsPerChunk − 1, for the positive remainder by masking.</summary>
    public const int CellMask = CellsPerChunk - 1;

    /// <summary>Cells in one chunk. 8³ = 512.</summary>
    public const int Volume = CellsPerChunk * CellsPerChunk * CellsPerChunk;

    static LightCellPos()
    {
        if (VoxelsPerCell <= 0
            || (VoxelsPerCell & (VoxelsPerCell - 1)) != 0
            || VoxelConstants.ChunkSize % VoxelsPerCell != 0
            || 1 << VoxelsPerCellLog2 != VoxelsPerCell
            || 1 << CellsPerChunkLog2 != CellsPerChunk)
        {
            throw new InvalidOperationException(
                $"A light cell is {VoxelsPerCell} voxels, which must be a power of two dividing "
                + $"{VoxelConstants.ChunkSize}, with the log2 constants agreeing. Otherwise a "
                + "chunk boundary stops being a cell boundary and independently lit neighbours "
                + "disagree along it.");
        }
    }

    /// <summary>The chunk this cell belongs to.</summary>
    /// <remarks>Arithmetic shift, so it is already right for negative coordinates.</remarks>
    public ChunkPos Chunk => new(
        X >> CellsPerChunkLog2,
        Y >> CellsPerChunkLog2,
        Z >> CellsPerChunkLog2);

    /// <summary>Coordinates within the containing chunk, always 0..7.</summary>
    public (int X, int Y, int Z) Local => (X & CellMask, Y & CellMask, Z & CellMask);

    public int LocalIndex => Index(X & CellMask, Y & CellMask, Z & CellMask);

    /// <summary>
    /// Flat order within a chunk: Z-major, then X, then <b>Y contiguous</b> — the same order as
    /// <see cref="VoxelPos.Index"/>, and this is the case that most deserves it.
    /// </summary>
    /// <remarks>
    /// Skylight propagates straight down, so the most common propagation in the game walks Y.
    /// The 2026-08-25 lesson applies harder here than to the thing that taught it.
    /// </remarks>
    public static int Index(int localX, int localY, int localZ) =>
        (localZ << (CellsPerChunkLog2 * 2)) | (localX << CellsPerChunkLog2) | localY;

    /// <summary>Inverse of <see cref="Index"/>.</summary>
    public static (int X, int Y, int Z) FromIndex(int index) => (
        (index >> CellsPerChunkLog2) & CellMask,
        index & CellMask,
        index >> (CellsPerChunkLog2 * 2));

    /// <summary>The cell containing a voxel.</summary>
    public static LightCellPos FromVoxel(VoxelPos voxel) => new(
        voxel.X >> VoxelsPerCellLog2,
        voxel.Y >> VoxelsPerCellLog2,
        voxel.Z >> VoxelsPerCellLog2);

    /// <summary>Rebuild a global cell position from a chunk and cell coordinates inside it.</summary>
    public static LightCellPos FromChunkLocal(ChunkPos chunk, int localX, int localY, int localZ) =>
        new((chunk.X << CellsPerChunkLog2) + localX,
            (chunk.Y << CellsPerChunkLog2) + localY,
            (chunk.Z << CellsPerChunkLog2) + localZ);

    /// <summary>The lowest-numbered voxel this cell contains.</summary>
    public VoxelPos MinVoxel => new(
        X << VoxelsPerCellLog2,
        Y << VoxelsPerCellLog2,
        Z << VoxelsPerCellLog2);

    public LightCellPos Offset(int dx, int dy, int dz) => new(X + dx, Y + dy, Z + dz);

    public override string ToString() => $"Cell({X}, {Y}, {Z})";
}
