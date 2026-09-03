using Overburden.Core.Voxels;

namespace Overburden.Core.Streaming;

/// <summary>Handle for a viewer registered with a <see cref="ChunkStreamer"/>.</summary>
/// <remarks>
/// A wrapper rather than a bare <c>int</c> so that a chunk count, a radius or a job index
/// cannot be passed where a viewer is meant. The same reasoning as
/// <see cref="ChunkPos"/> — the compiler is cheaper than the debugger.
/// </remarks>
public readonly record struct ViewerId(int Value)
{
    public override string ToString() => $"Viewer#{Value}";
}

/// <summary>
/// Somewhere terrain needs to exist around: a position and how far to load on each axis.
/// </summary>
/// <remarks>
/// <para>
/// <b>A viewer is not a camera.</b> It is three numbers, it lives in
/// <c>Overburden.Core</c>, and the Godot layer's entire involvement is reporting where the
/// player is. That keeps the no-Godot-in-Core seam intact, and — not by coincidence —
/// makes multiplayer a matter of registering a second viewer rather than a special case.
/// </para>
/// <para>
/// Position is in chunks, not metres. Quantising at the boundary is what gives streaming
/// its hysteresis for free: the box only changes when the viewer crosses into a new chunk,
/// so a player standing near an edge cannot thrash load and unload.
/// </para>
/// </remarks>
public readonly record struct Viewer(ChunkPos Position, int HorizontalRadius, int VerticalRadius)
{
    /// <summary>The chunks this viewer wants loaded.</summary>
    public ChunkBox Box => ChunkBox.Around(Position, HorizontalRadius, VerticalRadius);

    public override string ToString() =>
        $"Viewer({Position.X},{Position.Y},{Position.Z} r{HorizontalRadius}h/{VerticalRadius}v)";
}
