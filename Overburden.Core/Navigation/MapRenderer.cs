using Overburden.Core.Lighting;
using Overburden.Core.Voxels;

namespace Overburden.Core.Navigation;

/// <summary>One pixel of the map, before anything Godot has an opinion about.</summary>
public readonly record struct MapColour(byte R, byte G, byte B);

/// <summary>What heights the two ends of the colour ramp stand for.</summary>
/// <remarks>
/// <para>
/// <b><see cref="Auto"/> is what a map should almost always use, and the first version of this got
/// it wrong.</b> Handing the ramp the world's <em>peak</em> amplitude is the obvious thing and it
/// produces a picture of uniform mush: a shaped world runs from 19 to 144 voxels of amplitude, so
/// an ordinary patch of it uses about a third of the palette, centred — measured at 32% on the
/// first screenshot, which came out entirely brown. **A hypsometric ramp belongs to the map, not
/// to the world**, which is how every physical atlas has ever done it.
/// </para>
/// <para>
/// The cost is that two maps are not comparable by colour, which is why the panel prints the
/// range it settled on. For finding your way that is the right trade: the question is "which of
/// the things I can see is higher", never "is this the same brown as yesterday".
/// </para>
/// </remarks>
public readonly record struct MapRamp(float LowVoxels, float HighVoxels)
{
    /// <summary>Fit the ramp to the ground actually in view.</summary>
    public static MapRamp Auto => new(float.NaN, float.NaN);

    /// <summary>A fixed ramp, for a caller that wants two maps to share a scale.</summary>
    public static MapRamp Around(int seaLevelVoxels, float amplitudeVoxels) =>
        new(seaLevelVoxels - amplitudeVoxels, seaLevelVoxels + amplitudeVoxels);

    public bool IsAuto => float.IsNaN(LowVoxels) || float.IsNaN(HighVoxels);
}

/// <summary>
/// How a height becomes a colour, and how a slope becomes light and shadow.
/// </summary>
/// <remarks>
/// <para>
/// <b>Height, not local relief, and that is decision 1 of the pass.</b> Colouring by how lumpy a
/// place is sounds apter for finding mountains and is worse: a high flat plateau and a low flat
/// plain come out identical, so you cannot tell a valley you can walk out of from one you cannot.
/// A hypsometric ramp — the green-to-brown-to-pale of a physical atlas — reads as landscape at a
/// glance, and the shading is what makes the shape legible.
/// </para>
/// <para>
/// Every number here is an estimate and belongs to the look rather than to the world, so they are
/// gathered in one place for the play to move. <see cref="SlopeSoftnessVoxels"/> is the one with a
/// real argument behind it: it is the rise-per-pixel at which shading reaches half strength, and
/// the ramp is deliberately smooth rather than a threshold so that gentle country shades gently
/// instead of going flat.
/// </para>
/// </remarks>
public sealed record MapPalette
{
    public static readonly MapPalette Default = new();

    /// <summary>Low ground through to summits. Stops are at even intervals from -1 to +1.</summary>
    public IReadOnlyList<MapColour> Stops { get; init; } =
    [
        new(46, 74, 52),      // deep hollows
        new(70, 108, 62),     // lowland green
        new(120, 142, 74),    // rising ground
        new(150, 134, 88),    // olive
        new(146, 112, 76),    // brown
        new(178, 170, 160),   // bare rock
        new(232, 232, 230),   // summits
    ];

    /// <summary>How far the shading may lighten or darken a colour, as a share of it.</summary>
    public float ShadeStrength { get; init; } = 0.45f;

    /// <summary>
    /// Rise per pixel at which shading reaches half strength, as a share of the ramp's own range.
    /// </summary>
    /// <remarks>
    /// <b>A share rather than a number of voxels, for the reason <see cref="MapRamp.Auto"/>
    /// exists.</b> An absolute softness is right for exactly one kind of country: set for
    /// mountains it leaves gentle ground flat, and set for gentle ground it makes mountains a
    /// wall of black and white. Tying it to the range the map is already fitting means the
    /// shading arrives in proportion to what is there. An estimate, and the one number here most
    /// worth moving after a play.
    /// </remarks>
    public float SlopeSoftnessShareOfRange { get; init; } = 0.08f;
}

/// <summary>
/// Turns a patch of world into a picture of itself.
/// </summary>
/// <remarks>
/// <para>
/// <b>Godot-free and therefore testable, which is the whole reason this is a separate slice.</b> A
/// map that is subtly rotated the wrong way, or mirrored north to south, or off by half a pixel at
/// the centre, looks exactly like a map — there is no error message and nothing to notice until
/// somebody walks the wrong way for ten minutes. The oracle is in
/// <c>MapRendererTests.TheSameGroundIsTheSameColourWhicheverWayYouFace</c>: <b>a world position's
/// colour must not depend on which way the player is looking</b>, which is the whole of what
/// "this is a picture of the world" means.
/// </para>
/// <para>
/// <b>The light is fixed to the world, not to the screen</b>, and it costs nothing to make it so.
/// Shading from image-space neighbours would light whichever side of a hill happened to be facing
/// up, so turning round would move every shadow and the terrain would stop being recognisable. The
/// map is a rigid rotation of the world, so the world-space gradient is the image-space gradient
/// rotated by the same amount — three multiplies, no extra samples.
/// </para>
/// <para>
/// <b>It samples a one-pixel apron around the map</b> so that every drawn pixel has the four
/// neighbours the gradient wants. That is 130² samples for a 128² map: a 3% cost for an edge that
/// is shaded like everywhere else rather than flat.
/// </para>
/// </remarks>
public static class MapRenderer
{
    /// <summary>The direction the light comes from, in world (x, z): the north-west, as an atlas does.</summary>
    private const float LightX = -0.70710678f;

    private const float LightZ = -0.70710678f;

    /// <summary>
    /// Samples the terrain under <paramref name="projection"/> and writes one colour per pixel,
    /// row-major from the top-left.
    /// </summary>
    /// <param name="heights">The world. Asked for arbitrary coordinates; nothing need be loaded.</param>
    /// <param name="projection">Where, how wide, and which way up.</param>
    /// <param name="ramp">
    /// What the ends of the colour ramp mean. <see cref="MapRamp.Auto"/> fits them to the ground
    /// in view, which is what makes the picture read as landscape rather than as one flat tint.
    /// </param>
    /// <param name="palette">The look. <see cref="MapPalette.Default"/> unless a caller is sweeping it.</param>
    /// <param name="pixels">Receives <c>Size * Size</c> colours.</param>
    /// <returns>The ramp actually used, which is worth printing when it was chosen here.</returns>
    public static MapRamp Render(
        ISurfaceHeights heights,
        MapProjection projection,
        MapRamp ramp,
        MapPalette palette,
        Span<MapColour> pixels)
    {
        ArgumentNullException.ThrowIfNull(heights);
        ArgumentNullException.ThrowIfNull(palette);

        int size = projection.Size;
        if (size <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(projection), size, "a map with no pixels is not a map.");
        }

        if (pixels.Length < size * size)
        {
            throw new ArgumentException(
                $"the map is {size} x {size} = {size * size} pixels and the span holds "
                + $"{pixels.Length}.",
                nameof(pixels));
        }

        // Sampled two rings wide and filtered down to one: the inner ring is what the
        // gradient reads, and the filter is anti-aliasing. At the shipped 16 m a pixel the
        // terrain's top two octaves have periods of 16 and 8 m and fold straight into pixel
        // hash -- the first screenshot of the panel was a field of noise for exactly that
        // reason. A box over samples already being taken removes most of it for nothing.
        //
        // Square, and that is not incidental: a 3 x 3 neighbourhood maps onto itself under a
        // quarter turn, which is what keeps the bearing-independence test an equality.
        // Anything directional here would break it, and would be right to.
        int apron = size + 2;
        // -2, not -1: filtering a grid down by a ring moves what each cell is centred on, so
        // the sampling has to start a ring further out to land back where the render expects.
        // Getting this wrong shifts the whole picture one pixel against the projection -- and
        // a shift in *image* space is a different shift in the world at every bearing, which
        // is why TheSameGroundIsTheSameColourWhicheverWayYouFace went red for it.
        int[] sampled = Smoothed(
            SampleHeights(heights, projection, apron + 2, firstPixel: -2), apron + 2);

        (float rx, float rz) = projection.Right;
        (float fx, float fz) = projection.Forward;

        MapRamp used = ramp.IsAuto ? Fit(sampled) : ramp;
        float low = used.LowVoxels;

        // A range of zero is a flat world, and every t would be 0/0 without this. The floor puts
        // such a world squarely in the middle of the ramp, which is what "featureless" should
        // look like.
        float span = MathF.Max(used.HighVoxels - used.LowVoxels, 0.0001f);

        // A slope of zero must be exactly neutral, so a flat world renders one colour rather than
        // a field of rounding. Tested.
        float softness = MathF.Max(palette.SlopeSoftnessShareOfRange * span, 0.0001f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int here = sampled[((y + 1) * apron) + (x + 1)];

                // Central differences in image space: along the map's right, and along its up.
                float alongRight =
                    sampled[((y + 1) * apron) + (x + 2)] - sampled[((y + 1) * apron) + x];
                float alongForward =
                    sampled[(y * apron) + (x + 1)] - sampled[((y + 2) * apron) + (x + 1)];

                // ...rotated back into the world, which is what fixes the light to the ground.
                float slopeX = (alongRight * rx) + (alongForward * fx);
                float slopeZ = (alongRight * rz) + (alongForward * fz);

                float toward = (slopeX * LightX) + (slopeZ * LightZ);
                float shade = 1.0f + (palette.ShadeStrength * (toward / (MathF.Abs(toward) + softness)));

                // Into -1..1, which is what the ramp spans.
                float t = Math.Clamp((((here - low) / span) * 2.0f) - 1.0f, -1.0f, 1.0f);
                pixels[(y * size) + x] = Shaded(Ramp(palette.Stops, t), shade);
            }
        }

        return used;
    }

    /// <summary>
    /// A 3 x 3 box filter, returning a grid one ring smaller than the one it was given.
    /// </summary>
    /// <remarks>
    /// Integers rather than floats, because the heights are voxels and a map pixel cannot show
    /// a fraction of one; rounded to nearest rather than truncated, so a slope does not acquire
    /// a half-voxel bias that the hillshade would then draw as a permanent tilt.
    /// </remarks>
    internal static int[] Smoothed(int[] sampled, int pitch)
    {
        int inner = pitch - 2;
        var filtered = new int[inner * inner];

        for (int y = 0; y < inner; y++)
        {
            for (int x = 0; x < inner; x++)
            {
                int total = 0;
                for (int dy = 0; dy <= 2; dy++)
                {
                    for (int dx = 0; dx <= 2; dx++)
                    {
                        total += sampled[((y + dy) * pitch) + x + dx];
                    }
                }

                filtered[(y * inner) + x] = (int)MathF.Round(total / 9.0f);
            }
        }

        return filtered;
    }

    /// <summary>The lowest and highest ground in the sample, with a floor under flat country.</summary>
    /// <remarks>
    /// The floor matters: without it a perfectly level plain would have a range of zero and the
    /// shading would divide by nothing. Half a metre is below anything a person would call relief.
    /// </remarks>
    internal static MapRamp Fit(ReadOnlySpan<int> sampled)
    {
        int low = int.MaxValue;
        int high = int.MinValue;

        foreach (int height in sampled)
        {
            low = Math.Min(low, height);
            high = Math.Max(high, height);
        }

        const float Least = 2.0f;
        if (high - low < Least)
        {
            float middle = (low + high) * 0.5f;
            return new MapRamp(middle - (Least / 2.0f), middle + (Least / 2.0f));
        }

        return new MapRamp(low, high);
    }

    /// <summary>Allocates and fills the map, for callers with nowhere to put a span.</summary>
    public static MapColour[] Render(
        ISurfaceHeights heights,
        MapProjection projection,
        MapRamp ramp,
        MapPalette? palette = null)
    {
        var pixels = new MapColour[projection.Size * projection.Size];
        Render(heights, projection, ramp, palette ?? MapPalette.Default, pixels);
        return pixels;
    }

    /// <summary>
    /// The height under every pixel, plus a one-pixel apron so the edge has neighbours too.
    /// </summary>
    /// <remarks>
    /// Public because the overlays slice wants the same grid, and because a caller measuring the
    /// sampling cost should be able to measure it without the shading attached.
    /// </remarks>
    public static int[] SampleHeights(
        ISurfaceHeights heights, MapProjection projection, int apron, int firstPixel = -1)
    {
        var sampled = new int[apron * apron];

        for (int y = 0; y < apron; y++)
        {
            for (int x = 0; x < apron; x++)
            {
                (float worldX, float worldZ) = projection.WorldAt(x + firstPixel, y + firstPixel);
                sampled[(y * apron) + x] = heights.SurfaceHeightAt(
                    (int)MathF.Round(worldX),
                    (int)MathF.Round(worldZ));
            }
        }

        return sampled;
    }

    /// <summary>The hypsometric ramp at <paramref name="t"/> in [-1, 1].</summary>
    internal static MapColour Ramp(IReadOnlyList<MapColour> stops, float t)
    {
        if (stops.Count == 0)
        {
            throw new ArgumentException("a ramp needs at least one colour.", nameof(stops));
        }

        if (stops.Count == 1)
        {
            return stops[0];
        }

        float position = (Math.Clamp(t, -1.0f, 1.0f) + 1.0f) * 0.5f * (stops.Count - 1);
        int low = Math.Clamp((int)position, 0, stops.Count - 2);
        float blend = position - low;

        MapColour a = stops[low];
        MapColour b = stops[low + 1];

        return new MapColour(
            Mix(a.R, b.R, blend),
            Mix(a.G, b.G, blend),
            Mix(a.B, b.B, blend));
    }

    private static byte Mix(byte a, byte b, float t) =>
        (byte)Math.Clamp(MathF.Round(a + ((b - a) * t)), 0.0f, 255.0f);

    private static MapColour Shaded(MapColour colour, float shade) =>
        new(Scale(colour.R, shade), Scale(colour.G, shade), Scale(colour.B, shade));

    private static byte Scale(byte channel, float shade) =>
        (byte)Math.Clamp(MathF.Round(channel * shade), 0.0f, 255.0f);
}
