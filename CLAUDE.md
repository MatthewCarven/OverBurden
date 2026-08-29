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
| [docs/M3-PLAN.md](docs/M3-PLAN.md) | The current milestone — mining, split into M3a–M3d. **This is what to work from.** |
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
evaluation, for A/B against the lattice) and `--applybudget=<ms>` (a large value such as 1000
is the unbudgeted arm; there is no "off").

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

M0, all of M1, **all of M2 and all of M3** are done — **367 tests passing**.
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
- **Reach is the player's, not the tool's.** 5 m on `MiningController`, and it belongs to M4's
  controller. A tier says how big a bite it takes and what it is hard enough to take; how far
  an arm reaches is not that, and putting it in the tool JSON now would guess a field into a
  format mods have written against.

**The headless frame-time trap appeared a third time** and was not believed: a mining run
reported `frame ms` max 19.6 ms against a 6.9 ms control, while `update ms` max moved 0.13 ms
and the spike did not reproduce in two further runs. `update ms` times our own work directly.

**M4, the player, is next** — AABB against the voxel grid, axis-separated resolution, gravity
and step-up. It has no plan document yet; M1-PLAN and M3-PLAN are the worked examples of how
one gets written before any code.

**M3 has been played, and it feels good** — the verdict the milestone was actually waiting on,
since it is the first whose output was a feeling rather than a number. `VoxelHighlight` is
verified windowed. One note came out of it: **reach reads a little long at 5 m**, which is
twenty voxels. Unchanged, because it is fine for a creative-mode flycam and because reach is
the *player's* rather than the tool's — M4 is where something exists to own it, which is the
same argument that kept it out of the tool JSON.

Three things stay open, all needing a windowed run rather than a headless one: whether the
free budget removes the stutter (headless has no deferred deallocation), and M1d's
`RenderingServer` vs `MeshInstance3D` and 2×2×2 mesh-block questions, which need the windowed
draw-call counter that `StreamingProfiler.RenderLine` already prints.

The licence is settled: [PolyForm Noncommercial 1.0.0](LICENSE.md), source-available. It
can be loosened to MIT any time; it cannot be tightened after the first outside PR is
merged, which is the real freeze point.
