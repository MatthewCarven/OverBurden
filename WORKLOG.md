# MinerCraft — Worklog

## 2026-08-27 — M2d-1: the unload path, and measurement that corrected a decision

261 tests passing, unchanged — this part is a Godot-layer change plus instrumentation.

### Built

`MeshFreeQueue` (budgeted mesh release), `StreamingProfiler` (percentile sampling), a
scripted surface-following flythrough, and command-line overrides — `--profile`,
`--radius`, `--vradius`, `--margin`, `--speed`, `--warmup`, `--measure` — so a sweep is one
shell loop rather than five edit-rebuild cycles. That cheapness is what made the finding
below possible at all.

### The load margin is not a tuning knob, and three documents said it was

The headline, and it reverses a claim written yesterday. M2c-1 recorded that "while stage 2
places nothing, a margin of 1 produces identical terrain for 30% fewer chunks", and made
the margin a constructor parameter so M2d could pick the cheaper one.

It is wrong. **The barrier is enforced whether or not stage 2 places anything**, so dropping
the margin does not buy cheaper terrain — it shrinks the *meshable* radius by one on every
axis. Measured:

```
margin 1, r4h/2v   →  847 tracked, 97 drawn, 1.37% of one core, 28.7 MiB
margin 2, r3h/1v   →  847 tracked, 97 drawn, 1.37% of one core, 28.8 MiB
```

Identical on every number collected. Margin 1 at a given radius simply *is* margin 2 at one
radius less. There was never a 30% saving to take: the same saving, with the same output, is
available by lowering view distance and keeping the margin correct for when features exist.

Corrected in `ChunkStreamer`, M2-PLAN.md, DESIGN.md and CLAUDE.md. The parameter stays, but
now documented as a test seam rather than a knob.

### Measured: view distance, flying at 40 m/s

```
config    chunks   drawn   update p99   main    jobs dropped   memory
r2h/1v       567      49      0.82 ms   0.97%           127    27.2 MiB
r4h/2v      1521     189      1.20 ms   2.50%           712    32.3 MiB
r6h/3v      3179     249      1.71 ms   3.34%          5166    35.5 MiB
r8h/4v      5733     283      1.26 ms   3.13%         14061    35.2 MiB
r10h/5v     9375     307      1.60 ms   2.94%         30266    35.7 MiB
r12h/6v    14297     205      1.24 ms   3.11%         56132    31.5 MiB
```

And the same large radii at a realistic speed:

```
r10h/5v at 6 m/s    9375    1462      1.21 ms   1.67%    2810    86.8 MiB
r12h/6v at 6 m/s   14297    2245      1.42 ms   2.97%    5244   122.1 MiB
r12h/6v at 15 m/s  14297     759      1.64 ms   3.24%   15016    56.0 MiB
```

### Findings

- **Main-thread streaming cost plateaus at about 3% of one core**, flat from r6 to r12
  across a 4.5× range in chunk count. It is bounded by how fast the workers can hand back
  results, not by how big the box is — so view distance is not a main-thread cost at all.
  Update p99 stays near 1.2–1.7 ms throughout.
- **The drawn count falling at r12 was a speed effect, not a ceiling, and I nearly wrote it
  up as one.** At 40 m/s r12 draws 205 chunks against r10's 307, which reads exactly like
  "past the useful limit". At 6 m/s the same r12 draws 2,245 and sits at 2.97% of a core.
  The sustainable view distance is set by the ratio of movement speed to generation
  throughput, and nothing about the radius itself. Worth having run the control.
- **Memory is mesh-dominated: ~46 KiB per drawn chunk against ~2 KB of voxel storage.**
  From the 6 m/s pair, 783 extra drawn chunks cost 35.3 MiB. This is the useful shape:
  horizontal radius is expensive because it adds *surface*, while vertical radius is nearly
  free because it adds sky and buried rock that mesh to nothing. DESIGN.md argued for
  separate radii from instinct; this is the number behind it.
- **Caves are what cap view distance.** At 6.55 ms/chunk with caves as 80% of it, generation
  throughput is the constraint that makes r12 fall apart at speed. The optimisation deferred
  from M2b is therefore not a tidy-up — it is the thing that would raise the sustainable
  view distance, which is exactly the setting a player will reach for.
- **`VoxelRenderInstance` never disposed its `ArrayMesh`.** It freed the instance RID and
  left the mesh to the GC finaliser — so the expensive half, the vertex-buffer deallocation
  that this whole part exists to control, was happening at times nobody chose. Budgeting the
  frees without fixing that would have controlled the cheap half, measured no improvement,
  and been thoroughly confusing.
- **Frame delta does not measure streaming, and the first profiler said so loudly.** It
  reported mean, p50, p95, p99 and max all within 0.05 ms of each other — not uniform work,
  but a main loop paced by something that is not us, with our costs invisible inside it.
  Replaced with direct timing around `ChunkPipeline.Update` and the free queue, which is the
  number that actually predicts stutter.

### The honest limit of this measurement

**The free queue's benefit cannot be demonstrated headlessly**, because headless Godot has
no renderer and therefore no deferred Vulkan deallocation to spread out. What the runs do
show is that the budget is not itself a bottleneck: peak queue depth never exceeded 27 in
any configuration, so nothing backs up. The queue is justified by the mechanism and by
godot_voxel's experience of the same problem, not by a number produced here. Confirming it
needs a windowed run, and belongs with the next visual check.

### Suggested view-distance presets

Grounded in the table above, at walking speed, and headless — so these carry no rendering
cost yet:

| Preset | Radii | Chunks | Memory |
|---|---|---|---|
| Low | r4h/2v | 1,521 | not measured at this speed |
| Medium | r8h/4v | 5,733 | not measured at this speed |
| High | r10h/5v | 9,375 | 86.8 MiB |
| Ultra | r12h/6v | 14,297 | 122.1 MiB |

Only the last two were measured at 6 m/s; the sweep ran the smaller radii at 40 m/s, where
too little gets meshed for the memory figure to mean anything. Interpolating from ~46 KiB
per drawn chunk would give plausible numbers for the first two, which is exactly why they
are left blank — a figure that looks measured and is not is worse than a gap.

## 2026-08-27 — M2c-2: wiring streaming to the scheduler

261 tests passing, up from 254. **M2 now runs end to end**: terrain streams around the
flycam through the job queue and the greedy mesher, and `Main.cs` no longer builds a chunk
by hand.

### Built

`ChunkPipeline` (work list, job submission, result application, cancellation),
`GenerateChunkJob` and `MeshChunkJob`, `PaddedChunk.CopyFrom` for the worker-side
neighbourhood gather, chunk retention in `ChunkStreamer` while a job is in flight, and the
Godot half — mesh handover and RID freeing.

### Measured, headless, from a standing start

```
r4h/2v + 2 margin, 8 workers
1521 viewed (1521 tracked), 166 drawn, 0 in flight, 0 queued
1926 jobs done / 0 dropped / 0 faulted   — settled inside 5 s
```

**1926 = 1521 generates + 405 meshes, and 405 is exactly the 9×5×9 visible box.** That is
the M2c-1 margin property holding in the real pipeline rather than in a test that drove the
stages by hand: the margin generated, and not one margin chunk meshed. `viewed == tracked`
is the other half — nothing was retained after its last viewer left.

166 of 405 meshed chunks produced geometry at all. The rest are open sky or buried rock,
which is the uniform fast path from M2b paying out again.

### A work list, not a per-frame scan

Deciding what each chunk needs by sweeping all of them is O(n) per frame with n in the tens
of thousands. Instead a chunk is pushed exactly when something might have changed for it —
it was viewed, its job reported, or a neighbour advanced a stage — with the dedup flag on
the chunk so pushing twice in a frame costs nothing.

The subtle part is that **advancing one chunk has to push 26 others**. A chunk finishing
terrain may be the last thing its whole neighbourhood was waiting on before any of them can
run their feature stage. Miss that push and the pipeline stalls silently with everything
idle and nothing wrong — which is the worst shape of bug this milestone could have had.

### Findings

- **`Queued` and `Generating` are only distinguishable because submissions are budgeted.**
  This nearly got collapsed into one state: the sim thread cannot observe a worker picking a
  job up, so "queued" and "running" look identical from here, and an unreachable state is
  exactly the contradiction the enum exists to prevent. What rescues it is
  `maxSubmissionsPerFrame` — a chunk that wants generating but has no slot this frame sits in
  `Queued`, and one with a job in flight is `Generating`. Both reachable, both meaningful,
  and the six states from the plan survive intact. Worth knowing before anyone "simplifies"
  them.
- **`onChunkViewed` has to fire on revival, not just on creation.** A chunk retained past its
  last viewer keeps whatever state its cancelled job left it in. If coming back only
  incremented the refcount, that chunk would sit in `Generating` behind a job that was
  already cancelled and never be looked at again — a permanent hole exactly where the player
  turned around. Now the callback fires on every 0→1 transition and says "wanted", not
  "created".
- **The fault path got simpler after the retry counter turned out to be untestable.** The
  first version capped retries at three. With `WorldGenerator` a concrete dependency there is
  no clean way to induce a fault, and an untestable branch guarding against a spin is worse
  than the simpler behaviour: report it, step back, do not retry. A deterministic bug — which
  is what a fault here is — reaches the same end state either way, and leaving and returning
  retries it naturally. Deleted the counter and the dictionary behind it.
- **Under fast movement nine jobs in ten are cancelled, and that is the system working.**
  The fuzz measured 394 completed against 3,693 dropped. Recorded as an assertion, because a
  version of this test that quietly stopped exercising cancellation would still pass every
  other check in it. A real player crossing one boundary at a time turns over a 49-chunk
  shell rather than a 343-chunk box, so this ratio is the stress case, not the normal one.
- **The mesh job gathers all 26 neighbours although only 6 are readable today.** Face
  visibility never reads a border voxel with more than one coordinate out of range, so edges
  and corners are dead weight — until M5, where vertex AO samples diagonally and a corner
  voxel is precisely what it reads. 392 extra voxels against 6,144 is cheaper than revisiting
  the gather later, and the mesher's merge key already carries the AO channel for the same
  reason.
- **Stage 2 runs on the sim thread, deliberately.** It writes into its neighbours by design,
  so putting it on a worker needs a lock over the whole 3×3×3 region — real work for a stage
  that currently places nothing. Revisit trigger is the one already recorded for chunk
  storage: M3, the first concurrent writer.
- **`PaddedChunk`'s own documentation still claimed the copy happens on the sim thread**,
  which M2a reversed a session earlier. Corrected. The class doc was the last copy of a
  decision that had already been superseded in three other places.

### Still open

- The unload path frees RIDs immediately. That is M2d's headline item and the one most
  likely to be dismissed until it bites, because the stutter lands a frame or two after its
  cause.
- **Confirmed on screen the same day.** Continuous rolling terrain, no seams, camera above
  ground. The diagnostic worth keeping: the bright and dark slivers on the slopes follow
  terrain *contours, not the chunk grid*, and that is what rules out a meshing or streaming
  seam — a real seam would land on the 8 m lattice. They are terracing risers lit by which
  way each contour segment faces, which is the low-ambient TODO item showing its symptom.
  Terrain scale matches the settings exactly: ~62 m features against a 72 m view box, so
  one hill fills the screen.

## 2026-08-27 — M2c-1: streaming policy, as pure data

254 tests passing, up from 216.

M2-PLAN.md flagged M2c as the part most likely to need two sessions, so it was split before
starting rather than halfway through: **M2c-1 is the policy with no threads and no jobs**,
every property fuzzable against brute force; M2c-2 wires it to the scheduler, which is where
refcounting, cancellation and async completion actually interact.

### Built

`ChunkBox` (half-open box of chunk positions, with the six-way difference decomposition),
`Viewer` / `ViewerId`, `ChunkState` and `StreamedChunk` (the state machine with a guard),
and `ChunkStreamer` (refcounts, box difference on move, the stage barrier).

### The load margin is two chunks, and the second one was not in the plan

The most useful thing this session produced, because it is a design hole that would have
surfaced at M13 as a visible seam rather than as a bug.

A chunk may only mesh once its voxels are final, which means every neighbour that could
write into it has finished stage 2. But a neighbour can only *reach* stage 2 once its own
neighbours have terrain to be written into. That is two rings, not one:

| Ring | Reaches | Because |
|---|---|---|
| `R` | meshable | neighbours out to `R+1` have finished Features |
| `R+1` | Features | neighbours out to `R+2` have finished Terrain |
| `R+2` | Terrain | needs nobody — stage 1 is per-chunk |

The mesher's padded 34³ copy wanted the first ring anyway, so only the second is the stage
pipeline's own cost — but it is a real one: at radius 16 the loaded volume goes from
33³ = 35,937 chunks to 37³ = 50,653, **41% more**. Made a constructor parameter rather than
a constant, because while stage 2 places nothing a margin of 1 gives identical terrain for
30% fewer chunks, and M2d should measure that rather than guess.

> **Corrected the next day — see the M2d-1 entry.** M2d measured it, and there was no 30%
> saving to take: the barrier applies whether or not stage 2 places anything, so margin 1 at
> a given radius is exactly margin 2 at one radius less. Left standing here as what was
> believed at the time, which is the point of a worklog.

### The barrier turned out to make a second refcount unnecessary

The obvious way to keep the margin from meshing is to track which shell each chunk is in — a
second refcount over the unexpanded box, kept in step with the first.

None of that is needed. **A chunk in the margin fails the barrier by construction**, because
its own outward neighbours were never loaded at all. Running the pipeline to a standstill
lands the meshable set exactly on the viewer's visible box, from one refcount and a
predicate. `TheMeshableSetIsExactlyTheVisibleBox` asserts precisely that, at three radii —
no wider, so nothing draws voxels a neighbour could still change, and no narrower, so there
is no hole at the edge of view.

This is the second time a barrier has paid for itself by deleting bookkeeping rather than
adding it. Worth a bias toward deriving state over tracking it.

### Findings

- **Intersecting before slicing is not a fast path.** Handling the disjoint case up front
  looks like an optimisation and is actually load-bearing: it guarantees the overlap lies
  inside the box on every axis, which is what lets all six slices skip clamping. Written the
  other way round — slice first, clamp as you go — a non-overlapping box emits slabs with
  inverted bounds. Same code, half the length, and correct instead of nearly correct.
- **Fuzz the decomposition against brute force, and assert both halves.** 2,000 random box
  pairs, checking that the emitted sub-boxes are pairwise *disjoint* and that their union is
  *exact*. Disjointness is the half that matters here: a position emitted twice is a
  refcount incremented twice, which is a chunk that never unloads — and it presents as a
  memory leak that isn't one. Exactness alone would not have caught it.
- **The refcount fuzz asserts counts, not set membership.** Set equality passes while counts
  drift, and the two drift directions are different bugs: high never unloads, low unloads
  terrain someone is standing on. Checking the counts costs one dictionary and separates
  them.
- **Self-transitions had to be rejected, and that immediately caught a bug in the unload
  path** — the first run threw, because a chunk viewed and released without ever being
  queued had sat in `Unloaded` the whole time and was being sent there again. A guard that
  fires on its author within a minute of being written is a guard worth having.
- **One test was wrong about the design rather than about the code.** It asserted a fresh
  chunk has `ViewCount == 0`; there is no such moment, because a chunk record only exists
  because a viewer asked for it. Fixed the test and wrote the reason next to it.

### Deliberately left for M2c-2

`ChunkStreamer.Unview` removes the chunk entry the instant the count hits zero. That is
correct while nothing submits jobs and wrong the moment something does — a worker still
holds the chunk and will report a result for something the streamer no longer knows about.
The removal has to become conditional on the outstanding-job count. The comment marking the
exact line is in place, so it is a scheduled change rather than a discovered bug.

## 2026-08-27 — M2b: worldgen

216 tests passing, up from 186.

### Built

`Hash64` (splitmix64), `GradientNoise` (2D/3D gradient noise plus fBm), `StrataTable`,
`WorldGenSettings` / `GenerationStage`, and `WorldGenerator`. Terrain is a pure function of
seed and position: heightmap, strata, caves.

### Measured, over 2,048 chunks — 128 m × 64 m × 128 m

```
4.0 MiB total, mean ~2 KB/chunk, against 128 MiB naive — 32× better
68% of chunks uniform (803 air, 541 solid), 704 palettised, 0 direct
6.55 ms/chunk with caves, 1.35 ms without — caves are 80% of generation
```

That 68% uniform figure is M1b's uniform tier paying off exactly as designed, without
worldgen doing anything clever: only the sky case is an explicit fast path, and the solid
chunks fall out of `Compact()` on the way past.

### Findings

- **Measuring immediately was worth it, because the first number was bad.** 9.48 ms/chunk
  would have made streaming pointless, and nothing in M2c would have revealed *why* — it
  would have looked like a streaming problem. Isolating caves (regenerate the same region
  with `CavesEnabled = false`) took one run and showed they were 84% of the cost.
- **Two of the three obvious cave costs were free to remove.** `Hash64.Combine` chained
  four avalanche rounds, and 3D noise hashes eight lattice corners per octave per voxel, so
  that sits in the innermost loop the generator has; one round over multiply-mixed
  coordinates is ample for choosing among sixteen gradients. And the gradient table was
  indexed with `% 12` — a 64-bit integer division per corner — where padding the table to
  sixteen entries makes it `& 15`. Together: 9.48 → 6.55 ms/chunk.
- **The remaining cost is inherent and deliberately deferred to M2d.** ~256,000
  hash-and-dot operations per chunk is what evaluating 3D fBm per voxel costs. The real fix
  is a coarser cave lattice with interpolation — at frequency 0.02 the field barely varies
  across a 32-voxel chunk anyway — but that is an optimisation project, not a worldgen one,
  and M2b is closed without it. Across ~6 workers this is ~1.1 ms of wall clock per chunk,
  which is fine for building M2c against.
- **The default cave threshold was picked from a distribution, not from intuition.** The
  first guess of 0.55 carved nothing, because normalised fBm at these settings actually
  spans only [-0.48, 0.58] with p99 at 0.50 — "roughly [-1, 1]" is true of the theoretical
  range and badly misleading about the practical one. Measured over 216,000 samples and set
  to 0.42, near p95, for about 5% of underground volume.
- **Changing the hash broke the pinned-constants test, which is the test working.** The
  optimisation altered `Combine`, so the world changed, and the failure said so loudly and
  immediately. That is exactly the alarm that test exists to raise — it just happened to be
  a deliberate change rather than someone reaching for `HashCode.Combine`. Re-pinned.

### Two tests were wrong about the world rather than about the code

Both worth recording, because both were me asserting something tidier than reality:

- **Adjacent columns are allowed to have the same height.** At 48 voxels of amplitude over
  a frequency of 0.004 the terrain is deliberately smooth, so neighbouring columns
  routinely round to the same integer. Asserting they differ tests the rounding, not the
  noise. Now compares distant columns.
- **Caves cluster, so "caves exist" cannot be asserted per chunk.** At the default
  frequency a 32-voxel chunk spans less than one noise lattice cell, so plenty of chunks
  legitimately contain no cave at all — 5% of global volume is not 5% of every chunk. The
  test now separates the two claims by scope: "caves only ever remove material" holds in
  every chunk, "caves exist" is asserted across a 25-chunk region.

## 2026-08-27 — M2a: the job scheduler

186 tests passing, up from 154. 27 of them are the scheduler, all headless in Core.

### Built

`JobPriority` (four byte-bands packed into a `uint`), `JobKind`, `ViewerSet`, `IJob` /
`JobResult`, `JobQueue` (the flat re-sorted list), and `JobScheduler` (dedicated worker
threads, results back through a `Channel`). Nothing submits jobs yet — streaming at M2c is
what will feed it.

### The test that justifies the whole part

`MovingTheViewerReordersAlreadyQueuedWork` submits a near job *first*, then a far one, then
moves the viewer across the map before either runs. A FIFO returns the near job because it
arrived first; the queue returns the far one, because that is now where the player is. That
is the entire argument against `Channels` in one assertion.

### Findings

- **The queue's ordering contract is "after a sweep", and two of my own tests were
  asserting otherwise.** `Enqueue` deliberately does not sort — per-insert ordering is the
  cost this design exists to avoid — so an un-swept queue pops newest-first. Both failures
  were the tests being wrong, not the code, but the property was folklore until they
  failed. Now documented on the class and pinned by
  `BeforeAnySweepTheQueueIsInArrivalOrder`. It is also harmless in practice: the chunk
  queued most recently is usually the nearest one, and the next sweep corrects it anyway.
- **`ViewerSet` came out better than the thing it was copied from.** godot_voxel uses a
  pre-sized never-resized array plus an atomic count, which leaves individual position
  reads able to tear — they accept that, because a torn read only means one job scheduled
  at a wrong priority. We get strictly better for free: reference assignment is atomic in
  .NET, so publishing a whole new snapshot array by swapping one reference gives readers a
  consistent view with no tearing at all. Same lock-free property, no torn reads, and the
  allocation is irrelevant because viewers move at most once a frame. Second time in two
  days that a C# guarantee has made a C++ workaround unnecessary — the first was GC
  replacing `shared_ptr` for chunk lifetime.
- **The fuzz test initially proved much less than it looked like it did.** It submitted 400
  jobs and disposed immediately, so nearly everything came back Dropped from the shutdown
  path rather than actually running — 27 ms for the whole job suite was the tell. The
  exactly-once invariant held, but almost none of the concurrent run/cancel/sweep
  interleaving was being exercised. Now it drains first and asserts that most jobs really
  ran; the suite takes ~290 ms and eight consecutive runs are clean.
- **Caught a callback-under-lock bug in review, before running anything.** `TryTake`'s
  success path fired the dropped-entry callback while still holding the queue lock —
  directly contradicting the comment three lines below it explaining why that must never
  happen. Restructured to collect inside the lock and report outside. This is the kind of
  deadlock that reproduces once a fortnight on a customer's machine.

### Note

Nothing in `MinerCraft.Core/Jobs/` references Godot; the only occurrences of the word are
comments citing godot_voxel. The seam holds, and the tests would stop compiling if it
didn't.

## 2026-08-27 — M2 split into four parts, and DESIGN.md corrected

No code. [docs/M2-PLAN.md](docs/M2-PLAN.md) written: **M2a** job scheduler → **M2b**
worldgen → **M2c** streaming policy → **M2d** unload path and measurement. Same shape as
the M1 split, for the same reason — the four parts fail in completely different ways.

Also pushed to `github.com/MatthewCarven/MinerCraft` today, private, default branch `main`.
The pre-push sanity check passed: `reference/` tracked zero files, and the whole repo is
63 files and 122 KiB, which is itself the proof the 51 MB of godot_voxel stayed out.

### DESIGN.md was still wrong

The Channels correction had gone into TODO.md and the reference notes but **not** into
DESIGN.md, which still listed `System.Threading.Channels` under "Godot 4 / C# specific
traps" as a locked decision. Fixed, and both the FIFO and the priority-heap alternatives
are now recorded under "Rejected alternatives" so neither gets re-litigated. Worth noting
the failure mode: a correction that lands in the working docs but not the decisions doc is
a correction that comes back.

### Two findings from the planning pass itself

- **`HashCode.Combine` would have silently randomised the world.** .NET randomises its
  seed per process, so `hash(seed, cx, cy, cz)` built on it generates *different terrain
  every launch* — while looking perfectly correct within any single session. It fails only
  across restarts, which is exactly when a player would notice their world had changed.
  M2b now specifies splitmix64 or xxHash written explicitly, pinned by a test that asserts
  known constants from a fresh process. This is the kind of bug that would have been
  blamed on the noise function for a week.
- **GC settles the padded-copy question that was reopened yesterday.** godot_voxel needs
  `shared_ptr` so a chunk isn't freed while a mesh job holds it; in C# holding a reference
  already does that, so half their machinery is free for us. What GC does *not* solve is
  mutation — but at M2 there is no concurrent writer, since mining is M3 and fluid is M8.
  So: copy on the worker thread, which keeps the sim thread free and skips the copy
  entirely for cancelled jobs. **Revisit trigger recorded at M3**, where the first writer
  appears and needs either a per-chunk lock or a copy-on-write snapshot.

### Notes on the plan

- **M2c is flagged as likely to spill into two sessions.** Refcounting, in-flight
  cancellation and async completion interact, and the bugs are state-machine bugs that
  only appear under movement. M1c carried the same warning and didn't need it; this one
  probably will.
- Each part gets a single fuzzable conservation invariant, following the M1c lesson that
  one well-chosen property beats several narrow assertions. For the scheduler: every job
  reports exactly one result, never zero, never two. For streaming: the loaded set equals
  the union of viewer boxes, with no chunk waiting on a job that will never complete.
- **Flying fast is the test.** A streaming system standing still proves nothing; the
  measurements in M2d are specified under continuous load and unload.

## 2026-08-26 — Read godot_voxel's scheduling and streaming before M2

No code. Full notes in [docs/M2-REFERENCE-NOTES.md](docs/M2-REFERENCE-NOTES.md), with the
file list; TODO.md's M2 section rewritten around them.

**The reference earned its keep a second time.** Before M1b it caught the chunk storage
order. This time it caught that the M2 job queue was specified as the wrong data structure,
which would have been discovered only after the streaming code was built on top of it.

### The catch

TODO.md said "`System.Threading.Channels` job queue for gen + mesh". Channels is FIFO, and
a FIFO cannot do the two things a voxel job queue exists to do: **reprioritise queued work
as the player moves**, and **drop work that became irrelevant before it ran**. godot_voxel
uses neither a FIFO nor a heap — it keeps a flat vector re-sorted on a 200 ms timer, popping
from the back, with the same sweep recomputing priority and removing cancelled tasks.

A priority heap was my next instinct and is wrong for the same reason: it orders correctly
at insert time, and insert time is precisely when the ordering is least valuable. A chunk
queued at 20 m may be 200 m away before a thread frees up. Their `get_priority()` is a
virtual polled on an interval, not a number captured on push.

### Other findings that change the plan

- **Mesh *destruction* is a stutter source and we are unusually exposed to it.** They
  needed a whole third task runner for it, plus a task whose entire body is dropping a
  mesh reference later, because the renderer's deallocation cost is deferred and therefore
  unmeasurable from the caller. Their comment is candid: *"I hate this workaround because
  it feels like we are almost not in control of a stable framerate."* We draw through
  `RenderingServer` with explicit RIDs, so unloading chunks means freeing RIDs in bulk. The
  stutter arrives a frame or two after its cause, so it would have been misdiagnosed as a
  meshing problem. Now an explicit M2 task.
- **Three execution contexts, not one.** Worker pool (priority-ordered), a main-thread
  time-sliced runner (8 ms/frame), and a main-thread "N per frame until done" runner for
  costs you cannot measure. "Off the main thread" is not a sufficient plan.
- **One pool with a `serial` flag, not two pools.** I/O runs serially because it contends
  on shared resources anyway, and a second pool would make disk work compete in a separate
  ignorant queue instead of one ordering. Relevant at M6.
- **Streaming is a box difference over a refcounted viewer set.** Only the shell that
  entered or left view is iterated — O(R²) per boundary crossing rather than O(R³). Chunks
  are refcounted per viewer, which is also how multiplayer avoids being a special case.
  Building the refcount at M2 with one viewer is cheap; retrofitting it is a rewrite.
- **View distance must not be one scalar.** They keep a separate vertical ratio to avoid
  loading useless sky. We probably want that inverted — a mining game wants a tall narrow
  loaded volume, because the content is below the player.
- **Reopened an M1b leftover.** Our note says to build the padded 34³ copy on the sim
  thread when queueing. They hold references to the chunk and its 26 neighbours and copy
  *on the worker*, which keeps the sim thread free and skips the copy for cancelled jobs —
  but requires storage to be readable while the sim thread may write. A real tradeoff, not
  a free win. Deferred to M2 planning rather than decided here.
- **Cancellation is not deletion.** A dropped task still reports back (`TYPE_DROPPED`), or
  the chunk stays flagged as "being meshed" forever and never updates again.

### Patterns worth stealing outright

- Priority packed as four `uint8` bands in one `uint32`, so comparison is one integer
  compare.
- Viewer positions read **without a lock**, deliberately — priority is a heuristic, and
  paying for exactness is waste. Made safe by a pre-sized never-resized array plus an
  atomic count, not by the absence of races.
- Shared config invalidated by **replacement plus a `valid` flag** rather than mutation
  under a lock. In-flight tasks see the stale flag and discard their output. This composes
  neatly with our "registry frozen after load" rule: a reload makes a new frozen registry.
- Thread-local batching of enqueues, flushed once per frame, so the queue mutex is taken
  once per frame rather than once per chunk.

### Where they are unhappy with their own code

Worth recording, because these are the places not to copy:

- Distance-based task dropping is called "not always reliable" with a TODO to replace it
  with cancellation tokens. We should go straight to `CancellationToken`.
- Postponed tasks effectively spin: *"Those postponed tasks will sort of spinlock with no
  sleeping. Is that a bad thing?"* — an open question in their own source.
- A known mutex hotspot when tasks are very short. Our jobs are chunk-sized, so probably
  not our problem, but it says where to look first if the pool underperforms.

## 2026-08-26 — Licence: PolyForm Noncommercial 1.0.0

No code. [LICENSE.md](LICENSE.md) written, with README, TODO and CONTRIBUTORS updated to
match. Matthew delegated the choice.

### The argument

The decision looked like a values question and turned out to be a timing question.

**Loosening a licence is one commit; tightening one is impossible.** Anyone who receives
a copy under MIT keeps those rights permanently, so "start permissive, restrict later if
it gets serious" is not a strategy that exists. The reverse works fine. While one person
holds the whole copyright, the cost of picking too restrictive is therefore zero and the
cost of picking too permissive is unrecoverable. That asymmetry decides it without
needing to predict whether this is ever sold.

PolyForm Noncommercial over the alternatives because it says the actual intent in
readable English — read it, fork it, mod it, share it, don't sell it — in one short file.
BUSL-1.1 was the other serious candidate and has a genuinely nice property (automatic
conversion to Apache-2.0 after four years), but it is drafted for SaaS, and its
Additional Use Grant would need custom wording to make sense for a game.

### Findings

- **Nothing constrains the choice.** Shipped dependencies are Godot (MIT) and .NET 8
  (MIT); xunit and coverlet are test-only and never distributed. `reference/` is
  gitignored and never built against — the comment already sitting in `.gitignore` about
  not inheriting an attribution obligation turned out to be load-bearing.
- **A split code/content licence is premature.** It is the right long-term shape for a
  moddable game — permissive engine, restricted content, so the reusable part is reusable
  and nobody can ship *your* game. But every texture is currently procedural and there
  are no art assets at all, so there is nothing to split yet. Filed as a revisit trigger
  at first real assets rather than done now.
- **GPL was rejected on the mod story, not on principle.** MIT→GPL is legally fine and
  Godot's licence permits it. But whether runtime-loaded mods become derivative works is
  unsettled, and the base game shipping as a mod is a design pillar here. A licence that
  makes third-party mod authors think twice is the wrong licence for this project.
- **The licence is not the moat, and CONTRIBUTORS.md had already spotted why.** It
  records that AI-generated work sits in unsettled copyright territory. A licence can
  only restrict what is protectable, and a meaningful share of this code was written by
  Claude. What actually stops someone shipping a clone is the name and the content, not
  copyright over the engine source. Worth knowing before treating LICENSE.md as armour.
- **The freeze point is the first merged PR, not publication.** Publishing changes
  nothing about the ability to relicense; merging someone else's commit does. That is the
  real deadline, and it is now recorded as the first revisit trigger. A DCO sign-off
  requirement keeps the door open at almost no friction.

### Note on sourcing

The licence text was fetched from the canonical repository and diffed against what was
written, rather than reproduced from memory — a subtly wrong licence is worse than no
licence. Two obvious raw URLs 404'd because that repository's default branch is named
`1.0.0` rather than `main` or `master`. `LICENSE.md` is byte-identical to canonical apart
from the `Required Notice:` copyright line PolyForm expects the licensor to add.

## 2026-08-25 — M1c and M1d: greedy mesher, and a real chunk on screen

154 tests passing. M1c was flagged as the part most likely to spill into two sessions;
it didn't, largely because the test strategy turned out stronger than expected.

### Measured, on a chunk of test terrain

```
chunk stored as palette 4 @ 2b, 8,208 B for 32,768 voxels
naive 196,608 faces → culled 6,128 → greedy 850 quads
7.2× better than culled, 231× better than naive
```

### Built

`PaddedChunk` (34³), `MaterialTable`, `VoxelFace`, `MeshBuffer`/`ChunkMesh`,
`GreedyMesher`, and `ChunkMeshConverter` on the Godot side. The M0 block builder and the
Game-side face table are deleted — Core owns that now.

### Findings

- **The coverage-equivalence test is better than the two invariants it replaces.** The
  plan called for surface-area conservation and a no-overlap check. Expanding every
  emitted quad back into the voxel faces it covers and comparing that to the culled
  mesher's answer *as a set* subsumes both, and additionally catches faces invented in
  the wrong place — which neither original invariant would have. It reads the actual
  emitted vertex data rather than any internal side-channel, so it tests what ships.
- **The U and V axes per face are not free choices.** They must satisfy
  `du × dv = +normal`, or that one face renders inside-out while the other five look
  correct. Derived per face rather than guessed, and pinned by a winding test — the M0
  experience of not being able to check winding without running it was worth avoiding
  twice.
- **Clearing the mask as quads are emitted makes overlap structurally impossible**, not
  merely unlikely. Worth stating in the code, because it is the kind of property that
  gets refactored away by someone optimising the clear loop.
- **`ArrayPool` was the wrong call in the plan.** Pooling earns its keep when a buffer's
  lifetime is unpredictable; here the mesher owns its buffers for its whole life and the
  Godot bridge copies out of them, so plain reuse via `Reset()` is both simpler and
  strictly less work. Steady state allocates nothing either way.
- **The reference implementation in the tests is deliberately a second copy** of the
  visibility rule rather than a call into the mesher's. A reference that shares code with
  the thing it checks proves nothing.
- **Emitting Godot-free output cost one conversion loop and bought the headless tests.**
  That conversion is also what makes buffer reuse safe, since Godot copies rather than
  retaining.

### Note

`stackalloc` inside a nested loop in a test helper tripped CA2014 — a real stack-overflow
risk rather than a style nit. Hoisted out.

## 2026-08-25 — M1b: chunk storage

133 tests passing. `VoxelChunk` holds 32³ voxels in one of three representations, with
the differential fuzzer running 160,000 random operations against a naive `ushort[]`.

### Built

`VoxelChunk` — Uniform (single int, zero allocation) → Palettised (1/2/4/8-bit packed
indices) → Direct (`ushort[]`), promoted as content demands, with `Compact()`,
`Fill()` and a bulk `CopyTo`.

### Findings

- **The 16-bit palette tier the plan called for is pure overhead, and got dropped.** A
  16-bit palette index costs exactly as much as the material ID it points at, so above
  256 entries the palette buys nothing at all and Direct storage is the honest answer.
  Caught while writing `BitsForPaletteSize`; docs/M1-PLAN.md had specified 1→2→4→8→16.
- **Zero-initialised packed storage is already correct on promotion.** When a uniform
  chunk takes its first differing write, the new palette puts the old uniform value at
  index 0 — so a fresh zeroed word array already reads back as the value every voxel
  used to hold. No fill pass, no loop.
- **Every width we use divides 64 exactly**, so the "pad, don't straddle" decision costs
  nothing whatsoever rather than the few wasted bits per word I had expected when
  planning it. The tradeoff turned out to be free.
- **The memory claim needed correcting, not the code.** A test asserted a maximum-entropy
  chunk fits in 16 KB and it came to 16,420 B — the packed array is exactly 16 KB and I
  had forgotten the palette. Worse, I had called that chunk "typical" when every voxel
  being independently random across 9 materials is the 4-bit *worst* case. Split into
  three honest tests: max-entropy (exact bytes), terrain-like (2 bits, ~8 KB), and the
  one that actually explains the budget — **the chunks that dominate a world are entirely
  air or entirely stone and cost zero bytes**. The palette tier only pays for the thin
  shell between them.
- **Compaction is genuinely not eager, and there is a test that says so.** Mining a lone
  block back out leaves the chunk palettised rather than snapping back to uniform,
  because detecting that needs a 32,768-voxel scan and a pick swing must not pay for it.

### Note

`InternalsVisibleTo` added for the test project, so packing internals are tested
directly rather than being promoted to public API just to be reachable.

## 2026-08-25 — Reading godot_voxel, and a storage-order correction

Sources cloned into `reference/godot_voxel` (51 MB, gitignored, repo stays clean). Read
`storage/` ahead of schedule because M1b is next and that is exactly what it covers.

### Corrected: chunk storage order was wrong

They store `[z][x][y]` — **Y contiguous** — with the comment "because it allows faster
vertical-wise access (the engine is Y-up)", and `fill_3d_region_zxy` confirms it
throughout.

M0 chose the opposite, X contiguous, and the justification written down at the time was
*"Y-major keeps a horizontal slab together for the lighting column pass"* — which is
self-contradictory. A column pass wants a **column** contiguous; Y-major makes a
horizontal slab contiguous, which is precisely the opposite.

It matters more here than it would in a generic voxel engine, because nearly every
mechanic in MinerCraft is vertical: skylight propagates down columns, water falls,
structural support propagates up from bedrock, groundwater drains downward, terrain
generation writes a column at a time. All of those would have strided a full 1024-element
slab per step.

Changed to `[z][x][y]`. 100 tests pass, including the bijection test, which independently
confirms the new order is still a valid permutation with no aliasing. Added a test pinning
the exact strides so a future reordering has to argue with the reasoning first.

Cheap today, expensive after M1b — which is the whole argument for having read this now
rather than at M2.

### Confirmed: our palette tier is ours to add

Their `VoxelBuffer` has only `COMPRESSION_NONE` (raw 8/16-bit array) and
`COMPRESSION_UNIFORM`. No palette, no bit-packing. That is correct at their scale — a
16³ block of 8-bit voxels is 4 KB — and would not survive 64× the voxel count. So M1b
takes their uniform fast path and adds the palette tier they do not need, rather than
copying their storage design wholesale.

Nice detail worth stealing conceptually: a uniform channel stores its default value *in
the union slot where the data pointer would be*, so a uniform chunk allocates nothing at
all. In C# that is just two fields with no real saving, but "uniform means no allocation"
is the right invariant to hold.

## 2026-08-25 — Decision: build our own voxel engine

Matthew asked the right question at the right moment: should we just use
[Zylann/godot_voxel](https://github.com/Zylann/godot_voxel) instead of building this?

Decision recorded in
[ADR-0001](docs/adr/ADR-0001-build-our-own-voxel-engine.md): **build our own, read
theirs.**

### Why the timing mattered

Almost nothing was sunk cost. M1a's registry is independent of voxel storage and survives
either way; the coordinate types largely survive; only ~200 lines of block mesh builder
were throwaway. After M1c that stops being true, so this was close to the last cheap
moment to ask.

### What decided it

- **Adopting it would reverse the headless-Core decision.** `MinerCraft.Core` has no
  Godot reference on purpose; godot_voxel puts world storage inside the engine.
- **The game is the simulation, not the terrain.** Phase 2 needs fast direct access to
  voxel arrays plus its own per-voxel fields. A library is worth adopting when it owns
  something peripheral — this would own the thing the product is made of.
- **It is a C++ module and GDExtension is still incomplete**, so realistically we would
  be compiling a custom Godot instead of using the stock 4.7.1 mono build.

### The cost, stated plainly

Six to ten sessions rebuilding something that exists and is better tested, plus giving up
Transvoxel LOD for free. Not a free choice. The justification is that Phase 1 is the
*known* part and the project risk lives in Phase 2, where the library helps not at all.

Revisit triggers are named in the ADR rather than left to mood: M1c or M2 exceeding ~3
sessions each, and M13's LOD work.

### Housekeeping

`reference/` added and gitignored, for third-party sources we read but do not vendor.
Keeps the repo small and avoids inheriting an attribution obligation we do not need.

## 2026-08-24 — M1a: identity and the registry

99 tests passing. The base game now loads from `mods/minercraft/materials/*.json`
through the same code path a third-party mod would use, and the block on screen takes
its textures from those files rather than from a hardcoded enum.

### Built

- `Identifier` — namespaced, validated, lowercase-only.
- `Registry<T>` — seedable numeric ID assignment, freeze semantics, placeholder fill.
- `MaterialDefinition`, `ModManifest`, `IContentSource` (directory + in-memory),
  `ContentLoader`, `GameContent`.
- `mods/minercraft/` with air, stone, dirt, grass.

### Findings

- **The numeric ID space has to belong to the world, not the process.** Assigning
  numbers in registration order at startup looks correct until a mod is installed, at
  which point every number shifts and every saved chunk decodes to the wrong material.
  `IdStabilityTests.WithoutASavedTable_RemovingAModRenumbersEverythingAfterIt` pins that
  failure as a test on purpose, so the hazard stays visible rather than becoming folklore.
- **Reserving air's *number* is not the same as privileging air's *content*.** The
  tension was real: air must be ID 0 for the uniform-chunk fast path, but the dogfood
  rule says no privileged content path. Splitting the two resolves it — the loader
  reserves the number before scanning mods, and the definition still has to arrive from
  `air.json` or it is a load error. That also makes air's ID independent of filename
  ordering, which a test now pins.
- **The placeholder for a removed mod must be solid, not air.** Turning missing blocks
  into air would quietly punch holes through every structure built from them. It is
  opaque, non-replaceable, and retains the original identifier — that last part is what
  makes re-installing the mod lossless rather than merely non-crashing.
- **Load errors are collected, not thrown.** One mod with a malformed file must be a
  diagnosable message, not a black screen. Only a missing air definition is fatal.
- **Load order has to be deterministic even before it is dependency-correct.** Numeric
  IDs for unseen content are assigned in load order, so a non-deterministic scan would
  hand out different IDs on different runs. Base game first, then ordinal by mod ID.
- **A bare `"id": "copper_ore"` resolves into the owning mod's namespace**, so a mod
  cannot accidentally claim someone else's; writing the explicit `ns:path` form is how a
  compatibility patch deliberately targets another mod, and a genuine collision is
  reported rather than silently overriding.

### Deferred

- Dependency declarations are validated for presence but not topologically sorted. Real
  resolution belongs with scripting at M12, when mods can actually conflict.
- `res://mods` resolves to a real directory in the editor and debug builds but not inside
  an exported `.pck`. Packaging loose mod content is a genuine problem; it just is not
  this milestone's problem.

## 2026-08-24 — M0: skeleton

Builds clean, runs headless with no errors, 43 tests passing.

### Built

- `.slnx` solution, three projects. **`MinerCraft.Core` has no Godot reference and must
  never get one** — that absence is the whole multiplayer seam, and a test project that
  exercises Core without Godot is what keeps it honest.
- `VoxelConstants`, `VoxelPos`, `ChunkPos` in Core. Chunk/local decomposition by
  arithmetic shift and mask.
- `Texture2DArray` + `Voxel.gdshader`. Layer index in UV2.x, light×AO in UV2.y.
- Culled block mesh through `RenderingServer`, no `MeshInstance3D` anywhere.
- Flycam, `.gitignore`, `start.cmd` (prefers a vendored Godot, falls back to `GAME/`'s).

### Findings

- **Negative coordinates are the whole reason the coordinate types exist.** Truncating
  division puts voxel -1 in chunk 0 next to voxel 0, which tears a chunk-wide seam
  through the origin. Because chunk size is a power of two, `x >> 5` is the correct
  floor division and `x & 31` the correct positive remainder — both branch-free and both
  already right for negatives. Most of the 43 tests are there purely to pin this down;
  every one of them passes trivially in the positive octant.
- **Godot C# projects don't enable `ImplicitUsings`** (matching `GAME/`), so `System` and
  `System.Collections.Generic` need explicit imports. Cost one failed build.
- **Winding verified analytically, then confirmed on screen.** For all six faces the
  right-hand-rule normal of the emitted triangles points inward, consistently — so the
  block could only render all-correct or all-inside-out, never a mix, and a mixed
  winding bug would have been the nasty one. Matthew ran it and it is correct: Godot's
  clockwise front-face convention is as documented, no flip needed.
- **The screenshots settled four things at once**, which is a good argument for looking
  at the thing rather than only testing it: winding, texture-array layer indexing (three
  distinct materials on one mesh), per-vertex normals (the lit and unlit faces differ
  under `diffuse_lambert`), and the UV2.y shade channel multiplying through at 1.0.
- **The 4×4 tile grid is visible on each face**, which is culled meshing showing its
  working: 16 quads per face, one per voxel. Greedy meshing collapses that to 1 quad per
  face at M1 — 96 quads down to 6 on this shape, a 16× reduction, and the clearest
  possible demonstration of why greedy is mandatory at 0.25 m.
- **Culling maths checks out**: a 4³ solid block emits 192 triangles (six 4×4 faces)
  against 768 for naive per-voxel cubes.

### Deliberately deferred

- Textures are generated in code, so there are no binary assets yet. Real images arrive
  with the registry at M1.
- `RenderingServer` direct vs `MeshInstance3D` is still an assumption, not a
  measurement. The measurement belongs at M2 when there are real chunk counts.

## 2026-08-23 — Design session

Planned the whole thing end to end with Claude. No code written, by request.
Output: [docs/DESIGN.md](docs/DESIGN.md), [TODO.md](TODO.md).

Started as "a Minecraft clone", ended as something meaningfully different.

### Decided

- **Godot 4 + C#.** Chosen over Python/moderngl (GIL caps threaded meshing and render
  distance), JS/three.js, and raw C++/Rust. Godot 4.7 mono is already on disk in
  `GAME/`. Real threads for meshing and worldgen; renderer and input for free.
- **0.25 m voxels, 4 per metre.** 64x Minecraft density, chosen so a pick swing carves
  material rather than deleting a cube. Rejected 0.125 m (512x — lighting and remesh
  become the dominant project constraint) and the hybrid "1 m blocks that subdivide
  when damaged" (two world representations poison the mod API).
- **32³ chunks = 8 m cube.** Small enough that remeshing on a pick swing is cheap.
- **No mobs, no combat, no pathfinding.** Replaced with simulation pressure.
- **Data mods from M1, MoonSharp Lua at M12, C# DLL loading never.** Namespaced string
  IDs are the one decision that makes or breaks it, and it gets made in week one.
- **Multiplayer not now, but not designed out.** Headless sim core plus the event bus
  keeps the seam open.

### The turn that made the project

Claude pitched "the mine is the enemy" — structural integrity and cave-ins instead of
zombies. Matthew took it, kept automation as well, and added **rainfall-driven flooding**
on top.

That third addition is what closed the loop. The three systems aren't three features,
they're one mechanic:

> Rain fills basins → water finds your excavation → saturated rock loses cohesion →
> the ceiling comes down → you build pumps and shoring to fight back → which needs
> power → which needs more mining.

Everything downstream now has a reason to exist. Every machine in the automation tree
fights either water or gravity. Design brief collapsed to one line: **you are a mining
operation fighting water and gravity.**

### Findings worth keeping

- **Greedy meshing barely cares about voxel scale.** Output scales with surface area,
  not voxel count, so the same hillside at 0.25 m merges to roughly the same quad count
  as at 1 m. What gets 64x worse is *scanning* the volume — which is exactly what
  palette compression makes cheap. So greedy meshing goes from "nice optimisation" to
  "mandatory", and pays for itself.
- **Palette + bit-packed storage stops being an optimisation at this scale.** Raw is
  >300 MB, palette-compressed with a uniform-chunk fast path is ~40 MB. Day-one
  requirement.
- **Lighting is the real bill at 0.25 m, and the fix is to not pay it.** Propagate on a
  coarse 1 m grid and sample trilinearly at mesh time — 64x cheaper, and light is
  low-frequency enough that nobody can see the difference. Has to be decided up front
  because it's a mesher and shader decision.
- **Minecraft's fluid model is not worth inheriting.** No volume conservation, never
  equalises, infinite sources, and its stair-stepping would be 4x more visible here.
  Replaced with a three-tier hybrid: analytic watershed basins for rain and standing
  water, volume-conserving cellular automata on an active-cell set for water that's
  actually moving, and a coarse groundwater saturation field for seepage. Different
  scales, different solvers. Sleeping cells are load-bearing, not an optimisation.
- **Saturation degrading support distance is the keystone.** It means water doesn't just
  flood the mine, it *causes the cave-in*. One interaction, two systems, and it's what
  makes the loop feel like a single game rather than a features list.
- **Decouple storage granularity from placement granularity.** Store at 0.25 m, mine by
  carving spheres, but place with 1 m brushes — placing individual sub-voxels would be
  miserable. Falls out naturally into volumetric inventory (litres of stone, not "3
  stone blocks"), which makes crafting costs continuous.
- **Rain occlusion is nearly free.** Reuse the skylight value the lighting system already
  computes to kill particles underground. It's the detail that sells the effect.

### Open

- Godot's `RenderingServer`-direct path vs `MeshInstance3D` nodes needs a real
  measurement at target chunk count, not an assumption.
- Collapse debris representation: loose re-settling rubble (cheap) vs rigid falling
  chunks (better looking, much harder). Deferred to M9.
- Power model complexity — deliberately unspecified until M10.

### Flagged for Matthew

- **Not a git repo.** Wants `git init` before the first code lands.
