using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using Overburden.Core.Building;
using Overburden.Core.Content;
using Overburden.Core.Fluid;
using Overburden.Core.Hydrology;
using Overburden.Core.Inventory;
using Overburden.Core.Lighting;
using Overburden.Core.Jobs;
using Overburden.Core.Meshing;
using Overburden.Core.Mining;
using Overburden.Core.Persistence;
using Overburden.Core.Physics;
using Overburden.Core.Streaming;
using Overburden.Core.Structure;
using Overburden.Core.Voxels;
using Overburden.Core.Worldgen;
using Overburden.Game.Diagnostics;
using Overburden.Game.Player;
using Overburden.Game.Rendering;
using Overburden.Game.Ui;
using Overburden.Persistence;

namespace Overburden.Game;

/// <summary>
/// M2c entry point: load content, then stream terrain around the camera through the job
/// scheduler and the greedy mesher.
/// </summary>
/// <remarks>
/// Everything that decides <em>what</em> exists lives in <c>Overburden.Core</c>. This class
/// only reports where the camera is, turns finished meshes into <c>ArrayMesh</c>es, and
/// frees RIDs — the Godot half of the seam, and deliberately thin.
/// </remarks>
public partial class Main : Node3D
{
    /// <summary>
    /// Horizontal view radius in chunks. <b>6 since 2026-09-03 — 48 m of draw distance.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// Raised from 4 on Matthew's request, and measured first because this is the axis M5d-1
    /// found a cliff on. Same session, Debug, headless, 20 s measured, <c>--vradius=2</c>:
    /// </para>
    /// <code>
    ///                     r4 @ 6 m/s   r6 @ 6 m/s   r4 @ 20 m/s   r6 @ 20 m/s
    /// chunks viewed          1521         2601         1521          2601
    /// drawn                   167          335            0             0
    /// main                 30.74%       45.66%       75.29%        74.16%
    /// update ms mean        2.152        3.207        5.279         5.198
    /// light ms mean         2.079        3.095        5.236         5.141
    /// first draw            3.80 s       6.66 s       never         never
    /// all settled           4.63 s       7.69 s       never         never
    /// </code>
    /// <para>
    /// <b>The headline is that it does not create a new failure mode.</b> The 20 m/s arms draw
    /// nothing at <em>both</em> radii — that is M5d-1's recorded cliff, which has been there since
    /// M5 and is a property of light deriving on one sim thread, not of the view distance. r6 is
    /// fractionally <em>cheaper</em> there than r4 (74.16% against 75.29%), which is noise around
    /// "both are saturated". <b>So this raise spends headroom that exists rather than crossing a
    /// threshold.</b>
    /// </para>
    /// <para>
    /// <b>What it does cost is stated plainly: first draw roughly doubles</b>, 3.80 s to 6.66 s,
    /// and the main thread goes 30.74% to 45.66% at walking speed. 2,601 chunks loaded against
    /// 1,521 (1.71x) and 335 drawn against 167 (2.01x) — the drawn figure is the one that prices
    /// memory, at ~46 KiB a drawn chunk, so roughly 15 MiB of mesh against 8.
    /// </para>
    /// <para>
    /// <b>Nothing about water moved, and that was checked rather than assumed.</b> The watershed
    /// analysis window (512 m), the rain occlusion field (64 m) and the rain box (48 m) are all
    /// fixed constants that derive nothing from this number. One interaction worth knowing: the
    /// drawn world now reaches exactly as far as the rain box, where before it stopped 16 m short.
    /// </para>
    /// <para>
    /// <b>Every streaming figure recorded before this date is at r4h/2v and stays reproducible
    /// with <c>--radius=4 --vradius=2</c></b>, which is what keeps M2d's through M8's numbers
    /// comparable with each other.
    /// </para>
    /// </remarks>
    [Export] public int ViewDistanceHorizontal { get; set; } = 6;

    /// <summary>
    /// Separate from the horizontal radius on purpose — a mining game's content is below the
    /// player. Kept modest until M2d has measured what the load actually costs.
    /// </summary>
    [Export] public int ViewDistanceVertical { get; set; } = 2;

    /// <summary>Jobs submitted per frame. Bounds the sim-thread cost of a fast traverse.</summary>
    [Export] public int MaxSubmissionsPerFrame { get; set; } = 64;

    private readonly Dictionary<ChunkPos, VoxelRenderInstance> _instances = [];
    private readonly MeshFreeQueue _freeQueue = new();

    private JobScheduler? _scheduler;
    private ChunkPipeline? _pipeline;
    private WorldGenerator? _generator;
    private ViewerId _viewer;
    private FlyCam? _camera;
    private ShaderMaterial? _material;
    private ChunkPos _lastViewerChunk;
    private double _sinceReport;

    private MiningController? _mining;
    private VoxelHighlight? _highlight;
    private readonly List<ToolDefinition> _tiers = [];

    private PlaceController? _place;

    private Hud? _hud;
    private VoxelHighlight? _cellHighlight;
    private readonly VolumetricInventory _inventory = new();
    private bool _placeWhileProfiling;
    private bool _precisePlaceWhileProfiling;
    private long _startingStock;
    private BrushShape _startingBrush = BrushShape.Cube;

    private PlayerController? _player;
    private MaterialTable? _materials;
    private bool _announcedSpawn;

    /// <summary>The world file, or null for a run that saves nothing.</summary>
    /// <remarks>
    /// <b>Owned here rather than by the pipeline</b>, because it is read before the pipeline can
    /// exist: the header carries the ID table, and the table has to be seeded into the registry
    /// before a single material registers. Disposed after the pipeline, which writes through it.
    /// </remarks>
    private IWorldStore? _store;

    /// <summary>What the world file said the player was doing, or null for a new world.</summary>
    private PlayerSave? _savedPlayer;

    /// <summary>Where the camera was on the last live frame, while flying.</summary>
    /// <remarks>
    /// <para>
    /// <b>Cached every frame rather than read when the world is written, and that is a bug fix
    /// rather than a tidy-up.</b> In fly mode the camera is where the player is, so the save has
    /// to ask the camera — but the save also runs from <see cref="_ExitTree"/>, during teardown,
    /// and a played world came back with the player at <c>(0, -1.65, 0)</c> flying: the eye read
    /// as the scene origin. <b>Underground and noclipping, and self-perpetuating</b>, because the
    /// next launch restores that position and writes it back.
    /// </para>
    /// <para>
    /// The rule this is an instance of: <b>state that has to survive a quit must be captured while
    /// the frame is alive.</b> A Godot node during teardown is not a source of truth, and the
    /// walking half of this method never had the problem because it reads
    /// <c>PlayerController.FeetCentre</c>, which is plain C# state.
    /// </para>
    /// </remarks>
    private System.Numerics.Vector3? _flyingEye;

    /// <summary>The settings this world regenerates unstored terrain from. Written back on save.</summary>
    private WorldGenSettings _settings;

    private string _worldName = DefaultWorldName;
    private bool _worldNamed;
    private bool _persist = true;
    private double _autosaveSeconds = DefaultAutosaveSeconds;
    private double _sinceAutosave;

    private StreamingProfiler? _profiler;
    private float _flySpeed;

    /// <summary>The retained-chunk cache's cap, in MiB. Zero is off and is the control arm.</summary>
    /// <remarks>
    /// <b>64 MiB is an estimate, and it is Matthew's to move</b> — docs/CHUNK-CACHE-PLAN.md §4. At
    /// a few KB per cached chunk that is on the order of 20,000 chunks, several times the r6
    /// tracked box, so a turnaround of well over a minute at walking speed comes back from memory.
    /// </remarks>
    private long _chunkCacheMiB = 64;

    /// <summary>
    /// <c>--turnaround=&lt;seconds&gt;</c>: the scripted flythrough reverses every this many
    /// seconds, which is the motion the cache exists for and the one no profile could produce.
    /// </summary>
    private double _turnaroundSeconds;
    private bool _deriveFifo;
    private double _sinceTurnaround;
    private float _flyDirection = 1f;
    private int _turnarounds;

    /// <summary>
    /// The flythrough moves for this many seconds and then stops — the "hanging around" arm,
    /// which <c>--turnaround</c> (reverses) and <c>--speed=0</c> (never moves) cannot produce,
    /// and the case the adaptive radius's grow-back exists for. Zero, the default, never stops.
    /// </summary>
    private double _sprintForSeconds;
    private double _flownSeconds;

    // The adaptive view radius (docs/ADAPTIVE-RADIUS-PLAN.md). Off by default so every recorded
    // streaming figure stays reproducible with no flag; start.cmd opts in, as it does for the
    // light budget. Off, the state sits at the ceiling and the policy is never stepped.
    private bool _adaptiveRadius;
    private int _minRadius = ViewRadiusConfig.DefaultFloor;
    private int _shrinkSlabs = ViewRadiusConfig.DefaultShrinkSlabs;
    private double _growBelowLoad = ViewRadiusConfig.DefaultGrowBelowLoad;
    private double _cadenceSeconds = ViewRadiusConfig.DefaultCadenceSeconds;
    private ViewRadiusConfig _radiusConfig;
    private ViewRadiusState _radiusState;
    private int _radiusShrinks;
    private int _radiusGrows;
    private int _smallestRadius;
    private double[] _secondsAtRadius = [];
    private double _grownSeconds = -1.0;
    private int _backlogPeak;
    private long _backlogSum;
    private int _backlogSamples;
    private double _loadPeak;
    private double _loadSum;

    private int? _caveLatticeStep;
    private TimeSpan? _applyBudget;
    private TimeSpan? _lightBudget;
    private bool _mineWhileProfiling;
    private bool _entangleWhileProfiling;

    private bool _entangledSphereWhileProfiling;

    private float _chunkCooldownSeconds = MiningController.DefaultEntangledChunkCooldownSeconds;
    private bool _fallProfile;
    private bool _ambientOcclusion = true;
    private bool _lightInMergeKey = true;
    private float _skyBrightness = 1f;
    private float _fallHeightMetres = 600f;
    private int _restingSteps;
    private int _restedSteps;
    private int _falls;
    private float? _terminalSpeed;

    /// <summary>
    /// How long this world has been running. <b>The first thing in the project that is a clock.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// Advanced from <c>_Process</c>'s <c>delta</c> and from nowhere else, which is the whole of
    /// "time advances only while the world is open" — <b>a claim that is true by construction here
    /// and would stop being the moment anything set it from wall time.</b> Core cannot test it:
    /// <see cref="WorldClock"/> only moves when something calls <c>Advance</c>, so the assertion
    /// has always had to be about this line. What proves it is the world file, whose
    /// <c>ElapsedMillis</c> is what the last session left rather than what the calendar says.
    /// </para>
    /// <para>
    /// It is created before the world is opened and <em>replaced</em> once the header has been
    /// read, rather than being advanced from zero and corrected. A clock that ran for a few frames
    /// at the wrong time would rain the wrong weather onto the world for those frames, which is
    /// exactly the class of bug this milestone's determinism claims exist to make impossible.
    /// </para>
    /// </remarks>
    private WorldClock _clock = new();

    /// <summary>Multiplies how fast world time runs. A debug seam, not a setting.</summary>
    /// <remarks>
    /// <b>Weather is a pure function of time, so scaling the clock is the one knob that makes a
    /// whole storm visible inside a fifteen-second profile window without touching the weather at
    /// all.</b> That is why it is here and not a change to <see cref="Weather.EpisodeMillis"/>,
    /// which would rewrite every world's history. It is deliberately <em>not</em> M7d's
    /// <c>rainfallScale</c>: that one leaves the sky honest and speeds the water up, this one
    /// speeds the sky up too. A run that uses it is not comparable with one that does not.
    /// </remarks>
    private double _clockScale = 1.0;

    /// <summary>Forces the two weather uniforms, for a screenshot that has to be reproducible.</summary>
    /// <remarks>
    /// <b>A look override and nothing more</b> — it does not touch <see cref="Weather"/>, so no
    /// rain falls into any basin because of it and no volume is affected. Named for what it does
    /// to the screen rather than for weather, because a flag called <c>--weather</c> that silently
    /// failed to make it rain would be the worse kind of instrument. The alternative is hunting
    /// for a stormy <c>--clock=</c>, which is a different seam and also exists.
    /// </remarks>
    private float? _forcedWeather;

    /// <summary>Which way the camera faces at spawn, for a screenshot that needs an angle.</summary>
    private float _spawnYawDegrees;

    /// <summary>How far up or down it looks at spawn. Negative is down.</summary>
    private float _spawnPitchDegrees;

    /// <summary>The old, camera-relative face tint. An A/B seam — see the shader.</summary>
    private bool _tintInViewSpace;

    /// <summary>The sky dome's material, so the overcast can reach the sky as well as the light.</summary>
    private ProceduralSkyMaterial? _skyMaterial;

    /// <summary>The dome's four colours as authored, so dimming scales them rather than compounds.</summary>
    /// <remarks>
    /// <b>Held because the dimming is applied to the material every frame</b>, and a factor applied
    /// to whatever is already there decays the sky to black over a few seconds — a bug that would
    /// look exactly like nightfall arriving and be blamed on the weather function.
    /// </remarks>
    private Color[] _skyColours = [];

    /// <summary>The scene's environment, whose fog is most of what the sky actually looks like.</summary>
    private Godot.Environment? _environment;

    /// <summary>The authored fog colour, held for the same reason the dome's colours are.</summary>
    private Color _baseFogColour = Colors.White;

    /// <summary>This frame's delta, for the water surface's own rebuild timer.</summary>
    /// <remarks>
    /// Threaded through a field rather than an argument because <c>UpdateWater</c> is reached from
    /// <c>UpdateWeather</c>, which already takes one delta and passes it to the clock. Two deltas
    /// in one signature is how a caller ends up advancing the world twice.
    /// </remarks>
    private double _lastFrameSeconds;

    /// <summary>The water surface, or null in a run with no renderer.</summary>
    /// <remarks>
    /// Created beside <see cref="_rain"/> and for the same reason: a headless run has nothing to
    /// draw it with, and building meshes nobody can see would move every figure this project has
    /// recorded since M2d.
    /// </remarks>
    private WaterSurface? _waterSurface;

    /// <summary>The standing water: which basins exist near the player and what is in them.</summary>
    /// <remarks>
    /// <b>Created after the world is open, because it needs the world's clock and its ledger.</b>
    /// A fresh <c>WaterSystem</c> starting at zero on a world that is ten hours old would deliver
    /// ten hours of rain the moment its first window landed, on top of a ledger that already holds
    /// what fell in those hours - see the type's own <c>startMillis</c> remarks.
    /// </remarks>
    private WaterSystem? _water;

    // ---------------------------------------------------------------- M8b: Tier B in the loop

    /// <summary>Tier B: the water that is actually moving, over the streamer, told about edits by the pipeline.</summary>
    /// <remarks>
    /// <b>Created with the pipeline rather than with the Tier A water</b>, because its terrain is
    /// the streamer and its edits come through the pipeline's hook, both of which exist only once
    /// the pipeline does. <c>--nowater</c> turns it off with Tier A: that flag means "no water at
    /// all", and a control arm that left half the water in would price the wrong thing.
    /// </remarks>
    private FluidSimulation? _fluid;

    private FluidSurface? _fluidSurface;

    /// <summary>Wall-clock seconds since the last Tier B tick.</summary>
    private double _fluidSinceTick;

    /// <summary>The most fluid work one frame has paid this session, for the summary a non-profile run prints.</summary>
    /// <remarks>
    /// A frame's worth, not a tick's, since the tick budget slice: the budget bounds this number
    /// at itself plus one granule, and a tick's own cost is spread over however many frames it
    /// took. <see cref="_fluidLongestTickFrames"/> is the other half.
    /// </remarks>
    private double _fluidWorstMillis;

    /// <summary>
    /// <c>--fluidbudget=&lt;ms&gt;</c>: how much of a frame Tier B's tick may take before it
    /// yields and resumes next frame. <see cref="FluidSimulation.DefaultTickBudget"/> by default.
    /// </summary>
    /// <remarks>
    /// The sibling of <c>--lightbudget</c> and <c>--applybudget</c>, and the seam the slice is
    /// priced through. A budget of zero still visits one granule a frame, so the water still
    /// moves — just slowly enough to watch the slicing itself.
    /// </remarks>
    private TimeSpan _fluidBudget = FluidSimulation.DefaultTickBudget;

    /// <summary>
    /// <c>--fluidnear=&lt;rings&gt;</c>: how many chunk rings around the viewer Tier B visits every
    /// tick, the rest of the awake set being deferred past the boundary under a quota. Zero is no
    /// order at all — the default, the control arm, and what every recorded figure was taken in.
    /// </summary>
    /// <remarks>
    /// <b><c>start.cmd</c> passes 2, the way it passes <c>--adaptive=1</c></b>, so the play gets
    /// the feature while every measurement stays at the arm it was recorded in. Two rings is the
    /// 5x5 chunk column of WATER-THREAD-PLAN's slice 1, the box's full height: vertical is nearly
    /// free at r6h/2v and water is vertical.
    /// </remarks>
    private int _fluidNearRings;

    /// <summary>
    /// <c>--fluidfar=&lt;cells&gt;</c>: the least far cells admitted per tick while any near cell
    /// is awake. <see cref="FluidSimulation.DefaultFarFloorCells"/> by default, an estimate; a very
    /// large value is a second control arm.
    /// </summary>
    private int _fluidFarFloor = FluidSimulation.DefaultFarFloorCells;

    /// <summary>
    /// <c>--fluidfield=0</c>: run the tick against the streamer directly, as it did before the
    /// water thread's slice 2. On by default; zero is the control arm.
    /// </summary>
    /// <remarks>
    /// <b>The field is not a speed-up and does not claim to be</b> — <c>--terrain</c> priced the
    /// game's read at 1.47x a baked field's, a third of a visit — it is the terrain a worker can
    /// read, which is what slice 3 needs. The control exists because a substituted terrain is
    /// exactly the kind of change that should be provable to have moved nothing.
    /// </remarks>
    private bool _fluidFieldOn = true;

    private FluidField? _field;

    private readonly HashSet<ChunkPos> _fluidReachable = [];

    /// <summary>
    /// <c>--fieldbakes=<n></c>: at most this many chunk fields baked at one boundary (slice 3c).
    /// Eight is an estimate — ~16–20 ms in Debug — and this is the sweep; a very large value is
    /// the pre-3c arm, which baked the first flooded boundary's dozens in one 230 ms frame.
    /// </summary>
    private int _fieldBakesPerBoundary = 8;

    private readonly List<ChunkPos> _fieldLate = [];

    /// <summary>Chunks whose field landed late — past the cap, or after a decline — and were woken as an arrival.</summary>
    private long _fieldLateWakes;

    private long _fluidBudgetTicks;

    /// <summary>Frames the tick in progress has run over so far; the tick's span once it completes.</summary>
    private int _fluidFramesThisTick;

    /// <summary>The most frames one tick has needed, and how many ticks needed more than one.</summary>
    private int _fluidLongestTickFrames, _fluidSlicedTicks;

    /// <summary>The simulation's counters when the measured window opened, so the window's own figures can be reported.</summary>
    private long _fluidVisitsAtWarm, _fluidTicksAtWarm;

    // The near/far counters are lifetime totals like the rest, so the window keeps its own
    // baseline — a lifetime numerator over a window's denominator is the trap this project has
    // walked into twice.
    private long _fluidNearAtWarm, _fluidFarAtWarm, _fluidThrottledAtWarm;

    private int _fluidSlicedAtWarm, _fluidLongestTickFramesInWindow;

    private bool _fluidWarmSeen;

    /// <summary>
    /// <c>--fluidjob=1</c>: the tick runs as a scheduler job on a worker, the water thread's slice
    /// 3b. Off by default so a profile without the flag stays comparable with every recorded
    /// figure; <c>start.cmd</c> passes it, slice 1's shape. Needs the field, because a worker
    /// reads a captured view of it and cannot read the streamer.
    /// </summary>
    private bool _fluidJobOn;
    private int _fallCells = FluidSimulation.DefaultFallCells;
    private string _fluidSurfaceMode = "mesh";

    private System.Threading.CancellationTokenSource? _fluidJobCts;

    private long _fluidJobSubmittedAt;

    /// <summary>A tick that faulted on a worker freezes the water for the session; printed once.</summary>
    private bool _fluidJobFaulted;

    // The job's own figures: wall time from submission to collection, the worker's visit and walk
    // time, and boundaries that arrived to find the previous tick still out — a slow tick lowers
    // the rate rather than skipping a cell, and this is how often it did.
    private int _fluidJobs, _fluidJobsWaited;
    private double _fluidJobWallSum, _fluidJobWallMax, _fluidJobVisitSum, _fluidJobWalkSum, _fluidJobSettleSum;
    private int _fluidJobsAtWarm, _fluidJobsWaitedAtWarm;
    private double _fluidJobWallSumAtWarm, _fluidJobVisitSumAtWarm, _fluidJobWalkSumAtWarm, _fluidJobSettleSumAtWarm;
    private long _fluidRefusalsAtWarm, _fluidArrivedRefusalsAtWarm, _fluidRequeuedAtWarm;

    // What the boundary itself costs on the sim thread, by pass, so "fluid ms" on the job arm can
    // be read as parts rather than as one number: the settle, the hand-off and the drain, the
    // pour, the field sync, the publish, and the capture with the composition.
    private double _bSettle, _bHandoff, _bPour, _bCollapse, _bLake, _bSync, _bPublish, _bBegin;
    private double _bSettleMax, _bHandoffMax, _bPourMax, _bCollapseMax, _bLakeMax, _bSyncMax, _bPublishMax;
    private int _boundaries;
    private double _bSettleAtWarm, _bHandoffAtWarm, _bPourAtWarm, _bCollapseAtWarm, _bLakeAtWarm, _bSyncAtWarm, _bPublishAtWarm, _bBeginAtWarm;
    private int _boundariesAtWarm;

    /// <summary>The tick interval: 2 Hz, the rate M8-PLAN costed the active set against and M8a measured at.</summary>
    /// <remarks>
    /// <b>The automaton is rate-free, so this is the caller's number and nothing in Core reads
    /// it.</b> A tick that arrives late costs nothing — DESIGN's <i>slower than realtime is
    /// accuracy</i> — which is why a stalled frame carries at most one tick of debt and never a
    /// burst.
    /// </remarks>
    private const double FluidTickSeconds = 0.5;

    /// <summary>Litres one press of the relay pours. Enough to see; not enough to flood a mine.</summary>
    private const long RelayLitres = 10_000;

    /// <summary>Radius Alt+R takes water from, in metres.</summary>
    private const float TakeRadiusMetres = 2f;

    /// <summary>Voxels of radius the current pour lands in. Sized to the litres — see <see cref="RadiusVoxelsFor"/>.</summary>
    private int _relayRadiusVoxels;

    /// <summary>Millilitres the relay still owes, offered again every tick until they fit.</summary>
    /// <remarks>
    /// The bench's rule: the lake pours in as fast as the mouth accepts and what is refused is
    /// offered again next tick. What is <em>delivered</em> the simulation counts as injected; this
    /// is the undelivered remainder, printed beside it, because a relay reporting 10,000 L put
    /// while 9,900 were still pending would be lying by omission.
    /// </remarks>
    private long _relayPendingMillilitres;

    private VoxelPos _relayAt;

    /// <summary>Where Alt+R was pressed, waiting for the boundary; null when nothing is owed.</summary>
    /// <remarks>
    /// Queued the way the pour is (slice 3a): <see cref="FluidSimulation.Remove"/> is a boundary
    /// call and refuses while a tick owns the grid, and a keypress lands on any frame.
    /// </remarks>
    private VoxelPos? _relayTakeAt;

    private bool _relayKeyHeld;

    /// <summary><c>--injectlitres</c>: litres poured at the player once the measured window opens.</summary>
    private long _injectLitres;

    private bool _injected;

    private double _secondsSinceStart;

    /// <summary><c>--fillbasin</c>: litres poured into the basin under the player once the measured window opens.</summary>
    /// <remarks>
    /// The Tier A relay. A breach under a dry basin measures nothing, and waiting for rain to
    /// fill one is not a provocation seam; this is <c>--injectlitres</c> for the other tier, and
    /// it is a reported term for the same reason.
    /// </remarks>
    private long _fillBasinLitres;

    /// <summary><c>--breach</c>: the side, in voxels, of the square shaft bitten under the player once the window opens.</summary>
    private int _breachVoxels;

    private bool _breached;

    // ---------------------------------------------------------------- M9b: the support field in the loop

    /// <summary><c>--support=0</c> builds no region: no derives, no tint, the pre-M9b arm and the control.</summary>
    private bool _supportOn = true;

    /// <summary><c>--supporttint=0</c> derives everything and hands the mesher nothing — the tint's control.</summary>
    private bool _supportTint = true;

    /// <summary>The analysed set, the fields and the join; the pipeline pumps its queue.</summary>
    private SupportRegion? _support;

    /// <summary><c>--bitechunks=&lt;n&gt;</c>: an n × n square of chunks bitten under the player once the window opens.</summary>
    private int _biteChunks;

    /// <summary>
    /// <c>--bitedepth=&lt;chunks&gt;</c>: how many chunks below the player's the bite's top face sits.
    /// Three, because the strata are a metre of soil over stone and a bite one chunk down has a
    /// roof that is partly soil in most columns — fixture 4's picture, where the oracle is the
    /// stone plug of fixture 2.
    /// </summary>
    private int _biteDepth = 3;

    /// <summary><c>--inbite</c>: drop the camera into the bite's centre when it lands, so a screenshot looks up at the roof.</summary>
    private bool _inBite;

    private bool _bitten;
    private double _supportSeconds;
    private bool _supportOpenReported;
    private bool _supportWarmSeen;
    private long _supportDerivesAtWarm;
    private long _supportRegionDerivesAtWarm;
    private long _supportTrivialAtWarm;
    private long _supportRequeuedAtWarm;
    private long _supportDroppedAtWarm;
    private double _supportWorkerAtWarm;
    private double _supportCopyAtWarm;
    private double _supportGatherAtWarm;
    private double _supportApplyAtWarm;
    private int _strainRemeshesAtWarm;
    private int _strainUnchangedAtWarm;
    private readonly long[] _supportTriggersAtWarm = new long[5];

    // ---------------------------------------------------------------- M9c-1b: the fall in the loop

    /// <summary><c>--collapse=0</c> arms every timer and fires none — the warning-only arm, for walking under a marked roof.</summary>
    private bool _collapseOn = true;

    /// <summary><c>--collapseafter=&lt;min&gt;,&lt;max&gt;</c> in world seconds; 2 and 60 are decision 8's estimates.</summary>
    private CollapseWindow _collapseWindow = CollapseWindow.Default;

    /// <summary>The timers and the fall — swept at the fluid boundary, or every frame when there is no water.</summary>
    private CollapseScheduler? _collapse;

    /// <summary>Falls whose landing overlapped the body and the unstick lifted it clear / could not.</summary>
    private int _collapseFreed;
    private int _collapseBuried;

    private double _collapseSweepMillisSum;
    private double _collapseSweepMillisMax;
    private double _collapseFallMillisMax;
    private long _collapseSweepsAtWarm;
    private long _collapseFiredAtWarm;
    private long _collapseVoxelsAtWarm;
    private long _collapseDropAtWarm;
    private long _collapseStaleAtWarm;
    private long _collapseLiftedAtWarm;
    private long _collapseReturnedAtWarm;
    private int _collapseFreedAtWarm;
    private int _collapseBuriedAtWarm;
    private double _collapseSweepMillisAtWarm;

    /// <summary>The basin the breach went into, and its pit's sample — printed on the breach line with what it holds now.</summary>
    private int _breachBasin = Watershed.Outlet;
    private (int X, int Z) _breachSample;

    // ------------------------------------------------------------ M8b-2c: the A → B seam

    /// <summary>The seed surface as the loaded columns hold it — five reads per edited voxel.</summary>
    private StreamerSeedHeights? _seedHeights;

    /// <summary>Every hole in the analytic ground, filed under the sample its outside sits in.</summary>
    private PortalIndex? _portals;

    /// <summary>Feeds the index from the three arrival paths, the edit hook and the forget — dug chunks only.</summary>
    private PortalTracker? _portalTracker;

    /// <summary>Draws whole litres out of Tier A into Tier B at each tick boundary.</summary>
    private PortalDrain? _drain;

    private long _drawnAtWarm;

    // ------------------------------------------------------------ M8c-2: Tier C, seepage

    /// <summary>Every face through which a lake could seep into an excavation, filed under the chunk it arrives in.</summary>
    private SeepIndex? _seeps;

    /// <summary>Draws what the faces pass out of Tier A into Tier B at each tick boundary, lowest face first.</summary>
    private SeepDrain? _seepDrain;

    /// <summary>
    /// <c>--seepscale=&lt;x&gt;</c>: litres a second per square metre of face per metre of head.
    /// <see cref="SeepIndex.DefaultSeepageScale"/> by default, an estimate at the nuisance end —
    /// 1.76 L/s into a 10 × 10 × 3 m stone room under 5 m of head, ankle-deep in 3.9 h, dirt
    /// 17.5× that; M8d's play picks inside the range, as <c>rainfallScale</c> was picked. Zero
    /// builds no index and no drain: the off switch, and the control arm.
    /// </summary>
    private double _seepScale = SeepIndex.DefaultSeepageScale;

    /// <summary>
    /// <c>--seepmode=spring|sweat</c>: the whole wall sweating, each group handing litres round its
    /// faces (the default since 2026-09-11 — played and liked, and the cheaper arm for the
    /// automaton), or one running spring at the chunk's lowest face, the first arm played.
    /// </summary>
    private SeepMode _seepMode = SeepMode.Sweat;

    private long _seepedAtWarm;
    private long _seepPassesAtWarm;
    private double _seepMillisSum;
    private double _seepMillisSumAtWarm;
    private double _seepMillisMax;

    /// <summary>
    /// The awake count at boundaries where something seeped — the measurement M8c's pass named
    /// as the water-bodies trigger: a source with no end wakes the mine floor's sheet at every
    /// delivery, and whether it goes quiet between deliveries is what this reads.
    /// </summary>
    private int _seepAwakeSamples;
    private int _seepAwakeSamplesAtWarm;
    private long _seepAwakeSum;
    private long _seepAwakeSumAtWarm;
    private int _seepAwakeMin = int.MaxValue;
    private int _seepAwakeMax;

    // ------------------------------------------------------------ M8b-3a: the B → A seam

    /// <summary>Hands what came to rest on the analytic ground to the basin under it, at each tick boundary.</summary>
    private SurfaceHandoff? _handoff;

    /// <summary>M8d-5: where the lakes are, as the automaton sees them. Null without water.</summary>
    private LakeCeiling? _lakeCeiling;

    /// <summary>
    /// <c>--lakebody=0</c> builds no ceiling: the pre-M8d-5 arm minus the bank rule, so a hole in a
    /// bank treadmills in it. A measurement arm for the wrapper's cost on the visit, not a play arm.
    /// </summary>
    private bool _lakeBody = true;

    private long _handedAtWarm;

    // ------------------------------------------------------------ M8b-3b: the settle

    /// <summary>
    /// Levels the sheets the active set touches — what the automaton would diffuse to in O(n²)
    /// ticks, solved at a tick boundary after <see cref="_settleAfterTicks"/> quiet ones.
    /// </summary>
    private SheetSettle? _settle;

    /// <summary>
    /// <c>--settleafter=&lt;ticks&gt;</c>: ticks with no inflow before the sheet solve runs.
    /// <see cref="SheetSettle.DefaultSettleAfterTicks"/> by default; zero settles under inflow too.
    /// </summary>
    private int _settleAfterTicks = SheetSettle.DefaultSettleAfterTicks;

    /// <summary>
    /// <c>--settleshare=&lt;n&gt;</c>: a sheet is re-levelled when at least one cell in n of it is
    /// unsettled. <see cref="SheetSettle.DefaultSettleShare"/> by default; zero is no threshold,
    /// the behaviour before the share slice and the control arm.
    /// </summary>
    private int _settleShare = SheetSettle.DefaultSettleShare;

    /// <summary>
    /// <c>--spread=fixed|counted</c>: what a cell divides its sideways surplus by. Fixed by
    /// default — M8a's rule, and the one every recorded figure was taken under; <c>counted</c>
    /// is the play's arm for the rule the bench priced on 2026-09-07 (see <see cref="SpreadRule"/>).
    /// </summary>
    private SpreadRule _spread = SpreadRule.Fixed;

    private long _settlesAtWarm;

    /// <summary>The analysis in flight, or null. One at a time, by construction.</summary>
    /// <remarks>
    /// <b>A plain task rather than a job on the scheduler, and that is the design decision rather
    /// than laziness.</b> M7-PLAN is explicit that the watershed is not a chunk-shaped computation
    /// and that the instinct to make it one is the thing to resist: <c>JobPriority</c> ranks work
    /// by distance from the viewer in chunks, which says nothing about a window that is centred on
    /// the viewer already and analysed once every few hundred metres. It also has no chunk to be
    /// cancelled with. One at a time is enforced by this field being the only slot.
    /// </remarks>
    private System.Threading.Tasks.Task<WaterWindow>? _waterAnalysis;

    /// <summary>Milliseconds the last completed analysis took on its worker.</summary>
    private double _lastAnalysisMillis;

    /// <summary>How many samples on a side the water window is analysed at.</summary>
    /// <remarks>
    /// A 512 m window whatever the lattice: 128 samples at the 4 m cell, 256 at the 2 m cell
    /// (2026-09-10). Stated as a span rather than a pitch so that halving the cell does not
    /// silently halve how much of the world the rain lands on — a basin whose catchment crosses
    /// the window's edge drains off it. The cost is per sample: M7a measured 128 samples at 13.6 ms
    /// Release and 256 at 66 ms (the 1,024 m window, then), roughly quadratic in the pitch, on a
    /// worker once every ~128 m of travel — which is what <c>analysis ms</c> in the profile exists
    /// to confirm rather than assume.
    /// </remarks>
    private int _waterPitch = WindowVoxels / HeightField.DefaultCellVoxels;

    /// <summary>See <see cref="WaterSurface.SkipOverDug"/>; <c>--lakeoverdug=1</c> is the pre-M8d-5 arm, the plane drawn over a bite.</summary>
    private bool _lakeOverDug;

    /// <summary>See <see cref="WaterSurface.DilateShore"/>; <c>--shore=0</c> is the control arm.</summary>
    private bool _dilateShore = true;

    /// <summary>512 m, in voxels.</summary>
    private const int WindowVoxels = 512 * VoxelConstants.VoxelsPerMetre;

    /// <summary>Multiplies the rain reaching the ground. The knob M7b left for M7d to pick.</summary>
    /// <remarks>
    /// <para>
    /// <b>Twenty, and it is the one number in M7 that a person picked rather than a measurement.</b>
    /// The bench and the game between them bounded it: the median basin in a 512 m window fills in
    /// <b>32 minutes of this world's heaviest possible weather</b> unscaled, and a whole window
    /// reaches 10.2% of its leaf capacity in 40 minutes of <em>ordinary</em> weather — so a full
    /// basin unscaled is about six and a half hours, which nobody plays to watch. That arithmetic
    /// says the scale is somewhere in the tens; it cannot say where.
    /// </para>
    /// <para>
    /// <b>x40 shipped at M7d-1 and the play moved it, 2026-09-02:</b> <em>"a little too fast, um
    /// like you could half the fill speed, it would still work as well I reckon."</em> So a basin
    /// now fills in roughly twenty minutes rather than ten — still something you notice having
    /// happened rather than something you stand and watch, which is the feel DESIGN's core loop
    /// wants, and further from reading as a tap. <b>This is what M7d-2's play existed to decide</b>,
    /// and the halving is the only change it asked for.
    /// </para>
    /// <para>
    /// <b>It scales the depth applied to the world and nothing else</b>, so <see cref="Weather"/>
    /// goes on stating real intensities and the sky, the wet term and the rain particles all still
    /// read a real storm. That is what makes it an honest knob rather than a fudged rate, and
    /// multiplication is integral so every exactness claim in <see cref="BasinRainfall"/> survives
    /// it untouched.
    /// </para>
    /// <para>
    /// <b>Revisit trigger: a world whose basins are deeper than the spawn's.</b> The play that
    /// picked this was on gently terraced terrain holding shallow ponds, and a lake ten times the
    /// volume is ten times the wait at the same scale. <c>--rainscale=</c> stays the sweep.
    /// </para>
    /// </remarks>
    private int _rainfallScale = 20;

    /// <summary>Suppresses the water entirely. The control arm for the measurement.</summary>
    private bool _noWater;

    /// <summary>The rain, or null in a run that has no renderer to draw it with.</summary>
    private Rain? _rain;

    /// <summary>Suppresses the particles while leaving the rest of the weather alone.</summary>
    private bool _noRain;

    /// <summary>Ground heights around the player, so the rain knows where to stop.</summary>
    private readonly SkyHeightField _skyHeights = new();

    /// <summary>The chunk the sky-height field was last centred on.</summary>
    private ChunkPos _skyFieldCentre = new(int.MinValue, 0, int.MinValue);

    /// <summary>What <see cref="ChunkPipeline.SkyHeightChanges"/> read at the last rebuild.</summary>
    /// <remarks>
    /// <b>The second of the field's two reasons to be stale, and the one that is not free to
    /// notice.</b> The player crossing a chunk is a comparison this class already makes; somebody
    /// digging through a roof is not, and without it rain keeps falling into a shaft that has been
    /// filled in — or, worse and much more likely, refuses to fall into one that has just been
    /// opened. That second case is the feature, so it is the one that has to work on the frame
    /// after the last voxel comes out.
    /// </remarks>
    /// <remarks>
    /// <b>Zero rather than −1, and that is an instrument fix.</b> A sentinel here makes the very
    /// first refill compare unequal to <c>SkyHeightChanges</c>'s own zero and be attributed to
    /// digging — so every run reported "1 dug" before anything had been dug, which is precisely
    /// the false positive this counter exists to rule out.
    /// </remarks>
    private int _skyChangesAtRebuild;

    /// <summary>Field refills caused by digging through to the sky, and by walking.</summary>
    private int _skyFieldDigRebuilds;
    private int _skyFieldMoveRebuilds;

    /// <summary>Remeshes at the moment the measured window opened, for the zero-remesh claim.</summary>
    /// <remarks>
    /// <b>The assertion M7c exists to make is a difference, not a total</b>, because the fill is
    /// still relighting and remeshing while the profiler warms up. Taken at the warm-up boundary
    /// and subtracted at the end, so what is printed is what the storm cost rather than what the
    /// session did.
    /// </remarks>
    private int _remeshesAtWarm = -1;
    private int _relightsAtWarm = -1;

    /// <summary>
    /// The queue's sweep account at the first warm frame and at the last periodic report, so the
    /// profile's line is the window and the periodic line is the delta — the same reason the
    /// remesh counters above are differences and not totals.
    /// </summary>
    private SweepStatistics _sweepsAtWarm;
    private SweepStatistics _sweepsAtReport;

    /// <summary>Whether <c>--speed</c> was given, as opposed to left at its default.</summary>
    /// <remarks>
    /// <b>Needed so that <c>--speed=0</c> can mean nought rather than "unset".</b> A profile
    /// defaults to 40 m/s because a stationary streaming measurement measures a system that has
    /// stopped doing the thing being measured — but M5d's fill measurement is a
    /// <em>standing start</em> by definition, and without this the one arm it needs is the one
    /// arm that cannot be asked for.
    /// </remarks>
    private bool _speedGiven;

    /// <summary>When streaming started, for the standing-start fill measurements.</summary>
    private long _fillStartedTicks;

    /// <summary>Seconds from the first frame to the first chunk on screen, or −1.</summary>
    /// <remarks>
    /// <b>The number the light budget was swept against at M5c-1, and the one M5 moved.</b>
    /// Nothing meshes until its whole neighbourhood is lit, so light does not delay drawing a
    /// little — at too small a budget it delays it entirely, and the 5 s report showed a world
    /// fully generated with nothing drawn at all. Recorded beside the settle time because the
    /// two answer different questions: this one is what a player waits for.
    /// </remarks>
    private double _firstDrawSeconds = -1.0;

    /// <summary>Seconds until the light field first settled, or −1.</summary>
    private double _lightSettledSeconds = -1.0;

    /// <summary>When every chunk within <see cref="NearLitRadiusChunks"/> of the viewer had derived light.</summary>
    private double _nearLitSeconds = -1.0;

    private const int NearLitRadiusChunks = 2;

    /// <summary>Seconds until nothing at all was outstanding, or −1.</summary>
    private double _settledSeconds = -1.0;

    /// <summary>The world a run opens when nothing says otherwise.</summary>
    /// <remarks>
    /// <para>
    /// <b>A directory per world under <c>user://worlds/</c>, settled here rather than defaulted.</b>
    /// <c>user://</c> is Godot's per-user data directory and is the conventional answer; the extra
    /// path segment is the part worth arguing. A second world — M6d's scratch arm, a "new game", a
    /// backup — is then a new directory beside this one rather than a migration of a file sitting
    /// at the root, and moving a world file later strands the world that was in it. WAL leaves
    /// <c>-wal</c> and <c>-shm</c> sidecars next to the database, so a directory is what they
    /// wanted anyway.
    /// </para>
    /// </remarks>
    private const string DefaultWorldName = "default";

    /// <summary>How often the dirty set is written even though nothing has unloaded.</summary>
    /// <remarks>
    /// <b>Unload is the write path and this is the backstop.</b> A player digging in one spot
    /// never unloads the chunks they are digging, so without this the only writes are at quit and
    /// a crash costs the session. Thirty seconds against <see cref="ChunkPipeline.SaveAll"/>
    /// measured at 13 ms for the whole 1,521-chunk fill, where a real dirty set is tens of chunks.
    /// </remarks>
    private const double DefaultAutosaveSeconds = 30.0;

    /// <summary>The seed a brand new world is created with.</summary>
    /// <remarks>
    /// Only ever used once per world. After that the world's own header decides, because an
    /// unedited chunk is regenerated rather than stored — see <see cref="OpenWorld"/>.
    /// </remarks>
    private const ulong NewWorldSeed = 0x4D494E455243UL;

    public GameContent Content { get; } = new();

    public override void _Ready()
    {
        _camera = GetNodeOrNull<FlyCam>("FlyCam");
        _environment = GetNodeOrNull<WorldEnvironment>("WorldEnvironment")?.Environment;
        _skyMaterial = _environment?.Sky?.SkyMaterial as ProceduralSkyMaterial;

        if (_skyMaterial is not null)
        {
            _skyColours =
            [
                _skyMaterial.SkyTopColor,
                _skyMaterial.SkyHorizonColor,
                _skyMaterial.GroundHorizonColor,
                _skyMaterial.GroundBottomColor,
            ];
        }

        if (_environment is not null)
        {
            _baseFogColour = _environment.FogLightColor;
        }

        int margin = ApplyCommandLineOverrides();

        // After the overrides, not before: --skybrightness is a shader uniform set at build.
        _material = VoxelMaterial.Create(_skyBrightness);
        _material.SetShaderParameter("tint_in_view_space", _tintInViewSpace);

        // Not created in a headless run: there is no renderer, so every particle is a buffer
        // allocated to be stepped and never drawn. It also keeps the default headless profile
        // comparable with every one taken since M2d, which is the contract this project keeps.
        bool windowed = !DisplayServer.GetName().Equals("headless", StringComparison.Ordinal);

        if (windowed && !_noRain)
        {
            _rain = new Rain(this);
        }

        if (windowed && !_noWater)
        {
            _waterSurface = new WaterSurface(GetWorld3D())
            {
                DilateShore = _dilateShore,
                SkipOverDug = !_lakeOverDug,
                ColumnAt = (cx, cz) => _pipeline?.Streamer.ColumnAt(cx, cz),
            };
        }

        if (_camera is not null)
        {
            if (_spawnYawDegrees != 0f) _camera.TurnBy(Mathf.DegToRad(_spawnYawDegrees));
            if (_spawnPitchDegrees != 0f) _camera.AimBy(Mathf.DegToRad(_spawnPitchDegrees));
        }

        // Before the generator and before content, because an existing world decides both. Its
        // header carries the settings the unstored terrain has to keep regenerating from, and its
        // ID table has to reach the registry before a single material registers.
        if (!OpenWorld(out WorldGenSettings settings))
        {
            // Refused rather than played without saving. Carrying on with persistence quietly off
            // is an hour of digging lost to a line in the log, which is the failure this whole
            // milestone is about.
            GetTree().Quit(1);
            return;
        }

        _settings = settings;
        _generator = new WorldGenerator(settings, BuildStrata());
        SetUpWater();
        _materials = MaterialTable.From(Content.Materials);

        var viewers = new ViewerSet();
        _scheduler = new JobScheduler(viewers);
        _pipeline = new ChunkPipeline(
            _generator,
            _materials,
            _scheduler,
            viewers,
            onMeshReady: OnMeshReady,
            onMeshDiscarded: OnMeshDiscarded,
            onJobFaulted: (position, error) => GD.PushError($"Chunk job failed at {position}: {error}"),
            loadMargin: margin,
            resultBudget: _applyBudget,
            ambientOcclusion: _ambientOcclusion,
            lightBudget: _lightBudget,
            light: _lightInMergeKey,
            store: _store,
            // M8b: the fluid automaton cannot see edits, and this is how it is told. Wired before
            // _fluid exists, so the null guard is what the first frames run on. M8b-2c: the
            // portal tracker is told the same way, after the write and never on a no-op.
            onVoxelChanged: (voxel, _) =>
            {
                // The field first: the automaton is about to be told the world moved, and it must
                // read the moved world rather than the one it was baked from.
                _field?.Invalidate(voxel);
                _fluid?.Disturb(voxel);
                _portalTracker?.OnVoxelChanged(voxel);
            },
            chunkCacheBytes: _chunkCacheMiB * 1024 * 1024,
            distanceOrderedDerives: !_deriveFifo,
            // The forget, so the index never walks a chunk that is not here.
            onChunkForgotten: position => _portalTracker?.OnChunkForgotten(position),
            // The three arrival paths — generate, load, cache revival — through one callback, for
            // everything that is water. M8b-4a: the automaton wakes what the chunk's absence paused
            // and the faces beside it. M8b-4c moved both off onVoxelsReady onto this, which fires
            // only once the chunk's stored water is home: the chunk's own face wake then sees the
            // restored cells, and the portal drain never pours into a chunk the water cannot hold
            // yet — the played-world probe's first run did exactly that, and the drain threw.
            onFluidReady: (position, hasEdits) =>
            {
                _portalTracker?.OnVoxelsReady(position, hasEdits);
                _fluid?.ChunkArrived(position);

                // M8d-5: a panel lands after its column was covered, so the refresh's wake never
                // saw its cells; the band's own sweep does, and a cell asleep inside a lake is
                // visited, listed and handed at the next boundary.
                if (_lakeCeiling is not null && _fluid is not null)
                {
                    _lakeCeiling.WakeBand(position, _fluid.WakeCell);
                }
            });

        SetUpFluid(windowed);
        SetUpSupport();

        // Whatever the world remembered the player carrying. Before the first swing, so that a
        // reopened world can place from what it was already holding.
        _inventory.Reset(_restoredInventory.Select(entry => (entry.MaterialId, entry.Voxels)));

        // Before any chunk can be written, which is what replaces a world-spanning transaction:
        // numeric IDs are only allocated during content load, so the table on disk is already the
        // one every blob written after this points at. Rewritten on every open rather than only
        // for a new world, because installing a mod extends the table with entries the stored
        // header does not have — and a blob referring to an ID the header has never heard of is
        // exactly the desynchronisation the single-file layout exists to prevent.
        _pipeline.WriteWorldHeader(CurrentHeader());

        SetUpPlayer(_generator);

        _lastViewerChunk = ViewerChunk();

        // The configured radii are the ceiling. Adaptive, the session starts at the floor so the
        // fill is a growth (decision 4 of the plan): the near box draws in about a second and a
        // half instead of waiting for the whole box's derive, at the cost of the double derive at
        // every ring boundary, which the radius line prices.
        _radiusConfig = new ViewRadiusConfig(
            _minRadius, _minRadius, ViewDistanceHorizontal, ViewDistanceVertical,
            _pipeline.Streamer.LoadMargin, _shrinkSlabs, _growBelowLoad, _cadenceSeconds);
        if (_adaptiveRadius)
        {
            _radiusConfig.Validate();
            _radiusState = ViewRadiusState.AtFloor(_radiusConfig);
        }
        else
        {
            _radiusState = ViewRadiusState.AtCeiling(_radiusConfig);
        }

        _smallestRadius = _radiusState.Horizontal;
        _secondsAtRadius = new double[ViewDistanceHorizontal + 1];

        _viewer = _pipeline.Streamer.AddViewer(
            _lastViewerChunk, _radiusState.Horizontal, _radiusState.Vertical);

        SetUpMining();
        SetUpPlacement();
        SetUpHud();

        // Not MaterialId(), which falls back to air: placing air where a missing mod's lamp
        // should go would carve a hole out of whatever the player was pointing at.
        _lampMaterialId = Content.Materials.TryGetNumericId(Identifier.Parse(LampId), out int lamp)
            ? lamp
            : -1;

        // Last, so the fill measurement starts where streaming does rather than where content
        // loading does. Loading the mods is a fixed cost that is not what M5d is asking about.
        _fillStartedTicks = Stopwatch.GetTimestamp();

        GD.Print(
            $"M2c: streaming r{ViewDistanceHorizontal}h/{ViewDistanceVertical}v " +
            $"+{_pipeline.Streamer.LoadMargin} margin on {_scheduler.ThreadCount} worker(s), " +
            $"vertex AO {(_ambientOcclusion ? "on" : "off")}, " +
            $"light in merge key {(_lightInMergeKey ? "on" : "off")}, " +
            // Printed because a flag that silently did not arrive looks identical to one that
            // did until first draw — the 2026-09-03 "ground is gone" session could not tell.
            $"light budget {(_lightBudget ?? ChunkPipeline.DefaultLightBudget).TotalMilliseconds:F0} ms");
    }

    /// <summary>
    /// Builds the player body and puts the camera where its eyes will be.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A profile run starts in fly mode, and that is the whole of M4c's contract with
    /// M2d and M3d.</b> <c>--profile</c> assigns the camera's position directly and
    /// <see cref="AdvanceFlythrough"/> snaps it to the surface every frame; a body that also
    /// moved the camera would fight both. In fly mode the body is inert, so every number M2d
    /// and M3d produced stays comparable with M4d's.
    /// </para>
    /// <para>
    /// The camera is placed at the spawn <em>eye</em> rather than left where the scene put it,
    /// so walk mode opens with no snap on the frame the world finishes loading. The spawn
    /// column comes from <c>SurfaceHeightAt</c>, which is a pure function of the generator and
    /// needs no chunk to have loaded — which is the only reason there is a sensible place to
    /// point the camera before the streamer has produced anything.
    /// </para>
    /// </remarks>
    private void SetUpPlayer(WorldGenerator generator)
    {
        if (_camera is null)
        {
            return;
        }

        // Flying is a measurement instrument, so a profile run is not allowed to walk — except
        // the fall profile, where walking (falling, specifically) is the thing being measured.
        PlayerMode mode = _fallProfile || (_profiler is null && _flySpeed <= 0f)
            ? PlayerMode.Walk
            : PlayerMode.Fly;

        Vector3 scenePosition = _camera.GlobalPosition;
        VoxelPos column = VoxelPos.FromMetres(scenePosition.X, scenePosition.Y, scenePosition.Z);
        System.Numerics.Vector3 feet = PlayerSpawn.FeetOnSurface(
            generator.SurfaceHeightAt(column.X, column.Z), scenePosition.X, scenePosition.Z);

        // A world you have been in puts you back where you were — but never during a profile,
        // where the spawn is a measurement input and a run that started somewhere else is not
        // comparable with the one before it.
        bool restored = _profiler is null && _savedPlayer is not null;
        if (restored && _savedPlayer is PlayerSave saved)
        {
            feet = saved.FeetMetres;
            mode = saved.Flying ? PlayerMode.Fly : PlayerMode.Walk;
        }

        if (_fallProfile)
        {
            // Spawned high rather than teleported up once the window opens. A teleport of
            // several hundred metres moves the viewer dozens of chunks and reloads the whole
            // box, and that spike would land inside the measured window and be reported as the
            // cost of falling. Spawning high puts the reload in the warmup instead, where the
            // initial fill already lives.
            feet.Y += _fallHeightMetres;
        }

        PlayerMotionConfig config = _terminalSpeed is float terminal
            ? PlayerMotionConfig.Default with { TerminalSpeedMetresPerSecond = terminal }
            : PlayerMotionConfig.Default;

        _player = new PlayerController(config, feet, mode);

        if (mode == PlayerMode.Walk)
        {
            _camera.FlightEnabled = false;
            _camera.GlobalPosition = ToGodot(_player.EyePosition);
        }
        else if (restored)
        {
            // The camera is where the player is in fly mode, so a restored flying player has to
            // put it back rather than being dropped above the terrain like a new one.
            _camera.GlobalPosition = ToGodot(_player.EyePosition);
        }
        else
        {
            PlaceCameraAboveTerrain(generator);
        }

        GD.Print(
            $"M4c: {mode.ToString().ToLowerInvariant()} mode, body {config.WidthMetres:F2} x " +
            $"{config.HeightMetres:F2} m, eye {config.EyeHeightMetres:F2} m, " +
            $"spawn feet {feet.Y:F2} m{(restored ? ", restored from the world file" : "")}");
    }

    /// <summary>
    /// Reads <c>--profile</c>, <c>--radius</c>, <c>--vradius</c>, <c>--margin</c>,
    /// <c>--speed</c>, <c>--warmup</c>, <c>--measure</c>, <c>--cavestep</c>,
    /// <c>--applybudget</c>, <c>--lightbudget</c> and <c>--ao</c> from the command line, after a bare
    /// <c>--</c>.
    /// </summary>
    /// <remarks>
    /// Configurable from outside so a sweep is one shell loop rather than five edit-rebuild
    /// cycles — which is what makes comparing view distances, and margin 2 against 1,
    /// cheap enough to actually do.
    /// </remarks>
    private int ApplyCommandLineOverrides()
    {
        int margin = ChunkStreamer.DefaultLoadMargin;
        double warmup = 3.0;
        double measure = 15.0;
        bool profile = false;

        // A game flag on the engine's side of the bare "--" is ignored by Godot without a word,
        // and a run at the default budget looks identical to one at the requested budget until
        // first draw — which is how "start.cmd --lightbudget=16" cost an evening on 2026-09-03.
        // Godot's own options never take the name=value form, so that shape is ours.
        foreach (string argument in OS.GetCmdlineArgs())
        {
            if (argument.StartsWith("--", StringComparison.Ordinal) && argument.Contains('='))
            {
                GD.PushWarning(
                    $"Overburden: \"{argument}\" came before the bare \"--\" and the engine ignored it; "
                    + "game flags go after it: ... -- " + argument);
            }
        }

        foreach (string argument in OS.GetCmdlineUserArgs())
        {
            string[] parts = argument.Split('=', 2);
            string name = parts[0].TrimStart('-');
            string value = parts.Length > 1 ? parts[1] : string.Empty;

            switch (name)
            {
                case "radius" when int.TryParse(value, out int radius):
                    ViewDistanceHorizontal = radius;
                    break;

                case "vradius" when int.TryParse(value, out int vertical):
                    ViewDistanceVertical = vertical;
                    break;

                case "margin" when int.TryParse(value, out int parsed):
                    margin = parsed;
                    break;

                case "speed" when float.TryParse(value, out float speed):
                    _flySpeed = speed;
                    _speedGiven = true;
                    break;

                case "warmup" when double.TryParse(value, out double seconds):
                    warmup = seconds;
                    break;

                case "measure" when double.TryParse(value, out double seconds):
                    measure = seconds;
                    break;

                // A test seam, not a setting. Step 1 is per-voxel cave evaluation — the way
                // generation worked before M2d-2 — so a sweep can measure what the lattice
                // bought in the real streaming loop rather than only on the bench.
                case "cavestep" when int.TryParse(value, out int step):
                    _caveLatticeStep = step;
                    break;

                // Also a test seam. A budget large enough to never bind reproduces the
                // unbudgeted behaviour, which is the A/B this was built to settle.
                case "applybudget" when double.TryParse(value, out double budgetMillis):
                    _applyBudget = TimeSpan.FromMilliseconds(budgetMillis);
                    break;

                // The fourth seam of the same shape, and the one M5c-1 needed the moment it
                // existed: light derives and propagates on the sim thread, and how much of a
                // frame it may take is the whole of how long a standing start waits before it
                // can draw anything. Sweepable rather than rebuilt, for the same reason the
                // three above are.
                case "lightbudget" when double.TryParse(value, out double lightMillis):
                    _lightBudget = TimeSpan.FromMilliseconds(lightMillis);
                    break;

                // The third test seam of the same shape, and the one M5a exists to read.
                // --ao=0 restores the constant-brightness merge key every mesh used before
                // M5a, so the geometry line of one run against another is exactly what
                // per-vertex AO costs in quads. That is half of M5's fragment-shader
                // decision, and it is available a milestone before the decision is due.
                case "ao" when int.TryParse(value, out int ao):
                    _ambientOcclusion = ao != 0;
                    break;

                // The other half of the same A/B, and the two are deliberately independent.
                // M5-PLAN asks for AO off / AO on / AO+light on; having both switches gives the
                // fourth arm free, which is light with no occlusion — the only way to price
                // light in the merge key on its own rather than on top of AO.
                case "light" when int.TryParse(value, out int light):
                    _lightInMergeKey = light != 0;
                    break;

                // Scales the sky channel in the shader and nothing else. 0 leaves only block
                // light, which is what says the two channels really are independent all the way
                // to the screen rather than only in the mesher.
                case "skybrightness" when float.TryParse(value, out float brightness):
                    _skyBrightness = brightness;
                    break;

                // Where in this world's forecast the run starts. The weather is a pure function
                // of (seed, time), so this is how a screenshot of a storm is asked for by name
                // rather than waited for — and how the same storm is photographed twice.
                //
                // It sets the clock a run *starts* at, which for a run with no world file is the
                // whole of it. An *existing* world overrides it — named or not, so the default
                // world does too — because what that world remembers is the thing under test.
                // So provoking weather in a world you have already played needs --nosave or a
                // --world= name that does not exist yet; otherwise this silently does nothing.
                case "clock" when double.TryParse(value, out double seconds):
                    _clock = new WorldClock((long)(seconds * 1000.0));
                    break;

                // Makes a fifteen-second window cover a whole weather episode. See _clockScale
                // for why this is not the same knob as M7d's rainfallScale.
                case "clockscale" when double.TryParse(value, out double scale):
                    _clockScale = scale;
                    break;

                // Forces the sheen and the overcast without making it rain — see _forcedWeather.
                case "weather" when float.TryParse(value, out float forced):
                    _forcedWeather = Math.Clamp(forced, 0f, 1f);
                    break;

                // Turns the camera at spawn, which screenshot.gd has never been able to do.
                // A view-dependent artefact is invisible in one picture by definition — what
                // shows it is the same wall photographed from two yaws — so this is what makes
                // the face-tint A/B a comparison rather than an assertion.
                case "yaw" when float.TryParse(value, out float degrees):
                    _spawnYawDegrees = degrees;
                    break;

                // The vertical half of --yaw. Negative looks down, which is what a shot of rain
                // going into a shaft needs.
                case "pitch" when float.TryParse(value, out float pitchDegrees):
                    _spawnPitchDegrees = pitchDegrees;
                    break;

                // Restores the pre-M7c face tint, which read the view-space NORMAL. See the
                // shader's world_normal for what that did and why it stopped being cosmetic.
                case "viewnormal" when int.TryParse(value, out int viewNormal):
                    _tintInViewSpace = viewNormal != 0;
                    break;

                // The knob M7b measured the need for and deliberately did not pick: at real
                // rainfall the median basin fills in 35 minutes of the heaviest weather this world
                // has, which nobody can watch. It multiplies the depth applied to the world and
                // leaves Weather stating real intensities, so the sky and the wet term still read
                // a real storm - see BasinRainfall for why multiplication keeps every exactness
                // claim intact.
                case "rainscale" when int.TryParse(value, out int scale):
                    _rainfallScale = Math.Max(1, scale);
                    break;

                // Samples on a side of the analysed window. A sweep seam rather than a setting:
                // the analysis is roughly quadratic in the span, and this is what prices it.
                case "waterwindow" when int.TryParse(value, out int pitch):
                    _waterPitch = Math.Max(2, pitch);
                    break;

                // The shore's ring dilation (2026-09-10): 0 is the arm where a cell draws its
                // plane only if its own sample is under the level - the stair-stepped shoreline.
                case "shore" when int.TryParse(value, out int shore):
                    _dilateShore = shore != 0;
                    break;
                case "lakeoverdug" when int.TryParse(value, out int overDug):
                    _lakeOverDug = overDug != 0;
                    break;
                case "lakebody" when int.TryParse(value, out int lakeBody):
                    _lakeBody = lakeBody != 0;
                    break;

                // The water's control arm, the same shape as --norain. The analysis is the only
                // part of M7d that costs a worker anything, and one run with it and one without
                // is what prices it against the streaming it shares a machine with.
                case "nowater":
                    _noWater = true;
                    break;

                // The relay from the command line: litres poured at the player once the measured
                // window opens. M8-PLAN's "--injectlitres", and the reason the fluid line prints
                // injected beside held — a debug source creates litres from nothing, which is
                // exactly what the milestone's oracle forbids, so it is a term rather than a secret.
                case "injectlitres" when long.TryParse(value, out long inject) && inject > 0:
                    _injectLitres = inject;
                    break;

                // M8b-2c's two seams. --fillbasin is the Tier A relay: litres into the basin
                // under the player when the window opens, reported as injected on the water line.
                // --breach bites a square shaft that wide under the player at the same moment, so
                // a headless run has a lake, a hole under it, and both identities to read.
                case "fillbasin" when long.TryParse(value, out long fill) && fill > 0:
                    _fillBasinLitres = fill;
                    break;
                case "breach" when int.TryParse(value, out int breach) && breach > 0:
                    _breachVoxels = breach;
                    break;

                // M9b: the support field's switches. --support=0 is the control arm — no region,
                // no derives, no tint; --supporttint=0 keeps the derives and leaves the mesh alone,
                // which prices the tint's remeshes apart from the derives and is the look's control.
                case "support" when int.TryParse(value, out int support):
                    _supportOn = support != 0;
                    break;
                case "supporttint" when int.TryParse(value, out int supportTint):
                    _supportTint = supportTint != 0;
                    break;

                // M9c-1: the fall's two flags. --collapse=0 arms and never fires, the arm that lets
                // a person walk under a marked roof to judge the marking; --collapseafter=min,max
                // is decision 8's window in world seconds, 2 and 60 the shipped estimates, and
                // the headless oracle passes 1,2 so the plug falls inside the window.
                case "collapse" when int.TryParse(value, out int collapse):
                    _collapseOn = collapse != 0;
                    break;
                case "collapseafter" when TryParseWindow(value, out CollapseWindow window):
                    _collapseWindow = window;
                    break;

                // M9b's provocation: an n × n square of chunks bitten under the player when the
                // window opens, through the edit path, --breach's shape one scale up. Depth in
                // chunks below the player's; --inbite drops the camera into it for the screenshot,
                // because a plug is an underside and no view from the surface shows it.
                case "bitechunks" when int.TryParse(value, out int biteChunks) && biteChunks > 0:
                    _biteChunks = biteChunks;
                    break;
                case "bitedepth" when int.TryParse(value, out int biteDepth) && biteDepth > 0:
                    _biteDepth = biteDepth;
                    break;
                case "inbite":
                    _inBite = true;
                    break;

                // M8c-2: the seepage rate's sweep, rainfallScale's shape. Zero is off — no index,
                // no drain, no scans — which is the control arm and the back-out.
                case "seepscale" when double.TryParse(value, out double seepScale):
                    _seepScale = Math.Max(0.0, seepScale);
                    break;
                case "seepmode":
                    _seepMode = value.Equals("spring", StringComparison.OrdinalIgnoreCase) ? SeepMode.Spring : SeepMode.Sweat;
                    break;

                // M8d-1: the Tier B mesh — merged tops, sides, bottoms — or the pre-M8d-1 debug
                // view, one quad per surface cell, as the control arm for a look A/B; or the mesh
                // with one kind of face left out, so a screenshot can say which kind an artefact is.
                case "fluidsurface":
                    _fluidSurfaceMode = value.ToLowerInvariant();
                    break;

                // The fifth budget seam of the same shape: how much of a frame Tier B's tick may
                // take before it yields and resumes next frame. Priced at M8b-1 — a roof falling
                // in under a lake is a 73 ms tick in Release and ~0.7 s in Debug, and no tick
                // rate can bound a tick that arrives whole — so the tick is sliced, and this is
                // the sweep. Zero is not off: one granule a frame, so the water still moves.
                case "fluidbudget" when double.TryParse(value, out double fluidMillis):
                    _fluidBudget = TimeSpan.FromMilliseconds(Math.Max(0.0, fluidMillis));
                    break;

                // The water thread, slice 1: near water every tick, far water deferred past the
                // boundary under a quota of max(--fluidfar, the near set). Zero rings is no order,
                // which is the control arm and what every figure recorded before it was taken in.
                case "fluidnear" when int.TryParse(value, out int nearRings):
                    _fluidNearRings = Math.Max(0, nearRings);
                    break;

                case "fluidfar" when int.TryParse(value, out int farFloor):
                    _fluidFarFloor = Math.Max(1, farFloor);
                    break;

                // The water thread, slice 2: the tick reads a baked field instead of the
                // streamer's chunks. Zero is the arm every figure before it was taken in.
                case "fluidfield" when int.TryParse(value, out int fieldOn):
                    _fluidFieldOn = fieldOn != 0;
                    break;

                // The water thread, slice 3b: the tick as a job on a worker. Zero is the
                // sim-thread arm every figure before it was taken in.
                case "fluidjob" when int.TryParse(value, out int jobOn):
                    _fluidJobOn = jobOn != 0;
                    break;

                case "fieldbakes" when int.TryParse(value, out int bakes):
                    _fieldBakesPerBoundary = Math.Max(1, bakes);
                    break;

                // M8b-3b: how many ticks with nothing arriving before the sheet solve levels what
                // is still awake. Two — one second — is an estimate, and this is the sweep. Zero
                // settles under inflow as well, which is the arm that makes a stream down a tunnel
                // rise as a flat sheet instead of flowing; it exists so both can be seen.
                case "settleafter" when int.TryParse(value, out int settleAfter):
                    _settleAfterTicks = Math.Max(0, settleAfter);
                    break;

                // The share slice: a sheet with a move in it is re-levelled only when at least one
                // cell in n of it is unsettled, or a step stands in it. Zero is no threshold — the
                // pre-slice behaviour, which re-levelled a 162,572-cell lake twice a second for
                // its creeping edge — and the control arm.
                case "settleshare" when int.TryParse(value, out int settleShare):
                    _settleShare = Math.Max(0, settleShare);
                    break;

                // The spread rule: fixed (divide by five) or counted (one part for the cell and
                // one for each lower side). The bench's --spread is the same choice; this is the
                // arm a person judges by stabbing R.
                case "spread" when value is "fixed" or "counted":
                    _spread = value == "counted" ? SpreadRule.Counted : SpreadRule.Fixed;
                    break;

                // M8d-3, the fall rule: how many cells a visit may fall through vacant air.
                // 16 is the default (8 m/s from the first tick); 1 is the pre-M8d-3 arm, a cell a
                // tick, and the control every figure before it was taken in.
                case "fallcells" when int.TryParse(value, out int fallCells):
                    _fallCells = Math.Clamp(fallCells, 1, FluidSimulation.MaxFallCells);
                    break;

                // The rain's own A/B arm: everything else about the weather, with no particles.
                // What it is for is the storm-versus-control measurement — the particles are the
                // only part of M7c that costs GPU time, so a windowed run with and without them
                // is what prices them, and a screenshot pair is what says they are what changed.
                case "norain":
                    _noRain = true;
                    break;

                // Places one lamp where the crosshair points, so a screenshot can show block
                // light without anybody pressing L. See PlaceScriptedLamp.
                case "debuglamp":
                    _debugLamp = true;
                    break;

                case "profile":
                    profile = true;
                    break;

                // Holds the button down for the whole measured window. M3-PLAN asks for the
                // cost of mining *continuously*, not of one swing, and a person holding a
                // mouse is not a repeatable measurement.
                case "mine":
                    _mineWhileProfiling = true;
                    break;

                // The entangled miner from the command line, because F12 cannot be pressed
                // headlessly and a debug bind nobody has run is a debug bind that does not work.
                // The sixth seam of this shape. It deliberately does NOT imply --mine: on its
                // own it only proves the cast reaches, which is the half that has no other
                // witness.
                case "entangle":
                    _entangleWhileProfiling = true;
                    break;

                // The arm the chunk bite was priced against: entangled, but taking the tier's
                // sphere the way Q did before. Named for the thing it restores rather than for
                // the thing it disables, the same as --cavestep=1 and --viewnormal=1, because a
                // flag that reads as "off" invites being thought of as the default.
                case "entanglesphere":
                    _entangledSphereWhileProfiling = true;
                    break;

                // The chunk bite's rate, as a seam rather than a constant, for the reason
                // --rainscale is one: the measurement can only bound a feel number, and the
                // person judging it has to be able to move it without a rebuild. Unlike most
                // flags here it is NOT profile-only — Q is a play bind, so the value it sweeps
                // has to reach a played session or the sweep is useless.
                case "chunkcooldown" when float.TryParse(value, out float chunkCooldown)
                                          && chunkCooldown > 0.0f:
                    _chunkCooldownSeconds = chunkCooldown;
                    break;

                // The fall profile. M2d measured a viewer flying horizontally at a constant
                // height, which is the one motion the flythrough was built to produce and is
                // nothing like a player falling down a shaft: a fall is vertical, it is fast,
                // and the vertical view radius is the small one. It is the first motion that
                // can genuinely arrive somewhere before the world does.
                case "fall":
                    _fallProfile = true;
                    profile = true;
                    break;

                case "fallheight" when float.TryParse(value, out float height):
                    _fallHeightMetres = height;
                    break;

                // Terminal velocity from outside, because it is the constant this whole profile
                // exists to pick and sweeping it by rebuilding is how a sweep does not get done.
                // Same argument as --radius. M4-PLAN: "terminal velocity is a streaming
                // parameter in a physics costume".
                case "terminal" when float.TryParse(value, out float terminal):
                    _terminalSpeed = terminal;
                    break;

                // Which world under user://worlds/ this run opens. Naming one is also what puts
                // persistence back on for a profile run, which is how M6d measures the load path
                // without a measurement digging up the world somebody plays.
                case "world":
                    if (IsUsableWorldName(value))
                    {
                        _worldName = value;
                        _worldNamed = true;
                    }
                    else
                    {
                        // Said out loud rather than fallen back from quietly: opening the default
                        // world when a scratch one was asked for is how a measurement run digs up
                        // the world somebody plays.
                        GD.PushError($"--world='{value}' is not a usable directory name; ignored.");
                    }

                    break;

                // No world file at all: the pipeline takes a null store, every chunk comes from
                // the seed, and nothing is written. The world M2d and M5d measured.
                case "nosave":
                    _persist = false;
                    break;

                // The retained-chunk cache's cap. Zero is the committed pre-cache behaviour and
                // is the control arm; the default headless baseline has to come out identical in
                // both, because nothing leaves the box at a standing start. Not profile-only,
                // for the reason --chunkcooldown is not: memory is something a play reports.
                case "chunkcache" when long.TryParse(value, out long cacheMiB) && cacheMiB >= 0:
                    _chunkCacheMiB = cacheMiB;
                    break;

                // Reverses the flythrough every N seconds. A turnaround is the motion the cache
                // exists for — the box re-enters chunks it just left — and the straight-line
                // flythrough M2d built cannot produce it. Zero, the default, never turns.
                case "turnaround" when double.TryParse(value, out double turnaround) && turnaround > 0:
                    _turnaroundSeconds = turnaround;
                    break;

                // Restores the FIFO derive queue, the arm "the ground is gone" was seen in, so the
                // distance order can be measured against it rather than remembered. Named for
                // what it restores, like --cavestep=1 and --entanglesphere.
                case "derivefifo":
                    _deriveFifo = true;
                    break;

                // The adaptive view radius and its three estimates. --adaptive alone is on;
                // --adaptive=0 is the fixed radius, which is also the default, so a profile that
                // names neither is the arm every recorded figure was taken in.
                case "adaptive":
                    _adaptiveRadius = value.Length == 0 || value != "0";
                    break;

                case "minradius" when int.TryParse(value, out int minRadius) && minRadius >= 1:
                    _minRadius = minRadius;
                    break;

                case "shrinkslabs" when int.TryParse(value, out int slabs) && slabs >= 0:
                    _shrinkSlabs = slabs;
                    break;

                // The cadence: least seconds between radius changes, the grace after a grow, and
                // the load average's time constant. Was --growafter, a dwell, until the 7 m/s
                // corridor showed a walker never dwells.
                case "cadence" when double.TryParse(value, out double cadence) && cadence >= 0.0:
                    _cadenceSeconds = cadence;
                    break;

                // The light step's averaged share of its budget below which one more ring fits.
                case "growbelow" when double.TryParse(value, out double growBelow) && growBelow >= 0.0:
                    _growBelowLoad = growBelow;
                    break;

                // Moves for N seconds and then stands still: the arm the grow-back is judged in.
                case "sprintfor" when double.TryParse(value, out double sprintFor) && sprintFor > 0.0:
                    _sprintForSeconds = sprintFor;
                    break;

                case "autosave" when double.TryParse(value, out double autosave):
                    _autosaveSeconds = autosave;
                    break;

                // Holds the other button down for the whole measured window, the counterpart to
                // --mine and for the same reason: a person holding a mouse is not a repeatable
                // measurement.
                case "place":
                    _placeWhileProfiling = true;
                    break;

                // The precise arm of --place, and the only way to exercise the modifier without a
                // keyboard. **The wire is what this is for**: every layer of precision placement
                // has a test and the join between the held key and Place.Voxel has none, which is
                // exactly the shape of hole M5c-2 found by replacing a gather with null and
                // watching 556 tests stay green.
                case "preciseplace":
                    _placeWhileProfiling = true;
                    _precisePlaceWhileProfiling = true;
                    break;

                // Seeds the inventory, because --place has nothing to place otherwise and mining
                // first would measure the carve as well as the brush.
                case "stock" when long.TryParse(value, out long stock):
                    _startingStock = stock;
                    break;

                case "brush" when Enum.TryParse(value, ignoreCase: true, out BrushShape brush):
                    _startingBrush = brush;
                    break;

                default:
                    // A flag nobody reads is a play of the wrong arm that looks exactly like a play
                    // of the right one: --fluidjob=1 was passed to a build that did not have it
                    // (2026-09-10) and the log said nothing. Printed rather than warned, so it is
                    // in godot.log beside the setup lines it would otherwise have changed.
                    GD.Print($"Overburden: unknown flag \"{argument}\" — ignored");
                    break;
            }
        }

        // A run whose purpose is a measurement or a screenshot prop does not open the played
        // world. Two reasons and they are separate: --mine would carve it up, and a run that
        // *loads* what an earlier one saved is not comparable with M2d's figures — the whole
        // streaming shape changes when 1,521 generates become 1,521 serial reads. --world= is the
        // opt back in, deliberately explicit.
        if ((profile || _debugLamp) && !_worldNamed)
        {
            _persist = false;
        }

        if (profile)
        {
            _profiler = new StreamingProfiler { WarmupSeconds = warmup, MeasureSeconds = measure };

            // Flying is the test for the streaming profile. Standing still measures a system
            // that has stopped doing the thing being measured. The *fall* profile is the
            // exception: there the body is the motion, so the scripted flythrough must stay off
            // or it would assign the camera out from under the player every frame.
            if (_flySpeed <= 0f && !_fallProfile && !_speedGiven)
            {
                _flySpeed = 40f;
            }
        }

        return margin;
    }

    /// <summary><c>--collapseafter=min,max</c>: two finite, non-negative world seconds, comma-separated.</summary>
    private static bool TryParseWindow(string value, out CollapseWindow window)
    {
        window = CollapseWindow.Default;
        string[] parts = value.Split(',');
        if (parts.Length != 2
            || !double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double min)
            || !double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double max)
            || !double.IsFinite(min) || !double.IsFinite(max) || min < 0 || max < 0)
        {
            return false;
        }

        window = new CollapseWindow { MinSeconds = min, MaxSeconds = max };
        return true;
    }

    /// <summary>
    /// One fixed-step solve of the player body, and the camera moved onto its eyes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Here rather than in <see cref="_Process"/> because the step length has to be
    /// constant.</b> M4b's <c>Step</c> takes <c>dt</c> as an argument precisely so Godot owns
    /// the rate, and Godot's physics accumulator is the thing that already does fixed-step
    /// work. A jump apex that changed with the frame rate would get diagnosed as a physics bug.
    /// </para>
    /// <para>
    /// <b>The order M4-PLAN specifies is player → viewer → pipeline</b>, and it holds because
    /// Godot runs every physics tick of a frame before that frame's <c>_Process</c>. So the
    /// body is solved against the freshest world the streamer has managed, and the viewer that
    /// decides what loads next is placed from where the body actually ended up. Running the
    /// pipeline first would solve the player against a world one frame staler than necessary,
    /// on the frame where being current is the entire point.
    /// </para>
    /// </remarks>
    public override void _PhysicsProcess(double delta)
    {
        if (_player is null || _pipeline is null || _materials is null || _camera is null)
        {
            return;
        }

        long before = Stopwatch.GetTimestamp();
        bool stepped = _player.TryStep(_pipeline.Streamer, _materials, ReadIntent(), (float)delta);
        double stepMillis = Milliseconds(before, Stopwatch.GetTimestamp());

        if (!stepped)
        {
            return;
        }

        RecordPlayerStep(stepMillis);
        AdvanceFallProfile();

        if (!_announcedSpawn)
        {
            _announcedSpawn = true;
            GD.Print(
                $"M4c: spawned at {_player.FeetCentre} after {_player.StepsHeldForWorld} " +
                "step(s) held for the world");
        }

        _camera.GlobalPosition = ToGodot(_player.EyePosition);
    }

    /// <summary>
    /// Turns the keyboard and the camera's yaw into a <see cref="MoveIntent"/>.
    /// </summary>
    /// <remarks>
    /// The only place in the project that reads a key and means "walk". Direction comes from
    /// the camera flattened to the ground plane — see <see cref="FlyCam.WalkForward"/> for why
    /// it is normalised after flattening and not before — and the speed comes from the config,
    /// so M4d can retune walking without touching a binding.
    /// </remarks>
    private MoveIntent ReadIntent()
    {
        if (_camera is null || !_camera.MouseCaptured)
        {
            // Esc released the mouse. Stopping the walk as well as the look, for the reason
            // FlyCam.MouseCaptured gives about mining: clicking on another window should not
            // also march the player off a cliff.
            return MoveIntent.Still;
        }

        Vector3 direction = Vector3.Zero;
        if (Input.IsKeyPressed(Key.W)) direction += _camera.WalkForward;
        if (Input.IsKeyPressed(Key.S)) direction -= _camera.WalkForward;
        if (Input.IsKeyPressed(Key.D)) direction += _camera.WalkRight;
        if (Input.IsKeyPressed(Key.A)) direction -= _camera.WalkRight;

        // Ctrl sprints and Shift sneaks, matching Minecraft rather than the arrangement this
        // project carried from M2 to M6d-1, which had the two swapped. **Sneak is a game rule and
        // not only a speed**: PlayerMotion refuses a step that would walk a grounded body off an
        // edge, which in a game about standing on ledges above shafts you dug yourself is the
        // difference between building at the lip and falling down your own mine.
        return new MoveIntent(
            new System.Numerics.Vector3(direction.X, direction.Y, direction.Z),
            Jump: Input.IsKeyPressed(Key.Space),
            Sprint: Input.IsKeyPressed(Key.Ctrl),
            Sneak: Input.IsKeyPressed(Key.Shift));
    }

    /// <summary>The walk/fly toggle. Everything else about input belongs to its own node.</summary>
    /// <remarks>
    /// On a key-down edge rather than polled, because a toggle read every frame flips sixty
    /// times a second. <c>FlyCam</c> sees the event first and does not consume F.
    /// </remarks>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }

        if (key.Keycode == Key.Q)
        {
            ToggleEntangledMiner();
            return;
        }

        if (key.Keycode == Key.B)
        {
            CycleBrush();
            return;
        }

        if (key.Keycode == Key.V)
        {
            CycleMaterial();
            return;
        }

        if (key.Keycode == Key.T)
        {
            CycleTier();
            return;
        }

        if (key.Keycode >= Key.Key1 && key.Keycode <= Key.Key9)
        {
            SelectMaterialSlot((int)(key.Keycode - Key.Key1));
            return;
        }

        if (_player is null || _camera is null || key.Keycode != Key.F)
        {
            return;
        }

        if (_profiler is not null || _flySpeed > 0f)
        {
            // A scripted flythrough owns the camera. Handing it to a body mid-run would
            // invalidate the measurement rather than produce an interesting one.
            GD.Print("M4c: walk mode is unavailable during a profile run.");
            return;
        }

        var eye = new System.Numerics.Vector3(
            _camera.GlobalPosition.X, _camera.GlobalPosition.Y, _camera.GlobalPosition.Z);

        _player.ToggleMode(eye);
        _camera.FlightEnabled = _player.Mode == PlayerMode.Fly;
        _announcedSpawn = _player.Mode == PlayerMode.Fly;

        GD.Print($"M4c: {_player.Mode.ToString().ToLowerInvariant()} mode");
    }

    /// <summary>
    /// Q turns the entangled miner on and off.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A debug bind for excavating a test volume, not a feature.</b> M5d-2 is the played half
    /// of lighting and its questions are all about what a <em>large</em> excavation looks like —
    /// whether a mine reads as a mine, whether a 1 m light cell is too coarse for a tunnel
    /// narrower than one cell, whether rock beside a lit tunnel glows. Digging that by hand at
    /// 5 m of reach is the reason those questions have not been answered yet.
    /// </para>
    /// <para>
    /// On a key-down edge rather than polled, the same as F and L, because a toggle read every
    /// frame flips sixty times a second. <b>Q rather than a function key</b>: it is free — WASD
    /// leaves the whole of QE alone — it is mnemonic, and it is under the left hand already,
    /// which matters for something toggled mid-dig rather than once at startup. F12 is also the
    /// Steam overlay's screenshot key, which is a bad neighbour for a bind used while playing.
    /// </para>
    /// <para>
    /// <b>Refused during a profile run</b>, for exactly the reason walk mode is: <c>--mine</c>
    /// is pinned to the un-entangled sphere so that M3d's baseline stays comparable, and a run
    /// that quietly switched to arbitrary-range swings would report a different measurement
    /// under the same label. See <see cref="MiningController.EntangledSwings"/>.
    /// </para>
    /// </remarks>
    /// <summary>B cycles the brush, V cycles the material. Both print, because there is no HUD.</summary>
    /// <remarks>
    /// <b>B and V rather than more number keys</b>, which are the tool tiers and stay the tool
    /// tiers: the tier decides what a swing can take and the brush decides what a placement fills,
    /// and putting both on one row would make them look like one selection.
    /// </remarks>
    private void CycleBrush()
    {
        if (_place is null) return;

        GD.Print($"M6c: brush is now {_place.NextBrush().ToString().ToLowerInvariant()}");
    }

    /// <summary>Hands the HUD this frame's state to draw.</summary>
    /// <remarks>
    /// <b>The HUD is told, never asked.</b> Every value here is read from the controllers that
    /// own it, so there is exactly one answer to "what am I holding" — see <c>Hud</c>'s remarks
    /// for what a second copy would cost. The precision flag is recomputed here rather than
    /// cached from <c>UpdatePlacement</c> because it is a held key and the answer belongs to the
    /// frame, not to the last placement.
    /// </remarks>
    private void RefreshHud(double delta)
    {
        if (_hud is null || _place is null || _materials is null)
        {
            return;
        }

        bool precise = _camera is { MouseCaptured: true } && Input.IsKeyPressed(Key.Alt);

        _hud.Refresh(
            delta, _inventory, _materials, Content.Materials, _place, _mining, precise,
            _adaptiveRadius ? $"r{_radiusState.Horizontal}h/{_radiusState.Vertical}v" : null);
    }

    /// <summary>Builds the hotbar, unless this run is a measurement.</summary>
    /// <remarks>
    /// <b>Skipped during a profile and only during a profile.</b> The profile reports draw calls
    /// and primitives, and M1d's deferred 2x2x2 mesh-block question turns on that counter — a
    /// hotbar drawing into the same frame would put UI quads into a number that is supposed to
    /// be terrain. A plain headless run <em>does</em> build it, deliberately: instantiating every
    /// node in that file is the cheapest thing that would catch a script error in it, and a UI
    /// nobody has ever constructed headlessly is M5c-2's shape of hole one layer out.
    /// </remarks>
    private void SetUpHud()
    {
        if (_profiler is not null)
        {
            return;
        }

        _hud = new Hud { Name = "Hud" };
        AddChild(_hud);
    }

    /// <summary>Selects a hotbar slot from a number key.</summary>
    /// <remarks>
    /// <b>The number row moved here from the tool tiers at M6d-3, and the tiers moved to T.</b>
    /// Both wanted 1-9 and only one of them is what a player's hand expects to find there:
    /// Minecraft's number row is the hotbar, and it holds what you build with. The same
    /// muscle-memory argument that swapped Ctrl and Shift back on 2026-08-31 — decades against
    /// this project's few days. Two picks ship, so a cycle key costs the tiers almost nothing.
    /// </remarks>
    private void SelectMaterialSlot(int slot)
    {
        if (_place is null) return;

        int before = _place.MaterialId;
        int materialId = _place.SelectSlot(slot);

        if (materialId < 0)
        {
            GD.Print("M6d: carrying nothing — mine something first");
            return;
        }

        if (materialId == before) return;

        GD.Print(
            $"M6d: placing {Content.Materials.GetIdentifier(materialId)} " +
            $"({_inventory.Litres(materialId):F0} L held)");
    }

    /// <summary>Moves to the next tool tier, wrapping.</summary>
    /// <remarks>
    /// A cycle rather than a key each, because the tiers are an ordered ladder and there are two
    /// of them. If a mod ships eight picks this wants the number row back and the materials want
    /// somewhere else to go, which is the shape of that decision when it arrives.
    /// </remarks>
    private void CycleTier()
    {
        if (_mining is null || _tiers.Count == 0) return;

        int current = _tiers.IndexOf(_mining.Tool);
        ToolDefinition next = _tiers[(current + 1) % _tiers.Count];

        _mining.Tool = next;
        GD.Print(
            $"M3d: {next.Id} — radius {next.RadiusMetres:F2} m, " +
            $"takes hardness <= {next.HardnessThreshold:F1}");
    }

    private void CycleMaterial()
    {
        if (_place is null) return;

        int materialId = _place.NextMaterial();

        GD.Print(materialId < 0
            ? "M6c: carrying nothing — mine something first"
            : $"M6c: placing {Content.Materials.GetIdentifier(materialId)} "
              + $"({_inventory.Litres(materialId):F0} L held)");
    }

    private void ToggleEntangledMiner()
    {
        if (_mining is null)
        {
            return;
        }

        if (_profiler is not null)
        {
            GD.Print("M5d: the entangled miner is unavailable during a profile run.");
            return;
        }

        _mining.Entangled = !_mining.Entangled;

        // One mode, both verbs. Q is a single toggle, so a player who could bite at range but
        // not fill at range would have half a tool — and the half they were missing is the one
        // that puts back what the other half took.
        if (_place is not null)
        {
            _place.Entangled = _mining.Entangled;
        }

        if (!_mining.Entangled)
        {
            GD.Print("M5d: entangled miner OFF — reach back to "
                + $"{_player?.ReachMetres ?? PlayerController.DefaultReachMetres:F1} m");
            return;
        }

        // Says the bite out loud, because it is the one thing about this mode a player cannot
        // see until they have already removed eight metres of world. The hardness clause is
        // still printed: it is the half of the old rule that did not change, and the half that
        // makes a bedrock floor survive being pointed at.
        GD.Print(_mining.EntangledTakesChunk
            ? "M5d: entangled miner ON — reach unlimited (bounded by the loaded world), "
              + $"{1.0f / _mining.EntangledChunkCooldownSeconds:F1} swings/s, "
              + $"and each one takes the WHOLE {VoxelConstants.ChunkSizeMetres:F0} m chunk "
              + $"({VoxelConstants.ChunkVolume:N0} voxels) — still {_mining.Tool.Id.Path}'s "
              + "hardness gate, so it leaves what the tier cannot take. Alt still chips one voxel."
              + "\n      Right mouse FILLS the whole chunk in front of the crosshair, at the same "
              + "rate — only air is replaceable, so it fills holes and never overwrites rock."
            : "M5d: entangled miner ON — reach unlimited (bounded by the loaded world), "
              + $"{1.0f / MiningController.EntangledCooldownSeconds:F0} swings/s, "
              + $"still {_mining.Tool.Id.Path}'s bite and hardness gate");
    }

    private static Vector3 ToGodot(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>Feeds one solved step to the profiler.</summary>
    /// <remarks>
    /// <b>The step is timed directly, not read off <c>frame ms</c>.</b> That trap has been walked
    /// into three times — M2d-1, the mesh-apply budget and M3d — and every time the spike failed
    /// to reproduce while the directly-timed number moved a tenth of a millisecond. This is a
    /// stopwatch around <c>TryStep</c> and nothing else.
    /// </remarks>
    private void RecordPlayerStep(double milliseconds)
    {
        if (_profiler is not { Warm: true } || _player is null)
        {
            return;
        }

        PlayerStepResult step = _player.LastStep;

        // "At rest" is grounded, barely moving, and standing on something that actually
        // exists. All three matter, and the third was learned the hard way: a body held on an
        // unloaded chunk is grounded with its velocity zeroed by the contact, so it looked like
        // rest — and the step where the chunk arrived and turned out to be air then scored a
        // ground-state flicker. The r16/120 sweep reported 5 "flickers" over 28 "resting" steps
        // that were entirely the held-then-released pattern StepsHeld already counts.
        bool atRest = step.Grounded
            && !step.HeldByUnloadedFloor
            && step.Velocity.LengthSquared() < RestingSpeedSquared;

        if (atRest)
        {
            _restingSteps++;
        }

        _profiler.AddPlayerStep(
            milliseconds,
            step.Substeps,
            step.VoxelQueries,
            step.Grounded,
            step.HeldByUnloadedFloor,
            atRest);
    }

    /// <summary>0.01 m²/s² — a tenth of a metre per second, squared.</summary>
    private const float RestingSpeedSquared = 0.01f;

    /// <summary>Steps to stand still at the bottom of a fall before launching the next one.</summary>
    /// <remarks>
    /// One second at 60 Hz, and it is not padding: a fall profile that relaunched the instant it
    /// landed would never put the body at rest, so the ground-flicker detector would never run
    /// and its zero would mean nothing. This is what gives that counter something to count.
    /// </remarks>
    private const int RestStepsBeforeRelaunch = 60;

    /// <summary>
    /// Drops the body again once it has landed and rested, so a long window measures falling.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Each launch moves horizontally past the loaded box</b>, because a second fall down the
    /// same column would descend through chunks that are already resident and measure nothing.
    /// The stride is the full box plus a margin, so every fall meets fresh terrain.
    /// </para>
    /// <para>
    /// The cost is honest and is reported: the horizontal jump reloads the whole box, and that
    /// spike lands inside the measured window. One fall is the clean measurement; the fall count
    /// in the report is what says how much of the window was one.
    /// </para>
    /// </remarks>
    private void AdvanceFallProfile()
    {
        if (!_fallProfile || _player is null || _generator is null)
        {
            return;
        }

        if (!_player.State.Grounded)
        {
            _restedSteps = 0;
            return;
        }

        // Only rest that is actually rest. A body held on an unloaded chunk is "grounded" and is
        // the opposite of landed — relaunching there would cut the measurement short at exactly
        // the moment it was producing the number the profile exists for.
        if (_player.LastStep.HeldByUnloadedFloor)
        {
            _restedSteps = 0;
            return;
        }

        if (++_restedSteps < RestStepsBeforeRelaunch)
        {
            return;
        }

        _restedSteps = 0;
        _falls++;

        float stride = ((2 * ViewDistanceHorizontal) + 4) * VoxelConstants.ChunkSizeMetres;
        System.Numerics.Vector3 feet = _player.FeetCentre;
        float x = feet.X + stride;

        VoxelPos column = VoxelPos.FromMetres(x, 0f, feet.Z);
        System.Numerics.Vector3 launch = PlayerSpawn.FeetOnSurface(
            _generator.SurfaceHeightAt(column.X, column.Z), x, feet.Z);
        launch.Y += _fallHeightMetres;

        _player.Teleport(launch);
        _announcedSpawn = true;

        // A relaunch is a body at rest becoming a body in mid-air, which is the exact shape of
        // the thing the flicker counter looks for. Tell it this one was deliberate.
        _profiler?.ForgetGroundState();
    }

    public override void _Process(double delta)
    {
        if (_pipeline is null)
        {
            return;
        }

        // First, because everything that depends on when it is has to see one value of "now" for
        // the whole frame — and because a storm must be on the uniforms before the frame that
        // draws with them, not after it.
        UpdateWeather(delta);

        AdvanceFlythrough(delta);

        // Before anything can quit, and only while flying: see _flyingEye.
        if (_camera is not null && _player is { Mode: PlayerMode.Fly })
        {
            Vector3 eye = _camera.GlobalPosition;
            _flyingEye = new System.Numerics.Vector3(eye.X, eye.Y, eye.Z);
        }

        UpdateMining(delta);
        UpdatePlacement(delta);
        UpdateSupport(delta);

        // The viewer only moves in whole chunks. Comparing first means a camera drifting
        // inside one chunk never touches the streaming policy at all.
        ChunkPos current = ViewerChunk();
        bool moved = current != _lastViewerChunk;
        _lastViewerChunk = current;

        // The adaptive radius steps every frame, moving or not, because a grow is a thing that
        // happens while nothing else does. Off, the state sits at the ceiling and is never
        // stepped, so the call below is exactly the MoveViewer it was.
        ViewRadiusChange radiusChange = ViewRadiusChange.None;
        if (_adaptiveRadius)
        {
            // The light step's share of its budget is the fact "is the derive keeping up", on any
            // build and any machine: a share is a share whether a derive costs 0.24 ms or 1.4.
            int backlog = _pipeline.LightBuildBacklog;
            double budgetMillis = (_lightBudget ?? ChunkPipeline.DefaultLightBudget).TotalMilliseconds;
            double lightLoad = budgetMillis > 0.0 ? _pipeline.LastLightStepMillis / budgetMillis : 0.0;
            _radiusState = ViewRadiusPolicy.Step(
                _radiusState, moved, backlog, lightLoad, delta, _radiusConfig, out radiusChange);
            RecordRadius(radiusChange, backlog, delta);
        }

        if (moved || radiusChange != ViewRadiusChange.None)
        {
            // Timed, and only from M6d-2. This call is where an unloading shell is swept, which
            // is where Forget became a write — and it sits outside the update bracket below, so
            // every profile before this one measured the pipeline and not the sweep. A radius
            // change is the same box difference a move is, and one call covers both when they
            // land on the same frame, so a ring is never swept twice.
            long beforeMove = Stopwatch.GetTimestamp();
            _pipeline.Streamer.UpdateViewer(
                _viewer, new Viewer(current, _radiusState.Horizontal, _radiusState.Vertical));
            long afterMove = Stopwatch.GetTimestamp();

            if (_profiler is { Warm: true })
            {
                _profiler.ViewerMove.Add(Milliseconds(beforeMove, afterMove));
            }
        }

        long before = Stopwatch.GetTimestamp();
        _pipeline.Update(MaxSubmissionsPerFrame);
        long afterUpdate = Stopwatch.GetTimestamp();
        _freeQueue.Drain();
        long afterFree = Stopwatch.GetTimestamp();

        if (_profiler is { Warm: true })
        {
            // The zero-remesh claim is a difference rather than a total: the fill is still
            // relighting and remeshing through the warm-up, so a session total would report
            // hundreds and say nothing about the storm. Taken once, on the first warm frame.
            if (_remeshesAtWarm < 0)
            {
                _remeshesAtWarm = _pipeline.EditRemeshes;
                _relightsAtWarm = _pipeline.RelightRemeshes;
                _sweepsAtWarm = _scheduler!.Sweeps;
            }

            if (!_supportWarmSeen)
            {
                _supportWarmSeen = true;
                SnapshotSupportAtWarm();
            }

            _profiler.PipelineUpdate.Add(Milliseconds(before, afterUpdate));
            _profiler.MeshFree.Add(Milliseconds(afterUpdate, afterFree));

            // From the pipeline's own clock rather than a stopwatch out here, because the light
            // step is nested inside the update and timing it from the caller would have to
            // bracket the whole thing. It is already counted in update ms; see Light's remarks.
            _profiler.Light.Add(_pipeline.LastLightStepMillis);
        }

        RefreshHud(delta);
        RecordFillMilestones();

        if (_profiler is not null && _profiler.Tick(delta))
        {
            PrintProfile();
            GetTree().Quit();
            return;
        }

        Autosave(delta);
        Report(delta);
    }

    /// <summary>
    /// Moves the world clock on by one frame and hands the shader what the sky is doing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Two <c>SetShaderParameter</c> calls a frame, on one material every chunk shares, and
    /// that is the entire cost of weather.</b> Nothing is remeshed, nothing is repropagated,
    /// nothing enters the merge key: wetness is global and the skylight it multiplies is already
    /// in <c>COLOR.r</c> per vertex. This is the bill M5a paid for in vertex bytes being
    /// collected, and <see cref="PrintProfile"/> asserts the zero by printing it.
    /// </para>
    /// <para>
    /// <b>Overcast dims through <c>sky_brightness</c>, multiplying <see cref="_skyBrightness"/>
    /// rather than replacing it</b>, which is what keeps M5c-2's A/B alive:
    /// <c>--skybrightness=0</c> still renders the terrain pure black in any weather, so the
    /// screenshot that proves block light is a separate channel is still available. Replacing it
    /// would have quietly broken a recorded measurement seam and nothing would have failed.
    /// </para>
    /// <para>
    /// <b>The dimming constant lives here rather than in <see cref="Weather"/>.</b> Core says how
    /// much cloud there is; how dark a skyful of cloud makes the world is taste, and taste that
    /// three other systems integrate against would be taste nobody could retune. Same split as
    /// the face tint being in the shader rather than in the merge key.
    /// </para>
    /// </remarks>
    private void UpdateWeather(double delta)
    {
        _clock.Advance(delta * _clockScale);

        if (_material is null)
        {
            return;
        }

        RefreshSkyHeights();

        _lastFrameSeconds = delta;
        UpdateWater();
        UpdateFluid(delta);

        long now = _clock.ElapsedMillis;
        float wetness = _forcedWeather ?? Weather.WetnessAt(_settings.Seed, now);
        float overcast = _forcedWeather ?? Weather.OvercastAt(_settings.Seed, now);

        float dimming = 1f - (OvercastDimming * overcast);

        _material.SetShaderParameter("wetness", wetness);
        _material.SetShaderParameter("sky_brightness", _skyBrightness * dimming);

        // The sky dome as well as the light, or a storm is a dark landscape under a bright sky
        // and reads as the terrain shader having broken rather than as weather. The two are dimmed
        // by the same factor so ground and horizon stay in step.
        //
        // ON THE SKY MATERIAL, NOT ON Environment.BackgroundEnergyMultiplier, which was tried
        // first and does nothing to a drawn sky: with the multiplier at 0.4 the sky pixels came
        // back bit-identical to the clear-weather arm — (200, 210, 225) in both. It is not a
        // no-op in general, it just does not reach the dome. Sampling the screenshot is what said
        // so; by eye the storm looked convincing enough to sign off.
        //
        // DELIBERATELY NOT SCALED BY _skyBrightness, which the terrain is. That flag scales the
        // light grid's sky *channel* — it is the A/B that isolates block light — and the whole
        // point of M5c-2's screenshot is terrain rendering pure black against a sky that is still
        // there. Folding it in here would have made that arm a black rectangle, which proves
        // nothing at all.
        DimSkyDome(dimming);

        // THE RAIN IS DRIVEN BY THE RATE, NOT BY THE WETNESS, and the two are genuinely different
        // facts: you see drops while it is raining and you see wet ground for a while afterwards.
        // Driving the particles off wetness would leave rain visibly falling out of a clear sky
        // for two minutes after every shower.
        float rate = _forcedWeather
            ?? (Weather.RateAt(_settings.Seed, now)
                / (float)Weather.MaxRateNanometresPerMillisecond);

        if (_rain is not null && _camera is not null)
        {
            _rain.Update(_camera.GlobalPosition, rate, _skyHeights);
            _rain.SetBrightness(dimming);
        }

        _waterSurface?.SetBrightness(_skyBrightness * dimming);
        _fluidSurface?.SetBrightness(_skyBrightness * dimming);
        UpdateFluidLook();

        // Only while the profiler is warm, so the range reported beside the remesh count is the
        // weather the measurement actually saw rather than the weather the warm-up did.
        if (_profiler is { Warm: true })
        {
            _wettestSeen = Math.Max(_wettestSeen, wetness);
            _driestSeen = Math.Min(_driestSeen, wetness);
            _weatherSeen++;
        }
    }

    /// <summary>
    /// Rebuilds the rain's ground-height field when it has a reason to be stale, and not otherwise.
    /// </summary>
    /// <remarks>
    /// <b>Two triggers, and they are the complete set.</b> The field describes a fixed window of
    /// ground heights: it goes stale when the window should be somewhere else, which is a chunk
    /// crossing, or when a height inside it moved, which is
    /// <see cref="ChunkPipeline.SkyHeightChanges"/>. Nothing else can change what it says.
    /// <para>
    /// <b>The dig trigger is the one that matters and it is the cheaper of the two by a mile</b>,
    /// because the counter moves only when a column's open height actually shifts — a tunnel dug
    /// underground never touches it. So the common case of mining rebuilds nothing at all, and the
    /// uncommon case of breaking through to daylight rebuilds once.
    /// </para>
    /// </remarks>
    private void RefreshSkyHeights()
    {
        // Nothing but the rain reads this field, so a run with no rain in it must not pay for one.
        // Headless is that run, and it is also every profile figure this project has recorded
        // since M2d — so without this guard a moving headless profile would quietly carry a
        // millisecond per chunk crossing that no earlier run had.
        if (_pipeline is null || _rain is null)
        {
            return;
        }

        ChunkPos centre = ViewerChunk();
        bool moved = centre.X != _skyFieldCentre.X || centre.Z != _skyFieldCentre.Z;
        bool dug = _pipeline.SkyHeightChanges != _skyChangesAtRebuild;

        if (!moved && !dug)
        {
            return;
        }

        // Counted by cause, because the two are worth very different amounts. A crossing rebuild
        // is bookkeeping; a dig rebuild is the feature, and it is the one link in the chain from
        // "a voxel came out" to "rain falls in there" that no Core test can reach. A run that dug
        // through to daylight and reports zero of these has a wire off — which is exactly the
        // shape of hole M5c-2 found by replacing a call with null and watching nothing fail.
        if (dug) _skyFieldDigRebuilds++;
        if (moved) _skyFieldMoveRebuilds++;

        _skyFieldCentre = centre;
        _skyChangesAtRebuild = _pipeline.SkyHeightChanges;
        _skyHeights.Rebuild(_pipeline.Streamer, centre);

        if (_profiler is { Warm: true })
        {
            _profiler.SkyField.Add(_skyHeights.LastRebuildMillis);
        }
    }

    /// <summary>
    /// Builds the water session against the world that has just been opened.
    /// </summary>
    /// <remarks>
    /// <b>The ledger is loaded before the first window is analysed, and the order is what makes a
    /// reopened world come back with its lakes.</b> The ledger is the persistent half; a window is
    /// a view of the part of it the player is standing in. Analysing first and loading after would
    /// seed the first window from an empty ledger and then bank that emptiness over the top.
    /// </remarks>
    private void SetUpWater()
    {
        if (_generator is null || _noWater)
        {
            return;
        }

        var ledger = new BasinLedger();

        if (_store is not null)
        {
            // A changed lattice discards the volumes rather than refusing the world - the policy
            // is BasinLedger.Load's, and it is the right one: a pit is a sample on the lattice, so
            // changing it renames every basin, and rain refills in minutes where a refused world
            // helps nobody.
            bool kept = ledger.Load(
                _store.ReadBasins(),
                _restoredWatershedCellMetres,
                HeightField.DefaultCellVoxels / VoxelConstants.VoxelsPerMetre);

            if (!kept)
            {
                GD.PushWarning(
                    "M7d: the watershed lattice has changed since this world was written, so its "
                    + "stored water has been discarded. It refills.");
            }
        }

        _water = new WaterSystem(
            _generator,
            _settings.Seed,
            ledger,
            _waterPitch,
            HeightField.DefaultCellVoxels,
            _rainfallScale,
            _clock.ElapsedMillis);
    }

    /// <summary>
    /// Keeps the analysed window under the player and rains into it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The analysis runs on a worker and is installed on the sim thread</b>, which is the whole
    /// of the threading here. M7a measured a 1 km window at 66 ms; anything of that size on the
    /// sim thread would be four frames dropped every time the player crossed a window boundary.
    /// <c>WaterSystem.Analyse</c> is static and pure precisely so that this call is safe while the
    /// old window is still being rained on.
    /// </para>
    /// <para>
    /// <b>The clock advances whether or not a window has landed, and that is deliberate.</b>
    /// <c>Advance</c> is called every frame including while an analysis is in flight; the rain
    /// from that interval is delivered in one step when the window arrives, because the
    /// accumulator is started from what has been delivered rather than from now.
    /// </para>
    /// </remarks>
    private void UpdateWater()
    {
        if (_water is null || _generator is null)
        {
            return;
        }

        _water.Advance(_clock.ElapsedMillis);

        if (_waterAnalysis is { IsCompleted: true })
        {
            System.Threading.Tasks.Task<WaterWindow> finished = _waterAnalysis;
            _waterAnalysis = null;

            if (finished.IsCompletedSuccessfully)
            {
                _water.Install(finished.Result);
            }
            else
            {
                // Loud rather than silent, and not fatal: the world is entirely playable with no
                // standing water, and a run that quietly stopped analysing would look exactly like
                // a dry spell.
                GD.PushError($"M7d: the watershed analysis failed - {finished.Exception?.Message}");
            }
        }

        if (_waterSurface is not null && _water.Window is not null)
        {
            _waterSurface.Update(_lastFrameSeconds, _water);
        }

        if (_waterAnalysis is not null)
        {
            return;
        }

        VoxelPos here = PlayerVoxel();
        if (!_water.NeedsWindowFor(here.X, here.Z))
        {
            return;
        }

        // Captured into locals before the closure, because every one of these is read on a worker
        // and three of them are fields this class rewrites.
        ISurfaceHeights heights = _generator;
        (int originX, int originZ) = _water.WindowOriginFor(here.X, here.Z);
        int pitch = _water.Pitch;
        int cell = _water.CellVoxels;

        _waterAnalysis = System.Threading.Tasks.Task.Run(() =>
        {
            long started = Stopwatch.GetTimestamp();
            WaterWindow window = WaterSystem.Analyse(heights, originX, originZ, pitch, cell);
            _lastAnalysisMillis = Milliseconds(started, Stopwatch.GetTimestamp());
            return window;
        });
    }

    /// <summary>Where the player is, in voxels. The camera while flying, the body while walking.</summary>
    private VoxelPos PlayerVoxel()
    {
        Vector3 position = _player is { Mode: PlayerMode.Walk }
            ? ToGodot(_player.FeetCentre)
            : _camera?.GlobalPosition ?? Vector3.Zero;

        return VoxelPos.FromMetres(position.X, position.Y, position.Z);
    }

    /// <summary>Dims the sky dome to match the light, by scaling the colours it is drawn from.</summary>
    /// <remarks>
    /// <b>Two more obvious levers were tried first and neither reaches the drawn dome:</b>
    /// <c>Environment.BackgroundEnergyMultiplier</c> and
    /// <c>ProceduralSkyMaterial.EnergyMultiplier</c>. Both were confirmed to be set — the material
    /// resolves, the property takes the value — and the sky pixels came back <i>bit-identical</i>
    /// to the clear-weather arm either way. So the colours are scaled directly, which is the lever
    /// that does work.
    /// <para>
    /// <b>Recorded because the failure mode is silent and would be tried again.</b> Both properties
    /// are the ones a person reaches for, both look like they worked, and the only thing that says
    /// otherwise is sampling two screenshots. Eyeballing was not enough: by eye the storm arm
    /// looked convincingly stormy, because the terrain really had darkened and the sky above it
    /// had not.
    /// </para>
    /// </remarks>
    private void DimSkyDome(float dimming)
    {
        if (_skyMaterial is null || _skyColours.Length != 4)
        {
            return;
        }

        _skyMaterial.SkyTopColor = _skyColours[0] * dimming;
        _skyMaterial.SkyHorizonColor = _skyColours[1] * dimming;
        _skyMaterial.GroundHorizonColor = _skyColours[2] * dimming;
        _skyMaterial.GroundBottomColor = _skyColours[3] * dimming;

        // AND THE FOG, which is the half that was actually visible. M5d turned the environment's
        // fog off for the terrain and left it on for the sky — deliberately, so the procedural
        // sky's dark ground hemisphere does not show as a black band along the horizon. The
        // consequence nobody had cause to notice until now is that most of the sky you can see
        // IS the fog, at a fixed colour, which is why three attempts at dimming the dome itself
        // changed nothing measurable.
        if (_environment is not null)
        {
            _environment.FogLightColor = _baseFogColour * dimming;
        }
    }

    /// <summary>How much of the daylight a full overcast takes away.</summary>
    /// <remarks>
    /// A feel constant, and M7d is where a person judges it. It is deliberately short of 1: a
    /// storm should read as a storm rather than as nightfall, and the world going black under
    /// cloud would be indistinguishable from the renderer having failed — which is the specific
    /// thing M7d's play question about "weather rather than the renderer breaking" asks.
    /// </remarks>
    private const float OvercastDimming = 0.6f;

    /// <summary>Writes the dirty set on a timer, so a crash costs a minute rather than a session.</summary>
    /// <remarks>
    /// <para>
    /// <b>Synchronous, on the sim thread, deliberately.</b> <see cref="ChunkPipeline.SaveAll"/>
    /// waits for the serial lane to come out of the store first and then writes inline; the
    /// asynchronous path exists and is what an unload uses, but a timer that hands work to a queue
    /// leaves the crash window it was built to close still open.
    /// </para>
    /// <para>
    /// Nothing here is conditional on having edited: <c>SaveAll</c> with a clean world walks two
    /// lists and writes nothing, which is what
    /// <c>SaveAllIsIdempotentAndCheapWhenNothingIsDirty</c> pins.
    /// </para>
    /// </remarks>
    private void Autosave(double delta)
    {
        if (_pipeline is null || _store is null || _autosaveSeconds <= 0.0)
        {
            return;
        }

        _sinceAutosave += delta;
        if (_sinceAutosave < _autosaveSeconds)
        {
            return;
        }

        // Postponed a frame at a time while a tick owns the grid (slice 3a): SaveAll snapshots
        // the panels and refuses mid-tick, and a tick under a flood can span tens of frames. The
        // timer is left where it is, so the save fires on the first frame after the boundary.
        if (_fluid is { GridOwnedByTick: true })
        {
            return;
        }

        _sinceAutosave = 0.0;

        long before = Stopwatch.GetTimestamp();

        // The header first, because it carries the inventory and the player — the session state
        // that no chunk holds. Writing only the chunks would save the tunnel and forget what the
        // player was carrying when they dug it.
        _pipeline.WriteWorldHeader(CurrentHeader());
        WriteBasins();

        int panelsBefore = _pipeline.PanelsSaved;
        (int chunks, int columns) = _pipeline.SaveAll();
        int panels = _pipeline.PanelsSaved - panelsBefore;

        // Only when it did something. A line every thirty seconds saying nothing happened is how
        // a log stops being read.
        if (chunks > 0 || columns > 0 || panels > 0)
        {
            GD.Print(
                $"M6: autosaved {chunks} chunk(s), {columns} column(s) and {panels} fluid panel(s) in "
                + $"{Milliseconds(before, Stopwatch.GetTimestamp()):F1} ms");
        }
    }

    private static double Milliseconds(long from, long to) =>
        (to - from) * 1000.0 / Stopwatch.Frequency;

    /// <summary>
    /// Notes when the world first drew, when light first settled, and when everything did.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Three moments rather than one, because M5 moved the first and M5-PLAN estimated the
    /// second.</b> The plan put ~0.25 s against the propagation and nothing against the derive,
    /// which M5c-1 then measured as the larger half — so "how long does a standing start take to
    /// light" needs the light's own answer and the player's answer side by side.
    /// </para>
    /// <para>
    /// <b>Latched once each and never reset.</b> A flythrough crosses chunk boundaries forever,
    /// so light goes in and out of flight all run; only the first settle is the fill. Reading a
    /// later one would report how long the last chunk column took and label it the world.
    /// </para>
    /// <para>
    /// Recorded on every run, not only a profile, so the periodic report can print them from a
    /// plain headless run with no window and no measured window.
    /// </para>
    /// </remarks>
    private void RecordFillMilestones()
    {
        if (_pipeline is null)
        {
            return;
        }

        if (_firstDrawSeconds < 0.0 && _instances.Count > 0)
        {
            _firstDrawSeconds = FillSeconds();
        }

        if (_lightSettledSeconds < 0.0 && _pipeline.LightBuilds > 0 && !_pipeline.LightInFlight)
        {
            _lightSettledSeconds = FillSeconds();
        }

        // The instrument the distance-ordered derive queue is judged by. "The ground is gone"
        // (2026-09-03) was the player's own neighbourhood deriving last, and neither first draw
        // nor light settled can see that: inside a mined-out void nothing near the player has
        // geometry to draw, and light settled is the whole box. Two chunks is 16 m, the far side
        // of a mined-out room.
        if (_nearLitSeconds < 0.0 && _pipeline.LightBuilds > 0
            && _pipeline.AllDerivedWithin(ViewerChunk(), NearLitRadiusChunks))
        {
            _nearLitSeconds = FillSeconds();
        }

        // The adaptive radius's own milestone: the first moment the box stood at its ceiling with
        // the light settled. Under a growth the milestones above read whichever box existed when
        // they fired — first draw is the floor's — and this is the one comparable with a fixed
        // arm's light settled. With the radius fixed the two coincide.
        if (_grownSeconds < 0.0 && _radiusState.Horizontal >= ViewDistanceHorizontal
            && _pipeline.LightBuilds > 0 && !_pipeline.LightInFlight)
        {
            _grownSeconds = FillSeconds();
        }

        // Guarded on having drawn something as well as on being quiescent: the very first frame
        // is quiescent by construction — nothing has been asked for yet — and would latch a
        // settle time of zero for a world that had not started.
        if (_settledSeconds < 0.0 && _instances.Count > 0 && _pipeline.IsQuiescent)
        {
            _settledSeconds = FillSeconds();
        }
    }

    private double FillSeconds() =>
        (Stopwatch.GetTimestamp() - _fillStartedTicks) / (double)Stopwatch.Frequency;

    /// <summary>The standing-start fill, as one line.</summary>
    /// <remarks>
    /// Each figure prints "not yet" rather than a zero when its moment has not arrived. A
    /// flythrough at 40 m/s never settles, and "0.00 s" for that would read as instantaneous
    /// rather than as never — the same trap the ground-flicker detector fell into twice.
    /// </remarks>
    private string FillLine()
    {
        static string Seconds(double value) => value < 0.0 ? "not yet" : $"{value:F2} s";

        return $"fill      first draw {Seconds(_firstDrawSeconds)}, "
               + $"near lit {Seconds(_nearLitSeconds)} (within {NearLitRadiusChunks} chunks), "
               + $"light settled {Seconds(_lightSettledSeconds)}, "
               + $"all settled {Seconds(_settledSeconds)}"
               + (_adaptiveRadius ? $", grown {Seconds(_grownSeconds)} (at r{ViewDistanceHorizontal}h with light settled)" : string.Empty)
               + "\n";
    }

    /// <summary>Counts what the policy did this frame, for the radius line and the periodic report.</summary>
    private void RecordRadius(ViewRadiusChange change, int backlog, double delta)
    {
        switch (change)
        {
            case ViewRadiusChange.Shrank:
                _radiusShrinks++;
                break;
            case ViewRadiusChange.Grew:
                _radiusGrows++;
                break;
        }

        _smallestRadius = Math.Min(_smallestRadius, _radiusState.Horizontal);
        if (_radiusState.Horizontal < _secondsAtRadius.Length)
        {
            _secondsAtRadius[_radiusState.Horizontal] += delta;
        }

        if (_profiler is { Warm: true })
        {
            _backlogPeak = Math.Max(_backlogPeak, backlog);
            _backlogSum += backlog;
            _backlogSamples++;
            _loadPeak = Math.Max(_loadPeak, _radiusState.LightLoad);
            _loadSum += _radiusState.LightLoad;
        }
    }

    /// <summary>
    /// The adaptive radius's account of itself: what it did, where it spent the run, and the
    /// backlog it was reading. The time at each radius is the whole run, because a shrink in the
    /// warm-up is still where the box was; the backlog figures are the window.
    /// </summary>
    private string RadiusLines()
    {
        if (!_adaptiveRadius)
        {
            return string.Empty;
        }

        var spent = new List<string>();
        for (int h = _radiusConfig.FloorHorizontal; h < _secondsAtRadius.Length; h++)
        {
            if (_secondsAtRadius[h] > 0.0)
            {
                spent.Add($"r{h} {_secondsAtRadius[h]:F1} s");
            }
        }

        string signals = _backlogSamples > 0
            ? $"derive backlog peak {_backlogPeak:N0}, mean {(double)_backlogSum / _backlogSamples:F0}; " +
              $"light load peak {_loadPeak:F2}, mean {_loadSum / _backlogSamples:F2} of the budget, over {_backlogSamples:N0} frame(s)"
            : "signals not sampled (the window never opened)";

        bool atCeiling = _radiusState.Horizontal >= ViewDistanceHorizontal;
        return
            $"radius    {_radiusShrinks} shrink(s), {_radiusGrows} grow(s), smallest r{_smallestRadius}h, " +
            $"ending r{_radiusState.Horizontal}h/{_radiusState.Vertical}v {(atCeiling ? "at" : "below")} the ceiling | " +
            $"time at each radius (whole run): {string.Join(", ", spent)} | {signals} | " +
            $"floor r{_radiusConfig.FloorHorizontal}h/{_radiusConfig.FloorVertical}v, " +
            $"{_radiusConfig.ShrinkSlabs} slab(s) to shrink, grow below {_radiusConfig.GrowBelowLoad:F2} load, " +
            $"{_radiusConfig.CadenceSeconds:F1} s cadence\n";
    }

    /// <summary>What the light step cost, with every total's denominator beside it.</summary>
    /// <remarks>
    /// <b>The derive/propagate split is the point.</b> M5-PLAN costed the walk at ~779k visits
    /// and ~15 ms and said nothing about the derive; M5c-1 measured the derive as the larger
    /// half by an order of magnitude. Printing them apart is what keeps that statement true or
    /// catches it going stale, and it is the number <c>PaddedChunk.CopyFrom</c> would move.
    /// </remarks>
    private string LightLines()
    {
        if (_pipeline is null || _profiler is null)
        {
            return string.Empty;
        }

        // RunSeconds, not MeasuredSeconds: every counter below is a lifetime total on the
        // pipeline that kept counting through the warm-up, so the window is the wrong
        // denominator for it. See StreamingProfiler.RunSeconds.
        double seconds = Math.Max(_profiler.RunSeconds, 0.001);
        double derive = _pipeline.LightDeriveMillis;
        double propagate = _pipeline.LightPropagateMillis;
        long cells = _pipeline.LightCellsVisited;

        // Per-cell cost against the walk alone, not against the whole step. M5-PLAN's ~20 ns
        // guess is a guess about a cell visit, and dividing by the derive as well would compare
        // it against a different quantity and call the difference a prediction error.
        string perCell = cells > 0 ? $"{propagate * 1e6 / cells:F0} ns/cell" : "no cells";

        return $"{_profiler.Light.Line("light ms")}   (inside update ms, not beside it)\n"
               + $"lighting  {derive:F0} ms deriving / {propagate:F0} ms propagating "
               + $"over the whole run\n"
               + $"cells     {cells} visited, {cells / seconds:F0}/s over the "
               + $"{seconds:F1} s window, {perCell}\n"
               + $"derives   {_pipeline.LightBuilds} / {_pipeline.Streamer.TrackedCount} tracked, "
               + $"{_pipeline.UniformLightBuilds} uniform "
               + $"({(_pipeline.LightBuilds > 0 ? $"{100.0 * _pipeline.UniformLightBuilds / _pipeline.LightBuilds:F0}%" : "n/a")}), "
               + $"{_pipeline.RelightRemeshes} relight remesh(es)\n";
    }

    /// <summary>
    /// Builds the mining controller and the target highlight, and lists the tiers.
    /// </summary>
    /// <remarks>
    /// The tiers come out of the registry in numeric-ID order, which is load order, so the
    /// number keys are stable between runs and a mod that adds a tier simply appears at the
    /// end. Nothing here knows a tool by name.
    /// </remarks>
    private void SetUpMining()
    {
        foreach ((_, _, ToolDefinition tier) in Content.Tools.Entries())
        {
            _tiers.Add(tier);
        }

        if (_tiers.Count == 0)
        {
            GD.PushWarning("Overburden: no tool tiers loaded, so there is nothing to mine with.");
            return;
        }

        // The strongest tier by default. Starting on the weakest would be a truer tutorial,
        // but the first swing at stone would then do nothing and read as a broken build.
        ToolDefinition strongest = _tiers[0];
        foreach (ToolDefinition tier in _tiers)
        {
            if (tier.HardnessThreshold > strongest.HardnessThreshold)
            {
                strongest = tier;
            }
        }

        // Reach comes from the player, who owns it as of M4c. Nothing here has a default to
        // fall back on, which is the point — MiningController.ReachMetres is `required`.
        _mining = new MiningController(_pipeline!, _materials!, strongest)
        {
            Inventory = _inventory,
            ReachMetres = _player?.ReachMetres ?? PlayerController.DefaultReachMetres,

            // Set here rather than in the profile block, because Q is a play bind: a rate that
            // only reached a profile run would be a sweep seam for the one arm that cannot
            // judge it.
            EntangledChunkCooldownSeconds = _chunkCooldownSeconds,
        };

        _highlight = new VoxelHighlight(GetWorld3D());

        var names = new List<string>();
        for (int i = 0; i < _tiers.Count; i++)
        {
            names.Add($"{i + 1}:{_tiers[i].Id.Path}");
        }

        _mining.Entangled = _entangleWhileProfiling;
        _mining.EntangledTakesChunk = !_entangledSphereWhileProfiling;

        GD.Print($"M3d: mining with {strongest.Id} — tiers {string.Join(", ", names)}");
    }

    /// <summary>
    /// One frame of mining: aim, outline the target, and swing if the button is down.
    /// </summary>
    /// <remarks>
    /// The swing is timed separately from <see cref="ChunkPipeline.Update"/> because they are
    /// different costs answering different questions. This is the carve itself on the sim
    /// thread; <c>update ms</c> is where the remeshes it caused come back and get turned into
    /// meshes, a frame or more later. M3's real question — does a pick swing fit inside a
    /// frame? — is about the second number, and this one is here to prove it is not the
    /// first.
    /// </remarks>
    /// <summary>Builds the placement controller and the outline that shows where a brush lands.</summary>
    private void SetUpPlacement()
    {
        if (_pipeline is null || _materials is null || _player is null)
        {
            return;
        }

        _place = new PlaceController(_pipeline, _materials, _inventory)
        {
            Brush = _startingBrush,
            // The player's reach, the same number mining uses. A brush is not a tool and does not
            // get to reach further than an arm — M3d's decision, restated at M4c.
            ReachMetres = _player.ReachMetres,
            // Both halves of the mode move together, so an --entanglesphere A/B compares one tool
            // against one tool rather than a chunk bite against a brush fill. Set here rather than
            // beside the miner's own assignment because this controller is built after it, and a
            // --entangle profile that armed the bite but not the fill would be an arm nobody asked
            // for — the M5c-2 hole one layer out, in a seam rather than in the logic.
            Entangled = _entangleWhileProfiling,
            EntangledFillsChunk = !_entangledSphereWhileProfiling,
            ChunkCooldownSeconds = _chunkCooldownSeconds,
        };

        // A second outline, a metre across and in a different colour, because the brush lands on
        // the lattice rather than on the voxel under the crosshair. Without it the snap reads as
        // the game ignoring the aim, which is precisely what M6d has to judge.
        _cellHighlight = new VoxelHighlight(
            GetWorld3D(), BrushCell.SizeMetres, new Color(0.95f, 0.85f, 0.25f, 0.55f));

        if (_startingStock > 0)
        {
            // A profile seam, not a gift: --place has to have something to place, and mining
            // first would measure the carve as well as the brush.
            foreach ((int numericId, _, _) in Content.Materials.Entries())
            {
                if (numericId != MaterialIds.AirNumericId)
                {
                    _inventory.Add(numericId, _startingStock);
                }
            }
        }

        GD.Print(
            $"M6d: placing with the {_place.Brush.ToString().ToLowerInvariant()} brush — "
            + "right mouse places, B cycles the brush, 1-9 pick a material (V cycles), "
            + "T cycles the tool tier, Alt for one voxel");
    }

    /// <summary>Casts for the brush, places if the button is held, and moves the cell outline.</summary>
    private void UpdatePlacement(double delta)
    {
        if (_place is null || _camera is null || _player is null)
        {
            return;
        }

        Vector3 origin = _camera.GlobalPosition;
        Vector3 forward = -_camera.GlobalTransform.Basis.Z;
        bool placing = _camera.MouseCaptured && Input.IsMouseButtonPressed(MouseButton.Right);

        // The same modifier as mining, deliberately: Alt+left takes one voxel and Alt+right
        // places one, so "precision" is a thing the player learns once rather than per verb.
        bool precise = placing && Input.IsKeyPressed(Key.Alt);

        if (_placeWhileProfiling)
        {
            // Along the look direction rather than straight down, which is what --mine does. A
            // brush aimed at the ground under the camera lands in the cell the player is standing
            // in, where the body refusal eats most of it — so the run would measure the refusal.
            placing = true;

            // Brushes unless the precise arm was asked for, and stated rather than left to fall
            // out of "no key is held" — the same argument --mine makes. A voxel is 1/64th of a
            // cube, so a profile that quietly went precise would report a number 64 times cheaper
            // under the same label, which is why the report names the arm as well.
            precise = _precisePlaceWhileProfiling;
        }

        // Null while flying: the body is inert in fly mode and sits wherever the last F left it,
        // so passing it would refuse voxels around a position the player is no longer at.
        Overburden.Core.Physics.Aabb? body =
            _player.Mode == PlayerMode.Walk && _player.Spawned ? _player.State.Box : null;

        long before = Stopwatch.GetTimestamp();

        bool placed = _place.Update(
            delta,
            new System.Numerics.Vector3(origin.X, origin.Y, origin.Z),
            new System.Numerics.Vector3(forward.X, forward.Y, forward.Z),
            body,
            placing,
            precise);

        if (placed && _profiler is { Warm: true })
        {
            _profiler.Place.Add(Milliseconds(before, Stopwatch.GetTimestamp()));
        }

        if (placed && _place.RanOutThisFrame && !_announcedEmpty)
        {
            // Once, until something is placed successfully again. There is no HUD until M6d, so
            // the log is the only feedback there is — and a line every frame is not feedback.
            _announcedEmpty = true;
            GD.Print("M6c: out of material — mine some more, or press V for something else");
        }
        else if (placed && !_place.RanOutThisFrame)
        {
            _announcedEmpty = false;
        }

        if (_cellHighlight is null) return;

        if (!_place.HasTarget)
        {
            _cellHighlight.Hide();
            return;
        }

        // The outline follows the modifier, or the player cannot see what precision would do
        // until after they have done it. One voxel while Alt is held, one metre otherwise.
        bool preciseNow = precise || (_camera.MouseCaptured && Input.IsKeyPressed(Key.Alt));

        // Three scales now, and the chunk one is not cosmetic for the reason the mining outline
        // records: a chunk boundary is drawn nowhere, so a 1 m cell outline in front of an 8 m
        // fill shows the player the wrong object. Precision still wins, because Alt fills exactly
        // the voxel in the box.
        if (_place.TakesWholeChunk && !preciseNow)
        {
            (float fx, float fy, float fz) = _place.TargetChunk.OriginMetres;
            _cellHighlight.ShowAt(new Vector3(fx, fy, fz), VoxelConstants.ChunkSizeMetres);
            return;
        }

        (float x, float y, float z) = preciseNow
            ? _place.TargetVoxel.MinCornerMetres
            : _place.TargetCell.OriginVoxel.MinCornerMetres;

        _cellHighlight.ShowAt(
            new Vector3(x, y, z),
            preciseNow ? VoxelConstants.VoxelSize : BrushCell.SizeMetres);
    }

    private bool _announcedEmpty;

    private void UpdateMining(double delta)
    {
        if (_mining is null || _camera is null)
        {
            return;
        }

        Vector3 origin = _camera.GlobalPosition;
        Vector3 forward = -_camera.GlobalTransform.Basis.Z;
        bool mining = _camera.MouseCaptured && Input.IsMouseButtonPressed(MouseButton.Left);

        // Alt takes exactly the highlighted voxel instead of the tool's sphere, and holds the
        // same meaning for the other button: **one modifier means precision across both verbs.**
        // A held modifier rather than a toggle, because it is the exception and not the mode.
        //
        // **Moved off Ctrl at M6d-1, closing a note recorded at M3d.** Ctrl is free in walk mode
        // but is also FlyCam's descend, so precision mining while flying sank the builder about
        // 1.8 m between chips. Alt is free in walk and is *crawl* in fly, so "slow down to do
        // fine work" pairs with the modifier rather than fighting it — which is the argument
        // that named Alt as the better key a milestone before anything needed it.
        bool precise = mining && Input.IsKeyPressed(Key.Alt);

        if (_mineWhileProfiling)
        {
            // Straight down from a camera four metres above the surface. Aiming along the
            // flight path would spend most of the run pointing at sky, and a measurement of
            // mining that mostly misses is not one.
            //
            // ENTANGLED IS THE EXCEPTION, and it is the whole reason the flag exists: with the
            // reach bounded by the loaded world rather than by an arm, aiming along the flight
            // path finally hits the hillside ahead instead of nothing. Pointing at sky still
            // costs no swing — the cast returns Missed or Blocked and the button does nothing —
            // so the run measures fewer swings that each reach much further, which is exactly
            // the claim being checked.
            forward = _mining.Entangled ? forward : Vector3.Down;
            mining = true;

            // Spheres, always, and stated rather than left to fall out of "no key is held".
            // M3d's baseline — 1.2 clones and 2.4 KiB per swing, two orders of magnitude
            // inside a frame — measures the sphere, which is the expensive shape. A profile
            // that quietly chipped would report a cheaper number under the same label and
            // read as an improvement.
            precise = false;
        }

        long before = Stopwatch.GetTimestamp();

        bool swung = _mining.Update(
            delta,
            new System.Numerics.Vector3(origin.X, origin.Y, origin.Z),
            new System.Numerics.Vector3(forward.X, forward.Y, forward.Z),
            mining,
            precise);

        if (swung && _profiler is { Warm: true })
        {
            _profiler.Swing.Add(Milliseconds(before, Stopwatch.GetTimestamp()));
        }

        PlaceDebugLamp();
        RelayWater();

        if (_highlight is null)
        {
            return;
        }

        if (!_mining.HasTarget)
        {
            _highlight.Hide();
            return;
        }

        // Outline what the swing will actually take. For a sphere that is still the target
        // voxel — the bite is centred on it and roughly its own scale, so the box reads as
        // "here". A chunk bite is 8 m of world whose boundary is drawn nowhere, so the same box
        // would be showing the player the wrong object: aim a hand's width across an invisible
        // seam and a different cube goes. Precision wins, because Alt takes exactly the voxel
        // and that is the one case where the small box is the literal truth.
        if (_mining.TakesWholeChunk && !precise)
        {
            (float cx, float cy, float cz) = _mining.Hit.Voxel.Chunk.OriginMetres;
            _highlight.ShowAt(new Vector3(cx, cy, cz), VoxelConstants.ChunkSizeMetres);
        }
        else
        {
            _highlight.ShowAt(_mining.Hit.Voxel);
        }
    }

    /// <summary>
    /// L places one lamp in the empty voxel in front of the crosshair.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>M5b needs a light source in the world and M6 owns placement.</b> So: one key, one
    /// material, no inventory, no cooldown, no tier — the smallest thing that makes M5d
    /// playable, and small enough that nobody mistakes it for the feature.
    /// </para>
    /// <para>
    /// On a key-down edge rather than polled, for the reason the walk/fly toggle is: a placement
    /// read every frame lays sixty lamps a second. Nothing happens visually until M5c wires
    /// light into the pipeline — the lamp is a pale block that does not yet glow.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Places one lamp where the crosshair is pointing, once, without a keypress.
    /// </summary>
    /// <remarks>
    /// <b>Exists because a screenshot cannot press L, and "does block light reach the screen" is
    /// a look question.</b> With <c>--skybrightness=0</c> the lamp is the only light in the
    /// world, so the shot answers the one link the sky channel cannot: that
    /// <c>COLOR.g</c> carries block light and is tinted separately. Fired once and only once —
    /// the same edge-triggering the L key needs, for the same reason M5b recorded, since a
    /// per-frame read lays sixty lamps a second.
    /// </remarks>
    private void PlaceScriptedLamp()
    {
        if (!_debugLamp || _scriptedLampPlaced || _mining is null || _lampMaterialId < 0) return;

        // Nothing to place against until the world under the crosshair has actually loaded.
        if (!_mining.HasTarget) return;

        _scriptedLampPlaced = _mining.TryPlaceDebug(_lampMaterialId);

        if (_scriptedLampPlaced)
        {
            GD.Print($"M5c: scripted lamp at {_mining.Hit.Previous}");
        }
    }

    private bool _debugLamp;
    private bool _scriptedLampPlaced;

    /// <summary>Places a lamp where the crosshair points. A debug bind, and a used one.</summary>
    /// <remarks>
    /// <b>Do not remove this because the brushes supersede it — they do not, at range.</b>
    /// <c>TryPlaceDebug</c> places at <see cref="MiningController.Hit"/>, which respects the
    /// <em>effective</em> reach, so with the entangled miner on (<c>Q</c>) this drops a lamp
    /// anywhere in the loaded world. <b>Reported as load-bearing during the M7 play, 2026-09-02:</b>
    /// <em>"hitting Q then being able to place a light far off into the distance has been useful
    /// during the debug, and I don't want to lose the feature yet."</em> Right mouse cannot do it,
    /// because placement is bounded by the player's reach and reach is deliberately the player's
    /// rather than the tool's — locked at M3d and restated at M4c.
    /// <para>
    /// <b>So this is the second debug bind whose value is the pairing rather than the bind</b>, the
    /// first being the entangled miner itself. Neither is content; both are how a dark cave gets
    /// lit well enough to photograph. <b>Revisit trigger: a placement verb that takes a reach
    /// argument</b>, which would make this expressible through the brushes and let it go.
    /// </para>
    /// </remarks>
    private void PlaceDebugLamp()
    {
        PlaceScriptedLamp();

        if (_mining is null || _camera is null || !_camera.MouseCaptured) return;

        bool pressed = Input.IsKeyPressed(Key.L);
        bool edge = pressed && !_lampKeyHeld;
        _lampKeyHeld = pressed;

        if (!edge || _lampMaterialId < 0) return;

        if (_mining.TryPlaceDebug(_lampMaterialId))
        {
            GD.Print($"M5b: placed {LampId} at {_mining.Hit.Previous}");
        }
    }

    /// <summary>The material the debug bind places, or −1 if the base game does not ship one.</summary>
    /// <remarks>
    /// Resolved once and by identifier, not baked in as a number: material IDs come from the
    /// world's table and a mod that is not installed leaves a hole. A missing lamp disables the
    /// bind rather than placing whatever happens to hold that slot.
    /// </remarks>
    private int _lampMaterialId = -1;

    private bool _lampKeyHeld;

    private const string LampId = "overburden:lamp";

    /// <summary>
    /// Drives the camera along a fixed path during a profile run, hugging the surface.
    /// </summary>
    /// <remarks>
    /// A scripted path rather than a recorded one so two runs are comparable — the whole
    /// point is to put margin 2 against margin 1, or one view distance against another, and
    /// a hand-flown route differs more between runs than the thing being measured.
    /// Surface-following keeps terrain inside the vertical box, which is the case a player
    /// is actually in; flying at a fixed altitude would drift the ground out of view and
    /// quietly stop measuring meshing at all.
    /// </remarks>
    private void AdvanceFlythrough(double delta)
    {
        if (_flySpeed <= 0f || _camera is null || _generator is null)
        {
            return;
        }

        // The hanging-around arm: fly for N seconds, then stand. Counted from the first frame the
        // flythrough moves, warm-up included, so a run's "stopped at" is a time on the same clock
        // as its milestones.
        if (_sprintForSeconds > 0.0)
        {
            _flownSeconds += delta;
            if (_flownSeconds >= _sprintForSeconds)
            {
                return;
            }
        }

        if (_turnaroundSeconds > 0.0)
        {
            _sinceTurnaround += delta;
            if (_sinceTurnaround >= _turnaroundSeconds)
            {
                _sinceTurnaround -= _turnaroundSeconds;
                _flyDirection = -_flyDirection;
                _turnarounds++;
            }
        }

        Vector3 position = _camera.GlobalPosition;
        position.X += _flyDirection * _flySpeed * (float)delta;

        VoxelPos voxel = VoxelPos.FromMetres(position.X, position.Y, position.Z);
        int surface = _generator.SurfaceHeightAt(voxel.X, voxel.Z);
        position.Y = (surface + 16) * VoxelConstants.VoxelSize;

        _camera.GlobalPosition = position;
    }

    private void PrintProfile()
    {
        if (_pipeline is null || _profiler is null)
        {
            return;
        }

        ChunkStreamer streamer = _pipeline.Streamer;

        GD.Print(
            $"\n=== Overburden streaming profile ===\n" +
            $"config    r{ViewDistanceHorizontal}h/{ViewDistanceVertical}v " +
            $"+{streamer.LoadMargin} margin, {_scheduler!.ThreadCount} workers, " +
            (_fallProfile
                ? $"falling from {_fallHeightMetres:F0} m, terminal "
                  + $"{_player!.Config.TerminalSpeedMetresPerSecond:F0} m/s, {_falls + 1} fall(s)"
                : $"{_flySpeed:F0} m/s") +
            (_turnaroundSeconds > 0.0
                ? $", turning every {_turnaroundSeconds:F0} s ({_turnarounds} turn(s))"
                : string.Empty) +
            (_sprintForSeconds > 0.0 ? $", stopping after {_sprintForSeconds:F0} s" : string.Empty) +
            (_adaptiveRadius
                ? $", adaptive from r{_radiusConfig.FloorHorizontal}h/{_radiusConfig.FloorVertical}v"
                : string.Empty) +
            (_mining is null ? "\n" : $", {_mining.Tool.Id}\n") +
            $"{_profiler.PipelineUpdate.Line("update ms")}\n" +
            $"{_profiler.MeshFree.Line("free ms")}   (deferred by Godot — see MeshFreeQueue)\n" +
            (_profiler.ViewerMove.Count > 0
                ? $"{_profiler.ViewerMove.Line("viewer ms")}   (box difference, "
                  + $"{_profiler.ViewerMove.Count} move(s), beside update ms not inside it)\n"
                : "viewer ms  (the viewer never changed chunk — the sweep did not run)\n") +
            $"{_profiler.Frame.Line("frame ms")}\n" +
            $"main      {_profiler.PipelineUpdate.Total() / Math.Max(_profiler.MeasuredSeconds, 0.001) / 10.0:F2}% " +
            $"of one core, streaming on the main thread\n" +
            $"chunks    {streamer.ViewedCount} viewed / {streamer.TrackedCount} tracked / " +
            $"{_instances.Count} drawn\n" +
            GeometryLine() +
            $"jobs      {_pipeline.CompletedJobs} done / {_pipeline.DroppedJobs} dropped / " +
            $"{_pipeline.FaultedJobs} faulted\n" +
            $"applying  {_pipeline.AppliedResults} applied, {_pipeline.PendingResults} still " +
            $"waiting at the end of the run\n" +
            SweepLines() +
            $"freeing   {_freeQueue.TotalFreed} released, peak queue {_freeQueue.PeakPending}\n" +
            FillLine() +
            RadiusLines() +
            LightLines() +
            MiningLines() +
            PlacementLines() +
            PersistenceLines() +
            CacheLines() +
            SupportLines() +
            WeatherLines() +
            _profiler.PlayerLines(_profiler.MeasuredSeconds) +
            $"{StreamingProfiler.RenderLine()}\n" +
            $"{StreamingProfiler.MemoryLine()}\n");
    }

    /// <summary>
    /// The standing water: how much there is, where it is being analysed, and what that costs.
    /// </summary>
    /// <remarks>
    /// <b>Litres beside the wall clock is the measurement M7d exists to take</b>, because "slower
    /// than realtime is accuracy, not approximation" has been a principle since M7a's planning and
    /// has never had a number against it. A pond rising at a rate nobody can see is the thing
    /// <c>rainfallScale</c> is for, and this line is what a person picks that scale against.
    /// <para>
    /// <b>Basins-with-water against basins-in-the-window is the denominator that matters.</b> Zero
    /// litres and zero wet basins is a dry spell; zero litres and a window that has never been
    /// analysed is a wire that came loose, and from outside they look identical.
    /// </para>
    /// </remarks>
    private string WaterSummary()
    {
        WaterSystem water = _water!;

        if (water.Window is null)
        {
            string queued = _waterAnalysis is null ? "none queued" : "one in flight";
            return $"no window analysed yet ({queued})";
        }

        int basins = water.Window.Watershed.Basins.Count;
        int wet = 0;
        for (int basin = 0; basin < basins; basin++)
        {
            if (water.LevelAt(basin) is not null) wet++;
        }

        string overflow = water.SeedOverflowLitres > 0
            ? $", {water.SeedOverflowLitres:N0} L would not fit back"
            : string.Empty;

        long capacity = water.WindowCapacityLitres;
        string full = capacity > 0
            ? $" ({100.0 * water.StoredLitres / capacity:F1}% of {capacity:N0} L)"
            : string.Empty;

        return $"{water.StoredLitres:N0} L standing{full} in {wet}/{basins} basin(s), "
               + $"{water.Ledger.Count} basin(s) banked ({water.Ledger.TotalLitres:N0} L), "
               + $"{water.SpilledLitres:N0} L spilled, {water.InjectedLitres:N0} L injected, "
               + $"{water.DrawnLitres:N0} L drawn into Tier B, {water.SeepedLitres:N0} L seeped into it, "
               + $"{water.ReceivedLitres:N0} L received from it | "
               + $"window {water.SpanMetres} m x{water.WindowsInstalled}, "
               + $"{_lastAnalysisMillis:F1} ms to analyse, scale x{water.RainfallScale}"
               + overflow
               + (_waterSurface is null
                   ? " | no surface drawn (headless or --nowater)"
                   : $" | surface {_waterSurface.QuadCount} quad(s) over "
                     + $"{_waterSurface.WetBasins} basin(s), {_waterSurface.RingSamples} shore sample(s), {_waterSurface.DugSamples} dug sample(s) skipped, "
                     + $"{_waterSurface.LastRebuildMillis:F2} ms");
    }

    /// <summary>
    /// The occlusion field's state, beside the weather that reads it.
    /// </summary>
    /// <remarks>
    /// <b>"Unknown" against the total is the load-bearing figure and the reason this is printed at
    /// all.</b> Rain falling through the ground and a field that was never built look identical
    /// from the outside, and so do "the rain stopped at the surface" and "there was no rain". A
    /// large unknown share at a small view radius is the recorded artefact — the field is 128 m
    /// and the loaded region is not — while the same share at r16 is a defect. Neither is
    /// diagnosable from the picture.
    /// </remarks>
    private string SkyFieldSummary()
    {
        if (_rain is null)
        {
            return "no rain drawn (headless or --norain)";
        }

        double unknownShare =
            100.0 * _skyHeights.UnknownTexels / (SkyHeightField.Size * SkyHeightField.Size);

        return $"field {_skyHeights.Rebuilds} rebuild(s) "
               + $"({_skyFieldMoveRebuilds} moved / {_skyFieldDigRebuilds} dug), "
               + $"{_skyHeights.LastRebuildMillis:F2} ms, "
               + $"{unknownShare:F0}% unknown, ground "
               + $"{_skyHeights.LowestKnown:F1}..{_skyHeights.HighestKnown:F1} m, "
               + $"rain {(_rain.Falling ? "falling" : "off")}";
    }

    /// <summary>
    /// The weather half of the profile, and the number M7c exists to print.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Remeshes during the measured window, which must be zero.</b> That is the assertion, and
    /// it is the same shape as M5c-2 asserting a quad count: the claim "the wet term is one
    /// uniform" is not something the code can be read to establish, because the way it would fail
    /// — wetness reaching the merge key, or the overcast reseeding skylight — looks fine at every
    /// call site and shows up only as the world quietly remeshing itself while it rains.
    /// </para>
    /// <para>
    /// <b>Printed with its denominator</b>, which here is the weather the window actually saw: a
    /// zero from a run that stayed dry is a detector that never fired, not a pass. The range of
    /// the wetness term across the window is what says the storm happened, and <c>--clockscale</c>
    /// is how a fifteen-second window is made to contain one.
    /// </para>
    /// </remarks>
    private string WeatherLines()
    {
        int edits = _remeshesAtWarm < 0 ? 0 : _pipeline!.EditRemeshes - _remeshesAtWarm;
        int relights = _relightsAtWarm < 0 ? 0 : _pipeline!.RelightRemeshes - _relightsAtWarm;

        string verdict = _weatherSeen switch
        {
            0 => "the weather never ran",
            _ when _wettestSeen - _driestSeen < 0.01f =>
                $"the weather did not move (wet stayed at {_wettestSeen:F2}) — nothing was tested",
            _ => $"wet ranged {_driestSeen:F2}..{_wettestSeen:F2} over "
                 + $"{_clock.ElapsedSeconds:F0} s of world time",
        };

        string field = _profiler!.SkyField.Count > 0
            ? _profiler.SkyField.Line("skyfield ms") + "   (occlusion field refills, "
              + $"{_profiler.SkyField.Count} of them, beside viewer ms not inside it)\n"
            : "skyfield ms  (never refilled in the window — no crossing and nothing dug)\n";

        return $"weather   {edits} edit remesh(es) / {relights} relight remesh(es) during the "
               + $"measured window — {verdict}\n"
               + $"rain      {SkyFieldSummary()}\n"
               + field
               + WaterLines()
               + FluidLines();
    }

    /// <summary>
    /// The water half of the profile, with litres against wall clock.
    /// </summary>
    /// <remarks>
    /// <b>Litres per minute of <em>wall</em> clock, not of world time</b>, and the distinction is
    /// the whole point: <c>--clockscale</c> and <c>--rainscale</c> both make a pond fill faster and
    /// only one of them leaves the weather honest. A figure quoted per world-minute would read the
    /// same under either, which is exactly the confusion this milestone must not hand to whoever
    /// picks the scale.
    /// </remarks>
    private string WaterLines()
    {
        if (_water is null)
        {
            return "water     no water system (no world generator)\n";
        }

        // RunSeconds for the same reason the lighting line uses it: the litres are a lifetime
        // total and the rain has been falling since the world opened.
        double seconds = Math.Max(_profiler!.RunSeconds, 0.001);

        // Banked first, then the ledger alone. The ledger is the truth and the window is a view
        // of part of it, so window plus ledger counted every in-window basin twice — this line
        // read 2x on a world with one lake since M7d-1, and the L-per-minute figure with it.
        // Found at M8b-2c while adding the seam's terms.
        _water.Bank();
        long held = _water.Ledger.TotalLitres;
        string flight = _waterAnalysis is null ? string.Empty : ", one still in flight";

        string surface = _waterSurface is null
            ? "surface   not drawn (headless or --nowater)\n"
            : $"surface   {_waterSurface.QuadCount} quad(s) over {_waterSurface.WetBasins} basin(s), "
              + $"{_waterSurface.RingSamples} dry sample(s) drawing a neighbour's shore ({(_waterSurface.DilateShore ? "--shore=1" : "--shore=0")}), "
              + $"{_waterSurface.LastRebuildMillis:F2} ms to rebuild, "
              + $"{_waterSurface.Rebuilds} rebuild(s)\n";

        return $"water     {held:N0} L held, {_water.SpilledLitres:N0} L spilled, "
               + $"{_water.InjectedLitres:N0} L injected, {_water.DrawnLitres:N0} L drawn into Tier B, "
               + $"{_water.SeepedLitres:N0} L seeped into it, {_water.ReceivedLitres:N0} L received from it, "
               + $"{held * 60.0 / seconds:N0} L per wall-clock minute at scale "
               + $"x{_water.RainfallScale}\n"
               + $"analysis  {_lastAnalysisMillis:F1} ms for a {_water.SpanMetres} m window "
               + $"({_water.Pitch}x{_water.Pitch} samples), {_water.WindowsInstalled} installed"
               + $"{flight}\n"
               + surface;
    }

    // ---------------------------------------------------------------- M9b: the support field in the loop

    /// <summary>
    /// Builds the region and hands it to the pipeline, which pumps its queue into detached
    /// derives and tells it what its own hooks see: arrivals, edits, forgets.
    /// </summary>
    /// <remarks>
    /// After the water, because the cost table reads the table at submission (M9-PLAN decision 5)
    /// — <c>WaterSystem</c> is the <see cref="IWaterTable"/>, or nothing under <c>--nowater</c>.
    /// The table-move trigger is M9d's; a lake that rises over a settled roof afterwards is not
    /// re-derived until something else touches the roof, and that is stated so nobody reads it
    /// as a defect before then.
    /// </remarks>
    private void SetUpSupport()
    {
        if (_pipeline is null || _materials is null)
        {
            return;
        }

        if (!_supportOn)
        {
            GD.Print("M9: support is off (--support=0) — no region, no derives, no tint; the control arm");
            return;
        }

        ChunkStreamer streamer = _pipeline.Streamer;
        _support = new SupportRegion(position => streamer.Get(position)?.Voxels, _materials, SupportRule.Default, _water);
        _pipeline.Support = _support;
        _pipeline.SupportTint = _supportTint;
        GD.Print(
            $"M9: support field on — K = {SupportRule.MaxBudget}, refund every {SupportRule.Default.RestRefundVoxels} voxel(s) of rest; "
            + "a dug chunk and its six face neighbours are analysed, derives run as detached jobs at most "
            + $"{ChunkPipeline.MaxSupportSubmissionsPerFrame} a frame, and the tint reads settled fields only "
            + $"({(_supportTint ? "tint on" : "tint off, --supporttint=0")}; --support=0 is the control)");

        SetUpCollapse();
    }

    // ---------------------------------------------------------------- M9c-1b: the fall in the loop

    private const string RubbleId = "overburden:rubble";

    /// <summary>
    /// The scheduler over the region, the pipeline as the editor (so every fallen voxel goes
    /// through the hook chain a swing does — light, remesh, the region, the water) and the
    /// automaton as the water, or none under <c>--nowater</c>.
    /// </summary>
    /// <remarks>
    /// Rubble is looked up like the lamp — <c>TryGetNumericId</c>, never a fallback to air — and
    /// a world without it (a mod that removed the file) gets the warning-only arm with a line
    /// saying so, because a fall that wrote air would delete rock rather than move it.
    /// </remarks>
    private void SetUpCollapse()
    {
        if (_support is null || _pipeline is null || _materials is null)
        {
            return;
        }

        if (!Content.Materials.TryGetNumericId(Identifier.Parse(RubbleId), out int rubble) || !_materials.IsSolid(rubble))
        {
            GD.Print($"M9: collapse is off — {RubbleId} is not a solid material in this world's content, so a fall has nothing to write");
            return;
        }

        _collapse = new CollapseScheduler(_support, _pipeline, _materials, rubble, _settings.Seed, _collapseWindow, _fluid)
        {
            FireEnabled = _collapseOn,
        };
        GD.Print(
            $"M9: collapse {(_collapseOn ? "on" : "arming only (--collapse=0)")} — a settled chunk with something at risk arms "
            + $"{_collapseWindow.MinSeconds:0.##}–{_collapseWindow.MaxSeconds:0.##} s of world time by its deficit, ×0.5–1.5 seeded "
            + $"(--collapseafter=min,max); rubble is ID {rubble}; the sweep runs "
            + $"{(_fluid is null ? "every frame, no water to wait for (--nowater)" : "at each fluid boundary, waiting on fluid-pending chunks")}");
    }

    /// <summary>
    /// One boundary's sweep: arm, disarm, fire, and for each fall one line, the unstick, and the
    /// timings. From <see cref="UpdateFluid"/>'s <c>begin</c> block after the pours and before the
    /// field sync — the frame that owns the grid — or from <see cref="UpdateSupport"/> every frame
    /// when there is no water.
    /// </summary>
    private void SweepCollapse()
    {
        if (_collapse is null)
        {
            return;
        }

        long t0 = Stopwatch.GetTimestamp();
        IReadOnlyList<CollapseFire> fires = _collapse.Sweep(_clock.ElapsedMillis);
        double sweepMillis = Milliseconds(t0, Stopwatch.GetTimestamp());
        _collapseSweepMillisSum += sweepMillis;
        _collapseSweepMillisMax = Math.Max(_collapseSweepMillisMax, sweepMillis);
        if (fires.Count == 0)
        {
            return;
        }

        // The sweep's time is the falls' when anything fell; a sweep over ~1,500 settled fields
        // with nothing due is microseconds, so the max is the fall's.
        _collapseFallMillisMax = Math.Max(_collapseFallMillisMax, sweepMillis);
        foreach (CollapseFire fire in fires)
        {
            CollapseResult r = fire.Result;
            string water = _fluid is null
                ? string.Empty
                : $", lifted {r.LiftedMillilitres / (double)FluidConstants.MillilitresPerLitre:N0} L / returned {r.ReturnedMillilitres / (double)FluidConstants.MillilitresPerLitre:N0} L"
                  + (r.DisplacedMillilitres > 0 ? $" (*** {r.DisplacedMillilitres} mL DISPLACED ***)" : string.Empty);
            GD.Print(
                $"M9: fell — chunk {r.Chunk}: {r.Voxels:N0} voxel(s) in {r.Columns:N0} column(s), deepest drop {r.DeepestDrop} "
                + $"(mean {r.MeanDrop:F1}), {r.FellFrom.Count} chunk(s) fell from, {r.Written.Count} written into"
                + (r.StaleSkipped > 0 ? $", {r.StaleSkipped} stale skipped" : string.Empty)
                + water
                + (fire.CascadeStage ? ", cascade" : string.Empty)
                + $" at t {fire.WorldMillis / 1000.0:F1} s; the sweep took {sweepMillis:F1} ms");
            UnstickAfterFall(r);
        }
    }

    /// <summary>
    /// Decision 8's half of c-1: a body with rubble in its box is stuck on every axis, and health
    /// is M11's. After a fall whose written chunks overlap the body, lift it clear through
    /// <see cref="PlayerSpawn.TryUnstick"/> — up only, 4 m — or say it is buried and leave it,
    /// with <c>F</c> the only door until M11. Walking only: the flying body has no collision.
    /// </summary>
    private void UnstickAfterFall(CollapseResult fall)
    {
        if (_player is null || _pipeline is null || _materials is null || _player.Mode != PlayerMode.Walk)
        {
            return;
        }

        Overburden.Core.Physics.Aabb box = _player.State.Box;
        (VoxelPos lo, VoxelPos hi) = box.VoxelBounds;
        bool overlaps = false;
        foreach (ChunkPos written in fall.Written)
        {
            if (written.X >= lo.Chunk.X && written.X <= hi.Chunk.X
                && written.Y >= lo.Chunk.Y && written.Y <= hi.Chunk.Y
                && written.Z >= lo.Chunk.Z && written.Z <= hi.Chunk.Z)
            {
                overlaps = true;
                break;
            }
        }

        if (!overlaps || !VoxelCollider.OverlapsSolid(_pipeline, _materials, box))
        {
            return;
        }

        if (PlayerSpawn.TryUnstick(_pipeline, _materials, box, out Overburden.Core.Physics.Aabb freed))
        {
            _collapseFreed++;
            float lifted = freed.Min.Y - box.Min.Y;
            _player.Teleport(freed.FeetCentre);
            GD.Print($"M9: the player was freed {lifted:F2} m up after the fall in chunk {fall.Chunk}");
        }
        else
        {
            _collapseBuried++;
            GD.Print($"M9: the player is buried under the fall in chunk {fall.Chunk} — past the {PlayerSpawn.DefaultMaxUnstickMetres} m unstick; F is the door until M11");
        }
    }

    /// <summary>The collapse's terms on the periodic <c>M9:</c> line and the profile's <c>support</c> line.</summary>
    private string CollapseTerms(bool window)
    {
        if (_collapse is null)
        {
            return "; no collapse";
        }

        CollapseScheduler c = _collapse;
        long fired = c.Fired - (window ? _collapseFiredAtWarm : 0);
        long voxels = c.VoxelsFallen - (window ? _collapseVoxelsAtWarm : 0);
        long drop = c.DropSum - (window ? _collapseDropAtWarm : 0);
        long stale = c.StaleSkipped - (window ? _collapseStaleAtWarm : 0);
        long lifted = c.LiftedMillilitres - (window ? _collapseLiftedAtWarm : 0);
        long returned = c.ReturnedMillilitres - (window ? _collapseReturnedAtWarm : 0);
        long sweeps = c.Sweeps - (window ? _collapseSweepsAtWarm : 0);
        double sweepMillis = _collapseSweepMillisSum - (window ? _collapseSweepMillisAtWarm : 0);
        int freed = _collapseFreed - (window ? _collapseFreedAtWarm : 0);
        int buried = _collapseBuried - (window ? _collapseBuriedAtWarm : 0);
        const double litre = FluidConstants.MillilitresPerLitre;
        return $"; collapse {(c.FireEnabled ? "on" : "arming only")}: {c.Armed:N0} armed / {c.Due:N0} due / waiting (unsettled {c.WaitingUnsettled:N0}, fluid-pending {c.WaitingFluid:N0}), "
               + $"{fired:N0} fired{(window ? " in the window" : string.Empty)} ({c.Fired:N0} in all), {voxels:N0} voxel(s) fallen "
               + $"(mean drop {(voxels > 0 ? drop / (double)voxels : 0):F1}, deepest {c.DeepestDrop}), {stale:N0} stale skipped, "
               + (_fluid is null ? "no water" : $"lifted {lifted / litre:N0} L / returned {returned / litre:N0} L{(c.DisplacedMillilitres > 0 ? $" (*** {c.DisplacedMillilitres} mL DISPLACED ***)" : string.Empty)}")
               + $", {freed} freed / {buried} buried; {sweeps:N0} sweep(s), mean {(sweeps > 0 ? sweepMillis / sweeps : 0):F3} ms, "
               + $"worst {_collapseSweepMillisMax:F1} ms, longest fall {_collapseFallMillisMax:F1} ms";
    }

    /// <summary>The bite provocation, and the <c>M9: at open</c> line once.</summary>
    private void UpdateSupport(double delta)
    {
        if (_support is null || _pipeline is null)
        {
            return;
        }

        _supportSeconds += delta;
        BiteChunksIfDue();

        // M9c-1, decision 6: without water there is no boundary, so the sweep runs every frame
        // here; with water it runs in UpdateFluid's begin block, the frame that owns the grid.
        if (_fluid is null)
        {
            SweepCollapse();
        }

        // Once, when the fill has lit and every derive the open queued has landed — the moment
        // the at-risk count is the world's rather than a round's. A named world's open is what
        // this prints for; on the seed world it prints a zero with its denominator.
        if (!_supportOpenReported
            && _support.Derives > 0
            && _support.Pending == 0
            && _pipeline.SupportDerivesInFlight == 0
            && !_pipeline.LightInFlight)
        {
            _supportOpenReported = true;
            GD.Print(
                $"M9: at open — {_support.Derives:N0} derive(s) ({_support.Derives - _support.TrivialDerives:N0} searched) over "
                + $"{_support.Analysed.Count:N0} analysed chunk(s) in {_supportSeconds:F1} s; "
                + $"{_support.SettledAtRiskVoxels:N0} voxel(s) at risk in {_support.SettledAtRiskChunks:N0} chunk(s), "
                + $"{_support.FieldCount:N0} field(s) holding {_support.FieldBytes / 1024.0:N0} KiB; "
                + $"worker mean {(_pipeline.SupportDerivesApplied > 0 ? _pipeline.SupportWorkerMillis / _pipeline.SupportDerivesApplied : 0):F2} ms "
                + $"max {_pipeline.SupportWorkerMillisMax:F2} ms");
        }
    }

    /// <summary>
    /// Bites an n × n square of chunks, one chunk deep, under the player when the window opens —
    /// the breach's shape one scale up, and the bench's fixture 2 for n = 2 — so a headless run
    /// has a roof to read and a screenshot has an underside to look at.
    /// </summary>
    private void BiteChunksIfDue()
    {
        if (_bitten || _biteChunks <= 0 || _pipeline is null || _support is null)
        {
            return;
        }

        bool due = _profiler is null ? _supportSeconds >= 3.0 : _profiler.Warm;
        if (!due)
        {
            return;
        }

        _bitten = true;
        ChunkPos centre = PlayerVoxel().Chunk;
        int half = _biteChunks / 2;
        var min = new ChunkPos(centre.X - half, centre.Y - _biteDepth, centre.Z - half);
        var max = new ChunkPos(min.X + _biteChunks - 1, min.Y, min.Z + _biteChunks - 1);

        long before = Stopwatch.GetTimestamp();
        int removed = 0;
        int unloaded = 0;
        for (int cz = min.Z; cz <= max.Z; cz++)
        for (int cy = min.Y; cy <= max.Y; cy++)
        for (int cx = min.X; cx <= max.X; cx++)
        {
            var chunk = new ChunkPos(cx, cy, cz);
            if (_pipeline.Streamer.Get(chunk)?.Voxels is null)
            {
                unloaded++;
                continue;
            }

            VoxelPos origin = chunk.OriginVoxel;
            for (int z = 0; z < VoxelConstants.ChunkSize; z++)
            for (int x = 0; x < VoxelConstants.ChunkSize; x++)
            for (int y = 0; y < VoxelConstants.ChunkSize; y++)
            {
                if (_pipeline.SetVoxel(origin.Offset(x, y, z), MaterialIds.AirNumericId))
                {
                    removed++;
                }
            }
        }

        GD.Print(
            $"M9: bit a {_biteChunks}x{_biteChunks} square of chunks from {min} to {max}, {_biteDepth} chunk(s) under "
            + $"the player's, in {Milliseconds(before, Stopwatch.GetTimestamp()):F1} ms: {removed:N0} voxel(s) removed, "
            + $"{unloaded} chunk(s) not loaded; {_support.Analysed.Count:N0} analysed, {_support.Pending:N0} queued (--bitechunks)");

        if (_inBite && _camera is not null)
        {
            // The centre of the bitten block, in metres: a plug is an underside, and no view from
            // the surface shows one. The flythrough leaves the camera alone at --speed=0.
            VoxelPos lo = min.OriginVoxel;
            VoxelPos hi = max.OriginVoxel.Offset(VoxelConstants.ChunkSize, VoxelConstants.ChunkSize, VoxelConstants.ChunkSize);
            _camera.GlobalPosition = new Vector3(
                (lo.X + hi.X) * 0.5f * VoxelConstants.VoxelSize,
                (lo.Y + hi.Y) * 0.5f * VoxelConstants.VoxelSize,
                (lo.Z + hi.Z) * 0.5f * VoxelConstants.VoxelSize);
            GD.Print($"M9: the camera is in the bite at {_camera.GlobalPosition} (--inbite)");
        }
    }

    private void SnapshotSupportAtWarm()
    {
        if (_support is null || _pipeline is null)
        {
            return;
        }

        _supportDerivesAtWarm = _pipeline.SupportDerivesApplied;
        _supportRegionDerivesAtWarm = _support.Derives;
        _supportTrivialAtWarm = _support.TrivialDerives;
        _supportRequeuedAtWarm = _support.RequeuedStale;
        _supportDroppedAtWarm = _support.DroppedDeparted;
        _supportWorkerAtWarm = _pipeline.SupportWorkerMillis;
        _supportCopyAtWarm = _pipeline.SupportCopyMillis;
        _supportGatherAtWarm = _pipeline.SupportGatherMillis;
        _supportApplyAtWarm = _pipeline.SupportApplyMillis;
        _strainRemeshesAtWarm = _pipeline.StrainRemeshes;
        _strainUnchangedAtWarm = _pipeline.StrainSettlesUnchanged;
        for (int i = 0; i < _supportTriggersAtWarm.Length; i++)
        {
            _supportTriggersAtWarm[i] = _support.Triggered((SupportTrigger)i);
        }

        if (_collapse is not null)
        {
            _collapseSweepsAtWarm = _collapse.Sweeps;
            _collapseFiredAtWarm = _collapse.Fired;
            _collapseVoxelsAtWarm = _collapse.VoxelsFallen;
            _collapseDropAtWarm = _collapse.DropSum;
            _collapseStaleAtWarm = _collapse.StaleSkipped;
            _collapseLiftedAtWarm = _collapse.LiftedMillilitres;
            _collapseReturnedAtWarm = _collapse.ReturnedMillilitres;
            _collapseFreedAtWarm = _collapseFreed;
            _collapseBuriedAtWarm = _collapseBuried;
            _collapseSweepMillisAtWarm = _collapseSweepMillisSum;
        }
    }

    /// <summary>The periodic <c>M9:</c> line: the analysed set, the derives by cause, the barrier, and what is at risk.</summary>
    private string SupportSummary()
    {
        SupportRegion r = _support!;
        ChunkPipeline p = _pipeline!;
        long applied = p.SupportDerivesApplied;
        return $"{r.Analysed.Count:N0} analysed ({r.Dug.Count:N0} dug), {r.FieldCount:N0} field(s) holding {r.FieldBytes / 1024.0:N0} KiB; "
               + $"{r.Derives:N0} derive(s) — {r.Derives - r.TrivialDerives:N0} searched, {r.TrivialDerives:N0} closed-form, "
               + $"{r.RequeuedStale:N0} requeued stale, {r.DroppedDeparted:N0} dropped departed — by cause "
               + $"edit {r.Triggered(SupportTrigger.Edit):N0} / arrival {r.Triggered(SupportTrigger.Arrival):N0} / "
               + $"neighbour {r.Triggered(SupportTrigger.Neighbour):N0} / departure {r.Triggered(SupportTrigger.Departure):N0} / "
               + $"table {r.Triggered(SupportTrigger.Table):N0}; "
               + $"{p.SupportDerivesInFlight:N0} in flight, {r.Pending:N0} pending, {r.Unsettled:N0} unsettled; "
               + $"worker mean {(applied > 0 ? p.SupportWorkerMillis / applied : 0):F2} ms max {p.SupportWorkerMillisMax:F2} ms "
               + $"(copy {(applied > 0 ? p.SupportCopyMillis / applied : 0):F2}), sim gather {(p.SupportDerivesSubmitted > 0 ? p.SupportGatherMillis / p.SupportDerivesSubmitted : 0):F3} ms apply {(applied > 0 ? p.SupportApplyMillis / applied : 0):F3} ms; "
               + $"at risk {r.SettledAtRiskVoxels:N0} voxel(s) in {r.SettledAtRiskChunks:N0} settled chunk(s) "
               + $"({r.AtRiskVoxels:N0} in {r.AtRiskChunks:N0} counting unsettled); "
               + $"{p.StrainRemeshes:N0} tint remesh(es), {p.StrainSettlesUnchanged:N0} settle(s) changed nothing on screen"
               + (p.SupportDerivesFaulted > 0 ? $"; *** {p.SupportDerivesFaulted} derive(s) FAULTED ***" : string.Empty)
               + CollapseTerms(window: false);
    }

    /// <summary>The profile's <c>support</c> line, with the window's own denominators.</summary>
    private string SupportLines()
    {
        if (_support is null || _pipeline is null)
        {
            return $"support   no support field ({(_supportOn ? "no world" : "--support=0")})\n";
        }

        SupportRegion r = _support;
        ChunkPipeline p = _pipeline;
        double seconds = Math.Max(_profiler!.MeasuredSeconds, 0.001);
        long applied = p.SupportDerivesApplied - _supportDerivesAtWarm;
        long derives = r.Derives - _supportRegionDerivesAtWarm;
        long trivial = r.TrivialDerives - _supportTrivialAtWarm;
        double worker = p.SupportWorkerMillis - _supportWorkerAtWarm;
        double copy = p.SupportCopyMillis - _supportCopyAtWarm;
        double gather = p.SupportGatherMillis - _supportGatherAtWarm;
        double apply = p.SupportApplyMillis - _supportApplyAtWarm;
        string causes = string.Join(" / ", Enum.GetValues<SupportTrigger>().Select(
            t => $"{t.ToString().ToLowerInvariant()} {r.Triggered(t) - _supportTriggersAtWarm[(int)t]:N0}"));

        return $"support   {r.Analysed.Count:N0} analysed ({r.Dug.Count:N0} dug), {r.FieldCount:N0} field(s) holding {r.FieldBytes / 1024.0:N0} KiB; "
               + $"{derives:N0} derive(s) in the window ({derives / seconds:F1}/s) — {derives - trivial:N0} searched, {trivial:N0} closed-form, "
               + $"{r.RequeuedStale - _supportRequeuedAtWarm:N0} requeued stale, {r.DroppedDeparted - _supportDroppedAtWarm:N0} dropped departed, "
               + $"{r.Derives:N0} in all — queued by cause {causes}; "
               + $"{p.SupportDerivesInFlight:N0} in flight, {r.Pending:N0} pending, {r.Unsettled:N0} unsettled at the end; "
               + $"worker mean {(applied > 0 ? worker / applied : 0):F2} ms max {p.SupportWorkerMillisMax:F2} ms "
               + $"(copy {(applied > 0 ? copy / applied : 0):F2} ms), {worker / seconds / 10.0:F2}% of one worker; "
               + $"sim gather {(applied > 0 ? gather / Math.Max(1, p.SupportDerivesSubmitted - _supportDerivesAtWarm) : 0):F3} ms "
               + $"apply {(applied > 0 ? apply / applied : 0):F3} ms a derive, {(gather + apply) / seconds / 10.0:F3}% of the sim thread; "
               + $"at risk {r.SettledAtRiskVoxels:N0} voxel(s) in {r.SettledAtRiskChunks:N0} settled chunk(s) "
               + $"({r.AtRiskVoxels:N0} in {r.AtRiskChunks:N0} counting unsettled); "
               + $"{p.StrainRemeshes - _strainRemeshesAtWarm:N0} tint remesh(es) in the window, "
               + $"{p.StrainSettlesUnchanged - _strainUnchangedAtWarm:N0} settle(s) changed nothing on screen"
               + $"{(_supportTint ? string.Empty : " (tint off, --supporttint=0)")}"
               + (p.SupportDerivesFaulted > 0 ? $"; *** {p.SupportDerivesFaulted} derive(s) FAULTED ***" : string.Empty)
               + CollapseTerms(window: true)
               + "\n";
    }

    // ---------------------------------------------------------------- M8b: Tier B in the loop

    // M8b-4b read every fluid row here at open, into paused panels. M8b-4c reads the index instead
    // (in the pipeline, beside the chunk index) and each chunk's row arrives with the chunk, off
    // the serial lane, so the open is not a function of how much water the world holds and the
    // grid is bounded by the box rather than the world. The `M8b: at quit` line stays; what the
    // next open puts back is read off the periodic line's `loaded` as the chunks come home.

    private void SetUpFluid(bool windowed)
    {
        if (_pipeline is null || _noWater)
        {
            return;
        }

        var terrain = new VoxelFluidTerrain(_pipeline.Streamer, _materials!);

        // M8b-2c's seed surface off the columns (1.2 ms a bite against 10.3 with the function),
        // built first because M8d-5's ceiling reads it too.
        _seedHeights = new StreamerSeedHeights(_pipeline.Streamer, _generator!);

        // M8d-5: a lake is a body Tier B cannot enter. The band over every lake's bed, refreshed
        // at the boundary; the terrain the simulation is given is wrapped in it, so the field's
        // bits never carry the band and a level moving a voxel costs no rebake. The seep index and
        // the portal index keep the bare terrain — a band cell is air to them, and a portal's
        // mouth is the dug voxel beside it.
        _lakeCeiling = _water is null || !_lakeBody ? null : new LakeCeiling(_seedHeights, _water);

        // The field is a terrain over a terrain, so the simulation cannot tell which it was given
        // and every fixture and bench arm stays valid. See FluidField.
        _field = _fluidFieldOn ? new FluidField(terrain) : null;
        IFluidTerrain simTerrain = (IFluidTerrain?)_field ?? terrain;
        if (_lakeCeiling is not null)
        {
            simTerrain = new LakeBoundedTerrain(simTerrain, _lakeCeiling);
        }

        _fluid = new FluidSimulation(simTerrain, spread: _spread, fallCells: _fallCells);
        if (_fallCells != FluidSimulation.DefaultFallCells)
        {
            GD.Print($"M8d: a visit falls at most {_fallCells} cell(s) (--fallcells; {FluidSimulation.DefaultFallCells} is the default, 1 the pre-M8d-3 arm)");
        }
        _fluid.FarFloorCells = _fluidFarFloor;
        _fluidBudgetTicks = FluidSimulation.BudgetTicks(_fluidBudget);
        GD.Print($"M8b: Tier B spread rule is {_spread.ToString().ToLowerInvariant()} (--spread=fixed|counted)");
        GD.Print(_field is null
            ? "M8b: the tick reads the streamer directly (--fluidfield=0, the control arm)"
            : $"M8b: the tick reads a baked field, {FluidField.BytesPerChunk / 1024} KiB a chunk over the "
              + "wet chunks and their face neighbours (--fluidfield=0 is the control)");
        GD.Print(_fluidNearRings > 0
            ? $"M8b: near water is {_fluidNearRings} ring(s) of chunks, visited every tick; far water "
              + $"is admitted at least {_fluidFarFloor:N0} cell(s) a tick (--fluidnear=0 is no order)"
            : "M8b: no near/far order — every awake cell is visited every tick (--fluidnear=<rings>)");
        if (_fluidJobOn && _field is null)
        {
            _fluidJobOn = false;
            GD.Print("M8b: --fluidjob=1 needs the field a worker can read and --fluidfield=0 took it away; the tick stays on the sim thread");
        }

        GD.Print(_fluidJobOn
            ? "M8b: the tick runs as a scheduler job on a worker, one tick in flight at a time; "
              + "--fluidbudget means nothing on this arm (--fluidjob=0 is the sim-thread arm)"
            : $"M8b: the tick runs on the sim thread in slices of {_fluidBudget.TotalMilliseconds:F1} ms a frame (--fluidjob=1 is the worker arm)");
        _fluid.Publish();

        // M8b-4b: the save path takes its panels from here. After construction because the
        // terrain above is the pipeline's own streamer, which is the whole reason it is a
        // property.
        _pipeline.Fluid = _fluid;

        // M8b-2c: the seam. The index over the same holdability the automaton uses, the tracker
        // that keeps it to dug chunks, and the drain that connects it to Tier A once a tick.
        _portals = new PortalIndex(_seedHeights, terrain);

        // M8c-2: Tier C. The seep index over the same terrain — its bulk read goes through the
        // streamer's fluid gate, so a chunk whose water is still landing is nobody's rock — fed
        // by the same tracker under the same dug filter, and the drain beside the portal drain.
        // No water, or a scale of zero, builds neither: nothing scans, nothing seeps.
        _seeps = _water is not null && _seepScale > 0 ? new SeepIndex(_seedHeights, terrain) : null;
        _portalTracker = new PortalTracker(_portals, _seeps);
        _drain = _water is null ? null : new PortalDrain(_portals, _water, _fluid);
        _seepDrain = _seeps is null ? null : new SeepDrain(_seeps, _water!, _fluid, _seepScale, mode: _seepMode);
        GD.Print(_seepDrain is null
            ? $"M8c: seepage is off ({(_water is null ? "no water" : "--seepscale=0")}); the seep index is not built"
            : $"M8c: seepage at {_seepScale} L/(m²·m·s) — a dug chunk under a lake takes permeability × head × face area × that "
              + $"out of the lake each tick, {(_seepMode == SeepMode.Sweat ? "each group handing litres round its faces (--seepmode=sweat)" : "lowest face first (--seepmode=spring)")} "
              + "(--seepscale=0 is off, the default is an estimate at the nuisance end)");

        // M8b-3a: the other direction. Water resting on the seed ground is the basin's, by the
        // rain model; measured to remove 99.3–99.5% of a surface spill's visits (--spill).
        _handoff = _water is null ? null : new SurfaceHandoff(_seedHeights, _water, _fluid, _lakeCeiling);
        GD.Print(_lakeCeiling is null
            ? $"M8d: no lakes to keep Tier B out of ({(_water is null ? "no water" : "--lakebody=0, the measurement arm: a hole in a bank treadmills")})"
            : "M8d: a lake is a body Tier B cannot enter — the band over a lake's bed up to its top is not holdable, "
              + "water resting on the top is handed to the basin, water found inside is handed whole");

        // M8b-3b: the settle. Needs no Tier A, because a flooded mine has no Tier A body — the
        // sheets it levels are the ones the hand-off cannot reach.
        _settle = new SheetSettle(_fluid, _settleAfterTicks, _settleShare);
        if (windowed)
        {
            _fluidSurface = new FluidSurface(GetWorld3D(), debugView: _fluidSurfaceMode == "debug");
            SetUpUnderwaterTint();
            FluidSurfaceMesher mesher = _fluid.SurfaceMesher;
            switch (_fluidSurfaceMode)
            {
                case "debug":
                    mesher.DebugView = true;
                    GD.Print("M8d: the Tier B surface is the pre-M8d-1 debug view, one quad per surface cell (--fluidsurface=mesh is the mesh)");
                    break;
                case "nosides":
                    mesher.Sides = false;
                    GD.Print("M8d: the Tier B mesh without its sides (--fluidsurface)");
                    break;
                case "nobottoms":
                    mesher.Bottoms = false;
                    GD.Print("M8d: the Tier B mesh without its bottoms (--fluidsurface)");
                    break;
                case "nomerge":
                    mesher.Merge = false;
                    GD.Print("M8d: the Tier B mesh with its tops unmerged (--fluidsurface)");
                    break;
                case "notops":
                    mesher.Tops = false;
                    GD.Print("M8d: the Tier B mesh without its tops (--fluidsurface)");
                    break;
                case "mesh":
                    break;
                default:
                    GD.Print($"M8d: --fluidsurface={_fluidSurfaceMode} is not mesh, debug, nosides, nobottoms, notops or nomerge — the mesh it is");
                    break;
            }
        }
    }

    /// <summary>
    /// Begins a Tier B tick at 2 Hz, runs it under the frame budget, resumes it next frame if it
    /// did not finish, pours what the relay still owes at each tick boundary, and keeps the debug
    /// surface current.
    /// </summary>
    /// <remarks>
    /// <b>A tick that outlasts its half-second is not skipped, it is finished</b>, and the next one
    /// begins at the next frame after that — so under a roof-open flood the water ticks as fast as
    /// the budget allows and no faster, which is DESIGN's <i>slower than realtime is accuracy</i>
    /// with a number on it. The timer keeps at most one tick of debt across the stall, as before.
    /// The relay pours only at a boundary, so a tick in the game is the same tick the suite
    /// asserts is identical sliced or whole.
    /// </remarks>
    private void UpdateFluid(double delta)
    {
        if (_fluid is null)
        {
            return;
        }

        _secondsSinceStart += delta;
        InjectIfDue();
        BreachIfDue();

        if (_profiler is { Warm: true } && !_fluidWarmSeen)
        {
            _fluidWarmSeen = true;
            _fluidVisitsAtWarm = _fluid.CellVisits;
            _fluidTicksAtWarm = _fluid.Ticks;
            _fluidNearAtWarm = _fluid.NearCellsVisited;
            _fluidFarAtWarm = _fluid.FarCellsVisited;
            _fluidThrottledAtWarm = _fluid.ThrottledTicks;
            _fluidSlicedAtWarm = _fluidSlicedTicks;
            _drawnAtWarm = _drain?.DrawnMillilitres ?? 0;
            _seepedAtWarm = _seepDrain?.SeepedMillilitres ?? 0;
            _seepPassesAtWarm = _seepDrain?.Passes ?? 0;
            _seepMillisSumAtWarm = _seepMillisSum;
            _seepAwakeSamplesAtWarm = _seepAwakeSamples;
            _seepAwakeSumAtWarm = _seepAwakeSum;
            _handedAtWarm = _handoff?.HandedMillilitres ?? 0;
            _settlesAtWarm = _settle?.Settles ?? 0;
            _fluidJobsAtWarm = _fluidJobs;
            _fluidJobsWaitedAtWarm = _fluidJobsWaited;
            _fluidJobWallSumAtWarm = _fluidJobWallSum;
            _fluidJobVisitSumAtWarm = _fluidJobVisitSum;
            _fluidJobWalkSumAtWarm = _fluidJobWalkSum;
            _fluidJobSettleSumAtWarm = _fluidJobSettleSum;
            _fluidRefusalsAtWarm = _fluid.TickRefusals;
            _fluidArrivedRefusalsAtWarm = _fluid.TickRefusalsOfArrivedChunks;
            _fluidRequeuedAtWarm = _fluid.CellsRequeued;
            _bSettleAtWarm = _bSettle;
            _bHandoffAtWarm = _bHandoff;
            _bPourAtWarm = _bPour;
            _bCollapseAtWarm = _bCollapse;
            _bLakeAtWarm = _bLake;
            _bSyncAtWarm = _bSync;
            _bPublishAtWarm = _bPublish;
            _bBeginAtWarm = _bBegin;
            _boundariesAtWarm = _boundaries;
        }

        // Where the viewer is, for the near/far order. Called only when the order is on, so the
        // control arm never reaches the classification path however far the player walks; a
        // changed centre moves no water, it marks the carry for re-classification at the next
        // boundary.
        if (_fluidNearRings > 0)
        {
            _fluid.SetNearField(ViewerChunk(), _fluidNearRings);
        }

        _fluidSinceTick += delta;
        bool begin = !_fluid.GridOwnedByTick && _fluidSinceTick >= FluidTickSeconds && !_fluidJobFaulted;
        long boundaryStarted = Stopwatch.GetTimestamp();
        if (begin)
        {
            // At most one tick of debt after a stall: the remainder is kept, the burst is not.
            _fluidSinceTick = Math.Min(_fluidSinceTick - FluidTickSeconds, FluidTickSeconds);

            // The seam, at the boundary and only there — like the relay, so the tick that follows
            // is the same tick the suite asserts is identical sliced or whole. The settle first,
            // because it lists what it levelled as resting and the hand-off reads that list; it
            // reads the ticks since the last inflow before this boundary pours any, so the tick
            // after a draw is never a quiet one. Then B → A, so what rests on the ground is in the
            // lake before the lake is drawn from.
            long t0 = Stopwatch.GetTimestamp();

            // On the job arm the solve ran at the tail of the last job and was recorded at its
            // collection (slice 3c); the sim-thread arm settles here, as it always has.
            if (!_fluidJobOn)
            {
                _settle?.AtBoundary();
            }

            long t1 = Stopwatch.GetTimestamp();
            _handoff?.Absorb();
            _drain?.Draw();

            // M8c-2: the flush re-scans any chunk a burst of edits left stale, then the seep
            // drain reads the index. Timed apart from the drains it sits beside, because the
            // pass over a dug mine's chunks is the term the plan could not price.
            if (_seepDrain is not null)
            {
                long s0 = Stopwatch.GetTimestamp();
                _portalTracker?.Flush();
                _seepDrain.Draw();
                double seepMillis = Milliseconds(s0, Stopwatch.GetTimestamp());
                _seepMillisSum += seepMillis;
                _seepMillisMax = Math.Max(_seepMillisMax, seepMillis);
                if (_seepDrain.ChunksSeepingLastPass > 0)
                {
                    int awake = _fluid.ActiveCellCount;
                    _seepAwakeSamples++;
                    _seepAwakeSum += awake;
                    _seepAwakeMin = Math.Min(_seepAwakeMin, awake);
                    _seepAwakeMax = Math.Max(_seepAwakeMax, awake);
                }
            }

            long t2 = Stopwatch.GetTimestamp();
            TakePending();
            PourPending();
            long t3 = Stopwatch.GetTimestamp();

            // M9c-1, decision 6: the fall, after the pours and before the field sync, so the
            // tick that follows reads a terrain with the rubble in it and panels with the water
            // where it was returned. Its own bracket on the boundary line, because a fall is a
            // bulk edit through the whole hook chain and would otherwise read as a slow pour.
            SweepCollapse();
            long t3c = Stopwatch.GetTimestamp();

            // M8d-5, decision 6: the lakes as this boundary's passes left them, after the hand-off
            // and the drains have moved their litres and before the field is synced and captured —
            // so the hand-off read the ceiling the tick read, and the tick that follows reads the
            // levels as they stand now. The refresh wakes what a rise submerged.
            if (_lakeCeiling is not null)
            {
                _fluid.ReachableChunks(_fluidReachable);
                _lakeCeiling.Refresh(_fluidReachable, _fluid.WakeCell);
            }

            long t3l = Stopwatch.GetTimestamp();

            // Last, so the pour and the draw are already in and the chunks they wet are covered
            // before the tick reads them. Building here and nowhere else is what makes the terrain
            // constant for the length of a tick, which is what slice 3 will need.
            if (_field is not null)
            {
                if (_lakeCeiling is null)
                {
                    _fluid.ReachableChunks(_fluidReachable);
                }

                _fieldLate.Clear();
                _field.Sync(_fluidReachable, _fieldBakesPerBoundary, _fieldLate);

                // A field that landed late is an arrival to the water: the cells a tick paused
                // for want of it, and the neighbours that rested against it as a wall, wake now,
                // before the next batch is composed (slice 3c).
                foreach (ChunkPos chunk in _fieldLate)
                {
                    _fluid.ChunkArrived(chunk);
                    _fieldLateWakes++;
                }
            }

            long t4 = Stopwatch.GetTimestamp();

            // After the passes, so the pour and the settle are in what the report line and the
            // HUD read; the collection publishes again when the tick lands.
            _fluid.Publish();
            long t5 = Stopwatch.GetTimestamp();

            _boundaries++;
            double settle = Milliseconds(t0, t1), handoff = Milliseconds(t1, t2), pour = Milliseconds(t2, t3);
            double collapse = Milliseconds(t3, t3c), lake = Milliseconds(t3c, t3l), sync = Milliseconds(t3l, t4), publish = Milliseconds(t4, t5);
            _bSettle += settle;
            _bHandoff += handoff;
            _bPour += pour;
            _bCollapse += collapse;
            _bLake += lake;
            _bSync += sync;
            _bPublish += publish;
            _bSettleMax = Math.Max(_bSettleMax, settle);
            _bHandoffMax = Math.Max(_bHandoffMax, handoff);
            _bPourMax = Math.Max(_bPourMax, pour);
            _bCollapseMax = Math.Max(_bCollapseMax, collapse);
            _bLakeMax = Math.Max(_bLakeMax, lake);
            _bSyncMax = Math.Max(_bSyncMax, sync);
            _bPublishMax = Math.Max(_bPublishMax, publish);
        }

        if (begin && _fluidJobOn)
        {
            // Slice 3b: compose here, visit on a worker, collect in OnFluidTickCollected. The
            // captured view is the terrain the worker reads; the live field goes on being edited
            // and synced here without touching it. fluid ms now means this boundary's cost.
            long b0 = Stopwatch.GetTimestamp();
            FluidFieldView view = _field!.Capture();
            IFluidTerrain captured = _lakeCeiling is null
                ? view
                : new LakeBoundedTerrain(view, _lakeCeiling.Capture());
            FluidTickJob job = _fluid.BeginTick(captured, ViewerChunk(), walkSurface: _fluidSurface is not null, settle: _settle?.ForWorker);
            _bBegin += Milliseconds(b0, Stopwatch.GetTimestamp());
            job.Collected = OnFluidTickCollected;
            _fluidJobCts = new System.Threading.CancellationTokenSource();
            _fluidJobSubmittedAt = Stopwatch.GetTimestamp();
            _pipeline!.SubmitDetached(job, _fluidJobCts.Token);

            double boundaryMillis = Milliseconds(boundaryStarted, Stopwatch.GetTimestamp());
            _fluidWorstMillis = Math.Max(_fluidWorstMillis, boundaryMillis);
            if (_profiler is { Warm: true })
            {
                _profiler.FluidTick.Add(boundaryMillis);
            }

            return;
        }

        if (_fluidJobOn)
        {
            // The tick is out, or nothing is due. The surface is installed at collection.
            return;
        }

        if (begin || _fluid.TickInProgress)
        {
            // The sync arm only (the job arm returned above), where owned and in progress are one.
            long before = Stopwatch.GetTimestamp();

            // Bracketed so a miss inside a tick can be told from a miss at a boundary:
            // the first is what slice 3 cannot have, the second is a pour asking about
            // ground no panel has reached yet and is harmless.
            if (_field is not null) { _field.InTick = true; }

            FluidTickResult? done = _fluid.Step(before + _fluidBudgetTicks);
            if (_field is not null) { _field.InTick = false; }

            double millis = Milliseconds(before, Stopwatch.GetTimestamp());

            _fluidFramesThisTick++;
            _fluidWorstMillis = Math.Max(_fluidWorstMillis, millis);
            bool warm = _profiler is { Warm: true };
            if (warm)
            {
                _profiler!.FluidTick.Add(millis);
            }

            if (done is not null)
            {
                if (_fluidFramesThisTick > 1)
                {
                    _fluidSlicedTicks++;
                }

                _fluidLongestTickFrames = Math.Max(_fluidLongestTickFrames, _fluidFramesThisTick);
                if (warm)
                {
                    _fluidLongestTickFramesInWindow =
                        Math.Max(_fluidLongestTickFramesInWindow, _fluidFramesThisTick);
                }

                _fluidFramesThisTick = 0;
            }
        }

        _fluidSurface?.Update(delta, _fluid);
    }

    // M8d-2, decision 6: a full-screen wash when the eye is in the water. Its own layer rather
    // than the HUD's, because the HUD is skipped during a profile and a screenshot of the tint is
    // taken with one. The cheapest "you are in it" there is, and the wading precursor.
    private CanvasLayer? _underwaterLayer;
    private ColorRect? _underwaterTint;
    private bool _underwater;

    private void SetUpUnderwaterTint()
    {
        _underwaterLayer = new CanvasLayer { Name = "Underwater", Layer = 5 };
        _underwaterTint = new ColorRect
        {
            Name = "UnderwaterTint",
            Color = new Color(0.10f, 0.28f, 0.42f, 0.55f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        _underwaterTint.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _underwaterLayer.AddChild(_underwaterTint);
        AddChild(_underwaterLayer);
    }

    /// <summary>
    /// Every frame: the lerp's phase since the mesh landed, and whether the eye is in the water.
    /// </summary>
    private void UpdateFluidLook()
    {
        if (_fluidSurface is null || _camera is null)
        {
            return;
        }

        // 0 → 1 over the tick's half second, then held: a tick that takes longer than its period
        // leaves the surface where it arrived rather than moving it twice.
        double since = (Time.GetTicksMsec() - _fluidSurface.InstalledAtMsec) / 1000.0;
        _fluidSurface.SetPhase((float)(since / FluidTickSeconds));

        bool under = _fluidSurface.IsUnderwater(_camera.GlobalPosition);
        if (under != _underwater)
        {
            _underwater = under;
            if (_underwaterTint is not null)
            {
                _underwaterTint.Visible = under;
            }
        }
    }

    /// <summary>The job's result, on the sim thread, from the pipeline's result pass (slice 3b).</summary>
    /// <remarks>
    /// The tick is already collected — the deferred list drained, the unvisited cells of a tick
    /// that did not complete returned to the queue, the numbers published — by the time this runs;
    /// what is left is the game's side: the surface, the figures, and a fault.
    /// </remarks>
    private void OnFluidTickCollected(FluidTickJob job, JobOutcome outcome, Exception? error)
    {
        _fluidJobCts?.Dispose();
        _fluidJobCts = null;

        double wall = Milliseconds(_fluidJobSubmittedAt, Stopwatch.GetTimestamp());
        _fluidJobs++;
        _fluidJobWallSum += wall;
        _fluidJobWallMax = Math.Max(_fluidJobWallMax, wall);
        _fluidJobVisitSum += job.VisitMillis;
        _fluidJobWalkSum += job.WalkMillis;
        if (wall > FluidTickSeconds * 1000.0)
        {
            _fluidJobsWaited++;
        }

        if (job.Settle is { } solved)
        {
            _settle?.Record(solved, job.SettleMillis);
            _fluidJobSettleSum += job.SettleMillis;
        }

        if (job.Surface is not null && _fluidSurface is not null)
        {
            _fluidSurface.InstallFrom(job.Surface, job.WalkMillis);
        }

        if (outcome == JobOutcome.Faulted)
        {
            // Frozen rather than retried or handed back to the sim thread: a fault is a bug and
            // deterministic, so a retry is the same exception at 2 Hz and a fallback is the same
            // exception on the main thread, which takes the game with it. A frozen lake beside a
            // reported exception is the pipeline's visible-hole rule.
            _fluidJobFaulted = true;
            GD.PushError(
                $"M8b: the fluid tick faulted on a worker after {job.Batch:N0} cell(s) were composed, "
                + $"{job.Requeued:N0} returned to the queue — the water is frozen for this session: {error}");
        }
        else if (outcome == JobOutcome.Dropped && _fluidJobCancelledForQuit is false)
        {
            GD.PushWarning($"M8b: the fluid tick was dropped outside a quit; {job.Requeued:N0} cell(s) returned to the queue");
        }
    }

    private bool _fluidJobCancelledForQuit;

    /// <summary><c>--injectlitres</c>: the relay from the command line, so a headless run can flood something.</summary>
    /// <remarks>
    /// Fires when the measured window opens, so the whole pour is inside it, or three seconds in
    /// when there is no profiler. At the player rather than the crosshair, because a headless
    /// camera looks along the flight path and "the voxel in front of what you are looking at" is
    /// sky.
    /// </remarks>
    private void InjectIfDue()
    {
        if (_injected || _injectLitres <= 0 || _fluid is null)
        {
            return;
        }

        bool due = _profiler is null ? _secondsSinceStart >= 3.0 : _profiler.Warm;
        if (!due)
        {
            return;
        }

        // On the ground under the player rather than at the player: a flying camera is four
        // metres up, the automaton falls one cell a tick, and a ball in mid-air drains from its
        // bottom layer at one layer a tick — the first run delivered 15,871 of 50,000 L in its
        // window, measuring the fall rather than the flood. The ball's centre sits one radius
        // above the surface so the whole of it is open air.
        VoxelPos here = PlayerVoxel();
        int surface = _generator!.SurfaceHeightAt(here.X, here.Z);
        long millilitres = _injectLitres * FluidConstants.MillilitresPerLitre;
        int radius = RadiusVoxelsFor(millilitres);

        _injected = true;
        _relayAt = new VoxelPos(here.X, surface + 1 + radius, here.Z);
        _relayRadiusVoxels = radius;
        _relayPendingMillilitres += millilitres;
        GD.Print(
            $"M8b: injecting {_injectLitres:N0} L at {_relayAt}, a ball of radius {radius} voxels on "
            + "the ground under the player (--injectlitres)");
    }

    /// <summary>
    /// <c>--fillbasin</c> and <c>--breach</c>: a lake under the player, and a hole under the lake,
    /// when the measured window opens.
    /// </summary>
    /// <remarks>
    /// <b>The hardest part of testing M8 is still getting water to move at all.</b> A breach under
    /// a dry basin draws nothing and proves nothing, so the Tier A relay comes with it: litres into
    /// whichever basin the player stands over, reported as <c>injected</c> on the water line for
    /// the reason the Tier B relay is. The shaft is square, that many voxels a side, from the seed
    /// surface down, through the pipeline's own edit path so the tracker and the automaton are
    /// told the way a swing tells them. Waits for the window to be installed, because a basin is
    /// an index into one.
    /// </remarks>
    private void BreachIfDue()
    {
        if (_breached || (_fillBasinLitres <= 0 && _breachVoxels <= 0) || _water is null || _pipeline is null)
        {
            return;
        }

        bool due = _profiler is null ? _secondsSinceStart >= 3.0 : _profiler.Warm;
        if (!due || _water.Window is null)
        {
            return;
        }

        _breached = true;
        VoxelPos player = PlayerVoxel();
        (int sampleX, int sampleZ) = _water.SampleOf(player.X, player.Z);
        int basin = _water.BasinAtSample(sampleX, sampleZ);

        // The basin under the player, or the nearest one when the player's sample drains off the
        // window — a slope, which the spawn usually is. Either way the breach goes at the basin's
        // PIT, not under the player: a pour covers the pit first and a hillside sample last, and
        // the first run bit a shaft under a player standing 200,000 L above the lake's level
        // ("1 wet, 0 covered"). A real breach drains a lake from its bed, so that is the shape.
        if (basin == Watershed.Outlet)
        {
            long nearest = long.MaxValue;
            foreach (Basin candidate in _water.Window.Watershed.Basins)
            {
                long dx = candidate.PitX - player.X;
                long dz = candidate.PitZ - player.Z;
                long distance = (dx * dx) + (dz * dz);
                if (distance < nearest)
                {
                    nearest = distance;
                    basin = candidate.Index;
                }
            }

            if (basin == Watershed.Outlet)
            {
                GD.Print("M8b: the window has no basin at all, so --fillbasin and --breach have nowhere to go");
                return;
            }

            GD.Print(
                $"M8b: the player stands over no basin at sample ({sampleX}, {sampleZ}); using basin "
                + $"{basin}, whose pit is {Math.Sqrt(nearest) * VoxelConstants.VoxelSize:F0} m away");
        }

        Basin target = _water.Window.Watershed.Basins[basin];
        var here = new VoxelPos(
            target.PitX + (HeightField.DefaultCellVoxels / 2),
            player.Y,
            target.PitZ + (HeightField.DefaultCellVoxels / 2));
        _breachBasin = basin;
        _breachSample = _water.SampleOf(here.X, here.Z);

        if (_fillBasinLitres > 0)
        {
            long spilled = _water.Inject(basin, _fillBasinLitres);
            GD.Print(
                $"M8b: poured {_fillBasinLitres:N0} L into basin {basin}, {spilled:N0} L spilled, "
                + $"level now {_water.LevelAt(basin):F2} against a floor at {_water.Window.Watershed.Basins[basin].PitHeight} "
                + "(--fillbasin)");
        }

        if (_breachVoxels > 0)
        {
            int surface = _generator!.SurfaceHeightAt(here.X, here.Z);
            int depth = Math.Max(_breachVoxels, 8);
            int half = _breachVoxels / 2;
            int removed = 0;
            for (int y = surface; y > surface - depth; y--)
            {
                for (int dz = -half; dz < _breachVoxels - half; dz++)
                {
                    for (int dx = -half; dx < _breachVoxels - half; dx++)
                    {
                        if (_pipeline.SetVoxel(new VoxelPos(here.X + dx, y, here.Z + dz), MaterialIds.AirNumericId))
                        {
                            removed++;
                        }
                    }
                }
            }

            GD.Print(
                $"M8b: bit a {_breachVoxels}x{_breachVoxels} shaft {depth} deep under the player at "
                + $"({here.X}, {here.Z}), surface {surface}: {removed:N0} voxel(s) removed, "
                + $"{_portals?.Count ?? 0} portal(s) filed (--breach)");
        }
    }

    /// <summary>
    /// Voxels of radius a ball needs to hold this much, with a ring of slack for the cells that
    /// turn out to be rock.
    /// </summary>
    /// <remarks>
    /// <b>The pour is sized to the litres so that it lands in one tick, and the reason is what the
    /// first two runs measured instead of a flood.</b> A fixed one-metre ball is a hose: the
    /// automaton falls one cell a tick and spreads diffusively, so a ball in mid-air drains from its
    /// bottom layer at one layer a tick (15,871 of 50,000 L delivered in a 15 s window) and a ball on
    /// the ground drains only by its bottom layer spreading sideways (11,837 L). Either run measures
    /// the hose. A ball big enough for the whole pour puts the water down at once, and what the run
    /// then measures is the automaton spreading it, which is the thing under test.
    /// </remarks>
    private static int RadiusVoxelsFor(long millilitres)
    {
        double cells = millilitres / (double)FluidConstants.CellCapacityMillilitres;
        double radius = Math.Cbrt(3.0 * cells / (4.0 * Math.PI));
        return Math.Max(1, (int)Math.Ceiling(radius) + 1);
    }

    /// <summary>Alt+R's take, at the boundary it was queued for.</summary>
    private void TakePending()
    {
        if (_fluid is null || _relayTakeAt is not { } at)
        {
            return;
        }

        _relayTakeAt = null;
        long taken = TakeWaterAround(at);
        GD.Print($"M8b: relay took {taken / (double)FluidConstants.MillilitresPerLitre:N0} L around {at}");
    }

    /// <summary>Offers what the relay still owes to every cell in its ball, as fast as they take it.</summary>
    private void PourPending()
    {
        if (_fluid is null || _relayPendingMillilitres <= 0)
        {
            return;
        }

        int r = _relayRadiusVoxels;
        int r2 = r * r;

        // A fixed order, so the same pour lands the same way twice.
        for (int dy = r; dy >= -r; dy--)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    if ((dx * dx) + (dy * dy) + (dz * dz) > r2)
                    {
                        continue;
                    }

                    _relayPendingMillilitres =
                        _fluid.Add(_relayAt.Offset(dx, dy, dz), _relayPendingMillilitres);
                    if (_relayPendingMillilitres == 0)
                    {
                        return;
                    }
                }
            }
        }
    }

    /// <summary>
    /// The debug relay: R puts <see cref="RelayLitres"/> down at the crosshair as a ball sized to
    /// hold them, Alt+R takes back everything within <see cref="TakeRadiusMetres"/> of it. M8-PLAN's provocation seam, built as a bind and not as
    /// content.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>No material, no JSON, no ID</b> — the entangled miner's rule, second outing, and M8-PLAN
    /// says why: the moment <c>overburden:relay_pump</c> exists as a material the name is permanent
    /// whether or not the design behind it is finished. The endgame machine is M10's and reuses the
    /// arithmetic when it is designed.
    /// </para>
    /// <para>
    /// <b>It creates litres from nothing, which is what the milestone's oracle forbids, so it is a
    /// reported term rather than a hidden one</b>: injected and removed are printed beside held, and
    /// the identity is <c>held == injected − removed − displaced</c>. The pour is a source offered
    /// again every tick until it fits — the bench's rule — and the undelivered remainder is printed
    /// too.
    /// </para>
    /// </remarks>
    private void RelayWater()
    {
        if (_fluid is null || _mining is null || _camera is null || !_camera.MouseCaptured)
        {
            return;
        }

        bool pressed = Input.IsKeyPressed(Key.R);
        bool edge = pressed && !_relayKeyHeld;
        _relayKeyHeld = pressed;

        if (!edge || !_mining.HasTarget)
        {
            return;
        }

        VoxelPos at = _mining.Hit.Previous;
        if (Input.IsKeyPressed(Key.Alt))
        {
            // Cancels the pour at once and takes at the boundary, where the grid is ours.
            _relayPendingMillilitres = 0;
            _relayTakeAt = at;
            GD.Print($"M8b: relay will take back everything within {TakeRadiusMetres:F0} m of {at} at the boundary");
            return;
        }

        _relayAt = at;
        _relayPendingMillilitres += RelayLitres * FluidConstants.MillilitresPerLitre;
        _relayRadiusVoxels = RadiusVoxelsFor(_relayPendingMillilitres);
        GD.Print(
            $"M8b: relay pouring {RelayLitres:N0} L at {at} in a ball of radius {_relayRadiusVoxels} voxels; "
            + $"{_relayPendingMillilitres / (double)FluidConstants.MillilitresPerLitre:N0} L pending");
    }

    private long TakeWaterAround(VoxelPos centre)
    {
        if (_fluid is null)
        {
            return 0;
        }

        int r = (int)MathF.Ceiling(TakeRadiusMetres * VoxelConstants.VoxelsPerMetre);
        int r2 = r * r;

        long taken = 0;
        for (int dy = -r; dy <= r; dy++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    if ((dx * dx) + (dy * dy) + (dz * dz) > r2)
                    {
                        continue;
                    }

                    taken += _fluid.Remove(
                        centre.Offset(dx, dy, dz), FluidConstants.CellCapacityMillilitres);
                }
            }
        }

        return taken;
    }

    /// <summary>The B → A seam from both ends: Tier A's received equals Tier B's handed, whole litres, with the remainder pending.</summary>
    private string HandoffIdentity()
    {
        if (_handoff is null || _water is null)
        {
            return "no hand-off";
        }

        bool exact = (_water.ReceivedLitres * FluidConstants.MillilitresPerLitre) + _handoff.PendingMillilitres
            == _fluid!.HandedMillilitres;
        return exact ? "exact" : "*** NO ***";
    }

    /// <summary>The Tier B line the periodic report prints.</summary>
    /// <remarks>
    /// <b>Every term of the identity, and the undelivered remainder beside them.</b> A held figure
    /// on its own cannot say whether the relay lied, and "0 L moving" cannot say whether nothing
    /// was poured or everything drained into an unloaded chunk; the terms beside it can.
    /// </remarks>
    private string FluidSummary()
    {
        FluidSimulation f = _fluid!;
        const double litre = FluidConstants.MillilitresPerLitre;

        string surface = _fluidSurface is null
            ? " | no surface drawn (headless or --nowater)"
            : $" | surface {_fluidSurface.QuadCount:N0} quad(s) ({_fluidSurface.Tops:N0} top(s) over {_fluidSurface.CellsWalked:N0} surface cell(s), "
              + $"{_fluidSurface.Sides:N0} side(s), {_fluidSurface.Bottoms:N0} bottom(s), {_fluidSurface.LastMeshBytes / 1024.0:N0} KiB), "
              + $"{_fluidSurface.LastRebuildMillis:F2} ms to rebuild ({_fluidSurface.LastWalkMillis:F2} mesh + {_fluidSurface.LastInstallMillis:F2} install)";

        string settle = _settle is null
            ? string.Empty
            : $" | settle {_settle.Settles:N0} pass(es) after {_settle.SettleAfterTicks} quiet tick(s), "
              + $"share 1 in {_settle.SettleShare}: "
              + $"{_settle.SheetsLevelled:N0} levelled / {_settle.SheetsPacked:N0} packed / "
              + $"{_settle.SheetsSkipped:N0} left / {_settle.SheetsUnderShare:N0} left under share / "
              + $"{_settle.SheetsNoisy:N0} still being fed / "
              + $"{_settle.SheetsStepped:N0} levelled for a step, {_settle.CellsVisited:N0} cells visited, "
              + $"{_settle.CellsWritten:N0} written, "
              + $"largest sheet {_settle.LargestSheet:N0}, longest pass {_settle.LongestMillis:F1} ms";

        long drawn = _drain?.DrawnMillilitres ?? 0;
        long seeped = _seepDrain?.SeepedMillilitres ?? 0;
        FluidPublished p = f.Published;
        string seep = _seepDrain is null
            ? string.Empty
            : $"seeped {seeped / litre:N0} L ({_seepDrain.Mode.ToString().ToLowerInvariant()}) through {_seeps!.Count:N0} face(s) in {_seeps.ChunkCount:N0} chunk(s), "
              + $"{_seepDrain.ChunksSeepingLastPass:N0} seeping / {_seepDrain.ChunksFullLastPass:N0} full on the last pass, "
              + $"seep pass worst {_seepMillisMax:F1} ms, awake at seeping boundaries "
              + $"{(_seepAwakeSamples > 0 ? $"{_seepAwakeMin:N0}/{_seepAwakeSum / _seepAwakeSamples:N0}/{_seepAwakeMax:N0}" : "—")}; ";
        string job = _fluidJobOn
            ? $"{f.JobTicks:N0} job tick(s), {f.TickRefusals:N0} refusal(s) inside a tick of which {f.TickRefusalsOfArrivedChunks:N0} for a chunk that had arrived, "
              + $"{f.JobTicksNotCompleted:N0} not completed with {f.CellsRequeued:N0} cell(s) requeued, "
              + $"worker wall mean {(_fluidJobs > 0 ? _fluidJobWallSum / _fluidJobs : 0):F1} ms max {_fluidJobWallMax:F1} ms, "
              + $"{_fluidJobsWaited:N0} boundary(ies) waited; "
            : string.Empty;
        return $"{p.TotalMillilitres / litre:N0} L moving in {p.HeldCells:N0} cell(s) over "
               + $"{p.Panels} panel(s) ({p.PanelBytes / 1024.0 / 1024.0:F1} MiB) at tick {p.Ticks:N0}; "
               + $"injected {(p.Added - drawn - seeped) / litre:N0} L, drawn {drawn / litre:N0} L, "
               + $"loaded {p.Loaded / litre:N0} L from {f.RestoredPanels:N0} panel(s), "
               + $"evicted {p.Evicted / litre:N0} L with {f.EvictedPanels:N0} panel(s), "
               + $"removed {p.Removed / litre:N0} L, displaced {p.Displaced / litre:N0} L, "
               + $"handed {p.Handed / litre:N0} L to Tier A ({_handoff?.SubmergedHanded ?? 0:N0} cell(s) of it found inside a lake"
               + (_lakeCeiling is null ? string.Empty : $"; lake {_lakeCeiling.ChunkColumns:N0} column(s), {_lakeCeiling.Rebuilt:N0} rebuilt, {_lakeCeiling.WokenSubmerged:N0} woken") + ") — "
               + $"held == injected + drawn + seeped + loaded − removed − displaced − handed − evicted: {(p.IdentityExact ? "exact" : "*** NO ***")}, "
               + $"received == handed: {HandoffIdentity()}, "
               + $"lifted {p.Lifted / litre:N0} L == returned {p.Returned / litre:N0} L by falls: {(p.Lifted == p.Returned ? "exact" : "*** NO ***")}; "
               + seep
               + job
               + $"{_relayPendingMillilitres / litre:N0} L still to pour; "
               + $"paused {p.Paused:N0} cell(s) in {f.PausedChunkCount:N0} chunk(s), "
               + $"{f.ResumedCells:N0} resumed, {f.FaceWakes:N0} face wake(s), "
               + $"{f.LongFalls:N0} long fall(s) over {f.CellsFallen:N0} cell(s) at up to {f.FallCells}, "
               + $"evicting {f.EvictingPanelCount:N0} panel(s), {f.ReclaimedPanels:N0} reclaimed, "
               + $"{f.Deferrals:N0} edit(s) deferred to a boundary, "
               + $"{_pipeline?.FluidArrivalsParked ?? 0:N0} arrival(s) parked | "
               + $"{f.Ticks:N0} tick(s) at {1.0 / FluidTickSeconds:F0} Hz, {f.ActiveCellCount:N0} active "
               + (_fluidNearRings > 0
                   ? $"(last tick {f.NearAdmitted:N0} near + {f.FarAdmitted:N0} far, "
                     + $"{f.FarBacklog:N0} deferred), "
                   : string.Empty)
               + $"peak {f.PeakActiveCells:N0}, {f.CellVisits:N0} visits; budget "
               + $"{_fluidBudget.TotalMilliseconds:F1} ms a frame, worst frame {_fluidWorstMillis:F2} ms, "
               + $"{_fluidSlicedTicks:N0} tick(s) over more than one frame, longest {_fluidLongestTickFrames:N0}"
               + (_boundaries > 0 ? $" | boundary worst: {BoundaryWorst()}" : string.Empty)
               + settle
               + surface;
    }

    /// <summary>The worst single pass of each kind over the run — the frame a boundary can cost.</summary>
    private string BoundaryWorst() =>
        $"settle {_bSettleMax:F1} ms, hand-off and drain {_bHandoffMax:F1} ms, pour {_bPourMax:F1} ms, "
        + $"collapse {_bCollapseMax:F1} ms, lake {_bLakeMax:F1} ms, field sync {_bSyncMax:F1} ms, publish {_bPublishMax:F1} ms";

    /// <summary>The boundary's cost by pass, mean per boundary over the window.</summary>
    private string BoundaryLine()
    {
        int n = _boundaries - _boundariesAtWarm;
        if (n == 0)
        {
            return "boundary  (none in the window)\n";
        }

        return $"boundary  {n:N0} in the window, mean per boundary: settle {(_bSettle - _bSettleAtWarm) / n:F2} ms, "
               + $"hand-off and drain {(_bHandoff - _bHandoffAtWarm) / n:F2} ms, pour {(_bPour - _bPourAtWarm) / n:F2} ms, "
               + $"collapse {(_bCollapse - _bCollapseAtWarm) / n:F2} ms, "
               + $"lake {(_bLake - _bLakeAtWarm) / n:F2} ms, "
               + $"field sync {(_bSync - _bSyncAtWarm) / n:F2} ms, publish {(_bPublish - _bPublishAtWarm) / n:F2} ms, "
               + $"capture and compose {(_bBegin - _bBeginAtWarm) / n:F2} ms; worst over the run: "
               + BoundaryWorst() + "\n";
    }

    /// <summary>What the breach's pit sample maps to now, and what that holds: the denominator behind "wet" and "covered".</summary>
    private string BreachBasinNow()
    {
        if (_breachBasin == Watershed.Outlet || _water is null)
        {
            return string.Empty;
        }

        int basin = _water.BasinAtSample(_breachSample.X, _breachSample.Z);
        long stored = basin == Watershed.Outlet || _water.Water is null ? 0 : _water.Water.StoredIn(basin);
        string level = basin == Watershed.Outlet ? "none" : _water.LevelAt(basin)?.ToString("F2") ?? "none";
        return $"; the breach's pit sample {_breachSample} maps to basin {basin} now (was {_breachBasin}), "
               + $"holding {stored:N0} L at level {level}";
    }

    /// <summary>The Tier B half of the profile, with the measured window's own denominators.</summary>
    private string FluidLines()
    {
        if (_fluid is null)
        {
            return "fluid     no fluid simulation (--nowater or no world)\n";
        }

        FluidSimulation f = _fluid;
        const double litre = FluidConstants.MillilitresPerLitre;
        double seconds = Math.Max(_profiler!.MeasuredSeconds, 0.001);
        long visits = f.CellVisits - _fluidVisitsAtWarm;
        long ticks = f.Ticks - _fluidTicksAtWarm;

        // Per frame, not per tick: what a frame paid, which is the number the budget bounds. The
        // tick's own span is on the fluidtick line as frames.
        string tick = _profiler.FluidTick.Count > 0
            ? _profiler.FluidTick.Line("fluid ms")
              + $"  per frame the tick ran, budget {_fluidBudget.TotalMilliseconds:F1} ms\n"
            : "fluid ms  (no tick ran in the window)\n";
        int sliced = _fluidSlicedTicks - _fluidSlicedAtWarm;

        string field = _field is null
            ? "fluidfield off (--fluidfield=0)\n"
            : $"fluidfield {_field.Chunks:N0} chunk(s), {_field.Bytes / 1024.0 / 1024.0:F2} MiB; "
              + $"{_field.Builds:N0} built, {_field.Forgets:N0} forgotten, "
              + $"{_field.Invalidations:N0} edited, {_field.Declined:N0} declined, {_field.Capped:N0} past the cap of "
              + $"{_fieldBakesPerBoundary} a boundary, {_field.Waiting:N0} still owed, {_fieldLateWakes:N0} late and woken; {_field.Misses:N0} miss(es) of which "
              + $"{_field.TickMisses:N0} inside a tick — a boundary miss is a pour asking about "
              + "ground no panel has reached yet and is harmless; a tick miss is what "
              + "slice 3 cannot have\n";

        string surface = _fluidSurface is null
            ? "fluidsurf not drawn (headless or --nowater)\n"
            : $"fluidsurf {_fluidSurface.QuadCount:N0} quad(s) — {_fluidSurface.Tops:N0} top(s) over {_fluidSurface.CellsWalked:N0} surface cell(s), "
              + $"{_fluidSurface.Sides:N0} side(s), {_fluidSurface.Bottoms:N0} bottom(s), {_fluidSurface.LastMeshBytes / 1024.0:N0} KiB — "
              + $"{_fluidSurface.LastRebuildMillis:F2} ms ({_fluidSurface.LastWalkMillis:F2} mesh + {_fluidSurface.LastInstallMillis:F2} install) "
              + $"to rebuild, {_fluidSurface.Rebuilds} rebuild(s)\n";

        long drawn = _drain?.DrawnMillilitres ?? 0;
        long seeped = _seepDrain?.SeepedMillilitres ?? 0;
        long drawnA = (_water?.DrawnLitres ?? 0) * FluidConstants.MillilitresPerLitre;
        long seepedA = (_water?.SeepedLitres ?? 0) * FluidConstants.MillilitresPerLitre;
        string breach = _drain is null || _portals is null
            ? "breach    no portal index (--nowater)\n"
            : $"breach    {_portals.Count:N0} portal(s) under {_portals.SampleCount:N0} sample(s), "
              + $"{_drain.WetSamplesLastPass:N0} wet and {_drain.CoveredPortalsLastPass:N0} covered on the "
              + $"last pass; drawn {(drawn - _drawnAtWarm) / litre:N0} L in the window, {drawn / litre:N0} L in "
              + $"all over {_drain.Draws:N0} draw(s) — Tier A drawn == Tier B drawn: "
              + $"{(drawnA == drawn ? "exact" : "*** NO ***")}"
              + BreachBasinNow()
              + "\n";

        int seepPasses = (int)((_seepDrain?.Passes ?? 0) - _seepPassesAtWarm);
        int awakeSamples = _seepAwakeSamples - _seepAwakeSamplesAtWarm;
        string seep = _seepDrain is null || _seeps is null
            ? $"seep      no seepage ({(_water is null ? "--nowater" : "--seepscale=0")})\n"
            : $"seep      {_seeps.Count:N0} face(s) in {_seeps.ChunkCount:N0} chunk(s) at scale {_seepDrain.SeepageScale}, {_seepDrain.Mode.ToString().ToLowerInvariant()}; "
              + $"{_seepDrain.ChunksSeepingLastPass:N0} seeping / {_seepDrain.ChunksDeliveredLastPass:N0} delivered / "
              + $"{_seepDrain.ChunksFullLastPass:N0} skipped full on the last pass ({_seepDrain.FullChunks:N0} marked full); "
              + $"seeped {(seeped - _seepedAtWarm) / litre:N0} L in the window, {seeped / litre:N0} L in all over "
              + $"{_seepDrain.Deliveries:N0} deliver(ies); {_portalTracker?.Rescans ?? 0:N0} burst rescan(s); "
              + $"pass mean {(seepPasses > 0 ? (_seepMillisSum - _seepMillisSumAtWarm) / seepPasses : 0):F2} ms max {_seepMillisMax:F2} ms; "
              + $"awake at seeping boundaries min/mean/max "
              + $"{(awakeSamples > 0 ? $"{_seepAwakeMin:N0}/{(_seepAwakeSum - _seepAwakeSumAtWarm) / awakeSamples:N0}/{_seepAwakeMax:N0} over {awakeSamples:N0}" : "— (none seeped)")} "
              + $"— Tier A seeped == Tier B seeped: {(seepedA == seeped ? "exact" : "*** NO ***")}\n";

        long handed = f.HandedMillilitres;
        string handoff = _handoff is null
            ? "handoff   no hand-off (--nowater)\n"
            : $"handoff   {_handoff.RestedLastPass:N0} rested and {_handoff.OnGroundLastPass:N0} on the ground on the "
              + $"last pass ({_handoff.SubmergedLastPass:N0} inside a lake and handed whole, {_handoff.SubmergedHanded:N0} in all); "
              + $"handed {(handed - _handedAtWarm) / litre:N0} L in the window, {handed / litre:N0} L in "
              + $"all over {_handoff.Handoffs:N0} hand-off(s), {_handoff.PendingMillilitres:N0} mL pending in "
              + $"{_handoff.PendingBasins:N0} basin(s) — Tier A received == Tier B handed: {HandoffIdentity()}\n";

        string settle = _settle is null
            ? "settle    no sheet solve (--nowater)\n"
            : $"settle    {_settle.Settles - _settlesAtWarm:N0} pass(es) in the window, {_settle.Settles:N0} in all, "
              + $"after {_settle.SettleAfterTicks} quiet tick(s), share 1 in {_settle.SettleShare}; "
              + $"{_settle.SheetsLevelled:N0} levelled / "
              + $"{_settle.SheetsPacked:N0} packed / {_settle.SheetsSkipped:N0} left alone / "
              + $"{_settle.SheetsUnderShare:N0} left under share / {_settle.SheetsNoisy:N0} still being fed / "
              + $"{_settle.SheetsStepped:N0} levelled for a step, "
              + $"{_settle.CellsVisited:N0} cells visited, "
              + $"{_settle.CellsWritten:N0} written, {_settle.CellsWoken:N0} woken, largest sheet "
              + $"{_settle.LargestSheet:N0} cells, longest pass {_settle.LongestMillis:F2} ms, "
              + $"{_settle.TotalMillis:F1} ms in all\n";

        FluidPublished p = f.Published;
        int jobs = _fluidJobs - _fluidJobsAtWarm;
        string jobLine = !_fluidJobOn
            ? "fluidjob  off (--fluidjob=0): the tick ran on the sim thread\n"
            : jobs == 0
                ? "fluidjob  on, but no tick was collected in the window\n"
                : $"fluidjob  {jobs:N0} tick(s) collected in the window ({jobs / seconds:F2}/s against 2 nominal); "
                  + $"wall mean {(_fluidJobWallSum - _fluidJobWallSumAtWarm) / jobs:F1} ms max {_fluidJobWallMax:F1} ms "
                  + $"(worker visits mean {(_fluidJobVisitSum - _fluidJobVisitSumAtWarm) / jobs:F1} ms, "
                  + $"solve mean {(_fluidJobSettleSum - _fluidJobSettleSumAtWarm) / jobs:F2} ms, "
                  + $"walk mean {(_fluidJobWalkSum - _fluidJobWalkSumAtWarm) / jobs:F2} ms); "
                  + $"{_fluidJobsWaited - _fluidJobsWaitedAtWarm:N0} boundary(ies) waited for a slow tick; "
                  + $"{f.TickRefusals - _fluidRefusalsAtWarm:N0} refusal(s) inside a tick, of which {f.TickRefusalsOfArrivedChunks - _fluidArrivedRefusalsAtWarm:N0} for a chunk that had arrived by collection — decision 7's cost, the rest is the live answer; "
                  + $"{f.CellsRequeued - _fluidRequeuedAtWarm:N0} cell(s) requeued by ticks that did not complete\n"
                  + BoundaryLine();

        return $"fluid     {p.TotalMillilitres / litre:N0} L moving in {p.HeldCells:N0} cell(s) "
               + $"over {p.Panels} panel(s) ({p.PanelBytes / 1024.0 / 1024.0:F1} MiB); "
               + $"injected {(p.Added - drawn - seeped) / litre:N0} L, drawn {drawn / litre:N0} L, seeped {seeped / litre:N0} L, "
               + $"loaded {p.Loaded / litre:N0} L from {f.RestoredPanels:N0} panel(s), "
               + $"evicted {p.Evicted / litre:N0} L with {f.EvictedPanels:N0} panel(s), "
               + $"removed {p.Removed / litre:N0} L, displaced {p.Displaced / litre:N0} L, "
               + $"handed {p.Handed / litre:N0} L — "
               + $"held == injected + drawn + seeped + loaded − removed − displaced − handed − evicted: {(p.IdentityExact ? "exact" : "*** NO ***")}; "
               + $"lifted {p.Lifted / litre:N0} L == returned {p.Returned / litre:N0} L by falls: {(p.Lifted == p.Returned ? "exact" : "*** NO ***")}; "
               + $"{_relayPendingMillilitres / litre:N0} L still to pour; "
               + $"paused {p.Paused:N0} cell(s) in {f.PausedChunkCount:N0} chunk(s), "
               + $"{f.ResumedCells:N0} resumed, {f.FaceWakes:N0} face wake(s), "
               + $"{f.LongFalls:N0} long fall(s) over {f.CellsFallen:N0} cell(s) at up to {f.FallCells} (--fallcells), "
               + $"evicting {f.EvictingPanelCount:N0} panel(s), {f.ReclaimedPanels:N0} reclaimed\n"
               + jobLine
               + breach
               + seep
               + handoff
               + settle
               + $"fluidtick {ticks:N0} tick(s) in the window at {1.0 / FluidTickSeconds:F0} Hz, "
               + $"{visits:N0} visits ({visits / seconds:N0}/s against the ~520,000/s the light "
               + $"budget licenses), peak active {f.PeakActiveCells:N0}, {f.ActiveCellCount:N0} active at the end; "
               + $"{sliced:N0} tick(s) took more than one frame, longest {_fluidLongestTickFramesInWindow:N0} frame(s)\n"
               + $"fluidnear {(_fluidNearRings > 0 ? $"{_fluidNearRings} ring(s)" : "off")}, "
               + $"{f.NearCellsVisited - _fluidNearAtWarm:N0} near visit(s) against "
               + $"{f.FarCellsVisited - _fluidFarAtWarm:N0} far, {f.FarBacklog:N0} deferred at the end, "
               + $"{f.ThrottledTicks - _fluidThrottledAtWarm:N0} tick(s) deferred something\n"
               + tick
               + field
               + surface;
    }

    /// <summary>The range the wet term covered while measuring, so a zero has a denominator.</summary>
    private float _wettestSeen;
    private float _driestSeen = 1f;
    private int _weatherSeen;

    /// <summary>
    /// The mining half of the profile: what a held button actually cost.
    /// </summary>
    /// <remarks>
    /// Per swing rather than per second, because a swing is the unit the player experiences
    /// and the cooldown makes the per-second figure a statement about the cooldown. The clone
    /// rate is here because M3-PLAN guessed at it — "roughly one 2 KB clone per chunk per
    /// edit-and-remesh cycle" — and said M3d would settle it.
    /// </remarks>
    private string MiningLines()
    {
        if (_mining is null || _mining.Swings == 0)
        {
            return string.Empty;
        }

        double swings = _mining.Swings;

        return
            $"{_profiler!.Swing.Line("swing ms")}   (raycast + carve, sim thread)\n" +
            // The rate printed beside the count, because it is the thing a cooldown change is
            // trying to move and nothing here has ever said it out loud — every previous raise
            // was checked by dividing the swing count by the window in a worklog entry, which is
            // where the 10% denominator error above went unnoticed for six milestones.
            $"mining    {_mining.Swings} swings ({_mining.Swings / Math.Max(_profiler.RunSeconds, 0.001):F1}/s), " +
            $"{_mining.VoxelsRemoved / swings:F0} removed / " +
            $"{_mining.VoxelsRefused / swings:F0} too hard / " +
            $"{_mining.VoxelsAlreadyAir / swings:F0} already air per swing" +
            // A gate that never fired is not a gate that passed. VoxelsRefused' own remarks say
            // so; until now the report did not.
            (_mining.VoxelsRefused == 0
                ? $" — the hardness gate never fired, so {_mining.Tool.Id.Path} took everything "
                  + "it was pointed at and the mechanic is untested by this run\n"
                : "\n") +
            $"remesh    {_pipeline!.EditRemeshes / swings:F1} chunks per swing, " +
            // Per swing is safe on either denominator — both counts are lifetime and the
            // warm-up cancels. The /s figure is not, and was ~10% high until 2026-09-03.
            $"{_pipeline.EditRemeshes / Math.Max(_profiler.RunSeconds, 0.001):F1}/s" +
            $" | relight {_pipeline.RelightRemeshes / swings:F1} per swing, " +
            $"{_pipeline.RelightRemeshes} total\n" +
            $"clones    {_pipeline.ChunkClones / swings:F1} per swing " +
            $"({_pipeline.ChunkClones * 2.0 / swings:F1} KiB at ~2 KiB each)\n" +
            $"clipped   {_mining.ClippedSwings} swing(s) reached past the loaded world\n" +
            (_mining.EntangledSwings > 0
                ? $"entangled {_mining.EntangledSwings} swing(s) — NOT comparable with M3d's baseline\n"
                  + $"reach     {_mining.TotalReachMetres / swings:F1} m mean, {_mining.MaxReachMetres:F1} m furthest\n"
                : string.Empty) +
            // Printed with its denominator and with the mixture stated, because every per-swing
            // mean above divides by a swing count in which a chunk bite and a sphere are one
            // each while differing by ~1,900x in voxels. A mixed run's means are a blend of two
            // unrelated quantities, and nothing else in this report would say so.
            (_mining.ChunkSwings > 0
                ? $"chunk     {_mining.ChunkSwings} of {_mining.Swings} swing(s) took a whole "
                  + $"{VoxelConstants.ChunkSizeMetres:F0} m chunk "
                  + $"({_mining.ChunkSwings * (double)VoxelConstants.ChunkVolume
                        * VoxelConstants.VoxelSize * VoxelConstants.VoxelSize
                        * VoxelConstants.VoxelSize:N0} m³ of world offered up)"
                  + (_mining.ChunkSwings == _mining.Swings
                      ? "\n"
                      : " — MIXED with other shapes, so every per-swing mean above is a blend\n")
                : string.Empty);
    }

    /// <summary>What a brush costs, beside what mining costs.</summary>
    /// <remarks>
    /// <b>Two decimal places on the per-placement voxel counts, and that is not cosmetic.</b> A
    /// precise arm places about a fifth of a voxel a click once its target is mostly solid, and
    /// <c>F0</c> renders that as <c>0</c> — which reads as "placed nothing" for an arm that was
    /// placing, and cost a whole reading of the M6d-2 numbers before it was noticed. Same family
    /// as every counter here printing its own denominator.
    /// </para>
    /// <para>
    /// <b>Its own series rather than folded into <c>swing ms</c></b>, because the two shapes are
    /// not the same size: <c>hand_pick</c>'s sphere is about seventeen voxels and a cube brush is
    /// sixty-four, so one line would report whichever the run happened to do more of. Occupied
    /// against placed is the number that says whether the lattice is landing where the aim
    /// suggests — a placement that is nearly all occupied was aimed into a cell already full.
    /// </remarks>
    private string PlacementLines()
    {
        if (_place is null || _place.Placements == 0)
        {
            return string.Empty;
        }

        double placements = _place.Placements;

        // Named for the shape actually used rather than for the brush selected, because a run
        // that went precise did 1/64th the work under a label that says "cube" — the same trap
        // M3d records for chips sharing the swing line.
        string shape = _place.PrecisePlacements == _place.Placements
            ? "one voxel at a time"
            : $"the {_place.Brush.ToString().ToLowerInvariant()} brush";

        return
            $"{_profiler!.Place.Line("place ms")}   (raycast + brush, sim thread)\n" +
            $"building  {_place.Placements} placement(s) with {shape}, " +
            $"{_place.VoxelsPlaced / placements:F2} placed / " +
            $"{_place.VoxelsOccupied / placements:F2} occupied / " +
            $"{_place.VoxelsInsidePlayer / placements:F2} inside the player / " +
            $"{_place.VoxelsOutOfStock / placements:F2} unaffordable per placement\n" +
            $"carrying  {_inventory.TotalLitres:F0} L across {_inventory.MaterialCount} material(s)\n" +
            // The number M6d-1 named and did not take. Its recorded hypothesis is that
            // precision placement costs twice a brush per click because one voxel dropped
            // into fresh air pays a whole chunk clone and a whole relight marking, which
            // sixty-four in one cell amortise. Clones and remeshes per placement say so.
            $"clones    {_pipeline!.ChunkClones / placements:F2} per placement, " +
            $"remesh {_pipeline.EditRemeshes / placements:F2} chunks, " +
            $"relight {_pipeline.RelightRemeshes / placements:F2}" +
            // ChunkClones is pipeline-wide and mining moves it too. A run that did both
            // cannot attribute it, and saying so is cheaper than a counter nobody reads.
            (_mining is { Swings: > 0 }
                ? $"   (shared with {_mining.Swings} swing(s), not attributable)\n"
                : "\n") +
            (_place.PrecisePlacements > 0
                ? $"precise   {_place.PrecisePlacements} of {_place.Placements} placement(s) were "
                  + "one voxel — NOT comparable with a brush baseline\n"
                : string.Empty) +
            (_place.ChunkPlacements > 0
                ? $"fill      {_place.ChunkPlacements} of {_place.Placements} placement(s) filled a "
                  + $"whole {VoxelConstants.ChunkSizeMetres:F0} m chunk "
                  + $"({_place.ChunkPlacements * (double)VoxelConstants.ChunkVolume
                        * VoxelConstants.VoxelSize * VoxelConstants.VoxelSize
                        * VoxelConstants.VoxelSize:N0} m³ offered, "
                  + $"{_place.VoxelsPlaced * (double)VoxelConstants.VoxelSize
                        * VoxelConstants.VoxelSize * VoxelConstants.VoxelSize:N0} m³ actually put "
                  + "back)\n"
                  // Occupied is the headline for a fill rather than a footnote: only air is
                  // replaceable, so a fill aimed at rock is 32,768 refusals, and the ratio is
                  // what says whether the aim is landing in the hole or on the wall behind it.
                  + $"          {_place.VoxelsOccupied / (double)_place.ChunkPlacements:N0} "
                  + "voxel(s) per fill were already solid and were left alone"
                  + (_place.VoxelsOutOfStock > 0
                      ? $", {_place.VoxelsOutOfStock / (double)_place.ChunkPlacements:N0} "
                        + "unaffordable — the inventory is the real limit here\n"
                      : "\n")
                : string.Empty) +
            $"clipped   {_place.ClippedPlacements} placement(s) reached past the loaded world\n";
    }

    /// <summary>What persistence cost, on each of the two threads that pay for it.</summary>
    /// <remarks>
    /// <para>
    /// <b>The sim-thread figure and the worker figure are printed together and never
    /// summed.</b> M6-PLAN's headline risk is that unloading stopped being free, because
    /// <c>Forget</c> is called synchronously from inside a box-difference sweep that at r16
    /// hands over a 37x37 shell in one frame. The answer was to make the write a job, so the
    /// sweep does a flag and a list append and the disk work happens elsewhere. Two numbers,
    /// one near zero, is that answer holding; two similar ones is it having come undone, and
    /// no other line in this report would change if it had.
    /// </para>
    /// <para>
    /// <b>Bytes are per chunk and time is per batch, and they are labelled that way</b>
    /// because they were not paid on the same denominator: a batch is the transaction and a
    /// chunk is the blob. Quoting a per-chunk commit cost is the shape of error this project
    /// has recorded against <c>Compact()</c> and against cave-lattice voxel disagreement.
    /// </para>
    /// </remarks>
    /// <summary>What the retained-chunk cache held and did, beside the sources it stood in for.</summary>
    /// <remarks>
    /// <b>Hits are printed beside loads, generates and derives, because the hit count is only
    /// legible against what it replaced.</b> A run that never turns round reports zero hits for a
    /// reason that is not "the cache does nothing", and a run that turns round reports the loads
    /// and generates that did <em>not</em> happen as the difference between arms. Derives are
    /// there because slice 1 did not cache them, and reseeds beside them because slice 2 does: a
    /// chunk that comes back with its grid is reseeded instead of derived, and the hits that came
    /// back lit say how many. The two ways a cached grid is dropped — an edit at its face, or its
    /// column moving under it — are the hits that still cost a derive.
    /// </remarks>
    /// <summary>
    /// How often the queue's "every 200 ms" re-sort actually fired in the window, and what each
    /// one cost.
    /// </summary>
    /// <remarks>
    /// The interval is a floor, not a schedule: the sweep runs inside a worker's take, so the
    /// mean gap is what the workers' wake pattern made of it and the longest gap is how stale
    /// priorities were allowed to go. The counts are the measured window; the three peaks are
    /// the whole run, because the fill's first sweep is the largest and is wanted regardless.
    /// The TryTake count is the denominator — every take is a chance the sweep had — and the
    /// worker share is the lock held, which every other worker waits on.
    /// </remarks>
    private string SweepLines()
    {
        if (_scheduler is null || _profiler is null)
        {
            return string.Empty;
        }

        SweepStatistics window = _scheduler.Sweeps.Since(_sweepsAtWarm);
        double seconds = Math.Max(_profiler.MeasuredSeconds, 0.001);
        if (window.Sweeps == 0)
        {
            return $"sweeps    none in the window, over {window.TakeCalls:N0} TryTake call(s) — " +
                   "no worker took from the queue with the interval elapsed\n";
        }

        return
            $"sweeps    {window.Sweeps:N0} in the window = {window.Sweeps / seconds:F2}/s, one every " +
            $"{window.MeanGapMillis:F0} ms mean (longest gap {window.GapMillisMax:N0} ms, whole run), " +
            $"{window.ForcedSweeps:N0} forced, {window.EmptySweeps:N0} found nothing queued | " +
            $"{window.EntriesReevaluated:N0} priorities re-evaluated, {window.MeanEntriesPerSweep:F0} a sweep mean, " +
            $"{window.MostEntriesInOneSweep:N0} most (whole run), {window.EntriesDropped:N0} dropped | " +
            $"sweep ms mean {window.MeanSweepMillis:F3} / max {window.SweepMillisMax:F3} (whole run), " +
            $"{window.SweepMillisTotal / seconds / 10.0:F3}% of one worker with the lock held | " +
            $"{window.TakeCalls:N0} TryTake call(s), {window.TakesThatFoundWork:N0} took a job\n";
    }

    private string CacheLines()
    {
        if (_pipeline is null)
        {
            return string.Empty;
        }

        ChunkStreamer streamer = _pipeline.Streamer;
        const double MiB = 1024.0 * 1024.0;

        string sources =
            $"sources   {_pipeline.ChunksGenerated} generated, {_pipeline.ChunksLoaded} loaded, " +
            $"{streamer.CacheHits} from the cache ({streamer.CacheHitsWithLight} with their light) | " +
            $"{_pipeline.LightBuilds} derives, {_pipeline.LightRevivals} reseeded | " +
            $"fluid {_pipeline.FluidLoads} loaded off the lane ({_pipeline.FluidLoadMillis:F1} ms), " +
            $"{_pipeline.FluidLoadsFromMemory} from memory, {_pipeline.FluidReclaims} reclaimed\n";

        if (!streamer.CacheEnabled)
        {
            return "cache     off (--chunkcache=0), the control arm\n" + sources;
        }

        return
            $"cache     {streamer.CachedChunkCount} chunk(s) ({streamer.CachedChunksWithLight} with light) + " +
            $"{streamer.CachedColumnCount} column(s) held, " +
            $"{streamer.CachedBytes / MiB:F1} of {streamer.CacheCapacityBytes / MiB:F0} MiB | " +
            $"{streamer.CacheHits} chunk hit(s), {streamer.ColumnCacheHits} column hit(s), " +
            $"{streamer.ChunkEvictions} + {streamer.ColumnEvictions} evicted | " +
            $"grids dropped: {streamer.CachedLightDroppedByEdit} by an edit, " +
            $"{streamer.CachedLightDroppedByColumn} by the column\n" +
            sources;
    }

    private string PersistenceLines()
    {
        if (_pipeline is null)
        {
            return string.Empty;
        }

        if (_pipeline.SaveBatches == 0 && _pipeline.ChunksLoaded == 0)
        {
            // Not silence. A run with no world open reports zero saves for a reason that is
            // not "saving is free", and a blank line there reads as the latter.
            return "store     no world open, so nothing was read or written\n";
        }

        double batches = Math.Max(_pipeline.SaveBatches, 1);
        double chunks = Math.Max(_pipeline.ChunksSaved, 1);

        return
            $"store     {_pipeline.ChunksLoaded} chunk(s) loaded, " +
            $"{_pipeline.ColumnsRestored} column(s) restored, " +
            $"of {_pipeline.SavedChunkCount} stored\n" +
            $"saving    {_pipeline.ChunksSaved} chunk(s) + {_pipeline.ColumnsSaved} " +
            $"column(s) in {_pipeline.SaveBatches} batch(es), " +
            $"largest {_pipeline.LargestSaveBatch}, {_pipeline.FailedSaves} failed\n" +
            $"savecost  sim {_pipeline.SaveQueueMillis:F3} ms total " +
            $"({_pipeline.SaveQueueMillis / batches:F4} ms per batch) | " +
            $"worker {_pipeline.SaveWriteMillis:F1} ms total " +
            $"({_pipeline.SaveWriteMillis / batches:F2} ms per batch)\n" +
            $"bytes     {_pipeline.BytesSaved} written, " +
            $"{_pipeline.BytesSaved / chunks:F0} B per chunk blob\n";
    }
    /// <summary>
    /// What the drawn geometry costs, and the AO A/B M5a exists to read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Quads per drawn chunk is the number, not total quads.</b> Two runs at the same
    /// radius do not draw the same number of chunks — the cave lattice decides whether a
    /// given chunk meshes to nothing — so a total compares two things at once. The
    /// denominator is printed beside it for the reason every counter here prints its own.
    /// </para>
    /// <para>
    /// Mesh memory follows quads, at M2d's measured ~46 KiB per drawn chunk. Vertices are
    /// four per quad by construction and printed anyway, because the vertex count is what
    /// M5a's added <c>COLOR</c> channel multiplies against.
    /// </para>
    /// </remarks>
    /// <summary>The geometry half of the periodic report, compact.</summary>
    /// <remarks>
    /// Here as well as in the profile because a <b>standing start</b> is the exact A/B: the
    /// chunk set at a fixed radius is deterministic, so AO on against AO off compares two
    /// numbers rather than two samples of a moving flythrough.
    /// </remarks>
    private string QuadSummary()
    {
        long quads = 0;
        foreach (VoxelRenderInstance instance in _instances.Values) quads += instance.QuadCount;

        return $"quads {quads} ({quads / Math.Max(_instances.Count, 1)} per drawn chunk, "
               + $"AO {(_ambientOcclusion ? "on" : "off")}, "
               + $"light {(_lightInMergeKey ? "on" : "off")})";
    }

    private string GeometryLine()
    {
        long quads = 0;
        long vertices = 0;

        foreach (VoxelRenderInstance instance in _instances.Values)
        {
            quads += instance.QuadCount;
            vertices += instance.VertexCount;
        }

        double drawn = Math.Max(_instances.Count, 1);

        return $"geometry  {quads} quads / {vertices} vertices over {_instances.Count} drawn "
               + $"chunk(s) — {quads / drawn:F0} quads per chunk, "
               + $"AO {(_ambientOcclusion ? "on" : "off")}\n";
    }

    /// <summary>
    /// A chunk's mesh is ready. Runs on the main thread inside
    /// <see cref="ChunkPipeline.Update"/>, so touching the RenderingServer here is safe.
    /// </summary>
    private void OnMeshReady(StreamedChunk chunk, ChunkMesh mesh)
    {
        // Re-meshing an existing chunk replaces its instance; M3 makes that the common case.
        OnMeshDiscarded(chunk.Position);

        ArrayMesh? arrayMesh = ChunkMeshConverter.ToArrayMesh(mesh, _material!, _material!);
        if (arrayMesh is null)
        {
            // Nothing to draw — a chunk of open sky or of solid buried rock. Most chunks.
            return;
        }

        var (x, y, z) = chunk.Position.OriginMetres;
        _instances[chunk.Position] = new VoxelRenderInstance(
            GetWorld3D(),
            arrayMesh,
            new Transform3D(Basis.Identity, new Vector3(x, y, z)),
            mesh.QuadCount,
            mesh.Opaque.VertexCount + mesh.Transparent.VertexCount);
    }

    /// <summary>
    /// A chunk that had a mesh is gone.
    /// </summary>
    /// <remarks>
    /// Handed to <see cref="MeshFreeQueue"/> rather than freed here. Godot defers the actual
    /// deallocation, so releasing several hundred meshes in one frame stutters a frame or two
    /// <em>later</em> and gets blamed on meshing. The queue hides the instance immediately and
    /// spreads the frees over the following frames.
    /// </remarks>
    private void OnMeshDiscarded(ChunkPos position)
    {
        if (_instances.Remove(position, out VoxelRenderInstance? instance))
        {
            _freeQueue.Enqueue(instance);
        }
    }

    private ChunkPos ViewerChunk()
    {
        Vector3 position = _camera?.GlobalPosition ?? Vector3.Zero;
        return VoxelPos.FromMetres(position.X, position.Y, position.Z).Chunk;
    }

    /// <summary>
    /// Drops the camera just above the surface, so a fresh run opens on terrain rather than
    /// inside rock or high in empty sky.
    /// </summary>
    private void PlaceCameraAboveTerrain(WorldGenerator generator)
    {
        if (_camera is null)
        {
            return;
        }

        Vector3 position = _camera.GlobalPosition;
        VoxelPos voxel = VoxelPos.FromMetres(position.X, position.Y, position.Z);
        int surface = generator.SurfaceHeightAt(voxel.X, voxel.Z);

        float metres = (surface + 8) * VoxelConstants.VoxelSize;
        _camera.GlobalPosition = new Vector3(position.X, metres, position.Z);
    }

    /// <summary>Strata for the base game. Data already — moving it into mod JSON is on the TODO.</summary>
    private StrataTable BuildStrata() => new(
        [
            new StrataLayer(MaterialId("overburden:grass"), 1),
            new StrataLayer(MaterialId("overburden:dirt"), 3),
        ],
        MaterialId("overburden:stone"));

    private void Report(double delta)
    {
        _sinceReport += delta;
        if (_sinceReport < 5.0 || _pipeline is null)
        {
            return;
        }

        double reportSeconds = _sinceReport;
        _sinceReport = 0.0;
        ChunkStreamer streamer = _pipeline.Streamer;

        GD.Print(
            $"M2c: r{_radiusState.Horizontal}h/{_radiusState.Vertical}v, " +
            $"{streamer.ViewedCount} viewed ({streamer.TrackedCount} tracked), " +
            $"{_instances.Count} drawn, {_pipeline.JobsInFlight} in flight, " +
            $"{_pipeline.WorkListCount} queued, {_pipeline.PendingResults} to apply, " +
            $"{_freeQueue.PendingCount} to free | " +
            $"jobs {_pipeline.CompletedJobs} done / " +
            $"{_pipeline.DroppedJobs} dropped / {_pipeline.FaultedJobs} faulted | " +
            QuadSummary());

        if (_scheduler is not null)
        {
            // The delta since the last report, so the line reads as a rate; the peaks are the
            // run's, as on the profile's sweeps line.
            SweepStatistics all = _scheduler.Sweeps;
            SweepStatistics since = all.Since(_sweepsAtReport);
            _sweepsAtReport = all;
            GD.Print(
                $"M2a: sweeps {since.Sweeps:N0} in {reportSeconds:F1} s = {since.Sweeps / Math.Max(reportSeconds, 0.001):F1}/s, " +
                $"every {since.MeanGapMillis:F0} ms mean (longest {since.GapMillisMax:N0} ms, run), " +
                $"{since.EmptySweeps:N0} empty | {since.EntriesReevaluated:N0} re-evaluated " +
                $"({since.MeanEntriesPerSweep:F0} a sweep, most {since.MostEntriesInOneSweep:N0}, run), " +
                $"{since.EntriesDropped:N0} dropped | sweep ms {since.MeanSweepMillis:F3} mean / " +
                $"{since.SweepMillisMax:F3} max (run) | {since.TakeCalls:N0} takes, {since.TakesThatFoundWork:N0} took a job");
        }

        GD.Print($"M5c: {LightSummary()}");
        GD.Print($"M5d: {FillLine().TrimEnd()}");

        if (_store is not null)
        {
            GD.Print($"M6: {StoreSummary()}");
        }

        if (streamer.CacheEnabled)
        {
            GD.Print(
                $"cache: {streamer.CachedChunkCount} chunks + {streamer.CachedColumnCount} columns " +
                $"({streamer.CachedBytes / (1024.0 * 1024.0):F1} of " +
                $"{streamer.CacheCapacityBytes / (1024.0 * 1024.0):F0} MiB), " +
                $"{streamer.CacheHits} hits ({streamer.CacheHitsWithLight} lit), " +
                $"{streamer.ChunkEvictions + streamer.ColumnEvictions} evicted | " +
                $"{_pipeline.ChunksGenerated} generated, {_pipeline.ChunksLoaded} loaded, " +
                $"{_pipeline.LightBuilds} derives, {_pipeline.LightRevivals} reseeded");
        }

        GD.Print($"M6c: {InventorySummary()}");
        GD.Print($"M7c: {WeatherSummary()}");

        if (_water is not null) GD.Print($"M7d: {WaterSummary()}");
        if (_fluid is not null) GD.Print($"M8b: {FluidSummary()}");
        if (_support is not null) GD.Print($"M9: {SupportSummary()}");
    }

    /// <summary>
    /// What the sky is doing, and what it has cost the streamer to do it.
    /// </summary>
    /// <remarks>
    /// <b>The rate is printed beside the two terms derived from it</b>, because wetness and
    /// overcast are integrals over windows that reach forwards and backwards — so "raining hard
    /// and the ground still dry" and "dry and the ground still wet" are both correct readings, and
    /// without the instantaneous rate next to them either one looks like a wire that came loose.
    /// The clock is printed in world seconds rather than as a wall-clock time because it is not
    /// one: it stops when the world closes.
    /// </remarks>
    private string WeatherSummary()
    {
        long now = _clock.ElapsedMillis;
        int rate = Weather.RateAt(_settings.Seed, now);

        float wetness = _forcedWeather ?? Weather.WetnessAt(_settings.Seed, now);
        float overcast = _forcedWeather ?? Weather.OvercastAt(_settings.Seed, now);

        string forced = _forcedWeather is null ? string.Empty : " (forced)";
        string scaled = _clockScale == 1.0 ? string.Empty : $" at {_clockScale:F0}x";

        return $"t {_clock.ElapsedSeconds:F0} s{scaled}, "
               + $"rain {rate}/{Weather.MaxRateNanometresPerMillisecond} ({rate * 3.6:F0} mm/h), "
               + $"wet {wetness:F2}, cloud {overcast:F2}{forced}, "
               + $"sky {_skyBrightness * (1f - (OvercastDimming * overcast)):F2} | "
               + SkyFieldSummary();
    }

    /// <summary>What the store has done this session, each counter beside its denominator.</summary>
    /// <remarks>
    /// <b>"Loaded" against tracked is the number M6b's crossover worry turns on</b>, and it is the
    /// only one that says whether this run took the load path at all — a world with nothing saved
    /// generates throughout and reports zero, which is a pass rather than a silence. Pending and
    /// failed are the pair that says a store is in trouble: pending that climbs and stays up is a
    /// lane that is not keeping up, and failed is a disk that is refusing.
    /// </remarks>
    private string StoreSummary()
    {
        ChunkStreamer streamer = _pipeline!.Streamer;

        return $"{_pipeline.ChunksLoaded} loaded / {streamer.TrackedCount} tracked, "
               + $"{_pipeline.ChunksSaved} chunks + {_pipeline.ColumnsSaved} columns + "
               + $"{_pipeline.PanelsSaved} fluid panels saved "
               + $"({_pipeline.BytesSaved / 1024.0:F0} KiB), "
               + $"{_pipeline.SavedChunkCount} chunks in the world, "
               + $"{_pipeline.ColumnsRestored} columns restored, "
               + $"{_pipeline.PendingSaves} pending, {_pipeline.FailedSaves} failed | "
               + $"fluid {_pipeline.SavedFluidCount} row(s) in the world, "
               + $"{_pipeline.FluidLoads} loaded off the lane ({_pipeline.FluidLoadMillis:F1} ms, "
               + $"{_pipeline.FluidLoadsEmpty} empty), {_pipeline.FluidLoadsFromMemory} from memory, "
               + $"{_pipeline.FluidReclaims} reclaimed, {_pipeline.UnwrittenFluidCount} unwritten";
    }

    /// <summary>What has been dug up and what has been built with it.</summary>
    /// <remarks>
    /// <b>Litres carried beside voxels placed</b>, because the pair is the loop this milestone is
    /// for: mining fills the first and building empties it. A session where one moves and the
    /// other does not is a wire that is not connected.
    /// </remarks>
    private string InventorySummary()
    {
        string held = _inventory.MaterialCount == 0
            ? "carrying nothing"
            : $"{_inventory.TotalLitres:F0} L across {_inventory.MaterialCount} material(s)";

        if (_place is null) return held;

        return $"{held} | {_place.Placements} placement(s), {_place.VoxelsPlaced} voxel(s) placed, "
               + $"{_place.VoxelsOccupied} occupied, {_place.VoxelsInsidePlayer} inside the player, "
               + $"{_place.VoxelsOutOfStock} unaffordable";
    }

    /// <summary>
    /// The light stage's own numbers, printed beside the streaming ones.
    /// </summary>
    /// <remarks>
    /// <b>Every counter has its denominator next to it</b>, which this project has had to learn
    /// twice. "Derives" against tracked chunks says whether the rebuild rule is thrashing;
    /// "uniform" against derives is the closed form's coverage, which was 12% in the all-surface
    /// test world and needs a real one; and relight remeshes against drawn chunks is half of
    /// M5's fragment-shader trigger. A backlog that climbs and stays up means the 2 ms slice is
    /// too small, the same way <c>PeakPending</c> reads for the free path.
    /// </remarks>
    private string LightSummary()
    {
        ChunkStreamer streamer = _pipeline!.Streamer;
        int derives = _pipeline.LightBuilds;
        int uniform = _pipeline.UniformLightBuilds;

        string uniformShare = derives > 0 ? $"{100.0 * uniform / derives:F0}%" : "n/a";

        return $"{_pipeline.LightDeriveMillis:F0} ms deriving / "
               + $"{_pipeline.LightPropagateMillis:F0} ms propagating, "
               + $"{derives} derives / {streamer.TrackedCount} tracked ({uniform} uniform, "
               + $"{uniformShare}), {_pipeline.LightCellsVisited} cells visited, "
               + $"{streamer.SkyColumnCount} sky columns, "
               + $"{_pipeline.RelightRemeshes} relight remeshes, "
               + $"{_pipeline.LightBuildBacklog} queued, "
               + $"light {(_pipeline.LightInFlight ? "in flight" : "settled")}";
    }

    /// <summary>
    /// Opens the world file, restores what it remembers, and loads content against it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The one ordering trap in M6b-3.</b> Open the store, read the header, load content seeded
    /// with its ID table, and only then let anything register a material.
    /// <see cref="Registry{T}.SeedIdTable"/> throws if anything registered first, which is the
    /// right failure and a loud one — the alternative is numeric IDs handed out against a
    /// different table from the one every stored blob was written with, and stone-coloured air.
    /// </para>
    /// <para>
    /// <b>An existing world's settings replace the defaults rather than sitting beside them.</b>
    /// An unedited chunk is regenerated rather than stored, and it regenerates from all thirteen
    /// fields — so a world reopened by a build whose defaults have moved comes back as different
    /// terrain wrapped around the saved chunks. That is the whole reason the header carries the
    /// record rather than the seed.
    /// </para>
    /// </remarks>
    /// <returns>False if the world could not be opened, in which case the run is refused.</returns>
    private bool OpenWorld(out WorldGenSettings settings)
    {
        settings = NewWorldSettings();

        if (!_persist)
        {
            if (!LoadContent(null))
            {
                GD.PushError("Content did not load cleanly; refusing the run.");
                return false;
            }

            GD.Print("M6: no world file — nothing this run does will be saved");
            return true;
        }

        string path = WorldPath();

        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);

            var store = new SqliteWorldStore(path);
            _store = store;

            byte[]? header = store.ReadWorldHeader();

            // Null is how "new world" is detected, and it is the only place that question is
            // asked. A world file that exists but holds no header is a world that was created and
            // never written to, which is the same thing.
            if (header is null)
            {
                if (!LoadContent(null))
                {
                    throw new InvalidOperationException("the content did not load cleanly (see above)");
                }

                GD.Print($"M6: new world at {path}, seed 0x{settings.Seed:X}");
                return true;
            }

            WorldHeader world = WorldHeaderBlob.Read(header);
            settings = world.Settings;
            _restoredInventory = world.Inventory;
            _restoredWatershedCellMetres = world.WatershedCellMetres;
            _savedPlayer = world.Player;

            // The world's own clock replaces whatever --clock asked for, and that ordering is the
            // point rather than a precedence rule: this is the line that makes "time advances only
            // while the world is open" observable. Reopen a world an hour later and it resumes at
            // the millisecond it was closed at, because nothing here consults the calendar.
            _clock = new WorldClock(world.ElapsedMillis);

            if (_caveLatticeStep is not null)
            {
                // Ignored rather than applied, because the header is what the stored chunks were
                // generated against. --cavestep is an A/B seam and belongs to a run with its own
                // world or none at all.
                GD.PushWarning(
                    "--cavestep is ignored for an existing world: its header decides how the "
                    + "unstored terrain regenerates. Use --world=<name> for a scratch world.");
            }

            if (!LoadContent(world.MaterialIdTable))
            {
                // Inside the try on purpose: the catch disposes the store, and a world whose
                // content failed against its table must not be written to on the way out.
                throw new InvalidOperationException("the content did not load cleanly (see above)");
            }

            GD.Print(
                $"M6: opened {path} — seed 0x{settings.Seed:X}, "
                + $"{world.MaterialIdTable.Count} material id(s) restored, "
                + $"{world.Inventory.Count} material(s) carried, "
                + (world.Player is null ? "no saved player" : "player restored"));

            return true;
        }
        catch (Exception error)
        {
            // Loud, and fatal. A world that cannot be opened is not a world that should be played
            // beside: generating over the top of it and saving nothing would look like a fresh
            // start and read, on the next successful open, as a world that lost a session.
            GD.PushError($"M6: cannot open the world at {path} — {error.Message}");

            _store?.Dispose();
            _store = null;

            return false;
        }
    }

    /// <summary>What the world file said was being carried. Applied once the registry is loaded.</summary>
    private IReadOnlyList<InventoryEntry> _restoredInventory = [];

    /// <summary>The lattice this world's stored basin volumes are keyed on.</summary>
    /// <remarks>
    /// Defaulted to the lattice this build uses, so a world with no header - a new one - matches
    /// by construction and keeps nothing rather than discarding nothing.
    /// </remarks>
    private int _restoredWatershedCellMetres =
        HeightField.DefaultCellVoxels / VoxelConstants.VoxelsPerMetre;

    /// <summary>
    /// The world as it stands right now: its settings, its ID table, and the session.
    /// </summary>
    /// <remarks>
    /// <b>Rebuilt on every save rather than mutated</b>, because the ID table has to go with the
    /// inventory that is keyed by it — writing the two at different moments is the one arrangement
    /// that can desynchronise, which is the whole reason they share a blob.
    /// </remarks>
    private WorldHeader CurrentHeader() => new()
    {
        Settings = _settings,
        MaterialIdTable = Content.Materials.ExportIdTable(),
        Inventory = [.. _inventory.Entries().Select(e => new InventoryEntry(e.MaterialId, e.Voxels))],
        ElapsedMillis = _clock.ElapsedMillis,
        Player = SavedPlayer(),
    };

    /// <summary>Banks the analysed window's water and writes every basin the world knows about.</summary>
    /// <remarks>
    /// <para>
    /// <b>Bank first, always.</b> The ledger is the persistent thing and the installed window's
    /// water is a view of the part of it the player is standing in — so writing without banking
    /// saves the world as it was when they walked into this window rather than as it is, which
    /// reads as a pond that stops filling whenever somebody stands still near it.
    /// </para>
    /// <para>
    /// <b>Called from the same bracket as <c>SaveAll</c>, which is what keeps the store's "one
    /// caller at a time" contract.</b> M6b-3 found that contract broken by exactly this shape of
    /// addition: a sim-thread write while the serial lane is inside the store, and the lane is
    /// almost continuously inside it while the player moves. <c>SaveAll</c> drains the lane first.
    /// </para>
    /// </remarks>
    private void WriteBasins()
    {
        if (_store is null || _water is null)
        {
            return;
        }

        _water.Bank();
        _store.WriteBasins(_water.Ledger.Entries());
    }

    /// <summary>Where the player is, for the world file — or null if there is no body yet.</summary>
    /// <remarks>
    /// <b>The camera decides while flying and the body decides while walking</b>, and they are not
    /// interchangeable: in fly mode the body is inert and sits wherever the last F left it, so
    /// saving it would put the player back somewhere they flew away from ten minutes ago.
    /// </remarks>
    private PlayerSave? SavedPlayer()
    {
        // Nothing live to ask yet — at open, before the body exists — so keep what the world
        // already remembered. Returning null here would clear the saved position on every launch
        // and leave it cleared until the first autosave, which is a crash away from losing it.
        if (_player is not { Spawned: true }) return _savedPlayer;

        bool flying = _player.Mode == PlayerMode.Fly;

        if (!flying)
        {
            return new PlayerSave(_player.FeetCentre, flying);
        }

        // The cached eye, never the live node. If no frame has run in fly mode there is nothing
        // honest to say, so the previous answer stands rather than an invented one.
        if (_flyingEye is not System.Numerics.Vector3 eye) return _savedPlayer;

        return new PlayerSave(
            eye - new System.Numerics.Vector3(0.0f, _player.Config.EyeHeightMetres, 0.0f),
            Flying: true);
    }

    /// <summary>Where this run's world file lives. See <see cref="DefaultWorldName"/>.</summary>
    private string WorldPath() =>
        ProjectSettings.GlobalizePath($"user://worlds/{_worldName}/world.db");

    /// <summary>Whether <paramref name="name"/> can be a directory under <c>user://worlds/</c>.</summary>
    /// <remarks>
    /// <b>Not a security boundary — a single-player game's own command line is not an attacker.</b>
    /// It is there because the failure it prevents is silent: a name carrying a separator puts the
    /// world somewhere other than where the next run will look for it, and the symptom is a world
    /// that opens empty rather than an error.
    /// </remarks>
    private static bool IsUsableWorldName(string name) =>
        name.Length > 0
        && name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) < 0
        && name is not "." and not "..";

    /// <summary>The settings a world is created with, before it has a header of its own.</summary>
    private WorldGenSettings NewWorldSettings()
    {
        var settings = new WorldGenSettings { Seed = NewWorldSeed };

        return _caveLatticeStep is int step
            ? settings with { CaveLatticeStep = step }
            : settings;
    }

    /// <summary>Loads the mods against the world's table. False refuses the run.</summary>
    /// <remarks>
    /// A content error used to be printed and played past. On 2026-09-03 a stale binary opened
    /// the played world, failed to define air, carried on, and wrote a header with two numbers for
    /// one material on quit - so a content error is fatal for the reason an unopenable world is:
    /// playing beside it writes something the next session cannot read.
    /// </remarks>
    private bool LoadContent(IReadOnlyList<Identifier>? savedMaterialIds)
    {
        string modsPath = ProjectSettings.GlobalizePath("res://mods");
        var result = Content.Load(new DirectoryContentSource(modsPath), savedMaterialIds);

        foreach (string error in result.Errors)
        {
            GD.PushError($"Content: {error}");
        }

        GD.Print($"Content: {result}");
        return result.Success;
    }

    private int MaterialId(string identifier) =>
        Content.Materials.TryGetNumericId(Identifier.Parse(identifier), out int id)
            ? id
            : MaterialIds.AirNumericId;

    public override void _ExitTree()
    {
        // The rain first and on its own: it owns no chunk state and nothing below waits on it, so
        // it is deliberately outside the ordering argument that follows rather than an exception
        // to it.
        _rain?.Dispose();
        _waterSurface?.Dispose();
        _fluidSurface?.Dispose();
        _underwaterLayer?.QueueFree();
        _skyHeights.Dispose();

        // Order matters: stop the pipeline submitting, then stop the workers, then release
        // the RIDs. Freeing instances while a mesh result could still arrive would hand the
        // renderer a mesh for a chunk that has just been torn down.
        //
        // The pipeline first is also what saves the world: Dispose waits for the serial lane to
        // come out of the store and then writes everything dirty, synchronously. Disposing the
        // scheduler first would drop whatever was queued on that lane, and JobResult's contract
        // says a dropped job reports Dropped rather than running — which for a save is a deleted
        // tunnel. This is the single most likely way to ship a data-loss bug in M6.
        // Before the dispose, because Dispose writes the chunks and this writes the session. Both
        // are the same quit and losing either half is the same bug.
        if (_store is not null && _pipeline is not null)
        {
            try
            {
                _pipeline.WriteWorldHeader(CurrentHeader());
                WriteBasins();
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                GD.PushError($"M6: could not write the world session on quit — {error.Message}");
            }
        }

        // A tick out on a worker is cancelled and waited for before anything is written (slice
        // 3b): the collection gives its unvisited cells back to the queue, so the rows list them
        // awake, and the save that follows finds the grid free. Before the dispose, which is what
        // saves.
        if (_fluid is { JobInFlight: true } && _pipeline is not null)
        {
            int remaining = _fluid.RemainingInTick;
            long before = Stopwatch.GetTimestamp();
            _fluidJobCancelledForQuit = true;
            _fluidJobCts?.Cancel();
            bool reported = _pipeline.WaitForDetached();
            GD.Print(reported
                ? $"M8b: cancelled the tick in flight before the quit — {remaining:N0} cell(s) were still to visit, "
                  + $"{_fluid.CellsRequeued:N0} returned to the queue over the session, in "
                  + $"{Milliseconds(before, Stopwatch.GetTimestamp()):F1} ms"
                : "M8b: the fluid tick did not report within the timeout; the quit goes on without it");
        }

        // A tick in flight on the sim thread is finished before anything is written (slice 3a):
        // the snapshot refuses a grid half-way through a tick, and the quit is the one caller that
        // cannot wait for a boundary. Whole rather than cancelled: at a quit the cost is one
        // tick's remainder, printed.
        if (_fluid is { TickInProgress: true })
        {
            int remaining = _fluid.RemainingInTick;
            long before = Stopwatch.GetTimestamp();
            _fluid.Step();
            GD.Print(
                $"M8b: finished the tick in flight before the quit — {remaining:N0} cell(s) in "
                + $"{Milliseconds(before, Stopwatch.GetTimestamp()):F1} ms");
        }

        // The water as it stands at the instant it is written, because the periodic line is up to
        // five seconds stale and the hand-off keeps draining a spill between the two — the
        // figure the next open's "restored" line has to equal is this one.
        if (_fluid is not null && _store is not null)
        {
            _fluid.Publish();
            GD.Print(
                $"M8b: at quit — {_fluid.Published.TotalMillilitres / (double)FluidConstants.MillilitresPerLitre:N0} L in "
                + $"{_fluid.Published.HeldCells:N0} cell(s), {_fluid.Published.AwakeHeld:N0} awake or paused");
        }

        _pipeline?.Dispose();
        _scheduler?.Dispose();

        // After the dispose rather than before it, so the figures are what was actually written
        // rather than what was about to be. Printed at all because "did the quit path run?" is the
        // question this milestone most needs answered from a log a day later.
        if (_store is not null && _pipeline is not null)
        {
            GD.Print($"M6: world written on quit — {StoreSummary()}");

            // M8b-4c: the open no longer restores every row, so "at quit equals restored" is not
            // an oracle any more. This is its replacement — every row of the file summed after the
            // write, which the next open's `loaded` reaches once the box has covered every wet
            // chunk. Read here and not at open, because the open must not be a function of how
            // much water the world holds; a quit may be.
            if (_fluid is not null)
            {
                long onDisk = 0;
                int rows = 0, empty = 0;
                foreach ((ChunkPos position, byte[] blob) in _store.ReadFluid())
                {
                    rows++;
                    long total = FluidPanelBlob.Read(blob, position).TotalMillilitres;
                    if (total == 0) empty++;
                    onDisk += total;
                }

                GD.Print(
                    $"M8b: the world file holds {onDisk / (double)FluidConstants.MillilitresPerLitre:N0} L in "
                    + $"{rows:N0} row(s) ({empty:N0} empty) — what the next open's loaded reaches once every wet chunk is home");
            }

            // Printed because a wrong one is invisible until the next launch puts the player
            // somewhere strange, and the log is then the only record of what was written.
            GD.Print(SavedPlayer() is PlayerSave saved
                ? $"M6c: saved the player at {saved.FeetMetres}, "
                  + (saved.Flying ? "flying" : "walking")
                : "M6c: no player position saved");
        }

        _highlight?.Dispose();
        _highlight = null;

        // The cell outline is a second RenderingServer instance and leaks exactly as loudly as
        // the first would: Godot names the RID at exit and says nothing about which one.
        _cellHighlight?.Dispose();
        _cellHighlight = null;

        // Budget ignored here: a stutter during teardown costs nothing and a leak would not.
        _freeQueue.DrainAll();

        foreach (VoxelRenderInstance instance in _instances.Values)
        {
            instance.Dispose();
        }

        _instances.Clear();
        _pipeline = null;
        _scheduler = null;

        // Last, and after the pipeline, which writes through it.
        _store?.Dispose();
        _store = null;
    }
}
