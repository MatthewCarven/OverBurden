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
- [x] **Mining granularity: sphere on the bare click, single voxel under Ctrl.** Raised and
      decided 2026-08-30; built the same day as `Carve.Chip` / `Carve.Voxel`, 9 tests, six
      break-recipes all caught. DESIGN's "mining is sculpting" line stands and now records that
      the alternative was considered and why it lost. The reasoning, kept: The capability is not in question — both exist and the modifier is the
      mechanism. **What is in question is which one a bare click does**, and it is an *economy*
      decision rather than a control one:
      - **It reverses a DESIGN line.** "Mining is sculpting: a pick swing removes a sphere or
        cone of material" — DESIGN.md, *Mining and placement — decoupled granularity*. Changing
        the default is a change to that, and DESIGN is where locked decisions live, so it wants
        a deliberate edit rather than a drift.
      - **The cost is ~17× excavation throughput with the starting pick.** `hand_pick`'s 0.4 m
        sphere is about 17 voxels; one voxel is one voxel. DESIGN's own argument for volumetric
        inventory is that it "makes big excavations meaningfully productive", and this game's
        premise is a *mining operation* fighting water and gravity. **The Taj Mahal case and the
        mining-operation case want opposite defaults.**
      - **Decided: the sphere is the bare click and the single voxel is the modifier.** Same
        two capabilities, same one modifier, no DESIGN reversal, and the productive action stays
        the default in the game about being productive. Precision is opt-in for the person who
        has decided to build something, which is the person who will happily hold a key.
      - **No conflict with volumetric inventory** — that one is safe either way. A voxel is
        0.25³ m³ = **15.625 litres**, so "one block at a time" is a *mining granularity* and
        litres-not-counts is an *inventory unit*. Two separate decisions; neither constrains the
        other.
- [x] **Built: `Carve.Chip` takes `hit.Voxel` itself, not a sphere centred on it.** So
      `VoxelHighlight`'s outline stops being an indication of roughly where the bite lands and
      becomes literally the voxel about to be removed. Same hardness gate and same cooldown as
      the sphere — **precision changes how much a swing takes, never what it is strong enough
      to take**, or the modifier would become a way to mine anything one voxel at a time and
      M3's whole progression would be advisory.
- [x] **The shared `Take` is the evidence the gate was unified rather than forked.** Removing
      the hardness check from it fails **9** tests, **seven of them pre-existing sphere tests**.
      One edit breaks both shapes, which is exactly what a second copy would not do.
- [ ] **The modifier key: Ctrl is bound to descend in the mode a builder is most likely in** {D}. `FlyCam`
      binds Ctrl to descend and Alt to crawl, both fly-only — so in **walk** mode Ctrl and Alt
      are free, and in **fly** mode both are taken. Shift is sprint and stays sprint. **Shipped
      on Ctrl as asked, and it is fine in walk mode where Ctrl is free.** The cost is fly mode:
      holding Ctrl descends at `MoveSpeed`, so at the default 6 m/s and `hand_pick`'s 0.3 s
      cooldown a builder sinks **~1.8 m between chips**. **Alt is the better key and the swap is
      one line** — it is free in walk mode and means *crawl* in fly mode, so "slow down to do
      fine work" pairs rather than fights. Left as Ctrl pending a play-test rather than changed
      unasked.
- [x] **Not gated on fly mode**, decided 2026-08-30. A pure modifier, live in both modes. M4c made fly a *measurement instrument* —
      `--profile` implies it and refuses the walk toggle mid-run — so hanging a gameplay
      privilege on it ties a game rule to a profiling flag. There is no creative/survival concept
      in the project yet. Make it a modifier now; add a mode gate if and when game modes exist.
- [x] **`radiusMetres` keeps its meaning exactly**, which is the quiet dividend of leaving the
      sphere as the default: it is still "what a swing takes", the tool JSON is unchanged, and
      no format mods have written against moved. A single-voxel default would have made it "what
      the *modifier* swing takes", which is a format change and not a rename.
- [x] **`--mine` is pinned to the sphere path**, stated in code rather than left to fall out of
      "no key is held". M3d's baseline — 1.2 clones
      and 2.4 KiB per swing, swing cost two orders of magnitude inside a frame — measures the
      *worst* case, and a profile that quietly switched to single-voxel would report a cheaper
      number for the same label and look like an improvement.
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
      UV2.y shade channel — which M5a moved to `COLOR`, see M0 above.
- [ ] Ambient fill is low — faces angled away from the sun go nearly black. Cosmetic
      only, and M5 lighting replaces this shading path entirely, so not worth tuning
      the Environment now. **Owned by M5c, and settled 2026-08-30: the directional light
      goes and the grid is the light** — which also retires the terracing slivers below,
      since those are the sun hitting integer heightmap risers.
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
      (constant 1.0 until M5, but the channel exists now). **Superseded at M5a**: the shade
      needs two independently interpolated channels, so it moved to the vertex `COLOR` array
      and UV2.y is now spare. The M0 reservation still did its job — nothing downstream had
      assumed the channel, and moving it was one line in `ChunkMeshConverter` and one in the
      shader, exactly as predicted.
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
      `PlayerController.ReachMetres`** — still 5 m and still deliberately not in the tool JSON.
      **Settled by play 2026-08-30: 5 m stays**, with survival mode as the recorded trigger to
      shorten it to 3 m or 2 m.
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

### M4 — Player ✅ **done 2026-08-30**

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
- [x] **Built at M4d: `MoveResult.BlockedByUnloaded`**, the signal telling "stopped by rock"
      from "stopped by an unloaded chunk" — exactly *steps spent held*, the number that picks
      terminal velocity. **Deferring it at M4b was right:** an unread flag with no profiler
      consuming it is a number waiting to be believed, and built alongside its consumer it was
      correct first time, while M4d's ground-flicker counter — written with no consumer in mind
      — was wrong twice.
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
      **Settled at 5 m by play 2026-08-30**, so the plan's 3.5 m estimate was never shipped.
      At the time of the move it was deliberately left alone: the plan's table said 3.5 m and the
      M3 play-test said 5 m read long, both were feel, and retuning in the same pass as the move
      would have made it impossible to tell which change did what. Play decided it a day later.
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

#### M4d — Feel and measurement ✅ **done 2026-08-30**

- [x] **Answered: no, the player cannot outrun the streamer — not at any shipped setting.**
      `held 0` at terminal 40 m/s at r4h/2v, r6h/1v, r6h/2v, r6h/3v and r12h/3v, and at r6h/3v
      it survives **20, 40, 60 and 90 m/s** with zero held steps. **Terminal velocity stays
      40 m/s**, now with at least 2.25× of measured margin rather than an estimate.
- [x] **And where it *does* break, because "never happened" is not a measurement.** r12h/3v at
      90 m/s holds for 3 steps (0.42%, longest run 17 ms); r16h/3v at 120 m/s for 12 (1.67%,
      longest 83 ms). The arithmetic agrees: one chunk row at r6 is 13×13 = 169 chunks crossed
      every 0.2 s at 40 m/s, and at r16 it is 33×33 = 1,089 crossed every 0.067 s at 120 m/s.
- [x] `--fall`, `--fallheight` and `--terminal` added. **`--terminal` because sweeping the
      constant the profile exists to pick should not need a rebuild** — same argument as
      `--radius`.
- [x] **`MoveResult.BlockedByUnloaded` built**, the signal M4b deliberately deferred. A subset
      of `Blocked` by construction, so it cannot contradict it. 7 tests, five break-recipes,
      four caught.
- [x] **A falling step costs 16 voxel reads, not M4a's predicted 252** — and the reason is
      `ResolveAxis`'s zero-delta early-out, which M4a added so a resting body would not creep by
      a rounding error. A pure fall has exactly zero X and Z displacement, so two axes return
      before querying and only the Y layer is scanned: 2×2 voxels × 4 substeps. **A decision made
      for numerical stability turned out to be a performance one**, so do not "simplify" it away.
      Exact across the sweep: 8.0 reads at 20 m/s, 16.0 at 40, 20.0 at 60, 31.7 at 90.
- [x] **Collider cost is three orders of magnitude inside a frame**: `step ms` mean 0.013–0.015,
      max 0.57, against 16.7 ms. **Do not build** the query memoisation M4-PLAN named.
- [x] **Time it directly; do not read `frame ms`.** Fourth confirmation: `frame ms` sat at mean
      6.90 / max 7.48 across every run while directly-timed `update ms` max ranged 1.1–9.3 ms.
      The two numbers are unrelated.
- [x] **The flicker detector produced two artefacts before it was worth anything**, and both
      reported a small non-zero number that would have read as a real defect:
      - **It counted landings.** The landing step *is* the first at-rest step and the one before
        it was a fall. Fixed by making the test asymmetric — *was at rest, and is now not
        grounded*, which a landing cannot satisfy.
      - **It counted held steps.** A body on an unloaded chunk is grounded with velocity zeroed,
        so it looked like rest; the step where the chunk arrived and proved to be air was then a
        textbook flicker. The r16/120 run reported 5 over 28 "resting" steps, every one of them
        the held-then-released pattern the *next column* was already counting.
      Both found by reading a non-zero number and refusing to believe it. `RestingSteps` is now
      printed beside the flicker count, and the report says outright when the detector never ran.
- [ ] **Recorded limitation: the fall profile measures generation, not meshing.** A falling
      player falls through *air*, so every run reports `0 drawn`. The 250 m configuration lands
      three times and is the closest available to a player falling down a shaft they dug, where
      terrain would be in the box on all sides. **Revisit if meshing under vertical motion ever
      matters** — it would need a dug shaft, not a drop from altitude.
- [x] **Dropped jobs are the M2a scheduler working**: 31,421 at r12 and 47,478 at r16, cancelling
      stale work as the viewer outruns it. First time anything has moved fast enough to exercise
      the argument M2's reference reading bought against a FIFO.
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
- [x] **Played 2026-08-30: 5 m of reach stays.** "Generous but I'll allow" — and generous is
      the right direction to err in a creative sandbox, where the cost of over-reach is a
      slightly floaty crosshair and the cost of under-reach is not being able to touch what you
      are looking at. **This closes the last of M4d's feel questions.**
- [ ] **Revisit trigger: a survival mode wants 3 m or 2 m** {D}. Not a tuning note — the number
      is right for *this* mode and wrong for a mode that does not exist yet, so it changes when
      the mode does, not before.
- [ ] **Two things now wait on a game-mode concept**, and that is the interesting part: reach
      (5 m creative / 2–3 m survival) and whether the precision mining modifier should be gated
      to creative at all. Neither justifies building modes on its own; **two is when the shape of
      the thing starts being knowable**, and a third would make it overdue. Recorded so the count
      is visible rather than rediscovered.

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

Planned 2026-08-30 and split into four parts in [docs/M5-PLAN.md](docs/M5-PLAN.md). **Read it
before starting.** The structural point: light is the **first thing in this engine that is not a
function of one chunk**. Worldgen is pure per chunk, meshing takes a padded copy, a carve is
local — light propagates arbitrarily far, and removing one voxel can change light in a chunk
that is not a neighbour.

**The constant that makes it tractable, and it is exact rather than lucky:** 4-bit light reaches
15 m; the load margin is 2 chunks, 16 m. A source outside the loaded region contributes
`max(0, 15 − 16) = 0` at the visible box, so light computed over the loaded region is *exact*
inside the drawn one. **The margin now has a third load-bearing reason** (mesher ring, feature
stage, light reach) and it is the tightest of the three. If light ever goes to 5 bits — 31 m —
the margin must go to 4 chunks.

#### M5a — Vertex AO and the merge key — **done 2026-08-30**

- [x] Per-corner AO from the padded chunk's diagonals: `(side1 && side2) ? 0 : 3 - (side1 +
      side2 + corner)`. The `side1 && side2` case is the difference between a corner that reads
      as a corner and one that reads as a smudge. `MinerCraft.Core/Meshing/AmbientOcclusion.cs`.
- [x] **`PaddedChunk.CopyFrom`'s 26 neighbours were gathered for exactly this** and nothing
      about the gather changed. Every diagonal sample lands inside −1..32 by construction: the
      axis coordinate is the neighbour's, and the two in-plane coordinates move ±1 from 0..31.
- [x] **Two light channels, not one, and DESIGN forces it.** `CornerShade` carries
      `shadeSky = skylight × AO` and `shadeBlock = blocklight × AO`, 8 bits each, four corners
      packed into one `ulong`.
- [x] **AO multiplies into both**, so a diurnal cycle stays a uniform on `shadeSky` and costs no
      repropagation.
- [x] **Verified where the two bytes live, against Godot 4.7.1 rather than assumed — and the
      answer is `COLOR` for a stronger reason than the plan had.** Two probes: (1)
      `AddSurfaceFromArrays` with no flags produces a surface **without**
      `ARRAY_FLAG_COMPRESS_ATTRIBUTES`, so UV2 stays float32 and the packed value round-trips
      exactly; colours round-trip exactly at 8 bits too. (2) A render probe shows **no sRGB
      conversion is applied to `COLOR` under a `ShaderMaterial`** — the two paths differ by one
      output byte, which is 8-bit quantisation, where a decode would be ~0.23 apart.
      **So precision does not decide it, and packing is wrong anyway: the hardware interpolates
      the packed number.** The fractional part of an interpolated sky channel lands in the block
      channel's byte, so two corners one sky level apart sweep block light through its whole
      range in between. Per-vertex shade exists *to be interpolated*; a packed pair of
      independently varying channels cannot be. `R = shadeSky`, `G = shadeBlock`, two spare.
- [x] **Merge key `(Material, CornerShade)`**, and a sharper statement of why merging is exact
      than the plan had: adjacent faces **share** two corners, and an independently derived shade
      at a shared corner is the same number by definition — so key equality does not merely
      permit a constant shade along a merged run, it **implies** one. Pinned by
      `AMergedQuadHasConstantShadeAlongEveryAxisItMergedOn`.
- [x] **The diagonal flip**, implemented on the **sum of both shade channels** rather than on AO
      alone. Identical at M5a (block light is zero, sky is AO-monotone) and correct afterwards: a
      flat floor with a torch has constant AO and a shade gradient, and an AO-only test would
      never fire on the surface most likely to show the crease. Still a pure function of the
      merge key, so every face merged into a quad agrees which diagonal it takes.
- [x] Only `IsOpaque` materials occlude, so water is still an M8 question.
- [x] **`--ao=0` A/B taken, from a standing start so the chunk set is deterministic.**
      r4h/2v, 165 drawn: **262 → 492 quads per chunk, 1.88×**. r12h/3v @ 6 m/s, 1543 drawn:
      253 → 472, **1.87×**, static memory 94.8 → 150.4 MiB and `update ms` p99 1.39 → 2.05.
      Streaming unchanged: 1521 viewed / 165 drawn / 1926 jobs, M2d's figures to the digit.
- [x] **The 2–4× estimate ran pessimistic, as every estimate here has.** Measured 1.88×. Mesh
      memory grew 1.59× rather than 1.88× because a chunk's memory is not all mesh; solving the
      two r12 runs for a constant baseline gives **~42 KiB of mesh per drawn chunk with AO off**,
      independently reproducing M2d's ~46 KiB, and ~79 KiB with it on. Extrapolated to r12's
      2,245-chunk case that is ~205 MiB against the estimated 200–400 MiB.
- [x] **The AO-alone baseline for M5's fragment-shader trigger is 492 quads per drawn chunk**
      at r4h/2v. The trigger is light in the merge key costing more than 2× the quads of AO
      alone, so M5c must stay under ~984.
- [x] Tests: merge exactness against an independently written AO oracle, the constant-shade
      property, a solid chunk still emitting 6 quads, the inside corner reading 0, the complete
      occlusion truth table, and both triangles facing outwards in either flip state. 468 → 496.
- [x] **Break-recipes run, and two were not what the plan expected.** Swap corner sign table 8
      failed, drop `corner` 4, drop the `side1 && side2` case 5, sample the diagonal on one axis
      6, remove `CornerShade` from the key 8, force the flip off 6, wind the flipped triangles
      backwards 2. But: **swapping `side1`/`side2` is a no-op, not a hole** — the rule is
      symmetric in its two sides, since both `&&` and `+` commute. The meaningful version is
      mislabelling *which corner* gets which sample. And the plan's "the flip must not disturb
      `EveryQuadIsWoundConsistentlyWithItsNormal`" is structurally unfalsifiable: that test reads
      vertex positions and the flip changes indices only, so
      `BothTrianglesOfEveryQuadFaceOutwardsInEitherFlipState` was added to walk the indices.
- [x] **`ChunkPipelineTests.AnEditIsWrittenAndTheChunkIsRemeshed` had a comment that was never
      true.** "Somewhere buried" meant "not on the chunk boundary", and the first solid voxel in
      scan order has air on three sides — carving it is a change of exactly zero quads. It passed
      until M5a shifted the surrounding merges and the two totals came out equal.
      `FindSolidInterior` now checks all six neighbours.
- [ ] **Needs a human eye: is the AO ramp right?** `AmbientOcclusion.LevelShade` is a linear
      ramp bottoming at 0.4 — `[102, 153, 204, 255]` — chosen for being obvious rather than
      measured, and labelled a feel constant. The only part that is not taste: the four values
      must stay distinct, because they are the merge key. `screenshot.gd` makes the A/B
      repeatable; M5d judges it.

#### M5b — The light grid, propagation, and the un-light pass — **done 2026-08-30**

- [x] `ChunkLight`: 8³ = 512 cells per chunk at 1 m, 4-bit sky + 4-bit block, **512 bytes** of
      propagated values. **The derived side is 1.5 KB, not the planned 1 KB**, and the extra is
      not optional: the sky-source bit and the emission nibble are what let the un-light walk
      zero a cell without erasing the lamp it is walking away from. Still nothing against ~46 KiB
      of mesh.
- [x] **`[z][x][y]`, Y contiguous** — `LightCellPos.Index`, the same shape as `VoxelPos.Index`.
- [x] `VoxelsPerCell` a **power of two dividing 32**, checked in a static constructor *and*
      asserted by a test, because a constant that is currently right still needs the rule written
      where it can be read.
- [x] **Conductance is a property of the cell *face*.** Light crosses iff some voxel pair across
      the shared 4×4 boundary is non-opaque on both sides. Both wrong rules have a test each and
      both were run as break-recipes: "any opaque → cell opaque" fails 13 tests, "any air → face
      open" fails 3.
- [x] **Recorded cost stands**: the value is still per cell, so rock within a metre of a lit
      tunnel glows slightly. The lever if M5d says it reads badly is a 0.5 m cell, which
      `LightCellPos` already permits.
- [x] **Skylight seeded analytically** from `ISurfaceHeights.SurfaceHeightAt`, which
      `WorldGenerator` now implements. The interface exists so a test can state a flat world in
      one line rather than reverse-engineer a seed that produces one.
- [x] `SkyColumn` per chunk column, 32×32 `short`, seeded from the heightmap and lowered by
      edits. **Its downward scan is bounded by the loaded region rather than by its own limit**,
      because an unloaded voxel reads as opaque — the collider's rule, for the collider's reason.
      `MaxScanVoxels` is a backstop for a fixture that answers "air" forever and says so.
- [x] **A cell is a sky source only if *every* one of its sixteen columns is open, not any.**
      Under-seeding is recoverable — the cell above is a source, the face between them conducts
      through the open half, and full skylight falls through it for nothing — so a cliff face is
      lit by propagation anyway. Over-seeding is not: the value is per cell, so a mostly-solid
      cell declared a source pushes daylight into the rock through its own faces. Breaking it to
      "any" fails 3 tests.
- [x] **The lifetime half stays in M5c**, as split. What is here is the rules, which are pure.
- [x] **The oracle reached the sky-height path**, and the fuzz asserts it did: `breakthroughs > 4`
      printed beside the result, because a sequence that stayed underground would pass for the
      wrong reason.
- [x] Skylight rule: **full value straight down with no attenuation**, −1 per metre otherwise.
      **The free fall is restricted to full brightness**, and that restriction needed a test
      shape nothing else in the suite had — see the break-recipe note below.
- [x] `MaterialDefinition.LightEmission` (0..15), projected into `MaterialTable`, **refused
      rather than clamped at load** exactly as an out-of-range tool radius is, and clamped again
      in the table because the loader is not the only way a definition arrives.
      `mods/minercraft/materials/lamp.json` ships at 15.
- [x] **Debug bind: L places a lamp** in the empty voxel in front of the crosshair, on a key-down
      edge. One key, one material, no inventory, no cooldown. `Hit.Previous` is what M3b built
      and labelled "where M6's placement goes"; this is its first caller. Resolved by identifier
      and disabled if the base game does not ship a lamp — *not* falling back to air, which would
      carve a hole out of whatever the player was pointing at.
- [x] `ShippedContentTests.SomethingShippedActuallyEmitsLight` — something emits, in 1..15,
      nothing emissive is air, and something reaches the top of the range so the 0..15 decision
      is exercised by the content rather than only by the format.
- [x] **The un-light pass**, two queues, with the re-seed step. Dropping the re-seed fails 7
      tests including the oracle, which is exactly what M5-PLAN said to check.
- [x] **A better oracle than the one planned, and by a category.** "Incremental equals
      from-scratch" runs the *same add pass* as the subject, so it can only independently catch
      the un-light pass. The propagation invariant
      `V = max(S, max over conducting neighbours (V(n) − attenuation))` has a **unique** fixpoint,
      so checking it cell by cell is complete correctness with no propagation in the check at all
      — same category as M4a's sealed room. Both are asserted, plus a third that fell out free:
      incrementally maintained sky heights must equal a fresh downward scan of the voxels.
- [x] Tests: analytic falloff as an equality; a 0.25 m wall blocks and a 0.75 m bore conducts;
      an open column is full sky to the surface and dark below; a shaft stays lit however deep;
      cross-chunk propagation with no seam; determinism, bitwise. 496 → 518.
- [x] **Break-recipes: twelve run, ten bit, and the two that did not were different problems.**
      - **The six-neighbour expansion in `Invalidate` was provably dead work.** It existed for
        the closing-face case, and removing it fails nothing — because conductance is recorded on
        *both* cells of a face, so a face that changes always reports both in `BuildDerived`'s
        changed set. Deleted, the caller's contract written into the doc comment, and the
        property pinned by `SealingAFaceReportsBothOfItsCells`. Breaking the reporting instead
        fails 11 tests, which is what makes the one-cell version safe rather than lucky.
      - **"Any skylight value falls unattenuated" was a genuine hole.** A shaft straight down from
        the surface carries *full* skylight, which falls free under both rules, so it cannot tell
        them apart. The shape that can is a horizontal branch off the shaft with a **second shaft
        dropping from its far end**, fed at 10 rather than 15. Built, and the recipe now fails.
      - **One recipe hangs rather than failing:** dropping the "only if greater" guard in the add
        pass, exactly as predicted. The battery treats a timeout as its own outcome so it does
        not read as infrastructure trouble.
- [x] **The test fixture was half the problem and its shape is M5c's.** The first run took 4.5
      minutes and crashed the test host; it is 6 s now. Three causes, and the third is a design
      finding rather than a test one: **the rebuild scope is two different shapes.** A voxel's
      *conductance* reaches only its own chunk plus a neighbour across whichever faces it
      actually touches — an interior voxel, 30 in 32, rebuilds exactly one chunk. A change to the
      column's *sky height* is not local at all: it moves the sky flag the whole way up and down
      that chunk column and nowhere else. **M5c should implement exactly those two scopes.**

#### M5c — The pipeline: the stage, the sample, and the relight

**Split into M5c-1 (the stage and the relight) and M5c-2 (the sample and the shader), on the
same grounds M2c and M2d were split: everything in the first half is verifiable headlessly and
nothing in the second half is. M5c-1 done 2026-08-30, suite 518 → 542.**

##### M5c-1 — the stage, the lifetime, and the relight — **done 2026-08-30**

- [x] `GenerationStage.Lit` as a third rung; `CanMesh` requires the chunk and all 26 neighbours
      to be there. Nothing about `ChunkState` changed — no new job kind, no new transition.
- [x] `ChunkStreamer` implements `ILightChunks` and owns both lifetimes: a `ChunkLight` per chunk,
      and a `SkyColumn` per (x, z) **refcounted separately**, so a vertical unload does not take
      one away while chunks above still share it.
- [x] **Light needs no third margin ring, and that was the M5c-1 finding.** Following the existing
      ladder — mesh at R needs R+1 lit, lit at R+1 needs R+2 at `Features`, `Features` at R+2
      needs R+3 — the margin would have had to grow from 2 to 3, which is +17% chunks on top of
      M2c's +41%. **Deriving a chunk's light asks for voxels, not for a stage**, so the outermost
      loaded ring derives its cells and joins the propagation while never reaching `Lit` and never
      meshing. That is exactly the arrangement the margin's third load-bearing reason describes.
- [x] **Revisit trigger, and it is real: when `ApplyFeatures` starts placing anything, it must
      route through the relight path the way `SetVoxel` does.** Deriving at `Terrain` is sound
      today only because the feature stage writes nothing. A feature that writes into a neighbour
      is an edit by another name, and the machinery to repair it already exists.
- [x] **A push into a chunk with no light grid is dropped, and nothing remembered having tried.**
      The BFS pushes rather than pulls, which is correct while that chunk will never exist and a
      lost update the moment it arrives a few frames later. `LightPropagator.SpreadInto` re-spreads
      the six adjacent face layers whenever a grid comes into existence. **M5b could not have found
      this**: every fixture there builds all its grids before lighting any of them, which the
      streamer cannot do.
- [x] **Six face layers, not the whole 488-cell shell.** Light crosses faces only, so the twelve
      edges and eight corners cannot conduct into the chunk at all — a statement about the model
      rather than a saving.
- [x] **The first deterministic test for that bug passed on broken code**, because it dropped the
      chunk under a sky shaft where every cell is its own full sky source and relights itself. A
      lamp in the *neighbouring* chunk is the shape that separates the arms. Fourth time this
      project's "watch it fail" rule has earned itself.
- [x] **Two flags, as settled: `NeedsRemesh` is stale, `RemeshUrgent` is the player is waiting.**
      A relight sets only the first, and only on a chunk that has actually drawn something.
- [x] `IsQuiescent` moved onto the pipeline. A caller testing only jobs and the work list saw
      quiescence while every chunk sat one stage short of meshing — which is what a stalled light
      stage looks like from outside.
- [x] **Break-recipe battery: 12 recipes, 9 caught, 3 holes — and no two holes were the same
      kind.** "Advancing to `Lit` does not push its 26 neighbours" fails **14** tests, which is
      M2c-2's rule being load-bearing somewhere new.
- [x] **Hole 1 — nothing asserted the two-flag split, only the sign of the counters.** Putting
      `bool urgent = chunk.NeedsRemesh` back into `SubmitMesh` left the suite green, because the
      relight remeshes merely got counted as edit remeshes too and both stayed non-zero.
      `OnlyTheChunkHoldingTheLampIsUrgent` asserts the *number*.
- [x] **Hole 2 — a scenario that made both arms agree.** On a still world the derive mask never
      moves, so gain-only and inequality are the identity. Two overlapping viewers separate them:
      removing the second is all losses and no gains. `LosingANeighbourReDerivesNothing`.
- [x] **Hole 3 — the shaft test dug a one-voxel bore, and a one-voxel bore is never a sky
      source.** A cell is open to the sky only when **all sixteen** of its voxel columns are, so
      the bore's light arrived by free fall and conductance alone could take it away — the
      sky-height rebuild never mattered to it. A **cell-wide** shaft makes every cell down it a
      source in its own right, so capping the top must clear flags in chunks nobody edited.
      `CappingACellWideShaftPutsTheDarkBackInChunksTheCapIsNotIn`, which asserts the deep cell is
      in a different chunk from the cap so it cannot silently stop covering that.
- [x] **M5-PLAN warned about this case and the warning was not enough.** It asked the fuzz to
      include edits breaking through a column's topmost opaque voxel — and that path *did* run,
      the column *did* move, and the test still passed for the wrong reason. **A warning about
      which code path runs is not a warning about which property is checked.**
- [x] **Light propagates on the sim thread under a time budget**, one global cell queue writing
      into the streamer's grids. Cross-chunk propagation then needs no special case, because the
      algorithm has no per-chunk boundary. Same precedent and same recorded reason as
      `ApplyFeatures`. Time-sliced rather than counted, following the mesh-*apply* budget rather
      than the mesh-free one: this is our own CPU work start to finish, so it can be measured as
      it is spent. Resuming is free because the queues are fields.
- [x] **The plan costed the wrong half, and by an order of magnitude.** M5-PLAN estimated ~779k
      cell visits and ~15 ms for the fill and said nothing about deriving. **Measured in Release,
      per chunk:** surface 0.245 ms, deep 0.327 ms, open sky 0.204 ms — call it 0.27 ms, so
      **~0.41 s for the whole 1,521-chunk fill** against generation's 0.47 ms a chunk on eight
      workers.
- [x] **The budget is 4 ms, and a standing-start sweep is what says so.** Light delays the
      *first draw*, because nothing meshes until its neighbourhood is lit. At the 5 s report:
      1 ms → 857/1521 derived, 0 drawn; 2 ms → 1467/1521, 0 drawn; **4 ms → settled, 165 drawn**;
      8 ms identical. **2 ms was a real regression and invisible in the tests**, which pass their
      own generous budget. It is a ceiling and not a reservation — once lit, nothing is queued
      and the step costs nothing. `--lightbudget=` makes the sweep a shell loop, the fourth seam
      of that shape after `--cavestep`, `--applybudget` and `--ao`.
- [x] **The headless contract is unchanged to the digit**: 1521 viewed (1521 tracked), 165 drawn,
      1926 jobs, 492 quads per drawn chunk — M2d's and M5a's figures with light in the barrier,
      which is what keeps M5d comparable with everything before it.
- [x] **Real-world light numbers**, which the test world got wrong in both directions:
      `1521 derives / 1521 tracked (299 uniform, 20%), 585,012 cells visited, 169 sky columns,
      0 relight remeshes`. Exactly one derive per chunk; the closed form covers **20%**, not the
      test world's 12% nor M2b's 68%; **585k visits against the plan's estimated 779k — the
      plan's propagation arithmetic was good, and it was not the half that mattered**; and zero
      relight remeshes during the fill, which is the priority ordering staying intact.
- [x] **`PaddedChunk.CopyFrom` is 60–75% of that, not the derivation** — it recomputes the
      neighbour index per voxel where 27 block copies would do. **Named as the lever and
      deliberately not built**, per the rule; it would speed up meshing too, since a mesh job
      builds one per chunk as well. M5d decides.
- [x] **A uniform neighbourhood has a closed form and skips the copy entirely** — every face
      conducts iff the material is not opaque, every cell emits what the material emits. Pinned
      against the general path cell for cell and face for face, the `IsCaveAt`/`GenerateTerrain`
      shape of test, because two derivations of one field is two derivations that can drift.
      **Coverage measured at only 12% in the test world**, which is all surface; M2b's 68% uniform
      figure is for chunks alone and this needs the six face neighbours uniform *and equal*.
      **A real-world coverage number is an M5d measurement.**
- [x] **The suite went 8 s → 83 s and back to 16 s.** Deriving is real work and `Settle` sleeps a
      millisecond a pass, so a production-sized light slice turned every pipeline test into a few
      hundred extra round trips. `PipelineHarness.GenerousLightBudget` is the fix; a test that
      wants a starved budget passes its own.
- [ ] Rejected: a light job over a 3×3×3 snapshot (half-width 12 m against a 15 m reach — a
      source 14 m out is silently missed, and the seam appears only near light sources); 5×5×5
      (125 references per job); per-chunk iterative boundary exchange (data-dependent stopping
      condition); a worker with a lock held for the whole job.
- [ ] **A third `GenerationStage`, not a new `ChunkState`.** Light is a barrier, not a job, so
      `CanMesh` gains one clause and nothing about the job pipeline changes.
- [x] **What sets it — settled 2026-08-30: derived data built *and* the propagator drained.** A
      per-chunk stage whose condition carries one global term, because **a global BFS has no
      per-chunk completion event**. Two things have to hold and only one is per chunk: every
      chunk within light's ~2-chunk reach has derived data (the margin's third load-bearing
      reason, cashed in), and the propagation has actually run (global, because the queue is).
- [x] **Rejected: a per-chunk count of queued cells.** It is the obvious way to make the temporal
      half local and it is wrong in the direction testing does not show — a chunk with no queued
      cells of its own can have a neighbour's frontier one step from spilling in, so the count
      reads settled while the light is still moving.
- [x] **The global term is not a stall at any rate this game reaches**, estimated: ~779k cell
      visits for the initial fill at r4h/2v, ~15 ms, ~15 frames under a 1 ms budget — against the
      ~0.22 s the spawn gate already costs. A new chunk column in flight is a few chunks; a swing
      is a handful of cells. **All estimates; M5d measures the first.**
- [x] **Being wrong is recoverable by the path that has to exist anyway.** Light changing in an
      already-meshed chunk — a chunk loads two away, or the player breaks through — is caught by
      the drain's changed-cell set marking a remesh. Streaming and mining share one mechanism, and
      that is what lets the global term be as coarse as it is.
- [ ] **Advancing one chunk must push its 26 neighbours** — M2c-2's rule, and light arriving may
      be the last thing a neighbourhood was waiting on. Miss it and the pipeline stalls silently
      with everything idle.
- [ ] Padded light grid **10³ = 1,000 bytes, covering −1…8**: a vertex at the far corner sits at
      cell coordinate 8.0 and the interior centres run 0.5…7.5, so it needs cell 8 from the
      neighbour.
- [x] **Built on the sim thread at submission, not on the worker — settled 2026-08-30, and this
      corrects the plan.** A `ChunkLight` reference is not safe to hand a worker. Holding 27
      `VoxelChunk` references is safe because **voxels are copy-on-write since M3a**; light is
      not, and the propagator writes into these 512-byte arrays in place while a mesh job runs.
      Copy-on-write for light means thousands of clones per drain against **one 1,000-byte copy
      per mesh job** — 0.6% of the 157 KB padded voxel copy the worker still builds itself.
- [x] **Recorded because it reads as an inconsistency with `PaddedChunk` and invites being
      "fixed".** The two are built in different places for reasons that do not transfer either
      way.
- [ ] Trilinear sample per vertex, multiplied into AO to form the corner shade byte. Light and AO
      differing but their product matching merges anyway — harmless and slightly beneficial.
- [ ] **Re-light on edit marks every chunk whose padded light grid holds a changed cell** — the
      owner plus, on a boundary, its neighbour. Marking only the owner is the break-recipe and
      the symptom is a light seam on the chunk grid. M3a's `Neighbourhood()` marks all 27 for the
      analogous reason.
- [ ] **Most swings change no light at all** — a 0.4 m sphere inside a 1 m cell does not open a
      cell face. Mining is not a relighting storm; breaking *through* is. A light placed in open
      air is the 125-chunk case and is a one-off.
- [ ] **Try the cheap mitigation first:** a light-only remesh does not need band3 urgency. Light
      100 ms stale is invisible.
- [x] **It is two flags, not one line — settled 2026-08-30, and this corrects the plan.**
      `NeedsRemesh` carries both *the mesh is stale* and *the player is waiting on it*, and
      `SubmitMesh` reads the one flag for both — right while an edit is the only thing that can
      invalidate a mesh, and a relight is the second thing. `NeedsRemesh` keeps the first meaning,
      a new `RemeshUrgent` carries the second; an edit sets both, a relight sets only the first.
- [x] **A relight only marks a chunk that has actually drawn something.** A chunk that has never
      meshed needs no marking — the barrier already holds it until its light exists, and marking
      it would make its *first* mesh urgent. During the initial fill every chunk's light changes,
      so the whole distance-ordered queue would arrive at band3 and M2a's ordering would be
      silently gone. Nothing looks broken; the world just loads in the wrong order.
- [ ] **Revisit trigger for moving light out of the vertex entirely** (per-chunk 3D texture,
      hardware trilinear, *no remesh on a light change*): light in the merge key costs more than
      **2×** the quads of AO alone, **or** breaking into a lit space costs more than ~16 chunk
      remeshes at a rate mining can sustain. Not built first because it contradicts a DESIGN line,
      needs a material per drawn chunk or a clipmap volume, and every named fallback in this
      project so far has proved unnecessary when measured.
- [ ] **The directional light goes — settled 2026-08-30. The grid is the light.**
      `Voxel.gdshader` renders `unshaded`:
      `ALBEDO = tex * faceTint(NORMAL) * (shadeSky*skyTint*skyBrightness + shadeBlock*blockTint)`.
- [ ] **The face tint is computed in the shader from `NORMAL`, not baked into the vertex** — top
      brightest, sides middling, bottom darkest, the cheap fake every voxel game uses. Keeping it
      in the shader keeps a *look* constant that will certainly be tuned out of the merge key,
      and it costs nothing because a quad has exactly one normal.
- [ ] **`skyBrightness` is a uniform with no day/night cycle behind it yet.** It exists because
      the alternative — scaling the skylight seed and reflooding — relights the world every
      frame.
- [ ] **This retires two long-standing open items**: the low ambient fill, and the bright and
      dark slivers following terrain contours. Both are the sun hitting the integer heightmap's
      risers; with no sun the terracing goes back to being geometry rather than a lighting
      artefact. What stays a look question for M5d is whether the face-tint constants are right,
      not whether the sun goes.
- [ ] Tests: the barrier holds a chunk until its 26 neighbours are lit; **exact remesh scope,
      asserted in both directions** (over-marking is a performance bug that hides correctness
      bugs); unload releases the grid and revival recomputes it; an edit landing mid-mesh
      re-marks; the no-chunk-left-waiting fuzz extended over the new stage.

##### M5c-2 — the sample, the shader, and the sun — **done 2026-08-30**

- [x] `PaddedLight` gathered on the sim thread at submission; trilinear sample per vertex;
      occlusion multiplied into both channels; `Voxel.gdshader` `unshaded` with a
      `NORMAL`-derived face tint and a `sky_brightness` uniform; `DirectionalLight3D` removed
      from `Main.tscn`. Suite 542 → 556. **Lighting is end to end — a lamp glows.**
- [x] **The headline number is the opposite of what the plan expected.** Quads per drawn chunk at
      r4h/2v: **262** (AO off, light off) / **492** (AO on, light off — the M5a baseline) /
      **283** (AO off, light on) / **404** (AO on, light on). **Light in the merge key does not
      cost quads, it saves 18% of them**, and mesh memory follows quads.
- [x] **The mechanism: the shade is `occlusion × light`, so where light is zero the product is
      zero whatever the occlusion was.** Faces that could not merge under AO alone merge freely
      in the dark, which is most of a mine's surface area. The merge stays exact — the shades
      really are equal. Pinned by `InDarknessEveryOcclusionLevelCollapsesToTheSameShade`, because
      the quad measurement now depends on it and an ambient floor added to the shade *byte*
      rather than to the shader would silently undo it.
- [x] **So the fragment-shader fallback is retired, not deferred.** Its trigger was ">2× the
      quads of AO alone", i.e. ~984; measured 404, which is **0.82×**. Fifth named fallback in
      this project dissolved by measurement, and the first dissolved in the wrong direction.
- [x] **The plan's 10³ padded grid is one ring short: it is 11³ over −1…9.** The derivation is
      right for a sample taken *at* the vertex, and the sample is taken **half a cell along the
      face normal**, which pushes the far face to 8.5. **The offset is not a refinement** — a
      face sits on the boundary between solid and open, so without it a flat fully lit floor
      renders at 7.5 of 15 everywhere and looks like an art choice. `ChunkLight` recorded the
      offset at M5b and nothing fed it back into the grid size.
- [x] Cell 9's interpolation weight is **exactly zero** — the face-axis sample is `plane / 4 ± 0.5`
      with `plane` an integer, so at `plane == 32` the weight is 8.0 − 8 = 0 in binary. Clamping
      the index instead would be right today and silently wrong the first time the cell size or
      the offset changes, for 331 bytes.
- [x] **The sun going retired two long-standing open items**: the bright and dark slivers
      following terrain contours (the sun on the integer heightmap's risers), and the low ambient
      fill. Face tint is top 1.0 / ±Z 0.8 / ±X 0.65 / bottom 0.5, **in the shader and not the
      vertex** — a quad has one normal so it is free, and baking it would put a look constant
      into the merge key where retuning it means remeshing the world.
- [x] **Three screenshots, because one would have proved less.** Daylight (skylight, AO, tint, no
      slivers); `--skybrightness=0` (terrain **pure black** — sky is what lights the world, block
      light is genuinely zero, and darkness has no accidental ambient); and
      `--skybrightness=0 --debuglamp` (a warm pool over ~15 m and nothing else lit). **Only the
      third can prove `COLOR.g` carries block light and is tinted separately** — every other test
      in the milestone would pass identically if both channels went into one byte.
- [x] `--skybrightness=` and `--debuglamp` are the new seams, so the look questions are
      repeatable rather than a recollection. No ambient floor: an unlit cave is black, which is
      DESIGN's intent; the lever if M5d disagrees is one constant in the shader.
- [ ] **For M5d: the fog ignores the light grid.** At `--skybrightness=0` the horizon is
      fog-coloured while the ground is black. Fine against a lit world, wrong the first time
      somebody stands in a big unlit cavern.
- [x] **Break-recipe battery: 13 recipes, 9 caught, and both real holes were *joins*.** Nothing
      checked that each corner samples its own vertex (per-vertex light versus per-face light —
      smooth gradient versus flat-shaded facets), and nothing checked that the pipeline hands the
      mesher any light at all: replacing `GatherLight(...)` with `null` at the submission site
      left 556 tests green. **Every part of the path was tested and the wire between them was
      not**, which is the same shape M5c-1 found one layer in. Closed by
      `EachCornerSamplesItsOwnVertexRatherThanOneSharedPoint` and
      `ALampPutsBlockLightIntoADeliveredMeshsVertices`. Suite 556 → 558.
- [x] **Standing habit from it: put a recipe on every *call site* that passes data between
      layers, not only on the logic at each end.**
- [x] **Two uncaught recipes that are not holes.** Shrinking the grid to the plan's 10³ leaves
      everything green, because `Sample` skips zero-weight terms and never forms the out-of-range
      index — so the ring is belt and braces, M4a's contact-epsilon category, and the reason to
      keep it is that **the ring and the zero-weight `continue` are safe only together**: drop
      the ring and deleting that `continue` as tidying walks off the array. And truncating rather
      than rounding in `Scale` differs by at most 1 in 255, below the resolution of anything here.
- [x] **A test whose "not vacuous" guard was the entire test.**
      `AMergedQuadStillHasConstantShadeAlongEveryAxisItMergedOn` first used per-cell random
      light: every face differed, **nothing merged**, and every assertion passed vacuously. Only
      `Assert.True(merged > 0)` failed. Third fixture trap in M5c, from a new direction.

#### M5d — Feel and measurement

- [ ] **Quads per chunk: AO off / AO on / AO+light on.** Three numbers, one flag each, and they
      are the whole of the fragment-shader decision.
- [ ] Mesh memory, which follows quads and is the M2d finding this milestone most disturbs.
- [ ] Light propagation cost per frame, **timed directly**, with cells visited and the
      denominator printed beside it.
- [ ] Remeshes per swing, mining normally and mining through into a lit space, at five swings a
      second — against M3d's control of 1.04% of one core.
- [ ] Initial fill from a standing start, against the estimated ~0.25 s (779k cell visits at
      r4h/2v, ~20 ns each — both figures guesses).
- [ ] **Do not read `frame ms`.** The headless frame-time trap has been walked into four times
      and the directly-timed number moved by a tenth of a millisecond every time.
- [ ] Played: does a mine read as a mine? Is 1 m too coarse down a `hand_pick` tunnel whose bore
      is narrower than one cell — this is the play that would reopen the settled 1 m? Does rock
      beside a lit tunnel glow? Does AO earn its quads — DESIGN calls it "the single biggest
      visual payoff" and that is a claim, not a measurement? Are the face-tint constants right
      now the sun is gone? Can you mine by torchlight?

#### Decisions, all four settled 2026-08-30 before any code

- [x] **Light cell size: 1 m. DESIGN's number stands.** The `--lightcell` A/B flag is therefore
      **not built** — it existed to answer a question and the question is answered. **Revisit
      trigger:** M5d plays a `hand_pick` tunnel and the light reads blobby across a bore narrower
      than one cell; 0.5 m then costs 4 KB per chunk against 512 bytes and 8× the propagation,
      and `LightCellSize` already permits it. Worth restating why it was a question: DESIGN
      reasons in **metres** — "light is low-frequency, nobody sees the difference, 64× cheaper" —
      and the number that decides whether it *looks* right is in **voxels**. A 1 m cell is 64
      voxels and the tunnels are 0.75–1.25 m across, so the cell is wider than the corridor.
      That is why the trigger is a play rather than a number.
- [x] **`SkyColumn` is M5b**, with its lifetime in M5c. See the split recorded under M5b.
- [x] **The directional light goes.** The grid is the light. See M5c.
- [x] **Emission range 0..15**, matching the field with no scaling. **Revisit trigger, in
      Matthew's words: "unless we come up with better lighting later"** — a wider field or
      coloured light would take the range with it. Recorded because 0..15 means the brightest
      lamp any mod can define reaches exactly 15 m, and it reaches it for a *storage* reason
      rather than a design one: a **game-balance number wearing a storage costume**, the same
      shape as terminal velocity being a streaming parameter in a physics costume.
- [x] **One of the four moved work earlier rather than removing it.** Settling the directional
      light meant the grid is the only light, and DESIGN's M7 wet-surface term needs skylight on
      its own — so the vertex carries two channels rather than one pre-multiplied value, and
      that lands in **M5a**, not M5c. See M5a.

#### Surfaced elsewhere

- [ ] **`JobSchedulerTests.AThrowingJobFaultsWithoutTakingTheWorkerDownWithIt` is intermittent.**
      It failed in 2 of M5b's 12 break-recipe runs and passes on a clean tree. M2a's, not M5's —
      but **a flaky test is worse than a missing one during a mutation battery**, because
      "1 failed" reads as "the recipe was caught" and it took reading the failure names to see
      that no lighting test had failed at all. Worth pinning down before the next battery.

#### Deliberately not in M5

- [ ] Coloured light — three channels, three BFS passes, 12 bits per corner in the merge key.
      Nothing in DESIGN asks for it.
- [ ] Directional or cone lights. A headlamp is a shader effect, not a propagated field, and it
      would be M10's machine content anyway.
- [ ] Attenuation through translucent materials — water is the only candidate and it is M8. The
      conductance derivation is the right place and the predicate is already shaped for it.
- [ ] Placement of light sources through a real UI — M6.
- [ ] Persisting light. It is derived data: 512 bytes recomputed in microseconds against a chunk
      blob of kilobytes. M6 persists *edits* and the light follows. **`SkyColumn` is the
      exception** and is genuinely edit-derived, so it goes on M6's list.
- [ ] Distance LOD interacting with lighting — M13, and the coarse grid is already most of the
      answer: a 1 m light field downsamples where a voxel field does not.
- [ ] Darkness as a hazard — M13. M5 supplies the darkness and nothing that punishes it.
- [ ] Baking AO into the light grid. Different frequencies — AO is a 0.25 m effect and the grid
      is 1 m — and merging them loses exactly the detail AO exists to add.

### M6 — v0.1 playable sandbox

- [ ] SQLite persistence, `(cx, cy, cz) → blob`, palette in the world file
- [ ] **`SkyColumn` persists too, and it is the one lighting structure that must.** Light
      itself is derived data — 512 bytes recomputed in microseconds — but a column's sky
      height is edit-derived and cannot be recovered from the seed once a shaft exists. M5b
      builds it; M6 is where it stops being lost on horizontal unload.
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
