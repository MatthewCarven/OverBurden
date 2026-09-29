using Overburden.Core.Machines;
using Overburden.Core.Voxels;

namespace Overburden.Core.Editing;

/// <summary>
/// Told about every change an edit makes, so it can be put back — <see cref="EditHistory"/>'s
/// side of the undo.
/// </summary>
/// <remarks>
/// <para>
/// <b>It hangs off the two owners of state, not off the verbs.</b> The plan's first shape was a
/// fourth collaborator on <c>Carve.Take</c> and <c>Place.Put</c> beside the inventory and the
/// machine bodies, threaded through seventeen public entry points; the build found the cheaper
/// one — <see cref="Streaming.ChunkPipeline.SetVoxel"/> is the single write and already reads the
/// previous material for its own early-out, and <see cref="MachineSet"/> owns every row change.
/// Two hooks, no signature changes, and a shape nobody can forget to pass.
/// </para>
/// <para>
/// <b>Everything else repairs itself.</b> Light, the sky columns, the support field, the fluid
/// surface, the mesh and the collider are derived from the voxels through the same
/// <c>SetVoxel</c>, so an undo that writes prior materials back through it is indistinguishable
/// from a player writing them by hand. That is why the undo is a small feature.
/// </para>
/// </remarks>
public interface IEditJournal
{
    /// <summary>A voxel changed from <paramref name="was"/> to <paramref name="now"/>.</summary>
    void Wrote(VoxelPos voxel, int was, int now);

    /// <summary>A machine row was added to, or removed from, the set.</summary>
    void MachineChanged(MachineRow row, bool added);

    /// <summary>
    /// Whether changes are kept. Switched off around the world's own writes (a fall, the machine
    /// drain's reconcile and destroy, an undo's inverse), which are never the player's edit.
    /// </summary>
    bool Recording { get; set; }
}
