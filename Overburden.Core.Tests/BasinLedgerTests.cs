using Overburden.Core.Hydrology;
using Overburden.Core.Persistence;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// The water that outlives the window it was analysed in.
/// </summary>
/// <remarks>
/// The headline is <c>CaptureAndPourBackReproducesTheStateExactly</c>. Everything else here — the
/// store round trip, the window move, the discard — rests on that one being true, because if a
/// capture is only approximately the state then every save loses a little water and nothing ever
/// says so.
/// </remarks>
public class BasinLedgerTests
{
    private const int CellMetres = HeightField.DefaultCellVoxels / VoxelConstants.VoxelsPerMetre;
    // Litres per voxel of depth over one cell: (cell metres)² × 250, derived rather than stated
    // so the fixture follows the lattice — 4,000 L at the 4 m cell this suite was written at,
    // 1,000 L at the 2 m cell since 2026-09-10.
    private const long PerVoxel =
        (HeightField.DefaultCellVoxels / VoxelConstants.VoxelsPerMetre)
        * (HeightField.DefaultCellVoxels / VoxelConstants.VoxelsPerMetre) * 250L;

    /// <summary>Two pits either side of a ridge, inside one high rim — so composites hold water.</summary>
    private static int[,] TwoPitsOneRidge()
    {
        var samples = new int[40, 40];
        for (int z = 0; z < 40; z++)
        {
            for (int x = 0; x < 40; x++)
            {
                samples[z, x] = 20;
            }
        }

        samples[13, 12] = 2;
        samples[13, 13] = 5;
        samples[13, 14] = 2;
        return samples;
    }

    private static (Watershed Watershed, BasinWater Water) Analyse(
        int[,] samples, int originX = 0, int originZ = 0, int pitch = 24)
    {
        var field = HeightField.Sample(new SampleGrid(samples, 20), originX, originZ, pitch);
        var watershed = Watershed.Analyse(field);
        return (watershed, new BasinWater(MergeTree.Build(watershed)));
    }

    [Fact]
    public void CaptureAndPourBackReproducesTheStateExactly()
    {
        // Including a composite's slab, which is water standing above both basins' rims and
        // belongs to neither — the case a naive per-basin capture loses.
        (Watershed watershed, BasinWater water) = Analyse(TwoPitsOneRidge());
        water.Add(0, 40 * PerVoxel);

        int composite = water.Tree.Parent(0);
        Assert.True(water.SlabLitres(composite) > 0, "the fixture never filled past the ridge");

        long[] captured = water.CaptureByBasin();
        Assert.Equal(water.TotalStoredLitres, captured.Sum());

        var restored = new BasinWater(water.Tree);
        for (int basin = 0; basin < captured.Length; basin++)
        {
            Assert.Equal(0, restored.Add(basin, captured[basin]));
        }

        for (int node = 0; node < water.Tree.NodeCount; node++)
        {
            Assert.Equal(water.SlabLitres(node), restored.SlabLitres(node));
            Assert.Equal(water.FreeIn(node), restored.FreeIn(node));
        }

        for (int basin = 0; basin < watershed.Basins.Count; basin++)
        {
            Assert.Equal(water.LevelAt(basin), restored.LevelAt(basin));
        }
    }

    [Fact]
    public void ALakeSurvivesTheWindowMovingAwayAndComingBack()
    {
        // The thing the ledger exists for, and the thing an array-index key would break silently.
        int[,] samples = TwoPitsOneRidge();
        var ledger = new BasinLedger();

        (Watershed near, BasinWater water) = Analyse(samples);
        water.Add(0, 12 * PerVoxel);
        ledger.Absorb(near, water);

        long stored = ledger.TotalLitres;
        Assert.Equal(12 * PerVoxel, stored);

        // The player walks; the window is re-analysed somewhere else and holds different basins in
        // different array positions. Nothing about the lake is in memory any more.
        (Watershed far, BasinWater farWater) = Analyse(samples, 22 * 16, 22 * 16);
        Assert.DoesNotContain(
            far.Basins,
            basin => basin.PitX == near.Basins[0].PitX && basin.PitZ == near.Basins[0].PitZ);
        Assert.Equal(0, ledger.Seed(far, farWater));
        Assert.Equal(0, farWater.TotalStoredLitres);

        // And back.
        (Watershed back, BasinWater backWater) = Analyse(samples);
        Assert.Equal(0, ledger.Seed(back, backWater));
        Assert.Equal(stored, backWater.TotalStoredLitres);

        for (int basin = 0; basin < near.Basins.Count; basin++)
        {
            Assert.Equal(water.SlabLitres(basin), backWater.SlabLitres(basin));
        }
    }

    [Fact]
    public void AbsorbingReplacesRatherThanAdding()
    {
        // Absorbing as an addition would double a lake every time the player crossed the same
        // ground twice, which is the kind of defect that only shows up in a long session.
        (Watershed watershed, BasinWater water) = Analyse(TwoPitsOneRidge());
        water.Add(0, 3 * PerVoxel);

        var ledger = new BasinLedger();
        ledger.Absorb(watershed, water);
        ledger.Absorb(watershed, water);
        ledger.Absorb(watershed, water);

        Assert.Equal(3 * PerVoxel, ledger.TotalLitres);
    }

    [Fact]
    public void ABasinThatEmptiesLeavesNoRowBehind()
    {
        (Watershed watershed, BasinWater water) = Analyse(TwoPitsOneRidge());
        water.Add(0, 3 * PerVoxel);

        var ledger = new BasinLedger();
        ledger.Absorb(watershed, water);
        Assert.Equal(1, ledger.Count);

        ledger.Absorb(watershed, new BasinWater(water.Tree));
        Assert.Equal(0, ledger.Count);
        Assert.Empty(ledger.Entries());
    }

    [Fact]
    public void EntriesComeOutInAFixedOrder()
    {
        // The same ledger has to write the same rows, or a diff of two saves is unreadable and a
        // round-trip test can pass while the order wanders. M6c-2's argument for the inventory.
        var ledger = new BasinLedger();
        ledger.Load(
            [
                new StoredBasin(32, 16, 1000),
                new StoredBasin(-16, 48, 2000),
                new StoredBasin(32, -64, 3000),
            ],
            CellMetres,
            CellMetres);

        Assert.Equal(
            [
                new StoredBasin(-16, 48, 2000),
                new StoredBasin(32, -64, 3000),
                new StoredBasin(32, 16, 1000),
            ],
            ledger.Entries());
    }

    [Fact]
    public void AChangedLatticeDiscardsTheVolumesRatherThanReinterpretingThem()
    {
        // A pit is a sample on the watershed lattice, so changing the lattice renames every basin
        // in the world. Keeping the numbers would land stored water on whatever pit happened to be
        // nearest — a lake in the wrong valley, with nothing failing anywhere.
        var ledger = new BasinLedger();
        IReadOnlyList<StoredBasin> stored = [new StoredBasin(16, 32, 5000)];

        Assert.True(ledger.Load(stored, CellMetres, CellMetres));
        Assert.Equal(5000, ledger.TotalLitres);

        Assert.False(ledger.Load(stored, CellMetres * 2, CellMetres));
        Assert.Equal(0, ledger.TotalLitres);
        Assert.Empty(ledger.Entries());
    }

    [Fact]
    public void WaterThatNoLongerFitsLeavesRatherThanBecomingADebt()
    {
        // A window that has moved can hold a basin whose catchment is partly outside it, so its
        // capacity is smaller than when the water was stored. The surplus is reported, not hidden.
        (Watershed watershed, BasinWater water) = Analyse(TwoPitsOneRidge());

        var ledger = new BasinLedger();
        ledger.Load(
            [new StoredBasin(watershed.Basins[0].PitX, watershed.Basins[0].PitZ, 1_000_000_000)],
            CellMetres,
            CellMetres);

        long spilled = ledger.Seed(watershed, water);
        Assert.True(spilled > 0, "the fixture did not overflow, so this tests nothing");
        Assert.Equal(1_000_000_000, water.TotalStoredLitres + spilled);
    }
}
