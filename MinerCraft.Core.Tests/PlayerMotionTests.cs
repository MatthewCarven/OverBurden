using System.Numerics;
using MinerCraft.Core.Physics;
using MinerCraft.Core.Voxels;
using static MinerCraft.Core.Tests.PhysicsTestHelpers;

namespace MinerCraft.Core.Tests;

/// <summary>
/// Gravity, jump, step-up and step-down, against a dictionary of voxels.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every motion test goes through <see cref="Step"/></b>, which carries M4a's shared
/// postcondition forward: a body never <i>ends</i> a step overlapping solid. Motion adds three
/// new ways to violate it — a lift into a ceiling, a step-up onto something that is not there,
/// and a step-down snap into a floor — so the helper matters more here than it did a layer
/// down, not less.
/// </para>
/// <para>
/// <b>The step-up guards are tested by changing one thing.</b> Each of the three refusals uses
/// the same world, the same box and the same intent as a climb that succeeds, and differs only
/// in the guard's own input — the ground flag, the sign of the vertical speed, the presence of
/// a ceiling. A refusal test that builds its own scenario proves the body did not climb; it
/// does not prove the guard is why.
/// </para>
/// </remarks>
public class PlayerMotionTests
{
    /// <summary>60 Hz. Not the 20 Hz sim tick — see <see cref="PlayerMotion"/>.</summary>
    private const float Dt = 1.0f / 60.0f;

    private static readonly PlayerMotionConfig Config = PlayerMotionConfig.Default;

    private static readonly Vector3 East = new(1.0f, 0.0f, 0.0f);

    private static readonly Vector3 West = new(-1.0f, 0.0f, 0.0f);

    /// <summary>Twice the contact epsilon: what "resting exactly on this plane" means in a test.</summary>
    private const float Contact = VoxelCollider.ContactEpsilonMetres * 2.0f;

    // ------------------------------------------------------------------- gravity and landing

    [Fact]
    public void AFallingBodyLandsOnTopOfTheFloorAndItsVerticalSpeedIsZeroed()
    {
        TestWorld world = FlatGround();
        PlayerState state = PlayerState.AtFeet(new Vector3(2.0f, 6.0f, 2.0f), Config);

        for (int i = 0; i < 60; i++)
        {
            state = Step(world, state, MoveIntent.Still).State;
        }

        Assert.True(state.Grounded);
        Assert.InRange(state.Box.Min.Y, 1.0f, 1.0f + Contact);

        // Zeroed, not merely small. A body that keeps accumulating downward speed against the
        // floor launches itself the first time it walks over a hole.
        Assert.Equal(0.0f, state.VelocityMetresPerSecond.Y);
    }

    [Fact]
    public void StandingStillForTenThousandStepsDoesNotDriftAndGroundedNeverFlickers()
    {
        // M4a proves this for the collider given a constant nudge. This is the same claim one
        // layer up, where the nudge is produced rather than supplied: gravity re-accelerates
        // from zero every step because the landing zeroes it, so what is really being asserted
        // is that the accelerate-resolve-zero loop has no cycle in it.
        TestWorld world = FlatGround();
        PlayerState state = Standing(world, new Vector3(2.0f, 1.0f, 2.0f));

        for (int i = 0; i < 10_000; i++)
        {
            PlayerStepResult result = Step(world, state, MoveIntent.Still);

            AssertSameBits(state.Box, result.Box, $"drifted on step {i}");
            Assert.True(result.Grounded, $"ground state flickered on step {i}");
            Assert.Equal(0.0f, result.Velocity.Y);

            state = result.State;
        }
    }

    [Fact]
    public void ALongFallReachesTerminalVelocityAndNeverExceedsIt()
    {
        TestWorld world = FlatGround();
        PlayerState state = PlayerState.AtFeet(new Vector3(2.0f, 120.0f, 2.0f), Config);

        float terminal = Config.TerminalSpeedMetresPerSecond;
        float fastest = 0.0f;
        int substepsAtTerminal = 0;

        for (int i = 0; i < 400; i++)
        {
            PlayerStepResult result = Step(world, state, MoveIntent.Still);
            state = result.State;

            float speed = -state.VelocityMetresPerSecond.Y;
            Assert.True(speed <= terminal, $"exceeded terminal velocity on step {i}: {speed}");

            if (speed > fastest)
            {
                fastest = speed;
            }

            if (speed == terminal)
            {
                substepsAtTerminal = result.Substeps;
            }
        }

        Assert.Equal(terminal, fastest);
        Assert.True(state.Grounded, "never landed");

        // 40 m/s at 60 Hz is 0.667 m in a step, which is four substeps against a walking step's
        // one. That is the whole cost argument for terminal velocity, and it is a number the
        // plan asserted before anything could produce it.
        Assert.Equal(4, substepsAtTerminal);
    }

    [Fact]
    public void AnUnloadedChunkBelowStopsTheBodyRatherThanLettingItAccelerateAway()
    {
        // The decision M4 turns on, at the layer a player experiences it. The collider's version
        // of this test asserts the box does not enter the unloaded region; this one asserts the
        // consequence that matters for motion — the body *waits*, with its speed zeroed, rather
        // than piling up velocity against the edge of the world and shooting through the moment
        // the chunk arrives.
        var world = new TestWorld();
        world.Unload(new ChunkPos(0, -1, 0));

        PlayerState state = PlayerState.AtFeet(new Vector3(1.0f, 2.0f, 1.0f), Config);

        for (int i = 0; i < 120; i++)
        {
            state = Step(world, state, MoveIntent.Still).State;
            Assert.True(state.Box.Min.Y >= 0.0f, $"entered the unloaded chunk on step {i}");
        }

        Assert.True(state.Grounded);
        Assert.InRange(state.Box.Min.Y, 0.0f, Contact);
        Assert.Equal(0.0f, state.VelocityMetresPerSecond.Y);
    }

    // ------------------------------------------------------------------------------- jumping

    [Fact]
    public void AJumpFromRestReachesTheApexTheConstantsPredictAndComesBackDown()
    {
        TestWorld world = FlatGround();
        PlayerState state = Standing(world, new Vector3(2.0f, 1.0f, 2.0f));
        float start = state.Box.Min.Y;

        PlayerStepResult launched = Step(world, state, MoveIntent.Still.WithJump());
        Assert.True(launched.Jumped);
        Assert.False(launched.Grounded);
        state = launched.State;

        float apex = state.Box.Min.Y;
        for (int i = 0; i < 120 && !state.Grounded; i++)
        {
            state = Step(world, state, MoveIntent.Still).State;
            apex = MathF.Max(apex, state.Box.Min.Y);
        }

        // v²/2g = 7.5² / 50 = 1.125 m, and the tolerance is derived rather than magic: one
        // step's worth of the launch speed, v·dt = 0.125 m.
        float analytic = (Config.JumpSpeedMetresPerSecond * Config.JumpSpeedMetresPerSecond)
            / (2.0f * Config.GravityMetresPerSecondSquared);
        float oneStep = Config.JumpSpeedMetresPerSecond * Dt;

        // Asserted one-sided, because the direction is a design claim rather than an accident:
        // the jump *replaces* its step's gravity instead of being reduced by it, so the discrete
        // rise can only overshoot the analytic figure, never fall short of it. Measured, the
        // overshoot is 0.0625 m — exactly half a step of launch speed — for an apex of 1.1875 m.
        Assert.InRange(apex - start, analytic, analytic + oneStep);

        Assert.True(state.Grounded, "never came back down");
        Assert.InRange(state.Box.Min.Y, start - Contact, start + Contact);
    }

    [Fact]
    public void AJumpClearsAFourVoxelLedgeAndTheBodyStandsOnIt()
    {
        // What the apex is *for*, and the reason it cannot be quietly lowered: 1 m is four
        // voxels, which is what a heightmap cliff and a mined-out doorway both come out as.
        TestWorld world = Terrain((x, _) => x >= 8 ? 7 : 3);
        PlayerState state = Standing(world, new Vector3(1.0f, 1.0f, 1.0f));

        state = Step(world, state, MoveIntent.Walking(East).WithJump()).State;
        for (int i = 0; i < 60; i++)
        {
            state = Step(world, state, MoveIntent.Walking(East)).State;
        }

        Assert.True(state.Grounded);
        Assert.InRange(state.Box.Min.Y, 2.0f, 2.0f + Contact);
    }

    [Fact]
    public void JumpingIntoACeilingZeroesTheUpwardSpeedRatherThanSticking()
    {
        // Floor at 1 m, ceiling at 3 m, a 1.8 m body: 0.2 m of clearance and a jump that wants
        // 1.19 m of it.
        TestWorld world = new() { Background = v => v.Y <= 3 || v.Y >= 12 ? Dirt : Air };
        PlayerState state = Standing(world, new Vector3(2.0f, 1.0f, 2.0f));

        PlayerStepResult result = Step(world, state, MoveIntent.Still.WithJump());
        state = result.State;

        bool bonked = false;
        for (int i = 0; i < 10 && !bonked; i++)
        {
            result = Step(world, state, MoveIntent.Still);
            state = result.State;
            bonked = result.HitCeiling;
        }

        Assert.True(bonked, "never reached the ceiling");
        Assert.Equal(0.0f, state.VelocityMetresPerSecond.Y);
        Assert.True(state.Box.Max.Y <= 3.0f);

        // And then falls, rather than hanging there with upward speed it cannot spend.
        for (int i = 0; i < 60; i++)
        {
            state = Step(world, state, MoveIntent.Still).State;
        }

        Assert.True(state.Grounded);
        Assert.InRange(state.Box.Min.Y, 1.0f, 1.0f + Contact);
    }

    [Fact]
    public void JumpIsRefusedInMidAirSoTheControlCannotBeHeldToFly()
    {
        TestWorld world = FlatGround();
        PlayerState state = Standing(world, new Vector3(2.0f, 1.0f, 2.0f));

        PlayerStepResult first = Step(world, state, MoveIntent.Still.WithJump());
        Assert.True(first.Jumped);

        // Held down, not re-pressed: level-triggered jump is the familiar behaviour, and this is
        // what stops it becoming flight.
        PlayerStepResult second = Step(world, first.State, MoveIntent.Still.WithJump());
        Assert.False(second.Jumped);
        Assert.True(second.Velocity.Y < first.Velocity.Y);
    }

    // ------------------------------------------------------------------------ walking, speed

    [Theory]
    [InlineData(1.0f, 0.0f, false, 4.5f)]
    [InlineData(1.0f, 0.0f, true, 7.0f)]
    [InlineData(1.0f, 1.0f, false, 4.5f)]
    [InlineData(0.5f, 0.0f, false, 2.25f)]
    public void OneSecondOfWalkingCoversTheSpeedTheConstantsSay(
        float dx, float dz, bool sprint, float expectedMetres)
    {
        // The diagonal case is the one worth having: two keys held together are 1.414 units of
        // intent and must not walk 41% faster than one. The half-pushed case is the other half
        // of the same rule — the direction is clamped to unit length, never scaled up to it, so
        // an analogue stick at half deflection walks at half speed.
        TestWorld world = FlatGround();
        PlayerState state = Standing(world, new Vector3(4.0f, 1.0f, 4.0f));
        Vector3 start = state.FeetCentre;

        var intent = new MoveIntent(new Vector3(dx, 0.0f, dz), false, sprint);
        for (int i = 0; i < 60; i++)
        {
            state = Step(world, state, intent).State;
        }

        Vector3 travelled = state.FeetCentre - start;
        Assert.Equal(expectedMetres, new Vector3(travelled.X, 0.0f, travelled.Z).Length(), 2);
        Assert.True(state.Grounded, "left the ground while walking on a flat floor");
    }

    [Fact]
    public void WalkingIntoAWallStopsTheBodyAndZeroesThatComponentOfItsVelocity()
    {
        TestWorld world = new() { Background = v => v.X >= 8 || v.Y <= 3 ? Dirt : Air };
        PlayerState state = Standing(world, new Vector3(1.0f, 1.0f, 1.0f));

        for (int i = 0; i < 60; i++)
        {
            state = Step(world, state, MoveIntent.Walking(East)).State;
        }

        Assert.InRange(state.Box.Max.X, 2.0f - Contact, 2.0f);
        Assert.Equal(0.0f, state.VelocityMetresPerSecond.X);
    }

    // -------------------------------------------------------------------------------- step-up

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void StepUpClimbsALipUpToTheStepHeightAndRefusesOneAboveIt(int voxels, bool climbs)
    {
        // Two voxels is 0.5 m, which is the step height exactly — so the climbable case is the
        // knife edge, not a comfortable margin. It is also why the lift is placed at its exact
        // target rather than at whatever three substeps accumulate to.
        TestWorld world = Terrain((x, _) => x >= 8 ? 3 + voxels : 3);
        PlayerState state = Standing(world, new Vector3(1.0f, 1.0f, 1.0f));

        bool everSteppedUp = false;
        for (int i = 0; i < 60; i++)
        {
            PlayerStepResult result = Step(world, state, MoveIntent.Walking(East));
            everSteppedUp |= result.SteppedUp;
            state = result.State;

            Assert.True(result.Grounded, $"left the ground on step {i}");
        }

        Assert.Equal(climbs, everSteppedUp);

        if (climbs)
        {
            float top = (4 + voxels) * VoxelConstants.VoxelSize;
            Assert.InRange(state.Box.Min.Y, top, top + Contact);

            // A step-up costs no horizontal distance: the re-attempt makes the whole move, so
            // sixty steps of walking is sixty steps of walking whether or not there was a lip
            // in the way.
            Assert.InRange(state.FeetCentre.X, 1.0f + 4.4f, 1.0f + 4.6f);
        }
        else
        {
            Assert.InRange(state.Box.Max.X, 2.0f - Contact, 2.0f);
            Assert.InRange(state.Box.Min.Y, 1.0f, 1.0f + Contact);
        }
    }

    [Fact]
    public void StepUpRefusesWhenTheBodyWasNotOnTheGround()
    {
        // Otherwise a player scales a cliff by holding forward against it in mid-air. Same
        // world, same box, same intent as the climb below it — only the incoming ground flag
        // differs, which is the guard's own input.
        TestWorld world = Terrain((x, _) => x >= 8 ? 5 : 3);
        PlayerState pressed = PressedAgainstTheLip(world);

        PlayerStepResult airborne = Step(
            world, pressed with { Grounded = false }, MoveIntent.Walking(East));
        Assert.False(airborne.SteppedUp);
        Assert.InRange(airborne.Box.Min.Y, 1.0f - Contact, 1.0f + Contact);

        PlayerStepResult grounded = Step(world, pressed, MoveIntent.Walking(East));
        Assert.True(grounded.SteppedUp);
    }

    [Fact]
    public void StepUpRefusesWhileTheBodyIsMovingUpward()
    {
        // The same exploit through a different door: a jump into a wall would otherwise gain the
        // step height on top of the jump, every step, all the way up.
        TestWorld world = Terrain((x, _) => x >= 8 ? 5 : 3);
        PlayerState pressed = PressedAgainstTheLip(world);

        PlayerStepResult jumping = Step(
            world, pressed, MoveIntent.Walking(East).WithJump());

        Assert.True(jumping.Jumped);
        Assert.False(jumping.SteppedUp);

        // Risen by exactly one step of the launch speed and not a millimetre of step height
        // more.
        float expected = pressed.Box.Min.Y + (Config.JumpSpeedMetresPerSecond * Dt);
        Assert.Equal(expected, jumping.Box.Min.Y, 4);
    }

    [Fact]
    public void StepUpRefusesWhenTheLiftedBodyHasNoHeadroom()
    {
        // A 0.5 m lift under a ceiling 0.2 m above the head. The guard is structural rather
        // than a separate check — the lift is a real Move, so the ceiling stops it short, and
        // the horizontal re-attempt from there is blocked by the lip it was supposed to clear.
        // Standing on that lip would put the head 0.3 m into rock, so refusing is also the
        // right answer rather than merely a safe one.
        //
        // The pair is the point: identical but for the ceiling.
        TestWorld low = new()
        {
            Background = v => v.Y <= (v.X >= 8 ? 5 : 3) || v.Y >= 12 ? Dirt : Air,
        };
        TestWorld open = Terrain((x, _) => x >= 8 ? 5 : 3);

        Assert.False(WalkedIntoTheLip(low).SteppedUp);
        Assert.True(WalkedIntoTheLip(open).SteppedUp);

        static PlayerStepResult WalkedIntoTheLip(TestWorld world)
        {
            PlayerState state = Standing(world, new Vector3(1.0f, 1.0f, 1.0f));
            PlayerStepResult result = default;

            for (int i = 0; i < 20; i++)
            {
                result = Step(world, state, MoveIntent.Walking(East));
                state = result.State;

                if (result.SteppedUp)
                {
                    break;
                }
            }

            return result;
        }
    }

    [Fact]
    public void StepUpRefusesWhenTheLiftedMoveOnlyPartlySucceeds()
    {
        // M4-PLAN's fourth guard, and the one with a cost worth writing down. Walking diagonally
        // into a corner where one axis is a climbable lip and the other is a tall wall: the
        // lifted re-attempt clears the lip but is still blocked by the wall, so it has not
        // *fully* succeeded and the whole step-up is refused. The body stops at the corner
        // rather than climbing.
        //
        // That is the specified behaviour and it is safe, but it means a player hugging a wall
        // while walking up a slope stops dead until they let go of the wall direction.
        // **Recorded for M4d:** if that reads as stuck, the relaxation is one line — require the
        // re-attempt to be blocked on no axis the original move was not already blocked on.
        TestWorld world = new()
        {
            Background = v => v.Z >= 8 || v.Y <= (v.X >= 8 ? 5 : 3) ? Dirt : Air,
        };
        PlayerState state = Standing(world, new Vector3(1.0f, 1.0f, 1.0f));

        bool everSteppedUp = false;
        for (int i = 0; i < 60; i++)
        {
            PlayerStepResult result = Step(
                world, state, MoveIntent.Walking(new Vector3(1.0f, 0.0f, 1.0f)));

            everSteppedUp |= result.SteppedUp;
            state = result.State;
        }

        Assert.False(everSteppedUp);
        Assert.InRange(state.Box.Min.Y, 1.0f, 1.0f + Contact);
    }

    [Fact]
    public void ALiftOfExactlyTheStepHeightClearsALipOfExactlyTheStepHeight()
    {
        // The lift is placed at its exact target rather than at whatever three substeps of a
        // third of the step height accumulate to, and this is the test that can tell the
        // difference. It needs feet that sit *exactly* on a voxel plane, which is why the state
        // is built by hand rather than settled: an ordinary resting body floats 0.1 mm above the
        // floor, and that 0.1 mm swamps the few ULP of accumulation error, so the ordinary
        // climb tests cannot distinguish the two forms at all.
        //
        // Exactly-on-a-plane is not a contrivance. M4c spawns the body at a surface height, and
        // toggling out of fly mode puts it wherever the camera was.
        //
        // **What this actually protects is a claim in another milestone.** M4a recorded that
        // ContactEpsilonMetres could be set to zero with the whole suite green — the half-open
        // interval, not the epsilon, is what makes resting contact work. Measured: with the lift
        // accumulated instead of snapped, zeroing the epsilon fails four tests. So the snap is
        // what keeps M4a's claim true, and without it that claim would have gone stale the day
        // M4b landed, silently.
        TestWorld world = Terrain((x, _) => x >= 8 ? 5 : 3);

        var onThePlane = new PlayerState(
            Aabb.FromFeet(
                new Vector3(1.75f, 1.0f, 1.0f), Config.WidthMetres, Config.HeightMetres),
            Vector3.Zero,
            Grounded: true);

        Assert.Equal(2.0f, onThePlane.Box.Max.X);

        PlayerStepResult result = Step(world, onThePlane, MoveIntent.Walking(East));

        Assert.True(result.SteppedUp, "refused a lip of exactly the step height");
        Assert.InRange(result.Box.Min.Y, 1.5f, 1.5f + Contact);
    }

    [Fact]
    public void AStepUpNeverSettlesLowerThanItLifted()
    {
        // The settle drops by exactly what the lift achieved, so a step-up is a climb and never
        // a fall. What that really protects is step-down's distance limit: step-down deliberately
        // refuses to snap the body down more than the step height, so that walking off a real
        // ledge falls. A settle that could go further would give the same rule two answers
        // depending on whether the body happened to climb something first.
        //
        // Reaching it needs a body that clears the thing it just climbed in one step, because
        // the settle is otherwise bounded below by the ledge the re-attempt moved over. A
        // walking step is 0.075 m against a body 0.5 m wide, so walking cannot; 45 m/s can, and
        // that is the condition rather than a plausible walk speed — the same condition M4a's
        // remarks name for a mine cart.
        PlayerMotionConfig fast = Config with { WalkSpeedMetresPerSecond = 45.0f };

        // Ground at 1 m, a single-voxel fence 0.5 m higher, and a pit 0.75 m below the ground
        // immediately behind it.
        TestWorld world = new()
        {
            Background = v => v.X <= 7
                ? (v.Y <= 3 ? Dirt : Air)
                : v.X == 8
                    ? (v.Y <= 5 ? Dirt : Air)
                    : v.Y <= 0 ? Dirt : Air,
        };

        PlayerState state = Standing(world, new Vector3(1.0f, 1.0f, 1.0f), fast);
        float start = state.Box.Min.Y;

        PlayerStepResult climb = default;
        for (int i = 0; i < 10 && !climb.SteppedUp; i++)
        {
            climb = Step(world, state, MoveIntent.Walking(East), fast);
            state = climb.State;
        }

        Assert.True(climb.SteppedUp, "never got over the fence");
        Assert.InRange(climb.Box.Min.Y, start - Contact, start + Contact);
        Assert.False(climb.Grounded, "landed in the pit rather than stepping over the fence");
    }

    [Fact]
    public void StepUpIsOffEntirelyWhenTheStepHeightIsZero()
    {
        // The knob does what it says, which is also what makes the staircase test below
        // meaningful rather than a demonstration that walking works.
        PlayerMotionConfig flat = Config with { StepHeightMetres = 0.0f };
        TestWorld world = Terrain((x, _) => x >= 8 ? 4 : 3);
        PlayerState state = Standing(world, new Vector3(1.0f, 1.0f, 1.0f), flat);

        for (int i = 0; i < 60; i++)
        {
            state = Step(world, state, MoveIntent.Walking(East), flat).State;
        }

        Assert.InRange(state.Box.Max.X, 2.0f - Contact, 2.0f);
    }

    // ------------------------------------------------------------------------------ step-down

    [Fact]
    public void StepDownHoldsTheBodyOnASmallDropAndLetsItFallOffARealLedge()
    {
        // Without it the body is airborne for two or three steps off every one-voxel lip, and at
        // 0.25 m granularity that means the camera bobs continuously on any downhill slope —
        // which is every slope, because heightmap terrain steps by whole voxels.
        TestWorld lip = Terrain((x, _) => x >= 8 ? 2 : 3);
        PlayerState state = Standing(lip, new Vector3(1.0f, 1.0f, 1.0f));

        bool everSteppedDown = false;
        for (int i = 0; i < 60; i++)
        {
            PlayerStepResult result = Step(lip, state, MoveIntent.Walking(East));
            everSteppedDown |= result.SteppedDown;
            state = result.State;

            Assert.True(result.Grounded, $"went airborne over a one-voxel drop on step {i}");
        }

        Assert.True(everSteppedDown);
        Assert.InRange(state.Box.Min.Y, 0.75f, 0.75f + Contact);

        // Ten voxels is 2.5 m, five times the step height. That is a ledge, and walking off it
        // is a fall.
        TestWorld cliff = Terrain((x, _) => x >= 8 ? -7 : 3);
        state = Standing(cliff, new Vector3(1.0f, 1.0f, 1.0f));

        bool everAirborne = false;
        for (int i = 0; i < 60; i++)
        {
            PlayerStepResult result = Step(cliff, state, MoveIntent.Walking(East));
            everAirborne |= !result.Grounded;
            state = result.State;

            Assert.False(result.SteppedDown, $"clung to a 2.5 m drop on step {i}");
        }

        Assert.True(everAirborne);
        Assert.InRange(state.Box.Min.Y, -1.5f, -1.5f + Contact);
    }

    [Fact]
    public void StepDownDoesNotFireOnTheStepAJumpLaunchesOn()
    {
        // Without the non-upward guard this is not a subtle bug: the jump lifts the body 0.125 m
        // off the floor, step-down sees it airborne after having been grounded, probes 0.5 m
        // down, finds the floor it just left and snaps back onto it. Jumping would do nothing at
        // all.
        TestWorld world = FlatGround();
        PlayerState state = Standing(world, new Vector3(2.0f, 1.0f, 2.0f));
        float start = state.Box.Min.Y;

        PlayerStepResult result = Step(world, state, MoveIntent.Still.WithJump());

        Assert.False(result.SteppedDown);
        Assert.False(result.Grounded);
        Assert.True(result.Box.Min.Y > start + 0.1f, $"the jump did not leave the ground: {result}");
    }

    [Fact]
    public void AStaircaseOfSingleVoxelsIsWalkedUpAndBackDownWithoutEverLeavingTheGround()
    {
        // What step-up and step-down exist for, together, in the shape terrain actually takes:
        // a heightmap that steps by whole voxels. Neither half is a convenience here — without
        // step-up every tread is a wall to jump, and without step-down every tread is a
        // two-frame fall.
        TestWorld world = Terrain((x, _) => 3 + Math.Clamp((x - 8) / 2, 0, 8));
        PlayerState state = Standing(world, new Vector3(1.0f, 1.0f, 1.0f));

        bool climbed = false;
        for (int i = 0; i < 120; i++)
        {
            PlayerStepResult result = Step(world, state, MoveIntent.Walking(East));
            climbed |= result.SteppedUp;
            state = result.State;

            Assert.True(result.Grounded, $"left the ground climbing, on step {i}");
        }

        Assert.True(climbed);
        Assert.InRange(state.Box.Min.Y, 3.0f, 3.0f + Contact);

        bool descended = false;
        for (int i = 0; i < 120; i++)
        {
            PlayerStepResult result = Step(world, state, MoveIntent.Walking(West));
            descended |= result.SteppedDown;
            state = result.State;

            Assert.True(result.Grounded, $"left the ground descending, on step {i}");
        }

        Assert.True(descended);
        Assert.InRange(state.Box.Min.Y, 1.0f, 1.0f + Contact);
    }

    // ------------------------------------------------------------------ determinism and cost

    [Fact]
    public void AnIdenticalInputSequenceReplaysToABitwiseIdenticalTrajectory()
    {
        // The whole of what M4 owes multiplayer, at the layer that has state in it. M4a proved
        // the collider replays; this proves the accumulator on top of it does too, which is the
        // half that could plausibly not.
        List<PlayerState> first = Trajectory();
        List<PlayerState> second = Trajectory();

        Assert.Equal(first.Count, second.Count);
        for (int i = 0; i < first.Count; i++)
        {
            AssertSameBits(first[i].Box, second[i].Box, $"diverged at step {i}");
            AssertSameBits(
                first[i].VelocityMetresPerSecond,
                second[i].VelocityMetresPerSecond,
                $"velocity diverged at step {i}");
            Assert.Equal(first[i].Grounded, second[i].Grounded);
        }

        static List<PlayerState> Trajectory()
        {
            TestWorld world = Terrain((x, z) => 3 + Math.Clamp((x + z - 16) / 3, 0, 6));
            var random = new Random(20260829);
            PlayerState state = PlayerState.AtFeet(new Vector3(1.0f, 4.0f, 1.0f), Config);
            var trail = new List<PlayerState>();

            for (int i = 0; i < 600; i++)
            {
                var intent = new MoveIntent(
                    new Vector3(
                        ((float)random.NextDouble() - 0.5f) * 2.0f,
                        0.0f,
                        ((float)random.NextDouble() - 0.5f) * 2.0f),
                    random.NextDouble() < 0.05,
                    random.NextDouble() < 0.3);

                state = PlayerMotion.Step(world, Table, state, intent, Config, Dt).State;
                trail.Add(state);
            }

            return trail;
        }
    }

    [Fact]
    public void AWalkingStepCostsOneSubstepAndAStepUpCostsFourMoves()
    {
        // A regression detector, not a measurement to defend to the digit. The interesting shape
        // is the ratio: an ordinary step is one Move and M4a's ~41 voxel reads, and a step-up is
        // four Moves — the lift, the re-attempt, the settle, and the original attempt that
        // failed — so it is roughly four times an ordinary step and nowhere near the cost of
        // anything else in a frame.
        TestWorld world = Terrain((x, _) => x >= 8 ? 5 : 3);
        PlayerState state = Standing(world, new Vector3(1.0f, 1.0f, 1.0f));

        PlayerStepResult walking = Step(world, state, MoveIntent.Walking(East));
        Assert.Equal(1, walking.Substeps);
        Assert.InRange(walking.VoxelQueries, 1, 64);

        PlayerStepResult climbing = Step(world, PressedAgainstTheLip(world), MoveIntent.Walking(East));
        Assert.True(climbing.SteppedUp);
        Assert.InRange(climbing.VoxelQueries, 1, 400);
    }

    [Fact]
    public void TheEyeSitsAboveTheFeetAtTheHeightTheConfigSays()
    {
        // Where the camera goes at M4c, and where the mining ray starts. Cheap, and it is the
        // one dimension of the body nothing else in this file would notice going missing.
        PlayerState state = PlayerState.AtFeet(new Vector3(3.0f, 7.0f, -2.0f), Config);

        Assert.Equal(new Vector3(3.0f, 7.0f, -2.0f), state.FeetCentre);
        Assert.Equal(new Vector3(3.0f, 7.0f + 1.65f, -2.0f), state.EyePosition(Config));
        Assert.True(state.EyePosition(Config).Y < state.Box.Max.Y, "the eye is outside the head");
    }

    // ---------------------------------------------------------------------------- refusals

    [Theory]
    [InlineData(0.0f)]
    [InlineData(-1.0f / 60.0f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void AStepLengthThatIsNotPositiveAndFiniteIsRefused(float dt)
    {
        TestWorld world = FlatGround();
        PlayerState state = PlayerState.AtFeet(new Vector3(2.0f, 2.0f, 2.0f), Config);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => PlayerMotion.Step(world, Table, state, MoveIntent.Still, Config, dt));
    }

    [Fact]
    public void ANonFiniteWalkDirectionIsRefusedRatherThanPuttingTheBodyNowhere()
    {
        TestWorld world = FlatGround();
        PlayerState state = PlayerState.AtFeet(new Vector3(2.0f, 2.0f, 2.0f), Config);
        var intent = MoveIntent.Walking(new Vector3(float.NaN, 0.0f, 0.0f));

        Assert.Throws<ArgumentException>(
            () => PlayerMotion.Step(world, Table, state, intent, Config, Dt));
    }

    [Fact]
    public void ASetOfConstantsThatWouldMoveTheBodyStrangelyIsRefused()
    {
        // Validated where it is used rather than where it is built, which is
        // ToolDefinition.MaxRadiusMetres's pattern: an `init` property can be set from anywhere
        // and a validating constructor is not the only door.
        //
        // Only the *quiet* failures are worth a check. A NaN anywhere becomes a NaN displacement
        // and VoxelCollider.Move already refuses that by name; a terminal speed large enough to
        // matter runs past MaxSubsteps and is refused there. These six are the ones that would
        // otherwise produce a body that moves wrongly while reporting nothing.
        (string Why, PlayerMotionConfig Config)[] unusable =
        [
            ("negative gravity — the body rises forever, which reads as a streaming bug",
                Config with { GravityMetresPerSecondSquared = -25.0f }),
            ("no terminal speed — the body never falls, which reads as a collision bug",
                Config with { TerminalSpeedMetresPerSecond = 0.0f }),
            ("a step height at the body's own height — every wall is a ladder to walk at",
                Config with { StepHeightMetres = 1.8f }),
            ("an eye above the head — it sees through the ceiling the body stands under",
                Config with { EyeHeightMetres = 2.0f }),
            ("no width", Config with { WidthMetres = 0.0f }),
            ("a height that is not a number", Config with { HeightMetres = float.NaN }),
        ];

        TestWorld world = FlatGround();
        PlayerState state = PlayerState.AtFeet(new Vector3(2.0f, 2.0f, 2.0f), Config);

        foreach ((string why, PlayerMotionConfig config) in unusable)
        {
            Assert.True(
                Record.Exception(
                    () => PlayerMotion.Step(world, Table, state, MoveIntent.Still, config, Dt))
                    is ArgumentException,
                $"accepted {why}");
        }
    }

    // -------------------------------------------------------------------------------- helpers

    /// <summary>
    /// One step, with M4a's shared postcondition asserted on the way out.
    /// </summary>
    private static PlayerStepResult Step(
        TestWorld world,
        PlayerState state,
        MoveIntent intent,
        PlayerMotionConfig? config = null)
    {
        PlayerStepResult result = PlayerMotion.Step(
            world, Table, state, intent, config ?? Config, Dt);

        Assert.False(
            VoxelCollider.OverlapsSolid(world, Table, result.Box),
            $"ended a step overlapping solid: {result}");

        return result;
    }

    /// <summary>
    /// A body settled onto the ground under its feet, ready for a test that assumes it is
    /// standing.
    /// </summary>
    /// <remarks>
    /// <see cref="PlayerState.AtFeet"/> is deliberately not grounded — nothing has looked yet —
    /// so a test that asserts on the ground state from its first step would be asserting about
    /// the constructor rather than about motion.
    /// </remarks>
    private static PlayerState Standing(
        TestWorld world, Vector3 feetCentre, PlayerMotionConfig? config = null)
    {
        PlayerState state = PlayerState.AtFeet(feetCentre, config ?? Config);
        PlayerStepResult settled = Step(world, state, MoveIntent.Still, config);

        Assert.True(settled.Grounded, $"the test's starting position is not on the ground: {settled}");
        return settled.State;
    }

    /// <summary>
    /// A body standing on the flat, walked east until it is up against the lip and has stopped.
    /// </summary>
    private static PlayerState PressedAgainstTheLip(TestWorld world)
    {
        PlayerState state = Standing(world, new Vector3(1.0f, 1.0f, 1.0f));

        for (int i = 0; i < 20; i++)
        {
            PlayerStepResult result = PlayerMotion.Step(
                world, Table, state, MoveIntent.Walking(East), Config, Dt);

            if (result.SteppedUp)
            {
                // Walked one step too far: the caller wants the state *before* the climb, so
                // that the climb is the thing under test.
                break;
            }

            state = result.State;
        }

        return state;
    }
}
