namespace Overburden.Core.Machines;

/// <summary>
/// A channel's cells as a bank a window draws on between boundaries (IDEAL-MACHINES-PLAN decision
/// 8). Internal, and implemented explicitly by <see cref="MachineTick"/>: every caller of
/// <see cref="Draw"/> counts what came out in a term of its own — the pack's <c>taken</c> now, the
/// quarry's <c>dug</c> and recall's <c>recallPaid</c> later — or the pool's identity breaks.
/// </summary>
internal interface IChannelBank
{
    /// <summary>What the channel's enabled cells hold now — what a window can spend.</summary>
    long BankMillijoules(int channel);

    /// <summary>
    /// Takes up to <paramref name="millijoules"/> out of the channel's enabled cells, last-filled
    /// first, writing their charge directly. Returns what came out. Throws inside the tick's run.
    /// </summary>
    long Draw(int channel, long millijoules);
}
