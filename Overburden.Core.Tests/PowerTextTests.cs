using System.Globalization;
using Overburden.Core.Inventory;

namespace Overburden.Core.Tests;

/// <summary>
/// IM-6a, decision 3: power in Core beside the volume — W under a kilowatt, then kW, MW and GW, one decimal
/// under 100, floored — and the panel's energy moved to Core with the same output.
/// </summary>
public class PowerTextTests
{
    [Theory]
    [InlineData(0L, "0 W")]
    [InlineData(1L, "<0.1 W")]
    [InlineData(64L, "<0.1 W")]                       // a god vault holding a cubic metre of blocks: not nothing
    [InlineData(99L, "<0.1 W")]
    [InlineData(100L, "0.1 W")]
    [InlineData(2_500L, "2.5 W")]                     // a low vault's cubic metre of blocks
    [InlineData(99_999L, "99.9 W")]                   // floored: never the 100.0 a rounding makes
    [InlineData(100_000L, "100 W")]
    [InlineData(950_000L, "950 W")]
    [InlineData(999_999L, "999 W")]                   // floored: never 1,000 W
    [InlineData(1_000_000L, "1.0 kW")]
    [InlineData(3_800_000L, "3.8 kW")]                // his line's sun
    [InlineData(99_960_000L, "99.9 kW")]
    [InlineData(104_000_000L, "104 kW")]              // his line's drain
    [InlineData(999_999_999L, "999 kW")]
    [InlineData(1_000_000_000L, "1.0 MW")]
    [InlineData(500_000_000_000L, "500 MW")]
    [InlineData(1_000_000_000_000L, "1.0 GW")]
    [InlineData(12_345_000_000_000_000L, "12,345 GW")]
    [InlineData(-104_000_000L, "−104 kW")]           // the panel's minus sign
    [InlineData(-1L, "−<0.1 W")]
    public void APowerReadsInTheUnitItIsIn(long milliwatts, string expected)
    {
        Assert.Equal(expected, Culture.Invariant(() => PowerText.OfMilliwatts(milliwatts)));
    }

    [Theory]
    [InlineData(1_900_000L, 500L, "3.8 kW")]
    [InlineData(52_000_000L, 500L, "104 kW")]
    [InlineData(1_900_000L, 1_000L, "1.9 kW")]       // the same energy over a longer boundary is half the power
    [InlineData(976L, 500L, "1.9 W")]                 // 500 W under a byte of 255: the storm dims, never off
    [InlineData(1L, 500L, "<0.1 W")]
    public void MillijoulesABoundaryReadAsThePowerTheyAre(long millijoules, long boundaryMillis, string expected)
    {
        Assert.Equal(expected, Culture.Invariant(() => PowerText.OfMillijoulesPerBoundary(millijoules, boundaryMillis)));
    }

    [Fact]
    public void TheDefaultBoundaryIsTheTicksAndWattsAreAModFilesUnit()
    {
        Assert.Equal("104 kW", Culture.Invariant(() => PowerText.OfMillijoulesPerBoundary(52_000_000)));
        Assert.Equal("500 W", Culture.Invariant(() => PowerText.OfWatts(500)));
        Assert.Equal("2.0 kW", Culture.Invariant(() => PowerText.OfWatts(2_000)));
        Assert.Throws<ArgumentOutOfRangeException>(() => PowerText.OfMillijoulesPerBoundary(1, 0));
    }

    [Fact]
    public void TheExtremesDoNotThrow()
    {
        Assert.EndsWith(" GW", PowerText.OfMilliwatts(long.MaxValue));
        Assert.StartsWith("−", PowerText.OfMilliwatts(long.MinValue));
    }

    [Fact]
    public void ThePowersSeparatorsAreTheHudsCulture()
    {
        CultureInfo german = CultureInfo.GetCultureInfo("de-DE");
        Assert.Equal("3,8 kW", Culture.Under(german, () => PowerText.OfMilliwatts(3_800_000)));
        Assert.Equal("12.345 GW", Culture.Under(german, () => PowerText.OfMilliwatts(12_345_000_000_000_000L)));
    }

    // ---------------------------------------------------------------- energy, the panel's Joules moved

    [Theory]
    [InlineData(0L, "0 mJ")]
    [InlineData(999L, "999 mJ")]
    [InlineData(1_000L, "1 J")]
    [InlineData(9_999_000L, "9,999 J")]
    [InlineData(9_999_999L, "10,000 J")]              // rounded by the format, as it always was
    [InlineData(10_000_000L, "10.0 kJ")]
    [InlineData(3_600_000_000L, "3,600.0 kJ")]        // the lead-acid pack
    [InlineData(9_999_900_000L, "9,999.9 kJ")]
    [InlineData(10_000_000_000L, "10.0 MJ")]
    [InlineData(612_000_000_000L, "612.0 MJ")]        // his line's bank
    [InlineData(5_000_000_000_000L, "5,000.0 MJ")]    // a vacuum cell
    [InlineData(10_000_000_000_000L, "10.00 GJ")]
    [InlineData(1_000_000_000_000_000L, "1,000.00 GJ")]
    [InlineData(-1_500_000L, "−1,500 J")]
    [InlineData(-999L, "−999 mJ")]
    public void AnEnergyReadsAsThePanelAlwaysPrintedIt(long millijoules, string expected)
    {
        Assert.Equal(expected, Culture.Invariant(() => EnergyText.Of(millijoules)));
    }
}
