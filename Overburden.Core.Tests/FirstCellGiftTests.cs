using Overburden.Core.Building;
using Overburden.Core.Content;
using Overburden.Core.Editing;
using Overburden.Core.Fluid;
using Overburden.Core.Inventory;
using Overburden.Core.Machines;
using Overburden.Core.Mining;
using Overburden.Core.Persistence;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// IM-6c: the first cell's 1 GJ (IDEAL-MACHINES-PLAN decision 53; IM-6's pass, decision 23) — its own door, its own term in
/// the pool identity, once, and every round trip in the pass's table.
/// </summary>
public class FirstCellGiftTests
{
    private const int Air = 0, Stone = 1, CellMaterial = 2, SmallCell = 3, Crate = 4;
    private const int Cell64 = BrushCell.Voxels * BrushCell.Voxels * BrushCell.Voxels;
    private const float Pick = 3.5f;
    private const long GJ = FirstCellGift.GiftMillijoules;

    private static readonly PortDefinition Channel = new("channel", 0, 0, 15);

    private static MachineDefinition StorageOf(long capacityMillijoules) => new()
    {
        Kind = MachineKind.Storage,
        CapacityMillijoules = capacityMillijoules,
        Links = [new MachineLink("charge", MachineMedium.Power, MachineRole.Sink, MachineLink.ChannelVia, null, "channel")],
        Ports = [Channel],
    };

    private static readonly MachineDefinition CrateDefinition = new()
    {
        Kind = MachineKind.Vault,
        InsideMillilitres = MachineDefinition.MillilitresPerCubicMetre,
    };

    // The vacuum cell's 5 GJ, and a small cell under the gift so the clamp shows.
    private static readonly MaterialTable Materials = MaterialTable.ForTesting(
        //          air    stone  cell   small  crate
        isOpaque: [false, true, true, true, true],
        isFluid: [false, false, false, false, false],
        textureLayer: [0, 1, 2, 3, 4],
        hardness: [0f, 3f, 3f, 3f, 1f],
        machine: [null, null, StorageOf(5 * GJ), StorageOf(GJ / 2), CrateDefinition],
        unit: [default, default, MaterialUnit.Cells, MaterialUnit.Cells, MaterialUnit.Cells]);

    private static readonly BrushCell A = new(0, 0, 0), B = new(2, 0, 0), C = new(4, 0, 0);

    private sealed class HookedWorld : IVoxelEditor
    {
        private readonly TestWorld _inner = new();

        public MachineSet Set { get; } = new(Materials);

        public IEditJournal? Journal { get; set; }

        public bool TryGetMaterial(VoxelPos voxel, out int materialId) => _inner.TryGetMaterial(voxel, out materialId);

        public bool HoldsWater(VoxelPos voxel) => _inner.HoldsWater(voxel);

        public bool SetVoxel(VoxelPos voxel, int materialId)
        {
            int was = _inner[voxel];
            if (was == materialId) return true;
            if (!_inner.SetVoxel(voxel, materialId)) return false;
            Journal?.Wrote(voxel, was, materialId);
            Set.OnVoxelChanged(voxel, materialId);
            return true;
        }
    }

    private sealed class NoWater : IFluidTerrain
    {
        public bool CanHold(VoxelPos voxel) => false;
    }

    private sealed class Rig
    {
        public Rig(WorldMode mode, WorldFlags flags = WorldFlags.None)
        {
            var terrain = new NoWater();
            Tick = new MachineTick(Set, Materials, new FluidSimulation(terrain), terrain);
            World.Journal = History;
            Set.Journal = History;
            Set.Gift = FirstCellGift.For(mode, flags);
        }

        public HookedWorld World { get; } = new();

        public MachineSet Set => World.Set;

        public VolumetricInventory Pocket { get; } = new();

        public MachineTick Tick { get; }

        public EditHistory History { get; } = new();

        public MachineRow Place(BrushCell cell, int material)
        {
            Pocket.Add(material, Cell64);
            Assert.Equal(Cell64, Building.Place.Machine(World, Materials, cell, material, BrushFacing.PositiveX, Set, from: Pocket).Placed);
            History.EndStep();
            Assert.True(Set.TryGet(cell, out MachineRow row));
            return row;
        }

        public long Charge(BrushCell cell) => Set.TryGet(cell, out MachineRow row) ? Set.ChargeOf(row) : -1;

        /// <summary>A pick of one voxel, then the frame's drain dismantling the rest, as a quantum tool takes a machine.</summary>
        public void PickUp(BrushCell cell)
        {
            Assert.Equal(1, Carve.Voxel(World, Materials, cell.VoxelAt(1, 1, 1), Pick, Pocket, Set).Removed);
            Set.Drain(World, Pocket, _ => null);
            History.EndStep();
            Assert.False(Set.TryGet(cell, out _));
        }

        public UndoOutcome Undo() => History.Undo(World, Materials, Pocket, Set).Outcome;

        public void AssertExact() => Assert.True(Tick.PowerExact,
            $"pool: stored {Tick.StoredMillijoules} against entering {Set.StoredMillijoulesEntering} − leaving {Set.StoredMillijoulesLeaving} + granted {Set.StoredMillijoulesGranted}");
    }

    [Fact]
    public void TheFirstCellPlacedInACreativeWorldArrivesHoldingOneGigajouleCountedGranted()
    {
        var rig = new Rig(WorldMode.Creative);

        rig.Place(A, CellMaterial);

        Assert.Equal(GJ, rig.Charge(A));
        Assert.Equal(GJ, rig.Set.StoredMillijoulesGranted);
        Assert.Equal(0, rig.Set.StoredMillijoulesEntering); // its own door, never `entering` beside Alt+P
        Assert.True(rig.Set.Gift!.Given);
        Assert.Equal(A, rig.Set.Gift.GivenTo);
        Assert.Equal(GJ, rig.Set.Gift.GivenMillijoules);
        rig.AssertExact();
    }

    [Fact]
    public void TheGiftIsOnceTheSecondCellArrivesEmpty()
    {
        var rig = new Rig(WorldMode.Creative);
        rig.Place(A, CellMaterial);

        rig.Place(B, CellMaterial);

        Assert.Equal(0, rig.Charge(B));
        Assert.Equal(GJ, rig.Set.StoredMillijoulesGranted);
        rig.AssertExact();
    }

    [Fact]
    public void AMachineThatIsNotACellLeavesTheGiftForTheFirstCell()
    {
        var rig = new Rig(WorldMode.Creative);

        rig.Place(A, Crate);
        Assert.False(rig.Set.Gift!.Given);
        Assert.Equal(0, rig.Set.StoredMillijoulesGranted);

        rig.Place(B, CellMaterial);
        Assert.Equal(GJ, rig.Charge(B));
        rig.AssertExact();
    }

    [Fact]
    public void ASandboxWorldHasNoGiftAndItsCellArrivesEmpty()
    {
        var rig = new Rig(WorldMode.Sandbox);

        rig.Place(A, CellMaterial);

        Assert.Null(rig.Set.Gift);
        Assert.Equal(0, rig.Charge(A));
        Assert.Equal(0, rig.Set.StoredMillijoulesGranted);
        rig.AssertExact();
    }

    [Fact]
    public void ACreativeWorldWhoseHeaderRecordsTheGiftGivesNothingOnReopen()
    {
        var rig = new Rig(WorldMode.Creative, WorldFlags.KitGranted | WorldFlags.FirstCellGranted);

        rig.Place(A, CellMaterial);

        Assert.Equal(0, rig.Charge(A));
        Assert.Equal(0, rig.Set.StoredMillijoulesGranted);
        Assert.Null(rig.Set.Gift!.GivenTo);
    }

    [Fact]
    public void ACellSmallerThanTheGiftIsFilledToItsCapacityAndOnlyThatIsCounted()
    {
        var rig = new Rig(WorldMode.Creative);

        rig.Place(A, SmallCell);

        Assert.Equal(GJ / 2, rig.Charge(A));
        Assert.Equal(GJ / 2, rig.Set.StoredMillijoulesGranted);
        Assert.True(rig.Set.Gift!.Given);
        rig.AssertExact();
    }

    [Fact]
    public void TheBitIsStampedOnlyOnceTheGiftHasGone()
    {
        var rig = new Rig(WorldMode.Creative);
        Assert.Equal(WorldFlags.KitGranted, rig.Set.Gift!.Stamp(WorldFlags.KitGranted));

        rig.Place(A, CellMaterial);

        Assert.Equal(WorldFlags.KitGranted | WorldFlags.FirstCellGranted, rig.Set.Gift.Stamp(WorldFlags.KitGranted));
    }

    [Fact]
    public void PickingTheGiftedCellTakesItsChargeOutThroughLeavingAndTheUndoBringsItBackThroughEntering()
    {
        // The pass's table, row 7: today's rule for any cell.
        var rig = new Rig(WorldMode.Creative);
        rig.Place(A, CellMaterial);

        rig.PickUp(A);
        Assert.Equal(GJ, rig.Set.StoredMillijoulesLeaving);
        rig.AssertExact();

        Assert.Equal(UndoOutcome.Undone, rig.Undo());
        Assert.Equal(GJ, rig.Charge(A));
        Assert.Equal(GJ, rig.Set.StoredMillijoulesEntering);
        Assert.Equal(GJ, rig.Set.StoredMillijoulesGranted); // granted once, never again by the undo's re-add
        rig.AssertExact();
    }

    [Fact]
    public void UndoingTheGiftedPlacementLosesTheGiftForGood()
    {
        // The pass's table, row 8, decision 23's fork: the row and its 1 GJ go out through `leaving`, the gift stays
        // given, and the next cell placed arrives empty.
        var rig = new Rig(WorldMode.Creative);
        rig.Place(A, CellMaterial);

        Assert.Equal(UndoOutcome.Undone, rig.Undo());

        Assert.False(rig.Set.TryGet(A, out _));
        Assert.Equal(GJ, rig.Set.StoredMillijoulesLeaving);
        Assert.True(rig.Set.Gift!.Given);
        rig.AssertExact();

        rig.Place(B, CellMaterial);
        Assert.Equal(0, rig.Charge(B));
        Assert.Equal(GJ, rig.Set.StoredMillijoulesGranted);
        rig.AssertExact();
    }

    [Fact]
    public void ARowRestoredOrReAddedNeverTakesTheGift()
    {
        // Only a placement is offered the gift: a world file's rows at open, and an undo's re-add, come through Add.
        var rig = new Rig(WorldMode.Creative);
        var restored = new MachineRow(C, CellMaterial, BrushFacing.PositiveX, StorageOf(5 * GJ).DefaultPorts());

        rig.Set.Add(restored);

        Assert.Equal(0, rig.Charge(C));
        Assert.False(rig.Set.Gift!.Given);
        rig.AssertExact();
    }

    [Fact]
    public void TheGrantDoorRefusesARowThatIsNotAPlacedCellAndCountsNothing()
    {
        var rig = new Rig(WorldMode.Sandbox);
        rig.Place(A, Crate);

        Assert.Equal(-1, rig.Set.Grant(A, GJ));
        Assert.Equal(-1, rig.Set.Grant(B, GJ));
        Assert.Equal(0, rig.Set.StoredMillijoulesGranted);
    }

    [Fact]
    public void TheGiftsBooksHoldAcrossBoundariesThatChargeAndSpend()
    {
        // The term rides every boundary after it: the tick's own report and the pool identity both stay exact.
        var rig = new Rig(WorldMode.Creative);
        rig.Place(A, CellMaterial);

        for (int boundary = 0; boundary < 20; boundary++)
        {
            Assert.True(rig.Tick.Run().Exact, $"boundary {boundary}");
            rig.AssertExact();
        }
    }
}
