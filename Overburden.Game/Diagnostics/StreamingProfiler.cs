using System;
using System.Collections.Generic;
using Godot;

namespace Overburden.Game.Diagnostics;

/// <summary>A series of timing samples, reported as percentiles.</summary>
/// <remarks>
/// <b>Percentiles, not an average.</b> Streaming costs are bursty by nature — crossing a
/// chunk boundary does work that the ninety frames around it do not — so a mean hides
/// exactly the thing worth measuring. A run averaging 1 ms and spiking to 40 ms stutters
/// visibly and still reports "1 ms" to anything that only takes the mean.
/// </remarks>
public sealed class SampleSeries
{
    private readonly List<double> _values = [];

    public int Count => _values.Count;

    public void Add(double value) => _values.Add(value);

    public double Mean()
    {
        if (_values.Count == 0)
        {
            return 0.0;
        }

        double total = 0.0;
        foreach (double value in _values)
        {
            total += value;
        }

        return total / _values.Count;
    }

    public double Percentile(double quantile)
    {
        if (_values.Count == 0)
        {
            return 0.0;
        }

        var sorted = new List<double>(_values);
        sorted.Sort();

        int index = (int)Math.Round(quantile * (sorted.Count - 1));
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }

    public double Max()
    {
        double max = 0.0;
        foreach (double value in _values)
        {
            if (value > max)
            {
                max = value;
            }
        }

        return max;
    }

    public double Total()
    {
        double total = 0.0;
        foreach (double value in _values)
        {
            total += value;
        }

        return total;
    }

    public string Line(string label) =>
        $"{label,-10}mean {Mean():F3}  p50 {Percentile(0.50):F3}  p95 {Percentile(0.95):F3}  " +
        $"p99 {Percentile(0.99):F3}  max {Max():F3}";
}

/// <summary>
/// Measures what streaming costs the main thread, over a warmed-up window.
/// </summary>
/// <remarks>
/// <para>
/// <b>Our own work is timed directly rather than inferred from frame delta.</b> The first
/// version of this measured frame time and reported mean, p50, p95, p99 and max all within
/// 0.04 ms of each other — which is not uniform work, it is a main loop paced by something
/// that is not us. Nearly all the expensive work here happens on worker threads, so frame
/// delta says almost nothing about streaming; what predicts stutter is how long the main
/// thread spends inside <c>ChunkPipeline.Update</c> and the free queue.
/// </para>
/// <para>
/// Frame delta is still collected, because in a <em>windowed</em> run it carries the
/// rendering cost that a headless run cannot see. The gap between the two runs is that cost.
/// </para>
/// </remarks>
public sealed class StreamingProfiler
{
    private double _elapsed;

    /// <summary>Wall-clock frame time. Only meaningful windowed.</summary>
    public SampleSeries Frame { get; } = new();

    /// <summary>
    /// Main-thread time inside the pipeline: applying results, submitting jobs, and building
    /// the <c>ArrayMesh</c> for every chunk that finished meshing this frame.
    /// </summary>
    public SampleSeries PipelineUpdate { get; } = new();

    /// <summary>
    /// Main-thread time spent releasing meshes.
    /// </summary>
    /// <remarks>
    /// <b>Reads low, and that is the whole problem.</b> Godot defers the buffer
    /// deallocation, so the cost is not spent inside the call being timed — which is exactly
    /// why the free path is budgeted by count rather than by a time slice. Treat this as
    /// "what we can see", not as what it costs.
    /// </remarks>
    public SampleSeries MeshFree { get; } = new();

    /// <summary>
    /// Sim-thread time for one <c>ChunkStreamer.MoveViewer</c>: the whole box-difference sweep.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A sibling of <see cref="PipelineUpdate"/>, not a subset of it</b> — unlike
    /// <see cref="Light"/>. <c>MoveViewer</c> is called from <c>_Process</c> before
    /// <c>ChunkPipeline.Update</c> and outside the bracket that times it, so until M6d-2 this
    /// cost was measured by nothing at all. Adding the two together is correct here, and it is
    /// the one place in this class where that is true.
    /// </para>
    /// <para>
    /// <b>It is the series M6 has been owed since its plan.</b> The milestone's headline risk is
    /// that <c>Forget</c> stopped being free: it is called synchronously, on the sim thread, from
    /// inside <c>ChunkBox.ForEachDifference</c>, which at r16 hands it a 37x37 shell in one frame.
    /// Every other number in the profile would look identical whether that answer worked or not.
    /// </para>
    /// <para>
    /// Zero on most frames by construction — the viewer only moves in whole chunks — so read the
    /// maximum and the sample count, not the mean. A mean over frames that mostly did nothing is
    /// a statement about how often the camera crossed a boundary.
    /// </para>
    /// </remarks>
    public SampleSeries ViewerMove { get; } = new();

    /// <summary>
    /// Sim-thread time for one frame's light step: deriving, propagating and marking.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A subset of <see cref="PipelineUpdate"/>, not a sibling of it.</b> <c>StepLight</c> is
    /// called from inside <c>ChunkPipeline.Update</c>, so this time is already counted in
    /// <c>update ms</c>; it is broken out because light is the one part of the update whose cost
    /// is bounded by a budget of its own, and the budget was swept at M5c-1 against when the
    /// world could first draw rather than against throughput. Adding the two together
    /// double-counts.
    /// </para>
    /// <para>
    /// M5-PLAN asks for this timed directly and with its denominator beside it, which is the
    /// pair this project has had to learn twice — cells visited is a total, and a total with no
    /// seconds under it is not a rate.
    /// </para>
    /// </remarks>
    public SampleSeries Light { get; } = new();

    /// <summary>
    /// Sim-thread time to refill the rain's ground-height field.
    /// </summary>
    /// <remarks>
    /// <b>M7c-2's estimate was ~0.25 ms on a chunk crossing, and it was an estimate.</b> It is
    /// timed apart from <see cref="ViewerMove"/> even though both fire on a crossing, because
    /// M6d-2's lesson was that a cost folded into a neighbour's bracket is a cost nobody measures
    /// — the box-difference sweep went five milestones unmeasured that way. Sample count matters
    /// as much as the mean here: this also fires when somebody digs through to daylight, and a run
    /// where it fired once is a run that measured a chunk crossing and nothing else.
    /// </remarks>
    public SampleSeries SkyField { get; } = new();

    /// <summary>
    /// Sim-thread time for one swing: the raycast plus the carve.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="PipelineUpdate"/> on purpose. This is what the click costs
    /// directly; the remeshes it causes come back through the pipeline a frame or more later
    /// and land in <c>update ms</c>. M3's question is about the second number, and this
    /// series exists to show it is not the first.
    /// </remarks>
    public SampleSeries Swing { get; } = new();

    /// <summary>
    /// Sim-thread time for one placement: the raycast plus the brush.
    /// </summary>
    /// <remarks>
    /// Beside <see cref="Swing"/> rather than folded into it, because the two shapes are not the
    /// same size — <c>hand_pick</c>'s sphere is about seventeen voxels and a cube brush is
    /// sixty-four — so one series would report whichever the run happened to do more of.
    /// </remarks>
    public SampleSeries Place { get; } = new();

    /// <summary>
    /// Sim-thread time for one Tier B tick.
    /// </summary>
    /// <remarks>
    /// Timed directly, like everything else here. The tick runs at 2 Hz whatever the frame rate,
    /// so its cost is a spike rather than a load: M8a measured 2.7 ms for the widest breach — a
    /// sixth of a frame arriving at once — and named a time slice without building one. This is
    /// the series that says whether it needs building, and it is empty in a run where nothing was
    /// poured, which the report says rather than printing zeros.
    /// </remarks>
    public SampleSeries FluidTick { get; } = new();

    /// <summary>
    /// Main-thread time for one <c>PlayerController.TryStep</c>: the whole collider solve.
    /// </summary>
    /// <remarks>
    /// <b>Timed directly, and this is the fourth time that has had to be said.</b> The headless
    /// frame-time trap has been walked into at M2d-1, at the mesh-apply budget and at M3d, and
    /// every time the spike did not reproduce while the directly-timed number moved by a tenth
    /// of a millisecond. <c>frame ms</c> in a headless run is a main loop paced by something
    /// that is not us; this is a stopwatch around the call.
    /// </remarks>
    public SampleSeries PlayerStep { get; } = new();

    /// <summary>Player steps that actually ran during the measured window.</summary>
    public int Steps { get; private set; }

    /// <summary>Total substeps across those steps, including rejected step-up probes.</summary>
    public long Substeps { get; private set; }

    /// <summary>Total <c>TryGetMaterial</c> calls across those steps, on the same basis.</summary>
    public long VoxelQueries { get; private set; }

    /// <summary>Steps standing on a chunk that had not loaded.</summary>
    /// <remarks>
    /// <b>The number terminal velocity is supposed to be picked from.</b> The body is grounded
    /// and the ground is about to stop existing — it is the player having arrived somewhere
    /// before the world did. A run where this is zero says the streamer kept up at that speed.
    /// </remarks>
    public int StepsHeld { get; private set; }

    /// <summary>The longest unbroken run of held steps.</summary>
    /// <remarks>
    /// Separate from <see cref="StepsHeld"/> because they answer different questions. A hundred
    /// scattered single-step holds is the streamer keeping up with a frame to spare; one
    /// hundred-step stall is a second and a half of standing on nothing, and only the second is
    /// something a player would notice.
    /// </remarks>
    public int LongestHold { get; private set; }

    /// <summary>A body that was standing still stopped being on the ground.</summary>
    /// <remarks>
    /// <para>
    /// <b>A resting-contact regression detector that costs one counter.</b> A body standing
    /// still on a floor must report grounded every step forever; anything else means the −Y
    /// resolve is not firing reliably, which reads in game as the camera twitching.
    /// </para>
    /// <para>
    /// <b>The first version counted landings.</b> It compared ground state between consecutive
    /// steps whenever the body was at rest — but the landing step <em>is</em> the first at-rest
    /// step, and the one before it was a fall, so every landing scored a flicker. The first fall
    /// profile duly reported "1 flicker" and it was an artefact. The test is now one-directional
    /// and asymmetric: <b>was at rest, and is now not grounded</b>. A landing cannot satisfy it,
    /// a jump cannot (nothing asks the profiled body to jump), and a deliberate relaunch is
    /// excluded through <see cref="ForgetGroundState"/>.
    /// </para>
    /// <para>
    /// <b>Then it counted held steps, which was the second artefact.</b> A body standing on a
    /// chunk that has not loaded is grounded with its velocity zeroed by the contact, so it
    /// satisfied "at rest" — and the step where the chunk arrived and proved to be air was a
    /// textbook was-at-rest-now-airborne. The r16h/3v at 120 m/s sweep duly reported 5 flickers
    /// over 28 resting steps, every one of them the held-then-released pattern
    /// <see cref="StepsHeld"/> already counts. <b>The caller must exclude held steps from
    /// <c>atRest</c></b>, which is why that parameter is the caller's to compute and not
    /// derived here from <c>grounded</c>.
    /// </para>
    /// </remarks>
    public int GroundFlickers { get; private set; }

    /// <summary>Steps the body spent standing still.</summary>
    /// <remarks>
    /// Reported next to <see cref="GroundFlickers"/> so a zero is interpretable. A fall profile
    /// that never lands has nothing at rest to flicker, and "0 flickers" would otherwise read
    /// as a clean bill of health for a detector that never ran.
    /// </remarks>
    public int RestingSteps { get; private set; }

    private int _currentHold;
    private bool _lastAtRest;

    /// <summary>Records one player step.</summary>
    /// <param name="milliseconds">Directly measured cost of the step.</param>
    /// <param name="substeps">Substeps the step took.</param>
    /// <param name="voxelQueries">Voxel reads the step cost.</param>
    /// <param name="grounded">Whether the step ended on something.</param>
    /// <param name="heldByUnloaded">Whether that something was an unloaded chunk.</param>
    /// <param name="atRest">
    /// Whether the body is standing still, which is the only situation in which a ground-state
    /// change is a flicker rather than an event.
    /// </param>
    public void AddPlayerStep(
        double milliseconds,
        int substeps,
        int voxelQueries,
        bool grounded,
        bool heldByUnloaded,
        bool atRest)
    {
        PlayerStep.Add(milliseconds);
        Steps++;
        Substeps += substeps;
        VoxelQueries += voxelQueries;

        if (heldByUnloaded)
        {
            StepsHeld++;
            _currentHold++;
            if (_currentHold > LongestHold)
            {
                LongestHold = _currentHold;
            }
        }
        else
        {
            _currentHold = 0;
        }

        // Asymmetric on purpose — see GroundFlickers. A body that was standing still and is
        // suddenly airborne is the defect; a body that was airborne and is now standing still
        // is a landing.
        if (_lastAtRest && !grounded)
        {
            GroundFlickers++;
        }

        if (atRest)
        {
            RestingSteps++;
        }

        _lastAtRest = atRest;
    }

    /// <summary>
    /// Forgets the resting state, so a deliberate reposition is not scored as a flicker.
    /// </summary>
    /// <remarks>
    /// Called when the fall profile relaunches the body. Without it every relaunch would report
    /// a flicker, because the body was at rest and the next step is several hundred metres up in
    /// mid-air — which is exactly the pattern the detector is looking for, and exactly not a bug.
    /// </remarks>
    public void ForgetGroundState() => _lastAtRest = false;

    /// <summary>The player half of the report, or nothing if no step ran.</summary>
    public string PlayerLines(double measuredSeconds)
    {
        if (Steps == 0)
        {
            return string.Empty;
        }

        double steps = Steps;

        return
            $"{PlayerStep.Line("step ms")}   (collider solve, timed directly)\n"
            + $"player    {Steps} steps in {measuredSeconds:F1} s, "
            + $"{Substeps / steps:F2} substeps and {VoxelQueries / steps:F1} voxel reads per step\n"
            + $"held      {StepsHeld} step(s) on unloaded world "
            + $"({StepsHeld / steps * 100.0:F2}%), longest run {LongestHold} "
            + $"({LongestHold / 60.0 * 1000.0:F0} ms at 60 Hz)\n"
            + $"contact   {GroundFlickers} ground-state flicker(s) over {RestingSteps} "
            + "resting step(s)"
            + (RestingSteps == 0 ? "   (nothing rested — the detector did not run)" : string.Empty)
            + "\n";
    }

    /// <summary>Seconds of settling to discard before measuring.</summary>
    /// <remarks>
    /// Without it the report is dominated by the initial fill — a thousand chunks generated
    /// at once — which is a real cost but a different one from steady-state streaming, and
    /// averaging the two together describes neither.
    /// </remarks>
    public double WarmupSeconds { get; init; } = 3.0;

    /// <summary>Seconds to measure after warmup.</summary>
    public double MeasureSeconds { get; init; } = 15.0;

    public bool Warm { get; private set; }

    public bool Finished { get; private set; }

    /// <summary>Seconds actually measured, for turning totals into rates.</summary>
    /// <remarks>
    /// <b>The right denominator for anything sampled only while <see cref="Warm"/>, and the
    /// wrong one for a lifetime counter</b> — see <see cref="RunSeconds"/>.
    /// </remarks>
    public double MeasuredSeconds { get; private set; }

    /// <summary>
    /// Seconds the counters have actually been counting for: the warm-up plus the window.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Found 2026-09-03, and it had been quietly inflating every per-second figure whose
    /// numerator is a counter on the pipeline rather than a sample on the profiler.</b> The
    /// histograms — <c>update ms</c>, <c>swing ms</c>, <c>light ms</c> — are only fed while
    /// <see cref="Warm"/>, so <see cref="MeasuredSeconds"/> is exactly right for them. But
    /// <c>EditRemeshes</c>, <c>LightCellsVisited</c> and the water totals start at zero when the
    /// world opens and keep counting through the warm-up, so dividing them by the window
    /// overstates the rate by <c>(warmup + measure) / measure</c> — <b>10% at the 30 s windows
    /// this project measures with, and 20% at the 15 s default.</b>
    /// </para>
    /// <para>
    /// <b>Per-swing and per-chunk figures were never affected</b>, because there both the
    /// numerator and the denominator are lifetime counts and the warm-up cancels. So the
    /// recorded conclusions stand — <b>the entangled miner's "1.98x" is a ratio of two figures
    /// inflated identically</b> — and it is the absolute rates in the worklog (<c>63.9</c>
    /// remeshes/s, <c>54,514</c> cells/s) that were about a tenth high.
    /// </para>
    /// <para>
    /// Found by an arithmetic disagreement rather than by an assertion: a chunk bite on a 0.25 s
    /// cooldown reported <b>4.33 swings a second against a nominal 4.0</b>, and a cooldown that
    /// is <em>reset</em> rather than decremented can only ever deliver <em>fewer</em> swings than
    /// nominal. A rate above its own ceiling has to be a denominator.
    /// </para>
    /// </remarks>
    public double RunSeconds => WarmupSeconds + MeasuredSeconds;

    /// <summary>Advances the clock. Returns true on the frame the window closes.</summary>
    public bool Tick(double delta)
    {
        if (Finished)
        {
            return false;
        }

        _elapsed += delta;

        if (!Warm)
        {
            if (_elapsed < WarmupSeconds)
            {
                return false;
            }

            Warm = true;
            _elapsed = 0.0;
            return false;
        }

        Frame.Add(delta * 1000.0);
        MeasuredSeconds = _elapsed;

        if (_elapsed >= MeasureSeconds)
        {
            Finished = true;
            return true;
        }

        return false;
    }

    public static string MemoryLine()
    {
        double staticMiB = OS.GetStaticMemoryUsage() / (1024.0 * 1024.0);
        return $"memory    {staticMiB:F1} MiB static";
    }

    /// <summary>
    /// Draw calls and primitives, which are zero headless because there is no renderer.
    /// </summary>
    /// <remarks>
    /// This is the pair that answers M1d's deferred question about meshing 2×2×2 chunks into
    /// one surface: that trade only pays if draw calls, rather than primitives, are what is
    /// costing.
    /// </remarks>
    public static string RenderLine()
    {
        double drawCalls = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
        double primitives = Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame);

        return drawCalls <= 0.0 && primitives <= 0.0
            ? "render    (headless — no renderer; the gap to a windowed run is the render cost)"
            : $"render    {drawCalls:F0} draw calls, {primitives:F0} primitives";
    }
}
