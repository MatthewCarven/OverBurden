using System.Buffers.Binary;

namespace Overburden.Core.Machines;

/// <summary>
/// A storage row's charge, as the bytes its <see cref="MachineRow.KindState"/> holds (M10c).
/// </summary>
/// <remarks>
/// <para>
/// <b>The kind's state carries its own version byte</b> — a-1's rule for the row blob, so a
/// later part can add a field with no schema bump. Version 1 is the byte and an int64 of
/// millijoules; a row whose state is empty, or carries a version this build does not know, reads
/// as empty and is rewritten in this version the first time the tick writes it. Nothing here
/// parses inline in the tick, so there is one definition of what the bytes mean.
/// </para>
/// <para>
/// Millijoules, because the boundary's arithmetic is integer end to end (ladder decision 10):
/// a watt over 500 ms is 500 mJ, and a joule a litre is a millijoule a millilitre.
/// </para>
/// </remarks>
public static class StorageState
{
    /// <summary>The version this build writes.</summary>
    public const byte Version = 1;

    private const int Bytes = 1 + sizeof(long);

    /// <summary>The charge on the row, in millijoules; zero for an empty or unknown state.</summary>
    public static long Read(MachineRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        byte[] state = row.KindState;
        if (state.Length < Bytes || state[0] != Version)
        {
            return 0;
        }

        long charge = BinaryPrimitives.ReadInt64LittleEndian(state.AsSpan(1));
        return charge < 0 ? 0 : charge;
    }

    /// <summary>Writes the charge, in place when the row already holds this version's bytes.</summary>
    public static void Write(MachineRow row, long millijoules)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentOutOfRangeException.ThrowIfNegative(millijoules);

        byte[] state = row.KindState;
        if (state.Length != Bytes || state[0] != Version)
        {
            state = new byte[Bytes];
            state[0] = Version;
            row.KindState = state;
        }

        BinaryPrimitives.WriteInt64LittleEndian(state.AsSpan(1), millijoules);
    }
}

/// <summary>
/// A converter's last-sampled openness — how many of the sixteen voxel columns over its body are
/// open to the sky — as the bytes its <see cref="MachineRow.KindState"/> holds (M10c, decision 2).
/// </summary>
/// <remarks>
/// Kept on the row so that a panel in a chunk column nobody has loaded generates from what it
/// last saw rather than nothing (a row ticks unloaded when its inputs are row-local, ladder
/// decision 4); sampled afresh at every boundary the column is there. A row with no state, or a
/// version this build does not know, reads as dark: a panel nobody has looked at since it was
/// placed is one the next boundary samples anyway.
/// </remarks>
public static class ConverterState
{
    /// <summary>The version this build writes.</summary>
    public const byte Version = 1;

    /// <summary>Columns over a body: a brush cell is four by four.</summary>
    public const int Columns = Building.BrushCell.Voxels * Building.BrushCell.Voxels;

    private const int Bytes = 2;

    /// <summary>The open columns last sampled, 0..16.</summary>
    public static int ReadOpenColumns(MachineRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        byte[] state = row.KindState;
        if (state.Length < Bytes || state[0] != Version)
        {
            return 0;
        }

        return Math.Min((int)state[1], Columns);
    }

    /// <summary>Writes the sampled openness, in place when the row already holds this version's bytes.</summary>
    public static void WriteOpenColumns(MachineRow row, int open)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentOutOfRangeException.ThrowIfNegative(open);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(open, Columns);

        byte[] state = row.KindState;
        if (state.Length != Bytes || state[0] != Version)
        {
            state = new byte[Bytes];
            state[0] = Version;
            row.KindState = state;
        }

        state[1] = (byte)open;
    }
}
