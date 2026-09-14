using Overburden.Core.Content;
using Overburden.Core.Fluid;
using Overburden.Core.Voxels;
using Overburden.Core.Worldgen;

namespace Overburden.Core.Structure;

/// <summary>One armed chunk: when it armed, when it is due, in world milliseconds, and the deficit it was drawn for.</summary>
public readonly record struct CollapseTimer(long ArmedAtMillis, long DueAtMillis, int Deficit);

/// <summary>A fall the sweep made: the result, when, and whether it followed another in the same chunk within a delay window.</summary>
public readonly record struct CollapseFire(CollapseResult Result, long WorldMillis, bool CascadeStage);

/// <summary>
/// The delay window (M9-PLAN decision 8): world seconds between a roof marking at risk and its
/// fall, shortened by the deficit and jittered by a seeded draw. Min 2 s, max 60 s, estimates
/// — <c>--collapseafter=&lt;min&gt;,&lt;max&gt;</c> is the sweep.
/// </summary>
/// <remarks>
/// <b>The jitter is exponential since M9d (decision 7), with the deficit's delay as its mean.</b>
/// The first play's draw was <c>U(0.5, 1.5)</c> and a thin lip always went in about a minute —
/// Matthew: <i>"I want it to be unpredictable like, like you know if you have left too much
/// overhang"</i>. An exponential keeps the lean (more overhang falls sooner on average) and lets
/// the same lip go in five seconds or three minutes: the median is 0.69 of the mean, one draw in
/// twenty is under 0.05 of it and one in twenty over three times it. The window's two numbers are
/// the mean's ends now rather than the draw's, and <c>--collapseafter=</c> still sets them.
/// </remarks>
public sealed record CollapseWindow
{
    /// <summary>The estimates as shipped.</summary>
    public static readonly CollapseWindow Default = new();

    /// <summary>The unit at which the exponential draw is exactly its mean: <c>1 − e⁻¹</c>. For tests.</summary>
    public const float MeanUnit = 0.63212056f;

    private readonly double _minSeconds = 2.0;
    private readonly double _maxSeconds = 60.0;

    /// <summary>The delay at a deficit of <c>K</c> or more, before the jitter.</summary>
    public double MinSeconds
    {
        get => _minSeconds;
        init
        {
            if (!double.IsFinite(value) || value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The minimum delay is a finite, non-negative number of seconds.");
            }

            _minSeconds = value;
        }
    }

    /// <summary>The delay at a deficit of one, before the jitter.</summary>
    public double MaxSeconds
    {
        get => _maxSeconds;
        init
        {
            if (!double.IsFinite(value) || value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The maximum delay is a finite, non-negative number of seconds.");
            }

            _maxSeconds = value;
        }
    }

    /// <summary>
    /// The delay for a chunk whose worst voxel is <paramref name="maxDeficit"/> below zero:
    /// <c>lerp(max, min, deficit / K) × Exp(1)</c>, the exponential's unit drawn from
    /// <paramref name="unit"/> in [0, 1) as <c>−ln(1 − unit)</c>.
    /// </summary>
    public double DelaySeconds(int maxDeficit, float unit)
    {
        double share = Math.Clamp(maxDeficit / (double)SupportRule.MaxBudget, 0.0, 1.0);
        double max = Math.Max(_minSeconds, _maxSeconds);
        double nominal = max + ((_minSeconds - max) * share);
        double clamped = Math.Clamp(unit, 0.0, 0.999999);
        return nominal * -Math.Log(1.0 - clamped);
    }
}

/// <summary>
/// The timers (M9c-1, decision 3): one per analysed chunk, armed when its settled field first
/// marks something at risk, fired when due, disarmed when the set empties — a sweep over the
/// region's settled fields at each tick boundary rather than a hook on the settle.
/// </summary>
/// <remarks>
/// <para>
/// <b>The sweep is the cascade.</b> A timer is cleared at a fall and re-armed from the new deficit
/// when the chunk settles again, which gives a staged collapse its pace with no cascade rule: a
/// wedge's core is unreached — deficit <c>K</c> — so each stage re-arms at the minimum, and the
/// stages of a 4 × 4 room follow each other seconds apart, while the 2 × 2 plug's deficit of 8
/// arms near the maximum: 28–84 s of warning for twenty voxels. A re-derive that keeps the set
/// non-empty leaves the timer alone, as the plan says; a chunk that is unsettled is left alone too,
/// because its snapshot is the last settle's and a count read from it is not now's.
/// </para>
/// <para>
/// <b>Seeded in world time</b>, off the world clock so <c>--clockscale</c> applies to the roof the
/// way it applies to the weather, from <c>Hash64(seed ^ worldMillis, chunk)</c> — server-
/// authoritative and stateless, per non-negotiable 1. Arming polls at the boundary, so a roof arms
/// up to half a second late against a 2–60 s window. Nothing here is persisted (decision 7): a
/// roof at risk at quit is at risk at open and arms afresh inside the delay.
/// </para>
/// <para>
/// <b>An armed chunk that is due fires only if it is settled now and every chunk the fall would
/// touch can hold water</b>; otherwise it stays due and is tried at the next boundary. Fires that
/// wait are counted apart — unsettled and fluid-pending — because the first is the barrier working
/// and the second should read zero except for a boundary or two after a chunk arrives. With
/// <see cref="FireEnabled"/> off (<c>--collapse=0</c>) the sweep arms and never fires: the
/// warning-only arm, the one that lets a person walk under a marked roof to judge the marking.
/// </para>
/// </remarks>
public sealed class CollapseScheduler
{
    private readonly SupportRegion _region;
    private readonly IVoxelEditor _world;
    private readonly MaterialTable _materials;
    private readonly int _rubbleId;
    private readonly ulong _seed;
    private readonly ICollapseWater? _water;
    private readonly Dictionary<ChunkPos, CollapseTimer> _timers = [];
    private readonly Dictionary<ChunkPos, long> _lastFiredAt = [];
    private readonly List<ChunkPos> _scratch = [];
    private readonly List<CollapseFire> _fired = [];

    public CollapseScheduler(
        SupportRegion region,
        IVoxelEditor world,
        MaterialTable materials,
        int rubbleId,
        ulong seed,
        CollapseWindow? window = null,
        ICollapseWater? water = null)
    {
        _region = region ?? throw new ArgumentNullException(nameof(region));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _materials = materials ?? throw new ArgumentNullException(nameof(materials));
        if (!materials.IsSolid(rubbleId))
        {
            throw new ArgumentException("Rubble has to be a solid material.", nameof(rubbleId));
        }

        _rubbleId = rubbleId;
        _seed = seed;
        Window = window ?? CollapseWindow.Default;
        _water = water;
    }

    /// <summary>The delay window.</summary>
    public CollapseWindow Window { get; }

    /// <summary>False arms and never fires — <c>--collapse=0</c>.</summary>
    public bool FireEnabled { get; set; } = true;

    /// <summary>Chunks with a timer.</summary>
    public int Armed => _timers.Count;

    /// <summary>Armed chunks whose timer had elapsed at the last sweep and did not fire.</summary>
    public int Due { get; private set; }

    /// <summary>Due chunks the last sweep left because they, or a chunk under a run, were unsettled.</summary>
    public int WaitingUnsettled { get; private set; }

    /// <summary>Due chunks the last sweep left because a chunk the fall would write into cannot hold water yet.</summary>
    public int WaitingFluid { get; private set; }

    /// <summary>Falls, over the lifetime.</summary>
    public long Fired { get; private set; }

    /// <summary>Voxels that fell, over the lifetime.</summary>
    public long VoxelsFallen { get; private set; }

    /// <summary>Voxels of drop summed over every fallen voxel; over <see cref="VoxelsFallen"/> is the mean drop.</summary>
    public long DropSum { get; private set; }

    /// <summary>The longest drop any run made.</summary>
    public int DeepestDrop { get; private set; }

    /// <summary>At-risk voxels a fall found not solid in the world and left alone. Expected zero.</summary>
    public long StaleSkipped { get; private set; }

    /// <summary>Millilitres lifted from under falls, over the lifetime.</summary>
    public long LiftedMillilitres { get; private set; }

    /// <summary>Millilitres returned into cavities, over the lifetime.</summary>
    public long ReturnedMillilitres { get; private set; }

    /// <summary>Lifted millilitres a fall could not return and sent to displaced. Expected zero.</summary>
    public long DisplacedMillilitres { get; private set; }

    /// <summary>Sweeps run.</summary>
    public long Sweeps { get; private set; }

    /// <summary>Timers armed, over the lifetime.</summary>
    public long ArmedInAll { get; private set; }

    /// <summary>Timers disarmed because their chunk's settled set emptied without a fall, over the lifetime.</summary>
    public long Disarmed { get; private set; }

    /// <summary>Timers redrawn earlier because a settle raised their chunk's deficit (M9d, decision 2), over the lifetime.</summary>
    public long Shortened { get; private set; }

    /// <summary>The timer on a chunk, if armed.</summary>
    public bool TryGetTimer(ChunkPos chunk, out CollapseTimer timer) => _timers.TryGetValue(chunk, out timer);

    /// <summary>
    /// One boundary: arm, disarm, fire. Returns the falls this sweep made, in the order they
    /// fired; the list is the scheduler's and is overwritten by the next sweep.
    /// </summary>
    /// <param name="worldMillis">The world clock now.</param>
    public IReadOnlyList<CollapseFire> Sweep(long worldMillis)
    {
        Sweeps++;
        _fired.Clear();
        Due = 0;
        WaitingUnsettled = 0;
        WaitingFluid = 0;

        // A timer whose chunk no longer has a settled field — departed, or its field dropped —
        // disarms: a settled count of zero, read the only way it can be.
        _scratch.Clear();
        foreach (ChunkPos armed in _timers.Keys)
        {
            if (!_region.SettledFields.ContainsKey(armed))
            {
                _scratch.Add(armed);
            }
        }

        foreach (ChunkPos gone in _scratch)
        {
            _timers.Remove(gone);
            Disarmed++;
        }

        // The settled fields, copied: a fall writes voxels, which requeues chunks, which moves
        // entries under an enumeration.
        _scratch.Clear();
        _scratch.AddRange(_region.SettledFields.Keys);

        foreach (ChunkPos chunk in _scratch)
        {
            if (!_region.TryReadSettled(chunk, out ChunkSupport field))
            {
                // Unsettled: the snapshot is the last settle's. Left alone, timer and all — a
                // re-derive that keeps the set is not an event, and one that empties it disarms
                // at the settle.
                if (_timers.TryGetValue(chunk, out CollapseTimer waiting) && waiting.DueAtMillis <= worldMillis)
                {
                    Due++;
                    WaitingUnsettled++;
                }

                continue;
            }

            if (field.AtRiskCount == 0)
            {
                if (_timers.Remove(chunk))
                {
                    Disarmed++;
                }

                continue;
            }

            if (!_timers.TryGetValue(chunk, out CollapseTimer timer))
            {
                timer = Arm(chunk, field.MaxDeficit, worldMillis);
            }
            else if (field.MaxDeficit > timer.Deficit)
            {
                // A timer only shortens (M9d, decision 2): a lake that rose over a roof armed dry
                // raises its deficit, and the fall should come on the wet clock, not the dry one.
                // Redrawn for the new deficit and the earlier due kept; a draw that lands later
                // keeps the old due and records the deficit, so it is not redrawn every sweep. A
                // deficit that shrank keeps its due — a roof that was going to fall still is.
                CollapseTimer redrawn = Draw(chunk, field.MaxDeficit, worldMillis);
                if (redrawn.DueAtMillis < timer.DueAtMillis)
                {
                    timer = redrawn;
                    Shortened++;
                }
                else
                {
                    timer = timer with { Deficit = field.MaxDeficit };
                }

                _timers[chunk] = timer;
            }

            if (timer.DueAtMillis > worldMillis)
            {
                continue;
            }

            Due++;
            if (!FireEnabled)
            {
                continue;
            }

            CollapseOutcome outcome = Collapse.TryFall(_region, chunk, _world, _materials, _rubbleId, _water, out CollapseResult result);
            switch (outcome)
            {
                case CollapseOutcome.Fell:
                    Due--;
                    Record(chunk, result, worldMillis);
                    break;

                case CollapseOutcome.NothingAtRisk:
                    // Every voxel the field marked was stale, or nothing could move: the timer
                    // goes, and the next settle arms it again if anything is still marked.
                    Due--;
                    StaleSkipped += result.StaleSkipped;
                    _timers.Remove(chunk);
                    break;

                case CollapseOutcome.WaitingUnsettled:
                    WaitingUnsettled++;
                    break;

                case CollapseOutcome.WaitingFluid:
                    WaitingFluid++;
                    break;
            }
        }

        return _fired;
    }

    /// <summary>Whether a fall at this moment is a stage of a cascade: a fire within one delay window of the chunk's last.</summary>
    private bool IsCascadeStage(ChunkPos chunk, long worldMillis) =>
        _lastFiredAt.TryGetValue(chunk, out long last)
        && worldMillis - last <= (long)(Math.Max(Window.MinSeconds, Window.MaxSeconds) * 1000.0);

    private CollapseTimer Arm(ChunkPos chunk, int maxDeficit, long worldMillis)
    {
        CollapseTimer timer = Draw(chunk, maxDeficit, worldMillis);
        _timers[chunk] = timer;
        ArmedInAll++;
        return timer;
    }

    /// <summary>A timer for this chunk and deficit, drawn from the seed and the clock, stored by nobody.</summary>
    private CollapseTimer Draw(ChunkPos chunk, int maxDeficit, long worldMillis)
    {
        float unit = Hash64.ToUnitFloat(Hash64.ForChunk(_seed ^ (ulong)worldMillis, chunk));
        double delay = Window.DelaySeconds(maxDeficit, unit);
        return new CollapseTimer(worldMillis, worldMillis + (long)Math.Round(delay * 1000.0), maxDeficit);
    }

    private void Record(ChunkPos chunk, CollapseResult result, long worldMillis)
    {
        Fired++;
        VoxelsFallen += result.Voxels;
        DropSum += result.DropSum;
        DeepestDrop = Math.Max(DeepestDrop, result.DeepestDrop);
        StaleSkipped += result.StaleSkipped;
        LiftedMillilitres += result.LiftedMillilitres;
        ReturnedMillilitres += result.ReturnedMillilitres;
        DisplacedMillilitres += result.DisplacedMillilitres;
        _fired.Add(new CollapseFire(result, worldMillis, IsCascadeStage(chunk, worldMillis)));

        // The timer is cleared for every chunk whose voxels fell — the firing chunk and the
        // chunks its runs reached into — and the re-settle re-arms each from its new deficit.
        foreach (ChunkPos fell in result.FellFrom)
        {
            _timers.Remove(fell);
        }

        _lastFiredAt[chunk] = worldMillis;
    }
}
