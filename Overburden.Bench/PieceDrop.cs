using Overburden.Core.Content;
using Overburden.Core.Fluid;
using Overburden.Core.Structure;
using Overburden.Core.Voxels;

namespace Overburden.Bench;

/// <summary>
/// COLLAPSE-PLAN a-2's bench arm, driving part b-1's <see cref="CollapseDirector"/>: one fixture
/// dropped in Tetris pieces on a simulated clock — the fuse at a zero window starting and joining
/// events at every half-second boundary, the director's frame at 60 Hz, the region re-settled
/// every quarter second (the game's settle latency, an estimate) — with the oracles checked at
/// every landing. The game runs the same director, so these are its oracles.
/// </summary>
internal static class PieceDrop
{
    private const long FrameMillis = 16;
    private const long SettleMillis = 250;
    private const long BoundaryMillis = 500;
    private const long CapMillis = 600_000;

    internal static void Run(
        string name,
        Dictionary<ChunkPos, VoxelChunk> chunks,
        ChunkPos[] dug,
        IWaterTable? table,
        MaterialTable materials,
        SupportRule rule,
        int rubbleId,
        Func<SupportRegion, string> slice)
    {
        var region = new SupportRegion(chunk => chunks.GetValueOrDefault(chunk), materials, rule, table);
        foreach (ChunkPos chunk in dug)
        {
            region.MarkDug(chunk);
        }

        region.Settle();
        var editor = new Watched(new SupportBench.DictionaryEditor(chunks, region), materials, rubbleId);
        string before = slice(region);
        (long solidBefore, long sumBefore) = SupportBench.Census(chunks, materials);
        int atRiskBefore = region.SettledAtRiskVoxels;

        Console.WriteLine();
        Console.WriteLine($"--- {name}: {atRiskBefore:N0} voxel(s) at risk; {solidBefore:N0} solid, sum(Y) {sumBefore:N0}");
        if (atRiskBefore == 0)
        {
            Console.WriteLine("    nothing at risk: nothing falls.");
            return;
        }

        var director = new CollapseDirector(region, editor, materials, rubbleId, seed: 0);
        var scheduler = new CollapseScheduler(region, editor, materials, rubbleId, 0, new CollapseWindow { MinSeconds = 0, MaxSeconds = 0 })
        {
            Director = director,
        };

        long oracleBroken = 0;
        long descentBroken = 0;
        var byMino = new Dictionary<int, (long Pieces, long Voxels)>();
        director.Observer = (piece, plan, seen) =>
        {
            int lowest = seen.Where(c => c.Descends).Min(c => c.RestBottomY);
            if (plan.Rest.Y > lowest)
            {
                oracleBroken++;
            }

            if (plan.RestVoxels.Sum(v => (long)v.Y) >= piece.Voxels.Sum(v => (long)v.Y))
            {
                descentBroken++;
            }

            (long p, long vx) = byMino.GetValueOrDefault(piece.MinoVoxels);
            byMino[piece.MinoVoxels] = (p + 1, vx + piece.Voxels.Length);
        };

        int peakInFlight = 0;
        int peakEvents = 0;
        long settles = 0;
        long t = 0;
        long lastSettle = 0;
        long sumLast = sumBefore;
        bool dirty = false;
        var summaries = new List<CollapseEventSummary>();
        for (; t <= CapMillis; t += FrameMillis)
        {
            if (t % BoundaryMillis == 0)
            {
                scheduler.Sweep(t);
            }

            CollapseStep step = director.Frame(t);
            dirty |= step.Wrote;
            peakInFlight = Math.Max(peakInFlight, director.InFlight);
            peakEvents = Math.Max(peakEvents, director.Events.Count);
            summaries.AddRange(director.TakeEnded());

            if (director.ReleasedVoxels != director.LandedVoxels + director.InFlightVoxels + director.LostVoxels)
            {
                Console.WriteLine($"    *** IDENTITY BROKEN at t {t} ms: released {director.ReleasedVoxels} != landed {director.LandedVoxels} + in flight {director.InFlightVoxels} + lost {director.LostVoxels} ***");
                return;
            }

            if (dirty && t - lastSettle >= SettleMillis)
            {
                region.Settle();
                settles++;
                lastSettle = t;
                dirty = false;
                if (director.InFlight == 0)
                {
                    (_, long sumNow) = SupportBench.Census(chunks, materials);
                    if (sumNow > sumLast)
                    {
                        descentBroken++;
                    }

                    sumLast = sumNow;
                }
            }

            if (!dirty && director.Events.Count == 0 && scheduler.Armed == 0 && region.SettledAtRiskVoxels == 0)
            {
                break;
            }
        }

        region.Settle();
        (long solidAfter, long sumAfter) = SupportBench.Census(chunks, materials);
        Console.WriteLine(
            $"    {director.Released:N0} piece(s), {director.ReleasedVoxels:N0} voxel(s) in {t / 1000.0:F1} s simulated"
            + (t > CapMillis ? " *** CAP HIT ***" : string.Empty)
            + $"; {director.Started} event(s) started, {director.Joined} chunk(s) joined, peak {peakEvents} live; "
            + $"by mino {string.Join(", ", byMino.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}: {kv.Value.Pieces:N0} ({kv.Value.Voxels:N0} voxels)"))}; "
            + $"peak {peakInFlight} in flight; {director.Waited:N0} waited; {director.RefallVoxels:N0} rubble voxel(s) fell again; "
            + $"rescued {director.RescuedVoxels:N0}; {director.Blocked} blocked; lost {director.LostVoxels}");
        Console.WriteLine(
            $"    solve ms mean {(director.Solves == 0 ? 0 : director.SolveMillisSum / director.Solves):F3} / max {director.SolveMillisMax:F3} over {director.Solves:N0}; "
            + $"{director.Replans:N0} re-plan(s) over {settles:N0} settle(s): gather ms mean {(director.Replans == 0 ? 0 : director.GatherMillisSum / director.Replans):F3} / max {director.GatherMillisMax:F3}, cut ms mean {(director.Replans == 0 ? 0 : director.CutMillisSum / director.Replans):F3} / max {director.CutMillisMax:F3}");
        foreach (CollapseEventSummary s in summaries)
        {
            Console.WriteLine(
                $"    event {s.Id} from {s.Origin}: {s.Members} chunk(s), {s.Pieces:N0} piece(s), {s.Voxels:N0} voxel(s), {s.AgeSeconds:F1} s, "
                + $"peak pace x{s.PeakMultiplier:0.#}, mino {s.PeakMino}, {s.RescuedVoxels:N0} rescued");
        }

        Console.WriteLine(
            $"    oracles: solid {solidBefore:N0} → {solidAfter:N0} ({(solidBefore == solidAfter ? "conserved" : "*** NOT CONSERVED ***")}), "
            + $"sum(Y) {sumBefore:N0} → {sumAfter:N0} ({(sumAfter < sumBefore && descentBroken == 0 ? "down at every landing" : "*** NOT STRICTLY DOWN ***")}), "
            + $"solver {(oracleBroken == 0 ? "never above its own lowest" : $"*** ABOVE ITS LOWEST {oracleBroken} TIME(S) ***")}, "
            + $"{(editor.RubbleOnSolid == 0 ? "no landing on solid" : $"*** {editor.RubbleOnSolid} VOXEL(S) LANDED ON SOLID ***")}; "
            + $"{region.SettledAtRiskVoxels:N0} at risk after");

        Console.WriteLine("    before                                    |   after ('o' is held rubble; '#' at-risk rubble or rock)");
        string[] left = before.Split('\n');
        string[] right = slice(region).Split('\n');
        int width = left.Max(l => l.TrimEnd('\r').Length);
        for (int i = 0; i < Math.Max(left.Length, right.Length); i++)
        {
            string l = i < left.Length ? left[i].TrimEnd('\r') : string.Empty;
            string r = i < right.Length ? right[i].TrimEnd('\r') : string.Empty;
            if (l.Length == 0 && r.Length == 0)
            {
                continue;
            }

            Console.WriteLine($"{l.PadRight(width)}   |{r}");
        }
    }

    /// <summary>The editor, counting rubble written over a solid voxel — the landing oracle, read at the write.</summary>
    private sealed class Watched(IVoxelEditor inner, MaterialTable materials, int rubbleId) : IVoxelEditor
    {
        public long RubbleOnSolid { get; private set; }

        public bool TryGetMaterial(VoxelPos voxel, out int materialId) => inner.TryGetMaterial(voxel, out materialId);

        public bool SetVoxel(VoxelPos voxel, int materialId)
        {
            if (materialId == rubbleId && inner.TryGetMaterial(voxel, out int was) && materials.IsSolid(was))
            {
                RubbleOnSolid++;
            }

            return inner.SetVoxel(voxel, materialId);
        }
    }
}
