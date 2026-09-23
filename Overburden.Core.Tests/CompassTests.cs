using Overburden.Core.Navigation;

namespace Overburden.Core.Tests;

public class CompassTests
{
    // The convention, stated as four facts rather than as a formula. If the axes ever move, this
    // is the file that says so -- and a compass that points consistently the wrong way looks
    // exactly like a compass, which is why it is pinned here rather than checked by eye.
    [Theory]
    [InlineData(0.0f, -1.0f, 0.0f, "N")]
    [InlineData(1.0f, 0.0f, 90.0f, "E")]
    [InlineData(0.0f, 1.0f, 180.0f, "S")]
    [InlineData(-1.0f, 0.0f, 270.0f, "W")]
    public void TheFourCardinalDirections(float x, float z, float expected, string point)
    {
        Assert.Equal(expected, Compass.BearingDegrees(x, z), 3);
        Assert.Equal(point, Compass.Point(Compass.BearingDegrees(x, z)));
    }

    [Theory]
    [InlineData(1.0f, -1.0f, 45.0f, "NE")]
    [InlineData(1.0f, 1.0f, 135.0f, "SE")]
    [InlineData(-1.0f, 1.0f, 225.0f, "SW")]
    [InlineData(-1.0f, -1.0f, 315.0f, "NW")]
    public void TheDiagonalsAreHalfwayBetween(float x, float z, float expected, string point)
    {
        Assert.Equal(expected, Compass.BearingDegrees(x, z), 3);
        Assert.Equal(point, Compass.Point(Compass.BearingDegrees(x, z)));
    }

    [Fact]
    public void ABearingIsAlwaysInsideOneTurn()
    {
        // Atan2 returns (-pi, pi], so the negative half has to be folded -- and a readout printing
        // "-090" is a readout nobody can act on.
        for (int degrees = -720; degrees <= 720; degrees += 7)
        {
            float radians = degrees * (MathF.PI / 180.0f);
            float bearing = Compass.BearingDegrees(MathF.Sin(radians), -MathF.Cos(radians));

            Assert.InRange(bearing, 0.0f, 360.0f);
        }
    }

    [Fact]
    public void EveryPointIsReachableAndTheyComeRoundInOrder()
    {
        // Sixteen points over 360 degrees, so each owns 22.5 -- and the rounding has to wrap at
        // the top rather than index a seventeenth. Walking a full turn is what catches both.
        var seen = new List<string>();
        for (float bearing = 0.0f; bearing < 360.0f; bearing += 1.0f)
        {
            string point = Compass.Point(bearing);
            if (seen.Count == 0 || seen[^1] != point)
            {
                seen.Add(point);
            }
        }

        // N at the start and again at the end, because a turn comes back to where it began.
        Assert.Equal("N", seen[0]);
        Assert.Equal("N", seen[^1]);
        Assert.Equal(17, seen.Count);
        Assert.Equal(16, seen.Distinct().Count());
    }

    [Fact]
    public void StraightDownHasNoBearingAndSaysNorth()
    {
        // A player looking at their feet has a forward vector with no horizontal part at all.
        // Something has to be printed; north is the honest arbitrary answer, and NaN is not.
        Assert.Equal(0.0f, Compass.BearingDegrees(0.0f, 0.0f));
        Assert.Equal("N", Compass.Point(Compass.BearingDegrees(0.0f, 0.0f)));
    }
}
