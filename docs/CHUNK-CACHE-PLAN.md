# The retained-chunk cache — plan

Planned 2026-09-03 on Matthew's "go", after the "ground is gone" session. A streaming slice, not a
milestone: it sits between `ChunkStreamer` and the store and changes nothing about what a chunk
*is*. Read the 2026-09-03 worklog entries first — the case this exists for is a played world where
every chunk round the player is a stored one.

## Slice 1 — built 2026-09-03

Suite 895 → 910; the standing-start r6 baseline is 2601 / 325 / 3446 at 411 in both arms. The
design below was built as written except where this section says otherwise.

**Measured**, r6h/2v, Debug, `--speed=6 --turnaround=5`, cache 64 MiB against 0:

| | 6 m/s, seed world | 20 m/s, seed world | 6 m/s, played world |
|---|---|---|---|
| generated | 4599 → 3213 | 9539 → 4590 | 2266 → 1580 |
| loaded | 0 → 0 | 0 → 0 | 2332 → 1633 |
| from the cache | 0 → 1386 | 0 → 4960 | 0 → 1385 |
| derives | 5233 → 5269 | 8958 → 8952 | 5395 → 5400 |
| `viewer ms` mean / p50 | 6.58 / 6.07 → 0.91 / 0.33 | 6.08 / 6.08 → 1.05 / 0.29 | 6.76 / 6.12 → 0.95 / 0.34 |
| cache held at the end | 612 chunks + 68 columns, 1.3 MiB | 1989 + 221, 4.2 MiB | 612 + 68, 1.8 MiB |

The number the slice turns on came out as predicted: **loads and generates went to zero for the
cached chunks and the derives did not move**, so slice 2 is what remains. At 20 m/s both arms still
draw nothing — the M5d-1 cliff is a derive cliff, and the cache cannot touch it.

**Where the build departs from the plan:**

- **§6 is wrong as written: a dirty column is not cached.** A column released dirty is handed to
  the save with no copy-on-write to protect it — a chunk has `VoxelsSharedWithJob`, a column has
  nothing — so a revival could edit it under the serialiser. Only a column with nothing to write is
  kept. In play that is nearly all of them, because the autosave marks the live set clean; the one
  that is not is the column just dug, and it comes back through its restore as before. A restore
  landing after its column was released and cached is copied into the cached object
  (`ChunkStreamer.CachedColumnAt`), or the revival would hand back the analytic seed and never ask
  again.
- **§3's dirty-on-evict rule is belt and braces, and its premise was wrong.** `StepBack` does not
  restore `DirtySinceSave` on a dropped save; `ApplySave` puts the bytes back on the pending list
  and leaves the flag alone. Nothing today can dirty a cached chunk. The hook is kept and tested by
  setting the flag by hand.
- **Revival is a state-machine edge, `Unloaded → Generated`**, taken only when the chunk has
  voxels; the stage drops from `Lit` to `Features` on cache entry, because the grid goes with the
  chunk and `CanMesh` would otherwise mesh from a grid that does not exist. The pipeline's queue
  flags are cleared on entry — every queue skips an untracked position on dequeue, so a stale
  entry is harmless and a stale flag would strand the chunk.
- **A hit pushes the chunk's 26 neighbours**, for the reason a load's completion does: nothing
  else re-examines a neighbour that was waiting on it.
- **§4's 3.5 KB was for slice 2.** Without the grid a cached seed chunk is ~2.0 KB and a played-
  world chunk ~2.8 KB; 64 MiB is on the order of 25,000 chunks. Still an estimate, still Matthew's.
- **A first draw after the turn was not measured**: the profile reports the run's first draw, and
  a standing-start fill is identical in both arms. The `sources` line is the instrument.

**Found, not planned: the box-difference sweep is mostly column seeding.** M6d-2 measured `viewer
ms` at two to four times the pipeline update and chose not to chase it. A hit skips
`SkyColumn.Seed` (1,024 surface evaluations per column) and the record allocation, and the mean
drops seven-fold. Recorded in TODO as a question rather than acted on.

**The battery**: seventeen recipes against tests 1–6 and 8 below, fourteen caught first time.
Two were green until the tests were strengthened — a stage left at `Lit` meshes dark and is
repaired before the settle returns (now: exactly one delivery, zero relights, across a
turnaround), and a stale `InWorkList`/`LightBuildQueued` strands a revived chunk that quiescence
cannot see (now: every tracked chunk derived and the centre at `Lit` after a mid-flight fuzz).
One is the identity: pushing only the revived chunk, because reaching `Lit` pushes the 26 anyway.
One stays green for a reason the fuzz does not reach: a stale `AwaitingLight` heals whenever a
neighbour reaches `Lit`, and the stranding case is one chunk revived among already-lit neighbours.
Test 7 is slice 2's.

## Slice 2 — built 2026-09-04

Suite 926 → 936; the standing-start r6 baseline is unchanged in both arms. §5 was built with one
of its two pushes turned into a check, and the walk it asked for in test 7 found three things
that were not the cache's.

**Measured**, r6h/2v, Debug, `--turnaround=5`, cache 64 MiB against 0, the same recipe as slice 1:

| | 6 m/s, seed world | 20 m/s, seed world | 6 m/s, played world |
|---|---|---|---|
| generated | 4599 → 3213 | 9510 → 4590 | 2219 → 1543 |
| loaded | 0 → 0 | 0 → 0 | 2380 → 1670 |
| from the cache (with their grid) | 0 → 1377 (918) | 0 → 4812 (3261) | 0 → 1386 (919) |
| derives | 5157 → 3572 | 8454 → 5611 | 5094 → 3568 |
| reseeded instead | 0 → 918 | 0 → 3261 | 0 → 919 |
| `viewer ms` mean / p50 | 6.69 / 6.11 → 1.06 / 0.50 | 6.11 / 6.07 → 1.21 / 0.36 | 6.80 / 6.22 → 1.00 / 0.37 |
| `update ms` mean | 3.40 → 2.36 | 5.30 → 4.20 | 2.96 → 2.06 |
| main thread | 46.4% → 31.4% | 74.9% → 56.4% | 40.1% → 27.6% |
| first draw / light settled | 6.61 / 6.59 → 6.58 / 6.55 s | never → 12.20 / 12.17 s | 7.34 / 6.42 → 6.44 / 6.37 s |
| held at the end | 612 (482 lit) + 68, 2.0 MiB | 1989 (1525 lit) + 221, 6.5 MiB | 612 (494 lit) + 68, 2.6 MiB |

**The number the slice turns on came out: a third of the derives went with the loads and the
generates.** Every chunk that came back with its grid was reseeded rather than derived — 918 of
1,377 hits at 6 m/s, 3,261 of 4,812 at 20 m/s — and the ones that did not (459 at 6 m/s) had left
during the first fill before they ever derived, which is the fill having nothing to keep as well
as nothing to hit. **And the 20 m/s arm draws.** Slice 1 recorded that at 20 m/s both arms drew
nothing because M5d-1's cliff is a derive cliff; with the grids kept, the box retraces lit chunks
after its first turn, the derive queue empties enough for the drawn box to settle, and `first draw`
lands at 12.2 s where the control arm never lands at all. That is the turnaround shape only —
arriving somewhere new at 20 m/s is the cliff it always was.

**Where the build departs from the plan:**

- **§5's column push is a check at revival.** The plan walked the column dropping cached grids
  when a height moved; slice 1's finding that a dirty column is not cached is what made that
  insufficient, because a column can then be *replaced* while a chunk is out — released dirty,
  dropped, re-seeded on return as a different object — and no push at the change can name the
  chunks derived against an object that no longer exists. `SkyColumn.Revision` moves with every
  height and never repeats across columns; `StreamedChunk.LightSkyRevision` records what a grid was
  derived against; `ChunkStreamer.ValidateRevivedLight` compares the two once. One comparison covers
  an edit under the same column, a restore landing in a cached column, and the replacement, and each
  has its test. **The sharp direction is a height *raised* above a cached chunk**: digging down
  from above stops at the unloaded chunk and changes nothing in it, so only a placed voxel can make
  a cached chunk's sky flags stale.
- **§5's face push lives in `QueueLightBuild`, not at the edit.** "The derived data at this
  position is stale" is what every caller of that method means, so an untracked position drops the
  cached grid there; the edit's eight-chunk scope reaches a cached neighbour through the same call
  that reaches a loaded one, and it costs nothing for a tracked position, which is every interior
  write of a chunk bite.
- **The values are cleared on cache entry and reseeded in `StepLight`, not at the hit.** The
  reseed is `SpreadInto` plus `InvalidateChunk` — the fresh-grid path minus the derive — and it is
  deferred to the light step because the changed-cell list is only collected there; a seeded
  boundary cell nobody recorded leaves a drawn neighbour sampling darkness where the grid used to
  be. `LightInFlight` counts pending reseeds, so nothing reaches `Lit` from zeros in between.
- **The plan's memory estimate was the right one after all.** ~3.4 KB per cached seed chunk with
  its grid, ~4.4 KB in the played world; 64 MiB is on the order of 19,000 chunks.

**What test 7 found, none of it the cache's** — the walk is M5b's edits with the streamer cycling
chunks in and out, and its cap-0 control arm is what attributed each finding:

- **The whole loaded set was never a fixpoint.** A grid leaving stopped propagation at its faces
  and left what it had already given the cells next door — a top-layer cell under a vanished
  chunk kept the skylight that fell into it, a tunnel cell kept a lamp that left. M5c-1 accepted
  that because at margin 2 such light cannot reach the visible box; what it did not cover was the
  chunk coming back with different sources, when the kept values had no source anywhere and the
  returning grid imported them through `SpreadInto`. **`LightPropagator.RetractFrom` is the
  inverse of `SpreadInto`**: the 384 cells around a departed grid are held to the fixpoint rule with
  that grid contributing nothing, and only the ones that fail are darkened — a rock cell answers in
  one read, a sky source in two, so it is not the boundary thrash M5c-1 refused. Served first in
  `StepLight`, before any reseed.
- **The un-light walk had a hole at value 1 since M5b.** It skipped every face through which a
  zeroed cell could have given nothing, and with it the reseeding of a neighbour at 2 that still
  justified the cell at 1 — two lamps whose reach meets at one cell. M5b's fuzz never removed a lamp
  there; the walk did.
- **A face conducted on one cell's word, and that leaked skylight into rock.** A chunk derived
  while a neighbour was absent read air across that face; when the neighbour arrived holding rock
  its own bit said closed while the stale one said open, until the gain re-derive ran — and the
  revival's spread ran a frame before it, every time. **A face conducts only when both cells say
  so**, in the add pass, the un-light walk and the retraction: a no-op in the steady state, the
  fresh answer in the transient, one read of a byte already fetched.

**The battery: twenty-one recipes, fifteen caught, six green — and each green one has a name.**
Caught: the face push skipped (2 — the face test and the walk), the revision check skipped (4 —
all three column tests and the walk), the revision not bumped when a height closes down (1), not
bumped by a restore (1), the values kept on cache entry (2), no reseed at revival (6), no re-spread
at revival (1, the walk alone), a revival left at `Lit` (1), `LightNeighbours` zeroed on entry (1 —
the plan named it the oracle's identity, and it is; the cost assertion is what catches it), a
dropped grid's bytes not given back (2), the revision not recorded at the derive (7), departures
not retracted (2), a face crossed on one cell's word (1, the walk alone), and the un-light walk's
reseed hole (2). Green: **the revision not bumped when a height opens further** — unreachable for
a cached chunk, because the downward scan stops at it and cannot open its cells, so that bump is
belt and braces beside its sibling, which is not; **`LightInFlight` ignoring pending revivals, and
pending departures** — identities by ordering, since both are served at the top of the light step
and the pump that could mark a chunk `Lit` runs after it, kept because a budgeted reseed would make
them load-bearing; **the retraction trusting the cell's own word for a face** — an identity at
settle, where the two words agree, and the oracle reads the cell's word too; **the column check
dropping the grid but not `LightNeighbours`** — a null grid derives regardless; and **the retraction
running before the propagator forgets its one-entry cache** — the pre-existing guard at the top of
`StepLight`, green because the cache would have to be pointing at the departed chunk, which is a
flake by nature and M5c-1's category. The walk caught nine of the fifteen and two on its own; the
unmutated control run was green.

**Test 7's oracle is two checks and a control.** Every tracked chunk's derived data equals a fresh
derivation from the voxels and the column as they are now (faces toward an unloaded neighbour
masked — the gain-only rule's own blind spot, and harmless because a missing grid stops propagation
whatever the bit says); the whole tracked set is at the fixpoint; and the same walk at cap 0 says
which failures are the cache's. The plan wrote "incremental == from-scratch with the streamer
cycling chunks" and that is what it now asserts, over the whole loaded region.

## The question

**Does a chunk that leaves the loaded box and comes back cost anything?** Today it costs
everything: `ChunkStreamer.Forget` drops the voxels, the light grid and the sky column, and the
return is a serial load (or a generate), a derive on the sim thread, and a mesh. Turning round is
as expensive as arriving somewhere new. The cache makes the return free for the two halves that
are exactly reproducible — the voxels and the derived light — and leaves the two that are not.

**The premise `Forget` was written against has changed.** Its comment says a returning chunk
*regenerates its voxels from the seed*, so light derived from the old ones "would be keeping the
one thing guaranteed to be re-derived anyway". Since M6 a returning chunk comes back from disk or
from the seed **with the identical voxels either way** — that is the whole contract of persistence
— so light derived from them is stale only if a *neighbour* changed, which is a rule, not a
regeneration. That comment is corrected in the same pass as the code.

## What it buys, honestly

- **A turnaround costs no load, no generate, and (slice 2) no derive.** In a played world the
  stored neighbourhood is the part that arrives last through the serial lane and is derived last
  through the FIFO; a cache hit arrives at `View` time, so cached chunks enter the derive queue
  *first*. **Prediction, to be measured**: the turnaround case inverts the ordering problem the
  2026-09-03 entry describes, without touching the queue.
- **Nothing for arriving somewhere new.** A cache is reactive. Look-ahead along the direction of
  travel is a different change and is deliberately not this one.
- **Nothing for the first fill of a session**, which is the case Matthew saw. The distance-ordered
  derive queue (TODO) is that fix, and this plan does not replace it.

## Design

### 1. The cache holds `StreamedChunk` objects, not bytes

A hit is the object itself: `Voxels`, `Light` (slice 2), `Stage`, `HasEdits`, `DirtySinceSave`,
`VoxelsSharedWithJob`. Serialising into the cache would buy nothing — the store already holds the
bytes — and would lose the flags, and **the flags are where the persistence defects live**.

`ChunkStreamer` keeps a second dictionary, `_cached`, plus an eviction order. `Forget` becomes
"move to `_cached`" when the cache is enabled; `View` consults `_cached` before creating a new
`StreamedChunk`. The state machine is untouched: a cached chunk is `Unloaded`, as it is today after
`Forget`, and `StreamedChunk` already keeps its `Voxels` reference past `Forget` for a job in
flight, so `Unloaded` with voxels is a state that exists now.

### 2. Memory, then cache, then disk, then seed — and the order is the whole correctness argument

M6's rule is *memory, then disk, then the seed*, and its comment says getting the order wrong is
the only load-path failure that matters. The cache is a fourth source and it goes **second**: a
chunk in `_chunks` is never in `_cached`; a chunk in `_cached` is preferred to the store's index.
A cached chunk is the object that was saved, so it can never be older than the disk.

### 3. The cache never holds the only copy of anything

`QueueSave` still runs at unview, *before* the chunk enters the cache — unchanged. So eviction is
a free drop. **One belt-and-braces rule**: on eviction, if `DirtySinceSave` is true (a save was
dropped and `StepBack` restored the flag), queue the save first. Without it a dropped save followed
by an eviction is a deleted tunnel, and that is the one failure this project treats as unforgivable.

`SaveAll` on quit already drains the serial lane; cached chunks are clean by construction and are
not walked.

### 4. Bounded by bytes, evicted least-recently-forgotten first

`VoxelChunk.EstimatedBytes` exists and is exact per storage kind; a `ChunkLight` is 1,536 bytes.
The cap is `--chunkcache=<MiB>`, **0 is off and is the control arm**. Default **64 MiB — an
estimate, and it is Matthew's to move**: at ~3.5 KB per cached chunk that is ~18,000 chunks, seven
times the r6 tracked box, which is a turnaround of well over a minute at walking speed. Meshes are
not cached (46 KiB each, and they are the expensive half of memory per M2d).

Eviction order is a doubly linked list threaded through the entries, O(1) both ways. Least
recently *forgotten*, not least recently used: a cached chunk is never touched while cached.

### 5. Light — slice 2, and where the risk is

The derived data (conductance, sky-source bit, emission) is a function of the chunk's voxels,
its six face neighbours' voxels, and its sky column. All three are reproducible, so the derived
data is valid on return **unless one of them changed while the chunk was cached**. Two things can
change one, and both already have a hook site:

- **An edit at a chunk face** — `SetVoxel`'s existing scope, "the neighbour across whichever
  faces it touches". If that neighbour is cached, drop its derived data (`Light = null`,
  `LightNeighbours = 0`). The recipe goes on the call site, M5c-2's rule.
- **A sky height that moves** — the existing per-column `QueueLightBuild` loop. It walks loaded
  chunks in the column; it must now also drop the derived data of cached ones. *(Built as a check
  at revival instead — see "Slice 2 — built" for why a push cannot cover a replaced column.)*

**The propagated values are never kept.** A lamp placed or removed ten metres away while a chunk
was cached leaves its values wrong and nothing would ever darken them — an un-light walk runs at
edit time in loaded chunks only. On revival every cell is reset to its own source and the
propagator's existing new-grid path (`SpreadInto`, six face layers) refills it. That is exactly
what a fresh grid does today, minus the derive.

`LightNeighbours` is kept: the gain-only rule then re-derives on return only if a neighbour that
was absent is now present, which is the correct condition and the existing one.

### 6. Sky columns are cached the same way, and are always valid

A column's refcount hits zero when no chunk in it is loaded; a column can only change through an
edit to a loaded chunk in it; so a cached column has nothing that can change it. Same cap, same
list, no invalidation rule at all. Its restore from the store is a serial load today, so this is
also a serial-lane saving.

## What it does not do

- No prefetch, no look-ahead, no directional box. Recorded as the next question, not this one.
- No mesh cache. **Revisit trigger: slice 2 measured and the turnaround is mesh-bound.**
- No batched or parallel loads. Their trigger is unchanged: thousands of *non-uniform* stored
  chunks.
- Nothing in Godot. Every line is in `Overburden.Core`, and `Main` grows one flag.

## Split

- **Slice 1 — voxels and columns.** `_cached`, the byte cap, eviction, the dirty-on-evict rule,
  the flag, the profiler counters. Light is nulled exactly as `Forget` does today.
- **Slice 2 — derived light.** The two invalidation pushes, the reset-values-on-revival rule,
  and the oracle below. Only after slice 1's numbers say the derive is what remains, which the
  2026-09-03 figures already predict.

## Tests, and what has to break them

Oracles first, break recipes beside them. Every one runs against `InMemoryWorldStore`.

1. **Dig, walk away through the cache, come back: the edit is there and the store was written.**
   M6's headline scenario with the third source in the middle. Recipe: consult the cache *after*
   the store — must fail on a chunk edited, forgotten, and revived before its save lands.
2. **A hit submits no job.** `ChunksLoaded` and generate counts unchanged across a turnaround.
   Recipe: revive without checking `_cached` — counts move.
3. **Byte accounting is exact.** The tracked total equals the sum over entries after every
   forget and evict, fuzzed. Recipe: skip the subtraction on evict — drifts.
4. **Eviction is a free drop, except when it is not.** Cached, clean, evicted: zero store calls.
   Cached with `DirtySinceSave` (save dropped, flag restored): exactly one save queued. Recipe:
   remove the dirty check — the second half fails.
5. **`VoxelsSharedWithJob` survives the cache.** Forget mid-save, revive, edit: the edit clones.
   Recipe: clear the flag on cache entry — the save job sees the edit.
6. **The cache is a fourth source and never a first.** A chunk in `_chunks` is never in `_cached`,
   asserted after every transition, fuzzed with the box walking. Recipe: forget without removing
   from `_chunks` — the invariant fails at once.
7. **Slice 2's load-bearing one: incremental == from-scratch, with the streamer cycling
   chunks.** The M5b fuzz with the box walking away and back, edits placed at chunk faces, sky
   heights broken through, lamps placed *and removed* while the far chunk is cached. Recipes, each
   of which must fail this and nothing else: skip the face push; skip the column push; keep the
   propagated values on revival (fails only on the removed-lamp case, which is why that case is
   in the fixture); keep `LightNeighbours` at zero (does not fail — it only re-derives more, which
   is the identity recipe and is named as one).
8. **`--chunkcache=0` is the committed code.** The default headless baseline is unchanged to the
   digit in both arms at a standing start, because nothing leaves the box.

**Watch every one fail first.** Item 7's fixture must assert that the far chunk was actually
cached and actually revived, or it is the M5c-1 sky-shaft trap: a test that passes because the
path it names never ran.

## Measurement

A turnaround needs a seam the flythrough does not have: **`--turnaround=<seconds>`** reverses the
scripted velocity every N seconds. Then, Debug and Release, r6h/2v, 6 m/s and 20 m/s, cache 0
against default:

- loads, generates and derives per turnaround; `first draw` after the turn
- `viewer ms` (the box-difference sweep, which M6d-2 found is the larger sim-thread cost —
  a cache hit adds a dictionary move to it)
- resident memory and the cache's own byte count, printed with its cap
- the played world, by the `probe-load` recipe, with `screenshot.gd --settle=`

The number the slice turns on: **derives per turnaround at 6 m/s**. Slice 1 should leave it
where it is and slice 2 should take it to ~0 for the chunks that were cached; the loads should go
to ~0 in slice 1. If slice 1 moves `first draw` after the turn by less than the rep-to-rep spread,
that is expected and is the argument for slice 2, not against slice 1.

## Costs stated

- Memory: the cap, and the cap is the whole cost. 64 MiB default is an **estimate**.
- Sim thread: a dictionary move on forget and on hit, an O(1) list splice, and on evict a byte
  subtraction. Nothing per frame.
- Code: `ChunkStreamer` gains a second dictionary and two rules; `ChunkPipeline` gains two
  invalidation pushes at existing hook sites; `Main` gains one flag; the profiler two lines.

## Revisit triggers

- The default cap, the first time a play reports memory rather than stutter.
- Mesh caching, when slice 2's turnaround is measured mesh-bound.
- Look-ahead, when the turnaround is free and arriving somewhere new is still the complaint.
