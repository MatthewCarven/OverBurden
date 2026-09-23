using System.Collections.Generic;
using Overburden.Core.Hydrology;
using Overburden.Core.Navigation;

namespace Overburden.Game.Ui;

/// <summary>
/// Tier A's answer about standing water, in the two parts the map needs it in.
/// </summary>
/// <remarks>
/// <para>
/// <b>The distinction between "dry" and "nobody has looked" is the whole of this class.</b>
/// <c>WaterSystem.TryBasinAtSample</c> already keeps them apart — M10a's pass made it do so for the
/// unloaded-outlet rule — and the map wants the same split for the same kind of reason: a
/// confident dry plain drawn over unanalysed ground is the map lying about the one thing it cannot
/// see. The window is 512 m and the map is 2 km, so most of every map is the second answer.
/// </para>
/// <para>
/// <b>Levels are memoised per basin</b>, because <c>LevelAt</c> walks the merge tree and a map is
/// sixteen thousand pixels over a few dozen basins. Built fresh for each map, which is what keeps
/// it honest about a lake that is rising.
/// </para>
/// </remarks>
internal sealed class MapWater(WaterSystem water) : IMapWater
{
    private readonly Dictionary<int, float?> _levels = [];

    /// <summary>Clears the memo. Called once per map build, never per pixel.</summary>
    public void Forget() => _levels.Clear();

    public bool Covers(int worldVoxelX, int worldVoxelZ) =>
        water.TryBasinAtVoxel(worldVoxelX, worldVoxelZ, out _);

    public bool TryLevelVoxels(int worldVoxelX, int worldVoxelZ, out float levelVoxels)
    {
        levelVoxels = 0.0f;

        if (!water.TryBasinAtVoxel(worldVoxelX, worldVoxelZ, out int basin))
        {
            return false;
        }

        if (!_levels.TryGetValue(basin, out float? level))
        {
            double? held = water.LevelAt(basin);
            level = held is double metres ? (float)metres : null;
            _levels[basin] = level;
        }

        if (level is not float found)
        {
            return false;
        }

        levelVoxels = found;
        return true;
    }

}
