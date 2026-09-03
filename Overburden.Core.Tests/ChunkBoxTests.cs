using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

public class ChunkBoxTests
{
    [Fact]
    public void AroundIsInclusiveOfTheRadius()
    {
        ChunkBox box = ChunkBox.Around(ChunkPos.Zero, horizontalRadius: 1, verticalRadius: 1);

        Assert.Equal(new ChunkPos(-1, -1, -1), box.Min);
        Assert.Equal(new ChunkPos(2, 2, 2), box.Max);
        Assert.Equal(27, box.Volume);
        Assert.True(box.Contains(new ChunkPos(1, 1, 1)));
        Assert.False(box.Contains(new ChunkPos(2, 0, 0)));
    }

    [Fact]
    public void ZeroRadiusIsASingleChunk()
    {
        ChunkBox box = ChunkBox.Around(new ChunkPos(4, -2, 7), 0, 0);

        Assert.Equal(1, box.Volume);
        Assert.True(box.Contains(new ChunkPos(4, -2, 7)));
    }

    /// <summary>
    /// The vertical extent is its own radius, not a ratio of the horizontal one. A mining
    /// game wants a tall narrow column; collapsing both into one "view distance" is the
    /// change that later reaches every call site.
    /// </summary>
    [Fact]
    public void VerticalRadiusIsIndependentOfHorizontal()
    {
        ChunkBox box = ChunkBox.Around(ChunkPos.Zero, horizontalRadius: 2, verticalRadius: 5);

        Assert.Equal(5, box.SizeX);
        Assert.Equal(11, box.SizeY);
        Assert.Equal(5, box.SizeZ);
    }

    [Fact]
    public void ADegenerateAxisEmptiesTheWholeBox()
    {
        ChunkBox box = ChunkBox.FromBounds(0, 0, 0, 10, 0, 10);

        Assert.True(box.IsEmpty);
        Assert.Equal(0, box.Volume);
        Assert.NotEqual(ChunkBox.Empty, box);
    }

    [Fact]
    public void ExpandGrowsEveryAxisAndLeavesEmptyBoxesAlone()
    {
        ChunkBox box = ChunkBox.FromBounds(0, 0, 0, 2, 2, 2).Expand(2);

        Assert.Equal(new ChunkPos(-2, -2, -2), box.Min);
        Assert.Equal(new ChunkPos(4, 4, 4), box.Max);
        Assert.True(ChunkBox.Empty.Expand(3).IsEmpty);
    }

    [Fact]
    public void IntersectionOfDisjointBoxesIsEmpty()
    {
        ChunkBox a = ChunkBox.FromBounds(0, 0, 0, 4, 4, 4);
        ChunkBox b = ChunkBox.FromBounds(9, 0, 0, 12, 4, 4);

        Assert.True(a.Intersect(b).IsEmpty);
    }

    [Fact]
    public void DifferenceWithADisjointBoxIsTheWholeBox()
    {
        ChunkBox a = ChunkBox.FromBounds(0, 0, 0, 4, 4, 4);
        ChunkBox b = ChunkBox.FromBounds(100, 100, 100, 104, 104, 104);

        List<ChunkBox> parts = PartsOf(a, b);

        Assert.Single(parts);
        Assert.Equal(a, parts[0]);
    }

    [Fact]
    public void DifferenceWithACoveringBoxIsNothing()
    {
        ChunkBox a = ChunkBox.FromBounds(0, 0, 0, 4, 4, 4);
        ChunkBox b = ChunkBox.FromBounds(-5, -5, -5, 9, 9, 9);

        Assert.Empty(PartsOf(a, b));
        Assert.Empty(PartsOf(a, a));
    }

    /// <summary>
    /// The box-difference claim in one assertion: stepping a view box one chunk sideways
    /// touches a single face, not the volume. This is the O(R²)-not-O(R³) property that the
    /// whole decomposition exists for.
    /// </summary>
    [Fact]
    public void SteppingOneChunkTouchesOnlyTheTwoFaces()
    {
        const int radius = 8;
        const int side = 2 * radius + 1;

        ChunkBox before = ChunkBox.Around(ChunkPos.Zero, radius, radius);
        ChunkBox after = ChunkBox.Around(new ChunkPos(1, 0, 0), radius, radius);

        List<ChunkPos> entered = PositionsOf(after, before);
        List<ChunkPos> left = PositionsOf(before, after);

        Assert.Equal(side * side, entered.Count);
        Assert.Equal(side * side, left.Count);

        // Every chunk that entered is on the leading face; every one that left is on the
        // trailing face.
        Assert.All(entered, p => Assert.Equal(radius + 1, p.X));
        Assert.All(left, p => Assert.Equal(-radius, p.X));

        // And the volume that was spared is the overwhelming majority of the box.
        Assert.Equal(side * side * side, before.Volume);
        Assert.True(entered.Count * 10 < before.Volume);
    }

    [Fact]
    public void DifferenceNeverEmitsMoreThanSixBoxes()
    {
        var random = new Random(20260827);

        for (int trial = 0; trial < 500; trial++)
        {
            ChunkBox a = RandomBox(random);
            ChunkBox b = RandomBox(random);

            Assert.True(PartsOf(a, b).Count <= 6);
        }
    }

    /// <summary>
    /// The property that makes the decomposition trustworthy, checked against brute force:
    /// the emitted sub-boxes are pairwise disjoint, and their union is exactly the set
    /// difference. Disjointness is the half that matters for streaming — a position emitted
    /// twice is a refcount incremented twice and a chunk that never unloads.
    /// </summary>
    [Fact]
    public void SubBoxesArePairwiseDisjointAndCoverTheDifferenceExactly()
    {
        var random = new Random(31337);

        for (int trial = 0; trial < 2_000; trial++)
        {
            ChunkBox a = RandomBox(random);
            ChunkBox b = RandomBox(random);

            var emitted = new List<ChunkPos>();
            a.ForEachDifference(b, emitted.Add);

            // Disjoint: nothing is visited twice.
            Assert.Equal(emitted.Count, emitted.Distinct().Count());

            // Exact: brute force over every position in `a`.
            var expected = new HashSet<ChunkPos>();
            a.ForEach(p =>
            {
                if (!b.Contains(p))
                {
                    expected.Add(p);
                }
            });

            Assert.Equal(expected, emitted.ToHashSet());
        }
    }

    /// <summary>
    /// Streaming's real use: view the entered shell, unview the departed one, and expect
    /// the refcounts to land exactly on the new box. Doing it with two differences must
    /// give the same answer as recomputing the set from scratch.
    /// </summary>
    [Fact]
    public void ViewingAndUnviewingShellsTracksTheBoxExactly()
    {
        var random = new Random(99);
        var counts = new Dictionary<ChunkPos, int>();

        ChunkBox current = ChunkBox.Around(ChunkPos.Zero, 3, 2);
        current.ForEach(p => counts[p] = counts.GetValueOrDefault(p) + 1);

        for (int step = 0; step < 300; step++)
        {
            var moved = new ChunkPos(
                random.Next(-6, 7), random.Next(-6, 7), random.Next(-6, 7));
            ChunkBox next = ChunkBox.Around(moved, 3, 2);

            next.ForEachDifference(current, p => counts[p] = counts.GetValueOrDefault(p) + 1);
            current.ForEachDifference(next, p =>
            {
                counts[p] = counts[p] - 1;
                if (counts[p] == 0)
                {
                    counts.Remove(p);
                }
            });

            current = next;

            var expected = new HashSet<ChunkPos>();
            current.ForEach(p => expected.Add(p));

            Assert.Equal(expected, counts.Keys.ToHashSet());
            Assert.All(counts.Values, c => Assert.Equal(1, c));
        }
    }

    [Fact]
    public void ForEachVisitsEveryChunkOnceInStorageOrder()
    {
        ChunkBox box = ChunkBox.FromBounds(0, 0, 0, 2, 2, 2);
        var visited = new List<ChunkPos>();
        box.ForEach(visited.Add);

        Assert.Equal(8, visited.Count);
        Assert.Equal(8, visited.Distinct().Count());

        // Z-major, then X, then Y contiguous — the same order as chunk storage.
        Assert.Equal(new ChunkPos(0, 0, 0), visited[0]);
        Assert.Equal(new ChunkPos(0, 1, 0), visited[1]);
        Assert.Equal(new ChunkPos(1, 0, 0), visited[2]);
    }

    private static List<ChunkBox> PartsOf(ChunkBox a, ChunkBox b)
    {
        var parts = new List<ChunkBox>();
        a.Difference(b, parts.Add);
        return parts;
    }

    private static List<ChunkPos> PositionsOf(ChunkBox a, ChunkBox b)
    {
        var positions = new List<ChunkPos>();
        a.ForEachDifference(b, positions.Add);
        return positions;
    }

    /// <summary>
    /// Small and overlapping on purpose: a range of 0..5 over a span of 0..4 makes total
    /// containment, partial overlap and complete separation all common, which is where the
    /// slicing arithmetic goes wrong.
    /// </summary>
    private static ChunkBox RandomBox(Random random)
    {
        int minX = random.Next(0, 6);
        int minY = random.Next(0, 6);
        int minZ = random.Next(0, 6);

        return ChunkBox.FromBounds(
            minX, minY, minZ,
            minX + random.Next(0, 5),
            minY + random.Next(0, 5),
            minZ + random.Next(0, 5));
    }
}
