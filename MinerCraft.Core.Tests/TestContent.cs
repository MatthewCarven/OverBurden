using MinerCraft.Core.Content;

namespace MinerCraft.Core.Tests;

/// <summary>
/// Small, stable content fixtures for loader and registry tests.
/// </summary>
/// <remarks>
/// <b>Deliberately not a mirror of <c>MinerCraft.Game/mods/</c>.</b> These exist to be
/// minimal and to keep their numeric ID assignment stable, which is what
/// <see cref="IdStabilityTests"/> depends on; the shipped content has drifted from them
/// already and should be free to. What the game actually ships is loaded from disk and
/// checked by <see cref="ShippedContentTests"/> instead.
/// </remarks>
internal static class TestContent
{
    public static InMemoryContentSource BaseGame() => new InMemoryContentSource()
        .Add("minercraft", "mod.json",
            """{ "id": "minercraft", "name": "MinerCraft", "version": "0.1.0" }""")
        .Add("minercraft", "materials/air.json",
            """{ "id": "air", "hardness": 0, "permeability": 1.0, "opaque": false, "replaceable": true }""")
        .Add("minercraft", "materials/dirt.json",
            """{ "id": "dirt", "hardness": 0.8, "supportDistance": 8, "permeability": 0.35, "textureLayer": 1 }""")
        .Add("minercraft", "materials/stone.json",
            """{ "id": "stone", "hardness": 3.0, "supportDistance": 30, "permeability": 0.02, "textureLayer": 0 }""")

        // Two tiers, because one tier cannot demonstrate the mechanic: the hand pick's
        // threshold sits between dirt and stone, so it carves a dirt bank out from around a
        // stone seam and leaves the seam standing.
        .Add("minercraft", "tools/hand_pick.json",
            """{ "id": "hand_pick", "radiusMetres": 0.4, "hardnessThreshold": 1.0, "cooldownSeconds": 0.3 }""")
        .Add("minercraft", "tools/steel_pick.json",
            """{ "id": "steel_pick", "radiusMetres": 0.5, "hardnessThreshold": 3.5, "cooldownSeconds": 0.2 }""");

    public static InMemoryContentSource AddDeepDig(this InMemoryContentSource source) => source
        .Add("deepdig", "mod.json",
            """{ "id": "deepdig", "name": "DeepDig", "version": "1.0.0", "dependencies": ["minercraft"] }""")
        .Add("deepdig", "materials/copper_ore.json",
            """{ "id": "copper_ore", "hardness": 4.0, "supportDistance": 25, "textureLayer": 0 }""")
        .Add("deepdig", "tools/diamond_drill.json",
            """{ "id": "diamond_drill", "radiusMetres": 0.75, "hardnessThreshold": 6.0 }""");

    /// <summary>A mod whose ID sorts before "deepdig", used to show ID drift.</summary>
    public static InMemoryContentSource AddAardvark(this InMemoryContentSource source) => source
        .Add("aardvark", "mod.json",
            """{ "id": "aardvark", "name": "Aardvark", "version": "1.0.0" }""")
        .Add("aardvark", "materials/burrow_dirt.json",
            """{ "id": "burrow_dirt", "hardness": 0.5, "textureLayer": 1 }""");
}
