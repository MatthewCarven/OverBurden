# MinerCraft — orientation

A voxel mining game in Godot 4 + C#. **You are a mining operation fighting water and
gravity.** Rain fills basins, water finds your excavation, saturated rock loses cohesion
and the ceiling comes down; you build pumps and shoring to fight back, which needs power,
which needs more mining. No mobs, no combat — the mine is the antagonist.

## Read these before changing anything

| Document | What it is |
|---|---|
| [docs/DESIGN.md](docs/DESIGN.md) | Locked decisions, the five non-negotiables, per-system architecture, milestones, **and a list of rejected alternatives so they don't get re-litigated** |
| [docs/M1-PLAN.md](docs/M1-PLAN.md) | Worked example of how a milestone gets planned before it gets built |
| [docs/M2-REFERENCE-NOTES.md](docs/M2-REFERENCE-NOTES.md) | What reading godot_voxel's scheduler and streaming taught us, and what it changed in the M2 plan. **Read before building the job queue.** |
| [docs/adr/](docs/adr/) | ADR-0001: why we build our own voxel engine rather than adopt godot_voxel, **with named revisit triggers** |
| [TODO.md](TODO.md) | Milestones as checkable tasks, and the decisions still open |
| [WORKLOG.md](WORKLOG.md) | What happened and why, newest first. Findings worth keeping live here. |

## Non-negotiables

Violating any of these is expensive to undo. They are argued for in DESIGN.md; don't
quietly reverse one.

1. **`MinerCraft.Core` never references Godot.** That absence is the multiplayer seam.
   `MinerCraft.Core.Tests` exists partly to enforce it — if Core needs Godot, the tests
   stop compiling.
2. **Content IDs are namespaced strings** (`minercraft:stone`), resolved to numeric IDs
   through a table the **world** owns, not the process. Never an enum. Never renumber,
   never compact — a removed mod leaves a hole that stays.
3. **`minercraft:air` is numeric ID 0**, always.
4. **Texture arrays, never an atlas.** Greedy meshing tiles UVs past 0..1 and an atlas
   cannot express that.
5. **The base game is a mod.** It loads from `mods/minercraft/` through the identical
   loader a third-party mod uses, with no privileged path. If something can't be
   expressed the way a mod would express it, the format is wrong.

Two more that are settled but easier to forget:

- **Chunk storage order is `[z][x][y]` — Y contiguous.** Nearly every mechanic here is
  vertical (skylight, falling water, support propagation, groundwater drainage). This was
  wrong once already; see the 2026-08-25 worklog entry.
- **Light and AO are in the mesher's merge key** even though both are constant until M5.
  Removing them "because they're unused" means rewriting the sweep later.

## Commands

Build and test:

```bash
dotnet test MinerCraft.slnx
```

Run the game:

```bash
start.cmd
```

Verify headlessly — no display needed, catches scene and script errors and prints the
diagnostics:

```bash
../GAME/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path MinerCraft.Game --headless --quit-after 60
```

If Godot has never opened the project, run once with `--headless --import` first.

Godot 4.7.1-stable **mono** is not vendored here; `start.cmd` falls back to the shared
copy in `../GAME/`.

## Conventions

- Append findings and decisions to WORKLOG.md; tick and add tasks in TODO.md. Both as you
  go, not at the end.
- Commit with a message that explains *why*, and end with
  `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.
- Matthew runs `git init` and `git push` himself — flag when either is needed.
- `reference/` is gitignored and holds third-party sources we **read but never build
  against** (currently godot_voxel). Nothing in it belongs in a `.csproj`.

## Where things stand

M0 and all of M1 are done, 154 tests passing. A chunk of generated terrain renders
through the greedy mesher over palette-compressed storage, with materials loaded from
JSON. On that chunk: 8,208 B for 32,768 voxels, 196,608 naive faces → 850 quads.

**M2 is next: chunk streaming and infinite terrain.** The prerequisite reading of
godot_voxel's scheduling and streaming is **done** (2026-08-26) and paid for itself again:
it caught that the M2 job queue had been specified as `System.Threading.Channels`, and a
FIFO can neither reprioritise queued work as the player moves nor drop work that went stale
before it ran. Findings and the corrected plan are in
[docs/M2-REFERENCE-NOTES.md](docs/M2-REFERENCE-NOTES.md).

Next actual step is to **split M2 into sub-parts** the way M1 was split, before writing
code. The reference notes suggest the seams.

The licence is settled: [PolyForm Noncommercial 1.0.0](LICENSE.md), source-available. It
can be loosened to MIT any time; it cannot be tightened after the first outside PR is
merged, which is the real freeze point.
