using System.Numerics;
using MinerCraft.Core.Building;
using MinerCraft.Core.Mining;
using MinerCraft.Core.Physics;
using MinerCraft.Core.Voxels;
using static MinerCraft.Core.Tests.PhysicsTestHelpers;

namespace MinerCraft.Core.Tests;

/// <summary>
/// The brushes: what they fill, where they land, and what they refuse.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every result goes through <see cref="Accounted"/></b>, so
/// <see cref="PlaceResult.IsAccounted"/> is checked by every test rather than by one — the
/// reason <see cref="CarveResult.IsAccounted"/> records: the failure it catches is a
/// <c>continue</c> added later that is counted nowhere, and it leaves the report plausible.
/// </para>
/// <para>
/// <b>The headline is <see cref="APlacedRampIsWalkedUpByThePhysicsThatAlreadyExists"/>.</b> The
/// plan's claim for the whole staircase-not-slope decision is that a 0.25 m rise sits inside
/// M4b's 0.5 m step height, so a placed ramp needs no new collision shape and no new anything.
/// That is arithmetic until a body actually walks up one, and both halves are pure Core, so it
/// costs a test rather than a play-test.
/// </para>
/// </remarks>
public class PlaceTests
{
    /// <summary>60 Hz, matching <c>PlayerMotionTests</c>. Not the sim tick.</summary>
    private const float Dt = 1.0f / 60.0f;

    private static readonly PlayerMotionConfig Config = PlayerMotionConfig.Default;

    private static readonly Vector3 East = new(1.0f, 0.0f, 0.0f);

    /// <summary>Asserts the buckets add up, and hands the result back for the real assertions.</summary>
    private static PlaceResult Accounted(PlaceResult result)
    {
        Assert.True(result.IsAccounted, $"buckets do not add up: {result}");
        return result;
    }

    private static TestWorld EmptyWorld() => new();

    private static TestWorld SolidWorld() => new() { Background = _ => Stone };

    // ------------------------------------------------------------------------- the lattice

    [Fact]
    public void TheCellConstantsAgreeWithEachOther()
    {
        // The snap is a shift, so the two have to agree or every brush lands somewhere else.
        // Cheap to pin and impossible to notice by reading, which is the whole category.
        Assert.Equal(BrushCell.Voxels, 1 << BrushCell.VoxelsLog2);
        Assert.Equal(1.0f, BrushCell.SizeMetres);
    }

    [Fact]
    public void PointingAnywhereInACellBuildsTheSameBrick()
    {
        // The lattice property, stated as the thing a player would notice: a wall built by
        // clicking roughly at each metre has no seams and no one-voxel gaps, because where you
        // pointed inside the cell never mattered.
        var cell = new BrushCell(3, -2, 5);

        for (int lz = 0; lz < BrushCell.Voxels; lz++)
        for (int lx = 0; lx < BrushCell.Voxels; lx++)
        for (int ly = 0; ly < BrushCell.Voxels; ly++)
        {
            VoxelPos voxel = cell.VoxelAt(lx, ly, lz);

            Assert.Equal(cell, BrushCell.FromVoxel(voxel));
            Assert.Equal(cell, Place.TargetCell(new VoxelHit(voxel.Offset(0, -1, 0), voxel, (0, 1, 0), 1.0f)));
        }
    }

    [Fact]
    public void ACellIsFlooredNotTruncatedSoNegativeCoordinatesDoNotCollapseOntoZero()
    {
        // VoxelPos records this as a seam one chunk wide through the origin that is miserable to
        // diagnose later. Half the world has negative coordinates, and a lattice built with a
        // division would put voxels −1 and 0 in the same cell — two adjacent metres of wall
        // sharing one brick.
        Assert.Equal(new BrushCell(-1, -1, -1), BrushCell.FromVoxel(new VoxelPos(-1, -1, -1)));
        Assert.Equal(new BrushCell(-1, -1, -1), BrushCell.FromVoxel(new VoxelPos(-4, -4, -4)));
        Assert.Equal(new BrushCell(-2, -2, -2), BrushCell.FromVoxel(new VoxelPos(-5, -5, -5)));
        Assert.Equal(new BrushCell(0, 0, 0), BrushCell.FromVoxel(VoxelPos.Zero));

        // And the round trip: a cell's own origin voxel is in that cell.
        for (int i = -3; i <= 3; i++)
        {
            var cell = new BrushCell(i, i, i);
            Assert.Equal(cell, BrushCell.FromVoxel(cell.OriginVoxel));
        }
    }

    // --------------------------------------------------------------------------- the brushes

    [Fact]
    public void ACubeFillsExactlyItsOwnCell()
    {
        TestWorld world = EmptyWorld();
        var cell = new BrushCell(-2, 4, 7);

        PlaceResult result = Accounted(Place.Cube(world, Table, cell, Stone));

        Assert.Equal(64, result.Candidates);
        Assert.Equal(64, result.Placed);
        Assert.True(result.Complete);

        var expected = new HashSet<VoxelPos>();
        for (int lz = 0; lz < 4; lz++)
        for (int lx = 0; lx < 4; lx++)
        for (int ly = 0; ly < 4; ly++)
        {
            expected.Add(cell.VoxelAt(lx, ly, lz));
        }

        Assert.Equal(expected, world.WrittenVoxels);
    }

    [Fact]
    public void TwoCubesInAdjacentCellsTileWithNoGapAndNoOverlap()
    {
        // The reason the lattice exists. A gap shows as a one-voxel slit of daylight through a
        // wall; an overlap shows as nothing at all, until the inventory is charged twice for it.
        TestWorld world = EmptyWorld();
        var west = new BrushCell(0, 0, 0);

        Accounted(Place.Cube(world, Table, west, Stone));
        PlaceResult second = Accounted(Place.Cube(world, Table, west.Offset(1, 0, 0), Stone));

        Assert.Equal(64, second.Placed);
        Assert.Equal(0, second.Occupied);
        Assert.Equal(128, world.Writes.Count);
        Assert.Equal(128, world.WrittenVoxels.Count);

        // No slit: every voxel across the join is solid, including the pair either side of it.
        for (int z = 0; z < 4; z++)
        for (int y = 0; y < 4; y++)
        for (int x = 0; x < 8; x++)
        {
            Assert.True(
                world.TryGetMaterial(new VoxelPos(x, y, z), out int material) && material == Stone,
                $"voxel ({x}, {y}, {z}) is not solid");
        }
    }

    [Fact]
    public void TheSlabIsTheBottomHalfOfTheCell()
    {
        TestWorld world = EmptyWorld();
        var cell = new BrushCell(1, 1, 1);

        PlaceResult result = Accounted(Place.Slab(world, Table, cell, Stone));

        Assert.Equal(32, result.Placed);

        // Bottom half rather than middle or top: a slab is a floor, and a floor has to be
        // walkable from the cell beside it.
        Assert.All(world.WrittenVoxels, voxel => Assert.InRange(voxel.Y - cell.OriginVoxel.Y, 0, 1));
    }

    [Theory]
    [InlineData(BrushFacing.PositiveX)]
    [InlineData(BrushFacing.NegativeX)]
    [InlineData(BrushFacing.PositiveZ)]
    [InlineData(BrushFacing.NegativeZ)]
    public void ARampRisesOneVoxelPerStepTowardItsFacing(BrushFacing facing)
    {
        TestWorld world = EmptyWorld();
        var cell = new BrushCell(0, 0, 0);

        PlaceResult result = Accounted(Place.Ramp(world, Table, cell, Stone, facing));

        // 1 + 2 + 3 + 4 across the full width.
        Assert.Equal(40, result.Placed);

        for (int lz = 0; lz < 4; lz++)
        for (int lx = 0; lx < 4; lx++)
        {
            int along = facing switch
            {
                BrushFacing.PositiveX => lx,
                BrushFacing.NegativeX => 3 - lx,
                BrushFacing.PositiveZ => lz,
                _ => 3 - lz,
            };

            for (int ly = 0; ly < 4; ly++)
            {
                bool solid = world.TryGetMaterial(cell.VoxelAt(lx, ly, lz), out int material)
                             && material == Stone;

                // Column height is one voxel per step along the facing, so the low edge is one
                // voxel tall and the high edge fills the cell.
                Assert.Equal(ly <= along, solid);
            }
        }
    }

    [Fact]
    public void APlacedSphereExactlyRefillsACarvedOneOfTheSameRadius()
    {
        // Why Carve and Place share VoxelSphere. Two copies of the membership test would drift,
        // and the symptom is a rind of stragglers left behind when you fill in a hole you made —
        // invisible until somebody tries it, and by then both copies have moved.
        //
        // The fixture is picked so that the boundary case is *reachable*, and the first version
        // of it was not: with an arbitrary centre and radius, no voxel centre lands at exactly
        // the radius, so `<` and `<=` agree everywhere and a divergent private copy passed the
        // battery. Centre on a voxel centre with a whole number of voxels for a radius, and the
        // voxels along each axis sit exactly on the surface.
        TestWorld world = SolidWorld();
        var origin = new VoxelPos(16, -8, 32);
        (float cx, float cy, float cz) = origin.CentreMetres;
        var centre = new Vector3(cx, cy, cz);
        const float Radius = 0.5f;
        const int RadiusVoxels = 2;

        CarveResult carved = Carve.Sphere(world, Table, centre, Radius, hardnessThreshold: 5.0f);
        Assert.True(carved.Removed > 0, "the fixture carved nothing; the refill would be vacuous");

        // The fixture says out loud that it reached the case it exists for: the voxel exactly a
        // radius away is inside the sphere, which is the one `<` would drop.
        Assert.Contains(origin.Offset(RadiusVoxels, 0, 0), world.WrittenVoxels);

        PlaceResult filled = Accounted(Place.Sphere(world, Table, centre, Radius, Stone));

        Assert.Equal(carved.Candidates, filled.Candidates);
        Assert.Equal(carved.Removed, filled.Placed);
        Assert.Equal(0, filled.Occupied);

        // And the world really is solid again, checked over the whole bounding box rather than
        // the sphere, so that a placement which reached *further* than the carve fails too.
        for (int z = origin.Z - 3; z <= origin.Z + 3; z++)
        for (int x = origin.X - 3; x <= origin.X + 3; x++)
        for (int y = origin.Y - 3; y <= origin.Y + 3; y++)
        {
            Assert.True(
                world.TryGetMaterial(new VoxelPos(x, y, z), out int material) && material == Stone,
                $"voxel ({x}, {y}, {z}) was left hollow");
        }
    }

    // -------------------------------------------------------------------------- the refusals

    [Fact]
    public void ABrushNeverOverwritesMaterialAndReportsItOccupied()
    {
        // The hardness gate, defended from the other verb. A brush that overwrote material would
        // let anyone delete bedrock with a hand_pick: place dirt into it, then mine the dirt.
        TestWorld world = SolidWorld();

        PlaceResult result = Accounted(Place.Cube(world, Table, new BrushCell(0, 0, 0), Dirt));

        Assert.Equal(64, result.Occupied);
        Assert.Equal(0, result.Placed);
        Assert.Empty(world.Writes);
    }

    [Fact]
    public void ABrushFillsTheAirAroundRockRatherThanEatingIt()
    {
        // The same rule seen from the useful side: pushing a brush into a hillside gives you the
        // half that was air, which is what makes building against terrain work at all.
        var cell = new BrushCell(0, 0, 0);
        TestWorld world = new() { Background = v => v.X >= cell.VoxelAt(2, 0, 0).X ? Stone : Air };

        PlaceResult result = Accounted(Place.Cube(world, Table, cell, Dirt));

        Assert.Equal(32, result.Placed);
        Assert.Equal(32, result.Occupied);
        Assert.All(world.WrittenVoxels, voxel => Assert.InRange(voxel.X, 0, 1));
    }

    [Fact]
    public void PlacingAirIsRefusedBecauseItIsACarveWithNoGate()
    {
        TestWorld world = EmptyWorld();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => Place.Cube(world, Table, new BrushCell(0, 0, 0), Air));
    }

    [Fact]
    public void PlacingAMaterialNoTableHasIsRefused()
    {
        // Caught here rather than at the write: a voxel holding an ID the table has never heard
        // of is an exception in the mesher a frame later, a long way from the cause.
        TestWorld world = EmptyWorld();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => Place.Cube(world, Table, new BrushCell(0, 0, 0), Table.Count));
    }

    [Fact]
    public void VoxelsInsideTheBodyAreRefusedAndTheRestOfTheBrushStillLands()
    {
        // "You cannot build a block inside your own body" is not a mechanic anyone will miss, and
        // refusing here keeps placement from being a second deliberate caller of TryUnstick —
        // whose only honest home is "something went wrong", which is M9's collapse.
        TestWorld world = EmptyWorld();
        var cell = new BrushCell(0, 0, 0);

        // Feet at the cell's minimum corner: the body stands in the low-X, low-Z quarter of it.
        Aabb body = Aabb.FromFeet(new Vector3(0.25f, 0.0f, 0.25f), 0.5f, 1.8f);

        PlaceResult result = Accounted(Place.Cube(world, Table, cell, Stone, body));

        Assert.True(result.InsidePlayer > 0, "the fixture did not overlap the body");
        Assert.Equal(64 - result.InsidePlayer, result.Placed);

        (VoxelPos min, VoxelPos max) = body.VoxelBounds;
        Assert.All(world.WrittenVoxels, voxel => Assert.False(
            voxel.X >= min.X && voxel.X <= max.X
            && voxel.Y >= min.Y && voxel.Y <= max.Y
            && voxel.Z >= min.Z && voxel.Z <= max.Z,
            $"{voxel} was written inside the body"));
    }

    [Fact]
    public void AnUnloadedChunkIsUnreachableRatherThanPlaced()
    {
        // An unknown, not a refusal — the distinction M3b drew between Blocked and Missed. A
        // brush half of which fell off the edge of the loaded world did less than it looked like.
        TestWorld world = EmptyWorld();
        var cell = new BrushCell(0, 0, 0);
        world.Unload(cell.OriginVoxel.Chunk);

        PlaceResult result = Accounted(Place.Cube(world, Table, cell, Stone));

        Assert.Equal(64, result.Unreachable);
        Assert.Equal(0, result.Placed);
        Assert.False(result.Complete);
    }

    [Fact]
    public void AWriteRefusedAfterAGoodReadIsUnreachableRatherThanPlaced()
    {
        // The two halves of IVoxelEditor disagreeing. It should not happen — a read that
        // succeeded means the chunk is loaded and generated — so it is reported rather than
        // ignored, because an editor that ever does this would otherwise lose voxels silently.
        TestWorld world = EmptyWorld();
        var cell = new BrushCell(0, 0, 0);
        world.RefuseWrites(cell.OriginVoxel.Chunk);

        PlaceResult result = Accounted(Place.Cube(world, Table, cell, Stone));

        Assert.Equal(64, result.Unreachable);
        Assert.Equal(0, result.Placed);
    }

    [Theory]
    [InlineData(1.0f, 0.1f, BrushFacing.PositiveX)]
    [InlineData(-1.0f, 0.1f, BrushFacing.NegativeX)]
    [InlineData(0.1f, 1.0f, BrushFacing.PositiveZ)]
    [InlineData(0.1f, -1.0f, BrushFacing.NegativeZ)]
    [InlineData(0.7f, 0.71f, BrushFacing.PositiveZ)]
    public void ARampRisesAwayFromWhoeverPlacedIt(float dx, float dz, BrushFacing expected)
    {
        // Away rather than toward, so walking forward onto a ramp climbs it. Both readings are
        // natural and the wrong one builds every staircase backwards.
        //
        // The Y component is ignored on purpose: a ramp rising along Y is a wall, and what
        // somebody looking down at a step wants is the step going away from them.
        Assert.Equal(expected, Place.FacingFrom(new Vector3(dx, -0.9f, dz)));
    }

    // --------------------------------------------------------------- one voxel at a time

    [Fact]
    public void APreciseePlacementFillsTheVoxelBeforeTheSurfaceAndNothingElse()
    {
        // The mirror of Carve.Chip, and the same reason for taking hit.Previous rather than a
        // shape centred on it: the outline the player is looking at *is* the thing about to
        // change, which is the whole of why precision reads as precise.
        TestWorld world = new() { Background = v => v.Y <= 3 ? Stone : Air };
        var surface = new VoxelPos(4, 3, 4);
        var above = surface.Offset(0, 1, 0);
        var hit = new VoxelHit(surface, above, (0, 1, 0), 2.0f);

        Assert.Equal(above, Place.TargetVoxel(hit));

        PlaceResult result = Accounted(
            Place.Voxel(world, Table, Place.TargetVoxel(hit), Dirt));

        Assert.Equal(1, result.Candidates);
        Assert.Equal(1, result.Placed);
        Assert.Equal([above], world.WrittenVoxels);
    }

    [Fact]
    public void OneVoxelIsSixtyFourTimesLessThanACubeAndThatIsThePoint()
    {
        // Stated as a test because it is the argument for keeping the brushes rather than a
        // remark: a wall built voxel by voxel is 64x the clicks, so precision has to stay the
        // exception. M3c made the same argument for mining and it transfers without a change.
        TestWorld world = new();
        var cell = new BrushCell(0, 0, 0);

        PlaceResult cube = Accounted(Place.Cube(world, Table, cell, Stone));

        TestWorld other = new();
        PlaceResult one = Accounted(Place.Voxel(other, Table, cell.VoxelAt(0, 0, 0), Stone));

        Assert.Equal(64, cube.Placed);
        Assert.Equal(1, one.Placed);
    }

    [Fact]
    public void ThePreciseShapeSharesEveryRuleWithTheBrushes()
    {
        // One gate, not two. M3c measured what this is worth on the mining side: removing the
        // hardness check from Carve.Take failed nine tests across both shapes, which a forked copy
        // in each shape would not have done.
        TestWorld world = new() { Background = _ => Stone };
        var voxel = new VoxelPos(4, 4, 4);

        // Only air is replaceable.
        Assert.Equal(1, Accounted(Place.Voxel(world, Table, voxel, Dirt)).Occupied);

        // Air is refused as a material, and so is one the table does not have.
        TestWorld empty = new();
        Assert.Throws<ArgumentOutOfRangeException>(() => Place.Voxel(empty, Table, voxel, Air));
        Assert.Throws<ArgumentOutOfRangeException>(() => Place.Voxel(empty, Table, voxel, Table.Count));

        // The body is refused, and the inventory is charged.
        Aabb body = Aabb.FromFeet(new Vector3(1.125f, 1.0f, 1.125f), 0.5f, 1.8f);
        (VoxelPos min, _) = body.VoxelBounds;
        Assert.Equal(1, Accounted(Place.Voxel(empty, Table, min, Stone, body)).InsidePlayer);

        var inventory = new MinerCraft.Core.Inventory.VolumetricInventory();
        inventory.Add(Stone, 1);
        Assert.Equal(1, Accounted(Place.Voxel(empty, Table, voxel, Stone, null, inventory)).Placed);
        Assert.Equal(0, inventory.Voxels(Stone));
        Assert.Equal(1, Accounted(Place.Voxel(empty, Table, voxel.Offset(1, 0, 0), Stone, null, inventory)).OutOfStock);
    }

    // ------------------------------------------------------------------------ does it walk?

    [Fact]
    public void APlacedRampIsWalkedUpByThePhysicsThatAlreadyExists()
    {
        // The claim the whole staircase-not-slope decision rests on: a 0.25 m rise is inside
        // M4b's 0.5 m step height, so a placed ramp needs no new collision shape and no new
        // anything. Arithmetic until a body walks up one.
        //
        // Two ramps and a landing, each cell one higher than the last — which is how a staircase
        // actually tiles, and is a second reason the brush set only makes sense on a lattice.
        TestWorld world = FlatGround();

        Accounted(Place.Ramp(world, Table, new BrushCell(2, 1, 0), Stone, BrushFacing.PositiveX));
        Accounted(Place.Ramp(world, Table, new BrushCell(3, 2, 0), Stone, BrushFacing.PositiveX));
        Accounted(Place.Cube(world, Table, new BrushCell(4, 2, 0), Stone));

        // A wall at the top, so the walk ends standing on the landing rather than off the end of
        // it. Without this the body climbs, strolls off the edge and falls back to the floor —
        // which the first version of this test did, and reported as "it never climbed".
        Accounted(Place.Cube(world, Table, new BrushCell(5, 3, 0), Stone));

        // Feet on the floor, west of the stairs, in the middle of their one-metre width.
        PlayerState state = PlayerState.AtFeet(new Vector3(1.5f, 1.0f, 0.5f), Config);
        float highest = state.Box.Min.Y;

        for (int i = 0; i < 180; i++)
        {
            // No jump, ever. A staircase you have to jump up is a wall.
            state = PlayerMotion.Step(world, Table, state, MoveIntent.Walking(East), Config, Dt).State;
            highest = MathF.Max(highest, state.Box.Min.Y);
        }

        Assert.True(state.Grounded, $"the body should be standing on the landing; peak was {highest}");
        Assert.InRange(state.Box.Min.Y, 3.0f, 3.0f + (VoxelCollider.ContactEpsilonMetres * 2.0f));
        Assert.True(state.Box.Min.X > 4.0f, $"the body only reached x = {state.Box.Min.X}");
    }
}
