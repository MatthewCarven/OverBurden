using System.Numerics;

namespace Overburden.Core.Voxels;

/// <summary>
/// Which voxels a sphere of a given world-space radius covers. One definition, two verbs.
/// </summary>
/// <remarks>
/// <para>
/// <b>Shared by <c>Mining.Carve</c> and <c>Building.Place</c> deliberately.</b> A sphere carved
/// and a sphere placed at the same radius and centre have to be the same set of voxels, or
/// filling in a hole you just made leaves a rind of stragglers — and that failure is invisible
/// until somebody tries it, by which time the two copies have been drifting for a milestone. It
/// is the same argument <c>WorldGenerator</c> makes about <c>IsCaveAt</c> and
/// <c>GenerateTerrain</c> reading one definition of the cave field.
/// </para>
/// <para>
/// Extracted from <c>Carve</c> at M6c-1 rather than written fresh, so the membership fuzz that
/// has covered it since M3c covers both callers without being duplicated either.
/// </para>
/// </remarks>
internal static class VoxelSphere
{
    /// <summary>
    /// Whether <paramref name="voxel"/>'s <b>centre</b> lies inside the sphere. The definition.
    /// </summary>
    /// <remarks>
    /// Squared radius rather than radius, because the caller hoists the multiplication out of a
    /// cubic loop and because a square root would be the only irrational step in an otherwise
    /// exact test.
    /// </remarks>
    internal static bool Contains(VoxelPos voxel, Vector3 centreMetres, float radiusSquared)
    {
        (float x, float y, float z) = voxel.CentreMetres;

        float dx = x - centreMetres.X;
        float dy = y - centreMetres.Y;
        float dz = z - centreMetres.Z;

        return ((dx * dx) + (dy * dy) + (dz * dz)) <= radiusSquared;
    }

    /// <summary>
    /// The smallest voxel box that certainly contains every voxel <see cref="Contains"/> would
    /// keep. An optimisation, and nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The sphere's axis-aligned bounding box in metres is <c>centre ± radius</c>, and a
    /// candidate's centre lies inside it, so flooring both ends into voxel coordinates is
    /// sufficient: <see cref="VoxelPos.FromMetres"/> floors and is monotone, and a voxel
    /// centre at <c>(i + 0.5) × VoxelSize</c> can never sit exactly on a voxel boundary, so
    /// the rounding would have to be wrong by half a voxel to drop a candidate.
    /// </para>
    /// <para>
    /// <b>Reusing <see cref="VoxelPos.FromMetres"/> rather than deriving the tight bound is
    /// the point.</b> A hand-rolled <c>ceil((c − r)/s − 0.5)</c> is a second flooring
    /// primitive that has to be got right for negative coordinates all over again, and — this
    /// was measured — the obvious mutation of it, swapping <c>Math.Ceiling</c>/<c>Math.Floor</c>
    /// for casts, can only ever <em>widen</em> the box, so it is a break-recipe that cannot
    /// fail a test. This version is falsifiable: narrowing either end by one voxel breaks the
    /// membership fuzz within milliseconds, at negative coordinates first.
    /// </para>
    /// </remarks>
    internal static (VoxelPos Min, VoxelPos Max) Bounds(Vector3 centre, float radius) => (
        VoxelPos.FromMetres(centre.X - radius, centre.Y - radius, centre.Z - radius),
        VoxelPos.FromMetres(centre.X + radius, centre.Y + radius, centre.Z + radius));
}
