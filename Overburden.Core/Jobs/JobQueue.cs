using System.Diagnostics;

namespace Overburden.Core.Jobs;

/// <summary>
/// The pending-job list: a flat array popped from the back and re-sorted at most once per
/// interval, by whichever worker takes next once the interval has elapsed. Not a timer —
/// measured 2026-09-06 at 4.6–5.0 sweeps a second in every arm, because idle workers poll
/// every 100 ms; see <see cref="SweepStatistics"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not a FIFO and not a heap</b>, and both of those were considered and rejected — see
/// the "Rejected alternatives" section of docs/DESIGN.md.
/// <c>System.Threading.Channels</c> is FIFO, so it can neither reprioritise queued work as
/// the player moves nor drop work that went stale before it ran. A binary heap fixes
/// neither: it orders correctly at insert time, and insert time is exactly when the
/// ordering is least valuable, because a chunk queued at 20 m may be 200 m away before a
/// worker frees up.
/// </para>
/// <para>
/// So: keep the entries in a plain list and re-sort them periodically. One sweep
/// recomputes every priority, drops everything whose cancellation token has fired, and
/// sorts — all three in a single pass, because they all want to touch every entry anyway.
/// Sorting a few thousand entries five times a second costs nothing next to generating
/// one chunk.
/// </para>
/// <para>
/// <b>Ordering is established by the sweep, not by <see cref="Enqueue"/>.</b> Entries
/// added since the last sweep sit at the end of the list in arrival order, so a queue that
/// has not yet been swept pops newest-first. That is deliberate — sorting on insert is the
/// cost this design exists to avoid — and it is harmless in practice, because the most
/// recently queued chunk is usually the nearest one, and the next sweep corrects it
/// regardless. Tests that care about priority order must sweep first.
/// </para>
/// </remarks>
public sealed class JobQueue
{
    /// <summary>A queued job and everything the queue knows about it.</summary>
    public readonly record struct Entry(
        IJob Job,
        CancellationToken Cancellation,
        bool Serial,
        JobPriority Priority);

    private static readonly Comparison<Entry> ByAscendingPriority =
        static (a, b) => a.Priority.CompareTo(b.Priority);

    private readonly object _gate = new();
    private readonly List<Entry> _entries = [];
    private readonly ViewerSet _viewers;
    private readonly long _sweepIntervalMillis;
    private readonly Func<long> _nowMillis;
    private readonly Action<Entry> _onDropped;

    private long _lastSweepMillis;
    private bool _serialRunning;

    // The sweep's own account of itself; see SweepStatistics for why it is kept. All written
    // under _gate, in the sweep or the take that could have swept, so a snapshot is consistent.
    private long _sweeps;
    private long _forcedSweeps;
    private long _emptySweeps;
    private long _entriesReevaluated;
    private long _entriesDropped;
    private long _mostEntriesInOneSweep;
    private long _takeCalls;
    private long _takesThatFoundWork;
    private double _sweepMillisTotal;
    private double _sweepMillisMax;
    private long _gapMillisTotal;
    private long _gapMillisMax;

    /// <param name="viewers">Read during each sweep to recompute distance-based priority.</param>
    /// <param name="sweepInterval">
    /// How often priorities are recomputed. godot_voxel uses 200 ms; the cost is a full
    /// pass plus a sort, so this trades scheduling accuracy against CPU directly.
    /// </param>
    /// <param name="onDropped">
    /// Invoked for every entry the sweep removes. This is how a cancelled job still reports
    /// a result — see <see cref="JobResult"/>. Called outside the lock.
    /// </param>
    /// <param name="nowMillis">Injectable clock. Tests drive sweeps deterministically.</param>
    public JobQueue(
        ViewerSet viewers,
        TimeSpan sweepInterval,
        Action<Entry> onDropped,
        Func<long>? nowMillis = null)
    {
        _viewers = viewers;
        _sweepIntervalMillis = (long)sweepInterval.TotalMilliseconds;
        _onDropped = onDropped;
        _nowMillis = nowMillis ?? (static () => Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency);
        _lastSweepMillis = _nowMillis();
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>
    /// What the sweeps have done so far. Any thread; one consistent picture, taken under the lock.
    /// </summary>
    public SweepStatistics Statistics
    {
        get
        {
            lock (_gate)
            {
                return new SweepStatistics(
                    _sweeps,
                    _forcedSweeps,
                    _emptySweeps,
                    _entriesReevaluated,
                    _entriesDropped,
                    _mostEntriesInOneSweep,
                    _takeCalls,
                    _takesThatFoundWork,
                    _sweepMillisTotal,
                    _sweepMillisMax,
                    _gapMillisTotal,
                    _gapMillisMax);
            }
        }
    }

    public void Enqueue(IJob job, CancellationToken cancellation = default, bool serial = false)
    {
        var entry = new Entry(job, cancellation, serial, JobPriority.Evaluate(job.Kind, job.Position, _viewers, job.Urgency));
        lock (_gate)
        {
            _entries.Add(entry);
        }
    }

    /// <summary>Adds a whole frame's worth of jobs while taking the lock once.</summary>
    /// <remarks>
    /// Taking the lock per chunk is the obvious version and is measurably worse when a
    /// viewer crossing a chunk boundary submits several hundred jobs at once.
    /// </remarks>
    public void EnqueueBatch(ReadOnlySpan<Entry> entries)
    {
        if (entries.IsEmpty)
        {
            return;
        }

        lock (_gate)
        {
            foreach (Entry entry in entries)
            {
                _entries.Add(entry);
            }
        }
    }

    /// <summary>Builds an entry with its priority evaluated, for batching.</summary>
    public Entry MakeEntry(IJob job, CancellationToken cancellation = default, bool serial = false) =>
        new(job, cancellation, serial, JobPriority.Evaluate(job.Kind, job.Position, _viewers, job.Urgency));

    /// <summary>
    /// Takes the highest-priority runnable entry, sweeping first if one is due.
    /// </summary>
    /// <remarks>
    /// The sweep lives here rather than on its own thread because the lock is already held
    /// at exactly the right moment, and a queue nobody is taking from does not need its
    /// priorities to be fresh.
    /// </remarks>
    public bool TryTake(out Entry taken)
    {
        List<Entry>? dropped;
        bool found = false;
        taken = default;

        lock (_gate)
        {
            dropped = SweepIfDueLocked();
            _takeCalls++;

            // Highest priority sits at the end. Serial entries are skipped while another
            // serial entry runs, so this is not always the very last element — but serial
            // work is rare, so it nearly always is.
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                Entry entry = _entries[i];
                if (entry.Serial && _serialRunning)
                {
                    continue;
                }

                _entries.RemoveAt(i);
                if (entry.Serial)
                {
                    _serialRunning = true;
                }

                taken = entry;
                found = true;
                _takesThatFoundWork++;
                break;
            }
        }

        ReportDropped(dropped);
        return found;
    }

    /// <summary>Marks the serial lane free again. Call after running a serial entry.</summary>
    public void ReleaseSerial()
    {
        lock (_gate)
        {
            _serialRunning = false;
        }
    }

    /// <summary>
    /// Recomputes priorities, removes cancelled entries, and re-sorts. Public so tests can
    /// drive it without waiting on a clock.
    /// </summary>
    public void Sweep()
    {
        List<Entry>? dropped;
        lock (_gate)
        {
            dropped = SweepLocked(forced: true);
        }

        ReportDropped(dropped);
    }

    /// <summary>
    /// Empties the queue, reporting every remaining entry as dropped. Used at shutdown, so
    /// that jobs still queued when the scheduler stops still report their one result.
    /// </summary>
    public void DropAll()
    {
        List<Entry> dropped;
        lock (_gate)
        {
            dropped = [.. _entries];
            _entries.Clear();
        }

        ReportDropped(dropped);
    }

    private List<Entry>? SweepIfDueLocked()
    {
        long now = _nowMillis();
        if (now - _lastSweepMillis < _sweepIntervalMillis)
        {
            return null;
        }

        return SweepLocked(forced: false);
    }

    private List<Entry>? SweepLocked(bool forced)
    {
        // The gap is on the queue's own clock, so a test can read it; the duration is on the
        // Stopwatch, because the queue's clock is whole milliseconds and a sweep is usually less.
        long now = _nowMillis();
        long gap = now - _lastSweepMillis;
        _lastSweepMillis = now;
        long started = Stopwatch.GetTimestamp();

        int seen = _entries.Count;
        int reevaluated = 0;
        List<Entry>? dropped = null;

        // Walk backwards so removal does not disturb the indices still to be visited.
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            Entry entry = _entries[i];

            if (entry.Cancellation.IsCancellationRequested)
            {
                (dropped ??= []).Add(entry);
                _entries.RemoveAt(i);
                continue;
            }

            _entries[i] = entry with
            {
                Priority = JobPriority.Evaluate(
                    entry.Job.Kind, entry.Job.Position, _viewers, entry.Job.Urgency),
            };
            reevaluated++;
        }

        _entries.Sort(ByAscendingPriority);

        _sweeps++;
        if (forced)
        {
            _forcedSweeps++;
        }

        if (seen == 0)
        {
            _emptySweeps++;
        }

        _entriesReevaluated += reevaluated;
        _entriesDropped += dropped?.Count ?? 0;
        _mostEntriesInOneSweep = Math.Max(_mostEntriesInOneSweep, seen);
        _gapMillisTotal += gap;
        _gapMillisMax = Math.Max(_gapMillisMax, gap);

        double millis = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
        _sweepMillisTotal += millis;
        _sweepMillisMax = Math.Max(_sweepMillisMax, millis);
        return dropped;
    }

    /// <summary>
    /// Fires the dropped callback outside the lock. Calling user code while holding it is
    /// how a scheduler acquires a deadlock it cannot reproduce.
    /// </summary>
    private void ReportDropped(List<Entry>? dropped)
    {
        if (dropped is null)
        {
            return;
        }

        foreach (Entry entry in dropped)
        {
            _onDropped(entry);
        }
    }
}
