using System;
using System.Numerics;
using MinerCraft.Core.Content;
using MinerCraft.Core.Physics;
using MinerCraft.Core.Voxels;

namespace MinerCraft.Game.Player;

/// <summary>Which of the two bodies is driving the camera.</summary>
public enum PlayerMode
{
    /// <summary>The flycam. The physics body is inert and keeps its last position.</summary>
    Fly,

    /// <summary>The physics body. The camera rides its eye.</summary>
    Walk,
}

/// <summary>
/// The player as a session: which mode is driving, where the body is, and whether the world
/// has arrived yet.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately free of Godot types, for the reason <see cref="MiningController"/> gives:
/// everything about how the player behaves can then be driven from a headless profile run with
/// no camera and no input device, which is what makes M4d's fall measurement a measurement
/// rather than a person holding a key.
/// </para>
/// <para>
/// It lives in the Game project rather than Core because a <em>mode</em> is input state — it is
/// about which set of controls the person is holding, not about what the world does. The rules
/// that are the world's — where a spawn lands, whether the world is ready, what to do about a
/// body inside rock — are all in <see cref="PlayerSpawn"/> in Core, where they are tested.
/// </para>
/// <para>
/// <b>The body is inert in fly mode, and that is what keeps every M2d and M3d measurement
/// valid.</b> <c>--profile</c> assigns the camera's position directly and the scripted
/// flythrough snaps it to the surface every frame; a camera parented to a physics body does
/// neither. See docs/M4-PLAN.md, M4c.
/// </para>
/// </remarks>
public sealed class PlayerController
{
    /// <summary>The reach every player starts with, in metres. See <see cref="ReachMetres"/>.</summary>
    /// <remarks>
    /// Named rather than inlined so the property default and <c>Main</c>'s fallback are one
    /// number. Two literals is how M3d's tool JSON and controller field would have drifted.
    /// </remarks>
    public const float DefaultReachMetres = 5.0f;

    private readonly PlayerMotionConfig _config;

    /// <summary>A body at the given feet position, in the given mode.</summary>
    public PlayerController(PlayerMotionConfig config, Vector3 spawnFeetCentreMetres, PlayerMode mode)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Validate();

        _config = config;
        Mode = mode;
        State = PlayerState.AtFeet(spawnFeetCentreMetres, config);
    }

    /// <summary>The constants the body moves under.</summary>
    public PlayerMotionConfig Config => _config;

    /// <summary>Which body is driving the camera.</summary>
    public PlayerMode Mode { get; private set; }

    /// <summary>Where the body is, and how fast.</summary>
    public PlayerState State { get; private set; }

    /// <summary>
    /// Whether the body has been admitted to the world.
    /// </summary>
    /// <remarks>
    /// False until the chunks under the body exist. Until then the body is held exactly where
    /// it was placed rather than stepped, because at frame zero nothing is loaded, every voxel
    /// is therefore solid, and a step would resolve the body against rock in all six
    /// directions. Set false again by a switch into walk mode, which re-enters through this
    /// same gate.
    /// </remarks>
    public bool Spawned { get; private set; }

    /// <summary>
    /// How far the player can reach, in metres.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The player's, not the tool's</b> — M3d put it on <see cref="MiningController"/> with a
    /// comment saying it belonged here as soon as a player existed, and one does. A tier says
    /// how big a bite it takes and what it is hard enough to take it out of; how far an arm
    /// reaches is a property of the arm.
    /// </para>
    /// <para>
    /// <b>5 m, settled by play at M4d on 2026-08-30 and deliberately generous.</b> M4-PLAN's
    /// constants table guessed 3.5 m and it was never shipped; the M3 play-test called 5 m "a
    /// little long, which is twenty voxels", and playing it with a body decided that generous is
    /// the right way to be wrong in a sandbox — over-reach costs a slightly floaty crosshair,
    /// under-reach costs not being able to touch what you are looking at.
    /// </para>
    /// <para>
    /// <b>Recorded revisit trigger: a survival mode, which wants 3 m or 2 m.</b> The number is
    /// right for this mode and wrong for one that does not exist yet, so it changes when the mode
    /// does. It is the second thing waiting on a game-mode concept, after whether the precision
    /// mining modifier should be gated to creative.
    /// </para>
    /// </remarks>
    public float ReachMetres { get; init; } = DefaultReachMetres;

    /// <summary>What the last step that actually ran did. Default until one has.</summary>
    public PlayerStepResult LastStep { get; private set; }

    /// <summary>
    /// Steps refused because the world under the body had not loaded.
    /// </summary>
    /// <remarks>
    /// The spawn gate's own counter, and <b>not</b> M4d's <i>steps spent held</i>: that one is
    /// about a body that is being stepped and stopped by an unloaded chunk mid-fall, which
    /// needs a flag on the collider that does not exist yet. This one is about a body that is
    /// not being stepped at all. Reported at the moment of spawn, so it is a number somebody
    /// reads rather than one waiting to be believed.
    /// </remarks>
    public int StepsHeldForWorld { get; private set; }

    /// <summary>Where the camera sits.</summary>
    public Vector3 EyePosition => State.EyePosition(_config);

    /// <summary>Where the body stands.</summary>
    public Vector3 FeetCentre => State.FeetCentre;

    /// <summary>
    /// Advances the body by one step, if it is the body's turn to drive.
    /// </summary>
    /// <returns>
    /// True if a step ran. False means the body is inert — flying, or waiting for the world.
    /// </returns>
    /// <remarks>
    /// <b>Gate, then unstick, then step, in that order.</b> Unsticking before the gate would
    /// search an unloaded world, find solid at every height, and report a body stuck in open
    /// sky — see <see cref="PlayerSpawn.TryUnstick"/>.
    /// </remarks>
    public bool TryStep(
        IVoxelWorld world,
        MaterialTable materials,
        MoveIntent intent,
        float deltaSeconds)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(materials);

        if (Mode != PlayerMode.Walk)
        {
            return false;
        }

        if (!Spawned && !TryAdmit(world, materials))
        {
            StepsHeldForWorld++;
            return false;
        }

        LastStep = PlayerMotion.Step(world, materials, State, intent, _config, deltaSeconds);
        State = LastStep.State;
        return true;
    }

    /// <summary>
    /// Switches mode, handing the body the camera's position on the way into walk.
    /// </summary>
    /// <remarks>
    /// A switch into walk re-enters through the spawn gate rather than placing the body
    /// directly, so there is exactly one piece of code that decides whether the world is ready
    /// and what to do about a body inside rock. Toggling into a wall and spawning into a cave
    /// mouth are the same event, and a second copy of that decision is a second copy to drift.
    /// </remarks>
    public void SetMode(PlayerMode mode, Vector3 eyePositionMetres)
    {
        if (mode == Mode)
        {
            return;
        }

        Mode = mode;

        if (mode != PlayerMode.Walk)
        {
            return;
        }

        State = PlayerState.AtFeet(
            new Vector3(
                eyePositionMetres.X,
                eyePositionMetres.Y - _config.EyeHeightMetres,
                eyePositionMetres.Z),
            _config);

        Spawned = false;
        StepsHeldForWorld = 0;
    }

    /// <summary>The other mode.</summary>
    public void ToggleMode(Vector3 eyePositionMetres) =>
        SetMode(Mode == PlayerMode.Walk ? PlayerMode.Fly : PlayerMode.Walk, eyePositionMetres);

    /// <summary>
    /// Admits the body to the world once the chunks under it exist, shoving it clear if it
    /// arrived inside rock.
    /// </summary>
    /// <remarks>
    /// A failed unstick leaves the body held rather than admitting it, and the next step tries
    /// again. That is the right way round: the world is still loading, so a body that is buried
    /// now may not be a moment later — the ceiling above it may turn out to be the inside of a
    /// chunk that has not arrived, and every unloaded voxel reads as solid until it does.
    /// </remarks>
    private bool TryAdmit(IVoxelWorld world, MaterialTable materials)
    {
        if (!PlayerSpawn.IsWorldReadyFor(world, State.Box))
        {
            return false;
        }

        if (!PlayerSpawn.TryUnstick(world, materials, State.Box, out Aabb freed))
        {
            return false;
        }

        State = State with { Box = freed, VelocityMetresPerSecond = Vector3.Zero };
        Spawned = true;
        return true;
    }
}
