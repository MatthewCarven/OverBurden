using System.Numerics;
using Overburden.Core.Content;
using Overburden.Core.Lighting;
using Overburden.Core.Voxels;

namespace Overburden.Core.Worldgen;

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

    /// <summary>
    /// How many times longer the relief field's wavelength is than the surface's own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A region has to be much larger than the hills inside it or it reads as a second set of
    /// hills rather than as somewhere different. At the shipped surface frequency — a 62.5 m
    /// wavelength — this makes a region about 2 km across: four times the 512 m watershed window,
    /// so a basin is analysed inside one region rather than across several, and roughly five
    /// minutes of walking at sprint. Derived rather than a fifteenth setting, because how large
    /// somewhere-else is, is a decision about the game rather than about one world.
    /// </para>
    /// <para>
    /// <b>8 first, and the measurement said no.</b> That put a region at 500 m — the same size as
    /// the watershed window, and small enough that every sample window straddled a peak and a
    /// trough of the relief field, so the roughest place in a world was only 2.7 times the
    /// calmest and the whole thing read as one landscape with noisy edges. The wavelength is what
    /// makes regions legible, not the variation.
    /// </para>
    /// </remarks>
    public const float ReliefFrequencyRatio = 32.0f;

    /// <summary>
    /// The lowest and highest the relief field is taken to go, and where its threshold sits at
    /// <see cref="WorldGenSettings.ReliefRarity"/> 1.
    /// </summary>
    /// <remarks>
    /// <b>±0.6 rather than ±1, and that is the difference between a rarity dial and a dead
    /// one.</b> <see cref="GradientNoise.Sample2D"/> is documented as <em>roughly</em> in [-1, 1]
    /// and in practice spans about ±0.7, so a threshold placed by assuming the full range simply
    /// never fires — which is exactly how the first mask measured "0% hilly" and read as a
    /// plausible setting rather than as a feature that was switched off. These are the numbers the
    /// sweep is calibrated against; moving them moves what every rarity means.
    /// </remarks>
    private const float ReliefLowestSample = -0.6f;

    private const float ReliefReachableSample = 0.6f;

    private const float ReliefRarestThreshold = 0.5f;

    private readonly WorldGenSettings _settings;
    private readonly StrataTable _strata;
    private readonly ulong _surfaceSeed;
    private readonly ulong _caveSeed;
    private readonly ulong _reliefSeed;
    private readonly float _reliefBase;
    private readonly float _reliefRarity;
    private readonly bool _reliefVaries;
    private readonly float _reliefFrequency;
    private readonly float _reliefOffsetX;
    private readonly float _reliefOffsetZ;

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

        // Every one of these is written as a refusal of what is *not* allowed rather than as a
        // test for what is wrong, so that NaN is caught: every comparison with NaN is false, so
        // `rarity > 1` would wave it through and the amplitude would silently turn a whole world
        // into air. M3c's NaN hardness threshold, three fields along.
        float rarity = settings.ReliefRarity;
        if (!(rarity >= 0.0f && rarity <= 1.0f))
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                rarity,
                $"{nameof(WorldGenSettings.ReliefRarity)} must be between 0 and 1 — 0 ramps across "
                + "the whole relief field and 1 leaves only its peaks. Anything outside that (or "
                + "NaN) would put the ground somewhere nobody can predict.");
        }

        float reliefBase = settings.ReliefBaseVoxels;
        float peak = settings.SurfaceAmplitudeVoxels;

        if (!(reliefBase >= 0.0f && reliefBase <= peak))
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                reliefBase,
                $"{nameof(WorldGenSettings.ReliefBaseVoxels)} is the amplitude in the flattest "
                + "places and must be between 0 and "
                + $"{nameof(WorldGenSettings.SurfaceAmplitudeVoxels)} ({peak}), which is the "
                + "amplitude in the most mountainous. A floor above its own ceiling is not a "
                + "world with inverted mountains, it is a sign the two were passed the wrong way "
                + "round.");
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
        _reliefSeed = Hash64.Mix(settings.Seed ^ 0x5E11EF00UL);

        _reliefBase = reliefBase;
        _reliefRarity = rarity;

        // An exact equality, deliberately: this is the switch between "sample a second noise
        // field per column" and "do what this function has always done", so it wants to be the
        // same question the defaults answer rather than a tolerance somebody has to think about.
        _reliefVaries = reliefBase != peak;
        _reliefFrequency = settings.SurfaceFrequency / ReliefFrequencyRatio;

        // Where in its own lattice cell this world's origin sits. Without it every world is
        // identical at the one place everybody starts: gradient noise is *exactly* zero at its
        // lattice points, and world origin is one — so the relief sample at (0, 0) is 0 whatever
        // the seed, and four worlds on four seeds all measured an amplitude at spawn of 34.6
        // voxels, to the digit. A seed offsets its own origin by anything up to a whole region.
        //
        // **It cannot move a uniform world, which is why it is safe**: a uniform world never
        // reaches the sample at all, so every recorded figure in this project — all of them taken
        // on the uniform default — is untouched by construction rather than by care.
        //
        // The same is true of the *surface* noise, where the spawn column has been exactly sea
        // level in every world this project has ever made. That one is left alone deliberately:
        // offsetting it would move every landscape including the fixed profile seed's, and it
        // costs a player nothing.
        _reliefOffsetX = UnitFraction(Hash64.Mix(_reliefSeed ^ 0x0FF5E7A1UL));
        _reliefOffsetZ = UnitFraction(Hash64.Mix(_reliefSeed ^ 0x0FF5E7B2UL));
    }

    public WorldGenSettings Settings => _settings;

    /// <summary>
    /// Voxel Y of the topmost solid voxel in the column at <paramref name="worldX"/>,
    /// <paramref name="worldZ"/>, before caves are carved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Depends only on the two horizontal coordinates, which is what makes two chunks
    /// meeting at a boundary agree about the surface without either knowing the other
    /// exists. The relief field is sampled from the same two coordinates and so keeps that.
    /// </para>
    /// <para>
    /// <b>In a uniform world — <see cref="WorldGenSettings.ReliefBaseVoxels"/> equal to
    /// <see cref="WorldGenSettings.SurfaceAmplitudeVoxels"/> — this is bit for bit the function it
    /// has always been</b>, and deliberately by skipping the arithmetic rather than by
    /// interpolating to exactly one end. The second would also be exact today, and would stop
    /// being the moment anyone reordered the expression; the first cannot drift. It is also what
    /// keeps a uniform world from paying for a noise sample it does not use — this is the hottest
    /// function in the project after the cave lattice, at 1,024 evaluations per sky column.
    /// </para>
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

        if (!_reliefVaries)
        {
            return _settings.SeaLevelVoxels + (int)MathF.Round(noise * _settings.SurfaceAmplitudeVoxels);
        }

        return _settings.SeaLevelVoxels + (int)MathF.Round(noise * SurfaceAmplitudeAt(worldX, worldZ));
    }

    /// <summary>
    /// How far the surface deviates from sea level in this column: between
    /// <see cref="WorldGenSettings.ReliefBaseVoxels"/> in the flat places and
    /// <see cref="WorldGenSettings.SurfaceAmplitudeVoxels"/> in the mountains.
    /// </summary>
    /// <remarks>
    /// <b>One octave of relief field, and the sample is clamped before it is shaped.</b> A single
    /// octave is what a region is — adding detail to it would put hills on the map of where the
    /// hills are — and <see cref="GradientNoise.Sample2D"/> is documented as <em>roughly</em> in
    /// [-1, 1], which is not a guarantee, so the clamp is what keeps the result inside the floor
    /// and the ceiling the caller asked for rather than somewhere past them.
    /// </remarks>
    public float SurfaceAmplitudeAt(int worldX, int worldZ)
    {
        if (!_reliefVaries)
        {
            return _settings.SurfaceAmplitudeVoxels;
        }

        float relief = Math.Clamp(
            GradientNoise.Sample2D(
                _reliefSeed,
                (worldX * _reliefFrequency) + _reliefOffsetX,
                (worldZ * _reliefFrequency) + _reliefOffsetZ),
            -1.0f,
            1.0f);

        float span = _settings.SurfaceAmplitudeVoxels - _reliefBase;
        return _reliefBase + (span * ReliefShare(relief, _reliefRarity));
    }

    /// <summary>
    /// How much of the way from the floor to the ceiling a column at this relief sample gets:
    /// 0 in a plain, 1 on a summit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A threshold that rarity slides, and a square ramp above it.</b> Both a rising threshold
    /// and a rising exponent make mountains rarer, so shipping both would be two dials doing one
    /// job; the threshold is the one with a legible meaning — <em>how much of the map is
    /// mountain</em> — and the exponent is fixed at 2 so the foothills ease in rather than
    /// starting at a crease.
    /// </para>
    /// <para>
    /// <b>The threshold slides across <see cref="ReliefLowestSample"/>..<see
    /// cref="ReliefRarestThreshold"/>, not across [-1, 1]</b>, and that is the whole reason this
    /// dial does anything. Read the remarks on those constants before moving them.
    /// </para>
    /// </remarks>
    public static float ReliefShare(float relief, float rarity)
    {
        float threshold = ReliefLowestSample
            + ((ReliefRarestThreshold - ReliefLowestSample) * rarity);

        float ramp = Math.Clamp(
            (relief - threshold) / (ReliefReachableSample - threshold),
            0.0f,
            1.0f);

        return ramp * ramp;
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

    /// <summary>A hash as a fraction in [0, 1) — twenty-four bits, which is a float's mantissa.</summary>
    private static float UnitFraction(ulong hash) => (hash >> 40) * (1.0f / (1 << 24));

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
