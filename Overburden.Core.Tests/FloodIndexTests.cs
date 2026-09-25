using Overburden.Core.Fluid;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// M10d-3, decision 2: the sheet flood's two-layer index. The oracle is the dictionary it replaced
/// — same questions, same answers — and at the solve's level, the dictionary arm solving the same
/// water to the millilitre.
/// </summary>
public class FloodIndexTests
{
    private const int Cap = FluidConstants.CellCapacityMillilitres;

    [Fact]
    public void AnswersAsADictionaryDoesAcrossChunkFacesAndNegativeCoordinatesAndAfterEveryClear()
    {
        var random = new Random(0xF100D);
        var index = new FloodIndex();
        for (int flood = 0; flood < 40; flood++)
        {
            // A different Y and a different corner of the world each flood, so a slot's arrays are
            // reused by a different chunk column and stale stamps are what keeps them honest.
            int y = random.Next(-100, 100);
            int ox = random.Next(-5_000, 5_000), oz = random.Next(-5_000, 5_000);
            index.Clear();
            var oracle = new Dictionary<VoxelPos, int>();
            for (int op = 0; op < 3_000; op++)
            {
                var cell = new VoxelPos(ox + random.Next(-70, 70), y, oz + random.Next(-70, 70));
                if (random.Next(3) == 0 && !oracle.ContainsKey(cell))
                {
                    oracle[cell] = op;
                    index.Add(cell, op);
                }
                else
                {
                    bool expected = oracle.TryGetValue(cell, out int want);
                    Assert.Equal(expected, index.TryGet(cell, out int got));
                    if (expected)
                    {
                        Assert.Equal(want, got);
                    }
                }
            }
        }

        Assert.InRange(index.SlotsAllocated, 16, 64);
    }

    [Fact]
    public void AFloodReachingPastTheFlatWindowAnswersAsADictionaryDoesOnBothSidesOfItsEdge()
    {
        // The first layer is a flat window of 64 × 64 chunk columns anchored on the flood's first
        // column, with a dictionary past it (2026-09-26). The fuzz above stays within ±3 columns,
        // so it never leaves the window; this one spreads each flood over ±40, so about half its
        // columns are the fallback's, and a Clear re-anchors somewhere else with stale flat stamps
        // under it.
        var random = new Random(0xED6E);
        var index = new FloodIndex();
        int span = 40 * VoxelConstants.ChunkSize;
        for (int flood = 0; flood < 12; flood++)
        {
            int y = random.Next(-50, 50);
            int ox = random.Next(-50_000, 50_000), oz = random.Next(-50_000, 50_000);
            index.Clear();
            var oracle = new Dictionary<VoxelPos, int>();
            for (int op = 0; op < 6_000; op++)
            {
                var cell = new VoxelPos(ox + random.Next(-span, span), y, oz + random.Next(-span, span));
                if (random.Next(2) == 0 && !oracle.ContainsKey(cell))
                {
                    oracle[cell] = op;
                    index.Add(cell, op);
                }

                // Every add is asked back, and a random earlier cell too — a hit is what shows a
                // slot was filed where the next lookup looks.
                bool expected = oracle.TryGetValue(cell, out int want);
                Assert.Equal(expected, index.TryGet(cell, out int got));
                if (expected)
                {
                    Assert.Equal(want, got);
                }
            }

            foreach ((VoxelPos cell, int want) in oracle)
            {
                Assert.True(index.TryGet(cell, out int got) && got == want, $"{cell} lost in flood {flood}");
            }
        }
    }

    [Fact]
    public void ACellAddedBeforeAClearIsGoneAfterItEvenWhenItsSlotIsTakenByAnotherColumn()
    {
        var index = new FloodIndex();
        var first = new VoxelPos(5, 0, 5);
        index.Add(first, 7);
        index.Clear();

        // The same leaf in another chunk column takes slot 0 — the arrays the first cell was in.
        var other = new VoxelPos(5 + VoxelConstants.ChunkSize, 0, 5);
        Assert.False(index.TryGet(other, out _));
        index.Add(new VoxelPos(6 + VoxelConstants.ChunkSize, 0, 5), 1);
        Assert.False(index.TryGet(other, out _));
        Assert.False(index.TryGet(first, out _));
        Assert.Equal(1, index.SlotsAllocated);
    }

    [Fact]
    public void TwoColumnsWhoseZDiffersOnlyInSignAreDifferentSlots()
    {
        // The slot key packs the column's x over its z; a z sign-extended into the x half would
        // make (0, −1) and (−1, −1) one key.
        var index = new FloodIndex();
        index.Add(new VoxelPos(0, 0, -1), 1);
        Assert.False(index.TryGet(new VoxelPos(-1, 0, -1), out _));
        Assert.False(index.TryGet(new VoxelPos(0, 0, VoxelConstants.ChunkSize - 1), out _));
        Assert.True(index.TryGet(new VoxelPos(0, 0, -1), out int r) && r == 1);
    }

#if DEBUG
    [Fact]
    public void OneFloodIsOneY()
    {
        var index = new FloodIndex();
        index.Add(new VoxelPos(0, 3, 0), 0);
        Assert.Throws<InvalidOperationException>(() => index.TryGet(new VoxelPos(0, 4, 0), out _));
        index.Clear();
        index.Add(new VoxelPos(0, 4, 0), 0);
    }
#endif

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void TheDictionaryArmAndTheTwoLayerArmSolveTheSameWaterToTheMillilitre(int seed)
    {
        (long hashedSum, string hashedCounts, int hashedSlots) = Solve(seed, hashed: true);
        (long layeredSum, string layeredCounts, int layeredSlots) = Solve(seed, hashed: false);

        // Each arm used the index it names — or the comparison is one arm against itself.
        Assert.Equal(0, hashedSlots);
        Assert.InRange(layeredSlots, 4, 16);
        Assert.Equal(hashedCounts, layeredCounts);
        Assert.Equal(hashedSum, layeredSum);
        Assert.Contains("/", hashedCounts);
    }

    /// <summary>
    /// The arm test above compares two indexes over one solve, so it cannot see a change both
    /// arms share — and the flat arrays of 2026-09-26 (the parents, the cells as a chain) are
    /// under both. <b>Levelling hands its remainder to the first cells in flood order and packing
    /// breaks ties by it</b>, so a merge that spliced the smaller sheet on the front instead of
    /// the back lands different water with every count the same; the battery found nothing in the
    /// suite catching it (only the bench's fingerprint did). These are the checksums the solve
    /// landed before the change, taken from the old code and matched by the new.
    /// </summary>
    [Theory]
    [InlineData(1, -6763331748501281662L)]
    [InlineData(2, 3840366746218676025L)]
    [InlineData(3, -7692377351435289986L)]
    public void TheSolveLandsTheSameWaterItLandedBeforeTheFlatArrays(int seed, long expected)
    {
        Assert.Equal(expected, Solve(seed, hashed: false).Checksum);
    }

    // A floor straddling chunk faces on both signs, full; a noisy layer with holes over it; an
    // unsupported noisy layer over that. Several passes, re-poured the same in both arms.
    private static (long Checksum, string Counts, int Slots) Solve(int seed, bool hashed)
    {
        var terrain = new BoxTerrain(-40, 0, -40, 39, 2, 39);
        var sim = new FluidSimulation(terrain) { HashedFloodIndex = hashed };
        var random = new Random(seed);
        long checksum = 0;
        string counts = string.Empty;
        for (int pass = 0; pass < 4; pass++)
        {
            var seeds = new List<VoxelPos>();
            for (int x = -40; x < 40; x++)
            {
                for (int z = -40; z < 40; z++)
                {
                    for (int y = 0; y <= 2; y++)
                    {
                        var cell = new VoxelPos(x, y, z);
                        sim.Grid.Drain(cell, Cap);
                        int amount = y == 0 ? Cap : random.Next(5) == 0 ? 0 : random.Next(500, 14_000);
                        if (amount > 0)
                        {
                            sim.Grid.Fill(cell, amount);
                            seeds.Add(cell);
                        }
                    }
                }
            }

            SheetLevelResult r = sim.LevelSheets(seeds, share: SheetSettle.DefaultSettleShare);
            counts += $"{r.Sheets}/{r.Levelled}/{r.Packed}/{r.Skipped}/{r.UnderShare}/{r.CellsVisited}/{r.CellsWritten};";
            for (int x = -40; x < 40; x++)
            {
                for (int z = -40; z < 40; z++)
                {
                    checksum = (checksum * 31) + sim.Grid.AmountAt(new VoxelPos(x, 1, z));
                }
            }
        }

        return (checksum, counts, sim.FloodIndexSlots);
    }
}
