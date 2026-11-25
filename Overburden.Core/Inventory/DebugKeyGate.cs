using Overburden.Core.Persistence;

namespace Overburden.Core.Inventory;

/// <summary>The test keys: each a door into the world or a conserved quantity that no play would have (IM-6's pass, decision 24).</summary>
public enum DebugKey
{
    /// <summary>A hundred of every item (<see cref="DebugSeed"/>).</summary>
    P,

    /// <summary>Every placed cell, and the pack, filled.</summary>
    AltP,

    /// <summary>One lithium-ion pack, in hand.</summary>
    CtrlP,

    /// <summary>Ten thousand litres poured.</summary>
    R,

    /// <summary>A million litres poured.</summary>
    CtrlR,

    /// <summary>The water within 2 m taken back.</summary>
    AltR,

    /// <summary>Alt+R's twin, since NVIDIA's overlay owns Alt+R on his machine.</summary>
    AltG,

    /// <summary>The water held, and pumps, rain and roofs with it.</summary>
    H,

    /// <summary>A full collection, its time and the heap in the log.</summary>
    AltH,

    /// <summary>A free lamp, at range.</summary>
    L,
}

/// <summary>
/// Which test keys answer, by the world's mode and <c>--dev</c> (IDEAL-MACHINES-PLAN decision 52, amended by IM-6's pass,
/// decision 24). A table in Core so "every creative-off key does nothing" is a test; the Game asks it at each handler.
/// </summary>
/// <remarks>
/// <para>
/// <b>Sandbox keeps today's game exactly</b>: P, Alt+P, Ctrl+P, R, Alt+R, Alt+G and L always answer, and Ctrl+R, H and
/// Alt+H only under <c>--dev</c> (ALPHA-PLAN B7) — a Ctrl+R without it is R, as it always was.
/// </para>
/// <para>
/// <b>Creative turns every one off, whatever <c>--dev</c> says.</b> <c>start.cmd</c> passes <c>--dev</c>, so a creative world
/// honouring it would keep H in every world he opened from it, and H can freeze a burning fuse. Command-line flags
/// (<c>--stock=</c>, <c>--nogate</c>, <c>--pack=</c>) stay honoured in any mode, and flying stays: it is the only way out
/// of burial until M11.
/// </para>
/// </remarks>
public static class DebugKeyGate
{
    /// <summary>Every test key the gate rules on: decision 24's list, Ctrl+P and Alt+H included.</summary>
    public static IReadOnlyList<DebugKey> All { get; } = Enum.GetValues<DebugKey>();

    /// <summary>Whether <paramref name="key"/> answers in a world of <paramref name="mode"/>, with <c>--dev</c> as <paramref name="dev"/>.</summary>
    public static bool Allowed(DebugKey key, WorldMode mode, bool dev) =>
        mode == WorldMode.Sandbox && (dev || !DeveloperOnly(key));

    /// <summary>The keys a sandbox world answers only under <c>--dev</c>.</summary>
    public static bool DeveloperOnly(DebugKey key) => key is DebugKey.CtrlR or DebugKey.H or DebugKey.AltH;

    /// <summary>The key as the card and the message line spell it.</summary>
    public static string Label(DebugKey key) => key switch
    {
        DebugKey.AltP => "Alt+P",
        DebugKey.CtrlP => "Ctrl+P",
        DebugKey.CtrlR => "Ctrl+R",
        DebugKey.AltR => "Alt+R",
        DebugKey.AltG => "Alt+G",
        DebugKey.AltH => "Alt+H",
        _ => key.ToString(),
    };

    /// <summary>Why <paramref name="key"/> did nothing, for the message line; null when it answers.</summary>
    public static string? Refusal(DebugKey key, WorldMode mode, bool dev)
    {
        if (Allowed(key, mode, dev))
        {
            return null;
        }

        return mode == WorldMode.Sandbox
            ? $"{Label(key)} is a developer key: start with --dev to use it."
            : $"{Label(key)} is a test key, and test keys are off in a {WorldModes.Name(mode)} world.";
    }
}
