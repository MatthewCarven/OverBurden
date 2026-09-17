using Overburden.Core.Building;
using Overburden.Core.Lighting;
using Overburden.Core.Machines;
using Overburden.Core.Streaming;
using Overburden.Core.Voxels;

namespace Overburden.Game.Machines;

/// <summary>
/// The game's answer to a solar panel's one question: how many of the sixteen voxel columns
/// over its body are open to the sky, off the streamer's sky columns (M10c-2).
/// </summary>
/// <remarks>
/// <b>The light's own array, counted rather than all-or-nothing.</b> <see cref="SkyColumn.OpenAboveY"/>
/// is the first open Y of every column the light seeds skylight from; a column over the body is
/// open when that Y is at or below the body's top plus one — nothing solid stands above it.
/// <c>IsCellOpenToSky</c> demands all sixteen, which is right for seeding and wrong for a panel
/// beside a wall (the pass's fact 3); this is the same sixteen reads with a count. A chunk column
/// nobody has loaded has no <see cref="SkyColumn"/>, and the answer is <c>false</c> — the row's
/// last sample stands (<see cref="ConverterState"/>).
/// </remarks>
public sealed class StreamerSolarSky(ChunkStreamer streamer) : ISolarSky
{
    /// <summary>Columns asked and answered, and asked with nobody home — the profile's denominators.</summary>
    public long Answered { get; private set; }

    public long Unanswered { get; private set; }

    public bool TryOpenColumnsAbove(BrushCell cell, out int open)
    {
        VoxelPos origin = cell.OriginVoxel;
        SkyColumn? column = streamer.ColumnAt(origin.Chunk.X, origin.Chunk.Z);
        if (column is null || !column.IsSeeded)
        {
            Unanswered++;
            open = 0;
            return false;
        }

        int baseX = origin.X & VoxelConstants.ChunkMask;
        int baseZ = origin.Z & VoxelConstants.ChunkMask;
        int aboveTop = origin.Y + BrushCell.Voxels;
        open = 0;
        for (int dz = 0; dz < BrushCell.Voxels; dz++)
        {
            for (int dx = 0; dx < BrushCell.Voxels; dx++)
            {
                if (column.OpenAboveY(baseX + dx, baseZ + dz) <= aboveTop)
                {
                    open++;
                }
            }
        }

        Answered++;
        return true;
    }
}
