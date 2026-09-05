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
   trigger: the play calling it a leak.
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

### Why this might go first

M6d-1 set the precedent that a later part goes first when it answers an earlier one's question, and
the case here is real: **M8a's cell size is justified by M8b's requirement.** Not chosen, because a
seam with nothing on the far side of it cannot be tested — but **if M8a's measurement comes back
badly, this is the part that says whether the answer is a coarser grid or a smaller active set.**

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
