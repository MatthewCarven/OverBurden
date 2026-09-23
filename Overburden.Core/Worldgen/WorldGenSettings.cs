namespace Overburden.Core.Worldgen;

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

    /// <summary>
    /// This chunk's light values are final: its derived data is built and the propagation has
    /// settled. A chunk may mesh once all 26 of its neighbours are here.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A stage rather than a <c>ChunkState</c>, because light is a barrier and not a job</b> —
    /// no new job kind, no new transition, no new way to get stuck mid-pipeline.
    /// </para>
    /// <para>
    /// <b>Its condition carries one global term, and that is forced: a global BFS has no
    /// per-chunk completion event.</b> Two things must hold and only one of them is per chunk —
    /// every chunk within light's reach has derived data, and the propagation has actually run.
    /// The second is global because the queue is. See docs/M5-PLAN.md, where the local-looking
    /// substitute (counting a chunk's own queued cells) is rejected: a chunk with none of its own
    /// queued can still have a neighbour's frontier one step from spilling in, so the count reads
    /// settled while the light is still moving.
    /// </para>
    /// <para>
    /// <b>Building derived data is a different question from reaching this stage, and conflating
    /// them would cost a third margin ring.</b> Derived data needs <em>voxels</em>, not a stage,
    /// so the outermost loaded ring builds it and participates in propagation while never
    /// reaching <see cref="Lit"/> and never meshing — which is exactly what keeps light inside
    /// the visible box exact at a 2-chunk margin.
    /// </para>
    /// </remarks>
    Lit = 3,
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

    /// <summary>
    /// How much <see cref="SurfaceAmplitudeVoxels"/> varies from region to region, as a share
    /// of itself. 0 is a world of uniform lumpiness; 1 lets a region run from dead flat to twice
    /// the base relief.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What makes two worlds feel different is not one number, it is mountains here and plains
    /// there.</b> A per-world amplitude makes a world uniformly lumpy or uniformly flat and gives
    /// you nowhere to walk to; a second, much slower noise field multiplying the amplitude per
    /// column gives a world regions. It is a multiplier rather than a second additive octave
    /// deliberately: an octave adds detail everywhere, where this changes <em>how much of the
    /// existing shape</em> a place gets, which is the thing being asked for.
    /// </para>
    /// <para>
    /// <b>The default is 0, and that is a measurement decision rather than a taste one.</b> Every
    /// recorded figure in this project is a statement about one landscape — the r6 control, the
    /// keystone's at-risk count, "the first forty seconds of the profile seed are dry" — and all
    /// of them are taken on the default settings at a fixed seed. A non-zero default would move
    /// the ground under every one of them with the suite green. So the neutral value is what the
    /// record ships, what an old world reads back, and what a profile run gets; a new world
    /// created to be <em>played</em> is where the shipped value is applied, which is the same rule
    /// and the same reason as the random seed. <c>ReliefVariationIsNeutralByDefault</c> holds the
    /// bit-identity.
    /// </para>
    /// <para>
    /// The field it modulates is one octave at <see cref="WorldGenerator.ReliefFrequencyRatio"/>
    /// times the surface's wavelength — a derived number rather than a fourteenth setting,
    /// because how large a region is, is a decision about the game's shape rather than about
    /// one world.
    /// </para>
    /// </remarks>
    public float ReliefVariation { get; init; } = 0.0f;

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
    /// Caves are suppressed within this many voxels of the surface.
    /// </summary>
    /// <remarks>
    /// Without it, cave noise crossing the threshold near the surface bites craters out of
    /// the landscape from above, which reads as terrain damage rather than as a cave.
    /// Entrances still form where a cave runs close to the surface from underneath.
    /// </remarks>
    public int CaveSurfaceMarginVoxels { get; init; } = 8;

    /// <summary>
    /// Cave noise is evaluated every this many voxels and interpolated in between.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Must be a power of two that divides the chunk size.</b> The lattice is anchored in
    /// world space, so two chunks meeting at a seam interpolate from the same corners and
    /// agree exactly; that only holds if a chunk boundary is itself a lattice point.
    /// <see cref="WorldGenerator"/> throws otherwise rather than generating a world with
    /// invisible seams in it.
    /// </para>
    /// <para>
    /// Evaluating 3D fBm per voxel is what made caves 80% of generation cost: 32,768 voxels
    /// times two octaves times eight lattice corners is a quarter of a million hash-and-dot
    /// operations for one chunk. At <see cref="CaveFrequency"/> 0.02 the field's smallest
    /// octave has a 25-voxel period, so sampling every 4 voxels and interpolating loses very
    /// little — measured, see WORKLOG.md — for a 45× cut in noise evaluations.
    /// </para>
    /// <para>
    /// <b>1 means no interpolation</b> and reproduces per-voxel evaluation exactly, because
    /// every interpolation weight is then zero. It exists as the reference the coarser steps
    /// are measured against, not as a setting to ship.
    /// </para>
    /// </remarks>
    public int CaveLatticeStep { get; init; } = 4;
}
