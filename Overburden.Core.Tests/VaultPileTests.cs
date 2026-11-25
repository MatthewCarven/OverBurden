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
/// IM-5b: the dump's blocks (IDEAL-MACHINES-PLAN, "IM-5 — the code-level pass", decisions 11–16, and
/// "IM-5b — the re-read"). A burnt-out vault's or a wreck's voxels are written into the world as a cone,
/// one up for one out, into what is dry, never into a machine's cell, the player, a lake or a landing
/// collapse's rest; what cannot lie loose and what is past the heap's ceiling is decohered; the undo can't
/// touch a pile; and every identity holds throughout.
/// </summary>
/// <remarks>
/// The fixture is VaultDumpTests' own: the bowl over 48..79 with its floor at 31 and the rim at 39
/// everywhere else, and a room dug under the rim at X 32..51, Y 8..23, Z 32..51 — chunk (1, 0, 1). Here
/// the terrain is a world as well: what it answers <c>CanHold</c> from, it answers a material read from,
/// and a write through it disturbs the water as the game's hook does.
/// </remarks>
public class VaultPileTests
{
    private const ulong WorldSeed = 0x5EEDD0;
    private const int Cell = HeightField.DefaultCellVoxels;
    private const int Pitch = 128 / Cell;
    private const long CubicMetre = FluidConstants.MillilitresPerCubicMetre;
    private const int Floor = 31, Rim = 39;
    private const int RoomFloor = 8, RoomCeiling = 23;

    private const int Air = 0, Stone = 1, Vault = 2, CellMaterial = 3, Dirt = 4, Gravel = 5, Pack = 6, WaterMaterial = 7;

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
        //            air    stone  vault  cell   dirt   gravel pack   water
        isOpaque: [false, true, true, true, true, true, true, false],
        isFluid: [false, false, false, false, false, false, false, true],
        textureLayer: [0, 1, 2, 3, 4, 5, 6, 7],
        hardness: [0f, 3f, 3f, 3f, 1f, 1f, 1f, 0f],
        machine: [null, null, VaultDefinition, CellDefinition, null, null, null, null],
        unit: [default, default, MaterialUnit.Cells, MaterialUnit.Cells, default, default, default, default],
        // Water placeable here, though the shipped water is not, so the fluid rule is the only one keeping it loose-less.
        placeable: [true, true, true, true, true, true, false, true]);

    // The room's vaults: X 32, 40 and 48 along the diagonal, on the room's floor.
    private static readonly BrushCell West = new(8, 2, 8), Middle = new(10, 2, 10), East = new(12, 2, 12);

    // On the bowl's bed: standing on plane 32, its top at 36, over a floor of 31.
    private static readonly BrushCell Bed = new(14, 8, 14);

    // In the rock under the rim, west of the room: no air anywhere round its cell.
    private static readonly BrushCell Sealed = new(4, 2, 10);

    // Standing on the rim, its mouth above the seed surface: its water is a lake's.
    private static readonly BrushCell OnTheRim = new(8, 10, 22);

    private static ISurfaceHeights BowlAndRim() => new AnalyticSurface((x, z) =>
        x >= 48 && x < 80 && z >= 48 && z < 80 ? Floor : Rim);

    /// <summary>Dug voxels and open air above the seed, less what was placed, inside the loaded box — and a world over the same facts.</summary>
    private sealed class EditedTerrain(ISurfaceHeights seed) : IFluidTerrain, IVoxelEditor
    {
        public HashSet<VoxelPos> Dug { get; } = [];

        public Dictionary<VoxelPos, int> Placed { get; } = [];

        public HashSet<ChunkPos> Loaded { get; } = [];

        /// <summary>Chunks whose water is home too — the game's arrival lands a chunk's water after its voxels.</summary>
        public HashSet<ChunkPos> WaterLoaded { get; } = [];

        public FluidSimulation? Fluid { get; set; }

        public IEditJournal? Journal { get; set; }

        /// <summary>Writes that landed in a cell the live grid held water in: a pile lifts first, so it must make none.</summary>
        public List<VoxelPos> WetWrites { get; } = [];

        /// <summary>Every write refused while set, as a chunk the world will not take would refuse it.</summary>
        public bool RefuseAll { get; set; }

        public bool CanHold(VoxelPos v) =>
            Loaded.Contains(v.Chunk) && !Placed.ContainsKey(v) && (Dug.Contains(v) || v.Y > seed.SurfaceHeightAt(v.X, v.Z));

        public bool IsLoaded(VoxelPos v) => Loaded.Contains(v.Chunk) && WaterLoaded.Contains(v.Chunk);

        public bool TryGetMaterial(VoxelPos voxel, out int materialId)
        {
            materialId = Air;
            if (!Loaded.Contains(voxel.Chunk))
            {
                return false;
            }

            if (Placed.TryGetValue(voxel, out int placed))
            {
                materialId = placed;
                return true;
            }

            materialId = Dug.Contains(voxel) || voxel.Y > seed.SurfaceHeightAt(voxel.X, voxel.Z) ? Air : Stone;
            return true;
        }

        public bool SetVoxel(VoxelPos voxel, int materialId)
        {
            if (RefuseAll || !TryGetMaterial(voxel, out int was))
            {
                return false;
            }

            if (was == materialId)
            {
                return true;
            }

            if (Fluid is not null && Fluid.HeldAt(voxel) > 0)
            {
                WetWrites.Add(voxel);
            }

            int natural = voxel.Y > seed.SurfaceHeightAt(voxel.X, voxel.Z) ? Air : Stone;
            if (materialId == natural)
            {
                // The ground as the seed made it: an undo putting rock back un-digs it rather than placing.
                Placed.Remove(voxel);
                Dug.Remove(voxel);
            }
            else if (materialId == Air)
            {
                Placed.Remove(voxel);
                Dug.Add(voxel);
            }
            else
            {
                Placed[voxel] = materialId;
            }

            Journal?.Wrote(voxel, was, materialId);
            Fluid?.Disturb(voxel);
            return true;
        }
    }

    private sealed class Rig
    {
        public static readonly ChunkPos RoomChunk = new(1, 0, 1);

        public Rig(int cx0 = 0, int cx1 = 2, int cz0 = 1, int cz1 = 2, int cy0 = 0)
        {
            Seed = BowlAndRim();
            Terrain = new EditedTerrain(Seed);
            Water = new WaterSystem(Seed, WorldSeed, new BasinLedger(), Pitch);
            Water.Install(Water.AnalyseFor(64, 64));
            Ceiling = new LakeCeiling(Seed, Water);
            Bounded = new LakeBoundedTerrain(Terrain, Ceiling);
            Fluid = new FluidSimulation(Bounded);
            Terrain.Fluid = Fluid;
            Machines = new MachineSet(Materials);
            Tick = new MachineTick(Machines, Materials, Fluid, Bounded, Seed, Water) { FuseSeed = 0x5EED };
            Dump = NewDump();

            for (int cz = cz0; cz <= cz1; cz++)
            for (int cx = cx0; cx <= cx1; cx++)
            for (int cy = cy0; cy <= 1; cy++)
            {
                Load(new ChunkPos(cx, cy, cz));
            }

            DigBox(32, RoomFloor, 32, 51, RoomCeiling, 51);
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

        public EditHistory History { get; } = new();

        public HashSet<ChunkPos> Reachable { get; } = [];

        public (VoxelPos Min, VoxelPos Max)? Player { get; set; }

        private VaultDump NewDump() =>
            new(Machines, Fluid, Bounded, Seed, Water, Ceiling, submerged: Fluid.WakeCell, world: Terrain, materials: Materials)
            {
                History = History,
            };

        public void Load(ChunkPos chunk)
        {
            if (Terrain.Loaded.Add(chunk))
            {
                Terrain.WaterLoaded.Add(chunk);
                Reachable.Add(chunk);
                Fluid.ChunkArrived(chunk);
            }
        }

        public void Unload(ChunkPos chunk)
        {
            Terrain.Loaded.Remove(chunk);
            Terrain.WaterLoaded.Remove(chunk);
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

        /// <summary>A vault as a burnt fuse or a fall leaves it — owing — entered as a restored row is.</summary>
        public MachineRow Owing(BrushCell cell, (int Material, long Voxels)[] voxels, long water = 0, bool wreck = false)
        {
            var contents = new VaultContents
            {
                WaterMillilitres = water,
                Phase = VaultPhase.Dumping,
                FuseMillisLeft = VaultDump.ReleaseMillis,
                Wreck = wreck,
            };

            foreach ((int material, long count) in voxels)
            {
                contents.Add(material, count);
            }

            if (!wreck)
            {
                for (int lz = 0; lz < BrushCell.Voxels; lz++)
                for (int lx = 0; lx < BrushCell.Voxels; lx++)
                for (int ly = 0; ly < BrushCell.Voxels; ly++)
                {
                    Terrain.Placed[cell.VoxelAt(lx, ly, lz)] = Vault;
                }
            }

            var row = new MachineRow(cell, Vault, BrushFacing.PositiveX, VaultDefinition.DefaultPorts(), VaultState.Encode(contents));
            Machines.Add(row);
            return row;
        }

        /// <summary>A machine row with no body: its cell is air in the world, and still a machine's.</summary>
        public void Bodiless(BrushCell cell)
        {
            var row = new MachineRow(cell, CellMaterial, BrushFacing.PositiveX, CellDefinition.DefaultPorts());
            StorageState.Write(row, 0);
            Machines.Add(row);
        }

        public bool TryContents(BrushCell cell, out VaultContents contents)
        {
            contents = new VaultContents();
            return Machines.TryGet(cell, out MachineRow row) && VaultState.TryRead(row, out contents!);
        }

        public VaultContents Contents(BrushCell cell)
        {
            Assert.True(TryContents(cell, out VaultContents contents));
            return contents;
        }

        /// <summary>The game's order: the machine tick, the dump, the lakes, then one tick of the cells.</summary>
        public DumpReport Boundary(bool step = true)
        {
            Tick.Run();
            DumpReport report = Dump.Run(Player);
            Refresh();
            if (step)
            {
                Fluid.Step();
            }

            Assert.InRange(report.VoxelsPiled, 0, VaultDump.VoxelsPerBoundary);
            Assert.InRange(report.PileChunks, 0, VaultDump.PileChunksPerBoundary);
            ReportedLifted += report.PileLiftedMillilitres;
            return report;
        }

        /// <summary>The reports' lifted water summed over every boundary run, against the dump's own counter.</summary>
        public long ReportedLifted { get; private set; }

        /// <summary>Boundaries until no vault owes anything, each one's books checked unless a test has moved the world itself.</summary>
        public int RunToEnd(int cap = 400, bool books = true)
        {
            int boundaries = 0;
            while (Machines.Rows.Any(row => Machines.IsVault(row) && VaultState.TryRead(row, out VaultContents c) && c.Owes))
            {
                Boundary();
                boundaries++;
                if (books)
                {
                    AssertBooks();
                }

                Assert.True(boundaries < cap, "the dump never ended");
            }

            return boundaries;
        }

        public void Reopen()
        {
            RestoreReport report = Machines.Restore(Machines.Snapshot(), id => Materials.Machine(id)?.Ports);
            Assert.Equal(0, report.Clamped + report.Dropped + report.Defaulted + report.Unknown);
            Dump = NewDump();
        }

        /// <summary>Loose voxels the world holds that nothing placed but a pile: everything placed that is not a body.</summary>
        public IEnumerable<KeyValuePair<VoxelPos, int>> Piled =>
            Terrain.Placed.Where(entry => entry.Value is not Vault and not CellMaterial);

        /// <summary>
        /// Both tiers' identities and the store's two lines through the dump's own <c>Exact</c>, and the
        /// world: every voxel the store says was dumped is in it, and no write went into water.
        /// </summary>
        public void AssertBooks()
        {
            long b = Fluid.TotalMillilitres;
            Assert.Equal(
                Fluid.AddedMillilitres + Fluid.EmittedMillilitres + Fluid.PouredMillilitres + Fluid.DumpedMillilitres - Fluid.RemovedMillilitres
                    - Fluid.DisplacedMillilitres - Fluid.HandedMillilitres - Fluid.PumpedMillilitres - Fluid.SiphonedMillilitres,
                b);
            Assert.True(Machines.StoreExact, "the store's voxel line broke");
            Assert.True(Machines.StoreWaterExact, "the store's water line broke");
            Assert.True(Dump.Exact, "a twin disagrees with the dump's own counters");
            Assert.Equal(Machines.VoxelsDumped, Piled.LongCount());
            Assert.Empty(Terrain.WetWrites);

            // The lift's two lines (decision 17's question 1): the pile's own, and Tier B's, which the pile shares
            // with a fall and a fill into water and alone uses here.
            Assert.Equal(Dump.PileLiftedMillilitres, Dump.PileReturnedMillilitres + Dump.PileDisplacedMillilitres);
            Assert.Equal(Fluid.LiftedMillilitres, Fluid.ReturnedMillilitres + Fluid.LiftDisplacedMillilitres);
            Assert.Equal(Dump.PileLiftedMillilitres, Fluid.LiftedMillilitres);
            Assert.Equal(Dump.PileDisplacedMillilitres, Fluid.LiftDisplacedMillilitres);
        }

        /// <summary>A quarter-full cell of water on every floor voxel of the room outside the vault's footprint, as his pit's floor stood.</summary>
        public long WetTheFloor(BrushCell vault)
        {
            long added = 0;
            for (int z = 32; z <= 51; z++)
            for (int x = 32; x <= 51; x++)
            {
                var v = new VoxelPos(x, RoomFloor, z);
                if (!Inside(v, vault))
                {
                    long quarter = FluidConstants.CellCapacityMillilitres / 4;
                    added += quarter - Fluid.Add(v, quarter);
                }
            }

            return added;
        }

        /// <summary>Every open voxel of the room filled to the brim, so a ball in it finds no room.</summary>
        public long FillTheRoom()
        {
            long added = 0;
            for (int y = RoomFloor; y <= RoomCeiling; y++)
            for (int z = 32; z <= 51; z++)
            for (int x = 32; x <= 51; x++)
            {
                added += FluidConstants.CellCapacityMillilitres - Fluid.Add(new VoxelPos(x, y, z), FluidConstants.CellCapacityMillilitres);
            }

            return added;
        }
    }

    private static bool Inside(VoxelPos v, BrushCell cell)
    {
        VoxelPos o = cell.OriginVoxel;
        return v.X >= o.X && v.X < o.X + BrushCell.Voxels
            && v.Y >= o.Y && v.Y < o.Y + BrushCell.Voxels
            && v.Z >= o.Z && v.Z < o.Z + BrushCell.Voxels;
    }

    private static int ShellOf(int x, int y, int z, BrushCell vault)
    {
        VoxelPos o = vault.OriginVoxel;
        double dx = x + 0.5 - (o.X + (BrushCell.Voxels / 2.0));
        double dz = z + 0.5 - (o.Z + (BrushCell.Voxels / 2.0));
        return (int)Math.Floor(y - o.Y + Math.Sqrt((dx * dx) + (dz * dz)));
    }

    // ---------------------------------------------------------------- the positions (IM-6a, decision 5)

    [Fact]
    public void ThePilesPositionsAreTheVoxelsItWroteThisBoundaryAndNoMore()
    {
        // What the glitch is drawn from (IM-6's decision 14): each boundary's list is that boundary's writes,
        // each a pile block standing in the world, never two alike, at most the budget, cleared every boundary.
        var rig = new Rig();
        rig.Owing(Middle, [(Dirt, 1_500), (Gravel, 700)]);
        var seen = new HashSet<VoxelPos>();
        int boundaries = 0, full = 0;
        while (rig.Machines.Rows.Any(row => rig.Machines.IsVault(row) && VaultState.TryRead(row, out VaultContents c) && c.Owes))
        {
            DumpReport report = rig.Boundary();
            IReadOnlyList<VoxelPos> written = rig.Dump.PileVoxelsLast;
            Assert.Equal(report.VoxelsPiled, written.Count);
            Assert.InRange(written.Count, 0, VaultDump.VoxelsPerBoundary);
            full += written.Count == VaultDump.VoxelsPerBoundary ? 1 : 0;
            foreach (VoxelPos v in written)
            {
                Assert.True(seen.Add(v), $"{v} written twice");
                Assert.True(rig.Terrain.Placed.TryGetValue(v, out int material) && material is Dirt or Gravel, $"{v} is not a pile block");
                Assert.Contains(v.Chunk, rig.Dump.PileChunksLast);
            }

            rig.AssertBooks();
            Assert.True(++boundaries < 400, "the dump never ended");
        }

        Assert.True(full > 0, "no boundary piled its whole budget");
        Assert.Equal(2_200, seen.Count);
        Assert.True(seen.SetEquals(rig.Piled.Select(entry => entry.Key)));

        rig.Boundary();
        Assert.Empty(rig.Dump.PileVoxelsLast);
    }

    // ---------------------------------------------------------------- the heap (decision 11, the cone)

    [Fact]
    public void ARoomTakesEveryVoxelOnceAsASupportedConeAndTheVaultHoldsAgain()
    {
        var rig = new Rig();
        rig.Owing(Middle, [(Dirt, 3_000)]);

        int boundaries = rig.RunToEnd();

        Assert.InRange(boundaries, 6, 20); // 3,000 at 512 a boundary
        Assert.Equal(3_000, rig.Piled.Count(entry => entry.Value == Dirt));
        Assert.Equal(3_000, rig.Dump.VoxelsPiled);
        Assert.Equal(VaultPhase.Holding, rig.Contents(Middle).Phase);
        Assert.True(rig.Contents(Middle).IsEmpty);
        Assert.Equal(0, rig.Dump.VoxelsDecohered);

        // Every voxel in the room, none in the vault's cell, and each resting on something solid.
        foreach ((VoxelPos v, _) in rig.Piled)
        {
            Assert.InRange(v.X, 32, 51);
            Assert.InRange(v.Y, RoomFloor, RoomCeiling);
            Assert.InRange(v.Z, 32, 51);
            Assert.False(Inside(v, Middle));
            Assert.True(rig.Terrain.TryGetMaterial(v.Offset(0, -1, 0), out int under) && under != Air, $"{v} rests on air");
        }

        // A cone, shell by shell: no free slot is in a shell lower than the highest shell filled.
        int highestFilled = rig.Piled.Max(entry => ShellOf(entry.Key.X, entry.Key.Y, entry.Key.Z, Middle));
        int lowestFree = int.MaxValue;
        for (int z = 32; z <= 51; z++)
        for (int x = 32; x <= 51; x++)
        {
            int top = Inside(new VoxelPos(x, RoomFloor, z), Middle) ? RoomFloor + BrushCell.Voxels - 1 : RoomFloor - 1;
            while (rig.Terrain.Placed.TryGetValue(new VoxelPos(x, top + 1, z), out int m) && m == Dirt)
            {
                top++;
            }

            if (top + 1 <= RoomCeiling)
            {
                lowestFree = Math.Min(lowestFree, ShellOf(x, top + 1, z, Middle));
            }
        }

        Assert.True(highestFilled <= lowestFree, $"shell {highestFilled} filled while a slot in shell {lowestFree} stood free: not a cone");
    }

    [Fact]
    public void ThreePilesShareTheBudgetRoundRobinWithTheRemainderToTheFirstByPosition()
    {
        var rig = new Rig();
        rig.Owing(East, [(Dirt, 2_000)]);
        rig.Owing(West, [(Dirt, 2_000)]);
        rig.Owing(Middle, [(Dirt, 2_000)]);

        DumpReport report = rig.Boundary();

        Assert.Equal(3, report.Piling);
        Assert.Equal(VaultDump.VoxelsPerBoundary, report.VoxelsPiled);
        Assert.Equal(2_000 - 172, rig.Contents(West).TotalVoxels); // 512 = 3 × 170, and the 2 left over to the first
        Assert.Equal(2_000 - 170, rig.Contents(Middle).TotalVoxels);
        Assert.Equal(2_000 - 170, rig.Contents(East).TotalVoxels);
        rig.AssertBooks();
    }

    [Fact]
    public void AHollowByTheVaultFillsFirstAndAShaftDeeperThanEightMetresIsNoSlot()
    {
        // A pit a metre deep west of the vault, and a shaft nine and a half metres deep east of it, both two
        // voxels square in the room's floor. The chunks under the room are loaded, so the shaft has a bottom.
        var rig = new Rig(cy0: -1);
        rig.DigBox(36, RoomFloor - 4, 41, 37, RoomFloor - 1, 42);
        rig.DigBox(46, RoomFloor - 38, 41, 47, RoomFloor - 1, 42);
        rig.Owing(Middle, [(Dirt, 300)]);

        rig.RunToEnd();

        // The pit's sixteen voxels are in shells 1 to 4 — its floor below any slot on the room's — and 300
        // voxels complete shell 5 (about 207 slots, arithmetic), so every one of them is filled.
        for (int y = RoomFloor - 4; y < RoomFloor; y++)
        for (int z = 41; z <= 42; z++)
        for (int x = 36; x <= 37; x++)
        {
            Assert.True(rig.Terrain.Placed.TryGetValue(new VoxelPos(x, y, z), out int m) && m == Dirt, $"the pit at ({x}, {y}, {z}) was left open");
        }

        // The shaft is no slot, and nothing stands over its mouth on air.
        Assert.DoesNotContain(rig.Piled, entry => entry.Key.X is 46 or 47 && entry.Key.Z is 41 or 42);
        rig.AssertBooks();
    }

    [Fact]
    public void AHeapWhoseWorldChangedUnderItLooksAgainAndNeverOverwritesNorFloats()
    {
        var rig = new Rig();
        rig.Owing(Middle, [(Dirt, 1_000)]);
        Assert.Equal(VaultDump.VoxelsPerBoundary, rig.Boundary().VoxelsPiled);

        // Between boundaries the player builds a gravel voxel on the top of every column the pile has
        // reached, and digs the top voxel out of one: the queued slots are filled, and one floats.
        var tops = rig.Piled
            .GroupBy(entry => (entry.Key.X, entry.Key.Z))
            .Select(column => new VoxelPos(column.Key.X, column.Max(entry => entry.Key.Y), column.Key.Z))
            .ToList();
        VoxelPos dug = tops[0];
        Assert.True(rig.Terrain.SetVoxel(dug, Air));
        foreach (VoxelPos top in tops.Skip(1))
        {
            Assert.True(rig.Terrain.SetVoxel(top.Offset(0, 1, 0), Gravel));
        }

        int gravel = tops.Count - 1;
        rig.RunToEnd(books: false);

        Assert.Equal(gravel, rig.Terrain.Placed.Count(entry => entry.Value == Gravel));
        foreach ((VoxelPos v, int material) in rig.Piled)
        {
            Assert.True(rig.Terrain.TryGetMaterial(v.Offset(0, -1, 0), out int under) && under != Air, $"{v} ({material}) rests on air");
        }

        Assert.Equal(1_000, rig.Dump.VoxelsPiled);
        Assert.Equal(1_000 - 1, rig.Piled.Count(entry => entry.Value == Dirt));
        Assert.True(rig.Dump.Exact);
        Assert.Empty(rig.Terrain.WetWrites);
    }

    [Fact]
    public void NoBoundaryWritesMoreThanEightChunksAndTheNinthPileWaitsItsTurn()
    {
        // Nine small vaults on the rim, each in the middle of its own chunk column, so each pile's first voxels
        // land in a chunk of their own: nine chunks asked for, eight written.
        var rig = new Rig(cx0: 0, cx1: 3, cz0: 0, cz1: 3);
        var cells = new List<BrushCell>();
        foreach (int z in new[] { 3, 11, 27 })
        foreach (int x in new[] { 3, 11, 27 })
        {
            var cell = new BrushCell(x, (Rim + 1) / BrushCell.Voxels, z);
            rig.Owing(cell, [(Dirt, 5)]);
            cells.Add(cell);
        }

        DumpReport first = rig.Boundary();

        Assert.Equal(VaultDump.PileChunksPerBoundary, first.PileChunks);
        Assert.Equal(8 * 5, first.VoxelsPiled);
        Assert.Equal(1, cells.Count(cell => rig.TryContents(cell, out VaultContents c) && c.TotalVoxels == 5));
        rig.AssertBooks();

        DumpReport second = rig.Boundary();
        Assert.Equal(5, second.VoxelsPiled);
        Assert.All(cells, cell => Assert.True(rig.Contents(cell).IsEmpty));
        rig.AssertBooks();
    }

    // ---------------------------------------------------------------- what can lie loose, and the ceiling (12, 14)

    [Fact]
    public void WhatCannotLieLooseIsDecoheredWholeByMaterialAndTheRestPiles()
    {
        var rig = new Rig();
        rig.Owing(Middle, [(Dirt, 100), (CellMaterial, 64), (Pack, 2), (WaterMaterial, 5), (Air, 3), (99, 7)]);

        DumpReport first = rig.Boundary();

        Assert.Equal(64 + 2 + 5 + 3 + 7, first.VoxelsDecohered);
        Assert.Equal(81, rig.Dump.NotLooseDecoheredVoxels);
        Assert.Equal(64, rig.Machines.VoxelsDecoheredByMaterial[CellMaterial]);
        Assert.Equal(2, rig.Machines.VoxelsDecoheredByMaterial[Pack]);
        Assert.Equal(5, rig.Machines.VoxelsDecoheredByMaterial[WaterMaterial]);
        Assert.Equal(3, rig.Machines.VoxelsDecoheredByMaterial[Air]);
        Assert.Equal(7, rig.Machines.VoxelsDecoheredByMaterial[99]);
        Assert.Equal(100, first.VoxelsPiled);
        Assert.All(rig.Piled, entry => Assert.Equal(Dirt, entry.Value));
        Assert.True(rig.Contents(Middle).IsEmpty);
        rig.AssertBooks();
    }

    [Fact]
    public void ADumpPastTheHeapsCeilingDecoheresTheExcessFromTheHighestMaterialDownOnce()
    {
        var rig = new Rig();
        rig.Owing(Middle, [(Dirt, VaultDump.HeapCeilingVoxels - 1_000), (Gravel, 2_500)]);

        DumpReport first = rig.Boundary();

        Assert.Equal(1_500, first.VoxelsDecohered);
        Assert.Equal(1_500, rig.Dump.HeapCeilingDecoheredVoxels);
        Assert.Equal(1_500, rig.Machines.VoxelsDecoheredByMaterial[Gravel]);
        Assert.False(rig.Machines.VoxelsDecoheredByMaterial.ContainsKey(Dirt));
        Assert.Equal(VaultDump.HeapCeilingVoxels - first.VoxelsPiled, rig.Contents(Middle).TotalVoxels);
        rig.AssertBooks();

        // Checked once: the next boundary decoheres nothing, and nor does a reopen — what was admitted fitted.
        Assert.Equal(0, rig.Boundary().VoxelsDecohered);
        rig.Reopen();
        Assert.Equal(0, rig.Boundary().VoxelsDecohered);
        rig.AssertBooks();
    }

    // ---------------------------------------------------------------- the refusals (decision 11)

    // ---------------------------------------------------------------- a wet slot (decision 17's question 1, (c))

    [Fact]
    public void APileWritesIntoItsOwnWaterLiftingItUpItsColumn()
    {
        // The vault's own water pours first (decision 37, and since question 2 the whole of it before the heap starts), so
        // its floor is wet while the pile runs. The blocks waited for dry ground until 2026-11-25; now a wet slot takes its
        // block and its water climbs the column.
        var rig = new Rig();
        rig.Owing(Middle, [(Dirt, 100)], water: 2 * CubicMetre);

        rig.RunToEnd();

        Assert.Equal(100, rig.Dump.VoxelsPiled);
        Assert.True(rig.Dump.PileLiftedMillilitres > 0, "the water never stood in the pile's way — the test would not see a wet write");
        Assert.Equal(rig.Dump.PileLiftedMillilitres, rig.Dump.PileReturnedMillilitres);
        Assert.Equal(0, rig.Dump.PileDisplacedMillilitres);
        Assert.Equal(rig.Dump.PileLiftedMillilitres, rig.ReportedLifted);
        Assert.Equal(2 * CubicMetre, rig.Fluid.DumpedMillilitres);
        Assert.Equal(0, rig.Fluid.DisplacedMillilitres);
        rig.AssertBooks();
    }

    [Fact]
    public void AWetFloorNoLongerHoldsTheBlocksBack()
    {
        // Decision 17's windowed read: water standing on the floor before the dump, and a vault of blocks only. Every
        // slot the heap reaches is wet, and in his pit 32,000 voxels waited for good.
        var rig = new Rig();
        rig.Owing(Middle, [(Dirt, 400)]);
        long sheet = rig.WetTheFloor(Middle);
        Assert.True(sheet > 0);

        rig.RunToEnd();

        Assert.Equal(400, rig.Dump.VoxelsPiled);
        Assert.True(rig.Dump.PileLiftedMillilitres > 0);
        Assert.Equal(0, rig.Dump.PileDisplacedMillilitres);
        Assert.Equal(sheet, rig.Fluid.TotalMillilitres);
        rig.AssertBooks();
    }

    [Fact]
    public void LiftedWaterWithNoRoomUpItsColumnIsDisplacedAndCounted()
    {
        // Rock a voxel over the vault's top (un-dug, so it is the seed's rock and the books' count of piled voxels
        // stays the pile's), and water standing full on the vault. The heap's columns on the vault rest at its top
        // with the rock the next voxel up, so a slot's water can climb nowhere: it is displaced, never passed through
        // the rock into the air over it. No step, so the water stands where it was put.
        var rig = new Rig();
        VoxelPos o = Middle.OriginVoxel;
        int top = o.Y + BrushCell.Voxels;
        long water = 0;
        for (int z = o.Z; z < o.Z + BrushCell.Voxels; z++)
        for (int x = o.X; x < o.X + BrushCell.Voxels; x++)
        {
            Assert.True(rig.Terrain.Dug.Remove(new VoxelPos(x, top + 1, z)));
        }

        rig.Owing(Middle, [(Dirt, 400)]);
        for (int z = o.Z; z < o.Z + BrushCell.Voxels; z++)
        for (int x = o.X; x < o.X + BrushCell.Voxels; x++)
        {
            water += FluidConstants.CellCapacityMillilitres - rig.Fluid.Add(new VoxelPos(x, top, z), FluidConstants.CellCapacityMillilitres);
        }

        for (int boundaries = 0; rig.Contents(Middle).Owes; boundaries++)
        {
            Assert.True(boundaries < 50, "the dump never ended");
            rig.Boundary(step: false);
            rig.AssertBooks();
        }

        Assert.Equal(400, rig.Dump.VoxelsPiled);
        Assert.True(rig.Dump.PileDisplacedMillilitres > 0, "no slot on the vault was written — the test would not see a climb stopped");
        Assert.Equal(rig.Dump.PileDisplacedMillilitres, rig.Fluid.LiftDisplacedMillilitres);
        Assert.Equal(water - rig.Dump.PileDisplacedMillilitres, rig.Fluid.TotalMillilitres);
        for (int z = o.Z; z < o.Z + BrushCell.Voxels; z++)
        for (int x = o.X; x < o.X + BrushCell.Voxels; x++)
        {
            Assert.Equal(0, rig.Fluid.HeldAt(new VoxelPos(x, top + 2, z)));
        }

        rig.AssertBooks();
    }

    [Fact]
    public void TheClimbStopsAtAChunkWhoseWaterIsNotHome()
    {
        // A shaft over the vault's west side, from the room's ceiling into the chunk above, whose voxels are loaded and
        // whose water is not yet home. The shaft's columns are full of water to the chunk's edge, so a lifted slot's
        // water climbs to the edge and must stop there: a return into a chunk whose water is on the lane is refused.
        var rig = new Rig();
        var above = new ChunkPos(1, 1, 1);
        rig.DigBox(38, RoomCeiling + 1, 39, 39, 40, 44);
        rig.Terrain.WaterLoaded.Remove(above);
        rig.Owing(Middle, [(Dirt, 20)]);
        for (int z = 39; z <= 44; z++)
        for (int x = 38; x <= 39; x++)
        for (int y = RoomFloor; y < VoxelConstants.ChunkSize; y++)
        {
            Assert.Equal(0, rig.Fluid.Add(new VoxelPos(x, y, z), FluidConstants.CellCapacityMillilitres));
        }

        rig.Boundary(step: false);

        Assert.True(rig.Dump.PileLiftedMillilitres > 0, "no slot in the shaft was written");
        Assert.True(rig.Dump.PileDisplacedMillilitres > 0);
        for (int z = 39; z <= 44; z++)
        for (int x = 38; x <= 39; x++)
        for (int y = VoxelConstants.ChunkSize; y <= 40; y++)
        {
            Assert.Equal(0, rig.Fluid.HeldAt(new VoxelPos(x, y, z)));
        }

        rig.AssertBooks();
    }

    [Fact]
    public void ARefusedWritePutsItsWaterBack()
    {
        // A write the world refuses lands nothing, so it moves nothing: the slot's water goes straight back into it.
        var rig = new Rig();
        rig.Owing(Middle, [(Dirt, 20)]);
        rig.WetTheFloor(Middle);
        var before = new Dictionary<VoxelPos, long>();
        for (int z = 32; z <= 51; z++)
        for (int x = 32; x <= 51; x++)
        {
            var v = new VoxelPos(x, RoomFloor, z);
            before[v] = rig.Fluid.HeldAt(v);
        }

        rig.Terrain.RefuseAll = true;
        DumpReport refused = rig.Boundary(step: false);

        Assert.Equal(0, refused.VoxelsPiled);
        Assert.True(rig.Dump.PileLiftedMillilitres > 0, "no wet slot was tried — the test would not see a refusal");
        Assert.Equal(rig.Dump.PileLiftedMillilitres, rig.Dump.PileReturnedMillilitres);
        Assert.Equal(0, rig.Dump.PileDisplacedMillilitres);
        foreach ((VoxelPos v, long held) in before)
        {
            Assert.Equal(held, rig.Fluid.HeldAt(v));
        }

        rig.AssertBooks();

        rig.Terrain.RefuseAll = false;
        rig.RunToEnd();
        Assert.Equal(20, rig.Dump.VoxelsPiled);
        rig.AssertBooks();
    }

    // ---------------------------------------------------------------- the order (decision 17's question 2, (A))

    [Fact]
    public void TheHeapWaitsForItsVaultsWaterSoItNeverBuriesTheBall()
    {
        // In his pit the heap went out faster than the water and buried the ball it pours into, leaving 248.7 m³ owed for
        // good. 50 m³ wants a ball of radius 10, more cells than one boundary visits, so the water outlasts a boundary and
        // the old order would have piled while it poured. Every boundary the vault still owes the cells water at the
        // pile's turn, with its ball not resting, piles nothing.
        var rig = new Rig();
        rig.Owing(Middle, [(Dirt, 400)], water: 50 * CubicMetre);
        int boundaries = 0, waits = 0, lastWait = 0, firstPiled = 0;
        while (rig.Contents(Middle).Owes)
        {
            DumpReport report = rig.Boundary();
            boundaries++;
            rig.AssertBooks();
            if (rig.Contents(Middle).WaterMillilitres > 0 && report.NoRoom == 0)
            {
                Assert.Equal(0, report.VoxelsPiled);
                Assert.Equal(0, report.Piling);
                Assert.Equal(1, report.PileWaitingWater);
                waits++;
                lastWait = boundaries;
            }

            if (report.VoxelsPiled > 0 && firstPiled == 0)
            {
                firstPiled = boundaries;
            }

            Assert.True(boundaries < 400, "the dump never ended");
        }

        Assert.True(waits > 0, "the water never outlasted a boundary — the test would not see the order");
        Assert.Equal(waits, rig.Dump.PileWaitedWater);
        Assert.Equal(0, rig.Dump.NoRoomRests);
        Assert.True(firstPiled > lastWait, $"piled at boundary {firstPiled}, before the water was out at {lastWait}");
        Assert.Equal(400, rig.Dump.VoxelsPiled);
        Assert.Equal(50 * CubicMetre, rig.Fluid.DumpedMillilitres);

        // Every litre reached the cells. Some of what the blocks then lifted is displaced, counted in the books above: the
        // ball pours as a mound that stands to this sealed room's ceiling until the cells level it, so a column's second
        // block finds no room above it (the lift's climb stops at rock, build choice 2).
    }

    [Fact]
    public void ABallRestingForWantOfRoomLetsTheHeapGo()
    {
        // A room full to the brim: the ball's whole pass finds no room and rests, and waiting would not help, so the heap
        // goes. Its slots are full, and the room above them too, so what each lifts is displaced, and counted. The water
        // still waits for room. No step, so the room stays as it was put.
        var rig = new Rig();
        rig.Owing(Middle, [(Dirt, 400)], water: CubicMetre);
        Assert.True(rig.FillTheRoom() > 0);

        DumpReport first = rig.Boundary(step: false);
        Assert.Equal(1, first.NoRoom);
        Assert.Equal(0, first.PileWaitingWater);
        Assert.True(first.VoxelsPiled > 0, "the heap waited on a ball that was resting");
        rig.AssertBooks();
        for (int boundaries = 1; rig.Contents(Middle).TotalVoxels > 0; boundaries++)
        {
            Assert.True(boundaries < VaultDump.NoRoomWaitMillis / MachineTick.DefaultBoundaryMillis, "the heap outlasted the ball's rest");
            Assert.Equal(1, rig.Boundary(step: false).NoRoom);
            rig.AssertBooks();
        }

        Assert.Equal(400, rig.Dump.VoxelsPiled);
        Assert.Equal(CubicMetre, rig.Contents(Middle).WaterMillilitres);
        Assert.Equal(1, rig.Dump.NoRoomRests);
        Assert.Equal(0, rig.Dump.PileWaitedWater);
        Assert.True(rig.Dump.PileDisplacedMillilitres > 0, "a full room's lifted water found room — the room was not full");
        rig.AssertBooks();
    }

    [Fact]
    public void ALakesShareNeverHoldsTheHeapBack()
    {
        // A vault on the rim pays a lake, which needs no room by the vault, so its heap goes at once while the release
        // clock still runs.
        var rig = new Rig();
        rig.Owing(OnTheRim, [(Dirt, 100)], water: CubicMetre);

        DumpReport first = rig.Boundary();

        Assert.Equal(0, first.ToCells);
        Assert.True(rig.Contents(OnTheRim).WaterMillilitres > 0, "the vault's water was gone in a boundary — the test would not see the heap go first");
        Assert.Equal(100, first.VoxelsPiled);
        Assert.Equal(0, first.PileWaitingWater);
        Assert.Equal(0, rig.Dump.PileWaitedWater);
        rig.AssertBooks();
    }

    [Fact]
    public void APileNeverWritesIntoALakeAndCoversTheBandsItAsksAboutItself()
    {
        // A vault on the bowl's bed under a lake. A band exists only for a reachable column, so the bowl's are
        // taken out of the reach: only the pile's own cover stands between it and the lake's volume.
        var rig = new Rig();
        rig.Owing(Bed, [(Dirt, 50)]);
        Assert.Equal(0L, rig.Water.Inject(rig.Water.BasinAtSample(56 / Cell, 56 / Cell), 100_000));
        rig.Reachable.RemoveWhere(chunk => chunk.X >= 1);
        rig.Refresh();

        DumpReport report = rig.Boundary(step: false);

        Assert.Equal(0, report.VoxelsPiled);
        Assert.True(report.PileWaiting > 0);
        Assert.True(rig.Dump.PileWaitedLake > 0);
        Assert.Equal(50, rig.Contents(Bed).TotalVoxels);
        rig.AssertBooks();

        // The other arm: no lake, and the same vault piles.
        var dry = new Rig();
        dry.Owing(Bed, [(Dirt, 50)]);
        Assert.Equal(50, dry.Boundary(step: false).VoxelsPiled);
        dry.AssertBooks();
    }

    [Fact]
    public void APileNeverWritesIntoAMachinesCellThePlayerOrALandingCollapsesRest()
    {
        var rig = new Rig();
        var neighbour = new BrushCell(11, 2, 10);
        rig.Bodiless(neighbour); // east of the vault: its cell is air in the world, and still a machine's
        rig.Player = (new VoxelPos(36, RoomFloor, 36), new VoxelPos(37, RoomFloor + 6, 37));
        var reserved = new HashSet<VoxelPos>();
        for (int z = 40; z <= 43; z++)
        {
            reserved.Add(new VoxelPos(38, RoomFloor, z)); // the floor along the vault's west face
        }

        rig.Dump.Reserved = reserved;
        rig.Owing(Middle, [(Dirt, 800)]);

        rig.RunToEnd();

        Assert.Equal(800, rig.Dump.VoxelsPiled);
        foreach ((VoxelPos v, _) in rig.Piled)
        {
            Assert.False(Inside(v, neighbour), $"{v} is in a machine's cell");
            Assert.False(v.X is >= 36 and <= 37 && v.Y <= RoomFloor + 6 && v.Z is >= 36 and <= 37, $"{v} is inside the player");
            Assert.DoesNotContain(v, reserved);
        }

        Assert.True(rig.Dump.PileWaitedPlayer > 0);
        Assert.True(rig.Dump.PileWaitedReserved > 0);

        // A machine's cell counts as solid to rest on: the pile stands on the bodiless cell's top.
        Assert.Contains(rig.Piled, entry => Inside(entry.Key.Offset(0, -1, 0), neighbour));
        rig.AssertBooks();
    }

    [Fact]
    public void AVaultWhoseChunkIsUnloadedWaitsRatherThanRestingAndPilesOnceItIsBack()
    {
        var rig = new Rig();
        rig.Owing(Middle, [(Dirt, 50)]);
        rig.Unload(Rig.RoomChunk);

        DumpReport waiting = rig.Boundary(step: false);
        Assert.Equal(0, waiting.VoxelsPiled);
        Assert.Equal(0, waiting.PileNoRoom);
        Assert.Equal(0, rig.Dump.PileNoRoomRests);
        Assert.Equal(50, rig.Contents(Middle).TotalVoxels);

        rig.Load(Rig.RoomChunk);
        Assert.Equal(50, rig.Boundary().VoxelsPiled);
        rig.AssertBooks();
    }

    [Fact]
    public void ASlotWhoseChunkHasItsVoxelsButNotYetItsWaterIsPassedOver()
    {
        // A corridor west out of the room into chunk (0, 0, 1), whose voxels are in and whose water is not.
        var rig = new Rig();
        rig.DigBox(24, RoomFloor, 40, 31, RoomFloor + 3, 43);
        rig.Terrain.WaterLoaded.Remove(new ChunkPos(0, 0, 1));
        rig.Owing(Middle, [(Dirt, 1_500)]);

        rig.RunToEnd();

        Assert.DoesNotContain(rig.Piled, entry => entry.Key.X < 32);
        Assert.True(rig.Dump.PileWaitedUnloaded > 0, "the corridor was never asked about — the test would not see the write");
        rig.AssertBooks();
    }

    [Fact]
    public void AColumnThatWaitedIsAskedAgainOnceItsRefusalIsGone()
    {
        // Every floor voxel round the vault is reserved by a landing collapse at first: the pile goes on the
        // vault's top. Then the collapse lands, and the floor that waited takes the pile.
        var rig = new Rig();
        var reserved = new HashSet<VoxelPos>();
        for (int z = 32; z <= 51; z++)
        for (int x = 32; x <= 51; x++)
        {
            reserved.Add(new VoxelPos(x, RoomFloor, z));
        }

        rig.Dump.Reserved = reserved;
        rig.Owing(Middle, [(Dirt, 600)]);

        rig.Boundary();
        Assert.DoesNotContain(rig.Piled, entry => entry.Key.Y == RoomFloor);

        // The very next boundary, not after a rest: a heap that lost its waiting columns would find its queue
        // empty, rest as though the room were full, and only reach the floor ten seconds later from a new seed.
        reserved.Clear();
        DumpReport second = rig.Boundary();
        Assert.True(second.VoxelsPiled > 0, "the floor that waited was not asked again");
        Assert.Contains(rig.Piled, entry => entry.Key.Y == RoomFloor);

        rig.RunToEnd();
        Assert.Equal(600, rig.Dump.VoxelsPiled);
        Assert.Equal(0, rig.Dump.PileNoRoomRests);
        rig.AssertBooks();
    }

    [Fact]
    public void AHeapWithNoSlotRestsAndThenStartsAgainFromItsVault()
    {
        var rig = new Rig();
        rig.Owing(Sealed, [(Dirt, 10)]);

        DumpReport first = rig.Boundary();
        Assert.Equal(0, first.VoxelsPiled);
        Assert.Equal(1, first.PileNoRoom);
        Assert.Equal(1, rig.Dump.PileNoRoomRests);

        for (int i = 0; i < VaultDump.NoRoomWaitMillis / 500; i++)
        {
            DumpReport resting = rig.Boundary();
            Assert.Equal(0, resting.Piling);
            Assert.Equal(1, resting.PileNoRoom);
        }

        // The rest over, the player has dug the cell beside it: the heap starts again and finds the room.
        rig.DigBox(20, 8, 40, 23, 11, 43);
        DumpReport again = rig.Boundary();
        Assert.Equal(10, again.VoxelsPiled);
        Assert.Equal(1, rig.Dump.PileNoRoomRests);
        rig.AssertBooks();
    }

    // ---------------------------------------------------------------- the undo (decision 13)

    [Fact]
    public void CtrlZForgetsAStepInAChunkThePileWroteSoItCannotWriteOverThePile()
    {
        var rig = new Rig();
        rig.Terrain.Journal = rig.History;

        // The player digs a voxel of the room's east wall, in the room's own chunk, and the step closes.
        var dug = new VoxelPos(52, RoomFloor, 40);
        Assert.True(rig.Terrain.SetVoxel(dug, Air));
        Assert.True(rig.History.EndStep());
        Assert.Equal(1, rig.History.Steps);

        rig.Owing(Middle, [(Dirt, 200)]);
        DumpReport report = rig.Boundary();

        Assert.Equal(200, report.VoxelsPiled);
        Assert.Equal(1, report.StepsForgotten);
        Assert.Equal(1, rig.Dump.PileStepsForgotten);
        Assert.Equal(0, rig.History.Steps);

        UndoReport undo = rig.History.Undo(rig.Terrain, Materials, null, rig.Machines);
        Assert.False(undo.Ok);
        Assert.Equal(200, rig.Piled.Count());
        rig.AssertBooks();
    }

    [Fact]
    public void ThePileWritesWithRecordingOffSoAPlayersOpenStepElsewhereSurvivesIt()
    {
        var rig = new Rig();
        rig.Terrain.Journal = rig.History;

        // The player's step is still open, in another chunk: the corridor's rock west of the room, chunk (0, 0, 1).
        var dug = new VoxelPos(28, RoomFloor, 40);
        Assert.True(rig.Terrain.SetVoxel(dug, Air));
        Assert.Equal(1, rig.History.Steps);

        rig.Owing(Middle, [(Dirt, 200)]);
        DumpReport report = rig.Boundary();

        Assert.Equal(200, report.VoxelsPiled);
        Assert.Equal(0, report.StepsForgotten);
        Assert.Equal(1, rig.History.Steps);
        Assert.True(rig.History.Recording, "the dump left recording off");

        // And the undo takes back the player's own voxel, nothing of the pile's.
        Assert.True(rig.History.Undo(rig.Terrain, Materials, null, rig.Machines).Ok);
        Assert.True(rig.Terrain.TryGetMaterial(dug, out int restored) && restored == Stone);
        Assert.Equal(200, rig.Piled.Count());
        rig.AssertBooks();
    }

    // ---------------------------------------------------------------- the end (decision 6, for blocks)

    [Fact]
    public void AWreckOwingOnlyBlocksIsRemovedOnceItsLastVoxelLandsWithNothingForCtrlZ()
    {
        var rig = new Rig();
        rig.Owing(Middle, [(Dirt, 40)], wreck: true);
        rig.Machines.Journal = rig.History;
        Assert.Equal(0, rig.History.Steps);

        DumpReport report = rig.Boundary();

        Assert.Equal(40, report.VoxelsPiled);
        Assert.Equal(1, report.WrecksRemoved);
        Assert.False(rig.Machines.TryGet(Middle, out _));
        Assert.Equal(0, rig.History.Steps);

        // Its cell had no body, and is solid to the heap while it stands: the pile is on and round it.
        Assert.All(rig.Piled, entry => Assert.False(Inside(entry.Key, Middle)));
        rig.AssertBooks();
    }
}
