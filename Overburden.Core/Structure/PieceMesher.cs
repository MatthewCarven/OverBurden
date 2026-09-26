using Overburden.Core.Content;
using Overburden.Core.Meshing;
using Overburden.Core.Voxels;

namespace Overburden.Core.Structure;

/// <summary>
/// A falling piece's mesh (COLLAPSE-PLAN part c, decision 1): one quad per voxel face that shows,
/// through <see cref="MeshBuffer.AddQuad"/> with the greedy mesher's own corner order, so a piece
/// winds and textures exactly as a chunk does — the renderer converts it with the chunk converter.
/// </summary>
/// <remarks>
/// <para>
/// <b>Local to the piece, in metres</b>: a cell at (x, y, z) voxels spans (x, y, z) × the voxel
/// size to one voxel more, so the picture is moved by its anchor alone. UVs run one per voxel, the
/// chunk's tiling. The shade is written full and ignored: the piece shader lights a piece from an
/// instance uniform (decision 3).
/// </para>
/// <para>
/// <b>A face shows</b> where the neighbour cell is not in the piece, and where an opaque voxel
/// meets a see-through one in the piece — glass beside stone shows the stone behind it, as a chunk
/// does. Two see-through voxels hide the face between them.
/// </para>
/// </remarks>
public static class PieceMesher
{
    /// <summary>The mesh of <paramref name="cells"/>, each drawn as <paramref name="materials"/> says; null materials draw every cell as <paramref name="fallback"/>.</summary>
    /// <param name="cellVoxels">Voxels along a cell's edge: 1 for a piece's voxels, a mino's edge for an outline of its minos (the corruption, one quad a mino face).</param>
    public static ChunkMesh Build(
        IReadOnlyList<(int X, int Y, int Z)> cells,
        IReadOnlyList<int>? materials,
        MaterialTable table,
        int fallback = 0,
        int cellVoxels = 1)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(table);
        var mesh = new ChunkMesh();
        var at = new Dictionary<(int X, int Y, int Z), int>(cells.Count);
        for (int i = 0; i < cells.Count; i++)
        {
            at[cells[i]] = materials is not null && i < materials.Count ? materials[i] : fallback;
        }

        CornerShade shade = CornerShade.Uniform(byte.MaxValue, 0);
        Span<float> corners = stackalloc float[12];
        Span<float> p = stackalloc float[3];
        float scale = VoxelConstants.VoxelSize * cellVoxels;
        foreach (((int x, int y, int z), int material) in at)
        {
            bool opaque = table.IsOpaque(material);
            for (int face = 0; face < VoxelFace.Count; face++)
            {
                int[] o = VoxelFace.Offset[face];
                if (at.TryGetValue((x + o[0], y + o[1], z + o[2]), out int neighbour)
                    && (table.IsOpaque(neighbour) || !opaque))
                {
                    continue;
                }

                // GreedyMesher.EmitQuad's corners for a 1 × 1 run: p, p+du, p+du+dv, p+dv on the
                // face's plane, counter-clockwise from outside.
                int axis = VoxelFace.Axis[face];
                int uAxis = VoxelFace.UAxis[face];
                int vAxis = VoxelFace.VAxis[face];
                p[0] = x;
                p[1] = y;
                p[2] = z;
                p[axis] += VoxelFace.Direction[face] > 0 ? 1 : 0;
                corners[0] = p[0] * scale; corners[1] = p[1] * scale; corners[2] = p[2] * scale;
                p[uAxis] += 1;
                corners[3] = p[0] * scale; corners[4] = p[1] * scale; corners[5] = p[2] * scale;
                p[vAxis] += 1;
                corners[6] = p[0] * scale; corners[7] = p[1] * scale; corners[8] = p[2] * scale;
                p[uAxis] -= 1;
                corners[9] = p[0] * scale; corners[10] = p[1] * scale; corners[11] = p[2] * scale;

                (opaque ? mesh.Opaque : mesh.Transparent).AddQuad(
                    corners, VoxelFace.Normal[face], cellVoxels, cellVoxels, table.TextureLayer(material), shade);
            }
        }

        return mesh;
    }
}
