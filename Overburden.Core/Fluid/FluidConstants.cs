using Overburden.Core.Voxels;

namespace Overburden.Core.Fluid;

/// <summary>
/// The unit Tier B counts in, and how much of it one cell holds.
/// </summary>
/// <remarks>
/// <para>
/// <b>The unit is the millilitre, and that is a correction to DESIGN rather than a detail.</b>
/// DESIGN says a cell holds "an amount 0-255", written before M7 existed and therefore before
/// litres were the currency of water in this project. A 0.25 m cell is 0.015625 m³, which is
/// <b>15.625 litres — not a whole number</b>, so a 0..255 scale makes one unit 61.27 mL and every
/// conversion across the Tier A ↔ Tier B seam rounds. M8's oracle is volume conservation
/// <i>with no tolerance</i>, the same standard as M7b's "an hour in one call equals 3,600 calls of
/// one second, to the litre", and a rounding seam cannot meet it.
/// </para>
/// <para>
/// <b>Millilitres make both directions of the seam exact by arithmetic.</b> A litre is exactly
/// 1,000 mL, so Tier A → Tier B never rounds at all; Tier B → Tier A hands over whole litres and
/// what is left is under one litre, which is 6.4% of a single 0.25 m cell — a number that is
/// <i>reported</i> rather than absorbed. The cost of the correction is one byte per cell: 15,625
/// does not fit in a <c>byte</c> and does fit in a <c>ushort</c> with room to spare.
/// </para>
/// <para>
/// <b>The capacity is an exact integer division and that is luck worth pinning.</b>
/// 1,000,000 mL per cubic metre divided by 4³ voxels per cubic metre is 15,625 exactly. At three
/// voxels per metre it would be 1,000,000 / 27, which truncates — so a change to
/// <see cref="VoxelConstants.VoxelsPerMetre"/> would silently start losing a fraction of every
/// cell. <c>FluidConstantsTests</c> asserts the division is exact rather than trusting it.
/// </para>
/// </remarks>
public static class FluidConstants
{
    /// <summary>Millilitres in a litre. Tier A's currency, and the only unit that crosses the seam.</summary>
    public const int MillilitresPerLitre = 1_000;

    /// <summary>Millilitres in a cubic metre.</summary>
    public const int MillilitresPerCubicMetre = 1_000_000;

    /// <summary>Voxels in a cubic metre. 4³ at the locked voxel scale.</summary>
    public const int VoxelsPerCubicMetre =
        VoxelConstants.VoxelsPerMetre * VoxelConstants.VoxelsPerMetre * VoxelConstants.VoxelsPerMetre;

    /// <summary>
    /// Millilitres one full cell holds: 15,625, which is 15.625 L.
    /// </summary>
    /// <remarks>
    /// Must stay inside <see cref="ushort"/>, because that is what <see cref="FluidGrid"/> stores.
    /// </remarks>
    public const int CellCapacityMillilitres = MillilitresPerCubicMetre / VoxelsPerCubicMetre;

    /// <summary>Litres one full cell holds, as a double. For reports, never for arithmetic.</summary>
    public const double CellCapacityLitres = CellCapacityMillilitres / (double)MillilitresPerLitre;

    /// <summary>
    /// What a cell divides its surplus by when it spreads sideways under <see cref="SpreadRule.Fixed"/>:
    /// one part for itself and one for each of its four horizontal neighbours. The largest
    /// divisor under <see cref="SpreadRule.Counted"/> too, so the residue bound reads it.
    /// </summary>
    /// <remarks>
    /// <b>Fixed at five rather than counted from the eligible neighbours, and that was a
    /// correctness decision rather than a simplification.</b> Dividing by <i>eligible + 1</i>
    /// converges faster and is the obvious rule, but it makes one cell's move depend on <i>its
    /// neighbours' amounts</i> and not only on the pair — so a neighbour <b>gaining</b> water can
    /// shrink the divisor and turn a transfer of zero into a transfer of one. The wake rule then
    /// has to wake both neighbourhoods of every move instead of one, or the automaton stops with
    /// moves still available. Measured, not argued: the variable divisor left four cells of an
    /// 8x8 floor one millilitre out of level, and <c>FluidSimulation.VerifyQuiescent</c> caught
    /// it on the first run it was ever asked.
    /// <para>
    /// The cost is convergence speed, which M8a called the cheapest thing this project has to
    /// spend. Levelling two cells takes about sixteen ticks instead of one, because the
    /// difference decays by three fifths a tick; the <i>frontier</i> still advances one cell per
    /// tick. <b>The play spent it (2026-09-07)</b>: at a straight front a fifth of the surplus
    /// leaves a two-cell skirt and a mound behind it, and Matthew called the spread low. The
    /// counted rule is back as <see cref="SpreadRule.Counted"/>, with the double wake it needs,
    /// priced on the bench rather than argued; this constant stays the fixed rule's divisor and
    /// the counted rule's ceiling.
    /// </para>
    /// </remarks>
    public const int SpreadDivisor = 5;

    /// <summary>
    /// A cell holding less than this does not spread sideways. Water below it is a puddle rather
    /// than a film.
    /// </summary>
    /// <remarks>
    /// <b>Without a floor, five litres spilt on a cave floor spreads over every cell it can
    /// reach</b> — measured at 78 mL per cell across a whole 8x8 floor, a film 1.25 mm deep in a
    /// 250 mm voxel — and every one of those cells is then held, drawn, stored and re-woken by any
    /// disturbance. That is the active set the whole tier is built to bound, spent on water nobody
    /// can see.
    /// <para>
    /// <b>One litre, because that is the smallest amount the Tier A seam can express.</b> Tier A
    /// counts litres; anything under one of them cannot cross the boundary between the tiers in
    /// either direction, so a rule that stops moving water at exactly that point costs nothing
    /// the rest of the system could have used. It is 6.4% of a cell — 1.6 cm of standing water in
    /// a 25 cm voxel.
    /// </para>
    /// <para>
    /// <b>The cost, stated: a shallow spill settles in blotches rather than a level sheet</b>,
    /// varying by up to a litre between neighbouring cells. <b>Revisit trigger: M8d's transparent
    /// pass making that visible</b>, or a play-test reporting a puddle that looks stuck. Falling
    /// is never gated on this — one millilitre still falls, or water would hang in the air.
    /// </para>
    /// </remarks>
    public const int MinimumSpreadMillilitres = MillilitresPerLitre;
}
