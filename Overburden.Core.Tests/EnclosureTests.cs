using Overburden.Core.Fluid;
using Overburden.Core.Lighting;
using Overburden.Core.Voxels;
using static Overburden.Core.Tests.PhysicsTestHelpers;

namespace Overburden.Core.Tests;

/// <summary>
/// The courtyard's predicate (2026-09-20): the player's material encloses, nature's does not. A
/// flood at one level from the column asked about, stopped only by a placed solid voxel — solid
/// where the seed has air — walked through natural rock, open at the bound or at an unloaded
/// voxel, cached per region and dropped by the pipeline's hooks.
/// </summary>
/// <remarks>
/// The fixture is flat ground at seed 10 (water stands at 11) with a hill to the east whose top
/// is 20; walls are placed at level 11 and above. Every wall is at least one voxel inside a chunk
/// where the tests do not say otherwise, so a rule about chunk columns is exercised by the tests
/// that name it and not by accident.
/// </remarks>
public class EnclosureTests
{
    private const int Ground = 10;
    private const int Level = Ground + 1;
    private const int HillWest = 200;

    /// <summary>Flat at 10 west of x = 200, a hill at 20 from there.</summary>
    private static ISurfaceHeights Seed() => new AnalyticSurface((x, z) => x >= HillWest ? 20 : Ground);

    /// <summary>The world the flood reads: the seed, less what was dug, plus what was placed, inside the loaded set.</summary>
    private sealed class World(ISurfaceHeights seed) : IVoxelWorld
    {
        public HashSet<VoxelPos> Placed { get; } = [];

        public HashSet<VoxelPos> Dug { get; } = [];

        public HashSet<ChunkPos> Loaded { get; } = [];

        public long Reads { get; private set; }

        public bool TryGetMaterial(VoxelPos voxel, out int materialId)
        {
            Reads++;
            if (!Loaded.Contains(voxel.Chunk))
            {
                materialId = Air;
                return false;
            }

            bool solid = Placed.Contains(voxel) || (!Dug.Contains(voxel) && voxel.Y <= seed.SurfaceHeightAt(voxel.X, voxel.Z));
            materialId = solid ? Stone : Air;
            return true;
        }

        /// <summary>Loads every chunk over the box, rows 0 and −1 so the level is inside.</summary>
        public void LoadBox(int cx0, int cz0, int cx1, int cz1)
        {
            for (int cz = cz0; cz <= cz1; cz++)
            for (int cx = cx0; cx <= cx1; cx++)
            {
                Loaded.Add(new ChunkPos(cx, 0, cz));
            }
        }
    }

    private static (World World, Enclosure Enclosure) Fixture()
    {
        ISurfaceHeights seed = Seed();
        var world = new World(seed);
        world.LoadBox(-4, -4, 4, 4);
        var enclosure = new Enclosure(world, Table, seed);
        return (world, enclosure);
    }

    /// <summary>A square ring of placed voxels at one level, inclusive corners, optionally missing one side.</summary>
    private static void Wall(World world, Enclosure enclosure, int x0, int z0, int x1, int z1, int y, string? skip = null)
    {
        for (int x = x0; x <= x1; x++)
        {
            if (skip != "north") Place(world, enclosure, new VoxelPos(x, y, z0));
            if (skip != "south") Place(world, enclosure, new VoxelPos(x, y, z1));
        }

        for (int z = z0; z <= z1; z++)
        {
            if (skip != "west") Place(world, enclosure, new VoxelPos(x0, y, z));
            if (skip != "east") Place(world, enclosure, new VoxelPos(x1, y, z));
        }
    }

    private static void Place(World world, Enclosure enclosure, VoxelPos v)
    {
        world.Placed.Add(v);
        enclosure.VoxelChanged(v);
    }

    private static void Remove(World world, Enclosure enclosure, VoxelPos v)
    {
        world.Placed.Remove(v);
        enclosure.VoxelChanged(v);
    }

    [Fact]
    public void FourWallsOnGrassEncloseAtTheWatersLevelAndThreeDoNot()
    {
        (World world, Enclosure enclosure) = Fixture();
        Wall(world, enclosure, 4, 4, 12, 12, Level);

        Assert.True(enclosure.IsEnclosed(8, 8, Level));
        Assert.Equal(1, enclosure.EnclosedFloods);

        // Outside the walls is open, and the outside flood is the expensive one.
        Assert.False(enclosure.IsEnclosed(20, 8, Level));
        Assert.Equal(2, enclosure.Floods);
        Assert.Equal(1, enclosure.EnclosedFloods);

        // One wall taken down: the region it bounded is dropped by the edit, and the re-ask finds
        // the gap.
        Remove(world, enclosure, new VoxelPos(8, Level, 4));
        Assert.False(enclosure.IsEnclosed(8, 8, Level));
    }

    [Fact]
    public void NaturalRockIsWalkedThroughSoThreeWallsAndAHillsideReadAsOpen()
    {
        // Walls north, south and west, the hill to the east at 20 — solid at level 11, and the
        // seed's, so the flood goes through it and out the far side. The fourth wall is the fix.
        (World world, Enclosure enclosure) = Fixture();
        world.LoadBox(-8, -8, 8, 8);
        Wall(world, enclosure, HillWest - 10, 4, HillWest + 2, 12, Level, skip: "east");

        Assert.False(enclosure.IsEnclosed(HillWest - 6, 8, Level));

        // A fourth wall *inside* the hill is below the seed there and cannot be told from rock —
        // the stated cost — so it closes nothing; one on the grass side of the hill's foot does.
        Wall(world, enclosure, HillWest - 10, 4, HillWest + 2, 12, Level);
        Assert.False(enclosure.IsEnclosed(HillWest - 6, 8, Level));

        for (int z = 4; z <= 12; z++)
        {
            Place(world, enclosure, new VoxelPos(HillWest - 1, Level, z));
        }

        Assert.True(enclosure.IsEnclosed(HillWest - 6, 8, Level));
    }

    [Fact]
    public void AWallStandingOnHigherGroundHoldsTheWaterBelowItsFootingBecauseItClaimsTheGround()
    {
        // The courtyard on a slope: the uphill wall stands on ground at 20, the water inside
        // rests at 11 on the grass. At level 11 the uphill wall's columns are natural rock, and
        // without the footing rule the flood walked through them and out — the headless
        // courtyard on the spawn's slope leaked at every level below its uphill wall. A wall
        // claims the ground under it: the first voxel above its column's seed is solid, so the
        // column is an obstacle at every level below the footing too.
        (World world, Enclosure enclosure) = Fixture();
        world.LoadBox(-8, -8, 8, 8);

        // The ring follows the ground, as a brush ring does: each wall voxel is the first above
        // its own column's seed — on the grass at 11, on the hill at 21.
        ISurfaceHeights seed = Seed();
        for (int x = HillWest - 10; x <= HillWest + 2; x++)
        {
            Place(world, enclosure, new VoxelPos(x, seed.SurfaceHeightAt(x, 4) + 1, 4));
            Place(world, enclosure, new VoxelPos(x, seed.SurfaceHeightAt(x, 12) + 1, 12));
        }

        for (int z = 4; z <= 12; z++)
        {
            Place(world, enclosure, new VoxelPos(HillWest - 10, Level, z));
            Place(world, enclosure, new VoxelPos(HillWest + 2, 21, z));
        }

        Assert.True(enclosure.IsEnclosed(HillWest - 6, 8, Level));

        // The hill's own ground with nothing on it is still walked through: the same ring one
        // column short on the hill side is open.
        Remove(world, enclosure, new VoxelPos(HillWest + 2, 21, 8));
        Assert.False(enclosure.IsEnclosed(HillWest - 6, 8, Level));
    }

    [Fact]
    public void TheAnswerIsPerLevelSoAWallSolidAtTheTopAndOpenBelowEnclosesOnlyAtTheTop()
    {
        // A wall two voxels high with a gap in its lowest row: at the lake's top the column is
        // walled in; at the level below, the flood finds the gap — and so would the water.
        (World world, Enclosure enclosure) = Fixture();
        Wall(world, enclosure, 4, 4, 12, 12, Level);
        Wall(world, enclosure, 4, 4, 12, 12, Level + 1);
        Remove(world, enclosure, new VoxelPos(4, Level, 8));

        Assert.True(enclosure.IsEnclosed(8, 8, Level + 1));
        Assert.False(enclosure.IsEnclosed(8, 8, Level));
    }

    [Fact]
    public void AnEnclosureWiderThanTheBoundReadsAsOpen()
    {
        // Walls 2 × Bound + 8 apart: a flood from the middle reaches the bound before the wall.
        (World world, Enclosure enclosure) = Fixture();
        int half = Enclosure.BoundVoxels + 4;
        world.LoadBox(-8, -8, 8, 8);
        Wall(world, enclosure, -half, -half, half, half, Level);

        Assert.False(enclosure.IsEnclosed(0, 0, Level));

        // And the same walls one voxel inside the bound enclose — the bound is a distance rule,
        // not a size rule; the cells the flood visits are what it costs.
        (World small, Enclosure inside) = Fixture();
        small.LoadBox(-8, -8, 8, 8);
        int within = Enclosure.BoundVoxels - 2;
        Wall(small, inside, -within, -within, within, within, Level);
        Assert.True(inside.IsEnclosed(0, 0, Level));
    }

    [Fact]
    public void AnUnloadedVoxelIsOpenAndTheChunksArrivalDropsTheRegionEvenWithNoEdits()
    {
        // The courtyard straddles chunk column (0, 0) and (1, 0); the walls are in both, the
        // column between them is not loaded. Open until it arrives — and its arrival, with no
        // edits of its own, is what turns the answer: an unedited chunk holds no wall, but it
        // was the one thing the flood could not see past.
        ISurfaceHeights seed = Seed();
        var world = new World(seed);
        world.LoadBox(-2, -2, 2, 2);
        world.Loaded.Remove(new ChunkPos(1, 0, 0));
        var enclosure = new Enclosure(world, Table, seed);
        Wall(world, enclosure, 20, 4, 36, 12, Level);

        Assert.False(enclosure.IsEnclosed(24, 8, Level));
        Assert.Equal(1, enclosure.Regions);

        world.Loaded.Add(new ChunkPos(1, 0, 0));
        enclosure.ChunkArrived(new ChunkPos(1, 0, 0), hasEdits: false);
        Assert.Equal(0, enclosure.Regions);
        Assert.True(enclosure.IsEnclosed(24, 8, Level));
    }

    [Fact]
    public void AnArrivalWithNoEditsLeavesARegionThatNeverReachedItAlone()
    {
        // A chunk arriving beside a closed courtyard that saw nothing of it: nothing to drop, the
        // cache holds, no second flood. The arm every streaming step takes.
        (World world, Enclosure enclosure) = Fixture();
        Wall(world, enclosure, 4, 4, 12, 12, Level);
        Assert.True(enclosure.IsEnclosed(8, 8, Level));

        enclosure.ChunkArrived(new ChunkPos(3, 0, 3), hasEdits: false);
        Assert.Equal(1, enclosure.Regions);
        Assert.True(enclosure.IsEnclosed(8, 8, Level));
        Assert.Equal(1, enclosure.Floods);
        Assert.Equal(1, enclosure.CacheHits);
    }

    [Fact]
    public void WithNoEditedChunkWithinTheBoundTheAnswerIsOpenWithNoFloodAndNoCache()
    {
        // The r6 control's arm: nothing placed anywhere, every column open for nothing.
        (_, Enclosure enclosure) = Fixture();

        Assert.False(enclosure.IsEnclosed(8, 8, Level));
        Assert.False(enclosure.IsEnclosed(9, 8, Level));
        Assert.Equal(0, enclosure.Floods);
        Assert.Equal(0, enclosure.Regions);
        Assert.Equal(2, enclosure.Quiet);
    }

    [Fact]
    public void AnEditedChunkWithinTheBoundIsEnoughToFloodAndOneOutsideItIsNot()
    {
        // The pre-check's reach is the bound in chunks: a wall three chunk columns away cannot
        // be part of this column's enclosure, so its chunk does not make this column flood.
        (World world, Enclosure enclosure) = Fixture();
        world.LoadBox(-8, -8, 8, 8);
        Place(world, enclosure, new VoxelPos(-100, Level, 8));

        // Column −4 holds the wall; column 0 is four away and does not flood, column −3 does.
        Assert.False(enclosure.IsEnclosed(0, 8, Level));
        Assert.Equal(0, enclosure.Floods);

        Assert.False(enclosure.IsEnclosed(-70, 8, Level));
        Assert.Equal(1, enclosure.Floods);
    }

    [Fact]
    public void AStartThatIsNotAirIsNotEnclosed()
    {
        // The wall's own column, or a placed floor: the question is about air. Open, so a block
        // on a lake's bed keeps the band over it and nothing rests on its top inside the lake.
        (World world, Enclosure enclosure) = Fixture();
        Wall(world, enclosure, 4, 4, 12, 12, Level);

        Assert.False(enclosure.IsEnclosed(4, 8, Level));
        Assert.False(enclosure.IsEnclosed(8, 8, Ground));
    }

    [Fact]
    public void AWallInTheNextChunkColumnWithNoCellsThereStillDropsTheRegionWhenItGoes()
    {
        // The courtyard's interior is all in chunk column (0, 0); its east wall is the first
        // voxel of column (1, 0), with no visited cell in that column. Taking that wall down is
        // an edit in (1, 0), and the region must go: walls index the region as cells do.
        (World world, Enclosure enclosure) = Fixture();
        Wall(world, enclosure, 20, 4, 32, 12, Level);
        Assert.True(enclosure.IsEnclosed(26, 8, Level));

        var dropped = new List<(int X, int Z)>();
        enclosure.Changed += (column, _, _) => dropped.Add(column);
        Remove(world, enclosure, new VoxelPos(32, Level, 8));

        Assert.Contains((0, 0), dropped);
        Assert.Contains((1, 0), dropped);
        Assert.False(enclosure.IsEnclosed(26, 8, Level));
    }

    [Fact]
    public void AnEditAtAnotherLevelLeavesTheRegionAlone()
    {
        (World world, Enclosure enclosure) = Fixture();
        Wall(world, enclosure, 4, 4, 12, 12, Level);
        Assert.True(enclosure.IsEnclosed(8, 8, Level));

        Place(world, enclosure, new VoxelPos(8, Level + 3, 8));
        Assert.Equal(1, enclosure.Regions);
        Assert.Equal(0, enclosure.Dropped);
    }

    [Fact]
    public void EveryCellOfAnEnclosedFloodSharesTheAnswerFromTheCache()
    {
        (World world, Enclosure enclosure) = Fixture();
        Wall(world, enclosure, 4, 4, 12, 12, Level);
        Assert.True(enclosure.IsEnclosed(8, 8, Level));

        for (int z = 5; z <= 11; z++)
        for (int x = 5; x <= 11; x++)
        {
            Assert.True(enclosure.IsEnclosed(x, z, Level));
        }

        Assert.Equal(1, enclosure.Floods);
        Assert.Equal(49, enclosure.CacheHits);
        Assert.Equal(49, enclosure.CachedCells);
    }

    [Fact]
    public void TheCacheIsCappedAndEvictsTheOldestRegionsWithoutTellingAnyReader()
    {
        // The 2026-09-21 crash: a region per level the lake's top passed through, never dropped,
        // 5,884 of them at 16k cells. Open floods at distinct levels above one wall until the
        // cap (rows loaded to 7 chunks up, so every level is inside the world) — 4,097 floods,
        // cap is passed: the oldest go, the newest stay, nothing is dropped as a change and the
        // ceiling is never told, because nothing about the world moved.
        (World world, Enclosure enclosure) = Fixture();
        world.LoadBox(-8, -8, 8, 8);
        for (int cy = 1; cy <= (Enclosure.MaxRegions + 8) / 32 + 1; cy++)
        for (int cz = -8; cz <= 8; cz++)
        for (int cx = -8; cx <= 8; cx++)
        {
            world.Loaded.Add(new ChunkPos(cx, cy, cz));
        }

        Place(world, enclosure, new VoxelPos(0, Level, 0));
        var told = new List<(int X, int Z)>();
        enclosure.Changed += (column, _, _) => told.Add(column);

        int levels = 0;
        while (enclosure.Evicted == 0 && levels < Enclosure.MaxRegions + 8)
        {
            Assert.False(enclosure.IsEnclosed(8, 8, Level + levels));
            levels++;
        }

        Assert.True(enclosure.Evicted > 0, "no eviction past the cap");
        Assert.InRange(enclosure.Regions, 1, Enclosure.MaxRegions);
        Assert.Equal(0, enclosure.Dropped);
        Assert.Empty(told);

        // The newest level is still cached, the first is not.
        Assert.False(enclosure.IsEnclosed(8, 8, Level + levels - 1));
        long floods = enclosure.Floods;
        Assert.Equal(floods, enclosure.Floods);
        enclosure.IsEnclosed(8, 8, Level);
        Assert.Equal(floods + 1, enclosure.Floods);
    }

    [Fact]
    public void AForgetDropsTheRegionsInItsColumn()
    {
        (World world, Enclosure enclosure) = Fixture();
        Wall(world, enclosure, 4, 4, 12, 12, Level);
        Assert.True(enclosure.IsEnclosed(8, 8, Level));

        enclosure.ChunkForgotten(new ChunkPos(0, 0, 0));
        Assert.Equal(0, enclosure.Regions);
    }
}
