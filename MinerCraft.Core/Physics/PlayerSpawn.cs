using System;
using System.Numerics;
using MinerCraft.Core.Content;
using MinerCraft.Core.Voxels;

namespace MinerCraft.Core.Physics;

/// <summary>
/// Where a body enters the world, whether the world is ready to receive it, and what to do
/// when it finds itself inside rock.
/// </summary>
/// <remarks>
/// <para>
/// Three small rules that all exist because of the cycle at the top of M4-PLAN: the player's
/// position is an output of a query against the chunks the player decides to load, and at the
/// first frame nothing is loaded. An unloaded voxel is solid, so at frame zero <em>every</em>
/// voxel is solid and a body dropped in is sealed in rock in every direction.
/// </para>
/// <para>
/// <b>The order matters and is not interchangeable.</b> <see cref="IsWorldReadyFor"/> gates
/// <see cref="TryUnstick"/>, never the other way round. Unsticking against an unloaded world
/// finds solid at every height, runs to its lift bound, fails, and reports a body stuck in
/// open sky — which reads as the spawn being broken rather than as the world not having
/// arrived yet.
/// </para>
/// <para>
/// In Core rather than in the controller because these are world rules, not input state.
/// M6's placement can put a block inside the player deliberately and M9's collapse can do it
/// by accident; both want the same answer to "the box is inside solid, now what", and a
/// multiplayer server would need to agree with the client about it.
/// </para>
/// </remarks>
public static class PlayerSpawn
{
    /// <summary>How far <see cref="TryUnstick"/> will lift a body before giving up, in metres.</summary>
    /// <remarks>
    /// <b>An estimate.</b> 4 m is 16 voxels and a little over two body heights — far enough to
    /// climb out of the roof of a cave whose mouth happens to sit on the spawn column, which is
    /// the case this exists for, and short enough that a body which is genuinely buried gets a
    /// reported failure rather than a silent teleport to the surface. Nothing measured it.
    /// </remarks>
    public const float DefaultMaxUnstickMetres = 4.0f;

    /// <summary>
    /// Feet placed on top of the column's surface voxel.
    /// </summary>
    /// <param name="surfaceTopRow">
    /// The topmost solid voxel row, as <c>WorldGenerator.SurfaceHeightAt</c> returns it.
    /// </param>
    /// <param name="xMetres">Horizontal position of the spawn column.</param>
    /// <param name="zMetres">Horizontal position of the spawn column.</param>
    /// <remarks>
    /// <para>
    /// The row <em>above</em> the top solid voxel, so the feet land exactly on a voxel plane.
    /// That is not cosmetic: M4b records that a body resting on a plane is the case which
    /// distinguishes a snapped step-up lift from an accumulated one, and it is pinned by a
    /// test there. A spawn that placed the feet a fraction above the plane would rest at a
    /// fraction above it forever, and quietly stop exercising that case.
    /// </para>
    /// <para>
    /// <c>SurfaceHeightAt</c> is the surface <em>before caves are carved</em>, so this point can
    /// be over open air — which is exactly why <see cref="TryUnstick"/> exists and why the body
    /// falls rather than being asserted onto ground.
    /// </para>
    /// </remarks>
    public static Vector3 FeetOnSurface(int surfaceTopRow, float xMetres, float zMetres) =>
        new(xMetres, (surfaceTopRow + 1) * VoxelConstants.VoxelSize, zMetres);

    /// <summary>
    /// Whether enough of the world exists under and around <paramref name="box"/> for a step
    /// to mean anything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every chunk the box overlaps, <b>and the chunk beneath each of them</b>. The box's own
    /// chunks because an unloaded one is solid and would pin the body in place; the layer below
    /// because that is the floor, and standing on an unloaded chunk is standing on nothing that
    /// will still be there once it arrives — the body would hold, then drop.
    /// </para>
    /// <para>
    /// Asked of <see cref="IVoxelWorld"/> by probing one voxel per chunk rather than of the
    /// streamer by inspecting chunk states, so it is expressible against any world and
    /// testable without a pipeline. Any voxel in a chunk answers the same question, because
    /// <c>ChunkStreamer.TryGetMaterial</c>'s false means "this chunk has no voxels yet".
    /// </para>
    /// </remarks>
    public static bool IsWorldReadyFor(IVoxelWorld world, Aabb box)
    {
        ArgumentNullException.ThrowIfNull(world);

        (VoxelPos min, VoxelPos max) = box.VoxelBounds;
        ChunkPos low = min.Chunk;
        ChunkPos high = max.Chunk;

        for (int z = low.Z; z <= high.Z; z++)
        for (int x = low.X; x <= high.X; x++)
        for (int y = low.Y - 1; y <= high.Y; y++)
        {
            if (!world.TryGetMaterial(new ChunkPos(x, y, z).OriginVoxel, out _))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Lifts a box that starts inside solid material until it is clear.
    /// </summary>
    /// <param name="world">The world as loaded. An unloaded voxel counts as solid.</param>
    /// <param name="materials">The table the solidity predicate reads.</param>
    /// <param name="box">Where the body is now.</param>
    /// <param name="maxLiftMetres">How far up to look before giving up.</param>
    /// <param name="freed">Where it ended up. Unchanged from <paramref name="box"/> on failure.</param>
    /// <returns>True if the box is now clear of solid material — including if it already was.</returns>
    /// <remarks>
    /// <para>
    /// <b>A shove, not a lock.</b> Refusing to move a body that is inside rock is the failure
    /// that reads as the game being broken; lifting it out reads as a hiccup. That is the whole
    /// argument, and it is why this returns the nearest legal standing position upward rather
    /// than the nearest in any direction — up is the one direction where the answer is
    /// unambiguous and where the body does not end up somewhere it could not have walked to.
    /// </para>
    /// <para>
    /// <b>Candidates are voxel planes, and the box is rebuilt rather than translated.</b>
    /// Stepping plane to plane makes the search finite by construction and lands the feet
    /// exactly on a plane, which is the resting position M4b's tests depend on. Rebuilding from
    /// the untouched <c>Min.X</c>, <c>Min.Z</c> and <c>Size</c> — rather than calling
    /// <see cref="Aabb.Translated"/> with a computed delta — is M4a's corner-and-size rule: a
    /// lift must not perturb the horizontal position by a rounding error.
    /// </para>
    /// <para>
    /// Callers must gate this on <see cref="IsWorldReadyFor"/>. Against an unloaded world every
    /// candidate overlaps solid and this reports failure for a body standing in open sky.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The lift bound is not positive and finite.</exception>
    public static bool TryUnstick(
        IVoxelWorld world,
        MaterialTable materials,
        Aabb box,
        float maxLiftMetres,
        out Aabb freed)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(materials);

        if (!float.IsFinite(maxLiftMetres) || maxLiftMetres <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxLiftMetres),
                maxLiftMetres,
                "The lift bound is what makes the search terminate. A non-positive bound "
                + "would refuse every body that needs unsticking; a non-finite one would "
                + "search upward forever.");
        }

        freed = box;

        if (!VoxelCollider.OverlapsSolid(world, materials, box))
        {
            return true;
        }

        float ceiling = box.Min.Y + maxLiftMetres;

        // The floor of the current bottom face, from the one flooring primitive the project
        // has — a second one is a second chance to get negative coordinates wrong. Starting at
        // +1 rather than at the current row is deliberate: the current row is where the box
        // already is, and it overlaps.
        int row = VoxelPos.FromMetres(box.Min.X, box.Min.Y, box.Min.Z).Y + 1;

        for (; ; row++)
        {
            float y = row * VoxelConstants.VoxelSize;
            if (y > ceiling)
            {
                return false;
            }

            var lifted = new Aabb(new Vector3(box.Min.X, y, box.Min.Z), box.Size);
            if (!VoxelCollider.OverlapsSolid(world, materials, lifted))
            {
                freed = lifted;
                return true;
            }
        }
    }

    /// <summary>Unsticks with <see cref="DefaultMaxUnstickMetres"/>.</summary>
    public static bool TryUnstick(
        IVoxelWorld world,
        MaterialTable materials,
        Aabb box,
        out Aabb freed) =>
        TryUnstick(world, materials, box, DefaultMaxUnstickMetres, out freed);
}
