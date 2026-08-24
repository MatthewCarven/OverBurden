using MinerCraft.Core.Content;

namespace MinerCraft.Core.Tests;

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
            """{ "id": "stone", "hardness": 3.0, "supportDistance": 30, "permeability": 0.02, "textureLayer": 0 }""");

    public static InMemoryContentSource AddDeepDig(this InMemoryContentSource source) => source
        .Add("deepdig", "mod.json",
            """{ "id": "deepdig", "name": "DeepDig", "version": "1.0.0", "dependencies": ["minercraft"] }""")
        .Add("deepdig", "materials/copper_ore.json",
            """{ "id": "copper_ore", "hardness": 4.0, "supportDistance": 25, "textureLayer": 0 }""");

    /// <summary>A mod whose ID sorts before "deepdig", used to show ID drift.</summary>
    public static InMemoryContentSource AddAardvark(this InMemoryContentSource source) => source
        .Add("aardvark", "mod.json",
            """{ "id": "aardvark", "name": "Aardvark", "version": "1.0.0" }""")
        .Add("aardvark", "materials/burrow_dirt.json",
            """{ "id": "burrow_dirt", "hardness": 0.5, "textureLayer": 1 }""");
}
