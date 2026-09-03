using Godot;
using Overburden.Core.Meshing;

namespace Overburden.Game.Rendering;

/// <summary>
/// The one place Godot types meet mesher output.
/// </summary>
/// <remarks>
/// <see cref="GreedyMesher"/> emits plain <c>float[]</c> and <c>int[]</c> so it can be
/// tested headlessly and so a dedicated server can link Core without meshing anything.
/// The cost of that is this conversion — and it is also what makes reusing
/// <see cref="MeshBuffer"/>'s arrays safe, since Godot copies out of them here rather
/// than holding onto them.
/// </remarks>
/// <remarks>
/// <para>
/// <b>The two shade channels live in the vertex <c>COLOR</c> array, and M5a verified that
/// against Godot 4.7 rather than assuming it.</b> <c>AddSurfaceFromArrays</c> is called with
/// no flags and the resulting surface does <em>not</em> carry
/// <c>ARRAY_FLAG_COMPRESS_ATTRIBUTES</c>, so UV2 stays float32 and colours round-trip exactly
/// at 8 bits; a render probe confirmed no sRGB conversion is applied to <c>COLOR</c> under a
/// <c>ShaderMaterial</c>. Both storage options therefore worked, and COLOR was chosen for a
/// reason precision does not reach: two channels sharing one interpolated float cannot be
/// unpacked after interpolation. See <see cref="MeshBuffer.Shades"/>.
/// </para>
/// </remarks>
public static class ChunkMeshConverter
{
    /// <summary>
    /// Builds an <see cref="ArrayMesh"/> with up to two surfaces, opaque first. Returns
    /// null when there is nothing to draw, which is the common case for chunks that are
    /// entirely air or entirely buried.
    /// </summary>
    public static ArrayMesh? ToArrayMesh(
        ChunkMesh mesh,
        Material opaqueMaterial,
        Material transparentMaterial)
    {
        var arrayMesh = new ArrayMesh();

        AddSurface(arrayMesh, mesh.Opaque, opaqueMaterial);
        AddSurface(arrayMesh, mesh.Transparent, transparentMaterial);

        return arrayMesh.GetSurfaceCount() > 0 ? arrayMesh : null;
    }

    private static void AddSurface(ArrayMesh target, MeshBuffer buffer, Material material)
    {
        if (buffer.IsEmpty) return;

        int vertexCount = buffer.VertexCount;

        var vertices = new Vector3[vertexCount];
        var normals = new Vector3[vertexCount];
        var uvs = new Vector2[vertexCount];
        var uv2s = new Vector2[vertexCount];
        var colors = new Color[vertexCount];

        for (int i = 0; i < vertexCount; i++)
        {
            vertices[i] = new Vector3(
                buffer.Vertices[i * 3 + 0],
                buffer.Vertices[i * 3 + 1],
                buffer.Vertices[i * 3 + 2]);

            normals[i] = new Vector3(
                buffer.Normals[i * 3 + 0],
                buffer.Normals[i * 3 + 1],
                buffer.Normals[i * 3 + 2]);

            uvs[i] = new Vector2(buffer.Uvs[i * 2 + 0], buffer.Uvs[i * 2 + 1]);
            uv2s[i] = new Vector2(buffer.Uv2s[i * 2 + 0], buffer.Uv2s[i * 2 + 1]);

            colors[i] = new Color(
                buffer.Shades[i * 2 + 0] / 255f,
                buffer.Shades[i * 2 + 1] / 255f,
                0f,
                1f);
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.TexUV2] = uv2s;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.Index] = buffer.Indices[..buffer.IndexCount];

        target.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        target.SurfaceSetMaterial(target.GetSurfaceCount() - 1, material);
    }
}
