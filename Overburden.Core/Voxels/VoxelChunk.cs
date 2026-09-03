using System.Runtime.InteropServices;
using Overburden.Core.Content;

namespace Overburden.Core.Voxels;

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
    /// <para>
    /// <b>Never called from an edit.</b> Detecting that a material has left a chunk needs a
    /// full 32,768-voxel scan, and a pick swing must not pay for that.
    /// </para>
    /// <para>
    /// <b>And not called from serialisation either, which this comment claimed until M6a
    /// measured it.</b> "This runs on serialisation" stood here from M1 with nothing calling it
    /// at all — a prediction rather than a decision, and the prediction was wrong. Measured over
    /// 2,048 real chunks in Release: on a carved chunk it costs <b>13× the whole write</b> and
    /// saves <b>zero</b> bytes, because carving air into a chunk that already holds air leaves the
    /// palette untouched. On the case it actually exists for — a chunk that has lost the last
    /// voxel of some material — it costs <b>10× the write</b> and saves <b>7.5%</b> of a blob that
    /// already compresses 22×. So <see cref="Persistence.ChunkBlob.Write"/> defaults to leaving
    /// storage alone.
    /// </para>
    /// <para>
    /// It stays public because it is still the right thing for a caller that explicitly wants
    /// the smallest representation, and because it is the A/B arm the bench measures.
    /// <b>Revisit trigger:</b> a chunk edited across enough sessions to accumulate dead palette
    /// entries and widen its packing — the palette never shrinks on its own, and reaching 256
    /// entries converts it to 64 KB of direct storage. Unreachable with five shipped materials;
    /// a heavily modded world is where it would first bite.
    /// </para>
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

    // ---------------------------------------------------------------------------------
    // Serialisation surface. M6a's ChunkBlob is the only caller.
    //
    // Internal rather than public, and the representation stays private behind it, for the
    // same reason the packing widths are: promoting storage internals to public API just to
    // be reachable is how a compression device becomes a contract. What ChunkBlob needs is
    // the packed form verbatim — it is 4× smaller than the voxels before compression and
    // needs no per-voxel loop on either side — so it gets read-only views and validating
    // factories, and the *format* knowledge stays in ChunkBlob where it belongs.
    // ---------------------------------------------------------------------------------

    /// <summary>The palette, in index order. Empty unless <see cref="StorageKind.Palettised"/>.</summary>
    /// <remarks>
    /// <b>These are world numeric IDs, not palette indices</b>, and writing them as anything
    /// else is the mistake this whole surface exists to make hard. A blob whose palette held
    /// 0,1,2,… would round-trip flawlessly within one session and decode to the wrong
    /// materials the moment the mod set changed — which is precisely the failure
    /// non-negotiable 2 exists to prevent, arriving through the one door that bypasses it.
    /// </remarks>
    internal ReadOnlySpan<int> PaletteEntries =>
        _palette is null ? default : CollectionsMarshal.AsSpan(_palette);

    /// <summary>The bit-packed indices. Empty unless <see cref="StorageKind.Palettised"/>.</summary>
    internal ReadOnlySpan<ulong> PackedWords => _words is null ? default : _words;

    /// <summary>The raw voxels. Empty unless <see cref="StorageKind.Direct"/>.</summary>
    internal ReadOnlySpan<ushort> DirectVoxels => _direct is null ? default : _direct;

    /// <summary>Packing width in bits. Meaningful only when <see cref="StorageKind.Palettised"/>.</summary>
    internal int BitsPerIndex => _bitsPerIndex;

    /// <summary>Words a palettised chunk needs at <paramref name="bits"/> per index.</summary>
    internal static int WordsFor(int bits) => WordCount(bits);

    /// <summary>Rebuilds a palettised chunk from a blob, validating every field it was given.</summary>
    /// <remarks>
    /// <para>
    /// <b>Everything here arrives from a file, so nothing is trusted.</b> The expensive check is
    /// the last one: a width wide enough for the palette can still <em>represent</em> indices
    /// past its end — a palette of 5 at 4 bits leaves 5..15 addressable — and reading one is an
    /// <see cref="IndexOutOfRangeException"/> from inside the mesher on a worker thread, which is
    /// the same latent shape M3c recorded for <see cref="Content.MaterialTable"/> indexing raw.
    /// Scanning 32,768 indices costs microseconds against a disk read, and it runs on every load
    /// rather than on a rarely-taken branch.
    /// </para>
    /// <para>
    /// The width is required to be <em>sufficient</em> rather than exactly
    /// <see cref="BitsForPaletteSize"/>. Today the two always agree, because
    /// <see cref="Widen"/> repacks to the exact width on every growth — but pinning the format
    /// to an internal invariant means a later change to when repacking happens would start
    /// refusing worlds this version wrote.
    /// </para>
    /// </remarks>
    internal static VoxelChunk FromPalettised(
        ReadOnlySpan<int> palette, int bitsPerIndex, ReadOnlySpan<ulong> words)
    {
        if (palette.Length is < 1 or > MaxPaletteSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(palette), palette.Length,
                $"Palette holds {palette.Length} entries; 1..{MaxPaletteSize} is representable.");
        }

        if (bitsPerIndex is not (1 or 2 or 4 or 8))
        {
            throw new ArgumentOutOfRangeException(
                nameof(bitsPerIndex), bitsPerIndex, "Packing width must be 1, 2, 4 or 8 bits.");
        }

        if (bitsPerIndex < BitsForPaletteSize(palette.Length))
        {
            throw new ArgumentOutOfRangeException(
                nameof(bitsPerIndex), bitsPerIndex,
                $"{bitsPerIndex} bits cannot index a palette of {palette.Length}.");
        }

        if (words.Length != WordCount(bitsPerIndex))
        {
            throw new ArgumentException(
                $"Expected {WordCount(bitsPerIndex)} words at {bitsPerIndex} bits, got {words.Length}.",
                nameof(words));
        }

        foreach (int material in palette)
        {
            ValidateMaterial(material);
        }

        var chunk = new VoxelChunk();
        chunk._kind = StorageKind.Palettised;
        chunk._palette = [.. palette];
        chunk._paletteLookup = new Dictionary<int, int>(palette.Length);
        for (int i = 0; i < palette.Length; i++)
        {
            // A duplicate entry is not corrupt storage — every index still resolves — but it
            // would desync the lookup from the list and make the next write reuse the wrong
            // slot. Refused rather than tolerated, because it cannot arise from a real chunk.
            if (!chunk._paletteLookup.TryAdd(palette[i], i))
            {
                throw new ArgumentException(
                    $"Palette entry {palette[i]} appears twice, at {chunk._paletteLookup[palette[i]]} and {i}.",
                    nameof(palette));
            }
        }

        chunk.SetWidth(bitsPerIndex);
        chunk._words = words.ToArray();

        for (int i = 0; i < VoxelConstants.ChunkVolume; i++)
        {
            int word = i >> chunk._entriesPerWordLog2;
            int shift = (i & chunk._entriesPerWordMask) << chunk._bitsLog2;
            int index = (int)((chunk._words[word] >> shift) & chunk._valueMask);

            if (index >= palette.Length)
            {
                throw new ArgumentException(
                    $"Voxel {i} holds palette index {index}, past the {palette.Length}-entry palette.",
                    nameof(words));
            }
        }

        return chunk;
    }

    /// <summary>Rebuilds a direct-storage chunk from a blob.</summary>
    internal static VoxelChunk FromDirect(ReadOnlySpan<ushort> voxels)
    {
        if (voxels.Length != VoxelConstants.ChunkVolume)
        {
            throw new ArgumentException(
                $"Expected {VoxelConstants.ChunkVolume} voxels, got {voxels.Length}.", nameof(voxels));
        }

        var chunk = new VoxelChunk
        {
            _kind = StorageKind.Direct,
            _direct = voxels.ToArray(),
        };

        return chunk;
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
