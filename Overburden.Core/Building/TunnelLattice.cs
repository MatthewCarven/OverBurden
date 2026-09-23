using Overburden.Core.Voxels;

namespace Overburden.Core.Building;

/// <summary>
/// The quantum tunneller's lattice: 2 m cubes, eight voxels a side — two brush cells, a quarter
/// of a chunk (Matthew, 2026-09-23: <i>"a quantum tunneller that does about 2x2 meters to go
/// between the quantum miner, and quantum excavator"</i>, a 2 m cube on his word).
/// </summary>
/// <remarks>
/// <para>
/// <b>One definition for both verbs</b>, here rather than in <c>Carve</c>, because the fill
/// (<c>Place.Tunnel</c>)
/// and the bite (<c>Carve.Tunnel</c>) must name the same cubes — two constants that agree the
/// day they are written is the <c>IsCaveAt</c>/<c>GenerateTerrain</c> argument, and
/// <c>Carve.CubeVoxels = BrushCell.Voxels</c> is the same rule one size down.
/// </para>
/// <para>
/// <b>It nests both ways</b>: a multiple of the brush cell, so eight cube bites are one tunnel
/// bite and a 1 m brush lands inside a 2 m hole exactly; and a divisor of the chunk, so a tunnel
/// cube never straddles a chunk face and 64 of them are one chunk. The snap is a mask, never a
/// division, so voxel −1 is in the cube at −8 and no seam runs through the origin.
/// </para>
/// </remarks>
public static class TunnelLattice
{
    /// <summary>The edge in voxels: two brush cells.</summary>
    public const int Voxels = 2 * BrushCell.Voxels;

    /// <summary>The edge in metres.</summary>
    public const float SizeMetres = Voxels * VoxelConstants.VoxelSize;

    /// <summary>The origin of the 2 m cube holding <paramref name="voxel"/> — the lattice floor on both sides of zero.</summary>
    public static VoxelPos OriginOf(VoxelPos voxel) =>
        new(voxel.X & ~(Voxels - 1), voxel.Y & ~(Voxels - 1), voxel.Z & ~(Voxels - 1));
}
