using Overburden.Core.Streaming;

namespace Overburden.Core.Tests;

/// <summary>
/// The adaptive view radius policy: docs/ADAPTIVE-RADIUS-PLAN.md, tests 1–9 as rewritten after
/// the 7 m/s corridor broke the plan's dwell rule. Every test drives the pure function with
/// named frames, because the policy's "moving" is derived from the viewer's <em>chunk</em>
/// changing: a fixture that passes <c>viewerMoved: true</c> every frame is moving at a chunk a
/// frame, and one that never does is at rest from its first frame.
/// </summary>
public class ViewRadiusPolicyTests
{
    private const double Cadence = 2.0;
    private const double Frame = 1.0 / 60.0;

    private static ViewRadiusConfig Config(int ceilingH = 6, int ceilingV = 2, int slabs = 2, double cadence = Cadence) =>
        new(1, 1, ceilingH, ceilingV, LoadMargin: 2, slabs, ViewRadiusConfig.DefaultGrowBelowLoad, cadence);

    private static ViewRadiusState Step(
        ViewRadiusState state, bool moved, int backlog, double load, double dt, ViewRadiusConfig config,
        out ViewRadiusChange change) =>
        ViewRadiusPolicy.Step(state, moved, backlog, load, dt, config, out change);

    [Fact]
    public void ASlabIsTheFaceOfTheLoadedBox()
    {
        // 153 is the corridor's number at r6h/2v, margin 2; 49 is the floor's.
        ViewRadiusConfig config = Config();
        Assert.Equal(153, config.Slab(6, 2));
        Assert.Equal(49, config.Slab(1, 1));
    }

    [Fact]
    public void AtRestWithTheDeriveIdleTheBoxGrowsOneRingPerCadenceToTheCeilingAndNeverPast()
    {
        ViewRadiusConfig config = Config();
        ViewRadiusState state = ViewRadiusState.AtFloor(config);

        // The first grow waits one cadence out — the state was constructed as if just changed.
        state = Step(state, moved: false, backlog: 0, load: 0.0, dt: Cadence - 0.1, config, out ViewRadiusChange change);
        Assert.Equal(ViewRadiusChange.None, change);
        Assert.Equal(1, state.Horizontal);

        int grows = 0;
        for (int i = 0; i < 20; i++)
        {
            state = Step(state, moved: false, backlog: 0, load: 0.0, dt: Cadence, config, out change);
            if (change == ViewRadiusChange.Grew)
            {
                grows++;
            }

            // One ring per step and never two: the step after a grow, with no time passed, does
            // nothing, because it is inside the grace.
            ViewRadiusState same = Step(state, moved: false, backlog: 0, load: 0.0, dt: 0.0, config, out ViewRadiusChange none);
            Assert.Equal(ViewRadiusChange.None, none);
            Assert.Equal(state, same);
        }

        Assert.Equal(5, grows);
        Assert.Equal(6, state.Horizontal);
        Assert.Equal(2, state.Vertical);
    }

    [Fact]
    public void MovingWithABacklogOverThresholdShrinksOneRingPerCadenceToTheFloorAndNeverBelow()
    {
        ViewRadiusConfig config = Config();
        ViewRadiusState state = ViewRadiusState.AtCeiling(config);

        int shrinks = 0;
        for (int i = 0; i < 20; i++)
        {
            state = Step(state, moved: true, backlog: 100_000, load: 1.0, dt: Cadence, config, out ViewRadiusChange change);
            if (change == ViewRadiusChange.Shrank)
            {
                shrinks++;
                Assert.Equal(6 - shrinks, state.Horizontal);
            }
        }

        Assert.Equal(5, shrinks);
        Assert.Equal(1, state.Horizontal);
        Assert.Equal(1, state.Vertical);

        // A radius of zero is unreachable twice over: the policy stops at the floor, and a floor of
        // zero is refused at construction.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ViewRadiusConfig(0, 1, 6, 2, 2, 2, ViewRadiusConfig.DefaultGrowBelowLoad, Cadence).Validate());
    }

    [Fact]
    public void MovingWithABacklogAtOrUnderThresholdNeverShrinks()
    {
        // The on-foot sprint: at 7 m/s the derive keeps up and the box must not fall.
        ViewRadiusConfig config = Config();
        ViewRadiusState state = ViewRadiusState.AtCeiling(config);
        int threshold = config.ShrinkSlabs * config.Slab(6, 2);

        state = Step(state, moved: true, backlog: threshold, load: 1.0, dt: Frame, config, out ViewRadiusChange atThreshold);
        Assert.Equal(ViewRadiusChange.None, atThreshold);

        state = Step(state, moved: true, backlog: threshold / 2, load: 1.0, dt: Frame, config, out ViewRadiusChange under);
        Assert.Equal(ViewRadiusChange.None, under);
        Assert.Equal(6, state.Horizontal);

        state = Step(state, moved: true, backlog: threshold + 1, load: 1.0, dt: Frame, config, out ViewRadiusChange over);
        Assert.Equal(ViewRadiusChange.Shrank, over);
    }

    [Fact]
    public void AtRestWithAnyBacklogNeverShrinks()
    {
        // A fill at rest is not load: the whole box is in the backlog at a standing start.
        ViewRadiusConfig config = Config();
        ViewRadiusState state = ViewRadiusState.AtCeiling(config);

        for (int i = 0; i < 300; i++)
        {
            state = Step(state, moved: false, backlog: 1_000_000, load: 1.0, dt: Frame, config, out ViewRadiusChange change);
            Assert.NotEqual(ViewRadiusChange.Shrank, change);
        }

        Assert.Equal(6, state.Horizontal);
    }

    [Fact]
    public void MovingWithTheDeriveIdleGrowsAndMovingWithTheDeriveBusyDoesNot()
    {
        // The rule the 7 m/s corridor forced. A walker crosses a chunk every 1.1 s and would
        // never "dwell"; what lets the box grow under them is the derive keeping up — and what
        // keeps a flyer at the floor is the derive not keeping up, at any chunk rate.
        ViewRadiusConfig config = Config();

        // Five grows at a two-second cadence need ten seconds; 600 frames of 1/60 s sum to
        // 9.9999 s in floats and left the fifth one frame short. Twelve seconds, not ten.
        ViewRadiusState walker = ViewRadiusState.AtFloor(config);
        int grows = 0;
        for (int i = 0; i < 720; i++)
        {
            bool crossed = i % 66 == 0;
            walker = Step(walker, moved: crossed, backlog: 0, load: 0.2, dt: Frame, config, out ViewRadiusChange change);
            if (change == ViewRadiusChange.Grew) grows++;
        }

        Assert.Equal(5, grows);
        Assert.Equal(6, walker.Horizontal);

        // A flyer that has been flying carries its load. A fresh state carries none, and its
        // average has not reached the threshold when the first grace ends — so a box that starts
        // at the floor under a heavy load grows once at the first cadence before it knows, and
        // shrinks back at the next. One breath at spawn, by design; the fixture is the steady case.
        ViewRadiusState flyer = new(1, 1, 0.0, 0.0, 0.7);
        for (int i = 0; i < 600; i++)
        {
            bool crossed = i % 16 == 0;
            int backlog = i % 16 < 8 ? 40 : 0;
            flyer = Step(flyer, moved: crossed, backlog, load: 0.7, dt: Frame, config, out ViewRadiusChange change);
            Assert.Equal(ViewRadiusChange.None, change);
        }

        Assert.Equal(1, flyer.Horizontal);
    }

    [Fact]
    public void AGrowGetsAGraceInWhichTheRingItAskedForCannotShrinkIt()
    {
        // Recipe: drop the grace. A grow from r5 to r6 puts 576 chunks on the backlog, which is
        // over r6's threshold of 306, and a shrink on the very next frame would undo it while the
        // viewer is walking — the box would breathe forever at the edge of what it can afford.
        ViewRadiusConfig config = Config();
        ViewRadiusState state = new(5, 2, Cadence, Cadence, 0.0);

        state = Step(state, moved: true, backlog: 0, load: 0.1, dt: Frame, config, out ViewRadiusChange grew);
        Assert.Equal(ViewRadiusChange.Grew, grew);
        Assert.Equal(6, state.Horizontal);

        double elapsed = 0.0;
        while (elapsed < Cadence - Frame)
        {
            state = Step(state, moved: true, backlog: 576, load: 1.0, dt: Frame, config, out ViewRadiusChange during);
            Assert.Equal(ViewRadiusChange.None, during);
            elapsed += Frame;
        }

        // And once the grace is over, a ring that has not drained is an overload like any other.
        state = Step(state, moved: true, backlog: 576, load: 1.0, dt: 2 * Frame, config, out ViewRadiusChange after);
        Assert.Equal(ViewRadiusChange.Shrank, after);
    }

    [Fact]
    public void TheLoadIsAveragedOverTheCadence()
    {
        // A step from idle to saturated reads 63% one cadence later, so one busy frame — a ring
        // landing — cannot veto a grow, and one idle frame cannot license one.
        ViewRadiusConfig config = Config();
        ViewRadiusState state = ViewRadiusState.AtCeiling(config);

        state = Step(state, moved: false, backlog: 0, load: 1.0, dt: Cadence, config, out _);
        Assert.InRange(state.LightLoad, 0.62, 0.64);

        state = Step(state, moved: false, backlog: 0, load: 1.0, dt: Cadence, config, out _);
        Assert.InRange(state.LightLoad, 0.86, 0.87);

        // No time, no change.
        ViewRadiusState same = Step(state, moved: false, backlog: 0, load: 0.0, dt: 0.0, config, out _);
        Assert.Equal(state.LightLoad, same.LightLoad);
    }

    [Fact]
    public void HysteresisIsStructural()
    {
        // Over random frames: no change inside a cadence of the last one; every grow with the
        // averaged load under the threshold and an empty backlog; every shrink while moving with
        // the backlog over the current box's threshold. Watch it fail on "drop the grace" before
        // trusting it.
        var random = new Random(0x5EED);
        ViewRadiusConfig config = Config();
        ViewRadiusState state = ViewRadiusState.AtCeiling(config);

        double sinceChange = Cadence;
        double sinceMove = Cadence;
        int grows = 0;
        int shrinks = 0;

        for (int frame = 0; frame < 30_000; frame++)
        {
            bool moved = random.NextDouble() < 0.05;
            int backlog = random.NextDouble() < 0.5 ? 0 : random.Next(0, 5_000);
            double load = random.NextDouble();
            double dt = random.NextDouble() < 0.2 ? 0.5 : Frame;

            ViewRadiusState before = state;
            state = Step(state, moved, backlog, load, dt, config, out ViewRadiusChange change);

            sinceChange += dt;
            sinceMove = moved ? 0.0 : sinceMove + dt;

            if (change != ViewRadiusChange.None)
            {
                Assert.True(sinceChange >= Cadence, $"frame {frame}: changed {sinceChange:F2} s after the last change");
                sinceChange = 0.0;
            }

            if (change == ViewRadiusChange.Grew)
            {
                grows++;
                Assert.Equal(0, backlog);
                Assert.True(state.LightLoad < config.GrowBelowLoad, $"frame {frame}: grew at load {state.LightLoad:F2}");
            }
            else if (change == ViewRadiusChange.Shrank)
            {
                shrinks++;
                Assert.True(moved || sinceMove < Cadence, $"frame {frame}: shrank at rest");
                Assert.True(
                    backlog > config.ShrinkSlabs * config.Slab(before.Horizontal, before.Vertical),
                    $"frame {frame}: shrank under the threshold");
            }

            Assert.InRange(state.Horizontal, 1, 6);
            Assert.InRange(state.Vertical, 1, 2);
        }

        // The fixture has to have exercised both rules, or the assertions above are vacuous.
        Assert.True(grows > 20, $"only {grows} grows");
        Assert.True(shrinks > 20, $"only {shrinks} shrinks");
    }

    [Fact]
    public void VerticalTracksHorizontalWithinItsOwnBounds()
    {
        // The floor is 1h/1v, the 27; from 2h up the vertical is back at its ceiling, and with a
        // taller ceiling it climbs with the horizontal until it gets there.
        ViewRadiusConfig low = Config(ceilingH: 6, ceilingV: 2);
        Assert.Equal(1, low.VerticalFor(1));
        Assert.Equal(2, low.VerticalFor(2));
        Assert.Equal(2, low.VerticalFor(6));

        ViewRadiusConfig tall = Config(ceilingH: 6, ceilingV: 3);
        Assert.Equal(2, tall.VerticalFor(2));
        Assert.Equal(3, tall.VerticalFor(3));
        Assert.Equal(3, tall.VerticalFor(4));

        // And the policy applies it: one shrink from 2h/2v lands on 1h/1v, not 1h/2v (the box would
        // be 45 chunks, not 27).
        ViewRadiusState state = new(2, 2, 0.0, Cadence, 1.0);
        state = Step(state, moved: true, backlog: 100_000, load: 1.0, dt: 0.0, low, out ViewRadiusChange change);
        Assert.Equal(ViewRadiusChange.Shrank, change);
        Assert.Equal(1, state.Horizontal);
        Assert.Equal(1, state.Vertical);
    }

    [Fact]
    public void TheThresholdIsInSlabsOfTheCurrentBox()
    {
        // One backlog that shrinks r2 leaves r6 alone, because r6's slab is larger. The plan wrote
        // this the other way round and the first run said so: a backlog over r6's threshold is
        // over r2's smaller one too, so that direction cannot tell a scaled threshold from a
        // constant. The recipe is a constant count — which shrinks both or neither.
        ViewRadiusConfig config = Config();
        int backlog = (config.ShrinkSlabs * config.Slab(2, 2)) + 1;
        Assert.True(backlog <= config.ShrinkSlabs * config.Slab(6, 2), "the fixture's backlog must sit under r6's threshold");

        ViewRadiusState small = new(2, 2, 0.0, Cadence, 1.0);
        Step(small, moved: true, backlog, load: 1.0, dt: 0.0, config, out ViewRadiusChange smallChange);
        Assert.Equal(ViewRadiusChange.Shrank, smallChange);

        ViewRadiusState big = new(6, 2, 0.0, Cadence, 1.0);
        Step(big, moved: true, backlog, load: 1.0, dt: 0.0, config, out ViewRadiusChange bigChange);
        Assert.Equal(ViewRadiusChange.None, bigChange);
    }

    [Fact]
    public void AStepWithNothingToDoReturnsItsInput()
    {
        ViewRadiusConfig config = Config();

        ViewRadiusState atCeiling = ViewRadiusState.AtCeiling(config);
        Assert.Equal(atCeiling, Step(atCeiling, moved: false, backlog: 0, load: 0.0, dt: 0.0, config, out ViewRadiusChange a));
        Assert.Equal(ViewRadiusChange.None, a);

        ViewRadiusState justChanged = ViewRadiusState.AtFloor(config);
        Assert.Equal(justChanged, Step(justChanged, moved: true, backlog: 100_000, load: 0.0, dt: 0.0, config, out ViewRadiusChange b));
        Assert.Equal(ViewRadiusChange.None, b);
    }
}
