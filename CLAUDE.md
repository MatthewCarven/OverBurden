# MinerCraft — orientation

A voxel mining game in Godot 4 + C#. **You are a mining operation fighting water and
gravity.** Rain fills basins, water finds your excavation, saturated rock loses cohesion
and the ceiling comes down; you build pumps and shoring to fight back, which needs power,
which needs more mining. No mobs, no combat — the mine is the antagonist.

## Read these before changing anything

| Document | What it is |
|---|---|
| [docs/DESIGN.md](docs/DESIGN.md) | Locked decisions, the five non-negotiables, per-system architecture, milestones, **and a list of rejected alternatives so they don't get re-litigated** |
| [docs/M1-PLAN.md](docs/M1-PLAN.md) | Worked example of how a milestone gets planned before it gets built |
| [docs/M2-REFERENCE-NOTES.md](docs/M2-REFERENCE-NOTES.md) | What reading godot_voxel's scheduler and streaming taught us, and what it changed in the M2 plan. **Read before building the job queue.** |
| [docs/M2-PLAN.md](docs/M2-PLAN.md) | M2, split into M2a–M2d with per-part design and tests. Done, kept for the reasoning. |
| [docs/M3-PLAN.md](docs/M3-PLAN.md) | M3, mining, split into M3a–M3d. Done, kept for the reasoning. |
| [docs/M4-PLAN.md](docs/M4-PLAN.md) | M4, the player, split into M4a–M4d. Done, kept for the reasoning. |
| [docs/M5-PLAN.md](docs/M5-PLAN.md) | The current milestone — lighting and AO, split into M5a–M5d. **This is what to work from.** |
| [docs/adr/](docs/adr/) | ADR-0001: why we build our own voxel engine rather than adopt godot_voxel, **with named revisit triggers** |
| [TODO.md](TODO.md) | Milestones as checkable tasks, and the decisions still open |
| [WORKLOG.md](WORKLOG.md) | What happened and why, newest first. Findings worth keeping live here. |

## Non-negotiables

Violating any of these is expensive to undo. They are argued for in DESIGN.md; don't
quietly reverse one.

1. **`MinerCraft.Core` never references Godot.** That absence is the multiplayer seam.
   `MinerCraft.Core.Tests` exists partly to enforce it — if Core needs Godot, the tests
   stop compiling.
2. **Content IDs are namespaced strings** (`minercraft:stone`), resolved to numeric IDs
   through a table the **world** owns, not the process. Never an enum. Never renumber,
   never compact — a removed mod leaves a hole that stays.
3. **`minercraft:air` is numeric ID 0**, always.
4. **Texture arrays, never an atlas.** Greedy meshing tiles UVs past 0..1 and an atlas
   cannot express that.
5. **The base game is a mod.** It loads from `mods/minercraft/` through the identical
   loader a third-party mod uses, with no privileged path. If something can't be
   expressed the way a mod would express it, the format is wrong.

Two more that are settled but easier to forget:

- **Chunk storage order is `[z][x][y]` — Y contiguous.** Nearly every mechanic here is
  vertical (skylight, falling water, support propagation, groundwater drainage). This was
  wrong once already; see the 2026-08-25 worklog entry.
- **Light and AO are in the mesher's merge key** even though both are constant until M5.
  Removing them "because they're unused" means rewriting the sweep later.

## Commands

Build and test:

```bash
dotnet test MinerCraft.slnx
```

Run the game:

```bash
start.cmd
```

Measure worldgen — **Release, always; Debug is ~3× slower and reads like a real number**:

```bash
dotnet run --project MinerCraft.Bench -c Release
```

Verify headlessly — no display needed, catches scene and script errors and prints the
diagnostics:

```bash
../GAME/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path MinerCraft.Game --headless --quit-after 900
```

**`--quit-after` counts frames, not seconds** — 900 is about 15 s, and the streaming report
only prints every 5 s, so a small number exits before there is anything to see.

Profile a flythrough. The flags go after a bare `--`, as `name=value`, and the profiler
quits by itself when it is done, so no `--quit-after` is needed:

```bash
../GAME/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path MinerCraft.Game --headless -- --profile --radius=12 --vradius=6 --speed=40
```

Also `--margin=`, `--warmup=`, `--measure=`, `--cavestep=` (1 restores per-voxel cave
evaluation, for A/B against the lattice), `--applybudget=<ms>` (a large value such as 1000
is the unbudgeted arm; there is no "off") and `--ao=0` (restores the pre-M5a
constant-brightness merge key, for the vertex-AO A/B).

**A profile run quits by itself, so it needs no `--quit-after` — and passing one usually
truncates the run**, because 900 frames at ~145 fps is 6 s and the default window is 18.

Take a screenshot of the real scene, which is the only way to answer a look question — headless
has no renderer, so a shader error does not even surface there:

```bash
../GAME/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path MinerCraft.Game --script res://screenshot.gd -- C:/tmp/shot.png
```

It instances `Main.tscn`, waits 8 s for terrain, saves a PNG and quits. Extra arguments after
the path are passed through to the game, so `-- shot.png --ao=0` gives the other arm of an A/B.

Profile a **fall** — the vertical motion profile M2d's horizontal flythrough could not produce,
and the one that can arrive somewhere before the world does. `--fall` implies `--profile` and
forces walk mode, so the scripted flythrough stays off:

```bash
../GAME/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path MinerCraft.Game --headless -- --fall --radius=6 --vradius=3 --terminal=40 --fallheight=250 --measure=20
```

`--terminal=` sets terminal velocity and `--fallheight=` the drop, both so the constant this
profile exists to pick can be swept without a rebuild. **A fall height large enough not to land
inside the window measures nothing at rest** — the ground-flicker detector needs landings, and
the report says when it did not run.

If Godot has never opened the project, run once with `--headless --import` first.

Godot 4.7.1-stable **mono** is not vendored here; `start.cmd` falls back to the shared
copy in `../GAME/`.

## Conventions

- Append findings and decisions to WORKLOG.md; tick and add tasks in TODO.md. Both as you
  go, not at the end.
- Commit with a message that explains *why*, and end with
  `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.
- Matthew runs `git init` and `git push` himself — flag when either is needed. Remote is
  `github.com/MatthewCarven/MinerCraft`, **private**, default branch `main`.
- `reference/` is gitignored and holds third-party sources we **read but never build
  against** (currently godot_voxel). Nothing in it belongs in a `.csproj`.

## Where things stand

M0, all of M1, **all of M2, all of M3, all of M4, M5a and M5b** are done — **518 tests
passing**. The player walks, falls, and cannot outrun the streamer, voxels have ambient
occlusion, and there is a light grid — not yet wired into the pipeline, which is M5c.
Terrain streams around the flycam: worldgen and greedy meshing run on worker threads over palette-compressed storage,
with materials loaded from JSON, and chunks load and unload as the viewer moves.

Headless from a standing start, r4h/2v + 2 margin on 8 workers:

```
1521 viewed (1521 tracked), 165 drawn, 0 in flight, 0 queued
1926 jobs done / 0 dropped / 0 faulted   — settled inside 5 s
```

1926 = 1521 generates + 405 meshes, and 405 is exactly the 9×5×9 visible box. Drawn was 166
before M2d-2; the cave lattice carves marginally less, so one chunk now meshes to nothing.

M2 is split into four parts in [docs/M2-PLAN.md](docs/M2-PLAN.md). The prerequisite
reading of godot_voxel is done and paid for itself: it caught that the job queue had been
specified as `System.Threading.Channels`, and a FIFO can neither reprioritise queued work
as the player moves nor drop work that went stale before it ran. See
[docs/M2-REFERENCE-NOTES.md](docs/M2-REFERENCE-NOTES.md).

**M2a — job scheduler: done 2026-08-27.** `MinerCraft.Core/Jobs/` holds a flat list
re-sorted on a 200 ms timer, priority packed into one `uint` of four byte-bands, real
cancellation tokens, and dedicated worker threads. Every submitted job reports exactly one
result — that invariant is fuzzed, and a job that vanishes silently would leave its chunk
flagged as work-in-progress forever. Nothing submits jobs yet; streaming at M2c feeds it.

**M2b — worldgen: done 2026-08-27.** `MinerCraft.Core/Worldgen/` holds splitmix64, gradient
noise, a data-driven strata table, and a generator that is a pure function of seed and
position. Measured over 2,048 chunks: 4.0 MiB against 128 MiB naive, 68% of chunks uniform.

**M2b's `6.55 ms/chunk` was a Debug number** and said so nowhere — M2d-2 re-measured the same
code at 2.23 ms/chunk in Release. The cave share it reported (80%) was right; only the scale
was wrong. The game project builds Debug, so the streaming profiles are Debug too and are
consistent with each other — but never quote a generation figure without its build.

**M2c-1 — streaming policy: done 2026-08-27.** `MinerCraft.Core/Streaming/` holds `ChunkBox`
(six-way box difference, fuzzed against brute force for disjointness *and* exactness),
`Viewer` with separate horizontal and vertical radii, a chunk state machine that throws on
an illegal transition, and `ChunkStreamer` — refcounts across viewers, box difference on
move, and the stage barrier. M2c was split up front rather than halfway through, so this
part has no threads and no jobs.

Two things from it worth knowing before touching streaming:

- **The loaded region exceeds the visible one by two chunks, not one.** The mesher's padded
  copy needs the first ring; the cross-chunk feature stage needs the second, because a chunk
  reaches stage 2 only once its own neighbours have terrain. ~41% more chunks at radius 16.
  It is **not** a tuning knob: M2d measured margin 1 at r4h/2v as identical to margin 2 at
  r3h/1v, because the barrier applies whether or not stage 2 places anything.
- **Meshability is derived from the barrier, not tracked.** A margin chunk fails it by
  construction, so the meshable set lands exactly on the visible box with one refcount and
  no shell bookkeeping. Don't add a second refcount to "fix" the margin.

**M2c-2 — wiring: done 2026-08-27.** `ChunkPipeline` owns the work list, job submission,
result application and cancellation; `GenerateChunkJob` and `MeshChunkJob` do the work;
`Main.cs` is now a thin Godot shell that reports where the camera is and turns finished
meshes into RIDs. Three things about it that are easy to undo by accident:

- **Advancing one chunk pushes its 26 neighbours.** A chunk finishing terrain may be the
  last thing its whole neighbourhood was waiting on. Miss that push and the pipeline stalls
  silently, everything idle, nothing obviously wrong.
- **`onChunkViewed` fires on revival, not just creation.** A chunk retained past its last
  viewer keeps whatever state its cancelled job left it in, so coming back has to announce
  itself or that chunk sits behind a dead job forever.
- **`ViewedCount` and `TrackedCount` are different numbers.** A chunk with a job in flight
  is retained after its last viewer leaves. A gap between the two that does not close means
  a job never reported.

**M2d-1 — unload path and measurement: done 2026-08-27.** `MeshFreeQueue` releases meshes a
few per frame with the budget scaling to the backlog, and `VoxelRenderInstance` now disposes
its `ArrayMesh` — it never did, so the expensive half was left to the GC. `StreamingProfiler`
plus a scripted flythrough and `--profile --radius --vradius --margin --speed` make a sweep
one shell loop.

What the numbers changed:

- **The load margin is not a tuning knob**, and three docs said it was. Margin 1 at r4h/2v is
  identical to margin 2 at r3h/1v on every metric, because the barrier applies whether or not
  stage 2 places anything. Keep margin 2; view distance is the knob.
- **Main-thread streaming cost plateaus near 3% of one core** from r6 to r12 — bounded by
  worker hand-back rate, not box size.
- **Memory is mesh-dominated, ~46 KiB per drawn chunk** against ~2 KB of voxel storage. So
  horizontal radius is expensive and vertical is nearly free, which is the measured argument
  for separate radii.
- **Caves cap sustainable view distance.** r12 draws 205 chunks at 40 m/s and 2,245 at 6 m/s
  — a speed effect, not a radius ceiling — and the constraint is generation throughput, of
  which caves are 80%. M2d-2 acted on this and confirmed it.

**M2d-2 — cave optimisation: done 2026-08-28.** Cave noise is sampled on a world-anchored
lattice every `CaveLatticeStep` voxels (default 4) and interpolated in between, so a chunk
costs 729 fBm evaluations instead of 32,768. Generation went **2.23 → 0.47 ms/chunk** in
Release, and caves fell from 83% of generation to 20%. `MinerCraft.Bench` is the repeatable
harness that measured it; `--cavestep=1` restores per-voxel evaluation for A/B runs.

Four things about it that are easy to undo by accident:

- **`IsCaveAt` and `GenerateTerrain` share one definition of the field.** The query
  interpolates eight fresh corners, the generator reads a buffered lattice, and they agree
  bit for bit — same operations in the same order, pinned by an equality test. Two
  definitions would drift, and the symptom is mining a wall that is already air.
- **The step must be a power of two dividing 32.** That is what makes a chunk boundary a
  lattice point, which is what makes independently generated neighbours agree. The
  constructor throws otherwise; a seam is invisible until it is in a screenshot.
- **Interpolation only ever removes caves, never invents one** — measured, not assumed.
  Linear interpolation cannot exceed its endpoints, so peaks inside a cell get clipped, and
  a threshold at p95 selects exactly those peaks. So **the step is a cave-volume knob**: step
  4 costs 10% of cave volume, step 8 costs 37%. Voxel disagreement alone hides this.
- **Linear interpolation, not the quintic fade.** The fade belongs inside gradient noise
  where flattening at lattice points is the point; as a resampling kernel it is a
  first-order error where plain linear is second-order.

**Mesh-apply budget: done 2026-08-29.** `ChunkPipeline.Update` applies finished jobs under a
2 ms time slice and leaves the rest for the next frame — the second of the two budgets
M2-PLAN specified. Time-sliced rather than counted, because unlike freeing, the cost is spent
inside our call and can be measured as we go.

Read the worklog before touching it, because the measurement said two awkward things:

- **A time slice cannot bound the tail below the cost of one item.** Update max stays at
  3.3–4.7 ms whether the budget is 0.2 ms or effectively infinite — one mesh conversion is
  atomic. The budget bounds the average and the p99, not the worst case.
- **The 27.6 ms frame that motivated it was not mesh application**, does not reproduce, and
  the worst frame measured came from the *most* tightly budgeted run. **Frame time in
  headless is the wrong instrument** — use `update ms`, which times the call directly. This
  is the second time that trap has been walked into.

**M3a — the edit path: done 2026-08-29.** `ChunkPipeline.SetVoxel` writes a voxel, clones the
chunk first if a job holds it, and marks every chunk whose mesh that invalidates. This closed
both recorded revisit triggers at once — M1b's "no concurrent writer until M3" and M2a's "the
worker-side padded copy is safe because nothing writes yet".

Three things about it that are easy to undo by accident:

- **A `VoxelChunk` handed to a job is immutable from then on.** `Neighbourhood()` marks all 27,
  and an edit finding the flag set clones instead of writing. Mutating in place would not show
  a worker a stale voxel — `VoxelChunk.Set` can promote the representation and swap the backing
  arrays under a reader mid-copy, which is a `NullReferenceException` on a worker thread.
- **`SetVoxel` returns early when the material is already there.** That is not a micro-
  optimisation: a sphere carve rewrites air as air across most of its bounding box, and each
  one would otherwise clone a chunk and dirty up to eight neighbours.
- **`NeedsRemesh` survives a mesh completing.** An edit that lands mid-job produces a mesh built
  from the pre-edit snapshot, so `Complete` pushes the chunk again — nothing else would, because
  `Advance` refuses a chunk with a job in flight.

**M3b — the raycast: done 2026-08-29.** `VoxelRay.Cast` (Amanatides & Woo) over `IVoxelWorld`,
which `ChunkStreamer` implements. Returns the hit voxel, the face normal, the entry distance
and the last empty voxel before it — the last one is where M6's placement goes.

- **`Blocked` is not `Missed`.** An unloaded voxel is an unknown, not an empty one. Collapsing
  them lets the player aim through the edge of the loaded world at a chunk that does not exist,
  where the edit vanishes with nothing to report.
- **Its fuzz is a ray/AABB slab test, not a sampler, and that was a correction.** A sampler is
  less exact than the DDA — a ray can clip a voxel corner for less than one sample step — so
  the first version failed on the code being right.

**A working rule this milestone earned twice: watch a fuzz test fail before trusting it.**
M3a's hung instead of failing; M3b's first oracle was wrong in the subject's favour. Neither
would have shown up in a green run. Both now say in a comment what breaks them and how fast.

**M3c — the carve: done 2026-08-29.** `MinerCraft.Core/Mining/Carve.cs` takes a sphere of
material out of the world, gated per voxel by hardness; `ToolDefinition` and
`mods/minercraft/tools/*.json` make a tier content rather than a constant; `MaterialTable`
finally projects `Hardness`, which `MaterialDefinition` has carried unread since M1.
`IVoxelEditor` is `IVoxelWorld` plus a write, and `ChunkPipeline` implements it.

Four things about it that are easy to undo by accident:

- **The distance test is the definition; the bounding box is only an optimisation.** The box
  is derived exactly and then widened by one voxel on each side, so float error in the
  derivation can never be what excludes a voxel the predicate would keep. Tightening it
  "because the slack is wasteful" trades a correctness guarantee for a handful of iterations.
- **The carve skips air before it ever calls `SetVoxel`.** Air's hardness is 0, so it passes
  every threshold there is; without the skip a swing would report roughly double what it took.
  This does **not** replace M3a's "material is already there" early-out — that one now
  protects the next writer, which is M6's placement. Both belong.
- **A tool radius is a denial-of-service vector, and a NaN threshold is worse.** The scan is
  cubic, so `"radiusMetres": 1000` in a mod file freezes the game inside a loop rather than
  carving a crater; `ToolDefinition.MaxRadiusMetres` bounds it at load *and* in `Carve.Sphere`,
  because the loader is not the only caller. 4 m, and **labelled an estimate** — 16 m was the
  first answer and did not bound the failure its own comment named. A NaN threshold fails the
  other way, *open*: every comparison with NaN is false, so `hardness > threshold` never holds
  and the gate inverts from "takes nothing" to "takes everything".
- **A viewer leaving mid-remesh used to strand the mesh on screen.** `Meshed → Meshing` is an
  M3 edge, and before it a chunk that was `Meshing` had never delivered anything — so
  `OnChunkUnviewed`'s "is it `Meshed`?" test was equivalent to "does it have a live mesh?" and
  stopped being. `StreamedChunk.HasLiveMesh` is now the flag, and the old geometry no longer
  keeps drawing for terrain that has been unloaded. Mine something, fly away, and it was
  still there.
- **`Unreachable` is not `TooHard`**, the same way `RayOutcome.Blocked` is not `Missed`. A
  swing at a wall and a swing off the edge of the loaded world leave the same-looking hole and
  are not the same event.

**The base game's own files are finally loaded by a test.** `ShippedContentTests` reads
`MinerCraft.Game/mods/` from disk, rather than the `InMemoryContentSource` fixture every other
content test builds — which had already drifted (it has no grass). It also checks the shipped
*numbers*: that some tier leaves the hardest material standing and some tier can take it,
because the hardness gate is a property of the content and no amount of testing the code
finds a JSON file that has been edited to make it meaningless.

**The working rule is three for three now: a green run is not evidence.** M3a's fuzz hung
instead of failing, M3b's first oracle was wrong in the subject's favour, and M3c had a
pipeline test whose *comment* claimed it caught the border rule — reducing `ChunkEdits` to
owner-only left it green, because the writes already span eight chunks by themselves. Break
the code on purpose, and check the comment too.

**M3d — binding, feel and measurement: done 2026-08-29.** Left mouse mines on the flycam,
number keys pick a tier, and a wireframe `VoxelHighlight` outlines the target.
`MiningController` is Godot-free, so `--mine` drives a whole profile window headlessly.

**The milestone's real question is answered: yes, a pick swing fits inside a frame, with two
orders of magnitude to spare.** Mining continuously at five swings a second while flying
r6h/3v at 8 m/s:

```
                     streaming only     + continuous mining
update ms  p99            0.910               1.038
           max            2.036               2.118
main thread               0.80%               1.02%  of one core
```

Against a 16.7 ms frame. **Do not build** M3-PLAN's fallbacks — dirty-region meshing, smaller
mesh blocks, latency hiding — until something asks for them.

Three things about M3d that are easy to undo by accident:

- **A swing is centred on the hit voxel's *centre*, not on the ray's entry point.** The entry
  point lies exactly on a face, so half the sphere starts in air, and how much of it does
  depends on the angle of incidence — a radius written in a mod's JSON would mean something
  different on every swing. `Carve.SwingCentre` holds the decision, in Core, because where a
  swing lands is a game rule a multiplayer server would need.
- **M3a's clone estimate is settled: right per chunk, 7× pessimistic per swing.** Measured 1.2
  clones and 2.4 KiB per swing against a guessed 8 chunks and ~16 KB. Eight is the worst case
  at a chunk corner, not the typical one.
- **Reach is the player's, not the tool's.** 5 m, and a tier says how big a bite it takes and
  what it is hard enough to take, not how far an arm stretches — putting it in the tool JSON
  would guess a field into a format mods have written against. **M4c moved it off
  `MiningController` onto `PlayerController`**, where it now lives; the value is unchanged.

**The headless frame-time trap appeared a third time** and was not believed: a mining run
reported `frame ms` max 19.6 ms against a 6.9 ms control, while `update ms` max moved 0.13 ms
and the spike did not reproduce in two further runs. `update ms` times our own work directly.

**M4, the player, is done** — [docs/M4-PLAN.md](docs/M4-PLAN.md), all four parts: M4a the
collider, M4b motion, M4c binding, M4d feel and measurement. Five things
from the planning pass that are expensive to rediscover:

- **The viewer's position stops being an input.** Until now the flycam was told where to go
  and the streamer followed. From M4 the position is an *output* of a query against the very
  chunks the player decides to load. The cycle is broken by solving step *N* against the world
  as loaded at step *N*−1, and by treating **an unloaded voxel as solid**.
  `ChunkStreamer.TryGetMaterial` returns air *and* false, so ignoring the return value is the
  unrecoverable version of M3b's `Blocked`-is-not-`Missed`: the player leaves the loaded world
  and accelerates away from anything that could stop them.
- **Walking never tunnels and falling always does.** Move-then-resolve is exact below 15 m/s
  — one voxel, 0.25 m, over a 1/60 s step — so the bug cannot appear while movement is being
  tested. Substep at 0.2 m, capped on **displacement, not the box's dimensions**, because a
  0.25 m floor is thinner than the player.
- **Terminal velocity is a streaming parameter in a physics costume**, and **the player's
  width is coupled to the weakest tool's bite**. There is 16–24 m of loaded world below the
  player; `hand_pick` leaves a 0.75 m bore and a 0.5 m box passes with 0.125 m either side.
  Widen the player and the first tunnel a new player digs is one they cannot walk down.
- **Do not delete the flycam**, whatever its own doc comment says. `--profile` assigns
  `GlobalPosition` directly and `AdvanceFlythrough` snaps the camera every frame; a camera
  parented to a physics body does neither, and every measurement M2d and M3d built would go
  with it. Walk/fly toggle, `--profile` implies fly.
- **The trap is in M4c, not in the physics.** M4a and M4b are deterministic, headless and
  fuzzable — the collider's oracle is "seal the player in a room and try to shake them out",
  which is a boolean with no reference implementation and no tolerance.

**M4a — the collider: done 2026-08-29.** `MinerCraft.Core/Physics/` holds `Aabb` and
`VoxelCollider.Move(world, materials, box, displacement)` — axis-separated resolve, substepped
at 0.2 m, an unloaded voxel treated as solid, `grounded` falling out of the −Y resolve. A pure
function of a box, a displacement and a world.

Five things about it that are easy to undo by accident:

- **The voxel layer is taken from the *moved* box, never from "one past where the box was".**
  The two are the same layer while the box is outside a wall, so nothing distinguished them
  until a test was added for a box that *starts* inside one. The second form finds no new layer
  ahead of an already-overlapping box and waves the move through, walking it out the far side in
  about ten steps. It also cannot teleport the box forward past what the move asked for. This
  was a real hole — four break-recipes were caught by the suite and this fifth one was not.
- **`Aabb` stores a corner and a size, not two corners, and that is what makes standing still
  bitwise stable.** A resolve places one face from the blocking voxel's index and carries the
  size across untouched; recomputing the size as `Max − Min` every step rounds it differently
  each time and a resting player creeps. Same reason `ResolveAxis` returns early on a zero delta
  instead of translating by zero — adding `0.0f` is exact for every float *except* `-0.0f`.
- **The contact epsilon is belt and braces; the half-open interval is the load-bearing part.**
  Setting `ContactEpsilonMetres` to zero leaves the whole suite green, because a face exactly on
  a voxel plane is touching rather than inside and voxel planes are exact in binary. It is kept
  for a voxel size that is not a power of two, and its honest cost is a **knife edge at a gap
  exactly the box's width** — 0.1 mm of the trailing face inside the far wall. The box still
  cannot pass through, and a straight drop down such a shaft is exact.
- **The query-cost guess was pessimistic and needs no follow-up.** M4-PLAN guessed ~400
  `TryGetMaterial` calls per step; measured, a walking step costs **41**, and the worst
  alignment is 63 per substep — 252 at a four-substep fall. **Do not build** the chunk
  memoisation the plan named as the fix. One detail the plan's arithmetic missed: a 0.5 m box
  that is not grid-aligned straddles **three** voxel columns, not two.
- **Float positions cap the usable world at a few kilometres**, long before `VoxelPos`'s `int`
  does — one ULP passes the contact epsilon around ±800 m. **Revisit trigger:** a floating
  origin, not a wider float.

**M4b — motion: done 2026-08-29.** `PlayerMotion` and `PlayerMotionConfig` add gravity, jump,
terminal velocity, step-up and step-down on top of the collider. `Step(world, materials, state,
intent, config, dt)` is a pure function — no clock, no accumulator, no field — and the step
length is an argument, so 60 Hz is Godot's business rather than Core's. Suite 400 → 435.

Six things about it that are easy to undo by accident:

- **The lift is snapped to its exact target, and that is what keeps M4a's epsilon claim true.**
  Nothing caught removing it, because a resting body floats 0.1 mm up and that swamps a few ULP
  of substep accumulation. But M4a *recorded* that `ContactEpsilonMetres` can be set to zero
  with the suite green — and with the lift accumulated instead, zeroing it fails four tests. A
  finding in the previous milestone was one line from going stale with nothing failing to say
  so. Now pinned by a test that places the feet **exactly** on a voxel plane, which is what
  M4c's spawn and a fly-mode toggle both do.
- **The jump apex is 1.1875 m, not the plan's 1.125 m** — `v²/2g` is the continuous answer. A
  jump *replaces* its step's gravity rather than being reduced by it, so the rise overshoots by
  exactly half a step of launch speed. Clearance over a four-voxel ledge is 0.1875 m, and it is
  **rate-dependent**: twice as much at 30 Hz. The apex test asserts one-sided, because the
  direction is the design claim.
- **A step-up settles by exactly what it lifted**, so it is a climb and never a fall. What that
  protects is step-down's distance limit — a settle that could go further gives the same rule
  two answers depending on whether the body happened to climb something first. Also caught by
  nothing until a test was written for it, because walking cannot reach it: 0.075 m of travel
  against a body 0.5 m wide.
- **Removing step-down's non-upward guard breaks jumping entirely**, not subtly: the probe finds
  the floor the jump just left and snaps the body back onto it. Six tests fail. Ground state is
  read from the *previous* step throughout, which is M4a's stated cost, not a bug.
- **Step-up's "the re-attempt must fully succeed" guard has a recorded feel cost.** Hugging a
  wall while walking up a slope stops you dead, because the lifted re-attempt is still blocked
  on the wall. Specified behaviour, safe, and **if M4d says it reads as stuck the relaxation is
  one line** — require the re-attempt to be blocked on no axis the original move was not. **Not
  encountered in the 2026-08-30 play-test**, which walked up hills rather than into corners; the
  case needs provoking deliberately, so that is silence and not evidence.
- **M4d needed a signal M4b deliberately did not build, and the deferral was right.**
  `MoveResult` could not tell "stopped by rock" from "stopped by an unloaded chunk" — exactly
  *steps spent held*, the number that picks terminal velocity. It was left out because an unread
  flag with no profiler consuming it is a number waiting to be believed. **Built at M4d as
  `MoveResult.BlockedByUnloaded`, alongside the profiler that reads it**, and it was right first
  time — while the counter written *without* a consumer in mind, M4d's ground-flicker detector,
  was wrong twice.

Horizontal velocity is **replaced from intent every step rather than accelerated** — no friction,
no momentum, full air control — because acceleration, air control and ground friction are three
separate feel decisions and M4d is where a person makes them. Reach stayed on `MiningController`:
it is not a body dimension. Eye height *is* in the config, because it is the third dimension of
the same body.

**M4c — binding: done 2026-08-29.** `PlayerSpawn` in Core holds the three world rules — where a
spawn lands, whether the world is ready, what to do about a body inside rock — and
`PlayerController` in Game holds the session: which body is driving, and the gate the other one
waits behind. `FlyCam` gains `FlightEnabled`; there is no physics body node and no reparenting.
**F toggles walk and fly.** Suite 435 → 452.

**The prediction was that M4c would be where the surprises were, and it was not.** The binding
was mechanical; both things nothing caught were in the world rules that went into Core.

- **The flycam kept every number, which was the entire contract.** The default headless run
  reports **1521 viewed / 165 drawn / 1926 jobs** — M2d's recorded figures unchanged to the
  digit — so M4d's measurements stay comparable with M3d's. `--profile` and a non-zero
  `--speed` force fly mode *and refuse the F key mid-run*, because handing a scripted
  flythrough's camera to a body invalidates the run rather than making it interesting.
- **Gate, then unstick, then step. The reverse order is the unrecoverable one.** Against an
  unloaded world every unstick candidate is solid, the search runs to its 4 m bound, and a body
  standing in open sky is reported *stuck*. The gate checks every chunk the box overlaps **and
  the chunk beneath each of them** — the layer below because standing on an unloaded chunk is
  standing on something that will not be there. Measured: **13–14 steps, about 0.22 s, held
  for the world** at r4h/2v — it moves run to run with worker scheduling.
- **An unstick lifts and never drops, and that is a decision rather than a consequence.** A body
  with its head in a ceiling goes up through it onto the roof. **Caught by nothing** — every
  buried-body test placed the feet on a plane already, where the downward candidate is the box
  itself and overlaps by construction. `UnstickNeverMovesABodyDownward` closes it, and this is
  the rule M6's placement inherits, up-only included.
- **`Aabb.Translated` versus rebuilding the lifted box is undetectable, and is kept anyway.** No
  failing case exists inside the few-kilometre world M4a's float analysis allows. A
  documentation correction, not a caught bug — the same shape as M4a's zeroable epsilon.
- **Feet exactly on a voxel plane survives exactly one step.** The first −Y resolve places the
  body `ContactEpsilonMetres` clear, so the spawn's 1.25 m is 1.2501 m by the next frame. The
  state exists *only* at the spawn and at a mode toggle, which is why M4b's lift test constructs
  it by hand. Check this before writing any bitwise assertion about a resting body.
- **Order within a step came for free.** Player → viewer → pipeline holds because the step is in
  `_PhysicsProcess` and the rest in `_Process`, and Godot runs a frame's physics ticks first.
- **Reach moved to the player and deliberately did not change.** Still 5 m;
  `MiningController.ReachMetres` is now `required` and defaultless so there is no second literal.
  M4-PLAN's table said 3.5 m and the M3 play-test said 5 m read long — both were feel, and
  retuning in the same pass as the move would have confused the two changes. **Settled by play
  2026-08-30: 5 m stays**, generous on purpose, with **a survival mode as the recorded trigger**
  to shorten it to 3 m or 2 m. The plan's 3.5 m was an estimate and was never shipped.

**M4d — feel and measurement: done 2026-08-30. The milestone's question is answered: no, the
player cannot outrun the streamer.** `--fall` drops the body from height and measures it;
`--terminal` and `--fallheight` make the sweep a shell loop. Suite 461 → 468.

```
terminal 40 m/s, 250 m falls, 20 s     r4h/2v      r6h/3v
update ms p99 / max                 1.05 / 4.57  1.54 / 5.50
main thread                            1.14%        2.27%   of one core
step ms mean / max                 0.015 / 0.35 0.013 / 0.57
substeps / voxel reads per step      3.42 / 13.3  3.41 / 13.3
held on unloaded world                    0            0
```

Five things from it that are easy to undo by accident:

- **Terminal velocity 40 m/s stands, with 2.25× measured margin.** `held 0` at r4h/2v, r6h/1v,
  r6h/2v, r6h/3v and r12h/3v — and r6h/3v survives **90 m/s** with zero held steps. It first
  loses at r12h/3v @ 90 m/s (3 steps, longest 17 ms) and r16h/3v @ 120 m/s (12 steps, 83 ms).
  The arithmetic agrees: a chunk row at r6 is 169 chunks crossed every 0.2 s; at r16 it is 1,089
  crossed every 0.067 s.
- **A falling step costs 16 voxel reads, not M4a's predicted 252**, because `ResolveAxis` returns
  early on the two axes with zero displacement — an early-out added in M4a so a resting body
  would not creep by a rounding error. **A numerical-stability decision that turned out to be a
  performance one**; do not "simplify" it away. **Do not build** M4-PLAN's query memoisation.
- **The flicker detector was wrong twice before it was worth anything**, both times reporting a
  small non-zero number that read as a real defect: it counted **landings** (the landing step
  *is* the first at-rest step), then it counted **held steps** (a body on an unloaded chunk is
  grounded with velocity zeroed, so it looks like rest). `RestingSteps` is printed beside it now,
  and the report says outright when the detector never ran.
- **`MoveResult.BlockedByUnloaded` is a subset of `Blocked` by construction**, so it can never
  contradict it — recorded only on the face that actually stopped the box. Step-down writes the
  literal `NegativeY` rather than masking the probe's set, because a purely vertical probe cannot
  block horizontally at all: `ResolveAxis` returns early on a zero delta. A break-recipe proved
  the mask was dead code.
- **Recorded limitation: the fall profile measures generation, not meshing.** A falling player
  falls through air, so every run reports `0 drawn`. Falling down a shaft you dug — terrain on
  all sides — is not covered, and would need a dug shaft rather than a drop from altitude.

**The headless frame-time trap, a fourth time:** `frame ms` sat at mean 6.90 / max 7.48 across
every run while directly-timed `update ms` max ranged 1.1–9.3 ms. Unrelated numbers.

**M4 has been played, twice, and the headline question is answered: 0.25 m ground walks like
ground.** "Walking uphill occurred naturally, I did not need to jump" — which is DESIGN's claim
that step-up is not a convenience in a quarter-metre world but the difference between walking on
the ground and not, confirmed by a person rather than argued. **Step height stays 0.5 m and the
jump constants stand.** Two things came with it:

- **You "sortof" fit down a tunnel you carve**, and the hedge is exactly what M4-PLAN's
  arithmetic predicts. `hand_pick`'s 0.4 m radius leaves a 0.75 m bore and a 0.5 m box passes
  with **0.125 m either side — half a voxel** — while consecutive swings centred on different
  voxel centres wander the bore's axis. Fits on paper, scrapes in practice. **The cheap lever is
  the tool, not the player:** `radiusMetres` 0.4 → 0.5 gives a five-voxel bore and 0.375 m of
  clearance, and it is one number in one JSON file with no rebuild — which is what M3c made
  tiers content for. Widening the player is the move the plan warns against.
- **Two corner cases stayed unjudged and ordinary walking cannot reach them:** step-up guard 3's
  diagonal wall-hug, and step-down's 0.5 m instant drop. Neither was reported, which is silence
  rather than evidence.

**Mining has two shapes as of 2026-08-30: `Carve.Swing` spheres on a bare click, `Carve.Chip`
takes one voxel under Ctrl.** DESIGN's "mining is sculpting" line stands and now records why:
a single-voxel *default* was proposed and rejected, because `hand_pick`'s sphere is ~17 voxels
and the default would have been **~17× slower to excavate** with the starting tool — in a game
about a mining operation, and against DESIGN's own argument that volumetric inventory exists to
make big excavations productive. Precision is the exception, and an exception is worth a held
key. Four things about it that are easy to undo by accident:

- **`Chip` takes `hit.Voxel` itself, not a sphere centred on it.** So `VoxelHighlight`'s outline
  is literally the voxel about to be removed, rather than the centre of a bite extending past
  it. That is the whole reason precision reads as precise.
- **Same hardness gate, same cooldown.** Precision changes how much a swing takes, never what it
  is strong enough to take, and never how fast it fires. Either exemption would make the
  modifier the *efficient* way to mine as well as the accurate one, and the sphere would stop
  being the default in practice while still being it in the code.
- **One `Carve.Take` holds the gate for both shapes, and that is load-bearing.** Removing the
  hardness check from it fails **9** tests, **seven of them pre-existing sphere tests** — one
  edit breaking both shapes is exactly what a forked copy would not do.
- **`radiusMetres` did not change meaning**, which is the quiet dividend of keeping the sphere
  as the default: the tool JSON is untouched and no format mods write against moved.

**Known and unfixed: Ctrl is also FlyCam's descend.** Fine in walk mode where Ctrl is free; in
fly mode a builder sinks ~1.8 m between chips at the default speed and `hand_pick`'s cooldown.
**Alt is the better key** — free in walk, and *crawl* in fly, so "slow down to do fine work"
pairs rather than fights — and the swap is one line. Left on Ctrl pending a play-test.

**M3 has been played, and it feels good** — the verdict the milestone was actually waiting on,
since it is the first whose output was a feeling rather than a number. `VoxelHighlight` is
verified windowed. One note came out of it: **reach reads a little long at 5 m**, which is
twenty voxels. **M4d settled it 2026-08-30 and 5 m stayed** — generous, and generous is the
right way to be wrong in a sandbox. Unchanged, because it is fine for a creative-mode flycam and
because reach is
the *player's* rather than the tool's — M4 is where something exists to own it, which is the
same argument that kept it out of the tool JSON.

**M5, lighting, is planned — [docs/M5-PLAN.md](docs/M5-PLAN.md), split M5a–M5d. M5a and M5b
are built; M5c is next.** The structural point: **light is the first thing here that is not a function of one
chunk.** Worldgen is pure per chunk, meshing takes a padded copy, a carve is local — light
propagates arbitrarily far, and removing one voxel can change light in a chunk that is not a
neighbour. Five things from the planning pass that are expensive to rediscover:

- **4-bit light and the 2-chunk margin are exactly compatible, and that binds them.** Light
  reaches 15 m; the margin is 16 m; a source outside the loaded region contributes
  `max(0, 15 − 16) = 0` at the visible box. So light computed over the loaded region is **exact**
  inside the drawn one, and **the margin now has a third load-bearing reason** — the tightest of
  the three. **Revisit trigger: 5-bit light (31 m) needs a 4-chunk margin.**
- **Sampling light per vertex puts it in the merge key, and a merged quad only interpolates its
  four corners.** So two faces merge iff all four corner shades match — exact when they do, and
  where light varies the quads stop merging. That price is what M5a and M5c measure. The named
  fallback is a per-chunk 3D texture sampled in the fragment shader, where **a light change costs
  no remesh at all**; **do not build it** before the number says so.
- **The vertex carries two light channels, not one, and DESIGN is what forces it.** With the
  directional light gone (settled 2026-08-30) the grid is the only light, and DESIGN's M7 needs
  skylight *on its own* — "wet-surface shader term driven by skylight × recent rainfall" — which
  a pre-multiplied `sky × block × AO` cannot supply. So `shadeSky` and `shadeBlock` are separate,
  AO multiplies into both, and **a diurnal cycle is then a uniform costing no repropagation**.
  Default home is Godot's vertex `COLOR` array; packing both into UV2.y is exact in float32 and
  not in float16, and `AddSurfaceFromArrays` is called with no flags — **verify before relying
  on it.**
- **A 1 m light cell holds 64 voxels and `hand_pick`'s bore is 0.75 m** — the tunnels this game
  is about are narrower than one cell. So opacity is a property of the cell **face**, not the
  cell: light crosses iff some voxel pair across the shared 4×4 boundary is open on both sides.
  "Any opaque → opaque" means torches light nothing; "any air → open" means a 0.25 m wall leaks,
  and 0.25 m is a normal wall here.
- **The vertical radius means there is no top of the world to flood skylight down from.**
  Seeding is analytic off `WorldGenerator.SurfaceHeightAt`, plus a per-column `SkyColumn` map
  that edits lower — which is also exactly the structure M7's rain occlusion needs, and the one
  lighting structure M6 must persist.
- **The oracle is the sharpest available: incremental light == from-scratch light, cell for
  cell.** No reference implementation to get wrong; from-scratch *is* the definition, just too
  slow per frame. Same category as M4a's sealed room. Its fuzz must include edits that break
  through a column's topmost opaque voxel, or the sky-height path never runs and it passes for
  the wrong reason.

**All four of M5's open decisions were settled 2026-08-30, before any code.** Light cells stay at
DESIGN's **1 m** (revisit trigger: M5d plays a `hand_pick` tunnel and it reads blobby);
**`SkyColumn` is built at M5b** with its lifetime in M5c; **the directional light goes**; and
**emission stays 0..15** — which means the brightest lamp any mod can define reaches exactly
15 m for a storage reason rather than a design one.

**M5a — vertex AO and the merge key: done 2026-08-30.** Per-corner AO from the padded chunk,
two shade channels in the vertex, `CornerShade` in the merge key, the diagonal flip, and `--ao=0`
to turn it all off. Suite 468 → 496.

**The number M5a existed to take: AO costs 1.88× the quads**, against a 2–4× estimate — 262 → 492
per drawn chunk at r4h/2v from a standing start, 253 → 472 at r12h/3v, and static memory 94.8 →
150.4 MiB at r12. **So the AO-alone baseline for M5's fragment-shader trigger is 492 quads per
drawn chunk, and M5c must stay under ~984.** Streaming is untouched: 1521 viewed / 165 drawn /
1926 jobs, M2d's figures to the digit.

Six things about it that are easy to undo by accident:

- **The two shade bytes live in the vertex `COLOR` array, and packing them into UV2.y is not a
  precision question — it is structurally impossible.** Godot 4.7 leaves UV2 at float32
  (`AddSurfaceFromArrays` with no flags sets no `ARRAY_FLAG_COMPRESS_ATTRIBUTES`) and applies no
  sRGB conversion to `COLOR` under a `ShaderMaterial` — both probed, not assumed — so the plan's
  stated tie-break said to pack and save four bytes. **But the hardware interpolates the packed
  number**, and the fractional part of an interpolated sky channel lands in the block channel's
  byte: two corners one sky level apart sweep block light through its whole range in between.
  Per-vertex shade exists to be interpolated; a packed pair of independently varying channels
  cannot be.
- **Key equality does not permit a constant shade along a merged run, it forces one.** Adjacent
  faces *share* two corners, and an independently derived shade at a shared corner is the same
  number by definition — so `A.corner1 == B.corner0` for free, and requiring `A.corner0 ==
  B.corner0` therefore implies `A.corner0 == A.corner1`. That is why "merging is exact" is a
  testable property rather than an assertion, and it is what
  `AMergedQuadHasConstantShadeAlongEveryAxisItMergedOn` checks.
- **The diagonal flip compares the total shade, not AO.** Identical at M5a and different at M5c:
  a flat floor with a torch on it has constant AO and a varying gradient, so an AO-only test
  would never fire on the surface most likely to show the crease the flip removes.
- **`EveryQuadIsWoundConsistentlyWithItsNormal` cannot be broken by the flip**, and M5-PLAN asked
  for it as a guard. It reads vertex *positions*; the flip changes *indices*. Right answer for
  that test, hole for the flip — hence
  `BothTrianglesOfEveryQuadFaceOutwardsInEitherFlipState`, which walks the indices and asserts a
  flipped quad was actually seen.
- **The plan's "swap `side1`/`side2`" break-recipe is a no-op**, because
  `(s1 && s2) ? 0 : 3 − (s1 + s2 + c)` is symmetric in its two sides. The meaningful mutation is
  mislabelling *which corner* gets which sample, and that one fails 8 tests. Green run, third
  category: unreachable rather than untested.
- **`AmbientOcclusion.LevelShade` is a feel constant and its distinctness is not.** The ramp
  `[102, 153, 204, 255]` is taste and M5d judges it; the four values being *different* is the
  merge key, and collapsing two would silently merge visibly different faces.

**A test whose comment had never been true, found by M5a and not caused by it.**
`ChunkPipelineTests.AnEditIsWrittenAndTheChunkIsRemeshed` said its carve target was "somewhere
buried", but `FindSolidInterior` meant "not on the chunk boundary" and returned the first solid
voxel in scan order — which has air on three sides, so carving it removes three faces and exposes
three: **a change of exactly zero quads**. It passed anyway until M5a shifted the surrounding
merges and the two totals came out equal. Same shape as M3c's test whose comment claimed it
caught the border rule.

**M5b — the light grid, propagation and the un-light pass: done 2026-08-30.**
`MinerCraft.Core/Lighting/` holds `LightCellPos`, `ChunkLight` with cell-face conductance,
`SkyColumn`, and `LightPropagator`. `MaterialDefinition.LightEmission` is projected into
`MaterialTable`, `mods/minercraft/materials/lamp.json` ships a full-strength emitter, and **L**
places one. Suite 496 → 518. **Nothing is wired into the pipeline — a lamp placed today is a
pale block that does not glow. That is M5c.**

Seven things about it that are expensive to rediscover:

- **The oracle is better than the one M5-PLAN named, and by a category.** "Incremental equals
  from-scratch" runs the *same add pass* as the subject, so it can only independently catch the
  un-light pass. The propagation invariant
  `V = max(S, max over conducting neighbours (V(n) − attenuation))` has a **unique** fixpoint —
  a cycle of equal values would need zero attenuation all the way round, and the only
  zero-attenuation step is full skylight falling downward, which strictly decreases Y — so
  checking it cell by cell is complete correctness with no propagation in the check at all. Same
  category as M4a's sealed room. Both are asserted, plus a third that fell out free: the
  incrementally maintained sky heights must equal a fresh downward scan of the voxels.
- **The defect that cost seven tests was a missing half of a symmetry.** `Darken` reset a cell to
  its own source and queued the un-light walk when the value had *fallen*, and did nothing when
  it had *risen* — so a placed lamp wrote emission 15 into the derived data and left the cell's
  value at the zero it held as air. The rule is symmetric: **the cell's value ends at its own
  source whichever way it moved.**
- **`Invalidate` is one cell, not seven, and there is a contract behind that.** The caller must
  invalidate every cell `BuildDerived` reported as changed. The case that looks like it needs
  more is a face closing — a walk from A never examines B once the face stops conducting — but
  **conductance is recorded on both cells of a face**, so a changed face always reports both.
  An earlier six-neighbour expansion was provably dead work: removing it failed nothing and
  could not. `SealingAFaceReportsBothOfItsCells` pins the property, and breaking the reporting
  instead fails 11 tests.
- **The skylight free fall is restricted to full brightness, and only one test shape can tell.**
  A shaft straight down from the surface carries *full* skylight, which falls free under both
  rules. The shape that separates them is a horizontal branch off the shaft with a **second
  shaft dropping from its far end**, fed at 10 rather than 15. Without it, every dimly lit patch
  in the world would hang a column of its own light down to bedrock and nothing would fail.
- **A cell is a sky source only if *every* one of its sixteen voxel columns is open, not any.**
  Under-seeding is recoverable — the cell above is a source, the face between them conducts
  through the open half, and full skylight falls through for nothing, so a cliff face is lit by
  propagation anyway. Over-seeding is not: the value is per cell, so a mostly-solid cell declared
  a source pushes daylight into the rock through its own faces.
- **The derived side is 1.5 KB per chunk, not the planned 1 KB, and the extra is not optional.**
  The sky-source bit and the emission nibble are what let the un-light walk zero a cell without
  erasing the lamp it is walking away from.
- **The rebuild scope after an edit is two different shapes, and conflating them costs ~40×.**
  A voxel's *conductance* reaches only its own chunk plus a neighbour across whichever faces it
  actually touches — an interior voxel, 30 in 32, rebuilds exactly one chunk. A change to the
  column's *sky height* is not local at all: it moves the sky flag the whole way up and down that
  chunk column, and nowhere else. **M5c should implement exactly those two scopes**;
  `LightTestWorld` is deliberately written as the thing M5c has to build.

**Dropping the "only if greater" guard in the add pass hangs rather than fails**, as M5-PLAN
predicted. The M3a lesson again — a break-recipe battery has to treat a timeout as its own
outcome or it reads as infrastructure trouble.

**Three more M5c decisions were settled 2026-08-30, before its code**, all found by reading
`LightTestWorld` against `ChunkPipeline` — the pairing M5b wrote that fixture to invite. None is
a reversal; each is a place the plan stated something confidently and stopped one level above
where the wrong answer sat.

- **What sets the `Lit` stage: derived data built *and* the propagator drained.** A global BFS
  has no per-chunk completion event, so `Lit` is a per-chunk stage with one global term.
  **Rejected: a per-chunk count of queued cells** — the obvious way to localise it, and wrong in
  the direction testing does not show, because a chunk with no queued cells of its own can have a
  neighbour's frontier one step from spilling in. Safe to be this coarse only because the drain's
  changed-cell set marks a remesh, which streaming and mining then share.
- **The padded light grid is built on the sim thread at submission, not on the worker.** The
  27-reference gather is safe for voxels because **M3a made voxels copy-on-write**; light is not,
  and the propagator writes into those 512-byte arrays in place while a mesh job runs. One
  1,000-byte copy per job against thousands of clones per drain — 0.6% of the padded voxel copy.
  **It reads as an inconsistency with `PaddedChunk` and invites being "fixed" back.**
- **`NeedsRemesh` means both "stale" and "urgent", and a relight is the first thing that is one
  without the other.** `RemeshUrgent` splits them. A relight also only marks a chunk that has
  actually drawn something — otherwise the initial fill, where every chunk's light changes,
  arrives the whole distance-ordered queue at band3 and M2a's ordering is silently gone. Nothing
  looks broken; the world just loads in the wrong order.

Three things stay open, all needing a windowed run rather than a headless one: whether the
free budget removes the stutter (headless has no deferred deallocation), and M1d's
`RenderingServer` vs `MeshInstance3D` and 2×2×2 mesh-block questions, which need the windowed
draw-call counter that `StreamingProfiler.RenderLine` already prints. **A first reading of that
counter came out of M5a's windowed check and was not acted on: 209 draw calls and 186,728
primitives for 193 drawn chunks at r4h/2v.**

The licence is settled: [PolyForm Noncommercial 1.0.0](LICENSE.md), source-available. It
can be loosened to MIT any time; it cannot be tightened after the first outside PR is
merged, which is the real freeze point.
