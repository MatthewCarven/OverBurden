# Collapse plan — "Tetris gone bad"

The planning pass for [COLLAPSE-NOTES.md](COLLAPSE-NOTES.md), 2026-09-26, on `main`. **All fourteen
decisions taken the same day (*"as recommended"*); a-1 and a-2 are built (Core, all of part
a); part b's pass is written below and taken as recommended, and b-1 (the Core director) and b-2
(the worker cut and the wire into `Main`) are built; `--collapsemode=pieces` plays it, with nothing
drawn falling yet. Part c's pass is written below and taken as recommended; c-1 (Core) and c-2 (the
renderer) are built: pieces are drawn falling. **Played 2026-09-27, and `pieces` is the default**
(*"make it the default please"*; the glitch: *"like they turn cyan yeah like it"*). Parts d–f are
next** (TODO's "Next").
Read the notes first. They have his first brief, the demolition's numbers, and what M9 already does.

## His second brief (2026-09-26, the planning chat)

> "a tetris solver that uses compatible world data and let it collapse slowly to quickly like the
> initial march … When a collapse starts in the vicinity of the columns in question height does
> not matter (this will be a funny prank people play on each other later (it goes you tunnel
> under them then set off a collapse)) so blocks start falling at the rate of 2,3 or 4 per second
> falling by one voxel only per second up to eventually falling at the rate of say ten voxels a
> second … it would be good if they fell in the shapes of Tetris pieces so exact counts at your
> discretion like its 3d tetris … i would also like the debris to not stack highly like select
> the lowest point and spread out a little so the support structures fall away from some
> constructions"

And, mid-pass:

> "they seperate between each piece falling like" — "like digital corruption spreading"

It adds seven things to the notes:

1. **Pieces are 3D Tetris shapes.** The counts are my call.
2. **The release rate starts at 2–4 pieces a second.** The fall starts at one step a second and
   climbs to about ten.
3. **Height does not matter** near a collapse. This is the prank: tunnel under someone's
   building and set off a collapse.
4. **The debris goes to the lowest point and spreads a little.** This is the "solver": each piece
   picks where it lands, the way a Tetris AI picks the lowest placement.
5. **The support falls away from constructions**, rather than piling against what is left
   standing.
6. **Each piece separates.** Pieces come away one by one with gaps between them, never as one
   slab.
7. **It spreads like digital corruption**: a front moving out from where it started, not the
   whole set at once.

## Seven facts from the code

1. **The fall runs at the fluid boundary, which is 2 Hz.** It only runs every frame under
   `--nowater` (`Main.SweepCollapse`, called from `UpdateFluid`'s begin block). It is pinned
   there because the lift / write / return needs the grid, and the grid is the tick's between
   boundaries. **So a piece stepping ten times a second cannot live on the fall's clock.** It
   steps every frame. Only a landing into a wet cell waits for the boundary.
2. **The gate is the settled field now** (`SupportRegion.TryReadSettled`). One write requeues the
   chunk and its six neighbours, and they stay unsettled until their derives land (~1 ms each on a
   worker, plus the join's rounds). So after the first piece leaves a chunk, that chunk's next
   release waits for a settle. At 10 releases a second, **derive latency would set the pace, not
   the table**. So a chunk's pieces are planned from one settled snapshot, and the next settle
   prunes the plan (decision 5).
3. **Under an at-risk voxel is air, water, or another at-risk voxel** (`Collapse.cs`'s remark,
   derived from the rule). So pieces cut bottom-first always rest on something about to leave, or
   on nothing. **Every piece move strictly lowers Σ(Y)**, which proves termination for everything
   below, including rubble that falls again.
4. **Rubble reaches 2 voxels, and hanging costs double** (`rubble.json`). A landed tetracube
   overhanging an edge by more than a voxel is at risk again. **The tumble comes free**: a piece
   that lands badly falls again as a new piece. It still terminates, by fact 3.
5. **His tower was ~6,200 voxels in four sweeps over 6.5 s.** In 1 m-cell tetracubes (up to 256
   voxels each) that is about 24–30 pieces: roughly 8–10 s at 3 a second climbing, which matches
   what he saw. **In 0.25 m-voxel tetracubes (4 voxels each) it is ~1,550 pieces: eight minutes at
   3 a second**, and it looks like sand, not Tetris. That arithmetic is why decision 1 is his.
6. **Nothing but the player and water has ever moved in this world.** An in-flight piece would be
   the first rigid body. If it is written into the world at every step, every step runs the whole
   hook chain: remesh, light, sky column, rain field, support requeue, Tier B `Disturb`, the
   portal and seep trackers, the undo's suppression. It also tears for a frame at every chunk seam
   it crosses, because the two chunks remesh on different frames. **Render-only keeps it out of all
   of that until it lands.**
7. **The tint comes from the settled field, and the field is reach, not load.** Anything the field
   does not mark (decision 9's chimney) **cannot be warned about by the tint**. That cost is
   stated, not solved.

## Decisions (each with a recommendation)

**1. What a block is. — His, answered: the voxel,** *"feel free to select shapes that promote the
appearance of tetris"*. I recommended 1 m cells; he took 0.25 m voxels, with the shapes left to me.
Fact 5's arithmetic still holds (~1,550 voxel tetracubes for his tower), so **the mino grows with
the event's age**. A piece is always one of the eight tetracubes (I, O, T, L, S and the three
twisted 3D ones), or a 1–3 mino fragment where the set is broken up, built from minos of:
- **1 voxel (0.25 m) while the event is young**: small, slow, easy to catch;
- **2 × 2 × 2 voxels (0.5 m) once it accelerates**;
- **4 × 4 × 4 (1 m, the brush cell) once it is past saving.**

A mino carries whatever at-risk voxels lie inside it. So it starts as sand-sized Tetris you can
rebuild behind, and ends as 4 m tetrominoes. The voxel rate climbs ×8 and ×64 with the shape, on
top of the release rate (decision 4). The ages are the pace table's.

**2. How the set is cut, and in what order — a spreading front.** His "digital corruption
spreading": the event has an **origin**, the at-risk cell nearest the fire's deepest deficit. It
releases pieces **in order of distance from the origin across the at-risk set** (a breadth-first
front, face-connected cells), and bottom-first within the same distance. A seeded greedy grows a
tetracube from the next cell on the front. Every piece is its own mesh and leaves on its own
release tick, so **pieces separate**: gaps open between them as they fall, never one slab.
Deterministic from (seed, origin, event start), per non-negotiable 1, so a server and a client cut
the same pieces. The front reaching the base is what grows the set above it. The slow-then-fast
shape comes out of the rule as well as the table.

**2b. The corruption is visible before it falls.** The next few pieces in the queue (the front's
edge, about one second's releases at the current pace, an estimate) are **drawn corrupted**: an
overlay on their cells, glitching (a flicker and an offset in a shader, cheap). It is not a remesh
and not the strain tint's merge-key bit, because it changes every release. This is the warning
that makes "fix it if you're quick" playable: you watch the corruption crawl toward a column and
shore it before it arrives. A shore that holds a queued piece clears its corruption at the next
settle (decision 5). Recommended; the look itself is his to judge at part c.

**3. The event and its age.** The existing timer stays as **the fuse**: the warning, from the
first tint to the first piece, still `lerp(60 s, 2 s, deficit / K) × Exp(1)`. A fire starts an
**event** instead of a one-sweep fall. An armed chunk face-adjacent to an event's chunk joins it
and **skips its own fuse**. That fixes the demolition's backwards pace: stage 5 comes at the
event's pace, not at a fresh draw from its own deficit. **The pace is a function of the event's
age** (his answer: easy at first, accelerating from ~15 s, past rebuilding by ~30 s). It is not a
function of the pieces released, which was Tetris's ten lines and my first draft. The event ends when every chunk in it reads `at risk 0` with
nothing in flight, plus a 2 s grace (an estimate). **Stored nowhere**, like the timers: a quit
forgets the age. Stated cost: quitting resets a collapse. Revisit trigger: multiplayer, because
a server does not quit.

**4. The pace table, by age.** All estimates; his play moves them.

| age | pace × | releases/s | fall (his answer: 1 m/s → 10 m/s) | mino | voxels/s, about |
|---|---|---|---|---|---|
| 0–15 s | 1 | 3 (seeded 2–4) | 1 m/s | 0.25 m | 12 |
| 20 s | 2 | 6 | 2 m/s | 0.5 m | 190 |
| 25 s | 4 | 12 | 4 m/s | 0.5 m | 380 |
| 30 s | 8 | 24 | 8 m/s | 1 m | 6,100 |
| 35 s+ | 16+ | 48+ | 10 m/s (cap) | 1 m | 12,000+ |

It is flat for `--collapsegrace=` (15 s), then doubles every `--collapsedouble=` (5 s). The
mino grows at 2× and 8×. **The fall's speed is his answer, in m/s. The step is one mino**, so a
young piece steps 0.25 m four times a second, and a 1 m piece steps 1 m a tick. That keeps
Tetris's one-row-a-tick look at every size. Releases have no cap of their own; a frame budget of
2 ms of landings (an estimate) bounds them. At the start, one brush click (64 voxels) out-builds
five seconds of the collapse. By 30 s, the collapse takes a brush's worth every 10 ms.

**5. Saving it — partial credit by construction.** Each release re-reads the latest settled field.
**A queued piece whose voxels a newer settle says are held is dropped from the queue**, printed as
`rescued`. So shore up half a roof and half of it stays. It gets harder later on its own, because
the pace climbs and the base leaving grows the set above it. M9d's *a timer only shortens* stays
for the fuse alone. **The notes' question 5 (re-drawing longer at low levels) is retired**: the
piece is the unit of saving. Cost: a rescue lags a shore by one settle (tens of ms, an estimate).

**6. The visible fall — render-only.** At release the piece's voxels become air through
`SetVoxel` (the hooks see a bite). The piece's voxels are drawn as cubes in one shared
`MultiMeshInstance3D` (one draw call for everything in flight, a hundred pieces or a thousand), and
they drop **in discrete
Tetris steps** at the event's gravity. The world is written as rubble when it lands (decision 8).
In flight a piece is not solid: you can walk through one, and health is M11's. A landing that
overlaps the body goes through M9c-1's unstick. **A landing into Tier B water waits for the
boundary** and goes through the existing lift / write / return (`ICollapseWater`), so `displaced 0`
stays an oracle. **A dry landing is written the frame it lands.** **A quit lands everything in
flight first** (at its target, instantly) before the save. **A crash loses what is in flight**, at
most pieces-in-flight × 256 voxels of rubble. That cost is stated, and is the same class as the
water's.

**7. The solver — where a piece lands.** For each release, over the piece's **24 orientations**
and every offset within **R = 2 cells** (an estimate, `--collapsespread=`): drop it onto the
world's surface under it, treating cells reserved by pieces already in flight as solid, and **take
the lowest landing**. Ties go to the landing **farthest from the event's centre** (debris moves
away from what is still standing, his fifth point), then to a seeded draw. The piece steers there
one cell sideways a step while it falls, and rotates at release. If a sideways step is blocked, it
drops straight from where it is. Each step re-checks the world under it, so a block placed in its
path is landed on. Priced as a column-height map of the area once a release: 600 candidates,
microseconds (an estimate). **The oracle: no piece lands higher than the lowest landing its own
search could reach**, a boolean over the state.

**8. What lands.** A piece is its own material in flight (grey stone blocks falling), and **rubble
where it lands**. That is M9c-1's rule, unchanged: what falls is broken, and rubble credits nothing.

**9. Height does not matter — the chimney. — His.** Today a collapse takes only what the field
marks. A void 20 m under a house does nothing to the house, because stone reaches 7.5 m and
natural rock anchors. There are three arms:
- **(a) Always to the sky.** Every column a piece left is undermined up to its top (the sky
  column's open height, or the top of what he built), within the loaded box. Taken literally, it
  also turns **every 2 × 2 roof fall 60 m down into a sinkhole to the surface**, lakes included.
- **(b) To the sky once it is serious — his answer.** The chimney opens when an event is **30 s
  old**, the age at which the table says it is past saving. A collapse you catch early stays
  local; one you do not climbs to the surface. That is "if it was serious and/or critical well good luck" as a single
  condition, and the prank works: dig a chamber under them big enough to go serious.
- **(c) Bulking.** Broken rock takes more room, so a chimney rises until its rubble fills the void
  (~2× the void's height). This is realistic, and it kills the prank at depth.

Common to all three: the chimney is marked in the event, not the field, so the tint cannot show it
(fact 7). It stops at the loaded box's top, the vertical radius. When the chunk above loads later,
it is analysed next to the dug column and the field decides it. A quit forgets it, like the age.

**10. Line clears — not built.** As the notes said: the mine is the antagonist, and a cleared line
is a reward. The chimney is the closest thing to one: the rows above a cleared void come down.

**11. The phonk.** The game has no audio yet. What this adds is **one signal: an event exists,
and here is its age and pace.** An `AudioStreamPlayer` on the Game side can start a track and raise its
pitch or tempo by the pace (Tetris does exactly that). **The track is his and stays out of the
repo.** Korobeiniki itself is a public-domain folk tune; a downloaded phonk remix is someone's
recording, so it is fine on his machine, and it becomes a licence question the day the game ships.
Optional, last.

**12. Instruments.** On the `M9:` line and the `support` profile line: events live / ended, oldest
age, pieces released / landed / in flight / rescued, voxels released == landed + in flight (an
identity, printed), re-falls (the tumble), chimney columns, the worst landing ms. Printed once per
event: `M9: event — chunk …: N piece(s), T s, peak pace ×P, R rescued`. No per-piece line, because
there would be hundreds. On the bench, `--support --collapse --pieces` drops the seven fixtures
piece by piece with the ASCII cross-sections before and after, checks the identities and the
solver's oracle, and prints Σ(Y) strictly falling per piece.

**13. The collapse sensor — the first machine that reacts to the mine.** His idea, 2026-09-26:
*"if collapse detected, cut power to the water containers via quantum energy sharing and
deliberately flood the place"*. The flood half fights a locked decision: here water weakens rock
(the keystone: at risk 20 → 18,718 under a lake), so flooding a failing structure feeds it. In
the real world, draining a mine causes subsidence and flooded workings stand, but that is water
pressure holding the rock from inside, and this game's model does not do that by design. What
fits is the **sensor**. `overburden:collapse_sensor` is a machine row with a `radius` port and a
`channel`. While an event (decision 3) has a chunk within its radius, it raises a **signal on
its channel** at the boundary. A row on that channel with an `onsignal` port (0 ignore, 1 off,
2 on) follows it. The vacuum cell's `enabled` switch is the first reader, a pump the second, and
TNT-NOTES's charges the third when they exist. This is the decision-11 signal wired into
machines. It is the first automation that answers the mine rather than the player. Whether a
response helps is left to the physics. Recommended; **its design goes with the "ideal machines"
chat**, because a signal on a channel is new to the channel class (today a channel is a power pool
and a pump's outlet ring). Part f, after the play.

**14. Water slows a falling piece.** In flight a piece is only a picture (decision 6), so a step
whose cells are in Tier B water (read off the published wet copy, never the grid) falls at a
slower speed. Recommended: half the event's gravity, capped at 2 m/s (estimates). Cheap, and it
looks right; it saves nothing, because the piece has already broken off. Part c.

## Split

| Part | What | Verified by |
|---|---|---|
| **a** | Core, Godot-free: the cutter, the solver, the pace table, the event's state | The bench arm, ASCII, both oracles, tests |
| **b** | The wire, with no visible drop: events replace the sweep's fall, and a piece lands at the time its steps would take. `--collapsemode=sweep` is the M9c-1 control | The headless plug oracle, re-read in pieces; the r6 control to the digit |
| **c** | The look: the moving mesh, the steps, the steer, the corruption overlay on the queue (2b), the slower fall in water (14) | Screenshots |
| **d** | The chimney, whichever arm he takes | A tunnel under a placed house, headless on a scratch world |
| **e** | The play, and the phonk hook if he wants it | His one sentence per question |
| **f** | The collapse sensor (13), designed in the "ideal machines" chat | A headless event near a sensor switching a cell off, printed on the `M10:` line |

**Measure on copies.** For the tower: `worlds/backup-Regions16-pondlook-2026-09-26/`, which still
has the tower, with the support knocked out headless (`--at=` and a bite). Never on his
`Regions16-pondlook`. The demolished copy is the "after" picture.

## a-1 — built (2026-09-26)

On his *"as recommended"*, the same session as the pass. Core only, nothing wired.
`CollapsePace` is decision 4's table. `Polycubes` holds the 24 rotations, a canonical name for a
shape, and the tetracubes enumerated: 8 free and 86 fixed, both asserted, which pins the rotation
group. `PieceCutter` is decisions 1, 2 and 2b. Suite 1,901 → 1,922, with 21 tests in
`CollapsePieceTests`.

Three things about it that the pass did not say:

- **The constraint only ever points one mino down.** A voxel can only stand on another mino if
  it is on its own mino's bottom layer, so "no mino leaves before what it stands on" is one flag
  per mino: *does it depend on the mino below?* It is not a graph. So the cutter is a priority
  queue with lazy re-enqueue, and a mino becomes free when the one under it leaves.
- **The cutter never consults a shape table.** Any four face-connected cubes are a tetracube, so
  growing a connected set is enough, and the test checks every whole piece's canonical name
  against the enumeration. **86% of the tetracubes lie flat** on a 16 × 4 × 16 block (216 of 251)
  with the same-layer preference, and 0% with it inverted, so the preference is what makes them
  read as Tetris. 85%+ of a solid block's voxels fall as whole tetracubes; the rest are
  fragments where the front strands a mino.
- **On a flat slab the front only moves outward**, which is "digital corruption spreading" as an
  assertion: the next piece's seed is the nearest remaining mino, and the minimum over a
  shrinking set never goes down.

**Battery: twelve recipes, ten caught.** The two misses:
- **An identity:** at exactly the grace, `<` and `<=` give the same multiplier, 2⁰ = 1.
- **A real hole, now closed:** nothing asserted the flat preference. `MostTetracubesLieFlat` was
  added, measured in both arms.

One thing not to rediscover: a mutant restored with `sed -i.bak` / `mv` keeps its old
timestamp, so the next incremental build ran the mutant and a green file read red. Touch the
file.

## a-2 — built (2026-09-26)

Built on his *"a-2 please"*, the same session. `LandingSolver`, `CollapseEvent` and
`CollapseFlight` in Core; `CollapseLandingTests` (15); on the bench, `--support --collapse
--pieces` (`PieceDrop`) drops every fixture piece by piece on a simulated 60 Hz clock, re-settling
and re-cutting every 250 ms (the game's settle latency, an estimate). Suite 1,922 → 1,937. **Every
oracle holds on every fixture**: solid voxels are conserved, Σ(Y) goes down at every landing, the
solver never rests above its own lowest, nothing lands on solid, and `at risk 0` after.

The bench, Release with tiering off:

| fixture | at risk | pieces | voxels fallen | simulated | rubble fell again | solve ms mean / p99 / max |
|---|---|---|---|---|---|---|
| 2 × 2 plug | 20 | 6 | 20 | 9.9 s | 0 | 5.6 / 31 / 31 (the first solves: JIT) |
| 4 × 4 room | 47,600 | 1,695 | 110,853 | 50.7 s | 61,879 | 0.68 / 4.5 / 13.2 |
| soil roof | 975 | 106 | 1,003 | 26.7 s | 28 | 0.90 / 5.5 / 5.8 |
| 2 × 2 under 10 m | 14,060 | 579 | 26,438 | 40.5 s | 12,374 | 0.62 / 4.1 / 6.6 |
| mid-air chunk | 32,768 | 1,008 | 64,032 | 45.6 s | 31,264 | 0.90 / 10.3 / 18.8 |

Five things it found:

- **The solve had to be made cheaper, and both levers are exact.** The first cut was 2–6 ms a
  solve on average, 195 ms at worst. Two changes brought it to 0.5–0.9 ms mean and 19 ms worst:
  - **Leading faces:** a one-voxel move only newly occupies the face in that direction, so only
    the face is checked.
  - **The column jump:** once steering ends, the rest of the fall is one jump, using a per-solve
    memo of how far each column is free below it.

  Both are held equal to the whole-shape, tick-by-tick arm by a test, placement for placement and
  path for path (`Solve(…, wholeShape: true)`). What is left is candidates: 480–680 for a
  1-voxel-mino piece over a 17 × 17 spread. **Named lever, not built:** cap the spread in minos, or
  prune by a lower bound. Part b spends at most one solve per frame once the frame's 2 ms is gone.
- **Half of what falls in a big collapse is rubble falling again**: 61,879 of 110,853 voxels in
  the room, 31,264 of 64,032 in the mid-air chunk. Rubble reaches 2 voxels, and a tetracube can't
  perch on a pile's edge, so it rolls off. That is his *"debris to not stack highly"* coming out of
  the rule rather than being written in. The cost: the room comes down as 1,695 pieces over 51 s,
  where the sweep made a handful of falls. **Whether it reads as a pile settling or as churn is
  the play's question**, and rubble's `supportDistance` is the knob.
- **"Rescued" is not only the player's work.** With no player, the room reads 7,938 voxels
  rescued: rubble landing under a roof shores it. The count in play will include the pile's own
  props.
- **The battery found a real defect in the event.** A flight launched between two advances was
  credited with the time before its launch: the whole gap since the event's last advance, 30 s in
  the test. Each flight keeps its own clock now. It was found by the test written for a battery
  miss (big minos stepping a metre a tick), not by the miss's own recipe.
- **Re-plans cost on the sim thread.** The bench's gather over every at-risk chunk is ~2.4 ms a
  re-plan in the room. **Part b's lever:** gather only chunks whose settled at-risk set changed
  (`ChunkSupport.SameAtRiskSet` exists).

**Battery: sixteen recipes, fourteen caught.** Both misses were holes and are closed by tests:
- **A blocked steer retried every tick** was invisible. `ABlockedSteerGivesUpAndDropsStraight`
  pins the plan's rule: give up and drop straight. That rule is also what keeps the column jump
  available.
- **Flights stepping at the 1-voxel mino always** was invisible, because every flight test used
  1-voxel minos. `AnOldEventsBigPiecesStepAMetreATickAtTheFastFall` closes it, and it is the test
  that found the defect above.

Release is capped at about two pieces a frame (~120/s) by the scheduling rule, which never banks
more than one interval.

## Part b — the code-level pass (2026-09-26, a new chat)

Part b's paragraph read against the code it touches: `Main.SweepCollapse` and `UpdateSupport`,
`CollapseScheduler`, `Collapse.TryFall`, `ChunkPipeline.SetVoxel` and `HoldsWater`, `FillWater`
and the wet-fill queue in `PlaceController`, `FluidSimulation.Collapse`, `SupportRegion`'s settled
reads, the quit in `_ExitTree` and the bench's `PieceDrop`. **Decisions only; nothing built.**

### Eight facts from the code the pass did not have

1. **Placement already has the dry/wet split this part needs.** A brush writes its dry voxels at
   the click, on any frame, through `SetVoxel`; a voxel `HoldsWater` answers wet for (the published
   copy, stale by at most one tick) is refused and queued, and the boundary runs it again with
   `FillWater` in hand — lift, write, return over the shape's box grown by one brush cell,
   `lifted == returned + displaced`. Decision 6's "a dry landing is written the frame it lands, a
   wet one waits for the boundary" is that rule. It also inherits its one race: a cell dry in the
   copy that water reached inside the running tick is written solid, and the automaton displaces
   its water at the first visit. Placement has lived with that since 2026-09-19.
2. **`Lift` and `Return` throw mid-tick** (`RefuseMidTick`), so a wet landing cannot be done off
   the boundary, as the plan said. A release can: writing rock to air is a bite, and bites land
   on any frame.
3. **The cavity is not there any more when a wet piece lands.** `TryFall` returned the lifted
   water into the cavity its own run left, in the same column and the same sweep, so it always
   fitted and `displaced 0` was an oracle by construction. A piece is released, falls for a
   second or more and lands up to 2 m sideways; under a lake the water has flowed into the cavity
   by then. **The room is at the lake's surface, not beside the landing.** `FillWater`'s box
   alone would displace nearly everything a piece lifts on a lake bed. Nothing has measured this
   (decision 4).
4. **`CollapseFlight` does not re-check its path.** Decision 7 says each step re-checks the world
   under it; a-2's flight advances along a path solved at release, and the event reads no world.
   The re-check belongs to the caller, which part b is (decision 8).
5. **Reservations are per event.** `CollapseEvent._reserved` is private to one event, so two
   events near each other can solve two pieces into the same cells — the bench's `landed on solid`
   oracle, which only ever ran one event (decision 5).
6. **A settle needs the chunk and its six to be neither queued nor in flight** (`IsSettled`), and
   every release and landing requeues seven. At 3 releases a second the writes are ~330 ms apart
   and a derive is milliseconds, so members settle between releases and a shore is seen. Past
   ~20 s, with a write every frame or two, a member may not settle until the queue runs dry. **So
   a fast event goes in waves** — cut, drain, settle, re-cut — **and a rescue registers early and
   not late.** That is decision 5's "harder later" arriving by a second route. Unmeasured; b-2
   prints it.
7. **The gather and the cut have never been priced apart in the game's shape.** The bench's
   gather scans every settled field in the region (2.4 ms a re-plan in the room); the game has
   ~1,500 settled fields at open. The cut of a 47,600-voxel set has no number at all. Part b
   gathers members only (decision 7) and prints the cut's ms.
8. **Autosave runs every 30 s, and the quit can find the grid still a cancelled tick's.** Landing
   everything in flight at every autosave would jump every piece to its rest twice a minute. At
   the quit, `SaveAll` is already refused when the grid is still owned, so a `LandAll` that needs
   `Lift` has the same gate the save has (decision 11).

### Decisions (each with a recommendation)

**1. Where the loop lives — a Core `CollapseDirector`, shared by the bench and the game.**
Everything `PieceDrop` does between the event and the world — gather, re-plan, release, solve,
write to air, advance, re-check, land, count — moves into Core as one class driven by a clock the
caller passes. The bench's `--pieces` becomes a driver over it, so the bench's oracles test the
code the game runs rather than a copy of it. Recommended. The alternative is the loop in `Main`,
which leaves the bench proving a twin.

**2. Two clocks.** The fuse — `CollapseScheduler.Sweep`, arming and firing — stays at the fluid
boundary (2 Hz) where it is. A due fuse calls the director instead of `TryFall`. The director has
`Frame(now)`, every frame from `UpdateSupport`: re-plans, releases, advances, re-checks and dry
landings. It also has `Boundary(now, water)`, from `UpdateFluid`'s begin block where
`SweepCollapse` sits now: the wet landings. Under `--nowater` both run every frame, as the sweep
does today. Recommended.

**3. Dry or wet — placement's rule.** A due piece whose rest voxels all read dry in the published
copy, and whose chunks `IsReady`, lands in `Frame`. Any wet voxel sends the whole piece to the
next `Boundary`, reserved and hovering at rest (invisible in b; up to half a second of hover in
c, which decision 14's slower fall in water partly hides). Fact 1's one-tick race is accepted as
placement accepts it, and the automaton's `displaced` is printed beside the event's line so a
non-zero is seen. Recommended. **The alternative — every landing at the boundary while water is
on — kills the race and makes every landing 2 Hz**, which c could not draw at ten steps a second.

**4. Where a wet landing's water goes — up to the room.** Fact 3. In order:
1. the rest box grown by one brush cell, bottom-up (`FillWater.Settle`, unchanged);
2. the piece's own vacated cells, bottom-up — its cavity, if it is still air;
3. up the landing's columns to the first cell with room, within the loaded box — the level rising,
   which is where a stone dropped in a pool puts the water;
4. and only then `displaced`.

`lifted == returned + displaced` is by construction as before. **What changes is that
`displaced 0` goes from a construction to a measurement:** the keystone oracle under a lake
(decision 14) is where it is measured. Recommended. The cheaper arm, the fill's box alone, is
known to displace on a lake bed, and I'd not ship it into the first wet collapse.

**5. One reservation set for every event.** The director owns the set; each event adds and
removes through it; the solver reads it. **Events never merge.** An armed chunk face-adjacent to
two events joins the older, which has the higher pace — "good luck saving it". Recommended. The
merge arm would move one event's queue and flights into another and re-base an age. That's more
code for a case no play has shown.

**6. Membership, concretely.** A due fuse on a chunk face-adjacent to any event's member joins
that event; otherwise it starts one, with the origin at its deepest-deficit voxel (the bench's
`Gather` rule). At every sweep, an **armed** chunk face-adjacent to a member joins at once and
drops its timer: decision 3's "skips its own fuse". A member never arms. Rubble landing in a chunk
nobody analysed digs it through `SetVoxel`, and if it is at risk it arms, is adjacent and joins.
An event ends when every member reads settled `at risk 0`, nothing is queued and nothing is in
flight, plus the 2 s grace. Recommended: it is the plan's rule with the joins made exact.

**7. When to re-plan, and from what.** A re-plan fires when:
- a member's `SettledField` is a new object and `!SameAtRiskSet` with the one the queue was cut
  from;
- the mino changed;
- or the queue is empty and a settled member still has something at risk.

The gather reads **members only**, and each member's voxels are read back as solid from the
world. `StillAtRisk` at release reads the last settled snapshot. That is deliberate and is
decision 5's stated lag, not a gate on now (the kept snapshot is a plan, and the world read is the
gate). Fact 6's waves are accepted, and b-2 prints settles-between-releases and the cut's ms.
**Named lever, not built: the cut on a worker**, triggered by a cut over 2 ms in the game.
Recommended.

**8. The path re-check (fact 4).** Each time a flight crosses a whole step, the shape at its new
anchor is read against the world:
- **Blocked:** the flight is re-solved straight down (spread 0) from the last clear anchor. That
  is decision 7's "a block placed in its path is landed on".
- **Blocked at its own start** (a block placed into the air the piece left, in the second it
  fell): it rises to the first clear anchor above and is counted `lifted by a block`. The Σ(Y)
  oracle excludes those, because the player put the mass under it.

Cost: pieces in flight × voxels a piece, a few thousand reads a frame at the peak. Recommended.
The cheaper arm, checking at landing only, would let part c draw a piece through a new block.

**9. A landing past the loaded edge.** A player who flies off takes chunks with them. A rest voxel
whose chunk is gone at landing makes `SetVoxel` answer false. The piece is counted
`lost at the edge`, a term of the identity: `released == landed + in flight + lost`. It is
expected zero, and it is the same class as the crash's loss. **No pre-eviction hook until one is
seen.** Recommended.

**10. Undo during an event — disallowed.** His words at UNDO-PLAN: *"disallow if something
falls"*. The stack is cleared on every frame the director writes, and a press while an event is
live reads `nothing to undo: a collapse is under way` (`FellSinceLastEdit`'s sibling). The
director's writes are never journalled, with `Recording` off around `Frame` and `Boundary` as
around the sweep. **Cost: a shore you place mid-collapse can't be undone.** Recommended.

**11. The quit and the autosave.**
- **The quit:** `LandAll` just before `SaveAll` in `_ExitTree`, with the water in hand, under the
  save's own gate. When a cancelled tick still owns the grid, the save is refused as it already
  is, and the flights are lost with the rest.
- **The autosave does not land,** because of fact 8's jump. A crash loses what is in flight and
  what was released since the last autosave: the stated cost, unchanged.

Recommended.

**12. The flags.**
- `--collapsemode=sweep|pieces`.
- `--collapsegrace=` (15), `--collapsedouble=` (5), `--collapsespread=` (2 m) and
  `--collapsebudget=` (2 ms of landings a frame, then at most one solve), all onto `CollapsePace`
  and the director.
- **The default stays `sweep` through b.** With no visible drop, `pieces` reads as rock vanishing
  and rubble appearing elsewhere a second later, which is worse to play than the sweep. It flips
  to `pieces` on his verdict after part c draws the fall.

Recommended.

**13. The split — b-1 Core, b-2 the wire.**
- **b-1:** `CollapseDirector`, the shared reservations, the re-check, the water's return order
  and the edge term, with tests on a fake clock (the plug to `at risk 0`, wet waits for
  `Boundary`, dry lands in `Frame`, two events never share a cell, a blocked path, `LandAll`, a
  lost landing). The bench's `--pieces` is moved onto it with every a-2 oracle still green, and
  it prints the cut's ms.
- **b-2:** `Main` — the fuse's hand-off, the two calls, the flags, undo, the quit, the unstick
  after a landing frame, the `M9: event` line, the identity and the new terms on the periodic
  line, and the oracles.

Recommended. It is the Core/wire seam, and each half is a session.

**14. The oracles for b-2.**
- **The plug:** `--collapsemode=pieces` on the plug oracle reads one `M9: event` line, about six
  pieces and 20 voxels (the bench's figure), the identity, and `at risk 0`.
- **The keystone:** it needs `--measure=` raised. The bench's room took 51 s for 47,600 voxels,
  so 18,718 is an estimated 40 s, and the run should be 60. It reads `lifted == returned` with
  `displaced` measured (decision 4).
- **The sweep arm** reproduces both old oracles' lines to the digit.
- **The r6 control** is unchanged, because nothing is at risk there.

Recommended.

**On waiting for the ideal-machines chat:** the sensor (part f) reads "an event exists within my
radius", which is `CollapseDirector.Events` — members, age, pace — once b is built. So b is not
blocked by that chat. It only has to expose the events, and it will.

## b-1 — built (2026-09-26)

On his *"As recommended please"*. The pass's fourteen decisions are taken as written. All of b-1 is
Core; the game is untouched, and the sweep is still what it runs.

- **`CollapseDirector`**, between the events and the world. It starts and joins events at the
  fuse's word and re-plans from the members' settled snapshots. It releases pieces to air, solves
  each landing and advances the flights, re-checking every whole step. Dry landings go in `Frame`,
  wet ones in `Boundary`, and `LandAll` is the quit. It also ends events after the grace, keeps
  the counts, and hands back `CollapseEventSummary` for the line.
- **`CollapseEvent`**: the reservation set can be shared (decision 5); `Replot` for a blocked
  path; `Lose` for the edge term. The identity is `released == landed + in flight + lost`.
- **`CollapseScheduler.Director`**: when set, a member never arms, an armed chunk beside a member
  joins the older event, and a due fuse calls `Fire` instead of `TryFall`. Null is M9c-1's sweep,
  unchanged.
- **The bench's `--pieces` drives the director** through the scheduler, with a zero window, a
  boundary every 500 ms and a settle every 250 ms. So its oracles are now about the game's code,
  and it prints the gather and the cut apart.
- **`CollapseDirectorTests`, eighteen.** Suite 1,937 → 1,955.

The bench, Release with tiering off:

| fixture | at risk | events / joined | pieces | voxels fallen | simulated | fell again | rescued | solve ms mean / max | cut ms mean / max |
|---|---|---|---|---|---|---|---|---|---|
| 2 × 2 plug | 20 | 2 / 2 | 7 | 20 | 11.3 s | 0 | 0 | 4.8 / 32 (JIT) | 3.3 / 25 (JIT) |
| 4 × 4 room | 47,600 | 4 / 28 | 784 | 49,796 | 56.5 s | 17,572 | 22,868 | 0.51 / 7.7 | **29 / 133** |
| soil roof | 975 | 1 / 1 | 105 | 994 | 28.3 s | 19 | 0 | 0.95 / 6.3 | 1.0 / 2.1 |
| 2 × 2 under 10 m | 14,060 | 2 / 6 | 844 | 29,483 | 42.6 s | 15,419 | 752 | 0.60 / 9.0 | 6.9 / 31 |
| mid-air chunk | 32,768 | 1 / 9 | 1,020 | 65,940 | 48.6 s | 33,172 | 555 | 0.87 / 22 | **29 / 103** |

Every oracle holds on every fixture: solid conserved, Σ(Y) down at every landing, the solver never
above its own lowest, no rubble written over solid, the identity at every frame, `at risk 0` after.

What it found:

- **The cut is the cost, and the named lever's trigger has fired on the bench.** A re-plan in the
  room is 29 ms mean and 133 ms at worst. The gather is 1.3 ms of that, so the cut is the rest.
  The profile has no hot spot. The cut is linear in the at-risk set, with a constant of about
  twenty tuple-hash lookups a voxel. Decision 7 named the cut on a worker, triggered by "a cut over
  2 ms in the game". The game runs this code at Debug-optimised speed, so running it only to see
  that trigger fire would be a wasted run. **b-2 should begin with the worker cut** (his word, since
  it is a named lever). The old queue keeps releasing while the new one is cut, and the result is
  applied when it lands.
- **A sort-once change to `FrontDistances` did not move the cut.** It re-sorted every unvisited
  mino per component, which is quadratic in components, and I took that for the cost. The profile
  said no: same pieces, same numbers. It is kept, because it is exact and bounds the worst case,
  and its comment says it is a bound, not a speed-up.
- **A test found a real defect in the budget.** The one solve allowed past the frame's 2 ms was
  spent by asking an event with nothing due, so a second event with a piece waiting lost the
  frame. It is now counted when a piece is released. `PastTheBudgetOneSolveIsLetThroughAFrame`
  holds it at a budget of zero, the only deterministic budget. The rig lifts the budget, because a
  wall-clock budget lets a cold JIT decide which releases a test sees. That is how the defect
  showed: as a flake.
- **The plug comes down as two events, not one,** when the fuse fires diagonal chunks in the same
  sweep: the join is face-only, and diagonal chunks are not face-adjacent. b-2's headless oracle
  may read one `M9: event` line or two, depending on the draw. That is stated here rather than
  engineered away. Plan decision 3's "structure" is face-joined chunks.
- **The solver lets a piece rest standing.** It ranks landings by the lowest bottom voxel, so an
  O on edge ties an O lying flat. A test assertion found it. It is a-2's rule, unchanged. A Σ(Y)
  tie-break would be a one-line lever if the pile reads too tall in part c.
- **A block written in mid-air near an event is at risk, and comes down with the event.** The
  first path-block fixture put a floating slab in the piece's path and got 24 voxels of rubble
  from a 4-voxel piece. That is the collapse working correctly, and the fixture was changed to
  stand the block on the floor.
- **"Rescued" in the room is 22,868 voxels,** against 7,938 in a-2's own loop. Most of it is the
  pile propping the roof above it, not anyone's shore. b-2's line should say so beside the count.

**Battery: twenty-one recipes, twenty caught, one identity.** The first run of sixteen missed
four; three were holes and are closed by tests:
- **The box step removed**: the dry cavity took the water instead, and the test checked only the
  totals. Now it asserts where the water went.
- **The grace removed**, and **members read at rest while unsettled**: nothing asserted *when* an
  event ends. `AnEventEndsTheGraceAfterItsMembersSettleAtRest` holds both, on either side of the
  grace.
- The fourth is **an identity**: the at-risk half of the release gate. The frame re-cuts from the
  same snapshots before releasing, so no fixture can make that half decide anything. It is kept,
  with a comment saying so. Its live twin, **the world read at release**, was untested too (a
  voxel mined between two settles). `AQueuedVoxelMinedBeforeItsReleaseIsRescuedNotConjured`
  catches it: without the read, a piece of air fell and landed as rubble.

Two things not to rediscover:
- **The frame budget is wall-clock**, so a test rig with a real budget lets a cold JIT choose which
  pieces release. The rig lifts it, and the budget's rule is pinned at zero.
- **A heredoc turned a battery recipe's `\n` into real newlines.** Write the harness, never a
  heredoc.

## b-2, step 1 — the cut on a worker, built (2026-09-26)

On his *"Yes please"*, and b-1's first finding. The gather stays on the sim thread, because it
reads the world. The cut is pure, so it goes to `CollapseDirector.CutRunner`. Null cuts inline,
which is what the bench and the tests use; the game will pass `work => Task.Run(work)`.

While the cut runs, the old queue keeps releasing. When the cut lands, whatever was released
meanwhile comes out of its pieces, so nothing falls twice and nothing that fell counts as rescued.
One cut runs per event, and a trigger that arrives meanwhile is held and cut once the running one
lands. The mino is judged against the cut in flight, not against the old queue. An event with a
cut in flight is not at rest. A cut that throws throws on the sim thread when it lands, as a
synchronous one would. Suite 1,955 → 1,961.

The bench's `--cutworker` arm runs it on real threads. While a cut is on the worker, each
simulated frame takes a real 16 ms: without that pacing the simulated clock outran the thread,
and the mid-air chunk's first cut never landed in 600 simulated seconds. The arms ran alternately,
worker, sync, worker, sync, all in the machine's slow phase (a sync cut in the room read 65–73 ms
mean there, against b-1's 29):

| fixture | frame p99, sync → worker | frame max, sync → worker |
|---|---|---|
| 4 × 4 room | 249–277 → 8–9 ms | 300–330 → 30–40 ms |
| mid-air chunk | 162–177 → 11–12 ms | 180–199 → 51–61 ms |
| 2 × 2 under 10 m | 57–63 → 7.6–8.5 ms | 67–78 → 16–19 ms |

Every oracle holds in both arms. The worker arm has more churn: 57,588–68,630 voxels fall in the
room against 49,236, because the old queue releases stale pieces while a slow cut runs (4,900
voxels released meanwhile). That follows from the latency, and in the game it shrinks with the
machine's fast phase. **What is left of the worst frame (30–60 ms, rare) is the gather (up to
15 ms), a solve (up to 22 ms in the slow phase), and the landing of a cut.** The next lever, if
the game shows it: the gather on the worker, which needs a record of what was released since
each member's snapshot, because the worker cannot read the world.

**Battery: nine recipes, eight caught, one identity.** The identity was taking the voxels
released during a cut out of its *set*. That set is only read for the old queue's rescued count,
and a released voxel is not queued, so the line is deleted. Two recipes missed at first, both
about the mino growing while a cut runs. `TheMinoGrowingWhileACutRunsIsCutOnceAtTheNewMino` now
catches both. A swallowed fault read HUNG in the first run and was caught cleanly when run alone.

## b-2 — the wire, built (2026-09-26)

On his *"ok Claude lets continue I cant wait to see my tower collapse"*. `Main` only; Core is
untouched.

- **`--collapsemode=sweep|pieces`**, with `sweep` the default until part c draws the fall
  (decision 12). **Flipped to `pieces` 2026-09-27** on his play of c-2.
- **Under `pieces`,** `SetUpCollapse` builds the director with the water, the pace, the spread,
  the budget and `CutRunner = work => Task.Run(work)`, and sets it as the scheduler's `Director`.
- **`FrameCollapse` runs every frame** from `UpdateSupport`. `Boundary` runs inside
  `SweepCollapse`, after the fuse and under the same recording-off bracket.
- **`AfterCollapseStep`**: a step that wrote clears the undo stack (`M10: undo — the stack is
  cleared … a collapse is under way`). A step that landed runs the unstick, which now takes the
  written chunks rather than a fall. A press while an event is live reads `nothing to undo: a
  collapse is under way`.
- **The quit** lands what is in flight just before the save, under the save's own gate.
- **The lines:** `M9: event N — from … over … chunk(s): P piece(s), V voxel(s) in T s, peak pace
  ×…, mino …, R rescued (the pile's props count), L lost` when one ends, and a `pieces:` term on
  the `M9:` line and the profile's `support` line.
- **The flags** `--collapsegrace=`, `--collapsedouble=`, `--collapsespread=` and
  `--collapsebudget=`.

The oracles, headless, all with zero ERROR lines:

| run | reads |
|---|---|
| r6 control | 2601 / 325 / 3446 at 411, to the digit |
| plug, sweep | four `M9: fell` one boundary apart, 20 voxels, `at risk 0` — as recorded |
| **plug, pieces** | **one `M9: event`: 6 pieces, 20 voxels in 12.1 s, released 20 == landed 20, `at risk 0`** — the bench's six |
| **keystone, pieces** (`--measure=60`) | one event over 12 chunks, 1,184 pieces, 70,065 voxels released == landed (42,000 of them rubble falling again), **lifted 782 L == returned 782 L, 0 L up a column, displaced 0**, lost 0, `at risk 0` |

**The keystone's sweep arm is not reproducible to the digit, and never was.** Its first run today
read 9 falls and 18,207 voxels with no water lifted, against the recorded "four falls of ~6,200,
30 L". I checked it against a control, the same command on the code before b-1, interleaved over
five runs an arm:

- **Today's code:** 15,985 / 18,207 / 21,696 / 24,973 / 24,973 voxels, and 0–30 L lifted.
- **The code before b-1:** 15,985 / 21,380 / 24,973 / 24,973 / 24,973 voxels, and 0–31 L lifted.

The same outcomes turn up on both sides, and one of today's runs reproduces the record exactly
(peak 18,718, 30 L). **The mechanism is a race.** Three or four of the plug's falls land before
the pour at 5 s, depending on when the worker derives settle. That decides which roof the lake
stands over: the peak at risk reads 18,293–18,728 in 3 or 7 chunks, or 24,405–24,951 in 4 or 8.
What holds in every run is `at risk 0` at the end and lifted == returned. COMMANDS says so beside
the oracle now.

## Played, 2026-09-26 — the pace is right

His tower on `Regions16-pondlook`, under `--collapsemode=pieces`: four events, 902 pieces,
20,426 voxels, the biggest 48 s long with pieces up to 1 m, and the water exact (the worklog has
the table). His verdict:

> "yes slow to fast, um that is sort of realistic like because buildings unless in earthquakes
> usually give signs before they collapse"

**Decisions 3 and 4 stand as built:** the fuse and the tint are the signs, the flat 15 s is the
slow start, and the doubling from there is the fast end. His aside about earthquakes is colour, not
an instruction. It names the one collapse with no warning, which is TNT-NOTES's shake if that is
ever built. Still open for part c:
- the look of the drop itself;
- what a flight blocked where it stands does when it is drawn (96 blocked, 40 of them lifted);
- ~~whether the pace wants a ceiling (×644 at 62 s)~~ — **his "cap the top please"**: capped at
  ×16 (`CollapsePace.MaxMultiplier`, `--collapsecap=`), decision 4's last row, reached at 35 s: 48
  pieces a second of 1 m minos. At ×644 the frame budget was what held it back, not the table.

## Part c — the code-level pass (2026-09-26)

On his *"ok lets continue with part c please"*. This is part c's paragraph (the moving picture,
the steps, the steer, the corruption overlay on the queue, the slower fall in water), read
against the renderer. The code it touches: `Voxel.gdshaderinc`, `VoxelMaterial`,
`ChunkMeshConverter`, `OnMeshReady`, `ChunkPipeline`'s mesh apply, `StreamedChunk.NeedsRemesh`,
the light grid (`ChunkLight` via the streamer), `Polycubes.Rotations`, `LandingSolver`,
`CollapseFlight`, and `Rain`, which is the only thing already drawn that is not a chunk. His two
plays fed it too. **Decisions only; nothing built.**

### Seven facts from the code and the plays

1. **The voxel shader is unshaded, and its light is baked per vertex.** `COLOR.r` is sky × AO and
   `COLOR.g` is block × AO, both written by the mesher off the light grid. A falling piece has no
   mesher, so it has no light unless the renderer gives it some. Drawn with the chunk material
   as it stands, a piece would be black.
2. **A chunk's new mesh lands one or more frames after the edit.** `SetVoxel` marks the remesh;
   the job runs on a worker; `OnMeshReady` swaps the instance when it lands. So a piece drawn the
   frame it is released sits on top of the old rock until the remesh lands, the same faces in the
   same place, which z-fights. And a piece removed the frame it lands leaves a hole until the
   rubble's remesh arrives. **Both ends of a flight flicker unless the picture waits for the
   mesh.** `StreamedChunk.NeedsRemesh` is false when a mesh has caught up with every edit, and
   that is the test to wait on.
3. **The rotation at release is recoverable exactly.** `LandingSolver` picks an orientation from
   `Polycubes.Orientations`, which dedupes the 24 `Polycubes.Rotations`. The rotation that made
   the chosen shape can be found again, so each of the piece's voxels maps to its rotated cell.
   **A piece can be drawn in its own materials, voxel for voxel:** a stone and glass piece stays
   stone and glass in the air. Today the director reads a released voxel's material only to count
   the tumble.
4. **Pieces in flight at the tower's peak: 65 pieces, 2,332 voxels**, sampled on the periodic
   line; the bench's room peaked at 70. Tens of pieces, a few thousand voxels.
5. **The 40 pieces lifted in the tower** were flights blocked where they stood. Almost certainly
   that was another flight landing, writing rubble into the cells a picture occupied. Pieces are
   pictures to each other (decision 6), so nothing stopped it. Invisible in b; drawn, a piece
   jumps up. Of the 96 blocked, the rest re-plotted downward, which draws as a piece stopping
   early. That is fine.
6. **Nothing in the Core reads water for a flight yet.** Decision 14 (half the gravity in water,
   capped at 2 m/s) needs the flight's step rate to take a factor per flight. The published wet
   copy (`HoldsWater`) is already on the editor, and the dry/wet landing rule uses it.
7. **Headless draws nothing.** Every look question here is a windowed screenshot
   (`screenshot.gd`, `--settle=`) on a scratch world, as with glass and the map.

### Decisions (each with a recommendation)

**1. What a piece is drawn as — one small mesh per piece, built at release, moved by its
transform.** Decision 6 named a shared `MultiMeshInstance3D`. The code-level choice is between:
- **A mesh per piece.** It holds only the piece's outer faces, in its materials (fact 3). Each
  step is one transform write. At the peak that is ~65 instances and ~65 draw calls.
- **One MultiMesh of voxel cubes.** One draw call, but every voxel's instance data is rewritten
  whenever a piece steps: 2,332 cubes, and every hidden face with them.

Recommended: a mesh per piece. The build happens once at release; the per-frame cost is 65
transforms; and exact materials come free. Revisit trigger: draw calls show in a profile of a
×16 collapse. Then the named lever is the MultiMesh.

**2. What it looks like — its own materials, rotated at release, voxel for voxel** (fact 3).
- The solver records the rotation, and the director captures each voxel's material before it
  writes air.
- The piece's opaque faces use the chunk material and its glass faces use the glass one, the way
  `ChunkMeshConverter` splits a chunk.
- It turns to its landing orientation the moment it lets go. That is Tetris's rotation, and the
  plan's "rotates at release".

Recommended. The cheaper arm, one majority material per piece, is not cheaper enough to be worth
the wrong colour on a mixed piece.

**3. Its light — sampled from the light grid at the piece's centre once a step, and passed as
an instance uniform** (sky and block, no AO). A piece falling out of daylight into a dark pit
darkens as it goes, and one falling in a lamp-lit chamber is lit by the lamp. That takes a new
shader, `VoxelPiece.gdshader`, sharing the fragment with the chunks through the include, with its
own `vertex()` that reads the instance's light. The include's `vertex()` moves behind a define.
Recommended. Per-voxel light would mean rebuilding the mesh every step, for a shading difference
across a piece a metre or two wide.

**4. Steps, not glides.** The picture is drawn at `Anchor`, the path's whole tick. It moves a
mino at a time: four steps a second at the start (0.25 m at 1 m/s) and ten at the cap (1 m at
10 m/s). That is his *"falling by one voxel only per second"*, and Tetris's look at every size.
Recommended, as decision 6 already said. The alternative, interpolating between ticks, reads as
physics and not as Tetris.

**5. The hand-off at both ends — the picture waits for the mesh** (fact 2).
- **At release:** the picture is shown once every chunk the piece left has a mesh with no remesh
  pending. Until then the old rock is still drawn where the piece is, so nothing is missing.
- **At landing:** the picture stays until every chunk its rubble went into has a mesh with no
  remesh pending.
- **A cap of 0.5 s (an estimate) on both waits**, because a chunk written every frame by a fast
  collapse might never be caught up. The cap flickers where it fires, and the line counts it.

Recommended. The alternative, a fixed number of frames, is wrong on the slow phase of this
machine, which is exactly when the remesh is slow.

**6. The jumping pieces — a landing waits while another picture is drawn in its rest cells**
(fact 5).
- **The rule:** a due flight whose rest overlaps the cells another flight is drawn in now waits a
  frame, and lands once that flight has stepped past. This is Core, from the flights' anchors and
  shapes.
- **No deadlock:** rests are reserved and disjoint, so a waiting flight sits in its own rest,
  never in another's. The flight passing through is still moving, and a falling flight only
  goes down, so it leaves.
- **What stays:** a *player's* block placed into a picture still lifts it (decision 8's rule).
  That is rare and the player's doing.

Recommended. The alternative, reserving whole paths, makes the solver's search worse: more pieces
wait, and the debris spreads less.

**7. The corruption on the queue (decision 2b) — the next second's pieces drawn glitching.**
- **What:** each event's next pieces, as many as its pace releases in one second (3 at the start,
  48 at the cap), get an overlay over their cells while they are still rock.
- **How:** a second per-piece mesh slightly larger than the cells, in a transparent shader with a
  flicker and a small offset, animated by time in the shader. Nothing is remeshed, and nothing
  enters the merge key.
- **Needs:** a `Peek` on the event's queue in Core. The overlay is rebuilt when the queue's head
  changes, which is a release or a re-plan.
- **The rescue:** a shore that saves a queued piece clears its corruption at the next re-plan.

Recommended. The look itself is his, at the screenshot.

**8. Water slows a piece (decision 14).** Each step, a flight whose drawn cells hold water in the
published copy advances at half the event's fall speed, capped at 2 m/s. A factor per flight in
`Advance`, supplied by the director; the landing's timing follows. Recommended, as planned.

**9. Verification — windowed screenshots on a scratch world, with a control.**
- **The fixture:** `--bitechunks=2 --collapsemode=pieces --collapseafter=1,2` with `--inbite
  --pitch=85`, at a few `--settle=` times: the fuse, the first pieces, the middle, the pile.
- **The control:** a new flag, `--collapsepictures=0`, draws nothing, which is b's look. It is
  the A/B the screenshots are judged against, and the arm that prices the drawing on the
  profile's line.
- **The instruments:** pictures drawn; held at release and at landing (frames, and cap hits);
  landings that waited for a picture; overlay voxels; the ms the picture update takes.

Then his tower, on a copy of the backup. Recommended.

**10. The split — c-1 Core, c-2 the renderer.**
- **c-1:** the rotation and the materials captured, `Peek`, the per-flight water factor, and the
  landing that waits for a picture. Tests; the bench (its `blocked` should fall to about zero).
- **c-2:** `VoxelPiece.gdshader` and the include's split, the piece meshes and their light, the
  hand-off, the overlay, the flag, the lines and the screenshots.

**The default flips to `pieces` on his verdict after c-2** (decision 12). Recommended: the same
Core/renderer seam as b.

## Part c — played (2026-09-27)

He knocked his tower down with the pieces drawn (eight screenshots; log
`logs/tower-pictures-2026-09-27.log` under `app_userdata`). **Three verdicts:**
- **The default:** *"make it the default please"* — `_collapsePieces = true`. The r6 control reads
  2601 / 325 / 3446 at 411 after the flip; the plug at the default reads one event, 8 pieces,
  20 voxels, `at risk 0`; under `--collapsemode=sweep` it still reads 4 falls, 20 voxels, `at risk 0`.
- **The glitch:** *"like they turn cyan yeah like it"* — decision 7 stands as built.
- **The black pieces** (a piece lit from a cell with no light): *"I like that they are not the
  original colour like its indicitave that you aint getting that back"* — kept, not a defect.

**The settler:** 65% of what fell (174,529 of 268,699 voxels) was rubble falling again — the pile
tumbling. He did not call it churn. Decision 11 stays open: 8 lifts, at most 13 voxels (3.25 m).

## c-1 — built (2026-09-26)

On his *"as recommended please"*. The pass's ten decisions are taken as written. Core only.

- **The rotation, voxel for voxel.** `LandingPlan.VoxelCells` places each of the piece's voxels
  in the rotated shape: the rotation that made the chosen orientation, found again among the 24.
  `Replot` carries it with the shape.
- **The materials.** `CollapseFlight.Materials` holds what each voxel was when it let go,
  captured by the director before it writes air. `CollapseFlight.DrawnCells` gives the cells the
  piece is drawn in now.
- **The queue ahead.** `CollapseEvent.Peek(n)`, and `CollapseDirector.Upcoming(event, now)`: the
  next second's releases at the event's pace. That is decision 7's corruption, for c-2 to draw.
- **The water** (decision 8). `Advance` takes a factor for each flight. The director gives
  `WaterFactor(fall)` — half the fall, never past 2 m/s — to any flight drawn in a cell the
  published copy says is wet, and counts `SlowedInWater`.
- **The landing waits for a picture** (decision 6). A due flight whose rest another flight is
  drawn in waits a frame, in the frame and at the boundary alike; `WaitedForPicture` counts it.

`CollapseDirectorTests` has eleven more tests; suite 1,961 → 1,973.

**On the bench, the lifts that come from landings are gone, but not every lift.** Blocked flights
now read room 9, soil roof 0, 2 m head 0, 5 m 1, 10 m 13, mid-air 3. Ten lifts remain across the
seven fixtures (about one piece in 300). **Every one of them is `LiftedOffAReservation`**, a
second mechanism the pass did not see:
1. A flight's path was solved *before* another flight reserved a rest on it. Pictures pass through
   reservations, so the solver let the second flight choose a rest on the first one's path.
2. Rubble lands lower on the first flight's path.
3. Its re-plot's last clear spot lies inside that reservation.
4. The re-plot counts the reservation as solid, so it rises.

The lifts are small: 2–4 voxels each, 24 in all on the worst fixture, at most one mino (4 voxels,
1 m). They come only late in big collapses, when 1 m pieces are flying. That is **his decision
11** in the list below. Three arms:
- **Accept it**, recommended: a hitch of up to a metre, one piece in ~300, in the busiest moments
  of a collapse. It is below what the eye will pick out in that churn, and the screenshot at c-2
  says whether it is.
- **Swap who pops:** land the blocked flight where it stands and re-plot the reservation's owner
  on top. It is the same pop on the other piece.
- **Forbid rests on other flights' paths:** no pop, but the solver has to test every candidate
  against every remaining path. At ~65 flights × up to a hundred steps × up to 256 cells, that is
  the release rate's budget.

**Battery: thirteen recipes, twelve caught, one identity.**
- **The identity:** the check that a flight's own rest does not count against it. The flight
  asked about is always due, and due flights are skipped anyway, so the line is deleted.
- **One hole, closed:** the boundary's wait was untested.
  `AWetLandingAtTheBoundaryWaitsWhileAnotherPieceIsDrawnInItsRest` times a wet landing so that it
  reaches the boundary while another piece is drawn in its rest, and it catches the recipe.

**The water's 2 m/s cap cannot be reached at the start's pace** (half of 1 m/s is under it), so
the director test cannot see it. `WaterFactor` is pulled out and tested at 1, 4 and 10 m/s.

## c-2 — built (2026-09-26)

On his *"leave it, lets continue with c-2 please"* (decision 11 as recommended; see the last
point below).

**What was built:**
- **`PieceMesher` (Core):** a piece's faces through the greedy mesher's own `AddQuad` and corner
  order. A one-voxel piece is the mesher's voxel quad for quad, held by a test, so the winding
  cannot drift. Glass hides and shows faces as a chunk does.
- **The shaders:** `VoxelPiece.gdshader` and its glass twin share the include; the include's
  `vertex()` sits behind `VOXEL_PIECE`, reading the light from an `instance uniform`.
- **The glitch:** `CollapseGlitch.gdshader` flickers in blocks and tears sideways, animated by
  `TIME`.
- **`CollapsePictures` (Game):** a mesh per piece in its materials, moved a mino a step and lit
  from the grid at its middle; the hand-off at both ends; the corruption on the next second's
  queue.
- **The flag and the line:** `--collapsepictures=0` is the control, and a `pictures:` term is on
  the `M9:` line. Headless builds no pictures, so every headless figure is unmoved.
- **`screenshot.gd --settle=a,b,c`** takes a sequence from one run.

Suite 1,973 → 1,976.

**Verified on a copy of his tower's backup, in daylight.** The fixture:
`--world=<copy> --profile --speed=0 --at=-826,30,279 --yaw=90 --pitch=5 --bitechunks=7
--bitedepth=1 --collapseafter=1,2 --collapsemode=pieces`, which bites a 56 m slab through the
tower at 16–24 m and films from 20 m off. What the shots show:
- small pieces raining down the column through the pond's water;
- by 50 s, 1 m tetrominoes in the air, clay L and S shapes and grey stone ones;
- rubble piling at the base, and the glitch marking the next minos.

Zero ERROR lines in every run. The copy was deleted after.

**What the look runs found:**
- **The hand-off rule the pass chose failed under load.** It was "meshed, no remesh pending",
  and 478 of 509 pieces waited out the cap, because a chunk the collapse writes every frame is
  never caught up. Built instead: count the meshes each chunk installs, and wait for one more
  than at the edit, or two if a remesh was already running. Cap hits went down to 360 of 792
  (45%). What is left is the remesh queue's own latency past half a second in a collapse this
  size.
  - **Named lever, not pulled:** a longer cap. It hides a piece longer at release and holds it
    longer at landing, trading flicker for lateness, which a screenshot cannot judge; his eye
    in play does.
- **The corruption was the frame cost.** Keyed per piece, it rebuilt nearly everything at each
  re-cut: 21 ms, then 11.7 ms a frame. Re-cuts regroup voxels into new pieces, but the minos
  stay where they are, so it is now one overlay a mino, keyed on the lattice, sharing one cube
  mesh a size. The update is now 0.05 ms typical, 8.1 ms worst; the corruption's worst is 3.7.
- **A lamp cannot light a collapse:** the lamp in the roof fell with the roof. Look runs want
  daylight.
- **Decision 11's number was wrong for his tower.** The bench said lifts rise at most a mino
  (4 voxels). His tower's copy lifted 20–21 pieces, all off a reservation, **one by 18 voxels
  (4.5 m)**. He chose "leave it" on the bench's number, so the decision goes back to him with
  this one.
- **His window report,** during a run: *"the settler for the rubble was firing heavily i think"*.
  Rubble falling again was 3% of what fell; the support field re-derived 7,742 times in ~75 s.
  Which of those he saw is asked.

## What would reverse this

- **Render-only (6):** if pieces landing on each other in flight read wrong, or a player wants to
  stand on a falling piece. Then the piece moves into the world, and fact 6's hook chain gets
  priced.
- **The growing mino (1):** if the switch from 0.25 m to 1 m pieces reads as a jump rather than
  an escalation. Then the mino is fixed, and the release cap is priced for voxel pieces all the
  way.
- **The age is stored nowhere (3):** multiplayer, or a play where quitting to dodge a collapse
  feels like a cheat.
