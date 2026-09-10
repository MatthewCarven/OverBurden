using System.Diagnostics;
using Overburden.Core.Content;
using Overburden.Core.Fluid;
using Overburden.Core.Hydrology;
using Overburden.Core.Lighting;
using Overburden.Core.Streaming;
using Overburden.Core.Voxels;
using Xunit.Abstractions;

namespace Overburden.Core.Tests;

/// <summary>
/// M8c-1: where a lake could seep into an excavation, kept up to date as the world is dug, built
/// on, loaded and forgotten — and what a chunk's faces add up to under a table.
/// </summary>
/// <remarks>
/// <para>
/// The headline is <c>IncrementalEqualsFromScratchAfterAnySequenceOfScansEditsAndForgets</c>,
/// M8b-2b's oracle with the definition swapped: <see cref="SeepIndex.FacesOf"/> over every voxel of
/// every loaded chunk and its shell, against the bookkeeping, the delivery order and the groups.
/// The named tests around it each fix one clause, and each is the test a break-recipe falls to.
/// </para>
/// <para>
/// Heights are the topmost solid voxel: a surface of 40 means voxel 40 is rock and 41 the first
/// air. Rock is stone (0.02) below the surface's second voxel and dirt (0.35) in the top two,
/// unless a test places something else — so a shaft's groups mix permeabilities by Y.
/// </para>
/// </remarks>
public class SeepIndexTests(ITestOutputHelper output)
{
    private const float Stone = 0.02f;
    private const float Dirt = 0.35f;
    private const float Sealed = 0f;
    private const int Cell = HeightField.DefaultCellVoxels;

    /// <summary>A world with a seed surface, a loaded set, edits and two rocks, standing in for the streamer.</summary>
    private sealed class SeepWorld(ISurfaceHeights seed) : ISeepTerrain
    {
        public ISurfaceHeights Seed { get; } = seed;

        public HashSet<ChunkPos> Loaded { get; } = [];

        public HashSet<VoxelPos> Dug { get; } = [];

        /// <summary>Placed blocks and their permeability. Water may not be here.</summary>
        public Dictionary<VoxelPos, float> Placed { get; } = [];

        /// <summary>Holdable without being an edit — a natural cave.</summary>
        public HashSet<VoxelPos> Natural { get; } = [];

        public bool CanHold(VoxelPos v) =>
            Loaded.Contains(v.Chunk)
            && !Placed.ContainsKey(v)
            && (Dug.Contains(v) || Natural.Contains(v) || v.Y > Seed.SurfaceHeightAt(v.X, v.Z));

        public bool TryGetRock(VoxelPos v, out float permeability)
        {
            if (!Loaded.Contains(v.Chunk) || CanHold(v))
            {
                permeability = 0f;
                return false;
            }

            permeability = Placed.TryGetValue(v, out float placed) ? placed : NaturalRock(v);
            return true;
        }

        /// <summary>What the ground is made of: dirt in the top two voxels, stone below.</summary>
        public float NaturalRock(VoxelPos v) => v.Y >= Seed.SurfaceHeightAt(v.X, v.Z) - 1 ? Dirt : Stone;

        /// <summary>Whether to offer the scan a classified block, built from the per-voxel answers.</summary>
        public bool Bulk { get; set; }

        /// <summary>Blocks handed out, so a test can assert which path the scan took.</summary>
        public int BlocksRead { get; private set; }

        public bool TryReadNeighbourhood(ChunkPos chunk, SeepNeighbourhood into)
        {
            if (!Bulk)
            {
                return false;
            }

            BlocksRead++;
            VoxelPos origin = chunk.OriginVoxel;
            for (int z = -1; z <= VoxelConstants.ChunkSize; z++)
            {
                for (int x = -1; x <= VoxelConstants.ChunkSize; x++)
                {
                    for (int y = -1; y <= VoxelConstants.ChunkSize; y++)
                    {
                        var v = new VoxelPos(origin.X + x, origin.Y + y, origin.Z + z);
                        into[x, y, z] = !Loaded.Contains(v.Chunk) ? SeepNeighbourhood.Unloaded
                            : CanHold(v) ? SeepNeighbourhood.Holdable
                            : TryGetRock(v, out float permeability) ? permeability
                            : SeepNeighbourhood.Unloaded;
                    }
                }
            }

            return true;
        }

        public void Dig(SeepIndex index, VoxelPos v)
        {
            Placed.Remove(v);
            Dug.Add(v);
            index.OnVoxelChanged(v);
        }

        public void Place(SeepIndex index, VoxelPos v, float permeability)
        {
            Dug.Remove(v);
            Placed[v] = permeability;
            index.OnVoxelChanged(v);
        }

        public void Load(SeepIndex index, ChunkPos chunk)
        {
            Loaded.Add(chunk);
            index.Scan(chunk);
        }

        public void Unload(SeepIndex index, ChunkPos chunk)
        {
            Loaded.Remove(chunk);
            index.Forget(chunk);
        }
    }

    /// <summary>A table at one level over every sample, or nowhere.</summary>
    private sealed class FlatTable(double? level) : IWaterTable
    {
        public double? TableAt(int sampleX, int sampleZ) => level;
    }

    private static ISurfaceHeights Flat(int y) => new AnalyticSurface((_, _) => y);

    /// <summary>The chunk column (0, 0)'s chunk holding Y 32..63, where every fixture's surface sits.</summary>
    private static readonly ChunkPos SurfaceChunk = new(0, 1, 0);

    private static SeepFace Face(VoxelPos inside, int dx, int dy, int dz, float permeability) =>
        new(inside, inside.Offset(dx, dy, dz), permeability);

    [Fact]
    public void DiggingUnderALakeBedOpensAFaceThroughEveryRockNeighbourAndNoneThroughAir()
    {
        // One voxel dug two below the surface has six rock neighbours, so six faces: the floor
        // and four walls in stone, the roof in the dirt band. Digging the roof out trades the pair
        // for the roof voxel's own faces; digging the surface voxel out leaves it four walls,
        // because the voxel above it is the lake's air and air is nobody's rock.
        var world = new SeepWorld(Flat(40));
        var index = new SeepIndex(world.Seed, world);
        world.Load(index, SurfaceChunk);
        Assert.Equal(0, index.Count);

        var deep = new VoxelPos(5, 38, 5);
        world.Dig(index, deep);
        Assert.Equal(6, index.Count);
        Assert.True(index.Contains(Face(deep, 0, -1, 0, Stone)));
        Assert.True(index.Contains(Face(deep, 1, 0, 0, Stone)));
        Assert.True(index.Contains(Face(deep, 0, 1, 0, Dirt)));
        Assert.Equal(1, index.ChunkCount);

        var above = deep with { Y = 39 };
        world.Dig(index, above);
        Assert.False(index.Contains(Face(deep, 0, 1, 0, Dirt)));
        Assert.True(index.Contains(Face(above, 0, 1, 0, Dirt)));
        Assert.Equal(10, index.Count);

        var surfaceVoxel = deep with { Y = 40 };
        world.Dig(index, surfaceVoxel);
        Assert.False(index.Contains(Face(above, 0, 1, 0, Dirt)));
        Assert.Equal(13, index.Count);

        var found = new List<SeepFace>();
        index.FacesOf(surfaceVoxel, found);
        Assert.Equal(4, found.Count);
        Assert.All(found, f => Assert.False(f.IsFloor || f.IsCeiling));
    }

    [Fact]
    public void AVoxelAboveItsSeedSurfaceIsNeverAnInside()
    {
        // A block placed in the lake's own volume has air on five sides at or above the plane; none
        // of that air is an excavation, so the block is nobody's rock and the placement files
        // nothing. The definition says the same directly.
        var world = new SeepWorld(Flat(40));
        var index = new SeepIndex(world.Seed, world);
        world.Load(index, SurfaceChunk);

        var found = new List<SeepFace>();
        index.FacesOf(new VoxelPos(5, 41, 5), found);
        Assert.Empty(found);

        world.Place(index, new VoxelPos(5, 45, 5), Stone);
        Assert.Equal(0, index.Count);

        // The surface voxel itself is an inside once dug — four walls and a floor; the block
        // above it, in the lake, is then its roof — a cap over a hole, which does seep.
        world.Dig(index, new VoxelPos(5, 40, 5));
        Assert.Equal(5, index.Count);
        world.Place(index, new VoxelPos(5, 41, 5), Stone);
        Assert.Equal(6, index.Count);
        Assert.True(index.Contains(Face(new VoxelPos(5, 40, 5), 0, 1, 0, Stone)));

        // And the edit path's own copy of the clause: a block placed in the lake and taken out
        // again leaves a holdable voxel above the surface with undug rock beside it — the
        // battery's S2, which the first fixture never produced because nothing dug above ground.
        world.Place(index, new VoxelPos(9, 41, 9), Stone);
        world.Dig(index, new VoxelPos(9, 41, 9));
        Assert.Equal(6, index.Count);
        Assert.False(index.Contains(Face(new VoxelPos(9, 41, 9), 0, -1, 0, Dirt)));
    }

    [Fact]
    public void SealedRockIsNotAFaceAndADifferentRockIsADifferentFace()
    {
        // The permeability clause, both ways: a rock that passes nothing is no face — the room
        // has fewer faces, not a zero term — and the value is part of the face, so re-lining the
        // floor with dirt replaces the stone face rather than leaving a stale one.
        var world = new SeepWorld(Flat(40));
        var index = new SeepIndex(world.Seed, world);
        world.Load(index, SurfaceChunk);

        var room = new VoxelPos(5, 38, 5);
        var floor = room with { Y = 37 };
        world.Dig(index, room);
        Assert.Equal(6, index.Count);

        world.Dig(index, floor);
        Assert.Equal(10, index.Count);
        Assert.False(index.Contains(Face(room, 0, -1, 0, Stone)));

        world.Place(index, floor, Sealed);
        Assert.Equal(5, index.Count);
        Assert.False(index.Contains(Face(room, 0, -1, 0, Sealed)));
        Assert.False(index.Contains(Face(room, 0, -1, 0, Stone)));

        // The definition itself, asked directly (the oracle shares it, so only a direct read
        // can see it file sealed rock), and the edit path's own copy: a voxel dug beside the
        // sealed block gets faces through everything but it.
        var found = new List<SeepFace>();
        index.FacesOf(room, found);
        Assert.Equal(5, found.Count);
        Assert.DoesNotContain(found, f => f.Rock == floor);

        var beside = floor with { X = floor.X + 1 };
        world.Dig(index, beside);
        Assert.Equal(10, index.Count);
        Assert.False(index.Contains(Face(beside, -1, 0, 0, Sealed)));
        world.Place(index, beside, Stone);
        Assert.Equal(5, index.Count);

        world.Place(index, floor, Dirt);
        Assert.Equal(6, index.Count);
        Assert.True(index.Contains(Face(room, 0, -1, 0, Dirt)));
        Assert.False(index.Contains(Face(room, 0, -1, 0, Stone)));

        // A placed block's own faces, as an inside, are gone with the block: the voxels around
        // it are rock, but nothing arrives in rock.
        found.Clear();
        index.FacesOf(floor, found);
        Assert.Empty(found);
    }

    [Fact]
    public void AFaceAcrossAChunkBoundaryIsFoundByWhicheverHalfArrivesSecondAndForgottenWithEither()
    {
        // The inside is the last column of chunk 0 and its east wall is the first of chunk 1. An
        // unloaded voxel is nobody's rock, so the face exists only while both are here — and the
        // scan of whichever arrives second has to find it, from either side.
        var west = new ChunkPos(0, 1, 0);
        var east = new ChunkPos(1, 1, 0);
        var inside = new VoxelPos(31, 38, 5);
        SeepFace crossing = Face(inside, 1, 0, 0, Stone);

        var world = new SeepWorld(Flat(40));
        var index = new SeepIndex(world.Seed, world);
        world.Load(index, west);
        world.Dig(index, inside);
        Assert.Equal(5, index.Count);
        Assert.False(index.Contains(crossing));

        world.Load(index, east);
        Assert.Equal(6, index.Count);
        Assert.True(index.Contains(crossing));
        Assert.Equal(1, index.ChunkCount);

        world.Unload(index, east);
        Assert.Equal(5, index.Count);
        Assert.False(index.Contains(crossing));
        world.Load(index, east);
        Assert.Equal(6, index.Count);

        world.Unload(index, west);
        Assert.Equal(0, index.Count);
        Assert.Equal(0, index.ChunkCount);

        // The other order: the rock's chunk is here first and the inside's arrives already dug.
        var reversed = new SeepWorld(Flat(40));
        var reversedIndex = new SeepIndex(reversed.Seed, reversed);
        reversed.Dug.Add(inside);
        reversed.Load(reversedIndex, east);
        Assert.Equal(0, reversedIndex.Count);
        reversed.Load(reversedIndex, west);
        Assert.Equal(6, reversedIndex.Count);
        Assert.True(reversedIndex.Contains(crossing));

        // A face whose inside is in the rock's chunk's neighbour and whose rock is in the chunk
        // being forgotten: the second listing, by rock chunk, is what finds it.
        reversed.Unload(reversedIndex, east);
        Assert.Equal(5, reversedIndex.Count);
    }

    [Fact]
    public void AVerticalFaceAcrossAChunkBoundaryIsScannedFromBothSides()
    {
        // The floor of chunk Y 1 against the roof row of chunk Y 0, and the roof of chunk Y 1
        // against the floor row of chunk Y 2 — the two shell slabs Scan visits with one face each.
        var lower = new ChunkPos(0, 0, 0);
        var middle = new ChunkPos(0, 1, 0);
        var upper = new ChunkPos(0, 2, 0);

        var world = new SeepWorld(Flat(70));
        var index = new SeepIndex(world.Seed, world);
        var bottom = new VoxelPos(5, 32, 5);
        var top = new VoxelPos(5, 63, 5);
        world.Dug.Add(bottom);
        world.Dug.Add(top);

        world.Load(index, middle);
        Assert.Equal(10, index.Count);

        world.Load(index, lower);
        Assert.True(index.Contains(Face(bottom, 0, -1, 0, Stone)));
        world.Load(index, upper);
        Assert.True(index.Contains(Face(top, 0, 1, 0, Stone)));
        Assert.Equal(12, index.Count);

        // And an inside in the shell above: a dug voxel at the bottom of the upper chunk whose
        // floor is the middle chunk's top row, filed under the upper chunk.
        var shelf = new VoxelPos(9, 64, 9);
        world.Dig(index, shelf);
        Assert.True(index.Contains(Face(shelf, 0, -1, 0, Stone)));
        Assert.Equal(new ChunkPos(0, 2, 0), shelf.Chunk);
        Assert.Contains(index.FacesIn(upper), f => f.Inside == shelf);

        world.Unload(index, middle);
        Assert.False(index.Contains(Face(shelf, 0, -1, 0, Stone)));
        Assert.Equal(5, index.Count);

        // The one column the scan's "surface below the chunk" exit must not skip: a surface at
        // the row under the chunk's floor, its surface voxel dug and capped by a block placed in
        // the chunk's floor row. The inside is in the shell below and its roof is the cap.
        var capped = new SeepWorld(Flat(31));
        var cappedIndex = new SeepIndex(capped.Seed, capped);
        var mouth = new VoxelPos(5, 31, 5);
        capped.Dug.Add(mouth);
        capped.Placed[new VoxelPos(5, 32, 5)] = Stone;
        capped.Load(cappedIndex, lower);
        Assert.Equal(5, cappedIndex.Count);
        capped.Load(cappedIndex, middle);
        Assert.True(cappedIndex.Contains(Face(mouth, 0, 1, 0, Stone)));
        Assert.Equal(6, cappedIndex.Count);
    }

    [Fact]
    public void TheRealTerrainAnswersRockFromTheStreamerAndTheTable()
    {
        // The wire: VoxelFluidTerrain's second question over a streamer, with air, rock, a fluid
        // material, a chunk still generating and one that is not tracked. Rock is loaded and not
        // holdable; a fluid is holdable and so nobody's rock; unloaded is neither, whatever the
        // material would have been.
        var streamer = new ChunkStreamer(loadMargin: 0);
        streamer.AddViewer(ChunkPos.Zero, 2, 1);
        const int stone = 2;
        const int water = 3;
        streamer.Get(new ChunkPos(0, 0, 0))!.Voxels = new VoxelChunk(MaterialIds.AirNumericId);
        streamer.Get(new ChunkPos(1, 0, 0))!.Voxels = new VoxelChunk(stone);
        streamer.Get(new ChunkPos(-1, 0, 0))!.Voxels = new VoxelChunk(water);
        Assert.NotNull(streamer.Get(new ChunkPos(2, 0, 0)));
        Assert.Null(streamer.Get(new ChunkPos(9, 0, 0)));

        var terrain = new VoxelFluidTerrain(streamer, MaterialTable.ForTesting(
            isOpaque: [false, true, true, false],
            isFluid: [false, false, false, true],
            textureLayer: [0, 1, 2, 3],
            hardness: [0f, 0.8f, 3.0f, 0f],
            permeability: [1f, 0.35f, 0.02f, 1f]));

        Assert.False(terrain.TryGetRock(new VoxelPos(5, 5, 5), out _));
        Assert.True(terrain.TryGetRock(new VoxelPos(40, 5, 5), out float permeability));
        Assert.Equal(0.02f, permeability);
        Assert.False(terrain.TryGetRock(new VoxelPos(-5, 5, 5), out _));
        Assert.False(terrain.TryGetRock(new VoxelPos(70, 5, 5), out _));
        Assert.False(terrain.TryGetRock(new VoxelPos(300, 5, 5), out _));

        // The bulk read through the same gate: the block over chunk (0, 0, 0) sees stone to the
        // east, water to the west, and nothing loaded above, below or across the generating one.
        var block = new SeepNeighbourhood();
        Assert.True(terrain.TryReadNeighbourhood(ChunkPos.Zero, block));
        Assert.Equal(SeepNeighbourhood.Holdable, block[5, 5, 5]);
        Assert.Equal(0.02f, block[32, 5, 5]);
        Assert.Equal(SeepNeighbourhood.Holdable, block[-1, 5, 5]);
        Assert.Equal(SeepNeighbourhood.Unloaded, block[5, 32, 5]);
        Assert.Equal(SeepNeighbourhood.Unloaded, block[5, -1, 5]);

        // A chunk whose water is still landing is not loaded to the water in the block either.
        var pending = new SeepNeighbourhood();
        streamer.Get(new ChunkPos(1, 0, 0))!.FluidPending = true;
        Assert.True(terrain.TryReadNeighbourhood(ChunkPos.Zero, pending));
        Assert.Equal(SeepNeighbourhood.Unloaded, pending[32, 5, 5]);
        Assert.Null(streamer.VoxelsForFluid(new ChunkPos(1, 0, 0)));
        Assert.NotNull(streamer.VoxelsForFluid(ChunkPos.Zero));

        // A world that is only an IVoxelWorld has no bulk path and says so.
        var plain = new VoxelFluidTerrain(new PlainWorld(), Content.MaterialTable.ForTesting(
            isOpaque: [false], isFluid: [false], textureLayer: [0], hardness: [0f]));
        Assert.False(plain.TryReadNeighbourhood(ChunkPos.Zero, new SeepNeighbourhood()));
    }

    private sealed class PlainWorld : IVoxelWorld
    {
        public bool TryGetMaterial(VoxelPos voxel, out int materialId)
        {
            materialId = 0;
            return true;
        }
    }

    [Fact]
    public void FacesComeOutLowestFirstAndFloorBeforeWallsAndTheGroupsSumThem()
    {
        // The delivery order, decision 5: a floor spring before a wall weep. A shaft three deep
        // with a side branch at its foot: the first face handed out is at the shaft's foot, and
        // it is the floor.
        var world = new SeepWorld(Flat(40));
        var index = new SeepIndex(world.Seed, world);
        world.Load(index, SurfaceChunk);

        for (int y = 40; y >= 37; y--)
        {
            world.Dig(index, new VoxelPos(5, y, 5));
        }

        world.Dig(index, new VoxelPos(6, 37, 5));
        world.Dig(index, new VoxelPos(7, 37, 5));

        IReadOnlyList<SeepFace> faces = index.FacesIn(SurfaceChunk);
        Assert.Equal(index.Count, faces.Count);
        Assert.Equal(37, faces[0].Y);
        Assert.True(faces[0].IsFloor, $"the first face is {faces[0]}, not a floor");
        for (int i = 1; i < faces.Count; i++)
        {
            Assert.True(faces[i - 1].Y <= faces[i].Y, "not lowest first");
        }

        // The floor of the branch is three stone faces at Y 37 in one sample; the shaft's walls
        // at Y 40 and 39 are dirt and at 38, 37 stone. Groups sum exactly what the faces hold.
        IReadOnlyList<SeepGroup> groups = index.GroupsIn(SurfaceChunk);
        foreach (SeepGroup group in groups)
        {
            double expected = faces
                .Where(f => f.Y == group.Y && index.SampleOf(f.Inside) == (group.SampleX, group.SampleZ))
                .Sum(f => (double)f.Permeability);
            Assert.Equal(expected, group.Permeability, 12);
        }

        Assert.Equal(faces.Sum(f => (double)f.Permeability), groups.Sum(g => g.Permeability), 12);
        Assert.Contains(groups, g => g.Y == 40 && g.Permeability == 4 * (double)Dirt);
        Assert.Contains(groups, g => g.Y == 37);
    }

    [Fact]
    public void TheCoefficientIsHeadTimesPermeabilityTimesAreaAndNothingAtOrAboveTheTable()
    {
        // One voxel dug two below a surface of 40: six faces at Y 38, five stone and a dirt roof.
        // A table at 40.5 stands two voxels — half a metre — over the voxel's centre.
        var world = new SeepWorld(Flat(40));
        var index = new SeepIndex(world.Seed, world);
        world.Load(index, SurfaceChunk);
        world.Dig(index, new VoxelPos(5, 38, 5));

        double perm = (5 * (double)Stone) + Dirt;
        double expected = perm * 0.5 * SeepIndex.FaceAreaSquareMetres;
        Assert.Equal(expected, index.Coefficient(SurfaceChunk, new FlatTable(40.5)), 12);

        // At the voxel's centre, and below it: nothing. The wall between equal levels passes
        // nothing either way, and a face above the table is dry rock.
        Assert.Equal(0, index.Coefficient(SurfaceChunk, new FlatTable(38.5)));
        Assert.Equal(0, index.Coefficient(SurfaceChunk, new FlatTable(30)));
        Assert.Equal(0, index.Coefficient(SurfaceChunk, new FlatTable(null)));
        Assert.Equal(0, index.Coefficient(new ChunkPos(7, 7, 7), new FlatTable(40.5)));

        // The plan's own arithmetic, as a check on the units: 160 m² of stone under 5 m of head
        // at the shipped scale is about a litre a second.
        double faces = 160 / SeepIndex.FaceAreaSquareMetres;
        double coefficient = faces * Stone * 5 * SeepIndex.FaceAreaSquareMetres;
        Assert.Equal(0.96, SeepIndex.LitresPerSecond(coefficient, SeepIndex.DefaultSeepageScale), 6);
    }

    [Fact]
    public void TheTableIsReadPerSampleSoAChunkOnADivideSeepsOnOneSideOnly()
    {
        // Two voxels dug at Y 38 in different samples of one chunk: a table that stands over one
        // sample and not the other charges only that sample's faces.
        var world = new SeepWorld(Flat(40));
        var index = new SeepIndex(world.Seed, world);
        world.Load(index, SurfaceChunk);
        var wet = new VoxelPos(2, 38, 2);
        var dry = new VoxelPos(2 + Cell, 38, 2);
        world.Dig(index, wet);
        world.Dig(index, dry);
        Assert.NotEqual(index.SampleOf(wet), index.SampleOf(dry));

        var table = new SampleTable(index.SampleOf(wet), 42.5);
        double perm = (5 * (double)Stone) + Dirt;
        Assert.Equal(perm * 1.0 * SeepIndex.FaceAreaSquareMetres, index.Coefficient(SurfaceChunk, table), 12);
    }

    private sealed class SampleTable((int X, int Z) wet, double level) : IWaterTable
    {
        public double? TableAt(int sampleX, int sampleZ) => (sampleX, sampleZ) == wet ? level : null;
    }

    [Fact]
    public void ARemainderIsBoundedAtOneCellRefusedWithoutFacesAndLostWithTheChunk()
    {
        var world = new SeepWorld(Flat(40));
        var index = new SeepIndex(world.Seed, world);
        world.Load(index, SurfaceChunk);

        Assert.Equal(0, index.RemainderMillilitres(SurfaceChunk));
        Assert.Throws<InvalidOperationException>(() => index.SetRemainderMillilitres(SurfaceChunk, 1));

        world.Dig(index, new VoxelPos(5, 38, 5));
        index.SetRemainderMillilitres(SurfaceChunk, FluidConstants.CellCapacityMillilitres);
        Assert.Equal(FluidConstants.CellCapacityMillilitres, index.RemainderMillilitres(SurfaceChunk));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => index.SetRemainderMillilitres(SurfaceChunk, FluidConstants.CellCapacityMillilitres + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => index.SetRemainderMillilitres(SurfaceChunk, -1));

        // Filling the hole back in leaves the chunk with no faces, and a debt against no faces
        // is dropped with them.
        world.Place(index, new VoxelPos(5, 38, 5), Stone);
        Assert.Equal(0, index.Count);
        Assert.Equal(0, index.RemainderMillilitres(SurfaceChunk));

        world.Dig(index, new VoxelPos(5, 38, 5));
        index.SetRemainderMillilitres(SurfaceChunk, 500);
        world.Unload(index, SurfaceChunk);
        Assert.Equal(0, index.RemainderMillilitres(SurfaceChunk));
    }

    [Fact]
    public void ScanningTwiceAddsNothing()
    {
        var world = new SeepWorld(Flat(40));
        var index = new SeepIndex(world.Seed, world);
        world.Load(index, SurfaceChunk);
        world.Dig(index, new VoxelPos(5, 38, 5));
        world.Dig(index, new VoxelPos(9, 36, 9));

        Assert.Equal(0, index.Scan(SurfaceChunk));
        Assert.Equal(12, index.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncrementalEqualsFromScratchAfterAnySequenceOfScansEditsAndForgets(bool bulk)
    {
        // THE ORACLE. From scratch is the definition (FacesOf) over every voxel of every loaded
        // chunk and the one-voxel shell around it — a face touches a chunk from either side —
        // against the bookkeeping, the per-chunk delivery order and the groups. Twice: with the
        // scan reading per voxel, and with the scan reading a classified block (the game's path
        // since M8c-2), against the same per-voxel definition.
        var random = new Random(20260911);
        ChunkPos[] chunks =
        [
            new(0, 1, 0), new(1, 1, 0), new(0, 1, 1), new(1, 1, 1), new(0, 0, 0),
        ];
        float[] rocks = [Sealed, Stone, Dirt];

        int checks = 0;
        int checksWithCrossings = 0;
        int checksWithMixedGroups = 0;
        int checksAfterUnload = 0;

        for (int trial = 0; trial < 3; trial++)
        {
            // A hill with terraces and a cliff line, mostly inside chunk Y 1 (32..63), dipping
            // toward chunk Y 0 so the vertical shell slabs see faces too.
            int salt = trial * 7919;
            var seed = new AnalyticSurface((x, z) =>
                34 + (Hash((x >> 2) + salt, z >> 2) % 5) + (x >= 40 ? 0 : 4));

            // The dug rule bound to a set the ops move: a chunk is dug from its first dig on,
            // as the tracker has it, and a natural hollow in an undug chunk is never an inside.
            var dugChunks = new HashSet<ChunkPos>();
            var world = new SeepWorld(seed) { Bulk = bulk };
            var index = new SeepIndex(seed, world) { IsDug = dugChunks.Contains };
            world.Natural.Add(new VoxelPos(20, 36, 20));
            world.Natural.Add(new VoxelPos(44, 34, 40));
            world.Natural.Add(new VoxelPos(10, 31, 10));
            world.Natural.Add(new VoxelPos(12, 32, 12));
            world.Natural.Add(new VoxelPos(32, 36, 20));
            world.Natural.Add(new VoxelPos(20, 36, 32));
            world.Natural.Add(new VoxelPos(31, 38, 40));
            world.Load(index, chunks[0]);
            bool unloadedRecently = false;

            for (int op = 0; op < 200; op++)
            {
                int kind = random.Next(100);
                ChunkPos chunk = chunks[random.Next(chunks.Length)];
                int x = (chunk.X << VoxelConstants.ChunkSizeLog2) + random.Next(VoxelConstants.ChunkSize);
                int z = (chunk.Z << VoxelConstants.ChunkSizeLog2) + random.Next(VoxelConstants.ChunkSize);
                int s = seed.SurfaceHeightAt(x, z);

                if (kind < 45)
                {
                    // Mostly under the surface, sometimes just above it: a block placed in the
                    // lake and dug out again is the edit path's "above the surface" clause.
                    if (world.Loaded.Contains(chunk))
                    {
                        var target = new VoxelPos(x, s + 2 - random.Next(10), z);
                        if (dugChunks.Add(target.Chunk) && world.Loaded.Contains(target.Chunk))
                        {
                            // The tracker's rule: the first edit in a chunk scans it.
                            world.Dug.Add(target);
                            index.OnVoxelChanged(target);
                            index.Scan(target.Chunk);
                        }
                        else
                        {
                            world.Dig(index, target);
                        }
                    }
                }
                else if (kind < 55)
                {
                    // Beside a placed block, so sealed and re-lined rock meet the edit path.
                    if (world.Placed.Count > 0)
                    {
                        VoxelPos placed = world.Placed.Keys.ElementAt(random.Next(world.Placed.Count));
                        (int dx, int dy, int dz) = Sides[random.Next(Sides.Length)];
                        VoxelPos target = placed.Offset(dx, dy, dz);
                        if (world.Loaded.Contains(target.Chunk))
                        {
                            bool newlyDug = dugChunks.Add(target.Chunk);
                            world.Dig(index, target);
                            if (newlyDug)
                            {
                                index.Scan(target.Chunk);
                            }
                        }
                    }
                }
                else if (kind < 72)
                {
                    if (world.Loaded.Contains(chunk))
                    {
                        var target = new VoxelPos(x, s - 4 + random.Next(7), z);
                        bool newlyDug = dugChunks.Add(target.Chunk);
                        world.Place(index, target, rocks[random.Next(rocks.Length)]);
                        if (newlyDug)
                        {
                            index.Scan(target.Chunk);
                        }
                    }
                }
                else if (kind < 87)
                {
                    if (!world.Loaded.Contains(chunk))
                    {
                        world.Load(index, chunk);
                    }
                }
                else if (world.Loaded.Contains(chunk))
                {
                    world.Unload(index, chunk);
                    unloadedRecently = true;
                }

                if (op % 5 != 4)
                {
                    continue;
                }

                HashSet<SeepFace> expected = FromScratch(index, world);
                Assert.Equal(expected.Count, index.Count);
                foreach (SeepFace face in expected)
                {
                    Assert.True(index.Contains(face), $"missing {face}");
                }

                int filed = 0;
                int chunksWithFaces = 0;
                foreach (ChunkPos c in index.Chunks.ToList())
                {
                    IReadOnlyList<SeepFace> faces = index.FacesIn(c);
                    Assert.NotEmpty(faces);
                    chunksWithFaces++;
                    for (int i = 0; i < faces.Count; i++)
                    {
                        Assert.Contains(faces[i], expected);
                        Assert.Equal(c, faces[i].Inside.Chunk);
                        if (i > 0)
                        {
                            Assert.True(faces[i - 1].Y <= faces[i].Y, $"chunk {c} not lowest first");
                        }

                        filed++;
                    }

                    IReadOnlyList<SeepGroup> groups = index.GroupsIn(c);
                    Assert.Equal(
                        faces.Select(f => (index.SampleOf(f.Inside), f.Y)).Distinct().Count(),
                        groups.Count);
                    foreach (SeepGroup group in groups)
                    {
                        double sum = faces
                            .Where(f => f.Y == group.Y && index.SampleOf(f.Inside) == (group.SampleX, group.SampleZ))
                            .Sum(f => (double)f.Permeability);
                        Assert.Equal(sum, group.Permeability, 9);
                    }

                    if (faces.Select(f => f.Permeability).Distinct().Count() > 1)
                    {
                        checksWithMixedGroups++;
                    }
                }

                Assert.Equal(expected.Count, filed);
                Assert.Equal(chunksWithFaces, index.ChunkCount);

                checks++;
                if (expected.Any(f => f.Inside.Chunk != f.Rock.Chunk)) checksWithCrossings++;
                if (unloadedRecently && expected.Count > 0) checksAfterUnload++;
                unloadedRecently = false;
            }
        }

        output.WriteLine(
            $"{checks} checks: {checksWithCrossings} with a face across a chunk boundary, "
            + $"{checksWithMixedGroups} chunk-checks with mixed permeabilities, {checksAfterUnload} non-empty after an unload");
        Assert.True(checksWithCrossings > 0, "the fixture never produced a face across a chunk boundary");
        Assert.True(checksWithMixedGroups > 0, "no chunk ever mixed two rocks");
        Assert.True(checksAfterUnload > 0, "no check ever followed a forget with something left");
    }

    [Fact]
    public void TheBlockScanFilesExactlyWhatThePerVoxelScanFiles()
    {
        // The two readings of the definition against each other on the shapes the shell rules
        // are about: a face across every kind of chunk boundary, a capped mouth in the row under
        // a chunk floor, a sealed block, a natural cave, and a placed block in the lake.
        var perVoxel = new SeepWorld(Flat(63));
        var block = new SeepWorld(Flat(63)) { Bulk = true };
        var a = new SeepIndex(perVoxel.Seed, perVoxel);
        var b = new SeepIndex(block.Seed, block);

        foreach (SeepWorld world in new[] { perVoxel, block })
        {
            world.Dug.UnionWith([
                new VoxelPos(31, 40, 5), new VoxelPos(5, 32, 5), new VoxelPos(5, 63, 5), new VoxelPos(9, 64, 9),
                new VoxelPos(0, 50, 31), new VoxelPos(20, 55, 20), new VoxelPos(20, 54, 20),
            ]);
            world.Placed[new VoxelPos(20, 53, 20)] = Sealed;
            world.Placed[new VoxelPos(21, 55, 20)] = Dirt;
            world.Placed[new VoxelPos(9, 65, 9)] = Stone;
            world.Placed[new VoxelPos(5, 70, 5)] = Stone;
            world.Natural.Add(new VoxelPos(12, 45, 12));
        }

        ChunkPos[] chunks = [new(0, 1, 0), new(1, 1, 0), new(0, 1, 1), new(0, 0, 0), new(0, 2, 0)];
        foreach (ChunkPos chunk in chunks)
        {
            perVoxel.Load(a, chunk);
            block.Load(b, chunk);
            Assert.Equal(a.Count, b.Count);
        }

        Assert.Equal(chunks.Length, block.BlocksRead);
        Assert.True(a.Count > 30, $"only {a.Count} faces, the fixture is thin");
        foreach (ChunkPos chunk in chunks)
        {
            Assert.Equal(a.FacesIn(chunk), b.FacesIn(chunk));
            Assert.Equal(a.GroupsIn(chunk), b.GroupsIn(chunk));
        }

        // And the real classifier over a padded copy: missing reads as unloaded before the table
        // is asked, air and a fluid as holdable, rock as its bounded permeability.
        var materials = Content.MaterialTable.ForTesting(
            isOpaque: [false, true, true, false],
            isFluid: [false, false, false, true],
            textureLayer: [0, 0, 0, 0],
            hardness: [0f, 1f, 3f, 0f],
            permeability: [1f, 0.35f, 0.02f, 1f]);
        var padded = new PaddedChunk();
        padded.Fill(SeepNeighbourhood.MissingMaterial);
        padded[0, 0, 0] = 0;
        padded[1, 0, 0] = 3;
        padded[2, 0, 0] = 1;
        padded[3, 0, 0] = 2;
        var classified = new SeepNeighbourhood();
        classified.Classify(padded, materials);
        Assert.Equal(SeepNeighbourhood.Unloaded, classified[-1, -1, -1]);
        Assert.Equal(SeepNeighbourhood.Unloaded, classified[5, 5, 5]);
        Assert.Equal(SeepNeighbourhood.Holdable, classified[0, 0, 0]);
        Assert.Equal(SeepNeighbourhood.Holdable, classified[1, 0, 0]);
        Assert.Equal(0.35f, classified[2, 0, 0]);
        Assert.Equal(0.02f, classified[3, 0, 0]);
    }

    [Fact]
    public void TheTrackerFeedsTheSeepIndexUnderTheDugFilter()
    {
        // Decision 3 through the wiring: a natural cave in an unedited chunk is not a seep face
        // however permeable its walls; the chunk's first edit scans it and the cave joins; the
        // forget clears both indices. One tracker, so the two can never disagree about "dug".
        var world = new SeepWorld(Flat(40));
        var portals = new PortalIndex(world.Seed, world);
        var seeps = new SeepIndex(world.Seed, world);
        var tracker = new PortalTracker(portals, seeps);
        var cave = new VoxelPos(5, 34, 5);
        world.Natural.Add(cave);

        world.Loaded.Add(SurfaceChunk);
        tracker.OnVoxelsReady(SurfaceChunk, hasEdits: false);
        Assert.Equal(0, seeps.Count);

        // The cave's six faces, and the dug surface voxel's four walls and floor.
        world.Dug.Add(new VoxelPos(20, 40, 20));
        tracker.OnVoxelChanged(new VoxelPos(20, 40, 20));
        Assert.True(tracker.IsEdited(SurfaceChunk));
        Assert.Equal(6 + 5, seeps.Count);
        Assert.Contains(seeps.FacesIn(SurfaceChunk), f => f.Inside == cave && f.IsFloor);
        Assert.Equal(1, portals.Count);

        // The second edit in a dug chunk reaches the index through the hook alone — no scan.
        long scans = tracker.Scans;
        world.Dug.Add(new VoxelPos(20, 39, 20));
        tracker.OnVoxelChanged(new VoxelPos(20, 39, 20));
        Assert.Equal(scans, tracker.Scans);
        Assert.Equal(6 + 4 + 5, seeps.Count);

        // A cave in the unedited chunk beside the dug one, touching it: the wire check's case.
        // Its wall against the dug chunk is a pair touching a dug chunk — the portal rule — and
        // it is not a seep face, because the cave's chunk is not dug. The dug chunk's edit at the
        // shared face must not file it either.
        var east = new ChunkPos(1, 1, 0);
        var caveBeside = new VoxelPos(32, 36, 20);
        world.Natural.Add(caveBeside);
        world.Loaded.Add(east);
        tracker.OnVoxelsReady(east, hasEdits: false);
        Assert.Equal(15, seeps.Count);
        world.Dug.Add(new VoxelPos(31, 36, 20));
        tracker.OnVoxelChanged(new VoxelPos(31, 36, 20));
        Assert.Contains(seeps.FacesIn(SurfaceChunk), f => f.Inside == new VoxelPos(31, 36, 20) && f.Rock.Y == 35);
        Assert.DoesNotContain(seeps.FacesIn(east), f => f.Inside == caveBeside);
        Assert.Empty(seeps.FacesIn(east));
        var found = new List<SeepFace>();
        seeps.FacesOf(caveBeside, found);
        Assert.Empty(found);

        // A block placed at the shared face is the cave's rock only if the cave's chunk is dug:
        // the edit path's as-rock copy of the rule.
        world.Dug.Remove(new VoxelPos(31, 36, 20));
        world.Placed[new VoxelPos(31, 36, 20)] = Stone;
        tracker.OnVoxelChanged(new VoxelPos(31, 36, 20));
        Assert.Empty(seeps.FacesIn(east));

        // Dig in the east chunk and the cave's walls are faces from then on.
        world.Dug.Add(new VoxelPos(40, 40, 5));
        tracker.OnVoxelChanged(new VoxelPos(40, 40, 5));
        Assert.Contains(seeps.FacesIn(east), f => f.Inside == caveBeside);
        world.Loaded.Remove(east);
        tracker.OnChunkForgotten(east);

        world.Loaded.Remove(SurfaceChunk);
        tracker.OnChunkForgotten(SurfaceChunk);
        Assert.Equal(0, seeps.Count);
        Assert.Equal(0, portals.Count);

        // Back with its edits: both indices are scanned on the arrival — the cave and the two
        // dug voxels' faces; the voxel at the east face is a placed block now.
        world.Loaded.Add(SurfaceChunk);
        tracker.OnVoxelsReady(SurfaceChunk, hasEdits: true);
        Assert.Equal(15, seeps.Count);
        Assert.Equal(1, portals.Count);
    }

    [Fact]
    public void ABurstOfEditsIsRescannedWholeAtTheFlushAndEqualsTheDefinition()
    {
        // The bite lever: past SeepBurstThreshold edits in one chunk between flushes the
        // tracker stops telling the seep index per voxel and re-scans the chunk at the flush.
        // The index is stale in between — which is why the flush precedes the drain — and exact
        // after; the portal index is told every edit and is never stale; a hand's few edits
        // never reach the threshold and land at once.
        var world = new SeepWorld(Flat(40)) { Bulk = true };
        var portals = new PortalIndex(world.Seed, world);
        var seeps = new SeepIndex(world.Seed, world);
        var tracker = new PortalTracker(portals, seeps);
        world.Loaded.Add(SurfaceChunk);
        world.Loaded.Add(new ChunkPos(0, 0, 0));
        tracker.OnVoxelsReady(SurfaceChunk, hasEdits: false);
        tracker.OnVoxelsReady(new ChunkPos(0, 0, 0), hasEdits: false);

        // A hand's worth: immediate.
        for (int i = 0; i < 10; i++)
        {
            var v = new VoxelPos(5 + i, 38, 5);
            world.Dug.Add(v);
            tracker.OnVoxelChanged(v);
        }

        Assert.Equal(0, tracker.PendingRescans);
        Assert.Equal(seeps.Count, FromScratch(seeps, world).Count);

        // A bite: the first threshold's worth land, the rest wait.
        VoxelPos origin = SurfaceChunk.OriginVoxel;
        int edits = 0;
        for (int y = 0; y < VoxelConstants.ChunkSize; y++)
        {
            for (int z = 0; z < VoxelConstants.ChunkSize; z++)
            {
                for (int x = 0; x < VoxelConstants.ChunkSize; x++)
                {
                    var v = new VoxelPos(origin.X + x, origin.Y + y, origin.Z + z);
                    world.Dug.Add(v);
                    tracker.OnVoxelChanged(v);
                    edits++;
                }
            }
        }

        Assert.Equal(1, tracker.PendingRescans);
        HashSet<SeepFace> expected = FromScratch(seeps, world);
        Assert.NotEqual(expected.Count, seeps.Count);
        Assert.Equal(VoxelConstants.ChunkSize * VoxelConstants.ChunkSize, portals.Count);

        long rescans = tracker.Rescans;
        tracker.Flush();
        Assert.Equal(0, tracker.PendingRescans);
        Assert.Equal(rescans + 1, tracker.Rescans);
        Assert.Equal(expected.Count, seeps.Count);
        foreach (SeepFace face in expected)
        {
            Assert.True(seeps.Contains(face), $"missing {face}");
        }

        // The floor row of the bitten chunk stands on the chunk below's roof row, filed under
        // the bitten chunk: 1,024 faces the flush's scan took from the shell.
        Assert.Equal(VoxelConstants.ChunkSize * VoxelConstants.ChunkSize, seeps.FacesIn(SurfaceChunk).Count(f => f.IsFloor));

        // A burst that removes faces — the bitten chunk's floor row lined with stone — has to
        // forget before it re-scans, or the faces the deferred edits closed would survive.
        for (int i = 0; i < PortalTracker.SeepBurstThreshold * 2; i++)
        {
            var v = new VoxelPos(origin.X + (i & VoxelConstants.ChunkMask), origin.Y, origin.Z + (i >> VoxelConstants.ChunkSizeLog2));
            world.Dug.Remove(v);
            world.Placed[v] = Stone;
            tracker.OnVoxelChanged(v);
        }

        Assert.Equal(1, tracker.PendingRescans);
        tracker.Flush();
        expected = FromScratch(seeps, world);
        Assert.Equal(expected.Count, seeps.Count);
        foreach (SeepFace face in expected)
        {
            Assert.True(seeps.Contains(face), $"missing {face}");
        }

        // A flush with nothing pending scans nothing; a forget drops a pending rescan.
        rescans = tracker.Rescans;
        tracker.Flush();
        Assert.Equal(rescans, tracker.Rescans);
        for (int i = 0; i < PortalTracker.SeepBurstThreshold + 1; i++)
        {
            // Inside the chunk whatever the threshold: the first version walked off its east
            // face at i = 32 and counted the rest against the neighbour.
            var v = new VoxelPos(origin.X + (i & VoxelConstants.ChunkMask), origin.Y + 1, origin.Z + 1 + (i >> VoxelConstants.ChunkSizeLog2));
            world.Placed[v] = Stone;
            tracker.OnVoxelChanged(v);
        }

        Assert.Equal(1, tracker.PendingRescans);
        world.Loaded.Remove(SurfaceChunk);
        tracker.OnChunkForgotten(SurfaceChunk);
        Assert.Equal(0, tracker.PendingRescans);
        tracker.Flush();
        Assert.Equal(rescans, tracker.Rescans);
        Assert.Equal(FromScratch(seeps, world).Count, seeps.Count);
    }

    [Fact]
    public void AChunkBiteOfEditsIsTimed()
    {
        // Debug against a lambda seed and hash-set terrain, so a shape rather than a number; the
        // bench's --seep arm is the measurement. Two arms: the chunk holding the surface, and a
        // chunk wholly below it where every one of the 32,768 edits is at or below its surface
        // and there is no geometric exit — the arm the portal hook never had to pay for.
        var world = new SeepWorld(Flat(40));
        var index = new SeepIndex(world.Seed, world);
        world.Load(index, SurfaceChunk);
        var deep = new ChunkPos(0, 0, 0);
        world.Load(index, deep);

        // The surface chunk bitten out: its side walls are in chunks that are not loaded, so
        // nobody's rock; its floor row at Y 32 stands on the deep chunk's roof row, 1,024 faces.
        double surfaceMillis = TimeBite(world, index, SurfaceChunk);
        Assert.Equal(VoxelConstants.ChunkSize * VoxelConstants.ChunkSize, index.Count);

        // Then the deep chunk: every one of those floor faces goes, one removal per edit across
        // the chunk boundary, and the deep chunk's own walls and floor are unloaded too.
        double deepMillis = TimeBite(world, index, deep);
        Assert.Equal(0, index.Count);
        Assert.Equal(0, index.ChunkCount);

        output.WriteLine(
            $"bite of 32,768 edits: surface chunk {surfaceMillis:F2} ms, deep chunk {deepMillis:F2} ms "
            + "(Debug, lambda seed; the bench's --seep arm is the measurement)");
    }

    private static double TimeBite(SeepWorld world, SeepIndex index, ChunkPos chunk)
    {
        VoxelPos origin = chunk.OriginVoxel;
        for (int y = 0; y < VoxelConstants.ChunkSize; y++)
        {
            for (int z = 0; z < VoxelConstants.ChunkSize; z++)
            {
                for (int x = 0; x < VoxelConstants.ChunkSize; x++)
                {
                    world.Dug.Add(new VoxelPos(origin.X + x, origin.Y + y, origin.Z + z));
                }
            }
        }

        long started = Stopwatch.GetTimestamp();
        for (int y = 0; y < VoxelConstants.ChunkSize; y++)
        {
            for (int z = 0; z < VoxelConstants.ChunkSize; z++)
            {
                for (int x = 0; x < VoxelConstants.ChunkSize; x++)
                {
                    index.OnVoxelChanged(new VoxelPos(origin.X + x, origin.Y + y, origin.Z + z));
                }
            }
        }

        return Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }

    private static HashSet<SeepFace> FromScratch(SeepIndex index, SeepWorld world)
    {
        var expected = new HashSet<SeepFace>();
        var found = new List<SeepFace>();

        foreach (ChunkPos chunk in world.Loaded)
        {
            VoxelPos origin = chunk.OriginVoxel;
            for (int y = -1; y <= VoxelConstants.ChunkSize; y++)
            {
                for (int z = -1; z <= VoxelConstants.ChunkSize; z++)
                {
                    for (int x = -1; x <= VoxelConstants.ChunkSize; x++)
                    {
                        found.Clear();
                        index.FacesOf(new VoxelPos(origin.X + x, origin.Y + y, origin.Z + z), found);
                        foreach (SeepFace face in found)
                        {
                            if (face.Inside.Chunk == chunk || face.Rock.Chunk == chunk)
                            {
                                expected.Add(face);
                            }
                        }
                    }
                }
            }
        }

        return expected;
    }

    private static readonly (int X, int Y, int Z)[] Sides =
    [
        (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1),
    ];

    private static int Hash(int a, int b)
    {
        unchecked
        {
            uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663);
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return (int)(h & 0x7fffffff);
        }
    }
}
