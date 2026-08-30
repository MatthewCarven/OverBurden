using MinerCraft.Core.Content;
using MinerCraft.Core.Voxels;

namespace MinerCraft.Core.Meshing;

/// <summary>
/// Per-vertex ambient occlusion: how much of the space around a face corner is walled in.
/// </summary>
/// <remarks>
/// <para>
/// The standard rule, and it is standard because it is right. For one corner of one face,
/// look at the three voxels diagonally adjacent to it <em>on the outside of the face</em> —
/// the two edge-sharing <c>side1</c> and <c>side2</c>, and the <c>corner</c> between them.
/// </para>
/// <para>
/// <b>The <c>side1 &amp;&amp; side2</c> case is the whole point.</b> Two walls meeting at a
/// corner enclose it completely and whatever sits in the corner behind them is irrelevant, so
/// the vertex goes fully dark rather than two-thirds dark. That is the difference between an
/// inside corner that reads as a corner and one that reads as a smudge.
/// </para>
/// <para>
/// <b>Only opaque materials occlude.</b> Water darkening a corner is an M8 question and
/// <see cref="MaterialTable.IsOpaque"/> is already the right shape to answer it then.
/// </para>
/// <para>
/// <b>Every sample lands inside the padded chunk, and that is not luck.</b> The three
/// diagonals sit on the far side of the face — so their coordinate along the face's own axis
/// is the neighbour's, −1..32 — and move ±1 from an interior 0..31 on the two in-plane axes,
/// which is also −1..32. <see cref="PaddedChunk.CopyFrom"/> gathers all 26 neighbours rather
/// than the 6 face visibility needs precisely so the edges and corners of that border exist;
/// M1c's comment says so in as many words, and this is the milestone that cashes it in.
/// </para>
/// </remarks>
public static class AmbientOcclusion
{
    /// <summary>Fully open. Levels run 0 (walled in) to 3.</summary>
    public const int MaxLevel = 3;

    /// <summary>
    /// Brightness per AO level. <b>A feel constant, and M5d is what judges it.</b>
    /// </summary>
    /// <remarks>
    /// A linear ramp bottoming at 0.4 — 2/5, 3/5, 4/5, 5/5 — chosen for being obvious rather
    /// than measured. The one property that is <em>not</em> a matter of taste: the four values
    /// must stay distinct, because they are the merge key. Collapse two levels onto one byte
    /// and faces with visibly different occlusion start merging.
    /// </remarks>
    private static readonly byte[] LevelShade = [102, 153, 204, 255];

    /// <summary>Brightness for an occlusion level, 0..3.</summary>
    public static byte ShadeFor(int level) => LevelShade[level];

    /// <summary>
    /// The rule itself: <c>3 − occlusion</c>, where two sides meeting means full occlusion.
    /// </summary>
    public static int Level(bool side1, bool side2, bool corner) =>
        side1 && side2
            ? 0
            : MaxLevel - ((side1 ? 1 : 0) + (side2 ? 1 : 0) + (corner ? 1 : 0));

    /// <summary>Corner order 0,1,2,3 as signs along the face's U axis.</summary>
    private static readonly int[] SignU = [-1, 1, 1, -1];

    /// <summary>...and along its V axis. Together: p, p+du, p+du+dv, p+dv.</summary>
    private static readonly int[] SignV = [-1, -1, 1, 1];

    /// <summary>
    /// Fills <paramref name="levels"/> with the four corner AO levels of one voxel face.
    /// </summary>
    public static void LevelsFor(
        PaddedChunk voxels,
        MaterialTable materials,
        int face,
        int x,
        int y,
        int z,
        Span<int> levels)
    {
        int uAxis = VoxelFace.UAxis[face];
        int vAxis = VoxelFace.VAxis[face];
        int[] offset = VoxelFace.Offset[face];

        // The voxel in front of the face. Every sample is this one, displaced in the plane.
        Span<int> front = [x + offset[0], y + offset[1], z + offset[2]];

        for (int corner = 0; corner < CornerShade.Corners; corner++)
        {
            int su = SignU[corner];
            int sv = SignV[corner];

            bool side1 = IsOpaque(voxels, materials, front, uAxis, su, vAxis, 0);
            bool side2 = IsOpaque(voxels, materials, front, uAxis, 0, vAxis, sv);
            bool diagonal = IsOpaque(voxels, materials, front, uAxis, su, vAxis, sv);

            levels[corner] = Level(side1, side2, diagonal);
        }
    }

    /// <summary>The four corner shades of one voxel face, sky channel only.</summary>
    /// <remarks>
    /// Block light is zero until something can emit it, which is M5b. The channel is written
    /// anyway so the vertex format and the merge key are final now rather than at M5c.
    /// </remarks>
    public static CornerShade ShadeFor(
        PaddedChunk voxels, MaterialTable materials, int face, int x, int y, int z)
    {
        Span<int> levels = stackalloc int[CornerShade.Corners];
        LevelsFor(voxels, materials, face, x, y, z, levels);

        Span<byte> sky = stackalloc byte[CornerShade.Corners];
        Span<byte> block = stackalloc byte[CornerShade.Corners];

        for (int corner = 0; corner < CornerShade.Corners; corner++)
        {
            sky[corner] = ShadeFor(levels[corner]);
            block[corner] = 0;
        }

        return CornerShade.FromCorners(sky, block);
    }

    private static bool IsOpaque(
        PaddedChunk voxels,
        MaterialTable materials,
        ReadOnlySpan<int> front,
        int uAxis,
        int du,
        int vAxis,
        int dv)
    {
        Span<int> sample = [front[0], front[1], front[2]];
        sample[uAxis] += du;
        sample[vAxis] += dv;

        return materials.IsOpaque(voxels[sample[0], sample[1], sample[2]]);
    }
}
