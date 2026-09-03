namespace Overburden.Core.Fluid;

/// <summary>
/// How much water moves sideways in one tick. One definition, shared by the automaton and by the
/// check that says it has come to rest.
/// </summary>
/// <remarks>
/// <para>
/// <b>A cell's move depends on its own contents and one neighbour's, and on nothing else.</b> That
/// is the property the whole sleep/wake design rests on, and it is why
/// <see cref="FluidConstants.SpreadDivisor"/> is a constant rather than a count of eligible
/// neighbours — see the remarks there for the bug the counted version caused and how it was found.
/// Because the rule is local to a pair, a cell that <i>gains</i> water can never create a move for
/// anything but itself, so a transfer need only wake the neighbourhood it took water out of.
/// </para>
/// <para>
/// <b>The split is even, which is what dividing by five buys.</b> Giving each lower neighbour half
/// the difference in turn drains the source as it goes: a cell with 1,000 mL and four empty
/// neighbours ends at 500, 250, 125, 62 and keeps 63 — a fan pointing at whichever neighbour
/// happened to be examined first. Against a snapshot of the source and a fixed divisor of five it
/// gives 200 to each and keeps 200, which is the answer with no preferred direction.
/// </para>
/// <para>
/// <b>It also makes the horizontal phase provably terminate, which is this milestone's correctness
/// condition rather than a nicety.</b> Let the source hold <i>a</i>, let its <i>k</i> ≤ 4 lower
/// neighbours hold <i>b<sub>i</sub></i>, let <i>d</i> = 5, and let
/// <i>t<sub>i</sub></i> = ⌊(<i>a</i> − <i>b<sub>i</sub></i>)/<i>d</i>⌋. The change in the sum of
/// squares over those cells is <c>T² + Σt² − 2Σt(a − b)</c> where <c>T = Σt</c>. Since
/// <c>(a − b) ≥ d·t</c> by the floor and <c>T² ≤ kΣt²</c> by Cauchy–Schwarz, that is at most
/// <c>(k + 1 − 2d)Σt²</c>, which for <c>k ≤ 4</c> and <c>d = 5</c> is at most <c>−5Σt²</c> —
/// <b>strictly negative whenever anything moves at all</b>. The sum of squares is a non-negative
/// integer, so only finitely many sideways moves can happen at a fixed water height. Vertical
/// moves strictly reduce a different quantity — the sum of <i>amount × Y</i>, which is bounded
/// below because an unloaded voxel is solid — so the pair ordered lexicographically strictly
/// decreases on every move the automaton can make.
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
/// millilitres" a first reading suggests, and a test asserting a global bound will fail.
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

    /// <summary>Millilitres that move from a cell to one horizontal neighbour in one tick.</summary>
    /// <param name="amount">The source's contents at the start of the horizontal phase.</param>
    /// <param name="neighbour">The neighbour's contents.</param>
    public static int HorizontalTransfer(int amount, int neighbour)
    {
        if (neighbour >= amount || !CanSpread(amount))
        {
            return 0;
        }

        return (amount - neighbour) / FluidConstants.SpreadDivisor;
    }
}
