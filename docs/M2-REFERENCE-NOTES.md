# M2 reference notes — godot_voxel's threaded scheduling and streaming

> Findings from reading `reference/godot_voxel/`, 2026-08-26. Read as reference only;
> nothing here is copied code, and `reference/` stays gitignored and out of every
> `.csproj` — see [ADR-0001](adr/ADR-0001-build-our-own-voxel-engine.md).
>
> Files read: `meshers/mesh_block_task.{h,cpp}`, `util/tasks/threaded_task{,_runner}.h`,
> `util/tasks/threaded_task_runner.cpp`, `util/tasks/task_priority.h`,
> `util/tasks/{time_spread,progressive}_task_runner.h`, `engine/priority_dependency.{h,cpp}`,
> `engine/{meshing,streaming}_dependency.h`, `engine/buffered_task_scheduler.{h,cpp}`,
> `engine/voxel_engine.{h,cpp}`, `engine/voxel_engine_updater.cpp`,
> `terrain/fixed_lod/voxel_terrain.cpp`, `terrain/voxel_viewer.h`, `terrain/free_mesh_task.h`.
>
> `fixed_lod` is the analogue to read, not `variable_lod` — MinerCraft has no LOD until M13.

---

## The headline: our job queue is specified as the wrong data structure

[TODO.md](../TODO.md) says *"`System.Threading.Channels` job queue for gen + mesh."*
That is wrong, and it is wrong in a way that is expensive to discover after the streaming
code is built on top of it.

**Channels is FIFO.** godot_voxel deliberately does not use a FIFO, and does not use a
priority heap either. It keeps a **flat vector that is periodically re-sorted**, and pops
from the back. The reasoning is in a comment in `threaded_task_runner.cpp`, and it is
about the player moving:

> The point to keep sorting after tasks have been inserted is in case there are lots of
> pending tasks, which can take more than a few seconds to be processed. A player can move
> fast and the priority location can change. Some tasks can even become irrelevant before
> they are run, so we may remove them from the list so they don't slow down the process.

Three properties fall out of that, and a FIFO has none of them:

1. **Priority is not fixed at enqueue time.** A chunk queued while it was 20 m away may be
   200 m away by the time a thread is free. `IThreadedTask::get_priority()` is a virtual
   re-polled on an interval, not a number captured on insert.
2. **Tasks can be dropped before running.** The same sweep that recomputes priority also
   calls `is_cancelled()` and removes tasks from the queue entirely. In a mining game where
   the player tunnels back and forth, a large fraction of queued mesh work becomes garbage
   before it runs.
3. **Re-sorting is amortised, not per-insert.** The sweep runs on a timer
   (`set_priority_update_period`, **200 ms** in practice), not on every push. A binary heap
   would pay per-insert for an ordering that is stale by the time it matters.

A heap is the obvious first instinct and it is also wrong, for the same reason: it orders
correctly at insert time, and insert time is exactly when the ordering is least valuable.

**What we should build instead:** a `List<T>` (or array) of job records behind a lock, with
a periodic sweep that recomputes priority, drops cancelled jobs, sorts, and lets workers pop
the tail. Channels may still be the right tool for the *completed-results* hand-back to the
main thread, which genuinely is FIFO and genuinely doesn't need reprioritising.

---

## Priority

`TaskPriority` is a **union of four `uint8` bands over one `uint32`**, so comparing two
priorities is a single integer compare rather than a tuple comparison:

| Band | Meaning | Notes |
|---|---|---|
| `band0` | distance to nearest viewer | lowest precedence; `BAND_MAX - (distance >> 4)` |
| `band1` | LOD index | irrelevant to us until M13 |
| `band2` | task class (mesh / load / generate) | how work types are ordered against each other |
| `band3` | default / reserved | highest precedence |

Bands are ordered so that `band3` beats `band2` beats `band1` beats `band0`. For M2 we only
need `band0` (distance) and `band2` (job class), but the packing trick is worth stealing
outright — in C# a `uint` with four byte fields compares with one instruction.

**Viewer positions are read without a lock, deliberately.** From
`priority_dependency.h`:

> It's only used to adjust task priority so using a lock isn't worth it. In worst case
> scenario, a task will run much sooner or later than expected, but it will run in any case.

The vector is pre-sized (64 entries) and **never resized** after creation; an
`atomic_uint32_t` count bounds the valid range. That is what makes the unlocked read safe
rather than merely fast. This is a good pattern for us: priority is a heuristic, and paying
for a lock to make a heuristic exact is wasted.

Worth knowing that they are not happy with the *cancellation* half of this. `get_priority()`
has a side effect — it sets a `_too_far` flag that `is_cancelled()` later reads — and the
header carries a TODO wanting to replace `drop_distance_squared` with explicit cancellation
tokens, calling the current scheme "not always reliable". We should go straight to
cancellation tokens (`CancellationToken` is already idiomatic C#) and skip the intermediate
design they regret.

---

## One pool, two lanes — not two pools

There is a single `ThreadedTaskRunner _general_thread_pool`. The distinction between CPU
work and I/O work is a **per-task `serial` flag**, not a separate pool:

```
push_async_task(t)     → enqueue(t, serial=false)   // mesh, generate: run in parallel
push_async_io_task(t)  → enqueue(t, serial=true)    // load, save: one at a time
```

The comment explains why: I/O tasks *"usually can't run well in parallel due to locking
shared resources"*, and running them serially *"avoids clogging up all threads with waiting
tasks."* One `bool _is_serial_task_running` guarded by the queue mutex enforces it.

This matters at M6 when SQLite persistence arrives. The instinct will be a second thread
pool for disk; the cheaper answer is one pool and a serial flag, which also means disk work
competes for priority against mesh work in a single ordering rather than in two ignorant
queues.

**Thread count:** `clamp(round(0.5 × hw_threads), 1, max(hw_threads - 1, 1))` — roughly half
the cores, never all of them, never zero. Godot's renderer and our main thread need room.

---

## Three runners, because "off the main thread" isn't enough

This is the part most likely to be underestimated. godot_voxel has **three** distinct
execution contexts, not one:

| Runner | Where | Budget | For |
|---|---|---|---|
| `ThreadedTaskRunner` | worker threads | priority-ordered | generation, meshing, disk |
| `TimeSpreadTaskRunner` | main thread | **8 ms/frame** (µs budget) | work that must touch the scene, sliced like coroutines |
| `ProgressiveTaskRunner` | main thread | N per frame, all done in ~500 ms | work whose cost you *cannot measure* |

The third one is the interesting one, and its header comment is unusually candid:

> This is originally to workaround the terribly slow Vulkan buffer deallocation in Godot4.
> It happens on the main thread and causes deferred stutters when a terrain contains a lot
> of chunks and the camera moves fast. I hate this workaround because it feels like we are
> almost not in control of a stable framerate.

`terrain/free_mesh_task.h` is a task whose entire body is *dropping a reference to a mesh*.
It exists purely so that freeing many meshes at once gets spread across ~500 ms instead of
landing in one frame. Time-slicing cannot fix this, because the cost is not incurred inside
your call — Vulkan defers it.

**This applies to us directly and specifically.** MinerCraft draws through `RenderingServer`
with explicit RIDs (decided at M0), so unloading a chunk means `RenderingServer.FreeRid()`.
When the player flies across the world and hundreds of chunks unload in one frame, we will
hit exactly this, and we will misdiagnose it as a meshing problem because the stutter arrives
*after* the frame that caused it. Budget mesh *destruction*, not just mesh construction.

---

## Streaming policy

**Box difference, not volume iteration.** When a viewer moves, they compute
`prev_box.difference(new_box, callback)` and `new_box.difference(prev_box, callback)`, which
decompose the change into a handful of sub-boxes. Only the **shell** that actually entered or
left is iterated. Iterating the full R³ view volume every time the player crosses a chunk
boundary is the naive version and is O(R³) per boundary crossing instead of O(R²).

**Blocks are refcounted by viewer, not owned by "the player".** `view_mesh_block` /
`unview_mesh_block` increment and decrement; a block unloads when its count reaches zero.
`VoxelViewer` is a first-class node, and there can be several — which is how multiplayer
works without a special case. This fits our `MinerCraft.Core`-has-no-Godot seam well: a
viewer is a position and a radius, not a camera, and Core can own the streaming decision
entirely. Worth building the refcount in at M2 even with exactly one viewer, because
retrofitting it is a rewrite of the streaming policy.

**Vertical view distance is a separate ratio** (`_view_distance_vertical_ratio`). For a game
that is mostly horizontal, you shrink the vertical extent to avoid loading useless sky and
bedrock. **We likely want the opposite of the default**: a mining game wants a tall, narrow
loaded volume, because the interesting content is below the player, not around them. Either
way, "view distance" must not be one scalar.

**In-flight loads are cancelled on unview.** `_loading_blocks` is tracked separately from
loaded blocks; when a still-loading block's refcount hits zero, the request is removed before
it runs. Without this, running to a destination and turning around queues work for terrain
you will never see, and the queue never drains.

**Unload → save is asynchronous, and re-entry during the save is a real bug source.**
Unloaded-but-unsaved blocks go into `_unloaded_saving_blocks`. If the viewer comes back
before the save lands, `send_data_load_requests` finds the block there and revives it from
memory ("quick reloading") rather than issuing a disk read that would race the pending write.
They flag it as a corner case *"if the player moves fast back and forth or the task runner is
overloaded"* — both of which describe normal mining. This is an M6 problem, but the M2
streaming structure has to leave room for it.

**Dedup with a flag on the block, not a set lookup.** `is_in_update_list` on the mesh block
guarantees a chunk is queued at most once. Cheaper than a `HashSet` probe and impossible to
get out of sync, because the flag lives on the thing being queued. Directly relevant to M3,
where one carve touches several chunks and the same chunk gets dirtied repeatedly per frame.

---

## Data handoff to the mesh job

**They do not build the padded copy on the sim thread.** The task holds
`FixedArray<shared_ptr<VoxelBuffer>, 27>` — the chunk plus its 26 neighbours — and calls
`gather_voxels_cpu()` **on the worker thread**, copying the needed margin into a padded
buffer there, under a read lock covering the whole 3×3×3 region.

Our leftover M1b item says *"Padded 34³ copy on the sim thread when queueing a mesh job."*
That is the more expensive arrangement: it puts a 34³ copy per queued chunk on the thread we
most want to keep free, and it does the copy even for jobs that get cancelled before running.
Holding references and copying inside the job defers the cost to a thread that has time and
skips it entirely for dropped jobs. The tradeoff is that chunk storage must then be safe to
read from a worker while the sim thread may write it — which is a real cost, not a free win,
and is what their `SpatialLock3D` exists to pay.

**Locks are acquired over a box region in a fixed axis order.** The comment —
*"Using ZXY as convention to reconstruct positions with thread locking consistency"* — is
consistent lock ordering to prevent deadlock between overlapping 3×3×3 regions. Note the
order is ZXY again, matching their storage order and ours.

**Shared config is invalidated by replacement, not mutation.** `MeshingDependency` and
`StreamingDependency` are `shared_ptr`s holding the mesher/generator/stream, with a `valid`
flag:

> Pointers inside should not change. If they do, a new instance will be made and old ones
> will be marked invalid, rather than risking a bad pointer read or having to use (many)
> mutexes.

In-flight tasks hold the old `shared_ptr`, see `valid == false` in `apply_result()`, and
discard their output. This is how you change the mesher or the material table at runtime
without locking every task. We will want exactly this when the registry can be reloaded, and
it composes well with our "registry is frozen after load" rule — a reload produces a *new*
frozen registry rather than mutating the old one.

**Dropped tasks still report back.** `apply_result()` sends `TYPE_DROPPED` rather than
silently vanishing, so the terrain can clear its pending bookkeeping. A cancelled job that
doesn't notify leaks "this chunk is being meshed" state forever and the chunk never updates
again. Cancellation is not the same as deletion.

---

## Smaller things worth keeping

- **Batch the enqueue.** `BufferedTaskScheduler` is a thread-local vector; tasks accumulate
  during a frame and `flush()` pushes them in one locked batch. The queue mutex is taken once
  per frame instead of once per chunk.
- **Staging buffer with `try_lock`.** Workers move tasks from `_staged_tasks` into `_tasks`
  using `try_lock`, so a producer never blocks a worker and vice versa.
- **`STATUS_POSTPONED`.** A task that can't proceed returns a status and gets re-run later,
  instead of blocking a pool thread waiting. They are honest that the postponed queue
  effectively spins: *"Those postponed tasks will sort of spinlock with no sleeping. Is that
  a bad thing?"* — an open question in their own code, so not a pattern to copy blindly.
- **Mesh blocks may be a multiple of data blocks** (`mesh_to_data_factor`). If 32³ chunks
  produce too many draw calls, meshing 2×2×2 chunks into one surface is available without
  changing storage. Relevant to the M2 measurement task.
- **Known mutex hotspot.** *"When tasks are very short and there are a lot of tasks, one
  thread can monopolize this mutex."* Our jobs are chunk-sized and therefore not very short,
  so we are probably fine — but it says where to look if the pool underperforms.

---

## What this changes in our plan

| Our plan said | Reading says | Action |
|---|---|---|
| `System.Threading.Channels` job queue | FIFO can't reprioritise or drop | Re-sorted list + periodic sweep; Channels only for results hand-back |
| Padded 34³ copy on the sim thread | Copy on the worker from held references | Reconsider; costs a read lock on chunk storage |
| (unstated) | Mesh *destruction* stutters, deferred and unmeasurable | Budgeted free path for `RenderingServer` RIDs |
| (unstated) | Streaming is a box-difference over a refcounted viewer set | Build refcounting at M2, with one viewer |
| "chunk streaming around the player" | Viewers are plural and vertical extent is separate | Viewer abstraction in Core; tall volume, not cubic |
