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
/// builds one and reads the leak as wrong. <b>That trigger fired on 2026-09-20</b> (Matthew's
/// courtyard), and the answer is the <see cref="Enclosure"/>: a rested cell on the plane whose
/// column the player's walls close around at the cell's own level is not handed — it is the
/// courtyard's, not the basin's — and stays asleep where it rests. Nature's ground still leaks;
/// the player's walls hold. Without an enclosure the pass is as it was.
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
/// <b>Water on the lake's top is on the plane too, and water inside the lake is the lake's
/// (M8d-5).</b> With a <see cref="LakeCeiling"/>, the plane a cell stands on is the lake's top
/// where there is one and the seed ground where there is not — one test, <c>Y == plane + 1</c> —
/// so a pour onto a lake spreads on its surface, rests, and is handed exactly as a spill on the
/// ground is. And a cell the last tick found <i>inside</i> a lake (<see cref="FluidSimulation.Submerged"/>
/// — a level that rose past a sleeping cell, or bed water stored by a build before the rule) is
/// handed whole without waiting to rest, because a visit never moves it. This replaces M8d-3's
/// bank rule, which left a rested cell under its own lake standing to cut the 2026-09-09
/// treadmill; the treadmill was the bed under a lake being, to Tier B, dry ground it could spread
/// onto, and the ceiling takes that ground away instead. Without a ceiling the pass is M8b-3a's:
/// the seed ground is the only plane.
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
    private readonly LakeCeiling? _ceiling;
    private readonly Enclosure? _enclosure;
    private readonly Dictionary<(int X, int Z), int> _pending = [];

    public SurfaceHandoff(ISurfaceHeights seed, WaterSystem water, FluidSimulation fluid, LakeCeiling? ceiling = null, Enclosure? enclosure = null)
    {
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(water);
        ArgumentNullException.ThrowIfNull(fluid);

        _seed = seed;
        _water = water;
        _fluid = fluid;
        _ceiling = ceiling;
        _enclosure = enclosure;
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

    /// <summary>Of those, cells on the plane — the ground, or the lake's top — the ones handed, or woken for want of a window.</summary>
    public int OnGroundLastPass { get; private set; }

    /// <summary>Cells the last tick found inside a lake, handed whole on the last pass (M8d-5).</summary>
    public int SubmergedLastPass { get; private set; }

    /// <summary>Cells handed for being inside a lake, over the lifetime.</summary>
    public long SubmergedHanded { get; private set; }

    /// <summary>Basins with a remainder pending.</summary>
    public int PendingBasins => _pending.Count;

    /// <summary>Cells on the plane left standing on the last pass because their column is enclosed at their level (the courtyard).</summary>
    public int EnclosedLastPass { get; private set; }

    /// <summary>
    /// The raised watershed (docs/POND-PLAN.md, decision 6): with it, the enclosure is asked only
    /// for a cell whose sample the player has built in — a pool narrower than a cell, which the
    /// watershed cannot resolve. Anywhere else the basin under the sample is trusted, since a
    /// walled pool wide enough to be a basin is its own basin now. Null asks everywhere, the
    /// pre-2026-09-25 arm.
    /// </summary>
    public Hydrology.IRaisedSamples? Raised { get; set; }

    /// <summary>Cells left standing for being enclosed, over the lifetime — counted on every pass that listed them, so a pool still levelling inside its walls counts its cells each time they rest.</summary>
    public long EnclosedKept { get; private set; }

    private bool AsksEnclosure(VoxelPos cell)
    {
        if (Raised is null)
        {
            return true;
        }

        (int sampleX, int sampleZ) = _water.SampleOf(cell.X, cell.Z);
        return Raised.TryGetRaised(sampleX, sampleZ, out _);
    }

    /// <summary>
    /// One pass over the cells the last tick found at rest. Returns the millilitres handed off.
    /// Call at the tick boundary, before the next tick begins.
    /// </summary>
    public long Absorb()
    {
        IReadOnlyList<VoxelPos> rested = _fluid.Rested;
        IReadOnlyList<VoxelPos> submerged = _fluid.Submerged;
        RestedLastPass = rested.Count;
        OnGroundLastPass = 0;
        SubmergedLastPass = 0;
        EnclosedLastPass = 0;
        if (rested.Count == 0 && submerged.Count == 0)
        {
            return 0;
        }

        bool windowed = _water.Window is not null;
        long handedThisPass = 0;
        for (int i = 0; i < rested.Count; i++)
        {
            VoxelPos cell = rested[i];
            int amount = _fluid.Grid.AmountAt(cell);
            if (amount == 0 || cell.Y != PlaneAt(cell.X, cell.Z) + 1)
            {
                continue;
            }

            OnGroundLastPass++;
            if (_enclosure is not null && AsksEnclosure(cell) && _enclosure.IsEnclosed(cell.X, cell.Z, cell.Y))
            {
                // The courtyard: on the plane, and walled in at its own level. Not the basin's.
                // Left asleep where it rests — nothing wakes it but an edit or a neighbour, and
                // a wall coming down is an edit.
                EnclosedLastPass++;
                EnclosedKept++;
                continue;
            }

            if (!windowed)
            {
                // Nowhere to put it yet. Woken so it is visited, rests and is listed again next
                // tick, rather than sleeping on the ground until an edit happens to touch it.
                _fluid.Disturb(cell);
                continue;
            }

            handedThisPass += Hand(cell, amount);
        }

        // Inside a lake: the lake's whether or not it rested, and a visit never moves it. Before
        // a window lands there is no lake to be inside, so the list is empty by construction.
        for (int i = 0; i < submerged.Count; i++)
        {
            VoxelPos cell = submerged[i];
            int amount = _fluid.Grid.AmountAt(cell);
            if (amount == 0 || !windowed)
            {
                continue;
            }

            long taken = Hand(cell, amount);
            if (taken > 0)
            {
                SubmergedLastPass++;
                SubmergedHanded++;
            }

            handedThisPass += taken;
        }

        return handedThisPass;
    }

    /// <summary>The plane water stands on at a column: the lake's top over a lake, else the seed ground.</summary>
    private int PlaneAt(int x, int z) =>
        _ceiling is not null ? _ceiling.PlaneAt(x, z) : _seed.SurfaceHeightAt(x, z);

    /// <summary>Takes the whole cell out of Tier B and delivers its whole litres to the basin under its sample.</summary>
    private long Hand(VoxelPos cell, int amount)
    {
        (int sampleX, int sampleZ) = _water.SampleOf(cell.X, cell.Z);
        int basin = _water.BasinAtSample(sampleX, sampleZ);
        (int X, int Z) key = basin == Watershed.Outlet
            ? OutletKey
            : PitOf(basin);

        long taken = _fluid.HandOff(cell, amount);
        if (taken == 0)
        {
            return 0;
        }

        HandedMillilitres += taken;
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

        return taken;
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
