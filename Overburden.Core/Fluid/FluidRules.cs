namespace Overburden.Core.Fluid;

/// <summary>
/// How much water moves sideways in one tick. One definition, shared by the automaton and by the
/// check that says it has come to rest.
/// </summary>
/// <remarks>
/// <para>
/// <b>Under <see cref="SpreadRule.Fixed"/> a cell's move depends on its own contents and one
/// neighbour's, and on nothing else.</b> That is the property M8a's sleep/wake design rested on,
/// and it is why <see cref="FluidConstants.SpreadDivisor"/> is a constant rather than a count of
/// eligible neighbours — see the remarks there for the bug the counted version caused and how it
/// was found. Because the rule is local to a pair, a cell that <i>gains</i> water can never
/// create a move for anything but itself, so a transfer need only wake the neighbourhood it took
/// water out of. <b>Under <see cref="SpreadRule.Counted"/> that locality is given up on purpose</b>:
/// the divisor is one more than the number of holdable sides holding less than the cell, so a
/// cell's move reads its four sides as well as the pair, and the automaton wakes the neighbourhood
/// of both endpoints of every transfer to cover it (<c>FluidSimulation.Record</c>). Which rule a
/// simulation runs is fixed at its construction; the arithmetic here takes the divisor as an
/// argument and has no opinion.
/// </para>
/// <para>
/// <b>The split is even, which is what dividing by a snapshot count buys.</b> Giving each lower
/// neighbour half the difference in turn drains the source as it goes: a cell with 1,000 mL and
/// four empty neighbours ends at 500, 250, 125, 62 and keeps 63 — a fan pointing at whichever
/// neighbour happened to be examined first. Against a snapshot of the source and a divisor of
/// five it gives 200 to each and keeps 200, which is the answer with no preferred direction —
/// and the counted rule gives exactly that against four dry sides, because its count is four.
/// </para>
/// <para>
/// <b>It also makes the horizontal phase provably terminate, which is this milestone's correctness
/// condition rather than a nicety.</b> Let the source hold <i>a</i>, let its <i>k</i> ≤ 4 lower
/// neighbours hold <i>b<sub>i</sub></i>, let <i>d</i> be the divisor, and let
/// <i>t<sub>i</sub></i> = ⌊(<i>a</i> − <i>b<sub>i</sub></i>)/<i>d</i>⌋. The change in the sum of
/// squares over those cells is <c>T² + Σt² − 2Σt(a − b)</c> where <c>T = Σt</c>. Since
/// <c>(a − b) ≥ d·t</c> by the floor and <c>T² ≤ kΣt²</c> by Cauchy–Schwarz, that is at most
/// <c>(k + 1 − 2d)Σt²</c>. For the fixed rule, <c>k ≤ 4</c> and <c>d = 5</c> make it at most
/// <c>−5Σt²</c>; for the counted rule <c>d = k + 1</c> makes it exactly <c>−(k + 1)Σt²</c> —
/// <b>strictly negative whenever anything moves at all, under either rule</b>. A transfer clamped
/// by the destination's room only makes <i>t</i> smaller, and the floor inequality survives that.
/// The sum of squares is a non-negative integer, so only finitely many sideways moves can happen
/// at a fixed water height. Vertical moves strictly reduce a different quantity — the sum of
/// <i>amount × Y</i>, which is bounded below because an unloaded voxel is solid — so the pair
/// ordered lexicographically strictly decreases on every move the automaton can make. A fall of
/// several cells in one visit (M8d-3's rule, <see cref="FluidSimulation.FallCells"/>) reduces it
/// by several times as much, and is one move, so the proof does not notice the difference.
/// </para>
/// <para>
/// <b>What it costs, stated: two settled neighbours may differ by up to four millilitres — and
/// that is a bound per adjacent pair, not a bound on the surface.</b> A difference below the
/// divisor floors to a transfer of zero, so a chain of cells can each sit four millilitres below
/// the last and stay there: the resting surface is level to within
/// <c>(SpreadDivisor − 1) × cells of separation</c>. Measured on a three-cell join, the far end
/// settled 8 mL low and 12 mL was left standing one level above it, which is the same fact seen
/// from the other side. At 0.026% of a cell per cell that is 6.4 mm across a hundred-cell tunnel,
/// far below anything a renderer or a player could see — but it is <b>not</b> the "within four
/// millilitres" a first reading suggests, and a test asserting a global bound will fail. The
/// counted rule's divisor is never more than five, so the bound holds for it too, and a pair with
/// fewer lower sides rests closer.
/// </para>
/// <para>
/// <b>Driving the residue to one by moving at least a millilitre whenever two cells differ by two
/// or more does not work, and the way it fails is the exact failure this milestone is defined
/// against.</b> With four neighbours two millilitres below it, a cell would give one to each,
/// drop four, and become the low cell — which hands it back next tick, forever. A cell
/// oscillating by one unit passes every volume-conservation assertion there is. The termination
/// proof above is what forbids it: <c>t ≤ (a − b)/d</c> is the inequality it rests on, and a
/// minimum transfer breaks it.
/// </para>
/// <para>
/// <b>Shared on purpose, and the limit of what that proves is worth stating.</b>
/// <c>FluidSimulation.VerifyQuiescent</c> calls this too, so it is not an independent check of the
/// <i>rule</i> — mutating this function moves both halves together and they still agree, which is
/// the trap M6a recorded for writer/reader pairs. What the check is independent of is the
/// <b>sleep and wake bookkeeping</b>, which is the thing it exists to catch, the thing no volume
/// assertion can see, and the thing it caught the first time it ran.
/// </para>
/// </remarks>
public static class FluidRules
{
    /// <summary>
    /// Whether a cell holding this much will spread sideways at all. See
    /// <see cref="FluidConstants.MinimumSpreadMillilitres"/> for why there is a floor.
    /// </summary>
    public static bool CanSpread(int amount) =>
        amount >= FluidConstants.MinimumSpreadMillilitres;

    /// <summary>
    /// What a cell with this many holdable sides holding less than it divides its surplus by.
    /// </summary>
    /// <param name="rule">The simulation's rule.</param>
    /// <param name="lowerSides">Holdable sides holding strictly less than the cell, 0..4.</param>
    /// <remarks>
    /// Never more than <see cref="FluidConstants.SpreadDivisor"/> under either rule, which is
    /// what keeps the per-pair residue bound and the sheet solve's step constant where they were.
    /// A count of zero under the counted rule gives one, and is harmless: no side is lower, so
    /// every transfer is zero whatever the divisor.
    /// </remarks>
    public static int Divisor(SpreadRule rule, int lowerSides)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(lowerSides);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(lowerSides, 4);
        return rule == SpreadRule.Counted ? lowerSides + 1 : FluidConstants.SpreadDivisor;
    }

    /// <summary>
    /// Millilitres that move from a cell to one horizontal neighbour in one tick, under the
    /// fixed rule.
    /// </summary>
    /// <param name="amount">The source's contents at the start of the horizontal phase.</param>
    /// <param name="neighbour">The neighbour's contents.</param>
    /// <remarks>
    /// The pair-local form, and the one the sheet solve's step detector reads under either rule:
    /// five is the largest divisor there is, so a difference that moves at five moves at any.
    /// Under the counted rule that makes the detector conservative — it sees a move only where
    /// one exists under every divisor — which costs it nothing it is for, because the automaton's
    /// own wake rule, not the detector, is what guarantees the rest.
    /// </remarks>
    public static int HorizontalTransfer(int amount, int neighbour) =>
        HorizontalTransfer(amount, neighbour, FluidConstants.SpreadDivisor);

    /// <summary>
    /// Millilitres that move from a cell to one horizontal neighbour in one tick, dividing the
    /// difference by <paramref name="divisor"/>.
    /// </summary>
    public static int HorizontalTransfer(int amount, int neighbour, int divisor)
    {
        if (neighbour >= amount || !CanSpread(amount))
        {
            return 0;
        }

        return (amount - neighbour) / divisor;
    }
}
