using System.Numerics;
using Overburden.Core.Content;

namespace Overburden.Core.Voxels;

/// <summary>Reads single voxels by world position, for things that walk the grid.</summary>
/// <remarks>
/// <see cref="TryGetMaterial"/> returning false means "this voxel is not loaded", which is a
/// different answer from "this voxel is air" and has to stay different — see
/// <see cref="RayOutcome.Blocked"/>.
/// </remarks>
public interface IVoxelWorld
{
    bool TryGetMaterial(VoxelPos voxel, out int materialId);
}

/// <summary>How a cast ended.</summary>
public enum RayOutcome
{
    /// <summary>Nothing solid within range. The world was loaded the whole way.</summary>
    Missed,

    /// <summary>Found a voxel that is not air.</summary>
    Hit,

    /// <summary>
    /// Ran into a chunk that is not loaded, so the answer is unknown rather than "nothing".
    /// </summary>
    /// <remarks>
    /// Treating this as a miss would let the player mine through the edge of the loaded world,
    /// or aim at a voxel in a chunk that does not exist — where the edit then vanishes with
    /// nothing to report. The caller decides what to do; the cast refuses to guess.
    /// </remarks>
    Blocked,
}

/// <summary>What a cast found.</summary>
/// <param name="Voxel">The voxel hit, or the last one examined when nothing was hit.</param>
/// <param name="Previous">
/// The empty voxel the ray was in immediately before <paramref name="Voxel"/> — where a
/// placement goes at M6. Equal to <paramref name="Voxel"/> when the ray began inside solid
/// material.
/// </param>
/// <param name="Normal">
/// The face the ray entered through, as a unit step: <c>(-1, 0, 0)</c> for the −X face and so
/// on. Zero when the ray began inside solid material, because it entered no face.
/// </param>
/// <param name="DistanceMetres">Distance from the ray origin to where it entered the voxel.</param>
public readonly record struct VoxelHit(
    VoxelPos Voxel,
    VoxelPos Previous,
    (int X, int Y, int Z) Normal,
    float DistanceMetres);

/// <summary>
/// Walks a ray through the voxel grid, visiting every voxel it passes through in order.
/// </summary>
/// <remarks>
/// <para>
/// Amanatides &amp; Woo, which is exact: it visits the voxels the ray actually crosses, never
/// skipping one and never visiting one twice, at two additions and a comparison per step.
/// Sampling the ray at small intervals instead — the obvious alternative — misses voxels
/// thinner than the step and revisits others, and the failure looks like a pick that
/// occasionally refuses to break a wall.
/// </para>
/// <para>
/// <b>Stops at the first voxel that is not air.</b> Fluids do not exist until M8, at which
/// point "solid" and "not air" stop meaning the same thing and this wants a stop predicate
/// rather than a comparison against <see cref="MaterialIds.AirNumericId"/>. That is the
/// recorded revisit trigger.
/// </para>
/// </remarks>
public static class VoxelRay
{
    /// <summary>
    /// Casts from <paramref name="originMetres"/> along <paramref name="direction"/>.
    /// </summary>
    /// <param name="direction">Need not be normalised; a zero vector throws.</param>
    /// <param name="maxDistanceMetres">How far to walk before giving up.</param>
    public static RayOutcome Cast(
        IVoxelWorld world,
        Vector3 originMetres,
        Vector3 direction,
        float maxDistanceMetres,
        out VoxelHit hit)
    {
        ArgumentNullException.ThrowIfNull(world);

        float length = direction.Length();
        if (length == 0.0f || !float.IsFinite(length))
        {
            throw new ArgumentException("A ray needs a finite, non-zero direction.", nameof(direction));
        }

        Vector3 unit = direction / length;

        var voxel = VoxelPos.FromMetres(originMetres.X, originMetres.Y, originMetres.Z);
        VoxelPos previous = voxel;
        (int X, int Y, int Z) normal = (0, 0, 0);
        float travelled = 0.0f;

        int stepX = StepOf(unit.X);
        int stepY = StepOf(unit.Y);
        int stepZ = StepOf(unit.Z);

        // Distance along the ray to the first boundary crossing on each axis, and the
        // distance between crossings after that. Both are infinite on an axis the ray does
        // not move along, which is what keeps that axis from ever being chosen as the
        // nearest — no special case, just a comparison that can never win.
        float tMaxX = FirstBoundary(originMetres.X, unit.X, voxel.X);
        float tMaxY = FirstBoundary(originMetres.Y, unit.Y, voxel.Y);
        float tMaxZ = FirstBoundary(originMetres.Z, unit.Z, voxel.Z);

        float tDeltaX = StepLength(unit.X);
        float tDeltaY = StepLength(unit.Y);
        float tDeltaZ = StepLength(unit.Z);

        while (true)
        {
            if (!world.TryGetMaterial(voxel, out int material))
            {
                hit = new VoxelHit(voxel, previous, normal, travelled);
                return RayOutcome.Blocked;
            }

            if (material != MaterialIds.AirNumericId)
            {
                hit = new VoxelHit(voxel, previous, normal, travelled);
                return RayOutcome.Hit;
            }

            previous = voxel;

            // Step along whichever axis reaches its next boundary first. The face entered is
            // the one facing back the way we came, hence the negated step.
            if (tMaxX <= tMaxY && tMaxX <= tMaxZ)
            {
                travelled = tMaxX;
                tMaxX += tDeltaX;
                voxel = voxel.Offset(stepX, 0, 0);
                normal = (-stepX, 0, 0);
            }
            else if (tMaxY <= tMaxZ)
            {
                travelled = tMaxY;
                tMaxY += tDeltaY;
                voxel = voxel.Offset(0, stepY, 0);
                normal = (0, -stepY, 0);
            }
            else
            {
                travelled = tMaxZ;
                tMaxZ += tDeltaZ;
                voxel = voxel.Offset(0, 0, stepZ);
                normal = (0, 0, -stepZ);
            }

            if (travelled > maxDistanceMetres)
            {
                hit = new VoxelHit(previous, previous, normal, travelled);
                return RayOutcome.Missed;
            }
        }
    }

    private static int StepOf(float component) => component switch
    {
        > 0.0f => 1,
        < 0.0f => -1,
        _ => 0,
    };

    /// <summary>Distance along the ray from the origin to the first voxel boundary on one axis.</summary>
    private static float FirstBoundary(float origin, float direction, int voxel)
    {
        if (direction == 0.0f)
        {
            return float.PositiveInfinity;
        }

        float minCorner = voxel * VoxelConstants.VoxelSize;
        float target = direction > 0.0f ? minCorner + VoxelConstants.VoxelSize : minCorner;

        // Non-negative either way: going forward the far corner is ahead, going backward the
        // near corner is behind and so is the negative direction, and the two signs cancel.
        return (target - origin) / direction;
    }

    private static float StepLength(float direction) =>
        direction == 0.0f
            ? float.PositiveInfinity
            : VoxelConstants.VoxelSize / MathF.Abs(direction);
}
