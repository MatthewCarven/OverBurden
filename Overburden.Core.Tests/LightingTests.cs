using Overburden.Core.Content;
using Overburden.Core.Lighting;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// M5b: the light grid, cell-face conductance, both propagations, and the un-light pass.
/// </summary>
public class LightingTests
{
    private const int Air = 0;
    private const int Stone = 1;
    private const int Glass = 2;
    private const int Lamp = 3;

    /// <summary>Stone occludes, glass does not, the lamp is a solid full-strength source.</summary>
    private static MaterialTable Materials() => MaterialTable.ForTesting(
        //             air    stone  glass  lamp
        isOpaque: [false, true, false, true],
        isFluid: [false, false, false, false],
        textureLayer: [0, 1, 2, 3],
        hardness: [0f, 3f, 1f, 0.5f],
        lightEmission: [0, 0, 0, 15]);

    /// <summary>A world with no terrain anywhere, so only block light is interesting.</summary>
    private static LightTestWorld Underground(int radius = 2)
    {
        var world = new LightTestWorld(
            new ChunkPos(-radius, -radius, -radius),
            new ChunkPos(radius, radius, radius),
            Materials(),
            // Far above the box, so nothing in it is open to the sky and the sky channel is
            // uniformly zero. A sky-lit box would still be correct, but every falloff assertion
            // would then need to say which channel it meant twice over.
            new FlatSurface(4096));

        world.Light();
        return world;
    }

    // ---------------------------------------------------------------- the cell grid

    [Fact]
    public void ALightCellIsAWholeNumberOfVoxelsAndDividesAChunk()
    {
        // The constraint is a correctness one, not a tuning knob: it is what makes a chunk
        // boundary always a cell boundary, and so what lets two independently lit chunks agree.
        // Asserted here as well as enforced in the static constructor, because a constant that
        // is currently right still needs the rule written down where it can be read.
        Assert.Equal(0, VoxelConstants.ChunkSize % LightCellPos.VoxelsPerCell);
        Assert.Equal(0, LightCellPos.VoxelsPerCell & (LightCellPos.VoxelsPerCell - 1));
        Assert.Equal(LightCellPos.VoxelsPerCell, 1 << LightCellPos.VoxelsPerCellLog2);
        Assert.Equal(LightCellPos.CellsPerChunk, 1 << LightCellPos.CellsPerChunkLog2);
        Assert.Equal(512, LightCellPos.Volume);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(31, 31, 31)]
    [InlineData(-1, -1, -1)]
    [InlineData(-32, 5, -33)]
    public void CellCoordinatesFloorTowardsNegativeInfinityLikeVoxelsDo(int x, int y, int z)
    {
        var cell = LightCellPos.FromVoxel(new VoxelPos(x, y, z));

        // Truncating instead of flooring puts voxel −1 in cell 0 alongside voxel 0, which is a
        // seam one cell wide through the origin — the same trap VoxelPos documents.
        Assert.Equal((int)MathF.Floor(x / (float)LightCellPos.VoxelsPerCell), cell.X);
        Assert.Equal((int)MathF.Floor(y / (float)LightCellPos.VoxelsPerCell), cell.Y);
        Assert.Equal((int)MathF.Floor(z / (float)LightCellPos.VoxelsPerCell), cell.Z);

        var (lx, ly, lz) = cell.Local;
        Assert.Equal(cell, LightCellPos.FromChunkLocal(cell.Chunk, lx, ly, lz));
        Assert.Equal(cell.LocalIndex, LightCellPos.Index(lx, ly, lz));
        Assert.Equal((lx, ly, lz), LightCellPos.FromIndex(cell.LocalIndex));
    }

    // ---------------------------------------------------------------- block light

    [Fact]
    public void ASourceInOpenSpaceFallsOffByOnePerMetreInEveryDirection()
    {
        // A 6-connected BFS has no freedom here, so this is an equality rather than a bound.
        LightTestWorld world = Underground(radius: 2);
        world.Edit(new VoxelPos(0, 0, 0), Lamp);

        for (int x = -16; x <= 16; x++)
        for (int y = -16; y <= 16; y++)
        for (int z = -16; z <= 16; z++)
        {
            var cell = new LightCellPos(x, y, z);
            if (world.LightAt(cell.Chunk) is null) continue;

            int manhattan = Math.Abs(x) + Math.Abs(y) + Math.Abs(z);
            Assert.Equal(Math.Max(0, ChunkLight.MaxLevel - manhattan), world.Block(cell));
        }

        world.AssertFixpoint();
    }

    [Fact]
    public void LightCrossesChunkBoundariesWithNoSeam()
    {
        // The point of a single global queue over cell coordinates: there is no per-chunk
        // boundary in the algorithm, so this needs no special case to be true.
        LightTestWorld world = Underground();
        world.Edit(new VoxelPos(-1, -1, -1), Lamp); // the cell just inside chunk (−1,−1,−1)

        var source = LightCellPos.FromVoxel(new VoxelPos(-1, -1, -1));
        Assert.Equal(ChunkLight.MaxLevel, world.Block(source));

        for (int step = 1; step <= 10; step++)
        {
            Assert.Equal(
                ChunkLight.MaxLevel - step,
                world.Block(source.Offset(step, 0, 0)));
        }

        Assert.NotEqual(source.Chunk, source.Offset(10, 0, 0).Chunk);
        world.AssertFixpoint();
    }

    // ---------------------------------------------------------------- conductance

    [Fact]
    public void AQuarterMetreWallBlocksLight()
    {
        // 0.25 m is a *normal* wall thickness in this world, and a cell is 1 m. Under "any air
        // voxel makes the cell open" this leaks straight through, which is the whole reason
        // conductance is a property of the face rather than of the cell.
        LightTestWorld world = Underground(radius: 1);

        // Voxel x = 4 is the first layer of cell 1, so the wall lands exactly on the boundary
        // between cell 0 and cell 1 and the sixteen voxel pairs across that face all have a
        // solid half. One voxel — 0.25 m.
        Wall(world, x: 4);
        world.Light();

        world.Edit(new VoxelPos(0, 0, 0), Lamp);

        Assert.Equal(ChunkLight.MaxLevel, world.Block(new LightCellPos(0, 0, 0)));
        Assert.Equal(0, world.Block(new LightCellPos(1, 0, 0)));
        Assert.Equal(0, world.Block(new LightCellPos(2, 0, 0)));

        world.AssertConductanceIsSymmetric();
        world.AssertFixpoint();
    }

    [Fact]
    public void AThreeVoxelBoreConductsAlongItsOwnAxis()
    {
        // hand_pick leaves a 0.75 m bore, which is narrower than one light cell. Under "any
        // opaque voxel makes the cell opaque" a tunnel never conducts and torches light nothing.
        LightTestWorld world = Underground(radius: 1);

        world.Fill(_ => Stone);
        for (int x = -32; x < 64; x++)
        for (int y = 0; y < 3; y++)
        for (int z = 0; z < 3; z++)
        {
            world.Place(new VoxelPos(x, y, z), Air);
        }

        world.Light();
        world.Edit(new VoxelPos(0, 1, 1), Lamp);

        var source = new LightCellPos(0, 0, 0);
        Assert.Equal(ChunkLight.MaxLevel, world.Block(source));

        for (int step = 1; step <= 5; step++)
        {
            Assert.Equal(ChunkLight.MaxLevel - step, world.Block(source.Offset(step, 0, 0)));
        }

        // ...and not out through the rock beside it.
        Assert.Equal(0, world.Block(source.Offset(0, 1, 0)));

        world.AssertConductanceIsSymmetric();
        world.AssertFixpoint();
    }

    // ---------------------------------------------------------------- skylight

    [Fact]
    public void AnOpenColumnIsFullSkylightToTheSurfaceAndDarkBelowIt()
    {
        LightTestWorld world = Surface(surfaceY: 8);

        // The cell holding the surface is lit: the cell above it is a source, the face between
        // them conducts, and full skylight falls through it for nothing.
        Assert.Equal(ChunkLight.MaxLevel, world.Sky(new LightCellPos(0, 3, 0)));
        Assert.Equal(ChunkLight.MaxLevel, world.Sky(new LightCellPos(0, 2, 0)));

        // One cell into solid rock and there is no conducting face at all.
        Assert.Equal(0, world.Sky(new LightCellPos(0, 1, 0)));
        Assert.Equal(0, world.Sky(new LightCellPos(0, 0, 0)));

        world.AssertFixpoint();
    }

    /// <summary>
    /// A grid that comes into existence after its neighbours were lit still receives their light.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The propagation pushes rather than pulls, so a push into a chunk with no grid is
    /// dropped</b> — correct while that chunk will never exist, and a lost update the moment it
    /// arrives a few frames later. Nothing else puts it back: the neighbour that tried has long
    /// since left both queues, and a new chunk seeding its own sources says nothing about light
    /// arriving from outside it.
    /// </para>
    /// <para>
    /// <b>M5b could not have found this and M5c found it as a one-in-six flake.</b> Every fixture
    /// here builds all its grids before lighting any of them, which is a luxury the streamer does
    /// not have — chunks arrive in whatever order the workers finish. The symptom was a chunk lit
    /// towards the neighbours derived after it and dark towards those derived before, so it moved
    /// with thread scheduling. This states it deterministically.
    /// </para>
    /// <para>
    /// <b>The scenario has to be a chunk with no source of its own, and the first attempt at this
    /// test was not.</b> It dropped the chunk under a sky shaft — where every cell is itself a
    /// full sky source, so rebuilding the derived data re-seeds the whole chunk and it relights
    /// with no help from anyone. It passed with <see cref="LightPropagator.SpreadInto"/> disabled.
    /// A lamp in the <em>neighbouring</em> chunk is the shape that separates the two: the restored
    /// chunk has nothing intrinsic anywhere in it, so every lit cell in it arrived from outside.
    /// </para>
    /// <para>
    /// <b>Break-recipe: make <see cref="LightPropagator.SpreadInto"/> do nothing.</b> The restored
    /// chunk comes back completely dark and the from-scratch comparison fails outright.
    /// </para>
    /// </remarks>
    [Fact]
    public void AGridThatArrivesAfterItsNeighboursWereLitStillReceivesTheirLight()
    {
        // No sky anywhere in this fixture, so a lamp is the only source in the world and the
        // chunk next door has nothing of its own to relight itself with.
        LightTestWorld world = Underground();
        world.Edit(new VoxelPos(0, 0, 0), Lamp);

        var next = new ChunkPos(1, 0, 0);
        var probe = new LightCellPos(LightCellPos.CellsPerChunk, 0, 0);
        int lit = world.Block(probe);
        Assert.True(lit > 0, "the lamp should reach into the neighbouring chunk");

        Dictionary<ChunkPos, byte[]> expected = world.Snapshot();

        // The chunk unloads and comes back, which is ordinary streamer behaviour and the cheapest
        // deterministic form of "this grid did not exist when its neighbour spread into it".
        world.DropLight(next);
        world.RestoreLight(next);

        Assert.Equal(lit, world.Block(probe));
        Assert.Equal(expected, world.Snapshot(), new GridComparer());
        world.AssertFixpoint();
    }

    /// <summary>
    /// The closed form for a uniform neighbourhood agrees with the general derivation, cell for
    /// cell and face for face.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two derivations of one field is two derivations that can drift</b>, which is exactly
    /// the trap <c>IsCaveAt</c> and <c>GenerateTerrain</c> are pinned against — and the symptom
    /// there was mining a wall that was already air. Here it would be a seam wherever a uniform
    /// chunk meets a mixed one, which is every cave mouth and every cliff.
    /// </para>
    /// <para>
    /// The fast path exists because it is <b>60–75% of the derive cost</b>: it skips the 34³
    /// voxel copy entirely, and the copy is the larger half. Both opaque and non-opaque, and both
    /// emitting and not, because the closed form states conductance and emission separately and a
    /// mistake in either is invisible in the other.
    /// </para>
    /// <para>
    /// <b>The surface runs through the middle of the chunk on purpose.</b> The sky flag is the
    /// one part the two paths genuinely share, so a fixture with uniform sky would agree even if
    /// the sharing were broken.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(Air)]
    [InlineData(Stone)]
    [InlineData(Glass)]
    [InlineData(Lamp)]
    public void TheUniformClosedFormAgreesWithTheGeneralDerivation(int material)
    {
        MaterialTable materials = Materials();
        var position = new ChunkPos(0, 0, 0);

        // Half the chunk open to the sky, so the sky flag varies from cell to cell.
        var sky = new SkyColumn(0, 0);
        sky.Seed(new FlatSurface(VoxelConstants.ChunkSize / 2));

        var padded = new PaddedChunk();
        padded.Fill(material);

        var general = new ChunkLight();
        general.BuildDerived(padded, materials, position, sky);

        var closedForm = new ChunkLight();
        closedForm.BuildDerivedUniform(material, materials, position, sky);

        for (int index = 0; index < LightCellPos.Volume; index++)
        {
            Assert.Equal(
                general.Source(index, LightChannel.Sky),
                closedForm.Source(index, LightChannel.Sky));

            Assert.Equal(
                general.Source(index, LightChannel.Block),
                closedForm.Source(index, LightChannel.Block));

            for (int face = 0; face < 6; face++)
            {
                Assert.Equal(general.Conducts(index, face), closedForm.Conducts(index, face));
            }
        }

        // Not vacuous: this material's cells actually do something, and the sky flag really varies.
        bool conducts = !materials.IsOpaque(material);
        Assert.Equal(conducts, closedForm.Conducts(0, 0));
        Assert.Contains(
            Enumerable.Range(0, LightCellPos.Volume),
            i => closedForm.Source(i, LightChannel.Sky) == ChunkLight.MaxLevel);
        Assert.Contains(
            Enumerable.Range(0, LightCellPos.Volume),
            i => closedForm.Source(i, LightChannel.Sky) == 0);
    }

    /// <summary>
    /// Both build paths report the same cells as changed, which is what the repair scope is.
    /// </summary>
    /// <remarks>
    /// The values agreeing is not enough on its own: <c>changed</c> is the contract the
    /// propagation depends on, and a fast path that computed the right word but reported the
    /// wrong set would leave cells un-repaired with every value looking correct at the moment it
    /// was written.
    /// </remarks>
    [Fact]
    public void BothBuildPathsReportTheSameChangedCells()
    {
        MaterialTable materials = Materials();
        var position = new ChunkPos(1, 0, -2);

        var sky = new SkyColumn(1, -2);
        sky.Seed(new FlatSurface(VoxelConstants.ChunkSize / 2));

        var padded = new PaddedChunk();
        padded.Fill(Lamp);

        var generalChanges = new List<LightCellPos>();
        new ChunkLight().BuildDerived(padded, materials, position, sky, generalChanges);

        var closedFormChanges = new List<LightCellPos>();
        new ChunkLight().BuildDerivedUniform(Lamp, materials, position, sky, closedFormChanges);

        Assert.NotEmpty(generalChanges);
        Assert.Equal(generalChanges, closedFormChanges);
    }

    [Fact]
    public void AShaftStaysFullyLitHoweverDeepItGoes()
    {
        // The no-attenuation-downward rule, and the reason for it: at −1 per metre a shaft would
        // die fifteen metres down, which is the opposite of what a mine is.
        // Carved raw rather than one incremental edit at a time: this test is about the
        // propagation rule, and the incremental path has its own oracle further down.
        LightTestWorld world = Surface(surfaceY: 8, radius: 3, light: false);

        for (int y = 8; y > -96; y--)
        for (int x = 0; x < 4; x++)
        for (int z = 0; z < 4; z++)
        {
            world.Place(new VoxelPos(x, y, z), Air);
        }

        // A horizontal branch off the shaft, four cells down. Without one there is nothing but
        // rock beside the shaft and the falloff assertion below reads 0 — which is the right
        // answer to a question the test was not asking.
        for (int x = 4; x < 24; x++)
        for (int y = -16; y <= -13; y++)
        for (int z = 0; z < 4; z++)
        {
            world.Place(new VoxelPos(x, y, z), Air);
        }

        // ...and a second shaft dropping from the far end of that branch. This is the only
        // shape that can tell the two skylight rules apart: the light arriving here is dim
        // rather than full, so whether it falls for free is exactly the question. A shaft
        // straight down from the surface cannot distinguish them, because full skylight falls
        // free under both.
        for (int x = 20; x < 24; x++)
        for (int y = -48; y <= -17; y++)
        for (int z = 0; z < 4; z++)
        {
            world.Place(new VoxelPos(x, y, z), Air);
        }

        world.RecolumnFromVoxels();
        world.Light();

        for (int cy = 2; cy >= -8; cy--)
        {
            Assert.Equal(ChunkLight.MaxLevel, world.Sky(new LightCellPos(0, cy, 0)));
        }

        // A side branch off the shaft is not free, or every lit patch in the world would hang a
        // column of light under it down to bedrock.
        Assert.Equal(ChunkLight.MaxLevel - 1, world.Sky(new LightCellPos(1, -4, 0)));
        Assert.Equal(ChunkLight.MaxLevel - 2, world.Sky(new LightCellPos(2, -4, 0)));

        // ...and the second shaft, fed at 10 rather than 15, attenuates on the way down. This
        // is the assertion that fails if the free fall is not restricted to full brightness:
        // every dimly lit patch in the world would otherwise hang a column of its own light all
        // the way to bedrock.
        Assert.Equal(ChunkLight.MaxLevel - 5, world.Sky(new LightCellPos(5, -4, 0)));
        Assert.Equal(ChunkLight.MaxLevel - 6, world.Sky(new LightCellPos(5, -5, 0)));
        Assert.Equal(ChunkLight.MaxLevel - 7, world.Sky(new LightCellPos(5, -6, 0)));

        // Rock directly under the horizontal branch stays dark, conducting nothing.
        Assert.Equal(0, world.Sky(new LightCellPos(2, -5, 0)));

        world.AssertFixpoint();
    }

    [Fact]
    public void BreakingThroughToDaylightLightsTheRoomBelow()
    {
        // The case the sky-height path exists for, and the one an analytic seed alone cannot
        // reach: the terrain says this column is closed, and the player has just made it open.
        LightTestWorld world = Surface(surfaceY: 8, radius: 2, light: false);

        // A sealed room under the surface. Raw, because it is the setup rather than the subject.
        for (int x = 0; x < 8; x++)
        for (int y = 0; y < 4; y++)
        for (int z = 0; z < 8; z++)
        {
            world.Place(new VoxelPos(x, y, z), Air);
        }

        world.Light();
        Assert.Equal(0, world.Sky(new LightCellPos(0, 0, 0)));

        for (int y = 4; y <= 8; y++)
        for (int x = 0; x < 4; x++)
        for (int z = 0; z < 4; z++)
        {
            world.Edit(new VoxelPos(x, y, z), Air);
        }

        // The column now says it is open to the floor of the room, which is the incremental
        // sky-height path doing its job — an analytic seed alone cannot know this.
        Assert.Equal(0, world.Column(0, 0).OpenAboveY(0, 0));
        Assert.Equal(ChunkLight.MaxLevel, world.Sky(new LightCellPos(0, 0, 0)));

        world.AssertSkyHeightsMatchAScan();
        world.AssertFixpoint();
    }

    // ---------------------------------------------------------------- the un-light pass

    [Fact]
    public void RemovingASourceTakesItsLightWithIt()
    {
        LightTestWorld world = Underground();
        world.Edit(new VoxelPos(0, 0, 0), Lamp);
        Assert.Equal(ChunkLight.MaxLevel, world.Block(new LightCellPos(0, 0, 0)));

        world.Edit(new VoxelPos(0, 0, 0), Air);

        for (int x = -8; x <= 8; x++)
        {
            Assert.Equal(0, world.Block(new LightCellPos(x, 0, 0)));
        }

        world.AssertFixpoint();
    }

    [Fact]
    public void RemovingOneOfTwoOverlappingSourcesLeavesTheOther()
    {
        // The dark patch this milestone exists to avoid. The un-light walk zeroes everything the
        // removed lamp was lighting, which includes cells the surviving lamp also lit; only the
        // re-seed step puts them back. Drop it and this is where it shows.
        LightTestWorld world = Underground();

        world.Edit(new VoxelPos(0, 0, 0), Lamp);
        world.Edit(new VoxelPos(24, 0, 0), Lamp);   // six cells away

        world.Edit(new VoxelPos(0, 0, 0), Air);

        Assert.Equal(ChunkLight.MaxLevel, world.Block(new LightCellPos(6, 0, 0)));
        Assert.Equal(ChunkLight.MaxLevel - 6, world.Block(new LightCellPos(0, 0, 0)));

        world.AssertFixpoint();
    }

    [Fact]
    public void ClosingAWallTakesTheLightOffTheFarSide()
    {
        // The case that needs the six neighbours: the face between the sealed cell and the one
        // beyond it stops conducting, so a walk that started only at the edited cell would never
        // reach the far side and its light would survive with nothing to remove it.
        //
        // Sealed across a corridor rather than across the world, so the block is sixteen voxels
        // instead of sixteen thousand. Light cannot go round either way.
        LightTestWorld world = Corridor();
        world.Edit(new VoxelPos(0, 1, 1), Lamp);

        Assert.Equal(ChunkLight.MaxLevel - 4, world.Block(new LightCellPos(4, 0, 0)));

        for (int y = 0; y < 4; y++)
        for (int z = 0; z < 4; z++)
        {
            world.Edit(new VoxelPos(16, y, z), Stone);
        }

        Assert.Equal(0, world.Block(new LightCellPos(4, 0, 0)));
        Assert.Equal(0, world.Block(new LightCellPos(5, 0, 0)));
        Assert.Equal(ChunkLight.MaxLevel - 3, world.Block(new LightCellPos(3, 0, 0)));

        world.AssertConductanceIsSymmetric();
        world.AssertFixpoint();
    }

    [Fact]
    public void SealingAFaceReportsBothOfItsCells()
    {
        // The property that lets Invalidate be one cell rather than seven. Conductance is
        // recorded on both cells of a face, so closing one reports both — which means the
        // caller's changed set already contains the far side and no walk has to guess at it.
        // Nothing else in the suite would notice this stopping being true.
        LightTestWorld world = Corridor();

        var reported = new HashSet<LightCellPos>();
        for (int y = 0; y < 4; y++)
        for (int z = 0; z < 4; z++)
        {
            foreach (LightCellPos cell in world.Edit(new VoxelPos(16, y, z), Stone))
            {
                reported.Add(cell);
            }
        }

        Assert.Contains(new LightCellPos(4, 0, 0), reported);
        Assert.Contains(new LightCellPos(3, 0, 0), reported);
    }

    // ---------------------------------------------------------------- the oracle

    /// <summary>
    /// The oracle. After any sequence of edits the incrementally maintained grid must equal a
    /// from-scratch flood, cell for cell — and must satisfy the propagation invariant locally.
    /// </summary>
    /// <remarks>
    /// <para>
    /// From-scratch propagation <em>is</em> the definition of the field; it is only too slow to
    /// run per frame. So there is no reference implementation here to get wrong, no tolerance and
    /// no sampling — it is an array comparison.
    /// </para>
    /// <para>
    /// <b>The edits deliberately break through the surface.</b> An edit sequence that stays
    /// underground never moves a column's sky height, so the analytic path is never exercised and
    /// the comparison passes for the wrong reason. The assertion at the end is what proves the
    /// sequence actually reached it.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void IncrementalLightEqualsAFromScratchFlood(int seed)
    {
        var rng = new Random(seed);
        LightTestWorld world = Surface(surfaceY: 8, radius: 1);

        int breakthroughs = 0;
        int lamps = 0;

        for (int edit = 0; edit < 120; edit++)
        {
            // Biased to a band straddling the surface, so carving keeps arriving at daylight
            // rather than tunnelling harmlessly in the dark.
            var voxel = new VoxelPos(rng.Next(-24, 24), rng.Next(-8, 14), rng.Next(-24, 24));

            int before = world.Column(voxel.Chunk.X, voxel.Chunk.Z)
                .OpenAboveY(voxel.Local.X, voxel.Local.Z);

            int material = rng.Next(10) switch
            {
                <= 5 => Air,
                6 or 7 => Stone,
                8 => Glass,
                _ => Lamp,
            };

            world.Edit(voxel, material);
            if (material == Lamp) lamps++;

            int after = world.Column(voxel.Chunk.X, voxel.Chunk.Z)
                .OpenAboveY(voxel.Local.X, voxel.Local.Z);
            if (after != before) breakthroughs++;
        }

        world.AssertConductanceIsSymmetric();
        world.AssertSkyHeightsMatchAScan();
        world.AssertFixpoint();

        Dictionary<ChunkPos, byte[]> incremental = world.Snapshot();
        world.Reflood();
        Dictionary<ChunkPos, byte[]> fromScratch = world.Snapshot();

        foreach ((ChunkPos chunk, byte[] expected) in fromScratch)
        {
            Assert.True(
                expected.AsSpan().SequenceEqual(incremental[chunk]),
                $"{chunk} disagrees with a from-scratch flood");
        }

        // Denominators, because a pass over a sequence that never reached the interesting paths
        // looks exactly like a pass over one that did.
        Assert.True(breakthroughs > 4, $"only {breakthroughs} edit(s) moved a column's sky height");
        Assert.True(lamps > 4, $"only {lamps} lamp(s) placed, so overlap was never tested");
    }

    [Fact]
    public void TwoIdenticalEditSequencesProduceBitwiseIdenticalLight()
    {
        // The multiplayer claim, same as M4a's: two machines running the same edits must agree
        // exactly, not approximately.
        Assert.Equal(Run(), Run(), new GridComparer());

        static Dictionary<ChunkPos, byte[]> Run()
        {
            LightTestWorld world = Surface(surfaceY: 8, radius: 1);
            var rng = new Random(99);

            for (int edit = 0; edit < 60; edit++)
            {
                world.Edit(
                    new VoxelPos(rng.Next(-16, 16), rng.Next(0, 12), rng.Next(-16, 16)),
                    rng.Next(3) switch { 0 => Air, 1 => Stone, _ => Lamp });
            }

            return world.Snapshot();
        }
    }

    private sealed class GridComparer : IEqualityComparer<Dictionary<ChunkPos, byte[]>>
    {
        public bool Equals(Dictionary<ChunkPos, byte[]>? a, Dictionary<ChunkPos, byte[]>? b) =>
            a is not null && b is not null && a.Count == b.Count
            && a.All(entry => b.TryGetValue(entry.Key, out byte[]? other)
                              && entry.Value.AsSpan().SequenceEqual(other));

        public int GetHashCode(Dictionary<ChunkPos, byte[]> value) => value.Count;
    }

    // ---------------------------------------------------------------- fixtures

    /// <summary>A world with flat terrain at <paramref name="surfaceY"/> and open sky above.</summary>
    private static LightTestWorld Surface(int surfaceY, int radius = 2, bool light = true)
    {
        var world = new LightTestWorld(
            new ChunkPos(-radius, -radius, -radius),
            new ChunkPos(radius, radius, radius),
            Materials(),
            new FlatSurface(surfaceY));

        world.Fill(voxel => voxel.Y <= surfaceY ? Stone : Air);
        if (light) world.Light();
        return world;
    }

    /// <summary>Solid rock with one cell-wide tunnel bored along X through the origin.</summary>
    private static LightTestWorld Corridor()
    {
        LightTestWorld world = new(
            new ChunkPos(-1, -1, -1),
            new ChunkPos(1, 1, 1),
            Materials(),
            new FlatSurface(4096));

        world.Fill(_ => Stone);

        for (int x = -32; x < 64; x++)
        for (int y = 0; y < 4; y++)
        for (int z = 0; z < 4; z++)
        {
            world.Place(new VoxelPos(x, y, z), Air);
        }

        world.Light();
        return world;
    }

    /// <summary>A solid plane one voxel thick at the given X, spanning the world.</summary>
    /// <remarks>
    /// Raw rather than edit by edit: this is setup, and 9,216 single-voxel edits each repairing
    /// the light around themselves was most of a five-minute test run.
    /// </remarks>
    private static void Wall(LightTestWorld world, int x)
    {
        for (int y = -32; y < 64; y++)
        for (int z = -32; z < 64; z++)
        {
            world.Place(new VoxelPos(x, y, z), Stone);
        }
    }
}
