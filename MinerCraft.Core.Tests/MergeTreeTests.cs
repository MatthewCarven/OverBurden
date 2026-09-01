using MinerCraft.Core.Hydrology;

namespace MinerCraft.Core.Tests;

/// <summary>
/// The tree, and the routing that makes overflow terminate.
/// </summary>
/// <remarks>
/// The headline is <c>TheTwoCycleRoutesAndTerminates</c>. Its terrain is the one
/// <c>WatershedTests.TwoSiblingBasinsSpillIntoEachOtherAndThatIsWhyTheMergeTreeExists</c> uses to
/// record that the leaf spill graph cycles; here the same terrain fills, overflows and drains
/// without ever looping. The pair is the point — one test states the hazard and the other states
/// that it is answered, and neither means much on its own.
/// </remarks>
public class MergeTreeTests
{
    private const long PerVoxel = 4000;

    /// <summary>Two equal pits either side of a ridge at 5, inside one rim at 20.</summary>
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

    private static (Watershed Watershed, MergeTree Tree) Analyse(int[,] samples, int outside = 20)
    {
        var watershed = Watershed.Analyse(
            HeightField.Sample(new SampleGrid(samples, outside), 0, 0, pitch: samples.GetLength(0)));
        return (watershed, MergeTree.Build(watershed));
    }

    [Fact]
    public void TheTwoCycleRoutesAndTerminates()
    {
        (Watershed watershed, MergeTree tree) = Analyse(TwoPitsOneRidge());

        // The leaves still spill into each other — that fact has not changed and is not a defect.
        Assert.Equal(2, watershed.Basins.Count);
        Assert.Equal(1, watershed.Basins[0].SpillTo);
        Assert.Equal(0, watershed.Basins[1].SpillTo);

        // What the tree adds is somewhere for the water to go that is not sideways.
        Assert.Equal(3, tree.NodeCount);
        int composite = tree.Parent(0);
        Assert.Equal(composite, tree.Parent(1));
        Assert.Equal(5, tree.SpillHeight(0));
        Assert.Equal(5, tree.SpillHeight(1));
        Assert.Equal(20, tree.SpillHeight(composite));
        Assert.True(tree.SpillsOut(composite));

        // Each pit holds three voxels of depth over one cell; the ground between them, filled from
        // the ridge at 5 up to the rim at 20, is the composite's own slab.
        Assert.Equal(3 * PerVoxel, tree.Capacity(0));
        Assert.Equal(3 * PerVoxel, tree.Capacity(1));
        Assert.Equal(51 * PerVoxel, tree.Capacity(composite));
        Assert.Equal(45 * PerVoxel, tree.OwnCapacity(composite));

        // Pour it all into one pit and it terminates, having filled the other one on the way.
        var water = new BasinWater(tree);
        Assert.Equal(0, water.Add(0, 51 * PerVoxel));
        Assert.Equal(51 * PerVoxel, water.TotalStoredLitres);
        Assert.Equal(3 * PerVoxel, water.SlabLitres(1));

        // And past the rim it leaves the world rather than going round again.
        Assert.Equal(7 * PerVoxel, water.Add(0, 7 * PerVoxel));
    }

    [Fact]
    public void ASiblingFillsBeforeTheGroundBetweenThemDoes()
    {
        (_, MergeTree tree) = Analyse(TwoPitsOneRidge());
        int composite = tree.Parent(0);
        var water = new BasinWater(tree);

        // Exactly enough for the first pit: it is brim-full at the ridge and the other is dry.
        water.Add(0, 3 * PerVoxel);
        Assert.Equal(5, water.LevelAt(0));
        Assert.Equal(2, water.LevelAt(1));
        Assert.Equal(0, water.SlabLitres(composite));

        // The next drop crosses the ridge and runs downhill rather than standing on it.
        water.Add(0, 1 * PerVoxel);
        Assert.Equal(0, water.SlabLitres(composite));
        Assert.Equal(1 * PerVoxel, water.SlabLitres(1));
        Assert.Equal(5, water.LevelAt(0));
        Assert.InRange(water.LevelAt(1), 2.9, 3.1);

        // Only once both are brim-full does the surface rise into the ground between them — and
        // then it is one lake, so both basins report the same level.
        water.Add(0, 2 * PerVoxel);
        Assert.Equal(5, water.LevelAt(0));
        Assert.Equal(5, water.LevelAt(1));

        water.Add(0, 16 * PerVoxel);
        Assert.Equal(water.LevelAt(0), water.LevelAt(1));

        // Three cells under water — the two pits at 2 and the ridge at 5 — so 22 voxels of depth
        // stands at (22 + 9) / 3 = 10.33.
        Assert.InRange(water.LevelAt(0), 10.3, 10.4);
    }

    [Fact]
    public void ALeafsSpillHeightIsTheSaddleTheWatershedAlreadyFound()
    {
        // Two definitions of one number would drift, and the symptom would be a lake that stops
        // filling at a height nothing on screen explains. Same argument as IsCaveAt against
        // GenerateTerrain.
        var random = new Random(41);
        for (int trial = 0; trial < 10; trial++)
        {
            var samples = new int[20, 20];
            for (int z = 0; z < 20; z++)
            {
                for (int x = 0; x < 20; x++)
                {
                    samples[z, x] = random.Next(-6, 7);
                }
            }

            (Watershed watershed, MergeTree tree) = Analyse(samples, outside: 7);
            for (int leaf = 0; leaf < tree.LeafCount; leaf++)
            {
                Assert.Equal(watershed.Basins[leaf].SaddleHeight, tree.SpillHeight(leaf));
                Assert.Equal(watershed.Basins[leaf].CapacityLitres, tree.Capacity(leaf));
            }
        }
    }

    [Fact]
    public void TheTreeIsWellFormedOnRandomTerrain()
    {
        var random = new Random(9001);
        for (int trial = 0; trial < 15; trial++)
        {
            var samples = new int[24, 24];
            for (int z = 0; z < 24; z++)
            {
                for (int x = 0; x < 24; x++)
                {
                    samples[z, x] = random.Next(-8, 9);
                }
            }

            (_, MergeTree tree) = Analyse(samples, outside: 9);

            for (int node = 0; node < tree.NodeCount; node++)
            {
                Assert.True(tree.OwnCapacity(node) >= 0, "a composite held less than its children");

                if (tree.Left(node) != MergeTree.None)
                {
                    foreach (int child in new[] { tree.Left(node), tree.Right(node) })
                    {
                        Assert.Equal(node, tree.Parent(child));
                        Assert.True(
                            tree.SpillHeight(child) <= tree.SpillHeight(node),
                            "a child spilled higher than the lake that contains it");
                        Assert.True(tree.Capacity(child) <= tree.Capacity(node));
                    }

                    Assert.Equal(
                        tree.Capacity(node),
                        tree.OwnCapacity(node)
                            + tree.Capacity(tree.Left(node))
                            + tree.Capacity(tree.Right(node)));
                }

                // Climbing terminates, which is the property the whole part exists for.
                int steps = 0;
                for (int walk = node; !tree.SpillsOut(walk); walk = tree.Parent(walk))
                {
                    Assert.True(++steps <= tree.NodeCount, "the climb did not terminate");
                }
            }
        }
    }

    [Fact]
    public void EveryLitreIsEitherHeldOrGone()
    {
        // Conservation, exactly, with no tolerance — volumes are integers and so is the arithmetic.
        // It is the same assertion M7b's accumulator will need against rainfall.
        var random = new Random(1234);
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
            long added = 0;
            long spilled = 0;
            for (int pour = 0; pour < 200; pour++)
            {
                long litres = random.Next(1, 40) * PerVoxel;
                added += litres;
                spilled += water.Add(random.Next(tree.LeafCount), litres);
            }

            Assert.Equal(added, water.TotalStoredLitres + spilled);

            // And a subtree's stored litres are the sum of its nodes' slabs, which is what makes
            // "how much is in this lake" one subtraction rather than a walk.
            for (int node = 0; node < tree.NodeCount; node++)
            {
                Assert.Equal(SumOfSlabs(tree, water, node), water.StoredIn(node));
            }
        }
    }

    [Fact]
    public void FreeCapacityIsTheRoomLeftAnywhereInTheSubtree()
    {
        // EveryLitreIsEitherHeldOrGone pours until the landscape drowns, and a drowned tree has
        // every node either full or empty — under which a rolled-up free count and a per-node one
        // agree exactly. So that test cannot see the difference, and this one pours a trickle and
        // checks while the water is still going in.
        var random = new Random(77);
        var samples = new int[20, 20];
        for (int z = 0; z < 20; z++)
        {
            for (int x = 0; x < 20; x++)
            {
                samples[z, x] = random.Next(-5, 6);
            }
        }

        (_, MergeTree tree) = Analyse(samples, outside: 6);
        var water = new BasinWater(tree);

        for (int pour = 0; pour < 40; pour++)
        {
            water.Add(random.Next(tree.LeafCount), PerVoxel);

            bool sawPartiallyFilled = false;
            for (int node = 0; node < tree.NodeCount; node++)
            {
                long stored = SumOfSlabs(tree, water, node);
                Assert.Equal(tree.Capacity(node) - stored, water.FreeIn(node));
                sawPartiallyFilled |= stored > 0 && stored < tree.Capacity(node);
            }

            Assert.True(sawPartiallyFilled, "the fixture drowned before it could test anything");
        }
    }

    [Fact]
    public void WaterCannotBeAddedBackwards()
    {
        (_, MergeTree tree) = Analyse(TwoPitsOneRidge());
        var water = new BasinWater(tree);
        Assert.Throws<ArgumentOutOfRangeException>(() => water.Add(0, -1));
    }

    [Fact]
    public void ATerrainWithNoBasinsMakesAnEmptyTree()
    {
        var field = HeightField.Sample(
            new AnalyticSurface((x, _) => x / HeightField.DefaultCellVoxels), 0, 0, pitch: 10);
        var tree = MergeTree.Build(Watershed.Analyse(field));

        Assert.Equal(0, tree.LeafCount);
        Assert.Equal(0, tree.NodeCount);
        Assert.Equal(0, new BasinWater(tree).TotalStoredLitres);
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
