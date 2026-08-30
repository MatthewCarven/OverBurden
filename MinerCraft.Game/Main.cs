using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using MinerCraft.Core.Content;
using MinerCraft.Core.Jobs;
using MinerCraft.Core.Meshing;
using MinerCraft.Core.Mining;
using MinerCraft.Core.Physics;
using MinerCraft.Core.Streaming;
using MinerCraft.Core.Voxels;
using MinerCraft.Core.Worldgen;
using MinerCraft.Game.Diagnostics;
using MinerCraft.Game.Player;
using MinerCraft.Game.Rendering;

namespace MinerCraft.Game;

/// <summary>
/// M2c entry point: load content, then stream terrain around the camera through the job
/// scheduler and the greedy mesher.
/// </summary>
/// <remarks>
/// Everything that decides <em>what</em> exists lives in <c>MinerCraft.Core</c>. This class
/// only reports where the camera is, turns finished meshes into <c>ArrayMesh</c>es, and
/// frees RIDs — the Godot half of the seam, and deliberately thin.
/// </remarks>
public partial class Main : Node3D
{
    [Export] public int ViewDistanceHorizontal { get; set; } = 4;

    /// <summary>
    /// Separate from the horizontal radius on purpose — a mining game's content is below the
    /// player. Kept modest until M2d has measured what the load actually costs.
    /// </summary>
    [Export] public int ViewDistanceVertical { get; set; } = 2;

    /// <summary>Jobs submitted per frame. Bounds the sim-thread cost of a fast traverse.</summary>
    [Export] public int MaxSubmissionsPerFrame { get; set; } = 64;

    private readonly Dictionary<ChunkPos, VoxelRenderInstance> _instances = [];
    private readonly MeshFreeQueue _freeQueue = new();

    private JobScheduler? _scheduler;
    private ChunkPipeline? _pipeline;
    private WorldGenerator? _generator;
    private ViewerId _viewer;
    private FlyCam? _camera;
    private ShaderMaterial? _material;
    private ChunkPos _lastViewerChunk;
    private double _sinceReport;

    private MiningController? _mining;
    private VoxelHighlight? _highlight;
    private readonly List<ToolDefinition> _tiers = [];

    private PlayerController? _player;
    private MaterialTable? _materials;
    private bool _announcedSpawn;

    private StreamingProfiler? _profiler;
    private float _flySpeed;
    private int? _caveLatticeStep;
    private TimeSpan? _applyBudget;
    private TimeSpan? _lightBudget;
    private bool _mineWhileProfiling;
    private bool _fallProfile;
    private bool _ambientOcclusion = true;
    private bool _lightInMergeKey = true;
    private float _skyBrightness = 1f;
    private float _fallHeightMetres = 600f;
    private int _restingSteps;
    private int _restedSteps;
    private int _falls;
    private float? _terminalSpeed;

    /// <summary>Whether <c>--speed</c> was given, as opposed to left at its default.</summary>
    /// <remarks>
    /// <b>Needed so that <c>--speed=0</c> can mean nought rather than "unset".</b> A profile
    /// defaults to 40 m/s because a stationary streaming measurement measures a system that has
    /// stopped doing the thing being measured — but M5d's fill measurement is a
    /// <em>standing start</em> by definition, and without this the one arm it needs is the one
    /// arm that cannot be asked for.
    /// </remarks>
    private bool _speedGiven;

    /// <summary>When streaming started, for the standing-start fill measurements.</summary>
    private long _fillStartedTicks;

    /// <summary>Seconds from the first frame to the first chunk on screen, or −1.</summary>
    /// <remarks>
    /// <b>The number the light budget was swept against at M5c-1, and the one M5 moved.</b>
    /// Nothing meshes until its whole neighbourhood is lit, so light does not delay drawing a
    /// little — at too small a budget it delays it entirely, and the 5 s report showed a world
    /// fully generated with nothing drawn at all. Recorded beside the settle time because the
    /// two answer different questions: this one is what a player waits for.
    /// </remarks>
    private double _firstDrawSeconds = -1.0;

    /// <summary>Seconds until the light field first settled, or −1.</summary>
    private double _lightSettledSeconds = -1.0;

    /// <summary>Seconds until nothing at all was outstanding, or −1.</summary>
    private double _settledSeconds = -1.0;

    public GameContent Content { get; } = new();

    public override void _Ready()
    {
        LoadContent();

        _camera = GetNodeOrNull<FlyCam>("FlyCam");

        int margin = ApplyCommandLineOverrides();

        // After the overrides, not before: --skybrightness is a shader uniform set at build.
        _material = VoxelMaterial.Create(_skyBrightness);

        var settings = new WorldGenSettings { Seed = 0x4D494E455243UL };
        if (_caveLatticeStep is int caveStep)
        {
            settings = settings with { CaveLatticeStep = caveStep };
        }
        _generator = new WorldGenerator(settings, BuildStrata());
        _materials = MaterialTable.From(Content.Materials);

        var viewers = new ViewerSet();
        _scheduler = new JobScheduler(viewers);
        _pipeline = new ChunkPipeline(
            _generator,
            _materials,
            _scheduler,
            viewers,
            onMeshReady: OnMeshReady,
            onMeshDiscarded: OnMeshDiscarded,
            onJobFaulted: (position, error) => GD.PushError($"Chunk job failed at {position}: {error}"),
            loadMargin: margin,
            resultBudget: _applyBudget,
            ambientOcclusion: _ambientOcclusion,
            lightBudget: _lightBudget,
            light: _lightInMergeKey);

        SetUpPlayer(_generator);

        _lastViewerChunk = ViewerChunk();
        _viewer = _pipeline.Streamer.AddViewer(
            _lastViewerChunk, ViewDistanceHorizontal, ViewDistanceVertical);

        SetUpMining();

        // Not MaterialId(), which falls back to air: placing air where a missing mod's lamp
        // should go would carve a hole out of whatever the player was pointing at.
        _lampMaterialId = Content.Materials.TryGetNumericId(Identifier.Parse(LampId), out int lamp)
            ? lamp
            : -1;

        // Last, so the fill measurement starts where streaming does rather than where content
        // loading does. Loading the mods is a fixed cost that is not what M5d is asking about.
        _fillStartedTicks = Stopwatch.GetTimestamp();

        GD.Print(
            $"M2c: streaming r{ViewDistanceHorizontal}h/{ViewDistanceVertical}v " +
            $"+{_pipeline.Streamer.LoadMargin} margin on {_scheduler.ThreadCount} worker(s), " +
            $"vertex AO {(_ambientOcclusion ? "on" : "off")}, " +
            $"light in merge key {(_lightInMergeKey ? "on" : "off")}");
    }

    /// <summary>
    /// Builds the player body and puts the camera where its eyes will be.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A profile run starts in fly mode, and that is the whole of M4c's contract with
    /// M2d and M3d.</b> <c>--profile</c> assigns the camera's position directly and
    /// <see cref="AdvanceFlythrough"/> snaps it to the surface every frame; a body that also
    /// moved the camera would fight both. In fly mode the body is inert, so every number M2d
    /// and M3d produced stays comparable with M4d's.
    /// </para>
    /// <para>
    /// The camera is placed at the spawn <em>eye</em> rather than left where the scene put it,
    /// so walk mode opens with no snap on the frame the world finishes loading. The spawn
    /// column comes from <c>SurfaceHeightAt</c>, which is a pure function of the generator and
    /// needs no chunk to have loaded — which is the only reason there is a sensible place to
    /// point the camera before the streamer has produced anything.
    /// </para>
    /// </remarks>
    private void SetUpPlayer(WorldGenerator generator)
    {
        if (_camera is null)
        {
            return;
        }

        // Flying is a measurement instrument, so a profile run is not allowed to walk — except
        // the fall profile, where walking (falling, specifically) is the thing being measured.
        PlayerMode mode = _fallProfile || (_profiler is null && _flySpeed <= 0f)
            ? PlayerMode.Walk
            : PlayerMode.Fly;

        Vector3 scenePosition = _camera.GlobalPosition;
        VoxelPos column = VoxelPos.FromMetres(scenePosition.X, scenePosition.Y, scenePosition.Z);
        System.Numerics.Vector3 feet = PlayerSpawn.FeetOnSurface(
            generator.SurfaceHeightAt(column.X, column.Z), scenePosition.X, scenePosition.Z);

        if (_fallProfile)
        {
            // Spawned high rather than teleported up once the window opens. A teleport of
            // several hundred metres moves the viewer dozens of chunks and reloads the whole
            // box, and that spike would land inside the measured window and be reported as the
            // cost of falling. Spawning high puts the reload in the warmup instead, where the
            // initial fill already lives.
            feet.Y += _fallHeightMetres;
        }

        PlayerMotionConfig config = _terminalSpeed is float terminal
            ? PlayerMotionConfig.Default with { TerminalSpeedMetresPerSecond = terminal }
            : PlayerMotionConfig.Default;

        _player = new PlayerController(config, feet, mode);

        if (mode == PlayerMode.Walk)
        {
            _camera.FlightEnabled = false;
            _camera.GlobalPosition = ToGodot(_player.EyePosition);
        }
        else
        {
            PlaceCameraAboveTerrain(generator);
        }

        GD.Print(
            $"M4c: {mode.ToString().ToLowerInvariant()} mode, body {config.WidthMetres:F2} x " +
            $"{config.HeightMetres:F2} m, eye {config.EyeHeightMetres:F2} m, " +
            $"spawn feet {feet.Y:F2} m");
    }

    /// <summary>
    /// Reads <c>--profile</c>, <c>--radius</c>, <c>--vradius</c>, <c>--margin</c>,
    /// <c>--speed</c>, <c>--warmup</c>, <c>--measure</c>, <c>--cavestep</c>,
    /// <c>--applybudget</c>, <c>--lightbudget</c> and <c>--ao</c> from the command line, after a bare
    /// <c>--</c>.
    /// </summary>
    /// <remarks>
    /// Configurable from outside so a sweep is one shell loop rather than five edit-rebuild
    /// cycles — which is what makes comparing view distances, and margin 2 against 1,
    /// cheap enough to actually do.
    /// </remarks>
    private int ApplyCommandLineOverrides()
    {
        int margin = ChunkStreamer.DefaultLoadMargin;
        double warmup = 3.0;
        double measure = 15.0;
        bool profile = false;

        foreach (string argument in OS.GetCmdlineUserArgs())
        {
            string[] parts = argument.Split('=', 2);
            string name = parts[0].TrimStart('-');
            string value = parts.Length > 1 ? parts[1] : string.Empty;

            switch (name)
            {
                case "radius" when int.TryParse(value, out int radius):
                    ViewDistanceHorizontal = radius;
                    break;

                case "vradius" when int.TryParse(value, out int vertical):
                    ViewDistanceVertical = vertical;
                    break;

                case "margin" when int.TryParse(value, out int parsed):
                    margin = parsed;
                    break;

                case "speed" when float.TryParse(value, out float speed):
                    _flySpeed = speed;
                    _speedGiven = true;
                    break;

                case "warmup" when double.TryParse(value, out double seconds):
                    warmup = seconds;
                    break;

                case "measure" when double.TryParse(value, out double seconds):
                    measure = seconds;
                    break;

                // A test seam, not a setting. Step 1 is per-voxel cave evaluation — the way
                // generation worked before M2d-2 — so a sweep can measure what the lattice
                // bought in the real streaming loop rather than only on the bench.
                case "cavestep" when int.TryParse(value, out int step):
                    _caveLatticeStep = step;
                    break;

                // Also a test seam. A budget large enough to never bind reproduces the
                // unbudgeted behaviour, which is the A/B this was built to settle.
                case "applybudget" when double.TryParse(value, out double budgetMillis):
                    _applyBudget = TimeSpan.FromMilliseconds(budgetMillis);
                    break;

                // The fourth seam of the same shape, and the one M5c-1 needed the moment it
                // existed: light derives and propagates on the sim thread, and how much of a
                // frame it may take is the whole of how long a standing start waits before it
                // can draw anything. Sweepable rather than rebuilt, for the same reason the
                // three above are.
                case "lightbudget" when double.TryParse(value, out double lightMillis):
                    _lightBudget = TimeSpan.FromMilliseconds(lightMillis);
                    break;

                // The third test seam of the same shape, and the one M5a exists to read.
                // --ao=0 restores the constant-brightness merge key every mesh used before
                // M5a, so the geometry line of one run against another is exactly what
                // per-vertex AO costs in quads. That is half of M5's fragment-shader
                // decision, and it is available a milestone before the decision is due.
                case "ao" when int.TryParse(value, out int ao):
                    _ambientOcclusion = ao != 0;
                    break;

                // The other half of the same A/B, and the two are deliberately independent.
                // M5-PLAN asks for AO off / AO on / AO+light on; having both switches gives the
                // fourth arm free, which is light with no occlusion — the only way to price
                // light in the merge key on its own rather than on top of AO.
                case "light" when int.TryParse(value, out int light):
                    _lightInMergeKey = light != 0;
                    break;

                // Scales the sky channel in the shader and nothing else. 0 leaves only block
                // light, which is what says the two channels really are independent all the way
                // to the screen rather than only in the mesher.
                case "skybrightness" when float.TryParse(value, out float brightness):
                    _skyBrightness = brightness;
                    break;

                // Places one lamp where the crosshair points, so a screenshot can show block
                // light without anybody pressing L. See PlaceScriptedLamp.
                case "debuglamp":
                    _debugLamp = true;
                    break;

                case "profile":
                    profile = true;
                    break;

                // Holds the button down for the whole measured window. M3-PLAN asks for the
                // cost of mining *continuously*, not of one swing, and a person holding a
                // mouse is not a repeatable measurement.
                case "mine":
                    _mineWhileProfiling = true;
                    break;

                // The fall profile. M2d measured a viewer flying horizontally at a constant
                // height, which is the one motion the flythrough was built to produce and is
                // nothing like a player falling down a shaft: a fall is vertical, it is fast,
                // and the vertical view radius is the small one. It is the first motion that
                // can genuinely arrive somewhere before the world does.
                case "fall":
                    _fallProfile = true;
                    profile = true;
                    break;

                case "fallheight" when float.TryParse(value, out float height):
                    _fallHeightMetres = height;
                    break;

                // Terminal velocity from outside, because it is the constant this whole profile
                // exists to pick and sweeping it by rebuilding is how a sweep does not get done.
                // Same argument as --radius. M4-PLAN: "terminal velocity is a streaming
                // parameter in a physics costume".
                case "terminal" when float.TryParse(value, out float terminal):
                    _terminalSpeed = terminal;
                    break;
            }
        }

        if (profile)
        {
            _profiler = new StreamingProfiler { WarmupSeconds = warmup, MeasureSeconds = measure };

            // Flying is the test for the streaming profile. Standing still measures a system
            // that has stopped doing the thing being measured. The *fall* profile is the
            // exception: there the body is the motion, so the scripted flythrough must stay off
            // or it would assign the camera out from under the player every frame.
            if (_flySpeed <= 0f && !_fallProfile && !_speedGiven)
            {
                _flySpeed = 40f;
            }
        }

        return margin;
    }

    /// <summary>
    /// One fixed-step solve of the player body, and the camera moved onto its eyes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Here rather than in <see cref="_Process"/> because the step length has to be
    /// constant.</b> M4b's <c>Step</c> takes <c>dt</c> as an argument precisely so Godot owns
    /// the rate, and Godot's physics accumulator is the thing that already does fixed-step
    /// work. A jump apex that changed with the frame rate would get diagnosed as a physics bug.
    /// </para>
    /// <para>
    /// <b>The order M4-PLAN specifies is player → viewer → pipeline</b>, and it holds because
    /// Godot runs every physics tick of a frame before that frame's <c>_Process</c>. So the
    /// body is solved against the freshest world the streamer has managed, and the viewer that
    /// decides what loads next is placed from where the body actually ended up. Running the
    /// pipeline first would solve the player against a world one frame staler than necessary,
    /// on the frame where being current is the entire point.
    /// </para>
    /// </remarks>
    public override void _PhysicsProcess(double delta)
    {
        if (_player is null || _pipeline is null || _materials is null || _camera is null)
        {
            return;
        }

        long before = Stopwatch.GetTimestamp();
        bool stepped = _player.TryStep(_pipeline.Streamer, _materials, ReadIntent(), (float)delta);
        double stepMillis = Milliseconds(before, Stopwatch.GetTimestamp());

        if (!stepped)
        {
            return;
        }

        RecordPlayerStep(stepMillis);
        AdvanceFallProfile();

        if (!_announcedSpawn)
        {
            _announcedSpawn = true;
            GD.Print(
                $"M4c: spawned at {_player.FeetCentre} after {_player.StepsHeldForWorld} " +
                "step(s) held for the world");
        }

        _camera.GlobalPosition = ToGodot(_player.EyePosition);
    }

    /// <summary>
    /// Turns the keyboard and the camera's yaw into a <see cref="MoveIntent"/>.
    /// </summary>
    /// <remarks>
    /// The only place in the project that reads a key and means "walk". Direction comes from
    /// the camera flattened to the ground plane — see <see cref="FlyCam.WalkForward"/> for why
    /// it is normalised after flattening and not before — and the speed comes from the config,
    /// so M4d can retune walking without touching a binding.
    /// </remarks>
    private MoveIntent ReadIntent()
    {
        if (_camera is null || !_camera.MouseCaptured)
        {
            // Esc released the mouse. Stopping the walk as well as the look, for the reason
            // FlyCam.MouseCaptured gives about mining: clicking on another window should not
            // also march the player off a cliff.
            return MoveIntent.Still;
        }

        Vector3 direction = Vector3.Zero;
        if (Input.IsKeyPressed(Key.W)) direction += _camera.WalkForward;
        if (Input.IsKeyPressed(Key.S)) direction -= _camera.WalkForward;
        if (Input.IsKeyPressed(Key.D)) direction += _camera.WalkRight;
        if (Input.IsKeyPressed(Key.A)) direction -= _camera.WalkRight;

        return new MoveIntent(
            new System.Numerics.Vector3(direction.X, direction.Y, direction.Z),
            Jump: Input.IsKeyPressed(Key.Space),
            Sprint: Input.IsKeyPressed(Key.Shift));
    }

    /// <summary>The walk/fly toggle. Everything else about input belongs to its own node.</summary>
    /// <remarks>
    /// On a key-down edge rather than polled, because a toggle read every frame flips sixty
    /// times a second. <c>FlyCam</c> sees the event first and does not consume F.
    /// </remarks>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (_player is null || _camera is null
            || @event is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.F })
        {
            return;
        }

        if (_profiler is not null || _flySpeed > 0f)
        {
            // A scripted flythrough owns the camera. Handing it to a body mid-run would
            // invalidate the measurement rather than produce an interesting one.
            GD.Print("M4c: walk mode is unavailable during a profile run.");
            return;
        }

        var eye = new System.Numerics.Vector3(
            _camera.GlobalPosition.X, _camera.GlobalPosition.Y, _camera.GlobalPosition.Z);

        _player.ToggleMode(eye);
        _camera.FlightEnabled = _player.Mode == PlayerMode.Fly;
        _announcedSpawn = _player.Mode == PlayerMode.Fly;

        GD.Print($"M4c: {_player.Mode.ToString().ToLowerInvariant()} mode");
    }

    private static Vector3 ToGodot(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>Feeds one solved step to the profiler.</summary>
    /// <remarks>
    /// <b>The step is timed directly, not read off <c>frame ms</c>.</b> That trap has been walked
    /// into three times — M2d-1, the mesh-apply budget and M3d — and every time the spike failed
    /// to reproduce while the directly-timed number moved a tenth of a millisecond. This is a
    /// stopwatch around <c>TryStep</c> and nothing else.
    /// </remarks>
    private void RecordPlayerStep(double milliseconds)
    {
        if (_profiler is not { Warm: true } || _player is null)
        {
            return;
        }

        PlayerStepResult step = _player.LastStep;

        // "At rest" is grounded, barely moving, and standing on something that actually
        // exists. All three matter, and the third was learned the hard way: a body held on an
        // unloaded chunk is grounded with its velocity zeroed by the contact, so it looked like
        // rest — and the step where the chunk arrived and turned out to be air then scored a
        // ground-state flicker. The r16/120 sweep reported 5 "flickers" over 28 "resting" steps
        // that were entirely the held-then-released pattern StepsHeld already counts.
        bool atRest = step.Grounded
            && !step.HeldByUnloadedFloor
            && step.Velocity.LengthSquared() < RestingSpeedSquared;

        if (atRest)
        {
            _restingSteps++;
        }

        _profiler.AddPlayerStep(
            milliseconds,
            step.Substeps,
            step.VoxelQueries,
            step.Grounded,
            step.HeldByUnloadedFloor,
            atRest);
    }

    /// <summary>0.01 m²/s² — a tenth of a metre per second, squared.</summary>
    private const float RestingSpeedSquared = 0.01f;

    /// <summary>Steps to stand still at the bottom of a fall before launching the next one.</summary>
    /// <remarks>
    /// One second at 60 Hz, and it is not padding: a fall profile that relaunched the instant it
    /// landed would never put the body at rest, so the ground-flicker detector would never run
    /// and its zero would mean nothing. This is what gives that counter something to count.
    /// </remarks>
    private const int RestStepsBeforeRelaunch = 60;

    /// <summary>
    /// Drops the body again once it has landed and rested, so a long window measures falling.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Each launch moves horizontally past the loaded box</b>, because a second fall down the
    /// same column would descend through chunks that are already resident and measure nothing.
    /// The stride is the full box plus a margin, so every fall meets fresh terrain.
    /// </para>
    /// <para>
    /// The cost is honest and is reported: the horizontal jump reloads the whole box, and that
    /// spike lands inside the measured window. One fall is the clean measurement; the fall count
    /// in the report is what says how much of the window was one.
    /// </para>
    /// </remarks>
    private void AdvanceFallProfile()
    {
        if (!_fallProfile || _player is null || _generator is null)
        {
            return;
        }

        if (!_player.State.Grounded)
        {
            _restedSteps = 0;
            return;
        }

        // Only rest that is actually rest. A body held on an unloaded chunk is "grounded" and is
        // the opposite of landed — relaunching there would cut the measurement short at exactly
        // the moment it was producing the number the profile exists for.
        if (_player.LastStep.HeldByUnloadedFloor)
        {
            _restedSteps = 0;
            return;
        }

        if (++_restedSteps < RestStepsBeforeRelaunch)
        {
            return;
        }

        _restedSteps = 0;
        _falls++;

        float stride = ((2 * ViewDistanceHorizontal) + 4) * VoxelConstants.ChunkSizeMetres;
        System.Numerics.Vector3 feet = _player.FeetCentre;
        float x = feet.X + stride;

        VoxelPos column = VoxelPos.FromMetres(x, 0f, feet.Z);
        System.Numerics.Vector3 launch = PlayerSpawn.FeetOnSurface(
            _generator.SurfaceHeightAt(column.X, column.Z), x, feet.Z);
        launch.Y += _fallHeightMetres;

        _player.Teleport(launch);
        _announcedSpawn = true;

        // A relaunch is a body at rest becoming a body in mid-air, which is the exact shape of
        // the thing the flicker counter looks for. Tell it this one was deliberate.
        _profiler?.ForgetGroundState();
    }

    public override void _Process(double delta)
    {
        if (_pipeline is null)
        {
            return;
        }

        AdvanceFlythrough(delta);
        UpdateMining(delta);

        // The viewer only moves in whole chunks. Comparing first means a camera drifting
        // inside one chunk never touches the streaming policy at all.
        ChunkPos current = ViewerChunk();
        if (current != _lastViewerChunk)
        {
            _lastViewerChunk = current;
            _pipeline.Streamer.MoveViewer(_viewer, current);
        }

        long before = Stopwatch.GetTimestamp();
        _pipeline.Update(MaxSubmissionsPerFrame);
        long afterUpdate = Stopwatch.GetTimestamp();
        _freeQueue.Drain();
        long afterFree = Stopwatch.GetTimestamp();

        if (_profiler is { Warm: true })
        {
            _profiler.PipelineUpdate.Add(Milliseconds(before, afterUpdate));
            _profiler.MeshFree.Add(Milliseconds(afterUpdate, afterFree));

            // From the pipeline's own clock rather than a stopwatch out here, because the light
            // step is nested inside the update and timing it from the caller would have to
            // bracket the whole thing. It is already counted in update ms; see Light's remarks.
            _profiler.Light.Add(_pipeline.LastLightStepMillis);
        }

        RecordFillMilestones();

        if (_profiler is not null && _profiler.Tick(delta))
        {
            PrintProfile();
            GetTree().Quit();
            return;
        }

        Report(delta);
    }

    private static double Milliseconds(long from, long to) =>
        (to - from) * 1000.0 / Stopwatch.Frequency;

    /// <summary>
    /// Notes when the world first drew, when light first settled, and when everything did.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Three moments rather than one, because M5 moved the first and M5-PLAN estimated the
    /// second.</b> The plan put ~0.25 s against the propagation and nothing against the derive,
    /// which M5c-1 then measured as the larger half — so "how long does a standing start take to
    /// light" needs the light's own answer and the player's answer side by side.
    /// </para>
    /// <para>
    /// <b>Latched once each and never reset.</b> A flythrough crosses chunk boundaries forever,
    /// so light goes in and out of flight all run; only the first settle is the fill. Reading a
    /// later one would report how long the last chunk column took and label it the world.
    /// </para>
    /// <para>
    /// Recorded on every run, not only a profile, so the periodic report can print them from a
    /// plain headless run with no window and no measured window.
    /// </para>
    /// </remarks>
    private void RecordFillMilestones()
    {
        if (_pipeline is null)
        {
            return;
        }

        if (_firstDrawSeconds < 0.0 && _instances.Count > 0)
        {
            _firstDrawSeconds = FillSeconds();
        }

        if (_lightSettledSeconds < 0.0 && _pipeline.LightBuilds > 0 && !_pipeline.LightInFlight)
        {
            _lightSettledSeconds = FillSeconds();
        }

        // Guarded on having drawn something as well as on being quiescent: the very first frame
        // is quiescent by construction — nothing has been asked for yet — and would latch a
        // settle time of zero for a world that had not started.
        if (_settledSeconds < 0.0 && _instances.Count > 0 && _pipeline.IsQuiescent)
        {
            _settledSeconds = FillSeconds();
        }
    }

    private double FillSeconds() =>
        (Stopwatch.GetTimestamp() - _fillStartedTicks) / (double)Stopwatch.Frequency;

    /// <summary>The standing-start fill, as one line.</summary>
    /// <remarks>
    /// Each figure prints "not yet" rather than a zero when its moment has not arrived. A
    /// flythrough at 40 m/s never settles, and "0.00 s" for that would read as instantaneous
    /// rather than as never — the same trap the ground-flicker detector fell into twice.
    /// </remarks>
    private string FillLine()
    {
        static string Seconds(double value) => value < 0.0 ? "not yet" : $"{value:F2} s";

        return $"fill      first draw {Seconds(_firstDrawSeconds)}, "
               + $"light settled {Seconds(_lightSettledSeconds)}, "
               + $"all settled {Seconds(_settledSeconds)}\n";
    }

    /// <summary>What the light step cost, with every total's denominator beside it.</summary>
    /// <remarks>
    /// <b>The derive/propagate split is the point.</b> M5-PLAN costed the walk at ~779k visits
    /// and ~15 ms and said nothing about the derive; M5c-1 measured the derive as the larger
    /// half by an order of magnitude. Printing them apart is what keeps that statement true or
    /// catches it going stale, and it is the number <c>PaddedChunk.CopyFrom</c> would move.
    /// </remarks>
    private string LightLines()
    {
        if (_pipeline is null || _profiler is null)
        {
            return string.Empty;
        }

        double seconds = Math.Max(_profiler.MeasuredSeconds, 0.001);
        double derive = _pipeline.LightDeriveMillis;
        double propagate = _pipeline.LightPropagateMillis;
        long cells = _pipeline.LightCellsVisited;

        // Per-cell cost against the walk alone, not against the whole step. M5-PLAN's ~20 ns
        // guess is a guess about a cell visit, and dividing by the derive as well would compare
        // it against a different quantity and call the difference a prediction error.
        string perCell = cells > 0 ? $"{propagate * 1e6 / cells:F0} ns/cell" : "no cells";

        return $"{_profiler.Light.Line("light ms")}   (inside update ms, not beside it)\n"
               + $"lighting  {derive:F0} ms deriving / {propagate:F0} ms propagating "
               + $"over the whole run\n"
               + $"cells     {cells} visited, {cells / seconds:F0}/s over the "
               + $"{seconds:F1} s window, {perCell}\n"
               + $"derives   {_pipeline.LightBuilds} / {_pipeline.Streamer.TrackedCount} tracked, "
               + $"{_pipeline.UniformLightBuilds} uniform "
               + $"({(_pipeline.LightBuilds > 0 ? $"{100.0 * _pipeline.UniformLightBuilds / _pipeline.LightBuilds:F0}%" : "n/a")}), "
               + $"{_pipeline.RelightRemeshes} relight remesh(es)\n";
    }

    /// <summary>
    /// Builds the mining controller and the target highlight, and lists the tiers.
    /// </summary>
    /// <remarks>
    /// The tiers come out of the registry in numeric-ID order, which is load order, so the
    /// number keys are stable between runs and a mod that adds a tier simply appears at the
    /// end. Nothing here knows a tool by name.
    /// </remarks>
    private void SetUpMining()
    {
        foreach ((_, _, ToolDefinition tier) in Content.Tools.Entries())
        {
            _tiers.Add(tier);
        }

        if (_tiers.Count == 0)
        {
            GD.PushWarning("MinerCraft: no tool tiers loaded, so there is nothing to mine with.");
            return;
        }

        // The strongest tier by default. Starting on the weakest would be a truer tutorial,
        // but the first swing at stone would then do nothing and read as a broken build.
        ToolDefinition strongest = _tiers[0];
        foreach (ToolDefinition tier in _tiers)
        {
            if (tier.HardnessThreshold > strongest.HardnessThreshold)
            {
                strongest = tier;
            }
        }

        // Reach comes from the player, who owns it as of M4c. Nothing here has a default to
        // fall back on, which is the point — MiningController.ReachMetres is `required`.
        _mining = new MiningController(_pipeline!, _materials!, strongest)
        {
            ReachMetres = _player?.ReachMetres ?? PlayerController.DefaultReachMetres,
        };

        _highlight = new VoxelHighlight(GetWorld3D());

        var names = new List<string>();
        for (int i = 0; i < _tiers.Count; i++)
        {
            names.Add($"{i + 1}:{_tiers[i].Id.Path}");
        }

        GD.Print($"M3d: mining with {strongest.Id} — tiers {string.Join(", ", names)}");
    }

    /// <summary>
    /// One frame of mining: aim, outline the target, and swing if the button is down.
    /// </summary>
    /// <remarks>
    /// The swing is timed separately from <see cref="ChunkPipeline.Update"/> because they are
    /// different costs answering different questions. This is the carve itself on the sim
    /// thread; <c>update ms</c> is where the remeshes it caused come back and get turned into
    /// meshes, a frame or more later. M3's real question — does a pick swing fit inside a
    /// frame? — is about the second number, and this one is here to prove it is not the
    /// first.
    /// </remarks>
    private void UpdateMining(double delta)
    {
        if (_mining is null || _camera is null)
        {
            return;
        }

        SelectTier();

        Vector3 origin = _camera.GlobalPosition;
        Vector3 forward = -_camera.GlobalTransform.Basis.Z;
        bool mining = _camera.MouseCaptured && Input.IsMouseButtonPressed(MouseButton.Left);

        // Ctrl takes exactly the highlighted voxel instead of the tool's sphere. A held
        // modifier rather than a toggle, because it is the exception and not the mode: the
        // sphere is what mining *is* here, and precision is for the person who has stopped
        // excavating and started building.
        //
        // Ctrl is free in walk mode. In fly mode it is also FlyCam's descend, so precision
        // mining while flying sinks — see the M4d note in TODO.md; swapping to Alt is one line
        // if that reads badly.
        bool precise = mining && Input.IsKeyPressed(Key.Ctrl);

        if (_mineWhileProfiling)
        {
            // Straight down from a camera four metres above the surface. Aiming along the
            // flight path would spend most of the run pointing at sky, and a measurement of
            // mining that mostly misses is not one.
            forward = Vector3.Down;
            mining = true;

            // Spheres, always, and stated rather than left to fall out of "no key is held".
            // M3d's baseline — 1.2 clones and 2.4 KiB per swing, two orders of magnitude
            // inside a frame — measures the sphere, which is the expensive shape. A profile
            // that quietly chipped would report a cheaper number under the same label and
            // read as an improvement.
            precise = false;
        }

        long before = Stopwatch.GetTimestamp();

        bool swung = _mining.Update(
            delta,
            new System.Numerics.Vector3(origin.X, origin.Y, origin.Z),
            new System.Numerics.Vector3(forward.X, forward.Y, forward.Z),
            mining,
            precise);

        if (swung && _profiler is { Warm: true })
        {
            _profiler.Swing.Add(Milliseconds(before, Stopwatch.GetTimestamp()));
        }

        PlaceDebugLamp();

        if (_highlight is null)
        {
            return;
        }

        if (_mining.HasTarget)
        {
            _highlight.ShowAt(_mining.Hit.Voxel);
        }
        else
        {
            _highlight.Hide();
        }
    }

    /// <summary>
    /// L places one lamp in the empty voxel in front of the crosshair.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>M5b needs a light source in the world and M6 owns placement.</b> So: one key, one
    /// material, no inventory, no cooldown, no tier — the smallest thing that makes M5d
    /// playable, and small enough that nobody mistakes it for the feature.
    /// </para>
    /// <para>
    /// On a key-down edge rather than polled, for the reason the walk/fly toggle is: a placement
    /// read every frame lays sixty lamps a second. Nothing happens visually until M5c wires
    /// light into the pipeline — the lamp is a pale block that does not yet glow.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Places one lamp where the crosshair is pointing, once, without a keypress.
    /// </summary>
    /// <remarks>
    /// <b>Exists because a screenshot cannot press L, and "does block light reach the screen" is
    /// a look question.</b> With <c>--skybrightness=0</c> the lamp is the only light in the
    /// world, so the shot answers the one link the sky channel cannot: that
    /// <c>COLOR.g</c> carries block light and is tinted separately. Fired once and only once —
    /// the same edge-triggering the L key needs, for the same reason M5b recorded, since a
    /// per-frame read lays sixty lamps a second.
    /// </remarks>
    private void PlaceScriptedLamp()
    {
        if (!_debugLamp || _scriptedLampPlaced || _mining is null || _lampMaterialId < 0) return;

        // Nothing to place against until the world under the crosshair has actually loaded.
        if (!_mining.HasTarget) return;

        _scriptedLampPlaced = _mining.TryPlaceDebug(_lampMaterialId);

        if (_scriptedLampPlaced)
        {
            GD.Print($"M5c: scripted lamp at {_mining.Hit.Previous}");
        }
    }

    private bool _debugLamp;
    private bool _scriptedLampPlaced;

    private void PlaceDebugLamp()
    {
        PlaceScriptedLamp();

        if (_mining is null || _camera is null || !_camera.MouseCaptured) return;

        bool pressed = Input.IsKeyPressed(Key.L);
        bool edge = pressed && !_lampKeyHeld;
        _lampKeyHeld = pressed;

        if (!edge || _lampMaterialId < 0) return;

        if (_mining.TryPlaceDebug(_lampMaterialId))
        {
            GD.Print($"M5b: placed {LampId} at {_mining.Hit.Previous}");
        }
    }

    /// <summary>The material the debug bind places, or −1 if the base game does not ship one.</summary>
    /// <remarks>
    /// Resolved once and by identifier, not baked in as a number: material IDs come from the
    /// world's table and a mod that is not installed leaves a hole. A missing lamp disables the
    /// bind rather than placing whatever happens to hold that slot.
    /// </remarks>
    private int _lampMaterialId = -1;

    private bool _lampKeyHeld;

    private const string LampId = "minercraft:lamp";

    private void SelectTier()
    {
        if (_mining is null)
        {
            return;
        }

        for (int i = 0; i < _tiers.Count && i < 9; i++)
        {
            if (!Input.IsKeyPressed(Key.Key1 + i) || ReferenceEquals(_mining.Tool, _tiers[i]))
            {
                continue;
            }

            _mining.Tool = _tiers[i];
            GD.Print(
                $"M3d: {_tiers[i].Id} — radius {_tiers[i].RadiusMetres:F2} m, " +
                $"takes hardness <= {_tiers[i].HardnessThreshold:F1}");
        }
    }

    /// <summary>
    /// Drives the camera along a fixed path during a profile run, hugging the surface.
    /// </summary>
    /// <remarks>
    /// A scripted path rather than a recorded one so two runs are comparable — the whole
    /// point is to put margin 2 against margin 1, or one view distance against another, and
    /// a hand-flown route differs more between runs than the thing being measured.
    /// Surface-following keeps terrain inside the vertical box, which is the case a player
    /// is actually in; flying at a fixed altitude would drift the ground out of view and
    /// quietly stop measuring meshing at all.
    /// </remarks>
    private void AdvanceFlythrough(double delta)
    {
        if (_flySpeed <= 0f || _camera is null || _generator is null)
        {
            return;
        }

        Vector3 position = _camera.GlobalPosition;
        position.X += _flySpeed * (float)delta;

        VoxelPos voxel = VoxelPos.FromMetres(position.X, position.Y, position.Z);
        int surface = _generator.SurfaceHeightAt(voxel.X, voxel.Z);
        position.Y = (surface + 16) * VoxelConstants.VoxelSize;

        _camera.GlobalPosition = position;
    }

    private void PrintProfile()
    {
        if (_pipeline is null || _profiler is null)
        {
            return;
        }

        ChunkStreamer streamer = _pipeline.Streamer;

        GD.Print(
            $"\n=== MinerCraft streaming profile ===\n" +
            $"config    r{ViewDistanceHorizontal}h/{ViewDistanceVertical}v " +
            $"+{streamer.LoadMargin} margin, {_scheduler!.ThreadCount} workers, " +
            (_fallProfile
                ? $"falling from {_fallHeightMetres:F0} m, terminal "
                  + $"{_player!.Config.TerminalSpeedMetresPerSecond:F0} m/s, {_falls + 1} fall(s)"
                : $"{_flySpeed:F0} m/s") +
            (_mining is null ? "\n" : $", {_mining.Tool.Id}\n") +
            $"{_profiler.PipelineUpdate.Line("update ms")}\n" +
            $"{_profiler.MeshFree.Line("free ms")}   (deferred by Godot — see MeshFreeQueue)\n" +
            $"{_profiler.Frame.Line("frame ms")}\n" +
            $"main      {_profiler.PipelineUpdate.Total() / Math.Max(_profiler.MeasuredSeconds, 0.001) / 10.0:F2}% " +
            $"of one core, streaming on the main thread\n" +
            $"chunks    {streamer.ViewedCount} viewed / {streamer.TrackedCount} tracked / " +
            $"{_instances.Count} drawn\n" +
            GeometryLine() +
            $"jobs      {_pipeline.CompletedJobs} done / {_pipeline.DroppedJobs} dropped / " +
            $"{_pipeline.FaultedJobs} faulted\n" +
            $"applying  {_pipeline.AppliedResults} applied, {_pipeline.PendingResults} still " +
            $"waiting at the end of the run\n" +
            $"freeing   {_freeQueue.TotalFreed} released, peak queue {_freeQueue.PeakPending}\n" +
            FillLine() +
            LightLines() +
            MiningLines() +
            _profiler.PlayerLines(_profiler.MeasuredSeconds) +
            $"{StreamingProfiler.RenderLine()}\n" +
            $"{StreamingProfiler.MemoryLine()}\n");
    }

    /// <summary>
    /// The mining half of the profile: what a held button actually cost.
    /// </summary>
    /// <remarks>
    /// Per swing rather than per second, because a swing is the unit the player experiences
    /// and the cooldown makes the per-second figure a statement about the cooldown. The clone
    /// rate is here because M3-PLAN guessed at it — "roughly one 2 KB clone per chunk per
    /// edit-and-remesh cycle" — and said M3d would settle it.
    /// </remarks>
    private string MiningLines()
    {
        if (_mining is null || _mining.Swings == 0)
        {
            return string.Empty;
        }

        double swings = _mining.Swings;

        return
            $"{_profiler!.Swing.Line("swing ms")}   (raycast + carve, sim thread)\n" +
            $"mining    {_mining.Swings} swings, {_mining.VoxelsRemoved / swings:F0} removed / " +
            $"{_mining.VoxelsRefused / swings:F0} too hard / " +
            $"{_mining.VoxelsAlreadyAir / swings:F0} already air per swing\n" +
            $"remesh    {_pipeline!.EditRemeshes / swings:F1} chunks per swing, " +
            $"{_pipeline.EditRemeshes / Math.Max(_profiler.MeasuredSeconds, 0.001):F1}/s" +
            $" | relight {_pipeline.RelightRemeshes / swings:F1} per swing, " +
            $"{_pipeline.RelightRemeshes} total\n" +
            $"clones    {_pipeline.ChunkClones / swings:F1} per swing " +
            $"({_pipeline.ChunkClones * 2.0 / swings:F1} KiB at ~2 KiB each)\n" +
            $"clipped   {_mining.ClippedSwings} swing(s) reached past the loaded world\n";
    }

    /// <summary>
    /// What the drawn geometry costs, and the AO A/B M5a exists to read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Quads per drawn chunk is the number, not total quads.</b> Two runs at the same
    /// radius do not draw the same number of chunks — the cave lattice decides whether a
    /// given chunk meshes to nothing — so a total compares two things at once. The
    /// denominator is printed beside it for the reason every counter here prints its own.
    /// </para>
    /// <para>
    /// Mesh memory follows quads, at M2d's measured ~46 KiB per drawn chunk. Vertices are
    /// four per quad by construction and printed anyway, because the vertex count is what
    /// M5a's added <c>COLOR</c> channel multiplies against.
    /// </para>
    /// </remarks>
    /// <summary>The geometry half of the periodic report, compact.</summary>
    /// <remarks>
    /// Here as well as in the profile because a <b>standing start</b> is the exact A/B: the
    /// chunk set at a fixed radius is deterministic, so AO on against AO off compares two
    /// numbers rather than two samples of a moving flythrough.
    /// </remarks>
    private string QuadSummary()
    {
        long quads = 0;
        foreach (VoxelRenderInstance instance in _instances.Values) quads += instance.QuadCount;

        return $"quads {quads} ({quads / Math.Max(_instances.Count, 1)} per drawn chunk, "
               + $"AO {(_ambientOcclusion ? "on" : "off")}, "
               + $"light {(_lightInMergeKey ? "on" : "off")})";
    }

    private string GeometryLine()
    {
        long quads = 0;
        long vertices = 0;

        foreach (VoxelRenderInstance instance in _instances.Values)
        {
            quads += instance.QuadCount;
            vertices += instance.VertexCount;
        }

        double drawn = Math.Max(_instances.Count, 1);

        return $"geometry  {quads} quads / {vertices} vertices over {_instances.Count} drawn "
               + $"chunk(s) — {quads / drawn:F0} quads per chunk, "
               + $"AO {(_ambientOcclusion ? "on" : "off")}\n";
    }

    /// <summary>
    /// A chunk's mesh is ready. Runs on the main thread inside
    /// <see cref="ChunkPipeline.Update"/>, so touching the RenderingServer here is safe.
    /// </summary>
    private void OnMeshReady(StreamedChunk chunk, ChunkMesh mesh)
    {
        // Re-meshing an existing chunk replaces its instance; M3 makes that the common case.
        OnMeshDiscarded(chunk.Position);

        ArrayMesh? arrayMesh = ChunkMeshConverter.ToArrayMesh(mesh, _material!, _material!);
        if (arrayMesh is null)
        {
            // Nothing to draw — a chunk of open sky or of solid buried rock. Most chunks.
            return;
        }

        var (x, y, z) = chunk.Position.OriginMetres;
        _instances[chunk.Position] = new VoxelRenderInstance(
            GetWorld3D(),
            arrayMesh,
            new Transform3D(Basis.Identity, new Vector3(x, y, z)),
            mesh.QuadCount,
            mesh.Opaque.VertexCount + mesh.Transparent.VertexCount);
    }

    /// <summary>
    /// A chunk that had a mesh is gone.
    /// </summary>
    /// <remarks>
    /// Handed to <see cref="MeshFreeQueue"/> rather than freed here. Godot defers the actual
    /// deallocation, so releasing several hundred meshes in one frame stutters a frame or two
    /// <em>later</em> and gets blamed on meshing. The queue hides the instance immediately and
    /// spreads the frees over the following frames.
    /// </remarks>
    private void OnMeshDiscarded(ChunkPos position)
    {
        if (_instances.Remove(position, out VoxelRenderInstance? instance))
        {
            _freeQueue.Enqueue(instance);
        }
    }

    private ChunkPos ViewerChunk()
    {
        Vector3 position = _camera?.GlobalPosition ?? Vector3.Zero;
        return VoxelPos.FromMetres(position.X, position.Y, position.Z).Chunk;
    }

    /// <summary>
    /// Drops the camera just above the surface, so a fresh run opens on terrain rather than
    /// inside rock or high in empty sky.
    /// </summary>
    private void PlaceCameraAboveTerrain(WorldGenerator generator)
    {
        if (_camera is null)
        {
            return;
        }

        Vector3 position = _camera.GlobalPosition;
        VoxelPos voxel = VoxelPos.FromMetres(position.X, position.Y, position.Z);
        int surface = generator.SurfaceHeightAt(voxel.X, voxel.Z);

        float metres = (surface + 8) * VoxelConstants.VoxelSize;
        _camera.GlobalPosition = new Vector3(position.X, metres, position.Z);
    }

    /// <summary>Strata for the base game. Data already — moving it into mod JSON is on the TODO.</summary>
    private StrataTable BuildStrata() => new(
        [
            new StrataLayer(MaterialId("minercraft:grass"), 1),
            new StrataLayer(MaterialId("minercraft:dirt"), 3),
        ],
        MaterialId("minercraft:stone"));

    private void Report(double delta)
    {
        _sinceReport += delta;
        if (_sinceReport < 5.0 || _pipeline is null)
        {
            return;
        }

        _sinceReport = 0.0;
        ChunkStreamer streamer = _pipeline.Streamer;

        GD.Print(
            $"M2c: {streamer.ViewedCount} viewed ({streamer.TrackedCount} tracked), " +
            $"{_instances.Count} drawn, {_pipeline.JobsInFlight} in flight, " +
            $"{_pipeline.WorkListCount} queued, {_pipeline.PendingResults} to apply, " +
            $"{_freeQueue.PendingCount} to free | " +
            $"jobs {_pipeline.CompletedJobs} done / " +
            $"{_pipeline.DroppedJobs} dropped / {_pipeline.FaultedJobs} faulted | " +
            QuadSummary());

        GD.Print($"M5c: {LightSummary()}");
        GD.Print($"M5d: {FillLine().TrimEnd()}");
    }

    /// <summary>
    /// The light stage's own numbers, printed beside the streaming ones.
    /// </summary>
    /// <remarks>
    /// <b>Every counter has its denominator next to it</b>, which this project has had to learn
    /// twice. "Derives" against tracked chunks says whether the rebuild rule is thrashing;
    /// "uniform" against derives is the closed form's coverage, which was 12% in the all-surface
    /// test world and needs a real one; and relight remeshes against drawn chunks is half of
    /// M5's fragment-shader trigger. A backlog that climbs and stays up means the 2 ms slice is
    /// too small, the same way <c>PeakPending</c> reads for the free path.
    /// </remarks>
    private string LightSummary()
    {
        ChunkStreamer streamer = _pipeline!.Streamer;
        int derives = _pipeline.LightBuilds;
        int uniform = _pipeline.UniformLightBuilds;

        string uniformShare = derives > 0 ? $"{100.0 * uniform / derives:F0}%" : "n/a";

        return $"{_pipeline.LightDeriveMillis:F0} ms deriving / "
               + $"{_pipeline.LightPropagateMillis:F0} ms propagating, "
               + $"{derives} derives / {streamer.TrackedCount} tracked ({uniform} uniform, "
               + $"{uniformShare}), {_pipeline.LightCellsVisited} cells visited, "
               + $"{streamer.SkyColumnCount} sky columns, "
               + $"{_pipeline.RelightRemeshes} relight remeshes, "
               + $"{_pipeline.LightBuildBacklog} queued, "
               + $"light {(_pipeline.LightInFlight ? "in flight" : "settled")}";
    }

    private void LoadContent()
    {
        string modsPath = ProjectSettings.GlobalizePath("res://mods");
        var result = Content.Load(new DirectoryContentSource(modsPath));

        foreach (string error in result.Errors)
        {
            GD.PushError($"Content: {error}");
        }

        GD.Print($"Content: {result}");
    }

    private int MaterialId(string identifier) =>
        Content.Materials.TryGetNumericId(Identifier.Parse(identifier), out int id)
            ? id
            : MaterialIds.AirNumericId;

    public override void _ExitTree()
    {
        // Order matters: stop the pipeline submitting, then stop the workers, then release
        // the RIDs. Freeing instances while a mesh result could still arrive would hand the
        // renderer a mesh for a chunk that has just been torn down.
        _pipeline?.Dispose();
        _scheduler?.Dispose();

        _highlight?.Dispose();
        _highlight = null;

        // Budget ignored here: a stutter during teardown costs nothing and a leak would not.
        _freeQueue.DrainAll();

        foreach (VoxelRenderInstance instance in _instances.Values)
        {
            instance.Dispose();
        }

        _instances.Clear();
        _pipeline = null;
        _scheduler = null;
    }
}
