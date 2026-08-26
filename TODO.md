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

Not urgent — it stays private for now — but cheaper to settle before there are forks and
outside contributors than after.

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
- [ ] Decide whether commit authorship stays `matthewcarven@gmail.com`. It becomes a
      permanent public record on every commit, and rewriting history afterwards is
      unpleasant. GitHub's `users.noreply.github.com` address is the usual alternative.
- [ ] Godot's MIT attribution — a credits screen using `Engine.GetLicenseText()` and
      `Engine.GetCopyrightInfo()`. Obligation attaches when the **game** is distributed,
      not when the repo is published, so this belongs with the first export rather than
      here. Recorded so it isn't discovered at release.
- [ ] Sanity-check before the first push: `reference/` stays ignored (51 MB of
      godot_voxel), and no `export_credentials.cfg` has appeared.

## Needs a human eye

- [x] **Face winding — confirmed correct 2026-08-24.** Visually verified from three
      angles: grass top, dirt sides, stone underneath, solid convex silhouette. Godot's
      clockwise front-face convention is as documented, so no flip was needed. The
      screenshots also confirmed texture-array indexing, per-vertex normals, and the
      UV2.y shade channel.
- [ ] Ambient fill is low — faces angled away from the sun go nearly black. Cosmetic
      only, and M5 lighting replaces this shading path entirely, so not worth tuning
      the Environment now.

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
      **Reopened 2026-08-26:** godot_voxel does the opposite — the job holds references to
      the chunk and its 26 neighbours and does the padded copy *on the worker thread*.
      That keeps the sim thread free and skips the copy entirely for jobs cancelled before
      they run, but it requires chunk storage to be safe to read while the sim thread may
      write, which is a real cost and not a free win. Decide during M2 planning; see
      [docs/M2-REFERENCE-NOTES.md](docs/M2-REFERENCE-NOTES.md).

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

### M2 — World ⬅️ **start here**

- [x] **Read `reference/godot_voxel/` threaded mesh scheduling and streaming policy.**
      Done 2026-08-26 — findings in
      [docs/M2-REFERENCE-NOTES.md](docs/M2-REFERENCE-NOTES.md). It earned its keep again:
      it caught that the job queue below was specified as the wrong data structure.
- [ ] Plan M2 as sub-parts first, the way M1 was — see
      [docs/M1-PLAN.md](docs/M1-PLAN.md) for the shape. The reference notes suggest the
      seams: job queue → worldgen → streaming policy → the unload/free path.
- [ ] ~~`System.Threading.Channels` job queue for gen + mesh~~ **Corrected.** Channels is
      FIFO, and a FIFO cannot reprioritise queued work as the player moves, nor drop work
      that became irrelevant before it ran. A priority heap is also wrong, for the same
      reason: it orders at insert time, which is when the ordering is least valuable.
      Build a **flat list re-sorted on a timer** (~200 ms), with the sweep also dropping
      cancelled jobs. Channels stays appropriate for handing *results* back to the main
      thread, which really is FIFO.
      - [ ] Pack job priority into one `uint` of four byte-bands so comparison is a single
            integer compare. Bands we need now: distance, and job class.
      - [ ] Read viewer positions **without a lock** — priority is a heuristic, and a
            stale one still schedules correctly. Pre-size the viewer array and bound it
            with an atomic count rather than resizing it.
      - [ ] Use real `CancellationToken`s from the start. godot_voxel's distance-based
            drop check is something they explicitly regret and want to replace.
      - [ ] One pool, two lanes — a `serial` flag for disk work, not a second thread pool.
            Matters at M6; cheaper to build the flag in now.
      - [ ] Thread count `clamp(round(0.5 × cores), 1, cores - 1)`. Never all cores.
      - [ ] A cancelled job must still report back, or its chunk stays marked "meshing"
            forever and never updates again.
- [ ] **Budget mesh *destruction*, not just construction.** Freeing many `RenderingServer`
      RIDs in one frame stutters, and the cost is deferred inside the renderer, so it
      lands a frame or two *after* the cause and will be misdiagnosed as a meshing
      problem. Needs a "run N per frame until done" path, distinct from a time-sliced one,
      because the cost cannot be measured from our side. See the reference notes.
- [ ] Worldgen: 2D fBm heightmap, strata layers, 3D noise caves
- [ ] Deterministic per-chunk seeding `hash(seed, cx, cy, cz)`
- [ ] Staged generation pipeline so cross-chunk features don't tear
- [ ] Chunk streaming: **box difference, not volume iteration.** Iterate only the shell
      that entered or left view when the player crosses a chunk boundary — O(R²) per
      crossing, not O(R³). Empty-chunk skipping.
      - [ ] **Refcount chunks per viewer**, and make "viewer" a Core concept (a position
            and a radius, not a camera). Build it at M2 with exactly one viewer —
            retrofitting it later is a rewrite of the streaming policy, and it is what
            makes multiplayer not a special case.
      - [ ] Cancel in-flight loads when a chunk's refcount hits zero. Without it, running
            somewhere and turning around queues work for terrain you'll never see.
      - [ ] View distance is **not one scalar** — vertical extent is separate. A mining
            game probably wants a tall narrow volume, the opposite of the usual default.
      - [ ] Dedup queued chunks with a flag *on the chunk*, not a `HashSet` probe. Matters
            at M3 when one carve dirties the same chunk repeatedly in a frame.
- [ ] Measure: `RenderingServer` direct vs `MeshInstance3D` at target chunk count
      - [ ] Also measure whether meshing 2×2×2 chunks as one surface cuts draw calls
            enough to be worth it. Mesh blocks need not equal storage blocks.

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
