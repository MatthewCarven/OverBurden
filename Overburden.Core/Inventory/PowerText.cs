using System.Globalization;
using Overburden.Core.Machines;

namespace Overburden.Core.Inventory;

/// <summary>
/// A power as the HUD and the panel show it (IDEAL-MACHINES-PLAN, "IM-6 — the code-level pass", decision 3):
/// W under a kilowatt, then kW, MW and GW, one decimal under 100 of the unit.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it exists</b> (fact 8): the panel printed power as energy a boundary — a bill, a panel's output, a
/// pack's recharge — and a pump's power was never shown. The HUD's bank line (decision 8) reads in watts:
/// <c>+3.8 kW sun · −104 kW</c>. The mod files stay in watts; this is only how a rate is read.
/// </para>
/// <para>
/// <b>Floored, as <see cref="VolumeText"/> is, in integer milliwatts</b>, so 99.96 kW reads <c>99.9 kW</c> and
/// never the <c>100.0 kW</c> a rounding would make of it, and 999.9 W reads <c>999 W</c> rather than
/// <c>1,000 W</c>. A power that is not zero but floors to nothing reads <c>&lt;0.1 W</c>, as the panel's share
/// reads <c>&lt;0.1%</c>: a god vault holding one cubic metre of blocks bills 64 mW, which is not nothing. A
/// negative power takes the panel's minus sign, <c>−</c>.
/// </para>
/// </remarks>
public static class PowerText
{
    private const long MilliwattsPerWatt = 1_000;
    private const long MilliwattsPerKilowatt = 1_000_000;
    private const long MilliwattsPerMegawatt = 1_000_000_000;
    private const long MilliwattsPerGigawatt = 1_000_000_000_000;

    /// <summary>Under this many of a unit, one decimal; from it, whole units.</summary>
    public const long OneDecimalUnder = 100;

    /// <summary>A power in milliwatts.</summary>
    public static string OfMilliwatts(long milliwatts)
    {
        string sign = milliwatts < 0 ? "−" : string.Empty;
        ulong m = milliwatts < 0 ? (ulong)(-(milliwatts + 1)) + 1 : (ulong)milliwatts;
        if (m == 0)
        {
            return "0 W";
        }

        if (m < MilliwattsPerWatt / 10)
        {
            return sign + "<0.1 W";
        }

        (ulong scale, string unit) = m switch
        {
            < MilliwattsPerKilowatt => ((ulong)MilliwattsPerWatt, "W"),
            < MilliwattsPerMegawatt => ((ulong)MilliwattsPerKilowatt, "kW"),
            < MilliwattsPerGigawatt => ((ulong)MilliwattsPerMegawatt, "MW"),
            _ => ((ulong)MilliwattsPerGigawatt, "GW"),
        };

        CultureInfo culture = CultureInfo.CurrentCulture;
        ulong tenths = m / (scale / 10);
        if (tenths < OneDecimalUnder * 10)
        {
            return sign + (tenths / 10m).ToString("N1", culture) + " " + unit;
        }

        return sign + (m / scale).ToString("N0", culture) + " " + unit;
    }

    /// <summary>A power in whole watts — a mod file's figure.</summary>
    public static string OfWatts(long watts) => OfMilliwatts(checked(watts * MilliwattsPerWatt));

    /// <summary>
    /// Millijoules a boundary as power, through the boundary's length — the tick's figures (a bill, a panel's
    /// output, a pump's draw) read as the rate they are: a millijoule a millisecond is a watt.
    /// </summary>
    public static string OfMillijoulesPerBoundary(long millijoules, long boundaryMillis = MachineTick.DefaultBoundaryMillis)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(boundaryMillis);
        return OfMilliwatts((long)((Int128)millijoules * MilliwattsPerWatt / boundaryMillis));
    }
}

/// <summary>
/// An energy as the panel, the HUD and the log show it: mJ under a joule, then J, kJ, MJ and GJ — the panel's
/// <c>Joules</c>, moved to Core with the same output so it has tests (IM-6's pass, decision 3).
/// </summary>
/// <remarks>
/// Each unit runs to ten thousand of itself before the next takes over — <c>9,999 J</c>, then <c>10.0 kJ</c>
/// — and the figure is rounded by the format, not floored, as it always was: 9,999,999 mJ reads
/// <c>10,000 J</c>. Changing either moves every energy on screen, so it is pinned by a table.
/// </remarks>
public static class EnergyText
{
    /// <summary>Millijoules in the unit a person reads.</summary>
    public static string Of(long millijoules)
    {
        long sign = millijoules < 0 ? -1 : 1;
        long mJ = Math.Abs(millijoules);
        string s = sign < 0 ? "−" : string.Empty;
        if (mJ < 1_000L) return $"{s}{mJ} mJ";
        if (mJ < 10_000_000L) return $"{s}{mJ / 1000.0:N0} J";
        if (mJ < 10_000_000_000L) return $"{s}{mJ / 1_000_000.0:N1} kJ";
        if (mJ < 10_000_000_000_000L) return $"{s}{mJ / 1_000_000_000.0:N1} MJ";
        return $"{s}{mJ / 1_000_000_000_000.0:N2} GJ";
    }
}
