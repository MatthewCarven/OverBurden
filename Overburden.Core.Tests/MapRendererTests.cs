using Overburden.Core.Navigation;

namespace Overburden.Core.Tests;

public class MapRendererTests
{
    private const int Size = 64;
    private const int VoxelsPerPixel = 64;

    private static MapProjection At(float bearing) => new(
        CentreVoxelX: 0,
        CentreVoxelZ: 0,
        VoxelsPerPixel: VoxelsPerPixel,
        Size: Size,
        BearingDegrees: bearing);

    /// <summary>A ridge running north-south: height depends on X alone, so its lit side is fixed.</summary>
    private static AnalyticSurface Ridge() =>
        new((x, _) => (int)(80.0 * Math.Sin(x / 900.0)));

    [Fact]
    public void TheSameGroundIsTheSameColourWhicheverWayYouFace()
    {
        // **The oracle for this whole slice.** A map is a picture of the world, so a place's
        // colour is a property of the place -- not of which way the player happens to be looking.
        // It catches a mirrored axis, a rotation applied twice, a rotation applied to the heights
        // but not the shading, and above all a hillshade lit from the screen rather than from the
        // world, which would move every shadow as you turned and is the one that looks fine in a
        // screenshot.
        //
        // Quarter turns, because those permute the pixel grid exactly -- so this is an equality
        // rather than a tolerance. MapProjectionTests.TheFourQuarterTurnsPermuteTheGridExactly is
        // what keeps that true.
        AnalyticSurface world = Ridge();
        MapProjection north = At(0.0f);
        MapColour[] facingNorth = MapRenderer.Render(world, north, 80.0f, 0);

        foreach (float bearing in new[] { 90.0f, 180.0f, 270.0f })
        {
            MapProjection turned = At(bearing);
            MapColour[] rendered = MapRenderer.Render(world, turned, 80.0f, 0);

            int compared = 0;
            for (int y = 8; y < Size - 8; y += 3)
            {
                for (int x = 8; x < Size - 8; x += 3)
                {
                    (float worldX, float worldZ) = north.WorldAt(x, y);
                    (float px, float py) = turned.PixelAt(worldX, worldZ);

                    int tx = (int)MathF.Round(px);
                    int ty = (int)MathF.Round(py);
                    if (tx < 0 || ty < 0 || tx >= Size || ty >= Size)
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
    public void AFlatWorldRendersOneColourEverywhere()
    {
        // Two claims in one, and both matter. A slope of zero must shade to exactly neutral, or
        // gentle country comes out as a field of rounding noise; and a constant height must land
        // on one ramp colour rather than drifting across stops.
        var flat = new AnalyticSurface((_, _) => 17);
        MapColour[] pixels = MapRenderer.Render(flat, At(33.0f), 80.0f, 0);

        Assert.Single(pixels.Distinct());
    }

    [Fact]
    public void HigherGroundIsLighterThanLowerGround()
    {
        // The ramp's direction, which is the one thing about it a player would notice being wrong
        // and which no amount of staring at hex codes catches.
        var low = new AnalyticSurface((_, _) => -70);
        var high = new AnalyticSurface((_, _) => 70);

        MapColour dark = MapRenderer.Render(low, At(0.0f), 80.0f, 0)[0];
        MapColour pale = MapRenderer.Render(high, At(0.0f), 80.0f, 0)[0];

        Assert.True(
            pale.R + pale.G + pale.B > dark.R + dark.G + dark.B,
            $"summits {pale} should be lighter than hollows {dark}");
    }

    [Fact]
    public void GroundPastTheRampsEndsClampsRatherThanWrapping()
    {
        // A world tuned with --relief=6 has ground well past an amplitude the map was handed, and
        // a ramp that wrapped would paint the highest peaks in the colour of the deepest hollow.
        var farAbove = new AnalyticSurface((_, _) => 10_000);
        var atTheTop = new AnalyticSurface((_, _) => 80);

        Assert.Equal(
            MapRenderer.Render(atTheTop, At(0.0f), 80.0f, 0)[0],
            MapRenderer.Render(farAbove, At(0.0f), 80.0f, 0)[0]);
    }

    [Fact]
    public void TheLightComesFromTheNorthWest()
    {
        // Stated rather than implied. A slope rising toward the north-west is lit; the same slope
        // rising toward the south-east is in shadow. This is what gives the picture its shape, and
        // reversing it turns every hill into a hollow -- the classic relief illusion, and one that
        // reads as "the map is showing valleys" rather than as a defect.
        //
        // Height falls as x and z rise, so the ground rises toward -x, -z: the north-west.
        var facingTheLight = new AnalyticSurface((x, z) => -(x + z) / 8);
        var facingAway = new AnalyticSurface((x, z) => (x + z) / 8);

        MapColour lit = MapRenderer.Render(facingTheLight, At(0.0f), 400.0f, 0)[(Size / 2 * Size) + (Size / 2)];
        MapColour shadowed = MapRenderer.Render(facingAway, At(0.0f), 400.0f, 0)[(Size / 2 * Size) + (Size / 2)];

        Assert.True(
            lit.R + lit.G + lit.B > shadowed.R + shadowed.G + shadowed.B,
            $"a north-west facing slope {lit} should be lit against {shadowed}");
    }

    [Fact]
    public void ShadingSurvivesToTheVeryEdgeOfTheMap()
    {
        // The apron's reason. Without the extra ring the edge pixels have no neighbours, and
        // whatever they get instead -- a zero, a clamp, a repeat -- shades them differently from
        // the rest. A one-pixel wrong border is the kind of thing that is only ever noticed after
        // it ships.
        AnalyticSurface world = Ridge();
        MapColour[] pixels = MapRenderer.Render(world, At(0.0f), 80.0f, 0);

        // The top-left pixel and its inward neighbour sit on the same slope, so they should be
        // near-identical -- which they are not if the edge shaded against nothing.
        MapColour corner = pixels[0];
        MapColour inward = pixels[(1 * Size) + 1];

        Assert.True(
            Math.Abs(corner.R - inward.R) < 40,
            $"the edge {corner} shades quite unlike its neighbour {inward}");
    }

    [Fact]
    public void TheCentrePixelShowsTheGroundThePlayerIsStandingOn()
    {
        // The break battery asked for this one: sampling the apron at the wrong offset shifts the
        // whole picture by a pixel against the projection, so the marker drawn at the centre would
        // sit next to the ground it claims to be on. Every other test compares the render against
        // itself and is blind to a constant offset.
        //
        // Shading off, so the colour is the ramp alone and the boundary is exact rather than
        // muddied by a hillshade at a cliff.
        var cliff = new AnalyticSurface((x, _) => x < 0 ? -80 : 80);
        var unshaded = MapPalette.Default with { ShadeStrength = 0.0f };

        MapProjection map = At(0.0f);
        var pixels = new MapColour[Size * Size];
        MapRenderer.Render(cliff, map, 80.0f, 0, unshaded, pixels);

        int centre = map.CentrePixel;
        Assert.Equal(unshaded.Stops[^1], pixels[(centre * Size) + centre]);
        Assert.Equal(unshaded.Stops[0], pixels[(centre * Size) + centre - 1]);
    }

    [Fact]
    public void SeaLevelMovesWhatCountsAsHighGround()
    {
        // Also the battery's: every other test passes a sea level of zero, so dropping the term
        // entirely was an identity. A world generated with --sealevel elsewhere would have
        // rendered as though its whole surface were a mountain range.
        var ground = new AnalyticSurface((_, _) => 200);

        MapColour high = MapRenderer.Render(ground, At(0.0f), 80.0f, 0)[0];
        MapColour ordinary = MapRenderer.Render(ground, At(0.0f), 80.0f, 200)[0];

        Assert.NotEqual(high, ordinary);
        Assert.Equal(MapPalette.Default.Stops[^1], high);
    }

    [Fact]
    public void TheRampClampsBeyondItsOwnEnds()
    {
        // Belt and braces made load-bearing. Render clamps before it calls Ramp, so the clamp
        // inside Ramp is unreachable from there and a mutation to it was an identity -- but Ramp
        // is internal and the overlays slice will call it directly for its own legend, where the
        // caller has no such guarantee.
        var stops = MapPalette.Default.Stops;

        Assert.Equal(stops[0], MapRenderer.Ramp(stops, -5.0f));
        Assert.Equal(stops[^1], MapRenderer.Ramp(stops, 5.0f));
        Assert.Equal(stops[0], MapRenderer.Ramp(stops, float.NegativeInfinity));
    }

    [Fact]
    public void ASpanTooSmallForTheMapIsRefused()
    {
        var flat = new AnalyticSurface((_, _) => 0);
        var pixels = new MapColour[10];

        Assert.Throws<ArgumentException>(
            () => MapRenderer.Render(flat, At(0.0f), 80.0f, 0, MapPalette.Default, pixels));
    }

    [Fact]
    public void AMapWithNoPixelsIsRefused()
    {
        var flat = new AnalyticSurface((_, _) => 0);
        var empty = At(0.0f) with { Size = 0 };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => MapRenderer.Render(flat, empty, 80.0f, 0));
    }

    [Fact]
    public void TheRampReachesBothItsEndsAndPassesThroughEveryStop()
    {
        // A ramp that never reaches its last colour is a palette with a dead entry in it, and a
        // ramp that skips one is a palette somebody will keep retuning without effect.
        var palette = MapPalette.Default;

        Assert.Equal(palette.Stops[0], MapRenderer.Ramp(palette.Stops, -1.0f));
        Assert.Equal(palette.Stops[^1], MapRenderer.Ramp(palette.Stops, 1.0f));

        var seen = new HashSet<MapColour>();
        for (float t = -1.0f; t <= 1.0f; t += 0.001f)
        {
            seen.Add(MapRenderer.Ramp(palette.Stops, t));
        }

        foreach (MapColour stop in palette.Stops)
        {
            Assert.Contains(stop, seen);
        }
    }
}
