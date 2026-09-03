using System;
using Godot;
using Overburden.Core.Voxels;

namespace Overburden.Game.Rendering;

/// <summary>
/// A wireframe box drawn around the voxel the player is pointing at.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is an instrument, not decoration.</b> A voxel is 0.25 m; at any normal distance
/// that is a few pixels, and without a box around the target you cannot tell whether a swing
/// landed where you aimed or one voxel over. Every judgement about whether mining feels good
/// — how big the bite is, whether the hardness gate reads, whether the crosshair lies — is
/// unreliable made without it, which is why docs/M3-PLAN.md puts it in the milestone rather
/// than in a polish pass.
/// </para>
/// <para>
/// One instance for the whole game, moved and hidden rather than created and freed. The
/// target changes every frame the player turns their head, and a RID churned at frame rate is
/// the shape of cost <see cref="MeshFreeQueue"/> exists to avoid.
/// </para>
/// </remarks>
public sealed class VoxelHighlight : IDisposable
{
    /// <summary>
    /// How much bigger than the voxel the box is drawn.
    /// </summary>
    /// <remarks>
    /// Just enough to sit outside the surface. Drawn at exactly voxel size the lines are
    /// coplanar with the faces they outline and z-fight into a dashed mess that moves as the
    /// camera does — which looks like a rendering bug rather than like a highlight.
    /// </remarks>
    private const float Inflation = 1.03f;

    private readonly ArrayMesh _mesh;
    private readonly StandardMaterial3D _material;
    private readonly float _defaultSizeMetres;
    private Rid _instance;
    private (Vector3 Corner, float Size)? _shown;

    /// <summary>The 0.25 m outline around the voxel a swing would take.</summary>
    public VoxelHighlight(World3D world)
        : this(world, VoxelConstants.VoxelSize, new Color(0.02f, 0.02f, 0.03f, 0.85f))
    {
    }

    /// <summary>
    /// An outline of any size, so that placement can show the metre it would fill.
    /// </summary>
    /// <remarks>
    /// <b>The brush cell needs its own box or the player cannot see the lattice they are building
    /// on.</b> A brush lands on the 1 m grid wherever inside a cell you pointed, so without an
    /// outline of the cell itself the snap reads as the game ignoring the aim — which is exactly
    /// the thing M6d has to judge, and it cannot be judged if it is invisible.
    /// </remarks>
    public VoxelHighlight(World3D world, float sizeMetres, Color colour)
    {
        _defaultSizeMetres = sizeMetres;

        _material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = colour,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,

            // Off, because the box is drawn around the *targeted* voxel and the useful case
            // is aiming into a hole: with culling on, the far edges vanish and the outline
            // stops reading as a box at exactly the moment it matters.
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };

        _mesh = BuildUnitCubeWireframe();
        _mesh.SurfaceSetMaterial(0, _material);

        _instance = RenderingServer.InstanceCreate();
        RenderingServer.InstanceSetScenario(_instance, world.Scenario);
        RenderingServer.InstanceSetBase(_instance, _mesh.GetRid());
        RenderingServer.InstanceSetVisible(_instance, false);
    }

    /// <summary>The minimum corner currently outlined, or null when nothing is targeted.</summary>
    public Vector3? Target => _shown?.Corner;

    /// <summary>Outlines one voxel.</summary>
    public void ShowAt(VoxelPos voxel)
    {
        (float x, float y, float z) = voxel.MinCornerMetres;
        ShowAt(new Vector3(x, y, z), VoxelConstants.VoxelSize);
    }

    /// <summary>Outlines a box of this highlight's default size with its minimum corner here.</summary>
    public void ShowAt(Vector3 minCornerMetres) => ShowAt(minCornerMetres, _defaultSizeMetres);

    /// <summary>
    /// Outlines a box of <paramref name="sizeMetres"/> with its minimum corner here.
    /// </summary>
    /// <remarks>
    /// <b>The size is per show rather than per instance</b>, because the placement outline changes
    /// size with a held key: precision fills one voxel and a brush fills a metre, and the player
    /// has to be able to see which before committing. One outline changing scale beats a second
    /// RenderingServer instance to switch between — see the class remarks about RID churn.
    /// </remarks>
    public void ShowAt(Vector3 minCornerMetres, float sizeMetres)
    {
        if (_shown == (minCornerMetres, sizeMetres))
        {
            return;
        }

        _shown = (minCornerMetres, sizeMetres);

        float size = sizeMetres * Inflation;
        float offset = (size - sizeMetres) * 0.5f;

        RenderingServer.InstanceSetTransform(
            _instance,
            new Transform3D(
                Basis.Identity.Scaled(new Vector3(size, size, size)),
                minCornerMetres - new Vector3(offset, offset, offset)));

        RenderingServer.InstanceSetVisible(_instance, true);
    }

    public void Hide()
    {
        if (_shown is null)
        {
            return;
        }

        _shown = null;
        RenderingServer.InstanceSetVisible(_instance, false);
    }

    /// <summary>The twelve edges of a 0..1 cube, as a line list.</summary>
    /// <remarks>
    /// A unit cube scaled by the transform, rather than a mesh rebuilt per voxel: the size
    /// never changes, only where it sits.
    /// </remarks>
    private static ArrayMesh BuildUnitCubeWireframe()
    {
        var corners = new Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            corners[i] = new Vector3((i >> 0) & 1, (i >> 1) & 1, (i >> 2) & 1);
        }

        var vertices = new System.Collections.Generic.List<Vector3>(24);
        for (int a = 0; a < 8; a++)
        {
            for (int b = a + 1; b < 8; b++)
            {
                // Two corners share an edge exactly when they differ in one bit, so the
                // twelve edges fall out of the numbering with no table to get wrong.
                if (int.PopCount(a ^ b) != 1)
                {
                    continue;
                }

                vertices.Add(corners[a]);
                vertices.Add(corners[b]);
            }
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
        return mesh;
    }

    public void Dispose()
    {
        if (_instance.IsValid)
        {
            RenderingServer.FreeRid(_instance);
            _instance = default;
        }

        _mesh.Dispose();
        _material.Dispose();
    }
}
