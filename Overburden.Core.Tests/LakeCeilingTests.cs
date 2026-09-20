using Overburden.Core.Fluid;
using Overburden.Core.Hydrology;
using Overburden.Core.Lighting;
using Overburden.Core.Voxels;
using Xunit;
using Xunit.Abstractions;

namespace Overburden.Core.Tests;

/// <summary>
/// M8d-5: a lake is a body Tier B cannot enter. The band of natural air over a lake's bed up to
/// its top is not holdable, water resting on the top is handed as water on the ground is, and
/// water found inside is handed whole. The fixture is the portal drain's bowl with the surface
/// hand-off's seam on it — the two halves of the seam in one twin, which the treadmill needs.
/// </summary>
/// <remarks>
/// <para>
/// <b>The treadmill's shape is the headline test, run in both arms.</b> A chamber dug flush with
/// the bowl's wall opens a bank portal on every row down to the bed; the lake pours in, the
/// chamber fills to the bed's plane, and without the ceiling its sheet spreads out over the bed —
/// which the control arm asserts, so that the guarded boundary is known to be reached — while
/// with it the chamber fills to the level and stops, nothing on the bed, nothing handed back.
/// </para>
/// <para>
/// Heights are the topmost solid voxel; the plane the water stands on is one above. The bowl's
/// floor is 30, so its bed is plane 31 and four voxels of depth stand at 35; the bowl is 32 × 32
/// voxels, 64 m², 16,000 L a voxel of depth.
/// </para>
/// </remarks>
public class LakeCeilingTests(ITestOutputHelper output)
{
    private const ulong Seed = 0x1A4EUL;
    private const int Cell = HeightField.DefaultCellVoxels;
    private const int Pitch = 128 / Cell;
    private const int Cap = FluidConstants.CellCapacityMillilitres;
    private const long LitresPerVoxelOfDepth = 64L * 250;

    private const int Floor = 30;
    private const int Rim = 40;

    private static ISurfaceHeights Bowl() => new AnalyticSurface((x, z) =>
        x >= 48 && x < 80 && z >= 48 && z < 80 ? Floor : Rim);

    /// <summary>Dug voxels and open air above the seed, less what was placed, inside the loaded box.</summary>
    private sealed class EditedTerrain(ISurfaceHeights seed) : IFluidTerrain, IVoxelWorld
    {
        public HashSet<VoxelPos> Dug { get; } = [];

        public HashSet<VoxelPos> Placed { get; } = [];

        public HashSet<ChunkPos> Loaded { get; } = [];

        public bool CanHold(VoxelPos v) =>
            Loaded.Contains(v.Chunk)
            && !Placed.Contains(v)
            && (Dug.Contains(v) || v.Y > seed.SurfaceHeightAt(v.X, v.Z));

        /// <summary>The same world to the enclosure's flood: unloaded is unknown, placed and undug rock is solid.</summary>
        public bool TryGetMaterial(VoxelPos v, out int materialId)
        {
            if (!Loaded.Contains(v.Chunk))
            {
                materialId = PhysicsTestHelpers.Air;
                return false;
            }

            materialId = !Placed.Contains(v) && (Dug.Contains(v) || v.Y > seed.SurfaceHeightAt(v.X, v.Z))
                ? PhysicsTestHelpers.Air
                : PhysicsTestHelpers.Stone;
            return true;
        }
    }

    private sealed class Twin
    {
        public Twin(bool withCeiling = true, bool withEnclosure = false)
        {
            Seed = Bowl();
            Terrain = new EditedTerrain(Seed);
            Water = new WaterSystem(Seed, LakeCeilingTests.Seed, new BasinLedger(), Pitch);
            Water.Install(Water.AnalyseFor(64, 64));
            Enclosure = withEnclosure ? new Enclosure(Terrain, PhysicsTestHelpers.Table, Seed) : null;
            Ceiling = withCeiling ? new LakeCeiling(Seed, Water, Enclosure) : null;
            IFluidTerrain terrain = Ceiling is null ? Terrain : new LakeBoundedTerrain(Terrain, Ceiling);
            Fluid = new FluidSimulation(terrain);
            Index = new PortalIndex(Seed, Terrain);
            Tracker = new PortalTracker(Index);
            Drain = new PortalDrain(Index, Water, Fluid);
            Handoff = new SurfaceHandoff(Seed, Water, Fluid, Ceiling, Enclosure);

            // Chunks X 0..2, Y 0..1, Z 1..2: the bowl, its walls, and room west of it for a chamber.
            for (int cz = 1; cz <= 2; cz++)
            {
                for (int cx = 0; cx <= 2; cx++)
                {
                    for (int cy = 0; cy <= 1; cy++)
                    {
                        var chunk = new ChunkPos(cx, cy, cz);
                        Terrain.Loaded.Add(chunk);
                        Reachable.Add(chunk);
                        Tracker.OnVoxelsReady(chunk, hasEdits: false);
                    }
                }
            }

            Refresh();
        }

        public ISurfaceHeights Seed { get; }

        public EditedTerrain Terrain { get; }

        public WaterSystem Water { get; }

        public LakeCeiling? Ceiling { get; }

        public Enclosure? Enclosure { get; }

        public FluidSimulation Fluid { get; }

        /// <summary>A placed voxel, told to the enclosure and the tracker as the pipeline's edit hook would.</summary>
        public void Place(VoxelPos v)
        {
            Terrain.Placed.Add(v);
            Enclosure?.VoxelChanged(v);
            Tracker.OnVoxelChanged(v);
        }

        public void Unplace(VoxelPos v)
        {
            Terrain.Placed.Remove(v);
            Enclosure?.VoxelChanged(v);
            Tracker.OnVoxelChanged(v);
            Fluid.Disturb(v);
        }

        public PortalIndex Index { get; }

        public PortalTracker Tracker { get; }

        public PortalDrain Drain { get; }

        public SurfaceHandoff Handoff { get; }

        public HashSet<ChunkPos> Reachable { get; } = [];

        public int Basin => Water.BasinAtSample(48 / Cell, 48 / Cell);

        public double Level => Water.LevelAt(Basin)!.Value;

        public int Top => (int)Math.Floor(Level);

        public void Dig(VoxelPos v)
        {
            Terrain.Dug.Add(v);
            Tracker.OnVoxelChanged(v);
        }

        public void DigBox(int x0, int y0, int z0, int x1, int y1, int z1)
        {
            for (int y = y0; y <= y1; y++)
            {
                for (int z = z0; z <= z1; z++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        Dig(new VoxelPos(x, y, z));
                    }
                }
            }
        }

        /// <summary>
        /// Placed blocks on a cell's four sides, so a full cell put there stays there: a full cell
        /// on a flat bed otherwise spreads for hundreds of ticks, and "asleep on the bed" needs
        /// the cell to be where the test says it is.
        /// </summary>
        public void Cup(VoxelPos cell)
        {
            Terrain.Placed.Add(cell.Offset(1, 0, 0));
            Terrain.Placed.Add(cell.Offset(-1, 0, 0));
            Terrain.Placed.Add(cell.Offset(0, 0, 1));
            Terrain.Placed.Add(cell.Offset(0, 0, -1));
        }

        /// <summary>Ticks with no boundary between them until the automaton has nothing to do.</summary>
        public void StepToRest()
        {
            int ticks = 0;
            while (Fluid.ActiveCellCount > 0 && ticks++ < 5_000)
            {
                Fluid.Step();
            }

            Assert.Equal(0, Fluid.ActiveCellCount);
        }

        /// <summary>The ceiling's refresh, with the wake, as the boundary runs it.</summary>
        public void Refresh() => Ceiling?.Refresh(Reachable, Fluid.WakeCell);

        /// <summary>
        /// One game boundary and the tick after it, in the game's order: the hand-off over what
        /// the last tick found, the draw, the ceiling's refresh, then the automaton.
        /// </summary>
        public (long Handed, long Drawn) Tick()
        {
            long handed = Handoff.Absorb();
            long drawn = Drain.Draw();
            Refresh();
            Fluid.Step();
            return (handed, drawn);
        }

        /// <summary>Boundaries and ticks until nothing moves, nothing is drawn and nothing is handed.</summary>
        public int RunToRest(int maxTicks = 5_000)
        {
            int ticks = 0;
            int idle = 0;
            while (idle < 3 && ticks < maxTicks)
            {
                (long handed, long drawn) = Tick();
                idle = handed == 0 && drawn == 0 && Fluid.ActiveCellCount == 0 ? idle + 1 : 0;
                ticks++;
            }

            Assert.True(ticks < maxTicks, $"still moving after {ticks} ticks");
            Assert.True(Fluid.VerifyQuiescent(), "the automaton stopped with moves available");
            return ticks;
        }

        /// <summary>Millilitres Tier B holds in the bowl's natural air — the bed and above it.</summary>
        public long OnTheBed()
        {
            long held = 0;
            for (int z = 48; z < 80; z++)
            {
                for (int x = 48; x < 80; x++)
                {
                    for (int y = Floor + 1; y <= Rim + 1; y++)
                    {
                        held += Fluid.Grid.AmountAt(new VoxelPos(x, y, z));
                    }
                }
            }

            return held;
        }

        /// <summary>Both tiers' identities, the seam's two terms, and the whole world's, exactly.</summary>
        public void AssertIdentity()
        {
            long a = Water.StoredLitres;
            Assert.Equal(
                Water.ReceivedLitres + Water.InjectedLitres - Water.SpilledLitres - Water.DrawnLitres, a);

            long b = Fluid.TotalMillilitres;
            Assert.Equal(
                Fluid.AddedMillilitres - Fluid.RemovedMillilitres - Fluid.DisplacedMillilitres
                    - Fluid.HandedMillilitres,
                b);

            Assert.Equal(Fluid.HandedMillilitres, Handoff.HandedMillilitres);
            Assert.Equal(Water.ReceivedLitres, Handoff.ReceivedLitres);
            Assert.Equal(Water.DrawnLitres * FluidConstants.MillilitresPerLitre, Drain.DrawnMillilitres);
            Assert.Equal(0L, Fluid.DisplacedMillilitres);

            long relay = Fluid.AddedMillilitres - Drain.DrawnMillilitres;
            Assert.Equal(
                ((Water.InjectedLitres - Water.SpilledLitres) * FluidConstants.MillilitresPerLitre)
                    + relay - Fluid.RemovedMillilitres,
                (a * FluidConstants.MillilitresPerLitre) + Handoff.PendingMillilitres + b);
        }
    }

    [Fact]
    public void TheBandIsNaturalAirOverTheBedUpToTheLakesTopAndNothingElse()
    {
        var twin = new Twin();
        Assert.False(twin.Ceiling!.InLake(new VoxelPos(60, Floor + 1, 60)), "a dry bowl has no band");

        // Four voxels of depth and a bit over the plane at 31: the level is 35.125, the top cell
        // 35, an eighth full, and the sixth cell up is above the lake.
        twin.Water.Inject(twin.Basin, (4 * LitresPerVoxelOfDepth) + 2_000);
        twin.Refresh();
        Assert.Equal(Floor + 5, twin.Top);

        IFluidTerrain terrain = new LakeBoundedTerrain(twin.Terrain, twin.Ceiling);
        for (int y = Floor + 1; y <= Floor + 5; y++)
        {
            var cell = new VoxelPos(60, y, 60);
            Assert.True(twin.Ceiling.InLake(cell), $"Y {y} is under the top and not in the band");
            Assert.True(terrain.InLake(cell));
            Assert.False(terrain.CanHold(cell), $"Y {y} is in the lake and holdable");
        }

        Assert.False(twin.Ceiling.InLake(new VoxelPos(60, Floor + 6, 60)), "the cell above the top is in the band");
        Assert.True(terrain.CanHold(new VoxelPos(60, Floor + 6, 60)));
        Assert.False(twin.Ceiling.InLake(new VoxelPos(60, Floor, 60)), "the bed's rock is in the band");
        Assert.False(terrain.InLake(new VoxelPos(60, Floor, 60)));

        // A column on the rim is above the level everywhere; a cell dug into the bank below the
        // seed is where the lake pours into, never the lake.
        Assert.False(twin.Ceiling.InLake(new VoxelPos(40, Floor + 2, 60)));
        Assert.False(twin.Ceiling.InLake(new VoxelPos(40, Rim + 1, 60)));
        var dug = new VoxelPos(47, Floor + 2, 60);
        twin.Dig(dug);
        Assert.True(terrain.CanHold(dug));
        Assert.False(terrain.InLake(dug));

        // The plane: the top over the lake, the seed where there is none.
        Assert.Equal(Floor + 5, twin.Ceiling.PlaneAt(60, 60));
        Assert.Equal(Rim, twin.Ceiling.PlaneAt(40, 60));

        // Drawn to nothing, the band is gone.
        twin.Water.Draw(twin.Basin, twin.Water.StoredLitres);
        twin.Refresh();
        Assert.False(twin.Ceiling.InLake(new VoxelPos(60, Floor + 1, 60)));
        Assert.Equal(0, twin.Ceiling.ChunkColumns);
    }

    [Fact]
    public void ACellUnderItsOwnLakeIsHandedWholeAndNothingStaysOnTheBed()
    {
        // M8d-3's bank test, inverted: the same cell on the same bed under the same metre of lake
        // is now the lake's, handed at the first boundary that sees it.
        // The water goes down first, on a dry bed where a visit finds it at rest; then the lake,
        // over it. (Add refuses a lake cell, which is the rule seen from the relay's side.)
        var twin = new Twin();
        var bed = new VoxelPos(60, Floor + 1, 60);
        twin.Cup(bed);
        twin.Fluid.Add(bed, Cap);
        twin.StepToRest();

        twin.Water.Inject(twin.Basin, 4 * LitresPerVoxelOfDepth);
        Assert.True(twin.Level > Floor + 2);
        long before = twin.Water.StoredLitres;

        twin.Refresh();
        Assert.Equal(Cap, twin.Fluid.Add(bed, Cap));
        twin.Fluid.Step();
        twin.Handoff.Absorb();
        twin.AssertIdentity();
        twin.Tick();
        twin.AssertIdentity();

        Assert.Equal(0L, twin.Fluid.TotalMillilitres);
        Assert.Equal(Cap, twin.Handoff.HandedMillilitres);
        Assert.Equal(1L, twin.Handoff.SubmergedHanded);
        Assert.Equal(before + (Cap / FluidConstants.MillilitresPerLitre), twin.Water.StoredLitres);
        Assert.Equal(0, twin.Fluid.ActiveCellCount);
    }

    [Fact]
    public void APourOntoALakeRestsOnItsTopAndIsHandedAndTheLevelRisesByIt()
    {
        // A bit over four voxels, so the pour's own litres cannot carry the top past an integer
        // and turn a cell resting on the lake into one inside it — handed either way, but this
        // test is about the plane.
        var twin = new Twin();
        twin.Water.Inject(twin.Basin, (4 * LitresPerVoxelOfDepth) + 2_000);
        twin.Refresh();
        int top = twin.Top;
        long before = twin.Water.StoredLitres;

        // Sixty-four full cells dropped from three above the top, a 4 × 4 block two deep.
        const int cells = 32;
        for (int i = 0; i < cells; i++)
        {
            twin.Fluid.Add(new VoxelPos(56 + (i % 4), top + 3 + (i / 16), 56 + ((i / 4) % 4)), Cap);
        }

        int ticks = twin.RunToRest();
        twin.AssertIdentity();

        Assert.Equal(0L, twin.Fluid.TotalMillilitres);
        Assert.Equal((long)cells * Cap, twin.Handoff.HandedMillilitres);
        Assert.Equal(0L, twin.Handoff.SubmergedHanded);
        Assert.Equal(before + ((long)cells * Cap / FluidConstants.MillilitresPerLitre), twin.Water.StoredLitres);
        Assert.Equal(0L, twin.OnTheBed());
        output.WriteLine($"{cells} cells poured onto the lake rested and were handed in {ticks} ticks");
    }

    [Fact]
    public void AWalledCourtyardBelowTheLakesLevelIsNotTheLakesAndKeepsItsOwnWater()
    {
        // The courtyard (Matthew, 2026-09-20): four walls on the bowl's bed, taller than the
        // lake's top, around a 3 × 3 patch the nine cells fill exactly. Without the enclosure the patch's air is inside the
        // lake — no cell there can hold, a pour is found submerged and handed whole. With it the
        // column has no band: the pour lands, rests on the bed, is kept by the hand-off, and the
        // lake's litres are untouched. Then the wall comes down and the lake takes it.
        var twin = new Twin(withEnclosure: true);
        twin.Water.Inject(twin.Basin, 4 * LitresPerVoxelOfDepth);
        twin.Refresh();
        int top = twin.Top;

        for (int d = -2; d <= 2; d++)
        {
            for (int y = Floor + 1; y <= top + 2; y++)
            {
                twin.Place(new VoxelPos(60 + d, y, 58));
                twin.Place(new VoxelPos(60 + d, y, 62));
                twin.Place(new VoxelPos(58, y, 60 + d));
                twin.Place(new VoxelPos(62, y, 60 + d));
            }
        }

        twin.Refresh();
        Assert.False(twin.Ceiling!.InLake(new VoxelPos(60, Floor + 1, 60)));
        Assert.True(twin.Ceiling.InLake(new VoxelPos(50, Floor + 1, 50)));
        Assert.True(twin.Ceiling.RebuiltForEnclosure > 0, "the wall's edit did not rebuild the band");

        long lake = twin.Water.StoredLitres;
        long poured = 0;
        for (int x = 59; x <= 61; x++)
        {
            for (int z = 59; z <= 61; z++)
            {
                poured += Cap - twin.Fluid.Add(new VoxelPos(x, Floor + 1, z), Cap);
            }
        }

        Assert.Equal(9L * Cap, poured);
        twin.RunToRest();

        Assert.Equal(poured, twin.Fluid.TotalMillilitres);
        Assert.Equal(0L, twin.Handoff.HandedMillilitres);
        Assert.Equal(lake, twin.Water.StoredLitres);
        Assert.Equal(9L, twin.Handoff.EnclosedKept);
        twin.AssertIdentity();

        // The west wall taken down: the region is dropped, the band comes back over the
        // courtyard, its cells are woken as submerged, and the lake takes the pour whole.
        for (int y = Floor + 1; y <= top + 2; y++)
        {
            twin.Unplace(new VoxelPos(58, y, 60));
        }

        twin.RunToRest();
        Assert.True(twin.Ceiling.InLake(new VoxelPos(60, Floor + 1, 60)));
        Assert.Equal(0L, twin.Fluid.TotalMillilitres);
        Assert.Equal(poured, twin.Handoff.HandedMillilitres);
        Assert.Equal(lake + (poured / FluidConstants.MillilitresPerLitre), twin.Water.StoredLitres);
        twin.AssertIdentity();
    }

    [Fact]
    public void WithoutTheEnclosureTheSameCourtyardIsTheLakesAndItsPourIsHandedWhole()
    {
        // The guarded boundary is reached: the control arm, the world as it was on 2026-09-19.
        var twin = new Twin(withEnclosure: false);
        twin.Water.Inject(twin.Basin, 4 * LitresPerVoxelOfDepth);
        twin.Refresh();
        int top = twin.Top;
        for (int d = -2; d <= 2; d++)
        {
            for (int y = Floor + 1; y <= top + 2; y++)
            {
                twin.Place(new VoxelPos(60 + d, y, 58));
                twin.Place(new VoxelPos(60 + d, y, 62));
                twin.Place(new VoxelPos(58, y, 60 + d));
                twin.Place(new VoxelPos(62, y, 60 + d));
            }
        }

        twin.Refresh();
        Assert.True(twin.Ceiling!.InLake(new VoxelPos(60, Floor + 1, 60)));

        long before = twin.Water.StoredLitres;
        long poured = 0;
        for (int x = 59; x <= 61; x++)
        {
            for (int z = 59; z <= 61; z++)
            {
                poured += Cap - twin.Fluid.Add(new VoxelPos(x, top + 1, z), Cap);
            }
        }

        twin.RunToRest();
        Assert.Equal(0L, twin.Fluid.TotalMillilitres);
        Assert.Equal(poured, twin.Handoff.HandedMillilitres);
        Assert.Equal(before + (poured / FluidConstants.MillilitresPerLitre), twin.Water.StoredLitres);
        twin.AssertIdentity();
    }

    [Fact]
    public void ARisingLevelWakesACellItSubmergesAndTheLakeTakesIt()
    {
        // A cell resting on a placed block two above the bed is not on the plane and is never
        // handed; the lake then rises past it, the refresh wakes it, the next visit lists it
        // submerged and the boundary after that hands it.
        var twin = new Twin();
        var block = new VoxelPos(60, Floor + 1, 60);
        var cell = new VoxelPos(60, Floor + 2, 60);
        twin.Terrain.Placed.Add(block);
        twin.Cup(cell);
        twin.Fluid.Add(cell, Cap);
        twin.RunToRest();
        Assert.Equal(Cap, twin.Fluid.TotalMillilitres);
        Assert.Equal(0L, twin.Handoff.HandedMillilitres);

        // An eighth of a voxel: the top is the bed's plane, 31, and the cell at 32 is above the lake.
        twin.Water.Inject(twin.Basin, 2_000);
        twin.Refresh();
        Assert.Equal(cell.Y - 1, twin.Top);
        long wokenFirst = twin.Ceiling!.WokenSubmerged;
        Assert.Equal(32L * 32, wokenFirst);
        twin.Fluid.Step();
        Assert.Empty(twin.Fluid.Submerged);

        // One voxel more: the top is exactly the cell's Y. The band grew by one cell over each
        // of the bowl's 1,024 columns — (old, new], so exactly that many woken, and the cell at
        // the new top is among them.
        twin.Water.Inject(twin.Basin, LitresPerVoxelOfDepth);
        Assert.True(twin.Level > cell.Y && twin.Level < cell.Y + 1, $"level {twin.Level}");
        long before = twin.Water.StoredLitres;

        // The refresh is the wake; the tick after visits; the boundary after hands.
        twin.Refresh();
        Assert.Equal(cell.Y, twin.Top);
        Assert.Equal(wokenFirst + (32L * 32), twin.Ceiling.WokenSubmerged);
        twin.Fluid.Step();
        Assert.Equal(1L, twin.Fluid.SubmergedVisits);
        Assert.Single(twin.Fluid.Submerged);
        twin.Handoff.Absorb();

        Assert.Equal(0L, twin.Fluid.TotalMillilitres);
        Assert.Equal(1L, twin.Handoff.SubmergedHanded);
        Assert.Equal(before + (Cap / FluidConstants.MillilitresPerLitre), twin.Water.StoredLitres);
        twin.AssertIdentity();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AChamberFlushWithTheBankFillsToTheLevelAndNothingSpreadsOntoTheBed(bool withCeiling)
    {
        // The treadmill's shape: a chamber dug flush with the bowl's west wall, a bank portal on
        // every row from the bed to the level. The lake pours in through all of them. Without the
        // ceiling the chamber's sheet spreads out onto the bed — Tier B water under Tier A's
        // plane, the two surfaces of 2026-09-14 — and the control arm asserts exactly that, so the
        // boundary the rule guards is known to be reached. With it, the bed cannot be entered,
        // the chamber fills to the level, the draw stops and nothing is handed back.
        var twin = new Twin(withCeiling);
        twin.Water.Inject(twin.Basin, (5 * LitresPerVoxelOfDepth) + 2_000);
        twin.Refresh();
        int top = twin.Top;
        Assert.Equal(Floor + 6, top);

        // x 32..47, y 31..40, z 56..63: 16 × 10 × 8 cells, the east face on the wall.
        twin.DigBox(32, Floor + 1, 56, 47, Rim, 63);
        Assert.True(twin.Index.Count >= 8 * 5, $"only {twin.Index.Count} portals");

        if (!withCeiling)
        {
            // The treadmill, running: the chamber's sheet spreads onto the bed, the hand-off
            // returns it, the drain draws it again. It never rests, so a fixed run, and the
            // assertion is that drawn and handed climb together — the 2026-09-09 log's shape.
            for (int i = 0; i < 400; i++)
            {
                twin.Tick();
            }

            long drawnAt400 = twin.Water.DrawnLitres;
            long handedAt400 = twin.Handoff.HandedMillilitres;
            for (int i = 0; i < 200; i++)
            {
                twin.Tick();
            }

            twin.AssertIdentity();
            output.WriteLine(
                $"no ceiling, 600 ticks: drawn {twin.Water.DrawnLitres:N0} L, handed "
                + $"{twin.Handoff.HandedMillilitres / 1000:N0} L, on the bed {twin.OnTheBed() / 1000:N0} L, "
                + $"active {twin.Fluid.ActiveCellCount:N0}");
            Assert.True(twin.OnTheBed() > 0, "the control arm never reached the bed, so the rule was not tested");
            Assert.True(twin.Water.DrawnLitres > drawnAt400, "the drain stopped without the ceiling");
            Assert.True(twin.Handoff.HandedMillilitres > handedAt400, "the hand-off stopped without the ceiling");
            return;
        }

        int ticks = twin.RunToRest();
        twin.AssertIdentity();

        long onBed = twin.OnTheBed();
        long inChamber = twin.Fluid.TotalMillilitres - onBed;
        output.WriteLine(
            $"ceiling: rest after {ticks} ticks, drawn {twin.Water.DrawnLitres:N0} L, "
            + $"handed {twin.Handoff.HandedMillilitres / 1000:N0} L, level {twin.Level:F3}, "
            + $"chamber {inChamber / 1000:N0} L, on the bed {onBed / 1000:N0} L");

        Assert.Equal(0L, onBed);
        Assert.Equal(0L, twin.Handoff.HandedMillilitres);
        Assert.Equal(0L, twin.Handoff.SubmergedHanded);

        // The chamber stands within a cell of the lake: the lake's level is at or under the
        // lowest mouth that still has room, and the chamber's sheet is level with it.
        double level = twin.Level;
        int chamberTop = -1;
        for (int y = Floor + 1; y <= Rim; y++)
        {
            if (twin.Fluid.Grid.AmountAt(new VoxelPos(40, y, 60)) > 0)
            {
                chamberTop = y;
            }
        }

        Assert.InRange(chamberTop, (int)Math.Floor(level) - 1, (int)Math.Floor(level) + 1);
        Assert.True(twin.Water.DrawnLitres > 0);
    }

    [Fact]
    public void CoverBuildsTheBandUnderAPourBeforeItLandsWhereNoWaterWasNear()
    {
        // The review's finding: a band exists only for reachable columns, so a pour onto a lake
        // with no Tier B nearby was accepted into the lake's volume. Covered first, the lake
        // cell refuses and the pour lands on the top.
        var twin = new Twin();
        twin.Reachable.Clear();
        twin.Refresh();
        twin.Water.Inject(twin.Basin, (4 * LitresPerVoxelOfDepth) + 2_000);
        var inLake = new VoxelPos(60, Floor + 3, 60);
        Assert.Equal(0, twin.Ceiling!.ChunkColumns);

        // The control: uncovered, the lake cell takes the water.
        var control = new Twin();
        control.Reachable.Clear();
        control.Refresh();
        control.Water.Inject(control.Basin, (4 * LitresPerVoxelOfDepth) + 2_000);
        Assert.Equal(0L, control.Fluid.Add(inLake, Cap));

        twin.Ceiling.Cover(inLake.Offset(-4, -4, -4), inLake.Offset(4, 4, 4), twin.Fluid.WakeCell);
        Assert.True(twin.Ceiling.ChunkColumns > 0);
        Assert.Equal(Cap, twin.Fluid.Add(inLake, Cap));
        Assert.Equal(0L, twin.Fluid.Add(new VoxelPos(60, twin.Top + 1, 60), Cap));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ABiteInTheBedFillsToTheSeedAndItsWaterDrawsNoTopUnderTheLake(bool withCeiling)
    {
        // The chunk-out-of-the-lake screenshot (2026-09-14): a pit dug in the bed is below the
        // seed, so Tier B fills it to the seed and the cells from there to the level are the
        // lake's. The mesher must draw no top on the pit's water — the water goes on upward as
        // Tier A's — and the control arm shows the tops it would otherwise draw.
        var twin = new Twin(withCeiling);
        twin.Water.Inject(twin.Basin, (4 * LitresPerVoxelOfDepth) + 2_000);
        twin.Refresh();

        // A 4 × 4 pit, four deep, in the middle of the bed.
        twin.DigBox(60, Floor - 3, 60, 63, Floor, 63);
        int ticks = twin.RunToRest();
        Assert.True(twin.Water.DrawnLitres > 0, "nothing drained into the pit");

        // Full to the seed and no higher: the pit's top cells hold water, the bed's plane above
        // them is the lake's and holds none in Tier B.
        Assert.True(twin.Fluid.Grid.AmountAt(new VoxelPos(61, Floor, 61)) > 0, "the pit did not fill to the seed");
        if (withCeiling)
        {
            Assert.Equal(0, twin.Fluid.Grid.AmountAt(new VoxelPos(61, Floor + 1, 61)));
        }

        var mesher = new FluidSurfaceMesher();
        IFluidTerrain terrain = withCeiling ? new LakeBoundedTerrain(twin.Terrain, twin.Ceiling!) : twin.Terrain;
        FluidSurfaceMesh mesh = mesher.Build(twin.Fluid.Grid, terrain);
        output.WriteLine($"ceiling {withCeiling}: rest after {ticks} ticks, drawn {twin.Water.DrawnLitres:N0} L, tops {mesh.Tops}, under a lake {mesh.UnderLake}, surface cells {mesh.SurfaceCells}");
        if (withCeiling)
        {
            Assert.Equal(0, mesh.Tops);
            Assert.Equal(0, mesh.SurfaceCells);
            Assert.Equal(16, mesh.UnderLake);
        }
        else
        {
            Assert.True(mesh.Tops > 0, "the control arm drew no top, so the rule was not tested");
            Assert.Equal(0, mesh.UnderLake);
        }
    }

    [Fact]
    public void TheRefreshRebuildsOnlyWhereASampleTopMovedAndDropsColumnsThatLeft()
    {
        var twin = new Twin();
        LakeCeiling ceiling = twin.Ceiling!;
        Assert.Equal(0, ceiling.ChunkColumns);

        twin.Water.Inject(twin.Basin, 2 * LitresPerVoxelOfDepth);
        twin.Refresh();
        int columns = ceiling.ChunkColumns;
        long rebuilt = ceiling.Rebuilt;
        Assert.True(columns > 0);
        Assert.Equal(columns, rebuilt);

        // Nothing moved: nothing rebuilt.
        twin.Refresh();
        Assert.Equal(rebuilt, ceiling.Rebuilt);
        Assert.Equal(0, ceiling.RebuiltLastPass);

        // A hair more water keeps the top: still nothing.
        twin.Water.Inject(twin.Basin, 1);
        twin.Refresh();
        Assert.Equal(rebuilt, ceiling.Rebuilt);

        // A voxel more moves the top of every column over the lake.
        twin.Water.Inject(twin.Basin, LitresPerVoxelOfDepth);
        twin.Refresh();
        Assert.Equal(rebuilt + columns, ceiling.Rebuilt);

        // A column that leaves the reachable set loses its band.
        twin.Reachable.Clear();
        twin.Refresh();
        Assert.Equal(0, ceiling.ChunkColumns);
    }

    [Fact]
    public void ACapturedViewKeepsTheLakeItWasCapturedWithWhileTheLiveCeilingMoves()
    {
        var twin = new Twin();
        twin.Water.Inject(twin.Basin, 2 * LitresPerVoxelOfDepth);
        twin.Refresh();
        LakeCeilingView view = twin.Ceiling!.Capture();
        var cell = new VoxelPos(60, Floor + 4, 60);
        Assert.False(view.InLake(cell));
        Assert.False(twin.Ceiling.InLake(cell));

        twin.Water.Inject(twin.Basin, 2 * LitresPerVoxelOfDepth);
        twin.Refresh();
        Assert.True(twin.Ceiling.InLake(cell));
        Assert.False(view.InLake(cell), "the view followed the live ceiling");

        // And on a column the view has not read yet, so its own per-column cache cannot stand in
        // for the copy: a view over the live dictionary answers the new lake here.
        var elsewhere = new VoxelPos(70, Floor + 4, 70);
        Assert.True(twin.Ceiling.InLake(elsewhere));
        Assert.False(view.InLake(elsewhere), "the view shares the live ceiling's columns");

        IFluidTerrain frozen = new LakeBoundedTerrain(twin.Terrain, view);
        IFluidTerrain live = new LakeBoundedTerrain(twin.Terrain, twin.Ceiling);
        Assert.True(frozen.CanHold(cell));
        Assert.False(live.CanHold(cell));
    }

    [Fact]
    public void WakeBandWakesEveryHeldCellOfAChunksBandAndNothingOutsideIt()
    {
        // The arrival path: a panel whose water landed after its column was covered. Two cells in
        // the band asleep, one above it asleep; the band's wake lists the two and leaves the third.
        // Put down asleep before the lake exists: one on the bed, one on a placed block three
        // up, one on a placed block above where the top will be. Then the lake, with a refresh
        // that wakes nothing, so the wake under test is WakeBand's alone.
        var twin = new Twin();
        const int top = Floor + 5;
        var inA = new VoxelPos(60, Floor + 1, 60);
        var inB = new VoxelPos(70, top, 70);
        var above = new VoxelPos(65, top + 2, 65);
        twin.Terrain.Placed.Add(new VoxelPos(70, top - 1, 70));
        twin.Terrain.Placed.Add(new VoxelPos(65, top + 1, 65));
        twin.Cup(inA);
        twin.Cup(inB);
        twin.Cup(above);
        twin.Fluid.Add(inA, Cap);
        twin.Fluid.Add(inB, Cap);
        twin.Fluid.Add(above, Cap);
        twin.StepToRest();

        twin.Water.Inject(twin.Basin, (4 * LitresPerVoxelOfDepth) + 2_000);
        twin.Ceiling!.Refresh(twin.Reachable, null);
        Assert.Equal(top, twin.Top);

        long disturbedBefore = twin.Fluid.DisturbedCells;
        var woken = new List<VoxelPos>();
        foreach (ChunkPos chunk in twin.Reachable)
        {
            twin.Ceiling.WakeBand(chunk, woken.Add);
        }
        Assert.Contains(inA, woken);
        Assert.Contains(inB, woken);
        Assert.DoesNotContain(above, woken);
        Assert.All(woken, w => Assert.True(twin.Ceiling.InLake(w)));

        foreach (VoxelPos w in woken)
        {
            twin.Fluid.WakeCell(w);
        }

        twin.Fluid.Step();
        Assert.Equal(2, twin.Fluid.Submerged.Count);
        Assert.True(twin.Fluid.DisturbedCells > disturbedBefore);

        // Submerged and asleep is at rest, by the check's own reading: the boundary takes it.
        Assert.Equal(0, twin.Fluid.ActiveCellCount);
        Assert.True(twin.Fluid.VerifyQuiescent(), "a submerged cell reads as held water in rock");
    }
}
