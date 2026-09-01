namespace MinerCraft.Core.Hydrology;

/// <summary>
/// One depression: where it is, how much it holds, where it overflows to.
/// </summary>
/// <remarks>
/// <para>
/// <b>A basin is identified by its pit, in world coordinates — never by <see cref="Index"/>.</b>
/// The index is an array position in one analysis of one window, and the window is centred on the
/// player, so every index renumbers the moment they walk far enough. Volume persisted against an
/// index would silently reattach to a different basin: the lake drains, the valley next door
/// floods, and nothing fails anywhere. <see cref="PitX"/> and <see cref="PitZ"/> are a property of
/// the terrain, deterministic from the seed, and identical across window moves, sessions and
/// machines — which is non-negotiable 2's argument (identity is a name the world owns, not a
/// number the process assigned) turning up somewhere nobody expected to need it.
/// </para>
/// <para>
/// <b>The saddle is a leaf saddle.</b> Two adjacent depressions inside one larger bowl each spill
/// into the other at the ridge between them, well below the level the combined lake would reach —
/// so <see cref="SaddleHeight"/> is where <i>this</i> basin stops filling on its own, not where
/// the water stops rising. Above it the merge tree takes over, and that is M7a-2.
/// </para>
/// </remarks>
public sealed class Basin
{
    internal Basin(
        int index,
        int pitX,
        int pitZ,
        int pitHeight,
        int saddleHeight,
        int spillTo,
        int catchmentSamples,
        long squareMetresPerCell,
        VolumeCurve curve)
    {
        Index = index;
        PitX = pitX;
        PitZ = pitZ;
        PitHeight = pitHeight;
        SaddleHeight = saddleHeight;
        SpillTo = spillTo;
        CatchmentSamples = catchmentSamples;
        CatchmentSquareMetres = catchmentSamples * squareMetresPerCell;
        Curve = curve;
    }

    /// <summary>Position in this analysis only. <b>Not an identity</b> — see the remarks.</summary>
    public int Index { get; }

    /// <summary>World voxel X of the deepest sample. Half of the basin's identity.</summary>
    public int PitX { get; }

    /// <summary>World voxel Z of the deepest sample. The other half.</summary>
    public int PitZ { get; }

    /// <summary>Voxel Y of the deepest sample.</summary>
    public int PitHeight { get; }

    /// <summary>Voxel Y this basin overtops at, on its own.</summary>
    public int SaddleHeight { get; }

    /// <summary>
    /// The basin this one overflows into, or <see cref="Watershed.Outlet"/> when the water leaves
    /// the window.
    /// </summary>
    public int SpillTo { get; }

    /// <summary>Samples that drain here, lake and hillside together. Rain falls on all of them.</summary>
    public int CatchmentSamples { get; }

    /// <summary>What <see cref="CatchmentSamples"/> is worth in ground area.</summary>
    public long CatchmentSquareMetres { get; }

    /// <summary>Volume against level, capped at <see cref="SaddleHeight"/>.</summary>
    public VolumeCurve Curve { get; }

    /// <summary>Litres this basin holds before it spills.</summary>
    public long CapacityLitres => Curve.CapacityLitres;
}
