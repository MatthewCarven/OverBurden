using Overburden.Core.Content;
using Overburden.Core.Fluid;
using Overburden.Core.Structure;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// A dictionary of chunks with five materials, for the support field: air, stone (carries 30),
/// dirt (carries 8), water (a fluid, not solid) and rubble (carries 2, M9c-1's fill). Chunks can
/// leave and come back.
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
    public const int Rubble = 4;

    private readonly Dictionary<ChunkPos, VoxelChunk> _chunks = [];
    private readonly HashSet<ChunkPos> _departed = [];

    public MaterialTable Materials { get; } = MaterialTable.ForTesting(
        isOpaque: [false, true, true, false, true],
        isFluid: [false, false, false, true, false],
        textureLayer: [0, 0, 0, 0, 0],
        hardness: [0f, 3f, 0.8f, 0f, 0.5f],
        supportDistance: [0, 30, 8, 0, 2],
        supportCurve:
        [
            [],
            [new SupportPoint(2f, 24f), new SupportPoint(10f, 12f)],
            [new SupportPoint(1f, 4f), new SupportPoint(3f, 1f)],
            [],
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

    /// <summary>Solid voxels, and the sum of their Y — M9c-1's two oracles over every chunk that exists and is here.</summary>
    public (long Solid, long SumY) Census()
    {
        long solid = 0;
        long sumY = 0;
        foreach ((ChunkPos position, VoxelChunk chunk) in _chunks)
        {
            if (_departed.Contains(position))
            {
                continue;
            }

            if (chunk.IsUniform)
            {
                if (Materials.IsSolid(chunk.UniformValue))
                {
                    solid += VoxelConstants.ChunkVolume;
                    // 32 × 32 columns, each summing originY + 0 .. originY + 31.
                    long originY = position.OriginVoxel.Y;
                    const long n = VoxelConstants.ChunkSize;
                    sumY += n * n * ((n * originY) + (n * (n - 1) / 2));
                }

                continue;
            }

            VoxelPos origin = position.OriginVoxel;
            for (int z = 0; z < VoxelConstants.ChunkSize; z++)
            for (int x = 0; x < VoxelConstants.ChunkSize; x++)
            for (int y = 0; y < VoxelConstants.ChunkSize; y++)
            {
                if (Materials.IsSolid(chunk.Get(x, y, z)))
                {
                    solid++;
                    sumY += origin.Y + y;
                }
            }
        }

        return (solid, sumY);
    }

    /// <summary>Solid voxels of one material, over every chunk that is here.</summary>
    public long Count(int material)
    {
        long count = 0;
        foreach ((ChunkPos position, VoxelChunk chunk) in _chunks)
        {
            if (_departed.Contains(position))
            {
                continue;
            }

            if (chunk.IsUniform)
            {
                count += chunk.UniformValue == material ? VoxelConstants.ChunkVolume : 0;
                continue;
            }

            for (int index = 0; index < VoxelConstants.ChunkVolume; index++)
            {
                (int x, int y, int z) = VoxelPos.FromIndex(index);
                if (chunk.Get(x, y, z) == material)
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>
    /// The world as an editor: reads that refuse a chunk that is not here, and writes that tell
    /// the region and whoever else asked — the pipeline's hook chain, in miniature.
    /// </summary>
    public sealed class Editor(SupportTestWorld world, SupportRegion? region, Action<VoxelPos, int>? onChanged = null) : IVoxelEditor
    {
        public int Writes { get; private set; }

        public bool TryGetMaterial(VoxelPos voxel, out int materialId)
        {
            VoxelChunk? chunk = world.Get(voxel.Chunk);
            if (chunk is null)
            {
                materialId = Air;
                return false;
            }

            (int lx, int ly, int lz) = voxel.Local;
            materialId = chunk.Get(lx, ly, lz);
            return true;
        }

        public bool SetVoxel(VoxelPos voxel, int materialId)
        {
            VoxelChunk? chunk = world.Get(voxel.Chunk);
            if (chunk is null)
            {
                return false;
            }

            (int lx, int ly, int lz) = voxel.Local;
            if (chunk.Get(lx, ly, lz) == materialId)
            {
                return true;
            }

            chunk.Set(lx, ly, lz, materialId);
            Writes++;
            region?.Edited(voxel.Chunk);
            onChanged?.Invoke(voxel, materialId);
            return true;
        }
    }
}
