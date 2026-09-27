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

    /// <summary>
    /// Water-bodies part a's measurement (2026-09-28): what the awake cells are. Each held cell is
    /// sorted by kind — full; a partial cell resting on something (solid, or a full cell below: a
    /// surface); a partial cell over room (about to fall) — and, for surface cells, by the largest
    /// difference to a holdable side at its own Y: under 5 mL rests by the rule, 5–99 mL is
    /// dithering, 100–999 a gentle slope, 1 L or more a real gradient. Counts awake and asleep
    /// apart, and the distinct Y levels the surface cells stand at.
    /// </summary>
    public sealed record Kinds(
        long[] Awake, long[] Asleep, long[] SurfaceDiffAwake, long[] SurfaceDiffAsleep,
        long[] SurfaceAmountAwake, int SurfaceLevels, double Millis, long[] OverRoomWhy);

    /// <summary>
    /// Why an awake cell over room has not fallen, as the simulation's own terrain answers for the
    /// voxel below: [0] it says it can hold (a real fall owed), [1] it says it cannot (the sim's
    /// terrain disagrees with the world's: a field not built yet, or the lake band), [2] no
    /// second terrain was given.
    /// </summary>
    public static readonly string[] OverRoomNames = ["sim says room", "sim says no room", "not asked"];

    /// <summary>Kind names for <see cref="Kinds.Awake"/>: full, surface, over room.</summary>
    public static readonly string[] KindNames = ["full", "surface", "over room"];

    /// <summary>Difference bands for the surface: &lt;5 mL, 5–99 mL, 0.1–1 L, 1 L+, no holdable side.</summary>
    public static readonly string[] DiffNames = ["<5 mL", "5-99 mL", "0.1-1 L", ">=1 L", "no side"];

    /// <summary>Amount bands for awake surface cells: &lt;1 L (cannot spread), 1–5, 5–10, 10 L+.</summary>
    public static readonly string[] AmountNames = ["<1 L", "1-5 L", "5-10 L", ">=10 L"];

    public static Kinds Classify(FluidGrid grid, IEnumerable<VoxelPos> awake, Func<VoxelPos, bool> canHold, Func<VoxelPos, bool>? simCanHold = null)
    {
        long[] why = new long[3];
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(awake);
        ArgumentNullException.ThrowIfNull(canHold);
        long started = Stopwatch.GetTimestamp();
        var awakeSet = new HashSet<VoxelPos>(awake);
        long[] a = new long[3], s = new long[3], da = new long[5], ds = new long[5], am = new long[4];
        var levels = new HashSet<int>();
        foreach (VoxelPos cell in grid.HeldCells())
        {
            int amount = grid.AmountAt(cell);
            bool isAwake = awakeSet.Contains(cell);
            int kind;
            if (amount >= FluidConstants.CellCapacityMillilitres)
            {
                kind = 0;
            }
            else
            {
                VoxelPos below = cell.Offset(0, -1, 0);
                bool supported = !canHold(below) || grid.AmountAt(below) >= FluidConstants.CellCapacityMillilitres;
                kind = supported ? 1 : 2;
                if (!supported && isAwake)
                {
                    why[simCanHold is null ? 2 : simCanHold(below) ? 0 : 1]++;
                }
            }

            (isAwake ? a : s)[kind]++;
            if (kind != 1)
            {
                continue;
            }

            levels.Add(cell.Y);
            int worst = -1;
            foreach ((int dx, int _, int dz) in Faces)
            {
                if (dx == 0 && dz == 0)
                {
                    continue;
                }

                VoxelPos side = cell.Offset(dx, 0, dz);
                if (!canHold(side))
                {
                    continue;
                }

                worst = Math.Max(worst, Math.Abs(amount - grid.AmountAt(side)));
            }

            int band = worst < 0 ? 4 : worst < 5 ? 0 : worst < 100 ? 1 : worst < 1_000 ? 2 : 3;
            (isAwake ? da : ds)[band]++;
            if (isAwake)
            {
                am[amount < 1_000 ? 0 : amount < 5_000 ? 1 : amount < 10_000 ? 2 : 3]++;
            }
        }

        return new Kinds(a, s, da, ds, am, levels.Count, Stopwatch.GetElapsedTime(started).TotalMilliseconds, why);
    }

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
