using Overburden.Core.Content;
using Overburden.Core.Mining;
using Overburden.Core.Voxels;

namespace Overburden.Core.Tests;

/// <summary>
/// The single-voxel carve — the precision shape, held under a modifier while the sphere stays
/// the bare click.
/// </summary>
/// <remarks>
/// <para>
/// The claims worth testing here are not geometric, because there is no geometry: it is one
/// named voxel. They are that it takes <b>exactly</b> that one, that it is gated by the same
/// hardness rule as the sphere so tiers keep meaning what they mean, and that its
/// <see cref="CarveResult"/> is accounted the same way so the two shapes can be totalled
/// together by whatever reads them.
/// </para>
/// <para>
/// <b>Break-recipes run against the finished code:</b>
/// </para>
/// <list type="number">
/// <item><c>Chip</c> spheres the hit voxel instead of taking it — caught, by 2.</item>
/// <item><b>The hardness gate dropped from the shared <c>Take</c> — caught, by 9</b>, and
/// <b>seven of them are pre-existing sphere tests</b>. That number is the evidence that
/// extracting <c>Take</c> unified the gate rather than forking it: one edit breaks both shapes,
/// which is precisely what a second copy would not do.</item>
/// <item><c>Voxel</c> reporting <c>Candidates</c> 0 for a refusal — caught, by 2. <b>By their
/// exact-<c>CarveResult</c> comparisons, not only by
/// <see cref="CarveResult.IsAccounted"/></b>, which is worth being accurate about: the
/// accounting assertion does fail too, but the equality is what fires first and it is the one
/// present in every case here.</item>
/// <item>An unreachable voxel reported as <c>TooHard</c> — caught, by 3. Same distinction
/// <c>RayOutcome.Blocked</c> draws against <c>Missed</c>.</item>
/// <item>Air counted as removed — caught, by 6. Air's hardness is 0 and passes every threshold,
/// so only the identity check stops it.</item>
/// <item><c>Chip</c> passing an infinite threshold instead of the tool's — caught, by 1, and
/// only by <see cref="A_tier_that_cannot_sphere_a_material_cannot_chip_it_either"/>. Nothing
/// else in the suite notices that precision has stopped respecting tiers.</item>
/// </list>
/// <para>
/// Six recipes, six caught, none green — unusual for this project and worth stating plainly
/// rather than assuming the battery was too easy. The shape has no geometry to get wrong, which
/// is most of why.
/// </para>
/// </remarks>
public class CarveVoxelTests
{
    private const int Air = MaterialIds.AirNumericId;
    private const int Dirt = 1;
    private const int Stone = 2;

    private static MaterialTable Materials() => MaterialTable.ForTesting(
        //             air    dirt  stone
        isOpaque: [false, true, true],
        isFluid: [false, false, false],
        textureLayer: [0, 1, 2],
        hardness: [0f, 0.8f, 3.0f]);

    /// <summary>A tier that takes dirt and not stone, as <c>hand_pick</c> does.</summary>
    private static ToolDefinition WeakPick() => new()
    {
        Id = Identifier.Parse("overburden:test_weak"),
        RadiusMetres = 0.4f,
        HardnessThreshold = 1.0f,
        CooldownSeconds = 0.3f,
    };

    private static ToolDefinition StrongPick() => new()
    {
        Id = Identifier.Parse("overburden:test_strong"),
        RadiusMetres = 0.5f,
        HardnessThreshold = 3.5f,
        CooldownSeconds = 0.2f,
    };

    [Fact]
    public void One_voxel_is_taken_and_exactly_one_voxel_is_written()
    {
        var world = new FakeEditor { DefaultMaterial = Dirt };
        var target = new VoxelPos(3, -7, 11);

        CarveResult result = Carve.Voxel(world, Materials(), target, 1.0f);

        Assert.Equal(new CarveResult(1, 1, 0, 0, 0), result);
        Assert.True(result.IsAccounted);
        Assert.True(result.Complete);
        Assert.Equal([(target, Air)], world.Writes);
        Assert.Equal(Air, world[target]);
    }

    /// <summary>
    /// The neighbours are untouched, which is the entire point of the shape.
    /// </summary>
    /// <remarks>
    /// Stated as its own test rather than folded into the write count, because "one write"
    /// and "the right one write" are different claims and a shape that took the wrong single
    /// voxel would satisfy the first.
    /// </remarks>
    [Fact]
    public void Chipping_leaves_all_twenty_six_neighbours_alone()
    {
        var world = new FakeEditor { DefaultMaterial = Dirt };
        var target = new VoxelPos(0, 0, 0);

        Carve.Voxel(world, Materials(), target, 1.0f);

        for (int z = -1; z <= 1; z++)
        for (int x = -1; x <= 1; x++)
        for (int y = -1; y <= 1; y++)
        {
            var neighbour = new VoxelPos(x, y, z);
            if (neighbour == target)
            {
                continue;
            }

            Assert.Equal(Dirt, world[neighbour]);
        }
    }

    /// <summary>
    /// <c>Chip</c> takes the hit voxel itself, not a sphere centred on it.
    /// </summary>
    /// <remarks>
    /// The distinction that makes <c>VoxelHighlight</c>'s outline literally true in precision
    /// mode: the box drawn on screen is the thing about to be removed, rather than the centre
    /// of a bite that extends past it. A version that reused <c>Carve.SwingCentre</c> and the
    /// tool's radius would take seventeen voxels here, and this asserts one.
    /// </remarks>
    [Fact]
    public void Chip_takes_the_voxel_the_ray_hit()
    {
        var world = new FakeEditor { DefaultMaterial = Dirt };
        var target = new VoxelPos(6, 2, -4);
        var hit = new VoxelHit(target, target.Offset(0, 1, 0), (0, 1, 0), 3.25f);

        CarveResult result = Carve.Chip(world, Materials(), hit, StrongPick());

        Assert.Equal(1, result.Candidates);
        Assert.Equal(1, result.Removed);
        Assert.Equal([target], world.WrittenVoxels);
    }

    /// <summary>
    /// Precision changes how much a swing takes, never what it is strong enough to take.
    /// </summary>
    /// <remarks>
    /// The property that keeps tool tiers meaningful once a modifier exists. Without it, the
    /// modifier would be a way to mine anything one voxel at a time, and the hardness gate —
    /// which is the whole of M3's progression — would be advisory.
    /// </remarks>
    [Fact]
    public void A_tier_that_cannot_sphere_a_material_cannot_chip_it_either()
    {
        var stone = new VoxelPos(1, 1, 1);
        var hit = new VoxelHit(stone, stone.Offset(0, 1, 0), (0, 1, 0), 1.0f);

        var sphereWorld = new FakeEditor { DefaultMaterial = Stone };
        CarveResult sphere = Carve.Swing(sphereWorld, Materials(), hit, WeakPick());

        var chipWorld = new FakeEditor { DefaultMaterial = Stone };
        CarveResult chip = Carve.Chip(chipWorld, Materials(), hit, WeakPick());

        Assert.Equal(0, sphere.Removed);
        Assert.Empty(sphereWorld.Writes);

        Assert.Equal(new CarveResult(1, 0, 1, 0, 0), chip);
        Assert.True(chip.IsAccounted);
        Assert.Empty(chipWorld.Writes);

        // And the strong tier takes it both ways, so the refusal above is the gate and not a
        // shape that cannot remove anything at all.
        var strongWorld = new FakeEditor { DefaultMaterial = Stone };
        Assert.Equal(1, Carve.Chip(strongWorld, Materials(), hit, StrongPick()).Removed);
    }

    [Fact]
    public void Chipping_air_removes_nothing_and_writes_nothing()
    {
        var world = new FakeEditor { DefaultMaterial = Air };
        var target = new VoxelPos(2, 2, 2);

        CarveResult result = Carve.Voxel(world, Materials(), target, 1.0f);

        Assert.Equal(new CarveResult(1, 0, 0, 1, 0), result);
        Assert.True(result.IsAccounted);
        Assert.Empty(world.Writes);
    }

    /// <summary>
    /// An unloaded voxel is an unknown, not a refusal.
    /// </summary>
    /// <remarks>
    /// The same distinction <c>RayOutcome.Blocked</c> draws against <c>Missed</c> and
    /// <c>CarveResult.Unreachable</c> draws against <c>TooHard</c>. A chip at the edge of the
    /// loaded world that reported "too hard" would tell the player their tier is wrong when
    /// the truth is that the world has not arrived.
    /// </remarks>
    [Fact]
    public void An_unloaded_voxel_is_unreachable_and_not_too_hard()
    {
        var world = new FakeEditor { DefaultMaterial = Dirt };
        var target = new VoxelPos(40, 40, 40);
        world.Unload(target.Chunk);

        CarveResult result = Carve.Voxel(world, Materials(), target, 1.0f);

        Assert.Equal(new CarveResult(1, 0, 0, 0, 1), result);
        Assert.True(result.IsAccounted);
        Assert.False(result.Complete);
        Assert.Empty(world.Writes);
    }

    /// <summary>
    /// A world that reads but refuses the write reports the voxel rather than losing it.
    /// </summary>
    [Fact]
    public void A_refused_write_is_unreachable_rather_than_removed()
    {
        var world = new FakeEditor { DefaultMaterial = Dirt };
        var target = new VoxelPos(5, 5, 5);
        world.RefuseWrites(target.Chunk);

        CarveResult result = Carve.Voxel(world, Materials(), target, 1.0f);

        Assert.Equal(new CarveResult(1, 0, 0, 0, 1), result);
        Assert.True(result.IsAccounted);
        Assert.Equal(Dirt, world[target]);
    }

    /// <summary>
    /// NaN fails <em>open</em>, so it is refused here exactly as <c>Sphere</c> refuses it.
    /// </summary>
    /// <remarks>
    /// Every comparison with NaN is false, so <c>hardness &gt; threshold</c> never holds and
    /// the gate inverts from "takes nothing" to "takes anything". A second entry point into
    /// the carve is a second place that has to say so.
    /// </remarks>
    [Fact]
    public void A_NaN_threshold_is_refused_rather_than_taking_everything()
    {
        var world = new FakeEditor { DefaultMaterial = Stone };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => Carve.Voxel(world, Materials(), new VoxelPos(0, 0, 0), float.NaN));

        Assert.Empty(world.Writes);
    }

    /// <summary>
    /// A tier that takes nothing takes nothing, rather than throwing.
    /// </summary>
    /// <remarks>
    /// <c>float.NegativeInfinity</c> is what <c>Sphere</c>'s NaN message names as the legal way
    /// to express it, so it has to actually work.
    /// </remarks>
    [Fact]
    public void A_negative_infinity_threshold_takes_nothing()
    {
        var world = new FakeEditor { DefaultMaterial = Dirt };

        CarveResult result = Carve.Voxel(
            world, Materials(), new VoxelPos(0, 0, 0), float.NegativeInfinity);

        Assert.Equal(new CarveResult(1, 0, 1, 0, 0), result);
        Assert.Empty(world.Writes);
    }

    /// <summary>Reads fine, refuses writes, counts what it did — the same double as CarveTests.</summary>
    private sealed class FakeEditor : IVoxelEditor
    {
        private readonly Dictionary<VoxelPos, int> _voxels = [];
        private readonly HashSet<ChunkPos> _unloaded = [];
        private readonly HashSet<ChunkPos> _writeRefused = [];

        public int DefaultMaterial { get; init; } = MaterialIds.AirNumericId;

        public List<(VoxelPos Voxel, int Material)> Writes { get; } = [];

        public HashSet<VoxelPos> WrittenVoxels => Writes.Select(write => write.Voxel).ToHashSet();

        public int this[VoxelPos voxel] =>
            _voxels.TryGetValue(voxel, out int material) ? material : DefaultMaterial;

        public void Unload(ChunkPos chunk) => _unloaded.Add(chunk);

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
