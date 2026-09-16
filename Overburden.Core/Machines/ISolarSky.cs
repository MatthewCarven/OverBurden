using Overburden.Core.Building;

namespace Overburden.Core.Machines;

/// <summary>
/// What a solar converter asks of the world at a boundary: how many of the sixteen voxel columns
/// over a body are open to the sky (M10c, decision 2).
/// </summary>
/// <remarks>
/// <b>A count, not the light's all-or-nothing rule.</b> <c>SkyColumn.IsCellOpenToSky</c> demands
/// every column open, which is right for seeding skylight (under-seeding is recoverable) and
/// wrong for a panel beside a wall, which would read dark. The panel's rule is the same sixteen
/// reads with a count, off the same array; the game answers it from <c>ChunkStreamer.ColumnAt</c>
/// and a test from a dictionary. False means the column is not loaded and the row's last sample
/// stands — <see cref="ConverterState"/>.
/// </remarks>
public interface ISolarSky
{
    /// <summary>The open columns over <paramref name="cell"/>'s top, 0..16; false when nobody has the column loaded.</summary>
    bool TryOpenColumnsAbove(BrushCell cell, out int open);
}
