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

    private readonly FluidSimulation _fluid;
    private long _addedSeen;
    private long _disturbedSeen;

    public SheetSettle(
        FluidSimulation fluid,
        int settleAfterTicks = DefaultSettleAfterTicks,
        int settleShare = DefaultSettleShare)
    {
        ArgumentNullException.ThrowIfNull(fluid);
        ArgumentOutOfRangeException.ThrowIfNegative(settleAfterTicks);
        ArgumentOutOfRangeException.ThrowIfNegative(settleShare);

        _fluid = fluid;
        SettleAfterTicks = settleAfterTicks;
        SettleShare = settleShare;
        _addedSeen = fluid.AddedMillilitres;
        _disturbedSeen = fluid.DisturbedCells;
    }

    /// <summary>Ticks with no inflow before a pass runs. Zero runs one at every boundary with anything awake.</summary>
    public int SettleAfterTicks { get; }

    /// <summary>
    /// One cell in this many unsettled before a sheet is re-levelled; zero levels any sheet with a
    /// move in it. See <see cref="FluidSimulation.LevelSheets(int)"/>.
    /// </summary>
    public int SettleShare { get; }

    /// <summary>Consecutive ticks with no inflow, as of the last boundary.</summary>
    public int QuietTicks { get; private set; }

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

    /// <summary>
    /// The tick boundary. Returns the pass's result when one ran, null when the trigger did not
    /// fire. Call once per boundary, before anything pours into the next tick.
    /// </summary>
    public SheetLevelResult? AtBoundary()
    {
        bool inflow = _fluid.AddedMillilitres != _addedSeen || _fluid.DisturbedCells != _disturbedSeen;
        _addedSeen = _fluid.AddedMillilitres;
        _disturbedSeen = _fluid.DisturbedCells;
        QuietTicks = inflow ? 0 : QuietTicks + 1;

        if (QuietTicks < SettleAfterTicks || _fluid.ActiveCellCount == 0)
        {
            return null;
        }

        return Run();
    }

    /// <summary>One pass now, whatever the trigger says. Counted like any other.</summary>
    public SheetLevelResult Run()
    {
        long before = Stopwatch.GetTimestamp();
        SheetLevelResult result = _fluid.LevelSheets(SettleShare);
        double millis = (Stopwatch.GetTimestamp() - before) * 1000.0 / Stopwatch.Frequency;

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
        CellsVisited += result.CellsVisited;
        CellsWritten += result.CellsWritten;
        MillilitresMoved += result.MillilitresMoved;
        CellsWoken += result.CellsWoken;
        LargestSheet = Math.Max(LargestSheet, result.LargestSheet);
        LastMillis = millis;
        LongestMillis = Math.Max(LongestMillis, millis);
        TotalMillis += millis;
        return result;
    }
}
