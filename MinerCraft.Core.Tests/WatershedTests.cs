using MinerCraft.Core.Hydrology;

namespace MinerCraft.Core.Tests;

/// <summary>
/// The watershed is the first thing in this project that cannot be made exact by choosing a big
/// enough neighbourhood, so its tests are invariants and two oracles that share no code with it.
/// </summary>
/// <remarks>
/// <para>
/// The two independent oracles are the point. <c>TheSteppedTank…</c> is arithmetic done on paper,
/// and <c>FloodedHeightsMatchABruteForceMinimax</c> computes the same filled heights by a
/// completely different method — repeated flood fill at rising thresholds — which is far too slow
/// for a real window and exactly right for a twelve-sample one. Everything else here checks the
/// code against itself, and the project's rule is that a green run is not evidence.
/// </para>
/// <para>
/// <b>Heights are stated in world sample space</b>, so a terrain used at two different window
/// origins is literally the same terrain. That is what the identity test needs.
/// </para>
/// </remarks>
public class WatershedTests
{
    private const int Cell = HeightField.DefaultCellVoxels;

    /// <summary>A square pit with a shelf around it — the fixture VolumeCurveTests does by hand.</summary>
    private static int[,] SteppedTank() =>
        new[,]
        {
            { 10, 10, 10, 10, 10, 10, 10 },
            { 10, 10, 10, 10, 10, 10, 10 },
            { 10, 10, 2, 2, 2, 10, 10 },
            { 10, 10, 2, 0, 2, 10, 10 },
            { 10, 10, 2, 2, 2, 10, 10 },
            { 10, 10, 10, 10, 10, 10, 10 },
            { 10, 10, 10, 10, 10, 10, 10 },
        };

    [Fact]
    public void TheSteppedTankIsFoundWhereItIsAndHoldsWhatArithmeticSays()
    {
        var field = HeightField.Sample(new SampleGrid(SteppedTank(), outside: 10), 0, 0, pitch: 7);
        var watershed = Watershed.Analyse(field);

        var basin = Assert.Single(watershed.Basins);
        Assert.Equal(3 * Cell, basin.PitX);
        Assert.Equal(3 * Cell, basin.PitZ);
        Assert.Equal(0, basin.PitHeight);
        Assert.Equal(10, basin.SaddleHeight);
        Assert.Equal(Watershed.Outlet, basin.SpillTo);

        // 74 voxels of depth over nine cells, 4,000 L each — the same number VolumeCurveTests
        // derives on paper from the same shape.
        Assert.Equal(74 * 4000L, basin.CapacityLitres);
    }

    [Fact]
    public void FlatGroundDoesNotBecomeABasin()
    {
        // The corner cells of the shelf have no strictly lower neighbour of their own — their
        // four neighbours are two shelf cells and two rim cells — so a sweep that looks only at
        // cells strictly below itself declares each of them a pit. The terrain then fills with
        // basins that hold nothing, and the real pit loses the catchment they stole.
        var field = HeightField.Sample(new SampleGrid(SteppedTank(), outside: 10), 0, 0, pitch: 7);
        var watershed = Watershed.Analyse(field);

        Assert.Single(watershed.Basins);

        // And a wholly flat world has none at all, rather than one per interior cell.
        var flat = HeightField.Sample(new AnalyticSurface((_, _) => 12), 0, 0, pitch: 9);
        Assert.Empty(Watershed.Analyse(flat).Basins);
    }

    [Fact]
    public void ATiltedPlaneHasNoBasinsAndEveryDropLeaves()
    {
        var field = HeightField.Sample(new AnalyticSurface((x, _) => x / Cell), 0, 0, pitch: 12);
        var watershed = Watershed.Analyse(field);

        Assert.Empty(watershed.Basins);
        foreach (int label in watershed.Labels)
        {
            Assert.Equal(Watershed.Outlet, label);
        }
    }

    [Fact]
    public void TheEdgeIsAnOutletSoABowlOnItDoesNotPond()
    {
        // The same pit, once well inside the window and once straddling its edge. Inside it is a
        // basin; on the edge the water leaves the world, which is the failure direction chosen at
        // M7's planning pass because under-filling is the safe half.
        int[,] samples = new int[7, 7];
        for (int z = 0; z < 7; z++)
        {
            for (int x = 0; x < 7; x++)
            {
                samples[z, x] = 10;
            }
        }

        samples[3, 3] = 0;
        var inside = Watershed.Analyse(
            HeightField.Sample(new SampleGrid(samples, outside: 10), 0, 0, pitch: 7));
        Assert.Single(inside.Basins);

        samples[3, 3] = 10;
        samples[3, 0] = 0;
        var onEdge = Watershed.Analyse(
            HeightField.Sample(new SampleGrid(samples, outside: 10), 0, 0, pitch: 7));
        Assert.Empty(onEdge.Basins);
    }

    [Fact]
    public void OverflowFollowsTheLowestRimAndTheChainRunsOutOfTheWindow()
    {
        // A deep hole rimmed at 4, next to a shallow one that has a low way out to the window edge.
        // The chain is deep -> shallow -> gone, and it is acyclic precisely because the shallow
        // basin has a cheaper exit (3) than the ridge it shares with the deep one (4).
        //
        // A crossing costs the higher of the two samples it joins, so it is symmetric — which is
        // why a pair with no cheaper exit spills into each other instead. See the sibling test.
        int[,] samples =
        {
            { 20, 20, 20, 20, 20, 20, 20 },
            { 20, 20, 20, 20, 20, 20, 20 },
            { 20, 20, 20, 20, 20, 20, 20 },
            { 3, 3, 2, 4, 0, 20, 20 },
            { 20, 20, 20, 20, 20, 20, 20 },
            { 20, 20, 20, 20, 20, 20, 20 },
            { 20, 20, 20, 20, 20, 20, 20 },
        };

        var watershed = Watershed.Analyse(
            HeightField.Sample(new SampleGrid(samples, outside: 20), 0, 0, pitch: 7));

        Assert.Equal(2, watershed.Basins.Count);
        var deep = watershed.Basins.Single(b => b.PitHeight == 0);
        var shallow = watershed.Basins.Single(b => b.PitHeight == 2);

        // The deep hole's only way out is over the ridge at 4, into the shallow basin.
        Assert.Equal(4, deep.SaddleHeight);
        Assert.Equal(shallow.Index, deep.SpillTo);
        Assert.Equal(4 * 4000L, deep.CapacityLitres);

        // The shallow one leaves the window at 3 rather than climbing back over the ridge.
        Assert.Equal(3, shallow.SaddleHeight);
        Assert.Equal(Watershed.Outlet, shallow.SpillTo);
        Assert.Equal(1 * 4000L, shallow.CapacityLitres);
    }

    [Fact]
    public void TwoSiblingBasinsSpillIntoEachOtherAndThatIsWhyTheMergeTreeExists()
    {
        // Two equal pits either side of a low ridge, all inside one high rim. Each one's lowest
        // crossing is the ridge between them, so A spills to B and B spills to A: at leaf level
        // the spill graph HAS a two-cycle, and routing surplus around it would ping-pong forever.
        //
        // This is not a defect to fix here. It is the case a merge tree resolves — above the ridge
        // the two are one lake with a higher rim — and it is the reason M7a-2 exists. Recorded as
        // a test rather than a comment so that building the tree has something to change.
        int[,] samples =
        {
            { 20, 20, 20, 20, 20, 20, 20 },
            { 20, 20, 20, 20, 20, 20, 20 },
            { 20, 20, 20, 20, 20, 20, 20 },
            { 20, 20, 2, 5, 2, 20, 20 },
            { 20, 20, 20, 20, 20, 20, 20 },
            { 20, 20, 20, 20, 20, 20, 20 },
            { 20, 20, 20, 20, 20, 20, 20 },
        };

        var watershed = Watershed.Analyse(
            HeightField.Sample(new SampleGrid(samples, outside: 20), 0, 0, pitch: 7));

        Assert.Equal(2, watershed.Basins.Count);
        var left = watershed.Basins[0];
        var right = watershed.Basins[1];

        Assert.Equal(right.Index, left.SpillTo);
        Assert.Equal(left.Index, right.SpillTo);
        Assert.Equal(5, left.SaddleHeight);
        Assert.Equal(5, right.SaddleHeight);
    }

    [Fact]
    public void TheSamePitsComeBackThroughADifferentWindowOrigin()
    {
        // The test that would have caught keying a basin by array index. The window is centred on
        // the player, so it moves; a pit's world coordinate must not.
        var terrain = new SampleGrid(BowlAt(sampleX: 14, sampleZ: 13, radius: 4, depth: 8, plain: 30), 30);

        var near = Watershed.Analyse(HeightField.Sample(terrain, 0, 0, pitch: 32));
        var shifted = Watershed.Analyse(HeightField.Sample(terrain, 4 * Cell, 3 * Cell, pitch: 32));

        var a = Assert.Single(near.Basins);
        var b = Assert.Single(shifted.Basins);

        Assert.Equal(a.PitX, b.PitX);
        Assert.Equal(a.PitZ, b.PitZ);
        Assert.Equal(a.PitHeight, b.PitHeight);
        Assert.Equal(a.SaddleHeight, b.SaddleHeight);
        Assert.Equal(a.CapacityLitres, b.CapacityLitres);
        Assert.Equal(a.SpillTo, b.SpillTo);
        Assert.Equal(Watershed.Outlet, a.SpillTo);

        // The indices agree here only because each window holds exactly one basin. That is the
        // whole hazard: an index is a coincidence and a pit is a fact.
        Assert.Equal(14 * Cell, a.PitX);
        Assert.Equal(13 * Cell, a.PitZ);
    }

    [Fact]
    public void AConicBowlIsWithinAFewPerCentOfCalculus()
    {
        // An independent oracle with a tolerance rather than an exact one, and the tolerance is
        // discretisation rather than slack: the cone is sampled every 4 m and its depth rounded to
        // whole 0.25 m voxels, so the summed cells cannot equal the integral. Volume of a cone is
        // pi r^2 h / 3, which shares no code with anything here.
        const int depthVoxels = 80;
        const int radiusVoxels = 400;

        var terrain = new AnalyticSurface((x, z) =>
        {
            double centreX = x - (64 * Cell);
            double centreZ = z - (64 * Cell);
            double distance = Math.Sqrt((centreX * centreX) + (centreZ * centreZ));
            if (distance >= radiusVoxels)
            {
                return 0;
            }

            return (int)Math.Round(-depthVoxels + (distance * depthVoxels / radiusVoxels));
        });

        var watershed = Watershed.Analyse(HeightField.Sample(terrain, 0, 0, pitch: 128));
        var bowl = Assert.Single(watershed.Basins);

        const double litresPerCubicVoxel = 15.625;
        double expected = Math.PI * radiusVoxels * radiusVoxels * depthVoxels / 3.0
            * litresPerCubicVoxel;

        Assert.InRange(bowl.CapacityLitres, expected * 0.9, expected * 1.1);
        Assert.Equal(-depthVoxels, bowl.PitHeight);
        Assert.Equal(0, bowl.SaddleHeight);
    }

    [Fact]
    public void FloodedHeightsMatchABruteForceMinimax()
    {
        // The second independent oracle: the same filled heights by repeated flood fill at rising
        // thresholds, which is O(distinct heights x samples) and hopeless on a real window — and
        // completely correct on a twelve-sample one.
        var random = new Random(20260901);
        for (int trial = 0; trial < 20; trial++)
        {
            var samples = new int[12, 12];
            for (int z = 0; z < 12; z++)
            {
                for (int x = 0; x < 12; x++)
                {
                    samples[z, x] = random.Next(0, 6);
                }
            }

            var field = HeightField.Sample(new SampleGrid(samples, outside: 6), 0, 0, pitch: 12);
            var watershed = Watershed.Analyse(field);
            int[] expected = BruteForceFilled(field);

            for (int i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i], watershed.FilledHeights[i]);
            }
        }
    }

    [Fact]
    public void TheInvariantsHoldOnRandomTerrain()
    {
        var random = new Random(6553);
        for (int trial = 0; trial < 25; trial++)
        {
            var samples = new int[24, 24];
            for (int z = 0; z < 24; z++)
            {
                for (int x = 0; x < 24; x++)
                {
                    samples[z, x] = random.Next(-8, 9);
                }
            }

            var field = HeightField.Sample(new SampleGrid(samples, outside: 9), 0, 0, pitch: 24);
            var watershed = Watershed.Analyse(field);

            for (int i = 0; i < field.SampleCount; i++)
            {
                Assert.True(
                    watershed.FilledHeights[i] >= field.Heights[i],
                    "a sample filled below its own ground");

                int label = watershed.Labels[i];
                Assert.InRange(label, Watershed.Outlet, watershed.Basins.Count - 1);
            }

            foreach (var basin in watershed.Basins)
            {
                Assert.True(
                    basin.SaddleHeight > basin.PitHeight,
                    "a basin has to be deeper than the rim it spills over");
                Assert.NotEqual(basin.Index, basin.SpillTo);
                Assert.True(basin.CapacityLitres > 0, "a basin that holds nothing is not a basin");
                Assert.True(basin.CatchmentSamples > 0);
                Assert.Equal(basin.SaddleHeight, basin.Curve.SaddleHeight);
                Assert.Equal(basin.PitHeight, basin.Curve.FloorHeight);

                // The saddle is the lowest crossing out, so nothing cheaper may exist anywhere on
                // the basin's boundary — checked here by looking rather than by trusting the pass
                // that computed it.
                Assert.Equal(basin.SaddleHeight, LowestCrossingOutOf(watershed, basin.Index));
            }
        }
    }

    /// <summary>Sample heights for a conical pit dug into a flat plain.</summary>
    private static int[,] BowlAt(int sampleX, int sampleZ, int radius, int depth, int plain)
    {
        var samples = new int[40, 40];
        for (int z = 0; z < 40; z++)
        {
            for (int x = 0; x < 40; x++)
            {
                double distance = Math.Sqrt(
                    ((x - sampleX) * (x - sampleX)) + ((z - sampleZ) * (z - sampleZ)));
                samples[z, x] = distance >= radius
                    ? plain
                    : plain - (int)Math.Round(depth * (1.0 - (distance / radius)));
            }
        }

        return samples;
    }

    /// <summary>
    /// The filled height of every sample, computed by flooding at rising thresholds. Shares no
    /// code with <see cref="Watershed"/>.
    /// </summary>
    private static int[] BruteForceFilled(HeightField field)
    {
        int pitch = field.Pitch;
        int count = field.SampleCount;
        var filled = new int[count];
        Array.Fill(filled, int.MaxValue);

        var distinct = field.Heights.ToArray().Distinct().Order().ToArray();
        foreach (int threshold in distinct)
        {
            var reached = new bool[count];
            var queue = new Queue<int>();

            for (int sz = 0; sz < pitch; sz++)
            {
                for (int sx = 0; sx < pitch; sx++)
                {
                    bool border = sx == 0 || sz == 0 || sx == pitch - 1 || sz == pitch - 1;
                    int index = (sz * pitch) + sx;
                    if (border && field.Heights[index] <= threshold)
                    {
                        reached[index] = true;
                        queue.Enqueue(index);
                    }
                }
            }

            while (queue.TryDequeue(out int current))
            {
                int cx = current % pitch;
                int cz = current / pitch;
                (int, int)[] steps = [(cx - 1, cz), (cx + 1, cz), (cx, cz - 1), (cx, cz + 1)];

                foreach ((int nx, int nz) in steps)
                {
                    if (nx < 0 || nz < 0 || nx >= pitch || nz >= pitch)
                    {
                        continue;
                    }

                    int neighbour = (nz * pitch) + nx;
                    if (reached[neighbour] || field.Heights[neighbour] > threshold)
                    {
                        continue;
                    }

                    reached[neighbour] = true;
                    queue.Enqueue(neighbour);
                }
            }

            for (int i = 0; i < count; i++)
            {
                if (reached[i] && filled[i] == int.MaxValue)
                {
                    filled[i] = threshold;
                }
            }
        }

        return filled;
    }

    /// <summary>The cheapest crossing from a basin to anything that is not it.</summary>
    private static int LowestCrossingOutOf(Watershed watershed, int basin)
    {
        int pitch = watershed.Field.Pitch;
        int lowest = int.MaxValue;

        for (int sz = 0; sz < pitch; sz++)
        {
            for (int sx = 0; sx < pitch; sx++)
            {
                if (watershed.LabelAt(sx, sz) != basin)
                {
                    continue;
                }

                (int, int)[] steps = [(sx - 1, sz), (sx + 1, sz), (sx, sz - 1), (sx, sz + 1)];
                foreach ((int nx, int nz) in steps)
                {
                    if (nx < 0 || nz < 0 || nx >= pitch || nz >= pitch
                        || watershed.LabelAt(nx, nz) == basin)
                    {
                        continue;
                    }

                    lowest = Math.Min(
                        lowest,
                        Math.Max(
                            watershed.Field.HeightAt(sx, sz), watershed.Field.HeightAt(nx, nz)));
                }
            }
        }

        return lowest;
    }
}
