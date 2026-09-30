using System.Diagnostics;
using System.Text;
using Overburden.Core.Content;
using Overburden.Core.Fluid;
using Overburden.Core.Structure;
using Overburden.Core.Voxels;
using Overburden.Core.Worldgen;

namespace Overburden.Bench;

/// <summary>
/// M9a: the support field on real strata — the plan's seven fixtures, the derive's cost, and an
/// ASCII cross-section of each wedge, because the table in decision 1 is arithmetic and the eye
/// has to agree before the rule is believed.
/// </summary>
/// <remarks>
/// <para>
/// Each fixture is a box of generated chunks in a dictionary with bites taken out of it, the
/// analysed set built the way the game will build it — dug chunks and their six face neighbours —
/// and settled by <see cref="SupportRegion"/>, which is the join M9b puts on jobs. Chunks outside
/// the box are not loaded and anchor; chunks inside but outside the analysed set anchor through
/// their solid border. The real mod's IDs are in the strata, so stone is stone because
/// <c>stone.json</c> says 30.
/// </para>
/// <para>
/// <b>The first settle is cold and the steady figure is the best of three fresh regions</b>, each
/// deriving every analysed chunk from nothing; the gather (the 27-chunk padded copy and the six
/// layers) and the search are timed apart, per derive and per searched derive — the plan's
/// estimate was 1–3 ms Release for the search over 32,768 voxels.
/// </para>
/// <para>
/// <b>Run it with <c>DOTNET_TieredCompilation=0</c> for the figure that means anything.</b> With
/// tiering on, the search read 3.2 ms on the first two rows and 0.8 on the rest in one run, and
/// 2.5–4 ms on every row in the next — the derive at tier 0, and its promotion postponed for as
/// long as the process keeps jitting new methods, which a bench walking through fixtures does.
/// No warm-up by call count fixes that (<c>--watershed</c>'s trick is for OSR, a different
/// delay); tiering off is 0.8–1.1 ms on every row, and the line under the table says which arm
/// this is.
/// </para>
/// </remarks>
internal static class SupportBench
{
    private sealed class FlatTable(double? level) : IWaterTable
    {
        public double? TableAt(int sampleX, int sampleZ) => level;
    }

    private sealed class Fixture
    {
        public required string Name { get; init; }
        public required Dictionary<ChunkPos, VoxelChunk> Chunks { get; init; }
        public required ChunkPos[] Dug { get; init; }
        public required int SliceZ { get; init; }
        public required int SliceMinX { get; init; }
        public required int SliceMaxX { get; init; }
        public required int SliceMinY { get; init; }
        public required int SliceMaxY { get; init; }
        public IWaterTable? Table { get; init; }
        public string? Note { get; init; }
    }

    internal static int Run(
        WorldGenerator generator,
        MaterialTable materials,
        int stoneId,
        int dirtId,
        int grassId,
        string machineControl,
        bool isDebug,
        int restRefund,
        float supportScale,
        int rubbleId = -1,
        bool pieces = false)
    {
        Console.WriteLine("Support bench — M9a's field over the plan's seven fixtures");
        if (isDebug)
        {
            Console.WriteLine("*** DEBUG BUILD — these timings are meaningless. Re-run with -c Release. ***");
        }

        Console.WriteLine();
        Console.WriteLine(machineControl);
        string? tiering = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation");
        Console.WriteLine(
            tiering == "0"
                ? "  JIT tiering off (DOTNET_TieredCompilation=0): the search's figure is the optimised code's."
                : "  *** JIT tiering on: the search's figure can read 3x while Derive sits at tier 0 — re-run with DOTNET_TieredCompilation=0. ***");
        Console.WriteLine();

        var rule = new SupportRule { RestRefundVoxels = restRefund, DistanceScale = supportScale };
        Console.WriteLine(
            $"  Rule: K {SupportRule.MaxBudget}, refund {rule.Refund} per voxel of rest (RestRefundVoxels {rule.RestRefundVoxels}), "
            + $"distance scale {rule.DistanceScale}; stone {materials.SupportDistance(stoneId)} carries at {rule.Cost(materials.SupportDistance(stoneId))} a step, "
            + $"dirt {materials.SupportDistance(dirtId)} at {rule.Cost(materials.SupportDistance(dirtId))}, "
            + $"grass {materials.SupportDistance(grassId)} at {rule.Cost(materials.SupportDistance(grassId))}; "
            + $"hanging stone {rule.HangCost(rule.Cost(materials.SupportDistance(stoneId)))}.");
        Console.WriteLine(
            $"  Stone under head: {string.Join(", ", new[] { 0, 2, 5, 10 }.Select(h => $"{h} m → {materials.SupportDistanceAt(stoneId, h):F1} ({rule.Cost(materials.SupportDistanceAt(stoneId, h))} a step)"))}");
        Console.WriteLine();

        int surface = generator.SurfaceHeightAt(16, 16);
        int surfaceChunkY = FloorDiv(surface, VoxelConstants.ChunkSize);

        // A bite "8 m below the surface": its top face at least 32 voxels under the surface at
        // the centre, so the roof chunk is rock throughout.
        int deepY = FloorDiv(surface - 32, VoxelConstants.ChunkSize) - 1;

        var fixtures = new List<Fixture>
        {
            Room("1a. one bite in stone", generator, deepY, 1, 1),
            Room("1b. a row of four bites", generator, deepY, 4, 1),
            Room("2.  a 2 x 2 square (16 m)", generator, deepY, 2, 2),
            Room("3.  a 4 x 4 square (32 m)", generator, deepY, 4, 4),
            SoilRoof(generator, surfaceChunkY, materials, stoneId),
        };

        // 5. The 2 x 2 under head: the underside of the roof is the bite's top row plus one.
        int roofUnderside = ((deepY + 1) * VoxelConstants.ChunkSize);
        foreach (int head in new[] { 2, 5, 10 })
        {
            double level = roofUnderside + 0.5 + (head * VoxelConstants.VoxelsPerMetre);
            fixtures.Add(Room($"5.  the 2 x 2 under {head} m of head", generator, deepY, 2, 2, new FlatTable(level),
                $"table at Y {level:F1}, {head} m over the underside at Y {roofUnderside}"));
        }

        fixtures.Add(MidAir(generator, surfaceChunkY + 2, stoneId));
        fixtures.Add(CaveShell(generator, materials, deepY));

        Console.WriteLine("fixture                              analysed  derives (searched)  rounds   first ms   steady ms   gather ms/derive   search ms/derive   pops/search  pushes/search  at risk (chunks)   field KiB");
        Console.WriteLine("-------                              --------  ------------------  ------   --------   ---------   ----------------   ----------------   -----------  -------------  ----------------   ---------");

        // Warm the derive up before anything is timed — by call count, as --watershed learned to,
        // because the JIT re-tiers a method after thirty calls: with four warm settles the first
        // two rows read 3.1 ms a search and every row after them 0.8.
        {
            Fixture warm = fixtures[0];
            for (int rep = 0; rep < 24; rep++)
            {
                var region = new SupportRegion(chunk => warm.Chunks.GetValueOrDefault(chunk), materials, rule, warm.Table);
                foreach (ChunkPos chunk in warm.Dug) region.MarkDug(chunk);
                region.Settle();
            }
        }

        var sections = new StringBuilder();
        foreach (Fixture fixture in fixtures)
        {
            VoxelChunk? Voxels(ChunkPos chunk) => fixture.Chunks.GetValueOrDefault(chunk);

            SupportRegion Fresh()
            {
                var region = new SupportRegion(Voxels, materials, rule, fixture.Table);
                foreach (ChunkPos chunk in fixture.Dug)
                {
                    region.MarkDug(chunk);
                }

                return region;
            }

            SupportRegion first = Fresh();
            long started = Stopwatch.GetTimestamp();
            int rounds = first.Settle();
            double firstMillis = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

            double steadyMillis = double.MaxValue;
            SupportRegion last = first;
            for (int rep = 0; rep < 3; rep++)
            {
                SupportRegion region = Fresh();
                started = Stopwatch.GetTimestamp();
                region.Settle();
                steadyMillis = Math.Min(steadyMillis, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                last = region;
            }

            List<string> violations = last.VerifyFixpoint();
            string verdict = violations.Count == 0 ? "" : $"   *** FIXPOINT VIOLATED: {violations[0]} ***";
            long searched = last.Derives - last.TrivialDerives;
            double gatherMillis = last.GatherTicks * 1000.0 / Stopwatch.Frequency;
            double deriveMillis = last.DeriveTicks * 1000.0 / Stopwatch.Frequency;
            Console.WriteLine(
                $"{fixture.Name,-36} {last.Analysed.Count,8:N0}  {last.Derives,7:N0} ({searched,3})       {rounds,6}   {firstMillis,8:F1}   {steadyMillis,9:F1}   "
                + $"{gatherMillis / Math.Max(1, last.Derives),16:F2}   {(searched == 0 ? 0 : deriveMillis / searched),16:F2}   {(searched == 0 ? 0 : last.Pops / searched),11:N0}  {(searched == 0 ? 0 : last.Pushes / searched),13:N0}  "
                + $"{last.AtRiskVoxels,8:N0} ({last.AtRiskChunks,2})   {last.FieldBytes / 1024.0,9:F0}{verdict}");

            sections.AppendLine();
            sections.AppendLine($"--- {fixture.Name}: {fixture.Note}");
            sections.AppendLine(
                $"    slice at z = {fixture.SliceZ}, x {fixture.SliceMinX}..{fixture.SliceMaxX}, y {fixture.SliceMaxY} down to {fixture.SliceMinY}; "
                + "' ' air, '.' held, '-' held with nothing to spare (0), '#' at risk, 'X' unreached, ':' outside the analysed set, '|' not loaded");
            sections.Append(Slice(last, fixture, materials));
        }

        Console.WriteLine();
        Console.WriteLine("Cross-sections through each room's middle. The eye has to agree with the table before the rule is believed.");
        Console.Write(sections);

        if (rubbleId >= 0 && pieces)
        {
            Console.WriteLine();
            Console.WriteLine("The collapse in pieces (--pieces): every fixture cut into Tetris pieces and dropped on a simulated clock at 60 frames a second — the cutter's front, the solver's lowest-spot-and-spread, the event's pace.");
            Console.WriteLine("The oracles: solid voxels conserved, sum(Y) strictly down at every landing, released == landed + in flight, and no piece resting above any descending placement its own search simulated.");
            foreach (Fixture fixture in fixtures)
            {
                // The picture reaches the floor the rubble lands on, as Drop's does.
                ChunkPos lowestDug = fixture.Dug.MinBy(c => c.Y);
                int lowY = lowestDug.OriginVoxel.Y - 3;
                if (!(fixture.Chunks.GetValueOrDefault(lowestDug.Offset(0, -1, 0)) is { IsUniform: true } under && materials.IsSolid(under.UniformValue)))
                {
                    lowY -= VoxelConstants.ChunkSize;
                }

                Fixture shown = new()
                {
                    Name = fixture.Name, Chunks = fixture.Chunks, Dug = fixture.Dug, SliceZ = fixture.SliceZ,
                    SliceMinX = fixture.SliceMinX, SliceMaxX = fixture.SliceMaxX, SliceMinY = Math.Min(fixture.SliceMinY, lowY),
                    SliceMaxY = fixture.SliceMaxY, Table = fixture.Table, Note = fixture.Note,
                };
                PieceDrop.Run(fixture.Name, fixture.Chunks, fixture.Dug, fixture.Table, materials, rule, rubbleId,
                    region => Slice(region, shown, materials, rubbleId));
            }

            return 0;
        }

        if (rubbleId >= 0)
        {
            // Fixture 2 again, flooded: a fresh room, because the dry one above has been dropped.
            Fixture flooded = Room("2f. the 2 x 2 square, flooded to the ceiling", generator, deepY, 2, 2,
                note: "fixture 2 with every cell of its bites full through FluidGrid.Fill before the roof falls");
            Console.WriteLine();
            Console.WriteLine("The collapse (--collapse): every fixture dropped stage by stage with a zero delay — a sweep, a settle, a sweep — until nothing is at risk.");
            Console.WriteLine("The oracles are solid voxels conserved and sum(Y) strictly down at every stage; the picture after is beside the picture before.");
            foreach (Fixture fixture in fixtures)
            {
                Drop(fixture, materials, rule, rubbleId, flooded: false);
            }

            Drop(flooded, materials, rule, rubbleId, flooded: true);
        }

        return 0;
    }

    /// <summary>
    /// The fixture's dictionary as the world an editor writes: every write tells the region
    /// (the pipeline's <c>Support.Edited</c>) and the automaton (<c>Disturb</c>), the hook chain
    /// in miniature — the test world's editor, over the bench's chunks.
    /// </summary>
    internal sealed class DictionaryEditor(Dictionary<ChunkPos, VoxelChunk> chunks, SupportRegion region) : IVoxelEditor
    {
        public FluidSimulation? Fluid { get; set; }

        public int Writes { get; private set; }

        public bool TryGetMaterial(VoxelPos voxel, out int materialId)
        {
            if (!chunks.TryGetValue(voxel.Chunk, out VoxelChunk? chunk))
            {
                materialId = MaterialIds.AirNumericId;
                return false;
            }

            (int lx, int ly, int lz) = voxel.Local;
            materialId = chunk.Get(lx, ly, lz);
            return true;
        }

        public bool SetVoxel(VoxelPos voxel, int materialId)
        {
            if (!chunks.TryGetValue(voxel.Chunk, out VoxelChunk? chunk))
            {
                return false;
            }

            (int lx, int ly, int lz) = voxel.Local;
            if (chunk.Get(lx, ly, lz) == materialId)
            {
                return true;
            }

            chunk.Set(lx, ly, lz, materialId);
            Writes++;
            region.Edited(voxel.Chunk);
            Fluid?.Disturb(voxel);
            return true;
        }
    }

    /// <summary>Solid voxels and the sum of their Y over the fixture — decision 6's two oracles.</summary>
    internal static (long Solid, long SumY) Census(Dictionary<ChunkPos, VoxelChunk> chunks, MaterialTable materials)
    {
        long solid = 0;
        long sumY = 0;
        foreach ((ChunkPos position, VoxelChunk chunk) in chunks)
        {
            long originY = position.OriginVoxel.Y;
            if (chunk.IsUniform)
            {
                if (materials.IsSolid(chunk.UniformValue))
                {
                    const long n = VoxelConstants.ChunkSize;
                    solid += VoxelConstants.ChunkVolume;
                    sumY += n * n * ((n * originY) + (n * (n - 1) / 2));
                }

                continue;
            }

            for (int lz = 0; lz < VoxelConstants.ChunkSize; lz++)
            for (int lx = 0; lx < VoxelConstants.ChunkSize; lx++)
            for (int ly = 0; ly < VoxelConstants.ChunkSize; ly++)
            {
                if (materials.IsSolid(chunk.Get(lx, ly, lz)))
                {
                    solid++;
                    sumY += originY + ly;
                }
            }
        }

        return (solid, sumY);
    }

    /// <summary>
    /// Drops one fixture: a fresh region settled over its chunks, the scheduler with a zero
    /// window, then sweep → settle → sweep until nothing is at risk (or a stage cap), printing
    /// each stage's falls, <c>Σ(Y)</c> before and after, the count identity and the fixpoint,
    /// and the picture after beside the one the table printed. Flooded, the room's cells are
    /// filled first and the water's identity and its ticks to rest are printed after.
    /// </summary>
    private static void Drop(Fixture fixture, MaterialTable materials, SupportRule rule, int rubbleId, bool flooded)
    {
        var region = new SupportRegion(chunk => fixture.Chunks.GetValueOrDefault(chunk), materials, rule, fixture.Table);
        foreach (ChunkPos chunk in fixture.Dug)
        {
            region.MarkDug(chunk);
        }

        region.Settle();
        var editor = new DictionaryEditor(fixture.Chunks, region);
        FluidSimulation? fluid = null;
        long waterBefore = 0;
        if (flooded)
        {
            fluid = new FluidSimulation(new VoxelFluidTerrain(editor, materials));
            editor.Fluid = fluid;
            foreach (ChunkPos bite in fixture.Dug)
            {
                VoxelPos origin = bite.OriginVoxel;
                for (int lz = 0; lz < VoxelConstants.ChunkSize; lz++)
                for (int lx = 0; lx < VoxelConstants.ChunkSize; lx++)
                for (int ly = 0; ly < VoxelConstants.ChunkSize; ly++)
                {
                    fluid.Grid.Fill(origin.Offset(lx, ly, lz), FluidConstants.CellCapacityMillilitres);
                }
            }

            waterBefore = fluid.TotalMillilitres;
        }

        var scheduler = new CollapseScheduler(
            region, editor, materials, rubbleId, seed: 0, new CollapseWindow { MinSeconds = 0, MaxSeconds = 0 }, fluid);

        // The table's picture stops three rows under the roof; the rubble lands on the floor,
        // so both pictures here reach it — and a chunk further when what is under the lowest
        // dug chunk is not solid rock (the mid-air fill lands on the surface a chunk down).
        ChunkPos lowestDug = fixture.Dug.MinBy(c => c.Y);
        int lowY = lowestDug.OriginVoxel.Y - 3;
        if (!(fixture.Chunks.GetValueOrDefault(lowestDug.Offset(0, -1, 0)) is { IsUniform: true } under && materials.IsSolid(under.UniformValue)))
        {
            lowY -= VoxelConstants.ChunkSize;
        }

        fixture = new Fixture
        {
            Name = fixture.Name,
            Chunks = fixture.Chunks,
            Dug = fixture.Dug,
            SliceZ = fixture.SliceZ,
            SliceMinX = fixture.SliceMinX,
            SliceMaxX = fixture.SliceMaxX,
            SliceMinY = Math.Min(fixture.SliceMinY, lowY),
            SliceMaxY = fixture.SliceMaxY,
            Table = fixture.Table,
            Note = fixture.Note,
        };
        string before = Slice(region, fixture, materials);
        (long solidBefore, long sumBefore) = Census(fixture.Chunks, materials);
        Console.WriteLine();
        Console.WriteLine($"--- {fixture.Name}: {region.SettledAtRiskVoxels:N0} voxel(s) at risk in {region.SettledAtRiskChunks:N0} chunk(s) before; "
                          + $"{solidBefore:N0} solid, sum(Y) {sumBefore:N0}"
                          + (flooded ? $"; {waterBefore / (double)FluidConstants.MillilitresPerLitre:N0} L in the room" : string.Empty));
        if (region.SettledAtRiskVoxels == 0)
        {
            Console.WriteLine("    nothing at risk: nothing falls, and the picture is the one above.");
            return;
        }

        Console.WriteLine("    stage   chunk                     voxels   columns   deepest   mean drop   sum(Y) after      ms" + (flooded ? "   lifted L   returned L" : string.Empty));
        long sumLast = sumBefore;
        int stage = 0;
        int stages = 0;
        long voxelsInAll = 0;
        double millisInAll = 0;
        bool broken = false;
        const int maxStages = 64;
        for (; stage < maxStages; stage++)
        {
            long started = Stopwatch.GetTimestamp();
            IReadOnlyList<CollapseFire> fires = scheduler.Sweep(stage * 1000L);
            double millis = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (fires.Count == 0)
            {
                if (scheduler.Due == 0 && scheduler.WaitingUnsettled == 0)
                {
                    break;
                }

                // Something is due and waiting on a chunk under it: settle and try again.
                region.Settle();
                continue;
            }

            stages++;
            millisInAll += millis;
            (long solidNow, long sumNow) = Census(fixture.Chunks, materials);
            foreach (CollapseFire fire in fires)
            {
                CollapseResult r = fire.Result;
                voxelsInAll += r.Voxels;
                Console.WriteLine(
                    $"    {stages,5}   {r.Chunk,-24} {r.Voxels,8:N0}  {r.Columns,8:N0}  {r.DeepestDrop,8}  {r.MeanDrop,10:F1}   {sumNow,13:N0}  {millis / fires.Count,6:F1}"
                    + (flooded ? $"   {r.LiftedMillilitres / (double)FluidConstants.MillilitresPerLitre,8:N0}   {r.ReturnedMillilitres / (double)FluidConstants.MillilitresPerLitre,10:N0}" : string.Empty)
                    + (r.StaleSkipped > 0 ? $"   *** {r.StaleSkipped} STALE SKIPPED ***" : string.Empty)
                    + (r.DisplacedMillilitres > 0 ? $"   *** {r.DisplacedMillilitres} mL DISPLACED ***" : string.Empty));
            }

            if (solidNow != solidBefore || sumNow >= sumLast)
            {
                Console.WriteLine($"    *** ORACLE BROKEN at stage {stages}: solid {solidBefore:N0} → {solidNow:N0}, sum(Y) {sumLast:N0} → {sumNow:N0} ***");
                broken = true;
            }

            sumLast = sumNow;
            region.Settle();
            List<string> violations = region.VerifyFixpoint();
            if (violations.Count > 0)
            {
                Console.WriteLine($"    *** FIXPOINT VIOLATED after stage {stages}: {violations[0]} ***");
                broken = true;
            }
        }

        (long solidAfter, long sumAfter) = Census(fixture.Chunks, materials);
        Console.WriteLine(
            $"    {scheduler.Fired:N0} fall(s) in {stages:N0} stage(s) over {stage:N0} sweep(s), {voxelsInAll:N0} voxel(s) fallen (mean drop {(scheduler.VoxelsFallen > 0 ? scheduler.DropSum / (double)scheduler.VoxelsFallen : 0):F1}, "
            + $"deepest {scheduler.DeepestDrop}), {millisInAll:F1} ms in the falls; solid {solidBefore:N0} → {solidAfter:N0} "
            + $"({(solidAfter == solidBefore ? "conserved" : "*** NOT CONSERVED ***")}), sum(Y) {sumBefore:N0} → {sumAfter:N0}; "
            + $"{region.SettledAtRiskVoxels:N0} at risk after"
            + (stage >= maxStages ? $" *** STAGE CAP {maxStages} HIT ***" : string.Empty)
            + (broken ? " *** SEE ABOVE ***" : string.Empty));

        if (fluid is not null)
        {
            long started = Stopwatch.GetTimestamp();
            FluidSettleResult rest = fluid.Settle(maxTicks: 20_000);
            double restMillis = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            bool exact = fluid.TotalMillilitres == waterBefore && fluid.LiftedMillilitres == fluid.ReturnedMillilitres + fluid.DisplacedMillilitres;
            Console.WriteLine(
                $"    water: lifted {fluid.LiftedMillilitres / (double)FluidConstants.MillilitresPerLitre:N0} L, returned {fluid.ReturnedMillilitres / (double)FluidConstants.MillilitresPerLitre:N0} L, "
                + $"displaced {fluid.DisplacedMillilitres} mL, total {waterBefore / (double)FluidConstants.MillilitresPerLitre:N0} → {fluid.TotalMillilitres / (double)FluidConstants.MillilitresPerLitre:N0} L "
                + $"({(exact ? "exact" : "*** NO ***")}); {rest.Ticks:N0} tick(s) to rest ({(rest.Settled ? "real" : "*** BUDGET HIT ***")}, "
                + $"{rest.CellVisits:N0} visits, peak {rest.PeakActiveCells:N0} active) in {restMillis:F1} ms; quiescent {(fluid.VerifyQuiescent() ? "yes" : "*** NO ***")}");
        }

        Console.WriteLine("    before (as the table printed it)          |   after the last stage ('o' is held rubble; '#' at-risk rubble or rock)");
        string after = Slice(region, fixture, materials, rubbleId);
        string[] left = before.Split('\n');
        string[] right = after.Split('\n');
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

    /// <summary>A room of n × m bites at chunk row <paramref name="chunkY"/>, walls and roof generated around it.</summary>
    private static Fixture Room(
        string name, WorldGenerator generator, int chunkY, int bitesX, int bitesZ, IWaterTable? table = null, string? note = null)
    {
        var chunks = new Dictionary<ChunkPos, VoxelChunk>();
        var dug = new List<ChunkPos>();
        for (int cz = -2; cz <= bitesZ + 1; cz++)
        for (int cy = chunkY - 2; cy <= chunkY + 2; cy++)
        for (int cx = -2; cx <= bitesX + 1; cx++)
        {
            var position = new ChunkPos(cx, cy, cz);
            chunks[position] = generator.GenerateTerrain(position);
        }

        for (int cz = 0; cz < bitesZ; cz++)
        for (int cx = 0; cx < bitesX; cx++)
        {
            var bite = new ChunkPos(cx, chunkY, cz);
            chunks[bite] = new VoxelChunk(MaterialIds.AirNumericId);
            dug.Add(bite);
        }

        int undersideY = (chunkY + 1) * VoxelConstants.ChunkSize;
        return new Fixture
        {
            Name = name,
            Chunks = chunks,
            Dug = [.. dug],
            SliceZ = (bitesZ * VoxelConstants.ChunkSize / 2),
            SliceMinX = -8,
            SliceMaxX = (bitesX * VoxelConstants.ChunkSize) + 7,
            SliceMinY = undersideY - 3,
            SliceMaxY = undersideY + VoxelConstants.ChunkSize + 3,
            Table = table,
            Note = note ?? $"{bitesX} x {bitesZ} chunk(s) of air at chunk row {chunkY}, roof underside at Y {undersideY}",
        };
    }

    /// <summary>
    /// One bite whose roof is the shipped soil: the surface chunk sits over it, and the search
    /// looks for a chunk column near the origin where the soil's four voxels are the roof chunk's
    /// bottom four rows across most of the footprint.
    /// </summary>
    private static Fixture SoilRoof(WorldGenerator generator, int surfaceChunkY, MaterialTable materials, int stoneId)
    {
        // The soil is grass over three of dirt: surface − 3 .. surface. The roof chunk's bottom
        // row is 32 · Y; we want surface − 3 == 32 · Y for as much of the 32 × 32 footprint as
        // the terrace allows, so score columns by how many of their samples land exactly.
        ChunkPos best = default;
        int bestScore = -1;
        for (int cz = -6; cz <= 6; cz++)
        for (int cx = -6; cx <= 6; cx++)
        {
            int score = 0;
            int roofY = int.MinValue;
            for (int lz = 0; lz < VoxelConstants.ChunkSize; lz += 4)
            for (int lx = 0; lx < VoxelConstants.ChunkSize; lx += 4)
            {
                int s = generator.SurfaceHeightAt((cx * VoxelConstants.ChunkSize) + lx, (cz * VoxelConstants.ChunkSize) + lz);
                int want = FloorDiv(s - 3, VoxelConstants.ChunkSize) * VoxelConstants.ChunkSize;
                if (roofY == int.MinValue) roofY = want;
                if (want == roofY && s - 3 == roofY) score++;
            }

            if (score > bestScore)
            {
                bestScore = score;
                int centre = generator.SurfaceHeightAt((cx * VoxelConstants.ChunkSize) + 16, (cz * VoxelConstants.ChunkSize) + 16);
                best = new ChunkPos(cx, FloorDiv(centre - 3, VoxelConstants.ChunkSize) - 1, cz);
            }
        }

        var chunks = new Dictionary<ChunkPos, VoxelChunk>();
        for (int cz = best.Z - 2; cz <= best.Z + 2; cz++)
        for (int cy = best.Y - 2; cy <= best.Y + 2; cy++)
        for (int cx = best.X - 2; cx <= best.X + 2; cx++)
        {
            var position = new ChunkPos(cx, cy, cz);
            chunks[position] = generator.GenerateTerrain(position);
        }

        chunks[best] = new VoxelChunk(MaterialIds.AirNumericId);
        int undersideY = (best.Y + 1) * VoxelConstants.ChunkSize;
        VoxelPos origin = best.OriginVoxel;
        return new Fixture
        {
            Name = "4.  one bite with a soil roof",
            Chunks = chunks,
            Dug = [best],
            SliceZ = origin.Z + 16,
            SliceMinX = origin.X - 8,
            SliceMaxX = origin.X + VoxelConstants.ChunkSize + 7,
            SliceMinY = undersideY - 3,
            SliceMaxY = undersideY + 12,
            Note = $"bite {best}, roof underside at Y {undersideY}; {bestScore} of 64 surface samples put the soil's bottom exactly on it "
                + $"(surface at the centre {generator.SurfaceHeightAt(origin.X + 16, origin.Z + 16)})",
        };
    }

    private static Fixture MidAir(WorldGenerator generator, int chunkY, int stoneId)
    {
        var chunks = new Dictionary<ChunkPos, VoxelChunk>();
        for (int cz = -1; cz <= 1; cz++)
        for (int cy = chunkY - 1; cy <= chunkY + 1; cy++)
        for (int cx = -1; cx <= 1; cx++)
        {
            var position = new ChunkPos(cx, cy, cz);
            chunks[position] = generator.GenerateTerrain(position);
        }

        var filled = new ChunkPos(0, chunkY, 0);
        chunks[filled] = new VoxelChunk(stoneId);
        VoxelPos origin = filled.OriginVoxel;
        return new Fixture
        {
            Name = "6.  a chunk filled in mid-air",
            Chunks = chunks,
            Dug = [filled],
            SliceZ = 16,
            SliceMinX = -4,
            SliceMaxX = VoxelConstants.ChunkSize + 3,
            SliceMinY = origin.Y - 2,
            SliceMaxY = origin.Y + VoxelConstants.ChunkSize + 1,
            Note = $"chunk {filled} filled with stone above the surface, air on every side",
        };
    }

    /// <summary>
    /// A bite deep enough for the cave field, in the chunk whose six face neighbours hold the
    /// most cave air — the shell M8c's portal rule wet, asked of the roof in advance.
    /// </summary>
    private static Fixture CaveShell(WorldGenerator generator, MaterialTable materials, int deepY)
    {
        ChunkPos best = default;
        int bestCaves = -1;
        var cache = new Dictionary<ChunkPos, VoxelChunk>();
        VoxelChunk At(ChunkPos p)
        {
            if (!cache.TryGetValue(p, out VoxelChunk? c))
            {
                c = generator.GenerateTerrain(p);
                cache[p] = c;
            }

            return c;
        }

        // Cave air only: air more than the cave margin under the surface of its column, so a
        // roof chunk whose top breaks the surface does not score for the sky.
        int CaveAir(ChunkPos p)
        {
            VoxelChunk chunk = At(p);
            if (chunk.IsUniform && chunk.UniformValue != MaterialIds.AirNumericId) return 0;
            VoxelPos origin = p.OriginVoxel;
            int air = 0;
            for (int lz = 0; lz < VoxelConstants.ChunkSize; lz++)
            for (int lx = 0; lx < VoxelConstants.ChunkSize; lx++)
            {
                int surface = generator.SurfaceHeightAt(origin.X + lx, origin.Z + lz) - generator.Settings.CaveSurfaceMarginVoxels;
                for (int ly = 0; ly < VoxelConstants.ChunkSize; ly++)
                {
                    if (origin.Y + ly < surface && chunk.Get(lx, ly, lz) == MaterialIds.AirNumericId) air++;
                }
            }

            return air;
        }

        int[] bestByFace = new int[6];
        for (int cz = -4; cz <= 4; cz++)
        for (int cy = deepY - 3; cy <= deepY; cy++)
        for (int cx = -4; cx <= 4; cx++)
        {
            var candidate = new ChunkPos(cx, cy, cz);
            int[] byFace = new int[6];
            for (int face = 0; face < 6; face++)
            {
                byFace[face] = CaveAir(SupportRegion.Across(candidate, face));
            }

            // The roof is what falls, so the roof's caves count double in the choice.
            int caves = byFace.Sum() + byFace[2];
            if (caves > bestCaves)
            {
                bestCaves = caves;
                best = candidate;
                bestByFace = byFace;
            }
        }

        var chunks = new Dictionary<ChunkPos, VoxelChunk>();
        for (int cz = best.Z - 2; cz <= best.Z + 2; cz++)
        for (int cy = best.Y - 2; cy <= best.Y + 2; cy++)
        for (int cx = best.X - 2; cx <= best.X + 2; cx++)
        {
            var position = new ChunkPos(cx, cy, cz);
            chunks[position] = At(position);
        }

        int ownAir = CaveAir(best);
        chunks[best] = new VoxelChunk(MaterialIds.AirNumericId);
        VoxelPos origin = best.OriginVoxel;
        int undersideY = origin.Y + VoxelConstants.ChunkSize;
        return new Fixture
        {
            Name = "7.  a bite in the natural-cave shell",
            Chunks = chunks,
            Dug = [best],
            SliceZ = origin.Z + 16,
            SliceMinX = origin.X - 8,
            SliceMaxX = origin.X + VoxelConstants.ChunkSize + 7,
            SliceMinY = origin.Y - 3,
            SliceMaxY = undersideY + VoxelConstants.ChunkSize + 3,
            Note = $"bite {best}: cave voxels in its face neighbours +X {bestByFace[0]:N0} / -X {bestByFace[1]:N0} / roof {bestByFace[2]:N0} / "
                + $"floor {bestByFace[3]:N0} / +Z {bestByFace[4]:N0} / -Z {bestByFace[5]:N0}, {ownAir:N0} in the bitten chunk itself; "
                + "what is at risk here is from caves alone, since one bite in solid stone sheds nothing",
        };
    }

    private static string Slice(SupportRegion region, Fixture fixture, MaterialTable materials, int rubbleId = -1)
    {
        var text = new StringBuilder();
        for (int y = fixture.SliceMaxY; y >= fixture.SliceMinY; y--)
        {
            text.Append($"    {y,6} ");
            for (int x = fixture.SliceMinX; x <= fixture.SliceMaxX; x++)
            {
                var voxel = new VoxelPos(x, y, fixture.SliceZ);
                ChunkPos chunk = voxel.Chunk;
                if (!fixture.Chunks.TryGetValue(chunk, out VoxelChunk? voxels))
                {
                    text.Append('|');
                    continue;
                }

                (int lx, int ly, int lz) = voxel.Local;
                if (!materials.IsSolid(voxels.Get(lx, ly, lz)))
                {
                    text.Append(' ');
                    continue;
                }

                ChunkSupport? field = region.IsAnalysed(chunk) ? region.FieldAt(chunk) : null;
                if (field is null)
                {
                    text.Append(':');
                    continue;
                }

                byte value = field.Value(lx, ly, lz);
                bool rubble = rubbleId >= 0 && voxels.Get(lx, ly, lz) == rubbleId;
                text.Append(value == ChunkSupport.Unreached ? 'X' : ChunkSupport.IsAtRisk(value) ? '#' : rubble ? 'o' : value == ChunkSupport.Zero ? '-' : '.');
            }

            text.AppendLine();
        }

        return text.ToString();
    }

    private static int FloorDiv(int value, int divisor)
    {
        int quotient = value / divisor;
        return value % divisor != 0 && (value < 0) != (divisor < 0) ? quotient - 1 : quotient;
    }
}
