using Overburden.Core.Content;
using Overburden.Core.Meshing;
using Overburden.Core.Streaming;
using Overburden.Core.Structure;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// M9b: the support field in the loop — the region's pumped arm, the derive as a detached job
/// through the real pipeline, the settled barrier, and the strain bit in the mesh.
/// </summary>
/// <remarks>
/// The oracle is still <see cref="SupportRegion.VerifyFixpoint"/>, now reached through
/// <c>SetVoxel</c>, workers and <c>IsQuiescent</c> — the wire test M5c-2's rule asks for. The
/// pumped arm is held to the loop arm byte for byte under a random interleaving of takes,
/// landings and edits, because the in-flight rule is the one thing the loop arm cannot exercise.
/// </remarks>
public class SupportPipelineTests
{
    private const int Air = MaterialIds.AirNumericId;
    private const int Stone = SupportTestWorld.Stone;
    private const int Dirt = SupportTestWorld.Dirt;

    /// <summary>Deep enough that a 2 × 2 bite's roof is stone under stone, whatever the surface does.</summary>
    private static readonly ChunkPos BiteOrigin = new(0, -6, 0);

    private static readonly ChunkPos[] TwoByTwo =
    [
        BiteOrigin, BiteOrigin.Offset(1, 0, 0), BiteOrigin.Offset(0, 0, 1), BiteOrigin.Offset(1, 0, 1),
    ];

    // ------------------------------------------------------------------ through the pipeline

    [Fact]
    public void ATwoByTwoBiteThroughThePipelineSettlesToTheBenchsPlugAndTintsIt()
    {
        using var harness = new PipelineHarness();
        SupportRegion region = Attach(harness);
        harness.Pipeline.Streamer.AddViewer(BiteOrigin, 2, 2);
        harness.Settle();

        Assert.Equal(0, region.Analysed.Count);
        Assert.Equal(0, harness.Pipeline.SupportDerivesSubmitted);

        Bite(harness, TwoByTwo);

        // Queued before a frame has run: the edit hook is synchronous, the derive is not.
        Assert.True(region.Pending > 0);
        Assert.False(harness.Pipeline.IsQuiescent);

        harness.Settle();

        // The bench's fixture 2, through SetVoxel, workers and the join: the twenty-voxel plug,
        // every field at the fixpoint, nothing pending, everything settled.
        Assert.Empty(region.VerifyFixpoint());
        Assert.Equal(20, region.SettledAtRiskVoxels);
        Assert.Equal(4, region.SettledAtRiskChunks);
        Assert.Equal(0, region.Unsettled);
        Assert.Equal(0, region.InFlight);
        Assert.True(harness.Pipeline.SupportDerivesApplied > 0);
        Assert.Equal(harness.Pipeline.SupportDerivesSubmitted, harness.Pipeline.SupportDerivesApplied);
        Assert.Equal(0, harness.Pipeline.SupportDerivesInFlight);

        // The dug set is the pipeline's HasEdits set, read off the same hook the tracker reads.
        var edited = harness.Pipeline.Streamer.Chunks.Where(c => c.HasEdits).Select(c => c.Position).ToHashSet();
        Assert.Equal(edited, region.Dug.ToHashSet());

        // And the plug is on the mesh: the roof chunks' meshes carry the strain bit on exactly
        // the faces of at-risk voxels — twenty voxels, four roof chunks, some faces merged.
        int strained = 0;
        foreach (ChunkPos bite in TwoByTwo)
        {
            ChunkPos roof = bite.Offset(0, 1, 0);
            Assert.True(harness.LastMesh.TryGetValue(roof, out ChunkMesh? mesh), $"no mesh for the roof {roof}");
            strained += StrainedQuads(mesh!);
        }

        Assert.True(strained > 0, "no strained quad on any roof chunk");

        // Nothing else in the box is tinted: strain is where the field says, and nowhere else.
        foreach ((ChunkPos position, ChunkMesh mesh) in harness.LastMesh)
        {
            if (TwoByTwo.Any(bite => bite.Offset(0, 1, 0) == position))
            {
                continue;
            }

            Assert.Equal(0, StrainedQuads(mesh));
        }
    }

    [Fact]
    public void TheCollapseThroughThePipelineDropsThePlugRemeshesTheRoofAndLeavesTheFixpointClean()
    {
        // M9c-1's wire: the scheduler over the pipeline's own SetVoxel. A bite, a settle, a sweep
        // with no delay — one roof fires per settle, because a fall is an edit and the other
        // roofs are its neighbours — and after the last: the plug on the bite's floor as the
        // harness's stone (its rubble stand-in), HasEdits on every roof, the roofs remeshed,
        // the region requeued and settled, nothing at risk, quiescent and fixpoint-clean.
        using var harness = new PipelineHarness();
        SupportRegion region = Attach(harness);
        harness.Pipeline.Streamer.AddViewer(BiteOrigin, 2, 2);
        harness.Settle();
        Bite(harness, TwoByTwo);
        harness.Settle();
        Assert.Equal(20, region.SettledAtRiskVoxels);

        var scheduler = new CollapseScheduler(
            region, harness.Pipeline, PipelineHarness.Materials(), rubbleId: Stone, seed: 1,
            new CollapseWindow { MinSeconds = 0, MaxSeconds = 0 });
        var roofDeliveries = TwoByTwo.ToDictionary(b => b.Offset(0, 1, 0), b => harness.Deliveries.GetValueOrDefault(b.Offset(0, 1, 0)));

        int fired = 0;
        for (int boundary = 0; boundary < 8 && fired < 4; boundary++)
        {
            IReadOnlyList<CollapseFire> fires = scheduler.Sweep(boundary * 500L);
            fired += fires.Count;
            foreach (CollapseFire fire in fires)
            {
                Assert.Equal(5, fire.Result.Voxels);
                Assert.Equal(0, fire.Result.StaleSkipped);
                Assert.True(harness.Pipeline.Streamer.Get(fire.Result.Chunk)!.HasEdits);
            }

            // The fall's writes went through the pipeline: the region is requeued and the
            // pipeline is not quiescent until its derives land.
            if (fires.Count > 0)
            {
                Assert.False(harness.Pipeline.IsQuiescent);
            }

            harness.Settle();
        }

        Assert.Equal(4, fired);
        Assert.Equal(20, scheduler.VoxelsFallen);
        Assert.Empty(region.VerifyFixpoint());
        Assert.Equal(0, region.SettledAtRiskVoxels);
        Assert.Equal(0, region.Unsettled);
        Assert.True(harness.Pipeline.IsQuiescent);

        // The plug is on the floor of the bites, as stone, and the plug's cells are air.
        VoxelPos origin = BiteOrigin.OriginVoxel;
        for (int x = 30; x <= 33; x++)
        for (int z = 30; z <= 33; z++)
        {
            Assert.True(harness.Pipeline.TryGetMaterial(origin.Offset(x, 0, z), out int floor));
            Assert.Equal(Stone, floor);
            Assert.True(harness.Pipeline.TryGetMaterial(origin.Offset(x, 32, z), out int plug));
            Assert.Equal(Air, plug);
        }

        // Every roof and every bite was remeshed by the fall.
        foreach (ChunkPos bite in TwoByTwo)
        {
            ChunkPos roof = bite.Offset(0, 1, 0);
            Assert.True(harness.Deliveries.GetValueOrDefault(roof) > roofDeliveries[roof], $"the roof {roof} was not remeshed");
        }
    }

    [Fact]
    public void WithTheTintOffTheDerivesRunAndTheMeshIsUntouched()
    {
        using var harness = new PipelineHarness();
        SupportRegion region = Attach(harness);
        harness.Pipeline.SupportTint = false;
        harness.Pipeline.Streamer.AddViewer(BiteOrigin, 2, 2);
        harness.Settle();

        Bite(harness, TwoByTwo);
        harness.Settle();

        Assert.Equal(20, region.SettledAtRiskVoxels);
        Assert.Equal(0, harness.Pipeline.StrainRemeshes);

        // A remesh after the settle — a chip in a roof chunk's far corner, whose edit remesh
        // reads whatever the mesher is handed — with the field settled and the switch off.
        // Without this the roofs were all drawn before any field settled and the switch could
        // be ignored with nothing failing.
        VoxelPos corner = BiteOrigin.Offset(0, 1, 0).OriginVoxel.Offset(0, 31, 0);
        Assert.True(harness.Pipeline.SetVoxel(corner, Air));
        harness.Settle();
        Assert.Equal(20, region.SettledAtRiskVoxels);

        foreach (ChunkMesh mesh in harness.LastMesh.Values)
        {
            Assert.Equal(0, StrainedQuads(mesh));
        }
    }

    [Fact]
    public void ASettleAfterTheMeshIsOnScreenRemeshesOnlyWhereTheTintMoved()
    {
        using var harness = new PipelineHarness();
        SupportRegion region = Attach(harness);
        harness.Pipeline.Streamer.AddViewer(BiteOrigin, 2, 2);
        harness.Settle();

        // One bite: nothing at risk, so the roof meshes untinted, and that is the state a
        // second bite beside it changes.
        Bite(harness, [BiteOrigin]);
        harness.Settle();
        Assert.Equal(0, region.SettledAtRiskVoxels);
        int remeshesBefore = harness.Pipeline.StrainRemeshes;

        Bite(harness, [BiteOrigin.Offset(1, 0, 0), BiteOrigin.Offset(0, 0, 1), BiteOrigin.Offset(1, 0, 1)]);
        harness.Settle();

        Assert.Equal(20, region.SettledAtRiskVoxels);

        // The first bite's roof was drawn without strain and now needs it; the three new roofs
        // were remeshed for their own edit and read the settled field then, or were tinted by
        // this path — either way every roof on screen carries the plug now.
        Assert.True(harness.Pipeline.StrainRemeshes > remeshesBefore);
        foreach (ChunkPos bite in TwoByTwo)
        {
            Assert.True(StrainedQuads(harness.LastMesh[bite.Offset(0, 1, 0)]) > 0, $"roof {bite.Offset(0, 1, 0)} untinted");
        }
    }

    [Fact]
    public void AChunkThatLeavesAndReturnsIsReDerivedAndTheFieldMatches()
    {
        // With the cache, so the bites — edits, and this harness has no store — survive the walk
        // and come back as the third arrival path, voxels and HasEdits intact.
        using var harness = new PipelineHarness(chunkCacheBytes: 64L * 1024 * 1024);
        SupportRegion region = Attach(harness);
        ChunkStreamer streamer = harness.Pipeline.Streamer;
        ViewerId viewer = streamer.AddViewer(BiteOrigin, 2, 2);
        harness.Settle();

        Bite(harness, TwoByTwo);
        harness.Settle();
        Assert.Equal(20, region.SettledAtRiskVoxels);

        // Walk away: every field goes with its chunk, and the analysed set stays.
        streamer.UpdateViewer(viewer, new Viewer(BiteOrigin.Offset(40, 0, 0), 2, 2));
        harness.Settle();
        Assert.Equal(0, region.FieldCount);
        Assert.True(region.Analysed.Count > 0);

        // And back: the chunks return (cache or store or seed — the bites are edits, so from
        // the cache here), arrive, and derive to the same plug.
        streamer.UpdateViewer(viewer, new Viewer(BiteOrigin, 2, 2));
        harness.Settle();
        Assert.Empty(region.VerifyFixpoint());
        Assert.Equal(20, region.SettledAtRiskVoxels);
        Assert.True(region.Triggered(SupportTrigger.Departure) > 0);
        Assert.True(region.Triggered(SupportTrigger.Arrival) > 0);
    }

    // ------------------------------------------------------------------ the pumped arm, on its own

    [Fact]
    public void ATriggerReachingAChunkInFlightRequeuesItWhenTheResultLands()
    {
        var world = new SupportTestWorld();
        world.FillBox(new ChunkPos(-2, -2, -2), new ChunkPos(2, 2, 2), Stone);
        SupportRegion region = world.NewRegion();
        var bite = new ChunkPos(0, 0, 0);
        world.Fill(bite, Air);
        region.MarkDug(bite);

        // Take everything out, so nothing is queued and the seven are all in flight.
        var taken = new List<Taken>();
        while (Take(world, region, out Taken one))
        {
            taken.Add(one);
        }

        Assert.Equal(7, taken.Count);
        Assert.Equal(7, region.InFlight);
        Assert.Equal(0, region.Pending);

        // An edit lands in the bite while its derive is out: nothing is queued now — the chunk
        // and its six are all in flight — and each landing requeues its chunk, once. Seven,
        // because an edit at a chunk changes what its neighbours' padded copies hold, which is
        // MarkDug's rule whether or not a derive is out.
        world.SetBox(bite.OriginVoxel, bite.OriginVoxel.Offset(3, 3, 3), Dirt);
        region.Edited(bite);
        Assert.Equal(0, region.Pending);

        foreach (Taken one in taken)
        {
            Land(world, region, one, settled: null);
        }

        Assert.Equal(7, region.Pending);
        Assert.Equal(7, region.RequeuedStale);
        Assert.False(region.IsSettled(bite));

        // Round again, and the field is the one an edit-before-take would have given.
        Drain(world, region);
        Assert.Empty(region.VerifyFixpoint());
        Assert.True(region.TryTakeNext(out _) == false);
    }

    [Fact]
    public void AChunkIsNotSettledWhileOneOfItsSevenIsQueuedOrInFlight()
    {
        var world = new SupportTestWorld();
        world.FillBox(new ChunkPos(-2, -2, -2), new ChunkPos(2, 2, 2), Stone);
        SupportRegion region = world.NewRegion();
        var bite = new ChunkPos(0, 0, 0);
        world.Fill(bite, Air);
        region.MarkDug(bite);

        var settled = new List<ChunkPos>();
        var landed = new List<ChunkPos>();
        while (Take(world, region, out Taken taken))
        {
            Land(world, region, taken, settled);
            landed.Add(taken.Chunk);

            // Nothing settles while anything is queued: every one of the seven is a face
            // neighbour of the bite, and the bite is a neighbour of each — one still queued
            // holds the bite, and the bite held holds them all.
            if (region.Pending > 0 || region.InFlight > 0)
            {
                Assert.All(landed, chunk => Assert.False(region.IsSettled(chunk), $"{chunk} settled early"));
                Assert.Null(region.SettledField(chunk: bite));
            }
        }

        // Round two — the join requeued whoever's layer moved — until nothing is queued.
        Drain(world, region, settled);

        Assert.Equal(0, region.Unsettled);
        Assert.Contains(bite, settled);
        Assert.NotNull(region.SettledField(bite));
        Assert.Same(region.FieldAt(bite), region.SettledField(bite));
        Assert.Empty(region.VerifyFixpoint());
    }

    [Fact]
    public void AResultForAChunkThatLeftIsDroppedAndOneThatCameBackIsRequeued()
    {
        var world = new SupportTestWorld();
        world.FillBox(new ChunkPos(-2, -2, -2), new ChunkPos(2, 2, 2), Stone);
        SupportRegion region = world.NewRegion();
        var bite = new ChunkPos(0, 0, 0);
        world.Fill(bite, Air);
        region.MarkDug(bite);

        var taken = new List<Taken>();
        while (Take(world, region, out Taken one))
        {
            taken.Add(one);
        }

        ChunkPos roof = bite.Offset(0, 1, 0);
        ChunkPos wall = bite.Offset(1, 0, 0);
        Taken roofRequest = taken.Single(r => r.Chunk == roof);
        Taken wallRequest = taken.Single(r => r.Chunk == wall);

        // The roof leaves and its result lands: dropped, no field. The wall leaves and comes
        // back: its result lands and is kept, and it goes round again for the return.
        world.Depart(roof);
        region.Departed(roof);
        world.Depart(wall);
        region.Departed(wall);
        world.Return(wall);
        region.Arrived(wall);

        Land(world, region, roofRequest, null);
        Assert.Null(region.FieldAt(roof));
        Assert.Equal(1, region.DroppedDeparted);

        Land(world, region, wallRequest, null);
        Assert.NotNull(region.FieldAt(wall));
        Assert.Equal(1, region.RequeuedStale);

        foreach (Taken one in taken.Where(r => r.Chunk != roof && r.Chunk != wall))
        {
            Land(world, region, one, null);
        }

        Drain(world, region);
        Assert.Empty(region.VerifyFixpoint());
        Assert.Null(region.FieldAt(roof));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ThePumpedArmEqualsTheLoopArmUnderAnyInterleaving(int seedIndex)
    {
        // Requests taken and landed in a random order, with edits, departures and returns
        // landing between the two — the in-flight rule under fire — against a fresh region
        // settled in one loop over the same world. Uniqueness says they agree byte for byte.
        var random = new Random(0x9B00 + seedIndex);
        var world = new SupportTestWorld();
        var min = new ChunkPos(-1, -2, -1);
        var max = new ChunkPos(1, 1, 1);
        world.FillBox(min, max, Stone);
        world.FillBox(new ChunkPos(min.X, 2, min.Z), new ChunkPos(max.X, 2, max.Z), Air);
        world.SetBox(new VoxelPos(-32, 60, -32), new VoxelPos(63, 63, 63), Dirt);

        SupportRegion pumped = world.NewRegion();
        var dug = new List<ChunkPos>();
        var loaded = world.Chunks.ToList();
        var inFlight = new List<Taken>();

        for (int step = 0; step < 40; step++)
        {
            int op = random.Next(12);
            if (op < 4 || dug.Count == 0)
            {
                ChunkPos chunk = loaded[random.Next(loaded.Count)];
                if (world.IsDeparted(chunk)) continue;
                if (random.Next(3) == 0)
                {
                    world.Fill(chunk, Air);
                }
                else
                {
                    VoxelPos origin = chunk.OriginVoxel;
                    var lo = new VoxelPos(origin.X + random.Next(24), origin.Y + random.Next(24), origin.Z + random.Next(24));
                    world.SetBox(lo, lo.Offset(random.Next(1, 9), random.Next(1, 9), random.Next(1, 9)), random.Next(4) == 0 ? Dirt : Air);
                }

                pumped.Edited(chunk);
                if (!dug.Contains(chunk)) dug.Add(chunk);
            }
            else if (op < 6)
            {
                ChunkPos chunk = loaded[random.Next(loaded.Count)];
                if (world.IsDeparted(chunk)) continue;
                world.Depart(chunk);
                pumped.Departed(chunk);
            }
            else if (op < 7)
            {
                ChunkPos[] away = loaded.Where(world.IsDeparted).ToArray();
                if (away.Length == 0) continue;
                ChunkPos chunk = away[random.Next(away.Length)];
                world.Return(chunk);
                pumped.Arrived(chunk);
            }
            else if (op < 10)
            {
                // Take a few.
                int take = random.Next(1, 4);
                while (take-- > 0 && Take(world, pumped, out Taken taken))
                {
                    inFlight.Add(taken);
                }
            }
            else
            {
                // Land one, in any order.
                if (inFlight.Count == 0) continue;
                int at = random.Next(inFlight.Count);
                Taken taken = inFlight[at];
                inFlight.RemoveAt(at);
                Land(world, pumped, taken, null);
            }

            if (step % 8 != 7)
            {
                continue;
            }

            // Every eighth step: land everything, drain, and compare.
            foreach (Taken taken in inFlight)
            {
                Land(world, pumped, taken, null);
            }

            inFlight.Clear();
            Drain(world, pumped);

            SupportRegion fresh = world.NewRegion();
            foreach (ChunkPos chunk in Enumerable.Reverse(dug))
            {
                fresh.MarkDug(chunk);
            }

            fresh.Settle();

            Assert.Empty(pumped.VerifyFixpoint());
            Assert.Equal(fresh.Analysed.Count, pumped.Analysed.Count);
            Assert.Equal(0, pumped.Unsettled);
            foreach (ChunkPos chunk in fresh.Analysed)
            {
                ChunkSupport? expected = fresh.FieldAt(chunk);
                ChunkSupport? actual = pumped.FieldAt(chunk);
                if (expected is null)
                {
                    Assert.Null(actual);
                    continue;
                }

                Assert.NotNull(actual);
                Assert.True(expected.Snapshot().AsSpan().SequenceEqual(actual.Snapshot()), $"step {step}, {chunk} differs");
                Assert.Same(actual, pumped.SettledField(chunk));
            }
        }

        Assert.True(pumped.RequeuedStale > 0, "the interleaving never caught a derive in flight — the fixture is not testing the rule");
    }

    [Fact]
    public void TheCauseCountsCountQueueChangesNotCalls()
    {
        var world = new SupportTestWorld();
        world.FillBox(new ChunkPos(-2, -2, -2), new ChunkPos(2, 2, 2), Stone);
        SupportRegion region = world.NewRegion();
        var bite = new ChunkPos(0, 0, 0);
        world.Fill(bite, Air);

        // A bite is 32,768 hooks; the queue changes seven times.
        for (int i = 0; i < 32_768; i++)
        {
            region.Edited(bite);
        }

        Assert.Equal(7, region.Triggered(SupportTrigger.Edit));
        Assert.Equal(7, region.Pending);

        // Edited has an early-out that makes the count above true of calls too; Arrived has none,
        // so a second arrival with everything already queued is the call that must not count.
        region.Arrived(bite);
        long once = region.Triggered(SupportTrigger.Arrival);
        region.Arrived(bite);
        Assert.Equal(0, once);
        Assert.Equal(once, region.Triggered(SupportTrigger.Arrival));
        Assert.Equal(7, region.Pending);

        // And after the queue is emptied, the same arrival counts its seven.
        Drain(world, region);
        region.Arrived(bite);
        Assert.Equal(7, region.Triggered(SupportTrigger.Arrival));
    }

    // ------------------------------------------------------------------ the field's own compare

    [Fact]
    public void SameAtRiskSetComparesTheMarkedVoxelsAndNothingElse()
    {
        byte[] a = new byte[VoxelConstants.ChunkVolume];
        byte[] b = new byte[VoxelConstants.ChunkVolume];
        Array.Fill(a, ChunkSupport.Full);
        Array.Fill(b, ChunkSupport.Full);
        a[100] = ChunkSupport.Encode(-3);
        b[100] = ChunkSupport.Encode(-9);
        ChunkSupport fa = ChunkSupport.Dense(a);
        ChunkSupport fb = ChunkSupport.Dense(b);

        // Same voxel at risk at different deficits: the tint is the same.
        Assert.True(fa.SameAtRiskSet(fb));

        // A different voxel: not.
        byte[] c = (byte[])a.Clone();
        c[100] = ChunkSupport.Full;
        c[101] = ChunkSupport.Encode(-3);
        Assert.False(fa.SameAtRiskSet(ChunkSupport.Dense(c)));

        // Null is "nothing at risk": equal to an all-held field and not to fa.
        Assert.False(fa.SameAtRiskSet(null));
        Assert.True(ChunkSupport.Uniform(ChunkSupport.Full).SameAtRiskSet(null));
        Assert.True(ChunkSupport.Uniform(ChunkSupport.NotSolid).SameAtRiskSet(null));

        // A uniform field against a dense one holding the same set.
        byte[] all = new byte[VoxelConstants.ChunkVolume];
        Array.Fill(all, ChunkSupport.Unreached);
        Assert.True(ChunkSupport.Uniform(ChunkSupport.Unreached).SameAtRiskSet(ChunkSupport.Dense(all)));
    }

    // ------------------------------------------------------------------ the mesher

    [Fact]
    public void AnAtRiskVoxelsFacesCarryStrainAndStopTheMergeAtItsEdge()
    {
        // A flat stone floor, one voxel of it at risk: its top face is a quad of its own with
        // strain 1, the rest of the floor merges around it with strain 0.
        var padded = new PaddedChunk();
        padded.Fill(Air);
        for (int z = 0; z < VoxelConstants.ChunkSize; z++)
        for (int x = 0; x < VoxelConstants.ChunkSize; x++)
        {
            padded[x, 0, z] = Stone;
        }

        byte[] field = new byte[VoxelConstants.ChunkVolume];
        Array.Fill(field, ChunkSupport.NotSolid);
        for (int z = 0; z < VoxelConstants.ChunkSize; z++)
        for (int x = 0; x < VoxelConstants.ChunkSize; x++)
        {
            field[VoxelPos.Index(x, 0, z)] = ChunkSupport.Full;
        }

        // Off the diagonal, so a mesher reading the field with x and z swapped tints the wrong
        // voxel rather than the same one.
        field[VoxelPos.Index(10, 0, 20)] = ChunkSupport.Encode(-5);
        ChunkSupport support = ChunkSupport.Dense(field);
        MaterialTable materials = new SupportTestWorld().Materials;

        var withStrain = new ChunkMesh();
        new GreedyMesher(ambientOcclusion: false).Build(padded, materials, withStrain, light: null, support);
        var without = new ChunkMesh();
        new GreedyMesher(ambientOcclusion: false).Build(padded, materials, without, light: null, support: null);

        Assert.Equal(0, StrainedQuads(without));
        Assert.True(StrainedQuads(withStrain) >= 1);
        Assert.True(withStrain.Opaque.QuadCount > without.Opaque.QuadCount, "the at-risk voxel merged into the floor");

        // Every strained vertex sits on the at-risk voxel's faces: x in 10..11, z in 20..21 in voxels.
        MeshBuffer buffer = withStrain.Opaque;
        int strainedVertices = 0;
        for (int v = 0; v < buffer.VertexCount; v++)
        {
            if (buffer.Uv2s[v * 2 + 1] < 0.5f) continue;
            strainedVertices++;
            float x = buffer.Vertices[v * 3 + 0] / VoxelConstants.VoxelSize;
            float z = buffer.Vertices[v * 3 + 2] / VoxelConstants.VoxelSize;
            Assert.InRange(x, 10f, 11f);
            Assert.InRange(z, 20f, 21f);
        }

        Assert.True(strainedVertices >= 4);
    }

    // ------------------------------------------------------------------ helpers

    private static SupportRegion Attach(PipelineHarness harness)
    {
        ChunkStreamer streamer = harness.Pipeline.Streamer;
        var region = new SupportRegion(position => streamer.Get(position)?.Voxels, PipelineHarness.Materials(), SupportRule.Default);
        harness.Pipeline.Support = region;
        return region;
    }

    private static void Bite(PipelineHarness harness, IEnumerable<ChunkPos> chunks)
    {
        foreach (ChunkPos chunk in chunks)
        {
            VoxelPos origin = chunk.OriginVoxel;
            for (int z = 0; z < VoxelConstants.ChunkSize; z++)
            for (int x = 0; x < VoxelConstants.ChunkSize; x++)
            for (int y = 0; y < VoxelConstants.ChunkSize; y++)
            {
                Assert.True(harness.Pipeline.SetVoxel(origin.Offset(x, y, z), Air));
            }
        }
    }

    private static int StrainedQuads(ChunkMesh mesh)
    {
        int strained = 0;
        MeshBuffer buffer = mesh.Opaque;
        for (int quad = 0; quad < buffer.VertexCount / 4; quad++)
        {
            if (buffer.Uv2s[quad * 4 * 2 + 1] > 0.5f)
            {
                strained++;
            }
        }

        return strained;
    }

    /// <summary>A request with the 27 references captured when it was taken — what the job carries.</summary>
    private sealed record Taken(SupportDeriveRequest Request, VoxelChunk?[] Neighbourhood)
    {
        public ChunkPos Chunk => Request.Chunk;
    }

    /// <summary>Takes one request, capturing the neighbourhood now as the pipeline does at submission.</summary>
    private static bool Take(SupportTestWorld world, SupportRegion region, out Taken taken)
    {
        if (!region.TryTakeNext(out SupportDeriveRequest request))
        {
            taken = null!;
            return false;
        }

        var neighbourhood = new VoxelChunk?[PaddedChunk.NeighbourhoodSize];
        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
        {
            neighbourhood[PaddedChunk.NeighbourIndex(dx, dy, dz)] = world.Get(request.Chunk.Offset(dx, dy, dz));
        }

        taken = new Taken(request, neighbourhood);
        return true;
    }

    /// <summary>Runs a taken request's derive here — the worker's half, synchronously — and lands it.</summary>
    private static void Land(SupportTestWorld world, SupportRegion region, Taken taken, List<ChunkPos>? settled)
    {
        var padded = new PaddedChunk();
        padded.CopyFrom(taken.Neighbourhood, PaddedChunk.MissingMaterial);
        ChunkSupport field = new SupportDerive().Derive(padded, world.Materials, region.Rule, taken.Request.Costs, taken.Request.Faces, out SupportDeriveResult result);
        region.Applied(taken.Chunk, field, result, settled);
    }

    private static void Drain(SupportTestWorld world, SupportRegion region, List<ChunkPos>? settled = null)
    {
        while (Take(world, region, out Taken taken))
        {
            Land(world, region, taken, settled);
        }
    }
}
