using Overburden.Core.Building;
using Overburden.Core.Content;
using Overburden.Core.Editing;
using Overburden.Core.Voxels;

namespace Overburden.Core.Machines;

/// <summary>
/// The dump's blocks (IM-5b; IM-5's pass, decisions 11–16, as the re-read of 2026-10-11 left them): a pile
/// written straight into the world at voxel grain, last in the boundary, into whatever the water left dry.
/// </summary>
/// <remarks>
/// <para>
/// <b>The heap is a cone, one up for one out</b> (decision 11, his word at the re-read). Each voxel goes to
/// the free slot — an air voxel resting on something solid — with the least <i>height above the vault's
/// floor plus horizontal distance from the vault</i>, so a heap fills the hollows by its vault first, then
/// covers it, and grows at 45°. The slots are kept by column: a column's slot is the voxel the next block
/// there takes. A column joins the heap when a neighbour's pile reaches a height at which the column is
/// open — a block rolls off into it and falls to what it rests on — so a heap never passes through rock,
/// climbs a wall only once it is piled as high, and spills over a sill. A fall deeper than
/// <see cref="DropDepth"/> below the vault's floor is no slot, and nothing past <see cref="HeapRadius"/>
/// is reached. A machine's or a wreck's cell is never a slot and counts as solid to rest on.
/// </para>
/// <para>
/// <b>Each one-voxel shell is filled by angle round the vault</b> (the bench's build choice): the queue is
/// ordered by the shell, then the angle, so a boundary's voxels work a wedge from the heap's top to its foot
/// — a few chunk columns — rather than a ring round the whole heap. Chunks touched are what a pile costs
/// (the re-read's fact 5), and at most <see cref="PileChunksPerBoundary"/> are written a boundary.
/// </para>
/// <para>
/// <b>A slot is refused, and its column waits for the next boundary,</b> when its chunk is not loaded to the
/// water, when the live grid holds water there (the published copy misses this boundary's ball), when it is
/// in a lake band (each chunk column covered the first time a boundary asks), inside the player, or reserved
/// by a landing collapse. A slot the world has changed under is looked for again first: filled, the column's
/// slot moves up; dug out from under, it falls.
/// </para>
/// <para>
/// <b>Only what can lie loose is piled</b> (decision 12) and <b>a dump heaps at most
/// <see cref="HeapCeilingVoxels"/></b> (decision 14): both decided when the vault is first seen owing, the
/// rest decohered whole, the excess from the highest material ID down. The materials are piled mixed, each
/// voxel the material with the most left.
/// </para>
/// <para>
/// <b>Recording is off while the piles write, and the undo forgets every chunk they wrote</b> (decision 13),
/// here rather than in the game, so the guarantee is Core's and a test can break it. A heap that finds no
/// slot at all rests <see cref="NoRoomWaitMillis"/>, as a ball does, and starts again from its vault: the
/// world may have been dug.
/// </para>
/// </remarks>
public sealed partial class VaultDump
{
    /// <summary>Voxels written a boundary across every pile, shared round-robin by position (decision 33; the bench held it, 2026-10-11).</summary>
    public const int VoxelsPerBoundary = 512;

    /// <summary>Chunks the piles may write a boundary: what bounds the light and remeshing after it (the bench's build choice).</summary>
    public const int PileChunksPerBoundary = 8;

    /// <summary>How far from its vault a heap reaches, in voxels: 32 m (decision 11).</summary>
    public const int HeapRadius = 32 * VoxelConstants.VoxelsPerMetre;

    /// <summary>The most one dump heaps: 10,000 m³ (decision 14), the excess decohered when it is first seen owing.</summary>
    public const long HeapCeilingVoxels = 10_000L * VoxelConstants.VoxelsPerMetre * VoxelConstants.VoxelsPerMetre * VoxelConstants.VoxelsPerMetre;

    /// <summary>How far below its vault's floor a heap's block may fall to rest, in voxels: 8 m. A deeper drop is no slot.</summary>
    public const int DropDepth = 8 * VoxelConstants.VoxelsPerMetre;

    /// <summary>How far up a filled slot is looked for again, in voxels: a metre.</summary>
    private const int RescanVoxels = VoxelConstants.VoxelsPerMetre;

    /// <summary>Columns looked at a boundary for each voxel of a pile's share — stale and waiting ones included.</summary>
    private const int VisitsPerVoxel = 8;

    private readonly IVoxelEditor? _world;
    private readonly MaterialTable? _materials;
    private readonly long _voxelsDumpedBase, _voxelsDecoheredBase;

    private readonly Dictionary<BrushCell, Heap> _heaps = [];
    private readonly HashSet<ChunkPos> _pileChunks = [];
    private readonly HashSet<(int X, int Z)> _covered = [];
    private readonly List<MachineRow> _pilers = [];
    private readonly SortedDictionary<int, long> _left = [];
    private readonly SortedDictionary<int, long> _placedBy = [];

    private static readonly (int X, int Z)[] Sides = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    /// <summary>
    /// The undo the piles keep away from (decision 13): its recording is off while they write, and every
    /// step touching a chunk they wrote is forgotten after. Null keeps nothing, as the pipeline's journal.
    /// </summary>
    public EditHistory? History { get; set; }

    /// <summary>The voxels a landing collapse has reserved (<c>CollapseDirector.Reserved</c>); a pile never writes one.</summary>
    public IReadOnlySet<VoxelPos>? Reserved { get; set; }

    // ---- the pile's own counters, which Exact checks the store's twins against ----

    /// <summary>Voxels the piles wrote into the world, over the lifetime.</summary>
    public long VoxelsPiled { get; private set; }

    /// <summary>Voxels decohered: what cannot lie loose and what is past the heap's ceiling, over the lifetime.</summary>
    public long VoxelsDecohered { get; private set; }

    /// <summary>Of <see cref="VoxelsDecohered"/>, what could not lie loose (decision 12).</summary>
    public long NotLooseDecoheredVoxels { get; private set; }

    /// <summary>Of <see cref="VoxelsDecohered"/>, what the heap's ceiling took (decision 14).</summary>
    public long HeapCeilingDecoheredVoxels { get; private set; }

    /// <summary>Heaps that found no slot at all and rested, over the lifetime.</summary>
    public long PileNoRoomRests { get; private set; }

    /// <summary>Undo steps dropped for touching a chunk a pile wrote, over the lifetime (decision 13's own counter).</summary>
    public long PileStepsForgotten { get; private set; }

    /// <summary>Column-boundaries a heap waited on, by why: not loaded to the water, wet, in a lake, the player, a landing collapse.</summary>
    public long PileWaitedUnloaded { get; private set; }

    public long PileWaitedWet { get; private set; }

    public long PileWaitedLake { get; private set; }

    public long PileWaitedPlayer { get; private set; }

    public long PileWaitedReserved { get; private set; }

    /// <summary>The chunks the piles wrote at the last boundary.</summary>
    public IReadOnlyCollection<ChunkPos> PileChunksLast => _pileChunks;

    /// <summary>
    /// Whether a material can lie loose in the world (decision 12): placeable, not air, not a fluid, not a
    /// machine, and inside the table. A removed mod's stand-in reads placeable and solid, so it piles; a
    /// machine material would be cleared without credit at its chunk's next arrival.
    /// </summary>
    public static bool CanLieLoose(MaterialTable materials, int materialId)
    {
        ArgumentNullException.ThrowIfNull(materials);
        return materialId != MaterialIds.AirNumericId
            && (uint)materialId < (uint)materials.Count
            && materials.IsPlaceable(materialId)
            && !materials.IsFluid(materialId)
            && !materials.IsMachine(materialId);
    }

    private struct PileTally
    {
        public int Piling;
        public long Voxels;
        public int Waiting;
        public int NoRoom;
        public int Forgotten;
    }

    private enum SlotAnswer : byte
    {
        Ready,
        Moved,
        Closed,
        Waiting,
    }

    /// <summary>A heap column: where its next block goes, and which of its four neighbours it has opened.</summary>
    private sealed class Column(int x, int z, double distance, double angle)
    {
        public int X { get; } = x;

        public int Z { get; } = z;

        public double Distance { get; } = distance;

        public double Angle { get; } = angle;

        public int Slot { get; set; }

        public bool Queued { get; set; }

        public bool Closed { get; set; }

        /// <summary>A bit for each of <see cref="Sides"/> whose column is known — joined, out of reach or too deep.</summary>
        public byte Known { get; set; }
    }

    /// <summary>One dump's heap, kept between boundaries: its centre and floor, its columns and their queue.</summary>
    private sealed class Heap
    {
        public bool Seeded { get; set; }

        public int BaseY { get; set; }

        public double CentreX { get; set; }

        public double CentreZ { get; set; }

        public long RestMillis { get; set; }

        public Dictionary<(int X, int Z), Column> Columns { get; } = [];

        public PriorityQueue<Column, (int Shell, double Angle)> Queue { get; } = new();

        public List<Column> Waiting { get; } = [];

        public void Reset()
        {
            Seeded = false;
            Columns.Clear();
            Queue.Clear();
            Waiting.Clear();
        }
    }

    /// <summary>
    /// Decisions 12 and 14, when a vault is first seen owing: every material that cannot lie loose is
    /// decohered whole, then whatever is past the heap's ceiling, from the highest material ID down.
    /// </summary>
    private void AdmitVoxels(MachineRow row)
    {
        if (_materials is null || !VaultState.TryRead(row, out VaultContents contents) || contents.Voxels.Count == 0)
        {
            return;
        }

        foreach ((int id, long count) in contents.Voxels.ToArray())
        {
            if (!CanLieLoose(_materials, id))
            {
                ReleaseVoxels(row, id, dumped: 0, decohere: count);
                NotLooseDecoheredVoxels += count;
            }
        }

        if (!VaultState.TryRead(row, out contents))
        {
            return;
        }

        long excess = contents.TotalVoxels - HeapCeilingVoxels;
        foreach ((int id, long count) in contents.Voxels.Reverse().ToArray())
        {
            if (excess <= 0)
            {
                break;
            }

            long take = Math.Min(excess, count);
            ReleaseVoxels(row, id, dumped: 0, decohere: take);
            HeapCeilingDecoheredVoxels += take;
            excess -= take;
        }
    }

    /// <summary>
    /// The boundary's piles: every vault still owing voxels, by position, each a round-robin share of
    /// <see cref="VoxelsPerBoundary"/> with the remainder to the first, recording off throughout, and the
    /// undo told to forget every chunk written.
    /// </summary>
    private PileTally Pile((VoxelPos Min, VoxelPos Max)? player)
    {
        _pileChunks.Clear();
        _covered.Clear();
        var tally = default(PileTally);
        if (_world is null || _materials is null)
        {
            return tally;
        }

        _pilers.Clear();
        foreach (MachineRow row in _owing)
        {
            if (VaultState.TryRead(row, out VaultContents contents) && contents.Owes && contents.Voxels.Count > 0)
            {
                _pilers.Add(row);
            }
        }

        if (_pilers.Count == 0)
        {
            return tally;
        }

        EditHistory? history = History;
        bool recording = history?.Recording ?? false;
        if (history is not null)
        {
            history.Recording = false;
        }

        try
        {
            int share = VoxelsPerBoundary / _pilers.Count;
            int remainder = VoxelsPerBoundary % _pilers.Count;
            for (int i = 0; i < _pilers.Count; i++)
            {
                PileOne(_pilers[i], share + (i == 0 ? remainder : 0), player, ref tally);
            }
        }
        finally
        {
            if (history is not null)
            {
                history.Recording = recording;
            }
        }

        // Decision 13: a step the player took in a chunk the pile has since written would write its old
        // material back over the pile — blindly — and charge the player for it.
        if (history is not null)
        {
            foreach (ChunkPos chunk in _pileChunks)
            {
                tally.Forgotten += history.Forget(chunk);
            }
        }

        PileStepsForgotten += tally.Forgotten;
        return tally;
    }

    /// <summary>One vault's share: slots taken from its heap's queue, each checked, written, and the column moved on.</summary>
    private void PileOne(MachineRow row, int budget, (VoxelPos Min, VoxelPos Max)? player, ref PileTally tally)
    {
        Heap heap = HeapOf(row);
        if (heap.RestMillis > 0)
        {
            heap.RestMillis = Math.Max(0, heap.RestMillis - _boundaryMillis);
            tally.NoRoom++;
            return;
        }

        // The vault's own chunk gates the heap, as the mouth's gates a ball: a heap seeded into a world that is
        // not there would find no slot and rest as though the room were full.
        if (!Ready(MouthOf(row).Chunk))
        {
            PileWaitedUnloaded++;
            tally.Waiting++;
            return;
        }

        tally.Piling++;
        if (!heap.Seeded)
        {
            Seed(heap, row);
        }

        foreach (Column waited in heap.Waiting)
        {
            if (!waited.Queued && !waited.Closed)
            {
                Enqueue(heap, waited);
            }
        }

        heap.Waiting.Clear();

        VaultState.TryRead(row, out VaultContents contents);
        _left.Clear();
        foreach ((int id, long count) in contents.Voxels)
        {
            _left[id] = count;
        }

        _placedBy.Clear();
        long placed = 0;
        int visits = 0;
        int limit = budget * VisitsPerVoxel;
        while (placed < budget && visits < limit && _left.Count > 0 && heap.Queue.TryDequeue(out Column? column, out _))
        {
            visits++;
            column.Queued = false;
            switch (Check(heap, column, player, out VoxelPos slot))
            {
                case SlotAnswer.Moved:
                    Enqueue(heap, column);
                    continue;
                case SlotAnswer.Closed:
                    column.Closed = true;
                    continue;
                case SlotAnswer.Waiting:
                    heap.Waiting.Add(column);
                    tally.Waiting++;
                    continue;
            }

            if (!_pileChunks.Contains(slot.Chunk) && _pileChunks.Count >= PileChunksPerBoundary)
            {
                Enqueue(heap, column);
                break;
            }

            int material = MostLeft();
            if (!_world!.SetVoxel(slot, material))
            {
                heap.Waiting.Add(column);
                tally.Waiting++;
                PileWaitedUnloaded++;
                continue;
            }

            placed++;
            _pileChunks.Add(slot.Chunk);
            TakeOne(material);

            int below = column.Slot;
            column.Slot++;
            if (!TryOpen(new VoxelPos(column.X, column.Slot, column.Z), out bool open))
            {
                heap.Waiting.Add(column);
            }
            else if (open)
            {
                Enqueue(heap, column);
            }
            else
            {
                column.Closed = true;
            }

            // A block that sits at this height can roll into a neighbour open at it.
            for (int side = 0; side < Sides.Length; side++)
            {
                if ((column.Known & (1 << side)) == 0
                    && Discover(heap, column.X + Sides[side].X, column.Z + Sides[side].Z, column.Slot, below))
                {
                    column.Known |= (byte)(1 << side);
                }
            }
        }

        foreach ((int id, long count) in _placedBy)
        {
            ReleaseVoxels(row, id, dumped: count, decohere: 0);
        }

        VoxelsPiled += placed;
        tally.Voxels += placed;

        if (placed == 0 && heap.Queue.Count == 0 && heap.Waiting.Count == 0)
        {
            // No slot at all inside its reach: the heap rests, then starts again from its vault.
            heap.Reset();
            heap.RestMillis = NoRoomWaitMillis;
            PileNoRoomRests++;
            tally.NoRoom++;
        }
    }

    /// <summary>
    /// A new heap, or one starting again after a rest: its floor and centre from the vault's cell, and the
    /// columns of the cell and the ring round it, each opened at the first height from the mouth's layer
    /// down to the floor where it is open — the vault's top for its own columns, the floor beside it.
    /// </summary>
    private void Seed(Heap heap, MachineRow row)
    {
        VoxelPos origin = row.Origin;
        heap.BaseY = origin.Y;
        heap.CentreX = origin.X + (BrushCell.Voxels / 2.0);
        heap.CentreZ = origin.Z + (BrushCell.Voxels / 2.0);
        heap.Seeded = true;

        int mouth = origin.Y + BrushCell.Voxels;
        for (int z = origin.Z - 1; z <= origin.Z + BrushCell.Voxels; z++)
        {
            for (int x = origin.X - 1; x <= origin.X + BrushCell.Voxels; x++)
            {
                Discover(heap, x, z, mouth, origin.Y);
            }
        }
    }

    /// <summary>
    /// Joins a column to the heap if a block entering it between <paramref name="entryTop"/> and
    /// <paramref name="entryBottom"/> finds it open there, falling to what it rests on. True when the column
    /// is settled for good — joined, already known, out of reach, or too deep; false when it is shut at that
    /// height or not loaded, to be asked again when the neighbour's pile is higher.
    /// </summary>
    private bool Discover(Heap heap, int x, int z, int entryTop, int entryBottom)
    {
        if (heap.Columns.ContainsKey((x, z)))
        {
            return true;
        }

        double dx = x + 0.5 - heap.CentreX;
        double dz = z + 0.5 - heap.CentreZ;
        double distance = Math.Sqrt((dx * dx) + (dz * dz));
        if (distance > HeapRadius)
        {
            return true;
        }

        int y = entryTop;
        while (true)
        {
            if (!TryOpen(new VoxelPos(x, y, z), out bool open))
            {
                return false;
            }

            if (open)
            {
                break;
            }

            if (--y < entryBottom)
            {
                return false;
            }
        }

        var column = new Column(x, z, distance, Math.Atan2(dz, dx));
        heap.Columns[(x, z)] = column;
        switch (Fall(heap, column, y))
        {
            case SlotAnswer.Moved:
                Enqueue(heap, column);
                return true;
            case SlotAnswer.Closed:
                column.Closed = true;
                return true;
            default:
                // Not loaded under it: forgotten, so a later block finds it afresh.
                heap.Columns.Remove((x, z));
                return false;
        }
    }

    /// <summary>
    /// A slot as it stands now, and whether a block may go there: the world may have changed since the column
    /// was queued — filled (the slot moves up), dug from under (it falls) — and then the refusals.
    /// </summary>
    private SlotAnswer Check(Heap heap, Column column, (VoxelPos Min, VoxelPos Max)? player, out VoxelPos slot)
    {
        slot = new VoxelPos(column.X, column.Slot, column.Z);
        if (!TryOpen(slot, out bool open) || !TryOpen(slot.Offset(0, -1, 0), out bool openBelow))
        {
            PileWaitedUnloaded++;
            return SlotAnswer.Waiting;
        }

        if (!open)
        {
            for (int up = 1; up <= RescanVoxels; up++)
            {
                if (!TryOpen(slot.Offset(0, up, 0), out bool above))
                {
                    PileWaitedUnloaded++;
                    return SlotAnswer.Waiting;
                }

                if (above)
                {
                    column.Slot = slot.Y + up;
                    return SlotAnswer.Moved;
                }
            }

            return SlotAnswer.Closed;
        }

        if (openBelow)
        {
            SlotAnswer fell = Fall(heap, column, slot.Y);
            if (fell == SlotAnswer.Waiting)
            {
                PileWaitedUnloaded++;
            }

            return fell;
        }

        if (!Ready(slot.Chunk))
        {
            PileWaitedUnloaded++;
            return SlotAnswer.Waiting;
        }

        if (_fluid.HeldAt(slot) > 0)
        {
            PileWaitedWet++;
            return SlotAnswer.Waiting;
        }

        if (_ceiling is not null)
        {
            if (_covered.Add((slot.X >> VoxelConstants.ChunkSizeLog2, slot.Z >> VoxelConstants.ChunkSizeLog2)))
            {
                _ceiling.Cover(slot, slot, _submerged);
            }

            if (_ceiling.InLake(slot))
            {
                PileWaitedLake++;
                return SlotAnswer.Waiting;
            }
        }

        if (player is (VoxelPos min, VoxelPos max)
            && slot.X >= min.X && slot.X <= max.X
            && slot.Y >= min.Y && slot.Y <= max.Y
            && slot.Z >= min.Z && slot.Z <= max.Z)
        {
            PileWaitedPlayer++;
            return SlotAnswer.Waiting;
        }

        if (Reserved?.Contains(slot) == true)
        {
            PileWaitedReserved++;
            return SlotAnswer.Waiting;
        }

        return SlotAnswer.Ready;
    }

    /// <summary>
    /// From an open voxel down to the first that is not: the slot is the open voxel resting on it. Closed if
    /// the drop passes <see cref="DropDepth"/> under the heap's floor; waiting if it reaches an unloaded chunk.
    /// </summary>
    private SlotAnswer Fall(Heap heap, Column column, int from)
    {
        int y = from;
        int floor = heap.BaseY - DropDepth;
        while (true)
        {
            if (y - 1 < floor)
            {
                return SlotAnswer.Closed;
            }

            if (!TryOpen(new VoxelPos(column.X, y - 1, column.Z), out bool open))
            {
                return SlotAnswer.Waiting;
            }

            if (!open)
            {
                column.Slot = y;
                return SlotAnswer.Moved;
            }

            y--;
        }
    }

    /// <summary>
    /// Whether a voxel is open to the heap — air, and not in a machine's or a wreck's cell — or false when
    /// its chunk is not there to ask. Anything else, rock or a pile or a machine, is what a block rests on.
    /// </summary>
    private bool TryOpen(VoxelPos voxel, out bool open)
    {
        if (!_world!.TryGetMaterial(voxel, out int material))
        {
            open = false;
            return false;
        }

        open = material == MaterialIds.AirNumericId && !_machines.TryGet(BrushCell.FromVoxel(voxel), out _);
        return true;
    }

    private static void Enqueue(Heap heap, Column column)
    {
        int shell = (int)Math.Floor(column.Slot - heap.BaseY + column.Distance);
        column.Queued = true;
        heap.Queue.Enqueue(column, (shell, column.Angle));
    }

    /// <summary>The material with the most left, the lowest ID on a tie: the heap piles its materials mixed.</summary>
    private int MostLeft()
    {
        int best = -1;
        long most = 0;
        foreach ((int id, long count) in _left)
        {
            if (count > most)
            {
                best = id;
                most = count;
            }
        }

        return best;
    }

    private void TakeOne(int material)
    {
        long left = _left[material] - 1;
        if (left == 0)
        {
            _left.Remove(material);
        }
        else
        {
            _left[material] = left;
        }

        _placedBy[material] = _placedBy.GetValueOrDefault(material) + 1;
    }

    private Heap HeapOf(MachineRow row)
    {
        if (!_heaps.TryGetValue(row.Cell, out Heap? heap))
        {
            heap = new Heap();
            _heaps[row.Cell] = heap;
        }

        return heap;
    }

    /// <summary>The store's voxel door, and a spent wreck noted for after the walk.</summary>
    private void ReleaseVoxels(MachineRow row, int materialId, long dumped, long decohere)
    {
        if (_machines.ReleaseVoxels(row, materialId, dumped, decohere) == DumpEnd.SpentWreck)
        {
            _spent.Add(row);
        }

        VoxelsDecohered += decohere;
    }
}
