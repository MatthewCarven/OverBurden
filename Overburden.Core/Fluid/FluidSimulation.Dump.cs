using Overburden.Core.Voxels;

namespace Overburden.Core.Fluid;

/// <summary>
/// The dump's door into Tier B (IM-5a; IDEAL-MACHINES-PLAN decision 33, as IM-5's pass took it in
/// its decision 4): water a vault owed the world, poured into a cell, with a counter of its own and
/// nothing else.
/// </summary>
/// <remarks>
/// <see cref="Pour"/>'s path with the counter swapped, for the reason the siphon got its own pair:
/// <c>poured</c> is the siphon's, and <c>Siphon.Exact</c> reads every millilitre of it as its own, so
/// a dump through it would light the siphon's error every boundary (IM-5's pass, fact 4). The
/// identity gains <c>+ dumped</c>: the water left a vault, and the store's water line counts it
/// leaving. Refuses mid-tick, as every door does; the dump runs in the boundary's window, after the
/// machine tick.
/// </remarks>
public sealed partial class FluidSimulation
{
    /// <summary>Millilitres the dump poured into cells, over the lifetime. A term of the identity.</summary>
    public long DumpedMillilitres { get; private set; }

    /// <summary>
    /// Pours a vault's owed water into a voxel. Returns what would not fit; the caller asked
    /// <see cref="RoomFor"/> first and takes out of the vault only what landed.
    /// </summary>
    public long Dump(VoxelPos voxel, long millilitres)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(millilitres);
        RefuseMidTick(nameof(Dump));
        long accepted = FillFromOutside(voxel, millilitres);
        DumpedMillilitres += accepted;
        return millilitres - accepted;
    }
}
