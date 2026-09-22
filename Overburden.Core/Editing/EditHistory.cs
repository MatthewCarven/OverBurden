using Overburden.Core.Content;
using Overburden.Core.Inventory;
using Overburden.Core.Machines;
using Overburden.Core.Voxels;

namespace Overburden.Core.Editing;

/// <summary>Why an undo did nothing.</summary>
public enum UndoOutcome
{
    /// <summary>The step went back.</summary>
    Undone,

    /// <summary>Nothing to undo.</summary>
    Empty,

    /// <summary>Putting the material back would cost more than the inventory holds.</summary>
    OutOfStock,

    /// <summary>A voxel's chunk would not take the write — it left the box between the edit and the press.</summary>
    Unreachable,
}

/// <summary>What one undo did.</summary>
/// <param name="Voxels">Voxels written back.</param>
/// <param name="Chunks">Chunks they were in.</param>
/// <param name="MachineRows">Rows put back or taken away again.</param>
/// <param name="Debited">Voxels taken out of the inventory to fill holes back in.</param>
/// <param name="Credited">Voxels given back for material this step had placed.</param>
/// <param name="StepsLeft">Steps still on the stack.</param>
/// <param name="Refused">The material the step could not pay for, or the first voxel that could not be written.</param>
public readonly record struct UndoReport(
    UndoOutcome Outcome,
    int Voxels = 0,
    int Chunks = 0,
    int MachineRows = 0,
    long Debited = 0,
    long Credited = 0,
    int StepsLeft = 0,
    int RefusedMaterialId = 0,
    long RefusedShortfall = 0)
{
    public bool Ok => Outcome == UndoOutcome.Undone;
}

/// <summary>
/// The last few edits, so the last few can be put back — UNDO-PLAN, built 2026-09-22 on
/// Matthew's ask after the quantum excavator took most of a world.
/// </summary>
/// <remarks>
/// <para>
/// <b>A step is a frame in which something was written, not a click.</b> The plan said "the
/// click", and the build found that the click is not where all of a click's writing happens: a
/// fill into water is deferred to the fluid boundary, a dismantle lands at the frame's machine
/// drain, and a chunk fill is queued. A frame catches all three with no plumbing in the verbs,
/// and the one cost is that a click whose work is deferred to a later frame is two steps. Main
/// closes the open step once a frame, after the drain.
/// </para>
/// <para>
/// <b>Entries are grouped per chunk and deduplicated by voxel</b>: the first <c>was</c> and the
/// latest <c>now</c>, so a voxel written twice in a step (the displacing fill lifts and returns)
/// goes back to where it started in one write and is counted once. The grouping is also what
/// makes <see cref="Forget"/> a dictionary removal.
/// </para>
/// <para>
/// <b>Two things clear the stack, and both are the honest answer rather than a cheap one.</b> A
/// fall mints rubble, so undoing the bite that brought a roof down would restore the roof
/// <em>and</em> leave the rubble — matter from nothing. A chunk that leaves the box reloads its
/// edited state from the world file, so its entries can neither be applied nor deferred: the
/// chunk that comes back is not the chunk that left. Three steps is seconds of play, so clearing
/// costs a player nothing they were reaching for. See UNDO-PLAN decisions 4 and 5.
/// </para>
/// <para>
/// <b>The inventory is the exact inverse and is derived from the entries themselves</b>, with no
/// separate tally: putting a solid voxel back costs one of it, removing one the step placed
/// gives one back. A step that can no longer pay is refused <em>whole and kept</em> rather than
/// clamped, because clamping mints or destroys volume and this game's oracles are exact totals.
/// </para>
/// <para>
/// <b>An undo charges no energy and refunds none</b> (decision 7): any other rule makes Ctrl+Z
/// either an energy exploit — bite, undo, bite again — or a punishment for a slip.
/// </para>
/// </remarks>
public sealed class EditHistory : IEditJournal
{
    /// <summary>Steps kept. Matthew asked for three.</summary>
    public const int MaxSteps = 3;

    private sealed class Step
    {
        public readonly Dictionary<ChunkPos, Dictionary<ushort, (int Was, int Now)>> Voxels = [];
        public readonly List<(MachineRow Row, bool Added)> Machines = [];

        public int VoxelCount
        {
            get
            {
                int n = 0;
                foreach (Dictionary<ushort, (int, int)> chunk in Voxels.Values)
                {
                    n += chunk.Count;
                }

                return n;
            }
        }

        public bool IsEmpty => Voxels.Count == 0 && Machines.Count == 0;
    }

    private readonly List<Step> _steps = [];
    private Step? _open;

    /// <summary>Steps that can be undone.</summary>
    public int Steps => _steps.Count + (_open is { IsEmpty: false } ? 1 : 0);

    /// <summary>Voxels held across every step, the memory this costs.</summary>
    public int HeldVoxels
    {
        get
        {
            int n = _open?.VoxelCount ?? 0;
            foreach (Step step in _steps)
            {
                n += step.VoxelCount;
            }

            return n;
        }
    }

    /// <summary>Steps dropped because a chunk they touched left the box.</summary>
    public int ClearedByForget { get; private set; }

    /// <summary>Times the stack was cleared whole by a fall.</summary>
    public int ClearedByFall { get; private set; }

    /// <summary>Steps that fell off the end of the stack, unrecoverable by design.</summary>
    public int Dropped { get; private set; }

    /// <summary>Undos asked for.</summary>
    public int Undos { get; private set; }

    /// <summary>Undos refused, for any reason.</summary>
    public int Refusals { get; private set; }

    /// <summary>Whether recording is on. Off, every hook is a branch and nothing is kept.</summary>
    public bool Recording { get; set; } = true;

    /// <inheritdoc />
    public void Wrote(VoxelPos voxel, int was, int now)
    {
        if (!Recording || was == now)
        {
            return;
        }

        Step step = _open ??= new Step();
        if (!step.Voxels.TryGetValue(voxel.Chunk, out Dictionary<ushort, (int Was, int Now)>? chunk))
        {
            chunk = [];
            step.Voxels[voxel.Chunk] = chunk;
        }

        (int lx, int ly, int lz) = voxel.Local;
        ushort index = (ushort)VoxelPos.Index(lx, ly, lz);

        // The first `was` and the latest `now`: a voxel written twice in one step goes back to
        // where the step found it, not to where its second write found it.
        chunk[index] = chunk.TryGetValue(index, out (int Was, int Now) seen) ? (seen.Was, now) : (was, now);
    }

    /// <inheritdoc />
    public void MachineChanged(MachineRow row, bool added)
    {
        if (!Recording)
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(row);
        (_open ??= new Step()).Machines.Add((Copy(row), added));
    }

    /// <summary>
    /// Closes the open step, if anything was written into it. Called once a frame, after the
    /// machine drain, so a dismantle lands in the step that caused it.
    /// </summary>
    /// <returns>Whether a step was closed.</returns>
    public bool EndStep()
    {
        if (_open is null)
        {
            return false;
        }

        Step step = _open;
        _open = null;
        if (step.IsEmpty)
        {
            return false;
        }

        _steps.Add(step);
        while (_steps.Count > MaxSteps)
        {
            _steps.RemoveAt(0);
            Dropped++;
        }

        return true;
    }

    /// <summary>Drops everything, open step included — a fall's answer (decision 4).</summary>
    public void Clear()
    {
        if (Steps > 0)
        {
            ClearedByFall++;
        }

        _steps.Clear();
        _open = null;
    }

    /// <summary>
    /// Drops every step that touched <paramref name="chunk"/> — the chunk is leaving the box and
    /// will come back holding its edited state (decision 5). Returns how many went.
    /// </summary>
    public int Forget(ChunkPos chunk)
    {
        int dropped = 0;
        for (int i = _steps.Count - 1; i >= 0; i--)
        {
            if (_steps[i].Voxels.ContainsKey(chunk))
            {
                _steps.RemoveAt(i);
                dropped++;
            }
        }

        if (_open is not null && _open.Voxels.ContainsKey(chunk))
        {
            _open = null;
            dropped++;
        }

        ClearedByForget += dropped;
        return dropped;
    }

    /// <summary>
    /// Puts the last step back: the rows it added removed, the voxels it wrote restored, the rows
    /// it removed added again, and the inventory moved by exactly the inverse.
    /// </summary>
    /// <remarks>
    /// <b>The order is rows-removed, voxels, rows-added, and it is load-bearing.</b> A row still
    /// standing when air is written over its body takes damage marks and its drain destroys it;
    /// a row added before its body exists is a row over air, which the same drain destroys. So
    /// the set never sees a body and a row disagree.
    /// </remarks>
    public UndoReport Undo(IVoxelEditor world, MaterialTable materials, VolumetricInventory? inventory, MachineSet? machines)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(materials);

        EndStep();
        Undos++;
        if (_steps.Count == 0)
        {
            Refusals++;
            return new UndoReport(UndoOutcome.Empty);
        }

        Step step = _steps[^1];

        // The whole step's cost first, so a refusal leaves the world untouched rather than
        // half-restored. Net per material: what goes back costs, what is taken away credits.
        if (inventory is not null)
        {
            Dictionary<int, long> net = [];
            foreach (Dictionary<ushort, (int Was, int Now)> chunk in step.Voxels.Values)
            {
                foreach ((int was, int now) in chunk.Values)
                {
                    if (was != MaterialIds.AirNumericId)
                    {
                        net[was] = net.GetValueOrDefault(was) + 1;
                    }

                    if (now != MaterialIds.AirNumericId)
                    {
                        net[now] = net.GetValueOrDefault(now) - 1;
                    }
                }
            }

            foreach ((int material, long owed) in net)
            {
                if (owed > inventory.Voxels(material))
                {
                    Refusals++;
                    return new UndoReport(
                        UndoOutcome.OutOfStock,
                        StepsLeft: _steps.Count,
                        RefusedMaterialId: material,
                        RefusedShortfall: owed - inventory.Voxels(material));
                }
            }
        }

        // Every voxel has to be writable before any of them is written, for the same reason.
        foreach ((ChunkPos chunk, Dictionary<ushort, (int Was, int Now)> cells) in step.Voxels)
        {
            foreach (ushort index in cells.Keys)
            {
                if (!world.TryGetMaterial(Voxel(chunk, index), out _))
                {
                    Refusals++;
                    return new UndoReport(UndoOutcome.Unreachable, StepsLeft: _steps.Count);
                }
            }
        }

        // An undo writes through the same SetVoxel it listens to, and the first run of this
        // battery caught it recording its own inverse as a fresh step — so the press after an
        // undo redid the edit, and three steps bounced between two states. Nothing an undo
        // writes is journalled; that is also decision 10, "no redo", falling out for free.
        bool recording = Recording;
        Recording = false;
        try
        {
            return Apply(world, step, inventory, machines);
        }
        finally
        {
            Recording = recording;
        }
    }

    private UndoReport Apply(IVoxelEditor world, Step step, VolumetricInventory? inventory, MachineSet? machines)
    {
        int rows = 0;
        foreach ((MachineRow row, bool added) in step.Machines)
        {
            if (added && machines is not null && machines.Remove(row.Cell))
            {
                rows++;
            }
        }

        int voxels = 0;
        long debited = 0;
        long credited = 0;
        foreach ((ChunkPos chunk, Dictionary<ushort, (int Was, int Now)> cells) in step.Voxels)
        {
            foreach ((ushort index, (int was, int now)) in cells)
            {
                if (!world.SetVoxel(Voxel(chunk, index), was))
                {
                    continue;
                }

                voxels++;
                if (now != MaterialIds.AirNumericId)
                {
                    inventory?.Add(now, 1);
                    credited++;
                }

                if (was != MaterialIds.AirNumericId)
                {
                    inventory?.Take(was, 1);
                    debited++;
                }
            }
        }

        foreach ((MachineRow row, bool added) in step.Machines)
        {
            if (!added && machines is not null)
            {
                machines.Add(Copy(row));
                rows++;
            }
        }

        _steps.RemoveAt(_steps.Count - 1);
        return new UndoReport(
            UndoOutcome.Undone,
            voxels,
            step.Voxels.Count,
            rows,
            debited,
            credited,
            _steps.Count);
    }

    private static VoxelPos Voxel(ChunkPos chunk, ushort index)
    {
        (int x, int y, int z) = VoxelPos.FromIndex(index);
        return VoxelPos.FromChunkLocal(chunk, x, y, z);
    }

    /// <summary>
    /// A row as it stood, detached from the set's own object — its damage counters left at zero,
    /// because a row put back carries the body the undo restored and not the wounds that killed it.
    /// </summary>
    private static MachineRow Copy(MachineRow row) =>
        new(row.Cell, row.MaterialId, row.Facing, row.Ports, (byte[])row.KindState.Clone());
}
