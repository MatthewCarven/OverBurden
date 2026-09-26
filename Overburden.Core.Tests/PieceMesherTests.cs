using Overburden.Core.Meshing;
using Overburden.Core.Structure;
using Overburden.Core.Voxels;
using static Overburden.Core.Tests.MeshingTestHelpers;

namespace Overburden.Core.Tests;

/// <summary>
/// COLLAPSE-PLAN part c-2: a falling piece's mesh winds, textures and hides faces exactly as the
/// greedy mesher does for the same voxels, so a piece in the air looks like the rock it left and a
/// cull mode can never find a mirrored face on one and not the other.
/// </summary>
public class PieceMesherTests
{
    /// <summary>Every quad as its normal and its four corners in emission order — the winding — plus its layer.</summary>
    private static HashSet<string> Quads(MeshBuffer buffer, float dx = 0, float dy = 0, float dz = 0)
    {
        var quads = new HashSet<string>();
        for (int q = 0; q < buffer.QuadCount; q++)
        {
            int v = q * 4;
            string corners = string.Join(" ", Enumerable.Range(0, 4).Select(i =>
                $"({buffer.Vertices[(v + i) * 3] + dx:F3},{buffer.Vertices[(v + i) * 3 + 1] + dy:F3},{buffer.Vertices[(v + i) * 3 + 2] + dz:F3})"));
            quads.Add($"n({buffer.Normals[v * 3]},{buffer.Normals[v * 3 + 1]},{buffer.Normals[v * 3 + 2]}) {corners} layer {buffer.Uv2s[v * 2]}");
        }

        return quads;
    }

    [Fact]
    public void AOneVoxelPieceIsTheGreedyMeshersVoxelQuadForQuad()
    {
        var voxels = EmptyChunk();
        voxels[5, 9, 17] = Stone;
        var chunk = new ChunkMesh();
        new GreedyMesher().Build(voxels, StandardMaterials(), chunk);

        ChunkMesh piece = PieceMesher.Build([(0, 0, 0)], [Stone], StandardMaterials());

        const float s = VoxelConstants.VoxelSize;
        Assert.Equal(6, piece.Opaque.QuadCount);
        Assert.Equal(Quads(chunk.Opaque), Quads(piece.Opaque, 5 * s, 9 * s, 17 * s));
        Assert.Equal(chunk.Opaque.Indices[..chunk.Opaque.IndexCount], piece.Opaque.Indices[..piece.Opaque.IndexCount]);
    }

    [Fact]
    public void GlassHidesAndShowsFacesAsInAChunk()
    {
        // GlassTests' own fixture, as a piece: a pair of panes, a lone pane, and stone behind it.
        ChunkMesh piece = PieceMesher.Build(
            [(0, 0, 0), (1, 0, 0), (0, 0, 4), (0, 0, 5)],
            [Glass, Glass, Glass, Stone],
            StandardMaterials());

        // The pair draws ten faces unmerged (a chunk merges its long faces into six quads), the
        // lone pane five, its face against the stone hidden; the stone all six.
        Assert.Equal(6, piece.Opaque.QuadCount);
        Assert.Equal(10 + 5, piece.Transparent.QuadCount);
    }

    [Fact]
    public void AnIPieceShowsOnlyItsOutside()
    {
        ChunkMesh piece = PieceMesher.Build([(0, 0, 0), (1, 0, 0), (2, 0, 0), (3, 0, 0)], [Stone, Dirt, Stone, Dirt], StandardMaterials());
        Assert.Equal((4 * 6) - (2 * 3), piece.QuadCount);
        Assert.True(piece.Transparent.IsEmpty);

        // Each voxel keeps its own layer: two of the faces-per-voxel sets are dirt.
        var layers = Enumerable.Range(0, piece.Opaque.QuadCount).Select(q => piece.Opaque.Uv2s[q * 4 * 2]).ToList();
        Assert.Contains((float)StandardMaterials().TextureLayer(Dirt), layers);
        Assert.Contains((float)StandardMaterials().TextureLayer(Stone), layers);
    }
}
