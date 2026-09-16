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

    /// <summary>A table whose material 4 declares the relay's two ports, so SetPort has bounds to clamp to.</summary>
    /// <summary>Material 5: a placed storage of 5,000 J, and material 6: a pack (M10c).</summary>
    private const int CellMaterial = 5, PackMaterial = 6;

    private const long CellCapacity = 5_000L * 1000L;

    private static readonly MachineDefinition CellDefinition = new()
    {
        Kind = MachineKind.Storage,
        CapacityMillijoules = CellCapacity,
        Links = [new MachineLink("charge", MachineMedium.Power, MachineRole.Sink, MachineLink.ChannelVia, null, "channel")],
        Ports = [new PortDefinition("channel", 0, 0, 15)],
    };

    private static readonly MachineDefinition PackDefinition = new()
    {
        Kind = MachineKind.Storage,
        Carried = true,
        CapacityMillijoules = 3_600_000L * 1000L,
        RechargeWatts = 1000,
        Links = [new MachineLink("charge", MachineMedium.Power, MachineRole.Sink, MachineLink.ChannelVia, null, "channel")],
        Ports = [new PortDefinition("channel", 0, 0, 15)],
    };

    private static readonly Content.MaterialTable DeclaringTable = Content.MaterialTable.ForTesting(
        isOpaque: [false, true, true, true, true, true, true],
        isFluid: [false, false, false, false, false, false, false],
        textureLayer: [0, 1, 2, 3, 4, 5, 6],
        hardness: [0f, 1f, 1f, 1f, 3f, 3f, 1f],
        machine: [null, null, null, null, new MachineDefinition
        {
            Kind = MachineKind.Outlet,
            Links = [new MachineLink("water", MachineMedium.Fluid, MachineRole.Sink, MachineLink.ChannelVia, null, "channel")],
            Ports = RelayPorts,
        }, CellDefinition, PackDefinition],
        placeable: [true, true, true, true, true, true, false]);

    private static MachineRow Cell(BrushCell cell, long chargeMillijoules)
    {
        var row = new MachineRow(cell, CellMaterial, BrushFacing.PositiveX, [new("channel", 0)]);
        StorageState.Write(row, chargeMillijoules);
        return row;
    }

    // ---------------------------------------------------------------- the charge (M10c, decision 3)

    [Fact]
    public void AStorageRowsChargeRoundTripsTheBlobAndTheSnapshot()
    {
        MachineRow row = Cell(CellA, 1_234_567);
        Assert.Equal(1_234_567L, StorageState.Read(row));
        Assert.Equal(9, row.KindState.Length);
        Assert.Equal(StorageState.Version, row.KindState[0]);

        MachineRow back = MachineRowBlob.Read(MachineRowBlob.Write(row), row.Origin, row.MaterialId);
        Assert.Equal(1_234_567L, StorageState.Read(back));

        var set = new MachineSet(DeclaringTable);
        set.Add(row);
        var restored = new MachineSet(DeclaringTable);
        RestoreReport report = restored.Restore(set.Snapshot(), id => DeclaringTable.Machine(id)?.Ports);
        Assert.Equal(0, report.Clamped);
        Assert.True(restored.TryGet(CellA, out MachineRow again));
        Assert.Equal(1_234_567L, StorageState.Read(again));
        Assert.Equal(1_234_567L, restored.ChargeOf(again));
    }

    [Fact]
    public void AnEmptyOrForeignKindStateReadsAsNoChargeAndIsRewrittenInThisVersion()
    {
        var empty = new MachineRow(CellA, CellMaterial, BrushFacing.PositiveX, [new("channel", 0)]);
        Assert.Equal(0L, StorageState.Read(empty));

        var foreign = new MachineRow(CellB, CellMaterial, BrushFacing.PositiveX, [new("channel", 0)], [7, 1, 2, 3, 4, 5, 6, 7, 8]);
        Assert.Equal(0L, StorageState.Read(foreign));

        StorageState.Write(foreign, 42);
        Assert.Equal(42L, StorageState.Read(foreign));
        Assert.Equal(StorageState.Version, foreign.KindState[0]);

        // In place once the bytes are this version's: the same array, rewritten.
        byte[] before = foreign.KindState;
        StorageState.Write(foreign, 43);
        Assert.Same(before, foreign.KindState);
        Assert.Equal(43L, StorageState.Read(foreign));
    }

    [Fact]
    public void AChargeOverCapacityRestoresClampedAndCounted()
    {
        // Decision 21: a row read from disk is a caller, clamped to the material's bounds and
        // counted, never refused — through the set's own table, which is what knows a capacity.
        MachineRow row = Cell(CellA, CellCapacity + 1);
        List<StoredMachine> stored = [Stored(row)];

        var set = new MachineSet(DeclaringTable);
        RestoreReport report = set.Restore(stored, id => DeclaringTable.Machine(id)?.Ports);

        Assert.Equal(1, report.Clamped);
        Assert.True(set.TryGet(CellA, out MachineRow back));
        Assert.Equal(CellCapacity, StorageState.Read(back));
        Assert.Equal(CellCapacity, set.StoredMillijoulesEntering);

        // A set with no table clamps nothing: it has nothing to clamp against.
        var bare = new MachineSet();
        Assert.Equal(0, bare.Restore(stored, _ => null).Clamped);
        Assert.True(bare.TryGet(CellA, out MachineRow kept));
        Assert.Equal(CellCapacity + 1, StorageState.Read(kept));
        Assert.Equal(0L, bare.ChargeOf(kept));
    }

    [Fact]
    public void APackGivesToItsCapacityAndTakesToEmptyAndCountsBoth()
    {
        // The tick caps what it gives before calling Give, so the pack's own bound is belt and
        // braces there — pinned here on the object, because the header (c-3) and the tool will
        // call it too.
        var pack = new EquippedPack(PackMaterial, PackDefinition, chargeMillijoules: 100, channel: 3);
        Assert.Equal(100L, pack.InitialMillijoules);
        Assert.Equal(3, pack.Channel);

        Assert.Equal(PackDefinition.CapacityMillijoules - 100, pack.Give(long.MaxValue / 2));
        Assert.Equal(PackDefinition.CapacityMillijoules, pack.ChargeMillijoules);
        Assert.Equal(0L, pack.Give(1));

        Assert.Equal(PackDefinition.CapacityMillijoules, pack.Take(long.MaxValue / 2));
        Assert.Equal(0L, pack.ChargeMillijoules);
        Assert.Equal(0L, pack.Take(1));
        Assert.Equal(PackDefinition.CapacityMillijoules - 100, pack.GivenMillijoules);
        Assert.Equal(PackDefinition.CapacityMillijoules, pack.TakenMillijoules);

        // Clamped at construction too, and only a carried storage is a pack.
        Assert.Equal(PackDefinition.CapacityMillijoules, new EquippedPack(PackMaterial, PackDefinition, long.MaxValue, 0).ChargeMillijoules);
        Assert.Equal(15, new EquippedPack(PackMaterial, PackDefinition, 0, 99).Channel);
        Assert.Throws<ArgumentException>(() => new EquippedPack(CellMaterial, CellDefinition, 0, 0));
    }

    [Fact]
    public void ChargeEnteringAndLeavingAreCountedOnEveryDoor()
    {
        var set = new MachineSet(DeclaringTable);
        set.Add(Cell(CellA, 100));
        set.Add(Cell(CellB, 200));
        set.Add(Relay(CellC));
        Assert.Equal(300L, set.StoredMillijoulesEntering);
        Assert.Equal(0L, set.StoredMillijoulesLeaving);

        Assert.True(set.Remove(CellA));
        Assert.Equal(100L, set.StoredMillijoulesLeaving);

        // A restore over a live set: what stood leaves, what the table holds enters, and the
        // difference is the charge on the rows now.
        set.Restore(set.Snapshot(), id => DeclaringTable.Machine(id)?.Ports);
        Assert.Equal(500L, set.StoredMillijoulesEntering);
        Assert.Equal(300L, set.StoredMillijoulesLeaving);
        Assert.True(set.TryGet(CellB, out MachineRow b));
        Assert.Equal(200L, set.StoredMillijoulesEntering - set.StoredMillijoulesLeaving);
        Assert.Equal(200L, set.ChargeOf(b));

        // The pack's material is carried: a row of it in the set counts no charge, because the
        // pool never reads it.
        var pack = new MachineRow(CellA, PackMaterial, BrushFacing.PositiveX, [new("channel", 0)]);
        StorageState.Write(pack, 999);
        set.Add(pack);
        Assert.Equal(500L, set.StoredMillijoulesEntering);
        Assert.Equal(0L, set.ChargeOf(pack));
    }

    // ---------------------------------------------------------------- SetPort (M10b, decision 4)

    [Fact]
    public void SetPortInBoundsLandsTheValueAndMovesTheVersion()
    {
        var set = new MachineSet(DeclaringTable);
        set.Add(Relay(CellA));
        long before = set.Version;

        PortWrite write = set.SetPort(CellA, "channel", 7);

        Assert.Equal(PortWriteResult.Set, write.Result);
        Assert.Equal(7L, write.Value);
        Assert.True(write.Landed);
        Assert.True(set.TryGet(CellA, out MachineRow row));
        Assert.Equal(7L, row.Ports["channel"]);
        Assert.Equal(before + 1, set.Version);
        Assert.Equal(1L, set.PortWrites);
        Assert.Equal(0L, set.PortWritesClamped);
    }

    [Fact]
    public void SetPortOutOfBoundsLandsTheBoundAndCountsTheClamp()
    {
        var set = new MachineSet(DeclaringTable);
        set.Add(Relay(CellA));

        PortWrite high = set.SetPort(CellA, "channel", 99);
        PortWrite low = set.SetPort(CellA, "weight", -5);

        Assert.Equal(PortWriteResult.Clamped, high.Result);
        Assert.Equal(15L, high.Value);
        Assert.Equal(PortWriteResult.Clamped, low.Result);
        Assert.Equal(1L, low.Value);
        Assert.True(set.TryGet(CellA, out MachineRow row));
        Assert.Equal(15L, row.Ports["channel"]);
        Assert.Equal(1L, row.Ports["weight"]);
        Assert.Equal(2L, set.PortWrites);
        Assert.Equal(2L, set.PortWritesClamped);
    }

    [Fact]
    public void SetPortRefusesAnUndeclaredNameAnAbsentRowAndAHole()
    {
        var set = new MachineSet(DeclaringTable);
        set.Add(Relay(CellA));
        set.Add(new MachineRow(CellB, 3, BrushFacing.PositiveX, [new("channel", 0)]));
        long before = set.Version;

        Assert.Equal(PortWriteResult.NoSuchPort, set.SetPort(CellA, "rate", 5).Result);
        Assert.Equal(PortWriteResult.NoSuchRow, set.SetPort(CellC, "channel", 5).Result);

        // Material 3 declares no machine block: a hole's row refuses every write, because there
        // is nothing to clamp against and an unbounded write is what the restore exists to stop.
        Assert.Equal(PortWriteResult.NoSuchPort, set.SetPort(CellB, "channel", 5).Result);

        // And a set built without a table refuses the same way.
        var bare = new MachineSet();
        bare.Add(Relay(CellA));
        Assert.Equal(PortWriteResult.NoSuchPort, bare.SetPort(CellA, "channel", 5).Result);

        Assert.Equal(before, set.Version);
        Assert.Equal(0L, set.PortWrites);
        Assert.True(set.TryGet(CellB, out MachineRow hole));
        Assert.Equal(0L, hole.Ports["channel"]);
    }

    [Fact]
    public void AnUnchangedWriteIsCountedAndMovesNothing()
    {
        // A panel re-sending the value it shows costs no rebuild.
        var set = new MachineSet(DeclaringTable);
        set.Add(Relay(CellA, channel: 3));
        long before = set.Version;

        PortWrite write = set.SetPort(CellA, "channel", 3);

        Assert.Equal(PortWriteResult.Set, write.Result);
        Assert.Equal(before, set.Version);
        Assert.Equal(1L, set.PortWrites);

        // Clamped onto the value already there: counted as a clamp, still no move.
        set.SetPort(CellA, "channel", 3);
        set.Add(Relay(CellB, channel: 15));
        long afterAdd = set.Version;
        Assert.Equal(PortWriteResult.Clamped, set.SetPort(CellB, "channel", 40).Result);
        Assert.Equal(afterAdd, set.Version);
    }

    [Fact]
    public void AWrittenPortRoundTripsASnapshotAndARestore()
    {
        // M10b's quit oracle: a port write is on the row, and the row is the table.
        var set = new MachineSet(DeclaringTable);
        set.Add(Relay(CellA));
        set.SetPort(CellA, "channel", 9);
        set.SetPort(CellA, "weight", 250);

        var restored = new MachineSet(DeclaringTable);
        RestoreReport report = restored.Restore(set.Snapshot(), id => id == 4 ? RelayPorts : null);

        Assert.Equal(0, report.Clamped + report.Dropped + report.Defaulted);
        Assert.True(restored.TryGet(CellA, out MachineRow row));
        Assert.Equal(9L, row.Ports["channel"]);
        Assert.Equal(100L, row.Ports["weight"]);
    }

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
