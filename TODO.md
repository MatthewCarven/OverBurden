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
- [x] **Streamed terrain confirmed on screen 2026-08-27.** Continuous rolling terrain, no
      seams at chunk boundaries, camera lands above ground. The visible bright and dark
      slivers follow terrain **contours, not the chunk grid** — that is what rules out a
      meshing or streaming seam, which would appear on the 8 m lattice. They are the
      vertical risers of the integer heightmap's terracing, lit or unlit by which way each
      contour segment faces, and they are the concrete symptom of the low-ambient item
      above. Terrain scale matches the settings: ~62 m features, ~12 m amplitude, so at
      r4h's 72 m box roughly one hill fills the view.

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
6.55 ms/chunk with caves, 1.35 ms without — caves are 80% of generation   ← Debug build
```

- [x] **Cave generation was the bottleneck — done at M2d-2, 2026-08-28.** Two free wins were
      taken here at M2b (one avalanche round instead of four chained; 16-entry gradient table
      indexed by `& 15` instead of a 64-bit `% 12`), 9.48 → 6.55 ms/chunk. The rest was
      inherent to evaluating 3D fBm per voxel — ~256,000 hash-and-dot operations per chunk —
      and went at M2d-2 with a coarse lattice and interpolation.
- [x] **That 6.55 ms/chunk was a Debug measurement and nothing said so.** Re-measured on the
      same code: Debug 7.88 / 1.22, Release 2.23 / 0.38. The 80% cave share was right in both,
      so the finding held and only the scale was wrong. `MinerCraft.Bench` now prints a loud
      warning when built Debug. **Quote a generation figure with its build or not at all.**
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

#### M2d-1 — Unload path and measurement ✅ done 2026-08-27, 261 tests passing

- [x] **Budget mesh *destruction*, not just construction.** `MeshFreeQueue` hides the
      instance immediately and releases a few per frame, with the budget **scaling to the
      backlog** (`max(4, pending/30)`) so a fast traverse cannot outrun it. A count per
      frame, not a time slice, because Godot defers the deallocation and a stopwatch around
      the call reports nothing.
- [x] **`VoxelRenderInstance` never disposed its `ArrayMesh`** — the instance RID was freed
      and the mesh left to the GC finaliser, so the expensive half was happening whenever
      the collector felt like it. Fixed; without it the budget would have controlled only
      the cheap half.
- [x] `StreamingProfiler` with percentile sampling, a scripted surface-following flythrough,
      and `--profile --radius --vradius --margin --speed --warmup --measure`.
- [x] **Load margin measured, and the answer reversed the plan: it is not a tuning knob.**
      Margin 1 at r4h/2v is identical to margin 2 at r3h/1v on every metric — 847 tracked,
      97 drawn, 1.37% of one core. The barrier applies whether or not stage 2 places
      anything, so lowering the margin shrinks the meshable radius rather than buying
      anything. Corrected in four documents. **Keep margin 2; view distance is the knob.**
- [x] Memory and cost while **flying fast enough to force continuous load and unload**,
      swept r2 to r12 — table in [WORKLOG.md](WORKLOG.md).

**Measured**, 40 m/s unless noted:

```
config    chunks   drawn   update p99   main    dropped   memory
r4h/2v      1521     189      1.20 ms   2.50%      712    32.3 MiB
r8h/4v      5733     283      1.26 ms   3.13%    14061    35.2 MiB
r12h/6v    14297     205      1.24 ms   3.11%    56132    31.5 MiB
r12h/6v    14297    2245      1.42 ms   2.97%     5244   122.1 MiB   at 6 m/s
```

- [x] Main-thread cost **plateaus near 3% of one core** from r6 to r12 — bounded by worker
      hand-back rate, not by box size. View distance is not a main-thread cost.
- [x] **Memory is mesh-dominated, ~46 KiB per drawn chunk against ~2 KB of voxel storage.**
      Horizontal radius is expensive (it adds surface); vertical radius is nearly free (it
      adds sky and buried rock that mesh to nothing). The measured argument for separate
      radii.
- [ ] **Not measurable headless: whether the free budget removes the stutter.** Headless has
      no renderer and so no deferred deallocation to spread. Peak queue depth stayed ≤27 in
      every run, so the budget is not itself a bottleneck — but confirming the benefit needs
      a windowed run. Fold into the next visual check.
- [ ] Still open from M1d: `RenderingServer` direct vs `MeshInstance3D`, and whether meshing
      2×2×2 chunks into one surface cuts draw calls enough to be worth it. Both need the
      windowed draw-call counter, which `StreamingProfiler.RenderLine` already prints.
- [x] **Applying mesh results is now budgeted — done 2026-08-29.** `ChunkPipeline.Update`
      applies finished jobs under a 2 ms time slice and leaves the rest for the next frame.
      Time-sliced rather than counted, unlike the free path, because the cost *is* spent
      inside our call. `PendingResults` makes a backlog visible, and `--applybudget=<ms>`
      does the A/B.
- [x] **The 27.6 ms frame M2d-2 saw was not mesh application.** Six runs at r12h/6v and
      40 m/s put frame max between 6.9 and 13.1 ms with no relation to the budget, and the
      worst frame measured all session — 32.3 ms — came from the *most tightly budgeted* arm.
      Frame time in headless is paced by something that is not us; M2d-1 said so and it needed
      saying twice. Use `update ms`, which times `ChunkPipeline.Update` directly.
- [ ] **A time slice cannot bound the tail below the cost of one item.** Update max stays at
      3.3–4.7 ms whether the budget is 0.2 ms or 1000 ms, because one mesh conversion is
      atomic and no budget subdivides it. Bounding the true worst case means capping mesh size
      or splitting the conversion across frames. **Not worth doing on current evidence** —
      recorded so the limitation is known rather than rediscovered. Revisit if a windowed run
      shows the single-mesh cost as a visible hitch, or if M3's incremental remesh makes large
      single meshes common.
- [ ] The 2 ms default is a judgement, not a measurement: applying results averages 0.44 ms
      and hits 2.26 ms at p99 unbudgeted, so 2 ms clips about the worst 1% of frames. Nothing
      measured argues for 2 over 3.

#### M2d-2 — Cave optimisation ✅ done 2026-08-28, 274 tests passing

- [x] **Coarse cave lattice with trilinear interpolation.** `CaveLatticeStep` (default 4)
      samples cave fBm every 4 voxels and interpolates between, so a chunk costs 729 noise
      evaluations instead of 32,768. Release, 2,048 chunks: **2.23 → 0.47 ms/chunk**, caves
      from 83% of generation down to 20%.
- [x] **One definition of the field.** `IsCaveAt` resamples the same lattice rather than
      keeping an "exact" path beside it, and agrees with `GenerateTerrain` bit for bit.
      Two definitions drift, and the symptom is mining a wall that is already air.
- [x] **The step must be a power of two dividing the chunk size**, so a chunk boundary is a
      lattice point and independently generated neighbours interpolate from the same corners.
      The constructor throws otherwise — the damage is a seam, which nothing detects at
      runtime.
- [x] **Measured in the game, not just on the bench.** r12h/6v at 40 m/s: 145 → 731 chunks
      drawn, jobs done 19,738 → 52,120, dropped 30,792 → 16,188.
- [x] `MinerCraft.Bench` — repeatable worldgen harness over the same 2,048-chunk region M2b
      used, reporting time *and* accuracy, because a speed number without an accuracy number
      is half a trade. `--cavestep=N` on the game does the same A/B in the streaming loop.
- [ ] **The cave threshold has not been re-tuned for the lattice, and should be.**
      Interpolation only ever *removes* caves (measured: voxels gained ≈ 0.001%), because
      linear interpolation cannot exceed its endpoints and the threshold at p95 selects
      exactly the peaks that get clipped. Step 4 costs ~10% of cave volume — 1.444% of rock
      carved down to 1.292%. Recovering it means nudging `CaveThreshold` below 0.42, which is
      a worldgen tuning decision and was deliberately not bundled into an optimisation.
- [ ] Steps 8 and 16 are **not** free lunches despite tiny voxel-disagreement figures (0.54%,
      1.21%): they cost 37% and 83% of cave volume respectively. If the step is ever raised,
      judge it on the `carved` column, not the `lost` column.

#### Settings

- [ ] **Expose view distance as a player option.** `ChunkStreamer.UpdateViewer` already
      handles radius changes by box difference, and there is a test for it, so the mechanism
      exists. Presets grounded in the M2d-1 sweep, at walking speed and **excluding rendering
      cost**: Low r4h/2v (1,521 chunks, ~32 MiB), Medium r8h/4v (5,733), High r10h/5v
      (9,375, ~87 MiB), Ultra r12h/6v (14,297, ~122 MiB). The chunk counts are geometry and
      still hold after M2d-2; what moved is how much of the box actually gets *drawn* while
      moving, which is where the memory figures come from. Re-sweep before writing the
      presets into a settings menu.

### M3 — Mining ← first fun moment

Planned 2026-08-29 and split into four parts in [docs/M3-PLAN.md](docs/M3-PLAN.md). **Read
it before starting** — M3a is a concurrency part and the failure it prevents is a crash on a
worker thread, not a wrong voxel.

#### M3a — The edit path ✅ done 2026-08-29, 294 tests passing

- [x] **Copy-on-write on `VoxelChunk`.** A chunk instance handed to a job is immutable from
      then on; an edit clones it and installs the clone. `ChunkPipeline.Neighbourhood()`
      already captures references on the sim thread, so the worker's snapshot stays valid
      with no lock anywhere on the read path.
- [x] This closes **both** recorded revisit triggers: M1b's "no concurrent writer until M3"
      and M2a's "the worker-side padded copy is safe because nothing writes yet".
- [x] Dirty chunks coalesce into a set drained once per frame — never a remesh per voxel.
- [x] Border rule: an edited voxel at local 0 or 31 on any axis dirties the neighbour on that
      axis too, up to seven of them at a corner. Getting it wrong leaves stale geometry along
      chunk seams, which is where you mine most.
- [x] **Use `band3` of the job priority** for edit remeshes — the band M2a reserved for
      exactly this. Without it the rock you just mined stays on screen while the horizon
      loads.
- [x] Set a `HasEdits` flag on first write. One bit, free now, and it is exactly the set M6
      has to save.
- [x] The fuzz that matters: rewrite every voxel to version *k+1* per edit, so any snapshot a
      worker takes must be **uniform**. A torn read shows two versions and the assertion is
      one line.
- [x] **That fuzz hung instead of failing the first time it was checked**, because its readers
      stopped draining on the first mismatch and deadlocked the writer against a bounded queue.
      Now records and continues, and fails in 356 ms. **Watch a fuzz test fail before trusting
      it** — the same applies to whatever M3b writes against a brute-force reference.
- [x] `ChunkPipeline.ChunkClones` counts the clone rate. M3c gave it something to count; the
      number itself is still unmeasured and M3d settles it, so it is tracked there rather
      than here.

#### M3b — The raycast ✅ done 2026-08-29, 307 tests passing

- [x] Amanatides & Woo voxel DDA. Returns hit voxel, face normal, entry distance, and the
      last empty voxel before the hit — which is where M6's placement goes, so returning it
      now avoids revisiting.
- [x] Fuzzed against brute force the way `ChunkBox` was — but **not** against the fine-step
      sampler this plan called for. A sampler is *less exact than the DDA*: a ray can clip a
      voxel corner for less than one sample step, so it misses voxels the walk correctly
      reports, and the first version of this fuzz failed on the code being right. Replaced
      with an exact ray/AABB slab test, which is also a genuinely different algorithm.
- [x] Watched fail before being trusted: flipping the boundary calculation breaks it in 7 ms.
- [x] **An unloaded chunk must stop the ray, not read as air.** Otherwise the player targets
      a voxel in a chunk that does not exist and the edit vanishes with nothing to report.
- [x] Ray starting inside solid material returns that voxel with no normal, or the first
      swing mines whatever the camera is embedded in.

#### M3c — The carve ✅ done 2026-08-29, 364 tests passing

- [x] Sphere by voxel-centre distance test, radius in metres from the tool. `Carve.Sphere`
      in `MinerCraft.Core/Mining/`, with `Carve.Swing` reading its two numbers off a
      `ToolDefinition`. **"Sub-voxel" means the voxel is small, not partly full.**
- [x] **The distance test is the definition; the bounding box is only an optimisation.** The
      box is `VoxelPos.FromMetres(c ± r)` — provably sufficient, correct at negative
      coordinates for free, and reusing a flooring primitive that is already pinned. Do not
      replace it with a hand-derived tight bound: the first version was one, and its obvious
      break-recipe (floor → cast) can only *widen* the box, so it could never fail a test.
- [x] Hardness gates removal per voxel. `MaterialTable.Hardness` projects it now, and the two
      shipped tiers straddle dirt and stone so the gate is visible: `hand_pick` leaves a stone
      seam standing in a dirt bank, `steel_pick` takes it.
- [x] Tool tiers load from `mods/minercraft/tools/*.json` through the same loader materials
      use — non-negotiable 5. The JSON was not cut; two tiers ship, because **one tier cannot
      demonstrate a gate**.
- [x] `CarveResult` reports removed, too-hard and unreachable separately. **Unreachable is not
      too-hard**, for the same reason `RayOutcome.Blocked` is not `Missed`.
- [x] **A tool radius is a denial-of-service vector, and a NaN threshold is worse.** The scan
      is cubic, so `"radiusMetres": 1000` freezes the game rather than carving a crater;
      `ToolDefinition.MaxRadiusMetres` bounds it at load *and* in the carve. It is **4 m and
      an estimate** — 16 m was the first answer and did not bound the failure its own comment
      named. A NaN threshold fails the other way, *open*: `hardness > NaN` is false, so the
      gate inverts from "takes nothing" to "takes everything".
- [x] **`ToolDefinition.Unknown` is inert twice over**, and was inert zero times when first
      written. Radius 0 still met the distance test on a swing landing exactly on a voxel
      centre, and threshold 0 took anything of hardness 0. The test that should have caught
      it swung at a voxel *corner*. Non-positive radius now returns early, and the threshold
      is negative infinity.
- [x] **A viewer leaving mid-remesh stranded the mesh on screen.** `Meshed → Meshing` is an
      M3 edge; before it, "is it `Meshed`?" and "does the renderer hold a mesh?" were the same
      question. `StreamedChunk.HasLiveMesh` is the flag now. Mine, fly away, and the pre-edit
      geometry used to keep drawing for unloaded terrain.
- [x] **The base game's own files are loaded by a test at last.** `ShippedContentTests` reads
      `MinerCraft.Game/mods/` from disk — every other content test builds a fixture, which had
      already drifted. It checks the shipped *numbers* too, because the hardness gate is a
      property of the content and no code test finds a JSON file edited to make it pointless.
- [x] Eight mutations watched to fail before the tests were trusted. One of them exposed a
      *comment* that overclaimed: the pipeline test's "eight chunks remeshed" does not catch
      the border rule failing, because the writes span eight chunks by themselves. Split out a
      one-voxel corner test that does. **A green test is not evidence its comment is true.**
- [ ] Not measured yet: the clone rate under a held button. `ChunkPipeline.ChunkClones` counts
      it and a carve finally drives it, but nothing holds the button — that is M3d.

#### M3d — Binding, feel, measurement ✅ done 2026-08-29, 367 tests passing

- [x] Left mouse mines on the flycam, number keys pick a tier, Esc stops both looking and
      mining. `MiningController` is Godot-free so `--mine` can drive it headlessly.
- [x] Wireframe highlight on the targeted voxel, one `RenderingServer` instance moved rather
      than churned. **Verified in a windowed run 2026-08-29** {D} it draws where it should, so
      the inflation is enough to keep the lines off the faces they outline.
- [x] **The swing is centred on the hit voxel's centre**, not the ray's entry point. The
      entry point lies on a face, so half the sphere starts in air and the reach depends on
      the angle of incidence — a radius in a mod's JSON would mean something different every
      swing. `Carve.SwingCentre`, in Core, because where a swing lands is a game rule.
- [x] **Answered: yes, a pick swing fits in a frame, with room to spare.** Mining
      continuously at 5 swings/s while flying r6h/3v at 8 m/s moves `update ms` p99 from
      0.910 to 1.038 ms and the main thread from 0.80% to 1.02% of one core, against a
      16.7 ms frame. **Do not build** the dirty-region mesher, smaller mesh blocks, or a
      latency-hiding scheme until something asks for them.
- [x] **M3a's clone estimate settled: right per chunk, 7× pessimistic per swing.** Measured
      1.2 clones and 2.4 KiB per swing against a guess of 8 chunks and ~16 KB. Eight chunks
      is the worst case, at a corner; a 0.5 m sphere usually stays inside one.
- [x] The raycast and carve themselves are 0.044 ms mean / 0.17 ms max. The cost is all in
      the remesh, which is on workers, and its apply is already budgeted.
- [x] **The headless frame-time trap, third time.** A mining run showed `frame ms` max
      19.6 ms against a 6.9 ms control while `update ms` max moved 0.13 ms, and it did not
      reproduce twice. Use `update ms`.

#### Surfaced, not M3c's or M3d's

- [ ] `JobSchedulerTests.AThrowingJobFaultsWithoutTakingTheWorkerDownWithIt` failed once under
      heavy machine load and passes otherwise. It is a one-worker scheduler test with a
      wall-clock `Patience`, so it is load-sensitive by construction — worth knowing before
      it fires in CI and gets read as a real fault.
- [ ] `MaterialTable` indexes raw, so a material ID past its `Count` throws
      `IndexOutOfRangeException`. The mesher has the same exposure and would hit it first, but
      the carve is the first consumer a *player* triggers, so the same latent bug becomes a
      crash on click rather than at load. The fix is validating chunk contents at load: M6.

#### Open decisions

- [ ] **Instant removal or progressive damage?** M3 ships instantaneous with a per-tier
      cooldown, and the thing is now in hand — so this is answerable by playing it, which is
      Matthew's call rather than a measurement. Progressive needs per-voxel damage state,
      which chunk storage has nowhere to put, so it wants its own design pass rather than a
      field bolted on. `ToolDefinition.CooldownSeconds` already exists either way.
- [x] **Played 2026-08-29: mining feels good.** The verdict M3 was actually waiting on, and
      the one no profile could give.
- [ ] **Reach reads a little long at 5 m**, which is twenty voxels. Fine for a creative-mode
      flycam, so unchanged {D} but **reach is the player's, not the tool's**, and M4 is where
      something exists to own it. A survival reach wants to be shorter. **M4c moved it to
      `PlayerController.ReachMetres`** — still 5 m, still deliberately not in the tool JSON, and
      **M4d settles the number by playing.**
- [ ] **Is the bite the right size, and does the hardness gate read?** `hand_pick` (0.4 m,
      leaves stone) against `steel_pick` (0.5 m, takes it). Numbers, not code — they are two
      JSON files, so tuning them needs no rebuild.
- [ ] **Where is the swing centred — the ray's entry point, or the hit voxel's centre?** The
      entry point lies exactly on a voxel face, so about half the sphere lands in air and the
      effective reach depends on the incidence angle; the voxel centre is grid-aligned and
      stable under repeated swings but makes the crosshair feel like it lies near a voxel
      edge. **`CarveResult.AlreadyAir` against `Candidates` is the number that settles it**,
      which is why both are reported. M3d decides, with the thing in hand.
- [ ] **Edits are lost on unload at M3** — a chunk regenerates from the seed, so flying far
      enough erases the mine. Acceptable until M6, recorded so it is not filed as an edit-path
      bug.

### M4 — Player ⬅️ **in progress: M4a, M4b and M4c done, M4d next**

Planned 2026-08-29 and split into four parts in [docs/M4-PLAN.md](docs/M4-PLAN.md). **Read it
before starting.** The structural point: until now the viewer's position was an *input* and
the streamer followed it. From M4 the position is an *output* of a query against the very
chunks the viewer decides to load, and an unloaded voxel read as air means the player falls
out of the world and accelerates away from anything that could stop them.

#### M4a — The collider ✅ **done 2026-08-29**

`MinerCraft.Core/Physics/` holds `Aabb` and `VoxelCollider`. 33 new tests, suite 367 → 400.

- [x] AABB vs voxel grid, **axis-separated** resolution (X, then Y, then Z). A combined sweep
      snags on every voxel boundary of a flat wall; axis separation makes sliding the default.
- [x] Do **not** use Godot physics for terrain (`ConcavePolygonShape3D` is a trap)
- [x] **Substep so no substep exceeds 0.2 m**, under one voxel. Move-then-resolve is exact
      below 15 m/s (0.25 m ÷ 1/60 s) and silently tunnels above it — so **walking never
      tunnels and falling always does**, which is the worst possible shape for a bug.
- [x] The substep cap is on **displacement, not the box's dimensions**. A 0.25 m floor is
      thinner than the player. On the vector's *magnitude*, not per axis — 0.2 m on each of
      three axes is 0.35 m of travel, and there is a test for exactly that.
- [x] **An unloaded voxel is solid.** `ChunkStreamer.TryGetMaterial` hands back air *and*
      false; a caller that ignores the return value gets the unrecoverable failure. Same
      distinction as `RayOutcome.Blocked` vs `Missed`, worse consequence.
- [x] Solidity is a predicate — `id != air && !IsFluid(id)` — not a comparison against air.
      `MaterialTable.IsFluid` already exists, so this is correct now *and* at M8, and needs no
      revisit trigger the way `VoxelRay` did.
- [x] `grounded` is an **output of the solve** ("the −Y resolve fired"), not a separate probe.
      One step of latency for step-up, against ~40 extra voxel reads per step to remove it.
      `MoveResult.Grounded`, plus `HitCeiling` and `BlockedHorizontally` for M4b.
- [x] **Resting contact:** resolve to the face minus an epsilon, overlap is a strict
      inequality. The bug that survives into shipped games is a millimetre of jitter with
      `grounded` flickering and jump misfiring — not tunnelling.
- [x] **Which of those two is load-bearing was measured, and it is the strict inequality.**
      Setting `ContactEpsilonMetres` to zero leaves the whole suite green: a face exactly on a
      voxel plane is touching, not overlapping, and voxel planes are exact in binary. The
      epsilon is kept as belt and braces, and its honest cost is recorded below.
- [x] **`Aabb` stores a minimum corner and a size, not two corners**, and that is what makes
      standing still bitwise stable. A resolve places one face from the blocking voxel's index
      and carries the size across untouched; deriving the size as `Max − Min` every step would
      round it differently each time and a resting player would creep.
- [x] Lives in `MinerCraft.Core/Physics/`, `System.Numerics`. Where a player may stand is a
      world rule a multiplayer server must agree about.
- [x] **The fuzz with the sharp oracle: seal the player in a room** of solid voxels and apply
      thousands of random displacements, including magnitudes far past terminal velocity.
      Escaping is a boolean — no reference implementation, no tolerance. **Watched to fail:**
      raising `MaxSubstepMetres` to 1.0 m fails 14 of the 33 tests, the fuzz among them.
- [x] Corner-snag test: sliding diagonally along a flat wall travels the full tangential
      component. Fails if someone "simplifies" the resolve back into one pass — watched, by
      cancelling every axis when any one is blocked.
- [x] Shared postcondition asserted in *every* motion test, not only the fuzz: the box never
      ends a step overlapping solid. Lives in the `Step()` helper, so it cannot be forgotten.
- [x] Standing still for 10,000 steps does not drift and `grounded` never flickers. One
      settle of 0.1 mm on the first step, then bitwise nothing.
- [x] Determinism: identical input sequence, bitwise-identical trajectory. The whole of the
      multiplayer claim, and cheap now. Compared through `SingleToInt32Bits`, not `==`.
- [x] **Measured, so M4d does not have to: a walking step costs 41 `TryGetMaterial` calls**,
      not the guessed ~400, and the worst alignment is 63 per substep — 252 at a four-substep
      terminal-velocity fall. A resolve reads one voxel *layer*, not the whole box. **No
      memoisation, no spatial structure**; the guess was pessimistic and the fix it proposed is
      not needed.
      - The layer counts are not the 16 + 4 + 16 the box's nominal 2 × 8 × 2 suggests: a 0.5 m
        box that is not grid-aligned straddles **three** voxel columns, not two.

##### Found while building it, and worth keeping

- [x] **The voxel layer is taken from the *moved* box, never from "one past where the box
      was".** The two are the same layer while the box is outside the wall, so nothing in the
      suite could tell them apart — until a test was added for a box that *starts* inside one.
      The second form finds no new layer ahead of an already-overlapping box and waves the move
      straight through, walking it out the far side of the wall in about ten steps. It also
      cannot teleport the box forward past what the move asked for.
- [ ] **Knife edge: a gap exactly the box's width.** The resolve stops the leading face an
      epsilon short of contact, so a 0.5 m box in a 0.5 m gap has nowhere for that margin to
      come from and the trailing face ends 0.1 mm inside the far wall. It cannot pass through
      either side, and a straight drop down such a shaft is exact because an axis with no
      displacement is never resolved — both pinned by tests. Only reachable if a corridor is
      dug to precisely the player's width; `hand_pick` leaves 0.75 m. Setting the epsilon to
      zero would remove it, at the cost of the defence it exists to provide.
- [ ] **Float positions cap the usable world at a few kilometres**, long before `VoxelPos`'s
      `int` does. One float ULP passes the contact epsilon around ±800 m. **Revisit trigger:**
      a world meant to be walked further than that wants a *floating origin*, which is what
      every large-world engine ends up building — not a wider float, which `VoxelPos` would
      outrun anyway.
- [x] **A displacement needing more than 1,024 substeps is refused**, the same class of bound
      as `ToolDefinition.MaxRadiusMetres`. 204 m in one step is a velocity that has blown up,
      not a fast body, and the alternative is a loop long enough to look like a freeze. An
      estimate, and labelled one: it bounds a failure, it does not describe a limit.
- [x] **`VoxelCollider.OverlapsSolid` is public**, because M4c's unstick rule needs exactly it
      and so does every motion test's postcondition. `ResolveAxis` already gives partial
      unstick — it clears the layer the leading face is in — but not the full rule, which
      stays M4c's.

#### M4b — Motion ✅ **done 2026-08-29**

`MinerCraft.Core/Physics/` gains `PlayerMotion` and `PlayerMotionConfig`. 35 new tests, suite
400 → 435.

- [x] Gravity, jump, terminal velocity, step-up **and step-down**. `PlayerMotion.Step` is a pure
      function of a state, an intent and a world — no clock, no accumulator, no field.
- [x] **60 Hz fixed step, which is not the 20 Hz sim tick.** 20 Hz is 50 ms of input latency.
      Machines are simulated; the player is driven. Core's step takes `dt` so a test or a
      headless profile can drive it, and nothing in Core knows the rate.
- [x] Constants, **all estimates and labelled so in their own remarks**, replaced by playing at
      M4d: box 0.5 × 1.8 × 0.5 m, eye 1.65 m, gravity 25 m/s², jump 7.5 m/s, walk 4.5 m/s,
      sprint 7 m/s, terminal 40 m/s, step height 0.5 m.
- [x] **The apex is 1.1875 m, not the 1.125 m the plan derived** — measured to four decimals.
      `v²/2g` is the continuous answer; a jump that *replaces* its step's gravity rather than
      being reduced by it overshoots by exactly half a step of launch speed, `v·dt/2` = 0.0625 m.
      So the clearance over a four-voxel ledge is **0.1875 m**, half again what the plan claimed
      — and it is **rate-dependent**, twice as much at 30 Hz. The apex test asserts one-sided,
      because the direction is a design claim rather than an accident.
- [x] **Reach did not move here.** The plan's table lists it under M4b; it is not a body
      dimension, it is what an arm does, and it stays on `MiningController` until M4c has a
      player to own it. `EyeHeightMetres` *is* in the config, because it is the third dimension
      of the same body — splitting those across two files is how one goes missing.
- [ ] **Terminal velocity is a streaming parameter in a physics costume.** 16–24 m of loaded
      world below the player is crossed in 0.4–0.6 s at 40 m/s. Still M4d's to pick, and see the
      missing signal below.
- [x] **The player must fit down their own tunnel.** `hand_pick` (0.4 m) leaves a 0.75 m free
      bore; a 0.5 m box passes with 0.125 m either side. **Player width and the weakest tool's
      bite are coupled** and neither can be changed alone. Written into `WidthMetres`'s remarks
      where a person changing it will read it.
- [x] Step-up, with all four guards and a test each: grounded only, non-upward only, the
      horizontal re-attempt must **fully** succeed, and the whole box needs headroom at the
      lifted position. **The refusal tests change exactly one thing** against a climb that
      succeeds — a refusal test that builds its own scenario proves the body did not climb, not
      that the guard is why.
- [x] The headroom guard is **structural rather than a separate check**: the lift and the
      re-attempt are real `Move` calls and a `Move` that did not start overlapping cannot end
      overlapping. Tested anyway, because a guarantee that comes from somewhere else is the kind
      that quietly stops holding.
- [x] Step-down, the half people forget: without it a downhill slope makes the player airborne
      every few steps and the camera bobs continuously. Guards mirror step-up's, and a staircase
      of single voxels is now walked up **and back down** without ever leaving the ground.
- [x] Coyote time, jump buffering and variable jump height are M4d's and are additive — the
      model does not change to accept them. Jump is level-triggered for now, so holding the
      control bounces; edge-triggering needs the previous frame's button in `PlayerState`, which
      is jump buffering's field.

##### Found while building it, and worth keeping

- [x] **The lift snap is what keeps M4a's epsilon claim true, and nothing caught removing it.**
      An unobstructed lift is placed at its exact target rather than at accumulated substeps.
      With `ContactEpsilonMetres` in place that changes no outcome — a resting body floats 0.1 mm
      up and that swamps a few ULP. But M4a *recorded* that the epsilon can be set to zero with
      the suite green, and with the lift accumulated instead of snapped, zeroing it **fails four
      tests**. A documented finding in the previous milestone was one line away from going stale
      with nothing failing to say so. Now caught without touching the epsilon, by a test that
      places the feet exactly on a voxel plane — which is what M4c's spawn and a fly toggle both
      do.
- [x] **A step-up settles by exactly what it lifted**, so it is a climb and never a fall. What
      that protects is step-down's distance limit: a settle that could go further would give the
      same rule two answers depending on whether the body happened to climb something first.
      Nothing caught it either, because reaching it needs a body that clears what it just climbed
      in one step — 0.075 m of walking against a 0.5 m body cannot, so the test uses a 45 m/s
      body and says why.
- [ ] **Recorded cost: step-up's "fully succeed" guard makes wall-hugging stop you dead.**
      *Not encountered in the 2026-08-30 play-test, but that test walked up hills rather than
      into corners — the case needs provoking deliberately and has not been.*
      Walking diagonally into a corner where one axis is a climbable lip and the other is a tall
      wall refuses the climb, because the lifted re-attempt is still blocked on the wall. That is
      the plan's specified behaviour and it is safe. **If M4d finds it reads as stuck, the
      relaxation is one line:** require the re-attempt to be blocked on no axis the original move
      was not already blocked on. Pinned by a test either way.
- [ ] **M4d needs a signal that does not exist: `MoveResult` cannot tell "stopped by rock" from
      "stopped by an unloaded chunk".** That is exactly *steps spent held*, the number meant to
      pick terminal velocity. It is a flag on `IsSolidUncounted` and a field on `MoveResult`,
      deliberately not built now — an unread flag with no profiler consuming it is a number
      waiting to be believed.
- [x] **One `StepHeightMetres` for both directions**, because stepping down a lip you just
      stepped up should not behave differently. **Revisit trigger:** M4d, if a 0.5 m instant drop
      reads as a jolt where the matching rise does not. The split is one field and one call site.
- [x] **Horizontal velocity is replaced from intent every step, not accelerated.** No friction,
      no momentum, full air control — the snappiest model and the one whose faults are easiest to
      judge by playing. Acceleration, air control and ground friction are three separate feel
      decisions and a person makes them. The blocked-axis zeroing is currently only honest
      reporting and is kept because it stops being cosmetic the moment M4d adds acceleration.
- [x] Cost, as a regression detector rather than a measurement: a walking step is **one `Move`**
      and M4a's ~41 voxel reads; a successful step-up is **four** — the failed attempt, the lift,
      the re-attempt, the settle.
- [x] `TestWorld` moved out of `VoxelColliderTests` into a shared `PhysicsTestHelpers`, for the
      reason `ShippedContentTests` exists. The property it preserves is that an unloaded chunk
      answers *air **and** false*; a second copy that drifted would let a body which ignores the
      return value pass its own suite.

#### M4c — Binding ✅ **done 2026-08-29**

- [x] **Do not delete the flycam.** `FlyCam`'s own comment says M4 replaces it; that comment
      is wrong and following it destroys the measurement apparatus M2d and M3d were built to
      create. `--profile` assigns `GlobalPosition` directly and `AdvanceFlythrough` snaps the
      camera every frame; a camera parented to a physics body cannot do either.
      `FlyCam.FlightEnabled` is the whole of it: false in walk mode, and **only the movement
      is switched off — mouse-look stays live in both modes**, because looking around is the
      same act whichever body you are and because walking needs a yaw to walk along.
- [x] Walk/fly toggle on **F**, **`--profile` implies fly**, body inert in fly mode. A profile
      run also *refuses* the toggle mid-run, because handing a scripted flythrough's camera to
      a body invalidates the measurement rather than producing an interesting one. Verified:
      the default headless run reports **1521 viewed / 165 drawn / 1926 jobs**, M2d's recorded
      numbers unchanged to the digit.
- [x] Order within a step: **player → viewer → pipeline**. The step is in `_PhysicsProcess` and
      the rest in `_Process`; Godot runs every physics tick of a frame before that frame's
      `_Process`, so the order holds without a manual ordering.
- [x] **`ReachMetres` moved to `PlayerController`**, closing M3d's recorded item.
      `MiningController.ReachMetres` is now `required` and defaultless, so there is no second
      number to drift — `PlayerController.DefaultReachMetres` is the only literal.
      **Deliberately still 5 m**: the plan's table says 3.5 m and the play-test says 5 m reads
      long, both are feel, and **M4d settles it**. Moving the field and retuning it in one pass
      would make it impossible to tell which change did what.
- [x] **Spawn is the cycle at its worst**, and the order is **gate, then unstick, then step**.
      `PlayerSpawn.IsWorldReadyFor` checks every chunk the box overlaps *and the chunk beneath
      each of them*; `FeetOnSurface` places the feet on the row above the surface voxel;
      `TryUnstick` lifts plane by plane to a 4 m bound. Measured: **13–14 steps held for the
      world**, about 0.22 s, on the default headless run — it moves run to run with worker
      scheduling, so it is a scale and not a constant.
- [x] **Unsticking before the gate is the unrecoverable order.** Against an unloaded world every
      candidate is solid, the search runs to its bound, and a body standing in open sky is
      reported stuck. `UnstickFailsAgainstAnUnloadedWorld` pins it.
- [x] **An unstick lifts and never drops** — a body with its head in a ceiling goes up through
      it and onto the roof, not down out of it. Up is the only direction whose answer does not
      depend on which face was overlapping, and a body that can be pushed down by material
      above it can be pushed into the floor. **Caught by nothing until a test was written for
      it**; see the two green recipes below.
- [x] Mining the floor from under yourself works for free and is correct. **Recorded revisit
      trigger:** material appearing *inside* the box has no answer at M4 — M6's placement can
      do it deliberately, M9's collapse by accident — and `PlayerSpawn.TryUnstick` is its seed,
      including its up-only decision.
- [ ] Not a bug, and will be reported as one: walking into your own tunnel is walking into the
      dark until M5.

##### Six break-recipes, two green, and they are two different things

- [x] Caught: the gate always saying yes (3 tests), the gate dropping the chunk beneath (1, and
      nothing else), unstick accepting the first candidate blindly (5), `FeetOnSurface` using
      the surface row rather than the row above (3).
- [x] **Green, and a real hole: unstick starting at the box's own voxel row.** Every buried-body
      test placed the feet on a plane already, so the extra downward candidate was the box
      itself and overlapped by construction. `UnstickNeverMovesABodyDownward` closes it.
- [x] **Green, and a documentation correction: `Aabb.Translated` instead of rebuilding the
      box.** No failing case exists — `Min.Y + (y − Min.Y)` is exact for every pair a 4 m lift
      can produce inside the few-kilometre world M4a's float analysis allows, and the horizontal
      `+ 0.0f` is exact for every float except `−0.0f`, which `Aabb.FromFeet` cannot produce.
      Kept for rule-consistency with M4a's corner-and-size storage, **not** because a test can
      tell. Same shape as M4a's zeroable epsilon.
- [x] **Feet exactly on a voxel plane survives exactly one step.** The end-to-end spawn test was
      written expecting bitwise identity and was wrong: the first −Y resolve places the body
      `ContactEpsilonMetres` clear, so 1.25 m becomes 1.2501 m — the number the headless run
      prints. The state exists only at spawn and at a mode toggle, which is why M4b's lift test
      constructs it by hand. The *second* step is bitwise identical, so rest is stable.

##### What M4b hands M4c, so it isn't rediscovered at the binding

- [ ] **`PlayerState.AtFeet` is deliberately *not* grounded.** Nothing has looked yet; the first
      step's gravity finds the floor and says so. A spawn that hands back `Grounded: true` lets
      step-down fire before the body has ever been anywhere.
- [ ] **Spawn and the fly→walk toggle both place the feet exactly on a voxel plane**, because
      neither goes through a resolve that would add the contact epsilon. That is the exact case
      `ALiftOfExactlyTheStepHeightClearsALipOfExactlyTheStepHeight` pins — so do not nudge the
      spawn by an epsilon to make it look tidy, and do not be alarmed that the first step settles
      by 0.1 mm.
- [ ] **The camera and the mining ray share one origin:** `PlayerState.EyePosition(config)`.
      Bind both from that call rather than deriving the offset twice, which is how a half-height
      goes missing in one of them.
- [ ] **`dt` comes from `_PhysicsProcess`; Core does not know the rate.** `project.godot` sets no
      `physics_ticks_per_second`, so it is Godot's default 60 Hz, which is what M4b's constants
      were measured at. **Changing it changes the jump height** — the apex overshoot is
      `v·dt/2`, so a 30 Hz tick jumps 6 cm higher. Leave it alone or re-measure.

#### M4d — Feel and measurement

- [ ] **The question M4 is really asking: can the player outrun the streamer, and what does it
      feel like when they do?** M2d measured a horizontal flythrough at constant height. A
      fall is vertical, fast, and against the *small* radius.
- [ ] Add a fall mode to the profiler the way `--mine` was added. Report voxel queries and
      substeps per step, **steps spent held** on an unloaded chunk and the longest such stall,
      ground-state flicker count, and collider cost per step.
- [ ] **Time it directly; do not read `frame ms`.** The headless frame-time trap has been
      walked into three times — M2d-1, the mesh-apply budget, M3d — and each time the spike
      did not reproduce while the directly-timed number moved a tenth of a millisecond.
- [x] **Played 2026-08-30: the jump feels good.** The first of M4b's constants to get a verdict
      from a person rather than a number, and it is the one the plan was least sure of. 7.5 m/s
      against 25 m/s² stands, and the 1.1875 m apex is a keeper.
- [ ] **Jump height may become an option, possibly with optional double jump** {D}. Two notes so
      neither is re-derived:
      - **A "jump height" setting is not `JumpSpeedMetresPerSecond` under a different name.**
        Inverting the apex needs the *discrete* answer, `v²/2g + v·dt/2`, not the continuous
        `v²/2g` — and the second term is rate-dependent, so a height expressed in metres is only
        honest at a stated tick rate. Solve for `v` with the overshoot in, or the setting reads
        6 cm short at 60 Hz and 12 cm short at 30.
      - **Double jump is additive and cheap: one counter on `PlayerState`.** It is the same
        category M4b already named for coyote time and jump buffering — `PlayerState`'s "three
        fields and no more" comment anticipates exactly this, and `Step` stays a pure function
        with a fourth. It does not disturb step-down's non-upward guard, which already refuses
        while the body is rising.
- [x] **Played 2026-08-30: 0.25 m ground walks like ground.** "Walking uphill occurred
      naturally, I did not need to jump." This is the question M4d existed to ask and the one no
      profile could answer — DESIGN's claim that step-up is *not* a convenience here but the
      difference between walking on the ground and not is confirmed by a person. **Step height
      stays 0.5 m.**
- [ ] **Two step-up/step-down corner cases are still unjudged, and ordinary walking cannot reach
      them.** Neither was reported, which is silence rather than evidence:
      - **Guard 3's wall-hug** needs walking *diagonally* into a corner where one axis is a
        climbable lip and the other a tall wall. It is the case that would read as stuck, and it
        will not turn up by walking up a hill.
      - **Step-down's 0.5 m instant drop** needs a lip walked *off*, not up. The revisit trigger
        was always "does the drop jolt where the matching rise does not".
- [x] **Played 2026-08-30: you "sortof" fit down a tunnel you carve.** The coupling M4-PLAN named
      holds, and the hedge is exactly what its arithmetic predicts: `hand_pick`'s 0.4 m radius
      leaves a 0.75 m bore, a 0.5 m box passes with **0.125 m either side — half a voxel**, and
      consecutive swings centred on different voxel centres wander the bore's axis by more than
      nothing. So it fits on paper and scrapes in practice.
- [ ] **If the tunnel wants to feel roomier, the cheap lever is the tool, not the player** {D}.
      `hand_pick`'s `radiusMetres` 0.4 → 0.5 makes the bore five voxels, 1.25 m, clearing 0.375 m
      either side. It is **one number in one JSON file and needs no rebuild**, which is what M3c
      made tool tiers content for. Widening the *player* instead is the move M4-PLAN warns
      against, because width is the half of the coupling that is not free.
- [ ] Needs a human, still: is **5 m** the right reach now something owns it.

#### Deliberately not in M4

- [ ] Swimming and fluid drag — M8. The solidity predicate already reads `IsFluid`, so wading
      is the default rather than a special case.
- [ ] Falling damage — M11, where health is. Dropped items — M6, and DESIGN is explicit that
      Godot physics is fine for those.
- [ ] Placement colliding with the player — M6, using M4c's unstick rule.
- [ ] Non-cube collision shapes (slabs, ramps, ladders) — all "a voxel is one material or
      another" violations wearing different hats; M3c already argued that one.
- [ ] `PlayerTick` on the event bus, and multiplayer prediction. A fixed-step solve in Core
      with a determinism test is the whole of what M4 owes them.

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
