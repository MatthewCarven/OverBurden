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
    private bool _mineWhileProfiling;

    public GameContent Content { get; } = new();

    public override void _Ready()
    {
        LoadContent();

        _camera = GetNodeOrNull<FlyCam>("FlyCam");
        _material = VoxelMaterial.Create();

        int margin = ApplyCommandLineOverrides();

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
            resultBudget: _applyBudget);

        SetUpPlayer(_generator);

        _lastViewerChunk = ViewerChunk();
        _viewer = _pipeline.Streamer.AddViewer(
            _lastViewerChunk, ViewDistanceHorizontal, ViewDistanceVertical);

        SetUpMining();

        GD.Print(
            $"M2c: streaming r{ViewDistanceHorizontal}h/{ViewDistanceVertical}v " +
            $"+{_pipeline.Streamer.LoadMargin} margin on {_scheduler.ThreadCount} worker(s)");
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

        // Flying is a measurement instrument, so a profile run is not allowed to walk.
        PlayerMode mode = _profiler is null && _flySpeed <= 0f ? PlayerMode.Walk : PlayerMode.Fly;

        Vector3 scenePosition = _camera.GlobalPosition;
        VoxelPos column = VoxelPos.FromMetres(scenePosition.X, scenePosition.Y, scenePosition.Z);
        System.Numerics.Vector3 feet = PlayerSpawn.FeetOnSurface(
            generator.SurfaceHeightAt(column.X, column.Z), scenePosition.X, scenePosition.Z);

        var config = PlayerMotionConfig.Default;
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
    /// <c>--speed</c>, <c>--warmup</c>, <c>--measure</c>, <c>--cavestep</c> and
    /// <c>--applybudget</c> from the command line, after a bare <c>--</c>.
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

                case "profile":
                    profile = true;
                    break;

                // Holds the button down for the whole measured window. M3-PLAN asks for the
                // cost of mining *continuously*, not of one swing, and a person holding a
                // mouse is not a repeatable measurement.
                case "mine":
                    _mineWhileProfiling = true;
                    break;
            }
        }

        if (profile)
        {
            // Flying is the test. Standing still measures a system that has stopped doing
            // the thing being measured.
            _profiler = new StreamingProfiler { WarmupSeconds = warmup, MeasureSeconds = measure };
            if (_flySpeed <= 0f)
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

        if (!_player.TryStep(_pipeline.Streamer, _materials, ReadIntent(), (float)delta))
        {
            return;
        }

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
        }

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
            $"{_flySpeed:F0} m/s" +
            (_mining is null ? "\n" : $", {_mining.Tool.Id}\n") +
            $"{_profiler.PipelineUpdate.Line("update ms")}\n" +
            $"{_profiler.MeshFree.Line("free ms")}   (deferred by Godot — see MeshFreeQueue)\n" +
            $"{_profiler.Frame.Line("frame ms")}\n" +
            $"main      {_profiler.PipelineUpdate.Total() / Math.Max(_profiler.MeasuredSeconds, 0.001) / 10.0:F2}% " +
            $"of one core, streaming on the main thread\n" +
            $"chunks    {streamer.ViewedCount} viewed / {streamer.TrackedCount} tracked / " +
            $"{_instances.Count} drawn\n" +
            $"jobs      {_pipeline.CompletedJobs} done / {_pipeline.DroppedJobs} dropped / " +
            $"{_pipeline.FaultedJobs} faulted\n" +
            $"applying  {_pipeline.AppliedResults} applied, {_pipeline.PendingResults} still " +
            $"waiting at the end of the run\n" +
            $"freeing   {_freeQueue.TotalFreed} released, peak queue {_freeQueue.PeakPending}\n" +
            MiningLines() +
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
            $"{_pipeline.EditRemeshes / Math.Max(_profiler.MeasuredSeconds, 0.001):F1}/s\n" +
            $"clones    {_pipeline.ChunkClones / swings:F1} per swing " +
            $"({_pipeline.ChunkClones * 2.0 / swings:F1} KiB at ~2 KiB each)\n" +
            $"clipped   {_mining.ClippedSwings} swing(s) reached past the loaded world\n";
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
            GetWorld3D(), arrayMesh, new Transform3D(Basis.Identity, new Vector3(x, y, z)));
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
            $"{_pipeline.DroppedJobs} dropped / {_pipeline.FaultedJobs} faulted");
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
