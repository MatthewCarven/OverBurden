using Overburden.Core.Fluid;
using Overburden.Core.Hydrology;
using Overburden.Core.Lighting;
using Overburden.Core.Voxels;
using Xunit.Abstractions;

namespace Overburden.Core.Tests;

/// <summary>
/// M8b-2c's Core twin: Tier A over an analytic bowl, Tier B over a dug terrain, the portal index
/// between them, and the two-tier identity held exactly across every tick.
/// </summary>
/// <remarks>
/// <para>
/// The headline is <c>APitUnderAFullBasinFillsAndTheBasinDropsByExactlyWhatArrived</c>. The
/// identity it holds is the sum of the two tiers' own: Tier A's <c>held == injected − spilled −
/// drawn</c> (no rain here) and Tier B's <c>held == added − removed − displaced</c>, joined by
/// <c>drawn × 1,000 == the part of added that came through portals</c>. Each side reports the seam
/// from its own end, so a litre lost between them cannot hide in either.
/// </para>
/// <para>
/// Heights are the topmost solid voxel, as <c>ISurfaceHeights</c> reports them; the field samples
/// the plane above, so a bowl floor written as 30 is a lake bed at plane 31.
/// </para>
/// </remarks>
public class PortalDrainTests(ITestOutputHelper output)
{
    private const ulong Seed = 0xB0A7UL;
    private const int Pitch = 8;

    /// <summary>The bowl's floor voxel, and its rim. Planes 31 and 41.</summary>
    private const int Floor = 30;
    private const int Rim = 40;

    /// <summary>A square bowl over samples 3..4 on each axis (voxels 48..79), inside both windows the tests use.</summary>
    private static ISurfaceHeights Bowl() => new AnalyticSurface((x, z) =>
        x >= 48 && x < 80 && z >= 48 && z < 80 ? Floor : Rim);

    /// <summary>Holdability for Tier B: dug voxels, and open air above the seed surface, inside a loaded box.</summary>
    private sealed class DugTerrain(ISurfaceHeights seed) : IFluidTerrain
    {
        public HashSet<VoxelPos> Dug { get; } = [];

        public HashSet<ChunkPos> Loaded { get; } = [];

        public bool CanHold(VoxelPos v) =>
            Loaded.Contains(v.Chunk) && (Dug.Contains(v) || v.Y > seed.SurfaceHeightAt(v.X, v.Z));
    }

    private sealed class Twin
    {
        public Twin(int windowX = 64, int windowZ = 64)
        {
            Seed = Bowl();
            Terrain = new DugTerrain(Seed);
            Water = new WaterSystem(Seed, PortalDrainTests.Seed, new BasinLedger(), Pitch);
            Water.Install(Water.AnalyseFor(windowX, windowZ));
            Fluid = new FluidSimulation(Terrain);
            Index = new PortalIndex(Seed, Terrain);
            Tracker = new PortalTracker(Index);
            Drain = new PortalDrain(Index, Water, Fluid);

            // Chunk Y 0 and 1 (voxels 0..63) over the bowl and its walls.
            for (int cz = 1; cz <= 2; cz++)
            {
                for (int cx = 1; cx <= 2; cx++)
                {
                    for (int cy = 0; cy <= 1; cy++)
                    {
                        var chunk = new ChunkPos(cx, cy, cz);
                        Terrain.Loaded.Add(chunk);
                        Tracker.OnVoxelsReady(chunk, hasEdits: false);
                    }
                }
            }
        }

        public ISurfaceHeights Seed { get; }

        public DugTerrain Terrain { get; }

        public WaterSystem Water { get; }

        public FluidSimulation Fluid { get; }

        public PortalIndex Index { get; }

        public PortalTracker Tracker { get; }

        public PortalDrain Drain { get; }

        public int Basin => Water.BasinAtSample(3, 3);

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

        /// <summary>One game tick: the draw at the boundary, then the automaton.</summary>
        public long Tick()
        {
            long drawn = Drain.Draw();
            Fluid.Step();
            return drawn;
        }

        /// <summary>Both tiers' identities and the seam between them, exactly.</summary>
        public void AssertIdentity()
        {
            long a = Water.StoredLitres;
            Assert.Equal(Water.InjectedLitres - Water.SpilledLitres - Water.DrawnLitres, a);

            long b = Fluid.TotalMillilitres;
            Assert.Equal(
                Fluid.AddedMillilitres - Fluid.RemovedMillilitres - Fluid.DisplacedMillilitres, b);

            Assert.Equal(Water.DrawnLitres * FluidConstants.MillilitresPerLitre, Drain.DrawnMillilitres);

            long relay = Fluid.AddedMillilitres - Drain.DrawnMillilitres;
            Assert.Equal(
                ((Water.InjectedLitres - Water.SpilledLitres) * FluidConstants.MillilitresPerLitre)
                + relay - Fluid.RemovedMillilitres - Fluid.DisplacedMillilitres,
                (a * FluidConstants.MillilitresPerLitre) + b);
        }
    }

    [Fact]
    public void TheBowlIsABasinAndItsSampleMapsToIt()
    {
        var twin = new Twin();
        Assert.NotEqual(Watershed.Outlet, twin.Basin);
        Assert.Equal(twin.Basin, twin.Water.BasinAtSample(4, 4));
        Assert.Equal(Watershed.Outlet, twin.Water.BasinAtSample(-1, 3));
        Assert.Equal(Watershed.Outlet, twin.Water.BasinAtSample(3, Pitch));
        Assert.Equal((3, 4), twin.Water.SampleOf(63, 64));
        Assert.Equal((-1, 0), twin.Water.SampleOf(-1, 0));

        // A window whose origin is two samples over: the same world sample is a different array
        // index, and the lookup has to subtract the origin. The whole plateau drains into the
        // bowl, so a lookup that forgot the origin would still land on the bowl's label for the
        // bowl's own samples — the cases that tell are the ones that cross the window's edge.
        twin.Water.Install(twin.Water.AnalyseFor(112, 112));
        Watershed moved = twin.Water.Window!.Watershed;
        int originSample = moved.Field.OriginX / HeightField.DefaultCellVoxels;
        Assert.Equal(2, originSample);
        Assert.NotEqual(Watershed.Outlet, twin.Water.BasinAtSample(3, 3));

        // Every world sample in the moved window against the window's own label — true by
        // construction, and different from an origin-less lookup wherever two samples two apart
        // carry different labels, which the edge columns guarantee.
        int differing = 0;
        for (int sz = 0; sz < Pitch; sz++)
        {
            for (int sx = 0; sx < Pitch; sx++)
            {
                int expected = moved.LabelAt(sx, sz);
                Assert.Equal(expected, twin.Water.BasinAtSample(sx + originSample, sz + originSample));
                if (sx + originSample < Pitch && sz + originSample < Pitch
                    && moved.LabelAt(sx + originSample, sz + originSample) != expected)
                {
                    differing++;
                }
            }
        }

        Assert.True(differing > 0, "no sample's label differs from the one two samples over, so an origin-less lookup would pass");

        // And outside the moved window on either side.
        Assert.Equal(Watershed.Outlet, twin.Water.BasinAtSample(1, 1));
        Assert.Equal(Watershed.Outlet, twin.Water.BasinAtSample(originSample + Pitch, 3));
    }

    [Fact]
    public void ALakeExactlyAtTheFaceDrawsNothingAndOneLitreMoreDraws()
    {
        // The covered test is strict: a lake whose surface stands exactly on the face's plane has
        // no water against the face. Constructed directly rather than reached by draining,
        // because the draining tests' draws are not always whole cells and may never land on the
        // plane — which is how an off-by-one in the comparison hid from them.
        const int face = 35;
        long litresAtFace = (face - (Floor + 1)) * 4L * 4000;

        var twin = new Twin();
        twin.Water.Inject(twin.Basin, litresAtFace);
        Assert.Equal(face, twin.Water.LevelAt(twin.Basin));

        var inside = new VoxelPos(47, face, 60);
        twin.DigBox(30, face - 9, 58, 46, face, 62);
        twin.Dig(inside);
        Assert.Equal(1, twin.Index.Count);

        Assert.Equal(0, twin.Drain.Draw());
        Assert.Equal(1, twin.Drain.WetSamplesLastPass);
        Assert.Equal(0, twin.Drain.CoveredPortalsLastPass);

        twin.Water.Inject(twin.Basin, 1);
        Assert.True(twin.Water.LevelAt(twin.Basin) > face);
        Assert.True(twin.Drain.Draw() > 0);
        Assert.Equal(1, twin.Drain.CoveredPortalsLastPass);
        twin.AssertIdentity();
    }

    [Fact]
    public void AnOverfilledBasinSpillsAndTheSpillIsATermOfTheIdentity()
    {
        // The Tier A relay creates litres from nothing and the bowl cannot hold them all; what
        // spills has to be counted or Tier A's own identity is off by exactly the overflow.
        var twin = new Twin();
        long capacity = twin.Water.Window!.Watershed.Basins[twin.Basin].CapacityLitres;
        Assert.Equal(160_000, capacity);

        long spilled = twin.Water.Inject(twin.Basin, capacity + 40_000);
        Assert.Equal(40_000, spilled);
        Assert.Equal(40_000, twin.Water.SpilledLitres);
        Assert.Equal(capacity, twin.Water.StoredLitres);
        twin.AssertIdentity();

        twin.DigBox(60, Floor - 9, 60, 60, Floor, 60);
        twin.Tick();
        twin.AssertIdentity();
    }

    [Fact]
    public void APitUnderAFullBasinFillsAndTheBasinDropsByExactlyWhatArrived()
    {
        // THE ORACLE. A shaft under the lake bed, twenty cells deep: the mouth draws fifteen litres
        // a tick, the automaton carries them down, and when the shaft is full the draw stops with
        // the mouth cell under a litre short of full. The lake has lost exactly what the shaft
        // holds, and both identities hold after every tick.
        var twin = new Twin();
        long spilled = twin.Water.Inject(twin.Basin, 100_000);
        Assert.Equal(0, spilled);
        double levelBefore = twin.Water.LevelAt(twin.Basin)!.Value;
        Assert.True(levelBefore > Floor + 1);

        var mouth = new VoxelPos(60, Floor, 60);
        twin.DigBox(60, Floor - 19, 60, 60, Floor, 60);
        Assert.Equal(1, twin.Index.Count);
        Assert.True(twin.Index.Contains(new Portal(mouth, mouth with { Y = Floor + 1 })));

        long first = twin.Tick();
        Assert.Equal(15 * FluidConstants.MillilitresPerLitre, first);
        twin.AssertIdentity();

        long shaftCapacity = 20L * FluidConstants.CellCapacityMillilitres;
        int ticks = 1;
        for (; ticks < 400; ticks++)
        {
            twin.Tick();
            twin.AssertIdentity();
            if (twin.Fluid.TotalMillilitres > shaftCapacity - FluidConstants.MillilitresPerLitre
                && twin.Drain.Draw() == 0)
            {
                break;
            }
        }

        Assert.True(ticks < 400, "the shaft never filled");
        Assert.InRange(
            twin.Fluid.TotalMillilitres,
            shaftCapacity - FluidConstants.MillilitresPerLitre + 1,
            shaftCapacity);
        Assert.Equal(100_000 - twin.Water.DrawnLitres, twin.Water.StoredLitres);
        Assert.True(twin.Water.LevelAt(twin.Basin) < levelBefore);

        // Full to the litre: nothing more is drawn while the shaft holds what it holds.
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(0, twin.Tick());
        }

        twin.AssertIdentity();
        output.WriteLine(
            $"shaft full after {ticks} ticks: {twin.Water.DrawnLitres:N0} L drawn, "
            + $"{twin.Fluid.TotalMillilitres:N0} mL held of {shaftCapacity:N0}");
    }

    [Fact]
    public void ATunnelIntoTheBankDrawsUntilTheLakeReachesTheFaceAndNotBefore()
    {
        // The covered test is on the outside voxel. A tunnel driven into the bowl's wall at Y 35
        // opens onto the lake at Y 35; the lake stands at 35.375 and drains until its level is at
        // or under the face, then stops with water still in it and room still in the tunnel.
        var twin = new Twin();
        twin.Water.Inject(twin.Basin, 70_000);
        double level = twin.Water.LevelAt(twin.Basin)!.Value;
        Assert.True(level > 35 && level < 36, $"the fixture's lake stands at {level}");

        const int face = 35;
        var inside = new VoxelPos(47, face, 60);
        var outside = new VoxelPos(48, face, 60);
        Assert.Equal(Rim, twin.Seed.SurfaceHeightAt(inside.X, inside.Z));
        Assert.Equal(Floor, twin.Seed.SurfaceHeightAt(outside.X, outside.Z));

        // The face, and a chamber behind and below it big enough for far more than will come.
        // The chamber stops one column short of the wall, so the face is the only portal: a
        // chamber flush with the wall would open a bank portal on every row down to the lake
        // bed, and the lowest of them would drain the lake to its floor.
        twin.DigBox(30, face - 9, 58, 46, face, 62);
        twin.Dig(inside);
        Assert.Equal([new Portal(inside, outside)], twin.Index.Under(3, 3));
        Assert.Equal(1, twin.Index.Count);

        int ticks = 0;
        int idle = 0;
        while (idle < 5 && ticks < 2_000)
        {
            idle = twin.Tick() == 0 ? idle + 1 : 0;
            twin.AssertIdentity();
            ticks++;
        }

        Assert.True(idle >= 5, "the draw never stopped");

        double after = twin.Water.LevelAt(twin.Basin)!.Value;
        Assert.True(after <= face, $"the lake stands at {after}, above the face at {face}");

        // And not before: one more draw would have taken the level under the face, so what is
        // left is within one draw of the face's own volume — 4 voxels of depth over 4 cells.
        long litresAtFace = (face - (Floor + 1)) * 4L * 4000;
        Assert.InRange(twin.Water.StoredLitres, litresAtFace - 15, litresAtFace);

        long chamberCapacity = 17L * 10 * 5 * FluidConstants.CellCapacityMillilitres;
        Assert.True(twin.Fluid.TotalMillilitres < chamberCapacity / 2, "the chamber filled, so room stopped it, not the face");

        output.WriteLine(
            $"stopped after {ticks} ticks at level {after:F3} (face {face}): "
            + $"{twin.Water.DrawnLitres:N0} L drawn, {twin.Water.StoredLitres:N0} L left");
    }

    [Fact]
    public void ADryExcavationCostsNothing()
    {
        var twin = new Twin();
        twin.DigBox(60, Floor - 9, 60, 63, Floor, 63);
        Assert.True(twin.Index.Count > 0);

        Assert.Equal(0, twin.Drain.Draw());
        Assert.Equal(0, twin.Drain.WetSamplesLastPass);
        Assert.Equal(0, twin.Drain.CoveredPortalsLastPass);
        Assert.Equal(0, twin.Water.DrawnLitres);
    }

    [Fact]
    public void APortalUnderTheLakeButAboveItsSurfaceDrawsNothing()
    {
        // A bank tunnel at Y 38 into a lake standing at 35.375: the outside voxel is dry, so the
        // portal is filed and skipped, and it draws the moment the lake rises past it.
        var twin = new Twin();
        twin.Water.Inject(twin.Basin, 70_000);

        twin.DigBox(40, 38, 60, 47, 38, 60);
        Assert.True(twin.Index.Contains(new Portal(new VoxelPos(47, 38, 60), new VoxelPos(48, 38, 60))));

        Assert.Equal(0, twin.Tick());
        Assert.Equal(1, twin.Drain.WetSamplesLastPass);
        Assert.Equal(0, twin.Drain.CoveredPortalsLastPass);

        twin.Water.Inject(twin.Basin, 60_000);
        Assert.True(twin.Water.LevelAt(twin.Basin) > 38);
        Assert.True(twin.Tick() > 0);
        twin.AssertIdentity();
    }

    [Fact]
    public void AWindowMoveKeepsTheSeamExact()
    {
        // The index is keyed on the world lattice and the basin is looked up every pass, so a
        // re-analysed window with the bowl at a different array index draws on as before, and
        // the ledger carries the lake across.
        var twin = new Twin();
        twin.Water.Inject(twin.Basin, 100_000);
        twin.DigBox(60, Floor - 19, 60, 60, Floor, 60);

        for (int i = 0; i < 5; i++)
        {
            twin.Tick();
            twin.AssertIdentity();
        }

        long drawnBefore = twin.Water.DrawnLitres;
        long storedBefore = twin.Water.StoredLitres;
        int basinBefore = twin.Basin;

        // A window whose origin is 32 voxels over: the bowl at 48..79 is inside both.
        Assert.True(twin.Water.NeedsWindowFor(112, 112));
        twin.Water.Install(twin.Water.AnalyseFor(112, 112));
        Assert.Equal(storedBefore, twin.Water.StoredLitres);
        Assert.NotEqual(Watershed.Outlet, twin.Basin);

        // Over a run of ticks rather than every tick: a dead-end one-voxel shaft takes water only
        // as fast as the cell under its mouth has room when it is visited, which after the first
        // tick is about a litre a tick, so single ticks legitimately draw nothing while the mouth
        // waits. The seam's claim is that the move changed nothing, not that every tick draws.
        long drawnAcross = 0;
        for (int i = 0; i < 20; i++)
        {
            drawnAcross += twin.Tick();
            twin.AssertIdentity();
        }

        Assert.True(drawnAcross > 0, "nothing was drawn after the move");
        Assert.True(twin.Water.DrawnLitres > drawnBefore);
        output.WriteLine(
            $"basin {basinBefore} → {twin.Basin} across the move; {drawnAcross / 1000:N0} L drawn in 20 ticks after it");
    }
}
