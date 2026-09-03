using Overburden.Core.Content;
using Overburden.Core.Meshing;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>One decoded quad, in voxel units rather than metres.</summary>
/// <param name="Sky">Per-corner sky shade read back out of the buffer, in corner order.</param>
/// <param name="Block">Per-corner block shade, same order.</param>
/// <param name="Triangles">
/// The six indices the quad was emitted with, relative to its own first vertex. This is how
/// the diagonal flip is observed — it changes nothing about the vertices.
/// </param>
internal readonly record struct DecodedQuad(
    int Face,
    int Plane,
    int UMin,
    int UMax,
    int VMin,
    int VMax,
    byte[] Sky,
    byte[] Block,
    int[] Triangles)
{
    public int Area => (UMax - UMin) * (VMax - VMin);

    public int Width => UMax - UMin;

    public int Height => VMax - VMin;

    /// <summary>Which diagonal splits the quad: 0–2 by default, 1–3 when flipped.</summary>
    public bool Flipped => Triangles[0] != 0;
}

internal static class MeshingTestHelpers
{
    public const int Air = 0;
    public const int Stone = 1;
    public const int Dirt = 2;
    public const int Water = 3;
    public const int Glass = 4;

    public static MaterialTable StandardMaterials() => MaterialTable.ForTesting(
        //             air    stone  dirt   water  glass
        isOpaque: [false, true, true, false, false],
        isFluid: [false, false, false, true, false],
        textureLayer: [0, 7, 8, 9, 10],
        // Meshing has no use for hardness. Spelled out rather than defaulted, because an
        // optional argument here would let a carve test forget it and pass for the wrong
        // reason — zero hardness means "any tool mines this".
        hardness: [0f, 3.0f, 0.8f, 0f, 1.0f]);

    /// <summary>
    /// Independent reference implementation of face visibility, deliberately written out
    /// again rather than calling the mesher's — a reference that shares code with the
    /// thing it checks proves nothing.
    /// </summary>
    public static bool IsVisible(int here, int there, MaterialTable materials)
    {
        if (here == Air) return false;
        if (here == there) return false;
        return !materials.IsOpaque(there);
    }

    /// <summary>Every visible face, one entry per voxel face. The culled-mesher answer.</summary>
    public static HashSet<(int Face, int X, int Y, int Z)> VisibleFaces(
        PaddedChunk voxels, MaterialTable materials)
    {
        var faces = new HashSet<(int, int, int, int)>();

        for (int face = 0; face < VoxelFace.Count; face++)
        {
            int[] offset = VoxelFace.Offset[face];

            for (int z = 0; z < VoxelConstants.ChunkSize; z++)
            for (int x = 0; x < VoxelConstants.ChunkSize; x++)
            for (int y = 0; y < VoxelConstants.ChunkSize; y++)
            {
                int here = voxels[x, y, z];
                int there = voxels[x + offset[0], y + offset[1], z + offset[2]];

                if (IsVisible(here, there, materials))
                {
                    faces.Add((face, x, y, z));
                }
            }
        }

        return faces;
    }

    /// <summary>
    /// Independent reference implementation of the vertex AO rule, deliberately written out
    /// again rather than calling <see cref="AmbientOcclusion"/>'s.
    /// </summary>
    /// <remarks>
    /// Spelled with explicit per-face corner offsets rather than derived from
    /// <see cref="VoxelFace"/>'s U/V axes, so a wrong axis choice in the subject is a
    /// disagreement here rather than the same mistake made twice.
    /// </remarks>
    public static int AoLevel(
        PaddedChunk voxels, MaterialTable materials, int face, int x, int y, int z, int corner)
    {
        // (su, sv) for corners p, p+du, p+du+dv, p+dv.
        int su = corner is 1 or 2 ? 1 : -1;
        int sv = corner is 2 or 3 ? 1 : -1;

        int[] offset = VoxelFace.Offset[face];
        int fx = x + offset[0];
        int fy = y + offset[1];
        int fz = z + offset[2];

        int[] u = UnitVector(VoxelFace.UAxis[face]);
        int[] v = UnitVector(VoxelFace.VAxis[face]);

        bool side1 = materials.IsOpaque(voxels[fx + su * u[0], fy + su * u[1], fz + su * u[2]]);
        bool side2 = materials.IsOpaque(voxels[fx + sv * v[0], fy + sv * v[1], fz + sv * v[2]]);
        bool diagonal = materials.IsOpaque(voxels[
            fx + su * u[0] + sv * v[0],
            fy + su * u[1] + sv * v[1],
            fz + su * u[2] + sv * v[2]]);

        if (side1 && side2) return 0;
        return 3 - ((side1 ? 1 : 0) + (side2 ? 1 : 0) + (diagonal ? 1 : 0));
    }

    private static int[] UnitVector(int axis) => axis switch
    {
        0 => [1, 0, 0],
        1 => [0, 1, 0],
        _ => [0, 0, 1],
    };

    /// <summary>
    /// Reads quads back out of a buffer's vertex data, so tests check the actual emitted
    /// geometry rather than an internal side-channel.
    /// </summary>
    public static List<DecodedQuad> DecodeQuads(MeshBuffer buffer)
    {
        var quads = new List<DecodedQuad>();
        const float scale = VoxelConstants.VoxelSize;

        for (int quad = 0; quad < buffer.VertexCount / 4; quad++)
        {
            int v3 = quad * 4 * 3;

            int face = FaceForNormal(
                buffer.Normals[v3 + 0], buffer.Normals[v3 + 1], buffer.Normals[v3 + 2]);

            int axis = VoxelFace.Axis[face];
            int uAxis = VoxelFace.UAxis[face];
            int vAxis = VoxelFace.VAxis[face];

            Span<int> min = [int.MaxValue, int.MaxValue, int.MaxValue];
            Span<int> max = [int.MinValue, int.MinValue, int.MinValue];

            for (int corner = 0; corner < 4; corner++)
            {
                for (int component = 0; component < 3; component++)
                {
                    int value = (int)MathF.Round(buffer.Vertices[v3 + corner * 3 + component] / scale);
                    min[component] = Math.Min(min[component], value);
                    max[component] = Math.Max(max[component], value);
                }
            }

            var sky = new byte[4];
            var block = new byte[4];
            for (int corner = 0; corner < 4; corner++)
            {
                sky[corner] = buffer.Shades[(quad * 4 + corner) * 2 + 0];
                block[corner] = buffer.Shades[(quad * 4 + corner) * 2 + 1];
            }

            var triangles = new int[6];
            for (int i = 0; i < 6; i++)
            {
                triangles[i] = buffer.Indices[quad * 6 + i] - quad * 4;
            }

            quads.Add(new DecodedQuad(
                face,
                min[axis],
                min[uAxis], max[uAxis],
                min[vAxis], max[vAxis],
                sky, block, triangles));
        }

        return quads;
    }

    /// <summary>
    /// Expands quads back into the individual voxel faces they cover. Returns duplicates
    /// as a list rather than a set, so overlap is detectable.
    /// </summary>
    public static List<(int Face, int X, int Y, int Z)> RasteriseToFaces(IEnumerable<DecodedQuad> quads)
    {
        var covered = new List<(int, int, int, int)>();
        var position = new int[3];

        foreach (var quad in quads)
        {
            int axis = VoxelFace.Axis[quad.Face];
            int uAxis = VoxelFace.UAxis[quad.Face];
            int vAxis = VoxelFace.VAxis[quad.Face];

            // A +facing quad sits on the far side of its voxel, so the voxel is one back.
            int voxelAlongAxis = quad.Plane - (VoxelFace.Direction[quad.Face] > 0 ? 1 : 0);

            for (int v = quad.VMin; v < quad.VMax; v++)
            for (int u = quad.UMin; u < quad.UMax; u++)
            {
                position[axis] = voxelAlongAxis;
                position[uAxis] = u;
                position[vAxis] = v;

                covered.Add((quad.Face, position[0], position[1], position[2]));
            }
        }

        return covered;
    }

    private static int FaceForNormal(float x, float y, float z)
    {
        for (int face = 0; face < VoxelFace.Count; face++)
        {
            float[] normal = VoxelFace.Normal[face];
            if (normal[0] == x && normal[1] == y && normal[2] == z) return face;
        }

        throw new InvalidOperationException($"Normal ({x}, {y}, {z}) is not axis-aligned.");
    }

    public static PaddedChunk EmptyChunk()
    {
        var chunk = new PaddedChunk();
        chunk.Fill(Air);
        return chunk;
    }

    /// <summary>A chunk of random materials, border included, for coverage fuzzing.</summary>
    public static PaddedChunk RandomChunk(int seed, double density)
    {
        var rng = new Random(seed);
        var voxels = new PaddedChunk();
        int[] palette = [Air, Stone, Dirt, Water, Glass];

        for (int z = -1; z <= VoxelConstants.ChunkSize; z++)
        for (int x = -1; x <= VoxelConstants.ChunkSize; x++)
        for (int y = -1; y <= VoxelConstants.ChunkSize; y++)
        {
            voxels[x, y, z] = rng.NextDouble() < density
                ? palette[rng.Next(1, palette.Length)]
                : Air;
        }

        return voxels;
    }

    /// <summary>A chunk full of one material with an air border around it.</summary>
    public static PaddedChunk SolidChunk(int material)
    {
        var chunk = new PaddedChunk();
        chunk.Fill(material);
        chunk.FillBorder(Air);
        return chunk;
    }
}
