# MinerCraft — Worklog

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
