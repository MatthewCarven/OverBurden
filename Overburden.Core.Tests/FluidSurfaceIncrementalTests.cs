using Overburden.Core.Fluid;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// The surface mesher's incremental rule (2026-09-21): a panel is rebuilt only when something it
/// reads has moved, and the result is the mesh a rebuild of everything would give — quad for
/// quad, attributes included.
/// </summary>
public sealed class FluidSurfaceIncrementalTests
{
    private const int Cap = FluidConstants.CellCapacityMillilitres;
    private const int N = VoxelConstants.ChunkSize;

    /// <summary>
    /// A terrain whose lake answer and lake stamp a test can move, so the rule's lake clause can
    /// be reached without a <see cref="LakeCeiling"/>: the stamp is the ceiling's contract, and
    /// the clause reads nothing else.
    /// </summary>
    private sealed class StampedTerrain(Func<VoxelPos, bool> canHold) : IFluidTerrain
    {
        public Dictionary<(int X, int Z), long> Stamps { get; } = [];

        public Func<VoxelPos, bool> InLakeAt { get; set; } = _ => false;

        public Func<VoxelPos, bool> CanHoldAt { get; set; } = canHold;

        public bool CanHold(VoxelPos voxel) => CanHoldAt(voxel) && !InLakeAt(voxel);

        public bool InLake(VoxelPos voxel) => CanHoldAt(voxel) && InLakeAt(voxel);

        public long LakeStamp(ChunkPos chunk) => Stamps.TryGetValue((chunk.X, chunk.Z), out long stamp) ? stamp : 0;
    }

    // ----- the oracle -----

    [Fact]
    public void AnIncrementalUpdateEqualsARebuildOfEverythingAcrossAFlood()
    {
        // Two meshers over one simulation: one rebuilds every panel every tick (the pre-2026-09-21
        // arm), the other only what the rule says moved. Two pits. In the first a relay-sized
        // pour falls into a walled 32 x 32 pit whose floor is in one panel layer and whose water
        // climbs into the next, so the depth carry crosses a panel floor; it spans four chunk
        // columns, so sides cross panel faces; and the automaton keeps every panel of it moving
        // for the whole run. The second holds a level sheet over four panels and a full shaft up
        // through three, put down asleep, which nothing moves until the run does: rock appears
        // over the sheet, a lake band is rebuilt over one column and dropped again, a cell at the
        // bottom of the shaft is drained, a panel is drained empty and a panel is evicted. After
        // every tick the two meshers' whole surfaces must be the same arrays, and the second pit
        // is where the incremental arm has something to skip.
        static bool Pit(VoxelPos v) => v.Y >= 8 && v.X >= -16 && v.X < 16 && v.Z >= -16 && v.Z < 16;
        static bool Rest(VoxelPos v) => v.Y >= 8 && v.X >= 64 && v.X < 128 && v.Z >= 64 && v.Z < 128;
        var terrain = new StampedTerrain(v => Pit(v) || Rest(v));
        var sim = new FluidSimulation(terrain);
        for (int x = 64; x < 128; x++)
        {
            for (int z = 64; z < 128; z++)
            {
                sim.Grid.Fill(new VoxelPos(x, 8, z), 7_000);
            }
        }

        for (int y = 9; y < 90; y++)
        {
            sim.Grid.Fill(new VoxelPos(70, y, 70), Cap);
            sim.Grid.Fill(new VoxelPos(71, y, 70), Cap);
        }

        var full = new FluidSurfaceMesher { ForceFull = true };
        var incremental = new FluidSurfaceMesher();

        long pending = 900_000L * FluidConstants.MillilitresPerLitre;
        var at = new VoxelPos(0, 48, 0);
        const int radius = 7;
        int skipped = 0, ticksWithASkip = 0, removedSeen = 0, lakeRebuilds = 0, columnRebuilds = 0, touched = 0;

        for (int tick = 0; tick < 220; tick++)
        {
            if (pending > 0)
            {
                pending = Pour(sim, at, radius, pending);
            }

            sim.Step();

            switch (tick)
            {
                case 60:
                    // Rock appears over the resting sheet: the cell under it is woken, holds
                    // water, and has nowhere to go — nothing is written, and the disturb's touch
                    // is the only thing that moves its panel.
                    terrain.CanHoldAt = v => (Pit(v) || Rest(v)) && !(v.X == 100 && v.Y == 9 && v.Z == 100);
                    sim.Disturb(new VoxelPos(100, 9, 100));
                    break;
                case 100:
                    // A lake band over chunk column (3, 3): cells above the sheet there are the
                    // lake's, so the sheet under them draws no top — and the column's stamp moves.
                    terrain.InLakeAt = v => v.X >= 96 && v.X < 128 && v.Z >= 96 && v.Z < 128 && v.Y > 8 && v.Y < 40;
                    terrain.Stamps[(3, 3)] = 1;
                    break;
                case 130:
                    // A cell at the bottom of the shaft: the panels above it read a shorter carry.
                    sim.Grid.Drain(new VoxelPos(70, 9, 70), 2_000);
                    break;
                case 140:
                    // The band goes again.
                    terrain.InLakeAt = _ => false;
                    terrain.Stamps[(3, 3)] = 2;
                    break;
                case 170:
                    // A panel drained empty by hand, and one evicted: both leave the held set.
                    foreach (VoxelPos cell in sim.Grid.HeldCells().Where(c => c.Chunk == new ChunkPos(3, 0, 2)).ToList())
                    {
                        sim.Grid.Drain(cell, Cap);
                    }

                    sim.Grid.Evict(new ChunkPos(2, 0, 3));
                    break;
            }

            FluidSurfaceMesh a = full.Build(sim.Grid, terrain);
            FluidSurfaceMesh b = incremental.Build(sim.Grid, terrain);
            Assert.Equal(a.QuadCount, b.QuadCount);
            Assert.Equal(a.Positions, b.Positions);
            Assert.Equal(a.Normals, b.Normals);
            Assert.Equal(a.Attributes, b.Attributes);
            Assert.Equal(a.Indices, b.Indices);
            Assert.Equal(a.Tops, b.Tops);
            Assert.Equal(a.Sides, b.Sides);
            Assert.Equal(a.Bottoms, b.Bottoms);
            Assert.Equal(a.SurfaceCells, b.SurfaceCells);
            Assert.Equal(a.UnderLake, b.UnderLake);

            FluidSurfaceUpdate update = incremental.LastUpdate!;
            Assert.Equal(update.PanelsHeld, full.LastUpdate!.PanelsRebuilt);
            Assert.Equal(update.QuadCount, b.QuadCount);
            if (update.PanelsRebuilt < update.PanelsHeld)
            {
                ticksWithASkip++;
                skipped += update.PanelsHeld - update.PanelsRebuilt;
            }

            removedSeen += update.Removed.Count;
            lakeRebuilds += update.RebuiltForLake;
            columnRebuilds += update.RebuiltForColumn;
            if (tick == 60)
            {
                touched = update.RebuiltForOwnChange;
            }

            if (tick == 130)
            {
                // The panel over the drained one is a face neighbour; the one above that is the
                // column's.
                Assert.Equal(1, update.RebuiltForColumn);
                Assert.Contains(update.Rebuilt, m => m.Chunk == new ChunkPos(2, 2, 2));
            }
        }

        // The arms have to have differed in what they did, or the equality above is an identity.
        Assert.True(ticksWithASkip > 150, $"the incremental arm skipped a panel on only {ticksWithASkip} ticks");
        Assert.True(skipped > 600, $"only {skipped} panel builds were skipped over the run");
        Assert.Equal(2, removedSeen);
        Assert.Equal(2, lakeRebuilds);
        Assert.True(columnRebuilds >= 1, "no panel was rebuilt for its column, so the depth carry across a panel floor was not reached");
        Assert.True(touched >= 1, "the disturb over the resting sheet touched no panel");
        Assert.True(sim.Grid.PanelCount > 11, $"the pour filled only {sim.Grid.PanelCount - 7} panels, so its water never climbed into a second layer");
    }

    // ----- the rule, clause by clause -----

    [Fact]
    public void AnUnchangedPanelIsNotRebuilt()
    {
        var grid = new FluidGrid();
        FillSheet(grid, -16, 16, 8, 7_000);
        var mesher = new FluidSurfaceMesher();
        IFluidTerrain floor = new PredicateTerrain(v => v.Y >= 8);

        FluidSurfaceUpdate first = mesher.Update(grid, floor);
        Assert.Equal(4, first.PanelsHeld);
        Assert.Equal(4, first.RebuiltNew);

        // Every top rose from its floor on the first build, so each panel is built once more with
        // its previous height equal to its current, and then not at all.
        FluidSurfaceUpdate second = mesher.Update(grid, floor);
        Assert.Equal(4, second.RebuiltForPrevious);
        Assert.Equal(4, second.PanelsRebuilt);

        FluidSurfaceUpdate third = mesher.Update(grid, floor);
        Assert.Equal(0, third.PanelsRebuilt);
        Assert.Empty(third.Rebuilt);
        Assert.Empty(third.Removed);
        Assert.Equal(4, third.PanelsHeld);
        Assert.Equal(first.QuadCount, third.QuadCount);
        Assert.Equal(first.SurfaceBytes, third.SurfaceBytes);
        Assert.Equal(0L, third.RebuiltBytes);
    }

    [Fact]
    public void AWriteReachesThePanelItsFaceNeighboursAndTheColumnAbove()
    {
        // A sheet over a 3 x 3 block of chunk columns and a wet column up through four panels
        // over the centre one. One cell of the centre panel changes: the centre is rebuilt for
        // itself, its four sideways neighbours and the panel above for the face they share, and
        // the two panels further up the column for the depth carry; the corners keep their mesh.
        var grid = new FluidGrid();
        FillSheet(grid, -32, 64, 8, 7_000);
        for (int y = 9; y < 4 * N; y++)
        {
            grid.Fill(new VoxelPos(10, y, 10), Cap);
        }

        var mesher = new FluidSurfaceMesher();
        IFluidTerrain floor = new PredicateTerrain(v => v.Y >= 8);
        Settle(mesher, grid, floor);

        grid.Fill(new VoxelPos(20, 8, 20), 500);
        FluidSurfaceUpdate update = mesher.Update(grid, floor);

        var expected = new HashSet<ChunkPos>
        {
            new(0, 0, 0),
            new(1, 0, 0), new(-1, 0, 0), new(0, 0, 1), new(0, 0, -1), new(0, 1, 0),
            new(0, 2, 0), new(0, 3, 0),
        };
        Assert.Equal(expected, update.Rebuilt.Select(m => m.Chunk!.Value).ToHashSet());
        Assert.Equal(1, update.RebuiltForOwnChange);
        Assert.Equal(5, update.RebuiltForNeighbour);
        Assert.Equal(2, update.RebuiltForColumn);
        Assert.Equal(8, update.PanelsRebuilt);
        Assert.Equal(12, update.PanelsHeld);
    }

    [Fact]
    public void AWriteThatChangesNothingRebuildsNothing()
    {
        var grid = new FluidGrid();
        FillSheet(grid, 0, 16, 8, 7_000);
        var mesher = new FluidSurfaceMesher();
        IFluidTerrain floor = new PredicateTerrain(v => v.Y >= 8);
        Settle(mesher, grid, floor);

        // A transfer of nothing and a fill of nothing write the cell's own value back.
        grid.Transfer(new VoxelPos(3, 8, 3), new VoxelPos(4, 8, 3), 0);
        grid.Fill(new VoxelPos(3, 8, 3), 0);
        Assert.Equal(0, mesher.Update(grid, floor).PanelsRebuilt);
    }

    [Fact]
    public void ATouchWithNoWriteRebuildsThePanelAndItsNeighbours()
    {
        var grid = new FluidGrid();
        FillSheet(grid, 0, 64, 8, 7_000);
        var mesher = new FluidSurfaceMesher();
        IFluidTerrain floor = new PredicateTerrain(v => v.Y >= 8);
        Settle(mesher, grid, floor);

        grid.Touch(new ChunkPos(0, 0, 0));
        FluidSurfaceUpdate update = mesher.Update(grid, floor);
        Assert.Equal(1, update.RebuiltForOwnChange);
        Assert.Equal(2, update.RebuiltForNeighbour);
        Assert.Equal(3, update.PanelsRebuilt);

        // A chunk with no panel has no mesh to move.
        grid.Touch(new ChunkPos(9, 9, 9));
        Assert.Equal(0, mesher.Update(grid, floor).PanelsRebuilt);
    }

    [Fact]
    public void ALakeBandRebuiltOverAColumnRebuildsItsPanelAlone()
    {
        var terrain = new StampedTerrain(v => v.Y >= 8);
        var grid = new FluidGrid();
        FillSheet(grid, 0, 64, 8, 7_000);
        var mesher = new FluidSurfaceMesher();
        Settle(mesher, grid, terrain);

        // The band appears: the cells above the sheet in chunk column (1, 1) are the lake's now,
        // so the sheet there has no top, and only that panel is rebuilt.
        terrain.InLakeAt = v => v.X >= N && v.Z >= N && v.Y > 8;
        terrain.Stamps[(1, 1)] = 1;
        FluidSurfaceUpdate update = mesher.Update(grid, terrain);
        Assert.Equal(1, update.RebuiltForLake);
        Assert.Equal(1, update.PanelsRebuilt);
        Assert.Equal(new ChunkPos(1, 0, 1), update.Rebuilt[0].Chunk);
        Assert.Equal(N * N, update.Rebuilt[0].UnderLake);
        Assert.Equal(0, update.Rebuilt[0].Tops);
        Assert.Equal(3 * N * N, update.SurfaceCells);

        // The panel's previous surface went with its tops, so it is built once more — and only
        // it: its two neighbours draw no side against it (the shared face holds water at the same
        // fill on both sides) and so never read its previous surface, which the build records.
        // An unchanged stamp with an unchanged answer rebuilds nothing after that.
        FluidSurfaceUpdate settle = mesher.Update(grid, terrain);
        Assert.Equal(1, settle.RebuiltForPrevious);
        Assert.Equal(1, settle.PanelsRebuilt);
        Assert.Equal(0, mesher.Update(grid, terrain).PanelsRebuilt);
    }

    [Fact]
    public void APanelThatEmptiesIsRemovedAndItsNeighboursRebuilt()
    {
        var grid = new FluidGrid();
        FillSheet(grid, 0, 64, 8, 7_000);
        var mesher = new FluidSurfaceMesher();
        IFluidTerrain floor = new PredicateTerrain(v => v.Y >= 8);
        Settle(mesher, grid, floor);

        foreach (VoxelPos cell in grid.HeldCells().Where(c => c.Chunk == new ChunkPos(1, 0, 1)).ToList())
        {
            grid.Drain(cell, Cap);
        }

        FluidSurfaceUpdate update = mesher.Update(grid, floor);
        Assert.Equal([new ChunkPos(1, 0, 1)], update.Removed);
        Assert.Equal(3, update.PanelsHeld);
        Assert.Equal(2, update.RebuiltForNeighbour);
        Assert.Equal(2, update.PanelsRebuilt);
        Assert.Equal(3 * N * N, update.SurfaceCells);

        // The neighbours now draw a side each along the face the drained panel had water on. Its
        // previous surface stays through the pass that removes it — their sides' bottom edges
        // lerp from where its water stood — and goes after, which rebuilds them once more.
        Assert.True(update.Rebuilt.All(m => m.Sides >= 1), "a neighbour of the drained panel drew no side against the air it left");
        FluidSurfaceUpdate after = mesher.Update(grid, floor);
        Assert.Equal(2, after.RebuiltForPrevious);
        Assert.Equal(2, after.PanelsRebuilt);
        Assert.Equal(0, mesher.Update(grid, floor).PanelsRebuilt);
    }

    [Fact]
    public void APanelReleasedAndRefilledBetweenBuildsIsRebuilt()
    {
        // The stamp is grid-wide so a panel that goes and comes back cannot come back at the
        // number the mesher recorded for it.
        var grid = new FluidGrid();
        grid.Fill(new VoxelPos(3, 8, 3), 7_000);
        var mesher = new FluidSurfaceMesher();
        IFluidTerrain floor = new PredicateTerrain(v => v.Y >= 8);
        Settle(mesher, grid, floor);

        grid.Drain(new VoxelPos(3, 8, 3), Cap);
        grid.Fill(new VoxelPos(3, 8, 3), 7_000);
        FluidSurfaceUpdate update = mesher.Update(grid, floor);
        Assert.Equal(1, update.RebuiltForOwnChange);
        Assert.Empty(update.Removed);
    }

    [Fact]
    public void BuildAfterUpdateRebuildsEverythingOnce()
    {
        var grid = new FluidGrid();
        FillSheet(grid, 0, 64, 8, 7_000);
        var mesher = new FluidSurfaceMesher();
        IFluidTerrain floor = new PredicateTerrain(v => v.Y >= 8);
        Settle(mesher, grid, floor);

        FluidSurfaceMesh whole = mesher.Build(grid, floor);
        Assert.Equal(4, mesher.LastUpdate!.PanelsRebuilt);
        Assert.Equal(4, whole.Tops);
        Assert.Equal(new FluidSurfaceMesher().Build(grid, floor).Positions, whole.Positions);

        // And incremental from then on, with the whole surface still complete.
        Assert.Equal(0, mesher.Update(grid, floor).PanelsRebuilt);
        grid.Fill(new VoxelPos(3, 8, 3), 500);
        FluidSurfaceMesh again = mesher.Build(grid, floor);
        Assert.Equal(3, mesher.LastUpdate!.PanelsRebuilt);
        Assert.Equal(new FluidSurfaceMesher().Build(grid, floor).QuadCount, again.QuadCount);
    }

    // ----- the wire from the simulation -----

    [Fact]
    public void ADisturbTouchesThePanelsOfTheHeldCellsItWakes()
    {
        // The sheet is put down asleep and level, so nothing moves unless the disturb moves it.
        var sim = new FluidSimulation(new PredicateTerrain(v => v.Y >= 8));
        for (int x = 0; x < 8; x++)
        {
            for (int z = 0; z < 8; z++)
            {
                sim.Grid.Fill(new VoxelPos(x, 8, z), 7_000);
            }
        }

        var mesher = new FluidSurfaceMesher();
        Settle(mesher, sim.Grid, new PredicateTerrain(v => v.Y >= 8));

        // An edit in dry rock far from any water touches nothing.
        sim.Disturb(new VoxelPos(60, 3, 60));
        sim.Step();
        Assert.Equal(0, mesher.Update(sim.Grid, new PredicateTerrain(v => v.Y >= 8)).PanelsRebuilt);

        // An edit over the sheet: the cell under it is woken, holds water, has nowhere to go —
        // and its panel is touched whether or not the visit moved anything.
        sim.Disturb(new VoxelPos(3, 9, 3));
        sim.Step();
        FluidSurfaceUpdate update = mesher.Update(sim.Grid, new PredicateTerrain(v => v.Y >= 8));
        Assert.Equal(1, update.RebuiltForOwnChange);
        Assert.Equal(1, update.PanelsRebuilt);
    }

    [Fact]
    public void AChunkArrivingTouchesItsPanelAndTheNeighboursWithWaterOnItsFaces()
    {
        var sim = new FluidSimulation(new PredicateTerrain(v => v.Y >= 8));
        sim.Grid.Fill(new VoxelPos(31, 8, 5), 7_000);
        sim.Grid.Fill(new VoxelPos(32, 8, 5), 7_000);
        sim.Grid.Fill(new VoxelPos(70, 8, 70), 7_000);

        var mesher = new FluidSurfaceMesher();
        Settle(mesher, sim.Grid, new PredicateTerrain(v => v.Y >= 8));

        sim.ChunkArrived(new ChunkPos(1, 0, 0));
        FluidSurfaceUpdate update = mesher.Update(sim.Grid, new PredicateTerrain(v => v.Y >= 8));
        var rebuilt = update.Rebuilt.Select(m => m.Chunk!.Value).ToHashSet();
        Assert.Contains(new ChunkPos(1, 0, 0), rebuilt);
        Assert.Contains(new ChunkPos(0, 0, 0), rebuilt);
        Assert.DoesNotContain(new ChunkPos(2, 0, 2), rebuilt);
        Assert.Equal(2, update.RebuiltForOwnChange);
    }

    [Fact]
    public void ConcatenateOffsetsIndicesAndOrdersByChunk()
    {
        var grid = new FluidGrid();
        grid.Fill(new VoxelPos(40, 8, 3), 7_000);
        grid.Fill(new VoxelPos(3, 8, 3), 7_000);
        var mesher = new FluidSurfaceMesher();
        IFluidTerrain floor = new PredicateTerrain(v => v.Y >= 8);
        FluidSurfaceUpdate update = mesher.Update(grid, floor);
        Assert.Equal(2, update.Rebuilt.Count);

        FluidSurfaceMesh whole = FluidSurfaceMesh.Concatenate(update.Rebuilt);
        FluidSurfaceMesh reversed = FluidSurfaceMesh.Concatenate(update.Rebuilt.Reverse());
        Assert.Equal(whole.Positions, reversed.Positions);
        Assert.Equal(whole.Indices, reversed.Indices);
        Assert.Equal(update.QuadCount, whole.QuadCount);
        Assert.Equal(whole.VertexCount - 1, whole.Indices.Max());
        Assert.Equal(new FluidSurfaceMesher().Build(grid, floor).Positions, whole.Positions);
    }

    // ----- the sides pass's shortcuts against the definition -----

    [Fact]
    public void SidesMatchTheDefinitionFaceForFaceThroughFullLayers()
    {
        // A full panel (every cell drawn full — the shortcut reads only its perimeter rows), a
        // neighbour full to a level with a partial top, a thin sheet with a hole beside them and
        // a column standing alone, tops unmerged so a surface cell is drawn at its own amount.
        // The oracle counts cell-faces by the definition — a wet cell drawn higher than a
        // sideways neighbour that is wet or air — and the mesh's side quads, summed by their
        // length in cells, must equal it.
        var grid = new FluidGrid();
        for (int lx = 0; lx < N; lx++)
        {
            for (int lz = 0; lz < N; lz++)
            {
                for (int ly = 0; ly < N; ly++)
                {
                    grid.Fill(new VoxelPos(lx, ly, lz), Cap);
                    if (ly < 16) grid.Fill(new VoxelPos(N + lx, ly, lz), Cap);
                }

                grid.Fill(new VoxelPos(N + lx, 16, lz), 7_000 + ((lx + lz) & 3) * 500);
                if (lx != 5 || lz != 5) grid.Fill(new VoxelPos(lx, 2, N + lz), 3_000);
                if (lx != 5 || lz != 5) grid.Fill(new VoxelPos(lx, 1, N + lz), Cap);
                if (lx != 5 || lz != 5) grid.Fill(new VoxelPos(lx, 0, N + lz), Cap);
            }
        }

        for (int y = 0; y < 40; y++)
        {
            grid.Fill(new VoxelPos(-8, y, -8), Cap);
        }

        var mesher = new FluidSurfaceMesher { Merge = false };
        FluidSurfaceMesh mesh = mesher.Build(grid, new PredicateTerrain(_ => true));

        int Drawn(VoxelPos c)
        {
            int amount = grid.AmountAt(c);
            return amount == 0 ? 0 : grid.AmountAt(c.Offset(0, 1, 0)) > 0 ? Cap : amount;
        }

        int expected = 0;
        foreach (VoxelPos cell in grid.HeldCells())
        {
            int a = Drawn(cell);
            foreach ((int dx, int dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                if (a > Drawn(cell.Offset(dx, 0, dz)))
                {
                    expected++;
                }
            }
        }

        int faces = 0;
        for (int q = 0; q < mesh.QuadCount; q++)
        {
            int v = mesh.Indices[q * 6];
            float nx = mesh.Normals[v * 3], nz = mesh.Normals[(v * 3) + 2];
            if (nx == 0f && nz == 0f)
            {
                continue;
            }

            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                float along = nx != 0f ? mesh.Positions[((v + i) * 3) + 2] : mesh.Positions[(v + i) * 3];
                min = Math.Min(min, along);
                max = Math.Max(max, along);
            }

            faces += (int)MathF.Round((max - min) / VoxelConstants.VoxelSize);
        }

        Assert.True(expected > 5_000, $"the fixture has only {expected} side faces");
        Assert.Equal(expected, faces);

    }

    // ----- helpers -----
    // ----- helpers -----

    private static void FillSheet(FluidGrid grid, int from, int to, int y, int millilitres)
    {
        for (int x = from; x < to; x++)
        {
            for (int z = from; z < to; z++)
            {
                grid.Fill(new VoxelPos(x, y, z), millilitres);
            }
        }
    }

    /// <summary>Updates until nothing is rebuilt: the first build, the settle, then the rest.</summary>
    private static void Settle(FluidSurfaceMesher mesher, FluidGrid grid, IFluidTerrain terrain)
    {
        for (int i = 0; i < 8; i++)
        {
            if (mesher.Update(grid, terrain).PanelsRebuilt == 0)
            {
                return;
            }
        }

        Assert.Fail("the mesher did not settle in eight updates over an unchanging grid");
    }

    /// <summary>The relay's pour: a ball of cells around a point, each filled as far as it holds.</summary>
    private static long Pour(FluidSimulation sim, VoxelPos at, int radius, long pending)
    {
        for (int dz = -radius; dz <= radius && pending > 0; dz++)
        {
            for (int dx = -radius; dx <= radius && pending > 0; dx++)
            {
                for (int dy = -radius; dy <= radius && pending > 0; dy++)
                {
                    if ((dx * dx) + (dy * dy) + (dz * dz) > radius * radius)
                    {
                        continue;
                    }

                    long offer = Math.Min(pending, Cap);
                    pending -= offer - sim.Add(at.Offset(dx, dy, dz), offer);
                }
            }
        }

        return pending;
    }
}
