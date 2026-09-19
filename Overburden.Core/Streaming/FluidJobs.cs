using System.Diagnostics;

using Overburden.Core.Jobs;
using Overburden.Core.Persistence;
using Overburden.Core.Voxels;

namespace Overburden.Core.Streaming;

/// <summary>
/// Reads one chunk's stored fluid panel on the serial lane, after its voxels are here and before
/// it meshes.
/// </summary>
/// <remarks>
/// <para>
/// <b>Its own job for all three arrival paths, rather than a second row read inside
/// <see cref="LoadChunkJob"/></b> (M8b-4c): a never-edited chunk whose cave flooded has a fluid row
/// and no chunk blob, and a cache hit reads no blob at all, so the chunk load covers one path of
/// three. What that costs is one lane query — 6 µs by M6b-1's measurement — per <em>loaded</em>
/// wet chunk, against a ~0.1 ms read of a lake panel either way.
/// </para>
/// <para>
/// <b>In the chunk's own job slot</b>, so that cancellation at unview, retention past it and
/// revival with <see cref="StreamedChunk.FluidPending"/> intact are M2c-2's and M6b's machinery
/// rather than a second copy of it. The stated cost is that a wet chunk's first draw waits one
/// lane trip; the pipeline prints the loads and their lane milliseconds so it is read rather
/// than argued.
/// </para>
/// <para>
/// A row that will not read faults the job, as a chunk blob does: the alternative is a lake
/// quietly missing beside terrain that is there.
/// </para>
/// </remarks>
public sealed class LoadFluidJob : IJob
{
    private readonly IWorldStore _store;

    public LoadFluidJob(IWorldStore store, ChunkPos position)
    {
        _store = store;
        Position = position;
    }

    public JobKind Kind => JobKind.Load;

    public ChunkPos Position { get; }

    /// <summary>The stored panel, or null if the store had no row after all.</summary>
    /// <remarks>
    /// Null is not an error: the index says which chunks had rows when the world opened, and a
    /// row can be gone by the time the job runs only if something deleted it, which nothing does
    /// today. The pipeline clears the chunk's claim and carries on with no water.
    /// </remarks>
    public FluidPanelSnapshot? Result { get; private set; }

    /// <summary>Milliseconds this job spent in the store and the reader, on the lane.</summary>
    public double ElapsedMillis { get; private set; }

    /// <summary>Awake indices the row named that held nothing, left out by the reader (2026-09-19).</summary>
    public int AwakeDropped { get; private set; }

    public void Run(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();

        long entered = Stopwatch.GetTimestamp();

        byte[]? blob = _store.ReadFluid(Position);
        if (blob is not null)
        {
            Result = FluidPanelBlob.Read(blob, Position, out int dropped);
            AwakeDropped = dropped;
        }

        ElapsedMillis = (Stopwatch.GetTimestamp() - entered) * 1000.0 / Stopwatch.Frequency;
    }
}
