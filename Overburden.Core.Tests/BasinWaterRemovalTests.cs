using Overburden.Core.Hydrology;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// Water leaving Tier A through a basin's floor — M8b-2a, the half of the A → B seam that lives in
/// the tree — and the plane a level is measured against.
/// </summary>
/// <remarks>
/// <para>
/// The headline is <c>RemoveThenAddIsTheIdentityOnEverySlab</c>, and the test beside it says which
/// identity does <i>not</i> hold and why: <c>Add(x)</c> then <c>Remove(x)</c> restores the slabs
/// only while the add stayed inside one lake. Water that crossed a ridge into a dry neighbour is
/// the neighbour's, and a hole on this side cannot get it back — the no-siphons rule, which Tier B
/// carries as its own and Tier A has to carry too or the seam would siphon.
/// </para>
/// <para>
/// <b>Heights in a fixture are the topmost solid voxel, as <c>ISurfaceHeights</c> reports them;
/// the field samples the plane above.</b> A ridge written as 5 is a ridge plane at 6, and every
/// level asserted here reads one higher than the grid. That is M8b-2a's convention, and the
/// first-litre test at the bottom is the one that says why it exists.
/// </para>
/// </remarks>
public class BasinWaterRemovalTests
{
    // Litres per voxel of depth over one cell: (cell metres)² × 250, derived rather than stated
    // so the fixture follows the lattice — 4,000 L at the 4 m cell this suite was written at,
    // 1,000 L at the 2 m cell since 2026-09-10.
    private const long PerVoxel =
        (HeightField.DefaultCellVoxels / VoxelConstants.VoxelsPerMetre)
        * (HeightField.DefaultCellVoxels / VoxelConstants.VoxelsPerMetre) * 250L;

    /// <summary>Two equal pits either side of a ridge at 5, inside one rim at 20 — planes 3, 6, 21.</summary>
    private static int[,] TwoPitsOneRidge() =>
        new[,]
        {
            { 20, 20, 20, 20, 20, 20, 20 },
            { 20, 20, 20, 20, 20, 20, 20 },
            { 20, 20, 20, 20, 20, 20, 20 },
            { 20, 20, 2, 5, 2, 20, 20 },
            { 20, 20, 20, 20, 20, 20, 20 },
            { 20, 20, 20, 20, 20, 20, 20 },
            { 20, 20, 20, 20, 20, 20, 20 },
        };

    /// <summary>Three equal pits on one flat shelf at 20, inside a rim at 30 — planes 3, 21, 31.</summary>
    /// <remarks>
    /// Every crossing between the pits is at the shelf, so the merge tree closes its first
    /// composite at the same height as that composite's children, and its own slab can never hold
    /// anything. That is the shape which separates a walk that stops at an empty slab from one
    /// that asks whether the slab could ever hold water — and on integer heights sampled every
    /// 4 m it is the common case rather than a corner.
    /// </remarks>
    private static int[,] ThreePitsOneShelf()
    {
        var samples = new int[9, 9];
        for (int z = 0; z < 9; z++)
        {
            for (int x = 0; x < 9; x++)
            {
                bool border = z == 0 || z == 8 || x == 0 || x == 8;
                samples[z, x] = border ? 30 : 20;
            }
        }

        samples[4, 2] = 2;
        samples[4, 4] = 2;
        samples[4, 6] = 2;
        return samples;
    }

    private static (Watershed Watershed, MergeTree Tree) Analyse(int[,] samples, int outside = 20)
    {
        var watershed = Watershed.Analyse(
            HeightField.Sample(new SampleGrid(samples, outside), 0, 0, pitch: samples.GetLength(0)));
        return (watershed, MergeTree.Build(watershed));
    }

    [Fact]
    public void WaterLeavesOffTheTopOfTheLakeNotOutOfTheLeaf()
    {
        // Both pits full and ten voxels of depth standing above the ridge: one lake. A hole in
        // pit 0's floor lowers that lake, so the shared slab drains and neither pit's own slab
        // moves — taking from the leaf first would empty this pit while the surface stayed where
        // it was, which is the wrong physics and visibly so.
        (_, MergeTree tree) = Analyse(TwoPitsOneRidge());
        int composite = tree.Parent(0);
        var water = new BasinWater(tree);

        water.Add(0, 16 * PerVoxel);
        Assert.Equal(10 * PerVoxel, water.SlabLitres(composite));

        Assert.Equal(4 * PerVoxel, water.Remove(0, 4 * PerVoxel));

        Assert.Equal(6 * PerVoxel, water.SlabLitres(composite));
        Assert.Equal(3 * PerVoxel, water.SlabLitres(0));
        Assert.Equal(3 * PerVoxel, water.SlabLitres(1));

        // Still one lake: twelve voxels of depth over planes 3, 6 and 3 stands at (12 + 12) / 3.
        Assert.Equal(8.0, water.LevelAt(0));
        Assert.Equal(8.0, water.LevelAt(1));
    }

    [Fact]
    public void ASiblingKeepsItsShareOnceTheLevelIsAtTheRidge()
    {
        // Two voxels of depth above the ridge, and a hole asks for six. The shared slab gives its
        // two, this pit gives its three, and the other pit — full, at the ridge, its own lake now —
        // gives nothing. Five come out. Taking from the sibling would deliver all six and drain a
        // pond through a hole on the far side of a ridge, which is a siphon.
        (_, MergeTree tree) = Analyse(TwoPitsOneRidge());
        int composite = tree.Parent(0);
        var water = new BasinWater(tree);

        water.Add(0, 8 * PerVoxel);
        Assert.Equal(2 * PerVoxel, water.SlabLitres(composite));

        Assert.Equal(5 * PerVoxel, water.Remove(0, 6 * PerVoxel));

        Assert.Equal(0, water.SlabLitres(composite));
        Assert.Equal(0, water.SlabLitres(0));
        Assert.Equal(3 * PerVoxel, water.SlabLitres(1));

        // The neighbour stands at the ridge plane; the emptied pit sits on its own floor plane.
        Assert.Equal(6, water.LevelAt(1));
        Assert.Equal(3, water.LevelAt(0));

        // And nothing over there can reach a hole over here.
        Assert.Equal(0, water.Remove(0, PerVoxel));
        Assert.Equal(3 * PerVoxel, water.SlabLitres(1));
    }

    [Fact]
    public void AFlatShelfMergesThreePitsIntoOneLakeAtOneLevel()
    {
        // The defect building Remove found in LevelAt. The first composite here is closed at the
        // shelf, the same height as its children, so its slab holds nothing for ever; the water
        // above the shelf lives in the grandparent's slab. A walk that stops at "the parent's slab
        // is empty" reports the two pits under that composite at the shelf and the third at the
        // true level — one lake at two heights, since M7a-2.
        (_, MergeTree tree) = Analyse(ThreePitsOneShelf(), outside: 30);
        Assert.Equal(3, tree.LeafCount);
        Assert.Contains(
            Enumerable.Range(tree.LeafCount, tree.NodeCount - tree.LeafCount),
            node => tree.OwnCapacity(node) == 0);

        var water = new BasinWater(tree);

        // Eighteen voxels fill each pit to the shelf; ten more stand over all 49 interior cells.
        Assert.Equal(0, water.Add(0, 64 * PerVoxel));

        for (int leaf = 0; leaf < tree.LeafCount; leaf++)
        {
            Assert.Equal(water.LevelAt(0), water.LevelAt(leaf));
            Assert.Equal(18 * PerVoxel, water.SlabLitres(leaf));
        }

        Assert.InRange(water.LevelAt(0), 21 + (10.0 / 49) - 1e-9, 21 + (10.0 / 49) + 1e-9);

        // And a hole in any one pit lowers the whole lake together, off the shelf slab first.
        Assert.Equal(5 * PerVoxel, water.Remove(1, 5 * PerVoxel));
        for (int leaf = 0; leaf < tree.LeafCount; leaf++)
        {
            Assert.Equal(water.LevelAt(0), water.LevelAt(leaf));
            Assert.Equal(18 * PerVoxel, water.SlabLitres(leaf));
        }

        Assert.InRange(water.LevelAt(0), 21 + (5.0 / 49) - 1e-9, 21 + (5.0 / 49) + 1e-9);
    }

    [Fact]
    public void AddThenRemoveIsTheIdentityOnlyInsideOneLake()
    {
        (_, MergeTree tree) = Analyse(TwoPitsOneRidge());

        // Inside one lake — both pits full and the shared slab holding — a pour and a matching
        // removal cancel, slab for slab.
        var lake = new BasinWater(tree);
        lake.Add(0, 8 * PerVoxel);
        long[] before = Slabs(lake);

        Assert.Equal(0, lake.Add(0, 5 * PerVoxel));
        Assert.Equal(5 * PerVoxel, lake.Remove(0, 5 * PerVoxel));
        Assert.Equal(before, Slabs(lake));

        // Across a ridge into a dry neighbour they do not: the pour ran downhill into the other
        // pit, and a hole on this side cannot follow it. The removal is honoured out of this pit,
        // and the neighbour keeps what crossed. M8-PLAN stated this identity unconditionally and
        // this is the arm where it fails, by the physics rather than by a defect.
        var ridge = new BasinWater(tree);
        ridge.Add(0, 3 * PerVoxel);
        ridge.Add(0, 2 * PerVoxel);
        Assert.Equal(2 * PerVoxel, ridge.SlabLitres(1));

        Assert.Equal(2 * PerVoxel, ridge.Remove(0, 2 * PerVoxel));
        Assert.Equal(1 * PerVoxel, ridge.SlabLitres(0));
        Assert.Equal(2 * PerVoxel, ridge.SlabLitres(1));
    }

    [Fact]
    public void RemoveTakesNoMoreThanTheLakeHolds()
    {
        (_, MergeTree tree) = Analyse(TwoPitsOneRidge());
        var water = new BasinWater(tree);

        water.Add(0, 2 * PerVoxel);
        Assert.Equal(2 * PerVoxel, water.Remove(0, 5 * PerVoxel));
        Assert.Equal(0, water.TotalStoredLitres);

        Assert.Equal(0, water.Remove(0, PerVoxel));
        Assert.Equal(0, water.Remove(1, PerVoxel));
        Assert.Throws<ArgumentOutOfRangeException>(() => water.Remove(0, -1));
    }

    [Fact]
    public void RemoveThenAddIsTheIdentityOnEverySlab()
    {
        // THE ORACLE, and the direction it holds in. A removal drains path slabs from the top;
        // every sibling along the walk was full, or the walk would not have climbed past it; so
        // pouring the same litres back into the same basin refills exactly those slabs from the
        // bottom. Every slab and every rolled-up free count, to the litre, over random terrain.
        var random = new Random(80202);
        int reachedASharedSlab = 0;

        for (int trial = 0; trial < 10; trial++)
        {
            var samples = new int[20, 20];
            for (int z = 0; z < 20; z++)
            {
                for (int x = 0; x < 20; x++)
                {
                    samples[z, x] = random.Next(-5, 6);
                }
            }

            (_, MergeTree tree) = Analyse(samples, outside: 6);
            if (tree.LeafCount == 0)
            {
                continue;
            }

            var water = new BasinWater(tree);
            for (int pour = 0; pour < 60; pour++)
            {
                water.Add(random.Next(tree.LeafCount), random.Next(1, 40) * PerVoxel);
            }

            for (int round = 0; round < 100; round++)
            {
                int basin = random.Next(tree.LeafCount);
                long litres = random.Next(1, 40) * PerVoxel;
                long[] slabs = Slabs(water);
                long[] free = Frees(water);

                long removed = water.Remove(basin, litres);
                Assert.InRange(removed, 0, litres);

                for (int node = tree.LeafCount; node < tree.NodeCount; node++)
                {
                    if (water.SlabLitres(node) < slabs[node])
                    {
                        reachedASharedSlab++;
                        break;
                    }
                }

                Assert.Equal(0, water.Add(basin, removed));
                Assert.Equal(slabs, Slabs(water));
                Assert.Equal(free, Frees(water));
            }
        }

        Assert.True(
            reachedASharedSlab > 0,
            "no removal ever reached a shared slab, so the identity was only tested on lone pits");
    }

    [Fact]
    public void AfterAnySequenceOfAddsAndRemovesTheWaterIsAtRest()
    {
        // Three invariants a removal could break and conservation alone cannot see. Volume is
        // exact; the rolled-up free counts match the slabs (skip Refresh and LevelAt solves from a
        // volume the lake no longer holds — the defect M7's battery found in Add); and the state
        // is one a fill could have reached, checked by capturing it per basin and pouring it into
        // a fresh tree, which puts water back where fill-and-spill would rest it. Taking from the
        // leaf first leaves a shared slab standing over a pit with room, which no fill produces.
        //
        // And the ridge rule: a removal from one basin never raises any level, and never lowers a
        // neighbour below the ridge it shares with the path — the neighbour's level after is at
        // least the lesser of its level before and that ridge's plane. Taking from a sibling is
        // invisible to capture-and-pour-back, because the sibling's drained state is one a fill
        // could reach; this is what sees it.
        var random = new Random(51813);
        int reachedASharedSlab = 0;

        for (int trial = 0; trial < 10; trial++)
        {
            var samples = new int[20, 20];
            for (int z = 0; z < 20; z++)
            {
                for (int x = 0; x < 20; x++)
                {
                    samples[z, x] = random.Next(-5, 6);
                }
            }

            (Watershed watershed, MergeTree tree) = Analyse(samples, outside: 6);
            if (tree.LeafCount == 0)
            {
                continue;
            }

            var water = new BasinWater(tree);
            long added = 0;
            long removed = 0;
            long spilled = 0;

            for (int op = 0; op < 300; op++)
            {
                int basin = random.Next(tree.LeafCount);
                long litres = random.Next(1, 40) * PerVoxel;

                if (random.Next(3) == 0)
                {
                    double[] before = Levels(water, tree);
                    long[] slabs = Slabs(water);

                    removed += water.Remove(basin, litres);

                    for (int node = tree.LeafCount; node < tree.NodeCount; node++)
                    {
                        if (water.SlabLitres(node) < slabs[node])
                        {
                            reachedASharedSlab++;
                            break;
                        }
                    }

                    double[] after = Levels(water, tree);
                    for (int other = 0; other < tree.LeafCount; other++)
                    {
                        Assert.True(after[other] <= before[other], "a removal raised a level");

                        if (other == basin)
                        {
                            continue;
                        }

                        int ridge = RidgeBetween(tree, basin, other);
                        if (ridge == MergeTree.None)
                        {
                            Assert.Equal(before[other], after[other]);
                        }
                        else
                        {
                            Assert.True(
                                after[other] >= Math.Min(before[other], tree.SpillHeight(ridge)),
                                "a removal drained a neighbour below the ridge it shares with the path");
                        }
                    }
                }
                else
                {
                    added += litres;
                    spilled += water.Add(basin, litres);
                }

                Assert.Equal(added - removed - spilled, water.TotalStoredLitres);
                for (int node = 0; node < tree.NodeCount; node++)
                {
                    Assert.Equal(tree.Capacity(node) - SumOfSlabs(tree, water, node), water.FreeIn(node));
                }

                if (op % 10 == 9)
                {
                    long[] captured = water.CaptureByBasin();
                    var restored = new BasinWater(tree);
                    for (int leaf = 0; leaf < captured.Length; leaf++)
                    {
                        Assert.Equal(0, restored.Add(leaf, captured[leaf]));
                    }

                    Assert.Equal(Slabs(water), Slabs(restored));
                    for (int leaf = 0; leaf < watershed.Basins.Count; leaf++)
                    {
                        Assert.Equal(water.LevelAt(leaf), restored.LevelAt(leaf));
                    }
                }
            }
        }

        Assert.True(
            reachedASharedSlab > 0,
            "no removal ever reached a shared slab, so the invariants were only tested on lone pits");
    }

    [Fact]
    public void ABasinsFirstLitreStandsAboveTheGroundNotInsideIt()
    {
        // Why the field samples the plane and not the voxel. The pit's topmost solid voxel is 2,
        // occupying [2, 3); water stands on the plane at 3. Sampled at 2, a curve counting depth
        // from there filled a whole slab inside the rock before the level rose above the ground —
        // 4,000 L per sample that drew nothing, and WaterSurface's strictly-above test could not
        // fire until then. Sampled at 3, the first litre is above the ground.
        int[,] samples = TwoPitsOneRidge();
        var surface = new SampleGrid(samples, 20);
        var field = HeightField.Sample(surface, 0, 0, pitch: 7);
        var watershed = Watershed.Analyse(field);
        var water = new BasinWater(MergeTree.Build(watershed));

        Basin pit = watershed.Basins[0];
        int sx = pit.PitX / HeightField.DefaultCellVoxels;
        int sz = pit.PitZ / HeightField.DefaultCellVoxels;
        int solid = surface.SurfaceHeightAt(pit.PitX, pit.PitZ);

        Assert.Equal(2, solid);
        Assert.Equal(solid + 1, field.HeightAt(sx, sz));
        Assert.Equal(solid + 1, pit.PitHeight);

        // Empty, the surface sits on the ground plane, flush with it and drawn nowhere.
        Assert.Equal(field.HeightAt(sx, sz), water.LevelAt(0));

        // One litre, and it is above the ground rather than inside the top voxel.
        water.Add(0, 1);
        Assert.True(water.LevelAt(0) > field.HeightAt(sx, sz));
        Assert.True(water.LevelAt(0) > solid + 1);
    }

    /// <summary>
    /// The child, on <paramref name="other"/>'s side, of the lowest node whose subtree holds both
    /// basins — the rim <paramref name="other"/>'s water would have to stand above to be part of
    /// <paramref name="basin"/>'s lake. <see cref="MergeTree.None"/> when they share no ancestor.
    /// </summary>
    private static int RidgeBetween(MergeTree tree, int basin, int other)
    {
        var ancestors = new HashSet<int>();
        for (int node = basin; ; node = tree.Parent(node))
        {
            ancestors.Add(node);
            if (tree.SpillsOut(node))
            {
                break;
            }
        }

        int child = other;
        while (!ancestors.Contains(child))
        {
            if (tree.SpillsOut(child))
            {
                return MergeTree.None;
            }

            int parent = tree.Parent(child);
            if (ancestors.Contains(parent))
            {
                return child;
            }

            child = parent;
        }

        return MergeTree.None;
    }

    private static long[] Slabs(BasinWater water)
    {
        var slabs = new long[water.Tree.NodeCount];
        for (int node = 0; node < slabs.Length; node++)
        {
            slabs[node] = water.SlabLitres(node);
        }

        return slabs;
    }

    private static long[] Frees(BasinWater water)
    {
        var free = new long[water.Tree.NodeCount];
        for (int node = 0; node < free.Length; node++)
        {
            free[node] = water.FreeIn(node);
        }

        return free;
    }

    private static double[] Levels(BasinWater water, MergeTree tree)
    {
        var levels = new double[tree.LeafCount];
        for (int leaf = 0; leaf < levels.Length; leaf++)
        {
            levels[leaf] = water.LevelAt(leaf);
        }

        return levels;
    }

    private static long SumOfSlabs(MergeTree tree, BasinWater water, int node)
    {
        long total = water.SlabLitres(node);
        if (tree.Left(node) != MergeTree.None)
        {
            total += SumOfSlabs(tree, water, tree.Left(node));
            total += SumOfSlabs(tree, water, tree.Right(node));
        }

        return total;
    }
}
