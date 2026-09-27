using System.Diagnostics;
using Overburden.Core.Lighting;
using Overburden.Core.Voxels;

namespace Overburden.Core.Fluid;

/// <summary>
/// A measurement for the water-bodies pass (2026-09-28): Tier B's held water split into connected
/// components — face-to-face wet cells, Matthew's rule of 2026-09-26 (*"only if there is tier b water
/// connecting"*) — with each one's size, litres, span and how much of it lies below the seed.
/// WATER-BODIES-NOTES' decision 3 says the pass opens with this number: what finding a body costs.
/// </summary>
/// <remarks>Diagnostic only: nothing reads it back. A flood over every held cell, on the sim thread
/// at a boundary, where the grid is the sim thread's.</remarks>
public static class WaterComponents
{
    /// <summary>One component: cells, millilitres, full cells, the Y span, and cells below the seed.</summary>
    public readonly record struct Component(int Cells, long Millilitres, int FullCells, int MinY, int MaxY, int BelowSeed, VoxelPos Lowest);

    /// <summary>The components, largest first, and what the flood cost.</summary>
    public sealed record Report(IReadOnlyList<Component> Components, int HeldCells, double Millis);

    private static readonly (int X, int Y, int Z)[] Faces =
        [(1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)];

    public static Report Measure(FluidGrid grid, ISurfaceHeights? seed = null)
    {
        ArgumentNullException.ThrowIfNull(grid);
        long started = Stopwatch.GetTimestamp();
        var visited = new HashSet<VoxelPos>();
        var queue = new Queue<VoxelPos>();
        var components = new List<Component>();
        int held = 0;

        foreach (VoxelPos start in grid.HeldCells())
        {
            held++;
            if (!visited.Add(start))
            {
                continue;
            }

            int cells = 0, full = 0, below = 0;
            long millilitres = 0;
            int minY = int.MaxValue, maxY = int.MinValue;
            VoxelPos lowest = start;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                VoxelPos cell = queue.Dequeue();
                int amount = grid.AmountAt(cell);
                cells++;
                millilitres += amount;
                if (amount >= FluidConstants.CellCapacityMillilitres)
                {
                    full++;
                }

                if (cell.Y < minY || (cell.Y == minY && (cell.X, cell.Z).CompareTo((lowest.X, lowest.Z)) < 0))
                {
                    lowest = cell;
                }

                minY = Math.Min(minY, cell.Y);
                maxY = Math.Max(maxY, cell.Y);
                if (seed is not null && cell.Y <= seed.SurfaceHeightAt(cell.X, cell.Z))
                {
                    below++;
                }

                foreach ((int dx, int dy, int dz) in Faces)
                {
                    VoxelPos next = cell.Offset(dx, dy, dz);
                    if (grid.AmountAt(next) > 0 && visited.Add(next))
                    {
                        queue.Enqueue(next);
                    }
                }
            }

            components.Add(new Component(cells, millilitres, full, minY, maxY, below, lowest));
        }

        components.Sort((a, b) => b.Cells.CompareTo(a.Cells));
        return new Report(components, held, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }
}
