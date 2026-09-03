using Overburden.Core.Fluid;
using Overburden.Core.Voxels;
using Overburden.Core.Worldgen;

namespace Overburden.Core.Tests;

/// <summary>Terrain from a predicate, for shapes that are easier stated than built.</summary>
internal sealed class PredicateTerrain(Func<VoxelPos, bool> canHold) : IFluidTerrain
{
    public bool CanHold(VoxelPos voxel) => canHold(voxel);
}

/// <summary>
/// A hollow box: open inside, solid on all six faces and everywhere outside it.
/// </summary>
/// <remarks>
/// Everything outside is solid rather than open, which is the same answer the real adapter gives
/// for an unloaded voxel — so a test that leaks water out of the box is testing the thing that
/// matters rather than an artefact of the fixture.
/// </remarks>
internal sealed class BoxTerrain(int minX, int minY, int minZ, int maxX, int maxY, int maxZ)
    : IFluidTerrain
{
    public bool CanHold(VoxelPos v) =>
        v.X >= minX && v.X <= maxX
        && v.Y >= minY && v.Y <= maxY
        && v.Z >= minZ && v.Z <= maxZ;

    public static BoxTerrain Cube(int size) => new(0, 0, 0, size - 1, size - 1, size - 1);
}

/// <summary>
/// Real terrain from the real generator, bounded by a box so water cannot fall forever.
/// </summary>
/// <remarks>
/// <b>The fixture trap this project has been caught by repeatedly is the hand-built basin</b>: a
/// tank with square walls and a flat floor makes the automaton's easy case its only case. This
/// samples <see cref="WorldGenerator.SurfaceHeightAt"/> and <see cref="WorldGenerator.IsCaveAt"/>
/// directly — no chunks, no pipeline, no meshing — so a fuzz can run over ledges, overhangs and
/// cave networks that nobody designed.
/// </remarks>
internal sealed class GeneratedTerrain : IFluidTerrain
{
    private readonly WorldGenerator _generator;
    private readonly int _minX, _minY, _minZ, _maxX, _maxY, _maxZ;
    private readonly Dictionary<(int, int), int> _surface = [];

    public GeneratedTerrain(
        WorldGenerator generator, int minX, int minY, int minZ, int maxX, int maxY, int maxZ)
    {
        _generator = generator;
        _minX = minX; _minY = minY; _minZ = minZ;
        _maxX = maxX; _maxY = maxY; _maxZ = maxZ;
    }

    public bool CanHold(VoxelPos v)
    {
        if (v.X < _minX || v.X > _maxX || v.Y < _minY || v.Y > _maxY || v.Z < _minZ || v.Z > _maxZ)
        {
            return false;
        }

        if (!_surface.TryGetValue((v.X, v.Z), out int surface))
        {
            surface = _generator.SurfaceHeightAt(v.X, v.Z);
            _surface[(v.X, v.Z)] = surface;
        }

        return v.Y > surface || _generator.IsCaveAt(v.X, v.Y, v.Z, surface);
    }

    /// <summary>The highest holdable voxel in a column, or null if the column is solid throughout.</summary>
    public VoxelPos? TopOpening(int x, int z)
    {
        for (int y = _maxY; y >= _minY; y--)
        {
            var v = new VoxelPos(x, y, z);
            if (CanHold(v))
            {
                return v;
            }
        }

        return null;
    }
}

internal static class FluidAssert
{
    /// <summary>
    /// The milestone's conservation oracle: held equals what went in, less what came out.
    /// </summary>
    /// <remarks>
    /// <b>No tolerance, in integers</b>, the same standard as M7b's "an hour in one call equals
    /// 3,600 calls of one second, to the litre". <paramref name="rejected"/> is what
    /// <see cref="FluidSimulation.Add"/> handed back because it would not fit, which the caller
    /// owns and the simulation never counted.
    /// </remarks>
    public static void Conserves(FluidSimulation sim, long rejected = 0)
    {
        Assert.Equal(
            sim.AddedMillilitres - sim.RemovedMillilitres - sim.DisplacedMillilitres,
            sim.TotalMillilitres);

        // Belt and braces on the grid's own total, which is maintained separately from the
        // simulation's ledger and would not move if Transfer ever stopped being symmetric.
        long fromCells = 0;
        foreach (VoxelPos cell in sim.Grid.HeldCells())
        {
            fromCells += sim.Grid.AmountAt(cell);
        }

        Assert.Equal(sim.TotalMillilitres, fromCells);
        Assert.True(rejected >= 0);
    }

    /// <summary>Runs to rest and asserts that it got there, and that the rest is real.</summary>
    public static FluidSettleResult SettlesAndRests(FluidSimulation sim, int maxTicks = 100_000)
    {
        FluidSettleResult result = sim.Settle(maxTicks);
        Assert.True(result.Settled, $"still active after {result.Ticks} ticks");
        Assert.Equal(0, sim.ActiveCellCount);
        Assert.True(sim.VerifyQuiescent(), "the automaton stopped with moves still available");
        return result;
    }
}
