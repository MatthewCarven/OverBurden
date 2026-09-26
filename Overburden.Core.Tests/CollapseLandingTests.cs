using Overburden.Core.Structure;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// COLLAPSE-PLAN part a-2: the landing solver and the event. The solver's oracle is a boolean over
/// its own search — no piece rests higher than any descending placement it simulated — beside the
/// physical checks every landing must pass: it rests on something, it overlaps nothing, its path
/// only ever goes down or sideways one mino a tick, and Σ(Y) strictly falls.
/// </summary>
public class CollapseLandingTests
{
    private const int Air = SupportTestWorld.Air;
    private const int Stone = SupportTestWorld.Stone;

    // ---- Fixtures -----------------------------------------------------------------------------

    /// <summary>A stone chunk at (0,0,0) — the floor's top at Y 31 — under an air chunk, with room around.</summary>
    private static SupportTestWorld Floor()
    {
        var world = new SupportTestWorld();
        world.FillBox(new ChunkPos(-1, 0, -1), new ChunkPos(1, 0, 1), Stone);
        world.FillBox(new ChunkPos(-1, 1, -1), new ChunkPos(1, 2, 1), Air);
        return world;
    }

    private static CollapsePiece Piece(int mino, params VoxelPos[] voxels)
    {
        int shift = mino == 1 ? 0 : mino == 2 ? 1 : 2;
        (int X, int Y, int Z)[] minos = voxels.Select(v => (v.X >> shift, v.Y >> shift, v.Z >> shift)).Distinct().ToArray();
        return new CollapsePiece(0, mino, minos, voxels, 0);
    }

    private static VoxelPos[] Row(int x, int y, int z, int length) =>
        Enumerable.Range(0, length).Select(i => new VoxelPos(x + i, y, z)).ToArray();

    // ---- The solver ---------------------------------------------------------------------------

    [Fact]
    public void APieceOverAPitTakesThePit()
    {
        SupportTestWorld world = Floor();
        // A trench four long, one wide, three deep, 5 voxels east of the piece.
        world.SetBox(new VoxelPos(16, 29, 10), new VoxelPos(19, 31, 10), Air);
        CollapsePiece piece = Piece(1, Row(10, 40, 10, 4));
        var editor = new SupportTestWorld.Editor(world, null);

        LandingPlan plan = LandingSolver.Solve(piece, editor, world.Materials, null, new VoxelPos(11, 40, 10), 2f, 1)!;

        Assert.NotNull(plan);
        Assert.Equal(29, plan.Rest.Y);
        Assert.All(plan.RestVoxels, v => Assert.InRange(v.X, 16, 19));
        AssertLanding(world, editor, piece, plan, null);
    }

    [Fact]
    public void AReservedLandingIsSolidToTheNextPiece()
    {
        SupportTestWorld world = Floor();
        world.SetBox(new VoxelPos(16, 29, 10), new VoxelPos(19, 31, 10), Air);
        var reserved = new HashSet<VoxelPos>();
        for (int x = 16; x <= 19; x++)
        {
            for (int y = 29; y <= 31; y++)
            {
                reserved.Add(new VoxelPos(x, y, 10));
            }
        }

        CollapsePiece piece = Piece(1, Row(10, 40, 10, 4));
        var editor = new SupportTestWorld.Editor(world, null);
        LandingPlan plan = LandingSolver.Solve(piece, editor, world.Materials, reserved, new VoxelPos(11, 40, 10), 2f, 1)!;

        Assert.Equal(32, plan.Rest.Y);
        Assert.DoesNotContain(plan.RestVoxels, reserved.Contains);
        AssertLanding(world, editor, piece, plan, reserved);
    }

    [Fact]
    public void OnLevelGroundDebrisSpreadsAwayFromTheCentre()
    {
        SupportTestWorld world = Floor();
        CollapsePiece piece = Piece(1, new VoxelPos(10, 40, 10));
        var editor = new SupportTestWorld.Editor(world, null);
        var seen = new List<LandingCandidate>();
        LandingPlan plan = LandingSolver.Solve(piece, editor, world.Materials, null, new VoxelPos(10, 40, 10), 2f, 1, seen)!;

        // Every placement rests on the floor at 32; the chosen one is as far from the centre as a
        // fall can steer. Eight ticks down is eight sideways moves, plus one on the tick it lands
        // (the down step there is short, and the steer came first) — nine, so 8 out on one axis
        // and 1 on the other: 65 voxels², not the 128 of the spread's corner.
        Assert.Equal(32, plan.Rest.Y);
        int dx = plan.Rest.X - 10, dz = plan.Rest.Z - 10;
        Assert.Equal(65, (dx * dx) + (dz * dz));
        Assert.Equal(17 * 17, seen.Count);
    }

    [Fact]
    public void ABlockedSteerGivesUpAndDropsStraight()
    {
        // A ledge beside the piece at its own height, and a pit beyond it under the ledge. Steering
        // east is blocked at the first tick; the plan's rule gives the steer up, so the piece drops
        // to the floor rather than slipping east under the ledge once it has fallen past it. (A
        // steer retried every tick would find the pit, and would keep the fall off the column
        // jump for as long as it kept trying.)
        SupportTestWorld world = Floor();
        world.SetBox(new VoxelPos(11, 38, 5), new VoxelPos(20, 42, 15), Stone);   // the ledge
        world.SetBox(new VoxelPos(13, 24, 5), new VoxelPos(20, 31, 15), Air);     // the pit under it
        CollapsePiece piece = Piece(1, new VoxelPos(10, 40, 10));
        var editor = new SupportTestWorld.Editor(world, null);

        // Every steer that does not start east rests on the floor at 32; only a steer east that
        // kept trying could reach the pit.
        LandingPlan plan = LandingSolver.Solve(piece, editor, world.Materials, null, new VoxelPos(10, 40, 10), 2f, 1)!;

        Assert.Equal(32, plan.Rest.Y);
        Assert.True(plan.Rest.X <= 10, $"the piece reached {plan.Rest} past the ledge");
    }

    [Fact]
    public void AnUnloadedChunkIsSolidToAFallingPiece()
    {
        var world = new SupportTestWorld();
        world.Fill(new ChunkPos(0, 1, 0), Air);   // nothing below it exists
        CollapsePiece piece = Piece(1, Row(10, 40, 10, 2));
        var editor = new SupportTestWorld.Editor(world, null);
        LandingPlan plan = LandingSolver.Solve(piece, editor, world.Materials, null, new VoxelPos(10, 40, 10), 0f, 1)!;

        Assert.Equal(32, plan.Rest.Y);
    }

    [Fact]
    public void APieceThatCannotDescendWaits()
    {
        SupportTestWorld world = Floor();
        CollapsePiece piece = Piece(1, Row(10, 32, 10, 4));   // lying on the floor already
        var editor = new SupportTestWorld.Editor(world, null);
        var seen = new List<LandingCandidate>();

        Assert.Null(LandingSolver.Solve(piece, editor, world.Materials, null, new VoxelPos(10, 32, 10), 0f, 1, seen));
        Assert.NotEmpty(seen);
        Assert.DoesNotContain(seen, c => c.Descends);
    }

    [Fact]
    public void ABigMinoStepsAMinoATickAndFinishesVoxelExact()
    {
        SupportTestWorld world = Floor();
        // A 1 m mino at Y 41..44 over a floor whose top is 31: 9 voxels to fall, two whole ticks
        // of 4 and a last tick of 1.
        var voxels = new List<VoxelPos>();
        for (int x = 8; x < 12; x++)
        for (int y = 41; y < 45; y++)
        for (int z = 8; z < 12; z++)
        {
            voxels.Add(new VoxelPos(x, y, z));
        }

        CollapsePiece piece = Piece(4, [.. voxels]);
        var editor = new SupportTestWorld.Editor(world, null);
        LandingPlan plan = LandingSolver.Solve(piece, editor, world.Materials, null, new VoxelPos(10, 42, 10), 0f, 1)!;

        Assert.Equal(32, plan.Rest.Y);
        Assert.Equal([41, 37, 33, 32], plan.Path.Select(p => p.Y));
        AssertLanding(world, editor, piece, plan, null);
    }

    [Fact]
    public void FuzzedLandingsKeepTheOracle()
    {
        var random = new Random(26092026);
        for (int trial = 0; trial < 60; trial++)
        {
            SupportTestWorld world = Floor();

            // A rough floor: random pits and mounds in the top layers.
            for (int k = 0; k < 40; k++)
            {
                int x = random.Next(-8, 40), z = random.Next(-8, 40);
                int top = random.Next(26, 38);
                world.SetBox(new VoxelPos(x, top, z), new VoxelPos(x + random.Next(0, 4), 31, z + random.Next(0, 4)), top > 31 ? Stone : Air);
                if (top > 31)
                {
                    world.SetBox(new VoxelPos(x, 32, z), new VoxelPos(x + random.Next(0, 3), top, z + random.Next(0, 3)), Stone);
                }
            }

            // A blob in the air, cut; each piece solved against the world with the earlier pieces
            // reserved where they rest.
            var blob = new HashSet<VoxelPos>();
            int bx = random.Next(4, 24), bz = random.Next(4, 24), by = random.Next(44, 56);
            for (int k = 0; k < 30; k++)
            {
                blob.Add(new VoxelPos(bx + random.Next(0, 6), by + random.Next(0, 4), bz + random.Next(0, 6)));
            }

            foreach ((int X, int Y, int Z) at in blob.Select(v => (v.X, v.Y, v.Z)).ToList())
            {
                world.Set(new VoxelPos(at.X, at.Y, at.Z), Stone);
            }

            int mino = new[] { 1, 2, 4 }[trial % 3];
            var editor = new SupportTestWorld.Editor(world, null);
            var reserved = new HashSet<VoxelPos>();
            var centre = new VoxelPos(bx + 3, by, bz + 3);
            foreach (CollapsePiece piece in PieceCutter.Cut(blob, centre, mino, (ulong)trial))
            {
                var seen = new List<LandingCandidate>();
                LandingPlan? plan = LandingSolver.Solve(piece, editor, world.Materials, reserved, centre, 2f, (ulong)trial, seen);
                if (plan is null)
                {
                    Assert.DoesNotContain(seen, c => c.Descends);
                    continue;
                }

                // The leading faces answer what the whole shape answers, placement for placement.
                var seenWhole = new List<LandingCandidate>();
                LandingPlan? whole = LandingSolver.Solve(piece, editor, world.Materials, reserved, centre, 2f, (ulong)trial, seenWhole, wholeShape: true);
                Assert.Equal(seenWhole, seen);
                Assert.NotNull(whole);
                Assert.Equal(whole.Rest, plan.Rest);
                Assert.Equal(whole.Orientation, plan.Orientation);
                Assert.Equal(whole.Path, plan.Path);

                int lowest = seen.Where(c => c.Descends).Min(c => c.RestBottomY);
                Assert.True(plan.Rest.Y <= lowest, $"trial {trial}: rests at {plan.Rest.Y} with a placement at {lowest} simulated");
                AssertLanding(world, editor, piece, plan, reserved);

                // Written as the caller would: the piece's voxels to air, its rest as solid.
                foreach (VoxelPos v in piece.Voxels)
                {
                    world.Set(v, Air);
                }

                foreach (VoxelPos v in plan.RestVoxels)
                {
                    world.Set(v, Stone);
                }
            }
        }
    }

    /// <summary>Every landing: overlaps nothing, rests on something, moves a mino at most per axis per tick, only down or sideways, and lowers Σ(Y).</summary>
    private static void AssertLanding(SupportTestWorld world, SupportTestWorld.Editor editor, CollapsePiece piece, LandingPlan plan, HashSet<VoxelPos>? reserved)
    {
        var own = piece.Voxels.ToHashSet();
        bool Solid(VoxelPos v) =>
            (reserved?.Contains(v) ?? false)
            || (!own.Contains(v) && (!editor.TryGetMaterial(v, out int m) || world.Materials.IsSolid(m)));

        var rest = plan.RestVoxels.ToList();
        Assert.Equal(piece.Voxels.Length, rest.Count);
        Assert.DoesNotContain(rest, Solid);
        Assert.Contains(rest, v => Solid(v.Offset(0, -1, 0)));
        Assert.True(rest.Sum(v => (long)v.Y) < piece.Voxels.Sum(v => (long)v.Y), "the landing does not lower sum(Y)");

        int mino = piece.MinoVoxels;
        for (int i = 1; i < plan.Path.Count; i++)
        {
            VoxelPos a = plan.Path[i - 1], b = plan.Path[i];
            Assert.True(b.Y <= a.Y, "the path went up");
            Assert.InRange(a.Y - b.Y, 0, mino);
            Assert.InRange(Math.Abs(b.X - a.X) + Math.Abs(b.Z - a.Z), 0, mino);
            Assert.True(a != b, "a tick that went nowhere");
            foreach ((int X, int Y, int Z) s in plan.Shape)
            {
                Assert.False(Solid(b.Offset(s.X, s.Y, s.Z)), $"the path passes through solid at tick {i}");
            }
        }
    }

    // ---- The event ----------------------------------------------------------------------------

    private static HashSet<VoxelPos> Slab(int size, int y)
    {
        var set = new HashSet<VoxelPos>();
        for (int x = 0; x < size; x++)
        {
            for (int z = 0; z < size; z++)
            {
                set.Add(new VoxelPos(x, y, z));
            }
        }

        return set;
    }

    private static LandingPlan StraightDown(CollapsePiece piece, int drop)
    {
        VoxelPos anchor = new(piece.Voxels.Min(v => v.X), piece.Voxels.Min(v => v.Y), piece.Voxels.Min(v => v.Z));
        (int X, int Y, int Z)[] shape = Polycubes.Normalise(piece.Voxels.Select(v => (v.X - anchor.X, v.Y - anchor.Y, v.Z - anchor.Z)));
        var path = Enumerable.Range(0, drop + 1).Select(k => anchor.Offset(0, -k, 0)).ToList();
        return new LandingPlan(0, shape, anchor, path[^1], path, 1);
    }

    [Fact]
    public void AYoungEventReleasesTwoToFourPiecesASecondAndThenAccelerates()
    {
        var ev = new CollapseEvent(1, new VoxelPos(8, 0, 8), 0, CollapsePace.Default, 5);
        ev.Replan(Slab(64, 0), 0);
        var times = new List<long>();
        for (long t = 0; t <= 30_000; t++)
        {
            while (ev.TryRelease(t, _ => true, out CollapsePiece piece))
            {
                times.Add(t);
                ev.Launch(piece, StraightDown(piece, 1), t);
            }
        }

        var young = times.Zip(times.Skip(1), (a, b) => (At: a, Gap: b - a)).Where(p => p.At < 15_000).ToList();
        Assert.All(young, p => Assert.InRange(p.Gap, 250, 500));
        double youngRate = young.Count / 15.0;
        double oldRate = times.Count(t => t >= 25_000 && t < 30_000) / 5.0;
        Assert.InRange(youngRate, 2.5, 3.5);
        Assert.True(oldRate > 3 * youngRate, $"{oldRate:F1}/s at 25-30 s against {youngRate:F1}/s young");
    }

    [Fact]
    public void AShoreCountsAsRescuedAtTheReplanAndAtTheRelease()
    {
        var ev = new CollapseEvent(1, VoxelPos.Zero, 0, CollapsePace.Default, 5);
        HashSet<VoxelPos> slab = Slab(8, 0);
        ev.Replan(slab, 0);
        Assert.InRange(ev.Queued, 16, 32);   // 16 if the tetracubes tiled it; the greedy strands a mino or two

        // Half the slab is shored: the next settle's set is the other half.
        var half = slab.Where(v => v.X < 4).ToHashSet();
        ev.Replan(half, 100);
        Assert.Equal(32, ev.RescuedVoxels);

        // At the release, a piece the field no longer marks is dropped as rescued too.
        Assert.False(ev.TryRelease(100, _ => false, out _));
        Assert.Equal(64, ev.RescuedVoxels);
        Assert.Equal(0, ev.Queued);
        Assert.Equal(0, ev.Released);
    }

    [Fact]
    public void APieceFallsATickPerStepAndLandsWhenTheCallerSays()
    {
        var ev = new CollapseEvent(1, VoxelPos.Zero, 0, CollapsePace.Default, 5);
        ev.Replan(Slab(4, 20), 0);
        Assert.True(ev.TryRelease(0, _ => true, out CollapsePiece piece));
        LandingPlan plan = StraightDown(piece, 8);
        CollapseFlight flight = ev.Launch(piece, plan, 0);
        Assert.All(plan.RestVoxels, v => Assert.Contains(v, ev.Reserved));
        Assert.Equal(ev.ReleasedVoxels, ev.LandedVoxels + ev.InFlightVoxels);

        var due = new List<CollapseFlight>();
        ev.Advance(1_750, due);   // 1-voxel minos at 1 m/s: a tick is 0.25 s; 7 of 8 ticks
        Assert.Empty(due);
        Assert.Equal(plan.Path[7], flight.Anchor);
        ev.Advance(2_000, due);
        Assert.Same(flight, Assert.Single(due));

        // Due is not landed: it stays reserved until written.
        Assert.Equal(1, ev.InFlight.Count);
        ev.Land(flight);
        Assert.Empty(ev.InFlight);
        Assert.Empty(ev.Reserved);
        Assert.Equal(ev.ReleasedVoxels, ev.LandedVoxels);
        Assert.Throws<InvalidOperationException>(() => ev.Land(flight));
    }

    [Fact]
    public void AnOldEventsBigPiecesStepAMetreATickAtTheFastFall()
    {
        // At 30 s the mino is the 1 m cell and the fall is 8 m/s and climbing: a tick is ~0.12 s.
        var ev = new CollapseEvent(1, VoxelPos.Zero, 0, CollapsePace.Default, 5);
        ev.Replan(Slab(16, 40), 30_000);
        Assert.Equal(4, ev.PlannedMino);
        Assert.True(ev.TryRelease(30_000, _ => true, out CollapsePiece piece));
        CollapseFlight flight = ev.Launch(piece, StraightDown(piece, 4), 30_000);

        var due = new List<CollapseFlight>();
        ev.Advance(30_000, due);
        ev.Advance(30_400, due);   // ~3.4 ticks of 4
        Assert.Empty(due);
        ev.Advance(30_600, due);
        Assert.Same(flight, Assert.Single(due));
    }

    [Fact]
    public void TheMinoGrowsAndTheQueueIsRecutAtIt()
    {
        var ev = new CollapseEvent(1, VoxelPos.Zero, 0, CollapsePace.Default, 5);
        ev.Replan(Slab(16, 0), 0);
        Assert.Equal(1, ev.PlannedMino);
        Assert.False(ev.MinoChanged(15_000));
        Assert.True(ev.MinoChanged(20_000));
        ev.Replan(Slab(16, 0), 20_000);
        Assert.Equal(2, ev.PlannedMino);
        Assert.Equal(0, ev.RescuedVoxels);
    }

    [Fact]
    public void APieceThatWaitsComesBackFirst()
    {
        var ev = new CollapseEvent(1, VoxelPos.Zero, 0, CollapsePace.Default, 5);
        ev.Replan(Slab(8, 0), 0);
        Assert.True(ev.TryRelease(0, _ => true, out CollapsePiece first));
        ev.Wait(first);
        Assert.Equal(1, ev.Waited);
        Assert.False(ev.TryRelease(10, _ => true, out _));
        Assert.True(ev.TryRelease(1_000, _ => true, out CollapsePiece again));
        Assert.Same(first, again);
    }

    [Fact]
    public void AQuitLandsEverythingInFlight()
    {
        var ev = new CollapseEvent(1, VoxelPos.Zero, 0, CollapsePace.Default, 5);
        ev.Replan(Slab(8, 30), 0);
        long t = 0;
        for (int i = 0; i < 3; i++, t += 1_000)
        {
            Assert.True(ev.TryRelease(t, _ => true, out CollapsePiece piece));
            ev.Launch(piece, StraightDown(piece, 20), t);
        }

        var landed = new List<CollapseFlight>();
        ev.LandAll(landed);
        Assert.Equal(3, landed.Count);
        Assert.Empty(ev.InFlight);
        Assert.Empty(ev.Reserved);
        Assert.Equal(ev.ReleasedVoxels, ev.LandedVoxels);
    }
}
