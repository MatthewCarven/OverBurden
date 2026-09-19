using Overburden.Core.Hydrology;
using Overburden.Core.Lighting;

namespace Overburden.Core.Tests;

/// <summary>
/// The session that holds standing water together: the window, the ledger, and the move between
/// them.
/// </summary>
/// <remarks>
/// <b>The headline is <c>WalkingAwayAndBackLosesNoWater</c>, and it is the same category as M5b's
/// incremental-equals-from-scratch and M7b's hour-in-one-call.</b> A <see cref="BasinWater"/> only
/// ever holds what is inside the analysed window, so every window move is a chance to lose a lake
/// — and the assertion and the feature are the same statement, because "the pond was still there
/// when I walked back" is exactly what the ledger exists to make true.
/// </remarks>
public class WaterSystemTests
{
    private const ulong Seed = 0x4D494E455243UL;

    /// <summary>A bowl deep enough to hold water, on ground that drains off the edges.</summary>
    /// <remarks>
    /// Analytic rather than a literal grid, because the window this exercises moves — so the
    /// terrain has to answer for coordinates no fixture author enumerated.
    /// </remarks>
    private static ISurfaceHeights Bowls() => new AnalyticSurface((x, z) =>
    {
        // Two pits on a plateau, far enough apart to stay separate basins.
        int cell = HeightField.DefaultCellVoxels;
        int sx = FloorDiv(x, cell);
        int sz = FloorDiv(z, cell);

        int depth = 0;
        depth += Pit(sx, sz, 0, 0);
        depth += Pit(sx, sz, 12, 8);

        return 200 - depth;

        static int Pit(int sx, int sz, int cx, int cz)
        {
            int dx = sx - cx;
            int dz = sz - cz;
            int distance = Math.Max(Math.Abs(dx), Math.Abs(dz));
            return distance <= 2 ? 6 - (distance * 2) : 0;
        }
    });

    private static int FloorDiv(int value, int divisor)
    {
        int quotient = value / divisor;
        return value % divisor != 0 && (value < 0) != (divisor < 0) ? quotient - 1 : quotient;
    }

    private static WaterSystem NewSystem(int pitch = 32, int rainfallScale = 1_000_000) =>
        new(Bowls(), Seed, new BasinLedger(), pitch, HeightField.DefaultCellVoxels, rainfallScale);

    /// <summary>Rains until the window holds water, and returns the time reached.</summary>
    private static long RainUntilWet(WaterSystem system, long stepMillis = 60_000, int steps = 200)
    {
        long time = system.TimeMillis;
        for (int step = 0; step < steps && system.StoredLitres == 0; step++)
        {
            time += stepMillis;
            system.Advance(time);
        }

        return time;
    }

    [Fact]
    public void WalkingAwayAndBackLosesNoWater()
    {
        // THE ORACLE. The water lives only in the analysed window, so a move is where a lake can
        // vanish; the ledger is what carries it. Walk far enough to re-analyse, then walk back,
        // and the basins have to hold what they held.
        WaterSystem system = NewSystem();
        system.Install(system.AnalyseFor(0, 0));

        long time = RainUntilWet(system);
        Assert.True(system.StoredLitres > 0, "nothing filled, so the test compares two empty worlds");

        long before = system.StoredLitres;

        // M8b-2a: a removal rides the same absorb, with nothing new to store. Take half of the
        // wettest basin through its floor before walking, and the world has to come back holding
        // exactly what was left — a ledger that re-seeded from a stale figure would refill it.
        int wettest = 0;
        for (int basin = 1; basin < system.Window!.Watershed.Basins.Count; basin++)
        {
            if (system.Water!.StoredIn(basin) > system.Water.StoredIn(wettest)) wettest = basin;
        }

        long removed = system.Water!.Remove(wettest, Math.Max(1, system.Water.StoredIn(wettest) / 2));
        Assert.True(removed > 0, "the removal took nothing, so the walk carried the water it always did");
        before -= removed;

        // Far enough that the window origin actually changes, and back again.
        int far = system.SpanMetres * 4 * Overburden.Core.Voxels.VoxelConstants.VoxelsPerMetre;
        Assert.True(system.NeedsWindowFor(far, far), "the window did not move, so nothing was tested");

        system.Install(system.AnalyseFor(far, far));
        system.Advance(time);          // no time passes, so no rain falls
        system.Install(system.AnalyseFor(0, 0));

        Assert.Equal(before, system.StoredLitres);
    }

    [Fact]
    public void ABasinEmptiedThroughItsFloorLeavesTheLedger()
    {
        // The other half of "nothing new to store": a basin drained to nothing is absent from
        // the ledger rather than banked as zero, so a walk away and back re-seeds it with nothing
        // and the store never carries a row that means "no water here".
        WaterSystem system = NewSystem();
        system.Install(system.AnalyseFor(0, 0));
        RainUntilWet(system);

        Watershed watershed = system.Window!.Watershed;
        int wet = Enumerable.Range(0, watershed.Basins.Count)
            .First(basin => system.Water!.StoredIn(basin) > 0);
        Basin pit = watershed.Basins[wet];

        system.Bank();
        Assert.True(system.Ledger.LitresAt(pit.PitX, pit.PitZ) > 0, "the fixture banked nothing");
        int banked = system.Ledger.Count;

        // Everything its lake holds on the path through this basin, however much that is.
        long removed = system.Water!.Remove(wet, long.MaxValue / 2);
        Assert.True(removed > 0);
        Assert.Equal(0, system.Water.StoredIn(wet));

        system.Bank();
        Assert.Equal(0, system.Ledger.LitresAt(pit.PitX, pit.PitZ));
        Assert.Equal(banked - 1, system.Ledger.Count);
        Assert.Null(system.LevelAt(wet));
    }

    [Fact]
    public void ReceivingCountsOnItsOwnTermAndAnOutletReceiptLeavesTheWorld()
    {
        // The B → A seam's Tier A half. A receipt into a basin is an Add with the counter the
        // profile prints; a receipt on the outlet is counted as received and as spilled, which
        // is what rain on an outlet catchment does, so the identity
        // held == rained + injected + received − spilled − drawn holds either way.
        WaterSystem system = NewSystem();
        system.Install(system.AnalyseFor(0, 0));
        int basin = system.BasinAtSample(0, 0);
        Assert.NotEqual(Watershed.Outlet, basin);

        Assert.Equal(0, system.Receive(basin, 7));
        Assert.Equal(7, system.ReceivedLitres);
        Assert.Equal(7, system.StoredLitres);
        Assert.Equal(0, system.SpilledLitres);
        Assert.NotNull(system.LevelAt(basin));

        Assert.Equal(3, system.Receive(Watershed.Outlet, 3));
        Assert.Equal(10, system.ReceivedLitres);
        Assert.Equal(3, system.SpilledLitres);
        Assert.Equal(7, system.StoredLitres);

        // And more than the basin's whole tree holds: the surplus spills, and is counted once.
        long capacity = system.Water!.Tree.Capacity(basin);
        long spilled = system.Receive(basin, capacity + 5);
        Assert.True(spilled >= 5);
        Assert.Equal(3 + spilled, system.SpilledLitres);
        Assert.Equal(
            system.ReceivedLitres + system.InjectedLitres - system.SpilledLitres - system.DrawnLitres,
            system.StoredLitres);
        Assert.Throws<InvalidOperationException>(() => NewSystem().Receive(0, 1));
    }

    [Fact]
    public void WaterLeftBehindIsStillInTheLedgerWhileNobodyIsLookingAtIt()
    {
        // The structural point stated as an assertion: a lake outside the window has no
        // representation in the water at all, and the ledger is the only thing that still knows.
        WaterSystem system = NewSystem();
        system.Install(system.AnalyseFor(0, 0));
        RainUntilWet(system);

        long held = system.StoredLitres;
        Assert.True(held > 0);

        int far = system.SpanMetres * 4 * Overburden.Core.Voxels.VoxelConstants.VoxelsPerMetre;
        system.Install(system.AnalyseFor(far, far));

        Assert.True(system.Ledger.TotalLitres >= held, "the ledger forgot the lake it banked");
    }

    [Fact]
    public void AWindowMoveNeitherDropsRainNorCountsItTwice()
    {
        // Absorb-before-seed, stated as the property rather than as the order. Rain the same span
        // of time through one window and through seven, and the world has to hold the same water.
        //
        // THE SCALE IS THE LOAD-BEARING PART OF THIS FIXTURE. Written first at a scale that filled
        // both basins to the brim, it passed against every mutation - two of them real defects,
        // "seed before absorb" and "the delivered mark never advances" - because a full basin is
        // full whether the move lost a step's rain or delivered it twice. A test whose scenario
        // makes both arms agree is worth nothing, and this one had to be caught by a break-recipe
        // battery rather than by reading it.
        WaterSystem single = NewSystem(rainfallScale: 1);
        single.Install(single.AnalyseFor(0, 0));

        WaterSystem moved = NewSystem(rainfallScale: 1);
        moved.Install(moved.AnalyseFor(0, 0));

        const long step = 600_000;
        for (long time = step; time <= step * 6; time += step)
        {
            single.Advance(time);
            moved.Advance(time);

            // Re-install the *same* window between steps: the move machinery runs in full -
            // absorb, fresh water, seed - while the basins are identical, so any litre this
            // loses or invents is the move's doing and nothing else's.
            moved.Install(moved.AnalyseFor(0, 0));
        }

        Assert.True(single.StoredLitres > 0, "no rain fell, so the arms are two empty worlds");
        Assert.True(
            single.SpilledLitres == 0 && moved.SpilledLitres == 0,
            "the fixture saturated, so a move that lost or doubled a step would still agree");

        // A MOVE CAN ONLY LOSE, AND ONLY BY THE SUB-LITRE REMAINDER. This is the sharp half: a
        // move that dropped a step's rain, or delivered it twice, breaks one of these two
        // assertions, while the bounded shortfall below is the one cost a move genuinely has.
        Assert.True(
            moved.StoredLitres <= single.StoredLitres,
            $"a window move invented water: {moved.StoredLitres} against {single.StoredLitres}");

        // Each Install builds a fresh BasinRainfall, whose pending sub-litre remainder starts at
        // zero - so every move discards under one litre per basin. BasinRainfall's own remarks
        // warn that dropping the remainder *every step* stops a drizzle ever accumulating; this
        // drops it per *move*, which the window hysteresis makes rare, so a drizzle still fills a
        // basin between moves. Bounded, stated, and cheap next to a basin holding tens of
        // thousands of litres.
        //
        // Revisit trigger: a mechanic that re-analyses often - a much smaller window, or a
        // rainfall so light that a basin's whole intake is sub-litre per move. The fix is to carry
        // the remainder across a move keyed by pit, exactly as the ledger carries litres.
        const int moves = 6;
        long shortfall = single.StoredLitres - moved.StoredLitres;
        long bound = (long)moves * single.Window!.Watershed.Basins.Count;

        Assert.True(
            shortfall <= bound,
            $"a move lost {shortfall} L, past the {bound} L the sub-litre remainder can explain");
    }

    [Fact]
    public void RainOwedToTierBCrossesAWindowMoveAndTheFirstWindow()
    {
        // A drop diverted before any window exists is owed to the first one, and one owed at a
        // move is owed to the next — whole cells, not a rounding, so unlike the remainder above a
        // move must not drop it: a drop nobody paid for is water from nothing.
        const long cell = 15_625_000;
        WaterSystem system = NewSystem(rainfallScale: 1);
        system.DivertRain(cell);
        Assert.Equal(cell, system.RainOwedMicrolitres);
        Assert.Equal(0L, system.RainPaidMicrolitres);

        system.Install(system.AnalyseFor(0, 0));
        Assert.Equal(cell, system.RainOwedMicrolitres);

        system.DivertRain(cell);
        system.Install(system.AnalyseFor(0, 0));
        Assert.Equal(2 * cell, system.RainOwedMicrolitres);
        Assert.Equal(2 * cell, system.RainDivertedMicrolitres);

        long time = RainUntilWet(system);
        system.Advance(time + 3_600_000);
        Assert.Equal(0L, system.RainOwedMicrolitres);
        Assert.Equal(2 * cell, system.RainPaidMicrolitres);

        // And paid across a move: the paid total survives the accumulator being replaced.
        system.Install(system.AnalyseFor(0, 0));
        Assert.Equal(2 * cell, system.RainPaidMicrolitres);
        Assert.Equal(0L, system.RainOwedMicrolitres);
    }

    [Fact]
    public void RainThatFellBeforeTheFirstWindowLandedIsDeliveredRatherThanLost()
    {
        // The first analysis takes tens of milliseconds on a worker and the world is not paused for
        // it, so an interval always passes with nowhere to put the rain. It has to arrive the
        // moment there is somewhere - the accumulator's own "arriving late computes the same
        // number" property, one layer out.
        //
        // WRITTEN FIRST AS ITS OPPOSITE, asserting the waited arm held strictly less, and that
        // version failed for two reasons worth recording. The fixture rained hard enough that both
        // basins were full, so the arms agreed whatever the code did - a scenario that could not
        // have caught anything either way. And the code really was wrong: it started the
        // accumulator at "now" rather than at what had been delivered, which is what the comment
        // beside it already claimed it did not.
        WaterSystem waited = NewSystem(rainfallScale: 1);
        waited.Advance(3_600_000);
        waited.Install(waited.AnalyseFor(0, 0));
        waited.Advance(7_200_000);

        WaterSystem ready = NewSystem(rainfallScale: 1);
        ready.Install(ready.AnalyseFor(0, 0));
        ready.Advance(7_200_000);

        Assert.True(ready.StoredLitres > 0, "no rain fell, so the arms are two empty worlds");
        Assert.True(
            ready.SpilledLitres == 0,
            "the fixture saturated and spilled, so the two arms would agree whatever the code did");

        Assert.Equal(waited.TimeMillis, ready.TimeMillis);
        Assert.Equal(ready.StoredLitres, waited.StoredLitres);
    }

    [Fact]
    public void AReopenedWorldDoesNotPourItsWholeHistoryIntoTheFirstWindow()
    {
        // The other half of the same mechanism, and the half that is unrecoverable if wrong. A
        // world reopened at t = ten hours has a ledger that already holds what fell in those ten
        // hours; starting the un-integrated interval at zero would deliver all of it again the
        // moment the first window landed, on top of what is already there.
        var system = new WaterSystem(
            Bowls(), Seed, new BasinLedger(), 32,
            HeightField.DefaultCellVoxels, rainfallScale: 1, startMillis: 36_000_000);

        system.Install(system.AnalyseFor(0, 0));
        system.Advance(36_000_000);

        Assert.Equal(0, system.StoredLitres);
        Assert.Equal(0, system.SpilledLitres);
    }

    [Fact]
    public void TheWindowMovesOnlyAfterAQuarterSpanAndThenStays()
    {
        // Hysteresis, and the reason it is worth a test: without it a player standing on a window
        // boundary re-analyses every time they shift their weight, and the analysis is tens of
        // milliseconds on a worker.
        WaterSystem system = NewSystem();
        system.Install(system.AnalyseFor(0, 0));

        int voxelsPerMetre = Overburden.Core.Voxels.VoxelConstants.VoxelsPerMetre;
        int quarter = system.SpanMetres * voxelsPerMetre / 4;

        Assert.False(system.NeedsWindowFor(0, 0));
        Assert.False(
            system.NeedsWindowFor(quarter - HeightField.DefaultCellVoxels, 0),
            "the window moved before the player had crossed a quarter of it");
        Assert.True(
            system.NeedsWindowFor(quarter * 2, 0),
            "the window never moved however far the player walked");
    }

    [Fact]
    public void ALatticeThatDoesNotMatchIsRefusedRatherThanSilentlyRenamingEveryBasin()
    {
        // A pit is a sample on the lattice, so a window analysed on a different one names different
        // basins — and the ledger is keyed by pit. Installing it would reattach stored water to
        // whatever pit happened to be nearest, which is the failure the header's stored
        // WatershedCellMetres exists to catch a layer down.
        WaterSystem system = NewSystem();
        WaterWindow other = WaterSystem.Analyse(
            Bowls(), 0, 0, 32, HeightField.DefaultCellVoxels * 2);

        Assert.Throws<ArgumentException>(() => system.Install(other));
    }

    [Fact]
    public void TimeDoesNotRunBackwards()
    {
        WaterSystem system = NewSystem();
        system.Install(system.AnalyseFor(0, 0));
        system.Advance(1000);

        Assert.Throws<ArgumentOutOfRangeException>(() => system.Advance(999));
    }

    [Fact]
    public void BankingIsIdempotentAndIsWhatTheStoreWouldWrite()
    {
        // The ledger is the persistent thing and the water is a view of the part of it that is
        // currently analysed, so a save without a bank writes the world as it was when the player
        // entered the window rather than as it is.
        WaterSystem system = NewSystem();
        system.Install(system.AnalyseFor(0, 0));
        RainUntilWet(system);

        Assert.Equal(0, system.Ledger.TotalLitres);

        system.Bank();
        long banked = system.Ledger.TotalLitres;
        Assert.Equal(system.StoredLitres, banked);

        system.Bank();
        Assert.Equal(banked, system.Ledger.TotalLitres);
    }

    [Fact]
    public void ALevelIsReportedOnlyForABasinThatHoldsSomething()
    {
        // Null rather than the pit height, because "empty" and "brim-full at the pit" are different
        // facts and only one of them should draw a surface. M7d-2 renders from this.
        WaterSystem system = NewSystem();
        system.Install(system.AnalyseFor(0, 0));

        Assert.All(
            Enumerable.Range(0, system.Window!.Watershed.Basins.Count),
            basin => Assert.Null(system.LevelAt(basin)));

        RainUntilWet(system);

        Assert.Contains(
            Enumerable.Range(0, system.Window!.Watershed.Basins.Count),
            basin => system.LevelAt(basin) is not null);
    }
}
