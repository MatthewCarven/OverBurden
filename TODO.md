# MinerCraft — TODO

See [docs/DESIGN.md](docs/DESIGN.md) for the reasoning behind all of this.

## Decisions

- [x] **Engine — Godot 4 + C#.** Decided 2026-08-23. Real threads, renderer for free,
      4.7 mono already on disk.
- [x] **Voxel scale — 0.25 m.** Decided 2026-08-23.
- [x] **Chunk size — 32³ (8 m cube).** Decided 2026-08-23.
- [x] **No mobs.** Pressure comes from flooding, collapse and logistics.
- [x] **Mod support — data mods at M1, Lua at M12, no C# DLL loading.**
- [x] **Chunk storage order — Y-major, then Z, then X.** Decided 2026-08-24. X
      contiguous so the mesher sweeps a row with sequential reads; Y-major keeps a
      horizontal slab together for the lighting column pass.
- [ ] Collapse debris: loose re-settling rubble vs rigid falling chunks — decide at M9
- [ ] Power model complexity — decide at M10
- [ ] Name check: "MinerCraft" — keeping it?

## Needs a human eye

- [ ] **Face winding — run `start.cmd` and confirm the block is not inside-out.**
      All six faces are consistently wound (verified analytically: the right-hand-rule
      normal points inward on every face), so it renders either all-correct or
      all-flipped — never a mix. If flipped, it is one line in
      `VoxelMeshBuilder.AddFace`, already commented.

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

### M1 — Storage and registry ⚠️ over-invest here

- [ ] Move the face table (`VoxelFaces`) from Game to Core once the mesher emits plain
      float arrays instead of Godot types — it only lives in Game because of `Vector3`

- [ ] **Namespaced string IDs** (`minercraft:stone`) → per-world numeric palette,
      palette saved into the world file. No `enum BlockType`, ever.
- [ ] Registry: materials, items, tools, recipes, worldgen features, biomes
- [ ] Material properties: hardness, support distance, permeability, density, textures
- [ ] Load phase that scans `mods/` after base content
- [ ] Base game content registered through the public path — no privileged backdoor
- [ ] Chunk storage: palette + bit-packed array + uniform-chunk fast path
- [ ] `Texture2DArray` + custom `ShaderMaterial` (never an atlas)
- [ ] Greedy mesher, padded 34³ border copy, `ArrayPool<T>` for vertex buffers
- [ ] **Reserve vertex bits for light and AO** even though both are constant for now
- [ ] Render one chunk

### M2 — World

- [ ] `System.Threading.Channels` job queue for gen + mesh
- [ ] Worldgen: 2D fBm heightmap, strata layers, 3D noise caves
- [ ] Deterministic per-chunk seeding `hash(seed, cx, cy, cz)`
- [ ] Staged generation pipeline so cross-chunk features don't tear
- [ ] Chunk streaming around the player, empty-chunk skipping
- [ ] Measure: `RenderingServer` direct vs `MeshInstance3D` at target chunk count

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
