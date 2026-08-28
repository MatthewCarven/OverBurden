# MinerCraft

A voxel mining game where you are a mining operation fighting water and gravity.

Rain fills basins. Water finds your excavation. Saturated rock loses cohesion and the
ceiling comes down. You build pumps and shoring to fight back, which needs power,
which needs more mining.

No mobs, no combat. The mine is the antagonist.

**Design and reasoning: [docs/DESIGN.md](docs/DESIGN.md). Progress:
[TODO.md](TODO.md). History: [WORKLOG.md](WORKLOG.md).**

## Status

**M0, M1 and M2 done**, 274 tests passing. Terrain streams around the camera without end:
worldgen and greedy meshing run on worker threads over palette-compressed storage, drawn
via `RenderingServer` with a texture array, with every material loaded from
`mods/minercraft/*.json` exactly the way a third-party mod would load its own. Chunks load
and unload as you move, on a scheduler that reprioritises queued work and cancels what went
stale before a thread was free to run it.

Some numbers it is built on:

- **8,208 bytes** for a 32,768-voxel chunk, and 196,608 naive faces reduced to **850
  quads** — 7.2× better than culled meshing, 231× better than naive.
- **0.47 ms** to generate a chunk, of which caves are a fifth. They were four fifths until
  the noise moved onto a coarse lattice, which bought five times as much terrain on screen
  while flying.
- **~46 KiB per drawn chunk**, mesh-dominated against ~2 KB of voxel storage — which is why
  view distance is a horizontal radius and a vertical one, not a single number.

M3, mining, is next. It is the first thing that will be fun.

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

Worldgen has a benchmark. Run it in Release — Debug is about three times slower and the
number looks just as real:

```
dotnet run --project MinerCraft.Bench -c Release
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

## Licence

[PolyForm Noncommercial 1.0.0](LICENSE.md) — source-available, not open source.

Read it, fork it, mod it, learn from it, share it. Don't sell it. Personal, hobby,
research, educational and charitable use are all explicitly permitted; commercial use
is not.

Chosen because it is the low-regret option while the copyright is held by one person:
relaxing a licence later is one commit, but code once published under a permissive
licence can never be pulled back. If this never becomes commercial, it can move to MIT
at any time.

## Built with

Designed and built by Matthew Carven with [Claude Code](https://claude.com/claude-code)
(Claude Opus 5) as co-author. See [CONTRIBUTORS.md](CONTRIBUTORS.md).
