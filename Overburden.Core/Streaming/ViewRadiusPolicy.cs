namespace Overburden.Core.Streaming;

/// <summary>
/// The bounds and thresholds of the adaptive view radius. docs/ADAPTIVE-RADIUS-PLAN.md.
/// </summary>
/// <param name="FloorHorizontal">The smallest horizontal radius; Matthew's 27-chunk box is 1.</param>
/// <param name="FloorVertical">The smallest vertical radius; 1 for the 27-chunk box.</param>
/// <param name="CeilingHorizontal">The configured view distance, which the box returns to when the derive is idle.</param>
/// <param name="CeilingVertical">The configured vertical view distance.</param>
/// <param name="LoadMargin">The streamer's load margin, which is part of what a slab is.</param>
/// <param name="ShrinkSlabs">
/// How many slabs of derive backlog it takes to shrink — a slab being the chunks one chunk of
/// travel brings into the loaded box at the <em>current</em> radius, so the threshold scales with
/// the box. Two by default, an estimate.
/// </param>
/// <param name="GrowBelowLoad">
/// The light step's share of its budget, averaged over <see cref="CadenceSeconds"/>, below which
/// there is room for one more ring. Half by default, and that is arithmetic rather than taste:
/// one ring out adds at most 29% to a slab (r1 → r2), so a box growing at under half load
/// arrives at under two thirds, and a grow can never be the thing that causes a shrink. A larger
/// value lets the box breathe at the boundary; a smaller one leaves rings unused.
/// </param>
/// <param name="CadenceSeconds">
/// Three things at once, deliberately one knob: the least time between radius changes; the grace
/// after a grow in which the shrink rule may not judge the ring it just added; the time constant
/// of the load average; and how recently the viewer's chunk must have changed to count as
/// moving. Two seconds by default, an estimate.
/// </param>
public readonly record struct ViewRadiusConfig(
    int FloorHorizontal,
    int FloorVertical,
    int CeilingHorizontal,
    int CeilingVertical,
    int LoadMargin,
    int ShrinkSlabs,
    double GrowBelowLoad,
    double CadenceSeconds)
{
    public const int DefaultFloor = 1;
    public const int DefaultShrinkSlabs = 2;
    public const double DefaultGrowBelowLoad = 0.5;
    public const double DefaultCadenceSeconds = 2.0;

    /// <summary>The recommended defaults around a configured ceiling.</summary>
    public static ViewRadiusConfig Default(int ceilingHorizontal, int ceilingVertical, int loadMargin) =>
        new(DefaultFloor, DefaultFloor, ceilingHorizontal, ceilingVertical, loadMargin,
            DefaultShrinkSlabs, DefaultGrowBelowLoad, DefaultCadenceSeconds);

    /// <summary>
    /// Throws if the bounds cannot hold. A floor of zero is refused outright: a box with no
    /// neighbours is one the player gate cannot stand in, and nothing here should be able to
    /// reach it by counting down.
    /// </summary>
    public void Validate()
    {
        if (FloorHorizontal < 1 || FloorVertical < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(FloorHorizontal), "the floor is at least one chunk each way");
        }

        if (CeilingHorizontal < FloorHorizontal || CeilingVertical < FloorVertical)
        {
            throw new ArgumentOutOfRangeException(nameof(CeilingHorizontal), "the ceiling is at least the floor");
        }

        if (LoadMargin < 0 || ShrinkSlabs < 0 || !(CadenceSeconds >= 0.0) || !(GrowBelowLoad >= 0.0))
        {
            throw new ArgumentOutOfRangeException(nameof(ShrinkSlabs), "margin, slabs, load and cadence are non-negative");
        }
    }

    /// <summary>
    /// The chunks one chunk of travel brings into the loaded box at these radii: the face of the
    /// loaded box, margin included. 153 at r6h/2v with margin 2, which is the corridor's number.
    /// </summary>
    public int Slab(int horizontal, int vertical) =>
        ((2 * horizontal) + (2 * LoadMargin) + 1) * ((2 * vertical) + (2 * LoadMargin) + 1);

    /// <summary>
    /// The vertical radius that goes with a horizontal one: it tracks the horizontal within its
    /// own bounds, so the floor is <c>1h/1v</c> and from <c>2h</c> up the vertical is back at
    /// its ceiling. Vertical radius is nearly free per chunk of travel (M2d) and does not earn a
    /// second knob.
    /// </summary>
    public int VerticalFor(int horizontal) => Math.Clamp(horizontal, FloorVertical, CeilingVertical);
}

/// <summary>What the policy carries between frames.</summary>
/// <param name="Horizontal">The current horizontal radius.</param>
/// <param name="Vertical">The current vertical radius.</param>
/// <param name="SecondsSinceMove">Since the viewer's chunk last changed.</param>
/// <param name="SecondsSinceRadiusChange">Since the box last shrank or grew.</param>
/// <param name="LightLoad">
/// The light step's share of its budget, averaged over the cadence. Zero is idle, one is a step
/// that used its whole budget every frame.
/// </param>
public readonly record struct ViewRadiusState(
    int Horizontal,
    int Vertical,
    double SecondsSinceMove,
    double SecondsSinceRadiusChange,
    double LightLoad)
{
    /// <summary>
    /// At the floor, as if the viewer had just moved and the box had just changed, so the first
    /// grow waits one cadence and the fill it starts is judged by the load it produces.
    /// </summary>
    public static ViewRadiusState AtFloor(ViewRadiusConfig config) =>
        new(config.FloorHorizontal, config.FloorVertical, 0.0, 0.0, 0.0);

    /// <summary>At the ceiling, at rest.</summary>
    public static ViewRadiusState AtCeiling(ViewRadiusConfig config) =>
        new(config.CeilingHorizontal, config.CeilingVertical, config.CadenceSeconds, config.CadenceSeconds, 0.0);
}

/// <summary>What one step did to the radius.</summary>
public enum ViewRadiusChange
{
    None,
    Shrank,
    Grew,
}

/// <summary>
/// The adaptive view radius: fall to the floor while moving with a derive backlog, climb back a
/// ring at a time while the derive is idle enough to afford one more. A pure function of the last
/// state and this frame's facts — no clock, no field, no Godot — in the shape of
/// <c>PlayerMotion.Step</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two rules and a grace.</b> A shrink needs the viewer to be <em>moving</em> — its chunk
/// changed within the cadence — and the derive backlog to exceed
/// <see cref="ViewRadiusConfig.ShrinkSlabs"/> slabs of the current box. A grow needs the backlog
/// empty and the light step's averaged load under <see cref="ViewRadiusConfig.GrowBelowLoad"/>.
/// Neither may fire within one cadence of the last change: that grace is what lets a grow put a
/// ring's worth of derives on the backlog without the shrink rule reading the ring it just asked
/// for as an overload.
/// </para>
/// <para>
/// <b>The plan's rule was a dwell, and the 7 m/s corridor broke it on its first run.</b> "Grow
/// only while the viewer's chunk has not changed for two seconds" reads as "hanging around" and
/// means "never while walking": a chunk is crossed every 1.1 s at 7 m/s, so a box that started at
/// the floor stayed there for the whole run, 20 drawn against the fixed arm's 344. What separates
/// a walker from a flyer is not whether the chunk changes but whether the derive keeps up, and
/// the light step's share of its budget is that fact directly. It is also what makes the rule
/// right on every build: in Release the derive is six times cheaper, the load at 30 m/s is low,
/// and the box grows because it can.
/// </para>
/// <para>
/// <b>Why the box cannot breathe.</b> One ring out adds at most 29% to a slab, so a grow taken
/// under half load lands under two thirds and keeps up; the shrink rule then has nothing to see
/// once the grace has let the ring drain. The threshold is arithmetic, not tuning — see
/// <see cref="ViewRadiusConfig.GrowBelowLoad"/> — and the fuzz in <c>ViewRadiusPolicyTests</c>
/// pins what it can: no change inside a grace, no grow over the load, no shrink under the
/// threshold.
/// </para>
/// <para>
/// <b>The corridor's result is why this is a floor and not a slope.</b> At the flycam's 30 m/s
/// r2h/1v is as dark as r6, so there is nothing to scale along; see the 2026-09-06 worklog.
/// </para>
/// </remarks>
public static class ViewRadiusPolicy
{
    /// <summary>
    /// One frame. <paramref name="viewerMoved"/> is whether the viewer's <em>chunk</em> changed
    /// this frame; <paramref name="backlog"/> is the pipeline's derive backlog;
    /// <paramref name="lightLoad"/> is this frame's light step as a share of its budget (zero
    /// idle, one saturated); <paramref name="dt"/> the frame's length. Returns the new state and
    /// reports what changed.
    /// </summary>
    public static ViewRadiusState Step(
        ViewRadiusState state,
        bool viewerMoved,
        int backlog,
        double lightLoad,
        double dt,
        ViewRadiusConfig config,
        out ViewRadiusChange change)
    {
        double sinceMove = viewerMoved ? 0.0 : state.SecondsSinceMove + dt;
        double sinceChange = state.SecondsSinceRadiusChange + dt;
        double load = Average(state.LightLoad, lightLoad, dt, config.CadenceSeconds);

        // Moved this frame counts as moving whatever the cadence is, so a cadence of zero still
        // means "shrink only on the frame the viewer crossed a boundary" rather than "never".
        bool moving = viewerMoved || sinceMove < config.CadenceSeconds;
        bool inGrace = sinceChange < config.CadenceSeconds;

        int horizontal = state.Horizontal;
        change = ViewRadiusChange.None;

        if (!inGrace)
        {
            if (moving
                && horizontal > config.FloorHorizontal
                && backlog > config.ShrinkSlabs * config.Slab(horizontal, state.Vertical))
            {
                horizontal--;
                change = ViewRadiusChange.Shrank;
            }
            else if (backlog == 0
                     && load < config.GrowBelowLoad
                     && horizontal < config.CeilingHorizontal)
            {
                horizontal++;
                change = ViewRadiusChange.Grew;
            }
        }

        if (change != ViewRadiusChange.None)
        {
            sinceChange = 0.0;
        }

        // The vertical is a function of the horizontal, recomputed only when the horizontal moved,
        // so a step that does nothing returns exactly its input.
        int vertical = change == ViewRadiusChange.None ? state.Vertical : config.VerticalFor(horizontal);

        return new ViewRadiusState(horizontal, vertical, sinceMove, sinceChange, load);
    }

    /// <summary>
    /// An exponential average with the cadence as its time constant: a step from idle to
    /// saturated reads 63% one cadence later. A cadence of zero is no averaging at all.
    /// </summary>
    private static double Average(double previous, double sample, double dt, double cadence)
    {
        if (cadence <= 0.0 || dt >= cadence * 50.0)
        {
            return sample;
        }

        double alpha = 1.0 - Math.Exp(-dt / cadence);
        return previous + ((sample - previous) * alpha);
    }
}
