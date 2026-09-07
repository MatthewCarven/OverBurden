namespace Overburden.Core.Fluid;

/// <summary>
/// What a cell divides its sideways surplus by. One value per <see cref="FluidSimulation"/>,
/// fixed at construction.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two rules, because the play asked for the one M8a rejected and the rejection was about the
/// wake, not the physics.</b> <see cref="Fixed"/> divides by five whatever the neighbourhood —
/// one part for the cell and one for each of its four sides, dry or not — so a cell's move depends
/// on the pair and nothing else, and a transfer wakes only the neighbourhood it took water out of.
/// Its cost is convergence: at a straight front the edge cell gives a fifth of its surplus to the
/// one dry cell ahead, so the skirt beyond a full cell is 3,125 → 625 mL and stops at the litre
/// floor two cells out, and two cells level by three fifths a tick, sixteen ticks a pair.
/// Matthew's <i>"the amount of water that spreads into empty cells is a little low"</i>
/// (2026-09-07, 172 relay stabs at 2 Hz) is that cost, met.
/// </para>
/// <para>
/// <b><see cref="Counted"/> divides by one part for the cell and one for each side that is
/// actually lower</b> — <i>k + 1</i> — so a straight front gives half, and against four dry
/// sides it is the fixed rule exactly. M8a rejected it because a neighbour <i>gaining</i> water
/// can shrink a nearby cell's divisor and hand it a move with nothing awake to take it;
/// <c>VerifyQuiescent</c> caught that on its first run. The fix M8a named and did not price is
/// the one built here: a transfer wakes the destination's four sides as well as the source's
/// neighbourhood, so every cell whose inputs changed is queued, by construction (see
/// <c>FluidSimulation.Record</c>). The
/// termination proof in <see cref="FluidRules"/> holds with <i>d = k + 1</i>, and the resting
/// residue per pair is still under the largest divisor, which is still five.
/// </para>
/// <para>
/// <b>A setting rather than a replacement, because the claim for it is a number</b>: that far
/// fewer ticks to rest outweighs the extra wakes per move. <c>--spread</c> on the bench runs
/// <c>--fluid</c>, <c>--spill</c>, <c>--lake</c> and <c>--front</c> under either or both, and
/// <c>--spread=counted</c> in the game is the play's arm. The default is the rule every recorded
/// figure was taken under until the bench and the play say otherwise.
/// </para>
/// </remarks>
public enum SpreadRule
{
    /// <summary>Divide by five, always. M8a's rule and the default.</summary>
    Fixed,

    /// <summary>Divide by one more than the number of holdable sides holding less than the cell.</summary>
    Counted,
}
