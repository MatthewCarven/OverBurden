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
/// </remarks>
public interface IFluidTerrain
{
    /// <summary>True when Tier B water may occupy this voxel.</summary>
    bool CanHold(VoxelPos voxel);
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
    private readonly MaterialTable _materials =
        materials ?? throw new ArgumentNullException(nameof(materials));

    public bool CanHold(VoxelPos voxel)
    {
        if (!_world.TryGetMaterial(voxel, out int materialId))
        {
            // Not loaded is not the same answer as empty — VoxelRay.Blocked, one layer out.
            return false;
        }

        return materialId == MaterialIds.AirNumericId || _materials.IsFluid(materialId);
    }
}
