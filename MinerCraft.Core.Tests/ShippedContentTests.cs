using MinerCraft.Core.Content;
using MinerCraft.Core.Voxels;

namespace MinerCraft.Core.Tests;

/// <summary>
/// The real <c>mods/minercraft/</c> on disk, loaded through the real loader.
/// </summary>
/// <remarks>
/// <para>
/// Every other content test builds an <see cref="InMemoryContentSource"/>, which means the
/// files the game actually ships have never been read by a test. That is the wrong gap to
/// have under non-negotiable 5: the base game is a mod, and "the loader works" is a different
/// claim from "the base game's own files load".
/// </para>
/// <para>
/// It is also the only thing that can catch the shipped content saying something the game
/// depends on and no longer means — a tool tier whose threshold has drifted past stone, or a
/// material whose hardness has been edited to a value that makes it unmineable. Those are
/// data bugs, and no amount of testing the code finds them.
/// </para>
/// </remarks>
public class ShippedContentTests
{
    [Fact]
    public void TheBaseGameLoadsFromDiskWithNoErrors()
    {
        var content = new GameContent();

        LoadResult result = content.Load(new DirectoryContentSource(ModsDirectory()));

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(MaterialIds.AirNumericId, content.Materials.GetNumericId(MaterialIds.Air));
    }

    [Fact]
    public void EveryFileOnDiskIsAccountedForInTheLoadResult()
    {
        // Counts against the directory rather than against a literal, so adding a material or
        // a tier does not need this test edited — but a file that silently fails to register
        // still fails it.
        string mods = ModsDirectory();
        var content = new GameContent();

        LoadResult result = content.Load(new DirectoryContentSource(mods));

        Assert.Equal(Count(mods, "materials"), result.MaterialsLoaded);
        Assert.Equal(Count(mods, "tools"), result.ToolsLoaded);
        Assert.True(result.ToolsLoaded > 0, "the base game must ship something to mine with");

        static int Count(string mods, string kind) =>
            Directory.GetFiles(Path.Combine(mods, Identifier.BaseNamespace, kind), "*.json").Length;
    }

    [Fact]
    public void TheShippedTiersStraddleTheShippedMaterialsSoTheHardnessGateIsVisible()
    {
        // The mechanic is a property of the *content*, not of the code: if every shipped tier
        // could take every shipped material, the hardness gate would be dead weight that
        // nothing demonstrates and nobody would notice regressing. Written against whatever
        // is on disk rather than against remembered numbers, so editing a JSON file to break
        // the mechanic fails here rather than in a playtest.
        var content = new GameContent();
        content.Load(new DirectoryContentSource(ModsDirectory()));

        var tiers = content.Tools.Entries().Select(e => e.Value).ToList();
        var solids = content.Materials.Entries()
            .Select(e => e.Value)
            .Where(m => !m.IsAir)
            .ToList();

        Assert.NotEmpty(tiers);
        Assert.NotEmpty(solids);

        float softest = solids.Min(m => m.Hardness);
        float hardest = solids.Max(m => m.Hardness);

        Assert.True(
            tiers.Any(t => t.HardnessThreshold >= softest && t.HardnessThreshold < hardest),
            "no shipped tier leaves anything standing, so nothing shows what a tool is for");

        Assert.True(
            tiers.Any(t => t.HardnessThreshold >= hardest),
            "no shipped tier can take the hardest material, so the deepest rock is unmineable");
    }

    [Fact]
    public void EveryShippedTierIsUsable()
    {
        // A tier with no reach, or one that cannot take even the softest thing in the world,
        // loads perfectly happily and is simply a tool that does nothing.
        var content = new GameContent();
        content.Load(new DirectoryContentSource(ModsDirectory()));

        float softest = content.Materials.Entries()
            .Select(e => e.Value)
            .Where(m => !m.IsAir)
            .Min(m => m.Hardness);

        foreach (var tier in content.Tools.Entries().Select(e => e.Value))
        {
            Assert.InRange(tier.RadiusMetres, VoxelConstants.VoxelSize, ToolDefinition.MaxRadiusMetres);
            Assert.True(tier.CooldownSeconds > 0f, $"{tier.Id} swings infinitely fast");
            Assert.True(
                tier.HardnessThreshold >= softest,
                $"{tier.Id} cannot take even the softest material in the world");
        }
    }

    /// <summary>
    /// The <c>mods/</c> directory the game runs against, found by walking up to the solution.
    /// </summary>
    /// <remarks>
    /// Fails rather than skips when it cannot be found. A content test that quietly does
    /// nothing outside one particular layout is worse than no content test, because the suite
    /// still reports it as passing.
    /// </remarks>
    private static string ModsDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "MinerCraft.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.True(
            directory is not null,
            $"no MinerCraft.slnx above {AppContext.BaseDirectory}");

        string mods = Path.Combine(directory!.FullName, "MinerCraft.Game", "mods");
        Assert.True(Directory.Exists(mods), $"no mods directory at {mods}");

        return mods;
    }
}
