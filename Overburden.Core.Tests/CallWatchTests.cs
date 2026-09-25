using System.Diagnostics;
using Overburden.Core.Diagnostics;

namespace Overburden.Core.Tests;

/// <summary>
/// The stopwatch behind Matthew's table of watched functions. Its rows are what a comparison of two
/// branches is read off, so the extremes and the mean are asserted exactly on recorded ticks, and
/// off is asserted to record nothing. One class, so its tests never run beside each other on the
/// shared flag.
/// </summary>
public class CallWatchTests
{
    private static WatchRow Row(string name) => CallWatch.Rows().Single(r => r.Name == name);

    [Fact]
    public void ARowHoldsTheCallsAndTheLeastGreatestAndMeanOfWhatWasRecorded()
    {
        int id = CallWatch.Register("test: three calls");
        CallWatch.Reset();
        long tick = Stopwatch.Frequency / 1000; // a millisecond
        CallWatch.Record(id, 2 * tick);
        CallWatch.Record(id, 7 * tick);
        CallWatch.Record(id, 3 * tick);

        WatchRow row = Row("test: three calls");
        Assert.Equal(3, row.Calls);
        Assert.Equal(2.0, row.MinMs, 6);
        Assert.Equal(7.0, row.MaxMs, 6);
        Assert.Equal(4.0, row.MeanMs, 6);
        Assert.Equal(12.0, row.TotalMs, 6);
    }

    [Fact]
    public void OffRecordsNothingAndOnRecordsEveryScope()
    {
        int id = CallWatch.Register("test: scopes");
        bool was = CallWatch.Enabled;
        try
        {
            CallWatch.Enabled = false;
            CallWatch.Reset();
            using (CallWatch.Time(id))
            {
            }

            Assert.Equal(0, Row("test: scopes").Calls);

            CallWatch.Enabled = true;
            for (int i = 0; i < 5; i++)
            {
                using (CallWatch.Time(id))
                {
                }
            }

            WatchRow row = Row("test: scopes");
            Assert.Equal(5, row.Calls);
            Assert.True(row.MinMs <= row.MeanMs && row.MeanMs <= row.MaxMs, $"{row.MinMs} / {row.MeanMs} / {row.MaxMs}");
        }
        finally
        {
            CallWatch.Enabled = was;
        }
    }

    [Fact]
    public void AResetForgetsTheCallsAndTheExtremesAndKeepsTheName()
    {
        int id = CallWatch.Register("test: reset");
        CallWatch.Record(id, 1_000_000);
        CallWatch.Reset();
        CallWatch.Record(id, 10);

        WatchRow row = Row("test: reset");
        Assert.Equal(1, row.Calls);
        Assert.Equal(row.MinMs, row.MaxMs);
        Assert.Equal(id, CallWatch.Register("test: reset"));
    }

    [Fact]
    public void RecordsFromManyThreadsAreAllCountedAndTheExtremesAreTheTrueOnes()
    {
        int id = CallWatch.Register("test: threads");
        CallWatch.Reset();
        Parallel.For(1, 20_001, i => CallWatch.Record(id, i));

        WatchRow row = Row("test: threads");
        double toMs = 1000.0 / Stopwatch.Frequency;
        Assert.Equal(20_000, row.Calls);
        Assert.Equal(1 * toMs, row.MinMs, 9);
        Assert.Equal(20_000 * toMs, row.MaxMs, 9);
        Assert.Equal(10_000.5 * toMs, row.MeanMs, 9);
    }
}
