using Overburden.Core.Building;
using Overburden.Core.Machines;
using Overburden.Core.Persistence;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// <see cref="MachineRowBlob"/>: the row's bytes and back, and every refusal the reader makes.
/// </summary>
/// <remarks>
/// The reader validates the shape and nothing about the meaning — a port's bounds are
/// <see cref="MachineSet.Restore"/>'s business (<see cref="MachineSetTests"/>). Same oracle as the
/// chunk blob's: the row that comes back is the row that went in, and a blob the reader cannot
/// account for is refused rather than repaired.
/// </remarks>
public class MachineRowBlobTests
{
    private static readonly BrushCell Cell = new(3, -2, 7);

    private static MachineRow Row(byte[]? state = null) => new(
        Cell,
        materialId: 9,
        BrushFacing.NegativeZ,
        [new("channel", 5), new("weight", 42), new("z-last", -7)],
        state);

    [Fact]
    public void ARowRoundTrips()
    {
        MachineRow row = Row([1, 200, 3]);

        MachineRow back = MachineRowBlob.Read(MachineRowBlob.Write(row), row.Origin, row.MaterialId);

        Assert.Equal(Cell, back.Cell);
        Assert.Equal(9, back.MaterialId);
        Assert.Equal(BrushFacing.NegativeZ, back.Facing);
        Assert.Equal(row.Ports, back.Ports);
        Assert.Equal(new byte[] { 1, 200, 3 }, back.KindState);
    }

    [Fact]
    public void ARowWithNoPortsAndNoStateRoundTrips()
    {
        var row = new MachineRow(Cell, 1, BrushFacing.PositiveX);

        MachineRow back = MachineRowBlob.Read(MachineRowBlob.Write(row), row.Origin, 1);

        Assert.Empty(back.Ports);
        Assert.Empty(back.KindState);
    }

    [Fact]
    public void TheSameRowWritesTheSameBytesWhateverOrderItsPortsWereGiven()
    {
        // Ports are held by name in ordinal order, so a mod reordering its declarations — or a
        // dictionary enumerating differently — cannot change what the file holds.
        var a = new MachineRow(Cell, 1, BrushFacing.PositiveX, [new("a", 1), new("b", 2)]);
        var b = new MachineRow(Cell, 1, BrushFacing.PositiveX, [new("b", 2), new("a", 1)]);

        Assert.Equal(MachineRowBlob.Write(a), MachineRowBlob.Write(b));
    }

    [Fact]
    public void TheBlobCarriesItsOriginAndRefusesTheWrongKey()
    {
        byte[] blob = MachineRowBlob.Write(Row());

        BlobFormatException error = Assert.Throws<BlobFormatException>(
            () => MachineRowBlob.Read(blob, new VoxelPos(0, 0, 0), 9));

        Assert.Contains("wrong key", error.Message);
    }

    [Fact]
    public void AnOriginOffTheBrushLatticeIsRefused()
    {
        // A body is one brush cell, so a row keyed off the lattice names no cell a body could
        // fill. The blob itself carries the origin, so the check is on what the blob says.
        byte[] blob = MachineRowBlob.Write(Row());
        var offLattice = new VoxelPos(Cell.OriginVoxel.X + 1, Cell.OriginVoxel.Y, Cell.OriginVoxel.Z);
        Patch(blob, BlobFormat.FrameBytes, offLattice.X);

        BlobFormatException error = Assert.Throws<BlobFormatException>(
            () => MachineRowBlob.Read(blob, offLattice, 9));

        Assert.Contains("lattice", error.Message);
    }

    [Fact]
    public void ATruncatedBlobIsRefusedByTheFrame()
    {
        byte[] blob = MachineRowBlob.Write(Row([1, 2, 3, 4]));

        for (int cut = 1; cut < blob.Length; cut++)
        {
            byte[] shorter = blob[..^cut];
            Assert.Throws<BlobFormatException>(() => MachineRowBlob.Read(shorter, Cell.OriginVoxel, 9));
        }
    }

    [Fact]
    public void AnAppendedBlobIsRefusedByTheFrame()
    {
        byte[] blob = MachineRowBlob.Write(Row());
        byte[] longer = [.. blob, 0];

        Assert.Throws<BlobFormatException>(() => MachineRowBlob.Read(longer, Cell.OriginVoxel, 9));
    }

    [Fact]
    public void BytesAfterTheKindStateInsideAConsistentFrameAreRefused()
    {
        // Appending to the blob trips the frame's stored-length check first, so the battery's
        // "trailing bytes accepted" recipe was an identity against that fixture. This one builds a
        // payload with a byte after the kind state and a frame that declares it honestly, so the
        // only guard left to fire is the reader's own.
        byte[] blob = HandBuilt(ports: [("a", 1)], trailing: 1);

        BlobFormatException error = Assert.Throws<BlobFormatException>(
            () => MachineRowBlob.Read(blob, Cell.OriginVoxel, 1));

        Assert.Contains("follow the kind state", error.Message);
    }

    [Fact]
    public void AnUnknownVersionIsRefused()
    {
        byte[] blob = MachineRowBlob.Write(Row());
        blob[0] = (byte)(MachineRowBlob.Version + 1);

        BlobFormatException error = Assert.Throws<BlobFormatException>(
            () => MachineRowBlob.Read(blob, Cell.OriginVoxel, 9));

        Assert.Contains("version", error.Message);
    }

    [Fact]
    public void AFacingThatIsNotAFacingIsRefused()
    {
        byte[] blob = MachineRowBlob.Write(new MachineRow(Cell, 1, BrushFacing.PositiveX));
        Assert.False(IsCompressed(blob));
        blob[PayloadStart] = 200;

        BlobFormatException error = Assert.Throws<BlobFormatException>(
            () => MachineRowBlob.Read(blob, Cell.OriginVoxel, 1));

        Assert.Contains("facing", error.Message);
    }

    [Fact]
    public void APortNamedTwiceIsRefused()
    {
        // Two ports, both called "a": hand-built, because the writer cannot produce it.
        byte[] blob = HandBuilt(ports: [("a", 1), ("a", 2)]);

        BlobFormatException error = Assert.Throws<BlobFormatException>(
            () => MachineRowBlob.Read(blob, Cell.OriginVoxel, 1));

        Assert.Contains("twice", error.Message);
    }

    [Fact]
    public void APortNameOutsideTheCharsetIsRefused()
    {
        byte[] blob = HandBuilt(ports: [("Channel", 1)]);

        BlobFormatException error = Assert.Throws<BlobFormatException>(
            () => MachineRowBlob.Read(blob, Cell.OriginVoxel, 1));

        Assert.Contains("port name", error.Message);
    }

    [Fact]
    public void MoreThanSixteenPortsIsRefused()
    {
        byte[] blob = HandBuilt(ports: [.. Enumerable.Range(0, 17).Select(i => ($"p{i}", (long)i))]);

        BlobFormatException error = Assert.Throws<BlobFormatException>(
            () => MachineRowBlob.Read(blob, Cell.OriginVoxel, 1));

        Assert.Contains("at most", error.Message);
    }

    [Fact]
    public void ANegativeMaterialIsRefused() =>
        Assert.Throws<BlobFormatException>(
            () => MachineRowBlob.Read(MachineRowBlob.Write(Row()), Cell.OriginVoxel, -1));

    [Fact]
    public void TheKindStateIsCarriedByteForByteWhateverItHolds()
    {
        // An unknown kind's state — a later build's, or a removed mod's — survives a round trip
        // untouched, which is what lets a row outlive the build that wrote it.
        var state = new byte[300];
        new Random(7).NextBytes(state);

        MachineRow back = MachineRowBlob.Read(MachineRowBlob.Write(Row(state)), Cell.OriginVoxel, 9);

        Assert.Equal(state, back.KindState);
    }

    [Fact]
    public void ARowRefusesWhatItsBlobWouldRefuse()
    {
        // The constructor is the writer's gate: a name the reader would throw on never reaches a
        // blob, and a seventeenth port never does either.
        Assert.Throws<ArgumentException>(
            () => new MachineRow(Cell, 1, BrushFacing.PositiveX, [new("Bad", 1)]));
        Assert.Throws<ArgumentException>(
            () => new MachineRow(
                Cell, 1, BrushFacing.PositiveX,
                Enumerable.Range(0, 17).Select(i => new KeyValuePair<string, long>($"p{i}", i))));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MachineRow(Cell, -1, BrushFacing.PositiveX));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MachineRow(Cell, 1, (BrushFacing)9));
    }

    // ---------------------------------------------------------------------------------------------

    private const int PayloadStart = BlobFormat.FrameBytes + (3 * sizeof(int));

    private static bool IsCompressed(byte[] blob) => (blob[1] & 0x01) != 0;

    private static void Patch(byte[] blob, int offset, int value) =>
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(offset), value);

    /// <summary>A blob assembled by hand — the reader checked by something that is not the writer.</summary>
    private static byte[] HandBuilt((string Name, long Value)[] ports, int trailing = 0)
    {
        var payload = new List<byte> { (byte)BrushFacing.PositiveX, (byte)ports.Length };
        foreach ((string name, long value) in ports)
        {
            byte[] nameBytes = System.Text.Encoding.UTF8.GetBytes(name);
            payload.Add((byte)nameBytes.Length);
            payload.AddRange(nameBytes);
            payload.AddRange(BitConverter.GetBytes(value));
        }

        payload.AddRange(BitConverter.GetBytes(0));
        payload.AddRange(new byte[trailing]);

        var blob = new byte[PayloadStart + payload.Count];
        blob[0] = MachineRowBlob.Version;
        blob[1] = 0;
        Patch(blob, 2, payload.Count);
        Patch(blob, 6, payload.Count);
        VoxelPos origin = Cell.OriginVoxel;
        Patch(blob, BlobFormat.FrameBytes, origin.X);
        Patch(blob, BlobFormat.FrameBytes + 4, origin.Y);
        Patch(blob, BlobFormat.FrameBytes + 8, origin.Z);
        payload.CopyTo(blob, PayloadStart);
        return blob;
    }
}
