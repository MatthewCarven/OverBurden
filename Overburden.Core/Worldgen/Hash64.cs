using Overburden.Core.Voxels;

namespace Overburden.Core.Worldgen;

/// <summary>
/// The world's source of determinism: a stable, explicitly-written 64-bit hash.
/// </summary>
/// <remarks>
/// <para>
/// <b>Do not replace this with <see cref="HashCode"/>.</b> .NET randomises
/// <c>HashCode.Combine</c>'s seed per process, so a world seeded through it would generate
/// <i>different terrain every launch</i> — while looking perfectly correct within any
/// single session, because nothing inside one run disagrees with itself. It fails only
/// across restarts, which is exactly when a player would notice their world had changed.
/// </para>
/// <para>
/// The same rules out <c>string.GetHashCode</c>, <c>Random</c> without an explicit seed,
/// and anything else whose values the framework is free to change between versions. What
/// goes in here has to be arithmetic we wrote down, and
/// <c>KnownInputsHashToKnownOutputs</c> pins it against exactly that.
/// </para>
/// <para>
/// splitmix64, from Steele et al.'s <i>Fast Splittable Pseudorandom Number Generators</i>.
/// Chosen because it is four lines, passes the usual statistical batteries, and has no
/// state to carry between calls — which is what makes per-chunk generation order-independent.
/// </para>
/// </remarks>
public static class Hash64
{
    private const ulong GoldenGamma = 0x9E3779B97F4A7C15UL;

    /// <summary>Avalanches a single 64-bit value.</summary>
    public static ulong Mix(ulong value)
    {
        ulong z = value + GoldenGamma;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Hash of a seed and three signed coordinates.</summary>
    /// <remarks>
    /// Coordinates are cast through <c>uint</c> so negative positions map onto distinct
    /// bit patterns rather than sign-extending into a wall of ones — worlds extend in both
    /// directions and the negative half must be as well distributed as the positive.
    /// </remarks>
    /// <remarks>
    /// One avalanche round over multiply-mixed coordinates, not four chained rounds. The
    /// chained version was measured at 84% of chunk generation time, because 3D noise hashes
    /// eight lattice corners per octave per voxel — so this sits in the innermost loop the
    /// generator has. Odd multipliers make each coordinate's contribution invertible, and
    /// splitmix64's finalizer then avalanches the combination, which is ample for choosing
    /// among sixteen gradients.
    /// </remarks>
    public static ulong Combine(ulong seed, int x, int y, int z) => Mix(
        seed
        ^ 0x632BE59BD9B4E019UL
        ^ (0xC2B2AE3D27D4EB4FUL * (uint)x)
        ^ (0x165667B19E3779F9UL * (uint)y)
        ^ (0x27D4EB2F165667C5UL * (uint)z));

    /// <summary>Two-dimensional variant, for heightmaps and anything else column-wise.</summary>
    public static ulong Combine(ulong seed, int x, int z) => Combine(seed, x, 0, z);

    public static ulong ForChunk(ulong seed, ChunkPos chunk) =>
        Combine(seed, chunk.X, chunk.Y, chunk.Z);

    /// <summary>Uniform float in [0, 1).</summary>
    /// <remarks>
    /// Takes the top 24 bits, which are the best-mixed, and scales by 2^-24 — exactly
    /// representable, so this never rounds up to 1.0f and callers can rely on the
    /// half-open range.
    /// </remarks>
    public static float ToUnitFloat(ulong hash) => (hash >> 40) * (1.0f / 16777216.0f);

    /// <summary>Uniform float in [-1, 1).</summary>
    public static float ToSignedFloat(ulong hash) => (ToUnitFloat(hash) * 2.0f) - 1.0f;
}
