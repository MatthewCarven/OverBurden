using System.Diagnostics;
using System.Text;

namespace Overburden.Core.Diagnostics;

/// <summary>
/// A stopwatch round named functions: calls, and the least, greatest and mean time a call took
/// (2026-09-26, Matthew's ask — <i>"a table of watched functions … minimum, maximum and average
/// call times"</i>). Off unless <see cref="Enabled"/> is set (<c>--watch</c> in the game), and
/// then one flag read a call.
/// </summary>
/// <remarks>
/// <para>
/// <b>A call's time is wall time on the thread that made it, inclusive of what it calls</b> — a
/// worker descheduled mid-call counts the gap, and a nested watch is inside its parent's figure.
/// So a row is what one call cost the thread waiting on it, not CPU; the CPU share is the
/// sampling profiler's column, and the two answer different questions.
/// </para>
/// <para>
/// <b>Only functions a call of which is worth a stopwatch are watched.</b> A timestamp is ~20–30
/// ns a pair here; the flood's inner reads (<c>Find</c>, <c>Pop</c>, the index) are tens of
/// nanoseconds and millions a pass, so watching them would time the stopwatch. Those are the
/// sampler's.
/// </para>
/// <para>
/// Safe from any thread: counts and sums by <see cref="Interlocked"/>, the extremes by compare and
/// swap. A <see cref="Reset"/> racing a record can lose that one call, which is the price of no
/// lock on the path.
/// </para>
/// </remarks>
public static class CallWatch
{
    private const int MaxIds = 64;

    private static readonly string[] Names = new string[MaxIds];
    private static readonly long[] Calls = new long[MaxIds];
    private static readonly long[] Total = new long[MaxIds];
    private static readonly long[] Least = CreateLeast();
    private static readonly long[] Most = new long[MaxIds];
    private static readonly object Gate = new();
    private static int _registered;

    /// <summary>Set once at startup, before anything is watched. Off costs one read a call.</summary>
    public static bool Enabled { get; set; }

    /// <summary>Names a watch; the same name twice is the same row.</summary>
    public static int Register(string name)
    {
        lock (Gate)
        {
            for (int i = 0; i < _registered; i++)
            {
                if (Names[i] == name)
                {
                    return i;
                }
            }

            if (_registered == MaxIds)
            {
                throw new InvalidOperationException($"CallWatch holds {MaxIds} names; '{name}' is one too many.");
            }

            Names[_registered] = name;
            return _registered++;
        }
    }

    /// <summary><c>using var _ = CallWatch.Time(id);</c> — nothing at all while off.</summary>
    public static Scope Time(int id) => Enabled ? new Scope(id, Stopwatch.GetTimestamp()) : default;

    public static void Record(int id, long ticks)
    {
        Interlocked.Increment(ref Calls[id]);
        Interlocked.Add(ref Total[id], ticks);
        long seen = Volatile.Read(ref Least[id]);
        while (ticks < seen)
        {
            long was = Interlocked.CompareExchange(ref Least[id], ticks, seen);
            if (was == seen)
            {
                break;
            }

            seen = was;
        }

        seen = Volatile.Read(ref Most[id]);
        while (ticks > seen)
        {
            long was = Interlocked.CompareExchange(ref Most[id], ticks, seen);
            if (was == seen)
            {
                break;
            }

            seen = was;
        }
    }

    /// <summary>Forgets every call so far, keeping the names — the start of a measured window.</summary>
    public static void Reset()
    {
        for (int i = 0; i < MaxIds; i++)
        {
            Interlocked.Exchange(ref Calls[i], 0);
            Interlocked.Exchange(ref Total[i], 0);
            Interlocked.Exchange(ref Least[i], long.MaxValue);
            Interlocked.Exchange(ref Most[i], 0);
        }
    }

    public static IReadOnlyList<WatchRow> Rows()
    {
        int count = Volatile.Read(ref _registered);
        var rows = new List<WatchRow>(count);
        double toMs = 1000.0 / Stopwatch.Frequency;
        for (int i = 0; i < count; i++)
        {
            long calls = Volatile.Read(ref Calls[i]);
            long total = Volatile.Read(ref Total[i]);
            rows.Add(calls == 0
                ? new WatchRow(Names[i], 0, 0, 0, 0, 0)
                : new WatchRow(Names[i], calls, Volatile.Read(ref Least[i]) * toMs, Volatile.Read(ref Most[i]) * toMs,
                    total * toMs / calls, total * toMs));
        }

        // By name, because registration order is whichever class the run touched first.
        rows.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return rows;
    }

    /// <summary>One line a watch, in registration order, each with the window's seconds for a rate.</summary>
    public static string Table(double seconds)
    {
        var text = new StringBuilder();
        text.Append($"watch     {seconds:F1} s of run — calls, min / mean / max ms a call, total ms and ms a second (wall, inclusive)\n");
        foreach (WatchRow r in Rows())
        {
            text.Append(r.Calls == 0
                ? $"watch     {r.Name,-34} not called\n"
                : $"watch     {r.Name,-34} {r.Calls,9:N0}  {r.MinMs,9:F4} / {r.MeanMs,9:F4} / {r.MaxMs,9:F3}  {r.TotalMs,10:F1} ms  {r.TotalMs / seconds,8:F2} ms/s\n");
        }

        return text.ToString();
    }

    private static long[] CreateLeast()
    {
        var least = new long[MaxIds];
        Array.Fill(least, long.MaxValue);
        return least;
    }

    public readonly struct Scope : IDisposable
    {
        private readonly int _id;
        private readonly long _started;

        public Scope(int id, long started)
        {
            _id = id;
            _started = started;
        }

        public void Dispose()
        {
            if (_started != 0)
            {
                Record(_id, Stopwatch.GetTimestamp() - _started);
            }
        }
    }
}

public readonly record struct WatchRow(string Name, long Calls, double MinMs, double MaxMs, double MeanMs, double TotalMs);
