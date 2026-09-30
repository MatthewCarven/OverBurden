using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using Overburden.Core.Content;
using Overburden.Core.Lighting;
using Overburden.Core.Meshing;
using Overburden.Core.Streaming;
using Overburden.Core.Structure;
using Overburden.Core.Voxels;

namespace Overburden.Game.Rendering;

/// <summary>
/// What a collapse looks like in the air (COLLAPSE-PLAN part c, c-2): every piece in flight drawn
/// as its own small mesh in its own materials, moved a mino at a time and lit from the grid where
/// it is; and the next second's pieces, still rock, drawn over with a glitch.
/// </summary>
/// <remarks>
/// <para>
/// <b>The hand-off waits for the mesh at both ends</b> (decision 5). A piece is shown only once
/// every chunk it left has installed a mesh built after the piece left — until then the rock is
/// still drawn where it was, and showing both z-fights. A landed piece stays drawn until every
/// chunk its rubble went into has installed a mesh built after the landing, or the rubble would
/// blink in a frame late. <b>Counted, not asked:</b> the pass's rule was "meshed with no remesh
/// pending", and on his tower 478 of 509 pieces waited out the cap — a chunk a collapse writes
/// every frame is never caught up. So <see cref="NoteMeshReady"/> counts the meshes each chunk
/// installs, and a wait needs the count to pass what it was at the edit: one mesh more, or two if
/// a remesh was already running then, since that one was built from the voxels before the edit.
/// Half a second still caps either wait, counted.
/// </para>
/// <para>
/// <b>Nothing here is the world.</b> The pictures read the director's flights and the pipeline's
/// chunks and light; a picture is never solid and never written. A quit or a crash loses nothing
/// that is drawn here that the director does not already account for.
/// </para>
/// </remarks>
public sealed class CollapsePictures : IDisposable
{
    /// <summary>The longest a picture waits on a mesh at either end, milliseconds (decision 5's estimate).</summary>
    public const long HandOffCapMillis = 500;

    private sealed class Picture
    {
        public required MeshInstance3D Node { get; init; }

        public required ChunkPos[] Left { get; init; }

        /// <summary>The mesh count each chunk must reach, at release and then at landing.</summary>
        public Dictionary<ChunkPos, long> Needed { get; set; } = [];

        public long CreatedMillis { get; init; }

        public bool Shown { get; set; }

        public long LandedMillis { get; set; } = -1;

        public ChunkPos[] Rest { get; set; } = [];

        public VoxelPos DrawnAt { get; set; } = new(int.MinValue, 0, 0);
    }

    private readonly Node3D _root;
    private readonly MaterialTable _materials;
    private readonly ChunkPipeline _pipeline;
    private readonly int _rubbleId;
    private readonly ShaderMaterial _solid;
    private readonly ShaderMaterial _glass;
    private readonly ShaderMaterial _glitch;
    private readonly Dictionary<CollapseFlight, Picture> _pictures = [];
    private readonly Dictionary<(int X, int Y, int Z, int Mino), MeshInstance3D> _corrupt = [];
    private readonly Dictionary<int, ArrayMesh?> _minoCubes = [];
    private readonly HashSet<CollapseFlight> _live = [];
    private readonly Dictionary<(int X, int Y, int Z, int Mino), int> _upcoming = [];
    private readonly List<CollapseFlight> _gone = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Dictionary<ChunkPos, long> _meshes = [];

    /// <summary>A chunk installed a mesh — called from the pipeline's mesh-ready hook, empty meshes included.</summary>
    public void NoteMeshReady(ChunkPos chunk) => _meshes[chunk] = _meshes.GetValueOrDefault(chunk) + 1;

    public CollapsePictures(Node parent, ChunkPipeline pipeline, MaterialTable materials, int rubbleId, ShaderMaterial solid, ShaderMaterial glass)
    {
        _pipeline = pipeline;
        _materials = materials;
        _rubbleId = rubbleId;
        _solid = solid;
        _glass = glass;
        _glitch = new ShaderMaterial { Shader = GD.Load<Shader>("res://Rendering/CollapseGlitch.gdshader") };
        _root = new Node3D { Name = "CollapsePictures" };
        parent.AddChild(_root);
    }

    /// <summary>Pictures made, over the lifetime.</summary>
    public long Made { get; private set; }

    /// <summary>Frames a picture waited to be shown at release, and at landing to be taken down.</summary>
    public long HeldAtRelease { get; private set; }

    /// <inheritdoc cref="HeldAtRelease"/>
    public long HeldAtLanding { get; private set; }

    /// <summary>Waits the half-second cap cut short, at either end.</summary>
    public long CapHits { get; private set; }

    /// <summary>Pictures drawn now, and corrupted voxels drawn now.</summary>
    public int Drawn => _pictures.Values.Count(p => p.Shown);

    /// <inheritdoc cref="Drawn"/>
    public int CorruptVoxels { get; private set; }

    /// <summary>Of the last update, the milliseconds the corruption took, and the worst — the half that rebuilds when the queue moves.</summary>
    public double LastCorruptionMillis { get; private set; }

    /// <inheritdoc cref="LastCorruptionMillis"/>
    public double MaxCorruptionMillis { get; private set; }

    /// <summary>Corrupted minos placed, over the lifetime — a re-cut that leaves a mino queued reuses its overlay.</summary>
    public long CorruptionBuilt { get; private set; }

    /// <summary>Milliseconds the last update took, and the worst.</summary>
    public double LastMillis { get; private set; }

    /// <inheritdoc cref="LastMillis"/>
    public double MaxMillis { get; private set; }

    /// <summary>One frame: pictures made for new flights, moved and lit, handed off at both ends; the corruption rebuilt where the queue changed.</summary>
    public void Update(CollapseDirector director, long worldMillis)
    {
        long started = Stopwatch.GetTimestamp();
        long now = _clock.ElapsedMilliseconds;

        _live.Clear();
        foreach (CollapseEvent ev in director.Events)
        {
            foreach (CollapseFlight flight in ev.InFlight)
            {
                _live.Add(flight);
                if (!_pictures.TryGetValue(flight, out Picture? picture))
                {
                    picture = Make(flight, now);
                    _pictures[flight] = picture;
                }

                if (!picture.Shown)
                {
                    if (CaughtUp(picture.Needed))
                    {
                        picture.Shown = true;
                    }
                    else if (now - picture.CreatedMillis > HandOffCapMillis)
                    {
                        picture.Shown = true;
                        CapHits++;
                    }
                    else
                    {
                        HeldAtRelease++;
                    }
                }

                Place(picture, flight.Anchor, flight.Plan);
                picture.Node.Visible = picture.Shown;
            }
        }

        // Flights gone from the director: landed — held until the rubble's mesh is in — or lost.
        _gone.Clear();
        foreach ((CollapseFlight flight, Picture picture) in _pictures)
        {
            if (_live.Contains(flight))
            {
                continue;
            }

            if (picture.LandedMillis < 0)
            {
                VoxelPos first = flight.Plan.RestVoxels.First();
                bool landed = picture.Shown
                    && _pipeline.TryGetMaterial(first, out int material) && material == _rubbleId;
                if (!landed)
                {
                    _gone.Add(flight);
                    continue;
                }

                picture.LandedMillis = now;
                picture.Rest = [.. flight.Plan.RestVoxels.Select(v => v.Chunk).Distinct()];
                picture.Needed = NeededAfterAnEdit(picture.Rest);
                Place(picture, flight.Plan.Rest, flight.Plan);
            }

            if (CaughtUp(picture.Needed))
            {
                _gone.Add(flight);
            }
            else if (now - picture.LandedMillis > HandOffCapMillis)
            {
                CapHits++;
                _gone.Add(flight);
            }
            else
            {
                HeldAtLanding++;
            }
        }

        foreach (CollapseFlight flight in _gone)
        {
            _pictures[flight].Node.QueueFree();
            _pictures.Remove(flight);
        }

        long corruption = Stopwatch.GetTimestamp();
        UpdateCorruption(director, worldMillis);
        LastCorruptionMillis = Stopwatch.GetElapsedTime(corruption).TotalMilliseconds;
        MaxCorruptionMillis = Math.Max(MaxCorruptionMillis, LastCorruptionMillis);

        LastMillis = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        MaxMillis = Math.Max(MaxMillis, LastMillis);
    }

    /// <summary>Every picture and every corruption taken down — the quit, or the arm turned off.</summary>
    public void Dispose()
    {
        foreach (Picture picture in _pictures.Values)
        {
            picture.Node.QueueFree();
        }

        foreach (MeshInstance3D node in _corrupt.Values)
        {
            node.QueueFree();
        }

        _pictures.Clear();
        _corrupt.Clear();
        _root.QueueFree();
    }

    private Picture Make(CollapseFlight flight, long now)
    {
        LandingPlan plan = flight.Plan;
        ChunkMesh mesh = plan.VoxelCells.Length == flight.Piece.Voxels.Length
            ? PieceMesher.Build(plan.VoxelCells, flight.Materials.Length == plan.VoxelCells.Length ? flight.Materials : null, _materials, _rubbleId)
            : PieceMesher.Build(plan.Shape, null, _materials, _rubbleId);
        var node = new MeshInstance3D
        {
            Mesh = ChunkMeshConverter.ToArrayMesh(mesh, _solid, _glass),
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _root.AddChild(node);
        Made++;
        ChunkPos[] left = [.. flight.Piece.Voxels.Select(v => v.Chunk).Distinct()];
        return new Picture
        {
            Node = node,
            Left = left,
            Needed = NeededAfterAnEdit(left),
            CreatedMillis = now,
        };
    }

    /// <summary>Moves a picture to an anchor, and relights it from the grid when it has moved (decision 3).</summary>
    private void Place(Picture picture, VoxelPos anchor, LandingPlan plan)
    {
        if (picture.DrawnAt == anchor)
        {
            return;
        }

        picture.DrawnAt = anchor;
        const float voxel = VoxelConstants.VoxelSize;
        picture.Node.Position = new Vector3(anchor.X * voxel, anchor.Y * voxel, anchor.Z * voxel);

        // The light at the piece's middle: the grid's cell, sky and block over the grid's top.
        int maxX = 0, maxY = 0, maxZ = 0;
        foreach ((int X, int Y, int Z) s in plan.Shape)
        {
            maxX = Math.Max(maxX, s.X);
            maxY = Math.Max(maxY, s.Y);
            maxZ = Math.Max(maxZ, s.Z);
        }

        VoxelPos middle = anchor.Offset(maxX / 2, maxY / 2, maxZ / 2);
        LightCellPos cell = LightCellPos.FromVoxel(middle);
        ChunkLight? light = _pipeline.Streamer.LightAt(cell.Chunk);
        if (light is not null)
        {
            float sky = light.Get(cell.LocalIndex, LightChannel.Sky) / (float)ChunkLight.MaxLevel;
            float block = light.Get(cell.LocalIndex, LightChannel.Block) / (float)ChunkLight.MaxLevel;
            picture.Node.SetInstanceShaderParameter("piece_light", new Vector2(sky, block));
        }
    }

    /// <summary>
    /// For chunks just edited, the mesh count each must reach before its screen shows the edit: one
    /// more than now, or two if a remesh is running, since that one was built before the edit.
    /// </summary>
    private Dictionary<ChunkPos, long> NeededAfterAnEdit(ChunkPos[] chunks)
    {
        var needed = new Dictionary<ChunkPos, long>(chunks.Length);
        foreach (ChunkPos chunk in chunks)
        {
            bool running = _pipeline.Streamer.TryGet(chunk, out StreamedChunk streamed) && streamed.State == ChunkState.Meshing;
            needed[chunk] = _meshes.GetValueOrDefault(chunk) + (running ? 2 : 1);
        }

        return needed;
    }

    /// <summary>Whether every chunk has installed the meshes it needed to — or has left, and draws nothing to fight with.</summary>
    private bool CaughtUp(Dictionary<ChunkPos, long> needed)
    {
        foreach ((ChunkPos chunk, long count) in needed)
        {
            if (_meshes.GetValueOrDefault(chunk) < count && _pipeline.Streamer.TryGet(chunk, out _))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Decision 7: the next second's pieces of every event, drawn over with the glitch while they
    /// are rock — <b>one overlay a mino, keyed by the mino's place on the lattice</b>. Every re-cut
    /// makes new pieces and regroups the voxels, but the minos stay where they are, so a re-cut
    /// moves only the minos that joined or left the next second. Every overlay of a size shares one
    /// cube: nothing is meshed here after the first of each. The first two builds keyed by piece
    /// rebuilt nearly everything at each re-cut, up to 21 ms and then 11.7 ms a frame at ×16. The
    /// faces between two minos of one piece lie inside the rock and are hidden by it.
    /// </summary>
    private void UpdateCorruption(CollapseDirector director, long worldMillis)
    {
        _upcoming.Clear();
        int voxels = 0;
        foreach (CollapseEvent ev in director.Events)
        {
            foreach (CollapsePiece piece in director.Upcoming(ev, worldMillis))
            {
                voxels += piece.Voxels.Length;
                foreach ((int X, int Y, int Z) m in piece.Minos)
                {
                    _upcoming[(m.X, m.Y, m.Z, piece.MinoVoxels)] = piece.Order;
                }
            }
        }

        foreach (var key in _corrupt.Keys.Where(k => !_upcoming.ContainsKey(k)).ToList())
        {
            _corrupt[key].QueueFree();
            _corrupt.Remove(key);
        }

        foreach (var key in _upcoming.Keys)
        {
            if (_corrupt.ContainsKey(key))
            {
                continue;
            }

            if (!_minoCubes.TryGetValue(key.Mino, out ArrayMesh? cube))
            {
                cube = ChunkMeshConverter.ToArrayMesh(PieceMesher.Build([(0, 0, 0)], null, _materials, _rubbleId, key.Mino), _glitch, _glitch);
                _minoCubes[key.Mino] = cube;
            }

            float metres = VoxelConstants.VoxelSize * key.Mino;
            var node = new MeshInstance3D
            {
                Mesh = cube,
                Position = new Vector3(key.X * metres, key.Y * metres, key.Z * metres),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            node.SetInstanceShaderParameter("glitch_seed", ((key.X * 73856093) ^ (key.Y * 19349663) ^ (key.Z * 83492791)) % 1000 / 1000f);
            _root.AddChild(node);
            _corrupt[key] = node;
            CorruptionBuilt++;
        }

        CorruptVoxels = voxels;
    }
}
