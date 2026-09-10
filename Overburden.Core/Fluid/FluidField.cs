using Overburden.Core.Voxels;

namespace Overburden.Core.Fluid;

/// <summary>What the water needs to know about one voxel.</summary>
/// <remarks>
/// <b>One bit of storage behind a three-valued accessor</b>, so that widening it costs a build
/// function and an accessor rather than a format. <see cref="FluidClass.Unknown"/> is a chunk the
/// field has not been built for, which is not the same answer as
/// <see cref="FluidClass.Solid"/> — the distinction <c>VoxelRay.Blocked</c> made first and
/// <see cref="IFluidTerrain.IsLoaded"/> made for the water.
/// </remarks>
public enum FluidClass : byte
{
    /// <summary>No field for this chunk. Ask the source.</summary>
    Unknown = 0,

    /// <summary>Rock, or a material that is neither air nor a fluid.</summary>
    Solid = 1,

    /// <summary>Water may occupy it.</summary>
    Holdable = 2,
}

/// <summary>
/// A baked copy of the one question the automaton asks the world — the terrain a worker can read.
/// </summary>
/// <remarks>
/// <para>
/// <b>It is an <see cref="IFluidTerrain"/> built from an <see cref="IFluidTerrain"/>, which is why
/// nothing else changes.</b> The tick, every fixture and every bench arm take a terrain and cannot
/// tell the difference, so substituting this for <c>VoxelFluidTerrain</c> is an A/B between two
/// terrains rather than between two code paths, and "a tick against the field lands the same water
/// as a tick against the world" is a test rather than an argument.
/// </para>
/// <para>
/// <b>It exists for slice 3, not for speed, and the measurement says so.</b> <c>--terrain</c>
/// priced the game's read at 1.47x Release / 1.59x Debug of a baked field's — <i>a third of a
/// visit, not a multiple</i> — so this does not pay for itself on throughput. What it buys is a
/// terrain that does not change under a reader: <c>ChunkStreamer</c>'s dictionary is mutated by the
/// sim thread as chunks arrive and leave, and a worker cannot read it.
/// </para>
/// <para>
/// <b>A miss falls through to the source rather than answering wrongly</b>, which is what makes
/// this slice behaviour-preserving by construction: a chunk nobody built a field for gives the same
/// answer it always gave, and <see cref="Misses"/> counts how often that happened. **Slice 3 is
/// where a miss becomes an error**, because a worker cannot fall through; until then the counter is
/// the evidence that the pre-built set is the right set.
/// </para>
/// <para>
/// <b>Never mutated in place — an edit replaces the array.</b> M3a's copy-on-write rule at 4 KiB
/// instead of a palette. A reader that captured the old array keeps reading a consistent world one
/// edit out of date, which is decision 7 ("mutations during a tick land at the next boundary")
/// arriving in the storage rather than in the caller.
/// </para>
/// </remarks>
public sealed class FluidField : IFluidTerrain
{
    /// <summary>Bytes of bits for one chunk: 32,768 voxels at one bit each.</summary>
    public const int BytesPerChunk = VoxelConstants.ChunkVolume / 8;

    private readonly IFluidTerrain _source;
    private readonly Dictionary<ChunkPos, byte[]> _chunks = [];

    // The lookup the hot path repeats: a visit asks about one cell and its six face neighbours, and
    // five of the seven are in the same chunk. FluidGrid caches its panel lookup for the same
    // reason and to the same shape.
    private ChunkPos _cacheKey;
    private byte[]? _cache;

    public FluidField(IFluidTerrain source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
    }

    /// <summary>Chunks a field is built for.</summary>
    public int Chunks => _chunks.Count;

    /// <summary>What those fields cost in RAM.</summary>
    public long Bytes => (long)_chunks.Count * BytesPerChunk;

    /// <summary>Fields built over this field's lifetime.</summary>
    public long Builds { get; private set; }

    /// <summary>Fields dropped because their chunk left the reachable set.</summary>
    public long Forgets { get; private set; }

    /// <summary>Edits that replaced a chunk's array.</summary>
    public long Invalidations { get; private set; }

    /// <summary>
    /// Chunks <see cref="Sync"/> would have baked and could not, because the world could not
    /// answer for them yet. They fall through until it can.
    /// </summary>
    public long Declined { get; private set; }

    /// <summary>
    /// Questions about a chunk with no field, answered by the source instead.
    /// </summary>
    /// <remarks>
    /// <b>The number that says whether the reachable set is right.</b> A miss is free and correct
    /// today; on a worker it would be a read of the streamer's dictionary from the wrong thread, so
    /// slice 3 cannot start until this is zero across a flood.
    /// </remarks>
    public long Misses { get; private set; }

    /// <summary>
    /// Of those, the ones asked while a tick was running — <b>the number that has to be zero</b>.
    /// </summary>
    /// <remarks>
    /// <b>A miss at a boundary is structural and harmless; a miss inside a tick is what slice 3
    /// cannot have.</b> The relay and the portal drain call <see cref="FluidSimulation.Add"/>,
    /// which asks whether a voxel can hold <i>before</i> any panel exists there, so the reachable
    /// set — derived from the panels — cannot know about it in advance: 51,200 of them on one
    /// headless run, exactly 6,400 poured cells times the eight questions an <c>Add</c> asks.
    /// Those happen on the sim thread, where falling through to the streamer is what the code did
    /// anyway. A tick runs on a worker, and there falling through would be a read of the streamer's
    /// dictionary from the wrong thread.
    /// </remarks>
    public long TickMisses { get; private set; }

    /// <summary>Brackets a tick, so a miss inside one can be told from a miss at a boundary.</summary>
    public bool InTick { get; set; }

    /// <summary>The class of one voxel, or <see cref="FluidClass.Unknown"/> if no field covers it.</summary>
    public FluidClass ClassAt(VoxelPos voxel)
    {
        byte[]? bits = Bits(voxel.Chunk);
        if (bits is null)
        {
            return FluidClass.Unknown;
        }

        int index = voxel.LocalIndex;
        return (bits[index >> 3] & (1 << (index & 7))) != 0 ? FluidClass.Holdable : FluidClass.Solid;
    }

    /// <inheritdoc />
    public bool CanHold(VoxelPos voxel)
    {
        FluidClass held = ClassAt(voxel);
        if (held != FluidClass.Unknown)
        {
            return held == FluidClass.Holdable;
        }

        Misses++;
        if (InTick)
        {
            TickMisses++;
        }

        return _source.CanHold(voxel);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <b>Always the source, never the field's own coverage.</b> A field is built only for a chunk
    /// the world could answer for, but the chunk can unload afterwards while its field is still
    /// wanted, and answering "loaded" then would tell M8b-4a's pause rule that water in a departed
    /// chunk is safe to move. It is asked only where <see cref="CanHold"/> has already said no, so
    /// it is off the hot path; slice 3 replaces it with the captured set.
    /// </remarks>
    public bool IsLoaded(VoxelPos voxel) => _source.IsLoaded(voxel);

    /// <summary>
    /// Builds a field for every chunk in <paramref name="reachable"/> that has none, and drops
    /// every field whose chunk is not in it.
    /// </summary>
    /// <remarks>
    /// <b>Call at a tick boundary and nowhere else.</b> Dropping a chunk's field is what unloading
    /// looks like to the water, and doing it under a tick would change the terrain mid-trajectory.
    /// Forgetting through the same call that builds means an unloaded chunk needs no hook of its
    /// own: it simply stops being reachable.
    /// </remarks>
    public void Sync(IReadOnlyCollection<ChunkPos> reachable)
    {
        ArgumentNullException.ThrowIfNull(reachable);

        if (_chunks.Count > 0)
        {
            List<ChunkPos>? stale = null;
            foreach (ChunkPos chunk in _chunks.Keys)
            {
                if (!reachable.Contains(chunk))
                {
                    (stale ??= []).Add(chunk);
                }
            }

            if (stale is not null)
            {
                foreach (ChunkPos chunk in stale)
                {
                    Forget(chunk);
                }
            }
        }

        foreach (ChunkPos chunk in reachable)
        {
            if (!_chunks.ContainsKey(chunk))
            {
                Build(chunk);
            }

        }
    }

    /// <summary>Rebuilds one voxel's bit from the source, on a fresh array.</summary>
    /// <remarks>
    /// The hook the automaton already takes — <c>onVoxelChanged</c> beside
    /// <see cref="FluidSimulation.Disturb"/>. A chunk with no field needs nothing done: it will be
    /// built from the edited world when it next becomes reachable.
    /// </remarks>
    public void Invalidate(VoxelPos voxel)
    {
        ChunkPos chunk = voxel.Chunk;
        if (!_chunks.TryGetValue(chunk, out byte[]? bits))
        {
            return;
        }

        // A copy rather than a rebuild: 4 KiB against 32,768 questions to the world, and a copy is
        // what a reader holding the old array needs anyway.
        var next = (byte[])bits.Clone();
        Set(next, voxel.LocalIndex, _source.CanHold(voxel));
        _chunks[chunk] = next;
        if (_cacheKey == chunk)
        {
            _cache = next;
        }

        Invalidations++;
    }

    /// <summary>Drops one chunk's field, whatever the reachable set says. For an unload.</summary>
    public void Forget(ChunkPos chunk)
    {
        if (!_chunks.Remove(chunk))
        {
            return;
        }

        if (_cacheKey == chunk)
        {
            _cache = null;
        }

        Forgets++;
    }

    /// <summary>Drops everything. For a world closing.</summary>
    public void Clear()
    {
        _chunks.Clear();
        _cache = null;
    }

    /// <summary>
    /// Bakes one chunk, or declines to when the world cannot answer for it yet.
    /// </summary>
    /// <remarks>
    /// <b>Declining is the whole of a defect this shipped with on 2026-09-10.</b>
    /// <c>VoxelFluidTerrain.CanHold</c> answers <i>false</i> for a chunk that is not loaded — right
    /// for flow, since water must not enter a chunk that does not exist — so baking one produces a
    /// field of solid rock, and <b>nothing ever corrects it, because only an edit invalidates</b>.
    /// In play the portal drain found such a chunk and threw at every boundary: the settle ran 295
    /// times, the tick ran twice, and the water stopped. An unbakeable chunk is left
    /// <see cref="FluidClass.Unknown"/> so it falls through to the source and is baked at a later
    /// boundary, when the world can answer.
    /// </remarks>
    private bool Build(ChunkPos chunk)
    {
        VoxelPos origin = chunk.OriginVoxel;
        if (!_source.IsLoaded(origin))
        {
            Declined++;
            return false;
        }

        var bits = new byte[BytesPerChunk];

        // Z, X, Y to match the storage order the index is packed in, so the writes run forward.
        for (int lz = 0; lz < VoxelConstants.ChunkSize; lz++)
        {
            for (int lx = 0; lx < VoxelConstants.ChunkSize; lx++)
            {
                for (int ly = 0; ly < VoxelConstants.ChunkSize; ly++)
                {
                    if (_source.CanHold(new VoxelPos(origin.X + lx, origin.Y + ly, origin.Z + lz)))
                    {
                        int index = VoxelPos.Index(lx, ly, lz);
                        bits[index >> 3] |= (byte)(1 << (index & 7));
                    }
                }
            }
        }

        _chunks[chunk] = bits;
        Builds++;
        return true;
    }

    private static void Set(byte[] bits, int index, bool holdable)
    {
        if (holdable)
        {
            bits[index >> 3] |= (byte)(1 << (index & 7));
        }
        else
        {
            bits[index >> 3] &= (byte)~(1 << (index & 7));
        }
    }

    private byte[]? Bits(ChunkPos chunk)
    {
        if (_cache is not null && _cacheKey == chunk)
        {
            return _cache;
        }

        _chunks.TryGetValue(chunk, out byte[]? bits);
        _cacheKey = chunk;
        _cache = bits;
        return bits;
    }

    /// <summary>
    /// The field as it stands, frozen: what a tick on a worker reads (slice 3b). Call at a tick
    /// boundary, after <see cref="Sync"/>.
    /// </summary>
    /// <remarks>
    /// A copy of the dictionary's references — hundreds of entries, microseconds — over arrays
    /// that are never mutated in place, so the view is never torn by an <see cref="Invalidate"/>
    /// landing mid-tick: the live field gets a new array and the view keeps the old one, which is
    /// decision 7 in the storage. The view answers for nothing outside itself.
    /// </remarks>
    public FluidFieldView Capture() => new(new Dictionary<ChunkPos, byte[]>(_chunks));
}

/// <summary>
/// An immutable snapshot of a <see cref="FluidField"/>, safe to read from a worker while the live
/// field is edited and synced on the sim thread.
/// </summary>
/// <remarks>
/// <b><see cref="FluidClass.Unknown"/> is a refusal here, not a fall-through.</b> The live field
/// asks the streamer about a chunk it has no bits for; a worker cannot, so the view answers
/// <i>cannot hold, not loaded</i> and counts it. That is right by construction and not only safe:
/// a chunk with no field at the boundary was one the world could not answer for then —
/// <see cref="FluidField.Build"/> declines an unloaded chunk — so a cell in it pauses exactly as
/// M8b-4a's rule wants and the arrival wakes it. Slice 2's <c>TickMisses</c> was the measurement
/// that this costs nothing: zero on a 40 s flood. <see cref="Refusals"/> is the same count on this
/// arm, and the one that must stay at zero.
/// </remarks>
public sealed class FluidFieldView : IFluidTerrain
{
    private readonly Dictionary<ChunkPos, byte[]> _chunks;
    private ChunkPos _cacheKey;
    private byte[]? _cache;

    internal FluidFieldView(Dictionary<ChunkPos, byte[]> chunks)
    {
        _chunks = chunks;
    }

    /// <summary>Chunks the view covers.</summary>
    public int Chunks => _chunks.Count;

    /// <summary>Questions asked of a chunk the view does not cover, answered as a refusal.</summary>
    /// <remarks>Written by the worker that reads the view and read at collection; nothing else touches it.</remarks>
    public long Refusals { get; private set; }

    /// <summary>The chunks refused, so the collection can ask the live terrain which of them had arrived meanwhile.</summary>
    /// <remarks>
    /// A refusal for a chunk that is <i>not loaded</i> is the answer the live terrain gives too, and
    /// costs nothing; a refusal for a chunk that arrived mid-tick is a cell paused one boundary
    /// longer than the sync arm would have — decision 7's cost, and the one worth counting apart.
    /// </remarks>
    public HashSet<ChunkPos> RefusedChunks { get; } = [];

    /// <summary>True when the view covers the chunk — the captured set, which is what <c>IsLoaded</c> means on this arm.</summary>
    public bool Covers(ChunkPos chunk) => _chunks.ContainsKey(chunk);

    /// <inheritdoc />
    public bool CanHold(VoxelPos voxel)
    {
        byte[]? bits = Bits(voxel.Chunk);
        if (bits is null)
        {
            Refusals++;
            RefusedChunks.Add(voxel.Chunk);
            return false;
        }

        int index = voxel.LocalIndex;
        return (bits[index >> 3] & (1 << (index & 7))) != 0;
    }

    /// <inheritdoc />
    /// <remarks>The captured set: covered is loaded, which the planning pass named as the whole meaning of the flag on a worker.</remarks>
    public bool IsLoaded(VoxelPos voxel) => _chunks.ContainsKey(voxel.Chunk);

    private byte[]? Bits(ChunkPos chunk)
    {
        if (_cache is not null && _cacheKey == chunk)
        {
            return _cache;
        }

        _chunks.TryGetValue(chunk, out byte[]? bits);
        _cacheKey = chunk;
        _cache = bits;
        return bits;
    }
}
