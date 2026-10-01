using Overburden.Core.Hydrology;
using Overburden.Core.Lighting;
using Overburden.Core.Persistence;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// The raised watershed (docs/POND-PLAN.md, slice a): the player's material above the seed raises
/// a sample to the highest placed top in its cell, a ring on a pond's rim raises its saddle, and a
/// pit a raise covers hands its water to the basin now under it.
/// </summary>
public sealed class RaisedSurfaceTests
{
    private const int Cell = HeightField.DefaultCellVoxels;
    private const int Surface = 40;

    private const long PerVoxel =
        (Cell / VoxelConstants.VoxelsPerMetre) * (Cell / VoxelConstants.VoxelsPerMetre) * 250L;

    private static SkyColumn FlatColumn(int chunkX, int chunkZ)
    {
        var column = new SkyColumn(chunkX, chunkZ);
        column.Seed(new FlatSurface(Surface));
        return column;
    }

    private static RaisedSamples Raise(params ((int X, int Z) Sample, int Plane)[] raises)
    {
        var map = new Dictionary<(int X, int Z), int>();
        foreach (((int X, int Z) sample, int plane) in raises) map[sample] = plane;
        return new RaisedSamples(map);
    }

    // ---- The map ----

    [Fact]
    public void AWallAnywhereInACellRaisesTheWholeCellToItsTop()
    {
        var raised = new RaisedSurface();
        SkyColumn column = FlatColumn(0, 0);

        // One placed column deep inside cell (0, 0), and one on the far edge of cell (1, 0): a
        // wall a voxel thick, which the corner sample would never see.
        column.SetOpenAboveY(5, 5, Surface + 5);
        column.SetOpenAboveY((2 * Cell) - 1, 2, Surface + 3);

        Assert.True(raised.Observe(column));
        Assert.True(raised.TryGetRaised(0, 0, out int a));
        Assert.Equal(Surface + 5, a);
        Assert.True(raised.TryGetRaised(1, 0, out int b));
        Assert.Equal(Surface + 3, b);
        Assert.False(raised.TryGetRaised(2, 0, out _));
        Assert.Equal(2, raised.Count);
        Assert.Equal(1, raised.Version);
    }

    [Fact]
    public void ANegativeChunkColumnFilesItsCellsOnTheWorldLattice()
    {
        var raised = new RaisedSurface();
        SkyColumn column = FlatColumn(-1, -1);
        column.SetOpenAboveY(0, VoxelConstants.ChunkSize - 1, Surface + 2);

        raised.Observe(column);

        // Chunk -1 covers voxels -32..-1: its first cell is sample -4, its last -1.
        Assert.True(raised.TryGetRaised(-4, -1, out int plane));
        Assert.Equal(Surface + 2, plane);
        Assert.Equal(1, raised.Count);
    }

    [Fact]
    public void ADugColumnAndTheSurfaceItselfRaiseNothingAndOneBlockOnTheGrassDoes()
    {
        var raised = new RaisedSurface();
        SkyColumn column = FlatColumn(0, 0);
        column.SetOpenAboveY(1, 1, Surface - 10);   // dug: Tier B's, never a raise
        column.SetOpenAboveY(9, 1, Surface + 1);    // the seed plane itself: nothing placed
        raised.Observe(column);
        Assert.Equal(0, raised.Count);

        column.SetOpenAboveY(17, 1, Surface + 2);   // one block on the grass
        raised.Observe(column);
        Assert.True(raised.TryGetRaised(2, 0, out int plane));
        Assert.Equal(Surface + 2, plane);
    }

    [Fact]
    public void ARoofOverAirReadsAsRaisedTheStatedCost()
    {
        // Decision 2's cost, pinned: an open height cannot tell a wall from a roof, so a slab
        // twenty voxels up over open ground raises its cell as a wall would.
        var raised = new RaisedSurface();
        SkyColumn column = FlatColumn(0, 0);
        column.SetOpenAboveY(3, 3, Surface + 20);

        raised.Observe(column);

        Assert.True(raised.TryGetRaised(0, 0, out int plane));
        Assert.Equal(Surface + 20, plane);
    }

    [Fact]
    public void TheSameRevisionIsScannedOnceAndTakingTheWallDownRemovesTheRaise()
    {
        var raised = new RaisedSurface();
        SkyColumn column = FlatColumn(0, 0);
        column.SetOpenAboveY(4, 4, Surface + 4);

        Assert.True(raised.Observe(column));
        Assert.False(raised.Observe(column));
        Assert.Equal(1, raised.Scans);
        Assert.Equal(1, raised.Version);

        column.SetOpenAboveY(4, 4, Surface + 1);
        Assert.True(raised.Observe(column));
        Assert.Equal(0, raised.Count);
        Assert.Equal(2, raised.Version);
    }

    [Fact]
    public void AnEditThatLeavesEveryCellsMaximumAloneMovesNoVersion()
    {
        var raised = new RaisedSurface();
        SkyColumn column = FlatColumn(0, 0);
        column.SetOpenAboveY(1, 1, Surface + 6);
        raised.Observe(column);

        // A lower block in the same cell: the column's revision moves, the cell's max does not.
        column.SetOpenAboveY(2, 2, Surface + 3);
        Assert.False(raised.Observe(column));
        Assert.Equal(2, raised.Scans);
        Assert.Equal(1, raised.Version);
    }

    [Fact]
    public void AColumnThatUnloadsKeepsItsRaiseAndAFreshCopyOfItChangesNothing()
    {
        var raised = new RaisedSurface();
        SkyColumn column = FlatColumn(0, 0);
        column.SetOpenAboveY(6, 6, Surface + 5);
        raised.Observe(column);

        // The chunk column leaves the box and comes back as a new object with the same heights:
        // a new revision, so it is scanned, and the same answer, so no version.
        SkyColumn again = FlatColumn(0, 0);
        again.CopyFrom(column);
        Assert.False(raised.Observe(again));
        Assert.True(raised.TryGetRaised(0, 0, out int plane));
        Assert.Equal(Surface + 5, plane);
        Assert.Equal(1, raised.Version);
    }

    [Fact]
    public void DerivingFromTheStoreMatchesObservingTheLiveColumns()
    {
        var store = new InMemoryWorldStore();
        var live = new RaisedSurface();
        var columns = new List<(int X, int Z, byte[] Blob)>();
        foreach ((int x, int z) in new[] { (0, 0), (-2, 3), (5, -1) })
        {
            SkyColumn column = FlatColumn(x, z);
            column.SetOpenAboveY((x * 7) & 31, (z * 5) & 31, Surface + 3 + x);
            column.SetOpenAboveY(31, 0, Surface - 4);
            live.Observe(column);
            columns.Add((x, z, SkyColumnBlob.Write(column)));
        }

        store.Write(new WorldBatch { Columns = columns });

        var derived = new RaisedSurface();
        Assert.Equal(3, derived.DeriveFrom(store, new FlatSurface(Surface)));
        Assert.Equal(live.Count, derived.Count);
        foreach ((int x, int z) in new[] { (0, 0), (-8, 12), (20, -4), (1, 0) })
        {
            Assert.Equal(live.TryGetRaised(x, z, out int a), derived.TryGetRaised(x, z, out int b));
            Assert.Equal(a, b);
        }
    }

    [Fact]
    public void ASnapshotDoesNotSeeLaterEdits()
    {
        var raised = new RaisedSurface();
        SkyColumn column = FlatColumn(0, 0);
        column.SetOpenAboveY(1, 1, Surface + 2);
        raised.Observe(column);
        RaisedSamples snapshot = raised.Snapshot();

        column.SetOpenAboveY(1, 1, Surface + 9);
        raised.Observe(column);

        Assert.True(snapshot.TryGetRaised(0, 0, out int plane));
        Assert.Equal(Surface + 2, plane);
    }

    // ---- The field ----

    [Fact]
    public void NoRaiseInTheWindowSamplesTheFieldByteIdentically()
    {
        var terrain = new AnalyticSurface((x, z) => ((x * 31) ^ (z * 17)) % 23);
        int origin = -40 * Cell;
        HeightField plain = HeightField.Sample(terrain, origin, origin, pitch: 32);
        HeightField empty = HeightField.Sample(terrain, origin, origin, 32, Cell, RaisedSamples.Empty);
        HeightField outside = HeightField.Sample(
            terrain, origin, origin, 32, Cell, Raise(((500, 500), 1_000), ((-41, -40), 1_000)));

        Assert.True(plain.Heights.SequenceEqual(empty.Heights));
        Assert.True(plain.Heights.SequenceEqual(outside.Heights));
    }

    [Fact]
    public void ARaiseNeverLowersASample()
    {
        var terrain = new AnalyticSurface((_, _) => 20);
        // Off the diagonal, so a lookup with its axes swapped reads a different cell.
        HeightField field = HeightField.Sample(terrain, 0, 0, 4, Cell, Raise(((1, 3), 5), ((3, 0), 30)));

        Assert.Equal(21, field.HeightAt(1, 3));
        Assert.Equal(30, field.HeightAt(3, 0));
        Assert.Equal(21, field.HeightAt(0, 3));
    }

    /// <summary>
    /// A bowl: an edge ring at 1 (drains out of the window), a rim ring at 5, a floor at 2 with a
    /// pit at 1 in the middle. Planes are one higher (see SampleGrid).
    /// </summary>
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

    private static RaisedSamples RimAt(int plane)
    {
        var raises = new List<((int X, int Z), int)>();
        for (int z = 1; z <= 7; z++)
        {
            for (int x = 1; x <= 7; x++)
            {
                if (x == 1 || x == 7 || z == 1 || z == 7) raises.Add(((x, z), plane));
            }
        }

        return Raise([.. raises]);
    }

    private static Basin PitBasin(Watershed watershed, int sx = 4, int sz = 4) =>
        watershed.Basins.Single(b => b.PitX == sx * Cell && b.PitZ == sz * Cell);

    [Fact]
    public void ARingOnTheRimRaisesTheSaddleToItsTopAndTheCapacityByTheInsideTimesTheRise()
    {
        Watershed natural = Watershed.Analyse(HeightField.Sample(BowlTerrain, 0, 0, 9));
        Watershed walled = Watershed.Analyse(HeightField.Sample(BowlTerrain, 0, 0, 9, Cell, RimAt(10)));

        Basin before = PitBasin(natural);
        Basin after = PitBasin(walled);

        // Natural: the rim plane is 6, the pit 2, the floor 3 — 4 + 24 × 3 voxels of depth.
        Assert.Equal(6, before.SaddleHeight);
        Assert.Equal((4 + (24 * 3)) * PerVoxel, before.CapacityLitres);

        // Walled at plane 10: the same 25 cells, four voxels deeper each.
        Assert.Equal(10, after.SaddleHeight);
        Assert.Equal((8 + (24 * 7)) * PerVoxel, after.CapacityLitres);
        Assert.Equal(25 * 4 * PerVoxel, after.CapacityLitres - before.CapacityLitres);
    }

    [Fact]
    public void AGapOneCellWideInTheRingIsWhereTheLakeSpills()
    {
        RaisedSamples ring = RimAt(10);
        var map = new Dictionary<(int X, int Z), int>();
        for (int z = 0; z < 9; z++)
        {
            for (int x = 0; x < 9; x++)
            {
                if (ring.TryGetRaised(x, z, out int plane)) map[(x, z)] = plane;
            }
        }

        map.Remove((4, 1));    // one cell of the rim left at its natural height
        Watershed gapped = Watershed.Analyse(
            HeightField.Sample(BowlTerrain, 0, 0, 9, Cell, new RaisedSamples(map)));

        Assert.Equal(6, PitBasin(gapped).SaddleHeight);
    }

    // ---- The ledger ----

    private static WaterSystem Installed(IRaisedSamples? raised)
    {
        var system = new WaterSystem(BowlTerrain, 7, new BasinLedger(), pitch: 9);
        system.Install(WaterSystem.Analyse(BowlTerrain, 0, 0, 9, Cell, raised));
        return system;
    }

    private static int IndexOfPit(WaterSystem system, int sx = 4, int sz = 4) =>
        PitBasin(system.Window!.Watershed, sx, sz).Index;

    [Fact]
    public void TakingTheRingDownSpillsWhatTheNaturalRimCannotHold()
    {
        WaterSystem system = Installed(RimAt(10));
        long poured = 150 * PerVoxel;
        system.Inject(IndexOfPit(system), poured);

        system.Install(WaterSystem.Analyse(BowlTerrain, 0, 0, 9, Cell));
        system.Bank();

        long natural = (4 + (24 * 3)) * PerVoxel;
        Assert.Equal(natural, system.Ledger.TotalLitres);
        Assert.Equal(poured - natural, system.SeedOverflowLitres);
        Assert.Equal(0, system.SplitLakes);
    }

    [Fact]
    public void APierOverThePitSendsItsWaterToTheBasinNowUnderIt()
    {
        WaterSystem system = Installed(null);
        long poured = 30 * PerVoxel;
        system.Inject(IndexOfPit(system), poured);

        // A pier over the pit: that sample is raised above the floor, so the bowl's pit moves.
        system.Install(WaterSystem.Analyse(BowlTerrain, 0, 0, 9, Cell, Raise(((4, 4), 5))));
        system.Bank();

        Assert.Equal(0, system.SplitLakes);
        Assert.Equal(0, system.SeedOverflowLitres);
        Assert.Equal(poured, system.Ledger.TotalLitres);
        Assert.Equal(0, system.Ledger.LitresAt(4 * Cell, 4 * Cell));
        Basin now = Assert.Single(system.Window!.Watershed.Basins, b => b.CatchmentSamples > 1);
        Assert.Equal(poured, system.Ledger.LitresAt(now.PitX, now.PitZ));

        // The pier took the room of the water over the old pit: the level stood at (30 + 2 + 72)
        // / 25 = 4.16 over a floor at 2, and the pier's top at 5 is above it.
        Assert.Equal(2_160, system.DisplacedLitres);
    }

    [Fact]
    public void ASharedLakeJoinsWhatTheNewPitAlreadyHeldAndARowOutsideTheWindowStays()
    {
        Watershed pier = Watershed.Analyse(HeightField.Sample(BowlTerrain, 0, 0, 9, Cell, Raise(((4, 4), 5))));
        Basin now = Assert.Single(pier.Basins, b => b.CatchmentSamples > 1);
        Assert.NotEqual((4 * Cell, 4 * Cell), (now.PitX, now.PitZ));

        // A row waiting at the pier window's pit (not a pit of the open window, so not poured),
        // and one far outside the window.
        var ledger = new BasinLedger();
        ledger.Load(
            [
                new StoredBasin(now.PitX, now.PitZ, 12),
                new StoredBasin(400 * Cell, 4 * Cell, 99),
            ],
            storedCellMetres: 2,
            currentCellMetres: 2);
        var system = new WaterSystem(BowlTerrain, 7, ledger, pitch: 9);
        system.Install(WaterSystem.Analyse(BowlTerrain, 0, 0, 9, Cell));
        system.Inject(IndexOfPit(system), 30);

        system.Install(WaterSystem.Analyse(BowlTerrain, 0, 0, 9, Cell, Raise(((4, 4), 5))));
        system.Bank();

        Assert.Equal(42, ledger.LitresAt(now.PitX, now.PitZ));
        Assert.Equal(99, ledger.LitresAt(400 * Cell, 4 * Cell));
        Assert.Equal(0, ledger.LitresAt(4 * Cell, 4 * Cell));
        Assert.Equal(42, system.StoredLitres);
    }

    [Fact]
    public void AWindowMoveNeverRehomesAPitThatFallsOnItsEdge()
    {
        // The control for the same-origin guard: moved four samples east, the old pit is on the
        // new window's edge ring — not a pit there, and not a pit that a raise covered.
        WaterSystem system = Installed(null);
        long poured = 30 * PerVoxel;
        system.Inject(IndexOfPit(system), poured);

        system.Install(WaterSystem.Analyse(BowlTerrain, 4 * Cell, 0, 9, Cell));
        system.Bank();

        Assert.Equal(0, system.DisplacedLitres);
        Assert.Equal(poured, system.Ledger.LitresAt(4 * Cell, 4 * Cell));
    }
}
