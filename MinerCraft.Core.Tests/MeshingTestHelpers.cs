using MinerCraft.Core.Content;
using MinerCraft.Core.Meshing;
using MinerCraft.Core.Voxels;

namespace MinerCraft.Core.Tests;

/// <summary>One decoded quad, in voxel units rather than metres.</summary>
internal readonly record struct DecodedQuad(
    int Face,
    int Plane,
    int UMin,
    int UMax,
    int VMin,
    int VMax)
{
    public int Area => (UMax - UMin) * (VMax - VMin);
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

            quads.Add(new DecodedQuad(
                face,
                min[axis],
                min[uAxis], max[uAxis],
                min[vAxis], max[vAxis]));
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

    /// <summary>A chunk full of one material with an air border around it.</summary>
    public static PaddedChunk SolidChunk(int material)
    {
        var chunk = new PaddedChunk();
        chunk.Fill(material);
        chunk.FillBorder(Air);
        return chunk;
    }
}
