using Godot;
using MinerCraft.Core.Voxels;

namespace MinerCraft.Game.Player;

/// <summary>
/// Debug free camera. Replaced by a real player controller with AABB collision at M4;
/// until then this is how we look at the world.
/// </summary>
public partial class FlyCam : Camera3D
{
    /// <summary>Metres per second. Deliberately in metres, not voxels — a walking pace
    /// is about 1.4 m/s, so 6 m/s is a brisk fly and gives a feel for real scale.</summary>
    [Export] public float MoveSpeed { get; set; } = 6.0f;

    [Export] public float SprintMultiplier { get; set; } = 5.0f;
    [Export] public float CrawlMultiplier { get; set; } = 0.2f;
    [Export] public float MouseSensitivity { get; set; } = 0.0025f;

    /// <summary>Where the camera aims on startup, in metres.</summary>
    [Export] public Vector3 InitialTarget { get; set; } = new(0.5f, 0.5f, 0.5f);

    private const float PitchLimit = 1.55f; // just shy of ±90°, so up never flips

    private float _yaw;
    private float _pitch;
    private bool _mouseCaptured;

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
            $"MinerCraft M0 — voxel {VoxelConstants.VoxelSize} m, " +
            $"chunk {VoxelConstants.ChunkSize}³ ({VoxelConstants.ChunkSizeMetres} m)\n" +
            "  WASD move, Space/Ctrl up-down, Shift sprint, Alt crawl\n" +
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
