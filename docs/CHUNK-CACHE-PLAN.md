# The retained-chunk cache — plan

Planned 2026-09-03 on Matthew's "go", after the "ground is gone" session. A streaming slice, not a
milestone: it sits between `ChunkStreamer` and the store and changes nothing about what a chunk
*is*. Read the 2026-09-03 worklog entries first — the case this exists for is a played world where
every chunk round the player is a stored one.

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
  chunks in the column; it must now also drop the derived data of cached ones.

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
