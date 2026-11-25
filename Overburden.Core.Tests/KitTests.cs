using Overburden.Core.Content;
using Overburden.Core.Inventory;
using Overburden.Core.Persistence;

namespace Overburden.Core.Tests;

/// <summary>
/// IM-6c: kits (IDEAL-MACHINES-PLAN decision 53; IM-6's pass, decision 22) — the strict section, the union and its
/// conflicts, the plan in voxels, and the grant that happens once.
/// </summary>
public class KitTests
{
    /// <summary>The base game's test content, plus a pack and a fluid so the kit's checks have something to refuse.</summary>
    private static InMemoryContentSource Content() => TestContent.BaseGame()
        .Add("overburden", "materials/water.json", """{ "id": "water", "hardness": 0, "fluid": true, "opaque": false, "replaceable": true }""")
        .Add("overburden", "materials/clay.json", """{ "id": "clay", "hardness": 1.0, "textureLayer": 2 }""")
        .Add("overburden", "materials/computer.json", """{ "id": "computer", "placeable": false, "unit": "count", "programmer": true }""")
        .Add("overburden", "materials/cell.json",
            """{ "id": "cell", "unit": "cells", "machine": { "kind": "storage", "capacityJoules": 5000000000, "links": { "charge": { "carries": "power", "role": "sink", "via": "channel", "port": "channel" } }, "ports": { "channel": { "default": 0, "min": 0, "max": 15 } } } }""")
        .Add("overburden", "materials/pack.json",
            """{ "id": "pack", "placeable": false, "unit": "count", "machine": { "kind": "storage", "capacityJoules": 1000, "carried": true, "rechargeWatts": 10, "links": { "charge": { "carries": "power", "role": "sink", "via": "channel", "port": "channel" } }, "ports": { "channel": { "default": 0, "min": 0, "max": 15 } } } }""");

    private static (GameContent Content, LoadResult Result) Load(InMemoryContentSource source)
    {
        var content = new GameContent();
        return (content, content.Load(source));
    }

    private static (GameContent Content, LoadResult Result) LoadKit(string json, string file = "kits/start.json") =>
        Load(Content().Add("overburden", file, json));

    private static string Errors(LoadResult result) => string.Join("; ", result.Errors);

    // ---------------------------------------------------------------- the section

    [Fact]
    public void AKitLoadsItsMaterialsItsRowAndItsPack()
    {
        (GameContent content, LoadResult result) = LoadKit("""
            { "id": "start",
              "materials": { "stone": 10, "cell": 2, "computer": 1 },
              "row": ["stone", "cell", null, null, null, null, null, null, "clay"],
              "pack": { "material": "pack", "channel": 4 } }
            """);

        Assert.True(result.Success, Errors(result));
        Assert.Equal(1, result.KitsLoaded);
        KitDefinition kit = Assert.Single(content.Kits.Kits);
        Assert.Equal(Identifier.Parse("overburden:start"), kit.Id);
        Assert.Equal("overburden/kits/start.json", kit.File);
        Assert.Equal(
            [new KitMaterial(Identifier.Parse("overburden:stone"), 10), new KitMaterial(Identifier.Parse("overburden:cell"), 2), new KitMaterial(Identifier.Parse("overburden:computer"), 1)],
            kit.Materials);
        Assert.Equal(9, kit.Row!.Count);
        Assert.Null(kit.Row[2]);
        Assert.Equal(Identifier.Parse("overburden:clay"), kit.Row[8]);
        Assert.Equal(new KitPack(Identifier.Parse("overburden:pack"), 4), kit.Pack);
    }

    [Fact]
    public void AModWithNoKitsFolderLoadsNoKitsAndSaysNothing()
    {
        (GameContent content, LoadResult result) = Load(Content());

        Assert.True(result.Success, Errors(result));
        Assert.Equal(0, result.KitsLoaded);
        Assert.Empty(content.Kits.Kits);
    }

    [Theory]
    [InlineData("""{ "id": "k", "materials": { "stone": 1 }, "rows": [] }""", "unknown key 'rows' — did you mean 'row'?")]
    [InlineData("""{ "id": "k", "materials": { "stone": 1 }, "pack": { "material": "pack", "chanel": 1 } }""", "unknown key 'pack.chanel'")]
    [InlineData("""{ "materials": { "stone": 1 } }""", "no 'id'")]
    [InlineData("""{ "id": "k", "materials": { "granite": 1 } }""", "overburden:granite is not a material any loaded mod defines")]
    [InlineData("""{ "id": "k", "materials": { "water": 1 } }""", "is a fluid, which the pocket never holds")]
    [InlineData("""{ "id": "k", "materials": { "air": 1 } }""", "air is not something anyone can carry")]
    [InlineData("""{ "id": "k", "materials": { "stone": 0 } }""", "the count must be a whole number from 1")]
    [InlineData("""{ "id": "k", "materials": { "stone": -4 } }""", "the count must be a whole number from 1")]
    [InlineData("""{ "id": "k", "materials": { "stone": 1000000001 } }""", "the count must be a whole number from 1")]
    [InlineData("""{ "id": "k", "materials": { "stone": 1, "overburden:stone": 2 } }""", "is named twice")]
    [InlineData("""{ "id": "k", "row": ["stone", null, null, null, null, null, null, null] }""", "row holds 8 slot(s)")]
    [InlineData("""{ "id": "k", "row": ["stone", "clay", null, null, null, null, null, null, "stone"] }""", "row[8]: overburden:stone is on the row already")]
    [InlineData("""{ "id": "k", "row": ["water", null, null, null, null, null, null, null, null] }""", "row[0]: overburden:water is a fluid")]
    [InlineData("""{ "id": "k", "pack": { "material": "stone" } }""", "is not a pack (a carried storage)")]
    [InlineData("""{ "id": "k", "pack": { "material": "cell" } }""", "is not a pack (a carried storage)")]
    [InlineData("""{ "id": "k", "pack": { "material": "pack", "channel": 16 } }""", "pack.channel: 16 is not a channel")]
    [InlineData("""{ "id": "k", "pack": { "channel": 1 } }""", "pack: no 'material'")]
    [InlineData("""{ "id": "k" }""", "grants nothing")]
    public void AKitWithAFaultIsRefusedWholeNamingTheFileAndTheFault(string json, string expected)
    {
        (GameContent content, LoadResult result) = LoadKit(json);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.StartsWith("overburden/kits/start.json: ", StringComparison.Ordinal) && error.Contains(expected, StringComparison.Ordinal));
        Assert.Empty(content.Kits.Kits);
    }

    [Fact]
    public void AKitNamingAMaterialOnlyTheWorldsTableRemembersIsRefused()
    {
        // A removed mod leaves its material in the world's table as a placeholder, and the registry answers TryGet for
        // it; a kit granting that placeholder would put an unknown block in the pocket. Found writing the battery's
        // recipes: the fresh-load cases never reach a placeholder.
        Identifier[] saved = [MaterialIds.Air, Identifier.Parse("overburden:dirt"), Identifier.Parse("overburden:stone"), Identifier.Parse("gone:granite")];
        var content = new GameContent();

        LoadResult result = content.Load(
            Content().Add("overburden", "kits/start.json", """{ "id": "start", "materials": { "gone:granite": 5 } }"""),
            saved);

        Assert.Contains(result.Errors, error => error.Contains("gone:granite is not a material any loaded mod defines", StringComparison.Ordinal));
        Assert.Empty(content.Kits.Kits);
    }

    // ---------------------------------------------------------------- the union and its conflicts

    private static InMemoryContentSource WithMod(InMemoryContentSource source, string mod, string kit) => source
        .Add(mod, "mod.json", $$"""{ "id": "{{mod}}", "name": "{{mod}}", "version": "1" }""")
        .Add(mod, "kits/extra.json", kit);

    [Fact]
    public void TwoKitsGivingOneMaterialAreSummedAndAKitMayNameAnotherModsMaterial()
    {
        InMemoryContentSource source = WithMod(
            Content().Add("overburden", "kits/start.json", """{ "id": "start", "materials": { "stone": 10, "clay": 3 } }"""),
            "zinc",
            """{ "id": "more", "materials": { "overburden:stone": 5 } }""");

        (GameContent content, LoadResult result) = Load(source);
        Assert.True(result.Success, Errors(result));
        KitGrantPlan plan = KitGrant.Plan(content.Kits, content.Materials);

        int stone = content.Materials.GetNumericId(Identifier.Parse("overburden:stone"));
        int clay = content.Materials.GetNumericId(Identifier.Parse("overburden:clay"));
        Assert.Contains((stone, 15L * 64), plan.Voxels);
        Assert.Contains((clay, 3L * 64), plan.Voxels);
        Assert.Equal([Identifier.Parse("overburden:start"), Identifier.Parse("zinc:more")], plan.Kits);
    }

    [Fact]
    public void TheUnionIsTheSameWhicheverOrderTheModsSort()
    {
        // "aa" sorts before the base game's kit-giving neighbour and "zz" after it; the sums cannot tell.
        KitGrantPlan PlanWith(string mod)
        {
            InMemoryContentSource source = WithMod(
                Content().Add("overburden", "kits/start.json", """{ "id": "start", "materials": { "stone": 10 }, "row": ["stone", null, null, null, null, null, null, null, null] }"""),
                mod,
                """{ "id": "more", "materials": { "overburden:stone": 5, "overburden:clay": 1 } }""");
            (GameContent content, LoadResult result) = Load(source);
            Assert.True(result.Success, Errors(result));
            return KitGrant.Plan(content.Kits, content.Materials);
        }

        KitGrantPlan early = PlanWith("aa");
        KitGrantPlan late = PlanWith("zz");

        Assert.Equal(early.Voxels, late.Voxels);
        Assert.Equal(early.Row, late.Row);
    }

    [Fact]
    public void TheSameKitIdTwiceIsRefusedNamingBothFiles()
    {
        InMemoryContentSource source = Content()
            .Add("overburden", "kits/a.json", """{ "id": "start", "materials": { "stone": 1 } }""")
            .Add("overburden", "kits/b.json", """{ "id": "start", "materials": { "clay": 1 } }""");

        (_, LoadResult result) = Load(source);

        Assert.Contains(result.Errors, error => error.StartsWith("overburden/kits/b.json: ", StringComparison.Ordinal) && error.Contains("given by overburden/kits/a.json already", StringComparison.Ordinal));
    }

    [Fact]
    public void TwoKitsSettingTheRowAreRefusedNamingBothFiles()
    {
        InMemoryContentSource source = WithMod(
            Content().Add("overburden", "kits/start.json", """{ "id": "start", "row": ["stone", null, null, null, null, null, null, null, null] }"""),
            "zinc",
            """{ "id": "mine", "row": ["overburden:clay", null, null, null, null, null, null, null, null] }""");

        (_, LoadResult result) = Load(source);

        Assert.Contains(result.Errors, error => error.StartsWith("zinc/kits/extra.json: ", StringComparison.Ordinal) && error.Contains("sets the row, and so does overburden:start in overburden/kits/start.json", StringComparison.Ordinal));
    }

    [Fact]
    public void TwoKitsPuttingAPackOnAreRefusedNamingBothFiles()
    {
        InMemoryContentSource source = WithMod(
            Content().Add("overburden", "kits/start.json", """{ "id": "start", "pack": { "material": "pack" } }"""),
            "zinc",
            """{ "id": "mine", "pack": { "material": "overburden:pack", "channel": 2 } }""");

        (_, LoadResult result) = Load(source);

        Assert.Contains(result.Errors, error => error.StartsWith("zinc/kits/extra.json: ", StringComparison.Ordinal) && error.Contains("puts a pack on, and so does overburden:start in overburden/kits/start.json", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- the plan, in voxels

    [Fact]
    public void ThePlanCountsInTheHotbarsUnitsAndResolvesTheRowAndThePack()
    {
        (GameContent content, LoadResult result) = LoadKit("""
            { "id": "start", "materials": { "stone": 4096, "cell": 2, "computer": 1 },
              "row": [null, "computer", null, null, null, null, null, null, "cell"],
              "pack": { "material": "pack", "channel": 7 } }
            """);
        Assert.True(result.Success, Errors(result));
        Registry<MaterialDefinition> materials = content.Materials;
        int Id(string path) => materials.GetNumericId(Identifier.Parse($"overburden:{path}"));

        KitGrantPlan plan = KitGrant.Plan(content.Kits, materials);

        // A cubic metre and a body are a cell of 64 voxels; a piece is one.
        (int, long)[] expected = [(Id("stone"), 4096L * 64), (Id("computer"), 1L), (Id("cell"), 128L)];
        Assert.Equal(expected.OrderBy(entry => entry.Item1), plan.Voxels);
        Assert.Equal((4096L * 64) + 128 + 1, plan.TotalVoxels);
        Assert.Equal([HotbarSlots.Empty, Id("computer"), -1, -1, -1, -1, -1, -1, Id("cell")], plan.Row);
        Assert.Equal((Id("pack"), 7), plan.Pack);
    }

    [Fact]
    public void AnEmptyKitTablePlansNothing()
    {
        (GameContent content, _) = Load(Content());

        KitGrantPlan plan = KitGrant.Plan(content.Kits, content.Materials);

        Assert.True(plan.IsEmpty);
        Assert.Equal(0, plan.TotalVoxels);
    }

    // ---------------------------------------------------------------- the grant, once

    private static (GameContent Content, KitTable Kits) StartKit()
    {
        (GameContent content, LoadResult result) = LoadKit("""
            { "id": "start", "materials": { "stone": 100, "cell": 2, "computer": 1 },
              "row": ["computer", "stone", null, null, null, null, null, null, null],
              "pack": { "material": "pack" } }
            """);
        Assert.True(result.Success, Errors(result));
        return (content, content.Kits);
    }

    [Fact]
    public void ACreativeWorldIsGrantedItsKitAtCreationAndTheRowIsTheKitsEvenOverAnItem()
    {
        (GameContent content, KitTable kits) = StartKit();
        var pocket = new VolumetricInventory();
        int computer = content.Materials.GetNumericId(Identifier.Parse("overburden:computer"));
        pocket.KeepOffTheRow([computer]); // an item takes a slot only by a pin — and the kit's row is a set of pins

        WorldFlags flags = KitGrant.GrantIfDue(WorldMode.Creative, WorldFlags.None, kits, content.Materials, pocket, out KitGrantPlan? plan);

        Assert.NotNull(plan);
        Assert.Equal(WorldFlags.KitGranted, flags);
        Assert.Equal(100L * 64, pocket.Voxels(content.Materials.GetNumericId(Identifier.Parse("overburden:stone"))));
        Assert.Equal(1, pocket.Voxels(computer));
        Assert.Equal(computer, pocket.Hotbar.MaterialInSlot(0));
        Assert.Equal(plan.TotalVoxels, pocket.Entries().Sum(entry => entry.Voxels));
    }

    [Fact]
    public void TheKitIsGrantedOnceAndNotAgainOnReopenEvenThroughTheHeaderBytes()
    {
        (GameContent content, KitTable kits) = StartKit();
        var pocket = new VolumetricInventory();

        WorldFlags flags = KitGrant.GrantIfDue(WorldMode.Creative, WorldFlags.None, kits, content.Materials, pocket, out _);
        List<(int, long)> after = [.. pocket.Entries()];

        // The session's write, then the reopen's read: bit 0 rides the header and the second open grants nothing.
        WorldHeader written = new()
        {
            Settings = new Worldgen.WorldGenSettings { Seed = 1 },
            MaterialIdTable = content.Materials.ExportIdTable(),
            Inventory = [.. pocket.Entries().Select(entry => new InventoryEntry(entry.MaterialId, entry.Voxels))],
            Hotbar = pocket.Hotbar.ToArray(),
            Mode = WorldMode.Creative,
            Flags = flags,
        };
        WorldHeader reopened = WorldHeaderBlob.Read(WorldHeaderBlob.Write(written));

        WorldFlags again = KitGrant.GrantIfDue(reopened.Mode, reopened.Flags, kits, content.Materials, pocket, out KitGrantPlan? second);

        Assert.Null(second);
        Assert.Equal(flags, again);
        Assert.Equal(after, pocket.Entries().ToList());
    }

    [Theory]
    [InlineData(WorldMode.Sandbox, WorldFlags.None)]
    [InlineData(WorldMode.Sandbox, WorldFlags.FirstCellGranted)]
    [InlineData(WorldMode.Creative, WorldFlags.KitGranted)]
    [InlineData(WorldMode.Creative, WorldFlags.KitGranted | WorldFlags.FirstCellGranted)]
    public void NoKitIsGrantedToASandboxOrToACreativeWorldThatHasHadOne(WorldMode mode, WorldFlags flags)
    {
        (GameContent content, KitTable kits) = StartKit();
        var pocket = new VolumetricInventory();

        WorldFlags after = KitGrant.GrantIfDue(mode, flags, kits, content.Materials, pocket, out KitGrantPlan? plan);

        Assert.Null(plan);
        Assert.Equal(flags, after);
        Assert.Empty(pocket.Entries());
    }

    [Fact]
    public void ACreativeWorldWithNoKitsIsStillMarkedGrantedSoAKitInstalledLaterNeverArrivesLate()
    {
        // "Granted once at creation": a mod installed after the world was made does not hand its kit out on a reopen.
        (GameContent content, _) = Load(Content());
        var pocket = new VolumetricInventory();

        WorldFlags flags = KitGrant.GrantIfDue(WorldMode.Creative, WorldFlags.None, content.Kits, content.Materials, pocket, out KitGrantPlan? plan);

        Assert.Equal(WorldFlags.KitGranted, flags);
        Assert.True(plan!.IsEmpty);
        Assert.Empty(pocket.Entries());
    }
}
