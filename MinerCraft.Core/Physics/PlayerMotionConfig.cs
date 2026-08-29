using System.Numerics;

namespace MinerCraft.Core.Physics;

/// <summary>
/// The numbers that make a body move like a person rather than like a physics demo.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every value here is an estimate, and none of them can be derived.</b> They are the
/// starting numbers, chosen so the <i>relationships</i> between them are right — a jump that
/// clears a four-voxel ledge, a sprint comfortably under the tunnelling threshold, a player
/// narrow enough to fit down a tunnel the weakest pick digs. M4d replaces them by playing,
/// which is the only instrument that can judge them. Each one's remarks say where it came
/// from, so a later reader can tell a measured number from a guessed one without going back
/// through the worklog.
/// </para>
/// <para>
/// A record rather than a set of constants because the alternative is a static class that
/// tests cannot vary. Half of M4b's tests turn one number off — step height, gravity, jump —
/// to isolate the behaviour that number is responsible for, and <c>Default with { … }</c> is
/// how they do it.
/// </para>
/// <para>
/// <b>Validated at use rather than at construction</b>, which is
/// <c>ToolDefinition.MaxRadiusMetres</c>'s pattern and for its reason: an <c>init</c> property
/// can be set from anywhere and a validating constructor is not the only door. <see
/// cref="Validate"/> is called at the top of every <see cref="PlayerMotion.Step"/>, where it
/// cannot be skipped, and the cost is a dozen float comparisons against a step that does
/// thousands of dictionary lookups.
/// </para>
/// </remarks>
public sealed record PlayerMotionConfig
{
    /// <summary>The estimates M4-PLAN's table specifies, unmodified.</summary>
    public static PlayerMotionConfig Default { get; } = new();

    /// <summary>
    /// The body's footprint, in metres. Square.
    /// </summary>
    /// <remarks>
    /// <b>0.5 m — two voxels — and it is coupled to the weakest tool's bite, so neither can be
    /// changed alone.</b> <c>hand_pick</c> has a 0.4 m radius, and a swing centred on a voxel
    /// centre removes every voxel whose centre is within that, which along any axis through the
    /// centre is three voxels: a 0.75 m free bore. A 0.5 m box passes down it with 0.125 m
    /// either side. Widen the player past 0.75 m and the first thing a new player does — dig
    /// into a hillside with the starting pick — produces a tunnel they cannot walk down.
    /// </remarks>
    public float WidthMetres { get; init; } = 0.5f;

    /// <summary>The body's height, in metres.</summary>
    /// <remarks>
    /// 1.8 m, or 7.2 voxels. Human-ish and nothing more; it is not a multiple of the voxel
    /// size on purpose, because a body that is an exact number of voxels tall makes every
    /// head-clearance question land exactly on a plane and hides the ordinary case.
    /// </remarks>
    public float HeightMetres { get; init; } = 1.8f;

    /// <summary>
    /// Where the camera sits above the feet, in metres.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 1.65 m. Nothing in <see cref="PlayerMotion"/> reads it — it is here because it is the
    /// third dimension of the same body whose width and height are here, and splitting a body's
    /// dimensions across two files is how one of them goes missing when the other two change.
    /// It is also the mining ray's origin, which M4c binds.
    /// </para>
    /// <para>
    /// <b>Reach is deliberately not here.</b> M4-PLAN's constants table lists it, and it is not
    /// a body dimension — it is what an arm does, it belongs to the player controller M4c
    /// builds, and it currently lives on <c>MiningController</c> with a comment saying exactly
    /// that. Putting an unread <c>ReachMetres</c> in a <i>motion</i> config would be a number
    /// nothing enforces, which is the shape a guessed figure takes when it later gets built on
    /// as fact.
    /// </para>
    /// </remarks>
    public float EyeHeightMetres { get; init; } = 1.65f;

    /// <summary>Downward acceleration, in metres per second squared.</summary>
    /// <remarks>
    /// 25, against a real 9.81. Real gravity reads as floaty in a first-person game — the
    /// player hangs at the top of a jump long enough to notice — and games generally run 2–3×.
    /// Zero is accepted, because a test that wants to isolate horizontal motion has no other
    /// way to say so.
    /// </remarks>
    public float GravityMetresPerSecondSquared { get; init; } = 25.0f;

    /// <summary>The vertical speed a jump starts with, in metres per second.</summary>
    /// <remarks>
    /// <para>
    /// 7.5 m/s against 25 m/s² is an analytic apex of <c>v²/2g</c> = 1.125 m, which clears a
    /// 1 m (four-voxel) ledge.
    /// </para>
    /// <para>
    /// <b>The apex actually reached is 1.1875 m, not 1.125 m</b> — measured, and the difference
    /// is not error. Integrating in discrete steps, with the jump applied before the step it
    /// launches on, spends the first step at the full initial speed rather than at the average
    /// over that step, so the rise overshoots the analytic figure by half a step of initial
    /// speed: <c>v·dt/2</c> = 0.0625 m at 60 Hz. So the clearance over a four-voxel ledge is
    /// 0.1875 m rather than the 0.125 m the analytic number suggests, and it is
    /// <b>rate-dependent</b> — the same constants at 30 Hz overshoot twice as far. Nothing
    /// depends on that today because the player runs at one rate; it is written down because a
    /// jump height that changes with the tick rate is the kind of thing that gets diagnosed as
    /// a physics bug.
    /// </para>
    /// </remarks>
    public float JumpSpeedMetresPerSecond { get; init; } = 7.5f;

    /// <summary>Ground speed with no sprint, in metres per second.</summary>
    /// <remarks>
    /// 4.5. A real walking pace is 1.4 m/s and is glacial in a game — at 0.25 m voxels it is
    /// under six voxels a second, and a chunk is 32 across.
    /// </remarks>
    public float WalkSpeedMetresPerSecond { get; init; } = 4.5f;

    /// <summary>Ground speed while sprinting, in metres per second.</summary>
    /// <remarks>
    /// 7.0, which is comfortably under the 15 m/s at which move-then-resolve would start
    /// tunnelling if <see cref="VoxelCollider.MaxSubstepMetres"/> did not exist — and still
    /// under it if someone later raises this to something silly, because the substep cap is
    /// what actually holds the line rather than this number's modesty.
    /// </remarks>
    public float SprintSpeedMetresPerSecond { get; init; } = 7.0f;

    /// <summary>The fastest the body falls, in metres per second.</summary>
    /// <remarks>
    /// <para>
    /// <b>40 m/s, and it is a streaming parameter wearing a physics costume.</b> The vertical
    /// view radius is 2–3 chunks, so there is 16–24 m of loaded world beneath the player; at
    /// 40 m/s that is crossed in 0.4–0.6 s, and if the streamer cannot produce the next chunk
    /// in that time the player stands on nothing — an unloaded voxel is solid — until it does.
    /// The honest way to pick this number is to measure how fast chunks arrive under a falling
    /// viewer, which is M4d's job. 40 is where to start.
    /// </para>
    /// <para>
    /// It is also the substep cost driver: 40 m/s at 60 Hz is 0.667 m in a step, which is four
    /// substeps against a walking step's one.
    /// </para>
    /// </remarks>
    public float TerminalSpeedMetresPerSecond { get; init; } = 40.0f;

    /// <summary>
    /// How high a lip the body climbs without jumping, and how far it will drop to stay on the
    /// ground, both in metres.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 0.5 m — two voxels. <b>At 0.25 m this is not a convenience, it is what makes the ground
    /// walkable at all.</b> Terrain generated from a heightmap steps by whole voxels, so a
    /// slope is a staircase with seven steps per metre of rise, and without step-up every one
    /// of them is a wall to jump.
    /// </para>
    /// <para>
    /// <b>One number for both directions, on purpose.</b> M4-PLAN specifies step-down as
    /// triggering "when ground is found within the step height", so a second constant would be
    /// a second thing to tune where the symmetry is what makes walking feel consistent —
    /// stepping down a lip you just stepped up should not behave differently. <b>Revisit
    /// trigger:</b> M4d, if a 0.5 m instant drop reads as a jolt where the matching rise does
    /// not. The split is one field and one call site.
    /// </para>
    /// </remarks>
    public float StepHeightMetres { get; init; } = 0.5f;

    /// <summary>A body of these dimensions standing with its feet at the given point.</summary>
    public Aabb BoxAtFeet(Vector3 feetCentreMetres) =>
        Aabb.FromFeet(feetCentreMetres, WidthMetres, HeightMetres);

    /// <summary>
    /// Refuses a set of constants that would move the body in a way nothing downstream reports.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only the failures that are <i>quiet</i> are worth a check here. A NaN anywhere in this
    /// table becomes a NaN velocity and then a NaN displacement, which
    /// <see cref="VoxelCollider.Move"/> already refuses by name; a terminal velocity large
    /// enough to matter runs past <see cref="VoxelCollider.MaxSubsteps"/> and is refused there.
    /// What is not caught downstream is a negative gravity (the player rises forever, looking
    /// like a streaming bug), a terminal speed of zero (the player never falls at all, looking
    /// like a collision bug), and a step height at or above the body's own height — which is
    /// not a step but a climb, and turns any wall into a ladder by walking at it.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">Any constant is unusable.</exception>
    public void Validate()
    {
        Finite(WidthMetres, nameof(WidthMetres), strictlyPositive: true);
        Finite(HeightMetres, nameof(HeightMetres), strictlyPositive: true);
        Finite(EyeHeightMetres, nameof(EyeHeightMetres), strictlyPositive: true);
        Finite(TerminalSpeedMetresPerSecond, nameof(TerminalSpeedMetresPerSecond), strictlyPositive: true);
        Finite(GravityMetresPerSecondSquared, nameof(GravityMetresPerSecondSquared), strictlyPositive: false);
        Finite(JumpSpeedMetresPerSecond, nameof(JumpSpeedMetresPerSecond), strictlyPositive: false);
        Finite(WalkSpeedMetresPerSecond, nameof(WalkSpeedMetresPerSecond), strictlyPositive: false);
        Finite(SprintSpeedMetresPerSecond, nameof(SprintSpeedMetresPerSecond), strictlyPositive: false);
        Finite(StepHeightMetres, nameof(StepHeightMetres), strictlyPositive: false);

        if (EyeHeightMetres > HeightMetres)
        {
            throw new ArgumentException(
                $"The eye ({EyeHeightMetres} m) is above the top of the head "
                + $"({HeightMetres} m), so the camera would sit outside the body it collides "
                + "with and could see through a ceiling the body is standing under.");
        }

        if (StepHeightMetres >= HeightMetres)
        {
            throw new ArgumentException(
                $"A step height of {StepHeightMetres} m against a body {HeightMetres} m tall "
                + "is a climb, not a step: walking into any wall would lift the body over it.");
        }
    }

    private static void Finite(float value, string name, bool strictlyPositive)
    {
        if (!float.IsFinite(value) || value < 0.0f || (strictlyPositive && value == 0.0f))
        {
            string bound = strictlyPositive ? "strictly positive" : "at least zero";
            throw new ArgumentException($"{name} must be finite and {bound}; got {value}.");
        }
    }
}
