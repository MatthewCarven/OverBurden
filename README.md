# MinerCraft

A voxel mining game where you are a mining operation fighting water and gravity.

Rain fills basins. Water finds your excavation. Saturated rock loses cohesion and the
ceiling comes down. You build pumps and shoring to fight back, which needs power,
which needs more mining.

No mobs, no combat. The mine is the antagonist.

**Design and reasoning: [docs/DESIGN.md](docs/DESIGN.md). Progress:
[TODO.md](TODO.md). History: [WORKLOG.md](WORKLOG.md).**

## Status

**M0 and all of M1 done**, 154 tests passing. A chunk of generated terrain renders
through a greedy mesher, drawn via `RenderingServer` with a texture array, over
palette-compressed storage, with every material loaded from `mods/minercraft/*.json`
exactly the way a third-party mod would load its own.

On that test chunk: **8,208 bytes** for 32,768 voxels, and 196,608 naive faces reduced to
**850 quads** — 7.2× better than culled meshing, 231× better than naive.

M2, chunk streaming and infinite terrain, is next.

## Run it

```
start.cmd
```

Needs Godot 4.7.1-stable **mono**, either vendored at `./Godot_v4.7.1-stable_mono_win64/`
or shared from `../GAME/`. WASD to move, Space/Ctrl for up and down, Shift to sprint,
Esc to release the mouse.

```
dotnet test MinerCraft.slnx
```

## The shape of it

- **Godot 4 + C#**, .NET 8. Headless simulation core split from the Godot front end.
- **0.25 m voxels**, 4 per metre, in 32³ chunks. Mining carves material with a pick
  rather than deleting metre cubes; placement uses brushes, and inventory is
  volumetric — litres of stone, not block counts.
- **Three-tier fluid**: analytic watershed basins for rain and standing water,
  volume-conserving cellular automata for water that's moving, and a coarse
  groundwater saturation field for seepage.
- **Structural integrity** with per-material support distances, where saturation
  degrades support — so water doesn't merely flood the mine, it causes the cave-in.
- **Moddable from the first commit.** Namespaced string IDs and a content registry
  from M1, with the base game registered through the same public path a mod would
  use. Lua scripting via MoonSharp later.

## Built with

Designed and built by Matthew Carven with [Claude Code](https://claude.com/claude-code)
(Claude Opus 5) as co-author. See [CONTRIBUTORS.md](CONTRIBUTORS.md).
