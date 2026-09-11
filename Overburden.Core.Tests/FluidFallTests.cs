using Overburden.Core.Fluid;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// M8d-3's fall rule: a visit falls as far as is vacant, up to <see cref="FluidSimulation.FallCells"/>,
/// stateless, and lands the water where the one-cell rule would have got it several ticks later.
/// </summary>
public sealed class FluidFallTests
{
    private const int Cap = FluidConstants.CellCapacityMillilitres;

    /// <summary>A box with its floor at y = 8: open from 8 to 63, walls outside x, z 0..15.</summary>
    private static BoxTerrain Shaft() => new(0, 8, 0, 15, 63, 15);

    [Fact]
    public void ADropTenCellsUpLandsOnTheFloorInOneTickUnderTheRuleAndTenUnderTheControl()
    {
        // The landing is on the floor, so the next tick spreads it; the rule is read off the
        // floor's layer rather than the cell, in case the batch's order visits the landing too.
        var quick = new FluidSimulation(Shaft(), fallCells: 16);
        quick.Add(new VoxelPos(5, 18, 5), 6_000);
        quick.Step();
        Assert.Equal(6_000, OnLayer(quick, 8));
        Assert.Equal(1L, quick.LongFalls);
        Assert.Equal(10L, quick.CellsFallen);

        // The control falls a cell a tick — nine ticks for ten cells, because the first tick's
        // landing is the source's neighbour, woken into the same batch and visited later in it
        // (M8b-4a's "a poured cell drops two voxels in one Step"); the rule's landing ten cells
        // down is nobody's neighbour and waits for the next tick.
        var slow = new FluidSimulation(Shaft(), fallCells: 1);
        slow.Add(new VoxelPos(5, 18, 5), 6_000);
        slow.Step();
        Assert.Equal(0, OnLayer(slow, 8));
        int ticks = 1;
        while (OnLayer(slow, 8) < 6_000 && ticks < 20)
        {
            slow.Step();
            ticks++;
        }

        Assert.Equal(9, ticks);
        Assert.Equal(0L, slow.LongFalls);

        // Both rest on the same water, and the rest is real.
        FluidAssert.SettlesAndRests(quick);
        FluidAssert.SettlesAndRests(slow);
        FluidAssert.Conserves(quick);
        FluidAssert.Conserves(slow);
    }

    [Fact]
    public void TheFallIsCappedAtFallCells()
    {
        var sim = new FluidSimulation(Shaft(), fallCells: 4);
        sim.Add(new VoxelPos(5, 30, 5), 6_000);
        sim.Step();
        Assert.Equal(6_000, sim.Grid.AmountAt(new VoxelPos(5, 26, 5)));
        Assert.Equal(4L, sim.CellsFallen);
        sim.Step();
        Assert.Equal(6_000, sim.Grid.AmountAt(new VoxelPos(5, 22, 5)));

        Assert.Throws<ArgumentOutOfRangeException>(() => new FluidSimulation(Shaft(), fallCells: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FluidSimulation(Shaft(), fallCells: FluidSimulation.MaxFallCells + 1));
    }

    [Fact]
    public void ADropLandsOnWaterNotThroughIt()
    {
        // Half a cell of water on the floor, a drop ten cells up: the probe reads empty cells
        // only, so the drop lands in the empty cell over the water — nine cells fallen, not ten
        // — and merges into it next tick by the one-cell rule, clamped by the room. A probe that
        // passed a cell holding anything would land in the partial cell itself and read ten.
        // (The floor water goes in through the grid, asleep and off the ledger — through Add it
        // would be awake and spread before the drop arrives — so the total is asserted by hand.)
        var sim = new FluidSimulation(Shaft(), fallCells: 16);
        sim.Grid.Fill(new VoxelPos(5, 8, 5), Cap / 2);
        sim.Add(new VoxelPos(5, 18, 5), 6_000);
        sim.Step();
        Assert.Equal(9L, sim.CellsFallen);
        Assert.Equal(6_000, sim.Grid.AmountAt(new VoxelPos(5, 9, 5)));
        Assert.Equal(Cap / 2, sim.Grid.AmountAt(new VoxelPos(5, 8, 5)));
        sim.Step();
        Assert.Equal(0, sim.Grid.AmountAt(new VoxelPos(5, 9, 5)));
        Assert.Equal((Cap / 2) + 6_000, sim.Grid.AmountAt(new VoxelPos(5, 8, 5)));
        Assert.Equal((Cap / 2) + 6_000, sim.TotalMillilitres);

        // A full cell at y = 12 with air under it (put there by hand, asleep): the drop from 18
        // lands on it and cannot pass, so nothing reaches the floor this tick.
        sim = new FluidSimulation(Shaft(), fallCells: 16);
        sim.Grid.Fill(new VoxelPos(5, 12, 5), Cap);
        sim.Add(new VoxelPos(5, 18, 5), 6_000);
        sim.Step();
        Assert.Equal(0, OnLayer(sim, 8));
        Assert.Equal(5L, sim.CellsFallen);
        Assert.Equal(Cap + 6_000, sim.TotalMillilitres);
    }

    [Fact]
    public void ADropStopsAtRockAndTheLandingIsWokenSoItSpreads()
    {
        // A ledge at y = 20 under the drop, three cells wide, open air below it either side: the
        // drop lands on the ledge (rock is not vacant) and, being woken as a landing that is not
        // its source's neighbour — the line M8a kept for the relay — spreads next tick and falls
        // off the ledge's sides. Without Wake(to) it would sleep on the ledge with a move.
        var ledge = new PredicateTerrain(v =>
            v.X >= 0 && v.X < 16 && v.Z >= 0 && v.Z < 16 && v.Y >= 8 && v.Y < 64
            && !(v.Y == 20 && v.X >= 4 && v.X <= 6 && v.Z == 5));
        var sim = new FluidSimulation(ledge, fallCells: 16);
        sim.Add(new VoxelPos(5, 30, 5), Cap);
        sim.Step();
        Assert.Equal(Cap, sim.Grid.AmountAt(new VoxelPos(5, 21, 5)));
        Assert.True(sim.ActiveCellCount > 0, "the landing was not woken");
        sim.Step();
        Assert.True(sim.Grid.AmountAt(new VoxelPos(5, 21, 5)) < Cap, "the landing did not spread");
        FluidSettleResult rest = FluidAssert.SettlesAndRests(sim);
        Assert.True(rest.Ticks < 60, $"{rest.Ticks} ticks to rest");
        Assert.True(sim.Grid.AmountAt(new VoxelPos(5, 21, 5)) < Cap);
        FluidAssert.Conserves(sim);
    }

    [Fact]
    public void ARunOfFallsRestsInFewerTicksToTheSameWaterAsTheControl()
    {
        // A column of drops down a deep shaft under both arms: the same litres on the same floor,
        // the rule in fewer ticks, every visit conserving, the rest real under both. And the sum
        // of amount × Y — the proof's vertical measure — never rises, and falls on the first tick.
        foreach (int fall in new[] { 1, 16 })
        {
            var sim = new FluidSimulation(Shaft(), fallCells: fall);
            for (int y = 40; y < 60; y += 4)
            {
                sim.Add(new VoxelPos(7, y, 7), 3_000);
            }

            long measure = Measure(sim);
            int ticks = 0;
            while (sim.ActiveCellCount > 0 && ticks < 10_000)
            {
                FluidTickResult result = sim.Step();
                ticks++;
                long now = Measure(sim);
                Assert.True(now <= measure, $"Σ(amount × Y) rose on tick {ticks} ({fall} cells)");
                if (ticks == 1)
                {
                    Assert.True(now < measure && result.CellsMoved > 0, "the first tick moved nothing down");
                }

                measure = now;
            }

            FluidAssert.SettlesAndRests(sim);
            FluidAssert.Conserves(sim);
            Assert.Equal(15_000, sim.TotalMillilitres);
            Assert.True(fall == 1 ? ticks > 40 : ticks < 20, $"{ticks} ticks under {fall}");
        }
    }

    [Fact]
    public void ADropStopsAtTheEdgeOfTheLoadedWorldAndFallsOnWhenTheChunkArrives()
    {
        // The probe asks the terrain, so a chunk that is not loaded is solid to it as to every
        // other move (M4a's rule): a drop above one lands on the boundary and rests there, and
        // the chunk's arrival wakes it through the face wake, after which it falls the rest.
        var terrain = new LoadableTerrain(Shaft());
        var lower = new ChunkPos(0, 0, 0);
        terrain.Unload(lower);
        var sim = new FluidSimulation(terrain, fallCells: 16);
        sim.Add(new VoxelPos(5, 45, 5), 6_000);
        sim.Step();
        Assert.Equal(6_000, sim.Grid.AmountAt(new VoxelPos(5, 32, 5)));
        Assert.Equal(13L, sim.CellsFallen);
        FluidAssert.SettlesAndRests(sim);
        Assert.Equal(6_000, OnLayer(sim, 32));

        terrain.Load(lower);
        sim.ChunkArrived(lower);
        sim.Step();
        Assert.Equal(0, OnLayer(sim, 32));
        sim.Step();
        Assert.Equal(6_000, OnLayer(sim, 8));
        FluidAssert.SettlesAndRests(sim);
        FluidAssert.Conserves(sim);
    }

    private static int OnLayer(FluidSimulation sim, int y)
    {
        int sum = 0;
        foreach (VoxelPos cell in sim.Grid.HeldCells())
        {
            if (cell.Y == y)
            {
                sum += sim.Grid.AmountAt(cell);
            }
        }

        return sum;
    }

    private static long Measure(FluidSimulation sim)
    {
        long sum = 0;
        foreach (VoxelPos cell in sim.Grid.HeldCells())
        {
            sum += (long)sim.Grid.AmountAt(cell) * cell.Y;
        }

        return sum;
    }
}
