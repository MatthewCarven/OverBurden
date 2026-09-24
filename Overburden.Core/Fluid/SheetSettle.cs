using System.Diagnostics;

namespace Overburden.Core.Fluid;

/// <summary>
/// When the sheet solve runs: at a tick boundary, after <i>K</i> ticks with no inflow, while
/// anything is still awake — and again at every boundary that qualifies, until nothing is.
/// </summary>
/// <remarks>
/// <para>
/// <b>Inflow is water entering or the terrain changing: an <see cref="FluidSimulation.Add"/> —
/// the relay, the portal drain — or a <see cref="FluidSimulation.Disturb"/> that woke
/// something.</b> Water leaving through the hand-off or the relay is not inflow: a sheet drained
/// from above has less to level, not more. The count is of whole ticks since the last inflow,
/// measured boundary to boundary, so the tick that followed a pour is never a quiet one.
/// </para>
/// <para>
/// <b><i>K</i> = 2 is an estimate, labelled as one, and <c>--settleafter=</c> is the sweep.</b> One
/// second of nothing arriving is long enough that a stream down a tunnel is still a stream while
/// it flows and a floor levels itself once the flow stops; a person has yet to say whether that
/// reads right. <b>Zero settles under inflow too</b>, at every boundary with anything awake, which
/// is the arm that makes a flowing stream rise as a flat sheet instead of flowing — kept so both
/// can be seen rather than argued about. Every pass is O(the sheets the active set touches), so
/// repeating one at each boundary costs what the sheets cost and moves the front on each time.
/// </para>
/// <para>
/// <b>What a pass levels is the solve's decision, under the share this carries</b>: a sheet is
/// re-levelled when at least one cell in <see cref="SettleShare"/> of it is unsettled, or when a
/// step stands in it, and is otherwise left to the automaton with its flood stopped early. The
/// first play fired that lever — a 162,572-cell lake re-levelled twice a second for its creeping
/// edge — and <c>--settleshare=</c> is its sweep; the rule and its bound are in
/// <see cref="FluidSimulation.LevelSheets(int)"/>.
/// </para>
/// <para>
/// Sibling of <see cref="SurfaceHandoff"/> and <see cref="PortalDrain"/> at the boundary, and
/// deliberately outside the automaton for the reason they are: the tick has no opinion about
/// when it is worth solving what it would otherwise diffuse to.
/// </para>
/// </remarks>
public sealed class SheetSettle
{
    /// <summary>The trigger interval the game ships with: two ticks, one second at 2 Hz. An estimate.</summary>
    public const int DefaultSettleAfterTicks = 2;

    /// <summary>
    /// The share the game ships with: a sheet is re-levelled when at least one cell in eight of it
    /// is unsettled. Picked on the bench (M8-PLAN, the share slice); <c>--settleshare=</c> is the
    /// sweep and zero is no threshold.
    /// </summary>
    public const int DefaultSettleShare = 8;

    /// <summary>
    /// The pass cap the game ships with (M10d-3, decision 4, Matthew's word 2026-09-24): a pass stops
    /// at the first height boundary after it has tested this many seeds and cells, and the next
    /// pass resumes from that height. <b>An estimate</b>, sized off the load test's slow phase so the
    /// solve is about a third of the tick (~540 ns an item there, 1.2 M items a pass uncapped);
    /// <c>--solvecap=</c> is the sweep and 0 is no cap. <b>The constructor's default is 0, not
    /// this</b>, because every bench arm's recorded figure was taken uncapped and several are big
    /// enough that a default cap would move them without a word.
    /// </summary>
    public const int DefaultSolveCap = 500_000;

    /// <summary>
    /// The front advance the game ships with (docs/FRONT-ADVANCE-PLAN.md, Matthew's word
    /// 2026-09-24): rings of dry floor a levelled sheet may take a pass. <b>An estimate</b> — four
    /// cells, a metre a pass — until the bench's sweep; <c>--frontadvance=</c> is the seam and 0 is
    /// off. The constructor defaults to 0 for the reason <see cref="DefaultSolveCap"/> does.
    /// </summary>
    public const int DefaultFrontAdvance = 4;

    private readonly FluidSimulation _fluid;

    public SheetSettle(
        FluidSimulation fluid,
        int settleAfterTicks = DefaultSettleAfterTicks,
        int settleShare = DefaultSettleShare,
        int solveCap = 0,
        int frontAdvance = 0)
    {
        ArgumentNullException.ThrowIfNull(fluid);
        ArgumentOutOfRangeException.ThrowIfNegative(settleAfterTicks);
        ArgumentOutOfRangeException.ThrowIfNegative(settleShare);
        ArgumentOutOfRangeException.ThrowIfNegative(solveCap);
        ArgumentOutOfRangeException.ThrowIfNegative(frontAdvance);

        _fluid = fluid;
        SettleAfterTicks = settleAfterTicks;
        SettleShare = settleShare;
        SolveCap = solveCap;
        FrontAdvance = frontAdvance;
    }

    /// <summary>Rings of dry floor a levelled sheet may take a pass; zero is off.</summary>
    public int FrontAdvance { get; }

    /// <summary>Dry cells the advance has added to sheets, summed over every pass.</summary>
    public long CellsAdvanced { get; private set; }

    /// <summary>
    /// The inflow split over every pass (FRONT-ADVANCE-PLAN slice 2a): sheets tested for feeding,
    /// by the water that entered their chunks in the window as a share of them — under 0.1%, under
    /// 1%, under 10%, 10% or more, disturbed — and those levelled for being under the fed share.
    /// </summary>
    public long InflowUnderTenthPercent { get; private set; }

    public long InflowUnderOnePercent { get; private set; }

    public long InflowUnderTenPercent { get; private set; }

    public long InflowTenPercentPlus { get; private set; }

    public long InflowDisturbed { get; private set; }

    public long TrickleLevelled { get; private set; }

    /// <summary>
    /// Slice 2c over every pass: sheets still being fed whose tip was advanced, the dry cells they
    /// took, and the old cells of their tips levelled with them.
    /// </summary>
    public long FedSheetsAdvanced { get; private set; }

    public long FedCellsAdvanced { get; private set; }

    public long FedBandCells { get; private set; }

    /// <summary>Seeds and cells a pass may test before it stops at the next height; zero is no cap.</summary>
    public int SolveCap { get; }

    /// <summary>Passes that stopped at the cap and left heights to the next.</summary>
    public long CappedPasses { get; private set; }

    /// <summary>Seeds offered to a pass at heights it did not reach, summed.</summary>
    public long SeedsDeferred { get; private set; }

    /// <summary>Ticks with no inflow before a pass runs. Zero runs one at every boundary with anything awake.</summary>
    public int SettleAfterTicks { get; }

    /// <summary>
    /// One cell in this many unsettled before a sheet is re-levelled; zero levels any sheet with a
    /// move in it. See <see cref="FluidSimulation.LevelSheets(int)"/>.
    /// </summary>
    public int SettleShare { get; }

    /// <summary>Sheets a pass left alone for standing in a chunk that is still being fed.</summary>
    public long SheetsNoisy { get; private set; }

    /// <summary>Passes run.</summary>
    public long Settles { get; private set; }

    /// <summary>Passes that changed nothing.</summary>
    public long IdleSettles { get; private set; }

    public long SheetsFound { get; private set; }

    public long SheetsLevelled { get; private set; }

    public long SheetsPacked { get; private set; }

    public long SheetsSkipped { get; private set; }

    /// <summary>Sheets with a move in them left to the automaton for being under the share.</summary>
    public long SheetsUnderShare { get; private set; }

    /// <summary>Sheets under the share levelled anyway, for a step between settled plateaus.</summary>
    public long SheetsStepped { get; private set; }

    /// <summary>Cells the passes read — the cost the share bounds.</summary>
    public long CellsVisited { get; private set; }

    public long CellsWritten { get; private set; }

    public long MillilitresMoved { get; private set; }

    public long CellsWoken { get; private set; }

    /// <summary>Cells in the largest sheet any pass found.</summary>
    public int LargestSheet { get; private set; }

    /// <summary>What the last pass took, and the most any pass took.</summary>
    public double LastMillis { get; private set; }

    public double LongestMillis { get; private set; }

    public double TotalMillis { get; private set; }

    /// <summary>Seeds offered over every pass — the pass's input, which a large carry can put far above the cells it visits.</summary>
    public long SeedsOffered { get; private set; }

    /// <summary>The four phases of every pass, summed; see <see cref="SheetLevelResult"/>.</summary>
    public double GatherMillis { get; private set; }

    public double SortMillis { get; private set; }

    public double FloodMillis { get; private set; }

    public double LevelMillis { get; private set; }

    /// <summary>
    /// The tick boundary. Returns the pass's result when one ran, null when the trigger did not
    /// fire. Call once per boundary, before anything pours into the next tick.
    /// </summary>
    public SheetLevelResult? AtBoundary()
    {
        if (_fluid.ActiveCellCount == 0)
        {
            return null;
        }

        return Run();
    }

    /// <summary>One pass now, whatever the trigger says. Counted like any other.</summary>
    public SheetLevelResult Run()
    {
        long before = Stopwatch.GetTimestamp();
        SheetLevelResult result = _fluid.LevelSheets(SettleShare, SettleAfterTicks, SolveCap, FrontAdvance);
        double millis = (Stopwatch.GetTimestamp() - before) * 1000.0 / Stopwatch.Frequency;
        return Record(result, millis);
    }

    /// <summary>The parameters a job's worker runs the solve with. See <see cref="FluidSimulation.BeginTick"/>.</summary>
    public (int Share, int QuietTicks, int Cap, int Advance) ForWorker =>
        (SettleShare, SettleAfterTicks, SolveCap, FrontAdvance);

    /// <summary>Counts a pass that ran elsewhere — on a job's worker (slice 3c) — as one of this settle's.</summary>
    public SheetLevelResult Record(SheetLevelResult result, double millis)
    {
        Settles++;
        if (result.Idle)
        {
            IdleSettles++;
        }

        SheetsFound += result.Sheets;
        SheetsLevelled += result.Levelled;
        SheetsPacked += result.Packed;
        SheetsSkipped += result.Skipped;
        SheetsUnderShare += result.UnderShare;
        SheetsStepped += result.Stepped;
        SheetsNoisy += result.Noisy;
        CellsVisited += result.CellsVisited;
        CellsWritten += result.CellsWritten;
        MillilitresMoved += result.MillilitresMoved;
        CellsWoken += result.CellsWoken;
        LargestSheet = Math.Max(LargestSheet, result.LargestSheet);
        LastMillis = millis;
        LongestMillis = Math.Max(LongestMillis, millis);
        TotalMillis += millis;
        SeedsOffered += result.Seeds;
        SeedsDeferred += result.SeedsDeferred;
        CellsAdvanced += result.Advanced;
        InflowUnderTenthPercent += result.InflowUnderTenthPercent;
        InflowUnderOnePercent += result.InflowUnderOnePercent;
        InflowUnderTenPercent += result.InflowUnderTenPercent;
        InflowTenPercentPlus += result.InflowTenPercentPlus;
        InflowDisturbed += result.InflowDisturbed;
        TrickleLevelled += result.TrickleLevelled;
        FedSheetsAdvanced += result.FedAdvanced;
        FedCellsAdvanced += result.FedAdvancedCells;
        FedBandCells += result.FedBandCells;
        if (result.Capped)
        {
            CappedPasses++;
        }

        GatherMillis += result.GatherMillis;
        SortMillis += result.SortMillis;
        FloodMillis += result.FloodMillis;
        LevelMillis += result.LevelMillis;
        return result;
    }
}
