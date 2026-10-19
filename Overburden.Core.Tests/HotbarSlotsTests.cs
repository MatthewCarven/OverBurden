using Overburden.Core.Inventory;

namespace Overburden.Core.Tests;

/// <summary>
/// The hotbar's nine slots (alpha session C): an offer takes the first empty slot, a pin puts the
/// hand's material where the player says, and the world file's writer and reader share one rule.
/// </summary>
public class HotbarSlotsTests
{
    private const int E = HotbarSlots.Empty;

    private static HotbarSlots RowOf(params int[] slots)
    {
        var row = new HotbarSlots();
        row.Restore(slots);
        return row;
    }

    // ------------------------------------------------------------------------------ the offer

    [Fact]
    public void AnOfferTakesTheFirstEmptySlotAndNeverMovesAMaterialAlreadyOnTheRow()
    {
        HotbarSlots row = RowOf(5, E, 7, E, E, E, E, E, E);

        Assert.Equal(1, row.Offer(9));
        Assert.Equal(2, row.Offer(7));
        Assert.Equal(3, row.Offer(4));
        Assert.Equal([5, 9, 7, 4, E, E, E, E, E], row.ToArray());
    }

    [Fact]
    public void AnOfferToAFullRowTakesNoSlotAndIsNamedOnceASession()
    {
        HotbarSlots row = RowOf(1, 2, 3, 4, 5, 6, 7, 8, 9);
        long version = row.Version;

        Assert.Equal(-1, row.Offer(10));
        Assert.Equal(-1, row.Offer(10));
        Assert.Equal(-1, row.Offer(11));

        Assert.Equal([10, 11], row.DrainTurnedAway());
        Assert.Empty(row.DrainTurnedAway());
        Assert.Equal(-1, row.Offer(10));
        Assert.Empty(row.DrainTurnedAway());
        Assert.Equal(version, row.Version);
    }

    [Fact]
    public void AirIsNeverOffered() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new HotbarSlots().Offer(0));

    // -------------------------------------------------------------------------------- the pin

    [Fact]
    public void PinningAMaterialOnTheRowSwapsTheTwoSlots()
    {
        HotbarSlots row = RowOf(5, 6, 7, E, E, E, E, E, E);

        PinOutcome outcome = row.Pin(0, 7);

        Assert.Equal(new PinOutcome(PinKind.Swapped, 0, 5, 2), outcome);
        Assert.Equal([7, 6, 5, E, E, E, E, E, E], row.ToArray());
    }

    [Fact]
    public void PinningAMaterialOnTheRowToAnEmptySlotMovesIt()
    {
        HotbarSlots row = RowOf(5, 6, 7, E, E, E, E, E, E);

        Assert.Equal(new PinOutcome(PinKind.Moved, 8, E, 1), row.Pin(8, 6));
        Assert.Equal([5, E, 7, E, E, E, E, E, 6], row.ToArray());
    }

    [Fact]
    public void PinningFromOffTheRowIntoAnEmptySlotPlacesIt()
    {
        HotbarSlots row = RowOf(5, E, E, E, E, E, E, E, E);

        Assert.Equal(new PinOutcome(PinKind.Placed, 3, E, -1), row.Pin(3, 12));
        Assert.Equal([5, E, E, 12, E, E, E, E, E], row.ToArray());
    }

    [Fact]
    public void PinningFromOffTheRowSendsTheSlotsOldMaterialToTheFirstEmptySlot()
    {
        HotbarSlots row = RowOf(5, 6, E, E, E, E, E, E, E);

        Assert.Equal(new PinOutcome(PinKind.Displaced, 0, 5, 2), row.Pin(0, 12));
        Assert.Equal([12, 6, 5, E, E, E, E, E, E], row.ToArray());
    }

    [Fact]
    public void PinningFromOffAFullRowSendsTheSlotsOldMaterialOffIt()
    {
        HotbarSlots row = RowOf(1, 2, 3, 4, 5, 6, 7, 8, 9);

        Assert.Equal(new PinOutcome(PinKind.LeftRow, 4, 5, -1), row.Pin(4, 12));
        Assert.Equal([1, 2, 3, 4, 12, 6, 7, 8, 9], row.ToArray());
    }

    [Fact]
    public void PinningToItsOwnSlotOrWithNothingInHandChangesNothing()
    {
        HotbarSlots row = RowOf(5, 6, E, E, E, E, E, E, E);
        long version = row.Version;

        Assert.Equal(PinKind.AlreadyThere, row.Pin(1, 6).Kind);
        Assert.Equal(PinKind.NothingInHand, row.Pin(1, E).Kind);

        Assert.Equal([5, 6, E, E, E, E, E, E, E], row.ToArray());
        Assert.Equal(version, row.Version);
    }

    [Fact]
    public void APinOffTheRowOrOfAirIsACallerBug()
    {
        var row = new HotbarSlots();

        Assert.Throws<ArgumentOutOfRangeException>(() => row.Pin(9, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => row.Pin(-1, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => row.Pin(0, 0));
    }

    [Fact]
    public void EveryChangeBumpsTheVersionAndOnlyAChangeDoes()
    {
        var row = new HotbarSlots();
        long v0 = row.Version;

        row.Offer(5);
        Assert.True(row.Version > v0);

        long v1 = row.Version;
        row.Offer(5);
        Assert.Equal(v1, row.Version);

        row.Pin(3, 5);
        Assert.True(row.Version > v1);

        long v2 = row.Version;
        row.Clear(7);
        Assert.Equal(v2, row.Version);
        row.Clear(3);
        Assert.True(row.Version > v2);
        Assert.Equal(HotbarSlots.EmptyRow, row.ToArray());
    }

    // ------------------------------------------------------------------ the old world's row

    [Fact]
    public void AWorldWrittenBeforeTheSlotsGetsItsFirstNineHoldingsInIdOrder()
    {
        Assert.Equal([1, 2, 3, 4, 5, 7, 9, 10, 12], HotbarSlots.RowFor([1, 2, 3, 4, 5, 7, 9, 10, 12, 13, 14, 16]));
        Assert.Equal([2, 4, E, E, E, E, E, E, E], HotbarSlots.RowFor([2, 4]));
        Assert.Equal(HotbarSlots.EmptyRow, HotbarSlots.RowFor([]));
    }

    // ----------------------------------------------------------------------- the shared rule

    [Fact]
    public void ARowIsExactlyNineSlotsEachEmptyOrAMaterialInTheTableOnce()
    {
        Assert.Null(HotbarSlots.Problem([5, E, 7, E, E, E, E, E, 16], materialCount: 17));
        Assert.Null(HotbarSlots.Problem(HotbarSlots.EmptyRow, materialCount: 1));

        Assert.Contains("holds 8 slots", HotbarSlots.Problem([E, E, E, E, E, E, E, E], 17));
        Assert.Contains("holds 10 slots", HotbarSlots.Problem([E, E, E, E, E, E, E, E, E, E], 17));
        Assert.Contains("slot 2 names air", HotbarSlots.Problem([5, 0, E, E, E, E, E, E, E], 17));
        Assert.Contains("slot 3 is -2", HotbarSlots.Problem([5, E, -2, E, E, E, E, E, E], 17));
        Assert.Contains("slot 9 names material ID 17", HotbarSlots.Problem([5, E, E, E, E, E, E, E, 17], 17));
        Assert.Contains("slots 1 and 6 both name material ID 5", HotbarSlots.Problem([5, E, E, E, E, 5, E, E, E], 17));
    }

    [Fact]
    public void ARestoreRefusesABadRowWholeAndKeepsTheOneItHad()
    {
        HotbarSlots row = RowOf(5, 6, E, E, E, E, E, E, E);

        Assert.Throws<ArgumentException>(() => row.Restore([7, 7, E, E, E, E, E, E, E]));
        Assert.Equal([5, 6, E, E, E, E, E, E, E], row.ToArray());
    }
}
