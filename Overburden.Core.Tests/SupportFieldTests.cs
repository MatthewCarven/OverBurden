using Overburden.Core.Content;
using Overburden.Core.Structure;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// M9a: the support field — the rule, the derive, the join and the oracle.
/// </summary>
/// <remarks>
/// The oracle throughout is <see cref="SupportRegion.VerifyFixpoint"/>: every solid voxel equals
/// the rule applied to its real neighbours, and the fixpoint is unique, so a region that passes it
/// holds <em>the</em> field. The rooms M9-PLAN's table derives by hand are asserted to the voxel,
/// because the table is arithmetic and the code has to agree with it before either is believed.
/// </remarks>
public class SupportFieldTests
{
    private const int Stone = SupportTestWorld.Stone;
    private const int Dirt = SupportTestWorld.Dirt;
    private const int Air = SupportTestWorld.Air;
    private const int K = SupportRule.MaxBudget;

    /// <summary>A block of stone chunks, inclusive, with a bite (or several) taken out.</summary>
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

    private static SupportRegion Settled(SupportTestWorld world, IEnumerable<ChunkPos> dug, SupportRule? rule = null, Fluid.IWaterTable? table = null)
    {
        SupportRegion region = world.NewRegion(rule, table);
        foreach (ChunkPos chunk in dug)
        {
            region.MarkDug(chunk);
        }

        region.Settle();
        Assert.Empty(region.VerifyFixpoint());
        return region;
    }

    [Fact]
    public void ASingleBiteInStoneHasNothingAtRisk()
    {
        // The plan's first row: an 8 m roof is within 4 m of a wall everywhere, and stone carries
        // 30 voxels. The roof chunk's side faces border unanalysed stone, which anchors.
        var bite = new ChunkPos(0, 0, 0);
        SupportTestWorld world = StoneBlock(new ChunkPos(-2, -2, -2), new ChunkPos(2, 2, 2), bite);

        SupportRegion region = Settled(world, [bite]);

        Assert.Equal(7, region.Analysed.Count);
        Assert.Equal(0, region.AtRiskVoxels);
        Assert.Equal(ChunkSupport.NotSolid, region.FieldAt(bite)!.UniformValue);

        // The wall chunks rest on unanalysed rock and are full throughout — the closed form.
        ChunkSupport wall = region.FieldAt(new ChunkPos(1, 0, 0))!;
        Assert.True(wall.IsUniform);
        Assert.Equal(ChunkSupport.Full, wall.UniformValue);

        // The roof is dense: its underside spends toward the middle and recovers upward.
        ChunkSupport roof = region.FieldAt(new ChunkPos(0, 1, 0))!;
        Assert.False(roof.IsUniform);
        Assert.Equal(K - (4 * 16), roof.Budget(VoxelPos.Index(15, 0, 15)));
        Assert.Equal(K - 4, roof.Budget(VoxelPos.Index(0, 0, 15)));
        Assert.Equal(K, roof.Budget(VoxelPos.Index(15, 31, 15)));
    }

    [Fact]
    public void ATwoByTwoBiteInStoneShedsATwentyVoxelPlug()
    {
        // The plan's headline row, to the voxel: at the underside a path runs to the nearest of the
        // roof's four anchored sides, 120 − 4·d, negative from d = 31 — x and z in 30..33 — and
        // the layer resting on it is one refund better, negative only at 31..32. 16 + 4.
        ChunkPos[] bites = [new(0, 0, 0), new(1, 0, 0), new(0, 0, 1), new(1, 0, 1)];
        SupportTestWorld world = StoneBlock(new ChunkPos(-2, -2, -2), new ChunkPos(3, 2, 3), bites);

        SupportRegion region = Settled(world, bites);

        Assert.Equal(20, region.AtRiskVoxels);
        Assert.Equal(4, region.AtRiskChunks);

        var atRisk = new HashSet<VoxelPos>();
        foreach (ChunkPos chunk in region.Analysed)
        {
            ChunkSupport? field = region.FieldAt(chunk);
            if (field is null || field.AtRiskCount == 0)
            {
                continue;
            }

            for (int index = 0; index < VoxelConstants.ChunkVolume; index++)
            {
                if (field.IsAtRisk(index))
                {
                    (int x, int y, int z) = VoxelPos.FromIndex(index);
                    atRisk.Add(VoxelPos.FromChunkLocal(chunk, x, y, z));
                }
            }
        }

        var expected = new HashSet<VoxelPos>();
        for (int x = 30; x <= 33; x++)
        for (int z = 30; z <= 33; z++)
        {
            expected.Add(new VoxelPos(x, 32, z));
        }

        for (int x = 31; x <= 32; x++)
        for (int z = 31; z <= 32; z++)
        {
            expected.Add(new VoxelPos(x, 33, z));
        }

        Assert.Equal(expected, atRisk);

        // The centre's deficit: −8 at the underside, −4 above it, and the plug is what decision 8
        // scales the delay by.
        ChunkSupport corner = region.FieldAt(new ChunkPos(0, 1, 0))!;
        Assert.Equal(-8, corner.Budget(VoxelPos.Index(31, 0, 31)));
        Assert.Equal(-4, corner.Budget(VoxelPos.Index(31, 1, 31)));
        Assert.Equal(0, corner.Budget(VoxelPos.Index(31, 2, 31)));
        Assert.Equal(8, corner.MaxDeficit);
    }

    /// <summary>The plug fixture with a brush-cell column (4 × 4 voxels) from the floor up to <paramref name="topY"/>, inclusive.</summary>
    private static SupportTestWorld PluggedRoomWithPillar(int material, int topY, out ChunkPos[] bites)
    {
        bites = [new(0, 0, 0), new(1, 0, 0), new(0, 0, 1), new(1, 0, 1)];
        SupportTestWorld world = StoneBlock(new ChunkPos(-2, -2, -2), new ChunkPos(3, 2, 3), bites);
        world.SetBox(new VoxelPos(30, 0, 30), new VoxelPos(33, topY, 33), material);
        return world;
    }

    [Fact]
    public void AStonePillarFromFloorToRoofShoresTheTwoByTwoPlug()
    {
        // M10d-1, decision 5: the pillar verdict's oracle before the play. The floor is the top of an
        // analysed chunk at 120; a column standing on it rests to 120 at its top (Rest is
        // min(K, below + refund)); the roof voxel touching it rests to 120 and the plug — 30..33,
        // exactly the column's footprint — is held outright, with the rest of the roof within
        // 30 voxels sideways of it. The room's floor is y = −1 and its roof y = 32, so the column
        // is y 0..31.
        SupportTestWorld world = PluggedRoomWithPillar(Stone, topY: 31, out ChunkPos[] bites);

        SupportRegion region = Settled(world, bites);

        Assert.Equal(0, region.AtRiskVoxels);
        ChunkSupport corner = region.FieldAt(new ChunkPos(0, 1, 0))!;
        Assert.Equal(K, corner.Budget(VoxelPos.Index(31, 0, 31)));
    }

    [Fact]
    public void APillarOneVoxelShortOfTheRoofShoresNothing()
    {
        // The gap is under the roof, so the column stands at 120 to its own top and the roof is
        // exactly the plug fixture's: one voxel of contact is the whole rule, and a pillar that
        // does not reach reads as no pillar at all. (A gap at the floor is a different fixture —
        // the column then hangs from the plug and adds its own voxels to the at-risk count.)
        SupportTestWorld world = PluggedRoomWithPillar(Stone, topY: 30, out ChunkPos[] bites);

        SupportRegion region = Settled(world, bites);

        Assert.Equal(20, region.AtRiskVoxels);
        Assert.Equal(-8, region.FieldAt(new ChunkPos(0, 1, 0))!.Budget(VoxelPos.Index(31, 0, 31)));
    }

    [Fact]
    public void ADirtPillarShoresExactlyAsStoneDoes()
    {
        // The stated cost of the uniqueness proof: the refund is a world constant, so a climb
        // through dirt (carries 8) rests to 120 exactly as a climb through stone (carries 30)
        // does — the material's distance is spent sideways and hanging, never resting. If the
        // play says a soft pillar holding a roof reads wrong, the lever is a per-material cap on
        // the climb, never a per-material refund (which lets dirt-on-stone pump itself full).
        SupportTestWorld world = PluggedRoomWithPillar(Dirt, topY: 31, out ChunkPos[] bites);

        SupportRegion region = Settled(world, bites);

        Assert.Equal(0, region.AtRiskVoxels);
        Assert.Equal(K, region.FieldAt(new ChunkPos(0, 0, 0))!.Budget(VoxelPos.Index(31, 31, 31)));
    }

    [Fact]
    public void AGalleryEightMetresWideNeverFallsInStoneHoweverLong()
    {
        // A path runs to the nearest wall: four bites in a row are a roof 32 voxels across the
        // short way, 16 from a wall at worst, and 120 − 64 holds.
        ChunkPos[] bites = [new(0, 0, 0), new(1, 0, 0), new(2, 0, 0), new(3, 0, 0)];
        SupportTestWorld world = StoneBlock(new ChunkPos(-2, -2, -2), new ChunkPos(5, 2, 2), bites);

        SupportRegion region = Settled(world, bites);

        Assert.Equal(0, region.AtRiskVoxels);
    }

    [Fact]
    public void AChunkFilledInMidAirIsAtRiskThroughoutAndAtTheFloor()
    {
        // Decision 6's worst case: 32,768 voxels with no anchor. Every neighbour is analysed air,
        // so every face reads not-solid and nothing enters; the chunk is unreached throughout.
        // The first build had the floor as a source at every solid voxel and this held its top
        // two layers off its own floor — 30,720 — which is the finding behind Unreached.
        var world = new SupportTestWorld();
        world.FillBox(new ChunkPos(-1, -1, -1), new ChunkPos(1, 1, 1), Air);
        var filled = new ChunkPos(0, 0, 0);
        world.Fill(filled, Stone);

        SupportRegion region = Settled(world, [filled]);

        ChunkSupport field = region.FieldAt(filled)!;
        Assert.Equal(VoxelConstants.ChunkVolume, region.AtRiskVoxels);
        Assert.Equal(K, field.MaxDeficit);
        Assert.All(field.Snapshot(), value => Assert.Equal(ChunkSupport.Unreached, value));
    }

    [Fact]
    public void AnUnanalysedNeighbourAnchorsThroughItsSolidBorderOnly()
    {
        // The M9a pass's item 4. A floating stone chunk beside an unanalysed chunk: stone there
        // anchors the shared face and the voxels nearest it hold; air there anchors nothing and
        // the whole chunk is at the floor. The unanalysed chunk is two across, so the analysed
        // neighbour between them carries the field under test.
        foreach ((int beyond, bool anchored) in new[] { (Stone, true), (Air, false) })
        {
            var world = new SupportTestWorld();
            world.FillBox(new ChunkPos(-2, -1, -1), new ChunkPos(3, 1, 1), Air);
            var dug = new ChunkPos(0, 0, 0);
            var carrier = new ChunkPos(1, 0, 0);
            world.Fill(carrier, Stone);
            world.Fill(new ChunkPos(2, 0, 0), beyond);

            SupportRegion region = Settled(world, [dug]);

            Assert.False(region.IsAnalysed(new ChunkPos(2, 0, 0)));
            ChunkSupport field = region.FieldAt(carrier)!;
            if (anchored)
            {
                Assert.Equal(K - 4, field.Budget(VoxelPos.Index(31, 0, 0)));
                Assert.True(field.AtRiskCount > 0 && field.AtRiskCount < VoxelConstants.ChunkVolume);
            }
            else
            {
                Assert.Equal(VoxelConstants.ChunkVolume, field.AtRiskCount);
            }
        }
    }

    [Fact]
    public void ADirtVoxelRestingOnAStoneVoxelInMidAirFallsWithIt()
    {
        // The pair the uniqueness argument names. With a per-material refund the dirt would refund
        // 15 going up and cost 8 coming down, pumping the pair to full; here nothing reaches
        // either and both are unreached — and a reached pair (below) at the end of a run of dirt
        // is at the floor, both of them, because the floor is not support.
        var world = new SupportTestWorld();
        world.FillBox(new ChunkPos(-1, -1, -1), new ChunkPos(1, 1, 1), Air);
        var chunk = new ChunkPos(0, 0, 0);
        world.Set(new VoxelPos(10, 10, 10), Stone);
        world.Set(new VoxelPos(10, 11, 10), Dirt);

        SupportRegion region = Settled(world, [chunk]);

        ChunkSupport field = region.FieldAt(chunk)!;
        Assert.Equal(ChunkSupport.UnreachedBudget, field.Budget(VoxelPos.Index(10, 10, 10)));
        Assert.Equal(ChunkSupport.UnreachedBudget, field.Budget(VoxelPos.Index(10, 11, 10)));
        Assert.Equal(2, region.AtRiskVoxels);

        // Reached through a run of dirt along the bottom of the chunk from a stone chunk whose far
        // face is unloaded and anchors it: the stone's near face is 120 − 4·32 = −8 at the bottom
        // row (the arch above it is what the first version of this fixture forgot), dirt spends
        // 15 a step, so the run reads −23, −38, … −113 and the path dies at its eighth voxel;
        // from there on nothing live arrives, and the stone at its end and the dirt resting on
        // that are unreached.
        var reachedWorld = new SupportTestWorld();
        reachedWorld.FillBox(new ChunkPos(-1, -1, -1), new ChunkPos(1, 1, 1), Air);
        reachedWorld.Fill(new ChunkPos(-1, 0, 0), Stone);
        reachedWorld.SetBox(new VoxelPos(0, 0, 10), new VoxelPos(9, 0, 10), Dirt);
        reachedWorld.Set(new VoxelPos(10, 0, 10), Stone);
        reachedWorld.Set(new VoxelPos(10, 1, 10), Dirt);
        SupportRegion reached = Settled(reachedWorld, [chunk]);
        ChunkSupport run = reached.FieldAt(chunk)!;
        Assert.Equal(-8, reached.FieldAt(new ChunkPos(-1, 0, 0))!.Budget(VoxelPos.Index(31, 0, 10)));
        Assert.Equal(-23, run.Budget(VoxelPos.Index(0, 0, 10)));
        Assert.Equal(-113, run.Budget(VoxelPos.Index(6, 0, 10)));
        Assert.Equal(ChunkSupport.UnreachedBudget, run.Budget(VoxelPos.Index(7, 0, 10)));
        Assert.Equal(ChunkSupport.UnreachedBudget, run.Budget(VoxelPos.Index(10, 0, 10)));
        Assert.Equal(ChunkSupport.UnreachedBudget, run.Budget(VoxelPos.Index(10, 1, 10)));
        Assert.Equal(12, run.AtRiskCount);
    }

    [Fact]
    public void AFloatingColumnAcrossAChunkFaceConvergesToTheFloor()
    {
        // Decision 4's pathological case: a body straddling a horizontal face, each half deriving
        // against the other's last layer. Each round one half loses a refund off the other's
        // previous layer, so it converges in at most 2 · 2K / r rounds — the plan's "≤ 30" counted
        // one half — and the fixpoint it reaches is unreached throughout: nothing supports the
        // column, and no round order can make it stand. The build's second version settled this
        // at the floor instead, each half's floor a source to the other's.
        var world = new SupportTestWorld();
        world.FillBox(new ChunkPos(-1, -1, -1), new ChunkPos(1, 2, 1), Air);
        world.SetBox(new VoxelPos(5, 0, 5), new VoxelPos(5, 63, 5), Stone);
        ChunkPos[] dug = [new(0, 0, 0), new(0, 1, 0)];

        SupportRegion region = world.NewRegion();
        foreach (ChunkPos chunk in dug)
        {
            region.MarkDug(chunk);
        }

        int rounds = region.Settle();

        Assert.Empty(region.VerifyFixpoint());
        Assert.InRange(rounds, 1, (2 * (2 * K / SupportRule.Default.Refund)) + 4);
        Assert.Equal(64, region.AtRiskVoxels);
        Assert.Equal(ChunkSupport.UnreachedBudget, region.FieldAt(new ChunkPos(0, 0, 0))!.Budget(VoxelPos.Index(5, 0, 5)));
        Assert.Equal(ChunkSupport.UnreachedBudget, region.FieldAt(new ChunkPos(0, 1, 0))!.Budget(VoxelPos.Index(5, 31, 5)));

        // And the same column hanging from an unloaded chunk above it, which anchors: reached
        // from the top, spending a hang a voxel, 112 at the top and the path dead 30 voxels
        // down, everything under that unreached — every voxel below the fifteenth at risk, and
        // the bottom is not held by resting on anything. (A loaded stone chunk above would not
        // do: analysed, it hangs from its own top and its bottom layer is dead too.)
        var ceiling = new SupportTestWorld();
        ceiling.FillBox(new ChunkPos(-1, -1, -1), new ChunkPos(1, 1, 1), Air);
        ceiling.SetBox(new VoxelPos(5, 0, 5), new VoxelPos(5, 63, 5), Stone);
        SupportRegion hanging = ceiling.NewRegion();
        foreach (ChunkPos chunk in dug)
        {
            hanging.MarkDug(chunk);
        }

        hanging.Settle();
        Assert.Empty(hanging.VerifyFixpoint());
        ChunkSupport upper = hanging.FieldAt(new ChunkPos(0, 1, 0))!;
        Assert.Equal(K - 8, upper.Budget(VoxelPos.Index(5, 31, 5)));
        Assert.Equal(0, upper.Budget(VoxelPos.Index(5, 17, 5)));
        Assert.Equal(-8, upper.Budget(VoxelPos.Index(5, 16, 5)));
        Assert.Equal(64 - 15, hanging.AtRiskVoxels);
        Assert.Equal(-112, upper.Budget(VoxelPos.Index(5, 3, 5)));
        Assert.Equal(ChunkSupport.UnreachedBudget, upper.Budget(VoxelPos.Index(5, 2, 5)));
        Assert.Equal(ChunkSupport.UnreachedBudget, hanging.FieldAt(new ChunkPos(0, 0, 0))!.Budget(VoxelPos.Index(5, 0, 5)));
    }

    [Fact]
    public void TheClosedFormAgreesWithTheSearchOnAFullChunk()
    {
        // Two derivations of one field are two that can drift — the IsCaveAt / GenerateTerrain
        // shape. The one input both apply to is an all-solid chunk on a full bottom face.
        var world = new SupportTestWorld();
        var padded = new PaddedChunk();
        padded.Fill(Stone);
        var faces = new SupportFaces();
        faces.AnchorAll();
        var derive = new SupportDerive();
        SupportCostTable costs = SupportCostTable.Dry(world.Materials, SupportRule.Default);

        ChunkSupport closed = derive.Derive(padded, world.Materials, SupportRule.Default, costs, faces, out SupportDeriveResult trivial, closedForm: true);
        ChunkSupport searched = derive.Derive(padded, world.Materials, SupportRule.Default, costs, faces, out SupportDeriveResult general, closedForm: false);

        Assert.True(trivial.Trivial);
        Assert.False(general.Trivial);
        Assert.True(closed.IsUniform);
        Assert.False(searched.IsUniform);
        Assert.Equal(closed.Snapshot(), searched.Snapshot());
        Assert.Equal(VoxelConstants.ChunkVolume, general.Pops);

        // And the closed form does not fire when the bottom is not full: one air voxel below.
        padded[7, -1, 7] = Air;
        ChunkSupport notClosed = derive.Derive(padded, world.Materials, SupportRule.Default, costs, faces, out SupportDeriveResult result, closedForm: true);
        Assert.False(result.Trivial);
        Assert.Equal(K - SupportRule.Default.Cost(30), notClosed.Budget(VoxelPos.Index(7, 0, 7)));
    }

    [Fact]
    public void ALakeOverTheRoofShedsMoreThanTheDryRoofDoes()
    {
        // Decision 5 in the field: the same 2 × 2 room, dry and under water, the stone's curve
        // reading 24 at 2 m of head and 12 at 10. The roof's underside is at Y 32; a table at Y 72
        // puts 10 m over it, so the underside spends 10 a step and the plug is a pyramid.
        ChunkPos[] bites = [new(0, 0, 0), new(1, 0, 0), new(0, 0, 1), new(1, 0, 1)];
        SupportTestWorld world = StoneBlock(new ChunkPos(-2, -2, -2), new ChunkPos(3, 2, 3), bites);

        SupportRegion dry = Settled(world, bites);
        SupportRegion wet = Settled(world, bites, table: new SupportTestWorld.FlatTable(72));
        SupportRegion off = Settled(world, bites, table: null);

        Assert.Equal(20, dry.AtRiskVoxels);
        Assert.Equal(20, off.AtRiskVoxels);
        Assert.True(wet.AtRiskVoxels > 20 * 50, $"wet {wet.AtRiskVoxels}");

        // A voxel of the roof one step in from an anchored side, under 10 m: 120 − round(120/12).
        ChunkSupport roof = wet.FieldAt(new ChunkPos(0, 1, 0))!;
        Assert.Equal(K - 10, roof.Budget(VoxelPos.Index(0, 0, 15)));

        // And a table below the roof is dry for it: the head is what stands over the voxel.
        SupportRegion under = Settled(world, bites, table: new SupportTestWorld.FlatTable(20));
        Assert.Equal(20, under.AtRiskVoxels);
    }

    [Fact]
    public void TheRuleWithAFasterRefundArchesFlatter()
    {
        // --restrefund's sweep: refund per 15 voxels rather than 30 is 8 a step up, and the plug's
        // second layer recovers to zero, so only the underside's sixteen fall.
        ChunkPos[] bites = [new(0, 0, 0), new(1, 0, 0), new(0, 0, 1), new(1, 0, 1)];
        SupportTestWorld world = StoneBlock(new ChunkPos(-2, -2, -2), new ChunkPos(3, 2, 3), bites);

        SupportRegion region = Settled(world, bites, new SupportRule { RestRefundVoxels = 15 });

        Assert.Equal(16, region.AtRiskVoxels);
    }

    [Fact]
    public void ADistanceScaleBelowOneShedsMore()
    {
        // --supportscale: halve every distance and stone spends 8 a step, so a single 8 m bite —
        // nothing at risk as written — sheds its middle.
        var bite = new ChunkPos(0, 0, 0);
        SupportTestWorld world = StoneBlock(new ChunkPos(-2, -2, -2), new ChunkPos(2, 2, 2), bite);

        SupportRegion region = Settled(world, [bite], new SupportRule { DistanceScale = 0.5f });

        Assert.True(region.AtRiskVoxels > 0);
        Assert.Equal(K - 8, region.FieldAt(new ChunkPos(0, 1, 0))!.Budget(VoxelPos.Index(0, 0, 15)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void IncrementalEqualsFromScratchUnderEditsArrivalsAndDepartures(int seedIndex)
    {
        // The join's oracle, as M9-PLAN names it: edits land in analysed chunks and only they and
        // their neighbours re-derive; chunks leave and come back; the fields must equal a region
        // built from nothing over the same world in a different order, and both must satisfy the
        // fixpoint. Order independence is uniqueness, so a fresh region marks the dug set backwards.
        var random = new Random(0x4D39 + seedIndex);
        var world = new SupportTestWorld();
        var min = new ChunkPos(-1, -2, -1);
        var max = new ChunkPos(1, 1, 1);
        world.FillBox(min, max, Stone);

        // Sky over the block, so some roofs are soil under air rather than rock under rock.
        world.FillBox(new ChunkPos(min.X, 2, min.Z), new ChunkPos(max.X, 2, max.Z), Air);
        world.SetBox(new VoxelPos(-32, 60, -32), new VoxelPos(63, 63, 63), Dirt);

        SupportRegion incremental = world.NewRegion();
        var dugOrder = new List<ChunkPos>();
        var loaded = world.Chunks.ToList();

        for (int step = 0; step < 16; step++)
        {
            int op = random.Next(10);
            if (op < 5 || dugOrder.Count == 0)
            {
                // Bite: a chunk, or a box of voxels inside one, becomes air.
                ChunkPos chunk = loaded[random.Next(loaded.Count)];
                if (world.IsDeparted(chunk))
                {
                    continue;
                }

                if (random.Next(3) == 0)
                {
                    world.Fill(chunk, Air);
                }
                else
                {
                    VoxelPos origin = chunk.OriginVoxel;
                    var lo = new VoxelPos(origin.X + random.Next(24), origin.Y + random.Next(24), origin.Z + random.Next(24));
                    var hi = lo.Offset(random.Next(1, 9), random.Next(1, 9), random.Next(1, 9));
                    world.SetBox(lo, hi, Air);
                }

                incremental.Edited(chunk);
                if (!dugOrder.Contains(chunk))
                {
                    dugOrder.Add(chunk);
                }
            }
            else if (op < 7)
            {
                // Fill: a box of stone or dirt back into a dug chunk, possibly in mid-air.
                ChunkPos chunk = dugOrder[random.Next(dugOrder.Count)];
                if (world.IsDeparted(chunk))
                {
                    continue;
                }

                VoxelPos origin = chunk.OriginVoxel;
                var lo = new VoxelPos(origin.X + random.Next(24), origin.Y + random.Next(24), origin.Z + random.Next(24));
                var hi = lo.Offset(random.Next(1, 9), random.Next(1, 9), random.Next(1, 9));
                world.SetBox(lo, hi, random.Next(2) == 0 ? Stone : Dirt);
                incremental.Edited(chunk);
            }
            else if (op < 9)
            {
                // Depart: a chunk leaves the loaded world.
                ChunkPos chunk = loaded[random.Next(loaded.Count)];
                if (world.IsDeparted(chunk))
                {
                    continue;
                }

                world.Depart(chunk);
                incremental.Departed(chunk);
            }
            else
            {
                // Return one that left.
                ChunkPos[] away = loaded.Where(world.IsDeparted).ToArray();
                if (away.Length == 0)
                {
                    continue;
                }

                ChunkPos chunk = away[random.Next(away.Length)];
                world.Return(chunk);
                incremental.Arrived(chunk);
            }

            incremental.Settle();

            SupportRegion fresh = world.NewRegion();
            foreach (ChunkPos chunk in Enumerable.Reverse(dugOrder))
            {
                fresh.MarkDug(chunk);
            }

            fresh.Settle();

            // The fixpoint on both, every fourth step — it is the dear check, and the equality
            // below carries it across the steps between.
            if (step % 4 == 3)
            {
                Assert.Empty(incremental.VerifyFixpoint());
                Assert.Empty(fresh.VerifyFixpoint());
            }

            Assert.Equal(fresh.Analysed.Count, incremental.Analysed.Count);
            foreach (ChunkPos chunk in fresh.Analysed)
            {
                ChunkSupport? expected = fresh.FieldAt(chunk);
                ChunkSupport? actual = incremental.FieldAt(chunk);
                if (expected is null)
                {
                    Assert.Null(actual);
                    continue;
                }

                Assert.NotNull(actual);
                Assert.True(expected.Snapshot().AsSpan().SequenceEqual(actual.Snapshot()), $"step {step}, {chunk} differs");
            }
        }

        Assert.True(incremental.Derives > 0);
    }

    [Fact]
    public void EveryFaceLayerIsTheVoxelsTouchingThatFace()
    {
        // The join reads layers and the derive indexes them by the in-plane axes; a layer copied
        // from the wrong side, or transposed, would be a neighbour reading its own far wall.
        var values = new byte[VoxelConstants.ChunkVolume];
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = (byte)(index % 241);
        }

        ChunkSupport field = ChunkSupport.Dense(values);
        var layer = new byte[ChunkSupport.LayerSize];
        for (int face = 0; face < 6; face++)
        {
            field.CopyLayer(face, layer);
            int axis = Meshing.VoxelFace.Axis[face];
            int along = Meshing.VoxelFace.Direction[face] > 0 ? 31 : 0;
            (int lo, int hi) = ChunkSupport.OtherAxes(axis);
            for (int b = 0; b < 32; b++)
            for (int a = 0; a < 32; a++)
            {
                int[] local = new int[3];
                local[axis] = along;
                local[lo] = a;
                local[hi] = b;
                // The index spelt out rather than asked of LayerIndex, or a transposed LayerIndex
                // would be its own oracle: the lower-numbered in-plane axis runs fastest.
                Assert.Equal(field.Value(local[0], local[1], local[2]), layer[(b * 32) + a]);
            }
        }

        // And a neighbour across +X hands over its −X layer.
        var faces = new SupportFaces();
        faces.From(0, field);
        field.CopyLayer(1, layer);
        Assert.True(faces.Layer(0).SequenceEqual(layer));
        Assert.True(faces.IsAnchored(1));
    }

    [Fact]
    public void TheEncodingHoldsTheWholeRangeAndNothingElse()
    {
        Assert.Equal(ChunkSupport.Unreached, ChunkSupport.Encode(SupportRule.Floor));
        Assert.Equal(ChunkSupport.Unreached, ChunkSupport.Encode(SupportRule.Floor - 1000));
        Assert.Equal(ChunkSupport.Zero, ChunkSupport.Encode(0));
        Assert.Equal(ChunkSupport.Full, ChunkSupport.Encode(K));
        for (int budget = SupportRule.Floor + 1; budget <= K; budget++)
        {
            byte value = ChunkSupport.Encode(budget);
            Assert.Equal(budget, ChunkSupport.Decode(value));
            Assert.Equal(budget < 0, ChunkSupport.IsAtRisk(value));
            Assert.True(ChunkSupport.IsSolid(value));
        }

        Assert.False(ChunkSupport.IsSolid(ChunkSupport.NotSolid));
        Assert.False(ChunkSupport.IsAtRisk(ChunkSupport.NotSolid));
        Assert.True(ChunkSupport.IsSolid(ChunkSupport.Unreached));
        Assert.True(ChunkSupport.IsAtRisk(ChunkSupport.Unreached));
        Assert.False(ChunkSupport.IsReached(ChunkSupport.Unreached));
        Assert.True(ChunkSupport.IsReached(ChunkSupport.Encode(SupportRule.Floor + 1)));
        Assert.Equal(K, ChunkSupport.Deficit(ChunkSupport.Unreached));
        Assert.Equal(K - 1, ChunkSupport.Deficit(ChunkSupport.Encode(SupportRule.Floor + 1)));
        Assert.Equal(0, ChunkSupport.Deficit(ChunkSupport.Zero));
        Assert.Equal(ChunkSupport.Unreached, ChunkSupport.Encode(ChunkSupport.UnreachedBudget));
        Assert.Equal(ChunkSupport.UnreachedBudget, ChunkSupport.Decode(ChunkSupport.Unreached));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChunkSupport.Encode(K + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChunkSupport.Decode(ChunkSupport.NotSolid));

        ChunkSupport uniform = ChunkSupport.Uniform(ChunkSupport.Unreached);
        Assert.Equal(VoxelConstants.ChunkVolume, uniform.AtRiskCount);
        Assert.Equal(K, uniform.MaxDeficit);
        Assert.Equal(0, ChunkSupport.Uniform(ChunkSupport.Full).AtRiskCount);
    }

    [Fact]
    public void TheRulesCostsAreTheConstantsDecisionOneDerivedFrom()
    {
        SupportRule rule = SupportRule.Default;
        Assert.Equal(4, rule.Refund);
        Assert.Equal(4, rule.Cost(30));
        Assert.Equal(15, rule.Cost(8));
        Assert.Equal(60, rule.Cost(2));
        Assert.Equal(8, rule.HangCost(4));
        Assert.Equal(30, rule.HangCost(15));
        Assert.Equal(2 * K, rule.Cost(0));
        Assert.Equal(2 * K, rule.Cost(float.NaN));
        Assert.Equal(1, rule.Cost(1000));
        Assert.Equal(K, rule.Rest(K));
        Assert.Equal(K, rule.Rest(K - 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SupportRule { RestRefundVoxels = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new SupportRule { DistanceScale = 0f });
    }
}
