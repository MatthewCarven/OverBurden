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

**Matthew, on the seventh picture (2026-09-12):** *"isn't an issue, feel free to make dangerous any
old save"* — the shell stays as built, and the played world gets no grace at open: what the field
says is at risk in a chunk-bitten void is at risk, and M9c drops it. Decision 7's barrier is the
only protection an old save gets, and it is a barrier against a transient, not a migration.

**For M9b, three things this leaves on the table:** the gather is a fifth of a derive and is
`PaddedChunk.CopyFrom` through `VoxelChunk.Get` — the same lever M5c-1 named for light and did not
build; a chunk under a water table rebuilds its 3 KiB cost table every derive, which is
microseconds; and the bench's row 7 is the decision-3 cost in a number — a first bite beside a
wide natural cave sheds nothing, the second may bring the cave's whole ceiling down — which is the
play question *"don't disturb the old workings"* will answer.

## M9b — the planning pass

> 2026-09-12, decisions 3, 4, 7, 11 and 12's M9b row read against `SupportRegion`, `ChunkPipeline`
> (`Advance`, `Submit`, `Neighbourhood`, `Apply`'s detached branch, `AdvanceLight`, the three hooks),
> `IDetachedJob`, `PortalTracker`, `GreedyMesher`'s `FaceKey`, `MeshBuffer.Shades` and
> `ChunkMeshConverter`. Nine decisions, all recommended, none reversing one Matthew took; three of
> them are places the plan's one sentence stopped above where the wrong answer sat.

**What the built M9a hands M9b, in one paragraph.** `SupportRegion` owns the analysed set, the
fields, the join's worklist and the oracle, and derives in a loop — `Settle()` — through
`_voxels(chunk)` at derive time. Every field is a new object per derive (`SupportDerive.Derive`:
*"the field returned is new each call; the scratch is not"*), so a `ChunkSupport` is immutable and
can be handed to a worker by reference; the scratch is per `SupportDerive` instance, so a worker
needs its own. `SupportFaces` copies six layers; `SupportCostTable.Build` reads `IWaterTable` —
`WaterSystem`, sim-thread only. The gather is 27 references and a padded copy, which is exactly
what `MeshChunkJob` does on the worker from references captured on the sim thread under M3a's
copy-on-write.

### 1. The derive is a detached job, not the chunk's slot — *recommended*

`Advance` says *never two jobs for one chunk*, and the derive's commonest trigger is the edit that
also queues the urgent remesh. In the chunk's slot the two serialise: derive first and the rock the
player just broke waits a round trip and a millisecond; remesh first and the tint waits behind a mesh
and lands as a second remesh anyway. `IDetachedJob` exists for a job that is not about a chunk's
state machine (slice 3b) and `Apply` already routes it by name ahead of the chunk lookup, so
`SupportDeriveJob : IDetachedJob` with `Position` = the chunk (the distance band), `JobKind.Support`
level with `Mesh`, and the urgency byte set when the cause is an edit — the remesh's own band, so
the tint arrives with the mesh it belongs to. **The submission lives in `ChunkPipeline`** — a
`Support` property beside `Fluid`, null being `--support=0` — because the 27 references have to set
`VoxelsSharedWithJob` on every neighbour the way `Neighbourhood()` does, and that flag is the
pipeline's. The job carries: the 27 references (copied to a `PaddedChunk` on the worker, as the
mesh does), a `SupportFaces` copied on the sim thread from the neighbours' current fields, a
`SupportCostTable` built on the sim thread from the live table, and a `SupportDerive` from a
per-worker pool. `Run` touches nothing of the streamer's.

Costs stated: a detached job is not cancelled by an unview, so a result for a chunk that has left
is dropped at `Applied` (the region's `Departed` has already forgotten it) — one wasted millisecond
on a worker, never a wrong field; and stale inputs are decision 2's problem, not the slot's.

### 2. Coalescing is the in-flight rule, not a timer — *recommended*

Per chunk: **at most one derive in flight and one queued.** A trigger that lands while a derive is
in flight marks the chunk and the result requeues it — `NeedsRemesh` surviving a mesh completing,
third outing (M3a's rule, then the light's). That mark is needed for correctness whatever else is
built: a derive that ran on a snapshot older than the last edit is a field the oracle rejects. The
plan's `--supportcadence=` (250 ms, "an estimate") is a second mechanism over the first with no
number behind it, so it is **deferred, not built**: the `support` line prints derives a second and
requeues, and the trigger for the timer is that number scaling with swing rate until worker time
shows on the `sources` line. Arithmetic, for the record: the entangled chip at 40/s in one chunk is
at most one derive a round trip — a frame or two — so ~30/s × 1 ms, 0.4% of eight workers, and the
neighbours re-derive only when a face layer moved, which an interior chip does not do.

### 3. The barrier is per chunk over its seven, and the tint reads only settled fields — *recommended*

M9a's join climbs, so the transient is *at risk where it will hold*: a chunk derived before its
neighbour reads a plug that is not there. A tint that reads the transient flashes for a round and
clears — two remeshes and a visible lie. Decision 7's barrier is stated for the collapse and M9b
applies it to the tint: **a chunk is settled when it and its six analysed face neighbours are
neither queued nor in flight**, evaluated at every `Applied` for the chunk and its six. Not global
quiescence — under continuous mining the region never quiesces and the tint would never update.
A newly settled chunk marks a remesh **only when its at-risk set differs from the one its last mesh
read** (`ChunkSupport.SameAtRiskSet`, a 32 KiB compare at ~10 µs with a uniform fast path — not a
hash, because a collision is a stale tint and the compare costs nothing worth the argument), and it
marks it non-urgent: `NeedsRemesh` without `RemeshUrgent`, M5c-1's relight argument — a tint 100 ms
stale is invisible, and an at-risk shell arriving urgent at the open would reorder the whole
distance-ordered queue. The `M9:` line prints `unsettled`, the count the barrier is holding back.

### 4. The mesher takes the chunk's own field by reference; strain is one bit in the key — *recommended*

`MeshChunkJob` gains a `ChunkSupport?` — the field reference at submission, immutable, no copy, and
null until the chunk has settled once (decision 3), which the mesher reads as no strain. Every face
the mesher emits belongs to a voxel of the chunk itself (`here` is inside 0..31; only `there` is
padding), so decision 11's *a face belongs to its voxel* needs no padded field. **Binary, as the
plan says**: `FaceKey(Material, CornerShade, Strain)`, a third byte in `MeshBuffer.Shades`, written
to `COLOR.b` by the converter, which today writes a constant 0 there. A graded tint (deficit / K per
face) is one line later and a screenshot's call; it would fragment quads across a gradient the way
light does, and the plan's argument is that strain costs quads only where it fires. **The r6 control
is the oracle**: no edits, an empty analysed set, zero derives, and a key that gained a constant —
2601 / 325 / 3446 at 411, to the digit. `--supporttint=0` leaves the field on and the tint out, so
the open's extra remeshes can be priced apart from the derives, and so the look A/B has a control.

### 5. Wired to the pipeline's hooks, not to the tracker — *recommended*

`onVoxelsReady(position, hasEdits)` → `Arrived`, preceded by `MarkDug` when `hasEdits`;
`onVoxelChanged(voxel)` → `Edited(voxel.Chunk)`; `onChunkForgotten` → `Departed`. **Not
`onFluidReady`**: the portal tracker moved there at M8b-4c because a drain pours into panels, and
support reads Tier A's table, not the panels — a field derived before the chunk's water is home is
correct. The tracker's `_edited` and the region's `_dug` are the same rule read off adjacent hooks;
a pipeline test asserts they agree over the fuzz. Cost stated: the edit hook is per voxel and a bite
is 32,768 calls into a `HashSet` — a few milliseconds in Debug, on the sim thread, beside the
tracker's own; if the bite hitches, the lever is the per-chunk dirty list `SetVoxel` already keeps.

### 6. The water table is read at submission; the move trigger stays M9d's — *recommended*

`SupportCostTable.Build(chunk, WaterSystem, …)` on the sim thread at submission: sixteen lookups and
3 KiB, microseconds. A chunk derived after a lake rose sees the higher head; **a settled roof under
a lake that rises afterwards is not re-derived until something else touches it** — decision 4's
fourth trigger, which the split puts in M9d with the keystone. The consequence in M9b's own
provocation: `--fillbasin` pours in the same frame the bite lands, so the bite's derives read the
poured lake, and the keystone recipe reads right without the trigger. Said so nobody reads a dry
roof under a rising lake as a defect before M9d.

### 7. `--bitechunks=<n>` with `--bitedepth=<chunks>`, 3 by default — *recommended*

An *n* × *n* square of chunks bitten through `SetVoxel` when the measured window opens — the
breach's shape one scale up — with its top face `bitedepth` chunks below the player's. **Three, not
the "under the player" the plan implies**, because the strata are a metre of soil over stone and a
bite whose roof is the chunk under the player's has that roof partly soil in most columns — fixture
4's picture, not fixture 2's — and the headless oracle is fixture 2: **`at risk 20 voxel(s) in
1 chunk(s)` at n = 2, 0 at n = 1**, in the derive counts and the at-risk count on the `support`
line. The screenshot needs a camera in the bite looking up, because a plug is an underside and no
view from the surface shows it: `--inbite` drops the flycam to the bite's centre on the same frame
(profile and screenshot arms only) and `--pitch=85` does the rest. In play `Q` is the provocation.

### 8. The `M9:` line and the `support` profile line — *recommended*

Analysed chunks, fields held and their KiB, dug chunks; derives in the window split searched /
closed-form / requeued-stale / dropped-departed and by cause — edit, arrival, neighbour, departure;
in flight, pending, unsettled; worker mean / max ms; sim-thread gather and apply mean / max ms;
at-risk chunks and voxels **over settled fields only**; tint remeshes. `M9: at open` after the
first quiescence on a named world, with the wall clock to it. Every count has its denominator
beside it, and at-risk is printed as settled so the number M9c reads is the number the line says.

### 9. The first measurement is the played world's open — *recommended*

The probe recipe (`worlds/probe-load/`, `--world=probe-load`): derives to the first quiescence,
seconds to it, worker ms in Debug **with tiering on** — the build people play — against a Debug run
of the bench's `--support` with tiering on, so the profile figure has a reference from its own
build rather than M9a's Release-tiering-off number; at-risk chunks and voxels in the void's shell,
which is what M9c will drop and what Matthew accepted; fields' KiB. Estimates, labelled: the r6 box
holds ~1,500 analysed chunks there, most bitten and so closed-form, a few hundred searched over
two or three rounds — 1–2 s of worker wall in Debug, ~100 ms of sim thread spread under the result
budget, 5–15 MiB of fields. The r6 control and the seed-world flythrough are the other two arms
and must not move.

**Held over, named:** `Settle()` stays as the bench's and the suite's loop; the region grows a
`TryTakeNext` / `Applied` pair the pipeline pumps, and the oracle survives into the pipeline as a
test — edits through `SetVoxel`, `IsQuiescent`, `VerifyFixpoint` empty — which is the wire test
M5c-2's rule asks for. The field is dropped on unload and re-derived on return, as planned; keeping
it in the chunk cache is the lever the plan named. The dust half of decision 11 waits for M9c-2 with
the rest of the look.

## M9b — built

> 2026-09-12, the same day as the pass, all nine decisions taken as recommended (*"As planned"*).
> `SupportRegion` gained the pumped arm, `SupportDeriveJob` beside the fluid tick as the second
> detached job, `ChunkPipeline.Support` beside `Fluid`, strain in the mesher's key and on `UV2.y`,
> the shader's tint, `--support=`, `--supporttint=`, `--bitechunks=`, `--bitedepth=`, `--inbite`,
> the `M9:` line and the `support` profile line. Suite 1,319 → 1,332. Eighteen recipes, sixteen
> caught, two named misses.

**The oracle in the game, first run: `at risk 20 voxel(s) in 4 settled chunk(s)`** for
`--bitechunks=2` on the seed world — the bench's fixture 2 through `SetVoxel`, workers and the
join — with 0 at n = 1, and **the r6 control unchanged to the digit, 2601 / 325 / 3446 at 411**.
The screenshot from inside the bite shows the plug's underside as a red-brown 1 m square at the
roof's centre, and the pixel says so before the eye does: (94, 58, 42) on the plug against
(110, 92, 70) with `--supporttint=0`, an off-plug pixel identical in both arms. The scripted lamp
`--debuglamp` hangs from the plug and is at risk with it, which is the 21st voxel and the fifth
chunk that run prints.

**Numbers, Debug with tiering on — the build people play.** A searched derive is 4.7–7.5 ms mean
on a worker (copy 1.4–1.9 of it), max 10.8–17.5; the Debug bench with tiering on reads 0.83 ms
gather + 4.4 ms search, so the game's figure has its reference from its own build (M9a's 0.8–1.1
is Release with tiering off — a 4–5× Debug penalty on the search, the light derive's shape). The
sim thread pays 0.01–0.03 ms to gather a request and 0.09–0.20 ms to land one; the 2 × 2 bite's
whole run of derives is 0.05% of the sim thread and 2.9% of one worker over a 15 s window. The
join in parallel is dearer than the loop: 86–209 derives for a 2 × 2 whose loop arm takes 34,
because a neighbour's landing marks a chunk in flight stale and it goes round again — `requeued
stale` on the line, 30–83 of them — and every one of those is a millisecond on a worker the
player never waits for. **The played world's open could not be measured on the void**: the
chunk-bitten world is gone (retired 2026-09-07 and deleted), and `default` today holds 220 stored
chunks with one in the spawn's box — 25 derives over 7 analysed, nothing at risk, 192 KiB. The
figure the pass wanted is still owed on the first world dug big enough to have a shell.

**Three things the build changed from the pass, each recorded beside its decision:**

- **Strain rides `UV2.y`, not `COLOR.b`.** `MeshBuffer`'s own comment had named UV2.y *"the
  obvious home for the next per-vertex scalar that turns up"* since M5a, it was a written zero,
  and taking it leaves the shade stride every M5 test indexes untouched. Constant across a quad by
  the key, so the interpolation argument that ruled UV2 out for two light channels does not apply.
- **An edit reaching a chunk in flight requeues its six as well as itself** — `MarkDug`'s rule,
  not a new one: an edit at a chunk changes what the neighbours' padded copies hold. The first
  fixture expected one requeue and was wrong; the region's comment says why seven.
- **The bite is a square one chunk deep**, the bench's fixture 2, not the cube the first build
  made — a cube edits four times the voxels for the same roof.

**The battery, eighteen recipes.** Caught: the stale mark dropped, the landing ignoring it, settled
ignoring the neighbours, `SettledField` returning the live field, a departed result kept, the join
never requeuing, `SameAtRiskSet` always true, the strain remesh never marked, the tint switch
ignored, each of the three hooks unwired (arrival at all three sites), the mesher ignoring the
field, the mesher reading it transposed, the mesh job dropping it, and the cause counts counting
calls. **Three of those were misses on the first run and fixtures, not code**: the 2 × 2 plug and
the mesher fixture's (10, 0, 10) are both symmetric under x ↔ z, so a transposed read tinted the
same voxel (the mesher's is at (10, 0, 20) now); the tint-off test's roofs had all been drawn before
any field settled, so a switch that was ignored had nothing to show (it chips a roof corner after
the settle now); and `Edited`'s early-out made the call-counting recipe an identity on the one
path the test used (it counts `Arrived` twice now). **Two misses named and kept**: `IsQuiescent`'s
pending clause is an identity by construction — a landing's join enqueues and the same `Update`'s
`StepSupport` takes, so nothing is ever pending with nothing in flight at a frame's end — and a
detached derive is dropped only by a quit, which pumps no results, so `Abandoned(requeue: true)`
is a branch for the cancel that does not exist yet.

**What the wire found.** Nothing in Core: the pumped arm equalled the loop arm byte for byte on
its first fuzz, with `RequeuedStale > 0` asserted so the fixture is known to have exercised the
rule. The one defect was the cause counter, which counted every one of a bite's 1.8 M hook calls
under *edit* on the first headless run — 32,768 voxels times seven — and counts queue changes
now, which is what "queued by cause" was ever meant to say.

**For M9c, three things this leaves:** `SupportRegion.SettledField` is the field a collapse may
read and `SettledAtRiskVoxels` the count it drops, both behind the barrier already; the
`--bitechunks` provocation composes with `--fillbasin` as planned, and the keystone recipe reads
the poured lake because the pour lands the frame before the bite; and the tint's look is judged
at the first play — a graded tint is one line in the mesher and a screenshot's call.

## M9c-1 — the planning pass

> 2026-09-13, decisions 6, 7, 8, 9 and 12's M9c row read against the built `SupportRegion`
> (`SettledField`, `IsSettled`, `TrySettle`, `Edited`'s early-out, `Applied`'s join),
> `ChunkSupport` (`IsAtRisk`, `MaxDeficit`), `ChunkPipeline.SetVoxel` and its hook chain
> (`RelightAfterEdit`, `MarkForRemesh`, `Support.Edited`, `onVoxelChanged` → `FluidField.Invalidate`,
> `FluidSimulation.Disturb`, `PortalTracker.OnVoxelChanged`), `Main.UpdateFluid`'s boundary block on
> both arms, `FluidSimulation.Add` / `Remove` / `Displace` and the ownership rules, `FluidGrid.Fill`,
> `Registry.SeedIdTable`, `PlayerSpawn.TryUnstick` and last night's bite log. Eleven decisions, all
> recommended; two of them correct an oracle the plan wrote, and one moves a line of the split.

**What the built M9b hands M9c-1, in one paragraph.** `SupportRegion._settled` holds, per chunk,
*the field as it stood the last time the chunk and its six were all at rest* — a snapshot, kept
until the next settle replaces it. `SettledField(chunk)` returns that snapshot whether or not the
chunk is settled *now*; `IsSettled(chunk)` says whether it is. When `IsSettled` is true the
snapshot is `_fields[chunk]` (every transition to settled goes through `SettleAround` for the
chunk, since it is caused by a landing in one of its seven), and that field was derived from
voxels no edit has touched since — an edit anywhere in the chunk or its six requeues it through
`Edited` → `MarkDug`, whose early-out is safe because a neighbour's view of a chunk is its border
and the join compares the border's bytes. `ChunkSupport.MaxDeficit` is decision 8's input, built.
The collapse's own writes go through `SetVoxel`, so a fall requeues the chunk it fell from and the
chunks it landed in, and the chunk above a cavity joins the analysed set through `HasEdits` —
decision 3's staged collapse needs no code of its own. And the fluid grid is the tick's between
boundaries: `Add` and `Remove` refuse mid-tick, `Disturb` defers, and `Main`'s boundary block —
`begin` in `UpdateFluid`, both arms — is the one place a frame owns the grid.

**One fact from the rule that shapes the fall, derived and then leaned on.** *The voxel directly
below an at-risk voxel is non-solid, or at risk itself.* A solid voxel below with a budget `b ≥ 0`
gives the one above `min(K, b + r) ≥ 4` by rest; an anchor gives `K`; only an unreached voxel
(contributes nothing) or one at `b < −r` (gives `b + r < 0`) leaves the voxel above at risk. So an
at-risk *run* down a column always ends on air or water, never on held rock — and that is what
makes "drop it straight down" a strictly descending move rather than a hope.

### 1. What falls is the chunk's settled at-risk set plus the at-risk runs directly under it, across faces, into settled chunks — never rock-to-rubble in place — *recommended*

Decision 6 drops "every at-risk voxel"; decision 8 fires per chunk. Read together over a plug that
straddles a horizontal chunk face — the fact above says the voxels under a firing chunk's set may be
at risk in the chunk *below*, waiting on their own timer — the literal rule lands the upper voxels
on solid rock where they stand: rock → rubble in place, no movement, `Σ(Y)` unchanged, and the
in-place rubble then re-derives as rubble resting on at-risk rock and fires again as a no-op at
every boundary until the lower chunk goes. That is a wasted fire a boundary, a log that says
"collapse: 0 voxels", and decision 6's oracle false.

So the unit of a fall is a **column run**: from each at-risk voxel of the firing chunk, follow the
column downward through at-risk voxels — into the chunk below when it is analysed *and settled*,
reading its settled field — until the first non-solid voxel. The run falls as one. If the chunk
below is unsettled the fire **waits** for a later boundary (decision 7's barrier, one chunk down).
Within the firing chunk two runs in one column (at-risk, air, at-risk) fall bottom-up, the upper
onto the lower's rubble. **Consequences, stated:** `Σ(Y)` strictly decreases at every fall, which
is the plan's oracle kept rather than weakened; rubble is never written where rock stood; and
a fall is bounded by one chunk's at-risk set plus whatever hangs under it in the column — in
practice one chunk, at most a mid-air fill's 32,768 (fixture 6).

*Rejected: one timer per connected at-risk component across chunks* — a flood over settled fields
at every settle, for a staggered fall that reads as crumbling anyway (decision 3 below).

### 2. The gate is `IsSettled` now and the world, not the snapshot — *recommended*

The plan's "the deficit set at the moment of firing, re-read from the field" names `SettledField`,
and `SettledField` is the *last* settle. A chunk edited since — the player mining the plug out
while its timer runs — is unsettled with a stale snapshot, and a collapse reading it would move a
voxel that is air: nothing at the source, **rubble from nothing at the landing**. The conservation
oracle would catch it after the fact; the gate stops it. `SupportRegion.TryReadSettled(chunk, out
field)` returns the field only when `IsSettled(chunk)`, and the fall reads each at-risk voxel's
material from the world and skips one that is not solid, counted as `stale skipped` on the line —
belt and braces, expected to read zero, printed so a non-zero says the gate has a hole.

### 3. The timer is a sweep at the boundary, armed and disarmed from settled at-risk counts, and re-armed from the new deficit after a fall — *recommended*

Per analysed chunk, one `CollapseTimer { armedAtWorldMillis, dueAtWorldMillis }`, in a
`CollapseScheduler` in Core. Rather than a hook on the settle, the scheduler **sweeps the settled
fields at each boundary** (2 Hz; a walk over ~1,500 fields is microseconds, and `SettledAtRiskChunks`
already walks them for the line): a settled chunk with `AtRiskCount > 0` and no timer arms —
`due = now + lerp(max, min, MaxDeficit / K) × U(0.5, 1.5)`, the unit from
`Hash64.Combine(seed ^ worldMillis, chunk)`, world seconds off `WorldClock` so `--clockscale` applies
to the roof the way it applies to the weather; an armed chunk whose settled count is zero
disarms; an armed chunk that is due fires if `IsSettled` and decision 5's chunks are fluid-ready,
else stays due and is tried next boundary. A re-derive that keeps the set non-empty leaves the
timer alone, as the plan says. **After a fall the timer is cleared, and the re-settle re-arms it
from the new deficit** — which gives the cascade its pace with no cascade rule: a wedge's core is
unreached (deficit `K`), so each stage re-arms at the minimum, 1–3 s, and the stages of a 4 × 4
room follow each other seconds apart, while the 2 × 2 plug's deficit of 8 — its centre at −8 — arms near the maximum,
28–84 s of warning for 20 voxels. Arming a poll late costs up to half a second against a 2–60 s
window. `--collapse=0` sweeps and arms and never fires — the warning-only arm.

### 4. The re-pour goes bottom-up from the cell above the rubble's new top, through the column it fell through, into the cavity — *recommended*

Decision 9 says "into the cavity from the bottom up", and the cavity is in the roof. In a flooded
room every cell below is full and the cavity is the only room, so that is where the water ends
and it is right. In a half-flooded room the same rule lifts a floor pool to the ceiling and drops
it back through the automaton. Pouring instead **from the first cell above the new rubble
upward** — the traversed column first, then the cavity — puts displaced water where a stone
dropped in a pool puts it: the local level rises. The arithmetic that guarantees room: per column
a run of *n* voxels lands in *n* cells holding at most *n* × 15,625 mL, and vacates *n* cells of
rock now air, so the collected water always fits and the identity is exact by construction.
Per column, in order: drain every landing cell (a collapse-specific `Lift`, `Take`'s twin with its
own counter), write the column's final materials once per cell (a cell that is both cavity and
landing is written rubble once, never air then rubble), then `Return` bottom-up through
`Add`'s path, which reads the live terrain and so sees the air the rock left. `Disturb` at each
rubble write finds an emptied cell and wakes the neighbours that hold water; `Add` wakes the rest.

### 5. Tier B gains `lifted` and `returned`; the identity does not change; the fall waits for fluid-ready chunks so `displaced 0` stays an oracle — *recommended, and a correction to decision 9*

Decision 9 has `collapsed` join *both* tiers' identities. Tier A holds no water in the cells a fall
touches — a collapse edits voxels, and a lake over a cavity that opens to the surface reaches it
through the portal tracker's hook the way a breach does, on the seam already built — so the term
is Tier B's alone, and it is two: `LiftedMillilitres` and `ReturnedMillilitres`, equal by the
arithmetic in 4, printed together and asserted equal, with any remainder sent to `Displaced` and
never dropped. `held == injected + loaded − removed − displaced − handed − evicted` is untouched,
because a lift and a return are one representation to the same representation. `injected` stays
the relay's number.

The one way a return could refuse: `Add` asks the live `VoxelFluidTerrain`, whose `IsLoaded` gate
answers *not loaded* for a chunk whose stored water is still on the lane (`FluidPending`). A fall
landing in such a chunk would also write rubble over cells whose water lands a moment later and is
displaced at its first visit. So a fire requires the firing chunk, every chunk of its runs and
every landing chunk to be fluid-ready (`MayHoldWater`'s question, asked of the pipeline), and
waits otherwise — a boundary or two after an arrival, never longer. With `--nowater` there is no
grid and no wait.

### 6. The fall runs in the fluid boundary block, after the pours and before the field sync; without water, every frame from `UpdateSupport` — *recommended*

`Main.UpdateFluid`'s `begin` block is the frame that owns the grid on both arms (the job arm
composes the next tick after it; the sim-thread arm settles in it). The collapse goes after
`TakePending` / `PourPending` and before `_field.Sync` and `Publish`, so the tick that follows reads
a terrain with the rubble in it and the panels with the water where it was returned, and the
report line and the HUD see the fall in the same frame. `--nowater` has no boundary, so
`UpdateSupport` sweeps every frame instead. **Cost, priced by the bite**: last night's
`--bitechunks=2` wrote 131,072 voxels in 197 ms Debug — 1.5 µs a voxel through the whole hook
chain — so the plug is nothing, the 4 × 4 wedge's 47,600 would be ~70 ms as one fall and is
sixteen pieces of ≤ 3,000 (~5 ms) under decision 3's per-chunk timers, and the mid-air fill's
32,768 × 2 writes is two bites in one frame, the precedent the chunk fill already set. **One term
the bite already pays and this pass names**: `FluidField.Invalidate` clones 4 KiB per voxel when
the chunk has a fluid field, so a fall under a lake churns tens of MiB; the lever is a per-chunk
invalidation on a bulk edit, the same lever M8c-1 named for the seep scan, not built until a fall
under the played lake hitches.

### 7. `overburden:rubble` is one JSON file, one palette entry, and an ID appended to old worlds — *recommended; the numbers are estimates*

`mods/overburden/materials/rubble.json`: hardness 0.5 (anything that takes dirt takes it),
`supportDistance` 2 (cost 60: a rubble voxel cantilevers half a metre off a wall at `K`, which is
a rubble ledge and not a defect), no `supportUnderHead` (a curve on 2 is a number with no play
behind it — dry everywhere until a play says otherwise), permeability 0.6 (loose fill; dirt is
0.35), density 1800, `replaceable` absent so it is false on M6c-1's grounds, `textureLayer` 4 —
a fifth `LayerColour` and jitter in `VoxelMaterial`, a grey-brown coarser than stone, a
screenshot's call. Its label is its path, *rubble*, on the hotbar for free. **An old world's
header table gains it as the next ID at open and writes it at quit** — `Registry.Register` after
`SeedIdTable`, non-negotiable 2's designed path, which the lamp never exercised because every
played world postdates it; a test seeds the played world's real table and registers rubble after.
Mining rubble yields rubble, which is placeable, which is MATERIALS-NOTES' fill for free.

### 8. The unstick moves into c-1; damage and death stay c-2 — *recommended; a line of the split, Matthew's*

Decision 12 puts "the buried body" in c-2 with health. But a body with rubble in its box before
health exists is not unhurt, it is *stuck*: M4a's collider takes its layer from the moved box, so a
box inside a wall is blocked on every axis and gravity reads as grounded. Without health the
player has `F` (fly ignores collision) and nothing else. `PlayerSpawn.TryUnstick` exists, up-only,
4 m, and `PlayerController` already re-admits through it: after a fall whose landing cells overlap
the body's box, c-1 calls it and prints `M9: the player was freed N m up`; a failed unstick —
buried past the bound — prints `M9: the player is buried` and leaves the body for c-2 to kill.
Two dozen lines, and the play of c-1 is not spent under a roof with no way out.

### 9. Flags, the `M9:` line, and one line per fall — *recommended*

`--collapse=<0|1>` (1), `--collapseafter=<min>,<max>` in world seconds (2, 60 — decision 8's
estimates, Matthew's). The `M9:` line and the `support` profile line gain `armed / due / waiting
(unsettled N, fluid-pending N)`, `fired`, `voxels fallen` with the mean drop in voxels, `stale
skipped`, `lifted / returned L`, `freed / buried`. **Each fall prints one line** — chunk, voxels,
columns, the deepest drop, litres lifted and returned, whether it was a stage of a cascade
(a fire within one delay window of the last in the same chunk) — the bite's shape, because a
fall is a rare event a person will want to find in the log, and a cascade of sixteen pieces is
sixteen lines. `M8b:`'s identity line carries `lifted` and `returned`.

### 10. The bench's `--collapse` arm and the headless oracle — *recommended*

`--support --collapse` drops every fixture: the scheduler with a zero delay, fire → `Settle` →
fire until nothing is at risk, printing stages, voxels a stage, `Σ(Y)` before and after, the
count identity, and the picture after the last stage beside the picture before. Fixture 2
flooded — a `FluidSimulation` over `VoxelFluidTerrain` on the fixture's chunks (the `--terrain`
arm's `ChunkedWorld`), the room filled to the ceiling through `Fill` — prints lifted, returned,
displaced and the automaton's identity, then ticks to rest. The bench needs an `IVoxelEditor` over
its dictionary that tells the region and the automaton what it wrote, twenty lines. In the game:
`--bitechunks=2 --collapseafter=1,2` on the seed world, headless, reads `fell 20 voxel(s)` in the
window and `at risk 0` after; the r6 control unchanged to the digit; `--nowater` the same fall
with no water lines. The flooded oracle in play is `displaced 0` on the `M8b:` line across a
`Q` bite's roof under the played lake, which is M9d's recipe read early.

### 11. Deliberately not in c-1 — *recommended*

Dust, damage, death, the health bar (c-2); the table-move trigger and the fill's refusal (M9d);
per-source rubble and an angle of repose (content and MATERIALS-NOTES' expensive CA); a fall
retained across a quit (decision 7 — a timer at quit is re-rolled at open, and a fall mid-write is
impossible because the fall is one boundary). One thing recorded rather than built: rubble that
lands on an unloaded chunk rests on it as on rock, and when that chunk loads the rubble's chunk is
dug, analysed, and the rubble over air falls again — self-correcting, and the reason the landing
walk stops at the loaded edge rather than reasoning about it.

**Tests the build owes, named now so the battery has a shape:** the plug to the voxel (16 columns
of one rubble, four of two, the floor's `Σ(Y)`); count conserved and `Σ(Y)` strictly down at every
fall over a fuzz of rooms, fired until quiet, the fixpoint holding after; the run across a
horizontal face (a fixture with the at-risk run straddling `y = 32`, both orders of firing) and
the wait on an unsettled chunk below; the stale gate (a voxel mined out after arming is not a
rubble voxel at the landing); the flooded room (`lifted == returned`, `displaced 0`, the total
unchanged, the water in the cavity) and the half-flooded one (the water above the rubble, not in
the ceiling, after the automaton rests); the fluid-pending wait; the timer's window, its seeding,
disarm on zero, indifference to a re-derive that keeps the set, the re-arm at the minimum after a
fall, `--collapse=0`; the unstick; the content — numbers, not replaceable, the ID appended to a
seeded table; and the wire — a bite through the pipeline, settle, fire, `HasEdits` on the roof
chunk, the remesh marked, the region requeued, quiescent and fixpoint-clean after.
