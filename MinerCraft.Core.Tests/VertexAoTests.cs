using MinerCraft.Core.Meshing;
using MinerCraft.Core.Voxels;
using static MinerCraft.Core.Tests.MeshingTestHelpers;

namespace MinerCraft.Core.Tests;

/// <summary>
/// M5a: per-vertex ambient occlusion, the four-corner merge key, and the diagonal flip.
/// </summary>
public class VertexAoTests
{
    private const int Size = VoxelConstants.ChunkSize;

    private static ChunkMesh Mesh(PaddedChunk voxels, bool ambientOcclusion = true)
    {
        var output = new ChunkMesh();
        new GreedyMesher(ambientOcclusion).Build(voxels, StandardMaterials(), output);
        return output;
    }

    // ---------------------------------------------------------------- the rule itself

    [Theory]
    // side1, side2, corner -> level. The complete truth table, because the rule is small
    // enough to state exhaustively and every break-recipe below is one row of it.
    [InlineData(false, false, false, 3)]
    [InlineData(true, false, false, 2)]
    [InlineData(false, true, false, 2)]
    [InlineData(false, false, true, 2)]
    [InlineData(true, false, true, 1)]
    [InlineData(false, true, true, 1)]
    // Both sides walls the corner in regardless of what is behind it — this is the row that
    // makes an inside corner read as a corner rather than as a smudge, and it is the row a
    // plain sum would get wrong (it would say 1, and 0 with the corner solid).
    [InlineData(true, true, false, 0)]
    [InlineData(true, true, true, 0)]
    public void TheOcclusionRuleIsTheStandardOne(bool side1, bool side2, bool corner, int level)
    {
        Assert.Equal(level, AmbientOcclusion.Level(side1, side2, corner));
    }

    [Fact]
    public void EveryOcclusionLevelHasItsOwnShadeByte()
    {
        // Not a taste question, unlike the values themselves: the shade byte *is* the merge
        // key, so two levels sharing a byte would silently merge visibly different faces.
        var shades = Enumerable
            .Range(0, AmbientOcclusion.MaxLevel + 1)
            .Select(AmbientOcclusion.ShadeFor)
            .ToList();

        Assert.Equal(shades.Count, shades.Distinct().Count());
        Assert.Equal(byte.MaxValue, shades[AmbientOcclusion.MaxLevel]);
    }

    // ---------------------------------------------------------------- hand-built shapes

    [Fact]
    public void ALoneProtrudingCubeReadsFullyOpenEverywhere()
    {
        var voxels = EmptyChunk();
        voxels[5, 9, 17] = Stone;

        foreach (var quad in DecodeQuads(Mesh(voxels).Opaque))
        {
            Assert.All(quad.Sky, sky => Assert.Equal(AmbientOcclusion.ShadeFor(3), sky));
            Assert.All(quad.Block, block => Assert.Equal(0, block));
        }
    }

    [Fact]
    public void AnInsideCornerReadsZeroAtTheSharedVertex()
    {
        // Floor, plus two walls meeting along a vertical edge. The floor voxel tucked into
        // that corner has one vertex with two walls beside it, and that vertex goes black.
        var voxels = EmptyChunk();

        for (int z = -1; z <= Size; z++)
        for (int x = -1; x <= Size; x++)
        for (int y = -1; y <= Size; y++)
        {
            if (y <= 4 || (y >= 5 && (x >= 5 || z >= 5))) voxels[x, y, z] = Stone;
        }

        var materials = StandardMaterials();

        // +Y face of the floor voxel at (4, 4, 4). Corner 2 is the +U/+V one, which for a
        // +Y face is +Z and +X — one wall each.
        int shared = AoLevel(voxels, materials, face: 2, x: 4, y: 4, z: 4, corner: 2);
        Assert.Equal(0, shared);

        // ...and the diagonally opposite vertex, which has open air on both sides, does not.
        Assert.Equal(3, AoLevel(voxels, materials, face: 2, x: 4, y: 4, z: 4, corner: 0));

        var quad = Assert.Single(
            DecodeQuads(Mesh(voxels).Opaque),
            q => q.Face == 2 && q.UMin <= 4 && q.UMax > 4 && q.VMin <= 4 && q.VMax > 4);

        Assert.Contains(AmbientOcclusion.ShadeFor(0), quad.Sky);
    }

    [Fact]
    public void AWallStandingOnAFloorDarkensAlongTheContactEdge()
    {
        // M5-PLAN says a face against a flat plane "reads 2 along the contact edge". It reads
        // **1**: a continuous plane fills the diagonal corner voxel as well as the side one,
        // and only a lone side voxel with an open corner gives 2. Both rows are asserted here
        // so the correction is pinned rather than remembered.
        var floor = EmptyChunk();
        for (int z = -1; z <= Size; z++)
        for (int x = -1; x <= Size; x++)
        {
            floor[x, 4, z] = Stone;
        }

        floor[5, 5, 5] = Stone;
        var materials = StandardMaterials();

        // -Z face of the standing voxel: U is Y, V is X. Corner 0 is (-Y, -X): the floor is
        // below (side) and also below-and-across (diagonal).
        Assert.Equal(1, AoLevel(floor, materials, face: 5, x: 5, y: 5, z: 5, corner: 0));

        // The top of the same face is open on every sample.
        Assert.Equal(3, AoLevel(floor, materials, face: 5, x: 5, y: 5, z: 5, corner: 1));

        // The plan's "2" is this: one side voxel, nothing in the corner.
        var lone = EmptyChunk();
        lone[5, 5, 5] = Stone;
        lone[5, 4, 4] = Stone;

        Assert.Equal(2, AoLevel(lone, materials, face: 5, x: 5, y: 5, z: 5, corner: 0));
    }

    [Fact]
    public void OnlyOpaqueMaterialsOcclude()
    {
        // Water darkening a corner is an M8 question. Glass is the control: it is not opaque
        // either, so neither may cast occlusion, and the same predicate answers both.
        var voxels = EmptyChunk();
        voxels[5, 5, 5] = Stone;
        voxels[5, 6, 4] = Water;
        voxels[4, 6, 5] = Glass;
        voxels[4, 6, 4] = Water;

        var materials = StandardMaterials();

        for (int corner = 0; corner < CornerShade.Corners; corner++)
        {
            Assert.Equal(3, AoLevel(voxels, materials, face: 2, x: 5, y: 5, z: 5, corner));
        }

        // Corner 0 is the -U/-V one, which for a +Y face is -Z and -X: exactly the three
        // voxels placed above. Making its diagonal opaque is worth one level and no more.
        voxels[4, 6, 4] = Stone;
        Assert.Equal(2, AoLevel(voxels, materials, face: 2, x: 5, y: 5, z: 5, corner: 0));
    }

    // ---------------------------------------------------------------- the merge key

    /// <summary>
    /// The sharp one. Expand every emitted quad back into the voxel faces it covers, re-derive
    /// each one's four corner shades from the padded chunk with an independent implementation,
    /// and require all of them to equal the quad's own four-tuple.
    /// </summary>
    /// <remarks>
    /// A merge that averaged a corner away, or that kept only one corner's value, passes every
    /// coverage test in the suite and fails this. Break-recipe: drop <c>Shade</c> from
    /// <c>FaceKey</c> and watch it fail on the first non-flat fixture.
    /// </remarks>
    [Theory]
    [InlineData(1, 0.5)]
    [InlineData(2, 0.15)]
    [InlineData(3, 0.85)]
    [InlineData(21, 0.35)]
    public void EveryFaceAQuadCoversAgreesWithTheQuadsFourCorners(int seed, double density)
    {
        var voxels = RandomChunk(seed, density);
        var materials = StandardMaterials();
        var mesh = Mesh(voxels);

        int facesChecked = 0;

        foreach (var quad in DecodeQuads(mesh.Opaque).Concat(DecodeQuads(mesh.Transparent)))
        {
            foreach (var (x, y, z) in FacesUnder(quad))
            {
                for (int corner = 0; corner < CornerShade.Corners; corner++)
                {
                    int level = AoLevel(voxels, materials, quad.Face, x, y, z, corner);

                    Assert.Equal(AmbientOcclusion.ShadeFor(level), quad.Sky[corner]);
                    Assert.Equal(0, quad.Block[corner]);
                }

                facesChecked++;
            }
        }

        // The denominator, because a pass over nothing looks exactly like a pass.
        Assert.True(facesChecked > 1000, $"only {facesChecked} faces checked");
    }

    /// <summary>
    /// Merging is exact rather than approximate, and this is the property that says so.
    /// </summary>
    /// <remarks>
    /// Adjacent faces share two corners, and an independently derived shade at a shared corner
    /// is the same number by definition — so requiring all four corners to match forces the
    /// shade to be <em>constant</em> along whichever axis the run grew. A merged quad therefore
    /// interpolates exactly what the separate quads would have.
    /// </remarks>
    [Theory]
    [InlineData(4, 0.6)]
    [InlineData(5, 0.25)]
    public void AMergedQuadHasConstantShadeAlongEveryAxisItMergedOn(int seed, double density)
    {
        var mesh = Mesh(RandomChunk(seed, density));
        int merged = 0;

        foreach (var quad in DecodeQuads(mesh.Opaque).Concat(DecodeQuads(mesh.Transparent)))
        {
            if (quad.Width > 1)
            {
                Assert.Equal(quad.Sky[0], quad.Sky[1]);
                Assert.Equal(quad.Sky[3], quad.Sky[2]);
                merged++;
            }

            if (quad.Height > 1)
            {
                Assert.Equal(quad.Sky[0], quad.Sky[3]);
                Assert.Equal(quad.Sky[1], quad.Sky[2]);
                merged++;
            }
        }

        Assert.True(merged > 0, "nothing merged, so the property was never tested");
    }

    [Fact]
    public void AoDoesNotBreakUpFlatSurfaces()
    {
        // The regression test for "AO gratuitously stops greedy meshing working". Every corner
        // of a solid chunk floating in open air is fully exposed, so the shade is constant and
        // the six 32x32 quads M1c measured must survive unchanged.
        Assert.Equal(6, Mesh(SolidChunk(Stone)).QuadCount);
    }

    [Fact]
    public void AoOffRestoresTheConstantKeyAndTheOldQuadCount()
    {
        // The --ao=0 A/B, at the level it actually acts. A shape with occluded corners splits
        // into more quads with AO on than off; with AO off the counts must match M1c's rule
        // exactly, which is what makes the two profile runs comparable.
        var voxels = Terraced();

        int withAo = Mesh(voxels).QuadCount;
        int withoutAo = Mesh(voxels, ambientOcclusion: false).QuadCount;

        Assert.True(
            withAo > withoutAo,
            $"AO made no difference to merging ({withAo} vs {withoutAo}) — the key is not live");

        foreach (var quad in DecodeQuads(Mesh(voxels, ambientOcclusion: false).Opaque))
        {
            Assert.All(quad.Sky, sky => Assert.Equal(byte.MaxValue, sky));
            Assert.All(quad.Block, block => Assert.Equal(0, block));
        }
    }

    // ---------------------------------------------------------------- the diagonal flip

    [Fact]
    public void TheFlipFiresOnlyWhenTheDefaultDiagonalIsTheBrighterOne()
    {
        Assert.False(CornerShade.Uniform(255, 0).FlipDiagonal);

        Assert.True(Shade(255, 102, 255, 102).FlipDiagonal);
        Assert.False(Shade(102, 255, 102, 255).FlipDiagonal);

        // Block light counts too, which is the M5c half of the rule: a flat floor with a torch
        // on it has constant AO and a shade gradient, and an AO-only test would never fire.
        Assert.True(new CornerShade(
            CornerShade.FromCorners([255, 255, 255, 255], [90, 0, 90, 0]).Packed).FlipDiagonal);
    }

    [Fact]
    public void TheFlipIsSymmetricUnderRelabelling()
    {
        // Rotating the corner labels by two names the same diagonal, so the decision must not
        // move; rotating by one names the other diagonal, so it must invert. Together those
        // say the rule is about the geometry rather than about which corner got index 0.
        var shade = Shade(255, 204, 153, 102);

        Assert.Equal(shade.FlipDiagonal, Shade(153, 102, 255, 204).FlipDiagonal);
        Assert.NotEqual(shade.FlipDiagonal, Shade(204, 153, 102, 250).FlipDiagonal);
    }

    /// <summary>
    /// Both triangles of every emitted quad must face outwards, in either flip state.
    /// </summary>
    /// <remarks>
    /// <c>GreedyMesherTests.EveryQuadIsWoundConsistentlyWithItsNormal</c> reads vertex
    /// positions only, so the flip — which changes indices and nothing else — cannot make it
    /// fail. That is the right answer for that test and a hole for this one: getting the
    /// flipped index order backwards renders exactly the flipped quads inside-out, and only a
    /// check that walks the actual indices sees it.
    /// </remarks>
    [Theory]
    [InlineData(6, 0.45)]
    [InlineData(7, 0.7)]
    public void BothTrianglesOfEveryQuadFaceOutwardsInEitherFlipState(int seed, double density)
    {
        var mesh = Mesh(RandomChunk(seed, density));
        int flipped = 0;

        foreach (var buffer in new[] { mesh.Opaque, mesh.Transparent })
        {
            var quads = DecodeQuads(buffer);

            for (int quad = 0; quad < quads.Count; quad++)
            {
                if (quads[quad].Flipped) flipped++;

                float[] normal = VoxelFace.Normal[quads[quad].Face];

                for (int triangle = 0; triangle < 2; triangle++)
                {
                    // Godot's front faces are clockwise, so an outward triangle's
                    // index-order cross product points *against* the normal.
                    Assert.True(
                        Dot(Cross(buffer, quad, triangle), normal) < 0f,
                        $"quad {quad} triangle {triangle} is wound inside-out");
                }
            }
        }

        Assert.True(flipped > 0, "nothing flipped, so the flipped branch was never exercised");
    }

    [Theory]
    [InlineData(8, 0.4)]
    [InlineData(9, 0.75)]
    public void TheEmittedDiagonalAlwaysFollowsTheCornerShades(int seed, double density)
    {
        foreach (var quad in DecodeQuads(Mesh(RandomChunk(seed, density)).Opaque))
        {
            int diagonal02 = quad.Sky[0] + quad.Block[0] + quad.Sky[2] + quad.Block[2];
            int diagonal13 = quad.Sky[1] + quad.Block[1] + quad.Sky[3] + quad.Block[3];

            Assert.Equal(diagonal02 > diagonal13, quad.Flipped);
        }
    }

    // ---------------------------------------------------------------- packing

    [Fact]
    public void CornerShadeRoundTripsEveryBytePair()
    {
        var shade = CornerShade.FromCorners([0, 1, 128, 255], [255, 128, 1, 0]);

        for (int corner = 0; corner < CornerShade.Corners; corner++)
        {
            Assert.Equal(new byte[] { 0, 1, 128, 255 }[corner], shade.Sky(corner));
            Assert.Equal(new byte[] { 255, 128, 1, 0 }[corner], shade.Block(corner));
        }

        Assert.Equal(shade, new CornerShade(shade.Packed));
        Assert.Equal(byte.MaxValue, CornerShade.FullSky.Sky(2));
        Assert.Equal(0, CornerShade.FullSky.Block(2));
    }

    // ---------------------------------------------------------------- fixtures

    private static CornerShade Shade(byte a, byte b, byte c, byte d) =>
        CornerShade.FromCorners([a, b, c, d], [0, 0, 0, 0]);

    /// <summary>Steps of stone — the terracing an integer heightmap actually produces.</summary>
    private static PaddedChunk Terraced()
    {
        var voxels = EmptyChunk();

        for (int z = -1; z <= Size; z++)
        for (int x = -1; x <= Size; x++)
        {
            int height = 4 + Math.Clamp(x, 0, Size - 1) / 4;
            for (int y = -1; y < height; y++) voxels[x, y, z] = Stone;
        }

        return voxels;
    }

    /// <summary>The voxel faces one quad covers, in padded-chunk coordinates.</summary>
    private static IEnumerable<(int X, int Y, int Z)> FacesUnder(DecodedQuad quad)
    {
        int axis = VoxelFace.Axis[quad.Face];
        int uAxis = VoxelFace.UAxis[quad.Face];
        int vAxis = VoxelFace.VAxis[quad.Face];

        int along = quad.Plane - (VoxelFace.Direction[quad.Face] > 0 ? 1 : 0);

        for (int v = quad.VMin; v < quad.VMax; v++)
        for (int u = quad.UMin; u < quad.UMax; u++)
        {
            var position = new int[3];
            position[axis] = along;
            position[uAxis] = u;
            position[vAxis] = v;

            yield return (position[0], position[1], position[2]);
        }
    }

    private static float[] Cross(MeshBuffer buffer, int quad, int triangle)
    {
        int i0 = buffer.Indices[quad * 6 + triangle * 3 + 0];
        int i1 = buffer.Indices[quad * 6 + triangle * 3 + 1];
        int i2 = buffer.Indices[quad * 6 + triangle * 3 + 2];

        float[] a = Vertex(buffer, i0);
        float[] b = Vertex(buffer, i1);
        float[] c = Vertex(buffer, i2);

        float[] ab = [b[0] - a[0], b[1] - a[1], b[2] - a[2]];
        float[] ac = [c[0] - a[0], c[1] - a[1], c[2] - a[2]];

        return
        [
            ab[1] * ac[2] - ab[2] * ac[1],
            ab[2] * ac[0] - ab[0] * ac[2],
            ab[0] * ac[1] - ab[1] * ac[0],
        ];
    }

    private static float[] Vertex(MeshBuffer buffer, int index) =>
    [
        buffer.Vertices[index * 3 + 0],
        buffer.Vertices[index * 3 + 1],
        buffer.Vertices[index * 3 + 2],
    ];

    private static float Dot(float[] a, float[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
}
