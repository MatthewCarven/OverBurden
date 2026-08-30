using MinerCraft.Core.Content;
using MinerCraft.Core.Jobs;
using MinerCraft.Core.Meshing;
using MinerCraft.Core.Voxels;
using MinerCraft.Core.Worldgen;

namespace MinerCraft.Core.Streaming;

/// <summary>
/// Runs stage 1 of worldgen for one chunk on a worker thread.
/// </summary>
/// <remarks>
/// Generation is a pure function of seed and position, so this job reads nothing shared and
/// can run in any order on any thread. That is the property M2b was built for, and it is
/// what makes this job as simple as it looks.
/// </remarks>
public sealed class GenerateChunkJob : IJob
{
    private readonly WorldGenerator _generator;

    public GenerateChunkJob(WorldGenerator generator, ChunkPos position)
    {
        _generator = generator;
        Position = position;
    }

    public JobKind Kind => JobKind.Generate;

    public ChunkPos Position { get; }

    /// <summary>The generated voxels. Read by the sim thread once the result arrives.</summary>
    public VoxelChunk? Result { get; private set; }

    public void Run(CancellationToken cancellation)
    {
        // Generation of a single chunk is a handful of milliseconds and has no interior
        // checkpoint worth splitting it at, so this is checked once rather than polled.
        cancellation.ThrowIfCancellationRequested();
        Result = _generator.GenerateTerrain(Position);
    }
}

/// <summary>
/// Greedy-meshes one chunk on a worker thread, from a snapshot of it and its 26 neighbours.
/// </summary>
/// <remarks>
/// <para>
/// <b>The padded copy is built here, not by whoever queued the job.</b> The snapshot is 27
/// references captured on the sim thread — cheap — and the 34³ copy happens on the worker,
/// which keeps it off the sim thread and skips it entirely for a job cancelled before it
/// runs. See <see cref="PaddedChunk"/> for why holding the references is sufficient, and
/// for the M3 revisit trigger when a concurrent writer first appears.
/// </para>
/// <para>
/// A null neighbour reads as air, which is correct for the only case that produces one: a
/// chunk at the edge of the loaded region. Such a chunk fails the stage barrier and never
/// reaches meshing, so in practice the fallback is unreachable — it exists so that a bug in
/// the barrier shows up as a visible wall rather than as a crash on a worker thread.
/// </para>
/// </remarks>
public sealed class MeshChunkJob : IJob
{
    private readonly VoxelChunk?[] _neighbourhood;
    private readonly MaterialTable _materials;
    private readonly bool _ambientOcclusion;

    /// <param name="urgent">
    /// A remesh the player is waiting on, rather than one the streamer asked for. Raises
    /// band3 so it outranks every distance-ordered job in the queue — see
    /// <see cref="IJob.Urgency"/>.
    /// </param>
    /// <param name="ambientOcclusion">
    /// Off restores the constant-brightness merge key, for the A/B that says what per-vertex
    /// AO costs in quads. See <see cref="GreedyMesher"/>.
    /// </param>
    public MeshChunkJob(
        ChunkPos position,
        VoxelChunk?[] neighbourhood,
        MaterialTable materials,
        bool urgent = false,
        bool ambientOcclusion = true)
    {
        if (neighbourhood.Length != PaddedChunk.NeighbourhoodSize)
        {
            throw new ArgumentException(
                $"Expected {PaddedChunk.NeighbourhoodSize} chunks, got {neighbourhood.Length}.",
                nameof(neighbourhood));
        }

        Position = position;
        _neighbourhood = neighbourhood;
        _materials = materials;
        _ambientOcclusion = ambientOcclusion;
        Urgency = urgent ? EditUrgency : (byte)0;
    }

    /// <summary>Band3 for a remesh caused by an edit.</summary>
    /// <remarks>
    /// Not <c>byte.MaxValue</c>: later work — a chunk the player is standing inside, say —
    /// will want to outrank even this, and leaving headroom above is free now and awkward to
    /// find later.
    /// </remarks>
    public const byte EditUrgency = 128;

    public JobKind Kind => JobKind.Mesh;

    public ChunkPos Position { get; }

    public byte Urgency { get; }

    /// <summary>The built mesh. Read by the sim thread once the result arrives.</summary>
    public ChunkMesh? Result { get; private set; }

    public void Run(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();

        var padded = new PaddedChunk();
        padded.CopyFrom(_neighbourhood, MaterialIds.AirNumericId);

        // Between the gather and the sweep is the one place a long mesh job can bail out
        // without having wasted the expensive half.
        cancellation.ThrowIfCancellationRequested();

        var mesh = new ChunkMesh();
        new GreedyMesher(_ambientOcclusion).Build(padded, _materials, mesh);
        Result = mesh;
    }
}
