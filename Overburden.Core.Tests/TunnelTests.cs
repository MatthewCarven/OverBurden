using Overburden.Core.Building;
using Overburden.Core.Content;
using Overburden.Core.Inventory;
using Overburden.Core.Mining;
using Overburden.Core.Voxels;
using static Overburden.Core.Tests.PhysicsTestHelpers;

namespace Overburden.Core.Tests;

/// <summary>
/// The quantum tunneller's bite and fill (2026-09-23): a 2 m cube, 8 × 8 × 8 voxels, on a
/// lattice that nests inside the chunk and around the brush cell.
/// </summary>
/// <remarks>
/// The cube's hazards one lattice up — which cube, where it stops, the seam through zero, the
/// nesting — plus the pairing the fill exists for: the bite names the cube holding
/// <see cref="VoxelHit.Voxel"/> and the fill the one holding <see cref="VoxelHit.Previous"/>, so
/// aiming at the far wall of a bore fills the bore.
/// </remarks>
public class TunnelTests
{
    private const int Edge = Carve.TunnelVoxels;
    private const int Volume = Edge * Edge * Edge;

    private static ToolDefinition Tunneller() => new()
    {
        Id = Identifier.Parse("overburden:test_tunneller"),
        HardnessThreshold = 5.0f,
        CooldownSeconds = 0.25f,
        Bite = ToolBite.Tunnel,
    };

    private static VoxelHit HitOn(VoxelPos solid, VoxelPos previous) => new(solid, previous, (0, 1, 0), 4.0f);

    [Fact]
    public void TheEdgeIsTwoMetresAndNestsBetweenTheBrushCellAndTheChunk()
    {
        Assert.Equal(8, Edge);
        Assert.Equal(2.0f, TunnelLattice.SizeMetres);
        Assert.Equal(0, Edge % BrushCell.Voxels);
        Assert.Equal(0, VoxelConstants.ChunkSize % Edge);
        Assert.Equal(0, Edge & (Edge - 1));
    }

    [Theory]
    [InlineData(0, 0, 0, 0, 0, 0)]
    [InlineData(7, 7, 7, 0, 0, 0)]
    [InlineData(8, 0, 15, 8, 0, 8)]
    [InlineData(-1, -1, -1, -8, -8, -8)]
    [InlineData(-8, 3, 31, -8, 0, 24)]
    [InlineData(-9, -16, 33, -16, -16, 32)]
    public void TheOriginIsTheLatticeFloorOnBothSidesOfZero(int x, int y, int z, int ox, int oy, int oz)
    {
        Assert.Equal(new VoxelPos(ox, oy, oz), Carve.TunnelOrigin(new VoxelPos(x, y, z)));
    }

    [Theory]
    [InlineData(-13, 22, 7)]
    [InlineData(40, -3, -17)]
    [InlineData(-1, 0, 31)]
    public void EveryVoxelsTunnelCubeHoldsItsBrushCellAndSitsInsideItsChunk(int x, int y, int z)
    {
        // The nesting as a property of positions: the brush cell a voxel is in lies wholly in
        // that voxel's tunnel cube, and the tunnel cube wholly in its chunk.
        var voxel = new VoxelPos(x, y, z);
        VoxelPos tunnel = Carve.TunnelOrigin(voxel);
        VoxelPos cell = Carve.CubeOrigin(voxel);
        Assert.Equal(tunnel, Carve.TunnelOrigin(cell));
        Assert.Equal(tunnel, Carve.TunnelOrigin(cell.Offset(BrushCell.Voxels - 1, BrushCell.Voxels - 1, BrushCell.Voxels - 1)));
        Assert.Equal(voxel.Chunk, tunnel.Chunk);
        Assert.Equal(voxel.Chunk, tunnel.Offset(Edge - 1, Edge - 1, Edge - 1).Chunk);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(7, 7, 7)]
    [InlineData(3, 6, 1)]
    public void TheBiteTakesExactlyTheCubeHoldingTheHitVoxel(int lx, int ly, int lz)
    {
        var world = new TestWorld { Background = _ => Stone };
        var origin = new VoxelPos(-16, 8, 24);
        VoxelPos solid = origin.Offset(lx, ly, lz);

        CarveResult result = Carve.Tunnel(world, Table, HitOn(solid, solid.Offset(0, 1, 0)), Tunneller());

        Assert.Equal(new CarveResult(Volume, Volume, 0, 0, 0), result);
        Assert.Equal(Volume, world.WrittenVoxels.Count);
        Assert.All(world.WrittenVoxels, v =>
        {
            Assert.InRange(v.X, origin.X, origin.X + Edge - 1);
            Assert.InRange(v.Y, origin.Y, origin.Y + Edge - 1);
            Assert.InRange(v.Z, origin.Z, origin.Z + Edge - 1);
        });
    }

    [Fact]
    public void EightCubeBitesAreOneTunnelBite()
    {
        // Same world, same aim: the tunnel's written set is exactly the eight brush cells inside it.
        var cubes = new TestWorld { Background = _ => Stone };
        var origin = new VoxelPos(8, 0, -8);
        for (int dz = 0; dz < Edge; dz += BrushCell.Voxels)
        for (int dx = 0; dx < Edge; dx += BrushCell.Voxels)
        for (int dy = 0; dy < Edge; dy += BrushCell.Voxels)
        {
            Carve.Cube(cubes, Table, origin.Offset(dx, dy, dz), 5.0f);
        }

        var tunnel = new TestWorld { Background = _ => Stone };
        Carve.Tunnel(tunnel, Table, origin, 5.0f);

        Assert.Equal(cubes.WrittenVoxels, tunnel.WrittenVoxels);
    }

    [Fact]
    public void TheFillAimedAtTheBoresFarWallFillsTheBoreAndNotTheWall()
    {
        // Bite a cube out of solid stone, crediting the inventory, then aim at the wall beyond it
        // from inside: the hit is the wall voxel, Previous the last air of the bore. The fill must
        // land in the bore — Place.Tunnel from the wall's cube would be 512 Occupied.
        var world = new TestWorld { Background = _ => Stone };
        var inventory = new VolumetricInventory();
        var origin = new VoxelPos(16, 16, 16);
        Carve.Tunnel(world, Table, origin, 5.0f, inventory);
        Assert.Equal(Volume, inventory.Voxels(Stone));

        VoxelPos wall = origin.Offset(Edge, 3, 3);
        VoxelPos lastAir = origin.Offset(Edge - 1, 3, 3);
        VoxelHit hit = HitOn(wall, lastAir);
        Assert.NotEqual(Carve.TunnelOrigin(hit.Voxel), Place.TargetTunnel(hit));
        Assert.Equal(origin, Place.TargetTunnel(hit));

        PlaceResult result = Place.Tunnel(world, Table, Place.TargetTunnel(hit), Stone, from: inventory);

        Assert.True(result.IsAccounted, result.ToString());
        Assert.Equal(Volume, result.Placed);
        Assert.Equal(0, result.Occupied);
        Assert.Equal(0L, inventory.Voxels(Stone));
        for (int z = 0; z < Edge; z++)
        for (int x = 0; x < Edge; x++)
        for (int y = 0; y < Edge; y++)
        {
            Assert.Equal(Stone, world[origin.Offset(x, y, z)]);
        }
    }

    [Fact]
    public void TheFillNeverOverwritesAndStopsAtItsOwnCube()
    {
        // Half the cube is rock: those are refused, the air half is filled, nothing outside is written.
        var world = new TestWorld { Background = v => v.Y < 4 ? Stone : Air };
        var origin = new VoxelPos(0, 0, 0);

        PlaceResult result = Place.Tunnel(world, Table, origin, Dirt);

        Assert.True(result.IsAccounted, result.ToString());
        Assert.Equal(Volume / 2, result.Placed);
        Assert.Equal(Volume / 2, result.Occupied);
        Assert.All(world.WrittenVoxels, v =>
        {
            Assert.InRange(v.X, 0, Edge - 1);
            Assert.InRange(v.Y, 4, Edge - 1);
            Assert.InRange(v.Z, 0, Edge - 1);
        });
    }
}
