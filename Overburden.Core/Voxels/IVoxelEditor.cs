namespace Overburden.Core.Voxels;

/// <summary>
/// A voxel world that can be written to as well as read.
/// </summary>
/// <remarks>
/// <para>
/// Reading and writing are one interface rather than two because everything that edits the
/// world has to look before it writes — a carve needs the material to weigh against a tool's
/// hardness, and M6's placement will need to know it is replacing something replaceable.
/// Handing those two capabilities over separately means every caller re-pairs them, and the
/// pairing is the whole contract.
/// </para>
/// <para>
/// It exists at all so that <c>Mining.Carve</c> can be tested against a dictionary of voxels
/// instead of a streaming pipeline, three worker threads and a world generator. The real
/// implementation is <c>ChunkPipeline</c>, which is where copy-on-write and dirty marking
/// live; see <see cref="IVoxelWorld"/> for why a failed read is not the same answer as air.
/// </para>
/// </remarks>
public interface IVoxelEditor : IVoxelWorld
{
    /// <summary>
    /// Writes one voxel, and marks whatever that invalidates.
    /// </summary>
    /// <returns>
    /// False when the voxel is in a chunk that does not exist or has not generated yet. The
    /// caller is told rather than the write being dropped silently — a carve reaching past
    /// the loaded region has to be able to report that it was clipped.
    /// </returns>
    bool SetVoxel(VoxelPos voxel, int materialId);
}
