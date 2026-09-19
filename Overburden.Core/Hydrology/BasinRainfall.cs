namespace Overburden.Core.Hydrology;

/// <summary>
/// Rain falling on a watershed: what each basin's catchment collects, where the overflow goes, and
/// what leaves the world.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is DESIGN's Tier A, and its whole claim is that the level is solved rather than
/// simulated.</b> A tick costs one multiplication per basin — not per cell, not per voxel — so a
/// 1 km window's 1,376 basins at a slow tick is a few thousand operations a second, against the
/// half-million cell visits a second lighting is already licensed for by
/// <c>docs/SCALE.md</c>. Nothing here iterates water.
/// </para>
/// <para>
/// <b>Advancing an hour in one call must equal stepping it a second at a time, to the litre.</b>
/// That is the property "flew away mid-fill and came back" is made of, and it is also the whole
/// cash value of <i>slower than realtime is accuracy, not approximation</i> — a tick that is
/// allowed to be late is only allowed to be late because arriving late and arriving on time
/// compute the same number. Two things make it exact rather than nearly so:
/// </para>
/// <list type="bullet">
/// <item><b>Rainfall depth is exactly additive</b> — see <see cref="Weather"/>, where an interval's
/// depth is a sum of whole-episode products and splitting one interval into two splits the sum.
/// </item>
/// <item><b>The sub-litre remainder is carried, not dropped.</b> A basin's catchment collects
/// microlitres — a nanometre of rain over a square metre is exactly one microlitre — and only whole
/// litres are handed to the water. Discard the remainder instead and a drizzle onto a small
/// catchment rounds to nothing <i>every step forever</i>, so the basin never fills at all while an
/// hour integrated in one call fills it properly. <b>The oracle exists to catch exactly that.</b>
/// </item>
/// </list>
/// <para>
/// <b>Real rainfall is far too slow to watch, and <c>rainfallScale</c> is the honest knob for it
/// rather than a fudged rate.</b> Measured on real terrain at a 1 km window: <b>the median basin
/// fills in 35 minutes of this world's heaviest possible weather</b> — 43 mm/hour, already a
/// downpour — and since 55% of episodes are dry and most of the rest are lighter, the honest
/// expectation is several hours. A pond's surface then rises well under a millimetre a minute,
/// which nobody can judge. The scale multiplies the depth applied to the world while leaving
/// <see cref="Weather"/> stating real intensities, so the sky and the wet-surface term still read
/// a real storm; <b>M7d is where a person picks the number, and the bench's <c>fill min</c> column
/// is what they should pick it against.</b> Multiplication is integral, so every exactness claim
/// above survives it.
/// </para>
/// <para>
/// Rain is uniform over the window. Local weather is not a thing this milestone has any use for,
/// and the seam if it ever is: the depth is per-basin already — it is the <i>rate</i> that is
/// global.
/// </para>
/// </remarks>
public sealed class BasinRainfall
{
    private const long MicrolitresPerLitre = 1_000_000;

    private readonly BasinWater _water;
    private readonly ulong _seed;
    private readonly int _rainfallScale;
    private readonly long[] _catchmentSquareMetres;
    private readonly long[] _pendingMicrolitres;

    public BasinRainfall(
        Watershed watershed,
        BasinWater water,
        ulong seed,
        long startMillis = 0,
        int rainfallScale = 1)
    {
        ArgumentNullException.ThrowIfNull(watershed);
        ArgumentNullException.ThrowIfNull(water);
        ArgumentOutOfRangeException.ThrowIfNegative(startMillis);
        ArgumentOutOfRangeException.ThrowIfLessThan(rainfallScale, 1);

        if (water.Tree.LeafCount != watershed.Basins.Count)
        {
            throw new ArgumentException(
                "The water and the watershed describe different worlds.", nameof(water));
        }

        _water = water;
        _seed = seed;
        _rainfallScale = rainfallScale;
        TimeMillis = startMillis;

        _catchmentSquareMetres = new long[watershed.Basins.Count];
        _pendingMicrolitres = new long[watershed.Basins.Count];
        for (int basin = 0; basin < _catchmentSquareMetres.Length; basin++)
        {
            _catchmentSquareMetres[basin] = watershed.Basins[basin].CatchmentSquareMetres;
        }
    }

    /// <summary>World time this has been integrated up to.</summary>
    public long TimeMillis { get; private set; }

    /// <summary>
    /// Microlitres of this sky's rain that fell as Tier B cells instead of into a basin and are
    /// still to be paid for out of the rain that follows (2026-09-19).
    /// </summary>
    /// <remarks>
    /// <b>A raindrop the automaton can carry is still rain, so it comes out of the rain.</b>
    /// Matthew's <i>"rain to randomly drop tier b cells from the sky … to make you think about the
    /// water runoff"</i>: a cell of 15,625 mL put into the sky over the drawn box, which falls,
    /// runs off and is handed back to a basin, or into a mine — and it is not water from nothing
    /// (M8-PLAN's rule for a debug source is a reported term; this is not a debug source). The
    /// cell's volume is owed here and paid before any litre of the next rain reaches a basin, in
    /// microlitres, exact; the world's total is unchanged by a drop the moment it is paid, and
    /// what is still owed is printed. Global rather than per basin because rain is uniform over the
    /// window and a drop is one cell against megalitres — which basin is a few litres lower is
    /// noise, and a per-basin debt would sit unpaid on a basin the drops happened to favour.
    /// </remarks>
    public long OwedMicrolitres { get; private set; }

    /// <summary>Microlitres paid off <see cref="OwedMicrolitres"/> since this object was made.</summary>
    public long PaidMicrolitres { get; private set; }

    /// <summary>Owes <paramref name="microlitres"/> of rain to Tier B — a cell that fell from this sky.</summary>
    public void Divert(long microlitres)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(microlitres);
        OwedMicrolitres += microlitres;
    }

    /// <summary>Litres that have left the world since this object was made.</summary>
    public long SpilledLitres { get; private set; }

    /// <summary>Litres collected but not yet whole, held so nothing is lost to rounding.</summary>
    public long PendingMicrolitres(int basin) => _pendingMicrolitres[basin];

    /// <summary>
    /// Rains everything that fell between <see cref="TimeMillis"/> and
    /// <paramref name="timeMillis"/> onto the world, and returns what left it.
    /// </summary>
    public long AdvanceTo(long timeMillis)
    {
        if (timeMillis < TimeMillis)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeMillis), timeMillis, "World time does not run backwards.");
        }

        long depth = Weather.DepthNanometresOver(_seed, TimeMillis, timeMillis) * _rainfallScale;
        TimeMillis = timeMillis;
        if (depth == 0)
        {
            return 0;
        }

        long spilled = 0;
        for (int basin = 0; basin < _pendingMicrolitres.Length; basin++)
        {
            // Exact: nanometres × square metres is microlitres, with nothing rounded away.
            _pendingMicrolitres[basin] += depth * _catchmentSquareMetres[basin];

            // What Tier B was given from this sky is paid first, in microlitres, so the drops
            // and the basins share one rain and an hour in one call still equals an hour in
            // steps: the debt is paid from the same microlitres either way.
            if (OwedMicrolitres > 0)
            {
                long pay = Math.Min(OwedMicrolitres, _pendingMicrolitres[basin]);
                _pendingMicrolitres[basin] -= pay;
                OwedMicrolitres -= pay;
                PaidMicrolitres += pay;
            }

            long litres = _pendingMicrolitres[basin] / MicrolitresPerLitre;
            if (litres == 0)
            {
                continue;
            }

            _pendingMicrolitres[basin] -= litres * MicrolitresPerLitre;
            spilled += _water.Add(basin, litres);
        }

        SpilledLitres += spilled;
        return spilled;
    }
}
