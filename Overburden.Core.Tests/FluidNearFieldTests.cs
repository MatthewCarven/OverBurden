using Overburden.Core.Fluid;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// The near/far order: near water visited every tick, far water deferred past the boundary under
/// a quota (the water thread, slice 1).
/// </summary>
/// <remarks>
/// <b>The fixture is two pockets that cannot reach each other, one under the viewer and one six
/// chunks off.</b> The first version was a single 247-cell corridor, and it made every settle
/// assertion fail on the fixture rather than on the code: a long corridor is M8a's O(n²) case, in
/// which levelling to the millilitre effectively never finishes in <i>either</i> arm. Disjoint
/// pockets settle in tens of ticks and make "near" and "far" a property of the water rather than
/// of where a front has crept to. Everything sits at y = 8 and z = 5 rather than at zero, because
/// coordinate zero is a chunk's face layer and a boundary-scoped rule reaches every cell of a
/// fixture built there (M8b-4a's finding).
/// </remarks>
public sealed class FluidNearFieldTests
{
    private const int Cap = FluidConstants.CellCapacityMillilitres;

    private static readonly ChunkPos Viewer = new(0, 0, 0);
    private const int Rings = 1;

    // Chunk 0 spans x 0..31 and chunk 1 spans 32..63, so with one ring the near field is x < 64.
    // The near pocket is inside chunk 0; the far pocket is in chunk 6, five rings clear of the
    // near field's edge, so "far" is never a rounding question.
    private const int NearX = 8, FarX = 200;

    private static IFluidTerrain Pockets(int width = 8) => new PredicateTerrain(v =>
        v.Z == 5 && v.Y >= 8 && v.Y <= 40
        && ((v.X >= NearX && v.X < NearX + width) || (v.X >= FarX && v.X < FarX + width)));

    private static VoxelPos Near(int i) => new(NearX + (i % 8), 8 + (i / 8), 5);

    private static VoxelPos Far(int i) => new(FarX + (i % 8), 8 + (i / 8), 5);

    private static FluidSimulation Ordered(int farFloor = 8, int width = 8)
    {
        var sim = new FluidSimulation(Pockets(width));
        sim.SetNearField(Viewer, Rings);
        sim.FarFloorCells = farFloor;
        return sim;
    }

    /// <summary>Fills a run of cells to the brim, which is water with somewhere to go sideways.</summary>
    private static void Pour(FluidSimulation sim, Func<int, VoxelPos> at, int cells)
    {
        for (int i = 0; i < cells; i++)
        {
            sim.Add(at(i), Cap);
        }
    }

    [Fact]
    public void WithNoNearFieldTheBatchIsTheWholeActiveSet()
    {
        var sim = new FluidSimulation(Pockets());
        Pour(sim, Far, 40);

        FluidTickResult tick = sim.Step();

        // The control arm, and the property every recorded figure rests on: no order, no quota,
        // no deferral, and the near/far split reports the whole batch as near.
        Assert.Equal(0, sim.FarBacklog);
        Assert.Equal(0, sim.FarAdmitted);
        Assert.Equal(tick.CellsVisited, sim.NearAdmitted);
        Assert.Equal(0, sim.FarCellsVisited);
    }

    [Fact]
    public void WithNothingNearAwakeTheTickIsStillTheWholeActiveSet()
    {
        FluidSimulation sim = Ordered();
        Pour(sim, Far, 40);

        sim.Step();

        // Throttling rather than scheduling: the quota is unbounded when no near cell is awake, so
        // a viewer standing in a dry field ticks a distant flood exactly as the unordered arm does.
        Assert.Equal(0, sim.FarBacklog);
        Assert.True(sim.FarAdmitted >= 40);
        Assert.Equal(0, sim.NearAdmitted);
    }

    [Fact]
    public void ANearCellCapsTheFarSideAtTheFloor()
    {
        FluidSimulation sim = Ordered(farFloor: 8);
        Pour(sim, Near, 1);
        Pour(sim, Far, 40);

        FluidTickResult tick = sim.Step();

        // One drip beside the viewer drops the far side to the floor — the cliff, named in the
        // plan and asserted here so it cannot move silently.
        Assert.Equal(8, sim.FarAdmitted);
        Assert.True(sim.FarBacklog > 0, "nothing was deferred, so the quota did not bind");
        Assert.Equal(sim.NearAdmitted + 8, tick.CellsVisited);
    }

    [Fact]
    public void TheQuotaIsAtLeastTheNearSetSoATickCostsAtMostTwiceIt()
    {
        FluidSimulation sim = Ordered(farFloor: 2);
        Pour(sim, Near, 20);
        Pour(sim, Far, 100);

        FluidTickResult tick = sim.Step();

        Assert.True(sim.NearAdmitted >= 20);
        Assert.Equal(sim.NearAdmitted, sim.FarAdmitted);
        Assert.Equal(sim.NearAdmitted * 2, tick.CellsVisited);
    }

    [Fact]
    public void EveryNearCellIsVisitedEveryTickWhileAFarFloodIsBacklogged()
    {
        // A wide far pocket with the water poured into one end of it: a column that has to spread
        // stays awake for hundreds of ticks, where a pour that lands already stacked rests in
        // seventeen and takes the quota's cap off before the test has finished looking.
        FluidSimulation sim = Ordered(farFloor: 4, width: 32);
        Pour(sim, Far, 200);

        // The milestone's own claim, asserted rather than argued. The near pocket is kept awake by
        // a running tap, because near water that settles takes the quota's cap off with it — which
        // is correct behaviour and would make this test pass by not testing anything.
        for (int tick = 0; tick < 20; tick++)
        {
            sim.Add(Near(tick % 8), Cap / 2);

            var nearAwake = new HashSet<VoxelPos>();
            foreach (VoxelPos cell in sim.Pending)
            {
                if (cell.X < FarX)
                {
                    nearAwake.Add(cell);
                }
            }

            sim.Step();

            Assert.True(sim.FarBacklog > 0, $"tick {tick}: the far flood drained, so nothing was throttled");
            Assert.True(nearAwake.Count > 0, $"tick {tick}: no near cell was awake, so the quota was unbounded");
            Assert.Equal(nearAwake.Count, sim.NearAdmitted);
        }

        Assert.True(sim.ThrottledTicks >= 20);
    }

    [Fact]
    public void TheDeferredBacklogIsCountedAsActiveAndSettleDoesNotStopOnIt()
    {
        FluidSimulation sim = Ordered(farFloor: 4);
        Pour(sim, Near, 4);
        Pour(sim, Far, 60);

        sim.Step();
        Assert.True(sim.FarBacklog > 0);

        // A count that omitted the carry would report a throttled flood as quiescent, and every
        // caller that waits for rest — Settle, the store, the profile — would believe it.
        Assert.True(sim.ActiveCellCount >= sim.FarBacklog);

        FluidSettleResult settled = sim.Settle();

        Assert.True(settled.Settled, "the settle gave up with water still deferred");
        Assert.Equal(0, sim.FarBacklog);
        Assert.True(sim.VerifyQuiescent(), "rest was reported with moves still available");
        FluidAssert.Conserves(sim);
    }

    [Fact]
    public void DeferredCellsAreListedAsAwakeSoTheStoreStillSeesThem()
    {
        FluidSimulation sim = Ordered(farFloor: 4);
        Pour(sim, Near, 4);
        Pour(sim, Far, 60);

        sim.Step();
        Assert.True(sim.FarBacklog > 0);

        var awake = new HashSet<VoxelPos>(sim.AwakeHeldCells());

        // The one place a deferred cell could be lost for good: AwakeHeldCells is what the store
        // writes and what the eviction lists, so a backlog the dedupe set did not know about would
        // be a lake that quietly stopped being saved.
        int carried = 0;
        foreach (VoxelPos cell in sim.Pending)
        {
            if (sim.Grid.AmountAt(cell) > 0)
            {
                Assert.Contains(cell, awake);
                carried++;
            }
        }

        Assert.True(carried > 0, "no held cell was queued, so the walk proved nothing");
    }

    [Fact]
    public void AViewerMoveReclassifiesTheCarryAndLosesNoCell()
    {
        FluidSimulation sim = Ordered(farFloor: 4);
        Pour(sim, Near, 4);
        Pour(sim, Far, 60);

        sim.Step();
        long heldBefore = sim.TotalMillilitres;
        int activeBefore = sim.ActiveCellCount;
        Assert.True(sim.FarBacklog > 0);

        // Walk to the far end. The carry is re-classified at the next boundary — the once-per-move
        // O(backlog) walk this design pays for — and what was far becomes near.
        sim.SetNearField(new ChunkPos(6, 0, 0), Rings);
        sim.Step();

        Assert.True(sim.NearAdmitted > 0, "the far cells did not become near after the viewer moved");
        Assert.Equal(heldBefore, sim.TotalMillilitres);
        FluidAssert.Conserves(sim);

        // Nothing was duplicated by passing through the classification, and nothing that is still
        // awake fell out of the set the store walks.
        var seen = new HashSet<VoxelPos>();
        foreach (VoxelPos cell in sim.Pending)
        {
            Assert.True(seen.Add(cell), $"{cell} is queued twice after the move");
        }

        var awake = new HashSet<VoxelPos>(sim.AwakeHeldCells());
        foreach (VoxelPos cell in sim.Pending)
        {
            if (sim.Grid.AmountAt(cell) > 0)
            {
                Assert.Contains(cell, awake);
            }
        }

        Assert.True(activeBefore > 0);
    }

    [Fact]
    public void TurningTheOrderOffDrainsWhateverWasDeferred()
    {
        FluidSimulation sim = Ordered(farFloor: 4);
        Pour(sim, Near, 4);
        Pour(sim, Far, 60);

        sim.Step();
        int backlog = sim.FarBacklog;
        Assert.True(backlog > 0);

        sim.SetNearField(Viewer, 0);
        sim.Step();

        // Rings zero is the control arm, and a carry left standing after the flag went off would
        // be water the unordered arm can never reach.
        Assert.Equal(0, sim.FarBacklog);
        Assert.Equal(0, sim.FarAdmitted);
        FluidAssert.Conserves(sim);
    }

    [Fact]
    public void ASlicedTickLandsTheSameWaterAsAWholeTickUnderTheOrder()
    {
        FluidSimulation whole = Ordered(farFloor: 4);
        FluidSimulation sliced = Ordered(farFloor: 4);

        foreach (FluidSimulation sim in new[] { whole, sliced })
        {
            Pour(sim, Near, 6);
            Pour(sim, Far, 80);
        }

        // Admission is a function of the sets and never of the clock, which is the whole reason
        // the far floor is cells rather than a share of a frame's budget: slicing still changes
        // only when a tick ends.
        int slicedTicks = 0;
        for (int tick = 0; tick < 60; tick++)
        {
            whole.Step();

            FluidTickResult? done = null;
            int slices = 0;
            while (done is null)
            {
                done = sliced.Resume(7);
                slices++;
            }

            if (slices > 1)
            {
                slicedTicks++;
            }

            Assert.Equal(whole.NearAdmitted, sliced.NearAdmitted);
            Assert.Equal(whole.FarAdmitted, sliced.FarAdmitted);
            Assert.Equal(whole.FarBacklog, sliced.FarBacklog);
            Assert.Equal(whole.ActiveCellCount, sliced.ActiveCellCount);
            Assert.Equal(whole.TotalMillilitres, sliced.TotalMillilitres);

            foreach (VoxelPos cell in whole.Grid.HeldCells())
            {
                Assert.Equal(whole.Grid.AmountAt(cell), sliced.Grid.AmountAt(cell));
            }
        }

        Assert.True(slicedTicks > 0, "no tick was ever split, so nothing was compared");
    }

    [Fact]
    public void TheOrderedArmRestsWhereTheUnorderedArmRests()
    {
        var unordered = new FluidSimulation(Pockets());
        FluidSimulation ordered = Ordered(farFloor: 4);

        foreach (FluidSimulation sim in new[] { unordered, ordered })
        {
            Pour(sim, Near, 6);
            Pour(sim, Far, 80);
        }

        FluidSettleResult a = unordered.Settle();
        FluidSettleResult b = ordered.Settle();

        // The trajectories differ by design — the order changes who is visited when, which the
        // sideways split reads through its neighbours' amounts — so this asserts what must not
        // differ: both reach a real rest holding the same water, and the ordered arm pays more
        // ticks for it rather than fewer visits.
        Assert.True(a.Settled && b.Settled);
        Assert.True(unordered.VerifyQuiescent() && ordered.VerifyQuiescent());
        Assert.Equal(unordered.TotalMillilitres, ordered.TotalMillilitres);
        Assert.True(ordered.ThrottledTicks > 0, "nothing was ever deferred, so both arms ran the same tick");
        FluidAssert.Conserves(ordered);
    }

    [Fact]
    public void ADeferredCellIsNotWokenTwiceIntoTheSameTick()
    {
        FluidSimulation sim = Ordered(farFloor: 4);
        Pour(sim, Near, 4);
        Pour(sim, Far, 40);
        sim.Step();

        int backlog = sim.FarBacklog;
        Assert.True(backlog > 0);

        // A carried cell is awake, so waking it again must be a no-op: the carry lives in the same
        // dedupe set as the pending list, and a second copy would be a cell visited twice in one
        // tick and counted twice in every figure that reads the active set.
        foreach (VoxelPos cell in new List<VoxelPos>(sim.Pending))
        {
            sim.Disturb(cell);
        }

        Assert.Equal(backlog, sim.FarBacklog);

        var seen = new HashSet<VoxelPos>();
        foreach (VoxelPos cell in sim.Pending)
        {
            Assert.True(seen.Add(cell), $"{cell} is queued twice");
        }
    }

    [Fact]
    public void ATicksActiveAfterCountsWhatItDeferred()
    {
        FluidSimulation sim = Ordered(farFloor: 4);
        Pour(sim, Near, 4);
        Pour(sim, Far, 60);

        FluidTickResult tick = sim.Step();

        // ActiveAfter is what a caller waits on for rest — the profile's `active`, the store's
        // "anything left" — so a tick that reported only its wake list would call a throttled
        // flood quiescent one tick after throttling it.
        Assert.True(sim.FarBacklog > 0);
        Assert.True(
            tick.ActiveAfter >= sim.FarBacklog,
            $"ActiveAfter {tick.ActiveAfter} does not cover the {sim.FarBacklog} deferred cell(s)");
    }

    [Fact]
    public void TheDeferredCellsAreSeedsForTheSheetSolve()
    {
        FluidSimulation sim = Ordered(farFloor: 4);
        Pour(sim, Near, 4);
        Pour(sim, Far, 60);
        sim.Step();

        Assert.True(sim.FarBacklog > 0);

        // LevelSheets seeds from Pending, and the share rule's early stop assumes every unsettled
        // cell neighbours a seed — so a far sheet whose cells are all carried would be invisible to
        // the solve and would never be levelled while anything near stayed awake.
        int carried = 0;
        foreach (VoxelPos cell in sim.Pending)
        {
            if (cell.X >= FarX)
            {
                carried++;
            }
        }

        Assert.True(carried >= sim.FarBacklog, $"{carried} far seed(s) for {sim.FarBacklog} deferred cell(s)");

        SheetLevelResult levelled = sim.LevelSheets();
        Assert.True(levelled.Sheets > 0, "the solve found no sheet at all, so it saw neither pocket");
    }

    [Fact]
    public void TheFarSideMakesProgressOnEveryTickOfAPermanentNearTap()
    {
        FluidSimulation sim = Ordered(farFloor: 4, width: 16);
        Pour(sim, Far, 120);

        // The floor's whole job: a near tap that never stops must not stop the far side. Asserted
        // as progress every tick rather than as eventual rest, because a 16-wide pocket levels
        // diffusively and "it finished" is a property of M8a's O(n²), not of this slice.
        // <b>Not asserted, and named as a gap:</b> that the carry is drained from its front. A rule
        // admitting the newest first would starve the oldest cells under a growing backlog, and
        // there is no cheap boolean for it here — the round-robin rests on the ordering in
        // FluidSimulation.NearField's Classify, which puts carried cells ahead of the tick's wakes.
        long before = sim.FarCellsVisited;
        for (int tick = 0; tick < 200; tick++)
        {
            sim.Add(Near(tick % 8), Cap / 4);
            sim.Remove(Near((tick + 4) % 8), Cap / 4);
            sim.Step();

            if (sim.FarBacklog > 0)
            {
                Assert.True(
                    sim.FarCellsVisited >= before + 4,
                    $"tick {tick}: the far side was starved with {sim.FarBacklog} cell(s) waiting");
            }

            before = sim.FarCellsVisited;
        }

        Assert.True(sim.ThrottledTicks > 0, "nothing was ever deferred, so starvation was impossible anyway");
        FluidAssert.Conserves(sim);
    }

    [Fact]
    public void TheNearFieldIsAColumnSoDepthNeverMakesACellFar()
    {
        var deep = new PredicateTerrain(v => v.Z == 5 && v.X >= NearX && v.X < NearX + 8);
        var sim = new FluidSimulation(deep);
        sim.SetNearField(new ChunkPos(0, 4, 0), Rings);
        sim.FarFloorCells = 1;

        // Two hundred voxels below the viewer's chunk is six chunks down, and it is near: vertical
        // is nearly free at r6h/2v and water is vertical, so a shaft under your feet is near for
        // its whole depth.
        sim.Add(new VoxelPos(NearX, -200, 5), Cap);
        sim.Step();

        Assert.Equal(0, sim.FarAdmitted);
        Assert.Equal(0, sim.FarBacklog);
        Assert.True(sim.NearAdmitted > 0);
    }

    [Fact]
    public void SettleFinishesTheBacklogWhenNothingElseIsAwake()
    {
        FluidSimulation sim = Ordered(farFloor: 4);

        // Whole rows, so every poured cell is at rest on its first visit and nothing is woken.
        // <b>This state is the only one in which the deferred backlog and the wake list disagree</b>
        // — and the battery is what said so: with a partial top row the far flood keeps waking
        // cells, so a Settle that looped on the wake list alone still drained the carry and the
        // recipe "Settle stops on the pending list alone" passed on a scenario where both arms agree.
        Pour(sim, Near, 8);
        Pour(sim, Far, 64);

        sim.Step();

        Assert.True(sim.FarBacklog > 0, "nothing was deferred, so there is no backlog to strand");
        Assert.Empty(sim.Pending.Where(cell => cell.X < FarX));

        FluidSettleResult settled = sim.Settle();

        Assert.True(settled.Settled, "the settle exited with a deferred backlog still standing");
        Assert.Equal(0, sim.FarBacklog);
        Assert.True(sim.VerifyQuiescent());
        FluidAssert.Conserves(sim);
    }

    // The far pocket's chunk column: FarX 200 is chunk 6.
    private static readonly (int X, int Z)[] FarColumn = [(6, 0)];

    [Fact]
    public void AMachineColumnIsNearWhereverTheViewerStands()
    {
        // The near field's second kind of viewer (2026-09-19): a far outlet's mound was visited
        // once in ~260 ticks under the quota and the pump behind it stalled no room. With the
        // machine's column named, the far pocket is admitted whole every tick with the viewer
        // six chunks off — nothing deferred, nothing throttled.
        FluidSimulation sim = Ordered(farFloor: 4);
        sim.SetNearColumns(FarColumn);
        Pour(sim, Near, 4);
        Pour(sim, Far, 60);

        sim.Step();

        Assert.Equal(0, sim.FarBacklog);
        Assert.Equal(0, sim.FarAdmitted);
        Assert.Equal(0, sim.ThrottledTicks);
        Assert.True(sim.NearAdmitted >= 64, $"only {sim.NearAdmitted} admitted as near");
        Assert.Equal(1, sim.NearColumnCount);
        Assert.True(sim.IsNearColumn(6, 0));
        FluidAssert.Conserves(sim);
    }

    [Fact]
    public void NamingAMachineColumnReclassifiesTheCarryAndLosesNoCell()
    {
        // The viewer-move rule for a placement: the carry is walked once at the next boundary,
        // and what was far is near. Nothing duplicated, nothing dropped from the set the store walks.
        FluidSimulation sim = Ordered(farFloor: 4);
        Pour(sim, Near, 4);
        Pour(sim, Far, 60);

        sim.Step();
        long heldBefore = sim.TotalMillilitres;
        Assert.True(sim.FarBacklog > 0, "nothing was deferred, so there is no carry to re-classify");

        sim.SetNearColumns(FarColumn);
        sim.Step();

        Assert.Equal(0, sim.FarBacklog);
        Assert.True(sim.NearAdmitted > 4, "the far cells did not become near after the column was named");
        Assert.Equal(heldBefore, sim.TotalMillilitres);
        FluidAssert.Conserves(sim);

        var seen = new HashSet<VoxelPos>();
        foreach (VoxelPos cell in sim.Pending)
        {
            Assert.True(seen.Add(cell), $"{cell} is queued twice after the column was named");
        }

        var awake = new HashSet<VoxelPos>(sim.AwakeHeldCells());
        foreach (VoxelPos cell in sim.Pending)
        {
            if (sim.Grid.AmountAt(cell) > 0)
            {
                Assert.Contains(cell, awake);
            }
        }
    }

    [Fact]
    public void ADismantledMachinesColumnIsFarAgain()
    {
        // A wide far pocket poured at one end stays awake for hundreds of ticks, so the column
        // being taken away is observable: the next composition defers it under the quota again.
        FluidSimulation sim = Ordered(farFloor: 4, width: 32);
        sim.SetNearColumns(FarColumn);
        Pour(sim, Far, 200);
        Pour(sim, Near, 4);

        sim.Step();
        Assert.Equal(0, sim.FarBacklog);

        sim.SetNearColumns([]);
        sim.Add(Near(0), Cap / 2);
        sim.Step();

        Assert.Equal(0, sim.NearColumnCount);
        Assert.True(sim.FarBacklog > 0, "the far pocket was still admitted after its column was removed");
        FluidAssert.Conserves(sim);
    }

    [Fact]
    public void WithNoOrderTheMachineColumnsChangeNothing()
    {
        // Rings zero is the control arm — every fixture, every bench — and a machine column
        // must not put it onto the classification path.
        var sim = new FluidSimulation(Pockets());
        sim.SetNearColumns(FarColumn);
        Pour(sim, Near, 4);
        Pour(sim, Far, 60);

        FluidTickResult tick = sim.Step();

        Assert.Equal(0, sim.FarAdmitted);
        Assert.Equal(0, sim.FarBacklog);
        Assert.Equal(0, sim.ThrottledTicks);
        Assert.Equal(tick.CellsVisited, sim.NearAdmitted);
        Assert.Equal(0, sim.FarCellsVisited);
    }
}
