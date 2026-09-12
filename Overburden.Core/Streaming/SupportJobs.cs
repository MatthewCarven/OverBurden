using System.Diagnostics;
using Overburden.Core.Content;
using Overburden.Core.Jobs;
using Overburden.Core.Structure;
using Overburden.Core.Voxels;

namespace Overburden.Core.Streaming;

/// <summary>
/// Derives one chunk's support field on a worker (M9b, decision 1): a padded copy of the chunk
/// and its 26 neighbours built here from references captured on the sim thread, the six face
/// layers and the cost table gathered there at submission, and a field back.
/// </summary>
/// <remarks>
/// <para>
/// <b>Detached rather than the chunk's own job slot.</b> <c>Advance</c> allows one job per chunk,
/// and the derive's commonest cause is the edit that also queues the urgent remesh; in the slot
/// the two would serialise — the rock the player just broke waiting a millisecond and a round trip
/// for a field, or the tint landing as a second remesh anyway. So it is the second
/// <see cref="IDetachedJob"/> after the fluid tick, named for the chunk so the queue orders it by
/// distance, in the mesh's band, and urgent when an edit caused it so the tint arrives with the
/// mesh it belongs to.
/// </para>
/// <para>
/// <b>The 27 references are safe for the reason the mesh's are</b> — M3a's copy-on-write, with
/// <c>VoxelsSharedWithJob</c> set on every one by the pipeline at submission. The faces are copied
/// rather than referenced because a neighbour's field is replaced by its own next derive while
/// this one runs; the cost table reads the water table, which is the sim thread's. Nothing here
/// touches the streamer.
/// </para>
/// <para>
/// A detached job is not cancelled by an unview. A result for a chunk that has left lands and is
/// dropped by the region — one wasted millisecond on a worker, never a wrong field.
/// </para>
/// </remarks>
public sealed class SupportDeriveJob : IDetachedJob
{
    [ThreadStatic]
    private static SupportDerive? _derive;

    [ThreadStatic]
    private static PaddedChunk? _padded;

    private readonly VoxelChunk?[] _neighbourhood;
    private readonly MaterialTable _materials;
    private readonly SupportRule _rule;
    private readonly Action<SupportDeriveJob, JobOutcome> _onResult;

    public SupportDeriveJob(
        SupportDeriveRequest request,
        VoxelChunk?[] neighbourhood,
        MaterialTable materials,
        SupportRule rule,
        Action<SupportDeriveJob, JobOutcome> onResult)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(neighbourhood);
        if (neighbourhood.Length != PaddedChunk.NeighbourhoodSize)
        {
            throw new ArgumentException(
                $"Expected {PaddedChunk.NeighbourhoodSize} chunks, got {neighbourhood.Length}.",
                nameof(neighbourhood));
        }

        Request = request;
        _neighbourhood = neighbourhood;
        _materials = materials ?? throw new ArgumentNullException(nameof(materials));
        _rule = rule ?? throw new ArgumentNullException(nameof(rule));
        _onResult = onResult ?? throw new ArgumentNullException(nameof(onResult));
        Urgency = request.Urgent ? MeshChunkJob.EditUrgency : (byte)0;
    }

    public SupportDeriveRequest Request { get; }

    public JobKind Kind => JobKind.Support;

    public ChunkPos Position => Request.Chunk;

    public byte Urgency { get; }

    /// <summary>The derived field. Read by the sim thread once the result arrives.</summary>
    public ChunkSupport? Result { get; private set; }

    public SupportDeriveResult DeriveResult { get; private set; }

    /// <summary>Milliseconds the worker spent, copy and search together.</summary>
    public double ElapsedMillis { get; private set; }

    /// <summary>Milliseconds of that in the padded copy.</summary>
    public double CopyMillis { get; private set; }

    public void Run(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        long started = Stopwatch.GetTimestamp();

        PaddedChunk padded = _padded ??= new PaddedChunk();
        padded.CopyFrom(_neighbourhood, PaddedChunk.MissingMaterial);
        long copied = Stopwatch.GetTimestamp();

        cancellation.ThrowIfCancellationRequested();

        SupportDerive derive = _derive ??= new SupportDerive();
        Result = derive.Derive(padded, _materials, _rule, Request.Costs, Request.Faces, out SupportDeriveResult result);
        DeriveResult = result;

        long finished = Stopwatch.GetTimestamp();
        CopyMillis = (copied - started) * 1000.0 / Stopwatch.Frequency;
        ElapsedMillis = (finished - started) * 1000.0 / Stopwatch.Frequency;
    }

    public void OnResult(JobOutcome outcome, Exception? error) => _onResult(this, outcome);
}
