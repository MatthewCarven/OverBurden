# MinerCraft — TODO

See [docs/DESIGN.md](docs/DESIGN.md) for the reasoning behind all of this.

## Decisions

- [x] **Engine — Godot 4 + C#.** Decided 2026-08-23. Real threads, renderer for free,
      4.7 mono already on disk.
- [x] **Voxel scale — 0.25 m.** Decided 2026-08-23.
- [x] **Chunk size — 32³ (8 m cube).** Decided 2026-08-23.
- [x] **No mobs.** Pressure comes from flooding, collapse and logistics.
- [x] **Mod support — data mods at M1, Lua at M12, no C# DLL loading.**
- [x] **Chunk storage order — Z-major, then X, then Y contiguous.** Revised 2026-08-25
      after reading godot_voxel, which uses the same `[z][x][y]` order for the same
      reason. The original 2026-08-24 choice (X contiguous) was wrong: its stated
      justification — "keeps a horizontal slab together for the lighting column pass" —
      was self-contradictory, and every hot path in this game is vertical (skylight,
      falling water, support propagation, groundwater drainage, columnar terrain gen).
- [x] **Build our own voxel engine, don't adopt godot_voxel.** Decided 2026-08-25, see
      [ADR-0001](docs/adr/ADR-0001-build-our-own-voxel-engine.md). Read theirs as
      reference; `reference/` is gitignored for exactly that.
      - [x] **Read godot_voxel's threaded mesh scheduling and chunk streaming before
            M2** — done 2026-08-26, and it paid for itself a second time. Findings in
            [docs/M2-REFERENCE-NOTES.md](docs/M2-REFERENCE-NOTES.md).
      - [ ] Revisit trigger: M1c or M2 exceeding ~3 sessions each
      - [ ] Revisit trigger: M13 distance LOD, where their advantage is largest
- [ ] Collapse debris: loose re-settling rubble vs rigid falling chunks — decide at M9
- [ ] Power model complexity — decide at M10
- [ ] Name check: "MinerCraft" — keeping it?

## Before the repo goes public

Pushed to `github.com/MatthewCarven/MinerCraft` on 2026-08-27, **private**, default branch
`main`. Still not urgent — but the window for the cheap version of some of these is now
open rather than hypothetical, and it closes at "public" or "first collaborator", whichever
comes first.

- [x] **Licence — PolyForm Noncommercial 1.0.0.** Decided 2026-08-26, written to
      [LICENSE.md](LICENSE.md). Source-available, not open source: read, fork, mod and
      share freely; no commercial use. Picked on asymmetry rather than ideology —
      loosening a licence later is one commit, tightening one is impossible, and while
      you hold 100% of the copyright the cost of choosing "too restrictive" is zero.
      - [ ] Revisit trigger: **before merging the first outside PR.** After that the
            licence is effectively frozen without every contributor's agreement, so
            decide then whether to require a DCO sign-off or a CLA.
      - [ ] Revisit trigger: first real art/audio assets (≈M6, or first export) — that
            is when splitting code and content licences starts to earn its complexity.
            Right now every texture is procedural, so there is nothing to split.
      - [ ] Revisit trigger: if you ever want distro packaging or a real contributor
            community, that is the moment to go MIT or Apache-2.0 instead.
      - [ ] Not covered by any licence: the **name**. If "MinerCraft" survives the name
            check, the protection against someone shipping a clone is trademark and the
            content, not copyright on the engine.
- [ ] **Decide whether commit authorship stays `matthewcarven@gmail.com`** — now the only
      item here with a closing window. All 6 commits carry it. While the repo is private
      and has no other collaborators, rewriting it is genuinely cheap: `git filter-repo
      --mail-map`, then one force-push, and nothing downstream breaks because nothing
      downstream exists. That stops being true the moment it goes public or anyone else
      clones it, after which the address is a permanent public record on every commit.
      GitHub's `users.noreply.github.com` address is the usual alternative, and setting
      `git config user.email` afterwards keeps new commits consistent.
- [ ] Godot's MIT attribution — a credits screen using `Engine.GetLicenseText()` and
      `Engine.GetCopyrightInfo()`. Obligation attaches when the **game** is distributed,
      not when the repo is published, so this belongs with the first export rather than
      here. Recorded so it isn't discovered at release.
- [x] **Sanity-check before the first push — done 2026-08-27, passed.** `reference/`
      tracked 0 files, no `export_credentials.cfg` or `.pck`, 63 files and 122 KiB total,
      which is itself the proof the 51 MB of godot_voxel stayed out.
- [ ] Cosmetic, expected, not a bug: GitHub reports the licence as **"Other"**, because
      PolyForm isn't in its licence database. Nothing to fix unless the badge matters
      enough to switch licences, which it doesn't.

## Needs a human eye

- [x] **Face winding — confirmed correct 2026-08-24.** Visually verified from three
      angles: grass top, dirt sides, stone underneath, solid convex silhouette. Godot's
      clockwise front-face convention is as documented, so no flip was needed. The
      screenshots also confirmed texture-array indexing, per-vertex normals, and the
      UV2.y shade channel.
- [ ] Ambient fill is low — faces angled away from the sun go nearly black. Cosmetic
      only, and M5 lighting replaces this shading path entirely, so not worth tuning
      the Environment now.
- [ ] **Look at streamed terrain on a screen.** M2c-2 is verified headless and the numbers
      are exactly right — 1521 chunks tracked, 405 meshed, 0 leaked — but no one has seen
      it. Worth checking for seams at chunk boundaries, terrain that pops in visibly late,
      and whether the camera lands somewhere sensible. `start.cmd`, then fly.

---

## Phase 1 — Engine

### M0 — Skeleton ✅ done 2026-08-24, builds clean and runs headless

- [x] Godot 4.7.1 mono project, net8.0, `.slnx` solution
- [x] Project split: `MinerCraft.Core` (headless sim, no Godot reference — enforced by
      having no Godot dependency at all) / `MinerCraft.Core.Tests` / `MinerCraft.Game`
      (Godot front end). The test project exists to prove Core stays headless.
- [x] `VoxelConstants` — 0.25 m voxels, 32³ chunks, power-of-two shift/mask constants
- [x] `VoxelPos` / `ChunkPos` — floor-based chunk/local decomposition via shift and
      mask, correct for negative coordinates. 43 tests, all passing.
- [x] `Texture2DArray` + `Voxel.gdshader` — layer index in UV2.x, light/AO in UV2.y
      (constant 1.0 until M5, but the channel exists now)
- [x] Culled block mesh via `RenderingServer`, no `MeshInstance3D`. A 4³ block emits
      192 triangles against 768 naive.
- [x] Flycam — WASD/Space/Ctrl, sprint and crawl, mouse capture
- [x] `.gitignore`, `start.cmd`

### M1 — Storage, identity and the mesher ⚠️ over-invest here

Split into four sessions. Full reasoning in [docs/M1-PLAN.md](docs/M1-PLAN.md).

#### M1a — Identity and registry ✅ done 2026-08-24, 99 tests passing

- [x] `Identifier` — namespaced string IDs (`minercraft:stone`), validated, lowercase
      only. No `enum BlockType`, ever.
- [x] **One numeric ID space, owned by the world, not the run.** `SeedIdTable` restores
      the world's saved assignment; only unseen content gets fresh numbers appended.
- [x] **Never renumber, never compact.** Holes are preserved and never reclaimed; a
      removed mod's IDs resolve to a placeholder retaining the original string ID.
- [x] `minercraft:air` pinned to numeric ID 0 by reserving the *number* before loading —
      the definition still comes from JSON, so the dogfood rule holds
- [x] `Registry<T>` — frozen after the load phase, immutable and lock-free to read after
- [x] `MaterialDefinition` incl. support distance and permeability for M8/M9
- [x] `IContentSource` with directory and in-memory implementations
- [x] `mods/*/mod.json` manifests + `materials/*.json`, errors collected not thrown
- [x] **Base game ships as `mods/minercraft/` through the identical loader**
- [x] Wired into Godot: block textures now come from material JSON
- [x] Tests: identifier validation, freeze, save → remove mod → reload → re-add cycle,
      hole preservation, load determinism, and the counter-example showing ID drift
      *without* a saved table
- [ ] Deferred: dependency topological sort (parsed and validated now, resolved at M12)
- [ ] Deferred: `res://mods` does not resolve inside an exported `.pck` — packaging loose
      mod content needs solving before the first export, not before then

#### M1b — Chunk storage ✅ done 2026-08-25, 133 tests passing

- [x] Three representations in one class: Uniform (one int, **zero allocation**) →
      Palettised bit-packed → Direct `ushort[]` above 256 palette entries
- [x] **Padded to `ulong` word boundaries, no straddling.** Every width used (1, 2, 4, 8)
      divides 64 exactly, so padding wastes literally nothing and every read is one
      load/shift/mask with no branch and no second word fetch.
- [x] Palette widening at 1→2→4→8 bits, repack on boundary crossing
- [x] **Dropped the 16-bit palette tier** the plan called for: a 16-bit palette index
      costs exactly as much as the material ID it points at, so the tier is pure
      overhead. 256 entries is the honest cutover to Direct.
- [x] **Never shrinks eagerly** — `Compact()` only, pinned by a test asserting a chunk
      stays palettised after being mined back to uniform content
- [x] Per-chunk compression palette named distinctly from M1a's per-world ID table
- [x] `CopyTo` bulk read, with a uniform fast path that never touches packed storage
- [x] Tests: every width, exact growth boundaries, exact byte counts, `Compact` narrowing
      and demoting, Direct→Palettised recovery, bounds and 16-bit material validation
- [x] **Differential fuzz against a naive `ushort[]`** — 4 pool sizes × 40k random ops,
      each probing an unrelated voxel so a clobbered neighbour surfaces early, with
      `Compact()` interleaved to prove it never changes content
- [ ] Padded 34³ copy on the sim thread when queueing a mesh job — belongs with M1c,
      which is the thing that consumes it.
      **Reopened 2026-08-26, resolved 2026-08-27: copy on the worker thread instead.**
      godot_voxel needs `shared_ptr` to stop a chunk being freed while a job holds it; in
      C# holding a reference already does that, so GC gives us half the machinery free.
      The remaining hazard is *mutation*, not lifetime — and at M2 there is no concurrent
      writer, because mining is M3 and fluid is M8. So the worker-side copy is safe now,
      keeps the sim thread free, and skips the copy entirely for cancelled jobs.
      - [ ] **Revisit trigger: M3.** The first concurrent writer arrives with mining, and
            needs either a per-chunk lock or a copy-on-write snapshot. Scheduled decision,
            not a bug to discover. See [docs/M2-PLAN.md](docs/M2-PLAN.md).

#### M1c — Greedy mesher ✅ done 2026-08-25, did not spill

- [x] Sweep: 6 face directions × 32 slices, 2D mask per slice, greedy rectangle merge
- [x] **Merge key carries material + light + AO from the start**, constant until M5
- [x] `PaddedChunk` (34³) so boundary faces need no cross-chunk lookup and no locking
- [x] `MaterialTable` — properties flattened into arrays after the registry freezes,
      because the mesher asks "is this opaque?" tens of millions of times a second
- [x] Output `MeshBuffer`/`ChunkMesh` of plain `float[]`/`int[]` — Godot-free
- [x] Tiled UVs — a 32-voxel quad gets UV 0..32
- [x] **Opaque and transparent surfaces separated now**, before water needs it at M8
- [x] Mask allocated once per mesher and buffers reused via `Reset()` — allocation-free
      in steady state, and simpler than pooling since the buffers never escape
- [x] Tests: empty → 0, solid → exactly 6, single voxel → 6, checkerboard → culled count,
      layered terrain → 1
- [x] **Coverage equivalence**: every quad expanded back into voxel faces must reproduce
      the culled mesher's answer exactly, with no cell covered twice. Subsumes both
      surface-area conservation and no-overlap, across 5 random densities.
- [x] Winding invariant: `du × dv` points along the outward normal on every quad

#### M1d — Godot bridge ✅ done 2026-08-25

- [x] `ChunkMeshConverter` — `MeshBuffer` → `ArrayMesh`, the only place Godot types meet
      mesher output, and what makes reusing the buffers safe
- [x] Face table now lives in Core as `VoxelFace`; the Game-side copy and the M0 block
      builder are deleted
- [x] Real chunk of test terrain rendered: layered strata, a rolling surface, and a
      carved cavity so interior geometry is non-trivial
- [x] Measured: 8,208 B for 32,768 voxels; 196,608 naive faces → 6,128 culled → **850
      greedy quads**, 7.2× better than culled and 231× better than naive

### M2 — World

- [x] **Read `reference/godot_voxel/` threaded mesh scheduling and streaming policy.**
      Done 2026-08-26 — findings in
      [docs/M2-REFERENCE-NOTES.md](docs/M2-REFERENCE-NOTES.md). It earned its keep again:
      it caught that the job queue below was specified as the wrong data structure.
- [x] **Plan M2 as sub-parts first, the way M1 was** — done 2026-08-27, in
      [docs/M2-PLAN.md](docs/M2-PLAN.md). Four parts, built in order:
      **M2a** job scheduler → **M2b** worldgen → **M2c** streaming policy →
      **M2d** unload path and measurement. M2c is flagged as the one most likely to
      spill into two sessions. The bullets below are grouped by part in that document.
#### M2a — Job scheduler ✅ done 2026-08-27, 186 tests passing

- [x] ~~`System.Threading.Channels` job queue for gen + mesh~~ **Corrected and built.**
      Channels is FIFO, and a FIFO cannot reprioritise queued work as the player moves,
      nor drop work that became irrelevant before it ran. A priority heap is also wrong,
      for the same reason: it orders at insert time, which is when the ordering is least
      valuable. `JobQueue` is a **flat list re-sorted on a timer** (200 ms default), with
      one sweep recomputing priority, dropping cancelled entries, and sorting. Channels is
      used for handing *results* back to the sim thread, which really is FIFO.
- [x] `JobPriority` packs four byte-bands into one `uint`, so comparison is a single
      integer compare. Band0 distance, band2 job class, band1/band3 reserved.
- [x] Generation outranks meshing at equal distance — a mesh job cannot run before the
      data it reads exists, so scheduling it first only burns a worker slot.
- [x] `ViewerSet` reads **without a lock**. Improved on godot_voxel's pre-sized array plus
      atomic count: reference assignment is atomic in .NET, so swapping in a whole new
      snapshot array gives readers a consistent view with **no tearing at all**, which
      their approach cannot. Viewers move at most once a frame, so the allocation is free.
- [x] Real `CancellationToken`s from the start, skipping the distance-drop scheme
      godot_voxel explicitly regrets.
- [x] One pool, two lanes — a `serial` flag for disk work, not a second thread pool.
      Unused until M6; the flag exists now because retrofitting it touches every
      submission site.
- [x] Thread count `clamp(round(0.5 × cores), 1, cores - 1)`. Dedicated threads, not
      `Task.Run` — the .NET thread pool knows nothing about voxel priority.
- [x] **Every submitted job reports exactly one result** — never zero, never two. A job
      that throws faults without taking its worker down; a job still queued at shutdown
      reports Dropped. Fuzzed over 400 jobs with concurrent cancellation, viewer movement
      and sweeping.
- [x] Tests: band precedence, distance evaluation, the **reprioritisation test a FIFO
      cannot pass**, sweep timing, serial-lane exclusion, and the exactly-once invariant.
      27 job tests, all headless in Core.
- [ ] Deferred to M2c-2: nothing submits jobs yet. The scheduler is exercised only by tests
      until streaming feeds it. M2c-1 built the policy that decides *what* to submit; M2c-2
      does the submitting.

#### M2b — Worldgen ✅ done 2026-08-27, 216 tests passing

- [x] Worldgen as a pure function `(seed, chunkPos) → chunk`, reading nothing but its
      arguments — no cache, no neighbour lookup, no field mutated between calls. Pinned by
      an order-independence test, which is really a guard against anyone later adding
      shared mutable state.
- [x] Deterministic seeding via **splitmix64 written out explicitly**, never
      `HashCode.Combine`, whose seed .NET randomises per process. Pinned by
      `KnownInputsHashToKnownOutputs`; `Mix(0)` matches the published splitmix64 value, so
      the test also proves the mixer is the real algorithm rather than a plausible
      rearrangement of it.
- [x] Gradient noise, not value noise — value noise takes its extremes *at* lattice
      points, which shows up on a heightmap as an axis-aligned grid of hills.
- [x] 2D fBm heightmap, strata from a data-driven `StrataTable`, 3D noise caves with a
      surface margin so caves do not bite craters out of the landscape from above.
- [x] Sky fast path: a chunk entirely above terrain returns Uniform air without touching
      memory. Deep chunks are not special-cased — they fill and let `Compact()` notice.
- [x] `GenerationStage` enum and an `ApplyFeatures` hook that deliberately does nothing
      yet, so the two-stage barrier exists before M2c builds the pipeline around it.
- [x] Tests: determinism, seam agreement across a chunk boundary, order independence, the
      sky and deep fast paths, strata ordering, caves-only-remove, and noise continuity.

**Measured** over 2,048 chunks (128 m × 64 m × 128 m):

```
4.0 MiB total, mean ~2 KB/chunk, against 128 MiB naive — 32× better
68% of chunks uniform (803 air, 541 solid), 704 palettised, 0 direct
6.55 ms/chunk with caves, 1.35 ms without — caves are 80% of generation
```

- [ ] **Cave generation is the bottleneck and needs another pass — deferred to M2d.**
      Two free wins already taken (one avalanche round instead of four chained; 16-entry
      gradient table indexed by `& 15` instead of a 64-bit `% 12`), which took it from
      9.48 to 6.55 ms/chunk. What remains is inherent to evaluating 3D fBm per voxel:
      ~256,000 hash-and-dot operations per chunk. The real fix is to sample cave noise on
      a coarser lattice and interpolate, since at frequency 0.02 the field barely varies
      across a 32-voxel chunk anyway. Not urgent — across ~6 workers this is ~1.1 ms of
      wall clock per chunk — but it wants doing before shipping.
- [ ] Caves are blobby pockets rather than connected tunnels, which is what thresholding
      fBm gives you. Connected systems want ridged or worm noise, and are a stage-2
      feature once the pipeline can place things across chunk boundaries.
- [ ] Load strata from mod JSON rather than constructing `StrataTable` in code. The table
      is already data and takes resolved numeric IDs, so this is loader work, not a
      worldgen change.
#### M2c-1 — Streaming policy, as pure data ✅ done 2026-08-27, 254 tests passing

The two-session warning in [docs/M2-PLAN.md](docs/M2-PLAN.md) was taken up front: the
policy is built and fuzzed with no threads and no jobs, so M2c-2 wires the scheduler to
something whose invariants are already pinned.

- [x] `ChunkBox` with **box difference, not volume iteration** — a boundary crossing
      decomposes into at most six disjoint sub-boxes and touches only the shells that
      entered and left. O(R²) per crossing, not O(R³): at radius 8, 289 chunks instead of
      4,913. Verified against brute force over 2,000 random box pairs, asserting both
      halves — the sub-boxes are pairwise **disjoint** and their union is **exact**.
- [x] Intersect-first, so the disjoint case is handled before any slicing. Not a fast
      path: it is what lets the slicing arithmetic skip clamping entirely, and written the
      other way round a non-overlapping box emits slabs with inverted bounds.
- [x] `Viewer` is a Core concept — a position and **two** radii, not a camera. Vertical
      extent is separate from horizontal, so "view distance" is never one scalar.
- [x] **Refcount per chunk across viewers**, built with one viewer. A shared chunk survives
      one of two viewers leaving; the fuzz asserts counts, not just membership, because a
      count drifting high is a chunk that never unloads and a count drifting low unloads
      terrain someone is standing on.
- [x] `ChunkState` enum with a transition guard that **throws** on an illegal edge, rather
      than a scatter of `bool`s. Includes the three edges that are easy to forget: any
      state to Unloaded, one step back when a job reports Dropped, and Meshed back to
      Meshing for M3's re-mesh.
- [x] Dedup lives in the state, not a `HashSet` probe — "already queued" *is* `Queued`, so
      it cannot drift out of sync with the chunk it describes.
- [x] The **stage barrier**: a chunk advances to Features only once all 26 neighbours have
      completed Terrain, and meshes only once all 26 have completed Features.
- [x] **Load margin of two chunks**, derived rather than guessed — see M2-PLAN.md. The
      mesher's padded copy needs the first ring; the feature stage needs the second. Costs
      ~41% more chunks at radius 16, and a parameter rather than a constant so M2d can
      measure both.
- [x] `ChunkStreamer` publishes viewer positions into the `ViewerSet` the job queue sorts
      against, so streaming and scheduling cannot disagree about where the player is.
- [x] Tests: the shell test, two-viewer sharing, radius changes, hysteresis, the barrier at
      the edge of the loaded region, and the fuzz invariant over 400 random viewer
      add/move/resize/remove steps. 38 new tests, all headless in Core.

#### M2c-2 — Wiring streaming to the scheduler ✅ done 2026-08-27, 261 tests passing

**M2 runs end to end.** Terrain streams around the flycam through the job queue and the
greedy mesher; `Main.cs` no longer builds a chunk by hand.

- [x] `GenerateChunkJob` and `MeshChunkJob`, submitted through `SubmitBatch` so the queue
      lock is taken once per frame rather than once per chunk.
- [x] `ChunkPipeline` applies results on the sim thread: `DrainResults` → state transition →
      store voxels or hand the mesh over. Workers only read the snapshot given to them and
      write their own result field; every dictionary and refcount touch is sim-thread.
- [x] **A work list, not a per-frame scan.** A chunk is pushed exactly when something might
      have changed for it, with the dedup flag on the chunk. Advancing one chunk pushes its
      26 neighbours — miss that and the pipeline stalls silently with everything idle.
- [x] **Cancel in-flight work when a refcount hits zero.** Measured under fuzzed movement:
      394 jobs completed against 3,693 dropped, asserted so the test cannot quietly stop
      exercising the path.
- [x] **A chunk with a job in flight is retained, not forgotten.** `ForgetIfUnwanted` collects
      it once the result lands. `ViewedCount` and `TrackedCount` are separate for this
      reason, and a gap between them that does not close is a job that never reported.
- [x] `onChunkViewed` fires on **revival** as well as creation — a chunk that came back mid-job
      would otherwise sit behind a cancelled job forever, leaving a hole exactly where the
      player turned around.
- [x] Stage pipeline driven: Terrain on generate, Features once `CanAdvanceToFeatures`
      passes, mesh once `CanMesh` passes.
- [x] Worker-side neighbourhood gather via `PaddedChunk.CopyFrom` — all 26 neighbours, not
      the 6 that face visibility needs today, because M5's vertex AO samples diagonally.
- [x] Tests: end-to-end generate-and-mesh, the margin generating without meshing, full
      release when the last viewer leaves, leave-and-return mid-load, cancellation on
      teleport, chunk revival, and the no-chunk-left-waiting fuzz. 7 integration tests
      against a real scheduler on real threads.

**Measured** headless from a standing start, r4h/2v + 2 margin on 8 workers:

```
1521 viewed (1521 tracked), 166 drawn, 0 in flight, 0 queued
1926 jobs done / 0 dropped / 0 faulted   — settled inside 5 s
```

1926 = 1521 generates + 405 meshes, and 405 is exactly the 9×5×9 visible box.

- [ ] **Nothing has looked at this on a screen yet** — see "Needs a human eye" above. The
      numbers are right; "looks like terrain" is a different claim.
- [ ] Stage 2 runs on the sim thread, because it writes into its neighbours by design and a
      worker would need a lock over the whole 3×3×3 region. Fine while it places nothing.
      Revisit trigger is the one already recorded for chunk storage: **M3, the first
      concurrent writer.**
- [ ] A faulted job is reported and not retried, so the chunk renders as a hole until the
      player leaves and returns. Untested, deliberately: `WorldGenerator` is a concrete
      dependency with no clean way to induce a fault, and a seam added only for that would
      cost more than the branch is worth.

#### M2d — Unload path and measurement ⬅️ **start here**

- [ ] **Budget mesh *destruction*, not just construction.** Freeing many `RenderingServer`
      RIDs in one frame stutters, and the cost is deferred inside the renderer, so it lands
      a frame or two *after* the cause and will be misdiagnosed as a meshing problem. Needs
      a "run N per frame until done" path, distinct from a time-sliced one, because the cost
      cannot be measured from our side.
- [ ] Measure: `RenderingServer` direct vs `MeshInstance3D` at target chunk count
      - [ ] Also measure whether meshing 2×2×2 chunks as one surface cuts draw calls
            enough to be worth it. Mesh blocks need not equal storage blocks.
- [ ] Measure the load margin: 2 against 1, in chunks loaded and in generation time. While
      stage 2 places nothing the terrain is identical, so this is a free 30% if it matters.
- [ ] Memory and frame time at target view distance, and **while flying fast enough to force
      continuous load and unload**. Standing still proves nothing about a streaming system.
- [ ] The cave optimisation pass deferred from M2b — coarser cave lattice with
      interpolation. Do not treat it as a streaming problem when it surfaces.

### M3 — Mining ← first fun moment

- [ ] Amanatides & Woo voxel DDA raycast, returns hit face normal
- [ ] Sub-voxel sphere/cone carve, radius and hardness threshold from tool tier
- [ ] Incremental remesh on edit, budgeted
- [ ] Dirty-neighbour handling at chunk borders

### M4 — Player

- [ ] AABB vs voxel grid, **axis-separated** resolution (X, then Y, then Z)
- [ ] Gravity, jump, step-up
- [ ] Do **not** use Godot physics for terrain (`ConcavePolygonShape3D` is a trap)

### M5 — Lighting

- [ ] Coarse 1 m light grid, 4-bit sky + 4-bit block
- [ ] BFS flood fill, incremental re-propagation on edit (un-light pass is the fiddly one)
- [ ] Trilinear sample at mesh time
- [ ] Vertex AO at mesh time

### M6 — v0.1 playable sandbox

- [ ] SQLite persistence, `(cx, cy, cz) → blob`, palette in the world file
- [ ] Brush placement: 1 m cube, slab, ramp, sphere
- [ ] Volumetric inventory — litres, not block counts
- [ ] Hotbar / material selection UI

---

## Phase 2 — The loop

### M7 — Rain

- [ ] Watershed analysis at worldgen: basins, outflow saddles, volume→level curves
- [ ] Tier A basin accumulators, overflow routing downstream
- [ ] Camera-locked instanced rain billboards, velocity-stretched
- [ ] **Skylight-occlusion particle kill** so rain stops at the mine entrance
- [ ] Wet-surface shader term (skylight × recent rainfall), splash particles

### M8 — Water gets in

- [ ] Tier B volume-conserving cellular automata over an active-cell set
- [ ] Sleep/wake rules; settled cells merge into a Tier A body
- [ ] Tier C coarse groundwater saturation field, fed by rain, drains downhill
- [ ] Seepage into excavations driven by saturation × material permeability
- [ ] Transparent render pass, back-to-front sorting

### M9 — v0.5, the game exists

- [ ] Support BFS from anchored material, per-material support distance
- [ ] Incremental, local, budgeted, off-thread — never global
- [ ] Collapse into rubble
- [ ] **Saturation degrades support distance** ← the keystone interaction
- [ ] Damage/death from collapse

---

## Phase 3 — Depth

### M10 — Fighting back

- [ ] Machine framework registered through the public mod API (dogfood test:
      if a pump can't be built through it, the API is wrong)
- [ ] Fixed 20 Hz sim tick, spatially partitioned update list
- [ ] Power: generation / consumption / storage network
- [ ] Pumps, shoring/supports

### M11 — v1.0

- [ ] Drills, pipes, conveyors
- [ ] Crafting, health, hunger

### M12 — Modding tier 2

- [ ] MoonSharp host, sandboxed
- [ ] Event bus exposed: `BlockChanged`, `ChunkGenerated`, `PlayerTick`, `ToolUsed`,
      `RecipeCrafted`
- [ ] Mod loading order, dependency declaration, API versioning
- [ ] Write a non-trivial example mod as the real test

### M13 — Polish

- [ ] Distance LOD (downsample distant chunks 2x/4x by majority vote)
- [ ] Biomes
- [ ] Gas pockets, darkness as a hazard
