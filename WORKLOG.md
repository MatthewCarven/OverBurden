# MinerCraft — Worklog

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
