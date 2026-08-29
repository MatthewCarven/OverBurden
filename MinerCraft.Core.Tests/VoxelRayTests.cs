using System.Numerics;
using MinerCraft.Core.Content;
using MinerCraft.Core.Voxels;
using MinerCraft.Core.Worldgen;

namespace MinerCraft.Core.Tests;

public class VoxelRayTests
{
    private const int Stone = 3;
    private const float Size = VoxelConstants.VoxelSize;

    [Fact]
    public void ARayAlongAnAxisHitsTheFirstSolidVoxelAndNamesTheFaceItEntered()
    {
        var world = new FakeWorld();
        world.SetSolid(new VoxelPos(5, 0, 0));

        RayOutcome outcome = VoxelRay.Cast(
            world, Centre(0, 0, 0), new Vector3(1, 0, 0), 10f, out VoxelHit hit);

        Assert.Equal(RayOutcome.Hit, outcome);
        Assert.Equal(new VoxelPos(5, 0, 0), hit.Voxel);
        Assert.Equal(new VoxelPos(4, 0, 0), hit.Previous);
        Assert.Equal((-1, 0, 0), hit.Normal);

        // Entered at the boundary between voxel 4 and 5, which is 4.5 voxels from the centre
        // of voxel 0.
        Assert.Equal(4.5f * Size, hit.DistanceMetres, 4);
    }

    [Theory]
    [InlineData(1, 0, 0, -1, 0, 0)]
    [InlineData(-1, 0, 0, 1, 0, 0)]
    [InlineData(0, 1, 0, 0, -1, 0)]
    [InlineData(0, -1, 0, 0, 1, 0)]
    [InlineData(0, 0, 1, 0, 0, -1)]
    [InlineData(0, 0, -1, 0, 0, 1)]
    public void TheNormalPointsBackAlongTheRayOnEveryAxis(
        int dx, int dy, int dz, int nx, int ny, int nz)
    {
        // Negative directions get their own cases on purpose: a sign or a floor error is
        // invisible in the positive octant, which is where hand-written tests tend to live.
        var world = new FakeWorld();
        var target = new VoxelPos(dx * 6, dy * 6, dz * 6);
        world.SetSolid(target);

        RayOutcome outcome = VoxelRay.Cast(
            world, Centre(0, 0, 0), new Vector3(dx, dy, dz), 10f, out VoxelHit hit);

        Assert.Equal(RayOutcome.Hit, outcome);
        Assert.Equal(target, hit.Voxel);
        Assert.Equal((nx, ny, nz), hit.Normal);

        // Stepping back along the normal from the hit lands on the last empty voxel, which is
        // where a placement goes at M6.
        Assert.Equal(hit.Previous, hit.Voxel.Offset(nx, ny, nz));
    }

    [Fact]
    public void ARayThatStartsInsideSolidMaterialReturnsThatVoxelWithNoFace()
    {
        // Otherwise the first swing mines whatever the camera happens to be embedded in, and
        // reports a face it never crossed.
        var world = new FakeWorld();
        world.SetSolid(new VoxelPos(0, 0, 0));

        RayOutcome outcome = VoxelRay.Cast(
            world, Centre(0, 0, 0), new Vector3(1, 0, 0), 10f, out VoxelHit hit);

        Assert.Equal(RayOutcome.Hit, outcome);
        Assert.Equal(new VoxelPos(0, 0, 0), hit.Voxel);
        Assert.Equal((0, 0, 0), hit.Normal);
        Assert.Equal(0f, hit.DistanceMetres);
    }

    [Fact]
    public void ARayThroughEmptyLoadedSpaceMisses()
    {
        var world = new FakeWorld();
        world.SetSolid(new VoxelPos(100, 0, 0));

        RayOutcome outcome = VoxelRay.Cast(
            world, Centre(0, 0, 0), new Vector3(1, 0, 0), 2f, out _);

        Assert.Equal(RayOutcome.Missed, outcome);
    }

    [Fact]
    public void ARayReachingAnUnloadedChunkIsBlockedRatherThanMissing()
    {
        // The distinction that stops the player mining through the edge of the world. An
        // unloaded voxel is an unknown, not an empty one.
        var world = new FakeWorld { LoadedMaxX = 10 };

        RayOutcome outcome = VoxelRay.Cast(
            world, Centre(0, 0, 0), new Vector3(1, 0, 0), 100f, out VoxelHit hit);

        Assert.Equal(RayOutcome.Blocked, outcome);
        Assert.Equal(11, hit.Voxel.X);
    }

    [Fact]
    public void AZeroDirectionIsRejectedRatherThanLoopingForever()
    {
        var world = new FakeWorld();
        Assert.Throws<ArgumentException>(
            () => VoxelRay.Cast(world, Vector3.Zero, Vector3.Zero, 10f, out _));
    }

    [Fact]
    public void AnAxisAlignedRayDoesNotStallOnTheAxesItDoesNotMoveAlong()
    {
        // Two of the three tMax values are infinite here. If the nearest-axis comparison ever
        // picks one of those, the step is zero and the walk never terminates — so this test
        // failing looks like the suite hanging, which is why the range is kept short.
        var world = new FakeWorld();
        world.SetSolid(new VoxelPos(0, 3, 0));

        RayOutcome outcome = VoxelRay.Cast(
            world, Centre(0, 0, 0), new Vector3(0, 1, 0), 5f, out VoxelHit hit);

        Assert.Equal(RayOutcome.Hit, outcome);
        Assert.Equal(new VoxelPos(0, 3, 0), hit.Voxel);
    }

    [Fact]
    public void TheWalkAgreesWithAnExactSlabTestOverThousandsOfRandomRays()
    {
        // The ChunkBox move: fuzz against a brute-force reference rather than against a second
        // implementation of the same trick.
        //
        // **The reference is a ray/box intersection, not a sampler**, and that correction
        // matters. The obvious oracle — step along the ray far finer than a voxel and report
        // the first solid one — is *less exact than the thing it is testing*: a ray can clip a
        // voxel corner and stay inside it for a shorter distance than the sample step, so the
        // sampler misses voxels the DDA correctly reports. Written that way first, it failed
        // on the DDA being right. The slab test below has no such blind spot.
        //
        // Watched fail before being trusted, per the M3a lesson: flipping FirstBoundary to
        // use the near corner for positive directions breaks this in 7 ms.
        var world = new FakeWorld();
        ulong seed = 0xB0A710ADUL;

        for (int i = 0; i < 4000; i++)
        {
            world.Clear();

            // A sparse cloud, so rays usually pass through several empty voxels first and the
            // walk is actually exercised rather than hitting immediately.
            for (int s = 0; s < 40; s++)
            {
                world.SetSolid(new VoxelPos(
                    RandomInt(seed, i, s, 0, -8, 8),
                    RandomInt(seed, i, s, 1, -8, 8),
                    RandomInt(seed, i, s, 2, -8, 8)));
            }

            var origin = new Vector3(
                RandomFloat(seed, i, 3, -3f, 3f),
                RandomFloat(seed, i, 4, -3f, 3f),
                RandomFloat(seed, i, 5, -3f, 3f));

            var direction = new Vector3(
                RandomFloat(seed, i, 6, -1f, 1f),
                RandomFloat(seed, i, 7, -1f, 1f),
                RandomFloat(seed, i, 8, -1f, 1f));

            if (direction.Length() < 0.05f)
            {
                continue;
            }

            const float Range = 6f;
            RayOutcome outcome = VoxelRay.Cast(world, origin, direction, Range, out VoxelHit hit);
            VoxelPos? expected = NearestSolidBySlabTest(world, origin, direction, Range);

            if (expected is null)
            {
                Assert.Equal(RayOutcome.Missed, outcome);
            }
            else
            {
                Assert.Equal(RayOutcome.Hit, outcome);
                Assert.Equal(expected.Value, hit.Voxel);
            }
        }
    }

    /// <summary>
    /// The reference: intersect the ray with every solid voxel's box and keep the nearest.
    /// </summary>
    /// <remarks>
    /// Exact rather than sampled, and a genuinely different algorithm from the one under test —
    /// a slab test against each box, versus an incremental walk along the grid. Sharing no
    /// reasoning with the DDA is what makes agreement worth something.
    /// </remarks>
    private static VoxelPos? NearestSolidBySlabTest(
        FakeWorld world, Vector3 origin, Vector3 direction, float range)
    {
        Vector3 unit = Vector3.Normalize(direction);
        VoxelPos? nearest = null;
        float nearestEntry = float.PositiveInfinity;

        foreach (VoxelPos voxel in world.Solids)
        {
            if (TryIntersect(origin, unit, voxel, out float entry)
                && entry <= range
                && entry < nearestEntry)
            {
                nearestEntry = entry;
                nearest = voxel;
            }
        }

        return nearest;
    }

    private static bool TryIntersect(Vector3 origin, Vector3 unit, VoxelPos voxel, out float entry)
    {
        float enter = float.NegativeInfinity;
        float exit = float.PositiveInfinity;
        entry = 0f;

        for (int axis = 0; axis < 3; axis++)
        {
            float o = axis switch { 0 => origin.X, 1 => origin.Y, _ => origin.Z };
            float d = axis switch { 0 => unit.X, 1 => unit.Y, _ => unit.Z };
            int index = axis switch { 0 => voxel.X, 1 => voxel.Y, _ => voxel.Z };

            float min = index * Size;
            float max = min + Size;

            if (d == 0.0f)
            {
                if (o < min || o > max)
                {
                    return false;
                }

                continue;
            }

            float t1 = (min - o) / d;
            float t2 = (max - o) / d;
            if (t1 > t2)
            {
                (t1, t2) = (t2, t1);
            }

            enter = MathF.Max(enter, t1);
            exit = MathF.Min(exit, t2);
        }

        if (enter > exit || exit < 0.0f)
        {
            return false;
        }

        // Clamped, so a ray starting inside a voxel reports zero rather than a negative
        // distance and still counts as the nearest hit.
        entry = MathF.Max(enter, 0.0f);
        return true;
    }

    private static Vector3 Centre(int x, int y, int z)
    {
        (float cx, float cy, float cz) = new VoxelPos(x, y, z).CentreMetres;
        return new Vector3(cx, cy, cz);
    }

    private static int RandomInt(ulong seed, int index, int slot, int channel, int min, int max) =>
        min + (int)(Hash64.ToUnitFloat(Hash64.Combine(seed + (ulong)channel, index, slot, channel))
                    * (max - min + 1));

    private static float RandomFloat(ulong seed, int index, int channel, float min, float max) =>
        min + (Hash64.ToUnitFloat(Hash64.Combine(seed, index, channel, 0)) * (max - min));

    /// <summary>A hand-filled world, with a hard edge to stand in for unloaded chunks.</summary>
    private sealed class FakeWorld : IVoxelWorld
    {
        private readonly HashSet<VoxelPos> _solid = [];

        /// <summary>Everything beyond this on X reads as not loaded.</summary>
        public int LoadedMaxX { get; init; } = int.MaxValue;

        /// <summary>Every solid voxel, for the brute-force reference to iterate.</summary>
        public IReadOnlyCollection<VoxelPos> Solids => _solid;

        public void SetSolid(VoxelPos voxel) => _solid.Add(voxel);

        public void Clear() => _solid.Clear();

        public bool TryGetMaterial(VoxelPos voxel, out int materialId)
        {
            if (voxel.X > LoadedMaxX)
            {
                materialId = MaterialIds.AirNumericId;
                return false;
            }

            materialId = _solid.Contains(voxel) ? Stone : MaterialIds.AirNumericId;
            return true;
        }
    }
}
