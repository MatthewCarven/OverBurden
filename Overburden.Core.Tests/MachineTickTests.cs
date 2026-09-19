using Overburden.Core.Building;
using Overburden.Core.Content;
using Overburden.Core.Fluid;
using Overburden.Core.Hydrology;
using Overburden.Core.Lighting;
using Overburden.Core.Machines;
using Overburden.Core.Persistence;
using Overburden.Core.Voxels;
using Overburden.Core.Worldgen;
using Xunit.Abstractions;

namespace Overburden.Core.Tests;

/// <summary>
/// M10a-3: the machine tick. A pump is a drain and an outlet is a pour, end to end inside one
/// boundary call, on either tier, with nothing in flight afterwards — <c>pumped == emitted</c>
/// per boundary, both tiers' identities exact beside it, and the world's total a constant of the
/// machine layer.
/// </summary>
/// <remarks>
/// <para>
/// The fixture is the lake ceiling's bowl with the hand-off's ramp east of it, a room dug under
/// the bowl and a second room under the rim, both tiers, the portal drain and the hand-off — the
/// whole seam, so that a pump's litres can be shown to leave <c>drawn</c> and <c>handed</c>
/// alone. Heights are the topmost solid voxel; the plane the water stands on is one above. The
/// bowl's floor is 31 so its bed is plane 32, which is cell-aligned: a body at cell Y 8 stands on
/// the bed, and one at cell Y 10 stands on the rim (39, plane 40).
/// </para>
/// <para>
/// A body is 64 voxels marked placed in the terrain and a row in the set, which is what
/// <c>Place.Machine</c> leaves behind; the tick reads rows and the terrain and never the
/// placement path.
/// </para>
/// </remarks>
public class MachineTickTests(ITestOutputHelper output)
{
    private const ulong Seed = 0x9A3EUL;
    private const int Cell = HeightField.DefaultCellVoxels;
    private const int Pitch = 128 / Cell;
    private const int Cap = FluidConstants.CellCapacityMillilitres;
    private const int Litre = FluidConstants.MillilitresPerLitre;
    private const int Cell64 = BrushCell.Voxels * BrushCell.Voxels * BrushCell.Voxels;

    private const int Floor = 31;
    private const int Rim = 39;

    private const int Air = 0, Stone = 1, Pump = 2, Outlet = 3, SlowPump = 4, WidePump = 5;

    // M10c: the pool's materials — a 500 W panel, one at 90%, a 5 GJ cell, a 100 J cell, the
    // relay with its bill, and a pack.
    private const int Panel = 6, DimPanel = 7, BigCell = 8, SmallCell = 9, BilledPump = 10, PackMaterial = 11;

    /// <summary>The relay's rate: 100 L/s is 50,000 mL a boundary, 3.2 cells.</summary>
    private const long RelayMillilitresPerBoundary = 50_000;

    /// <summary>A 500 W panel over 500 ms, open and clear: 250,000 mJ.</summary>
    private const long PanelMillijoulesPerBoundary = 250_000;

    /// <summary>The relay's bill: a joule a litre is a millijoule a millilitre, so 1,000 J/L is 50,000,000 mJ a boundary at full rate.</summary>
    private const long RelayBill = 1000;

    private const long BigCellCapacity = 5_000_000_000L * 1000L;
    private const long SmallCellCapacity = 100L * 1000L;
    private const long PackCapacity = 3_600_000L * 1000L;
    private const long PackRechargePerBoundary = 1000L * 500L;

    private static MachineDefinition PumpAt(long millilitresPerSecond, int radius = 1, long joulesPerLitre = 0) => new()
    {
        Kind = MachineKind.Pump,
        MillilitresPerSecond = millilitresPerSecond,
        IntakeRadiusCells = radius,
        JoulesPerLitre = joulesPerLitre,
        Links = [new MachineLink("water", MachineMedium.Fluid, MachineRole.Source, MachineLink.ChannelVia, null, "channel")],
        Ports = [new PortDefinition("channel", 0, 0, 15), new PortDefinition("weight", 1, 1, 100)],
    };

    private static MachineDefinition PanelAt(long watts, int efficiencyPerMille = 1000) => new()
    {
        Kind = MachineKind.Converter,
        RatedWatts = watts,
        EfficiencyPerMille = efficiencyPerMille,
        Links =
        [
            new MachineLink("sky", MachineMedium.Power, MachineRole.Sink, MachineDefinition.SolarVia, null, null),
            new MachineLink("out", MachineMedium.Power, MachineRole.Source, MachineLink.ChannelVia, null, "channel"),
        ],
        Ports = [new PortDefinition("channel", 0, 0, 15)],
    };

    private static MachineDefinition CellOf(long capacityMillijoules, bool carried = false, long? rechargeWatts = null) => new()
    {
        Kind = MachineKind.Storage,
        CapacityMillijoules = capacityMillijoules,
        Carried = carried,
        RechargeWatts = rechargeWatts,
        Links = [new MachineLink("charge", MachineMedium.Power, MachineRole.Sink, MachineLink.ChannelVia, null, "channel")],
        Ports = [new PortDefinition("channel", 0, 0, 15)],
    };

    private static readonly MachineDefinition OutletDefinition = new()
    {
        Kind = MachineKind.Outlet,
        Links = [new MachineLink("water", MachineMedium.Fluid, MachineRole.Sink, MachineLink.ChannelVia, null, "channel")],
        Ports = [new PortDefinition("channel", 0, 0, 15), new PortDefinition("weight", 1, 1, 100)],
    };

    internal static readonly MaterialTable Materials = MaterialTable.ForTesting(
        //             air    stone  pump   outlet slow   wide   panel  dim    big    small  billed pack
        isOpaque: [false, true, true, true, true, true, true, true, true, true, true, true],
        isFluid: [false, false, false, false, false, false, false, false, false, false, false, false],
        textureLayer: [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11],
        hardness: [0f, 3.0f, 3.0f, 3.0f, 3.0f, 3.0f, 1.0f, 1.0f, 3.0f, 3.0f, 3.0f, 1.0f],
        machine:
        [
            null, null, PumpAt(100_000), OutletDefinition, PumpAt(470), PumpAt(100_000, radius: 2),
            PanelAt(500), PanelAt(500, 900), CellOf(BigCellCapacity), CellOf(SmallCellCapacity),
            PumpAt(100_000, joulesPerLitre: RelayBill), CellOf(PackCapacity, carried: true, rechargeWatts: 1000),
        ],
        unit:
        [
            default, default, MaterialUnit.Cells, MaterialUnit.Cells, MaterialUnit.Cells, MaterialUnit.Cells,
            MaterialUnit.Cells, MaterialUnit.Cells, MaterialUnit.Cells, MaterialUnit.Cells, MaterialUnit.Cells, MaterialUnit.Count,
        ],
        placeable: [true, true, true, true, true, true, true, true, true, true, true, false]);

    /// <summary>The sky a test dictates: a cell's open columns, or nothing loaded over it.</summary>
    private sealed class DictatedSky : ISolarSky
    {
        public Dictionary<BrushCell, int> Open { get; } = [];

        public bool TryOpenColumnsAbove(BrushCell cell, out int open) => Open.TryGetValue(cell, out open);
    }

    /// <summary>The bowl over 48..79, the ramp rising east of it over 80..93, the rim everywhere else.</summary>
    private static ISurfaceHeights BowlAndRamp() => new AnalyticSurface((x, z) =>
    {
        if (x >= 48 && x < 80 && z >= 48 && z < 80)
        {
            return Floor;
        }

        if (x >= 80 && x < 100 && z >= 48 && z < 80)
        {
            return Math.Min(Rim, Floor + ((x - 80) / 2) + 1);
        }

        return Rim;
    });

    /// <summary>Dug voxels and open air above the seed, less what was placed, inside the loaded box.</summary>
    private sealed class EditedTerrain(ISurfaceHeights seed) : IFluidTerrain
    {
        public HashSet<VoxelPos> Dug { get; } = [];

        public HashSet<VoxelPos> Placed { get; } = [];

        public HashSet<ChunkPos> Loaded { get; } = [];

        public bool CanHold(VoxelPos v) =>
            Loaded.Contains(v.Chunk)
            && !Placed.Contains(v)
            && (Dug.Contains(v) || v.Y > seed.SurfaceHeightAt(v.X, v.Z));

        public bool IsLoaded(VoxelPos v) => Loaded.Contains(v.Chunk);
    }

    private sealed class Twin
    {
        // The room under the bowl: chunk (1, 0, 1), cells X 8..12, Y 2..5, Z 8..12 — its west
        // wall is the chunk's, so a pump against it has a box reaching into chunk X 0.
        public const int RoomMin = 32, RoomMax = 51, RoomFloor = 8, RoomTop = 23;

        // Where a pour lands: the floor rows through the pumps' boxes, nearest the pumps first.
        public const int PourZMin = 36, PourZMax = 47;

        // The second room, under the rim: chunk (0, 0, 1), cells X 2..5, Y 2..5, Z 10..13.
        public const int WestMin = 8, WestMax = 23, WestZMin = 40, WestZMax = 55;

        public static readonly BrushCell PumpCell = new(10, 2, 10);
        public static readonly BrushCell RimOutlet = new(9, 10, 9);
        public static readonly BrushCell RimOutletB = new(11, 10, 9);
        public static readonly BrushCell RoomOutlet = new(12, 2, 12);
        public static readonly BrushCell RoofedOutlet = new(9, 5, 12);
        public static readonly BrushCell BedPump = new(14, 8, 14);
        public static readonly BrushCell BedOutlet = new(18, 8, 18);
        public static readonly BrushCell WestOutlet = new(3, 2, 11);
        public static readonly ChunkPos RoomChunk = new(1, 0, 1);
        public static readonly ChunkPos WestChunk = new(0, 0, 1);
        public static readonly ChunkPos BedOutletChunk = new(2, 1, 2);

        public Twin(long boundaryMillis = MachineTick.DefaultBoundaryMillis)
        {
            Seed = BowlAndRamp();
            Terrain = new EditedTerrain(Seed);
            Water = new WaterSystem(Seed, MachineTickTests.Seed, new BasinLedger(), Pitch);
            Water.Install(Water.AnalyseFor(64, 64));
            Ceiling = new LakeCeiling(Seed, Water);
            Bounded = new LakeBoundedTerrain(Terrain, Ceiling);
            Fluid = new FluidSimulation(Bounded);
            Index = new PortalIndex(Seed, Terrain);
            Tracker = new PortalTracker(Index);
            Drain = new PortalDrain(Index, Water, Fluid);
            Handoff = new SurfaceHandoff(Seed, Water, Fluid, Ceiling);
            Machines = new MachineSet(Materials);
            Tick = new MachineTick(Machines, Materials, Fluid, Bounded, Seed, Water, boundaryMillis) { Sky = Sky };

            for (int cz = 1; cz <= 2; cz++)
            {
                for (int cx = 0; cx <= 2; cx++)
                {
                    for (int cy = 0; cy <= 1; cy++)
                    {
                        Load(new ChunkPos(cx, cy, cz));
                    }
                }
            }

            DigBox(RoomMin, RoomFloor, RoomMin, RoomMax, RoomTop, RoomMax);
            DigBox(WestMin, RoomFloor, WestZMin, WestMax, RoomTop, WestZMax);
            Refresh();
        }

        public ISurfaceHeights Seed { get; }

        public EditedTerrain Terrain { get; }

        public LakeBoundedTerrain Bounded { get; }

        public WaterSystem Water { get; }

        public LakeCeiling Ceiling { get; }

        public FluidSimulation Fluid { get; }

        public PortalIndex Index { get; }

        public PortalTracker Tracker { get; }

        public PortalDrain Drain { get; }

        public SurfaceHandoff Handoff { get; }

        public MachineSet Machines { get; }

        public MachineTick Tick { get; }

        public DictatedSky Sky { get; } = new();

        public HashSet<ChunkPos> Reachable { get; } = [];

        public int Basin => Water.BasinAtSample(48 / Cell, 48 / Cell);

        public void Load(ChunkPos chunk)
        {
            if (Terrain.Loaded.Add(chunk))
            {
                Reachable.Add(chunk);
                Tracker.OnVoxelsReady(chunk, hasEdits: false);
                Fluid.ChunkArrived(chunk);
            }
        }

        public void Unload(ChunkPos chunk)
        {
            Terrain.Loaded.Remove(chunk);
            Reachable.Remove(chunk);
        }

        public void Dig(VoxelPos v)
        {
            Terrain.Dug.Add(v);
            Tracker.OnVoxelChanged(v);
        }

        public void DigBox(int x0, int y0, int z0, int x1, int y1, int z1)
        {
            for (int y = y0; y <= y1; y++)
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                Dig(new VoxelPos(x, y, z));
            }
        }

        /// <summary>What <c>Place.Machine</c> leaves: 64 solid voxels and a row with the definition's default ports.</summary>
        /// <param name="charge">A storage row's charge, written before the row enters so the set's entering count sees it — a restored row's shape, never a placed one's (a placed cell is empty).</param>
        /// <param name="open">A converter's open columns as the sky reports them; null leaves the column unloaded.</param>
        public MachineRow Place(BrushCell cell, int material, int channel = 0, int weight = 1, long charge = 0, int? open = 16)
        {
            for (int lz = 0; lz < BrushCell.Voxels; lz++)
            for (int lx = 0; lx < BrushCell.Voxels; lx++)
            for (int ly = 0; ly < BrushCell.Voxels; ly++)
            {
                Terrain.Placed.Add(cell.VoxelAt(lx, ly, lz));
            }

            var row = new MachineRow(cell, material, BrushFacing.PositiveX, Materials.Machine(material)!.DefaultPorts());
            row.SetPortUnchecked("channel", channel);
            if (row.TryGetPort("weight", out _))
            {
                row.SetPortUnchecked("weight", weight);
            }

            if (charge > 0)
            {
                StorageState.Write(row, charge);
            }

            if (Materials.Machine(material)!.Kind == MachineKind.Converter && open is { } columns)
            {
                Sky.Open[cell] = columns;
            }

            Machines.Add(row);
            return row;
        }

        /// <summary>Takes every millilitre out of the room, so the next boundary reads no water.</summary>
        public void DrainRoom()
        {
            for (int y = RoomFloor; y <= RoomTop; y++)
            for (int z = RoomMin; z <= RoomMax; z++)
            for (int x = RoomMin; x <= RoomMax; x++)
            {
                var v = new VoxelPos(x, y, z);
                long held = Fluid.Grid.AmountAt(v);
                if (held > 0)
                {
                    Fluid.Remove(v, held);
                }
            }
        }

        /// <summary>The bodies stand; only the rows are replaced — a quit and a reopen as the set sees it.</summary>
        public void ReopenRows()
        {
            RestoreReport report = Machines.Restore(Machines.Snapshot(), id => Materials.Machine(id)?.Ports);
            Assert.Equal(0, report.Clamped + report.Dropped + report.Defaulted + report.Unknown);
        }

        /// <summary>Whole cells onto the room's floor, nearest the pump first, until the litres are down.</summary>
        public long PourIntoRoom(long millilitres, int xMin = RoomMin, int xMax = RoomMax, int zMin = PourZMin, int zMax = PourZMax)
        {
            long rejected = 0;
            long remaining = millilitres;
            for (int y = RoomFloor; y <= RoomTop && remaining > 0; y++)
            for (int z = zMin; z <= zMax && remaining > 0; z++)
            for (int x = xMin; x <= xMax && remaining > 0; x++)
            {
                var v = new VoxelPos(x, y, z);
                if (!Bounded.CanHold(v) || Fluid.Grid.AmountAt(v) > 0)
                {
                    continue;
                }

                long give = Math.Min(Cap, remaining);
                rejected += Fluid.Add(v, give);
                remaining -= give;
            }

            Assert.Equal(0L, rejected);
            Assert.Equal(0L, remaining);
            return millilitres;
        }

        /// <summary>A lake in the bowl, the ceiling rebuilt over it.</summary>
        public void Lake(long litres)
        {
            long spilled = Water.Inject(Basin, litres);
            Assert.Equal(0L, spilled);
            Refresh();
        }

        public void Refresh() => Ceiling.Refresh(Reachable, Fluid.WakeCell);

        /// <summary>The boundary's passes in the game's order — the hand-off, the drain, the machines — then the ceiling and one tick.</summary>
        public MachineTickReport Boundary(bool step = true)
        {
            Handoff.Absorb();
            Drain.Draw();
            MachineTickReport report = Tick.Run();
            Refresh();
            if (step)
            {
                Fluid.Step();
            }

            return report;
        }

        /// <summary>Millilitres standing in the layer above an outlet's top — where its pour lands.</summary>
        public long OverMouthOf(BrushCell outlet)
        {
            long held = 0;
            for (int lz = 0; lz < BrushCell.Voxels; lz++)
            for (int lx = 0; lx < BrushCell.Voxels; lx++)
            {
                held += Fluid.Grid.AmountAt(outlet.VoxelAt(lx, BrushCell.Voxels, lz));
            }

            return held;
        }

        /// <summary>Millilitres in the intake box of a pump at <paramref name="cell"/>.</summary>
        public long InBoxOf(BrushCell cell, int radius = 1)
        {
            long held = 0;
            foreach ((int dx, int dy, int dz) in MachineTick.IntakeCellsAt(radius))
            {
                BrushCell neighbour = cell.Offset(dx, dy, dz);
                for (int lz = 0; lz < BrushCell.Voxels; lz++)
                for (int lx = 0; lx < BrushCell.Voxels; lx++)
                for (int ly = 0; ly < BrushCell.Voxels; ly++)
                {
                    held += Fluid.Grid.AmountAt(neighbour.VoxelAt(lx, ly, lz));
                }
            }

            return held;
        }

        /// <summary>Both tiers' identities, the seam's, the machine layer's, and the whole world's, exactly.</summary>
        public void AssertIdentity()
        {
            long a = Water.StoredLitres;
            Assert.Equal(
                Water.ReceivedLitres + Water.InjectedLitres + Water.EmittedLitres
                    - Water.SpilledLitres - Water.DrawnLitres - Water.SeepedLitres - Water.PumpedLitres,
                a);

            long b = Fluid.TotalMillilitres;
            Assert.Equal(
                Fluid.AddedMillilitres + Fluid.EmittedMillilitres - Fluid.RemovedMillilitres
                    - Fluid.DisplacedMillilitres - Fluid.HandedMillilitres - Fluid.PumpedMillilitres,
                b);
            Fluid.Publish();
            Assert.True(Fluid.Published.IdentityExact, "the published identity disagrees with the live one");

            // The seam from both ends, and the machine layer from both ends.
            Assert.Equal(Water.DrawnLitres * Litre, Drain.DrawnMillilitres);
            Assert.Equal(Fluid.HandedMillilitres, Handoff.HandedMillilitres);
            Assert.Equal((Water.ReceivedLitres * Litre) + Handoff.PendingMillilitres, Handoff.HandedMillilitres);
            Assert.True(Tick.CrossTierExact, "pumped != emitted across the tiers");
            Assert.Equal(Fluid.PumpedMillilitres, Tick.PumpedMillilitres);
            Assert.Equal(Fluid.EmittedMillilitres, Tick.EmittedMillilitres);
            Assert.Equal(Water.PumpedLitres, Tick.PumpedLitres);
            Assert.Equal(Water.EmittedLitres, Tick.EmittedLitres);

            // The whole world: every transfer between the tiers and within them cancels, so a
            // pump is invisible here. Injected water enters, spilled, removed and displaced water
            // leaves, and nothing else moves the sum.
            Assert.Equal(
                ((Water.InjectedLitres - Water.SpilledLitres) * Litre)
                    + Fluid.AddedMillilitres - Drain.DrawnMillilitres - Fluid.RemovedMillilitres - Fluid.DisplacedMillilitres,
                (a * Litre) + Handoff.PendingMillilitres + b);
        }
    }

    [Fact]
    public void TheFixtureIsOneBasinWithTheRampDrainingIntoItAndTheRoomsDry()
    {
        var twin = new Twin();
        Assert.NotEqual(Watershed.Outlet, twin.Basin);
        Assert.Equal(twin.Basin, twin.Water.BasinAtSample(72 / Cell, 72 / Cell));
        Assert.Equal(twin.Basin, twin.Water.BasinAtSample(88 / Cell, 60 / Cell));
        Assert.Equal(0, twin.Water.Window!.Watershed.Field.OriginX);
        Assert.Equal(Floor + 1, twin.Water.Window.Watershed.Basins[twin.Basin].PitHeight);

        // The rooms are air the water can hold; the ground over them is not; the bed is plane 32.
        Assert.True(twin.Bounded.CanHold(new VoxelPos(40, 8, 40)));
        Assert.False(twin.Bounded.CanHold(new VoxelPos(40, 7, 40)));
        Assert.False(twin.Bounded.CanHold(new VoxelPos(40, 24, 40)));
        Assert.True(twin.Bounded.CanHold(new VoxelPos(60, 32, 60)));
        Assert.False(twin.Bounded.CanHold(new VoxelPos(60, 31, 60)));

        // Fact 6's two answers: a sample the window covers and says drains off its edge, and a
        // sample it does not cover, read the same through BasinAtSample and differ through Try.
        Assert.Equal(Watershed.Outlet, twin.Water.BasinAtSample(-1, 5));
        Assert.False(twin.Water.TryBasinAtSample(-1, 5, out _));
        Assert.Equal(Watershed.Outlet, twin.Water.BasinAtSample(Pitch - 1, Pitch - 1));
        Assert.True(twin.Water.TryBasinAtSample(Pitch - 1, Pitch - 1, out int edge));
        Assert.Equal(Watershed.Outlet, edge);
    }

    [Fact]
    public void TheIntakeCellsAreNearestFirstFacesThenEdgesThenCorners()
    {
        ReadOnlySpan<(int Dx, int Dy, int Dz)> one = MachineTick.IntakeCellsAt(1);
        Assert.Equal(26, one.Length);
        for (int i = 0; i < 6; i++) Assert.Equal(1, Math.Abs(one[i].Dx) + Math.Abs(one[i].Dy) + Math.Abs(one[i].Dz));
        for (int i = 6; i < 18; i++) Assert.Equal(2, Math.Abs(one[i].Dx) + Math.Abs(one[i].Dy) + Math.Abs(one[i].Dz));
        for (int i = 18; i < 26; i++) Assert.Equal(3, Math.Abs(one[i].Dx) + Math.Abs(one[i].Dy) + Math.Abs(one[i].Dz));
        Assert.DoesNotContain((0, 0, 0), one.ToArray());

        ReadOnlySpan<(int Dx, int Dy, int Dz)> two = MachineTick.IntakeCellsAt(2);
        Assert.Equal(124, two.Length);
        for (int i = 1; i < two.Length; i++)
        {
            int a = Math.Abs(two[i - 1].Dx) + Math.Abs(two[i - 1].Dy) + Math.Abs(two[i - 1].Dz);
            int b = Math.Abs(two[i].Dx) + Math.Abs(two[i].Dy) + Math.Abs(two[i].Dz);
            Assert.True(a <= b, $"cell {i} is nearer than cell {i - 1}");
        }
    }

    // ---------------------------------------------------------------- M10c: the pool

    private static long ChargeOf(MachineRow row) => StorageState.Read(row);

    [Fact]
    public void APanelWithNoCellClampsEverythingItGeneratesAndTheIdentityHolds()
    {
        // Generation with no storage is use-it-or-lose-it, and the loss is a printed term (the
        // ladder's decision 9): a channel with no battery reads clamped == generated.
        var twin = new Twin();
        twin.Place(Twin.RimOutlet, Panel);

        for (int boundary = 0; boundary < 3; boundary++)
        {
            MachineTickReport report = twin.Boundary(step: false);
            Assert.Equal(PanelMillijoulesPerBoundary, report.GeneratedMillijoules);
            Assert.Equal(PanelMillijoulesPerBoundary, report.ClampedMillijoules);
            Assert.Equal(0L, report.DrawnMillijoules);
            Assert.True(twin.Tick.PowerExact);
        }

        Assert.Equal(3 * PanelMillijoulesPerBoundary, twin.Tick.GeneratedMillijoules);
        Assert.Equal(twin.Tick.GeneratedMillijoules, twin.Tick.ClampedMillijoules);
        Assert.Equal(0L, twin.Tick.StoredMillijoules);
    }

    [Fact]
    public void APanelAndACellChargeByExactlyTheProductEachBoundary()
    {
        // Decision 2's one integer product: 500 W × 500 ms × open/16 × (256 − byte)/256 × eff/1000.
        var twin = new Twin();
        twin.Place(Twin.RimOutlet, Panel);
        MachineRow cell = twin.Place(Twin.RimOutletB, BigCell);

        twin.Boundary(step: false);
        Assert.Equal(PanelMillijoulesPerBoundary, ChargeOf(cell));
        twin.Boundary(step: false);
        twin.Boundary(step: false);
        Assert.Equal(3 * PanelMillijoulesPerBoundary, ChargeOf(cell));
        Assert.Equal(0L, twin.Tick.ClampedMillijoules);

        // The storm: a byte of 255 leaves a 256th, never nothing — 976 mJ, the split row's oracle.
        twin.Tick.OvercastByte = 255;
        MachineTickReport storm = twin.Boundary(step: false);
        Assert.Equal(976L, storm.GeneratedMillijoules);
        Assert.Equal((3 * PanelMillijoulesPerBoundary) + 976, ChargeOf(cell));

        // Half the columns roofed, halves it; none open, nothing.
        twin.Tick.OvercastByte = 0;
        twin.Sky.Open[Twin.RimOutlet] = 8;
        Assert.Equal(PanelMillijoulesPerBoundary / 2, twin.Boundary(step: false).GeneratedMillijoules);
        twin.Sky.Open[Twin.RimOutlet] = 0;
        Assert.Equal(0L, twin.Boundary(step: false).GeneratedMillijoules);

        // A panel at 90% gives 225,000 beside a full one's 250,000.
        twin.Sky.Open[Twin.RimOutlet] = 16;
        twin.Place(Twin.RoomOutlet, DimPanel);
        Assert.Equal(PanelMillijoulesPerBoundary + 225_000, twin.Boundary(step: false).GeneratedMillijoules);
        Assert.True(twin.Tick.PowerExact);
    }

    [Fact]
    public void AnUnloadedPanelGeneratesFromItsLastSampleAndOneNeverSampledIsDark()
    {
        var twin = new Twin();
        MachineRow panel = twin.Place(Twin.RimOutlet, Panel, open: 12);
        twin.Place(Twin.RimOutletB, BigCell);

        Assert.Equal(187_500L, twin.Boundary(step: false).GeneratedMillijoules);
        Assert.Equal(12, ConverterState.ReadOpenColumns(panel));
        Assert.Equal(1L, twin.Tick.SolarSampled);

        // The column leaves the box: the byte on the row stands in.
        twin.Sky.Open.Remove(Twin.RimOutlet);
        Assert.Equal(187_500L, twin.Boundary(step: false).GeneratedMillijoules);
        Assert.Equal(1L, twin.Tick.SolarUnsampled);

        // And a panel placed where nobody has the column generates nothing until somebody does.
        twin.Place(Twin.RoomOutlet, Panel, open: null);
        Assert.Equal(187_500L, twin.Boundary(step: false).GeneratedMillijoules);
        twin.Sky.Open[Twin.RoomOutlet] = 16;
        Assert.Equal(187_500L + PanelMillijoulesPerBoundary, twin.Boundary(step: false).GeneratedMillijoules);
        Assert.True(twin.Tick.PowerExact);
    }

    [Fact]
    public void TwoCellsFillInOriginOrderAndTheSecondOnlyOnceTheFirstIsFull()
    {
        // Four columns open: 62,500 mJ a boundary into two 100,000 mJ cells.
        var twin = new Twin();
        twin.Place(Twin.RimOutlet, Panel, open: 4);
        MachineRow first = twin.Place(Twin.RimOutletB, SmallCell);
        MachineRow second = twin.Place(new BrushCell(12, 10, 9), SmallCell);
        Assert.True(MachineTickOriginBefore(first, second));

        twin.Boundary(step: false);
        Assert.Equal(62_500L, ChargeOf(first));
        Assert.Equal(0L, ChargeOf(second));

        twin.Boundary(step: false);
        Assert.Equal(SmallCellCapacity, ChargeOf(first));
        Assert.Equal(25_000L, ChargeOf(second));

        twin.Boundary(step: false);
        Assert.Equal(87_500L, ChargeOf(second));

        MachineTickReport full = twin.Boundary(step: false);
        Assert.Equal(SmallCellCapacity, ChargeOf(second));
        Assert.Equal(50_000L, full.ClampedMillijoules);
        Assert.True(twin.Tick.PowerExact);
    }

    private static bool MachineTickOriginBefore(MachineRow a, MachineRow b) =>
        a.Origin.X < b.Origin.X || (a.Origin.X == b.Origin.X && (a.Origin.Y < b.Origin.Y || (a.Origin.Y == b.Origin.Y && a.Origin.Z < b.Origin.Z)));

    [Fact]
    public void ABilledPumpMovesItsBudgetAndTheCellFallsByExactlyTheBill()
    {
        var twin = new Twin();
        MachineRow pump = twin.Place(Twin.PumpCell, BilledPump);
        twin.Place(Twin.RimOutlet, Outlet);
        MachineRow cell = twin.Place(Twin.RoomOutlet, BigCell, charge: BigCellCapacity);
        twin.PourIntoRoom(120 * Cap);
        twin.Fluid.Settle(200);
        Assert.Equal(BigCellCapacity, twin.Machines.StoredMillijoulesEntering);

        MachineTickReport report = twin.Boundary(step: false);

        Assert.Equal(1, report.Moved);
        Assert.Equal(RelayMillilitresPerBoundary, pump.LastMovedMillilitres);
        Assert.Equal(RelayMillilitresPerBoundary * RelayBill, report.DrawnMillijoules);
        Assert.Equal(BigCellCapacity - (RelayMillilitresPerBoundary * RelayBill), ChargeOf(cell));
        Assert.Equal(0L, report.ClampedMillijoules);
        Assert.True(twin.Tick.PowerExact);
        twin.AssertIdentity();
    }

    [Fact]
    public void APumpWithTwentyFiveLitresOfJoulesMovesExactlyThatAndThenStallsUnpoweredUnlessDry()
    {
        var twin = new Twin();
        MachineRow pump = twin.Place(Twin.PumpCell, BilledPump);
        twin.Place(Twin.RimOutlet, Outlet);
        MachineRow cell = twin.Place(Twin.RoomOutlet, BigCell, charge: 25_000L * RelayBill);
        twin.PourIntoRoom(120 * Cap);
        twin.Fluid.Settle(200);

        MachineTickReport first = twin.Boundary(step: false);
        Assert.Equal(1, first.Moved);
        Assert.Equal(25_000L, pump.LastMovedMillilitres);
        Assert.Equal(0L, ChargeOf(cell));

        // Water, room and an outlet, and nothing to pay with.
        MachineTickReport second = twin.Boundary(step: false);
        Assert.Equal(0, second.Moved);
        Assert.Equal(1, second.Unpowered);
        Assert.Equal(PumpStall.Unpowered, pump.LastStall);
        Assert.Equal(1L, twin.Tick.StalledUnpowered);

        // A dry pump is not a starved one: no water beats unpowered.
        twin.DrainRoom();
        MachineTickReport dry = twin.Boundary(step: false);
        Assert.Equal(PumpStall.NoWater, pump.LastStall);
        Assert.Equal(0, dry.Unpowered);
        Assert.True(twin.Tick.PowerExact);
        twin.AssertIdentity();
    }

    [Fact]
    public void TwoPumpsOnOneChannelShareACellFirstComeInOriginOrder()
    {
        // 75 L worth: the first pump in origin order takes its 50, the second gets 25.
        var twin = new Twin();
        MachineRow a = twin.Place(Twin.PumpCell, BilledPump);
        MachineRow b = twin.Place(new BrushCell(11, 2, 10), BilledPump);
        Assert.True(MachineTickOriginBefore(a, b));
        twin.Place(Twin.RimOutlet, Outlet);
        twin.Place(Twin.RimOutletB, Outlet);
        twin.Place(Twin.RoomOutlet, BigCell, charge: 75_000L * RelayBill);
        twin.PourIntoRoom(200 * Cap);
        twin.Fluid.Settle(200);

        MachineTickReport report = twin.Boundary(step: false);

        Assert.Equal(2, report.Moved);
        Assert.Equal(RelayMillilitresPerBoundary, a.LastMovedMillilitres);
        Assert.Equal(25_000L, b.LastMovedMillilitres);
        Assert.Equal(75_000L * RelayBill, report.DrawnMillijoules);
        Assert.True(twin.Tick.PowerExact);

        // The cell is spent: both stall at the next boundary, with water and room still there.
        Assert.Equal(2, twin.Boundary(step: false).Unpowered);
        Assert.True(twin.Tick.PowerExact);
        twin.AssertIdentity();
    }

    [Fact]
    public void ASwapStowsTheOutgoingPacksChargeAndTheIdentityHoldsAcrossIt()
    {
        // M10c-3, decision 4, amended 2026-09-18: what the outgoing pack held leaves the pool as
        // `stowed` — it goes back into the inventory with the pack (a count with no charge until
        // Matthew's "keep its charge as an item") — a term and not a tolerance. The identity has
        // to hold across any number of swaps, the tool drawing on each, or a swap would be the
        // one door the pool leaks through.
        var twin = new Twin();
        var first = new EquippedPack(PackMaterial, Materials.Machine(PackMaterial)!, chargeMillijoules: 1_000, channel: 0);
        Assert.Equal(0L, twin.Tick.EquipPack(first));
        twin.Place(Twin.RimOutlet, Panel);

        // The panel charges it; the tool takes some; then the swap.
        twin.Boundary(step: false);
        Assert.Equal(400L, first.Take(400));
        long held = first.ChargeMillijoules;
        Assert.Equal(1_000 + PanelMillijoulesPerBoundary - 400, held);
        Assert.True(twin.Tick.PowerExact);

        var second = new EquippedPack(PackMaterial, Materials.Machine(PackMaterial)!, chargeMillijoules: 0, channel: 0);
        Assert.Equal(held, twin.Tick.EquipPack(second));
        Assert.Equal(held, twin.Tick.PackStowedMillijoules);
        Assert.Equal(1L, twin.Tick.PacksUnequipped);
        Assert.Same(second, twin.Tick.Pack);
        Assert.True(twin.Tick.PowerExact);

        // The second pack charges from the same panel and the identity still holds; so does
        // unequipping to nothing, which stows the second's charge too.
        twin.Boundary(step: false);
        Assert.Equal(PanelMillijoulesPerBoundary, second.ChargeMillijoules);
        Assert.Equal(200L, second.Take(200));
        Assert.True(twin.Tick.PowerExact);

        Assert.Equal(PanelMillijoulesPerBoundary - 200, twin.Tick.EquipPack(null));
        Assert.Null(twin.Tick.Pack);
        Assert.Equal(held + PanelMillijoulesPerBoundary - 200, twin.Tick.PackStowedMillijoules);
        Assert.Equal(1_000L, twin.Tick.PackInitialMillijoules);
        Assert.Equal(600L, twin.Tick.PackTakenMillijoules);
        Assert.True(twin.Tick.PowerExact);
        twin.AssertIdentity();

        // With nothing equipped a boundary clamps the panel's whole output again.
        Assert.Equal(PanelMillijoulesPerBoundary, twin.Boundary(step: false).ClampedMillijoules);
        Assert.True(twin.Tick.PowerExact);

        // A stowed pack equipped again comes back through `initial` with what it was stowed
        // holding — the round trip is two terms that cancel, and the identity reads exact with
        // the charge in the pool again rather than counted twice or lost.
        var again = new EquippedPack(PackMaterial, Materials.Machine(PackMaterial)!, chargeMillijoules: held, channel: 0);
        Assert.Equal(0L, twin.Tick.EquipPack(again));
        Assert.Equal(1_000L + held, twin.Tick.PackInitialMillijoules);
        Assert.True(twin.Tick.PowerExact);
        twin.Boundary(step: false);
        Assert.Equal(held + PanelMillijoulesPerBoundary, again.ChargeMillijoules);
        Assert.True(twin.Tick.PowerExact);
        twin.AssertIdentity();
    }

    [Fact]
    public void ThePackRechargesLastAtItsBoundAndNeverSupplies()
    {
        var twin = new Twin();
        var pack = new EquippedPack(PackMaterial, Materials.Machine(PackMaterial)!, chargeMillijoules: 0, channel: 0);
        twin.Tick.EquipPack(pack);

        // A panel and no cell: the pack takes the whole 250,000 and nothing is clamped.
        twin.Place(Twin.RimOutlet, Panel);
        MachineTickReport report = twin.Boundary(step: false);
        Assert.Equal(PanelMillijoulesPerBoundary, report.PackGivenMillijoules);
        Assert.Equal(PanelMillijoulesPerBoundary, pack.ChargeMillijoules);
        Assert.Equal(0L, report.ClampedMillijoules);

        // A full cell beside it: the pack takes its bound, 500,000 a boundary, off the cell.
        MachineRow cell = twin.Place(Twin.RimOutletB, BigCell, charge: BigCellCapacity);
        report = twin.Boundary(step: false);
        Assert.Equal(PackRechargePerBoundary, report.PackGivenMillijoules);
        Assert.Equal(BigCellCapacity + PanelMillijoulesPerBoundary - PackRechargePerBoundary, ChargeOf(cell));
        Assert.True(twin.Tick.PowerExact);

        // Last: a billed pump on the channel is paid before the pack sees anything.
        var starved = new Twin();
        var full = new EquippedPack(PackMaterial, Materials.Machine(PackMaterial)!, PackCapacity, channel: 0);
        starved.Tick.EquipPack(full);
        MachineRow pump = starved.Place(Twin.PumpCell, BilledPump);
        starved.Place(Twin.RimOutlet, Outlet);
        starved.PourIntoRoom(120 * Cap);
        starved.Fluid.Settle(200);

        // The pack holds 3.6 MJ and the pump wants 50 kJ: it stalls, because a backpack is not a
        // battery bank.
        MachineTickReport stalled = starved.Boundary(step: false);
        Assert.Equal(PumpStall.Unpowered, pump.LastStall);
        Assert.Equal(PackCapacity, full.ChargeMillijoules);
        Assert.Equal(0L, stalled.PackGivenMillijoules);
        Assert.True(starved.Tick.PowerExact);

        // And with a panel of its own, the pump is paid first and the pack gets the rest.
        starved.Place(Twin.RimOutletB, Panel);
        starved.Place(Twin.RoomOutlet, BigCell, charge: 60_000L * RelayBill);
        MachineTickReport paid = starved.Boundary(step: false);
        Assert.Equal(RelayMillilitresPerBoundary * RelayBill, paid.DrawnMillijoules);
        Assert.Equal(0L, paid.PackGivenMillijoules);
        Assert.True(starved.Tick.PowerExact);
    }

    [Fact]
    public void ALakePumpUnderALitreOfJoulesStallsUnpoweredAndWithOneMovesExactlyOne()
    {
        // Whole litres cross a tier, so a lake pump's affordable offer is floored to litres: with
        // 999 mL worth of joules it stalls unpowered rather than reading no room off a take that
        // rounded to nothing at the outlet.
        var twin = new Twin();
        MachineRow pump = twin.Place(Twin.BedPump, BilledPump);
        twin.Place(Twin.RoomOutlet, Outlet);
        MachineRow cell = twin.Place(Twin.RoofedOutlet, BigCell, charge: 999L * RelayBill);
        twin.Lake(110_000);

        MachineTickReport report = twin.Boundary(step: false);
        Assert.Equal(PumpStall.Unpowered, pump.LastStall);
        Assert.Equal(0, report.NoRoom);
        Assert.Equal(999L * RelayBill, ChargeOf(cell));

        // One litre's worth more: exactly one litre crosses and the cell keeps the 999 mL's.
        twin.Place(Twin.RimOutletB, Panel, open: 4);
        twin.Boundary(step: false);
        Assert.Equal(PumpStall.None, pump.LastStall);
        Assert.Equal(Litre, pump.LastMovedMillilitres);
        Assert.Equal(1L, twin.Tick.PumpedLitres);
        Assert.Equal((999L * RelayBill) + 62_500 - (Litre * RelayBill), ChargeOf(cell));
        Assert.True(twin.Tick.PowerExact);
        twin.AssertIdentity();
    }

    [Fact]
    public void PowerOffIsTheFreeArmAndMovesNoJoule()
    {
        var twin = new Twin();
        twin.Tick.PowerOff = true;
        MachineRow pump = twin.Place(Twin.PumpCell, BilledPump);
        twin.Place(Twin.RimOutlet, Outlet);
        twin.Place(Twin.RimOutletB, Panel);
        MachineRow cell = twin.Place(Twin.RoomOutlet, BigCell, charge: 12_345);
        twin.PourIntoRoom(120 * Cap);
        twin.Fluid.Settle(200);

        MachineTickReport report = twin.Boundary(step: false);

        Assert.Equal(RelayMillilitresPerBoundary, pump.LastMovedMillilitres);
        Assert.Equal(0L, report.GeneratedMillijoules);
        Assert.Equal(0L, report.DrawnMillijoules);
        Assert.Equal(0L, report.ClampedMillijoules);
        Assert.Equal(12_345L, ChargeOf(cell));
        Assert.True(twin.Tick.PowerExact);
        twin.AssertIdentity();
    }

    [Fact]
    public void AChargeRestoredAndRepouredTicksFromWhereItStood()
    {
        var twin = new Twin();
        twin.Place(Twin.PumpCell, BilledPump);
        twin.Place(Twin.RimOutlet, Outlet);
        twin.Place(Twin.RimOutletB, Panel);
        twin.Place(Twin.RoomOutlet, BigCell, charge: 120_000L * RelayBill);
        twin.PourIntoRoom(120 * Cap);
        twin.Fluid.Settle(200);
        twin.Boundary(step: false);
        long before = twin.Tick.StoredMillijoules;
        Assert.Equal((120_000L - RelayMillilitresPerBoundary) * RelayBill + PanelMillijoulesPerBoundary, before);

        twin.ReopenRows();

        Assert.Equal(before, twin.Tick.StoredMillijoules);
        Assert.True(twin.Tick.PowerExact);
        MachineTickReport report = twin.Boundary(step: false);
        Assert.Equal(1, report.Moved);
        Assert.Equal(RelayMillilitresPerBoundary * RelayBill, report.DrawnMillijoules);
        Assert.True(twin.Tick.PowerExact);
    }

    [Fact]
    public void ThePoolIdentityHoldsOverAThousandBoundariesWithEverythingRunning()
    {
        var twin = new Twin();
        // The pack starts full, or it would drink every boundary's generation for nine thousand
        // boundaries and the cells would never see a millijoule to overflow with.
        var pack = new EquippedPack(PackMaterial, Materials.Machine(PackMaterial)!, PackCapacity, channel: 0);
        twin.Tick.EquipPack(pack);
        twin.Place(Twin.PumpCell, BilledPump);
        twin.Place(Twin.RimOutlet, Outlet);
        twin.Place(Twin.RimOutletB, Panel);
        twin.Place(new BrushCell(12, 10, 9), Panel, open: 9);
        twin.Place(Twin.RoomOutlet, SmallCell);
        twin.Place(Twin.RoofedOutlet, SmallCell, charge: SmallCellCapacity);
        twin.PourIntoRoom(120 * Cap);
        twin.Fluid.Settle(200);

        // Four phases, so every term moves: the pump running on the panels (a trickle, since it
        // takes all it can pay for), the room dry so the cells fill and overflow, the sky roofed
        // over a re-poured room so the cells run dry and the bill is the constraint, and the sun
        // back.
        long moves = 0, unpowered = 0;
        for (int boundary = 0; boundary < 1000; boundary++)
        {
            if (boundary % 50 == 25)
            {
                // The tool draws on the pack between boundaries; the identity carries it.
                pack.Take(300_000);
            }

            if (boundary % 100 == 99)
            {
                twin.Tick.OvercastByte = (byte)(boundary % 256);
            }

            if (boundary == 200) twin.DrainRoom();
            if (boundary == 350) twin.PourIntoRoom(120 * Cap);

            int dark = boundary is >= 350 and < 450 ? 0 : 16;
            twin.Sky.Open[Twin.RimOutletB] = dark;
            twin.Sky.Open[new BrushCell(12, 10, 9)] = dark == 0 ? 0 : 9;

            MachineTickReport report = twin.Boundary(step: (boundary % 7) == 0);
            moves += report.Moved;
            unpowered += report.Unpowered;
            Assert.True(twin.Tick.PowerExact, $"boundary {boundary}: the pool identity broke");
            Assert.True(report.Exact, $"boundary {boundary}: pumped != emitted");
        }

        twin.AssertIdentity();
        Assert.True(moves > 0);
        Assert.True(unpowered > 0, "the fixture never ran the cell dry, so the bill was never the constraint");
        Assert.True(twin.Tick.ClampedMillijoules > 0, "the small cell never overflowed");
        output.WriteLine(
            $"1,000 boundaries: {moves} move(s), {unpowered} unpowered, generated {twin.Tick.GeneratedMillijoules:N0} mJ, "
            + $"drawn {twin.Tick.DrawnByPumpsMillijoules:N0}, clamped {twin.Tick.ClampedMillijoules:N0}, pack given {twin.Tick.PackGivenMillijoules:N0}, "
            + $"stored {twin.Tick.StoredMillijoules:N0} + pack {pack.ChargeMillijoules:N0}");
    }

    // ---------------------------------------------------------------- B -> B

    [Fact]
    public void APumpMovesItsBudgetFromItsBoxToTheLayerAboveTheOutletInOneBoundary()
    {
        var twin = new Twin();
        twin.Place(Twin.PumpCell, Pump);
        twin.Place(Twin.RimOutlet, Outlet);
        long poured = twin.PourIntoRoom(40 * Cap);
        long before = twin.InBoxOf(Twin.PumpCell);
        Assert.True(before >= RelayMillilitresPerBoundary, $"the box holds {before} mL, under one budget");

        MachineTickReport report = twin.Boundary(step: false);

        Assert.True(report.Exact, report.ToString());
        Assert.Equal(1, report.Moved);
        Assert.Equal(RelayMillilitresPerBoundary, report.PumpedMillilitres);
        Assert.Equal(RelayMillilitresPerBoundary, report.EmittedMillilitres);
        Assert.Equal(0L, report.PumpedLitres + report.EmittedLitres);
        Assert.Equal(before - RelayMillilitresPerBoundary, twin.InBoxOf(Twin.PumpCell));
        Assert.Equal(RelayMillilitresPerBoundary, twin.OverMouthOf(Twin.RimOutlet));
        Assert.Equal(poured, twin.Fluid.TotalMillilitres);
        twin.AssertIdentity();
    }

    [Fact]
    public void ThePumpDrainsTheNearestCellsFirst()
    {
        var twin = new Twin();
        twin.Place(Twin.PumpCell, Pump);
        twin.Place(Twin.RimOutlet, Outlet);
        twin.PourIntoRoom(120 * Cap);
        twin.Fluid.Settle(200);

        // The six face cells' voxels at the floor hold water; a budget of 3.2 cells comes out of
        // them before any edge cell is touched.
        long facesBefore = 0, edgesBefore = 0;
        ReadOnlySpan<(int Dx, int Dy, int Dz)> cells = MachineTick.IntakeCellsAt(1);
        for (int i = 0; i < cells.Length; i++)
        {
            long held = HeldIn(twin, Twin.PumpCell.Offset(cells[i].Dx, cells[i].Dy, cells[i].Dz));
            if (i < 6) facesBefore += held; else edgesBefore += held;
        }

        Assert.True(facesBefore >= RelayMillilitresPerBoundary, $"the faces hold {facesBefore} mL");
        twin.Boundary(step: false);

        long facesAfter = 0, edgesAfter = 0;
        for (int i = 0; i < cells.Length; i++)
        {
            long held = HeldIn(twin, Twin.PumpCell.Offset(cells[i].Dx, cells[i].Dy, cells[i].Dz));
            if (i < 6) facesAfter += held; else edgesAfter += held;
        }

        Assert.Equal(facesBefore - RelayMillilitresPerBoundary, facesAfter);
        Assert.Equal(edgesBefore, edgesAfter);
    }

    private static long HeldIn(Twin twin, BrushCell cell)
    {
        long held = 0;
        for (int lz = 0; lz < BrushCell.Voxels; lz++)
        for (int lx = 0; lx < BrushCell.Voxels; lx++)
        for (int ly = 0; ly < BrushCell.Voxels; ly++)
        {
            held += twin.Fluid.Grid.AmountAt(cell.VoxelAt(lx, ly, lz));
        }

        return held;
    }

    [Fact]
    public void ALoopInsideTheRoomKeepsTheWorldsTotalConstantOverTenThousandBoundaries()
    {
        // The outlet's mouth is in the room, so what it pours falls back toward the pump: the
        // player's loop, not a rule's defect, and the machine layer's identity is that the world's
        // total never moves by a millilitre however long it runs.
        var twin = new Twin();
        twin.Place(Twin.PumpCell, Pump);
        twin.Place(Twin.RoomOutlet, Outlet);
        long poured = twin.PourIntoRoom(200 * Cap);

        long moved = 0;
        for (int boundary = 0; boundary < 10_000; boundary++)
        {
            MachineTickReport report = twin.Boundary();
            Assert.True(report.Exact, $"boundary {boundary}: {report}");
            Assert.Equal(poured, twin.Fluid.TotalMillilitres);
            moved += report.PumpedMillilitres;
            if (boundary % 500 == 0)
            {
                twin.AssertIdentity();
            }
        }

        twin.AssertIdentity();
        Assert.Equal(0L, twin.Fluid.DisplacedMillilitres);
        Assert.True(moved > 1_000 * RelayMillilitresPerBoundary, $"only {moved} mL moved in 10,000 boundaries");
        output.WriteLine($"loop: {moved / Litre:N0} L moved over 10,000 boundaries, {twin.Fluid.CellVisits:N0} visits, {twin.Tick.IntakeVoxelsRead:N0} intake voxels read");
    }

    // ---------------------------------------------------------------- stalls

    [Fact]
    public void APumpWithNoOutletOnItsChannelStallsAndReadsNothing()
    {
        var twin = new Twin();
        twin.Place(Twin.PumpCell, Pump);
        twin.PourIntoRoom(40 * Cap);

        MachineTickReport report = twin.Boundary(step: false);

        Assert.Equal(1, report.NoOutlet);
        Assert.Equal(0, report.Moved);
        Assert.Equal(0L, twin.Tick.IntakeVoxelsRead);
        Assert.Equal(0L, twin.Fluid.PumpedMillilitres);
    }

    [Fact]
    public void APumpOnChannelOneIgnoresChannelZerosOutlets()
    {
        var twin = new Twin();
        twin.Place(Twin.PumpCell, Pump, channel: 1);
        twin.Place(Twin.RimOutlet, Outlet, channel: 0);
        twin.PourIntoRoom(40 * Cap);

        MachineTickReport report = twin.Boundary(step: false);
        Assert.Equal(1, report.NoOutlet);
        Assert.Equal(0L, twin.Fluid.PumpedMillilitres);

        twin.Place(Twin.RimOutletB, Outlet, channel: 1);
        report = twin.Boundary(step: false);
        Assert.Equal(1, report.Moved);
        Assert.Equal(RelayMillilitresPerBoundary, twin.OverMouthOf(Twin.RimOutletB));
        Assert.Equal(0L, twin.OverMouthOf(Twin.RimOutlet));
    }

    [Fact]
    public void ADryBoxStallsNoWaterOffThePanelCountsWithoutReadingACell()
    {
        var twin = new Twin();
        twin.Place(Twin.PumpCell, Pump);
        twin.Place(Twin.RimOutlet, Outlet);

        MachineTickReport report = twin.Boundary(step: false);

        Assert.Equal(1, report.NoWater);
        Assert.Equal(1L, twin.Tick.IntakesSkippedDry);
        Assert.Equal(0L, twin.Tick.IntakeVoxelsRead);

        // Water in the room but none in the box: the panel count says yes and the cells say no.
        twin.PourIntoRoom(4 * Cap, xMin: Twin.RoomMax - 3, xMax: Twin.RoomMax, zMin: Twin.RoomMax - 3, zMax: Twin.RoomMax);
        report = twin.Boundary(step: false);
        Assert.Equal(1, report.NoWater);
        Assert.Equal(1L, twin.Tick.IntakesSkippedDry);
        Assert.Equal(26L * Cell64, twin.Tick.IntakeVoxelsRead);
    }

    [Fact]
    public void AnOutletUnderARoofHasNoRoomAndThePumpStallsNoRoom()
    {
        var twin = new Twin();
        twin.Place(Twin.PumpCell, Pump);
        twin.Place(Twin.RoofedOutlet, Outlet);
        Assert.False(twin.Bounded.CanHold(Twin.RoofedOutlet.VoxelAt(0, BrushCell.Voxels, 0)), "the roofed outlet's mouth is meant to be rock");
        twin.PourIntoRoom(40 * Cap);

        MachineTickReport report = twin.Boundary(step: false);

        Assert.Equal(1, report.NoRoom);
        Assert.Equal(0L, twin.Fluid.PumpedMillilitres);
        Assert.Equal(0L, twin.Fluid.EmittedMillilitres);
    }

    [Fact]
    public void ABoxStraddlingAnUnloadedChunkStallsWholeThoughItsOwnChunkHoldsWater()
    {
        // The pump stands at the room's west wall; its box reaches into chunk X 0, which is away.
        // An unloaded panel would read dry — the conditional no — so nothing is read at all.
        var twin = new Twin();
        var edge = new BrushCell(8, 2, 10);
        twin.Place(edge, Pump);
        twin.Place(Twin.RimOutlet, Outlet);
        twin.PourIntoRoom(40 * Cap);
        Assert.True(twin.InBoxOf(edge) > 0);
        Assert.Equal(Twin.RoomChunk, edge.OriginVoxel.Chunk);
        Assert.Equal(Twin.WestChunk, edge.Offset(-1, 0, 0).OriginVoxel.Chunk);
        twin.Unload(Twin.WestChunk);

        MachineTickReport report = twin.Boundary(step: false);

        Assert.Equal(1, report.Unloaded);
        Assert.Equal(0L, twin.Tick.IntakeVoxelsRead);
        Assert.Equal(0L, twin.Fluid.PumpedMillilitres);

        twin.Load(Twin.WestChunk);
        report = twin.Boundary(step: false);
        Assert.Equal(1, report.Moved);
    }

    // ---------------------------------------------------------------- the cursor

    // ---------------------------------------------------------------- M10b: SetPort and the weighted ring

    [Fact]
    public void AnOutletMovedOffTheChannelBySetPortLeavesThePumpNoOutletAtTheNextBoundary()
    {
        // Fact 4 of M10b's pass: the tick reads channels at rebuild and rebuilds on Version, so
        // SetPort has to move it — the pump stalls at the very next boundary, not one later.
        var twin = new Twin();
        twin.Place(Twin.PumpCell, Pump);
        twin.Place(Twin.RimOutlet, Outlet);
        twin.PourIntoRoom(120 * Cap);
        twin.Fluid.Settle(200);
        Assert.Equal(1, twin.Boundary(step: false).Moved);

        PortWrite write = twin.Machines.SetPort(Twin.RimOutlet, "channel", 3);
        Assert.Equal(PortWriteResult.Set, write.Result);

        MachineTickReport report = twin.Boundary(step: false);
        Assert.Equal(1, report.NoOutlet);
        Assert.Equal(0, report.Moved);
        Assert.Empty(twin.Tick.RingOn(0));
        Assert.Single(twin.Tick.RingOn(3));

        // And a pump moved onto the same channel finds it there.
        twin.Machines.SetPort(Twin.PumpCell, "channel", 3);
        Assert.Equal(1, twin.Boundary(step: false).Moved);
    }

    [Fact]
    public void NearColumnsAreEveryPumpsBoxAndEveryOutletsMouthAndNothingElse()
    {
        // The near field's second kind of viewer (2026-09-19). The pump in the room's middle at
        // radius 1 is column (1, 1) alone; the west room's outlet is column (0, 1) alone, so a
        // list that forgot outlets would be one column short; a panel and a cell stand in column
        // (2, 1), which a list that took every row would name and this one must not.
        var twin = new Twin();
        twin.Place(Twin.PumpCell, Pump);
        twin.Place(Twin.WestOutlet, Outlet);
        twin.Place(new BrushCell(17, 10, 9), Panel);
        twin.Place(new BrushCell(18, 10, 9), BigCell);

        var columns = new HashSet<(int X, int Z)>(twin.Tick.NearColumns);

        Assert.Equal(new HashSet<(int, int)> { (1, 1), (0, 1) }, columns);
    }

    [Fact]
    public void NearColumnsFollowTheIntakeRadiusAndTheSet()
    {
        // A wide pump one cell east of the room's west wall reaches two cells west at radius 2 —
        // across the chunk's face into column X 0 — where a radius-1 box would stay inside
        // column 1; and a dismantle takes a column out at the next read, because the list is
        // rebuilt with the rings.
        var twin = new Twin();
        var nearTheWall = new BrushCell(9, 2, 10);
        twin.Place(nearTheWall, WidePump);
        Assert.Equal(new HashSet<(int, int)> { (0, 1), (1, 1) }, new HashSet<(int X, int Z)>(twin.Tick.NearColumns));

        MachineRow west = twin.Place(Twin.WestOutlet, Outlet);
        Assert.Equal(new HashSet<(int, int)> { (0, 1), (1, 1) }, new HashSet<(int X, int Z)>(twin.Tick.NearColumns));

        Assert.True(twin.Machines.Remove(nearTheWall));
        Assert.Equal(new HashSet<(int, int)> { (0, 1) }, new HashSet<(int X, int Z)>(twin.Tick.NearColumns));

        Assert.True(twin.Machines.Remove(west.Cell));
        Assert.Empty(twin.Tick.NearColumns);
    }

    [Fact]
    public void WeightsAreTheSmoothInterleave()
    {
        // Weights 5 : 1 : 1 read A A B A C A A — the two lighter outlets spread through the ring
        // rather than bunched at its end, which is what the smooth weighted round-robin is for.
        var twin = new Twin();
        MachineRow a = twin.Place(Twin.RimOutlet, Outlet, weight: 5);
        MachineRow b = twin.Place(Twin.RimOutletB, Outlet, weight: 1);
        MachineRow c = twin.Place(new BrushCell(12, 10, 9), Outlet, weight: 1);

        IReadOnlyList<MachineRow> ring = twin.Tick.RingOn(0);

        Assert.Equal(new[] { a, a, b, a, c, a, a }, ring);
    }

    [Fact]
    public void WeightsThreeToOneDeliverThreeBoundariesToOnePerFour()
    {
        var twin = new Twin();
        twin.Place(Twin.PumpCell, Pump);
        twin.Place(Twin.RimOutlet, Outlet, weight: 3);
        twin.Place(Twin.RimOutletB, Outlet, weight: 1);
        twin.PourIntoRoom(200 * Cap);
        twin.Fluid.Settle(200);

        // The mouths are emptied between boundaries — a layer holds five boundaries' worth, and
        // a full layer would send the sixth to the other outlet for room rather than for weight.
        int toA = 0, toB = 0;
        var order = new List<char>();
        for (int boundary = 0; boundary < 8; boundary++)
        {
            Assert.Equal(1, twin.Boundary(step: false).Moved);
            long a = twin.OverMouthOf(Twin.RimOutlet), b = twin.OverMouthOf(Twin.RimOutletB);
            Assert.True((a == RelayMillilitresPerBoundary) != (b == RelayMillilitresPerBoundary), $"boundary {boundary} split the offer: {a} / {b}");
            if (a > 0) { toA++; order.Add('A'); } else { toB++; order.Add('B'); }
            foreach (BrushCell outlet in new[] { Twin.RimOutlet, Twin.RimOutletB })
            for (int lz = 0; lz < BrushCell.Voxels; lz++)
            for (int lx = 0; lx < BrushCell.Voxels; lx++)
            {
                twin.Fluid.Remove(outlet.VoxelAt(lx, BrushCell.Voxels, lz), Cap);
            }
        }

        Assert.Equal(6, toA);
        Assert.Equal(2, toB);
        Assert.Equal("AABAAABA", string.Concat(order));
    }

    [Fact]
    public void WeightsAllOneAreThePlainRingEntryForEntry()
    {
        // The round-robin is the all-equal case: the ring is the outlets in origin order, once
        // each, which is what every a-3 test ran on.
        var twin = new Twin();
        MachineRow b = twin.Place(Twin.RimOutletB, Outlet);
        MachineRow a = twin.Place(Twin.RimOutlet, Outlet);
        MachineRow room = twin.Place(Twin.RoomOutlet, Outlet);

        Assert.Equal(new[] { a, b, room }, twin.Tick.RingOn(0));
    }

    [Fact]
    public void AWeightOfAHundredCostsOneRoomReadPerOutletPerPump()
    {
        // The ring lists a heavy outlet a hundred times; a pump walking it reads its room once.
        var twin = new Twin();
        twin.Place(Twin.PumpCell, Pump);
        twin.Place(Twin.RimOutlet, Outlet, weight: 100);
        twin.Place(Twin.RimOutletB, Outlet, weight: 1);
        twin.PourIntoRoom(RelayMillilitresPerBoundary, xMin: 36, xMax: 39);
        Assert.Equal(RelayMillilitresPerBoundary, twin.InBoxOf(Twin.PumpCell));
        Assert.Equal(101, twin.Tick.RingOn(0).Count);

        // The offer is exactly one budget and the first outlet takes it all: one room read.
        twin.Boundary(step: false);
        Assert.Equal(1L, twin.Tick.OutletRoomReads);

        // The box is empty now: the walk never starts, so no read at all.
        Assert.Equal(1, twin.Boundary(step: false).NoWater);
        Assert.Equal(1L, twin.Tick.OutletRoomReads);

        // A roofed heavy outlet is read once and passed over, the light one read once and used.
        var roofed = new Twin();
        roofed.Place(Twin.PumpCell, Pump);
        roofed.Place(Twin.RoofedOutlet, Outlet, weight: 100);
        roofed.Place(Twin.RimOutlet, Outlet, weight: 1);
        roofed.PourIntoRoom(40 * Cap);
        MachineTickReport report = roofed.Boundary(step: false);
        Assert.Equal(1, report.Moved);
        Assert.Equal(2L, roofed.Tick.OutletRoomReads);
        Assert.Equal(RelayMillilitresPerBoundary, roofed.OverMouthOf(Twin.RimOutlet));
    }

    [Fact]
    public void ARowReadsBackTheStallAndTheMoveTheReportCounted()
    {
        // What the panel shows beside the number the player typed (M10b, decision 6).
        var twin = new Twin();
        MachineRow pump = twin.Place(Twin.PumpCell, Pump);
        Assert.Equal(PumpStall.None, pump.LastStall);

        twin.Boundary(step: false);
        Assert.Equal(PumpStall.NoOutlet, pump.LastStall);
        Assert.Equal(0L, pump.LastMovedMillilitres);

        twin.Place(Twin.RimOutlet, Outlet);
        twin.Boundary(step: false);
        Assert.Equal(PumpStall.NoWater, pump.LastStall);

        twin.PourIntoRoom(40 * Cap);
        twin.Boundary(step: false);
        Assert.Equal(PumpStall.None, pump.LastStall);
        Assert.Equal(RelayMillilitresPerBoundary, pump.LastMovedMillilitres);

        // A lake pump reads its litres back in millilitres, the same unit as the box's.
        var lake = new Twin();
        MachineRow bed = lake.Place(Twin.BedPump, Pump);
        lake.Place(Twin.RoomOutlet, Outlet);
        lake.Lake(110_000);
        lake.Boundary(step: false);
        Assert.Equal(RelayMillilitresPerBoundary, bed.LastMovedMillilitres);
    }

    [Fact]
    public void APortWrittenSnapshottedAndRestoredTicksOnItsNewChannel()
    {
        // The quit oracle (M10b, decision 7): a port write is on the row, the row is the table,
        // and the tick after the reopen reads the ring the write made.
        var twin = new Twin();
        twin.Place(Twin.PumpCell, Pump);
        twin.Place(Twin.RimOutlet, Outlet);
        twin.Place(Twin.RimOutletB, Outlet);
        twin.PourIntoRoom(120 * Cap);
        twin.Fluid.Settle(200);
        Assert.Equal(PortWriteResult.Set, twin.Machines.SetPort(Twin.PumpCell, "channel", 5).Result);
        Assert.Equal(PortWriteResult.Set, twin.Machines.SetPort(Twin.RimOutletB, "channel", 5).Result);
        Assert.Equal(PortWriteResult.Clamped, twin.Machines.SetPort(Twin.RimOutletB, "weight", 1_000).Result);

        twin.ReopenRows();

        Assert.True(twin.Machines.TryGet(Twin.RimOutletB, out MachineRow restored));
        Assert.Equal(5L, restored.Ports["channel"]);
        Assert.Equal(100L, restored.Ports["weight"]);
        MachineTickReport report = twin.Boundary(step: false);
        Assert.Equal(1, report.Moved);
        Assert.Equal(RelayMillilitresPerBoundary, twin.OverMouthOf(Twin.RimOutletB));
        Assert.Equal(0L, twin.OverMouthOf(Twin.RimOutlet));
        Assert.Single(twin.Tick.RingOn(0));
        Assert.Equal(100, twin.Tick.RingOn(5).Count);
    }

    [Fact]
    public void TwoOutletsOnOneChannelTakeAlternateBoundaries()
    {
        var twin = new Twin();
        twin.Place(Twin.PumpCell, Pump);
        twin.Place(Twin.RimOutlet, Outlet);
        twin.Place(Twin.RimOutletB, Outlet);
        twin.PourIntoRoom(120 * Cap);
        twin.Fluid.Settle(200);

        Assert.Equal(0, twin.Tick.CursorOn(0));
        twin.Boundary(step: false);
        Assert.Equal(RelayMillilitresPerBoundary, twin.OverMouthOf(Twin.RimOutlet));
        Assert.Equal(0L, twin.OverMouthOf(Twin.RimOutletB));
        Assert.Equal(1, twin.Tick.CursorOn(0));

        twin.Boundary(step: false);
        Assert.Equal(RelayMillilitresPerBoundary, twin.OverMouthOf(Twin.RimOutlet));
        Assert.Equal(RelayMillilitresPerBoundary, twin.OverMouthOf(Twin.RimOutletB));
        Assert.Equal(0, twin.Tick.CursorOn(0));

        twin.Boundary(step: false);
        Assert.Equal(2 * RelayMillilitresPerBoundary, twin.OverMouthOf(Twin.RimOutlet));
        Assert.Equal(RelayMillilitresPerBoundary, twin.OverMouthOf(Twin.RimOutletB));
    }

    [Fact]
    public void TwoPumpsOnOneChannelReachTwoOutletsInOneBoundary()
    {
        var twin = new Twin();
        twin.Place(Twin.PumpCell, Pump);
        twin.Place(new BrushCell(12, 2, 10), Pump);
        twin.Place(Twin.RimOutlet, Outlet);
        twin.Place(Twin.RimOutletB, Outlet);
        twin.PourIntoRoom(160 * Cap);
        twin.Fluid.Settle(200);

        MachineTickReport report = twin.Boundary(step: false);

        Assert.Equal(2, report.Moved);
        Assert.Equal(RelayMillilitresPerBoundary, twin.OverMouthOf(Twin.RimOutlet));
        Assert.Equal(RelayMillilitresPerBoundary, twin.OverMouthOf(Twin.RimOutletB));
        Assert.Equal(2 * RelayMillilitresPerBoundary, report.PumpedMillilitres);
        Assert.True(report.Exact);
    }

    [Fact]
    public void AnOutletWithoutRoomForTheWholeOfferPassesTheRemainderToTheNext()
    {
        // The first outlet's layer is nearly full: 16 cells at Cap less one litre of room. The
        // pump fills that litre and the next outlet takes the rest, in the same boundary.
        var twin = new Twin();
        twin.Place(Twin.PumpCell, Pump);
        twin.Place(Twin.RimOutlet, Outlet);
        twin.Place(Twin.RimOutletB, Outlet);
        twin.PourIntoRoom(120 * Cap);
        twin.Fluid.Settle(200);
        for (int lz = 0; lz < BrushCell.Voxels; lz++)
        for (int lx = 0; lx < BrushCell.Voxels; lx++)
        {
            VoxelPos v = Twin.RimOutlet.VoxelAt(lx, BrushCell.Voxels, lz);
            Assert.Equal(0L, twin.Fluid.Add(v, lx == 0 && lz == 0 ? Cap - Litre : Cap));
        }

        long overBefore = twin.OverMouthOf(Twin.RimOutlet);
        MachineTickReport report = twin.Boundary(step: false);

        Assert.Equal(1, report.Moved);
        Assert.Equal(RelayMillilitresPerBoundary, report.PumpedMillilitres);
        Assert.Equal(overBefore + Litre, twin.OverMouthOf(Twin.RimOutlet));
        Assert.Equal(RelayMillilitresPerBoundary - Litre, twin.OverMouthOf(Twin.RimOutletB));
        Assert.Equal(1, twin.Tick.CursorOn(0));
    }

    // ---------------------------------------------------------------- A -> B

    [Fact]
    public void APumpWhoseMouthStandsInTheLakeTakesWholeLitresFromTierAAndTheDrainsEqualityStillHolds()
    {
        var twin = new Twin();
        twin.Place(Twin.BedPump, Pump);
        twin.Place(Twin.RoomOutlet, Outlet);

        // A portal from the bed into the room, so the drain draws beside the pump and the two
        // terms can be told apart: drawn is the portal's, pumped is the pump's, each from both ends.
        for (int y = Twin.RoomTop + 1; y <= Floor; y++) twin.Dig(new VoxelPos(50, y, 50));
        Assert.True(twin.Index.Count > 0, "the shaft into the room should have filed a portal");

        twin.Lake(110_000);
        VoxelPos mouth = Twin.BedPump.Offset(0, 1, 0).OriginVoxel;
        Assert.True(twin.Bounded.InLake(mouth), $"the mouth at {mouth} is meant to be in the lake (level {twin.Water.LevelAt(twin.Basin):F2})");
        long lakeBefore = twin.Water.StoredLitres;

        MachineTickReport report = twin.Boundary(step: false);

        Assert.Equal(1, report.Moved);
        Assert.Equal(0L, report.PumpedMillilitres);
        Assert.Equal(RelayMillilitresPerBoundary / Litre, report.PumpedLitres);
        Assert.Equal(RelayMillilitresPerBoundary, report.EmittedMillilitres);
        Assert.True(report.Exact);
        Assert.Equal(RelayMillilitresPerBoundary, twin.OverMouthOf(Twin.RoomOutlet));
        Assert.True(twin.Water.DrawnLitres > 0, "the portal drew nothing");
        Assert.Equal(lakeBefore - twin.Water.DrawnLitres - twin.Water.PumpedLitres, twin.Water.StoredLitres);
        Assert.Equal(twin.Water.DrawnLitres * Litre, twin.Drain.DrawnMillilitres);
        twin.AssertIdentity();

        for (int i = 0; i < 40; i++)
        {
            twin.Boundary();
            twin.AssertIdentity();
        }

        Assert.True(twin.Water.PumpedLitres > 10 * (RelayMillilitresPerBoundary / Litre));
        Assert.Equal(twin.Water.DrawnLitres * Litre, twin.Drain.DrawnMillilitres);
    }

    [Fact]
    public void ASlowLakePumpDrawsALitreEveryFourthOrFifthBoundaryAndNeverMoreThanItIsOwed()
    {
        // 470 mL/s is 235 mL a boundary: the first litre on the fifth boundary (1,175 owed), the
        // second on the ninth (2,115), and so on — floor(n × 235 / 1,000) after n boundaries.
        var twin = new Twin();
        MachineRow row = twin.Place(Twin.BedPump, SlowPump);
        twin.Place(Twin.RoomOutlet, Outlet);
        twin.Lake(110_000);

        long pumped = 0;
        for (int boundary = 1; boundary <= 40; boundary++)
        {
            MachineTickReport report = twin.Boundary();
            Assert.True(report.PumpedLitres <= 1, $"boundary {boundary} pumped {report.PumpedLitres} L");
            pumped += report.PumpedLitres;
            Assert.Equal(boundary * 235 / Litre, pumped);
            Assert.Equal(boundary * 235 % Litre, row.OwedMillilitres);
            Assert.True(report.Exact);
        }

        Assert.Equal(9L, pumped);
        twin.AssertIdentity();
    }

    [Fact]
    public void ThePitBesideTheLakePumpsNothingThoughTheTableStandsOverIt()
    {
        // Fact 5: the water table is a level, not a footprint. A pit dug into the ramp above the
        // lake's shore has the lake's level over its sample and no lake in it, and M10-PLAN's
        // rule — draw from Tier A where the table stands above the top — would have had this
        // pump drink the lake through the rock at 100 L/s. InLake is the question, and it says no.
        var twin = new Twin();
        twin.DigBox(88, 28, 60, 91, 36, 63);
        var pit = new BrushCell(22, 7, 15);
        twin.Place(pit, Pump);
        twin.Place(Twin.RoomOutlet, Outlet);
        twin.Lake(60_000);

        VoxelPos mouth = pit.Offset(0, 1, 0).OriginVoxel;
        (int sx, int sz) = twin.Water.SampleOf(mouth.X, mouth.Z);
        double? table = twin.Water.TableAt(sx, sz);
        Assert.True(table is not null && table > mouth.Y, $"the premise: the table ({table}) is meant to stand over the mouth at {mouth}");
        Assert.True(twin.Bounded.CanHold(mouth), "the mouth is dug air");
        Assert.False(twin.Bounded.InLake(mouth));

        MachineTickReport report = twin.Boundary(step: false);

        Assert.Equal(0, report.Moved);
        Assert.Equal(1, report.NoWater);
        Assert.Equal(0L, twin.Water.PumpedLitres);
        Assert.Equal(0L, twin.Fluid.PumpedMillilitres);
        twin.AssertIdentity();
    }

    // ---------------------------------------------------------------- B -> A, the unloaded-outlet rule

    [Fact]
    public void AnUnloadedOutletOnTheGroundInsideTheWindowTakesWholeLitresIntoItsBasinWithNothingPending()
    {
        var twin = new Twin();
        twin.Place(Twin.PumpCell, Pump);
        twin.Place(Twin.BedOutlet, Outlet);
        twin.PourIntoRoom(40 * Cap);
        Assert.Equal(Twin.BedOutletChunk, Twin.BedOutlet.Offset(0, 1, 0).OriginVoxel.Chunk);
        twin.Unload(Twin.BedOutletChunk);
        Assert.True(Twin.BedOutlet.OriginVoxel.Y + BrushCell.Voxels > twin.Seed.SurfaceHeightAt(Twin.BedOutlet.OriginVoxel.X, Twin.BedOutlet.OriginVoxel.Z));
        long lakeBefore = twin.Water.StoredLitres;

        MachineTickReport report = twin.Boundary(step: false);

        Assert.Equal(1, report.Moved);
        Assert.Equal(RelayMillilitresPerBoundary, report.PumpedMillilitres);
        Assert.Equal(0L, report.EmittedMillilitres);
        Assert.Equal(RelayMillilitresPerBoundary / Litre, report.EmittedLitres);
        Assert.True(report.Exact);
        Assert.Equal(lakeBefore + (RelayMillilitresPerBoundary / Litre), twin.Water.StoredLitres);
        Assert.Equal(RelayMillilitresPerBoundary / Litre, twin.Tick.EmittedLitresToUnloadedOutlets);
        Assert.Equal(0L, twin.Handoff.PendingMillilitres);
        Assert.Equal(0L, twin.Water.ReceivedLitres);
        twin.AssertIdentity();
    }

    [Fact]
    public void AnUnloadedOutletHalfBuriedInTheGroundStillStandsOnIt()
    {
        // The --pump arm's shape: the cell holding the first air voxel over a surface that is not
        // on the lattice has its origin up to three voxels into the ground and its mouth above it.
        // The rule is the mouth's, so the outlet is Tier A's. The ramp at x 84 is 34: cell Y 8 is
        // 32..35, three voxels of rock and one of air, mouth at 36, and the sample drains to the bowl.
        var twin = new Twin();
        twin.Place(Twin.PumpCell, Pump);
        var buried = new BrushCell(21, 8, 15);
        twin.Place(buried, Outlet);
        twin.PourIntoRoom(40 * Cap);
        twin.Unload(new ChunkPos(2, 1, 1));
        Assert.Equal(34, twin.Seed.SurfaceHeightAt(buried.OriginVoxel.X, buried.OriginVoxel.Z));
        Assert.Equal(32, buried.OriginVoxel.Y);
        long lakeBefore = twin.Water.StoredLitres;

        MachineTickReport report = twin.Boundary(step: false);

        Assert.Equal(1, report.Moved);
        Assert.Equal(RelayMillilitresPerBoundary / Litre, report.EmittedLitres);
        Assert.Equal(lakeBefore + (RelayMillilitresPerBoundary / Litre), twin.Water.StoredLitres);
        twin.AssertIdentity();
    }

    [Fact]
    public void AnUnloadedOutletUndergroundAcceptsNothing()
    {
        var twin = new Twin();
        twin.Place(Twin.PumpCell, Pump);
        twin.Place(Twin.WestOutlet, Outlet);
        twin.PourIntoRoom(40 * Cap);
        twin.Unload(Twin.WestChunk);
        Assert.True(Twin.WestOutlet.OriginVoxel.Y + BrushCell.Voxels <= twin.Seed.SurfaceHeightAt(Twin.WestOutlet.OriginVoxel.X, Twin.WestOutlet.OriginVoxel.Z));

        MachineTickReport report = twin.Boundary(step: false);

        Assert.Equal(0, report.Moved);
        Assert.Equal(1, report.Unloaded);
        Assert.Equal(0L, twin.Fluid.PumpedMillilitres);
        Assert.Equal(0L, twin.Water.EmittedLitres);

        // Loaded again, it is an outlet in a room: Tier B room in the layer above its top.
        twin.Load(Twin.WestChunk);
        report = twin.Boundary(step: false);
        Assert.Equal(1, report.Moved);
        Assert.Equal(RelayMillilitresPerBoundary, twin.OverMouthOf(Twin.WestOutlet));
    }

    [Fact]
    public void AnOutletOffTheWindowStallsUnanalysedWhereBasinAtSampleWouldHaveReadOutlet()
    {
        // Fact 6: BasinAtSample folds "off the window" into Outlet, and an outlet delivered to
        // Outlet leaves the world. Off the window the ground is unanalysed and the pump stalls.
        var twin = new Twin();
        twin.Place(Twin.PumpCell, Pump);
        var west = new BrushCell(-2, 10, 10);
        twin.Place(west, Outlet);
        twin.PourIntoRoom(40 * Cap);
        Assert.False(twin.Terrain.IsLoaded(west.OriginVoxel));
        (int sx, int sz) = twin.Water.SampleOf(west.OriginVoxel.X, west.OriginVoxel.Z);
        Assert.Equal(Watershed.Outlet, twin.Water.BasinAtSample(sx, sz));
        Assert.False(twin.Water.TryBasinAtSample(sx, sz, out _));

        MachineTickReport report = twin.Boundary(step: false);

        Assert.Equal(0, report.Moved);
        Assert.Equal(1, report.Unanalysed);
        Assert.Equal(0L, twin.Fluid.PumpedMillilitres);
        Assert.Equal(0L, twin.Water.EmittedLitres);
        Assert.Equal(0L, twin.Water.SpilledLitres);
    }

    [Fact]
    public void AnUnloadedOutletOnGroundThatDrainsOffTheWindowsEdgeSendsTheLitresOutOfTheWorld()
    {
        // The window covers the rim's far corner and says it drains off the edge: the litres
        // leave the world, counted as emitted and spilled — rain on an outlet catchment's rule.
        var twin = new Twin();
        twin.Place(Twin.PumpCell, Pump);
        var corner = new BrushCell(30, 10, 22);
        twin.Place(corner, Outlet);
        twin.PourIntoRoom(40 * Cap);
        (int sx, int sz) = twin.Water.SampleOf(corner.OriginVoxel.X, corner.OriginVoxel.Z);
        Assert.True(twin.Water.TryBasinAtSample(sx, sz, out int basin));
        Assert.Equal(Watershed.Outlet, basin);
        Assert.False(twin.Terrain.IsLoaded(corner.OriginVoxel));

        MachineTickReport report = twin.Boundary(step: false);

        Assert.Equal(1, report.Moved);
        Assert.Equal(RelayMillilitresPerBoundary / Litre, report.EmittedLitres);
        Assert.Equal(RelayMillilitresPerBoundary / Litre, twin.Water.SpilledLitres);
        Assert.Equal(0L, twin.Water.StoredLitres);
        twin.AssertIdentity();
    }

    [Fact]
    public void OnlyWholeLitresCrossATierAndTheRemainderStaysWhereItWas()
    {
        // B -> A: 20,500 mL in the box against a 50,000 budget offers 20,500; a basin counts
        // litres, so 20,000 cross and 500 stay in the box. A -> B: a lake pump into a layer with
        // 15,125 mL of room takes 15 L, not 15,125 mL, and the layer is left 125 mL short.
        var twin = new Twin();
        twin.Place(Twin.PumpCell, Pump);
        twin.Place(Twin.BedOutlet, Outlet);
        twin.Unload(Twin.BedOutletChunk);
        VoxelPos beside = Twin.PumpCell.Offset(1, 0, 0).VoxelAt(0, 0, 0);
        Assert.Equal(0L, twin.Fluid.Add(beside, Cap));
        Assert.Equal(0L, twin.Fluid.Add(beside.Offset(0, 0, 1), 4_875));
        Assert.Equal(20_500L, twin.InBoxOf(Twin.PumpCell));

        MachineTickReport report = twin.Boundary(step: false);

        Assert.Equal(20_000L, report.PumpedMillilitres);
        Assert.Equal(20L, report.EmittedLitres);
        Assert.Equal(500L, twin.InBoxOf(Twin.PumpCell));
        twin.AssertIdentity();

        var lake = new Twin();
        lake.Place(Twin.BedPump, Pump);
        lake.Place(Twin.RoomOutlet, Outlet);
        lake.Lake(110_000);
        for (int lz = 0; lz < BrushCell.Voxels; lz++)
        for (int lx = 0; lx < BrushCell.Voxels; lx++)
        {
            VoxelPos v = Twin.RoomOutlet.VoxelAt(lx, BrushCell.Voxels, lz);
            Assert.Equal(0L, lake.Fluid.Add(v, lx == 0 && lz == 0 ? Cap - 15_125 : Cap));
        }

        long overBefore = lake.OverMouthOf(Twin.RoomOutlet);
        report = lake.Boundary(step: false);

        Assert.Equal(15L, report.PumpedLitres);
        Assert.Equal(15_000L, report.EmittedMillilitres);
        Assert.Equal(overBefore + 15_000, lake.OverMouthOf(Twin.RoomOutlet));
        lake.AssertIdentity();
    }

    [Fact]
    public void AnOutletWhoseMouthStandsInTheLakeDeliversWholeLitresToTheLake()
    {
        var twin = new Twin();
        twin.Place(Twin.PumpCell, Pump);
        twin.Place(Twin.BedOutlet, Outlet);
        twin.PourIntoRoom(40 * Cap);
        twin.Lake(110_000);
        VoxelPos mouth = Twin.BedOutlet.Offset(0, 1, 0).OriginVoxel;
        Assert.True(twin.Bounded.InLake(mouth));
        long lakeBefore = twin.Water.StoredLitres;

        MachineTickReport report = twin.Boundary(step: false);

        Assert.Equal(1, report.Moved);
        Assert.Equal(RelayMillilitresPerBoundary / Litre, report.EmittedLitres);
        Assert.Equal(RelayMillilitresPerBoundary / Litre, twin.Tick.EmittedLitresIntoLakes);
        Assert.Equal(lakeBefore + (RelayMillilitresPerBoundary / Litre), twin.Water.StoredLitres);
        Assert.Equal(0L, twin.OverMouthOf(Twin.BedOutlet));
        twin.AssertIdentity();
    }

    [Fact]
    public void ALakePumpAndALakeOutletMoveWholeLitresWithinTierA()
    {
        var twin = new Twin();
        twin.Place(Twin.BedPump, Pump);
        twin.Place(Twin.BedOutlet, Outlet);
        twin.Lake(110_000);
        long lakeBefore = twin.Water.StoredLitres;

        MachineTickReport report = twin.Boundary(step: false);

        Assert.Equal(1, report.Moved);
        Assert.Equal(RelayMillilitresPerBoundary / Litre, report.PumpedLitres);
        Assert.Equal(RelayMillilitresPerBoundary / Litre, report.EmittedLitres);
        Assert.Equal(0L, report.PumpedMillilitres + report.EmittedMillilitres);
        Assert.Equal(lakeBefore, twin.Water.StoredLitres);
        Assert.Equal(0L, twin.Fluid.TotalMillilitres);
        twin.AssertIdentity();
    }

    // ---------------------------------------------------------------- the wider intake

    [Fact]
    public void AWiderIntakeReachesWaterAFaceCellCannot()
    {
        // Water two cells from the body: radius 1 reads nothing, radius 2 reads it.
        var twin = new Twin();
        twin.Place(Twin.RimOutlet, Outlet);
        twin.PourIntoRoom(4 * Cap, xMin: 48, xMax: 51, zMin: 40, zMax: 40);

        var narrow = new Twin();
        narrow.Place(Twin.RimOutlet, Outlet);
        narrow.PourIntoRoom(4 * Cap, xMin: 48, xMax: 51, zMin: 40, zMax: 40);

        twin.Place(Twin.PumpCell, WidePump);
        narrow.Place(Twin.PumpCell, Pump);
        Assert.Equal(0L, narrow.InBoxOf(Twin.PumpCell, 1));
        Assert.Equal(4L * Cap, twin.InBoxOf(Twin.PumpCell, 2));

        Assert.Equal(1, narrow.Boundary(step: false).NoWater);
        MachineTickReport wide = twin.Boundary(step: false);
        Assert.Equal(1, wide.Moved);
        Assert.Equal(RelayMillilitresPerBoundary, wide.PumpedMillilitres);
        Assert.Equal(124L * Cell64, twin.Tick.IntakeVoxelsRead);
    }

    // ---------------------------------------------------------------- determinism

    [Fact]
    public void TheSameSetFromTheSameOpenTwiceLandsTheSameMillilitresInTheSameCells()
    {
        Twin a = Scene(), b = Scene();
        for (int boundary = 0; boundary < 60; boundary++)
        {
            MachineTickReport ra = a.Boundary(), rb = b.Boundary();
            Assert.Equal(ra, rb);
        }

        Dictionary<VoxelPos, int> cellsA = a.Fluid.Grid.HeldCells().ToDictionary(c => c, c => a.Fluid.Grid.AmountAt(c));
        Dictionary<VoxelPos, int> cellsB = b.Fluid.Grid.HeldCells().ToDictionary(c => c, c => b.Fluid.Grid.AmountAt(c));
        Assert.Equal(cellsA.Count, cellsB.Count);
        Assert.All(cellsA, kv => Assert.Equal(kv.Value, cellsB[kv.Key]));
        Assert.Equal(a.Water.StoredLitres, b.Water.StoredLitres);
        Assert.Equal(a.Tick.CursorOn(0), b.Tick.CursorOn(0));

        static Twin Scene()
        {
            var twin = new Twin();
            twin.Place(Twin.PumpCell, Pump);
            twin.Place(new BrushCell(12, 2, 10), Pump);
            twin.Place(Twin.RimOutlet, Outlet);
            twin.Place(Twin.RimOutletB, Outlet);
            twin.Place(Twin.RoomOutlet, Outlet);
            twin.Place(Twin.BedPump, Pump);
            twin.PourIntoRoom(160 * Cap);
            twin.Lake(110_000);
            return twin;
        }
    }

    [Fact]
    public void ARowAddedOrRemovedRebuildsTheRingAndAShrunkRingWrapsTheCursor()
    {
        var twin = new Twin();
        twin.Place(Twin.PumpCell, Pump);
        twin.Place(Twin.RimOutlet, Outlet);
        MachineRow b = twin.Place(Twin.RimOutletB, Outlet);
        twin.PourIntoRoom(120 * Cap);
        twin.Fluid.Settle(200);

        twin.Boundary(step: false);
        Assert.Equal(1, twin.Tick.CursorOn(0));

        Assert.True(twin.Machines.Remove(b.Cell));
        MachineTickReport report = twin.Boundary(step: false);
        Assert.Equal(1, report.Outlets);
        Assert.Equal(1, report.Moved);
        Assert.Equal(0, twin.Tick.CursorOn(0));
        Assert.Equal(2 * RelayMillilitresPerBoundary, twin.OverMouthOf(Twin.RimOutlet));
        Assert.Equal(0L, twin.OverMouthOf(Twin.RimOutletB));
    }

    [Fact]
    public void ARowOfUnknownKindTicksNothing()
    {
        var twin = new Twin();
        var row = new MachineRow(Twin.PumpCell, Stone, BrushFacing.PositiveX, [new KeyValuePair<string, long>("channel", 0)]);
        twin.Machines.Add(row);
        twin.Place(Twin.RimOutlet, Outlet);
        twin.PourIntoRoom(40 * Cap);

        MachineTickReport report = twin.Boundary(step: false);
        Assert.Equal(0, report.Pumps);
        Assert.Equal(1, report.Outlets);
        Assert.Equal(0L, twin.Fluid.PumpedMillilitres);
    }

    // ---------------------------------------------------------------- the lifecycle

    /// <summary>Bodies solid to the water: <c>Place.Machine</c>'s effect on a terrain that has no notion of a placed voxel.</summary>
    private sealed class BodiedTerrain(LoadableTerrain inner, MachineSet machines) : IFluidTerrain
    {
        public bool CanHold(VoxelPos v) => inner.CanHold(v) && !machines.TryGet(BrushCell.FromVoxel(v), out _);

        public bool IsLoaded(VoxelPos v) => inner.IsLoaded(v);
    }

    private static WorldGenerator Generator(ulong seed) =>
        new(
            new WorldGenSettings
            {
                Seed = seed,
                CaveThreshold = 0.35f,
                CaveLatticeStep = 4,
            },
            new StrataTable([new StrataLayer(1, 1), new StrataLayer(2, 3)], 3));

    [Theory]
    [InlineData(0x5EEDUL)]
    [InlineData(0xA11CEUL)]
    [InlineData(0xBEEFUL)]
    [InlineData(7UL)]
    public void AcrossRandomUnloadsAndQuitsWithMachinesRunningBothIdentitiesStayExact(ulong seed)
    {
        // M8b-4a's lifecycle with machines in it: pumps and outlets stand in the terrain's own
        // openings on real strata, water is poured, chunks leave and come back, and every few
        // rounds the world quits — panels and the machine table written to a store, a fresh
        // simulation and a fresh set opened from it, the chunks arriving again. At every
        // boundary Tier B's identity, with pumped and emitted in it, holds; the machine layer's
        // own, pumped == emitted, holds; a pump whose box is away stalls rather than reading a
        // dry panel; and after a quit the rows come back whole and the tick goes on from them.
        WorldGenerator generator = Generator(seed);
        int surface = generator.SurfaceHeightAt(0, 0);
        const int minX = -16, maxX = 15, minZ = -16, maxZ = 15;
        int minY = surface - 24, maxY = surface + 15;

        var inner = new GeneratedTerrain(generator, minX, minY, minZ, maxX, maxY, maxZ);
        var toggled = new LoadableTerrain(inner);
        var machines = new MachineSet(Materials);
        var bodied = new BodiedTerrain(toggled, machines);
        var sim = new FluidSimulation(bodied);
        var tick = new MachineTick(machines, Materials, sim, bodied);
        var store = new InMemoryWorldStore();

        ChunkPos lo = new VoxelPos(minX, minY, minZ).Chunk;
        ChunkPos hi = new VoxelPos(maxX, maxY, maxZ).Chunk;
        var chunks = new List<ChunkPos>();
        for (int cx = lo.X; cx <= hi.X; cx++)
        for (int cy = lo.Y; cy <= hi.Y; cy++)
        for (int cz = lo.Z; cz <= hi.Z; cz++)
        {
            chunks.Add(new ChunkPos(cx, cy, cz));
        }

        var random = new Random((int)(seed & 0x7FFFFFFF));

        // Bodies in the open air above the surface: a pump and two outlets on channel 0, a pump
        // and an outlet on channel 1, each a cell whose 64 voxels are all holdable air.
        int placed = 0;
        int[] kinds = [Pump, Outlet, Outlet, Pump, Outlet];
        int[] channels = [0, 0, 0, 1, 1];
        for (int attempt = 0; attempt < 2_000 && placed < kinds.Length; attempt++)
        {
            // Above the surface and under the box's lid, so a mouth's layer is inside the box.
            var cell = new BrushCell(random.Next(minX / 4, maxX / 4), random.Next(surface / 4 + 1, maxY / 4 - 1), random.Next(minZ / 4, maxZ / 4));
            bool clear = !machines.TryGet(cell, out _);
            for (int lz = 0; lz < BrushCell.Voxels && clear; lz++)
            for (int lx = 0; lx < BrushCell.Voxels && clear; lx++)
            for (int ly = 0; ly < BrushCell.Voxels && clear; ly++)
            {
                clear = inner.CanHold(cell.VoxelAt(lx, ly, lz));
            }

            if (!clear)
            {
                continue;
            }

            var row = new MachineRow(cell, kinds[placed], BrushFacing.PositiveX, Materials.Machine(kinds[placed])!.DefaultPorts());
            row.SetPortUnchecked("channel", channels[placed]);
            machines.Add(row);
            placed++;
        }

        Assert.Equal(kinds.Length, placed);

        // Water in every pump's box to begin with, so the run is not vacuous for the machines
        // whatever the random pours do; what the pumps move after that is the terrain's.
        long offered = 0, rejected = 0, pumpedInAll = 0;
        foreach (MachineRow row in machines.Rows)
        {
            if (Materials.Machine(row.MaterialId)!.Kind != MachineKind.Pump) continue;
            int poured = 0;
            foreach ((int dx, int dy, int dz) in MachineTick.IntakeCellsAt(1))
            {
                BrushCell neighbour = row.Cell.Offset(dx, dy, dz);
                for (int lz = 0; lz < BrushCell.Voxels && poured < 8; lz++)
                for (int lx = 0; lx < BrushCell.Voxels && poured < 8; lx++)
                for (int ly = 0; ly < BrushCell.Voxels && poured < 8; ly++)
                {
                    VoxelPos v = neighbour.VoxelAt(lx, ly, lz);
                    if (bodied.CanHold(v))
                    {
                        offered += Cap;
                        rejected += sim.Add(v, Cap);
                        poured++;
                    }
                }
            }

            Assert.Equal(8, poured);
        }

        int quits = 0, moves = 0, stalledAway = 0, stalledNoRoom = 0, stalledNoWater = 0;
        for (int round = 0; round < 80; round++)
        {
            if (sim.TickInProgress) sim.Step();

            if (random.Next(3) != 0)
            {
                int x = random.Next(minX, maxX + 1);
                int z = random.Next(minZ, maxZ + 1);
                VoxelPos? top = inner.TopOpening(x, z);
                if (top is { } cell && toggled.IsLoaded(cell) && bodied.CanHold(cell))
                {
                    offered += Cap;
                    rejected += sim.Add(cell, Cap);
                }
            }

            // The boundary: the machines, then the ticks. A pump whose box straddles a chunk
            // that is away stalls whole, and the report says so.
            MachineTickReport report = tick.Run();
            Assert.True(report.Exact, $"seed {seed:X}, round {round}: {report}");
            pumpedInAll += report.PumpedInAll;
            moves += report.Moved;
            stalledAway += report.Unloaded;
            stalledNoRoom += report.NoRoom;
            stalledNoWater += report.NoWater;

            int ticks = random.Next(1, 9);
            for (int t = 0; t < ticks; t++)
            {
                sim.Step();
            }

            if (random.Next(4) == 0)
            {
                sim.LevelSheets();
            }

            FluidAssert.Conserves(sim, rejected);
            Assert.True(tick.CrossTierExact);
            Assert.Equal(sim.PumpedMillilitres, tick.PumpedMillilitres);
            Assert.Equal(sim.EmittedMillilitres, tick.EmittedMillilitres);

            List<ChunkPos> wet = sim.Grid.HeldCells().Select(c => c.Chunk).Distinct().Where(chunks.Contains).ToList();
            ChunkPos target = wet.Count > 0 && random.Next(10) < 7 ? wet[random.Next(wet.Count)] : chunks[random.Next(chunks.Count)];
            if (toggled.IsChunkLoaded(target))
            {
                toggled.Unload(target);
            }
            else
            {
                toggled.Load(target);
                sim.ChunkArrived(target);
            }

            if (random.Next(5) == 0)
            {
                // The quit: the tick in flight is finished, the panels and the rows are written
                // in one batch, and a fresh simulation and set open from the store with every
                // chunk away, the rows restored before the first boundary.
                if (sim.TickInProgress) sim.Step();
                long heldBefore = sim.TotalMillilitres;
                int rowsBefore = machines.Count;
                List<ChunkPos> loaded = chunks.Where(toggled.IsChunkLoaded).ToList();

                var pending = new List<FluidPanelSnapshot>();
                sim.TakeAllDirty(pending);
                store.Write(new WorldBatch
                {
                    Fluid = [.. pending.Select(p => (p.Chunk, FluidPanelBlob.Write(p)))],
                    Machines = machines.Snapshot(),
                });

                foreach (ChunkPos chunk in chunks) toggled.Unload(chunk);
                machines = new MachineSet(Materials);
                RestoreReport restored = machines.Restore(store.ReadMachines(), id => Materials.Machine(id)?.Ports);
                Assert.Equal(rowsBefore, restored.Rows);
                Assert.Equal(0, restored.Clamped + restored.Dropped + restored.Defaulted + restored.Unknown);

                bodied = new BodiedTerrain(toggled, machines);
                sim = new FluidSimulation(bodied);
                foreach ((ChunkPos position, byte[] blob) in store.ReadFluid())
                {
                    sim.Restore(FluidPanelBlob.Read(blob, position));
                }

                tick = new MachineTick(machines, Materials, sim, bodied);
                Assert.Equal(heldBefore, sim.TotalMillilitres);
                Assert.Equal(sim.TotalMillilitres, sim.LoadedMillilitres);

                // With everything away every pump stalls unloaded and nothing moves.
                MachineTickReport away = tick.Run();
                Assert.Equal(0, away.Moved);
                Assert.Equal(2, away.Unloaded);

                foreach (ChunkPos chunk in loaded)
                {
                    toggled.Load(chunk);
                    sim.ChunkArrived(chunk);
                }

                rejected = 0;
                quits++;
            }
        }

        foreach (ChunkPos chunk in chunks)
        {
            if (!toggled.IsChunkLoaded(chunk))
            {
                toggled.Load(chunk);
                sim.ChunkArrived(chunk);
            }
        }

        for (int i = 0; i < 20; i++)
        {
            tick.Run();
            sim.Step();
        }

        FluidAssert.Conserves(sim, rejected);
        Assert.True(tick.CrossTierExact);
        Assert.True(moves > 0, $"no pump ever moved anything ({stalledAway} away, {stalledNoRoom} no room, {stalledNoWater} no water): the run says nothing about the machines");
        output.WriteLine(
            $"seed {seed:X}: {offered / Litre:N0} L offered, {pumpedInAll / Litre:N0} L pumped over {moves} pump-boundaries, "
            + $"{stalledAway} stalled on a chunk away, {stalledNoRoom} no room, {stalledNoWater} no water, {quits} quit(s), {machines.Count} row(s) at the end");
    }
}
