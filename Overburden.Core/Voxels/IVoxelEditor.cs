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

    /// <summary>
    /// Whether this voxel holds Tier B water — the fill's refusal (M9d, decision 3): replaceable
    /// means air with no water in it, because a fill that overwrote a wet cell would delete the
    /// water it filled and the identity would report it as <c>displaced</c> rather than restore it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A default rather than a requirement, the way <c>IFluidTerrain.IsLoaded</c> defaults, so the
    /// dictionary editors every carve and brush test runs against are untouched and dry. The real
    /// implementation reads the automaton's <i>published</i> wet set — never the grid, which
    /// refuses while a worker owns it — so the answer is stale by at most one tick, uniformly, and
    /// the fill that slips through is one tick's arrivals, still counted on <c>displaced</c>.
    /// </para>
    /// <para>
    /// A voxel under a Tier A plane is <i>not</i> wet by this question: a lake is a body with no
    /// cells, its volume is a number the terrain cannot reduce, so a fill there is a pier and
    /// deletes nothing. Matthew's to reverse after the play; the refusal is one <c>InLake</c> read.
    /// </para>
    /// </remarks>
    bool HoldsWater(VoxelPos voxel) => false;
}
