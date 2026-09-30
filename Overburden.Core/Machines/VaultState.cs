using System.Buffers.Binary;

namespace Overburden.Core.Machines;

/// <summary>Where a vault is in its life (IDEAL-MACHINES-PLAN decisions 27, 28 and 34).</summary>
public enum VaultPhase : byte
{
    /// <summary>A store: it takes deposits and gives withdrawals, and its bill is paid.</summary>
    Holding,

    /// <summary>Its bill went unpaid: the fuse is burning. A boundary that pays in full disarms it.</summary>
    Fusing,

    /// <summary>The fuse burned out: its contents are owed to the world, and nothing stops the dump (34).</summary>
    Dumping,
}

/// <summary>
/// What a vault holds and where it is in its life — decoded from the row's bytes, changed, and
/// written straight back. Never kept live between those (decision 20: the codec bytes are the state).
/// </summary>
public sealed class VaultContents
{
    /// <summary>Voxels held, by material, in the world's numeric IDs; never a zero entry.</summary>
    public SortedDictionary<int, long> Voxels { get; } = [];

    /// <summary>Water held, in millilitres. Zero until IM-4 gives a vault its water doors.</summary>
    public long WaterMillilitres { get; set; }

    public VaultPhase Phase { get; set; }

    /// <summary>What is left of a burning fuse, in boundary milliseconds; zero unless <see cref="VaultPhase.Fusing"/>.</summary>
    public int FuseMillisLeft { get; set; }

    /// <summary>
    /// The vault's own charge, in millijoules (IM-3b): what pays its bill when its channel's pool
    /// can't, refilled from the channel's spare up to a minute of its bill as it stands. Part of the
    /// pool's <c>stored</c> through <see cref="MachineSet.ChargeOf"/>, so it enters and leaves with
    /// the row as a cell's charge does. Never the bank's: the window cannot draw it.
    /// </summary>
    public long OwnChargeMillijoules { get; set; }

    /// <summary>
    /// A vault a fall took while it held something (decision 17): no body, the row kept, its contents
    /// owed, removed only once empty.
    /// </summary>
    public bool Wreck { get; set; }

    /// <summary>Every voxel held, over every material.</summary>
    public long TotalVoxels
    {
        get
        {
            long total = 0;
            foreach (long voxels in Voxels.Values) total += voxels;
            return total;
        }
    }

    /// <summary>The inside it fills: blocks and water share it, a voxel taking 15.625 L (decision 15).</summary>
    public long UsedMillilitres => (TotalVoxels * MachineDefinition.VoxelMillilitres) + WaterMillilitres;

    public bool IsEmpty => Voxels.Count == 0 && WaterMillilitres == 0;

    /// <summary>
    /// Whether the contents are owed to the world rather than held in the store: a wreck's, or a
    /// dumping vault's (the store identity's <c>owed</c>, decision 19).
    /// </summary>
    public bool Owes => Wreck || Phase == VaultPhase.Dumping;

    public long VoxelsOf(int materialId) => Voxels.GetValueOrDefault(materialId);

    /// <summary>Adds <paramref name="voxels"/> of a material; no bound here, the caller asked about room.</summary>
    public void Add(int materialId, long voxels)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(voxels);
        if (voxels > 0)
        {
            Voxels[materialId] = VoxelsOf(materialId) + voxels;
        }
    }

    /// <summary>Takes up to <paramref name="voxels"/> of a material; returns what came out.</summary>
    public long Take(int materialId, long voxels)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(voxels);
        long held = VoxelsOf(materialId);
        long taken = Math.Min(held, voxels);
        if (taken == held)
        {
            Voxels.Remove(materialId);
        }
        else
        {
            Voxels[materialId] = held - taken;
        }

        return taken;
    }
}

/// <summary>
/// A vault row's state as the bytes its <see cref="MachineRow.KindState"/> holds — one codec that
/// owns the whole blob (IDEAL-MACHINES-PLAN decision 20; IM-3's pass, decision 8).
/// </summary>
/// <remarks>
/// <para>
/// <b>Strict, unlike the cell's and the panel's.</b> Those read an unknown version as empty, which
/// costs a panel one boundary's sample. A vault read as empty would be a vault the pick takes apart
/// with its contents still inside, so an unknown version is kept opaque and inert: <see cref="TryRead"/>
/// says no, and every rule treats the row as holding something. A row with no state at all is a
/// vault just placed, and is empty.
/// </para>
/// <para>
/// Version 1: the version; a flags byte (bit 0, wreck); the phase; the fuse's milliseconds left, an
/// int32; the own charge in millijoules, an int64 (IM-3b, with no version of its own: no vault's
/// bytes were saved before it); the water in millilitres, an int64; the count of materials, an
/// int32; then each material's numeric ID (int32) and voxels (int64), sorted by ID, none zero.
/// Little-endian, as every blob here.
/// </para>
/// </remarks>
public static class VaultState
{
    /// <summary>The version this build writes.</summary>
    public const byte Version = 1;

    private const byte WreckFlag = 1;
    /// <summary>The bytes before the first material entry.</summary>
    internal const int HeaderBytes = 1 + 1 + 1 + sizeof(int) + sizeof(long) + sizeof(long) + sizeof(int);

    /// <summary>One material entry's bytes: its ID and its voxels.</summary>
    internal const int EntryBytes = sizeof(int) + sizeof(long);

    /// <summary>
    /// Reads the row's contents. False for a version this build does not know or bytes that do not
    /// parse — the row is then kept as it is and reads as holding something.
    /// </summary>
    public static bool TryRead(MachineRow row, out VaultContents contents)
    {
        ArgumentNullException.ThrowIfNull(row);
        return TryRead(row.KindState, out contents);
    }

    /// <inheritdoc cref="TryRead(MachineRow, out VaultContents)"/>
    public static bool TryRead(ReadOnlySpan<byte> state, out VaultContents contents)
    {
        contents = new VaultContents();
        if (state.Length == 0)
        {
            return true;
        }

        if (state[0] != Version || state.Length < HeaderBytes)
        {
            return false;
        }

        byte flags = state[1];
        byte phase = state[2];
        int fuse = BinaryPrimitives.ReadInt32LittleEndian(state[3..]);
        long own = BinaryPrimitives.ReadInt64LittleEndian(state[7..]);
        long water = BinaryPrimitives.ReadInt64LittleEndian(state[15..]);
        int count = BinaryPrimitives.ReadInt32LittleEndian(state[23..]);
        if ((flags & ~WreckFlag) != 0 || phase > (byte)VaultPhase.Dumping || fuse < 0 || own < 0 || water < 0
            || count < 0 || state.Length != HeaderBytes + ((long)count * EntryBytes))
        {
            return false;
        }

        contents.Wreck = (flags & WreckFlag) != 0;
        contents.Phase = (VaultPhase)phase;
        contents.FuseMillisLeft = fuse;
        contents.OwnChargeMillijoules = own;
        contents.WaterMillilitres = water;
        int previous = int.MinValue;
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> entry = state.Slice(HeaderBytes + (i * EntryBytes), EntryBytes);
            int material = BinaryPrimitives.ReadInt32LittleEndian(entry);
            long voxels = BinaryPrimitives.ReadInt64LittleEndian(entry[sizeof(int)..]);
            if ((i > 0 && material <= previous) || voxels <= 0)
            {
                contents = new VaultContents();
                return false;
            }

            contents.Voxels[material] = voxels;
            previous = material;
        }

        return true;
    }

    /// <summary>The bytes for <paramref name="contents"/>, in this version.</summary>
    public static byte[] Encode(VaultContents contents)
    {
        ArgumentNullException.ThrowIfNull(contents);
        var state = new byte[HeaderBytes + (contents.Voxels.Count * EntryBytes)];
        state[0] = Version;
        state[1] = contents.Wreck ? WreckFlag : (byte)0;
        state[2] = (byte)contents.Phase;
        BinaryPrimitives.WriteInt32LittleEndian(state.AsSpan(3), contents.FuseMillisLeft);
        BinaryPrimitives.WriteInt64LittleEndian(state.AsSpan(7), contents.OwnChargeMillijoules);
        BinaryPrimitives.WriteInt64LittleEndian(state.AsSpan(15), contents.WaterMillilitres);
        BinaryPrimitives.WriteInt32LittleEndian(state.AsSpan(23), contents.Voxels.Count);
        int offset = HeaderBytes;
        foreach ((int material, long voxels) in contents.Voxels)
        {
            BinaryPrimitives.WriteInt32LittleEndian(state.AsSpan(offset), material);
            BinaryPrimitives.WriteInt64LittleEndian(state.AsSpan(offset + sizeof(int)), voxels);
            offset += EntryBytes;
        }

        return state;
    }

    /// <summary>Writes <paramref name="contents"/> onto the row, replacing whatever it held.</summary>
    public static void Write(MachineRow row, VaultContents contents)
    {
        ArgumentNullException.ThrowIfNull(row);
        row.KindState = Encode(contents);
    }
}
