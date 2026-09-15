using Overburden.Core.Building;
using Overburden.Core.Content;
using Overburden.Core.Inventory;
using Overburden.Core.Machines;
using Overburden.Core.Mining;
using Overburden.Core.Voxels;
using static Overburden.Core.Tests.PhysicsTestHelpers;

namespace Overburden.Core.Tests;

/// <summary>
/// The machine set's drain (M10a-2, decisions 3 and 4): the dismantle after a body is damaged —
/// <c>Lost == Carved</c> credits the remainder, <c>Lost &gt; Carved</c> destroys — and the
/// reconcile at a chunk's arrival — absent → rebuilt or returned, partial → destroyed, an orphan →
/// cleared. Hooks queue and mark; only the drain writes.
/// </summary>
public class MachineDrainTests
{
    private const int Pump = 4;
    private const int Rubble = 5;
    private const int Cell64 = BrushCell.Voxels * BrushCell.Voxels * BrushCell.Voxels;
    private const float SteelPick = 3.5f;

    private static readonly MaterialTable Materials = MaterialTable.ForTesting(
        //             air    dirt   water  stone  pump   rubble
        isOpaque: [false, true, false, true, true, true],
        isFluid: [false, false, true, false, false, false],
        textureLayer: [0, 1, 2, 3, 4, 5],
        hardness: [0f, 0.8f, 0f, 3.0f, 3.0f, 0.5f],
        machine: [null, null, null, null, PipelineHarness.PumpDefinition, null],
        unit: [default, default, default, default, MaterialUnit.Cells, default]);

    private static readonly BrushCell Cell = new(2, 3, 2);

    /// <summary>
    /// A world that fires the pipeline's edit hook into the set on every changed write, so a test
    /// drives the same wire Main does: hook on the write, drain on the frame.
    /// </summary>
    private sealed class HookedWorld : IVoxelEditor
    {
        private readonly TestWorld _inner = new();

        public MachineSet Set { get; } = new(Materials);

        public int WritesDuringHooks { get; private set; }

        private bool _inHook;

        public int this[VoxelPos voxel] => _inner[voxel];

        public void Seed(VoxelPos voxel, int material) => _inner.Set(voxel, material);

        public bool TryGetMaterial(VoxelPos voxel, out int materialId) => _inner.TryGetMaterial(voxel, out materialId);

        public bool HoldsWater(VoxelPos voxel) => _inner.HoldsWater(voxel);

        public bool SetVoxel(VoxelPos voxel, int materialId)
        {
            if (_inHook) WritesDuringHooks++;
            if (_inner[voxel] == materialId) return true;
            if (!_inner.SetVoxel(voxel, materialId)) return false;

            _inHook = true;
            Set.OnVoxelChanged(voxel, materialId);
            _inHook = false;
            return true;
        }

        public int BodyVoxelsAt(BrushCell cell, int material)
        {
            int count = 0;
            for (int lz = 0; lz < BrushCell.Voxels; lz++)
            for (int lx = 0; lx < BrushCell.Voxels; lx++)
            for (int ly = 0; ly < BrushCell.Voxels; ly++)
            {
                if (_inner[cell.VoxelAt(lx, ly, lz)] == material) count++;
            }

            return count;
        }
    }

    private static (HookedWorld World, VolumetricInventory Inventory) Placed()
    {
        var world = new HookedWorld();
        var inventory = new VolumetricInventory();
        inventory.Add(Pump, Cell64);
        PlaceResult placed = Place.Machine(world, Materials, Cell, Pump, BrushFacing.PositiveX, world.Set, from: inventory);
        Assert.Equal(Cell64, placed.Placed);
        Assert.Equal(0L, inventory.Voxels(Pump));
        return (world, inventory);
    }

    private static DrainReport Drain(HookedWorld world, VolumetricInventory inventory) =>
        world.Set.Drain(world, inventory, _ => null);

    // ------------------------------------------------------------------------------ dismantle

    [Fact]
    public void ASwingThatTakesSeventeenEndsWithSixtyFourInTheInventory()
    {
        (HookedWorld world, VolumetricInventory inventory) = Placed();

        for (int i = 0; i < 17; i++)
        {
            CarveResult swing = Carve.Voxel(world, Materials, Cell.VoxelAt(i % 4, (i / 4) % 4, i / 16), SteelPick, inventory, world.Set);
            Assert.Equal(1, swing.Removed);
        }

        Assert.Equal(17L, inventory.Voxels(Pump));
        Assert.True(world.Set.TryGet(Cell, out MachineRow row));
        Assert.Equal(17, row.Lost);
        Assert.Equal(17, row.Carved);
        Assert.True(world.Set.HasWorkQueued);

        DrainReport report = Drain(world, inventory);

        Assert.Equal(1, report.Dismantled);
        Assert.Equal(47, report.CreditedVoxels);
        Assert.Equal(Cell64, inventory.Voxels(Pump));
        Assert.False(world.Set.TryGet(Cell, out _));
        Assert.Equal(0, world.BodyVoxelsAt(Cell, Pump));
        Assert.Equal(1, world.Set.Dismantled);
        Assert.Equal(0, world.Set.Destroyed);
        Assert.False(world.Set.HasWorkQueued);
    }

    [Fact]
    public void AChipOfOneVoxelDismantlesTheWholeMachine()
    {
        (HookedWorld world, VolumetricInventory inventory) = Placed();

        Carve.Voxel(world, Materials, Cell.VoxelAt(3, 3, 3), SteelPick, inventory, world.Set);
        Drain(world, inventory);

        Assert.Equal(Cell64, inventory.Voxels(Pump));
        Assert.Equal(0, world.Set.Count);
        Assert.Equal(0, world.BodyVoxelsAt(Cell, Pump));
    }

    [Fact]
    public void TheCubeBiteTakesAllSixtyFourAndTheDrainCreditsNothingMore()
    {
        // Q's bite through Take: 64 carved and credited by the carve, the drain finds nothing
        // left to credit and only removes the row.
        (HookedWorld world, VolumetricInventory inventory) = Placed();

        CarveResult bite = Carve.Cube(world, Materials, Cell.OriginVoxel, SteelPick, inventory, world.Set);
        Assert.Equal(Cell64, bite.Removed);
        Assert.Equal(Cell64, inventory.Voxels(Pump));

        DrainReport report = Drain(world, inventory);

        Assert.Equal(1, report.Dismantled);
        Assert.Equal(0, report.CreditedVoxels);
        Assert.Equal(Cell64, inventory.Voxels(Pump));
        Assert.Equal(0, world.Set.Count);
    }

    [Fact]
    public void AHandPickThatCannotTakeTheBodyDamagesNothing()
    {
        (HookedWorld world, VolumetricInventory inventory) = Placed();

        CarveResult swing = Carve.Voxel(world, Materials, Cell.VoxelAt(0, 0, 0), hardnessThreshold: 1.0f, inventory, world.Set);

        Assert.Equal(1, swing.TooHard);
        Assert.False(world.Set.HasWorkQueued);
        Assert.Equal(1, world.Set.Count);
    }

    [Fact]
    public void AFallThatTakesHalfDestroysTheMachineWithoutCredit()
    {
        // A fall writes air into a body without a carve: Lost climbs, Carved does not, and the
        // row is destroyed — the un-fallen half cleared, nothing credited, however far the rubble
        // went. The fall never has to know a machine exists.
        (HookedWorld world, VolumetricInventory inventory) = Placed();

        for (int lz = 0; lz < BrushCell.Voxels; lz++)
        for (int lx = 0; lx < 2; lx++)
        for (int ly = 0; ly < BrushCell.Voxels; ly++)
        {
            world.SetVoxel(Cell.VoxelAt(lx, ly, lz), Air);
        }

        Assert.True(world.Set.TryGet(Cell, out MachineRow row));
        Assert.Equal(32, row.Lost);
        Assert.Equal(0, row.Carved);

        DrainReport report = Drain(world, inventory);

        Assert.Equal(1, report.DestroyedAtDrain);
        Assert.Equal(0, report.Dismantled);
        Assert.Equal(0L, inventory.Voxels(Pump));
        Assert.Equal(0, world.Set.Count);
        Assert.Equal(0, world.BodyVoxelsAt(Cell, Pump));
        Assert.Equal(1, world.Set.Destroyed);
    }

    [Fact]
    public void RubbleLandingInsideTheBodyDestroysItAndIsLeftWhereItLies()
    {
        (HookedWorld world, VolumetricInventory inventory) = Placed();
        world.SetVoxel(Cell.VoxelAt(1, 0, 1), Rubble);

        Drain(world, inventory);

        Assert.Equal(0, world.Set.Count);
        Assert.Equal(0L, inventory.Voxels(Pump));
        Assert.Equal(Rubble, world[Cell.VoxelAt(1, 0, 1)]);
        Assert.Equal(0, world.BodyVoxelsAt(Cell, Pump));
    }

    [Fact]
    public void AFallAndASwingInOneFrameCreditOnlyWhatTheSwingTook()
    {
        (HookedWorld world, VolumetricInventory inventory) = Placed();

        for (int i = 0; i < 10; i++)
        {
            world.SetVoxel(Cell.VoxelAt(i % 4, 0, i / 4), Air);
        }

        for (int i = 0; i < 5; i++)
        {
            Carve.Voxel(world, Materials, Cell.VoxelAt(i % 4, 3, i / 4), SteelPick, inventory, world.Set);
        }

        Assert.Equal(5L, inventory.Voxels(Pump));

        DrainReport report = Drain(world, inventory);

        Assert.Equal(1, report.DestroyedAtDrain);
        Assert.Equal(5L, inventory.Voxels(Pump));
        Assert.Equal(0, world.Set.Count);
        Assert.Equal(0, world.BodyVoxelsAt(Cell, Pump));
    }

    [Fact]
    public void TheHooksNeverWrite()
    {
        (HookedWorld world, VolumetricInventory inventory) = Placed();

        Carve.Voxel(world, Materials, Cell.VoxelAt(0, 0, 0), SteelPick, inventory, world.Set);
        world.Set.ChunkArrived(Cell.OriginVoxel.Chunk, hasEdits: true, voxels: null);

        Assert.True(world.Set.HasWorkQueued);
        Assert.Equal(0, world.WritesDuringHooks);
        Assert.Equal(1, world.Set.Count);
        Assert.Equal(63, world.BodyVoxelsAt(Cell, Pump));
    }

    [Fact]
    public void ADrainWithNothingQueuedDoesNothing()
    {
        (HookedWorld world, VolumetricInventory inventory) = Placed();

        DrainReport report = Drain(world, inventory);

        Assert.False(report.DidAnything);
        Assert.Equal(1, world.Set.Count);
    }

    // ------------------------------------------------------------------------------ reconcile

    private static VoxelChunk ChunkVoxelsOf(HookedWorld world, ChunkPos chunk)
    {
        var voxels = new VoxelChunk();
        VoxelPos origin = chunk.OriginVoxel;
        for (int lz = 0; lz < VoxelConstants.ChunkSize; lz++)
        for (int lx = 0; lx < VoxelConstants.ChunkSize; lx++)
        for (int ly = 0; ly < VoxelConstants.ChunkSize; ly++)
        {
            int material = world[new VoxelPos(origin.X + lx, origin.Y + ly, origin.Z + lz)];
            if (material != Air) voxels.Set(lx, ly, lz, material);
        }

        return voxels;
    }

    private static DrainReport Arrive(HookedWorld world, VolumetricInventory inventory, bool hasEdits = true)
    {
        ChunkPos chunk = Cell.OriginVoxel.Chunk;
        VoxelChunk voxels = ChunkVoxelsOf(world, chunk);
        world.Set.ChunkArrived(chunk, hasEdits, voxels);
        return world.Set.Drain(world, inventory, c => c == chunk ? voxels : null);
    }

    [Fact]
    public void AWholeBodyAtArrivalIsLeftAlone()
    {
        (HookedWorld world, VolumetricInventory inventory) = Placed();

        DrainReport report = Arrive(world, inventory);

        Assert.Equal(1, report.Reconciled);
        Assert.False(report.DidAnything);
        Assert.Equal(1, world.Set.Count);
    }

    [Fact]
    public void AnAbsentBodyIsRebuiltFromItsRow()
    {
        // The chunk never landed with its machine: the row is the truth, the body follows.
        var world = new HookedWorld();
        var inventory = new VolumetricInventory();
        world.Set.Add(new MachineRow(Cell, Pump, BrushFacing.PositiveX, PipelineHarness.PumpDefinition.DefaultPorts()));

        DrainReport report = Arrive(world, inventory, hasEdits: false);

        Assert.Equal(1, report.Rebuilt);
        Assert.Equal(Cell64, world.BodyVoxelsAt(Cell, Pump));
        Assert.Equal(1, world.Set.Count);
        Assert.Equal(0L, inventory.Voxels(Pump));
        Assert.True(world.Set.TryGet(Cell, out MachineRow row));
        Assert.Equal(0, row.Lost);
    }

    [Fact]
    public void AnAbsentBodyWhoseCellIsRockGoesBackToTheInventory()
    {
        // The chunk regenerated as rock because its write was lost. Nothing can stand there; the
        // row says the player owns the pump, so they get it back.
        var world = new HookedWorld();
        world.Seed(Cell.VoxelAt(2, 2, 2), Stone);
        var inventory = new VolumetricInventory();
        world.Set.Add(new MachineRow(Cell, Pump, BrushFacing.PositiveX));

        DrainReport report = Arrive(world, inventory, hasEdits: false);

        Assert.Equal(1, report.Returned);
        Assert.Equal(0, world.Set.Count);
        Assert.Equal(Cell64, inventory.Voxels(Pump));
        Assert.Equal(0, world.BodyVoxelsAt(Cell, Pump));
        Assert.Equal(Stone, world[Cell.VoxelAt(2, 2, 2)]);
    }

    [Fact]
    public void APartialBodyAtArrivalIsDestroyedWithoutCredit()
    {
        // Only a fall leaves a partial body on disk — the dismantle never does — so it is
        // destroyed, the leftover voxels cleared, nothing credited.
        (HookedWorld world, VolumetricInventory inventory) = Placed();
        for (int i = 0; i < 20; i++)
        {
            world.Seed(Cell.VoxelAt(i % 4, i / 4 % 4, i / 16), Air);
        }

        DrainReport report = Arrive(world, inventory);

        Assert.Equal(1, report.Destroyed);
        Assert.Equal(0, world.Set.Count);
        Assert.Equal(0L, inventory.Voxels(Pump));
        Assert.Equal(0, world.BodyVoxelsAt(Cell, Pump));
    }

    [Fact]
    public void AnOrphanBodyIsClearedWithoutCredit()
    {
        // A body written at an unload before the row's autosave, the row lost with a crash: the
        // header's debit was never written either, so the player still has the material and the
        // orphan goes to air.
        var world = new HookedWorld();
        for (int lz = 0; lz < BrushCell.Voxels; lz++)
        for (int lx = 0; lx < BrushCell.Voxels; lx++)
        for (int ly = 0; ly < BrushCell.Voxels; ly++)
        {
            world.Seed(Cell.VoxelAt(lx, ly, lz), Pump);
        }

        var inventory = new VolumetricInventory();

        DrainReport report = Arrive(world, inventory);

        Assert.Equal(Cell64, report.OrphanVoxels);
        Assert.Equal(0L, inventory.Voxels(Pump));
        Assert.Equal(0, world.BodyVoxelsAt(Cell, Pump));
        Assert.Equal(0, world.Set.Count);
    }

    [Fact]
    public void AnOrphanBesideARowedBodyLeavesTheBodyAlone()
    {
        (HookedWorld world, VolumetricInventory inventory) = Placed();
        BrushCell orphan = Cell.Offset(1, 0, 0);
        for (int lz = 0; lz < BrushCell.Voxels; lz++)
        for (int lx = 0; lx < BrushCell.Voxels; lx++)
        for (int ly = 0; ly < BrushCell.Voxels; ly++)
        {
            world.Seed(orphan.VoxelAt(lx, ly, lz), Pump);
        }

        DrainReport report = Arrive(world, inventory);

        Assert.Equal(Cell64, report.OrphanVoxels);
        Assert.Equal(Cell64, world.BodyVoxelsAt(Cell, Pump));
        Assert.Equal(0, world.BodyVoxelsAt(orphan, Pump));
        Assert.Equal(1, world.Set.Count);
    }

    [Fact]
    public void AChunkWithNoMachineMaterialAndNoRowIsSkippedInPaletteTime()
    {
        var world = new HookedWorld();
        world.Seed(Cell.VoxelAt(0, 0, 0), Stone);

        ChunkPos chunk = Cell.OriginVoxel.Chunk;
        world.Set.ChunkArrived(chunk, hasEdits: true, ChunkVoxelsOf(world, chunk));

        Assert.False(world.Set.HasWorkQueued);
        Assert.Equal(1, world.Set.ChunksSkippedByPalette);

        // And an unedited chunk with no rows is not even that: worldgen never places a body.
        world.Set.ChunkArrived(chunk, hasEdits: false, ChunkVoxelsOf(world, chunk));
        Assert.False(world.Set.HasWorkQueued);
        Assert.Equal(1, world.Set.ChunksSkippedByPalette);
    }

    [Fact]
    public void AChunkThatLeftBeforeTheDrainIsDroppedAndQueuedAgainByItsNextArrival()
    {
        var world = new HookedWorld();
        world.Set.Add(new MachineRow(Cell, Pump, BrushFacing.PositiveX));
        ChunkPos chunk = Cell.OriginVoxel.Chunk;

        world.Set.ChunkArrived(chunk, hasEdits: false, voxels: null);
        DrainReport gone = world.Set.Drain(world, null, _ => null);

        Assert.Equal(1, gone.Left);
        Assert.Equal(0, gone.Rebuilt);
        Assert.Equal(0, world.BodyVoxelsAt(Cell, Pump));
        Assert.False(world.Set.HasWorkQueued);

        VoxelChunk voxels = ChunkVoxelsOf(world, chunk);
        world.Set.ChunkArrived(chunk, hasEdits: false, voxels);
        DrainReport back = world.Set.Drain(world, null, _ => voxels);

        Assert.Equal(1, back.Rebuilt);
        Assert.Equal(Cell64, world.BodyVoxelsAt(Cell, Pump));
    }

    [Fact]
    public void AnArrivalQueuedTwiceIsReconciledOnce()
    {
        var world = new HookedWorld();
        world.Set.Add(new MachineRow(Cell, Pump, BrushFacing.PositiveX));
        ChunkPos chunk = Cell.OriginVoxel.Chunk;
        VoxelChunk voxels = ChunkVoxelsOf(world, chunk);

        world.Set.ChunkArrived(chunk, hasEdits: false, voxels);
        world.Set.ChunkArrived(chunk, hasEdits: false, voxels);
        DrainReport report = world.Set.Drain(world, null, _ => voxels);

        Assert.Equal(1, report.Reconciled);
        Assert.Equal(1, report.Rebuilt);
    }

    [Fact]
    public void ARowDamagedAndReconciledInOneDrainIsHandledOnce()
    {
        // A partial body queued by its arrival and marked damaged by the hook in the same frame:
        // the reconcile removes it, and the dismantle finds it gone rather than crediting a row
        // the reconcile already destroyed.
        (HookedWorld world, VolumetricInventory inventory) = Placed();
        world.SetVoxel(Cell.VoxelAt(0, 0, 0), Air);

        DrainReport report = Arrive(world, inventory);

        Assert.Equal(1, report.Destroyed);
        Assert.Equal(0, report.Dismantled);
        Assert.Equal(0, report.DestroyedAtDrain);
        Assert.Equal(0L, inventory.Voxels(Pump));
        Assert.Equal(0, world.Set.Count);
    }
}
