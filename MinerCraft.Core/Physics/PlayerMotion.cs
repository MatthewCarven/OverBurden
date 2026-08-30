using System.Numerics;
using MinerCraft.Core.Content;
using MinerCraft.Core.Voxels;

namespace MinerCraft.Core.Physics;

/// <summary>What a body is being asked to do this step.</summary>
/// <param name="WalkDirection">
/// Which way the body wants to go, in world metres. The Y component is ignored — walking is
/// horizontal, and where the camera is pointing is the caller's business.
/// </param>
/// <param name="Jump">Whether the jump control is held.</param>
/// <param name="Sprint">Whether the sprint modifier is held.</param>
/// <remarks>
/// <para>
/// Intent, not velocity: the caller says <i>forward and to the left</i> and the config says
/// how fast that is. That is what lets M4d change the walk speed without touching a binding,
/// and what lets a headless profile drive the body with no input device at all.
/// </para>
/// <para>
/// <b>The direction is clamped to unit length, never scaled up to it.</b> Two keys held
/// together are 1.414 units of intent and must not walk 41% faster than one; an analogue stick
/// half-pushed is 0.5 units and should walk at half speed. Normalising unconditionally would
/// get the first right and the second wrong.
/// </para>
/// </remarks>
public readonly record struct MoveIntent(Vector3 WalkDirection, bool Jump, bool Sprint)
{
    /// <summary>Standing still, hands off every control.</summary>
    public static MoveIntent Still => default;

    /// <summary>Walking the given way, not jumping, not sprinting.</summary>
    public static MoveIntent Walking(Vector3 direction) => new(direction, false, false);

    /// <summary>Sprinting the given way.</summary>
    public static MoveIntent Sprinting(Vector3 direction) => new(direction, false, true);

    /// <summary>This intent with the jump control held.</summary>
    public MoveIntent WithJump() => this with { Jump = true };

    /// <summary>The horizontal velocity this intent asks for, in metres per second.</summary>
    internal Vector3 HorizontalVelocity(PlayerMotionConfig config)
    {
        var flat = new Vector3(WalkDirection.X, 0.0f, WalkDirection.Z);
        float length = flat.Length();
        if (length == 0.0f)
        {
            return Vector3.Zero;
        }

        float speed = Sprint
            ? config.SprintSpeedMetresPerSecond
            : config.WalkSpeedMetresPerSecond;

        return length > 1.0f ? flat * (speed / length) : flat * speed;
    }
}

/// <summary>Everything about a body that survives from one step to the next.</summary>
/// <param name="Box">Where the body is.</param>
/// <param name="VelocityMetresPerSecond">How fast it is going.</param>
/// <param name="Grounded">Whether it was standing on something at the end of the last step.</param>
/// <remarks>
/// <para>
/// Three fields and no more, which is what makes <see cref="PlayerMotion.Step"/> a pure
/// function and the determinism test a two-line replay. Anything a step needs that is not here
/// — how long the jump button has been held, how long since the body left the ground — would
/// be coyote time or jump buffering, and those are M4d's and additive.
/// </para>
/// <para>
/// <b><paramref name="Grounded"/> is deliberately an input as well as an output.</b> Step-up
/// and step-down both read the <i>previous</i> step's ground state, because X and Z resolve
/// before Y and the current step's answer does not exist yet when they need it. That is one
/// frame of latency, 16 ms at 60 Hz, against the roughly 40 extra voxel reads per step a
/// separate downward probe would cost.
/// </para>
/// </remarks>
public readonly record struct PlayerState(
    Aabb Box,
    Vector3 VelocityMetresPerSecond,
    bool Grounded)
{
    /// <summary>A body standing still with its feet at the given point.</summary>
    /// <remarks>
    /// Not grounded, because nothing has looked yet. The first step's gravity finds the floor
    /// and says so; claiming otherwise here would let step-down fire before the body has ever
    /// been anywhere.
    /// </remarks>
    public static PlayerState AtFeet(Vector3 feetCentreMetres, PlayerMotionConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return new PlayerState(config.BoxAtFeet(feetCentreMetres), Vector3.Zero, false);
    }

    /// <summary>Where the body is standing — horizontally centred, at the soles.</summary>
    public Vector3 FeetCentre => Box.FeetCentre;

    /// <summary>Where the camera sits, and where the mining ray starts.</summary>
    public Vector3 EyePosition(PlayerMotionConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        Vector3 feet = Box.FeetCentre;
        return new Vector3(feet.X, feet.Y + config.EyeHeightMetres, feet.Z);
    }
}

/// <summary>Where one step left the body, and what happened on the way.</summary>
/// <param name="State">The body after the step. Feed it back in for the next one.</param>
/// <param name="Blocked">
/// The faces a resolve pushed back on <b>along the path that was kept</b>. A step-up's probes
/// are not in here: the lift and the rejected horizontal attempt are how the answer was
/// found, not what happened to the body.
/// </param>
/// <param name="Jumped">A jump was accepted this step. What a sound or an animation triggers on.</param>
/// <param name="SteppedUp">The body climbed a lip it walked into.</param>
/// <param name="SteppedDown">The body was pulled down onto ground it would otherwise have left.</param>
/// <param name="Substeps">
/// Total substeps across every <see cref="VoxelCollider.Move"/> this step made, <b>including
/// the probes whose results were thrown away</b> — a cost figure that excluded rejected work
/// would make a refused step-up look free.
/// </param>
/// <param name="VoxelQueries">Total voxel reads this step cost, on the same basis.</param>
/// <param name="BlockedByUnloaded">
/// Of <paramref name="Blocked"/>, the faces stopped by a chunk that has not loaded rather than
/// by material — on the same "path that was kept" basis as <paramref name="Blocked"/>, so a
/// rejected step-up probe's contact with unloaded world is not in here.
/// </param>
public readonly record struct PlayerStepResult(
    PlayerState State,
    BlockedFaces Blocked,
    bool Jumped,
    bool SteppedUp,
    bool SteppedDown,
    int Substeps,
    int VoxelQueries,
    BlockedFaces BlockedByUnloaded = BlockedFaces.None)
{
    /// <summary>Where the body ended up.</summary>
    public Aabb Box => State.Box;

    /// <summary>How fast it is going now.</summary>
    public Vector3 Velocity => State.VelocityMetresPerSecond;

    /// <summary>Whether it is standing on something.</summary>
    public bool Grounded => State.Grounded;

    /// <summary>Whether it put its head into something.</summary>
    public bool HitCeiling => (Blocked & BlockedFaces.PositiveY) != 0;

    /// <summary>Whether it walked into something it could not climb.</summary>
    public bool BlockedHorizontally =>
        (Blocked & (BlockedFaces.NegativeX | BlockedFaces.PositiveX
                    | BlockedFaces.NegativeZ | BlockedFaces.PositiveZ)) != 0;

    /// <summary>
    /// The body is standing on world that has not loaded — <b>M4d's <i>step spent held</i></b>.
    /// </summary>
    /// <remarks>
    /// <see cref="Grounded"/> is true and this is true at the same time, and that is not a
    /// contradiction: the body <em>is</em> resting on something, and that something is going to
    /// stop existing. Counting steps where both hold is how fast the player is outrunning the
    /// streamer, which is the measurement terminal velocity is picked from.
    /// </remarks>
    public bool HeldByUnloadedFloor => (BlockedByUnloaded & BlockedFaces.NegativeY) != 0;

    public override string ToString() =>
        $"{Box} v{Velocity}{(Grounded ? " grounded" : string.Empty)}"
        + $"{(SteppedUp ? " stepped-up" : string.Empty)}"
        + $"{(SteppedDown ? " stepped-down" : string.Empty)}"
        + $" blocked {Blocked}, {Substeps} substep(s), {VoxelQueries} voxel read(s)"
        + (BlockedByUnloaded == BlockedFaces.None ? "" : $", unloaded on {BlockedByUnloaded}");
}

/// <summary>
/// Gravity, jump, step-up and step-down: turning intent into a displacement and handing it to
/// <see cref="VoxelCollider"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>A pure function of a state, an intent and a world</b>, exactly as M4a is a pure function
/// of a box and a displacement. No clock, no accumulator, no field anywhere in this file. The
/// step's length arrives as an argument, which is what lets a test run a 10,000-step fall in
/// milliseconds and lets a headless profile drive the body with no Godot in the process.
/// </para>
/// <para>
/// <b>60 Hz, and that is not the 20 Hz sim tick.</b> DESIGN specifies a fixed 20 Hz step for
/// machines; the player does not run on it, because 20 Hz is 50 ms of input latency and that
/// is the difference between a controller that feels tight and one that feels like a laggy
/// server. Two rates, and the reason they differ is that machines are <i>simulated</i> and the
/// player is <i>driven</i>. Nothing here knows the rate — Godot's <c>_PhysicsProcess</c>
/// supplies it at M4c.
/// </para>
/// <para>
/// <b>Horizontal velocity is replaced from intent every step rather than accelerated.</b> Stop
/// pressing forward and the body stops that frame; there is no friction, no momentum and full
/// control in mid-air. That is the snappiest model and the one whose faults are easiest to
/// judge by playing, which is the point — acceleration, air control and ground friction are
/// three separate feel decisions and M4d is where a person decides them. The vertical
/// component <i>is</i> integrated, because a jump that did not carry its speed forward would
/// not be a jump.
/// </para>
/// <para>
/// <b>What is deliberately absent:</b> coyote time, jump buffering and variable jump height
/// from how long the button is held. All three are real, all three are decided by playing
/// rather than by reasoning, and all three are additive — none of them requires this model to
/// change shape, only <see cref="PlayerState"/> to carry another counter.
/// </para>
/// </remarks>
public static class PlayerMotion
{
    /// <summary>
    /// Advances the body by one step.
    /// </summary>
    /// <param name="world">The world as it was loaded at the end of the previous step.</param>
    /// <param name="materials">The table the solidity predicate reads.</param>
    /// <param name="state">The body at the start of the step.</param>
    /// <param name="intent">What it is being asked to do.</param>
    /// <param name="config">The constants.</param>
    /// <param name="deltaSeconds">How long the step is.</param>
    /// <exception cref="ArgumentException">
    /// The constants are unusable, or the intent is not finite. A non-finite <i>velocity</i>
    /// is not checked here on purpose: it becomes a non-finite displacement, which
    /// <see cref="VoxelCollider.Move"/> already refuses by name, and one loud failure at the
    /// layer that can describe it beats two.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">The step length is not positive and finite.</exception>
    public static PlayerStepResult Step(
        IVoxelWorld world,
        MaterialTable materials,
        PlayerState state,
        MoveIntent intent,
        PlayerMotionConfig config,
        float deltaSeconds)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(materials);
        ArgumentNullException.ThrowIfNull(config);

        config.Validate();

        if (!float.IsFinite(deltaSeconds) || deltaSeconds <= 0.0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deltaSeconds),
                deltaSeconds,
                "A step needs a finite, strictly positive length. A zero-length step would "
                + "apply no gravity and report the previous ground state as if it had been "
                + "re-derived, which is worse than doing nothing.");
        }

        if (!float.IsFinite(intent.WalkDirection.X)
            || !float.IsFinite(intent.WalkDirection.Y)
            || !float.IsFinite(intent.WalkDirection.Z))
        {
            throw new ArgumentException(
                $"A walk direction must be finite; got {intent.WalkDirection}.",
                nameof(intent));
        }

        Vector3 horizontal = intent.HorizontalVelocity(config);
        float verticalSpeed = VerticalSpeed(state, intent, config, deltaSeconds, out bool jumped);

        var velocity = new Vector3(horizontal.X, verticalSpeed, horizontal.Z);
        Vector3 displacement = velocity * deltaSeconds;

        MoveResult moved = VoxelCollider.Move(world, materials, state.Box, displacement);
        int substeps = moved.Substeps;
        int queries = moved.VoxelQueries;

        Aabb box = moved.Box;
        BlockedFaces blocked = moved.Blocked;
        BlockedFaces blockedByUnloaded = moved.BlockedByUnloaded;
        bool steppedUp = false;

        // Step-up, and its first two guards are here rather than inside TryStepUp because they
        // are about the body's situation rather than about the geometry ahead of it.
        //
        //   Grounded, or holding forward against a cliff face climbs it in mid-air.
        //   Non-upward, which is the same exploit through a different door — a jump into a
        //   wall would otherwise gain the step height on top of the jump, every step.
        //
        // The ground state read is the *previous* step's, per PlayerState's remarks: X and Z
        // have already resolved by the time this runs, so the current step's answer does not
        // exist yet.
        if (moved.BlockedHorizontally && state.Grounded && verticalSpeed <= 0.0f)
        {
            var horizontalOnly = new Vector3(displacement.X, 0.0f, displacement.Z);

            if (TryStepUp(
                    world, materials, state.Box, horizontalOnly, config,
                    out Aabb lifted, out BlockedFaces liftedBlocked,
                    out BlockedFaces liftedByUnloaded, ref substeps, ref queries))
            {
                box = lifted;
                blocked = liftedBlocked;
                blockedByUnloaded = liftedByUnloaded;
                steppedUp = true;
            }
        }

        bool grounded = (blocked & BlockedFaces.NegativeY) != 0;
        bool steppedDown = false;

        // Step-down, the half people forget. Walking off a one-voxel lip without it leaves the
        // body airborne for two or three steps, and at 0.25 m granularity that means the camera
        // bobs continuously on any downhill slope — which is every slope, because heightmap
        // terrain steps by whole voxels.
        //
        // The guards mirror step-up's exactly: only from ground, only while not rising, and
        // only as far as the step height — so walking off an actual ledge still falls. Not
        // after a step-up, which has already settled onto whatever it climbed.
        if (!grounded && !steppedUp && state.Grounded && verticalSpeed <= 0.0f)
        {
            MoveResult probe = VoxelCollider.Move(
                world, materials, box, new Vector3(0.0f, -config.StepHeightMetres, 0.0f));

            substeps += probe.Substeps;
            queries += probe.VoxelQueries;

            if (probe.Grounded)
            {
                box = probe.Box;
                blocked |= BlockedFaces.NegativeY;

                // Written as the literal face, mirroring the line above, rather than as a mask
                // over probe.BlockedByUnloaded. A break-recipe proved the mask was redundant
                // *by construction*: the probe is a purely vertical move, and ResolveAxis
                // returns early on a zero delta — M4a's bitwise-stability early-out — so the
                // probe cannot block on X or Z and its unloaded set can only ever contain
                // NegativeY. Masking would have been dead code documenting a worry that cannot
                // happen; this states the actual claim.
                if (probe.HeldByUnloadedFloor)
                {
                    blockedByUnloaded |= BlockedFaces.NegativeY;
                }
                grounded = true;
                steppedDown = true;
            }
        }

        return new PlayerStepResult(
            new PlayerState(box, VelocityAfterContact(velocity, blocked), grounded),
            blocked,
            jumped,
            steppedUp,
            steppedDown,
            substeps,
            queries,
            blockedByUnloaded);
    }

    /// <summary>
    /// This step's vertical speed: a jump if one was asked for and earned, otherwise gravity.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A jump replaces that step's gravity rather than being reduced by it</b>, so the body
    /// leaves the ground at exactly <see cref="PlayerMotionConfig.JumpSpeedMetresPerSecond"/>
    /// and the apex is never lower than the analytic figure. The other order costs
    /// <c>g·dt</c> off the launch — a sixth of a metre of height at these constants — for no
    /// reason a player could name.
    /// </para>
    /// <para>
    /// <b>Level-triggered, not edge-triggered:</b> holding the jump control bounces. That is
    /// the familiar behaviour, and the alternative needs the previous frame's button state in
    /// <see cref="PlayerState"/>, which is jump buffering's field and therefore M4d's.
    /// </para>
    /// </remarks>
    private static float VerticalSpeed(
        PlayerState state,
        MoveIntent intent,
        PlayerMotionConfig config,
        float deltaSeconds,
        out bool jumped)
    {
        jumped = intent.Jump && state.Grounded;
        if (jumped)
        {
            return config.JumpSpeedMetresPerSecond;
        }

        float speed = state.VelocityMetresPerSecond.Y
            - (config.GravityMetresPerSecondSquared * deltaSeconds);

        float terminal = -config.TerminalSpeedMetresPerSecond;
        return speed < terminal ? terminal : speed;
    }

    /// <summary>
    /// Zeroes the components a resolve pushed back on.
    /// </summary>
    /// <remarks>
    /// The vertical half is load-bearing: without it a body resting on the floor accumulates
    /// downward speed forever and the first hole it walks over launches it at whatever it has
    /// reached, and a head-bonk keeps its upward speed and sticks to the ceiling until gravity
    /// wins. The horizontal half is currently only honest reporting, since
    /// <see cref="MoveIntent"/> replaces those components next step regardless — it is here
    /// because the moment M4d adds acceleration it stops being cosmetic, and a body that runs
    /// into a wall while its stored velocity says otherwise is a bug that would then have to be
    /// found rather than avoided.
    /// </remarks>
    private static Vector3 VelocityAfterContact(Vector3 velocity, BlockedFaces blocked)
    {
        const BlockedFaces X = BlockedFaces.NegativeX | BlockedFaces.PositiveX;
        const BlockedFaces Y = BlockedFaces.NegativeY | BlockedFaces.PositiveY;
        const BlockedFaces Z = BlockedFaces.NegativeZ | BlockedFaces.PositiveZ;

        return new Vector3(
            (blocked & X) != 0 ? 0.0f : velocity.X,
            (blocked & Y) != 0 ? 0.0f : velocity.Y,
            (blocked & Z) != 0 ? 0.0f : velocity.Z);
    }

    /// <summary>
    /// Lift, re-attempt the horizontal move, and settle back down.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The re-attempt must succeed completely.</b> Accepting a partial success is accepting
    /// a squeeze into a gap that is not there: the body would climb halfway up a lip, get
    /// stopped by whatever is above it, and be left standing on the lip's face.
    /// </para>
    /// <para>
    /// <b>The headroom guard is structural rather than a separate test, and that is worth
    /// knowing before someone adds the explicit one.</b> M4-PLAN asks that the whole box be
    /// clear at the lifted position, not just the feet. Both the lift and the re-attempt are
    /// real <see cref="VoxelCollider.Move"/> calls, and a Move that did not start overlapping
    /// cannot end overlapping — so a ceiling stops the lift short, and a low ceiling over the
    /// destination blocks the horizontal re-attempt against the ceiling voxel itself. There is
    /// a test for each, because a guarantee that comes from somewhere else is the kind that
    /// quietly stops holding.
    /// </para>
    /// <para>
    /// <b>The settle drops by exactly what the lift achieved</b>, so a step-up can never leave
    /// the body lower than it started — it is a climb, not a fall — and can never leave it
    /// higher than the thing it climbed onto. When there is nothing under the destination
    /// (a knee-high fence with air behind it) the body simply ends at its original height,
    /// which is stepping over rather than up, and gravity has the next word.
    /// </para>
    /// </remarks>
    private static bool TryStepUp(
        IVoxelWorld world,
        MaterialTable materials,
        Aabb box,
        Vector3 horizontalMetres,
        PlayerMotionConfig config,
        out Aabb result,
        out BlockedFaces blocked,
        out BlockedFaces blockedByUnloaded,
        ref int substeps,
        ref int queries)
    {
        result = box;
        blocked = BlockedFaces.None;
        blockedByUnloaded = BlockedFaces.None;

        if (config.StepHeightMetres <= 0.0f)
        {
            return false;
        }

        MoveResult lift = VoxelCollider.Move(
            world, materials, box, new Vector3(0.0f, config.StepHeightMetres, 0.0f));

        substeps += lift.Substeps;
        queries += lift.VoxelQueries;

        // An unobstructed lift is placed at the exact target rather than at whatever three
        // substeps of a third of the step height accumulate to. Same reasoning as Aabb storing
        // a size instead of deriving one: a rise of exactly the step height must clear a lip of
        // exactly the step height, and a few ULP of accumulation error in the wrong direction
        // puts the leading face back inside the lip's top voxel, refusing a step the constants
        // say is legal.
        //
        // **Measured, because the obvious version of that claim is wrong.** An ordinary resting
        // body floats VoxelCollider.ContactEpsilonMetres above the floor, and 0.1 mm swamps the
        // accumulation error — so with the epsilon in place, snapping changes no outcome at all
        // and the whole suite passes either way. What the snap actually buys is that M4a's
        // recorded claim stays true: setting ContactEpsilonMetres to zero leaves the suite green.
        // With the lift accumulated instead of snapped, zeroing it fails four tests. The snap is
        // what keeps the epsilon a second belt rather than the load-bearing one, and the test
        // that can see the difference without touching the epsilon has to place the feet exactly
        // on a voxel plane — which is what M4c's spawn and a fly-mode toggle both do.
        Aabb liftedBox = lift.HitCeiling
            ? lift.Box
            : box.WithMinOn(1, box.Min.Y + config.StepHeightMetres);

        float achieved = liftedBox.Min.Y - box.Min.Y;
        if (achieved <= 0.0f)
        {
            return false;
        }

        MoveResult retry = VoxelCollider.Move(world, materials, liftedBox, horizontalMetres);
        substeps += retry.Substeps;
        queries += retry.VoxelQueries;

        if (retry.BlockedHorizontally)
        {
            return false;
        }

        MoveResult settle = VoxelCollider.Move(
            world, materials, retry.Box, new Vector3(0.0f, -achieved, 0.0f));

        substeps += settle.Substeps;
        queries += settle.VoxelQueries;

        result = settle.Box;
        blocked = retry.Blocked | settle.Blocked;

        // The same two moves, so the subset invariant survives the composition: a face can only
        // appear here if the move that contributed it also contributed the face to `blocked`.
        blockedByUnloaded = retry.BlockedByUnloaded | settle.BlockedByUnloaded;
        return true;
    }
}
