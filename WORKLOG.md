# MinerCraft — Worklog

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
