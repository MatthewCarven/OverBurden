using Overburden.Core.Building;
using Overburden.Core.Content;
using Overburden.Core.Fluid;
using Overburden.Core.Inventory;
using Overburden.Core.Machines;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// IM-6a, decision 2 (IDEAL-MACHINES-PLAN, "IM-6 — the code-level pass"): the runway from each channel's
/// flows, never from the bank's change. The tick keeps each boundary's terms per channel where it counts the
/// global ones, the bank's draw counts itself, and the runway reads twenty boundaries of in and out against
/// the bank. A light rig — cells, panels, vaults and a window, no water; the pumps' term is in
/// <see cref="MachineTickTests"/> and the real siphon's in <see cref="SiphonTests"/>.
/// </summary>
public class RunwayTests
{
    private const int Air = 0, CellMaterial = 1, PanelMaterial = 2, WindowMaterial = 3, VaultMaterial = 4, Stone = 5;

    private static readonly PortDefinition Channel = new("channel", 0, 0, 15);
    private static readonly PortDefinition Enabled = new(MachineTick.EnabledPort, 1, 0, 1);

    private static readonly MachineDefinition CellDefinition = new()
    {
        Kind = MachineKind.Storage,
        CapacityMillijoules = 1_000_000_000_000_000L,
        Links = [new MachineLink("charge", MachineMedium.Power, MachineRole.Sink, MachineLink.ChannelVia, null, "channel")],
        Ports = [Channel, Enabled],
    };

    private static readonly MachineDefinition PanelDefinition = new()
    {
        Kind = MachineKind.Converter,
        RatedWatts = 500,
        Links =
        [
            new MachineLink("sky", MachineMedium.Power, MachineRole.Sink, MachineDefinition.SolarVia, null, null),
            new MachineLink("out", MachineMedium.Power, MachineRole.Source, MachineLink.ChannelVia, null, "channel"),
        ],
        Ports = [Channel],
    };

    // 10 m³ at the low grade's rates, so a few hundred voxels bill more than a dim panel makes.
    private static readonly MachineDefinition VaultDefinition = new()
    {
        Kind = MachineKind.Vault,
        InsideMillilitres = 10 * MachineDefinition.MillilitresPerCubicMetre,
        WaterHoldMilliwattsPerCubicMetre = 25_000,
        BlockHoldMilliwattsPerCubicMetre = 2_500,
        HoldsWater = true,
        Links = [new MachineLink("power", MachineMedium.Power, MachineRole.Sink, MachineLink.ChannelVia, null, "channel")],
        Ports = [Channel, Enabled],
    };

    private static readonly MaterialTable Materials = MaterialTable.ForTesting(
        //            air    cell   panel  window vault  stone
        isOpaque: [false, true, true, true, true, true],
        isFluid: [false, false, false, false, false, false],
        textureLayer: [0, 1, 2, 3, 4, 5],
        hardness: [0f, 3f, 1f, 1f, 3f, 3f],
        machine: [null, CellDefinition, PanelDefinition, WindowTests.WindowDefinition, VaultDefinition, null],
        placeable: [true, true, true, false, true, true]);

    private const long Boundary = MachineTick.DefaultBoundaryMillis;

    private sealed class NoWater : IFluidTerrain
    {
        public bool CanHold(VoxelPos voxel) => false;
    }

    private sealed class Sky : ISolarSky
    {
        public int Open { get; set; } = 16;

        public bool TryOpenColumnsAbove(BrushCell cell, out int open)
        {
            open = Open;
            return true;
        }
    }

    private sealed class Rig
    {
        public Rig()
        {
            var terrain = new NoWater();
            Machines = new MachineSet(Materials);
            Tick = new MachineTick(Machines, Materials, new FluidSimulation(terrain), terrain) { Sky = SkyAbove, FuseSeed = 0x6A };
            Pocket.Add(Stone, 1_000_000);
        }

        public MachineSet Machines { get; }

        public MachineTick Tick { get; }

        public Sky SkyAbove { get; } = new();

        public VolumetricInventory Pocket { get; } = new();

        /// <summary>A row as a restore leaves it, its charge or contents written before it enters.</summary>
        public MachineRow Place(BrushCell cell, int material, int channel, long charge = 0, long stone = 0)
        {
            var row = new MachineRow(cell, material, BrushFacing.PositiveX, Materials.Machine(material)!.DefaultPorts());
            row.SetPortUnchecked("channel", channel);
            if (charge > 0)
            {
                StorageState.Write(row, charge);
            }

            if (stone > 0)
            {
                var contents = new VaultContents();
                contents.Add(Stone, stone);
                VaultState.Write(row, contents);
            }

            Machines.Add(row);
            return row;
        }

        public EquippedPack Wear(int channel)
        {
            var pack = new EquippedPack(WindowMaterial, WindowTests.WindowDefinition, 0, channel);
            Tick.EquipPack(pack);
            return pack;
        }

        public VaultContents Contents(MachineRow row)
        {
            Assert.True(VaultState.TryRead(row, out VaultContents contents));
            return contents;
        }
    }

    private static long Solar(int open, byte overcast = 0) => MachineTick.SolarMillijoules(PanelDefinition, open, overcast, Boundary);

    private static long BillOf(VaultContents contents) => MachineTick.BillOf(VaultDefinition, contents, Boundary);

    /// <summary>
    /// Decision 2's identity: each per-channel term, over the sixteen channels, is the report's global term.
    /// Shared with the water rigs in <see cref="MachineTickTests"/>.
    /// </summary>
    internal static void AssertFlowsSum(MachineTick tick, MachineTickReport report)
    {
        long generated = 0, fromPool = 0, fromOwn = 0, pumps = 0, given = 0, returned = 0;
        for (int channel = 0; channel < MachineTick.Channels; channel++)
        {
            ChannelFlows flows = tick.FlowsOn(channel);
            generated += flows.GeneratedMillijoules;
            fromPool += flows.HeldFromPoolMillijoules;
            fromOwn += flows.HeldFromOwnMillijoules;
            pumps += flows.PumpsDrewMillijoules;
            given += flows.OwnGivenMillijoules;
            returned += flows.OwnReturnedMillijoules;
        }

        Assert.Equal(report.GeneratedMillijoules, generated);
        Assert.Equal(report.HeldMillijoules, fromPool + fromOwn);
        Assert.Equal(report.HeldFromOwnMillijoules, fromOwn);
        Assert.Equal(report.DrawnMillijoules, pumps);
        Assert.Equal(report.OwnGivenMillijoules, given);
        Assert.Equal(report.OwnReturnedMillijoules, returned);
    }

    // ---------------------------------------------------------------- the ledger

    [Fact]
    public void EachChannelsTermsSumToTheGlobalOnesAndLandOnTheirOwnChannelOverRandomBoundaries()
    {
        // Four channels, each a cell, a panel and a vault, under a random sky, with the window, the siphon,
        // deposits, withdrawals and Alt+P between boundaries. Every term must sum to its global twin, and each
        // must land where it happened: the generation by the panels on the channel, the bills by its vaults
        // that paid, the draws by what was drawn on it.
        var rig = new Rig();
        var random = new Random(0x6A);
        var cells = new MachineRow[4];
        var vaults = new MachineRow[4];
        for (int c = 0; c < 4; c++)
        {
            cells[c] = rig.Place(new BrushCell(c, 0, 0), CellMaterial, c, charge: random.Next(4) == 0 ? 0 : random.NextInt64(2_000_000_000));
            rig.Place(new BrushCell(c, 1, 0), PanelMaterial, c);
            vaults[c] = rig.Place(new BrushCell(c, 2, 0), VaultMaterial, c, stone: random.Next(64, 640));
        }

        EquippedPack window = rig.Wear(0);
        bool sawOwnPaid = false, sawReturned = false, sawGiven = false;
        int channelsDrawnOn = 0;
        var drawnOnEver = new bool[MachineTick.Channels];
        for (int boundary = 0; boundary < 400; boundary++)
        {
            rig.SkyAbove.Open = random.Next(17);
            rig.Tick.OvercastByte = (byte)random.Next(256);
            var drawn = new long[MachineTick.Channels];
            if (random.Next(3) == 0)
            {
                window.Channel = random.Next(4);
                drawn[window.Channel] += window.Take(random.NextInt64(50_000_000));
            }

            if (random.Next(3) == 0)
            {
                int c = random.Next(5);
                drawn[c] += rig.Tick.PaySiphon(c, random.NextInt64(50_000_000));
            }

            if (random.Next(4) == 0)
            {
                rig.Machines.Deposit(random.Next(4), Stone, random.Next(200), rig.Pocket);
            }

            if (random.Next(4) == 0)
            {
                rig.Machines.Withdraw(random.Next(4), Stone, random.Next(200), rig.Pocket);
            }

            if (random.Next(10) == 0)
            {
                rig.Machines.SetCharge(cells[random.Next(4)].Cell, random.NextInt64(1_000_000_000));
            }

            var bills = new long[MachineTick.Channels];
            var before = new VaultContents[4];
            for (int c = 0; c < 4; c++)
            {
                before[c] = rig.Contents(vaults[c]);
            }

            MachineTickReport report = rig.Tick.Run();

            AssertFlowsSum(rig.Tick, report);
            for (int c = 0; c < 4; c++)
            {
                if (vaults[c].LastHoldPaid)
                {
                    bills[c] += BillOf(before[c]);
                }
            }

            for (int channel = 0; channel < MachineTick.Channels; channel++)
            {
                ChannelFlows flows = rig.Tick.FlowsOn(channel);
                long panels = channel < 4 ? 1 : 0;
                Assert.Equal(panels * Solar(rig.SkyAbove.Open, rig.Tick.OvercastByte), flows.GeneratedMillijoules);
                Assert.Equal(bills[channel], flows.HeldFromPoolMillijoules + flows.HeldFromOwnMillijoules);
                Assert.Equal(drawn[channel], flows.DrawnMillijoules);
                Assert.Equal(0, flows.PumpsDrewMillijoules);
                if (drawn[channel] > 0 && !drawnOnEver[channel])
                {
                    drawnOnEver[channel] = true;
                    channelsDrawnOn++;
                }

                sawOwnPaid |= flows.HeldFromOwnMillijoules > 0;
                sawReturned |= flows.OwnReturnedMillijoules > 0;
                sawGiven |= flows.OwnGivenMillijoules > 0;
            }

            Assert.True(rig.Tick.PowerExact, $"boundary {boundary}: the pool identity broke");
            Assert.Equal(rig.Tick.PackTakenMillijoules + rig.Tick.SiphonPaidMillijoules, rig.Tick.BankDrawnMillijoules);
        }

        Assert.True(sawOwnPaid, "no bill was ever paid from an own charge");
        Assert.True(sawReturned, "no own charge was ever returned");
        Assert.True(sawGiven, "no own charge was ever given");
        Assert.True(channelsDrawnOn >= 3, $"draws landed on {channelsDrawnOn} channel(s)");
    }

    [Fact]
    public void ADrawCountsOnItsOwnChannelAtTheNextBoundaryAndOnlyThatOne()
    {
        var rig = new Rig();
        rig.Place(new BrushCell(0, 0, 0), CellMaterial, 2, charge: 10_000_000_000);
        rig.Place(new BrushCell(1, 0, 0), CellMaterial, 6, charge: 10_000_000_000);
        EquippedPack window = rig.Wear(2);
        rig.Tick.Run();

        Assert.Equal(1_000_000, window.Take(1_000_000));
        Assert.Equal(2_000_000, rig.Tick.PaySiphon(6, 2_000_000));
        Assert.Equal(0, rig.Tick.PaySiphon(9, 5_000_000)); // an empty bank: nothing drawn, nothing counted

        // Drawn between boundaries, so the boundary after them is theirs, not the one before.
        Assert.Equal(0, rig.Tick.FlowsOn(2).DrawnMillijoules);
        rig.Tick.Run();
        Assert.Equal(1_000_000, rig.Tick.FlowsOn(2).DrawnMillijoules);
        Assert.Equal(2_000_000, rig.Tick.FlowsOn(6).DrawnMillijoules);
        Assert.Equal(1_000_000, rig.Tick.FlowsOn(2).Out);
        for (int channel = 0; channel < MachineTick.Channels; channel++)
        {
            if (channel is not 2 and not 6)
            {
                Assert.Equal(default, rig.Tick.FlowsOn(channel));
            }
        }

        // And never again: the next boundary drew nothing.
        rig.Tick.Run();
        Assert.Equal(0, rig.Tick.FlowsOn(2).DrawnMillijoules);
        Assert.Equal(0, rig.Tick.FlowsOn(6).DrawnMillijoules);
        Assert.Equal(3_000_000, rig.Tick.BankDrawnMillijoules);
        Assert.True(rig.Tick.PowerExact);
    }

    [Fact]
    public void AltPAPickedCellAndACellPlacedLeaveTheFlowsAndTheRatesAlone()
    {
        // A dim panel and a vault that bills more: a channel draining at a steady rate. Alt+P (entering), a
        // cell picked up (leaving) and a charged cell placed (entering) each move the bank and nothing else.
        var rig = new Rig();
        rig.SkyAbove.Open = 1;
        rig.Tick.OvercastByte = 128;
        rig.Place(new BrushCell(0, 0, 0), CellMaterial, 3, charge: 1_000_000_000_000);
        MachineRow spare = rig.Place(new BrushCell(1, 0, 0), CellMaterial, 3, charge: 5_000_000_000);
        rig.Place(new BrushCell(2, 0, 0), PanelMaterial, 3);
        MachineRow vault = rig.Place(new BrushCell(3, 0, 0), VaultMaterial, 3, stone: 640);
        long bill = BillOf(rig.Contents(vault));
        long solar = Solar(1, 128);
        Assert.True(bill > solar, $"the fixture does not drain: bill {bill} mJ, sun {solar} mJ");

        var runway = new Runway();
        for (int i = 0; i < Runway.Boundaries; i++)
        {
            rig.Tick.Run();
            runway.Record(rig.Tick);
        }

        ChannelFlows steady = rig.Tick.FlowsOn(3);
        Assert.Equal(new ChannelFlows(solar, bill, 0, 0, 0, 0, 0), steady);
        RunwayReading before = runway.Read(3);
        Assert.Equal(solar * 1000 / Boundary, before.InMilliwatts);
        Assert.Equal(bill * 1000 / Boundary, before.OutMilliwatts);

        void Step(string what)
        {
            rig.Tick.Run();
            runway.Record(rig.Tick);
            Assert.Equal(steady, rig.Tick.FlowsOn(3));
            RunwayReading after = runway.Read(3);
            Assert.True(before.InMilliwatts == after.InMilliwatts && before.OutMilliwatts == after.OutMilliwatts, $"{what} moved the rates: {before} then {after}");
            Assert.Equal(rig.Tick.BankOf(3), after.BankMillijoules);
            Assert.Equal(after.BankMillijoules * Runway.Boundaries * Boundary / (Runway.Boundaries * (bill - solar)), after.RunwayMillis);
            Assert.True(rig.Tick.PowerExact);
        }

        rig.Machines.SetCharge(spare.Cell, 900_000_000_000);
        Step("Alt+P");
        Assert.True(rig.Machines.Remove(spare.Cell));
        Step("a picked cell");
        rig.Place(new BrushCell(4, 0, 0), CellMaterial, 3, charge: 77_000_000_000);
        Step("a cell placed");
    }

    [Fact]
    public void ADepositsRechargeAndAWithdrawalsReturnAreNotFlowsOfTheRunway()
    {
        var rig = new Rig();
        rig.Place(new BrushCell(0, 0, 0), CellMaterial, 5, charge: 1_000_000_000_000);
        rig.Place(new BrushCell(1, 0, 0), PanelMaterial, 5);
        MachineRow vault = rig.Place(new BrushCell(2, 0, 0), VaultMaterial, 5);
        rig.Tick.Run();
        long solar = Solar(16);

        // A deposit raises the own charge's capacity; the next boundary's spare fills it. Given, not out.
        Assert.Equal(320, rig.Machines.Deposit(5, Stone, 320, rig.Pocket));
        VaultContents filled = rig.Contents(vault);
        long bill = BillOf(filled);
        long minute = MachineTick.OwnChargeCapacity(VaultDefinition, filled);
        rig.Tick.Run();
        ChannelFlows deposit = rig.Tick.FlowsOn(5);
        Assert.Equal(minute, deposit.OwnGivenMillijoules);
        Assert.True(minute > 0);
        Assert.Equal(bill, deposit.HeldFromPoolMillijoules);
        Assert.Equal(bill, deposit.Out);
        Assert.Equal(solar, deposit.In);
        var afterDeposit = new Runway();
        afterDeposit.Record(rig.Tick);
        Assert.Equal(bill * 1000 / Boundary, afterDeposit.Read(5).OutMilliwatts);

        // A withdrawal lowers it to nothing; the next boundary returns the minute before the bills. Returned, not in.
        long own = rig.Contents(vault).OwnChargeMillijoules;
        Assert.Equal(minute, own);
        Assert.Equal(320, rig.Machines.Withdraw(5, Stone, 320, rig.Pocket));
        rig.Tick.Run();
        ChannelFlows withdrawal = rig.Tick.FlowsOn(5);
        Assert.Equal(own, withdrawal.OwnReturnedMillijoules);
        Assert.Equal(solar, withdrawal.In);
        Assert.Equal(0, withdrawal.Out);
        var afterWithdrawal = new Runway();
        afterWithdrawal.Record(rig.Tick);
        Assert.Equal(solar * 1000 / Boundary, afterWithdrawal.Read(5).InMilliwatts);
        Assert.Null(afterWithdrawal.Read(5).RunwayMillis);
        Assert.True(rig.Tick.PowerExact);
    }

    [Fact]
    public void ABillPaidFromTheVaultsOwnMinuteIsNotOutflow()
    {
        // The cell holds one bill and one minute exactly, under a dark sky: the first boundary pays the bill
        // from the pool and gives the minute (given, not out), leaving the bank empty; the second pays the bill
        // from the minute, which was counted out of the bank when it was given.
        var rig = new Rig();
        rig.SkyAbove.Open = 0;
        MachineRow vault = rig.Place(new BrushCell(2, 0, 0), VaultMaterial, 7, stone: 640);
        VaultContents contents = rig.Contents(vault);
        long bill = BillOf(contents), minute = MachineTick.OwnChargeCapacity(VaultDefinition, contents);
        rig.Place(new BrushCell(0, 0, 0), CellMaterial, 7, charge: bill + minute);

        rig.Tick.Run();
        Assert.Equal(new ChannelFlows(0, bill, 0, 0, minute, 0, 0), rig.Tick.FlowsOn(7));
        Assert.Equal(bill, rig.Tick.FlowsOn(7).Out);
        Assert.Equal(0, rig.Tick.BankOf(7));

        rig.Tick.Run();
        Assert.True(vault.LastHoldPaid);
        Assert.Equal(new ChannelFlows(0, 0, bill, 0, 0, 0, 0), rig.Tick.FlowsOn(7));
        Assert.Equal(0, rig.Tick.FlowsOn(7).Out);
        Assert.True(rig.Tick.PowerExact);
    }

    [Fact]
    public void UnderPowerOffOnlyTheDrawsAreFlows()
    {
        var rig = new Rig();
        rig.Place(new BrushCell(0, 0, 0), CellMaterial, 1, charge: 10_000_000_000);
        rig.Place(new BrushCell(1, 0, 0), PanelMaterial, 1);
        rig.Place(new BrushCell(2, 0, 0), VaultMaterial, 1, stone: 100);
        EquippedPack window = rig.Wear(1);
        rig.Tick.PowerOff = true;

        window.Take(4_000);
        rig.Tick.Run();

        Assert.Equal(new ChannelFlows(0, 0, 0, 0, 0, 0, 4_000), rig.Tick.FlowsOn(1));
    }

    // ---------------------------------------------------------------- the runway

    private static void Record(Runway runway, int channel, long inflow, long outflow, long bank)
    {
        var flows = new ChannelFlows[MachineTick.Channels];
        var banks = new long[MachineTick.Channels];
        flows[channel] = new ChannelFlows(inflow, outflow, 0, 0, 0, 0, 0);
        banks[channel] = bank;
        runway.Record(flows, banks);
    }

    [Fact]
    public void TheRunwayReadsTheLastTwentyBoundariesAndNoMore()
    {
        var runway = new Runway();
        Assert.Equal(new RunwayReading(0, 0, 0, null), runway.Read(7));

        // One boundary: a window of 500 ms.
        Record(runway, 7, 0, 1_000, 1_000_000);
        Assert.Equal(1, runway.Recorded);
        Assert.Equal(new RunwayReading(1_000_000, 0, 2_000, 1_000_000L * 500 / 1_000), runway.Read(7));

        for (int i = 1; i < Runway.Boundaries; i++)
        {
            Record(runway, 7, 0, 1_000, 1_000_000);
        }

        Assert.Equal(Runway.Boundaries, runway.Recorded);
        Assert.Equal(new RunwayReading(1_000_000, 0, 2_000, 1_000_000L * 10_000 / 20_000), runway.Read(7));

        // Nineteen at three times the rate: one of the old boundaries is still in the window.
        for (int i = 0; i < Runway.Boundaries - 1; i++)
        {
            Record(runway, 7, 0, 3_000, 1_000_000);
        }

        Assert.Equal(new RunwayReading(1_000_000, 0, 5_800, 1_000_000L * 10_000 / 58_000), runway.Read(7));

        // The twentieth pushes it out.
        Record(runway, 7, 0, 3_000, 1_000_000);
        Assert.Equal(Runway.Boundaries, runway.Recorded);
        Assert.Equal(new RunwayReading(1_000_000, 0, 6_000, 1_000_000L * 10_000 / 60_000), runway.Read(7));

        // Every other channel recorded nothing but zeros.
        Assert.Equal(new RunwayReading(0, 0, 0, null), runway.Read(8));
        Assert.Equal(RunwayLevel.None, runway.Read(8).Level);
    }

    [Theory]
    [InlineData(600_000L, RunwayLevel.Plain)]
    [InlineData(599_999L, RunwayLevel.Amber)]
    [InlineData(60_000L, RunwayLevel.Amber)]
    [InlineData(59_999L, RunwayLevel.Red)]
    [InlineData(1L, RunwayLevel.Red)]
    [InlineData(0L, RunwayLevel.Red)]
    public void TheRunwayIsAmberUnderTenMinutesAndRedUnderAMinute(long bank, RunwayLevel level)
    {
        // One boundary of 500 ms with a net drain of 500 mJ: a millijoule a millisecond, so the runway in
        // milliseconds is the bank in millijoules.
        var runway = new Runway();
        Record(runway, 4, 100, 600, bank);

        RunwayReading reading = runway.Read(4);

        Assert.Equal(bank, reading.RunwayMillis);
        Assert.Equal(level, reading.Level);
        Assert.Equal(-1_000, reading.NetMilliwatts); // 500 mJ over 500 ms is a watt

    }

    [Theory]
    [InlineData(600L, 600L)]
    [InlineData(700L, 600L)]
    public void ABankThatHoldsOrGrowsHasNoRunway(long inflow, long outflow)
    {
        var runway = new Runway();
        Record(runway, 0, inflow, outflow, 0);

        RunwayReading reading = runway.Read(0);

        Assert.Null(reading.RunwayMillis);
        Assert.Equal(RunwayLevel.None, reading.Level);
    }

    [Fact]
    public void TheRatesReadAsPowerOverTheWindow()
    {
        // His line: +3.8 kW sun, −104 kW. A boundary is 500 ms, so 3.8 kW is 1.9 kJ a boundary and 104 kW 52 kJ.
        var runway = new Runway();
        for (int i = 0; i < Runway.Boundaries; i++)
        {
            Record(runway, 0, 1_900_000, 52_000_000, 612_000_000_000);
        }

        RunwayReading reading = runway.Read(0);

        Assert.Equal(3_800_000, reading.InMilliwatts);
        Assert.Equal(104_000_000, reading.OutMilliwatts);
        Assert.Equal("3.8 kW", Culture.Invariant(() => PowerText.OfMilliwatts(reading.InMilliwatts)));
        Assert.Equal("104 kW", Culture.Invariant(() => PowerText.OfMilliwatts(reading.OutMilliwatts)));
        Assert.Equal("612.0 MJ", Culture.Invariant(() => EnergyText.Of(reading.BankMillijoules)));

        // 612 MJ over a 100.2 kW net drain: 6,107.78 s, an hour and forty-one minutes.
        Assert.Equal(612_000_000_000L * 10_000 / (20 * (52_000_000L - 1_900_000)), reading.RunwayMillis);
        Assert.Equal(RunwayLevel.Plain, reading.Level);
    }

    [Fact]
    public void ARunwayReadsTheTicksFlowsAndBankAndRefusesAnotherBoundary()
    {
        var rig = new Rig();
        rig.SkyAbove.Open = 1;
        rig.Tick.OvercastByte = 200;
        rig.Place(new BrushCell(0, 0, 0), CellMaterial, 1, charge: 50_000_000_000);
        rig.Place(new BrushCell(1, 0, 0), PanelMaterial, 1);
        MachineRow vault = rig.Place(new BrushCell(2, 0, 0), VaultMaterial, 1, stone: 640);
        long bill = BillOf(rig.Contents(vault)), solar = Solar(1, 200);
        Assert.True(bill > solar, $"the fixture does not drain: bill {bill} mJ, sun {solar} mJ");
        var runway = new Runway();
        for (int i = 0; i < 25; i++)
        {
            rig.Tick.Run();
            runway.Record(rig.Tick);
        }

        RunwayReading reading = runway.Read(1);

        Assert.Equal(rig.Tick.BankOf(1), reading.BankMillijoules);
        Assert.Equal(solar * 1000 / Boundary, reading.InMilliwatts);
        Assert.Equal(bill * 1000 / Boundary, reading.OutMilliwatts);
        Assert.Equal(reading.BankMillijoules * 10_000 / (20 * (bill - solar)), reading.RunwayMillis);
        Assert.Throws<ArgumentException>(() => new Runway(1_000).Record(rig.Tick));
    }
}

/// <summary>Formats under the invariant culture, as <see cref="VolumeTextTests"/> does.</summary>
internal static class Culture
{
    public static string Invariant(Func<string> format) => Under(System.Globalization.CultureInfo.InvariantCulture, format);

    public static string Under(System.Globalization.CultureInfo culture, Func<string> format)
    {
        System.Globalization.CultureInfo before = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = culture;
        try
        {
            return format();
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = before;
        }
    }
}
