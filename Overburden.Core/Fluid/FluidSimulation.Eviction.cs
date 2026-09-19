using Overburden.Core.Persistence;
using Overburden.Core.Voxels;

namespace Overburden.Core.Fluid;

/// <summary>
/// M8b-4c: the panel leaves RAM with its chunk — one tick after it.
/// </summary>
/// <remarks>
/// <para>
/// <b>A forgotten chunk's panel lingers one tick, and the tick does the bookkeeping the snapshot
/// needs.</b> The row on disk lists a chunk's <em>paused</em> cells as awake; its <em>queued</em>
/// cells — a flood front inside a chunk that has just left the box — are found only by walking the
/// active set, which the snapshot at unview refuses to do per chunk of a shell (M8b-4b). Released
/// the instant the chunk goes, those cells are visited next tick, read an empty panel and sleep:
/// not paused, not listed, forgotten — a stranded front on every walk across a flood, with a move
/// available that nothing will ever take. So the panel stays: every queued cell of the chunk is
/// visited in the first tick begun after the mark, finds the chunk absent, and is paused by M8b-4a's
/// rule; at the boundary that completes that tick the paused list is exact and the panel goes.
/// Cells a neighbour wakes <em>into</em> the chunk meanwhile are face cells by construction —
/// nothing else can reach an untracked chunk — and the arrival's face wake covers them.
/// </para>
/// <para>
/// <b>Two things the eviction must clear and one it must not touch.</b> <c>_storedAwake</c> must
/// forget the chunk, or the next <see cref="TakeAllDirty"/> snapshots it with no panel and writes
/// its row empty — the lake deleted by the autosave, silently, with the identity exact.
/// <c>_emptied</c> must not gain it: an unload is not a drain, and the row is the truth
/// (<see cref="FluidGrid.Evict"/> is the way in that does not mark the row stale). And the paused
/// set goes with the panel, because after the release nothing in that chunk can be woken —
/// <c>Wake</c> refuses a cell that can neither hold nor holds.
/// </para>
/// <para>
/// <b><c>held</c> leaves RAM now, so the identity gains a term</b>: <c>held == injected + drawn +
/// loaded − removed − displaced − handed − evicted</c>. <see cref="LoadedMillilitres"/> counts every
/// restore and <see cref="EvictedMillilitres"/> every release, so the net on disk stays visible.
/// </para>
/// </remarks>
public sealed partial class FluidSimulation
{
    // Chunks whose panel is on its way out, and the completed-tick count at which it may go. Marked
    // between ticks the next tick visits everything queued, so the panel goes when that tick
    // completes. A mark that arrives mid-tick is deferred until this tick completes (slice 3a),
    // which puts it one tick later by the same arithmetic — cells woken earlier in this tick are
    // queued for the next one, so the panel must outlive that one too. Both are constructed in the
    // tests and asserted strictly.
    private readonly Dictionary<ChunkPos, long> _evicting = [];

    /// <summary>Millilitres released with their chunks, to disk. A term of the identity.</summary>
    public long EvictedMillilitres { get; private set; }

    /// <summary>Panels released with their chunks, over the lifetime.</summary>
    public int EvictedPanels { get; private set; }

    /// <summary>Panels marked for release and still in RAM, waiting for their tick.</summary>
    public int EvictingPanelCount => _evicting.Count;

    /// <summary>Chunks that came back inside the eviction window and kept their panel.</summary>
    public int ReclaimedPanels { get; private set; }

    /// <inheritdoc />
    public void Evict(ChunkPos chunk)
    {
        if (Defer(DeferredKind.Evict, chunk: chunk))
        {
            return;
        }

        // A chunk with no panel has nothing to linger for; if it owes an empty row (drained after
        // it was stored) the next TakeEvicted takes it at once. Marked in every case so the caller
        // has one call to make, and re-marking a chunk already going only moves its tick.
        _evicting[chunk] = _grid.HasPanel(chunk) ? Ticks + 1 : Ticks;
    }

    /// <inheritdoc />
    public bool MayHoldWater(ChunkPos chunk)
    {
        // With a worker on the grid the panel dictionary is its; the panels as they stood at
        // submission and the captured set are the whole of what the worker can have added to,
        // and a chunk in the captured set is loaded already (slice 3b).
        bool hasPanel = _job is null
            ? _grid.HasPanel(chunk)
            : _publishedPanels.Contains(chunk) || (_capturedView?.Covers(chunk) ?? false);

        if (hasPanel || _evicting.ContainsKey(chunk))
        {
            return true;
        }

        // A mark that arrived mid-tick is in the queue rather than the dictionary until the tick
        // completes, and the chunk it names has come back before it landed.
        foreach (Deferred op in _deferred)
        {
            if (op.Kind == DeferredKind.Evict && op.Chunk == chunk)
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public bool Reclaim(ChunkPos chunk)
    {
        RefuseMidTick(nameof(Reclaim));

        if (_evicting.Remove(chunk))
        {
            ReclaimedPanels++;
            return true;
        }

        return _grid.HasPanel(chunk);
    }

    /// <inheritdoc />
    public void TakeEvicted(List<FluidPanelSnapshot> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        RefuseMidTick(nameof(TakeEvicted));

        if (_evicting.Count == 0)
        {
            return;
        }

        List<ChunkPos>? due = null;
        foreach ((ChunkPos chunk, long at) in _evicting)
        {
            if (Ticks >= at)
            {
                (due ??= []).Add(chunk);
            }
        }

        if (due is null)
        {
            return;
        }

        foreach (ChunkPos chunk in due)
        {
            _evicting.Remove(chunk);

            // Written if the row would otherwise be stale: the panel was written since its last
            // snapshot, it was drained since (an empty row is owed), it has paused cells the row
            // may not list, or its row lists awake cells that have since rested. A clean panel
            // with no paused cells and a row that lists nothing awake is exactly on disk already.
            bool stale = _grid.IsDirty(chunk) || _paused.ContainsKey(chunk) || _storedAwake.Contains(chunk);
            if (stale)
            {
                (ushort, ushort)[] cells = _grid.TakeSnapshot(chunk);
                ushort[] awake = _paused.TryGetValue(chunk, out HashSet<VoxelPos>? paused)
                    ? AwakeIndices(HeldIndices(paused))
                    : [];
                into.Add(new FluidPanelSnapshot(chunk, cells, awake));
            }

            long released = _grid.Evict(chunk);
            if (released > 0)
            {
                EvictedMillilitres += released;
                EvictedPanels++;
            }

            if (_paused.Remove(chunk, out HashSet<VoxelPos>? set))
            {
                _pausedCells -= set.Count;
            }

            _storedAwake.Remove(chunk);

            // The inflow stamp goes with the chunk. A chunk that leaves and comes back has
            // received nothing since, and a map that only ever grew would hold an entry for
            // every chunk the world has ever poured into.
            ForgetInflow(chunk);
        }
    }
}
