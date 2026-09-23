namespace Overburden.Core.Navigation;

/// <summary>A thing worth finding again, at a world position.</summary>
/// <param name="VoxelX">Where it is.</param>
/// <param name="VoxelZ">Where it is.</param>
/// <param name="Colour">What it is. The panel picks by kind.</param>
public readonly record struct MapPin(int VoxelX, int VoxelZ, MapColour Colour);

/// <summary>
/// What the map can ask about standing water.
/// </summary>
/// <remarks>
/// <b>Two questions, not one, and the difference is the whole reason this interface exists.</b>
/// "There is no water here" and "nobody has analysed here" are different facts, and a map that
/// folded them together would draw a confident dry plain over ground it knows nothing about. Tier A
/// analyses a 512 m window around the player and the map is four times that wide, so most of every
/// map is the second answer — which is why <see cref="Covers"/> is asked of every pixel and the
/// boundary is drawn.
/// </remarks>
public interface IMapWater
{
    /// <summary>Whether anybody has analysed the ground under this column.</summary>
    bool Covers(int worldVoxelX, int worldVoxelZ);

    /// <summary>The surface of any water standing here, in voxels. False where there is none.</summary>
    bool TryLevelVoxels(int worldVoxelX, int worldVoxelZ, out float levelVoxels);
}

/// <summary>
/// Everything drawn over the terrain: your mine, your machines, and the water.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is what makes it a map of a mining operation rather than a map of a landscape</b>, and
/// all three are nearly free. Where you have dug is the stored-chunk set the pipeline already
/// keeps; the machines are rows restored whole at open; only the water costs a lookup a pixel, and
/// only inside the window.
/// </para>
/// <para>
/// <b>Every overlay is anchored to the world, so every one of them has to survive the
/// bearing-independence oracle</b> — a pin that drifts when you turn is worse than no pin, because
/// you would walk to it. <c>TheSameGroundIsTheSameColourWhicheverWayYouFace</c> runs with the
/// overlays on for exactly that reason.
/// </para>
/// </remarks>
public sealed record MapOverlay
{
    /// <summary>Nothing drawn over the terrain. Renders exactly as the terrain alone.</summary>
    public static readonly MapOverlay None = new();

    /// <summary>The water, or null to draw none and mark no boundary.</summary>
    public IMapWater? Water { get; init; }

    /// <summary>
    /// Whether a chunk column has been dug, by chunk coordinates. Null draws no mine.
    /// </summary>
    /// <remarks>
    /// Per chunk column rather than per voxel, because that is the granularity the store keeps and
    /// because an 8 m column is half a pixel at the shipped scale — finer would be a lie the map
    /// cannot draw anyway.
    /// </remarks>
    public Func<int, int, bool>? IsDug { get; init; }

    /// <summary>Machines, and anything else worth a dot.</summary>
    public IReadOnlyList<MapPin> Pins { get; init; } = [];

    public bool IsEmpty => Water is null && IsDug is null && Pins.Count == 0;
}
