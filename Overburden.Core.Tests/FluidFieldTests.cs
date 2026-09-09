using Overburden.Core.Fluid;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// The baked terrain: a field built from a terrain must answer as that terrain does, and a tick
/// against it must land the same water (the water thread, slice 2).
/// </summary>
public sealed class FluidFieldTests
{
    private const int Cap = FluidConstants.CellCapacityMillilitres;

    /// <summary>A terrain a test can dig, and which counts what was asked of it.</summary>
    private sealed class CountingTerrain(Func<VoxelPos, bool> canHold) : IFluidTerrain
    {
        private readonly HashSet<VoxelPos> _dug = [];

        public long Questions { get; private set; }

        public bool CanHold(VoxelPos voxel)
        {
            Questions++;
            return _dug.Contains(voxel) || canHold(voxel);
        }

        public void Dig(VoxelPos voxel) => _dug.Add(voxel);
    }

    private static IEnumerable<ChunkPos> Box(int chunks)
    {
        for (int x = 0; x < chunks; x++)
        {
            for (int y = 0; y < chunks; y++)
            {
                for (int z = 0; z < chunks; z++)
                {
                    yield return new ChunkPos(x, y, z);
                }
            }
        }
    }

    private static CountingTerrain Cave() => new(v =>
        // Something with structure in all three axes, so a field that transposed its index would
        // disagree somewhere rather than looking plausible.
        ((v.X * 7) + (v.Y * 13) + (v.Z * 3)) % 5 != 0 && v.Y < 40);

    [Fact]
    public void AFieldAnswersAsTheTerrainItWasBuiltFrom()
    {
        CountingTerrain source = Cave();
        var field = new FluidField(source);
        var reachable = new HashSet<ChunkPos>(Box(2));
        field.Sync(reachable);

        // Voxel for voxel over every chunk built — the IsCaveAt/GenerateTerrain shape of test, and
        // the only one that catches an index packed in a different order from the storage.
        foreach (ChunkPos chunk in reachable)
        {
            VoxelPos origin = chunk.OriginVoxel;
            for (int i = 0; i < VoxelConstants.ChunkVolume; i += 7)
            {
                (int lx, int ly, int lz) = VoxelPos.FromIndex(i);
                var voxel = new VoxelPos(origin.X + lx, origin.Y + ly, origin.Z + lz);
                Assert.Equal(source.CanHold(voxel), field.CanHold(voxel));
                Assert.Equal(
                    source.CanHold(voxel) ? FluidClass.Holdable : FluidClass.Solid,
                    field.ClassAt(voxel));
            }
        }

        Assert.Equal(0, field.Misses);
        Assert.Equal(8, field.Chunks);
        Assert.Equal(8L * FluidField.BytesPerChunk, field.Bytes);
    }

    [Fact]
    public void AChunkWithNoFieldFallsThroughAndIsCounted()
    {
        CountingTerrain source = Cave();
        var field = new FluidField(source);
        field.Sync(new HashSet<ChunkPos> { ChunkPos.Zero });

        var inside = new VoxelPos(4, 4, 4);
        var outside = new VoxelPos(400, 4, 4);

        Assert.Equal(FluidClass.Unknown, field.ClassAt(outside));
        Assert.Equal(0, field.Misses);

        // Behaviour-preserving by construction: an unbaked chunk gives the answer it always gave,
        // and says so in a counter. Slice 3 is where a miss becomes an error.
        Assert.Equal(source.CanHold(outside), field.CanHold(outside));
        Assert.Equal(1, field.Misses);

        field.CanHold(inside);
        Assert.Equal(1, field.Misses);
    }

    [Fact]
    public void AnEditReplacesTheArrayRatherThanMutatingIt()
    {
        CountingTerrain source = Cave();
        var field = new FluidField(source);
        field.Sync(new HashSet<ChunkPos> { ChunkPos.Zero });

        // A voxel the cave rule calls solid, so digging it is a real change.
        VoxelPos dug = Enumerable.Range(0, VoxelConstants.ChunkVolume)
            .Select(i => VoxelPos.FromIndex(i))
            .Select(l => new VoxelPos(l.X, l.Y, l.Z))
            .First(v => !source.CanHold(v));

        Assert.False(field.CanHold(dug));
        source.Dig(dug);

        // Not until it is told: the field is a copy, and the world moving does not move it.
        Assert.False(field.CanHold(dug));

        field.Invalidate(dug);
        Assert.True(field.CanHold(dug));
        Assert.Equal(1, field.Invalidations);

        // Its neighbours are untouched, which is what says a clone was edited rather than rebuilt
        // from a source that has since moved.
        Assert.Equal(source.CanHold(dug.Offset(1, 0, 0)), field.CanHold(dug.Offset(1, 0, 0)));
    }

    [Fact]
    public void AReaderHoldingTheOldFieldSeesAConsistentWorldOneEditBehind()
    {
        CountingTerrain source = Cave();
        var field = new FluidField(source);
        field.Sync(new HashSet<ChunkPos> { ChunkPos.Zero });

        VoxelPos dug = Enumerable.Range(0, VoxelConstants.ChunkVolume)
            .Select(i => VoxelPos.FromIndex(i))
            .Select(l => new VoxelPos(l.X, l.Y, l.Z))
            .First(v => !source.CanHold(v));

        // What a worker holds: the answers as they were when it started. Copy-on-write is what
        // makes "one edit behind" the worst case rather than "torn".
        bool before = field.CanHold(dug);
        source.Dig(dug);
        field.Invalidate(dug);

        Assert.False(before);
        Assert.True(field.CanHold(dug));
    }

    [Fact]
    public void SyncBuildsWhatIsReachableAndForgetsWhatIsNot()
    {
        CountingTerrain source = Cave();
        var field = new FluidField(source);

        field.Sync(new HashSet<ChunkPos>(Box(2)));
        Assert.Equal(8, field.Chunks);
        Assert.Equal(8, field.Builds);

        // Half the box walks away; the other half is not rebuilt.
        field.Sync(new HashSet<ChunkPos> { ChunkPos.Zero, new(0, 0, 1) });
        Assert.Equal(2, field.Chunks);
        Assert.Equal(6, field.Forgets);
        Assert.Equal(8, field.Builds);

        // An unloaded chunk needs no hook of its own: it stops being reachable, and that is all.
        Assert.Equal(FluidClass.Unknown, field.ClassAt(new VoxelPos(40, 40, 40)));
    }

    [Fact]
    public void ATickAgainstTheFieldLandsTheSameWaterAsATickAgainstTheWorld()
    {
        // The substitution's own oracle, and the reason FluidField is an IFluidTerrain: the two
        // arms are the same simulation over two terrains, so anything that differs is the field.
        CountingTerrain direct = Cave();
        CountingTerrain baked = Cave();
        var field = new FluidField(baked);
        field.Sync(new HashSet<ChunkPos>(Box(3)));

        var a = new FluidSimulation(direct);
        var b = new FluidSimulation(field);

        foreach (FluidSimulation sim in new[] { a, b })
        {
            for (int i = 0; i < 24; i++)
            {
                sim.Add(new VoxelPos(20 + (i % 6), 36, 20 + (i / 6)), Cap);
            }
        }

        for (int tick = 0; tick < 120; tick++)
        {
            a.Step();
            b.Step();

            Assert.Equal(a.ActiveCellCount, b.ActiveCellCount);
            Assert.Equal(a.TotalMillilitres, b.TotalMillilitres);
        }

        foreach (VoxelPos cell in a.Grid.HeldCells())
        {
            Assert.Equal(a.Grid.AmountAt(cell), b.Grid.AmountAt(cell));
        }

        Assert.Equal(a.Grid.HeldCellCount, b.Grid.HeldCellCount);
        Assert.Equal(0, field.Misses);
        Assert.True(a.CellVisits > 10_000, $"only {a.CellVisits} visits: the flood was too small to mean anything");

        // <b>The field asks its source once per voxel of every chunk it builds, and never again.</b>
        // Stated as an equality rather than as "fewer than the world", because that is false and
        // this fixture is what said so: 27 chunks is 884,736 questions to bake against the direct
        // arm's 85,384 over 120 ticks. <b>A chunk's field costs 32,768 questions and saves one per
        // read, so it is an amortisation that loses whenever more is baked than the water uses</b> —
        // which is the argument for the reachable set being the wet chunks and their shell rather
        // than the view, and the second reason (after --terrain's 1.47x) that this slice is
        // slice 3's enabler and not a speed-up.
        Assert.Equal(27L * VoxelConstants.ChunkVolume, baked.Questions);
        Assert.Equal(0, field.Invalidations);
    }

    [Fact]
    public void TheReachableSetIsTheWetChunksAndTheirFaceNeighbours()
    {
        var sim = new FluidSimulation(new PredicateTerrain(_ => true));
        sim.Add(new VoxelPos(40, 40, 40), Cap);

        var reachable = new HashSet<ChunkPos>();
        sim.ReachableChunks(reachable);

        var wet = new ChunkPos(1, 1, 1);
        Assert.Contains(wet, reachable);
        Assert.Contains(wet.Offset(1, 0, 0), reachable);
        Assert.Contains(wet.Offset(0, -1, 0), reachable);

        // <b>Two rings of face neighbours, not one — 1 + 6 + 18.</b> One ring was the first rule
        // and the miss counter refuted it in a headless run: an awake cell need not hold water, so
        // it can sit in the first ring, and visiting it reads *its* face neighbours, which are in
        // the second. Everything reachable by two steps along faces is in, which includes an edge
        // like (1, 1, 0)...
        Assert.Equal(25, reachable.Count);
        Assert.Contains(wet.Offset(2, 0, 0), reachable);
        Assert.Contains(wet.Offset(1, 1, 0), reachable);

        // ...and excludes a corner, which is three steps and which no visit can read.
        Assert.DoesNotContain(wet.Offset(1, 1, 1), reachable);
        Assert.DoesNotContain(wet.Offset(3, 0, 0), reachable);
    }
}
