# The front advance — water that spreads sideways faster

Matthew, 2026-09-24, over a screenshot of a trench spilling into a valley: *"What I am dreaming of
is a way water can spread laterally (horizontally) out faster"*. Of four ideas (the solve pushes
the front; a sideways run in the tick like the fall rule; a faster tick; the counted divisor again)
he took the first: *"yes write the plan for idea 1"*. **Planned and built the same day — slices
a to c; read "Built" at the end. Slice d, the look, is Matthew's.** Seven facts
from the code, eight decisions each with a recommendation, a split, and the tests and measurements
that come before anything is believed.

## The idea in one paragraph

The sheet solve levels a sheet — the wet, supported cells at one Y that touch sideways — to within
a millilitre, and **deliberately never extends it onto dry ground**: *"the frontier keeps creeping
at the automaton's own pace"* (`FluidSimulation.Sheets.cs`, the class remarks). The front advance
lets a sheet the pass is levelling anyway take in up to *N* rings of the dry, holdable, supported
cells beside it at the same Y, and level over the lot, so the edge of a pour moves *N* cells a pass
where it crept one every few ticks. It is the fall rule's shape turned on its side: M8d-3 lets a
visit drop water up to sixteen cells through vacant air because that is where the water was going;
this lets a pass put water up to *N* cells across a flat floor for the same reason.

## Seven facts

1. **The front today is diffusive and capped at the tick.** A visit transfers only to face
   neighbours, 0.25 m, at 2 Hz (`Main.FluidTickSeconds`, a constant), so the edge can never beat
   0.5 m/s and in practice goes as the square root of time. The counted divisor moved it a fifth
   (September; *"they both look like water"*). The `--front` bench's recorded figures: wet radius
   20 cells at tick 20 under the fixed rule with the solve, 35 under counted.
2. **The solve already knows the front.** `Pop` flags a sheet cell `CellFront` when a sideways
   neighbour is holdable and not the sheet's — dry, or wet and falling — and `WakeAfterSolve` wakes
   every front cell of a levelled sheet so the automaton spreads from it. The advance starts from
   exactly those cells; the rings are a flood outward from them, the way the sheet flood goes
   inward.
3. **"Supported" is the no-siphons rule's floor.** `IsSupported`: the cell below is unholdable or
   full. A dry cell at a lip (open air under it) is not supported, so a ring stops there by the same
   test the sheet uses, and the front cell beside the lip — still a front, still woken — pours over
   through the automaton and the fall rule. Nothing crosses a Y.
4. **Every move the solve makes is a `FluidGrid.Transfer` between two cells of one sheet**, so
   conservation needs no new term; an advanced cell is a cell of the sheet being levelled, reached
   through holdable supported cells at its Y. The identities stay as they are.
5. **A cell that has just got wet rises from its floor** (`FluidSurfaceMesher` remarks — *"a cell
   that has just got wet rises from its floor, which is the plan's stated default"*), and the
   `tick_phase` lerp draws that over the half-second between ticks. So a front that advances *N*
   cells a pass should read as water running across the floor rather than a sheet snapping outward
   — a prediction, and decision 7's screenshot is its check.
6. **On natural ground the water is Tier A's once it rests.** `SurfaceHandoff` hands a rested cell
   standing on the seed plane to the basin under it (a 50,000 L surface spill is the basin's in
   about twelve seconds). So the advance matters most where the hand-off does not reach: **dug
   ground** — mine floors, tunnels, his trench — **placed floors and enclosures**. On grass it makes
   a spill rest over a wider area sooner, which the hand-off then takes sooner; that is a side
   effect, not the point.
7. **Two rules already decide which sheets a pass levels, and both would bound the advance.** The
   share: a sheet with fewer than one cell in eight unsettled is frozen and left to the automaton
   (the 162,572-cell lake with a creeping edge). And the quiet rule: a sheet in a chunk that
   received water within two ticks is *still being fed* and not levelled, so a stream down a tunnel
   stays a stream. A pour's puddle is levelled a second after it lands and then, as it grows, its
   disturbed edge becomes a smaller share of it until it freezes; a pump's outlet feeds its sheet
   every boundary and is never levelled at all.

## Eight decisions

### 1. Advance only the sheets the pass levels — *recommended*

The advance is a step inside `Solve` after `LevelTargets`/`PackTargets` has been decided and before
`Apply`: the rings are flooded from the sheet's front cells, added to the sheet, and the targets
computed over the larger sheet. **A frozen sheet is not advanced** (its flood stopped at layer
three; its total is unknown), **nor is a sheet still being fed**. Cost stated: past the radius at
which a pour's edge falls under one cell in eight, the advance stops and the edge creeps as it does
today; and water from a pump spreads at the automaton's pace. Decisions 5 and 6 are where those
come back. Rejected: advancing every sheet whatever the share — that re-floods the big lake at
every boundary for its edge, the defect the share was built against.

### 2. How far: *N* whole rings a pass, `--frontadvance=<rings>` — *recommended, N from the bench*

A ring is one breadth-first layer of dry, holdable, supported cells at the sheet's Y, sideways from
the current front. Whole rings, so the new edge is a contour and not a ragged patch that depends on
flood order. **N = 4 is an estimate** (1 m a pass, 2 m/s at 2 Hz — four times the tick's ceiling
today); the bench picks it. **0 is off and is the constructor's default**, the pass cap's rule: every
bench figure recorded so far reproduces, and the game passes the shipped value.

### 3. How thin: stop before a ring would take the share under two litres a cell — *recommended*

Adding a ring spreads the sheet's total over more cells. The advance takes rings while `total /
cells` stays at or above `2 × MinimumSpreadMillilitres` (2 L; an estimate) and stops before the one
that would not. Under it the automaton's own threshold and the pack take over as they do today, so
a pour ends as a puddle, never as a film that the next pass packs back into blobs. The factor of
two keeps a margin over the spread threshold so the new edge can still creep.

### 4. The lip, the lake and the unloaded chunk stop a ring — *recommended*

A ring takes a cell only if `CanHold` (so rock, a lake's band and an unloaded chunk all stop it —
the terrain's own no) and `IsSupported` (so a lip stops it). **Rejected: advancing over a lip** — it
would put water on the far side of a drop the automaton has to fall down, a teleport. The front cell
at the lip stays a front and is woken; the water goes over the edge the way it does now, through the
fall rule.

### 5. Two sheets that meet — *the first sheet's cells are wet to the second* — *recommended*

Sheets are applied one after another within a Y. A ring flood sees the grid as the earlier sheets
left it, so a dry gap between two sheets is claimed by whichever advances first, and the second's
ring stops at the first's new cells (they are wet, not dry). The two are one sheet at the next pass
and levelled together, the step rule catching any difference in height. Deterministic because the
sheets are judged in a fixed order from sorted seeds. Rejected: merging them within the pass — it
would need a second flood and changes the union-find for a case one pass resolves.

### 6. A sheet still being fed — *not at slice 1, revisit on the play* — *recommended*

The quiet rule exists so a stream reads as a stream. An outlet's sheet, fed every boundary, is the
case his screenshot most likely shows, and slice 1 does not advance it. **The revisit trigger is
his play**: if water from a pump or a breach still reads as slow, slice 2 advances fed sheets
without levelling their interior — rings from the front, filled from the front band only — which is
a different mechanism with its own conservation argument, and gets its own pass.

### 7. The look is judged by a screenshot and by his play — *recommended*

Fact 5 predicts the advance draws as water running, not snapping. A windowed screenshot pair
(`--frontadvance=0` against 4, `R` on a placed stone floor, a few seconds in) and one play sentence
settle it. A dug floor, not grass, so the hand-off does not take the puddle first (fact 6).

### 8. A tick-rate flag beside it, as a comparison arm — *recommended, cheap*

`FluidTickSeconds` becomes `--fluidhz=<n>` (2 shipped). Doubling it doubles every speed — falling
included — and every per-second cost, and it is the one-line answer to "is the advance better than
just ticking faster". Not a lever to ship; an arm to look at beside the advance.

## The split

| part | what | check |
|---|---|---|
| **a — Core** | the ring flood in `Solve`, the thinness stop, the wake of the new front, the result's `Advanced` cells count, `--frontadvance` threaded through `SheetSettle` and the worker tuple like the cap | the tests below; the battery; the whole suite at 0 |
| **b — the bench** | `--front --frontadvance 0,1,2,4,8`: wet radius at the checkpoints, ticks to rest, visits, the pass's ms; a dug-floor pour and a spill arm | radius against ticks per N; where the freeze (fact 7) flattens the curve |
| **c — the wire** | the game flag, the `settle` line's `advanced N cell(s) over R ring(s)`, `--fluidhz` | a headless pour on a copy of a played mine floor, identities exact, 0 `ERROR` |
| **d — the look** | the screenshot pair and his play | decision 7's sentence |

## Tests, before any number is believed

- **Advance 0 is today's pass to the millilitre**, on the existing solve fixtures.
- **Every newly wet cell is the definition**: same Y as its sheet, holdable, supported, and within
  *N* sideways steps of the sheet's pre-pass cells through cells of that kind. Checked against a
  brute-force flood over the fixture, not the subject's own rings.
- **Conservation exact**, and **nothing crosses a Y**: every other layer unchanged to the millilitre.
- **A lip stops a ring**: a floor ending at a drop gets no water on the far side, and the front
  cell at the lip is woken.
- **The thinness stop**: a small pour on a wide floor stops advancing at the ring before the share
  falls under two litres, never after.
- **`VerifyQuiescent` after the next tick** — the wake set is complete: the new edge is woken.
- **A frozen sheet and a fed sheet are not advanced.**
- **Two sheets across a gap** meet deterministically and are one sheet at the next pass.
- **The cap counts the rings**: a pass's items include the cells the advance tested.
- A break-recipe battery on each of those, as for the cap.

## What it does not do

It does not make water faster down a slope, which is the fall rule and the automaton; it does not
move water between Ys; it does not change Tier A, which already solves a lake's level in one go;
and at slice 1 it does not touch a sheet that is being fed.

## Built — slices a, b and c (2026-09-24)

On *"As recommended, build it please"*, the same afternoon as the plan. Slice d, the look, is his.

**Core.** `FluidSimulation.Advance` runs inside `Solve` for a sheet the pass levels, before its
targets are set: whole rings from the sheet's front cells over dry, holdable, supported cells at
its Y, each taken only while `total ≥ 2 L × cells` (`AdvanceFloorMillilitres`); the new cells are
`Discover`ed into the sheet, so `Apply` fills them through `FluidGrid.Transfer` and conservation
needs no term. Then the front is recomputed for the old front and the new cells (`IsFrontOf`: a
holdable side that is not this sheet's, **including a cell of another sheet this pass reached
across a gap**), so the wake reaches the new edge and the old edge is listed as resting.
`SheetSettle.DefaultFrontAdvance` is 4 and the constructor defaults to 0 (the cap's rule); the
worker tuple carries it; the hand-named-seeds overload takes it too, defaulting to 0, so the
unload path is unchanged and a test can solve water nothing woke.

**Thirteen tests** (`FrontAdvanceTests`), among them the definition against a brute-force flood,
whole rings (24N + 2N(N − 1) from a 6 × 6 square — sideways steps, so a corner is two), the lip,
the thinness stop computed from the pour's own total, the settle's quiescence after an advance,
frozen and fed sheets untouched, two sheets across a gap in either order, the cap counting the
rings, a sleeping puddle beside a ring keeping its water, and **a corridor closed at both ends,
water put down asleep, where a seam between a deep and a shallow sheet is stranded unless each is
the other's front** — in an open fixture the tick's own wake cascade repairs the seam from its ends,
which is why the first battery missed it. Ten recipes, nine caught; the tenth (the pack test
reading the pre-advance count) is an identity, because an advance only runs while the total covers
2 L a cell, which is always above the pack's 999 mL on either count.

**The bench found what the tests did not**: `_cellFlags[Discover(cell, root)] = …` reads the array
before the call, and `Discover` grows the arrays — the write went into the old one the first time a
pass crossed 256 records inside an advance. No fixture had; the front bench's first advance arm
threw. Two statements now, and `AnAdvanceThatGrowsThePassPastItsRecordArrays…` throws on the
one-liner and passes on the fix.

**The bench (`--front --frontadvance 0,1,2,4,8`)**, 10,000 L on the flat floor of a 3-chamber
block (walls 48 cells from the centre), the solve at the game's quiet interval:

| rings | radius at tick 5 | 10 | 20 | wet at tick 10 | rested | solve ms in all |
|---|---|---|---|---|---|---|
| 0 | 11 | 16 | 26 | 1,047 | 45.0 s | 337 |
| 1 | 15 | 25 | 45 | 1,705 | 37.5 s | 284 |
| 2 | 19 | 34 | wall | 2,771 | 39.5 s | 143 |
| **4** | **27** | **wall** | wall | **5,171** | 43.5 s | **116** |
| 8 | 37 | wall | wall | 5,364 | 34.0 s | 90 |

**At 4 the pool is at the walls, 12 m out, by tick 10 — five seconds — where it took ~25 s; the
solve costs a third of what it did**, because a sheet that has spread needs less re-levelling. Every
arm rests for real with every litre accounted for. 4 ships; 8 buys only the first two seconds. The
freeze (fact 7) did not bite at this pour's size; a bigger floor is where it would show.

**The wire.** `--frontadvance=<rings>` (4; 0 the control) and `--fluidhz=<n>` (2; 0.25–20). The tick
length is a field now, handed to the machine tick's boundary and — for the first time — to the seep
drain, which had been built with its own 0.5 s default, so a pump's and a wall's litres a second do
not move with the rate; the settle's quiet interval is in ticks and does. The `settle` line prints
`front advance N ring(s): M cell(s) in all`. Headless, a 50,000 L pour on the worker: shipped
`2,899 cell(s)`, `--frontadvance=0` `no front advance`, `--fluidhz=4` 100 ticks in the window
against 50; 0 `ERROR` and every identity exact in all three. Suite 1,863; r6 control to the digit.

## Slice 2 — the planning pass (2026-09-24, evening)

Matthew, after playing slices a–c on `Regions13`: *"very little difference to the 4, look overall
its better so I am not complaining, do you have any other ideas how we could get more horizontal
movement?"* — then *"yes write the slice 2 plan"*. Decision 6 promised this slice on his play, and
the play's log is the reason it is shaped the way it is. **Not built.** Six facts, seven decisions,
a split.

### Six facts

1. **His session's settle line: 39,735 sheets levelled, 101,991 left under the share, 184,380 left
   alone for *still being fed*** — over 2,215 passes, ~83 fed sheets a pass against ~18 levelled.
   9.86 ML was drawn from the lake into his pit in the session. So the advance reached the one sheet
   in five that was levelled; the four others were fed, which is why 8 rings read like 4.
2. **"Fed" is chunk-granular and volume-blind.** `NoteInflow` stamps the voxel's *chunk* with the
   tick on every `Add` (the relay's `R`, the portal drain, seepage, rain drops, rain on the player's
   material), every `Emit` (a pump's outlet), every disturbance under water and the collapse's
   re-pour; `IsBeingFed` calls a sheet fed if any of its cells stands in a chunk stamped within the
   quiet window (two ticks). One 15.6 L rain drop, or one litre sweated out of a wall, makes every
   sheet touching that 8 m chunk "a stream" for a second.
3. **Sweat-mode seepage delivers whole litres per group, and at the shipped scale a wet room's
   wall owes a litre every boundary or two** (1.76 L/s through a 10 × 10 × 3 m stone room under
   5 m of head). So every dug chunk under a lake's table with a seep face is fed more or less
   permanently, and every sheet in it is never levelled — before the advance existed as well as
   after. **An estimate, not a count**: the settle line does not say why a sheet was fed, which is
   decision 1.
4. **The quiet rule exists for streams** (`IsBeingFed`'s remarks): levelling a sheet under inflow
   flattens the gradient that carries the flow, and a stream down a tunnel rises as a flat sheet
   instead of flowing — the arm `--settleafter=0` keeps, and the one the recorded play figures
   (80 s at 2 against 12 s at 0) turned on. Whatever changes must keep a real stream a stream.
5. **A fed sheet is skipped after its flood completes** — it is not frozen and it is worth
   levelling, so its cells, its total and its front are all known when `IsBeingFed` says no. Fed
   sheets already pay the flood; the skip saves only the targets and the writes.
6. **The advance and the levelling are separable.** `Advance` adds rings to the sheet; the targets
   are a separate step (`LevelTargets` / `PackTargets`). A fed sheet can take the same rings with a
   different target rule — one that does not flatten it.

### Seven decisions

#### 1. Count why a sheet was fed before changing what fed means — *recommended, first*

The settle line gains the fed sheets split by **the inflow into their chunks over the quiet window
as a share of the sheet's own total**: under 0.1%, under 1%, under 10%, and more. Source-blind on
purpose — a litre is a litre whether it sweated, rained or was pumped — and it needs only a per-chunk
millilitre count beside the tick `NoteInflow` already stamps. One play with it says how much of the
184,380 is trickle and how much is stream, which is what decision 2's threshold is picked from.

#### 2. A sheet is fed only when its inflow is a real share of it — *recommended*

`IsBeingFed` becomes: the millilitres that entered the sheet's chunks within the quiet window are at
least **X% of the sheet's total** (1%, an estimate until decision 1's count), not merely more than
zero. A sweating wall, a rain drop, an edit under a still lake: trickles, levelled and advanced like
any other sheet. A breach pouring a lake into a tunnel, a pump outlet: streams, left alone as now.
A large lake touched by a small inflow is levelled, which is right — relative to its volume it is
still. **`--fedshare=<percent>`**, and **0 is the old rule** (any inflow is fed), the control arm.
Cost: a per-chunk millilitre sum aged over K + 1 ticks, and one sum over a fed sheet's distinct
chunks — the chunks are already walked by `IsBeingFed`. The recorded settle-interval figures were
taken on a breach, which stays fed, so they should hold; decision 7 checks.

#### 3. A sheet that is still fed advances *pro rata* — *recommended, second*

For a real stream, the rings are taken exactly as today (fronts, whole rings, the 2 L floor) and
filled by **scaling the sheet** rather than levelling it: every existing cell keeps
`cells / (cells + new)` of what it held, and each new cell gets the sheet's new mean. The profile —
the gradient that makes the stream flow — keeps its shape, lowered by that factor; the edge moves
out N cells a pass. Conservation is `Transfer`s inside the sheet as now, with the rounding remainder
handed out a millilitre at a time in flood order, the level rule's way. **Rejected: lending from the
edge band only** — a stream's edge is its thinnest water, so the 2 L floor would stop it almost
every time. **Rejected: advancing a fed sheet by levelling it** — that is the flat sheet the quiet
rule was built to prevent. Cost stated: the whole sheet is written, as a levelling is, and a stream
of *n* cells that gains *m* loses `m / (n + m)` of its gradient for a tick, which the automaton's
next visits restore from the inflow.

#### 4. Order: 1, then 2, then 3 only if the play still wants it — *recommended*

Decision 2 is cheap, source-blind and may by itself move most of the 184,380 into the levelled
column. Decision 3 is a new target rule with its own proofs; build it only if a play after 2 still
reads a stream as slow. Each is a slice with its own tests.

#### 5. The thinness floor, the lip, the cap and the frozen sheet are unchanged — *recommended*

The same `AdvanceFloorMillilitres`, the same `IsSupported`, the rings counted by the cap, and a
sheet under the share still not advanced. Nothing in slices a–c is reopened.

#### 6. Tests — *recommended*

For 2: a puddle beside a sweating wall (inflow a trickle) is levelled and advanced; the same puddle
under a pour of 10% of its volume a tick is fed and left; a large still lake touched by a litre is
levelled; `--fedshare=0` is today's pass to the millilitre. For 3: along a channel fed at one end,
the gradient's sign is kept cell for cell after a pro-rata pass (a stream is still a stream);
conservation; quiescence after the settle; rings identical to the level rule's for the same sheet
and N; the remainder never makes a cell exceed its capacity. A break-recipe battery on each.

#### 7. The measurement and the look — *recommended*

`--front` gains a fed arm (the ball poured over several ticks from a source that keeps pouring),
radius at the checkpoints under `--fedshare=0` / 1 and with pro rata on or off. In the game, the
settle line's fed split before and after on a copy of his `Regions13`, identities exact. And his
sentence: does the breach's water run across the pit floor now, and does it still read as a stream.

### The split

| part | what | check |
|---|---|---|
| **2a — count** | per-chunk inflow millilitres; the fed split on the `settle` line | one play's split: trickle against stream |
| **2b — volume-aware fed** | `IsBeingFed` by share, `--fedshare=` (1%; 0 the control) | the tests; the fed column falls on the copy; the bench's fed arm |
| **2c — pro rata** | the scaled target rule for a sheet still fed, after the same rings | the stream test; the fed arm's radius; his play |

### Slices 2a and 2b — built, and what the count said (2026-09-24, evening)

On *"As recommended, build it please"*. **2a**: every chunk keeps an eight-tick ring of the
millilitres it received (`InflowRing`), fed by `NoteInflow(voxel, millilitres)` — the volume from
`Add` and `Emit`, and `InflowRing.Disturbance` from an edit under water and a fall's re-pour, which
are fed whatever their volume as a changed terrain always was. `IsBeingFed` sums the litres that
entered a sheet's distinct chunks within the quiet window and sorts every sheet it tests into
under 0.1% / under 1% / under 10% / 10% or more / disturbed, on the `settle` line. **2b**: a sheet is
fed only when that sum is at least `FedSharePercent` of it — 1% shipped (`--fedshare=`), 0 the old
any-inflow rule and the constructor's default, so every recorded figure reproduces; a window longer
than the ring keeps the old rule. Eleven tests (`FedShareTests`); ten recipes, nine caught on the
first run — the miss was the 1–10% band, which no fixture had, and a test now pours 1.7% into it.
Suite 1,873; r6 control to the digit.

**The count, on a copy of `Regions13` with the weather sped up (`--clockscale=20`, ~100 rain drops
in the minute), both arms 0 `ERROR` and every identity exact:**

| fed sheets by inflow share | `--fedshare=0` | 1% |
|---|---|---|
| under 0.1% | 0 | 0 |
| 0.1–1% | 0 | 0 |
| 1–10% | 3 | 0 |
| 10% or more | 1,306 | 869 |
| disturbed | 6 | 0 |

(The arms' totals differ with the rain's timing — 107 drops against 99 — not with the rule.)
**Fact 3's estimate was wrong for this world: the sheets it calls fed are fed** — small puddles a
15.6 L drop lands in and the breach's streams, a tenth of themselves or more within two ticks —
and it had no seepage to count (`seeped 0 L`; the pit is not under the lake's table). The copy at
rest, without the weather, tested no fed sheet at all. **So 2b is right and costs nothing, it will
matter in a mine sweating under a lake, and it does not move his breach pit.** Decision 4's
condition for 2c — a play that still reads a stream as slow — is what the count predicts; 2c is the
lever for his case, and it waits on his word.
