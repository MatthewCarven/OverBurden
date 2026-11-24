using System.Globalization;
using Overburden.Core.Building;
using Overburden.Core.Content;
using Overburden.Core.Machines;

namespace Overburden.Core.Tests;

/// <summary>
/// IM-6b, decisions 8 and 13: the HUD's bank line while the quantum backpack is worn, and the fuse's top line —
/// what each shows, when, and from which rows. Rows only, with the runway fed its figures directly.
/// </summary>
public class BankLineTests
{
    private const int Air = 0, Stone = 1, High = 2, Small = 3, Crate = 4;

    private static readonly PortDefinition Channel = new("channel", 0, 0, 15);
    private static readonly PortDefinition Enabled = new(MachineTick.EnabledPort, 1, 0, 1);
    private static readonly MachineLink Power = new("power", MachineMedium.Power, MachineRole.Sink, MachineLink.ChannelVia, null, "channel");

    private static MachineDefinition VaultOf(long cubicMetres, long blockMilliwatts) => new()
    {
        Kind = MachineKind.Vault,
        InsideMillilitres = cubicMetres * MachineDefinition.MillilitresPerCubicMetre,
        WaterHoldMilliwattsPerCubicMetre = blockMilliwatts * 10,
        BlockHoldMilliwattsPerCubicMetre = blockMilliwatts,
        HoldsWater = true,
        Links = [Power],
        Ports = [Channel, Enabled],
    };

    private static readonly MaterialTable Materials = MaterialTable.ForTesting(
        isOpaque: [false, true, true, true, true],
        isFluid: [false, false, false, false, false],
        textureLayer: [0, 1, 2, 3, 4],
        hardness: [0f, 3f, 3f, 3f, 1f],
        machine:
        [
            null, null,
            VaultOf(10_000, 400),  // the high grade
            VaultOf(100, 2_500),   // the low grade
            new MachineDefinition { Kind = MachineKind.Vault, InsideMillilitres = MachineDefinition.MillilitresPerCubicMetre },
        ]);

    private static MachineRow Box(MachineSet set, BrushCell cell, int material, int channel = 0, bool enabled = true, VaultContents? contents = null, bool paid = false)
    {
        var row = new MachineRow(cell, material, BrushFacing.PositiveX, Materials.Machine(material)!.DefaultPorts(),
            contents is null ? null : VaultState.Encode(contents));
        if (material is High or Small)
        {
            row.SetPortUnchecked("channel", channel);
            row.SetPortUnchecked(MachineTick.EnabledPort, enabled ? 1 : 0);
        }

        row.LastHoldPaid = paid;
        set.Add(row);
        return row;
    }

    private static VaultContents Holding(long stoneVoxels = 0, long waterMillilitres = 0, VaultPhase phase = VaultPhase.Holding, int fuseMillis = 0, bool wreck = false)
    {
        var contents = new VaultContents { WaterMillilitres = waterMillilitres, Phase = phase, FuseMillisLeft = fuseMillis, Wreck = wreck };
        if (stoneVoxels > 0)
        {
            contents.Add(Stone, stoneVoxels);
        }

        return contents;
    }

    /// <summary>One boundary's figures on one channel, the rest at zero, recorded <paramref name="boundaries"/> times.</summary>
    private static Runway RunwayOf(int channel, long bank, long inPerBoundary, long outPerBoundary, int boundaries = Runway.Boundaries)
    {
        var runway = new Runway();
        var flows = new ChannelFlows[MachineTick.Channels];
        var banks = new long[MachineTick.Channels];
        flows[channel] = new ChannelFlows(inPerBoundary, outPerBoundary, 0, 0, 0, 0, 0);
        banks[channel] = bank;
        for (int i = 0; i < boundaries; i++)
        {
            runway.Record(flows, banks);
        }

        return runway;
    }

    private static string Invariant(Func<string> text)
    {
        CultureInfo before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            return text();
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void HisLineReadsAsThePlanWroteIt()
    {
        // BANK 612 MJ ch 0 · +3.8 kW sun · −104 kW · runway 1 h 38 m · hold 2 kW · store 68%, in the HUD's units:
        // 1.9 kJ in and 52 kJ out a 500 ms boundary are 3.8 kW and 104 kW; 612 MJ over the 100.2 kW net is 1 h 41 m.
        var view = new BankView(
            0,
            new RunwayReading(612_000_000_000, 3_800_000, 104_000_000, 5_880_000),
            2_000_000,
            new StoreFill(68, 100),
            0,
            HasStore: true);
        Assert.Equal(
            "BANK 612.0 MJ ch 0   ·   +3.8 kW sun   ·   −104 kW   ·   runway 1 h 38 m   ·   hold 2.0 kW   ·   store 68%",
            Invariant(() => BankLine.Text(view)));
    }

    [Fact]
    public void TheRunwayShowsOnlyWhileTheBankDrains()
    {
        // Out 52 kJ against in 1.9 kJ a boundary: a drain, so a runway. In over out: none.
        BankView draining = BankLine.Read(new MachineSet(Materials), RunwayOf(0, 612_000_000_000, 1_900_000, 52_000_000), 0);
        Assert.NotNull(draining.Runway.RunwayMillis);
        Assert.Contains("runway", Invariant(() => BankLine.Text(draining)));

        BankView rising = BankLine.Read(new MachineSet(Materials), RunwayOf(0, 612_000_000_000, 52_000_000, 1_900_000), 0);
        Assert.Null(rising.Runway.RunwayMillis);
        Assert.DoesNotContain("runway", Invariant(() => BankLine.Text(rising)));

        BankView level = BankLine.Read(new MachineSet(Materials), RunwayOf(0, 612_000_000_000, 1_000, 1_000), 0);
        Assert.DoesNotContain("runway", Invariant(() => BankLine.Text(level)));
    }

    [Fact]
    public void TheLineReadsTheBackpacksChannelAndNoOther()
    {
        Runway runway = RunwayOf(3, 70_000_000_000, 0, 1_000_000);
        BankView three = BankLine.Read(new MachineSet(Materials), runway, 3);
        BankView zero = BankLine.Read(new MachineSet(Materials), runway, 0);
        Assert.Equal(70_000_000_000, three.Runway.BankMillijoules);
        Assert.Equal(0, zero.Runway.BankMillijoules);
        Assert.StartsWith("BANK 70.0 MJ ch 3", Invariant(() => BankLine.Text(three)));

        // An out-of-range channel is clamped, as the runway clamps one.
        Assert.Equal(MachineTick.Channels - 1, BankLine.Read(new MachineSet(Materials), runway, 99).Channel);
    }

    [Fact]
    public void TheHoldIsEveryBillingVaultOnTheChannelAtItsGradesRates()
    {
        var set = new MachineSet(Materials);

        // 64 voxels of stone is one cubic metre: 400 mW in the high vault. 1,000 L of water in the low one: 25 W.
        Box(set, new BrushCell(0, 0, 0), High, channel: 2, contents: Holding(stoneVoxels: 64));
        Box(set, new BrushCell(1, 0, 0), Small, channel: 2, contents: Holding(waterMillilitres: 1_000_000));

        // Switched off still bills (decision 27); another channel, a dumping vault, a wreck and a crate do not.
        Box(set, new BrushCell(2, 0, 0), High, channel: 2, enabled: false, contents: Holding(stoneVoxels: 64));
        Box(set, new BrushCell(3, 0, 0), High, channel: 5, contents: Holding(stoneVoxels: 6_400));
        Box(set, new BrushCell(4, 0, 0), High, channel: 2, contents: Holding(stoneVoxels: 6_400, phase: VaultPhase.Dumping));
        Box(set, new BrushCell(5, 0, 0), High, channel: 2, contents: Holding(stoneVoxels: 6_400, wreck: true, phase: VaultPhase.Dumping));
        Box(set, new BrushCell(6, 0, 0), Crate, contents: Holding(stoneVoxels: 64));

        Assert.Equal(400 + 25_000 + 400, BankLine.HoldMilliwattsOf(set, 2));
        Assert.Equal(40_000, BankLine.HoldMilliwattsOf(set, 5));
        Assert.Equal(0, BankLine.HoldMilliwattsOf(set, 0));

        // It matches the tick's own bill, a boundary at a time: 25.8 W for 500 ms is 12.9 J.
        long bills = 0;
        foreach (MachineRow row in set.Rows)
        {
            if (row.TryGetPort("channel", out long c) && c == 2 && VaultState.TryRead(row, out VaultContents contents) && !contents.Owes)
            {
                bills += MachineTick.BillOf(set.VaultDefinitionOf(row)!, contents, 500);
            }
        }

        Assert.Equal(BankLine.HoldMilliwattsOf(set, 2) * 500 / 1000, bills);
    }

    [Fact]
    public void TheHoldAndTheStoreShowOnlyWhenThereIsOne()
    {
        var empty = new MachineSet(Materials);
        string bare = Invariant(() => BankLine.Text(BankLine.Read(empty, RunwayOf(0, 0, 0, 0), 0)));
        Assert.DoesNotContain("hold", bare);
        Assert.DoesNotContain("store", bare);

        // An empty vault bills nothing, but it is a store.
        var one = new MachineSet(Materials);
        Box(one, new BrushCell(0, 0, 0), High, channel: 0);
        string withStore = Invariant(() => BankLine.Text(BankLine.Read(one, RunwayOf(0, 0, 0, 0), 0)));
        Assert.DoesNotContain("hold", withStore);
        Assert.Contains("store 0%", withStore);

        // A vault switched off is out of the store (it takes no deposits) but still bills.
        var off = new MachineSet(Materials);
        Box(off, new BrushCell(0, 0, 0), High, channel: 0, enabled: false, contents: Holding(stoneVoxels: 64));
        BankView view = BankLine.Read(off, RunwayOf(0, 0, 0, 0), 0);
        Assert.False(view.HasStore);
        Assert.Equal(400, view.HoldMilliwatts);
        Assert.DoesNotContain("store", Invariant(() => BankLine.Text(view)));
    }

    [Fact]
    public void TheStoreAndItsWaterAreTheChannelsOpenVaults()
    {
        var set = new MachineSet(Materials);
        Box(set, new BrushCell(0, 0, 0), Small, channel: 1, contents: Holding(stoneVoxels: 3_200, waterMillilitres: 25_000_000));
        Box(set, new BrushCell(1, 0, 0), Small, channel: 1);
        Box(set, new BrushCell(2, 0, 0), Small, channel: 4, contents: Holding(waterMillilitres: 7_000_000));

        BankView view = BankLine.Read(set, RunwayOf(1, 0, 0, 0), 1);

        // 50 m³ of stone and 25 m³ of water in 200 m³: 37%.
        Assert.Equal(new StoreFill(75 * MachineDefinition.MillilitresPerCubicMetre, 200 * MachineDefinition.MillilitresPerCubicMetre), view.Store);
        Assert.Equal(25_000_000, view.StoredWaterMillilitres);
        Assert.EndsWith("store 37%", Invariant(() => BankLine.Text(view)));
    }

    [Theory]
    [InlineData(0L, "0 s")]
    [InlineData(999L, "0 s")]
    [InlineData(59_999L, "59 s")]
    [InlineData(60_000L, "1 m 00 s")]
    [InlineData(725_000L, "12 m 05 s")]
    [InlineData(3_599_999L, "59 m 59 s")]
    [InlineData(5_880_000L, "1 h 38 m")]
    [InlineData(86_399_999L, "23 h 59 m")]
    [InlineData(273_600_000L, "3 d 04 h")]
    [InlineData(long.MaxValue, "999+ d")]
    [InlineData(-5L, "0 s")]
    public void ADurationReadsInItsTwoLargestUnitsFloored(long millis, string text)
    {
        Assert.Equal(text, BankLine.Duration(millis));
    }

    [Theory]
    [InlineData(0L, 100L, "0%")]
    [InlineData(1L, 1_000L, "<1%")]
    [InlineData(68L, 100L, "68%")]
    [InlineData(999L, 1_000L, "99%")]
    [InlineData(100L, 100L, "100%")]
    [InlineData(150L, 100L, "150%")]
    [InlineData(5L, 0L, "–")]
    public void AStoreShareIsAWholePercentFloored(long used, long inside, string text)
    {
        Assert.Equal(text, BankLine.Percent(used, inside));
    }

    [Fact]
    public void TheSoonestFuseIsAmongTheLoadedBoxAndTheBackpacksChannel()
    {
        var set = new MachineSet(Materials);
        BrushCell loadedFar = new(0, 0, 0), unloadedMine = new(100, 0, 0), unloadedOther = new(200, 0, 0), loadedSoon = new(1, 0, 0);
        Box(set, loadedFar, High, channel: 7, contents: Holding(stoneVoxels: 64, phase: VaultPhase.Fusing, fuseMillis: 18_000));
        Box(set, unloadedMine, High, channel: 3, contents: Holding(stoneVoxels: 64, phase: VaultPhase.Fusing, fuseMillis: 12_000));
        Box(set, unloadedOther, High, channel: 7, contents: Holding(stoneVoxels: 64, phase: VaultPhase.Fusing, fuseMillis: 2_000));
        bool Loaded(BrushCell cell) => cell.X < 50;

        // Out of the box on another channel shows only on its panel; on the backpack's channel it shows wherever it is.
        Assert.Equal(unloadedMine, BankLine.SoonestFuse(set, Loaded, backpackChannel: 3)!.Value.Cell);
        Assert.Equal(loadedFar, BankLine.SoonestFuse(set, Loaded, backpackChannel: null)!.Value.Cell);

        // A sooner one in the box wins.
        Box(set, loadedSoon, Small, channel: 9, contents: Holding(stoneVoxels: 64, phase: VaultPhase.Fusing, fuseMillis: 4_000), paid: true);
        FuseWatch soonest = BankLine.SoonestFuse(set, Loaded, backpackChannel: 3)!.Value;
        Assert.Equal(loadedSoon, soonest.Cell);
        Assert.True(soonest.Paused);
        Assert.Equal("VAULT LOSING CONTAINMENT — 4 s (paused)", BankLine.FuseText(soonest));
    }

    [Fact]
    public void NoFuseLineWithoutALitFuse()
    {
        var set = new MachineSet(Materials);
        Box(set, new BrushCell(0, 0, 0), High, contents: Holding(stoneVoxels: 64));
        Box(set, new BrushCell(1, 0, 0), High, contents: Holding(stoneVoxels: 64, phase: VaultPhase.Dumping));
        Box(set, new BrushCell(2, 0, 0), High, contents: Holding(stoneVoxels: 64, phase: VaultPhase.Fusing, fuseMillis: 3_000, wreck: true));
        Assert.Null(BankLine.SoonestFuse(set, _ => true, backpackChannel: 0));
    }

    [Theory]
    [InlineData(14_000, false, "VAULT LOSING CONTAINMENT — 14 s")]
    [InlineData(13_001, false, "VAULT LOSING CONTAINMENT — 14 s")]
    [InlineData(1, false, "VAULT LOSING CONTAINMENT — 1 s")]
    [InlineData(12_000, true, "VAULT LOSING CONTAINMENT — 12 s (paused)")]
    public void TheFuseLineRoundsItsSecondsUp(int millis, bool paused, string text)
    {
        Assert.Equal(text, BankLine.FuseText(new FuseWatch(millis, paused, new BrushCell(0, 0, 0))));
    }
}
