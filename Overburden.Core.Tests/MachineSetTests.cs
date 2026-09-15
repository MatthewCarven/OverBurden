using Overburden.Core.Building;
using Overburden.Core.Machines;
using Overburden.Core.Persistence;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// <see cref="MachineSet"/> as M10a-1 builds it: rows only, keyed by cell and indexed by chunk,
/// snapshotted whole and restored against the ports a material declares.
/// </summary>
public class MachineSetTests
{
    private static readonly PortDefinition[] RelayPorts =
    [
        new("channel", 0, 0, 15),
        new("weight", 1, 1, 100),
    ];

    private static readonly BrushCell CellA = new(0, 0, 0);
    private static readonly BrushCell CellB = new(7, 3, 7);   // same chunk as A: 8 cells a chunk
    private static readonly BrushCell CellC = new(8, 0, 0);   // the next chunk over

    private static MachineRow Relay(BrushCell cell, long channel = 0, long weight = 1) =>
        new(cell, 4, BrushFacing.PositiveX, [new("channel", channel), new("weight", weight)]);

    [Fact]
    public void RowsAreFoundByCellAndByChunk()
    {
        var set = new MachineSet();
        set.Add(Relay(CellA));
        set.Add(Relay(CellB));
        set.Add(Relay(CellC));

        Assert.Equal(3, set.Count);
        Assert.True(set.TryGet(CellB, out MachineRow row));
        Assert.Equal(CellB, row.Cell);
        Assert.Equal(2, set.RowsIn(CellA.OriginVoxel.Chunk).Count);
        Assert.Single(set.RowsIn(CellC.OriginVoxel.Chunk));
        Assert.True(set.HasRowsIn(CellC.OriginVoxel.Chunk));
        Assert.False(set.HasRowsIn(new ChunkPos(5, 5, 5)));
        Assert.Empty(set.RowsIn(new ChunkPos(5, 5, 5)));
    }

    [Fact]
    public void RemovingTheLastRowInAChunkForgetsTheChunk()
    {
        // The hooks' fast path asks HasRowsIn 32,768 times a bite; a chunk whose rows are all
        // gone must answer no in one lookup rather than hand back an empty list.
        var set = new MachineSet();
        set.Add(Relay(CellA));
        set.Add(Relay(CellB));

        Assert.True(set.Remove(CellA));
        Assert.True(set.HasRowsIn(CellA.OriginVoxel.Chunk));
        Assert.True(set.Remove(CellB));
        Assert.False(set.HasRowsIn(CellA.OriginVoxel.Chunk));
        Assert.False(set.Remove(CellB));
        Assert.Equal(0, set.Count);
    }

    [Fact]
    public void OneCellHoldsOneBody()
    {
        var set = new MachineSet();
        set.Add(Relay(CellA));

        Assert.Throws<InvalidOperationException>(() => set.Add(Relay(CellA)));
        Assert.Equal(1, set.Count);
    }

    [Fact]
    public void TheVersionMovesOnEveryChange()
    {
        var set = new MachineSet();
        long v0 = set.Version;
        set.Add(Relay(CellA));
        long v1 = set.Version;
        set.Remove(CellA);
        long v2 = set.Version;

        Assert.True(v1 > v0);
        Assert.True(v2 > v1);
    }

    [Fact]
    public void ASnapshotIsInOriginOrderWhateverOrderTheRowsWereAdded()
    {
        // The inventory's Entries() rule: the same set writes the same table twice, so an
        // autosave of an unchanged world is byte-identical to the last one.
        var forward = new MachineSet();
        forward.Add(Relay(CellA));
        forward.Add(Relay(CellB));
        forward.Add(Relay(CellC));

        var backward = new MachineSet();
        backward.Add(Relay(CellC));
        backward.Add(Relay(CellB));
        backward.Add(Relay(CellA));

        List<StoredMachine> a = forward.Snapshot();
        List<StoredMachine> b = backward.Snapshot();

        Assert.Equal(a, b);
        Assert.Equal([CellA.OriginVoxel, CellB.OriginVoxel, CellC.OriginVoxel], a.Select(row => row.Origin));
    }

    [Fact]
    public void ASnapshotRestoresToTheSameSet()
    {
        var set = new MachineSet();
        set.Add(Relay(CellA, channel: 3, weight: 50));
        set.Add(Relay(CellC, channel: 15));

        var restored = new MachineSet();
        RestoreReport report = restored.Restore(set.Snapshot(), _ => RelayPorts);

        Assert.Equal(new RestoreReport(2, 0, 0, 0, 0), report);
        Assert.Equal(restored.Snapshot(), set.Snapshot());
        Assert.True(restored.TryGet(CellA, out MachineRow a));
        Assert.Equal(3L, a.Ports["channel"]);
        Assert.Equal(50L, a.Ports["weight"]);
    }

    [Fact]
    public void RestoreReplacesRatherThanMerges()
    {
        var set = new MachineSet();
        set.Add(Relay(CellA));

        set.Restore([], _ => RelayPorts);

        Assert.Equal(0, set.Count);
        Assert.False(set.HasRowsIn(CellA.OriginVoxel.Chunk));
    }

    [Fact]
    public void AValueOutsideItsBoundsIsClampedAndCounted()
    {
        // A row is a caller (decision 21): a channel of 99 from a file becomes 15, the row is
        // kept, and the open's line says so. Refusing the row would refuse the world over a
        // number the programmer can retype.
        var row = new MachineRow(CellA, 4, BrushFacing.PositiveX, [new("channel", 99), new("weight", -5)]);

        var set = new MachineSet();
        RestoreReport report = set.Restore([Stored(row)], _ => RelayPorts);

        Assert.Equal(2, report.Clamped);
        Assert.True(set.TryGet(CellA, out MachineRow back));
        Assert.Equal(15L, back.Ports["channel"]);
        Assert.Equal(1L, back.Ports["weight"]);
    }

    [Fact]
    public void ADeclaredPortTheRowLacksTakesItsDefault()
    {
        var row = new MachineRow(CellA, 4, BrushFacing.PositiveX, [new("channel", 2)]);

        var set = new MachineSet();
        RestoreReport report = set.Restore([Stored(row)], _ => RelayPorts);

        Assert.Equal(1, report.Defaulted);
        Assert.True(set.TryGet(CellA, out MachineRow back));
        Assert.Equal(2L, back.Ports["channel"]);
        Assert.Equal(1L, back.Ports["weight"]);
    }

    [Fact]
    public void APortNoDeclarationNamesIsDroppedAndCounted()
    {
        var row = new MachineRow(
            CellA, 4, BrushFacing.PositiveX, [new("channel", 2), new("weight", 1), new("legacy", 7)]);

        var set = new MachineSet();
        RestoreReport report = set.Restore([Stored(row)], _ => RelayPorts);

        Assert.Equal(1, report.Dropped);
        Assert.True(set.TryGet(CellA, out MachineRow back));
        Assert.False(back.TryGetPort("legacy", out _));
        Assert.Equal(2, back.Ports.Count);
    }

    [Fact]
    public void ARowOfUnknownKindIsKeptVerbatimAndCounted()
    {
        // A removed mod's machine — its material a hole, its ports undeclared — sits inert with
        // everything it had, the way its material hole stays a hole. Nothing is dropped, nothing
        // is clamped, and the next snapshot writes it back unchanged.
        var row = new MachineRow(
            CellA, 4, BrushFacing.NegativeZ, [new("channel", 99), new("odd", -1)], [3, 1, 4]);
        List<StoredMachine> stored = [Stored(row)];

        var set = new MachineSet();
        RestoreReport report = set.Restore(stored, _ => null);

        Assert.Equal(new RestoreReport(1, 1, 0, 0, 0), report);
        Assert.Equal(stored, set.Snapshot());
    }

    [Fact]
    public void TwoStoredRowsInOneCellRefuseTheTable()
    {
        StoredMachine one = Stored(Relay(CellA));

        var set = new MachineSet();

        Assert.Throws<BlobFormatException>(() => set.Restore([one, one], _ => RelayPorts));
    }

    [Fact]
    public void ACorruptRowRefusesTheTableRatherThanBeingRepaired()
    {
        StoredMachine good = Stored(Relay(CellA));
        var bad = new StoredMachine(CellC.OriginVoxel, 4, good.Blob[..^3]);

        var set = new MachineSet();

        Assert.Throws<BlobFormatException>(() => set.Restore([good, bad], _ => RelayPorts));
    }

    [Fact]
    public void APortDefinitionRefusesWhatItCouldNotClampTo()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PortDefinition("a", 5, 0, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PortDefinition("a", 0, 1, 0));
        Assert.Throws<ArgumentException>(() => new PortDefinition("A", 0, 0, 1));
        Assert.Throws<ArgumentException>(() => new PortDefinition("", 0, 0, 1));
        Assert.Throws<ArgumentException>(() => new PortDefinition(new string('a', 33), 0, 0, 1));
        Assert.Equal(15L, new PortDefinition("channel", 0, 0, 15).Clamp(99));
        Assert.Equal(0L, new PortDefinition("channel", 0, 0, 15).Clamp(-3));
    }

    private static StoredMachine Stored(MachineRow row) =>
        new(row.Origin, row.MaterialId, MachineRowBlob.Write(row));
}
