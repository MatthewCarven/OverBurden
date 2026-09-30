using Overburden.Core.Fluid;
using Overburden.Core.Hydrology;
using Overburden.Core.Lighting;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// The lake over a quarry (M8-PLAN, 2026-09-27): which samples are mostly a dug pit not yet full to
/// the seed. The oracle is a count built exactly at the half boundary, over the portal index the
/// drain already keeps.
/// </summary>
public class BiteFillTests
{
    private const int Cell = HeightField.DefaultCellVoxels;
    private const int SeedY = 30;

    /// <summary>Sample (6, 6) covers voxels 48..55 on both axes.</summary>
    private const int SampleX = 48 / Cell;
    private const int SampleZ = 48 / Cell;

    private sealed class DugTerrain(ISurfaceHeights seed) : IFluidTerrain
    {
        public HashSet<VoxelPos> Dug { get; } = [];

        public bool CanHold(VoxelPos v) => Dug.Contains(v) || v.Y > seed.SurfaceHeightAt(v.X, v.Z);
    }

    private sealed class Rig
    {
        public Rig(ISurfaceHeights? seed = null)
        {
            Seed = seed ?? new AnalyticSurface((_, _) => SeedY);
            Terrain = new DugTerrain(Seed);
            Fluid = new FluidSimulation(Terrain);
            Index = new PortalIndex(Seed, Terrain);
            Tracker = new PortalTracker(Index);
            Fill = new BiteFill(Index, Fluid.Grid) { Slices = 1 };
            for (int cz = 1; cz <= 2; cz++)
            {
                for (int cx = 1; cx <= 2; cx++)
                {
                    for (int cy = 0; cy <= 1; cy++)
                    {
                        Tracker.OnVoxelsReady(new ChunkPos(cx, cy, cz), hasEdits: false);
                    }
                }
            }
        }

        public ISurfaceHeights Seed { get; }

        public DugTerrain Terrain { get; }

        public FluidSimulation Fluid { get; }

        public PortalIndex Index { get; }

        public PortalTracker Tracker { get; }

        public BiteFill Fill { get; }

        /// <summary>Digs a column from its seed voxel down three voxels.</summary>
        public void DigColumn(int x, int z)
        {
            int seed = Seed.SurfaceHeightAt(x, z);
            for (int y = seed - 3; y <= seed; y++)
            {
                var v = new VoxelPos(x, y, z);
                Terrain.Dug.Add(v);
                Tracker.OnVoxelChanged(v);
            }
        }

        /// <summary>The sample's 64 columns, in row order.</summary>
        public static IEnumerable<(int X, int Z)> Footprint()
        {
            for (int z = 48; z < 48 + Cell; z++)
            {
                for (int x = 48; x < 48 + Cell; x++)
                {
                    yield return (x, z);
                }
            }
        }

        public void FillSeedCell(int x, int z, long millilitres) =>
            Assert.Equal(0, Fluid.Add(new VoxelPos(x, Seed.SurfaceHeightAt(x, z), z), millilitres));
    }

    [Fact]
    public void ADryPitIsUnfilledAndAFullOneIsNot()
    {
        var rig = new Rig();
        foreach ((int x, int z) in Rig.Footprint())
        {
            rig.DigColumn(x, z);
        }

        rig.Fill.Refresh();
        Assert.True(rig.Fill.IsUnfilled(SampleX, SampleZ));
        Assert.Equal(1, rig.Fill.UnfilledSamples);
        Assert.Equal((Cell * Cell / 2) + 1, rig.Fill.CellsRead);
        Assert.False(rig.Fill.IsUnfilled(SampleX + 1, SampleZ));

        foreach ((int x, int z) in Rig.Footprint())
        {
            rig.FillSeedCell(x, z, FluidConstants.CellCapacityMillilitres);
        }

        rig.Fill.Refresh();
        Assert.False(rig.Fill.IsUnfilled(SampleX, SampleZ));
        Assert.Equal(Cell * Cell / 2, rig.Fill.CellsRead);
        Assert.Equal(2, rig.Fill.Refreshes);
    }

    [Fact]
    public void MoreThanHalfTheFootprintDryIsTheBoundary()
    {
        var rig = new Rig();
        (int X, int Z)[] footprint = [.. Rig.Footprint()];
        foreach ((int x, int z) in footprint)
        {
            rig.DigColumn(x, z);
        }

        // 32 filled, 32 dry: exactly half is not more than half.
        for (int i = 0; i < 32; i++)
        {
            rig.FillSeedCell(footprint[i].X, footprint[i].Z, FluidConstants.CellCapacityMillilitres);
        }

        rig.Fill.Refresh();
        Assert.Equal(32, rig.Fill.CellsRead); // the index keeps dig order: 32 full first decides it
        Assert.False(rig.Fill.IsUnfilled(SampleX, SampleZ));

        // One more dry — take one of the filled cells back out — and it is 33 of 64.
        var back = new VoxelPos(footprint[0].X, SeedY, footprint[0].Z);
        rig.Fluid.Remove(back, FluidConstants.CellCapacityMillilitres);
        rig.Fill.Refresh();
        Assert.Equal(64, rig.Fill.CellsRead);
        Assert.True(rig.Fill.IsUnfilled(SampleX, SampleZ));
    }

    [Fact]
    public void ACellDrawnFullToTheLitreIsFullAndALitreOfRoomIsNot()
    {
        var rig = new Rig();
        foreach ((int x, int z) in Rig.Footprint())
        {
            rig.DigColumn(x, z);
            // The drain's full: 15 whole litres into 15,625 mL, 625 mL of room left.
            rig.FillSeedCell(x, z, FluidConstants.CellCapacityMillilitres - 625);
        }

        rig.Fill.Refresh();
        Assert.False(rig.Fill.IsUnfilled(SampleX, SampleZ));

        // A whole litre of room in every cell is not full.
        foreach ((int x, int z) in Rig.Footprint())
        {
            rig.Fluid.Remove(new VoxelPos(x, SeedY, z), FluidConstants.MillilitresPerLitre - 625);
        }

        rig.Fill.Refresh();
        Assert.True(rig.Fill.IsUnfilled(SampleX, SampleZ));
    }

    [Fact]
    public void OnlyPitsCountNotTheBankFacesOfADugStep()
    {
        // A step inside the sample: seed 30 west of x = 52, 26 from it. A column dug on the high
        // side at x = 51 opens upward (one pit) and sideways onto the low side's air at every
        // dug voxel above 26 (four banks). Eight such columns are 8 pits and 32 banks: 40 portals,
        // over half the footprint, and 8 dug columns, well under it.
        var rig = new Rig(new AnalyticSurface((x, _) => x >= 52 ? 26 : SeedY));
        for (int z = 48; z < 48 + Cell; z++)
        {
            rig.DigColumn(51, z);
        }

        IReadOnlyList<Portal> under = rig.Index.Under(SampleX, SampleZ);
        Assert.Equal(Cell, under.Count(p => p.IsPit));
        Assert.True(under.Count > Cell * Cell / 2);

        rig.Fill.Refresh();
        Assert.Equal(Cell, rig.Fill.CellsRead);
        Assert.False(rig.Fill.IsUnfilled(SampleX, SampleZ));
    }

    [Fact]
    public void ASampleTheCallerCallsDryIsNeverAsked()
    {
        var rig = new Rig();
        foreach ((int x, int z) in Rig.Footprint())
        {
            rig.DigColumn(x, z);
        }

        rig.Fill.Refresh((_, _) => false);
        Assert.False(rig.Fill.IsUnfilled(SampleX, SampleZ));
        Assert.Equal(0, rig.Fill.CellsRead);
        Assert.Equal(0, rig.Fill.SamplesAsked);
        Assert.Equal(1, rig.Fill.SamplesSkipped);

        rig.Fill.Refresh((x, z) => x == SampleX && z == SampleZ);
        Assert.True(rig.Fill.IsUnfilled(SampleX, SampleZ));
        Assert.Equal(1, rig.Fill.SamplesAsked);
    }

    [Fact]
    public void AMixedFootprintIsReadUntilDecidedAndNoFurther()
    {
        // Alternating dry and full: neither side reaches its bound until the very last pit.
        var rig = new Rig();
        (int X, int Z)[] footprint = [.. Rig.Footprint()];
        for (int i = 0; i < footprint.Length; i++)
        {
            rig.DigColumn(footprint[i].X, footprint[i].Z);
            if (i % 2 == 1)
            {
                rig.FillSeedCell(footprint[i].X, footprint[i].Z, FluidConstants.CellCapacityMillilitres);
            }
        }

        rig.Fill.Refresh();
        Assert.False(rig.Fill.IsUnfilled(SampleX, SampleZ));
        Assert.Equal(64, rig.Fill.CellsRead);

        // The first 40 full: decided after 32 reads, not 64.
        var early = new Rig();
        for (int i = 0; i < footprint.Length; i++)
        {
            early.DigColumn(footprint[i].X, footprint[i].Z);
            if (i < 40)
            {
                early.FillSeedCell(footprint[i].X, footprint[i].Z, FluidConstants.CellCapacityMillilitres);
            }
        }

        early.Fill.Refresh();
        Assert.False(early.Fill.IsUnfilled(SampleX, SampleZ));
        Assert.Equal(32, early.Fill.CellsRead);
    }

    [Fact]
    public void SlicedEverySampleIsAskedOnceInFourAndAGoneOneIsDropped()
    {
        // Sixteen samples, each wholly dug and dry, over four chunks' worth of ground.
        var rig = new Rig();
        var fill = new BiteFill(rig.Index, rig.Fluid.Grid);
        Assert.Equal(4, fill.Slices);
        var samples = new List<(int X, int Z)>();
        for (int sz = 5; sz < 9; sz++)
        {
            for (int sx = 5; sx < 9; sx++)
            {
                samples.Add((sx, sz));
                for (int z = sz * Cell; z < (sz + 1) * Cell; z++)
                {
                    for (int x = sx * Cell; x < (sx + 1) * Cell; x++)
                    {
                        rig.DigColumn(x, z);
                    }
                }
            }
        }

        // Four refreshes ask each sample exactly once between them, and then all are unfilled.
        int asked = 0;
        for (int i = 0; i < 4; i++)
        {
            int before = fill.UnfilledSamples;
            fill.Refresh();
            asked += fill.SamplesAsked;
            Assert.Equal(before + fill.SamplesAsked, fill.UnfilledSamples);
        }

        Assert.Equal(samples.Count, asked);
        Assert.Equal(samples.Count, fill.UnfilledSamples);
        Assert.All(samples, s => Assert.True(fill.IsUnfilled(s.X, s.Z)));

        // Fill every pit of one sample with rock: its portals leave the index, and within four
        // refreshes its entry goes, the others staying.
        (int X, int Z) gone = samples[5];
        for (int z = gone.Z * Cell; z < (gone.Z + 1) * Cell; z++)
        {
            for (int x = gone.X * Cell; x < (gone.X + 1) * Cell; x++)
            {
                var v = new VoxelPos(x, SeedY, z);
                rig.Terrain.Dug.Remove(v);
                rig.Tracker.OnVoxelChanged(v);
            }
        }

        Assert.Empty(rig.Index.Under(gone.X, gone.Z).Where(p => p.IsPit));
        for (int i = 0; i < 4; i++)
        {
            fill.Refresh();
        }

        Assert.False(fill.IsUnfilled(gone.X, gone.Z));
        Assert.Equal(samples.Count - 1, fill.UnfilledSamples);
    }

    [Fact]
    public void AnUntouchedWorldHasNothingToAsk()
    {
        var rig = new Rig();
        rig.Fill.Refresh();
        Assert.Equal(0, rig.Index.Count);
        Assert.Equal(0, rig.Fill.UnfilledSamples);
        Assert.False(rig.Fill.IsUnfilled(SampleX, SampleZ));
    }
}
