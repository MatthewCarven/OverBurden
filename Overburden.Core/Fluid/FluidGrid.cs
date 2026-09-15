using Overburden.Core.Voxels;

namespace Overburden.Core.Fluid;

/// <summary>
/// Where Tier B water is: millilitres per voxel, stored sparsely as one panel per chunk.
/// </summary>
/// <remarks>
/// <para>
/// <b>Conservation is by construction, not by assertion.</b> Every millilitre that moves does so
/// through <see cref="Transfer"/>, which subtracts and adds the same number in one call, and the
/// running total is adjusted by the delta of a single private writer. There is no code path that
/// can set a cell to an arbitrary value, so a total that drifts would need the writer itself to be
/// wrong rather than any of its callers — which is the difference between a property the tests
/// check and a property the type has. <see cref="Fill"/> and <see cref="Drain"/> are the only ways
/// water enters or leaves, and both report what they actually did.
/// </para>
/// <para>
/// <b>One panel per chunk rather than a dictionary of cells, mirroring <c>ChunkLight</c>.</b> A
/// panel is 32³ <see cref="ushort"/> — 64 KiB — allocated when the first drop enters a chunk and
/// released the moment the last one leaves, so an unflooded world costs nothing at all. The tick
/// asks about six neighbours per cell and five of them are usually in the same chunk, which a
/// one-entry cache turns into an array index; a <c>Dictionary&lt;VoxelPos, int&gt;</c> would have
/// hashed all six and would have measured its own hash function rather than the automaton.
/// </para>
/// <para>
/// <b>The named lever, deliberately not built: panels are dense and a flooded tunnel network is
/// not.</b> A chunk holding one cell of water costs the same 64 KiB as a chunk holding 32,768.
/// <see cref="PanelCount"/> is printed beside the held-cell count so the sparsity is visible in
/// the report rather than in somebody's head.
/// <para>
/// <b>Measured at M8a-2, and the trigger this comment first carried — "panels averaging under ~1%
/// full" — fired on the very first run.</b> A 96 m mine flooding through a 2.25 m breach held
/// 17,738 cells across 68 panels: <b>0.80% occupancy</b>, and the smaller arms were 0.37%. That is
/// not a surprise and it is not a defect — a tunnel grid is a sparse skeleton cut through solid
/// rock, which is the geometry a dense panel is worst at. <b>What it cost was 4.2 MiB</b>, which
/// is a twentieth of one drawn chunk's mesh budget at r12, so the occupancy trigger turned out to
/// be measuring the wrong quantity. <b>Restated: revisit when <see cref="PanelBytes"/> passes
/// ~64 MiB in a played session</b>, which is an absolute cost rather than a ratio that a sparse
/// world will always fail.
/// </para>
/// </para>
/// </remarks>
public sealed class FluidGrid
{
    private sealed class Panel
    {
        public readonly ushort[] Cells = new ushort[VoxelConstants.ChunkVolume];

        /// <summary>Millilitres in this panel, so an empty one can be released without a scan.</summary>
        public long Total;

        /// <summary>Cells holding anything at all, which is what decides when to release it.</summary>
        public int Held;

        /// <summary>Written since the last snapshot. Set by the one writer, cleared by <see cref="TakeSnapshot"/>.</summary>
        public bool Dirty;

        /// <summary>Snapshotted at least once, so a row on disk may exist for it and must be emptied if this panel is.</summary>
        public bool Stored;

        /// <summary>
        /// One bit per cell, set while the cell holds anything — the fill's wet answer (M9d,
        /// decision 4). Maintained by <see cref="Write"/> on a zero crossing, so it costs a
        /// branch the visit already takes and never a scan.
        /// </summary>
        public readonly ulong[] Wet = new ulong[WetWords];

        /// <summary>A bit flipped since <see cref="CollectWet"/> last copied this panel out.</summary>
        public bool WetChanged;
    }

    /// <summary>Words in a panel's wet bitset: 32,768 cells over 64-bit words.</summary>
    public const int WetWords = VoxelConstants.ChunkVolume / 64;

    private readonly Dictionary<ChunkPos, Panel> _panels = [];

    // Panels released since the last CollectWet: their published copy, if any, is to be dropped.
    // A chunk that was released and re-obtained in between is in both lists and the copy wins.
    private readonly List<ChunkPos> _wetReleased = [];

    // Chunks whose panel was released after being snapshotted: the row on disk still holds water
    // that has since drained away, and the next save writes it empty. A panel released before it
    // was ever snapshotted has no row and is not remembered.
    private readonly HashSet<ChunkPos> _emptied = [];

    // A one-entry cache, because the tick walks six neighbours of one cell and five of them share
    // its chunk. Invalidated by hand wherever a panel is released — a stale entry here would hand
    // out an array that is no longer in the dictionary, and writes to it would vanish silently.
    private ChunkPos _cachedKey;
    private Panel? _cachedPanel;
    private bool _cacheValid;

    private long _total;
    private int _held;

    /// <summary>Millilitres held everywhere in this grid.</summary>
    public long TotalMillilitres => _total;

    /// <summary>Cells holding anything at all. Not the active set — settled water is still held.</summary>
    public int HeldCellCount => _held;

    /// <summary>Chunks with a panel allocated. The denominator for how sparse the flood is.</summary>
    public int PanelCount => _panels.Count;

    /// <summary>Bytes the panels occupy, which is the whole of this structure's footprint.</summary>
    public long PanelBytes => (long)_panels.Count * VoxelConstants.ChunkVolume * sizeof(ushort);

    /// <summary>Millilitres in one voxel.</summary>
    public int AmountAt(VoxelPos voxel)
    {
        Panel? panel = Find(voxel.Chunk);
        return panel is null ? 0 : panel.Cells[voxel.LocalIndex];
    }

    /// <summary>Millilitres this voxel could still take.</summary>
    public int FreeAt(VoxelPos voxel) => FluidConstants.CellCapacityMillilitres - AmountAt(voxel);

    /// <summary>Whether the voxel holds anything, off the panel's bitset — the live answer, for tests and the bench.</summary>
    public bool IsWet(VoxelPos voxel)
    {
        Panel? panel = Find(voxel.Chunk);
        if (panel is null)
        {
            return false;
        }

        int index = voxel.LocalIndex;
        return (panel.Wet[index >> 6] & (1UL << (index & 63))) != 0;
    }

    /// <summary>
    /// Copies every panel's wet bitset that changed since the last call into
    /// <paramref name="published"/>, drops the copies of panels released since, and returns
    /// how many were copied. The sim thread's answer to <see cref="IsWet"/> while a worker owns
    /// the grid (M9d, decision 4): one path, always the copy, stale by at most one tick.
    /// </summary>
    /// <remarks>
    /// A copy is 4 KiB and only a touched panel pays it — 44 panels on the played world's flood
    /// is 176 KiB a boundary. The arrays are reused across calls, so a steady flood allocates
    /// nothing here.
    /// </remarks>
    public int CollectWet(Dictionary<ChunkPos, ulong[]> published)
    {
        ArgumentNullException.ThrowIfNull(published);

        foreach (ChunkPos chunk in _wetReleased)
        {
            if (!_panels.ContainsKey(chunk))
            {
                published.Remove(chunk);
            }
        }

        _wetReleased.Clear();

        int copied = 0;
        foreach ((ChunkPos chunk, Panel panel) in _panels)
        {
            if (!panel.WetChanged)
            {
                continue;
            }

            if (!published.TryGetValue(chunk, out ulong[]? copy))
            {
                copy = new ulong[WetWords];
                published[chunk] = copy;
            }

            Array.Copy(panel.Wet, copy, WetWords);
            panel.WetChanged = false;
            copied++;
        }

        return copied;
    }

    /// <summary>The wet bit for a voxel in a published copy, or false with no copy.</summary>
    public static bool IsWetIn(Dictionary<ChunkPos, ulong[]> published, VoxelPos voxel)
    {
        ArgumentNullException.ThrowIfNull(published);
        if (!published.TryGetValue(voxel.Chunk, out ulong[]? copy))
        {
            return false;
        }

        int index = voxel.LocalIndex;
        return (copy[index >> 6] & (1UL << (index & 63))) != 0;
    }

    /// <summary>
    /// Puts water in. Returns what would not fit, which the caller must account for rather than
    /// drop — this is where the milestone's oracle is either kept or quietly broken.
    /// </summary>
    public long Fill(VoxelPos voxel, long millilitres)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(millilitres);
        if (millilitres == 0)
        {
            return 0;
        }

        Panel panel = Obtain(voxel.Chunk);
        int index = voxel.LocalIndex;
        int room = FluidConstants.CellCapacityMillilitres - panel.Cells[index];
        int take = (int)Math.Min(room, millilitres);
        if (take > 0)
        {
            Write(voxel.Chunk, panel, index, panel.Cells[index] + take);
        }
        else
        {
            // Obtain may have allocated a panel for a cell that turned out to be full in a chunk
            // that was otherwise empty. Releasing here keeps PanelCount honest as a sparsity
            // measure; leaving it would make an empty panel immortal.
            Release(voxel.Chunk, panel);
        }

        return millilitres - take;
    }

    /// <summary>Takes water out. Returns what was actually removed, which may be less than asked.</summary>
    public long Drain(VoxelPos voxel, long millilitres)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(millilitres);

        Panel? panel = Find(voxel.Chunk);
        if (panel is null || millilitres == 0)
        {
            return 0;
        }

        int index = voxel.LocalIndex;
        int take = (int)Math.Min(panel.Cells[index], millilitres);
        if (take > 0)
        {
            Write(voxel.Chunk, panel, index, panel.Cells[index] - take);
        }

        return take;
    }

    /// <summary>
    /// Moves up to <paramref name="requested"/> millilitres between two voxels, clamped by what
    /// the source has and what the destination can take. Returns what moved.
    /// </summary>
    /// <remarks>
    /// <b>The only mutator the automaton is allowed to call</b>, and the reason its volume
    /// conservation needs no arithmetic anywhere else. The clamps are here rather than in the tick
    /// so that a rule which asks for too much is merely wrong about the trajectory, never wrong
    /// about the total.
    /// </remarks>
    public int Transfer(VoxelPos from, VoxelPos to, int requested)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(requested);
        if (requested == 0 || from == to)
        {
            return 0;
        }

        Panel? source = Find(from.Chunk);
        if (source is null)
        {
            return 0;
        }

        int fromIndex = from.LocalIndex;
        int available = source.Cells[fromIndex];
        if (available == 0)
        {
            return 0;
        }

        Panel destination = Obtain(to.Chunk);
        int toIndex = to.LocalIndex;
        int room = FluidConstants.CellCapacityMillilitres - destination.Cells[toIndex];

        int moved = Math.Min(requested, Math.Min(available, room));
        if (moved == 0)
        {
            Release(to.Chunk, destination);
            return 0;
        }

        // Obtain may have replaced the panel dictionary's entry for from.Chunk in the cache, and
        // it may even be the same panel when the two voxels share a chunk. Re-find rather than
        // reuse, because writing through a stale reference is the one way this could lose water.
        Write(to.Chunk, destination, toIndex, destination.Cells[toIndex] + moved);
        Panel again = Find(from.Chunk)!;
        Write(from.Chunk, again, fromIndex, again.Cells[fromIndex] - moved);
        return moved;
    }

    /// <summary>Every voxel holding anything, in no particular order.</summary>
    /// <remarks>
    /// <b>Scans every allocated panel — 32,768 cells each — so it is for verification and reports,
    /// never for the tick.</b> The tick iterates the active set and nothing else, which is the
    /// whole design.
    /// </remarks>
    public IEnumerable<VoxelPos> HeldCells()
    {
        foreach ((ChunkPos chunk, Panel panel) in _panels)
        {
            if (panel.Held == 0)
            {
                continue;
            }

            VoxelPos origin = chunk.OriginVoxel;
            for (int index = 0; index < panel.Cells.Length; index++)
            {
                if (panel.Cells[index] == 0)
                {
                    continue;
                }

                (int lx, int ly, int lz) = VoxelPos.FromIndex(index);
                yield return new VoxelPos(origin.X + lx, origin.Y + ly, origin.Z + lz);
            }
        }
    }

    /// <summary>
    /// Every held cell with nothing directly above it, and how much it holds: the water's top
    /// surface, which is what a renderer draws and what a sweep of the wet volume is for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One array read per cell, because Y is contiguous.</b> A chunk stores <c>[z][x][y]</c>, so
    /// the cell above is the <i>next index</i> — the column is walked downward carrying the value
    /// above it, and only the top layer of a panel asks the grid at all. The caller's obvious
    /// version, <c>HeldCells()</c> filtered by <c>AmountAt(cell + Y)</c>, pays a panel lookup per
    /// held cell instead: <b>measured at 25.40 ms of a 28.38 ms surface rebuild</b> on a
    /// 259,178-cell flood in the Debug build the game is played in, which was 89% of the cost and
    /// the reason this exists.
    /// </para>
    /// <para>
    /// <b>Not "the highest cell in each column".</b> A column can hold two bodies of water with air
    /// between them — a flooded gallery under a pond — and both have a surface. The test is per
    /// cell for that reason, and a fixture with one body per column cannot tell the two rules apart.
    /// </para>
    /// </remarks>
    public IEnumerable<(VoxelPos Cell, int Millilitres)> SurfaceCells()
    {
        foreach ((ChunkPos chunk, Panel panel) in _panels)
        {
            if (panel.Held == 0)
            {
                continue;
            }

            VoxelPos origin = chunk.OriginVoxel;
            ushort[] cells = panel.Cells;

            for (int lz = 0; lz < VoxelConstants.ChunkSize; lz++)
            {
                for (int lx = 0; lx < VoxelConstants.ChunkSize; lx++)
                {
                    int column = VoxelPos.Index(lx, 0, lz);

                    // The one cell whose neighbour above is in another panel. Asked once per
                    // column rather than once per cell, and through the grid because the panel
                    // above may not exist.
                    int above = AmountAt(new VoxelPos(
                        origin.X + lx, origin.Y + VoxelConstants.ChunkSize, origin.Z + lz));

                    for (int ly = VoxelConstants.ChunkSize - 1; ly >= 0; ly--)
                    {
                        int amount = cells[column + ly];
                        if (amount > 0 && above == 0)
                        {
                            yield return (
                                new VoxelPos(origin.X + lx, origin.Y + ly, origin.Z + lz), amount);
                        }

                        above = amount;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Every voxel holding anything in one face layer of a chunk: the 32 × 32 cells whose local
    /// coordinate on the axis of <paramref name="face"/> is 0 on the negative side and 31 on the
    /// positive. Exactly one component of <paramref name="face"/> is non-zero.
    /// </summary>
    /// <remarks>
    /// <b>For the arrival wake (M8b-4a), and bounded by a face rather than a chunk for the reason
    /// <c>Disturb</c> wakes only cells that hold water.</b> A chunk arriving beside a lake has
    /// at most six layers of 1,024 cells that could have gained a move while it was away, and a
    /// chunk with no panel costs one dictionary lookup — waking every cell of the chunk would be
    /// the 32,768-visit trap a chunk bite already met once. A layer is read with a fixed local
    /// coordinate and two loops, so the cost is the scan and not a <see cref="HeldCells"/> over
    /// the whole panel.
    /// </remarks>
    public IEnumerable<VoxelPos> HeldCellsOnFace(ChunkPos chunk, (int X, int Y, int Z) face)
    {
        int axes = Math.Abs(face.X) + Math.Abs(face.Y) + Math.Abs(face.Z);
        if (axes != 1)
        {
            throw new ArgumentOutOfRangeException(nameof(face), face, "a face is one unit along one axis");
        }

        Panel? panel = Find(chunk);
        if (panel is null || panel.Held == 0)
        {
            yield break;
        }

        VoxelPos origin = chunk.OriginVoxel;
        int fixedAt = face.X + face.Y + face.Z > 0 ? VoxelConstants.ChunkMask : 0;
        for (int a = 0; a < VoxelConstants.ChunkSize; a++)
        {
            for (int b = 0; b < VoxelConstants.ChunkSize; b++)
            {
                // Which two axes vary depends on which one the face fixes.
                (int lx, int ly, int lz) = face.X != 0 ? (fixedAt, a, b)
                    : face.Y != 0 ? (a, fixedAt, b)
                    : (a, b, fixedAt);
                if (panel.Cells[VoxelPos.Index(lx, ly, lz)] == 0)
                {
                    continue;
                }

                yield return new VoxelPos(origin.X + lx, origin.Y + ly, origin.Z + lz);
            }
        }
    }

    // ---------------------------------------------------------------------------------------
    // Persistence (M8b-4b). A panel is dirty from its first write until it is snapshotted, and a
    // snapshot is a copy of the held cells the save path owns from then on.
    // ---------------------------------------------------------------------------------------

    /// <summary>Whether a save would write this chunk: its panel was written since the last snapshot, or released since.</summary>
    public bool IsDirty(ChunkPos chunk) =>
        (_panels.TryGetValue(chunk, out Panel? panel) && panel.Dirty) || _emptied.Contains(chunk);

    /// <summary>Every chunk <see cref="IsDirty"/> is true for. A copy, so the caller may snapshot while iterating.</summary>
    public List<ChunkPos> DirtyChunks()
    {
        var dirty = new List<ChunkPos>();
        foreach ((ChunkPos chunk, Panel panel) in _panels)
        {
            if (panel.Dirty) dirty.Add(chunk);
        }

        foreach (ChunkPos chunk in _emptied)
        {
            if (!_panels.ContainsKey(chunk)) dirty.Add(chunk);
        }

        return dirty;
    }

    /// <summary>
    /// Copies the held cells of one chunk as ascending (index, millilitres) pairs and marks the
    /// panel clean. An empty array for a chunk with no panel, which is what a released one writes.
    /// </summary>
    /// <remarks>
    /// <b>Clears the dirty bit here, at the copy, and not when the bytes land</b> — a write
    /// arriving while the save is in flight sets it again and the panel is written a second
    /// time; clearing on completion would swallow exactly those writes. The same rule as the
    /// pipeline's <c>DirtySinceSave</c>. The copy is one scan of the panel, so it costs the same
    /// for a chunk holding one cell as for a full one; what varies is the array.
    /// </remarks>
    public (ushort Index, ushort Millilitres)[] TakeSnapshot(ChunkPos chunk)
    {
        _emptied.Remove(chunk);

        Panel? panel = Find(chunk);
        if (panel is null || panel.Held == 0)
        {
            return [];
        }

        var cells = new (ushort, ushort)[panel.Held];
        int at = 0;
        for (int index = 0; index < panel.Cells.Length && at < cells.Length; index++)
        {
            ushort amount = panel.Cells[index];
            if (amount != 0)
            {
                cells[at++] = ((ushort)index, amount);
            }
        }

        panel.Dirty = false;
        panel.Stored = true;
        return cells;
    }

    /// <summary>
    /// Puts a stored panel back, whole. The chunk must hold nothing yet; returns the millilitres restored.
    /// </summary>
    /// <remarks>
    /// <b>Not a write, by the dirty bit's rule</b>: what comes off the disk is what is on the disk,
    /// so the restored panel is clean and marked stored, and a restore that changed nothing costs
    /// no row at the next save. Every cell goes through <see cref="Write"/> so the totals are the
    /// writer's; only the flags are set by hand afterwards. It refuses a chunk that already holds
    /// water rather than adding to it, because the only caller is the open and the grid is empty
    /// then — a second restore over the first would double the water and pass the identity, since
    /// <c>loaded</c> would double with it.
    /// </remarks>
    public long Restore(ChunkPos chunk, ReadOnlySpan<(ushort Index, ushort Millilitres)> cells)
    {
        if (Find(chunk) is not null)
        {
            throw new InvalidOperationException($"{chunk} already holds water; a panel is restored once, into an empty grid.");
        }

        if (cells.Length == 0)
        {
            return 0;
        }

        Panel panel = Obtain(chunk);
        long restored = 0;
        foreach ((ushort index, ushort millilitres) in cells)
        {
            if (index >= panel.Cells.Length || millilitres == 0 || millilitres > FluidConstants.CellCapacityMillilitres)
            {
                throw new ArgumentOutOfRangeException(nameof(cells), $"cell {index} holds {millilitres} mL");
            }

            Write(chunk, panel, index, millilitres);
            restored += millilitres;
        }

        panel.Dirty = false;
        panel.Stored = true;
        return restored;
    }

    /// <summary>Whether this chunk has a panel allocated — one held cell or more.</summary>
    public bool HasPanel(ChunkPos chunk) => _panels.ContainsKey(chunk);

    /// <summary>
    /// Cells holding anything in this chunk — the panel's own count, maintained on every zero
    /// crossing, so a pump can ask "is there any water in my box" with one lookup a chunk
    /// before it reads a cell (M10a-3, decision 7). Zero for a chunk with no panel.
    /// </summary>
    public int HeldIn(ChunkPos chunk) => Find(chunk)?.Held ?? 0;

    /// <summary>Every chunk holding a panel. O(panels), not O(cells).</summary>
    /// <remarks>
    /// The seed of the reachable set the field is built over: awake cells either hold water, and so
    /// live in a panel, or are empty cells a wet neighbour woke, and so live within one voxel of
    /// one. Derived from the panels rather than from the active set because a flood's active set is
    /// hundreds of thousands of cells and its panels are dozens.
    /// </remarks>
    public IEnumerable<ChunkPos> PanelChunks() => _panels.Keys;

    /// <summary>Every panel holding anything, with its cells, for the surface mesher's scans. Read only.</summary>
    internal IEnumerable<(ChunkPos Chunk, ushort[] Cells)> HeldPanels()
    {
        foreach ((ChunkPos chunk, Panel panel) in _panels)
        {
            if (panel.Held > 0)
            {
                yield return (chunk, panel.Cells);
            }
        }
    }

    /// <summary>A panel's cells, or null when the chunk has none or holds nothing. Read only.</summary>
    internal ushort[]? PanelCells(ChunkPos chunk)
    {
        Panel? panel = Find(chunk);
        return panel is null || panel.Held == 0 ? null : panel.Cells;
    }

    /// <summary>
    /// Drops a chunk's panel without touching the row on disk. Returns the millilitres that left
    /// RAM with it, which the caller counts as evicted.
    /// </summary>
    /// <remarks>
    /// <b>The third way a panel goes, and the one that does not mark the row stale</b> (M8b-4c).
    /// A drained panel goes through <see cref="Release"/> and is remembered so its row is written
    /// empty; this is an unload, not a drain — the water is on disk and the row is the truth —
    /// so it must not enter <c>_emptied</c>, or the next save would write the lake's row empty
    /// with the identity exact. The caller snapshots first if the row is stale; this only takes
    /// the bytes away. The dirty bit goes with the panel: whatever it said is either on the
    /// snapshot just taken or was already on disk.
    /// </remarks>
    public long Evict(ChunkPos chunk)
    {
        if (!_panels.Remove(chunk, out Panel? panel))
        {
            return 0;
        }

        long released = panel.Total;
        _total -= panel.Total;
        _held -= panel.Held;
        _wetReleased.Add(chunk);

        if (_cacheValid && _cachedKey == chunk)
        {
            _cacheValid = false;
            _cachedPanel = null;
        }

        return released;
    }

    private void Write(ChunkPos chunk, Panel panel, int index, int amount)
    {
        int previous = panel.Cells[index];
        panel.Cells[index] = (ushort)amount;
        panel.Dirty = true;

        int delta = amount - previous;
        panel.Total += delta;
        _total += delta;

        if (previous == 0 && amount != 0)
        {
            panel.Held++;
            _held++;
            panel.Wet[index >> 6] |= 1UL << (index & 63);
            panel.WetChanged = true;
        }
        else if (previous != 0 && amount == 0)
        {
            panel.Held--;
            _held--;
            panel.Wet[index >> 6] &= ~(1UL << (index & 63));
            panel.WetChanged = true;
        }

        if (panel.Held == 0)
        {
            Release(chunk, panel);
        }
    }

    private Panel? Find(ChunkPos chunk)
    {
        if (_cacheValid && _cachedKey == chunk)
        {
            return _cachedPanel;
        }

        _panels.TryGetValue(chunk, out Panel? panel);
        _cachedKey = chunk;
        _cachedPanel = panel;
        _cacheValid = true;
        return panel;
    }

    private Panel Obtain(ChunkPos chunk)
    {
        Panel? panel = Find(chunk);
        if (panel is not null)
        {
            return panel;
        }

        panel = new Panel();
        _panels[chunk] = panel;
        _cachedKey = chunk;
        _cachedPanel = panel;
        _cacheValid = true;
        return panel;
    }

    private void Release(ChunkPos chunk, Panel panel)
    {
        if (panel.Total != 0 || panel.Held != 0)
        {
            return;
        }

        _panels.Remove(chunk);
        _wetReleased.Add(chunk);
        if (panel.Stored)
        {
            _emptied.Add(chunk);
        }

        if (_cacheValid && _cachedKey == chunk)
        {
            _cacheValid = false;
            _cachedPanel = null;
        }
    }
}
