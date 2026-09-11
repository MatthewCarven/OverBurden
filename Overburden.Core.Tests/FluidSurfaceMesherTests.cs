using Overburden.Core.Fluid;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// M8d-1: the Tier B mesh against the debug view it replaces, and against its own rules — a side
/// only where water meets air or a lower fill, a bottom only under a falling cell, every attribute
/// constant across a merged quad, the previous height from the previous build.
/// </summary>
public sealed class FluidSurfaceMesherTests
{
    private const int Cap = FluidConstants.CellCapacityMillilitres;
    private const float Size = VoxelConstants.VoxelSize;

    /// <summary>Open everywhere: every neighbour of every wet cell is air.</summary>
    private static readonly IFluidTerrain Open = new PredicateTerrain(_ => true);

    // ----- the debug view's oracle -----

    [Fact]
    public void TopsCoverExactlyTheSurfaceCellsAtTheirFillHeights()
    {
        // The oracle the plan asked for: the debug view drew one quad per surface cell at the
        // cell's fill height. Enumerate the merged tops back to cells and match the walk's set
        // cell for cell and height for height. The fixture is the walk's own — water across panel
        // boundaries and two bodies in one column — so the tops have to handle both.
        var grid = new FluidGrid();
        var random = new Random(90_10);
        for (int i = 0; i < 4_000; i++)
        {
            grid.Fill(new VoxelPos(random.Next(-40, 40), random.Next(-40, 72), random.Next(-40, 40)), random.Next(1, Cap));
        }

        foreach (int y in new[] { 28, 29, 30, 31, 34, 35 })
        {
            grid.Fill(new VoxelPos(5, y, 5), Cap);
        }

        // And a level sheet across a panel boundary, so the oracle walks a merged quad back to
        // its cells rather than only one-cell tops — a random scatter merges almost nothing.
        for (int x = 20; x < 44; x++)
        {
            for (int z = 20; z < 44; z++)
            {
                grid.Fill(new VoxelPos(x, 60, z), 8_000);
            }
        }

        // Every surface cell's height, per column; a top over a cell must sit within the
        // tolerance of one of them (a run draws at its first cell's height), and each is used once.
        var expected = new Dictionary<(int X, int Z), List<float>>();
        int surfaceCells = 0;
        foreach ((VoxelPos cell, int ml) in grid.SurfaceCells())
        {
            surfaceCells++;
            if (!expected.TryGetValue((cell.X, cell.Z), out List<float>? heights))
            {
                expected[(cell.X, cell.Z)] = heights = [];
            }

            heights.Add((cell.Y + (ml / (float)Cap)) * Size);
        }

        FluidSurfaceMesh mesh = new FluidSurfaceMesher().Build(grid, Open);
        Assert.Equal(surfaceCells, mesh.SurfaceCells);

        const float tolerance = (FluidSurfaceMesher.ToleranceMillilitres / (float)Cap * Size) + 1e-5f;
        int covered = 0;
        foreach (Quad q in Quads(mesh))
        {
            if (q.Normal != (0f, 1f, 0f))
            {
                continue;
            }

            (int x0, int x1, int z0, int z1) = q.CellRectangle();
            Assert.True(x1 > x0 && z1 > z0, "a top has no area");
            float h = q.P0.Y;
            for (int x = x0; x < x1; x++)
            {
                for (int z = z0; z < z1; z++)
                {
                    Assert.True(expected.TryGetValue((x, z), out List<float>? heights), $"({x}, {z}) is drawn and has no surface cell");
                    int at = heights.FindIndex(height => MathF.Abs(height - h) <= tolerance);
                    Assert.True(at >= 0, $"({x}, {z}) at {h} m is drawn and no surface cell there is within the tolerance");
                    heights.RemoveAt(at);
                    covered++;
                }
            }
        }

        Assert.Equal(surfaceCells, covered);
        Assert.All(expected.Values, heights => Assert.Empty(heights));
        Assert.True(mesh.Tops < mesh.SurfaceCells, $"nothing merged: {mesh.Tops} tops over {mesh.SurfaceCells} cells");
    }

    [Fact]
    public void EveryQuadIsWoundWithItsNormal()
    {
        // M5a's shape of test: the winding is what a cull mode reads, and M8d-2 culls back faces.
        // The corners have (p1 − p0) × (p3 − p0) against the normal and the indices walk them
        // forward, which is Godot's clockwise front face seen from the normal's side — the terrain
        // mesher's convention mirrored twice — for every kind of face.
        var grid = new FluidGrid();
        grid.Fill(new VoxelPos(10, 10, 10), Cap);
        grid.Fill(new VoxelPos(11, 10, 10), Cap / 2);
        grid.Fill(new VoxelPos(10, 12, 10), Cap / 3);
        var terrain = new PredicateTerrain(v => v.Y >= 10);

        FluidSurfaceMesh mesh = new FluidSurfaceMesher().Build(grid, terrain);
        int seen = 0;
        foreach (Quad q in Quads(mesh))
        {
            seen++;
            (float ax, float ay, float az) = (q.P1.X - q.P0.X, q.P1.Y - q.P0.Y, q.P1.Z - q.P0.Z);
            (float bx, float by, float bz) = (q.P3.X - q.P0.X, q.P3.Y - q.P0.Y, q.P3.Z - q.P0.Z);
            (float cx, float cy, float cz) = ((ay * bz) - (az * by), (az * bx) - (ax * bz), (ax * by) - (ay * bx));
            float along = (cx * q.Normal.X) + (cy * q.Normal.Y) + (cz * q.Normal.Z);
            Assert.True(along < 0, $"a quad with normal {q.Normal} is wound the other way ({along})");
        }

        Assert.True(seen >= 8, $"only {seen} quads: the fixture reached too few kinds of face");
        Assert.True(mesh.Bottoms > 0, "no bottom in the fixture: the cell at y = 12 has air under it");
    }

    // ----- merging -----

    [Fact]
    public void ARunTakesCellsWithinTwoMillimetresAndDrawsNoSliverBetweenThem()
    {
        // The settle leaves a sheet level to a millilitre; the automaton's own rest to four. A key
        // on the raw amount would split such a sheet into contours and draw a side between every
        // pair. A run takes any cell within the tolerance of its first, draws at the first's
        // height, and a side needs more than the tolerance of step.
        var grid = new FluidGrid();
        var random = new Random(3);
        for (int x = 0; x < 32; x++)
        {
            for (int z = 0; z < 32; z++)
            {
                grid.Fill(new VoxelPos(x, 8, z), 8_000 + random.Next(0, 5));
            }
        }

        FluidSurfaceMesh sheet = new FluidSurfaceMesher().Build(grid, new PredicateTerrain(v => v.Y >= 8));
        Assert.Equal(1, sheet.Tops);
        Assert.Equal(4, sheet.Sides);
        float drawn = Quads(sheet).Single(q => q.Normal == (0f, 1f, 0f)).P0.Y;
        Assert.Equal((8 + (grid.AmountAt(new VoxelPos(0, 8, 0)) / (float)Cap)) * Size, drawn);

        // A real step — a column raised by twice the tolerance — splits the sheet into three runs
        // and draws a side on each face of the raised column; its ±Z walls break the sheet's own
        // perimeter runs too (3 + 3 + 1 + 1 round the edge, plus the two faces between).
        for (int z = 0; z < 32; z++)
        {
            grid.Fill(new VoxelPos(16, 8, z), 2 * FluidSurfaceMesher.ToleranceMillilitres);
        }

        FluidSurfaceMesh stepped = new FluidSurfaceMesher().Build(grid, new PredicateTerrain(v => v.Y >= 8));
        Assert.Equal(3, stepped.Tops);
        Assert.Equal(10, stepped.Sides);

        // Two runs that could not merge — the depth under them differs — drawn 50 mL apart still
        // get the sliver between them: a crack that thin is a pixel wide at a grazing angle, and
        // the relay's ball was striped with the ground through such cracks before the side rule
        // compared drawn heights exactly.
        grid = new FluidGrid();
        grid.Fill(new VoxelPos(0, 9, 0), 8_000);
        grid.Fill(new VoxelPos(1, 9, 0), 8_050);
        grid.Fill(new VoxelPos(1, 8, 0), Cap);
        FluidSurfaceMesh unmergeable = new FluidSurfaceMesher().Build(grid, new PredicateTerrain(v => v.Y >= 8 && !(v.X == 0 && v.Y == 8)));
        Assert.Equal(2, unmergeable.Tops);
        Quad sliver = Quads(unmergeable).Single(q => q.Normal == (-1f, 0f, 0f) && q.P0.X == 1 * Size && q.MinY() > 9 * Size);
        Assert.Equal((9 + (8_000 / (float)Cap)) * Size, sliver.MinY());
        Assert.Equal((9 + (8_050 / (float)Cap)) * Size, sliver.MaxY());

        // A step of exactly the tolerance is not a step.
        grid = new FluidGrid();
        grid.Fill(new VoxelPos(0, 8, 0), 8_000);
        grid.Fill(new VoxelPos(1, 8, 0), 8_000 + FluidSurfaceMesher.ToleranceMillilitres);
        Assert.Equal(1, new FluidSurfaceMesher().Build(grid, new PredicateTerrain(v => v.Y >= 8)).Tops);
        grid.Fill(new VoxelPos(1, 8, 0), 1);
        FluidSurfaceMesh over = new FluidSurfaceMesher().Build(grid, new PredicateTerrain(v => v.Y >= 8));
        Assert.Equal(2, over.Tops);
        Assert.Equal(7, over.Sides);
    }

    [Fact]
    public void ALevelSheetIsOneTopPerPanel()
    {
        var grid = new FluidGrid();
        for (int x = -32; x < 32; x++)
        {
            for (int z = 0; z < 32; z++)
            {
                grid.Fill(new VoxelPos(x, 8, z), 6_000);
            }
        }

        FluidSurfaceMesh mesh = new FluidSurfaceMesher().Build(grid, new PredicateTerrain(v => v.Y >= 8));
        Assert.Equal(2_048, mesh.SurfaceCells);
        Assert.Equal(2, mesh.Tops);

        // Its perimeter against air: four walls, each one run per panel side it crosses. The
        // sheet is 64 × 32 across two panels: ±Z walls run along X inside a panel (2 runs each),
        // ±X walls run along Z (1 each).
        Assert.Equal(6, mesh.Sides);
        Assert.Equal(0, mesh.Bottoms);
    }

    [Fact]
    public void TheMergeKeyIsFillDepthAndPreviousHeight()
    {
        // Three things a vertex carries, each breaking a run on its own. Fill: alternating fills
        // along X merge nowhere. Depth: an equal fill over a floor that steps merges only where
        // the column under it is as deep. Previous height: two cells at the same fill now, one of
        // which stood lower last build, merge only once their history agrees.
        var grid = new FluidGrid();
        for (int x = 0; x < 8; x++)
        {
            grid.Fill(new VoxelPos(x, 4, 0), x % 2 == 0 ? 5_000 : 6_000);
        }

        Assert.Equal(8, new FluidSurfaceMesher().Build(grid, Open).Tops);

        grid = new FluidGrid();
        for (int x = 0; x < 8; x++)
        {
            grid.Fill(new VoxelPos(x, 5, 0), 5_000);
            if (x >= 4)
            {
                grid.Fill(new VoxelPos(x, 4, 0), Cap);
            }
        }

        FluidSurfaceMesh stepped = new FluidSurfaceMesher().Build(grid, Open);
        Assert.Equal(2, stepped.Tops);
        var depths = new HashSet<float>();
        foreach (Quad q in Quads(stepped))
        {
            if (q.Normal == (0f, 1f, 0f))
            {
                depths.Add(q.D0);
            }
        }

        Assert.Equal(new HashSet<float> { (0 + (5_000 / (float)Cap)) * Size, (1 + (5_000 / (float)Cap)) * Size }, depths);

        grid = new FluidGrid();
        var mesher = new FluidSurfaceMesher();
        grid.Fill(new VoxelPos(0, 4, 0), 3_000);
        grid.Fill(new VoxelPos(1, 4, 0), 7_000);
        mesher.Build(grid, Open);
        grid.Fill(new VoxelPos(0, 4, 0), 4_000);
        Assert.Equal(2, mesher.Build(grid, Open).Tops);
        Assert.Equal(1, mesher.Build(grid, Open).Tops);
    }

    [Fact]
    public void TheSameGridBuildsTheSameArrays()
    {
        var grid = new FluidGrid();
        var random = new Random(7);
        for (int i = 0; i < 2_000; i++)
        {
            grid.Fill(new VoxelPos(random.Next(0, 48), random.Next(0, 40), random.Next(0, 48)), random.Next(1, Cap));
        }

        FluidSurfaceMesh a = new FluidSurfaceMesher().Build(grid, Open);
        FluidSurfaceMesh b = new FluidSurfaceMesher().Build(grid, Open);
        Assert.Equal(a.Positions, b.Positions);
        Assert.Equal(a.Normals, b.Normals);
        Assert.Equal(a.Attributes, b.Attributes);
        Assert.Equal(a.Indices, b.Indices);
        Assert.Equal(a.VertexCount * 3, a.Positions.Length);
        Assert.Equal(a.VertexCount * 2, a.Attributes.Length);
        Assert.Equal(a.Tops + a.Sides + a.Bottoms, a.QuadCount);
    }

    // ----- sides -----

    [Fact]
    public void ASideIsDrawnOnlyWhereWaterMeetsAirOrALowerFill()
    {
        // One full cell on a floor, open all round: a top and four sides, no bottom.
        var grid = new FluidGrid();
        grid.Fill(new VoxelPos(3, 9, 3), Cap);
        var floor = new PredicateTerrain(v => v.Y >= 9);
        FluidSurfaceMesh alone = new FluidSurfaceMesher().Build(grid, floor);
        Assert.Equal((1, 4, 0), (alone.Tops, alone.Sides, alone.Bottoms));

        // Rock on its −X: three sides. The rock's own face is there; drawing ours would fight it.
        var wall = new PredicateTerrain(v => v.Y >= 9 && !(v.X == 2 && v.Z == 3));
        Assert.Equal(3, new FluidSurfaceMesher().Build(grid, wall).Sides);

        // A neighbour the terrain cannot answer for: no side either.
        grid = new FluidGrid();
        grid.Fill(new VoxelPos(32, 9, 3), Cap);
        var edge = new LoadableTerrain(floor);
        edge.Unload(new VoxelPos(31, 9, 3).Chunk);
        Assert.Equal(3, new FluidSurfaceMesher().Build(grid, edge).Sides);

        // A dry neighbour whose local index was a wet cell in the panel built before: its drawn
        // amount is stale scratch and must read as dry, or the cell beside it loses a face.
        grid = new FluidGrid();
        grid.Fill(new VoxelPos(5, 9, 5), Cap);
        grid.Fill(new VoxelPos(32 + 6, 9, 5), Cap);
        Assert.Equal(8, new FluidSurfaceMesher().Build(grid, floor).Sides);

        // A lower neighbour: one side from the neighbour's height to the cell's, drawn by the
        // fuller cell and not by the other; an equal neighbour, none between them.
        grid = new FluidGrid();
        grid.Fill(new VoxelPos(3, 9, 3), 12_000);
        grid.Fill(new VoxelPos(4, 9, 3), 4_000);
        FluidSurfaceMesh pair = new FluidSurfaceMesher().Build(grid, floor);
        Assert.Equal(2, pair.Tops);
        Assert.Equal(3 + 3 + 1, pair.Sides);
        Quad between = Quads(pair).Single(q => q.Normal == (1f, 0f, 0f) && q.P0.X == 4 * Size);
        Assert.Equal((9 + (4_000 / (float)Cap)) * Size, between.MinY());
        Assert.Equal((9 + (12_000 / (float)Cap)) * Size, between.MaxY());

        // Topped up to equal: no face between them, and their ±Z walls now agree along X, so
        // each is one run of two cells — four sides, not six.
        grid.Fill(new VoxelPos(4, 9, 3), 8_000);
        FluidSurfaceMesh level = new FluidSurfaceMesher().Build(grid, floor);
        Assert.Equal(4, level.Sides);
        Assert.Equal(1, level.Tops);
    }

    [Fact]
    public void SidesAreOverEveryWetCellNotOnlyTheSurface()
    {
        // A column three cells high with air all round: a waterfall's shape, or water paused at
        // the loaded edge. The surface rule would draw one cell's walls; every wet cell has them.
        var grid = new FluidGrid();
        for (int y = 9; y < 12; y++)
        {
            grid.Fill(new VoxelPos(3, y, 3), Cap);
        }

        FluidSurfaceMesh column = new FluidSurfaceMesher().Build(grid, new PredicateTerrain(v => v.Y >= 9));
        Assert.Equal(1, column.Tops);
        Assert.Equal(1, column.SurfaceCells);
        Assert.Equal(12, column.Sides);
        Assert.Equal(0, column.Bottoms);

        // A part-full cell with water above it is drawn full: its side spans the whole cell, so a
        // falling column is a shaft rather than a stack of slabs with slots between. The surface
        // cell above keeps its fill.
        grid = new FluidGrid();
        grid.Fill(new VoxelPos(3, 9, 3), 5_000);
        grid.Fill(new VoxelPos(3, 10, 3), 5_000);
        FluidSurfaceMesh falling = new FluidSurfaceMesher().Build(grid, new PredicateTerrain(v => v.Y >= 9));
        Quad lowerSide = Quads(falling).First(q => q.Normal == (1f, 0f, 0f) && q.MinY() == 9 * Size);
        Assert.Equal(10 * Size, lowerSide.MaxY());
        Quad upperSide = Quads(falling).First(q => q.Normal == (1f, 0f, 0f) && q.MinY() == 10 * Size);
        Assert.Equal((10 + (5_000 / (float)Cap)) * Size, upperSide.MaxY());
        Assert.Equal((10 + (5_000 / (float)Cap)) * Size, Quads(falling).Single(q => q.Normal == (0f, 1f, 0f)).P0.Y);

        // The same wall is one run when the cells along it agree, which a level sheet's edge does.
        grid = new FluidGrid();
        for (int x = 0; x < 6; x++)
        {
            grid.Fill(new VoxelPos(x, 9, 0), Cap);
        }

        Assert.Equal(4, new FluidSurfaceMesher().Build(grid, new PredicateTerrain(v => v.Y >= 9)).Sides);

        // And breaks where the neighbours along it differ, because the face's lower edge is the
        // neighbour's height: a wall over alternating wet and dry neighbours is a run per change.
        grid.Fill(new VoxelPos(0, 9, 1), 4_000);
        grid.Fill(new VoxelPos(2, 9, 1), 4_000);
        grid.Fill(new VoxelPos(4, 9, 1), 4_000);
        FluidSurfaceMesh alternating = new FluidSurfaceMesher().Build(grid, new PredicateTerrain(v => v.Y >= 9));
        Quad[] plusZ = Quads(alternating).Where(q => q.Normal == (0f, 0f, 1f) && q.P0.Z == 1 * Size).ToArray();
        Assert.Equal(6, plusZ.Length);
        Assert.Equal(3, plusZ.Count(q => q.MinY() == (9 + (4_000 / (float)Cap)) * Size));
        Assert.Equal(3, plusZ.Count(q => q.MinY() == 9 * Size));
    }

    [Fact]
    public void ABottomIsDrawnUnderACellWithAirBeneathIt()
    {
        var grid = new FluidGrid();
        grid.Fill(new VoxelPos(3, 12, 3), 5_000);
        FluidSurfaceMesh falling = new FluidSurfaceMesher().Build(grid, Open);
        Assert.Equal(1, falling.Bottoms);
        Quad bottom = Quads(falling).Single(q => q.Normal == (0f, -1f, 0f));
        Assert.Equal(12 * Size, bottom.P0.Y);

        // On rock, none; on water, none — the cell below is wet and the pair is one body, whose
        // own underside is the rock under it.
        Assert.Equal(0, new FluidSurfaceMesher().Build(grid, new PredicateTerrain(v => v.Y >= 12)).Bottoms);
        grid.Fill(new VoxelPos(3, 11, 3), Cap);
        Assert.Equal(0, new FluidSurfaceMesher().Build(grid, new PredicateTerrain(v => v.Y >= 11)).Bottoms);
        Assert.Equal(1, new FluidSurfaceMesher().Build(grid, Open).Bottoms);
    }

    // ----- attributes -----

    [Fact]
    public void DepthIsTheContiguousWetColumnUnderTheVertexAcrossPanels()
    {
        // Five deep straddling y = 32, so the count has to carry from the panel below; and a
        // second body in the same column above an air gap, whose depth starts again. The upper
        // panel is filled first, so the carry depends on the mesher's own ordering of panels and
        // not on the dictionary's.
        var grid = new FluidGrid();
        for (int y = 34; y >= 30; y--)
        {
            grid.Fill(new VoxelPos(7, y, 7), y == 34 ? 2_000 : Cap);
        }

        grid.Fill(new VoxelPos(7, 40, 7), 9_000);

        FluidSurfaceMesh mesh = new FluidSurfaceMesher().Build(grid, Open);
        Quad lower = Quads(mesh).Single(q => q.Normal == (0f, 1f, 0f) && q.P0.Y < 36 * Size);
        Quad upper = Quads(mesh).Single(q => q.Normal == (0f, 1f, 0f) && q.P0.Y > 36 * Size);
        Assert.Equal((4 + (2_000 / (float)Cap)) * Size, lower.D0);
        Assert.Equal((0 + (9_000 / (float)Cap)) * Size, upper.D0);

        // The carry is per column of panels: a wet top layer in the panel built just before is
        // not depth under the bottom layer of the panel built next when that panel is a column
        // over. Panel (0, 1, 0) precedes (1, 1, 0) in the mesher's order.
        grid.Fill(new VoxelPos(9, 63, 9), Cap);
        grid.Fill(new VoxelPos(32 + 9, 32, 9), 6_000);
        Quad across = Top(new FluidSurfaceMesher().Build(grid, Open), 32 + 9, 9);
        Assert.Equal((0 + (6_000 / (float)Cap)) * Size, across.D0);

        // A side's bottom edge is as deep as the neighbour's height in that column; its top edge
        // as deep as the cell's. Here every neighbour is air, so the bottom edge reads the wet
        // cells below and the top edge those plus the cell.
        Quad side = Quads(mesh).First(q => q.Normal == (1f, 0f, 0f) && q.MinY() == 32 * Size);
        Assert.Equal(2 * Size, side.DepthAt(side.MinY()));
        Assert.Equal(3 * Size, side.DepthAt(side.MaxY()));
    }

    [Fact]
    public void ThePreviousHeightIsWhereTheColumnsSurfaceStoodLastBuild()
    {
        var grid = new FluidGrid();
        var mesher = new FluidSurfaceMesher();
        var cell = new VoxelPos(2, 6, 2);

        // First build: no previous, so the floor.
        grid.Fill(cell, 4_000);
        Quad first = Top(mesher.Build(grid, Open), cell.X, cell.Z);
        Assert.Equal(6 * Size, first.Prev0);

        // Rising within the cell: the previous is last build's height.
        grid.Fill(cell, 5_000);
        Quad risen = Top(mesher.Build(grid, Open), cell.X, cell.Z);
        Assert.Equal((6 + (4_000 / (float)Cap)) * Size, risen.Prev0);
        Assert.Equal((6 + (9_000 / (float)Cap)) * Size, risen.P0.Y);

        // Rising out of the cell into the one above: the new surface cell's previous is the old
        // surface, one cell down.
        grid.Fill(cell, Cap);
        grid.Fill(cell.Offset(0, 1, 0), 1_000);
        Quad above = Top(mesher.Build(grid, Open), cell.X, cell.Z);
        Assert.Equal((7 + (1_000 / (float)Cap)) * Size, above.P0.Y);
        Assert.Equal((6 + (9_000 / (float)Cap)) * Size, above.Prev0, 5);

        // Falling back: the surface cell's previous is the old surface, one cell up.
        grid.Drain(cell.Offset(0, 1, 0), 1_000);
        grid.Drain(cell, 3_000);
        Quad fallen = Top(mesher.Build(grid, Open), cell.X, cell.Z);
        Assert.Equal((7 + (1_000 / (float)Cap)) * Size, fallen.Prev0, 5);

        // A surface that fell ten cells in one tick (M8d-3) comes down from where it was, not up
        // from its floor: the nearest previous surface above within the fall's reach.
        grid.Fill(new VoxelPos(4, 20, 4), 6_000);
        mesher.Build(grid, Open);
        grid.Drain(new VoxelPos(4, 20, 4), 6_000);
        grid.Fill(new VoxelPos(4, 10, 4), 6_000);
        Assert.Equal((20 + (6_000 / (float)Cap)) * Size, Top(mesher.Build(grid, Open), 4, 4).Prev0, 5);

        // And across a panel's top: from y = 40 in the panel above to y = 30 in this one.
        grid.Fill(new VoxelPos(5, 40, 5), 6_000);
        mesher.Build(grid, Open);
        grid.Drain(new VoxelPos(5, 40, 5), 6_000);
        grid.Fill(new VoxelPos(5, 30, 5), 6_000);
        Assert.Equal((40 + (6_000 / (float)Cap)) * Size, Top(mesher.Build(grid, Open), 5, 5).Prev0, 5);

        // The reach's last cell inside the panel — a previous surface on the top layer, six
        // above — is inside the search, not one past it.
        grid.Fill(new VoxelPos(6, 31, 6), 6_000);
        mesher.Build(grid, Open);
        grid.Drain(new VoxelPos(6, 31, 6), 6_000);
        grid.Fill(new VoxelPos(6, 25, 6), 6_000);
        Assert.Equal((31 + (6_000 / (float)Cap)) * Size, Top(mesher.Build(grid, Open), 6, 6).Prev0, 5);

        // A side's lower edge asks the same question of a dry neighbour, through the search the
        // perimeter uses rather than the tops' cursor: its previous surface in the panel above.
        grid.Fill(new VoxelPos(8, 40, 7), 6_000);
        mesher.Build(grid, Open);
        grid.Drain(new VoxelPos(8, 40, 7), 6_000);
        grid.Fill(new VoxelPos(7, 30, 7), 6_000);
        FluidSurfaceMesh sided = mesher.Build(grid, Open);
        Quad plusX = Quads(sided).Single(q => q.Normal == (1f, 0f, 0f) && q.P0.X == 8 * Size && q.MinY() == 30 * Size);
        Assert.Equal((40 + (6_000 / (float)Cap)) * Size, plusX.Prev0, 5);

        // The searches' far bounds are inclusive: the top layer of the panel above, 33 cells up,
        // for a top (the cursor's hand-over to the panel above), and the top layer of the same
        // panel for a side's dry neighbour (the perimeter's search).
        grid.Fill(new VoxelPos(9, 63, 9), 6_000);
        mesher.Build(grid, Open);
        grid.Drain(new VoxelPos(9, 63, 9), 6_000);
        grid.Fill(new VoxelPos(9, 30, 9), 6_000);
        Assert.Equal((63 + (6_000 / (float)Cap)) * Size, Top(mesher.Build(grid, Open), 9, 9).Prev0, 5);

        grid.Fill(new VoxelPos(11, 31, 10), 6_000);
        mesher.Build(grid, Open);
        grid.Drain(new VoxelPos(11, 31, 10), 6_000);
        grid.Fill(new VoxelPos(10, 25, 10), 6_000);
        FluidSurfaceMesh edged = mesher.Build(grid, Open);
        Quad edge = Quads(edged).Single(q => q.Normal == (1f, 0f, 0f) && q.P0.X == 11 * Size && q.P0.Z == 10 * Size && q.MinY() == 25 * Size);
        Assert.Equal((31 + (6_000 / (float)Cap)) * Size, edge.Prev0, 5);

        // A cell that has just got wet, elsewhere, rises from its floor whatever else happened.
        grid.Fill(new VoxelPos(12, 6, 12), 2_000);
        Assert.Equal(6 * Size, Top(mesher.Build(grid, Open), 12, 12).Prev0);

        // The previous the next build reads is what was drawn, not what was held: a cell that was
        // second in a run stood at the run's height, and lerps from there when it is alone.
        grid = new FluidGrid();
        mesher = new FluidSurfaceMesher();
        grid.Fill(new VoxelPos(0, 6, 0), 3_000);
        grid.Fill(new VoxelPos(1, 6, 0), 3_100);
        Assert.Equal(1, mesher.Build(grid, Open).Tops);
        grid.Fill(new VoxelPos(0, 6, 0), 6_000);
        Assert.Equal((6 + (3_000 / (float)Cap)) * Size, Top(mesher.Build(grid, Open), 1, 0).Prev0);

        // Every corner of a merged top carries the same previous height, because it is in the key.
        grid = new FluidGrid();
        mesher = new FluidSurfaceMesher();
        for (int x = 0; x < 4; x++) grid.Fill(new VoxelPos(x, 6, 0), 3_000);
        mesher.Build(grid, Open);
        for (int x = 0; x < 4; x++) grid.Fill(new VoxelPos(x, 6, 0), 1_000);
        FluidSurfaceMesh sheet = mesher.Build(grid, Open);
        Assert.Equal(1, sheet.Tops);
        Quad merged = Quads(sheet).Single(q => q.Normal == (0f, 1f, 0f));
        Assert.All(new[] { merged.Prev0, merged.Prev1, merged.Prev2, merged.Prev3 }, p => Assert.Equal((6 + (3_000 / (float)Cap)) * Size, p));
    }

    [Fact]
    public void ThePreviousHeightMatchesADictionaryOfThePreviousSurfaceEverywhere()
    {
        // The store is per panel in index order with a cursor for the tops and a search for the
        // sides; the definition is three lookups in a dictionary of the previous build's surface.
        // Random water, random changes — including surfaces that cross a panel's top and bottom
        // layer, which is where the cursor hands over to the search — and every top's previous
        // height against the dictionary's answer.
        var random = new Random(2026_09_11);
        var grid = new FluidGrid();
        var mesher = new FluidSurfaceMesher();
        for (int i = 0; i < 3_000; i++)
        {
            grid.Fill(new VoxelPos(random.Next(-20, 20), random.Next(20, 44), random.Next(-20, 20)), random.Next(1, Cap));
        }

        for (int x = -20; x < 20; x++)
        {
            for (int z = -20; z < 20; z++)
            {
                grid.Fill(new VoxelPos(x, 31, z), Cap);
                grid.Fill(new VoxelPos(x, 32, z), random.Next(0, 3) == 0 ? 0 : random.Next(1, Cap));
            }
        }

        // A top over a run is drawn at the run's first cell, within the tolerance of every cell in
        // it; its previous is the run's first cell's previous, itself a drawn height, so the
        // previous can sit two tolerances from the dictionary's raw answer.
        const float tolerance = (2 * FluidSurfaceMesher.ToleranceMillilitres / (float)Cap * Size) + 1e-5f;
        int crossings = 0;
        for (int round = 0; round < 4; round++)
        {
            var previous = new Dictionary<VoxelPos, int>();
            foreach ((VoxelPos cell, int ml) in grid.SurfaceCells())
            {
                previous[cell] = ml;
            }

            mesher.Build(grid, Open);

            for (int i = 0; i < 1_500; i++)
            {
                var cell = new VoxelPos(random.Next(-20, 20), random.Next(20, 44), random.Next(-20, 20));
                if (random.Next(2) == 0) grid.Fill(cell, random.Next(1, Cap));
                else grid.Drain(cell, random.Next(1, Cap));
            }

            // Surfaces crossing y = 32 both ways, and columns emptied from the top down by up
            // to the fall's reach, so a surface lands many cells under where it was — across the
            // panel boundary for some — which is the search the fall rule made necessary.
            for (int x = -20; x < 20; x++)
            {
                var top = new VoxelPos(x, 32, x);
                if (grid.AmountAt(top) > 0) grid.Drain(top, Cap);
                else grid.Fill(top, random.Next(1, Cap));

                int drop = random.Next(2, FluidSurfaceMesher.FallSearchCells);
                int fromY = random.Next(24, 44);
                for (int dy = 0; dy < drop; dy++)
                {
                    grid.Drain(new VoxelPos(-x, fromY - dy, x), Cap);
                }
            }

            FluidSurfaceMesh mesh = mesher.Build(grid, Open);
            foreach (Quad q in Quads(mesh))
            {
                if (q.Normal != (0f, 1f, 0f))
                {
                    continue;
                }

                (int x0, int x1, int z0, int z1) = q.CellRectangle();
                // A full cell's top sits on the next cell's floor, so the cell is whichever of
                // the two candidates holds water at exactly this height.
                int guess = (int)MathF.Floor(q.P0.Y / Size);
                for (int x = x0; x < x1; x++)
                {
                    for (int z = z0; z < z1; z++)
                    {
                        int y = guess;
                        int held = grid.AmountAt(new VoxelPos(x, y, z));
                        if (held == 0 || MathF.Abs(((y + (held / (float)Cap)) * Size) - q.P0.Y) > tolerance) y--;
                        var cell = new VoxelPos(x, y, z);
                        held = grid.AmountAt(cell);
                        Assert.True(held > 0 && MathF.Abs(((y + (held / (float)Cap)) * Size) - q.P0.Y) <= tolerance, $"{cell} is not the surface under a top at {q.P0.Y}");
                        float expected;
                        if (previous.TryGetValue(cell, out int same)) expected = (y + (same / (float)Cap)) * Size;
                        else if (NearestAbove(previous, cell) is (int dy, int above)) { expected = (y + (((dy * Cap) + above) / (float)Cap)) * Size; if (y + dy >= 32 && y < 32) crossings++; }
                        else if (previous.TryGetValue(cell.Offset(0, -1, 0), out int below)) { expected = (y + ((below - Cap) / (float)Cap)) * Size; if (y == 32) crossings++; }
                        else expected = y * Size;
                        Assert.True(MathF.Abs(expected - q.Prev0) <= tolerance, $"{cell}: previous height {q.Prev0} against {expected}");
                    }
                }
            }
        }

        Assert.True(crossings > 20, $"only {crossings} surfaces crossed a panel boundary: the search path was barely reached");
    }

    [Fact]
    public void TheMesherAsksTheTerrainOnlyAboutDryNeighboursAndNeverCountsARefusal()
    {
        // A level sheet in the middle of a panel, floor under it: the terrain is asked about the
        // perimeter's dry neighbours and nothing inside the sheet. And on a captured view the
        // questions go IsLoaded first, so a neighbour outside the view is not a refusal — the
        // refusal count is decision 7's instrument and the mesher must not move it.
        var grid = new FluidGrid();
        for (int x = 4; x < 12; x++)
        {
            for (int z = 4; z < 12; z++)
            {
                grid.Fill(new VoxelPos(x, 9, z), 7_000);
            }
        }

        FluidSurfaceMesh mesh = new FluidSurfaceMesher().Build(grid, new PredicateTerrain(v => v.Y >= 9));
        Assert.Equal(32 + 64, mesh.TerrainAsks);

        var field = new FluidField(new PredicateTerrain(v => v.Y >= 9));
        field.Sync([ChunkPos.Zero]);
        FluidFieldView view = field.Capture();
        grid.Fill(new VoxelPos(31, 9, 8), Cap);
        new FluidSurfaceMesher().Build(grid, view);
        Assert.Equal(0L, view.Refusals);
    }

    // ----- helpers -----

    /// <summary>The definition the store's cursor and searches are pinned to: the nearest previous surface above within the fall's reach.</summary>
    private static (int Dy, int Amount)? NearestAbove(Dictionary<VoxelPos, int> previous, VoxelPos cell)
    {
        for (int dy = 1; dy <= FluidSurfaceMesher.FallSearchCells; dy++)
        {
            if (previous.TryGetValue(cell.Offset(0, dy, 0), out int amount))
            {
                return (dy, amount);
            }
        }

        return null;
    }

    private static Quad Top(FluidSurfaceMesh mesh, int x, int z) =>
        Quads(mesh).Single(q =>
        {
            if (q.Normal != (0f, 1f, 0f)) return false;
            (int x0, int x1, int z0, int z1) = q.CellRectangle();
            return x >= x0 && x < x1 && z >= z0 && z < z1;
        });

    private readonly record struct Vertex(float X, float Y, float Z);

    private sealed record Quad(
        Vertex P0, Vertex P1, Vertex P2, Vertex P3,
        (float X, float Y, float Z) Normal,
        float Prev0, float D0, float Prev1, float D1, float Prev2, float D2, float Prev3, float D3)
    {
        public (int X0, int X1, int Z0, int Z1) CellRectangle()
        {
            float minX = Math.Min(Math.Min(P0.X, P1.X), Math.Min(P2.X, P3.X));
            float maxX = Math.Max(Math.Max(P0.X, P1.X), Math.Max(P2.X, P3.X));
            float minZ = Math.Min(Math.Min(P0.Z, P1.Z), Math.Min(P2.Z, P3.Z));
            float maxZ = Math.Max(Math.Max(P0.Z, P1.Z), Math.Max(P2.Z, P3.Z));
            return ((int)MathF.Round(minX / Size), (int)MathF.Round(maxX / Size), (int)MathF.Round(minZ / Size), (int)MathF.Round(maxZ / Size));
        }

        public float MinY() => Math.Min(Math.Min(P0.Y, P1.Y), Math.Min(P2.Y, P3.Y));

        public float MaxY() => Math.Max(Math.Max(P0.Y, P1.Y), Math.Max(P2.Y, P3.Y));

        public float DepthAt(float y)
        {
            if (P0.Y == y) return D0;
            if (P1.Y == y) return D1;
            if (P2.Y == y) return D2;
            return D3;
        }
    }

    private static IEnumerable<Quad> Quads(FluidSurfaceMesh mesh)
    {
        Assert.Equal(0, mesh.Indices.Length % 6);
        for (int q = 0; q < mesh.QuadCount; q++)
        {
            int first = mesh.Indices[q * 6];
            Assert.Equal(new[] { first, first + 1, first + 2, first, first + 2, first + 3 }, mesh.Indices[(q * 6)..((q * 6) + 6)]);
            Vertex V(int i) => new(mesh.Positions[i * 3], mesh.Positions[(i * 3) + 1], mesh.Positions[(i * 3) + 2]);
            (float, float, float) Nm(int i) => (mesh.Normals[i * 3], mesh.Normals[(i * 3) + 1], mesh.Normals[(i * 3) + 2]);
            for (int i = 1; i < 4; i++)
            {
                Assert.Equal(Nm(first), Nm(first + i));
            }

            yield return new Quad(
                V(first), V(first + 1), V(first + 2), V(first + 3), Nm(first),
                mesh.Attributes[first * 2], mesh.Attributes[(first * 2) + 1],
                mesh.Attributes[(first + 1) * 2], mesh.Attributes[((first + 1) * 2) + 1],
                mesh.Attributes[(first + 2) * 2], mesh.Attributes[((first + 2) * 2) + 1],
                mesh.Attributes[(first + 3) * 2], mesh.Attributes[((first + 3) * 2) + 1]);
        }
    }
}
