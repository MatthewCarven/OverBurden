using Overburden.Core.Hydrology;
using Overburden.Core.Lighting;
using Overburden.Core.Voxels;

namespace Overburden.Core.Fluid;

/// <summary>
/// The B → A seam itself: once a tick, every cell that came to rest on the analytic ground hands
/// what it holds to the basin under it and drains — DESIGN's <i>stable cells sleep and merge into
/// a Tier A body</i>, literally.
/// </summary>
/// <remarks>
/// <para>
/// <b>Water on the analytic ground is already Tier A's, by Tier A's own model.</b> Rain landing
/// anywhere on a catchment is litres in that catchment's basin the moment it lands; Tier A never
/// represents water <i>on</i> a hillside, only water <i>in</i> a pit. So a Tier B cell resting on
/// the plane the ground stands on — <c>SurfaceHeightAt + 1</c>, the plane <c>HeightField</c>
/// samples and <c>SkyColumn.Seed</c> pins — is rain that has landed, and handing it to the basin
/// under its sample is the same delivery <c>BasinRainfall</c> makes. The spill soaks into the
/// ground where it stops moving and stands up again as the lake's level, which is where Tier A
/// says it always was.
/// </para>
/// <para>
/// <b>Only resting cells, and only the cell on the plane.</b> Water in motion stays Tier B, so a
/// pour is seen spreading before it merges; and only the cell standing directly on the ground
/// hands off, because the cell above it falls into the space the hand-off leaves and hands off in
/// turn — one cell a tick a column, the automaton's own rate. A cell resting on a placed block
/// above the ground is not on the plane and is never handed, which is what keeps a tank built on
/// the surface holding water: the seam reads the <i>seed</i> surface, never the edited one. A
/// tank whose floor is the natural ground leaks into the basin, exactly as rain into it would,
/// and that limit is stated rather than hidden — the revisit trigger is the first play that
/// builds one and reads the leak as wrong.
/// </para>
/// <para>
/// <b>Whole litres cross; the remainder is carried per pit, never dropped.</b> Tier A counts
/// litres and a cell holds 15,625 mL, so a hand-off is exact only with a sub-litre remainder
/// somewhere. It is kept here, keyed by the basin's pit in world coordinates — the identity a
/// window move does not rename — so a move loses nothing, and it is under a litre per basin,
/// reported as <see cref="PendingMillilitres"/>. Taking the whole cell rather than its whole
/// litres is what leaves no film: a spill on natural ground ends with Tier B holding nothing at
/// all, which is the end state the awake set is measured against. The remainder is lost at quit
/// like the rain accumulator's, under a litre a basin.
/// </para>
/// <para>
/// <b>A cell under its own sample's lake is left standing (M8d-3, decision 8).</b> Tier A already
/// holds that water as the lake's level; handing the cell back would raise the level with water
/// the lake then drains again through whatever hole let it out — the 2026-09-09 treadmill, ~7 kL a
/// tick each way for ever. Refused, the water rests as Tier B under the lake and the drain stops
/// when the column under the mouth stands as high as the lake. Cost: a pour into a lake stays
/// Tier B on its bed rather than raising the lake, and a spill onto a lake's wet bank levels by
/// the automaton instead of vanishing.
/// </para>
/// <para>
/// <b>A cell on an outlet sample leaves the world</b>, as rain on an outlet catchment does, and
/// counts on both <c>received</c> and <c>spilled</c>. Leaving it in Tier B instead would leave it
/// asleep for ever, since a cell with no move is never woken by anything but an edit. <b>Before
/// the first window lands</b> there is no basin to receive, so cells on the plane are woken
/// rather than skipped: they rest again next tick and are listed again, until there is.
/// </para>
/// <para>
/// Sibling of <see cref="PortalDrain"/>, and the pair is the whole seam: the drain moves litres A →
/// B where a lake stands over a hole, this moves them B → A where water stands still on the
/// ground. Each is outside the automaton, which has no opinion about the analytic surface, and
/// each reports its half so that <c>drawn</c> and <c>handed</c> can be checked from both ends.
/// </para>
/// </remarks>
public sealed class SurfaceHandoff
{
    private static readonly (int X, int Z) OutletKey = (int.MinValue, int.MinValue);

    private readonly ISurfaceHeights _seed;
    private readonly WaterSystem _water;
    private readonly FluidSimulation _fluid;
    private readonly Dictionary<(int X, int Z), int> _pending = [];

    public SurfaceHandoff(ISurfaceHeights seed, WaterSystem water, FluidSimulation fluid)
    {
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(water);
        ArgumentNullException.ThrowIfNull(fluid);

        _seed = seed;
        _water = water;
        _fluid = fluid;
    }

    /// <summary>Millilitres taken out of Tier B. Equals <see cref="FluidSimulation.HandedMillilitres"/>' growth since construction.</summary>
    public long HandedMillilitres { get; private set; }

    /// <summary>Whole litres delivered to Tier A. Equals <see cref="WaterSystem.ReceivedLitres"/>' growth since construction.</summary>
    public long ReceivedLitres { get; private set; }

    /// <summary>Sub-litre remainders held per pit, in all. Under a litre per basin that has ever received.</summary>
    public long PendingMillilitres { get; private set; }

    /// <summary>Cells handed off, over the lifetime.</summary>
    public long Handoffs { get; private set; }

    /// <summary>Cells the last tick found at rest, ground or not.</summary>
    public int RestedLastPass { get; private set; }

    /// <summary>Of those, cells on the analytic ground — the ones handed, or woken for want of a window.</summary>
    public int OnGroundLastPass { get; private set; }

    /// <summary>Of the cells on the ground on the last pass, how many stood under their sample's lake and were left there (M8d-3).</summary>
    public int UnderLakeLastPass { get; private set; }

    /// <summary>Cells refused because they stood under their own lake, over the lifetime.</summary>
    public long RefusedUnderLake { get; private set; }

    /// <summary>Basins with a remainder pending.</summary>
    public int PendingBasins => _pending.Count;

    /// <summary>
    /// One pass over the cells the last tick found at rest. Returns the millilitres handed off.
    /// Call at the tick boundary, before the next tick begins.
    /// </summary>
    public long Absorb()
    {
        IReadOnlyList<VoxelPos> rested = _fluid.Rested;
        RestedLastPass = rested.Count;
        OnGroundLastPass = 0;
        UnderLakeLastPass = 0;
        if (rested.Count == 0)
        {
            return 0;
        }

        bool windowed = _water.Window is not null;
        long handedThisPass = 0;
        for (int i = 0; i < rested.Count; i++)
        {
            VoxelPos cell = rested[i];
            int amount = _fluid.Grid.AmountAt(cell);
            if (amount == 0 || cell.Y != _seed.SurfaceHeightAt(cell.X, cell.Z) + 1)
            {
                continue;
            }

            OnGroundLastPass++;
            if (!windowed)
            {
                // Nowhere to put it yet. Woken so it is visited, rests and is listed again next
                // tick, rather than sleeping on the ground until an edit happens to touch it.
                _fluid.Disturb(cell);
                continue;
            }

            (int sampleX, int sampleZ) = _water.SampleOf(cell.X, cell.Z);
            int basin = _water.BasinAtSample(sampleX, sampleZ);

            // M8d-3, decision 8: a cell under its own sample's lake is the lake's already, and is
            // left standing. Handing it back is the treadmill — a hole in a bank drains the lake,
            // fills, overflows onto the wet bed beside it, and the hand-off gave the overflow
            // straight back to the basin it came from, drawn and handed climbing together for
            // ever. Under, not merely wet: the lake's surface stands above the cell's top, so the
            // cell is submerged. Any water at all in the basin puts its level a hair over the bed
            // plane, and a rule on that refused the sixth cell of every spill into a bowl because
            // the first five had made a lake — the hand-off's own case, broken by its own
            // delivery. A cell at a lake's shallow margin is still handed, and the margin rises
            // until it is a cell deep; a cell on the bed beside a bitten bank is under a lake
            // many cells deep and stays. The water rests where it is as Tier B, and the drain
            // stops when the column under the mouth is as high as the lake.
            if (basin != Watershed.Outlet && _water.LevelAt(basin) is { } level && level > cell.Y + 1)
            {
                UnderLakeLastPass++;
                RefusedUnderLake++;
                continue;
            }

            (int X, int Z) key = basin == Watershed.Outlet
                ? OutletKey
                : PitOf(basin);

            long taken = _fluid.HandOff(cell, amount);
            if (taken == 0)
            {
                continue;
            }

            HandedMillilitres += taken;
            handedThisPass += taken;
            Handoffs++;

            _pending.TryGetValue(key, out int pending);
            pending += (int)taken;
            int litres = pending / FluidConstants.MillilitresPerLitre;
            pending -= litres * FluidConstants.MillilitresPerLitre;
            PendingMillilitres += taken - ((long)litres * FluidConstants.MillilitresPerLitre);
            _pending[key] = pending;

            if (litres > 0)
            {
                _water.Receive(basin, litres);
                ReceivedLitres += litres;
            }
        }

        return handedThisPass;
    }

    /// <summary>The remainder held for one basin of the installed window, in millilitres.</summary>
    public int PendingFor(int basin)
    {
        (int X, int Z) key = basin == Watershed.Outlet ? OutletKey : PitOf(basin);
        return _pending.TryGetValue(key, out int pending) ? pending : 0;
    }

    private (int X, int Z) PitOf(int basin)
    {
        Basin b = _water.Window!.Watershed.Basins[basin];
        return (b.PitX, b.PitZ);
    }
}
