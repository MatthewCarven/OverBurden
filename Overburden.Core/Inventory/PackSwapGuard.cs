namespace Overburden.Core.Inventory;

/// <summary>
/// The pack swap that asks first (ALPHA-PLAN B10): a right-click that would put on a pack holding
/// less than the worn one, of the same kind, does nothing the first time and is let through by a
/// second right-click for the same kind within <see cref="WindowSeconds"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Asks rather than refuses</b>, because only the worn pack charges (the channel's recharge and
/// Alt+P both fill the worn one alone, and there is no take-off verb), so the same-kind swap is the
/// only way to charge a second pack of a kind. A refusal would make that impossible while the worn
/// pack held anything. What it catches is his play of 2026-10-12: a right-click with a lithium-ion
/// pack in hand, wearing one at 5.0 MJ, put on an empty one, and it read as losing the charge.
/// </para>
/// <para>
/// The clock is the caller's, in seconds, so a test can step it. Any swap let through, and any
/// that never needed asking, clears the question: a second click only counts as the answer to the
/// click just before it.
/// </para>
/// </remarks>
public sealed class PackSwapGuard
{
    /// <summary>How long a question stands: a second right-click inside this many seconds is the yes.</summary>
    public const double WindowSeconds = 3.0;

    private int _askedMaterialId = -1;
    private double _askedAtSeconds;

    /// <summary>
    /// Whether the swap to a pack of <paramref name="materialId"/> may go ahead now.
    /// <paramref name="wouldWearLess"/> is <see cref="CarriedPacks.WouldWearLess"/>'s answer.
    /// False means "asked": the caller says so on screen and does nothing else.
    /// </summary>
    public bool Allows(int materialId, bool wouldWearLess, double nowSeconds)
    {
        if (!wouldWearLess)
        {
            _askedMaterialId = -1;
            return true;
        }

        if (_askedMaterialId == materialId && nowSeconds - _askedAtSeconds <= WindowSeconds)
        {
            _askedMaterialId = -1;
            return true;
        }

        _askedMaterialId = materialId;
        _askedAtSeconds = nowSeconds;
        return false;
    }
}
