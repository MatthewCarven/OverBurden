using Overburden.Core.Building;
using Overburden.Core.Content;
using Overburden.Core.Inventory;
using Overburden.Core.Machines;
using Overburden.Core.Mining;
using Overburden.Core.Streaming;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// The machine set on the real pipeline's hooks, wired as <c>Main</c> wires them (M10a-2): the
/// edit hook marks, the arrival hook queues on all three paths, and a drain after each update
/// writes. The cases a test double cannot reach — a chunk regenerating without its body, a
/// cache revival bringing the body back, a chunk that leaves between the hook and the drain.
/// </summary>
public class MachinePipelineTests
{
    private const int Pump = PipelineHarness.Pump;
    private const int Cell64 = BrushCell.Voxels * BrushCell.Voxels * BrushCell.Voxels;
    private static readonly ChunkPos FarAway = new(200, 0, 200);

    /// <summary>A brush cell well above the harness's terrain — its top is a few voxels up — in chunk (0, 1, 0).</summary>
    private static readonly BrushCell Cell = new(4, 10, 4);

    private static readonly MaterialTable Materials = PipelineHarness.MaterialsWithPump();

    private sealed class Wired : IDisposable
    {
        public Wired(long chunkCacheBytes = 0)
        {
            Set = new MachineSet(Materials);
            Harness = new PipelineHarness(
                materials: Materials,
                chunkCacheBytes: chunkCacheBytes,
                onVoxelChanged: (voxel, material) => Set.OnVoxelChanged(voxel, material),
                onVoxelsReady: (position, hasEdits) => Arrivals.Add(position));

            // The arrival hook needs the chunk's voxels, which the harness cannot hand it before
            // the pipeline exists; Main reads them off the streamer, and so does this.
            Viewer = Harness.Pipeline.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
        }

        public ViewerId Viewer { get; private set; }

        public MachineSet Set { get; }

        public PipelineHarness Harness { get; }

        public ChunkPipeline Pipeline => Harness.Pipeline;

        public List<ChunkPos> Arrivals { get; } = [];

        public VolumetricInventory Inventory { get; } = new();

        /// <summary>One frame: the pipeline's update, then the hook's queue handed to the set, then the drain.</summary>
        public DrainReport Frame()
        {
            Pipeline.Update();
            foreach (ChunkPos chunk in Arrivals)
            {
                StreamedChunk? streamed = Pipeline.Streamer.Get(chunk);
                Set.ChunkArrived(chunk, streamed?.HasEdits ?? false, streamed?.Voxels);
            }

            Arrivals.Clear();
            return Set.Drain(Pipeline, Inventory, chunk => Pipeline.Streamer.Get(chunk)?.Voxels);
        }

        public DrainReport Settle()
        {
            var total = default(DrainReport);
            for (int i = 0; i < 20_000; i++)
            {
                DrainReport frame = Frame();
                total.Rebuilt += frame.Rebuilt;
                total.Returned += frame.Returned;
                total.Destroyed += frame.Destroyed;
                total.OrphanVoxels += frame.OrphanVoxels;
                total.Dismantled += frame.Dismantled;
                total.CreditedVoxels += frame.CreditedVoxels;
                total.DestroyedAtDrain += frame.DestroyedAtDrain;
                total.Reconciled += frame.Reconciled;
                total.Left += frame.Left;
                if (Pipeline.IsQuiescent && Arrivals.Count == 0 && !Set.HasWorkQueued)
                {
                    Assert.Empty(Harness.Faults);
                    return total;
                }

                Thread.Sleep(1);
            }

            throw new TimeoutException("the pipeline did not settle");
        }

        public int BodyVoxels()
        {
            int count = 0;
            for (int lz = 0; lz < BrushCell.Voxels; lz++)
            for (int lx = 0; lx < BrushCell.Voxels; lx++)
            for (int ly = 0; ly < BrushCell.Voxels; ly++)
            {
                if (Pipeline.TryGetMaterial(Cell.VoxelAt(lx, ly, lz), out int material) && material == Pump) count++;
            }

            return count;
        }

        public void Place()
        {
            Inventory.Add(Pump, Cell64);
            PlaceResult placed = Building.Place.Machine(Pipeline, Materials, Cell, Pump, BrushFacing.PositiveX, Set, from: Inventory);
            Assert.Equal(Cell64, placed.Placed);
            Assert.Equal(Cell64, BodyVoxels());
        }

        public void Dispose() => Harness.Dispose();
    }

    [Fact]
    public void ABodyWhoseChunkRegeneratedWithoutItIsRebuiltFromTheRowAtArrival()
    {
        // No store and no cache: the chunk that leaves comes back from the seed, body gone, row
        // still here. The arrival queues it, the next frame's drain rewrites the body — the
        // "chunk never landed with its machine" case on the real generate path.
        using var w = new Wired();
        w.Settle();
        w.Place();

        w.Pipeline.Streamer.MoveViewer(w.Viewer, FarAway);
        w.Settle();
        Assert.False(w.Pipeline.TryGetMaterial(Cell.OriginVoxel, out _), "the chunk should have left");

        w.Pipeline.Streamer.MoveViewer(w.Viewer, ChunkPos.Zero);
        DrainReport back = w.Settle();

        Assert.Equal(1, back.Rebuilt);
        Assert.Equal(Cell64, w.BodyVoxels());
        Assert.Equal(1, w.Set.Count);
        Assert.Equal(0L, w.Inventory.Voxels(Pump));
    }

    [Fact]
    public void ABodyThatComesBackFromTheCacheIsWholeAndLeftAlone()
    {
        // The third arrival path: a cache revival brings the voxels back as they left, body and
        // all, and the reconcile finds nothing to do.
        using var w = new Wired(chunkCacheBytes: 64L << 20);
        w.Settle();
        w.Place();

        w.Pipeline.Streamer.MoveViewer(w.Viewer, FarAway);
        w.Settle();

        w.Pipeline.Streamer.MoveViewer(w.Viewer, ChunkPos.Zero);
        DrainReport back = w.Settle();

        Assert.True(w.Pipeline.Streamer.CacheHits > 0, "the chunk should have come back from the cache");
        Assert.Equal(0, back.Rebuilt);
        Assert.True(back.Reconciled >= 1);
        Assert.Equal(Cell64, w.BodyVoxels());
    }

    [Fact]
    public void ASwingThroughThePipelineDismantlesWithTheHookAndTheDrain()
    {
        using var w = new Wired();
        w.Settle();
        w.Place();

        CarveResult swing = Carve.Voxel(w.Pipeline, Materials, Cell.VoxelAt(1, 1, 1), 3.5f, w.Inventory, w.Set);
        Assert.Equal(1, swing.Removed);
        Assert.True(w.Set.HasWorkQueued);

        DrainReport frame = w.Frame();

        Assert.Equal(1, frame.Dismantled);
        Assert.Equal(63, frame.CreditedVoxels);
        Assert.Equal(Cell64, w.Inventory.Voxels(Pump));
        Assert.Equal(0, w.BodyVoxels());
        Assert.Equal(0, w.Set.Count);
    }

    [Fact]
    public void AnOrphanBodyOnTheGeneratePathIsClearedAtArrival()
    {
        // A body with no row — placed through the pipeline directly, as a crash before the
        // row's autosave would leave it — is cleared the frame after its chunk arrives.
        using var w = new Wired();
        w.Settle();
        for (int lz = 0; lz < BrushCell.Voxels; lz++)
        for (int lx = 0; lx < BrushCell.Voxels; lx++)
        for (int ly = 0; ly < BrushCell.Voxels; ly++)
        {
            Assert.True(w.Pipeline.SetVoxel(Cell.VoxelAt(lx, ly, lz), Pump));
        }

        Assert.Equal(Cell64, w.BodyVoxels());
        Assert.Equal(0, w.Set.Count);

        // A fresh arrival of the same chunk: the hook as the pipeline would fire it.
        w.Arrivals.Add(Cell.OriginVoxel.Chunk);
        DrainReport frame = w.Frame();

        Assert.Equal(Cell64, frame.OrphanVoxels);
        Assert.Equal(0, w.BodyVoxels());
        Assert.Equal(0L, w.Inventory.Voxels(Pump));
    }
}
