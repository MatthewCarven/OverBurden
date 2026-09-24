# The front advance — water that spreads sideways faster

Matthew, 2026-09-24, over a screenshot of a trench spilling into a valley: *"What I am dreaming of
is a way water can spread laterally (horizontally) out faster"*. Of four ideas (the solve pushes
the front; a sideways run in the tick like the fall rule; a faster tick; the counted divisor again)
he took the first: *"yes write the plan for idea 1"*. **A planning pass, not built.** Seven facts
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
