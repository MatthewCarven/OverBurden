using Overburden.Core.Content;
using Overburden.Core.Voxels;

namespace Overburden.Core.Fluid;

/// <summary>
/// The one question the cellular automaton asks the world: can water be in this voxel?
/// </summary>
/// <remarks>
/// <para>
/// One method rather than a voxel world, for the reason <c>ISurfaceHeights</c> is one method: it
/// makes the CA testable against a lambda instead of a streaming pipeline, and it keeps the
/// automaton from acquiring an opinion about materials. <see cref="VoxelFluidTerrain"/> is the
/// adapter onto the real world.
/// </para>
/// <para>
/// <b>An unloaded voxel is solid, and that is the same decision M4a made for the collider.</b>
/// Water must not flow into a chunk that does not exist, because the chunk arriving later would
/// find water already inside its rock. The safe failure is that a flood stops at the edge of the
/// loaded world and resumes when the world catches up — under-filling rather than leaking, which
/// is the direction M7 chose for its window edge too.
/// </para>
/// <para>
/// <b>But solid is the right answer for <i>flow</i> and the wrong one for water that is already
/// there</b> — M8b-4's finding, read off a play log: 2.9 ML of 7.2 ML drawn in one session was
/// "displaced", every jump of it on a window move, because an awake cell whose chunk had left the
/// box was visited, found itself unable to hold, and was squeezed into neighbours that were
/// unloaded too. For water in an unloaded voxel the honest answer is neither solid nor air but
/// <i>unknown</i>, and the response to unknown is to stop. <see cref="IsLoaded"/> is that second
/// question, asked only on the branch where a visited cell cannot hold — so the tick's hot path
/// pays nothing for it — and answered "not loaded" it pauses the cell in place rather than
/// displacing it. A chunk arriving wakes what it paused; see
/// <see cref="FluidSimulation.ChunkArrived"/>.
/// </para>
/// </remarks>
public interface IFluidTerrain
{
    /// <summary>True when Tier B water may occupy this voxel.</summary>
    bool CanHold(VoxelPos voxel);

    /// <summary>
    /// True when the world can answer for this voxel at all. False is <i>unknown</i>, not solid:
    /// water already there is paused, never displaced.
    /// </summary>
    /// <remarks>
    /// <b>Defaults to true, because a terrain with no notion of loading is fully loaded</b> — which
    /// is exactly what every fixture and bench that answers <see cref="CanHold"/> from a predicate
    /// meant before this method existed, and what keeps their recorded figures reproducible. Only
    /// a terrain that can lose a chunk overrides it. Asked only where <see cref="CanHold"/> has
    /// already said no, and only by <c>Visit</c>, <c>VerifyQuiescent</c> and the arrival wake.
    /// </remarks>
    bool IsLoaded(VoxelPos voxel) => true;
}

/// <summary>
/// <see cref="IFluidTerrain"/> over a real voxel world.
/// </summary>
/// <remarks>
/// <para>
/// <b>Holdable means air or an existing fluid material, and deliberately not "not opaque".</b>
/// Glass is transparent and solid; opacity is a lighting property and reusing it here would be
/// the kind of coincidental agreement that survives until the first mod ships a window.
/// </para>
/// <para>
/// <b>Revisit trigger: the first material that is neither solid nor fluid</b> — a ladder, a grass
/// tuft, a rope. Today none exists, so testing for air and fluid is exact; the day one does, this
/// wants a real <c>IsSolid</c> on <see cref="MaterialDefinition"/> rather than a second special
/// case here. M8c adds <c>permeability</c> to the same file for seepage, which is the natural
/// moment to look at it.
/// </para>
/// </remarks>
public sealed class VoxelFluidTerrain(IVoxelWorld world, MaterialTable materials) : IFluidTerrain
{
    private readonly IVoxelWorld _world = world ?? throw new ArgumentNullException(nameof(world));

    // The gate (M8b-4c): a world that knows which chunks are still reading their stored water
    // answers "not loaded" for them here and nowhere else. Bound once, so the hot path is one
    // predictable branch and the same dictionary lookup TryGetMaterial already pays.
    private readonly IFluidWorld? _fluidWorld = world as IFluidWorld;

    private readonly MaterialTable _materials =
        materials ?? throw new ArgumentNullException(nameof(materials));

    public bool CanHold(VoxelPos voxel)
    {
        if (!Loaded(voxel, out int materialId))
        {
            // Not loaded is not the same answer as empty — VoxelRay.Blocked, one layer out.
            return false;
        }

        return materialId == MaterialIds.AirNumericId || _materials.IsFluid(materialId);
    }

    /// <summary>
    /// The streamer's own answer: a chunk that is tracked and has voxels — and, since M8b-4c, whose
    /// stored water is home. A chunk still generating answers false here as it does in
    /// <see cref="CanHold"/>, and for the same reason: its water is paused until the voxels exist,
    /// and the arrival wakes it. A chunk whose panel is still being read answers false for the same
    /// reason again, one step later — water flowing into it before its row lands would meet the
    /// restore, and <see cref="FluidGrid.Restore"/> refuses a chunk that holds water.
    /// </summary>
    public bool IsLoaded(VoxelPos voxel) => Loaded(voxel, out _);

    private bool Loaded(VoxelPos voxel, out int materialId) =>
        _fluidWorld is not null
            ? _fluidWorld.TryGetMaterialForFluid(voxel, out materialId)
            : _world.TryGetMaterial(voxel, out materialId);
}

/// <summary>
/// A voxel world that knows which of its chunks are loaded <em>to the water</em>.
/// </summary>
/// <remarks>
/// <b>M8b-4c's gate, on the world rather than on the automaton.</b> A chunk arrives before its
/// stored water in all three of the pipeline's arrival paths — revived on the sim thread, landed
/// whole from a load, generated — so between the voxels and the row a neighbour's water could
/// flow into its air, and the restore would find water where it expects none. The automaton
/// already has one notion of a chunk it cannot answer for (M8b-4a's pause), so rather than a
/// second set inside the simulation the streamer answers this question the way it answers
/// <see cref="IVoxelWorld.TryGetMaterial"/>, with one more flag read on the record it has already
/// found. Light, mesh and physics never see the flag; only the water does.
/// </remarks>
public interface IFluidWorld : IVoxelWorld
{
    /// <summary>
    /// <see cref="IVoxelWorld.TryGetMaterial"/>, except that a chunk whose stored fluid panel has
    /// not yet been read answers false: not loaded, to the water.
    /// </summary>
    bool TryGetMaterialForFluid(VoxelPos voxel, out int materialId);
}
