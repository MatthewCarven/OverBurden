using Overburden.Core.Fluid;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// Water bodies part a (2026-09-28): the solve's wake counters, which ask whether the solve under the
/// near/far order keeps waking the pool it levels. An unlevel puddle put down asleep through the grid
/// in chunk (0, 0, 0), solved from its own cells, with the viewer over it, far from it, or no order.
/// </summary>
public class SolveWakeCountTests
{
    private static VoxelPos At(int x, int y, int z) => new(x, y, z);

    private static readonly PredicateTerrain Floor = new(v => v.Y == 1 && v.X is >= 0 and < 32 && v.Z is >= 0 and < 32);

    private static (FluidSimulation Sim, List<VoxelPos> Cells) Puddle()
    {
        var sim = new FluidSimulation(Floor);
        var cells = new List<VoxelPos>();
        for (int x = 4; x < 10; x++)
        {
            for (int z = 4; z < 10; z++)
            {
                sim.Grid.Fill(At(x, 1, z), 6_000 + (400 * ((x + z) % 6)));
                cells.Add(At(x, 1, z));
            }
        }

        return (sim, cells);
    }

    [Fact]
    public void WithNoOrderTheSolvesWakesAreCountedAndNoneAreFar()
    {
        (FluidSimulation sim, List<VoxelPos> cells) = Puddle();

        SheetLevelResult r = sim.LevelSheets(cells);

        Assert.Equal(1, r.Levelled);
        Assert.True(r.CellsWoken > 0, "levelling an asleep puddle wakes its front");
        Assert.Equal(r.CellsWoken, sim.SolveCellsWoken);
        Assert.Equal(0, sim.SolveFarSheetsLevelled);
        Assert.Equal(0, sim.SolveFarCellsWoken);
    }

    [Fact]
    public void ASheetOutsideTheNearRingsIsCountedFar()
    {
        (FluidSimulation sim, List<VoxelPos> cells) = Puddle();
        sim.SetNearField(new ChunkPos(10, 0, 10), rings: 1);

        SheetLevelResult r = sim.LevelSheets(cells);

        Assert.Equal(1, r.Levelled);
        Assert.Equal(1, sim.SolveFarSheetsLevelled);
        Assert.Equal(r.CellsWoken, sim.SolveFarCellsWoken);
        Assert.Equal(r.CellsWoken, sim.SolveCellsWoken);
    }

    [Fact]
    public void ASheetInsideTheNearRingsIsNotCountedFar()
    {
        (FluidSimulation sim, List<VoxelPos> cells) = Puddle();
        sim.SetNearField(ChunkPos.Zero, rings: 1);

        SheetLevelResult r = sim.LevelSheets(cells);

        Assert.Equal(1, r.Levelled);
        Assert.Equal(0, sim.SolveFarSheetsLevelled);
        Assert.Equal(r.CellsWoken, sim.SolveCellsWoken);
    }
}
