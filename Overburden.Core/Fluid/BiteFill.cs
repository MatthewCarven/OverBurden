using Overburden.Core.Diagnostics;

namespace Overburden.Core.Fluid;

/// <summary>
/// Which watershed samples are mostly a dug pit that is not yet full to the seed — the ones the
/// Tier A plane must not be drawn over (M8-PLAN, "The lake over a quarry", 2026-09-27).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why.</b> The plane draws over a bite because the water above the seed there is the lake's,
/// and it assumes the bite below is full (M8d-5's reversal of decision 7). A 4 × 4 pit is full in
/// 34 ticks; his quarry on TajMahal filled at ~1.7 kL/s and stayed under-filled for minutes, so the
/// sheet hung over dry floor. <i>"the sheets like sometimes they construct over empty areas"</i>.
/// </para>
/// <para>
/// <b>A dug column is a pit portal.</b> A column dug at its seed height has a holdable voxel at the
/// seed opening upward — <see cref="Portal.IsPit"/> — and the index already keeps every one, filed
/// under its sample, in edited chunks only. So an untouched lake costs nothing here. A column is
/// <i>unfilled</i> when its seed voxel could still take a whole litre, which is the drain's own
/// "full to the litre": a cell drawn full sits at 625 mL of room and counts as full.
/// </para>
/// <para>
/// <b>Half the footprint</b> (decision 2): a sample is unfilled when more than half its
/// <c>cell × cell</c> columns are. "Any" would drop the plane over a rim sample that is mostly lake;
/// "all" would keep a sheet over a pit that is mostly dry.
/// </para>
/// <para>
/// <b>Refreshed at the fluid boundary, read by the surface between them.</b> Tier B's grid belongs
/// to the tick's worker between boundaries, and the surface rebuilds on its own 0.25 s timer, so
/// the answers are taken where the drain reads the same cells and kept until the next boundary.
/// </para>
/// <para>
/// <b>Cost, measured on his quarry:</b> reading every pit of every sample was 6.8 ms a refresh,
/// 14 ms at worst, against a budget of 1. So a refresh asks only the samples the caller says are
/// wet — the plane is drawn nowhere else, and the ring only dilates within one lake — and a sample
/// stops reading once its answer is decided: more than half dry, or too many full for the rest to
/// make it so. <see cref="CellsRead"/> is the denominator. That halved it (2.8 ms mean, 5.3 worst),
/// and the rest is the grid's panel lookup a cell, so <b>a refresh asks one slice of the samples in
/// <see cref="Slices"/></b>, by a hash of the sample: every sample is asked once every four
/// boundaries, two seconds at 2 Hz, against a quarry that fills over minutes.
/// </para>
/// </remarks>
public sealed class BiteFill
{
    private readonly PortalIndex _index;
    private readonly FluidGrid _grid;
    private readonly HashSet<(int X, int Z)> _unfilled = [];
    private readonly List<(int X, int Z)> _stale = [];

    /// <summary>Which slice a sample is asked in: a hash, so neighbours spread across slices.</summary>
    private static int SliceOf((int X, int Z) sample, int slices) =>
        slices == 1 ? 0 : (int)((uint)((sample.X * 73856093) ^ (sample.Z * 19349663)) % (uint)slices);

    public BiteFill(PortalIndex index, FluidGrid grid)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(grid);
        _index = index;
        _grid = grid;
    }

    /// <summary>How many boundaries it takes to ask every sample once. 1 asks all of them every refresh.</summary>
    public int Slices { get; init; } = 4;

    /// <summary>Samples found unfilled, as of each one's last ask.</summary>
    public int UnfilledSamples => _unfilled.Count;

    /// <summary>Seed cells read on the last refresh — at most a little over half a footprint per sample asked.</summary>
    public int CellsRead { get; private set; }

    /// <summary>Samples with pits the last refresh asked about, and those it skipped as dry of lake.</summary>
    public int SamplesAsked { get; private set; }

    /// <inheritdoc cref="SamplesAsked"/>
    public int SamplesSkipped { get; private set; }

    /// <summary>Refreshes, over the lifetime.</summary>
    public long Refreshes { get; private set; }

    /// <summary>True when the world-lattice sample is mostly a dug pit not yet full to the seed.</summary>
    public bool IsUnfilled(int worldSampleX, int worldSampleZ) => _unfilled.Contains((worldSampleX, worldSampleZ));

    private static readonly int WatchRefresh = CallWatch.Register("bite fill: refresh");

    /// <summary>Re-reads every pit portal's seed cell. At the fluid boundary, where the grid is the sim thread's.</summary>
    /// <param name="wet">
    /// Whether a world-lattice sample stands under a lake that holds anything; null asks every
    /// sample. The game passes the drain's own test, so a dry excavation costs nothing.
    /// </param>
    public void Refresh(Func<int, int, bool>? wet = null)
    {
        using CallWatch.Scope callWatch = CallWatch.Time(WatchRefresh);
        int slices = Math.Max(1, Slices);
        int slice = (int)(Refreshes % slices);
        CellsRead = 0;
        SamplesAsked = 0;
        SamplesSkipped = 0;
        Refreshes++;

        // This slice's answers are re-asked from nothing, including a sample whose pits have all
        // gone (filled with rock) since its last ask — its entry must not outlive it.
        _stale.Clear();
        foreach ((int X, int Z) sample in _unfilled)
        {
            if (SliceOf(sample, slices) == slice)
            {
                _stale.Add(sample);
            }
        }

        foreach ((int X, int Z) sample in _stale)
        {
            _unfilled.Remove(sample);
        }

        int footprint = _index.CellVoxels * _index.CellVoxels;
        int half = footprint / 2;
        foreach ((int X, int Z) sample in _index.Samples)
        {
            if (SliceOf(sample, slices) != slice)
            {
                continue;
            }

            if (wet is not null && !wet(sample.X, sample.Z))
            {
                SamplesSkipped++;
                continue;
            }

            SamplesAsked++;
            int dry = 0;
            int full = 0;
            foreach (Portal portal in _index.Under(sample.X, sample.Z))
            {
                if (!portal.IsPit)
                {
                    continue;
                }

                CellsRead++;
                if (_grid.FreeAt(portal.Inside) >= FluidConstants.MillilitresPerLitre)
                {
                    // Decided: more than half the footprint is a dry pit.
                    if (++dry > half)
                    {
                        _unfilled.Add(sample);
                        break;
                    }
                }
                else if (++full >= footprint - half)
                {
                    // Decided the other way: the columns left cannot make more than half dry.
                    break;
                }
            }
        }
    }
}
