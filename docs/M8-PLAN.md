# M8 — Water gets in

> Planned 2026-09-02, before any code. Split M8a–M8d.
> **Read [SCALE.md](SCALE.md) first**, and read M7's play findings in [TODO.md](../TODO.md) —
> two of them are inputs to this milestone rather than context for it.

M7 built **Tier A**: analytic basins whose level is *solved, not simulated*. It fills the world's
hollows and it deliberately cannot see your excavation. M8 is the second link of DESIGN's core
loop — ***water finds your excavation*** — and it is where the mine starts to be the antagonist
rather than the scenery.

Three things ship: **Tier B**, a volume-conserving cellular automaton over an explicit active-cell
set, for water that is actually moving; **Tier C**, a coarse groundwater saturation field that
seeps into excavations; and the **transparent render pass** that Tier A's one-plane-per-basin
surface was explicitly a placeholder for.

---

## The structural point

Every milestone so far has produced state that is a **function of something**.

- Worldgen is a pure function of the seed and a position.
- Light is a **fixpoint** of the voxels — it has one correct value, and from-scratch *is* the
  definition.
- Physics takes `dt` and remembers nothing.
- **Tier A water is a function of volume and terrain**: pour in *N* litres and the level is solved
  by the volume→level curve, however the litres got there.

**Tier B is the first state in this project that is a trajectory rather than a value.** Where the
water is depends on where it came from and which way it went. There is no closed form to check it
against, and "run it again from scratch" is not a definition of anything, because the from-scratch
answer depends on the same history.

That has one enormous consequence and it should shape the whole milestone:

> **Tier B is a transient, and its correctness condition is that it terminates.** Every active cell
> must eventually sleep and merge into a Tier A body. A CA that keeps 40,000 cells awake because
> the water never quite settles is not slow — it is *wrong*, and it will look like a performance
> problem right up until somebody realises it is a termination problem.

DESIGN already says this — *"sleeping is load-bearing, not an optimisation"* — and the reason to
restate it here is that **the natural failure mode is invisible in a short test and fatal in a long
play.** A cell oscillating by one unit forever passes every volume-conservation assertion.

### The second structural change: state that nothing can regenerate

M6 was the first milestone where the only copy of something was on disk. **M8 is the first where
the only copy of something is in RAM and nothing can recompute it** — an in-flight flood has no
seed, no analytic form, and no fixpoint. M7's `BasinLedger` handles this for Tier A by banking
*litres per pit*, which is a number that survives anything.

So M8 needs a stated answer to *what happens to moving water when you fly away or quit*, and the
answer that costs least is almost certainly **settle it rather than store it**: integrate the
transient to its resting state and hand the volume to Tier A, whose ledger already persists. A
transient nobody is watching has no observer to be wrong for. **This is a decision below, not an
assumption.**

---

## The two questions M7's play handed this milestone

Both came from play rather than from planning, and both are load-bearing here.

### 1. The minimum drainable hole — this decides the CA's resolution

> *"Unless I make a hole 4x4 it isn't going to drain into eventually?"*

The 4 m grid is **Tier A's analysis resolution, not water's physics resolution**. If M8 implements
"water gets in" by re-running the watershed off edited heights, the height field samples every 4 m,
a 0.75 m `hand_pick` bore falls between samples, and **the minimum drainable hole really is 4 m
across** — absurd in a game whose starting tool leaves a 0.75 m bore and whose player is 0.5 m
wide.

**M5b solved the identical problem and the answer transfers verbatim.** A 1 m light cell holds 64
voxels and the tunnels this game is about are narrower than one cell, so opacity became a property
of the cell **face**: light crosses iff *some voxel pair across the shared boundary* is open on
both sides. The M8 rule is that sentence with "water" for "light".

**Twice now the answer has been that the tunnels this game is about are narrower than a grid chosen
for another reason.** That is now a standing caution rather than two anecdotes: a mining game's
characteristic feature is roughly three voxels wide, and every coarse grid this engine adds will
meet it.

### 2. Water does not fall into what you dug — the thing the milestone exists to fix

> *"The surface level was rising in sealed ponds like, but where I had tunnelled into the earth, it
> did not fall in."*

Recorded as the first play-confirmed motivation for Tier B. Worth keeping because it says which
half of M8 is the headline: **not the CA, the seam.** A perfect CA that no basin ever hands water
to changes nothing on screen.

---

## Decisions to settle before any code

Seven, in the order they constrain each other. **Numbers marked *estimate* are estimates**; the
measurement that replaces each is named.

### 1. Tier B runs at voxel resolution — 0.25 m

SCALE.md records this as open and warns it is a **64× swing**. Settling it toward voxels:

- **It is forced by decision 2 below.** A cell coarser than the bore cannot represent the bore. A
  1 m cell containing a 0.75 m tunnel is neither open nor closed, and every workaround for that is
  a fractional-occupancy model — which is a worse CA, not a cheaper one.
- **The cost is affordable and the arithmetic is in SCALE's own terms.** SCALE sizes a generous
  cave at 30 × 30 × 10 m as ~900 cells of *waterline* at 1 m. Waterline is an area, so at 0.25 m it
  is **16×, not 64× — ~14,400 cells**. At a 2 Hz tick that is **~28,800 cell visits per second,
  about 5.5% of the ~520,000/s the light budget already licenses.** *(Derived from SCALE's
  constants, not measured. The measurement that replaces it is M8a's.)*
- **The 64× figure is about volume and the active set is not a volume.** That is the same
  distinction SCALE was written to make, applied to the system SCALE flagged.

**Revisit trigger: the first sustained flood whose active set exceeds ~50,000 cells**, which is
where the estimate above stops having a factor of ten in hand.

**And re-derive SCALE.md's "sizing the systems that do not exist yet" section once M8a measures
this**, because that section says outright to.

### 2. A breach is detected at voxel resolution, on the cell *face* — never by re-analysis

The direct consequence of the play's question. Water leaves a Tier A body through a face iff **some
voxel pair across that face is open on both sides**, exactly as light crosses a cell face.

Both ends of this fail in ways worth naming, and they are the ends M5b already named:
*any open voxel drains the lake* empties a basin through a hairline crack; *the whole cell must be
open* means a pick drains nothing.

**Tier A never learns the hole exists.** It loses volume to an adjacent active cell and re-solves
its level from what remains, which is arithmetic it already does. **That is why this is cheap** —
`BasinWater.Add` already routes and re-solves; M8 adds a `Remove` on the same curve.

### 3. Tier C is integrated forward, not swept — M7's trick, reused

DESIGN specifies a coarse per-1 m saturation field fed by rain and draining downhill. **A periodic
sweep over it is exactly what SCALE.md exists to forbid**: 778,752 cells at r4h/2v is 0.36 s of one
thread for a single pass.

But saturation is a slow scalar driven by rainfall, and **rainfall is already a pure function of
(seed, time)** — M7b's whole design. So a cell's saturation can be **evaluated on demand from the
time it was last touched**, precisely as `BasinRainfall` integrates a basin forward. Nothing sweeps;
a cell is computed when something asks.

**What that costs, stated rather than hidden:** drainage downhill is a *coupling* between cells,
and a coupled field has no per-cell closed form. The honest version is that **the analytic part is
the rain input and the decay; the lateral drainage is not analytic** and needs either a coarse
active set of its own or a deliberately crude approximation. **This is the part of M8 most likely
to have been costed wrong**, and it is named here for that reason — M7a named its analysis cost the
same way and was right to.

### 4. Permeability is content, not a constant

Seepage rate is *saturation × material permeability*, so permeability is a per-material number and
belongs in `mods/minercraft/materials/*.json` beside `hardness` and `lightEmission` — both of which
took this route. **Non-negotiable 5**: if it cannot be expressed the way a mod would express it,
the format is wrong.

**The trap M3c recorded applies unchanged**: a mod-authored rate is a denial-of-service vector, and
a NaN fails *open*. Bound it at load and at use, and remember M3c's finding that the loader is not
the only caller.

### 5. Moving water is settled on unload, not stored

**Superseded by M8b-4a and M8b-4b (2026-09-06 / 2026-09-07): moving water is *paused* on unload and
*stored* — awake cells listed as such — rather than settled.** The planning pass for M8b-4 records
why: a settle cannot run over a chunk the terrain calls solid, running it before the forget needs a
hook the streamer does not have, and a pause is exact where a settle moves water along paths that
may cross into other chunks that are also leaving. The "unsafe alternative" below — a flood resuming
into a world that has changed — is answered by the arrival wake rather than avoided: the chunk's
own voxels are stored with its water in one transaction, and an edit at a boundary while the chunk
was away wakes the faces on both sides. The text is kept as written for the reasoning.

Per the structural point. On a window move, a chunk unload or a quit, an active region **integrates
to rest and its volume goes to the Tier A body**, whose `BasinLedger` already persists per pit.

- **The safe failure is stated**: settling early makes water arrive at its resting place *sooner*
  than a watched flood would. Under DESIGN's *slower than realtime is accuracy* that is a
  deviation in the harmless direction, and it is the same argument that licensed integrate-forward.
- **The unsafe alternative is a partial store**: an active set written to disk without the terrain
  state it was computed against is a flood that resumes into a world that has changed.
- **Header impact: none expected.** Tier C's saturation field is the one thing that may need
  storing, and it is a candidate for being derived from (seed, time, edited heights) instead.
  **Decide at M8c, and if it must be stored, that is v3 → v4 additive under M6c-2's policy.**

### 6. Tier B water is not a voxel material

It is tempting to make water a material and let the existing mesher draw it. **Rejected before it
is proposed**, for three reasons that are each sufficient:

- **It would put water in the merge key and remesh chunks as it moved.** M7c-1's headline was that
  a whole storm costs *zero* remeshes; a CA writing voxels would cost thousands.
- **`SetVoxel` is the edit path, and an edit clones a chunk if a job holds it.** Water moving at
  2 Hz across a flooded network would clone chunks continuously — M3a's copy-on-write is sized for
  a player's swings, not for a fluid.
- **A voxel is 0/1 occupancy and water needs an amount.** DESIGN says cells hold 0–255.

Water is its own grid, drawn by its own pass. **Same shape as the light grid**, which is also not
voxels and also has its own storage.

### 7. The code lives in `Overburden.Core/Fluid/`, beside `Hydrology/`

`Hydrology/` is Tier A — analysis, basins, curves, the merge tree, the accumulator. Tier B and C
are a different kind of thing (stateful, ticked, local) and share only the litre. Splitting them
keeps M7's exactness claims legible and stops a CA's mutable arrays leaking into a module whose
whole selling point is that it stores one number per basin.

**Core, therefore Godot-free** — non-negotiable 1, and the same reason `MiningController` and
`PlaceController` are: it makes the whole thing headlessly testable and server-authoritative.

---

## Provoking the thing under test — and the relay as a debug bind

**The hardest part of testing M8 is not the CA, it is getting water to move at all.** A flood
needs a full basin, a breach in the right place, and the patience to wait for both. M7 hit the
same wall and solved it with seams rather than settings — `--clock=250` opens in a downpour,
`--rainscale=` speeds the fill without lying about the sky, `--weather=` forces the look, and
`--norain` / `--nowater` are the control arms. **Every one of those paid for itself, and M8 needs
the equivalent before it needs anything else.**

**Matthew's observation, 2026-09-02, and it is the right one:** *"The teleport-level tech will be
endgame, but for the purposes of testing? Invaluable."* A pump that moves *n* litres from here to
there on a keypress is **the provocation seam for the entire milestone**:

- **It puts water where the test needs it, instantly** — no waiting for rain, no hunting for a
  basin that happens to be full.
- **It provokes the A — B seam on demand**, which is otherwise reachable only by digging into a
  full basin and hoping.
- **It is M8a's measurement instrument.** The number M8a exists to take is *a 9,000,000 L lake
  draining into a shaft*, and this is how that case is set up in seconds rather than in a session.
- **It resets a test without restarting the world**, which is the difference between an
  afternoon of sweeps and three of them.

### Build it as a debug bind, not as content — the entangled miner's rule, second outing

**This is the one thing to get right and it is a naming decision rather than a code one.** M5d
built the entangled miner as a debug bind on `MiningController` **with no `reachMetres` in any
JSON and no `ToolDefinition` field**, and recorded why: *once that field exists, every mod writes
against it and the decision is gone for every tool.* Non-negotiable 2 is the same trap one level
up — **a content ID is never renumbered and a removed mod leaves a hole that stays**, so the
moment `minercraft:relay_pump` exists as a material, the name is permanent whether or not the
design behind it is finished.

So: **a keypress and a flag, no material, no JSON, no ID.** The endgame machine reuses the
arithmetic and is named when it is designed — which is M10, and which the M10 notes already
record as *"burn that bridge when we get to it"*.

### The trap this instrument carries, and it is the milestone's own oracle

**A debug water source creates litres from nothing, which is precisely what M8's oracle forbids.**
Volume conservation with no tolerance is the property the whole milestone is verified against, so
an instrument that injects water **will make the conservation check fail, or worse, will be
silently subtracted from it by somebody trying to make the check pass.**

**So the injected total is a reported number, not a hidden one**: the profiler prints *litres
injected* and *litres removed* beside *litres held*, and the oracle becomes `held == rained +
injected — spilled — removed`. **Third outing for "print the denominator beside the
number"** — and the first where the denominator exists because the instrument itself moved it.

**Recommended shape**, matching M7's flags rather than inventing a new convention: a key that
takes water from the basin under the crosshair, a key that dumps it at the crosshair, and
`--injectlitres=<n>` for a headless run to do the same without a keyboard.

---

## M8a — The cellular automaton and the active set

**The CA itself, in Core, with nothing feeding it.** Cells at 0.25 m holding 0–255; push down where
there is capacity, equalise sideways otherwise; sleep when stable; an explicit active set that is
the only thing iterated.

### The measurement to take first, before the design is trusted

M7a's planning pass took its expensive number first and was right to — the estimate was in range
and wrong about which half dominated. **M8a's equivalent: how large does the active set get during
a real flood, and how long does it take to terminate?**

The case to measure is **not** SCALE's settling cave. It is **a 9,000,000 L lake draining into a
shaft** — which is the case the play will actually produce, is a sustained flow rather than a
settling body, and could plausibly make an entire tunnel network active at once. *(Estimate: this
is the number most likely to be an order of magnitude out, exactly as M7c-2's occlusion field was
ten times its estimate.)*

Report **peak active cells, cell visits per second, and time to quiescence**, and print them beside
the light budget's ~520,000 visits/s so the comparison is in the report rather than in somebody's
head.

### The oracle — and it is two properties, not one

- **Volume is conserved exactly, in integers.** M7 established that litres are integral and that
  nanometres × square metres divide out exactly; the CA must not introduce a float. Total in minus
  total out equals total held, with **no tolerance** — the same standard as M7b's *an hour in one
  call equals 3,600 calls of one second, to the litre*.
- **Every run terminates, and the resting state is level.** Water left alone reaches quiescence in
  bounded steps, and no cell is left holding more than a neighbour it conducts to. **This is the
  property a volume test cannot see**, and it is the one that fails in play rather than in CI.

Both are booleans over a constructed situation with no reference implementation — the category M4a's
sealed room and M5b's fixpoint established as the best available here.

**Fuzz it against the thing that will actually break it:** irregular terrain generated from the real
worldgen, not boxes. A hand-built basin is the fixture trap this project has been caught by five
times.

---

## M8b — The seam: Tier A ↔ Tier B ← **split four ways 2026-09-03, before any code**

**The half the play asked for**, and structurally the harder one.

- **A → B, the breach.** Face-level detection at voxel resolution (decision 2). The trigger already
  exists and is already measured: `ChunkPipeline.SkyHeightChanges` is M7c-2's dig trigger and counts
  height *moves*, and the rain occlusion field is built from edited heights **at 0.5 m and a 0.75 m
  bore registers on it**. Whatever notices *daylight now reaches here* is close kin to whatever
  notices *water now reaches here*.
- **B → A, the settle.** A quiescent region becomes Tier A volume. **This is where volume is most
  likely to be created or destroyed**, because it is a change of representation, and M6a's lesson
  applies: mutate both halves of the pair or you only prove they agree.
- **Unload and quit** (decision 5).

### The planning pass, 2026-09-03 — what M8a's code and measurement changed

Three things, read against the automaton as built rather than as planned:

- **Decision 2's cell-face rule is now trivial, because the cell is the voxel.** It was written
  while the cell size was open; at 0.25 m a face between two voxels is one pair, so "some voxel
  pair across the face is open on both sides" reduces to *both voxels are open*. What survives of
  the decision is the other half — **Tier A never learns the hole exists**; it loses litres and
  re-solves.
- **A flooded mine has no Tier A body, and decision 5 assumed one.** *"An active region integrates
  to rest and its volume goes to the Tier A body, whose ledger already persists per pit"* is true of
  water that ends in a surface basin and false of water that ends in a tunnel — a tunnel is not a
  pit on the 4 m lattice and never will be. So **resting Tier B water underground has to be
  stored**, and *settle rather than store* becomes *settle, then store what settled*. That is a
  store beside `columns`, not a header change; the first state here that is neither a seed function
  nor a fixpoint nor a basin ledger.
- **M8a's finding — the flood keeps its whole wetted area awake until it is level, O(n²) in the
  corridor — means the settle is a solve, not a wait.** Tier A's *solved, not simulated* applied to
  a Tier B region: flood-fill the connected open cells from the water, sort by Y, pour the region's
  volume from the bottom up, write the cells, sleep them. O(n log n), exactly level, no diffusion.
  **It is the same operation whether the trigger is an unload or a region that has stopped
  receiving inflow**, and the plan's named fallback — a coarser rest tolerance — is not needed if
  this exists.

### The split — and each part's oracle

**M8b-1 — the seam edits, the chamber fixture, and the tick in the game loop, with the relay.**
Headless-verifiable. The two internal edits the fluid-kind decision needs (`Down` as one field;
room as a method) with `--fluid` re-run to show no digit moved. **A second `--fluid` fixture shape,
the bitten chunk** — an 8 m void under a lake, then 2 × 2 × 2 of them — because the play made
chambers the normal case and the cost model was derived from bores; **this comes before the tick
budget is settled, not after.** Then the wiring: `FluidSimulation` over `VoxelFluidTerrain` in
`Main`, ticked at 2 Hz on the sim thread, told about edits through an optional
`onVoxelChanged` on the pipeline — **and `Disturb` wakes only cells that hold water**, never the
opened voxel itself, because a chunk bite is 32,768 opened voxels and waking each for a visit that
finds nothing is 229,000 visits in one tick. **The tick budget followed on 2026-09-04, as its own
slice**: the chamber fixture priced a roof falling in at 73.5 ms in Release and ~0.7 s in Debug for
one tick, which no rate can bound, so `Step` became a cursor over its batch resumed each frame under
`--fluidbudget=` — see the worklog entry of that date for the frames it costs. The relay: **`R` puts litres at the crosshair, Alt+R
takes them back, `--injectlitres=<n>` does the first headlessly**, no material, no JSON, no ID.
The profile prints `held == injected − removed − displaced` with every term, plus active cells,
visits per second against the light budget, and tick milliseconds. **And a debug surface**, the
Tier A surface's shape over held cells, because a flood nobody can see cannot be judged and M8d's
real pass is two parts away. **Oracle:** the identity above, exact; the streaming report unchanged
to the digit with nothing injected; a screenshot with water in a dug hole.

**M8b-2 — A → B, the breach.** A *portal* is an air voxel at or below its column's analytic
surface with a face neighbour above *that* neighbour's surface — a hole in the analytic ground,
whether dug from above (a pit floor) or from the side (a tunnel into a lake bank). Portals are
found at edit time and, for chunks that arrive with edits, by a scan of any loaded chunk that
straddles the surface band. **Indexed by the 4 m sample they sit under, and only wet samples are
walked each tick**, so a dry excavation costs nothing however large. Where the neighbour is covered
by Tier A — its basin's level stands above it — the portal draws whole litres from the basin into
its own cell as fast as the cell has room, through a new `BasinWater.Remove` that is `Pour`
reversed: from the merged lake's top slab down the path to the leaf, then `Refresh`. **Oracle:** an
in-memory twin of both tiers — `A_held + B_held == rained − spilled + injected − removed −
displaced`, exact; the level falling below the face stops the flow; a dug pit under a full basin
fills and the basin drops by exactly what arrived.

**M8b-3 — B → A, the handoff and the settle.** A resting cell *above* the analytic surface, in a
basin's footprint, hands its whole litres to that basin and drains — DESIGN's *stable cells sleep
and merge into a Tier A body*, literally, and the thing that stops a surface spill diffusing
forever. **Measured first, because M8a's named fallback is waiting on this number**: how much of
a spill's awake set does the handoff remove. Then the analytic settle above, as a method on the
simulation, run on a region that has had no inflow for a stated interval and on unload. **Oracle:**
volume exact across the representation change; `VerifyQuiescent` true after every settle; a
settled region's surface level to within one cell of level everywhere.

**M8b-4 — the store: unload, load and quit.** A `fluid` table beside `columns`, one blob per chunk
holding the resting cells, written when a chunk with settled water unloads or the world saves, read
when the chunk loads, never woken by the load itself. M6b's *one caller at a time* store contract
and its `SaveAll` drain apply unchanged; **a fluid save is a save job on the serial lane and must
not be cancelled**, for the reason a chunk save must not be. **Oracle:** the Tier B twin of
`WalkingAwayAndBackLosesNoWater` — flood a mine, walk away until it unloads, walk back, quit,
reopen: the same millilitres in the same cells. **This is the part where a defect is permanent
data loss, so it is last and it is alone.**

### M8b-2 — the planning pass, 2026-09-05, before any code

Read against `BasinWater`, `MergeTree`, `VolumeCurve`, `HeightField`, `Watershed`, `WaterSystem`,
`SkyColumn`, `WaterSurface`, the pipeline's edit hook and the streamer's arrival paths. Five things
the paragraph above stated one level above where the answer sits, then the decisions, the split,
the oracles and the cost model.

**1. Tier A's height convention is the solid voxel's index, not the plane the water stands on —
and the seam is where that stops being cosmetic.** `HeightField.Sample` stores `SurfaceHeightAt`,
which is the topmost *solid* voxel; `VolumeCurve` counts litres as `(level − h)` per cell; and
`WaterSurface` draws the quad at `level × 0.25 m`, which for a level inside `(h, h+1]` is *inside*
the top ground voxel, because voxel `h` occupies `[h, h+1)`. Tier A has been self-consistent and one
slab low since M7: a pond's first 4,000 L per sample fill a phantom slab inside the rock and draw
nothing, which nobody could see. Tier B puts water in real voxels, so the moment litres cross the
seam the two conventions meet. **Recommendation: sample `SurfaceHeightAt + 1` — the plane
`SkyColumn.Seed` already calls the sky's start — so a Tier A level is the plane the water stands
on.** Basin identity is a pit's (x, z) and is untouched; capacities are differences of heights and
are unchanged; stored litres are unchanged and their solved levels move up one voxel, which is where
they always were. A pond then draws from its first litre. One line, one test (*a basin's first litre
draws above the ground, not inside it*), and every M7 level figure in the worklog reads one voxel
low from then on. **Matthew's decision**, because it moves what a played world's ponds look like.

**2. `Remove` is not `Pour` reversed; it is `LevelAt`'s walk reversed.** `Pour` fills children before
a parent's slab, bottom-up from the leaf. Water leaving through a hole in a basin's floor comes off
the *top* of whatever lake that basin is part of: walk up from the leaf exactly as `LevelAt` does —
while the node is brim-full and its parent's slab holds something — to the lake's top node, take
from that slab, then from each node down the path to the leaf, and `Refresh(basin)` once. Never a
sibling off the path: once the shared slab is empty the level is at the ridge and the other side is
its own lake, which is Tier B's *no siphons* rule arriving in Tier A. Returns what it took, which may
be less than asked. Break-recipes, each with its own test: take from the leaf first (a hole under a
merged lake would empty its own basin while the lake stayed level — the wrong physics, visibly);
take from a sibling; skip `Refresh` (the level is solved from a stale rolled-up count, which is
exactly the defect M7's battery found in `Add`); and the identity `Add(x)` then `Remove(x)` restores
every slab, to the litre. **The window twin**: a `Remove` is carried by `CaptureByBasin` → `Absorb`
with nothing new to store, so `WalkingAwayAndBackLosesNoWater` gains a removal.

**3. A portal is a property of a voxel and its six neighbours, and the analytic surface it needs is
computed already and thrown away.** `SkyColumn.Seed` evaluates `SurfaceHeightAt` for all 1,024
columns of a chunk column — the cost `viewer ms` turned out to be — and keeps only the edited
open-above height. The portal test needs the *seed* height of the voxel's column and of its four
neighbours' columns, at edit time and in the scan. **Decision: `SkyColumn` keeps its seed heights**
(a second 2 KB `short` array, ~0.6 MB at r6, never persisted because it is analytic and re-derivable
at `Seed`), so the test is array reads. That is also the number M8c's seepage wants — saturation
against the ground surface — so it is built once. With it, the portal test for voxel `v` at `(x, y,
z)` with seed surface `s`: `y > s` → not a portal; `y == s` → a *pit* portal iff `v` and `(x, s+1,
z)` are holdable; `y < s` → a *bank* portal iff some horizontal neighbour column has `s' < y` and
both `v` and that neighbour voxel are holdable. **A natural cave can never be a pit portal**, because
worldgen keeps every cave `CaveSurfaceMarginVoxels` below its own column's surface; it can only be
a bank portal, on a cliff whose neighbour column is lower by more than the margin, under a lake.

**4. The edit-time hook is a chunk bite × 32,768, and the load-time scan has a third arrival
path.** M8b-1's rule for `Disturb` applies verbatim: a bite is 32,768 `onVoxelChanged` calls, and
anything per call is multiplied by that. With seed heights as reads the test is ~100 ns and a bite
~3 ms *(estimate; measure)*. The scan runs when a chunk's voxels arrive — `Complete(Generate)`,
`Complete(Load)`, and **a cache revival, which goes through neither** — so it wants one pipeline
callback, `onVoxelsReady`, fired wherever `chunk.Voxels` is set on the sim thread, with the test
that every arrival fires it exactly once. Per chunk the scan is per column: the surface voxel, plus
the bank face `(s', s]` toward each lower neighbour — a few thousand reads, ~50 µs *(estimate)*.
Portals leave the index when their chunk is forgotten, evicted or cached, or the index grows for
ever and walks unloaded voxels.

**5. Indexed by 4 m sample, walked per key, with the covered test on the *neighbour*.**
`Dictionary<(sampleX, sampleZ), List<VoxelPos>>` keyed on the world lattice, which a window move
does not rename. Each tick: per key, one label lookup through the current window's origin (a key
outside the window or on an `Outlet` sample is skipped); per portal, covered iff
`LevelAt(basin) > n.y` where `n` is the above-surface neighbour — for a bank portal `n`'s sample,
not `v`'s, because that is where the water is. A covered portal draws `min(floor(room / 1000),
what the path holds)` whole litres: `Remove` first, then `FluidSimulation.Add`, and the two must
agree to the litre or the identity breaks. A full cell takes 15 L and then sits at 625 mL of room
until it drains — "full to the litre", by design. So one portal voxel is 30 L/s at 2 Hz, which is
M8a's measured 29; a bitten chunk under a lake is 1,024 portals in four samples, and its tick is
the chamber fixture's 43,374-cell tick, **which the tick budget slice now bounds** — the bench's
`--fluidshape chamber` is this seam's model with a lake for a source, so its figures stand as the
prediction.

**The one thing the seam does not persist, stated.** Until M8b-4, water drawn into a pit lives in
RAM: the lake has lost its litres (the ledger persists that) and a quit loses the pit's. A quit
deletes water. That is M8's known first — *the only copy of something is in RAM* — and it is why
M8b-4 exists and is last; it is acceptable for the debug-stage seam and unacceptable to ship.

**The Tier A surface over a draining pit** stays where it is: the pit's sample height is the
*analytic* one, so the quad hangs at the lake level until the level falls under the sample, while
Tier B's debug surface fills the pit below it. Two surfaces for one body of water, briefly. M8d
owns the look; recorded here so the first screenshot is not read as a defect.

#### The split — three, each headless-verifiable, in this order

- **M8b-2a — `BasinWater.Remove` and the height convention. ✅ Built 2026-09-05; see "M8b-2a —
  built" below.** Core only. The walk above, the four break-recipes, the window twin, and — if
  Matthew agrees — `SurfaceHeightAt + 1` in `HeightField.Sample` with the first-litre test.
  **Oracle**: `Add(x)` then `Remove(x)` is the identity on every slab *(corrected when built: it is
  `Remove(x)` then `Add(what it took)` that is always the identity; the other holds only inside one
  lake)*; a removal from a merged lake lowers the sibling's level to the ridge and never below it;
  `LevelAt` after a removal equals the curve's level for what remains.
- **M8b-2b — the portal index. ✅ Built 2026-09-05; see "M8b-2b — built" below.** Core, in
  `Overburden.Core/Fluid/`, with nothing drawing yet: `PortalIndex.Scan(chunk, heights, world)`,
  `OnVoxelChanged`, `Forget(chunk)`, and the seed heights on `SkyColumn`. **Oracle**: after any sequence of scans, edits and forgets, the index equals a
  from-scratch scan of every loaded chunk — M5b's incremental-equals-from-scratch, and here
  from-scratch is cheap enough to *be* the definition. Break-recipes: skip the neighbours on an
  edit; skip bank portals; skip the forget; a placed block that leaves its portal standing; a bite's
  worth of edits timed, against the ~3 ms estimate.
- **M8b-2c — the draw, the wiring, the profile and the play. ✅ Built 2026-09-05; see "M8b-2c —
  built" below. The play is Matthew's.** `onVoxelsReady` on the pipeline,
  the index fed from it and from `onVoxelChanged` and the streamer's forget; `DrawThroughPortals`
  at the tick boundary beside `PourPending`; `drawn` printed on *both* tiers' lines so each identity
  stays exact alone and the two-tier one is their sum; **`--breach=<voxels>`** as the headless
  provocation — a hole bitten under the player when the window opens, the shape `--injectlitres`
  already has — because the hardest part of testing M8 is still getting water to move. **Oracle**:
  a Core twin — `WaterSystem` over `AnalyticSurface`, `FluidSimulation` over a matching
  `PredicateTerrain` with a pit, the index between them — holding
  `A_held + B_held == rained − spilled + injected − removed − displaced` exactly across many ticks;
  the flow stops when the level reaches the face and not before; a pit under a full basin fills and
  the basin drops by exactly what arrived; the r4 and r6 streaming digits unchanged with nothing
  breached. **Play**: bite a chunk under the terrace pond with `Q` and watch the pond drop.

#### Decisions

Settled in this pass: seed heights kept on `SkyColumn`; the portal test as written in 3; portals
found through edits (below); `onVoxelsReady` as one callback for three arrival paths; the index
keyed on the world lattice; `--breach=` as the seam.

**For Matthew, two — both taken as recommended on 2026-09-05 (*"Lets continue"*): a hole is what
was dug, and the convention is fixed in M8b-2a.**

1. **What counts as a hole: only what was dug, or any air where the seed had rock?** The plan and
   the play both framed the breach as *the excavation*. **Recommendation: found through edits** —
   portals are looked for at edit time and by scanning chunks that arrive with edits, and nowhere
   else. A natural cave mouth on a cliff face under a lake in an unedited chunk is then not a
   portal, and the lake hangs over it as it does today. The alternative scans every surface-band
   chunk and drains every lake that touches a cave mouth, at load, world-wide — a behaviour M7's
   play never saw, with no cost model, from a milestone whose oracle is exact volume. It is a
   one-line widening later (drop the edits filter). **Revisit trigger: the first play that finds a
   lake overhanging a cave mouth and reads it as wrong.**
2. **Fix Tier A's height convention now (finding 1)?** **Recommendation: yes, in M8b-2a**, because
   the seam is where the one-slab offset stops being invisible, and it is cheaper to move before
   the B→A handoff (M8b-3) is written against the old convention than after.

#### Cost model, all estimates until M8b-2b and 2c measure them

Per tick: one tree walk per sample holding portals, one room read per portal under a wet sample,
and a `Remove` + `Add` per portal that draws — bounded by excavation under standing water, not by
the world. Edit time: ~100 ns per edited voxel, ~3 ms per chunk bite. Arrival: ~50 µs per chunk
in the surface band, once. Memory: 2 KB per sky column for the seed heights, ~0.6 MB at r6, plus
the index, which is one `VoxelPos` per portal. The worst tick is the chamber fixture's, already
measured and already sliced.

#### M8b-2a — built 2026-09-05

Suite 953 → 963. What departed from the paragraph above, and what it found:

- **The oracle was stated backwards.** `Add(x)` then `Remove(x)` is the identity only while the add
  stays inside one lake; water that crossed a ridge into a neighbour with room is the neighbour's,
  and a hole on this side comes back short. That is the no-siphons rule, not a defect. The identity
  that always holds is `Remove(x)` then `Add(what it took)`, because every sibling along the walk
  was full — fuzzed over random terrain, every slab and free count to the litre. Both are tests.
- **`LevelAt` had a defect the planning pass did not see, and `Remove` inherits the walk that has
  it fixed.** A composite closed at the same crossing height as its children has zero own capacity,
  so "the parent's slab is empty" said nothing about whether the water stood above the ridge; three
  pits on one flat shelf reported one lake at two heights, since M7a-2. `LakeTop` is now the one
  walk both verbs use: through a zero-capacity parent when both children are full, stopping there
  when the sibling is not. Caught by a three-pit fixture and by the random-terrain fuzz.
- **The plane is pinned to the sky column.** `HeightField` and `SkyColumn.Seed` both store
  `SurfaceHeightAt + 1`, asserted equal over sixteen columns of one analytic surface — because 2b's
  portal test asks the sky column and Tier A asks the field, and a slab between them would have
  been the seam's first defect. Every played pond's surface is one voxel higher than before.
- **Thirteen break-recipes, thirteen caught**; the old walk by exactly the two tests written for
  it, take-from-sibling by the ridge rule (invisible to capture-and-re-pour, since a drained sibling
  is a reachable state), the sky column's +1 by the pin test alone. Nothing built on the Game side.

#### M8b-2b — built 2026-09-05

Suite 963 → 974. `PortalIndex` in `Overburden.Core/Fluid/`, `SkyColumn.SeedSurfaceY`, and a
`--portal` bench arm. What departed from findings 3–5, and what it found:

- **A portal is a pair, filed under the outside's sample.** `Portal(Inside, Outside)` — the
  holdable voxel at or below its seed surface and the face neighbour above *its own* surface — so
  the covered test in finding 5 has the voxel it asks about in hand, and a bank breach under a
  ridge is walked when the *lake's* sample is wet rather than the hillside's. `IsPit` is
  `Outside.Y == Inside.Y + 1`.
- **A scan takes pairs touching the chunk from either side**, because a pit under a chunk
  boundary has its halves in two chunks and the second to arrive must find it; `Forget` removes
  pairs with either half in the chunk. Finding 4's "the surface voxel plus the bank face toward
  each lower neighbour" is the candidate set, over the footprint and its one-voxel ring.
- **The edit hook is ten pair evaluations with the five seed surfaces read once**, geometry
  before terrain. Measured in Release: **a bite is 1.249 ms with the seed as array reads and
  10.293 ms with the analytic function** — the ~3 ms estimate holds only with the arrays, which
  is why `SkyColumn` keeps them (2 KB a column, never persisted, left alone by `CopyFrom`, and a
  throw when unseeded). A deep bite is 0.904 ms of geometric exits. Lever, not built: a chunk
  wholly below its footprint's surface band skips the hook.
- **The natural-cave fixture had no caves.** The shipped threshold is never reached in the test
  region, so the first version of *a natural cave is never a pit* scanned solid rock and the
  margin recipe passed. The fixture now asserts 2,005 columns with a cave directly under the
  margin, and the recipe fails. **Fourteen break-recipes, fourteen caught** after that repair.
- **Not built, and 2c's**: the streamer-backed `ISurfaceHeights`, `onVoxelsReady`, the forget
  hook, the dug-chunks-only filter at the scan's call site, `DrawThroughPortals`, `--breach=`,
  and the profile lines. The index does not know what an edit is; decision 1 is the caller's.

#### M8b-2c — built 2026-09-05

Suite 974 → 989. `PortalDrain`, `PortalTracker`, `StreamerSeedHeights`, `WaterSystem.Draw` /
`Inject` / `BasinAtSample`, `onVoxelsReady` and `onChunkForgotten` on the pipeline, the wiring in
`Main`, `--fillbasin=` and `--breach=`, and `drawn` on both tiers' lines with a `breach` line
between them. What departed from the paragraph above, and what it found:

- **The seam works in the game on its first run at a pit**: 200,000 L poured, a 3 × 3 shaft eight
  deep under it, 7 portals covered, 1,117 L drawn over 144 draws in 20 s, both identities exact and
  Tier A's drawn equal to Tier B's, the shaft full to the litre. The r6 control with nothing
  breached is unchanged to the digit. **The first run bit the shaft under the player and drew
  nothing** — the spawn's sample belongs to a basin whose pit is elsewhere and sits above the lake
  — so the breach goes at the basin's pit, which is where a real breach drains a lake from.
- **`--fillbasin=` was not in the plan and the plan's `--breach=` measures nothing without it.** A
  breach under a dry basin is a hole in dry ground; the Tier A relay comes with it, reported as
  `injected` on the water line for the reason the Tier B relay is on the fluid line.
- **Decision 1 needed a rule the plan did not write.** "Scan chunks that arrive with edits" never
  files a pit whose mouth is the top row of a dug chunk when the chunk above arrives second and
  unedited. `PortalTracker` re-scans a dug neighbour on an unedited arrival and scans a chunk on
  its first edit; its fuzz holds the index to *the definition over every pair touching a dug, loaded
  chunk*. A natural bank in an unedited chunk stays unfiled until that chunk is dug — the decision's
  exact shape, as a test.
- **A dead-end one-voxel shaft takes about a litre a tick after the first**, because the mouth
  passes down only the room the cell under it had when visited. Not the seam's; the automaton's
  throughput, and M8d's waterfall question with a second number (the 3 × 3 breach drew 56 L/s
  against 210 nominal). Recorded, not acted on.
- **The profile's `held` on the water line counted every in-window basin twice since M7d-1**
  (window plus ledger, where the ledger is the truth). Banks first and reads the ledger now.
- **Battery**: see the worklog entry. Three of the first battery's misses were fixture holes — a
  one-draw tolerance hiding the covered test's off-by-one, a window-origin mutation landing on the
  bowl's own catchment because the whole plateau drains into it, and no test that ever overfilled
  a basin — each now a sharp case.
- **Not built, and stated**: nothing drawn persists (M8b-4); no B → A handoff (M8b-3); the
  deep-chunk hook skip named at 2b. **The play is Matthew's**: `Q` a chunk out from under the
  terrace pond and watch the pond drop.

### M8b-3 — the planning pass, 2026-09-05, and the measurement it asked for

Read against `FluidSimulation`, `FluidRules`, `FluidGrid`, `BasinWater`, `BasinRainfall`,
`WaterSystem`, `PortalDrain` and `Main`'s tick boundary. The paragraph above says *measure first*,
and the measurement needs the hand-off to exist, so M8b-3a — the hand-off — was built as the
instrument and measured in the same pass; the settle is designed here and not built. Five things the
paragraph stated one level above where the answer sits, then the number, the settle, the decisions
and the split.

**1. Water on the analytic ground is already Tier A's, by Tier A's own model — the hand-off is
`BasinRainfall`'s delivery with a different source.** Rain landing anywhere on a catchment is litres
in that catchment's basin the moment it lands; Tier A never represents water *on* a hillside, only
water *in* a pit. So a Tier B cell resting on the plane the ground stands on — `SurfaceHeightAt +
1`, the plane 2a pinned — is rain that has landed, and handing it to the basin under its sample is
the delivery the accumulator makes. The spill soaks into the ground where it stops moving and stands
up again as the lake's level. Three things follow. **A cell on an outlet sample leaves the world**,
as rain on an outlet catchment does, counted on `received` *and* `spilled`; kept in Tier B it would
sleep on the ground for ever, since nothing wakes a cell with no move. **A tank whose floor is the
natural ground leaks into the basin**, exactly as rain inside its walls would — Tier A has no
representation of a walled patch of catchment — and a tank on a placed floor does not, because the
seam reads the *seed* surface and a cell on a block is not on the plane. Stated and pinned by a test
rather than hidden; revisit trigger: the first play that builds one and reads the leak as wrong.
**And the cell above a handed cell falls into the space it left and hands off in turn**: one cell a
tick a column, the automaton's own rate, so only the cell on the plane is ever tested.

**2. "Resting" is the automaton's own visit that found no move, and it needs no new state.** A
visit that could neither fall nor spread is the visit that would have moved the water if it could —
that is the definition of rest the sleep rule already uses. `FluidSimulation` now records the cells
each tick visited and found nothing to do for (`Rested`, cleared when the next tick begins), and the
hand-off is a pass over that list at the tick boundary, beside `PortalDrain.Draw`. The automaton
stays ignorant of the analytic surface for the reason the portal index is ignorant of what an edit
is. **The plan's *resting cell above the surface* is therefore only the cell on the plane**, and
water in motion stays Tier B — a pour is seen spreading before it merges, which is DESIGN's
*stable* cells and the difference between a spill that soaks away and a spill that vanishes where
it lands.

**3. Whole litres cross; the remainder must be carried, and it is carried per pit.** A cell holds
15,625 mL and Tier A counts litres, so either a hand-off leaves up to 999 mL in every ground cell —
a film over every cell a spill touched, held, drawn, stored and never moving, the exact cost
`MinimumSpreadMillilitres` exists to prevent — or the seam keeps a sub-litre remainder somewhere. It
is kept in `SurfaceHandoff`, keyed by the basin's pit in world coordinates (the identity a window
move does not rename, non-negotiable 2 again), delivered as whole litres the moment it makes one,
and under a litre per basin that has ever received. Taking the whole cell is what makes a spill on
natural ground end with Tier B holding *nothing*, which is the end state the awake set is measured
against. Lost at quit like the rain accumulator's remainder, under a litre a basin, reported as
`pending` on the profile.

**4. The number: the hand-off removes 99.3–99.5% of a surface spill's visits and turns "never rests"
into rest in minutes of world time.** `--spill`, Release, machine control 1.07×, the relay's ball on
the ground at the bench's origin with a 256 m window of 83 basins installed around it:

```
pour       hand-off   rested after            visits        peak     ended
10,000 L   off        9,111 ticks (76 min)    25,022,162    5,775    4,541 cells held for ever
10,000 L   on           170 ticks (1.4 min)      181,674    2,172    Tier B empty; 9,999 L in 2 basins, 1,000 mL pending
50,000 L   off        never (cap 20,000)      189,876,378   12,420   11,498 cells, 6,634 still awake
50,000 L   on           314 ticks (2.6 min)      939,219    8,700    Tier B empty; 49,999 L in 2 basins, 1,000 mL pending
```

Both tiers' identities, the seam's (`received × 1,000 + pending == handed`) and the whole world's
are exact in every arm. The pass costs 0.8 µs a hand-off with the analytic seed function (8 ms over
9,618); in the game it reads a column. **So M8a's named fallback — a coarser rest tolerance — is
dead for the surface**, where the hand-off is the settle; and it removes *nothing* from a mine
flood, where no cell is on the plane, which is what the analytic settle below is for. `--fluid`
re-run with the rested list collected: every digit of the M8b-1 table reproduces, to the visit.

**5. The settle is a solve of *sheets*, not of the region — and the plan's flood-fill would
teleport water.** "Flood-fill the connected open cells from the water, sort by Y, pour from the
bottom up" fills the lowest reachable cells first regardless of which water can reach them: two
pockets fed from one junction, the lower half full and the higher half full, would have the higher
one's water moved into the lower — across a mine, through rock, with no path the automaton could
take, and visibly. The no-siphons rule is the constraint. What the automaton converges to, and what
M8a's O(n²) is spent on, is a **sheet**: the wet cells at one Y that are sideways-connected through
holdable cells and each supported (the cell below full or unholdable). A sheet's rest state is
level to within four millilitres a pair; the solve is to distribute the sheet's total evenly across
its cells — exact to a millilitre, quiescent by `FluidRules` since no pair differs by five — which
moves water only along paths the sideways rule already uses. Components are found by a flood over
wet cells at the same Y, O(n) per sheet, no sort. Two rules inside it: **the sheet is the wet cells
only, not the dry cells it could still spread into**, so a settle never snaps water across a floor
the frontier had not reached — the frontier keeps creeping at the automaton's pace and the sheet
behind it is level at once, and a settle that fires again a few ticks later pumps the front forward
two cells a time; and **a sheet whose even share would be under a litre a cell is packed, not
spread** — its water into as few of its own cells as possible at 999 mL each, the cells that held
most first — because an even film over every cell is the puddle regime `MinimumSpreadMillilitres`
was designed against, and the automaton's own answer there is a puddle.

**When it runs, stated as an estimate with the sweep beside it.** At a tick boundary, when the last
*K* ticks had no inflow — no `Add`, no portal draw, no `Disturb` — and the active set is not empty;
then again at every boundary while that holds, until the automaton is quiescent, because each pass
is O(sheet) and each moves the front. *K* = 2 ticks (one second) is the estimate; `--settleafter=
<ticks>` is the sweep and `0` settles under inflow too, which is the arm that would make a stream
down a tunnel rise uniformly instead of flowing, and is there so a person can see both. And on
unload, as a method the store's caller runs on the sheets touching the departing chunk, which is
M8b-4's call site. **Oracle**: `VerifyQuiescent` true after a settle on a fully-wetted region;
volume exact to the millilitre across every settle; every pair in a settled sheet within one
millilitre; and the two bench figures — the chamber's 1,915-tick settle and the corridor's *never*
— each becoming one solve and a handful of ticks.

#### The split — two, in this order

- **M8b-3a — the hand-off, built in this pass.** `FluidSimulation.Rested` and `HandOff` (a
  `Remove` with its own counter, `HandedMillilitres`), `WaterSystem.Receive` and `ReceivedLitres`,
  `SurfaceHandoff` in `Overburden.Core/Fluid/` beside `PortalDrain`, wired at `Main`'s tick boundary
  before the draw, `handed` and `received` on both tiers' lines with a `handoff` line between them,
  and `--spill` on the bench. **Oracle**: the Core twin — the bowl with a ramp, the relay's ball on
  the ramp, and Tier B holding nothing when the automaton stops, with both identities, the seam's
  and the whole world's exact after every boundary; the sub-litre remainder; the placed block, the
  tank, the outlet, the window move, the missing window; the real-terrain fuzz ending with Tier B
  empty; `--fluid` unchanged to the visit.
- **M8b-3b — the settle.** `FluidSimulation.LevelSheets()` (or a name that says *solve*), the
  no-inflow trigger with `--settleafter=`, the unload-time method, the `settle` line on the profile
  (settles run, sheets levelled, cells written, the longest), and `--fluidsettle` on the bench
  gaining an analytic arm beside the wait. **Not built until Matthew has seen the hand-off in play**
  — see the decisions.

#### Decisions

Settled in this pass: the hand-off tests the plane cell only; resting is the automaton's own
no-move visit; the remainder is kept per pit and taken whole; an outlet cell spills; a cell on the
ground before the first window is woken rather than skipped; the settle is per sheet, wet cells
only, packed under a litre a cell.

**For Matthew, two:**

1. **Does a spill soaking into the ground read right?** With the hand-off, the relay's water on a
   hillside runs down, pools where it rests, and each pooled cell drains into the basin the tick it
   stops — the puddle disappears from where it lay and the lake at the pit rises by what it held.
   Physically it is water finding its level; on screen it is water vanishing into grass and
   reappearing at the bottom of the valley, which M7's *"lakes overhanging yes"* play may or may
   not read as wrong. **Recommendation: ship it and let the play judge**, because the alternative
   — leaving surface water in Tier B until it is level — is the state the measurement shows never
   arriving, and the lever if it reads wrong is a delay rather than a redesign (hand off a cell
   only after it has rested *n* boundaries in a row, which costs a small dictionary). Revisit
   trigger: the play calling it a leak. **Deferred by Matthew the same evening** — *"Give me some
   time with the seepage metric, can we revisit the question when we have something for it to
   seep into"* — so the hand-off ships as recommended and the question is parked until M8c gives
   the ground `permeability`, when "soaking in" becomes a rate through rock rather than a hand-off
   at the plane. The HUD readout for `handed` / `received` waits with it.
2. **The settle's trigger interval, and whether it fires under inflow.** *K* = 2 ticks is an
   estimate; `--settleafter=` is the sweep; 0 is the arm that levels a flowing stream. **Recommend
   building 3b after the hand-off has been played**, so the settle's look question — a tunnel
   floor levelling itself in one tick behind a creeping front — is judged against a seam the play
   has already accepted, and so 3b can take the hand-off's interval (if decision 1 wants one) for
   free.

#### Cost model, one estimate left

Per tick: one `AmountAt` per no-move visit for the rested list (measured: no digit of `--fluid`
moved, and its `ms/tick` inside the machine's noise), and at the boundary one seed read and one
label lookup per rested cell — under a microsecond each with the analytic function, less with the
columns. A flood underground pays the seed read for every resting visit and hands off nothing;
**the lever, if a profile ever shows it, is the one 2b named: a chunk wholly below its footprint's
surface band skips the test.** Memory: one `VoxelPos` per rested cell per tick, and a dictionary
entry per basin that has ever received. The settle *(estimate)*: O(wet cells) per pass, a pass per
boundary while the no-inflow condition holds, so the corridor that stayed awake for 55 hours is a
few passes of a few thousand cells.

#### M8b-3a — built 2026-09-05, in the pass

Suite 989 → 1,006. `FluidSimulation.Rested` and `HandOff` with `HandedMillilitres`,
`WaterSystem.Receive` with `ReceivedLitres`, `SurfaceHandoff`, the wiring at `Main`'s boundary
before the draw, `handed` and `received` on the profile with a `handoff` line, and `--spill`. What
departed from the paragraph above, and what it found:

- **In the game, r4h/2v standing, 50,000 L from the relay, headless**: 10,558 L handed over 1,677
  hand-offs in the window, 711 mL pending, `received == handed` and both identities exact, the water
  line's `held` equal to Tier B's `handed` to the litre; the breach run unchanged with 0 handed and
  its 9 rested cells all in the shaft; the r6 control 2601 / 325 / 3446 at 411 with every new term
  zero. The 15 s window ends with the ball still draining, as the bench's 85 s says it should.
- **The played world floods on open.** A mis-invoked control (`--nosave` on the engine's side of
  the bare `--`, dropped silently) opened `default`: 465,036 L drawn into the mine in four seconds
  through the portals its 4,562 dug chunks file on arrival, 110,792 cells active, ticks of 87–188
  frames in Debug. The seam working as built, seen for the first time; recorded for the play and
  for M8b-4.
- **Eighteen break-recipes, one hole**: listing moving cells as rested passed every hand-off test,
  because a spill handed off mid-flow still ends in the basin — the mutation moved *when*, and no
  end-state assertion sees when. The rested-list test asserts a spreading cell is not listed and
  the twin asserts nothing is handed on a pour's first boundary. Five simulation recipes did not
  apply until the harness matched CRLF.
- **Not built**: the settle (3b), with the two decisions above open.

#### M8b-3b — built 2026-09-05

`FluidSimulation.LevelSheets()` in `FluidSimulation.Sheets.cs` (a partial, because the solve wakes
through the automaton's own private bookkeeping and an outside class waking cells would be a new
public mutator with a contract): a flood over wet, holdable, supported cells at one Y from each
seed, ascending Y; a sheet with no transferable pair inside it left alone; otherwise the total
distributed evenly in flood order, or packed at 999 mL into the cells that held most when the
share is under that; every move a `FluidGrid.Transfer` between two cells of one sheet; then the
front and the water above every drawn-down cell woken, and nothing else, and every other written
cell listed in `Rested` — the one thing besides a visit that appends to it, and the reason the
settle runs *before* the hand-off at the boundary (see finding 6). `SheetSettle` is the trigger —
*K* quiet ticks measured boundary to boundary, inflow being an `Add` or a `Disturb` that touched
wet water (`DisturbedCells`, new) — with the counters the profile's `settle` line prints;
`--settleafter=<ticks>` is the sweep, 2 the default. `--fluidsolve wait|solve|both` is the bench's
analytic arm, and `--spill` gained a third arm with the settle beside the hand-off. Suite 1,006 →
1,032; 23 recipes applied, 22 caught, the one miss an identity (each edge of a sheet is examined from both
of its ends, so testing one direction of a pair is enough).

**Measured, Release, control 1.02–1.05×, `--fluidsolve both` where the wait arm was affordable
(the 96 m wait arms are M8b-1's recorded 41-minute cap); ticks and visits reproduce to the digit
across three runs, the milliseconds within noise:**

```
fixture             bore   wait: ticks to rest   CPU        visits         solve: ticks   CPU       visits       passes   largest sheet   longest pass
chamber 8 m           3        1,915             549 ms     5,574,306          38          95 ms     105,063         37       1,024          10.4 ms
mine 16 m             1       11,130           2,478 ms    26,147,469         136         134 ms     126,142        135         861           5.6 ms
mine 16 m             9       48,153          31,539 ms   238,190,977         683       1,794 ms   4,270,698        682       1,040           9.4 ms
mine 96 m             1        never (cap 100,000, 41 min)                    258       3,983 ms     403,453        257      25,223          72.6 ms
mine 96 m             9        never                                        4,242      17,938 ms   4,564,143      4,241      28,277          28.1 ms
```

Every arm reaches rest and `VerifyQuiescent` says the rest is real; volume exact in all. Five things:

1. **"A handful of ticks" was the estimate and the number is hundreds, because the solve levels
   and the ticks after it are spent on what it cannot do.** A front creeps a cell a tick and water
   falls a cell a tick, by design (wet cells only, no siphons); the solve makes each sheet level
   the moment it is found and the automaton still has to carry the water to the next sheet. What
   collapsed is the visits — 53× on the chamber, 207× and 56× on the 16 m mine — and the world
   time: 93 minutes to one, 401 to six, *never* to two. The bore-9 96 m arm's 4,242 ticks are
   2,073 → 64 active over 4,000 ticks of water trickling through 0.75 m dead ends at the ~1 L a
   tick 2c measured, which no solve reaches.
2. **A pass per boundary is O(sheet) per tick while a front creeps, and on the 96 m mine that is
   the whole of the cost.** Each new front cell makes one pair unsettled and the whole 28,000-cell
   sheet is re-levelled for it: 44.7 M cell writes over 4,241 passes at bore 9, 17 s of the
   arm's 17.9. Net CPU against the wait arm is 1.8× better there and hundreds of times better
   everywhere else. **Lever, named and not built**: re-level only when the sheet's unsettled pairs
   are more than a fraction of it, or top up the front from the interior instead of re-solving
   the sheet. Revisit trigger: a `settle` line whose `ms in all` is a noticeable share of a
   session.
3. **The pass is atomic, and a floor-sized sheet is seconds.** 1–3 µs a cell in Release (72.6 ms
   over 25,223; 28.1 over 28,277), so the played world's ~1.3 million-cell floor is 1.5–4 s a
   pass in Release and ten times that in Debug, in one frame, once the flood stops and *K* ticks
   pass. Not met yet — the world's lakes were drained by the play and its flood on open is now
   91 kL in the first 29 ticks — and **the slice is the lever**: a cursor over sheets like the
   tick's over its batch. Recorded in TODO with that trigger.
4. **On the played world the default does nothing during the flood and zero costs 12 ms a tick.**
   Copies of `default`, headless, Debug, 29 ticks: *K* = 2 fired no pass, because the drain is
   inflow every tick; *K* = 0 fired 29, largest sheet 169 cells, longest pass 12.6 ms, and the
   active set was 21,697 against 21,448 — the early flood is falls and fronts, not sheets. The
   settle is for the end of a flood, which is what it was designed for.
5. **Two fixtures were wrong before the battery, and one was the design's own rule.** The
   packing fixture was five cells each under a litre — at rest already, by `CanSpread` — and the
   skip rule correctly left it; packing is for a sheet that has a move and a share under a litre,
   and a film that is already still stays still. And `DisturbedCells` first counted only cells
   newly queued, so an edit under moving water was not inflow; it counts wet cells touched now.
   The fixtures put water down through `FluidGrid.Fill`, asleep and unlevel — the state diffusion
   leaves for thousands of ticks — so what is awake after a solve is what the solve woke.
6. **The solve and the hand-off had a seam, and the first fix for it was the wrong one.** The
   relay's 50,000 L on the spawn's ground, Debug profile: 10,558 L handed in the window with the
   settle off, 4,178 with it on. The hand-off reads the tick's record of no-move visits, and a
   sheet the solve levels is asleep without one — its interior is level, still, on the ground, and
   would never be the basin's, which is the puddle held for ever the hand-off exists against.
   Waking every written cell for the visit that would list it fixed the sealed-box test and not
   the number (4,117 L), and cost ten times the visits on the 96 m mine (47.8 M against 4.6 M at
   bore 9) — under it the spill still rested sooner (164 ticks against 314) because levelling
   moves water from the resting interior to the moving edge every boundary, which slows the
   first thirty ticks and hastens the end; the visit came a tick late, every tick. The fix that
   stands is the precise one: the solve lists what it levelled and left with no move as resting,
   because a visit would find exactly that, and the settle runs before the hand-off at the
   boundary. Fronts are not listed (they have a move and are woken for it); cells packed to
   nothing are not (nothing rests there). **With it, `--spill` rests in 18 and 29 ticks against
   170 and 314, every litre in the basins, and the relay's whole 50,000 L is the basin's inside
   the profile window with Tier B empty** — against 10,558 L with the settle off. Three recipes
   pin it.

The r6 control is 2601 / 325 / 3446 at 411 with zero passes. How the soak reads with the settle in
front of it is deferred to M8c with the hand-off's own decision.

#### M8b-3b — the share slice, built 2026-09-05

Finding 2's lever, built the day the play fired it. `FluidSimulation.LevelSheets(share)`: a sheet
with a move inside it is re-levelled only when at least one cell in *share* of it is unsettled —
has a transferable pair inside the sheet — or a step stands in it; `SheetSettle` carries the
share, `--settleshare=<n>` is the sweep, **8 by default, an estimate**, zero no threshold and the
control arm. The `settle` line prints sheets left under the share, sheets levelled for a step and
the cells the passes visited, which is the number the share bounds. On the bench `--settleshare <n>`
applies to `--fluid`'s solve arm, `--spill`'s settle arm and **`--lake`, the slice's own arm**: a
chamber block's floor filled level and asleep, with eight litres a tick drawn off ten cells of one
edge — the played world's lake with its loaded edge draining into the unloaded world, which no
draining fixture reproduces. Suite 1,032 → 1,040; ten recipes, eight caught, two findings.

**The TODO's one line — "re-level only when more than one cell in n of it is unsettled" — needed
two things it did not say, and both are the slice:**

1. **The share cannot be read off the flood, because the flood is the cost.** Half of the 275 ms
   the play paid was finding the 162,572-cell sheet, and a rule that floods a sheet in order to
   leave it alone halves the bill and keeps the hitch. The wake rule is what makes the decision
   cheap: a transfer wakes both of its cells and every neighbour of the source, a draw wakes the
   cell and its neighbours, so at a boundary **every cell with a move is an awake cell or beside
   one**. The flood therefore runs from every seed of one Y at once, layer by layer; after layer 1
   it has met every unsettled cell of every sheet and from there the count only grows, so a sheet
   whose count reaches *share* × its unsettled cells is under the share whatever the rest holds
   and its flood is **frozen** there, its frontier kept. Floods that meet are one sheet
   (union-find, smaller into larger); a live flood reaching a frozen one thaws it and the joined
   sheet is judged on the joined counts — and the frontier has to go back into the layer, because
   a discovered cell never examined has no front flag and a raised edge cell would not be woken.
   The bound is exactly as sound as the wake rule. **A caller with hand-picked seeds — M8b-4's
   unload method — must pass a share of zero**: one named cell of a sheet does not neighbour every
   unsettled cell of it.
2. **A step between two levelled puddles is the disturbance that is narrow and not local.** Two
   puddles levelled apart at different heights creep and meet along a line: a few cells of a big
   sheet, under any share, and left alone the band widens as √t — minutes for a lake, the O(n²)
   the settle exists against. The settled cells bordering the disturbance tell a merge from a
   creep: a creeping edge is bordered on one side by an interior that agrees about the level, a
   merge on both sides by interiors that do not. A sheet whose bordering settled cells span more
   than `StepMillilitres` — 32, eight resting pairs' worth, an estimate — is levelled whole
   whatever its share. The border is read from whichever cell of a pair is examined second, so
   the order two neighbours are reached in cannot lose one.

**Measured, Release, control 1.03–1.04×:**

```
--lake: 12 chambers, one 147,456-cell sheet, 8 L a tick off one edge, 60 ticks at the game's interval
share   passes   levelled   under share   visited/pass   written/pass   steady pass   solve ms/tick   automaton visits   awake mean
  0        59        59          0           144,194         8,199        20.5 ms        21.4             44,238            154
  4        59         0         59             1,469             0        0.05 ms         0.19            47,581            812
  8        59         0         59             2,911             0        0.08 ms         0.31            47,581            812
 16        59         0         59             5,801             0        0.15 ms         0.56            47,581            812

--fluid, mine 96 m at bore 9, the solve arm (finding 2's arm)
share   ticks to rest   settle CPU   solve visited   solve written   solve ms   levelled   under share   stepped
  0         4,243         21.2 s        118.6 M         44.8 M        20,386     18,290          0           0
  4         9,901         10.8 s         32.9 M         19.0 M         9,346     17,604     39,521         851
  8         9,896         12.0 s         44.8 M         19.6 M        10,465     21,311     34,173         749
 16        10,023         14.8 s         69.1 M         19.6 M        13,184     21,905     28,331         619

--fluid, chamber 4 at bore 3:  share 0  151 ticks, 569 ms of solve, 2.53 M visited;  share 8  294 ticks, 561 ms, 2.49 M
--spill:  18 and 29 ticks to rest at share 0 and at share 8 alike, every litre the basin's in both
```

Every arm reaches a rest that is real and a volume that is exact. Seven things:

1. **On the play's shape the pass goes from 20 ms to 0.08 ms and the visits from 144,194 to
   2,911** — 250× and 50× — and the cost is linear in the share, as the bound says. The solve
   writes nothing at all there; the automaton takes the edge's levelling with 812 cells awake
   instead of 154, at 7.5% more visits.
2. **On the mine the CPU halves and the ticks to rest double, and the doubled half is free.** The
   sheets left under the share rest by diffusion — 9,900 ticks against 4,243 — but the tail is
   under 64 awake cells for its last 6,000. Rest is a correctness figure and every arm has it;
   the world time is the lever's price, stated.
3. **The chamber floor does not move, and that is the rule working.** A front spreading over dry
   floor thins the whole sheet behind it, which is a wide disturbance, so the 16,548-cell sheet is
   worth a solve at nearly every pass in both arms (907 levelled against 936); 294 ticks to rest
   against 151.
4. **The pass is 0.14 µs a cell in Release, not finding 3's 1–3 µs, and the instrument was the
   fixture both times.** `DugTerrain` bakes a column on its first `CanHold`, evaluating the cave
   field for every voxel in it, and the first flood over a lake pays for every column it reads:
   1,385 ms with none baked, 480 ms with the floor baked and not the ring of wall around it, 90 ms
   with both — which is the pass's own growth — then 20 ms steady. Finding 3's figure was taken
   over a mine whose columns the flow had baked unevenly. By this figure the played world's
   162,572-cell floor is ~23 ms a pass in Release; the play's 275 ms is the Debug build.
5. **The share-0 arm reproduces the M8b-3b table to within the remainder.** 4,243 ticks against
   4,242 and 4,567,464 visits against 4,564,143: the multi-source flood orders a sheet's cells
   differently and the spare millilitres land on other cells. The wait arm is untouched.
6. **The battery's first run put the pre-slice code back.** It restored the subject with a git
   checkout and the subject was uncommitted, so recipe 1 mutated the new solve, the restore
   installed the old one, and nine recipes reported their pattern matched nowhere; the harness
   copies now. Of the ten — no freeze, an exclusive share, no step, no thaw, a one-sided border,
   a union losing its counts, the frozen check dropped, a zero step tolerance, freezing before the
   border is complete, the border read from the wrong cell — eight were caught. The no-thaw recipe
   only after its test asserted the intermediate (every front cell pending after the solve),
   because a lost front flag is woken a tick later by its neighbour's spread and rest-and-verify
   cannot see when; the zero tolerance only after the creep fixture carried the four-millilitre
   tilt rest allows; the frozen check only once `LargestSheet`, which counts whole sheets, was
   pinned, since a frozen sheet fails the share test it was frozen for anyway. **Two are
   findings**: freezing before the border is complete is unreachable under the wake rule, which
   puts every border cell at layer one (the gate at layer three has a layer of slack, kept); and
   recording the unsettled cell's own amount as the border is within four millilitres of the
   identity, because that branch fires only for a pair settled against each other.
7. **Eight is a guess inside a measured range.** Four to sixteen are all negligible on the lake
   and all halve the mine; a smaller share waits for a wider disturbance before solving, a larger
   one floods more per pass. **Revisit trigger: a play that sees a poured mound or a merge line
   sit unlevelled for longer than reads right** — the merge is the step detector's case, the
   mound is the share's — or a `settle` line whose visited cells are a share of the tick's.

### Why this might go first

M6d-1 set the precedent that a later part goes first when it answers an earlier one's question, and
the case here is real: **M8a's cell size is justified by M8b's requirement.** Not chosen, because a
seam with nothing on the far side of it cannot be tested — but **if M8a's measurement comes back
badly, this is the part that says whether the answer is a coarser grid or a smaller active set.**

### M8b-4 — the planning pass, 2026-09-06, before any code

The part as written above — *a `fluid` table, one blob per chunk holding the resting cells, written
when a chunk with settled water unloads, read when it loads, never woken by the load* — was read
against the grid, the pipeline's unload and save path, the store contract and the play's two
findings, and three things it assumed are not so. Each moves the design, and the third splits it.

**1. Water in an unloaded chunk is not deleted today — awake water is.** `FluidGrid` keeps a panel
per chunk and nothing releases it on unload: the streamer's forget does not reach the grid, so a
sleeping cell in a chunk that left the box sits in RAM and is there when the chunk comes back.
What the play's 75% was is the *awake* cells: a cell mid-flow is visited next tick, its chunk is
gone, the terrain adapter answers "solid" for an unloaded voxel (M4a's rule, and the right one for
*flow*), so `Visit` displaces it into neighbours that are unloaded too and drains it. Unloaded is
not rock. For water that is *already there*, the honest answer is neither "solid" nor "air" but
*unknown*, and the honest response to unknown is to stop. **So the walk's fix is a pause, not a
settle**: a visited cell whose chunk is unloaded goes to sleep in place and is remembered as paused;
when the chunk arrives — generated, loaded, or back from the cache, the one `onVoxelsReady` hook
M8b-2c already routes all three through — its paused cells are woken, and so is every held cell on
its six faces and every held cell of a loaded neighbour that touches those faces, because an edit
at the boundary on either side while the chunk was away is the one thing a pause cannot have seen.
`IFluidTerrain` gains `IsLoaded`, asked only on the branch where a visited cell cannot hold, so the
tick's hot path pays nothing; `VerifyQuiescent` skips unloaded cells, because paused is not a move.
No water moves, so the twin oracle — *the same millilitres in the same cells* — holds by
construction for the walk, before any disk exists. This departs from decision 5 and from 3b's
"unload-time method": a settle cannot run over a chunk the terrain calls solid, running it before
the forget needs a hook inside the box sweep the streamer does not have, and a pause is exact where
a settle moves water along paths that may cross into other chunks that are also leaving.
`LevelSheets(seeds, share: 0)` stays as the API; nothing in this part calls it.

**2. The store is a mirror of RAM, not a replacement for it — at first.** With panels never
released, "read when the chunk loads" has nothing to do: at open every stored panel is read into the
grid and is *paused* by finding 1's rule, and the arrival hook wakes it exactly as it wakes a chunk
back from the cache. One mechanism for revival and for load, and the plan's "never woken by the
load itself" becomes "woken by the arrival, like everything else". The blob therefore carries the
awake cells as well as the resting ones — a paused flood resumes when its chunk returns, and a
stored one must too — as a list of cell indices beside the sparse cells. The cost of the mirror
is stated: the played world's mine floor is ~1,254 chunks of lake at 64 KiB a panel, **78 MiB of
panels, over the 64 MiB trigger `FluidGrid` recorded at M8a-2, on the first lake the store meets.**
That is the third slice's number, not the second's.

**3. The three concerns are three slices, ordered by what a defect costs.**

- **M8b-4a — the pause and the arrival.** Core only, no disk: `IsLoaded` on the terrain, the
  paused list, the wake rule at arrival, `paused` on the `M8b:` line beside `displaced`. Oracle:
  M5's lifecycle fuzz for water — random floods in a box whose chunks a test toggles loaded and
  unloaded, with a control arm that never unloads — asserting no cell in an unloaded chunk changes
  while it is unloaded, displaced litres are zero when nothing turns to rock, and with everything
  back `VerifyQuiescent` holds after the settle. The play's number is the oracle's other half:
  `displaced` on the `M8b:` line stays at zero across a walk. **Break-recipes to file before
  writing it**: pause without remembering (paused cells never wake), wake the paused cells and not
  the faces (a boundary dig is missed), wake this side's faces and not the neighbour's, treat
  unloaded as air (a flood leaks into the void), and `VerifyQuiescent` still reading unloaded
  cells (every fuzz round false for the wrong reason).
- **M8b-4b — the store.** `FluidPanelBlob` in `Overburden.Core/Persistence/` on the sky column's
  frame: cx, cy, cz, then the held cells as (index, millilitres) pairs ascending, then the paused
  indices, zlib through `BlobFormat.TryCompress`; the reader validates every index and amount and
  that the paused set is a subset of the held set, the way `ChunkBlob.Read` validates against the
  material table. A `fluid` table beside `columns`, schema 2 → 3 additive (`CREATE TABLE IF NOT
  EXISTS`, the existing stamp), header version unchanged. **The fluid blob rides the chunk batch as
  a third list on `IWorldStore.Write`**, for the reason a column rides it: a chunk's voxels written
  in one transaction and its water in another is, after a crash between them, water inside rock or
  rock over a hole — M6a's argument a second time. Dirtiness is a bit per panel set by the grid's
  one private writer (`FluidGrid.Write`, the same place conservation lives) and cleared at
  submission, `DirtySinceSave`'s rule; the sparse pairs are extracted on the sim thread when the
  panel is queued, because the tick writes the panel while the serial lane would be serialising
  it — the `VoxelsSharedWithJob` problem with no copy-on-write to lean on, answered by copying the
  ~4 KB that matter. Written at unload with the chunk, at autosave and at quit through `SaveAll`'s
  walk; read whole at open. A `loaded` term joins the identity (`held == injected + drawn + loaded
  − removed − displaced − handed`). Oracle: the Tier B twin of `WalkingAwayAndBackLosesNoWater` —
  flood, walk away, walk back, quit, reopen, the same millilitres in the same cells and the same
  cells awake; the blob's own battery in M6a's shape (a hand-decoded write, a hand-built read,
  truncation, both halves of the pair mutated); the contract suite extended for the third list
  and the atomicity; and the wire — a headless `--injectlitres` run that quits and reopens, with
  the `M8b:` line's `held` equal across the quit. **Last and alone, because a defect here is
  permanent data loss.** Measure the blob (a lake chunk against a tunnel chunk, bytes and
  microseconds) on `--blob` before deciding whether the sparse form is worth its second code path
  against the 64 KiB panel through zlib — the estimate is 4 KB against 64 KB raw and near-equal
  compressed, and it is an estimate.
- **M8b-4c — the panel leaves RAM with its chunk.** The 78 MiB. A panel whose chunk is forgotten
  is written and released; a chunk arriving with a stored panel and no panel in RAM submits a
  `LoadFluidJob` on the serial lane, or its `LoadChunkJob` reads both rows in one trip — decide on
  the load bench, since M6's crossover worry was per-query overhead and a second query per chunk
  doubles it. A chunk the cache retains keeps its panel, in the sparse form and under the cache's
  byte cap, so a turnaround costs no fluid load. **Priced before built**: a `--fluid` arm that
  walks the box across the flooded mine and prints panels written, read, bytes and lane
  milliseconds; the revisit trigger it answers is `PanelBytes` past 64 MiB in a play, which the
  first lake will fire.

**What is not in this part, stated.** The settle's unload method (3b) is not called and its share
rule is unchanged. Tier A's window-edge loss — 1,855,295 L "would not fit back" in the same play — is
Tier A's and stays in TODO under M7. The surface's `HeldCells()` scan is untouched; it draws paused
water as it draws sleeping water, which is right.

**The decisions handed back, with the recommendation first:** pause rather than settle on unload
(recommended, for the three reasons under finding 1); three slices in the order above
(recommended — 4a is the play's number and needs no disk, 4b is the data-loss part and goes alone,
4c is the memory lever and wants its own bench arm); the fluid blob on the chunk batch rather than
its own call (recommended, M6a's argument); and the blob's sparse form against the raw panel, which
the bench decides.

### M8b-4a — built, 2026-09-06

**Built as the pass wrote it, and nothing in the design moved.** `IFluidTerrain.IsLoaded` is a
second question with a default of `true` — a terrain with no notion of loading is fully loaded,
which is what the eleven predicate terrains in the tests and benches always meant, so none of them
changed; `VoxelFluidTerrain` answers it with the streamer's own `TryGetMaterial` boolean, so a
chunk still generating is "not loaded" as it is "cannot hold". `Visit` asks it only on the branch
where `CanHold` has already said no, and a cell the world cannot answer for is put in a per-chunk
set and neither moved nor listed as rested. `FluidSimulation.ChunkArrived(ChunkPos)` is the other
half, wired beside the portal tracker on the pipeline's `onVoxelsReady`: the chunk's paused set is
dropped and its cells woken; then, for each of the six faces, every held cell in the chunk's own
face layer and every held cell in the *loaded* neighbour's facing layer, through a new
`FluidGrid.HeldCellsOnFace` that reads one 32 × 32 layer with a fixed local coordinate rather than
scanning the panel. `VerifyQuiescent` skips a held cell that cannot hold *and* is not loaded, and
still fails on one that cannot hold and is. The `M8b:` line and the profile's `fluid` line carry
`paused N cell(s) in M chunk(s), R resumed, F face wake(s)` beside `displaced`.

**Measured in the same headless instrument with the pause taken out** — r4h/2v, 12 m/s, turning
every 10 s for three turns, a 200,000 L relay pour under the player, Debug — which is the play's
walk with the play's number:

```
                                 pause off (pre-4a)     pause on
displaced                            188,866 L               0 L
handed to Tier A                      11,134 L         195,981 L
resumed at an arrival                        0            20,159 cells
face wakes                                 406               406
identities                               exact             exact
```

Nineteen litres in twenty deleted by the walk, with both identities exact in both arms — the
argument for `displaced` being a reported term. With the pause, the last periodic line reads
`0 active, paused 1,629 cell(s) in 6 chunk(s)`: 4,922 L asleep in chunks the player left, the
state 4b will store. Suite 1,040 → 1,053; the r6 control unchanged to the digit with every new
term zero.

**The battery: fourteen recipes, thirteen caught.** The five the pass filed — pause without
remembering, wake the paused and not the chunk's own faces, wake this side's faces and not the
neighbour's, treat unloaded as air, `VerifyQuiescent` still reading unloaded cells — each fail one
named test; the others caught were the pause gone (displace as before), a paused cell listed as
rested, a paused cell re-queued for ever, the arrival forgetting to drop the chunk's set, the face
layer read from the wrong side, the face's axis mislabelled, an unloaded neighbour's cells woken
anyway, and the adapter calling everything loaded. **The miss is an identity**: asking `IsLoaded`
before `CanHold` changes no answer, because *not loaded implies cannot hold* for every terrain, and
"the hot path pays nothing" is a cost claim the suite cannot see. Three things the build found
that the pass did not say:

- **Coordinate zero is a chunk face layer.** The first fixtures put a corridor at y = 0, z = 0 —
  the chunk's −Y and −Z faces — so the face wake reached every cell in it: nine wakes where the
  boundary has two, and the mid-air cell "only the paused list can reach" reachable by the faces.
  Three of the first run's failures were this and none was the code. The fixtures sit at y = 8,
  z = 5. A cell woken beside the source is also visited later in the same tick, so one `Step`
  drops a poured cell two voxels; the test reads where the water is.
- **The chunk's own face wake has one real case, and it is the sheet solve.** Every other change
  a loaded neighbour can make — a pour, a draw, a dig, a flow — wakes across the boundary already,
  and the woken cell is paused and remembered. `LevelSheets` writes a levelled cell beside a
  neighbour that cannot hold without waking across it, because such a neighbour is not a front,
  so the cell across gains a move with nobody to say so. The test removes a litre from the far
  end of a sheet, levels it with the east chunk away, asserts the paused list is empty and the
  east edge has a move, and then that the arrival finds it.
- **Paused is not rested.** `Rested` is the hand-off's input; a paused cell listed there would be
  handed to the basin under ground the world cannot answer for. One assertion, one recipe.

**Not built, stated**: nothing persists — a quit still loses paused water (4b); the panels stay in
RAM (4c); the settle's unload method is not called. The lifecycle fuzz — eighty rounds of pours,
digs, ticks and sheet solves over real terrain across four chunk columns, chunks taken away and
brought back at random against a twin that never loses one — asserts that not a millilitre in an
absent chunk moves, that nothing is displaced in either arm, and that with everything back both
rest and the rest is real. It is the fixture 4b's twin of `WalkingAwayAndBackLosesNoWater` extends
across a quit.

### M8b-4b — built, 2026-09-07

**Built as the pass wrote it — the blob on the sky column's frame, the `fluid` table at schema 3,
the panels riding the chunk batch as a third list on `IWorldStore.Write`, a dirty bit per panel
set by the grid's one writer and cleared at the copy, read whole at open into paused panels,
`loaded` in the identity — and three things the pass did not say.** Matthew's brief for the slice:
*"do not fear data loss of water, like I am not going to cry if I have to wait until it rains
again"*; it was built to the plan's standard anyway, because the tests were cheap and a defect
here is the kind that shows a session later. Suite 1,073 → 1,134.

- **The sparse form against the raw panel — the bench decided, and the plan's crossover was the
  wrong quantity.** The pass said four bytes a pair against two bytes a cell, so pairs below half
  full. `--blob` (Release) says zlib takes the whole panel to 320–690 bytes at *every* shape —
  smaller than the pairs even for a 1% tunnel — and that the pairs' compression cost grows with
  the cell count while the panel's is flat:

  ```
  shape         form       cells  write µs   read µs   stored B
  tunnel, 1%    sparse       316      30.2      18.4        430
  tunnel, 1%    dense        316      82.0     111.9        319
  tunnel, 3%    sparse     1,024      90.8      43.4      2,028
  tunnel, 3%    dense      1,024     154.6     118.0        762
  half full     sparse    16,384   2,379.4     233.7     22,729
  half full     dense     16,384     166.9     106.9        688
  all lake      sparse    32,768   6,418.4     402.9     45,391
  all lake      dense     32,768      82.6     105.8        336
  ```

  An all-lake panel is 6.4 ms as pairs and 0.08 ms as a panel, and the played lake is 1,254 of
  them — eight seconds at a quit against a tenth of one. Both forms ship, the reader takes both,
  and **the writer's threshold is 1,024 held cells, an estimate from the rows above** (sparse ≈
  20 µs + 0.14 µs a cell, dense ≈ 120–170 µs flat); it can move without a format version.
- **A row that lists awake cells has to be rewritten once they rest, or every open wakes them
  again.** The fuzz's first run said so: a cell listed awake at one quit, restored, woken at its
  arrival and found at rest writes nothing, so its panel is clean while its row still says awake,
  and the next open paused 33 cells where 19 were awake. Harmless per open — one visit each —
  and the played lake would pay a million such visits every session for ever. The simulation
  remembers which chunks' rows list awake cells and `TakeAllDirty` rewrites those too, so after
  any save the rows list exactly the cells that are awake now. Caught by the fuzz's exact
  paused-count assertion; one recipe in the battery.
- **A drained panel writes itself empty.** The grid releases a panel when its last drop leaves,
  and a row written while the lake was there would put the lake back at the next open — water
  from nothing, and the identity blind to it because `loaded` rises with `held`. A panel that
  has been snapshotted is remembered on release and written as an empty row; one that came and
  went between two saves has no row and is not.
- **The unview snapshot lists only the paused cells and the save's walk is exact.** Finding a
  leaving chunk's queued cells means walking the whole active set, once per chunk of a shell;
  the queued cells are paused by the next tick and listed by the next `SaveAll`, which walks the
  active set once for every panel — so the row can under-list for at most one autosave interval,
  the window the autosave already accepts.
- **`IFluidPersistence` is a property on the pipeline, not a constructor argument**, because the
  simulation's terrain is the pipeline's own streamer and cannot exist first.

**Measured — the wire, headless, a scratch world, a 50,000 L relay pour on the ground under the
player, quit at 1,800 frames and reopen:**

```
at quit     7,144 L in 1,900 cell(s), 1,894 awake or paused — 4 panels, 9 KiB written
at reopen   restored 7,144 L in 4 panel(s), 1,894 cell(s) stored awake and paused, in 8.1 ms
            identity exact from the first line (loaded 7,144 L); the arrivals resumed 1,894
```

The pour is on the analytic ground, so the hand-off soaks it into the basin across both sessions
(25,957 L handed by the first run's ten-second line, 6,497 L more in the second's) — the basin
table carries Tier A's half of it, as it has since M7d. **The r6 control, invoked verbatim, is
2601 / 325 / 3446 at 411 with every fluid term zero.** A schema-2 world file built by hand — the
played world's shape — opens, keeps its chunks and basins, gains the table and is stamped 3.

**The battery: 26 recipes, 25 caught, 1 identity.** The grid's dirty bit unset, uncleared, or
blind to a released panel; a restore that dirties, that counts as added, that pauses nothing, or
that is not refused over water; the awake walk forgetting any one of its three sources; the save
skipping clean panels with awake cells or rows that list rested ones; the unview queueing no
panel; `SaveAll` taking none; either save path dropping its panels on a failure; the job not
handing the list to the store; the reader skipping the subset check or the capacity check; the
writer's threshold inverted; the dense writer one index off; the in-memory store writing its
first list before checking its third; the schema not stamped. **The identity is the `Distinct`
in the awake-index sort**: both callers already hand over a set, so it is belt and braces, and it
is now labelled as such. Four fixtures were wrong before the code was: a cell at x = 35 in a test
about the west chunk (x = 35 is the east chunk); a "lake at rest" on a corridor that ran on into
the next chunk, which made it a wall of water; a pour of four cells' worth into one cell; and a
coverage guard on the dice — *some* quit must drain a stored panel — that one seed never rolled,
replaced by a deterministic drain-and-quit at the end of every run.

**Not built, stated.** The panels stay in RAM (4c). **The sim-thread copy at a save is unpriced on
the played world**: a snapshot scans the panel and copies its held cells, ~30–60 µs a full panel
in Release by the bench's read figures, so the played lake's 1,254 panels are on the order of
50–75 ms at a quit and an autosave mid-flood copies every panel the flood touched — an estimate,
and Matthew's play is where it gets a number; the lever if it hitches is a dense copy (one 64 KiB
`memcpy`) with the sparse extraction on the lane. The settle's unload method is still not called.

**Played 2026-09-07, on the played world, read off the logs.** The first open at schema 2 read
`loaded 0 L`; the mine flooded; the quit wrote 3,435 panels (12.3 MiB) with `at quit — 2,216,035 L
in 220,333 cell(s), 212,621 awake or paused`; the next open `restored 2,216,035 L in 167 panel(s)
(119 empty row(s)), 212,621 cell(s)` in 114.6 ms, identity exact from its first line, the arrivals
resuming 348,611. Exact to the litre and the cell, and the 3,435-written / 286-read gap is the
drained-panel rule doing its job. **The copy is priced and the estimate above was low by a
build**: in Debug the autosave line read 72–100 ms for 105–197 panels (0.5 ms a panel) and
399 ms on the first autosave after the restore, with every restored cell awake. The `memcpy`
lever stands and is not built; Matthew did not report the hitch. The larger stall the same log
showed is not the store's: the Tier B surface mesh at 144,559 quads took 119.7 ms on the main
thread four times a second while the flood ran — recorded in TODO under M8b.

### M8b-4c — the planning pass, 2026-09-07, before any code

The paragraph above — *a panel whose chunk is forgotten is written and released; a chunk arriving
with a stored panel submits a `LoadFluidJob` on the lane or its `LoadChunkJob` reads both rows; a
cached chunk keeps its panel in the sparse form under the cache's cap* — was read against the grid,
the simulation's pause, the pipeline's three arrival paths and its save queue, the streamer's
cache and the store. Five things it assumed are not so, and the second and third are the whole of
the slice.

**1. After 4a, the tick never writes a panel whose chunk is not loaded, and that is the invariant
4c stands on.** A cell in an absent chunk is paused rather than moved; water cannot be moved *into*
one because `CanHold` is false there; the sheet solve floods holdable cells only; the hand-off
reads rested cells only; and `Wake` refuses a cell that can neither hold nor holds. So a panel
whose chunk has left the box is inert in every direction, and the only things that still read it
are the surface's `HeldCells()` scan, `VerifyQuiescent` (which skips it) and the arrival's face
wake on the *neighbour's* side. Releasing it changes no trajectory. **The surface stops drawing
water in unloaded chunks**, which is right — the terrain there is not drawn either — and it is
where the 119.7 ms rebuild the play found may lose most of its quads: the scan is over every
panel in RAM, and after 4c that is the box, not the world. Measured after, not assumed.

**2. "Written and released at forget" is one tick too early, and the loss is in RAM, not on
disk.** The chunk's *paused* cells are listed by `TakeDirty`; its *queued* cells — a flood front
inside a chunk that has just left — are not, because finding them means walking the active set
per chunk of a shell (4b's reason). With the panel released the instant the chunk goes, those
queued cells are visited next tick, read `AmountAt == 0`, and sleep — not paused, not listed,
forgotten. When the chunk returns, the arrival's face wake covers its six face layers and the
row's awake list covers what was paused, and the interior front stays asleep with a move
available: **a stranded front on every walk across a flood, and `VerifyQuiescent` false after
it.** The 4a lifecycle fuzz extended with eviction will show it in one round. The fix is to let
the tick do the walk it already does: **a forgotten chunk is marked evicting and its panel stays
one tick more**; every one of its queued cells is visited in that tick, finds the chunk absent
and is paused — the 4a rule doing 4c's bookkeeping — and at the boundary that completes the
first tick *begun after the mark*, the panel is snapshotted with the paused list now exact and
released. Cells woken *into* the evicting chunk during that tick by a neighbour's flow are face
cells by construction — nothing else can reach an untracked chunk — and the face wake covers
them at arrival. So the row under-lists nothing the arrival cannot repair, in RAM and across a
quit alike, which is a stronger statement than 4b's "at most one autosave interval". The cost is
a panel lingering half a second (or one sliced tick) after its chunk, reported on the `M8b:` line
as `evicting N panel(s)`; a chunk re-viewed inside that window keeps its panel and needs no load,
which is a *fourth* source, ahead of the other three.

**3. The chunk arrives before its water, and the gap is where a defect would hide.** A revived
chunk is `Generated` on the sim thread the frame it is viewed; a loaded one lands whole from its
job; a generated one likewise — and in all three the voxels are in the streamer before any row
could be read, so a neighbour's water flows into the chunk's air, and the restore then meets a
panel that holds water. `FluidGrid.Restore` throws on exactly that, and **the throw stays: it is
the tripwire for a hole in the gate**, not a case to merge around (merging would double the
water and pass the identity, since `loaded` doubles with it). The gate is: **a chunk whose row
is still to be read is not loaded, to the water.** Not a new concept in the simulation — 4a's
pause already handles a chunk the world cannot answer for, and its fixtures already toggle
chunks loaded and unloaded — but a flag on `StreamedChunk` (`FluidPending`) that the fluid
terrain adapter reads through a streamer method beside `TryGetMaterial`, so the hot path pays
the one dictionary lookup it pays today and nothing more. Light, mesh and physics do not see the
flag; only the water does. The arrival hook splits in two: `onVoxelsReady` stays the portal
tracker's, and a new `onFluidReady` fires `ChunkArrived` — synchronously at voxels-ready for a
chunk with no row, and after the restore lands for one with a row — so the face wake on the
chunk's own side sees the restored cells, and the neighbour's paused cells wake into a chunk
that can now answer. **The restore itself goes through the chunk's job slot**, on the serial
lane, between voxels-ready and mesh, for all three paths: one mechanism, and cancellation,
retention past the unview and revival with the flag intact all come from machinery M2c-2 and
M6b built. Reading the row inside `LoadChunkJob` is rejected because it covers one path of
three — a never-edited chunk whose cave flooded has a row and no chunk blob, and a cache hit
reads no blob at all. What it would have saved is one lane query per *loaded* wet chunk, 6 µs
by M6b-1's measurement against a 0.1 ms read of a lake panel; the bench cannot decide a
structural question and the number it can give is already known. **The stated cost: a wet
chunk's first draw waits one lane trip**, bounded by the loads ahead of it in the lane, which
under a fill is the same backlog its own load sat in; the profile's `sources` line prints
fluid loads and their lane milliseconds so it is read rather than argued.

**4. A load can race a save, and the chunks' answer does not transfer — a better one does.** A
chunk revived before its save lands is kept behind the write by the serial lane and a priority
band, and the pipeline's comment calls the band the weaker half. For panels there is no need to
race at all: the snapshot taken at eviction is a *copy* the pipeline holds until its batch
reports, and it is the newest state of that chunk's water by construction. So the pipeline
keeps every snapshot not yet on disk in a dictionary by chunk — pending and in flight — and a
chunk arriving whose position is in it is restored from memory on the sim thread, no job, no
lane. **Memory, then the evicting set, then the snapshots not yet written, then the store's
index, then nothing**: the chunk's own order with two sources more, and for the chunk's own
reason — consulting them in the wrong order is the only failure here that matters, and the
wrong one restores *the past*: an older row over a newer copy, water reappearing where it had
drained, with the identity exact because `loaded` counts whatever came back. The twin oracle
sees it; the identity cannot. A `_savedFluid` index loaded at open beside `_savedChunks`, claimed
at submission and unclaimed on a failed batch the way the chunk index is. Empty rows stay in the
index and in the table: a load of one restores nothing, clears the flag, and costs 6 µs once;
deleting them at the writer is a tidy for later and is not a format change.

**5. `held` leaves RAM now, so the identity gains a term.** `held == injected + drawn + loaded −
removed − displaced − handed` was exact because nothing left the grid but water; a released panel
takes its millilitres with it, to disk. **`evicted` joins the line: millilitres released with
their chunk**, and `loaded` keeps counting every restore, so `loaded − evicted` is the net on
disk and both stay visible. The at-quit / restored equality the 4b play check read is gone as
an oracle — the open reads an index, not the water — and its replacement is the fuzz's twin and
the walk arm's identity; in play, the `M6:` line prints the index's row count beside the
chunks', which is the number `held` is bounded by.

Two things the eviction must clear and one it must not touch, each a recipe: **`_storedAwake`
must forget an evicted chunk**, or the next `TakeAllDirty` snapshots it with no panel and
writes its row empty — *the lake is deleted by the autosave, silently, with the identity exact*;
**`_emptied` must not gain it** — an unload is not a drain, and the row on disk is the truth;
and the paused set is dropped with the panel, because after release nothing in that chunk can
be woken. `FluidGrid.Evict(chunk)` is the third way a panel goes, beside drain and never, and it
is the one that does not mark the row stale.

**What the cache keeps: nothing.** The plan said the sparse form under the cap. `--blob`
already said the sparse form of a lake panel is 128 KiB as pairs against 64 KiB dense, so the
form the plan named is the largest of the three; dense is the 78 MiB the slice exists to remove
and would evict terrain to hold water; and the compressed blob (336 B) is produced on the lane
after the forget, so caching it means compressing on the sim thread at every forget of a wet
chunk, ~0.4 ms Debug, a shell at a time. **A cache hit therefore costs one fluid load** — a
6 µs query and a 0.1 ms read in Release for a lake panel, the smallest of the four things a hit
was built to skip — and the cache's own invariant, *nothing here is a source of truth*, stays
trivially true. Revisit trigger: the turnaround arm on the played world reads its fluid loads
as a hitch; the lever then is the blob, kept from the save job's own bytes on their way to the
store.

**Tests, and what has to break them.** The 4a lifecycle fuzz gains an in-memory store and a
third arm: chunks toggled out are evicted through `TakeEvicted` → `FluidPanelBlob.Write` →
store, and toggled back through index → `Read` → `Restore` → `ChunkArrived`, against a twin
that never evicts; the same cells hold the same millilitres once everything is back, both rest,
the rest is real, and a quit mid-walk (`TakeAllDirty`, reopen from the store, the rest of the
walk) changes none of that. Both eviction timings are constructed — marked mid-tick and marked
between ticks — and the paused list asserted exact against the queued set at the moment of the
mark, strictly. The pipeline suite gains: a wet chunk meshes only after its restore; a chunk
with no row fires fluid-ready at voxels-ready; a fluid job dropped by an unview is resubmitted
at revival with the flag intact through the cache; a fluid job faulting on a bad row faults
loudly; the in-memory source is consulted before the store (a newer snapshot pending, an older
row on disk, the newer one wins); and an evicting chunk re-viewed needs no load. The recipes:
release at the mark rather than after the tick (stranded front — `VerifyQuiescent`); evict
without clearing `_storedAwake` (the lake deleted at the next save — the twin, across a quit);
evict into `_emptied` (an empty row over a full one — the twin); one terrain question in the
simulation bypassing the gate (water enters a pending chunk — `Restore` throws); `ChunkArrived`
fired before the restore (the chunk's own face wake sees nothing — `VerifyQuiescent`); the flag
cleared at retain (a revived chunk without its water — the twin); the store consulted before
memory (the past restored — the twin); `evicted` dropped from the identity (the line reads *NO*);
the index not unclaimed on a failed save (a load of nothing — the null path's counter).

**Measurement, priced before built.** A bench arm — `--evict` — over `--lake`'s chamber block,
filled and asleep: a box W chunks wide sweeps across it one chunk every K ticks with an in-memory
store, printing per step the panels evicted and restored, bytes written and read, microseconds
per write and read on real lake shapes, peak `PanelBytes` against a control that never evicts,
the visits (which should differ only by the arrival wakes), and at the end both identities and
`VerifyQuiescent` in both arms. Then the profile: the turnaround on a copy of the played world
(`--world=probe-load --turnaround=10`), where the `sources` line gains fluid loads split by
source — memory, pending, store — with their lane milliseconds, and the `M8b:` line carries
`evicted` and `evicting`. The play's number is `PanelBytes` on the `M8b:` line falling from the
world's wet chunks to the box's, and the surface rebuild beside it.

**The decisions handed back, recommendation first:** the restore as a chunk-slot job on the
serial lane for all three arrival paths (recommended; riding `LoadChunkJob` rejected as one path
of three); the panel lingering one tick after the forget (recommended; immediate release
rejected as a stranded front on every walk); nothing in the cache (recommended, with the blob as
the named lever and the turnaround arm as its trigger); memory before the store, with the
pending snapshots as a source (recommended, no alternative that is not the past); `evicted` on
the identity (required); empty rows kept (recommended; the delete is a tidy). Nothing in the
blob or the schema moves.

### M8b-4c — built, 2026-09-07

**Built as the pass wrote it, all six decisions as recommended — Matthew: "all six as
recommended, build it please" — and one thing the pass did not say, found by the played world
on the first probe.** `FluidSimulation.Evict` / `TakeEvicted` / `Reclaim` in
`FluidSimulation.Eviction.cs`, `FluidGrid.Evict` (the third way a panel goes, the one that does
not mark the row stale), `StreamedChunk.FluidPending` read by the fluid terrain through
`IFluidWorld.TryGetMaterialForFluid` on the streamer, `LoadFluidJob` in the chunk's own slot on
the serial lane between voxels-ready and mesh, `_savedFluid` beside `_savedChunks`, the unwritten
snapshots as a source ahead of the store, `onFluidReady` as the water's arrival hook, `evicted`
on the identity, `IWorldStore.LoadFluidIndex` / `ReadFluid(ChunkPos)` beside the whole-table read.
Suite 1,134 → 1,158; 12 recipes, 11 caught, 1 identity. No format or schema change.

- **The portal tracker had to move onto the fluid-ready hook too, and the played world said so
  on the first probe.** The tracker scanned a chunk's holes at voxels-ready; the drain then
  poured Tier A's water into a hole whose chunk was still reading its row, the automaton refused
  it (not loaded, to the water), and `PortalDrain`'s "refused what it had room for" invariant
  threw. The rule is general: *to everything that is water, a chunk arrives when its water is
  home.* Both the tracker and the automaton take `onFluidReady` now, which carries the chunk's
  edit flag for the tracker's sake; `onVoxelsReady` is left to callers that are not water. The
  relay was already safe — `Add` refuses a cell the terrain cannot hold and reports it as still
  to pour.
- **The flag surviving the cache is an identity, and the remark that said it was load-bearing
  was wrong.** A revival is an arrival, and the pipeline re-derives `FluidPending` from its
  sources at every arrival; clearing the flag at retain fails nothing. Left untouched at retain
  anyway, with the reason recorded on the flag.
- **The gate has a pipeline-level test after the battery said the unit test was its only
  catcher.** Water poured into the chunk above one whose row is held on the lane waits on its
  floor for twelve ticks and falls the moment the row lands; the assertion that got there took
  three tries, because the restored water in the wet chunk is mid-flow and drains out once woken,
  so the wet chunk's total is not a monotone quantity — the poured water leaving the chunk above
  is.
- **Two gated-store tests hung the suite for ten minutes before they failed.** A worker blocked
  at a read gate blocks the scheduler's dispose for ever, and a dispose that writes through a
  closed write gate does the same; both gates open in a `finally` now, so a failed assertion
  reads as one.
- **The bench's identity read `*** NO ***` in both arms on its first run**, because the fixture
  fills the grid directly, outside the simulation's ledger; `filled` joins the bench's identity,
  and "evicted" was counting rows written rather than panels released — a panel back on disk
  unchanged writes nothing, and 63 of 109 did.

**Measured — the bench (`--evict`, Release, 1.05× machine), a 6 × 6 × 6 block with four full
layers (147,456 cells, 2.3 ML) and a three-column box walking across it and back, one column
every four ticks:**

```
arm     ticks  visits   peak MiB  evicted  rows written  write µs/row  restored  read µs/panel  tick ms  rest  quiescent  identity
keep       76  69,622       2.4        0             0           0.0         0            0.0    0.436  7,627       yes     exact
evict      76  68,273       1.2      109            46         279.9       111          151.7    0.198  7,573       yes     exact
```

The visits differ by the arrival wakes and nothing else; both rest, and the rest is real — the
stranded front the immediate release would have left is the `quiescent` column. A row is
280 µs to write and 152 µs to read on real lake shapes, against the pass's 0.1 ms estimate for
the read and 120–170 µs for the write.

**Measured — the played world, headless, a copy of the retired chunk-bitten world, r4h/2v at
12 m/s turning every 10 s for 30 s, against the previous commit's build on the same copy and
flags:**

```
                                  M8b-4b (HEAD)          M8b-4c
panels in RAM at the end          364 (22.8 MiB)         81 (5.1 MiB)
fluid rows read off the lane      —                      256 in 121.9 ms (0.48 ms each, Debug)
reclaimed inside the window       —                      40
first draw / near lit             10.12 s / 4.55 s       10.27 s / 6.12 s
viewer ms mean / p99              83.0 / 159.9           61.4 / 130.6
savecost sim total                32.2 ms                98.2 ms (273 panels snapshotted)
identity                          exact                  exact, with evicted
```

The first probe, before the tracker moved, read first draw 27.4 s and threw on every drain; the
20 s was the exception's, not the slice's. **Two costs the run names and this slice does not
pay**: `viewer ms` is 60–80 ms a move on this world in both arms — the box sweep with the
portal tracker's scans and the arrival wakes inside it, older than 4c and recorded in TODO; and
`FluidGrid.Restore` writes a lake panel cell by cell, ~32,768 `Write` calls on the sim thread
inside the apply, which is the p99 frame moving from 33 to 58 ms — the lever is a bulk restore
of the dense form with the totals recomputed once, named and not built.

**The wire, headless, a scratch world, a 200,000 L pour under a viewer turning every 10 s, then
a reopen standing still:**

```
quit 1    at quit — 15,147 L in RAM; the world file holds 16,077 L in 12 row(s) (4 empty)
open 2    loaded 16,074 L from 7 panel(s), 10 rows off the lane in 6.5 ms; identity exact
quit 2    the world file holds 3 L in 12 row(s) (11 empty)
```

The 3 L between the file and the reopen's `loaded` is one row the standing box never covered,
and the second quit's line says exactly that — the file minus what came home is what is still
away. The first session evicted 14 panels and read 11 back, identity exact with `evicted`. The
r6 control, invoked verbatim, is 2601 / 325 / 3446 at 411 with every fluid term zero.

**The play oracle changed.** `M8b: at quit` still prints the water in RAM, and a new line under
it prints what the world file holds, every row summed after the write — the figure the next
open's `loaded` reaches once every wet chunk has come home. "At quit equals restored" is gone
because the open restores nothing; the periodic line's `loaded` and `evicted` are what to read.

---


## M8c — Tier C: groundwater and seepage

The coarse saturation field, integrated forward (decision 3), fed by rain, draining downhill,
seeping into excavations at *saturation × permeability* (decision 4).

**This is what makes mining under a lake frightening, and it is the tier that gives pumps a job that
never ends** — which is DESIGN's stated purpose for it. It is also the least specified of the three
and the one whose cost is named as most likely wrong (decision 3).

**One design question to settle inside it:** seepage is a *rate*, so it can be integrated forward
like rainfall — but the moment it seeps into a space, that space is Tier B and Tier B is ticked.
**The join between a lazily-evaluated source and an eagerly-ticked sink is the shape to get right**,
and it is the same join M7b built between analytic rainfall and a stored accumulator.

### M8c — the planning pass, 2026-09-10

Read the paragraph above against what M8b built, and against MATERIALS-NOTES, SCALE and the seams.
**The paragraph above was written before the seams existed, and every hard question it names is
answered by one of them.** Decision 3's honest problem — *lateral drainage is a coupling with no
per-cell closed form* — dissolves once the water table is Tier A's level rather than a field of its
own; the "join between a lazily-evaluated source and an eagerly-ticked sink" is `PortalDrain`,
already built; decision 5's "Tier C may need storing, decide at M8c" is decided by having nothing to
store. Four findings and then the decisions.

#### Findings

1. **The water table under a lake is the lake, and Tier A already holds it.** DESIGN's Tier C is
   *a coarse per-1 m saturation field, rain-fed, draining downhill*, and the plan's decision 3
   correctly says that field cannot be swept (SCALE: 778,752 cells at r4h/2v, 1.33 million at r6)
   and that its lateral coupling has no closed form. But the quantity that field exists to produce
   — *how saturated is the rock beside this excavation* — is, under a lake, the head of that lake
   over the voxel, and a basin's level is one number Tier A solves, banks per pit and persists.
   **So saturation is derived from Tier A, not stored anywhere**: for a voxel in a basin's
   catchment, `head = level − y`, saturated where positive, dry where not; recent rain enters only
   as a look (M7c's `WetnessAt`) until M9 asks for more. The field DESIGN describes is what a real
   groundwater model would carry; the water table *is* that model's answer, and Tier A computes it
   from volume and terrain already. Fifth outing for *the cheap version is the more accurate one*,
   and it is the one that keeps M8c's oracle exact: seepage draws litres out of a basin that has
   them, through `BasinWater.Remove` (M8b-2a), so `held == rained + injected + received − spilled −
   drawn − seeped` stays an identity with no source that creates water.
2. **Seepage is the portal drain through intact rock, with a permeability factor.** A portal is a
   dug voxel opening onto a voxel above its own seed surface — a hole at voxel resolution, filed
   under the outside's sample. A *seep face* is a dug voxel whose face neighbour is rock, below the
   water table of the column it stands in: the same shape, the same index (per chunk, from the
   three arrival paths, the edit hook and the forget — `PortalTracker`'s wiring), the same "a hole
   is only what was dug" filter, the same draw at the tick boundary into Tier B cells. What differs
   is the rate: a portal passes whatever room the cell under the mouth has; a seep face passes
   `permeability(rock) × head × scale × Δt` litres. **Everything the paragraph above called the
   shape to get right is `PortalDrain`, and it has been in the game since 2026-09-05.**
3. **Nothing new is stored, so there is no header change.** The seep index is derived from the
   chunk's voxels and the seed surface, like the portal index; the table is Tier A's; the only
   state is a sub-cell remainder per seeping chunk, bounded at one cell (15.625 L) and lost on a
   quit — the same bound M7d-1 recorded for a window move's remainder, and stated rather than
   hidden. Decision 5's v4 question is answered: **no**.
4. **The 388% figure is an instrument reading, not a rim overfill, and the term's own comment says
   so.** `WindowCapacityLitres` sums *leaf* capacities — deliberately, "because the composite slabs
   above the leaves are lakes that have already drowned their ridges" — so a merged lake standing
   above its saddles reads over 100% by construction, and the played world's big lake reads 388%.
   The hand-off cannot create litres and `Add` spills at the root. The play question *does a lake
   read higher than its bank* stands on its own; the number is not evidence for it. **Re-read the
   `M7d:` line with that denominator in mind before believing any percentage over 100.**

#### What seeps, exactly

For each loaded, dug chunk: every holdable voxel *v* (dug, or open above the seed surface inside
the chunk — the portal index's holdability) with a face neighbour *r* that is rock, where the water
table over *v*'s column stands above *v*. The table is `LevelAt(basin of SampleOf(v.X, v.Z))`, the
lake's level if the column's basin is part of one, absent otherwise. Head is `table − (v.Y + 0.5)`
in voxels, converted to metres. The face's rate is `perm(material of r) × head_m × seepageScale`,
in litres per second per face, summed per chunk into a *coefficient* that changes only when the
chunk's voxels or the table change. At each tick boundary the chunk's litres are `coefficient × Δt`
plus its remainder; whole cells are drawn from the basin through `Remove` and delivered into Tier B
at the seeping faces **lowest first** — head is greatest there, and a floor spring before a wall
weep is what a real mine does — under a per-tick granule, the rest carried.

Natural caves do not seep, by the portal rule — *a hole is what was dug* — and for the same reason:
a cave under a lake that seeped from the moment the chunk generated would put water everywhere at
once with nothing having been done. Named so the first cave found dry under a lake reads as a
decision.

#### The cost, estimated, and the measurement that replaces it

- **The scan.** A chunk bite is 32,768 voxels × 6 faces; the portal scan of the same bite is
  1.2 ms Release with the seed surface read once per column. The seep scan reads the same and
  adds a material lookup per rock face — *estimate* 1.5–2 ms Release per dug chunk at arrival or
  edit, on the sim thread where the portal scan runs. The played mine is 4,584 dug chunks; at open
  that is ~7–9 s of scans, which is a lot for a frame and nothing for the serial lane the fluid
  restore already uses. **M8c-1's bench takes the number.** Lever if it bites: the coefficient
  stored in the fluid panel row (that *would* be a schema change; not recommended until measured).
- **The tick.** One multiply per seeping chunk, plus deliveries under a granule. Below the noise
  of the fluid tick's own boundary.
- **The rate.** *Estimate*: a 10 × 10 × 3 m room in stone (JSON `permeability` 0.02) under 5 m of
  head has ~160 m² of wall; at `seepageScale` 0.06 L/(m²·m·s) that is ~1 L/s, 25,000 L to stand
  ankle-deep on the floor — seven hours of world time. Dirt at 0.35 is seventeen times faster.
  That is the *nuisance* end the settled decision asks for; `--seepscale=` is the sweep and M8d's
  play picks inside it, as `rainfallScale` was picked.
- **What it does to Tier B, and the trigger it sets.** Seepage is the first source with no end:
  a mine under a lake is fed for as long as the lake has water, and the lake is refilled by rain.
  A resting sheet on the mine floor (not seed ground, so never handed off) is woken by every
  delivery — a small ripple per cell, bounded by the settle's share slice, but *never nothing*.
  **M8c-2 measures the awake count under steady seepage on the played world; if it does not go
  quiet between deliveries, water bodies (WATER-BODIES-NOTES.md) move ahead of M8d**, because a
  body takes a delivery as litres and stays asleep.

#### Split

- **M8c-1 — the seep index and the measurement.** Core only. `SeepIndex` beside `PortalIndex`:
  faces per chunk with material and Y, the coefficient, the per-chunk remainder; the definition
  over every voxel of every scanned chunk as the fuzz's oracle, with the edit path a second copy
  of the geometry (M8b-2b's rule, which is what let its fuzz catch a mutation). `MaterialTable`
  projects `Permeability`, bounded at load and use to [0, 1] with NaN failing closed (M3c). A
  bench arm `--seep`: a room under a lake, swept by head and by material, printing faces, scan ms,
  L/tick and whether the lowest face delivers first.
- **M8c-2 — the draw.** `SeepDrain` beside `PortalDrain` at the tick boundary: litres out of the
  basin through `Remove`, cells into Tier B lowest-first under a granule, `seeped` on both tiers'
  lines and in both identities, `--seepscale=` the lever, the `M8b:` and `M7d:` lines carrying it,
  and a `seep` profile line (faces, chunks seeping, litres in the window and in all, the awake
  count between deliveries). Play: dig under the played lake with a `hand_pick`, then a chunk out,
  and read whether the water arriving reads as *the mine getting wet* or as a tap.
- **M8c-3 — the query M9 reads.** `Saturation.At(voxel)` in Core: head below the table, or zero;
  pure, tested, unread until M9 — so M9's keystone interaction has its input defined by the
  milestone that owns it rather than guessed by the one that consumes it. The damp-wall look is
  M8d's and not here.

#### Decisions, each with a recommendation

1. **Tier C is derived from Tier A's level, not a stored field.** *Recommended.* Cost: no lateral
   groundwater — a tunnel below lake A's level but under a divide into dry basin B's catchment gets
   nothing, though A is twenty metres away. Revisit trigger: that play, exactly. The lever then is
   a radius over neighbouring lakes' levels, still derived, still no store.
2. **Seepage draws from the basin, so the oracle stays exact; no lake, no seepage.** *Recommended.*
   Cost: a wet hillside with no lake does not weep into a tunnel; recent rain is a look only.
   Revisit trigger: M9 wanting saturation from rain alone for support — then rain's contribution
   is a second derived term with its own identity, not a store.
3. **Only dug chunks seep, natural caves do not** — the portal rule. *Recommended*; see above.
4. **Permeability is the JSON scalar, per material, bounded [0, 1], NaN closed.** *Recommended*,
   and it is MATERIALS-NOTES' one urgent item: the field has been in the format unread since M1
   and the trap is a constant shipping green. Solubility stays out (that note's decision 2).
5. **Delivery is lowest-face-first under a granule.** *Recommended.* Cost: a per-chunk sort by Y
   once per scan. The alternative — deliver at the wall where the face is — is the same list
   unsorted and puts a wall weep before a floor spring, which is backwards. **Played 2026-09-11
   at scale ×10: the water comes out of the wall at one point and runs — *"more like a tap"* — and
   Matthew kept it: *"that is how it works in real life"*. In hard rock it does: inflow is through
   joints and fissures, a spring or a running crack, not a sweating wall; sweating is dirt and
   sand. A per-group, faces-take-turns delivery was proposed on the verdict and withdrawn on the
   reply. Revisit trigger: a room dug in dirt reading wrong, whose fix is per material — dirt
   sweats, stone springs — and not a change to this rule.** **And then, the same evening:** *"I
   might take you up on the sweating metric … having to wade through water until you can get
   enough iron"* — so both arms exist, `--seepmode=spring|sweat`, spring the default because it
   was played and judged. Sweat lets each group (one sample, one Y) owe its own litres and hands
   each to the next face in that group with room, round-robin; a submerged group stops, which is
   what a head difference of nothing does and is the one place the arms differ in litres (the
   spring redirects a submerged face's share to the next face with room). Headless over the
   provocation shaft the two are within 5% in litres, visits and awake cells; the played world is
   the A/B, and the per-material version — stone springs, dirt sweats — is what the switch exists
   to inform. **The awake count under each, with nothing else in the world (`--seepawake`,
   2026-09-11): a 10 × 10 × 3 m room at the shipped scale keeps 141 cells awake under a spring
   and 18 under sweat, quiet 87% of ticks; a tunnel-sized room is quiet 97% under either. Sweat is
   the cheaper arm for the automaton — drops rest, a stream runs — and a drip does not force water
   bodies (decision 8's measurement, answered: bodies stay after M8d).**
6. **`seepageScale` ships at the nuisance end, an estimate, swept by flag, picked in M8d's play.**
   *Recommended* — the settled option 3, and `rainfallScale`'s shape.
7. **Nothing is stored; the remainder is lost on a quit, bounded at one cell per chunk.**
   *Recommended*, stated as a bound and asserted.
8. **M8c-1 and M8c-2 are built as one session each; M8c-3 is a morning.** The measurement in
   M8c-2 — the awake count under steady seepage — decides whether water bodies go before M8d.

### M8c-1 — built, 2026-09-11

Matthew's *"M8c as planned"* took all eight. Suite 1,234 → 1,257; 33 break-recipes, 32 caught,
the one miss an identity. `Overburden.Core/Fluid/SeepIndex.cs` holds `SeepFace`, `SeepGroup`,
`IWaterTable` and the index; `ISeepTerrain` is `IFluidTerrain` plus *is this loaded rock, and how
permeable* (`VoxelFluidTerrain` answers both); `MaterialTable.Permeability` is the field's first
reader since M1, refused outside [0, 1] at load and clamped with NaN → 0 at use;
`PortalTracker` takes the seep index as a second, optional index and feeds it the same events
under the same dug filter; `WaterSystem` implements `IWaterTable` as `BasinAtSample` → `LevelAt`.
Nothing is wired into `Main` — that is M8c-2 — and the bench arm is `--seep`. Where the build
departed from the pass, and what the measurement said:

- **The table is not in the definition, and the coefficient is per group, not per face.** The
  pass wrote "a face … where the water table over v's column stands above v" as if the table were
  part of what the index holds; it changes every tick and nothing edits the world when it does,
  so the index holds every candidate face — holdable, at or below its seed surface, against loaded
  rock of non-zero permeability — and `Coefficient(chunk, table)` asks the table at the boundary.
  Faces are summed per (sample, Y) into `SeepGroup`s first, because every face in a sample at one
  Y sees the same head: the bench's 6,613 faces are 345 groups, and a coefficient is 0.025 ms.
  **Sealed rock is not a face at all**, rather than a zero term, so a room lined with something
  impermeable has no faces and the drain never visits it.
- **Filed under the inside's chunk, with a second listing by rock chunk for the faces that cross a
  boundary.** A face whose rock is in the neighbouring chunk has to go when either chunk goes; the
  first draft walked six neighbours' stores on every forget, which at a box sweep's slab of chunks
  against a dug mine's six-thousand-face stores is milliseconds a frame. The cross listing makes a
  forget O(faces removed). The vertical shell — the row under the chunk's floor and the row over
  its roof — is scanned with one face each, and a column whose surface is the row under the floor
  is the one the "surface below the chunk" exit must not skip: a dug surface voxel capped by a
  block placed in the chunk's floor row, which is a test because the battery's S13 needed it.
- **The scan's cost is the read, and the plan's estimate was wrong by three to four times.**
  `--seep`, Release, 1.03x machine: **a chunk of solid rock scans in 4.65 ms, of which 2.65 ms is
  32,768 `CanHold` reads at 77 ns each** through the game's own `VoxelFluidTerrain` over a
  palettised chunk; a bitten-out chunk (uniform air, every voxel seven reads) 3.5 ms; the room's
  eight chunks 55 ms, 6.9 ms each. The portal scan is 1.2 ms because it evaluates a band of Ys; a
  seep face can be anywhere below the surface and there is no geometric exit, so **the floor is
  one holdability read per voxel**. Over the played mine's 4,584 dug chunks that is 16–30 s of
  sim-thread scans at open in Release, ~3× in Debug, against the pass's "7–9 s, nothing for the
  serial lane". **Lever, named and not built: scan a padded copy** — `PaddedChunk`'s 34³ decode is
  ~0.2 ms and the scan over it is array reads, so a chunk would be ~0.5 ms and could run on a
  worker; it would make the portal scan cheaper too. M8c-2 measures the open on the played world
  and takes it if it bites, which the number says it will.
- **The edit path under a chunk bite is 14.4 ms Release**, against the portal hook's 0.9–1.2:
  32,768 hooks with no geometric exit, each a holdability read, six rock reads and six dictionary
  probes. The seed surface is not the cost (16.4 ms with the analytic seed). In the Debug build a
  bite already costs 16.9 ms mean on the sim thread; this would be ~45 ms more, four times a
  second under `Q`. **Lever, named and not built: a bulk edit** — `Carve.Chunk` telling the
  tracker to forget and re-scan the chunk (3.5 ms) instead of 32,768 hooks — which is M8c-2's to
  wire, since the tracker's edit hook is where the bite lands.
- **The rate is measured and the pass's arithmetic was right in shape.** The 10 × 10 × 3 m room in
  the real strata at the shipped 0.06 L/(m²·m·s): **1.76 L/s under 5 m of head, ankle-deep
  (25,000 L) in 3.9 hours of world time**; 0.084 L/s under half a metre, where only the floor and
  the lowest wall faces are below the table, 9.2 L/s under 20 m. All dirt is 17.5× stone — 30.9 L/s
  at 5 m, ankle-deep in thirteen minutes. The pass's "about a litre a second, seven hours" counted
  160 m² of wall; the room has 320 m² of face. **The nuisance end is where it sits, and
  `--seepscale=` is M8c-2's sweep for M8d's play.**
- **The delivery order is the index's, and the bench prints it per chunk**: the first face handed
  out is at the chunk's lowest Y and is a floor wherever the chunk has one. The room's upper
  chunks have no floor faces (their floor row is the room's own air), so their first face is the
  lowest wall — the bench says "floor first: NO" for them and that is the shape, not a defect.
- **The remainder is on the index, bounded at one cell, refused for a chunk with no faces, and
  lost with the chunk** — decision 7 as three assertions. It is a debt against faces, not water:
  the drain owes the chunk sub-litre seepage it has not yet drawn from the basin, so a quit loses
  time and no litre from any ledger.
- **Thirty-three recipes, thirty-two caught, four of them after the fixtures were fixed.** Nobody
  dug beside a sealed block (S5, and S4 — the definition filing sealed rock — is shared with the
  oracle, so only a direct `FacesOf` read can see it); nobody re-opened a voxel above the surface
  beside rock (S2 — a block placed in the lake and dug out again); and the tracker test's only edit
  was the one that triggers a scan, so the hook's own call to the seep index was never load-bearing
  (S26). The fuzz gained digs just above the surface and digs beside a placed block. **S31 —
  `VoxelFluidTerrain.TryGetRock` treating an unloaded voxel as rock — is an identity**: the
  streamer answers an unloaded voxel with air's ID 0 and `false`, so dropping the loaded check
  changes nothing; kept because the answer is by the streamer's convention, not by construction.

### M8c-2 — the planning pass, 2026-09-11, against the built index

Matthew, on being shown the M8c-1 numbers: *"I am tempted to back out of seepage, because with
infinite terrain, it will never settle?"* — and, on the answer, *"Ok lets continue"*. The answer is
in the worklog; the part of it that shaped this pass: "never settles" is the design for the game
(pumps have a job that never ends), is bounded by the loaded box for the engine (nothing seeps
where nobody is), and is a measurement for Tier B (the awake count between deliveries, already
the water-bodies trigger). So the pass adds one mechanism the paragraph above did not have — a
full chunk is skipped — and takes the two levers M8c-1 priced, because the numbers said they
would bite before the first play could.

1. **Delivery is pooled per chunk, lowest face first, into faces below their own sample's table
   only.** *Recommended.* On a divide one sample's faces are under a lake and the next sample's are
   dry rock; a face above its table receives nothing however low it sorts.
2. **A chunk whose eligible faces are all full is skipped until its faces change or sixteen passes
   go by.** *Recommended.* The rate is measured against the lake, not against the water already in
   the mine, so a flooded room under a lake would otherwise walk every face every tick for ever.
   The index's per-chunk revision says when faces changed; the recheck catches water leaving by a
   route that files no face (Alt+R, an eviction). This is where "never settles" costs one flag.
3. **The scan reads a classified padded copy when the terrain can provide one.** *Recommended*, and
   built: 0.56 ms a solid chunk against 2.5 per voxel, ~1 ms a room chunk against 7. The per-voxel
   path stays as the definition's own reading and the fixtures' path; the game's terrain hands
   the block through the streamer's fluid gate, so a chunk whose panel is still landing is nobody's
   rock in the block as it is per voxel.
4. **The edit hook stays per voxel up to 64 edits in a chunk between flushes, then the chunk is
   re-scanned whole at the next flush.** *Recommended*, and built: every face an edit can touch has
   the edited voxel as one half, so one chunk's scan is complete and the neighbours need nothing.
   The index is stale between the burst and the flush, which is why the flush precedes the drain.
5. **`seeped` is its own term on both tiers.** *Recommended.* `WaterSystem.Seep` beside `Draw`;
   Tier B's `injected` is `Added − drawn − seeped`; the fluid identity reads `held == injected +
   drawn + seeped + loaded − removed − displaced − handed − evicted`.
6. **`--seepscale=` ships at 0.06, zero builds neither index nor drain.** *Recommended.* Zero is the
   control arm and the back-out, and costs nothing.
7. **The awake count is read at boundaries where something seeped**, min/mean/max in the window on
   the `seep` line and the `M8b:` line. *Recommended.* The measurement decision 8 named.
8. **A delivery counts as inflow to the settle**, so a drip-fed chunk's puddle levels diffusively
   and never by the solve. *Recommended as is*, recorded as the cost of a source with no end; the
   revisit trigger is a play that reads the puddle as syrup, and the lever is water bodies.
9. **The remainder is a debt, not water**, bounded at one cell whichever way a pass ends.
   *Recommended.* A quit loses time and no litre from any ledger.
10. **A seep face's inside must lie in a dug chunk — the dug rule is on the inside, not on the
    pair.** *Found by the wire check, not recommended in advance.* See below.

### M8c-2 — built, 2026-09-11, and M8c-3 with it

Suite 1,257 → 1,270; 30 break-recipes, 29 caught after three fixtures were fixed (nobody filled the room under a low lake, nobody placed rock at a cave's face from the dug side, no cave sat on a chunk's side face), the miss an identity — the tracker marks a chunk dug before it tells the index, so the as-inside dug check on the edit path can never fail through it. `SeepDrain` beside `PortalDrain`; `WaterSystem.Seep` and
`SeepedLitres`; `IFluidWorld.VoxelsForFluid`, `ISeepTerrain.TryReadNeighbourhood` and
`SeepNeighbourhood` (the block path); `PortalTracker`'s burst and `Flush`; `SeepIndex.IsDug` and
`RevisionOf`; `Saturation.HeadMetresAt` (M8c-3, unread until M9); `--seepscale=` and the `seep`
profile line; the `M8b:` and `M7d:` lines carrying `seeped`. What the build found:

- **The portal rule was too loose for faces, and the first wire check said so.** A 3 × 3 shaft
  bitten under a poured lake filed 110 faces in *two* chunks, and after the shaft's own pit had
  drained one chunk kept seeping — a natural cave in the undug neighbour, touching the bitten
  chunk, indexed as an inside by "every pair touching a dug chunk". Portals get away with that
  rule because a cave is never a pit and a bank needs a cliff; a seep face needs only rock beside
  a hollow, and caves are everywhere underground. The rule is now on the inside's chunk:
  `SeepIndex.IsDug`, bound by the tracker to its edited set, in the definition so the oracle sees
  it. With it the same shaft is 106 faces in one chunk, and the cave stays dry until someone digs
  in its chunk.
- **The pour provocation measures the pit, not the lake.** `--fillbasin` pours into the basin
  under the player and `--breach` bites its pit; on this seed the pit holds 1,000 L at its saddle
  and the rest of a 200,000 L pour spills into the neighbours below it, so the shaft drains the
  pit in a few ticks and the table over it is gone — correct physics (the no-siphons rule in
  Tier A), and nothing to seep from. The `breach` line now prints what the pit's sample maps to
  and holds, which is the denominator behind "0 wet". Two million litres stands over the pit;
  at `--seepscale=100` the shaft seeps 268 L in the window, both identities exact, until the
  portals empty the pit and seeping stops with it.
- **Two fixtures were the portal drain and decision 5 wearing a seepage costume.** A rim tunnel
  meant to test "a face above the table is dry" ended at the bank, opened onto the lake's air,
  and filled through its portal; moved into the rock, it then stayed dry when the lake rose over
  it — because the chunk's rate is pooled and lands lowest first, and the room below it had
  room. The test now asserts that. The dry-face rule is tested by a hollow *lower* than the room
  under a sample that drains elsewhere, found by asking the watershed rather than placed.
- **The tracker's burst caught a test of its own.** A 256-voxel room dug through the tracker is
  past the threshold, so a coefficient read before the flush was over the first 64 edits — a
  third under the true rate, and the test that "measured" it was reading the mechanism it exists
  to defer. `DigRoom` flushes, and says why.
- **The full mark and a lake running dry look the same from the litres and are not.** A pass that
  delivers less than it owed because every face is full marks the chunk; one that delivers less
  because the lake emptied on the way must not, or the rain refilling it waits sixteen passes to
  be noticed. `starved` is set when a face's lake gives less than asked or is gone after a
  delivery; the test pours 300 L over a 4,000 L room and expects the second 300 to arrive.
- **The block scan and the per-voxel scan are held to each other** on the shapes the shell rules
  are about, and the fuzz runs both against the same per-voxel definition with the dug rule bound
  to a set the ops move. The real classifier is tested over a padded copy with a missing
  neighbour, air, water and two rocks.

---

## M8d — The transparent pass, measurement and play

M7d-2 shipped one flat plane per basin, depth-tested but not depth-written, and said in its own
source comment that **M8 owns the real transparent pass**. Tier B water has a surface that is not
flat and not one plane, so this is a genuine renderer part rather than a polish pass.

Split on the seam M5c, M7c and M7d were split on: **what a headless run can verify, and what needs a
person.**

The play questions, provisionally — they are the milestone's actual verdict:

- **Does water finding your excavation read as dread or as an inconvenience?** DESIGN's whole claim
  is that the mine is the antagonist.
- **Is a flooded mine recoverable?** See the open decision below.
- **Does water moving at a slow tick look like water, or like a spreadsheet?** DESIGN's *slower than
  realtime* survived M7's play on a pond rising over minutes. **A pond is the easy case for that
  claim and a waterfall is the hard one**, and there is a stated perceptual floor: *a water surface
  two metres away that jumps once a second is not slow, it is broken.* **Tier A carries an
  interpolable level for exactly this reason; Tier B needs the equivalent decided.**

### M8d — the planning pass, 2026-09-11

Read the paragraph above against what is on the screen now, and against three plays that have
already judged it: *"looks cool going down the stairs … it would be nice if eventually it's a bit
more 3D, like it's a very flat affair right now"* (2026-09-04), the wall weep read as *"more like a
tap"* (today), and the pit filling from the side with a stepped sheet on its floor (today). Four
findings, the split, and the decisions.

#### Findings

1. **Two surfaces exist and both were built to be seen, not looked at.** `WaterSurface` is Tier
   A's: one plane per basin at its solved level, a ring of dry samples drawing the neighbour's
   plane, clipped by the terrain's depth, rebuilt four times a second in ~3 ms. `FluidSurface` is
   Tier B's and calls itself a debug view in its first line: one quad on top of every held cell
   that has nothing above it, at the cell's fill height, no sides, no merging, one flat colour.
   They share `Water.gdshader` — unshaded, a Schlick sheen against the sky colour, `blend_mix`,
   depth tested and never written. "More 3D" is three specific absences in that list: **no vertical
   face where water meets air** (a sheet has no lip, a fall has no front), **no depth in the
   colour** (a puddle and a pool are the same blue), and **no motion between ticks** (a cell's top
   jumps at 2 Hz, which is DESIGN's stated floor met head-on).
2. **The cost is already the largest main-thread term, and it grows with sides.** Slice 3c's play:
   the surface install is 7.8 ms a tick for 20,196 quads, on the main thread, after the walk moved
   to the worker; M8b-4b's play saw 144,559 quads rebuilt in 119.7 ms four times a second under a
   flood. Sides multiply the quads (a wet column at a step adds one to four faces), so M8d cannot
   add them to the debug mesh as it stands. Two levers, both cheap: **merge the tops** — a level
   sheet is runs of equal fill, `WaterSurface` already merges runs on `(basin, level)` and the
   played lake is 162,572 cells of one level — and **build the arrays on the worker**, where the
   walk already runs; the main thread then pays `AddSurfaceFromArrays` and nothing else.
3. **Depth in the colour needs no depth-buffer read.** The walk knows, for every surface cell, how
   many wet cells stand under it in the column; that is a number per vertex, and the shader
   darkens with it. A depth-texture read would give the true optical path but costs a second pass
   and a resolve; the column's own depth is free, worker-side, and right for the two cases the
   play named — a puddle on a floor and a pool in a pit.
4. **The perceptual floor has a cheap answer with one honest hole.** Keep the previous walk's fill
   per surface cell as a second vertex attribute and lerp between the two in the vertex shader on
   a `tick_phase` uniform that runs 0 → 1 over the half second — no remesh between ticks, a
   surface that rises instead of jumping, and the same trick Tier A's interpolable level is. The
   hole: a cell that was dry last tick has no previous height. Rising from the cell's floor is the
   right default for a filling cell; a cell that emptied vanishes at the tick, which is a drop
   leaving and reads as one. Stated rather than hidden.
5. **The waterfall is priced and not built, and it is this part's.** A cell falls one voxel a
   tick — 0.5 m/s — which is the number behind *"water falls as gravity decides"* reading as slow.
   TODO carries Matthew's fall rule (2026-09-07) priced on paper: fall as far as is vacant up to
   *N* cells a tick, stateless, the termination proof intact, ~6 ms a second in Release for a
   thousand-cell front. With sides on the mesh a fall has a front to look at, so the rule and the
   look are one play.
6. **The lake-bank treadmill is the look's problem now.** A hole in a bank drains the lake, fills,
   overflows onto a wet sample and hands straight back — `drawn` and `handed` climbing together
   for ever with 8–10,000 cells awake (2026-09-09). TODO parked it as M8c's or M8d's; M8c did not
   touch it. Of the two shapes named, **the hand-off refusing a cell whose sample's lake stands
   above it** is one condition in `SurfaceHandoff`, keeps every identity (the water simply stays
   Tier B's), and is the rule the seep drain already applies to a face. It belongs here because a
   waterfall into a lake-side hole is exactly that case, and the fall rule would make it faster.

#### Split

- **M8d-1 — the mesh.** Core: a `FluidSurfaceMesher` that turns the walk's surface cells into
  vertex arrays — tops merged along runs of equal fill, a side face wherever a wet cell meets air
  or a lower fill on any of its four sides, per vertex the column depth and the previous fill —
  pure, tested against the debug mesh's quads on the same cells, benched on the played lake's
  shape (`--lake`) and a flood (`--front`). Game: the job emits the arrays, `FluidSurface` installs
  them. Measured: quads and install ms against the debug surface on the same runs; the r6 control
  unchanged. Headless-verifiable throughout.
- **M8d-2 — the look.** The shader: depth darkening, side faces shaded by their normal, the
  tick-phase lerp, and an underwater tint when the eye's voxel holds water (the cheapest "you are
  in it", and the wading precursor). Screenshots with `--injectlitres` on a stepped hillside and on
  the played pit, each arm against the debug surface. Needs a person.
- **M8d-3 — the fall and the bank.** `--fallcells=<n>` (not `--fall`, which is the fall profile),
  stateless, benched on `--front` and `--fluid` for visits and for the front's shape, then the
  hand-off's refusal for a cell under its own lake's level, with the treadmill's scratch-world
  shape as the test. Judged by the waterfall question.
- **M8d-4 — the play.** The three questions above plus *"more 3D"* as the fourth, on the played
  world and on a fresh one, with the log read behind each verdict.

#### Decisions, each with a recommendation

1. **Tier B's mesh gains side faces and merged tops.** *Recommended.* Cost: the mesher is a second
   walk over the surface cells, on the worker; the quad count moves from one per surface cell to
   one per run plus the sides, which on a level sheet is far fewer and on a stair is more.
2. **Depth in the colour comes from the column's wet depth per vertex, not a depth-buffer read.**
   *Recommended.* Cost: a pool over a cave reads as its column, not its optical path; the honest
   version is a second pass, and the trigger for it is a screenshot that looks wrong.
3. **Between ticks the surface lerps from the previous fill on a `tick_phase` uniform.**
   *Recommended*, and it is DESIGN's floor answered the way Tier A answers it. Cost: a cell that
   emptied vanishes at the tick; a cell that appeared rises from its floor.
4. **Tier B's mesh writes depth; Tier A's plane keeps not writing it.** *Recommended.* One mesh
   with tops and sides needs its own faces to sort, and writing depth does it for free; the cost
   is that Tier B water seen through Tier A water shows the nearer only, which is the lake's
   surface over a flooded pit — right more often than not.
5. **The arrays are built on the worker; the main thread only installs.** *Recommended.* Cost:
   the job carries the arrays back, a few hundred KiB a tick at the played lake's size.
6. **An underwater tint when the eye's voxel holds water.** *Recommended*, in M8d-2. Cost: a
   canvas overlay and one `AmountAt` a frame.
7. **The fall rule ships stateless, up to 16 cells a tick, `--fallcells=` the sweep, 1 the
   control.** *Recommended* — 8 m/s from the first tick, no velocity byte, the proof intact —
   with the bench taken before the default is set: if the front's shape on `--front` moves, the
   rule is judged in play at both values; if it does not, 16 ships.
8. **The hand-off refuses a cell whose sample's lake stands above it.** *Recommended.* Cost: a
   spill onto a wet sample under a lake stays Tier B's and levels by the automaton instead of
   vanishing into the basin — which is what it would do.
9. **Water bodies stay after M8d.** *Recommended*, on `--seepawake`'s number: a drip does not
   force them, and M8d-1's merged tops take the surface's share of their argument.
10. **M8d-1 and M8d-3 are one session each; M8d-2 is screenshots and a person; M8d-4 is play.**
    *Recommended.*

---

## The open decision — settled by Matthew, 2026-09-02: option 3

**M8 makes the mine floodable and nothing fights back until M10.**

DESIGN's loop is *rain → water → saturation → collapse*, with **pumps and shoring as the answer**,
and pumps are machines, which is M10. So on the current order there are two whole milestones —
M8 and M9 — in which water can take your excavation and the only counter is to dig a drain or
abandon it. M9 then adds collapse on top, which is *"the mine kills you"*.

Three ways out, and this is a pacing judgement rather than a technical one:

1. **Ship it as planned and let the play judge it.** A mine you can lose is the game DESIGN
   describes, and digging a drain is a real answer that the terrain already supports. Cheapest,
   and consistent with every milestone so far being judged by play.
2. **Bring a hand-placed pump forward into M8d**, as a single placeable that removes litres at a
   fixed rate with no power. Small — it is a `Place` verb and a subtraction — and it makes M8's play
   ask *"is fighting the water fun?"* rather than *"is losing to it tolerable?"*.
3. **Tune the seepage rate low enough that M8 is a nuisance rather than a defeat**, and let M10 turn
   it up when the counter exists. Free, and reversible.

**Settled 2026-09-02: option 3, and machines stay whole at M10.** *"Keep M8 clean, tune seepage
low."* So M8 ships with **no machines at all**, and the seepage rate is set low enough that flooding
is a nuisance rather than a defeat, with M10 turning it up once a counter exists.

Two consequences worth stating, and the second is why this is the better answer:

- **M8d's play question stays *"is losing to the water tolerable?"* rather than becoming *"is
  fighting it fun?"***, and the first is the honest question for a milestone with no counter in it.
  Judging a pump there would have judged M10 early and confounded two changes — the mistake M4c
  avoided by refusing to retune reach in the same pass as the move.
- **It keeps header v4 out of M8.** A machine is the first *entity* this world holds (see
  [M10-NOTES.md](M10-NOTES.md)), so bringing a pump forward would have dragged an entity table and
  a format version into the milestone whose oracle is exact volume conservation. **Two structural
  firsts in one milestone is how a defect hides behind a feature.**

**The rate is the lever, and it is the same shape as `rainfallScale`** — which the play has
already moved once, from x40 to x20. Expect to tune it in M8d and again at M10.

---

## Deliberately not in M8

- **Pumps with power, shoring, machines.** M10, unless the decision above moves one forward.
- **Structural collapse and saturation weakening support.** M9 — though **M8c's saturation field is
  the input M9 consumes**, and that is the recorded keystone interaction between them.
- **Rivers.** A visible stream is moving water and would be permanently awake, which is the worst
  case for decision 1's active set. The recorded guess is that a river is an *analytic channel*
  found at worldgen, not Tier B at all. See TODO.
- **Erosion.** The terrain is a pure function of the seed and non-negotiably so.
- **Water pressure, flow velocity, anything a real fluid solver does.** DESIGN rejected
  Minecraft's model for not conserving volume and never equalising; it did not ask for CFD.
- **Swimming, drowning, buoyancy.** The player does not interact with water as a body at M8.
  **Named because it is the first thing a play-test will try**, and it needs its own decision.
