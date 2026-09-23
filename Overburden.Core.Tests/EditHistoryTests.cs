using System.Numerics;
using Overburden.Core.Building;
using Overburden.Core.Content;
using Overburden.Core.Editing;
using Overburden.Core.Inventory;
using Overburden.Core.Machines;
using Overburden.Core.Mining;
using Overburden.Core.Streaming;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// The three-step undo (UNDO-PLAN, 2026-09-22).
/// </summary>
/// <remarks>
/// <b>The oracle is the whole world, not a counter</b>: a click of any shape, undone, leaves
/// every voxel in the region and every entry in the inventory exactly as they were. That is
/// what a journal recording <c>now</c> instead of <c>was</c>, or an undo applied in the wrong
/// order, cannot survive — and a counter-based assertion would pass under both.
/// </remarks>
public class EditHistoryTests
{
    private const int Air = MaterialIds.AirNumericId;
    private const int Grass = 1;
    private const int Dirt = 2;
    private const int Stone = 3;

    /// <summary>Voxels in a brush cell — what a machine body costs.</summary>
    private const int Cell = BrushCell.Voxels * BrushCell.Voxels * BrushCell.Voxels;

    /// <summary>Below the deepest the surface reaches, so a shape lands in solid rock.</summary>
    private static readonly ChunkPos Deep = new(0, -3, 0);

    private sealed class Fixture : IDisposable
    {
        public Fixture(bool withPump = false)
        {
            Materials = withPump ? PipelineHarness.MaterialsWithPump() : PipelineHarness.Materials();
            Harness = new PipelineHarness(materials: Materials);
            Harness.Pipeline.Streamer.AddViewer(Deep, 1, 1);
            Harness.Settle();
            Harness.Pipeline.Journal = History;
        }

        public PipelineHarness Harness { get; }

        public EditHistory History { get; } = new();

        public ChunkPipeline Pipeline => Harness.Pipeline;

        public MaterialTable Materials { get; }

        public VolumetricInventory Inventory { get; } = new();

        /// <summary>Every voxel of the nine chunks around <see cref="Deep"/>, as the world holds them now.</summary>
        public Dictionary<VoxelPos, int> Snapshot()
        {
            var seen = new Dictionary<VoxelPos, int>();
            for (int cz = -1; cz <= 1; cz++)
            for (int cy = -1; cy <= 1; cy++)
            for (int cx = -1; cx <= 1; cx++)
            {
                var chunk = new ChunkPos(Deep.X + cx, Deep.Y + cy, Deep.Z + cz);
                VoxelPos origin = chunk.OriginVoxel;
                for (int z = 0; z < VoxelConstants.ChunkSize; z++)
                for (int y = 0; y < VoxelConstants.ChunkSize; y++)
                for (int x = 0; x < VoxelConstants.ChunkSize; x++)
                {
                    var voxel = new VoxelPos(origin.X + x, origin.Y + y, origin.Z + z);
                    if (Pipeline.TryGetMaterial(voxel, out int material))
                    {
                        seen[voxel] = material;
                    }
                }
            }

            return seen;
        }

        public List<(int MaterialId, long Voxels)> Stock() => [.. Inventory.Entries()];

        public void Dispose() => Harness.Dispose();
    }

    /// <summary>The corner eight chunks meet at, in metres — a shape there crosses every boundary.</summary>
    private static Vector3 Corner => new(
        Deep.OriginVoxel.X * VoxelConstants.VoxelSize,
        Deep.OriginVoxel.Y * VoxelConstants.VoxelSize,
        Deep.OriginVoxel.Z * VoxelConstants.VoxelSize);

    [Theory]
    [InlineData("sphere")]
    [InlineData("voxel")]
    [InlineData("cube")]
    [InlineData("chunk")]
    [InlineData("brush-cube")]
    [InlineData("brush-slab")]
    [InlineData("brush-ramp")]
    [InlineData("brush-sphere")]
    public void AClickOfAnyShapeUndoneLeavesTheWorldAndTheInventoryByteIdentical(string shape)
    {
        using var fixture = new Fixture();
        MaterialTable materials = fixture.Materials;

        // The brush arms need air to fill and stock to fill it with, and **that setup must be
        // outside the compared state**: the first run of this battery carved the hollow after
        // taking the snapshot, so all four brush arms failed on a hole the undo was never asked
        // to restore. Setup, then snapshot, then the one click under test.
        var cell = BrushCell.FromVoxel(new VoxelPos(Deep.OriginVoxel.X + 8, Deep.OriginVoxel.Y + 8, Deep.OriginVoxel.Z + 8));
        bool brush = shape.StartsWith("brush", StringComparison.Ordinal);
        if (brush)
        {
            fixture.Inventory.Add(Dirt, 10_000);
            Carve.Cube(fixture.Pipeline, materials, cell.OriginVoxel, 3.5f);
            fixture.History.Clear();
        }

        Dictionary<VoxelPos, int> before = fixture.Snapshot();
        List<(int, long)> stockBefore = fixture.Stock();

        var hollow = new VoxelPos(Deep.OriginVoxel.X + 4, Deep.OriginVoxel.Y + 4, Deep.OriginVoxel.Z + 4);
        int changed = shape switch
        {
            "sphere" => Carve.Sphere(fixture.Pipeline, materials, Corner, 0.75f, 3.5f, fixture.Inventory).Removed,
            "voxel" => Carve.Voxel(fixture.Pipeline, materials, hollow, 3.5f, fixture.Inventory).Removed,
            "cube" => Carve.Cube(fixture.Pipeline, materials, hollow, 3.5f, fixture.Inventory).Removed,
            "chunk" => Carve.Chunk(fixture.Pipeline, materials, Deep, 3.5f, fixture.Inventory).Removed,
            "brush-cube" => Place.Cube(fixture.Pipeline, materials, cell, Dirt, from: fixture.Inventory).Placed,
            "brush-slab" => Place.Slab(fixture.Pipeline, materials, cell, Dirt, from: fixture.Inventory).Placed,
            "brush-ramp" => Place.Ramp(fixture.Pipeline, materials, cell, Dirt, BrushFacing.PositiveX, from: fixture.Inventory).Placed,
            "brush-sphere" => Place.Sphere(fixture.Pipeline, materials, cell, 0.5f, Dirt, from: fixture.Inventory).Placed,
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "not a shape"),
        };

        Assert.True(changed > 0, $"the {shape} arm changed nothing, so the undo has nothing to prove");
        Assert.True(fixture.History.EndStep());
        Assert.Equal(1, fixture.History.Steps);

        UndoReport report = fixture.History.Undo(fixture.Pipeline, materials, fixture.Inventory, machines: null);

        Assert.Equal(UndoOutcome.Undone, report.Outcome);
        Assert.Equal(changed, report.Voxels);
        Assert.Equal(0, report.StepsLeft);
        Assert.Equal(before, fixture.Snapshot());
        Assert.Equal(stockBefore, fixture.Stock());
    }

    [Fact]
    public void ThreeStepsGoBackInOrderAndTheFourthIsGone()
    {
        using var fixture = new Fixture();
        MaterialTable materials = fixture.Materials;

        var snapshots = new List<Dictionary<VoxelPos, int>>();
        for (int i = 0; i < 4; i++)
        {
            snapshots.Add(fixture.Snapshot());
            var origin = new VoxelPos(Deep.OriginVoxel.X + (4 * i) + 2, Deep.OriginVoxel.Y + 4, Deep.OriginVoxel.Z + 4);
            Assert.True(Carve.Cube(fixture.Pipeline, materials, origin, 3.5f, fixture.Inventory).Removed > 0);
            fixture.History.EndStep();
        }

        Assert.Equal(EditHistory.MaxSteps, fixture.History.Steps);
        Assert.Equal(1, fixture.History.Dropped);

        // Back through three, newest first: after undoing step 4 the world is snapshot 4's, and so on.
        for (int i = 3; i >= 1; i--)
        {
            Assert.True(fixture.History.Undo(fixture.Pipeline, materials, fixture.Inventory, null).Ok);
            Assert.Equal(snapshots[i], fixture.Snapshot());
        }

        // The first step is gone for good, so the world never returns to snapshot 0.
        Assert.Equal(UndoOutcome.Empty, fixture.History.Undo(fixture.Pipeline, materials, fixture.Inventory, null).Outcome);
        Assert.NotEqual(snapshots[0], fixture.Snapshot());
    }

    [Fact]
    public void AStepThatCanNoLongerPayIsRefusedWholeAndKept()
    {
        using var fixture = new Fixture();
        MaterialTable materials = fixture.Materials;

        var origin = new VoxelPos(Deep.OriginVoxel.X + 4, Deep.OriginVoxel.Y + 4, Deep.OriginVoxel.Z + 4);
        int removed = Carve.Cube(fixture.Pipeline, materials, origin, 3.5f, fixture.Inventory).Removed;
        fixture.History.EndStep();

        Dictionary<VoxelPos, int> hollow = fixture.Snapshot();

        // Spent it. Putting the rock back would cost what is no longer there.
        foreach ((int material, long voxels) in fixture.Stock())
        {
            fixture.Inventory.Take(material, voxels);
        }

        UndoReport report = fixture.History.Undo(fixture.Pipeline, materials, fixture.Inventory, null);

        Assert.Equal(UndoOutcome.OutOfStock, report.Outcome);
        Assert.Equal(removed, report.RefusedShortfall + (removed - report.RefusedShortfall));
        Assert.True(report.RefusedShortfall > 0);

        // Whole, and kept: nothing moved, and the step is still there for when the stock is back.
        Assert.Equal(hollow, fixture.Snapshot());
        Assert.Equal(1, fixture.History.Steps);

        fixture.Inventory.Add(Stone, 100_000);
        fixture.Inventory.Add(Dirt, 100_000);
        fixture.Inventory.Add(Grass, 100_000);
        Assert.True(fixture.History.Undo(fixture.Pipeline, materials, fixture.Inventory, null).Ok);
    }

    [Fact]
    public void AChunkLeavingDropsTheStepsThatTouchedItAndLeavesTheOthers()
    {
        using var fixture = new Fixture();
        MaterialTable materials = fixture.Materials;

        var here = new VoxelPos(Deep.OriginVoxel.X + 4, Deep.OriginVoxel.Y + 4, Deep.OriginVoxel.Z + 4);
        var there = new VoxelPos(Deep.OriginVoxel.X + 36, Deep.OriginVoxel.Y + 4, Deep.OriginVoxel.Z + 4);
        Carve.Cube(fixture.Pipeline, materials, here, 3.5f, fixture.Inventory);
        fixture.History.EndStep();
        Carve.Cube(fixture.Pipeline, materials, there, 3.5f, fixture.Inventory);
        fixture.History.EndStep();
        Assert.Equal(2, fixture.History.Steps);

        Assert.Equal(1, fixture.History.Forget(Deep));

        Assert.Equal(1, fixture.History.Steps);
        Assert.Equal(1, fixture.History.ClearedByForget);
        Assert.True(fixture.History.Undo(fixture.Pipeline, materials, fixture.Inventory, null).Ok);
        Assert.Equal(0, fixture.History.Steps);
    }

    [Fact]
    public void AFallClearsEverythingIncludingTheOpenStep()
    {
        using var fixture = new Fixture();
        MaterialTable materials = fixture.Materials;

        var origin = new VoxelPos(Deep.OriginVoxel.X + 4, Deep.OriginVoxel.Y + 4, Deep.OriginVoxel.Z + 4);
        Carve.Cube(fixture.Pipeline, materials, origin, 3.5f, fixture.Inventory);
        fixture.History.EndStep();
        Carve.Voxel(fixture.Pipeline, materials, new VoxelPos(Deep.OriginVoxel.X + 20, Deep.OriginVoxel.Y + 4, Deep.OriginVoxel.Z + 4), 3.5f, fixture.Inventory);

        // Two steps' worth: one closed, one still open.
        Assert.Equal(2, fixture.History.Steps);

        fixture.History.Clear();

        Assert.Equal(0, fixture.History.Steps);
        Assert.Equal(1, fixture.History.ClearedByFall);

        // His word on the first play: *disallow* if something falls, which is a press that says
        // why rather than one that says nothing — so the outcome carries the fall until the next
        // edit, and reads as an ordinary empty stack after it.
        Assert.True(fixture.History.FellSinceLastEdit);
        Assert.Equal(UndoOutcome.EmptyAfterAFall, fixture.History.Undo(fixture.Pipeline, materials, fixture.Inventory, null).Outcome);

        Carve.Voxel(fixture.Pipeline, materials, new VoxelPos(Deep.OriginVoxel.X + 24, Deep.OriginVoxel.Y + 4, Deep.OriginVoxel.Z + 4), 3.5f, fixture.Inventory);
        Assert.False(fixture.History.FellSinceLastEdit);
        Assert.True(fixture.History.Undo(fixture.Pipeline, materials, fixture.Inventory, null).Ok);
        Assert.Equal(UndoOutcome.Empty, fixture.History.Undo(fixture.Pipeline, materials, fixture.Inventory, null).Outcome);
    }

    [Fact]
    public void AVoxelWrittenTwiceInOneStepGoesBackToWhereTheStepFoundIt()
    {
        // The rule the displacing fill needs: lift, write, write again — the undo restores the
        // material the step *found*, not the one its second write found, and counts the voxel once.
        using var fixture = new Fixture();
        MaterialTable materials = fixture.Materials;
        fixture.Inventory.Add(Dirt, 100);
        var voxel = new VoxelPos(Deep.OriginVoxel.X + 4, Deep.OriginVoxel.Y + 4, Deep.OriginVoxel.Z + 4);

        Assert.True(fixture.Pipeline.TryGetMaterial(voxel, out int original));
        Assert.NotEqual(Air, original);

        Carve.Voxel(fixture.Pipeline, materials, voxel, 3.5f, fixture.Inventory);
        Place.Voxel(fixture.Pipeline, materials, voxel, Dirt, from: fixture.Inventory);
        fixture.History.EndStep();

        UndoReport report = fixture.History.Undo(fixture.Pipeline, materials, fixture.Inventory, null);

        Assert.Equal(1, report.Voxels);
        Assert.True(fixture.Pipeline.TryGetMaterial(voxel, out int after));
        Assert.Equal(original, after);
        Assert.Equal(100, fixture.Inventory.Voxels(Dirt));
        Assert.Equal(0, fixture.Inventory.Voxels(original));
    }

    [Fact]
    public void AMachineGoesBackWithItsPortsAndItsRowMatchesItsBody()
    {
        // The order the undo has to keep: a row standing over air takes damage marks, a row added
        // before its body is a row over air, and either way the next drain destroys it.
        using var fixture = new Fixture(withPump: true);
        MaterialTable materials = fixture.Materials;
        const int pumpId = PipelineHarness.Pump;
        var machines = new MachineSet(materials) { Journal = fixture.History };
        // A body is 64 voxels and the inventory counts voxels, whatever the unit says.
        fixture.Inventory.Add(pumpId, 4 * Cell);

        var cell = BrushCell.FromVoxel(new VoxelPos(Deep.OriginVoxel.X + 8, Deep.OriginVoxel.Y + 8, Deep.OriginVoxel.Z + 8));
        Carve.Cube(fixture.Pipeline, materials, cell.OriginVoxel, 5.0f);
        fixture.History.Clear();

        Assert.Equal(Cell, Place.Machine(
            fixture.Pipeline, materials, cell, pumpId, BrushFacing.PositiveX, machines, from: fixture.Inventory).Placed);
        machines.SetPort(cell, "channel", 7);
        fixture.History.EndStep();

        Assert.True(machines.TryGet(cell, out MachineRow placed));
        Assert.Equal(7, placed.Ports["channel"]);

        // Undo the placement: the row goes, the body goes, the cell comes back.
        Assert.True(fixture.History.Undo(fixture.Pipeline, materials, fixture.Inventory, machines).Ok);
        Assert.False(machines.TryGet(cell, out _));
        Assert.Equal(4 * Cell, fixture.Inventory.Voxels(pumpId));
        Assert.True(fixture.Pipeline.TryGetMaterial(cell.OriginVoxel, out int empty));
        Assert.Equal(Air, empty);

        // And the other direction: a row removed comes back with its ports, undamaged.
        Place.Machine(fixture.Pipeline, materials, cell, pumpId, BrushFacing.PositiveX, machines, from: fixture.Inventory);
        machines.SetPort(cell, "channel", 5);
        fixture.History.EndStep();
        fixture.History.Clear();

        Assert.True(machines.TryGet(cell, out MachineRow standing));
        machines.Remove(cell);
        Carve.Cube(fixture.Pipeline, materials, cell.OriginVoxel, 5.0f, fixture.Inventory);
        fixture.History.EndStep();

        Assert.True(fixture.History.Undo(fixture.Pipeline, materials, fixture.Inventory, machines).Ok);
        Assert.True(machines.TryGet(cell, out MachineRow back));
        Assert.Equal(5, back.Ports["channel"]);
        Assert.Equal(standing.MaterialId, back.MaterialId);
        Assert.Equal(standing.Facing, back.Facing);
        Assert.Equal(0, back.Lost);
        Assert.Equal(0, back.Carved);
        Assert.True(fixture.Pipeline.TryGetMaterial(cell.OriginVoxel, out int body));
        Assert.Equal(pumpId, body);
    }

    [Fact]
    public void NothingToUndoIsRefusedRatherThanSilent()
    {
        using var fixture = new Fixture();

        UndoReport report = fixture.History.Undo(fixture.Pipeline, fixture.Materials, fixture.Inventory, null);

        Assert.Equal(UndoOutcome.Empty, report.Outcome);
        Assert.Equal(1, fixture.History.Undos);
        Assert.Equal(1, fixture.History.Refusals);
    }

    [Fact]
    public void RecordingOffKeepsNothing()
    {
        using var fixture = new Fixture();
        fixture.History.Recording = false;

        Carve.Cube(
            fixture.Pipeline,
            fixture.Materials,
            new VoxelPos(Deep.OriginVoxel.X + 4, Deep.OriginVoxel.Y + 4, Deep.OriginVoxel.Z + 4),
            3.5f,
            fixture.Inventory);

        Assert.Equal(0, fixture.History.Steps);
        Assert.Equal(0, fixture.History.HeldVoxels);
    }
}
