using Overburden.Core.Content;
using Overburden.Core.Lighting;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>A world whose unedited terrain is flat at a stated height.</summary>
internal sealed class FlatSurface(int surfaceY) : ISurfaceHeights
{
    public int SurfaceHeightAt(int worldX, int worldZ) => surfaceY;
}

/// <summary>
/// A box of chunks with voxels, light grids and sky columns, wired the way M5c will wire them.
/// </summary>
/// <remarks>
/// <para>
/// Everything outside the box reads as not loaded, which is the situation the real streamer is
/// permanently in and the one the propagator has to stop at cleanly.
/// </para>
/// <para>
/// <b>The edit path here is deliberately the one M5c has to implement</b>: write the voxel,
/// update the sky column, rebuild the affected chunks' derived data collecting the cells that
/// actually moved, invalidate exactly those, drain. If the incremental result ever disagrees
/// with a from-scratch flood, it is this sequence that is wrong.
/// </para>
/// </remarks>
internal sealed class LightTestWorld : ILightChunks, IVoxelWorld
{
    private readonly Dictionary<ChunkPos, VoxelChunk> _voxels = [];
    private readonly Dictionary<ChunkPos, ChunkLight> _light = [];
    private readonly Dictionary<(int X, int Z), SkyColumn> _columns = [];
    private readonly ChunkPos _min;
    private readonly ChunkPos _max;

    // Reused rather than allocated per rebuild. A PaddedChunk is 34³ ints — 157 KB — and the
    // first version of this fixture allocated one per chunk per edit, which was most of why a
    // single test could run the host out of memory.
    private readonly PaddedChunk _padded = new();
    private readonly VoxelChunk?[] _neighbourhood = new VoxelChunk?[PaddedChunk.NeighbourhoodSize];

    public LightTestWorld(
        ChunkPos min, ChunkPos max, MaterialTable materials, ISurfaceHeights heights)
    {
        _min = min;
        _max = max;
        Materials = materials;
        Propagator = new LightPropagator(this);

        for (int x = min.X; x <= max.X; x++)
        for (int y = min.Y; y <= max.Y; y++)
        for (int z = min.Z; z <= max.Z; z++)
        {
            var chunk = new ChunkPos(x, y, z);
            _voxels[chunk] = new VoxelChunk(MaterialIds.AirNumericId);
            _light[chunk] = new ChunkLight();
        }

        for (int x = min.X; x <= max.X; x++)
        for (int z = min.Z; z <= max.Z; z++)
        {
            var column = new SkyColumn(x, z);
            column.Seed(heights);
            _columns[(x, z)] = column;
        }
    }

    public MaterialTable Materials { get; }

    public LightPropagator Propagator { get; }

    public IEnumerable<ChunkPos> Chunks => _light.Keys;

    public ChunkLight? LightAt(ChunkPos chunk) => _light.GetValueOrDefault(chunk);

    public SkyColumn Column(int chunkX, int chunkZ) => _columns[(chunkX, chunkZ)];

    public bool TryGetMaterial(VoxelPos voxel, out int materialId)
    {
        if (_voxels.TryGetValue(voxel.Chunk, out VoxelChunk? chunk))
        {
            var (lx, ly, lz) = voxel.Local;
            materialId = chunk.Get(lx, ly, lz);
            return true;
        }

        materialId = MaterialIds.AirNumericId;
        return false;
    }

    public int Material(VoxelPos voxel) =>
        TryGetMaterial(voxel, out int material) ? material : MaterialIds.AirNumericId;

    /// <summary>Writes a voxel with no light bookkeeping, for building a world before lighting it.</summary>
    public void Place(VoxelPos voxel, int material)
    {
        if (!_voxels.TryGetValue(voxel.Chunk, out VoxelChunk? chunk)) return;

        var (lx, ly, lz) = voxel.Local;
        chunk.Set(lx, ly, lz, material);
    }

    /// <summary>Fills every voxel in the box from a function of its global position.</summary>
    public void Fill(Func<VoxelPos, int> material)
    {
        foreach ((ChunkPos position, VoxelChunk chunk) in _voxels)
        {
            VoxelPos origin = position.OriginVoxel;

            for (int lz = 0; lz < VoxelConstants.ChunkSize; lz++)
            for (int lx = 0; lx < VoxelConstants.ChunkSize; lx++)
            for (int ly = 0; ly < VoxelConstants.ChunkSize; ly++)
            {
                chunk.Set(lx, ly, lz,
                    material(new VoxelPos(origin.X + lx, origin.Y + ly, origin.Z + lz)));
            }
        }
    }

    /// <summary>Builds derived data everywhere and floods from scratch. The starting state.</summary>
    public void Light()
    {
        foreach (ChunkPos chunk in _light.Keys) BuildDerived(chunk, changed: null);
        Reflood();
    }

    /// <summary>Clears every value and floods from every source. The definition of the field.</summary>
    public void Reflood()
    {
        foreach (ChunkLight light in _light.Values) light.ClearLight();
        foreach (ChunkPos chunk in _light.Keys) Propagator.SeedSources(chunk);
        Propagator.Drain();
    }

    /// <summary>
    /// One edit, maintained incrementally — the path the oracle is checking.
    /// </summary>
    /// <remarks>
    /// <b>The rebuild scope is two different shapes, and conflating them is expensive.</b> A
    /// voxel's conductance reaches only the chunk it is in, plus a neighbour across whichever
    /// faces it actually touches — so an interior voxel, which is 30 in 32, rebuilds exactly one
    /// chunk. A change to the column's sky height is not local at all: it moves the sky flag on
    /// cells all the way up and down that chunk column, and nowhere else. The first version of
    /// this rebuilt the 3×3 horizontal footprint over the full loaded height for every edit,
    /// which is the union of both shapes and about forty times the work.
    /// </remarks>
    /// <returns>Every cell whose derived data moved — the exact repair scope.</returns>
    public IReadOnlyList<LightCellPos> Edit(VoxelPos voxel, int material)
    {
        if (!_voxels.TryGetValue(voxel.Chunk, out VoxelChunk? chunk)) return [];

        var (lx, ly, lz) = voxel.Local;
        if (chunk.Get(lx, ly, lz) == material) return [];

        chunk.Set(lx, ly, lz, material);

        ChunkPos position = voxel.Chunk;
        bool skyMoved = _columns[(position.X, position.Z)]
            .OnVoxelChanged(voxel, this, Materials);

        var changed = new List<LightCellPos>();

        // A neighbour chunk only when the voxel actually sits against that face.
        int minX = lx == 0 ? -1 : 0, maxX = lx == VoxelConstants.ChunkMask ? 1 : 0;
        int minY = ly == 0 ? -1 : 0, maxY = ly == VoxelConstants.ChunkMask ? 1 : 0;
        int minZ = lz == 0 ? -1 : 0, maxZ = lz == VoxelConstants.ChunkMask ? 1 : 0;

        for (int dx = minX; dx <= maxX; dx++)
        for (int dy = minY; dy <= maxY; dy++)
        for (int dz = minZ; dz <= maxZ; dz++)
        {
            BuildDerived(position.Offset(dx, dy, dz), changed);
        }

        if (skyMoved)
        {
            for (int cy = _min.Y; cy <= _max.Y; cy++)
            {
                BuildDerived(new ChunkPos(position.X, cy, position.Z), changed);
            }
        }

        foreach (LightCellPos cell in changed) Propagator.Invalidate(cell);

        Propagator.Drain();
        return changed;
    }

    /// <summary>Takes a chunk's grid away, as a horizontal unload does. Voxels stay.</summary>
    public void DropLight(ChunkPos chunk)
    {
        _light.Remove(chunk);
        Propagator.ForgetCache();
    }

    /// <summary>
    /// Gives a chunk a fresh grid and wires it in exactly the way M5c's pipeline does.
    /// </summary>
    /// <remarks>
    /// <b><see cref="LightPropagator.SpreadInto"/> is the step that is easy to leave out</b>, and
    /// leaving it out is invisible in a fixture that builds every grid before lighting anything —
    /// which is what this one does everywhere else, and what M5b never had reason not to do. The
    /// pipeline cannot: chunks arrive one at a time in whatever order the workers finish, so a
    /// neighbour routinely floods light at a chunk whose grid does not exist yet.
    /// </remarks>
    public void RestoreLight(ChunkPos chunk)
    {
        if (!_voxels.ContainsKey(chunk)) return;

        _light[chunk] = new ChunkLight();
        Propagator.ForgetCache();
        Propagator.SpreadInto(chunk);

        var changed = new List<LightCellPos>();
        BuildDerived(chunk, changed);
        foreach (LightCellPos cell in changed) Propagator.Invalidate(cell);

        Propagator.Drain();
    }

    private void BuildDerived(ChunkPos position, ICollection<LightCellPos>? changed)
    {
        if (!_light.TryGetValue(position, out ChunkLight? light)) return;

        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
        {
            _neighbourhood[PaddedChunk.NeighbourIndex(dx, dy, dz)] =
                _voxels.GetValueOrDefault(position.Offset(dx, dy, dz));
        }

        // Air for a missing neighbour, matching the mesh job. It cannot matter: a chunk that is
        // not loaded has no light grid either, so the propagation stops at that face regardless
        // of what its conductance bit says.
        _padded.CopyFrom(_neighbourhood, MaterialIds.AirNumericId);

        light.BuildDerived(
            _padded, Materials, position, _columns[(position.X, position.Z)], changed);
    }

    /// <summary>
    /// Re-derives every column's open height by scanning the voxels downward from the top of the
    /// loaded box. The definition <see cref="SkyColumn.OnVoxelChanged"/> maintains incrementally.
    /// </summary>
    /// <remarks>
    /// Assumes the top of the box is open sky, which every fixture here arranges by putting the
    /// surface well below it. That assumption is the whole reason the real game cannot do this
    /// and needs the analytic seed instead: with a vertical radius of two chunks there is no top
    /// of the world to scan from.
    /// </remarks>
    public void RecolumnFromVoxels()
    {
        int top = ((_max.Y + 1) << VoxelConstants.ChunkSizeLog2) - 1;
        int bottom = _min.Y << VoxelConstants.ChunkSizeLog2;

        foreach ((int chunkX, int chunkZ) in _columns.Keys)
        {
            SkyColumn column = _columns[(chunkX, chunkZ)];
            int originX = chunkX << VoxelConstants.ChunkSizeLog2;
            int originZ = chunkZ << VoxelConstants.ChunkSizeLog2;

            for (int lz = 0; lz < VoxelConstants.ChunkSize; lz++)
            for (int lx = 0; lx < VoxelConstants.ChunkSize; lx++)
            {
                int y = top + 1;
                while (y > bottom
                       && !Materials.IsSolid(
                           Material(new VoxelPos(originX + lx, y - 1, originZ + lz))))
                {
                    y--;
                }

                column.SetOpenAboveY(lx, lz, y);
            }
        }
    }

    /// <summary>
    /// Every column's incrementally maintained open height must equal a fresh scan of the voxels.
    /// </summary>
    public void AssertSkyHeightsMatchAScan()
    {
        var incremental = _columns.ToDictionary(
            entry => entry.Key,
            entry => Heights(entry.Value));

        RecolumnFromVoxels();

        foreach ((var key, short[] scanned) in _columns.ToDictionary(
                     entry => entry.Key, entry => Heights(entry.Value)))
        {
            Assert.True(
                scanned.AsSpan().SequenceEqual(incremental[key]),
                $"column {key} drifted from what the voxels say");
        }

        static short[] Heights(SkyColumn column)
        {
            var values = new short[VoxelConstants.ChunkSize * VoxelConstants.ChunkSize];
            for (int lz = 0; lz < VoxelConstants.ChunkSize; lz++)
            for (int lx = 0; lx < VoxelConstants.ChunkSize; lx++)
            {
                values[(lz * VoxelConstants.ChunkSize) + lx] =
                    checked((short)column.OpenAboveY(lx, lz));
            }

            return values;
        }
    }

    public int Sky(LightCellPos cell) => Value(cell, LightChannel.Sky);

    public int Block(LightCellPos cell) => Value(cell, LightChannel.Block);

    public int Value(LightCellPos cell, LightChannel channel) =>
        LightAt(cell.Chunk)?.Get(cell.LocalIndex, channel) ?? 0;

    public Dictionary<ChunkPos, byte[]> Snapshot() =>
        _light.ToDictionary(entry => entry.Key, entry => entry.Value.SnapshotLight());

    /// <summary>
    /// The strong oracle: every cell satisfies the propagation invariant against its own
    /// neighbours.
    /// </summary>
    /// <remarks>
    /// <para>
    /// M5-PLAN's oracle is "incremental equals from-scratch", and that is a good test — but it
    /// runs the <em>same add pass</em> as the thing it checks, so it can only ever catch the
    /// un-light pass being wrong. This one shares no code at all: the fixpoint
    /// <c>V = max(S, max over conducting neighbours (V(n) − attenuation))</c> is unique, so
    /// checking it cell by cell is a complete statement of correctness with no propagation in
    /// it. Same category as M4a's sealed room — a boolean over a constructed situation rather
    /// than an agreement between two implementations.
    /// </para>
    /// <para>
    /// The attenuation rule is written out again here rather than called, for the reason
    /// <c>MeshingTestHelpers.IsVisible</c> is: a reference that shares reasoning with its
    /// subject proves nothing.
    /// </para>
    /// </remarks>
    public void AssertFixpoint()
    {
        foreach ((ChunkPos position, ChunkLight light) in _light)
        {
            for (int index = 0; index < LightCellPos.Volume; index++)
            {
                var (lx, ly, lz) = LightCellPos.FromIndex(index);
                var cell = LightCellPos.FromChunkLocal(position, lx, ly, lz);

                foreach (LightChannel channel in new[] { LightChannel.Sky, LightChannel.Block })
                {
                    int expected = light.Source(index, channel);

                    for (int face = 0; face < 6; face++)
                    {
                        if (!light.Conducts(index, face)) continue;

                        LightCellPos neighbour = Neighbour(cell, face);
                        ChunkLight? grid = LightAt(neighbour.Chunk);
                        if (grid is null) continue;

                        int value = grid.Get(neighbour.LocalIndex, channel);

                        // Full skylight falls straight down for nothing; everything else costs
                        // a metre. "Down" is stated as a coordinate comparison rather than a
                        // face index so this does not inherit the subject's face table.
                        bool freeFall = channel == LightChannel.Sky
                                        && value == ChunkLight.MaxLevel
                                        && cell.Y == neighbour.Y - 1;

                        int carried = freeFall ? value : value - 1;
                        if (carried > expected) expected = carried;
                    }

                    Assert.True(
                        expected == light.Get(index, channel),
                        $"{cell} {channel}: holds {light.Get(index, channel)}, "
                        + $"neighbours and its own source say {expected}");
                }
            }
        }
    }

    /// <summary>Conductance must agree from both sides of every internal face.</summary>
    /// <remarks>
    /// Derived independently per cell rather than computed once and mirrored, so this is a real
    /// check on the derivation rather than a restatement of it. An asymmetric face would let
    /// light in through a wall it could not leave by, which the fixpoint check would then report
    /// as a violation somewhere else entirely.
    /// </remarks>
    public void AssertConductanceIsSymmetric()
    {
        foreach ((ChunkPos position, ChunkLight light) in _light)
        {
            for (int index = 0; index < LightCellPos.Volume; index++)
            {
                var (lx, ly, lz) = LightCellPos.FromIndex(index);
                var cell = LightCellPos.FromChunkLocal(position, lx, ly, lz);

                for (int face = 0; face < 6; face++)
                {
                    LightCellPos neighbour = Neighbour(cell, face);
                    ChunkLight? grid = LightAt(neighbour.Chunk);
                    if (grid is null) continue;

                    Assert.True(
                        light.Conducts(index, face)
                            == grid.Conducts(neighbour.LocalIndex, face ^ 1),
                        $"{cell} face {face} disagrees with {neighbour} face {face ^ 1}");
                }
            }
        }
    }

    /// <summary>Face order matches VoxelFace: 0=+X, 1=−X, 2=+Y, 3=−Y, 4=+Z, 5=−Z.</summary>
    public static LightCellPos Neighbour(LightCellPos cell, int face) => face switch
    {
        0 => cell.Offset(1, 0, 0),
        1 => cell.Offset(-1, 0, 0),
        2 => cell.Offset(0, 1, 0),
        3 => cell.Offset(0, -1, 0),
        4 => cell.Offset(0, 0, 1),
        _ => cell.Offset(0, 0, -1),
    };
}
