using Overburden.Core.Structure;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// COLLAPSE-PLAN part a-1: the pace table, the tetracubes and the cutter. The cutter's oracles are
/// booleans over its output — every voxel in exactly one piece, every piece face-connected and at
/// most four minos (so a Tetris shape, which the enumeration pins), no voxel released before the
/// at-risk voxel under it — over constructed shapes and a fuzz of the shapes the support rule makes.
/// </summary>
public class CollapsePieceTests
{
    // ---- The shapes -------------------------------------------------------------------------

    [Fact]
    public void ThereAreTwentyFourRotationsAndTheyAreDistinct()
    {
        (int X, int Y, int Z) probe = (1, 2, 3);
        var images = Polycubes.Rotations.Select(r => r(probe)).ToHashSet();
        Assert.Equal(24, Polycubes.Rotations.Count);
        Assert.Equal(24, images.Count);
    }

    [Fact]
    public void ThereAreEightFreeTetracubesAndEightySixFixedOnes()
    {
        // Eight under rotation: I, O, T, L, S and the three twisted ones (a tripod and two screws,
        // which are mirror images and so distinct). 86 fixed — the count that pins the rotation
        // group, since a group with a reflection in it would fold the screws together.
        Assert.Equal(8, Polycubes.FreeTetracubes.Count);
        Assert.Equal(86, Polycubes.FreeTetracubes.Sum(shape => Polycubes.Orientations(shape).Count));
    }

    [Fact]
    public void AShapeHasOneNameWhateverWayRoundItIs()
    {
        (int X, int Y, int Z)[] el = [(0, 0, 0), (1, 0, 0), (2, 0, 0), (2, 1, 0)];
        string name = Polycubes.Canonical(el);
        foreach ((int X, int Y, int Z)[] orientation in Polycubes.Orientations(el))
        {
            Assert.Equal(name, Polycubes.Canonical(orientation.Select(c => (c.X + 7, c.Y - 3, c.Z + 11)).ToArray()));
        }

        Assert.Equal(24, Polycubes.Orientations(el).Count);
        Assert.Equal(3, Polycubes.Orientations([(0, 0, 0), (1, 0, 0), (2, 0, 0), (3, 0, 0)]).Count);
    }

    // ---- The pace -----------------------------------------------------------------------------

    [Theory]
    [InlineData(0.0, 1.0, 3.0, 1.0, 1)]
    [InlineData(15.0, 1.0, 3.0, 1.0, 1)]
    [InlineData(20.0, 2.0, 6.0, 2.0, 2)]
    [InlineData(25.0, 4.0, 12.0, 4.0, 2)]
    [InlineData(30.0, 8.0, 24.0, 8.0, 4)]
    [InlineData(35.0, 16.0, 48.0, 10.0, 4)]
    [InlineData(40.0, 16.0, 48.0, 10.0, 4)]
    [InlineData(62.0, 16.0, 48.0, 10.0, 4)]
    public void ThePaceIsFlatThenDoublesEveryFiveSecondsUpToItsCap(double age, double multiplier, double releases, double fall, int mino)
    {
        CollapsePace pace = CollapsePace.Default;
        Assert.Equal(multiplier, pace.Multiplier(age), 9);
        Assert.Equal(releases, pace.ReleasesPerSecond(age), 9);
        Assert.Equal(fall, pace.FallMetresPerSecond(age), 9);
        Assert.Equal(mino, pace.MinoVoxels(age));
    }

    [Fact]
    public void TheCapMovesAndIsNeverBelowTheStart()
    {
        var pace = new CollapsePace { MaxMultiplier = 4 };
        Assert.Equal(2.0, pace.Multiplier(20), 9);
        Assert.Equal(4.0, pace.Multiplier(25), 9);
        Assert.Equal(4.0, pace.Multiplier(600), 9);
        Assert.Equal(2, pace.MinoVoxels(600));   // a cap under the metre mino keeps the half-metre one
        Assert.Throws<ArgumentOutOfRangeException>(() => new CollapsePace { MaxMultiplier = 0.5 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new CollapsePace { MaxMultiplier = double.PositiveInfinity });
    }

    [Fact]
    public void TheStartReleasesTwoToFourPiecesASecond()
    {
        CollapsePace pace = CollapsePace.Default;
        Assert.Equal(1.0 / 2.0, pace.ReleaseIntervalSeconds(0, 0f), 9);
        Assert.True(pace.ReleaseIntervalSeconds(0, 0.99999f) > 1.0 / 4.0);
        Assert.Equal(1.0 / 3.0, pace.ReleaseIntervalSeconds(0, 0.5f), 9);
        Assert.Throws<ArgumentOutOfRangeException>(() => pace.ReleaseIntervalSeconds(0, 1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => pace.ReleaseIntervalSeconds(0, float.NaN));
    }

    [Fact]
    public void AStepIsOneMinoAtTheFallsSpeed()
    {
        CollapsePace pace = CollapsePace.Default;
        Assert.Equal(0.25, pace.StepSeconds(0, 1), 9);   // 0.25 m at 1 m/s
        Assert.Equal(0.1, pace.StepSeconds(40, 4), 9);   // 1 m at the 10 m/s cap
        Assert.False(pace.IsSerious(29.9));
        Assert.True(pace.IsSerious(30));
    }

    [Fact]
    public void APaceRefusesANumberThatIsNotOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CollapsePace { DoubleSeconds = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new CollapsePace { GraceSeconds = double.NaN });
        Assert.Throws<ArgumentOutOfRangeException>(() => new CollapsePace { MaxFallMetresPerSecond = double.PositiveInfinity });
    }

    // ---- The cutter ---------------------------------------------------------------------------

    [Fact]
    public void EveryVoxelIsInExactlyOnePieceAndEveryPieceIsATetrisShape()
    {
        HashSet<VoxelPos> slab = Box(new VoxelPos(0, 10, 0), new VoxelPos(15, 10, 15));
        foreach (int mino in new[] { 1, 2, 4 })
        {
            List<CollapsePiece> pieces = PieceCutter.Cut(slab, new VoxelPos(8, 10, 8), mino, 42);
            AssertPartitionAndShapes(slab, pieces, mino);
        }
    }

    [Fact]
    public void NoVoxelLeavesBeforeTheAtRiskVoxelUnderIt()
    {
        // A wall three metres tall hanging over air: the front starts at its top edge, and still
        // the pieces must come from the bottom up in every column.
        HashSet<VoxelPos> wall = Box(new VoxelPos(0, 20, 0), new VoxelPos(11, 31, 1));
        foreach (int mino in new[] { 1, 2, 4 })
        {
            List<CollapsePiece> pieces = PieceCutter.Cut(wall, new VoxelPos(6, 31, 0), mino, 7);
            AssertPartitionAndShapes(wall, pieces, mino);
            AssertBottomUp(wall, pieces);
        }
    }

    [Fact]
    public void OnAFlatSlabTheFrontOnlyMovesOutward()
    {
        HashSet<VoxelPos> slab = Box(new VoxelPos(-20, 4, -20), new VoxelPos(19, 4, 19));
        List<CollapsePiece> pieces = PieceCutter.Cut(slab, new VoxelPos(0, 4, 0), 1, 3);
        Assert.Contains((0, 4, 0), pieces[0].Minos);
        for (int i = 1; i < pieces.Count; i++)
        {
            Assert.True(pieces[i].FrontDistance >= pieces[i - 1].FrontDistance, $"piece {i} went back toward the origin");
        }

        // The corruption spreads: the last piece is near the far corner's distance, 40 minos out
        // (its seed, not its farthest mino, so up to three short).
        Assert.InRange(pieces[^1].FrontDistance, 37, 40);
    }

    [Fact]
    public void MostOfASolidSetFallsAsWholeTetracubes()
    {
        // "Shapes that promote the appearance of Tetris": the greedy leaves fragments where the
        // front strands a mino, and they should be the exception.
        HashSet<VoxelPos> block = Box(new VoxelPos(0, 0, 0), new VoxelPos(15, 3, 15));
        List<CollapsePiece> pieces = PieceCutter.Cut(block, new VoxelPos(8, 0, 8), 1, 11);
        int inTetracubes = pieces.Where(p => p.IsTetracube).Sum(p => p.Voxels.Length);
        Assert.True(inTetracubes >= block.Count * 0.85, $"only {inTetracubes} of {block.Count} voxels fell as whole tetracubes");
    }

    [Fact]
    public void MostTetracubesLieFlat()
    {
        // Flat pieces are the ones that read as Tetris: the growth prefers the seed's own layer.
        // Measured 2026-09-26: 216 of 251 flat with the preference, 0 of 256 with it inverted.
        HashSet<VoxelPos> block = Box(new VoxelPos(0, 0, 0), new VoxelPos(15, 3, 15));
        List<CollapsePiece> pieces = PieceCutter.Cut(block, new VoxelPos(8, 0, 8), 1, 11);
        List<CollapsePiece> whole = pieces.Where(p => p.IsTetracube).ToList();
        int flat = whole.Count(p => p.Minos.All(m => m.Y == p.Minos[0].Y));
        Assert.True(flat >= whole.Count * 0.75, $"{flat} of {whole.Count} tetracubes lie flat");
    }

    [Fact]
    public void TheCutIsAFunctionOfItsInputs()
    {
        HashSet<VoxelPos> block = Box(new VoxelPos(0, 0, 0), new VoxelPos(9, 5, 9));
        List<CollapsePiece> a = PieceCutter.Cut(block, new VoxelPos(4, 0, 4), 1, 99);
        List<CollapsePiece> b = PieceCutter.Cut(new HashSet<VoxelPos>(block.Reverse()), new VoxelPos(4, 0, 4), 1, 99);
        List<CollapsePiece> c = PieceCutter.Cut(block, new VoxelPos(4, 0, 4), 1, 100);
        Assert.Equal(Describe(a), Describe(b));
        Assert.NotEqual(Describe(a), Describe(c));
    }

    [Fact]
    public void AMinoWithAHoleFallsAsThePartThatIsThere()
    {
        HashSet<VoxelPos> cell = Box(new VoxelPos(0, 0, 0), new VoxelPos(3, 3, 3));
        cell.Remove(new VoxelPos(1, 1, 1));
        cell.Remove(new VoxelPos(3, 3, 3));
        List<CollapsePiece> pieces = PieceCutter.Cut(cell, VoxelPos.Zero, 4, 1);
        CollapsePiece only = Assert.Single(pieces);
        Assert.Equal(62, only.Voxels.Length);
        Assert.Single(only.Minos);
    }

    [Fact]
    public void AnEmptySetHasNoPiecesAndABadMinoIsRefused()
    {
        Assert.Empty(PieceCutter.Cut(new HashSet<VoxelPos>(), VoxelPos.Zero, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => PieceCutter.Cut(new HashSet<VoxelPos> { VoxelPos.Zero }, VoxelPos.Zero, 3, 0));
    }

    [Fact]
    public void FuzzedShapesTheRuleMakesCutCleanly()
    {
        // The shapes a settled field marks: column runs over air, face-connected across columns,
        // with overhangs, gaps and negative coordinates — every oracle at every mino size.
        var random = new Random(20260926);
        for (int trial = 0; trial < 120; trial++)
        {
            var set = new HashSet<VoxelPos>();
            int columns = random.Next(1, 90);
            int x = random.Next(-10, 10), z = random.Next(-10, 10);
            for (int c = 0; c < columns; c++)
            {
                x += random.Next(-1, 2);
                z += random.Next(-1, 2);
                int bottom = random.Next(-12, 12);
                int top = bottom + random.Next(0, 9);
                for (int y = bottom; y <= top; y++)
                {
                    set.Add(new VoxelPos(x, y, z));
                }
            }

            VoxelPos origin = set.ElementAt(random.Next(set.Count));
            foreach (int mino in new[] { 1, 2, 4 })
            {
                List<CollapsePiece> pieces = PieceCutter.Cut(set, origin, mino, (ulong)trial);
                AssertPartitionAndShapes(set, pieces, mino);
                AssertBottomUp(set, pieces);
            }
        }
    }

    // ---- Helpers ------------------------------------------------------------------------------

    private static HashSet<VoxelPos> Box(VoxelPos min, VoxelPos max)
    {
        var set = new HashSet<VoxelPos>();
        for (int x = min.X; x <= max.X; x++)
        {
            for (int y = min.Y; y <= max.Y; y++)
            {
                for (int z = min.Z; z <= max.Z; z++)
                {
                    set.Add(new VoxelPos(x, y, z));
                }
            }
        }

        return set;
    }

    private static void AssertPartitionAndShapes(HashSet<VoxelPos> set, List<CollapsePiece> pieces, int mino)
    {
        var seen = new HashSet<VoxelPos>();
        var tetracubes = Polycubes.FreeTetracubes.Select(s => Polycubes.Canonical(s)).ToHashSet();
        int shift = mino == 1 ? 0 : mino == 2 ? 1 : 2;
        for (int i = 0; i < pieces.Count; i++)
        {
            CollapsePiece piece = pieces[i];
            Assert.Equal(i, piece.Order);
            Assert.Equal(mino, piece.MinoVoxels);
            Assert.InRange(piece.Minos.Length, 1, PieceCutter.MaxMinos);
            Assert.True(Polycubes.IsConnected(piece.Minos), $"piece {i} is not face-connected");
            if (piece.IsTetracube)
            {
                Assert.Contains(Polycubes.Canonical(piece.Minos), tetracubes);
            }

            var minos = piece.Minos.ToHashSet();
            foreach (VoxelPos v in piece.Voxels)
            {
                Assert.True(set.Contains(v), $"piece {i} carries {v}, which was not at risk");
                Assert.True(seen.Add(v), $"{v} is in two pieces");
                Assert.Contains((v.X >> shift, v.Y >> shift, v.Z >> shift), minos);
            }

            // No empty mino: every mino named holds a voxel of the piece.
            Assert.Equal(minos.Count, piece.Voxels.Select(v => (v.X >> shift, v.Y >> shift, v.Z >> shift)).Distinct().Count());
        }

        Assert.Equal(set.Count, seen.Count);
    }

    private static void AssertBottomUp(HashSet<VoxelPos> set, List<CollapsePiece> pieces)
    {
        var order = new Dictionary<VoxelPos, int>();
        foreach (CollapsePiece piece in pieces)
        {
            foreach (VoxelPos v in piece.Voxels)
            {
                order[v] = piece.Order;
            }
        }

        foreach (VoxelPos v in set)
        {
            VoxelPos under = v.Offset(0, -1, 0);
            if (order.TryGetValue(under, out int underOrder))
            {
                Assert.True(underOrder <= order[v], $"{v} leaves in piece {order[v]} before {under} under it, in piece {underOrder}");
            }
        }
    }

    private static string Describe(List<CollapsePiece> pieces) =>
        string.Join('|', pieces.Select(p => string.Join(';', p.Minos.Select(m => $"{m.X},{m.Y},{m.Z}"))));
}
