using System;
using System.Collections.Generic;
using Godot;
using MinerCraft.Core.Content;
using MinerCraft.Core.Jobs;
using MinerCraft.Core.Meshing;
using MinerCraft.Core.Streaming;
using MinerCraft.Core.Voxels;
using MinerCraft.Core.Worldgen;
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

    private JobScheduler? _scheduler;
    private ChunkPipeline? _pipeline;
    private ViewerId _viewer;
    private FlyCam? _camera;
    private ShaderMaterial? _material;
    private ChunkPos _lastViewerChunk;
    private double _sinceReport;

    public GameContent Content { get; } = new();

    public override void _Ready()
    {
        LoadContent();

        _camera = GetNodeOrNull<FlyCam>("FlyCam");
        _material = VoxelMaterial.Create();

        var settings = new WorldGenSettings { Seed = 0x4D494E455243UL };
        var generator = new WorldGenerator(settings, BuildStrata());

        var viewers = new ViewerSet();
        _scheduler = new JobScheduler(viewers);
        _pipeline = new ChunkPipeline(
            generator,
            MaterialTable.From(Content.Materials),
            _scheduler,
            viewers,
            onMeshReady: OnMeshReady,
            onMeshDiscarded: OnMeshDiscarded,
            onJobFaulted: (position, error) => GD.PushError($"Chunk job failed at {position}: {error}"));

        PlaceCameraAboveTerrain(generator);

        _lastViewerChunk = ViewerChunk();
        _viewer = _pipeline.Streamer.AddViewer(
            _lastViewerChunk, ViewDistanceHorizontal, ViewDistanceVertical);

        GD.Print(
            $"M2c: streaming r{ViewDistanceHorizontal}h/{ViewDistanceVertical}v " +
            $"+{_pipeline.Streamer.LoadMargin} margin on {_scheduler.ThreadCount} worker(s)");
    }

    public override void _Process(double delta)
    {
        if (_pipeline is null)
        {
            return;
        }

        // The viewer only moves in whole chunks. Comparing first means a camera drifting
        // inside one chunk never touches the streaming policy at all.
        ChunkPos current = ViewerChunk();
        if (current != _lastViewerChunk)
        {
            _lastViewerChunk = current;
            _pipeline.Streamer.MoveViewer(_viewer, current);
        }

        _pipeline.Update(MaxSubmissionsPerFrame);
        Report(delta);
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
    /// <b>Freed immediately, and M2d is where that stops being good enough.</b> Godot defers
    /// the actual deallocation, so freeing several hundred RIDs in one frame stutters a frame
    /// or two later and gets blamed on meshing. The fix is a "free N per frame until drained"
    /// budget rather than a time slice, because the cost is not spent inside this call and
    /// cannot be measured from here. See docs/M2-REFERENCE-NOTES.md.
    /// </remarks>
    private void OnMeshDiscarded(ChunkPos position)
    {
        if (_instances.Remove(position, out VoxelRenderInstance? instance))
        {
            instance.Dispose();
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
            $"{_pipeline.WorkListCount} queued | jobs {_pipeline.CompletedJobs} done / " +
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

        foreach (VoxelRenderInstance instance in _instances.Values)
        {
            instance.Dispose();
        }

        _instances.Clear();
        _pipeline = null;
        _scheduler = null;
    }
}
