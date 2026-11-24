namespace Overburden.Core.Machines;

/// <summary>
/// One channel's power at one boundary (IDEAL-MACHINES-PLAN, "IM-6 — the code-level pass", decision 2):
/// each term the tick counted on the channel, where its global twin is counted, and what was drawn out of
/// the channel's bank between the boundary before and this one.
/// </summary>
/// <param name="GeneratedMillijoules">What the channel's converters made this boundary.</param>
/// <param name="HeldFromPoolMillijoules">What the pool paid of its vaults' bills.</param>
/// <param name="HeldFromOwnMillijoules">What the vaults' own charges paid of their bills (IM-3b).</param>
/// <param name="PumpsDrewMillijoules">What its pumps paid.</param>
/// <param name="OwnGivenMillijoules">What the spare put into its vaults' own charges, after the consumers.</param>
/// <param name="OwnReturnedMillijoules">What own charges over a minute of their bills gave back, before the bills.</param>
/// <param name="DrawnMillijoules">What windows, siphons and any later drawer took out of its bank since the boundary before.</param>
public readonly record struct ChannelFlows(
    long GeneratedMillijoules,
    long HeldFromPoolMillijoules,
    long HeldFromOwnMillijoules,
    long PumpsDrewMillijoules,
    long OwnGivenMillijoules,
    long OwnReturnedMillijoules,
    long DrawnMillijoules)
{
    /// <summary>What a runway counts coming in: the generation, and only that.</summary>
    /// <remarks>
    /// Left out, by decision 2: an own charge's return (a transfer inside <c>stored</c>, IM-3b's decision 6),
    /// and every door that is not a flow — <c>entering</c>, so Alt+P, a restore and a cell placed or switched
    /// on; <c>leaving</c>, so a cell picked up. None of them is in this record to be counted.
    /// </remarks>
    public long In => GeneratedMillijoules;

    /// <summary>What a runway counts going out: the bills the pool paid, the pumps, and the draws.</summary>
    /// <remarks>
    /// Left out: what the own charges were given (a transfer, as the return is) and what they paid, which was
    /// counted out of the bank when it was given.
    /// </remarks>
    public long Out => HeldFromPoolMillijoules + PumpsDrewMillijoules + DrawnMillijoules;
}

/// <summary>How a runway reads on the HUD (decision 2).</summary>
public enum RunwayLevel : byte
{
    /// <summary>Not draining: the bank holds or grows, and no runway is shown.</summary>
    None,

    /// <summary>Draining, with ten minutes or more left: shown, uncoloured.</summary>
    Plain,

    /// <summary>Under ten minutes.</summary>
    Amber,

    /// <summary>Under sixty seconds.</summary>
    Red,
}

/// <summary>A channel's runway as the last boundary left it: the bank, the flows over the window as power, and the time left.</summary>
/// <param name="BankMillijoules">What the channel's enabled cells held at the last boundary recorded.</param>
/// <param name="InMilliwatts">The window's mean inflow, as power.</param>
/// <param name="OutMilliwatts">The window's mean outflow, as power.</param>
/// <param name="RunwayMillis">The bank over the net drain, in milliseconds; null while not draining.</param>
public readonly record struct RunwayReading(long BankMillijoules, long InMilliwatts, long OutMilliwatts, long? RunwayMillis)
{
    /// <summary>In less out, as power: negative while draining.</summary>
    public long NetMilliwatts => InMilliwatts - OutMilliwatts;

    /// <summary>Red under a minute, amber under ten, plain from there, none while not draining.</summary>
    public RunwayLevel Level => RunwayMillis switch
    {
        null => RunwayLevel.None,
        < Runway.RedUnderMillis => RunwayLevel.Red,
        < Runway.AmberUnderMillis => RunwayLevel.Amber,
        _ => RunwayLevel.Plain,
    };
}

/// <summary>
/// How long a channel's bank lasts at the rate it is going (IDEAL-MACHINES-PLAN, "IM-6 — the code-level pass",
/// decision 2): the last twenty boundaries of each channel's flows, in and out, and the bank at the last.
/// </summary>
/// <remarks>
/// <para>
/// <b>Built from flows, never from the bank's change</b> (facts 6 and 7). Alt+P fills cells through
/// <c>entering</c>, a picked cell leaves through <c>leaving</c>, and a vault's own minute moves charge between
/// the cells and itself; averaged over ten seconds, picking up one full 5 GJ cell would read as 500 MW out. So
/// in is the generation and out is the bills the pool paid, the pumps and the draws — <see cref="ChannelFlows.In"/>
/// and <see cref="ChannelFlows.Out"/> — and a new drawer is outflow because the bank's draw counts itself.
/// </para>
/// <para>
/// <b>The runway is the bank over the net drain</b>, <c>bank ÷ (out − in)</c> over the window, while out is
/// more than in: amber under ten minutes, red under sixty seconds. Before twenty boundaries it averages what
/// it has. It prints only; no conserved quantity moves.
/// </para>
/// </remarks>
public sealed class Runway
{
    /// <summary>Boundaries kept for each channel: ten seconds at the game's 2 Hz.</summary>
    public const int Boundaries = 20;

    /// <summary>Under this, a runway is amber: ten minutes.</summary>
    public const long AmberUnderMillis = 10 * 60 * 1000;

    /// <summary>Under this, a runway is red: sixty seconds.</summary>
    public const long RedUnderMillis = 60 * 1000;

    private readonly long _boundaryMillis;
    private readonly long[,] _in = new long[MachineTick.Channels, Boundaries];
    private readonly long[,] _out = new long[MachineTick.Channels, Boundaries];
    private readonly long[] _bank = new long[MachineTick.Channels];
    private int _next;

    /// <param name="boundaryMillis">How much of a second one boundary is — the tick's.</param>
    public Runway(long boundaryMillis = MachineTick.DefaultBoundaryMillis)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(boundaryMillis);
        _boundaryMillis = boundaryMillis;
    }

    /// <summary>Boundaries recorded, up to <see cref="Boundaries"/> — the window's length.</summary>
    public int Recorded { get; private set; }

    /// <summary>One boundary, read off the tick after its run: every channel's flows and bank.</summary>
    public void Record(MachineTick tick)
    {
        ArgumentNullException.ThrowIfNull(tick);
        if (tick.BoundaryMillis != _boundaryMillis)
        {
            throw new ArgumentException($"The tick's boundary is {tick.BoundaryMillis} ms and this runway's {_boundaryMillis} ms.", nameof(tick));
        }

        Span<ChannelFlows> flows = stackalloc ChannelFlows[MachineTick.Channels];
        Span<long> banks = stackalloc long[MachineTick.Channels];
        for (int channel = 0; channel < MachineTick.Channels; channel++)
        {
            flows[channel] = tick.FlowsOn(channel);
            banks[channel] = tick.BankOf(channel);
        }

        Record(flows, banks);
    }

    /// <summary>One boundary from its figures: sixteen channels' flows and banks.</summary>
    public void Record(ReadOnlySpan<ChannelFlows> flows, ReadOnlySpan<long> banks)
    {
        if (flows.Length != MachineTick.Channels || banks.Length != MachineTick.Channels)
        {
            throw new ArgumentException($"A boundary is {MachineTick.Channels} channels' flows and banks.");
        }

        for (int channel = 0; channel < MachineTick.Channels; channel++)
        {
            _in[channel, _next] = flows[channel].In;
            _out[channel, _next] = flows[channel].Out;
            _bank[channel] = banks[channel];
        }

        _next = (_next + 1) % Boundaries;
        Recorded = Math.Min(Recorded + 1, Boundaries);
    }

    /// <summary>The channel's runway over the window as it stands.</summary>
    public RunwayReading Read(int channel)
    {
        int c = Math.Clamp(channel, 0, MachineTick.Channels - 1);
        if (Recorded == 0)
        {
            return new RunwayReading(_bank[c], 0, 0, null);
        }

        // The slots past Recorded have never been written, so the first Recorded are the window either way.
        long inSum = 0, outSum = 0;
        for (int i = 0; i < Recorded; i++)
        {
            inSum += _in[c, i];
            outSum += _out[c, i];
        }

        long windowMillis = Recorded * _boundaryMillis;
        long inMilliwatts = (long)((Int128)inSum * 1000 / windowMillis);
        long outMilliwatts = (long)((Int128)outSum * 1000 / windowMillis);
        if (outSum <= inSum)
        {
            return new RunwayReading(_bank[c], inMilliwatts, outMilliwatts, null);
        }

        Int128 runway = (Int128)_bank[c] * windowMillis / (outSum - inSum);
        return new RunwayReading(_bank[c], inMilliwatts, outMilliwatts, runway > long.MaxValue ? long.MaxValue : (long)runway);
    }
}
