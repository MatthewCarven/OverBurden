# M7 — Rain

> Planned 2026-09-01, before any code. Split M7a–M7d.
> **Read [SCALE.md](SCALE.md) first** — it was written for this milestone and the two after it.

M7 is the first link of DESIGN's core loop: *rain fills basins → water finds your excavation →
saturated rock loses cohesion → the ceiling comes down*. It builds the **Tier A** half of the
three-tier fluid model — analytic basins whose surface level is *solved, not simulated* — plus
the rain you can see, the rain that stops at your mine entrance, and the wet ground it leaves.

**Tier B (moving water) and Tier C (groundwater) are M8 and are not in this milestone.** M7's
water never enters an excavation.

---

## The structural point

Every milestone so far has had a bound that made a local computation exact.

- Worldgen is a pure function of one chunk.
- Meshing needs one chunk and a padded copy of its 26 neighbours.
- A carve is bounded by the tool radius.
- **Light** was the first thing that was not a function of one chunk — and it was saved by
  arithmetic: light reaches 15 m, the load margin is 16 m, so a source outside the loaded region
  contributes `max(0, 15 − 16) = 0` and light computed over the loaded region is *exact* inside
  the drawn one.

**A watershed has no such bound.** Where water goes from a column depends on terrain arbitrarily
far away; there is no attenuation distance and no margin that makes a window exact. A ridge a
kilometre away decides whether the valley you are standing in drains east or fills up. This is the
first system in the project that **cannot be made exact by choosing a big enough neighbourhood**,
and every decision in M7a is downstream of accepting that.

**What makes it tractable is that the input is a pure function rather than the world.**
`ISurfaceHeights.SurfaceHeightAt(worldX, worldZ)` already exists, is analytic, is seeded, needs no
chunk to be loaded or generated, and already has an interface with a test double — M5b built it so
a `SkyColumn` test could state a flat world in one line. So the watershed analysis:

- needs **no streaming, no margin, no barrier, no stage** — it is not a chunk-shaped computation
  at all;
- can run at **any resolution over any window**, on a worker thread, whenever it likes;
- is **deterministic from the seed**, so it never needs saving. Only the water *volume* does.

That is a much better position than M5 started from, and it is worth saying out loud because the
instinct will be to make the watershed a chunk-stage like everything else. It is not one.

### The second structural change: the world gains a clock

`WorldHeader` carries settings, the ID table, the inventory and the player. **It does not carry
time**, and nothing in the project has ever needed it: worldgen is timeless, light is a fixpoint,
physics takes `dt` as an argument and remembers nothing.

M7 needs a clock and so does everything after it — the accumulator's integrate-forward, "recent
rainfall" for the wet term, M8's fill-in-progress, M9's deferred-collapse delay. It is **additive**,
so header **v2 → v3 migrates** under M6c-2's recorded policy (*an additive version migrates; a
version that changes what a field means is refused*). Cheap now; a format break later.

---

## Decisions settled before any code

Three were put to Matthew on 2026-09-01 and answered; the rest follow from them or from reading
what already exists. **All of them are cheaper to reverse now than after M7a's code.**

### 1. M7 renders standing water — Tier A gets a surface

Two arguments, and the second is the one that decided it:

- **The renderer is not throwaway.** DESIGN's Tier B says *settled cells sleep and merge into a
  Tier A body*, so a Tier A body needs a surface whether it is built at M7 or M8. Building it here
  is bringing work forward, not duplicating it.
- **Without it M7 has no play verdict.** This project's habit is that a milestone ends with a
  person judging it — "0.25 m ground walks like ground", "happy with the current state", "quit and
  reopen works". A milestone whose core system is a number in an accumulator cannot be judged at
  all, and M5b already ran that experiment: it shipped a correct light grid that *did not glow*,
  and nobody could say anything about it until M5c-2.

**Known ugly part, stated up front: the shoreline.** A surface emitted on the 4 m analysis grid
against 0.25 m terrain will stair-step at the water's edge, which is the most-looked-at part of a
pond. Accepted for M7, because **M8's Tier B is what replaces it** and refining shore cells now is
work that gets thrown away. The lever if the play says it is unbearable: re-sample the boundary
ring at 1 m. Do not build it before the play asks.

### 2. Rainfall is a seeded weather cycle — and the weather is analytic

Rain rate is a **pure function of (seed, world time)**, not a state machine that has to be
stepped. The implementation is the project's existing trick applied one level up: chop time into
fixed **episodes**, hash `splitmix64(seed ^ episodeIndex)` into that episode's intensity, and
rainfall over any interval is an exact sum over the episodes it spans.

Three things fall out of that shape, and the first is the reason for it:

- **Integrate-forward becomes exact rather than approximate.** Rain over `[t₀, t₁]` is closed
  form, so a player who flies away for ten minutes and comes back finds the level the fill would
  have reached — without ten minutes of ticks having run, and *without the arithmetic differing
  from the stepped version by so much as a litre*. That is the seam DESIGN's Tier A promises, and
  an analytic weather function is what makes it defensible instead of a cheat.
- **It is server-authoritative for free**, per non-negotiable 1. Two clients evaluating the same
  function of the same seed at the same world time get the same storm with no synchronisation.
- **It is testable without a clock**, because it is a function of time rather than a thing that
  has advanced through time.

**`--rain=<intensity>` overrides it** for profiles and screenshots — the same seam `--lightbudget`
and `--cavestep` occupy, and for the same reason: an arm you cannot ask for is an arm nobody
measures.

**Time advances only while the world is open.** Quitting for a week must not flood the map. A
clock that ran on wall time would make the first action after a long absence a surprise the player
had no way to see coming, and it makes the integrate-forward interval unbounded. **Revisit
trigger: the first mechanic that wants the world to have moved on while you were away** — crops,
or an offline pump.

### 3. The watershed grid is 4 m

Half a chunk; 2×2 samples per 8 m chunk column; 16 m² of catchment per sample.

| | 30 m pond | 1 km window | verdict |
|---|---:|---:|---|
| 8 m | 4×4 = 16 samples | 15,625 | volume curve is nearly a step function; small basins vanish |
| **4 m** | **8×8 ≈ 56 samples** | **62,500** | **chosen** |
| 2 m | 15×15 ≈ 225 samples | 250,000 | better shores, 4× the analysis and 4× the memory |

It deliberately sits between the 1 m light cell and the 8 m chunk column, so it **borrows neither
one's revisit trigger** — this is the `BrushCell`-is-not-`LightCellPos` argument from M6c-1, which
exists because the 1 m light cell has a live trigger to halve it and a shared constant would move
the brush lattice from inside a change about lighting. A watershed cell tied to either would move
every basin in the world for a reason that had nothing to do with water.

**It is format-affecting**, because basin identity is derived from it (see 4). Store
`WatershedCellMetres` in the header at v3. **On mismatch, discard stored basin volumes rather than
refuse the world** — they are re-fillable in minutes of rain, and refusing a world over a
derived quantity is a worse failure than an empty lake.

### 4. A basin is identified by its pit, in world coordinates — never by an array index

The analysis runs over a player-centred window. Move the window and every array index renumbers,
so **persisted volume keyed on an index silently reattaches to a different basin** — the lake
drains and the valley next door floods, with nothing failing anywhere.

Priority-flood already gives each depression its **pit**: the world column of its lowest sample.
That is a property of the terrain, deterministic from the seed, stable across window moves,
sessions and machines. It is the same argument as non-negotiable 2 — *identity is a name the world
owns, not a number the process happens to have assigned* — arriving in a place nobody expected to
need it.

**Consequence to write down before it is discovered:** two pits can tie on elevation. The
tie-break must be a total order on the world coordinate (x then z), not "whichever the heap popped
first", or the ID moves between runs on the same terrain.

### 5. The window edge is an outlet, not a wall

A basin larger than the window is not modelled, and there are only two ways to be wrong about it:
treat the edge as a **wall** and water pools against nothing, or treat it as an **outlet** and
water leaves the world.

**Outlet, because the failure direction is the safe one.** Under-filling means a lake that should
exist does not; over-filling means water arriving where the player built. In a game whose
antagonist is water, the honest failure is the one that does not drown a base for a reason the
player cannot see. It also keeps volume conservation checkable — what leaves through the edge is
counted, so *rain in == stored + spilled* is an exact assertion rather than an inequality.

**Recorded cost:** a lake wider than the window fills only to the window's rim. **Revisit trigger:
a body of water whose far shore the player can see** — at 1,024 m the window is seven times the
r16 view distance, so this should not fire, and if it does the fix is a coarse second tier rather
than a bigger window.

### 6. Edits do not change the watershed at M7, and the seam is named

The analysis reads `SurfaceHeightAt`, which is unedited terrain *before caves are carved*. So a
pit you dug does not fill with rain at M7, and neither does a cave mouth swallow a stream.

That sounds like a hole in the core loop and is not, because **the mechanism that carries water
into an excavation is Tier B, which is M8 by design**. Shipping half of it here — edited basins
without moving water — would produce water teleporting into a hole rather than running into it.

**The structure the follow-up needs already exists and is already persisted:** `SkyColumn` holds
the *edited* sky height per voxel column, refcounted per (x, z), saved at M6a. An edited-surface
watershed reads that instead of the analytic function, at 0.25 m rather than 4 m, over a much
smaller window. Named, not built.

### 7. The code lives in `Overburden.Core/Hydrology/`

No Godot, per non-negotiable 1. The analysis takes `ISurfaceHeights` and returns plain data; the
accumulator takes a clock reading and returns levels. The renderer and the particles are Game.
`Overburden.Persistence` gains one table.

---

## M7a — The watershed

**Pure Core, headless, no threads, no Godot.** The whole part is a function from a heightmap
window to a set of basins.

### What it computes

**Priority-flood** (Barnes, Lehman & Mulla 2014 — depression filling and watershed labelling in
one pass) over the window:

1. Push every edge sample into a min-heap at its own elevation.
2. Pop the lowest; for each unvisited neighbour, its **spill level** is
   `max(neighbour elevation, popped spill level)`; push it and label it with the popped sample's
   basin.
3. A sample whose spill level exceeds its own elevation is inside a depression.

From one pass this yields, per basin: the **pit**, the **saddle** (the elevation at which it
overflows and which basin it overflows into), the **catchment** (every sample that drains to it,
which is what rain falls on), and the member samples.

**The merge tree.** When a basin fills to its saddle it does not simply spill forever into its
neighbour — the two become one body with a higher rim. Sorting saddles ascending and merging with
union-find builds the terrain's join tree up front, so a filling basin walks *up* a tree instead of
routing around a graph. **This is what makes cascading overflow acyclic by construction** rather
than by a loop guard: A spilling to B and B spilling back to A is not a case that can arise,
because above the saddle there is only one basin.

**The volume→level curve.** For a basin, sort its samples' elevations ascending and prefix-sum
them; then

```
volume(L) = Σ over samples with h < L of (L − h) × cellArea
```

is a binary search plus one multiply, and `level(V)` is the same search on the inverse. **Exact,
strictly monotonic where the basin has area, and invertible** — which is what lets the level be
*solved* rather than integrated, and what makes the round-trip test below meaningful. Capped at
the saddle; above it the surplus is the merge tree's problem.

Memory: a 1,000-sample basin costs ~12 KB of sorted elevations and sums. Tens of basins in a
window, so tens of megabytes is not a risk and a knotted 32-point approximation is **not** needed.

### The number this part is most likely to have got wrong — measured 2026-09-01

**Answered, and the answer settles it — with a caveat about the instrument that is worth more
than the number.** A 1,024 m window costs **66 ms measured on 2026-09-01 evening / 27 ms
reference-equivalent**, sampling plus flooding plus M7a-2's merge tree. **No incremental watershed
is ever built**: it runs on a worker, once per window move, and is well inside the 200 ms rethink
line below.

**The caveat: this machine was measured at 2.5x its own speed hours apart, with identical
binaries.** M7a-1 first published 19.9 ms; the same build later reported 43 ms, and the worldgen
bench — recorded at 0.47 ms/chunk since M2d-2 — reported 1.16 in the same session. The watershed
bench now prints that control above its table, and **every figure here should be divided by it
before being compared with anything in the worklog.**

**The estimate below was inside its range and wrong about which half.** Sampling measured at
**55 ns/sample**, two to four times cheaper than assumed, and the flood is **82% of the total**.
The range was right for a reason that was not, which matters because the *lever* moved:
`SurfaceHeightAt` is not where this spends its time. The original reasoning is kept below because
being wrong in this particular way is the thing worth recognising next time.

**How long the analysis takes, and therefore whether it ever needs to be incremental.**

The estimate: a 1,024 m window at 4 m is 256×256 = **65,536** samples. Worldgen produces 1,024
columns of a chunk in a fraction of 0.47 ms Release, so `SurfaceHeightAt` is on the order of
0.1–0.2 µs — call the sampling **7–13 ms**, and priority-flood on 65k cells a few more.
**~10–30 ms on a worker, once per window move.** *Estimate, not measurement.*

If that holds, **no incremental watershed is ever built**: the window is simply re-analysed
wholesale on a worker thread when the player leaves the centre box, exactly as `ChunkBox` does its
difference with hysteresis. Measure it first thing in M7a and record the real figure — if it is
under ~50 ms this is settled forever, and if it is over 200 ms the design needs a second look
before M7b depends on it.

### Tests

The oracle is invariants, not a reference implementation — no second priority-flood to get wrong.

- **`volume(level(v)) == v` and `level(volume(L)) == L`** across the range, to a stated tolerance.
- **A cone-shaped bowl checked against calculus.** `ISurfaceHeights` is an interface, so a conic
  depression is three lines, and its volume at a given level has a closed form that shares no code
  with the subject. **This is the one genuinely independent oracle in the part** — everything else
  checks the code against itself.
- **Every sample's spill level ≥ its own elevation**, and following spill targets strictly
  increases in elevation, so the merge tree is acyclic. Fuzzed over random heightmaps.
- **A basin never exceeds its saddle**: pour absurd volume in, assert the level pins at the saddle
  and the remainder was handed upward.
- **Two bowls and a ridge**: known saddle, known merge level, asserted.
- **A tilted plane has no basins at all** and every drop leaves through the edge.
- **Pit identity is stable**: analyse the same terrain through two different window origins and
  assert the same pits come back with the same catchments where the windows overlap. This is the
  test that would have caught keying a basin by index, and it is the one that matters most.
- **Break-recipes to script** (the rule is three for three from M3 onward — watch each fail):
  edge as wall instead of outlet; `>` for `>=` at the saddle; drop the saddle cap; key a basin by
  index; tie-break pits by heap order; forget `cellArea` in the volume sum; sort descending.

---

### Split into M7a-1 and M7a-2, on the evidence

**M7a-1 — the flood, the curves and the measurement — is done (2026-09-01).** M7a-2 is the merge
tree, and it was separated for a reason that only appeared while building: **the leaf spill graph
has two-cycles by construction.** A crossing costs the higher of the two samples it joins, so it is
symmetric — two basins with no cheaper exit are each other's lowest crossing, and surplus routed
around the leaves ping-pongs forever. The merge tree is what resolves it, and **1,336 of the 1,376
basins in a 1 km window spill into another basin rather than out of the world**, so it is the
common case rather than an edge case.

**M7a-2 is done (2026-09-01) and the lazy-curve question is closed by measurement**: building
every composite's curve eagerly costs **0.8 MB at a 1 km window**, so it is built eagerly and the
lazy alternative is retired rather than deferred.

One number from M7a-1 belongs to M7b rather than to M7a: **every basin in a 1 km window full to its
own saddle is 54.7 ML, which is 5.2 cm of water spread flat over the square kilometre**, and the
mean basin is 40 m³. Basins are shallow relative to rain and most of them are puddles — which is
what the rainfall rate should be set against, and it predicts ponds appearing within minutes.

## M7b — The clock, the weather and the accumulator

**Still pure Core, plus one table and one header field.**

- **`WorldClock`** — simulated milliseconds as a `long`, advanced by the frame's `dt`, persisted
  in the header at v3. Integer milliseconds rather than a `double` of seconds because a float
  clock drifts and this one is about to be the input to a determinism claim.
- **The weather function** — `RainfallRate(seed, timeMs)` and `RainfallOver(seed, t₀, t₁)`, the
  second as a closed-form sum over episodes. Episode length is a constant to be picked and stated;
  a few minutes reads as weather rather than as flicker.
- **The accumulator** — per basin, a volume in litres (M6c-2's precedent: **integers of the
  smallest real unit**, because a float accumulator drifts and "how full is it" stops having one
  answer). Rain in per interval is `RainfallOver × catchmentArea`; the level is solved from the
  curve; surplus above the saddle walks the merge tree; surplus at the window edge leaves and is
  counted.
- **Persistence** — a `basins` table keyed on `(pitX, pitZ)` with a volume, written on the same
  autosave path as everything else, plus `WatershedCellMetres` and the clock in the header.
  Basins with zero volume are simply absent.

### The oracle, and why it is the same category as M5's

**Integrating 600 s in one call must equal 600 calls of 1 s, to the litre.** No tolerance, no
epsilon — the accumulator is integers and the weather is a closed-form sum, so the two paths are
either identical or one of them is wrong.

That is not merely a good test. **It is precisely the property that "flew away mid-fill and came
back" depends on**, so the assertion and the feature are the same statement — the same shape as
M5b's *incremental light == from-scratch light*, and the reason both are worth more than a
hand-checked scenario. It is also the concrete cash value of *[slower than realtime is accuracy,
not approximation](../TODO.md)*: a tick that is allowed to be late is only allowed to be late
because arriving late and arriving on time compute the same number.

Also tested: a round trip through the store; overflow cascading two basins deep conserving volume
exactly; a world reopened with a different `WatershedCellMetres` discarding volumes rather than
throwing; the clock not advancing while the world is closed.

---

## M7c — Rain you can see

Godot side. **Headless cannot verify any of this** — M5c-2's lesson, and this is the part it was
written for — so the deliverable includes screenshots and says which question each one answers.

- **Camera-locked box of GPU-instanced, velocity-stretched billboards**, per DESIGN. Approximation,
  reads as volumetric, costs one draw call.
- **The occlusion kill is a sky-height texture.** Upload the local sky-height field from
  `SkyColumn` — which is *edited* height, so this works down a shaft you dug — as a single-channel
  texture around the player; the particle process shader samples it and kills any particle below
  it. **Resolution is the decision to make here and it is not free:** 1 m over 128 m is 32 KB and
  ~16k reads to build, but `hand_pick`'s bore is 0.75 m, so a shaft the rain should fall down would
  not register. **Recommend 0.5 m over 128 m** (256×256, ~65k reads, ~0.25 ms to rebuild on a chunk
  crossing), with the revisit trigger: *a shaft rain should fall into and does not*.
- **Splashes** at the kill point, which is also the ground.
- **The wet-surface term is one uniform and zero remeshes**, and it is where M5's bill gets
  collected. Wetness is `recent rainfall × skylight`; skylight is already `COLOR.r` per vertex and
  rainfall is global, so nothing enters the merge key and nothing is remeshed. **DESIGN's stated
  reason for keeping the two light channels separate was this exact term** — it is the first thing
  to spend what M5a paid for, and the assertion to write is that a rainstorm produces **0 remeshes**,
  the same way M5c-2 asserted a quad count.
- **Overcast dims the sky through `sky_brightness`**, the uniform M5c-2 built with nothing behind
  it precisely so a global light change could arrive without touching the grid. First consumer.
  Storm → dark → rain is most of what makes weather legible.

### One thing M7c changes about a decision currently filed as cosmetic

A wet sheen is view-dependent, and on 2026-09-01 the face tint was found to be computed from
`NORMAL` inside `fragment()`, **which in Godot 4 is view space** — filed as an A/B for a person
because Matthew likes the current look. **The moment M7c adds a second view-dependent term, that
stops being a look preference and becomes a correctness question**, because a sheen computed off a
camera-relative normal will slide across surfaces as the player turns. Settle it in M7c rather than
discovering it in a screenshot.

---

## M7d — Standing water, measurement and play

- **The Tier A surface**: one flat translucent surface per basin with volume, at the solved level,
  over the samples below it. Depth-write off, no back-to-front sorting inside a basin because it is
  one plane; **M8 owns the real transparent pass**.
- **Measurement**, with the arms named up front: analysis job ms (the M7a estimate, confirmed or
  corrected); `update ms` and `viewer ms` against the no-rain control; particle draw calls and
  primitives from `StreamingProfiler.RenderLine`; **remeshes during a storm, which must be zero**;
  litres per minute against wall clock, so "slower than realtime" has a number rather than a
  feeling.
- **Play** — the questions no test answers:
  - Does rain read as rain, and does the storm read as weather rather than as the renderer
    breaking?
  - **Does it stop at the mine entrance?** DESIGN calls this *the detail people notice*.
  - Does a pond visibly rising over minutes feel right, or does it feel broken? This is the play
    that judges the project-level principle, not just this milestone's tuning.
  - Is the 4 m shoreline tolerable until M8?

---

## Order, and the case for a different one

**a → b → c → d.** M7b needs M7a's curves; M7d needs both plus a surface to draw; M7c needs only
`SkyColumn`, which already exists.

**So M7c could go first**, and that is not a silly idea — it is the part with something on screen,
and M6d-1 established the precedent that a later part goes first when it answers an earlier part's
question. It is not chosen because the wet term's "recent rainfall" wants the weather function to
exist, and stubbing it means writing the seam twice. **If the milestone starts to feel long
without anything visible, reordering c ahead of b is the intended relief valve** and costs a debug
slider.

---

## Deliberately not in M7

- **Tier B and Tier C.** Moving water, seepage, water in your excavation, pumps. That is M8, and
  M7's job is to hand it a volume and a level.
- **Edits changing the watershed.** See decision 6; the seam is `SkyColumn` and it is already
  persisted.
- **Rivers.** Overflow between basins is arithmetic at M7, not a visible stream. A stream is
  moving water, which is Tier B.
- **Rain while the world is closed**, and any offline progression.
- **Erosion.** The terrain is a pure function of the seed and non-negotiably so.
- **Audio.** The project has none, and rain is not the milestone to introduce a subsystem.
- **Snow, temperature, biomes, seasons.** The weather function returns an intensity, and every one
  of those is a second output nobody needs yet.
- **A second coarse watershed tier** for lakes larger than the window. Named in decision 5 with its
  trigger; do not build it before the trigger fires.
