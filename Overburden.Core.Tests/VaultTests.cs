using Overburden.Core.Building;
using Overburden.Core.Content;
using Overburden.Core.Editing;
using Overburden.Core.Fluid;
using Overburden.Core.Inventory;
using Overburden.Core.Machines;
using Overburden.Core.Mining;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// IM-3: the vault and the crate (IDEAL-MACHINES-PLAN decisions 13–22, 27, 28 and 34, as IM-3's pass
/// took them). The codec, the store and its two doors, the bill and the fuse, the pick that asks
/// first and moves at its first voxel, the wreck every remove path yields to, the undo's refusal,
/// and the store's voxel identity through all of it.
/// </summary>
public class VaultTests
{
    private const int Air = 0, Stone = 1, Small = 2, High = 3, Crate = 4, CellMaterial = 5, Dirt = 6, Clay = 7;
    private const int Cell64 = BrushCell.Voxels * BrushCell.Voxels * BrushCell.Voxels;
    private const float Pick = 3.5f;

    // A small vault: 1 m³ (64 voxels), 25 W a m³ of water and 2.5 W of blocks — the low grade's rates.
    // A high one: 10 m³ at 4 W and 0.4 W, so it is cheaper a cubic metre and filled first.
    private const long SmallBlockMilliwatts = 2_500, HighBlockMilliwatts = 400;

    private static readonly MachineLink PowerSink = new("power", MachineMedium.Power, MachineRole.Sink, MachineLink.ChannelVia, null, "channel");
    private static readonly PortDefinition Channel = new("channel", 0, 0, 15);
    private static readonly PortDefinition Enabled = new(MachineTick.EnabledPort, 1, 0, 1);

    private static MachineDefinition VaultOf(long cubicMetres, long waterMilliwatts, long blockMilliwatts) => new()
    {
        Kind = MachineKind.Vault,
        InsideMillilitres = cubicMetres * MachineDefinition.MillilitresPerCubicMetre,
        WaterHoldMilliwattsPerCubicMetre = waterMilliwatts,
        BlockHoldMilliwattsPerCubicMetre = blockMilliwatts,
        HoldsWater = true,
        Links = [PowerSink],
        Ports = [Channel, Enabled],
    };

    private static readonly MachineDefinition CrateDefinition = new()
    {
        Kind = MachineKind.Vault,
        InsideMillilitres = MachineDefinition.MillilitresPerCubicMetre,
    };

    private static readonly MachineDefinition CellDefinition = new()
    {
        Kind = MachineKind.Storage,
        CapacityMillijoules = 1_000_000_000_000L,
        Links = [new MachineLink("charge", MachineMedium.Power, MachineRole.Sink, MachineLink.ChannelVia, null, "channel")],
        Ports = [Channel, Enabled],
    };

    internal static readonly MaterialTable Materials = MaterialTable.ForTesting(
        //            air    stone  small  high   crate  cell   dirt   clay
        isOpaque: [false, true, true, true, true, true, true, true],
        isFluid: [false, false, false, false, false, false, false, false],
        textureLayer: [0, 1, 2, 3, 4, 5, 6, 7],
        hardness: [0f, 3f, 3f, 3f, 1f, 3f, 0.8f, 1f],
        machine: [null, null, VaultOf(1, 25_000, SmallBlockMilliwatts), VaultOf(10, 4_000, HighBlockMilliwatts), CrateDefinition, CellDefinition, null, null],
        unit: [default, default, MaterialUnit.Cells, MaterialUnit.Cells, MaterialUnit.Cells, MaterialUnit.Cells, default, default]);

    /// <summary>A world that fires the set's edit hook on every changed write, and tells the journal first, as the pipeline does.</summary>
    private sealed class HookedWorld : IVoxelEditor
    {
        private readonly TestWorld _inner = new();

        public MachineSet Set { get; } = new(Materials);

        public IEditJournal? Journal { get; set; }

        public int this[VoxelPos voxel] => _inner[voxel];

        public void Seed(VoxelPos voxel, int material) => _inner.Set(voxel, material);

        public bool TryGetMaterial(VoxelPos voxel, out int materialId) => _inner.TryGetMaterial(voxel, out materialId);

        public bool HoldsWater(VoxelPos voxel) => _inner.HoldsWater(voxel);

        public bool SetVoxel(VoxelPos voxel, int materialId)
        {
            int was = _inner[voxel];
            if (was == materialId) return true;
            if (!_inner.SetVoxel(voxel, materialId)) return false;
            Journal?.Wrote(voxel, was, materialId);
            Set.OnVoxelChanged(voxel, materialId);
            return true;
        }

        public int BodyVoxelsAt(BrushCell cell, int material)
        {
            int count = 0;
            for (int lz = 0; lz < BrushCell.Voxels; lz++)
            for (int lx = 0; lx < BrushCell.Voxels; lx++)
            for (int ly = 0; ly < BrushCell.Voxels; ly++)
            {
                if (_inner[cell.VoxelAt(lx, ly, lz)] == material) count++;
            }

            return count;
        }

        public VoxelChunk ChunkVoxels(ChunkPos chunk)
        {
            var voxels = new VoxelChunk();
            VoxelPos origin = chunk.OriginVoxel;
            for (int lz = 0; lz < VoxelConstants.ChunkSize; lz++)
            for (int lx = 0; lx < VoxelConstants.ChunkSize; lx++)
            for (int ly = 0; ly < VoxelConstants.ChunkSize; ly++)
            {
                int material = _inner[new VoxelPos(origin.X + lx, origin.Y + ly, origin.Z + lz)];
                if (material != Air) voxels.Set(lx, ly, lz, material);
            }

            return voxels;
        }
    }

    private sealed class NoWater : IFluidTerrain
    {
        public bool CanHold(VoxelPos voxel) => false;
    }

    private sealed class Rig
    {
        public Rig(bool journal = false)
        {
            var terrain = new NoWater();
            Tick = new MachineTick(Set, Materials, new FluidSimulation(terrain), terrain) { FuseSeed = 0x5EED };
            if (journal)
            {
                World.Journal = History;
                Set.Journal = History;
            }
        }

        public HookedWorld World { get; } = new();

        public MachineSet Set => World.Set;

        public VolumetricInventory Pocket { get; } = new();

        public MachineTick Tick { get; }

        public EditHistory History { get; } = new();

        /// <summary>A body placed from the pocket through <see cref="Place.Machine"/>, as the brush does, then its channel set.</summary>
        public MachineRow Place(BrushCell cell, int material, int channel = 0)
        {
            Pocket.Add(material, Cell64);
            PlaceResult placed = Building.Place.Machine(World, Materials, cell, material, BrushFacing.PositiveX, Set, from: Pocket);
            Assert.Equal(Cell64, placed.Placed);
            if (channel != 0)
            {
                Assert.Equal(PortWriteResult.Set, Set.SetPort(cell, "channel", channel).Result);
            }

            return Row(cell);
        }

        public MachineRow Row(BrushCell cell)
        {
            Assert.True(Set.TryGet(cell, out MachineRow row));
            return row;
        }

        /// <summary>Stocks the pocket and deposits into one vault by its row — the crate's door, used here to fill a vault exactly.</summary>
        public void Fill(BrushCell cell, int material, long voxels)
        {
            Pocket.Add(material, voxels);
            Assert.Equal(voxels, Set.DepositInto(cell, material, voxels, Pocket));
        }

        /// <summary>A charged cell with no body — the tick reads rows, never bodies.</summary>
        public MachineRow Charged(BrushCell cell, long millijoules, int channel = 0)
        {
            var row = new MachineRow(cell, CellMaterial, BrushFacing.PositiveX, CellDefinition.DefaultPorts());
            row.SetPortUnchecked("channel", channel);
            StorageState.Write(row, millijoules);
            Set.Add(row);
            return row;
        }

        public DrainReport Drain() => Set.Drain(World, Pocket, _ => null);

        public DrainReport Arrive(ChunkPos chunk)
        {
            VoxelChunk voxels = World.ChunkVoxels(chunk);
            Set.ChunkArrived(chunk, hasEdits: true, voxels);
            return Set.Drain(World, Pocket, c => c == chunk ? voxels : null);
        }

        /// <summary>A fall's writes: unjournalled, the stack cleared (Main's bracket), then the frame's drain.</summary>
        public DrainReport Fall(BrushCell cell, int voxels = Cell64, int into = Air)
        {
            bool recording = History.Recording;
            History.Recording = false;
            for (int i = 0; i < voxels; i++)
            {
                World.SetVoxel(cell.VoxelAt(i % 4, i / 4 % 4, i / 16), into);
            }

            History.Recording = recording;
            History.Clear();
            DrainReport report = Drain();
            History.EndStep();
            return report;
        }

        public VaultContents Contents(BrushCell cell)
        {
            Assert.True(VaultState.TryRead(Row(cell), out VaultContents contents));
            return contents;
        }

        /// <summary>
        /// A quit and a reopen as the set sees it: the rows restored before the journal is wired, as
        /// Main does (Main.cs, the restore then <c>_machines.Journal</c>), and a new session's empty
        /// undo stack.
        /// </summary>
        public void Reopen()
        {
            bool recording = History.Recording;
            History.Recording = false;
            RestoreReport report = Set.Restore(Set.Snapshot(), id => Materials.Machine(id)?.Ports);
            History.Recording = recording;
            History.Clear();
            Assert.Equal(0, report.Clamped + report.Dropped + report.Defaulted);
        }

        public void AssertBooks()
        {
            Assert.True(Set.StoreExact, $"store: held {Set.HeldVoxels} + owed {Set.OwedVoxels} against entering {Set.VaultVoxelsEntering} − leaving {Set.VaultVoxelsLeaving} + deposited {Set.DepositedVoxels} − withdrawn {Set.WithdrawnVoxels}");
            Assert.True(Tick.PowerExact, "the pool identity broke");
        }
    }

    private static readonly BrushCell A = new(2, 3, 2), B = new(3, 3, 2), C = new(5, 3, 2), D = new(7, 3, 2);

    // ---------------------------------------------------------------- the codec (decision 8)

    [Fact]
    public void TheCodecRoundTripsEveryFieldAndAFreshVaultIsEmpty()
    {
        var contents = new VaultContents { WaterMillilitres = 1_234_567, Phase = VaultPhase.Fusing, FuseMillisLeft = 14_250, Wreck = true };
        contents.Add(Dirt, 40);
        contents.Add(Stone, 7);

        Assert.True(VaultState.TryRead(VaultState.Encode(contents), out VaultContents read));
        Assert.Equal([Stone, Dirt], read.Voxels.Keys);
        Assert.Equal(47, read.TotalVoxels);
        Assert.Equal(1_234_567, read.WaterMillilitres);
        Assert.Equal(VaultPhase.Fusing, read.Phase);
        Assert.Equal(14_250, read.FuseMillisLeft);
        Assert.True(read.Wreck);
        Assert.Equal((47 * MachineDefinition.VoxelMillilitres) + 1_234_567, read.UsedMillilitres);

        Assert.True(VaultState.TryRead(ReadOnlySpan<byte>.Empty, out VaultContents fresh));
        Assert.True(fresh.IsEmpty);
        Assert.False(fresh.Owes);
    }

    public static TheoryData<string, byte[]> DamagedBlobs()
    {
        var contents = new VaultContents();
        contents.Add(Dirt, 5);
        contents.Add(Stone, 3);
        byte[] good = VaultState.Encode(contents);

        byte[] Changed(Action<byte[]> change)
        {
            byte[] copy = (byte[])good.Clone();
            change(copy);
            return copy;
        }

        return new TheoryData<string, byte[]>
        {
            { "a newer version", Changed(b => b[0] = 2) },
            { "truncated", good[..^1] },
            { "a flag this build does not know", Changed(b => b[1] = 2) },
            { "a phase past dumping", Changed(b => b[2] = 3) },
            { "a zero entry", Changed(b => b[^8] = 0) },
            { "entries out of order", Changed(b => { (b[19], b[31]) = (b[31], b[19]); }) },
        };
    }

    [Theory]
    [MemberData(nameof(DamagedBlobs))]
    public void AnUnknownOrDamagedBlobIsNeverReadAsEmpty(string what, byte[] blob)
    {
        // Decision 8: strict, unlike the cell's codec. A vault read as empty is one the pick takes
        // apart with its contents still inside.
        Assert.False(VaultState.TryRead(blob, out _), what);
    }

    // ---------------------------------------------------------------- the grammar (decisions 1, 2)

    private static (GameContent Content, LoadResult Result) LoadMachine(string json)
    {
        InMemoryContentSource source = TestContent.BaseGame()
            .Add("vaults", "mod.json", """{ "id": "vaults", "name": "Vaults", "version": "1" }""")
            .Add("vaults", "materials/box.json", json);
        var content = new GameContent();
        return (content, content.Load(source));
    }

    [Fact]
    public void AVaultAndACrateLoadFromTheirFiles()
    {
        (GameContent content, LoadResult result) = LoadMachine("""
            { "id": "box", "unit": "cells",
              "machine": { "kind": "vault", "insideCubicMetres": 100, "holdWattsPerCubicMetre": { "water": 25, "blocks": 2.5 }, "holdsWater": true,
                "links": { "power": { "carries": "power", "role": "sink", "via": "channel", "port": "channel" } },
                "ports": { "channel": { "default": 0, "min": 0, "max": 15 }, "enabled": { "default": 1, "min": 0, "max": 1 } } } }
            """);
        Assert.True(result.Success, string.Join("; ", result.Errors));
        MachineDefinition vault = content.Materials.Get(Identifier.Parse("vaults:box")).Machine!;
        Assert.Equal(MachineKind.Vault, vault.Kind);
        Assert.Equal(100 * MachineDefinition.MillilitresPerCubicMetre, vault.InsideMillilitres);
        Assert.Equal(25_000, vault.WaterHoldMilliwattsPerCubicMetre);
        Assert.Equal(2_500, vault.BlockHoldMilliwattsPerCubicMetre);
        Assert.True(vault.HoldsWater);
        Assert.True(vault.IsChannelVault);

        (GameContent crateContent, LoadResult crateResult) = LoadMachine("""{ "id": "box", "unit": "cells", "machine": { "kind": "vault", "insideCubicMetres": 1 } }""");
        Assert.True(crateResult.Success, string.Join("; ", crateResult.Errors));
        MachineDefinition crate = crateContent.Materials.Get(Identifier.Parse("vaults:box")).Machine!;
        Assert.False(crate.IsChannelVault);
        Assert.False(crate.HoldsWater);
    }

    [Theory]
    [InlineData("""{ "kind": "vault", "insideCubicMetres": 0 }""", "insideCubicMetres must be within")]
    [InlineData("""{ "kind": "vault", "insideCubicMetres": 1000001 }""", "insideCubicMetres must be within")]
    [InlineData("""{ "kind": "vault", "insideCubicMetres": 1, "holdWattsPerCubicMetre": { "water": 0.0005 } }""", "whole number of milliwatts")]
    [InlineData("""{ "kind": "vault", "insideCubicMetres": 1, "holdWattsPerCubicMetre": { "blocks": 1 } }""", "is a crate")]
    [InlineData("""{ "kind": "vault", "insideCubicMetres": 1, "holdsWater": true }""", "is a crate")]
    [InlineData("""{ "kind": "vault", "insideCubicMetres": 1, "holdWattsPerCubicMetre": { "watr": 1 } }""", "did you mean 'water'")]
    [InlineData("""
        { "kind": "vault", "insideCubicMetres": 1,
          "links": { "power": { "carries": "power", "role": "sink", "via": "channel", "port": "channel" },
                     "water": { "carries": "fluid", "role": "sink", "via": "channel", "port": "channel" } },
          "ports": { "channel": { "default": 0, "min": 0, "max": 15 } } }
        """, "exactly one link")]
    [InlineData("""{ "kind": "vault" }""", "insideCubicMetres must be more than 0")]
    public void AVaultThatCouldNotBeServedIsRefusedNamingTheRule(string machine, string mentioning)
    {
        (_, LoadResult result) = LoadMachine($$"""{ "id": "box", "unit": "cells", "machine": {{machine}} }""");

        Assert.Contains(result.Errors, e => e.Contains("box.json") && e.Contains(mentioning));
    }

    // ---------------------------------------------------------------- the store (decisions 9, 14, 22, 27)

    [Fact]
    public void DepositsFillTheCheapestVaultFirstAndWithdrawalsTakeTheLastFilled()
    {
        var rig = new Rig();
        rig.Place(A, Small);
        rig.Place(B, High);
        rig.Place(C, Small);

        // The high grade bills less a cubic metre, so it fills first; then the two small ones by position.
        Assert.Equal([B, A, C], rig.Set.StoreOf(0).Select(r => r.Cell));

        rig.Pocket.Add(Dirt, 700);
        Assert.Equal(700, rig.Set.Deposit(0, Dirt, 700, rig.Pocket));
        Assert.Equal(640, rig.Contents(B).VoxelsOf(Dirt));
        Assert.Equal(60, rig.Contents(A).VoxelsOf(Dirt));
        Assert.Equal(0, rig.Contents(C).VoxelsOf(Dirt));
        Assert.Equal(0, rig.Pocket.Voxels(Dirt));

        // The last filled gives first.
        Assert.Equal(80, rig.Set.Withdraw(0, Dirt, 80, rig.Pocket));
        Assert.Equal(0, rig.Contents(A).VoxelsOf(Dirt));
        Assert.Equal(620, rig.Contents(B).VoxelsOf(Dirt));
        Assert.Equal(80, rig.Pocket.Voxels(Dirt));

        // A full store takes what fits and leaves the rest in the pocket.
        rig.Pocket.Add(Dirt, 1000);
        Assert.Equal(20 + 64 + 64, rig.Set.Deposit(0, Dirt, 1080, rig.Pocket));
        Assert.Equal(1080 - 148, rig.Pocket.Voxels(Dirt));
        rig.AssertBooks();
    }

    [Fact]
    public void AVaultSwitchedOffIsOutOfTheStoreAndItsChannelIsItsOwn()
    {
        var rig = new Rig();
        rig.Place(A, Small);
        rig.Place(B, Small);
        rig.Place(C, Small, channel: 3);
        rig.Set.SetPort(B, MachineTick.EnabledPort, 0);

        Assert.Equal([A], rig.Set.StoreOf(0).Select(r => r.Cell));
        Assert.Equal([C], rig.Set.StoreOf(3).Select(r => r.Cell));

        // SetPort carries the contents with the row (decision 11): nothing to move, nothing to count.
        rig.Fill(A, Dirt, 10);
        rig.Set.SetPort(A, "channel", 3);
        Assert.Equal(10, rig.Contents(A).VoxelsOf(Dirt));
        Assert.Equal([A, C], rig.Set.StoreOf(3).Select(r => r.Cell));
        rig.AssertBooks();
    }

    [Fact]
    public void ACrateIsOpenedByItsRowAndBelongsToNoStore()
    {
        var rig = new Rig();
        rig.Place(A, Crate);
        Assert.Empty(rig.Set.StoreOf(0));

        rig.Fill(A, Clay, 64);
        rig.Pocket.Add(Clay, 5);
        Assert.Equal(0, rig.Set.DepositInto(A, Clay, 5, rig.Pocket));
        Assert.Equal(64, rig.Set.WithdrawFrom(A, Clay, 100, rig.Pocket));
        Assert.True(rig.Contents(A).IsEmpty);
        rig.AssertBooks();
    }

    [Fact]
    public void AnOverfullVaultKeepsWhatItHoldsRefusesDepositsAndIsCounted()
    {
        // Decision 11: a grade retuned smaller keeps its contents at restore, never clamped.
        var rig = new Rig();
        rig.Place(A, Small);
        var contents = new VaultContents();
        contents.Add(Dirt, 100);
        VaultState.Write(rig.Row(A), contents);

        RestoreReport report = rig.Set.Restore(rig.Set.Snapshot(), id => Materials.Machine(id)?.Ports);

        Assert.Equal(1, report.Overfull);
        Assert.Contains("1 vault(s) overfull", report.ToString());
        Assert.Equal(100, rig.Contents(A).VoxelsOf(Dirt));
        rig.Pocket.Add(Dirt, 1);
        Assert.Equal(0, rig.Set.Deposit(0, Dirt, 1, rig.Pocket));
    }

    // ---------------------------------------------------------------- the bill and the fuse (decisions 6, 7)

    [Fact]
    public void TheBillRoundsUpAndDoesNotOverflow()
    {
        MachineDefinition small = Materials.Machine(Small)!;
        var metre = new VaultContents();
        metre.Add(Dirt, 64);

        // 2.5 W a m³ of blocks × 1 m³ × 0.5 s = 1.25 J.
        Assert.Equal(1_250, MachineTick.BillOf(small, metre, 500));

        // One voxel at a god vault's 64 mW a m³ for half a second is half a millijoule: never free.
        MachineDefinition god = VaultOf(1_000_000, 640, 64);
        var one = new VaultContents();
        one.Add(Dirt, 1);
        Assert.Equal(1, MachineTick.BillOf(god, one, 500));

        // A god vault full of water: 640 kW for half a second, 320 kJ.
        var full = new VaultContents { WaterMillilitres = 1_000_000 * MachineDefinition.MillilitresPerCubicMetre };
        Assert.Equal(320_000_000, MachineTick.BillOf(god, full, 500));

        // At the engine's dearest hold the numerator is 10⁹ × 10¹² × 500, past a long; the bill is not.
        MachineDefinition dearest = VaultOf(1_000_000, MachineDefinition.MaxHoldMilliwattsPerCubicMetre, 0);
        Assert.Equal(500_000_000_000_000, MachineTick.BillOf(dearest, full, 500));

        Assert.Equal(0, MachineTick.BillOf(small, new VaultContents(), 500));
    }

    [Fact]
    public void TwoVaultsBothBillAndTheCellsFallByExactlyTheHeldTerm()
    {
        var rig = new Rig();
        rig.Place(A, Small);
        rig.Place(B, High);
        rig.Fill(A, Dirt, 64);
        rig.Fill(B, Dirt, 640);
        MachineRow cell = rig.Charged(D, 1_000_000);

        MachineTickReport report = rig.Tick.Run();

        // 1.25 J for the small one's cubic metre, 0.4 W × 10 m³ × 0.5 s = 2 J for the high one.
        Assert.Equal(1_250 + 2_000, report.HeldMillijoules);
        Assert.Equal(1_000_000 - 3_250, StorageState.Read(cell));
        Assert.Equal(2, rig.Tick.VaultBillsPaid);
        Assert.Equal(VaultPhase.Holding, rig.Contents(A).Phase);
        rig.AssertBooks();
    }

    [Fact]
    public void ThePoolPaysTheCheapestVaultFirstAndEachBillIsAllOrNothing()
    {
        var rig = new Rig();
        rig.Place(A, Small);
        rig.Place(B, High);
        rig.Fill(A, Dirt, 64);
        rig.Fill(B, Dirt, 640);
        rig.Charged(D, 2_500); // enough for the high one's 2 J, not both

        rig.Tick.Run();

        Assert.Equal(VaultPhase.Holding, rig.Contents(B).Phase);
        Assert.Equal(VaultPhase.Fusing, rig.Contents(A).Phase);
        Assert.Equal(2_000, rig.Tick.HeldMillijoules);
        rig.AssertBooks();
    }

    [Fact]
    public void AnUnpaidBillLightsTheFuseAPaidOnePutsItOutAndAFuseBurnedThroughDumps()
    {
        var rig = new Rig();
        rig.Place(A, Small);
        rig.Fill(A, Dirt, 64);

        rig.Tick.Run();
        VaultContents lit = rig.Contents(A);
        Assert.Equal(VaultPhase.Fusing, lit.Phase);
        Assert.InRange(lit.FuseMillisLeft, MachineTick.FuseMinMillis, MachineTick.FuseMaxMillis);
        Assert.Equal(1, rig.Tick.FusesArmed);

        // A boundary that pays in full disarms it.
        MachineRow cell = rig.Charged(D, 1_250);
        rig.Tick.Run();
        Assert.Equal(VaultPhase.Holding, rig.Contents(A).Phase);
        Assert.Equal(0, rig.Contents(A).FuseMillisLeft);
        Assert.Equal(1, rig.Tick.FusesDisarmed);

        // Broke again: it lights, and burns down by the boundary's milliseconds to the dump.
        rig.Tick.Run();
        int length = rig.Contents(A).FuseMillisLeft;
        int boundaries = 0;
        while (rig.Contents(A).Phase == VaultPhase.Fusing)
        {
            rig.Tick.Run();
            boundaries++;
            Assert.True(boundaries < 100, "the fuse never burned through");
        }

        Assert.Equal((length + 499) / 500, boundaries);
        VaultContents dumping = rig.Contents(A);
        Assert.Equal(VaultPhase.Dumping, dumping.Phase);
        Assert.Equal(64, dumping.VoxelsOf(Dirt));
        Assert.Equal(64, rig.Set.OwedVoxels);
        Assert.Equal(0, rig.Set.HeldVoxels);
        Assert.Empty(rig.Set.StoreOf(0));

        // Once it burned out nothing saves it (decision 34): power back pays nothing and changes nothing.
        rig.Set.SetCharge(cell.Cell, 1_000_000);
        rig.Tick.Run();
        Assert.Equal(VaultPhase.Dumping, rig.Contents(A).Phase);
        Assert.Equal(1, rig.Tick.FusesBurnt);
        rig.AssertBooks();
    }

    [Fact]
    public void AnEmptyVaultCostsNothingAndNeverFuses()
    {
        var rig = new Rig();
        rig.Place(A, Small);
        for (int i = 0; i < 50; i++) rig.Tick.Run();

        Assert.Equal(VaultPhase.Holding, rig.Contents(A).Phase);
        Assert.Equal(0, rig.Tick.FusesArmed + rig.Tick.VaultBillsUnpaid);
        Assert.Equal(0, rig.Tick.HeldMillijoules);
    }

    [Fact]
    public void ASwitchedOffVaultStillPays()
    {
        // Decision 27: enabled 0 closes it to deposits and withdrawals, and it keeps paying.
        var rig = new Rig();
        rig.Place(A, Small);
        rig.Fill(A, Dirt, 64);
        rig.Set.SetPort(A, MachineTick.EnabledPort, 0);
        rig.Charged(D, 1_000_000);

        rig.Tick.Run();

        Assert.Equal(1_250, rig.Tick.HeldMillijoules);
        rig.Pocket.Add(Dirt, 1);
        Assert.Equal(0, rig.Set.DepositInto(A, Dirt, 1, rig.Pocket));
    }

    [Fact]
    public void ASaveDuringAFuseResumesAtTheSavedMilliseconds()
    {
        var rig = new Rig();
        rig.Place(A, Small);
        rig.Fill(A, Dirt, 64);
        rig.Tick.Run();
        rig.Tick.Run();
        int saved = rig.Contents(A).FuseMillisLeft;

        rig.Reopen();
        Assert.Equal(VaultPhase.Fusing, rig.Contents(A).Phase);
        Assert.Equal(saved, rig.Contents(A).FuseMillisLeft);

        rig.Tick.Run();
        Assert.Equal(saved - 500, rig.Contents(A).FuseMillisLeft);
    }

    [Fact]
    public void AFuseIsSeededAndLeansShorterTheLessTheChannelPays()
    {
        static int Draw(ulong seed, long charge)
        {
            var rig = new Rig();
            rig.Tick.FuseSeed = seed;
            rig.Place(A, Small);
            rig.Fill(A, Dirt, 64);
            if (charge > 0) rig.Charged(D, charge);
            rig.Tick.Run();
            return rig.Contents(A).FuseMillisLeft;
        }

        Assert.Equal(Draw(7, 0), Draw(7, 0));

        // A channel that pays nothing draws in the lower three quarters of the window: 10–15 s.
        for (ulong seed = 0; seed < 40; seed++)
        {
            int broke = Draw(seed, 0);
            Assert.InRange(broke, 10_000, 15_000);
            Assert.True(Draw(seed, 1_249) >= broke, "a channel just short never draws shorter than a broke one");
        }
    }

    // ---------------------------------------------------------------- the pick (decisions 3, 4)

    [Fact]
    public void APickMovesAHoldingVaultsContentsToItsSistersAndTheUndoGetsAnEmptyVaultBack()
    {
        var rig = new Rig(journal: true);
        rig.Place(A, Small);
        rig.Place(C, High);
        rig.History.EndStep();
        rig.Fill(A, Dirt, 50);

        CarveResult swing = Carve.Voxel(rig.World, Materials, A.VoxelAt(1, 1, 1), Pick, rig.Pocket, rig.Set);
        Assert.Equal(1, swing.Removed);

        // Moved at the first voxel, before the drain: the vault is empty and C holds it all.
        Assert.True(rig.Contents(A).IsEmpty);
        Assert.Equal(50, rig.Contents(C).VoxelsOf(Dirt));
        Assert.Equal(1, rig.Set.VaultMovesAtPick);

        DrainReport drained = rig.Drain();
        rig.History.EndStep();
        Assert.Equal(1, drained.Dismantled);
        Assert.False(rig.Set.TryGet(A, out _));
        Assert.Equal(Cell64, rig.Pocket.Voxels(Small));
        Assert.Equal(0, rig.Set.VaultVoxelsLeaving);
        rig.AssertBooks();

        // Ctrl+Z puts the vault back empty, its body from the pocket; the contents stay with C.
        UndoReport undone = rig.History.Undo(rig.World, Materials, rig.Pocket, rig.Set);
        Assert.Equal(UndoOutcome.Undone, undone.Outcome);
        Assert.True(rig.Contents(A).IsEmpty);
        Assert.Equal(50, rig.Contents(C).VoxelsOf(Dirt));
        Assert.Equal(Cell64, rig.World.BodyVoxelsAt(A, Small));
        rig.AssertBooks();
    }

    [Fact]
    public void APickWithNowhereToPutTheContentsCarvesNothing()
    {
        var rig = new Rig();
        rig.Place(A, Small);
        rig.Place(C, Small, channel: 1); // a vault on another channel is no sister
        rig.Fill(A, Dirt, 10);

        CarveResult swing = Carve.Voxel(rig.World, Materials, A.VoxelAt(0, 0, 0), Pick, rig.Pocket, rig.Set);
        rig.Drain();

        Assert.Equal(0, swing.Removed);
        Assert.Equal(1, swing.TooHard);
        Assert.Equal(Cell64, rig.World.BodyVoxelsAt(A, Small));
        Assert.Equal(10, rig.Contents(A).VoxelsOf(Dirt));
        Assert.Equal(1, rig.Set.PickVoxelsRefused);
        rig.AssertBooks();

        // Empty, it comes apart like any machine.
        rig.Set.WithdrawFrom(A, Dirt, 10, rig.Pocket);
        Assert.Equal(1, Carve.Voxel(rig.World, Materials, A.VoxelAt(0, 0, 0), Pick, rig.Pocket, rig.Set).Removed);
        Assert.Equal(1, rig.Drain().Dismantled);
    }

    [Fact]
    public void AtRangeAVaultHoldingAnythingIsTooHardWhateverTheRoom()
    {
        // Decision 17: a stray excavator bite 500 m away never moves a store.
        var rig = new Rig();
        rig.Place(A, Small);
        rig.Place(C, High);
        rig.Fill(A, Dirt, 1);

        CarveResult bite = Carve.Cube(rig.World, Materials, A.OriginVoxel, 5f, rig.Pocket, rig.Set.AtRange);
        Assert.Equal(0, bite.Removed);
        Assert.Equal(Cell64, rig.World.BodyVoxelsAt(A, Small));

        // An empty one comes away whole at range.
        CarveResult empty = Carve.Cube(rig.World, Materials, C.OriginVoxel, 5f, rig.Pocket, rig.Set.AtRange);
        Assert.Equal(Cell64, empty.Removed);
    }

    [Fact]
    public void ACrateHoldingAnythingIsTooHardForItHasNoSisters()
    {
        var rig = new Rig();
        rig.Place(A, Crate);
        rig.Place(C, High);
        rig.Fill(A, Clay, 3);

        Assert.Equal(0, Carve.Voxel(rig.World, Materials, A.OriginVoxel, Pick, rig.Pocket, rig.Set).Removed);
        Assert.Equal(3, rig.Contents(A).VoxelsOf(Clay));
    }

    [Fact]
    public void TwoFullVaultsInOneSwingNeverStrandABody()
    {
        // IM-3's pass, fact 2. A and B hold 32 each in 64-voxel vaults; C has room for 32. At the
        // drain, each would have been allowed on room the other then took. Moving at the first
        // voxel, the first of the pair goes into its sister and the second is refused: one is
        // dismantled, one stands full, nothing is a wreck and nothing leaves the books.
        var rig = new Rig();
        rig.Place(A, Small);
        rig.Place(B, Small);
        rig.Place(C, Small);
        rig.Fill(A, Dirt, 32);
        rig.Fill(B, Dirt, 32);
        rig.Fill(C, Dirt, 32);

        Carve.Tunnel(rig.World, Materials, A.OriginVoxel, 5f, rig.Pocket, rig.Set);
        DrainReport drained = rig.Drain();

        Assert.Equal(1, drained.Dismantled);
        Assert.Equal(0, drained.Wrecked);
        Assert.Equal(0, rig.Set.VaultsWrecked);
        MachineRow standing = rig.Set.TryGet(A, out MachineRow a) ? a : rig.Row(B);
        Assert.Equal(64, VaultState.TryRead(standing, out VaultContents full) ? full.VoxelsOf(Dirt) : -1);
        Assert.Equal(Cell64, rig.World.BodyVoxelsAt(standing.Cell, Small));
        Assert.Equal(32, rig.Contents(C).VoxelsOf(Dirt));
        Assert.Equal(Cell64, rig.Pocket.Voxels(Small));
        Assert.Equal(0, rig.Set.VaultVoxelsLeaving);
        rig.AssertBooks();
    }

    [Fact]
    public void AVaultWhoseBytesThisBuildCannotReadRefusesThePickAndTheUndoAndIsNeverBilled()
    {
        var rig = new Rig(journal: true);
        rig.Place(A, Small);
        rig.Place(C, High);
        rig.Row(A).KindState = [9, 1, 2, 3];

        Assert.True(rig.Set.HoldsAnything(rig.Row(A)));
        Assert.Equal(0, Carve.Voxel(rig.World, Materials, A.OriginVoxel, Pick, rig.Pocket, rig.Set).Removed);
        Assert.Equal(1, rig.Set.UnreadableVaults);

        // The placement is still the last step: undoing it would take the unreadable row away.
        UndoReport refused = rig.History.Undo(rig.World, Materials, rig.Pocket, rig.Set);
        Assert.Equal(UndoOutcome.VaultHolds, refused.Outcome);
        Assert.True(rig.Set.TryGet(A, out _));

        rig.Charged(D, 1_000_000);
        rig.Tick.Run();
        Assert.Equal(0, rig.Tick.HeldMillijoules);
        Assert.Equal([9, 1, 2, 3], rig.Row(A).KindState);
    }

    // ---------------------------------------------------------------- the wreck (decision 5)

    [Fact]
    public void AFallMakesAWreckThatKeepsItsContentsAndItsCell()
    {
        var rig = new Rig();
        rig.Place(A, Small);
        rig.Fill(A, Dirt, 40);

        DrainReport report = rig.Fall(A, voxels: 20);

        Assert.Equal(1, report.Wrecked);
        Assert.Equal(0, report.DestroyedAtDrain);
        VaultContents wreck = rig.Contents(A);
        Assert.True(wreck.Wreck);
        Assert.Equal(VaultPhase.Dumping, wreck.Phase);
        Assert.Equal(40, wreck.VoxelsOf(Dirt));
        Assert.Equal(0, rig.World.BodyVoxelsAt(A, Small));
        Assert.Equal(0, rig.Pocket.Voxels(Small)); // a fall credits nothing
        Assert.Equal(40, rig.Set.OwedVoxels);
        Assert.Empty(rig.Set.StoreOf(0));
        rig.AssertBooks();

        // It holds its cell: no machine goes there, and a block placed in it changes nothing.
        rig.Pocket.Add(High, Cell64);
        Assert.Equal(0, Building.Place.Machine(rig.World, Materials, A, High, BrushFacing.PositiveX, rig.Set, from: rig.Pocket).Placed);
        rig.World.SetVoxel(A.VoxelAt(2, 2, 2), Stone);
        Assert.False(rig.Drain().DidAnything);
        Assert.True(rig.Contents(A).Wreck);

        // And an empty vault a fall takes is destroyed as any machine is.
        rig.Place(C, Small);
        Assert.Equal(1, rig.Fall(C).DestroyedAtDrain);
        Assert.False(rig.Set.TryGet(C, out _));
    }

    [Fact]
    public void AWreckIsNeverRebuiltAtArrivalAndSurvivesAReopen()
    {
        var rig = new Rig();
        rig.Place(A, Small);
        rig.Fill(A, Dirt, 40);
        rig.Fall(A);

        rig.Reopen();
        DrainReport arrived = rig.Arrive(A.OriginVoxel.Chunk);

        Assert.Equal(0, arrived.Rebuilt);
        Assert.Equal(0, rig.World.BodyVoxelsAt(A, Small));
        Assert.True(rig.Contents(A).Wreck);
        Assert.Equal(40, rig.Contents(A).VoxelsOf(Dirt));
        rig.AssertBooks();
    }

    [Fact]
    public void BothOfTheReconcilesRemoveBranchesKeepAVaultsContents()
    {
        // Rock where the body stood — the returned branch: the body back to the pocket, a wreck kept.
        var rock = new Rig();
        rock.Place(A, Small);
        rock.Fill(A, Dirt, 12);
        for (int i = 0; i < Cell64; i++) rock.World.Seed(A.VoxelAt(i % 4, i / 4 % 4, i / 16), Stone);
        DrainReport returned = rock.Arrive(A.OriginVoxel.Chunk);
        Assert.Equal(1, returned.Wrecked);
        Assert.Equal(0, returned.Returned);
        Assert.Equal(Cell64, rock.Pocket.Voxels(Small));
        Assert.Equal(12, rock.Contents(A).VoxelsOf(Dirt));
        rock.AssertBooks();

        // Part of a body — the destroyed branch: a wreck, no credit.
        var partial = new Rig();
        partial.Place(A, Small);
        partial.Fill(A, Dirt, 12);
        for (int i = 0; i < 10; i++) partial.World.Seed(A.VoxelAt(i % 4, i / 4 % 4, i / 16), Air);
        DrainReport destroyed = partial.Arrive(A.OriginVoxel.Chunk);
        Assert.Equal(1, destroyed.Wrecked);
        Assert.Equal(0, destroyed.Destroyed);
        Assert.Equal(0, partial.Pocket.Voxels(Small));
        Assert.Equal(12, partial.Contents(A).VoxelsOf(Dirt));
        Assert.Equal(0, partial.World.BodyVoxelsAt(A, Small));
        partial.AssertBooks();

        // A holding vault whose chunk never landed — all air — is rebuilt, as any machine is.
        var lost = new Rig();
        lost.Place(A, Small);
        lost.Fill(A, Dirt, 12);
        for (int i = 0; i < Cell64; i++) lost.World.Seed(A.VoxelAt(i % 4, i / 4 % 4, i / 16), Air);
        Assert.Equal(1, lost.Arrive(A.OriginVoxel.Chunk).Rebuilt);
        Assert.False(lost.Contents(A).Wreck);
    }

    // ---------------------------------------------------------------- the undo (decision 10)

    [Fact]
    public void TheUndoRefusesToTakeAwayAVaultThatHoldsAnythingBeforeWritingAnything()
    {
        var rig = new Rig(journal: true);
        rig.Place(A, Small);
        rig.Fill(A, Dirt, 5);

        UndoReport refused = rig.History.Undo(rig.World, Materials, rig.Pocket, rig.Set);

        Assert.Equal(UndoOutcome.VaultHolds, refused.Outcome);
        Assert.Equal(1, refused.StepsLeft);
        Assert.Equal(Cell64, rig.World.BodyVoxelsAt(A, Small));
        Assert.Equal(5, rig.Contents(A).VoxelsOf(Dirt));
        Assert.Equal(0, rig.Pocket.Voxels(Small));

        // Emptied, the same step undoes: the body back to the pocket, the row gone.
        rig.Set.WithdrawFrom(A, Dirt, 5, rig.Pocket);
        Assert.Equal(UndoOutcome.Undone, rig.History.Undo(rig.World, Materials, rig.Pocket, rig.Set).Outcome);
        Assert.False(rig.Set.TryGet(A, out _));
        Assert.Equal(Cell64, rig.Pocket.Voxels(Small));
        rig.AssertBooks();
    }

    // ---------------------------------------------------------------- the identity (decision 19)

    [Fact]
    public void TheStoreIdentityHoldsOverAThousandRandomOperationsTheUndoAmongThem()
    {
        var rig = new Rig(journal: true);
        var random = new Random(0x13);
        BrushCell[] cells = [.. Enumerable.Range(0, 10).Select(i => new BrushCell(2 + (2 * i), 3, 2))];
        int[] kinds = [Small, High, Small, Crate, Small, High, Small, Small, Crate, Small];
        rig.History.Recording = false; // the bank is the rig's, not an edit the undo could take back
        MachineRow power = rig.Charged(new BrushCell(0, 10, 0), 3_000);
        rig.History.Recording = true;
        rig.History.EndStep();
        long leavingAtReopens = 0;
        var seen = new HashSet<string>();

        for (int op = 0; op < 1000; op++)
        {
            int i = random.Next(cells.Length);
            BrushCell cell = cells[i];
            bool exists = rig.Set.TryGet(cell, out MachineRow row);
            bool wreck = exists && VaultState.TryRead(row, out VaultContents c) && c.Wreck;
            switch (random.Next(10))
            {
                case 0 when !exists:
                    rig.Place(cell, kinds[i], channel: 0);
                    rig.History.EndStep();
                    seen.Add("place");
                    break;
                case 1:
                case 2:
                {
                    int material = random.Next(2) == 0 ? Dirt : Clay;
                    long voxels = random.Next(1, 120);
                    rig.Pocket.Add(material, voxels);
                    if (exists && kinds[i] == Crate) rig.Set.DepositInto(cell, material, voxels, rig.Pocket);
                    else rig.Set.Deposit(0, material, voxels, rig.Pocket);
                    seen.Add("deposit");
                    break;
                }

                case 3:
                    if (exists && kinds[i] == Crate) rig.Set.WithdrawFrom(cell, random.Next(2) == 0 ? Dirt : Clay, random.Next(1, 80), rig.Pocket);
                    else rig.Set.Withdraw(0, random.Next(2) == 0 ? Dirt : Clay, random.Next(1, 80), rig.Pocket);
                    seen.Add("withdraw");
                    break;
                case 4 when exists && !wreck:
                    Carve.Voxel(rig.World, Materials, cell.VoxelAt(random.Next(4), random.Next(4), random.Next(4)), Pick, rig.Pocket, rig.Set);
                    rig.Drain();
                    rig.History.EndStep();
                    seen.Add(rig.Set.TryGet(cell, out _) ? "pick refused" : "pick");
                    break;
                case 5 when exists && !wreck && random.Next(3) == 0:
                    rig.Fall(cell, voxels: random.Next(1, Cell64 + 1));
                    seen.Add("fall");
                    break;
                case 6:
                    rig.History.Undo(rig.World, Materials, rig.Pocket, rig.Set);
                    seen.Add("undo");
                    break;
                case 7 when random.Next(5) == 0:
                    leavingAtReopens += rig.Set.HeldVoxels + rig.Set.OwedVoxels;
                    rig.Reopen();
                    power = rig.Row(power.Cell);
                    seen.Add("reopen");
                    break;
                case 8:
                case 9:
                    if (random.Next(20) == 0) rig.Set.SetCharge(power.Cell, random.Next(0, 5_000));
                    rig.Tick.Run();
                    seen.Add("boundary");
                    break;
            }

            rig.AssertBooks();
            Assert.Equal(leavingAtReopens, rig.Set.VaultVoxelsLeaving);
            Assert.True(rig.Set.TryGet(power.Cell, out _), $"op {op}: the bank's row went, after {string.Join(", ", seen)} (cell {cell}, exists {exists}, wreck {wreck})");
        }

        Assert.Superset(new HashSet<string> { "place", "deposit", "withdraw", "pick", "pick refused", "fall", "undo", "reopen", "boundary" }, seen);
        Assert.True(rig.Tick.FusesArmed > 0, "no bill ever went unpaid");
        Assert.True(rig.Set.VaultMovesAtPick > 0, "no pick ever moved contents");
        Assert.True(rig.Set.VaultsWrecked > 0, "no fall ever made a wreck");
    }
}
