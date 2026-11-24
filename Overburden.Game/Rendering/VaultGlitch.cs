using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Overburden.Core.Building;
using Overburden.Core.Content;
using Overburden.Core.Machines;
using Overburden.Core.Structure;
using Overburden.Core.Voxels;

namespace Overburden.Game.Rendering;

/// <summary>
/// The vault's two glitches, windowed only (IDEAL-MACHINES-PLAN decisions 28 to 30; IM-6's pass, decisions 13 and 14):
/// a fusing vault's body glitches cyan, and a block the dump's pile writes glitches cyan as it appears.
/// </summary>
/// <remarks>
/// <para>
/// <b>The fuse.</b> One node a fusing vault, a shell a hair outside its 1 m body, with the collapse's own shader
/// (<c>CollapseGlitch.gdshader</c>) and a seed from its cell, so two vaults never flicker in step. A paused fuse — its
/// bill paid, waiting on its minute (IM-3b) — holds steady: the same shell on a material whose rate is zero.
/// Updated at each boundary, since a vault's phase only moves at one.
/// </para>
/// <para>
/// <b>The pile.</b> The voxels the piles wrote at the last boundary (<see cref="VaultDump.PileVoxelsLast"/>, at most
/// 512) drawn as one MultiMesh, each instance's seed and fade in its custom data, fading over
/// <see cref="PileFadeSeconds"/>. One node a voxel was the collapse's way, and IM-5's fact 14 priced it at 11.7–21 ms a
/// frame at scale. At most <see cref="MaxPileGlitches"/> at once; the oldest go first.
/// </para>
/// </remarks>
public sealed class VaultGlitch : IDisposable
{
    /// <summary>How long a pile block glitches after it appears.</summary>
    public const double PileFadeSeconds = 1.0;

    /// <summary>The most pile blocks glitching at once: two boundaries' worth of the pile's 512, and then some.</summary>
    public const int MaxPileGlitches = 2048;

    private readonly Node3D _root;
    private readonly ShaderMaterial _burning;
    private readonly ShaderMaterial _steady;
    private readonly ArrayMesh? _body;
    private readonly MultiMesh _multi;
    private readonly Dictionary<BrushCell, (MeshInstance3D Node, bool Paused)> _fusing = [];
    private readonly List<(VoxelPos Voxel, double Born, float Seed)> _pile = [];

    /// <param name="parent">The node the glitches hang under.</param>
    /// <param name="materials">The table the cube is meshed against.</param>
    /// <param name="opaqueMaterial">Any opaque material, for the mesher's face rule; the shader draws over its colour.</param>
    public VaultGlitch(Node parent, MaterialTable materials, int opaqueMaterial)
    {
        Shader glitch = GD.Load<Shader>("res://Rendering/CollapseGlitch.gdshader");
        _burning = new ShaderMaterial { Shader = glitch };
        _steady = new ShaderMaterial { Shader = glitch };
        _steady.SetShaderParameter("glitch_rate", 0.0f);

        // The body's shell: one brush cell, the collapse's mino cube at the brush's size.
        _body = ChunkMeshConverter.ToArrayMesh(PieceMesher.Build([(0, 0, 0)], null, materials, opaqueMaterial, BrushCell.Voxels), _burning, _burning);

        var pileMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://Rendering/PileGlitch.gdshader") };
        ArrayMesh? voxel = ChunkMeshConverter.ToArrayMesh(PieceMesher.Build([(0, 0, 0)], null, materials, opaqueMaterial, 1), pileMaterial, pileMaterial);
        _multi = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseCustomData = true,
            Mesh = voxel,
            InstanceCount = MaxPileGlitches,
            VisibleInstanceCount = 0,
        };

        _root = new Node3D { Name = "VaultGlitch" };
        parent.AddChild(_root);
        _root.AddChild(new MultiMeshInstance3D
        {
            Name = "PileGlitch",
            Multimesh = _multi,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,

            // The instances span the whole loaded box, so the node's own bounds must not cull them.
            CustomAabb = new Aabb(new Vector3(-1e6f, -1e6f, -1e6f), new Vector3(2e6f, 2e6f, 2e6f)),
        });
    }

    /// <summary>Fusing vaults drawn now.</summary>
    public int FusesDrawn => _fusing.Count;

    /// <summary>Of <see cref="FusesDrawn"/>, how many hold steady because their fuse is paused.</summary>
    public int FusesSteady => _fusing.Values.Count(f => f.Paused);

    /// <summary>Pile blocks glitching now.</summary>
    public int PileDrawn => _pile.Count;

    /// <summary>Pile blocks handed over, over the session, and of them how many were dropped past <see cref="MaxPileGlitches"/>.</summary>
    public long PileSeen { get; private set; }

    public long PileDropped { get; private set; }

    /// <summary>At a boundary: a shell on every fusing vault whose chunk is loaded, steady while paused; none on any other.</summary>
    public void UpdateFuses(MachineSet machines, Func<BrushCell, bool> loaded)
    {
        ArgumentNullException.ThrowIfNull(machines);
        ArgumentNullException.ThrowIfNull(loaded);
        HashSet<BrushCell> burning = [];
        foreach (MachineRow row in machines.Rows)
        {
            if (machines.VaultDefinitionOf(row) is not { IsChannelVault: true }
                || !VaultState.TryRead(row, out VaultContents contents)
                || contents.Phase != VaultPhase.Fusing
                || contents.Wreck
                || !loaded(row.Cell))
            {
                continue;
            }

            burning.Add(row.Cell);
            bool paused = row.LastHoldPaid;
            if (_fusing.TryGetValue(row.Cell, out (MeshInstance3D Node, bool Paused) drawn))
            {
                if (drawn.Paused != paused)
                {
                    drawn.Node.MaterialOverride = paused ? _steady : _burning;
                    _fusing[row.Cell] = (drawn.Node, paused);
                }

                continue;
            }

            if (_body is null)
            {
                continue;
            }

            (float x, float y, float z) = row.Cell.OriginVoxel.MinCornerMetres;
            var node = new MeshInstance3D
            {
                Mesh = _body,
                Position = new Vector3(x, y, z),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                MaterialOverride = paused ? _steady : _burning,
            };
            node.SetInstanceShaderParameter("glitch_seed", SeedOf(row.Cell.X, row.Cell.Y, row.Cell.Z));
            _root.AddChild(node);
            _fusing[row.Cell] = (node, paused);
        }

        foreach (BrushCell gone in _fusing.Keys.Where(cell => !burning.Contains(cell)).ToArray())
        {
            _fusing[gone].Node.QueueFree();
            _fusing.Remove(gone);
        }
    }

    /// <summary>At a boundary, after the dump: the blocks the piles just wrote start their second of glitch.</summary>
    public void AddPile(IReadOnlyList<VoxelPos> voxels, double nowSeconds)
    {
        ArgumentNullException.ThrowIfNull(voxels);
        foreach (VoxelPos voxel in voxels)
        {
            _pile.Add((voxel, nowSeconds, SeedOf(voxel.X, voxel.Y, voxel.Z)));
        }

        PileSeen += voxels.Count;
        if (_pile.Count > MaxPileGlitches)
        {
            int over = _pile.Count - MaxPileGlitches;
            _pile.RemoveRange(0, over);
            PileDropped += over;
        }
    }

    /// <summary>Every frame: each pile block's fade, the expired dropped, the MultiMesh rewritten from what is left.</summary>
    public void Frame(double nowSeconds)
    {
        if (_pile.Count == 0)
        {
            if (_multi.VisibleInstanceCount != 0) _multi.VisibleInstanceCount = 0;
            return;
        }

        int expired = 0;
        while (expired < _pile.Count && nowSeconds - _pile[expired].Born >= PileFadeSeconds)
        {
            expired++;
        }

        if (expired > 0)
        {
            _pile.RemoveRange(0, expired);
        }

        for (int i = 0; i < _pile.Count; i++)
        {
            (VoxelPos voxel, double born, float seed) = _pile[i];
            (float x, float y, float z) = voxel.MinCornerMetres;
            _multi.SetInstanceTransform(i, new Transform3D(Basis.Identity, new Vector3(x, y, z)));
            float fade = (float)Math.Clamp(1.0 - ((nowSeconds - born) / PileFadeSeconds), 0.0, 1.0);
            _multi.SetInstanceCustomData(i, new Color(seed, fade, 0f, 0f));
        }

        _multi.VisibleInstanceCount = _pile.Count;
    }

    /// <summary>A seed in [0, 1) from a lattice position, the collapse's hash.</summary>
    private static float SeedOf(int x, int y, int z) =>
        Math.Abs(((x * 73856093) ^ (y * 19349663) ^ (z * 83492791)) % 1000) / 1000f;

    public void Dispose()
    {
        foreach ((MeshInstance3D node, bool _) in _fusing.Values)
        {
            node.QueueFree();
        }

        _fusing.Clear();
        _pile.Clear();
        _root.QueueFree();
    }
}
