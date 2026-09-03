namespace Overburden.Core.Worldgen;

/// <summary>
/// Deterministic gradient noise, and fractal Brownian motion over it.
/// </summary>
/// <remarks>
/// <para>
/// Gradient noise rather than value noise. Value noise is less code, but it takes its
/// extremes at integer lattice points, which shows up on a heightmap as a visible grid of
/// hills and hollows aligned to the axes. Gradient noise is zero at the lattice points and
/// takes its extremes between them, so the grid does not read as a grid.
/// </para>
/// <para>
/// Every value comes from <see cref="Hash64"/>, so there is no permutation table to seed,
/// no state, and no initialisation order to get wrong. Sampling the same coordinate twice
/// — in any thread, in any process, in any order — gives the same answer, which is what
/// makes chunk generation embarrassingly parallel.
/// </para>
/// </remarks>
public static class GradientNoise
{
    // The eight 2D directions, and the twelve 3D edge-midpoint directions Perlin settled
    // on. Both sets are deliberately unnormalised in the classic way: components are 0 or
    // ±1, so the dot products are adds and subtracts.
    private static readonly (float X, float Y)[] Gradients2D =
    [
        (1, 0), (-1, 0), (0, 1), (0, -1),
        (1, 1), (-1, 1), (1, -1), (-1, -1),
    ];

    // Perlin's twelve edge-midpoint directions, padded to sixteen by repeating four of
    // them. Sixteen means the index is `hash & 15` rather than `hash % 12`, and a 64-bit
    // integer division per lattice corner is not something to pay in the innermost loop of
    // cave generation. The slight bias toward the four repeats is the standard tradeoff and
    // is invisible in terrain.
    private static readonly (float X, float Y, float Z)[] Gradients3D =
    [
        (1, 1, 0), (-1, 1, 0), (1, -1, 0), (-1, -1, 0),
        (1, 0, 1), (-1, 0, 1), (1, 0, -1), (-1, 0, -1),
        (0, 1, 1), (0, -1, 1), (0, 1, -1), (0, -1, -1),
        (1, 1, 0), (-1, 1, 0), (0, -1, 1), (0, -1, -1),
    ];

    /// <summary>
    /// Quintic fade, 6t⁵ − 15t⁴ + 10t³. Its first and second derivatives are both zero at
    /// 0 and 1, which is what stops lattice boundaries showing as creases under lighting.
    /// </summary>
    private static float Fade(float t) => t * t * t * ((t * ((t * 6.0f) - 15.0f)) + 10.0f);

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);

    /// <summary>2D gradient noise, roughly in [-1, 1].</summary>
    public static float Sample2D(ulong seed, float x, float y)
    {
        int x0 = FloorToInt(x);
        int y0 = FloorToInt(y);
        float fx = x - x0;
        float fy = y - y0;

        float u = Fade(fx);
        float v = Fade(fy);

        float n00 = Dot2D(seed, x0, y0, fx, fy);
        float n10 = Dot2D(seed, x0 + 1, y0, fx - 1.0f, fy);
        float n01 = Dot2D(seed, x0, y0 + 1, fx, fy - 1.0f);
        float n11 = Dot2D(seed, x0 + 1, y0 + 1, fx - 1.0f, fy - 1.0f);

        return Lerp(Lerp(n00, n10, u), Lerp(n01, n11, u), v);
    }

    /// <summary>3D gradient noise, roughly in [-1, 1].</summary>
    public static float Sample3D(ulong seed, float x, float y, float z)
    {
        int x0 = FloorToInt(x);
        int y0 = FloorToInt(y);
        int z0 = FloorToInt(z);
        float fx = x - x0;
        float fy = y - y0;
        float fz = z - z0;

        float u = Fade(fx);
        float v = Fade(fy);
        float w = Fade(fz);

        float n000 = Dot3D(seed, x0, y0, z0, fx, fy, fz);
        float n100 = Dot3D(seed, x0 + 1, y0, z0, fx - 1.0f, fy, fz);
        float n010 = Dot3D(seed, x0, y0 + 1, z0, fx, fy - 1.0f, fz);
        float n110 = Dot3D(seed, x0 + 1, y0 + 1, z0, fx - 1.0f, fy - 1.0f, fz);
        float n001 = Dot3D(seed, x0, y0, z0 + 1, fx, fy, fz - 1.0f);
        float n101 = Dot3D(seed, x0 + 1, y0, z0 + 1, fx - 1.0f, fy, fz - 1.0f);
        float n011 = Dot3D(seed, x0, y0 + 1, z0 + 1, fx, fy - 1.0f, fz - 1.0f);
        float n111 = Dot3D(seed, x0 + 1, y0 + 1, z0 + 1, fx - 1.0f, fy - 1.0f, fz - 1.0f);

        float x00 = Lerp(n000, n100, u);
        float x10 = Lerp(n010, n110, u);
        float x01 = Lerp(n001, n101, u);
        float x11 = Lerp(n011, n111, u);

        return Lerp(Lerp(x00, x10, v), Lerp(x01, x11, v), w);
    }

    /// <summary>Summed octaves of <see cref="Sample2D"/>, normalised to roughly [-1, 1].</summary>
    public static float Fbm2D(ulong seed, float x, float y, int octaves, float lacunarity, float gain)
    {
        float sum = 0.0f;
        float amplitude = 1.0f;
        float totalAmplitude = 0.0f;
        float frequency = 1.0f;

        for (int i = 0; i < octaves; i++)
        {
            // Each octave gets its own seed, so octaves are independent rather than the
            // same field sampled at different scales — which would correlate their peaks.
            sum += Sample2D(Hash64.Mix(seed + (ulong)i), x * frequency, y * frequency) * amplitude;
            totalAmplitude += amplitude;
            amplitude *= gain;
            frequency *= lacunarity;
        }

        return totalAmplitude > 0.0f ? sum / totalAmplitude : 0.0f;
    }

    /// <summary>Summed octaves of <see cref="Sample3D"/>, normalised to roughly [-1, 1].</summary>
    public static float Fbm3D(ulong seed, float x, float y, float z, int octaves, float lacunarity, float gain)
    {
        float sum = 0.0f;
        float amplitude = 1.0f;
        float totalAmplitude = 0.0f;
        float frequency = 1.0f;

        for (int i = 0; i < octaves; i++)
        {
            sum += Sample3D(
                Hash64.Mix(seed + (ulong)i),
                x * frequency,
                y * frequency,
                z * frequency) * amplitude;
            totalAmplitude += amplitude;
            amplitude *= gain;
            frequency *= lacunarity;
        }

        return totalAmplitude > 0.0f ? sum / totalAmplitude : 0.0f;
    }

    private static float Dot2D(ulong seed, int ix, int iy, float dx, float dy)
    {
        (float gx, float gy) = Gradients2D[(int)(Hash64.Combine(seed, ix, iy) & 7)];
        return (gx * dx) + (gy * dy);
    }

    private static float Dot3D(ulong seed, int ix, int iy, int iz, float dx, float dy, float dz)
    {
        ulong h = Hash64.Combine(seed, ix, iy, iz);
        (float gx, float gy, float gz) = Gradients3D[(int)(h & 15)];
        return (gx * dx) + (gy * dy) + (gz * dz);
    }

    /// <summary>
    /// Floor toward negative infinity. <c>(int)</c> truncates toward zero, which would
    /// make the lattice cell at −0.5 and +0.5 the same cell and mirror the world across
    /// the origin.
    /// </summary>
    private static int FloorToInt(float value)
    {
        int truncated = (int)value;
        return value < truncated ? truncated - 1 : truncated;
    }
}
