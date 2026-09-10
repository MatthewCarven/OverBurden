using Overburden.Core.Hydrology;
using Overburden.Core.Voxels;

namespace Overburden.Core.Fluid;

/// <summary>
/// How saturated the rock at a voxel is: the head of water standing over it, in metres, or zero.
/// M8c-3 — the query M9's support model reads, defined by the milestone that owns the answer.
/// </summary>
/// <remarks>
/// <para>
/// <b>Tier C's whole output, as one function.</b> The water table over a column is the level of
/// whatever lake its sample drains to (<see cref="IWaterTable"/>, decision 1); a voxel below that
/// level is under <c>level − centre</c> of head, and one at or above it is dry. Nothing is stored
/// and nothing is swept: the answer is derived when asked, from Tier A's one number per basin.
/// This is the same arithmetic <see cref="SeepIndex.Coefficient"/> multiplies permeability by,
/// kept here so that M9 reads the quantity M8c defines rather than one it guesses.
/// </para>
/// <para>
/// <b>Unread until M9</b>, and deliberately a number rather than a curve: DESIGN's
/// support-against-saturation is a per-material curve, and that curve is M9's to shape over this
/// input. Recent rain is not in it (decision 2: rain with no lake is a look) — the revisit
/// trigger is M9 wanting saturation on a wet hillside for support, which is a second derived term
/// with its own identity, not a store.
/// </para>
/// </remarks>
public static class Saturation
{
    /// <summary>Metres of water standing over the centre of this voxel by its column's table, or zero.</summary>
    public static double HeadMetresAt(VoxelPos voxel, IWaterTable table, int cellVoxels = HeightField.DefaultCellVoxels)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentOutOfRangeException.ThrowIfLessThan(cellVoxels, 1);

        double? level = table.TableAt(FloorDiv(voxel.X, cellVoxels), FloorDiv(voxel.Z, cellVoxels));
        if (level is null)
        {
            return 0;
        }

        double headVoxels = level.Value - (voxel.Y + 0.5);
        return headVoxels > 0 ? headVoxels * VoxelConstants.VoxelSize : 0;
    }

    /// <summary>True when the voxel's centre lies below its column's table.</summary>
    public static bool IsSaturated(VoxelPos voxel, IWaterTable table, int cellVoxels = HeightField.DefaultCellVoxels) =>
        HeadMetresAt(voxel, table, cellVoxels) > 0;

    private static int FloorDiv(int value, int divisor)
    {
        int quotient = value / divisor;
        return value % divisor != 0 && (value < 0) != (divisor < 0) ? quotient - 1 : quotient;
    }
}
