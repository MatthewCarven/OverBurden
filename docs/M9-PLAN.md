# M9 — The mine kills you

> Planned 2026-09-11, before any code. Split M9a–M9d.
> **Read [SCALE.md](SCALE.md) and [MATERIALS-NOTES.md](MATERIALS-NOTES.md) first**, then TODO's
> *Deferred collapse* note (Matthew, 2026-09-01), which this pass takes as its starting point rather
> than as a fallback. Every number here is derived from the constants in the code or labelled an
> estimate; the first measurement is M9a's bench arm.

M7 built the rain, M8 the water getting in. M9 is the last link of DESIGN's loop before the player
can fight back — ***saturated rock loses cohesion and the ceiling comes down*** — and it is the
milestone DESIGN calls **v0.5, the game exists**, because it is the first one whose output is a
consequence rather than a phenomenon. Rain and water are things you watch; a collapse is a thing
that happens *to* you.

Three things ship: a **support field** over the rock near your excavation, per material, degraded
by the head of water M8c defined; **deferred collapse** of whatever the field says is unsupported,
into loose rubble that drops; and the **consequence** — rubble that lands on you hurts, rubble that
buries you kills, and the warning signs that make either attributable.

---

## Two facts from the code that shape the whole milestone

**There is no bedrock.** DESIGN's line — *BFS from anchored material (bedrock, or anything
transitively reaching it)* — names an anchor that does not exist: `mods/overburden/materials/` is
air, dirt, grass, lamp and stone, `BuildStrata` is one voxel of grass, three of dirt and stone
forever, and the world is infinite downward. What the engine does have, three times over, is the
rule **an unloaded voxel is solid** — M4a's collider, M8a's automaton, M8c's seep scan all read the
box's edge as rock. Support inherits it: *the edge of what is analysed is the anchor*. Decision 3
says what "analysed" is.

**DESIGN's literal rule has no arch, and the chunk bite makes that the common case.** A plain
distance-to-anchor rule says a voxel is at risk when it is more than `supportDistance` lateral
steps from support. Over a 16 m square room — four bites in a 2 × 2, the excavation the play
produces at four a second — the middle of the roof is 32 voxels from every wall and stone carries
30, so the middle is at risk. **So is every voxel directly above it, all the way to the sky**,
because each rests on the one below and gains nothing from the rock beside it. The roof does not
shed a stone; a one-metre shaft opens to the surface through however much overburden there is.
Real rock does not do that — a fall arches and stops — and a model that turns every second bite
into a sinkhole is not the antagonist DESIGN describes. Decision 1 adds the arch with one constant
and, as it turns out, a uniqueness proof the incremental solver needs anyway.

---

## The structural point

Every state here so far has been one of four kinds: a pure function (worldgen), a fixpoint (light,
the watershed), a function of volume (Tier A), or a trajectory (Tier B). **Support is a fixpoint —
M5's kind — and the collapse is the first *consequence* the engine draws from one.** Light is
computed and drawn; support is computed and then something is *done* about it, to the voxels, the
water and the player. That is why the milestone splits the way it does: the field first (M9a, a
fixpoint with M5b's oracle), then its wiring (M9b), then the act (M9c), then the keystone (M9d).

> **Support is skylight upside down.** Skylight enters at the sky, falls free through open air and
> attenuates one level per sideways step through it. Support enters at the anchor, rises free
> through solid rock and is spent per sideways step through it. Both are the unique fixpoint of a
> local rule whose only zero-cost step is vertical; both are derived per chunk from a padded copy
> and joined across chunk faces. M9a is M5b with the sign of Y flipped, "conducts" meaning *both
> solid*, and a per-material step cost instead of 1. Read `LightPropagator` and `ChunkLight` before
> writing a line of it — and read cache slice 2's worklog entry, because the three light defects it
> found are the three the incremental half of this can have.

And the thing the deferral buys, stated once here so every budget below can lean on it: **a late
answer is a later collapse, which is indistinguishable from the design.** Light that is late is a
dark chunk on screen; water that is late is DESIGN's *slower than realtime*; support that is late
is a roof that stands a few seconds longer. Nothing in M9 has to fit a frame.

---

## Decisions to settle before any code

Twelve, each with a recommendation. Matthew's are marked. **All twelve taken as recommended, 2026-09-11 — *"As planned"*; the estimates in 5 and 8 ship as written until play moves them.**

### 1. The rule is a budget with an arch, not a plain distance — *recommended*

Every solid voxel carries a **budget** `B` in `[0, K]`, `K = 240` (divisible by every shipped
support distance and most a mod would write). At an anchor `B = K`. A step from a solid voxel into a
solid neighbour:

| step | cost | why |
|---|---|---|
| **sideways** into material *m* | `− c(m)`, where `c(m) = round(K / supportDistance(m))` | the material being carried spends its own share: stone (30) costs 8 a step, dirt (8) costs 30 |
| **up** (the voxel rests on the one below) | `+ r`, capped at `K`, where `r = K / RestRefundVoxels` | each voxel of rock above the underside buys reach — this is the arch |
| **down** (the voxel hangs from the one above) | `− 2 · max(r, c(m))` | rock in tension; and load-bearing for decision 4, see there |

A voxel is **at risk** when no path from an anchor reaches it with `B ≥ 0`. `RestRefundVoxels = 30`
ships — `r = 8`, stone's own step, so **stone arches at 45° and soil (30 a step) at ~75°**, which is
what competent rock and sinkholes in soil respectively do. *An estimate; `--restrefund=` is the
sweep.* A mixed span spends each material's share as it crosses it, so five voxels of stone and five
of dirt cost `40 + 150 = 190 < 240` and hold — the plain rule would count the stone's span against
the dirt and drop it.

**What the arch does to the chunk bite**, derived from the constants above (the bench draws each of
these as a cross-section before any of it is believed):

A path runs to the *nearest* wall, so what matters is a room's shorter plan dimension: **a gallery
8 m wide of any length never falls in stone** — every roof voxel is within 4 m of a long wall — and
the smallest stone room that sheds anything is a 2 × 2 bite.

| room, material | at risk | falls |
|---|---|---|
| one bite, or a row of them, in stone | nothing | — |
| 16 m square (2 × 2 bites), stone | a 1 m × 1 m plug in the middle of the roof, 0.5 m thick | **20 voxels, 0.3 m³**, and the roof then holds — it arched |
| 24 m square (3 × 3), stone | a pyramid 9 m on a side at the base, 4.5 m tall | ~7,800 voxels, ~120 m³ |
| 32 m square (4 × 4), stone | 17 m on a side, 8.5 m tall — **measured at M9a: a trapezoid 17 layers (4.25 m) tall, cut flat by the hang from the anchored top face** | ~52,000 voxels, ~820 m³ — **47,600 measured** — more than one bite took out |
| one bite with its top face in the shipped soil | the soil over the middle 4 m × 4 m, 1 m thick; ~~the stone above holds~~ there is no stone above the soil — sky | 1,024 voxels, 16 m³ of dirt onto the floor — **975 measured on a terrace flat over 39 of 64 samples** |
| 16 m square, stone, under 5 m of lake (decision 5's curve) | a pyramid 7 m on a side, 5.5 m tall | ~5,700 voxels, ~90 m³ — **three hundred times the dry fall**. *Measured at M9a with the head at the underside: 532 under 2 m, 3,028 under 5 m, 14,060 under 10 m — a room 8 m down under a 5 m lake reads 13 m of head* |
| the same 16 m square under the plain rule | the 1 m column to the surface | the overburden |

The last two rows are the milestone: the keystone interaction as a number, and the reason decision
1 is not a refinement. *(Derived from the constants; the bench draws every row before it is
believed.)*

**Why the costs are what they are — the uniqueness argument.** The fixpoint `B(v) = max over solid
neighbours n of step(n → v, B(n))` has one solution iff every closed walk has strictly negative
weight. A closed walk has as many ups as downs; each up refunds `r`, each down costs at least `2r`,
each sideways step costs at least `c_min > 0` — so a cycle's weight is `≤ −(downs · r) − sideways
steps < 0` unless the walk is empty. **That is why hanging costs double and why the refund is a
world constant rather than a material's**: with a per-material refund, a dirt voxel over a stone one
refunds 30 going up and costs 16 coming down, the pair pumps itself to full budget, and a floating
slab of dirt on stone never falls. The rule was designed for physics and constrained by the proof;
the proof is also what makes decision 4's incremental solver converge.

**Storage: one byte per voxel.** `0..240` is the budget, the values above it a capped deficit (how
far past zero, for decision 8's delay). *Superseded at M9a — see "M9a — the planning pass", item 1:
`K = 120`, the byte holds `[−K, K]` exactly, and a deficit past `K` is `Unreached`.* Rounding: `round(240 / sd)` is exact for every shipped
distance and within 10% for `sd ≤ 48`; the loader **refuses `supportDistance` above 48** (12 m) the
way `ToolDefinition.MaxRadiusMetres` refuses a radius, because a 1000 in a mod file is a roof that
never falls. **Content change that rides on this: `lamp.json`'s `supportDistance` 4 → 8**, since
hanging costs double and a 1 m lamp cube under a ceiling would otherwise lose its lower half.

*Rejected: the plain distance* (the sinkhole); *a two-pass rule* — plain distance minus the height
above the void — which re-arms the moment the void's roof moves up and eats to the surface in stages;
*load and density* — a real beam-stress model, MATERIALS-NOTES' cliff.

### 2. Resolution: per voxel, one byte, only where the field is non-trivial — *recommended*

Light lives at 1 m with a face-conductance rule because a 0.75 m bore is narrower than a cell.
Support could do the same — *support crosses a cell face iff some voxel pair across it is solid on
both sides* — at 512 B and ~0.3 ms a chunk, `ChunkLight`'s shape verbatim. It is not recommended,
for one reason: **the thing that collapses is voxels.** At cell resolution "what falls" is *every
solid voxel of an at-risk cell*, so the cavity a collapse leaves is bounded by the 1 m lattice, cut
through walls the player did not dig; and the arch of decision 1 is stepped at 1 m. A quarter-metre
world with a one-metre collapse is the mismatch M8's minimum-drainable-hole question was about.

The cost of per voxel, stated: **32 KiB per analysed solid chunk** against 512 B, and a derive over
32,768 voxels against 512. *Estimate: 1–3 ms Release, 5–15 ms Debug a chunk*, from M8c's per-voxel
scan floor (0.5 ms over a padded copy) plus a label-correcting search with a bucket queue over 241
budget levels. **M9a's first bench number.** Memory in the worst world — the played mine fully
boxed in bites, ~2,000 boundary chunks inside the box — is ~64 MiB; a typical mine is tens of
chunks. All-air chunks hold no field; a chunk with no void within reach holds a flag.

**Fallback, named and not built: the cell rule.** Trigger: the bench's derive over 4 ms Release, or
the field's memory in play exceeding the mesh's. **Lever, named and not built: store the field in a
`VoxelChunk`** — a palette over budgets compresses a chunk that is 95% at `K` to a few bits a voxel
for free, and promotes as the values diversify.

### 3. What is anchored: the edge of the analysed set, and the analysed set is dug chunks and their six face neighbours — *recommended*

No bedrock, so the anchor is a rule rather than a material: **a voxel across a face into a chunk
that is unloaded, or not analysed, carries a full budget.** M4a's *unloaded is solid*, extended to
*and holds*.

The analysed set is **every chunk with `HasEdits`** — the portal rule, *a hole is only what was
dug*, from `PortalTracker.IsEdited` — **plus the chunk across each of its six faces.** The shell is
where a bite's roof is: a bitten chunk is all air, and the rock over it is the undug chunk above. Two
consequences, both stated as costs:

- **A natural cave is stable until a chunk touching it is dug.** The alternative — analyse the whole
  loaded box — makes 20% of the world's chunks (the cave share) candidates for collapse as you walk
  past them, and a collapse *edits*: every natural fall turns a regenerable chunk into a stored one,
  by the thousand, on every walk. Rejected on that alone. The cost is that the first swing near a
  wide natural cave may bring its roof down — deferred, with warning signs, and *"don't disturb the
  old workings"* is a mine's own rule. The bench's natural-cave fixture (real strata, the shipped
  cave field) measures how much of the shell around one bite is at risk from caves alone, which is
  M8c's *"the portal rule wet a natural cave beside the first shaft"* asked in advance.
- **A wedge taller than the shell is truncated at the shell's top face — for one collapse.** The
  4 × 4 room's 8.5 m pyramid is capped at the 8 m roof chunk; when the roof falls, rubble lands and rock
  leaves, the roof chunk is dug, the chunk above joins the shell and is analysed, and the collapse
  continues upward in stages. The truncation is temporal, and deferral is what makes it a feature.

**Anchoring at the analysed edge is optimistic, and unlike light the margin does not make the
visible box exact.** Light reaches 15 m inside a 16 m margin, so nothing outside the box can change
a drawn cell. A support path can run to a wall 30 m away — or at 45° upward indefinitely — so a roof
whose only support leaves the loaded region is *supported until the box moves and shows the void*.
The failure is a **later** collapse, never an earlier one, which is the failure the deferral
licenses. Recorded here so nobody looks for the margin argument and, not finding it, adds a ring.

### 4. Home: a from-scratch derive per chunk on a worker, joined at the faces by re-deriving whoever's input changed — *recommended*

The derive takes what the mesh job takes — a `PaddedChunk`, safe since M3a's copy-on-write — plus
the **six neighbours' face layers of budgets** captured on the sim thread at submission (as
`PaddedLight` is gathered), plus the per-(sample, Y) head table for decision 5, and returns the
chunk's field. If the chunk's own face layers changed, its neighbours are queued. That is chunk-wise
iteration to the fixpoint rather than M5b's propagate-and-retract walk. Three arguments:

- **The from-scratch oracle is the implementation.** M5b's un-light pass exists because a
  propagated field needs an inverse; cache slice 2 found three defects in that inverse. A chunk that
  is re-derived whole has no inverse to get wrong, and "incremental equals from-scratch" becomes an
  assertion about the *join* only.
- **Everything runs on a worker, and nothing has to fit a frame.** Light's derive is on the sim
  thread and is M5d-1's cliff; support's is a chunk job like the mesh, priced at milliseconds, and a
  round of neighbour re-derives is bounded by the reach.
- **Convergence is decision 1's proof, cashed.** A chunk whose neighbour has not derived assumes the
  neighbour's face is anchored — the safe direction, since *nothing falls* is the failure that costs
  nothing — so the iteration starts optimistic and descends. It reaches the unique fixpoint because
  every cross-face cycle is strictly negative: a floating body straddling a horizontal face loses
  `r = 8` a round, a vertical one `2c ≥ 16`, so **the pathological case converges in ≤ 30 rounds
  and the ordinary edit in one or two.** With a per-material refund the horizontal cycle would net
  zero and a floating column across a chunk face would stand forever — the same defect in the same
  place as decision 1's dirt-on-stone pair.

Derives **coalesce**: a chunk re-derives at most once per `--supportcadence=` (250 ms, an estimate)
however many edits landed, so a swing at 5/s costs a handful of derives a second, not 5 × 27.

**Triggers**: an edit in an analysed chunk; a chunk arriving into the analysed set (all three paths —
the `onVoxelsReady` hook the portal tracker uses); a neighbour's face layer changing; and, for
decision 5, the water table over any of a chunk's samples moving by a voxel since it was derived —
checked per analysed chunk at Tier A's tick, 16 lookups each. **The field is dropped on unload and
re-derived on return**; retaining it in the chunk cache the way slice 2 retains light is a named
lever, not built.

### 5. The saturation curve is content: head metres → support distance, piecewise linear, per material — *recommended, and Matthew's on the numbers*

MATERIALS-NOTES' one urgent item, the half M8c-1 did not do. `supportDistance` stays as the dry
value; a new optional field:

```json
{ "id": "stone", "supportDistance": 30, "supportUnderHead": [[2, 24], [10, 12]] }
```

Head in metres from `Saturation.HeadMetresAt` — the quantity M8c defined for this reader, stored
nowhere, derived from Tier A's level — to a support distance in voxels; linear between points,
`supportDistance` the implied point at zero head, constant past the last point, absent means dry
everywhere. The loader refuses a curve whose head is not increasing or whose distance rises with
head. `MaterialTable.SupportCost(material, headMetres)` is the projection, and the derive builds a
16-sample × 32-Y table per chunk from it, because the head is a property of the column and the row.

**The shipped numbers are estimates at the nuisance end, like `--seepscale`'s 0.06**: stone
`[[2, 24], [10, 12]]` — a 16 m square room under five metres of lake sheds three hundred times what it sheds
dry, and a tunnel still never falls; dirt and grass `[[1, 4], [3, 1]]` — a wet dirt roof falls at
any span. `--supportscale=<x>` multiplies every distance, `--saturation=0` is the dry control.
**Revisit trigger: the first play where a roof under the played lake stands when the person expected
it to go, or goes when a tunnel width should have held** — the same shape as `rainfallScale`'s
halving.

### 6. Debris: loose voxels, dropped straight down, instantly, into one `overburden:rubble` — *recommended*

TODO's *loose re-settling rubble vs rigid falling chunks — decide at M9*. Loose, and the simplest
loose there is: every at-risk voxel becomes rubble and lands on the first non-holdable voxel below it
in its own column, in one boundary, with no angle of repose — **granular flow is MATERIALS-NOTES'
deferred behaviour**, a second CA with its own termination proof, and this milestone has a
conservation oracle to keep exact already. A pile is a set of columns.

- **Rubble is one material**, `IsReplaceable = false`, hardness 0.5, `supportDistance` 2, high
  permeability. A stone roof that falls is rubble now, and mining it yields rubble — the collapse
  *costs* stone, which is right, and a rubble pile is the fill MATERIALS-NOTES gives sand. Per-source
  rubble (stone rubble, dirt rubble) is content later if the loss reads wrong.
- **Not replaceable, on M6c-1's grounds**: a brush that overwrote rubble would delete volume the
  inventory never saw. Mine it first. Revisit trigger: clearing a fall reads as tedious.
- **The volume identity is voxel count**: rock out equals rubble in, exactly, and a collapse
  strictly lowers Σ(Y of solid voxels) — M8a's potential with rock for water — so a cascade
  terminates. Both are M9c's oracle.
- **A placed chunk in mid-air is rubble by these rules and nothing special**: 32,768 voxels with no
  anchor, at risk the moment the fill lands, dropped as one fall. TODO named it the worst case; it is
  the mid-air fixture on the bench.

*Rejected: rigid falling bodies* — a moving solid against voxels and water is the hybrid
representation DESIGN rejected, arriving as a physics object.

### 7. The at-risk state is not persisted; the field is re-derived at open and every delay re-rolled — *recommended*

TODO's own recommendation, made concrete. Nothing new goes in the world file: the field is a
function of voxels and the water table, both persisted already; rubble is voxels. **No collapse may
fire until a chunk and its six neighbours have derived against their current inputs** — the `Lit`
barrier's shape — so the open has a grace of one derive round, and a roof at risk at quit is at risk
at open and falls inside the delay window, which is honest: it *was* at risk. A save cannot freeze a
ceiling.

### 8. The delay is seeded, in world time, and shortens with the deficit — *recommended; the window is Matthew's*

Per analysed chunk, one timer, started when the chunk's deficit set becomes non-empty and left
running by later derives that keep it so. Delay = `lerp(max, min, clamp(deficit / K, 0, 1)) ×
U(0.5, 1.5)` in **world seconds** off `WorldClock`, drawn from `Hash64(worldSeed, chunk, worldMillis
at arming)` — server-authoritative and stateless, per non-negotiable 1 and the note M10-NOTES
already inherited from here. **Min 2 s, max 60 s, estimates**; `--collapseafter=<min>,<max>` the
sweep. The deficit scaling is TODO's named refinement and it is what lets decision 5 express itself
as *the roof goes sooner* rather than as a step: a rising lake deepens the deficit tick by tick.

What falls is **the deficit set at the moment of firing**, re-read from the field, not the set that
armed the timer. **`--collapse=0` analyses and marks and never fires** — the warning-only arm, and
the one that lets a person walk under a marked roof to judge the marking.

### 9. Water under a fall is collected and re-poured into the cavity the rock left — never destroyed — *recommended*

`Displace` pushes a displaced cell down, sideways, then up and drains what has nowhere to go, and in
a flooded chamber every neighbour is full: a roof dropped into a flooded mine in one boundary would
report megalitres `displaced`. But **the cavity the rock leaves is exactly the room the water
needs** — N voxels of rubble land, N voxels of air open above. So a collapse runs at a fluid tick
boundary (the grid belongs to the tick; `R` and Alt+R already queue there), sums the water in every
cell it lands in, writes rock → air and air → rubble, and `Add`s the sum into the cavity from the
bottom up. `collapsed` joins both tiers' identities; `displaced` stays zero across a fall, which is
the oracle in play.

**The same rule answers TODO's fill question** (*"a fill into a flooded chamber deletes the water it
fills … a design question for M9's placement pass"*): recommended, **a fill refuses a voxel holding
water** — replaceable means air with no water in it. The cost is the loop: you cannot fill a flooded
chamber until M10's pump has emptied it. Matthew's.

### 10. Health at M9 is the minimum that makes *the mine kills you* true — *recommended; DESIGN's table needs a word*

DESIGN puts *health* in M11 and *the mine kills you* in M9, and Matthew's proposal is explicit:
*"use it to damage your health so you always mine the overhangs."* Recommended: `PlayerHealth` in
Core — a number, 100, slow regeneration; **damage per rubble voxel landing in the body's box**; a
body still overlapped after the fall goes through `PlayerSpawn.TryUnstick` (M4c's up-only rule,
which TODO has named as M9's seed twice), and **buried past the 4 m unstick bound is death**. Death
is **respawn at the world spawn with the inventory kept** — the game never ends, so death is a cost
in time and position, not a loss of anything. Health is not persisted at M9 (full at open; header
v4 is the machines'). A bar on the status line. Fall damage, drowning (M10-NOTES' *being
underwater*) and the numbers are M11's, and DESIGN's M11 row should say *the rest of health*.

### 11. Warning signs ship with the collapse, not after it — *recommended*

TODO's one real objection: a purely random collapse is not attributable, and *so you always mine the
overhangs* cannot be learned from damage with no cause. Two signs, both cheap:

- **A strain channel in the vertex colour.** `COLOR.b` is free (r and g are the two light channels);
  the mesher takes the chunk's own field — no padding, a face belongs to its voxel — and writes 1 on
  the faces of at-risk voxels, in the merge key like light, so it costs quads only where it fires.
  The shader darkens or cracks. **One remesh per at-risk transition per chunk**, which is nothing
  beside a swing's 1.6. Also what makes the field visible in a screenshot, so M9b can be judged
  before M9c falls anything.
- **Dust from the underside**: one `GPUParticles3D` per at-risk chunk emitting from the region's
  lowest faces, downward. M7c-2's particle findings apply, all four.

Sound waits for the cosmetics pass — there is no audio in the project and the gate is headroom.

### 12. The split — *recommended*

| part | ships | oracle | verified |
|---|---|---|---|
| **M9a — the field** | `Overburden.Core/Structure/`: the rule, `ChunkSupport`, the derive from a padded copy + neighbour faces + the head table, `MaterialTable.SupportCost` and the curve's loader, the anchor rule, `--support` on the bench | unique-fixpoint check per voxel; incremental == from-scratch under a fuzz of edits and arrivals; the seven cross-sections drawn | bench, Release |
| **M9b — the wiring** | the analysed set, the derive as a chunk job, coalescing, the four triggers, the neighbour join, the strain tint, `M9:` line, `support` profile line, `--support=0`, `--bitechunks=<n>` | a 2 × 2 bite in stone reads `at risk 20 voxel(s)`; one bite reads 0; the r6 control unchanged to the digit | headless + screenshot |
| **M9c — the collapse** | c-1: `overburden:rubble`, the timer, the fall at the boundary with the re-pour, the cascade, `--collapse=0`, `--collapseafter=`; c-2: health, damage, death, respawn, the buried body, dust, the HUD | voxel count conserved; Σ(Y) strictly decreases; `displaced 0` across a fall; the cascade ends with no deficit | headless: the plug falls, the line says so |
| **M9d — the keystone and the play** | the table-move trigger wired, the fill's refusal, the play recipes, the four questions | the same chamber dry and under the played lake | Matthew |

M9c-1 goes alone, the way M8b-4b did: a defect in the fall is an edit to the world that cannot be
undone and a deletion of water the identity would report but not restore.

---

## The measurement to take first, on the bench, before any of it is trusted

`--support`, Release, over real strata through the game's own terrain, printing for each fixture the
derive's ms (first and steady), the rounds to converge across faces, the at-risk count, **and an
ASCII cross-section of the wedge** through the room's middle — because the table in decision 1 is
arithmetic and the eye has to agree before the rule is believed:

1. One bite in stone, 8 m below the surface, and a row of four — nothing at risk.
2. A 2 × 2 square — the plug, 20 voxels.
3. A 4 × 4 square — the 8.5 m pyramid, truncated at the roof chunk.
4. One bite with its top face in the soil — the dirt patch, 1,024 voxels.
5. The 2 × 2 square under 0, 2, 5 and 10 m of head — the curve.
6. A chunk filled in mid-air — 32,768 at risk.
7. The natural-cave shell — a bite in the shipped cave field, at-risk voxels from caves alone.

And the collapse arm for M9c: each of those dropped, the conservation identity, the cascade's steps,
and a flooded chamber under fixture 2 with `displaced` printed. **`--collapse` reruns the flow, which
is deterministic, so the two arms start from the same roof.**

---

## Provoking it in the game

`--bitechunks=<n>` bites an *n* × *n* square of chunks under the player when the measured window opens,
through the pipeline's edit path so the tracker, the field and the automaton are told the way a
swing tells them — `--breach`'s shape, one scale up. `--collapse=0` marks and never fires;
`--collapseafter=1,2` fires almost at once. The `M9:` line prints analysed chunks, derives in the
window with worker mean and max, rounds, at-risk chunks and voxels, timers armed, collapses and
voxels fallen, litres re-poured, and health. **A seep provocation and a support provocation compose**:
`--fillbasin=2000000 --bitechunks=2 --seepscale=100` is the keystone in fifteen seconds.

---

## Deliberately not in M9

- **Granular flow** — sand and rubble with an angle of repose. MATERIALS-NOTES' expensive one.
- **Shoring** — raising local support is M10's machine, and the first thing the loop wants after
  this milestone. The rule leaves it a seat: a placed material with a large `supportDistance` *is*
  shoring, so a concrete pillar is content before it is a machine.
- **Solubility** — its own pass after M9, per MATERIALS-NOTES, now that there is a roof to dissolve
  from under.
- **Load, density, stress** — density stays unread. The arch is one constant, not a beam model.
- **Natural collapse outside dug chunks**, and the field retained across unload — both named above.
- **Health beyond the minimum, and its persistence** — M11, and header v4.
- **Sound** — the cosmetics pass.
- **Strata from JSON** — the M2b TODO. Decision 5's curves are written for materials that exist; the
  twenty-two in MATERIALS-NOTES arrive with their strata.

---

## What this pass changes in the other documents, once the decisions are taken

- **DESIGN's *Structural integrity* section**: *bedrock* → *the edge of the analysed set*; the arch
  added beside *per-material support distance*; *incrementally, locally, on a budget, off-thread*
  stays, with *deferred* added — and the M11 row gains *the rest of health*.
- **MATERIALS-NOTES** decision 1: the `supportDistance` half done, the curve's shape recorded.
- **`MaterialDefinition.SupportDistance`'s doc comment**: *in voxels* stays; the cap and the hang
  rule join it.
- **SCALE.md's *M9's collapse* paragraph**: the sizing question it named — *how many regions are at
  risk at once* — gets its answer from M9b's line, and the per-voxel derive joins the *what is
  actually iterated* table.

---

## M9a — the planning pass

> 2026-09-12, decisions 1–5 and the bench section read against `ChunkLight`, `LightPropagator`,
> `PaddedChunk`, `SeepNeighbourhood`, `Saturation` and `MaterialTable` before any code. Seven
> places the plan stopped one level above where the wrong answer sat; each is recorded beside the
> rule it changes, and none reverses a decision Matthew took.

1. **Decision 1's byte cannot hold the field at `K = 240`, and the deficit's range is the
   budget's own.** The plan puts `0..240` in the byte and "a capped deficit" above it — fifteen
   codes. But the plug's own derivation needs exact negatives: the underside's centre is `−8`, the
   layer resting on it is `−8 + r = −4` and at risk, the layer above that `0` and held. A deficit
   quantised at a chunk face moves that sign, so a plug straddling a face would be a different
   thickness from one inside a chunk, and "incremental equals from-scratch" would be false by
   representation. And decision 8 clamps `deficit / K` at 1, so the deficit is wanted all the way
   to `−K`: the range is `[−K, K]`, 481 values. **`K = 120` ships**: 241 values with an offset of
   120, fifteen codes spare, one of them *not solid*. Every cost halves exactly — stone 4 a step,
   dirt 15, the refund 4 at `RestRefundVoxels = 30` — so the geometry in decision 1's table is
   unchanged to the voxel; what changed is the deficit's granularity, which nothing reads finer
   than the delay's lerp. Rounding: `round(120 / sd)` is exact for every shipped distance and
   within 10% for `sd ≤ 32`, within 20% to the cap of 48. **The floor at `−K` is a source** — every
   solid voxel is worth at least `−K` on its own — which is what bounds the range, keeps the
   fixpoint unique (the max-over-paths argument holds with one more source), and lands exactly
   where decision 8's clamp saturates. A mid-air chunk is `−K` throughout rather than `−∞`.

2. **The derive is label-setting, not the plan's label-correcting.** Subtract `r` per voxel of
   height — `B′(v) = B(v) − r · y(v)` — and every step is non-positive: up costs 0, sideways `−c`,
   down `r − 2 · max(r, c) ≤ −r`. That is Dijkstra on a max-heap, one pop per voxel, and a bucket
   queue over the 365 primed levels (`2K + 31r`) makes it linear. The cap at `K` is
   `min(K − r · y, ·)`, monotone and non-increasing, so the label-setting argument survives it. The
   plan's estimate stands as the bench's number; what this changes is that the derive's cost is a
   bound rather than a convergence.

3. **The 4 × 4 room's pyramid is truncated by hanging, not by the face.** The roof chunk's top
   face borders an unanalysed chunk and is anchored, and a voxel hanging from an anchor is at
   `K − 2r = 112`, the one under it `104`, so the top fifteen layers of any roof chunk are held from
   above however wide the room. The plan's "8.5 m pyramid truncated at the 8 m roof chunk" is a
   pyramid of at most seventeen layers, 4.25 m, and the bench draws it before the table is
   corrected. The staged collapse decision 3 describes is unchanged — the next stage begins when the
   roof chunk is dug and the chunk above joins the set.

4. **An unanalysed *loaded* neighbour anchors through its solid border voxels only; an unloaded
   one anchors everywhere.** Decision 3 says a voxel across a face into an unanalysed chunk carries
   a full budget, and read literally that anchors a roof to the air of a natural cave or the sky
   beside it. `PaddedChunk` already carries the border, and `SeepNeighbourhood.MissingMaterial`
   already names an unloaded one, so the honest rule costs nothing: solid across the face, `K`;
   air, nothing; missing, `K`. The optimistic direction is preserved in both cases.

5. **Fixture 4's row says "the stone above holds", and there is no stone above the soil.** The
   strata are grass over dirt over stone; a bite with its top face in the soil has a roof of soil
   and sky above it. The count stands — 1,024, the middle 4 m × 4 m through all four soil layers,
   because the underside's centre is at the floor and four layers of `+4` cannot recover a `−15`
   sideways step — but the row's words are wrong and the bench's picture is the correction.

6. **A solid material with `supportDistance` 0 carries nothing sideways.** Air's 0 never enters
   the rule (air is not solid); a mod's solid 0 would divide by zero. Its cost is `2K`: one
   sideways step from a full anchor is the floor. Recorded rather than refused, because a material
   that hangs only straight down is a legitimate thing for a mod to mean.

7. **Decision 2's flag is a closed form, and it is pinned against the general path.** An all-solid
   chunk whose bottom face reads `K` everywhere is `K` throughout — every voxel rests on a full
   voxel — with no search; an all-air chunk holds no array. Both are the `IsCaveAt` /
   `GenerateTerrain` shape: one definition, two paths, an equality test between them.

Decisions taken in the pass, all as recommended above and none of them Matthew's: **`K = 120`**
(1); **the label-setting derive** (2); **anchor through solid border voxels** (4); and **the rule's
refund and distance scale are parameters of a `SupportRule` value, not constants**, because
`--restrefund=` and `--supportscale=` are named sweeps and a constant cannot be swept. The head
table is per chunk from `IWaterTable` through `Saturation.HeadMetresAt`, 16 samples × 32 rows, as
decision 5 says. Nothing in `Main`, no job, no tint — M9b's.

## M9a — built

> 2026-09-12. `Overburden.Core/Structure/` — `SupportRule`, `ChunkSupport`, `SupportCostTable`,
> `SupportFaces`, `SupportDerive`, `SupportRegion` — with `MaterialTable.SupportDistance` /
> `SupportDistanceAt` / `IsSolid`, the loader's cap and curve, `supportUnderHead` in the four
> shipped materials, `lamp.json` 4 → 8, and `--support` on the bench. Suite 1,296 → 1,319.
> Nothing in `Main`, no job, no tint: M9b's.

**The numbers, Release, `DOTNET_TieredCompilation=0`** (the line under the bench's table says
which arm; with tiering on the search reads up to 3× while `Derive` sits at tier 0, and no warm-up
by call count moves it while the bench keeps jitting new methods):

```
fixture                          analysed  derives (searched)  rounds   gather ms/derive   search ms/derive   at risk (chunks)   field KiB
1a. one bite in stone                   7        8 (  2)            2               0.19               1.14         0 ( 0)          64
1b. a row of four bites                22       38 ( 14)            2               0.19               0.83         0 ( 0)         257
2.  a 2 x 2 square (16 m)              20       34 ( 15)            2               0.20               0.80        20 ( 4)         289
3.  a 4 x 4 square (32 m)              64      128 ( 58)            3               0.24               0.95    47,600 (16)         931
4.  one bite with a soil roof           7        8 (  3)            2               0.29               0.97       975 ( 1)          96
5.  the 2 x 2 under 2 m of head        20       34 ( 15)            2               0.27               1.14       532 ( 4)         289
5.  the 2 x 2 under 5 m of head        20       34 ( 15)            2               0.21               0.90     3,028 ( 4)         289
5.  the 2 x 2 under 10 m of head       20       34 ( 15)            2               0.20               0.92    14,060 ( 4)         289
6.  a chunk filled in mid-air           7        7 (  2)            1               0.19               0.34    32,768 ( 1)          64
7.  a bite in the natural-cave shell    7        8 (  2)            2               0.19               0.84         0 ( 0)          64
```

**The first number: 0.8–1.1 ms a searched derive and 0.2 ms a gather**, against the plan's 1–3 ms
estimate and the cell rule's 4 ms trigger; ~32,000 pops and ~36,000 pushes a search, so the
label-setting argument holds in practice — a voxel is bettered once in ten. A wall chunk is the
closed form and costs the gather alone. The 4 × 4 room from nothing is 128 derives in 3 rounds,
66 ms; on eight workers in M9b that is a frame.

**Six of the seven pictures agree with the arithmetic, and the seventh answers M8c's question the
other way.** The 2 × 2 plug is sixteen voxels at the underside, four resting on them, and a row of
zeros over it — the arch, in a picture. The 4 × 4's wedge is a 45° trapezoid **seventeen layers
tall, 4.25 m, cut flat by the hang from the anchored top face** — the pass's item 3 drawn, not the
table's 8.5 m — with a ten-voxel-wide core at the underside past every wall's reach; 47,600 voxels
against the table's ~52,000. The soil roof is the middle 4 m × 4 m through all four soil layers,
975 against 1,024 on a terrace that is flat over 39 of 64 samples. The 2 × 2 under 10 m of head is
a pyramid 22 layers tall, 14,060 voxels; under 5 m, 3,028 — **the table's "under 5 m of lake,
~5,700" measured the head at the lake, and the bench measures it at the underside**, where a room
8 m down under a 5 m lake reads 13 m. The mid-air chunk is unreached throughout. **And the
natural-cave shell sheds nothing**: the bench's search found a bite whose roof chunk is 21,061
voxels of cave, and the cave's *ceiling* is in the chunk above that, outside the analysed set,
anchored — so the first bite beside a wide natural cave brings nothing down, because the roof that
would fall is not analysed until the chunk holding it is a face neighbour of a dug one. M8c's
*"the portal rule wet a natural cave beside the first shaft"* asked of the roof in advance, and
answered: not until the second bite.

**Three things the build changed, each found by a test failing, and the third by the pass's own
oracle refusing to be complete:**

- **The floor is not a source, anywhere.** The plan's rule has no floor; the byte needs one; the
  first build made `−K` a source at every solid voxel — and a chunk placed in mid-air recovered one
  refund a layer off its own floor and held its top two layers, 30,720 at risk of 32,768. The
  second made it a source only where an anchor reached — and a column straddling a chunk face
  settled at the floor rather than unreached, each half's floor justifying the other's, 118 rounds
  to get there, and the fixpoint oracle accepted it because a floor is a fixpoint. **So a path
  dies when its budget reaches `−K`, only a live path reaches anything, and a voxel no live path
  reaches is `Unreached`** — code 0, the plan's `−∞` and every deficit past `K` alike, a source of
  nothing. That is the max over *live* paths from the anchors: one fixpoint, no flood, no clamp,
  and the cost stated on the constant: a deficit past `K` is not counted further, so the exact
  rule's `−136` at a wide roof's centre — recovered thirty-four layers up — is unreached here and
  reached from the anchored top face or not at all. `SupportRule.IsLive` is the whole of it.
- **The join starts from below.** Decision 4's optimistic start — an underived neighbour is an
  anchor — loses one refund a round on a body across a face: 33 rounds and 156 derives for the
  2 × 2 room, 61 and 724 for the 4 × 4. From below — an underived analysed neighbour contributes
  nothing through its face; an unloaded or unanalysed one anchors as always — every round carries
  live paths one chunk further and the analysed set's diameter bounds it: 2 and 34, 3 and 128, the
  at-risk counts identical to the voxel, which is the uniqueness proof cashed the other way. The
  transient is inverted — a chunk derived before its neighbour reads at risk where it will hold —
  and decision 7's barrier is what keeps a collapse (M9c) and the tint (M9b) from reading it.
- **The derive is Dijkstra on a bucket queue in a height potential**, the pass's item 2, and
  `SupportDeriveResult.Pushes / Pops ≈ 1.1` says it is label-setting in practice as well as in
  proof. The cap at `K` is a clamp that only lowers, which the argument survives; a floor clamp
  would not have been — a hang from the floor to the floor lands in a bucket already scanned —
  and that is the mechanical reason the first two designs above were not just wrong but not
  label-setting either.

**The other departures from the plan, all in "M9a — the planning pass" above and now code:**
`K = 120` with the offset encoding (item 1); the anchor through solid border voxels only, and
`PaddedChunk.MissingMaterial` moved down from `SeepNeighbourhood` so both readers share the one
constant (item 4); the closed form for an all-solid chunk on a full bottom face, pinned against the
search (item 7); a solid material with `supportDistance` 0 at cost `2K` (item 6). `SupportRule` is
a record carrying `RestRefundVoxels` and `DistanceScale` — `--restrefund` and `--supportscale` on
the bench, space-separated like its siblings.

**Tests, 23 new.** The oracle is `SupportRegion.VerifyFixpoint`: every solid voxel of every field
equals the rule over its real neighbours across faces, through a per-voxel lookup sharing nothing
with the derive's padded copy, buckets or layer indexing, and the fixpoint's uniqueness makes it
complete. Around it: the plug to the voxel (the twenty positions, `−8 / −4 / 0` up the centre), one
bite and a gallery at zero, the mid-air chunk unreached, the column across a face converging to
unreached in at most `2 · 2K / r` rounds and the same column under an anchored ceiling at
`112, 104, … 0, −8, … dead`, dirt on stone unreached and a run of dirt dying at its eighth voxel, an
unanalysed neighbour anchoring through stone and not through air, the closed form against the
search, the lake over the roof against the dry roof and against a table below it, the refund and
the scale as sweeps, the layer copy per face and the neighbour's opposite face, the encoding's whole
range, the rule's constants; and the fuzz — bites, fills, departures and returns over a 36-chunk
block with sky over it, the incremental region against a fresh one marked in reverse order, byte
for byte, and both held to the fixpoint. Content: the distance reaching the table, the cap refused
at 49 and −1 and loaded at 48, eight unreadable curves refused by name, a good one interpolated at
seven heads, the placeholder's distance for a removed mod, and the shipped content's numbers —
every solid material inside the range, stone the farthest, the lamp surviving a hang, stone and
dirt carrying less under water.

**Eleven break-recipes, ten caught and one an identity that exposed a self-oracle.** Hanging at
single cost, the destination's material ignored sideways, the join never requeuing, a path live at
exactly `−K`, the closed form firing on any bottom, a departed chunk's layer kept, the head ignored,
an anchor through air, the derive reading a layer transposed, a rising curve accepted — all caught.
The shared `LayerIndex` transposed is an identity: writer and reader both go through it, so a
consistent transposition reads the right voxel — and the layer test had been using `LayerIndex` as
its own oracle, which is why it stayed green; it spells the index out now and catches that
mutation too. The join recipe is worth a note: the symmetric plug is the same twenty voxels after
one round as after two, so only the asymmetric fixtures — the dirt run, the fuzz — saw it.

**Two fixtures were wrong before the code was.** The dirt-run test put its run ten voxels up a
chunk anchored at one side and expected `120 − 4·32 − 15` at its first voxel; the arch from that
side rises at 45° and the run started at `+17`. The hanging column's first fixture put a loaded
stone chunk over it as the ceiling — analysed, so it hung from its own top and its bottom layer was
dead before the column began; an unloaded chunk anchors, a loaded one is analysed, and the
difference is the whole of decision 3. Neither was a code defect; the verifier passed both.

**For M9b, three things this leaves on the table:** the gather is a fifth of a derive and is
`PaddedChunk.CopyFrom` through `VoxelChunk.Get` — the same lever M5c-1 named for light and did not
build; a chunk under a water table rebuilds its 3 KiB cost table every derive, which is
microseconds; and the bench's row 7 is the decision-3 cost in a number — a first bite beside a
wide natural cave sheds nothing, the second may bring the cave's whole ceiling down — which is the
play question *"don't disturb the old workings"* will answer.
