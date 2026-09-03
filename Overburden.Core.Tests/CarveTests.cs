using System.Numerics;
using Overburden.Core.Content;
using Overburden.Core.Mining;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// The sphere carve, against a dictionary of voxels rather than a streaming pipeline. The
/// interesting failures here are geometric — an off-by-one in the bounding box, a sign error
/// in the negative octant, a hardness comparison the wrong way round — and none of them need
/// a thread or a mesher to show up.
/// </summary>
public class CarveTests
{
    private const int Air = MaterialIds.AirNumericId;
    private const int Dirt = 1;
    private const int Stone = 2;
    private const int Bedrock = 3;

    /// <summary>The base game's hardnesses, plus something nothing at M3 can take.</summary>
    private static MaterialTable Materials() => MaterialTable.ForTesting(
        //             air    dirt  stone bedrock
        isOpaque: [false, true, true, true],
        isFluid: [false, false, false, false],
        textureLayer: [0, 1, 2, 3],
        hardness: [0f, 0.8f, 3.0f, 50f]);

    [Theory]
    [InlineData(0f, 0f, 0f, 0.5f)]
    [InlineData(1.37f, -2.11f, 0.6f, 0.5f)]
    [InlineData(-4.3f, -1.9f, -7.7f, 0.75f)]
    [InlineData(3.3f, 0.15f, -0.05f, 0.26f)]
    [InlineData(-0.03f, -0.03f, -0.03f, 1.25f)]
    public void TheSphereRemovesExactlyTheVoxelsWhoseCentresLieInside(
        float cx, float cy, float cz, float radius)
    {
        // Negative octants get their own cases on purpose. A floor or a sign error in the
        // bounding-box derivation is invisible where every coordinate is positive, which is
        // where hand-written cases tend to live. Watched to fail: narrowing either end of
        // Carve.Bounds by one voxel breaks this in milliseconds, negative cases first.
        var world = new FakeEditor { DefaultMaterial = Dirt };
        var centre = new Vector3(cx, cy, cz);

        CarveResult result = Carve.Sphere(world, Materials(), centre, radius, 1.0f);
        Accounted(result);

        Assert.Equal(CentresWithin(centre, radius), world.WrittenVoxels);
        Assert.Equal(world.WrittenVoxels.Count, result.Removed);
        Assert.Equal(result.Candidates, result.Removed);

        // One write per voxel. A bounding box that visited a voxel twice would still produce
        // the right set and the wrong count.
        Assert.Equal(world.Writes.Count, world.WrittenVoxels.Count);

        Assert.Equal(0, result.TooHard);
        Assert.True(result.Complete);
    }

    [Fact]
    public void AVoxelCentreExactlyOnTheRadiusIsInsideAndTheNextOneOutIsNot()
    {
        // The `<=` at the boundary, on values that are exact in binary so the assertion is
        // about the comparison rather than about float luck. The fuzz above is deliberately
        // blind to this: random centres essentially never produce a tie, so flipping `<=` to
        // `<` would leave every hole one shell narrower and the fuzz green.
        var world = new FakeEditor { DefaultMaterial = Dirt };
        (float x, float y, float z) = new VoxelPos(0, 0, 0).CentreMetres;

        Carve.Sphere(world, Materials(), new Vector3(x, y, z), 0.5f, 1.0f);

        // Voxel 2's centre is 0.625 m and voxel -2's is -0.375 m: both exactly 0.5 m from
        // the swing's 0.125 m, and both exact in binary, so this is about the comparison
        // rather than about float luck.
        Assert.Contains(new VoxelPos(2, 0, 0), world.WrittenVoxels);
        Assert.Contains(new VoxelPos(-2, 0, 0), world.WrittenVoxels);
        Assert.DoesNotContain(new VoxelPos(3, 0, 0), world.WrittenVoxels);
        Assert.DoesNotContain(new VoxelPos(-3, 0, 0), world.WrittenVoxels);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(7, -13, 29)]
    [InlineData(-1, -1, -1)]
    [InlineData(-33, 64, -100)]
    public void ASwingAimedAtAVoxelsCentreTakesThatVoxel(int x, int y, int z)
    {
        // Pins Carve's geometry against VoxelPos.CentreMetres at arbitrary, mostly negative
        // coordinates. The radius is a tenth of a voxel, so the aimed-at voxel is the only
        // one whose centre can possibly be inside — its nearest neighbour's is 0.25 m away.
        var world = new FakeEditor { DefaultMaterial = Dirt };
        (float mx, float my, float mz) = new VoxelPos(x, y, z).CentreMetres;

        CarveResult result = Carve.Sphere(
            world, Materials(), new Vector3(mx, my, mz), 0.025f, 1.0f);
        Accounted(result);

        Assert.Equal(1, result.Removed);
        Assert.Equal(new HashSet<VoxelPos> { new(x, y, z) }, world.WrittenVoxels);
    }

    [Fact]
    public void ASwingOfNoRadiusTakesNothingEvenAimedExactlyAtAVoxelCentre()
    {
        // Zero is a clean no-op, not a knife edge. Without the early return the test reads
        // "distance 0 ≤ 0" and this swing takes one voxel while a swing a nanometre away
        // takes none — and ToolDefinition.Unknown is a radius of exactly zero.
        var world = new FakeEditor { DefaultMaterial = Dirt };
        (float x, float y, float z) = new VoxelPos(0, 0, 0).CentreMetres;

        CarveResult result = Carve.Sphere(world, Materials(), new Vector3(x, y, z), 0f, 1.0f);
        Accounted(result);

        Assert.Equal(0, result.Candidates);
        Assert.Empty(world.Writes);
    }

    [Fact]
    public void AToolTooWeakForStoneCarvesTheDirtOutFromAroundIt()
    {
        // The mechanic, and the reason hardness is per voxel rather than per swing. The seam
        // is left standing proud of the hole, and nothing has to say "you cannot mine this".
        var world = new FakeEditor { DefaultMaterial = Dirt };
        var centre = new Vector3(0f, 0f, 0f);
        const float radius = 0.75f;

        // A seam one voxel thick through the middle of the swing, laid out without reference
        // to the sphere so the fixture does not assume the answer.
        for (int y = -8; y <= 8; y++)
        for (int z = -8; z <= 8; z++)
        {
            world.Set(new VoxelPos(0, y, z), Stone);
        }

        var inside = CentresWithin(centre, radius);
        int seamInside = inside.Count(v => v.X == 0);

        CarveResult result = Carve.Sphere(world, Materials(), centre, radius, 1.0f);
        Accounted(result);

        // Both guards matter: a seam that fell outside the sphere would make the milestone's
        // headline mechanic pass vacuously.
        Assert.True(seamInside > 0, "the fixture must put stone inside the sphere");
        Assert.True(inside.Count - seamInside > 0, "the fixture must put dirt inside it too");

        Assert.Equal(inside.Count - seamInside, result.Removed);
        Assert.Equal(seamInside, result.TooHard);

        Assert.All(world.Writes, write => Assert.NotEqual(0, write.Voxel.X));
        Assert.All(inside.Where(v => v.X == 0), v => Assert.Equal(Stone, world[v]));
    }

    [Fact]
    public void AMaterialExactlyAsHardAsTheThresholdComesAway()
    {
        // "At or below", not "below". A tier written to mine stone whose threshold is stone's
        // hardness must mine stone, or every number in every mod's JSON is a lie by an epsilon.
        var world = new FakeEditor { DefaultMaterial = Stone };

        CarveResult result = Carve.Sphere(world, Materials(), Vector3.Zero, 0.5f, 3.0f);
        Accounted(result);

        Assert.True(result.Removed > 0);
        Assert.Equal(0, result.TooHard);
    }

    [Fact]
    public void AToolThatCanTakeNothingRemovesNothingAndSaysHowMuchItLeft()
    {
        var world = new FakeEditor { DefaultMaterial = Bedrock };

        CarveResult result = Carve.Sphere(world, Materials(), Vector3.Zero, 0.5f, 3.5f);
        Accounted(result);

        Assert.Equal(0, result.Removed);
        Assert.Equal(CentresWithin(Vector3.Zero, 0.5f).Count, result.TooHard);
        Assert.Empty(world.Writes);

        // Nothing was reached-and-failed; it was reached and refused. The two are different
        // reports and a swing at a wall must not look like a swing off the edge of the world.
        Assert.True(result.Complete);
    }

    [Fact]
    public void ASwingInOpenSkyCountsAirWithoutRemovingOrWritingAnything()
    {
        // Air's hardness is 0, so it passes every threshold there is, and a sphere centred on
        // a surface is roughly half air. Counting it as removed would make every swing report
        // about double what it took — and Removed is the number M6 reads as a volume.
        var world = new FakeEditor { DefaultMaterial = Air };

        CarveResult result = Carve.Sphere(world, Materials(), Vector3.Zero, 0.5f, 1.0f);
        Accounted(result);

        Assert.True(result.Candidates > 0);
        Assert.Equal(result.Candidates, result.AlreadyAir);
        Assert.Equal(0, result.Removed);
        Assert.Empty(world.Writes);
    }

    [Fact]
    public void CarvingOnlyEverWritesAirAndNeverTouchesAVoxelThatIsAlreadyAir()
    {
        // Structurally guaranteed today — there is no material parameter, and air is skipped
        // by numeric identity. This is what keeps it guaranteed the day someone adds a
        // "carve to rubble" tier.
        var world = new FakeEditor { DefaultMaterial = Dirt };
        var alreadyAir = new HashSet<VoxelPos>();

        for (int y = 0; y <= 4; y++)
        {
            var voxel = new VoxelPos(0, y, 0);
            world.Set(voxel, Air);
            alreadyAir.Add(voxel);
        }

        CarveResult result = Carve.Sphere(world, Materials(), Vector3.Zero, 1.0f, 1.0f);
        Accounted(result);

        Assert.NotEmpty(world.Writes);
        Assert.All(world.Writes, write => Assert.Equal(Air, write.Material));
        Assert.All(world.Writes, write => Assert.DoesNotContain(write.Voxel, alreadyAir));

        // And the fixture really did put empty voxels inside the sphere.
        var inside = CentresWithin(Vector3.Zero, 1.0f);
        Assert.Equal(alreadyAir.Count(inside.Contains), result.AlreadyAir);
        Assert.True(result.AlreadyAir > 0);
    }

    [Fact]
    public void HundredsOfSwingsOnlyEverTurnMaterialIntoAirAndNeverTheReverse()
    {
        // The caves-only-remove invariant from M2b, one layer up, and a genuinely different
        // computation from the carve — which never reads the world twice. It is what would
        // catch a mis-indexed write or a wrong replacement id, since every other test here
        // only ever looks at the voxels the carve told it about.
        var random = new Random(20260829);
        var world = new FakeEditor { DefaultMaterial = Air };
        var region = new List<VoxelPos>();

        for (int x = -8; x < 8; x++)
        for (int y = -8; y < 8; y++)
        for (int z = -8; z < 8; z++)
        {
            var voxel = new VoxelPos(x, y, z);
            region.Add(voxel);
            world.Set(voxel, random.Next(4));
        }

        Dictionary<int, int> before = Census(world, region);
        int removed = 0;

        for (int swing = 0; swing < 40; swing++)
        {
            var centre = new Vector3(
                (float)((random.NextDouble() - 0.5) * 3.0),
                (float)((random.NextDouble() - 0.5) * 3.0),
                (float)((random.NextDouble() - 0.5) * 3.0));

            CarveResult result = Carve.Sphere(world, Materials(), centre, 0.5f, 3.0f);
            Accounted(result);
            removed += result.Removed;
        }

        Dictionary<int, int> after = Census(world, region);

        Assert.True(removed > 0);
        Assert.Equal(region.Count, after.Values.Sum());
        Assert.True(after[Air] > before[Air]);
        Assert.Equal(removed, after[Air] - before[Air]);
        Assert.True(after[Dirt] < before[Dirt]);
        Assert.True(after[Stone] < before[Stone]);

        // The one material no tool here can take is untouched, and no material was created.
        Assert.Equal(before[Bedrock], after[Bedrock]);
        Assert.All(new[] { Dirt, Stone, Bedrock }, id => Assert.True(after[id] <= before[id]));
    }

    [Fact]
    public void ACarveOnAChunkCornerWritesIntoAllEightChunks()
    {
        // Where a carve is most likely to be subtly wrong, and where mining actually happens.
        // The origin is the corner eight chunks meet at, so a radius of half a metre lands in
        // every one of them.
        var world = new FakeEditor { DefaultMaterial = Dirt };

        Carve.Sphere(world, Materials(), Vector3.Zero, 0.5f, 1.0f);

        var chunks = world.Writes.Select(write => write.Voxel.Chunk).ToHashSet();
        Assert.Equal(8, chunks.Count);

        for (int dz = -1; dz <= 0; dz++)
        for (int dy = -1; dy <= 0; dy++)
        for (int dx = -1; dx <= 0; dx++)
        {
            Assert.Contains(new ChunkPos(dx, dy, dz), chunks);
        }
    }

    [Fact]
    public void ACarveReachingIntoAnUnloadedChunkEditsWhatItCanAndReportsTheRest()
    {
        // Dropping this silently is the failure the whole return type exists to prevent: the
        // player sees a hole that is the wrong shape and has nothing to tell them why.
        var missing = new ChunkPos(-1, -1, -1);
        var world = new FakeEditor { DefaultMaterial = Dirt };
        world.Unload(missing);

        CarveResult result = Carve.Sphere(world, Materials(), Vector3.Zero, 0.5f, 1.0f);
        Accounted(result);

        var inside = CentresWithin(Vector3.Zero, 0.5f);
        int clipped = inside.Count(voxel => voxel.Chunk == missing);

        Assert.True(clipped > 0, "the fixture must actually clip part of the sphere");
        Assert.False(result.Complete);

        // The exact count, not merely non-zero: turning the unreachable `continue` into a
        // `break` would silently lose the reachable remainder and still report "some".
        Assert.Equal(clipped, result.Unreachable);
        Assert.Equal(inside.Count - clipped, result.Removed);
        Assert.DoesNotContain(missing, world.Writes.Select(write => write.Voxel.Chunk));
    }

    [Fact]
    public void ASwingWhoseBoxTouchesUnloadedSpaceButWhoseSphereDoesNotIsStillComplete()
    {
        // The negative control for the one above. The bounding box's corners reach a further
        // r(√3 − 1) than the sphere ever does, so counting Unreachable over the box rather
        // than over the membership test would make Complete false for most swings anywhere
        // near the loaded edge, and it would stop meaning anything at all.
        var world = new FakeEditor { DefaultMaterial = Dirt };
        world.Unload(new ChunkPos(-1, -1, -1));

        // Centred in the middle of chunk 0 with a radius that cannot reach chunk -1's voxels,
        // but whose box corner does sit in it.
        var centre = new Vector3(0.3f, 0.3f, 0.3f);
        CarveResult result = Carve.Sphere(world, Materials(), centre, 0.28f, 1.0f);
        Accounted(result);

        Assert.True(result.Removed > 0);
        Assert.Equal(0, result.Unreachable);
        Assert.True(result.Complete);
    }

    [Fact]
    public void AVoxelThatReadsButRefusesToBeWrittenIsReportedRatherThanCountedAsRemoved()
    {
        // The branch that should be unreachable in practice, because a successful read means
        // the chunk is loaded and generated — which is the same condition the write tests.
        // Without this it is dead code no test has ever executed, and the day the two halves
        // of IVoxelEditor diverge it would lose voxels instead of reporting them.
        var world = new FakeEditor { DefaultMaterial = Dirt };
        world.RefuseWrites(new ChunkPos(-1, -1, -1));

        CarveResult result = Carve.Sphere(world, Materials(), Vector3.Zero, 0.5f, 1.0f);
        Accounted(result);

        var inside = CentresWithin(Vector3.Zero, 0.5f);
        int refused = inside.Count(v => v.Chunk == new ChunkPos(-1, -1, -1));

        Assert.True(refused > 0);
        Assert.Equal(refused, result.Unreachable);
        Assert.Equal(inside.Count - refused, result.Removed);
        Assert.False(result.Complete);
    }

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(ToolDefinition.MaxRadiusMetres + 1f)]
    public void AnUnusableRadiusThrowsRatherThanScanningTheWorld(float radius)
    {
        // The last case is the one that matters: the scan is cubic, so a mod writing a radius
        // of a kilometre does not carve a crater, it stops the game inside a loop.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Carve.Sphere(new FakeEditor(), Materials(), Vector3.Zero, radius, 1.0f));
    }

    [Theory]
    [InlineData(float.NaN, 0f, 0f)]
    [InlineData(0f, float.NaN, 0f)]
    [InlineData(0f, 0f, float.NaN)]
    [InlineData(float.PositiveInfinity, 0f, 0f)]
    public void ANonFiniteCentreThrowsOnEveryAxis(float x, float y, float z)
    {
        // All three, because a check written for X only lets a NaN Y through, where it would
        // return a successful, empty carve rather than an error.
        Assert.Throws<ArgumentException>(() =>
            Carve.Sphere(new FakeEditor(), Materials(), new Vector3(x, y, z), 0.5f, 1.0f));
    }

    [Fact]
    public void ANaNHardnessThresholdThrowsRatherThanMiningEverything()
    {
        // The one unusable value that fails *open*. Every comparison with NaN is false, so
        // `hardness > threshold` never holds and the gate inverts: a NaN threshold does not
        // take nothing, it takes everything, bedrock included. A tool that mines the world is
        // the opposite of what someone typing a bad number expects.
        var world = new FakeEditor { DefaultMaterial = Bedrock };

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Carve.Sphere(world, Materials(), Vector3.Zero, 0.5f, float.NaN));

        Assert.Empty(world.Writes);

        // Negative infinity is the supported way to say "takes nothing", and it works.
        CarveResult none = Carve.Sphere(
            world, Materials(), Vector3.Zero, 0.5f, float.NegativeInfinity);

        Accounted(none);
        Assert.Equal(0, none.Removed);
        Assert.Equal(none.Candidates, none.TooHard);
    }

    [Fact]
    public void SwingTakesItsRadiusAndItsThresholdFromTheTool()
    {
        // Tools are content. If the swing did not read both numbers off the definition, a mod
        // could add a tier and nothing would change.
        var materials = Materials();
        var centre = new Vector3(0.125f, 0.125f, 0.125f);

        var weak = Tool("hand_pick", radius: 0.4f, threshold: 1.0f);
        var strong = Tool("steel_pick", radius: 0.4f, threshold: 3.5f);
        var wide = Tool("wide_pick", radius: 0.8f, threshold: 3.5f);

        Assert.Equal(0, Carve.Swing(StoneWorld(), materials, centre, weak).Removed);

        int narrow = Carve.Swing(StoneWorld(), materials, centre, strong).Removed;
        Assert.True(narrow > 0);
        Assert.True(Carve.Swing(StoneWorld(), materials, centre, wide).Removed > narrow);

        static FakeEditor StoneWorld() => new() { DefaultMaterial = Stone };
    }

    [Fact]
    public void ATierWhoseModIsGoneCarvesNothingEvenAimedStraightAtAVoxel()
    {
        // ToolDefinition.Unknown is inert where MaterialDefinition.Unknown is solid, and both
        // choices are the same rule: pick the failure that cannot destroy a world. Aimed at a
        // voxel *centre* deliberately — aiming at Vector3.Zero, which is a voxel corner, made
        // an earlier version of this test pass without exercising anything.
        var tools = new Registry<ToolDefinition>("tools", ToolDefinition.Unknown);
        int id = tools.ReserveId(Identifier.Parse("deepdig:plasma_lance"));
        tools.Freeze();

        ToolDefinition ghost = tools.Get(id);
        var world = new FakeEditor { DefaultMaterial = Dirt };
        (float x, float y, float z) = new VoxelPos(0, 0, 0).CentreMetres;

        Assert.True(ghost.IsUnknown);
        Assert.Equal(0, Carve.Swing(world, Materials(), new Vector3(x, y, z), ghost).Removed);
        Assert.Empty(world.Writes);

        // And it stays inert if M3d ever gives it a reach: nothing real is soft enough.
        var reaching = new ToolDefinition
        {
            Id = ghost.Id,
            RadiusMetres = 0.5f,
            HardnessThreshold = ghost.HardnessThreshold,
        };

        CarveResult result = Carve.Swing(world, Materials(), new Vector3(x, y, z), reaching);
        Assert.Equal(0, result.Removed);
        Assert.Equal(result.Candidates, result.TooHard);
    }

    [Fact]
    public void ASwingLandsOnTheHitVoxelsCentreRatherThanWhereTheRayEnteredIt()
    {
        // The M3d decision, pinned. The entry point lies on a voxel face, so a sphere centred
        // there is half air before it starts and its reach depends on the angle of incidence.
        var world = new FakeEditor { DefaultMaterial = Air };
        world.Set(new VoxelPos(0, 0, 0), Dirt);

        // Enters the -X face well off centre, at (0.0, 0.19, 0.19).
        RayOutcome outcome = VoxelRay.Cast(
            world,
            new Vector3(-1.0f, 0.19f, 0.19f),
            new Vector3(1f, 0f, 0f),
            5f,
            out VoxelHit hit);

        Assert.Equal(RayOutcome.Hit, outcome);
        Assert.Equal(new VoxelPos(0, 0, 0), hit.Voxel);

        (float cx, float cy, float cz) = hit.Voxel.CentreMetres;
        Assert.Equal(new Vector3(cx, cy, cz), Carve.SwingCentre(hit));

        // And it is genuinely a different point from where the ray came in, or this test
        // would pass whichever choice the code made.
        Vector3 entry = new Vector3(-1.0f, 0.19f, 0.19f) + (new Vector3(1f, 0f, 0f) * hit.DistanceMetres);
        Assert.NotEqual(entry, Carve.SwingCentre(hit));
    }

    [Fact]
    public void TwoSwingsAtTheSameVoxelTakeTheSameMaterialWhereverTheRayEnteredIt()
    {
        // The property the voxel centre was chosen for, and the one the entry point cannot
        // have: a tool's radius means the same thing however you were looking when you
        // clicked. Both rays here hit the same voxel, one through the middle of its top face
        // and one through a corner of it.
        //
        // Agreement alone is a weak claim, and watching it fail showed why: VoxelHit does not
        // carry the ray, so Carve.Swing(hit, ...) *cannot* centre on the entry point — the
        // type makes the wrong answer inexpressible, and any voxel-derived centre agrees.
        // The symmetry assertion below is the one with teeth, because it separates the voxel's
        // centre from its corner, which is what a sloppier "close enough" version would use.
        var tool = Tool("pick", radius: 0.5f, threshold: 1.0f);

        HashSet<VoxelPos> middle = SwingDownAt(0.125f, 0.125f, out VoxelHit a, out Vector3 entryA);
        HashSet<VoxelPos> corner = SwingDownAt(0.005f, 0.005f, out VoxelHit b, out Vector3 entryB);

        Assert.Equal(a.Voxel, b.Voxel);

        // The rays fall the same distance, so it is the entry *point* that has to differ for
        // this to be testing anything: one comes in through the middle of the top face and
        // one through a corner of it.
        Assert.NotEqual(entryA, entryB);
        Assert.NotEmpty(middle);
        Assert.Equal(middle, corner);

        // The bite is centred on the voxel the player aimed at: mirroring it through that
        // voxel on X and on Z maps the removed set onto itself. Y is left out because the
        // fixture is a half-space and the sphere is clipped by the surface, not by the shape.
        foreach (VoxelPos voxel in middle)
        {
            Assert.Contains(
                new VoxelPos((2 * a.Voxel.X) - voxel.X, voxel.Y, (2 * a.Voxel.Z) - voxel.Z),
                middle);
        }

        HashSet<VoxelPos> SwingDownAt(float x, float z, out VoxelHit hit, out Vector3 entry)
        {
            var world = new FakeEditor { DefaultMaterial = Air };
            for (int vx = -6; vx <= 6; vx++)
            for (int vy = -6; vy <= -1; vy++)
            for (int vz = -6; vz <= 6; vz++)
            {
                world.Set(new VoxelPos(vx, vy, vz), Dirt);
            }

            var origin = new Vector3(x, 1.0f, z);
            var direction = new Vector3(0f, -1f, 0f);

            RayOutcome outcome = VoxelRay.Cast(world, origin, direction, 5f, out hit);

            Assert.Equal(RayOutcome.Hit, outcome);
            entry = origin + (direction * hit.DistanceMetres);
            Accounted(Carve.Swing(world, Materials(), hit, tool));

            return world.WrittenVoxels;
        }
    }

    private static void Accounted(CarveResult result) =>
        Assert.True(result.IsAccounted, $"unaccounted candidates: {result}");

    private static Dictionary<int, int> Census(FakeEditor world, IEnumerable<VoxelPos> region)
    {
        var counts = new Dictionary<int, int> { [Air] = 0, [Dirt] = 0, [Stone] = 0, [Bedrock] = 0 };
        foreach (VoxelPos voxel in region)
        {
            counts[world[voxel]]++;
        }

        return counts;
    }

    private static ToolDefinition Tool(string path, float radius, float threshold) => new()
    {
        Id = new Identifier(Identifier.BaseNamespace, path),
        RadiusMetres = radius,
        HardnessThreshold = threshold,
    };

    /// <summary>
    /// Every voxel whose centre is within <paramref name="radius"/> of
    /// <paramref name="centre"/> — the definition, computed the slow way.
    /// </summary>
    /// <remarks>
    /// Deliberately shares nothing with <see cref="Carve"/>'s derivation. It scans a
    /// generously oversized cube instead of a tight bounding box, and it asks
    /// <see cref="VoxelPos.CentreMetres"/> for each centre in double precision. The bounding
    /// box is where an off-by-one would live, so a reference that derived the same box would
    /// agree with the bug. It <b>cannot</b> see the <c>&lt;=</c> versus <c>&lt;</c> choice —
    /// random centres essentially never tie — which is why the hand-built boundary case above
    /// is not redundant with it.
    /// </remarks>
    private static HashSet<VoxelPos> CentresWithin(Vector3 centre, float radius)
    {
        var inside = new HashSet<VoxelPos>();
        int reach = (int)MathF.Ceiling(radius * VoxelConstants.VoxelsPerMetre) + 3;
        VoxelPos origin = VoxelPos.FromMetres(centre.X, centre.Y, centre.Z);

        for (int x = origin.X - reach; x <= origin.X + reach; x++)
        for (int y = origin.Y - reach; y <= origin.Y + reach; y++)
        for (int z = origin.Z - reach; z <= origin.Z + reach; z++)
        {
            var voxel = new VoxelPos(x, y, z);
            (float vx, float vy, float vz) = voxel.CentreMetres;

            double dx = vx - (double)centre.X;
            double dy = vy - (double)centre.Y;
            double dz = vz - (double)centre.Z;

            if ((dx * dx) + (dy * dy) + (dz * dz) <= (double)radius * radius)
            {
                inside.Add(voxel);
            }
        }

        return inside;
    }

    /// <summary>
    /// A world that is one material everywhere unless told otherwise, with named chunks that
    /// answer "not loaded" — and, separately, named chunks that read but refuse to be written.
    /// </summary>
    private sealed class FakeEditor : IVoxelEditor
    {
        private readonly Dictionary<VoxelPos, int> _voxels = [];
        private readonly HashSet<ChunkPos> _unloaded = [];
        private readonly HashSet<ChunkPos> _writeRefused = [];

        public int DefaultMaterial { get; init; } = MaterialIds.AirNumericId;

        /// <summary>Every write attempted, in order and including any duplicates.</summary>
        public List<(VoxelPos Voxel, int Material)> Writes { get; } = [];

        public HashSet<VoxelPos> WrittenVoxels => Writes.Select(write => write.Voxel).ToHashSet();

        public int this[VoxelPos voxel] =>
            _voxels.TryGetValue(voxel, out int material) ? material : DefaultMaterial;

        public void Set(VoxelPos voxel, int material) => _voxels[voxel] = material;

        public void Unload(ChunkPos chunk) => _unloaded.Add(chunk);

        /// <summary>Reads fine, refuses writes — the two halves of the interface disagreeing.</summary>
        public void RefuseWrites(ChunkPos chunk) => _writeRefused.Add(chunk);

        public bool TryGetMaterial(VoxelPos voxel, out int materialId)
        {
            if (_unloaded.Contains(voxel.Chunk))
            {
                materialId = MaterialIds.AirNumericId;
                return false;
            }

            materialId = this[voxel];
            return true;
        }

        public bool SetVoxel(VoxelPos voxel, int materialId)
        {
            if (_unloaded.Contains(voxel.Chunk) || _writeRefused.Contains(voxel.Chunk))
            {
                return false;
            }

            Writes.Add((voxel, materialId));
            _voxels[voxel] = materialId;
            return true;
        }
    }
}
