using System;
using System.Collections.Generic;
using Godot;

namespace MinerCraft.Game.Diagnostics;

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
    public double MeasuredSeconds { get; private set; }

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
