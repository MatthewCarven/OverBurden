using System.Numerics;
using Overburden.Core.Building;
using Overburden.Core.Inventory;
using Overburden.Core.Physics;
using Overburden.Core.Voxels;
using static Overburden.Core.Tests.PhysicsTestHelpers;

namespace Overburden.Core.Tests;

/// <summary>
/// The whole-chunk fill — the entangled miner's undo, 32,768 voxels of an 8 m cube.
/// </summary>
/// <remarks>
/// <para>
/// The mirror of <c>CarveChunkTests</c>, and the claims split the same way: the shape is fixed
/// and enormous, so what can go wrong is <b>which</b> chunk and <b>where it stops</b>. But the
/// fill has one hazard the bite does not, and it is the reason most of this file exists —
/// <b>the fill and the bite name different chunks from the same aim</b>. Mining takes the chunk
/// holding <see cref="VoxelHit.Voxel"/>; building takes the one holding
/// <see cref="VoxelHit.Previous"/>. Copying the mining half is the natural mistake, it is one
/// token, and it breaks the verb precisely in the situation it was built for.
/// </para>
/// <para>
/// The four properties worth the file: it covers its own chunk <b>exactly</b> and touches
/// nothing outside it; it fills the space <b>in front of</b> the surface rather than behind it;
/// only air is replaceable, so a fill can never delete terrain <em>at any scale</em>; and the
/// six buckets still account, so a chunk fill and a brush can be totalled by the same reader.
/// </para>
/// <para>
/// <b>Break-recipes run against the finished code:</b>
/// </para>
/// <list type="number">
/// <item>Loop bounds <c>&lt;=</c> rather than <c>&lt;</c>, spilling one voxel into three
/// neighbouring chunks — <b>caught, by 16</b>. As with the bite, the ones that matter are the
/// tests asserting what was <em>not</em> written: <see cref="PlaceResult.IsAccounted"/> stays
/// true throughout at 35,937 candidates.</item>
/// <item><b>The mirror-image bug: <c>TargetChunk</c> returning <c>hit.Voxel.Chunk</c> rather
/// than <c>hit.Previous.Chunk</c> — caught, by 3.</b> Two of the three are scenario tests
/// rather than the unit one, which is the point: this recipe passes every test whose hit does
/// not straddle a chunk boundary, and a hit that does not straddle one is a hit at something
/// other than a wall. <see cref="AimingAtTheFarWallOfAChunkYouEmptiedRefillsThatChunk"/> is the
/// test that keeps a play-test's worth of confusion out of the way.</item>
/// <item>Only-air-is-replaceable dropped, so a fill overwrites material — <b>caught, by 6</b>,
/// three of them pre-existing brush tests in <c>PlaceTests</c>. One edit breaking every
/// placement shape is what a forked gate would not do, and the same evidence M3c took for
/// <c>Carve.Take</c>.</item>
/// <item>The body refusal dropped — <b>caught, by 3</b>. Worth its own recipe rather than
/// trusting the shared <c>Put</c>, because at 8 m this is the difference between a fill and a
/// burial.</item>
/// <item>The debit moved ahead of the occupancy test, so a fill into rock charges for the rock —
/// <b>caught, by 9</b>. The scaled-up version of the "wall built against a cliff costs the volume
/// of the cliff" case <c>Put</c> documents; at 1 m³ it is an annoyance, at 512 m³ it empties an
/// inventory for a fill that wrote nothing.</item>
/// <item>The debit removed after a successful write — <b>caught, by 7</b>.</item>
/// <item><b><c>TargetChunk</c> by truncating division, so voxel −1 lands in chunk 0 — GREEN, and
/// a genuine hole rather than an identity.</b> Closed by
/// <see cref="AnAimBelowTheOriginNamesTheChunkBelowTheOriginRatherThanChunkZero"/>, after which
/// it is <b>caught by 2</b>. Every other test in this file names its chunk directly, and both
/// seam tests straddle a <em>positive</em> boundary, so nothing exercised the one conversion that
/// can actually round. <b>The bite's battery recorded this exact asymmetry — chunk → voxel cannot
/// round, voxel → chunk can — and its own version of the recipe came back an identity. Here the
/// same reasoning found a live defect, because the fill actually calls the conversion.</b> A
/// recorded finding earning its keep one milestone later, in the sibling that reaches
/// further.</item>
/// <item><c>Candidate()</c> dropped from the chunk loop, so the buckets are derived from
/// themselves — <b>caught, by 15</b>, every one of them through <see cref="Accounted"/>. This is
/// the recipe <see cref="PlaceResult.IsAccounted"/> exists for and it is checked everywhere
/// rather than once.</item>
/// <item>Placing air permitted — <b>caught, by 3</b>, and <b>two of the three are pre-existing
/// <c>PlaceTests</c> cases</b>, so this was never a hole.
/// <see cref="FillingAChunkWithAirIsRefusedLikeEveryOtherPlacement"/> is belt and braces:
/// <c>Validate</c> is shared and already covered for <c>Cube</c> and <c>Voxel</c>, and this
/// pins that the chunk overload reaches it too — the overload where the consequence is deleting
/// 512 m³ with no tool and no gate. <b>M4a's zeroable-epsilon category, not M5c-2's missing
/// wire</b>, and it is written down that way because the opposite was predicted before the
/// battery ran.</item>
/// <item>Unreachable folded into occupied, so an unloaded chunk reports as full — <b>caught, by
/// 2</b>. The <see cref="RayOutcome.Blocked"/>-is-not-<see cref="RayOutcome.Missed"/>
/// distinction, third scale.</item>
/// </list>
/// <para>
/// <b>Ten recipes, one genuine hole, and the hole was the one the sibling battery had already
/// pointed at.</b> The predicted catch-counts written here before the battery ran were wrong in
/// both directions and wrong about which recipe mattered — the shared <c>Put</c> is far better
/// covered than guessed (9, 7, 6 rather than 2, 2, 5), and the one thing genuinely untested was
/// the arithmetic nobody thought to doubt.
/// </para>
/// </remarks>
public class PlaceChunkTests
{
    /// <summary>Voxels in a chunk: the number every count in this file is measured against.</summary>
    private const int ChunkVoxels =
        VoxelConstants.ChunkSize * VoxelConstants.ChunkSize * VoxelConstants.ChunkSize;

    /// <summary>Asserts the buckets add up, and hands the result back for the real assertions.</summary>
    private static PlaceResult Accounted(PlaceResult result)
    {
        Assert.True(result.IsAccounted, $"buckets do not add up: {result}");
        return result;
    }

    /// <summary>A hit on the face between <paramref name="solid"/> and the voxel before it.</summary>
    /// <remarks>
    /// Built by hand rather than cast, so that a test can place the two voxels either side of a
    /// chunk boundary deliberately. The normal and the distance are unread by everything under
    /// test here and are filled in plausibly rather than meaningfully.
    /// </remarks>
    private static VoxelHit HitOn(VoxelPos solid, VoxelPos previous) =>
        new(solid, previous, (0, 1, 0), 4.0f);

    // --------------------------------------------------------------- what it covers

    [Fact]
    public void AChunkFillFillsEveryVoxelOfItsOwnChunkAndNoneOfAnyOther()
    {
        var world = new TestWorld();

        PlaceResult result = Accounted(
            Place.Chunk(world, Table, new ChunkPos(0, 0, 0), Stone));

        Assert.Equal(ChunkVoxels, result.Candidates);
        Assert.Equal(ChunkVoxels, result.Placed);
        Assert.Equal(0, result.Occupied);

        Assert.All(
            world.WrittenVoxels,
            voxel => Assert.Equal(new ChunkPos(0, 0, 0), voxel.Chunk));
    }

    [Fact]
    public void EveryVoxelOfTheChunkIsWrittenExactlyOnce()
    {
        var world = new TestWorld();

        Place.Chunk(world, Table, new ChunkPos(2, -1, 3), Stone);

        // Count against distinct count: a nest that visited a voxel twice would still report
        // 32,768 placed if the second visit found it already filled, so the tally alone cannot
        // see this. It would show up as a doubled inventory bill and nothing else.
        Assert.Equal(ChunkVoxels, world.Writes.Count);
        Assert.Equal(ChunkVoxels, world.WrittenVoxels.Count);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(5, 2, -7)]
    [InlineData(-1, -1, -1)]
    [InlineData(-3, 4, -12)]
    public void ChunksBelowTheOriginAreWholeChunksToo(int cx, int cy, int cz)
    {
        var chunk = new ChunkPos(cx, cy, cz);
        var world = new TestWorld();

        PlaceResult result = Accounted(Place.Chunk(world, Table, chunk, Stone));

        // The negative cases are the ones that matter: VoxelPos.Chunk shifts rather than divides,
        // so a fill that derived its origin by truncating division would land chunk −1 on top of
        // chunk 0 and leave a chunk-wide seam through the origin — invisible in the world.
        Assert.Equal(ChunkVoxels, result.Placed);
        Assert.All(world.WrittenVoxels, voxel => Assert.Equal(chunk, voxel.Chunk));
    }

    // ------------------------------------------------- which chunk, given an aim

    [Fact]
    public void TheChunkFilledIsTheOneInFrontOfTheSurfaceRatherThanTheOneBehindIt()
    {
        // The hit voxel is the first of chunk 1; the empty voxel before it is the last of chunk 0.
        var solid = new VoxelPos(VoxelConstants.ChunkSize, 4, 4);
        var previous = new VoxelPos(VoxelConstants.ChunkSize - 1, 4, 4);
        Assert.NotEqual(solid.Chunk, previous.Chunk);

        Assert.Equal(previous.Chunk, Place.TargetChunk(HitOn(solid, previous)));
    }

    [Fact]
    public void AimingAtTheFarWallOfAChunkYouEmptiedRefillsThatChunk()
    {
        // Solid rock everywhere except chunk 0, which has been bitten out — the shape the whole
        // verb exists for, and the one where taking hit.Voxel.Chunk would fill the wall instead.
        var world = new TestWorld { Background = _ => Stone };
        VoxelPos origin = new ChunkPos(0, 0, 0).OriginVoxel;
        world.Fill(
            origin,
            new VoxelPos(
                origin.X + VoxelConstants.ChunkSize - 1,
                origin.Y + VoxelConstants.ChunkSize - 1,
                origin.Z + VoxelConstants.ChunkSize - 1),
            Air);

        // Looking east across the hole at its far wall: the wall is chunk 1, the air is chunk 0.
        var wall = new VoxelPos(VoxelConstants.ChunkSize, 4, 4);
        var lastAir = new VoxelPos(VoxelConstants.ChunkSize - 1, 4, 4);

        PlaceResult result = Accounted(
            Place.Chunk(world, Table, HitOn(wall, lastAir), Stone));

        Assert.Equal(ChunkVoxels, result.Placed);
        Assert.Equal(0, result.Occupied);
        Assert.All(
            world.WrittenVoxels,
            voxel => Assert.Equal(new ChunkPos(0, 0, 0), voxel.Chunk));
    }

    [Theory]
    [InlineData(-1, -1, -1, -1, -1, -1)]
    [InlineData(-32, -32, -32, -1, -1, -1)]
    [InlineData(-33, 5, -1, -2, 0, -1)]
    [InlineData(0, 0, 0, 0, 0, 0)]
    public void AnAimBelowTheOriginNamesTheChunkBelowTheOriginRatherThanChunkZero(
        int px, int py, int pz, int cx, int cy, int cz)
    {
        // Written because the battery's "TargetChunk by truncating division" recipe came back
        // GREEN — a real hole rather than an identity. Every other test in this file names its
        // chunk directly, and the two seam tests straddle a positive boundary, so nothing
        // exercised the one conversion that can actually round: voxel -> chunk.
        //
        // VoxelPos.Chunk shifts rather than divides, so voxel -1 belongs to chunk -1. Truncating
        // division puts it in chunk 0 beside voxel 0, which collapses two chunks into one and
        // leaves a chunk-wide seam through the origin — invisible in the world, and it would fill
        // the wrong 512 m3 every time somebody built west or below spawn.
        var previous = new VoxelPos(px, py, pz);

        Assert.Equal(
            new ChunkPos(cx, cy, cz),
            Place.TargetChunk(HitOn(previous.Offset(0, 1, 0), previous)));
    }

    [Fact]
    public void TwoAimsEitherSideOfASeamFillDifferentChunks()
    {
        var world = new TestWorld();

        // Two hits a voxel apart across a chunk boundary. Their Previous voxels are in different
        // chunks, so the fills must be too — the invisible-seam case, deliberately provoked.
        ChunkPos near = Place.TargetChunk(
            HitOn(new VoxelPos(VoxelConstants.ChunkSize, 4, 4),
                  new VoxelPos(VoxelConstants.ChunkSize - 1, 4, 4)));
        ChunkPos far = Place.TargetChunk(
            HitOn(new VoxelPos(VoxelConstants.ChunkSize + 1, 4, 4),
                  new VoxelPos(VoxelConstants.ChunkSize, 4, 4)));

        Assert.Equal(new ChunkPos(0, 0, 0), near);
        Assert.Equal(new ChunkPos(1, 0, 0), far);
        Assert.Empty(world.Writes);
    }

    // ------------------------------------------------------------------ the gate

    [Fact]
    public void FillingIntoSolidRockWritesNothingAndReportsItAllOccupied()
    {
        // The safety claim at its own scale: only air is replaceable, so this verb cannot be a
        // chunk-shaped overwrite. If it ever became one, the hardness gate would be bypassable by
        // the building verb over 512 m3 at a time.
        var world = new TestWorld { Background = _ => Stone };

        PlaceResult result = Accounted(
            Place.Chunk(world, Table, new ChunkPos(0, 0, 0), Dirt));

        Assert.Equal(ChunkVoxels, result.Candidates);
        Assert.Equal(ChunkVoxels, result.Occupied);
        Assert.Equal(0, result.Placed);
        Assert.Empty(world.Writes);
    }

    [Fact]
    public void AFillLeavesTheSpaceThePlayerIsStandingInAlone()
    {
        // The ordinary use of this verb is refilling the hole you are stood in, so without the
        // body refusal the ordinary use walls the player in.
        var world = new TestWorld();
        Aabb body = Aabb.FromFeet(new Vector3(4.0f, 4.0f, 4.0f), 0.5f, 1.8f);
        (VoxelPos min, VoxelPos max) = body.VoxelBounds;

        PlaceResult result = Accounted(
            Place.Chunk(world, Table, new ChunkPos(0, 0, 0), Stone, body));

        Assert.True(result.InsidePlayer > 0, "the body sits inside the chunk being filled");
        Assert.Equal(ChunkVoxels - result.InsidePlayer, result.Placed);

        Assert.All(
            world.WrittenVoxels,
            voxel => Assert.False(
                voxel.X >= min.X && voxel.X <= max.X
                && voxel.Y >= min.Y && voxel.Y <= max.Y
                && voxel.Z >= min.Z && voxel.Z <= max.Z,
                $"{voxel} was written inside the player's own box"));
    }

    [Fact]
    public void FillingAChunkWithAirIsRefusedLikeEveryOtherPlacement()
    {
        // Written because the recipe that permitted it came back green: Validate is shared, so
        // the guard was never missing, but nothing asserted this overload reaches it — and this
        // is the overload where placing air deletes 512 m3 with no tool and no hardness gate.
        var world = new TestWorld { Background = _ => Stone };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => Place.Chunk(world, Table, new ChunkPos(0, 0, 0), Air));

        Assert.Empty(world.Writes);
    }

    [Fact]
    public void AnUnloadedChunkIsUnreachableRatherThanRefusedAndSaysSo()
    {
        var world = new TestWorld();
        world.Unload(new ChunkPos(0, 0, 0));

        PlaceResult result = Accounted(
            Place.Chunk(world, Table, new ChunkPos(0, 0, 0), Stone));

        // An unknown, not a full chunk. Folding these together would make a fill aimed past the
        // loaded edge report as "there was already something there", which is the one reading
        // that stops the player trying again once it loads.
        Assert.Equal(ChunkVoxels, result.Unreachable);
        Assert.Equal(0, result.Occupied);
        Assert.Equal(0, result.Placed);
        Assert.False(result.Complete);
    }

    // ------------------------------------------------------------- the inventory

    [Fact]
    public void AFillStopsWhenTheStockRunsOutAndTheCountSaysHowShort()
    {
        // 512,000 litres a fill, so running dry is the ordinary outcome rather than the unusual
        // one — and the partial-fill rule is what makes that bearable rather than baffling.
        const int Stock = 1_000;
        var world = new TestWorld();
        var inventory = new VolumetricInventory();
        inventory.Add(Stone, Stock);

        PlaceResult result = Accounted(
            Place.Chunk(world, Table, new ChunkPos(0, 0, 0), Stone, body: null, from: inventory));

        Assert.Equal(Stock, result.Placed);
        Assert.Equal(ChunkVoxels - Stock, result.OutOfStock);
        Assert.Equal(Stock, world.Writes.Count);
        Assert.Equal(0L, inventory.Voxels(Stone));
    }

    [Fact]
    public void AFillIntoRockCostsNothingRatherThanTheVolumeOfTheRock()
    {
        // Put consults the stock last, after the voxel is known to be fillable. At brush scale
        // charging earlier is an annoyance; at 32,768 candidates it empties an inventory for a
        // fill that wrote nothing.
        var world = new TestWorld { Background = _ => Stone };
        var inventory = new VolumetricInventory();
        inventory.Add(Dirt, 500L);

        PlaceResult result = Accounted(
            Place.Chunk(world, Table, new ChunkPos(0, 0, 0), Dirt, body: null, from: inventory));

        Assert.Equal(ChunkVoxels, result.Occupied);
        Assert.Equal(0, result.OutOfStock);
        Assert.Equal(500L, inventory.Voxels(Dirt));
    }

    [Fact]
    public void TheInventoryIsDebitedExactlyOncePerVoxelActuallyPlaced()
    {
        var world = new TestWorld();
        var inventory = new VolumetricInventory();
        inventory.Add(Stone, ChunkVoxels * 2L);

        PlaceResult result = Accounted(
            Place.Chunk(world, Table, new ChunkPos(0, 0, 0), Stone, body: null, from: inventory));

        Assert.Equal(ChunkVoxels, result.Placed);
        Assert.Equal(ChunkVoxels, inventory.Voxels(Stone));
    }

    // ------------------------------------------------------------ the accounting

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void CandidatesIsTheChunkVolumeWhateverTheChunkHeld(bool solid, bool unloaded)
    {
        var world = solid ? new TestWorld { Background = _ => Stone } : new TestWorld();
        if (unloaded)
        {
            world.Unload(new ChunkPos(0, 0, 0));
        }

        PlaceResult result = Accounted(
            Place.Chunk(world, Table, new ChunkPos(0, 0, 0), Dirt));

        // The shape is the storage grid, so unlike a sphere there is no aim, no radius and no
        // rounding that could change how many voxels are considered. A candidate count that ever
        // moved would mean the loop bounds had.
        Assert.Equal(ChunkVoxels, result.Candidates);
    }
}
