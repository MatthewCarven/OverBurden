using Overburden.Core.Voxels;

namespace Overburden.Core.Machines;

/// <summary>
/// What the carve tells the machine set: a body voxel was taken by a pick and credited.
/// </summary>
/// <remarks>
/// <para>
/// <b>The one fact that separates a swing from a fall</b> (M10a's pass, fact 4 and decision 4).
/// Both write air into a body; the <c>onVoxelChanged</c> hook sees the same thing in both cases.
/// What differs is who credited the inventory: <c>Carve.Take</c> credits per voxel it removes and
/// the fall credits nothing — so the carve says what it took, the hook counts what was lost, and
/// at the drain <c>Lost == Carved</c> means a pick dismantled the machine (the remainder is
/// credited) while <c>Lost &gt; Carved</c> means something else did (it is destroyed). The fall
/// keeps not knowing a machine exists.
/// </para>
/// <para>
/// Called after the credit, only for a voxel whose material the table says is a machine's — one
/// <c>bool[]</c> read per removed voxel for ordinary mining.
/// </para>
/// </remarks>
public interface IMachineBodies
{
    /// <summary>
    /// Whether a machine voxel reads as too hard to the tool asking — asked before the hardness check,
    /// for a machine material only (IDEAL-MACHINES-PLAN decision 17; IM-3's pass, decision 3). A vault
    /// holding anything is, unless its sisters can take everything; at range, whatever the room.
    /// </summary>
    bool Refuses(VoxelPos voxel, int materialId);

    /// <summary>A voxel of a machine material was removed by a pick and credited to the inventory.</summary>
    void Taken(VoxelPos voxel, int materialId);
}
