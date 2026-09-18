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
}
