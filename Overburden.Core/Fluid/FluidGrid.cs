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
    }

    private readonly Dictionary<ChunkPos, Panel> _panels = [];

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
        }
        else if (previous != 0 && amount == 0)
        {
            panel.Held--;
            _held--;
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
