using Overburden.Core.Content;
using Overburden.Core.Machines;
using Xunit;

namespace Overburden.Core.Tests;

/// <summary>
/// The T cycle's two rules (2026-09-19): the order the tools come in, and which one a fresh
/// world starts on. They moved into Core with the quantum tiers, because a costing tool on the
/// cycle is exactly the case the old "strongest tool" default got wrong.
/// </summary>
public sealed class ToolTiersTests
{
    private static ToolDefinition Tool(string id, float threshold, ToolBite bite = ToolBite.Sphere, long? joules = null) => new()
    {
        Id = Identifier.Parse(id),
        HardnessThreshold = threshold,
        RadiusMetres = 0.5f,
        Bite = bite,
        Power = joules is { } j ? new ToolPower(MachineLink.ChannelVia, j) : null,
    };

    [Fact]
    public void TheCycleIsWeakestFirstThenSmallestBiteThenId()
    {
        // Handed in shuffled, so the order is the rule's and not the registry's.
        var ordered = ToolTiers.Order(
        [
            Tool("overburden:quantum_excavator", 5.0f, ToolBite.Chunk, 50),
            Tool("overburden:steel_pick", 3.5f),
            Tool("overburden:quantum_miner", 5.0f, ToolBite.Cube, 50),
            Tool("overburden:quantum_tunneller", 5.0f, ToolBite.Tunnel, 50),
            Tool("overburden:hand_pick", 1.0f),
        ]);

        // The tunneller sits between the miner and the excavator (2026-09-23) because the bite
        // is ordered by size, not because of its name — "tunneller" sorts after "excavator".
        Assert.Equal(
            ["overburden:hand_pick", "overburden:steel_pick", "overburden:quantum_miner", "overburden:quantum_tunneller", "overburden:quantum_excavator"],
            ordered.Select(t => t.Id.ToString()).ToArray());
    }

    [Fact]
    public void TwoToolsAlikeInThresholdAndBiteCycleInIdOrder()
    {
        var ordered = ToolTiers.Order([Tool("overburden:b_pick", 1.0f), Tool("overburden:a_pick", 1.0f)]);
        Assert.Equal(["overburden:a_pick", "overburden:b_pick"], ordered.Select(t => t.Id.ToString()).ToArray());
    }

    [Fact]
    public void TheDefaultIsTheStrongestFreeToolAndNeverACostingOne()
    {
        // The whole reason the rule exists: the quantum tiers are the strongest tools on the
        // cycle and a fresh world must not start on one, or the first swing costs from an empty
        // pack and takes nothing.
        var ordered = ToolTiers.Order(
        [
            Tool("overburden:hand_pick", 1.0f),
            Tool("overburden:steel_pick", 3.5f),
            Tool("overburden:quantum_miner", 5.0f, ToolBite.Cube, 50),
            Tool("overburden:quantum_excavator", 5.0f, ToolBite.Chunk, 50),
        ]);

        Assert.Equal("overburden:steel_pick", ToolTiers.Default(ordered).Id.ToString());
    }

    [Fact]
    public void APowerBlockOfNothingIsFreeAndMayBeTheDefault()
    {
        // "Declares a power of nothing" is free by ToolDefinition.IsFree, and the default rule
        // reads that and not the block's presence.
        var ordered = ToolTiers.Order([Tool("overburden:hand_pick", 1.0f), Tool("overburden:idle", 9.0f, ToolBite.Cube, 0)]);
        Assert.Equal("overburden:idle", ToolTiers.Default(ordered).Id.ToString());
    }

    [Fact]
    public void WithNoFreeToolTheDefaultIsTheFirst()
    {
        var ordered = ToolTiers.Order([Tool("overburden:big", 5.0f, ToolBite.Chunk, 50), Tool("overburden:small", 5.0f, ToolBite.Cube, 50)]);
        Assert.Equal("overburden:small", ToolTiers.Default(ordered).Id.ToString());
    }

    [Fact]
    public void AnEmptyCycleHasNoDefault()
    {
        Assert.Throws<ArgumentException>(() => ToolTiers.Default([]));
    }
}
