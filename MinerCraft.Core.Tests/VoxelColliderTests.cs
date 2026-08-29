using System.Numerics;
using MinerCraft.Core.Content;
using MinerCraft.Core.Physics;
using MinerCraft.Core.Voxels;

namespace MinerCraft.Core.Tests;

/// <summary>
/// The AABB-vs-grid collider, against a dictionary of voxels. No gravity, no input, no Godot
/// and no threads — a box, a displacement and a world, which is the whole reason M4a comes
/// first.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every motion test goes through <see cref="Step"/></b>, which asserts the shared
/// postcondition: a box never <i>ends</i> a step overlapping solid. Putting it in one helper
/// rather than in the fuzz alone is deliberate — a resolve that is subtly wrong in a
/// hand-built case tends to be wrong in a way the hand-built assertions were not written to
/// notice.
/// </para>
/// <para>
/// The sharp one is <see cref="ThousandsOfWildDisplacementsNeverGetTheBoxOutOfASealedRoom"/>.
/// It has no reference implementation and no tolerance, because escaping a sealed room is a
/// boolean.
/// </para>
/// </remarks>
public class VoxelColliderTests
{
    private const int Air = MaterialIds.AirNumericId;
    private const int Dirt = 1;
    private const int Water = 2;
    private const int Stone = 3;

    /// <summary>Two solids and a fluid, so the solidity predicate has something to get wrong.</summary>
    private static readonly MaterialTable Table = MaterialTable.ForTesting(
        //             air    dirt   water  stone
        isOpaque: [false, true, false, true],
        isFluid: [false, false, true, false],
        textureLayer: [0, 1, 2, 3],
        hardness: [0f, 0.8f, 0f, 3.0f]);

    /// <summary>
    /// The body M4b's table specifies: 0.5 × 1.8 × 0.5 m, or 2 × 7.2 × 2 voxels. M4a does not
    /// own these numbers, but testing the shape that will actually walk around is worth more
    /// than testing a convenient cube.
    /// </summary>
    private const float PlayerWidth = 0.5f;

    private const float PlayerHeight = 1.8f;

    /// <summary>
    /// What one 60 Hz step of M4b's 25 m/s² gravity displaces a grounded body by: about
    /// 6.9 mm, which is a thirty-sixth of a voxel. Small on purpose — resting contact has to
    /// survive being nudged into the floor every single step.
    /// </summary>
    private const float GravityStep = -0.00694f;

    private const float Voxel = VoxelConstants.VoxelSize;

    // ---------------------------------------------------------------- resting and the floor

    [Fact]
    public void AFallingBoxComesToRestOnTopOfTheFloorAndSaysItIsGrounded()
    {
        var world = new TestWorld { Background = v => v.Y <= 3 ? Dirt : Air };
        var box = Aabb.FromFeet(new Vector3(2.0f, 3.0f, 2.0f), PlayerWidth, PlayerHeight);

        MoveResult result = Step(world, box, new Vector3(0f, -2.5f, 0f));

        // Voxel 3 is the top floor layer, spanning [0.75, 1.0). The box rests on 1.0 m — and
        // it is asked to fall further than that, so the substeps after contact have to re-snap
        // to the same place rather than pushing on through.
        Assert.True(result.Grounded);
        Assert.InRange(result.Box.Min.Y, 1.0f, 1.0f + (VoxelCollider.ContactEpsilonMetres * 2f));
        Assert.Equal(13, result.Substeps);
    }

    [Fact]
    public void StandingStillForTenThousandStepsDoesNotDriftAndGroundedNeverFlickers()
    {
        // The bug that survives into shipped games is not tunnelling — it is the one-frame
        // oscillation: the resolve places the box on a face, the next step's test finds it
        // touching, pushes it out again, and the camera vibrates by a millimetre forever while
        // `grounded` flickers and jump misfires. This is the test for that, and it is not a
        // fuzz: it is standing still and asserting nothing whatsoever happens.
        var world = new TestWorld { Background = v => v.Y <= 3 ? Dirt : Air };
        var box = Aabb.FromFeet(new Vector3(2.0f, 1.0f, 2.0f), PlayerWidth, PlayerHeight);
        var gravity = new Vector3(0f, GravityStep, 0f);

        MoveResult settled = Step(world, box, gravity);
        Assert.True(settled.Grounded);

        // The one-time settle: the resolve stops an epsilon short of contact, so a box placed
        // exactly on the plane drops to plane + epsilon and stays there. A tenth of a
        // millimetre, once — and then bitwise nothing. The bound is two epsilons rather than
        // one because `1.0f + 1e-4f` does not round to exactly 1.0001; what is being asserted
        // is that the settle is a fraction of a millimetre, not that it is exactly the epsilon.
        Assert.InRange(
            MathF.Abs(settled.Box.Min.Y - box.Min.Y),
            0f,
            VoxelCollider.ContactEpsilonMetres * 2f);

        Aabb current = settled.Box;
        for (int i = 0; i < 10_000; i++)
        {
            MoveResult result = Step(world, current, gravity);

            AssertSameBits(settled.Box, result.Box, $"drifted on step {i}");
            Assert.True(result.Grounded, $"ground state flickered on step {i}");

            current = result.Box;
        }
    }

    [Fact]
    public void AnAxisWithNoDisplacementIsLeftBitwiseAloneEvenAsAnotherAxisResolves()
    {
        // Why the resolve returns early on a zero delta rather than translating by zero and
        // carrying on: adding 0.0f is exact for every float except -0.0f, and "except" is how
        // a drift test starts passing for the wrong reason. The horizontal coordinates here
        // are deliberately values with no exact binary form.
        var world = new TestWorld { Background = v => v.Y <= 3 ? Dirt : Air };
        var box = Aabb.FromFeet(new Vector3(1.3f, 2.0f, -7.1f), PlayerWidth, PlayerHeight);

        MoveResult result = Step(world, box, new Vector3(0f, -1.5f, 0f));

        Assert.Equal(BitsOf(box.Min.X), BitsOf(result.Box.Min.X));
        Assert.Equal(BitsOf(box.Min.Z), BitsOf(result.Box.Min.Z));
        Assert.True(result.Grounded);
    }

    // -------------------------------------------------------------------------- the six faces

    [Theory]
    [InlineData(1, 0, 0, BlockedFaces.PositiveX)]
    [InlineData(-1, 0, 0, BlockedFaces.NegativeX)]
    [InlineData(0, 1, 0, BlockedFaces.PositiveY)]
    [InlineData(0, -1, 0, BlockedFaces.NegativeY)]
    [InlineData(0, 0, 1, BlockedFaces.PositiveZ)]
    [InlineData(0, 0, -1, BlockedFaces.NegativeZ)]
    public void DrivenIntoAnyOfTheSixWallsTheBoxStopsAtItAndReportsThatFace(
        int dx, int dy, int dz, BlockedFaces expected)
    {
        // All six, because the two directions on an axis are different code paths — one places
        // the minimum face and the other derives it from the size — and a sign error shows up
        // in exactly one of them.
        var world = SealedRoom();
        var box = Aabb.FromFeet(new Vector3(2.5f, 2.5f, 2.5f), PlayerWidth, PlayerHeight);

        MoveResult result = Step(world, box, new Vector3(dx, dy, dz) * 10f);

        Assert.Equal(expected, result.Blocked);

        float low = Component(result.Box.Min, dx, dy, dz);
        float high = Component(result.Box.Max, dx, dy, dz);

        if (dx + dy + dz > 0)
        {
            Assert.InRange(high, RoomHighMetres - (VoxelCollider.ContactEpsilonMetres * 2f), RoomHighMetres);
        }
        else
        {
            Assert.InRange(low, RoomLowMetres, RoomLowMetres + (VoxelCollider.ContactEpsilonMetres * 2f));
        }
    }

    [Fact]
    public void TheBoxWedgesInASlotOneVoxelWiderThanItselfAndStillSlidesAlongIt()
    {
        // A 3-voxel slot (0.75 m) around a 2-voxel box (0.5 m): 0.125 m of slack each side.
        // Not an accident — it is the bore M4b says the weakest tool leaves, which is the
        // coupling between the player's width and `hand_pick`'s bite.
        var world = new TestWorld { Background = v => v.X >= 8 && v.X <= 10 ? Air : Stone };
        var box = new Aabb(
            new Vector3(2.125f, 0f, 0f), new Vector3(PlayerWidth, PlayerHeight, PlayerWidth));

        MoveResult pushed = Step(world, box, new Vector3(1.0f, 0f, 0f));
        Assert.Equal(BlockedFaces.PositiveX, pushed.Blocked);
        Assert.InRange(pushed.Box.Max.X, 2.75f - (VoxelCollider.ContactEpsilonMetres * 2f), 2.75f);

        MoveResult pulled = Step(world, pushed.Box, new Vector3(-1.0f, 0f, 0f));
        Assert.Equal(BlockedFaces.NegativeX, pulled.Blocked);
        Assert.InRange(pulled.Box.Min.X, 2.0f, 2.0f + (VoxelCollider.ContactEpsilonMetres * 2f));

        // And the slot is a corridor, not a cell: nothing obstructs Z.
        MoveResult along = Step(world, pulled.Box, new Vector3(0f, 0f, 5.0f));
        Assert.Equal(BlockedFaces.None, along.Blocked);
        Assert.Equal(pulled.Box.Min.Z + 5.0f, along.Box.Min.Z, 4);
    }

    [Fact]
    public void ABoxDroppedStraightDownAShaftExactlyItsOwnWidthReachesTheBottom()
    {
        // The knife edge named in VoxelCollider's remarks, pinned rather than avoided. A
        // 2-voxel shaft is exactly 0.5 m and so is the box, so there is no room for the
        // contact epsilon on both sides at once — but a straight drop never resolves X or Z at
        // all, so the descent is exact. What must not happen is passing through the shaft
        // wall, and the postcondition in Step() is what checks that.
        var world = new TestWorld
        {
            Background = v => v.Y <= 3 ? Dirt
                : v.X is 8 or 9 && v.Z is 8 or 9 ? Air
                : Stone,
        };
        var box = new Aabb(
            new Vector3(2.0f, 3.0f, 2.0f), new Vector3(PlayerWidth, PlayerHeight, PlayerWidth));

        Aabb current = box;
        for (int i = 0; i < 60; i++)
        {
            current = Step(world, current, new Vector3(0f, -0.15f, 0f)).Box;
        }

        Assert.InRange(current.Min.Y, 1.0f, 1.0f + (VoxelCollider.ContactEpsilonMetres * 2f));
        Assert.Equal(BitsOf(box.Min.X), BitsOf(current.Min.X));
        Assert.Equal(BitsOf(box.Min.Z), BitsOf(current.Min.Z));
    }

    // ------------------------------------------------------------------------ the corner snag

    [Fact]
    public void SlidingDiagonallyAlongAFlatWallTravelsTheWholeTangentialDistance()
    {
        // The bug axis separation exists to prevent. A combined sweep finds the earliest impact
        // across all three axes and stops there, so every voxel boundary along a flat wall is a
        // separate impact and the box crawls — or stops dead. This test fails loudly if someone
        // ever "simplifies" the resolve back into one pass, and the margin is enormous: the
        // subject travels 10 m tangentially where a sweep travels a fraction of one voxel.
        var world = new TestWorld
        {
            Background = v => v.X >= 8 ? Stone : v.Y <= 3 ? Dirt : Air,
        };
        var box = Aabb.FromFeet(new Vector3(1.0f, 1.0f, 0f), PlayerWidth, PlayerHeight);

        const int Steps = 200;
        const float Tangential = 0.05f;

        Aabb current = box;
        for (int i = 0; i < Steps; i++)
        {
            MoveResult result = Step(
                world, current, new Vector3(Tangential, GravityStep, Tangential));

            Assert.True(result.Grounded, $"left the floor on step {i}");
            current = result.Box;
        }

        // Into the wall on X, and completely unimpeded on Z.
        Assert.InRange(current.Max.X, 2.0f - (VoxelCollider.ContactEpsilonMetres * 2f), 2.0f);
        Assert.InRange(
            current.Min.Z - box.Min.Z, (Steps * Tangential) - 0.01f, (Steps * Tangential) + 0.01f);
    }

    [Fact]
    public void ABoxThatStartsInsideAWallIsPushedOutOfItRatherThanSinkingFurtherIn()
    {
        // Deliberately violates the precondition every other test here maintains, which is why
        // it calls Move directly instead of going through Step(): the postcondition cannot hold
        // for a box that began overlapping.
        //
        // This is what makes ResolveAxis take its voxel layer from the *moved* box rather than
        // from "the layer one past where the box already was". Both are the same layer while
        // the box is outside the wall, so nothing else in this file can tell them apart — but
        // once the box is inside, the second one finds no *new* layer ahead and waves the move
        // straight through. Watched to fail: it walks out the far side of the wall in about ten
        // steps.
        //
        // M4a does not owe a full unstick — that is M4c's rule, and this only clears the layer
        // the leading face is in. What it owes is that going deeper is never an option.
        var world = new TestWorld { Background = v => v.X >= 8 ? Stone : Air };

        // Straddling the wall face at 2.0 m: 0.4 m of a 0.5 m box is already inside rock.
        var start = new Aabb(
            new Vector3(1.9f, 0f, 0f), new Vector3(PlayerWidth, PlayerHeight, PlayerWidth));
        Assert.True(VoxelCollider.OverlapsSolid(world, Table, start));

        Aabb current = start;
        for (int i = 0; i < 50; i++)
        {
            current = VoxelCollider.Move(world, Table, current, new Vector3(0.1f, 0f, 0f)).Box;

            Assert.True(
                current.Max.X <= start.Max.X,
                $"sank further into the wall on step {i}: {current}");
            Assert.True(
                current.Min.X < 2.0f,
                $"ended up entirely inside the wall on step {i}: {current}");
        }
    }

    // ------------------------------------------------------------------------ unloaded is solid

    [Fact]
    public void AnUnloadedChunkStopsTheBoxAndTheBoxNeverEntersIt()
    {
        // The decision the whole milestone turns on. TryGetMaterial sets the material to air
        // *and* returns false, so a collider that ignores the return value reads the edge of
        // the loaded world as a hole in the floor — and the player accelerates away from every
        // chunk that could ever have stopped them.
        //
        // There is no floor here at all. The only thing below the box is a chunk that has not
        // loaded, whose top face is y = 0.
        var world = new TestWorld();
        world.Unload(new ChunkPos(0, -1, 0));

        var box = Aabb.FromFeet(new Vector3(1.0f, 2.0f, 1.0f), PlayerWidth, PlayerHeight);

        Aabb current = box;
        for (int i = 0; i < 40; i++)
        {
            MoveResult result = Step(world, current, new Vector3(0f, -0.2f, 0f));
            current = result.Box;

            Assert.True(current.Min.Y >= 0f, $"entered the unloaded chunk on step {i}: {current}");
        }

        Assert.InRange(current.Min.Y, 0f, VoxelCollider.ContactEpsilonMetres * 2f);
    }

    [Fact]
    public void SolidityIsAPredicateSoAFluidFloorIsNotAFloor()
    {
        // `id != air && !IsFluid(id)` rather than `id != air`. Correct today because fluids do
        // not exist, and still correct at M8 when they do — with wading falling out as the
        // default rather than as a special case bolted on afterwards.
        var water = new TestWorld { Background = v => v.Y <= 3 ? Water : Air };
        var dirt = new TestWorld { Background = v => v.Y <= 3 ? Dirt : Air };
        var box = Aabb.FromFeet(new Vector3(2.0f, 3.0f, 2.0f), PlayerWidth, PlayerHeight);

        MoveResult throughWater = Step(water, box, new Vector3(0f, -2.5f, 0f));
        MoveResult ontoDirt = Step(dirt, box, new Vector3(0f, -2.5f, 0f));

        // The same fall, the same depth, and the only difference is a flag on the material:
        // through the water to 0.5 m, which is below the surface it would have stood on.
        Assert.False(throughWater.Grounded);
        Assert.Equal(box.Min.Y - 2.5f, throughWater.Box.Min.Y, 4);
        Assert.True(ontoDirt.Grounded);
        Assert.InRange(ontoDirt.Box.Min.Y, 1.0f, 1.0f + (VoxelCollider.ContactEpsilonMetres * 2f));
    }

    // ----------------------------------------------------------------------------- the fuzz

    [Fact]
    public void ThousandsOfWildDisplacementsNeverGetTheBoxOutOfASealedRoom()
    {
        // The oracle with no reference implementation, no sampling and no tolerance: the box
        // is inside a closed shell of solid voxels, or it is not.
        //
        // Watched to fail before being trusted, per the rule M3 earned three times. Raising
        // VoxelCollider.MaxSubstepMetres to 1.0 — one substep instead of the cap — escapes on
        // the first iteration, because the resolve tests only the voxel layer the leading face
        // lands in and a step that long lands it outside the room entirely. That is what makes
        // the cap load-bearing rather than a tuning knob, and it is why the magnitudes below
        // go far past the 0.67 m a 40 m/s terminal velocity produces at 60 Hz.
        var world = SealedRoom();
        var random = new Random(2026_08_29);
        var box = Aabb.FromFeet(new Vector3(2.5f, 2.5f, 2.5f), PlayerWidth, PlayerHeight);

        for (int i = 0; i < 1500; i++)
        {
            // Magnitudes from a nudge to 8 m in a single step — 480 m/s, twelve times terminal
            // velocity — so most iterations substep heavily and a few do not substep at all.
            float scale = MathF.Pow(10f, (float)random.NextDouble() * 3f) * 0.008f;
            var displacement = new Vector3(
                ((float)random.NextDouble() - 0.5f) * scale,
                ((float)random.NextDouble() - 0.5f) * scale,
                ((float)random.NextDouble() - 0.5f) * scale);

            box = Step(world, box, displacement).Box;

            Assert.InRange(box.Min.X, RoomLowMetres, RoomHighMetres);
            Assert.InRange(box.Min.Y, RoomLowMetres, RoomHighMetres);
            Assert.InRange(box.Min.Z, RoomLowMetres, RoomHighMetres);
            Assert.InRange(box.Max.X, RoomLowMetres, RoomHighMetres);
            Assert.InRange(box.Max.Y, RoomLowMetres, RoomHighMetres);
            Assert.InRange(box.Max.Z, RoomLowMetres, RoomHighMetres);
        }
    }

    [Fact]
    public void AnIdenticalInputSequenceReplaysToABitwiseIdenticalTrajectory()
    {
        // Cheap now, and it is the whole of what M4 owes multiplayer: a fixed-step solve in
        // Core that two machines can run and agree about.
        List<Aabb> first = Trajectory();
        List<Aabb> second = Trajectory();

        Assert.Equal(first.Count, second.Count);
        for (int i = 0; i < first.Count; i++)
        {
            AssertSameBits(first[i], second[i], $"diverged at step {i}");
        }

        static List<Aabb> Trajectory()
        {
            var world = SealedRoom();
            var random = new Random(4242);
            var box = Aabb.FromFeet(new Vector3(2.5f, 3.0f, 2.5f), PlayerWidth, PlayerHeight);
            var trail = new List<Aabb>();

            for (int i = 0; i < 500; i++)
            {
                box = VoxelCollider.Move(
                    world,
                    Table,
                    box,
                    new Vector3(
                        ((float)random.NextDouble() - 0.5f) * 0.9f,
                        ((float)random.NextDouble() - 0.55f) * 0.9f,
                        ((float)random.NextDouble() - 0.5f) * 0.9f)).Box;

                trail.Add(box);
            }

            return trail;
        }
    }

    // ------------------------------------------------------------------------ cost and limits

    [Fact]
    public void AWalkingStepCostsFarFewerVoxelReadsThanM4aGuessed()
    {
        // M4a guessed ~400 TryGetMaterial calls per step, from 32 voxels of box × 3 axes × 4
        // substeps. Measured, this step costs **41**: a resolve reads one voxel *layer* rather
        // than the whole box, and walking is one substep. The layers are not the 16 + 4 + 16
        // the box's nominal 2 × 8 × 2 suggests, because a 0.5 m box that is not grid-aligned
        // straddles *three* voxel columns rather than two — 16 + 1 + 24 here, and 63 in the
        // worst alignment. Four substeps of that is 252 against the guessed 400.
        //
        // Asserted loosely, because this is a regression detector for someone widening the scan
        // back out to the whole box, not a measurement to defend to the digit.
        var world = new TestWorld { Background = v => v.Y <= 3 ? Dirt : Air };
        var box = Aabb.FromFeet(new Vector3(2.0f, 1.0f, 2.0f), PlayerWidth, PlayerHeight);

        MoveResult result = VoxelCollider.Move(
            world, Table, box, new Vector3(0.075f, GravityStep, 0.075f));

        Assert.Equal(1, result.Substeps);
        Assert.InRange(result.VoxelQueries, 1, 64);
    }

    [Theory]
    [InlineData(0f, 0f, 0f, 1)]
    [InlineData(0.2f, 0f, 0f, 1)]
    [InlineData(0.25f, 0f, 0f, 2)]
    [InlineData(0f, -0.667f, 0f, 4)]
    [InlineData(0.2f, 0.2f, 0.2f, 2)]
    public void SubstepsAreCappedOnTheDisplacementsMagnitudeNotPerAxis(
        float dx, float dy, float dz, int expected)
    {
        // The last case is the one worth having: 0.2 m on each of three axes is 0.346 m of
        // travel, so a per-axis cap would wave it through as a single substep and it would step
        // over a voxel diagonally.
        Assert.Equal(expected, VoxelCollider.SubstepsFor(new Vector3(dx, dy, dz)));
    }

    [Fact]
    public void ADisplacementThatWouldNeedMoreSubstepsThanTheBoundIsRefused()
    {
        // A velocity that has blown up, not a fast body. The alternative to throwing is a loop
        // long enough to look like a freeze — the same failure ToolDefinition.MaxRadiusMetres
        // bounds for a cubic carve scan.
        var world = SealedRoom();
        var box = Aabb.FromFeet(new Vector3(2.5f, 2.5f, 2.5f), PlayerWidth, PlayerHeight);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => VoxelCollider.Move(world, Table, box, new Vector3(0f, -1e9f, 0f)));
    }

    [Theory]
    [InlineData(float.NaN, 0f, 0f)]
    [InlineData(0f, float.PositiveInfinity, 0f)]
    [InlineData(0f, 0f, float.NegativeInfinity)]
    public void ANonFiniteDisplacementIsRefusedRatherThanPuttingTheBoxNowhere(
        float dx, float dy, float dz)
    {
        var world = SealedRoom();
        var box = Aabb.FromFeet(new Vector3(2.5f, 2.5f, 2.5f), PlayerWidth, PlayerHeight);

        Assert.Throws<ArgumentException>(
            () => VoxelCollider.Move(world, Table, box, new Vector3(dx, dy, dz)));
    }

    [Theory]
    [InlineData(0f, 1f, 1f)]
    [InlineData(1f, -1f, 1f)]
    [InlineData(1f, 1f, float.NaN)]
    public void ADegenerateBoxIsRefusedAtConstruction(float w, float h, float d)
    {
        // A zero-sized box overlaps no voxel, so it would move through solid rock reporting no
        // collision. That is a silent wrong answer, which is worse than a loud one.
        Assert.Throws<ArgumentException>(
            () => new Aabb(Vector3.Zero, new Vector3(w, h, d)));
    }

    // -------------------------------------------------------------------------------- geometry

    [Fact]
    public void AFaceExactlyOnAVoxelPlaneTouchesThatVoxelWithoutOverlappingIt()
    {
        // The half-open rule, and it is not pedantry: it is what lets a player walk. A resting
        // box's minimum face sits exactly on the floor's top plane, so the floor voxel must not
        // be in VoxelBounds — make the test inclusive at both ends and every horizontal move
        // collides with the ground the box is standing on.
        //
        // **This test looks trivial and is one of only three that catch the rule breaking.**
        // Swapping LastIndex's `ceil − 1` for a `floor` fails exactly this, the exact-width
        // shaft, and the box that starts inside a wall — the three situations where a face
        // lands exactly on a voxel plane. Not one of the ordinary walking-and-falling tests
        // notices, because the contact epsilon stops every resolved face a tenth of a
        // millimetre off the plane, so in ordinary motion a face is never exactly on one and
        // the two forms agree. The epsilon masks the rule it is a second belt for. Deleting
        // this as a restatement of the type's remarks would take real coverage with it, which
        // is why the coverage is written down here rather than left to be rediscovered.
        var box = new Aabb(new Vector3(1.0f, 1.0f, 1.0f), new Vector3(0.5f, 0.5f, 0.5f));
        (VoxelPos min, VoxelPos max) = box.VoxelBounds;

        Assert.Equal(new VoxelPos(4, 4, 4), min);
        Assert.Equal(new VoxelPos(5, 5, 5), max);
    }

    [Fact]
    public void VoxelBoundsAreCorrectBelowTheOrigin()
    {
        // Floor, never truncation. A box straddling zero must not report a range that skips the
        // voxel on the negative side of it.
        var box = new Aabb(new Vector3(-0.3f, -0.3f, -0.3f), new Vector3(0.5f, 0.5f, 0.5f));
        (VoxelPos min, VoxelPos max) = box.VoxelBounds;

        Assert.Equal(new VoxelPos(-2, -2, -2), min);
        Assert.Equal(new VoxelPos(0, 0, 0), max);
    }

    [Fact]
    public void FeetCentreRoundTripsThroughFromFeet()
    {
        var feet = new Vector3(-3.25f, 7.5f, 0.125f);
        Aabb box = Aabb.FromFeet(feet, PlayerWidth, PlayerHeight);

        Assert.Equal(feet, box.FeetCentre);
        Assert.Equal(new Vector3(PlayerWidth, PlayerHeight, PlayerWidth), box.Size);
    }

    // --------------------------------------------------------------------------------- helpers

    /// <summary>
    /// One move, with the postcondition every motion test shares asserted on the way out.
    /// </summary>
    private static MoveResult Step(TestWorld world, Aabb box, Vector3 displacement)
    {
        MoveResult result = VoxelCollider.Move(world, Table, box, displacement);

        Assert.False(
            VoxelCollider.OverlapsSolid(world, Table, result.Box),
            $"ended a step overlapping solid: {result}");

        return result;
    }

    /// <summary>Interior voxels 1..20 on every axis are air; everything outside is stone.</summary>
    private static TestWorld SealedRoom() => new()
    {
        Background = v => v.X >= RoomMinVoxel && v.X <= RoomMaxVoxel
            && v.Y >= RoomMinVoxel && v.Y <= RoomMaxVoxel
            && v.Z >= RoomMinVoxel && v.Z <= RoomMaxVoxel
                ? Air
                : Stone,
    };

    private const int RoomMinVoxel = 1;
    private const int RoomMaxVoxel = 20;
    private const float RoomLowMetres = RoomMinVoxel * Voxel;
    private const float RoomHighMetres = (RoomMaxVoxel + 1) * Voxel;

    private static float Component(Vector3 vector, int dx, int dy, int dz) =>
        dx != 0 ? vector.X : dy != 0 ? vector.Y : vector.Z;

    private static int BitsOf(float value) => BitConverter.SingleToInt32Bits(value);

    private static void AssertSameBits(Aabb expected, Aabb actual, string because)
    {
        Assert.True(
            BitsOf(expected.Min.X) == BitsOf(actual.Min.X)
            && BitsOf(expected.Min.Y) == BitsOf(actual.Min.Y)
            && BitsOf(expected.Min.Z) == BitsOf(actual.Min.Z)
            && BitsOf(expected.Size.X) == BitsOf(actual.Size.X)
            && BitsOf(expected.Size.Y) == BitsOf(actual.Size.Y)
            && BitsOf(expected.Size.Z) == BitsOf(actual.Size.Z),
            $"{because}: expected {expected}, got {actual}");
    }

    /// <summary>
    /// A world that is air unless <see cref="Background"/> says otherwise, with named chunks
    /// that answer "not loaded".
    /// </summary>
    /// <remarks>
    /// <see cref="Background"/> is a predicate rather than a filled region because a floor that
    /// has to extend ten metres in every direction is 65,000 dictionary entries, and the tests
    /// that need one are the long ones.
    /// </remarks>
    private sealed class TestWorld : IVoxelWorld
    {
        private readonly Dictionary<VoxelPos, int> _voxels = [];
        private readonly HashSet<ChunkPos> _unloaded = [];

        public Func<VoxelPos, int> Background { get; init; } = _ => Air;

        public void Set(VoxelPos voxel, int material) => _voxels[voxel] = material;

        public void Unload(ChunkPos chunk) => _unloaded.Add(chunk);

        public bool TryGetMaterial(VoxelPos voxel, out int materialId)
        {
            if (_unloaded.Contains(voxel.Chunk))
            {
                // Air *and* false, exactly as ChunkStreamer does it. A test double that
                // returned something obviously wrong here would let a collider that ignores
                // the return value pass.
                materialId = Air;
                return false;
            }

            materialId = _voxels.TryGetValue(voxel, out int material) ? material : Background(voxel);
            return true;
        }
    }
}
