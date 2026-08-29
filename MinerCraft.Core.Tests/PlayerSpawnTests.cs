using System.Numerics;
using MinerCraft.Core.Physics;
using MinerCraft.Core.Voxels;

namespace MinerCraft.Core.Tests;

/// <summary>
/// The three rules that get a body into the world: where it lands, whether the world is there
/// yet, and what to do when it lands inside rock.
/// </summary>
/// <remarks>
/// <para>
/// <b>Break-recipes run against the finished code, and what they caught.</b> Per the rule this
/// project has earned three times over — a green run is not evidence:
/// </para>
/// <list type="number">
/// <item><b>Unstick starting at the box's own voxel row rather than one above</b> — green,
/// four of six recipes in, and it is a real hole. Every buried-body test placed the feet on a
/// plane already, so the extra candidate was the box itself and overlapped by construction.
/// Off a plane, with a body whose <i>head</i> is what is stuck, the lower candidate can be
/// clear and the rule silently becomes "move to the nearest free plane" instead of "lift".
/// <see cref="Unstick_never_moves_a_body_downward"/> was written for it and fails on it.</item>
/// <item><c>IsWorldReadyFor</c> dropping the <c>- 1</c> on the low Y, so the chunk beneath is
/// not checked — caught, by
/// <see cref="Not_ready_when_the_chunk_beneath_the_feet_is_missing"/>, and by nothing else.</item>
/// <item><b>Unstick using <c>Aabb.Translated</c> with a computed delta instead of rebuilding
/// the box</b> — green, and after trying to construct a failing case: <b>a documentation
/// correction, not a hole</b>. <c>Min.Y + (y − Min.Y)</c> is exact for every pair of values a
/// 4 m lift can produce inside the few-kilometre world M4a's float analysis allows, and the
/// horizontal <c>+ 0.0f</c> is exact for every float except <c>−0.0f</c>, which
/// <see cref="Aabb.FromFeet"/> cannot produce. The rebuild is kept because it makes the lift
/// obviously vertical, not because a test can tell. This is the same shape as M4a's
/// "<c>ContactEpsilonMetres</c> can be zeroed with the suite green".</item>
/// <item>Unstick accepting the first candidate whether or not it is clear — caught, by five
/// tests.</item>
/// <item>The readiness gate always saying yes — caught, by three, including
/// <see cref="Unstick_fails_against_an_unloaded_world"/>, which is the one that pins the
/// gate-before-unstick order.</item>
/// <item><c>FeetOnSurface</c> using <c>surfaceTopRow</c> rather than <c>+ 1</c> — caught by
/// three, but <see cref="A_body_spawned_on_the_surface_is_already_at_rest"/> is the one that
/// matters: without it a spawn one voxel low is buried, the unstick quietly rescues it, and
/// nothing about the game looks wrong.</item>
/// </list>
/// </remarks>
public sealed class PlayerSpawnTests
{
    private static readonly PlayerMotionConfig Config = PlayerMotionConfig.Default;

    // PhysicsTestHelpers.FlatGround(): solid up to and including voxel row 3, so the top of
    // the ground is y = 1 m. Named here because every expectation below is relative to it.
    private const int GroundTopRow = 3;
    private const float GroundTopMetres = (GroundTopRow + 1) * VoxelConstants.VoxelSize;

    [Fact]
    public void Feet_land_on_the_row_above_the_surface_voxel()
    {
        Vector3 feet = PlayerSpawn.FeetOnSurface(GroundTopRow, 4.0f, -7.0f);

        Assert.Equal(4.0f, feet.X);
        Assert.Equal(-7.0f, feet.Z);
        Assert.Equal(GroundTopMetres, feet.Y);
    }

    /// <summary>
    /// The feet are exactly on a voxel plane, bitwise.
    /// </summary>
    /// <remarks>
    /// M4b records that a resting body whose feet lie exactly on a plane is what distinguishes
    /// a snapped step-up lift from an accumulated one, and pins it with a test that constructs
    /// that position by hand. This is the other end of that claim: the spawn is one of the two
    /// things in the running game that actually produces it, so a spawn that landed a ULP above
    /// the plane would leave that test the only place the case ever occurred.
    /// </remarks>
    [Fact]
    public void Feet_land_exactly_on_a_voxel_plane()
    {
        for (int row = -40; row <= 40; row++)
        {
            Vector3 feet = PlayerSpawn.FeetOnSurface(row, 0.5f, 0.5f);
            PhysicsTestHelpers.AssertSameBits(
                new Vector3(0.5f, (row + 1) * VoxelConstants.VoxelSize, 0.5f),
                feet,
                $"the feet must sit exactly on the plane above row {row}");
        }
    }

    /// <summary>
    /// A body spawned on flat ground is standing on it, not in it and not above it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The end-to-end claim the placement rule actually makes, and the only test that notices
    /// if <see cref="PlayerSpawn.FeetOnSurface"/> forgets its <c>+ 1</c>: a body spawned one
    /// row low is buried, the unstick quietly rescues it, and every other assertion here still
    /// passes.
    /// </para>
    /// <para>
    /// <b>Written expecting the first step to be bitwise identity, and that was wrong.</b> The
    /// step lifts the body by exactly <see cref="VoxelCollider.ContactEpsilonMetres"/>: gravity
    /// pushes it a fraction into the floor and the −Y resolve places it the epsilon clear. So
    /// <b>feet exactly on a voxel plane is a state that exists only at the spawn and at a mode
    /// toggle</b> — one step of anything at all and it is a tenth of a millimetre above. That is
    /// why M4b's lift test constructs the position by hand rather than walking a body into it,
    /// and it is the thing to remember before writing any other bitwise assertion about a
    /// resting body.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_body_spawned_on_the_surface_is_already_at_rest()
    {
        TestWorld world = PhysicsTestHelpers.FlatGround();
        Vector3 feet = PlayerSpawn.FeetOnSurface(GroundTopRow, 2.0f, 2.0f);
        Aabb box = Config.BoxAtFeet(feet);

        Assert.False(
            VoxelCollider.OverlapsSolid(world, PhysicsTestHelpers.Table, box),
            "a body spawned on the surface must not start inside it");

        Assert.True(
            PlayerSpawn.TryUnstick(world, PhysicsTestHelpers.Table, box, out Aabb freed),
            "and must therefore need no unsticking");

        PhysicsTestHelpers.AssertSameBits(box, freed, "an unstick that was not needed must not move the body");

        // A step of gravity finds the floor immediately, and leaves the body the contact
        // epsilon above the plane it was spawned on — not below it, and not a voxel down.
        PlayerStepResult step = PlayerMotion.Step(
            world,
            PhysicsTestHelpers.Table,
            new PlayerState(box, Vector3.Zero, Grounded: false),
            MoveIntent.Still,
            Config,
            1.0f / 60.0f);

        Assert.True(step.Grounded, "the first step must find the ground it was spawned on");
        Assert.Equal(box.Min.X, step.Box.Min.X);
        Assert.Equal(box.Min.Z, step.Box.Min.Z);
        Assert.Equal(
            GroundTopMetres + VoxelCollider.ContactEpsilonMetres,
            step.Box.Min.Y,
            tolerance: 1e-6f);

        // And the second step does not creep any further — the resting position is stable,
        // which is the property M4a's corner-and-size storage exists to give.
        PlayerStepResult settled = PlayerMotion.Step(
            world, PhysicsTestHelpers.Table, step.State, MoveIntent.Still, Config, 1.0f / 60.0f);

        PhysicsTestHelpers.AssertSameBits(
            step.Box, settled.Box, "a body at rest on its spawn must not drift");
    }

    [Fact]
    public void Ready_when_every_chunk_around_and_below_the_body_is_loaded()
    {
        TestWorld world = PhysicsTestHelpers.FlatGround();
        Aabb box = Config.BoxAtFeet(new Vector3(2.0f, GroundTopMetres, 2.0f));

        Assert.True(PlayerSpawn.IsWorldReadyFor(world, box));
    }

    /// <summary>
    /// The chunk beneath the feet counts, even though the box does not touch it.
    /// </summary>
    /// <remarks>
    /// This is the whole reason the gate is not simply "the box's own chunks". An unloaded
    /// chunk reads as solid, so a body standing on one is held up by nothing that will still be
    /// there — it stands, and then drops the moment the chunk arrives and turns out to be air.
    /// </remarks>
    [Fact]
    public void Not_ready_when_the_chunk_beneath_the_feet_is_missing()
    {
        TestWorld world = PhysicsTestHelpers.FlatGround();

        // Feet a little way up inside chunk Y = 0, so the box is comfortably clear of the
        // boundary and only the chunk *below* is missing.
        Aabb box = Config.BoxAtFeet(new Vector3(2.0f, 4.0f, 2.0f));
        Assert.True(PlayerSpawn.IsWorldReadyFor(world, box), "precondition: everything is loaded");

        world.Unload(new ChunkPos(0, -1, 0));

        Assert.False(PlayerSpawn.IsWorldReadyFor(world, box));
    }

    [Fact]
    public void Not_ready_when_a_chunk_the_body_occupies_is_missing()
    {
        TestWorld world = PhysicsTestHelpers.FlatGround();

        // Feet just below a chunk boundary, so the head is in the chunk above.
        float boundary = VoxelConstants.ChunkSizeMetres;
        Aabb box = Config.BoxAtFeet(new Vector3(2.0f, boundary - 0.5f, 2.0f));
        Assert.True(PlayerSpawn.IsWorldReadyFor(world, box), "precondition: everything is loaded");

        world.Unload(new ChunkPos(0, 1, 0));

        Assert.False(PlayerSpawn.IsWorldReadyFor(world, box));
    }

    [Fact]
    public void Unstick_leaves_a_body_that_is_already_clear_exactly_where_it_is()
    {
        TestWorld world = PhysicsTestHelpers.FlatGround();
        Aabb box = Config.BoxAtFeet(new Vector3(1.3f, 5.7f, -2.9f));

        Assert.True(PlayerSpawn.TryUnstick(world, PhysicsTestHelpers.Table, box, out Aabb freed));
        PhysicsTestHelpers.AssertSameBits(box, freed, "a clear body must not be nudged");
    }

    [Fact]
    public void Unstick_lifts_a_buried_body_to_the_top_of_the_ground()
    {
        TestWorld world = PhysicsTestHelpers.FlatGround();

        // Feet at 0.5 m is voxel row 2, two rows inside the ground.
        Aabb box = Config.BoxAtFeet(new Vector3(2.0f, 0.5f, 2.0f));
        Assert.True(
            VoxelCollider.OverlapsSolid(world, PhysicsTestHelpers.Table, box),
            "precondition: the body starts buried");

        Assert.True(PlayerSpawn.TryUnstick(world, PhysicsTestHelpers.Table, box, out Aabb freed));

        Assert.Equal(GroundTopMetres, freed.Min.Y);
        Assert.False(
            VoxelCollider.OverlapsSolid(world, PhysicsTestHelpers.Table, freed),
            "the postcondition of a successful unstick");
    }

    [Fact]
    public void Unstick_lands_the_feet_exactly_on_a_voxel_plane()
    {
        TestWorld world = PhysicsTestHelpers.FlatGround();
        Aabb box = Config.BoxAtFeet(new Vector3(1.0f / 3.0f, 0.37f, -1.0f / 7.0f));

        Assert.True(PlayerSpawn.TryUnstick(world, PhysicsTestHelpers.Table, box, out Aabb freed));

        PhysicsTestHelpers.AssertSameBits(
            new Aabb(new Vector3(box.Min.X, GroundTopMetres, box.Min.Z), box.Size),
            freed,
            "a lift is vertical, exact, and leaves the horizontal position and size untouched");
    }

    /// <summary>
    /// A gap the body cannot stand up in is not a way out.
    /// </summary>
    /// <remarks>
    /// The same claim M4b's step-up makes about headroom, at a different door. Here it comes
    /// from <see cref="VoxelCollider.OverlapsSolid"/> testing the <i>whole</i> box rather than
    /// its feet, so the test is against a rule that lives somewhere else — which is exactly the
    /// kind of guarantee that stops holding quietly.
    /// </remarks>
    [Fact]
    public void Unstick_skips_a_pocket_too_short_for_the_body()
    {
        // Ground to row 3 (top y = 1.0), a slab at rows 8..9 (y 2.0..2.5), air elsewhere. The
        // gap between them is 1.0 m and the body is 1.8 m, so it does not fit.
        var world = new TestWorld
        {
            Background = v => v.Y <= 3 || (v.Y >= 8 && v.Y <= 9)
                ? PhysicsTestHelpers.Dirt
                : PhysicsTestHelpers.Air,
        };

        Aabb box = Config.BoxAtFeet(new Vector3(2.0f, 0.5f, 2.0f));

        Assert.True(PlayerSpawn.TryUnstick(world, PhysicsTestHelpers.Table, box, out Aabb freed));

        Assert.Equal(2.5f, freed.Min.Y);
        Assert.False(VoxelCollider.OverlapsSolid(world, PhysicsTestHelpers.Table, freed));
    }

    /// <summary>
    /// An unstick is a lift. It never drops the body, even when dropping would be shorter.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The case: the body's <i>head</i> is in a ceiling and its feet are in open air, so the
    /// nearest free position is a few centimetres <b>down</b>. The rule takes the one upward
    /// instead — through the ceiling and onto the roof — because up is the only direction whose
    /// answer does not depend on which face happened to be overlapping, and because a body that
    /// can be pushed down by material appearing above it can be pushed into the floor.
    /// </para>
    /// <para>
    /// <b>The honest cost, recorded for M6.</b> Placing a block at the player's head shoves them
    /// on top of it rather than crouching them under it. That is what Minecraft does and it is
    /// almost certainly right, but it is a decision rather than a consequence, and M6 is where
    /// something deliberately creates material at the player's position.
    /// </para>
    /// <para>
    /// Written because break-recipe 1 was green: every other buried-body test here starts with
    /// the feet already on a plane, where the downward candidate is the box itself and overlaps
    /// by construction. Feet at 1.21 m is deliberately off-plane.
    /// </para>
    /// </remarks>
    [Fact]
    public void Unstick_never_moves_a_body_downward()
    {
        // Ground to row 3 (top y = 1.0) and a ceiling at rows 12..13 (y 3.0..3.5).
        var world = new TestWorld
        {
            Background = v => v.Y <= 3 || (v.Y >= 12 && v.Y <= 13)
                ? PhysicsTestHelpers.Dirt
                : PhysicsTestHelpers.Air,
        };

        // Feet off-plane at 1.21 m: the box's top is 3.01 m, a centimetre into the ceiling.
        // Dropping it to the plane at 1.0 m would free it; lifting has to clear the ceiling.
        Aabb box = Config.BoxAtFeet(new Vector3(2.0f, 1.21f, 2.0f));
        Assert.True(
            VoxelCollider.OverlapsSolid(world, PhysicsTestHelpers.Table, box),
            "precondition: the head is in the ceiling");
        Assert.False(
            VoxelCollider.OverlapsSolid(
                world, PhysicsTestHelpers.Table, new Aabb(new Vector3(box.Min.X, 1.0f, box.Min.Z), box.Size)),
            "precondition: and dropping to the plane below would free it");

        Assert.True(PlayerSpawn.TryUnstick(world, PhysicsTestHelpers.Table, box, out Aabb freed));

        Assert.True(
            freed.Min.Y >= box.Min.Y,
            $"an unstick must never drop the body: {box.Min.Y} m became {freed.Min.Y} m");
        Assert.Equal(3.5f, freed.Min.Y);
        Assert.False(VoxelCollider.OverlapsSolid(world, PhysicsTestHelpers.Table, freed));
    }

    [Fact]
    public void Unstick_gives_up_rather_than_searching_forever()
    {
        var world = new TestWorld { Background = _ => PhysicsTestHelpers.Stone };
        Aabb box = Config.BoxAtFeet(new Vector3(2.0f, 0.5f, 2.0f));

        Assert.False(PlayerSpawn.TryUnstick(world, PhysicsTestHelpers.Table, box, out Aabb freed));
        PhysicsTestHelpers.AssertSameBits(box, freed, "a failed unstick must not have moved the body");
    }

    /// <summary>
    /// Unsticking against an unloaded world fails, which is why the gate comes first.
    /// </summary>
    /// <remarks>
    /// The controller must call <c>IsWorldReadyFor</c> before <c>TryUnstick</c>. Reversed, a
    /// body standing in open sky at frame zero is reported stuck — every voxel is unloaded,
    /// every unloaded voxel is solid, and the search runs to its bound and fails. Which reads
    /// as the spawn being broken rather than as the world not having arrived.
    /// </remarks>
    [Fact]
    public void Unstick_fails_against_an_unloaded_world()
    {
        var world = new TestWorld();
        for (int y = -2; y <= 2; y++)
        {
            world.Unload(new ChunkPos(0, y, 0));
        }

        Aabb box = Config.BoxAtFeet(new Vector3(2.0f, 2.0f, 2.0f));

        Assert.False(PlayerSpawn.IsWorldReadyFor(world, box), "and the gate says so first");
        Assert.False(PlayerSpawn.TryUnstick(world, PhysicsTestHelpers.Table, box, out _));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void Unstick_refuses_a_lift_bound_that_would_not_terminate(float bound)
    {
        TestWorld world = PhysicsTestHelpers.FlatGround();
        Aabb box = Config.BoxAtFeet(new Vector3(2.0f, 0.5f, 2.0f));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => PlayerSpawn.TryUnstick(world, PhysicsTestHelpers.Table, box, bound, out _));
    }
}
