namespace MinerCraft.Core.Worldgen;

/// <summary>
/// How far a chunk has been generated.
/// </summary>
/// <remarks>
/// <para>
/// Features that cross a chunk boundary — an ore vein, a cave system, a ravine later —
/// cannot be placed by a chunk that only knows about itself, or the two halves disagree
/// and the seam is visible. The fix is a barrier: <b>a chunk may advance to stage N+1 only
/// once all its neighbours have completed stage N</b>, so a feature placed at stage 2 can
/// write into neighbours whose terrain already exists.
/// </para>
/// <para>
/// Enforcing that barrier is streaming's job and arrives with M2c. The stages exist now,
/// and <see cref="Features"/> currently places nothing, because retrofitting a pipeline
/// after worldgen assumes single-stage generation is a rewrite of the generator — see
/// docs/M2-PLAN.md.
/// </para>
/// </remarks>
public enum GenerationStage : byte
{
    /// <summary>Nothing generated. The chunk does not exist yet.</summary>
    None = 0,

    /// <summary>
    /// Heightmap, strata and caves. Purely a function of position and seed, so it needs no
    /// neighbours and can run in any order on any thread.
    /// </summary>
    Terrain = 1,

    /// <summary>
    /// Cross-chunk features. Requires every neighbour to have reached
    /// <see cref="Terrain"/>. Places nothing yet.
    /// </summary>
    Features = 2,
}

/// <summary>
/// Everything that shapes the terrain, in one immutable record.
/// </summary>
/// <remarks>
/// Frequencies are per voxel, so a frequency of 0.01 means one noise lattice cell per 100
/// voxels — 25 metres at 0.25 m voxels. Keeping them in voxel units rather than metres
/// avoids a conversion in the innermost loop of generation.
/// </remarks>
public sealed record WorldGenSettings
{
    public required ulong Seed { get; init; }

    /// <summary>Voxel Y that a completely flat world would have as its topmost solid voxel.</summary>
    public int SeaLevelVoxels { get; init; } = 0;

    /// <summary>How far the surface deviates from <see cref="SeaLevelVoxels"/>, in voxels.</summary>
    public float SurfaceAmplitudeVoxels { get; init; } = 48.0f;

    public float SurfaceFrequency { get; init; } = 0.004f;

    public int SurfaceOctaves { get; init; } = 4;

    public float Lacunarity { get; init; } = 2.0f;

    public float Gain { get; init; } = 0.5f;

    public bool CavesEnabled { get; init; } = true;

    public float CaveFrequency { get; init; } = 0.02f;

    public int CaveOctaves { get; init; } = 2;

    /// <summary>
    /// Cave noise above this carves air. Higher means rarer caves.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The useful range is much narrower than "roughly [-1, 1]" suggests. Measured over
    /// 216,000 samples at the default frequency and octaves, the normalised fBm spans only
    /// [-0.48, 0.58], with p50 at 0.03 and p99 at 0.50 — so a threshold of 0.55 carves
    /// about one voxel in a thousand, which is indistinguishable from caves being off.
    /// 0.42 sits near p95 and carves roughly 5% of underground volume.
    /// </para>
    /// <para>
    /// Thresholding fBm gives blobby, poorly-connected pockets rather than tunnels. That is
    /// a known limitation of the technique and fine for M2; connected cave systems want
    /// ridged or worm noise, and are a stage-2 feature once the pipeline can place things
    /// across chunk boundaries.
    /// </para>
    /// </remarks>
    public float CaveThreshold { get; init; } = 0.42f;

    /// <summary>
    /// Caves are suppressed within this many voxels of the surface, fading in below it.
    /// </summary>
    /// <remarks>
    /// Without it, cave noise crossing the threshold near the surface bites craters out of
    /// the landscape from above, which reads as terrain damage rather than as a cave.
    /// Entrances still form where a cave runs close to the surface from underneath.
    /// </remarks>
    public int CaveSurfaceMarginVoxels { get; init; } = 8;
}
