using System.Numerics;
using Overburden.Core.Physics;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// "Stopped by rock" against "stopped by a chunk that has not arrived" — the signal M4b
/// deliberately did not build, and M4d's fall profile is the consumer that makes it honest.
/// </summary>
/// <remarks>
/// <para>
/// The two cases produce the <b>same collision</b>, which is the decision the milestone turns
/// on and is not what is under test here. What is under test is that they are now
/// <em>distinguishable</em>, that <c>BlockedByUnloaded</c> can never contradict <c>Blocked</c>,
/// and that a held body resumes falling once the world catches up rather than staying stuck on
/// nothing.
/// </para>
/// <para>
/// <b>Break-recipes run against the finished code:</b>
/// </para>
/// <list type="number">
/// <item><c>byUnloaded</c> set on every block rather than only on unloaded ones — caught, by
/// <see cref="Resting_on_real_ground_is_not_held"/>.</item>
/// <item><c>unloaded</c> set from the material check rather than from the load check — caught,
/// by <see cref="A_body_falling_onto_an_unloaded_chunk_is_grounded_and_held"/>.</item>
/// <item>The flag recorded on the wrong face — caught, by
/// <see cref="The_held_face_is_the_face_that_stopped_the_box"/>.</item>
/// <item><c>PlayerMotion.Step</c> dropping the field on the way out — caught, by
/// <see cref="A_step_onto_an_unloaded_chunk_reports_held"/>.</item>
/// <item><b>Step-down's probe contributing unloaded faces it did not block on — green</b>, and
/// it is a <b>documentation correction</b> rather than a hole. The probe is a purely vertical
/// move, and <c>ResolveAxis</c> returns early on a zero delta — M4a's bitwise-stability
/// early-out — so it cannot block on X or Z and its unloaded set can only ever contain
/// <c>NegativeY</c>. The mask that recipe removed was dead code documenting an impossible
/// worry; the code now states the actual claim instead. Third time this shape has appeared,
/// after M4a's zeroable contact epsilon and M4c's <c>Aabb.Translated</c>.</item>
/// </list>
/// <para>
/// Which leaves <see cref="Blocked_by_unloaded_is_always_a_subset_of_blocked"/> as a
/// <b>guard rather than a detector</b>, and that is worth being straight about: the invariant
/// currently holds <em>by construction</em>, because every composition point ORs the two sets
/// from the same <c>MoveResult</c>s. No recipe could be built that breaks it without also
/// rewriting how they compose. It is kept because that is exactly the property a later edit
/// would break silently.
/// </para>
/// </remarks>
public sealed class HeldByUnloadedTests
{
    private static readonly PlayerMotionConfig Config = PlayerMotionConfig.Default;

    /// <summary>A world of air with the chunk below the origin column missing.</summary>
    /// <remarks>
    /// Air rather than terrain everywhere else, so the <em>only</em> thing that can stop a body
    /// is the unloaded chunk. A test that left real ground in reach would pass for the wrong
    /// reason.
    /// </remarks>
    private static TestWorld AirWithNothingBelow()
    {
        var world = new TestWorld();
        world.Unload(new ChunkPos(0, -1, 0));
        return world;
    }

    [Fact]
    public void A_body_falling_onto_an_unloaded_chunk_is_grounded_and_held()
    {
        TestWorld world = AirWithNothingBelow();

        // Feet a little above the chunk boundary at y = 0, falling into it.
        Aabb box = Config.BoxAtFeet(new Vector3(2.0f, 0.5f, 2.0f));
        MoveResult result = VoxelCollider.Move(
            world, PhysicsTestHelpers.Table, box, new Vector3(0.0f, -1.0f, 0.0f));

        Assert.True(result.Grounded, "an unloaded voxel is solid — that has not changed");
        Assert.True(result.HeldByUnloadedFloor, "and it is now sayable that it was not ground");
        Assert.True(result.HeldByUnloadedWorld);
    }

    [Fact]
    public void Resting_on_real_ground_is_not_held()
    {
        TestWorld world = PhysicsTestHelpers.FlatGround();

        Aabb box = Config.BoxAtFeet(new Vector3(2.0f, 2.0f, 2.0f));
        MoveResult result = VoxelCollider.Move(
            world, PhysicsTestHelpers.Table, box, new Vector3(0.0f, -1.5f, 0.0f));

        Assert.True(result.Grounded);
        Assert.False(result.HeldByUnloadedFloor, "dirt is not an unloaded chunk");
        Assert.Equal(BlockedFaces.None, result.BlockedByUnloaded);
    }

    /// <summary>
    /// The flag lands on the face that actually stopped the box, not on Y by habit.
    /// </summary>
    [Fact]
    public void The_held_face_is_the_face_that_stopped_the_box()
    {
        var world = new TestWorld();
        world.Unload(new ChunkPos(1, 0, 0));

        // Just inside chunk (0,0,0) on X, walking into the missing chunk next door. No
        // vertical displacement at all, so a −Y flag could only come from somewhere wrong.
        float boundary = VoxelConstants.ChunkSizeMetres;
        Aabb box = Config.BoxAtFeet(new Vector3(boundary - 0.4f, 2.0f, 2.0f));

        MoveResult result = VoxelCollider.Move(
            world, PhysicsTestHelpers.Table, box, new Vector3(0.5f, 0.0f, 0.0f));

        Assert.Equal(BlockedFaces.PositiveX, result.Blocked);
        Assert.Equal(BlockedFaces.PositiveX, result.BlockedByUnloaded);
        Assert.False(result.HeldByUnloadedFloor, "nothing was underneath it");
        Assert.True(result.HeldByUnloadedWorld);
    }

    [Fact]
    public void A_step_onto_an_unloaded_chunk_reports_held()
    {
        TestWorld world = AirWithNothingBelow();

        var state = new PlayerState(
            // Feet 0.1 m above the chunk boundary at y = 0. One step at -20 m/s is 0.333 m,
            // so the box genuinely enters the missing chunk below — at 0.4 m it would stop
            // short of it and the test would pass for the wrong reason.
            Config.BoxAtFeet(new Vector3(2.0f, 0.1f, 2.0f)),
            new Vector3(0.0f, -20.0f, 0.0f),
            Grounded: false);

        PlayerStepResult step = PlayerMotion.Step(
            world, PhysicsTestHelpers.Table, state, MoveIntent.Still, Config, 1.0f / 60.0f);

        Assert.True(step.Grounded);
        Assert.True(step.HeldByUnloadedFloor, "the field must survive PlayerMotion.Step");
    }

    /// <summary>
    /// A held body resumes falling once the chunk arrives and turns out to be air.
    /// </summary>
    /// <remarks>
    /// The behavioural claim the whole signal is about: the body is not stuck, it is
    /// <em>waiting</em>. If this did not hold, "steps spent held" would be measuring a bug
    /// rather than a race between the player and the streamer.
    /// </remarks>
    [Fact]
    public void A_held_body_falls_again_once_the_world_arrives()
    {
        TestWorld world = AirWithNothingBelow();

        var state = new PlayerState(
            // Feet 0.1 m above the chunk boundary at y = 0. One step at -20 m/s is 0.333 m,
            // so the box genuinely enters the missing chunk below — at 0.4 m it would stop
            // short of it and the test would pass for the wrong reason.
            Config.BoxAtFeet(new Vector3(2.0f, 0.1f, 2.0f)),
            new Vector3(0.0f, -20.0f, 0.0f),
            Grounded: false);

        PlayerStepResult held = PlayerMotion.Step(
            world, PhysicsTestHelpers.Table, state, MoveIntent.Still, Config, 1.0f / 60.0f);
        Assert.True(held.HeldByUnloadedFloor);

        // Another step against the same world stays held and does not sink.
        PlayerStepResult stillHeld = PlayerMotion.Step(
            world, PhysicsTestHelpers.Table, held.State, MoveIntent.Still, Config, 1.0f / 60.0f);
        Assert.True(stillHeld.HeldByUnloadedFloor);
        Assert.Equal(held.Box.Min.Y, stillHeld.Box.Min.Y, tolerance: 1e-5f);

        // The chunk arrives, and it is air.
        var loaded = new TestWorld();
        PlayerStepResult falling = PlayerMotion.Step(
            loaded, PhysicsTestHelpers.Table, stillHeld.State, MoveIntent.Still, Config, 1.0f / 60.0f);

        Assert.False(falling.Grounded, "nothing is holding it up any more");
        Assert.False(falling.HeldByUnloadedFloor);
        Assert.True(
            falling.Box.Min.Y < stillHeld.Box.Min.Y,
            "and it resumes falling rather than staying where the missing chunk left it");
    }

    /// <summary>
    /// <c>BlockedByUnloaded</c> is always a subset of <c>Blocked</c>, at both layers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The invariant that makes the field safe to read: a caller can trust that a held face was
    /// genuinely a contact. Fuzzed rather than argued because the composition points — step-up
    /// ORing two moves, step-down ORing a probe — are exactly where a stray face could be
    /// introduced without any single call site looking wrong.
    /// </para>
    /// <para>
    /// <b>Not watched to fail, and that was checked rather than assumed.</b> The obvious recipe
    /// — letting step-down's probe contribute unmasked faces — leaves this green, because a
    /// purely vertical probe cannot block horizontally at all. Every composition point ORs both
    /// sets from the same moves, so the invariant holds by construction today and this is a
    /// guard against a later edit rather than a detector of a present bug. Recorded because a
    /// fuzz test believed to be catching something, and catching nothing, is worse than none.
    /// </para>
    /// </remarks>
    [Fact]
    public void Blocked_by_unloaded_is_always_a_subset_of_blocked()
    {
        var random = new Random(0x4D34D);

        for (int i = 0; i < 4000; i++)
        {
            // A patchwork of loaded terrain and missing chunks, so both kinds of contact are
            // reachable and the body is often against one of each.
            var world = new TestWorld
            {
                Background = v => ((v.X >> 2) + (v.Y >> 2) + (v.Z >> 2)) % 3 == 0
                    ? PhysicsTestHelpers.Stone
                    : PhysicsTestHelpers.Air,
            };

            for (int c = 0; c < 6; c++)
            {
                world.Unload(new ChunkPos(
                    random.Next(-2, 3), random.Next(-2, 3), random.Next(-2, 3)));
            }

            var state = new PlayerState(
                Config.BoxAtFeet(new Vector3(
                    (float)(random.NextDouble() * 20.0 - 10.0),
                    (float)(random.NextDouble() * 20.0 - 10.0),
                    (float)(random.NextDouble() * 20.0 - 10.0))),
                new Vector3(
                    (float)(random.NextDouble() * 20.0 - 10.0),
                    (float)(random.NextDouble() * 40.0 - 30.0),
                    (float)(random.NextDouble() * 20.0 - 10.0)),
                Grounded: random.Next(2) == 0);

            var intent = new MoveIntent(
                new Vector3((float)(random.NextDouble() * 2.0 - 1.0), 0.0f,
                            (float)(random.NextDouble() * 2.0 - 1.0)),
                Jump: random.Next(4) == 0,
                Sprint: random.Next(2) == 0);

            PlayerStepResult step = PlayerMotion.Step(
                world, PhysicsTestHelpers.Table, state, intent, Config, 1.0f / 60.0f);

            Assert.True(
                (step.BlockedByUnloaded & ~step.Blocked) == BlockedFaces.None,
                $"iteration {i}: held on {step.BlockedByUnloaded} but blocked only on "
                + $"{step.Blocked} — a face was reported held without being a contact");
        }
    }

    /// <summary>
    /// A move that hits nothing reports nothing, which is the boring half worth pinning.
    /// </summary>
    [Fact]
    public void An_unobstructed_move_is_not_held()
    {
        var world = new TestWorld();

        MoveResult result = VoxelCollider.Move(
            world,
            PhysicsTestHelpers.Table,
            Config.BoxAtFeet(new Vector3(2.0f, 4.0f, 2.0f)),
            new Vector3(0.0f, -0.1f, 0.0f));

        Assert.Equal(BlockedFaces.None, result.Blocked);
        Assert.Equal(BlockedFaces.None, result.BlockedByUnloaded);
        Assert.False(result.HeldByUnloadedWorld);
    }
}
