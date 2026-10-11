# Overburden

A voxel mining game where you are a mining operation fighting water and gravity.

Rain fills basins. Water finds your excavation. Saturated rock loses cohesion and the
ceiling comes down. You build pumps and shoring to fight back, which needs power,
which needs more mining.

No mobs, no combat. The mine is the antagonist.

Alpha Edition
https://drive.google.com/file/d/13kcW2xIEVM2Ur8eQSQurquE_8klUd-_H/view?usp=sharing

**Design and reasoning: [docs/DESIGN.md](docs/DESIGN.md). Progress:
[TODO.md](TODO.md). History: [WORKLOG.md](WORKLOG.md).**

## Status

**M0 through M5 done, and M6 under way** — 734 tests passing. Terrain streams around the
camera without end: worldgen and greedy meshing run on worker threads over palette-compressed
storage, drawn via `RenderingServer` with a texture array, with every material loaded from
`mods/overburden/*.json` exactly the way a third-party mod would load its own. Chunks load
and unload as you move, on a scheduler that reprioritises queued work and cancels what
went stale before a thread was free to run it. A pick swing carves a sphere out of the world,
gated by tool tier against material hardness. You walk it — an AABB against the voxel grid,
gravity, jump, step-up — and you cannot outrun the streamer falling 250 m at terminal
velocity. There is no sun: the voxel grid is the light, skylight and lamplight are separate
channels, and a mine runs from lit rock to actual black. **And it saves** — dig a tunnel, fly
away, quit, come back, and the tunnel is still there, along with what you were carrying and where
you were standing. **You can build, too**: brushes place a cube, a slab, a four-step ramp or a
sphere on a one-metre lattice, out of the volume you mined, and Alt makes either mouse button
work one voxel at a time.

Some numbers it is built on:

- **8,208 bytes** for a 32,768-voxel chunk, and 196,608 naive faces reduced to **850
  quads** — 7.2× better than culled meshing, 231× better than naive.
- **0.47 ms** to generate a chunk, of which caves are a fifth. They were four fifths until
  the noise moved onto a coarse lattice, which bought five times as much terrain on screen
  while flying.
- **~46 KiB per drawn chunk**, mesh-dominated against ~2 KB of voxel storage — which is why
  view distance is a horizontal radius and a vertical one, not a single number.
- **189 bytes** to store an edited chunk, and **35 µs** to read one back and rebuild it —
  which turned out to make a dug-in world *faster* to enter than a fresh one, because loading
  1,521 chunks beats generating them.

**M3 is mining, and you can do it.** Hold the left mouse button and a sphere of material
comes out of the world, gated per voxel by hardness. Swing a `hand_pick` at a dirt bank with
a stone seam through it and the dirt goes while the seam stands — no special case, no "you
cannot mine this" message, just the shape of the hole telling you what your tool is for. Tool
tiers are content, loading from `mods/overburden/tools/*.json` through the same loader
materials use; the number keys switch between them.

It has been played, and it feels good — which was the actual bar, this being the first
milestone whose output is a feeling rather than a number.

The milestone's question was whether a pick swing fits inside a frame. Mining continuously
while flying costs **a fifth of a percent of one core** and moves the pipeline's p99 from
0.91 ms to 1.04 ms, against a 16.7 ms frame. It fits.

M6 is persistence, placement and volumetric inventory, planned in
[docs/M6-PLAN.md](docs/M6-PLAN.md). Its structural point: every chunk until now was recoverable
from nothing, so unloading one was free. M6 is the first milestone that creates state whose only
copy is on disk — which makes the unload path a write, and a bug there destroys work that cannot
be rebuilt. Save and load are done; brush placement and the inventory are next.

## Run it

```
start.cmd
```

Needs Godot 4.7.1-stable **mono**, either vendored at `./Godot_v4.7.1-stable_mono_win64/`
or shared from `../GAME/`. Bindings follow Minecraft where the idiom is borrowed: WASD to move,
Space and Shift for up and down, Ctrl to sprint or fly faster, Esc to release the mouse.
**F toggles walking and flying**; walking, Space jumps and **Shift sneaks, which will not let you
walk off an edge**. Left mouse mines a sphere, right mouse places a brush, and **Alt+click is
precision on either** — one voxel taken, one voxel placed. B cycles the brush, V cycles the
material, and 1 and 2 pick a tool tier.

Your world is written to `user://worlds/default/world.db` — on Windows,
`%APPDATA%/Godot/app_userdata/Overburden/worlds/`. It saves as chunks unload, every thirty
seconds, and on quit.

```
dotnet test Overburden.slnx
```

Worldgen has a benchmark. Run it in Release — Debug is about three times slower and the
number looks just as real:

```
dotnet run --project Overburden.Bench -c Release
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
