using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using MinerCraft.Core.Building;
using MinerCraft.Core.Content;
using MinerCraft.Core.Inventory;
using MinerCraft.Core.Jobs;
using MinerCraft.Core.Meshing;
using MinerCraft.Core.Mining;
using MinerCraft.Core.Persistence;
using MinerCraft.Core.Physics;
using MinerCraft.Core.Streaming;
using MinerCraft.Core.Voxels;
using MinerCraft.Core.Worldgen;
using MinerCraft.Game.Diagnostics;
using MinerCraft.Game.Player;
using MinerCraft.Game.Rendering;
using MinerCraft.Persistence;

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

    private PlaceController? _place;
    private VoxelHighlight? _cellHighlight;
    private readonly VolumetricInventory _inventory = new();
    private bool _placeWhileProfiling;
    private bool _precisePlaceWhileProfiling;
    private long _startingStock;
    private BrushShape _startingBrush = BrushShape.Cube;

    private PlayerController? _player;
    private MaterialTable? _materials;
    private bool _announcedSpawn;

    /// <summary>The world file, or null for a run that saves nothing.</summary>
    /// <remarks>
    /// <b>Owned here rather than by the pipeline</b>, because it is read before the pipeline can
    /// exist: the header carries the ID table, and the table has to be seeded into the registry
    /// before a single material registers. Disposed after the pipeline, which writes through it.
    /// </remarks>
    private IWorldStore? _store;

    /// <summary>What the world file said the player was doing, or null for a new world.</summary>
    private PlayerSave? _savedPlayer;

    /// <summary>Where the camera was on the last live frame, while flying.</summary>
    /// <remarks>
    /// <para>
    /// <b>Cached every frame rather than read when the world is written, and that is a bug fix
    /// rather than a tidy-up.</b> In fly mode the camera is where the player is, so the save has
    /// to ask the camera — but the save also runs from <see cref="_ExitTree"/>, during teardown,
    /// and a played world came back with the player at <c>(0, -1.65, 0)</c> flying: the eye read
    /// as the scene origin. <b>Underground and noclipping, and self-perpetuating</b>, because the
    /// next launch restores that position and writes it back.
    /// </para>
    /// <para>
    /// The rule this is an instance of: <b>state that has to survive a quit must be captured while
    /// the frame is alive.</b> A Godot node during teardown is not a source of truth, and the
    /// walking half of this method never had the problem because it reads
    /// <c>PlayerController.FeetCentre</c>, which is plain C# state.
    /// </para>
    /// </remarks>
    private System.Numerics.Vector3? _flyingEye;

    /// <summary>The settings this world regenerates unstored terrain from. Written back on save.</summary>
    private WorldGenSettings _settings;

    private string _worldName = DefaultWorldName;
    private bool _worldNamed;
    private bool _persist = true;
    private double _autosaveSeconds = DefaultAutosaveSeconds;
    private double _sinceAutosave;

    private StreamingProfiler? _profiler;
    private float _flySpeed;
    private int? _caveLatticeStep;
    private TimeSpan? _applyBudget;
    private TimeSpan? _lightBudget;
    private bool _mineWhileProfiling;
    private bool _entangleWhileProfiling;
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

    /// <summary>The world a run opens when nothing says otherwise.</summary>
    /// <remarks>
    /// <para>
    /// <b>A directory per world under <c>user://worlds/</c>, settled here rather than defaulted.</b>
    /// <c>user://</c> is Godot's per-user data directory and is the conventional answer; the extra
    /// path segment is the part worth arguing. A second world — M6d's scratch arm, a "new game", a
    /// backup — is then a new directory beside this one rather than a migration of a file sitting
    /// at the root, and moving a world file later strands the world that was in it. WAL leaves
    /// <c>-wal</c> and <c>-shm</c> sidecars next to the database, so a directory is what they
    /// wanted anyway.
    /// </para>
    /// </remarks>
    private const string DefaultWorldName = "default";

    /// <summary>How often the dirty set is written even though nothing has unloaded.</summary>
    /// <remarks>
    /// <b>Unload is the write path and this is the backstop.</b> A player digging in one spot
    /// never unloads the chunks they are digging, so without this the only writes are at quit and
    /// a crash costs the session. Thirty seconds against <see cref="ChunkPipeline.SaveAll"/>
    /// measured at 13 ms for the whole 1,521-chunk fill, where a real dirty set is tens of chunks.
    /// </remarks>
    private const double DefaultAutosaveSeconds = 30.0;

    /// <summary>The seed a brand new world is created with.</summary>
    /// <remarks>
    /// Only ever used once per world. After that the world's own header decides, because an
    /// unedited chunk is regenerated rather than stored — see <see cref="OpenWorld"/>.
    /// </remarks>
    private const ulong NewWorldSeed = 0x4D494E455243UL;

    public GameContent Content { get; } = new();

    public override void _Ready()
    {
        _camera = GetNodeOrNull<FlyCam>("FlyCam");

        int margin = ApplyCommandLineOverrides();

        // After the overrides, not before: --skybrightness is a shader uniform set at build.
        _material = VoxelMaterial.Create(_skyBrightness);

        // Before the generator and before content, because an existing world decides both. Its
        // header carries the settings the unstored terrain has to keep regenerating from, and its
        // ID table has to reach the registry before a single material registers.
        if (!OpenWorld(out WorldGenSettings settings))
        {
            // Refused rather than played without saving. Carrying on with persistence quietly off
            // is an hour of digging lost to a line in the log, which is the failure this whole
            // milestone is about.
            GetTree().Quit(1);
            return;
        }

        _settings = settings;
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
            light: _lightInMergeKey,
            store: _store);

        // Whatever the world remembered the player carrying. Before the first swing, so that a
        // reopened world can place from what it was already holding.
        _inventory.Reset(_restoredInventory.Select(entry => (entry.MaterialId, entry.Voxels)));

        // Before any chunk can be written, which is what replaces a world-spanning transaction:
        // numeric IDs are only allocated during content load, so the table on disk is already the
        // one every blob written after this points at. Rewritten on every open rather than only
        // for a new world, because installing a mod extends the table with entries the stored
        // header does not have — and a blob referring to an ID the header has never heard of is
        // exactly the desynchronisation the single-file layout exists to prevent.
        _pipeline.WriteWorldHeader(CurrentHeader());

        SetUpPlayer(_generator);

        _lastViewerChunk = ViewerChunk();
        _viewer = _pipeline.Streamer.AddViewer(
            _lastViewerChunk, ViewDistanceHorizontal, ViewDistanceVertical);

        SetUpMining();
        SetUpPlacement();

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

        // A world you have been in puts you back where you were — but never during a profile,
        // where the spawn is a measurement input and a run that started somewhere else is not
        // comparable with the one before it.
        bool restored = _profiler is null && _savedPlayer is not null;
        if (restored && _savedPlayer is PlayerSave saved)
        {
            feet = saved.FeetMetres;
            mode = saved.Flying ? PlayerMode.Fly : PlayerMode.Walk;
        }

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
        else if (restored)
        {
            // The camera is where the player is in fly mode, so a restored flying player has to
            // put it back rather than being dropped above the terrain like a new one.
            _camera.GlobalPosition = ToGodot(_player.EyePosition);
        }
        else
        {
            PlaceCameraAboveTerrain(generator);
        }

        GD.Print(
            $"M4c: {mode.ToString().ToLowerInvariant()} mode, body {config.WidthMetres:F2} x " +
            $"{config.HeightMetres:F2} m, eye {config.EyeHeightMetres:F2} m, " +
            $"spawn feet {feet.Y:F2} m{(restored ? ", restored from the world file" : "")}");
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

                // The entangled miner from the command line, because F12 cannot be pressed
                // headlessly and a debug bind nobody has run is a debug bind that does not work.
                // The sixth seam of this shape. It deliberately does NOT imply --mine: on its
                // own it only proves the cast reaches, which is the half that has no other
                // witness.
                case "entangle":
                    _entangleWhileProfiling = true;
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

                // Which world under user://worlds/ this run opens. Naming one is also what puts
                // persistence back on for a profile run, which is how M6d measures the load path
                // without a measurement digging up the world somebody plays.
                case "world":
                    if (IsUsableWorldName(value))
                    {
                        _worldName = value;
                        _worldNamed = true;
                    }
                    else
                    {
                        // Said out loud rather than fallen back from quietly: opening the default
                        // world when a scratch one was asked for is how a measurement run digs up
                        // the world somebody plays.
                        GD.PushError($"--world='{value}' is not a usable directory name; ignored.");
                    }

                    break;

                // No world file at all: the pipeline takes a null store, every chunk comes from
                // the seed, and nothing is written. The world M2d and M5d measured.
                case "nosave":
                    _persist = false;
                    break;

                case "autosave" when double.TryParse(value, out double autosave):
                    _autosaveSeconds = autosave;
                    break;

                // Holds the other button down for the whole measured window, the counterpart to
                // --mine and for the same reason: a person holding a mouse is not a repeatable
                // measurement.
                case "place":
                    _placeWhileProfiling = true;
                    break;

                // The precise arm of --place, and the only way to exercise the modifier without a
                // keyboard. **The wire is what this is for**: every layer of precision placement
                // has a test and the join between the held key and Place.Voxel has none, which is
                // exactly the shape of hole M5c-2 found by replacing a gather with null and
                // watching 556 tests stay green.
                case "preciseplace":
                    _placeWhileProfiling = true;
                    _precisePlaceWhileProfiling = true;
                    break;

                // Seeds the inventory, because --place has nothing to place otherwise and mining
                // first would measure the carve as well as the brush.
                case "stock" when long.TryParse(value, out long stock):
                    _startingStock = stock;
                    break;

                case "brush" when Enum.TryParse(value, ignoreCase: true, out BrushShape brush):
                    _startingBrush = brush;
                    break;
            }
        }

        // A run whose purpose is a measurement or a screenshot prop does not open the played
        // world. Two reasons and they are separate: --mine would carve it up, and a run that
        // *loads* what an earlier one saved is not comparable with M2d's figures — the whole
        // streaming shape changes when 1,521 generates become 1,521 serial reads. --world= is the
        // opt back in, deliberately explicit.
        if ((profile || _debugLamp) && !_worldNamed)
        {
            _persist = false;
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
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }

        if (key.Keycode == Key.Q)
        {
            ToggleEntangledMiner();
            return;
        }

        if (key.Keycode == Key.B)
        {
            CycleBrush();
            return;
        }

        if (key.Keycode == Key.V)
        {
            CycleMaterial();
            return;
        }

        if (_player is null || _camera is null || key.Keycode != Key.F)
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

    /// <summary>
    /// Q turns the entangled miner on and off.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A debug bind for excavating a test volume, not a feature.</b> M5d-2 is the played half
    /// of lighting and its questions are all about what a <em>large</em> excavation looks like —
    /// whether a mine reads as a mine, whether a 1 m light cell is too coarse for a tunnel
    /// narrower than one cell, whether rock beside a lit tunnel glows. Digging that by hand at
    /// 5 m of reach is the reason those questions have not been answered yet.
    /// </para>
    /// <para>
    /// On a key-down edge rather than polled, the same as F and L, because a toggle read every
    /// frame flips sixty times a second. <b>Q rather than a function key</b>: it is free — WASD
    /// leaves the whole of QE alone — it is mnemonic, and it is under the left hand already,
    /// which matters for something toggled mid-dig rather than once at startup. F12 is also the
    /// Steam overlay's screenshot key, which is a bad neighbour for a bind used while playing.
    /// </para>
    /// <para>
    /// <b>Refused during a profile run</b>, for exactly the reason walk mode is: <c>--mine</c>
    /// is pinned to the un-entangled sphere so that M3d's baseline stays comparable, and a run
    /// that quietly switched to arbitrary-range swings would report a different measurement
    /// under the same label. See <see cref="MiningController.EntangledSwings"/>.
    /// </para>
    /// </remarks>
    /// <summary>B cycles the brush, V cycles the material. Both print, because there is no HUD.</summary>
    /// <remarks>
    /// <b>B and V rather than more number keys</b>, which are the tool tiers and stay the tool
    /// tiers: the tier decides what a swing can take and the brush decides what a placement fills,
    /// and putting both on one row would make them look like one selection.
    /// </remarks>
    private void CycleBrush()
    {
        if (_place is null) return;

        GD.Print($"M6c: brush is now {_place.NextBrush().ToString().ToLowerInvariant()}");
    }

    private void CycleMaterial()
    {
        if (_place is null) return;

        int materialId = _place.NextMaterial();

        GD.Print(materialId < 0
            ? "M6c: carrying nothing — mine something first"
            : $"M6c: placing {Content.Materials.GetIdentifier(materialId)} "
              + $"({_inventory.Litres(materialId):F0} L held)");
    }

    private void ToggleEntangledMiner()
    {
        if (_mining is null)
        {
            return;
        }

        if (_profiler is not null)
        {
            GD.Print("M5d: the entangled miner is unavailable during a profile run.");
            return;
        }

        _mining.Entangled = !_mining.Entangled;

        GD.Print(_mining.Entangled
            ? $"M5d: entangled miner ON — reach unlimited (bounded by the loaded world), "
              + $"{1.0f / MiningController.EntangledCooldownSeconds:F0} swings/s, "
              + $"still {_mining.Tool.Id.Path}'s bite and hardness gate"
            : "M5d: entangled miner OFF — reach back to "
              + $"{_player?.ReachMetres ?? PlayerController.DefaultReachMetres:F1} m");
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

        // Before anything can quit, and only while flying: see _flyingEye.
        if (_camera is not null && _player is { Mode: PlayerMode.Fly })
        {
            Vector3 eye = _camera.GlobalPosition;
            _flyingEye = new System.Numerics.Vector3(eye.X, eye.Y, eye.Z);
        }

        UpdateMining(delta);
        UpdatePlacement(delta);

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

        Autosave(delta);
        Report(delta);
    }

    /// <summary>Writes the dirty set on a timer, so a crash costs a minute rather than a session.</summary>
    /// <remarks>
    /// <para>
    /// <b>Synchronous, on the sim thread, deliberately.</b> <see cref="ChunkPipeline.SaveAll"/>
    /// waits for the serial lane to come out of the store first and then writes inline; the
    /// asynchronous path exists and is what an unload uses, but a timer that hands work to a queue
    /// leaves the crash window it was built to close still open.
    /// </para>
    /// <para>
    /// Nothing here is conditional on having edited: <c>SaveAll</c> with a clean world walks two
    /// lists and writes nothing, which is what
    /// <c>SaveAllIsIdempotentAndCheapWhenNothingIsDirty</c> pins.
    /// </para>
    /// </remarks>
    private void Autosave(double delta)
    {
        if (_pipeline is null || _store is null || _autosaveSeconds <= 0.0)
        {
            return;
        }

        _sinceAutosave += delta;
        if (_sinceAutosave < _autosaveSeconds)
        {
            return;
        }

        _sinceAutosave = 0.0;

        long before = Stopwatch.GetTimestamp();

        // The header first, because it carries the inventory and the player — the session state
        // that no chunk holds. Writing only the chunks would save the tunnel and forget what the
        // player was carrying when they dug it.
        _pipeline.WriteWorldHeader(CurrentHeader());

        (int chunks, int columns) = _pipeline.SaveAll();

        // Only when it did something. A line every thirty seconds saying nothing happened is how
        // a log stops being read.
        if (chunks > 0 || columns > 0)
        {
            GD.Print(
                $"M6: autosaved {chunks} chunk(s) and {columns} column(s) in "
                + $"{Milliseconds(before, Stopwatch.GetTimestamp()):F1} ms");
        }
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
            Inventory = _inventory,
            ReachMetres = _player?.ReachMetres ?? PlayerController.DefaultReachMetres,
        };

        _highlight = new VoxelHighlight(GetWorld3D());

        var names = new List<string>();
        for (int i = 0; i < _tiers.Count; i++)
        {
            names.Add($"{i + 1}:{_tiers[i].Id.Path}");
        }

        _mining.Entangled = _entangleWhileProfiling;

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
    /// <summary>Builds the placement controller and the outline that shows where a brush lands.</summary>
    private void SetUpPlacement()
    {
        if (_pipeline is null || _materials is null || _player is null)
        {
            return;
        }

        _place = new PlaceController(_pipeline, _materials, _inventory)
        {
            Brush = _startingBrush,
            // The player's reach, the same number mining uses. A brush is not a tool and does not
            // get to reach further than an arm — M3d's decision, restated at M4c.
            ReachMetres = _player.ReachMetres,
        };

        // A second outline, a metre across and in a different colour, because the brush lands on
        // the lattice rather than on the voxel under the crosshair. Without it the snap reads as
        // the game ignoring the aim, which is precisely what M6d has to judge.
        _cellHighlight = new VoxelHighlight(
            GetWorld3D(), BrushCell.SizeMetres, new Color(0.95f, 0.85f, 0.25f, 0.55f));

        if (_startingStock > 0)
        {
            // A profile seam, not a gift: --place has to have something to place, and mining
            // first would measure the carve as well as the brush.
            foreach ((int numericId, _, _) in Content.Materials.Entries())
            {
                if (numericId != MaterialIds.AirNumericId)
                {
                    _inventory.Add(numericId, _startingStock);
                }
            }
        }

        GD.Print(
            $"M6c: placing with the {_place.Brush.ToString().ToLowerInvariant()} brush — "
            + "right mouse places, B cycles the brush, V cycles the material");
    }

    /// <summary>Casts for the brush, places if the button is held, and moves the cell outline.</summary>
    private void UpdatePlacement(double delta)
    {
        if (_place is null || _camera is null || _player is null)
        {
            return;
        }

        Vector3 origin = _camera.GlobalPosition;
        Vector3 forward = -_camera.GlobalTransform.Basis.Z;
        bool placing = _camera.MouseCaptured && Input.IsMouseButtonPressed(MouseButton.Right);

        // The same modifier as mining, deliberately: Alt+left takes one voxel and Alt+right
        // places one, so "precision" is a thing the player learns once rather than per verb.
        bool precise = placing && Input.IsKeyPressed(Key.Alt);

        if (_placeWhileProfiling)
        {
            // Along the look direction rather than straight down, which is what --mine does. A
            // brush aimed at the ground under the camera lands in the cell the player is standing
            // in, where the body refusal eats most of it — so the run would measure the refusal.
            placing = true;

            // Brushes unless the precise arm was asked for, and stated rather than left to fall
            // out of "no key is held" — the same argument --mine makes. A voxel is 1/64th of a
            // cube, so a profile that quietly went precise would report a number 64 times cheaper
            // under the same label, which is why the report names the arm as well.
            precise = _precisePlaceWhileProfiling;
        }

        // Null while flying: the body is inert in fly mode and sits wherever the last F left it,
        // so passing it would refuse voxels around a position the player is no longer at.
        MinerCraft.Core.Physics.Aabb? body =
            _player.Mode == PlayerMode.Walk && _player.Spawned ? _player.State.Box : null;

        long before = Stopwatch.GetTimestamp();

        bool placed = _place.Update(
            delta,
            new System.Numerics.Vector3(origin.X, origin.Y, origin.Z),
            new System.Numerics.Vector3(forward.X, forward.Y, forward.Z),
            body,
            placing,
            precise);

        if (placed && _profiler is { Warm: true })
        {
            _profiler.Place.Add(Milliseconds(before, Stopwatch.GetTimestamp()));
        }

        if (placed && _place.RanOutThisFrame && !_announcedEmpty)
        {
            // Once, until something is placed successfully again. There is no HUD until M6d, so
            // the log is the only feedback there is — and a line every frame is not feedback.
            _announcedEmpty = true;
            GD.Print("M6c: out of material — mine some more, or press V for something else");
        }
        else if (placed && !_place.RanOutThisFrame)
        {
            _announcedEmpty = false;
        }

        if (_cellHighlight is null) return;

        if (!_place.HasTarget)
        {
            _cellHighlight.Hide();
            return;
        }

        // The outline follows the modifier, or the player cannot see what precision would do
        // until after they have done it. One voxel while Alt is held, one metre otherwise.
        bool preciseNow = precise || (_camera.MouseCaptured && Input.IsKeyPressed(Key.Alt));

        (float x, float y, float z) = preciseNow
            ? _place.TargetVoxel.MinCornerMetres
            : _place.TargetCell.OriginVoxel.MinCornerMetres;

        _cellHighlight.ShowAt(
            new Vector3(x, y, z),
            preciseNow ? VoxelConstants.VoxelSize : BrushCell.SizeMetres);
    }

    private bool _announcedEmpty;

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

        // Alt takes exactly the highlighted voxel instead of the tool's sphere, and holds the
        // same meaning for the other button: **one modifier means precision across both verbs.**
        // A held modifier rather than a toggle, because it is the exception and not the mode.
        //
        // **Moved off Ctrl at M6d-1, closing a note recorded at M3d.** Ctrl is free in walk mode
        // but is also FlyCam's descend, so precision mining while flying sank the builder about
        // 1.8 m between chips. Alt is free in walk and is *crawl* in fly, so "slow down to do
        // fine work" pairs with the modifier rather than fighting it — which is the argument
        // that named Alt as the better key a milestone before anything needed it.
        bool precise = mining && Input.IsKeyPressed(Key.Alt);

        if (_mineWhileProfiling)
        {
            // Straight down from a camera four metres above the surface. Aiming along the
            // flight path would spend most of the run pointing at sky, and a measurement of
            // mining that mostly misses is not one.
            //
            // ENTANGLED IS THE EXCEPTION, and it is the whole reason the flag exists: with the
            // reach bounded by the loaded world rather than by an arm, aiming along the flight
            // path finally hits the hillside ahead instead of nothing. Pointing at sky still
            // costs no swing — the cast returns Missed or Blocked and the button does nothing —
            // so the run measures fewer swings that each reach much further, which is exactly
            // the claim being checked.
            forward = _mining.Entangled ? forward : Vector3.Down;
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
            PlacementLines() +
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
            $"clipped   {_mining.ClippedSwings} swing(s) reached past the loaded world\n" +
            (_mining.EntangledSwings > 0
                ? $"entangled {_mining.EntangledSwings} swing(s) — NOT comparable with M3d's baseline\n"
                  + $"reach     {_mining.TotalReachMetres / swings:F1} m mean, {_mining.MaxReachMetres:F1} m furthest\n"
                : string.Empty);
    }

    /// <summary>What a brush costs, beside what mining costs.</summary>
    /// <remarks>
    /// <b>Its own series rather than folded into <c>swing ms</c></b>, because the two shapes are
    /// not the same size: <c>hand_pick</c>'s sphere is about seventeen voxels and a cube brush is
    /// sixty-four, so one line would report whichever the run happened to do more of. Occupied
    /// against placed is the number that says whether the lattice is landing where the aim
    /// suggests — a placement that is nearly all occupied was aimed into a cell already full.
    /// </remarks>
    private string PlacementLines()
    {
        if (_place is null || _place.Placements == 0)
        {
            return string.Empty;
        }

        double placements = _place.Placements;

        // Named for the shape actually used rather than for the brush selected, because a run
        // that went precise did 1/64th the work under a label that says "cube" — the same trap
        // M3d records for chips sharing the swing line.
        string shape = _place.PrecisePlacements == _place.Placements
            ? "one voxel at a time"
            : $"the {_place.Brush.ToString().ToLowerInvariant()} brush";

        return
            $"{_profiler!.Place.Line("place ms")}   (raycast + brush, sim thread)\n" +
            $"building  {_place.Placements} placement(s) with {shape}, " +
            $"{_place.VoxelsPlaced / placements:F0} placed / " +
            $"{_place.VoxelsOccupied / placements:F0} occupied / " +
            $"{_place.VoxelsInsidePlayer / placements:F0} inside the player / " +
            $"{_place.VoxelsOutOfStock / placements:F0} unaffordable per placement\n" +
            $"carrying  {_inventory.TotalLitres:F0} L across {_inventory.MaterialCount} material(s)\n" +
            (_place.PrecisePlacements > 0
                ? $"precise   {_place.PrecisePlacements} of {_place.Placements} placement(s) were "
                  + "one voxel — NOT comparable with a brush baseline\n"
                : string.Empty) +
            $"clipped   {_place.ClippedPlacements} placement(s) reached past the loaded world\n";
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

        if (_store is not null)
        {
            GD.Print($"M6: {StoreSummary()}");
        }

        GD.Print($"M6c: {InventorySummary()}");
    }

    /// <summary>What the store has done this session, each counter beside its denominator.</summary>
    /// <remarks>
    /// <b>"Loaded" against tracked is the number M6b's crossover worry turns on</b>, and it is the
    /// only one that says whether this run took the load path at all — a world with nothing saved
    /// generates throughout and reports zero, which is a pass rather than a silence. Pending and
    /// failed are the pair that says a store is in trouble: pending that climbs and stays up is a
    /// lane that is not keeping up, and failed is a disk that is refusing.
    /// </remarks>
    private string StoreSummary()
    {
        ChunkStreamer streamer = _pipeline!.Streamer;

        return $"{_pipeline.ChunksLoaded} loaded / {streamer.TrackedCount} tracked, "
               + $"{_pipeline.ChunksSaved} chunks + {_pipeline.ColumnsSaved} columns saved "
               + $"({_pipeline.BytesSaved / 1024.0:F0} KiB), "
               + $"{_pipeline.SavedChunkCount} chunks in the world, "
               + $"{_pipeline.ColumnsRestored} columns restored, "
               + $"{_pipeline.PendingSaves} pending, {_pipeline.FailedSaves} failed";
    }

    /// <summary>What has been dug up and what has been built with it.</summary>
    /// <remarks>
    /// <b>Litres carried beside voxels placed</b>, because the pair is the loop this milestone is
    /// for: mining fills the first and building empties it. A session where one moves and the
    /// other does not is a wire that is not connected.
    /// </remarks>
    private string InventorySummary()
    {
        string held = _inventory.MaterialCount == 0
            ? "carrying nothing"
            : $"{_inventory.TotalLitres:F0} L across {_inventory.MaterialCount} material(s)";

        if (_place is null) return held;

        return $"{held} | {_place.Placements} placement(s), {_place.VoxelsPlaced} voxel(s) placed, "
               + $"{_place.VoxelsOccupied} occupied, {_place.VoxelsInsidePlayer} inside the player, "
               + $"{_place.VoxelsOutOfStock} unaffordable";
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

    /// <summary>
    /// Opens the world file, restores what it remembers, and loads content against it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The one ordering trap in M6b-3.</b> Open the store, read the header, load content seeded
    /// with its ID table, and only then let anything register a material.
    /// <see cref="Registry{T}.SeedIdTable"/> throws if anything registered first, which is the
    /// right failure and a loud one — the alternative is numeric IDs handed out against a
    /// different table from the one every stored blob was written with, and stone-coloured air.
    /// </para>
    /// <para>
    /// <b>An existing world's settings replace the defaults rather than sitting beside them.</b>
    /// An unedited chunk is regenerated rather than stored, and it regenerates from all thirteen
    /// fields — so a world reopened by a build whose defaults have moved comes back as different
    /// terrain wrapped around the saved chunks. That is the whole reason the header carries the
    /// record rather than the seed.
    /// </para>
    /// </remarks>
    /// <returns>False if the world could not be opened, in which case the run is refused.</returns>
    private bool OpenWorld(out WorldGenSettings settings)
    {
        settings = NewWorldSettings();

        if (!_persist)
        {
            LoadContent(null);
            GD.Print("M6: no world file — nothing this run does will be saved");
            return true;
        }

        string path = WorldPath();

        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);

            var store = new SqliteWorldStore(path);
            _store = store;

            byte[]? header = store.ReadWorldHeader();

            // Null is how "new world" is detected, and it is the only place that question is
            // asked. A world file that exists but holds no header is a world that was created and
            // never written to, which is the same thing.
            if (header is null)
            {
                LoadContent(null);
                GD.Print($"M6: new world at {path}, seed 0x{settings.Seed:X}");
                return true;
            }

            WorldHeader world = WorldHeaderBlob.Read(header);
            settings = world.Settings;
            _restoredInventory = world.Inventory;
            _savedPlayer = world.Player;

            if (_caveLatticeStep is not null)
            {
                // Ignored rather than applied, because the header is what the stored chunks were
                // generated against. --cavestep is an A/B seam and belongs to a run with its own
                // world or none at all.
                GD.PushWarning(
                    "--cavestep is ignored for an existing world: its header decides how the "
                    + "unstored terrain regenerates. Use --world=<name> for a scratch world.");
            }

            LoadContent(world.MaterialIdTable);

            GD.Print(
                $"M6: opened {path} — seed 0x{settings.Seed:X}, "
                + $"{world.MaterialIdTable.Count} material id(s) restored, "
                + $"{world.Inventory.Count} material(s) carried, "
                + (world.Player is null ? "no saved player" : "player restored"));

            return true;
        }
        catch (Exception error)
        {
            // Loud, and fatal. A world that cannot be opened is not a world that should be played
            // beside: generating over the top of it and saving nothing would look like a fresh
            // start and read, on the next successful open, as a world that lost a session.
            GD.PushError($"M6: cannot open the world at {path} — {error.Message}");

            _store?.Dispose();
            _store = null;

            return false;
        }
    }

    /// <summary>What the world file said was being carried. Applied once the registry is loaded.</summary>
    private IReadOnlyList<InventoryEntry> _restoredInventory = [];

    /// <summary>
    /// The world as it stands right now: its settings, its ID table, and the session.
    /// </summary>
    /// <remarks>
    /// <b>Rebuilt on every save rather than mutated</b>, because the ID table has to go with the
    /// inventory that is keyed by it — writing the two at different moments is the one arrangement
    /// that can desynchronise, which is the whole reason they share a blob.
    /// </remarks>
    private WorldHeader CurrentHeader() => new()
    {
        Settings = _settings,
        MaterialIdTable = Content.Materials.ExportIdTable(),
        Inventory = [.. _inventory.Entries().Select(e => new InventoryEntry(e.MaterialId, e.Voxels))],
        Player = SavedPlayer(),
    };

    /// <summary>Where the player is, for the world file — or null if there is no body yet.</summary>
    /// <remarks>
    /// <b>The camera decides while flying and the body decides while walking</b>, and they are not
    /// interchangeable: in fly mode the body is inert and sits wherever the last F left it, so
    /// saving it would put the player back somewhere they flew away from ten minutes ago.
    /// </remarks>
    private PlayerSave? SavedPlayer()
    {
        // Nothing live to ask yet — at open, before the body exists — so keep what the world
        // already remembered. Returning null here would clear the saved position on every launch
        // and leave it cleared until the first autosave, which is a crash away from losing it.
        if (_player is not { Spawned: true }) return _savedPlayer;

        bool flying = _player.Mode == PlayerMode.Fly;

        if (!flying)
        {
            return new PlayerSave(_player.FeetCentre, flying);
        }

        // The cached eye, never the live node. If no frame has run in fly mode there is nothing
        // honest to say, so the previous answer stands rather than an invented one.
        if (_flyingEye is not System.Numerics.Vector3 eye) return _savedPlayer;

        return new PlayerSave(
            eye - new System.Numerics.Vector3(0.0f, _player.Config.EyeHeightMetres, 0.0f),
            Flying: true);
    }

    /// <summary>Where this run's world file lives. See <see cref="DefaultWorldName"/>.</summary>
    private string WorldPath() =>
        ProjectSettings.GlobalizePath($"user://worlds/{_worldName}/world.db");

    /// <summary>Whether <paramref name="name"/> can be a directory under <c>user://worlds/</c>.</summary>
    /// <remarks>
    /// <b>Not a security boundary — a single-player game's own command line is not an attacker.</b>
    /// It is there because the failure it prevents is silent: a name carrying a separator puts the
    /// world somewhere other than where the next run will look for it, and the symptom is a world
    /// that opens empty rather than an error.
    /// </remarks>
    private static bool IsUsableWorldName(string name) =>
        name.Length > 0
        && name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) < 0
        && name is not "." and not "..";

    /// <summary>The settings a world is created with, before it has a header of its own.</summary>
    private WorldGenSettings NewWorldSettings()
    {
        var settings = new WorldGenSettings { Seed = NewWorldSeed };

        return _caveLatticeStep is int step
            ? settings with { CaveLatticeStep = step }
            : settings;
    }

    private void LoadContent(IReadOnlyList<Identifier>? savedMaterialIds)
    {
        string modsPath = ProjectSettings.GlobalizePath("res://mods");
        var result = Content.Load(new DirectoryContentSource(modsPath), savedMaterialIds);

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
        //
        // The pipeline first is also what saves the world: Dispose waits for the serial lane to
        // come out of the store and then writes everything dirty, synchronously. Disposing the
        // scheduler first would drop whatever was queued on that lane, and JobResult's contract
        // says a dropped job reports Dropped rather than running — which for a save is a deleted
        // tunnel. This is the single most likely way to ship a data-loss bug in M6.
        // Before the dispose, because Dispose writes the chunks and this writes the session. Both
        // are the same quit and losing either half is the same bug.
        if (_store is not null && _pipeline is not null)
        {
            try
            {
                _pipeline.WriteWorldHeader(CurrentHeader());
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                GD.PushError($"M6: could not write the world header on quit — {error.Message}");
            }
        }

        _pipeline?.Dispose();
        _scheduler?.Dispose();

        // After the dispose rather than before it, so the figures are what was actually written
        // rather than what was about to be. Printed at all because "did the quit path run?" is the
        // question this milestone most needs answered from a log a day later.
        if (_store is not null && _pipeline is not null)
        {
            GD.Print($"M6: world written on quit — {StoreSummary()}");

            // Printed because a wrong one is invisible until the next launch puts the player
            // somewhere strange, and the log is then the only record of what was written.
            GD.Print(SavedPlayer() is PlayerSave saved
                ? $"M6c: saved the player at {saved.FeetMetres}, "
                  + (saved.Flying ? "flying" : "walking")
                : "M6c: no player position saved");
        }

        _highlight?.Dispose();
        _highlight = null;

        // The cell outline is a second RenderingServer instance and leaks exactly as loudly as
        // the first would: Godot names the RID at exit and says nothing about which one.
        _cellHighlight?.Dispose();
        _cellHighlight = null;

        // Budget ignored here: a stutter during teardown costs nothing and a leak would not.
        _freeQueue.DrainAll();

        foreach (VoxelRenderInstance instance in _instances.Values)
        {
            instance.Dispose();
        }

        _instances.Clear();
        _pipeline = null;
        _scheduler = null;

        // Last, and after the pipeline, which writes through it.
        _store?.Dispose();
        _store = null;
    }
}
