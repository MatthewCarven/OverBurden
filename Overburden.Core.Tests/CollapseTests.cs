using Overburden.Core.Content;
using Overburden.Core.Fluid;
using Overburden.Core.Structure;
using Overburden.Core.Voxels;
using Overburden.Core.Worldgen;

namespace Overburden.Core.Tests;

/// <summary>
/// M9c-1: the fall and the timer — the plug to the voxel, the two oracles over a fuzz of rooms,
/// the run across a horizontal face, the gates, the water, and the window.
/// </summary>
/// <remarks>
/// The oracles are decision 6's: <b>solid voxels are conserved</b> — rock out equals rubble in —
/// and <b>Σ(Y of solid voxels) strictly decreases at every fall</b>, which is M8a's potential with
/// rock for water and is what makes a cascade terminate. Around them the fixpoint check holds
/// after every settle, because a fall is an edit like any other and the field must be <em>the</em>
/// field afterwards.
/// </remarks>
public class CollapseTests
{
    private const int Air = SupportTestWorld.Air;
    private const int Stone = SupportTestWorld.Stone;
    private const int Rubble = SupportTestWorld.Rubble;
    private const int K = SupportRule.MaxBudget;
    private const long Cap = FluidConstants.CellCapacityMillilitres;

    private static readonly ChunkPos[] TwoByTwo = [new(0, 0, 0), new(1, 0, 0), new(0, 0, 1), new(1, 0, 1)];

    /// <summary>A block of stone chunks, inclusive, with bites taken out.</summary>
    private static SupportTestWorld StoneBlock(ChunkPos min, ChunkPos max, params ChunkPos[] bites)
    {
        var world = new SupportTestWorld();
        world.FillBox(min, max, Stone);
        foreach (ChunkPos bite in bites)
        {
            world.Fill(bite, Air);
        }

        return world;
    }

    private static SupportRegion Settled(SupportTestWorld world, IEnumerable<ChunkPos> dug)
    {
        SupportRegion region = world.NewRegion();
        foreach (ChunkPos chunk in dug)
        {
            region.MarkDug(chunk);
        }

        region.Settle();
        Assert.Empty(region.VerifyFixpoint());
        return region;
    }

    /// <summary>The 2 × 2 room and its twenty-voxel plug, settled.</summary>
    private static (SupportTestWorld World, SupportRegion Region) Plug()
    {
        SupportTestWorld world = StoneBlock(new ChunkPos(-2, -2, -2), new ChunkPos(3, 2, 3), TwoByTwo);
        SupportRegion region = Settled(world, TwoByTwo);
        Assert.Equal(20, region.SettledAtRiskVoxels);
        return (world, region);
    }

    private static HashSet<VoxelPos> PlugVoxels()
    {
        var plug = new HashSet<VoxelPos>();
        for (int x = 30; x <= 33; x++)
        for (int z = 30; z <= 33; z++)
        {
            plug.Add(new VoxelPos(x, 32, z));
        }

        for (int x = 31; x <= 32; x++)
        for (int z = 31; z <= 32; z++)
        {
            plug.Add(new VoxelPos(x, 33, z));
        }

        return plug;
    }

    private static CollapseWindow Instant => new() { MinSeconds = 0, MaxSeconds = 0 };

    // ------------------------------------------------------------------ the plug

    [Fact]
    public void ThePlugFallsToTheFloorAsTwelveSinglesAndFourPairs()
    {
        (SupportTestWorld world, SupportRegion region) = Plug();
        var editor = new SupportTestWorld.Editor(world, region);
        (long solidBefore, long sumBefore) = world.Census();

        // The plug straddles four roof chunks; each holds a quarter of it. Fire all four — one
        // per settle, because a fall is an edit and the other three roofs are its neighbours:
        // the second read unsettled until the first's writes have derived.
        int fell = 0;
        foreach (ChunkPos bite in TwoByTwo)
        {
            ChunkPos roof = bite.Offset(0, 1, 0);
            if (fell > 0)
            {
                Assert.Equal(CollapseOutcome.WaitingUnsettled, Collapse.TryFall(region, roof, editor, world.Materials, Rubble, null, out _));
                region.Settle();
            }

            CollapseOutcome outcome = Collapse.TryFall(region, roof, editor, world.Materials, Rubble, water: null, out CollapseResult result);
            Assert.Equal(CollapseOutcome.Fell, outcome);
            Assert.Equal(5, result.Voxels);
            Assert.Equal(4, result.Columns);
            Assert.Equal(32, result.DeepestDrop);
            Assert.Equal(0, result.StaleSkipped);
            fell += result.Voxels;
        }

        Assert.Equal(20, fell);

        // Every plug voxel is air, and the rubble is on the floor of the bite: twelve columns with
        // one voxel at y = 0, four with two at y = 0 and 1 — the run of two lands as a run of two.
        foreach (VoxelPos voxel in PlugVoxels())
        {
            Assert.Equal(Air, world.Material(voxel));
        }

        for (int x = 30; x <= 33; x++)
        for (int z = 30; z <= 33; z++)
        {
            bool inner = x is 31 or 32 && z is 31 or 32;
            Assert.Equal(Rubble, world.Material(new VoxelPos(x, 0, z)));
            Assert.Equal(inner ? Rubble : Air, world.Material(new VoxelPos(x, 1, z)));
            Assert.Equal(Air, world.Material(new VoxelPos(x, 2, z)));
        }

        Assert.Equal(20, world.Count(Rubble));

        // The two oracles: the count is conserved and Σ(Y) fell by exactly 12 × 32 + 4 × 64.
        (long solidAfter, long sumAfter) = world.Census();
        Assert.Equal(solidBefore, solidAfter);
        Assert.Equal(sumBefore - (12 * 32) - (4 * 64), sumAfter);

        // The fall is an edit: the roofs and the bites are requeued, and once settled nothing is
        // at risk — the voxels that rested on the plug at 0 reach 0 again sideways, and the
        // rubble rests on the floor.
        Assert.True(region.Pending > 0);
        region.Settle();
        Assert.Empty(region.VerifyFixpoint());
        Assert.Equal(0, region.SettledAtRiskVoxels);
    }

    [Fact]
    public void FiringAChunkWithNothingAtRiskWritesNothing()
    {
        (SupportTestWorld world, SupportRegion region) = Plug();
        var editor = new SupportTestWorld.Editor(world, region);

        // A wall chunk: analysed, settled, full throughout.
        CollapseOutcome outcome = Collapse.TryFall(region, new ChunkPos(2, 0, 0), editor, world.Materials, Rubble, null, out _);

        Assert.Equal(CollapseOutcome.NothingAtRisk, outcome);
        Assert.Equal(0, editor.Writes);
    }

    // ------------------------------------------------------------------ the two oracles, fuzzed

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void EveryFallConservesTheCountAndLowersSumYUntilNothingIsAtRisk(int seedIndex)
    {
        // Random rooms in a block of stone with air over it: one to four bites in a row or a
        // square, at a depth that leaves a stone roof, fired by the scheduler with no delay
        // until nothing is at risk, the fixpoint checked after every settle.
        var random = new Random(0x9C1 + seedIndex);
        var world = new SupportTestWorld();
        world.FillBox(new ChunkPos(-2, -3, -2), new ChunkPos(5, 1, 5), Stone);
        world.FillBox(new ChunkPos(-2, 2, -2), new ChunkPos(5, 3, 5), Air);

        int bitesX = random.Next(1, 4);
        int bitesZ = random.Next(1, 4);
        var bites = new List<ChunkPos>();
        for (int cx = 0; cx < bitesX; cx++)
        for (int cz = 0; cz < bitesZ; cz++)
        {
            var bite = new ChunkPos(cx, -1, cz);
            world.Fill(bite, Air);
            bites.Add(bite);
        }

        // A few extra voxels chipped out of the roof, so the fixture is not symmetric.
        SupportRegion region = world.NewRegion();
        var editor = new SupportTestWorld.Editor(world, region);
        foreach (ChunkPos bite in bites)
        {
            region.MarkDug(bite);
        }

        for (int i = 0; i < 6; i++)
        {
            var chip = new VoxelPos(random.Next(0, bitesX * 32), random.Next(0, 3), random.Next(0, bitesZ * 32));
            editor.SetVoxel(chip, Air);
        }

        region.Settle();
        Assert.Empty(region.VerifyFixpoint());
        int atRiskAtStart = region.SettledAtRiskVoxels;

        var scheduler = new CollapseScheduler(region, editor, world.Materials, Rubble, seed: 7, Instant);
        (long solid, long sumY) = world.Census();
        int stages = 0;
        long fallen = 0;
        for (int sweep = 0; sweep < 64; sweep++)
        {
            IReadOnlyList<CollapseFire> fires = scheduler.Sweep(worldMillis: sweep * 500L);
            if (fires.Count == 0 && scheduler.Due == 0 && region.SettledAtRiskVoxels == 0)
            {
                break;
            }

            foreach (CollapseFire fire in fires)
            {
                stages++;
                fallen += fire.Result.Voxels;
                Assert.True(fire.Result.Voxels > 0);
                Assert.Equal(0, fire.Result.StaleSkipped);
            }

            if (fires.Count > 0)
            {
                // Once per sweep: two roofs far enough apart to both be settled can fall in one.
                (long solidNow, long sumNow) = world.Census();
                Assert.Equal(solid, solidNow);
                Assert.True(sumNow < sumY, $"Σ(Y) did not fall at stage {stages}: {sumY} → {sumNow}");
                sumY = sumNow;
            }

            region.Settle();
            Assert.Empty(region.VerifyFixpoint());
        }

        Assert.Equal(0, region.SettledAtRiskVoxels);
        Assert.Equal(0, scheduler.Armed);
        Assert.Equal(fallen, scheduler.VoxelsFallen);
        Assert.Equal(fallen, world.Count(Rubble));
        Assert.Equal(0, scheduler.StaleSkipped);
        Assert.True(atRiskAtStart == 0 || fallen > 0, $"{atRiskAtStart} at risk at the start and nothing fell");
        Assert.True(atRiskAtStart > 0 || fallen == 0, $"nothing at risk at the start and {fallen} fell");
    }

    // ------------------------------------------------------------------ across a horizontal face

    /// <summary>Two chunks of stone stacked in mid-air over a stone floor, so every column is a 64-voxel at-risk run across y = 32.</summary>
    private static (SupportTestWorld World, SupportRegion Region, ChunkPos Lower, ChunkPos Upper) Stack()
    {
        var world = new SupportTestWorld();
        world.FillBox(new ChunkPos(-1, -1, -1), new ChunkPos(1, 4, 1), Air);
        world.FillBox(new ChunkPos(-1, -2, -1), new ChunkPos(1, -2, 1), Stone);
        var lower = new ChunkPos(0, 1, 0);
        var upper = new ChunkPos(0, 2, 0);
        world.Fill(lower, Stone);
        world.Fill(upper, Stone);
        SupportRegion region = Settled(world, [lower, upper]);
        Assert.Equal(2 * VoxelConstants.ChunkVolume, region.SettledAtRiskVoxels);
        return (world, region, lower, upper);
    }

    [Fact]
    public void ARunAcrossAChunkFaceFallsAsOneWhenTheUpperChunkFires()
    {
        (SupportTestWorld world, SupportRegion region, ChunkPos lower, ChunkPos upper) = Stack();
        var editor = new SupportTestWorld.Editor(world, region);
        (long solid, long sumY) = world.Census();

        CollapseOutcome outcome = Collapse.TryFall(region, upper, editor, world.Materials, Rubble, null, out CollapseResult result);

        // The upper chunk's runs reach through the lower chunk's field: 64 voxels a column, one
        // fall, both chunks listed as fallen from.
        Assert.Equal(CollapseOutcome.Fell, outcome);
        Assert.Equal(2 * VoxelConstants.ChunkVolume, result.Voxels);
        Assert.Equal(1024, result.Columns);
        Assert.Contains(lower, result.FellFrom);
        Assert.Contains(upper, result.FellFrom);

        // A 64-run over 64 voxels of air (chunks −1 and 0) lands on the floor: rubble fills
        // chunks −1 and 0, the two stone chunks are air.
        Assert.Equal(64, result.DeepestDrop);
        Assert.Equal(2 * VoxelConstants.ChunkVolume, world.Count(Rubble));
        Assert.Equal(Rubble, world.Material(new VoxelPos(5, -32, 5)));
        Assert.Equal(Rubble, world.Material(new VoxelPos(5, 31, 5)));
        Assert.Equal(Air, world.Material(new VoxelPos(5, 32, 5)));
        Assert.Equal(Air, world.Material(new VoxelPos(5, 95, 5)));

        (long solidAfter, long sumAfter) = world.Census();
        Assert.Equal(solid, solidAfter);
        Assert.Equal(sumY - (2L * VoxelConstants.ChunkVolume * 64), sumAfter);

        region.Settle();
        Assert.Empty(region.VerifyFixpoint());
        Assert.Equal(0, region.SettledAtRiskVoxels);
    }

    [Fact]
    public void ARunAcrossAChunkFaceFallsInTwoStagesWhenTheLowerChunkFiresFirst()
    {
        (SupportTestWorld world, SupportRegion region, ChunkPos lower, ChunkPos upper) = Stack();
        var editor = new SupportTestWorld.Editor(world, region);
        (long solid, _) = world.Census();

        // The lower chunk's runs end at its own top face — nothing above it is followed — so it
        // falls alone, and the upper chunk, requeued by the edits under it, waits.
        Assert.Equal(CollapseOutcome.Fell, Collapse.TryFall(region, lower, editor, world.Materials, Rubble, null, out CollapseResult first));
        Assert.Equal(VoxelConstants.ChunkVolume, first.Voxels);
        Assert.Equal(CollapseOutcome.WaitingUnsettled, Collapse.TryFall(region, upper, editor, world.Materials, Rubble, null, out _));

        region.Settle();
        Assert.Empty(region.VerifyFixpoint());
        Assert.Equal(VoxelConstants.ChunkVolume, region.SettledAtRiskVoxels);

        // Then the upper chunk falls onto the lower's rubble, whose top is at y = -1: 64 voxels
        // of drop for a chunk that began at 64, and the end state is the one-fall test's exactly.
        Assert.Equal(CollapseOutcome.Fell, Collapse.TryFall(region, upper, editor, world.Materials, Rubble, null, out CollapseResult second));
        Assert.Equal(VoxelConstants.ChunkVolume, second.Voxels);
        Assert.Equal(64, second.DeepestDrop);
        Assert.Equal(2 * VoxelConstants.ChunkVolume, world.Count(Rubble));
        Assert.Equal(Rubble, world.Material(new VoxelPos(5, 31, 5)));
        Assert.Equal(Air, world.Material(new VoxelPos(5, 32, 5)));
        Assert.Equal(solid, world.Census().Solid);

        region.Settle();
        Assert.Empty(region.VerifyFixpoint());
        Assert.Equal(0, region.SettledAtRiskVoxels);
    }

    [Fact]
    public void AFireWaitsWhileAChunkUnderItsRunsIsUnsettled()
    {
        (SupportTestWorld world, SupportRegion region, ChunkPos lower, ChunkPos upper) = Stack();
        var editor = new SupportTestWorld.Editor(world, region);

        // A voxel placed two chunks under the lower one queues its chunk and the six around it,
        // one of which is the air chunk under the lower: the lower is unsettled (one of its
        // seven is pending) while the upper is not (none of its seven is). The upper's own gate
        // passes; the walk into the lower is what refuses, and nothing is written.
        editor.SetVoxel(new VoxelPos(5, -27, 5), Stone);
        Assert.True(region.IsSettled(upper));
        Assert.False(region.IsSettled(lower));
        Assert.Equal(CollapseOutcome.WaitingUnsettled, Collapse.TryFall(region, upper, editor, world.Materials, Rubble, null, out _));
        Assert.Equal(1, editor.Writes);

        region.Settle();
        Assert.Equal(CollapseOutcome.Fell, Collapse.TryFall(region, upper, editor, world.Materials, Rubble, null, out CollapseResult result));
        Assert.Equal(2 * VoxelConstants.ChunkVolume, result.Voxels);

        // Column (5, 5) landed on the placed voxel rather than the floor.
        Assert.Equal(Rubble, world.Material(new VoxelPos(5, -26, 5)));
        Assert.Equal(Stone, world.Material(new VoxelPos(5, -27, 5)));
        Assert.Equal(Air, world.Material(new VoxelPos(5, -28, 5)));
    }

    [Fact]
    public void TwoRunsInOneColumnFallBottomUpTheUpperOntoTheLowersRubble()
    {
        // A stone chunk in mid-air with a one-voxel-thick air slab through its middle, so every
        // column is at-risk, air, at-risk: the lower 16 land on the floor, the upper 15 on them.
        var world = new SupportTestWorld();
        world.FillBox(new ChunkPos(-1, -1, -1), new ChunkPos(1, 3, 1), Air);
        world.FillBox(new ChunkPos(-1, -2, -1), new ChunkPos(1, -2, 1), Stone);
        var block = new ChunkPos(0, 1, 0);
        world.Fill(block, Stone);
        world.SetBox(new VoxelPos(0, 48, 0), new VoxelPos(31, 48, 31), Air);
        SupportRegion region = Settled(world, [block]);
        Assert.Equal(VoxelConstants.ChunkVolume - 1024, region.SettledAtRiskVoxels);
        var editor = new SupportTestWorld.Editor(world, region);
        (long solid, _) = world.Census();

        Assert.Equal(CollapseOutcome.Fell, Collapse.TryFall(region, block, editor, world.Materials, Rubble, null, out CollapseResult result));

        Assert.Equal(VoxelConstants.ChunkVolume - 1024, result.Voxels);
        Assert.Equal(1024, result.Columns);
        Assert.Equal(65, result.DeepestDrop);
        for (int y = -32; y <= -2; y++)
        {
            Assert.Equal(Rubble, world.Material(new VoxelPos(7, y, 7)));
        }

        Assert.Equal(Air, world.Material(new VoxelPos(7, -1, 7)));
        Assert.Equal(solid, world.Census().Solid);
        region.Settle();
        Assert.Empty(region.VerifyFixpoint());
        Assert.Equal(0, region.SettledAtRiskVoxels);
    }

    // ------------------------------------------------------------------ the gates

    [Fact]
    public void AChunkMinedSinceItsSettleIsNotFiredFromItsStaleSnapshot()
    {
        (SupportTestWorld world, SupportRegion region) = Plug();
        var editor = new SupportTestWorld.Editor(world, region);
        ChunkPos roof = new ChunkPos(0, 1, 0);
        Assert.NotNull(region.SettledField(roof));

        // The player mines the plug out of this roof chunk through the editor, which tells the
        // region: the snapshot still says five at risk, the chunk is unsettled, and a fall is
        // refused rather than read from it.
        foreach (VoxelPos voxel in PlugVoxels().Where(v => v.Chunk == roof))
        {
            editor.SetVoxel(voxel, Air);
        }

        Assert.Equal(5, region.SettledField(roof)!.AtRiskCount);
        Assert.Equal(CollapseOutcome.WaitingUnsettled, Collapse.TryFall(region, roof, editor, world.Materials, Rubble, null, out _));
        Assert.Equal(0, world.Count(Rubble));

        region.Settle();
        Assert.Equal(CollapseOutcome.NothingAtRisk, Collapse.TryFall(region, roof, editor, world.Materials, Rubble, null, out _));
        Assert.Equal(0, world.Count(Rubble));
    }

    [Fact]
    public void AnAtRiskVoxelThatIsAirInTheWorldIsSkippedAndCounted()
    {
        (SupportTestWorld world, SupportRegion region) = Plug();
        var editor = new SupportTestWorld.Editor(world, region);
        ChunkPos roof = new ChunkPos(0, 1, 0);

        // Behind the region's back — a write that told nobody — the roof chunk's corner plug
        // voxels are air. The gate cannot see it; the world can, and the fall skips them.
        world.Set(new VoxelPos(31, 32, 31), Air);
        world.Set(new VoxelPos(31, 33, 31), Air);
        Assert.True(region.IsSettled(roof));

        Assert.Equal(CollapseOutcome.Fell, Collapse.TryFall(region, roof, editor, world.Materials, Rubble, null, out CollapseResult result));

        Assert.Equal(2, result.StaleSkipped);
        Assert.Equal(3, result.Voxels);
        Assert.Equal(Air, world.Material(new VoxelPos(31, 0, 31)));
        Assert.Equal(Rubble, world.Material(new VoxelPos(30, 0, 30)));
    }

    // ------------------------------------------------------------------ the water

    private sealed class Flooded
    {
        public required SupportTestWorld World { get; init; }
        public required SupportRegion Region { get; init; }
        public required SupportTestWorld.Editor Editor { get; init; }
        public required FluidSimulation Fluid { get; init; }
    }

    /// <summary>The plug's room with water in its bites from y = 0 up to <paramref name="toY"/> inclusive, asleep.</summary>
    private static Flooded Room(int toY)
    {
        (SupportTestWorld world, SupportRegion region) = Plug();
        FluidSimulation fluid = null!;
        var editor = new SupportTestWorld.Editor(world, region, (voxel, _) => fluid.Disturb(voxel));
        fluid = new FluidSimulation(new VoxelFluidTerrain(editor, world.Materials));
        for (int x = 0; x < 64; x++)
        for (int z = 0; z < 64; z++)
        for (int y = 0; y <= toY; y++)
        {
            Assert.Equal(0, fluid.Grid.Fill(new VoxelPos(x, y, z), Cap));
        }

        return new Flooded { World = world, Region = region, Editor = editor, Fluid = fluid };
    }

    [Fact]
    public void ARoofFallingIntoAFloodedRoomLiftsWhatItLandsInAndReturnsItToTheCavity()
    {
        Flooded f = Room(toY: 31);
        long total = f.Fluid.TotalMillilitres;
        Assert.Equal(64L * 64 * 32 * Cap, total);

        foreach (ChunkPos bite in TwoByTwo)
        {
            f.Region.Settle();
            Assert.Equal(CollapseOutcome.Fell, Collapse.TryFall(f.Region, bite.Offset(0, 1, 0), f.Editor, f.World.Materials, Rubble, f.Fluid, out CollapseResult result));
            Assert.Equal(5 * Cap, result.LiftedMillilitres);
            Assert.Equal(result.LiftedMillilitres, result.ReturnedMillilitres);
            Assert.Equal(0, result.DisplacedMillilitres);
        }

        // Twenty cells' worth lifted, twenty returned, the total untouched, nothing displaced —
        // and the water is in the cavity: every plug voxel holds a full cell, every landing
        // cell is rubble with nothing in it.
        Assert.Equal(20 * Cap, f.Fluid.LiftedMillilitres);
        Assert.Equal(20 * Cap, f.Fluid.ReturnedMillilitres);
        Assert.Equal(0, f.Fluid.DisplacedMillilitres);
        Assert.Equal(total, f.Fluid.TotalMillilitres);
        foreach (VoxelPos voxel in PlugVoxels())
        {
            Assert.Equal(Cap, f.Fluid.Grid.AmountAt(voxel));
        }

        Assert.Equal(0, f.Fluid.Grid.AmountAt(new VoxelPos(30, 0, 30)));
        Assert.Equal(0, f.Fluid.Grid.AmountAt(new VoxelPos(31, 1, 31)));

        // The automaton has nothing to do: a full room is a full room.
        FluidSettleResult rest = f.Fluid.Settle(maxTicks: 1000);
        Assert.True(rest.Settled);
        Assert.True(f.Fluid.VerifyQuiescent());
        Assert.Equal(total, f.Fluid.TotalMillilitres);
        Assert.Equal(0, f.Fluid.DisplacedMillilitres);
    }

    [Fact]
    public void ARoofFallingIntoAHalfFloodedRoomRaisesThePoolRatherThanFillingTheCeiling()
    {
        Flooded f = Room(toY: 15);
        long total = f.Fluid.TotalMillilitres;

        foreach (ChunkPos bite in TwoByTwo)
        {
            f.Region.Settle();
            Assert.Equal(CollapseOutcome.Fell, Collapse.TryFall(f.Region, bite.Offset(0, 1, 0), f.Editor, f.World.Materials, Rubble, f.Fluid, out CollapseResult result));
            Assert.Equal(result.LiftedMillilitres, result.ReturnedMillilitres);
            Assert.Equal(0, result.DisplacedMillilitres);
        }

        Assert.Equal(20 * Cap, f.Fluid.LiftedMillilitres);
        Assert.Equal(total, f.Fluid.TotalMillilitres);

        // Returned bottom-up from the first cell above the rubble: the lifted water sits on the
        // pool's surface, at y = 16 over the landing columns, and nothing is in the ceiling.
        Assert.Equal(Cap, f.Fluid.Grid.AmountAt(new VoxelPos(30, 16, 30)));
        Assert.Equal(0, f.Fluid.Grid.AmountAt(new VoxelPos(30, 32, 30)));
        foreach (VoxelPos voxel in PlugVoxels())
        {
            Assert.Equal(0, f.Fluid.Grid.AmountAt(voxel));
        }

        // Then the automaton spreads the surface out; nothing goes up, nothing is lost.
        FluidSettleResult rest = f.Fluid.Settle(maxTicks: 5000);
        Assert.True(rest.Settled);
        Assert.Equal(total, f.Fluid.TotalMillilitres);
        Assert.Equal(0, f.Fluid.DisplacedMillilitres);
        foreach ((VoxelPos cell, int _) in f.Fluid.Grid.SurfaceCells())
        {
            Assert.True(cell.Y <= 17, $"water at {cell} after the rest");
        }
    }

    private sealed class PendingTerrain(IFluidTerrain inner, ChunkPos pending) : IFluidTerrain
    {
        public bool Pending { get; set; } = true;

        public bool CanHold(VoxelPos voxel) => !(Pending && voxel.Chunk == pending) && inner.CanHold(voxel);

        public bool IsLoaded(VoxelPos voxel) => !(Pending && voxel.Chunk == pending) && inner.IsLoaded(voxel);
    }

    [Fact]
    public void AFireWaitsWhileAChunkItWouldWriteIntoCannotHoldWaterYet()
    {
        (SupportTestWorld world, SupportRegion region) = Plug();
        var editor = new SupportTestWorld.Editor(world, region);
        var terrain = new PendingTerrain(new VoxelFluidTerrain(editor, world.Materials), TwoByTwo[0]);
        var fluid = new FluidSimulation(terrain);
        ChunkPos roof = TwoByTwo[0].Offset(0, 1, 0);

        // The landing chunk's stored water is still on the lane: nothing is written.
        Assert.Equal(CollapseOutcome.WaitingFluid, Collapse.TryFall(region, roof, editor, world.Materials, Rubble, fluid, out _));
        Assert.Equal(0, editor.Writes);
        Assert.True(region.IsSettled(roof));

        terrain.Pending = false;
        Assert.Equal(CollapseOutcome.Fell, Collapse.TryFall(region, roof, editor, world.Materials, Rubble, fluid, out CollapseResult result));
        Assert.Equal(5, result.Voxels);
    }

    [Fact]
    public void WithNoWaterAtAllAFallNeitherLiftsNorWaits()
    {
        (SupportTestWorld world, SupportRegion region) = Plug();
        var editor = new SupportTestWorld.Editor(world, region);
        ChunkPos roof = TwoByTwo[0].Offset(0, 1, 0);

        Assert.Equal(CollapseOutcome.Fell, Collapse.TryFall(region, roof, editor, world.Materials, Rubble, water: null, out CollapseResult result));
        Assert.Equal(0, result.LiftedMillilitres);
        Assert.Equal(0, result.ReturnedMillilitres);
        Assert.Equal(5, result.Voxels);
    }

    // ------------------------------------------------------------------ the timer

    [Fact]
    public void TheWindowShortensWithTheDeficitAndTheJitterIsExponentialAboutIt()
    {
        var window = new CollapseWindow { MinSeconds = 2, MaxSeconds = 60 };
        const float mean = CollapseWindow.MeanUnit;

        // At the mean unit the draw is the deficit's delay itself: the window's two numbers are
        // the mean's ends (M9d, decision 7).
        Assert.Equal(60.0, window.DelaySeconds(0, mean), 4);
        Assert.Equal(2.0, window.DelaySeconds(K, mean), 4);
        Assert.Equal(2.0, window.DelaySeconds(10 * K, mean), 4);
        Assert.Equal(31.0, window.DelaySeconds(K / 2, mean), 4);

        // The plug: deficit 8 has a mean near the maximum, and the same lip goes in five seconds
        // or three minutes — the median is 0.69 of the mean, a unit of 0.05 is one draw in twenty
        // under a twentieth of it, and 0.95 is one in twenty over three times it.
        double plug = window.DelaySeconds(8, mean);
        Assert.InRange(plug, 55.0, 57.0);
        Assert.Equal(plug * 0.693147, window.DelaySeconds(8, 0.5f), 2);
        Assert.InRange(window.DelaySeconds(8, 0.05f), plug * 0.05, plug * 0.052);
        Assert.InRange(window.DelaySeconds(8, 0.95f), plug * 2.99, plug * 3.0);
        Assert.Equal(0.0, window.DelaySeconds(8, 0f), 6);

        // Bounded above at the unit's cap, so a draw is never infinite; strictly increasing in
        // the unit, so the seed's order is the due's order.
        Assert.InRange(window.DelaySeconds(8, 1f), plug * 13.8, plug * 13.9);
        Assert.True(window.DelaySeconds(8, 0.3f) < window.DelaySeconds(8, 0.31f));

        // The sweep flag: zero everywhere is the instant arm.
        Assert.Equal(0.0, Instant.DelaySeconds(8, 0.7f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CollapseWindow { MinSeconds = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new CollapseWindow { MaxSeconds = double.NaN });
    }

    /// <summary>The widest a draw can be at a given deficit: the mean at the unit's cap.</summary>
    private static long WidestMillis(CollapseWindow window, int deficit) =>
        (long)Math.Ceiling(window.DelaySeconds(deficit, 1f) * 1000.0) + 1;

    [Fact]
    public void ASettledRoofArmsOnceIsSeededAndFiresWhenDue()
    {
        (SupportTestWorld world, SupportRegion region) = Plug();
        var editor = new SupportTestWorld.Editor(world, region);
        var scheduler = new CollapseScheduler(region, editor, world.Materials, Rubble, seed: 42);

        Assert.Empty(scheduler.Sweep(worldMillis: 10_000));
        Assert.Equal(4, scheduler.Armed);
        Assert.Equal(0, scheduler.Due);
        ChunkPos roof = TwoByTwo[0].Offset(0, 1, 0);
        Assert.True(scheduler.TryGetTimer(roof, out CollapseTimer timer));
        Assert.Equal(10_000, timer.ArmedAtMillis);
        Assert.Equal(8, timer.Deficit);
        Assert.InRange(timer.DueAtMillis - timer.ArmedAtMillis, 0, WidestMillis(CollapseWindow.Default, 8));

        // Seeded: the same seed, chunk and moment give the same due; a different seed does not
        // (over four chunks, at least one moves).
        var twin = new CollapseScheduler(Settled(world, TwoByTwo), editor, world.Materials, Rubble, seed: 42);
        twin.Sweep(10_000);
        Assert.True(twin.TryGetTimer(roof, out CollapseTimer twinTimer));
        Assert.Equal(timer, twinTimer);
        var other = new CollapseScheduler(Settled(world, TwoByTwo), editor, world.Materials, Rubble, seed: 43);
        other.Sweep(10_000);
        bool anyMoved = TwoByTwo.Any(bite =>
            other.TryGetTimer(bite.Offset(0, 1, 0), out CollapseTimer o)
            && scheduler.TryGetTimer(bite.Offset(0, 1, 0), out CollapseTimer s)
            && o.DueAtMillis != s.DueAtMillis);
        Assert.True(anyMoved);

        // A sweep before any is due leaves every timer alone.
        long earliest = TwoByTwo.Min(b => scheduler.TryGetTimer(b.Offset(0, 1, 0), out CollapseTimer t) ? t.DueAtMillis : long.MaxValue);
        scheduler.Sweep(earliest - 1);
        Assert.Equal(0, scheduler.Fired);
        Assert.True(scheduler.TryGetTimer(roof, out CollapseTimer same));
        Assert.Equal(timer, same);

        // Once every roof is due they fall one per boundary: the first fall is an edit that
        // unsettles the other three, which wait — counted — and fire once their derives land.
        long latest = TwoByTwo.Max(b => scheduler.TryGetTimer(b.Offset(0, 1, 0), out CollapseTimer t) ? t.DueAtMillis : 0);
        IReadOnlyList<CollapseFire> fires = scheduler.Sweep(latest);
        Assert.Single(fires);
        Assert.Equal(3, scheduler.Due);
        Assert.Equal(3, scheduler.WaitingUnsettled);
        Assert.False(fires[0].CascadeStage);
        for (int boundary = 1; boundary < 4; boundary++)
        {
            region.Settle();
            fires = scheduler.Sweep(latest + (boundary * 500));
            Assert.Single(fires);
            Assert.False(fires[0].CascadeStage);
        }

        Assert.Equal(4, scheduler.Fired);
        Assert.Equal(20, scheduler.VoxelsFallen);
        Assert.Equal(0, scheduler.Armed);

        // The re-settle finds nothing at risk, so nothing re-arms.
        region.Settle();
        scheduler.Sweep(latest + 2_000);
        Assert.Equal(0, scheduler.Armed);
        Assert.Equal(0, scheduler.Due);
    }

    [Fact]
    public void ATimerDisarmsWhenTheSetEmptiesAndSurvivesARederiveThatKeepsIt()
    {
        (SupportTestWorld world, SupportRegion region) = Plug();
        var editor = new SupportTestWorld.Editor(world, region);
        var scheduler = new CollapseScheduler(region, editor, world.Materials, Rubble, seed: 1);
        ChunkPos roof = TwoByTwo[0].Offset(0, 1, 0);
        scheduler.Sweep(0);
        Assert.True(scheduler.TryGetTimer(roof, out CollapseTimer armed));

        // A chip in the roof chunk's far corner: a re-derive that keeps the plug. Unsettled, the
        // sweep leaves the timer alone; settled again with the same set, so does the next.
        editor.SetVoxel(roof.OriginVoxel.Offset(0, 31, 0), Air);
        scheduler.Sweep(1_000);
        Assert.True(scheduler.TryGetTimer(roof, out CollapseTimer kept));
        Assert.Equal(armed, kept);
        region.Settle();
        scheduler.Sweep(2_000);
        Assert.True(scheduler.TryGetTimer(roof, out kept));
        Assert.Equal(armed, kept);

        // Shoring: a stone pillar under the plug's centre takes the deficit away, and the
        // timer with it — no fall, one disarm.
        for (int y = 0; y < 32; y++)
        {
            editor.SetVoxel(new VoxelPos(31, y, 31), Stone);
            editor.SetVoxel(new VoxelPos(32, y, 31), Stone);
            editor.SetVoxel(new VoxelPos(31, y, 32), Stone);
            editor.SetVoxel(new VoxelPos(32, y, 32), Stone);
        }

        region.Settle();
        Assert.Empty(region.VerifyFixpoint());
        Assert.Equal(0, region.SettledAtRiskVoxels);
        scheduler.Sweep(3_000);
        Assert.Equal(0, scheduler.Armed);
        Assert.Equal(4, scheduler.Disarmed);
        Assert.Equal(0, scheduler.Fired);
    }

    [Fact]
    public void ATimerDisarmsWhenItsChunkLeavesTheLoadedWorld()
    {
        (SupportTestWorld world, SupportRegion region) = Plug();
        var editor = new SupportTestWorld.Editor(world, region);
        var scheduler = new CollapseScheduler(region, editor, world.Materials, Rubble, seed: 1);
        scheduler.Sweep(0);
        Assert.Equal(4, scheduler.Armed);

        // One roof leaves: its field goes, and its timer with it at the next sweep — a settled
        // count of zero, read the only way it can be. The other three keep theirs.
        ChunkPos roof = TwoByTwo[0].Offset(0, 1, 0);
        world.Depart(roof);
        region.Departed(roof);
        scheduler.Sweep(1_000);
        Assert.False(scheduler.TryGetTimer(roof, out _));
        Assert.Equal(3, scheduler.Armed);
        Assert.Equal(1, scheduler.Disarmed);
        Assert.Equal(0, scheduler.Fired);
    }

    [Fact]
    public void AFallClearsTheTimersOfEveryChunkItFellFromAndLeavesTheOthers()
    {
        // The stack, with a seed under which the lower chunk is due first: it falls alone, its
        // timer goes, and the upper — a neighbour that did not fall — keeps the timer it had and
        // fires from it once settled, over the lower's rubble.
        (SupportTestWorld world, SupportRegion region, ChunkPos lower, ChunkPos upper) = Stack();
        var editor = new SupportTestWorld.Editor(world, region);
        var window = new CollapseWindow { MinSeconds = 2, MaxSeconds = 60 };
        CollapseScheduler scheduler = null!;
        CollapseTimer lowerTimer = default, upperTimer = default;
        for (ulong seed = 0; seed < 64; seed++)
        {
            scheduler = new CollapseScheduler(region, editor, world.Materials, Rubble, seed, window);
            scheduler.Sweep(0);
            Assert.True(scheduler.TryGetTimer(lower, out lowerTimer));
            Assert.True(scheduler.TryGetTimer(upper, out upperTimer));
            if (lowerTimer.DueAtMillis < upperTimer.DueAtMillis)
            {
                break;
            }
        }

        Assert.True(lowerTimer.DueAtMillis < upperTimer.DueAtMillis, "no seed in 64 put the lower chunk first");

        // Both unreached throughout — deficit K — so both drawn about the minimum.
        Assert.Equal(K, lowerTimer.Deficit);
        Assert.Equal(K, upperTimer.Deficit);
        Assert.InRange(lowerTimer.DueAtMillis, 0, WidestMillis(window, K));
        Assert.InRange(upperTimer.DueAtMillis, 0, WidestMillis(window, K));

        IReadOnlyList<CollapseFire> fires = scheduler.Sweep(lowerTimer.DueAtMillis);
        Assert.Single(fires);
        Assert.Equal(lower, fires[0].Result.Chunk);
        Assert.Equal([lower], fires[0].Result.FellFrom);
        Assert.False(scheduler.TryGetTimer(lower, out _));
        Assert.True(scheduler.TryGetTimer(upper, out CollapseTimer kept));
        Assert.Equal(upperTimer, kept);

        region.Settle();
        Assert.Equal(VoxelConstants.ChunkVolume, region.SettledAtRiskVoxels);
        fires = scheduler.Sweep(upperTimer.DueAtMillis);
        Assert.Single(fires);
        Assert.Equal(upper, fires[0].Result.Chunk);
        Assert.False(fires[0].CascadeStage);
        Assert.Equal(0, scheduler.Armed);
        region.Settle();
        Assert.Equal(0, region.SettledAtRiskVoxels);
    }

    [Fact]
    public void ADirtRoofArmsAtTheMinimumAndCascadesAsTheChunkAboveJoinsTheAnalysedSet()
    {
        // A bite under a chunk of dirt: the wedge's core is unreached — dirt at cost 15 is dead
        // at d = 8 — so the deficit is K and the timer arms at the minimum, and the whole wedge,
        // 3,152 voxels, is one fall: every held voxel above it has a sideways path through held
        // voxels (the field is 120 − 15(d + 1) + 4k, and the column at d = 7 is held all the way
        // up). The cascade comes from decision 3, with no code of its own: the fall marks the
        // roof dug, the stone chunk above joins the analysed set, its own field is 56 at the
        // centre rather than the anchor's 120, and the centre voxels the top face held at
        // exactly 0 come loose — eight voxels, a second stage re-armed from its own deficit.
        var world = new SupportTestWorld();
        world.FillBox(new ChunkPos(-2, -2, -2), new ChunkPos(2, 2, 2), Stone);
        var bite = new ChunkPos(0, 0, 0);
        var roof = new ChunkPos(0, 1, 0);
        world.Fill(bite, Air);
        world.Fill(roof, SupportTestWorld.Dirt);
        SupportRegion region = Settled(world, [bite]);
        Assert.Equal(3_152, region.SettledAtRiskVoxels);
        Assert.Equal(K, region.SettledField(roof)!.MaxDeficit);
        Assert.False(region.IsAnalysed(roof.Offset(0, 1, 0)));
        var editor = new SupportTestWorld.Editor(world, region);
        var window = new CollapseWindow { MinSeconds = 2, MaxSeconds = 60 };
        var scheduler = new CollapseScheduler(region, editor, world.Materials, Rubble, seed: 11, window);
        (long solid, long sumY) = world.Census();

        var stages = new List<int>();
        long now = 0;
        long lastFire = long.MinValue;
        for (int round = 0; round < 10; round++)
        {
            scheduler.Sweep(now);
            if (!scheduler.TryGetTimer(roof, out CollapseTimer timer))
            {
                break;
            }

            Assert.Equal(now, timer.ArmedAtMillis);
            if (stages.Count == 0)
            {
                Assert.Equal(K, timer.Deficit);
                Assert.InRange(timer.DueAtMillis - now, 0, WidestMillis(window, K));
            }

            now = timer.DueAtMillis;
            IReadOnlyList<CollapseFire> fires = scheduler.Sweep(now);
            Assert.Single(fires);
            Assert.Equal(roof, fires[0].Result.Chunk);
            Assert.Equal(lastFire != long.MinValue && now - lastFire <= 60_000, fires[0].CascadeStage);
            lastFire = now;
            stages.Add(fires[0].Result.Voxels);
            Assert.False(scheduler.TryGetTimer(roof, out _));

            // A sweep before the settle: the roof is unsettled and its snapshot is the last
            // settle's, still marking what just fell — nothing arms from it.
            scheduler.Sweep(now + 1);
            Assert.False(scheduler.TryGetTimer(roof, out _));

            (long solidNow, long sumNow) = world.Census();
            Assert.Equal(solid, solidNow);
            Assert.True(sumNow < sumY);
            sumY = sumNow;

            region.Settle();
            Assert.Empty(region.VerifyFixpoint());
            now += 500;
        }

        Assert.Equal([3_152, 8], stages);
        Assert.True(region.IsAnalysed(roof.Offset(0, 1, 0)));
        Assert.Equal(0, region.SettledAtRiskVoxels);
        Assert.Equal(0, scheduler.Armed);
        Assert.Equal(3_160, world.Count(Rubble));
    }

    [Fact]
    public void RubbleRestingOnAnUnloadedChunkFallsAgainWhenThatChunkLoads()
    {
        // The plan's recorded-not-built case: a stone chunk in mid-air over a chunk that is not
        // loaded lands its rubble on that chunk as on rock. When the chunk loads — air — the
        // rubble's chunk is dug and analysed, the rubble hangs over air, the timer that was
        // cleared at the fall re-arms from the new deficit (K, the minimum), and it falls again.
        var world = new SupportTestWorld();
        world.FillBox(new ChunkPos(-1, -1, -1), new ChunkPos(1, 3, 1), Air);
        world.FillBox(new ChunkPos(-1, -2, -1), new ChunkPos(1, -2, 1), Stone);
        var block = new ChunkPos(0, 2, 0);
        var hidden = new ChunkPos(0, 0, 0);
        world.Fill(block, Stone);
        world.Depart(hidden);
        SupportRegion region = Settled(world, [block]);
        var editor = new SupportTestWorld.Editor(world, region);
        var window = new CollapseWindow { MinSeconds = 2, MaxSeconds = 60 };
        var scheduler = new CollapseScheduler(region, editor, world.Materials, Rubble, seed: 3, window);

        scheduler.Sweep(0);
        Assert.True(scheduler.TryGetTimer(block, out CollapseTimer first));
        IReadOnlyList<CollapseFire> fires = scheduler.Sweep(first.DueAtMillis);
        Assert.Single(fires);
        Assert.Equal(32, fires[0].Result.DeepestDrop);
        Assert.Equal(Rubble, world.Material(new VoxelPos(9, 32, 9)));
        Assert.Equal(Rubble, world.Material(new VoxelPos(9, 63, 9)));
        Assert.False(scheduler.TryGetTimer(block, out _));

        region.Settle();
        Assert.Equal(0, region.SettledAtRiskVoxels);
        long now = first.DueAtMillis + 500;
        scheduler.Sweep(now);
        Assert.Equal(0, scheduler.Armed);

        // The chunk arrives, air: the rubble chunk is its neighbour and re-derives against it.
        world.Return(hidden);
        region.Arrived(hidden);
        region.Settle();
        Assert.Empty(region.VerifyFixpoint());
        Assert.Equal(VoxelConstants.ChunkVolume, region.SettledAtRiskVoxels);

        scheduler.Sweep(now);
        ChunkPos rubble = new ChunkPos(0, 1, 0);
        Assert.True(scheduler.TryGetTimer(rubble, out CollapseTimer second));
        Assert.Equal(now, second.ArmedAtMillis);
        Assert.Equal(K, second.Deficit);
        Assert.InRange(second.DueAtMillis - now, 0, WidestMillis(window, K));

        fires = scheduler.Sweep(second.DueAtMillis);
        Assert.Single(fires);
        Assert.Equal(rubble, fires[0].Result.Chunk);
        Assert.Equal(64, fires[0].Result.DeepestDrop);
        Assert.Equal(Rubble, world.Material(new VoxelPos(9, -32, 9)));
        Assert.Equal(Air, world.Material(new VoxelPos(9, 32, 9)));
        region.Settle();
        Assert.Equal(0, region.SettledAtRiskVoxels);
        Assert.Equal(VoxelConstants.ChunkVolume, world.Count(Rubble));
    }

    [Fact]
    public void WithFiringOffTheSweepArmsCountsDueAndNeverFalls()
    {
        (SupportTestWorld world, SupportRegion region) = Plug();
        var editor = new SupportTestWorld.Editor(world, region);
        var scheduler = new CollapseScheduler(region, editor, world.Materials, Rubble, seed: 9, Instant) { FireEnabled = false };

        scheduler.Sweep(0);
        scheduler.Sweep(1_000_000);

        Assert.Equal(4, scheduler.Armed);
        Assert.Equal(4, scheduler.Due);
        Assert.Equal(0, scheduler.Fired);
        Assert.Equal(0, editor.Writes);
        Assert.Equal(20, region.SettledAtRiskVoxels);
    }

    [Fact]
    public void ADueChunkWaitsUnsettledAndIsCountedAsSuch()
    {
        (SupportTestWorld world, SupportRegion region) = Plug();
        var editor = new SupportTestWorld.Editor(world, region);
        var scheduler = new CollapseScheduler(region, editor, world.Materials, Rubble, seed: 9, new CollapseWindow { MinSeconds = 1, MaxSeconds = 1 });
        scheduler.Sweep(0);
        Assert.Equal(4, scheduler.Armed);

        // A chip in one roof chunk queues it and its six, so every roof is unsettled — one is
        // pending, the others have a pending neighbour — when the timers come due.
        editor.SetVoxel(TwoByTwo[0].Offset(0, 1, 0).OriginVoxel.Offset(0, 31, 0), Air);
        scheduler.Sweep(2_000);
        Assert.Equal(0, scheduler.Fired);
        Assert.Equal(4, scheduler.Due);
        Assert.Equal(4, scheduler.WaitingUnsettled);
        Assert.Equal(0, scheduler.WaitingFluid);
        Assert.Equal(4, scheduler.Armed);

        region.Settle();
        scheduler.Sweep(2_000);
        Assert.Equal(1, scheduler.Fired);
        Assert.Equal(3, scheduler.WaitingUnsettled);
        for (int boundary = 1; boundary < 4; boundary++)
        {
            region.Settle();
            scheduler.Sweep(2_000 + (boundary * 500));
        }

        Assert.Equal(4, scheduler.Fired);
        Assert.Equal(0, scheduler.Due);
        Assert.Equal(0, scheduler.WaitingUnsettled);
    }

    // ------------------------------------------------------------------ the content

    [Fact]
    public void RubbleIsShippedAsPlannedAndIsNotReplaceable()
    {
        var content = new GameContent();
        LoadResult result = content.Load(new DirectoryContentSource(ShippedContentTests.ModsDirectory()));
        Assert.True(result.Success, string.Join("; ", result.Errors));

        int id = content.Materials.GetNumericId(Identifier.Parse("overburden:rubble"));
        MaterialDefinition rubble = content.Materials.Get(id);
        Assert.Equal(0.5f, rubble.Hardness);
        Assert.Equal(2, rubble.SupportDistance);
        Assert.Empty(rubble.SupportUnderHead);
        Assert.Equal(0.6f, rubble.Permeability);
        Assert.Equal(4, rubble.TextureLayer);
        Assert.False(rubble.IsReplaceable);
        Assert.False(rubble.IsFluid);
        Assert.True(rubble.IsOpaque);

        MaterialTable table = MaterialTable.From(content.Materials);
        Assert.True(table.IsSolid(id));
        Assert.Equal(2, table.SupportDistance(id));
    }

    [Fact]
    public void AnOldWorldsTableGainsRubbleAsTheNextIdAtOpen()
    {
        // The played world's real v3 header, captured before rubble existed: its table is
        // seeded first, the mods load against it, and rubble takes the next number — the path
        // non-negotiable 2 designed and the lamp never exercised.
        byte[] header = Fixture("played-world-v3-header.bin");
        IReadOnlyList<Identifier> saved = Persistence.WorldHeaderBlob.Read(header).MaterialIdTable;
        Assert.DoesNotContain(Identifier.Parse("overburden:rubble"), saved);

        var content = new GameContent();
        LoadResult result = content.Load(new DirectoryContentSource(ShippedContentTests.ModsDirectory()), saved);
        Assert.True(result.Success, string.Join("; ", result.Errors));

        // Everything shipped since that header was written is appended after it, in the
        // directory's load order (M10b's computer and M10a-2's relay pair sort before rubble),
        // and the saved prefix is untouched — which is the whole of non-negotiable 2: an old
        // world's numbers never move.
        IReadOnlyList<Identifier> exported = content.Materials.ExportIdTable();
        Assert.Equal(saved, exported.Take(saved.Count));
        Identifier[] appended = [.. exported.Skip(saved.Count)];
        Assert.Equal(7, appended.Length);
        Assert.Contains(Identifier.Parse("overburden:rubble"), appended);
        Assert.Contains(Identifier.Parse("overburden:computer"), appended);
        Assert.Contains(Identifier.Parse("overburden:solar_panel"), appended);
        Assert.Contains(Identifier.Parse("overburden:vacuum_cell"), appended);
        Assert.Contains(Identifier.Parse("overburden:lead_acid_pack"), appended);
        Assert.Contains(Identifier.Parse("overburden:relay_pump"), appended);
        Assert.Contains(Identifier.Parse("overburden:relay_outlet"), appended);
        Assert.True(content.Materials.GetNumericId(Identifier.Parse("overburden:rubble")) >= saved.Count);
        Assert.Equal(exported.Count, exported.Distinct().Count());
    }

    private static byte[] Fixture(string name)
    {
        using Stream stream = typeof(CollapseTests).Assembly.GetManifestResourceStream("Overburden.Core.Tests.Fixtures." + name)
            ?? throw new InvalidOperationException("fixture missing: " + name);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
