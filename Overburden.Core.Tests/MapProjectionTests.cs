using Overburden.Core.Navigation;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

public class MapProjectionTests
{
    private static MapProjection At(float bearing, int centreX = 0, int centreZ = 0) => new(
        CentreVoxelX: centreX,
        CentreVoxelZ: centreZ,
        VoxelsPerPixel: MapProjection.DefaultVoxelsPerPixel,
        Size: MapProjection.DefaultSize,
        BearingDegrees: bearing);

    [Fact]
    public void TheCentrePixelIsWhereThePlayerIsStanding()
    {
        // The property a marker is drawn on and the one everything else is measured from. True at
        // every bearing, because rotating about the centre leaves the centre alone.
        foreach (float bearing in new[] { 0.0f, 37.0f, 90.0f, 180.0f, 271.5f })
        {
            MapProjection map = At(bearing, centreX: 4_000, centreZ: -2_500);
            (float x, float z) = map.WorldAt(map.CentrePixel, map.CentrePixel);

            Assert.Equal(4_000.0f, x, 2);
            Assert.Equal(-2_500.0f, z, 2);
        }
    }

    [Theory]
    [InlineData(0.0f)]
    [InlineData(45.0f)]
    [InlineData(90.0f)]
    [InlineData(153.75f)]
    [InlineData(270.0f)]
    public void APixelSurvivesTheRoundTripToTheWorldAndBack(float bearing)
    {
        // Right and Forward are orthonormal, so the inverse is exact rather than approximate --
        // which is what lets a machine's pin land on the pixel it belongs to rather than near it.
        MapProjection map = At(bearing, centreX: -1_234, centreZ: 987);

        for (int y = 0; y < map.Size; y += 17)
        {
            for (int x = 0; x < map.Size; x += 17)
            {
                (float worldX, float worldZ) = map.WorldAt(x, y);
                (float backX, float backY) = map.PixelAt(worldX, worldZ);

                Assert.Equal(x, backX, 2);
                Assert.Equal(y, backY, 2);
            }
        }
    }

    [Fact]
    public void FacingNorthPutsNorthAtTheTopAndEastOnTheRight()
    {
        // North is -Z and east is +X, the convention Compass holds. Getting the forward component
        // the wrong way round mirrors the map north to south, which looks like a map right up
        // until somebody walks the wrong way.
        MapProjection map = At(0.0f);

        (float _, float aboveZ) = map.WorldAt(map.CentrePixel, map.CentrePixel - 10);
        Assert.True(aboveZ < 0.0f, "up the image should be north, which is -Z");

        (float rightX, float _) = map.WorldAt(map.CentrePixel + 10, map.CentrePixel);
        Assert.True(rightX > 0.0f, "the right of the image should be east, which is +X");
    }

    [Fact]
    public void FacingEastPutsEastAtTheTop()
    {
        // The whole point of a facing-up map, and the assertion that separates it from a north-up
        // one. At a bearing of 90 the top of the image is +X.
        MapProjection map = At(90.0f);

        (float aboveX, float aboveZ) = map.WorldAt(map.CentrePixel, map.CentrePixel - 10);

        Assert.True(aboveX > 0.0f, "facing east, up the image should be east");
        Assert.Equal(0.0f, aboveZ, 2);
    }

    [Fact]
    public void APixelIsExactlyItsShareOfGround()
    {
        // The scale means what it says: one pixel of travel is VoxelsPerPixel voxels of world.
        MapProjection map = At(0.0f);

        (float here, float _) = map.WorldAt(map.CentrePixel, map.CentrePixel);
        (float next, float _) = map.WorldAt(map.CentrePixel + 1, map.CentrePixel);

        Assert.Equal(map.VoxelsPerPixel, next - here, 2);
    }

    [Fact]
    public void TheShippedScaleIsTwoKilometresAcross()
    {
        // Matthew's choice, and the fit is with the region size: a region is about 2 km, so the
        // default map holds one of them. Stated as a test because the two constants live apart
        // and a change to either should have to notice the other.
        MapProjection map = At(0.0f);

        // 128 pixels of 16 m is 2,048 m -- "2 km" to a person, a power of two to the code, and
        // the distinction is worth one line because the first version of this asserted 2,000 and
        // went red for being right.
        Assert.Equal(2_048.0f, map.SpanMetres, 1);
        Assert.Equal(16, map.VoxelsPerPixel / VoxelConstants.VoxelsPerMetre);
    }

    [Fact]
    public void ForwardAndRightAreAUnitPairAtRightAngles()
    {
        // What makes PixelAt an exact inverse rather than a least-squares guess. A scale sneaking
        // into either vector would show up as a map that is subtly the wrong size, which is
        // exactly the kind of thing nobody sees.
        foreach (float bearing in new[] { 0.0f, 22.5f, 45.0f, 118.0f, 300.0f })
        {
            MapProjection map = At(bearing);
            (float fx, float fz) = map.Forward;
            (float rx, float rz) = map.Right;

            Assert.Equal(1.0f, (fx * fx) + (fz * fz), 4);
            Assert.Equal(1.0f, (rx * rx) + (rz * rz), 4);
            Assert.Equal(0.0f, (fx * rx) + (fz * rz), 4);
        }
    }

    [Fact]
    public void TheFourQuarterTurnsPermuteTheGridExactly()
    {
        // At 90-degree bearings the rotation maps pixels onto pixels with nothing left over, which
        // is what makes the renderer's own bearing-independence test an equality rather than a
        // tolerance. If this stops holding, that test has quietly become approximate.
        MapProjection north = At(0.0f);

        foreach (float bearing in new[] { 90.0f, 180.0f, 270.0f })
        {
            MapProjection turned = At(bearing);

            for (int y = 0; y < north.Size; y += 23)
            {
                for (int x = 0; x < north.Size; x += 23)
                {
                    (float worldX, float worldZ) = north.WorldAt(x, y);
                    (float px, float py) = turned.PixelAt(worldX, worldZ);

                    Assert.Equal(MathF.Round(px), px, 3);
                    Assert.Equal(MathF.Round(py), py, 3);
                }
            }
        }
    }
}
