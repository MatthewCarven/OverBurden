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
