using Overburden.Core.Hydrology;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// Walls that split a lake (docs/LAKE-EDIT-PLAN.md, L-1): a same-origin re-analysis shares each old
/// lake's litres among the new basins by the water that stood over each cell at the old level, so a
/// wall across a full lake leaves each side holding what it held, and the wall's own strip of water
/// goes to the basin that now holds it (decision 3, displacement).
/// </summary>
public sealed class LakeSplitTests
{
    private const int Cell = HeightField.DefaultCellVoxels;
    private const int Pitch = 12;

    private const long PerVoxel =
        (Cell / VoxelConstants.VoxelsPerMetre) * (Cell / VoxelConstants.VoxelsPerMetre) * 250L;

    /// <summary>
    /// A trough four samples deep (z 3..6) and eight long (x 2..9): its west half's floor at plane 3,
    /// its east half's at plane 5. Around it a flat at plane 10 that drains into it, then a band at
    /// plane 11 that drains off the window, then the edge ring. Planes are one higher than the grid
    /// (see SampleGrid).
    /// </summary>
    /// <remarks>
    /// The two halves sit at different depths so that sharing by area and sharing by depth give
    /// different answers, and the flat at 10 is there so that a raised cell on the lake's shore drains
    /// to the lake rather than off the window.
    /// </remarks>
    private static int[,] Trough()
    {
        var grid = new int[Pitch, Pitch];
        for (int z = 0; z < Pitch; z++)
        {
            for (int x = 0; x < Pitch; x++)
            {
                bool edge = x == 0 || z == 0 || x == Pitch - 1 || z == Pitch - 1;
                bool band = !edge && (x == 1 || z == 1 || x == Pitch - 2 || z == Pitch - 2);
                grid[z, x] = edge ? 1 : band ? 10 : 9;
            }
        }

        for (int z = 3; z <= 6; z++)
        {
            for (int x = 2; x <= 9; x++)
            {
                grid[z, x] = x <= 5 ? 2 : 4;
            }
        }

        return grid;
    }

    private static readonly SampleGrid TroughTerrain = new(Trough(), outside: 1);

    /// <summary>Litres that put the whole trough at plane 7: 16 cells 4 deep and 16 cells 2 deep.</summary>
    private const long FullAtSeven = ((16 * 4) + (16 * 2)) * PerVoxel;

    private static RaisedSamples Raise(params ((int X, int Z) Sample, int Plane)[] raises)
    {
        var map = new Dictionary<(int X, int Z), int>();
        foreach (((int X, int Z) sample, int plane) in raises) map[sample] = plane;
        return new RaisedSamples(map);
    }

    /// <summary>A wall across the trough at x = 5, tied into the flat on both banks.</summary>
    private static RaisedSamples WallAtFive(int plane = 12) =>
        Raise(((5, 3), plane), ((5, 4), plane), ((5, 5), plane), ((5, 6), plane));

    /// <summary>A ring round (4, 4) in the trough's west half, leaving its z = 6 row open.</summary>
    private static RaisedSamples RingRoundFourFour() =>
        Raise(
            ((3, 3), 12), ((4, 3), 12), ((5, 3), 12),
            ((3, 4), 12), ((5, 4), 12),
            ((3, 5), 12), ((4, 5), 12), ((5, 5), 12));

    /// <summary>
    /// The trough, filled, and banked as an autosave would — so the ledger already holds a row for
    /// the old pit, which a re-analysis must replace rather than add to.
    /// </summary>
    private static WaterSystem Filled(long litres)
    {
        var system = new WaterSystem(TroughTerrain, 7, new BasinLedger(), pitch: Pitch);
        system.Install(WaterSystem.Analyse(TroughTerrain, 0, 0, Pitch, Cell));
        Basin trough = Assert.Single(system.Window!.Watershed.Basins);
        Assert.Equal(0, system.Inject(trough.Index, litres));
        system.Bank();
        Assert.Equal(litres, system.Ledger.TotalLitres);
        return system;
    }

    private static void Reanalyse(WaterSystem system, IRaisedSamples? raised) =>
        system.Install(WaterSystem.Analyse(TroughTerrain, 0, 0, Pitch, Cell, raised));

    private static int BasinAt(WaterSystem system, int sx, int sz)
    {
        Assert.True(system.TryBasinAtSample(sx, sz, out int basin));
        Assert.NotEqual(Watershed.Outlet, basin);
        return basin;
    }

    private static double Level(WaterSystem system, int sx, int sz) =>
        system.LevelAt(BasinAt(system, sx, sz)) ?? double.NaN;

    private static int LakeOf(WaterSystem system, int sx, int sz) =>
        system.Water!.LakeTopOf(BasinAt(system, sx, sz));

    private static long Held(WaterSystem system, int sx, int sz) =>
        system.Water!.StoredIn(LakeOf(system, sx, sz));

    [Fact]
    public void TheFixtureIsOneLakeAtPlaneSeven()
    {
        WaterSystem system = Filled(FullAtSeven);
        Assert.Equal(7.0, Level(system, 2, 3), 9);
        Assert.Equal(7.0, Level(system, 9, 6), 9);
    }

    [Fact]
    public void AWallAcrossAFullLakeLeavesEachSideWhereItStoodAndTheWallsStripRaisesTheSideHoldingIt()
    {
        WaterSystem system = Filled(FullAtSeven);

        Reanalyse(system, WallAtFive());

        // Two lakes now. The wall's cells drain west (the west floor is the lower neighbour), so the
        // water that stood over them, 4 cells 4 deep, goes west: decision 3's displacement.
        int west = BasinAt(system, 2, 3);
        int east = BasinAt(system, 9, 6);
        Assert.NotEqual(west, east);
        Assert.Equal(west, BasinAt(system, 5, 4));
        Assert.NotEqual(LakeOf(system, 2, 3), LakeOf(system, 9, 6));

        Assert.Equal(((12 * 4) + (4 * 4)) * PerVoxel, Held(system, 2, 3));
        Assert.Equal(16 * 2 * PerVoxel, Held(system, 9, 6));

        // East is exactly where it stood; west rose by the strip over its 12 cells.
        Assert.Equal(7.0, Level(system, 9, 6), 9);
        Assert.Equal(3.0 + (64.0 / 12.0), Level(system, 2, 3), 9);

        Assert.Equal(1, system.SplitLakes);
        Assert.Equal(FullAtSeven, system.SplitLitres);
        Assert.Equal(4 * 4 * PerVoxel, system.DisplacedLitres);

        // The banked row was replaced, not added to.
        Assert.Equal(0, system.SeedOverflowLitres);
        system.Bank();
        Assert.Equal(FullAtSeven, system.Ledger.TotalLitres);
        Basin westBasin = system.Window!.Watershed.Basins[west];
        Basin eastBasin = system.Window.Watershed.Basins[east];
        Assert.Equal(64 * PerVoxel, system.Ledger.LitresAt(westBasin.PitX, westBasin.PitZ));
        Assert.Equal(32 * PerVoxel, system.Ledger.LitresAt(eastBasin.PitX, eastBasin.PitZ));
    }

    [Fact]
    public void PouringIntoOneSideRaisesThatSideOnly()
    {
        WaterSystem system = Filled(FullAtSeven);
        Reanalyse(system, WallAtFive());
        double west = Level(system, 2, 3);

        Assert.Equal(0, system.Inject(BasinAt(system, 9, 6), 16 * PerVoxel));

        Assert.Equal(8.0, Level(system, 9, 6), 9);
        Assert.Equal(west, Level(system, 2, 3), 9);
    }

    [Fact]
    public void TakingTheWallDownGivesOneLevelAgainWithTheTotalConserved()
    {
        WaterSystem system = Filled(FullAtSeven);
        Reanalyse(system, WallAtFive());
        system.Inject(BasinAt(system, 9, 6), 16 * PerVoxel);

        // Ctrl+Z, or the pick: the raise goes and the same ground is analysed again. The wall's
        // cells stood above both lakes, so there is nothing more to displace or split.
        Reanalyse(system, null);

        Assert.Equal(BasinAt(system, 2, 3), BasinAt(system, 9, 6));
        Assert.Equal(7.5, Level(system, 2, 3), 9);
        Assert.Equal(1, system.SplitLakes);
        Assert.Equal(4 * 4 * PerVoxel, system.DisplacedLitres);
        Assert.Equal(0, system.SeedOverflowLitres);
        system.Bank();
        Assert.Equal(FullAtSeven + (16 * PerVoxel), system.Ledger.TotalLitres);
    }

    [Fact]
    public void ARingRoundADryAreaAndARaiseElsewhereMoveNoLitre()
    {
        WaterSystem system = Filled(FullAtSeven);
        Basin before = Assert.Single(system.Window!.Watershed.Basins);

        // A ring on the dry flat round (8, 8), and one raised sample on the flat at (3, 8).
        Reanalyse(
            system,
            Raise(
                ((7, 7), 12), ((8, 7), 12), ((9, 7), 12),
                ((7, 8), 12), ((9, 8), 12),
                ((7, 9), 12), ((8, 9), 12), ((9, 9), 12),
                ((3, 8), 11)));

        Assert.Equal(7.0, Level(system, 2, 3), 9);
        Assert.Equal(FullAtSeven, Held(system, 2, 3));
        Assert.Null(system.LevelAt(BasinAt(system, 8, 8)));
        Assert.Equal(0, system.SeedOverflowLitres);
        Assert.Equal(0, system.SplitLakes);
        Assert.Equal(0, system.DisplacedLitres);

        system.Bank();
        Assert.Equal(FullAtSeven, system.Ledger.LitresAt(before.PitX, before.PitZ));
        Assert.Equal(FullAtSeven, system.Ledger.TotalLitres);
    }

    [Fact]
    public void ARingBuiltInALakeKeepsTheWaterThatStoodInsideIt()
    {
        WaterSystem system = Filled(FullAtSeven);

        Reanalyse(system, RingRoundFourFour());

        Assert.NotEqual(LakeOf(system, 4, 4), LakeOf(system, 2, 3));

        // Inside: the one cell's 4 voxels of depth, so its level stays at 7. Outside: its own 7 cells
        // at plane 3 and 16 at plane 5, plus the ring's 8 cells 4 deep.
        Assert.Equal(4 * PerVoxel, Held(system, 4, 4));
        Assert.Equal(7.0, Level(system, 4, 4), 9);
        Assert.Equal(((7 * 4) + (16 * 2) + (8 * 4)) * PerVoxel, Held(system, 2, 3));
        Assert.Equal((92.0 + (7 * 3) + (16 * 5)) / 23.0, Level(system, 9, 6), 9);
        Assert.Equal(0, system.SeedOverflowLitres);
        Assert.Equal(1, system.SplitLakes);
        Assert.Equal(8 * 4 * PerVoxel, system.DisplacedLitres);
    }

    [Fact]
    public void ALakeSharedByLevelKeepsEveryLitreWhenTheSharesAreNotWhole()
    {
        // An odd number of litres puts the level between voxels, so neither side's share is whole
        // and the remainder has to land somewhere.
        long poured = FullAtSeven + 7;
        WaterSystem system = Filled(poured);

        Reanalyse(system, WallAtFive());

        Assert.Equal(poured, Held(system, 2, 3) + Held(system, 9, 6));
        system.Bank();
        Assert.Equal(poured, system.Ledger.TotalLitres + system.SeedOverflowLitres);
        Assert.Equal(0, system.SeedOverflowLitres);
    }

    [Fact]
    public void TheRemainderGoesToTheLargestFraction()
    {
        // 96,007 L puts the level at 7.00021875. Inside the ring, one cell over a floor at 3:
        // 4,000.21875 L. Outside, the rest: 92,006.78125 L. The floors leave one litre over, and
        // it goes to the larger fraction, outside.
        long poured = FullAtSeven + 7;
        WaterSystem system = Filled(poured);

        Reanalyse(system, RingRoundFourFour());

        Assert.Equal(4_000, Held(system, 4, 4));
        Assert.Equal(92_007, Held(system, 2, 3));
    }

    [Fact]
    public void AWallUnderTheWaterSplitsNothingAndDisplacesItsOwnHeight()
    {
        WaterSystem system = Filled(FullAtSeven);

        // A wall to plane 6, a voxel under the surface: one lake over it still.
        Reanalyse(system, WallAtFive(plane: 6));

        Assert.NotEqual(BasinAt(system, 2, 3), BasinAt(system, 9, 6));
        Assert.Equal(LakeOf(system, 2, 3), LakeOf(system, 9, 6));
        Assert.Equal(0, system.SplitLakes);
        Assert.Equal(4 * 3 * PerVoxel, system.DisplacedLitres);

        // 12 cells at 3, the wall's 4 at 6 and 16 at 5 under 96 voxels of depth: 32 L = 236.
        Assert.Equal(236.0 / 32.0, Level(system, 2, 3), 9);
        Assert.Equal(236.0 / 32.0, Level(system, 9, 6), 9);
        Assert.Equal(FullAtSeven, Held(system, 2, 3));
    }

    [Fact]
    public void AWallRaisedThroughTheSurfaceSplitsTheLakeAtTheLevelItHadAboveBothPits()
    {
        // First under the water, so the old lake is two pits under one surface, its level 7.375
        // read from the node above them both. Then up through the surface to plane 12.
        WaterSystem system = Filled(FullAtSeven);
        Reanalyse(system, WallAtFive(plane: 6));

        Reanalyse(system, WallAtFive());

        // West: 12 cells 4.375 deep and the wall's 4 at 1.375. East: 16 cells 2.375 deep.
        Assert.Equal(58 * PerVoxel, Held(system, 2, 3));
        Assert.Equal(38 * PerVoxel, Held(system, 9, 6));
        Assert.Equal(3.0 + (58.0 / 12.0), Level(system, 2, 3), 9);
        Assert.Equal(7.375, Level(system, 9, 6), 9);
        Assert.Equal(1, system.SplitLakes);
        Assert.Equal((12 + (4 * 1.375)) * PerVoxel, system.DisplacedLitres);
    }

    // ---- Where a raised cell's water goes when it no longer drains to a lake ----

    /// <summary>
    /// A five-sample window: high ground at plane 10 with one pit at plane 3 at (1, 2), next to the
    /// edge ring, which stands at plane 5 — the pit's only way out.
    /// </summary>
    private static readonly SampleGrid PondByTheEdge = new(PondByTheEdgeGrid(), outside: 4);

    private static int[,] PondByTheEdgeGrid()
    {
        var grid = new int[5, 5];
        for (int z = 0; z < 5; z++)
        {
            for (int x = 0; x < 5; x++)
            {
                bool edge = x == 0 || z == 0 || x == 4 || z == 4;
                grid[z, x] = edge ? 4 : 9;
            }
        }

        grid[2, 1] = 2;
        return grid;
    }

    [Fact]
    public void FillingAPondWithABlockSendsItsWaterWhereTheBlockNowDrainsAndCountsWhatLeaves()
    {
        var system = new WaterSystem(PondByTheEdge, 7, new BasinLedger(), pitch: 5);
        system.Install(WaterSystem.Analyse(PondByTheEdge, 0, 0, 5, Cell));
        Basin pond = Assert.Single(system.Window!.Watershed.Basins);
        system.Inject(pond.Index, PerVoxel);

        // The block fills the pond to plane 8, above the edge it spilled over, so the pond's cell
        // now drains off the window and the litres it held leave the world.
        system.Install(WaterSystem.Analyse(PondByTheEdge, 0, 0, 5, Cell, Raise(((1, 2), 8))));
        system.Bank();

        Assert.Empty(system.Window!.Watershed.Basins);
        Assert.Equal(PerVoxel, system.SeedOverflowLitres);
        Assert.Equal(PerVoxel, system.DisplacedLitres);
        Assert.Equal(0, system.Ledger.TotalLitres);
    }

    // ---- A stored lake the window cannot see is left alone ----

    /// <summary>RaisedSurfaceTests' bowl: an edge ring, a rim at plane 6, a floor at 3, a pit at 2 in the middle.</summary>
    private static int[,] Bowl()
    {
        var grid = new int[9, 9];
        for (int z = 0; z < 9; z++)
        {
            for (int x = 0; x < 9; x++)
            {
                bool edge = x == 0 || x == 8 || z == 0 || z == 8;
                bool rim = !edge && (x == 1 || x == 7 || z == 1 || z == 7);
                grid[z, x] = edge ? 1 : rim ? 5 : 2;
            }
        }

        grid[4, 4] = 1;
        return grid;
    }

    private static readonly SampleGrid BowlTerrain = new(Bowl(), outside: 1);

    [Fact]
    public void ARaiseInAMovedWindowLeavesAStoredLakeWhosePitFallsOnItsEdge()
    {
        // The bowl filled, then the window moved four samples east: the old pit is on the new
        // window's edge ring, so the ledger holds its water for a window that can see it.
        var system = new WaterSystem(BowlTerrain, 7, new BasinLedger(), pitch: 9);
        system.Install(WaterSystem.Analyse(BowlTerrain, 0, 0, 9, Cell));
        long poured = 30 * PerVoxel;
        Basin pit = Assert.Single(system.Window!.Watershed.Basins, b => b.PitX == 4 * Cell && b.PitZ == 4 * Cell);
        system.Inject(pit.Index, poured);
        system.Install(WaterSystem.Analyse(BowlTerrain, 4 * Cell, 0, 9, Cell));

        // A raise in the moved window: the same ground again, at the same origin. Its predecessor
        // deleted the stored lake here, counting it as water that would not fit back.
        system.Install(WaterSystem.Analyse(BowlTerrain, 4 * Cell, 0, 9, Cell, Raise(((10, 4), 9))));
        system.Bank();

        Assert.Equal(poured, system.Ledger.LitresAt(4 * Cell, 4 * Cell));
        Assert.Equal(0, system.SeedOverflowLitres);
    }
}
