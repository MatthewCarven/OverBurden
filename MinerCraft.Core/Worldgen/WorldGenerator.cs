using MinerCraft.Core.Content;
using MinerCraft.Core.Voxels;

namespace MinerCraft.Core.Worldgen;

/// <summary>
/// Turns a chunk position into voxels.
/// </summary>
/// <remarks>
/// <para>
/// <b>A pure function of its arguments.</b> <see cref="GenerateTerrain"/> reads the seed,
/// the settings and the position it was given, and nothing else — no cache, no neighbour
/// lookup, no field mutated between calls. That is what lets chunks generate in any order,
/// on any thread, with no locking, and it is easy to lose by accident the first time a
/// feature wants to know what its neighbour did. When that day comes the answer is a
/// second <see cref="GenerationStage"/>, not a peek sideways.
/// </para>
/// <para>
/// Determinism rests entirely on <see cref="Hash64"/>. Read the warning there before
/// substituting anything for it.
/// </para>
/// </remarks>
public sealed class WorldGenerator
{
    private const int Size = VoxelConstants.ChunkSize;

    private readonly WorldGenSettings _settings;
    private readonly StrataTable _strata;
    private readonly ulong _surfaceSeed;
    private readonly ulong _caveSeed;

    public WorldGenerator(WorldGenSettings settings, StrataTable strata)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(strata);

        _settings = settings;
        _strata = strata;

        // Derive an independent seed per feature, so changing cave settings cannot shift
        // the surface and vice versa.
        _surfaceSeed = Hash64.Mix(settings.Seed ^ 0x5EA51DE0UL);
        _caveSeed = Hash64.Mix(settings.Seed ^ 0xCA1E5000UL);
    }

    public WorldGenSettings Settings => _settings;

    /// <summary>
    /// Voxel Y of the topmost solid voxel in the column at <paramref name="worldX"/>,
    /// <paramref name="worldZ"/>, before caves are carved.
    /// </summary>
    /// <remarks>
    /// Depends only on the two horizontal coordinates, which is what makes two chunks
    /// meeting at a boundary agree about the surface without either knowing the other
    /// exists.
    /// </remarks>
    public int SurfaceHeightAt(int worldX, int worldZ)
    {
        float noise = GradientNoise.Fbm2D(
            _surfaceSeed,
            worldX * _settings.SurfaceFrequency,
            worldZ * _settings.SurfaceFrequency,
            _settings.SurfaceOctaves,
            _settings.Lacunarity,
            _settings.Gain);

        return _settings.SeaLevelVoxels + (int)MathF.Round(noise * _settings.SurfaceAmplitudeVoxels);
    }

    /// <summary>True if the voxel at this world position is carved out by a cave.</summary>
    public bool IsCaveAt(int worldX, int worldY, int worldZ, int surfaceHeight)
    {
        if (!_settings.CavesEnabled)
        {
            return false;
        }

        int depth = surfaceHeight - worldY;
        if (depth < _settings.CaveSurfaceMarginVoxels)
        {
            return false;
        }

        float noise = GradientNoise.Fbm3D(
            _caveSeed,
            worldX * _settings.CaveFrequency,
            worldY * _settings.CaveFrequency,
            worldZ * _settings.CaveFrequency,
            _settings.CaveOctaves,
            _settings.Lacunarity,
            _settings.Gain);

        return noise > _settings.CaveThreshold;
    }

    /// <summary>Generates the <see cref="GenerationStage.Terrain"/> stage of one chunk.</summary>
    public VoxelChunk GenerateTerrain(ChunkPos chunk)
    {
        int chunkMinY = chunk.Y << VoxelConstants.ChunkSizeLog2;
        int chunkMaxY = chunkMinY + Size - 1;
        int originX = chunk.X << VoxelConstants.ChunkSizeLog2;
        int originZ = chunk.Z << VoxelConstants.ChunkSizeLog2;

        Span<int> heights = stackalloc int[Size * Size];
        int highest = int.MinValue;
        int lowest = int.MaxValue;

        for (int lz = 0; lz < Size; lz++)
        {
            for (int lx = 0; lx < Size; lx++)
            {
                int height = SurfaceHeightAt(originX + lx, originZ + lz);
                heights[(lz * Size) + lx] = height;

                if (height > highest)
                {
                    highest = height;
                }

                if (height < lowest)
                {
                    lowest = height;
                }
            }
        }

        // The sky fast path, and the reason it is worth computing the heightmap first.
        // Most chunks in a world with sky above it are entirely above the terrain, and a
        // uniform-air chunk costs one int and touches no memory at all. Caves cannot
        // change this: they only ever remove solid material, and there is none here.
        if (highest < chunkMinY)
        {
            return new VoxelChunk(MaterialIds.AirNumericId);
        }

        var voxels = new VoxelChunk(MaterialIds.AirNumericId);

        for (int lz = 0; lz < Size; lz++)
        {
            for (int lx = 0; lx < Size; lx++)
            {
                int surface = heights[(lz * Size) + lx];
                if (surface < chunkMinY)
                {
                    // This whole column is sky within this chunk. Leave it as the air the
                    // chunk already holds.
                    continue;
                }

                int topSolid = Math.Min(surface, chunkMaxY);

                // Y is the contiguous axis in chunk storage, so walking a column upward is
                // walking memory forward. See VoxelPos.Index.
                for (int worldY = chunkMinY; worldY <= topSolid; worldY++)
                {
                    if (IsCaveAt(originX + lx, worldY, originZ + lz, surface))
                    {
                        continue;
                    }

                    int material = _strata.MaterialAtDepth(surface - worldY);
                    voxels.Set(lx, worldY - chunkMinY, lz, material);
                }
            }
        }

        // A chunk that came out entirely solid — or entirely air, if caves ate all of it —
        // collapses back to the uniform representation here rather than needing its own
        // detection pass on the way in.
        voxels.Compact();
        return voxels;
    }

    /// <summary>
    /// Applies <see cref="GenerationStage.Features"/> to a chunk whose neighbours have all
    /// completed <see cref="GenerationStage.Terrain"/>.
    /// </summary>
    /// <remarks>
    /// Deliberately empty. The stage exists so that the pipeline, the barrier and the chunk
    /// state machine are built around two stages from the start; adding the second one
    /// after everything assumes a single stage is the expensive version. Ore veins and
    /// cross-chunk cave systems land here.
    /// </remarks>
    public void ApplyFeatures(ChunkPos chunk, VoxelChunk voxels)
    {
        ArgumentNullException.ThrowIfNull(voxels);
        _ = chunk;
    }
}
