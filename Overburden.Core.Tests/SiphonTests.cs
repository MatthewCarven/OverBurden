using System.Numerics;
using Overburden.Core.Building;
using Overburden.Core.Content;
using Overburden.Core.Fluid;
using Overburden.Core.Hydrology;
using Overburden.Core.Inventory;
using Overburden.Core.Lighting;
using Overburden.Core.Machines;
using Overburden.Core.Voxels;
using Overburden.Core.Worldgen;

namespace Overburden.Core.Tests;

/// <summary>
/// IM-4a: the siphon (IDEAL-MACHINES-PLAN decisions 23–26, as IM-4's pass took them). A click
/// enqueues a ray; the boundary resolves it with the grid in hand, moves the water through the twins
/// on both engines, stores it and pays for it — or pours it back for free — and every twin is checked
/// against the siphon's own counters, beside the Tier B identity, the pool's and the store's.
/// </summary>
/// <remarks>
/// The fixture is <see cref="MachineTickTests"/>' bowl over a dug room, both tiers, with a voxel world
/// for the ray read off the same seed and edits: rock at and under the surface, air above it and in
/// what was dug. A lake in the bowl is Tier A; water put on the room's floor is Tier B.
/// </remarks>
public class SiphonTests
{
    private const ulong WorldSeed = 0x9A3EUL;
    private const int CellSize = HeightField.DefaultCellVoxels;
    private const int Pitch = 128 / CellSize;
    private const int Cap = FluidConstants.CellCapacityMillilitres;
    private const int Floor = 31, Rim = 39;
    private const int Air = 0, Stone = 1, BigVault = 2, SmallVault = 3, CellMaterial = 4, WindowMaterial = 5, Water = 6;

    /// <summary>1,000 J a litre is a million millijoules a litre, a thousand a millilitre.</summary>
    private const long PerLitre = 1_000_000;

    private static readonly MachineLink PowerSink = new("power", MachineMedium.Power, MachineRole.Sink, MachineLink.ChannelVia, null, "channel");
    private static readonly PortDefinition Channel = new("channel", 0, 0, 15);
    private static readonly PortDefinition Enabled = new(MachineTick.EnabledPort, 1, 0, 1);

    private static MachineDefinition VaultOf(long cubicMetres) => new()
    {
        Kind = MachineKind.Vault,
        InsideMillilitres = cubicMetres * MachineDefinition.MillilitresPerCubicMetre,
        WaterHoldMilliwattsPerCubicMetre = 25_000,
        BlockHoldMilliwattsPerCubicMetre = 2_500,
        HoldsWater = true,
        Links = [PowerSink],
        Ports = [Channel, Enabled],
    };

    private static readonly MachineDefinition CellDefinition = new()
    {
        Kind = MachineKind.Storage,
        CapacityMillijoules = 1_000_000_000_000_000L,
        Links = [new MachineLink("charge", MachineMedium.Power, MachineRole.Sink, MachineLink.ChannelVia, null, "channel")],
        Ports = [Channel, Enabled],
    };

    private static readonly MaterialTable Materials = MaterialTable.ForTesting(
        //          air    stone  big    small  cell   window water
        isOpaque: [false, true, true, true, true, true, false],
        isFluid: [false, false, false, false, false, false, true],
        textureLayer: [0, 1, 2, 3, 4, 5, 0],
        hardness: [0f, 3f, 3f, 3f, 3f, 1f, 0f],
        machine: [null, null, VaultOf(100), VaultOf(1), CellDefinition, WindowTests.WindowDefinition, null],
        unit: [default, default, MaterialUnit.Cells, MaterialUnit.Cells, MaterialUnit.Cells, MaterialUnit.Count, default]);

    private static ToolDefinition SiphonOf(ToolBite bite, long joulesPerLitre = 1_000) => new()
    {
        Id = Identifier.Parse($"test:siphon_{bite.ToString().ToLowerInvariant()}"),
        Bite = bite,
        Takes = ToolTakes.Water,
        Power = new ToolPower(MachineLink.ChannelVia, 0, joulesPerLitre),
    };

    private static readonly ToolDefinition Cube = SiphonOf(ToolBite.Cube);

    /// <summary>The bowl over 48..79, the ramp rising east of it, the rim everywhere else — the machine tests' seed.</summary>
    private static ISurfaceHeights BowlAndRamp() => new AnalyticSurface((x, z) =>
    {
        if (x >= 48 && x < 80 && z >= 48 && z < 80) return Floor;
        if (x >= 80 && x < 100 && z >= 48 && z < 80) return Math.Min(Rim, Floor + ((x - 80) / 2) + 1);
        return Rim;
    });

    private sealed class EditedTerrain(ISurfaceHeights seed) : IFluidTerrain
    {
        public HashSet<VoxelPos> Dug { get; } = [];

        public HashSet<ChunkPos> Loaded { get; } = [];

        public bool CanHold(VoxelPos v) => Loaded.Contains(v.Chunk) && (Dug.Contains(v) || v.Y > seed.SurfaceHeightAt(v.X, v.Z));

        public bool IsLoaded(VoxelPos v) => Loaded.Contains(v.Chunk);
    }

    /// <summary>The ray's world: rock at and under the seed surface, air above it and wherever it was dug.</summary>
    private sealed class SeedWorld(ISurfaceHeights seed, EditedTerrain terrain) : IVoxelWorld
    {
        public bool TryGetMaterial(VoxelPos v, out int materialId)
        {
            materialId = Air;
            if (!terrain.Loaded.Contains(v.Chunk)) return false;
            materialId = terrain.Dug.Contains(v) || v.Y > seed.SurfaceHeightAt(v.X, v.Z) ? Air : Stone;
            return true;
        }
    }

    private sealed class Rig
    {
        public const int RoomMin = 32, RoomMax = 51, RoomFloor = 8, RoomTop = 23;

        public Rig()
        {
            Seed = BowlAndRamp();
            Terrain = new EditedTerrain(Seed);
            WaterSystem = new WaterSystem(Seed, WorldSeed, new BasinLedger(), Pitch);
            WaterSystem.Install(WaterSystem.AnalyseFor(64, 64));
            Ceiling = new LakeCeiling(Seed, WaterSystem);
            Fluid = new FluidSimulation(new LakeBoundedTerrain(Terrain, Ceiling));
            Machines = new MachineSet(Materials);
            Tick = new MachineTick(Machines, Materials, Fluid, new LakeBoundedTerrain(Terrain, Ceiling), Seed, WaterSystem);
            for (int cz = 1; cz <= 2; cz++)
            for (int cx = 0; cx <= 2; cx++)
            for (int cy = 0; cy <= 1; cy++)
            {
                var chunk = new ChunkPos(cx, cy, cz);
                Terrain.Loaded.Add(chunk);
                Reachable.Add(chunk);
                Fluid.ChunkArrived(chunk);
            }

            for (int y = RoomFloor; y <= RoomTop; y++)
            for (int z = RoomMin; z <= RoomMax; z++)
            for (int x = RoomMin; x <= RoomMax; x++)
            {
                Terrain.Dug.Add(new VoxelPos(x, y, z));
            }

            Refresh();
            World = new SeedWorld(Seed, Terrain);
            Siphon = new Siphon(Fluid, WaterSystem, Ceiling, World, Machines, Tick, Fluid.WakeCell);
        }

        public ISurfaceHeights Seed { get; }

        public EditedTerrain Terrain { get; }

        public WaterSystem WaterSystem { get; }

        public LakeCeiling Ceiling { get; }

        public FluidSimulation Fluid { get; }

        public MachineSet Machines { get; }

        public MachineTick Tick { get; }

        public SeedWorld World { get; }

        public Siphon Siphon { get; }

        public HashSet<ChunkPos> Reachable { get; } = [];

        public EquippedPack Backpack { get; } = new(WindowMaterial, WindowTests.WindowDefinition, 0, channel: 0);

        public int Basin => WaterSystem.BasinAtSample(48 / CellSize, 48 / CellSize);

        public void Refresh() => Ceiling.Refresh(Reachable, Fluid.WakeCell);

        public MachineRow Vault(BrushCell cell, int material = BigVault)
        {
            var row = new MachineRow(cell, material, BrushFacing.PositiveX, Materials.Machine(material)!.DefaultPorts());
            Machines.Add(row);
            return row;
        }

        public MachineRow Charged(BrushCell cell, long millijoules)
        {
            var row = new MachineRow(cell, CellMaterial, BrushFacing.PositiveX, CellDefinition.DefaultPorts());
            StorageState.Write(row, millijoules);
            Machines.Add(row);
            return row;
        }

        /// <summary>A lake in the bowl, the ceiling rebuilt over it unless the test wants it stale.</summary>
        public void Lake(long litres, bool refresh = true)
        {
            Assert.Equal(0L, WaterSystem.Inject(Basin, litres));
            if (refresh) Refresh();
        }

        /// <summary>Full cells over the room's floor, x and z within the square given.</summary>
        public long FloorWater(int min, int max)
        {
            long put = 0;
            for (int z = min; z <= max; z++)
            for (int x = min; x <= max; x++)
            {
                Assert.Equal(0L, Fluid.Add(new VoxelPos(x, RoomFloor, z), Cap));
                put += Cap;
            }

            return put;
        }

        /// <summary>Eyes in metres over a voxel column, looking straight down.</summary>
        public static Vector3 Above(int x, int y, int z) =>
            new((x + 0.5f) * VoxelConstants.VoxelSize, (y + 0.5f) * VoxelConstants.VoxelSize, (z + 0.5f) * VoxelConstants.VoxelSize);

        public static readonly Vector3 Down = new(0, -1, 0);

        public SiphonRefusal Click(SiphonVerb verb, Vector3 eye, ToolDefinition? tool = null) =>
            Siphon.Enqueue(verb, eye, Down, 100f, tool ?? Cube, Backpack);

        public VaultContents Contents(MachineRow row)
        {
            Assert.True(VaultState.TryRead(row, out VaultContents contents));
            return contents;
        }

        /// <summary>Every line the siphon touches: its twins, the pool, the store, Tier B's checked identity.</summary>
        public void AssertBooks()
        {
            Assert.True(Siphon.Exact, "a twin disagrees with the siphon's own counters");
            Assert.True(Tick.PowerExact, "the pool identity broke");
            Assert.True(Machines.StoreWaterExact, "the store's water line broke");
            Fluid.Publish();
            Assert.True(Fluid.Published.IdentityExact, "Tier B's identity broke");
        }
    }

    // ---------------------------------------------------------------- Tier A (the lake)

    [Fact]
    public void ALakeTakeThenPourRestoresTheBasinExactlyAndOnlyTheTakeIsPaid()
    {
        var rig = new Rig();
        rig.Lake(100_000);
        MachineRow vault = rig.Vault(new BrushCell(0, 10, 0));
        MachineRow cell = rig.Charged(new BrushCell(1, 10, 0), 10_000L * PerLitre);
        long stored = rig.WaterSystem.StoredLitres;
        Vector3 eye = Rig.Above(64, 60, 64);

        // A click only enqueues: nothing has moved until the boundary.
        Assert.Equal(SiphonRefusal.None, rig.Click(SiphonVerb.Take, eye));
        Assert.Equal(stored, rig.WaterSystem.StoredLitres);
        Assert.Equal(0, rig.Contents(vault).WaterMillilitres);

        SiphonReport took = rig.Siphon.Apply();
        Assert.Equal(1_000, took.TakenLitres);
        Assert.Equal(0, took.TakenMillilitres);
        Assert.Equal(stored - 1_000, rig.WaterSystem.StoredLitres);
        Assert.Equal(1_000_000, rig.Contents(vault).WaterMillilitres);
        Assert.Equal(1_000 * PerLitre, took.PaidMillijoules);
        Assert.Equal((10_000L - 1_000) * PerLitre, StorageState.Read(cell));
        rig.AssertBooks();

        Assert.Equal(SiphonRefusal.None, rig.Click(SiphonVerb.Pour, eye));
        SiphonReport poured = rig.Siphon.Apply();
        Assert.Equal(1_000, poured.PouredLitres);
        Assert.Equal(0, poured.PaidMillijoules);
        Assert.Equal(stored, rig.WaterSystem.StoredLitres);
        Assert.Equal(0, rig.Contents(vault).WaterMillilitres);
        Assert.Equal((10_000L - 1_000) * PerLitre, StorageState.Read(cell));
        Assert.Equal(1_000, rig.WaterSystem.SiphonedLitres);
        Assert.Equal(1_000, rig.WaterSystem.PouredLitres);
        rig.AssertBooks();
    }

    [Fact]
    public void TheRayFindsALakeWhoseBandIsStaleBecauseItCoversTheColumnsItCrosses()
    {
        // IM-4's pass, fact 4: InLake answers false where no band is built. The lake is poured with
        // the ceiling left stale; without its own cover the ray would pass through it to the bed.
        var rig = new Rig();
        rig.Lake(100_000, refresh: false);
        rig.Vault(new BrushCell(0, 10, 0));
        rig.Charged(new BrushCell(1, 10, 0), 10_000L * PerLitre);
        Assert.False(rig.Ceiling.InLake(new VoxelPos(64, 33, 64)));

        rig.Click(SiphonVerb.Take, Rig.Above(64, 60, 64));
        SiphonReport took = rig.Siphon.Apply();

        Assert.Equal(1_000, took.TakenLitres);
        Assert.Equal(0, took.Missed);
        rig.AssertBooks();
    }

    [Fact]
    public void ATakeIsCutToTheStoresRoomAndThenToTheBankAndNeverTakenOnCredit()
    {
        var rig = new Rig();
        rig.Lake(100_000);
        long stored = rig.WaterSystem.StoredLitres;
        MachineRow small = rig.Vault(new BrushCell(0, 10, 0), SmallVault);
        MachineRow cell = rig.Charged(new BrushCell(1, 10, 0), 10_000L * PerLitre);
        Vector3 eye = Rig.Above(64, 60, 64);

        // The tunneller's size is 8,000 L; a 1 m³ store has room for 1,000.
        rig.Click(SiphonVerb.Take, eye, SiphonOf(ToolBite.Tunnel));
        Assert.Equal(1_000, rig.Siphon.Apply().TakenLitres);
        Assert.Equal(1_000_000, rig.Contents(small).WaterMillilitres);

        // Full: a take moves nothing and pays nothing.
        rig.Click(SiphonVerb.Take, eye);
        SiphonReport full = rig.Siphon.Apply();
        Assert.Equal(0, full.TakenLitres);
        Assert.Equal(0, full.PaidMillijoules);

        // Room again, and a bank of 500 L's worth: half the cube, and the bank to zero.
        rig.Vault(new BrushCell(2, 10, 0));
        rig.Machines.SetCharge(cell.Cell, 500 * PerLitre);
        rig.Click(SiphonVerb.Take, eye);
        SiphonReport half = rig.Siphon.Apply();
        Assert.Equal(500, half.TakenLitres);
        Assert.Equal(0, StorageState.Read(cell));
        Assert.Equal(stored - 1_500, rig.WaterSystem.StoredLitres);
        rig.AssertBooks();
    }

    [Fact]
    public void ATakeFillsTheStoreInFillOrderAndAPourEmptiesTheLastFilledFirst()
    {
        // Decision 7. Two vaults alike but for size bill at one rate, so the fill order is the
        // origins': the small one at x 0 first. (IM-4a's mutation battery: no test had two vaults
        // with room, so either order passed.)
        var rig = new Rig();
        rig.Lake(100_000);
        MachineRow first = rig.Vault(new BrushCell(0, 10, 0), SmallVault);
        MachineRow last = rig.Vault(new BrushCell(2, 10, 0));
        rig.Charged(new BrushCell(1, 10, 0), 10_000L * PerLitre);
        Vector3 eye = Rig.Above(64, 60, 64);

        // The tunneller's 8,000 L: the small vault's 1,000 first, the rest into the big one.
        rig.Click(SiphonVerb.Take, eye, SiphonOf(ToolBite.Tunnel));
        Assert.Equal(8_000, rig.Siphon.Apply().TakenLitres);
        Assert.Equal(1_000_000, rig.Contents(first).WaterMillilitres);
        Assert.Equal(7_000_000, rig.Contents(last).WaterMillilitres);

        // A cube's pour, 1,000 L, comes out of the last-filled vault; the first keeps its own.
        rig.Click(SiphonVerb.Pour, eye);
        Assert.Equal(1_000, rig.Siphon.Apply().PouredLitres);
        Assert.Equal(1_000_000, rig.Contents(first).WaterMillilitres);
        Assert.Equal(6_000_000, rig.Contents(last).WaterMillilitres);
        rig.AssertBooks();

        // The tunneller's pour asks 8,000 L of a store holding 7,000: cut to what it holds, and both
        // vaults empty. (Only the random battery reached this cut before.)
        rig.Click(SiphonVerb.Pour, eye, SiphonOf(ToolBite.Tunnel));
        Assert.Equal(7_000, rig.Siphon.Apply().PouredLitres);
        Assert.Equal(0, rig.Machines.StoredWaterOf(0));
        rig.AssertBooks();
    }

    [Fact]
    public void TheStoresWaterLineHoldsAcrossAReopenAndARemoveWithWaterInAVault()
    {
        // The line's entering and leaving terms: a world saved with water in a vault restores
        // through Add, and a vault removed with water in it takes it out of the line. Zero in play
        // for the remove, as the blocks' is, but the terms are the line's. (IM-4a's mutation battery:
        // every other test added its vaults empty.)
        var rig = new Rig();
        rig.Lake(100_000);
        rig.Vault(new BrushCell(0, 10, 0));
        rig.Charged(new BrushCell(1, 10, 0), 10_000L * PerLitre);
        rig.Click(SiphonVerb.Take, Rig.Above(64, 60, 64));
        Assert.Equal(1_000, rig.Siphon.Apply().TakenLitres);

        RestoreReport report = rig.Machines.Restore(rig.Machines.Snapshot(), id => Materials.Machine(id)?.Ports);
        Assert.Equal(0, report.Clamped + report.Dropped + report.Defaulted);
        Assert.Equal(1_000_000, rig.Machines.StoredWaterOf(0));
        Assert.Equal(1_000_000, rig.Machines.VaultWaterEntering);
        Assert.Equal(1_000_000, rig.Machines.VaultWaterLeaving);
        rig.AssertBooks();

        Assert.True(rig.Machines.Remove(new BrushCell(0, 10, 0)));
        Assert.Equal(0, rig.Machines.StoredWaterOf(0));
        Assert.Equal(2_000_000, rig.Machines.VaultWaterLeaving);
        rig.AssertBooks();
    }

    [Fact]
    public void APourIntoABrimmingLakeSpillsAndBothEnginesCountTheSpill()
    {
        // Tier A's identity is never evaluated in the game (IM-4's pass, fact 3), so its siphon terms
        // are checked here, across a pour that spills: held == injected + poured - spilled - siphoned
        // in this rig, which neither rains nor draws. (IM-4a's mutation battery: the random battery's
        // lake never filled, so no pour had spilled.)
        var rig = new Rig();
        rig.Lake(100_000);
        rig.Vault(new BrushCell(0, 10, 0));
        rig.Charged(new BrushCell(1, 10, 0), 10_000L * PerLitre);
        Vector3 eye = Rig.Above(64, 60, 64);
        rig.Click(SiphonVerb.Take, eye);
        Assert.Equal(1_000, rig.Siphon.Apply().TakenLitres);

        // Brim the basin's tree, then pour the take back into it.
        long capacity = rig.WaterSystem.Water!.Tree.Capacity(rig.Basin);
        rig.WaterSystem.Inject(rig.Basin, capacity);
        rig.Refresh();
        long stored = rig.WaterSystem.StoredLitres;
        long spilledBefore = rig.WaterSystem.SpilledLitres;

        rig.Click(SiphonVerb.Pour, eye);
        SiphonReport poured = rig.Siphon.Apply();
        Assert.Equal(1_000, poured.PouredLitres);
        Assert.Equal(1_000, poured.SpilledLitres);
        Assert.Equal(1_000, rig.WaterSystem.SpilledLitres - spilledBefore);
        Assert.Equal(stored, rig.WaterSystem.StoredLitres);
        Assert.Equal(0, rig.Machines.StoredWaterOf(0));

        WaterSystem w = rig.WaterSystem;
        Assert.Equal(w.InjectedLitres + w.PouredLitres - w.SpilledLitres - w.SiphonedLitres, w.StoredLitres);
        Assert.Equal(0, w.ReceivedLitres + w.EmittedLitres + w.DrawnLitres + w.SeepedLitres + w.PumpedLitres);
        rig.AssertBooks();
    }

    // ---------------------------------------------------------------- Tier B (the room)

    [Fact]
    public void ATierBTakeThenPourKeepsTheCheckedIdentityExactAndPutsTheWaterBackWhereItWas()
    {
        var rig = new Rig();
        MachineRow vault = rig.Vault(new BrushCell(0, 10, 0));
        MachineRow cell = rig.Charged(new BrushCell(1, 10, 0), 10_000L * PerLitre);
        long put = rig.FloorWater(40, 43);
        Vector3 eye = Rig.Above(40, 20, 40);

        rig.Click(SiphonVerb.Take, eye);
        SiphonReport took = rig.Siphon.Apply();
        Assert.Equal(put, took.TakenMillilitres);
        Assert.Equal(0, took.TakenLitres);
        Assert.Equal(put, rig.Contents(vault).WaterMillilitres);
        Assert.Equal(put * (PerLitre / 1_000), took.PaidMillijoules);
        Assert.Equal(0, rig.Fluid.HeldAt(new VoxelPos(41, Rig.RoomFloor, 42)));
        rig.AssertBooks();

        // Nothing left to stop at, so the pour lands on the air above the floor, bottom up.
        rig.Click(SiphonVerb.Pour, eye);
        SiphonReport poured = rig.Siphon.Apply();
        Assert.Equal(put, poured.PouredMillilitres);
        Assert.Equal(0, rig.Contents(vault).WaterMillilitres);
        for (int z = 40; z <= 43; z++)
        for (int x = 40; x <= 43; x++)
        {
            Assert.Equal(Cap, rig.Fluid.HeldAt(new VoxelPos(x, Rig.RoomFloor, z)));
        }

        Assert.Equal(put, rig.Fluid.SiphonedMillilitres);
        Assert.Equal(put, rig.Fluid.PouredMillilitres);
        Assert.Equal((10_000L * PerLitre) - (put * (PerLitre / 1_000)), StorageState.Read(cell));
        rig.AssertBooks();
    }

    [Fact]
    public void AChunkTakeSpreadsOverBoundariesWithinTheBudgetAndPaysAsItMoves()
    {
        // Decision 6: 3,200 cells a boundary. The excavator's size is a whole chunk, 32,768 cells walked
        // top down, so the floor's water is reached in the eighth boundary and the op ends in the eleventh.
        var rig = new Rig();
        rig.Vault(new BrushCell(0, 10, 0));
        rig.Charged(new BrushCell(1, 10, 0), 100_000L * PerLitre);
        long put = rig.FloorWater(Rig.RoomMin, Rig.RoomMax);
        ToolDefinition excavator = SiphonOf(ToolBite.Chunk);

        Assert.Equal(SiphonRefusal.None, rig.Click(SiphonVerb.Take, Rig.Above(40, 20, 40), excavator));
        Assert.Equal(SiphonRefusal.Busy, rig.Click(SiphonVerb.Take, Rig.Above(44, 20, 44)));

        int boundaries = 0;
        long taken = 0, paid = 0;
        while (rig.Siphon.Waiting > 0)
        {
            SiphonReport report = rig.Siphon.Apply();
            boundaries++;
            Assert.InRange(report.CellsVisited, 1, Siphon.CellsPerBoundary);
            Assert.Equal(report.TakenMillilitres * (PerLitre / 1_000), report.PaidMillijoules);
            taken += report.TakenMillilitres;
            paid += report.PaidMillijoules;
            rig.AssertBooks();
            Assert.True(boundaries < 20, "the chunk take never finished");
        }

        Assert.Equal(11, boundaries);
        Assert.Equal(put, taken);
        Assert.Equal(put * (PerLitre / 1_000), paid);
        Assert.Equal(32_768, rig.Siphon.CellsVisited);
    }

    [Fact]
    public void ARoomTakeIsCutToTheStoresRoom()
    {
        // Decision 7 for Tier B: the excavator's chunk over the room's flooded floor, 6,250 L, into a
        // store with room for 1,000. The take stops there and finishes, and the rest stays on the
        // floor. (IM-4a's mutation battery: the room check could go and every test stayed green, since
        // no Tier B take had met a full store.)
        var rig = new Rig();
        MachineRow small = rig.Vault(new BrushCell(0, 10, 0), SmallVault);
        rig.Charged(new BrushCell(1, 10, 0), 100_000L * PerLitre);
        long put = rig.FloorWater(Rig.RoomMin, Rig.RoomMax);

        rig.Click(SiphonVerb.Take, Rig.Above(40, 20, 40), SiphonOf(ToolBite.Chunk));
        long taken = 0;
        for (int boundary = 0; boundary < 20 && rig.Siphon.Waiting > 0; boundary++)
        {
            taken += rig.Siphon.Apply().TakenMillilitres;
            rig.AssertBooks();
        }

        Assert.Equal(0, rig.Siphon.Waiting);
        Assert.Equal(1_000_000, taken);
        Assert.Equal(1_000_000, rig.Contents(small).WaterMillilitres);
        long left = 0;
        for (int z = Rig.RoomMin; z <= Rig.RoomMax; z++)
        for (int x = Rig.RoomMin; x <= Rig.RoomMax; x++)
        {
            left += rig.Fluid.HeldAt(new VoxelPos(x, Rig.RoomFloor, z));
        }

        Assert.Equal(put - 1_000_000, left);
    }

    [Fact]
    public void APartialTakeLowersTheLevelFromTheTop()
    {
        // Decision 7's partial take and the walk's order: water two layers deep, a bank that pays
        // for one layer, and the take lowers the level — never a hole under standing water.
        // (IM-4a's mutation battery: every take so far emptied its shape, and either order does that.)
        var rig = new Rig();
        rig.Vault(new BrushCell(0, 10, 0));
        rig.Charged(new BrushCell(1, 10, 0), 250 * PerLitre);
        for (int y = Rig.RoomFloor; y <= Rig.RoomFloor + 1; y++)
        for (int z = 40; z <= 43; z++)
        for (int x = 40; x <= 43; x++)
        {
            Assert.Equal(0L, rig.Fluid.Add(new VoxelPos(x, y, z), Cap));
        }

        rig.Click(SiphonVerb.Take, Rig.Above(40, 20, 40));
        Assert.Equal(16L * Cap, rig.Siphon.Apply().TakenMillilitres);
        for (int z = 40; z <= 43; z++)
        for (int x = 40; x <= 43; x++)
        {
            Assert.Equal(0, rig.Fluid.HeldAt(new VoxelPos(x, Rig.RoomFloor + 1, z)));
            Assert.Equal(Cap, rig.Fluid.HeldAt(new VoxelPos(x, Rig.RoomFloor, z)));
        }

        rig.AssertBooks();
    }

    // ---------------------------------------------------------------- the click

    [Fact]
    public void AClickThenAQuitMovesNothingAndPaysNothing()
    {
        var rig = new Rig();
        rig.Lake(100_000);
        MachineRow vault = rig.Vault(new BrushCell(0, 10, 0));
        MachineRow cell = rig.Charged(new BrushCell(1, 10, 0), 10_000L * PerLitre);
        long stored = rig.WaterSystem.StoredLitres;

        rig.Click(SiphonVerb.Take, Rig.Above(64, 60, 64));
        rig.Click(SiphonVerb.Take, Rig.Above(64, 60, 64));
        rig.Siphon.Clear();

        Assert.Equal(2, rig.Siphon.Dropped);
        Assert.Equal(0, rig.Siphon.Waiting);
        Assert.False(rig.Siphon.Apply().DidAnything);
        Assert.Equal(stored, rig.WaterSystem.StoredLitres);
        Assert.Equal(0, rig.Contents(vault).WaterMillilitres);
        Assert.Equal(10_000L * PerLitre, StorageState.Read(cell));
        rig.AssertBooks();
    }

    [Fact]
    public void ARockToolTheLeadAcidPackAndNoPackAreRefusedAtTheClick()
    {
        var rig = new Rig();
        var pick = new ToolDefinition { Id = Identifier.Parse("test:pick"), HardnessThreshold = 3f };
        var leadAcid = new EquippedPack(CellMaterial, new MachineDefinition
        {
            Kind = MachineKind.Storage,
            Carried = true,
            CapacityMillijoules = 1_000,
            Links = [new MachineLink("charge", MachineMedium.Power, MachineRole.Sink, MachineLink.ChannelVia, null, "channel")],
            Ports = [Channel],
        }, 0, channel: 0);

        Assert.Equal(SiphonRefusal.NotAWaterTool, rig.Siphon.Enqueue(SiphonVerb.Take, Rig.Above(64, 60, 64), Rig.Down, 100f, pick, rig.Backpack));
        Assert.Equal(SiphonRefusal.NoBackpack, rig.Siphon.Enqueue(SiphonVerb.Take, Rig.Above(64, 60, 64), Rig.Down, 100f, Cube, leadAcid));
        Assert.Equal(SiphonRefusal.NoBackpack, rig.Siphon.Enqueue(SiphonVerb.Take, Rig.Above(64, 60, 64), Rig.Down, 100f, Cube, null));
        Assert.Equal(0, rig.Siphon.Waiting);
    }

    [Fact]
    public void ATakeAtNothingMissesAndAPourAtNothingLandsOnTheSurface()
    {
        var rig = new Rig();
        MachineRow vault = rig.Vault(new BrushCell(0, 10, 0));
        rig.Charged(new BrushCell(1, 10, 0), 10_000L * PerLitre);

        // Dry room: the take's ray reaches the floor and finds no water.
        rig.Click(SiphonVerb.Take, Rig.Above(40, 20, 40));
        Assert.Equal(1, rig.Siphon.Apply().Missed);

        // An empty store pours nothing, and says it finished.
        rig.Click(SiphonVerb.Pour, Rig.Above(40, 20, 40));
        SiphonReport empty = rig.Siphon.Apply();
        Assert.Equal(0, empty.PouredMillilitres);
        Assert.Equal(0, rig.Contents(vault).WaterMillilitres);
        rig.AssertBooks();
    }

    // ---------------------------------------------------------------- a random battery

    [Fact]
    public void EveryLineHoldsOverTwoHundredRandomTakesAndPoursOnBothTiers()
    {
        var rig = new Rig();
        rig.Lake(120_000);
        rig.Vault(new BrushCell(0, 10, 0));
        rig.Vault(new BrushCell(2, 10, 0), SmallVault);
        MachineRow cell = rig.Charged(new BrushCell(1, 10, 0), 0);
        rig.FloorWater(Rig.RoomMin, Rig.RoomMax);
        long lake0 = rig.WaterSystem.StoredLitres;
        var random = new Random(0x5170);
        ToolDefinition[] sizes = [Cube, SiphonOf(ToolBite.Tunnel)];

        for (int op = 0; op < 200; op++)
        {
            if (random.Next(4) == 0)
            {
                rig.Machines.SetCharge(cell.Cell, random.Next(0, 20_000) * (PerLitre / 10));
            }

            Vector3 eye = random.Next(2) == 0
                ? Rig.Above(56 + random.Next(16), 60, 56 + random.Next(16))
                : Rig.Above(Rig.RoomMin + random.Next(20), 20, Rig.RoomMin + random.Next(20));
            rig.Click(random.Next(2) == 0 ? SiphonVerb.Take : SiphonVerb.Pour, eye, sizes[random.Next(sizes.Length)]);
            if (random.Next(3) != 0)
            {
                rig.Siphon.Apply();
            }

            rig.AssertBooks();
            Assert.Equal(lake0 - rig.Siphon.TakenLitres + rig.Siphon.PouredLitres - rig.Siphon.SpilledLitres, rig.WaterSystem.StoredLitres);
        }

        Assert.True(rig.Siphon.TakenLitres > 0 && rig.Siphon.PouredLitres > 0, "no lake op ran both ways");
        Assert.True(rig.Siphon.TakenMillilitres > 0 && rig.Siphon.PouredMillilitres > 0, "no room op ran both ways");
    }

    // ---------------------------------------------------------------- the pocket and the ID (decision 8)

    [Fact]
    public void ThePocketRefusesWaterAtItsOneDoorWhateverTheDoor()
    {
        var pocket = new VolumetricInventory();
        pocket.RefuseMaterials(Materials.Fluids());

        pocket.Add(Water, 64);
        pocket.Add(Stone, 64);
        Assert.Equal(0, pocket.Voxels(Water));
        Assert.Equal(64, pocket.Voxels(Stone));
        Assert.Equal(64, pocket.RefusedVoxels);

        // The world file's restore and --stock='s seed both reach Add.
        pocket.Reset([(Water, 10), (Stone, 5)]);
        Assert.Equal(0, pocket.Voxels(Water));
        for (int id = 1; id < Materials.Count; id++) pocket.Add(id, 1);
        Assert.Equal(0, pocket.Voxels(Water));
        Assert.True(pocket.Refuses(Water));
        Assert.False(pocket.Refuses(Stone));
    }

    [Fact]
    public void TheBaseGameShipsWaterAsAFluidTheTableListsAndNothingPlaces()
    {
        var content = new GameContent();
        LoadResult result = content.Load(new DirectoryContentSource(ShippedContentTests.ModsDirectory()));
        Assert.True(result.Success, string.Join("; ", result.Errors));

        MaterialDefinition water = content.Materials.Get(Identifier.Parse("overburden:water"));
        Assert.True(water.IsFluid);
        Assert.False(water.IsPlaceable);
        MaterialTable table = MaterialTable.From(content.Materials);
        Assert.Equal([content.Materials.GetNumericId(Identifier.Parse("overburden:water"))], table.Fluids());
    }

    // ---------------------------------------------------------------- the tool grammar (decision 5)

    private static (GameContent Content, LoadResult Result) LoadTool(string json)
    {
        InMemoryContentSource source = TestContent.BaseGame()
            .Add("siphons", "mod.json", """{ "id": "siphons", "name": "Siphons", "version": "1" }""")
            .Add("siphons", "tools/siphon.json", json);
        var content = new GameContent();
        return (content, content.Load(source));
    }

    [Fact]
    public void ASiphonLoadsTakingWaterPricedALitreAndSortsAfterEveryRockTool()
    {
        (GameContent content, LoadResult result) = LoadTool("""
            { "id": "siphon", "bite": "tunnel", "takes": "water", "cooldownSeconds": 0.25,
              "power": { "via": "channel", "joulesPerLitre": 1000 } }
            """);
        Assert.True(result.Success, string.Join("; ", result.Errors));
        ToolDefinition siphon = content.Tools.Get(Identifier.Parse("siphons:siphon"));
        Assert.Equal(ToolTakes.Water, siphon.Takes);
        Assert.Equal(1_000, siphon.Power!.MillijoulesPerMillilitre);
        Assert.False(siphon.IsFree);

        List<ToolDefinition> order = ToolTiers.Order(content.Tools.Entries().Select(e => e.Value));
        Assert.Same(siphon, order[^1]);
        Assert.NotSame(siphon, ToolTiers.Default(order));
    }

    [Theory]
    [InlineData("""{ "id": "siphon", "takes": "water" }""", "never the sphere")]
    [InlineData("""{ "id": "siphon", "bite": "cube", "takes": "water", "power": { "via": "channel", "joulesPerVoxel": 50 } }""", "priced power.joulesPerLitre")]
    [InlineData("""{ "id": "siphon", "bite": "cube", "power": { "via": "channel", "joulesPerLitre": 1000 } }""", "prices a tool that takes water")]
    [InlineData("""{ "id": "siphon", "bite": "cube", "takes": "lava" }""", "rock or water")]
    [InlineData("""{ "id": "siphon", "bite": "cube", "takes": "water", "power": { "via": "channel", "joulesPerLitre": -1 } }""", "joulesPerLitre must be within")]
    public void ASiphonThatCouldNotBeServedIsRefusedNamingTheRule(string json, string mentioning)
    {
        (_, LoadResult result) = LoadTool(json);
        Assert.Contains(result.Errors, e => e.Contains("siphon.json") && e.Contains(mentioning));
    }
}
