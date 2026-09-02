# MinerCraft — Design Document

> Status: M0–M6 built and played, M7 planned. Locked decisions here; per-milestone plans in
> `M*-PLAN.md`.
> Last updated: 2026-09-01

## One-line pitch

You are a mining operation fighting water and gravity.

## The core loop

Rain fills basins → water finds your excavation → saturated rock loses cohesion →
the ceiling comes down → you build pumps and shoring to fight back → which needs
power → which needs more mining.

Flooding, structural collapse, and automation are not three features. They are one
loop, and each feeds the next. Design decisions get evaluated against whether they
serve it.

Explicitly **not** in scope: mobs, combat, AI pathfinding. Pressure comes from
simulation, not enemies.

---

## Locked decisions

| Decision | Choice | Rationale |
|---|---|---|
| Engine / language | **Godot 4 + C# (.NET)** | Renderer, input and windowing for free; real threads for meshing and worldgen; Godot 4.7 mono already on disk |
| Voxel scale | **0.25 m (4 per metre)** | 64x Minecraft density. Sub-voxel pick carving feels like digging, not deleting cubes. Tractable with palette compression + greedy meshing + coarse lighting |
| Chunk size | **32³ voxels = 8 m cube** | Small enough that a pick swing remeshes fast; empty-chunk skipping keeps draw calls sane |
| Scope target | Survival, **no mobs** | Replaced by flooding / collapse / automation |
| Mod support | **Data mods from M1, Lua (MoonSharp) at M12** | Registry discipline costs ~15% up front, ~300% to retrofit |
| Multiplayer | **Not now, but don't design it out** | Headless sim core + event bus keeps the seam open |
| Borrowed idioms | **Match Minecraft unless there is a stated reason not to** | Settled 2026-08-31. Where a control or a behaviour is one players already know, muscle memory is decades old and this project's is days old, so the burden of proof is on deviating |
| Simulation speed | **Slower than realtime, on purpose** | Settled 2026-09-01. Rain, water, saturation and collapse are slow *in the world*, so a sim that runs behind the frame rate matches the phenomenon rather than approximating it — and a late tick stops being a correctness failure. See below |

### Borrowed idioms, and where the line is

Where this game does something Minecraft also does, **it should do it the same way unless there is
a recorded reason.** Settled after M6c's play found the movement keys had been mirror-imaged since
M2 — sprint on Shift and descend on Ctrl, where Minecraft is Sneak = Shift and Sprint = Ctrl.
Nobody had noticed for four milestones because each key was individually defensible.

The rule cuts both ways and the second half matters more:

- **A borrowed control keeps its meaning.** Shift sneaks and descends; Ctrl sprints and hurries;
  Space jumps and rises. A player who has crouched at the edge of a drop before knows what should
  happen, and a different answer reads as this game being broken rather than as it having its own
  opinion.
- **A borrowed control brings its behaviour with it.** Sneak is not a speed modifier with a
  familiar key — it stops you walking off an edge, and that is the whole reason anybody presses
  it. Taking the key without the rule is the worse half of both options.
- **Where this game is genuinely different, deviate and say why.** Voxels are 0.25 m rather than
  1 m, mining is a sphere rather than a block, and placement is brushes on a metre lattice; none of
  those has a Minecraft answer to borrow. **Alt is precision on both mouse buttons** and Minecraft
  has no equivalent at all, so it is chosen on its own merits.

The trap this closes: a binding that is individually reasonable can still be wrong, because what
makes it wrong is a convention held somewhere other than in this repository.

---

## Non-negotiables (decided week one, murder to change later)

1. **Namespaced string IDs.** `minercraft:stone`, `mymod:copper_ore`. Resolved to
   numeric runtime IDs through a per-world palette that is **saved into the world
   file**. A hardcoded `enum BlockType` kills modding permanently and you won't
   notice for three months.
2. **Palette + bit-packed chunk storage with a uniform-chunk fast path.** At 0.25 m
   this is a requirement, not an optimisation. Raw storage blows past 300 MB;
   palette-compressed lands near 40 MB.
3. **Texture2DArray, never an atlas.** Greedy meshing needs tiled UVs. An atlas
   cannot do it, and swapping later is a rewrite of the mesher and shader.
4. **Reserve vertex bits for light and AO now**, even if both are constant at first.
5. **The base game is written as a mod**, against the same public registry API, with
   no privileged backdoor. This is the only thing that guarantees the mod API is
   sufficient to build a real mechanic rather than just recolour things.

---

## Architecture

### Storage

- Chunk = `{ palette, bitpacked array }`, 32³ voxels, with a single-value special
  case for the enormous number of all-air and all-stone chunks.
- Materials carry properties, all moddable: hardness, support distance,
  **permeability**, density, texture refs.

### Meshing

- **Greedy meshing**, mandatory at this scale. Output scales with *surface area*,
  not voxel count — the same hillside at 0.25 m merges into roughly the same quad
  count as at 1 m. What gets 64x more expensive is scanning the volume, which is
  exactly what palette compression makes cheap.
- Mesh jobs take a **padded 34³ border copy** so they touch zero shared state and
  run lock-free on worker threads.
- Pool vertex arrays (`ArrayPool<T>`). GC churn from per-remesh allocation causes
  frame hitches exactly while mining.

### Streaming

- **Viewers are plural and live in Core** — a position and two radii, not a camera. The
  Godot layer only reports where the player is. Multiplayer is then a second viewer rather
  than a special case.
- **Vertical view distance is its own radius, not a ratio of the horizontal one.** A mining
  game's content is below the player, so the useful shape is a tall column. "View distance"
  is never one scalar.
- **Chunks are refcounted across viewers**; zero unloads. Built with one viewer, because
  retrofitting it is a rewrite of the policy.
- **Box difference on a boundary crossing**, so only the shells that entered and left are
  iterated — O(R²) per crossing, not O(R³). Chunk quantisation supplies the hysteresis.
- **The loaded region is larger than the visible one by a two-chunk margin**, and both
  chunks of it are load-bearing: the mesher's padded 34³ copy needs the first ring, and the
  cross-chunk feature stage needs the second, because a chunk can only run stage 2 once its
  own neighbours have terrain to be written into. Costs ~41% more chunks at radius 16.
  **Not a tuning knob** — measured at M2d, lowering it shrinks the meshable radius by one
  rather than buying anything, because the barrier applies whether or not stage 2 places
  something. View distance is the knob.
- **A third reason arrived with M5, and it is the tightest: light reach.** 4-bit light
  reaches 15 m and the margin is 16 m, so a source outside the loaded region contributes
  `max(0, 15 − 16) = 0` at the visible box — light computed over the loaded region is
  *exact* inside the drawn one, with 1 m to spare. **5-bit light would reach 31 m and need a
  4-chunk margin.** The two constants live in different files and nothing would fail to
  connect them, so they are recorded on both sides.
- **View distance costs memory through meshes, not through chunks** — ~46 KiB per drawn
  chunk against ~2 KB of voxel storage. So horizontal radius is expensive and vertical
  radius is nearly free, since extra vertical chunks are sky and buried rock that mesh to
  nothing. That is the measured argument for the separate radii above.
- **One enum per chunk, `Unloaded → Queued → Generating → Generated → Meshing → Meshed`**,
  not a scatter of `bool`s that can express contradictory states. "Already queued" is a
  state, so it cannot drift out of sync with the thing it describes.

### Lighting

- **Propagate on a coarse 1 m grid, sample trilinearly at mesh time.** Light is
  low-frequency; nobody sees the difference and it is 64x cheaper than propagating
  at voxel resolution. This is a mesher + shader decision, so it is made up front.
  **Two consequences noticed while planning M5, neither of which reverses this:**
  a per-vertex sample puts light in the greedy merge key, and a merged quad only
  interpolates its four corners — so faces merge iff all four corner shades match, which
  is exact but costs quads wherever light varies — **AO alone measured 1.88× the quads at M5a,
  against a 2–4× guess**, so the half of that cost which is already spent is known. And a 1 m
  cell holds **64 voxels**, which
  is wider than the tunnels this game is about, so opacity has to be a property of the cell
  *face* rather than the cell. The named fallback — sampling light in the fragment shader
  from a small 3D texture, which costs no remesh on a light change — is measured before it
  is built. See [M5-PLAN.md](M5-PLAN.md).
- 4 bits skylight + 4 bits blocklight, BFS flood fill, incremental re-propagation.
- Vertex AO computed at mesh time — cheap, and the single biggest visual payoff.
- **No directional light. The grid is the light** — settled 2026-08-30, planning M5. The
  renderer is `unshaded`, with a per-face tint computed from the normal standing in for
  directional shading. That is what makes a cave dark rather than blue-dark, and it retires
  the low-ambient-fill and terrain-terracing artefacts M0–M1 recorded, both of which are the
  sun striking the integer heightmap's risers.
- **Skylight and blocklight reach the shader separately**, rather than pre-multiplied into
  one shade value. Not a speculation about day and night: the **wet-surface term below needs
  skylight on its own**. A diurnal cycle then falls out as a uniform on the sky channel and
  costs no repropagation — scaling the seed and reflooding would relight the world every
  frame.
- **Skylight is seeded analytically, not flooded from the top of the world**, because the
  vertical view radius means there is no top of the world. `SurfaceHeightAt` supplies it for
  unedited terrain and a per-column sky-height map carries what edits change.

### Slower than realtime is accuracy, not approximation

Raised by Matthew on 2026-09-01, before M7 or M8 was planned: *"I'd be happy if water is 'less
than realtime' because it does work like that, it usually takes a few minutes of rain to fill the
lower levels of a large cave."*

The whole antagonist stack — rain, then water, then saturation, then loss of cohesion, then
collapse — is made of processes that are slow in the world. **A simulation that runs slower than
the frame rate is not cutting a corner here; it is matching the phenomenon.**

- **Falling behind stops being a correctness failure.** Every budget in this engine so far has had
  to be argued against a correctness risk. If the water tick is allowed to be late, a skipped tick
  means the water arrives a moment later — which is indistinguishable from the design.
- **It licenses the analytic seam.** A slow process described by a rate can be **integrated
  forward** when nobody is watching, so cells simulate in view and accumulators integrate out of
  view. That reads as a cheat if water is supposed to be realtime and as the correct
  implementation if it is not. **M7b's oracle is this claim made testable**: integrating 600 s in
  one call must equal 600 one-second calls, to the litre.
- **The same argument produced M9's leading candidate a milestone earlier.** Deferred collapse —
  a random delay between *became possible* and *fell* — is the same structural win, and it is
  what gives warning signs somewhere to live.

**What it does not buy:** the active set is still the thing to engineer. The tick *rate* is what
this licenses; the set *size* is still bounded by sleep/wake rules. And there is a perceptual
floor near the player — a water surface two metres away that jumps once a second is not slow, it
is broken, so Tier A carries a level the renderer can interpolate between ticks.

### Fluid — three-tier hybrid

Minecraft's level 0-7 flow model is rejected: it does not conserve volume, never
equalises, and creates infinite sources. Its stair-stepping would be 4x more
visible here.

- **Tier A — Analytic basins.** Watershed analysis at worldgen finds basins, their
  outflow saddles, and a precomputed volume→level curve. Rain adds volume to an
  accumulator; the surface level is *solved, not simulated*. Overflow routes
  downstream. Costs a float per basin, scales to the whole world.
- **Tier B — Active cellular automata.** Volume-conserving CA over an explicit
  **active-cell set**, for water that is actually moving (tunnel breach, waterfall,
  pump outlet). Push down where there is capacity, equalise sideways otherwise. Stable
  cells sleep and merge into a Tier A body. Sleeping is load-bearing, not an optimisation.
  **Corrected at M8a-1, 2026-09-02: cells hold millilitres, capacity 15,625, not an
  amount 0-255.** This line predates M7 and therefore predates litres being the currency
  of water here. A 0.25 m cell is **15.625 litres — not a whole number** — so a 0..255
  scale makes one unit 61.27 mL and every conversion across the Tier A ↔ Tier B seam
  rounds, which M8's no-tolerance conservation oracle cannot survive. A litre is exactly
  1,000 mL, so the seam is exact in both directions; the correction costs one byte per
  cell. **1,000,000 mL per cubic metre over 4³ voxels is 15,625 exactly** — a division
  that would truncate at any voxel scale that is not a power of two.
- **Tier C — Groundwater saturation.** Coarse per-1 m scalar field. Rain feeds it,
  it drains downhill, and it seeps into excavations at a rate set by saturation and
  material permeability. This is what makes mining under a lake frightening and
  gives pumps a job that never ends.

### Rain

- Camera-locked box of GPU-instanced velocity-stretched billboards
  (`MultiMeshInstance3D` / `GpuParticles3D`). Approximation, reads as volumetric,
  costs nothing.
- **Kill particles where skylight is occluded** — reuse the lighting skylight value
  so rain stops at the mine entrance. Nearly free, and it is the detail people
  notice.
- Wet-surface shader term driven by skylight x recent rainfall. Splash particles on
  impact. Rainfall feeds Tier A and Tier C. **The term costs one uniform and no remeshes** —
  skylight is already per-vertex and rainfall is global — and it is the reason the two light
  channels are kept separate rather than pre-multiplied.

**Settled at M7's planning pass, 2026-09-01** — full reasoning in [M7-PLAN.md](M7-PLAN.md):

- **The watershed grid is 4 m**, deliberately between the 1 m light cell and the 8 m chunk column
  so it borrows neither one's revisit trigger.
- **A basin is identified by the world coordinate of its pit, never by an array index.** The
  analysis window is player-centred and renumbers when it moves; an index key silently reattaches
  stored water to a different basin.
- **The window edge is an outlet, not a wall**, because under-filling is the safe failure and
  over-filling drowns a base for a reason the player cannot see.
- **Rainfall is a pure function of (seed, world time)**, not a stepped state machine — which makes
  integrate-forward exact and server-authoritative for free.
- **The world gains a clock at M7** (header v2 → v3, additive, migrates), and time advances only
  while the world is open.
- **Edits do not change the watershed at M7.** Water reaching an excavation is Tier B, which is
  M8; the seam for an edited-surface watershed is `SkyColumn`, which already exists and already
  persists.

### Structural integrity

- BFS from anchored material (bedrock, or anything transitively reaching it), with a
  per-material **support distance** — stone carries far, dirt less, sand barely.
  Unsupported material collapses into loose rubble.
- Run **incrementally, locally, on a budget, off-thread**, triggered by mining.
  Never globally.
- **Saturation degrades support distance.** Water does not merely flood the mine, it
  *causes the cave-in*. This is the single interaction that ties the game together.

### Player

- **Roll our own AABB-vs-grid collision, axis-separated** (move X and resolve, then
  Y, then Z). A combined sweep produces corner-snag bugs.
- **Do not use Godot physics for terrain.** `ConcavePolygonShape3D` per chunk is a
  known performance trap. Godot physics is fine for dropped items.
- Block picking: Amanatides & Woo voxel DDA. Exact, cheap, returns the hit face
  normal for placement.

### Mining and placement — decoupled granularity

- Storage is 0.25 m, but the player never places single sub-voxels; that would be
  miserable.
- **Mining is sculpting**: a pick swing removes a sphere or cone of material. Tool
  tier sets radius and hardness threshold. **Confirmed 2026-08-30, against the
  alternative**: a single-voxel default was proposed and rejected, because `hand_pick`'s
  sphere is ~17 voxels and the default would have been ~17× slower to excavate with the
  starting tool — in a game about a mining operation, and against the volumetric-inventory
  argument two lines below. **Single-voxel mining exists as `Carve.Chip`, held under Ctrl**,
  because precision is what a builder wants and a builder will hold a key.
- **Placement uses brushes**: 1 m cube, slab, ramp, sphere. Fine detail comes from
  carving, not from placing.
  - **A "ramp" is an arrangement of 0.25 m cubes, not a sloped block** — clarified while planning
    M6, because the word invites the other reading and the other reading is a rejected
    alternative. A sloped block is a "a voxel is one material or another" violation wearing a
    different hat, which M3c and M4d have each already argued. So a 1 m ramp brush is a four-step
    staircase, a slab is 4×2×4 voxels at 0.5 m thick, and both are walkable by M4b's 0.5 m
    step-up with no new collision shape.
  - **Brushes snap to the world 1 m lattice**, because the slab and the ramp only make sense on
    one — a ramp that is not grid-aligned cannot be tiled into stairs. The lattice is what makes
    the brush *set* coherent rather than just the cube. Recommended at M6c, judged by play at M6d.
- **Inventory is volumetric** — litres of stone, not "3 stone blocks". Falls out of
  sub-voxel mining, makes crafting costs continuous, makes big excavations
  meaningfully productive.

### Automation

Every machine registers through the same public registry path a Lua mod would use.
Machines are the dogfood test: if a pump cannot be built through the mod API, the
API is wrong.

Core set, each one existing to fight water or gravity:

- **Pumps** — move volume between fluid tiers
- **Shoring / supports** — raise local support distance
- **Drills** — scheduled sub-voxel carving
- **Pipes and conveyors** — fluid and item transport
- **Power** — a simple generation / consumption / storage network

Machines tick on a fixed **20 Hz sim step** in a spatially-partitioned update list,
decoupled from render framerate. Keeps it deterministic for possible multiplayer.

### Mod support

- **Registry**: one owner of every definition — materials, items, tools, recipes,
  worldgen features, biomes. Populated in a load phase that scans `mods/` after base
  content. Keyed by namespaced string.
- **Event bus**: typed events for everything a mod might react to — `BlockChanged`,
  `ChunkGenerated`, `PlayerTick`, `ToolUsed`, `RecipeCrafted`. These are also
  approximately the multiplayer packets.
- **Tier 1 (M1)**: data mods — JSON/TOML defs + textures from `mods/`. No code
  execution, no security surface. Roughly 80% of what mods actually do.
- **Tier 2 (M12)**: MoonSharp Lua. Pure C#, no native deps, works with Godot,
  sandboxable. Deferred until the API surface has settled and we know what is worth
  exposing.
- **Tier 3**: C# assembly loading. Deliberately not planned — zero sandboxing,
  breaks under AOT export, permanent API-stability tax.

### Persistence

SQLite, `(cx, cy, cz) → blob`. Region files are the "proper" answer; SQLite is the
pragmatic one for a solo project. World file carries the string→int ID palette.

**"World file" means a table in the same database, not a sidecar** — settled while planning M6,
because the obvious layout (JSON beside the chunk DB) is the one arrangement that can
desynchronise: a crash between the two leaves blobs full of numeric IDs whose meaning is a version
behind, which is the exact failure non-negotiable 2 exists to prevent. Atomicity is most of what
SQLite is being paid for here. Any write that allocates a new numeric ID commits in the same
transaction as the blob that first used it. See [M6-PLAN.md](M6-PLAN.md).

---

## Godot 4 / C# specific traps

- `RenderingServer` direct calls for chunk meshes, not thousands of
  `MeshInstance3D` nodes — node overhead is real at this chunk count.
- **Job queue: a flat list re-sorted on a timer, not `Channels` and not a heap.**
  Corrected 2026-08-26; see [M2-REFERENCE-NOTES.md](M2-REFERENCE-NOTES.md). `Channels` is
  FIFO and cannot reprioritise queued work as the player moves, nor drop work that went
  stale before it ran. Channels remains right for handing *results* back to the main
  thread, which really is FIFO.
- Dedicated worker threads for that queue, not `Task.Run` — the .NET thread pool knows
  nothing about voxel priority.
- `ArrayPool<T>` for mesh buffers.
- Custom `ShaderMaterial` + `Texture2DArray`.
- Structs over classes in hot paths; watch GC.

---

## Milestones

### Phase 1 — Engine (known quantity)

| | |
|---|---|
| M0 | Godot skeleton, `RenderingServer` cube, flycam |
| M1 | Chunk storage (palette + bitpack + uniform fast path), **string-ID registry**, greedy mesher, one chunk |
| M2 | Chunk streaming, threaded worldgen (heightmap, strata, caves), infinite terrain |
| M3 | DDA raycast, sub-voxel sphere carve, incremental remesh — **first fun moment** |
| M4 | Player AABB physics, gravity, jump |
| M5 | Coarse-grid lighting + vertex AO |
| M6 | SQLite save/load, brush placement, volumetric inventory — **v0.1 playable sandbox** |

### Phase 2 — The loop (novel engineering, expect surprises)

| | |
|---|---|
| M7 | ✅ **built and played 2026-09-02** — watershed basins, Tier A rain accumulation, rain particles, wet shader, **a water surface you can watch rise** |
| M8 | Tier B active fluid CA + Tier C groundwater seepage — *the mine floods* |
| M9 | Structural integrity, collapse, saturation weakening — *the mine kills you* — **v0.5, the game exists** |

### Phase 3 — Depth

| | |
|---|---|
| M10 | Machine framework, power, pumps, shoring — *you fight back* |
| M11 | Drills, conveyors, crafting, health — **v1.0** |
| M12 | MoonSharp scripting API |
| M13 | Distance LOD, biomes, polish |

Over-invest in **M1**. Storage and registry are the two things that are murder to
change later.

---

## Rejected alternatives, and why

- **Python + moderngl** — fits the rest of the portfolio, but the GIL caps threaded
  meshing and render distance. Godot gives real threads.
- **0.125 m voxels** — 512x density. Lighting and remesh cost become the dominant
  project constraint; LOD stops being optional.
- **Hybrid 1 m blocks that subdivide when damaged** — memory-cheap, but two world
  representations complicate meshing, collision, saving, and especially the mod API.
  Modders would have to understand both.
- **Minecraft-style fluid flow** — no volume conservation, no equalisation, infinite
  sources, and 4x more visible stair-stepping at this scale.
- **Godot physics for terrain collision** — `ConcavePolygonShape3D` per chunk is a
  well-known performance trap.
- **Texture atlas** — incompatible with greedy meshing's tiled UVs.
- **`System.Threading.Channels` as the gen/mesh job queue** — held until 2026-08-26. It is
  FIFO, so it cannot reprioritise queued work as the player moves, and cannot drop work
  that became irrelevant before it ran. Both are the entire purpose of a voxel job queue.
- **A priority heap for that queue** — the obvious replacement, and wrong for the same
  reason: it orders correctly at insert time, and insert time is precisely when the
  ordering is least valuable. A chunk queued at 20 m may be 200 m away before a worker
  frees up.
