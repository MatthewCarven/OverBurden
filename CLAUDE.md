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
| [docs/COLLAPSE-NOTES.md](docs/COLLAPSE-NOTES.md), [docs/COLLAPSE-PLAN.md](docs/COLLAPSE-PLAN.md) | **The slow-then-fast collapse, "Tetris gone bad"** (2026-09-26). **Parts a–c and e built and played; pieces are the default (2026-09-27)**, drawn falling with the cyan glitch, the phonk and a 10–20 s fuse. Open: d (the chimney), f (the sensor), decision 11. Read the notes' "What exists", then the plan's "c-2 — built", "Part c — played" and "Part e — built". |
| [docs/M10-NOTES.md](docs/M10-NOTES.md), [docs/M10-PLAN.md](docs/M10-PLAN.md) | The quantum machine family and M10's first plan. Read before planning any machine. |
| [docs/M10-LADDER-PLAN.md](docs/M10-LADDER-PLAN.md) | **The ladder** — hand → steam → DC → AC → quantum and the crafting under it; twenty-three decisions, parts M10a–M10j. **M10a–M10c built; M10d is the play (his) and d-1/d-3 built; M10e (the conductor index) is next after the play.** The lithium-ion pack (14.4 MJ, 2 kW) shipped early, 2026-10-12, through `P`. Its one idea: connection is membership, movement a ledger transfer at the boundary. |
| [docs/UNDO-PLAN.md](docs/UNDO-PLAN.md) | Ctrl+Z, three steps. **Built and played 2026-09-22.** A fall clears the stack; the journal hangs off `SetVoxel` and `MachineSet`; an undo must not journal its own writes. |
| [docs/MAP-NOTES.md](docs/MAP-NOTES.md) | F12, the map. **Built 2026-09-23 (slices a–d), played: 2 km stays.** A picture must not depend on the viewer; the ramp is fitted to the view; the map aliases past ~16 m a pixel. |
| [docs/GLASS-PLAN.md](docs/GLASS-PLAN.md) | Glass. **Built 2026-09-25 on main; the look played 2026-09-26** (black, 0.20 — *"Glass looks GOOD!"*). The sky column asks `IsSolid`, so glass is a roof to rain and a true skylight. Open: a floor under glass gleams in a storm; water behind glass sort order. |
| [docs/POND-PLAN.md](docs/POND-PLAN.md) | The raised watershed and his water grid. **Built on the branch `water-column-test` and merged into main 2026-10-09 on his verdict**: walls you place raise a lake's rim, and the lake reaches under what you build. Its seven "better" criteria and how each stood at the merge are at the end. |
| [docs/TNT-NOTES.md](docs/TNT-NOTES.md) | Blasting. Notes, not scheduled; his design folded in (blasted voxels go nowhere; the shake). Six decisions still his. |
| [docs/ADAPTIVE-RADIUS-PLAN.md](docs/ADAPTIVE-RADIUS-PLAN.md) | The adaptive view radius. **Built 2026-09-06**; the plan's dwell rule was wrong, the built section says why. Far mode (F10 since 2026-10-12; F11 before, now fullscreen) raises its ceiling. |
| [docs/CHUNK-CACHE-PLAN.md](docs/CHUNK-CACHE-PLAN.md) | The retained-chunk cache. **Built.** Read slice 2's section before touching `LightPropagator`. |
| [docs/MATERIALS-NOTES.md](docs/MATERIALS-NOTES.md) | Elements, and why not "just implement chemistry": geology that reacts to water, machine recipes as the chemistry set. |
| [docs/WORLDGEN-NOTES.md](docs/WORLDGEN-NOTES.md) | Ores, layers, trees, permeability — his thinking-ahead. Not scheduled; its three questions are his. |
| [docs/WATER-BODIES-NOTES.md](docs/WATER-BODIES-NOTES.md), [docs/WATER-BODIES-PLAN.md](docs/WATER-BODIES-PLAN.md) | Resting water as one number over a void. **The pass is written and taken as recommended (2026-09-28)**: his TajMahal pool is one component holding 99.6% of the water, and it never comes to rest, so part a (rest) comes before any body. **Part a is parked (2026-09-29)**: the pool rests with the near/far order off, but every cheap fix under the order failed the load test; a carry with O(1) removal is the open piece. `--bodyprobe=` and `--rest` measure it. |
| [docs/LAKE-EDIT-NOTES.md](docs/LAKE-EDIT-NOTES.md) | **Building in a lake** (2026-09-30): his words verbatim on displacement, a wall breaking a lake's sheet, and a sooner Tier B → A hand-back, with what the code offers and four questions. Today a block in a lake is a pier. |
| [docs/LAKE-EDIT-PLAN.md](docs/LAKE-EDIT-PLAN.md) | **Walls that split a lake, the code-level pass (2026-10-01): all seven decisions taken as recommended; L-1, L-2 and E-1 built the same day; his play passed 2026-10-09, and all of it is on main since the pond branch merged.** His Tier B conversion can't separate (the hand-back rejoins the one lake); the pond branch's raise divides a lake but gives every litre to the old pit's side (fact 10). Recommended: share by the water over each cell (L-1, L-2 on the branch), and make the enclosure's answer independent of who asks first (E-1, main). |
| [docs/ALPHA-PLAN.md](docs/ALPHA-PLAN.md) | **The public alpha** (2026-10-12, his ask: a public zip, extract and run). Twenty-one facts from three read-only searches (there is no export; content is found relative to the working directory; no menu, no quit key, no help; the phonk can't ship; personal data if the repo goes public) and **twenty-three decisions, his**, split into four sessions: the export, the first ten minutes, the hotbar's slots, going public. **A and B built; B is 0.1.2-alpha (2026-10-19).** |
| [docs/IDEAL-MACHINES-BRIEF.md](docs/IDEAL-MACHINES-BRIEF.md), [docs/IDEAL-MACHINES-PLAN.md](docs/IDEAL-MACHINES-PLAN.md) | **The ideal machines (2026-09-29).** The brief holds his words and **nine taken decisions** verbatim (one pool, bankruptcy a feature; quarries; materials; water in the inventory; the quarry only where you are). **The plan is the panel's result: sixty-one decisions, all taken as recommended (2026-09-29); IM-0b through IM-4c done (2026-09-30; 4c kept today's lake rule at his word); IM-5's sixteen decisions taken (2026-09-30); IM-5a built (2026-10-10, after a re-read against the merged watershed), IM-5b built (2026-10-11: the pile, a cone at his word, its bench `--pile`), IM-6 next; TODO's "Order and collisions" says what comes first.** Read its "Eight facts" first, especially fact 3 (the undo can mint today) and fact 7 (a sensor can starve a vault). **The panel's papers are kept in [docs/panel/ideal-machines-2026-09-29/](docs/panel/ideal-machines-2026-09-29/)**: the reader maps are file-by-file maps of the machine code (as at `71e6eed`), the place to start an IM slice. |

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
`start.cmd`. The Debug build it launches is **JIT-optimised** (`Directory.Build.props`). **But
until 2026-09-28 only the Game project was.** `Overburden.Core` and `Overburden.Persistence`, built
as the Game's references by `dotnet build Overburden.Game`, got an empty Configuration at props
time and were built unoptimised. A `dotnet test` or solution build in between optimised them, so
**which one a play or headless figure ran against depended on the last build command.** On his
quarry the fix made the water tick 5.5× faster (see the worklog). Check a DLL with its
`DebuggableAttribute`: 2 is optimised, 263 is not.

The alpha zip (needs the 4.7.1 mono export templates; close the game first). It prints the zip's size,
SHA-256 and commit, refuses a zip missing its readme, quick start, licence or notices, and writes a version-free
copy, `build/Overburden-alpha-win64.zip`, for the upload. **Bump `<Version>` in `Overburden.Game.csproj` before
every zip that goes out**:

```bash
pwsh tools/make-alpha.ps1
```

Benches are **Release only** — Debug is 3–10× slower and reads like a real number:

```bash
dotnet run --project Overburden.Bench -c Release -- --light
```

The arms (`--light`, `--blob`, `--store`, `--watershed`, `--portal`, `--seep`, `--surface`,
`--seepawake`, `--support`, `--solve`, `--fluid`, `--spill`, `--lake`, `--evict`, `--terrain`,
`--front`, `--pump`, `--pile`) and their sweep flags are in COMMANDS.md. **`--support` wants
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
opened the project, run once with `--headless --import`. **Godot 4.7.1-stable mono is vendored here**, in
`Godot_v4.7.1-stable_mono_win64/` (gitignored, there since 2026-08-25), and `start.cmd` and
`tools/make-alpha.ps1` use it first; the recipes in this file name the shared copy in `../GAME/`, the same
version. A worktree has neither beside it, so give the script `-Godot <exe>` there.

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

**2026-10-19 — alpha session B is built and on main, zipped as 0.1.2-alpha.** It was built on 2026-10-13 in
the worktree `alpha-b` and never committed, while main's hand-over still said "next: B". This session found it,
read it whole, fixed a missing space, closed a test hole its battery's recipes found (9 of 9 red), and merged it
(`57ece15`). **0.1.2-alpha** (`0d52f6f`): `build/Overburden-0.1.2-alpha-win64.zip` and the version-free copy ran
clean unpacked, the card showing at a new world's first open and not on its reopen. **His look, then his
upload.** His untested 2026-10-12 builds and 0.1.2's check are at the top of his list, queued at his word.
- **What B is** ([docs/ALPHA-PLAN.md](docs/ALPHA-PLAN.md), "Session B — built"): Esc's menu, which doesn't
  pause (`Ui/EscMenu.cs`); F1's card (`Ui/ControlsCard.cs`, kept in step with QUICKSTART by hand); `--dev` for
  Ctrl+R, H and Alt+H (`start.cmd` passes it, the export doesn't); a message line (`Hud.ShowMessage`); the swap
  that asks (`PackSwapGuard`). `--openmenu` and `--openmenu=controls` open them for `screenshot.gd`.

Suite **2,305**, r6 to the digit. Main is ahead of GitHub; he pushes.

**Next session, in order:**
1. **Alpha session C, the hotbar's slots** (ALPHA-PLAN 21: nine slots that stay put, saved with the world, new
   things into the first empty slot, a key to pin what V selected; M10-LADDER-PLAN's inventory decisions 2, 3
   and 7 for the Core half). It takes a header version, so IM-6's moves up one. Its code-level pass first.
2. Then **D** (the repo's README rewrite, CONTRIBUTING with a contributor rights clause, the docs read, his
   desktop's specs for the readme).
3. **IM-6 waits behind the alpha.** Its code-level pass carries: the vault's panel (his warranty line, a lock
   on the channel box, power auto-scaled), water in the hotbar through `VolumeText`, the pile's cyan glitch
   (decision 16), and the windowed read of a real dump. Before it: the ten build warnings, ideally collapse f-1.

The ideal machines plan: all sixty-one decisions were taken on 2026-09-29. **Built:** IM-0b, IM-1 to IM-3b,
IM-4a (thirty-six mutations red), IM-4b, IM-4c (reduced at his word), **IM-5a, the dump's water** (2026-10-10,
twenty-eight red) and **IM-5b, the pile** (2026-10-11, forty-two red; its seven build choices taken
2026-10-12). **Taken and not built:** IM-6 onward. Suite **2,302**.

**The lake plan is taken (2026-10-01): [docs/LAKE-EDIT-PLAN.md](docs/LAKE-EDIT-PLAN.md), all seven decisions
as recommended; his last question closed 2026-10-12** (the walled area beneath TajMahal2 was two areas he
levelled to watch the rain, no failure). His means,
converting Tier A to Tier B where voxels are placed, can't keep the sides apart. The pond branch's raise
already divides a lake at a wall, and **L-1 and L-2 (built) make each side keep the water that stood over it,
with `--dam=` to show it headless**. A wall's own strip of water goes to the side its cells drain to, often most
of it to one side, so that side can rise up to twice decision 3's 14 cm (corrected in the plan). **E-1 (built,
on both branches) makes a walled area's answer the area's.** **His play passed (2026-10-09)**: R into one side
of a split lake raised only that side, and on his word the pond branch is merged.

His test world is **`LakeTest2`** (made 2026-10-11; its world file reads seed `0xD8FF0AD`, not the `0x3794C6C5` once recorded), and he pointed `start.cmd` at it himself.

**The plan to build: [docs/IDEAL-MACHINES-PLAN.md](docs/IDEAL-MACHINES-PLAN.md).** The shape: a channel is an
account (one bank, one store); the quantum backpack is a window that holds nothing; a vault bills for what it
holds, and an unpaid bill fuses it (10–20 s, cyan) and dumps it; a storm is when vaults let go; the quarry is a
machine with a shared budget, only where you are; water is an inventory item through the siphon; creative is a
header byte and a kit. Each slice has a "code-level pass" and a "built" section in the plan; the previous
hand-over, slice by slice, is in [docs/STATUS-HISTORY.md](docs/STATUS-HISTORY.md).

**From TODO's "Order and collisions"**, at the top of TODO: before IM-6, the build warnings; before IM-7, the
engine-wide pin (TODO's Levers), and the quarry's pass reads the `--pile` bench (its cost is chunks touched, not
the field's clone).

**His open threads:**
- far mode's six rungs (F10 now; in TODO, the caps checked first).

**How the slices have gone:** every one has had a code-level pass first. Each pass used three read-only
searches in parallel, facts with file and line, the load-bearing ones checked by hand, then numbered decisions
for him. IM-3b, IM-4c and IM-5 each changed the plan at that pass, and IM-5b's re-read found decision 11
unbuildable as written. And **read his list's notes before building on an "as recommended" from the same
sitting**: at IM-4c his notes reversed the choice. **Plan the mutation battery before running it**: at IM-5b,
writing the recipes found six holes, and a recipe that survives can be one a fallback route hides.

IM-6's panel owes his warranty line, a lock on the channel box, and power auto-scaled on screen.

**Buildable alongside: collapse part f, slice f-1** (Core and tests), thirteen decisions taken
([docs/COLLAPSE-PLAN.md](docs/COLLAPSE-PLAN.md), "Part f"). The ideal-machines plan's three amendments there are
taken too; none changes f-1. **IM-1 landed first** (its pass's decision 1, taken), so f-1 gives its two
readers `"extras": { "sense": {} }` beside `onsignal` and switches `Sense` on in `MachineExtras.Served`.
**Build in a worktree if he is playing or may start**, because `start.cmd` builds whatever the main folder
holds, half-edited or not. On 2026-09-29 he launched mid-IM-1 and his build caught the tree before the loader
change, harmlessly; a tree that compiled halfway through a change would have put half of it in his hands.

**TajMahal is retired** (2026-09-29, his word: *"I borked the supports so want to restart"*): moved to
`retired-TajMahal-2026-09-29`. **TajMahal2 is retired too** (2026-10-01, his word: *"you can delete or retire
tajmahal2 as you please"*), as `retired-TajMahal2-2026-10-01`. **The first `LakeTest` is retired too**
(2026-10-11, his word, *"sounds good to me"*, for playtesting fresh): moved to `retired-LakeTest-2026-10-11`
(made 2026-10-01, seed `0x2C15F365`). **His fresh test world is `LakeTest2`** (seed `0xD8FF0AD`); he
pointed `start.cmd` at it himself the same night. A world's name is only its folder under `worlds/`, so a
rename is a folder move with the game closed.

- **Water bodies part a is parked** ([docs/WATER-BODIES-PLAN.md](docs/WATER-BODIES-PLAN.md), "c-3,
  run"). His pool rests with the near/far order off (2% awake at 900 s), but every cheap fix under the
  order failed the load test. c-2 (promote a levelled sheet's far front) closes two thirds of the gap
  and needs a far carry with O(1) removal, a planned piece of its own. Bodies wait on it. The ranking
  measure is the five-minute queued mean, not one probe.
- **The pond branch is merged (2026-10-09).** On `LakeTest`, three runs each, alternated, it matched main: the
  same frame times and water rate, about 9 ms a second of its own work, the same total water (WORKLOG). At his
  old pond it measured 1.9× main, only because it keeps the water main spilled
  ([docs/POND-PLAN.md](docs/POND-PLAN.md), "Re-measured").
- **Far mode has a third rung**, r10h/6v; he played it to the top. Its key is F10 since 2026-10-12 (F11 is
  fullscreen, ALPHA-PLAN decision 17).
- **Where a probe must stand:** the old TajMahal's pool `--at=-268,-5,1284` (a copy of
  `retired-TajMahal-2026-09-29`, never `TajMahal2`), his pond on `Regions16` `--at=-852,34,282`. A saved
  spawn loads the wrong water; read the first `M8b:` line's `loaded`.
- **His worlds:** the old TajMahal is `retired-TajMahal-2026-09-29`, with `backup-TajMahal-2026-09-28` beside
  it. His tower restores from `backup-Regions16-pondlook-2026-09-26` on "restore my tower"; the collapsed
  copies are kept, dated.

`start2.cmd`–`start5.cmd` and `pondlook.cmd` (his tower) are Matthew's, recycled as he tests, and
deliberately untracked.

**The collapse** ([docs/COLLAPSE-PLAN.md](docs/COLLAPSE-PLAN.md)): parts a–c and e built and played.
- Tetris pieces are the default; every M9 oracle recorded before 2026-09-27 wants
  `--collapsemode=sweep`.
- Also built: the cyan glitch (*"like it"*), black pieces kept on his word, the phonk from `media/`
  (gitignored; his ear, 2026-09-30: *"sounds good an want to leave it as is"*), and the 10–20 s fuse
  leaning with the deficit.
- Open: part d (the chimney); **part f (the sensor): thirteen decisions taken 2026-09-29, f-1 next**; decision 11 (lifts off a reservation, up to 4.5 m) on his "fix it".
- **The keystone oracle is a race**: read `at risk 0` and lifted == returned, not its falls.

**Waiting on him** (all in MATTHEW-TODO): the fed-advance
load test (`start3` against `start3 --fedadvance=0`); the M10d play queue (3 and 4 answered 2026-09-30) and
the older checks; glass's water sort order; TNT's six decisions. **Played 2026-09-26: the quantum tunneller's 2 m cube is the right middle tier —
*"I like the 2m cube"*.**

**Worlds.** His played worlds are under `app_userdata/Overburden/worlds/`; the `MinerCraft/`
folder beside it is the pre-rename backup and his to delete. **Five are retired measurement
worlds — open them only as copies with `--world=`**: `retired-flooded-2026-09-19` (16 pumps,
21.1 ML of Tier B), `retired-spread-2026-09-22` (**the load test** — 209 machines, 66.6 ML,
3.4 M cells active at open), `retired-pool-2026-09-22` (the enclosure at scale and M10d's *dry*
fixture — 127 machines, 7.09 ML), `retired-TajMahal-2026-09-29` (his quarry and pool, the water
bodies' measurement world and IM-0's; its supports borked in play), and `retired-LakeTest-2026-10-11`
(the lake splits' world: L-1, L-2, E-1 and the pond branch's A/B were measured on copies of it, his 4.2 ML
lake 33 m from his spawn; his glass rain pool at about x −12, z −63). His demolished tower is `Regions16-pondlook-demolished-2026-09-26`
(the collapse's measurement world, a copy only) with its log at `logs/demolition-2026-09-26.log`.

**The rename.** The game is Overburden; stages 1–3 (solution, namespaces, content namespace, mod
folder, `user://` folder) are done. **Stage 4 is Matthew's**: the GitHub repo, the local folder,
the remote URL, and the assistant's memory directory, which is still keyed `MinerCraft`. **The GitHub repo is
done: renamed `MatthewCarven/OverBurden` and public** (seen 2026-10-12; GitHub redirects the old name). The
local remote still names `MinerCraft` until he runs `git remote set-url origin
https://github.com/MatthewCarven/OverBurden.git`; the folder and the memory key are still to go.

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
- **Check `git worktree list` at a session's start.** Alpha session B sat built and uncommitted in
  `.claude/worktrees/alpha-b` for six days while the hand-over said it was next (found 2026-10-19).
- **Alt+G takes water back** as well as Alt+R, because NVIDIA's overlay owns Alt+R on his machine.
- **The export** (2026-10-12): `pwsh tools/make-alpha.ps1` builds the alpha zip in `build/`. Godot's export
  wants a classic `.sln`, kept in `tools/godot-sln/` (one beside the game's `.csproj` breaks `start.cmd`). An
  exported build reads `mods/` and `media/` beside its exe, saves to `%APPDATA%\Overburden Alpha\`, and applies
  `start.cmd`'s four settings itself; the editor binary is unchanged. **`OS.Alert` shows a box even headless**,
  so the start-failure box is gated off headless. SQLite works exported. **The Godot editor strips
  `project.godot`'s comments whenever it saves** (it did when he fetched the templates), so the file is kept
  in the editor's own form and its settings are explained here: the `.template` user-dir pair is the export's
  own folder (ALPHA-PLAN 9), `dotnet/project/solution_directory` is the export's `.sln`, and nearest-mipmap
  filtering is blocky by intent. Opening the editor also writes `.uid` files beside scripts; they stay
  untracked.

The licence is [PolyForm Noncommercial 1.0.0](LICENSE.md), source-available. It can be loosened to
MIT any time; it cannot be tightened after the first outside PR is merged.
