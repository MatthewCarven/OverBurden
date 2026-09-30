using Overburden.Core.Voxels;

namespace Overburden.Core.Fluid;

/// <summary>
/// The siphon's two doors into Tier B (IDEAL-MACHINES-PLAN decision 24; IM-4's pass, decision 3):
/// a take and a pour, each a twin with a counter of its own and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// <b>Never <see cref="Add"/>, <see cref="Remove"/>, <see cref="Pump"/> or <see cref="Emit"/></b>,
/// because every one of those counters already means something the siphon is not: <c>injected</c>
/// is water from nothing, <c>removed</c> the debug relay's take, and <c>pumped</c> and
/// <c>emitted</c> are pinned equal to the machine tick's own counters by tests and a comment in
/// <c>MachineTick</c>. A siphon through any of them would break a line that is exact today. So
/// water the siphon takes leaves the grid as <see cref="SiphonedMillilitres"/> and water it pours
/// arrives as <see cref="PouredMillilitres"/>, and the identity gains <c>+ poured − siphoned</c>:
/// the water went into the store and came out of it, and the store's own line counts both.
/// </para>
/// <para>
/// Both refuse mid-tick, as <see cref="Pump"/> and <see cref="Emit"/> do: the siphon's queue drains
/// in the boundary's window, where the grid is the frame's.
/// </para>
/// </remarks>
public sealed partial class FluidSimulation
{
    /// <summary>Millilitres the siphon took out of cells, over the lifetime. A term of the identity.</summary>
    public long SiphonedMillilitres { get; private set; }

    /// <summary>Millilitres the siphon poured into cells, over the lifetime. A term of the identity.</summary>
    public long PouredMillilitres { get; private set; }

    /// <summary>
    /// Takes water out of a voxel into the siphon's hand. Returns what was taken, which the caller
    /// puts in the store — or the store's line and this identity stop agreeing.
    /// </summary>
    public long Siphon(VoxelPos voxel, long millilitres)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(millilitres);
        RefuseMidTick(nameof(Siphon));
        long taken = Take(voxel, millilitres);
        SiphonedMillilitres += taken;
        return taken;
    }

    /// <summary>
    /// Pours water from the store into a voxel. Returns what would not fit; the caller asked
    /// <see cref="RoomFor"/> first and withdraws only what landed.
    /// </summary>
    /// <remarks><see cref="Emit"/>'s path — the live terrain's <c>CanHold</c>, the room, the wake — with the counter swapped.</remarks>
    public long Pour(VoxelPos voxel, long millilitres)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(millilitres);
        RefuseMidTick(nameof(Pour));

        if (millilitres == 0 || !_terrain.CanHold(voxel))
        {
            return millilitres;
        }

        long offered = Math.Min(millilitres, RoomAt(voxel));
        long overflow = millilitres - offered + _grid.Fill(voxel, offered);
        long accepted = millilitres - overflow;
        if (accepted > 0)
        {
            PouredMillilitres += accepted;
            NoteInflow(voxel, accepted);
            Wake(voxel);
            WakeNeighboursOf(voxel);
        }

        return overflow;
    }

    /// <summary>What a pour into <paramref name="voxel"/> would accept now: zero where the live terrain holds no water.</summary>
    public long RoomFor(VoxelPos voxel)
    {
        RefuseMidTick(nameof(RoomFor));
        return _terrain.CanHold(voxel) ? RoomAt(voxel) : 0;
    }

    /// <summary>Millilitres in a voxel now, asked at the boundary — the siphon's read of what a take would find.</summary>
    public long HeldAt(VoxelPos voxel)
    {
        RefuseMidTick(nameof(HeldAt));
        return _grid.AmountAt(voxel);
    }
}
