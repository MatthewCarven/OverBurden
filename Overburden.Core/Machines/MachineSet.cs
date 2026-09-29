using Overburden.Core.Building;
using Overburden.Core.Content;
using Overburden.Core.Inventory;
using Overburden.Core.Persistence;
using Overburden.Core.Voxels;

namespace Overburden.Core.Machines;

/// <summary>
/// Every machine row in the world, in memory, whether or not its chunk is loaded — and the
/// two things that keep the rows and the bodies agreeing: the reconcile at a chunk's arrival and
/// the dismantle after a body is damaged.
/// </summary>
/// <remarks>
/// <para>
/// <b>Read whole at open and written whole at every session save</b> (M10-PLAN decision 4): rows
/// are hundreds, not millions, every one of them ticks every boundary, and the disk is a mirror
/// of this set rather than the other way round — the basins' arrangement since M7b-2, for the
/// same reason. The set is keyed by brush cell, because a body is exactly one cell and a cell
/// never straddles a chunk face, and indexed by chunk, because the pipeline's hooks and the
/// reconcile ask "what is in this chunk" and must answer in one lookup 32,768 times a bite.
/// </para>
/// <para>
/// <b>The hooks queue and the frame writes</b> (M10a's pass, decision 3). <see cref="ChunkArrived"/>
/// and <see cref="OnVoxelChanged"/> run inside the pipeline's <c>Complete</c> — before the
/// neighbour push, mid-tick under <c>--fluidjob=1</c>, 32,768 times a bite — and do nothing but
/// add a chunk to a list or bump a counter. <see cref="Drain"/> runs once a frame from the sim
/// thread, where the brush writes, and does the reconcile and the dismantle through
/// <c>SetVoxel</c>. Nothing here ever writes from a hook.
/// </para>
/// <para>
/// <b>The credit rule is <c>Lost == Carved</c></b> (decision 4). A fall and a swing both write air
/// into a body and the hook cannot tell them apart; what differs is that <c>Carve.Take</c> credits
/// the inventory per voxel it removes and says so through <see cref="IMachineBodies"/>, and the
/// fall credits nothing. So a row damaged only by picks is dismantled with its remainder credited,
/// and a row that lost a voxel to anything else is destroyed without — whether it fell whole or
/// half, however far the rubble went, and the voxels the player mined off it first are theirs.
/// </para>
/// <para>
/// <b>Only a pick's dismantle is the player's edit; everything else the drain writes is the
/// world's</b> (IM-0b, 2026-09-29). The dismantle lands in the player's undo step with the swing
/// that caused it, so Ctrl+Z puts the machine back whole. The reconcile and the destroy after a
/// fall run with the journal off: journalled, each was a mint through Ctrl+Z. An undone destroy
/// re-added the row over the air the fall left, and the chunk's next arrival rebuilt the body from
/// it; an undone rebuild credited its 64 voxels and left the row to be rebuilt again; an undone
/// return gave the row back to be returned again.
/// </para>
/// </remarks>
public sealed partial class MachineSet : IMachineBodies
{
    private readonly Dictionary<BrushCell, MachineRow> _rows = [];
    private readonly Dictionary<ChunkPos, List<MachineRow>> _byChunk = [];
    private readonly MaterialTable? _materials;

    private readonly List<ChunkPos> _arrivals = [];
    private readonly HashSet<ChunkPos> _queued = [];
    private readonly List<MachineRow> _damaged = [];
    private readonly HashSet<MachineRow> _damagedSet = [];

    /// <summary>A set with no material table: rows only, no palette check at arrival. The store slice's shape.</summary>
    public MachineSet()
    {
    }

    /// <summary>A set that knows which materials are machines, so an arrival can be skipped in O(palette).</summary>
    public MachineSet(MaterialTable materials)
    {
        ArgumentNullException.ThrowIfNull(materials);
        _materials = materials;
    }

    /// <summary>How many rows the set holds.</summary>
    public int Count => _rows.Count;

    /// <summary>Every row, in no particular order. The tick sorts what it needs sorted.</summary>
    public IReadOnlyCollection<MachineRow> Rows => _rows.Values;

    /// <summary>Bumped on every add and remove, so a caller can cache a derived order and know when it went stale.</summary>
    public long Version { get; private set; }

    /// <summary>Chunks queued for the next drain and rows marked damaged — the frame's work.</summary>
    public bool HasWorkQueued => _arrivals.Count > 0 || _damaged.Count > 0;

    // ---- lifetime counters, for the M10: line ----

    /// <summary>Bodies placed through <see cref="Building.Place.Machine"/>.</summary>
    public long Placed { get; private set; }

    /// <summary>Bodies rewritten from their row at arrival — a chunk that never landed with its machine.</summary>
    public long Rebuilt { get; private set; }

    /// <summary>Rows dismantled at arrival with credit, because their cell had regenerated as rock.</summary>
    public long ReturnedAtArrival { get; private set; }

    /// <summary>Rows destroyed at arrival without credit, because only part of the body was there.</summary>
    public long DestroyedAtArrival { get; private set; }

    /// <summary>Machine-material voxels with no row over them, written to air without credit.</summary>
    public long OrphanVoxelsCleared { get; private set; }

    /// <summary>Arrivals reconciled.</summary>
    public long ChunksReconciled { get; private set; }

    /// <summary>Arrivals whose palette held no machine material and no row — skipped in O(palette).</summary>
    public long ChunksSkippedByPalette { get; private set; }

    /// <summary>Queued chunks that had left the box before the drain reached them.</summary>
    public long ChunksLeftBeforeDrain { get; private set; }

    /// <summary>Rows dismantled by a pick, their remainder credited.</summary>
    public long Dismantled { get; private set; }

    /// <summary>Voxels the dismantle credited on top of what the pick took.</summary>
    public long DismantledVoxelsCredited { get; private set; }

    /// <summary>Rows destroyed at the drain without credit — something other than a pick took a voxel.</summary>
    public long Destroyed { get; private set; }

    /// <summary>Port writes through <see cref="SetPort"/>, over the lifetime, clamped or not.</summary>
    public long PortWrites { get; private set; }

    /// <summary>Of those, writes whose value was outside the port's bounds and landed clamped.</summary>
    public long PortWritesClamped { get; private set; }

    /// <summary>The row whose body fills <paramref name="cell"/>, if any.</summary>
    public bool TryGet(BrushCell cell, out MachineRow row) => _rows.TryGetValue(cell, out row!);

    /// <summary>The rows whose bodies are in <paramref name="chunk"/>; empty for a chunk with none.</summary>
    public IReadOnlyList<MachineRow> RowsIn(ChunkPos chunk) =>
        _byChunk.TryGetValue(chunk, out List<MachineRow>? rows) ? rows : [];

    /// <summary>Whether any row's body is in <paramref name="chunk"/>. The hooks' first question.</summary>
    public bool HasRowsIn(ChunkPos chunk) => _byChunk.ContainsKey(chunk);

    /// <summary>
    /// Millijoules of charge that entered the set on storage rows and vaults' own charges — added,
    /// placed or restored — over the session. With <see cref="StoredMillijoulesLeaving"/>, the pool
    /// identity's <c>loaded</c> and <c>stowed</c> terms in one pair (M10c).
    /// </summary>
    public long StoredMillijoulesEntering { get; private set; }

    /// <summary>Millijoules of charge that left the set on rows removed — dismantled, destroyed, or replaced at a restore.</summary>
    public long StoredMillijoulesLeaving { get; private set; }

    /// <summary>The charge on a row: a placed cell's, or a vault's own (IM-3b); zero for anything else here.</summary>
    /// <remarks>
    /// Through the table, never by reading the bytes alone: another kind's state could carry a
    /// version byte of 1 and eight bytes after it, and a set built without a table (a test's)
    /// counts no charge at all rather than guessing. <b>A vault's own charge is counted here</b>, so
    /// the pool identity carries it with no term of its own: a bill it pays is <c>held</c>, a
    /// recharge or a return is a transfer inside <c>stored</c>, and a pick, Ctrl+Z and a reopen count
    /// it as they count a cell's (IM-3b's pass, fact 3). A vault whose bytes this build cannot read
    /// counts none, as it counts no contents.
    /// </remarks>
    public long ChargeOf(MachineRow row) => _materials?.Machine(row.MaterialId) switch
    {
        { Kind: MachineKind.Storage, Carried: false } => StorageState.Read(row),
        { Kind: MachineKind.Vault } => VaultState.TryRead(row, out VaultContents contents) ? contents.OwnChargeMillijoules : 0,
        _ => 0,
    };

    /// <summary>
    /// Sets the charge on the storage row at <paramref name="cell"/>, clamped to its capacity,
    /// with the difference counted as charge entering or leaving — the debug seed (<c>--pump</c>'s
    /// cell, <c>Alt+P</c>) and nothing in play, which charges through the tick alone.
    /// </summary>
    /// <returns>The charge that landed, or −1 when the cell holds no placed storage row.</returns>
    public long SetCharge(BrushCell cell, long millijoules)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(millijoules);
        if (!_rows.TryGetValue(cell, out MachineRow? row)
            || _materials?.Machine(row.MaterialId) is not { Kind: MachineKind.Storage, Carried: false } storage)
        {
            return -1;
        }

        long before = StorageState.Read(row);
        long landed = Math.Min(millijoules, storage.CapacityMillijoules);
        StorageState.Write(row, landed);
        if (landed > before) StoredMillijoulesEntering += landed - before;
        else StoredMillijoulesLeaving += before - landed;
        return landed;
    }

    /// <summary>A placed storage row's capacity in millijoules, or zero when its material is not a storage here.</summary>
    public long CapacityOf(MachineRow row) =>
        _materials?.Machine(row.MaterialId) is { Kind: MachineKind.Storage, Carried: false } storage ? storage.CapacityMillijoules : 0;

    /// <summary>
    /// Told about every row added or removed, for the undo (UNDO-PLAN). Null keeps nothing. It must be
    /// the journal the drain's editor writes to as well (Main wires both to one history), because the
    /// drain switches it off around the world's own writes.
    /// </summary>
    public Editing.IEditJournal? Journal { get; set; }

    /// <summary>Adds a row. Refuses a second row at the same cell: one cell, one body.</summary>
    public void Add(MachineRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (!_rows.TryAdd(row.Cell, row))
        {
            throw new InvalidOperationException($"A machine already stands at {row.Cell}.");
        }

        if (!_byChunk.TryGetValue(row.Chunk, out List<MachineRow>? rows))
        {
            rows = [];
            _byChunk[row.Chunk] = rows;
        }

        rows.Add(row);
        StoredMillijoulesEntering += ChargeOf(row);
        VaultVoxelsEntering += KnownVaultVoxels(row);
        Version++;
        Journal?.MachineChanged(row, added: true);
    }

    /// <summary>Removes the row at <paramref name="cell"/>. False when there was none.</summary>
    public bool Remove(BrushCell cell)
    {
        if (!_rows.Remove(cell, out MachineRow? row))
        {
            return false;
        }

        List<MachineRow> rows = _byChunk[row.Chunk];
        rows.Remove(row);
        if (rows.Count == 0)
        {
            _byChunk.Remove(row.Chunk);
        }

        StoredMillijoulesLeaving += ChargeOf(row);
        VaultVoxelsLeaving += KnownVaultVoxels(row);
        Version++;
        Journal?.MachineChanged(row, added: false);
        return true;
    }

    /// <summary>A body was placed and its row created — <see cref="Building.Place.Machine"/>'s call.</summary>
    internal void NotePlaced(MachineRow row)
    {
        Add(row);
        Placed++;
    }

    /// <summary>
    /// Writes a port on the row at <paramref name="cell"/>, clamped to the material's declaration
    /// — the computer's one write (M10b, decision 4), and the <c>--port=</c> seam's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Never throws for a value, because a row is a caller</b> (the ladder's decision 21): the
    /// panel, the flag and a multiplayer server all arrive here, and a number outside its bounds
    /// is clamped and counted rather than refused. A name the declaration does not carry is
    /// refused by name; a row whose material declares no ports — a hole left by a removed mod,
    /// or a set built without a table — refuses every write, because there is nothing to clamp
    /// against and an unbounded write is the thing the restore exists to prevent.
    /// </para>
    /// <para>
    /// <b><see cref="Version"/> moves on every write that changed a value</b>, so the tick rebuilds
    /// its rings at the next boundary: a pump whose outlet just left its channel stalls
    /// <c>no outlet</c> there and not a boundary later. An unchanged write is counted and moves
    /// nothing, so a panel re-sending the value it shows costs no rebuild.
    /// </para>
    /// </remarks>
    public PortWrite SetPort(BrushCell cell, string name, long value)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (!_rows.TryGetValue(cell, out MachineRow? row))
        {
            return new PortWrite(PortWriteResult.NoSuchRow, 0);
        }

        if (_materials?.Machine(row.MaterialId)?.Port(name) is not { } port)
        {
            return new PortWrite(PortWriteResult.NoSuchPort, 0);
        }

        long landed = port.Clamp(value);
        bool clamped = landed != value;
        PortWrites++;
        if (clamped) PortWritesClamped++;

        if (!row.TryGetPort(name, out long before) || before != landed)
        {
            row.SetPortUnchecked(name, landed);
            Version++;
        }

        return new PortWrite(clamped ? PortWriteResult.Clamped : PortWriteResult.Set, landed);
    }

    // ---------------------------------------------------------------------------------------
    // The hooks: queue and mark, never write.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// A chunk's voxels have arrived (all three paths). Queues it for the next drain when the set
    /// has rows in it or its palette might hold a machine material; O(palette), no voxel read.
    /// </summary>
    public void ChunkArrived(ChunkPos chunk, bool hasEdits, VoxelChunk? voxels)
    {
        bool wanted = HasRowsIn(chunk);

        if (!wanted && hasEdits && voxels is not null && _materials is not null)
        {
            // Only an edited chunk can hold a body: worldgen never places a machine material. A
            // regenerated chunk with rows in it is wanted above, for the rebuild.
            foreach (int id in _materials.MachineIds)
            {
                if (voxels.MayContain(id))
                {
                    wanted = true;
                    break;
                }
            }
        }

        if (!wanted)
        {
            if (hasEdits) ChunksSkippedByPalette++;
            return;
        }

        if (_queued.Add(chunk))
        {
            _arrivals.Add(chunk);
        }
    }

    /// <summary>
    /// A voxel was written. If it is inside a row's cell and is no longer the body's material,
    /// the row lost a voxel — two lookups, and nothing else.
    /// </summary>
    public void OnVoxelChanged(VoxelPos voxel, int materialId)
    {
        if (_byChunk.Count == 0 || !_byChunk.ContainsKey(voxel.Chunk))
        {
            return;
        }

        if (!_rows.TryGetValue(BrushCell.FromVoxel(voxel), out MachineRow? row) || materialId == row.MaterialId)
        {
            return;
        }

        // A wreck has no body to lose (IM-3's pass, decision 5): a block placed in its cell, a
        // second fall, or the drain clearing what a first one left is no damage to it.
        if (IsKnownWreck(row))
        {
            return;
        }

        row.Lost++;
        MarkDamaged(row);
    }

    /// <inheritdoc />
    public void Taken(VoxelPos voxel, int materialId)
    {
        if (_byChunk.Count == 0 || !_byChunk.ContainsKey(voxel.Chunk))
        {
            return;
        }

        if (!_rows.TryGetValue(BrushCell.FromVoxel(voxel), out MachineRow? row) || materialId != row.MaterialId)
        {
            return;
        }

        // A vault's contents go to its sisters at the pick's first voxel, the instant Refuses allowed
        // it (IM-3's pass, decision 4). The world's hook has already marked the row damaged by now —
        // the write comes before this call — which is why the move leaves the row out by name and
        // not by the mark; after the first voxel the vault is empty and this does nothing.
        if (IsVault(row))
        {
            MoveAtPick(row);
        }

        row.Carved++;
        MarkDamaged(row);
    }

    private void MarkDamaged(MachineRow row)
    {
        if (_damagedSet.Add(row))
        {
            _damaged.Add(row);
        }
    }

    // ---------------------------------------------------------------------------------------
    // The drain: once a frame, on the sim thread, where the brush writes.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Reconciles every chunk that arrived since the last drain and dismantles every row damaged
    /// since, writing through <paramref name="world"/>. O(1) with nothing queued.
    /// </summary>
    /// <param name="world">The editor the writes go through — the pipeline, in the game.</param>
    /// <param name="inventory">Where a dismantled or returned body's voxels are credited; null credits nothing.</param>
    /// <param name="voxelsOf">
    /// The chunk's voxels now, or null if it has left the box since the hook queued it — a chunk
    /// that left is dropped and queued again by its next arrival.
    /// </param>
    public DrainReport Drain(IVoxelEditor world, VolumetricInventory? inventory, Func<ChunkPos, VoxelChunk?> voxelsOf)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(voxelsOf);

        var report = default(DrainReport);

        if (_arrivals.Count > 0)
        {
            // The reconcile is the world keeping rows and bodies agreeing, never the player's edit.
            bool recording = PauseJournal();
            try
            {
                foreach (ChunkPos chunk in _arrivals)
                {
                    _queued.Remove(chunk);

                    VoxelChunk? voxels = voxelsOf(chunk);
                    if (voxels is null)
                    {
                        ChunksLeftBeforeDrain++;
                        report.Left++;
                        continue;
                    }

                    Reconcile(chunk, voxels, world, inventory, ref report);
                    ChunksReconciled++;
                    report.Reconciled++;
                }
            }
            finally
            {
                ResumeJournal(recording);
            }

            _arrivals.Clear();
        }

        if (_damaged.Count > 0)
        {
            // A snapshot, because a vault kept as a wreck is cleared with its row still standing,
            // and a row whose bytes this build cannot read is marked again by that write (IM-3).
            // Marks the drain's own writes leave are not damage, and go with the snapshot.
            MachineRow[] damaged = [.. _damaged];
            _damaged.Clear();
            _damagedSet.Clear();
            foreach (MachineRow row in damaged)
            {
                Dismantle(row, world, inventory, ref report);
            }

            _damaged.Clear();
            _damagedSet.Clear();
        }

        return report;
    }

    /// <summary>
    /// The rows are the truth and the body follows: whole → nothing; absent → rebuilt into air,
    /// or dismantled with credit when the cell regenerated as rock; partial → destroyed without
    /// credit (only a fall leaves a partial body on disk); a body voxel no row covers → air.
    /// </summary>
    private void Reconcile(ChunkPos chunk, VoxelChunk voxels, IVoxelEditor world, VolumetricInventory? inventory, ref DrainReport report)
    {
        if (_byChunk.TryGetValue(chunk, out List<MachineRow>? rows))
        {
            // A copy, because a row removed below leaves the list under the loop.
            foreach (MachineRow row in rows.ToArray())
            {
                // A wreck is never rebuilt and never removed here (IM-3's pass, decision 5).
                if (IsKnownWreck(row))
                {
                    continue;
                }

                int present = 0;
                bool allAir = true;
                for (int lz = 0; lz < BrushCell.Voxels; lz++)
                for (int lx = 0; lx < BrushCell.Voxels; lx++)
                for (int ly = 0; ly < BrushCell.Voxels; ly++)
                {
                    if (!world.TryGetMaterial(row.Cell.VoxelAt(lx, ly, lz), out int material))
                    {
                        allAir = false;
                        continue;
                    }

                    if (material == row.MaterialId) present++;
                    else if (material != MaterialIds.AirNumericId) allAir = false;
                }

                if (present == BrushCell.Voxels * BrushCell.Voxels * BrushCell.Voxels)
                {
                    continue;
                }

                if (present == 0)
                {
                    if (allAir)
                    {
                        // The chunk never landed with its machine — an autosave's row, a chunk
                        // written before the placement or lost with a crash. Rebuilt, so the
                        // authored state wins; the hook sees the body's own material and counts
                        // nothing.
                        WriteCell(world, row.Cell, row.MaterialId);
                        Rebuilt++;
                        report.Rebuilt++;
                    }
                    else if (HoldsAnything(row))
                    {
                        // Rock where a vault stood that holds something: the body goes back to the
                        // inventory as a returned one would, and the row stays as a wreck with its
                        // contents owed — it never leaves the books (decision 17).
                        inventory?.Add(row.MaterialId, BrushCell.Voxels * BrushCell.Voxels * BrushCell.Voxels);
                        KeepAsWreck(row);
                        report.Wrecked++;
                    }
                    else
                    {
                        // The cell is not air — the chunk regenerated as rock because its write
                        // was lost. Nothing can stand there; the row says the player owns it, so
                        // it goes back to the inventory.
                        Remove(row.Cell);
                        inventory?.Add(row.MaterialId, BrushCell.Voxels * BrushCell.Voxels * BrushCell.Voxels);
                        ReturnedAtArrival++;
                        report.Returned++;
                    }

                    continue;
                }

                // Part of a body on disk is a fall's — the dismantle never leaves one — so it is
                // destroyed; a vault holding anything becomes a wreck instead, its body cleared.
                if (HoldsAnything(row))
                {
                    KeepAsWreck(row);
                    ClearBodyVoxels(world, row.Cell, row.MaterialId);
                    report.Wrecked++;
                    continue;
                }

                // The row goes first, so the writes below are not damage the hook counts.
                Remove(row.Cell);
                ClearBodyVoxels(world, row.Cell, row.MaterialId);
                DestroyedAtArrival++;
                report.Destroyed++;
            }
        }

        if (_materials is null)
        {
            return;
        }

        bool mayHoldMachine = false;
        foreach (int id in _materials.MachineIds)
        {
            if (voxels.MayContain(id))
            {
                mayHoldMachine = true;
                break;
            }
        }

        if (!mayHoldMachine)
        {
            return;
        }

        // A machine-material voxel with no row over it is the orphan of a crash before the row's
        // autosave: the body was written at an unload, the row never was. Air, without credit —
        // the header's debit was never written either, so the player still has the material.
        VoxelPos origin = chunk.OriginVoxel;
        for (int lz = 0; lz < VoxelConstants.ChunkSize; lz++)
        for (int lx = 0; lx < VoxelConstants.ChunkSize; lx++)
        for (int ly = 0; ly < VoxelConstants.ChunkSize; ly++)
        {
            int material = voxels.Get(lx, ly, lz);
            if (!_materials.IsMachine(material))
            {
                continue;
            }

            var voxel = new VoxelPos(origin.X + lx, origin.Y + ly, origin.Z + lz);
            if (_rows.ContainsKey(BrushCell.FromVoxel(voxel)))
            {
                continue;
            }

            if (world.SetVoxel(voxel, MaterialIds.AirNumericId))
            {
                OrphanVoxelsCleared++;
                report.OrphanVoxels++;
            }
        }
    }

    /// <summary>
    /// <c>Lost == Carved</c>: a pick did it, the remainder is credited. <c>Lost &gt; Carved</c>:
    /// something else took a voxel — a fall — and the remainder is cleared without credit.
    /// </summary>
    private void Dismantle(MachineRow row, IVoxelEditor world, VolumetricInventory? inventory, ref DrainReport report)
    {
        if (!_rows.TryGetValue(row.Cell, out MachineRow? live) || !ReferenceEquals(live, row))
        {
            // Already gone — the reconcile above removed it this drain, or a placement replaced
            // it. Nothing to write and nothing to credit.
            return;
        }

        // A wreck has no body to lose (IM-3's pass, decision 5): a block placed in its cell, or a
        // second fall, marks it and nothing follows. A vault that still holds anything is never
        // removed here — the pick moved its contents already, so one that holds is a fall's, or a
        // move that could not happen — and becomes a wreck, its body cleared as the fall left it.
        if (IsVault(row) && (IsKnownWreck(row) || HoldsAnything(row)))
        {
            if (IsKnownWreck(row))
            {
                KeepAsWreck(row);
                return;
            }

            bool picked = row.Lost == row.Carved;
            KeepAsWreck(row);
            bool wasRecording = PauseJournal();
            try
            {
                int cleared = ClearBodyVoxels(world, row.Cell, row.MaterialId);
                if (picked)
                {
                    inventory?.Add(row.MaterialId, cleared);
                }
            }
            finally
            {
                ResumeJournal(wasRecording);
            }

            report.Wrecked++;
            return;
        }

        // In both arms the row goes first, so the writes after it are not damage the hook counts
        // against a row that no longer exists.
        if (row.Lost == row.Carved)
        {
            // A pick did it: the player's edit, journalled into their step.
            Remove(row.Cell);
            int cleared = ClearBodyVoxels(world, row.Cell, row.MaterialId);
            inventory?.Add(row.MaterialId, cleared);
            Dismantled++;
            DismantledVoxelsCredited += cleared;
            report.Dismantled++;
            report.CreditedVoxels += cleared;
            return;
        }

        // Something else took a voxel, a fall: the world's write, which the undo must never see.
        bool recording = PauseJournal();
        try
        {
            Remove(row.Cell);
            ClearBodyVoxels(world, row.Cell, row.MaterialId);
        }
        finally
        {
            ResumeJournal(recording);
        }

        Destroyed++;
        report.DestroyedAtDrain++;
    }

    /// <summary>Switches the journal off for the world's own writes; returns what it was, for <see cref="ResumeJournal"/>.</summary>
    private bool PauseJournal()
    {
        if (Journal is null)
        {
            return false;
        }

        bool was = Journal.Recording;
        Journal.Recording = false;
        return was;
    }

    private void ResumeJournal(bool was)
    {
        if (Journal is not null)
        {
            Journal.Recording = was;
        }
    }

    private static void WriteCell(IVoxelEditor world, BrushCell cell, int materialId)
    {
        for (int lz = 0; lz < BrushCell.Voxels; lz++)
        for (int lx = 0; lx < BrushCell.Voxels; lx++)
        for (int ly = 0; ly < BrushCell.Voxels; ly++)
        {
            world.SetVoxel(cell.VoxelAt(lx, ly, lz), materialId);
        }
    }

    /// <summary>Writes air over every voxel of the cell that still holds the body's material; returns how many.</summary>
    private static int ClearBodyVoxels(IVoxelEditor world, BrushCell cell, int materialId)
    {
        int cleared = 0;
        for (int lz = 0; lz < BrushCell.Voxels; lz++)
        for (int lx = 0; lx < BrushCell.Voxels; lx++)
        for (int ly = 0; ly < BrushCell.Voxels; ly++)
        {
            VoxelPos voxel = cell.VoxelAt(lx, ly, lz);
            if (world.TryGetMaterial(voxel, out int material) && material == materialId
                && world.SetVoxel(voxel, MaterialIds.AirNumericId))
            {
                cleared++;
            }
        }

        return cleared;
    }

    // ---------------------------------------------------------------------------------------
    // Persistence.
    // ---------------------------------------------------------------------------------------

    /// <summary>Every row as stored bytes, in origin order, for the session save.</summary>
    /// <remarks>
    /// Origin order rather than dictionary order, so the same set writes the same table twice —
    /// the inventory's <c>Entries()</c> rule, for the reason M6d-3 gave it a second dependant.
    /// </remarks>
    public List<StoredMachine> Snapshot()
    {
        var rows = new List<StoredMachine>(_rows.Count);
        foreach (MachineRow row in _rows.Values)
        {
            rows.Add(new StoredMachine(row.Origin, row.MaterialId, MachineRowBlob.Write(row)));
        }

        rows.Sort(static (a, b) =>
        {
            int c = a.Origin.X.CompareTo(b.Origin.X);
            if (c != 0) return c;
            c = a.Origin.Y.CompareTo(b.Origin.Y);
            return c != 0 ? c : a.Origin.Z.CompareTo(b.Origin.Z);
        });

        return rows;
    }

    /// <summary>
    /// Replaces the set with the stored rows, checking each port against its material's
    /// declaration where the material declares any.
    /// </summary>
    /// <param name="stored">The machine table, whole.</param>
    /// <param name="portsOf">
    /// The ports a material's machine block declares, or null for a material that is not a known
    /// machine — a hole left by a removed mod, or a material with no machine block — whose rows
    /// are kept verbatim and counted <see cref="RestoreReport.Unknown"/>.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>A row is a caller</b> (the ladder's decision 21): a port the declaration does not name
    /// is dropped, a declared port the row lacks takes its default, and a value outside its bounds
    /// is clamped — each counted, none refused, because refusing a row is refusing the world over
    /// a number the programmer can retype. A blob that cannot be <em>read</em> is refused, by the
    /// policy every blob here follows: a corrupt row silently repaired is a machine somewhere the
    /// player did not put it.
    /// </para>
    /// </remarks>
    public RestoreReport Restore(
        IReadOnlyList<StoredMachine> stored,
        Func<int, IReadOnlyList<PortDefinition>?> portsOf)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(portsOf);

        // Whatever stood here leaves with its charge; what the table holds enters below through
        // Add. A restore over an empty set counts nothing here, and one over a live set (a test's
        // reopen) nets to zero, so the pool identity holds across it either way.
        foreach (MachineRow leaving in _rows.Values)
        {
            StoredMillijoulesLeaving += ChargeOf(leaving);
            VaultVoxelsLeaving += KnownVaultVoxels(leaving);
        }

        _rows.Clear();
        _byChunk.Clear();
        _arrivals.Clear();
        _queued.Clear();
        _damaged.Clear();
        _damagedSet.Clear();
        Version++;

        int unknown = 0, dropped = 0, defaulted = 0, clamped = 0, overfull = 0;

        foreach (StoredMachine entry in stored)
        {
            MachineRow row = MachineRowBlob.Read(entry.Blob, entry.Origin, entry.MaterialId);

            IReadOnlyList<PortDefinition>? declared = portsOf(row.MaterialId);
            if (declared is null)
            {
                unknown++;
            }
            else
            {
                foreach (string name in row.Ports.Keys.ToArray())
                {
                    if (!declared.Any(port => port.Name == name))
                    {
                        row.RemovePort(name);
                        dropped++;
                    }
                }

                foreach (PortDefinition port in declared)
                {
                    if (!row.TryGetPort(port.Name, out long value))
                    {
                        row.SetPortUnchecked(port.Name, port.Default);
                        defaulted++;
                        continue;
                    }

                    long inBounds = port.Clamp(value);
                    if (inBounds != value)
                    {
                        row.SetPortUnchecked(port.Name, inBounds);
                        clamped++;
                    }
                }
            }

            // The charge is the storage kind's state and its bound is the material's capacity —
            // clamped and counted by decision 21's rule, since a row read from disk is a caller
            // too. Through the set's own table: the ports lookup a test hands in says nothing
            // about capacities, and a set without a table clamps nothing.
            if (_materials?.Machine(row.MaterialId) is { Kind: MachineKind.Storage, Carried: false } storage)
            {
                long charge = StorageState.Read(row);
                if (charge > storage.CapacityMillijoules)
                {
                    StorageState.Write(row, storage.CapacityMillijoules);
                    clamped++;
                }
            }

            // A vault's contents are never clamped (IM-3's pass, decision 11), unlike a cell's
            // charge: a grade retuned smaller keeps what it holds, refuses deposits, and is counted.
            if (_materials?.Machine(row.MaterialId) is { Kind: MachineKind.Vault } vault
                && VaultState.TryRead(row, out VaultContents contents)
                && contents.UsedMillilitres > vault.InsideMillilitres)
            {
                overfull++;
            }

            if (_rows.ContainsKey(row.Cell))
            {
                throw new BlobFormatException(
                    $"Machine row at {row.Origin}: a second row stands in the same cell. The table is damaged.");
            }

            Add(row);
        }

        return new RestoreReport(stored.Count, unknown, dropped, defaulted, clamped, overfull);
    }
}

/// <summary>How a <see cref="MachineSet.SetPort"/> landed.</summary>
public enum PortWriteResult : byte
{
    /// <summary>The value was in bounds and is on the row.</summary>
    Set,

    /// <summary>The value was outside the port's bounds; the bound is on the row.</summary>
    Clamped,

    /// <summary>No row stands in that cell.</summary>
    NoSuchRow,

    /// <summary>The row's material declares no port by that name — or declares no ports at all.</summary>
    NoSuchPort,
}

/// <summary>What one <see cref="MachineSet.SetPort"/> did, and the value that landed.</summary>
public readonly record struct PortWrite(PortWriteResult Result, long Value)
{
    /// <summary>True when a value is on the row after the call, clamped or not.</summary>
    public bool Landed => Result is PortWriteResult.Set or PortWriteResult.Clamped;

    public override string ToString() => Result switch
    {
        PortWriteResult.Set => $"set to {Value}",
        PortWriteResult.Clamped => $"clamped to {Value}",
        PortWriteResult.NoSuchRow => "no machine there",
        _ => "no such port",
    };
}

/// <summary>What <see cref="MachineSet.Restore"/> found, for the open's line.</summary>
/// <param name="Rows">Rows read.</param>
/// <param name="Unknown">Rows whose material declares no ports this build knows — kept verbatim.</param>
/// <param name="Dropped">Ports no declaration names, removed.</param>
/// <param name="Defaulted">Declared ports the row lacked, supplied.</param>
/// <param name="Clamped">Values outside their bounds, clamped.</param>
/// <param name="Overfull">Vaults holding more than their inside — kept, never clamped, refusing deposits (IM-3).</param>
public readonly record struct RestoreReport(int Rows, int Unknown, int Dropped, int Defaulted, int Clamped, int Overfull = 0)
{
    public override string ToString() =>
        $"{Rows} machine row(s) read ({Unknown} of unknown kind), {Dropped} port(s) dropped, "
        + $"{Defaulted} defaulted, {Clamped} clamped"
        + (Overfull > 0 ? $", {Overfull} vault(s) overfull" : string.Empty);
}

/// <summary>What one <see cref="MachineSet.Drain"/> did.</summary>
public struct DrainReport
{
    /// <summary>Arrivals reconciled.</summary>
    public int Reconciled;

    /// <summary>Queued chunks that had left before the drain.</summary>
    public int Left;

    /// <summary>Bodies rewritten from their row.</summary>
    public int Rebuilt;

    /// <summary>Rows dismantled with credit because their cell was rock.</summary>
    public int Returned;

    /// <summary>Rows destroyed at arrival for a partial body.</summary>
    public int Destroyed;

    /// <summary>Machine-material voxels no row covered, written to air.</summary>
    public int OrphanVoxels;

    /// <summary>Rows a pick dismantled.</summary>
    public int Dismantled;

    /// <summary>Voxels the dismantle credited.</summary>
    public int CreditedVoxels;

    /// <summary>Rows destroyed at the drain — a fall took a voxel.</summary>
    public int DestroyedAtDrain;

    /// <summary>Vaults holding something made wrecks instead of removed, at the drain or at an arrival (IM-3).</summary>
    public int Wrecked;

    /// <summary>True when the drain wrote or credited anything.</summary>
    public readonly bool DidAnything =>
        Rebuilt + Returned + Destroyed + OrphanVoxels + Dismantled + DestroyedAtDrain + Wrecked > 0;

    public override readonly string ToString() =>
        $"{Reconciled} reconciled ({Left} left), {Rebuilt} rebuilt, {Returned} returned, {Destroyed} destroyed, "
        + $"{OrphanVoxels} orphan voxel(s) cleared; {Dismantled} dismantled ({CreditedVoxels} credited), "
        + $"{DestroyedAtDrain} destroyed, {Wrecked} wrecked";
}
