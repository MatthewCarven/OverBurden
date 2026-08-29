using MinerCraft.Core.Content;

namespace MinerCraft.Core.Voxels;

/// <summary>
/// Storage for one 32³ chunk of numeric material IDs.
/// </summary>
/// <remarks>
/// <para>32,768 voxels per chunk and thousands of chunks resident, so a raw
/// <c>ushort[]</c> would be 64 KB each and well past 300 MB for a modest view distance.
/// At 1 m voxels raw storage is simply affordable — godot_voxel does exactly that — but
/// 0.25 m voxels are 64× denser, which is what buys the palette tier below.</para>
///
/// <para>Three representations, promoted as content demands:</para>
/// <list type="number">
///   <item><b>Uniform</b> — the whole chunk is one material, stored as a single int with
///   <b>no allocation at all</b>. Most of the world: air above the surface, stone well
///   below it.</item>
///   <item><b>Palettised</b> — a per-chunk palette plus bit-packed indices at 1, 2, 4 or
///   8 bits. The common case is ≤16 materials, so 4 bits and 16 KB.</item>
///   <item><b>Direct</b> — raw <c>ushort</c> per voxel once the palette passes 256. A
///   safety valve that should essentially never trigger.</item>
/// </list>
///
/// <para>This palette is <b>per-chunk and unstable</b> — purely a compression device,
/// rebuilt freely. It is emphatically not the world's stable ID table from
/// <see cref="Registry{T}"/>; conflating the two would be a nasty bug, hence the
/// different names.</para>
///
/// <para><b>Not thread-safe.</b> Writes happen on the sim thread; mesh jobs work from a
/// padded copy taken on that same thread, which is what lets them run without locks.</para>
/// </remarks>
public sealed class VoxelChunk
{
    public enum StorageKind : byte
    {
        Uniform,
        Palettised,
        Direct,
    }

    /// <summary>
    /// Above this the palette stops paying for itself: a 16-bit palette index costs the
    /// same as storing the material ID outright, so the tier would be pure overhead.
    /// </summary>
    private const int MaxPaletteSize = 256;

    private StorageKind _kind = StorageKind.Uniform;

    // Uniform
    private int _uniformValue;

    // Palettised
    private ulong[]? _words;
    private List<int>? _palette;
    private Dictionary<int, int>? _paletteLookup;
    private int _bitsPerIndex;
    private int _bitsLog2;
    private int _entriesPerWordLog2;
    private int _entriesPerWordMask;
    private ulong _valueMask;

    // Direct
    private ushort[]? _direct;

    /// <summary>A chunk of nothing but <paramref name="materialId"/>, allocating nothing.</summary>
    public VoxelChunk(int materialId = MaterialIds.AirNumericId)
    {
        ValidateMaterial(materialId);
        _uniformValue = materialId;
    }

    public StorageKind Kind => _kind;

    public bool IsUniform => _kind == StorageKind.Uniform;

    /// <summary>Meaningful only when <see cref="IsUniform"/>.</summary>
    public int UniformValue => _uniformValue;

    /// <summary>Distinct materials currently addressable. 1 when uniform.</summary>
    public int PaletteSize => _kind switch
    {
        StorageKind.Uniform => 1,
        StorageKind.Palettised => _palette!.Count,
        _ => -1, // direct storage has no palette
    };

    /// <summary>Payload bytes, excluding object headers. Used by the memory tests.</summary>
    public int EstimatedBytes => _kind switch
    {
        StorageKind.Uniform => 0,
        StorageKind.Palettised => _words!.Length * sizeof(ulong) + _palette!.Count * sizeof(int),
        _ => _direct!.Length * sizeof(ushort),
    };

    public int Get(int localX, int localY, int localZ) =>
        GetAt(VoxelPos.Index(localX, localY, localZ));

    public int GetAt(int index)
    {
        ValidateIndex(index);

        switch (_kind)
        {
            case StorageKind.Uniform:
                // No memory touched at all — this is the fast path that most of the world
                // takes, and the reason the uniform tier exists.
                return _uniformValue;

            case StorageKind.Palettised:
            {
                int word = index >> _entriesPerWordLog2;
                int shift = (index & _entriesPerWordMask) << _bitsLog2;
                int paletteIndex = (int)((_words![word] >> shift) & _valueMask);
                return _palette![paletteIndex];
            }

            default:
                return _direct![index];
        }
    }

    public void Set(int localX, int localY, int localZ, int materialId) =>
        SetAt(VoxelPos.Index(localX, localY, localZ), materialId);

    public void SetAt(int index, int materialId)
    {
        ValidateIndex(index);
        ValidateMaterial(materialId);

        switch (_kind)
        {
            case StorageKind.Uniform:
                // Writing the value a uniform chunk already holds is free, which matters:
                // worldgen fills large volumes with what is already there.
                if (materialId == _uniformValue) return;
                PromoteToPalettised(materialId);
                WritePalettised(index, materialId);
                return;

            case StorageKind.Palettised:
                WritePalettised(index, materialId);
                return;

            default:
                _direct![index] = (ushort)materialId;
                return;
        }
    }

    /// <summary>An independent copy sharing no mutable state with this one.</summary>
    /// <remarks>
    /// <para>
    /// This is what makes editing safe while a worker is reading. A mesh job captures a
    /// <see cref="VoxelChunk"/> <em>reference</em> on the sim thread and copies out of it
    /// later on its own thread; an edit that mutated that instance in place would not merely
    /// show the worker a stale voxel, it would rewrite the representation underneath it —
    /// <see cref="Set"/> can promote uniform to palettised to direct, swapping the backing
    /// arrays and the width fields together. Cloning instead, and installing the clone,
    /// leaves the worker holding something nobody will ever write to again.
    /// </para>
    /// <para>
    /// Roughly 2 KB for a typical palettised chunk and nothing at all for a uniform one, so
    /// the cost is a gen0 allocation per chunk per edit-and-remesh cycle. See
    /// docs/M3-PLAN.md for why this beats a lock or a cancel-and-retry.
    /// </para>
    /// </remarks>
    public VoxelChunk Clone()
    {
        var copy = new VoxelChunk
        {
            _kind = _kind,
            _uniformValue = _uniformValue,
            _bitsPerIndex = _bitsPerIndex,
            _bitsLog2 = _bitsLog2,
            _entriesPerWordLog2 = _entriesPerWordLog2,
            _entriesPerWordMask = _entriesPerWordMask,
            _valueMask = _valueMask,
            _words = _words?.ToArray(),
            _direct = _direct?.ToArray(),
            _palette = _palette is null ? null : [.. _palette],
        };

        // Rebuilt rather than copied: it is a pure index of _palette, and reconstructing it
        // cannot drift out of step with the list the way a shallow copy of both could.
        if (copy._palette is not null)
        {
            copy._paletteLookup = new Dictionary<int, int>(copy._palette.Count);
            for (int i = 0; i < copy._palette.Count; i++)
            {
                copy._paletteLookup[copy._palette[i]] = i;
            }
        }

        return copy;
    }

    /// <summary>Resets the whole chunk to one material, releasing any allocation.</summary>
    public void Fill(int materialId)
    {
        ValidateMaterial(materialId);

        _kind = StorageKind.Uniform;
        _uniformValue = materialId;
        _words = null;
        _palette = null;
        _paletteLookup = null;
        _direct = null;
    }

    /// <summary>Copies every voxel into <paramref name="destination"/> in index order.</summary>
    /// <remarks>
    /// The bulk read the mesher's padded copy is built from. Uniform chunks fill without
    /// ever touching packed storage.
    /// </remarks>
    public void CopyTo(Span<int> destination)
    {
        if (destination.Length < VoxelConstants.ChunkVolume)
        {
            throw new ArgumentException(
                $"Destination holds {destination.Length}, need {VoxelConstants.ChunkVolume}.",
                nameof(destination));
        }

        if (_kind == StorageKind.Uniform)
        {
            destination[..VoxelConstants.ChunkVolume].Fill(_uniformValue);
            return;
        }

        for (int i = 0; i < VoxelConstants.ChunkVolume; i++)
        {
            destination[i] = GetAt(i);
        }
    }

    /// <summary>
    /// Rebuilds storage in the smallest representation that fits what is actually present:
    /// drops unused palette entries, narrows the bit width, and demotes to uniform where
    /// only one material survives.
    /// </summary>
    /// <remarks>
    /// <b>Never called from an edit.</b> Detecting that a material has left a chunk needs a
    /// full 32,768-voxel scan, and a pick swing must not pay for that. This runs on
    /// serialisation, or when something explicitly asks.
    /// </remarks>
    public void Compact()
    {
        if (_kind == StorageKind.Uniform) return;

        var used = new Dictionary<int, int>();
        var order = new List<int>();

        for (int i = 0; i < VoxelConstants.ChunkVolume; i++)
        {
            int material = GetAt(i);
            if (used.TryAdd(material, order.Count))
            {
                order.Add(material);
            }
        }

        if (order.Count == 1)
        {
            Fill(order[0]);
            return;
        }

        if (order.Count > MaxPaletteSize)
        {
            // Still too varied for a palette; Direct is already the right answer.
            if (_kind != StorageKind.Direct) ConvertToDirect();
            return;
        }

        // Snapshot before overwriting the representation we are reading through.
        var materials = new int[VoxelConstants.ChunkVolume];
        CopyTo(materials);

        _kind = StorageKind.Palettised;
        _direct = null;
        _palette = order;
        _paletteLookup = used;
        SetWidth(BitsForPaletteSize(order.Count));
        _words = new ulong[WordCount(_bitsPerIndex)];

        for (int i = 0; i < VoxelConstants.ChunkVolume; i++)
        {
            WriteIndexRaw(i, used[materials[i]]);
        }
    }

    private void PromoteToPalettised(int newMaterial)
    {
        _palette = [_uniformValue, newMaterial];
        _paletteLookup = new Dictionary<int, int> { [_uniformValue] = 0, [newMaterial] = 1 };
        SetWidth(1);

        // Zero-initialised means every voxel reads as palette entry 0, which is the value
        // the chunk was already uniformly holding. No fill pass needed.
        _words = new ulong[WordCount(1)];
        _kind = StorageKind.Palettised;
    }

    private void WritePalettised(int index, int materialId)
    {
        if (!_paletteLookup!.TryGetValue(materialId, out int paletteIndex))
        {
            paletteIndex = _palette!.Count;

            if (paletteIndex >= MaxPaletteSize)
            {
                ConvertToDirect();
                _direct![index] = (ushort)materialId;
                return;
            }

            _palette.Add(materialId);
            _paletteLookup[materialId] = paletteIndex;

            int needed = BitsForPaletteSize(_palette.Count);
            if (needed != _bitsPerIndex)
            {
                Widen(needed);
            }
        }

        WriteIndexRaw(index, paletteIndex);
    }

    /// <summary>Repacks every entry at a wider index size. Rare, so simplicity wins.</summary>
    private void Widen(int newBits)
    {
        var oldWords = _words!;
        int oldBitsLog2 = _bitsLog2;
        int oldEntriesPerWordLog2 = _entriesPerWordLog2;
        int oldEntriesPerWordMask = _entriesPerWordMask;
        ulong oldValueMask = _valueMask;

        SetWidth(newBits);
        _words = new ulong[WordCount(newBits)];

        for (int i = 0; i < VoxelConstants.ChunkVolume; i++)
        {
            int word = i >> oldEntriesPerWordLog2;
            int shift = (i & oldEntriesPerWordMask) << oldBitsLog2;
            int value = (int)((oldWords[word] >> shift) & oldValueMask);
            WriteIndexRaw(i, value);
        }
    }

    private void ConvertToDirect()
    {
        var direct = new ushort[VoxelConstants.ChunkVolume];
        for (int i = 0; i < VoxelConstants.ChunkVolume; i++)
        {
            direct[i] = (ushort)GetAt(i);
        }

        _direct = direct;
        _kind = StorageKind.Direct;
        _words = null;
        _palette = null;
        _paletteLookup = null;
    }

    private void WriteIndexRaw(int index, int paletteIndex)
    {
        int word = index >> _entriesPerWordLog2;
        int shift = (index & _entriesPerWordMask) << _bitsLog2;
        _words![word] = (_words[word] & ~(_valueMask << shift)) | ((ulong)paletteIndex << shift);
    }

    /// <summary>
    /// Sets the packing width and its derived shifts and masks.
    /// </summary>
    /// <remarks>
    /// Indices are <b>padded to word boundaries, never straddling two words</b>. Every
    /// width we use (1, 2, 4, 8) divides 64 exactly, so padding wastes literally nothing,
    /// and in exchange every read is one load, one shift and one mask — no branch, no
    /// second word fetch. This array is read tens of millions of times a second by the
    /// mesher, which is the whole argument.
    /// </remarks>
    private void SetWidth(int bits)
    {
        _bitsPerIndex = bits;
        _bitsLog2 = System.Numerics.BitOperations.TrailingZeroCount(bits);
        _entriesPerWordLog2 = 6 - _bitsLog2;
        _entriesPerWordMask = (1 << _entriesPerWordLog2) - 1;
        _valueMask = (1UL << bits) - 1;
    }

    private static int WordCount(int bits) => VoxelConstants.ChunkVolume / (64 / bits);

    /// <summary>Narrowest power-of-two width that indexes a palette of this size.</summary>
    internal static int BitsForPaletteSize(int paletteSize) => paletteSize switch
    {
        <= 2 => 1,
        <= 4 => 2,
        <= 16 => 4,
        <= MaxPaletteSize => 8,
        _ => throw new ArgumentOutOfRangeException(
            nameof(paletteSize),
            $"Palette of {paletteSize} exceeds {MaxPaletteSize}; use direct storage."),
    };

    private static void ValidateIndex(int index)
    {
        if ((uint)index >= VoxelConstants.ChunkVolume)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index), $"Voxel index {index} outside 0..{VoxelConstants.ChunkVolume - 1}.");
        }
    }

    private static void ValidateMaterial(int materialId)
    {
        if ((uint)materialId > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(materialId),
                $"Material ID {materialId} outside 0..{ushort.MaxValue}. Direct storage is 16-bit.");
        }
    }

    public override string ToString() => _kind switch
    {
        StorageKind.Uniform => $"Chunk(uniform {_uniformValue})",
        StorageKind.Palettised => $"Chunk(palette {_palette!.Count} @ {_bitsPerIndex}b, {EstimatedBytes} B)",
        _ => $"Chunk(direct, {EstimatedBytes} B)",
    };
}
