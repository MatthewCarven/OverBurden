using Overburden.Core.Fluid;
using Overburden.Core.Lighting;
using Overburden.Core.Voxels;

namespace Overburden.Core.Hydrology;

/// <summary>One analysed window: the terrain, the basins in it, and the tree that merges them.</summary>
/// <remarks>
/// <b>Produced on a worker and installed on the sim thread</b>, which is the whole reason it is a
/// separate type rather than fields on <see cref="WaterSystem"/>. Everything in here is immutable
/// once built, so handing it across a thread needs no lock; the mutable half — the water, the
/// accumulator, the ledger — never leaves the sim thread.
/// </remarks>
public sealed class WaterWindow
{
    public WaterWindow(Watershed watershed, MergeTree tree)
    {
        ArgumentNullException.ThrowIfNull(watershed);
        ArgumentNullException.ThrowIfNull(tree);

        Watershed = watershed;
        Tree = tree;
    }

    public Watershed Watershed { get; }

    public MergeTree Tree { get; }

    /// <summary>The centre of this window in world voxels — what the player is measured against.</summary>
    public int CentreVoxelX =>
        Watershed.Field.OriginX + (Watershed.Field.Pitch * Watershed.Field.CellVoxels / 2);

    public int CentreVoxelZ =>
        Watershed.Field.OriginZ + (Watershed.Field.Pitch * Watershed.Field.CellVoxels / 2);
}

/// <summary>
/// The standing water around the player: which basins exist, how much is in them, and where the
/// surface is.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the join M7 was designed around, and it is deliberately not a chunk-shaped thing.</b>
/// The analysis takes <see cref="ISurfaceHeights"/>, which is analytic and seeded, so it needs no
/// streaming, no margin, no barrier and no stage — M7-PLAN says the instinct to make it one is the
/// thing to resist, and this class is where that instinct would have landed.
/// </para>
/// <para>
/// <b>The threading contract is the one thing a caller can get wrong, so it is stated rather than
/// implied.</b> <see cref="Analyse"/> is pure and static and belongs on a worker; everything else
/// here belongs on the sim thread. The two never touch the same object: <c>Analyse</c> reads a
/// height function and returns a fresh <see cref="WaterWindow"/>, and <see cref="Install"/> is
/// what publishes it. There is no lock because there is nothing shared, and adding state that
/// spans the two would need one.
/// </para>
/// <para>
/// <b>What survives a window move is the ledger, not the water.</b> A <see cref="BasinWater"/> only
/// ever holds what is inside the analysed window, so walking a kilometre leaves the lake behind
/// with no representation in memory at all — which is why <see cref="BasinLedger"/> exists and why
/// a basin is identified by its pit in world coordinates rather than by an array index. Installing
/// a new window absorbs the old water into the ledger first and seeds the new one from it, so the
/// lake is still there when you walk back.
/// </para>
/// <para>
/// <b>Rainfall is integrated forward rather than stepped</b>, so a tick that arrives late computes
/// the same number as a tick that arrives on time — see <see cref="BasinRainfall"/>, whose oracle
/// is exactly that property. Nothing here has to run at any particular rate, and nothing here
/// accumulates error if it does not.
/// </para>
/// </remarks>
public sealed class WaterSystem : IWaterTable
{
    private readonly ISurfaceHeights _heights;
    private readonly ulong _seed;

    private BasinRainfall? _rain;

    /// <summary>World time every litre up to which has been delivered to some basin.</summary>
    /// <remarks>
    /// <b>Distinct from <see cref="TimeMillis"/>, and the gap between them is rain that fell with
    /// nowhere to put it</b> - the tens of milliseconds a first analysis takes on a worker, during
    /// which the world is not paused. The next window to land is constructed to start here rather
    /// than at now, so that interval is integrated in one step the moment there is somewhere to
    /// receive it. That is the accumulator's own "arriving late computes the same number" property
    /// one layer out, and without this the two numbers would differ by however long the analysis
    /// happened to take.
    /// </remarks>
    private long _integratedToMillis;

    /// <param name="pitch">Samples on a side of the analysed window.</param>
    /// <param name="cellVoxels">Voxels per sample. Must match the world header's lattice.</param>
    /// <param name="rainfallScale">
    /// Multiplies the depth applied to the world while leaving <see cref="Weather"/> stating real
    /// intensities, so the sky and the wet-surface term still read a real storm. <b>Real rainfall
    /// fills the median basin in 35 minutes of the heaviest weather this world has</b>, which
    /// nobody can watch — M7b measured it and left picking this number to M7d.
    /// </param>
    /// <param name="startMillis">
    /// The world clock this session begins at. <b>Not zero for a reopened world, and getting that
    /// wrong is unrecoverable rather than untidy</b>: the un-integrated interval is delivered when
    /// the first window lands, so starting at zero would pour the whole of a world's history into
    /// its basins the moment the player walked in - on top of the ledger, which already holds what
    /// fell in every previous session.
    /// </param>
    public WaterSystem(
        ISurfaceHeights heights,
        ulong seed,
        BasinLedger ledger,
        int pitch,
        int cellVoxels = HeightField.DefaultCellVoxels,
        int rainfallScale = 1,
        long startMillis = 0)
    {
        ArgumentNullException.ThrowIfNull(heights);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentOutOfRangeException.ThrowIfLessThan(pitch, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(rainfallScale, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(startMillis);

        TimeMillis = startMillis;
        _integratedToMillis = startMillis;
        _heights = heights;
        _seed = seed;
        Ledger = ledger;
        Pitch = pitch;
        CellVoxels = cellVoxels;
        RainfallScale = rainfallScale;
    }

    /// <summary>Every basin in the world that holds water, including ones nobody is looking at.</summary>
    public BasinLedger Ledger { get; }

    public int Pitch { get; }

    public int CellVoxels { get; }

    public int RainfallScale { get; }

    /// <summary>Metres across the analysed window.</summary>
    public int SpanMetres => Pitch * CellVoxels / VoxelConstants.VoxelsPerMetre;

    /// <summary>The window currently installed, or null before the first analysis lands.</summary>
    public WaterWindow? Window { get; private set; }

    /// <summary>The water in the installed window, or null before the first analysis lands.</summary>
    public BasinWater? Water { get; private set; }

    /// <summary>World time the accumulator has been integrated to.</summary>
    public long TimeMillis { get; private set; }

    /// <summary>Litres that have left the world through an outlet since this session began.</summary>
    public long SpilledLitres { get; private set; }

    /// <summary>Litres taken out of basins through portals into Tier B — see <see cref="Draw"/>.</summary>
    /// <remarks>
    /// A term in Tier A's own identity, <c>held == rained + injected − spilled − drawn</c>, and
    /// the number Tier B's <c>drawn</c> has to equal to the litre: the two tiers report the seam
    /// from both ends so that a leak between them has nowhere to hide.
    /// </remarks>
    public long DrawnLitres { get; private set; }

    /// <summary>Litres handed to basins by Tier B through <see cref="Receive"/> — the B → A seam.</summary>
    /// <remarks>
    /// The other half of <see cref="DrawnLitres"/>' argument: Tier B's <c>handed</c> has to equal
    /// this to the litre, and Tier A's identity is <c>held == rained + injected + received −
    /// spilled − drawn</c>. Litres received on an outlet sample count here <i>and</i> in
    /// <see cref="SpilledLitres"/>, because they arrived and then left the world, which is what
    /// rain on an outlet catchment does.
    /// </remarks>
    public long ReceivedLitres { get; private set; }

    /// <summary>Litres put into basins by <see cref="Inject"/>, the debug pour for Tier A.</summary>
    /// <remarks>
    /// M8-PLAN's rule for the relay, applied to this tier: a debug source creates litres from
    /// nothing, so it is a reported term rather than something subtracted out.
    /// </remarks>
    public long InjectedLitres { get; private set; }

    /// <summary>Windows installed this session. One is a session that never moved.</summary>
    public int WindowsInstalled { get; private set; }

    /// <summary>Litres the ledger could not fit back into a re-analysed window.</summary>
    /// <remarks>
    /// <b>Normally zero and worth watching when it is not.</b> It means a basin's stored water no
    /// longer fits the basin, which happens when the window has moved far enough that part of a
    /// catchment now lies outside it and its capacity has shrunk. The water leaves rather than
    /// being carried as an invisible debt — see <see cref="BasinLedger.Seed"/>. A figure that
    /// climbs every time the player walks is the window being too small for the terrain.
    /// </remarks>
    public long SeedOverflowLitres { get; private set; }

    /// <summary>
    /// The lattice-aligned origin of the window that should be analysed for a player at this
    /// position.
    /// </summary>
    /// <remarks>
    /// <b>Snapped to a whole window-quarter rather than centred exactly on the player</b>, so that
    /// walking back and forth across one spot cannot re-analyse repeatedly. Together with
    /// <see cref="NeedsWindowFor"/>'s quarter-span threshold this gives a hysteresis of a whole
    /// step: the window moves, and the player then has to cross another quarter-span to move it
    /// again.
    /// </remarks>
    public (int X, int Z) WindowOriginFor(int voxelX, int voxelZ)
    {
        int span = Pitch * CellVoxels;
        int step = Math.Max(CellVoxels, span / 4);

        int centreX = FloorTo(voxelX, step);
        int centreZ = FloorTo(voxelZ, step);

        return (FloorTo(centreX - (span / 2), CellVoxels), FloorTo(centreZ - (span / 2), CellVoxels));
    }

    /// <summary>Whether the player has walked far enough to want a different window.</summary>
    public bool NeedsWindowFor(int voxelX, int voxelZ)
    {
        if (Window is null)
        {
            return true;
        }

        (int originX, int originZ) = WindowOriginFor(voxelX, voxelZ);
        return originX != Window.Watershed.Field.OriginX
            || originZ != Window.Watershed.Field.OriginZ;
    }

    /// <summary>
    /// Samples the terrain, floods it and builds the merge tree. <b>Pure — run it on a worker.</b>
    /// </summary>
    /// <remarks>
    /// <b>Nothing here touches this instance</b>, which is what makes it safe to call off the sim
    /// thread while the old window is still being rained on. It is static for that reason rather
    /// than for tidiness: an instance method would invite reading a field, and the field it would
    /// read is the one <see cref="Install"/> is about to replace.
    /// </remarks>
    public static WaterWindow Analyse(
        ISurfaceHeights heights, int originX, int originZ, int pitch, int cellVoxels)
    {
        HeightField field = HeightField.Sample(heights, originX, originZ, pitch, cellVoxels);
        Watershed watershed = Watershed.Analyse(field);
        return new WaterWindow(watershed, MergeTree.Build(watershed));
    }

    /// <summary>Analyses the window this player position wants. A convenience over <see cref="Analyse"/>.</summary>
    public WaterWindow AnalyseFor(int voxelX, int voxelZ)
    {
        (int originX, int originZ) = WindowOriginFor(voxelX, voxelZ);
        return Analyse(_heights, originX, originZ, Pitch, CellVoxels);
    }

    /// <summary>
    /// Publishes a window analysed elsewhere: banks the old water, seeds the new from the ledger.
    /// </summary>
    /// <remarks>
    /// <b>Absorb before seed, always.</b> The old window and the new one overlap almost entirely,
    /// so a basin usually appears in both — seeding first would pour the ledger's stale figure into
    /// the new water and then overwrite the ledger with it, losing everything that fell while the
    /// analysis was running. The order is what makes a window move invisible rather than a small
    /// leak every time the player walks.
    /// </remarks>
    public void Install(WaterWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (window.Watershed.Field.CellVoxels != CellVoxels)
        {
            throw new ArgumentException(
                $"Window analysed on a {window.Watershed.Field.CellVoxels}-voxel lattice cannot be "
                + $"installed on a {CellVoxels}-voxel one: a basin is identified by a pit on that "
                + "lattice, so the two windows do not name the same basins.",
                nameof(window));
        }

        if (Window is not null && Water is not null)
        {
            Ledger.Absorb(Window.Watershed, Water);
        }

        var water = new BasinWater(window.Tree);
        SeedOverflowLitres += Ledger.Seed(window.Watershed, water);

        Window = window;
        Water = water;
        WindowsInstalled++;

        // Started at what has actually been *delivered*, not at now. Across a window move those
        // are the same instant and this reads like a needless distinction; on the first window
        // they differ by however long the analysis took, and the rain from that interval lands
        // rather than being lost. Either way no rain is dropped and none is counted twice, which
        // is the property a move has to have.
        //
        // THE ONE THING A MOVE DOES COST, STATED: the new accumulator's pending sub-litre
        // remainder starts at zero, so under a litre per basin is discarded here. BasinRainfall's
        // own remarks warn that dropping the remainder *every step* stops a drizzle ever
        // accumulating at all; this drops it per *move*, which the window hysteresis makes rare,
        // so a drizzle still fills a basin between moves. Bounded and cheap next to a basin
        // holding tens of thousands of litres — and asserted as a bound rather than waved at, in
        // AWindowMoveNeitherDropsRainNorCountsItTwice.
        //
        // Revisit trigger: anything that re-analyses often — a much smaller window, or rainfall so
        // light that a basin's whole intake between moves is sub-litre. The fix is to carry the
        // remainder across a move keyed by pit, exactly as the ledger carries litres.
        _rain = new BasinRainfall(
            window.Watershed, water, _seed, _integratedToMillis, RainfallScale);
    }

    /// <summary>Rains everything that fell since the last call. Returns litres that left the world.</summary>
    public long Advance(long timeMillis)
    {
        if (timeMillis < TimeMillis)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeMillis), timeMillis, "World time does not run backwards.");
        }

        TimeMillis = timeMillis;

        if (_rain is null)
        {
            // Before the first window lands. The clock still advances, so the rain that fell while
            // the first analysis was running is not lost — it is simply integrated in one step by
            // the accumulator the moment there is somewhere to put it, which is the same
            // "arriving late computes the same number" property the whole design rests on.
            return 0;
        }

        long spilled = _rain.AdvanceTo(timeMillis);
        _integratedToMillis = timeMillis;
        SpilledLitres += spilled;
        return spilled;
    }

    /// <summary>Banks the installed window's water so the ledger is what gets written.</summary>
    /// <remarks>
    /// <b>Called before a save, and idempotent.</b> The ledger is the persistent thing; the water
    /// is a view of the part of it that is currently analysed. Writing without this saves the world
    /// as it was when the player entered the window rather than as it is.
    /// </remarks>
    public void Bank()
    {
        if (Window is not null && Water is not null)
        {
            Ledger.Absorb(Window.Watershed, Water);
        }
    }

    /// <summary>The water surface over a basin, in voxel Y, or null if it holds nothing.</summary>
    public double? LevelAt(int basin)
    {
        if (Water is null || Window is null) return null;
        return Water.StoredIn(basin) > 0 ? Water.LevelAt(basin) : null;
    }

    /// <summary>
    /// The water table over a world-lattice sample: the lake standing over the basin it drains to,
    /// or null off the window, on an outlet, or over a dry basin. M8c's Tier C, derived and stored
    /// nowhere — see <see cref="IWaterTable"/>.
    /// </summary>
    public double? TableAt(int sampleX, int sampleZ)
    {
        int basin = BasinAtSample(sampleX, sampleZ);
        return basin == Watershed.Outlet ? null : LevelAt(basin);
    }

    /// <summary>
    /// Takes up to <paramref name="litres"/> out of the lake this basin is part of, through its
    /// floor, and returns what was taken. The A → B half of the breach.
    /// </summary>
    /// <remarks>
    /// <see cref="BasinWater.Remove"/> with the counter the profile prints. Whole litres, because
    /// the seam is exact both ways only in whole litres: a Tier B cell holds 15,625 mL and a litre
    /// is 1,000, so the caller asks for what fits and never rounds.
    /// </remarks>
    public long Draw(int basin, long litres)
    {
        if (Water is null)
        {
            return 0;
        }

        long drawn = Water.Remove(basin, litres);
        DrawnLitres += drawn;
        return drawn;
    }

    /// <summary>
    /// Takes whole litres from Tier B into the lake this basin is part of, or out of the world
    /// when the basin is <see cref="Watershed.Outlet"/>. Returns what spilled. The B → A half of
    /// the seam.
    /// </summary>
    /// <remarks>
    /// <see cref="BasinWater.Add"/> with the counter the profile prints, and the same rule rain
    /// has for a catchment that drains off the window: the litres leave. A resting cell on an
    /// outlet sample is water on ground that Tier A says drains out of the world, and keeping it
    /// in Tier B instead would leave it asleep there for ever, since nothing wakes a cell that has
    /// nowhere to go.
    /// </remarks>
    public long Receive(int basin, long litres)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(litres);
        if (Water is null)
        {
            throw new InvalidOperationException("No window is installed, so there is no basin to receive into.");
        }

        ReceivedLitres += litres;
        long spilled = basin == Watershed.Outlet ? litres : Water.Add(basin, litres);
        SpilledLitres += spilled;
        return spilled;
    }

    /// <summary>Pours litres into a basin from nothing — the debug seam for Tier A. Returns what spilled.</summary>
    public long Inject(int basin, long litres)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(litres);
        if (Water is null)
        {
            throw new InvalidOperationException("No window is installed, so there is no basin to pour into.");
        }

        InjectedLitres += litres;
        long spilled = Water.Add(basin, litres);
        SpilledLitres += spilled;
        return spilled;
    }

    /// <summary>The world-lattice sample a voxel column sits in, on the same lattice the window uses.</summary>
    public (int X, int Z) SampleOf(int voxelX, int voxelZ) =>
        (FloorDiv(voxelX, CellVoxels), FloorDiv(voxelZ, CellVoxels));

    /// <summary>
    /// The basin under a world-lattice sample in the installed window, or
    /// <see cref="Watershed.Outlet"/> when the sample is outside the window or drains off its edge.
    /// </summary>
    /// <remarks>
    /// <b>Keyed on the world lattice, which a window move does not rename</b>, so a portal index
    /// keyed the same way survives a move with no re-keying: only the label it maps to changes,
    /// and that is looked up here every tick rather than stored.
    /// </remarks>
    public int BasinAtSample(int worldSampleX, int worldSampleZ)
    {
        if (Window is null)
        {
            return Watershed.Outlet;
        }

        HeightField field = Window.Watershed.Field;
        int sx = worldSampleX - (field.OriginX / field.CellVoxels);
        int sz = worldSampleZ - (field.OriginZ / field.CellVoxels);
        if (sx < 0 || sz < 0 || sx >= field.Pitch || sz >= field.Pitch)
        {
            return Watershed.Outlet;
        }

        return Window.Watershed.LabelAt(sx, sz);
    }

    /// <summary>Litres standing in the installed window right now.</summary>
    public long StoredLitres => Water?.TotalStoredLitres ?? 0;

    /// <summary>Litres the window's pits would hold if every one of them were brim-full.</summary>
    /// <remarks>
    /// <b>The denominator that turns litres into "how full", which is what picking
    /// <see cref="RainfallScale"/> actually turns on.</b> A litre count on its own says nothing
    /// about whether a person would see a pond rise: a hundred million litres is a full map or a
    /// puddle depending on the terrain. Leaf capacity rather than the merge tree's total, because
    /// the composite slabs above the leaves are lakes that have already drowned their ridges — by
    /// then the question has answered itself.
    /// </remarks>
    public long WindowCapacityLitres
    {
        get
        {
            if (Window is null) return 0;

            long capacity = 0;
            foreach (Basin basin in Window.Watershed.Basins)
            {
                capacity += basin.CapacityLitres;
            }

            return capacity;
        }
    }

    private static int FloorTo(int value, int step)
    {
        int remainder = value % step;
        return remainder < 0 ? value - remainder - step : value - remainder;
    }

    private static int FloorDiv(int value, int divisor) => FloorTo(value, divisor) / divisor;
}
