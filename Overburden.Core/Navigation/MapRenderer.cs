using Overburden.Core.Lighting;
using Overburden.Core.Voxels;

namespace Overburden.Core.Navigation;

/// <summary>One pixel of the map, before anything Godot has an opinion about.</summary>
public readonly record struct MapColour(byte R, byte G, byte B);

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

    /// <summary>Rise per pixel, in voxels, at which shading reaches half strength. An estimate.</summary>
    public float SlopeSoftnessVoxels { get; init; } = 24.0f;
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
    /// <param name="amplitudeVoxels">
    /// The height the ramp's top stands for, measured from <paramref name="seaLevelVoxels"/>. The
    /// world's own peak amplitude, so a world tuned for big mountains does not render every one of
    /// them white.
    /// </param>
    /// <param name="seaLevelVoxels">The height the middle of the ramp stands for.</param>
    /// <param name="palette">The look. <see cref="MapPalette.Default"/> unless a caller is sweeping it.</param>
    /// <param name="pixels">Receives <c>Size * Size</c> colours.</param>
    public static void Render(
        ISurfaceHeights heights,
        MapProjection projection,
        float amplitudeVoxels,
        int seaLevelVoxels,
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

        int apron = size + 2;
        int[] sampled = SampleHeights(heights, projection, apron);

        (float rx, float rz) = projection.Right;
        (float fx, float fz) = projection.Forward;

        // A slope of zero must be exactly neutral, so a flat world renders one colour rather than
        // a field of rounding. Tested.
        float softness = MathF.Max(palette.SlopeSoftnessVoxels, 0.0001f);
        float span = MathF.Max(MathF.Abs(amplitudeVoxels), 0.0001f);

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

                float t = Math.Clamp((here - seaLevelVoxels) / span, -1.0f, 1.0f);
                pixels[(y * size) + x] = Shaded(Ramp(palette.Stops, t), shade);
            }
        }
    }

    /// <summary>Allocates and fills the map, for callers with nowhere to put a span.</summary>
    public static MapColour[] Render(
        ISurfaceHeights heights,
        MapProjection projection,
        float amplitudeVoxels,
        int seaLevelVoxels,
        MapPalette? palette = null)
    {
        var pixels = new MapColour[projection.Size * projection.Size];
        Render(heights, projection, amplitudeVoxels, seaLevelVoxels, palette ?? MapPalette.Default, pixels);
        return pixels;
    }

    /// <summary>
    /// The height under every pixel, plus a one-pixel apron so the edge has neighbours too.
    /// </summary>
    /// <remarks>
    /// Public because the overlays slice wants the same grid, and because a caller measuring the
    /// sampling cost should be able to measure it without the shading attached.
    /// </remarks>
    public static int[] SampleHeights(ISurfaceHeights heights, MapProjection projection, int apron)
    {
        var sampled = new int[apron * apron];

        for (int y = 0; y < apron; y++)
        {
            for (int x = 0; x < apron; x++)
            {
                (float worldX, float worldZ) = projection.WorldAt(x - 1, y - 1);
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
