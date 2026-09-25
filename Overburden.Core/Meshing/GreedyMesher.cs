using Overburden.Core.Content;
using Overburden.Core.Lighting;
using Overburden.Core.Structure;
using Overburden.Core.Voxels;
using Overburden.Core.Diagnostics;

namespace Overburden.Core.Meshing;

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

    private readonly FaceKey[] _mask = new FaceKey[Size * Size];
    private readonly bool _ambientOcclusion;

    /// <param name="ambientOcclusion">
    /// Off restores the constant-brightness key M1c shipped, which is the A/B that says what
    /// per-vertex AO costs in quads. A test seam, not a setting — the same shape as
    /// <c>--cavestep=1</c> restoring per-voxel cave evaluation.
    /// </param>
    public GreedyMesher(bool ambientOcclusion = true)
    {
        _ambientOcclusion = ambientOcclusion;
    }

    /// <summary>What must match for two adjacent faces to merge into one quad.</summary>
    /// <remarks>
    /// <para>
    /// <b>Two faces merge iff all four corner shades match, and when they do the merge is
    /// exact rather than an approximation.</b> A merged quad only interpolates between its own
    /// four corners, so a quad spanning three different shades would ignore the middle one —
    /// but it cannot arise. Adjacent faces share two corners, and an independently computed
    /// shade at a shared corner is the same number by definition; so requiring all four to
    /// match forces the shade to be constant along whichever axis the run grew. Where light or
    /// occlusion varies, quads simply stop merging, and that is the price M5d measures.
    /// </para>
    /// <para>
    /// The AO channel and the light channel have been in this key since M1c, when both were
    /// constant, for exactly this reason: adding them later would mean rewriting the sweep.
    /// </para>
    /// <para>
    /// <b>Strain is one bit (M9b, decision 11)</b>: a face of a voxel the settled support field
    /// has at risk. Binary rather than graded so it costs quads only where it fires — a graded
    /// deficit would fragment quads across its gradient the way light does — and a face belongs
    /// to its voxel, which is the chunk's own, so no padded field is needed.
    /// </para>
    /// </remarks>
    private readonly record struct FaceKey(int Material, CornerShade Shade, bool Strain)
    {
        /// <summary>Air never produces a face, so the zero value doubles as "empty".</summary>
        public bool HasFace => Material != MaterialIds.AirNumericId;
    }

    private static readonly int WatchMesh = CallWatch.Register("mesh a chunk");

    /// <param name="light">
    /// The chunk's light cells with a border of its neighbours'. Null restores the pre-M5c shade
    /// — full skylight everywhere and no block light — which is the arm of M5d's measurement
    /// that says what putting light in the merge key costs in quads.
    /// </param>
    /// <param name="support">
    /// The chunk's settled support field, or null for no strain anywhere — the pre-M9b mesh, and
    /// the <c>--supporttint=0</c> arm.
    /// </param>
    public void Build(
        PaddedChunk voxels, MaterialTable materials, ChunkMesh output, PaddedLight? light = null, ChunkSupport? support = null)
    {
        using CallWatch.Scope callWatch = CallWatch.Time(WatchMesh);
        output.Reset();

        for (int face = 0; face < VoxelFace.Count; face++)
        {
            SweepFace(face, voxels, materials, light, support, output);
        }
    }

    private void SweepFace(
        int face,
        PaddedChunk voxels,
        MaterialTable materials,
        PaddedLight? light,
        ChunkSupport? support,
        ChunkMesh output)
    {
        for (int slice = 0; slice < Size; slice++)
        {
            if (BuildMask(face, slice, voxels, materials, light, support))
            {
                MergeAndEmit(face, slice, materials, output);
            }
        }
    }

    /// <summary>Marks which faces on this slice are visible. Returns false if none are.</summary>
    private bool BuildMask(
        int face, int slice, PaddedChunk voxels, MaterialTable materials, PaddedLight? light, ChunkSupport? support)
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

                // Only sampled for a face that will actually be emitted: AO costs twelve
                // padded-chunk reads on top of the two visibility already did, and most
                // positions in a slice have no face at all.
                // The face is the chunk's own voxel's — position is inside 0..31 — so the field
                // is asked at the local index, with no border.
                _mask[v * Size + u] = visible
                    ? new FaceKey(
                        here,
                        ShadeFor(face, position, voxels, materials, light),
                        support is not null && support.IsAtRisk(VoxelPos.Index(position[0], position[1], position[2])))
                    : default;

                any |= visible;
            }
        }

        return any;
    }

    /// <summary>
    /// The four corner shades this face contributes to the merge key — two independent switches,
    /// so M5d can price occlusion and light apart rather than only together.
    /// </summary>
    private CornerShade ShadeFor(
        int face,
        ReadOnlySpan<int> position,
        PaddedChunk voxels,
        MaterialTable materials,
        PaddedLight? light)
    {
        if (light is null)
        {
            return _ambientOcclusion
                ? AmbientOcclusion.ShadeFor(
                    voxels, materials, face, position[0], position[1], position[2])
                : CornerShade.FullSky;
        }

        return AmbientOcclusion.ShadeFor(
            voxels, materials, light, face,
            position[0], position[1], position[2],
            occlude: _ambientOcclusion);
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
            key.Shade,
            key.Strain);
    }
}
