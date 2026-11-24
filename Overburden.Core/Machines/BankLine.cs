using System.Globalization;
using Overburden.Core.Building;
using Overburden.Core.Inventory;

namespace Overburden.Core.Machines;

/// <summary>
/// One channel's bank as the HUD shows it while the quantum backpack is worn (IDEAL-MACHINES-PLAN decision 11;
/// IM-6's pass, decision 8), read at a boundary: the runway's reading, the vaults' hold, and the store.
/// </summary>
/// <param name="Channel">The backpack's channel.</param>
/// <param name="Runway">The bank, the flows over the window as power, and the time left (<see cref="Machines.Runway"/>).</param>
/// <param name="HoldMilliwatts">What the channel's vaults bill for what they hold, as power: every vault on the channel that is not dumping or a wreck, switched off or not.</param>
/// <param name="Store">The store's fill, over the vaults that are open to it (<see cref="MachineSet.StoreFillOf"/>).</param>
/// <param name="StoredWaterMillilitres">The water in the store (<see cref="MachineSet.StoredWaterOf"/>) — the gauge beside the row.</param>
/// <param name="HasStore">Whether any vault on the channel is open to the store; <c>store</c> shows only then.</param>
public readonly record struct BankView(
    int Channel,
    RunwayReading Runway,
    long HoldMilliwatts,
    StoreFill Store,
    long StoredWaterMillilitres,
    bool HasStore);

/// <summary>The soonest lit fuse the HUD's top line names (decision 13): its time left, and whether it waits on a paid bill.</summary>
/// <param name="MillisLeft">The fuse's milliseconds left.</param>
/// <param name="Paused">Its bill was paid at the last boundary, so it waits rather than burns (IM-3b).</param>
/// <param name="Cell">The vault's cell.</param>
public readonly record struct FuseWatch(int MillisLeft, bool Paused, BrushCell Cell);

/// <summary>
/// The HUD's bank line and the fuse's top line, built in Core so they are tested (IM-6's pass, decisions 8 and 13):
/// <c>BANK 612.0 MJ ch 0 · +3.8 kW sun · −104.0 kW · runway 1 h 38 m · hold 2.0 kW · store 68%</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Read at a boundary, never live</b> (IM-6a's build choice 6): Main records the runway after the machine tick
/// and hands the HUD this view, so the bank and the runway on the line agree with each other, and the HUD is told,
/// never asked.
/// </para>
/// <para>
/// <b>What shows when.</b> The runway only while the net is a drain, coloured by its <see cref="RunwayLevel"/>;
/// the hold only while a vault bills; the store only while one is open to it. The bank, the channel and both
/// flows always.
/// </para>
/// </remarks>
public static class BankLine
{
    /// <summary>The separator the HUD's status line uses between terms.</summary>
    public const string Separator = "   ·   ";

    /// <summary>The channel's bank view from the set and the runway as the last boundary left them.</summary>
    public static BankView Read(MachineSet machines, Runway runway, int channel)
    {
        ArgumentNullException.ThrowIfNull(machines);
        ArgumentNullException.ThrowIfNull(runway);
        int c = Math.Clamp(channel, 0, MachineTick.Channels - 1);
        return new BankView(
            c,
            runway.Read(c),
            HoldMilliwattsOf(machines, c),
            machines.StoreFillOf(c),
            machines.StoredWaterOf(c),
            machines.StoreOf(c).Count > 0);
    }

    /// <summary>
    /// What the channel's vaults bill, as milliwatts, floored: each vault's blocks and water at its grade's rates
    /// (decision 16) — every vault on the channel that bills, which is every one not dumping and not a wreck.
    /// </summary>
    public static long HoldMilliwattsOf(MachineSet machines, int channel)
    {
        ArgumentNullException.ThrowIfNull(machines);
        Int128 numerator = 0;
        foreach (MachineRow row in machines.Rows)
        {
            if (machines.VaultDefinitionOf(row) is not { IsChannelVault: true } definition
                || ChannelOf(row) != channel
                || !VaultState.TryRead(row, out VaultContents contents)
                || contents.Owes)
            {
                continue;
            }

            numerator += (Int128)definition.BlockHoldMilliwattsPerCubicMetre * contents.TotalVoxels * MachineDefinition.VoxelMillilitres;
            numerator += (Int128)definition.WaterHoldMilliwattsPerCubicMetre * contents.WaterMillilitres;
        }

        Int128 milliwatts = numerator / MachineDefinition.MillilitresPerCubicMetre;
        return milliwatts > long.MaxValue ? long.MaxValue : (long)milliwatts;
    }

    /// <summary>The HUD's bank line for <paramref name="view"/>.</summary>
    public static string Text(BankView view)
    {
        RunwayReading r = view.Runway;
        var text = new System.Text.StringBuilder();
        text.Append(CultureInfo.CurrentCulture, $"BANK {EnergyText.Of(r.BankMillijoules)} ch {view.Channel}");
        text.Append(Separator).Append('+').Append(PowerText.OfMilliwatts(r.InMilliwatts)).Append(" sun");
        text.Append(Separator).Append('−').Append(PowerText.OfMilliwatts(r.OutMilliwatts));
        if (r.RunwayMillis is { } runwayMillis)
        {
            text.Append(Separator).Append("runway ").Append(Duration(runwayMillis));
        }

        if (view.HoldMilliwatts > 0)
        {
            text.Append(Separator).Append("hold ").Append(PowerText.OfMilliwatts(view.HoldMilliwatts));
        }

        if (view.HasStore)
        {
            text.Append(Separator).Append("store ").Append(Percent(view.Store.UsedMillilitres, view.Store.InsideMillilitres));
        }

        return text.ToString();
    }

    /// <summary>
    /// A time left, floored, in its two largest units: <c>45 s</c>, <c>12 m 05 s</c>, <c>1 h 38 m</c>, <c>3 d 04 h</c>.
    /// </summary>
    public static string Duration(long millis)
    {
        long seconds = Math.Max(0, millis) / 1000;
        if (seconds < 60) return $"{seconds} s";
        long minutes = seconds / 60;
        if (minutes < 60) return $"{minutes} m {seconds % 60:00} s";
        long hours = minutes / 60;
        if (hours < 24) return $"{hours} h {minutes % 60:00} m";
        long days = hours / 24;
        return days < 1000 ? $"{days} d {hours % 24:00} h" : "999+ d";
    }

    /// <summary>A whole percentage, floored; a non-zero share under one reads <c>&lt;1%</c>, and no capacity reads <c>–</c>.</summary>
    public static string Percent(long used, long inside)
    {
        if (inside <= 0) return "–";
        long percent = (long)((Int128)Math.Max(0, used) * 100 / inside);
        return used > 0 && percent == 0 ? "<1%" : $"{percent}%";
    }

    /// <summary>
    /// The soonest lit fuse among the vaults the player should hear about (decision 13): those whose cell
    /// <paramref name="inLoadedBox"/> answers for, and those on <paramref name="backpackChannel"/> wherever they are.
    /// Null when no such vault is fusing.
    /// </summary>
    public static FuseWatch? SoonestFuse(MachineSet machines, Func<BrushCell, bool> inLoadedBox, int? backpackChannel)
    {
        ArgumentNullException.ThrowIfNull(machines);
        ArgumentNullException.ThrowIfNull(inLoadedBox);
        FuseWatch? soonest = null;
        foreach (MachineRow row in machines.Rows)
        {
            if (machines.VaultDefinitionOf(row) is not { IsChannelVault: true }
                || !VaultState.TryRead(row, out VaultContents contents)
                || contents.Phase != VaultPhase.Fusing
                || contents.Wreck)
            {
                continue;
            }

            if (ChannelOf(row) != backpackChannel && !inLoadedBox(row.Cell))
            {
                continue;
            }

            var watch = new FuseWatch(contents.FuseMillisLeft, row.LastHoldPaid, row.Cell);
            if (soonest is not { } best
                || watch.MillisLeft < best.MillisLeft
                || (watch.MillisLeft == best.MillisLeft && !watch.Paused && best.Paused))
            {
                soonest = watch;
            }
        }

        return soonest;
    }

    /// <summary>The HUD's top line for a lit fuse: <c>VAULT LOSING CONTAINMENT — 14 s</c>, with <c>(paused)</c> while it waits.</summary>
    /// <remarks>The seconds round up, so the line never reads <c>0 s</c> while the fuse still burns.</remarks>
    public static string FuseText(FuseWatch fuse) =>
        $"VAULT LOSING CONTAINMENT — {(Math.Max(0, fuse.MillisLeft) + 999) / 1000} s{(fuse.Paused ? " (paused)" : string.Empty)}";

    private static int ChannelOf(MachineRow row) =>
        row.TryGetPort(MachineTick.ChannelPort, out long channel) ? (int)Math.Clamp(channel, 0, MachineTick.Channels - 1) : 0;
}
