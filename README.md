# MinerCraft

A voxel mining game where you are a mining operation fighting water and gravity.

Rain fills basins. Water finds your excavation. Saturated rock loses cohesion and the
ceiling comes down. You build pumps and shoring to fight back, which needs power,
which needs more mining.

No mobs, no combat. The mine is the antagonist.

**Design and reasoning: [docs/DESIGN.md](docs/DESIGN.md). Progress:
[TODO.md](TODO.md). History: [WORKLOG.md](WORKLOG.md).**

## Status

Planning only — there is no code yet. The design is settled through to v1.0 and
broken into milestones; M0 (Godot project skeleton, `Core`/`Game` split, a textured
cube, a flycam) is next.

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
