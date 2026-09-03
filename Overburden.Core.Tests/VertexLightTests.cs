using Overburden.Core.Content;
using Overburden.Core.Lighting;
using Overburden.Core.Meshing;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// M5c-2: the padded light grid, the per-vertex trilinear sample, and what the two of them do
/// to the merge key.
/// </summary>
/// <remarks>
/// Pure — a padded chunk, a padded light grid and a mesher. No pipeline, no threads, no
/// propagation: the light field is <em>stated</em> here rather than flooded, which is what makes
/// these tests about the sample rather than about M5b all over again.
/// </remarks>
public class VertexLightTests
{
    private const int Air = MeshingTestHelpers.Air;
    private const int Stone = MeshingTestHelpers.Stone;

    private static MaterialTable Materials() => MeshingTestHelpers.StandardMaterials();

    // ------------------------------------------------------------------ the grid

    [Fact]
    public void EveryCellComesFromTheChunkThatOwnsIt()
    {
        var grids = new ChunkLight?[PaddedChunk.NeighbourhoodSize];

        // Each neighbour filled with a value derived from its own offset, so a cell that came
        // from the wrong chunk is a wrong number rather than a coincidence.
        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
        {
            var grid = new ChunkLight();
            int sky = ((dx + 1) * 3) + (dy + 1) + 1;
            int block = (dz + 1) + 1;

            for (int index = 0; index < LightCellPos.Volume; index++)
            {
                grid.Set(index, LightChannel.Sky, sky);
                grid.Set(index, LightChannel.Block, block);
            }

            grids[PaddedChunk.NeighbourIndex(dx, dy, dz)] = grid;
        }

        var padded = new PaddedLight();
        padded.CopyFrom(grids);

        for (int z = PaddedLight.Low; z <= PaddedLight.High; z++)
        for (int x = PaddedLight.Low; x <= PaddedLight.High; x++)
        for (int y = PaddedLight.Low; y <= PaddedLight.High; y++)
        {
            // The reference asks which chunk a cell coordinate falls in by dividing rather than
            // by shifting, so it shares no arithmetic with the subject.
            int dx = Div(x), dy = Div(y), dz = Div(z);
            int expectedSky = ((dx + 1) * 3) + (dy + 1) + 1;
            int expectedBlock = (dz + 1) + 1;

            byte cell = padded[x, y, z];

            Assert.Equal(expectedSky, cell & 0xF);
            Assert.Equal(expectedBlock, (cell >> 4) & 0xF);
        }

        static int Div(int cell) => (int)Math.Floor(cell / (double)LightCellPos.CellsPerChunk);
    }

    [Fact]
    public void SamplingACellCentreReturnsThatCellExactly()
    {
        var padded = new PaddedLight();
        padded.Fill(0, 0);
        padded.Set(3, 4, 5, sky: 12, block: 7);

        padded.Sample(3.5f, 4.5f, 5.5f, out float sky, out float block);

        Assert.Equal(12f / ChunkLight.MaxLevel, sky, 5);
        Assert.Equal(7f / ChunkLight.MaxLevel, block, 5);
    }

    [Fact]
    public void SamplingHalfwayBetweenTwoCentresAveragesThem()
    {
        var padded = new PaddedLight();
        padded.Fill(0, 0);
        padded.Set(3, 4, 5, sky: 15, block: 0);
        padded.Set(4, 4, 5, sky: 5, block: 0);

        padded.Sample(4.0f, 4.5f, 5.5f, out float sky, out _);

        Assert.Equal(10f / ChunkLight.MaxLevel, sky, 5);
    }

    /// <summary>
    /// The value sits at the cell's centre, not at its low corner.
    /// </summary>
    /// <remarks>
    /// <b>Half a metre of offset in the whole light field, and it would look plausible.</b>
    /// Sampling as though a cell's value lived at <c>i</c> rather than <c>i + 0.5</c> shifts
    /// every gradient by half a cell, which reads as light leaking round the wrong side of a
    /// corner rather than as an obvious bug.
    /// </remarks>
    [Fact]
    public void TheValueSitsAtTheCellCentreAndNotAtItsCorner()
    {
        var padded = new PaddedLight();
        padded.Fill(0, 0);
        padded.Set(2, 2, 2, sky: 15, block: 0);

        padded.Sample(2.0f, 2.5f, 2.5f, out float atCorner, out _);
        padded.Sample(2.5f, 2.5f, 2.5f, out float atCentre, out _);

        Assert.Equal(1f, atCentre, 5);
        Assert.Equal(0.5f, atCorner, 5);
    }

    /// <summary>
    /// Every sample a mesh sweep can ask for lands inside the grid, the far face included.
    /// </summary>
    /// <remarks>
    /// <b>This does not catch shrinking the grid to the 10³ the plan derived, and an M5c-2
    /// break-recipe proved it: <see cref="PaddedLight.Sample"/> skips zero-weight terms, so the
    /// out-of-range index is never even formed.</b> The outermost ring is belt and braces —
    /// M4a's contact-epsilon category, kept because dropping it silently promotes that
    /// <c>continue</c> from an optimisation to a bounds check.
    /// <para>
    /// What this test does still hold is the pairing: it walks every face of every boundary
    /// voxel, so it fails the moment the ring and the zero-weight skip are <em>both</em> gone.
    /// </para>
    /// </remarks>
    [Fact]
    public void EverySampleAMeshSweepCanAskForIsInsideTheGrid()
    {
        PaddedChunk voxels = MeshingTestHelpers.SolidChunk(Stone);
        var light = new PaddedLight();
        light.Fill(15, 15);
        MaterialTable materials = Materials();

        const int last = VoxelConstants.ChunkSize - 1;

        foreach (int x in new[] { 0, last })
        foreach (int y in new[] { 0, last })
        foreach (int z in new[] { 0, last })
        for (int face = 0; face < VoxelFace.Count; face++)
        {
            // Throws IndexOutOfRange if the grid is a ring short. No assertion needed — the
            // absence of an exception is the whole claim.
            AmbientOcclusion.ShadeFor(voxels, materials, light, face, x, y, z);
        }
    }

    // ------------------------------------------------------------------ the shade

    /// <summary>
    /// A flat, fully lit floor renders at full brightness — not at half.
    /// </summary>
    /// <remarks>
    /// <b>This is the whole reason the sample is offset half a cell along the face normal, and
    /// the failure without it is severe rather than subtle.</b> A face sits exactly on the
    /// boundary between the solid voxel below and the open one above, so a vertex on that
    /// boundary interpolates equally between the lit cell and the dark rock — a fully lit floor
    /// would come out at 7.5 of 15, everywhere, and look like a deliberate art choice.
    /// <para>
    /// <b>Break-recipe: set <see cref="PaddedLight.NormalOffsetCells"/> to zero.</b> The sky
    /// shade drops from 255 to 128.
    /// </para>
    /// </remarks>
    [Fact]
    public void AFlatLitFloorIsFullyBrightRatherThanHalfLit()
    {
        // Rock below the cell boundary, air above it — so the cell at y = 0 is open and lit and
        // the cell at y = -1 is solid and dark, with the floor's surface exactly between them.
        PaddedChunk voxels = MeshingTestHelpers.EmptyChunk();
        for (int z = -1; z <= VoxelConstants.ChunkSize; z++)
        for (int x = -1; x <= VoxelConstants.ChunkSize; x++)
        for (int y = -1; y < 0; y++)
        {
            voxels[x, y, z] = Stone;
        }

        var light = new PaddedLight();
        light.Fill(0, 0);
        for (int z = PaddedLight.Low; z <= PaddedLight.High; z++)
        for (int x = PaddedLight.Low; x <= PaddedLight.High; x++)
        for (int y = 0; y <= PaddedLight.High; y++)
        {
            light.Set(x, y, z, sky: ChunkLight.MaxLevel, block: 0);
        }

        // The +Y face of the topmost rock voxel, well inside the chunk so nothing else is in play.
        CornerShade shade = AmbientOcclusion.ShadeFor(
            voxels, Materials(), light, face: 2, x: 16, y: -1, z: 16);

        for (int corner = 0; corner < CornerShade.Corners; corner++)
        {
            Assert.Equal(byte.MaxValue, shade.Sky(corner));
            Assert.Equal(0, shade.Block(corner));
        }
    }

    /// <summary>
    /// Each of a face's four corners samples at its <em>own</em> vertex, not at one shared point.
    /// </summary>
    /// <remarks>
    /// <b>Written because the break-recipe battery caught nothing when all four corners were made
    /// to sample the same vertex</b> — and that is the difference between per-vertex light and
    /// per-face light, which is to say between a smooth gradient across a surface and flat-shaded
    /// facets. Every other test in this file passed: the merge tests still saw shades varying
    /// from face to face, the flat-floor test had uniform light so all corners agreed anyway, and
    /// the constant-shade-along-a-merged-run test is satisfied trivially when a quad's corners
    /// are all equal.
    /// <para>
    /// A gradient along X and a +Y face: corners 0 and 3 differ in X — see
    /// <c>VoxelFace.VAxis[2]</c> — so they must carry different shades.
    /// </para>
    /// </remarks>
    [Fact]
    public void EachCornerSamplesItsOwnVertexRatherThanOneSharedPoint()
    {
        PaddedChunk voxels = MeshingTestHelpers.EmptyChunk();
        for (int x = 0; x < VoxelConstants.ChunkSize; x++)
        for (int z = 0; z < VoxelConstants.ChunkSize; z++)
        {
            voxels[x, 0, z] = Stone;
        }

        var light = new PaddedLight();
        for (int z = PaddedLight.Low; z <= PaddedLight.High; z++)
        for (int x = PaddedLight.Low; x <= PaddedLight.High; x++)
        for (int y = PaddedLight.Low; y <= PaddedLight.High; y++)
        {
            light.Set(x, y, z, sky: Math.Clamp(x * 2, 0, ChunkLight.MaxLevel), block: 0);
        }

        CornerShade shade = AmbientOcclusion.ShadeFor(
            voxels, Materials(), light, face: 2, x: 4, y: 0, z: 4);

        // Corner 0 sits at the low X end of this face and corner 3 at the high end, so a
        // gradient along X has to separate them. One shared sample point makes all four equal.
        Assert.NotEqual(shade.Sky(0), shade.Sky(3));
    }

    /// <summary>Occlusion scales the light rather than replacing it, in both channels.</summary>
    /// <remarks>
    /// <b>Recorded from the battery: rounding versus truncating in <c>Scale</c> is caught by
    /// nothing, and no test here should try to.</b> The two differ by at most 1 in 255, which is
    /// below the resolution of every look question and every merge in this suite — it can shift
    /// the odd quad count by a hair and cannot shift anything a person sees. Rounding is kept as
    /// the honest conversion, not as a defended property.
    /// </remarks>
    [Theory]
    [InlineData(15, 0, 255, 0)]
    [InlineData(0, 15, 0, 255)]
    [InlineData(15, 15, 255, 255)]
    [InlineData(5, 10, 85, 170)]
    public void OcclusionMultipliesIntoBothChannels(int sky, int block, int expectedSky, int expectedBlock)
    {
        PaddedChunk voxels = MeshingTestHelpers.EmptyChunk();
        voxels[16, 16, 16] = Stone;

        var light = new PaddedLight();
        light.Fill(sky, block);

        // A lone voxel, so every corner of every face is fully open and occlusion is 1.
        CornerShade shade = AmbientOcclusion.ShadeFor(
            voxels, Materials(), light, face: 2, x: 16, y: 16, z: 16);

        for (int corner = 0; corner < CornerShade.Corners; corner++)
        {
            Assert.Equal(expectedSky, shade.Sky(corner));
            Assert.Equal(expectedBlock, shade.Block(corner));
        }
    }

    /// <summary>
    /// In darkness every occlusion level collapses to the same shade — which is why light in
    /// the merge key <em>reduces</em> the quad count instead of raising it.
    /// </summary>
    /// <remarks>
    /// <b>Measured headlessly at r4h/2v: 492 quads per drawn chunk with AO alone, 404 with AO
    /// and light.</b> That is the opposite of what M5-PLAN expected, and this is the mechanism.
    /// Occlusion multiplies into the light rather than sitting beside it, so where the light is
    /// zero the product is zero whatever the occlusion was — and faces that could not merge
    /// under AO alone merge freely in the dark, which is most of a mine's surface area.
    /// <para>
    /// The merge is <em>exact</em>, not an approximation: the shades really are equal, and the
    /// faces really do render identically. Pinned here because the quad measurement now depends
    /// on it, and because someone adding an ambient floor to the shade byte — rather than to the
    /// shader, where it belongs — would silently undo it.
    /// </para>
    /// </remarks>
    [Fact]
    public void InDarknessEveryOcclusionLevelCollapsesToTheSameShade()
    {
        // An inside corner, so the four corners of this face genuinely differ in occlusion.
        PaddedChunk voxels = MeshingTestHelpers.EmptyChunk();
        for (int x = 0; x < 8; x++)
        for (int z = 0; z < 8; z++)
        {
            voxels[16 + x, 16, 16 + z] = Stone;
        }

        voxels[16, 17, 16] = Stone;

        MaterialTable materials = Materials();

        var lit = new PaddedLight();
        lit.Fill(ChunkLight.MaxLevel, 0);

        var dark = new PaddedLight();
        dark.Fill(0, 0);

        CornerShade litShade = AmbientOcclusion.ShadeFor(
            voxels, materials, lit, face: 2, x: 17, y: 16, z: 16);

        CornerShade darkShade = AmbientOcclusion.ShadeFor(
            voxels, materials, dark, face: 2, x: 17, y: 16, z: 16);

        // Not vacuous: this face really does have corners at different occlusion levels.
        Assert.True(
            litShade.Sky(0) != litShade.Sky(1)
            || litShade.Sky(1) != litShade.Sky(2)
            || litShade.Sky(2) != litShade.Sky(3),
            "expected a face whose corners differ in occlusion");

        for (int corner = 0; corner < CornerShade.Corners; corner++)
        {
            Assert.Equal(0, darkShade.Sky(corner));
            Assert.Equal(0, darkShade.Block(corner));
        }
    }

    // ------------------------------------------------------------------ the merge key

    /// <summary>
    /// Constant light merges exactly as well as no light at all.
    /// </summary>
    /// <remarks>
    /// The merge key gained two varying channels, so the guard worth having is that it costs
    /// nothing where they do not vary — otherwise the quad measurement would be reporting a
    /// sampling artefact rather than the light field.
    /// </remarks>
    [Fact]
    public void UniformLightCostsNoQuadsAgainstNoLightAtAll()
    {
        PaddedChunk voxels = MeshingTestHelpers.RandomChunk(seed: 20260830, density: 0.55);
        MaterialTable materials = Materials();

        var uniform = new PaddedLight();
        uniform.Fill(ChunkLight.MaxLevel, 0);

        var withoutLight = new ChunkMesh();
        new GreedyMesher().Build(voxels, materials, withoutLight);

        var withUniformLight = new ChunkMesh();
        new GreedyMesher().Build(voxels, materials, withUniformLight, uniform);

        Assert.True(withoutLight.QuadCount > 0);
        Assert.Equal(withoutLight.QuadCount, withUniformLight.QuadCount);
    }

    /// <summary>
    /// A light gradient stops faces merging, and that is the price rather than a defect.
    /// </summary>
    /// <remarks>
    /// The other direction of the same guard: if varying light merged just as well, the sample
    /// would not be reaching the merge key at all and the whole channel would be inert.
    /// </remarks>
    [Fact]
    public void AGradientAcrossAFlatSurfaceStopsItMergingIntoOneQuad()
    {
        PaddedChunk voxels = MeshingTestHelpers.EmptyChunk();
        for (int x = 0; x < VoxelConstants.ChunkSize; x++)
        for (int z = 0; z < VoxelConstants.ChunkSize; z++)
        {
            voxels[x, 0, z] = Stone;
        }

        MaterialTable materials = Materials();

        var uniform = new PaddedLight();
        uniform.Fill(ChunkLight.MaxLevel, 0);

        var gradient = new PaddedLight();
        for (int z = PaddedLight.Low; z <= PaddedLight.High; z++)
        for (int x = PaddedLight.Low; x <= PaddedLight.High; x++)
        for (int y = PaddedLight.Low; y <= PaddedLight.High; y++)
        {
            gradient.Set(x, y, z, sky: Math.Clamp(x * 2, 0, ChunkLight.MaxLevel), block: 0);
        }

        var flat = new ChunkMesh();
        new GreedyMesher().Build(voxels, materials, flat, uniform);

        var varied = new ChunkMesh();
        new GreedyMesher().Build(voxels, materials, varied, gradient);

        Assert.True(
            varied.QuadCount > flat.QuadCount,
            $"a gradient should break merging: {varied.QuadCount} against {flat.QuadCount}");
    }

    /// <summary>
    /// Every merged quad still has a constant shade along the axes it merged on.
    /// </summary>
    /// <remarks>
    /// M5a proved this for occlusion; it is the property that makes "merging is exact" testable
    /// rather than asserted, and light varying independently is exactly the thing that could
    /// break it. Adjacent faces share two corners and an independently derived shade at a shared
    /// corner is the same number by definition, so requiring all four to match forces the shade
    /// to be constant along whichever axis the run grew.
    /// </remarks>
    [Fact]
    public void AMergedQuadStillHasConstantShadeAlongEveryAxisItMergedOn()
    {
        PaddedChunk voxels = MeshingTestHelpers.RandomChunk(seed: 4242, density: 0.5);

        // Two flat regions with one boundary between them, not per-cell noise. A field that
        // varies every cell merges *nothing*, which makes the "merged > 0" guard fail rather
        // than the property — the first version of this test did exactly that, and the guard is
        // the only reason it was noticed instead of passing vacuously.
        var light = new PaddedLight();
        for (int z = PaddedLight.Low; z <= PaddedLight.High; z++)
        for (int x = PaddedLight.Low; x <= PaddedLight.High; x++)
        for (int y = PaddedLight.Low; y <= PaddedLight.High; y++)
        {
            light.Set(x, y, z,
                sky: y >= LightCellPos.CellsPerChunk / 2 ? ChunkLight.MaxLevel : 4,
                block: x >= LightCellPos.CellsPerChunk / 2 ? ChunkLight.MaxLevel : 0);
        }

        var mesh = new ChunkMesh();
        new GreedyMesher().Build(voxels, MeshingTestHelpers.StandardMaterials(), mesh, light);

        int merged = 0;

        foreach (DecodedQuad quad in MeshingTestHelpers.DecodeQuads(mesh.Opaque))
        {
            if (quad.Width > 1)
            {
                merged++;
                Assert.Equal(quad.Sky[0], quad.Sky[1]);
                Assert.Equal(quad.Block[0], quad.Block[1]);
            }

            if (quad.Height > 1)
            {
                merged++;

                // The block channel as well as the sky one. M5a could only ever check sky —
                // nothing emitted — so a mesher that wrote the two into the same byte would have
                // passed everything that existed before this line.
                Assert.Equal(quad.Sky[0], quad.Sky[3]);
                Assert.Equal(quad.Block[0], quad.Block[3]);
            }
        }

        Assert.True(merged > 0, "no quad merged, so this proved nothing");
    }
}
