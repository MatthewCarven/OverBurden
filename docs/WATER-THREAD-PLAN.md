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
