using Overburden.Core.Inventory;
using Overburden.Core.Persistence;

namespace Overburden.Core.Tests;

/// <summary>
/// The charges carried beside a pack's count (2026-09-18, Matthew's <i>"keep its charge as an
/// item"</i>): what is kept, what is not, and which one a right-click takes.
/// </summary>
public sealed class CarriedPacksTests
{
    [Fact]
    public void AStowedChargeComesBackAndTheFullestIsTakenFirst()
    {
        var packs = new CarriedPacks();
        Assert.True(packs.Stow(new PackSave(2, 1_000, 0)));
        Assert.True(packs.Stow(new PackSave(2, 2_650_600_000, 3)));
        Assert.True(packs.Stow(new PackSave(2, 7, 1)));

        Assert.Equal(3, packs.Count(2));
        Assert.Equal(2_650_600_000L, packs.FullestMillijoules(2));

        Assert.Equal(new PackSave(2, 2_650_600_000, 3), packs.TakeFullest(2));
        Assert.Equal(new PackSave(2, 1_000, 0), packs.TakeFullest(2));
        Assert.Equal(new PackSave(2, 7, 1), packs.TakeFullest(2));
        Assert.Null(packs.TakeFullest(2));
        Assert.Equal(0, packs.Count());
    }

    [Fact]
    public void AnEmptyPackLeavesNoEntryAndAnotherMaterialsPackIsNotTaken()
    {
        var packs = new CarriedPacks();
        Assert.False(packs.Stow(new PackSave(2, 0, 3)));
        Assert.Equal(0, packs.Count());
        Assert.Equal(0L, packs.FullestMillijoules(2));

        packs.Stow(new PackSave(1, 50, 0));
        Assert.Null(packs.TakeFullest(2));
        Assert.Equal(1, packs.Count(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => packs.Stow(new PackSave(2, -1, 0)));
    }

    [Fact]
    public void TheEntriesAreTheSaveFormsOrderWhateverTheStowOrder()
    {
        // The header writes Entries verbatim, so the same packs have to write the same bytes:
        // by material, fullest first, then channel.
        var packs = new CarriedPacks();
        packs.Stow(new PackSave(2, 5, 9));
        packs.Stow(new PackSave(1, 1, 0));
        packs.Stow(new PackSave(2, 5, 2));
        packs.Stow(new PackSave(2, 9, 0));

        Assert.Equal(
            [new PackSave(1, 1, 0), new PackSave(2, 9, 0), new PackSave(2, 5, 2), new PackSave(2, 5, 9)],
            packs.Entries);

        var reset = new CarriedPacks();
        reset.Reset(packs.Entries);
        Assert.Equal(packs.Entries, reset.Entries);
    }

    [Fact]
    public void ASameKindSwapWouldWearLessOnlyWhenEveryCarriedOneHoldsLessThanTheWornOne()
    {
        // His play of 2026-10-12: wearing a lithium-ion pack at 5,023.6 kJ, nothing charged carried.
        const int lithium = 2, lead = 1;
        const long worn = 5_023_600_000;
        var packs = new CarriedPacks();
        Assert.True(packs.WouldWearLess(lithium, lithium, worn));

        packs.Stow(new PackSave(lithium, worn - 1, 0));
        Assert.True(packs.WouldWearLess(lithium, lithium, worn));

        // Equal loses nothing whichever is worn; fuller is the swap working as meant.
        packs.Stow(new PackSave(lithium, worn, 4));
        Assert.False(packs.WouldWearLess(lithium, lithium, worn));
        packs.Stow(new PackSave(lithium, worn + 1, 0));
        Assert.False(packs.WouldWearLess(lithium, lithium, worn));

        // Another kind is asked for by name, and an empty worn pack loses nothing coming off.
        var none = new CarriedPacks();
        Assert.False(none.WouldWearLess(lead, lithium, worn));
        Assert.False(none.WouldWearLess(lithium, lithium, 0));

        // A charged pack of another kind carried doesn't count for this one.
        none.Stow(new PackSave(lead, worn * 2, 0));
        Assert.True(none.WouldWearLess(lithium, lithium, worn));
    }

    [Fact]
    public void TheGuardAsksOnceAndASecondClickForTheSameKindInsideTheWindowIsTheYes()
    {
        const int lithium = 2, lead = 1;
        var guard = new PackSwapGuard();

        // Nothing to ask: through at once, every time.
        Assert.True(guard.Allows(lithium, wouldWearLess: false, nowSeconds: 0));
        Assert.True(guard.Allows(lithium, wouldWearLess: false, nowSeconds: 0));

        // Asked, then the yes at the window's edge.
        Assert.False(guard.Allows(lithium, wouldWearLess: true, nowSeconds: 10));
        Assert.True(guard.Allows(lithium, wouldWearLess: true, nowSeconds: 10 + PackSwapGuard.WindowSeconds));

        // Past the window, the next click asks again.
        Assert.False(guard.Allows(lithium, wouldWearLess: true, nowSeconds: 14));

        // Too late is a new question, not the answer to the old one.
        Assert.False(guard.Allows(lithium, wouldWearLess: true, nowSeconds: 14 + PackSwapGuard.WindowSeconds + 0.01));
        Assert.True(guard.Allows(lithium, wouldWearLess: true, nowSeconds: 18));

        // A click for another kind in between is not the yes, and a swap that needed no asking
        // clears the question.
        Assert.False(guard.Allows(lithium, wouldWearLess: true, nowSeconds: 30));
        Assert.False(guard.Allows(lead, wouldWearLess: true, nowSeconds: 31));
        Assert.False(guard.Allows(lithium, wouldWearLess: true, nowSeconds: 32));
        Assert.True(guard.Allows(lead, wouldWearLess: false, nowSeconds: 33));
        Assert.False(guard.Allows(lithium, wouldWearLess: true, nowSeconds: 34));

        // The yes is spent at once: a third click straight after it asks again, though it lands
        // inside the window the question opened. (Without this line nothing caught a yes that
        // stayed open: the battery's recipe, written before it ran.)
        Assert.True(guard.Allows(lithium, wouldWearLess: true, nowSeconds: 35));
        Assert.False(guard.Allows(lithium, wouldWearLess: true, nowSeconds: 36));
    }
}
