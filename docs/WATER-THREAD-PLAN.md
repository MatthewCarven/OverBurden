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

**Amended 2026-09-19 — the near field has a second kind of viewer: the machines.** M10d's play
put 43 relay outlets on the surface and the player 16 m or more from them underground, and the
order did exactly what this section says it does: the outlets' water was far, admitted at
`max(512, near)` a tick over a 456k-cell backlog, so each outlet's 250 L mouth layer was visited
once in ~260 ticks, the pump behind it stalled `no room` for the rest of the session (4,809 by
the end), and the hand-off — which reads *rested* cells, and a rested cell is a visited one —
moved only while he stood on the surface (*"the water processing cannot keep up with the 'dump'
rate of the pumps"*). A machine reads its own water every boundary and is what "near" means, so
`FluidSimulation.SetNearColumns` takes the chunk columns under every pump's intake box and every
outlet's mouth layer (`MachineTick.NearColumns`, rebuilt with the rings), `IsNear` is the rings
*or* those columns — the arithmetic first, one hash probe for a cell the rings refused — and a
changed set re-classifies the carry at the next boundary exactly as a viewer move does, once per
placement. Under a job the set lands at the next `BeginTick` like the centre. Rings 0 still skips
all of it. Cost: the awake water in those columns every tick — a mound draining or a sheet
creeping into an intake is a few hundred cells a machine — and one bigger tick when the set
changes. The other arm, a far outlet on the analytic ground pouring straight into Tier A as an
unloaded one does, was O(1) and rejected: it empties a reservoir built on the surface through
its floor at 16 m and does nothing for a far intake. Wire: the a-3 oracle with
`--pumpoutlet=48 --fluidnear=2 --speed=0` reads `fluidnear 2 ring(s) + 4 machine column(s),
11,462 near visit(s) against 0 far` where the same run with the hand-over commented out reads
`0 machine column(s), 1,126 near visit(s) against 10,336 far` — the same 1,000 L pumped and
handed in both, moved from the far side to the near. Six recipes, six caught; suite 1,646 →
1,652; the `M8b:` line's `near`/`far` bracket carries `N machine column(s) near`. **The same
log answered the tick-split trigger below**: `1,261 boundary(ies) waited` of 1,375, and it was
not the visit loop (3,514 cells a tick) but the solve on the job's tail flooding 294k cells a
pass from the carry's seeds — the lever is the seeds, in TODO under M10d, not the split.

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

#### Slice 2 priced before it is built — 2026-09-09, and it does not pay for itself

`--terrain` runs the same flood twice over terrain that answers identically: arm one the bench's
baked bitset, arm two the production `VoxelFluidTerrain` over real `VoxelChunk`s in a dictionary —
the game's own path, a hash lookup on a three-int key, a palette decode and a material-table read,
up to six a visit. **The oracle is that both arms visit the same cells and land the same water**, so
the clock is the only thing that differed.

```
                              Release                  Debug
baked bitset                0.228 µs/visit          1.389 µs/visit
chunked, as the game reads  0.334 µs/visit          2.202 µs/visit
                            1.47x                   1.59x
```

400,000 L into a 3 × 3 × 3 block of bitten chunks; 2,544,721 visits and 51,967,461 mL identical in
both arms (Release), 718,928 / 25,399,541 (Debug, 200 ticks). Machine control 1.03x.

**So the plan's question is answered, and the answer is no.** It asked: *"If the game's visit is
markedly dearer than the bench's, the bitset pays for itself here and the thread is a separate
question."* The game's visit **is** dearer — but by **a third of a visit, not a multiple**. The read
is 0.106 µs of a 0.334 µs visit in Release and 0.813 of 2.202 in Debug, and a baked field replaces
it with an indexed read rather than with nothing. **A third of Tier B's visit time, for ~10 MiB and
a copy-on-write invariant, is not a case on its own.**

**What survives is the structural reason, which was always the real one**: slice 2 is *the terrain a
worker can read*. The streamer's dictionary cannot be read off the sim thread, so slice 3 cannot
exist without it. That makes slice 2 an enabler rather than a win, and it inherits slice 3's revisit
trigger instead of standing beside it.

**Recommendation: slice 2 and slice 3 both wait on slice 1's play.** The plan already says to build
slice 3 *"when the play reads the flood on open as the thing that hurts rather than the thing that
is slow"*, and it said slices 1 and 2 *"stand on their own either way"* — that second half is what
this measurement withdraws. If the play says the order is enough, neither is needed; if it says the
flood still hurts, both go together and the 32% comes along with the thread.

**Two things the arm cannot tell you**, said in its own output: the bench's bitset is one column of
bits per (x, z) where slice 2's would be one array per chunk, so the locality differs and this is an
upper bound; and every chunk here is resident and warm, where the game's dictionary holds thousands
and four other systems walk it — so if anything the chunked arm under-states the game.

**And the arm found its own fixture defect before it found anything else.** The first run reported
`ARMS DISAGREE` — 2,544,721 visits against 2,350,355 — because a chunk straddling the fixture's
bounds is created for the part inside it and defaulted to *air*, so its outside was holdable where
the baked arm calls it rock. Chunks are created solid now and the open voxels are written into them.
**Without the identical-trajectory oracle that run would have printed a plausible 1.08x and been
believed.**

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

## Slices 2 and 3 — the planning pass, 2026-09-10

One pass over both, because **slice 3 cannot be built without slice 2** and three of slice 3's
inputs have moved since it was written. Read the two paragraphs above against `FluidSimulation`,
`IFluidTerrain`, `VoxelFluidTerrain`, `ChunkStreamer.TryGetMaterialForFluid`, `ChunkPipeline`'s five
fluid call sites, `Main.UpdateFluid` and `JobPriority`.

### What has moved since the paragraphs were written

- **Slice 2 does not pay for itself on speed** (`--terrain`, 2026-09-09): the game's read is 1.47x
  Release / 1.59x Debug of a baked field's, a third of a visit rather than a multiple. It is the
  enabler and nothing else, so it ships *because* slice 3 is wanted, not on its own merits.
- **The field is lazily-wet, not 10 MiB of view.** The ~10 MiB assumed one field per tracked chunk.
  **A tick reads its own cells and their face neighbours and nothing else**, so the chunks it can
  reach are *the chunks holding awake cells, plus those chunks' six face neighbours* — known at the
  boundary, before the job is submitted. The played world holds 89 wet rows, so with the shell that
  is a few hundred chunks: **1–2 MiB.** *An estimate, from the wet-chunk counts in the play logs.*
- **The surface walk can ride the tick job** rather than needing a snapshot of its own (2026-09-10):
  the job already has exclusive access to the grid, and the walk is 8.85 ms of the 12.09 ms rebuild.
- **The budget's ~8x is measured now**, not derived: 2 ms of a frame buys ~41,000 visits a second
  against a core's ~345,000 in Debug.

### Slice 2, revised — the field is a terrain, so the tick does not change at all

`IFluidTerrain` is one method plus `IsLoaded`, and `VoxelFluidTerrain` is the adapter onto the real
world. **So the field is another adapter over the same interface**: `FluidField` implements
`IFluidTerrain`, is built *by asking a real `IFluidTerrain`* voxel by voxel, and the tick takes it
without knowing the difference. That keeps Core Godot-free with no new interface, keeps every
existing fixture working, and makes the A/B *the same tick against two terrains* — which is exactly
the shape `--terrain` already benches.

- **Scope and lifetime.** A field per chunk, built on the sim thread at a boundary for any chunk in
  the reachable set that has none, kept in a dictionary keyed by chunk, dropped when the chunk's
  panel goes (beside `ForgetInflow`, which the settle's wait already hooks) . Building one is a
  32,768-voxel walk of the real terrain — the plan's ~10 µs, *unmeasured*; a few hundred at once on
  the first flooded boundary is the cost to watch, and it is the same shape as the light derive's
  first fill.
- **Invalidation is copy-on-write, on the hook the automaton already takes.** `onVoxelChanged` →
  `Disturb` → also replace that chunk's field with an edited copy, 4 KiB, so a worker holding the
  old array is never torn. M3a's rule at 4 KiB instead of a palette. **The edit therefore reaches
  the water one tick late**, which is decision 7 already taken.
- **`IsLoaded` becomes the captured set**: the chunk positions whose fields exist. A chunk leaving
  mid-tick reads as loaded until the boundary, where M8b-4a's pause rule sees it — the plan's own
  wording, and it is what makes the pause still correct.
- **Oracle**: the field's answer equals the real terrain's, voxel for voxel, across random edits and
  arrivals (the `IsCaveAt`/`GenerateTerrain` shape, fourth outing); and **a tick against the field
  equals a tick against the live terrain, cell for cell**, which is the sliced-equals-whole test one
  level out and the thing that says the substitution is free.
- **Priced on the sim thread before any thread exists**, still: the tick runs against the field with
  everything else unchanged, and the `fluid ms` line says what it bought.

### Slice 3, revised — the grid becomes the job's, and the reads are the harder half

The plan says *"everything that touches the grid from the sim thread moves from at the boundary to
between jobs"* and names six seams. **There are thirteen, and the ones it does not name are reads.**
Mutations are easy — they queue. A read that races is silent.

```
mutations (queue to the boundary)      reads (must wait for the boundary)
  Add           the relay pour           TakeAllDirty     the autosave and quit
  Remove        Alt+R                    AwakeHeldCells   the report line
  Disturb       the edit hook            TotalMillilitres the report and the HUD
  ChunkArrived  onFluidReady             SurfaceCells     the surface walk
  Restore       a stored panel landing   VerifyQuiescent  refuses mid-tick already
  Evict / Reclaim / TakeEvicted          Rested           the hand-off
  LevelSheets   the settle               Pending          the settle's seeds
  HandOff       the hand-off             Grid.AmountAt    the drain's room test
```

- **The rule, stated once: while a tick job is in flight the grid belongs to the job.** Mutations
  queue and apply at the boundary; reads either wait for the boundary or use the value the last
  boundary published. **The autosave is the sharp one** — a 30 s timer landing mid-tick, and a tick
  under a flood can outlast a frame by hundreds — so `TakeAllDirty` has to *wait*, not race, and a
  quit has to wait for or cancel the tick before writing.
- **Cancelling a tick mid-flight is already safe, and that is M8b's slicing paying out.** The tick
  is a cursor over its batch and the grid is consistent between cell visits, so a cancelled tick
  leaves a legal world with a partly-visited batch — the same state a sliced tick is in every frame
  today. **No new invariant is needed for quit**, which is the one place this could have been
  unrecoverable.
- **The boundary's four passes stay on the sim thread**: the settle, the hand-off, the drain, the
  relay pour. They already run only at a boundary, which is now *between* jobs rather than between
  ticks. Only the surface walk moves *into* the job, because it is the one that is expensive and
  read-only.
- **Priority.** `JobKind` is Mesh 10, Generate/Load 20, Save 30, and `Evaluate` wants a chunk
  position the tick does not have, so the tick builds its priority with `FromBands` directly.
  **Save's argument applies verbatim** — the queue never empties under flight, so anything ranked
  below generation waits for a quiet moment that never comes, and a flood is exactly when that
  matters. But unlike a save the cost is *not* nil: one worker of eight for the length of a tick,
  which is the plan's named revisit trigger for a dedicated thread (`sources` showing generation
  starving).
- **Oracle**: the job-run tick equals the sim-thread tick cell for cell from the same inputs; the
  identity at every boundary; the lifecycle fuzz driven through the job path; and a **race battery**
  — an edit, an unload, a pour, an autosave and a quit each landing mid-tick, against a control that
  does the same at boundaries. **`--fluidbudget` stops meaning anything on this arm and should say
  so on the setup line**, the way `--fluidnear=0` says it is off.

### The decisions

1. **Slice 2 ships and is measured alone before slice 3 starts.** Recommended: it is behaviour-
   preserving by construction (same tick, substituted terrain) and it is the half that can be
   verified without concurrency.
2. **`FluidField` implements `IFluidTerrain` and is built from one.** Recommended: the tick, every
   fixture and every bench arm are untouched, and the A/B is two terrains rather than two code paths.
3. **Scope is the chunks holding awake cells plus their face neighbours, built at the boundary,
   cached per chunk, dropped with the panel.** Recommended; 1–2 MiB is an estimate and the first
   flooded boundary's build cost is the number to take.
4. **One bit, behind the class accessor** — already decided 2026-09-09, unchanged by this pass.
5. **While a tick job is in flight the grid is the job's: mutations queue, reads wait.** Recommended,
   and the autosave is the case to build the gate around rather than the relay pour.
6. **The surface walk moves into the tick job; the settle, hand-off, drain and pour stay on the sim
   thread.** Recommended: it is the only expensive read, and it needs no snapshot inside the job.
7. **The tick job ranks with `Save`, above generation.** Recommended on Save's own starvation
   argument, with the honest difference stated: a tick is not nil, so this is the trigger for a
   dedicated thread if `sources` shows generation starving.
8. **Slice 3 is still gated on the play**, per the plan's revisit trigger — *build it when the play
   reads the flood on open as the thing that hurts rather than the thing that is slow*. Slice 2 is
   not, once slice 3 is wanted.

### Slice 3a — the grid belongs to the tick: begun 2026-09-10, held for a decision, **built the same day**

**Written, working, and stashed rather than committed** on the first pass (`slice-3a-wip`), held for
the decision below; **Matthew's "ok lets continue" took it, and the built section follows this one.**
The first pass's description is kept because the build departed from it in three places.

**Why 3a exists as a slice at all.** A tick has been sliced across frames since M8b's budget slice,
so *a tick is in flight* is a state that already lasts up to tens of frames — 46 of them for a
33,961-cell tick on the played world. **Every seam slice 3b has to survive is therefore already
reachable today, synchronously.** So the rule can be built and exercised with no thread, and 3b
becomes "change what sets the flag" rather than "introduce the flag and the concurrency together".

**The shape, as built.** `FluidSimulation.Ownership.cs`, a new partial:

- `GridOwnedByTick` — today `TickInProgress`, named separately because it is a claim about
  ownership rather than about progress. 3b keeps the name and changes what sets it.
- **Void mutators defer**: `Disturb`, `ChunkArrived`, `Restore`, `Evict` push onto a `List<Deferred>`
  (a struct of kind + voxel + chunk + panel, in arrival order) and return. `ApplyDeferred()` runs
  them at the boundary, **before** the settle, the hand-off and the drain, because those read what
  the edits change.
- **Reads refuse**: `TakeDirty`, `TakeAllDirty`, `TakeEvicted`, `Reclaim` throw the way
  `VerifyQuiescent` does — there is no honest value for a grid half-way through a tick, and a
  caller that has not been taught to wait should be loud rather than silently wrong.
- **`Add`, `Remove`, `HandOff` assert rather than defer**, because each returns how much it moved
  and a deferred call cannot answer.

**Three things it found, and the first is why it is held.**

1. **It reverses a documented, tested M8b property.** `FluidSimulation`'s class doc says *"Water may
   be added, removed or disturbed between slices, and that is the game's case — the relay pours and
   edits land between frames"*, and `WaterAddedBetweenSlicesIsNeitherLostNorLeftAsleep` and
   `RemovingWaterBetweenSlicesWakesWhatWasAboveIt` exist to pin it. Decision 7 (taken 2026-09-07)
   *is* that reversal, so it is authorised — but **14 failures across 6 test names**, and finishing
   means rewriting the contract those tests were written to state. **That is the decision waiting:
   confirm the reversal knowingly rather than have it happen as a side effect.**
2. **`Remove` refusing mid-tick would crash in play.** It is Alt+R, a keypress, which lands on any
   frame. **Main must queue it the way it already queues pours** (`_relayPendingMillilitres`), after
   which `Remove` is boundary-only and the assert is right.
3. **The rule caught a real caller on its first run**, which is the good news: the eviction fuzz —
   and behind it `ChunkPipeline.SubmitSaves` — calls `TakeEvicted` mid-tick. **That is exactly the
   autosave seam the planning pass predicted**, surfaced by a failing test rather than by argument.

**To finish 3a**: rewrite the two `BetweenSlices` tests to assert the new contract (a mutation
between slices is deferred and lands at the boundary, losing nothing), fix the four other failing
names to reach a boundary before calling, rewrite the class doc paragraph, queue Alt+R in `Main`,
call `ApplyDeferred()` at the top of the boundary block in `UpdateFluid`, and teach
`ChunkPipeline.SubmitSaves` to skip the fluid half while `GridOwnedByTick`. Then the break battery.

**Then 3b** (the job) and **3c** (the race battery and the measurement), per the pass above.

### Slice 3a — built 2026-09-10

The recipe above was followed and departed from three times, each because reading the pipeline
against the stash found something the stash had not. Suite 1,208 → 1,216; 24 break-recipes, 22
caught, the two misses named in the worklog entry.

**Departure 1 — the deferred list is applied by the tick's own completion, not by the caller at
the next boundary.** The recipe said "call `ApplyDeferred()` at the top of the boundary block in
`UpdateFluid`". Between a tick ending and the next beginning there is a window — up to half a
second — in which the grid is nobody's and a mutation lands directly, so a `Reclaim` there could
run *before* the `Evict` that had been deferred ahead of it, and the panel would leave after the
chunk was back. Arrival order is the whole point of a queue. `Complete()` now drains the list the
moment the batch is cleared and `Ticks` has advanced, before the result is built; `ApplyDeferred`
is private; and *the grid is not the tick's* and *nothing is deferred* are one state. **The
`Evict` due-tick clause fell out of that**: a mark deferred mid-tick applies after `Ticks++`, which
is the "one tick later" M8b-4c's linger rule wanted and `Evict` had been computing by hand
(`TickInProgress ? Ticks + 2 : Ticks + 1`); the clause is gone and the strict eviction test still
passes to the tick. 3b keeps the shape: the sim thread collects the job's result and drains the
list in the same call, before the flag clears to anyone.

**Departure 2 — to the water, a wet chunk arrives at a boundary, and the pipeline is what makes
that true.** Deferring `Restore` alone is not safe: the streamer's "loaded to the water" gate is
`StreamedChunk.FluidPending`, which `ArriveFluid` cleared at arrival, so a visit later in the same
tick could flow water into a chunk whose stored water lands at the boundary — where `Restore`
refuses a chunk that already holds some, by a rule M8b-4c pinned on purpose. So the pipeline
**parks** an arrival while the grid is the tick's: the flag stays set, the chunk (with its row, if
the lane had already read one) goes into `_fluidWaiting`, the pump refuses to submit a second read
for it, and `ReleaseFluidArrivals` — a new step in `Update`, between `ApplyResults` and the light —
runs the arrival on the first frame after the boundary, dropping any parked chunk nobody views any
more. `CollectEvicted` is skipped while owned for the same reason, and `SaveAll` refuses up front
with nothing queued or claimed. `IFluidPersistence` gained `GridOwnedByTick` and `MayHoldWater`.

**Departure 3 — a dry chunk does not wait, and the first battery run said why the line is where
it is.** Parking *every* arrival would hold every mesh for the length of a tick, which under a
flood is the world not drawing. So only a chunk with water somewhere waits: a panel in RAM, a
mark for eviction (with no panel, owing a row — or **a mark still in the deferred queue**, which is
every chunk that left inside this tick), an unwritten snapshot, or a row in the store. Anything
else is ready at once with the tick still in flight. **The first battery run caught the dry branch
falling through to `Reclaim`, which refuses mid-tick** — three tests threw before any recipe was
applied — and the fix had a second half: a chunk with a mark standing must not be made ready
either, because water could flow into it before the boundary's collection, which would then evict
a *viewed* chunk's panel. `MayHoldWater` is the one question the pipeline asks the simulation
mid-tick, honest in 3a because a tick creates a panel only in a chunk loaded to the water; **in 3b
it is a read racing the worker's panel creation and wants a boundary-published set.**

**Consequences stated as costs.** A chunk that leaves and comes back inside one tick waits for the
boundary even when dry — a turnaround inside half a second, rare in play and bounded by the tick.
Deferred `ChunkArrived` calls land in one batch at completion — cheap, because
`HeldCellsOnFace` early-outs on a chunk with no panel. The quit finishes the tick in flight
synchronously and prints what that cost; a cancel would need to return the unvisited half of the
batch to the pending set, which nothing does yet, and at a quit the cost is one tick's remainder.
The autosave is postponed a frame at a time while a tick is in flight, with the timer left where it
is. And what is still readable mid-tick — `AwakeHeldCells`, `TotalMillilitres`, `Rested`,
`SurfaceCells`, the `Grid` — is listed in `FluidSimulation.Ownership.cs` as 3b's to gate.

**What the M8b: line prints**: `N edit(s) deferred to a boundary, M arrival(s) parked` beside
`reclaimed`; and `M8b: finished the tick in flight before the quit — N cell(s) in T ms` when a quit
found one.

**Then 3b** (the job — change what sets `GridOwnedByTick`, drain the list where the result is
collected, gate the reads above, publish the wet-chunk set) and **3c** (the race battery and the
measurement).

### Slice 3b — the tick as a job: the planning pass, 2026-09-10

Read against the built 3a, `JobScheduler`, `JobPriority`, `ChunkPipeline.Apply`, `FluidField`,
`FluidSurface` and every `_terrain.` site in `FluidSimulation`. Four things the pass found that
the two paragraphs above do not say, then the decisions.

**1. The split is Begin / Run / Complete, and 3a already put the seam in the right place.**
`TickInProgress` is `_batch.Count > 0`, true from `Begin()` (the batch composed) to `Complete()`
(the batch cleared, the deferred list drained). Put `Begin` at submission and `Complete` at
collection, both on the sim thread, and **`GridOwnedByTick => TickInProgress` stays literally
true across the job's whole life** — 3a's "3b changes what sets it" turns out to be *nothing*
changes what sets it; what changes is who runs the visits in between. The worker runs the visit
loop only (`Resume`'s body, in granules, polling cancellation) plus the surface walk; it never
touches `_pending`'s composition, `Ticks`, the deferred list, the near field or the boundary
passes. So the state the worker writes is: cell amounts and panel creation in `_grid`, `_cursor`,
`_rested`, `_paused`/`_pausedCells`, `_queued` (wakes), `_inflowTick`, and counters — and every
one of those is read on the sim thread only at a boundary today, with three exceptions below.

**2. The terrain the worker reads is a captured view, and a miss inside it is a refusal.** The
live `FluidField` falls through to the streamer on a miss and its dictionary is mutated by
`Invalidate` (the edit hook, any frame) and `Sync` (the boundary). A worker cannot read either. So
`FluidField.Capture()` at the boundary returns an immutable view — a copy of the dictionary's
references, hundreds of entries, microseconds; the arrays are copy-on-write already so the view is
never torn — and in the view **`Unknown` answers `CanHold` false and `IsLoaded` false**, counted.
That is what slice 2's `TickMisses` was the gate for: 0 on a 40 s flood, so a refusal inside a
tick costs nothing measured, and it is right by construction — a chunk with no field at the
boundary was not loaded then (`Build` declines an unloaded chunk), so its cells pause exactly as
M8b-4a's rule wants and `ChunkArrived` wakes them. The visit path takes the view through a
per-tick `_visitTerrain`; **every boundary call keeps the live terrain** — `Add`, the sheet
solve, `ChunkArrived`'s face wake, `Wake`'s refusal — and the build classifies each of the sixteen
`_terrain.` sites. **Consequence: the job arm requires the field.** `--fluidfield=0` with the job
on is the sync arm with a line saying so, not a crash and not a worker reading the streamer.

**3. Three reads still cross the tick, and each gets a different answer.**
- **The report line and the HUD** read `TotalMillilitres`, `HeldCellCount`, `PanelCount`,
  `PanelBytes`, `AwakeHeldCells().Count()`, `PausedCellCount` and the identity terms every frame.
  Longs are atomic on x64 but a mid-trajectory total is not a boundary value and `FluidIdentity()`
  could read `*** NO ***` across a `Displace` and its counter. **A `Published` snapshot, taken at
  every collection**, is what both read; the M8b: line stops reading the grid at all.
- **`AwakeHeldCells()`, `Grid`, `Step()`/`Resume()`** refuse while a job is in flight, the way
  `TakeAllDirty` does — a `_jobInFlight` flag beside the batch. The bench and the suite keep the
  sync path, which is the control arm and the oracle's other half.
- **`MayHoldWater`** reads `_grid.HasPanel`, and the worker creates panels. Mid-job it reads
  **the published panel set ∪ the captured reachable set** instead — the worker can create a panel
  only inside the captured set — which over-approximates by chunks that are loaded already and
  therefore not arriving.

**4. A cancelled tick has to give its unvisited cells back, so 3a's "quit finishes the tick" lever
gets built here rather than named.** `Complete()` clears the batch; a tick that stopped at the
cursor would lose every awake cell past it — asleep, unvisited, a stranded front on every quit. So
collection of a `Dropped` result wakes `_batch[cursor..]` into the pending set before completing
(`Wake` refuses nothing that holds), and the row lists them awake, exact. The quit then cancels
the job's token and pumps results until that one result is applied — `DrainStoreLane`'s shape,
generalised — and `SaveAll` finds the grid free. `M8b: cancelled the tick in flight before the
quit — N cell(s) returned to the queue` replaces 3a's line. **The pipeline's `Apply` is
chunk-centric** — it would decrement `OutstandingJobs` on whatever chunk a tick's nominal position
named — so the tick is the first *detached* job: an `IDetachedJob` with an `OnResult` the
pipeline routes to ahead of the chunk lookup, submitted through `SubmitDetached` so `JobsInFlight`
stays honest, and `IsQuiescent` counts it.

**What it costs, stated.** One worker of eight for the tick's length under a flood — the
`sources` line's generation figure against the sync arm is the number, and generation starving is
the plan's own trigger for a dedicated thread. The surface updates once a tick (2 Hz) rather than
four times a second, and freezes for the length of a long tick — which is when the water is not
moving on screen anyway. The `Published` snapshot is a second copy of six numbers, report-only.
`--fluidbudget=` means nothing on the job arm and the setup line says so. The autosave can now be
postponed for the length of a multi-second tick rather than tens of frames.

**What it should buy, as a prediction to be measured rather than believed**: `fluid ms` per frame
collapses to the boundary passes (the settle's pass, the hand-off, the drain, the pour and the
capture — the settle's 19.5 ms longest pass is the new worst frame, and it was already there);
visits a second from the budget's ~41,000 to a core's ~345,000 in Debug, so the flood-on-open
ticks at ~3 Hz instead of every 188 frames; the surface walk's 8.85 ms leaves the main thread. A
`fluidjob` profile line — tick wall time on the worker mean/max, ticks a second achieved against
2 nominal, boundaries that waited for a job, the walk's ms — and `fluid ms` kept, now meaning the
boundary's cost.

**Oracle.** (a) The job-run tick equals the sync tick cell for cell from the same inputs — a twin
driven through `Begin`/worker-loop/`Complete` on the test thread against `Step()`, with the same
edits deferred at the same boundaries; (b) the same with a real scheduler and real threads through
the `Session` harness — identity exact, `displaced 0`, rest real; (c) a cancel after *k* granules
returns exactly the unvisited cells and conserves; (d) the refusals mid-job; (e) the captured view's
refusal and its count; (f) `MayHoldWater` over-approximates mid-job and never under-approximates;
(g) the lifecycle fuzzes driven through the job path. **3c** is the race battery on real threads —
an edit, an unload, a pour, a save and a quit landing mid-job — and the measurement.

#### The decisions

1. **`FluidTickJob` is a detached job on the shared scheduler**, routed by the pipeline through
   `IDetachedJob.OnResult` ahead of the chunk lookup, submitted by `SubmitDetached`, waited on by a
   generalised `DrainStoreLane`. Recommended: it gets exactly-one-result, cancellation and the
   profiler for free, and the alternative — a second scheduler or a thread — forfeits all three.
2. **The worker reads a captured `FluidField` view in which `Unknown` is false for both
   questions, and the job arm requires the field** (`--fluidfield=0` ⇒ the sync arm, printed).
   Recommended: it is what `TickMisses == 0` was measured for.
3. **`Begin` at submission and `Complete` at collection, both on the sim thread; the worker runs
   the visit loop and the surface walk and nothing else; `GridOwnedByTick` stays
   `TickInProgress`.** Recommended: the smallest change to a state machine 3a just proved.
4. **Mid-job reads: the report line and HUD read a `Published` snapshot; `Grid`,
   `AwakeHeldCells` and `Step` refuse; `MayHoldWater` reads published panels ∪ the captured
   set.** Recommended.
5. **A cancelled tick returns its unvisited cells to the queue, and the quit cancels rather than
   finishes.** Recommended, and it is 3a's named lever built now because a quit must not sit
   through a multi-second tick.
6. **A faulted tick freezes the water for the session, printed once.** Recommended over falling
   back to the sync arm, which would rethrow the same deterministic fault on the main thread
   every boundary and take the game with it; a frozen lake beside a reported exception is the
   pipeline's visible-hole rule.
7. **The surface walk runs at the end of the job**, cells handed back, geometry built at
   collection; 2 Hz. Recommended: fresher than walking first, and the only boundary pass that
   changes *which* cells are surface is packing, one boundary stale and invisible.
8. **`JobKind.FluidTick = 30`, Save's band, position the viewer's chunk.** Decision 7 of the
   earlier pass, restated because the queue evaluates priority from `Kind`, not from a value the
   job hands it.
9. **Off by default in the code, on in `start.cmd` as `--fluidjob=1`** — slice 1's shape, so a
   profile without the flag stays comparable with every recorded figure and the play gets the arm.
   Recommended.

### Slice 3b — built 2026-09-10, all nine as recommended

Suite 1,216 → 1,234; 25 break-recipes, 25 caught — two on the second run, both fixtures (the twin
test's disturbances were aimed at voxels the falling sheet had not reached and woke nothing on
either arm; the surface walk had no assertion). Unplayed; `start.cmd` passes `--fluidjob=1`.

**What was built, against the decisions.** `FluidSimulation.Job.cs`: `BeginTick(captured,
position, walkSurface)` composes the batch on the sim thread and returns a `FluidTickJob`;
`RunOnWorker` is the visit loop in granules, polling cancellation between them, then the surface
walk; `CollectTick` gives a tick that did not complete its unvisited cells back, completes —
which drains 3a's deferred list — and publishes. `FluidTickJob : IDetachedJob`, `JobKind.FluidTick
= 30`. `FluidFieldView` is `FluidField.Capture()`: a copy of references over copy-on-write arrays,
`Unknown` refuses both questions and counts. `ChunkPipeline.SubmitDetached` / `IDetachedJob.OnResult`
routed ahead of the chunk lookup / `WaitForDetached`. `FluidPublished` for the report line and the
HUD; `Grid`, `AwakeHeldCells`, `Rested`, `Resume`, `Step`, `ReachableChunks`, `LevelSheets`,
`Publish` refuse under a job; `SetNearField` lands at the next begin; `MayHoldWater` reads the
published panels and the captured set. Main: the boundary passes, capture, begin and submit; the
collection installs the surface from the worker's cells; the quit cancels and waits; a fault
freezes the water, printed once. `fluid ms` now means the boundary's cost, and a `boundary` line
prints it by pass — mean per boundary in the window and the worst over the run.

**Three things the build found that the pass did not.**

1. **Ownership is the batch *or* the job, and the fuzz's job arm found it on its first run.** A
   job begun with nothing awake has an empty batch, so `TickInProgress` is false for its whole
   life while a worker holds the grid, and a second job began under the first. `GridOwnedByTick =>
   TickInProgress || _job is not null`. So "nothing changes what sets the flag" was one term short.
2. **`Wake` asked the live terrain from the worker.** A move wakes its neighbours, and `Wake`
   refuses a cell that neither holds nor can hold by asking `_terrain` — the streamer, in the
   game. The pass classified sixteen sites and missed that this one is reached from the visit
   path; `TheWorkerReadsTheCapturedTerrainAndNotTheLiveOne` — two counting terrains, the live one
   must be asked nothing during a run — found it, and `Wake` reads the visit terrain now, which at
   a boundary is the live one.
3. **The refusal count over-claimed, and the played world's open said so.** 4,754 refusals in the
   open's first seconds read as the number that had to be zero not being zero. A refusal for a
   chunk that is *not loaded* is the live terrain's own answer and costs nothing; only a refusal
   for a chunk that arrived mid-tick is a cell paused one boundary longer than the sync arm — so
   the view records the chunks it refused and the collection asks the live terrain which of them
   had arrived. **On the open: 4,754 refusals, of which one.** That one is decision 7's whole cost
   there.

**Measured.** The M8b-4a wire check on the job arm lands the same trajectory as the sync arm to
the visit — 736,323 visits, 32,953 hand-offs, peak 27,947, all 200,000 L handed — with 61 ticks
collected at 2.03/s, worker wall mean 39 ms max 147 ms, no boundary waiting, no refusal; the
boundary costs settle 7.0 ms, field sync 4.3, publish 1.1, hand-off and drain 0.8, pour 0.4,
capture and compose 0.02, mean per boundary. **On the played world's open, both arms hold 2 Hz**
at ~18,000 active (13,000 near + 5,000 far): the sync arm in ticks of up to 27 frames at the 2 ms
budget, the job arm at a worker wall of ~50 ms mean, 168 max, 0 boundaries waited, `loaded`
identical to the litre and every identity exact. The `sources` line is identical in both arms at a
standing start (3,528 jobs), so generation is not starving there; a moving flood is the play's to
read. **The surface on the job arm**: 775 quads from the worker's walk, 0.78 ms of walk on the
worker against 0.28 ms of install on the main thread.

**Two costs on the open that both arms pay and neither arm ever timed**, visible now because the
boundary is: **field sync 235–279 ms** worst at the first flooded boundary — slice 2's "the first
flooded boundary's build cost is the number to take", taken: dozens of 32,768-question bakes in
one frame — and **the settle's first pass 114–138 ms** over the restored lake. Both are named in
TODO as levers; both are the whole of `fluid ms` on the job arm.

**Not measured yet, and it is the play's**: the flood the played world used to have on open is
gone (the lakes were drained on 2026-09-05), so the case 3b was planned for — 110,000 cells, 188
frames a tick — no longer exists in that file. The provocation is `Q` under a lake with
`--fluidjob=1`, reading `fluidjob`'s wall and `boundary(ies) waited` on the `M8b:` line, and the
`M2c:` line's generates beside the sync arm's.

**Played 2026-09-10 — *"I got it to skip slightly but that was all"* — and the log says what the
play was**: 155 s, 101 relay pours, **peak 153,250 active cells at a steady 2 Hz**, worker wall
172 ms mean / 319 ms max, `0 boundaries waited`, every identity exact, `displaced 0`, 461 chunks
drawn throughout, 0 refusals for a chunk that had arrived. The sim-thread arm cannot do that: a
319 ms tick at 2 ms a frame is 160 frames, a tick every 2.7 s. **The skip is the boundary's worst
frame, 332 ms — field sync 230 ms and the settle's first pass 143 ms** — the two costs 3b's
measurement named as both arms' and neither arm's to see. Accepted; the skip is 3c's first item.

### Slice 3c — the planning pass, 2026-09-10

Three things, in the order the play put them: the skip, the race battery, and the one measurement
still open.

**1. The skip is two boundary passes, and each has a small lever with the order preserved.**

- **The field sync.** `FluidField.Sync` bakes every reachable chunk with no field, inline, at the
  boundary: 32,768 questions to the streamer each, ~2.3 ms in Debug, and the first flooded
  boundary of a restore or a chunk bite finds dozens at once — 230 ms in one frame. Two levers.
  *Cap the bakes per boundary* (`--fieldbakes=<n>`, 8 an estimate at ~16–20 ms Debug): a chunk
  past the cap has no field, so its cells refuse and pause, and **the bake landing at a later
  boundary must wake them the way an arrival does** — a paused cell has no other way back, which
  is a latent hole today for a *declined* build whose chunk never "arrives" (it was loaded all
  along). In play the reachable set grows by a few chunks a tick and never meets the cap; the open
  and the bite are where it spreads a 230 ms frame over a dozen boundaries. *Or bake as a job*: the
  bake is a pure function of a chunk's voxels and the material table, `VoxelChunk` is already
  copy-on-write while shared with a job (M3a), so a `BakeFieldJob` per chunk on the scheduler with
  the result applied at a boundary removes the cost from the sim thread entirely — but it is a new
  chunk-scoped job kind through the pipeline's `Complete`, with the wake at landing the same. The
  cap is twenty lines and bounded; the job is the principled one.
- **The settle's first pass.** `LevelSheets(share, quiet)` over `Pending` at the boundary, 143 ms
  over the played lake, atomic by design (M8b-3b: a pass is O(sheet)). At the end of a job's visits
  `Pending` is exactly the next tick's set, so **the solve can run at the tail of `RunOnWorker`**,
  after the visits and before the surface walk, and the order the M8b-3b argument fixed — settle,
  then hand-off, then drain, then pour, then the next tick — is unchanged, since the collection
  runs the hand-off after the job. The trigger (`SheetSettle.AtBoundary`'s quiet-tick decision)
  stays on the sim thread and is captured into the job; the three `_terrain.` sites in
  `FluidSimulation.Sheets.cs` take the visit terrain, as `Wake` did; the result lands on the
  settle's counters at collection. Cost: the job's wall grows by the solve — 319 + 143 ms is
  inside the 500 ms and "a slow tick lowers the rate" covers the rest.
- **`Publish`'s awake walk**: 14.9 ms worst on the play, a walk over the paused and queued sets at
  every boundary. A count maintained where cells are woken, visited and paused makes it O(1).

**2. The race battery, on real threads.** Every seam the ownership design closes is closed by
refusal or deferral, so a "race" here is a mutation landing while a *running* worker — not a held
one — reads the view and writes the grid: `Disturb` and `field.Invalidate` (an edit), `Evict` and
`Sync` (an unload — the sync is a boundary call and must stay one), `ChunkArrived`/`Restore` (an
arrival, parked), `SaveAll` (refused, the autosave postponed), and a quit (cancel, wait, write).
The oracle the runtime gives is weak — .NET reports no data race — so the battery's oracle is:
no fault, the view unchanged by an edit that landed during the run (a counting view), every
identity exact at every collection, rest real at the end, and **the quit mid-job**: cancel, wait,
write, reopen, and the rows list the requeued cells awake with the water cell for cell. Driven as
one stress test over the real scheduler with a large pour so the worker is genuinely running while
the sim thread edits under it, plus the quit as its own test. **A recipe the battery must contain**:
`Invalidate` mutating the live array in place instead of replacing it — the copy-on-write the
view's safety rests on has no test that fails without it.

**3. The measurement still open: generation under a moving flood.** The wire check's `sources`
line is identical in both arms and the play drew 461 chunks under a 153k-cell flood, so nothing
says generation starves — but nothing measured it either. One scripted arm: `--injectlitres=
2000000 --speed=12 --turnaround=10` in both arms, reading `sources` and `first draw`; the plan's
trigger for a dedicated thread is that line moving.

#### The decisions

1. **3c is the skip's two levers, the race battery and the starving measurement, in that order.**
   Recommended: the skip is what the play reported, and the battery is the plan's own item.
2. **The field bakes are capped per boundary, with the bake waking the chunk's paused cells as an
   arrival does; the bake-as-a-job is the named revisit** if the cap's wait is ever seen in play.
   Recommended: twenty bounded lines against a new job kind, and the wake-at-bake closes a latent
   hole either way.
3. **The settle's solve runs at the tail of the job, the trigger stays on the sim thread, the
   order is preserved.** Recommended.
4. **The awake count is maintained, not walked.** Recommended.
5. **The race battery is one stress test over the real scheduler plus the quit-mid-job test, with
   the in-place `Invalidate` recipe in its battery.** Recommended.

### Slice 3c — built 2026-09-10, all five as recommended

Suite 1,234 → 1,240; 14 break-recipes, 14 caught — three of them only on a second run, because
the first run's catches came from a stress test that was itself flaky, which is the finding below.
`--fieldbakes=<n>` (8) is the cap's sweep; `--fluidjob=1` is unchanged in `start.cmd`.

**The skip.** `FluidField.Sync(reachable, maxBuilds, lateBuilt)` bakes at most *n* chunks a
boundary, owes the rest (and every decline), and hands back the chunks it baked late so Main can
wake them with `ChunkArrived` — a paused cell's only way back. The settle's solve runs at the tail
of the job (`SolveOnWorker`, told `Ticks + 1` so the quiet test reads as the boundary's; the three
`_terrain.` sites in the sheet solve take the visit terrain) and `SheetSettle.Record` counts it at
collection; the order settle → hand-off → drain → pour → next tick is unchanged, and the twin test
asserts the worker solves exactly when the boundary would and against the same tick number.
`Publish` counts queued-plus-paused without the walk; the quit's line walks once
(`Publish(exactAwake: true)`). **On the played world's open — which now holds the 153,000-cell
flood the play made — the worst field sync went from 748 ms to 51 ms, and the settle's sim-thread
cost to 0.** The cap's price is refusals: 318,807 against 15,265 uncapped, of which 394 for chunks
that had arrived — cells waiting a boundary or two for their bake.

**The race battery found a real hole in 3b, and it was not any of the races the pass listed.** The
stress test — a pour big enough to keep a worker running while the sim thread edits under it,
moves the box away and back, and is refused pours and saves — stranded a front on two runs in
three, on clean code, always the same shape: a cell holding water asleep over an empty holdable
cell, not paused, not queued. Bisecting the mutations put it on the moves; the diagnostic
(`FirstCellWithAMove`, which now names the cell and why for every `SettlesAndRests`) put it on
the eviction. **A chunk that leaves the box keeps its panel, so it stays reachable, so its field
stayed with it — and a worker's view therefore covered a chunk the world no longer answered for.**
The eviction's linger rule depends on a departed chunk reading *not loaded*, so the tick pauses
its queued cells and the release lists them awake; on the job arm the tick went on moving them,
the release listed only the paused, and the moved ones came back asleep. The sync arm never had
this, because the live terrain says "not loaded". `Sync` drops the field of any reachable chunk
the source no longer answers for (`Departed`) and owes it; the worker then pauses the cells as the
sync arm does. **Eighteen stress runs green after; the pin is
`AFieldForAChunkThatLeftIsDroppedAtTheSyncSoTheWorkerPausesItsCells`.** On the way it also found
**`Invalidate` baking an unloaded voxel as rock** — the frozen-water "conditional no" through the
edit hook's door, unreachable in play (edits need a loaded chunk) and reachable by the battery;
it declines and drops the field now.

**A finding about the battery itself.** The first battery run reported four recipes caught only
by the stress test, including one that could not affect it (the solve's tick number — the stress
passes no settle to the job). That was the flake wearing a catch's costume; the re-run after the
fix showed three of the four MISSED, and each got its own pin. **A recipe caught by a test that is
not stable is not caught.**

**The measurement.** A 2 ML moving flood (`--injectlitres=2000000 --speed=12 --turnaround=10`,
r4h/2v, 30 s), both arms: `3276 generated` in both — generation does not starve, so the dedicated
thread's trigger does not fire — and **`first draw` 6.08 s on the job arm against 8.72 s** on the
sync arm, because the sim thread is free. The sync arm ran 36 ticks in the window with a longest of
289 frames; the job arm ran 57 at 2.1× the visits (5.45 M against 2.57 M), worker wall 288 ms mean
/ 685 max with the solve at 77 ms of it, 17 boundaries waiting for a slow tick — the rate lowering
rather than a cell skipped, as decided. The boundary on the job arm: 23 ms mean, of which field
sync 13.5, capture and compose 3.9 (a 200,000-cell batch), pour 3.5 (the injection's 198.7 ms
once), hand-off 2.1, settle and publish 0.

**Unplayed.** The play check is the open of the played world and a bite under a lake, reading
`boundary worst` on the `M8b:` line — field sync should read tens of milliseconds, settle 0 — and
whether the skip is gone.

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
- **The tick split across workers — one job per chunk, or per colour wave — if `boundary(ies)
  waited` on the `M8b:` line goes non-zero routinely**: a tick longer than its 500 ms period on the
  played world. **Fired 2026-09-19 and closed the other way**: 1,261 of 1,375 boundaries waited on
  the played world with the tick's batch at 3,514 cells — the worker's 750–870 ms was the settle's
  solve flooding 294k cells a pass from the carry's 456k seeds, which a split of the visit loop
  would not touch; the lever is the solve's seeds (TODO under M10d), and this trigger reads the
  `fluidjob` line's `visit` ms against its `solve mean` before it is believed again. Matthew's question, 2026-09-15 (*"the chances of running each chunk's water calc
  in its own thread? or too much double handling / too much overcopy"*). Not built, because the
  visit loop is not what anybody waits for: a 150,000-cell flood tick is ~45 ms Release and 172–319
  Debug against a 500 ms period, `waited` read 0 on the 3b play, and the skip that was felt was
  the boundary — field sync, hand-off, pour, all sim-thread — which threads under the tick do not
  touch. **The shape when it fires: colour waves, not halos.** Chunks of even `(x + y + z)` run as
  N detached jobs, then the odd ones, so adjacent chunks never race; what it costs is the grid's
  four shared things — the panel dictionary (no release mid-tick), the one-entry cache, the
  `_total`/`_held` counters (per panel, summed at collection) and the wake queue (per job, merged)
  — and the visit order, which is a physics input here, so the sliced-equals-whole oracle becomes
  wave-order-equals-a-reference. The halo form (six face layers copied per chunk, 12 KB, each side
  computing the same pairwise flux — which M8a's fixed divisor was chosen to allow) is the cheaper
  copy and the wrong physics: M8d-3's 16-cell fall crosses faces inside a tick and a one-layer halo
  stops it at the face.

## Deliberately not in this piece

- Panel lifetime — M8b-4c's, and the reason this waits for it: a worker holding a panel that the
  unload wants to release is the race M3a solved for voxels with `VoxelsSharedWithJob`.
- Seepage (M8c) and the transparent pass (M8d).
- Matthew's fall rule (TODO, beside M8d's waterfall question) — orthogonal, and cheaper on the
  thread than off it, which is one more reason it waits.
