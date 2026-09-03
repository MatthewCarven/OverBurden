using Overburden.Core.Content;
using Overburden.Core.Inventory;
using Overburden.Core.Mining;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// The whole-chunk carve — the entangled miner's bite, 32,768 voxels of an 8 m cube.
/// </summary>
/// <remarks>
/// <para>
/// The claims here are not geometric either, and in a different way from <c>Chip</c>: the shape
/// is fixed and enormous, so what can go wrong is <b>which</b> chunk and <b>where it stops</b>.
/// A shape defined by the storage grid rather than by a point in metres is one off-by-one from
/// eating the neighbour, and a chunk boundary is invisible in the world, so nobody playing
/// would be able to tell a wrong answer from a right one.
/// </para>
/// <para>
/// The three properties worth the file: it covers its own chunk <b>exactly</b> and touches
/// nothing outside it; the hardness gate is still the shared one, so a tier that cannot take
/// stone leaves stone standing across an 8 m face; and the four buckets still account, so a
/// chunk bite and a sphere can be totalled by the same reader.
/// </para>
/// <para>
/// <b>Break-recipes run against the finished code:</b>
/// </para>
/// <list type="number">
/// <item>Loop bounds <c>&lt;=</c> rather than <c>&lt;</c>, so the bite spills one voxel into
/// three neighbouring chunks — <b>caught, by 12</b>, and the ones that matter are the tests
/// asserting what was <em>not</em> written. <c>Candidates</c> alone would have gone from 32,768
/// to 35,937 with <see cref="CarveResult.IsAccounted"/> true throughout.</item>
/// <item><b>Chunk derived from <c>SwingCentre(hit)</c> rather than <c>hit.Voxel</c> — GREEN, and
/// it is an identity rather than a hole.</b> <see cref="Carve.SwingCentre"/> returns the centre
/// of <c>hit.Voxel</c>, and <see cref="VoxelPos.FromMetres"/> floors that straight back to the
/// same voxel, so the mutation replaces a value with itself. <b>Recorded because the identity is
/// conditional on a decision with a live documented alternative</b>: <c>SwingCentre</c>'s own
/// remarks argue against centring on the ray's entry point, and if that ever reverses the two
/// derivations stop agreeing — at which point this shape is right by luck rather than by having
/// used <c>hit.Voxel</c> on purpose. Same category as M4a's zeroable epsilon and M8a's
/// <c>Wake(to)</c>. Recipes 2b–2d below are what that recipe was reaching for.</item>
/// <item>2b. Chunk taken from <c>hit.Previous</c> — the empty voxel across the face, which is a
/// different chunk exactly when the hit is on a chunk boundary, i.e. whenever you are looking at
/// a wall. <b>Caught by 1 before
/// <see cref="AHitOnAChunkFaceTakesTheChunkItHitAndNotTheOneTheRayCameFrom"/> was written for it
/// and by 7 after</b>; the pre-existing seam test straddled a seam on the wrong axis, so its
/// <c>Previous</c> stayed inside the chunk and the arms agreed.</item>
/// <item>2c. <c>chunk.OriginVoxel</c> replaced by a truncating float multiply — <b>GREEN, and an
/// identity again</b>. A chunk index times 32.0f is exact and truncating a whole float returns
/// it, so there is nothing to lose. <b>Informative rather than a hole, and it names the
/// asymmetry</b>: the chunk → voxel direction cannot round, only voxel → chunk can, which is
/// exactly why <see cref="VoxelPos.Chunk"/> shifts and why the seam through the origin is always
/// a voxel → chunk bug.</item>
/// <item>2d. That direction — the hit voxel's chunk by truncating division, so voxel −1 lands in
/// chunk 0 beside voxel 0 — <b>caught, by 2</b>. The real hazard, and the one
/// <see cref="ChunksBelowTheOriginAreWholeChunksToo"/> exists for.</item>
/// <item>The hardness gate dropped from the shared <c>Take</c> — <b>caught, by 11</b>, most of
/// them pre-existing sphere and chip tests. Same evidence as M3d's: one edit breaking three
/// shapes is what a forked gate would not do.</item>
/// <item>Air counted as removed — caught, by 8.</item>
/// <item><c>Unreachable</c> reported as <c>TooHard</c> for an unloaded chunk — caught, by 4.</item>
/// <item>The inventory credited to one material rather than per voxel inside <c>Take</c> —
/// <b>caught, by 3</b>. A chunk is the largest volume in the game and therefore the most likely
/// shape to span a material seam, so crediting one number would be wrong here far more often
/// than it is wrong for a sphere.</item>
/// <item>7. <c>Candidates</c> counted only for non-air voxels, which would make
/// <see cref="CarveResult.IsAccounted"/> false and the cost look world-dependent — caught, by 3.</item>
/// </list>
/// <para>
/// <b>Ten recipes, eight caught, and both green results were identities rather than holes</b> —
/// which is itself the finding worth keeping. A shape whose geometry is <em>the storage grid</em>
/// has very little to get wrong in the direction a mutation naturally reaches for: every
/// "compute the chunk a different way" recipe either lands on the same value by construction or
/// is the one genuine hazard, <c>VoxelPos.Chunk</c> truncating. <b>The recipe that actually
/// found a gap was the one about a different quantity</b> — <c>hit.Previous</c> rather than
/// <c>hit.Voxel</c> — and it needed a test written for it.
/// </para>
/// </remarks>
public class CarveChunkTests
{
    private const int Air = MaterialIds.AirNumericId;
    private const int Dirt = 1;
    private const int Stone = 2;

    private const int Volume = VoxelConstants.ChunkVolume;

    private static MaterialTable Materials() => MaterialTable.ForTesting(
        //             air    dirt  stone
        isOpaque: [false, true, true],
        isFluid: [false, false, false],
        textureLayer: [0, 1, 2],
        hardness: [0f, 0.8f, 3.0f]);

    /// <summary>A tier that takes dirt and not stone, as <c>hand_pick</c> does.</summary>
    private static ToolDefinition WeakPick() => new()
    {
        Id = Identifier.Parse("minercraft:test_weak"),
        RadiusMetres = 0.4f,
        HardnessThreshold = 1.0f,
        CooldownSeconds = 0.3f,
    };

    private static ToolDefinition StrongPick() => new()
    {
        Id = Identifier.Parse("minercraft:test_strong"),
        RadiusMetres = 0.5f,
        HardnessThreshold = 3.5f,
        CooldownSeconds = 0.2f,
    };

    private static VoxelHit HitAt(VoxelPos voxel) =>
        new(voxel, voxel.Offset(0, 1, 0), (0, 1, 0), 4.0f);

    [Fact]
    public void AChunkBiteTakesEveryVoxelOfItsOwnChunkAndNoneOfAnyOther()
    {
        var world = new FakeEditor { DefaultMaterial = Dirt };
        var chunk = new ChunkPos(2, -3, 5);

        CarveResult result = Carve.Chunk(world, Materials(), chunk, 1.0f);

        Assert.Equal(new CarveResult(Volume, Volume, 0, 0, 0), result);
        Assert.True(result.IsAccounted);
        Assert.True(result.Complete);

        // The written set is the chunk's own voxels, exactly — asserted as a set rather than as
        // a count, because a count is what an off-by-one loop bound keeps looking right to.
        Assert.Equal(Volume, world.Writes.Count);
        Assert.All(world.WrittenVoxels, voxel => Assert.Equal(chunk, voxel.Chunk));
        Assert.Equal(Volume, world.WrittenVoxels.Count);
    }

    [Fact]
    public void EveryVoxelOfTheChunkIsWrittenExactlyOnce()
    {
        var world = new FakeEditor { DefaultMaterial = Stone };
        var chunk = new ChunkPos(0, 0, 0);

        Carve.Chunk(world, Materials(), chunk, 5.0f);

        // Distinct positions == total writes is the property; a duplicated write would leave the
        // world correct and the accounting wrong, which is the failure a set alone cannot see.
        Assert.Equal(world.Writes.Count, world.WrittenVoxels.Count);

        VoxelPos origin = chunk.OriginVoxel;
        Assert.Contains(origin, world.WrittenVoxels);
        Assert.Contains(
            origin.Offset(
                VoxelConstants.ChunkSize - 1,
                VoxelConstants.ChunkSize - 1,
                VoxelConstants.ChunkSize - 1),
            world.WrittenVoxels);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(31, 31, 31)]
    [InlineData(31, 0, 15)]
    public void TheChunkTakenIsTheOneHoldingTheHitVoxelEvenOnItsOuterFace(int lx, int ly, int lz)
    {
        // The recipe this exists for: deriving the chunk from the swing *centre* in metres
        // instead of from the hit voxel. The two agree everywhere except within half a voxel of
        // a boundary — and a hit is always on a surface, so that is where they are asked.
        var world = new FakeEditor { DefaultMaterial = Dirt };
        var chunk = new ChunkPos(-1, 4, -2);
        VoxelPos target = VoxelPos.FromChunkLocal(chunk, lx, ly, lz);

        CarveResult result = Carve.Chunk(world, Materials(), HitAt(target), StrongPick());

        Assert.Equal(Volume, result.Removed);
        Assert.All(world.WrittenVoxels, voxel => Assert.Equal(chunk, voxel.Chunk));
    }

    [Fact]
    public void TwoAdjacentVoxelsEitherSideOfASeamTakeDifferentChunks()
    {
        var materials = Materials();

        var left = new FakeEditor { DefaultMaterial = Dirt };
        Carve.Chunk(left, materials, HitAt(new VoxelPos(31, 5, 5)), StrongPick());

        var right = new FakeEditor { DefaultMaterial = Dirt };
        Carve.Chunk(right, materials, HitAt(new VoxelPos(32, 5, 5)), StrongPick());

        Assert.All(left.WrittenVoxels, voxel => Assert.Equal(new ChunkPos(0, 0, 0), voxel.Chunk));
        Assert.All(right.WrittenVoxels, voxel => Assert.Equal(new ChunkPos(1, 0, 0), voxel.Chunk));
        Assert.Empty(left.WrittenVoxels.Intersect(right.WrittenVoxels));
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(-1, 0, 0)]
    [InlineData(0, 1, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, 1)]
    [InlineData(0, 0, -1)]
    public void AHitOnAChunkFaceTakesTheChunkItHitAndNotTheOneTheRayCameFrom(int nx, int ny, int nz)
    {
        // The shape of the real mistake: hit.Previous is the empty voxel the ray was in a step
        // earlier, and on a chunk boundary that is *the other chunk*. Looking at a wall is
        // exactly this situation, so it is not a corner case — it is the normal one. Every face
        // of the cube, because a single normal would leave five untested and the failure is
        // per-axis.
        var world = new FakeEditor { DefaultMaterial = Dirt };
        var chunk = new ChunkPos(0, 0, 0);

        // A voxel on the face the ray entered through, so that stepping back along the normal
        // leaves the chunk.
        VoxelPos target = VoxelPos.FromChunkLocal(
            chunk,
            nx > 0 ? VoxelConstants.ChunkSize - 1 : nx < 0 ? 0 : 12,
            ny > 0 ? VoxelConstants.ChunkSize - 1 : ny < 0 ? 0 : 12,
            nz > 0 ? VoxelConstants.ChunkSize - 1 : nz < 0 ? 0 : 12);

        VoxelPos previous = target.Offset(nx, ny, nz);
        Assert.NotEqual(chunk, previous.Chunk);   // the fixture really does straddle a seam

        var hit = new VoxelHit(target, previous, (nx, ny, nz), 4.0f);
        CarveResult result = Carve.Chunk(world, Materials(), hit, StrongPick());

        Assert.Equal(Volume, result.Removed);
        Assert.All(world.WrittenVoxels, voxel => Assert.Equal(chunk, voxel.Chunk));
        Assert.DoesNotContain(previous, world.WrittenVoxels);
    }

    [Fact]
    public void ChunksBelowTheOriginAreWholeChunksToo()
    {
        // Not reachable through VoxelPos.Chunk, which shifts rather than truncates — recorded in
        // the class remarks as an unreachable recipe. It is here because the claim is about the
        // seam through the origin that ChunkPos exists to prevent, and a future overload taking
        // metres is exactly the thing that would reintroduce it.
        var world = new FakeEditor { DefaultMaterial = Dirt };

        CarveResult result = Carve.Chunk(world, Materials(), new ChunkPos(-1, -1, -1), 1.0f);

        Assert.Equal(Volume, result.Removed);
        Assert.Contains(new VoxelPos(-32, -32, -32), world.WrittenVoxels);
        Assert.Contains(new VoxelPos(-1, -1, -1), world.WrittenVoxels);
        Assert.DoesNotContain(new VoxelPos(0, 0, 0), world.WrittenVoxels);
    }

    [Fact]
    public void ATierTooWeakForStoneLeavesTheWholeStoneHalfOfTheChunkStanding()
    {
        // The mechanic at chunk scale, and the reason the bite is not a delete key: half the
        // cube is stone, the weak tier takes the dirt and reports the rest refused rather than
        // silently taking it.
        var world = new FakeEditor { DefaultMaterial = Dirt };
        var chunk = new ChunkPos(0, 0, 0);
        VoxelPos origin = chunk.OriginVoxel;

        for (int z = 0; z < VoxelConstants.ChunkSize; z++)
        for (int x = 0; x < VoxelConstants.ChunkSize; x++)
        for (int y = 0; y < VoxelConstants.ChunkSize / 2; y++)
        {
            world.SetVoxel(origin.Offset(x, y, z), Stone);
        }

        world.Writes.Clear();

        CarveResult result = Carve.Chunk(world, Materials(), chunk, WeakPick().HardnessThreshold);

        Assert.Equal(Volume / 2, result.Removed);
        Assert.Equal(Volume / 2, result.TooHard);
        Assert.Equal(0, result.AlreadyAir);
        Assert.True(result.IsAccounted);

        // And it is the *stone* that survived, not merely half of something.
        Assert.All(world.WrittenVoxels, voxel => Assert.True(voxel.Y >= VoxelConstants.ChunkSize / 2));
        Assert.Equal(Stone, world[origin]);
    }

    [Fact]
    public void AChunkOfOpenSkyCountsAirWithoutWritingAnything()
    {
        var world = new FakeEditor { DefaultMaterial = Air };

        CarveResult result = Carve.Chunk(world, Materials(), new ChunkPos(3, 9, 3), 5.0f);

        Assert.Equal(new CarveResult(Volume, 0, 0, Volume, 0), result);
        Assert.Empty(world.Writes);
    }

    [Fact]
    public void AnUnloadedChunkIsUnreachableRatherThanRefusedAndSaysSo()
    {
        var world = new FakeEditor { DefaultMaterial = Dirt };
        var chunk = new ChunkPos(7, 1, 7);
        world.Unload(chunk);

        CarveResult result = Carve.Chunk(world, Materials(), chunk, 5.0f);

        Assert.Equal(new CarveResult(Volume, 0, 0, 0, Volume), result);
        Assert.False(result.Complete);
        Assert.Empty(world.Writes);
    }

    [Fact]
    public void CandidatesIsTheChunkVolumeWhateverTheChunkHeld()
    {
        // The one number that is a constant of this shape rather than of the world, and the
        // property that makes the cost knowable in advance: unlike a radius, nothing a caller
        // or a mod file supplies can make the loop longer.
        var materials = Materials();

        foreach (int fill in new[] { Air, Dirt, Stone })
        {
            var world = new FakeEditor { DefaultMaterial = fill };
            CarveResult result = Carve.Chunk(world, materials, new ChunkPos(1, 2, 3), 1.0f);

            Assert.Equal(Volume, result.Candidates);
            Assert.True(result.IsAccounted);
        }
    }

    [Fact]
    public void TheInventoryIsCreditedPerMaterialAcrossASeamThroughTheChunk()
    {
        // A chunk is the largest volume in the game and so the most likely shape to span two
        // materials. Crediting from Removed after the loop would have to guess which.
        var world = new FakeEditor { DefaultMaterial = Dirt };
        var chunk = new ChunkPos(0, 0, 0);
        VoxelPos origin = chunk.OriginVoxel;

        int stoneVoxels = 0;
        for (int z = 0; z < VoxelConstants.ChunkSize; z++)
        for (int x = 0; x < VoxelConstants.ChunkSize; x++)
        for (int y = 0; y < 8; y++)
        {
            world.SetVoxel(origin.Offset(x, y, z), Stone);
            stoneVoxels++;
        }

        var inventory = new VolumetricInventory();
        Carve.Chunk(world, Materials(), chunk, StrongPick().HardnessThreshold, inventory);

        Assert.Equal(stoneVoxels, inventory.Voxels(Stone));
        Assert.Equal(Volume - stoneVoxels, inventory.Voxels(Dirt));
        Assert.Equal(0, inventory.Voxels(Air));
    }

    [Fact]
    public void ANaNThresholdThrowsRatherThanTakingTheWholeChunkIncludingBedrock()
    {
        var world = new FakeEditor { DefaultMaterial = Stone };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => Carve.Chunk(world, Materials(), ChunkPos.Zero, float.NaN));

        Assert.Empty(world.Writes);
    }

    /// <summary>Reads fine, refuses writes, counts what it did — the same double as CarveTests.</summary>
    private sealed class FakeEditor : IVoxelEditor
    {
        private readonly Dictionary<VoxelPos, int> _voxels = [];
        private readonly HashSet<ChunkPos> _unloaded = [];

        public int DefaultMaterial { get; init; } = MaterialIds.AirNumericId;

        public List<(VoxelPos Voxel, int Material)> Writes { get; } = [];

        public HashSet<VoxelPos> WrittenVoxels => Writes.Select(write => write.Voxel).ToHashSet();

        public int this[VoxelPos voxel] =>
            _voxels.TryGetValue(voxel, out int material) ? material : DefaultMaterial;

        public void Unload(ChunkPos chunk) => _unloaded.Add(chunk);

        public bool TryGetMaterial(VoxelPos voxel, out int materialId)
        {
            if (_unloaded.Contains(voxel.Chunk))
            {
                materialId = MaterialIds.AirNumericId;
                return false;
            }

            materialId = this[voxel];
            return true;
        }

        public bool SetVoxel(VoxelPos voxel, int materialId)
        {
            if (_unloaded.Contains(voxel.Chunk))
            {
                return false;
            }

            Writes.Add((voxel, materialId));
            _voxels[voxel] = materialId;
            return true;
        }
    }
}
