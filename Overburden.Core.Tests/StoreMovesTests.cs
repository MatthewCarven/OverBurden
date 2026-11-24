using Overburden.Core.Building;
using Overburden.Core.Content;
using Overburden.Core.Inventory;
using Overburden.Core.Machines;

namespace Overburden.Core.Tests;

/// <summary>
/// IM-6a, decision 4: the store's moves as one Core seam — to the store and to the pocket on the backpack's
/// channel, into and out of one box, and out of the combined box pocket first — each returning what moved, a
/// closed box refused with its reason, a material the pocket refuses never moved, and the store's fill.
/// Rows only: the moves never touch the world.
/// </summary>
public class StoreMovesTests
{
    private const int Air = 0, Stone = 1, Dirt = 2, High = 3, Small = 4, Crate = 5, CellMaterial = 6, Water = 7, WindowMaterial = 8, LeadAcid = 9;

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
        //            air    stone  dirt   high   small  crate  cell   water  window lead
        isOpaque: [false, true, true, true, true, true, true, false, true, true],
        isFluid: [false, false, false, false, false, false, false, true, false, false],
        textureLayer: [0, 1, 2, 3, 4, 5, 6, 0, 8, 9],
        hardness: [0f, 3f, 1f, 3f, 3f, 1f, 3f, 0f, 1f, 1f],
        machine:
        [
            null, null, null,
            VaultOf(10, 400),   // the high grade: cheaper a cubic metre, so filled first
            VaultOf(1, 2_500),  // the low grade
            new MachineDefinition { Kind = MachineKind.Vault, InsideMillilitres = MachineDefinition.MillilitresPerCubicMetre },
            new MachineDefinition
            {
                Kind = MachineKind.Storage,
                CapacityMillijoules = 1_000_000,
                Links = [new MachineLink("charge", MachineMedium.Power, MachineRole.Sink, MachineLink.ChannelVia, null, "channel")],
                Ports = [Channel, Enabled],
            },
            null,
            WindowTests.WindowDefinition,
            new MachineDefinition { Kind = MachineKind.Storage, Carried = true, CapacityMillijoules = 3_600_000_000, RechargeWatts = 250, Links = [Power], Ports = [Channel] },
        ]);

    private sealed class Rig
    {
        public Rig()
        {
            Pocket.RefuseMaterials([Water]);
            Moves = new StoreMoves(Machines, Pocket);
        }

        public MachineSet Machines { get; } = new(Materials);

        public VolumetricInventory Pocket { get; } = new();

        public StoreMoves Moves { get; }

        public MachineRow Box(BrushCell cell, int material, int channel = 0, bool enabled = true, VaultContents? contents = null, byte[]? bytes = null)
        {
            byte[]? state = bytes ?? (contents is null ? null : VaultState.Encode(contents));
            var row = new MachineRow(cell, material, BrushFacing.PositiveX, Materials.Machine(material)!.DefaultPorts(), state);
            if (material is High or Small)
            {
                row.SetPortUnchecked("channel", channel);
                row.SetPortUnchecked(MachineTick.EnabledPort, enabled ? 1 : 0);
            }

            Machines.Add(row);
            return row;
        }

        public static VaultContents Holding(params (int Material, long Voxels)[] voxels)
        {
            var contents = new VaultContents();
            foreach ((int material, long count) in voxels)
            {
                contents.Add(material, count);
            }

            return contents;
        }

        public long In(MachineRow row, int material)
        {
            Assert.True(VaultState.TryRead(row, out VaultContents contents));
            return contents.VoxelsOf(material);
        }

        public static EquippedPack Window(int channel) => new(WindowMaterial, WindowTests.WindowDefinition, 0, channel);

        public static EquippedPack LeadAcidPack() => new(LeadAcid, Materials.Machine(LeadAcid)!, 0, 0);
    }

    /// <summary>IM-6b (decision 9): the stock section's rows — the store's holdings summed over the vaults it lists, and one box's own.</summary>
    [Fact]
    public void TheStoresStockIsItsOpenVaultsSummedAndABoxShowsItsOwnOpenOrNot()
    {
        var rig = new Rig();
        rig.Box(new BrushCell(0, 0, 0), High, channel: 1, contents: Rig.Holding((Stone, 640), (Dirt, 64)));
        rig.Box(new BrushCell(1, 0, 0), Small, channel: 1, contents: Rig.Holding((Stone, 60)));
        rig.Box(new BrushCell(2, 0, 0), High, channel: 2, contents: Rig.Holding((Stone, 5)));
        MachineRow off = rig.Box(new BrushCell(3, 0, 0), High, channel: 1, enabled: false, contents: Rig.Holding((Dirt, 7)));
        MachineRow crate = rig.Box(new BrushCell(4, 0, 0), Crate, contents: Rig.Holding((Dirt, 3)));

        // A switched-off vault is out of the store (decision 27), so its 7 dirt are not in the channel's stock.
        Assert.Equal(new SortedDictionary<int, long> { [Stone] = 700, [Dirt] = 64 }, rig.Moves.StockOfStore(1));
        Assert.Equal(new SortedDictionary<int, long> { [Stone] = 5 }, rig.Moves.StockOfStore(2));
        Assert.Empty(rig.Moves.StockOfStore(0));

        // But its own panel still shows what it holds, and so does a crate's.
        Assert.Equal(new SortedDictionary<int, long> { [Dirt] = 7 }, rig.Moves.StockOfBox(off.Cell));
        Assert.Equal(new SortedDictionary<int, long> { [Dirt] = 3 }, rig.Moves.StockOfBox(crate.Cell));
        Assert.Empty(rig.Moves.StockOfBox(new BrushCell(9, 9, 9)));

        // A click moves a piece of a counted item and a cubic metre of anything else.
        Assert.Equal(1, StoreMoves.ClickVoxels(MaterialUnit.Count));
        Assert.Equal(64, StoreMoves.ClickVoxels(MaterialUnit.Cells));
        Assert.Equal(64, StoreMoves.ClickVoxels(MaterialUnit.Litres));
    }

    [Fact]
    public void ToStoreFillsTheBackpacksChannelInFillOrderAndToPocketTakesTheLastFilledFirst()
    {
        var rig = new Rig();
        MachineRow high = rig.Box(new BrushCell(0, 0, 0), High, channel: 1);
        MachineRow small = rig.Box(new BrushCell(1, 0, 0), Small, channel: 1);
        MachineRow other = rig.Box(new BrushCell(2, 0, 0), High, channel: 0);
        rig.Pocket.Add(Stone, 700);
        EquippedPack window = Rig.Window(1);

        Assert.Equal(new StoreMove(700, StoreRefusal.None), rig.Moves.ToStore(window, Stone, 700));
        Assert.Equal((640L, 60L, 0L, 0L), (rig.In(high, Stone), rig.In(small, Stone), rig.In(other, Stone), rig.Pocket.Voxels(Stone)));

        Assert.Equal(new StoreMove(100, StoreRefusal.None), rig.Moves.ToPocket(window, Stone, 100));
        Assert.Equal((600L, 0L, 100L), (rig.In(high, Stone), rig.In(small, Stone), rig.Pocket.Voxels(Stone)));

        // Another channel's store is another store.
        Assert.Equal(new StoreMove(100, StoreRefusal.None), rig.Moves.ToStore(Rig.Window(0), Stone, 100));
        Assert.Equal(100, rig.In(other, Stone));
        Assert.Equal(new StoreMove(0, StoreRefusal.None), rig.Moves.ToPocket(Rig.Window(5), Stone, 100));
        Assert.True(rig.Machines.StoreExact);
    }

    [Fact]
    public void WithoutAQuantumBackpackTheStoreIsNotReachable()
    {
        var rig = new Rig();
        MachineRow high = rig.Box(new BrushCell(0, 0, 0), High, contents: Rig.Holding((Stone, 10)));
        rig.Pocket.Add(Stone, 10);

        foreach (EquippedPack? pack in new[] { null, Rig.LeadAcidPack() })
        {
            Assert.Equal(new StoreMove(0, StoreRefusal.NoBackpack), rig.Moves.ToStore(pack, Stone, 10));
            Assert.Equal(new StoreMove(0, StoreRefusal.NoBackpack), rig.Moves.ToPocket(pack, Stone, 10));
        }

        Assert.Equal((10L, 10L), (rig.In(high, Stone), rig.Pocket.Voxels(Stone)));
    }

    [Fact]
    public void IntoBoxAndOutOfBoxMoveIntoAndOutOfThatBoxAlone()
    {
        var rig = new Rig();
        MachineRow crate = rig.Box(new BrushCell(0, 0, 0), Crate);
        MachineRow high = rig.Box(new BrushCell(1, 0, 0), High, contents: Rig.Holding((Stone, 5)));
        rig.Pocket.Add(Stone, 100);

        Assert.Equal(new StoreMove(64, StoreRefusal.None), rig.Moves.IntoBox(crate.Cell, Stone, 100)); // a cubic metre's room
        Assert.Equal(new StoreMove(10, StoreRefusal.None), rig.Moves.OutOfBox(crate.Cell, Stone, 10));
        Assert.Equal(new StoreMove(5, StoreRefusal.None), rig.Moves.OutOfBox(high.Cell, Stone, 50));
        Assert.Equal((54L, 0L, 51L), (rig.In(crate, Stone), rig.In(high, Stone), rig.Pocket.Voxels(Stone)));
        Assert.True(rig.Machines.StoreExact);
    }

    [Theory]
    [InlineData("off")]
    [InlineData("dumping")]
    [InlineData("wreck")]
    [InlineData("unreadable")]
    [InlineData("cell")]
    [InlineData("empty")]
    public void AClosedBoxIsRefusedWithItsReasonAndNothingMoves(string how)
    {
        var rig = new Rig();
        var cell = new BrushCell(3, 0, 0);
        StoreRefusal expected;
        switch (how)
        {
            case "off":
                rig.Box(cell, High, enabled: false, contents: Rig.Holding((Stone, 20)));
                expected = StoreRefusal.SwitchedOff;
                break;
            case "dumping":
                VaultContents dumping = Rig.Holding((Stone, 20));
                dumping.Phase = VaultPhase.Dumping;
                dumping.FuseMillisLeft = VaultDump.ReleaseMillis;
                rig.Box(cell, High, contents: dumping);
                expected = StoreRefusal.Dumping;
                break;
            case "wreck":
                // A wreck is dumping too, and switched off besides: the strongest reason is the one told.
                VaultContents wreck = Rig.Holding((Stone, 20));
                wreck.Phase = VaultPhase.Dumping;
                wreck.Wreck = true;
                rig.Box(cell, High, enabled: false, contents: wreck);
                expected = StoreRefusal.Wreck;
                break;
            case "unreadable":
                rig.Box(cell, High, bytes: [99, 0, 0]);
                expected = StoreRefusal.Unreadable;
                break;
            case "cell":
                rig.Machines.Add(new MachineRow(cell, CellMaterial, BrushFacing.PositiveX, Materials.Machine(CellMaterial)!.DefaultPorts()));
                expected = StoreRefusal.NotABox;
                break;
            default:
                expected = StoreRefusal.NotABox;
                break;
        }

        rig.Pocket.Add(Stone, 20);
        byte[]? before = rig.Machines.TryGet(cell, out MachineRow row) ? row.KindState.ToArray() : null;

        Assert.Equal(expected, rig.Machines.ClosedReason(cell));
        Assert.Equal(new StoreMove(0, expected), rig.Moves.IntoBox(cell, Stone, 20));
        Assert.Equal(new StoreMove(0, expected), rig.Moves.OutOfBox(cell, Stone, 20));
        Assert.Equal(new StoreMove(0, expected), rig.Moves.OutOfCombined(Rig.Window(0), cell, Stone, 20));
        Assert.Equal(20, rig.Pocket.Voxels(Stone));
        if (before is not null)
        {
            Assert.Equal(before, row.KindState.ToArray());
        }

        Assert.True(rig.Machines.StoreExact);
    }

    [Fact]
    public void OutOfCombinedTakesThePocketFirstThenTheStoreAndNeverTheBoxItself()
    {
        // The aimed box is a vault on the backpack's channel, so part of the store: the container's box shows
        // it alone and the combined box the rest (M10-LADDER-PLAN, the screen's point 1). It holds some
        // already, and is the last filled, so a store half that forgot to leave it out would take from it first.
        var rig = new Rig();
        MachineRow high = rig.Box(new BrushCell(0, 0, 0), High, contents: Rig.Holding((Stone, 100)));
        MachineRow aimed = rig.Box(new BrushCell(1, 0, 0), Small, contents: Rig.Holding((Stone, 10)));
        rig.Pocket.Add(Stone, 30);
        EquippedPack window = Rig.Window(0);

        Assert.Equal(new StoreMove(50, StoreRefusal.None), rig.Moves.OutOfCombined(window, aimed.Cell, Stone, 50));
        Assert.Equal((0L, 80L, 60L), (rig.Pocket.Voxels(Stone), rig.In(high, Stone), rig.In(aimed, Stone)));
        Assert.Equal(20, rig.Machines.VoxelsMovedBetweenBoxes);
        Assert.True(rig.Machines.StoreExact);

        // Its room is the bound: 64 voxels in a cubic metre, 60 held.
        Assert.Equal(new StoreMove(4, StoreRefusal.None), rig.Moves.OutOfCombined(window, aimed.Cell, Stone, 100));
        Assert.Equal((76L, 64L), (rig.In(high, Stone), rig.In(aimed, Stone)));
        Assert.True(rig.Machines.StoreExact);
    }

    [Fact]
    public void TheCombinedBoxsStoreHalfTakesTheLastFilledFirst()
    {
        // As a withdrawal does: the store fills the high grade first, so the low one is emptied first.
        var rig = new Rig();
        MachineRow high = rig.Box(new BrushCell(0, 0, 0), High, contents: Rig.Holding((Stone, 100)));
        MachineRow small = rig.Box(new BrushCell(1, 0, 0), Small, contents: Rig.Holding((Stone, 20)));
        MachineRow crate = rig.Box(new BrushCell(2, 0, 0), Crate);

        Assert.Equal(new StoreMove(30, StoreRefusal.None), rig.Moves.OutOfCombined(Rig.Window(0), crate.Cell, Stone, 30));
        Assert.Equal((90L, 0L, 30L), (rig.In(high, Stone), rig.In(small, Stone), rig.In(crate, Stone)));
        Assert.True(rig.Machines.StoreExact);
    }

    [Fact]
    public void WithoutAQuantumBackpackTheCombinedBoxIsThePocketAlone()
    {
        var rig = new Rig();
        MachineRow high = rig.Box(new BrushCell(0, 0, 0), High, contents: Rig.Holding((Stone, 100)));
        MachineRow crate = rig.Box(new BrushCell(1, 0, 0), Crate);
        rig.Pocket.Add(Stone, 5);

        Assert.Equal(new StoreMove(5, StoreRefusal.None), rig.Moves.OutOfCombined(null, crate.Cell, Stone, 10));
        rig.Pocket.Add(Stone, 5);
        Assert.Equal(new StoreMove(5, StoreRefusal.None), rig.Moves.OutOfCombined(Rig.LeadAcidPack(), crate.Cell, Stone, 10));
        Assert.Equal((10L, 100L, 0L), (rig.In(crate, Stone), rig.In(high, Stone), rig.Pocket.Voxels(Stone)));

        // With the window worn, the store fills what the pocket can't — into a crate too, which has no channel.
        Assert.Equal(new StoreMove(10, StoreRefusal.None), rig.Moves.OutOfCombined(Rig.Window(0), crate.Cell, Stone, 10));
        Assert.Equal((20L, 90L), (rig.In(crate, Stone), rig.In(high, Stone)));
    }

    [Fact]
    public void NothingMovesAMaterialThePocketRefusesByAnyDoor()
    {
        // A vault's bytes can name a fluid as voxels — a restore from a build that had none, or a mod that made
        // a block a fluid. The pocket drops a refused material into a counter, so a move out would lose it.
        var rig = new Rig();
        MachineRow high = rig.Box(new BrushCell(0, 0, 0), High, contents: Rig.Holding((Water, 64), (Stone, 10)));
        MachineRow crate = rig.Box(new BrushCell(1, 0, 0), Crate);
        EquippedPack window = Rig.Window(0);
        long refusedBefore = rig.Pocket.RefusedVoxels;

        Assert.Equal(new StoreMove(0, StoreRefusal.PocketRefuses), rig.Moves.ToPocket(window, Water, 64));
        Assert.Equal(new StoreMove(0, StoreRefusal.PocketRefuses), rig.Moves.OutOfBox(high.Cell, Water, 64));
        Assert.Equal(new StoreMove(0, StoreRefusal.PocketRefuses), rig.Moves.ToStore(window, Water, 64));
        Assert.Equal(new StoreMove(0, StoreRefusal.PocketRefuses), rig.Moves.IntoBox(crate.Cell, Water, 64));
        Assert.Equal(new StoreMove(0, StoreRefusal.PocketRefuses), rig.Moves.OutOfCombined(window, crate.Cell, Water, 64));

        // The set's own doors carry the guard too.
        Assert.Equal(0, rig.Machines.Withdraw(0, Water, 64, rig.Pocket));
        Assert.Equal(0, rig.Machines.WithdrawFrom(high.Cell, Water, 64, rig.Pocket));
        Assert.Equal(128, rig.Machines.WithdrawRefusedVoxels);

        Assert.Equal(64, rig.In(high, Water));
        Assert.Equal(0, rig.In(crate, Water));
        Assert.Equal(refusedBefore, rig.Pocket.RefusedVoxels);
        Assert.True(rig.Machines.StoreExact);

        // And the rest of the vault still moves.
        Assert.Equal(new StoreMove(10, StoreRefusal.None), rig.Moves.ToPocket(window, Stone, 10));
    }

    [Fact]
    public void TheStoreFillCountsBlocksAndWaterTogetherOverOneChannelsOpenVaults()
    {
        var rig = new Rig();
        VaultContents wet = Rig.Holding((Stone, 64));
        wet.WaterMillilitres = 2_000_000;
        rig.Box(new BrushCell(0, 0, 0), High, channel: 2, contents: wet);
        rig.Box(new BrushCell(1, 0, 0), Small, channel: 2, contents: Rig.Holding((Dirt, 32)));
        rig.Box(new BrushCell(2, 0, 0), High, channel: 2, enabled: false, contents: Rig.Holding((Stone, 640)));
        VaultContents dumping = Rig.Holding((Stone, 64));
        dumping.Phase = VaultPhase.Dumping;
        rig.Box(new BrushCell(3, 0, 0), High, channel: 2, contents: dumping);
        rig.Box(new BrushCell(4, 0, 0), High, channel: 3, contents: Rig.Holding((Stone, 64)));
        rig.Box(new BrushCell(5, 0, 0), Crate, contents: Rig.Holding((Stone, 64)));

        Assert.Equal(new StoreFill(3_500_000, 11_000_000), rig.Machines.StoreFillOf(2));
        Assert.Equal(new StoreFill(1_000_000, 10_000_000), rig.Machines.StoreFillOf(3));
        Assert.Equal(new StoreFill(0, 0), rig.Machines.StoreFillOf(9));
    }

    [Fact]
    public void OverRandomMovesNoVoxelIsMadeOrLostAndTheStoresLineHolds()
    {
        var rig = new Rig();
        MachineRow[] boxes =
        [
            rig.Box(new BrushCell(0, 0, 0), High, channel: 0),
            rig.Box(new BrushCell(1, 0, 0), Small, channel: 0),
            rig.Box(new BrushCell(2, 0, 0), High, channel: 1),
            rig.Box(new BrushCell(3, 0, 0), Crate),
        ];
        rig.Pocket.Add(Stone, 2_000);
        rig.Pocket.Add(Dirt, 1_000);
        int[] materials = [Stone, Dirt];
        var total = new Dictionary<int, long> { [Stone] = 2_000, [Dirt] = 1_000 };
        var random = new Random(0x6A4);
        int moved = 0;
        for (int step = 0; step < 2_000; step++)
        {
            if (random.Next(10) == 0)
            {
                rig.Machines.SetPort(boxes[1].Cell, MachineTick.EnabledPort, random.Next(2));
            }

            int material = materials[random.Next(2)];
            long voxels = random.Next(200);
            BrushCell box = boxes[random.Next(boxes.Length)].Cell;
            EquippedPack window = Rig.Window(random.Next(2));
            long pocket = rig.Pocket.Voxels(material);
            int op = random.Next(5);
            StoreMove move = op switch
            {
                0 => rig.Moves.ToStore(window, material, voxels),
                1 => rig.Moves.ToPocket(window, material, voxels),
                2 => rig.Moves.IntoBox(box, material, voxels),
                3 => rig.Moves.OutOfBox(box, material, voxels),
                _ => rig.Moves.OutOfCombined(window, box, material, voxels),
            };

            Assert.InRange(move.Voxels, 0, voxels);
            long change = rig.Pocket.Voxels(material) - pocket;
            switch (op)
            {
                case 0 or 2: Assert.Equal(-move.Voxels, change); break;
                case 1 or 3: Assert.Equal(move.Voxels, change); break;
                default: Assert.InRange(-change, 0, move.Voxels); break;
            }

            moved += move.Voxels > 0 ? 1 : 0;
            Assert.True(rig.Machines.StoreExact, $"step {step}: the store's line broke");
            foreach (int m in materials)
            {
                long held = boxes.Sum(row => rig.In(row, m));
                Assert.Equal(total[m], rig.Pocket.Voxels(m) + held);
            }

            foreach (MachineRow row in boxes)
            {
                Assert.True(VaultState.TryRead(row, out VaultContents contents));
                Assert.True(contents.UsedMillilitres <= rig.Machines.VaultDefinitionOf(row)!.InsideMillilitres, $"step {step}: {row.Cell} overfull");
            }
        }

        Assert.True(moved > 500, $"only {moved} moves moved anything");
        Assert.True(rig.Machines.VoxelsMovedBetweenBoxes > 0, "the combined box's store half never moved anything");
    }
}
