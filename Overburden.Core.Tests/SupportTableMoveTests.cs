using Overburden.Core.Fluid;
using Overburden.Core.Structure;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// M9d, decisions 1 and 2: the water table moving over a settled roof is a trigger, read as a
/// comparison at the boundary rather than a re-derive of everything; and a collapse timer only
/// shortens when the deficit under it grows.
/// </summary>
public class SupportTableMoveTests
{
    private const int Air = SupportTestWorld.Air;
    private const int Stone = SupportTestWorld.Stone;
    private const int Rubble = SupportTestWorld.Rubble;
    private const int K = SupportRule.MaxBudget;

    private static readonly ChunkPos[] TwoByTwo = [new(0, 0, 0), new(1, 0, 0), new(0, 0, 1), new(1, 0, 1)];

    /// <summary>A lake whose level can be moved between sweeps, flat or over one chunk column only.</summary>
    private sealed class MovingTable : IWaterTable
    {
        public double? Level { get; set; }

        /// <summary>When set, only the 2 m samples of this chunk column read the level; every other sample is dry.</summary>
        public ChunkPos? OnlyOver { get; set; }

        public int Asked { get; private set; }

        public double? TableAt(int sampleX, int sampleZ)
        {
            Asked++;
            if (OnlyOver is { } column)
            {
                int minX = column.OriginVoxel.X / SupportCostTable.CellVoxels;
                int minZ = column.OriginVoxel.Z / SupportCostTable.CellVoxels;
                bool inside = sampleX >= minX && sampleX < minX + SupportCostTable.SamplesPerAxis
                    && sampleZ >= minZ && sampleZ < minZ + SupportCostTable.SamplesPerAxis;
                if (!inside)
                {
                    return null;
                }
            }

            return Level;
        }
    }

    private static SupportTestWorld Room()
    {
        var world = new SupportTestWorld();
        world.FillBox(new ChunkPos(-2, -2, -2), new ChunkPos(3, 2, 3), Stone);
        foreach (ChunkPos bite in TwoByTwo)
        {
            world.Fill(bite, Air);
        }

        return world;
    }

    private static SupportRegion Settled(SupportTestWorld world, IWaterTable? table)
    {
        SupportRegion region = world.NewRegion(table: table);
        foreach (ChunkPos chunk in TwoByTwo)
        {
            region.MarkDug(chunk);
        }

        region.Settle();
        Assert.Empty(region.VerifyFixpoint());
        return region;
    }

    private static void AssertSameFields(SupportRegion actual, SupportRegion expected)
    {
        Assert.Equal(expected.Analysed.Count, actual.Analysed.Count);
        foreach (ChunkPos chunk in expected.Analysed)
        {
            ChunkSupport? want = expected.FieldAt(chunk);
            ChunkSupport? got = actual.FieldAt(chunk);
            Assert.Equal(want is null, got is null);
            if (want is not null)
            {
                Assert.Equal(want.Snapshot(), got!.Snapshot());
            }
        }
    }

    // ------------------------------------------------------------------ the trigger

    [Fact]
    public void ARisingLakeRequeuesTheChunksUnderItAndTheJoinDoesTheRest()
    {
        // The lake stands over one chunk column of the room only, below the roof to start — dry
        // for every chunk — then rises to 10 m over the roof. Exactly the three analysed chunks of
        // that column read a moved table: the roof, the bite and the floor. Their re-derives move
        // face layers, and the join queues the neighbours the ordinary way.
        SupportTestWorld world = Room();
        var table = new MovingTable { Level = -100, OnlyOver = new ChunkPos(0, 0, 0) };
        SupportRegion region = Settled(world, table);
        Assert.Equal(20, region.AtRiskVoxels);

        table.Level = 72;
        int askedBefore = table.Asked;
        int requeued = region.CheckTable();

        Assert.Equal(3, requeued);
        Assert.Equal(3, region.Pending);
        Assert.Equal(3, region.Triggered(SupportTrigger.Table));
        Assert.Equal(1, region.TableChecks);
        Assert.Equal(region.Analysed.Count, region.TableCompared);
        Assert.Equal(3, region.TableRebuilt);
        Assert.Equal(0, region.TableUnchanged);

        // The comparison asked the world exactly once per distinct sample under the analysed set
        // — chunks stacked in a column share theirs, and the three rebuilt tables asked nothing
        // more: the memo. (Without it the count is near analysed × 16 plus 528 a rebuild.)
        int asked = table.Asked - askedBefore;
        int columns = region.Analysed.Select(c => (c.X, c.Z)).Distinct().Count();
        Assert.Equal(columns * SupportCostTable.Samples, asked);

        region.Settle();
        Assert.Empty(region.VerifyFixpoint());
        Assert.True(region.AtRiskVoxels > 20, $"at risk {region.AtRiskVoxels}");

        // The oracle: the trigger's result is the from-scratch derive at the new table, field
        // for field — incremental equals from-scratch, decision 4's assertion about the join.
        AssertSameFields(region, Settled(Room(), new MovingTable { Level = 72, OnlyOver = new ChunkPos(0, 0, 0) }));

        // Nothing moved since: the sweep compares and rebuilds nothing.
        Assert.Equal(0, region.CheckTable());
        Assert.Equal(3, region.TableRebuilt);
        Assert.Equal(0, region.Pending);
    }

    [Fact]
    public void ALakeMovingUnderAVoxelRequeuesNothing()
    {
        SupportTestWorld world = Room();
        var table = new MovingTable { Level = 72 };
        SupportRegion region = Settled(world, table);

        table.Level = 72.9;
        Assert.Equal(0, region.CheckTable());
        Assert.Equal(0, region.TableRebuilt);
        Assert.Equal(0, region.Pending);

        table.Level = 71.1;
        Assert.Equal(0, region.CheckTable());
        Assert.Equal(0, region.TableRebuilt);
    }

    [Fact]
    public void ALakeRisingBelowEveryChunkRequeuesNothing()
    {
        // A reading is the water over the chunk's bottom row, so a lake that rises from far
        // below to still below reads zero both times — the cost table could not tell either.
        SupportTestWorld world = Room();
        var table = new MovingTable { Level = -200 };
        SupportRegion region = Settled(world, table);

        table.Level = -100;
        Assert.Equal(0, region.CheckTable());
        Assert.Equal(0, region.TableRebuilt);
        Assert.Equal(region.Analysed.Count, region.TableCompared);
    }

    [Fact]
    public void ALakeRisingPastTheCurvesLastPointRebuildsAndRequeuesNothing()
    {
        // Stone's curve ends at 10 m of head; a lake 26 m over the highest row already puts every
        // voxel past it, and ten voxels more moves every reading and no cost. The second stage:
        // the table is rebuilt, its bytes match, the chunk is not queued — and the readings are
        // re-recorded, or the next sweep would rebuild it again for the same nothing.
        SupportTestWorld world = Room();
        var table = new MovingTable { Level = 200 };
        SupportRegion region = Settled(world, table);
        int analysed = region.Analysed.Count;

        table.Level = 210;
        Assert.Equal(0, region.CheckTable());
        Assert.Equal(analysed, region.TableRebuilt);
        Assert.Equal(analysed, region.TableUnchanged);
        Assert.Equal(0, region.Pending);

        Assert.Equal(0, region.CheckTable());
        Assert.Equal(analysed, region.TableRebuilt);
    }

    [Fact]
    public void AChunkAlreadyQueuedOrInFlightIsLeftToItsOwnDerive()
    {
        // The queued chunk reads the live table when it derives; queueing it again under Table
        // would be a second entry for one derive. The sweep skips it and counts the others.
        SupportTestWorld world = Room();
        var table = new MovingTable { Level = -100 };
        SupportRegion region = Settled(world, table);
        ChunkPos roof = new(0, 1, 0);
        region.Edited(roof);
        int pending = region.Pending;
        Assert.True(pending >= 1);

        table.Level = 72;
        int requeued = region.CheckTable();

        Assert.False(region.Triggered(SupportTrigger.Table) > requeued);
        Assert.Equal(pending + requeued, region.Pending);
        Assert.Equal(region.Analysed.Count - pending, region.TableCompared);

        // In flight: take the roof's request and hold it while the table moves again.
        region.Settle();
        Assert.Empty(region.VerifyFixpoint());
        region.Edited(roof);
        Assert.True(region.TryTakeNext(out SupportDeriveRequest request));
        Assert.Equal(roof, request.Chunk);
        table.Level = 200;
        long before = region.Triggered(SupportTrigger.Table);
        region.CheckTable();
        Assert.Equal(1, region.InFlight);
        region.Abandoned(roof, requeue: true);
        region.Settle();
        Assert.Empty(region.VerifyFixpoint());
        // The from-scratch twin has the roof dug too, since the edit marked it so.
        SupportRegion twin = Room().NewRegion(table: new MovingTable { Level = 200 });
        foreach (ChunkPos chunk in TwoByTwo) twin.MarkDug(chunk);
        twin.MarkDug(roof);
        twin.Settle();
        AssertSameFields(region, twin);
        Assert.True(region.Triggered(SupportTrigger.Table) >= before);
    }

    [Fact]
    public void ADrainingLakeRequeuesTooAndTheRoofRecovers()
    {
        // The other direction: a lake drained through a breach lowers the head, the roof
        // re-derives and sheds less — the trigger is a move, not a rise.
        SupportTestWorld world = Room();
        var table = new MovingTable { Level = 72 };
        SupportRegion region = Settled(world, table);
        int wet = region.AtRiskVoxels;
        Assert.True(wet > 20);

        table.Level = -100;
        Assert.True(region.CheckTable() > 0);
        region.Settle();
        Assert.Empty(region.VerifyFixpoint());
        Assert.Equal(20, region.AtRiskVoxels);
        AssertSameFields(region, Settled(Room(), new MovingTable { Level = -100 }));
    }

    [Fact]
    public void NoTableMeansNoSweep()
    {
        SupportTestWorld world = Room();
        SupportRegion region = Settled(world, table: null);
        Assert.Equal(0, region.CheckTable());
        Assert.Equal(0, region.TableChecks);
    }

    [Fact]
    public void AWindowMoveThatKeepsEveryLevelRequeuesNothing()
    {
        // The readings are per world sample; a re-analysis that renumbers every basin but leaves
        // the levels where they were is not a move. Modelled as a new table object with the same
        // answers — the region holds no basin index to go stale.
        SupportTestWorld world = Room();
        var relay = new Relay { Current = new MovingTable { Level = 72 } };
        SupportRegion region = world.NewRegion(table: relay);
        foreach (ChunkPos chunk in TwoByTwo) region.MarkDug(chunk);
        region.Settle();

        relay.Current = new MovingTable { Level = 72 };
        Assert.Equal(0, region.CheckTable());
        Assert.Equal(0, region.TableRebuilt);
    }

    private sealed class Relay : IWaterTable
    {
        public MovingTable Current { get; set; } = null!;

        public double? TableAt(int sampleX, int sampleZ) => Current.TableAt(sampleX, sampleZ);
    }

    // ------------------------------------------------------------------ the timer

    [Fact]
    public void ATimerShortensWhenTheLakeRaisesTheDeficitAndNeverLengthens()
    {
        // The plug armed dry — deficit 8, a mean near the window's top — then the lake rises to
        // 10 m over the roof and the deficit goes to K. The timer is redrawn about the minimum and
        // keeps the earlier due; when the lake drains again the deficit falls and the due stays.
        var window = new CollapseWindow { MinSeconds = 2, MaxSeconds = 60 };
        ChunkPos roof = new(0, 1, 0);

        SupportTestWorld world = null!;
        MovingTable table = null!;
        SupportRegion region = null!;
        CollapseScheduler scheduler = null!;
        CollapseTimer dry = default, wet = default;
        bool shortened = false;
        for (ulong seed = 0; seed < 64 && !shortened; seed++)
        {
            world = Room();
            table = new MovingTable { Level = -100 };
            region = Settled(world, table);
            var editor = new SupportTestWorld.Editor(world, region);
            scheduler = new CollapseScheduler(region, editor, world.Materials, Rubble, seed, window) { FireEnabled = false };
            scheduler.Sweep(10_000);
            Assert.True(scheduler.TryGetTimer(roof, out dry));
            Assert.Equal(8, dry.Deficit);

            table.Level = 72;
            Assert.True(region.CheckTable() > 0);
            region.Settle();
            Assert.Empty(region.VerifyFixpoint());
            Assert.Equal(K, region.FieldAt(roof)!.MaxDeficit);

            scheduler.Sweep(10_500);
            Assert.True(scheduler.TryGetTimer(roof, out wet));
            Assert.Equal(K, wet.Deficit);
            Assert.True(wet.DueAtMillis <= dry.DueAtMillis, $"seed {seed}: wet due {wet.DueAtMillis} after dry {dry.DueAtMillis}");
            shortened = wet.DueAtMillis < dry.DueAtMillis;
        }

        Assert.True(shortened, "no seed in 64 drew the wet timer earlier than the dry one");
        // Four roofs over the 2 × 2, each armed dry and each judged; this one was shortened.
        long shortenedCount = scheduler.Shortened;
        Assert.InRange(shortenedCount, 1, 4);
        Assert.Equal(10_500, wet.ArmedAtMillis);

        // A second sweep at the same deficit redraws nothing.
        scheduler.Sweep(11_000);
        Assert.True(scheduler.TryGetTimer(roof, out CollapseTimer again));
        Assert.Equal(wet, again);
        Assert.Equal(shortenedCount, scheduler.Shortened);

        // The lake drains: the deficit falls back to 8 and the due it had stays.
        table.Level = -100;
        Assert.True(region.CheckTable() > 0);
        region.Settle();
        Assert.Equal(8, region.FieldAt(roof)!.MaxDeficit);
        scheduler.Sweep(11_500);
        Assert.True(scheduler.TryGetTimer(roof, out CollapseTimer drained));
        Assert.Equal(wet.DueAtMillis, drained.DueAtMillis);
        Assert.Equal(shortenedCount, scheduler.Shortened);
    }

    [Fact]
    public void ALaterRedrawKeepsTheOldDueAndRecordsTheDeficit()
    {
        // The other branch: a seed under which the wet draw lands after the dry due keeps the dry
        // due — a timer never lengthens — and records the new deficit so the next sweep does not
        // draw again.
        var window = new CollapseWindow { MinSeconds = 2, MaxSeconds = 60 };
        ChunkPos roof = new(0, 1, 0);
        bool kept = false;
        for (ulong seed = 0; seed < 256 && !kept; seed++)
        {
            SupportTestWorld world = Room();
            var table = new MovingTable { Level = -100 };
            SupportRegion region = Settled(world, table);
            var editor = new SupportTestWorld.Editor(world, region);
            var scheduler = new CollapseScheduler(region, editor, world.Materials, Rubble, seed, window) { FireEnabled = false };
            scheduler.Sweep(10_000);
            Assert.True(scheduler.TryGetTimer(roof, out CollapseTimer dry));

            table.Level = 72;
            region.CheckTable();
            region.Settle();
            scheduler.Sweep(10_500);
            Assert.True(scheduler.TryGetTimer(roof, out CollapseTimer wet));
            Assert.Equal(K, wet.Deficit);
            if (wet.DueAtMillis == dry.DueAtMillis)
            {
                kept = true;
                // The other three roofs may have shortened; this one did not.
                Assert.InRange(scheduler.Shortened, 0, 3);
                Assert.Equal(dry.ArmedAtMillis, wet.ArmedAtMillis);
                scheduler.Sweep(11_000);
                Assert.True(scheduler.TryGetTimer(roof, out CollapseTimer again));
                Assert.Equal(wet, again);
            }
        }

        Assert.True(kept, "no seed in 256 drew the wet timer after the dry due");
    }
}
