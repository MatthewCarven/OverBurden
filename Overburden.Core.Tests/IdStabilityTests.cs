using Overburden.Core.Content;

namespace Overburden.Core.Tests;

/// <summary>
/// The tests M1a exists for. Everything else in the content system is bookkeeping; this
/// is the behaviour that decides whether a modded world survives its mods changing.
/// </summary>
public class IdStabilityTests
{
    private static readonly Identifier CopperOre = Identifier.Parse("deepdig:copper_ore");
    private static readonly Identifier Stone = Identifier.Parse("minercraft:stone");

    [Fact]
    public void WithoutASavedTable_RemovingAModRenumbersEverythingAfterIt()
    {
        // This is the failure the world ID table exists to prevent, pinned as a test so
        // the hazard stays visible. "aardvark" sorts before "deepdig", so dropping it
        // shifts every number that came after.
        var withAardvark = new GameContent();
        withAardvark.Load(TestContent.BaseGame().AddAardvark().AddDeepDig());

        var withoutAardvark = new GameContent();
        withoutAardvark.Load(TestContent.BaseGame().AddDeepDig());

        Assert.NotEqual(
            withAardvark.Materials.GetNumericId(CopperOre),
            withoutAardvark.Materials.GetNumericId(CopperOre));
    }

    [Fact]
    public void RemovingAMod_LeavesItsIdsExactlyWhereTheyWere()
    {
        // Session 1: play with DeepDig installed.
        var session1 = new GameContent();
        session1.Load(TestContent.BaseGame().AddDeepDig());

        int copperId = session1.Materials.GetNumericId(CopperOre);
        int stoneId = session1.Materials.GetNumericId(Stone);
        var savedTable = session1.Materials.ExportIdTable().ToList();

        // Session 2: DeepDig uninstalled, world reloaded.
        var session2 = new GameContent();
        session2.Load(TestContent.BaseGame(), savedTable);

        Assert.Equal(copperId, session2.Materials.GetNumericId(CopperOre));
        Assert.Equal(stoneId, session2.Materials.GetNumericId(Stone));

        // Its blocks resolve to an inert placeholder rather than crashing or becoming air.
        Assert.True(session2.Materials.IsPlaceholder(copperId));

        var placeholder = session2.Materials.Get(copperId);
        Assert.True(placeholder.IsUnknown);
        Assert.True(placeholder.IsOpaque);
        Assert.False(placeholder.IsAir);

        // And it still knows what it used to be — this is what makes the next test work.
        Assert.Equal(CopperOre, placeholder.Id);
    }

    [Fact]
    public void ReinstallingAMod_RestoresTheWorldExactly()
    {
        var session1 = new GameContent();
        session1.Load(TestContent.BaseGame().AddDeepDig());
        int copperId = session1.Materials.GetNumericId(CopperOre);

        // Play a session without it...
        var session2 = new GameContent();
        session2.Load(TestContent.BaseGame(), session1.Materials.ExportIdTable().ToList());

        // ...then put it back.
        var session3 = new GameContent();
        session3.Load(TestContent.BaseGame().AddDeepDig(), session2.Materials.ExportIdTable().ToList());

        Assert.Equal(copperId, session3.Materials.GetNumericId(CopperOre));
        Assert.False(session3.Materials.IsPlaceholder(copperId));

        var restored = session3.Materials.Get(copperId);
        Assert.False(restored.IsUnknown);
        Assert.Equal(4.0f, restored.Hardness);
    }

    [Fact]
    public void AddingAMod_DoesNotDisturbExistingIds()
    {
        var before = new GameContent();
        before.Load(TestContent.BaseGame().AddDeepDig());
        var savedTable = before.Materials.ExportIdTable().ToList();

        // "aardvark" sorts first alphabetically, so without the saved table it would
        // claim the low numbers and shift everything.
        var after = new GameContent();
        after.Load(TestContent.BaseGame().AddDeepDig().AddAardvark(), savedTable);

        foreach (var id in savedTable.Where(i => i.IsValid))
        {
            Assert.Equal(
                before.Materials.GetNumericId(id),
                after.Materials.GetNumericId(id));
        }

        // The newcomer is appended past the end of the old table.
        Assert.True(after.Materials.GetNumericId(Identifier.Parse("aardvark:burrow_dirt")) >= savedTable.Count);
    }

    [Fact]
    public void IdsSurviveManyReloadsUnchanged()
    {
        var content = new GameContent();
        content.Load(TestContent.BaseGame().AddDeepDig());
        var table = content.Materials.ExportIdTable().ToList();
        var original = table.ToList();

        for (int i = 0; i < 10; i++)
        {
            var next = new GameContent();

            // Alternate between having the mod installed and not.
            var source = i % 2 == 0 ? TestContent.BaseGame() : TestContent.BaseGame().AddDeepDig();
            next.Load(source, table);

            table = next.Materials.ExportIdTable().ToList();
        }

        Assert.Equal(original, table);
    }

    [Fact]
    public void HolesInASavedTableAreNeverReclaimed()
    {
        // A hole is how an ID belonging to content we can no longer name is preserved.
        // Handing it to a newcomer would silently rewrite every voxel that used it.
        var content = new GameContent();
        content.Load(
            TestContent.BaseGame().AddAardvark(),
            [MaterialIds.Air, default, default, Stone]);

        Assert.Equal(0, content.Materials.GetNumericId(MaterialIds.Air));
        Assert.Equal(3, content.Materials.GetNumericId(Stone));
        Assert.True(content.Materials.GetNumericId(Identifier.Parse("aardvark:burrow_dirt")) > 3);
        Assert.False(content.Materials.GetIdentifier(1).IsValid);
        Assert.False(content.Materials.GetIdentifier(2).IsValid);
    }
}
