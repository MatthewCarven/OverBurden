using Overburden.Core.Content;
using Overburden.Core.Fluid;
using Overburden.Core.Voxels;

namespace Overburden.Core.Structure;

/// <summary>What <see cref="Collapse.TryFall"/> did, or why it did nothing.</summary>
public enum CollapseOutcome : byte
{
    /// <summary>Rubble moved: the result says how much.</summary>
    Fell,

    /// <summary>The chunk's settled field has nothing at risk.</summary>
    NothingAtRisk,

    /// <summary>The chunk, or a chunk one of its runs reaches into, is not settled: try next boundary.</summary>
    WaitingUnsettled,

    /// <summary>A chunk the fall would write into cannot hold water yet — its stored panel is still on the lane.</summary>
    WaitingFluid,
}

/// <summary>One fall's numbers — the line the game prints and the counts the sweep sums.</summary>
public readonly record struct CollapseResult(
    ChunkPos Chunk,
    int Voxels,
    int Columns,
    int DeepestDrop,
    long DropSum,
    int StaleSkipped,
    long LiftedMillilitres,
    long ReturnedMillilitres,
    long DisplacedMillilitres,
    IReadOnlyCollection<ChunkPos> FellFrom,
    IReadOnlyCollection<ChunkPos> Written)
{
    /// <summary>Voxels of drop per voxel fallen.</summary>
    public double MeanDrop => Voxels == 0 ? 0 : DropSum / (double)Voxels;
}

/// <summary>
/// The fall (M9c-1, decisions 1, 2, 4 and 5): a chunk's settled at-risk set, followed down its
/// columns through at-risk voxels across chunk faces into settled chunks, dropped as column runs
/// onto the first solid voxel below, as one material, with the water lifted from under it and
/// returned into the cavity it left.
/// </summary>
/// <remarks>
/// <para>
/// <b>The unit is a column run, not a voxel</b>, because of one fact derived from the rule: the
/// voxel directly below an at-risk voxel is non-solid, or at risk itself — a solid voxel below
/// with a live budget gives the one above a refund, an anchor gives <c>K</c>, and only an
/// unreached voxel or one past <c>−r</c> leaves the voxel above negative. So an at-risk run down a
/// column always ends on air or water, never on held rock, and dropping it is a strictly
/// descending move. The plan's per-chunk "every at-risk voxel" would land the voxels of a plug
/// straddling a horizontal chunk face on the at-risk rock under them, in the chunk below, waiting
/// on its own timer: rock to rubble in place, <c>Σ(Y)</c> unchanged, and a no-op fire at every
/// boundary until the lower chunk went. A run follows the column into the chunk below when it is
/// analysed and settled, and the fire <b>waits</b> when it is not — decision 7's barrier, one
/// chunk down.
/// </para>
/// <para>
/// <b>The gate is the settled field <em>now</em>, and the world.</b> <see cref="SupportRegion.TryReadSettled"/>
/// refuses a chunk with a stale snapshot; and every at-risk voxel is read back from the world and
/// skipped when it is not solid — belt and braces, counted as <see cref="CollapseResult.StaleSkipped"/>,
/// expected to read zero, printed so a non-zero says the gate has a hole.
/// </para>
/// <para>
/// <b>Per column, in order: lift, write, return.</b> Every landing cell is drained first, so the
/// rubble write finds an empty cell and <c>Disturb</c> wakes only the neighbours; the column's
/// final materials are written once per cell — a cell that is both cavity and landing (a run
/// falling less than its own height) is written rubble once, never air then rubble; then the
/// lifted water goes back <b>bottom-up from the first cell above the new rubble</b>, through the
/// column the run fell through and into the cavity, which is where a stone dropped in a pool puts
/// the water: the local level rises. The plan's "into the cavity from the bottom up" would lift a
/// floor pool to the ceiling of a half-flooded room and drop it back through the automaton. It
/// always fits — a run of <i>n</i> lands in <i>n</i> cells holding at most <i>n</i> capacities
/// and vacates <i>n</i> — so <c>lifted == returned</c> by construction and any remainder is
/// counted as displaced rather than lost.
/// </para>
/// <para>
/// <b>Two runs in one column fall bottom-up</b>, the upper onto the lower's rubble, with the upper
/// run's drop computed from where the lower one landed rather than read from the world, because
/// the world between them is air by construction (a held voxel cannot sit under an at-risk one)
/// and the lower run's writes have already landed. Rubble that would land on a chunk that is not
/// loaded rests on it as on rock — the landing walk stops at the loaded edge rather than reasoning
/// about it; when that chunk loads, the rubble's chunk is dug and analysed, and rubble over air
/// falls again. Self-correcting, and recorded rather than built.
/// </para>
/// </remarks>
public static class Collapse
{
    private readonly struct Run(int x, int z, int top, int bottom)
    {
        public readonly int X = x;
        public readonly int Z = z;
        public readonly int Top = top;
        public readonly int Bottom = bottom;
        public int Length => Top - Bottom + 1;
    }

    /// <summary>
    /// Drops what the chunk's settled field marks at risk, with the runs under it. Writes through
    /// <paramref name="world"/>, so the pipeline's hooks — light, remesh, the region, the water —
    /// see every voxel the way a swing tells them.
    /// </summary>
    /// <param name="water">The water, or null for a world with none: no lift, no return, no wait.</param>
    public static CollapseOutcome TryFall(
        SupportRegion region,
        ChunkPos chunk,
        IVoxelEditor world,
        MaterialTable materials,
        int rubbleId,
        ICollapseWater? water,
        out CollapseResult result)
    {
        ArgumentNullException.ThrowIfNull(region);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(materials);
        if (!materials.IsSolid(rubbleId))
        {
            throw new ArgumentException("Rubble has to be a solid material.", nameof(rubbleId));
        }

        result = default;
        if (!region.TryReadSettled(chunk, out ChunkSupport field))
        {
            return CollapseOutcome.WaitingUnsettled;
        }

        if (field.AtRiskCount == 0)
        {
            return CollapseOutcome.NothingAtRisk;
        }

        // Phase 1: every run, from the settled fields and the world as they stand, before a
        // voxel is written — a write requeues the chunk and its six, and the snapshots read
        // here are what the fall is of.
        var runs = new List<Run>();
        var fellFrom = new HashSet<ChunkPos> { chunk };
        int stale = 0;
        VoxelPos origin = chunk.OriginVoxel;
        for (int lz = 0; lz < VoxelConstants.ChunkSize; lz++)
        {
            for (int lx = 0; lx < VoxelConstants.ChunkSize; lx++)
            {
                int columnBase = VoxelPos.Index(lx, 0, lz);
                int wx = origin.X + lx;
                int wz = origin.Z + lz;
                int ly = VoxelConstants.ChunkSize - 1;
                while (ly >= 0)
                {
                    if (!field.IsAtRisk(columnBase + ly))
                    {
                        ly--;
                        continue;
                    }

                    if (!SolidInWorld(world, materials, new VoxelPos(wx, origin.Y + ly, wz)))
                    {
                        stale++;
                        ly--;
                        continue;
                    }

                    int top = origin.Y + ly;
                    int bottom = top;
                    bool endedOnStale = false;
                    ly--;
                    while (ly >= 0 && field.IsAtRisk(columnBase + ly))
                    {
                        if (!SolidInWorld(world, materials, new VoxelPos(wx, origin.Y + ly, wz)))
                        {
                            stale++;
                            endedOnStale = true;
                            ly--;
                            break;
                        }

                        bottom = origin.Y + ly;
                        ly--;
                    }

                    if (ly < 0 && !endedOnStale)
                    {
                        // The run reaches the chunk's floor: follow it into the chunk below.
                        CollapseOutcome? wait = ExtendBelow(region, world, materials, chunk, lx, lz, ref bottom, fellFrom, ref stale);
                        if (wait is { } outcome)
                        {
                            return outcome;
                        }
                    }

                    runs.Add(new Run(wx, wz, top, bottom));
                }
            }
        }

        if (runs.Count == 0)
        {
            // Everything the field marked was stale: nothing solid to drop.
            result = new CollapseResult(chunk, 0, 0, 0, 0, stale, 0, 0, 0, fellFrom, []);
            return CollapseOutcome.NothingAtRisk;
        }

        // Where each run lands. Runs were collected top-down per column and are grouped by
        // column in the list; per column they fall bottom-up, so the lower run's landing is
        // known before the upper's is computed. Collected before anything is written so the
        // fluid-ready wait can refuse the whole fall.
        var landBottoms = new int[runs.Count];
        var written = new HashSet<ChunkPos>();
        int i = 0;
        while (i < runs.Count)
        {
            int j = i;
            while (j + 1 < runs.Count && runs[j + 1].X == runs[i].X && runs[j + 1].Z == runs[i].Z)
            {
                j++;
            }

            // runs[i..j] is one column, top-down; the lowest is runs[j].
            int landBottom = runs[j].Bottom - GapBelow(world, materials, runs[j]);
            landBottoms[j] = landBottom;
            for (int k = j - 1; k >= i; k--)
            {
                landBottoms[k] = landBottoms[k + 1] + runs[k + 1].Length;
            }

            for (int k = i; k <= j; k++)
            {
                if (landBottoms[k] >= runs[k].Bottom)
                {
                    continue;
                }

                for (int y = landBottoms[k]; y <= runs[k].Top; y++)
                {
                    written.Add(new VoxelPos(runs[k].X, y, runs[k].Z).Chunk);
                }
            }

            i = j + 1;
        }

        if (water is not null)
        {
            foreach (ChunkPos touched in written)
            {
                if (!water.IsReady(touched))
                {
                    return CollapseOutcome.WaitingFluid;
                }
            }
        }

        // Phase 2: the writes, column by column, run by run from the bottom.
        int voxels = 0;
        int columns = 0;
        int deepest = 0;
        long dropSum = 0;
        long lifted = 0;
        long returned = 0;
        long displaced = 0;
        i = 0;
        while (i < runs.Count)
        {
            int j = i;
            while (j + 1 < runs.Count && runs[j + 1].X == runs[i].X && runs[j + 1].Z == runs[i].Z)
            {
                j++;
            }

            bool columnMoved = false;
            for (int k = j; k >= i; k--)
            {
                Run run = runs[k];
                int landBottom = landBottoms[k];
                int gap = run.Bottom - landBottom;
                if (gap <= 0)
                {
                    // Solid directly under the run where the field said air: the world moved
                    // between the settle and now. Counted, and the run stands where it is.
                    stale += run.Length;
                    continue;
                }

                int n = run.Length;
                int landTop = landBottom + n - 1;

                long liftedHere = 0;
                if (water is not null)
                {
                    for (int y = landBottom; y <= landTop; y++)
                    {
                        liftedHere += water.Lift(new VoxelPos(run.X, y, run.Z));
                    }
                }

                for (int y = landBottom; y <= run.Top; y++)
                {
                    var cell = new VoxelPos(run.X, y, run.Z);
                    world.SetVoxel(cell, y <= landTop ? rubbleId : MaterialIds.AirNumericId);
                }

                if (liftedHere > 0)
                {
                    long remaining = liftedHere;
                    for (int y = landTop + 1; y <= run.Top && remaining > 0; y++)
                    {
                        remaining = water!.Return(new VoxelPos(run.X, y, run.Z), remaining);
                    }

                    if (remaining > 0)
                    {
                        water!.DisplaceLifted(remaining);
                        displaced += remaining;
                    }

                    lifted += liftedHere;
                    returned += liftedHere - remaining;
                }

                voxels += n;
                dropSum += (long)n * gap;
                deepest = Math.Max(deepest, gap);
                columnMoved = true;
            }

            if (columnMoved)
            {
                columns++;
            }

            i = j + 1;
        }

        result = new CollapseResult(chunk, voxels, columns, deepest, dropSum, stale, lifted, returned, displaced, fellFrom, written);
        return voxels > 0 ? CollapseOutcome.Fell : CollapseOutcome.NothingAtRisk;
    }

    /// <summary>
    /// Follows a run that reached its chunk's floor down through the chunk below, and the one
    /// below that, while the voxels are at risk in a settled field. Returns the wait when a chunk
    /// on the way is analysed and not settled; null when the run ended.
    /// </summary>
    private static CollapseOutcome? ExtendBelow(
        SupportRegion region,
        IVoxelEditor world,
        MaterialTable materials,
        ChunkPos chunk,
        int lx,
        int lz,
        ref int bottom,
        HashSet<ChunkPos> fellFrom,
        ref int stale)
    {
        ChunkPos below = chunk.Offset(0, -1, 0);
        while (region.IsAnalysed(below))
        {
            if (!region.TryReadSettled(below, out ChunkSupport lower))
            {
                // Analysed and no settled field to read: unsettled, or not loaded. An unloaded
                // analysed chunk anchors, so nothing above it is at risk and the run has ended;
                // an unsettled one is the wait.
                return region.IsSettled(below) ? null : CollapseOutcome.WaitingUnsettled;
            }

            VoxelPos origin = below.OriginVoxel;
            int columnBase = VoxelPos.Index(lx, 0, lz);
            int ly = VoxelConstants.ChunkSize - 1;
            bool any = false;
            while (ly >= 0 && lower.IsAtRisk(columnBase + ly))
            {
                if (!SolidInWorld(world, materials, new VoxelPos(origin.X + lx, origin.Y + ly, origin.Z + lz)))
                {
                    stale++;
                    if (any)
                    {
                        fellFrom.Add(below);
                    }

                    return null;
                }

                bottom = origin.Y + ly;
                any = true;
                ly--;
            }

            if (any)
            {
                fellFrom.Add(below);
            }

            if (ly >= 0)
            {
                return null;
            }

            below = below.Offset(0, -1, 0);
        }

        return null;
    }

    /// <summary>Vacant voxels under the run's bottom, down to the first solid one or the edge of the loaded world.</summary>
    private static int GapBelow(IVoxelWorld world, MaterialTable materials, Run run)
    {
        int gap = 0;
        int y = run.Bottom - 1;
        while (world.TryGetMaterial(new VoxelPos(run.X, y, run.Z), out int material) && !materials.IsSolid(material))
        {
            gap++;
            y--;
        }

        return gap;
    }

    private static bool SolidInWorld(IVoxelWorld world, MaterialTable materials, VoxelPos voxel) =>
        world.TryGetMaterial(voxel, out int material) && materials.IsSolid(material);
}
