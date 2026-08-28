# M2 — Streaming, worldgen, and the job queue

> Planning doc. See [DESIGN.md](DESIGN.md) for the decisions this implements, and
> [M2-REFERENCE-NOTES.md](M2-REFERENCE-NOTES.md) for what reading godot_voxel changed.
> Written 2026-08-27.

M2 is "chunk streaming and infinite terrain" in one line, which hides four parts that fail
in completely different ways:

| Part | Problem | Failure mode if rushed |
|---|---|---|
| **M2a** Job scheduler | Priority queue that reprioritises and cancels as the player moves | Everything above inherits a queue that can't reprioritise — the rewrite we just avoided |
| **M2b** Worldgen | Deterministic `(seed, pos) → chunk`, no tearing at seams | Visible seams, or terrain that changes shape on reload |
| **M2c** Streaming policy | Which chunks exist, refcounted across viewers | Refcount leaks. Looks like a memory leak, isn't one, and is miserable to find |
| **M2d** Unload path and measurement | Budgeted RID freeing, real numbers at real chunk counts | Stutter that lands after its cause and gets blamed on meshing |

**M2c is the one most likely to spill into two sessions.** Refcounting, in-flight
cancellation and async completion interact, and the bugs are state-machine bugs that only
appear under movement. M1c got that warning and didn't need it; this one probably will.

---

## M2a — The job scheduler

### The problem

Chunks need generating and meshing off the main thread. Which chunk matters most changes
continuously as the player moves, and a large fraction of queued work becomes worthless
before a thread is free to run it.

### The corrected data structure

DESIGN.md said `System.Threading.Channels`. That was wrong and is now recorded as a
rejected alternative. **Channels is FIFO**, and a FIFO can neither reprioritise queued work
nor drop stale work. A priority heap is wrong for the same reason: it orders at insert
time, which is exactly when the ordering is least valuable.

**Build a flat `List<Job>` behind a lock, swept on a timer.** The sweep — every ~200 ms,
not every push — does three things in one pass:

1. recompute each job's priority from current viewer positions,
2. remove jobs whose cancellation token is signalled,
3. sort, so workers pop the highest priority off the tail.

Sorting a few thousand entries five times a second is nothing. Doing it per-push, for an
ordering that is stale by the time it is read, is the expensive mistake.

### Priority is one `uint`

Four `byte` bands packed into a `uint` so comparison is a single integer compare:

| Band | Meaning |
|---|---|
| `band0` (low) | distance to nearest viewer, decreasing |
| `band1` | unused until M13 LOD |
| `band2` | job class — generate ahead of mesh, since mesh depends on it |
| `band3` (high) | reserved for "the player is standing on it" style overrides |

Bands are only meaningful in that precedence order; the point of the packing is that the
comparator is `a.Packed < b.Packed` and nothing else.

**Viewer positions are read without a lock, deliberately.** Priority is a heuristic — a
slightly stale distance still schedules approximately right, and a job scheduled
approximately right is not a bug. Make it safe the way godot_voxel does: a pre-sized array
that is never resized, bounded by an atomic count. Safe because of the allocation
discipline, not because the race can't happen.

### Cancellation, not distance-dropping

godot_voxel drops jobs by comparing distance inside `get_priority()`, and their own header
calls that "not always reliable" with a TODO to replace it. We start where they want to
end up: a real `CancellationToken` per job, signalled by the streaming layer when a chunk
stops being wanted.

**A cancelled job must still report a result.** If it vanishes silently, the chunk stays
flagged "meshing in progress" forever and never updates again. `JobResult` carries a
`Dropped` outcome; cancellation is not deletion.

### The pool

- **Dedicated threads, not `Task.Run`.** These are long-lived workers pulling from our
  queue. Handing jobs to the .NET thread pool puts us back on a scheduler that knows
  nothing about voxel priority, which is the whole thing we are building.
- **Thread count `clamp(round(0.5 × cores), 1, cores - 1)`.** Never all cores — Godot's
  renderer and our main thread need room.
- **One pool, two lanes.** A `serial` flag, not a second pool: disk jobs run one at a time
  because they contend on the same file anyway, and a second pool would make them compete
  in a separate queue that is ignorant of everything else. Not needed until M6; the flag
  costs nothing now and is awkward to retrofit.
- **Batch the enqueue.** Accumulate a frame's jobs in a list, take the lock once. Not once
  per chunk.
- **Results come back through a `Channel`.** This direction genuinely is FIFO and genuinely
  doesn't need reprioritising, so Channels is the right tool here — just not for the
  job queue itself.

### Where the padded 34³ copy happens — and why GC settles it

The open M1b item says build the padded copy on the sim thread when queueing. godot_voxel
does the opposite: the job holds `shared_ptr`s to the chunk and its 26 neighbours and
copies on the worker thread, which keeps the sim thread free and skips the copy entirely
for cancelled jobs.

**Copy on the worker.** Half the machinery they need for that is free for us: they use
`shared_ptr` to stop a chunk being freed while a job holds it, and in C# holding a
reference already does that. GC solves the lifetime half at zero cost.

What GC does *not* solve is mutation — a worker reading a chunk the sim thread is writing.
At M2 there is no writer: mining is M3, fluid is M8. So the worker-side copy is safe now,
and cheaper. **The revisit trigger is M3**, where the first concurrent writer appears and
either a per-chunk lock or a copy-on-write snapshot becomes necessary. Recorded so it is a
scheduled decision rather than a discovered bug.

### Tests

The entire scheduler is Core-only and headless-testable with fake jobs — no Godot, no
terrain, no meshing.

- Ordering: a nearer job runs before a farther one.
- **Reprioritisation:** enqueue a far job and a near job, move the viewer so the ordering
  inverts, assert the formerly-far job now runs first. This is the test that would have
  failed on Channels, so it is the one that matters.
- Cancellation: a cancelled job never runs its body, and still reports exactly one
  `Dropped` result.
- **The leak invariant, fuzzed:** across N random jobs, M workers, and random
  cancellations, every job reports exactly one result — never zero, never two. Threading
  makes exact sequences untestable, so assert conservation instead of order.
- Serial lane: two serial jobs never overlap; a serial job and a parallel job may.

---

## M2b — Worldgen

### The problem

Deterministic terrain that can be generated one chunk at a time, in any order, on any
thread, and produces the same world every time.

### Pure function, no ambient state

`(seed, chunkPos) → VoxelChunk`, reading nothing but its arguments. That constraint is
what makes generation trivially parallel and trivially testable, and it is easy to lose
by accident the first time a feature wants to know about its neighbour.

**Use an explicit stable hash — not `HashCode.Combine`.** .NET randomises `HashCode`'s
seed per process, so `hash(seed, cx, cy, cz)` built on it produces a *different world every
launch*. This is a genuinely nasty trap: it looks correct in a single session and fails
only across restarts, which is also exactly when a player would notice their world changed.
Write splitmix64 or xxHash explicitly and pin it with a test asserting known constants.

### Layers

- **2D fBm heightmap** → surface elevation.
- **Strata by depth** below that surface, from material definitions, so strata are moddable
  rather than hardcoded.
- **3D noise caves** carved after strata.

### The staged pipeline, and why it exists

Features that cross a chunk boundary — an ore vein, a cave system, later a ravine — cannot
be generated by a chunk that only knows about itself, or the halves disagree and the seam
shows.

The fix is stages with a barrier: **a chunk may advance to stage N+1 only when all its
neighbours have completed stage N.** Stage 1 is pure per-chunk terrain; stage 2 places
cross-chunk features, deterministically seeded from the feature's *origin* chunk, writing
into neighbours as needed. A chunk is only meshable once it has completed the final stage.

This is the part most likely to be waved through at M2 and then be expensive at M13 when
biomes arrive. The stage machinery should exist even if stage 2 initially places nothing.

### The empty-chunk fast path

A chunk entirely above the heightmap is uniform air; entirely below the cave floor,
uniform stone. `VoxelChunk`'s Uniform representation already costs one `int` and zero
allocation — the generator just has to *notice* rather than filling 32,768 voxels and
compacting afterwards. For a world with sky above and rock below, this is most chunks.

### Tests

- **Determinism across processes:** same seed and position produce byte-identical output in
  a fresh process. This is the test that catches the `HashCode.Combine` trap, and it must
  actually re-run the hash from constants rather than comparing two in-process calls.
- **Seam continuity:** generate two adjacent chunks independently and assert the surface
  agrees along the shared boundary column.
- **Order independence:** generating a 3×3 region in random order produces the same result
  as generating it in raster order — the real test of the staged pipeline.
- Uniform fast path: a chunk well above terrain comes back Uniform, and allocates nothing.
- Strata invariant: material at depth d is what the strata table says, for random columns.

---

## M2c — Streaming policy

### The problem

Decide which chunks should exist as viewers move, without loading what won't be seen,
without unloading what still is, and without leaking bookkeeping when the player changes
their mind mid-load.

### Viewers are plural, and live in Core

A viewer is a **position, a horizontal radius, and a vertical radius** — not a camera. Core
owns the streaming decision; the Godot layer only reports where the player is. That keeps
the non-negotiable seam intact and, not coincidentally, is what makes multiplayer a matter
of having two viewers rather than a special case.

**Vertical extent is separate from horizontal.** godot_voxel keeps a vertical *ratio* to
avoid loading useless sky. We likely want it the other way round — a mining game's content
is below the player, so a tall narrow column beats a cube. Either way "view distance" must
not be a single scalar, and making it one now is a change that reaches every call site.

### Box difference, not volume iteration

When a viewer crosses a chunk boundary, compute the difference between the old and new
boxes and iterate only the shells that entered and left. O(R²) per crossing rather than
O(R³). At a 16-chunk radius that is roughly a thousand chunks touched instead of thirty
thousand, every crossing.

Chunk quantisation also provides the hysteresis for free: the boxes only change when the
viewer crosses a chunk boundary, so standing on one doesn't thrash.

### Refcount per chunk, across viewers

View increments, unview decrements, zero unloads. Build this at M2 with exactly one viewer.
It is a few lines now and a rewrite of the streaming policy later, and it is what stops two
players from unloading each other's terrain.

**Cancel in-flight work when the count hits zero.** Without it, running somewhere and
turning around queues work for terrain nobody will ever see, and under continuous movement
the queue never drains.

### An explicit state machine

`Unloaded → Queued → Generating → Generated → Meshing → Meshed`, one enum on the chunk,
rather than a scatter of `bool`s that can express contradictory states. The dedup flag
lives here too — "already queued" is a state, not a `HashSet` probe, and cannot drift out
of sync with the thing it describes.

### The load margin is two chunks, and the second one was not in this plan

*Added 2026-08-27, while building M2c-1.* The stage barrier and the streaming radius
interact, and the plan above did not say how.

A chunk may only mesh once its voxels are final, which means every neighbour that could
write into it has finished stage 2. That neighbour can only reach stage 2 once *its*
neighbours have terrain. Two rings, not one:

| Ring | Reaches | Because |
|---|---|---|
| `R` | meshable | its neighbours out to `R+1` have finished Features |
| `R+1` | Features | its neighbours out to `R+2` have finished Terrain |
| `R+2` | Terrain | needs nobody — stage 1 is per-chunk |

The mesher's padded 34³ copy wanted the first ring anyway, so only the second is the stage
pipeline's own cost. It is not free: at radius 16 the loaded volume goes from 33³ = 35,937
chunks to 37³ = 50,653, or **41% more**. That is the honest price of seamless cross-chunk
features.

> **Measured at M2d, and it corrected this section.** The claim originally written here —
> that while stage 2 places nothing, a margin of 1 gives identical terrain for 30% fewer
> chunks — is wrong. The barrier is enforced regardless of whether stage 2 places anything,
> so dropping the margin shrinks the *meshable* radius by one on every axis. Margin 1 at
> r4h/2v measured identical to margin 2 at r3h/1v on every number: 847 tracked, 97 drawn,
> 1.37% of one core. **The margin is not a tuning knob** — view distance is, and it already
> exists.

**The payoff is that meshability needs no bookkeeping at all.** A chunk out in the margin
has neighbours beyond the margin that were never loaded, so it fails the barrier by
construction. The margin generates and never meshes because of the barrier itself — there
is one refcount over the expanded box and no second one tracking which shell a chunk is in.

### Split into M2c-1 and M2c-2

The two-session warning was taken up front rather than discovered halfway:

- **M2c-1 — the policy as pure data.** `ChunkBox` and box difference, `Viewer`, the chunk
  state machine, the refcounted `ChunkStreamer`, the stage barrier. No threads, no jobs,
  every property fuzzable against brute force.
- **M2c-2 — wiring to the scheduler.** Generate and mesh job types, cancelling on refcount
  zero, applying results, driving the stage pipeline. This is where refcounting,
  cancellation and async completion actually interact, and it starts on a policy whose
  invariants are already pinned.

### Tests

All headless: a viewer is Core data, so the entire policy is testable without Godot.

- Move a viewer one chunk and assert exactly the expected shell entered and exited — the
  box-difference test.
- Two overlapping viewers: a shared chunk survives one of them leaving.
- Leave and return before the load completes: no duplicate load, no lost chunk, no orphaned
  job.
- **The fuzzable invariant:** after any random sequence of viewer moves, the set of loaded
  chunks equals the union of the viewer boxes, and no chunk is in a state waiting on a job
  that will never complete. That single property catches most refcount and cancellation
  bugs, the same way coverage-equivalence did for the mesher.

---

## M2d — Unload path, bridge, and measurement

### Budget destruction, not just construction

The finding most likely to be dismissed until it bites. Freeing many meshes in one frame
stutters, and because the renderer defers the actual deallocation the stutter lands a frame
or two *after* the cause — so it gets blamed on meshing. godot_voxel needed a dedicated
runner plus a task whose entire body is releasing a mesh reference later, and their comment
about it is openly unhappy.

We draw through `RenderingServer` with explicit RIDs, so unloading means `FreeRid` in bulk.
Two main-thread budgets, and they are not the same mechanism:

- **Time-sliced** (a few ms per frame) for applying mesh results, where we can measure cost
  as we go.
- **"N per frame until drained"** for freeing RIDs, where we *cannot* measure it, because
  the cost isn't spent inside our call.

**Of those two budgets, only the second was built at M2d-1.** Applying mesh results is
still unbounded: if a hundred chunks finish meshing in one frame, a hundred `ArrayMesh`es
get built in that frame. Recorded rather than quietly dropped — and **M2d-2 collected on it**:
with generation 4.7× faster the same flythrough hands back 2.6× as many finished meshes,
update p99 went 1.14 → 2.04 ms, and one frame hit 27.6 ms against a previous worst of 6.9.
It stayed invisible only while generation was slow enough to meter it. This is now the next
thing to build.

### Split into M2d-1 and M2d-2

- **M2d-1 — unload path and measurement.** Done 2026-08-27. `MeshFreeQueue`, the profiler,
  the scripted flythrough, and the sweep. Results below.
- **M2d-2 — the cave optimisation** deferred from M2b. Done 2026-08-28. A world-anchored
  lattice sampled every 4 voxels and interpolated between, so a chunk costs 729 fBm
  evaluations instead of 32,768: generation went 2.23 → 0.47 ms/chunk in Release, and the
  same r12h/6v flythrough at 40 m/s drew 731 chunks against 145. M2d-1 had raised this from
  tidy-up to the highest-value item in M2, on the grounds that generation throughput caps
  sustainable view distance — which is exactly what the A/B then showed.

  Two things it taught that the plan did not anticipate. **The accuracy cost is not where
  you look for it:** coarsening only ever *loses* caves, because linear interpolation cannot
  exceed its endpoints and the threshold selects precisely the peaks that get clipped, so
  the step is a cave-volume knob and judging it by voxel disagreement picks the wrong
  default. And **the baseline the priority was argued from was a Debug number** — 6.55
  ms/chunk against 2.23 in Release. The argument survived because the 80% cave share was
  scale-free, but that was luck, not method. See WORKLOG.md.

### Finally answerable measurements — and what they answered

M1d deliberately left these open because one chunk proves nothing:

- ~~Memory and frame time at target view distance, and while flying fast enough to force
  continuous load and unload.~~ **Answered at M2d-1**, swept r2 to r12; table in
  WORKLOG.md. Main-thread cost plateaus near 3% of one core from r6 up, bounded by worker
  hand-back rate rather than box size. Memory is mesh-dominated at ~46 KiB per drawn chunk
  against ~2 KB of voxel storage, which makes horizontal radius expensive and vertical
  nearly free — the measured argument for the separate radii this document asked for on
  instinct.
- **Still open, and both need a windowed run** rather than a headless one, because they are
  rendering questions and headless has no renderer:
  - `RenderingServer` direct versus `MeshInstance3D` at real chunk counts.
  - Whether meshing 2×2×2 chunks into one surface cuts draw calls enough to be worth it —
    mesh blocks need not equal storage blocks. This only pays if draw calls rather than
    primitives are the cost, which is why `StreamingProfiler.RenderLine` prints both.
  - Whether the free budget actually removes the stutter. Headless has no deferred
    deallocation to spread, so M2d-1 could only show that the budget is not itself a
    bottleneck — peak queue depth stayed under 27 everywhere.

---

## Order and rationale

**M2a → M2b → M2c → M2d.**

M2a first: everything else sits on it, and the correction is freshest now. Worldgen can be
stubbed to flat terrain to exercise the queue.

M2b second because it is a pure function with no threading — headless-testable on its own,
and it gives the scheduler real work to schedule.

M2c third, needing both. It is the risky one.

M2d last, because honest measurement needs real chunk counts, which is the same reason M1d
deferred the `RenderingServer` question in the first place.

**Deliberately not in M2:** LOD (M13), persistence (M6), and the concurrent-writer question
for chunk storage (M3, trigger recorded in M2a above).
