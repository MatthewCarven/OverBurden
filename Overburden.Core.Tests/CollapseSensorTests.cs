using Overburden.Core.Building;
using Overburden.Core.Content;
using Overburden.Core.Fluid;
using Overburden.Core.Machines;
using Overburden.Core.Persistence;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>A sense a test dictates: fuses by chunk with their time left, and event members; it counts what it is asked.</summary>
internal sealed class FakeCollapseSense : ICollapseSense
{
    public Dictionary<ChunkPos, long> Fuses { get; } = [];

    public HashSet<ChunkPos> Members { get; } = [];

    public int ArmedAsked { get; private set; }

    public bool Armed(ChunkPos chunk)
    {
        ArmedAsked++;
        return Fuses.ContainsKey(chunk);
    }

    public bool Member(ChunkPos chunk) => Members.Contains(chunk);

    public long? FuseLeft(ChunkPos chunk) => Fuses.TryGetValue(chunk, out long left) ? left : null;
}

/// <summary>
/// Collapse f-1 (COLLAPSE-PLAN part f, and f-1's re-read): the sensor kind, the per-channel signal it
/// raises at the boundary, and the two readers that follow it through <c>onsignal</c> — the placed cell
/// and the pump — with the ideal-machines decision 10's rule that a signalled-off cell never unpays a
/// vault's bill. A light rig, no water: the water side is in <see cref="MachineTickTests"/>.
/// </summary>
public class CollapseSensorTests
{
    private const int Air = 0, ReaderCell = 1, PlainCell = 2, ReaderPump = 3, Sensor = 4, Vault = 5, Pack = 6, Window = 7, Panel = 8;

    private const long CellCapacity = 1_000_000_000L;                 // 1 MJ
    private const long PackRecharge = 250L * MachineTick.DefaultBoundaryMillis; // 125 J a boundary
    private const long VaultBill = 500_000L;                          // 1 m³ of water at 1 kW/m³, half a second
    private const long PanelBoundary = 500L * MachineTick.DefaultBoundaryMillis; // 250 J a boundary, open and clear

    private static readonly MachineLink Charge = new("charge", MachineMedium.Power, MachineRole.Sink, MachineLink.ChannelVia, null, "channel");
    private static readonly PortDefinition Channel = new("channel", 0, 0, 15);
    private static readonly PortDefinition Enabled = new(MachineTick.EnabledPort, 1, 0, 1);
    private static readonly PortDefinition OnSignal = new(MachineTick.OnSignalPort, 0, 0, 2);
    private static readonly PortDefinition Radius = new(MachineDefinition.RadiusPort, 1, 0, MachineDefinition.MaxSensorRadiusChunks);
    private static readonly Dictionary<MachineExtra, ExtraForm> Sense = new() { [MachineExtra.Sense] = ExtraForm.Switch };

    private static readonly MachineDefinition ReaderCellDefinition = new()
    {
        Kind = MachineKind.Storage,
        CapacityMillijoules = CellCapacity,
        Links = [Charge],
        Ports = [Channel, Enabled, OnSignal],
        ExtraForms = Sense,
    };

    /// <summary>A cell with a port called <c>onsignal</c> and no <c>sense</c> extra: not a reader, so it follows nothing.</summary>
    private static readonly MachineDefinition PlainCellDefinition = new()
    {
        Kind = MachineKind.Storage,
        CapacityMillijoules = CellCapacity,
        Links = [Charge],
        Ports = [Channel, Enabled, OnSignal],
    };

    private static readonly MachineDefinition ReaderPumpDefinition = new()
    {
        Kind = MachineKind.Pump,
        MillilitresPerSecond = 100_000,
        JoulesPerLitre = 1000,
        Links = [new MachineLink("water", MachineMedium.Fluid, MachineRole.Source, MachineLink.ChannelVia, null, "channel")],
        Ports = [Channel, OnSignal],
        ExtraForms = Sense,
    };

    internal static readonly MachineDefinition SensorDefinition = new()
    {
        Kind = MachineKind.Sensor,
        Senses = MachineDefinition.SensesCollapse,
        Ports = [Channel, Radius],
    };

    private static readonly MachineDefinition VaultDefinition = new()
    {
        Kind = MachineKind.Vault,
        InsideMillilitres = MachineDefinition.MillilitresPerCubicMetre,
        WaterHoldMilliwattsPerCubicMetre = 1_000_000,
        HoldsWater = true,
        Links = [new MachineLink("power", MachineMedium.Power, MachineRole.Sink, MachineLink.ChannelVia, null, "channel")],
        Ports = [Channel],
    };

    private static readonly MaterialTable Materials = MaterialTable.ForTesting(
        //            air    reader plain  pump   sensor vault  pack   window panel
        isOpaque: [false, true, true, true, true, true, true, true, true],
        isFluid: [false, false, false, false, false, false, false, false, false],
        textureLayer: [0, 1, 2, 3, 4, 5, 6, 7, 8],
        hardness: [0f, 3f, 3f, 3f, 3f, 3f, 1f, 1f, 1f],
        machine:
        [
            null, ReaderCellDefinition, PlainCellDefinition, ReaderPumpDefinition, SensorDefinition, VaultDefinition,
            new MachineDefinition { Kind = MachineKind.Storage, Carried = true, CapacityMillijoules = 3_600_000_000L, RechargeWatts = 250, Links = [Charge], Ports = [Channel] },
            WindowTests.WindowDefinition,
            new MachineDefinition
            {
                Kind = MachineKind.Converter,
                RatedWatts = 500,
                Links =
                [
                    new MachineLink("sky", MachineMedium.Power, MachineRole.Sink, MachineDefinition.SolarVia, null, null),
                    new MachineLink("out", MachineMedium.Power, MachineRole.Source, MachineLink.ChannelVia, null, "channel"),
                ],
                Ports = [Channel],
            },
        ],
        placeable: [true, true, true, true, true, true, false, false, true]);

    private static readonly BrushCell SensorCell = new(4, 4, 4);

    private sealed class NoWater : IFluidTerrain
    {
        public bool CanHold(VoxelPos voxel) => false;
    }

    private sealed class OpenSky : ISolarSky
    {
        public bool TryOpenColumnsAbove(BrushCell cell, out int open)
        {
            open = 16;
            return true;
        }
    }

    private sealed class Rig
    {
        public Rig(bool withSense = true)
        {
            var terrain = new NoWater();
            Machines = new MachineSet(Materials);
            Tick = new MachineTick(Machines, Materials, new FluidSimulation(terrain), terrain)
            {
                Sky = new OpenSky(),
                CollapseSense = withSense ? Sense : null,
            };
        }

        public FakeCollapseSense Sense { get; } = new();

        public MachineSet Machines { get; }

        public MachineTick Tick { get; }

        /// <summary>A row as a restore would leave it: its ports and charge set before it enters, so <c>entering</c> counts the charge.</summary>
        public MachineRow Place(BrushCell cell, int material, int channel = 0, long charge = 0, long? onSignal = null, long? radius = null)
        {
            var row = new MachineRow(cell, material, BrushFacing.PositiveX, Materials.Machine(material)!.DefaultPorts());
            row.SetPortUnchecked("channel", channel);
            if (onSignal is { } on) row.SetPortUnchecked(MachineTick.OnSignalPort, on);
            if (radius is { } r) row.SetPortUnchecked(MachineDefinition.RadiusPort, r);
            if (charge > 0) StorageState.Write(row, charge);
            Machines.Add(row);
            return row;
        }

        /// <summary>A vault holding a cubic metre of water, so its bill is <see cref="VaultBill"/> a boundary.</summary>
        public MachineRow PlaceVault(BrushCell cell, int channel = 0)
        {
            var row = new MachineRow(cell, Vault, BrushFacing.PositiveX, Materials.Machine(Vault)!.DefaultPorts());
            row.SetPortUnchecked("channel", channel);
            VaultState.Write(row, new VaultContents { WaterMillilitres = MachineDefinition.MillilitresPerCubicMetre });
            Machines.Add(row);
            return row;
        }

        public EquippedPack Wear(int material, int channel = 0, long charge = 0)
        {
            var pack = new EquippedPack(material, Materials.Machine(material)!, charge, channel);
            Tick.EquipPack(pack);
            return pack;
        }

        /// <summary>A fuse on the chunk <paramref name="dx"/>, <paramref name="dy"/>, <paramref name="dz"/> from the sensor's.</summary>
        public ChunkPos Fuse(int dx, int dy, int dz, long left = 12_000)
        {
            ChunkPos chunk = Near(dx, dy, dz);
            Sense.Fuses[chunk] = left;
            return chunk;
        }

        public ChunkPos Member(int dx, int dy, int dz)
        {
            ChunkPos chunk = Near(dx, dy, dz);
            Sense.Members.Add(chunk);
            return chunk;
        }

        public long Bank(int channel = 0) => ((IChannelBank)Tick).BankMillijoules(channel);

        private static ChunkPos Near(int dx, int dy, int dz)
        {
            ChunkPos home = SensorCell.OriginVoxel.Chunk;
            return new ChunkPos(home.X + dx, home.Y + dy, home.Z + dz);
        }
    }

    private static long ChargeOf(MachineRow row) => StorageState.Read(row);

    // ---------------------------------------------------------------- the fixtures and the kind

    [Fact]
    public void TheFixturesAreValidMachines()
    {
        foreach (MachineDefinition definition in new[] { ReaderCellDefinition, PlainCellDefinition, ReaderPumpDefinition, SensorDefinition, VaultDefinition })
        {
            Assert.True(definition.TryValidate(out string? error), error);
        }

        Assert.Equal(MachineKind.Sensor, Materials.Machine(Sensor)!.Kind);
    }

    // ---------------------------------------------------------------- what trips it (decisions 1, 2)

    [Fact]
    public void AFuseWithinTheRadiusTripsTheSensorAndSignalsItsChannel()
    {
        var rig = new Rig();
        MachineRow sensor = rig.Place(SensorCell, Sensor, channel: 3);
        rig.Fuse(1, -1, 1, left: 12_000);   // a corner: Chebyshev 1

        MachineTickReport report = rig.Tick.Run();

        Assert.True(rig.Tick.IsSignalled(3));
        Assert.False(rig.Tick.IsSignalled(0));
        Assert.Equal(new SensorReading(SensorTrip.Fuse, 1, 12_000), sensor.LastSensed);
        Assert.Equal(1, report.ChannelsSignalled);
        Assert.Equal(1, report.SensorsTripped);
        Assert.Equal(1, report.TrippedByFuse);
        Assert.Equal(0, report.TrippedByEvent);
        Assert.Equal(1, rig.Tick.ChannelsSignalled);
        Assert.Equal(1L, rig.Tick.SensorTripsByFuse);
        Assert.Equal(27L, rig.Tick.SenseLookups);   // (2·1 + 1)³ at the default radius
        Assert.Equal(27, rig.Sense.ArmedAsked);
    }

    [Fact]
    public void AnEventWithinTheRadiusTripsItAndBeatsAFuse()
    {
        var rig = new Rig();
        MachineRow sensor = rig.Place(SensorCell, Sensor, channel: 5, radius: 2);
        rig.Member(-2, 0, 0);
        rig.Member(0, 2, -1);
        rig.Fuse(1, 0, 0, left: 9_000);
        rig.Fuse(0, 0, 2, left: 4_000);

        MachineTickReport report = rig.Tick.Run();

        // The nearest member's distance, and the soonest fuse beside it, whatever tripped the sensor.
        Assert.Equal(new SensorReading(SensorTrip.Event, 2, 4_000), sensor.LastSensed);
        Assert.True(rig.Tick.IsSignalled(5));
        Assert.Equal(1, report.TrippedByEvent);
        Assert.Equal(0, report.TrippedByFuse);

        rig.Member(1, 1, 1);
        rig.Tick.Run();
        Assert.Equal(new SensorReading(SensorTrip.Event, 1, 4_000), sensor.LastSensed);

        // The event ends: the fuses still trip it, the soonest one read with its distance.
        rig.Sense.Members.Clear();
        report = rig.Tick.Run();
        Assert.Equal(new SensorReading(SensorTrip.Fuse, 2, 4_000), sensor.LastSensed);
        Assert.Equal(1, report.TrippedByFuse);
        Assert.True(rig.Tick.IsSignalled(5));
        Assert.Equal(2L, rig.Tick.SensorTripsByEvent);
        Assert.Equal(1L, rig.Tick.SensorTripsByFuse);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    public void OutOfTheRadiusDoesNotTripAndTheEdgeDoes(int radius)
    {
        int beyond = radius + 1;
        var rig = new Rig();
        MachineRow sensor = rig.Place(SensorCell, Sensor, radius: radius);
        rig.Fuse(beyond, 0, 0);
        rig.Fuse(0, -beyond, 0);
        rig.Fuse(0, 0, beyond);
        rig.Member(-beyond, beyond, -beyond);
        rig.Member(0, 0, -beyond);

        rig.Tick.Run();

        Assert.Equal(SensorTrip.Quiet, sensor.LastSensed.Trip);
        Assert.False(rig.Tick.IsSignalled(0));
        long side = (2 * radius) + 1;
        Assert.Equal(side * side * side, rig.Tick.SenseLookups);

        // The corner at exactly the radius is in reach.
        rig.Fuse(radius, -radius, radius, left: 15_000);
        rig.Tick.Run();
        Assert.Equal(new SensorReading(SensorTrip.Fuse, radius, 15_000), sensor.LastSensed);
        Assert.True(rig.Tick.IsSignalled(0));
    }

    [Fact]
    public void ARadiusPastTheEnginesBoundReadsAsTheBound()
    {
        // A row set past 4 (a restore clamps, but the tick does not trust it) asks 9³ chunks, not 15³.
        var rig = new Rig();
        MachineRow sensor = rig.Place(SensorCell, Sensor, radius: 7);
        rig.Fuse(5, 0, 0);

        rig.Tick.Run();

        Assert.Equal(729L, rig.Tick.SenseLookups);
        Assert.Equal(SensorTrip.Quiet, sensor.LastSensed.Trip);
    }

    [Fact]
    public void WithNoSenseEverySensorIsQuietAndAsksNothing()
    {
        var rig = new Rig(withSense: false);
        MachineRow sensor = rig.Place(SensorCell, Sensor);
        rig.Fuse(0, 0, 0);
        rig.Member(0, 0, 0);

        MachineTickReport report = rig.Tick.Run();

        Assert.Equal(default, sensor.LastSensed);
        Assert.False(rig.Tick.IsSignalled(0));
        Assert.Equal(0, report.SensorsTripped);
        Assert.Equal(0L, rig.Tick.SenseLookups);
        Assert.Equal(1L, rig.Tick.SensorReads);
        Assert.Equal(1, rig.Tick.Sensors);
    }

    [Fact]
    public void TheSignalStandsUntilTheNextBoundaryAndClearsWithTheFuse()
    {
        var rig = new Rig();
        rig.Place(SensorCell, Sensor);
        MachineRow cell = rig.Place(new BrushCell(0, 0, 0), ReaderCell, charge: 400_000, onSignal: 1);
        rig.Fuse(0, 0, 0);

        rig.Tick.Run();
        Assert.True(rig.Tick.IsSignalled(0));
        Assert.Equal(0L, rig.Bank());

        // The fuse goes out between boundaries: the signal is the last boundary's until the next.
        rig.Sense.Fuses.Clear();
        Assert.True(rig.Tick.IsSignalled(0));
        Assert.True(rig.Tick.IsSignalledOff(cell));
        Assert.Equal(0L, rig.Bank());

        rig.Tick.Run();
        Assert.False(rig.Tick.IsSignalled(0));
        Assert.False(rig.Tick.IsSignalledOff(cell));
        Assert.Equal(400_000L, rig.Bank());
        Assert.True(rig.Tick.PowerExact);
    }

    [Fact]
    public void TwoSensorsOnTwoChannelsSignalOnlyTheirOwn()
    {
        var rig = new Rig();
        rig.Place(SensorCell, Sensor, channel: 2);
        MachineRow far = rig.Place(new BrushCell(4 + (8 * 6), 4, 4), Sensor, channel: 9);
        rig.Fuse(0, 1, 0);
        MachineRow held = rig.Place(new BrushCell(0, 0, 0), ReaderCell, channel: 2, charge: 1_000, onSignal: 1);
        MachineRow other = rig.Place(new BrushCell(1, 0, 0), ReaderCell, channel: 9, charge: 1_000, onSignal: 1);

        rig.Tick.Run();

        Assert.True(rig.Tick.IsSignalled(2));
        Assert.False(rig.Tick.IsSignalled(9));
        Assert.Equal(SensorTrip.Quiet, far.LastSensed.Trip);
        Assert.True(rig.Tick.IsSignalledOff(held));
        Assert.False(rig.Tick.IsSignalledOff(other));
    }

    // ---------------------------------------------------------------- the readers (decisions 4, 5)

    [Theory]
    [InlineData(0, false, true)]
    [InlineData(1, false, true)]
    [InlineData(2, false, false)]
    [InlineData(0, true, true)]
    [InlineData(1, true, false)]
    [InlineData(2, true, true)]
    public void ACellFollowsOnSignal(long onSignal, bool signalled, bool pooled)
    {
        var rig = new Rig();
        rig.Place(SensorCell, Sensor);
        if (signalled) rig.Fuse(0, 0, 0);
        MachineRow cell = rig.Place(new BrushCell(0, 0, 0), ReaderCell, charge: 1_000_000, onSignal: onSignal);
        EquippedPack pack = rig.Wear(Pack);

        MachineTickReport report = rig.Tick.Run();

        Assert.Equal(signalled, rig.Tick.IsSignalled(0));
        Assert.Equal(!pooled, rig.Tick.IsSignalledOff(cell));
        Assert.Equal(pooled ? 0 : 1, report.CellsSignalledOff);

        // The pack draws last from the pool: a held cell gives it nothing, and keeps its charge whole.
        Assert.Equal(pooled ? PackRecharge : 0, pack.ChargeMillijoules);
        Assert.Equal(pooled ? 1_000_000 - PackRecharge : 1_000_000, ChargeOf(cell));
        Assert.Equal(pooled ? ChargeOf(cell) : 0, rig.Bank());
        Assert.True(rig.Tick.PowerExact);
    }

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(1, false, false)]
    [InlineData(2, false, true)]
    [InlineData(0, true, false)]
    [InlineData(1, true, true)]
    [InlineData(2, true, false)]
    public void APumpFollowsOnSignal(long onSignal, bool signalled, bool held)
    {
        var rig = new Rig();
        rig.Place(SensorCell, Sensor);
        if (signalled) rig.Fuse(0, 0, 0);
        MachineRow pump = rig.Place(new BrushCell(0, 0, 0), ReaderPump, onSignal: onSignal);

        MachineTickReport report = rig.Tick.Run();

        // A held pump stalls signalled first; a running one here has no outlet, the next reason.
        Assert.Equal(held ? PumpStall.Signalled : PumpStall.NoOutlet, pump.LastStall);
        Assert.Equal(held ? 1 : 0, report.Signalled);
        Assert.Equal(held ? 0 : 1, report.NoOutlet);
        Assert.Equal(held ? 1L : 0L, rig.Tick.StalledSignalled);
        Assert.Equal(held, rig.Tick.IsSignalledOff(pump));
    }

    [Fact]
    public void ARowWithoutTheSenseExtraIgnoresItsOnSignalPort()
    {
        var rig = new Rig();
        rig.Place(SensorCell, Sensor);
        rig.Fuse(0, 0, 0);
        MachineRow plain = rig.Place(new BrushCell(0, 0, 0), PlainCell, charge: 1_000_000, onSignal: 1);
        EquippedPack pack = rig.Wear(Pack);

        rig.Tick.Run();

        Assert.True(rig.Tick.IsSignalled(0));
        Assert.False(rig.Tick.IsSignalledOff(plain));
        Assert.Equal(PackRecharge, pack.ChargeMillijoules);
        Assert.Equal(1_000_000 - PackRecharge, ChargeOf(plain));
    }

    [Fact]
    public void AnOnSignalOutsideZeroToTwoFollowsNothing()
    {
        var rig = new Rig();
        rig.Place(SensorCell, Sensor);
        MachineRow cell = rig.Place(new BrushCell(0, 0, 0), ReaderCell, charge: 1_000, onSignal: 3);
        MachineRow pump = rig.Place(new BrushCell(1, 0, 0), ReaderPump, onSignal: -1);

        rig.Tick.Run();
        Assert.False(rig.Tick.IsSignalledOff(cell));
        Assert.False(rig.Tick.IsSignalledOff(pump));

        rig.Fuse(0, 0, 0);
        rig.Tick.Run();
        Assert.False(rig.Tick.IsSignalledOff(cell));
        Assert.Equal(PumpStall.NoOutlet, pump.LastStall);
    }

    // ---------------------------------------------------------------- the hold (the ideal-machines decision 10)

    [Fact]
    public void ASignalledOffCellNeverUnpaysAVaultsBill()
    {
        // The only cell on the channel is held off. The bill is paid out of it all the same; it keeps the
        // rest, and gives none of it to the vault's minute or to the pack, which come after the consumers.
        var held = new Rig();
        held.Place(SensorCell, Sensor);
        held.Fuse(0, 0, 0);
        MachineRow heldVault = held.PlaceVault(new BrushCell(0, 0, 0));
        MachineRow heldCell = held.Place(new BrushCell(1, 0, 0), ReaderCell, charge: 2_000_000, onSignal: 1);
        EquippedPack heldPack = held.Wear(Pack);

        MachineTickReport report = held.Tick.Run();

        Assert.True(held.Tick.IsSignalledOff(heldCell));
        Assert.True(heldVault.LastHoldPaid);
        Assert.Equal(VaultBill, report.HeldMillijoules);
        Assert.Equal(1L, held.Tick.VaultBillsPaid);
        Assert.Equal(0L, held.Tick.FusesArmed);
        Assert.True(VaultState.TryRead(heldVault, out VaultContents heldContents));
        Assert.Equal(VaultPhase.Holding, heldContents.Phase);
        Assert.Equal(0L, heldContents.OwnChargeMillijoules);
        Assert.Equal(2_000_000 - VaultBill, ChargeOf(heldCell));
        Assert.Equal(0L, heldPack.ChargeMillijoules);
        Assert.True(held.Tick.PowerExact);

        // The control: the same cell not held pays the same bill, then fills the vault's minute.
        var on = new Rig();
        on.Place(SensorCell, Sensor);
        on.Fuse(0, 0, 0);
        MachineRow onVault = on.PlaceVault(new BrushCell(0, 0, 0));
        MachineRow onCell = on.Place(new BrushCell(1, 0, 0), ReaderCell, charge: 2_000_000, onSignal: 0);
        on.Wear(Pack);

        on.Tick.Run();

        Assert.True(onVault.LastHoldPaid);
        Assert.Equal(VaultBill, on.Tick.HeldMillijoules);
        Assert.True(VaultState.TryRead(onVault, out VaultContents onContents));
        Assert.Equal(2_000_000 - VaultBill, onContents.OwnChargeMillijoules);
        Assert.Equal(0L, ChargeOf(onCell));
        Assert.True(on.Tick.PowerExact);
    }

    [Fact]
    public void AHeldCellShortOfTheBillLeavesItUnpaidExactlyAsItWouldUnheld()
    {
        // The signal changes nothing about the bill: short is short, held or not. What differs is after
        // the consumers: the cell still on puts its 300 J into the vault's minute; the held one keeps it.
        foreach (long onSignal in new long[] { 0, 1 })
        {
            var rig = new Rig();
            rig.Place(SensorCell, Sensor);
            rig.Fuse(0, 0, 0);
            MachineRow vault = rig.PlaceVault(new BrushCell(0, 0, 0));
            MachineRow cell = rig.Place(new BrushCell(1, 0, 0), ReaderCell, charge: 300_000, onSignal: onSignal);

            rig.Tick.Run();

            Assert.False(vault.LastHoldPaid);
            Assert.Equal(1L, rig.Tick.FusesArmed);
            Assert.Equal(0L, rig.Tick.HeldMillijoules);
            Assert.True(VaultState.TryRead(vault, out VaultContents contents));
            Assert.Equal(VaultPhase.Fusing, contents.Phase);
            Assert.Equal(onSignal == 1 ? 300_000L : 0L, ChargeOf(cell));
            Assert.Equal(onSignal == 1 ? 0L : 300_000L, contents.OwnChargeMillijoules);
            Assert.True(rig.Tick.PowerExact);
        }
    }

    [Fact]
    public void ABillReachesTheHeldCellsOnlyAfterTheSunAndTheCellsStillOnAndTheLastHeldPaysFirst()
    {
        // Held A and B (in that position order) at 400 J each, a cell still on at 300 J, a bill of 500 J:
        // the cell on pays 300, and the 200 left comes out of the held cells, the last in order first.
        var rig = new Rig();
        rig.Place(SensorCell, Sensor);
        rig.Fuse(0, 0, 0);
        rig.PlaceVault(new BrushCell(0, 0, 1));
        MachineRow heldA = rig.Place(new BrushCell(1, 0, 0), ReaderCell, charge: 400_000, onSignal: 1);
        MachineRow heldB = rig.Place(new BrushCell(2, 0, 0), ReaderCell, charge: 400_000, onSignal: 1);
        MachineRow stillOn = rig.Place(new BrushCell(3, 0, 0), ReaderCell, charge: 300_000, onSignal: 0);

        rig.Tick.Run();

        Assert.Equal(400_000L, ChargeOf(heldA));
        Assert.Equal(200_000L, ChargeOf(heldB));
        Assert.Equal(0L, ChargeOf(stillOn));
        Assert.Equal(VaultBill, rig.Tick.HeldMillijoules);
        Assert.True(rig.Tick.PowerExact);

        // With the sun paying, the held cells are not touched at all.
        var sunny = new Rig();
        sunny.Place(SensorCell, Sensor);
        sunny.Fuse(0, 0, 0);
        sunny.PlaceVault(new BrushCell(0, 0, 1));
        sunny.Place(new BrushCell(5, 0, 0), Panel);
        sunny.Place(new BrushCell(6, 0, 0), Panel);
        MachineRow sunnyHeld = sunny.Place(new BrushCell(1, 0, 0), ReaderCell, charge: 400_000, onSignal: 1);

        sunny.Tick.Run();

        Assert.Equal(400_000L, ChargeOf(sunnyHeld));
        Assert.Equal(2 * PanelBoundary, sunny.Tick.GeneratedMillijoules);
        Assert.Equal(VaultBill, sunny.Tick.HeldMillijoules);
        Assert.True(sunny.Tick.PowerExact);
    }

    [Fact]
    public void AHeldCellTakesNoGeneration()
    {
        var rig = new Rig();
        rig.Place(SensorCell, Sensor);
        rig.Fuse(0, 0, 0);
        rig.Place(new BrushCell(5, 0, 0), Panel);
        MachineRow held = rig.Place(new BrushCell(1, 0, 0), ReaderCell, charge: 10_000, onSignal: 1);
        MachineRow stillOn = rig.Place(new BrushCell(2, 0, 0), ReaderCell, charge: 10_000, onSignal: 0);

        rig.Tick.Run();

        Assert.Equal(10_000L, ChargeOf(held));
        Assert.Equal(10_000 + PanelBoundary, ChargeOf(stillOn));
        Assert.Equal(0L, rig.Tick.ClampedMillijoules);
        Assert.True(rig.Tick.PowerExact);
    }

    // ---------------------------------------------------------------- the bank between boundaries (decision 5)

    [Fact]
    public void AHeldCellIsOutOfTheWindowsBankBetweenBoundaries()
    {
        var rig = new Rig();
        rig.Place(SensorCell, Sensor);
        MachineRow held = rig.Place(new BrushCell(0, 0, 0), ReaderCell, charge: 500_000, onSignal: 1);
        MachineRow late = rig.Place(new BrushCell(1, 0, 0), ReaderCell, charge: 200_000, onSignal: 2);
        EquippedPack window = rig.Wear(Window);

        // Before the first boundary nothing is signalled: an onsignal 2 cell is already out.
        Assert.Equal(500_000L, window.SpendableMillijoules);

        rig.Fuse(0, 0, 0);
        rig.Tick.Run();
        Assert.Equal(200_000L, window.SpendableMillijoules);
        Assert.Equal(200_000L, window.Take(1_000_000));
        Assert.Equal(500_000L, ChargeOf(held));
        Assert.Equal(0L, ChargeOf(late));

        rig.Sense.Fuses.Clear();
        rig.Tick.Run();
        Assert.Equal(500_000L, window.SpendableMillijoules);
        Assert.Equal(100_000L, window.Take(100_000));
        Assert.Equal(400_000L, ChargeOf(held));
        Assert.True(rig.Tick.PowerExact);
    }

    [Fact]
    public void TheSiphonCannotDrawOnAHeldCell()
    {
        var rig = new Rig();
        rig.Place(SensorCell, Sensor);
        rig.Fuse(0, 0, 0);
        MachineRow held = rig.Place(new BrushCell(0, 0, 0), ReaderCell, charge: 500_000, onSignal: 1);
        rig.Tick.Run();

        Assert.Equal(0L, rig.Tick.BankOf(0));
        Assert.Equal(0L, rig.Tick.PaySiphon(0, 100_000));
        Assert.Equal(500_000L, ChargeOf(held));
        Assert.True(rig.Tick.PowerExact);
    }

    // ---------------------------------------------------------------- the identity, the port, the blob

    [Fact]
    public void ThePoolIdentityIsExactThroughSignalsComingAndGoing()
    {
        var rig = new Rig();
        rig.Place(SensorCell, Sensor);
        rig.Place(new BrushCell(5, 0, 0), Panel);
        rig.PlaceVault(new BrushCell(0, 0, 1));
        MachineRow off1 = rig.Place(new BrushCell(0, 0, 0), ReaderCell, charge: 30_000_000, onSignal: 1);
        MachineRow on2 = rig.Place(new BrushCell(1, 0, 0), ReaderCell, charge: 10_000_000, onSignal: 2);
        rig.Place(new BrushCell(2, 0, 0), PlainCell, charge: 7_000_000, onSignal: 1);
        rig.Place(new BrushCell(3, 0, 0), ReaderCell, charge: 9_000_000);
        rig.Wear(Pack);

        int signalledBoundaries = 0;
        for (int boundary = 0; boundary < 40; boundary++)
        {
            bool signal = (boundary / 3) % 2 == 1;
            if (signal) rig.Fuse(0, 0, 0);
            else rig.Sense.Fuses.Clear();

            // This boundary's held cell: onsignal 1 while signalled, onsignal 2 while not.
            MachineRow heldNow = signal ? off1 : on2;
            long heldBefore = ChargeOf(heldNow);
            rig.Tick.Run();
            Assert.Equal(signal, rig.Tick.IsSignalled(0));
            Assert.True(rig.Tick.IsSignalledOff(heldNow));
            if (signal) signalledBoundaries++;

            // A held cell never gains: it takes no generation and none of the spare.
            Assert.True(ChargeOf(heldNow) <= heldBefore, $"boundary {boundary}: {ChargeOf(heldNow)} > {heldBefore}");

            Assert.True(rig.Tick.PowerExact, $"boundary {boundary}");
            rig.Tick.PaySiphon(0, 20_000);
            Assert.True(rig.Tick.PowerExact, $"after the siphon, boundary {boundary}");
        }

        Assert.Equal(19, signalledBoundaries);
        Assert.Equal(19L, rig.Tick.SensorTripsByFuse);
    }

    [Fact]
    public void ASignalIsNeverWrittenToAPortOrTheBlobAndMovesNoVersion()
    {
        var rig = new Rig();
        MachineRow sensor = rig.Place(SensorCell, Sensor, channel: 1, radius: 2);
        MachineRow pump = rig.Place(new BrushCell(0, 0, 0), ReaderPump, channel: 1, onSignal: 1);
        MachineRow cell = rig.Place(new BrushCell(1, 0, 0), ReaderCell, channel: 1, charge: 700_000, onSignal: 1);
        MachineRow[] rows = [sensor, pump, cell];

        rig.Tick.Run();
        long version = rig.Machines.Version;
        byte[][] before = rows.Select(MachineRowBlob.Write).ToArray();
        Dictionary<string, long>[] ports = rows.Select(r => new Dictionary<string, long>(r.Ports)).ToArray();

        rig.Fuse(0, 0, 1);
        rig.Tick.Run();
        Assert.True(rig.Tick.IsSignalled(1));
        Assert.Equal(PumpStall.Signalled, pump.LastStall);
        Assert.True(rig.Tick.IsSignalledOff(cell));
        AssertUnchanged();

        rig.Sense.Fuses.Clear();
        rig.Tick.Run();
        Assert.False(rig.Tick.IsSignalled(1));
        AssertUnchanged();

        void AssertUnchanged()
        {
            Assert.Equal(version, rig.Machines.Version);
            for (int i = 0; i < rows.Length; i++)
            {
                Assert.Equal(before[i], MachineRowBlob.Write(rows[i]));
                Assert.Equal(ports[i], new Dictionary<string, long>(rows[i].Ports));
            }
        }
    }

    [Fact]
    public void RowsSavedBeforeF1RestoreAsTheyAreWithOnSignalZero()
    {
        // The shipped readers before f-1: the relay with its channel and priority, the cell with its
        // channel and enabled, and no onsignal on either. Ports are saved by name, so the restore gives
        // the missing one its default, counted, and nothing else moves.
        var content = new GameContent();
        LoadResult result = content.Load(new DirectoryContentSource(ShippedContentTests.ModsDirectory()));
        Assert.True(result.Success, string.Join("; ", result.Errors));
        MaterialTable table = MaterialTable.From(content.Materials);
        int relay = content.Materials.GetNumericId(Identifier.Parse("overburden:relay_pump"));
        int vacuum = content.Materials.GetNumericId(Identifier.Parse("overburden:vacuum_cell"));

        var oldPump = new MachineRow(new BrushCell(0, 0, 0), relay, BrushFacing.PositiveX,
            [new KeyValuePair<string, long>("channel", 2), new KeyValuePair<string, long>(MachineTick.PriorityPort, 7)]);
        var oldCell = new MachineRow(new BrushCell(1, 0, 0), vacuum, BrushFacing.PositiveX,
            [new KeyValuePair<string, long>("channel", 2), new KeyValuePair<string, long>(MachineTick.EnabledPort, 1)]);
        StorageState.Write(oldCell, 1_000_000);
        List<StoredMachine> saved =
        [
            new(oldPump.Origin, relay, MachineRowBlob.Write(oldPump)),
            new(oldCell.Origin, vacuum, MachineRowBlob.Write(oldCell)),
        ];

        var machines = new MachineSet(table);
        RestoreReport report = machines.Restore(saved, id => table.Machine(id)?.Ports);

        Assert.Equal(new RestoreReport(2, 0, 0, 2, 0), report);
        MachineRow pump = machines.Rows.Single(r => r.MaterialId == relay);
        MachineRow cell = machines.Rows.Single(r => r.MaterialId == vacuum);
        Assert.Equal(0L, pump.Ports[MachineTick.OnSignalPort]);
        Assert.Equal(0L, cell.Ports[MachineTick.OnSignalPort]);
        Assert.Equal(2L, pump.Ports["channel"]);
        Assert.Equal(7L, pump.Ports[MachineTick.PriorityPort]);
        Assert.Equal(1_000_000L, StorageState.Read(cell));

        // Both are readers now, and at 0 they follow nothing.
        var terrain = new NoWater();
        var tick = new MachineTick(machines, table, new FluidSimulation(terrain), terrain) { CollapseSense = new FakeCollapseSense() };
        tick.Run();
        Assert.True(table.Machine(relay)!.Has(MachineExtra.Sense));
        Assert.True(table.Machine(vacuum)!.Has(MachineExtra.Sense));
        Assert.False(tick.IsSignalledOff(pump));
        Assert.False(tick.IsSignalledOff(cell));
        Assert.Equal(1_000_000L, tick.BankOf(2));

        // Saved again, the row carries the port it was given.
        MachineRow reread = MachineRowBlob.Read(MachineRowBlob.Write(cell), cell.Origin, vacuum);
        Assert.Equal(0L, reread.Ports[MachineTick.OnSignalPort]);
    }

    // ---------------------------------------------------------------- the loader (decision 6; the re-read's choices 5, 6)

    private const string Mod = """{ "id": "machines", "name": "Machines", "version": "1" }""";

    private const string ChannelPort = """ "channel": { "default": 0, "min": 0, "max": 15 } """;

    private const string RadiusPort = """, "radius": { "default": 1, "min": 0, "max": 4 } """;

    private static string SensorJson(string senses = "\"senses\": \"collapse\",", string links = "", string ports = ChannelPort + RadiusPort) => $$"""
        { "id": "sensor", "hardness": 3.0, "unit": "cells",
          "machine": { "kind": "sensor", {{senses}} {{links}}
            "ports": { {{ports}} } } }
        """;

    private static (GameContent Content, LoadResult Result) Load(params (string File, string Json)[] files)
    {
        InMemoryContentSource source = TestContent.BaseGame()
            .Add("machines", "mod.json", Mod)
            .Add("overburden", "extras.json", MachineExtrasTests.QuantumRow);
        foreach ((string file, string json) in files)
        {
            source.Add("machines", $"materials/{file}", json);
        }

        var content = new GameContent();
        return (content, content.Load(source));
    }

    [Fact]
    public void ASensorLoadsWithItsChannelAndRadius()
    {
        (GameContent content, LoadResult result) = Load(("sensor.json", SensorJson()));

        Assert.True(result.Success, string.Join("; ", result.Errors));
        MachineDefinition sensor = content.Materials.Get(Identifier.Parse("machines:sensor")).Machine!;
        Assert.Equal(MachineKind.Sensor, sensor.Kind);
        Assert.Equal(MachineDefinition.SensesCollapse, sensor.Senses);
        Assert.Equal(new PortDefinition(MachineDefinition.RadiusPort, 1, 0, 4), sensor.Port(MachineDefinition.RadiusPort));
        Assert.Empty(sensor.Links);
    }

    [Fact]
    public void ASensorMayDeclareANarrowerRadius()
    {
        (_, LoadResult result) = Load(("sensor.json", SensorJson(ports: ChannelPort + """, "radius": { "default": 0, "min": 0, "max": 2 } """)));

        Assert.True(result.Success, string.Join("; ", result.Errors));
    }

    public static TheoryData<string, string> EveryRefusedSensor => new()
    {
        { SensorJson(senses: ""), "a sensor names what it senses" },
        { SensorJson(senses: "\"senses\": \"power\","), "IM-9" },
        { SensorJson(senses: "\"senses\": \"colapse\","), "did you mean 'collapse'?" },
        { SensorJson(senses: "\"senses\": \"water\","), "'water' is not a sense" },
        { SensorJson(links: """ "links": { "power": { "carries": "power", "role": "sink", "via": "channel", "port": "channel" } }, """), "a sensor takes no links" },
        { SensorJson(ports: RadiusPort.TrimStart(',', ' ')), "declares a 'channel' port" },
        { SensorJson(ports: ChannelPort), "'radius' port inside 0..4" },
        { SensorJson(ports: ChannelPort + """, "radius": { "default": 1, "min": 0, "max": 5 } """), "'radius' port inside 0..4" },
        { SensorJson(ports: ChannelPort + """, "radius": { "default": 0, "min": -1, "max": 4 } """), "'radius' port inside 0..4" },
    };

    [Theory]
    [MemberData(nameof(EveryRefusedSensor))]
    public void ASensorIsRefusedNamingWhatIsWrong(string json, string expected)
    {
        (GameContent content, LoadResult result) = Load(("sensor.json", json));

        string error = Assert.Single(result.Errors);
        Assert.Contains("sensor.json", error);
        Assert.Contains(expected, error);
        Assert.False(content.Materials.Contains(Identifier.Parse("machines:sensor")));
    }

    [Fact]
    public void SensesOnAnotherKindIsRefused()
    {
        (_, LoadResult result) = Load(("pump.json", """
            { "id": "pump", "hardness": 3.0, "unit": "cells",
              "machine": { "kind": "pump", "litresPerSecond": 100, "senses": "collapse",
                "links": { "water": { "carries": "fluid", "role": "source", "via": "channel", "port": "channel" } },
                "ports": { "channel": { "default": 0, "min": 0, "max": 15 } } } }
            """));

        Assert.Contains(result.Errors, e => e.Contains("pump.json") && e.Contains("senses belongs to a sensor"));
    }

    [Theory]
    [InlineData("outlet", """ "water": { "carries": "fluid", "role": "sink", "via": "channel", "port": "channel" } """, "")]
    [InlineData("converter", """ "sky": { "carries": "power", "role": "sink", "via": "solar" }, "out": { "carries": "power", "role": "source", "via": "channel", "port": "channel" } """, """ "ratedWatts": 500, """)]
    public void TheSenseExtraIsRefusedOnAKindTheTickDoesNotRead(string kind, string links, string extra)
    {
        (_, LoadResult result) = Load(($"{kind}.json", $$"""
            { "id": "{{kind}}", "hardness": 3.0, "unit": "cells",
              "machine": { "kind": "{{kind}}", {{extra}} "extras": { "sense": {} },
                "links": { {{links}} },
                "ports": { "channel": { "default": 0, "min": 0, "max": 15 }, "onsignal": { "default": 0, "min": 0, "max": 2 } } } }
            """));

        Assert.Contains(result.Errors, e => e.Contains($"{kind}.json") && e.Contains("is read on a pump or a placed storage"));
    }

    [Fact]
    public void SenseIsServedFromF1OnAPumpAndAPlacedCell()
    {
        Assert.Equal(MachineExtra.Sense, MachineExtras.Served & MachineExtra.Sense);

        (GameContent content, LoadResult result) = Load(
            ("pump.json", """
                { "id": "pump", "hardness": 3.0, "unit": "cells",
                  "machine": { "kind": "pump", "litresPerSecond": 100, "extras": { "sense": {} },
                    "links": { "water": { "carries": "fluid", "role": "source", "via": "channel", "port": "channel" } },
                    "ports": { "channel": { "default": 0, "min": 0, "max": 15 }, "onsignal": { "default": 0, "min": 0, "max": 2 } } } }
                """),
            ("cell.json", """
                { "id": "cell", "hardness": 3.0, "unit": "cells",
                  "machine": { "kind": "storage", "capacityJoules": 1000, "extras": { "sense": {} },
                    "links": { "charge": { "carries": "power", "role": "sink", "via": "channel", "port": "channel" } },
                    "ports": { "channel": { "default": 0, "min": 0, "max": 15 }, "onsignal": { "default": 0, "min": 0, "max": 2 } } } }
                """));

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(ExtraForm.Switch, content.Materials.Get(Identifier.Parse("machines:pump")).Machine!.FormOf(MachineExtra.Sense));
        Assert.Equal(ExtraForm.Switch, content.Materials.Get(Identifier.Parse("machines:cell")).Machine!.FormOf(MachineExtra.Sense));
    }

    [Fact]
    public void TheShippedReadersCarryTheSenseExtraAndOnSignalAtZero()
    {
        var content = new GameContent();
        LoadResult result = content.Load(new DirectoryContentSource(ShippedContentTests.ModsDirectory()));
        Assert.True(result.Success, string.Join("; ", result.Errors));

        foreach (string id in new[] { "relay_pump", "vacuum_cell" })
        {
            MachineDefinition machine = content.Materials.Get(Identifier.Parse($"overburden:{id}")).Machine!;
            Assert.Equal(ExtraForm.Switch, machine.FormOf(MachineExtra.Sense));
            Assert.Equal(new PortDefinition(MachineTick.OnSignalPort, 0, 0, 2), machine.Port(MachineTick.OnSignalPort));
        }

        // No shipped sensor until f-2 seeds one with its texture.
        Assert.DoesNotContain(content.Materials.Entries(), e => e.Value.Machine is { Kind: MachineKind.Sensor });
    }
}
