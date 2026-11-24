using Overburden.Core.Content;

namespace Overburden.Core.Machines;

/// <summary>
/// The clicks' water branch (IDEAL-MACHINES-PLAN, "IM-6 — the code-level pass", decision 11 and fact 13): what
/// the buttons and Q do while a tool that takes water is on T. Built and tested before any water tool shipped.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it exists.</b> Nothing in the Game read a tool's <see cref="ToolDefinition.Takes"/> before IM-6b, so a
/// water tool on T would have been swung by the miner and filled by the brush as a rock tool — and free, since a
/// siphon's <c>joulesPerVoxel</c> is 0. On a water tier the controllers are never asked to carve or fill: Main
/// asks <see cref="IsWaterTool"/> before each, and routes the click here instead.
/// </para>
/// <para>
/// <b>The verbs.</b> The left click's edge, or Q held, is a take; the right click's edge is a pour; each queues
/// one op on the <see cref="Siphon"/>, which moves nothing until the next boundary. The right click keeps the
/// hand's item verb (the computer opens a panel, a pack is worn) when an item is in hand, because those verbs are
/// the hand's and the pour is the fill's twin, which the hand's material asks for. One cooldown, the tool's
/// <c>cooldownSeconds</c>, serves both verbs, as one bite and one fill share a tier's rate.
/// </para>
/// </remarks>
public sealed class SiphonClicks
{
    /// <summary>Whether <paramref name="tool"/> takes water: then the carve and the fill are never asked, and the clicks come here.</summary>
    public static bool IsWaterTool(ToolDefinition? tool) => tool is { Takes: ToolTakes.Water };

    /// <summary>Seconds until the next op may queue; zero when one may.</summary>
    public double CooldownLeft { get; private set; }

    /// <summary>Takes queued through this branch, over its lifetime.</summary>
    public long Takes { get; private set; }

    /// <summary>Pours queued through this branch, over its lifetime.</summary>
    public long Pours { get; private set; }

    /// <summary>Runs the cooldown down by a frame. Called once a frame, before <see cref="Take"/> and <see cref="Pour"/>.</summary>
    public void Cool(double delta)
    {
        if (delta > 0)
        {
            CooldownLeft = Math.Max(0.0, CooldownLeft - delta);
        }
    }

    /// <summary>
    /// The take's decision: <see cref="SiphonVerb.Take"/> when <paramref name="tool"/> takes water, the left click
    /// was pressed this frame or Q is held, and the cooldown has run out; otherwise null.
    /// </summary>
    /// <param name="pressed">The left button's edge this frame.</param>
    /// <param name="held">Q held (or a profile's held button): a take at every cooldown, no click.</param>
    public SiphonVerb? Take(ToolDefinition? tool, bool pressed, bool held)
    {
        if (!IsWaterTool(tool) || !(pressed || held) || CooldownLeft > 0)
        {
            return null;
        }

        CooldownLeft = tool!.CooldownSeconds;
        Takes++;
        return SiphonVerb.Take;
    }

    /// <summary>
    /// The pour's decision: <see cref="SiphonVerb.Pour"/> when <paramref name="tool"/> takes water, the right click
    /// was pressed this frame, no item is in hand, and the cooldown has run out; otherwise null.
    /// </summary>
    /// <param name="pressed">The right button's edge this frame.</param>
    /// <param name="itemInHand">The hand holds an item (the computer, a pack): its own verb answers the right click.</param>
    public SiphonVerb? Pour(ToolDefinition? tool, bool pressed, bool itemInHand)
    {
        if (!IsWaterTool(tool) || !pressed || itemInHand || CooldownLeft > 0)
        {
            return null;
        }

        CooldownLeft = tool!.CooldownSeconds;
        Pours++;
        return SiphonVerb.Pour;
    }
}
