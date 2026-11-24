using Overburden.Core.Voxels;

namespace Overburden.Core.Machines;

/// <summary>
/// What the machine tick asks about the collapse (COLLAPSE-PLAN part f, f-1): three O(1) questions
/// a chunk, so the tick never holds the director or the scheduler. The game hands it one over
/// both (f-2); a test hands it a fake.
/// </summary>
/// <remarks>
/// Asked at the tick, which runs before the boundary's collapse sweep, so every answer is the last
/// boundary's — one boundary (0.5 s) late against a 10–20 s fuse, accepted (decision 11).
/// </remarks>
public interface ICollapseSense
{
    /// <summary>Whether the chunk's fuse is burning: its at-risk set armed, and not yet an event.</summary>
    bool Armed(ChunkPos chunk);

    /// <summary>Whether the chunk is a member of a live event: its pieces are falling.</summary>
    bool Member(ChunkPos chunk);

    /// <summary>World milliseconds left on the chunk's fuse, or null when it has none.</summary>
    long? FuseLeft(ChunkPos chunk);
}

/// <summary>What tripped a sensor at a boundary, the stronger first in a reading (decision 7).</summary>
public enum SensorTrip : byte
{
    /// <summary>No fuse and no event within its radius — or no sense handed to the tick.</summary>
    Quiet,

    /// <summary>A fuse burning within its radius: the earliest warning the game has (decision 1).</summary>
    Fuse,

    /// <summary>An event within its radius: the roof is coming down. Beats a fuse in a reading.</summary>
    Event,
}

/// <summary>
/// One sensor's reading at the last boundary (f-1's re-read, build choice 7): what tripped it, how far
/// away in chunks (3D Chebyshev), and for a fuse the time it has left. Kept on the row for the session,
/// never saved — f-2's panel and lines read it; the tick itself needs only whether it tripped.
/// </summary>
/// <param name="Trip">An event if any member is within the radius, else a fuse if any is burning there, else quiet.</param>
/// <param name="DistanceChunks">The nearest member's distance for an event; the soonest fuse's for a fuse; 0 when quiet.</param>
/// <param name="FuseLeftMillis">The soonest fuse's time left within the radius, whatever tripped it; null when none burns there.</param>
public readonly record struct SensorReading(SensorTrip Trip, int DistanceChunks, long? FuseLeftMillis)
{
    /// <summary>Whether the sensor raises its channel's signal.</summary>
    public bool Tripped => Trip != SensorTrip.Quiet;
}
