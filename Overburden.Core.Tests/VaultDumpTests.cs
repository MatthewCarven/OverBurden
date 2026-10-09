using Overburden.Core.Building;
using Overburden.Core.Content;
using Overburden.Core.Editing;
using Overburden.Core.Fluid;
using Overburden.Core.Hydrology;
using Overburden.Core.Lighting;
using Overburden.Core.Machines;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// IM-5a: the dump's water (IDEAL-MACHINES-PLAN, "IM-5 — the code-level pass", decisions 2–10, and the
/// re-read of 2026-10-09). A burnt-out vault's water goes to the lake its mouth stands over, paced by the
/// release clock, or into the cells around it as a ball within a budget; a new dump past 10 ML in the
/// workings is decohered; a vault in an unloaded chunk owes until the water is in; and the dump ends — a
/// standing vault back to holding, a wreck removed with nothing for Ctrl+Z. Every identity exact throughout.
/// </summary>
/// <remarks>
/// The fixture is the machine tick's: the bowl over 48..79 with its floor at 31 and the rim at 39
/// everywhere else, and a room dug under the rim at X 32..51, Y 8..23, Z 32..51 — chunk (1, 0, 1). A
/// vault on the bowl's bed stands above the seed surface, so its water is the lake's; one in the room
/// is in the workings, so its water is the cells'.
/// </remarks>
public class VaultDumpTests
{
    private const ulong WorldSeed = 0x5EEDD0;
    private const int Cell = HeightField.DefaultCellVoxels;
    private const int Pitch = 128 / Cell;
    private const long Litre = FluidConstants.MillilitresPerLitre;
    private const long CubicMetre = FluidConstants.MillilitresPerCubicMetre;
    private const long Megalitre = 1_000_000L * Litre;
    private const int Floor = 31, Rim = 39;

    private const int Air = 0, Stone = 1, Vault = 2, CellMaterial = 3;

    private static readonly MachineDefinition VaultDefinition = new()
    {
        Kind = MachineKind.Vault,
        InsideMillilitres = 100 * MachineDefinition.MillilitresPerCubicMetre,
        WaterHoldMilliwattsPerCubicMetre = 25_000,
        BlockHoldMilliwattsPerCubicMetre = 2_500,
        HoldsWater = true,
        Links = [new MachineLink("power", MachineMedium.Power, MachineRole.Sink, MachineLink.ChannelVia, null, "channel")],
        Ports = [new PortDefinition("channel", 0, 0, 15), new PortDefinition(MachineTick.EnabledPort, 1, 0, 1)],
    };

    private static readonly MachineDefinition CellDefinition = new()
    {
        Kind = MachineKind.Storage,
        CapacityMillijoules = 1_000_000_000_000L,
        Links = [new MachineLink("charge", MachineMedium.Power, MachineRole.Sink, MachineLink.ChannelVia, null, "channel")],
        Ports = [new PortDefinition("channel", 0, 0, 15), new PortDefinition(MachineTick.EnabledPort, 1, 0, 1)],
    };

    private static readonly MaterialTable Materials = MaterialTable.ForTesting(
        //            air    stone  vault  cell
        isOpaque: [false, true, true, true],
        isFluid: [false, false, false, false],
        textureLayer: [0, 1, 2, 3],
        hardness: [0f, 3f, 3f, 3f],
        machine: [null, null, VaultDefinition, CellDefinition],
        unit: [default, default, MaterialUnit.Cells, MaterialUnit.Cells]);

    // The room's vaults, by position: X 32, 40 and 48 along the diagonal, on the room's floor.
    private static readonly BrushCell West = new(8, 2, 8), Middle = new(10, 2, 10), East = new(12, 2, 12);

    // The bowl's: standing on its bed (plane 32), the mouth at 36 over a floor of 31.
    private static readonly BrushCell Bed = new(14, 8, 14), BedB = new(16, 8, 16);

    // On the rim far past the window's 128 voxels: above the seed, and nobody has analysed it.
    private static readonly BrushCell OffWindow = new(60, 10, 60);

    // In the rock under the rim, west of the room, in chunk (0, 0, 1): no dug voxel within its ball.
    private static readonly BrushCell Sealed = new(4, 2, 10);

    // In a pit under the bowl's floor: the body at Y 24..27, its mouth cell 28..31 the pit's only air.
    private static readonly BrushCell Pit = new(14, 6, 14);

    private static ISurfaceHeights BowlAndRim() => new AnalyticSurface((x, z) =>
        x >= 48 && x < 80 && z >= 48 && z < 80 ? Floor : Rim);

    /// <summary>Dug voxels and open air above the seed, less what was placed, inside the loaded box.</summary>
    /// <remarks>
    /// <see cref="Stale"/> answers <c>CanHold</c> for a dug voxel whether or not its chunk is loaded — the
    /// field's baked bits for a chunk that has gone, which the re-read's fourth point is about.
    /// </remarks>
    private sealed class EditedTerrain(ISurfaceHeights seed) : IFluidTerrain
    {
        public HashSet<VoxelPos> Dug { get; } = [];

        public HashSet<VoxelPos> Placed { get; } = [];

        public HashSet<ChunkPos> Loaded { get; } = [];

        public bool Stale { get; set; }

        public bool CanHold(VoxelPos v) =>
            (Stale || Loaded.Contains(v.Chunk))
            && !Placed.Contains(v)
            && (Dug.Contains(v) || v.Y > seed.SurfaceHeightAt(v.X, v.Z));

        public bool IsLoaded(VoxelPos v) => Loaded.Contains(v.Chunk);
    }

    private sealed class Rig
    {
        public static readonly ChunkPos RoomChunk = new(1, 0, 1), CorridorChunk = new(0, 0, 1);

        public Rig()
        {
            Seed = BowlAndRim();
            Terrain = new EditedTerrain(Seed);
            Water = new WaterSystem(Seed, WorldSeed, new BasinLedger(), Pitch);
            Water.Install(Water.AnalyseFor(64, 64));
            Ceiling = new LakeCeiling(Seed, Water);
            Bounded = new LakeBoundedTerrain(Terrain, Ceiling);
            Fluid = new FluidSimulation(Bounded);
            Machines = new MachineSet(Materials);
            Tick = new MachineTick(Machines, Materials, Fluid, Bounded, Seed, Water) { FuseSeed = 0x5EED };
            Dump = new VaultDump(Machines, Fluid, Bounded, Seed, Water, Ceiling, submerged: Fluid.WakeCell);

            for (int cz = 1; cz <= 2; cz++)
            for (int cx = 0; cx <= 2; cx++)
            for (int cy = 0; cy <= 1; cy++)
            {
                Load(new ChunkPos(cx, cy, cz));
            }

            DigBox(32, 8, 32, 51, 23, 51);
            Refresh();
        }

        public ISurfaceHeights Seed { get; }

        public EditedTerrain Terrain { get; }

        public WaterSystem Water { get; }

        public LakeCeiling Ceiling { get; }

        public LakeBoundedTerrain Bounded { get; }

        public FluidSimulation Fluid { get; }

        public MachineSet Machines { get; }

        public MachineTick Tick { get; }

        public VaultDump Dump { get; private set; }

        public HashSet<ChunkPos> Reachable { get; } = [];

        public void Load(ChunkPos chunk)
        {
            if (Terrain.Loaded.Add(chunk))
            {
                Reachable.Add(chunk);
                Fluid.ChunkArrived(chunk);
            }
        }

        public void Unload(ChunkPos chunk)
        {
            Terrain.Loaded.Remove(chunk);
            Reachable.Remove(chunk);
        }

        public void DigBox(int x0, int y0, int z0, int x1, int y1, int z1)
        {
            for (int y = y0; y <= y1; y++)
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                Terrain.Dug.Add(new VoxelPos(x, y, z));
            }
        }

        public void Refresh() => Ceiling.Refresh(Reachable, Fluid.WakeCell);

        /// <summary>A vault as a burnt fuse or a fall leaves it — owing, its release clock at ten seconds — entered as a restored row is.</summary>
        public MachineRow Owing(BrushCell cell, long water, bool wreck = false, int clock = VaultDump.ReleaseMillis, long ownCharge = 0, int channel = 0)
        {
            var contents = new VaultContents
            {
                WaterMillilitres = water,
                Phase = VaultPhase.Dumping,
                FuseMillisLeft = clock,
                Wreck = wreck,
                OwnChargeMillijoules = ownCharge,
            };

            return Add(cell, contents, body: !wreck, channel);
        }

        public MachineRow Add(BrushCell cell, VaultContents contents, bool body = true, int channel = 0)
        {
            if (body)
            {
                for (int lz = 0; lz < BrushCell.Voxels; lz++)
                for (int lx = 0; lx < BrushCell.Voxels; lx++)
                for (int ly = 0; ly < BrushCell.Voxels; ly++)
                {
                    Terrain.Placed.Add(cell.VoxelAt(lx, ly, lz));
                }
            }

            var row = new MachineRow(cell, Vault, BrushFacing.PositiveX, VaultDefinition.DefaultPorts(), VaultState.Encode(contents));
            row.SetPortUnchecked("channel", channel);
            Machines.Add(row);
            return row;
        }

        /// <summary>A charged cell with no body, on a channel — the tick reads rows, never bodies.</summary>
        public MachineRow Charged(BrushCell cell, long millijoules, int channel = 0)
        {
            var row = new MachineRow(cell, CellMaterial, BrushFacing.PositiveX, CellDefinition.DefaultPorts());
            row.SetPortUnchecked("channel", channel);
            StorageState.Write(row, millijoules);
            Machines.Add(row);
            return row;
        }

        public VaultContents Contents(BrushCell cell)
        {
            Assert.True(Machines.TryGet(cell, out MachineRow row));
            Assert.True(VaultState.TryRead(row, out VaultContents contents));
            return contents;
        }

        /// <summary>The game's order: the machine tick, the dump, the lakes, then one tick of the cells.</summary>
        public DumpReport Boundary(bool step = true)
        {
            Tick.Run();
            DumpReport report = Dump.Run();
            Refresh();
            if (step)
            {
                Fluid.Step();
            }

            return report;
        }

        /// <summary>A quit and a reopen as the set sees it, and a new session's dump, which has admitted nothing.</summary>
        public void Reopen()
        {
            RestoreReport report = Machines.Restore(Machines.Snapshot(), id => Materials.Machine(id)?.Ports);
            Assert.Equal(0, report.Clamped + report.Dropped + report.Defaulted + report.Unknown);
            Dump = new VaultDump(Machines, Fluid, Bounded, Seed, Water, Ceiling, submerged: Fluid.WakeCell);
        }

        public long HeldIn(int x0, int y0, int z0, int x1, int y1, int z1)
        {
            long held = 0;
            for (int y = y0; y <= y1; y++)
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                held += Fluid.Grid.AmountAt(new VoxelPos(x, y, z));
            }

            return held;
        }

        /// <summary>
        /// Both tiers' identities, the store's water line, the dump's own twins and the pool's — and the whole
        /// world: what the vaults held is in a lake, in the cells, still in a vault, spilled or decohered.
        /// </summary>
        public void AssertBooks()
        {
            long a = Water.StoredLitres;
            Assert.Equal(
                Water.ReceivedLitres + Water.InjectedLitres + Water.EmittedLitres + Water.PouredLitres + Water.DumpedLitres
                    - Water.SpilledLitres - Water.DrawnLitres - Water.SeepedLitres - Water.PumpedLitres - Water.SiphonedLitres,
                a);

            long b = Fluid.TotalMillilitres;
            Assert.Equal(
                Fluid.AddedMillilitres + Fluid.EmittedMillilitres + Fluid.PouredMillilitres + Fluid.DumpedMillilitres - Fluid.RemovedMillilitres
                    - Fluid.DisplacedMillilitres - Fluid.HandedMillilitres - Fluid.PumpedMillilitres - Fluid.SiphonedMillilitres,
                b);
            Fluid.Publish();
            Assert.True(Fluid.Published.IdentityExact, "the published identity disagrees with the live one");

            Assert.True(Machines.StoreWaterExact, "the store's water line broke");
            Assert.True(Dump.Exact, "a twin disagrees with the dump's own counters");
            Assert.True(Tick.PowerExact, "the pool identity broke");

            Assert.Equal(
                Machines.VaultWaterEntering - Machines.VaultWaterLeaving,
                ((a - Water.InjectedLitres + Water.SpilledLitres) * Litre)
                    + (b - Fluid.AddedMillilitres + Fluid.RemovedMillilitres + Fluid.DisplacedMillilitres)
                    + Machines.HeldWaterMillilitres + Machines.OwedWaterMillilitres + Machines.WaterDecoheredMillilitres);
        }
    }

    // ---------------------------------------------------------------- a lake's share (decisions 2 and 3)

    [Fact]
    public void ALakesShareLandsEveryLitreInTwentyBoundariesAndTheSubLitreRestIsDecohered()
    {
        var rig = new Rig();
        rig.Owing(Bed, 12_345_678);
        long stored = rig.Water.StoredLitres;

        DumpReport first = rig.Boundary();
        Assert.Equal(1, first.ToLakes);
        Assert.Equal(618, first.DumpedLitres); // ceil(12,345 ÷ 20)
        Assert.Equal(12_345_678 - 618_000, rig.Contents(Bed).WaterMillilitres);
        Assert.Equal(VaultDump.ReleaseMillis - 500, rig.Contents(Bed).FuseMillisLeft);
        rig.AssertBooks();

        int boundaries = 1;
        while (rig.Contents(Bed).Phase == VaultPhase.Dumping)
        {
            rig.Boundary();
            boundaries++;
            rig.AssertBooks();
            Assert.True(boundaries < 40, "the release never ended");
        }

        Assert.Equal(20, boundaries);
        Assert.Equal(12_345, rig.Water.DumpedLitres);
        Assert.Equal(678, rig.Dump.DecoheredMillilitres);
        Assert.Equal(678, rig.Machines.WaterDecoheredMillilitres);
        Assert.Equal(0, rig.Water.SpilledLitres);
        Assert.Equal(stored + 12_345, rig.Water.StoredLitres);
        Assert.Equal(0, rig.Fluid.DumpedMillilitres);

        VaultContents after = rig.Contents(Bed);
        Assert.True(after.IsEmpty);
        Assert.Equal(VaultPhase.Holding, after.Phase);
        Assert.Equal(0, after.FuseMillisLeft);
        Assert.Equal(1, rig.Machines.DumpsEnded);
    }

    [Fact]
    public void AVaultOnTheSurfaceOffTheWindowOwesAndMovesNothing()
    {
        var rig = new Rig();
        rig.Owing(OffWindow, 5 * CubicMetre);

        DumpReport report = rig.Boundary();

        Assert.Equal(1, report.Unanalysed);
        Assert.Equal(0, report.ToLakes + report.ToCells);
        Assert.Equal(5 * CubicMetre, rig.Contents(OffWindow).WaterMillilitres);
        Assert.Equal(VaultDump.ReleaseMillis - 500, rig.Contents(OffWindow).FuseMillisLeft);
        rig.AssertBooks();
    }

    // ---------------------------------------------------------------- the cells' share (decision 4)

    [Fact]
    public void AVaultInTheWorkingsPoursABallWithinTheBudgetUntilItIsEmpty()
    {
        var rig = new Rig();
        rig.Owing(Middle, 30 * CubicMetre);

        int boundaries = 0;
        while (rig.Contents(Middle).Phase == VaultPhase.Dumping)
        {
            DumpReport report = rig.Boundary();
            boundaries++;
            Assert.InRange(report.CellsVisited, 0, VaultDump.CellsPerBoundary);
            Assert.Equal(0, report.ToLakes);
            rig.AssertBooks();
            Assert.True(boundaries < 400, "the ball never emptied the vault");
        }

        Assert.Equal(30 * CubicMetre, rig.Fluid.DumpedMillilitres);
        Assert.Equal(30 * CubicMetre, rig.Dump.DumpedMillilitres);
        Assert.Equal(0, rig.Dump.DecoheredMillilitres);
        Assert.Equal(0, rig.Water.DumpedLitres);
        Assert.Equal(30 * CubicMetre, rig.HeldIn(32, 8, 32, 51, 23, 51));
        Assert.True(rig.Contents(Middle).IsEmpty);
        Assert.Equal(VaultPhase.Holding, rig.Contents(Middle).Phase);
    }

    [Fact]
    public void ThreeBallsShareTheBudgetRoundRobinWithTheRemainderToTheFirstByPosition()
    {
        var rig = new Rig();
        rig.Owing(East, 50 * CubicMetre);
        rig.Owing(West, 50 * CubicMetre);
        rig.Owing(Middle, 50 * CubicMetre);

        DumpReport report = rig.Boundary();

        Assert.Equal(3, report.ToCells);
        Assert.Equal(VaultDump.CellsPerBoundary, report.CellsVisited);
        Assert.Equal(1_068, rig.Dump.VisitedLast[West]);
        Assert.Equal(1_066, rig.Dump.VisitedLast[Middle]);
        Assert.Equal(1_066, rig.Dump.VisitedLast[East]);
        rig.AssertBooks();
    }

    [Fact]
    public void ACellInAChunkNotLoadedToTheWaterIsPassedOverEvenWhenItsFieldStillSaysItHolds()
    {
        var rig = new Rig();
        rig.DigBox(24, 8, 40, 31, 11, 43); // a corridor west out of the room, into chunk (0, 0, 1)
        rig.Owing(new BrushCell(8, 2, 10), 30 * CubicMetre);
        rig.Terrain.Stale = true;
        rig.Unload(Rig.CorridorChunk);

        rig.Boundary(step: false);
        Assert.True(rig.Dump.DumpedMillilitres > 0, "the ball poured nothing at all");
        Assert.Equal(0, rig.HeldIn(24, 8, 40, 31, 11, 43));

        // The other arm: the chunk back, the next pass reaches the corridor.
        rig.Load(Rig.CorridorChunk);
        rig.Boundary(step: false);
        Assert.True(rig.HeldIn(24, 8, 40, 31, 11, 43) > 0, "the corridor took nothing once loaded — the test would not see a leak");
        rig.AssertBooks();
    }

    [Fact]
    public void ABallCoversTheLakeBandsBeforeItPoursSoNoLitreLandsInsideALake()
    {
        // A vault in a pit under the bowl's floor, its mouth cell the pit's only air, under a lake. A band
        // exists only for a reachable column, so the bowl's columns are taken out of the reach first: only
        // the ball's own cover can stand between its pour and the lake's volume.
        var rig = new Rig();
        rig.DigBox(56, 24, 56, 59, 31, 59);
        rig.Owing(Pit, 3 * CubicMetre);
        Assert.Equal(0L, rig.Water.Inject(rig.Water.BasinAtSample(56 / Cell, 56 / Cell), 100_000));
        rig.Reachable.RemoveWhere(chunk => chunk.X >= 1);
        rig.Refresh();

        DumpReport report = rig.Boundary(step: false);

        Assert.Equal(1, report.ToCells);
        Assert.Equal(CubicMetre, report.DumpedMillilitres); // the mouth cell's 64 voxels, and nothing in the lake
        Assert.Equal(CubicMetre, rig.HeldIn(56, 28, 56, 59, 31, 59));
        Assert.Equal(0, rig.HeldIn(48, 32, 48, 79, 40, 79));
        rig.AssertBooks();
    }

    [Fact]
    public void AWholePassThatLandsNothingRestsTheBallAndThenItTriesAgain()
    {
        var rig = new Rig();
        rig.Owing(Sealed, CubicMetre); // in the rock west of the room: its ball holds no air

        DumpReport first = rig.Boundary();
        Assert.True(first.CellsVisited > 0);
        Assert.Equal(0, first.DumpedMillilitres);
        Assert.Equal(1, first.NoRoom);
        Assert.Equal(1, rig.Dump.NoRoomRests);
        Assert.Equal(CubicMetre, rig.Contents(Sealed).WaterMillilitres);

        // Resting: twenty boundaries visit nothing and pour nothing, then a pass again.
        for (int i = 0; i < VaultDump.NoRoomWaitMillis / 500; i++)
        {
            DumpReport resting = rig.Boundary();
            Assert.Equal(0, resting.CellsVisited);
            Assert.Equal(1, resting.NoRoom);
        }

        Assert.True(rig.Boundary().CellsVisited > 0, "the ball never woke");
        Assert.Equal(2, rig.Dump.NoRoomRests);
        rig.AssertBooks();
    }

    // ---------------------------------------------------------------- the ceiling (decision 5)

    [Fact]
    public void ANewDumpPastTenMegalitresInTheWorkingsIsDecoheredAtOnceAndALakeHasNoCeiling()
    {
        var rig = new Rig();
        rig.Owing(West, 6 * Megalitre);
        rig.Owing(East, 6 * Megalitre);
        rig.Owing(Bed, 20 * Megalitre);

        DumpReport first = rig.Boundary();

        Assert.Equal(3, first.Admitted);
        Assert.Equal(2 * Megalitre, rig.Dump.CeilingDecoheredMillilitres);
        Assert.Equal(2 * Megalitre, first.DecoheredMillilitres);
        Assert.Equal(1_000_000, first.DumpedLitres); // the lake's twentieth, past any ceiling

        // The first by position kept all of its six; the second, four. Owed and poured, the two make ten.
        Assert.Equal(10 * Megalitre, rig.Contents(West).WaterMillilitres + rig.Contents(East).WaterMillilitres + rig.Fluid.DumpedMillilitres);
        Assert.True(rig.Contents(East).WaterMillilitres <= 4 * Megalitre);
        Assert.True(rig.Contents(West).WaterMillilitres > 4 * Megalitre);
        rig.AssertBooks();

        // Checked once: the next boundary admits no one and decoheres nothing more.
        Assert.Equal(0, rig.Boundary().Admitted);
        Assert.Equal(2 * Megalitre, rig.Dump.CeilingDecoheredMillilitres);

        // And a reopen admits them again with nothing to take: what was admitted fitted, and owed water only falls.
        rig.Reopen();
        DumpReport reopened = rig.Boundary();
        Assert.Equal(3, reopened.Admitted);
        Assert.Equal(0, rig.Dump.CeilingDecoheredMillilitres);
        rig.AssertBooks();
    }

    // ---------------------------------------------------------------- owed, and the end (decisions 6 and 7)

    [Fact]
    public void AVaultInTheWorkingsOwesWhileItsChunkIsUnloadedAndPoursAtTheFirstBoundaryAfter()
    {
        var rig = new Rig();
        rig.Owing(Middle, 10 * CubicMetre);
        rig.Unload(Rig.RoomChunk);

        DumpReport waiting = rig.Boundary(step: false);
        Assert.Equal(1, waiting.Unloaded);
        Assert.Equal(0, waiting.CellsVisited);
        Assert.Equal(10 * CubicMetre, rig.Contents(Middle).WaterMillilitres);
        rig.AssertBooks();

        // The chunk back is not a pour: nothing moves until the boundary.
        rig.Load(Rig.RoomChunk);
        Assert.Equal(0, rig.Fluid.DumpedMillilitres);

        DumpReport poured = rig.Boundary();
        Assert.Equal(0, poured.Unloaded);
        Assert.Equal(1, poured.ToCells);
        Assert.True(poured.DumpedMillilitres > 0);
        rig.AssertBooks();
    }

    [Fact]
    public void PowerComingBackAfterTheBurnoutPaysNothingAndChangesNothing()
    {
        var rig = new Rig();
        rig.Owing(Bed, 40 * Litre, ownCharge: 7_000);
        rig.Charged(new BrushCell(1, 1, 9), 1_000_000_000);

        while (rig.Contents(Bed).Phase == VaultPhase.Dumping)
        {
            rig.Boundary();
            Assert.Equal(0, rig.Tick.HeldMillijoules);
            Assert.Equal(0, rig.Tick.FusesDisarmed);
            rig.AssertBooks();
            Assert.True(rig.Tick.Boundaries < 40, "the dump never ended");
        }

        Assert.Equal(40, rig.Water.DumpedLitres);
        Assert.Equal(VaultPhase.Holding, rig.Contents(Bed).Phase);

        // Back in Holding and empty, its own charge goes back to the pool at the next boundary.
        rig.Boundary();
        Assert.Equal(0, rig.Contents(Bed).OwnChargeMillijoules);
        Assert.Equal(7_000, rig.Tick.OwnChargeReturnedMillijoules);
        rig.AssertBooks();
    }

    [Fact]
    public void AnEmptiedVaultHoldsAgainAndAnEmptiedWreckIsGoneWithNothingForCtrlZ()
    {
        var rig = new Rig();
        rig.Owing(Bed, 2_500);
        rig.Owing(BedB, 1_500, wreck: true, ownCharge: 5_000);
        var history = new EditHistory();
        rig.Machines.Journal = history;
        Assert.Equal(0, history.Steps);

        DumpReport first = rig.Boundary();
        Assert.Equal(1, first.WrecksRemoved);
        Assert.False(rig.Machines.TryGet(BedB, out _));
        Assert.Equal(5_000, rig.Machines.StoredMillijoulesLeaving);
        Assert.Equal(1, rig.Machines.WrecksRemoved);
        Assert.Equal(0, history.Steps);
        rig.AssertBooks();

        DumpReport second = rig.Boundary();
        Assert.Equal(1, second.Ended);
        VaultContents standing = rig.Contents(Bed);
        Assert.Equal(VaultPhase.Holding, standing.Phase);
        Assert.False(standing.Wreck);
        Assert.True(standing.IsEmpty);
        Assert.Equal(3, rig.Water.DumpedLitres); // a litre from each first, then the standing vault's second
        Assert.Equal(1_000, rig.Dump.DecoheredMillilitres); // half a litre each
        Assert.Equal(0, history.Steps);
        rig.AssertBooks();
    }

    [Fact]
    public void AnOwingVaultWithNothingInItEndsAtTheNextBoundary()
    {
        var rig = new Rig();
        rig.Owing(Middle, 0);
        rig.Owing(Bed, 0, wreck: true);

        DumpReport report = rig.Boundary();

        Assert.Equal(1, report.Ended);
        Assert.Equal(1, report.WrecksRemoved);
        Assert.Equal(VaultPhase.Holding, rig.Contents(Middle).Phase);
        Assert.False(rig.Machines.TryGet(Bed, out _));
        rig.AssertBooks();
    }

    // ---------------------------------------------------------------- through the tick (decisions 3 and 9)

    [Fact]
    public void AFuseThatBurnsOutStartsTheReleaseClockAndReleasesInTheSameBoundary()
    {
        var rig = new Rig();
        rig.Add(Bed, new VaultContents { WaterMillilitres = 20 * CubicMetre });

        int boundaries = 0;
        while (rig.Tick.FusesBurnt == 0)
        {
            rig.Boundary();
            boundaries++;
            Assert.True(boundaries < 60, "the fuse never burned out");
        }

        // The boundary it burnt: its clock set to ten seconds by the tick, a boundary run down by the dump,
        // and the lake's first twentieth already in.
        Assert.Equal(1_000, rig.Dump.Last.DumpedLitres);
        Assert.Equal(VaultDump.ReleaseMillis - 500, rig.Contents(Bed).FuseMillisLeft);
        Assert.Equal(1, rig.Dump.Last.Admitted);
        rig.AssertBooks();
    }
}
