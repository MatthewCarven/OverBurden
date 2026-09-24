using Overburden.Core.Voxels;

namespace Overburden.Core.Fluid;

/// <summary>
/// The sheet solve's seed order — (Y, X, Z) ascending — as one packed <see cref="long"/> a
/// position, so the sort is a primitive sort with no delegate.
/// </summary>
/// <remarks>
/// <para>
/// <b>The order is the comparator's exactly, and that is the whole contract.</b> M10d-3 measured
/// the three-key delegate sort at ~320 ns a seed and 29% of a pass on the load test (683 k seeds,
/// 220 ms); the solve reads the order for two things only — seeds grouped by Y bottom-up, and a
/// total order so the same water solves the same way whichever cell woke first — and a key that
/// sorts identically keeps both, so every pass is bitwise the pass it was.
/// </para>
/// <para>
/// <b>Each coordinate is biased by 2^20 into 21 bits</b>, Y in the top field, then X, then Z, and
/// the key stays non-negative. The cost is a range of ±1,048,575 voxels — ±262 km, two orders past
/// the few kilometres M4a's float positions allow — and a position outside it makes the caller
/// fall back to the comparator for that pass, counted rather than thrown.
/// </para>
/// </remarks>
public static class SeedOrder
{
    public const int Bits = 21;
    public const int Bias = 1 << (Bits - 1);
    public const int Min = -Bias;
    public const int Max = Bias - 1;
    private const long Mask = (1L << Bits) - 1;

    /// <summary>Whether a position packs; every coordinate in [<see cref="Min"/>, <see cref="Max"/>].</summary>
    public static bool Fits(VoxelPos p) =>
        (uint)(p.X + Bias) <= (uint)Mask && (uint)(p.Y + Bias) <= (uint)Mask && (uint)(p.Z + Bias) <= (uint)Mask;

    /// <summary>The key. Only for a position that <see cref="Fits"/>.</summary>
    public static long Pack(VoxelPos p) =>
        ((long)(p.Y + Bias) << (2 * Bits)) | ((long)(p.X + Bias) << Bits) | (long)(p.Z + Bias);

    public static VoxelPos Unpack(long key) => new(
        (int)((key >> Bits) & Mask) - Bias,
        (int)((key >> (2 * Bits)) & Mask) - Bias,
        (int)(key & Mask) - Bias);

    /// <summary>The comparator the key replaces, kept as the fallback and as the tests' oracle.</summary>
    public static int Compare(VoxelPos a, VoxelPos b) =>
        a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.X != b.X ? a.X.CompareTo(b.X) : a.Z.CompareTo(b.Z);
}
