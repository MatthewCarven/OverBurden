namespace Overburden.Core.Structure;

/// <summary>
/// How fast a collapse event runs at a given age (COLLAPSE-PLAN decision 4): flat for a grace, then
/// doubling at a fixed period — the release rate, the fall's speed and the mino's size all read the
/// one multiplier. Matthew's brief: easy to rescue at first, accelerating from ~15 s, past rebuilding
/// by ~30 s; 2–4 pieces a second at the start, the fall 1 m/s climbing to about 10.
/// </summary>
/// <remarks>
/// <para>
/// <b>By age, not by pieces released.</b> Tetris levels up on lines cleared, and the pass's first
/// draft did the same with pieces; his answer was a clock ("after like 30 seconds you or even say 15
/// seconds like it should start accelerating"), and a clock is also what makes the rescue window the
/// same size whatever the structure is.
/// </para>
/// <para>
/// <b>The mino grows with the multiplier</b> (decision 1): a piece is built from 1-voxel minos while
/// the event is young, 2 × 2 × 2 once it doubles, the 1 m brush cell once it is past saving. Without
/// it his 6,200-voxel tower is ~1,550 voxel tetracubes, eight minutes at 3 a second.
/// </para>
/// <para>Every number here is an estimate from the pass; his play moves them.</para>
/// </remarks>
public sealed record CollapsePace
{
    /// <summary>The shipped table.</summary>
    public static readonly CollapsePace Default = new();

    private readonly double _graceSeconds = 15.0;
    private readonly double _doubleSeconds = 5.0;
    private readonly double _startReleasesPerSecond = 3.0;
    private readonly double _startFallMetresPerSecond = 1.0;
    private readonly double _maxFallMetresPerSecond = 10.0;
    private readonly double _seriousSeconds = 30.0;

    /// <summary>Seconds at the starting pace before the doubling begins.</summary>
    public double GraceSeconds
    {
        get => _graceSeconds;
        init => _graceSeconds = NonNegative(value, nameof(GraceSeconds));
    }

    /// <summary>Seconds per doubling once the grace is over.</summary>
    public double DoubleSeconds
    {
        get => _doubleSeconds;
        init => _doubleSeconds = Positive(value, nameof(DoubleSeconds));
    }

    /// <summary>Pieces a second at the start — the mean of a seeded 2/3–4/3 spread, his "2, 3 or 4".</summary>
    public double StartReleasesPerSecond
    {
        get => _startReleasesPerSecond;
        init => _startReleasesPerSecond = Positive(value, nameof(StartReleasesPerSecond));
    }

    /// <summary>The fall's speed at the start, metres a second.</summary>
    public double StartFallMetresPerSecond
    {
        get => _startFallMetresPerSecond;
        init => _startFallMetresPerSecond = Positive(value, nameof(StartFallMetresPerSecond));
    }

    /// <summary>The fall's speed cap, metres a second.</summary>
    public double MaxFallMetresPerSecond
    {
        get => _maxFallMetresPerSecond;
        init => _maxFallMetresPerSecond = Positive(value, nameof(MaxFallMetresPerSecond));
    }

    /// <summary>The age at which an event is serious: past saving, and the chimney opens (decision 9).</summary>
    public double SeriousSeconds
    {
        get => _seriousSeconds;
        init => _seriousSeconds = NonNegative(value, nameof(SeriousSeconds));
    }

    /// <summary>The multiplier at which the mino becomes 2 voxels on a side.</summary>
    public const double HalfMetreMinoAt = 2.0;

    /// <summary>The multiplier at which the mino becomes the 1 m brush cell.</summary>
    public const double MetreMinoAt = 8.0;

    /// <summary>1 at the start, then ×2 every <see cref="DoubleSeconds"/> past <see cref="GraceSeconds"/>.</summary>
    public double Multiplier(double ageSeconds) =>
        ageSeconds <= _graceSeconds ? 1.0 : Math.Pow(2.0, (ageSeconds - _graceSeconds) / _doubleSeconds);

    /// <summary>The mean release rate at this age, pieces a second. No cap: a frame budget bounds the wire.</summary>
    public double ReleasesPerSecond(double ageSeconds) => _startReleasesPerSecond * Multiplier(ageSeconds);

    /// <summary>The fall's speed at this age, metres a second, capped.</summary>
    public double FallMetresPerSecond(double ageSeconds) =>
        Math.Min(_maxFallMetresPerSecond, _startFallMetresPerSecond * Multiplier(ageSeconds));

    /// <summary>Voxels along a mino's edge at this age: 1, 2 or 4.</summary>
    public int MinoVoxels(double ageSeconds)
    {
        double m = Multiplier(ageSeconds);
        return m >= MetreMinoAt ? 4 : m >= HalfMetreMinoAt ? 2 : 1;
    }

    /// <summary>Whether the event is past saving at this age.</summary>
    public bool IsSerious(double ageSeconds) => ageSeconds >= _seriousSeconds;

    /// <summary>
    /// Seconds from one release to the next at this age, spread by <paramref name="unit"/> (a seeded
    /// draw in [0, 1)) over 2/3–4/3 of the mean rate — at the start, 2 to 4 pieces a second.
    /// </summary>
    public double ReleaseIntervalSeconds(double ageSeconds, float unit)
    {
        if (!(unit >= 0f && unit < 1f))
        {
            throw new ArgumentOutOfRangeException(nameof(unit), unit, "A unit draw is in [0, 1).");
        }

        double rate = ReleasesPerSecond(ageSeconds) * ((2.0 / 3.0) + (unit * 2.0 / 3.0));
        return 1.0 / rate;
    }

    /// <summary>Seconds a piece takes to step one mino down at this age: a mino's edge over the fall's speed.</summary>
    public double StepSeconds(double ageSeconds, int minoVoxels) =>
        minoVoxels / (double)Voxels.VoxelConstants.VoxelsPerMetre / FallMetresPerSecond(ageSeconds);

    private static double Positive(double value, string name) =>
        double.IsFinite(value) && value > 0 ? value : throw new ArgumentOutOfRangeException(name, value, "Must be finite and positive.");

    private static double NonNegative(double value, string name) =>
        double.IsFinite(value) && value >= 0 ? value : throw new ArgumentOutOfRangeException(name, value, "Must be finite and non-negative.");
}
