using System.Numerics;
using MinerCraft.Core.Voxels;

namespace MinerCraft.Core.Physics;

/// <summary>
/// An axis-aligned box in world metres — the shape everything that walks through the voxel
/// grid is approximated by.
/// </summary>
/// <remarks>
/// <para>
/// <b>Stored as a minimum corner and a size, not as two corners.</b> That is not a taste
/// call, it is what makes standing still stable. <see cref="VoxelCollider"/> resolves a
/// collision by placing one face on a voxel plane and deriving the other from the size; if
/// the size were itself derived — <c>Max − Min</c>, recomputed every step — each resolve
/// would round it slightly differently and a resting player would creep. With the size
/// stored, a resolve is a pure function of the blocking voxel's index and the constants, so
/// the same contact produces bitwise the same box every step, forever.
/// </para>
/// <para>
/// <b>A box occupies the half-open interval <c>[Min, Max)</c>.</b> A face lying exactly on a
/// voxel boundary is touching that voxel, not inside it. This is the rule that lets a player
/// rest on a floor without colliding with it: the box's <c>Min.Y</c> sits exactly on the
/// floor's top plane, so the floor voxel is not in <see cref="VoxelBounds"/> and a horizontal
/// move does not snag on the ground it is standing on. Make the test inclusive at both ends
/// and walking becomes impossible.
/// </para>
/// <para>
/// <see cref="float"/> rather than <see cref="double"/>, matching
/// <see cref="System.Numerics.Vector3"/> and the rest of the project. The honest cost:
/// contact resolution is exact only while one ULP stays well under
/// <see cref="VoxelCollider.ContactEpsilonMetres"/>, which holds out to roughly ±800 m from
/// the origin and degrades gradually past it. <b>Recorded revisit trigger:</b> a world
/// meant to be walked further than a few kilometres from the origin wants a floating origin
/// — which is what every large-world engine ends up building — not a wider float here,
/// because <see cref="VoxelPos"/> is <see cref="int"/> and would run out long before
/// <see cref="double"/> did.
/// </para>
/// </remarks>
public readonly record struct Aabb
{
    /// <summary>Builds a box from its minimum corner and its extent, both in metres.</summary>
    /// <exception cref="ArgumentException">
    /// The size is not finite and strictly positive on all three axes. A zero-sized box
    /// overlaps nothing, so it would pass straight through the world reporting no collision —
    /// a silent wrong answer rather than a loud one.
    /// </exception>
    public Aabb(Vector3 min, Vector3 size)
    {
        if (!float.IsFinite(size.X) || !float.IsFinite(size.Y) || !float.IsFinite(size.Z)
            || size.X <= 0.0f || size.Y <= 0.0f || size.Z <= 0.0f)
        {
            throw new ArgumentException(
                $"A box needs a finite, strictly positive size on every axis; got {size}. "
                + "A degenerate box overlaps no voxel and would move through solid rock "
                + "reporting nothing.",
                nameof(size));
        }

        Min = min;
        Size = size;
    }

    /// <summary>The minimum corner, in metres. Inclusive.</summary>
    public Vector3 Min { get; }

    /// <summary>The extent along each axis, in metres. Preserved exactly by every resolve.</summary>
    public Vector3 Size { get; }

    /// <summary>The maximum corner, in metres. <b>Exclusive</b> — see the type's remarks.</summary>
    public Vector3 Max => Min + Size;

    /// <summary>The geometric centre, in metres.</summary>
    public Vector3 Centre => Min + (Size * 0.5f);

    /// <summary>
    /// The point directly between the box's feet: horizontally centred, vertically at the
    /// bottom face. The natural anchor for a standing body, and the inverse of
    /// <see cref="FromFeet"/>.
    /// </summary>
    public Vector3 FeetCentre =>
        new(Min.X + (Size.X * 0.5f), Min.Y, Min.Z + (Size.Z * 0.5f));

    /// <summary>
    /// A square-footprint body of <paramref name="widthMetres"/> across and
    /// <paramref name="heightMetres"/> tall, standing with its feet at
    /// <paramref name="feetCentreMetres"/>.
    /// </summary>
    /// <remarks>
    /// The feet, not the centre, because that is the coordinate every other system means by
    /// "where the player is": the spawn point <c>WorldGenerator.SurfaceHeightAt</c> returns,
    /// the voxel column the streamer is asked to load, and the point a step-up measures from.
    /// Deriving the corner at each of those call sites is how a half-height offset goes
    /// missing in one of them.
    /// </remarks>
    public static Aabb FromFeet(Vector3 feetCentreMetres, float widthMetres, float heightMetres)
    {
        float half = widthMetres * 0.5f;
        return new Aabb(
            new Vector3(feetCentreMetres.X - half, feetCentreMetres.Y, feetCentreMetres.Z - half),
            new Vector3(widthMetres, heightMetres, widthMetres));
    }

    /// <summary>The same box moved by <paramref name="deltaMetres"/>. Size untouched.</summary>
    public Aabb Translated(Vector3 deltaMetres) => new(Min + deltaMetres, Size);

    /// <summary>
    /// The inclusive range of voxels this box overlaps, under the half-open rule.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The low end reuses <see cref="VoxelPos.FromMetres"/> rather than re-deriving a floor,
    /// for the reason <c>Carve.Bounds</c> gives: a second flooring primitive is a second
    /// chance to get negative coordinates wrong, and the first one is already tested.
    /// </para>
    /// <para>
    /// The high end cannot reuse it. It is <c>ceil(x) − 1</c>, not <c>floor(x)</c>, and the
    /// two differ in exactly the case that matters — a face lying exactly on a voxel plane.
    /// <c>floor</c> would include the voxel beyond that plane, which is the floor a resting
    /// player stands on, and horizontal movement would then collide with the ground.
    /// </para>
    /// </remarks>
    public (VoxelPos Min, VoxelPos Max) VoxelBounds
    {
        get
        {
            Vector3 max = Max;
            return (
                VoxelPos.FromMetres(Min.X, Min.Y, Min.Z),
                new VoxelPos(LastIndex(max.X), LastIndex(max.Y), LastIndex(max.Z)));
        }
    }

    /// <summary>
    /// The index of the last voxel an interval ending at <paramref name="highMetres"/>
    /// overlaps. See <see cref="VoxelBounds"/> for why this is not a floor.
    /// </summary>
    internal static int LastIndex(float highMetres) =>
        (int)MathF.Ceiling(highMetres * VoxelConstants.VoxelsPerMetre) - 1;

    /// <summary>This box with <see cref="Min"/> replaced on one axis. 0 = X, 1 = Y, 2 = Z.</summary>
    internal Aabb WithMinOn(int axis, float valueMetres) => axis switch
    {
        0 => new Aabb(new Vector3(valueMetres, Min.Y, Min.Z), Size),
        1 => new Aabb(new Vector3(Min.X, valueMetres, Min.Z), Size),
        _ => new Aabb(new Vector3(Min.X, Min.Y, valueMetres), Size),
    };

    /// <summary>
    /// This box with <see cref="Max"/> placed on one axis, the size carried across unchanged.
    /// </summary>
    internal Aabb WithMaxOn(int axis, float valueMetres) =>
        WithMinOn(axis, valueMetres - Component(Size, axis));

    /// <summary>One component of a vector by axis index. 0 = X, 1 = Y, 2 = Z.</summary>
    internal static float Component(Vector3 vector, int axis) => axis switch
    {
        0 => vector.X,
        1 => vector.Y,
        _ => vector.Z,
    };

    /// <summary>One component of a voxel position by axis index. 0 = X, 1 = Y, 2 = Z.</summary>
    internal static int Component(VoxelPos voxel, int axis) => axis switch
    {
        0 => voxel.X,
        1 => voxel.Y,
        _ => voxel.Z,
    };

    /// <summary>A vector that is <paramref name="valueMetres"/> on one axis and zero elsewhere.</summary>
    internal static Vector3 OnAxis(int axis, float valueMetres) => axis switch
    {
        0 => new Vector3(valueMetres, 0.0f, 0.0f),
        1 => new Vector3(0.0f, valueMetres, 0.0f),
        _ => new Vector3(0.0f, 0.0f, valueMetres),
    };

    public override string ToString() => $"Aabb(min {Min}, size {Size})";
}
