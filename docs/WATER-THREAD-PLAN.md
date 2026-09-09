# The water thread — near water at 2 Hz, far water as available, and the tick off the sim thread

Planned 2026-09-07 with Matthew, from a question rather than a play finding: *"are we running the
water sim on the main thread like if I were willing to put up with the delay could we do water on
a seperate thread, with locally (say like the surrounding 5x5 chunk grid) running at 2hz while
distant water was done on an as available basis like?"* Placement was his — *"or wherever you
please"* — and it goes **after M8b-4c and before M8c**, because M8c adds seepage to the tick and
should be built on the tick's final home. Not started.

## Where the water runs today, and why

Tier B ticks at 2 Hz on the sim thread, sliced under `--fluidbudget=` at 2 ms a frame. A visit is
0.25 µs in Release and 2.9 µs in the Debug build the game is played in, so the budget buys ~41,000
visits a second there. **A tick does not end until every awake cell has been visited, near or far,
which is why the flood on open — 110,792 cells — took 188 frames a tick.** Near water and far
water are equally slow because nothing orders them.

It is on the sim thread for correctness, not speed. Every worker job here takes an immutable copy
of its inputs: worldgen is pure, a mesh takes a padded copy, a save takes a snapshot. The water
cannot take a copy — its input is every loaded voxel wherever water is, and it runs for ever.
Light had the same shape and stayed on the sim thread (M5c-1). **So the thread's real cost is a
terrain the worker can read, not the thread.**

Matthew's question is two changes, and only one needs the thread.

## Three slices, each useful alone, in this order

### Slice 1 — the order (sim thread, no thread)

At each tick start the awake cells are bucketed by chunk ring from the viewer, near first, and the
frame budget lapses on the far ones. Near water is visited every tick; far water gets what is
left, and its tick simply takes longer — DESIGN's *slower than realtime is accuracy*, applied by
distance.

- **"Near" is the 5×5 chunk column around the viewer, the box's full height.** Vertical is nearly
  free at r6h/2v and water is vertical. `--fluidnear=<rings>` sweeps it; 0 is no order, today's
  behaviour, and the control arm.
- **A far floor: one granule in eight of each frame's budget goes to far cells** whatever the near
  backlog, so a far flood always progresses and can still rest, hand off and settle. An estimate,
  swept by `--fluidfarshare=`.
- **What survives untouched**: conservation (it is in `FluidGrid.Transfer`), termination (per
  move, not per tick), the identity, the hand-off (reads `Rested`), the pause. **What changes**:
  the visit order within a tick, which the sideways split reads through its neighbours' amounts
  — so the trajectory differs from the unordered arm and the sliced-equals-whole test needs both
  arms bucketed the same way. Core's fixtures have no viewer and keep the old order.
- **Cost**: bucketing is O(awake) per tick — ~0.5 ms Release, ~2 ms Debug on the 110,000-cell
  flood, at 2 Hz. Stated, and the profile's `fluidtick` line gains *near visited / far visited /
  far backlog* per tick so the number is read rather than believed.
- **Oracle**: the fuzz with a viewer, asserting rest is real and the identity exact in both arms;
  a fixture with water near and a flood far, asserting every near cell is visited every tick
  while the far backlog drains at the floor's rate and reaches rest.

Judged in play before slice 2 starts: a relay pour beside the player while `--fillbasin` and
`--breach` flood a mine at the edge of the box is the provocation, because silence about
near-versus-far is not evidence.

### Slice 1 — the planning pass, 2026-09-09

Read the slice's paragraph above against `FluidSimulation.Begin/Resume/Complete`, `Main.UpdateFluid`,
`SheetSettle`, `PortalDrain` and `AwakeHeldCells`. **The paragraph names the right feature and the
wrong mechanism**, in the way the adaptive radius's grow rule did: it says the order is a sort and
the far floor is a share of the frame budget, and neither quantity can deliver what the slice is
for. What follows is the corrected mechanism, the four things it touches that the paragraph does
not mention, and the decisions.

#### The sort alone delivers nothing, because a tick does not end until its batch does

`Begin()` swaps the pending set in as the batch; `Resume` walks it; `Complete()` fires only when the
cursor reaches the end. **The frame budget lapses inside a tick, never between one and the next.**
So bucketing the batch near-first puts near cells in the first frame of a tick and then does not
visit them again until the tick ends — which on the flood-on-open is 188 frames later. Near water
would move once every three seconds, not at 2 Hz. The order is necessary and is not sufficient:
**what makes near water tick at 2 Hz is far cells being deferred past the boundary**, so the tick
can complete without them.

That also relocates the far floor. "One granule in eight of each frame's budget" is a *frame*
quantity being asked to settle a *batch* question, and paying it would mean interleaving far visits
between near ones under clock control — which is the one thing that would cost the property M8b-4's
budget slice bought: **a tick run whole lands the same water as a tick run in slices**, asserted
cell for cell over 400 ticks. If admission is a function of the sets and never of the clock, that
test survives verbatim with both arms given the same near centre. **So the far floor is a number of
cells per tick, not a share of a frame.**

#### The mechanism — a change to `Begin()`, and one more list than this section says

**Superseded in part by "The carry costs more than the water it admits" below**, which keeps the
deferred cells out of the swap and names the four readers that then have to know about them. The
partition, the quota and the throttling rule are as written here.

At `Begin()`, after the swap:

1. Partition the batch in place — near cells to the front, far cells after, relative order preserved
   inside each. Near is `max(|dx|, |dz|) <= rings` on `VoxelPos.Chunk` against the viewer's chunk,
   ignoring Y, which is the plan's 5×5 column at `rings = 2`.
2. Admit `nearCount + quota` cells; move the tail back into the fresh `_pending` and leave it in
   `_queued`; truncate the batch.
3. `quota = near == 0 ? everything : max(FarFloorCells, nearCount)`.

The deferred cells are put into `_pending` *before* the tick runs, so this tick's wakes append behind
them and the round-robin is free: a carried cell is admitted before a cell woken since, and a far cell
visited this tick goes to the back. Nothing else in the class moves. `_queued` still holds exactly
"awake and not yet visited this tick", so `AwakeHeldCells`, `ActiveCellCount`, `Complete`'s
`ActiveAfter` and `Settle`'s loop condition are all correct unchanged — **which is the single most
load-bearing consequence of doing it this way**, because `AwakeHeldCells` is what the store writes and
what the eviction lists, and a far backlog held in a fourth list of its own would be a lake that
quietly stops being saved.

**Rule 3 is throttling, not scheduling: with no near water awake, the tick is byte-for-byte today's
tick.** That is worth more than it sounds — it bounds the change's blast radius to exactly the case
the play is about, and it is a property a test can assert. Its cost is a cliff at the first near
cell: one drip beside you drops a 110,000-cell far flood to the floor's rate. Named rather than
smoothed, because every floor has that edge and the alternatives all pay for it with a second
constant.

Why `max(floor, near)` rather than a constant: it makes **a tick at most twice its near cost**, so
the rate the player is watching degrades only with the water the player is standing in, and far
water gets a real share rather than a starvation allowance. The floor, 512 cells — eight granules,
which is where "one in eight" survives, reinterpreted — is an estimate; `--fluidfar=<cells>` is its
sweep and a very large value is a second control arm.

#### Four things the paragraph does not mention

- **The boundary work goes back to 2 Hz, and that is a rate change in the game.** `SheetSettle`,
  `SurfaceHandoff` and `PortalDrain` all run at `begin`, so today a far flood's 188-frame tick throttles
  them to ~0.3 Hz; slice 1 restores the 2 Hz every one of them was designed and priced at — one portal
  voxel at "30 L/s at 2 Hz" is 30 L/s again. **A restoration rather than an inflation**, but it is
  visible: a breached lake drains faster in wall clock than it does today, which makes the known
  treadmill (drawn and handed climbing together, so the settle's quiet counter never fires) turn
  faster too. The number to watch is the settle pass, which was up to 275 ms in Debug on the played
  lake's 162,572-cell sheet; the share threshold is what bounds it, and if it bites the lever is to
  seed the settle from the near set alone.
- **Reclassifying the carry every tick is O(awake) for nothing in the common case.** A carried cell's
  class can only change when the viewer's chunk changes, so classify new wakes always and re-classify
  the carry only when the viewer's chunk moved since the last tick. Exact, and it takes the 110,000-cell
  case from a per-tick partition to a per-move one.
- **Core keeps the old order by construction**, because `rings = 0` is no partition and no viewer is
  the default. Every existing fixture, the whole bench and `Settle()` are the control arm without
  being edited.
- **`Settle()` still terminates** — the floor guarantees progress on the far side — but takes more
  ticks when a viewer is set. Only the new tests set one.

#### Cost, oracles, and what would say it is wrong

**Cost**: the partition is `VoxelPos.Chunk` (three shifts) and a compare per awake cell, once a tick,
on the near set plus new wakes — under 0.1 ms Debug in the ordinary case, ~2 ms Debug on a
110,000-cell reclassification after a viewer move. *Estimates.* The `fluidtick` line gains near
visited / far visited / far backlog, and the periodic `M8b:` line carries the backlog, because the
play is read off the log.

**Oracles**: the sliced-against-whole test run again with `rings = 2` on both arms; a fixture with
water near and a flood far, asserting every near cell is visited every tick while the far backlog
drains at the floor's rate and reaches real rest (`VerifyQuiescent`); the identity exact in both
arms; `AwakeHeldCells` unchanged in count across a viewer move mid-flood, which is the save path and
the one place a deferred cell could be lost; and the break-recipe battery, whose interesting recipes
are the floor removed, the partition unstable, the carry appended behind new wakes, and the deferred
cells dropped from `_queued`.

**Judged in play, and the provocation is in the paragraph above**: a relay pour at your feet while
`--fillbasin` and `--breach` flood a mine at the edge of the box. Silence about near-versus-far from
a play with no far water is not a null.

#### The decisions, handed back

1. **The far quota is cells admitted per tick, not a share of the frame budget.** Recommended: the
   budget-driven form costs the whole/sliced identity, which is a tested property since M8b's tick
   budget slice.
2. **Far is throttled only while near water is awake** — no near cells means today's tick, exactly.
   Recommended, with the cliff at the first near cell named rather than smoothed.
3. **`quota = max(512 cells, near count)`, `--fluidfar=<cells>` sweeping the floor.** Recommended;
   512 is an estimate, the 1× multiplier is the argument that a tick costs at most twice its near set.
4. **`--fluidnear=<rings>`, 0 in code and `start.cmd` passing 2.** Recommended, on the adaptive
   radius's precedent: every recorded figure stays at the unordered arm unless it says otherwise, and
   the play still gets the feature. The counted divisor's flag went into `start.cmd` the same way and
   came out again when the play read the same under both rules.
5. **Near is Chebyshev on chunk X and Z, full height**, as planned.
6. **The boundary work returning to 2 Hz is accepted and measured, not mitigated.** Recommended,
   because 2 Hz is what it was priced at; the settle pass is the figure that would reverse it, and
   seeding the settle from the near set is the lever if it does.
7. **Re-classify the carried far cells only when the viewer's chunk changed.** Recommended; it is
   exact rather than an approximation.

#### The carry costs more than the water it admits — Matthew, 2026-09-09, before any code

*"The cross handling overhead, makes it less than optimal?"* — yes, and the tax has the wrong
shape. Two refinements, both taken before slice 1 is built.

**The bookkeeping scales with the backlog you are ignoring, not with the work you are doing.**
Under the shape above the deferred cells go back into `_pending`, so the next `Begin()` swaps them
into the batch and partitions all of them again to admit the floor's 512. On the flood-on-open's
110,000 cells that is a 1.3 MiB streaming walk — two shifts, two compares and a list write a cell,
~0.5 ms Release and ~2 ms Debug, *estimates* — paid every tick to schedule 512 visits worth 1.5 ms
of Debug work. **The overhead exceeds the far work it admits**, and against a 3,000-cell near set
it is a ~20% tax on the tick. Nothing is wrong; it is simply the one cost that grows with the
number nobody is spending.

**So the carry stays out of the swap.** A third ordered list, `_farCarry`, admitted from its front,
never swapped and never re-walked at a tick boundary. Per tick the classification is then
O(new wakes + admitted) — under 0.02 ms Debug in the case above, a ~50× reduction on the same
fixture — and the O(backlog) walk moves to **once per viewer chunk change**, which is the only event
that can reclassify anything: ~2 ms every 1.1 s walking and every 0.27 s at flycam sprint, under
1% either way.

**What that costs is the "Begin() and nothing else" claim, and the four readers are named rather
than discovered**: `ActiveCellCount` and `Complete`'s `ActiveAfter` (both become the queued set plus
the tick's remainder, which equals today's value when the carry is empty), `Settle`'s loop
condition, and `Pending`, which `LevelSheets` seeds from and which must be both lists in a fixed
order. **`AwakeHeldCells` stays correct untouched**, because the carry is still in `_queued` — that
was the load-bearing part of the earlier shape and it survives. The other three are shallow, and
each gets a break-recipe.

**And the seam is a real artefact, not only a cost.** Round-robin admission puts your own pool's
leading edge — the cells that just crossed the ring, 16–24 m away — in the same queue as a lake
draining a hundred metres off, and behind it. A 10,000 L pour spreads ~18 m in five seconds
(M8b-1), so the edge reaches the seam while you are watching it, and it would stall there for the
length of a distant flood. **So far admission is nearest-first**, bucketed by ring on the same
per-move walk that reclassifies — free, given that walk — with **one part in eight of the far quota
reserved for a rotating tail**, so distance ordering cannot starve the distant.

That is where decision 4's number belongs. *"One granule in eight"* was the right fraction against
the wrong risk: it was protecting far water from near water, which the quota already does; what
actually needs a floor is the far *tail* against the far *front*.

**Two more decisions, both recommended:**

8. **The deferred far cells live in their own ordered list, out of the swap**, with the four
   readers above updated and tested. Recommended: the alternative is a per-tick cost proportional
   to the backlog, which is the one number a throttle exists to stop paying.
9. **Far admission is nearest-first, with one part in eight of the quota reserved for a rotating
   tail.** Recommended: FIFO stalls your own pool's edge behind a distant lake, which is the
   artefact a play would report as the water "sticking" at an invisible line.

#### Slice 1 — built 2026-09-09, unplayed

`FluidSimulation.NearField.cs`: `SetNearField(centre, rings)`, the classification, the quota, the
carry, and `--fluidnear=<rings>` / `--fluidfar=<cells>` in the game. **Rings 0 is the default and
the control arm**, and `start.cmd` passes 2 the way it passes `--adaptive=1`. Suite 1,179 → 1,196 (17 new tests); the Game builds and three headless arms ran. Five things worth keeping.

- **The wire holds and the identity never moved.** `fluidnear 2 ring(s), 346,118 near visit(s)
  against 385,300 far, 3 tick(s) deferred something` on the standard wire check, with
  `held == injected + drawn + loaded − removed − displaced − handed − evicted: exact` on every
  report and `paused` behaving exactly as before. The control arm prints `fluidnear off, 744,714
  near visit(s) against 0 far, 0 deferred` and is the same run it was.
- **The headless instrument can barely exercise the throttle, and that is a fact about the
  provocation rather than about the code.** `--injectlitres` pours *at the player*, so the water is
  near by construction: at 12 m/s the throttle bound on 3 of 59 ticks (the moments the pour
  straddled the ring as the player left it), and at 4 m/s on **none at all** — 1,634,268 near visits
  against 1,992 far, because the player never leaves the pour. **The throttle needs two separated
  bodies of water**, which is exactly the provocation the plan named and which one flag cannot
  produce. `--fillbasin=400000 --breach=7 --injectlitres=100000` is the arm that does it — a lake
  draining into a mine at the basin's pit while the relay pours at your feet — and it reads
  **396,952 near against 281,717 far, 12 of 79 ticks deferring, both identities exact**. Quote that
  invocation, not the wire check, for anything about the order.
- **Two fixtures were wrong before the code was, and both were the same trap one level apart.**
  The first corridor was 247 cells long — M8a's O(n²) case — so every settle assertion failed on the
  fixture, in *both* arms, because levelling to the millilitre effectively never finishes there;
  disjoint pockets settle in tens of ticks. Then a far pour that lands already stacked rests in
  seventeen ticks and takes the quota's cap off before a twenty-tick test has finished looking, so
  the far pocket is wide and the water goes in one end. **Neither failure was about near and far.**
- **Thirteen recipes of fourteen caught, one identity, and the miss the battery found was a
  scenario where both arms agree.** "Settle stops on the pending list alone" passed, because the
  deferred backlog and the wake list only disagree when *nothing was woken* — and a far pour with a
  partial top row keeps waking cells, so the carry drained either way.
  `SettleFinishesTheBacklogWhenNothingElseIsAwake` pours whole rows so every cell rests on its first
  visit, and it fails against the mutation. The identity is "the carry never compacted", which is
  memory hygiene with no observable behaviour. **Named as a gap and not covered: that the carry is
  drained from its *front*** — FIFO against LIFO only bites under a growing backlog and there is no
  cheap boolean for it; the test asserts far-side progress every tick instead.
- **`_queued` is what made this a small change.** The carry lives in the same dedupe set as the wake
  list, so `AwakeHeldCells` — the store's walk and the eviction's — is correct untouched, and the
  four readers that did change are the ones the planning pass named. `ComposeBatch` removes the
  batch's own cells from `_queued` one at a time rather than clearing and re-adding the backlog,
  which also takes the *whole-tick* path off the capacity-proportional `HashSet.Clear` that M8b's
  budget slice recorded as its overshoot lever — for a throttled tick only.

### Slice 2 — the readable terrain (sim thread, priced first)

A holdability bit per voxel, 4 KiB a chunk, built when a chunk's voxels arrive on the pipeline's
`onVoxelsReady`, updated by the `onVoxelChanged` hook the automaton already takes, **copied on
write** so a reader holding the old array is never torn — M3a's rule at 4 KiB instead of a
palette. About 10 MiB at r6.

- **Priced on the sim thread before any thread exists**, because today's hot path is a dictionary
  lookup through the streamer and a material-table read per neighbour, and the bench that
  measured 0.25 µs a visit was already reading a bitset. If the game's visit is markedly dearer
  than the bench's, the bitset pays for itself here and the thread is a separate question.
- **`IsLoaded` becomes a captured set** of chunk positions at tick start, so a chunk leaving
  mid-tick reads as loaded until the boundary, where the pause rule sees it.
- **Oracle**: the bitset equals the material table's answer voxel for voxel across random edits
  and arrivals — the `IsCaveAt`/`GenerateTerrain` shape of test — and the copy-on-write is caught
  by an edit landing mid-enumeration.
- **Cost**: ~10 µs a chunk arrival to build, one 4 KiB clone per edited chunk per tick (a chunk
  bite is one clone, not 32,768).

#### What gets copied is a bit, not a chunk — Matthew, 2026-09-09

*"We have to copy the chunks for the water right?"* **No — one bit per voxel, and slice 1 copies
nothing at all.** Three things the question is worth pinning before slice 2 is built.

**Copying chunks is possible and is not the cheap part.** The water's inputs *are* knowable at a
tick boundary — the chunks holding awake cells and their face neighbours, ~1,254 on the played
world's flood — and M3a's `VoxelsSharedWithJob` is the mechanism, so "mark them shared and let the
next edit clone" would work. What it would not fix is the read. Today's hot path per neighbour is
`_chunks.TryGetValue(voxel.Chunk)` — a dictionary lookup on a three-int key — then a palette decode
in `VoxelChunk.Get`, then `MaterialTable.IsFluid`, and a visit asks it about itself and up to five
neighbours. **The bench that measured 0.25 µs a visit was reading a baked bitset**; the game reads
that. So the bitset is not the price of the thread, it is a sim-thread saving that the thread later
happens to need, which is why slice 2 is priced before any thread exists and stands alone if slice 3
never happens.

**It is not a memory saving, and the honest number should be stated that way.** 32,768 voxels is
4 KiB of bits against a chunk's ~2 KB of palette-compressed voxels (M2b: 4.0 MiB over 2,048
chunks) — **about twice the memory of the thing it summarises**, ~10 MiB at r6. What it buys is an
indexed bit test instead of three indirections, and an array that can be handed to a reader whole.
**Lever, not built: a uniform chunk needs one bit rather than 4 KiB** — the light derive's uniform
closed form (M5c-1) in a second place, and solid rock below the surface is the common case, so the
10 MiB is an upper bound rather than a figure.

**And the open question is M8c, which is the next milestone after this piece.** MATERIALS-NOTES has
seepage reading `permeability`, which is a per-material value; a one-bit field cannot express it,
and baking one immediately before the milestone that needs more than one bit is how a format
becomes a constraint. Two answers, and the recommendation is the second: widen the field to two
bits (impermeable / permeable / holdable), or **keep the bit for flow and let seepage ask the world
at a tick boundary on the sim thread**, since soaking is a boundary-rate process and not a
per-visit question. **Decided 2026-09-09 — one bit, behind a class accessor; see the section below**, on the
grounds that this framing was wrong: the field is a derived cache rather than a format, so
widening it in M8c is a build function and an accessor, with nothing on disk and nothing to
migrate.

#### One bit, behind a widenable accessor — asked for four, decided 2026-09-09

Matthew: *"can we widen to 4bits please"*, then *"actually you decide"*. **One bit, and the reason
is that the alarm I raised above was wrong in its premise.**

**The field is derived and never persisted.** It is rebuilt from a chunk's voxels every time that
chunk arrives — no header version, no mod-visible surface, nothing on disk, nothing a world
remembers. So the usual argument for provisioning a field wide, *that changing it later is
expensive*, **does not apply here at all**: widening it in M8c is a build function and an accessor,
in one assembly, with no migration and no format. "Decide it before slice 2 bakes the field" treated
a cache like a format, and a cache is exactly what this is.

**What the four bits would have cost is the one thing the field exists for.** The reason a baked
field reads at 0.25 µs a visit is locality; 16 KiB a chunk against 4 KiB is **40.6 MiB at r6 against
10.2 MiB** (2,601 tracked chunks) and four times the working set for the same flood — a few dozen
chunks' worth of field in a 2 MiB L2 instead of a few hundred. The per-visit read is identical
either way (a nibble is an index shift and a mask, a bit is a shift and a mask), **so if the wide
field were slower it would be slower for a reason no profile line would name.** Paying that for a
milestone that is notes rather than a plan is speculative generality on the one axis this field is
supposed to win.

**So: one bit, reached only through `FluidField.ClassAt(voxel)` returning an enum**, never through
the bits. Callers ask what class a voxel is; the storage is private; M8c widens it to a nibble by
changing the build and the accessor and nothing else. That is not provisioning, it is one method.

**Two things this keeps from the four-bit pass, because they were right regardless.**

- **The uniform-chunk fast path is worth building rather than naming.** A solid chunk below the
  surface — the commonest chunk there is — is one class instead of 4 KiB, which is M5c-1's uniform
  closed form in a second place, and it makes the 10.2 MiB an upper bound.
- **The field is derived, never authored, and pinned as such.** `MaterialTable` stays the one
  definition of what a material is; a test asserts the field's answer equals the table's for every
  voxel of a random world, the `IsCaveAt`/`GenerateTerrain` shape, third outing. A mod editing a
  material's JSON must move the field, and what makes that safe is that nothing else knows the
  mapping.

11. **The readable terrain is one bit per voxel behind a class accessor, and M8c widens it if
    seepage turns out to need a per-visit answer.** Decided rather than recommended, on the
    grounds that the field is a derived cache and widening it later is two functions. Slice 2's
    measurement prices it against today's dictionary-and-palette path with and without the uniform
    fast path; **the revisit trigger is M8c's seepage wanting permeability inside the tick**, at
    which point the accessor is already the seam.

### Slice 3 — the tick as a job

Each 2 Hz boundary submits one `FluidTickJob` to the M2a scheduler with the captured terrain and
the wake list, and applies its result when it returns — counters, the rested list, the paused
set. **Everything that touches the grid from the sim thread moves from "at the boundary" to
"between jobs"**: pours, digs, arrivals, the hand-off, the settle, the drain, the surface sweep,
`TakeDirty` at unview and `TakeAllDirty` at a save. Six seams built since M8b-1, all mechanical,
each a place a stale read can hide.

- **A scheduler job per tick rather than a dedicated thread.** It gets *every job reports exactly
  one result*, cancellation on quit and the profiler for free; the cost is one worker of eight for
  the length of a tick during a flood, which the `sources` line will show as generation starving.
  **Revisit to a dedicated thread if it does.**
- **Mutations during a tick land at the next boundary**, half a second later, rather than locking
  the grid. A relay pour arriving 0.5 s late is invisible; a lock on the automaton's one mutator
  is not.
- **A slow tick lowers the rate.** The boundary fires at 2 Hz or when the job returns, whichever
  is later; no cell is skipped. `--fluidbudget=` stops meaning anything on this arm and says so.
- **Throughput**: from the budget's ~41,000 visits a second in Debug to a core's ~345,000, so the
  flood on open ticks at ~3 Hz instead of every 188 frames and the roof-open case at ~1 Hz. The
  profile gains a `fluidjob` line — tick wall time on the worker, ticks a second achieved against
  2 nominal, boundaries that waited — and `fluid ms` should collapse to the boundary's cost.
- **Oracle**: the job-run tick equals the sim-thread tick cell for cell from the same inputs (the
  sliced-equals-whole test's shape, one level out); the identity at every boundary; the lifecycle
  fuzz driven through the job path; and **a race battery** — an edit, an unload, a pour, a save
  and a quit during a tick — against a control that does the same at boundaries.

**Stated risk, and it is the reason this is last**: slice 3 is the first *ongoing* state on a
worker. Every other job here is a pure function of its inputs, and the whole test discipline
since M2a assumes it.

## The decisions, taken 2026-09-07 as recommended

1. A separate piece, after M8b-4c and before M8c.
2. Slice 1 alone first, judged in play before slice 2.
3. Near is 5×5 chunks horizontally, the box's full height.
4. The far floor is one granule in eight, an estimate.
5. Slice 2 is a copy-on-write bitset, not the wet chunks marked shared with the job.
6. Slice 3 is a scheduler job per tick, not a dedicated thread.
7. Mutations during a tick land at the next boundary.
8. A slow tick lowers the rate.

## Revisit triggers

- **Build slice 3 when the play reads the flood on open as the thing that hurts rather than the
  thing that is slow.** Slices 1 and 2 stand on their own either way.
- **Drop slice 1's order if the play cannot tell near from far after the provocation above** —
  and only after it, because a null from a play that never had far water is not a null.
- **A dedicated thread** if the `sources` line shows generation starving during a flood.

## Deliberately not in this piece

- Panel lifetime — M8b-4c's, and the reason this waits for it: a worker holding a panel that the
  unload wants to release is the race M3a solved for voxels with `VoxelsSharedWithJob`.
- Seepage (M8c) and the transparent pass (M8d).
- Matthew's fall rule (TODO, beside M8d's waterfall question) — orthogonal, and cheaper on the
  thread than off it, which is one more reason it waits.
