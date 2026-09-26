using System.Diagnostics;
using Overburden.Core.Content;
using Overburden.Core.Fluid;
using Overburden.Core.Structure;
using Overburden.Core.Voxels;
using Overburden.Core.Worldgen;

namespace Overburden.Bench;

/// <summary>
/// COLLAPSE-PLAN a-2's bench arm: one fixture dropped in Tetris pieces on a simulated clock — the
/// event releasing at its pace, the solver placing each piece, the flights advancing a frame at a
/// time and landing as rubble, the region re-settled and the queue re-cut every quarter second
/// (the game's settle latency, an estimate) — with the oracles checked at every landing.
/// </summary>
internal static class PieceDrop
{
    private const long FrameMillis = 16;
    private const long ReplanMillis = 250;
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
        var editor = new SupportBench.DictionaryEditor(chunks, region);
        string before = slice(region);
        (long solidBefore, long sumBefore) = SupportBench.Census(chunks, materials);
        (HashSet<VoxelPos> atRisk, VoxelPos origin) = Gather(region, editor, materials);

        Console.WriteLine();
        Console.WriteLine($"--- {name}: {atRisk.Count:N0} voxel(s) at risk; {solidBefore:N0} solid, sum(Y) {sumBefore:N0}");
        if (atRisk.Count == 0)
        {
            Console.WriteLine("    nothing at risk: nothing falls.");
            return;
        }

        var ev = new CollapseEvent(1, origin, 0, CollapsePace.Default, 0);
        ev.Replan(atRisk, 0);
        Console.WriteLine($"    origin {origin}; the first cut: {ev.Queued:N0} piece(s) of 1-voxel minos");

        var solveMillis = new List<double>();
        long candidates = 0;
        long oracleBroken = 0;
        long descentBroken = 0;
        long landedOnSolid = 0;
        long refallVoxels = 0;
        int peakInFlight = 0;
        double settleMillis = 0;
        double gatherMillis = 0;
        int replans = 1;
        var byMino = new Dictionary<int, (long Pieces, long Voxels)>();
        var due = new List<CollapseFlight>();
        long t = 0;
        long lastReplan = 0;
        bool dirty = false;
        long sumLast = sumBefore;
        for (; t <= CapMillis; t += FrameMillis)
        {
            // Releases: as many as the pace has due this frame.
            while (ev.TryRelease(t, piece => StillAtRisk(piece, region, editor, materials), out CollapsePiece piece))
            {
                var seen = new List<LandingCandidate>();
                long started = Stopwatch.GetTimestamp();
                LandingPlan? plan = LandingSolver.Solve(piece, editor, materials, ev.Reserved, ev.Origin, LandingSolver.DefaultSpreadMetres, ev.Seed, seen);
                solveMillis.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                if (plan is null)
                {
                    ev.Wait(piece);
                    break;
                }

                candidates += plan.Candidates;
                int lowest = seen.Where(c => c.Descends).Min(c => c.RestBottomY);
                if (plan.Rest.Y > lowest)
                {
                    oracleBroken++;
                }

                if (plan.RestVoxels.Sum(v => (long)v.Y) >= piece.Voxels.Sum(v => (long)v.Y))
                {
                    descentBroken++;
                }

                foreach (VoxelPos v in piece.Voxels)
                {
                    editor.TryGetMaterial(v, out int m);
                    if (m == rubbleId)
                    {
                        refallVoxels++;
                    }

                    editor.SetVoxel(v, MaterialIds.AirNumericId);
                }

                ev.Launch(piece, plan, t);
                (long p, long vx) = byMino.GetValueOrDefault(piece.MinoVoxels);
                byMino[piece.MinoVoxels] = (p + 1, vx + piece.Voxels.Length);
                dirty = true;
            }

            peakInFlight = Math.Max(peakInFlight, ev.InFlight.Count);

            // Landings.
            due.Clear();
            ev.Advance(t, due);
            foreach (CollapseFlight flight in due)
            {
                foreach (VoxelPos v in flight.Plan.RestVoxels)
                {
                    if (editor.TryGetMaterial(v, out int m) && materials.IsSolid(m))
                    {
                        landedOnSolid++;
                    }

                    editor.SetVoxel(v, rubbleId);
                }

                ev.Land(flight);
                dirty = true;
            }

            if (ev.ReleasedVoxels != ev.LandedVoxels + ev.InFlightVoxels)
            {
                Console.WriteLine($"    *** IDENTITY BROKEN at t {t} ms: released {ev.ReleasedVoxels} != landed {ev.LandedVoxels} + in flight {ev.InFlightVoxels} ***");
                return;
            }

            // The settle and the re-cut, at the game's latency.
            if ((dirty && t - lastReplan >= ReplanMillis) || ev.MinoChanged(t))
            {
                long started = Stopwatch.GetTimestamp();
                region.Settle();
                settleMillis += Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                started = Stopwatch.GetTimestamp();
                (atRisk, _) = Gather(region, editor, materials);
                gatherMillis += Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                ev.Replan(atRisk, t);
                replans++;
                lastReplan = t;
                dirty = false;
                if (ev.InFlight.Count == 0)
                {
                    (_, long sumNow) = SupportBench.Census(chunks, materials);
                    if (sumNow > sumLast)
                    {
                        descentBroken++;
                    }

                    sumLast = sumNow;
                }
            }

            if (ev.IsIdle && !dirty && atRisk.Count == 0)
            {
                break;
            }
        }

        region.Settle();
        (long solidAfter, long sumAfter) = SupportBench.Census(chunks, materials);
        double mean = solveMillis.Count == 0 ? 0 : solveMillis.Average();
        double max = solveMillis.Count == 0 ? 0 : solveMillis.Max();
        double p99 = solveMillis.Count == 0 ? 0 : solveMillis.OrderBy(x => x).ElementAt((int)(solveMillis.Count * 0.99));
        Console.WriteLine(
            $"    {ev.Released:N0} piece(s), {ev.ReleasedVoxels:N0} voxel(s) in {t / 1000.0:F1} s simulated"
            + (t > CapMillis ? " *** CAP HIT ***" : string.Empty)
            + $"; by mino {string.Join(", ", byMino.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}: {kv.Value.Pieces:N0} ({kv.Value.Voxels:N0} voxels)"))}; "
            + $"peak {peakInFlight} in flight; {ev.Waited:N0} waited; {refallVoxels:N0} rubble voxel(s) fell again; rescued {ev.RescuedVoxels:N0}");
        Console.WriteLine(
            $"    solve ms mean {mean:F3} / p99 {p99:F3} / max {max:F3} over {solveMillis.Count:N0} (candidates {(ev.Released == 0 ? 0 : candidates / ev.Released):N0} a piece); "
            + $"{replans:N0} re-plan(s): settle {settleMillis:F0} ms, gather {gatherMillis:F0} ms in all");
        Console.WriteLine(
            $"    oracles: solid {solidBefore:N0} → {solidAfter:N0} ({(solidBefore == solidAfter ? "conserved" : "*** NOT CONSERVED ***")}), "
            + $"sum(Y) {sumBefore:N0} → {sumAfter:N0} ({(sumAfter < sumBefore && descentBroken == 0 ? "down at every landing" : "*** NOT STRICTLY DOWN ***")}), "
            + $"solver {(oracleBroken == 0 ? "never above its own lowest" : $"*** ABOVE ITS LOWEST {oracleBroken} TIME(S) ***")}, "
            + $"{(landedOnSolid == 0 ? "no landing on solid" : $"*** {landedOnSolid} VOXEL(S) LANDED ON SOLID ***")}; "
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

    /// <summary>Every solid voxel the settled fields mark at risk, and the one with the largest deficit (lowest, then hashed) as the origin.</summary>
    private static (HashSet<VoxelPos> AtRisk, VoxelPos Origin) Gather(SupportRegion region, IVoxelWorld world, MaterialTable materials)
    {
        var set = new HashSet<VoxelPos>();
        VoxelPos origin = default;
        (int Deficit, int NegY, ulong Tie) best = (-1, 0, 0);
        foreach ((ChunkPos chunk, ChunkSupport field) in region.SettledFields)
        {
            if (field.AtRiskCount == 0)
            {
                continue;
            }

            VoxelPos o = chunk.OriginVoxel;
            for (int i = 0; i < VoxelConstants.ChunkVolume; i++)
            {
                byte value = field.Value(i);
                if (!ChunkSupport.IsAtRisk(value))
                {
                    continue;
                }

                (int lx, int ly, int lz) = VoxelPos.FromIndex(i);
                var v = new VoxelPos(o.X + lx, o.Y + ly, o.Z + lz);
                if (!world.TryGetMaterial(v, out int m) || !materials.IsSolid(m))
                {
                    continue;
                }

                set.Add(v);
                (int, int, ulong) score = (ChunkSupport.Deficit(value), -v.Y, ~Hash64.Combine(0, v.X, v.Y, v.Z));
                if (score.CompareTo(best) > 0)
                {
                    best = score;
                    origin = v;
                }
            }
        }

        return (set, origin);
    }

    private static bool StillAtRisk(CollapsePiece piece, SupportRegion region, IVoxelWorld world, MaterialTable materials)
    {
        foreach (VoxelPos v in piece.Voxels)
        {
            if (!world.TryGetMaterial(v, out int m) || !materials.IsSolid(m))
            {
                return false;
            }

            ChunkSupport? field = region.SettledField(v.Chunk);
            if (field is null || !field.IsAtRisk(v.LocalIndex))
            {
                return false;
            }
        }

        return true;
    }
}
