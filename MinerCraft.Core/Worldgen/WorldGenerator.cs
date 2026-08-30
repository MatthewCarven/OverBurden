using System.Numerics;
using MinerCraft.Core.Content;
using MinerCraft.Core.Lighting;
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
/// The cave lattice is the place that constraint is most under threat: it is a buffer of
/// noise samples reused across a whole chunk. It lives in a <c>Span</c> on the calling
/// thread's stack for exactly that reason. Promoting it to a field would make generation
/// order-dependent and two workers would corrupt each other's terrain; <c>Generation
/// OrderDoesNotChangeTheResult</c> is the test standing over that.
/// </para>
/// <para>
/// Determinism rests entirely on <see cref="Hash64"/>. Read the warning there before
/// substituting anything for it.
/// </para>
/// </remarks>
public sealed class WorldGenerator : ISurfaceHeights
{
    private const int Size = VoxelConstants.ChunkSize;

    /// <summary>Lattice samples per axis at the finest step worth shipping.</summary>
    /// <remarks>
    /// The per-chunk lattice is stack-allocated, so its worst case has to be a size a worker
    /// thread can carry. Step 2 is 17³ floats — about 19 KiB — and is the finest step with a
    /// plausible case for it. Step 1 exists only as the exact reference, and its 33³ lattice
    /// falls back to the heap rather than putting 143 KiB on a worker's stack.
    /// </remarks>
    private const int MaxStackPitch = (Size / 2) + 1;

    private const int MaxStackLatticeSamples = MaxStackPitch * MaxStackPitch * MaxStackPitch;

    /// <summary>Lattice samples per axis at step 1, which is the most there can ever be.</summary>
    private const int MaxLatticePitch = Size + 1;

    private readonly WorldGenSettings _settings;
    private readonly StrataTable _strata;
    private readonly ulong _surfaceSeed;
    private readonly ulong _caveSeed;

    // Derived from CaveLatticeStep once, because all three are wanted per voxel. The step is
    // a power of two, so `& _caveStepMask` is the offset within a lattice cell and
    // `>> _caveStepLog2` is the cell index — both of which are correct for negative world
    // coordinates in two's complement, where `/` and `%` are not.
    private readonly int _caveStepMask;
    private readonly int _caveStepLog2;
    private readonly float _caveStepInverse;
    private readonly int _caveLatticePitch;

    public WorldGenerator(WorldGenSettings settings, StrataTable strata)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(strata);

        int step = settings.CaveLatticeStep;
        if (step < 1 || (step & (step - 1)) != 0 || Size % step != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                step,
                $"{nameof(WorldGenSettings.CaveLatticeStep)} must be a power of two that divides the "
                + $"{Size}-voxel chunk size. The cave lattice is anchored in world space, and a chunk "
                + "boundary that is not itself a lattice point would leave two chunks interpolating "
                + "from different corners — a seam that is invisible until it is in a screenshot.");
        }

        _settings = settings;
        _strata = strata;

        _caveStepMask = step - 1;
        _caveStepLog2 = BitOperations.Log2((uint)step);
        _caveStepInverse = 1.0f / step;
        _caveLatticePitch = (Size / step) + 1;

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

    /// <summary>
    /// The cave field at one world position: fBm sampled on the
    /// <see cref="WorldGenSettings.CaveLatticeStep"/> lattice and interpolated between
    /// lattice points.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the definition of the field, not an approximation of one.</b>
    /// <see cref="GenerateTerrain"/> computes the identical value from a buffered lattice
    /// rather than eight fresh samples, and the two agree bit for bit — pinned by
    /// <c>IsCaveAtAgreesWithTheChunkItGenerated</c>. Leaving a separate "exact" definition
    /// around for callers to reach for is how the terrain and the queries that read it drift
    /// apart, which surfaces later as mining a wall that is already air.
    /// </para>
    /// <para>
    /// <b>Linear interpolation, deliberately, not the quintic fade used inside
    /// <see cref="GradientNoise"/>.</b> The fade is there to shape a field out of gradients,
    /// where flattening at lattice points is the point. Reusing it to <i>resample</i> an
    /// existing smooth field is a first-order error — smoothstep is flat near the lattice
    /// points where the true field is not — while plain linear interpolation is second-order
    /// and exact wherever the field is locally linear.
    /// </para>
    /// </remarks>
    public float CaveNoiseAt(int worldX, int worldY, int worldZ)
    {
        int mask = _caveStepMask;
        int x0 = worldX & ~mask;
        int y0 = worldY & ~mask;
        int z0 = worldZ & ~mask;
        int step = mask + 1;

        float tx = (worldX & mask) * _caveStepInverse;
        float ty = (worldY & mask) * _caveStepInverse;
        float tz = (worldZ & mask) * _caveStepInverse;

        // X, then Z, then Y — the same order GenerateTerrain interpolates in, because
        // floating-point addition is not associative and "the same value" has to mean the
        // same bits for the agreement test to be an equality rather than a tolerance.
        float lowZ0 = Lerp(LatticeAt(x0, y0, z0), LatticeAt(x0 + step, y0, z0), tx);
        float lowZ1 = Lerp(LatticeAt(x0, y0, z0 + step), LatticeAt(x0 + step, y0, z0 + step), tx);
        float low = Lerp(lowZ0, lowZ1, tz);

        int y1 = y0 + step;
        float highZ0 = Lerp(LatticeAt(x0, y1, z0), LatticeAt(x0 + step, y1, z0), tx);
        float highZ1 = Lerp(LatticeAt(x0, y1, z0 + step), LatticeAt(x0 + step, y1, z0 + step), tx);
        float high = Lerp(highZ0, highZ1, tz);

        return Lerp(low, high, ty);
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

        return CaveNoiseAt(worldX, worldY, worldZ) > _settings.CaveThreshold;
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

        // The highest voxel anywhere in this chunk that any column could carve: caves start
        // CaveSurfaceMarginVoxels below their own column's surface, and no column here rises
        // above `highest`. If even that voxel is below the chunk, sampling the lattice would
        // be entirely dead work — which is the common case for the band of chunks holding
        // the surface itself.
        int caveCeiling = highest - _settings.CaveSurfaceMarginVoxels;
        bool anyCaves = _settings.CavesEnabled && caveCeiling >= chunkMinY;

        int pitch = _caveLatticePitch;
        int sampleCount = anyCaves ? pitch * pitch * pitch : 0;
        Span<float> latticeScratch = sampleCount <= MaxStackLatticeSamples
            ? stackalloc float[sampleCount]
            : new float[sampleCount];

        if (anyCaves)
        {
            SampleCaveLattice(originX, chunkMinY, originZ, latticeScratch);
        }

        // One column's worth of the field, already interpolated in X and Z. The inner loop
        // then costs a single lerp per voxel instead of a full trilinear resample, because
        // the horizontal weights are constant down a column.
        Span<float> columnField = stackalloc float[MaxLatticePitch];

        var voxels = new VoxelChunk(MaterialIds.AirNumericId);
        float threshold = _settings.CaveThreshold;
        int margin = _settings.CaveSurfaceMarginVoxels;

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

                // Split the column at the surface margin rather than re-testing the depth
                // per voxel: below `caveTop` a voxel may be carved, above it never can be.
                int caveTop = anyCaves ? Math.Min(topSolid, surface - margin) : chunkMinY - 1;

                // Y is the contiguous axis in chunk storage, so walking a column upward is
                // walking memory forward. See VoxelPos.Index.
                if (caveTop >= chunkMinY)
                {
                    BuildColumnField(latticeScratch, lx, lz, columnField);

                    for (int worldY = chunkMinY; worldY <= caveTop; worldY++)
                    {
                        int ly = worldY - chunkMinY;
                        int gy = ly >> _caveStepLog2;
                        float noise = Lerp(
                            columnField[gy],
                            columnField[gy + 1],
                            (ly & _caveStepMask) * _caveStepInverse);

                        if (noise > threshold)
                        {
                            continue;
                        }

                        voxels.Set(lx, ly, lz, _strata.MaterialAtDepth(surface - worldY));
                    }
                }

                for (int worldY = Math.Max(chunkMinY, caveTop + 1); worldY <= topSolid; worldY++)
                {
                    voxels.Set(lx, worldY - chunkMinY, lz, _strata.MaterialAtDepth(surface - worldY));
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

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);

    /// <summary>One raw lattice sample. The world coordinates must be multiples of the step.</summary>
    private float LatticeAt(int worldX, int worldY, int worldZ)
    {
        float frequency = _settings.CaveFrequency;
        return GradientNoise.Fbm3D(
            _caveSeed,
            worldX * frequency,
            worldY * frequency,
            worldZ * frequency,
            _settings.CaveOctaves,
            _settings.Lacunarity,
            _settings.Gain);
    }

    /// <summary>
    /// Fills the chunk's lattice, one sample every <see cref="WorldGenSettings.CaveLatticeStep"/>
    /// voxels, with a closing sample on each far face so interpolation never runs off the end.
    /// </summary>
    /// <remarks>
    /// Indexed <c>[gz][gx][gy]</c> — Y contiguous, the same order and for the same reason as
    /// chunk storage itself, because this is read down columns.
    /// </remarks>
    private void SampleCaveLattice(int originX, int originY, int originZ, Span<float> lattice)
    {
        int pitch = _caveLatticePitch;
        int step = _caveStepMask + 1;

        for (int gz = 0; gz < pitch; gz++)
        {
            int worldZ = originZ + (gz * step);

            for (int gx = 0; gx < pitch; gx++)
            {
                int worldX = originX + (gx * step);
                int baseIndex = ((gz * pitch) + gx) * pitch;

                for (int gy = 0; gy < pitch; gy++)
                {
                    lattice[baseIndex + gy] = LatticeAt(worldX, originY + (gy * step), worldZ);
                }
            }
        }
    }

    /// <summary>
    /// Collapses the lattice to one column by interpolating in X and Z, leaving only the
    /// vertical interpolation to do per voxel.
    /// </summary>
    private void BuildColumnField(ReadOnlySpan<float> lattice, int lx, int lz, Span<float> column)
    {
        int pitch = _caveLatticePitch;
        int gx = lx >> _caveStepLog2;
        int gz = lz >> _caveStepLog2;
        float tx = (lx & _caveStepMask) * _caveStepInverse;
        float tz = (lz & _caveStepMask) * _caveStepInverse;

        int x0z0 = ((gz * pitch) + gx) * pitch;
        int x1z0 = x0z0 + pitch;
        int x0z1 = (((gz + 1) * pitch) + gx) * pitch;
        int x1z1 = x0z1 + pitch;

        for (int gy = 0; gy < pitch; gy++)
        {
            float z0 = Lerp(lattice[x0z0 + gy], lattice[x1z0 + gy], tx);
            float z1 = Lerp(lattice[x0z1 + gy], lattice[x1z1 + gy], tx);
            column[gy] = Lerp(z0, z1, tz);
        }
    }
}
