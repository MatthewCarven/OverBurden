using System.Collections.Generic;
using Godot;
using MinerCraft.Core.Voxels;

namespace MinerCraft.Game.Rendering;

/// <summary>
/// The six axis-aligned faces of a voxel, and their geometry.
/// </summary>
/// <remarks>
/// This table is the seed of the real mesher at M1. It lives here rather than in
/// MinerCraft.Core only because it is expressed in Godot vector types; when the greedy
/// mesher moves to Core it will emit plain float arrays and this becomes the conversion
/// layer.
/// </remarks>
public static class VoxelFaces
{
    public const int Count = 6;

    /// <summary>Outward normal of each face, and the neighbour offset to test against.</summary>
    public static readonly Vector3I[] Normals =
    [
        new(1, 0, 0),   // +X
        new(-1, 0, 0),  // -X
        new(0, 1, 0),   // +Y  (top)
        new(0, -1, 0),  // -Y  (bottom)
        new(0, 0, 1),   // +Z
        new(0, 0, -1),  // -Z
    ];

    /// <summary>
    /// Four corners per face on the unit cube, ordered bottom-left, bottom-right,
    /// top-right, top-left as seen from outside the cube.
    /// </summary>
    public static readonly Vector3[][] Corners =
    [
        // +X — seen from +X, screen right is -Z, up is +Y
        [new(1, 0, 1), new(1, 0, 0), new(1, 1, 0), new(1, 1, 1)],
        // -X — seen from -X, screen right is +Z
        [new(0, 0, 0), new(0, 0, 1), new(0, 1, 1), new(0, 1, 0)],
        // +Y — seen from above, screen right is +X, screen up is -Z
        [new(0, 1, 1), new(1, 1, 1), new(1, 1, 0), new(0, 1, 0)],
        // -Y — seen from below, screen right is +X, screen up is +Z
        [new(0, 0, 0), new(1, 0, 0), new(1, 0, 1), new(0, 0, 1)],
        // +Z
        [new(0, 0, 1), new(1, 0, 1), new(1, 1, 1), new(0, 1, 1)],
        // -Z — seen from -Z, screen right is -X
        [new(1, 0, 0), new(0, 0, 0), new(0, 1, 0), new(1, 1, 0)],
    ];
}

public static class VoxelMeshBuilder
{
    /// <summary>Fully lit. Real light and AO arrive at M5; the channel exists now.</summary>
    private const float FullBright = 1.0f;

    /// <summary>
    /// Builds a solid cube of <paramref name="voxelsPerSide"/> voxels, emitting only
    /// the faces on the outside. This is deliberately a miniature of the M1 mesher:
    /// same face table, same vertex format, same "skip a face whose neighbour is solid"
    /// rule — just with the neighbour test hardcoded to a cube rather than read from
    /// chunk storage.
    /// </summary>
    public static ArrayMesh BuildSolidBlock(
        int voxelsPerSide,
        Material material,
        int topLayer,
        int sideLayer,
        int bottomLayer)
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var uv2s = new List<Vector2>();
        var indices = new List<int>();

        for (int y = 0; y < voxelsPerSide; y++)
        for (int z = 0; z < voxelsPerSide; z++)
        for (int x = 0; x < voxelsPerSide; x++)
        {
            for (int face = 0; face < VoxelFaces.Count; face++)
            {
                var n = VoxelFaces.Normals[face];
                int nx = x + n.X, ny = y + n.Y, nz = z + n.Z;

                // Interior faces are the whole point of culled meshing: a face whose
                // neighbour is solid can never be seen, so it is never emitted.
                bool neighbourIsSolid =
                    nx >= 0 && nx < voxelsPerSide &&
                    ny >= 0 && ny < voxelsPerSide &&
                    nz >= 0 && nz < voxelsPerSide;
                if (neighbourIsSolid) continue;

                AddFace(
                    vertices, normals, uvs, uv2s, indices,
                    face,
                    new Vector3(x, y, z) * VoxelConstants.VoxelSize,
                    face switch { 2 => topLayer, 3 => bottomLayer, _ => sideLayer });
            }
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV2] = uv2s.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(0, material);
        return mesh;
    }

    private static void AddFace(
        List<Vector3> vertices, List<Vector3> normals,
        List<Vector2> uvs, List<Vector2> uv2s, List<int> indices,
        int face, Vector3 originMetres, int layer)
    {
        int baseIndex = vertices.Count;
        var corners = VoxelFaces.Corners[face];
        var normal = new Vector3(
            VoxelFaces.Normals[face].X,
            VoxelFaces.Normals[face].Y,
            VoxelFaces.Normals[face].Z);

        for (int i = 0; i < 4; i++)
        {
            vertices.Add(originMetres + corners[i] * VoxelConstants.VoxelSize);
            normals.Add(normal);
            uv2s.Add(new Vector2(layer, FullBright));
        }

        // Matches the corner order: bottom-left, bottom-right, top-right, top-left.
        uvs.Add(new Vector2(0, 1));
        uvs.Add(new Vector2(1, 1));
        uvs.Add(new Vector2(1, 0));
        uvs.Add(new Vector2(0, 0));

        // Godot's documented convention is CLOCKWISE winding for front faces, and the
        // corner table above is counter-clockwise as seen from outside — so the index
        // order reverses it. If the block renders inside-out, this is the only place to
        // look: swap to (0,1,2)/(0,2,3).
        indices.Add(baseIndex + 0);
        indices.Add(baseIndex + 2);
        indices.Add(baseIndex + 1);
        indices.Add(baseIndex + 0);
        indices.Add(baseIndex + 3);
        indices.Add(baseIndex + 2);
    }
}
