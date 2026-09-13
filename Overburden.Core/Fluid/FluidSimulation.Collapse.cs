using Overburden.Core.Voxels;

namespace Overburden.Core.Fluid;

/// <summary>
/// The water's side of a collapse (M9c-1, decisions 4 and 5): <see cref="ICollapseWater"/> over
/// the grid, at a tick boundary, with two counters the identity does not read.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="Lift"/> is <c>Take</c>'s twin and <see cref="Return"/> is <see cref="Add"/>'s</b>,
/// each with its own counter and nothing else — the same shape <see cref="HandOff"/> took beside
/// <see cref="Remove"/>, for the same reason: the water is not gone and did not arrive, it moved
/// from the cells the rubble landed in to the cells the rock left, and <c>lifted == returned</c>
/// is the line a fall prints. <c>held == injected + loaded − removed − displaced − handed −
/// evicted</c> is untouched, because a lift subtracts from <c>held</c> exactly what the return adds
/// back, and a remainder that could not be returned goes to <c>displaced</c> through
/// <see cref="DisplaceLifted"/>, where the identity already counts it.
/// </para>
/// <para>
/// Both refuse mid-tick the way <see cref="Add"/> and <see cref="Remove"/> do: a fall runs in the
/// boundary block, where the grid is the frame's, and a caller that reaches here inside a tick is
/// a caller that has not been taught to wait.
/// </para>
/// </remarks>
public sealed partial class FluidSimulation : ICollapseWater
{
    /// <summary>Millilitres lifted out of cells a fall landed rubble in, over the lifetime.</summary>
    public long LiftedMillilitres { get; private set; }

    /// <summary>Millilitres returned into the cavities falls left, over the lifetime. Equals <see cref="LiftedMillilitres"/> less what went to <see cref="DisplacedMillilitres"/>.</summary>
    public long ReturnedMillilitres { get; private set; }

    /// <inheritdoc />
    public long Lift(VoxelPos cell)
    {
        RefuseMidTick(nameof(Lift));
        long taken = Take(cell, FluidConstants.CellCapacityMillilitres);
        LiftedMillilitres += taken;
        return taken;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <see cref="Add"/>'s path — the live terrain's <c>CanHold</c>, the room, the wake — with the
    /// counter swapped, so a return sees the air the rock left the moment it was written.
    /// </remarks>
    public long Return(VoxelPos cell, long millilitres)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(millilitres);
        RefuseMidTick(nameof(Return));

        if (millilitres == 0 || !_terrain.CanHold(cell))
        {
            return millilitres;
        }

        long offered = Math.Min(millilitres, RoomAt(cell));
        long overflow = millilitres - offered + _grid.Fill(cell, offered);
        long accepted = millilitres - overflow;
        if (accepted > 0)
        {
            ReturnedMillilitres += accepted;
            NoteInflow(cell);
            Wake(cell);
            WakeNeighboursOf(cell);
        }

        return overflow;
    }

    /// <inheritdoc />
    public void DisplaceLifted(long millilitres)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(millilitres);
        DisplacedMillilitres += millilitres;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The terrain's own gate, asked at the chunk's origin: <c>VoxelFluidTerrain.IsLoaded</c> is
    /// <c>TryGetMaterialForFluid</c>, which answers false for a chunk still reading its stored
    /// panel, and a fixture that answers from a predicate is loaded everywhere.
    /// </remarks>
    public bool IsReady(ChunkPos chunk) => _terrain.IsLoaded(chunk.OriginVoxel);
}
