using Overburden.Core.Fluid;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// The front advance's slice 2 (docs/FRONT-ADVANCE-PLAN.md): a sheet is still being fed only when the
/// water that entered its chunks within the quiet window is a share of it. The puddle is put down
/// asleep through the grid and solved from hand-named seeds, so the only inflow is the one each test
/// makes; the chunk is (0, 0, 0), voxels 0..31, and every inflow lands inside it.
/// </summary>
public class FedShareTests
{
    private static VoxelPos At(int x, int y, int z) => new(x, y, z);

    // A floor at y = 1 over rock, x and z 0..31: one chunk column's worth, every cell supported.
    private static readonly PredicateTerrain Floor = new(v => v.Y == 1 && v.X is >= 0 and < 32 && v.Z is >= 0 and < 32);

    /// <summary>A 6 × 6 puddle at x, z 4..9, unlevel (so a pass would level it), about 8 L a cell.</summary>
    private static (FluidSimulation Sim, List<VoxelPos> Cells) Puddle(double fedShare, int side = 6)
    {
        var sim = new FluidSimulation(Floor) { FedSharePercent = fedShare };
        var cells = new List<VoxelPos>();
        for (int x = 4; x < 4 + side; x++)
        {
            for (int z = 4; z < 4 + side; z++)
            {
                sim.Grid.Fill(At(x, 1, z), 6_000 + (400 * ((x + z) % 6)));
                cells.Add(At(x, 1, z));
            }
        }

        return (sim, cells);
    }

    private static bool IsLevel(FluidSimulation sim, List<VoxelPos> cells)
    {
        int lo = cells.Min(c => sim.Grid.AmountAt(c));
        int hi = cells.Max(c => sim.Grid.AmountAt(c));
        return hi - lo <= 1;
    }

    [Fact]
    public void ATrickleIntoThePuddlesChunkNoLongerLeavesItUnlevelled()
    {
        // One litre, a quarter of a percent of the puddle, landed elsewhere in its chunk — a rain
        // drop's worth, or a wall's sweat. The old rule called the puddle a stream for it.
        (FluidSimulation sim, List<VoxelPos> cells) = Puddle(fedShare: 1.0);
        sim.Add(At(20, 1, 20), 1_000);

        SheetLevelResult r = sim.LevelSheets(cells, share: 0, quietTicks: 2);

        Assert.Equal(0, r.Noisy);
        Assert.Equal(1, r.Levelled);
        Assert.Equal(1, r.TrickleLevelled);
        Assert.Equal(1, r.InflowUnderOnePercent);
        Assert.True(IsLevel(sim, cells));

        // The control: the same litre under the old rule leaves it alone.
        (FluidSimulation old, List<VoxelPos> oldCells) = Puddle(fedShare: 0);
        old.Add(At(20, 1, 20), 1_000);
        SheetLevelResult o = old.LevelSheets(oldCells, share: 0, quietTicks: 2);
        Assert.Equal(1, o.Noisy);
        Assert.Equal(0, o.Levelled);
        Assert.Equal(1, o.InflowUnderOnePercent);
        Assert.False(IsLevel(old, oldCells));
    }

    [Fact]
    public void APourOfATenthOfThePuddleKeepsItFed()
    {
        (FluidSimulation sim, List<VoxelPos> cells) = Puddle(fedShare: 1.0);
        long total = cells.Sum(c => (long)sim.Grid.AmountAt(c));
        sim.Add(At(20, 1, 20), 15_000);
        sim.Add(At(21, 1, 20), 15_000);
        Assert.True(30_000 * 10 >= total, "the pour is under a tenth of the fixture");

        SheetLevelResult r = sim.LevelSheets(cells, share: 0, quietTicks: 2);

        Assert.Equal(1, r.Noisy);
        Assert.Equal(1, r.InflowTenPercentPlus);
        Assert.Equal(0, r.TrickleLevelled);
        Assert.False(IsLevel(sim, cells));
    }

    [Fact]
    public void ADisturbanceIsFedWhateverItsVolume()
    {
        // An edit under water changes the terrain, not the volume: it was fed before slice 2 and is.
        (FluidSimulation sim, List<VoxelPos> cells) = Puddle(fedShare: 1.0);
        sim.Disturb(At(6, 1, 6));

        SheetLevelResult r = sim.LevelSheets(cells, share: 0, quietTicks: 2);

        Assert.Equal(1, r.Noisy);
        Assert.Equal(1, r.InflowDisturbed);
    }

    [Fact]
    public void ALargeStillSheetTouchedByALitreIsLevelled()
    {
        (FluidSimulation sim, List<VoxelPos> cells) = Puddle(fedShare: 1.0, side: 20);
        sim.Add(At(29, 1, 29), 1_000);

        SheetLevelResult r = sim.LevelSheets(cells, share: 0, quietTicks: 2);

        Assert.Equal(1, r.InflowUnderTenthPercent);
        Assert.Equal(1, r.Levelled);
    }

    [Fact]
    public void InflowOlderThanTheWindowIsNotCountedAndNewerIs()
    {
        // 30 L at tick 0 and a litre at tick 2: inside a two-tick window at tick 2 they are a tenth
        // of the puddle and it is fed. A litre more at tick 3, and the 30 L has left the window: 2 L,
        // a trickle, and the puddle is levelled. The poured cells are far enough from the puddle that
        // three ticks of spreading cannot reach it.
        (FluidSimulation sim, List<VoxelPos> cells) = Puddle(fedShare: 1.0);
        sim.Add(At(24, 1, 24), 15_000);
        sim.Add(At(25, 1, 24), 15_000);
        sim.Step();
        sim.Step();
        sim.Add(At(24, 1, 28), 1_000);
        Assert.Equal(1, sim.LevelSheets(cells, share: 0, quietTicks: 2).Noisy);

        sim.Step();
        sim.Add(At(28, 1, 28), 1_000);
        SheetLevelResult r = sim.LevelSheets(cells, share: 0, quietTicks: 2);
        Assert.Equal(0, r.Noisy);
        Assert.Equal(1, r.TrickleLevelled);
        Assert.True(IsLevel(sim, cells));
    }

    [Fact]
    public void AWindowLongerThanTheRingKeepsTheOldRule()
    {
        (FluidSimulation sim, List<VoxelPos> cells) = Puddle(fedShare: 1.0);
        sim.Add(At(20, 1, 20), 1_000);

        SheetLevelResult r = sim.LevelSheets(cells, share: 0, quietTicks: InflowRing.Slots);

        Assert.Equal(1, r.Noisy);
        Assert.Equal(0, r.TrickleLevelled);
    }

    [Fact]
    public void ATrickleFedPuddleIsAdvancedToo()
    {
        // The point of the slice: a puddle the old rule called fed now takes the front advance.
        (FluidSimulation sim, List<VoxelPos> cells) = Puddle(fedShare: 1.0);
        sim.Add(At(20, 1, 20), 1_000);

        SheetLevelResult r = sim.LevelSheets(cells, share: 0, quietTicks: 2, advance: 4);

        Assert.True(r.Advanced > 0, "the trickle-fed puddle did not advance");
    }

    [Fact]
    public void TheRingForgetsASlotWhenItsTickComesRoundAgain()
    {
        // Slot = tick mod 8: tick 8 reuses tick 0's slot and must not add to what tick 0 left there.
        var ring = new InflowRing();
        ring.Note(0, 30_000);
        ring.Note(8, 1_000);
        Assert.Equal(1_000, ring.Within(8, 2, out bool disturbed));
        Assert.False(disturbed);
        ring.Note(9, InflowRing.Disturbance);
        Assert.Equal(1_000, ring.Within(10, 2, out disturbed));
        Assert.True(disturbed);
        Assert.Equal(0, ring.Within(13, 2, out disturbed));
        Assert.False(disturbed);
    }

    [Fact]
    public void AnOutletsPourCountsByItsVolume()
    {
        // A pump's outlet is water, not a disturbance: a litre emitted beside the puddle is a trickle.
        (FluidSimulation sim, List<VoxelPos> cells) = Puddle(fedShare: 1.0);
        Assert.Equal(0, sim.Emit(At(20, 1, 20), 1_000));

        SheetLevelResult r = sim.LevelSheets(cells, share: 0, quietTicks: 2);

        Assert.Equal(0, r.Noisy);
        Assert.Equal(1, r.TrickleLevelled);
    }

    [Fact]
    public void APourOfAFewPercentIsFedAndCountedInItsDecade()
    {
        // Between the trickle and the tenth: 5 L into ~288 L is ~1.7% — over the 1% share, so fed,
        // and in the 1-10% column of the split.
        (FluidSimulation sim, List<VoxelPos> cells) = Puddle(fedShare: 1.0);
        long total = cells.Sum(c => (long)sim.Grid.AmountAt(c));
        sim.Add(At(20, 1, 20), 5_000);
        double percent = 5_000 * 100.0 / total;
        Assert.InRange(percent, 1.0, 10.0);

        SheetLevelResult r = sim.LevelSheets(cells, share: 0, quietTicks: 2);

        Assert.Equal(1, r.Noisy);
        Assert.Equal(1, r.InflowUnderTenPercent);
        Assert.Equal(0, r.InflowUnderOnePercent);
        Assert.Equal(0, r.InflowTenPercentPlus);
    }
}
