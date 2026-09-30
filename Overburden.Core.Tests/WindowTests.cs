using Overburden.Core.Building;
using Overburden.Core.Content;
using Overburden.Core.Fluid;
using Overburden.Core.Inventory;
using Overburden.Core.Machines;
using Overburden.Core.Persistence;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// IM-2: the quantum backpack as a window (IDEAL-MACHINES-PLAN decisions 7 and 8) and the
/// consumers' order (decisions 9 and 41). A light rig — cells, a panel, packs, pumps with no water —
/// because the window never touches water; the water-side tests are in <see cref="MachineTickTests"/>.
/// </summary>
public class WindowTests
{
    private const int Air = 0, BigCell = 1, SmallCell = 2, Panel = 3, Window = 4, LeadAcid = 5, Pump = 6, PriorityPump = 7;

    private const long BigCapacity = 1_000_000_000L;   // 1 MJ
    private const long SmallCapacity = 100_000_000L;   // 100 kJ
    private const long LeadAcidCapacity = 3_600_000_000L;
    private const long Cost = 50_000;                  // the quantum tools' 50 J a voxel

    private static readonly MachineLink Charge = new("charge", MachineMedium.Power, MachineRole.Sink, MachineLink.ChannelVia, null, "channel");
    private static readonly PortDefinition Channel = new("channel", 0, 0, 15);

    private static MachineDefinition CellOf(long capacity) => new()
    {
        Kind = MachineKind.Storage,
        CapacityMillijoules = capacity,
        Links = [Charge],
        Ports = [Channel, new PortDefinition(MachineTick.EnabledPort, 1, 0, 1)],
    };

    /// <summary>Decision 7's shape: carried, the charge extra in the window form, no capacity, no recharge.</summary>
    internal static readonly MachineDefinition WindowDefinition = new()
    {
        Kind = MachineKind.Storage,
        Carried = true,
        Links = [Charge],
        Ports = [Channel],
        ExtraForms = new Dictionary<MachineExtra, ExtraForm> { [MachineExtra.Charge] = ExtraForm.Window },
    };

    private static MachineDefinition PumpOf(bool priority) => new()
    {
        Kind = MachineKind.Pump,
        MillilitresPerSecond = 100_000,
        JoulesPerLitre = 1000,
        Links = [new MachineLink("water", MachineMedium.Fluid, MachineRole.Source, MachineLink.ChannelVia, null, "channel")],
        Ports = priority ? [Channel, new PortDefinition(MachineTick.PriorityPort, 5, 0, 9)] : [Channel],
        ExtraForms = priority
            ? new Dictionary<MachineExtra, ExtraForm> { [MachineExtra.Priority] = ExtraForm.Port }
            : new Dictionary<MachineExtra, ExtraForm>(),
    };

    private static readonly MaterialTable Materials = MaterialTable.ForTesting(
        //            air    big    small  panel  window lead   pump   prio
        isOpaque: [false, true, true, true, true, true, true, true],
        isFluid: [false, false, false, false, false, false, false, false],
        textureLayer: [0, 1, 2, 3, 4, 5, 6, 7],
        hardness: [0f, 3f, 3f, 1f, 1f, 1f, 3f, 3f],
        machine:
        [
            null, CellOf(BigCapacity), CellOf(SmallCapacity),
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
            WindowDefinition,
            new MachineDefinition { Kind = MachineKind.Storage, Carried = true, CapacityMillijoules = LeadAcidCapacity, RechargeWatts = 250, Links = [Charge], Ports = [Channel] },
            PumpOf(priority: false), PumpOf(priority: true),
        ],
        placeable: [true, true, true, true, false, false, true, true]);

    private sealed class NoWater : IFluidTerrain
    {
        public bool CanHold(VoxelPos voxel) => false;
    }

    private sealed class Sky : ISolarSky
    {
        public Action? During { get; set; }

        public bool TryOpenColumnsAbove(BrushCell cell, out int open)
        {
            During?.Invoke();
            open = 16;
            return true;
        }
    }

    private sealed class Rig
    {
        public Rig()
        {
            var terrain = new NoWater();
            Machines = new MachineSet(Materials);
            Tick = new MachineTick(Machines, Materials, new FluidSimulation(terrain), terrain) { Sky = SkyAbove };
        }

        public MachineSet Machines { get; }

        public MachineTick Tick { get; }

        public Sky SkyAbove { get; } = new();

        /// <summary>A row as a restore would leave it: its charge written before it enters, so <c>entering</c> counts it.</summary>
        public MachineRow Place(BrushCell cell, int material, int channel = 0, long charge = 0, long? priority = null)
        {
            var row = new MachineRow(cell, material, BrushFacing.PositiveX, Materials.Machine(material)!.DefaultPorts());
            row.SetPortUnchecked("channel", channel);
            if (priority is { } p)
            {
                row.SetPortUnchecked(MachineTick.PriorityPort, p);
            }

            if (charge > 0)
            {
                StorageState.Write(row, charge);
            }

            Machines.Add(row);
            return row;
        }

        public EquippedPack Wear(int material, int channel = 0, long charge = 0)
        {
            var pack = new EquippedPack(material, Materials.Machine(material)!, charge, channel);
            Tick.EquipPack(pack);
            return pack;
        }
    }

    private static long ChargeOf(MachineRow row) => StorageState.Read(row);

    // ---------------------------------------------------------------- the window (decisions 7, 8)

    [Fact]
    public void AWindowHoldsNothingAndSpendsItsChannelsCells()
    {
        var rig = new Rig();
        MachineRow cell = rig.Place(new BrushCell(0, 0, 0), BigCell, charge: 400_000_000);
        EquippedPack window = rig.Wear(Window);

        Assert.True(window.IsWindow);
        Assert.Equal(0, window.CapacityMillijoules);
        Assert.Equal(0, window.ChargeMillijoules);
        Assert.Equal(400_000_000 / Cost, window.VoxelsAffordable(Cost));
        Assert.Equal(400_000_000, window.SpendableMillijoules);

        long paid = window.Pay(1000, Cost);

        Assert.Equal(1000 * Cost, paid);
        Assert.Equal(400_000_000 - (1000 * Cost), ChargeOf(cell));
        Assert.Equal(0, window.ChargeMillijoules);
        Assert.Equal(1000 * Cost, window.TakenMillijoules);
        Assert.Equal(1000 * Cost, rig.Tick.PackTakenMillijoules);
        Assert.True(rig.Tick.PowerExact);
    }

    [Fact]
    public void ADrawTakesTheLastFilledCellFirstAndStopsAtTheBank()
    {
        // The write-back fills cells in position order, each to capacity, so the last in that order
        // is the emptiest; the draw reads it backwards and keeps the first ones full.
        var rig = new Rig();
        MachineRow first = rig.Place(new BrushCell(0, 0, 0), SmallCell, charge: SmallCapacity);
        MachineRow second = rig.Place(new BrushCell(1, 0, 0), SmallCell, charge: 30_000_000);
        EquippedPack window = rig.Wear(Window);

        Assert.Equal(20_000_000, window.Take(20_000_000));
        Assert.Equal(SmallCapacity, ChargeOf(first));
        Assert.Equal(10_000_000, ChargeOf(second));

        Assert.Equal(40_000_000, window.Take(40_000_000));
        Assert.Equal(0, ChargeOf(second));
        Assert.Equal(SmallCapacity - 30_000_000, ChargeOf(first));

        // No floor (decision 11's "bankruptcy has no floor"): it spends to zero and stops there.
        Assert.Equal(70_000_000, window.Take(1_000_000_000));
        Assert.Equal(0, ChargeOf(first));
        Assert.Equal(0, window.Take(1));
        Assert.Equal(0, window.VoxelsAffordable(Cost));
        Assert.True(rig.Tick.PowerExact);
    }

    [Fact]
    public void AWindowSpendsOnlyItsOwnChannelsEnabledCells()
    {
        var rig = new Rig();
        MachineRow on = rig.Place(new BrushCell(0, 0, 0), SmallCell, charge: SmallCapacity);
        MachineRow off = rig.Place(new BrushCell(1, 0, 0), SmallCell, charge: SmallCapacity);
        MachineRow elsewhere = rig.Place(new BrushCell(2, 0, 0), SmallCell, channel: 3, charge: SmallCapacity);
        rig.Machines.SetPort(off.Cell, MachineTick.EnabledPort, 0);
        EquippedPack window = rig.Wear(Window);

        Assert.Equal(SmallCapacity / Cost, window.VoxelsAffordable(Cost));
        Assert.Equal(SmallCapacity, window.Take(3 * SmallCapacity));
        Assert.Equal(0, ChargeOf(on));
        Assert.Equal(SmallCapacity, ChargeOf(off));
        Assert.Equal(SmallCapacity, ChargeOf(elsewhere));

        // The panel sets the window's channel; it draws there from then on.
        window.Channel = 3;
        Assert.Equal(SmallCapacity, window.Take(SmallCapacity));
        Assert.Equal(0, ChargeOf(elsewhere));
        Assert.True(rig.Tick.PowerExact);
    }

    [Fact]
    public void TheRechargeTheDebugFillAndGiveGiveAWindowNothing()
    {
        // Decision 7: RechargePack, Alt+P's FillPack and any Give refuse a window.
        var rig = new Rig();
        rig.Place(new BrushCell(0, 0, 0), BigCell, charge: BigCapacity / 2);
        rig.Place(new BrushCell(0, 5, 0), Panel);
        EquippedPack window = rig.Wear(Window);

        Assert.Equal(0, window.Give(1_000_000));
        Assert.Equal(0, rig.Tick.FillPack(LeadAcidCapacity));
        for (int i = 0; i < 100; i++)
        {
            MachineTickReport report = rig.Tick.Run();
            Assert.Equal(0, report.PackGivenMillijoules);
        }

        Assert.Equal(0, window.ChargeMillijoules);
        Assert.Equal(0, rig.Tick.PackGivenMillijoules);
        Assert.Equal(0, rig.Tick.PackDebugMillijoules);
        Assert.True(rig.Tick.PowerExact);

        // What the header saves for the worn pack, as Main writes it: the window's charge is 0.
        var saved = new PackSave(window.MaterialId, window.ChargeMillijoules, window.Channel);
        Assert.Equal(0, saved.ChargeMillijoules);

        // And a stowed window forgets its channel, as a flat lead-acid pack does: nothing is kept.
        window.Channel = 7;
        Assert.False(new CarriedPacks().Stow(new PackSave(window.MaterialId, window.ChargeMillijoules, window.Channel)));
    }

    [Fact]
    public void AWindowNobodyWearsBuysNothingAndAnUnequippedOneLetsGoOfTheBank()
    {
        var rig = new Rig();
        MachineRow cell = rig.Place(new BrushCell(0, 0, 0), BigCell, charge: BigCapacity);
        var loose = new EquippedPack(Window, WindowDefinition, chargeMillijoules: 0, channel: 0);
        Assert.Equal(0, loose.VoxelsAffordable(Cost));
        Assert.Equal(0, loose.Take(Cost));

        EquippedPack window = rig.Wear(Window);
        Assert.Equal(Cost, window.Take(Cost));

        // A swap to the lead-acid pack: the outgoing window draws on nothing, and its taken stays
        // counted in the session's sums, so the identity holds across the swap.
        rig.Wear(LeadAcid, charge: 1_000_000);
        Assert.Equal(0, window.Take(Cost));
        Assert.Equal(0, window.VoxelsAffordable(Cost));
        Assert.Equal(BigCapacity - Cost, ChargeOf(cell));
        Assert.Equal(Cost, rig.Tick.PackTakenMillijoules);
        Assert.True(rig.Tick.PowerExact);
    }

    [Fact]
    public void ADrawInsideTheRunThrowsAndTheRunRecovers()
    {
        // Decision 8: never inside the run. The pool is summed at its start and written back at
        // its end, so a draw in between would be spent twice or lost. The sky is the one callback
        // the run makes, so it is where a draw could try.
        var rig = new Rig();
        rig.Place(new BrushCell(0, 0, 0), BigCell, charge: BigCapacity);
        rig.Place(new BrushCell(0, 5, 0), Panel);
        EquippedPack window = rig.Wear(Window);
        rig.SkyAbove.During = () => window.Take(Cost);

        var thrown = Assert.Throws<InvalidOperationException>(() => rig.Tick.Run());
        Assert.Contains("never inside the machine tick's run", thrown.Message);

        rig.SkyAbove.During = null;
        rig.Tick.Run();
        Assert.Equal(Cost, window.Take(Cost));
    }

    [Fact]
    public void TheValidatorRefusesAWindowWithACapacityOrARecharge()
    {
        MachineDefinition WindowWith(long capacity, long? recharge) => new()
        {
            Kind = MachineKind.Storage,
            Carried = true,
            CapacityMillijoules = capacity,
            RechargeWatts = recharge,
            Links = [Charge],
            Ports = [Channel],
            ExtraForms = new Dictionary<MachineExtra, ExtraForm> { [MachineExtra.Charge] = ExtraForm.Window },
        };

        Assert.True(WindowWith(0, null).TryValidate(out string? ok), ok);
        Assert.False(WindowWith(1000, null).TryValidate(out string? capacity));
        Assert.Contains("a window holds nothing", capacity);
        Assert.False(WindowWith(0, 250).TryValidate(out string? recharge));
        Assert.Contains("a window holds nothing", recharge);

        // Every other storage still declares a capacity.
        Assert.False(new MachineDefinition { Kind = MachineKind.Storage, Carried = true, Links = [Charge], Ports = [Channel] }.TryValidate(out string? empty));
        Assert.Contains("capacityJoules must be more than 0", empty);
    }

    [Fact]
    public void ThePoolIdentityHoldsOverAThousandRandomBitesAndBoundaries()
    {
        // The split's test for IM-2: every door into the pool, in random order — the window's
        // draws, the lead-acid pack's, swaps between them, the debug fill, cells switched off and
        // on, cells added charged and removed, the sun, and boundaries — with the identity read
        // after each.
        var rig = new Rig();
        var random = new Random(0x1A2);
        var cells = new List<MachineRow>
        {
            rig.Place(new BrushCell(0, 0, 0), BigCell, charge: BigCapacity / 3),
            rig.Place(new BrushCell(1, 0, 0), SmallCell, charge: SmallCapacity),
            rig.Place(new BrushCell(2, 0, 0), SmallCell, channel: 1, charge: SmallCapacity / 2),
        };
        rig.Place(new BrushCell(0, 5, 0), Panel);
        rig.Place(new BrushCell(1, 5, 0), Panel);
        EquippedPack worn = rig.Wear(Window);
        int nextX = 3;
        long windowDrawn = 0, boundaries = 0, swaps = 0;

        for (int op = 0; op < 1000; op++)
        {
            switch (random.Next(8))
            {
                case 0:
                case 1:
                    rig.Tick.Run();
                    boundaries++;
                    break;
                case 2:
                case 3:
                {
                    int voxels = random.Next(0, 5000);
                    int affordable = worn.VoxelsAffordable(Cost);
                    long paid = worn.Pay(Math.Min(voxels, affordable), Cost);
                    Assert.Equal(Math.Min(voxels, affordable) * Cost, paid);
                    if (worn.IsWindow) windowDrawn += paid;
                    break;
                }

                case 4:
                    worn = random.Next(2) == 0
                        ? rig.Wear(Window, channel: random.Next(2))
                        : rig.Wear(LeadAcid, channel: random.Next(2), charge: random.NextInt64(0, LeadAcidCapacity));
                    swaps++;
                    break;
                case 5:
                {
                    MachineRow cell = cells[random.Next(cells.Count)];
                    rig.Machines.SetPort(cell.Cell, MachineTick.EnabledPort, random.Next(2));
                    break;
                }

                case 6:
                    if (random.Next(2) == 0 && cells.Count > 1)
                    {
                        MachineRow gone = cells[random.Next(cells.Count)];
                        Assert.True(rig.Machines.Remove(gone.Cell));
                        cells.Remove(gone);
                    }
                    else
                    {
                        cells.Add(rig.Place(new BrushCell(nextX++, 0, 0), SmallCell, channel: random.Next(2), charge: random.NextInt64(0, SmallCapacity)));
                    }

                    break;
                case 7:
                    if (random.Next(3) == 0)
                    {
                        rig.Tick.FillPack(random.NextInt64(0, LeadAcidCapacity));
                    }
                    else
                    {
                        rig.Tick.OvercastByte = (byte)random.Next(256);
                    }

                    break;
            }

            Assert.True(rig.Tick.PowerExact, $"op {op}: the pool identity broke");
        }

        Assert.True(windowDrawn > 0, "the window never drew");
        Assert.True(boundaries > 100 && swaps > 20);
        Assert.True(rig.Tick.GeneratedMillijoules > 0);
        Assert.True(rig.Machines.StoredMillijoulesLeaving > 0, "no cell was removed holding anything");
    }

    // ---------------------------------------------------------------- priority (decisions 9, 41)

    [Fact]
    public void EqualPrioritiesArePositionOrderAndAHigherOneGoesFirst()
    {
        var rig = new Rig();
        MachineRow plain = rig.Place(new BrushCell(0, 0, 0), Pump);
        MachineRow middle = rig.Place(new BrushCell(1, 0, 0), PriorityPump);
        MachineRow high = rig.Place(new BrushCell(2, 0, 0), PriorityPump, priority: 9);
        MachineRow low = rig.Place(new BrushCell(3, 0, 0), PriorityPump, priority: 0);
        MachineRow alsoMiddle = rig.Place(new BrushCell(4, 0, 0), PriorityPump, priority: 5);

        // A pump without the extra reads the middle, 5, and so ties with a shipped one by position.
        Assert.Equal([high, plain, middle, alsoMiddle, low], rig.Tick.PumpOrder);

        // A port write moves the version, and the next rebuild re-sorts.
        rig.Machines.SetPort(low.Cell, MachineTick.PriorityPort, 9);
        Assert.Equal([high, low, plain, middle, alsoMiddle], rig.Tick.PumpOrder);
    }

    [Fact]
    public void ARowsPriorityIsClampedAndOnlyReadWithTheExtra()
    {
        MachineDefinition withExtra = Materials.Machine(PriorityPump)!;
        MachineDefinition without = Materials.Machine(Pump)!;
        var row = new MachineRow(new BrushCell(0, 0, 0), PriorityPump, BrushFacing.PositiveX, withExtra.DefaultPorts());

        Assert.Equal(MachineTick.DefaultPriority, MachineTick.PriorityOf(row, withExtra));
        row.SetPortUnchecked(MachineTick.PriorityPort, 42);
        Assert.Equal(9, MachineTick.PriorityOf(row, withExtra));
        Assert.Equal(MachineTick.DefaultPriority, MachineTick.PriorityOf(row, without));
    }
}
