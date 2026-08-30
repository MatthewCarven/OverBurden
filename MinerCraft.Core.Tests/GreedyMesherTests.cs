using MinerCraft.Core.Meshing;
using MinerCraft.Core.Voxels;
using static MinerCraft.Core.Tests.MeshingTestHelpers;

namespace MinerCraft.Core.Tests;

public class GreedyMesherTests
{
    private const int Size = VoxelConstants.ChunkSize;

    private static ChunkMesh Mesh(PaddedChunk voxels)
    {
        var output = new ChunkMesh();
        new GreedyMesher().Build(voxels, StandardMaterials(), output);
        return output;
    }

    [Fact]
    public void EmptyChunkProducesNothing()
    {
        var mesh = Mesh(EmptyChunk());

        Assert.True(mesh.IsEmpty);
        Assert.Equal(0, mesh.QuadCount);
    }

    [Fact]
    public void SolidChunkCollapsesToSixQuads()
    {
        // The headline result: 32³ solid voxels expose 6,144 faces, and greedy meshing
        // merges each 32×32 face into one quad. This is why 0.25 m voxels are viable.
        var mesh = Mesh(SolidChunk(Stone));

        Assert.Equal(6, mesh.QuadCount);
        Assert.Equal(6, mesh.Opaque.QuadCount);
        Assert.True(mesh.Transparent.IsEmpty);

        Assert.Equal(6 * Size * Size, VisibleFaces(SolidChunk(Stone), StandardMaterials()).Count);

        foreach (var quad in DecodeQuads(mesh.Opaque))
        {
            Assert.Equal(Size * Size, quad.Area);
        }
    }

    [Fact]
    public void SingleVoxelProducesSixQuads()
    {
        var voxels = EmptyChunk();
        voxels[5, 9, 17] = Stone;

        var mesh = Mesh(voxels);

        Assert.Equal(6, mesh.QuadCount);
        Assert.All(DecodeQuads(mesh.Opaque), quad => Assert.Equal(1, quad.Area));
    }

    [Fact]
    public void CheckerboardIsTheWorstCaseAndMergesNothing()
    {
        // Every solid voxel is surrounded by air and no two same-plane faces are adjacent,
        // so greedy meshing can do nothing and must exactly equal the culled count. This
        // bounds the worst case rather than the best one.
        var voxels = EmptyChunk();

        for (int z = 0; z < Size; z++)
        for (int x = 0; x < Size; x++)
        for (int y = 0; y < Size; y++)
        {
            if ((x + y + z) % 2 == 0) voxels[x, y, z] = Stone;
        }

        var mesh = Mesh(voxels);
        int culled = VisibleFaces(voxels, StandardMaterials()).Count;

        Assert.Equal(culled, mesh.QuadCount);
        Assert.All(DecodeQuads(mesh.Opaque), quad => Assert.Equal(1, quad.Area));
    }

    [Fact]
    public void MergedQuadsTileTheirTextureRatherThanStretchingIt()
    {
        // A quad spanning 32 voxels gets UV 0..32, relying on repeat_enable in the shader.
        // This is exactly what a texture atlas cannot express, and the reason DESIGN.md
        // forbids one.
        var mesh = Mesh(SolidChunk(Stone));
        var uvs = mesh.Opaque.Uvs;

        float maxU = 0f;
        float maxV = 0f;
        for (int i = 0; i < mesh.Opaque.VertexCount; i++)
        {
            maxU = MathF.Max(maxU, uvs[i * 2 + 0]);
            maxV = MathF.Max(maxV, uvs[i * 2 + 1]);
        }

        Assert.Equal(Size, maxU);
        Assert.Equal(Size, maxV);
    }

    [Fact]
    public void EveryQuadIsWoundConsistentlyWithItsNormal()
    {
        // Corners are emitted p, p+du, p+du+dv, p+dv, and VoxelFace picks U and V per face
        // so that du × dv points along the outward normal. Get that wrong on one face and
        // it renders inside-out while the other five look fine.
        var voxels = EmptyChunk();
        voxels[4, 4, 4] = Stone;
        voxels[10, 10, 10] = Water;

        var mesh = Mesh(voxels);

        foreach (var buffer in new[] { mesh.Opaque, mesh.Transparent })
        {
            for (int quad = 0; quad < buffer.VertexCount / 4; quad++)
            {
                int v3 = quad * 4 * 3;

                var c0 = Corner(buffer, v3, 0);
                var c1 = Corner(buffer, v3, 1);
                var c3 = Corner(buffer, v3, 3);

                var du = (c1.X - c0.X, c1.Y - c0.Y, c1.Z - c0.Z);
                var dv = (c3.X - c0.X, c3.Y - c0.Y, c3.Z - c0.Z);

                var cross = (
                    du.Item2 * dv.Item3 - du.Item3 * dv.Item2,
                    du.Item3 * dv.Item1 - du.Item1 * dv.Item3,
                    du.Item1 * dv.Item2 - du.Item2 * dv.Item1);

                float dot = cross.Item1 * buffer.Normals[v3 + 0]
                            + cross.Item2 * buffer.Normals[v3 + 1]
                            + cross.Item3 * buffer.Normals[v3 + 2];

                Assert.True(dot > 0, $"quad {quad} is wound against its own normal");
            }
        }
    }

    private static (float X, float Y, float Z) Corner(MeshBuffer buffer, int v3, int corner) =>
        (buffer.Vertices[v3 + corner * 3 + 0],
         buffer.Vertices[v3 + corner * 3 + 1],
         buffer.Vertices[v3 + corner * 3 + 2]);

    [Fact]
    public void FluidsGoToTheTransparentSurface()
    {
        var voxels = EmptyChunk();
        voxels[3, 3, 3] = Water;
        voxels[8, 8, 8] = Stone;

        var mesh = Mesh(voxels);

        Assert.Equal(6, mesh.Opaque.QuadCount);
        Assert.Equal(6, mesh.Transparent.QuadCount);
    }

    [Fact]
    public void OpaqueNeighboursHideEachOther()
    {
        // Stone against dirt shows no boundary face — you cannot see between two solids.
        var voxels = EmptyChunk();
        voxels[5, 5, 5] = Stone;
        voxels[6, 5, 5] = Dirt;

        var mesh = Mesh(voxels);

        // 12 faces total minus the two that face each other.
        Assert.Equal(10, mesh.QuadCount);
    }

    [Fact]
    public void WaterAgainstGlassIsVisibleButWaterAgainstWaterIsNot()
    {
        var voxels = EmptyChunk();
        voxels[5, 5, 5] = Water;
        voxels[6, 5, 5] = Water;
        voxels[7, 5, 5] = Glass;

        var mesh = Mesh(voxels);
        var faces = VisibleFaces(voxels, StandardMaterials());

        Assert.Equal(faces.Count, RasteriseToFaces(
            DecodeQuads(mesh.Opaque).Concat(DecodeQuads(mesh.Transparent))).Count);
    }

    [Fact]
    public void TextureLayerComesFromTheMaterialTable()
    {
        var voxels = EmptyChunk();
        voxels[1, 1, 1] = Dirt; // layer 8 in StandardMaterials

        var mesh = Mesh(voxels);

        for (int i = 0; i < mesh.Opaque.VertexCount; i++)
        {
            Assert.Equal(8f, mesh.Opaque.Uv2s[i * 2 + 0]);

            // UV2.y carried light x AO until M5a moved the shade into its own byte pair.
            // A lone voxel is unoccluded, so the sky channel is full and block light is zero
            // because nothing emits any until M5b.
            Assert.Equal(0f, mesh.Opaque.Uv2s[i * 2 + 1]);
            Assert.Equal(255, mesh.Opaque.Shades[i * 2 + 0]);
            Assert.Equal(0, mesh.Opaque.Shades[i * 2 + 1]);
        }
    }

    [Fact]
    public void BorderVoxelsSuppressBoundaryFaces()
    {
        // The whole point of the padded copy: a chunk whose neighbour is solid must not
        // emit a face there, or every chunk seam becomes a visible wall.
        var solidNeighbours = new PaddedChunk();
        solidNeighbours.Fill(Stone);

        var mesh = Mesh(solidNeighbours);

        Assert.True(mesh.IsEmpty);
    }

    [Fact]
    public void ReusingAMesherAndBufferGivesTheSameResult()
    {
        // Buffers are reused rather than reallocated, so a stale count or an uncleared
        // mask would show up as contamination between builds.
        var mesher = new GreedyMesher();
        var materials = StandardMaterials();
        var output = new ChunkMesh();

        var complex = EmptyChunk();
        for (int i = 0; i < Size; i++) complex[i, i, i] = Stone;

        mesher.Build(complex, materials, output);
        int complexQuads = output.QuadCount;

        mesher.Build(SolidChunk(Stone), materials, output);
        Assert.Equal(6, output.QuadCount);

        mesher.Build(complex, materials, output);
        Assert.Equal(complexQuads, output.QuadCount);

        mesher.Build(EmptyChunk(), materials, output);
        Assert.True(output.IsEmpty);
    }

    /// <summary>
    /// The strong test. Greedy meshing is only allowed to <i>merge</i> faces — never to
    /// lose one, duplicate one, or invent one. Expanding every emitted quad back into the
    /// voxel faces it covers must reproduce the culled mesher's answer exactly, with no
    /// cell covered twice.
    /// </summary>
    [Theory]
    [InlineData(1, 0.5)]
    [InlineData(2, 0.15)]
    [InlineData(3, 0.85)]
    [InlineData(4, 0.02)]
    [InlineData(5, 0.98)]
    public void CoverageExactlyMatchesTheCulledMesher(int seed, double density)
    {
        var materials = StandardMaterials();
        var voxels = RandomChunk(seed, density);

        var mesh = Mesh(voxels);

        var covered = RasteriseToFaces(
            DecodeQuads(mesh.Opaque).Concat(DecodeQuads(mesh.Transparent)));

        var expected = VisibleFaces(voxels, materials);

        // No cell covered twice — the classic greedy failure.
        Assert.Equal(covered.Count, covered.Distinct().Count());

        // Nothing missing, nothing invented.
        Assert.Equal(expected, covered.ToHashSet());
    }

    [Theory]
    [InlineData(11, 0.5)]
    [InlineData(12, 0.2)]
    [InlineData(13, 0.9)]
    public void MergingNeverProducesMoreQuadsThanFaces(int seed, double density)
    {
        var voxels = RandomChunk(seed, density);
        var mesh = Mesh(voxels);

        int culled = VisibleFaces(voxels, StandardMaterials()).Count;

        Assert.True(
            mesh.QuadCount <= culled,
            $"greedy emitted {mesh.QuadCount} quads for {culled} faces");
    }

    [Fact]
    public void LayeredTerrainMergesAggressively()
    {
        // The realistic case, and the one the performance argument rests on: flat layers
        // merge into a handful of large quads rather than thousands of small ones.
        var voxels = new PaddedChunk();
        voxels.Fill(Air);

        for (int z = -1; z <= Size; z++)
        for (int x = -1; x <= Size; x++)
        for (int y = -1; y < 16; y++)
        {
            voxels[x, y, z] = Stone;
        }

        var mesh = Mesh(voxels);
        int culled = VisibleFaces(voxels, StandardMaterials()).Count;

        // One quad for the exposed top surface; the sides are hidden by the border.
        Assert.Equal(1, mesh.QuadCount);
        Assert.Equal(Size * Size, culled);
    }

}
