using Overburden.Core.Content;
using Overburden.Core.Inventory;
using Overburden.Core.Mining;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// The cube carve — the entangled miner's bite since 2026-09-14, 512 voxels of a 2 m cube on a
/// 2 m lattice.
/// </summary>
/// <remarks>
/// <para>
/// The same shape of claim as <see cref="CarveChunkTests"/>, one lattice level down: the shape
/// is fixed, so what can go wrong is <b>which</b> cube and <b>where it stops</b>. Two hazards
/// are the cube's own. <b>The lattice is built by a mask, not a division</b>, so the seam
/// through the origin — voxel −1 landing in the cube at 0 — is the recipe most likely to be
/// reintroduced by a tidy-up; and <b>the cube must nest inside the chunk</b>, because a cube
/// straddling a chunk face is a bite that spills into a chunk the hit never named, which is the
/// chunk bite's off-by-one wearing a smaller coat.
/// </para>
/// <para>
/// <b>Break-recipes run against the finished code:</b>
/// </para>
/// <list type="number">
/// <item><see cref="Carve.CubeOrigin"/> by truncating division (<c>/ 8 * 8</c>) — <b>caught, by
/// 6</b>: the origin theory's negative rows, the straddling test's negative chunk and the tiling
/// test, whose cubes then overlap at zero.</item>
/// <item>Loop bounds <c>&lt;=</c> — <b>caught, by 13</b>, the set assertions again.</item>
/// <item>Cube from <c>hit.Previous</c> rather than <c>hit.Voxel</c> — <b>caught, by 5</b>: the
/// three face rows straddle the seam on the axis the ray came along, and the corner hits of the
/// straddling test have their Previous outside the chunk.</item>
/// <item><c>CubeVoxels</c> 8 → 6, not a divisor of 32 — <b>caught, by 9</b>: a cube then
/// straddles a chunk face and the sixty-four no longer tile. And 8 → 16, which divides 32 and
/// nests but is not the 2 m the ask named — <b>caught, by 10</b>, the origin theory's literal
/// expected values being what pins the size.</item>
/// <item>The hardness gate, air-as-removed and the inventory credit live in the shared
/// <c>Take</c>, and their recipes are the sphere, chip and chunk files' — one edit breaks four
/// shapes, which a forked gate would not.</item>
/// </list>
/// </remarks>
public class CarveCubeTests
{
    private const int Dirt = 1;
    private const int Stone = 2;

    private const int Edge = Carve.CubeVoxels;
    private const int Volume = Edge * Edge * Edge;

    private static MaterialTable Materials() => MaterialTable.ForTesting(
        //             air    dirt  stone
        isOpaque: [false, true, true],
        isFluid: [false, false, false],
        textureLayer: [0, 1, 2],
        hardness: [0f, 0.8f, 3.0f]);

    private static ToolDefinition WeakPick() => new()
    {
        Id = Identifier.Parse("overburden:test_weak"),
        RadiusMetres = 0.4f,
        HardnessThreshold = 1.0f,
        CooldownSeconds = 0.3f,
    };

    private static ToolDefinition StrongPick() => new()
    {
        Id = Identifier.Parse("overburden:test_strong"),
        RadiusMetres = 0.5f,
        HardnessThreshold = 3.5f,
        CooldownSeconds = 0.2f,
    };

    private static VoxelHit HitAt(VoxelPos voxel) =>
        new(voxel, voxel.Offset(0, 1, 0), (0, 1, 0), 4.0f);

    [Fact]
    public void TheEdgeDividesTheChunkSoACubeNestsInsideIt()
    {
        // The nesting claim as arithmetic, before any geometry: a power of two that divides the
        // chunk, the way the cave lattice step must.
        Assert.Equal(0, VoxelConstants.ChunkSize % Edge);
        Assert.Equal(0, Edge & (Edge - 1));
    }

    [Theory]
    [InlineData(0, 0, 0, 0, 0, 0)]
    [InlineData(7, 7, 7, 0, 0, 0)]
    [InlineData(8, 0, 0, 8, 0, 0)]
    [InlineData(-1, -1, -1, -8, -8, -8)]
    [InlineData(-8, 3, 15, -8, 0, 8)]
    [InlineData(-9, -16, 31, -16, -16, 24)]
    public void TheCubeOriginIsTheLatticeFloorOnBothSidesOfZero(int x, int y, int z, int ox, int oy, int oz)
    {
        // Voxel −1 belongs to the cube at −8, not the one at 0: the seam a division would put
        // through the origin, and the reason the floor is a mask.
        Assert.Equal(new VoxelPos(ox, oy, oz), Carve.CubeOrigin(new VoxelPos(x, y, z)));
    }

    [Fact]
    public void ACubeBiteTakesEveryVoxelOfItsOwnCubeAndNoneOfAnyOther()
    {
        var world = new FakeEditor { DefaultMaterial = Dirt };
        var origin = new VoxelPos(16, -24, 40);

        CarveResult result = Carve.Cube(world, Materials(), origin, 1.0f);

        Assert.Equal(new CarveResult(Volume, Volume, 0, 0, 0), result);
        Assert.True(result.IsAccounted);
        Assert.True(result.Complete);

        // The written set exactly, as a set: a count is what an off-by-one bound looks right to.
        Assert.Equal(Volume, world.Writes.Count);
        Assert.Equal(Volume, world.WrittenVoxels.Count);
        Assert.All(world.WrittenVoxels, voxel =>
        {
            Assert.InRange(voxel.X, origin.X, origin.X + Edge - 1);
            Assert.InRange(voxel.Y, origin.Y, origin.Y + Edge - 1);
            Assert.InRange(voxel.Z, origin.Z, origin.Z + Edge - 1);
        });
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(7, 7, 7)]
    [InlineData(3, 5, 1)]
    public void TheCubeTakenIsTheOneHoldingTheHitVoxelWhereverInsideItTheHitLands(int lx, int ly, int lz)
    {
        var world = new FakeEditor { DefaultMaterial = Dirt };
        var origin = new VoxelPos(-16, 8, 24);

        CarveResult result = Carve.Cube(world, Materials(), HitAt(origin.Offset(lx, ly, lz)), StrongPick());

        Assert.Equal(Volume, result.Removed);
        Assert.Contains(origin, world.WrittenVoxels);
        Assert.Contains(origin.Offset(Edge - 1, Edge - 1, Edge - 1), world.WrittenVoxels);
        Assert.DoesNotContain(origin.Offset(Edge, 0, 0), world.WrittenVoxels);
        Assert.DoesNotContain(origin.Offset(-1, 0, 0), world.WrittenVoxels);
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(0, 1, 0)]
    [InlineData(0, 0, 1)]
    public void AHitOnACubeFaceTakesTheCubeItHitAndNotTheOneTheRayCameFrom(int nx, int ny, int nz)
    {
        // The hit voxel is the first voxel of its cube along the ray; Previous is the last voxel
        // of the cube before it. Deriving the cube from Previous takes the wrong one on every
        // wall, which is where every real hit lands.
        var world = new FakeEditor { DefaultMaterial = Dirt };
        var hitVoxel = new VoxelPos(8, 8, 8);
        VoxelPos previous = hitVoxel.Offset(-nx, -ny, -nz);
        var hit = new VoxelHit(hitVoxel, previous, (nx, ny, nz), 2.0f);

        CarveResult result = Carve.Cube(world, Materials(), hit, StrongPick());

        Assert.Equal(Volume, result.Removed);
        Assert.Contains(hitVoxel, world.WrittenVoxels);
        Assert.DoesNotContain(previous, world.WrittenVoxels);
    }

    [Fact]
    public void ACubeNeverStraddlesAChunkFace()
    {
        // Every voxel a bite writes is in the hit voxel's chunk — asserted from a hit at each of
        // the eight corners of a chunk that itself straddles zero, because those are the hits a
        // straddling cube would betray.
        var chunk = new ChunkPos(-1, 0, -1);
        int last = VoxelConstants.ChunkSize - 1;
        foreach ((int cx, int cy, int cz) in new[]
                 {
                     (0, 0, 0), (last, 0, 0), (0, last, 0), (0, 0, last),
                     (last, last, 0), (last, 0, last), (0, last, last), (last, last, last),
                 })
        {
            var world = new FakeEditor { DefaultMaterial = Dirt };
            VoxelPos hitVoxel = chunk.OriginVoxel.Offset(cx, cy, cz);

            Carve.Cube(world, Materials(), HitAt(hitVoxel), StrongPick());

            Assert.Equal(Volume, world.WrittenVoxels.Count);
            Assert.All(world.WrittenVoxels, voxel => Assert.Equal(chunk, voxel.Chunk));
        }
    }

    [Fact]
    public void SixtyFourCubesTileAChunkExactly()
    {
        // The nesting claim as geometry, on a chunk below the origin: the cubes at every lattice
        // origin inside the chunk write the chunk's voxel set once each, no overlap, no gap. This
        // is what makes the chunk bite and the cube bite the same tool at two scales.
        var world = new FakeEditor { DefaultMaterial = Dirt };
        var chunk = new ChunkPos(-2, -1, 3);
        int perEdge = VoxelConstants.ChunkSize / Edge;

        for (int z = 0; z < perEdge; z++)
        for (int x = 0; x < perEdge; x++)
        for (int y = 0; y < perEdge; y++)
        {
            VoxelPos inside = chunk.OriginVoxel.Offset(x * Edge + 3, y * Edge + 5, z * Edge + 7);
            Carve.Cube(world, Materials(), Carve.CubeOrigin(inside), 1.0f);
        }

        Assert.Equal(VoxelConstants.ChunkVolume, world.Writes.Count);
        Assert.Equal(VoxelConstants.ChunkVolume, world.WrittenVoxels.Count);
        Assert.All(world.WrittenVoxels, voxel => Assert.Equal(chunk, voxel.Chunk));
    }

    [Fact]
    public void ATierTooWeakForStoneLeavesTheStoneHalfOfTheCubeStanding()
    {
        var world = new FakeEditor { DefaultMaterial = Dirt };
        var origin = new VoxelPos(0, 0, 0);
        for (int z = 0; z < Edge; z++)
        for (int x = 0; x < Edge; x++)
        for (int y = 0; y < Edge / 2; y++)
        {
            world.SetVoxel(origin.Offset(x, y, z), Stone);
        }

        world.Writes.Clear();

        CarveResult result = Carve.Cube(world, Materials(), HitAt(origin.Offset(1, 6, 1)), WeakPick());

        Assert.Equal(Volume, result.Candidates);
        Assert.Equal(Volume / 2, result.Removed);
        Assert.Equal(Volume / 2, result.TooHard);
        // Refused is not unreachable: the tier declined, the world answered.
        Assert.True(result.Complete);
        Assert.All(world.WrittenVoxels, voxel => Assert.True(voxel.Y >= Edge / 2));
        Assert.Equal(Stone, world[origin]);
    }

    [Fact]
    public void AirIsCountedAndNeverWritten()
    {
        var world = new FakeEditor();
        world.SetVoxel(new VoxelPos(2, 2, 2), Dirt);
        world.Writes.Clear();

        CarveResult result = Carve.Cube(world, Materials(), new VoxelPos(0, 0, 0), 1.0f);

        Assert.Equal(new CarveResult(Volume, 1, 0, Volume - 1, 0), result);
        Assert.Single(world.Writes);
    }

    [Fact]
    public void TheInventoryIsCreditedPerMaterialAcrossASeamThroughTheCube()
    {
        var world = new FakeEditor { DefaultMaterial = Dirt };
        var origin = new VoxelPos(8, 8, 8);
        int stone = 0;
        for (int z = 0; z < Edge; z++)
        for (int x = 0; x < Edge; x++)
        {
            world.SetVoxel(origin.Offset(x, 0, z), Stone);
            stone++;
        }

        var inventory = new VolumetricInventory();

        Carve.Cube(world, Materials(), origin, 3.5f, inventory);

        Assert.Equal(stone, inventory.Voxels(Stone));
        Assert.Equal(Volume - stone, inventory.Voxels(Dirt));
    }

    [Fact]
    public void AnUnloadedChunkIsUnreachableRatherThanRefused()
    {
        var world = new FakeEditor { DefaultMaterial = Dirt };
        world.Unload(new ChunkPos(0, 0, 0));

        CarveResult result = Carve.Cube(world, Materials(), new VoxelPos(8, 8, 8), 1.0f);

        Assert.Equal(new CarveResult(Volume, 0, 0, 0, Volume), result);
        Assert.Empty(world.Writes);
    }

    [Fact]
    public void ANaNThresholdThrowsRatherThanTakingTheWholeCube()
    {
        var world = new FakeEditor { DefaultMaterial = Stone };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => Carve.Cube(world, Materials(), new VoxelPos(0, 0, 0), float.NaN));
        Assert.Empty(world.Writes);
    }

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
