namespace MinerCraft.Core.Meshing;

/// <summary>
/// Vertex and index arrays for one render surface, in plain floats and ints.
/// </summary>
/// <remarks>
/// <para>Deliberately Godot-free. That keeps the mesher testable headlessly, and means a
/// dedicated server can link Core without ever meshing anything.</para>
///
/// <para>Buffers are grown and then <b>reused</b> via <see cref="Reset"/> rather than
/// rented from an <c>ArrayPool</c>. Pooling earns its keep when a buffer's lifetime is
/// unpredictable; here the mesher owns these for its whole life and the Godot bridge
/// copies out of them, so plain reuse is both simpler and strictly less work — steady
/// state allocates nothing either way.</para>
/// </remarks>
public sealed class MeshBuffer
{
    private const int InitialQuads = 256;

    public float[] Vertices { get; private set; } = new float[InitialQuads * 4 * 3];
    public float[] Normals { get; private set; } = new float[InitialQuads * 4 * 3];
    public float[] Uvs { get; private set; } = new float[InitialQuads * 4 * 2];

    /// <summary>Per-vertex (texture array layer, light × ambient occlusion).</summary>
    public float[] Uv2s { get; private set; } = new float[InitialQuads * 4 * 2];

    public int[] Indices { get; private set; } = new int[InitialQuads * 6];

    public int VertexCount { get; private set; }

    public int IndexCount { get; private set; }

    public int QuadCount => IndexCount / 6;

    public bool IsEmpty => IndexCount == 0;

    public void Reset()
    {
        VertexCount = 0;
        IndexCount = 0;
    }

    /// <summary>
    /// Appends one quad. Corners must be ordered <c>p, p+du, p+du+dv, p+dv</c>, which
    /// <see cref="VoxelFace"/>'s U/V choice makes counter-clockwise seen from outside.
    /// </summary>
    /// <param name="uExtent">Quad width in voxels — becomes the U texture coordinate, so
    /// a merged quad tiles rather than stretching. This is the part an atlas cannot do.</param>
    /// <param name="vExtent">Quad height in voxels.</param>
    public void AddQuad(
        ReadOnlySpan<float> corners,
        ReadOnlySpan<float> normal,
        float uExtent,
        float vExtent,
        float layer,
        float shade)
    {
        EnsureCapacity();

        int baseVertex = VertexCount;
        int v3 = VertexCount * 3;
        int v2 = VertexCount * 2;

        // UVs follow the corner order and flip V, matching the M0 convention.
        Span<float> us = [0f, uExtent, uExtent, 0f];
        Span<float> vs = [vExtent, vExtent, 0f, 0f];

        for (int i = 0; i < 4; i++)
        {
            Vertices[v3 + i * 3 + 0] = corners[i * 3 + 0];
            Vertices[v3 + i * 3 + 1] = corners[i * 3 + 1];
            Vertices[v3 + i * 3 + 2] = corners[i * 3 + 2];

            Normals[v3 + i * 3 + 0] = normal[0];
            Normals[v3 + i * 3 + 1] = normal[1];
            Normals[v3 + i * 3 + 2] = normal[2];

            Uvs[v2 + i * 2 + 0] = us[i];
            Uvs[v2 + i * 2 + 1] = vs[i];

            Uv2s[v2 + i * 2 + 0] = layer;
            Uv2s[v2 + i * 2 + 1] = shade;
        }

        VertexCount += 4;

        // Reverses the counter-clockwise corner order into Godot's clockwise front-face
        // convention — the same mapping M0 used and that was verified on screen.
        Indices[IndexCount + 0] = baseVertex + 0;
        Indices[IndexCount + 1] = baseVertex + 2;
        Indices[IndexCount + 2] = baseVertex + 1;
        Indices[IndexCount + 3] = baseVertex + 0;
        Indices[IndexCount + 4] = baseVertex + 3;
        Indices[IndexCount + 5] = baseVertex + 2;
        IndexCount += 6;
    }

    private void EnsureCapacity()
    {
        if (VertexCount * 3 + 12 <= Vertices.Length && IndexCount + 6 <= Indices.Length)
        {
            return;
        }

        Vertices = Grow(Vertices, VertexCount * 3);
        Normals = Grow(Normals, VertexCount * 3);
        Uvs = Grow(Uvs, VertexCount * 2);
        Uv2s = Grow(Uv2s, VertexCount * 2);
        Indices = Grow(Indices, IndexCount);
    }

    private static T[] Grow<T>(T[] source, int used)
    {
        var grown = new T[source.Length * 2];
        Array.Copy(source, grown, used);
        return grown;
    }
}

/// <summary>
/// A chunk's geometry, split by render pass.
/// </summary>
/// <remarks>
/// The split exists from the start even though nothing is transparent until water arrives
/// at M8, because transparent geometry needs back-to-front sorting and its own material —
/// and retrofitting the separation later would touch every emit path in the mesher.
/// </remarks>
public sealed class ChunkMesh
{
    public MeshBuffer Opaque { get; } = new();

    public MeshBuffer Transparent { get; } = new();

    public bool IsEmpty => Opaque.IsEmpty && Transparent.IsEmpty;

    public int QuadCount => Opaque.QuadCount + Transparent.QuadCount;

    public void Reset()
    {
        Opaque.Reset();
        Transparent.Reset();
    }
}
