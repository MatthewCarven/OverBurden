using System.Diagnostics;
using Overburden.Core.Voxels;

namespace Overburden.Core.Fluid;

/// <summary>
/// The sheet flood's cell index — position to record — as two layers: a chunk column's slot by
/// its (x, z), then a flat 32 × 32 array by the cell's local (x, z). One flood is one Y, so Y is
/// not a layer.
/// </summary>
/// <remarks>
/// <para>
/// <b>The shape is Matthew's layered lookup table</b> (github.com/MatthewCarven/Python-Lookuptables,
/// the 2D bench: a bucket by the key's first digit, then by its second), with a direct-indexed leaf
/// where his scans a short list, because a coordinate is a small bounded digit and a word is not.
/// It replaced a <c>Dictionary&lt;VoxelPos, int&gt;</c> — an insert a cell and four probes, the one
/// place the solve hashed a position — after M10d-3 measured the flood at 57–68% of the solve on
/// the load test. <see cref="FluidGrid"/> stores water the same way; this is the flood catching up.
/// </para>
/// <para>
/// <b>Nothing is cleared between floods.</b> A slot's arrays are reused by whatever chunk column
/// takes the slot next, and an entry counts only when its stamp is this flood's generation, so a
/// <see cref="Clear"/> is a dictionary clear over the slots and one increment. The memory his
/// version is greedy with is bounded here by the columns one flood touches: 8 KiB a slot, kept
/// for the simulation's life, a few megabytes on the load test's widest pass.
/// </para>
/// <para>
/// <b>The last slot is remembered</b>, because a flood's neighbour probes are almost always in the
/// chunk column of the cell being popped; the dictionary is asked only on a change of column.
/// </para>
/// </remarks>
internal sealed class FloodIndex
{
    private const int Side = VoxelConstants.ChunkSize;
    private const int Log2 = VoxelConstants.ChunkSizeLog2;
    private const int LocalMask = Side - 1;
    private const int LeafSize = Side * Side;

    /// <summary>
    /// The first layer, flat (2026-09-26, Matthew's CSharp-Lookuptables <c>FlatTableBuckets</c>):
    /// a window of <see cref="Window"/>² chunk columns anchored on the flood's first, the column's
    /// offset in it the array index — no hashing on a crossing. A flood reaching past the window
    /// (a sheet reaching more than 256 m from where its flood began) falls back to the dictionary, so the
    /// window is a speed, never a limit. Cleared by the stamp, as the leaves are.
    /// </summary>
    private const int Window = 64;

    private readonly int[] _flatSlot = new int[Window * Window];
    private readonly int[] _flatStamp = new int[Window * Window];
    private int _anchorX;
    private int _anchorZ;
    private bool _anchored;

    private readonly Dictionary<long, int> _slots = [];
    private int[][] _records = new int[16][];
    private int[][] _stamps = new int[16][];
    private int _slotCount;
    private int _stamp = 1;
    private long _lastKey = long.MinValue;
    private int _lastSlot = -1;
    private int _y = int.MinValue;

    /// <summary>Chunk columns this simulation has ever held a slot for at once — the memory's high-water mark.</summary>
    public int SlotsAllocated { get; private set; }

    /// <summary>Forgets every entry, in time independent of how many there were.</summary>
    public void Clear()
    {
        _slots.Clear();
        _anchored = false;
        _slotCount = 0;
        _lastKey = long.MinValue;
        _lastSlot = -1;
        _y = int.MinValue;
        if (++_stamp == int.MaxValue)
        {
            // Two billion floods; every stale stamp could collide with the wrapped counter.
            for (int s = 0; s < SlotsAllocated; s++)
            {
                Array.Clear(_stamps[s]);
            }

            Array.Clear(_flatStamp);

            _stamp = 1;
        }
    }

    public bool TryGet(VoxelPos cell, out int record)
    {
        CheckY(cell);
        int slot = FindSlot(cell, create: false);
        int leaf = Leaf(cell);
        if (slot >= 0 && _stamps[slot][leaf] == _stamp)
        {
            record = _records[slot][leaf];
            return true;
        }

        record = -1;
        return false;
    }

    public bool Contains(VoxelPos cell) => TryGet(cell, out _);

    public void Add(VoxelPos cell, int record)
    {
        CheckY(cell);
        int slot = FindSlot(cell, create: true);
        int leaf = Leaf(cell);
        _stamps[slot][leaf] = _stamp;
        _records[slot][leaf] = record;
    }

    private static int Leaf(VoxelPos cell) => ((cell.X & LocalMask) << Log2) | (cell.Z & LocalMask);

    private int FindSlot(VoxelPos cell, bool create)
    {
        long key = ((long)(cell.X >> Log2) << 32) | (uint)(cell.Z >> Log2);
        if (key == _lastKey)
        {
            return _lastSlot;
        }

        int cx = cell.X >> Log2, cz = cell.Z >> Log2;
        if (!_anchored)
        {
            _anchorX = cx - (Window / 2);
            _anchorZ = cz - (Window / 2);
            _anchored = true;
        }

        int wx = cx - _anchorX, wz = cz - _anchorZ;
        bool flat = (uint)wx < Window && (uint)wz < Window;
        int flatIndex = flat ? (wx * Window) + wz : -1;

        int slot;
        if (flat ? _flatStamp[flatIndex] == _stamp : _slots.TryGetValue(key, out slot))
        {
            slot = flat ? _flatSlot[flatIndex] : _slots[key];
        }
        else
        {
            if (!create)
            {
                return -1;
            }

            slot = _slotCount++;
            if (slot == SlotsAllocated)
            {
                if (slot == _records.Length)
                {
                    Array.Resize(ref _records, slot * 2);
                    Array.Resize(ref _stamps, slot * 2);
                }

                _records[slot] = new int[LeafSize];
                _stamps[slot] = new int[LeafSize];
                SlotsAllocated++;
            }

            if (flat)
            {
                _flatSlot[flatIndex] = slot;
                _flatStamp[flatIndex] = _stamp;
            }
            else
            {
                _slots[key] = slot;
            }
        }

        _lastKey = key;
        _lastSlot = slot;
        return slot;
    }

    /// <summary>One flood is one Y; an entry at another would alias a cell of this layer.</summary>
    [Conditional("DEBUG")]
    private void CheckY(VoxelPos cell)
    {
        if (_y == int.MinValue)
        {
            _y = cell.Y;
        }
        else if (cell.Y != _y)
        {
            throw new InvalidOperationException(
                $"The flood index holds Y {_y} and was asked about {cell}; one flood is one Y.");
        }
    }
}
