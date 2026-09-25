using Overburden.Core.Content;
using Overburden.Core.Lighting;
using Overburden.Core.Meshing;
using Overburden.Core.Voxels;
using static Overburden.Core.Tests.MeshingTestHelpers;

namespace Overburden.Core.Tests;

/// <summary>
/// Glass (docs/GLASS-PLAN.md, decision 1): a roof to the sky column — rain, the raised watershed
/// and solar all read that height — and a window to light, which falls through it at full strength.
/// </summary>
public sealed class GlassTests
{
    private const int Air = 0;
    private const int Rock = 1;
    private const int Pane = 2;

    /// <summary>The roof's voxel row: cell 10 (voxels 40–43), so cells 11–15 above it are open sky.</summary>
    private const int RoofY = 40;

    private static MaterialTable Materials() => MaterialTable.ForTesting(
        //             air    rock   glass
        isOpaque: [false, true, false],
        isFluid: [false, false, false],
        textureLayer: [0, 1, 2],
        hardness: [0f, 3f, 0.4f]);

    /// <summary>One chunk column two chunks tall, open to the sky from its floor, with a roof over the whole of it.</summary>
    private static LightTestWorld Roofed(int roof, bool leaveOneOut = false)
    {
        var world = new LightTestWorld(new ChunkPos(0, 0, 0), new ChunkPos(0, 1, 0), Materials(), new FlatSurface(-1));
        world.Fill(voxel => voxel.Y == RoofY && !(leaveOneOut && voxel.X == 13 && voxel.Z == 17) ? roof : Air);
        world.RecolumnFromVoxels();
        world.Light();
        return world;
    }

    private static LightCellPos CellUnder(int cellsDown) =>
        LightCellPos.FromVoxel(new VoxelPos(13, RoofY - (cellsDown * LightCellPos.VoxelsPerCell), 17));

    [Fact]
    public void AGlassRoofClosesTheSkyColumnAsAStoneOneDoes()
    {
        LightTestWorld glass = Roofed(Pane);
        LightTestWorld stone = Roofed(Rock);

        Assert.Equal(RoofY + 1, glass.Column(0, 0).OpenAboveY(13, 17));
        Assert.Equal(RoofY + 1, stone.Column(0, 0).OpenAboveY(13, 17));
    }

    [Fact]
    public void UnderAGlassRoofTheSkylightFallsFreeAndUnderStoneItIsDark()
    {
        LightTestWorld glass = Roofed(Pane);
        LightTestWorld stone = Roofed(Rock);
        LightTestWorld open = new(new ChunkPos(0, 0, 0), new ChunkPos(0, 1, 0), Materials(), new FlatSurface(-1));
        open.Light();

        // The cell the pane sits in is not a sky source, but full skylight falls free through
        // any face that conducts (M5b's shaft rule), and a pane conducts: the room under a glass
        // roof is lit as the sky is, all the way down. The pass predicted a level a metre (14,
        // 13, …) and was wrong; the free fall is the better answer and the one kept.
        Assert.Equal(LightCellPos.FromVoxel(new VoxelPos(13, RoofY, 17)), CellUnder(0));
        for (int down = 0; down <= 9; down++)
        {
            Assert.Equal(15, glass.Sky(CellUnder(down)));
        }

        // The controls: stone passes nothing, and with no roof it is full sky all the way down.
        Assert.Equal(0, stone.Sky(CellUnder(1)));
        Assert.Equal(15, open.Sky(CellUnder(1)));

        glass.AssertFixpoint();
    }

    [Fact]
    public void ThePaneThatClosesARoofIsTheIncrementalEditTheScanAgreesWith()
    {
        // Every pane but one placed, lit from scratch; the last pane placed as an edit — the
        // path OnVoxelChanged takes in play. The column moves, the light under it drops, and
        // both agree with a rebuild from the voxels.
        LightTestWorld world = Roofed(Pane, leaveOneOut: true);
        Assert.Equal(15, world.Sky(CellUnder(1)));

        world.Edit(new VoxelPos(13, RoofY, 17), Pane);
        world.Propagator.Drain();

        Assert.Equal(RoofY + 1, world.Column(0, 0).OpenAboveY(13, 17));
        Assert.Equal(15, world.Sky(CellUnder(1)));
        world.AssertSkyHeightsMatchAScan();
        world.AssertFixpoint();
    }

    [Fact]
    public void GlassBesideGlassDrawsNothingBetweenAndRockBehindGlassDrawsItsFace()
    {
        var voxels = EmptyChunk();
        voxels[5, 5, 5] = Glass;
        voxels[6, 5, 5] = Glass;
        voxels[5, 5, 9] = Glass;
        voxels[5, 5, 10] = Stone;

        var output = new ChunkMesh();
        new GreedyMesher().Build(voxels, StandardMaterials(), output);

        // The pair draws as one box — nothing between the two panes, its long faces merged — so
        // six quads; the lone pane draws five, its face against the stone hidden because stone
        // is opaque. The stone draws all six, the one behind the pane included.
        Assert.Equal(6, output.Opaque.QuadCount);
        Assert.Equal(6 + 5, output.Transparent.QuadCount);
    }
}
