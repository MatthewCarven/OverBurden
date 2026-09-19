using System.Collections.Generic;
using System.Linq;

namespace Overburden.Core.Content;

/// <summary>
/// The T cycle's two rules: which order the tools come in, and which one a fresh world starts
/// on. In Core so that the shipped content is tested against the rules the game applies.
/// </summary>
/// <remarks>
/// <para>
/// <b>Order is (threshold, bite, id).</b> Weakest first, so the cycle reads as a ladder —
/// hand → steel → miner → excavator — without a <c>tier</c> number in the format. Two tools at
/// one threshold sort by the size of their bite; the ID is the tie-break so the same content
/// always cycles the same way.
/// </para>
/// <para>
/// <b>The default is the strongest tool that is free.</b> Until 2026-09-19 it was the strongest
/// tool, which was why the entangled miner had to stay off the cycle: on it, a 5.0 threshold
/// would have made it a fresh world's first pick, and the first swing would cost from an empty
/// pack and take nothing — a broken build to the eye. Starting on the weakest would be a truer
/// tutorial, but a first swing at stone doing nothing reads the same way. A content set with no
/// free tool defaults to its first, and says nothing; a mod that ships only costing tools has
/// chosen that.
/// </para>
/// </remarks>
public static class ToolTiers
{
    /// <summary>The tools in cycle order: weakest first, smallest bite first among equals, ID as the tie-break.</summary>
    public static List<ToolDefinition> Order(IEnumerable<ToolDefinition> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        return tools
            .OrderBy(t => t.HardnessThreshold)
            .ThenBy(t => t.Bite)
            .ThenBy(t => t.Id.ToString(), StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The tool a fresh world starts on: the strongest free one, or the first when none is free.</summary>
    public static ToolDefinition Default(IReadOnlyList<ToolDefinition> ordered)
    {
        ArgumentNullException.ThrowIfNull(ordered);
        if (ordered.Count == 0)
        {
            throw new ArgumentException("No tools to choose a default from.", nameof(ordered));
        }

        for (int i = ordered.Count - 1; i >= 0; i--)
        {
            if (ordered[i].IsFree)
            {
                return ordered[i];
            }
        }

        return ordered[0];
    }
}
