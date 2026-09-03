using Overburden.Core.Hydrology;

namespace Overburden.Core.Tests;

/// <summary>
/// The curve is what "solved, not simulated" means, so these are the tests that say it inverts.
/// </summary>
/// <remarks>
/// Litres per voxel of depth is 4,000 throughout — one 4 m cell, one 0.25 m voxel of water, 4 m³ —
/// which is what a <see cref="HeightField"/> with the default cell size hands over. Written as a
/// literal here so the arithmetic in each test can be checked by hand against it.
/// </remarks>
public class VolumeCurveTests
{
    private const long PerVoxel = 4000;

    /// <summary>
    /// A tank with a known floor plan, so the expected volume is arithmetic rather than a second
    /// implementation of the curve.
    /// </summary>
    /// <remarks>
    /// One cell at 0, eight at 2, saddle at 10 — the shape of a square pit with a shelf around it.
    /// It is the strongest oracle in the file because every number below can be derived on paper:
    /// this shares no code with the subject and is not a rounder, differently-arranged copy of it.
    /// </remarks>
    private static VolumeCurve SteppedTank()
    {
        int[] cells = [2, 2, 2, 2, 0, 2, 2, 2, 2];
        return VolumeCurve.Build(cells, saddleHeight: 10, PerVoxel);
    }

    [Fact]
    public void TheSteppedTankMatchesArithmeticDoneByHand()
    {
        var curve = SteppedTank();

        // Below the shelf only the floor cell holds anything.
        Assert.Equal(0, curve.LitresAt(0));
        Assert.Equal(1 * 2 * PerVoxel, curve.LitresAt(2));

        // Above it, one cell four deep and eight cells two deep.
        Assert.Equal(((1 * 4) + (8 * 2)) * PerVoxel, curve.LitresAt(4));

        // And at the saddle: ten voxels over the floor, eight over the shelf.
        Assert.Equal(((1 * 10) + (8 * 8)) * PerVoxel, curve.CapacityLitres);
        Assert.Equal(74 * PerVoxel, curve.CapacityLitres);
    }

    [Fact]
    public void LitresAndLevelInvertEachOther()
    {
        var curve = SteppedTank();

        for (long litres = 0; litres <= curve.CapacityLitres; litres += 1000)
        {
            double level = curve.LevelAt(litres);
            Assert.InRange(curve.LitresAt(level), litres - 1, litres + 1);
        }

        // The tolerance is set by the round trip through integer litres, not by the arithmetic:
        // volume is rounded to the litre, so the level it inverts to can be out by half a litre
        // spread over the covered cells — a quarter of a millimetre with one cell covered, and
        // less with more. Stated rather than tuned until it passes.
        for (double level = 0; level <= 10; level += 0.125)
        {
            long litres = curve.LitresAt(level);
            Assert.InRange(curve.LevelAt(litres), level - 1e-3, level + 1e-3);
        }
    }

    [Fact]
    public void AboveTheSaddleItPinsRatherThanGoingOnFilling()
    {
        var curve = SteppedTank();

        Assert.Equal(10, curve.LevelAt(curve.CapacityLitres));
        Assert.Equal(10, curve.LevelAt(curve.CapacityLitres * 1000));
        Assert.Equal(curve.CapacityLitres, curve.LitresAt(50));
    }

    [Fact]
    public void AnEmptyBasinSitsOnItsFloorAndAnEmptyCurveHoldsNothing()
    {
        var curve = SteppedTank();
        Assert.Equal(0, curve.LevelAt(0));
        Assert.Equal(0, curve.LevelAt(-1));

        var nothing = VolumeCurve.Build([], saddleHeight: 4, PerVoxel);
        Assert.Equal(0, nothing.CapacityLitres);
        Assert.Equal(4, nothing.FloorHeight);
        Assert.Equal(0, nothing.LitresAt(100));
    }

    [Fact]
    public void DuplicateElevationsAreCountedOnceEach()
    {
        // Three cells at exactly the same height is the case an upper bound and a lower bound
        // disagree about, and the volume formula is only allowed to count each cell once.
        int[] cells = [3, 3, 3];
        var curve = VolumeCurve.Build(cells, saddleHeight: 5, PerVoxel);

        Assert.Equal(0, curve.LitresAt(3));
        Assert.Equal(3 * 1 * PerVoxel, curve.LitresAt(4));
        Assert.Equal(3 * 2 * PerVoxel, curve.CapacityLitres);
        Assert.Equal(4, curve.LevelAt(3 * 1 * PerVoxel));
    }

    [Fact]
    public void ACellAtOrAboveTheSaddleIsRefused()
    {
        // Such a cell is never underwater before the basin spills, so including it would report
        // capacity the basin cannot reach — and the error would be invisible in every level below
        // the saddle.
        Assert.Throws<ArgumentException>(() => VolumeCurve.Build([1, 5], saddleHeight: 5, PerVoxel));
        Assert.Throws<ArgumentException>(() => VolumeCurve.Build([1, 9], saddleHeight: 5, PerVoxel));
    }

    [Fact]
    public void TheLevelRisesMonotonicallyWithVolume()
    {
        var curve = SteppedTank();

        double previous = double.NegativeInfinity;
        for (long litres = 0; litres <= curve.CapacityLitres; litres += 500)
        {
            double level = curve.LevelAt(litres);
            Assert.True(level >= previous, $"level fell at {litres} L");
            previous = level;
        }
    }
}
