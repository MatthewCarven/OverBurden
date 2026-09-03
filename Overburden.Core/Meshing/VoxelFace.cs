namespace Overburden.Core.Meshing;

/// <summary>
/// The six axis-aligned face directions, and the in-plane axes the mesher sweeps them on.
/// </summary>
/// <remarks>
/// <para>Order matches the M0 face table: 0=+X, 1=-X, 2=+Y, 3=-Y, 4=+Z, 5=-Z.</para>
///
/// <para><b>The U and V axes are not arbitrary.</b> They are chosen per face so that
/// <c>du × dv = +normal</c>, which makes the corner sequence
/// <c>p, p+du, p+du+dv, p+dv</c> counter-clockwise as seen from outside the solid — the
/// convention M0 established and that was confirmed on screen. Swap a pair and that face
/// renders inside-out while the other five stay correct, which is exactly the sort of bug
/// that is hard to spot and easy to prevent, so
/// <c>GreedyMesherTests.EveryQuadIsWoundConsistentlyWithItsNormal</c> pins it.</para>
/// </remarks>
public static class VoxelFace
{
    public const int Count = 6;

    /// <summary>Axis the normal lies along: 0 = X, 1 = Y, 2 = Z.</summary>
    public static readonly int[] Axis = [0, 0, 1, 1, 2, 2];

    /// <summary>+1 for the positive-facing side, -1 for the negative.</summary>
    public static readonly int[] Direction = [1, -1, 1, -1, 1, -1];

    /// <summary>In-plane axis the greedy sweep merges along first (quad width).</summary>
    public static readonly int[] UAxis = [1, 2, 2, 0, 0, 1];

    /// <summary>In-plane axis merged second (quad height).</summary>
    public static readonly int[] VAxis = [2, 1, 0, 2, 1, 0];

    /// <summary>Outward normal as (x, y, z).</summary>
    public static readonly float[][] Normal =
    [
        [1f, 0f, 0f],
        [-1f, 0f, 0f],
        [0f, 1f, 0f],
        [0f, -1f, 0f],
        [0f, 0f, 1f],
        [0f, 0f, -1f],
    ];

    /// <summary>Neighbour offset to test for visibility, as (x, y, z).</summary>
    public static readonly int[][] Offset =
    [
        [1, 0, 0],
        [-1, 0, 0],
        [0, 1, 0],
        [0, -1, 0],
        [0, 0, 1],
        [0, 0, -1],
    ];
}
