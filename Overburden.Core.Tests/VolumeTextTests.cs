using System.Globalization;
using Overburden.Core.Inventory;

namespace Overburden.Core.Tests;

/// <summary>
/// The hotbar's volume (2026-10-12, his answer): litres under a cubic metre, cubic metres from one
/// up, floored, two decimals under 1,000 m³ and whole cubic metres past it.
/// </summary>
public class VolumeTextTests
{
    private static string Invariant(Func<string> format)
    {
        CultureInfo before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            return format();
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Theory]
    [InlineData(0L, "0 L")]
    [InlineData(1L, "15 L")]          // 15.625 L, floored
    [InlineData(63L, "984 L")]        // the most a pocket holds under a cubic metre
    [InlineData(64L, "1 m³")]         // one brush cell
    [InlineData(65L, "1.01 m³")]      // 1.015625
    [InlineData(96L, "1.5 m³")]       // no trailing zero
    [InlineData(6_399L, "99.98 m³")]  // 99.984375: floored, so it never reads as the 100 it isn't
    [InlineData(6_400L, "100 m³")]    // what P seeds of clay and of glass
    public void APocketHoldingReadsInLitresUnderACubicMetreAndInCubicMetresFromOne(long voxels, string expected)
    {
        Assert.Equal(expected, Invariant(() => VolumeText.OfVoxels(voxels)));
    }

    [Theory]
    [InlineData(999_999L, "999 L")]
    [InlineData(1_000_000L, "1 m³")]
    [InlineData(1_130_000L, "1.13 m³")]             // the float trap: 1.13 × 100 is 112.99999… as a double
    [InlineData(1_009_999L, "1 m³")]                // under a hundredth over: floored away
    [InlineData(999_999_999L, "999.99 m³")]
    [InlineData(1_000_000_000L, "1,000 m³")]        // from 1,000 m³, whole cubic metres
    [InlineData(1_000_990_000L, "1,000 m³")]
    [InlineData(1_767_903_281_000L, "1,767,903 m³")] // his number that did not fit a slot
    public void WaterInMillilitresReadsTheSameWay(long millilitres, string expected)
    {
        Assert.Equal(expected, Invariant(() => VolumeText.OfMillilitres(millilitres)));
    }

    [Fact]
    public void AVoxelIsExactlyItsMillilitres()
    {
        // One definition of a volume for the pocket and for water: 64 voxels and a million millilitres
        // are the same text, at every step a pocket can take.
        for (long voxels = 0; voxels <= 64 * 1_200; voxels += 7)
        {
            Assert.Equal(
                Invariant(() => VolumeText.OfMillilitres(voxels * 15_625)),
                Invariant(() => VolumeText.OfVoxels(voxels)));
        }
    }

    [Fact]
    public void ANegativeHoldingIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => VolumeText.OfMillilitres(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => VolumeText.OfVoxels(-1));
    }

    [Fact]
    public void TheSeparatorsAreTheHudsCulture()
    {
        // The rest of the HUD formats with the current culture (N0), so the volume does too.
        CultureInfo before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            Assert.Equal("99,98 m³", VolumeText.OfVoxels(6_399));
            Assert.Equal("1.767.903 m³", VolumeText.OfMillilitres(1_767_903_281_000L));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }
}
