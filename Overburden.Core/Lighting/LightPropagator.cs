using Overburden.Core.Meshing;
using Overburden.Core.Voxels;

namespace Overburden.Core.Lighting;

/// <summary>The light grids a propagation may reach. Missing means not loaded.</summary>
/// <remarks>
/// <b>A null grid stops propagation, and that is correct rather than a fallback.</b> Light
/// reaches 15 m and the load margin is 16 m, so a source outside the loaded region cannot
/// contribute inside the visible box — the light computed over what is loaded is exact where it
/// is drawn. See <see cref="ChunkLight.MaxLevel"/>.
/// </remarks>
public interface ILightChunks
{
    ChunkLight? LightAt(ChunkPos chunk);
}

/// <summary>
/// The BFS that maintains both light fields: the add pass, the un-light pass, and the single
/// invalidation that repairs an edit.
/// </summary>
/// <remarks>
/// <para>
/// <b>Light is the first thing in this engine that is not a function of one chunk.</b> A source
/// propagates arbitrarily far and removing one voxel can change light in a chunk that is not a
/// neighbour. So there is no per-chunk boundary in the algorithm at all — one queue over global
/// cell coordinates, resolving chunks as it goes — which is what makes cross-chunk propagation
/// need no special case rather than a fragile one.
/// </para>
/// <para>
/// <b>The invariant this maintains, and the whole of the oracle:</b>
/// <code>
/// V(cell) = max( S(cell), max over conducting neighbours n of ( V(n) − attenuation(n → cell) ) )
/// </code>
/// where <c>S</c> is the cell's intrinsic source. That fixpoint is unique, so it can be checked
/// <em>locally</em>, cell by cell, with no second propagation to get wrong. M5-PLAN's oracle was
/// "incremental equals from-scratch"; this is stronger and shares no code with the subject.
/// </para>
/// </remarks>
public sealed class LightPropagator
{
    /// <summary>Cells whose value rose and must be spread from.</summary>
    private readonly Queue<LightCellPos>[] _add = [new(), new()];

    /// <summary>Cells whose value fell, with the value they held. See <see cref="Drain"/>.</summary>
    private readonly Queue<(LightCellPos Cell, int Value)>[] _remove = [new(), new()];

    private readonly ILightChunks _chunks;

    private ChunkPos _cachedPosition;
    private ChunkLight? _cachedLight;
    private bool _hasCache;

    private Func<long>? _nowTicks;
    private long _deadlineTicks;
    private int _sinceClockCheck;
    private bool _outOfBudget;

    public LightPropagator(ILightChunks chunks) => _chunks = chunks;

    /// <summary>Cell visits across every drain so far. The denominator M5d reports against.</summary>
    public long CellsVisited { get; private set; }

    /// <summary>
    /// Where every cell whose value actually moved is appended, or null to record nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>That set is the exact remesh scope</b>, and it is the same shape of contract
    /// <see cref="ChunkLight.BuildDerived"/> uses for the derived side: the collection belongs to
    /// the caller, so the caller decides how often it is cleared. Null costs nothing, which is
    /// what a test doing a from-scratch reflood wants.
    /// </para>
    /// <para>
    /// <b>The caller must clear it each cycle, and the drain budget is what bounds it.</b> A
    /// budgeted drain writes at most one frame's worth of cells, so the set stays a few tens of
    /// thousands of entries however large the world is. Left to accumulate across an unbudgeted
    /// flood of a streaming world it would reach one entry per cell per chunk, which is where the
    /// memory would go.
    /// </para>
    /// </remarks>
    public ICollection<LightCellPos>? Changes { get; set; }

    /// <summary>Cell visits between clock reads while draining under a deadline.</summary>
    /// <remarks>
    /// A visit is a queue pop and up to six neighbour tests — tens of nanoseconds. Reading the
    /// clock per visit would be a large fraction of the work being measured, and 512 visits is
    /// well under a millisecond, so the overshoot past the deadline is smaller than the jitter
    /// on the deadline itself.
    /// </remarks>
    private const int BudgetCheckInterval = 512;

    public bool HasWork =>
        _add[0].Count > 0 || _add[1].Count > 0 || _remove[0].Count > 0 || _remove[1].Count > 0;

    /// <summary>
    /// Queues every source cell in a chunk, for a grid whose values have just been cleared.
    /// </summary>
    public void SeedSources(ChunkPos chunk)
    {
        ChunkLight? light = Light(chunk);
        if (light is null) return;

        for (int index = 0; index < LightCellPos.Volume; index++)
        {
            var (lx, ly, lz) = LightCellPos.FromIndex(index);
            var cell = LightCellPos.FromChunkLocal(chunk, lx, ly, lz);

            foreach (LightChannel channel in Channels)
            {
                int source = light.Source(index, channel);
                if (source <= 0) continue;

                if (source > light.Get(index, channel)) light.Set(index, channel, source);
                _add[(int)channel].Enqueue(cell);
            }
        }
    }

    /// <summary>
    /// Repairs the light around a cell whose derived data changed — conductance, emission, sky,
    /// or all three.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One operation for both directions, because the un-light pass already handles the
    /// harder one.</b> Opening a face can only raise light and closing one can only lower it,
    /// but knowing which happened buys nothing: darkening a cell and letting the add pass
    /// re-light it from whatever survived is correct either way, and a single code path is a
    /// single path to get right. The redundant work is bounded by the light radius.
    /// </para>
    /// <para>
    /// <b>The caller's contract, and it is the whole of why this is one cell rather than seven:
    /// invalidate every cell <see cref="ChunkLight.BuildDerived"/> reported as changed, not just
    /// the one holding the edited voxel.</b> The case that looks like it needs more is a face
    /// closing: a walk starting at A never examines B, because the face it would cross no longer
    /// conducts, so light B received through it would survive with nothing to remove it. But
    /// conductance is recorded on <em>both</em> cells of a face, so a face that changes always
    /// reports both of them — and an earlier version that darkened the six neighbours as well
    /// turned out to be provably dead work: removing it fails no test, and cannot, because the
    /// changed set already contains B. <c>SealingAFaceReportsBothOfItsCells</c> pins the
    /// property that makes this safe, since nothing else would notice it stopping being true.
    /// </para>
    /// </remarks>
    public void Invalidate(LightCellPos cell) => Darken(cell);

    /// <summary>
    /// Every cell whose voxels lie inside a chunk, invalidated. Use after a chunk's derived data
    /// is rebuilt wholesale — an unload and revive, or a first light.
    /// </summary>
    public void InvalidateChunk(ChunkPos chunk)
    {
        for (int cz = 0; cz < LightCellPos.CellsPerChunk; cz++)
        for (int cx = 0; cx < LightCellPos.CellsPerChunk; cx++)
        for (int cy = 0; cy < LightCellPos.CellsPerChunk; cy++)
        {
            Invalidate(LightCellPos.FromChunkLocal(chunk, cx, cy, cz));
        }
    }

    /// <summary>
    /// Re-spreads from every cell that touches <paramref name="chunk"/> from outside it, without
    /// disturbing any of their values.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Call this whenever a chunk's grid comes into existence, and the failure it prevents is
    /// ordering-dependent.</b> This BFS pushes rather than pulls, and a push into a chunk with no
    /// grid is silently dropped — correct while that chunk will never exist, and a lost update
    /// the moment it turns up a few frames later. Nothing else would put it back: the neighbour
    /// that tried has long since left both queues, and the new chunk seeding its own sources says
    /// nothing about light arriving from outside.
    /// </para>
    /// <para>
    /// The symptom is a chunk lit by whichever of its neighbours happened to be derived after it
    /// and dark towards the ones derived before — so it moves with worker scheduling, and a chunk
    /// under a lit shaft is exactly where it shows.
    /// </para>
    /// <para>
    /// <b>Six face layers, not the whole shell.</b> Light crosses faces only, so the twelve edges
    /// and eight corners of the surrounding shell cannot conduct into this chunk at all: 384
    /// cells rather than 488, and the difference is a statement about the model rather than a
    /// saving.
    /// </para>
    /// </remarks>
    public void SpreadInto(ChunkPos chunk)
    {
        const int size = LightCellPos.CellsPerChunk;

        for (int face = 0; face < VoxelFace.Count; face++)
        {
            int axis = VoxelFace.Axis[face];
            int outward = VoxelFace.Direction[face] > 0 ? size : -1;

            int uAxis = axis == 0 ? 1 : 0;
            int vAxis = axis == 2 ? 1 : 2;

            Span<int> local = stackalloc int[3];

            for (int v = 0; v < size; v++)
            for (int u = 0; u < size; u++)
            {
                local[axis] = outward;
                local[uAxis] = u;
                local[vAxis] = v;

                Respread(LightCellPos.FromChunkLocal(chunk, local[0], local[1], local[2]));
            }
        }
    }

    /// <summary>Queues a cell to spread again, leaving its value exactly as it is.</summary>
    /// <remarks>
    /// Deliberately <em>not</em> <see cref="Darken"/>: that resets a cell to its own source and
    /// runs the un-light walk, which is the right repair for derived data that moved and pure
    /// waste for a cell whose value is already correct and merely needs re-announcing. The add
    /// pass only writes where it is strictly brighter, so re-spreading is idempotent.
    /// </remarks>
    private void Respread(LightCellPos cell)
    {
        ChunkLight? light = Light(cell.Chunk);
        if (light is null) return;

        int index = cell.LocalIndex;

        foreach (LightChannel channel in Channels)
        {
            if (light.Get(index, channel) > 0) _add[(int)channel].Enqueue(cell);
        }
    }

    /// <summary>
    /// Drops a cell back to its own source value and queues the walk that clears whatever it lit.
    /// </summary>
    private void Darken(LightCellPos cell)
    {
        ChunkLight? light = Light(cell.Chunk);
        if (light is null) return;

        int index = cell.LocalIndex;

        foreach (LightChannel channel in Channels)
        {
            int value = light.Get(index, channel);
            int source = light.Source(index, channel);

            // The cell ends at its own source either way, and which direction it moved decides
            // what has to be queued. Dropping needs the un-light walk to clear what this cell
            // was lighting; *rising* needs nothing but the spread — and forgetting that second
            // case is why a freshly placed lamp lit nothing at all: its emission was in the
            // derived data and its value was still the zero it had as empty air.
            if (value != source)
            {
                light.Set(index, channel, source);
                Changes?.Add(cell);
            }

            if (value > source) _remove[(int)channel].Enqueue((cell, value));

            // Queued even when nothing changed: a face that just opened has to be spread
            // through, and the value that spreads is whatever the cell already holds.
            if (light.Get(index, channel) > 0) _add[(int)channel].Enqueue(cell);
        }
    }

    /// <summary>
    /// Runs both passes to a fixpoint. Returns the cells visited.
    /// </summary>
    /// <remarks>
    /// <b>Removal first, then addition, and the order is the algorithm.</b> The un-light walk
    /// zeroes every cell whose value is exactly what this one would have given it, and collects
    /// every neighbour that kept its light as a re-seed for the add pass. Skip the collection and
    /// light never comes back where two sources overlapped — the dark patch nobody can explain,
    /// invisible until somebody breaks a wall in exactly the wrong place. It is what
    /// <c>DroppingTheReseedStep</c> in the break-recipes removes.
    /// </remarks>
    public long Drain() => Drain(nowTicks: null, deadlineTicks: 0);

    /// <summary>
    /// Runs both passes until the fixpoint or the deadline, whichever comes first. Returns the
    /// cells visited.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Resuming is free because the queues are fields.</b> Stopping mid-pass leaves the
    /// frontier exactly where it was and the next call carries on; there is no checkpoint to
    /// build and no partial state to reconstruct.
    /// </para>
    /// <para>
    /// <b>A partially drained field is wrong, and nothing may mesh from it</b> — which is what
    /// <see cref="Worldgen.GenerationStage.Lit"/> requiring <see cref="HasWork"/> to be false
    /// enforces. That is the whole reason the barrier carries a global term.
    /// </para>
    /// <para>
    /// <b>Time-sliced rather than counted, and the precedent is the mesh-apply budget rather than
    /// the mesh-free one.</b> Freeing is counted because Godot defers the deallocation and a
    /// stopwatch around the call measures nothing; this is our own CPU work start to finish, so
    /// it can be measured as it is spent.
    /// </para>
    /// </remarks>
    /// <param name="nowTicks">
    /// <see cref="System.Diagnostics.Stopwatch"/>-tick clock, or null to run to the fixpoint with
    /// no deadline at all. Injectable so a test drives the budget rather than racing a real one.
    /// </param>
    public long Drain(Func<long>? nowTicks, long deadlineTicks)
    {
        _nowTicks = nowTicks;
        _deadlineTicks = deadlineTicks;
        _sinceClockCheck = 0;
        _outOfBudget = false;

        long visited = 0;

        foreach (LightChannel channel in Channels)
        {
            // Removal before addition within a channel, which is the algorithm — see Drain's
            // remarks. Bailing between the two is safe for the same reason bailing anywhere is:
            // the queues keep the frontier and the next call resumes it.
            visited += Remove(channel);
            if (_outOfBudget) break;

            visited += Add(channel);
            if (_outOfBudget) break;
        }

        _nowTicks = null;
        CellsVisited += visited;
        return visited;
    }

    /// <summary>Whether the deadline has passed, checked one time in every 512 visits.</summary>
    private bool OutOfBudget()
    {
        if (_nowTicks is null) return false;

        if (++_sinceClockCheck < BudgetCheckInterval) return false;

        _sinceClockCheck = 0;
        _outOfBudget = _nowTicks() >= _deadlineTicks;
        return _outOfBudget;
    }

    private long Remove(LightChannel channel)
    {
        Queue<(LightCellPos Cell, int Value)> queue = _remove[(int)channel];
        Queue<LightCellPos> reseeds = _add[(int)channel];
        long visited = 0;

        while (queue.Count > 0)
        {
            if (OutOfBudget()) break;

            (LightCellPos cell, int old) = queue.Dequeue();
            visited++;

            ChunkLight? light = Light(cell.Chunk);
            if (light is null) continue;

            int index = cell.LocalIndex;

            for (int face = 0; face < VoxelFace.Count; face++)
            {
                if (!light.Conducts(index, face)) continue;

                int expected = old - Attenuation(channel, face, old);
                if (expected <= 0) continue;

                int[] offset = VoxelFace.Offset[face];
                var next = cell.Offset(offset[0], offset[1], offset[2]);

                ChunkLight? neighbour = Light(next.Chunk);
                if (neighbour is null) continue;

                int nextIndex = next.LocalIndex;
                int value = neighbour.Get(nextIndex, channel);
                if (value == 0) continue;

                if (value == expected)
                {
                    // Exactly what this cell was giving it, so this cell is why it is lit.
                    // Falling back to the neighbour's own source rather than to zero is what
                    // stops the walk erasing the lamp it is walking away from.
                    int source = neighbour.Source(nextIndex, channel);
                    neighbour.Set(nextIndex, channel, source);
                    Changes?.Add(next);
                    queue.Enqueue((next, value));

                    if (source > 0) reseeds.Enqueue(next);
                }
                else
                {
                    // Lit by something else, so it survives — and it is exactly what re-fills
                    // the hole this walk is making.
                    reseeds.Enqueue(next);
                }
            }
        }

        return visited;
    }

    private long Add(LightChannel channel)
    {
        Queue<LightCellPos> queue = _add[(int)channel];
        long visited = 0;

        while (queue.Count > 0)
        {
            if (OutOfBudget()) break;

            LightCellPos cell = queue.Dequeue();
            visited++;

            ChunkLight? light = Light(cell.Chunk);
            if (light is null) continue;

            int index = cell.LocalIndex;
            int value = light.Get(index, channel);
            if (value <= 0) continue;

            for (int face = 0; face < VoxelFace.Count; face++)
            {
                if (!light.Conducts(index, face)) continue;

                int carried = value - Attenuation(channel, face, value);
                if (carried <= 0) continue;

                int[] offset = VoxelFace.Offset[face];
                var next = cell.Offset(offset[0], offset[1], offset[2]);

                ChunkLight? neighbour = Light(next.Chunk);
                if (neighbour is null) continue;

                int nextIndex = next.LocalIndex;

                // "Only if greater" is what makes this terminate: a value can rise at most
                // fifteen times, so the queue drains however tangled the cave is.
                if (carried <= neighbour.Get(nextIndex, channel)) continue;

                neighbour.Set(nextIndex, channel, carried);
                Changes?.Add(next);
                queue.Enqueue(next);
            }
        }

        return visited;
    }

    /// <summary>
    /// What a step costs. One per metre, except full skylight falling straight down.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Without the exception a shaft dies fifteen metres down</b>, and a shaft that goes dark
    /// is the opposite of what a mine is about. With it, daylight reaches the bottom of any
    /// vertical opening however deep, and a side tunnel off that shaft still falls off at a metre
    /// a step.
    /// </para>
    /// <para>
    /// <b>The exception is restricted to full brightness on purpose.</b> Letting an
    /// already-attenuated value fall unattenuated would hang a vertical column of light under
    /// every dimly lit patch in the world — a tunnel lit to 9 would paint 9 straight down to
    /// bedrock. That is the same rule every voxel game converges on, and the reason is this one.
    /// </para>
    /// </remarks>
    private static int Attenuation(LightChannel channel, int face, int value) =>
        channel == LightChannel.Sky && face == NegativeY && value == ChunkLight.MaxLevel ? 0 : 1;

    /// <summary>Index of the −Y face in <see cref="VoxelFace"/>'s order.</summary>
    private const int NegativeY = 3;

    private static readonly LightChannel[] Channels = [LightChannel.Sky, LightChannel.Block];

    /// <summary>
    /// The grid for a chunk, with a one-entry cache.
    /// </summary>
    /// <remarks>
    /// A BFS is spatially coherent — consecutive cells are usually in the same chunk — so one
    /// slot removes almost every lookup. Held here rather than in the caller because both passes
    /// and the invalidation all resolve cells the same way.
    /// </remarks>
    private ChunkLight? Light(ChunkPos chunk)
    {
        if (_hasCache && _cachedPosition == chunk) return _cachedLight;

        _cachedPosition = chunk;
        _cachedLight = _chunks.LightAt(chunk);
        _hasCache = true;
        return _cachedLight;
    }

    /// <summary>
    /// Forgets the chunk cache. Call when a grid is added or removed underneath the propagator.
    /// </summary>
    public void ForgetCache() => _hasCache = false;
}
