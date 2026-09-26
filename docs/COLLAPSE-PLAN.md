# Collapse plan — "Tetris gone bad"

The planning pass for [COLLAPSE-NOTES.md](COLLAPSE-NOTES.md), 2026-09-26, on `main`. **Not built.**
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

## What would reverse this

- **Render-only (6):** if pieces landing on each other in flight read wrong, or a player wants to
  stand on a falling piece. Then the piece moves into the world, and fact 6's hook chain gets
  priced.
- **The growing mino (1):** if the switch from 0.25 m to 1 m pieces reads as a jump rather than
  an escalation. Then the mino is fixed, and the release cap is priced for voxel pieces all the
  way.
- **The age is stored nowhere (3):** multiplayer, or a play where quitting to dodge a collapse
  feels like a cheat.
