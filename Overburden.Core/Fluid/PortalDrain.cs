using Overburden.Core.Hydrology;
using Overburden.Core.Voxels;

namespace Overburden.Core.Fluid;

/// <summary>
/// The A → B seam itself: once a tick, every portal whose outside voxel a lake stands over draws
/// whole litres out of that lake into its inside cell, as fast as the cell has room.
/// </summary>
/// <remarks>
/// <para>
/// <b>Tier A never learns the hole exists.</b> It loses litres through <see cref="WaterSystem.Draw"/>
/// and re-solves its level; the portal index knows where the holes are, and this class is the
/// only thing that connects the two. A perfect automaton no basin ever hands water to changes
/// nothing on screen — M8-PLAN's "the headline is the seam, not the CA".
/// </para>
/// <para>
/// <b>Walked per wet sample, not per portal.</b> The index files portals under the sample their
/// outside voxel sits in; a sample maps to a basin through the installed window's labels, and a
/// basin holding nothing is skipped before any of its portals are looked at. A dry excavation
/// costs nothing however large. The covered test is on the outside voxel, <c>level &gt;
/// Outside.Y</c>, re-read per portal so the flow stops at the face and not one draw later.
/// </para>
/// <para>
/// <b>Whole litres, room first, then the lake, then the cell — and the three must agree to the
/// litre.</b> A cell has 15,625 mL of capacity, so a full draw is 15 L and the cell then sits at
/// 625 mL of room until it drains: "full to the litre", by design, and one portal voxel is 30 L/s
/// at 2 Hz, which is M8a's measured 29. The lake may hand back less than the room asks for; what
/// it hands back is what the cell is given, and a cell refusing water the index said it could
/// hold is an index that missed an edit, which throws rather than refunding — the wiring is the
/// thing that would be wrong, and a refund would hide it.
/// </para>
/// <para>
/// Samples are visited in sorted order rather than dictionary order, so the same state draws the
/// same way twice; M8b-4's oracle is "the same millilitres in the same cells".
/// </para>
/// </remarks>
public sealed class PortalDrain
{
    private readonly PortalIndex _index;
    private readonly WaterSystem _water;
    private readonly FluidSimulation _fluid;
    private readonly List<(int X, int Z)> _samples = [];

    public PortalDrain(PortalIndex index, WaterSystem water, FluidSimulation fluid)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(water);
        ArgumentNullException.ThrowIfNull(fluid);

        _index = index;
        _water = water;
        _fluid = fluid;
    }

    /// <summary>Millilitres that have crossed the seam. Equals <see cref="WaterSystem.DrawnLitres"/> × 1,000, always.</summary>
    public long DrawnMillilitres { get; private set; }

    /// <summary>Portal draws that moved water, over the lifetime.</summary>
    public long Draws { get; private set; }

    /// <summary>Samples with portals that mapped to a wet basin on the last pass.</summary>
    public int WetSamplesLastPass { get; private set; }

    /// <summary>Portals whose outside voxel was under a lake on the last pass, drawn or not.</summary>
    public int CoveredPortalsLastPass { get; private set; }

    /// <summary>One pass over every portal. Returns the millilitres drawn.</summary>
    public long Draw()
    {
        WetSamplesLastPass = 0;
        CoveredPortalsLastPass = 0;

        if (_water.Water is null || _index.Count == 0)
        {
            return 0;
        }

        _samples.Clear();
        _samples.AddRange(_index.Samples);
        _samples.Sort();

        long drawnThisPass = 0;
        foreach ((int X, int Z) sample in _samples)
        {
            int basin = _water.BasinAtSample(sample.X, sample.Z);
            if (basin == Watershed.Outlet || _water.Water.StoredIn(basin) == 0)
            {
                continue;
            }

            WetSamplesLastPass++;
            foreach (Portal portal in _index.Under(sample.X, sample.Z))
            {
                // Re-read per portal: the draws before this one lowered the lake.
                if (_water.Water.StoredIn(basin) == 0
                    || _water.Water.LevelAt(basin) <= portal.Outside.Y)
                {
                    continue;
                }

                CoveredPortalsLastPass++;

                long room = _fluid.Grid.FreeAt(portal.Inside);
                long litres = room / FluidConstants.MillilitresPerLitre;
                if (litres == 0)
                {
                    continue;
                }

                long drawn = _water.Draw(basin, litres);
                if (drawn == 0)
                {
                    continue;
                }

                long millilitres = drawn * FluidConstants.MillilitresPerLitre;
                long refused = _fluid.Add(portal.Inside, millilitres);
                if (refused != 0)
                {
                    throw new InvalidOperationException(
                        $"Portal {portal} refused {refused} mL it had room for: the index holds a "
                        + "portal the terrain cannot hold, so an edit never reached it.");
                }

                DrawnMillilitres += millilitres;
                drawnThisPass += millilitres;
                Draws++;
            }
        }

        return drawnThisPass;
    }
}
