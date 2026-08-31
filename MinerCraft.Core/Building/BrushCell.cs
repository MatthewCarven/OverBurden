using System.Numerics;
using MinerCraft.Core.Voxels;

namespace MinerCraft.Core.Building;

/// <summary>
/// One cell of the world's 1 m building lattice — the unit every brush is placed on.
/// </summary>
/// <remarks>
/// <para>
/// <b>Snapping to a lattice is what makes the brush <em>set</em> coherent, not just the cube.</b>
/// Brushes tile: a wall built of cubes has no seams and no one-voxel gaps, and the slab and the
/// ramp only mean anything on a lattice at all, since a ramp that is not grid-aligned cannot be
/// tiled into a staircase. DESIGN's <i>"fine detail comes from carving, not from placing"</i> is
/// the argument — the brushes are the bulk tool, and bulk that tiles is the point.
/// </para>
/// <para>
/// <b>It lives in Core</b> on M3d's grounds, as the placement counterpart to
/// <c>Carve.SwingCentre</c>: where a brush lands is a game rule, and a multiplayer server would
/// need to agree about it rather than trusting a client's arithmetic.
/// </para>
/// <para>
/// <b>Deliberately not <c>LightCellPos</c>, which is also 1 m and shares this origin.</b> Reusing
/// it would work today and cost nothing. It is a trap: the 1 m light cell carries a live revisit
/// trigger from M5 — a <c>hand_pick</c> tunnel reading blobby — and halving it would silently
/// halve the brush lattice, moving where every brush in the game lands, from inside a change that
/// has nothing to do with building. Two things that are the same size for unrelated reasons want
/// two constants.
/// </para>
/// <para>
/// <b>Revisit trigger:</b> M6d plays it and lattice-snapping reads as unresponsive — that is,
/// the brush going where the lattice says rather than where the crosshair sat is felt as the
/// game ignoring the aim rather than as the wall lining up.
/// </para>
/// </remarks>
public readonly record struct BrushCell(int X, int Y, int Z)
{
    /// <summary>Voxels along one edge of a cell. One metre of them.</summary>
    /// <remarks>
    /// Derived from <see cref="VoxelConstants.VoxelsPerMetre"/> rather than written as 4,
    /// because the cell is <em>a metre</em> — if the voxel size ever changed, a cell would still
    /// be a metre and this would still be right. Pinned against <see cref="VoxelsLog2"/> by a
    /// test, since the snap is a shift and a shift needs the two to agree.
    /// </remarks>
    public const int Voxels = VoxelConstants.VoxelsPerMetre;

    /// <summary>log2(<see cref="Voxels"/>), so the snap can be a shift rather than a division.</summary>
    /// <remarks>
    /// A right shift, never <c>/ Voxels</c>: truncating division puts voxel −1 in cell 0 beside
    /// voxel 0, which is <see cref="VoxelPos"/>'s recorded seam-through-the-origin bug wearing a
    /// different hat. Half the world has negative coordinates.
    /// </remarks>
    internal const int VoxelsLog2 = 2;

    /// <summary>A cell's edge length in metres. One, by construction.</summary>
    public const float SizeMetres = Voxels * VoxelConstants.VoxelSize;

    /// <summary>The cell containing <paramref name="voxel"/>.</summary>
    public static BrushCell FromVoxel(VoxelPos voxel) => new(
        voxel.X >> VoxelsLog2,
        voxel.Y >> VoxelsLog2,
        voxel.Z >> VoxelsLog2);

    /// <summary>This cell's minimum-corner voxel.</summary>
    public VoxelPos OriginVoxel => new(X << VoxelsLog2, Y << VoxelsLog2, Z << VoxelsLog2);

    /// <summary>This cell's centre, in world metres.</summary>
    public Vector3 CentreMetres => new(
        (X + 0.5f) * SizeMetres,
        (Y + 0.5f) * SizeMetres,
        (Z + 0.5f) * SizeMetres);

    /// <summary>
    /// The voxel at <paramref name="localX"/>, <paramref name="localY"/>,
    /// <paramref name="localZ"/> within this cell, each 0..<see cref="Voxels"/>−1.
    /// </summary>
    public VoxelPos VoxelAt(int localX, int localY, int localZ) => new(
        (X << VoxelsLog2) + localX,
        (Y << VoxelsLog2) + localY,
        (Z << VoxelsLog2) + localZ);

    /// <summary>The cell <paramref name="dx"/>, <paramref name="dy"/>, <paramref name="dz"/> away.</summary>
    public BrushCell Offset(int dx, int dy, int dz) => new(X + dx, Y + dy, Z + dz);

    public override string ToString() => $"Cell({X}, {Y}, {Z})";
}

/// <summary>Which way a ramp rises.</summary>
/// <remarks>
/// <para>
/// <b>The direction of the <em>rise</em>, not of the descent</b> — a ramp facing
/// <see cref="PositiveX"/> is one voxel tall at its −X edge and four at its +X edge, so walking
/// toward +X climbs it. Stating which end the name refers to is worth a sentence, because both
/// readings are natural and the wrong one builds every staircase backwards.
/// </para>
/// <para>
/// Four values rather than six: a ramp rising along Y is a wall, and a ramp is a thing you walk
/// up. This is the first brush parameter that is not a scalar, and the first thing in this game
/// with an orientation at all — which is why the brushes are methods rather than an
/// <c>IShape</c> the caller supplies, following <c>Carve</c>'s recorded argument.
/// </para>
/// </remarks>
public enum BrushFacing
{
    PositiveX,
    NegativeX,
    PositiveZ,
    NegativeZ,
}
