using MinerCraft.Core.Content;
using MinerCraft.Core.Voxels;

namespace MinerCraft.Core.Meshing;

/// <summary>
/// Turns a <see cref="PaddedChunk"/> into the minimum set of quads that draws it.
/// </summary>
/// <remarks>
/// <para>Culled meshing emits one quad per visible face. Greedy meshing merges coplanar
/// runs of identical faces into single large quads, and its output scales with the
/// terrain's <i>surface area</i> rather than its voxel count — which is precisely why
/// 0.25 m voxels are viable at all. A solid chunk goes from 6,144 quads to 6.</para>
///
/// <para>One instance per worker thread. The mask is allocated once and reused, so
/// meshing allocates nothing in steady state.</para>
/// </remarks>
public sealed class GreedyMesher
{
    private const int Size = VoxelConstants.ChunkSize;

    /// <summary>
    /// Full brightness. Light and ambient occlusion arrive at M5; until then these are
    /// constant, but they are <b>in the merge key from the start</b> — two faces may only
    /// merge if their lighting matches as well as their material, and discovering that at
    /// M5 would mean rewriting the sweep.
    /// </summary>
    private const byte FullBright = 255;

    private readonly FaceKey[] _mask = new FaceKey[Size * Size];

    /// <summary>What must match for two adjacent faces to merge into one quad.</summary>
    private readonly record struct FaceKey(int Material, byte Light, byte Ao)
    {
        /// <summary>Air never produces a face, so the zero value doubles as "empty".</summary>
        public bool HasFace => Material != MaterialIds.AirNumericId;
    }

    public void Build(PaddedChunk voxels, MaterialTable materials, ChunkMesh output)
    {
        output.Reset();

        for (int face = 0; face < VoxelFace.Count; face++)
        {
            SweepFace(face, voxels, materials, output);
        }
    }

    private void SweepFace(int face, PaddedChunk voxels, MaterialTable materials, ChunkMesh output)
    {
        for (int slice = 0; slice < Size; slice++)
        {
            if (BuildMask(face, slice, voxels, materials))
            {
                MergeAndEmit(face, slice, materials, output);
            }
        }
    }

    /// <summary>Marks which faces on this slice are visible. Returns false if none are.</summary>
    private bool BuildMask(int face, int slice, PaddedChunk voxels, MaterialTable materials)
    {
        int axis = VoxelFace.Axis[face];
        int uAxis = VoxelFace.UAxis[face];
        int vAxis = VoxelFace.VAxis[face];
        int[] offset = VoxelFace.Offset[face];

        bool any = false;
        Span<int> position = stackalloc int[3];

        for (int v = 0; v < Size; v++)
        {
            for (int u = 0; u < Size; u++)
            {
                position[axis] = slice;
                position[uAxis] = u;
                position[vAxis] = v;

                int here = voxels[position[0], position[1], position[2]];
                int there = voxels[
                    position[0] + offset[0],
                    position[1] + offset[1],
                    position[2] + offset[2]];

                bool visible = IsFaceVisible(here, there, materials);
                _mask[v * Size + u] = visible
                    ? new FaceKey(here, FullBright, FullBright)
                    : default;

                any |= visible;
            }
        }

        return any;
    }

    /// <summary>
    /// Whether the face of <paramref name="here"/> looking towards
    /// <paramref name="there"/> can be seen.
    /// </summary>
    /// <remarks>
    /// Three rules, in order: air has no surface; two voxels of the same material never
    /// show the boundary between them (so a solid region is hollow inside); and anything
    /// behind an opaque neighbour is hidden. Together those also give the right answer for
    /// water against air, water against water, and stone against dirt.
    /// </remarks>
    private static bool IsFaceVisible(int here, int there, MaterialTable materials)
    {
        if (here == MaterialIds.AirNumericId) return false;
        if (here == there) return false;
        return !materials.IsOpaque(there);
    }

    private void MergeAndEmit(int face, int slice, MaterialTable materials, ChunkMesh output)
    {
        for (int v = 0; v < Size; v++)
        {
            int u = 0;
            while (u < Size)
            {
                var key = _mask[v * Size + u];
                if (!key.HasFace)
                {
                    u++;
                    continue;
                }

                // Grow along U first...
                int width = 1;
                while (u + width < Size && _mask[v * Size + u + width] == key)
                {
                    width++;
                }

                // ...then along V, but only in whole rows of that width.
                int height = 1;
                while (v + height < Size && RowMatches(v + height, u, width, key))
                {
                    height++;
                }

                EmitQuad(face, slice, u, v, width, height, key, materials, output);

                // Clearing as we go is what makes overlapping quads structurally
                // impossible rather than merely unlikely.
                for (int clearV = v; clearV < v + height; clearV++)
                {
                    _mask.AsSpan(clearV * Size + u, width).Clear();
                }

                u += width;
            }
        }
    }

    private bool RowMatches(int v, int u, int width, FaceKey key)
    {
        for (int k = 0; k < width; k++)
        {
            if (_mask[v * Size + u + k] != key) return false;
        }

        return true;
    }

    private static void EmitQuad(
        int face,
        int slice,
        int u,
        int v,
        int width,
        int height,
        FaceKey key,
        MaterialTable materials,
        ChunkMesh output)
    {
        int axis = VoxelFace.Axis[face];
        int uAxis = VoxelFace.UAxis[face];
        int vAxis = VoxelFace.VAxis[face];

        // A +facing quad sits on the far side of its voxel, a -facing one on the near side.
        int plane = slice + (VoxelFace.Direction[face] > 0 ? 1 : 0);

        Span<float> p = stackalloc float[3];
        p[axis] = plane;
        p[uAxis] = u;
        p[vAxis] = v;

        // Walking p through the four corners in order p, p+du, p+du+dv, p+dv. The U/V axes
        // are chosen in VoxelFace so that this comes out counter-clockwise from outside.
        const float scale = VoxelConstants.VoxelSize;
        Span<float> corners = stackalloc float[12];

        corners[0] = p[0] * scale; corners[1] = p[1] * scale; corners[2] = p[2] * scale;
        p[uAxis] += width;
        corners[3] = p[0] * scale; corners[4] = p[1] * scale; corners[5] = p[2] * scale;
        p[vAxis] += height;
        corners[6] = p[0] * scale; corners[7] = p[1] * scale; corners[8] = p[2] * scale;
        p[uAxis] -= width;
        corners[9] = p[0] * scale; corners[10] = p[1] * scale; corners[11] = p[2] * scale;

        var buffer = materials.IsOpaque(key.Material) ? output.Opaque : output.Transparent;

        buffer.AddQuad(
            corners,
            VoxelFace.Normal[face],
            width,
            height,
            materials.TextureLayer(key.Material),
            key.Light / 255f * (key.Ao / 255f));
    }
}
