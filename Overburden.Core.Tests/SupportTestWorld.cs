using Overburden.Core.Content;
using Overburden.Core.Fluid;
using Overburden.Core.Structure;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// A dictionary of chunks with four materials, for the support field: air, stone (carries 30),
/// dirt (carries 8) and water (a fluid, not solid). Chunks can leave and come back.
/// </summary>
/// <remarks>
/// The shipped curves for stone and dirt are the fixture's too, so a test that puts a lake over
/// a roof reads the content's own numbers rather than a made-up cliff.
/// </remarks>
internal sealed class SupportTestWorld
{
    public const int Air = MaterialIds.AirNumericId;
    public const int Stone = 1;
    public const int Dirt = 2;
    public const int Water = 3;

    private readonly Dictionary<ChunkPos, VoxelChunk> _chunks = [];
    private readonly HashSet<ChunkPos> _departed = [];

    public MaterialTable Materials { get; } = MaterialTable.ForTesting(
        isOpaque: [false, true, true, false],
        isFluid: [false, false, false, true],
        textureLayer: [0, 0, 0, 0],
        hardness: [0f, 3f, 0.8f, 0f],
        supportDistance: [0, 30, 8, 0],
        supportCurve:
        [
            [],
            [new SupportPoint(2f, 24f), new SupportPoint(10f, 12f)],
            [new SupportPoint(1f, 4f), new SupportPoint(3f, 1f)],
            [],
        ]);

    public IEnumerable<ChunkPos> Chunks => _chunks.Keys;

    /// <summary>The chunk, or null when it does not exist or has left.</summary>
    public VoxelChunk? Get(ChunkPos chunk) =>
        _departed.Contains(chunk) ? null : _chunks.GetValueOrDefault(chunk);

    public bool Exists(ChunkPos chunk) => _chunks.ContainsKey(chunk);

    /// <summary>Creates or replaces a chunk as one material.</summary>
    public void Fill(ChunkPos chunk, int material) => _chunks[chunk] = new VoxelChunk(material);

    /// <summary>Fills a box of chunks, inclusive, as one material.</summary>
    public void FillBox(ChunkPos min, ChunkPos max, int material)
    {
        for (int z = min.Z; z <= max.Z; z++)
        for (int y = min.Y; y <= max.Y; y++)
        for (int x = min.X; x <= max.X; x++)
        {
            Fill(new ChunkPos(x, y, z), material);
        }
    }

    public int Material(VoxelPos voxel)
    {
        VoxelChunk? chunk = _chunks.GetValueOrDefault(voxel.Chunk);
        if (chunk is null)
        {
            return Air;
        }

        (int lx, int ly, int lz) = voxel.Local;
        return chunk.Get(lx, ly, lz);
    }

    /// <summary>Writes one voxel; the chunk must exist.</summary>
    public void Set(VoxelPos voxel, int material)
    {
        (int lx, int ly, int lz) = voxel.Local;
        _chunks[voxel.Chunk].Set(lx, ly, lz, material);
    }

    /// <summary>Writes a box of voxels, inclusive, skipping chunks that do not exist.</summary>
    public HashSet<ChunkPos> SetBox(VoxelPos min, VoxelPos max, int material)
    {
        var touched = new HashSet<ChunkPos>();
        for (int z = min.Z; z <= max.Z; z++)
        for (int y = min.Y; y <= max.Y; y++)
        for (int x = min.X; x <= max.X; x++)
        {
            var voxel = new VoxelPos(x, y, z);
            if (!_chunks.ContainsKey(voxel.Chunk))
            {
                continue;
            }

            Set(voxel, material);
            touched.Add(voxel.Chunk);
        }

        return touched;
    }

    public void Depart(ChunkPos chunk) => _departed.Add(chunk);

    public void Return(ChunkPos chunk) => _departed.Remove(chunk);

    public bool IsDeparted(ChunkPos chunk) => _departed.Contains(chunk);

    public SupportRegion NewRegion(SupportRule? rule = null, IWaterTable? table = null) =>
        new(Get, Materials, rule ?? SupportRule.Default, table);

    /// <summary>A lake at one level over every sample.</summary>
    public sealed class FlatTable(double? level) : IWaterTable
    {
        public double? TableAt(int sampleX, int sampleZ) => level;
    }
}
