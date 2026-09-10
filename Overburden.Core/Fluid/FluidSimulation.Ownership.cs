using Overburden.Core.Persistence;
using Overburden.Core.Voxels;

namespace Overburden.Core.Fluid;

/// <summary>
/// Who owns the grid: while a tick is in flight it is the tick's, and everything else queues or
/// waits (the water thread, slice 3a).
/// </summary>
/// <remarks>
/// <para>
/// <b>The rule slice 3 needs, built and exercised before any thread exists.</b> A tick has been
/// sliced across frames since M8b's budget slice, so <i>a tick is in flight</i> is a state that
/// already lasts up to tens of frames — on the played world a 33,961-cell tick spans 46 of them.
/// Everything the sim thread does to the grid in that window is therefore already the case slice 3b
/// will have to survive, and it can be made correct here with no concurrency at all.
/// </para>
/// <para>
/// <b>Mutations queue, reads refuse, and the split is not arbitrary.</b> A void mutator can be
/// applied later and nobody is waiting on an answer; a read cannot, because there is no honest
/// value to return for a grid half-way through a tick — the same reasoning that makes
/// <see cref="VerifyQuiescent"/> throw rather than answer. So <see cref="Disturb"/>,
/// <see cref="ChunkArrived"/>, <see cref="Restore"/> and <see cref="Evict"/> defer, and
/// <see cref="TakeAllDirty"/>, <see cref="TakeDirty"/>, <see cref="TakeEvicted"/> and
/// <see cref="Reclaim"/> throw so that a caller which has not been taught to wait is loud rather
/// than silently wrong.
/// </para>
/// <para>
/// <b>The deferred list is applied the instant the tick completes, by the tick itself</b>, so
/// <i>the grid is not the tick's</i> and <i>nothing is deferred</i> are one state. The first shape
/// had the caller apply it at the next boundary, and that leaves a gap: between a tick ending and
/// the next beginning a mutation lands directly, so a <c>Reclaim</c> could run before the
/// <c>Evict</c> that had been deferred ahead of it and the panel would leave after the chunk was
/// back. Arrival order is the whole point of a queue, so the queue drains before anything else
/// can see the grid. Slice 3b keeps that: the sim thread collects the job's result and drains
/// the list in the same call, before the flag clears to anyone.
/// </para>
/// <para>
/// <b><see cref="Add"/>, <see cref="Remove"/> and <see cref="HandOff"/> are deliberately not
/// deferred</b>: each returns how much it moved, and a deferred call cannot answer. They are
/// boundary calls by construction — the relay pours at a boundary, the drain draws at a boundary,
/// the hand-off runs at a boundary — so they assert instead. The one caller that could fire
/// mid-tick is a keypress, and queueing a keypress is the caller's job, which is where the relay
/// already keeps its litres.
/// </para>
/// <para>
/// <b>What changes for the player: an edit under a sliced tick lands at the boundary rather than
/// inside it.</b> Half a second at worst, and it removes a real oddity — a chunk bite could
/// previously alter the world half-way through a trajectory that had already read the old terrain.
/// </para>
/// <para>
/// <b>What is deliberately still readable mid-tick, and is slice 3b's to gate</b>:
/// <see cref="AwakeHeldCells"/>, <see cref="TotalMillilitres"/>, <see cref="Rested"/>,
/// <see cref="SurfaceCells"/> and <see cref="Grid"/> itself. Synchronous, they are coherent between
/// cell visits and the report line reads them; under a worker they are the reads that must wait
/// for the boundary or use the value the last one published.
/// </para>
/// </remarks>
public sealed partial class FluidSimulation
{
    private enum DeferredKind : byte
    {
        Disturb = 0,
        ChunkArrived = 1,
        Restore = 2,
        Evict = 3,
    }

    private readonly struct Deferred(DeferredKind kind, VoxelPos voxel, ChunkPos chunk, FluidPanelSnapshot? panel)
    {
        public readonly DeferredKind Kind = kind;
        public readonly VoxelPos Voxel = voxel;
        public readonly ChunkPos Chunk = chunk;
        public readonly FluidPanelSnapshot? Panel = panel;
    }

    // In arrival order, because two edits to one voxel and an arrival for the chunk they are in
    // have to land the way they happened.
    private readonly List<Deferred> _deferred = [];

    /// <summary>Mutations that arrived mid-tick and are waiting for the tick to complete.</summary>
    public int DeferredCount => _deferred.Count;

    /// <summary>Mutations deferred over this simulation's lifetime.</summary>
    public long Deferrals { get; private set; }

    /// <inheritdoc />
    /// <remarks>
    /// The same condition as <see cref="TickInProgress"/> today, and named separately because it is
    /// a statement about ownership rather than about progress: slice 3b keeps the name and changes
    /// what sets it.
    /// </remarks>
    public bool GridOwnedByTick => TickInProgress;

    /// <summary>Applies everything that arrived mid-tick. Called by <see cref="Complete"/>, once the batch is cleared.</summary>
    /// <remarks>
    /// After <c>Ticks</c> has advanced, so an <see cref="Evict"/> deferred mid-tick is due one tick
    /// later than one marked between ticks — the linger rule M8b-4c states, arrived at by the
    /// queue rather than by a clause in <c>Evict</c>. Before the result is built, so the pending
    /// count it reports includes what the deferred edits woke.
    /// </remarks>
    private void ApplyDeferred()
    {
        if (_deferred.Count == 0)
        {
            return;
        }

        if (GridOwnedByTick)
        {
            throw new InvalidOperationException(
                $"ApplyDeferred asked mid-tick, with {RemainingInTick} cell(s) still to visit; "
                + "the deferred list is applied when the tick completes and nowhere else.");
        }

        // Indexed rather than enumerated, and trimmed afterwards: applying one defers nothing —
        // the grid is not the tick's — but a Restore may throw, and the list must then still hold
        // what has not been applied rather than have lost it.
        int applied = 0;
        try
        {
            for (; applied < _deferred.Count; applied++)
            {
                Deferred op = _deferred[applied];
                switch (op.Kind)
                {
                    case DeferredKind.Disturb:
                        Disturb(op.Voxel);
                        break;
                    case DeferredKind.ChunkArrived:
                        ChunkArrived(op.Chunk);
                        break;
                    case DeferredKind.Restore:
                        Restore(op.Panel!);
                        break;
                    case DeferredKind.Evict:
                        Evict(op.Chunk);
                        break;
                }
            }
        }
        finally
        {
            _deferred.RemoveRange(0, applied);
        }
    }

    /// <summary>Queues a mutation that arrived while the tick owned the grid. True when it queued.</summary>
    private bool Defer(DeferredKind kind, VoxelPos voxel = default, ChunkPos chunk = default, FluidPanelSnapshot? panel = null)
    {
        if (!GridOwnedByTick)
        {
            return false;
        }

        _deferred.Add(new Deferred(kind, voxel, chunk, panel));
        Deferrals++;
        return true;
    }

    /// <summary>Refuses a read of a grid that is half-way through a tick.</summary>
    private void RefuseMidTick(string what)
    {
        if (GridOwnedByTick)
        {
            throw new InvalidOperationException(
                $"{what} asked mid-tick, with {RemainingInTick} cell(s) still to visit; the grid "
                + "belongs to the tick until it completes. Finish it with Step(), or wait for the "
                + "boundary — there is no honest answer for a grid half-way through a tick.");
        }
    }
}
