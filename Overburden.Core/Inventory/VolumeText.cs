using System.Globalization;
using Overburden.Core.Fluid;

namespace Overburden.Core.Inventory;

/// <summary>
/// A volume as the hotbar shows it: litres under a cubic metre, cubic metres from one up.
/// </summary>
/// <remarks>
/// <para>
/// <b>His answer, 2026-10-12</b> (the hotbar's unit, owed before IM-6): <c>1,767,903,281 L</c> does not
/// fit a slot, and IM-6 puts water in the hotbar's last slot. A cubic metre is 1,000 L and one brush
/// cell, so a heap reads in the unit a player builds in; under one, a few voxels (15.625 L each) still
/// read in litres rather than as <c>0.02 m³</c>. Arms not taken: cubic metres always (a voxel reads
/// <c>0.016 m³</c>), and cubic centimetres (a voxel reads <c>15,625 cm³</c>).
/// </para>
/// <para>
/// <b>Floored, never rounded.</b> A slot answers "have I enough?", so 99.98 m³ must not read as 100.
/// Two decimals under 1,000 m³, where one voxel (0.015625 m³) still moves the figure; whole cubic
/// metres from 1,000 up, where a decimal is noise and the width is what matters.
/// </para>
/// <para>
/// <b>Integer arithmetic, in millilitres,</b> because water arrives in millilitres (IM-6's store) and a
/// voxel is exactly 15,625 of them: a float would floor 1.13 m³ to 1.12. In Core rather than the HUD so
/// the rule has tests, and so the water slot and the material slots share one definition of a volume.
/// </para>
/// </remarks>
public static class VolumeText
{
    /// <summary>From this many cubic metres up, whole cubic metres only.</summary>
    public const long WholeCubicMetresFrom = 1_000;

    private const long HundredthOfACubicMetre = FluidConstants.MillilitresPerCubicMetre / 100;

    /// <summary>A holding counted in voxels — a pocket material's.</summary>
    public static string OfVoxels(long voxels) =>
        OfMillilitres(checked(voxels * FluidConstants.CellCapacityMillilitres));

    /// <summary>A holding counted in millilitres — water's.</summary>
    public static string OfMillilitres(long millilitres)
    {
        if (millilitres < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(millilitres), millilitres, "A holding cannot be negative.");
        }

        CultureInfo culture = CultureInfo.CurrentCulture;
        if (millilitres < FluidConstants.MillilitresPerCubicMetre)
        {
            return (millilitres / FluidConstants.MillilitresPerLitre).ToString("N0", culture) + " L";
        }

        if (millilitres < WholeCubicMetresFrom * FluidConstants.MillilitresPerCubicMetre)
        {
            // Decimal, so the hundredths divide exactly: 6,399 voxels is 99.98, never 99.979999.
            decimal cubicMetres = (millilitres / HundredthOfACubicMetre) / 100m;
            return cubicMetres.ToString("#,0.##", culture) + " m³";
        }

        return (millilitres / FluidConstants.MillilitresPerCubicMetre).ToString("N0", culture) + " m³";
    }
}
