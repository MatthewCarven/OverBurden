using Overburden.Core.Navigation;

namespace Overburden.Core.Tests;

public class MapOverlayTests
{
    private const int Size = 64;
    private const int VoxelsPerPixel = 64;

    private static MapProjection At(float bearing) => new(
        CentreVoxelX: 0,
        CentreVoxelZ: 0,
        VoxelsPerPixel: VoxelsPerPixel,
        Size: Size,
        BearingDegrees: bearing);

    private static AnalyticSurface Ridge() =>
        new((x, _) => (int)(80.0 * Math.Sin(x / 900.0)));

    /// <summary>Water standing at one level over a square patch of world, and nowhere else.</summary>
    private sealed class Pond(int centreX, int centreZ, int halfVoxels, float level, int coveredHalf)
        : IMapWater
    {
        public bool Covers(int worldVoxelX, int worldVoxelZ) =>
            Math.Abs(worldVoxelX - centreX) <= coveredHalf
            && Math.Abs(worldVoxelZ - centreZ) <= coveredHalf;

        public bool TryLevelVoxels(int worldVoxelX, int worldVoxelZ, out float levelVoxels)
        {
            levelVoxels = level;
            return Covers(worldVoxelX, worldVoxelZ)
                && Math.Abs(worldVoxelX - centreX) <= halfVoxels
                && Math.Abs(worldVoxelZ - centreZ) <= halfVoxels;
        }
    }

    [Fact]
    public void NoOverlayRendersExactlyTheTerrain()
    {
        // The floor under everything else here: an overlay that draws nothing must cost nothing
        // and change nothing, or every figure and every screenshot taken before slice c is a
        // different picture now.
        AnalyticSurface world = Ridge();

        MapColour[] plain = MapRenderer.Render(world, At(41.0f), MapRamp.Auto);
        MapColour[] empty = MapRenderer.Render(world, At(41.0f), MapRamp.Auto, null, MapOverlay.None);

        Assert.Equal(plain, empty);
    }

    [Fact]
    public void TheSameGroundIsTheSameColourWhicheverWayYouFaceWithOverlaysOn()
    {
        // **Slice a's oracle, extended to everything slice c draws.** Every overlay is anchored to
        // the world, so every one of them has to survive a turn — and a pin that drifts when you
        // turn is worse than no pin at all, because you would walk to where it said.
        //
        // Quarter turns, so the pixel grids permute exactly and this stays an equality.
        AnalyticSurface world = Ridge();
        var overlay = new MapOverlay
        {
            Water = new Pond(-600, 400, halfVoxels: 700, level: 40.0f, coveredHalf: 1_400),
            IsDug = (cx, cz) => cx is >= -3 and <= 1 && cz is >= 0 and <= 4,
            Pins =
            [
                new MapPin(512, -256, new MapColour(255, 96, 96)),
                new MapPin(-900, 700, new MapColour(96, 255, 128)),
            ],
        };

        MapProjection north = At(0.0f);
        MapColour[] facingNorth = MapRenderer.Render(world, north, MapRamp.Auto, null, overlay);

        foreach (float bearing in new[] { 90.0f, 180.0f, 270.0f })
        {
            MapProjection turned = At(bearing);
            MapColour[] rendered = MapRenderer.Render(world, turned, MapRamp.Auto, null, overlay);

            int compared = 0;
            for (int y = 8; y < Size - 8; y += 3)
            {
                for (int x = 8; x < Size - 8; x += 3)
                {
                    (float worldX, float worldZ) = north.WorldAt(x, y);
                    (float px, float py) = turned.PixelAt(worldX, worldZ);

                    int tx = (int)MathF.Round(px);
                    int ty = (int)MathF.Round(py);
                    if (tx < 1 || ty < 1 || tx >= Size - 1 || ty >= Size - 1)
                    {
                        continue;
                    }

                    compared++;
                    Assert.Equal(facingNorth[(y * Size) + x], rendered[(ty * Size) + tx]);
                }
            }

            Assert.True(compared > 100, $"only {compared} pixels compared at bearing {bearing}");
        }
    }

    [Fact]
    public void APinLandsOnThePixelTheProjectionNames()
    {
        // The pin's whole job is to be in the right place. PixelAt is tested to be an exact
        // inverse, so this is the wire between that and the picture.
        AnalyticSurface world = Ridge();
        var pin = new MapPin(700, -1_300, new MapColour(255, 0, 255));

        MapProjection map = At(23.0f);
        MapColour[] pixels = MapRenderer.Render(
            world, map, MapRamp.Auto, null, new MapOverlay { Pins = [pin] });

        (float px, float py) = map.PixelAt(pin.VoxelX, pin.VoxelZ);
        int x = (int)MathF.Round(px);
        int y = (int)MathF.Round(py);

        Assert.InRange(x, 0, Size - 1);
        Assert.InRange(y, 0, Size - 1);
        Assert.Equal(pin.Colour, pixels[(y * Size) + x]);

        // And exactly one pixel wears it, so a pin cannot be mistaken for terrain.
        Assert.Single(pixels.Where(p => p == pin.Colour));
    }

    [Fact]
    public void PinsStandingTogetherDoNotBlackOutEachOther()
    {
        // A mining operation's machines stand in a yard, not scattered: the pool world has 127 of
        // them within a few metres, and drawing each pin's outline as it went painted them over
        // one another's centres until the whole base was a black blob. Centres first, outlines
        // around what is left.
        AnalyticSurface world = Ridge();
        var red = new MapColour(255, 0, 0);
        var green = new MapColour(0, 255, 0);

        MapProjection map = At(0.0f);
        int step = map.VoxelsPerPixel;

        MapColour[] pixels = MapRenderer.Render(
            world, map, MapRamp.Auto, null,
            new MapOverlay
            {
                Pins =
                [
                    new MapPin(0, 0, red),
                    new MapPin(step, 0, green),
                    new MapPin(step * 2, 0, red),
                ],
            });

        int centre = map.CentrePixel;

        Assert.Equal(red, pixels[(centre * Size) + centre]);
        Assert.Equal(green, pixels[(centre * Size) + centre + 1]);
        Assert.Equal(red, pixels[(centre * Size) + centre + 2]);
    }

    [Fact]
    public void APinOffTheMapIsDroppedRatherThanClampedToTheRim()
    {
        // A pin pushed to the edge would claim a position it does not have, and walking to it
        // would be walking to nothing. Silence is the honest answer for something out of view.
        AnalyticSurface world = Ridge();
        var faraway = new MapPin(9_000_000, -9_000_000, new MapColour(255, 0, 255));

        MapColour[] with = MapRenderer.Render(
            world, At(0.0f), MapRamp.Auto, null, new MapOverlay { Pins = [faraway] });
        MapColour[] without = MapRenderer.Render(world, At(0.0f), MapRamp.Auto);

        Assert.Equal(without, with);
    }

    [Fact]
    public void DugGroundIsDarkerThanTheGroundBesideIt()
    {
        // Disturbed, not replaced: the mine darkens the terrain rather than painting over it, so
        // a mine on a slope still reads as a slope.
        //
        // **One chunk column, not a half-plane**, and that is the break battery's doing. The first
        // version asked `cx >= 0`, which gives the same answer whether it is handed a chunk
        // coordinate or a voxel one -- so the eightfold mixup that would put the whole mine in the
        // wrong place was invisible. A predicate naming one column can only be satisfied by the
        // right units.
        AnalyticSurface world = Ridge();
        var overlay = new MapOverlay { IsDug = (cx, cz) => cx == 2 && cz == 0 };

        MapColour[] dug = MapRenderer.Render(world, At(0.0f), MapRamp.Auto, null, overlay);
        MapColour[] untouched = MapRenderer.Render(world, At(0.0f), MapRamp.Auto);

        int centre = Size / 2;

        // One pixel east of centre is world x 64, which is chunk column 2. Its neighbours are not.
        int inside = (centre * Size) + centre + 1;
        int outside = (centre * Size) + centre + 4;

        Assert.True(
            dug[inside].R + dug[inside].G + dug[inside].B
                < untouched[inside].R + untouched[inside].G + untouched[inside].B,
            $"the dug column {dug[inside]} should be darker than {untouched[inside]} undug");

        Assert.Equal(untouched[outside], dug[outside]);
    }

    [Fact]
    public void WaterCoversTheGroundItStandsOverAndNothingElse()
    {
        // A level above the ground is water; the same level below it is not. The comparison is
        // against the terrain, not against a flag, which is what lets a lake have a shore.
        var bowl = new AnalyticSurface((x, _) => x < 0 ? -200 : 200);
        var overlay = new MapOverlay
        {
            Water = new Pond(0, 0, halfVoxels: 4_000, level: 0.0f, coveredHalf: 4_000),
        };

        MapColour[] pixels = MapRenderer.Render(bowl, At(0.0f), MapRamp.Around(0, 200.0f), null, overlay);

        int centre = Size / 2;
        MapColour low = pixels[(centre * Size) + centre - 6];
        MapColour high = pixels[(centre * Size) + centre + 6];

        // The low half is under the level and reads as water; the high half is above it and does
        // not. Water is bluer than it is red, and this terrain never is.
        Assert.True(low.B > low.R, $"the drowned half {low} should read as water");
        Assert.True(high.R >= high.B, $"the dry half {high} should not");
    }

    [Fact]
    public void DeeperWaterIsDarker()
    {
        // The depth ramp, which is what makes a lake read as a lake rather than as a blue blob.
        var shelf = new AnalyticSurface((x, _) => x < 0 ? -400 : -20);
        var overlay = new MapOverlay
        {
            Water = new Pond(0, 0, halfVoxels: 4_000, level: 0.0f, coveredHalf: 4_000),
        };

        MapColour[] pixels = MapRenderer.Render(shelf, At(0.0f), MapRamp.Around(0, 400.0f), null, overlay);

        int centre = Size / 2;
        MapColour deep = pixels[(centre * Size) + centre - 6];
        MapColour shallow = pixels[(centre * Size) + centre + 6];

        Assert.True(
            deep.R + deep.G + deep.B < shallow.R + shallow.G + shallow.B,
            $"deep water {deep} should be darker than shallow {shallow}");
    }

    [Fact]
    public void TheMapSaysWhereItStopsKnowingAboutWater()
    {
        // Tier A analyses 512 m and the map is four times that, so most of it is ground nobody has
        // looked at. Drawing a confident dry plain over that would be the map lying about the one
        // thing it cannot see, so the rim says where the knowledge ends.
        AnalyticSurface world = Ridge();
        var overlay = new MapOverlay
        {
            Water = new Pond(0, 0, halfVoxels: 0, level: -10_000.0f, coveredHalf: 700),
        };

        MapColour[] pixels = MapRenderer.Render(world, At(0.0f), MapRamp.Auto, null, overlay);

        Assert.Contains(MapPalette.Default.WindowEdge, pixels);

        // And it is a rim rather than a fill: far fewer pixels than the area it encloses.
        int rim = pixels.Count(p => p == MapPalette.Default.WindowEdge);
        Assert.InRange(rim, 8, Size * 4);
    }

    [Fact]
    public void WithNoWaterThereIsNoRim()
    {
        // The rim is a statement about the water analysis. A map with no water to ask has nothing
        // to say about where that analysis ends, and should not draw a line implying it does.
        AnalyticSurface world = Ridge();
        var overlay = new MapOverlay { IsDug = (_, _) => true };

        MapColour[] pixels = MapRenderer.Render(world, At(0.0f), MapRamp.Auto, null, overlay);

        Assert.DoesNotContain(MapPalette.Default.WindowEdge, pixels);
    }
}
