# The adaptive view radius — plan

Planned 2026-09-06, on Matthew's proposal from the sprint corridor: *"ideally as you started
sprinting you would lower the draw distance by up to 50% depending on load, and just fill in the
further details as the player starts hanging around … under load i'd be happy if we just maintain
the 27 chunks we have to for a complete box."* **Not built.** Read the 2026-09-06 worklog entry
"The sprint corridor" before this; every number below comes from it.

## The question

**Can the box shrink fast enough while flying that the world stays drawn, and grow back while the
player hangs around without costing more than the fill it replaces?** Today the radius is a
constant chosen at spawn. At the on-foot sprint of 7 m/s the default r6 draws throughout, so
there is nothing to adapt for on foot. At the flycam's 30 m/s **only the 27-chunk box draws** —
r1h/1v, 25 of 27 under `start.cmd`'s 16 ms light budget — and r2h/1v, r3 and r6 draw nothing at
any budget, because the derive demand exceeds a whole sim thread. Halving the radius, the
proposal's "up to 50%", buys nothing at 30 m/s. **The floor is a cliff edge, not a slope, so the
policy is not "scale the radius with load"; it is "fall to the floor when the derive cannot keep
up, and climb back when it can."**

## What it buys, honestly

- **At the flycam's sprint the world draws.** That is the whole of the benefit at speed, and it is
  binary: 25 of 27 at the floor against 0 of 2,601 at r6.
- **Nothing on foot.** The threshold is set so that 7 m/s at r6 never shrinks, and the 7 m/s
  corridor is the arm that proves it — adaptive must equal fixed to the digit there.
- **A faster first draw at spawn, if the fill becomes a growth.** Nothing meshes until the
  propagator drains, and the drain is global (M5c-1's coarse `Lit` term), so at r6 the first mesh
  appears at 7.3 s even though the player's neighbourhood is lit at 1.1 s. At the floor the whole
  box is lit in 1.3 s and draws at 1.5 s. **Starting at the floor and growing gives that first
  draw for free** — at a cost stated below, and only if decision 4 goes that way.
- **Nothing the derive itself costs.** Every chunk is derived ~1.8 times while moving, at every
  radius, because the leading slab derives against an absent neighbour and again when it arrives.
  This plan changes how many chunks are in the slab, not what a slab costs; the third-ring lever
  in TODO is the orthogonal change, and the two compose.

Matthew's aside about circular trees and golden-ratio spirals is not applied, and half of it is
already how the fill orders itself: the job queue and the derive queue are nearest-first, which on
a chunk grid is a discrete spiral, and growing a ring at a time is concentric shells. No new
ordering is built here.

## Design

### 1. The policy is a pure function in Core

`Overburden.Core/Streaming/ViewRadiusPolicy.cs`. No clock, no field, no Godot — the shape of
`PlayerMotion.Step`. Inputs each frame: whether the viewer's chunk changed this frame, the derive
backlog (`ChunkPipeline.LightBuildBacklog`, which exists), `dt`, and the current state. State: the
current radii, seconds since the viewer's chunk last changed, seconds since the radius last
changed. Config: the floor (`1h/1v`), the ceiling (the configured `ViewDistanceHorizontal` /
`ViewDistanceVertical`), the shrink threshold in **slabs**, and the dwell `growAfter`.

Two rules, and they are disjoint by construction:

- **Shrink** one ring when *moving* — the viewer's chunk changed within `growAfter` seconds — and
  the backlog exceeds `shrinkSlabs × slab(h, v)`, where `slab(h, v) = (2h + 5)(2v + 5)` is the
  number of chunks one chunk of travel brings into the loaded box. One ring per step, never below
  the floor.
- **Grow** one ring when *dwelling* — the viewer's chunk unchanged for `growAfter` seconds — and
  the backlog is **empty**. One ring per step, never above the ceiling.

**Hysteresis is structural rather than tuned.** A grow adds a ring's worth of backlog, and that
backlog cannot cause a shrink because a shrink needs the viewer to be moving; a shrink empties
nothing and cannot cause a grow because a grow needs a dwell. The only way from a grow to a shrink
is a move, and the only way from a shrink to a grow is `growAfter` of stillness. That is an oracle
(test 6), not a hope.

**The threshold is in slabs of the current box, so it scales itself.** The same 300-chunk backlog
is two slabs at r1 and a fifth of one at r6. A constant count would shrink a small box at the
first hitch and never shrink a large one in time.

**Vertical tracks horizontal within its own bounds**: `v = clamp(h, floorV, ceilingV)`, so the
floor is `1h/1v`, Matthew's 27, and from `h ≥ 2` the vertical is back at the ceiling. Vertical
radius is nearly free per chunk of travel (M2d) and does not deserve a second knob. A fall at
`v = 1` was already measured with `held 0` at M4d (r6h/1v, 40 m/s).

### 2. Why the backlog and not speed or saturation

- **Speed** is exact and deterministic, and wrong across builds: at 30 m/s in Release the derive
  is ~12% of a core and r6 would draw, so a speed rule would shrink a box that did not need it,
  and this machine swings 2.5× hours apart. The backlog is the thing "0 drawn" is made of, and it
  is self-correcting on any build.
- **Light-step saturation** (`LastLightStepMillis` at the budget) fires throughout a fill at rest,
  which is normal and not load. The backlog *while moving* is the condition, and "while moving"
  is the half saturation cannot supply.

### 3. Main applies it through `UpdateViewer`, one ring per frame

`ChunkStreamer.UpdateViewer(id, viewer with { HorizontalRadius, VerticalRadius })` already loads
and unloads exactly the difference between the two boxes; a radius change is the same box
difference a move is, and its shells are disjoint for the same reason. The call goes beside the
existing `MoveViewer` in `_Process`, inside the `viewer ms` bracket, so the sweep it costs lands
in the number that already measures sweeps. `ViewDistanceHorizontal` stays the *ceiling* and the
fixed radius when adaptive is off; the flythrough's turnaround stride reads it and must go on
reading the ceiling.

One ring per frame on the shrink is a bound on the sweep, not a feel choice: r6's outer ring is
576 tracked records, and `viewer ms` is 9.8 ms today for a 153-record slab. If the measurement
says a ring in one frame is a hitch, a face per frame is the fallback and is the same call.

### 4. What the shrink forgets goes to the cache, and what it loses is stated

Forgotten rings enter the retained-chunk cache with their light; at 64 MiB the cache holds
~19,000 chunks against a full r6 box of 2,601. A grow-back after a short sprint is therefore
reseeds and meshes, no generates and no derives, and the `sources` line says how many. A long
sprint's sides are new ground either way.

Tier B water in a forgotten ring **pauses** (M8b-4a) and resumes at the grow-back; a shrink over
a flood is churn, counted on the `M8b:` line as `paused` / `resumed`. Tier A is analysed on a
512 m window and does not care. The rain field's 64 m window reads ground beyond the floor's
24 m as unknown and does not kill there, so at the floor rain falls through terrain the field
cannot see; cosmetic, at 30 m/s, and recorded.

### 5. Spawn — decision 4

If the session starts at the floor the fill is a growth: first draw at ~1.5 s rather than 7.3 s,
rings appearing as each settles. **The cost is the double derive at every ring boundary**: each
step re-derives the previous box's outer shell, and the shells from `1h/1v` to `6h/2v` sum to
~2,640 chunks — roughly doubling the fill's derive count (estimate, from the box arithmetic;
uniform chunks derive by closed form and cost less than the count suggests). The alternative is
to grow straight to the ceiling in one step when dwelling: +8% derives, one large sweep, and the
whole box appearing at once at the end, as today. The progressive version is what "fill in the
further details" asks for and is recommended; the one-step version is the fallback if the
measurement's `grown` time reads as too long. The third-ring lever, if built, removes the
progressive version's cost entirely.

## What it does not do

- No directional or look-ahead box; the shrink is symmetric.
- No per-direction radii, no spiral or tree ordering, no change to the queues.
- No change to the derive: the third-ring lever is a separate planning pass against M5c-1.
- Nothing in the streamer. The only new Core type is the policy.

## Seams and reporting

- **`--adaptive=1`**, default 0, and **`start.cmd` passes it** the way it passes
  `--lightbudget=16` — every recorded streaming figure was taken at a fixed radius and stays
  reproducible with no flag (decision 3).
- **`--minradius=<h>`** the floor, default 1; **`--shrinkslabs=<n>`** the threshold, default 2;
  **`--growafter=<s>`** the dwell, default 2. **All three estimates**, swept in play.
- **`--sprintfor=<s>`**: the flythrough moves for that many seconds and then stops. The
  "hanging around" arm does not exist today — `--turnaround` reverses, `--speed=0` never moves —
  and it is the case the grow-back exists for.
- **Profile**: a `radius` line — shrinks and grows made, seconds spent at each radius, the
  smallest reached, the backlog's peak and mean, and whether the ceiling was regained by the end;
  and the `fill` line gains **`grown`**, the moment the box first stood at the ceiling with light
  settled, which is the figure comparable with a fixed arm's `light settled`. Under growth the
  existing milestones read the current box and are not comparable.
- **Periodic `M2c:` line** prints the current radius; **the HUD's status line** shows it when
  adaptive is on. That is the play's oracle: fly, hold Ctrl, watch the number fall and the world
  stay drawn; stop, watch it climb and the rings fill in.

## Tests, and what has to break them

Oracles first, break recipes beside them.

1. **Dwelling at the floor with an empty backlog grows one ring per `growAfter`, to the ceiling,
   never past it.** Recipe: grow two rings in one step; ignore the ceiling.
2. **Moving with the backlog above threshold shrinks one ring per step to the floor, never
   below.** Recipe: ignore the floor — a radius of 0 must be unreachable.
3. **Moving with the backlog below threshold changes nothing** — the on-foot sprint. Recipe:
   drop the threshold, shrink on any backlog.
4. **Dwelling with any backlog never shrinks** — a fill at rest is not load. Recipe: drop the
   moving clause.
5. **Moving with an empty backlog never grows.** Recipe: drop the dwell clause; growth while
   moving is the oscillation the design exists to exclude.
6. **Structural hysteresis, fuzzed**: over random input sequences, a grow is never followed by a
   shrink without a move between them, and a shrink never by a grow without `growAfter` of
   stillness. Watch it fail on recipe 5 before trusting it.
7. **Vertical tracks horizontal within its bounds**: `1h → 1v`, `2h.. → ceiling v`. Recipe: leave
   vertical at the ceiling at the floor (the box is 45, not 27).
8. **The threshold is in slabs of the current box**: one backlog that shrinks r6 leaves r2 alone.
   Recipe: a constant count.
9. **Idempotent with no time and no change**: same inputs, same state out.
10. **Streamer: a smaller radius through `UpdateViewer` leaves every record inside the new box
    untouched, puts exactly the ring into the cache, and a grow-back hits the cache for the whole
    ring with zero generates.** Recipe: rebuild the box instead of the difference — fails on
    record identity. Both orders, shrink then grow and grow then shrink.
11. **Pipeline: `LightBuildBacklog` under a zero budget falls by exactly one per update** — the
    zero-budget oracle the derive-queue slice built. Recipe: count enqueues rather than pending.
12. **The wire, headless**: the 30 m/s corridor with `--adaptive=1` ends with `drawn > 0` and the
    `radius` line at the floor; with `--adaptive=0` it ends at 0. And at 7 m/s the `radius` line
    reads "never left r6" — the threshold's oracle in play form. M5c-2's rule: every layer tested
    and the wire between them is where the hole was.

Fixture trap to name in advance: the policy's "moving" is derived from the viewer's *chunk*
changing, so a fixture that moves the position by less than a chunk is dwelling, and one that
steps the position every frame by a chunk is moving at 480 m/s. Say which each fixture is.

## Measurement

Debug, headless, `--lightbudget=7` standing in for play's 16 ms at 60 fps, and the 4 ms default
beside it; `--warmup=12` so the initial fill is out of the count.

- **The corridor at 30 m/s, fixed r6 against adaptive**: drawn at the end, sim thread %, the
  `radius` trajectory, and how many seconds the shrink took to reach the floor.
- **The corridor at 7 m/s**: adaptive equals fixed to the digit. A boolean.
- **A standing start, fixed against adaptive-from-floor**: `first draw` (expect ~1.5 s against
  7.3), `grown` (expect later than the fixed `light settled` of 8.5 s by the ring re-derives),
  and the derive count (the estimate above, measured).
- **`--sprintfor=10 --speed=30`, then dwell**: seconds from the stop to `grown`, and the `sources`
  line's cache hits during the grow-back — the arm the grow-back exists for.
- **The 20 m/s turnaround** (M5d-1's cliff arm): adaptive against fixed drawn.
- **`viewer ms` max** across all of them: the number that says whether a ring per frame is right.
- **Windowed, Matthew's**: fly, sprint, stop; the HUD's radius and whether the fill-in reads as
  filling in or as popping.

## Costs stated

- **Every recorded streaming figure assumes a fixed radius.** Off by default keeps them; the
  switch lives in `start.cmd`.
- **A grow from the floor pays the double derive at every ring boundary**, ~2,640 extra derives
  from `1h/1v` to `6h/2v`, estimate. The fill draws sooner and completes later. The third-ring
  lever removes it.
- **A ring's box difference in one frame**: 576 records at r6's outer ring. Measured before it is
  believed; a face per frame is the fallback.
- **At the floor you see 16 m ahead**, half a second at 30 m/s. r2h/1v does not draw at that
  speed, so there is no middle to offer.
- **Rain at the floor falls through unseen ground beyond 24 m.** Cosmetic; recorded.

## Decisions handed back, recommendation first

1. **The floor: `1h/1v`, the 27.** There is no middle — r2h/1v is dark at 30 m/s.
2. **The signal: the derive backlog in slabs of the current box, shrink only while moving.** Over
   speed (build-dependent) and over light-step saturation (fires at rest).
3. **Default off, `--adaptive=1` in `start.cmd`.** The `--lightbudget=16` precedent; the alternative
   is on by default and every baseline moves.
4. **Spawn at the floor, so the fill is a growth.** First draw ~1.5 s for roughly double the
   fill's derives; the one-step grow is the fallback if `grown` reads too long.
5. **This before the third-ring lever.** It is small and Main-side; the lever wants its own pass
   against M5c-1 and then removes this slice's grow cost.
6. **`--shrinkslabs=2`, `--growafter=2`** — estimates, to be swept in play.

## Revisit triggers

- A play where the fill-in reads as popping rather than filling: the one-step grow, or a slower
  `growAfter`.
- `viewer ms` max over a frame at the shrink: a face per frame.
- The third-ring lever built: re-measure decision 4's cost, which it should erase.
- A Release build in play: the backlog rule should simply never fire at 30 m/s, and the `radius`
  line will say so.
