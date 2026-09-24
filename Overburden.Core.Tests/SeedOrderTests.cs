using Overburden.Core.Fluid;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// M10d-3's first lever: the sheet solve sorts its seeds on a packed key rather than a three-key
/// delegate. The contract is that the order is the comparator's exactly, so a pass is bitwise the
/// pass it was; the fallback past the key's range is the comparator itself.
/// </summary>
public class SeedOrderTests
{
    private const int Cap = FluidConstants.CellCapacityMillilitres;

    [Fact]
    public void ThePackedKeySortsExactlyAsTheComparatorDoesIncludingNegativesAndBothEnds()
    {
        var random = new Random(0x5EED);
        int[] edges = [SeedOrder.Min, SeedOrder.Min + 1, -1, 0, 1, SeedOrder.Max - 1, SeedOrder.Max];
        var positions = new List<VoxelPos>();
        for (int i = 0; i < 20_000; i++)
        {
            // Half from the edges so ties in Y and X are common and the lower fields decide.
            int Pick() => random.Next(2) == 0
                ? edges[random.Next(edges.Length)]
                : random.Next(SeedOrder.Min, SeedOrder.Max + 1);
            positions.Add(new VoxelPos(Pick(), Pick(), Pick()));
        }

        var byComparator = new List<VoxelPos>(positions);
        byComparator.Sort(SeedOrder.Compare);

        long[] keys = positions.Select(SeedOrder.Pack).ToArray();
        Array.Sort(keys);
        var byKey = keys.Select(SeedOrder.Unpack).ToList();

        Assert.Equal(byComparator, byKey);
        Assert.All(keys, k => Assert.True(k >= 0, $"key {k} is negative"));
    }

    [Fact]
    public void APositionRoundTripsThroughItsKey()
    {
        VoxelPos[] cases =
        [
            new(0, 0, 0), new(-1, -1, -1), new(SeedOrder.Min, SeedOrder.Max, 0),
            new(SeedOrder.Max, SeedOrder.Min, SeedOrder.Max), new(123_456, -7, -654_321),
        ];
        foreach (VoxelPos p in cases)
        {
            Assert.True(SeedOrder.Fits(p));
            Assert.Equal(p, SeedOrder.Unpack(SeedOrder.Pack(p)));
        }
    }

    [Theory]
    [InlineData(SeedOrder.Min - 1)]
    [InlineData(SeedOrder.Max + 1)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void ACoordinateOutsideTheRangeDoesNotFitOnAnyAxis(int outside)
    {
        Assert.False(SeedOrder.Fits(new VoxelPos(outside, 0, 0)));
        Assert.False(SeedOrder.Fits(new VoxelPos(0, outside, 0)));
        Assert.False(SeedOrder.Fits(new VoxelPos(0, 0, outside)));
    }

    /// <summary>
    /// A floor at Y 0 that levelling draws below full, with water at Y 1 over three of its cells.
    /// Bottom-up, the floor is levelled first and the water above it loses its support and is not
    /// a sheet; top-down, the upper row would be levelled among itself. So the answer says which
    /// order the pass ran in.
    /// </summary>
    private static (FluidSimulation Sim, List<VoxelPos> Upper) OrderSensitive(int dx)
    {
        var sim = new FluidSimulation(new BoxTerrain(dx, 0, 0, dx + 3, 1, 0));
        int[] floor = [Cap, Cap, Cap, Cap - 6_000];
        for (int x = 0; x < 4; x++)
        {
            sim.Grid.Fill(new VoxelPos(dx + x, 0, 0), floor[x]);
        }

        int[] upper = [3_000, 1_000, 500];
        var upperCells = new List<VoxelPos>();
        for (int x = 0; x < 3; x++)
        {
            upperCells.Add(new VoxelPos(dx + x, 1, 0));
            sim.Grid.Fill(upperCells[x], upper[x]);
        }

        return (sim, upperCells);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2_000_000, 1)] // past SeedOrder.Max: the comparator's pass
    public void SeedsGivenTopDownAreSolvedBottomUpOnEitherPath(int dx, long fallbacks)
    {
        (FluidSimulation sim, List<VoxelPos> upper) = OrderSensitive(dx);
        var seeds = new List<VoxelPos>();
        for (int y = 1; y >= 0; y--)
        {
            for (int x = 3; x >= 0; x--)
            {
                seeds.Add(new VoxelPos(dx + x, y, 0));
            }
        }

        sim.LevelSheets(seeds);

        Assert.Equal(fallbacks, sim.SeedSortFallbacks);
        Assert.Equal([3_000, 1_000, 500], upper.Select(c => sim.Grid.AmountAt(c)));
        for (int x = 0; x < 4; x++)
        {
            Assert.InRange(sim.Grid.AmountAt(new VoxelPos(dx + x, 0, 0)), Cap - 1_500, Cap - 1_500);
        }
    }
}
