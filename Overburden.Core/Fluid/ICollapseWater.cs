using Overburden.Core.Voxels;

namespace Overburden.Core.Fluid;

/// <summary>
/// What a collapse asks of the water (M9c-1, decisions 4 and 5): lift every litre out of a cell
/// the rubble is about to land in, return it into the cavity the rock left, and own up to any
/// remainder. Three calls, at a tick boundary, with the grid the caller's.
/// </summary>
/// <remarks>
/// <para>
/// <b>The two terms are Tier B's alone and the identity does not change.</b> A lift and a return
/// are one representation to the same representation, so neither is <c>added</c> nor
/// <c>removed</c>; they are printed together and asserted equal, and whatever a return could not
/// place — none, by the arithmetic in the plan: a run of <i>n</i> lands in <i>n</i> cells and
/// vacates <i>n</i> — goes to <c>displaced</c>, the one counter that already means <i>water the
/// world took a cell from</i>, and is never dropped.
/// </para>
/// <para>
/// An interface rather than the simulation itself so the fall in <c>Overburden.Core.Structure</c>
/// is testable against a ledger and runs with no water at all — <c>--nowater</c> has no grid and
/// no wait.
/// </para>
/// </remarks>
public interface ICollapseWater
{
    /// <summary>Drains everything the cell holds, returning the millilitres. Zero for a dry cell.</summary>
    long Lift(VoxelPos cell);

    /// <summary>Puts lifted water back into a cell, returning what would not fit.</summary>
    long Return(VoxelPos cell, long millilitres);

    /// <summary>Lifted water that found no cell to return to: counted as displaced, never dropped.</summary>
    void DisplaceLifted(long millilitres);

    /// <summary>Whether this cell's chunk can hold water now — loaded, and its stored water home.</summary>
    /// <remarks>
    /// Decision 5's wait: a return into a chunk whose panel is still on the lane would be refused,
    /// and rubble written over its cells would displace their water at the first visit. Asked of
    /// every chunk a fall touches before anything is written, so <c>displaced 0</c> stays an oracle.
    /// </remarks>
    bool IsReady(ChunkPos chunk);
}
