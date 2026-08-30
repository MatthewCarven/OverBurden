using Godot;
using MinerCraft.Core.Voxels;

namespace MinerCraft.Game.Player;

/// <summary>
/// Free camera. <b>Not</b> replaced by the M4 player controller — it becomes fly mode
/// alongside it, and stays the instrument every profile run is driven through.
/// </summary>
/// <remarks>
/// This comment used to say M4 would replace it, which was a reasonable guess at M0 and is
/// wrong now. Acting on it would take the measurement apparatus with it: <c>--profile</c>
/// assigns <see cref="Node3D.GlobalPosition"/> directly and <c>Main.AdvanceFlythrough</c>
/// snaps the camera to the surface every frame, and neither works on a camera parented to a
/// physics body. See docs/M4-PLAN.md, M4c.
/// </remarks>
public partial class FlyCam : Camera3D
{
    /// <summary>Metres per second. Deliberately in metres, not voxels — a walking pace
    /// is about 1.4 m/s, so 6 m/s is a brisk fly and gives a feel for real scale.</summary>
    [Export] public float MoveSpeed { get; set; } = 6.0f;

    [Export] public float SprintMultiplier { get; set; } = 5.0f;
    [Export] public float CrawlMultiplier { get; set; } = 0.2f;
    [Export] public float MouseSensitivity { get; set; } = 0.0025f;

    /// <summary>Where the camera aims on startup, in metres.</summary>
    [Export] public Vector3 InitialTarget { get; set; } = new(4f, 3f, 4f);

    private const float PitchLimit = 1.55f; // just shy of ±90°, so up never flips

    private float _yaw;
    private float _pitch;
    private bool _mouseCaptured;

    /// <summary>
    /// Whether the mouse belongs to the game right now.
    /// </summary>
    /// <remarks>
    /// Mining polls the left button, and Esc releasing the mouse has to stop that as well as
    /// stop looking around — otherwise clicking on something else while the window has focus
    /// digs a hole in the world.
    /// </remarks>
    public bool MouseCaptured => _mouseCaptured;

    /// <summary>
    /// Whether WASD drives the camera directly.
    /// </summary>
    /// <remarks>
    /// False in walk mode, where <see cref="PlayerController"/> owns the position and
    /// <c>Main</c> assigns the camera to the body's eye every physics step. <b>Only the
    /// movement is switched off — mouse-look stays live in both modes</b>, because looking
    /// around is the same act whichever body you are, and because walking needs a yaw to walk
    /// along. Toggling this is what makes the mode switch one line rather than a reparent.
    /// </remarks>
    public bool FlightEnabled { get; set; } = true;

    /// <summary>Which way the camera is looking, flattened to the ground plane.</summary>
    /// <remarks>
    /// <b>Normalised after flattening, not before.</b> Dropping the Y component of a pitched
    /// forward vector shortens it — looking down 60° leaves 0.5 units — and
    /// <c>MoveIntent</c> clamps intent to unit length without ever scaling it up, so the
    /// un-normalised version would walk at half speed for looking at your feet.
    /// </remarks>
    public Vector3 WalkForward
    {
        get
        {
            Vector3 forward = -GlobalTransform.Basis.Z;
            forward.Y = 0f;
            return forward.LengthSquared() > 0f ? forward.Normalized() : -Vector3.Forward;
        }
    }

    /// <summary>The camera's right, on the ground plane.</summary>
    /// <remarks>
    /// Horizontal already — pitch rotates about this axis, so it cannot tilt it — but flattened
    /// anyway rather than assumed, because the assumption is a property of the rotation order
    /// and would go quiet if a roll were ever added.
    /// </remarks>
    public Vector3 WalkRight
    {
        get
        {
            Vector3 right = GlobalTransform.Basis.X;
            right.Y = 0f;
            return right.LengthSquared() > 0f ? right.Normalized() : Vector3.Right;
        }
    }

    public override void _Ready()
    {
        if (!GlobalPosition.IsEqualApprox(InitialTarget))
        {
            LookAt(InitialTarget, Vector3.Up);
        }

        // Seed yaw/pitch from wherever LookAt left us, so the first mouse movement is
        // relative to the aimed direction instead of snapping to zero.
        _yaw = Rotation.Y;
        _pitch = Rotation.X;

        SetMouseCaptured(true);

        GD.Print(
            $"MinerCraft — voxel {VoxelConstants.VoxelSize} m, " +
            $"chunk {VoxelConstants.ChunkSize}³ ({VoxelConstants.ChunkSizeMetres} m)\n" +
            "  WASD move, Space/Ctrl up-down, Shift sprint, Alt crawl\n" +
            "  F toggles walk and fly — walking, Space jumps and Shift sprints\n" +
            "  Left mouse mines a sphere, Ctrl+left takes one voxel\n" +
            "  Number keys pick a tier\n" +
            "  L places a lamp — a debug bind, not M6's placement\n" +
            "  Q toggles the entangled miner — unlimited reach, 8 swings/s, also a debug bind\n" +
            "  Esc releases the mouse, click to recapture");
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseMotion motion when _mouseCaptured:
                _yaw -= motion.Relative.X * MouseSensitivity;
                _pitch = Mathf.Clamp(
                    _pitch - motion.Relative.Y * MouseSensitivity,
                    -PitchLimit, PitchLimit);
                Rotation = new Vector3(_pitch, _yaw, 0);
                break;

            case InputEventKey { Pressed: true, Keycode: Key.Escape }:
                SetMouseCaptured(false);
                break;

            case InputEventMouseButton { Pressed: true } when !_mouseCaptured:
                SetMouseCaptured(true);
                break;
        }
    }

    public override void _Process(double delta)
    {
        if (!FlightEnabled)
        {
            return;
        }

        var direction = Vector3.Zero;

        // Basis columns rather than named axes: movement follows where we are looking,
        // including pitch, which is what makes a flycam feel right.
        if (Input.IsKeyPressed(Key.W)) direction -= Basis.Z;
        if (Input.IsKeyPressed(Key.S)) direction += Basis.Z;
        if (Input.IsKeyPressed(Key.A)) direction -= Basis.X;
        if (Input.IsKeyPressed(Key.D)) direction += Basis.X;

        // Vertical stays world-aligned — pitched up/down movement would be disorienting.
        if (Input.IsKeyPressed(Key.Space)) direction += Vector3.Up;
        if (Input.IsKeyPressed(Key.Ctrl)) direction -= Vector3.Up;

        if (direction == Vector3.Zero) return;

        float speed = MoveSpeed;
        if (Input.IsKeyPressed(Key.Shift)) speed *= SprintMultiplier;
        if (Input.IsKeyPressed(Key.Alt)) speed *= CrawlMultiplier;

        GlobalPosition += direction.Normalized() * speed * (float)delta;
    }

    private void SetMouseCaptured(bool captured)
    {
        _mouseCaptured = captured;
        Input.MouseMode = captured
            ? Input.MouseModeEnum.Captured
            : Input.MouseModeEnum.Visible;
    }
}
