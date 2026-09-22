namespace Overburden.Core.Lighting;

/// <summary>
/// The live sky columns by chunk column — what <see cref="Streaming.ChunkStreamer"/> holds, read
/// by things that want a column's open height and seed surface without the chunks: the solar
/// panel's openness, and the rain over the player's material (2026-09-22).
/// </summary>
public interface ISkyColumns
{
    /// <summary>The live column at (x, z), or null where no chunk of the column is loaded.</summary>
    SkyColumn? ColumnAt(int chunkX, int chunkZ);
}
