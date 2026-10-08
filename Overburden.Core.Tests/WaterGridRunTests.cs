using Overburden.Core.Content;
using Overburden.Core.Fluid;
using Overburden.Core.Lighting;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// The overhang pass (docs/POND-PLAN.md, 2026-09-26): a built column's water room is its lowest open
/// run above the natural ground, and water crosses between columns only where their runs overlap
/// under the level. Matthew's play: water under his overhang filled from Tier B's side and never
/// from the lake's, because the grid read the overhang as a wall up to its top.
/// </summary>
public sealed class WaterGridRunTests
{
    private const int N = VoxelConstants.ChunkSize;
    private const int Cell = 8;
    private const int Surface = 40;          // natural ground to y 40; the seed plane is 41
    private const double Pond = 45.5;
    private const int Stone = 1;

    /// <summary>
    /// Flat sky columns whose tops follow the voxels placed in them, and the voxels themselves:
    /// solid to <see cref="Surface"/>, then whatever was placed. Clearing a voxel under a column's
    /// top leaves the top, and so its revision, alone — the case the grid's edit count exists for.
    /// </summary>
    private sealed class World : ISkyColumns, IVoxelWorld
    {
        private readonly Dictionary<(int, int), SkyColumn> _columns = [];
        private readonly HashSet<(int, int, int)> _placed = [];
        private readonly HashSet<(int, int, int)> _cleared = [];

        /// <summary>Voxels at y in [From, To) are not loaded: the world cannot answer them.</summary>
        public (int From, int To)? UnloadedY { get; set; }

        public SkyColumn? ColumnAt(int chunkX, int chunkZ)
        {
            if (!_columns.TryGetValue((chunkX, chunkZ), out SkyColumn? column))
            {
                column = new SkyColumn(chunkX, chunkZ);
                column.Seed(new FlatSurface(Surface));
                _columns[(chunkX, chunkZ)] = column;
            }

            return column;
        }

        public bool TryGetMaterial(VoxelPos voxel, out int materialId)
        {
            (int, int, int) key = (voxel.X, voxel.Y, voxel.Z);
            if (UnloadedY is { } unloaded && voxel.Y >= unloaded.From && voxel.Y < unloaded.To)
            {
                materialId = MaterialIds.AirNumericId;
                return false;
            }

            bool solid = !_cleared.Contains(key) && (voxel.Y <= Surface || _placed.Contains(key));
            materialId = solid ? Stone : MaterialIds.AirNumericId;
            return true;
        }

        /// <summary>Places stone at y in [y0, y1) in one voxel column, raising its top to match.</summary>
        public void Place(int x, int z, int y0, int y1)
        {
            for (int y = y0; y < y1; y++)
            {
                _placed.Add((x, y, z));
                _cleared.Remove((x, y, z));
            }

            int cx = Floor(x, N), cz = Floor(z, N);
            SkyColumn column = ColumnAt(cx, cz)!;
            int lx = x - (cx * N), lz = z - (cz * N);
            if (y1 > column.OpenAboveY(lx, lz))
            {
                column.SetOpenAboveY(lx, lz, y1);
            }
        }

        /// <summary>Clears y in [y0, y1) under a column's top — its top and revision do not move.</summary>
        public void Clear(int x, int z, int y0, int y1)
        {
            for (int y = y0; y < y1; y++)
            {
                _cleared.Add((x, y, z));
            }
        }

        /// <summary>A ring of wall [41, top) round the room x0..x1, z0..x1 (the ring itself included).</summary>
        public void Ring(int x0, int x1, int z0, int z1, int top)
        {
            for (int x = x0; x <= x1; x++)
            {
                Place(x, z0, Surface + 1, top);
                Place(x, z1, Surface + 1, top);
            }

            for (int z = z0 + 1; z < z1; z++)
            {
                Place(x0, z, Surface + 1, top);
                Place(x1, z, Surface + 1, top);
            }
        }
    }

    private sealed class Table(Func<int, int, double?> at) : IWaterTable
    {
        public double? TableAt(int sampleX, int sampleZ) => at(sampleX, sampleZ);
    }

    private static readonly Table PondEverywhere = new((_, _) => Pond);

    private static MaterialTable Materials() => MaterialTable.ForTesting(
        isOpaque: [false, true],
        isFluid: [false, false],
        textureLayer: [0, 1],
        hardness: [0f, 3f]);

    private static int Floor(int value, int divisor) =>
        value >= 0 ? value / divisor : -((-value + divisor - 1) / divisor);

    private static double? Level(WaterColumns grid, int localX, int localZ) =>
        grid.TryGetLevel(localX, localZ, out double level) ? level : null;

    private static WaterColumns Build(World world, bool voxels = true) =>
        WaterGrid.Build(0, 0, world, PondEverywhere, Cell, voxels ? world : null, voxels ? Materials() : null)!;

    [Theory]
    [InlineData(46, 47)]   // the underside above the water: the room under it is the ground to 46
    [InlineData(44, 47)]   // dipping under the water: the room under it is the ground to 44
    public void AnOverhangBesideTheLakeIsWetUnderneathWhereItWasAWallBefore(int underside, int top)
    {
        var world = new World();

        // A roof over the whole of cell (3, 3), open on every side to the unbuilt cells round it.
        for (int x = 24; x < 32; x++)
        {
            for (int z = 24; z < 32; z++)
            {
                world.Place(x, z, underside, top);
            }
        }

        WaterColumns grid = Build(world);
        for (int x = 24; x < 32; x++)
        {
            for (int z = 24; z < 32; z++)
            {
                Assert.Equal(Pond, Level(grid, x, z));
            }
        }

        // The rule before the pass read the overhang as a wall to its top, which is the play's
        // finding: the lake stopped at the edge.
        WaterColumns before = Build(world, voxels: false);
        Assert.Null(Level(before, 28, 28));
    }

    [Fact]
    public void AWalledRoofedRoomUnderTheLakeStaysDry()
    {
        var world = new World();
        world.Ring(9, 14, 9, 14, 47);
        for (int x = 10; x <= 13; x++)
        {
            for (int z = 10; z <= 13; z++)
            {
                world.Place(x, z, 46, 47);   // the roof, the room [41, 46) under it
            }
        }

        WaterColumns grid = Build(world);
        for (int x = 10; x <= 13; x++)
        {
            for (int z = 10; z <= 13; z++)
            {
                Assert.Null(Level(grid, x, z));
            }
        }

        Assert.Equal(Pond, Level(grid, 8, 11));   // outside the ring, the lake
    }

    [Theory]
    [InlineData(43, true)]    // a sill under the water: the lake comes through the window
    [InlineData(46, false)]   // a sill over it: the wall holds
    public void AWallHoldsToItsWindowSill(int sill, bool floods)
    {
        var world = new World();
        world.Ring(9, 14, 9, 14, 47);
        world.Clear(9, 11, sill, sill + 1);   // a one-voxel window in the west wall

        WaterColumns grid = Build(world);
        Assert.Equal(floods ? Pond : null, Level(grid, 11, 11));
        Assert.Equal(floods ? Pond : null, Level(grid, 9, 11));
    }

    [Fact]
    public void ADoorwayAtTheFloorLetsTheLakeIntoTheRoom()
    {
        var world = new World();
        world.Ring(9, 14, 9, 14, 47);
        world.Clear(9, 11, Surface + 1, Surface + 3);   // a doorway two high, the lintel over it

        WaterColumns grid = Build(world);
        Assert.Equal(Pond, Level(grid, 12, 12));

        // The same ring with no doorway is dry inside, so the doorway is what let it in.
        var sealedRoom = new World();
        sealedRoom.Ring(9, 14, 9, 14, 47);
        Assert.Null(Level(Build(sealedRoom), 12, 12));
    }

    [Fact]
    public void TwoRunsThatDoNotOverlapDoNotConnectEvenUnderTheLevel()
    {
        // A window [44, 45) in the west wall, and behind it a room whose floor is built up to 45:
        // the window's water faces solid, and the room's run [45, ∞) starts where the window's
        // ends. Both floors are under 45.5, which is all the rule before the pass asked.
        var world = new World();
        world.Ring(9, 14, 9, 14, 47);
        world.Clear(9, 11, 44, 45);
        for (int x = 10; x <= 13; x++)
        {
            for (int z = 10; z <= 13; z++)
            {
                world.Place(x, z, Surface + 1, 45);
            }
        }

        WaterColumns grid = Build(world);
        Assert.Equal(Pond, Level(grid, 9, 11));    // the window itself is the lake's
        Assert.Null(Level(grid, 10, 11));          // behind it, solid at the window's height
        Assert.Null(Level(grid, 12, 12));
    }

    [Fact]
    public void AnEditUnderAColumnsTopRebuildsTheGridOnceItIsCounted()
    {
        var world = new World();
        world.Ring(9, 14, 9, 14, 47);
        var grids = new WaterGrids(world, PondEverywhere, Cell, world, Materials());
        Assert.Null(Level(grids.Get(0, 0)!, 12, 12));

        // A doorway cut under the wall's top: no sky revision moves.
        world.Clear(9, 11, Surface + 1, Surface + 3);
        Assert.Null(Level(grids.Get(0, 0)!, 12, 12));   // not counted: the cache is right to keep it

        grids.VoxelChanged(new VoxelPos(9, Surface + 1, 11));
        Assert.Equal(Pond, Level(grids.Get(0, 0)!, 12, 12));
    }

    [Fact]
    public void AnEditInANeighbouringChunkColumnIsInTheKeyToo()
    {
        // The pad reads a cell of the chunk column next door, so a door cut there moves this grid.
        // The room straddles x = -32: the grid is chunk column (-2, 0), and the east wall with its
        // door is at x = -29, in (-1, 0), inside the pad. Rounding -29 toward zero would count the
        // edit against (0, 0), which this grid's key does not hold.
        var world = new World();
        world.Ring(-36, -29, 9, 14, 47);
        var grids = new WaterGrids(world, PondEverywhere, Cell, world, Materials());
        Assert.Null(Level(grids.Get(-2, 0)!, 31, 11));   // x = -33, inside the room

        world.Clear(-29, 11, Surface + 1, Surface + 3);
        grids.VoxelChanged(new VoxelPos(-29, Surface + 1, 11));
        Assert.Equal(Pond, Level(grids.Get(-2, 0)!, 31, 11));
    }

    [Fact]
    public void APocketUnderARoofBesideARaisedFloorIsNotReachedOverTheFloor()
    {
        // The flood comes down: through a window at 45 onto a floor built up to 45, whose water
        // stands [45, 45.5). Beside it a pocket under a roof, open [41, 44): at those heights the
        // raised floor is solid, so the two never meet. The pocket's own floor is under the level,
        // which is all a test on the entered column alone would ask.
        var world = new World();
        world.Ring(9, 14, 9, 14, 47);
        world.Clear(9, 11, 45, 46);
        for (int x = 10; x <= 13; x++)
        {
            for (int z = 10; z <= 13; z++)
            {
                if ((x, z) == (10, 11)) world.Place(x, z, Surface + 1, 45);        // the raised floor
                else if ((x, z) == (11, 11)) world.Place(x, z, 44, 47);            // the roofed pocket
                else world.Place(x, z, Surface + 1, 47);                          // solid
            }
        }

        WaterColumns grid = Build(world);
        Assert.Equal(Pond, Level(grid, 10, 11));
        Assert.Null(Level(grid, 11, 11));
    }

    [Fact]
    public void AnUnloadedVoxelEndsARunAsSolid()
    {
        // The overhang of the first test, with the voxels under it unloaded: the grid cannot see
        // the room there, so it leaves the water out — missing, never guessed.
        var world = new World { UnloadedY = (Surface + 1, 46) };
        for (int x = 24; x < 32; x++)
        {
            for (int z = 24; z < 32; z++)
            {
                world.Place(x, z, 46, 47);
            }
        }

        Assert.Null(Level(Build(world), 28, 28));
    }

    // A built zone from x 16 to 48, wider than a grid's pad on either side of the seam at x 32: a
    // pillar at each cell's corner marks the cell built and blocks only its own column. The lake is
    // at x 48 and beyond (samples 6 up); west of it the land is dry (no table).
    private static World BuiltAcrossTheSeam()
    {
        var world = new World();
        for (int cx = 2; cx < 6; cx++)
        {
            for (int cz = -1; cz <= 4; cz++)
            {
                world.Place(cx * Cell, cz * Cell, Surface + 1, 60);
            }
        }

        return world;
    }

    private static double? LakeFromSampleSix(int sampleX, int sampleZ) => sampleX >= 6 ? Pond : null;

    [Fact]
    public void ALakeCrossesTheSeamIntoABuiltZoneWiderThanThePad()
    {
        World world = BuiltAcrossTheSeam();

        // And an overhang in the west, which only the handed water reaches: the west window has no
        // table of its own, so its walks are bounded by the handed level or not made at all.
        for (int x = 17; x < 24; x++)
        {
            for (int z = 17; z < 24; z++)
            {
                world.Place(x, z, 46, 47);
            }
        }
        var grids = new WaterGrids(world, new Table(LakeFromSampleSix), Cell, world, Materials());

        WaterColumns east = grids.Get(1, 0)!;
        Assert.Equal(Pond, Level(east, 1, 10));   // x 33, reached from the lake in its own window
        Assert.Equal(0, east.HopsAt(1, 10));

        WaterColumns west = grids.Get(0, 0)!;
        Assert.Equal(Pond, Level(west, 20, 10));  // x 20: 28 voxels from the lake, handed over the seam
        Assert.Equal(1, west.HopsAt(20, 10));
        Assert.True(west.TryGetSource(20, 10, out int sx, out _) && sx >= 6);
        Assert.Equal(Pond, Level(west, 20, 20));   // under the overhang

        // Without the neighbour the same grid is dry there: the pad alone cannot reach.
        Assert.Null(Level(WaterGrid.Build(0, 0, world, new Table(LakeFromSampleSix), Cell, world, Materials())!, 20, 10));
    }

    [Fact]
    public void ItConvergesWhicheverGridIsAskedFirst()
    {
        World world = BuiltAcrossTheSeam();
        var grids = new WaterGrids(world, new Table(LakeFromSampleSix), Cell, world, Materials());

        Assert.Null(Level(grids.Get(0, 0)!, 20, 10));   // no neighbour built yet
        Assert.Equal(Pond, Level(grids.Get(1, 0)!, 1, 10));
        Assert.Equal(Pond, Level(grids.Get(0, 0)!, 20, 10));   // the neighbour's version moved its key
        long builds = grids.Builds;
        grids.Get(1, 0);
        grids.Get(0, 0);
        Assert.Equal(builds + 1, grids.Builds);   // one more rebuild of the east to see the west, then at rest
        grids.Get(1, 0);
        grids.Get(0, 0);
        Assert.Equal(builds + 1, grids.Builds);
    }

    [Fact]
    public void ALakeCutOffByAWallDoesNotLingerPastTheHopCap()
    {
        World world = BuiltAcrossTheSeam();
        var grids = new WaterGrids(world, new Table(LakeFromSampleSix), Cell, world, Materials());
        grids.Get(1, 0);
        grids.Get(0, 0);
        grids.Get(1, 0);
        Assert.Equal(Pond, Level(grids.Get(0, 0)!, 20, 10));

        // A wall along x 48, taller than the water, the whole depth of both windows.
        for (int z = -8; z < 40; z++)
        {
            world.Place(48, z, Surface + 1, 60);
            grids.VoxelChanged(new VoxelPos(48, Surface + 1, z));
        }

        // The two grids would keep each other wet on each other's last answer; each rebuild adds a
        // hop, and the cap ends it.
        int rounds = 0;
        while (rounds < 4 * WaterGrid.MaxHops
            && (Level(grids.Get(1, 0)!, 1, 10) is not null | Level(grids.Get(0, 0)!, 20, 10) is not null))
        {
            rounds++;
        }

        Assert.Null(Level(grids.Get(1, 0)!, 1, 10));
        Assert.Null(Level(grids.Get(0, 0)!, 20, 10));
        Assert.InRange(rounds, 1, WaterGrid.MaxHops);
    }

    [Fact]
    public void ALakeThatDrainsTakesTheHandedWaterWithItWithinOneRound()
    {
        // The west grid's key holds its own window's samples, not the far sample its water came
        // from, so it sees the drain through the east grid's version: east first, then west. The
        // hop cap is not what ends it — the source's live table is.
        World world = BuiltAcrossTheSeam();
        bool full = true;
        var grids = new WaterGrids(world, new Table((sx, sz) => full ? LakeFromSampleSix(sx, sz) : null), Cell, world, Materials());
        grids.Get(1, 0);
        Assert.Equal(Pond, Level(grids.Get(0, 0)!, 20, 10));

        full = false;
        Assert.Null(Level(grids.Get(1, 0)!, 1, 10));
        Assert.Null(Level(grids.Get(0, 0)!, 20, 10));
    }

    [Fact]
    public void ALakeRisingInsideAVoxelDoesNotRebuildTheNeighbours()
    {
        // The east grid rebuilds for its own reason (an edit) while the lake has risen a fraction
        // of a voxel. Its water reads the same to a neighbour, so the west grid is not rebuilt —
        // compared exactly, every boundary of a rising lake rebuilt the ring of grids (8,363
        // builds in 30 s on his pond).
        World world = BuiltAcrossTheSeam();
        double lake = Pond;
        var grids = new WaterGrids(world, new Table((sx, _) => sx >= 6 ? lake : null), Cell, world, Materials());
        grids.Get(1, 0);
        grids.Get(0, 0);
        grids.Get(1, 0);
        grids.Get(0, 0);
        long builds = grids.Builds;

        lake = Pond + 0.3;   // 45.8: the same floor and ceiling

        // In chunk column (2, 0): inside the east grid's nine, outside the west's, so the east
        // rebuilds and the west has no reason of its own to.
        grids.VoxelChanged(new VoxelPos(72, 70, 10));
        grids.Get(1, 0);
        grids.Get(0, 0);
        Assert.Equal(builds + 1, grids.Builds);
    }
}
