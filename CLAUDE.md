# Overburden — orientation

A voxel mining game in Godot 4 + C#. **You are a mining operation fighting water and
gravity.** Rain fills basins, water finds your excavation, saturated rock loses cohesion
and the ceiling comes down; you build pumps and shoring to fight back, which needs power,
which needs more mining. No mobs, no combat — the mine is the antagonist.

**This file was consolidated on 2026-09-26.** It had grown to 385 KB — every session's hand-over
paragraph stacked on the last, and every flag's paragraph under Commands. Both moved out
verbatim: the flag and recipe reference is [docs/COMMANDS.md](docs/COMMANDS.md), and every
earlier "pick up here" with the M2–M8 milestone summaries is
[docs/STATUS-HISTORY.md](docs/STATUS-HISTORY.md). Nothing was deleted. **What prompted it was
stale data, not length** — Matthew, the same night: *"feel free to store data that you wish kept
in the project"*. So write freely, anywhere it belongs; the one rule is that **"Where things
stand" describes now**. At a session's close, update it and move the superseded hand-over into
STATUS-HISTORY, so nothing out of date reads as current.

## Read these before changing anything

| Document | What it is |
|---|---|
| [docs/DESIGN.md](docs/DESIGN.md) | Locked decisions, the five non-negotiables, per-system architecture, milestones, **and a list of rejected alternatives so they don't get re-litigated**. |
| [docs/COMMANDS.md](docs/COMMANDS.md) | **Every bench arm, game flag and headless oracle, verbatim.** Grep it for a flag before a run that takes flags. |
| [docs/STATUS-HISTORY.md](docs/STATUS-HISTORY.md) | Every earlier "where things stand", newest first, and the M2–M8 milestone summaries — the shortest account of why the engine is shaped as it is. |
| [TODO.md](TODO.md) | **Open work only** — what is next, what waits on Matthew, owed checks, named levers, later milestones. Delete a line once it is done and recorded. Everything finished up to 2026-09-26, with its reasoning, is [docs/TODO-HISTORY.md](docs/TODO-HISTORY.md): an older "in TODO under M…" means that file. |
| [MATTHEW-TODO.md](MATTHEW-TODO.md) | **Matthew's own short list** — the play queue, decisions waiting on his word, chores only he can run. One screen or two, never a record. **Keep it current at the end of a session**: move a played check to answered, add a new check when one is built, refresh the status block. Its "Notes to myself" section is his; fold what he writes there into the right plan and empty it. |
| [WORKLOG.md](WORKLOG.md) | What happened and why, newest first. Findings worth keeping live here. |
| [docs/SCALE.md](docs/SCALE.md) | How many voxels and cells exist at each view distance and what a full sweep costs. **Read before designing anything that checks the world periodically.** |
| [docs/adr/](docs/adr/) | ADR-0001: why we build our own voxel engine rather than adopt godot_voxel, with named revisit triggers. |
| [docs/M1-PLAN.md](docs/M1-PLAN.md) | Worked example of how a milestone gets planned before it gets built. |
| [docs/M2-REFERENCE-NOTES.md](docs/M2-REFERENCE-NOTES.md) | What reading godot_voxel's scheduler taught us. Read before touching the job queue. |
| [docs/M2-PLAN.md](docs/M2-PLAN.md) … [docs/M6-PLAN.md](docs/M6-PLAN.md) | M2 streaming, M3 mining, M4 the player, M5 lighting and AO, M6 persistence, placement and inventory. **Done**, kept for the reasoning. |
| [docs/M7-PLAN.md](docs/M7-PLAN.md), [docs/M7-PLAY.md](docs/M7-PLAY.md) | M7, rain. **Done and played 2026-09-02.** Splashes were cut; the occlusion field's estimate was ten times out. The play doc keeps the five verdicts. |
| [docs/M8-PLAN.md](docs/M8-PLAN.md) | M8, water gets in: Tier B's automaton, the seam, the hand-off and settle, the store, seepage, the mesh, the lake as a body (M8d-5). **Done and played 2026-09-11.** Its built sections are where the water's rules are argued. |
| [docs/WATER-THREAD-PLAN.md](docs/WATER-THREAD-PLAN.md) | The tick's near/far order, the field, and the tick as a worker job. **Built** (slices 1–3c). Read before touching the tick's order or its home. |
| [docs/FRONT-ADVANCE-PLAN.md](docs/FRONT-ADVANCE-PLAN.md) | Water that spreads sideways faster: `--frontadvance=` (4), fed by volume (`--fedshare=`), streams level their tip band (`--fedadvance=`, `--fedband=` 6). **Built and played 2026-09-24**; his load test is still owed. |
| [docs/M9-PLAN.md](docs/M9-PLAN.md) | M9, the mine kills you: the support field, the tint, the fall, the keystone. **M9a–M9d built and played.** Read its "two facts" first: there is no bedrock, and a literal distance rule opens a shaft to the surface over a 2 × 2 bite. |
| [docs/COLLAPSE-NOTES.md](docs/COLLAPSE-NOTES.md), [docs/COLLAPSE-PLAN.md](docs/COLLAPSE-PLAN.md) | **The slow-then-fast collapse, "Tetris gone bad"** (2026-09-26). Fourteen decisions taken; **part a and b-1 (all Core) built; b-2, the wire, next — beginning with the cut on a worker.** Read the notes' "What exists", then the plan's facts, "a-2 — built", "Part b — the code-level pass" and "b-1 — built". |
| [docs/M10-NOTES.md](docs/M10-NOTES.md), [docs/M10-PLAN.md](docs/M10-PLAN.md) | The quantum machine family and M10's first plan. Read before planning any machine. |
| [docs/M10-LADDER-PLAN.md](docs/M10-LADDER-PLAN.md) | **The ladder** — hand → steam → DC → AC → quantum and the crafting under it; twenty-three decisions, parts M10a–M10j. **M10a–M10c built; M10d is the play (his) and d-1/d-3 built; M10e (the conductor index) is next after the play.** Its one idea: connection is membership, movement a ledger transfer at the boundary. |
| [docs/UNDO-PLAN.md](docs/UNDO-PLAN.md) | Ctrl+Z, three steps. **Built and played 2026-09-22.** A fall clears the stack; the journal hangs off `SetVoxel` and `MachineSet`; an undo must not journal its own writes. |
| [docs/MAP-NOTES.md](docs/MAP-NOTES.md) | F12, the map. **Built 2026-09-23 (slices a–d), played: 2 km stays.** A picture must not depend on the viewer; the ramp is fitted to the view; the map aliases past ~16 m a pixel. |
| [docs/GLASS-PLAN.md](docs/GLASS-PLAN.md) | Glass. **Built 2026-09-25 on main; the look played 2026-09-26** (black, 0.20 — *"Glass looks GOOD!"*). The sky column asks `IsSolid`, so glass is a roof to rain and a true skylight. Open: a floor under glass gleams in a storm; water behind glass sort order. |
| [docs/POND-PLAN.md](docs/POND-PLAN.md) | The raised watershed and his water grid. **Built on the branch `water-column-test`, not on main**; its "better" criteria are in the branch's copy of this file, and the merge waits on his verdict. |
| [docs/TNT-NOTES.md](docs/TNT-NOTES.md) | Blasting. Notes, not scheduled; his design folded in (blasted voxels go nowhere; the shake). Six decisions still his. |
| [docs/ADAPTIVE-RADIUS-PLAN.md](docs/ADAPTIVE-RADIUS-PLAN.md) | The adaptive view radius. **Built 2026-09-06**; the plan's dwell rule was wrong, the built section says why. F11 far mode raises its ceiling. |
| [docs/CHUNK-CACHE-PLAN.md](docs/CHUNK-CACHE-PLAN.md) | The retained-chunk cache. **Built.** Read slice 2's section before touching `LightPropagator`. |
| [docs/MATERIALS-NOTES.md](docs/MATERIALS-NOTES.md) | Elements, and why not "just implement chemistry": geology that reacts to water, machine recipes as the chemistry set. |
| [docs/WORLDGEN-NOTES.md](docs/WORLDGEN-NOTES.md) | Ores, layers, trees, permeability — his thinking-ahead. Not scheduled; its three questions are his. |
| [docs/WATER-BODIES-NOTES.md](docs/WATER-BODIES-NOTES.md) | Resting water as one number over a void. Deferred: a drip did not force bodies (M8c's measurement). |

## Non-negotiables

Violating any of these is expensive to undo. They are argued for in DESIGN.md; don't
quietly reverse one.

1. **`Overburden.Core` never references Godot.** That absence is the multiplayer seam.
   `Overburden.Core.Tests` exists partly to enforce it — if Core needs Godot, the tests
   stop compiling.
2. **Content IDs are namespaced strings** (`overburden:stone`), resolved to numeric IDs
   through a table the **world** owns, not the process. Never an enum. Never renumber,
   never compact — a removed mod leaves a hole that stays. **And a retired namespace is an
   alias, forever**: `minercraft:` was the base game until 2026-09-03 and is inside every
   world saved before then, so `Identifier` folds it to `overburden:` at construction. Never
   remove an entry from `Identifier.RetiredNamespaces`.
3. **`overburden:air` is numeric ID 0**, always.
4. **Texture arrays, never an atlas.** Greedy meshing tiles UVs past 0..1 and an atlas
   cannot express that.
5. **The base game is a mod.** It loads from `mods/overburden/` through the identical
   loader a third-party mod uses, with no privileged path. If something can't be
   expressed the way a mod would express it, the format is wrong.

Two more that are settled but easier to forget:

- **Chunk storage order is `[z][x][y]` — Y contiguous.** Nearly every mechanic here is
  vertical (skylight, falling water, support propagation, groundwater drainage). This was
  wrong once already; see the 2026-08-25 worklog entry.
- **Light and AO are in the mesher's merge key.** Removing either "because it is constant here"
  means rewriting the sweep.

## Commands

The everyday ones. **Everything else — every bench arm, every flag with its control arm, every
oracle verbatim — is in [docs/COMMANDS.md](docs/COMMANDS.md).**

```bash
dotnet test Overburden.slnx
```

```bash
start.cmd
```

`start.cmd` builds `Overburden.Game` first and refuses to launch on a failed build, then passes
`-- --lightbudget=16 --adaptive=1 --fluidnear=2 --fluidjob=1` itself; anything you give it lands
after those as a game flag, and a later value wins. **The code's defaults are the other arms of
all four**, because every recorded measurement was taken at them and a profile does not go through
`start.cmd`. The Debug build it launches is **JIT-optimised since 2026-09-15**
(`Directory.Build.props`); every play or profile figure before that date is an unoptimised figure
and is not comparable.

Benches are **Release only** — Debug is 3–10× slower and reads like a real number:

```bash
dotnet run --project Overburden.Bench -c Release -- --light
```

The arms (`--light`, `--blob`, `--store`, `--watershed`, `--portal`, `--seep`, `--surface`,
`--seepawake`, `--support`, `--solve`, `--fluid`, `--spill`, `--lake`, `--evict`, `--terrain`,
`--front`, `--pump`) and their sweep flags are in COMMANDS.md. **`--support` wants
`DOTNET_TieredCompilation=0`**. **Read the machine-control line before quoting a bench figure**:
this machine has measured 2.5× slower hours apart with identical binaries.

Verify headlessly — catches scene and script errors; `--quit-after` counts **frames**, not seconds:

```bash
../GAME/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path Overburden.Game --headless --quit-after 900
```

**The r6 control, verbatim** — `2601 / 325 / 3446 at 411` is the pass, and it is the cheapest
proof the binary is the tree:

```bash
../GAME/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path Overburden.Game --headless --quit-after 2400 -- --nosave
```

Profile a flythrough (quits by itself; a `--quit-after` usually truncates it):

```bash
../GAME/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path Overburden.Game --headless -- --profile --radius=12 --vradius=6 --speed=40
```

Screenshot the real scene — the only way to answer a look question, since headless has no
renderer (it also builds no Tier B surface and no `WaterSurface`). `--settle=<ms>` overrides the
8 s wait, and a played world needs it:

```bash
../GAME/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path Overburden.Game --script res://screenshot.gd -- C:/tmp/shot.png
```

The rules that bite, every one of them learned the expensive way:

- **Game flags go after a bare `--`**, as `name=value`. One before it is dropped by the engine
  without a word; an unknown one after it prints `unknown flag … — ignored`.
- **`dotnet build Overburden.Game` before any hand-rolled Godot run.** Godot with `--path` loads
  whatever DLL is in `.godot/mono/temp/bin` and never rebuilds; `dotnet test` does not refresh it.
  While Matthew plays the DLL is locked — build to a scratch `-o` instead.
- **A measurement does not open the played world.** `--profile`, `--fall` and `--debuglamp` leave
  persistence off unless `--world=` names one, and a new world under `--profile` gets the fixed
  seed `0x4D494E455243` so every recorded figure keeps reproducing. `--nosave` beats `--world=`
  silently. Probe a played world **on a copy** (`worlds/<name>/` → `worlds/probe-…/`) and delete
  the copy after.
- **`--speed=0` is a standing start** and is distinct from omitting `--speed` (40 m/s).
- **Copy `logs/godot.log` aside before a headless probe** — Godot keeps five logs and rotates, so
  a handful of probes erases the play you meant to read. Read logs with `grep -a`, and count the
  `ERROR` lines before believing any report: an exception inside a Godot callback is caught and
  logged, not crashed, so it reads as a feature that works once.
- **This machine runs at two speeds about three times apart, minute to minute.** Never compare
  two runs across a phase: alternate A B A B, or interleave the arms in one process.
- **`--watch`** prints calls and min / mean / max for every `CallWatch`-named function under a
  profile. **The sampler attaches by PID, never launches**: `dotnet-trace collect -p <pid>
  --profile dotnet-sampled-thread-time,dotnet-common --format Speedscope --duration 00:00:00:30`.

**The world file lives at `user://worlds/<name>/world.db`** — on Windows
`%APPDATA%/Godot/app_userdata/Overburden/worlds/`. `--world=<name>` picks or creates one (quote it
whole if it has spaces: `start.cmd "--world=Deep Mine"`); `--seed=<n>`, `--relief=`,
`--reliefbase=`, `--reliefrare=` apply only to a world that does not exist yet. If Godot has never
opened the project, run once with `--headless --import`. Godot 4.7.1-stable **mono** is not
vendored here; `start.cmd` falls back to the shared copy in `../GAME/`.

## Conventions

- Append findings and decisions to WORKLOG.md; tick and add tasks in TODO.md. Both as you go, not
  at the end — and at the session's close, delete TODO's ticked lines once the finding is in the
  worklog and its plan, so TODO stays the open work.
- Commit with a message that explains *why*, and end with the `Co-Authored-By:` line the harness
  gives this session — never copy one from an older commit.
- Matthew runs `git init` and `git push` himself — flag when either is needed. Remote is
  `github.com/MatthewCarven/MinerCraft`, **private**, default branch `main`. Run `git fetch`
  before calling anything unpushed; the remote-tracking ref only moves on a fetch or a push.
- `reference/` is gitignored and holds third-party sources we **read but never build
  against** (currently godot_voxel). Nothing in it belongs in a `.csproj`.
- Working copies are CRLF (`core.autocrlf`). Edit through a Python script that reads bytes and
  writes back the endings it found; `sed -i` in Git Bash writes LF.

## Where things stand

**2026-09-26, late night.** Suite **1,955**; the r6 control to the digit. `main` and
`water-column-test` both match GitHub as of a fetch. `start2.cmd`–`start5.cmd` are Matthew's and
deliberately untracked.

**Next on my side: collapse b-2, the wire.** Part a and b-1 are built, all Core:
`CollapseDirector` sits between the events and the world, the scheduler hands it a due fuse,
and the bench's `--support --collapse --pieces` drives it with every oracle holding. The game
still runs the sweep. **b-1's first finding is that the re-cut costs 29 ms mean and 133 ms worst
in the room**, so b-2 should begin with decision 7's named lever, the cut on a worker. That is
his word to give; it is in MATTHEW-TODO. Half of a big collapse is rubble tumbling off the pile,
and whether that reads as settling or churn is his once he can see it (part c). The collapse
sensor (part f) reads `CollapseDirector.Events`.

**Waiting on him** (all in MATTHEW-TODO): the pond branch's verdict (merge or retire
`water-column-test`); the fed-advance load test (`start3` against `start3 --fedadvance=0`); the
M10d play queue and the older checks; glass's storm gleam and water sort order; TNT's six
decisions. **Played 2026-09-26: the quantum tunneller's 2 m cube is the right middle tier —
*"I like the 2m cube"*.**

**Worlds.** His played worlds are under `app_userdata/Overburden/worlds/`; the `MinerCraft/`
folder beside it is the pre-rename backup and his to delete. **Three are retired measurement
worlds — open them only as copies with `--world=`**: `retired-flooded-2026-09-19` (16 pumps,
21.1 ML of Tier B), `retired-spread-2026-09-22` (**the load test** — 209 machines, 66.6 ML,
3.4 M cells active at open), `retired-pool-2026-09-22` (the enclosure at scale and M10d's *dry*
fixture — 127 machines, 7.09 ML). His demolished tower is `Regions16-pondlook-demolished-2026-09-26`
(the collapse's measurement world, a copy only) with its log at `logs/demolition-2026-09-26.log`.

**The rename.** The game is Overburden; stages 1–3 (solution, namespaces, content namespace, mod
folder, `user://` folder) are done. **Stage 4 is Matthew's**: the GitHub repo, the local folder,
the remote URL, and the assistant's memory directory, which is still keyed `MinerCraft`.

**Things not to rediscover** (the standing ones; each session's own are in STATUS-HISTORY):

- **A hook on an arrival path runs mid-tick** and may ask the automaton nothing but
  `MayHoldWater`; a hook that throws inside `Complete` eats the neighbour push with `faulted 0`,
  a silent stall.
- **A hook on the one write path sees every writer** — the undo's own inverse, the collapse's
  rubble, `MachineSet.Restore` at open. Enumerate the writers from the code.
- **Every recorded water oracle runs `--fluidnear=0`**, where the carry is empty; measure the
  solve on a copy of a played world with `--fluidnear=2 --fluidjob=1`.
- **`arr[Call()]` reads the array before the call** — a fixture must cross a scratch array's
  growth boundary (256) at least once.
- **In an open fixture the tick's wake cascade repairs an unwoken seam from its ends** — close the
  corridor and put the water down asleep, or a missing wake reads as correct.
- **A debug door into a conserved quantity is a term**, or the identity lies from then on.
- **Alt+G takes water back** as well as Alt+R, because NVIDIA's overlay owns Alt+R on his machine.
- The release export with SQLitePCLRaw's natives is still unverified.

The licence is [PolyForm Noncommercial 1.0.0](LICENSE.md), source-available. It can be loosened to
MIT any time; it cannot be tightened after the first outside PR is merged.
