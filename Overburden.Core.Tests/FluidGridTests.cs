using Overburden.Core.Fluid;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// The unit and the storage. Everything here is arithmetic that can be checked on paper, which is
/// the point — the automaton's conservation claim rests entirely on this file being right.
/// </summary>
public class FluidGridTests
{
    private const int Cap = FluidConstants.CellCapacityMillilitres;

    [Fact]
    public void ACellHoldsAWholeNumberOfMillilitres()
    {
        // 1,000,000 mL per cubic metre over 4x4x4 voxels. The division has to be exact or every
        // cell in the world silently loses a fraction of itself — at three voxels per metre it
        // would be 1,000,000/27, which truncates.
        Assert.Equal(0, FluidConstants.MillilitresPerCubicMetre % FluidConstants.VoxelsPerCubicMetre);
        Assert.Equal(15_625, Cap);
        Assert.Equal(15.625, FluidConstants.CellCapacityLitres, 6);
    }

    [Fact]
    public void ACellFitsInTheStorageItIsWrittenTo()
    {
        // FluidGrid stores a ushort per cell. A capacity past 65,535 would wrap silently.
        Assert.True(Cap <= ushort.MaxValue);
    }

    [Fact]
    public void TheSeamFromLitresNeverRounds()
    {
        // Tier A counts litres; Tier B counts millilitres. Litres to millilitres is exact by
        // construction, which is the half of the seam that must never lose anything.
        Assert.Equal(1_000, FluidConstants.MillilitresPerLitre);
        Assert.Equal(9_000_000_000L, 9_000_000L * FluidConstants.MillilitresPerLitre);
    }

    [Fact]
    public void FillingBeyondCapacityHandsBackTheRest()
    {
        var grid = new FluidGrid();
        long overflow = grid.Fill(new VoxelPos(3, 4, 5), Cap + 500);

        Assert.Equal(500, overflow);
        Assert.Equal(Cap, grid.AmountAt(new VoxelPos(3, 4, 5)));
        Assert.Equal(Cap, grid.TotalMillilitres);
    }

    [Fact]
    public void DrainingMoreThanIsThereReportsWhatWasActuallyTaken()
    {
        var grid = new FluidGrid();
        grid.Fill(new VoxelPos(0, 0, 0), 400);

        Assert.Equal(400, grid.Drain(new VoxelPos(0, 0, 0), 10_000));
        Assert.Equal(0, grid.TotalMillilitres);
    }

    [Fact]
    public void ATransferMovesTheSameNumberOutAsItMovesIn()
    {
        var grid = new FluidGrid();
        grid.Fill(new VoxelPos(0, 0, 0), 1_000);

        int moved = grid.Transfer(new VoxelPos(0, 0, 0), new VoxelPos(0, 1, 0), 300);

        Assert.Equal(300, moved);
        Assert.Equal(700, grid.AmountAt(new VoxelPos(0, 0, 0)));
        Assert.Equal(300, grid.AmountAt(new VoxelPos(0, 1, 0)));
        Assert.Equal(1_000, grid.TotalMillilitres);
    }

    [Fact]
    public void ATransferIsClampedByTheRoomAtTheFarEnd()
    {
        var grid = new FluidGrid();
        grid.Fill(new VoxelPos(0, 0, 0), Cap);
        grid.Fill(new VoxelPos(1, 0, 0), Cap - 10);

        Assert.Equal(10, grid.Transfer(new VoxelPos(0, 0, 0), new VoxelPos(1, 0, 0), 5_000));
        Assert.Equal(Cap + Cap - 10, grid.TotalMillilitres);
    }

    [Fact]
    public void ATransferAcrossAChunkBoundaryConservesJustAsOneInsideDoes()
    {
        // Voxel -1 and voxel 0 are in different chunks, and the crossing at the origin is the one
        // truncating division would get wrong. VoxelPos masks instead, so this is a check that the
        // grid actually uses the masked index rather than an assumption about it.
        var grid = new FluidGrid();
        grid.Fill(new VoxelPos(-1, 0, 0), 900);

        Assert.Equal(900, grid.Transfer(new VoxelPos(-1, 0, 0), new VoxelPos(0, 0, 0), 900));
        Assert.Equal(0, grid.AmountAt(new VoxelPos(-1, 0, 0)));
        Assert.Equal(900, grid.AmountAt(new VoxelPos(0, 0, 0)));
        Assert.Equal(900, grid.TotalMillilitres);
        Assert.Equal(1, grid.PanelCount);
    }

    [Fact]
    public void AnEmptiedChunkGivesItsPanelBack()
    {
        var grid = new FluidGrid();
        grid.Fill(new VoxelPos(0, 0, 0), 100);
        grid.Fill(new VoxelPos(100, 0, 0), 100);
        Assert.Equal(2, grid.PanelCount);

        grid.Drain(new VoxelPos(100, 0, 0), 100);

        Assert.Equal(1, grid.PanelCount);
        Assert.Equal(100, grid.TotalMillilitres);
    }

    [Fact]
    public void AskingAboutAFullCellInAnEmptyChunkDoesNotStrandAPanel()
    {
        // Fill returns early when there is no room, and the panel it may have allocated on the way
        // in has to go back. Otherwise PanelCount stops being a sparsity measure and starts being
        // a count of chunks anybody ever asked about.
        var grid = new FluidGrid();
        grid.Fill(new VoxelPos(0, 0, 0), Cap);
        Assert.Equal(1, grid.PanelCount);

        Assert.Equal(50, grid.Fill(new VoxelPos(0, 0, 0), 50));
        Assert.Equal(1, grid.PanelCount);

        Assert.Equal(0, grid.Transfer(new VoxelPos(0, 0, 0), new VoxelPos(500, 0, 0), 0));
        Assert.Equal(1, grid.PanelCount);
    }

    [Fact]
    public void AChunkThatEmptiesAndFillsAgainIsStillCountedOnce()
    {
        // Releasing a panel has to invalidate the one-entry cache, or the next write to that chunk
        // goes into an array the dictionary no longer holds: the running total moves and the cell
        // is invisible to HeldCells. Nothing else in the suite empties a chunk and comes back to
        // it, so this recipe was uncaught until this test existed.
        var grid = new FluidGrid();
        var far = new VoxelPos(100, 0, 0);

        grid.Fill(far, 700);
        grid.Drain(far, 700);
        Assert.Equal(0, grid.PanelCount);

        grid.Fill(far, 400);

        Assert.Equal(1, grid.PanelCount);
        Assert.Equal(400, grid.AmountAt(far));
        Assert.Equal(400, grid.TotalMillilitres);
        Assert.Equal([far], grid.HeldCells());
    }

    [Fact]
    public void HeldCellsListsExactlyTheCellsThatHoldSomething()
    {
        var grid = new FluidGrid();
        grid.Fill(new VoxelPos(1, 2, 3), 10);
        grid.Fill(new VoxelPos(-9, -9, -9), 20);
        grid.Fill(new VoxelPos(40, 40, 40), 30);
        grid.Drain(new VoxelPos(1, 2, 3), 10);

        List<VoxelPos> held = [.. grid.HeldCells()];

        Assert.Equal(2, held.Count);
        Assert.Contains(new VoxelPos(-9, -9, -9), held);
        Assert.Contains(new VoxelPos(40, 40, 40), held);
        Assert.Equal(2, grid.HeldCellCount);
        Assert.Equal(50, grid.TotalMillilitres);
    }

    [Fact]
    public void TransferringToItselfIsRefusedRatherThanDoubled()
    {
        var grid = new FluidGrid();
        grid.Fill(new VoxelPos(0, 0, 0), 100);

        Assert.Equal(0, grid.Transfer(new VoxelPos(0, 0, 0), new VoxelPos(0, 0, 0), 100));
        Assert.Equal(100, grid.TotalMillilitres);
    }
}
