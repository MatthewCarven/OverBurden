using Overburden.Core.Lighting;
using Overburden.Core.Meshing;
using Overburden.Core.Streaming;
using Overburden.Core.Voxels;
using Overburden.Core.Worldgen;

namespace Overburden.Core.Tests;

/// <summary>
/// M5c-1: light through the real pipeline — the stage that gates meshing, the grids' lifetime,
/// and the relight an edit triggers.
/// </summary>
/// <remarks>
/// <para>
/// <b>These are integration tests against a real <see cref="Jobs.JobScheduler"/> on real
/// threads</b>, for the reason <see cref="ChunkPipelineTests"/> gives: the bugs here live in the
/// interaction between refcounting, a global BFS and async completion, and a fake scheduler that
/// completed jobs inline would have none of them.
/// </para>
/// <para>
/// <b>M5b's oracle is reused rather than restated.</b> The propagation fixpoint is unique, so
/// checking it cell by cell is a complete statement of correctness with no propagation in the
/// check — and applying it to the grids the <em>pipeline</em> built is what says the wiring did
/// not quietly break the thing M5b proved about the algorithm.
/// </para>
/// </remarks>
public class LightPipelineTests
{
    private const int Air = Overburden.Core.Content.MaterialIds.AirNumericId;
    private const int Stone = 3;

    [Fact]
    public void ThePropagationFixpointHoldsAcrossEveryGridThePipelineBuilt()
    {
        using var harness = new PipelineHarness();
        harness.Pipeline.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
        harness.Settle();

        AssertFixpoint(harness);

        // Not vacuous: some cell somewhere is actually lit, and some cell is actually dark.
        // A pipeline that built every grid and propagated nothing satisfies the invariant
        // perfectly and is completely broken.
        Assert.Contains(AllCells(harness), c => Sky(harness, c) == ChunkLight.MaxLevel);
        Assert.Contains(AllCells(harness), c => Sky(harness, c) == 0);
    }

    /// <summary>
    /// The barrier: a chunk that has drawn a mesh is <see cref="GenerationStage.Lit"/>, and so
    /// are all 26 of its neighbours.
    /// </summary>
    /// <remarks>
    /// The margin fails this by construction, exactly as it fails the feature barrier — its own
    /// outward neighbours were never loaded, so it never reaches <see cref="GenerationStage.Lit"/>
    /// and never meshes. No shell bookkeeping, one refcount, same as M2c.
    /// </remarks>
    [Fact]
    public void NothingMeshesUntilItAndAllTwentySixNeighboursAreLit()
    {
        using var harness = new PipelineHarness();
        harness.Pipeline.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
        harness.Settle();

        ChunkStreamer streamer = harness.Pipeline.Streamer;
        Assert.NotEmpty(harness.MeshedPositions);

        foreach (ChunkPos position in harness.MeshedPositions)
        {
            Assert.Equal(GenerationStage.Lit, streamer.Get(position)!.Stage);
            Assert.True(
                streamer.AllNeighboursAtLeast(position, GenerationStage.Lit),
                $"{position} was meshed with a neighbour that is not lit");
        }

        // And the other direction: the outermost loaded ring derived its light and joined the
        // propagation without ever reaching the stage. That is what keeps light off the load
        // margin — a third ring would show up here as this chunk having no grid at all.
        StreamedChunk margin = streamer.Get(new ChunkPos(3, 0, 0))!;
        Assert.NotEqual(GenerationStage.Lit, margin.Stage);
        Assert.NotNull(margin.Light);
        Assert.True(margin.Light!.HasDerived);
    }

    /// <summary>
    /// Every chunk gets exactly one derive on a still world — the rebuild rule is not thrashing.
    /// </summary>
    /// <remarks>
    /// <b>The failure this catches is a performance one that nothing else would report.</b>
    /// <see cref="StreamedChunk.LightNeighbours"/> rebuilds when a neighbour is <em>gained</em>,
    /// and a rule that also fired on a neighbour being lost, or that compared the masks for
    /// inequality rather than for gain, would rebuild the load boundary every time a viewer
    /// moved — correct output, several times the cost, and no test failing to say so.
    /// </remarks>
    [Fact]
    public void AStillWorldDerivesEachChunkExactlyOnce()
    {
        using var harness = new PipelineHarness();
        harness.Pipeline.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
        harness.Settle();

        Assert.Equal(harness.Pipeline.Streamer.TrackedCount, harness.Pipeline.LightBuilds);

        // Pumping a settled pipeline derives nothing further.
        int before = harness.Pipeline.LightBuilds;
        for (int i = 0; i < 10; i++) harness.Pipeline.Update();
        Assert.Equal(before, harness.Pipeline.LightBuilds);
    }

    /// <summary>
    /// A chunk that unloads releases its grid, and one that comes back is derived afresh rather
    /// than inheriting what it had.
    /// </summary>
    /// <remarks>
    /// This used to argue that a revived chunk regenerates its voxels from the seed, so a kept
    /// grid would be re-derivable anyway. That premise has been false since M6 — a returning chunk
    /// has the identical voxels whichever source it comes from — and the reason the grid still
    /// goes is that nothing yet invalidates it for a neighbour that changed while the chunk was
    /// out. That is the retained-chunk cache's second slice (docs/CHUNK-CACHE-PLAN.md §5); until
    /// it lands, a stale grid is indistinguishable from a fresh one, which is the direction that
    /// fails silently. ChunkCacheTests re-asserts this with the cache on.
    /// </remarks>
    [Fact]
    public void UnloadingReleasesTheLightGridAndReturningRebuildsIt()
    {
        using var harness = new PipelineHarness();
        ChunkStreamer streamer = harness.Pipeline.Streamer;
        ViewerId id = streamer.AddViewer(ChunkPos.Zero, 1, 1);
        harness.Settle();

        var probe = new ChunkPos(0, 0, 0);
        ChunkLight before = streamer.Get(probe)!.Light!;
        int[] skyBefore = CellValues(before);

        // Far enough that the whole original box unloads.
        streamer.MoveViewer(id, new ChunkPos(40, 0, 40));
        harness.Settle();
        Assert.False(streamer.IsTracked(probe));

        streamer.MoveViewer(id, ChunkPos.Zero);
        harness.Settle();

        ChunkLight after = streamer.Get(probe)!.Light!;
        Assert.NotSame(before, after);
        Assert.True(after.HasDerived);

        // Rebuilt to the same answer, because the world is a pure function of the seed. A grid
        // that had merely been *kept* would also pass this — which is why NotSame is asserted
        // above and why this one is about correctness rather than about lifetime.
        Assert.Equal(skyBefore, CellValues(after));
        AssertFixpoint(harness);
    }

    /// <summary>
    /// Sky columns are refcounted over the chunk column, so a vertical unload does not take one
    /// away while chunks above still share it.
    /// </summary>
    [Fact]
    public void SkyColumnsAreSharedDownTheChunkColumnAndReleasedWithTheLastChunk()
    {
        using var harness = new PipelineHarness();
        ChunkStreamer streamer = harness.Pipeline.Streamer;
        ViewerId id = streamer.AddViewer(ChunkPos.Zero, 1, 2);
        harness.Settle();

        // One column per (x, z) of the load box, not per chunk: radius 1 plus a 2-chunk margin
        // is 7 across, against 7 × 11 × 7 chunks.
        Assert.Equal(7 * 7, streamer.SkyColumnCount);
        Assert.True(streamer.TrackedCount > streamer.SkyColumnCount);

        // Every chunk in a column holds the same object — the thing a per-chunk copy would get
        // wrong, and it would only show up as a shaft lighting one chunk deep.
        SkyColumn top = streamer.Get(new ChunkPos(0, 2, 0))!.Sky!;
        SkyColumn bottom = streamer.Get(new ChunkPos(0, -2, 0))!.Sky!;
        Assert.Same(top, bottom);

        streamer.RemoveViewer(id);
        harness.Settle();
        Assert.Equal(0, streamer.SkyColumnCount);
    }

    /// <summary>
    /// Placing a lamp relights, and remeshes exactly the drawn chunks whose sampled light moved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Both directions, because over-marking is a performance bug that hides correctness
    /// bugs.</b> Marking all 27 neighbours of the edit would pass a one-directional test and
    /// quietly cost 27 mesh sweeps per swing.
    /// </para>
    /// <para>
    /// The expected set is derived from the light grids themselves — snapshot every cell before
    /// and after, then ask which chunks' padded 10³ sample window holds a cell that moved. That
    /// shares no code with <c>MarkRelitChunks</c>, which is the whole point; a reference that
    /// reuses the subject's reasoning proves nothing.
    /// </para>
    /// </remarks>
    [Fact]
    public void ALampRemeshesExactlyTheDrawnChunksWhoseSampledLightMoved()
    {
        using var harness = new PipelineHarness();
        ChunkStreamer streamer = harness.Pipeline.Streamer;
        streamer.AddViewer(ChunkPos.Zero, 1, 1);
        harness.Settle();

        Dictionary<ChunkPos, int[]> before = Snapshot(harness);
        var drawn = harness.MeshedPositions.ToHashSet();
        Dictionary<ChunkPos, int> deliveriesBefore = new(harness.Deliveries);

        // Inside the centre chunk and away from its faces, so the voxel-dirty set is one chunk
        // and anything wider came from the light.
        var lamp = new VoxelPos(16, 16, 16);
        Assert.True(harness.Pipeline.SetVoxel(lamp, PipelineHarness.Lamp));
        harness.Settle();

        Dictionary<ChunkPos, int[]> after = Snapshot(harness);

        // Independent expectation: a chunk needs a new mesh iff it was drawn and some cell in
        // its own grid or one cell into a neighbour changed — or its voxels did.
        var expected = new HashSet<ChunkPos>();
        foreach (ChunkPos position in drawn)
        {
            if (position == lamp.Chunk || SampledLightChanged(position, before, after))
            {
                expected.Add(position);
            }
        }

        var remeshed = drawn
            .Where(p => harness.Deliveries[p] > deliveriesBefore.GetValueOrDefault(p))
            .ToHashSet();

        Assert.Equal(expected, remeshed);

        // Not vacuous in either direction: the lamp lit more than its own chunk, and it did not
        // light everything.
        Assert.True(remeshed.Count > 1, "a full-strength lamp should reach past its own chunk");
        Assert.True(remeshed.Count < drawn.Count, "a lamp should not remesh the whole visible box");

        AssertFixpoint(harness);
    }

    /// <summary>
    /// A relight is stale but not urgent; an edit is both. The two flags stay apart.
    /// </summary>
    /// <remarks>
    /// <b>The break-recipe is deriving urgency from "the light moved".</b> During the initial
    /// fill every chunk's light changes, so that arrives the entire distance-ordered streaming
    /// queue at band3 and M2a's ordering is silently gone — nothing looks broken, the world just
    /// loads in the wrong order. Counting the two kinds separately is what makes it visible.
    /// </remarks>
    [Fact]
    public void FillingTheWorldWithLightCountsNoEditRemeshesAtAll()
    {
        using var harness = new PipelineHarness();
        harness.Pipeline.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
        harness.Settle();

        Assert.Equal(0, harness.Pipeline.EditRemeshes);
        Assert.Equal(0, harness.Pipeline.RelightRemeshes);

        // And nothing was left flagged urgent for a later mesh to pick up.
        Assert.All(harness.Pipeline.Streamer.Chunks, c => Assert.False(c.RemeshUrgent));
    }

    [Fact]
    public void ALampCountsAsARelightRemeshAndTheCarveThatPlacedItAsAnEdit()
    {
        using var harness = new PipelineHarness();
        harness.Pipeline.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
        harness.Settle();

        harness.Pipeline.SetVoxel(new VoxelPos(16, 16, 16), PipelineHarness.Lamp);
        harness.Settle();

        // The chunk holding the lamp is urgent — the player is looking at the block they placed.
        Assert.True(harness.Pipeline.EditRemeshes > 0);

        // The chunks it merely lit are not. If this is zero the light never left its own chunk;
        // if it equals the drawn count the relight marked everything.
        Assert.True(
            harness.Pipeline.RelightRemeshes > 0,
            "a full-strength lamp should have relit chunks beyond the one holding it");
    }

    /// <summary>
    /// A lamp's relights are submitted at ordinary priority — only the chunk holding it is urgent.
    /// </summary>
    /// <remarks>
    /// <b>Written because the break-recipe battery found nothing that caught
    /// <c>bool urgent = chunk.NeedsRemesh</c> going back into <c>SubmitMesh</c>.</b> The counts
    /// still came out non-zero on both sides — the two-flag split is the whole point of the
    /// mitigation M5-PLAN names, and until this test nothing asserted the number rather than its
    /// sign. The lamp is placed in the interior of a chunk, away from every face, so the voxel
    /// edit dirties exactly one chunk and any urgency beyond that came from light.
    /// </remarks>
    [Fact]
    public void OnlyTheChunkHoldingTheLampIsUrgent()
    {
        using var harness = new PipelineHarness();
        harness.Pipeline.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
        harness.Settle();

        harness.Pipeline.SetVoxel(new VoxelPos(16, 16, 16), PipelineHarness.Lamp);
        harness.Settle();

        Assert.Equal(1, harness.Pipeline.EditRemeshes);
        Assert.True(
            harness.Pipeline.RelightRemeshes > 1,
            "a full-strength lamp should have relit several chunks at ordinary priority");
    }

    /// <summary>
    /// Unloading a neighbouring region re-derives nothing at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Recorded outcome, not a caught bug: weakening the gain-only rule to
    /// <c>present != chunk.LightNeighbours</c> is <em>unreachable</em>, and this test does not
    /// catch it either.</b> The battery found nothing that did, and reading the unload path says
    /// why — <c>OnChunkUnviewed</c> cancels and discards and pushes nothing, and neither does
    /// <c>Forget</c>. So a chunk that loses a neighbour is never re-examined, and the rule that
    /// would have rebuilt it never runs. The gain direction is different: a neighbour arriving
    /// finishes a generate, which pushes all 26.
    /// </para>
    /// <para>
    /// The gain-only form is kept because it is the one that states the intent, and because the
    /// moment anything <em>does</em> push a chunk after an unload — a relight, an edit at the
    /// margin, a future fix elsewhere — the difference becomes real. Same shape as M4a's contact
    /// epsilon, which can be set to zero with the suite green and is kept anyway.
    /// </para>
    /// <para>
    /// What this test does assert is reachable and worth keeping: unloading a region costs no
    /// derives at all, and the light left behind still satisfies the fixpoint. A push added to
    /// the unload path would show up here immediately.
    /// </para>
    /// </remarks>
    [Fact]
    public void LosingANeighbourReDerivesNothing()
    {
        using var harness = new PipelineHarness();
        ChunkStreamer streamer = harness.Pipeline.Streamer;
        streamer.AddViewer(ChunkPos.Zero, 1, 1);
        ViewerId second = streamer.AddViewer(new ChunkPos(6, 0, 0), 1, 1);
        harness.Settle();

        int before = harness.Pipeline.LightBuilds;

        streamer.RemoveViewer(second);
        harness.Settle();

        Assert.Equal(before, harness.Pipeline.LightBuilds);
        AssertFixpoint(harness);
    }

    /// <summary>
    /// A lamp puts block light into the vertices of a mesh the pipeline actually delivered.
    /// </summary>
    /// <remarks>
    /// <b>Written because the break-recipe battery caught nothing when <c>SubmitMesh</c> was made
    /// to hand the job <c>null</c> instead of a gathered light grid.</b> The whole of M5c-2's
    /// vertex-light suite calls <c>GreedyMesher.Build</c> directly, and the whole of M5c-1's
    /// pipeline suite checks states, counters and quiescence — so the pipeline could have stopped
    /// passing light to the mesher entirely and 556 tests would have stayed green while the world
    /// rendered at a flat full brightness.
    /// <para>
    /// The same shape of gap M5c-1 found between Core-level correctness and the wiring, one layer
    /// further out: <b>every part of the path was tested and the join was not.</b> Asserted on
    /// the bytes of a delivered <c>ChunkMesh</c>, which is the last thing Core owns before Godot.
    /// </para>
    /// </remarks>
    [Fact]
    public void ALampPutsBlockLightIntoADeliveredMeshsVertices()
    {
        using var harness = new PipelineHarness();
        harness.Pipeline.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
        harness.Settle();

        // Nothing emits yet, so no delivered vertex may carry any block light at all.
        Assert.False(AnyDeliveredMeshHasBlockLight(harness), "block light before any emitter");

        harness.Pipeline.SetVoxel(new VoxelPos(16, 16, 16), PipelineHarness.Lamp);
        harness.Settle();

        Assert.True(
            AnyDeliveredMeshHasBlockLight(harness),
            "a lamp lit nothing that reached a vertex — the pipeline is not handing the mesher "
            + "its light grid");
    }

    private static bool AnyDeliveredMeshHasBlockLight(PipelineHarness harness)
    {
        foreach (ChunkMesh mesh in harness.LastMesh.Values)
        {
            foreach (MeshBuffer buffer in new[] { mesh.Opaque, mesh.Transparent })
            {
                // Sky is byte 0 of each pair and block is byte 1 — see MeshBuffer.AddQuad.
                for (int vertex = 0; vertex < buffer.VertexCount; vertex++)
                {
                    if (buffer.Shades[(vertex * 2) + 1] > 0) return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Light moving under a chunk that has never drawn anything marks nothing.
    /// </summary>
    /// <remarks>
    /// The margin is the case: its light changes constantly as the fill proceeds and it has no
    /// mesh to invalidate. Marking it would put a chunk that can never satisfy the barrier onto
    /// the work list on every drain.
    /// </remarks>
    [Fact]
    public void LightMovingUnderAnUndrawnChunkMarksNothing()
    {
        using var harness = new PipelineHarness();
        ChunkStreamer streamer = harness.Pipeline.Streamer;
        streamer.AddViewer(ChunkPos.Zero, 1, 1);
        harness.Settle();

        foreach (StreamedChunk chunk in streamer.Chunks)
        {
            if (chunk.HasLiveMesh) continue;

            Assert.False(
                chunk.NeedsRemesh,
                $"{chunk.Position} has never drawn a mesh and is flagged for a rebuild");
        }
    }

    /// <summary>
    /// Digging a shaft down from open sky carries full daylight to the bottom of it.
    /// </summary>
    /// <remarks>
    /// <b>The end-to-end statement of what this milestone is for</b>, and the one that exercises
    /// the sky-height path rather than the conductance path: breaking through a column's topmost
    /// opaque voxel is what moves <see cref="SkyColumn"/>, which is not a local change at all —
    /// it moves the sky flag the whole way up and down the chunk column. M5-PLAN warns that a
    /// fuzz without this case passes for the wrong reason, and so would this suite.
    /// </remarks>
    [Fact]
    public void AShaftDugFromTheSurfaceCarriesDaylightToItsFloor()
    {
        using var harness = new PipelineHarness();
        ChunkStreamer streamer = harness.Pipeline.Streamer;
        streamer.AddViewer(ChunkPos.Zero, 1, 1);
        harness.Settle();

        // A column at the centre of the loaded box, from well above the surface to well below.
        const int x = 16;
        const int z = 16;
        int top = HighestSolid(harness, x, z);
        Assert.True(top > int.MinValue, "expected solid ground somewhere in the centre column");

        int floor = top - 24;
        for (int y = top; y >= floor; y--)
        {
            harness.Pipeline.SetVoxel(new VoxelPos(x, y, z), Air);
        }

        harness.Settle();

        // The cell holding the shaft's floor sees full daylight: skylight falls unattenuated
        // down a vertical opening, however deep. Without that exception a shaft dies fifteen
        // metres down, which is the opposite of what a mine is about.
        var floorCell = LightCellPos.FromVoxel(new VoxelPos(x, floor, z));
        Assert.Equal(ChunkLight.MaxLevel, Sky(harness, floorCell));

        AssertFixpoint(harness);
    }

    /// <summary>
    /// Capping a cell-wide shaft puts the dark back all the way down it, including in the chunks
    /// the cap is not in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The un-light pass through the pipeline, and the only test that reaches the cross-chunk
    /// half of the sky-height rebuild.</b> The battery found nothing that caught
    /// <c>QueueColumnLightBuild</c> being reduced to the edited chunk alone, and the reason is
    /// instructive: a <em>one-voxel</em> bore never makes any cell a sky source, because a cell
    /// is only open to the sky when all sixteen of its voxel columns are. Its light arrives by
    /// free fall from above instead, which conductance alone can take away.
    /// </para>
    /// <para>
    /// A <b>cell-wide</b> shaft is the shape that separates them: every cell down it is a sky
    /// source <em>in its own right</em>, so capping the top must clear the source flag in chunks
    /// nobody edited. Rebuild only the edited chunk and the shaft below stays at full daylight
    /// with a rock cap over it. Hence the assertion that the deep cell is in a different chunk
    /// from the cap — without it this test would quietly stop covering the thing it exists for.
    /// </para>
    /// </remarks>
    [Fact]
    public void CappingACellWideShaftPutsTheDarkBackInChunksTheCapIsNotIn()
    {
        using var harness = new PipelineHarness();
        harness.Pipeline.Streamer.AddViewer(ChunkPos.Zero, 1, 1);
        harness.Settle();

        // Cell-aligned, so the shaft is exactly one light cell across and every cell in it has
        // all sixteen of its voxel columns open.
        const int x0 = 16;
        const int z0 = 16;
        const int span = LightCellPos.VoxelsPerCell;

        // The highest solid voxel across all sixteen of the cell's columns, not just one of
        // them. The terrain is not flat, so digging from one column's surface leaves the taller
        // ones sealed — and a cell is open to the sky only when *every* column is, which is the
        // rule that makes this test mean anything.
        int top = int.MinValue;
        for (int dx = 0; dx < span; dx++)
        for (int dz = 0; dz < span; dz++)
        {
            top = Math.Max(top, HighestSolid(harness, x0 + dx, z0 + dz));
        }

        Assert.True(top > int.MinValue, "expected solid ground somewhere in the centre cell");

        int floor = top - 40;

        for (int y = top; y >= floor; y--)
        for (int dx = 0; dx < span; dx++)
        for (int dz = 0; dz < span; dz++)
        {
            harness.Pipeline.SetVoxel(new VoxelPos(x0 + dx, y, z0 + dz), Air);
        }

        harness.Settle();

        // Probed a whole cell clear of the shaft's floor, so every one of its sixteen voxel
        // columns is open and it is a sky source in its own right. The cell *containing* the
        // floor is not: the floor is where the downward scan stopped, so that cell's lowest
        // voxel sits below the open height and it is lit by free fall from above instead —
        // which conductance alone can take away, and which is how the first version of this
        // test managed to pass on broken code.
        var deepCell = LightCellPos.FromVoxel(new VoxelPos(x0, floor + (2 * span), z0));
        var capCell = LightCellPos.FromVoxel(new VoxelPos(x0, top, z0));
        Assert.NotEqual(capCell.Chunk, deepCell.Chunk);

        Assert.Equal(ChunkLight.MaxLevel, SkySource(harness, deepCell));
        Assert.Equal(ChunkLight.MaxLevel, Sky(harness, deepCell));

        // Cap it with one cell of stone at the top of the shaft.
        for (int dy = 0; dy < span; dy++)
        for (int dx = 0; dx < span; dx++)
        for (int dz = 0; dz < span; dz++)
        {
            harness.Pipeline.SetVoxel(new VoxelPos(x0 + dx, top - dy, z0 + dz), Stone);
        }

        harness.Settle();

        // The source flag, not just the value, and that is the whole point: the cap is in a
        // different chunk, so only the column rebuild can clear this. Assert the value alone and
        // a cell that stayed a source would sit at full daylight under a rock cap and still look
        // consistent to the fixpoint check, because the check reads the same stale flag.
        Assert.Equal(0, SkySource(harness, deepCell));
        Assert.Equal(0, Sky(harness, deepCell));
        AssertFixpoint(harness);
    }

    /// <summary>
    /// The M2c fuzz, now with a light stage in it: a random walk leaves nothing waiting.
    /// </summary>
    /// <remarks>
    /// <b>A light stage is exactly the kind of addition that leaves a chunk waiting on something
    /// that already happened</b> — the global drain going quiet pushes no chunk by itself, and
    /// the parked list is what stops that being a permanent stall. This runs the whole thing
    /// against real threads with viewers moving faster than the pipeline can keep up.
    /// </remarks>
    [Fact]
    public void ARandomWalkLeavesNoChunkWaitingOnLight()
    {
        var random = new Random(2026_08_30);
        using var harness = new PipelineHarness();
        ChunkStreamer streamer = harness.Pipeline.Streamer;
        ViewerId id = streamer.AddViewer(ChunkPos.Zero, 1, 1);

        for (int step = 0; step < 40; step++)
        {
            streamer.MoveViewer(id, new ChunkPos(
                random.Next(-3, 4), random.Next(-2, 3), random.Next(-3, 4)));

            for (int pump = random.Next(0, 3); pump > 0; pump--)
            {
                harness.Pipeline.Update();
            }
        }

        harness.Settle();

        Assert.Equal(0, harness.Pipeline.FaultedJobs);
        Assert.False(harness.Pipeline.LightInFlight);

        // Every visible chunk finished the whole ladder, light included.
        ChunkPos centre = streamer.GetViewer(id).Position;
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dz = -1; dz <= 1; dz++)
        {
            StreamedChunk chunk = streamer.Get(centre.Offset(dx, dy, dz))!;
            Assert.Equal(GenerationStage.Lit, chunk.Stage);
            Assert.Equal(ChunkState.Meshed, chunk.State);
        }

        // No column outlived its chunks, and none was dropped while chunks still shared it.
        Assert.All(streamer.Chunks, c => Assert.NotNull(c.Sky));

        AssertFixpoint(harness);
    }

    /// <summary>Highest solid voxel in one column, searched across the loaded chunks.</summary>
    private static int HighestSolid(PipelineHarness harness, int x, int z)
    {
        for (int y = 96; y >= -96; y--)
        {
            if (harness.Pipeline.TryGetMaterial(new VoxelPos(x, y, z), out int material)
                && material != Air)
            {
                return y;
            }
        }

        return int.MinValue;
    }

    /// <summary>Skylight in one cell, or 0 where there is no grid.</summary>
    private static int Sky(PipelineHarness harness, LightCellPos cell) =>
        harness.Pipeline.Streamer.LightAt(cell.Chunk)?.Get(cell.LocalIndex, LightChannel.Sky) ?? 0;

    /// <summary>
    /// What the cell is worth with no neighbours at all — full if it is open to the sky.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="Sky"/> in exactly the way that matters for the sky-height
    /// rebuild: a cell can hold full daylight because it <em>is</em> a source, or because one
    /// falls into it from above. Only the first is derived data, and only the first is what a
    /// column rebuild in another chunk can change.
    /// </remarks>
    private static int SkySource(PipelineHarness harness, LightCellPos cell) =>
        harness.Pipeline.Streamer.LightAt(cell.Chunk)?.Source(cell.LocalIndex, LightChannel.Sky) ?? 0;

    private static IEnumerable<LightCellPos> AllCells(PipelineHarness harness)
    {
        foreach (StreamedChunk chunk in harness.Pipeline.Streamer.Chunks)
        {
            if (chunk.Light is null) continue;

            for (int index = 0; index < LightCellPos.Volume; index++)
            {
                var (lx, ly, lz) = LightCellPos.FromIndex(index);
                yield return LightCellPos.FromChunkLocal(chunk.Position, lx, ly, lz);
            }
        }
    }

    internal static int[] CellValues(ChunkLight light)
    {
        var values = new int[LightCellPos.Volume];
        for (int index = 0; index < LightCellPos.Volume; index++)
        {
            values[index] = light.Get(index, LightChannel.Sky)
                            | (light.Get(index, LightChannel.Block) << 4);
        }

        return values;
    }

    private static Dictionary<ChunkPos, int[]> Snapshot(PipelineHarness harness) =>
        harness.Pipeline.Streamer.Chunks
            .Where(c => c.Light is not null)
            .ToDictionary(c => c.Position, c => CellValues(c.Light!));

    /// <summary>
    /// Whether any cell in <paramref name="position"/>'s padded 10³ sample window moved.
    /// </summary>
    /// <remarks>
    /// Stated over global cell coordinates −1…8 rather than by asking which chunks a changed
    /// cell reaches, so it is the inverse of what the pipeline computes rather than a copy of it.
    /// </remarks>
    private static bool SampledLightChanged(
        ChunkPos position,
        Dictionary<ChunkPos, int[]> before,
        Dictionary<ChunkPos, int[]> after)
    {
        for (int cz = -1; cz <= LightCellPos.CellsPerChunk; cz++)
        for (int cx = -1; cx <= LightCellPos.CellsPerChunk; cx++)
        for (int cy = -1; cy <= LightCellPos.CellsPerChunk; cy++)
        {
            LightCellPos cell = LightCellPos.FromChunkLocal(position, 0, 0, 0)
                .Offset(cx, cy, cz);

            if (!before.TryGetValue(cell.Chunk, out int[]? was)
                || !after.TryGetValue(cell.Chunk, out int[]? now))
            {
                continue;
            }

            if (was[cell.LocalIndex] != now[cell.LocalIndex]) return true;
        }

        return false;
    }

    /// <summary>
    /// M5b's oracle, applied to the grids the pipeline built.
    /// </summary>
    /// <remarks>
    /// <c>V = max(S, max over conducting neighbours (V(n) − attenuation))</c> has a unique
    /// fixpoint, so checking it cell by cell is complete correctness with no propagation in the
    /// check. The attenuation rule is written out again rather than called, for the reason
    /// <c>LightTestWorld.AssertFixpoint</c> gives: a reference that shares reasoning with its
    /// subject proves nothing.
    /// </remarks>
    internal static void AssertFixpoint(PipelineHarness harness)
    {
        ChunkStreamer streamer = harness.Pipeline.Streamer;

        foreach (StreamedChunk chunk in streamer.Chunks)
        {
            if (chunk.Light is not { HasDerived: true } light) continue;

            for (int index = 0; index < LightCellPos.Volume; index++)
            {
                var (lx, ly, lz) = LightCellPos.FromIndex(index);
                var cell = LightCellPos.FromChunkLocal(chunk.Position, lx, ly, lz);

                foreach (LightChannel channel in new[] { LightChannel.Sky, LightChannel.Block })
                {
                    int expected = light.Source(index, channel);

                    for (int face = 0; face < 6; face++)
                    {
                        if (!light.Conducts(index, face)) continue;

                        LightCellPos neighbour = LightTestWorld.Neighbour(cell, face);
                        ChunkLight? grid = streamer.LightAt(neighbour.Chunk);
                        if (grid is null) continue;

                        int value = grid.Get(neighbour.LocalIndex, channel);

                        bool freeFall = channel == LightChannel.Sky
                                        && value == ChunkLight.MaxLevel
                                        && cell.Y == neighbour.Y - 1;

                        int carried = freeFall ? value : value - 1;
                        if (carried > expected) expected = carried;
                    }

                    Assert.True(
                        expected == light.Get(index, channel),
                        $"{cell} {channel}: holds {light.Get(index, channel)}, "
                        + $"neighbours and its own source say {expected}");
                }
            }
        }
    }
}
