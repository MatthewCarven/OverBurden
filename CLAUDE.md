# Overburden — orientation

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
| [docs/M2-PLAN.md](docs/M2-PLAN.md) | M2, split into M2a–M2d with per-part design and tests. Done, kept for the reasoning. |
| [docs/M3-PLAN.md](docs/M3-PLAN.md) | M3, mining, split into M3a–M3d. Done, kept for the reasoning. |
| [docs/M4-PLAN.md](docs/M4-PLAN.md) | M4, the player, split into M4a–M4d. Done, kept for the reasoning. |
| [docs/M5-PLAN.md](docs/M5-PLAN.md) | M5, lighting and AO, split into M5a–M5d. Done 2026-08-31, kept for the reasoning. |
| [docs/M6-PLAN.md](docs/M6-PLAN.md) | M6, persistence, placement and inventory, split into M6a–M6d. Done and played 2026-09-01, kept for the reasoning. |
| [docs/M7-PLAN.md](docs/M7-PLAN.md) | M7, rain, split into M7a–M7d. Planned 2026-09-01, **built and played 2026-09-02**. Kept for the reasoning; **read SCALE.md before it**. Two places it was overtaken: splashes were cut (M7c-2) and the occlusion field's cost estimate was ten times out (M7d-1). |
| [docs/M7-PLAY.md](docs/M7-PLAY.md) | M7's play: five questions, **all five answered 2026-09-02**, the flags that provoke weather without waiting for it, and how to read the `M7c:`/`M7d:` log lines. Kept for the verdicts. |
| [docs/M10-NOTES.md](docs/M10-NOTES.md) | **The quantum machine family** — Matthew’s proposal, 2026-09-02: a fluid pump that teleports to a wireless output, and a general machine programmer. Notes rather than a plan, on the deferred-collapse precedent. **Read before planning M10, and before shipping any machine — it names the entity-storage change that header v4 rides on.** |
| [docs/MATERIALS-NOTES.md](docs/MATERIALS-NOTES.md) | **Elements, and whether to "just implement chemistry"** — a brainstorm with Matthew, 2026-09-03. Notes rather than a plan. The recommendation is *geology that reacts to water, with machine recipes as the chemistry set*; the element table says why each material would exist. **Read before M8c reads `permeability` and before M9 reads `supportDistance` — both have been in the material JSON unread since M1, and the trap is a constant shipping green.** |
| [docs/M8-PLAN.md](docs/M8-PLAN.md) | M8, water gets in, split M8a–M8d. **Planned 2026-09-02; M8a built the same day; M8b split four ways and M8b-1 built 2026-09-03; M8b-2 planned 2026-09-05 and all three parts built the same day, both open decisions taken as recommended — water crosses the seam in the game; M8b-3 planned and built the same day: the hand-off (3a) measured, the settle (3b) built and priced against the chamber, both mines and the played world, the spill's look deferred to M8c and the settle's interval an estimate with `--settleafter=` as the sweep.** Tier B’s cell size is settled at 0.25 m and SCALE’s open 64x question is answered in it. **Its M8b section records a gap the plan had**: a flooded mine has no Tier A body, so resting mine water is stored, and the settle is a solve. Read the tick-budget item in TODO before M8b-2. |
| [docs/ADAPTIVE-RADIUS-PLAN.md](docs/ADAPTIVE-RADIUS-PLAN.md) | **The adaptive view radius**, Matthew's proposal from the sprint corridor, planned and **built 2026-09-06**: fall to the 27-chunk floor while moving with a derive backlog, climb back a ring at a time while the derive is idle enough for one more. **Its "Built" section records that the plan's grow rule — a dwell — was wrong, and the 7 m/s corridor said so on its first run**; the rule is the light step's share of its budget now. Read the corridor worklog entry before it: the floor is a cliff edge, not a slope. |
| [docs/WATER-THREAD-PLAN.md](docs/WATER-THREAD-PLAN.md) | **The water thread**, Matthew's question of 2026-09-07. **Slice 1 — the near/far order — was planned and built 2026-09-09** and its planning pass corrected the slice's own mechanism twice, which is why that pass is worth reading before the code; slice 2 is **priced and parked**, slice 3 unstarted. **Placed after M8b-4c and before M8c.** Read before touching the tick's order or its home. |
| [docs/CHUNK-CACHE-PLAN.md](docs/CHUNK-CACHE-PLAN.md) | **The retained-chunk cache**, planned 2026-09-03, **slice 1 built the same day and slice 2 on 2026-09-04**: chunks leaving the box keep their voxels, column and derived light in a byte-capped cache, so a turnaround costs no load, no generate and no derive. **Both "built" sections record where the build departed from the plan** — a column rule the plan did not have, a column push that became a check at revival, and the three older light defects slice 2's walk found. Read the slice 2 section before touching `LightPropagator`. |
| [docs/SCALE.md](docs/SCALE.md) | **How many voxels and cells exist at each view distance, what one full sweep costs, and what each system actually iterates.** Read before designing anything that wants to check the world periodically — M7, M8 and M9 all start here. |
| [docs/adr/](docs/adr/) | ADR-0001: why we build our own voxel engine rather than adopt godot_voxel, **with named revisit triggers** |
| [TODO.md](TODO.md) | Milestones as checkable tasks, and the decisions still open. **M6's section is the plan as a checklist.** |
| [WORKLOG.md](WORKLOG.md) | What happened and why, newest first. Findings worth keeping live here. |

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
- **Light and AO are in the mesher's merge key** even though both are constant until M5.
  Removing them "because they're unused" means rewriting the sweep later.

## Commands

Build and test:

```bash
dotnet test Overburden.slnx
```

Run the game:

```bash
start.cmd
```

**`start.cmd` passes `-- --lightbudget=16 --adaptive=1` itself** — the first since 2026-09-03,
Matthew's "lighting switch", and the second since 2026-09-06, the adaptive view radius. (It carried
`--spread=counted` from 2026-09-07 to 2026-09-09 for the counted divisor's play; the A/B read the same
under both rules and the flag came out — pass it yourself to see that arm.) It launches the Debug build, where a derive is ~1.4 ms and the 4 ms default gives two or
three a frame, which is what lit a mined-out room over tens of seconds. Anything you give
`start.cmd` lands after that bare `--` as a game flag, and a later `--lightbudget=` wins. **The
pipeline default stays 4 ms**, because every recorded measurement was taken at it and a profile
run does not go through `start.cmd`.

Measure worldgen — **Release, always; Debug is ~3× slower and reads like a real number**:

```bash
dotnet run --project Overburden.Bench -c Release
```

Measure **lighting** on the same harness. Also Release-only, and here the Debug penalty is worse
— **5.9× on the derive, ~10× on the walk** — so a Debug figure is not a measurement at all:

```bash
dotnet run --project Overburden.Bench -c Release -- --light
```

Measure **chunk serialisation**, which is what settled whether `Compact()` runs on save — **and,
since M8b-4b, the fluid panel blob in both its forms on three shapes**, which is what put the
writer's sparse/dense threshold at 1,024 cells. Release only, same as the others:

```bash
dotnet run --project Overburden.Bench -c Release -- --blob
```

Measure the **SQLite read path** against the generation it replaces — the number M6's crossover
worry turned on:

```bash
dotnet run --project Overburden.Bench -c Release -- --store
```

It reports write, read, raw and stored bytes across six arms — generated, carved and *stripped*
chunks, each with and without `Compact`. **The stripped arm exists because the carved one cannot
benefit**: carving air into a chunk that already holds air leaves the palette untouched, so
measuring only that prices `Compact` against a fixture with nothing to gain.

It loads `mods/overburden/` through the real loader, because conductance is a function of which
materials are opaque and a fixture would measure a made-up world.

Measure the **M7 watershed** — the number that decided whether an incremental watershed ever has to
be built. Release only, and it sweeps window sizes at the 4 m cell:

```bash
dotnet run --project Overburden.Bench -c Release -- --watershed
```

**Sampling, flooding and the merge tree are timed apart**, because M7-PLAN's estimate assumed
sampling dominated and it does not — it is about an eighth of the cost. **It warms up by call count
rather than by one pass**, because one warm-up pass leaves a small window in tier-0 code while a
large one is promoted mid-flood by OSR, which reported the 1 km window at 47 ms instead of 20 and
looked exactly like a memory-locality finding.

Measure **M8b-2b's portal index over a chunk bite** — the seed surface as the array reads
`SkyColumn` holds against the analytic function, which is the number that decided where the seed
heights live (1.2 ms against 10.3 ms a bite, Release, 2026-09-05). Release only:

```bash
dotnet run --project Overburden.Bench -c Release -- --portal
```

Measure **M8's Tier B automaton** — a lake draining into a mine, swept by breach width. Release
only, and **the fixture bakes its terrain into a bitset before timing anything**, because the first
version answered `CanHold` with `IsCaveAt` and reported 3.86 µs a visit against a true 0.2–0.3:

```bash
dotnet run --project Overburden.Bench -c Release -- --fluid
```

`--fluidbores 1,3,5,9` is the sweep (breach width in voxels), `--fluidspan`/`--fluiddepth` size the
mine, `--fluidlitres` the lake, `--fluidticks` the flow window and `--fluidsettle` the tick budget
for the settle afterwards — **space-separated, not `=`, unlike the game's flags.** **`--fluidshape
chamber --fluidchambers <n>` is the other fixture** (M8b-1): a block of bitten 8 m chunks with a
shaft into its roof, and a bore of 32 or more opens the whole roof, which is a chunk bitten out from
under a lake. **Quote the flow window with any L/tick**: the rate decays as the mine backs up, and
the M8a-2 table was a shorter window than the default — it does not reproduce, and the committed
code's figures (WORKLOG, M8b-1) are the baseline. **The settle cap is a budget, not a correctness statement** — `FluidRules`
carries a termination proof, so hitting it means levelling is diffusive rather than that it
oscillates, and the row says how many cells were still awake.

**Read the machine-control line before quoting anything from it.** It times worldgen over 256
chunks and prints the result against the 0.47 ms/chunk recorded at M2d-2 — **this machine has been
measured at 2.5x that, hours apart, with identical binaries**, which is enough to move a figure
across a decision threshold. Divide by the stated factor before comparing with any number in the
worklog.

Measure against a **played** world without touching it — the recipe that retired M6's load-path
concern on 2026-09-03. Copy `worlds/<name>/` to `worlds/probe-load/`, copy it again to
`worlds/probe-empty/` and `delete from chunks; delete from columns` there (same header, position,
clock and basins — nothing to load), then run both with `--profile --world=probe-load` /
`--world=probe-empty` and delete both afterwards. **Stand where the player stood, and read `light
settled` rather than `first draw`**: inside a mined-out void every mesh is correctly empty, so
`first draw` never fires and the report reads as a stall. The world file is SQLite; count
`chunks` rows and their `length(blob)` (uniform ≈ 52–63 B) before deciding what a run measured.

Verify headlessly — no display needed, catches scene and script errors and prints the
diagnostics:

```bash
../GAME/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path Overburden.Game --headless --quit-after 900
```

**`--quit-after` counts frames, not seconds** — 900 is about 15 s, and the streaming report
only prints every 5 s, so a small number exits before there is anything to see.

Profile a flythrough. The flags go after a bare `--`, as `name=value`, and the profiler
quits by itself when it is done, so no `--quit-after` is needed:

```bash
../GAME/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path Overburden.Game --headless -- --profile --radius=12 --vradius=6 --speed=40
```

Also `--margin=`, `--warmup=`, `--measure=`, `--cavestep=` (1 restores per-voxel cave
evaluation, for A/B against the lattice), `--applybudget=<ms>` (a large value such as 1000
is the unbudgeted arm; there is no "off"), `--lightbudget=<ms>` (how much of a frame light
derivation and propagation may take; **swept at M5c-1 and 4 ms is the knee** — below it the
world has generated but cannot draw, because nothing meshes until its neighbourhood is lit),
`--ao=0` and `--light=0` (the two halves of the shade, switchable independently — see the quad
table below), `--skybrightness=<0..2>` (scales the sky channel alone; 0 leaves only block light —
**the overcast multiplies this rather than replacing it, so the seam survives weather**),
`--debuglamp` (places one lamp where the crosshair points, so a screenshot can show block
light without a keypress) and `--entangle` (the entangled miner, which with `--mine` aims along
the flight path rather than straight down — see below). **`--entanglesphere` restores its
pre-2026-09-03 bite**, the tier's sphere instead of a whole chunk, and is the arm the chunk bite
was priced against; **`--chunkcooldown=<seconds>` sweeps the chunk bite's rate and is deliberately
not profile-only**, because `Q` is a play bind and a rate that only reached a profile run would be
a seam for the one arm that cannot judge it.

M7c-1 adds four more, all seams rather than settings. **`--clock=<seconds>` starts the run
somewhere in this world's forecast** — the weather is a pure function of (seed, time), so this is
how a storm is asked for by name rather than waited for; a named world overrides it, because what
that world remembers is the thing under test. **`--clockscale=<x>` makes world time run faster**,
which is what puts a whole two-minute weather episode inside a fifteen-second profile window; it is
**not** M7d's `rainfallScale`, which leaves the sky honest and speeds the water up, and a run using
it is not comparable with one that does not. **`--weather=<0..1>` forces the sheen and the overcast
without making it rain** — a look override, named for what it does to the screen, because a flag
called `--weather` that silently failed to fill a basin would be the worse instrument. And
**`--yaw=<degrees>` turns the camera at spawn**, which `screenshot.gd` has never been able to do —
a view-dependent artefact is invisible in one picture by definition, so the face-tint A/B needs the
same wall from two angles. `--viewnormal=1` is that A/B's other arm.

M7c-2 adds **`--pitch=<degrees>`**, the vertical half of `--yaw` (negative looks down), because a
shot of rain going into a shaft has to look down it; and **`--norain`**, which leaves the whole of
the weather in place and removes only the particles. That is the arm that priced them, and the
particles are the only part of M7c that costs GPU time.

M7d-1 adds **`--rainscale=<x>`**, which multiplies the depth applied to the world while leaving the
sky honest — **not** `--clockscale`, which speeds the weather up too; **`--waterwindow=<samples>`**,
the sweep seam for the analysis, whose cost is roughly quadratic in the span; and **`--nowater`**,
the control arm that priced the whole system at nothing measurable — **and turns Tier B off with
Tier A from M8b-1.**

M8b-1 adds **`--injectlitres=<n>`**, the debug relay from the command line: a ball of water sized to
hold the litres, put on the ground under the player when the measured window opens (three seconds in
without a profiler). **Sized rather than fixed, because a fixed ball is a hose** — the automaton
falls one cell a tick and spreads diffusively, so a source region drains only at its boundary's
rate, and a run with one measures the hose rather than the flood. In play **`R` puts 10,000 L down
at the crosshair and Alt+R takes back everything within 2 m**; no material, no JSON, no ID. The
profile's `fluid` block prints every term of `held == injected − removed − displaced`, and **the
bare default headless run is not a streaming control any more**: it opens the played world and
stands in its void (198 drawn at 100 quads a chunk). **The r6 control is, verbatim,**

```bash
../GAME/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path Overburden.Game --headless --quit-after 2400 -- --nosave
```

— 2601 / 325 / 3446 at 411 — because 900 frames is ~6 s and the r6 fill settles at ~7. **`--nosave`
goes after the bare `--` or the engine drops it without a word** (Main's warning covers only
`name=value` flags), and the run then opens the played world, which as of M8b-3a floods on open and
reads nothing like a control.

M8b-2c adds the seam's two provocations, which go together. **`--fillbasin=<litres>` is the Tier A
relay**: litres poured into the basin under the player when the measured window opens, reported
as `injected` on the water line for the reason `--injectlitres` is on the fluid line. **`--breach=<voxels>`
bites a square shaft that wide (at least 8 deep) under the player at the same moment**, through the
pipeline's own edit path so the portal tracker and the automaton are told the way a swing tells
them. **If the player's sample drains off the window — a slope, which the spawn usually is — both
go to the nearest basin's pit instead**, and the run says so. The profile's `breach` line prints
portals filed, samples wet and portals covered on the last pass, litres drawn in the window and in
all, and whether Tier A's `drawn` equals Tier B's; both tiers' lines carry `drawn` so each identity
stays exact alone. A breach with no lake over it measures nothing, which is why the pour comes with it.

M8b-3a adds no flag and one bench arm. **`--spill` measures the B → A hand-off**: the relay's ball on
real ground with a 256 m window analysed around it, with and without the hand-off, printing the
active set's decay, ticks to rest, the visits paid and where every litre ended, with both tiers'
identities, the seam's and the whole world's checked. Release only; `--spilllitres a,b` sweeps the
pour and `--spillticks` caps an arm:

```bash
dotnet run --project Overburden.Bench -c Release -- --spill
```

The profile's `handoff` line prints cells rested and on the ground on the last pass, litres handed
in the window and in all, millilitres pending and in how many basins, and whether Tier A's
`received` equals Tier B's `handed`; both tiers' lines carry the term, and the water line's `held`
now includes what Tier B handed over.

M8b-3b adds **`--settleafter=<ticks>`**, the settle's trigger: how many ticks with nothing arriving
— no pour, no draw, no edit under water — before the sheet solve levels what is still awake, and
then at every boundary until nothing is. Two by default, an estimate. **Zero settles under inflow
too**, which is the arm that makes a stream down a tunnel rise as a flat sheet instead of flowing,
kept so both can be seen rather than argued about; a very large value is the control. The profile's
`settle` line prints passes in the window and in all, sheets levelled / packed / left alone, cells
written and woken, the largest sheet and the longest pass, and the periodic `M8b:` line carries the
same. On the bench **`--fluidsolve wait|solve|both`** runs the settle phase with the automaton
alone, with the solve at every boundary, or both — the solve row reruns the flow, which is
deterministic, so the two rows start from the same water:

```bash
dotnet run --project Overburden.Bench -c Release -- --fluid --fluidshape chamber --fluidbores 3 --fluidsolve both
```

The settle's share slice (2026-09-05) adds **`--settleshare=<n>`**: a sheet with a move in it is
re-levelled only when at least one cell in *n* of it is unsettled, or a step stands in it; 8 by
default, an estimate, and **0 is no threshold** — the pre-slice behaviour, which re-levelled a
162,572-cell lake twice a second for its creeping edge, and the control arm. The `settle` line prints
sheets left under the share, sheets levelled for a step, and the cells the passes visited, which is
the number the share bounds. On the bench **`--settleshare <n>`** (space-separated) applies to
`--fluid`'s solve arm, to `--spill`'s settle arm and to **`--lake`, the slice's own arm** — a chamber
block's floor filled level and asleep with eight litres a tick drawn off one edge, the played lake's
shape, priced at no threshold against the share (`--lakechambers`, default 12 for a 147,456-cell
sheet; `--laketicks`, default 60). **It bakes every column the flood reads before timing anything**,
because the fixture bakes a column's cave field on its first `CanHold` and the first pass otherwise
reads as 1,385 ms; and it prints its first five passes by name, because the first pays for growth:

```bash
dotnet run --project Overburden.Bench -c Release -- --lake --settleshare 8
```

M8b-4c (2026-09-07) adds **`--evict`**, the eviction bench: a box walks across a flooded chamber
block and back, evicting the panels it leaves and reading back the ones it returns to, against a
keep-everything control, printing peak panel memory, rows written and their microseconds, and —
the column that matters — whether the automaton rests with everything back, which is the stranded
front an early release would leave. `--evictchambers`, `--evictwidth`, `--evictstep` and
`--evictlayers` (space-separated) size it:

```bash
dotnet run --project Overburden.Bench -c Release -- --evict
```

In the game the `sources` line and the `M6:` line print fluid rows read off the lane with their
lane milliseconds, from memory, and reclaimed; the `M8b:` line carries `evicted` and `evicting`;
and **`M8b: at quit` has a line under it saying what the world file holds**, every row summed after
the write — the next open's `loaded` reaches it once every wet chunk is home, which replaced "at
quit equals restored" when the open stopped reading the rows.

Price **what a Tier B visit pays to ask the world** — the measurement WATER-THREAD-PLAN's slice 2
asks for before any bitset is built. The same flood twice over terrain that answers identically:
the bench's baked bitset against the production `VoxelFluidTerrain` over real chunks in a
dictionary, which is the game's own path. **The oracle is that both arms visit the same cells and
land the same water**, printed, so a run whose arms disagree has timed two floods and says so
instead of reporting a plausible ratio — which is what the first run did, because a chunk
straddling the fixture's bounds defaulted to air. **1.47x Release / 1.59x Debug, which is a third
of a visit rather than a multiple**, so the field does not pay for itself on speed and slice 2 is
slice 3's enabler. `--terrainchambers`, `--terrainlitres` and `--terrainticks` size it:

```bash
dotnet run --project Overburden.Bench -c Release -- --terrain
```

The spread rule bench (2026-09-07) adds **`--spread fixed|counted|both`** to `--fluid`, `--spill`,
`--lake` and its own arm, **`--front`**: the relay's 10,000 L ball on the flat floor of a bitten
block, printing the wet radius, the visible cells and the profile along one axis at checkpoints,
with and without the sheet solve at every boundary, then ticks and visits to rest and whether the
rest is real. **The counted divisor is one part for the cell and one for each side holding less
(M8a's rejected rule, with the double wake it needs); fixed is five always, the default, and the
rule every recorded figure was taken under.** `--frontlitres` and `--frontticks` size it. In the
game **`--spread=counted`** is the play's arm, printed on the `M8b:` setup line; the default is
fixed. The rule is a constructor argument on `FluidSimulation`, not a constant, because the wake
rule depends on it:

```bash
dotnet run --project Overburden.Bench -c Release -- --front --spread both
```

The tick budget slice (2026-09-04) adds **`--fluidbudget=<ms>`**, the fifth budget seam of the same
shape as `--lightbudget`: how much of a frame Tier B's tick may take before it yields and resumes
next frame, 2 ms by default. **Zero is not off** — one granule of 64 visits a frame, so the water
still moves, slowly enough to watch the slicing. The `fluidtick` line prints how many ticks needed
more than one frame and the longest, and **`fluid ms` is per frame now, not per tick** — the number
the budget bounds. On the bench, `--fluidbudget <ms>` (space-separated, like its siblings) runs
each flow tick as back-to-back slices and prints slices per tick and the worst slice, which is
how the roof-open case was priced in frames.

The adaptive view radius (2026-09-06) adds six. **`--adaptive=1` turns it on; off is the default
and the arm every recorded streaming figure was taken in, and `start.cmd` passes it on** the way
it passes the light budget. The box starts at the floor, falls a ring at a time while the viewer
is moving with a derive backlog over `--shrinkslabs=` (2) slabs of the current box, and climbs a
ring at a time while the backlog is empty and the light step's averaged share of its budget is
under `--growbelow=` (0.5); `--cadence=` (2 s) is the least time between changes, the grace after a
grow and the load's time constant; `--minradius=` (1) is the floor. **`--sprintfor=<s>` moves the
flythrough for that many seconds and then stops** — the hanging-around arm, which `--turnaround`
(reverses) and `--speed=0` (never moves) cannot produce. The profile's `radius` line prints shrinks
and grows, seconds at each radius, the backlog and load it read, and the `fill` line gains `grown`,
the first moment the box stood at its ceiling with light settled — **the figure comparable with a
fixed arm's `light settled`; under a growth the other milestones read the floor's box.**

The retained-chunk cache adds two. **`--chunkcache=<MiB>` caps the cache, 64 by default, and 0 is
off — the committed pre-cache behaviour and the control arm.** Not profile-only, because memory is
something a play reports. **`--turnaround=<seconds>` reverses the scripted flythrough every N
seconds**, which is the motion the cache exists for and the one M2d's straight line could never
produce; the profile prints how many turns it made, and a `cache` line beside a `sources` line
(generated / loaded / from the cache / derives), because a hit count is only legible against what
it replaced. **The standing-start baseline is identical in both arms**, to the digit, because
nothing leaves the box; the cache line reads `0 chunks` there and that is a pass, not a silence.
**Since slice 2 the `sources` line also prints how many hits came back with their grid and how many
reseeds stood in for derives**, and the `cache` line how many cached grids were dropped by an edit
at their face or by their column moving — the hits that still cost a derive.

The distance-ordered derive queue adds one flag and one milestone. **`--derivefifo` restores the
FIFO the queue was until 2026-09-03**, the arm "the ground is gone" was seen in, so the order is
measured against it rather than remembered. **The `fill` line now prints `near lit`** — the moment
every chunk within two of the viewer has derived light — because neither `first draw` nor `light
settled` can see the player's own neighbourhood deriving last: inside a mined-out void nothing near
the player has geometry, and `light settled` is the whole box. **Read `near lit` for the order
and `light settled` for the total**; the order moves the first and cannot move the second.

**Building is bound to the right mouse button**, `B` cycles the brush (cube, slab, ramp, sphere),
and **`1`–`9` pick a material from the hotbar** (`V` still cycles to the next one). **`T` cycles
the tool tier** — it was the number row until M6d-3, and the row went to materials because that is
where a hand expects to find what it builds with. `--place` holds the button for a whole
profile window, `--stock=<voxels>` seeds the inventory so such a run measures the brush rather
than the carve, and `--brush=<name>` picks the shape.

**The world file lives at `user://worlds/<name>/world.db`** — on Windows,
`%APPDATA%/Godot/app_userdata/Overburden/worlds/`. `--world=<name>` picks one, `--nosave` opens
none at all, and `--autosave=<seconds>` moves the 30 s flush. **A run that is a measurement or a
screenshot prop does not open the played world**: `--profile`, `--fall` and `--debuglamp` leave
persistence off unless `--world=` names one, because `--mine` would carve up the world somebody
plays and because a run that *loads* what an earlier one saved is not comparable with M2d's
figures. Naming a world is the explicit opt back in, and it is how M6d measures the load path.

**`--speed=0` means a standing start and is distinct from omitting `--speed`**, which defaults a
profile to 40 m/s. That distinction was added at M5d-1, because the standing start is the only
arm in which the chunk set at a fixed radius is deterministic — so AO on against AO off compares
two numbers rather than two samples of a moving camera — and it was the one arm that could not
be asked for.

**A profile run quits by itself, so it needs no `--quit-after` — and passing one usually
truncates the run**, because 900 frames at ~145 fps is 6 s and the default window is 18.

Take a screenshot of the real scene, which is the only way to answer a look question — headless
has no renderer, so a shader error does not even surface there:

```bash
../GAME/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path Overburden.Game --script res://screenshot.gd -- C:/tmp/shot.png
```

It instances `Main.tscn`, waits 8 s for terrain, saves a PNG and quits. Extra arguments after
the path are passed through to the game, so `-- shot.png --ao=0` gives the other arm of an A/B.
**`--settle=<ms>` overrides the 8 s, and the played world needs it**: it takes 14 s to light headless
and longer windowed, so the default shoots a scene that has not drawn yet, which looks exactly
like a renderer defect (2026-09-03).

Profile a **fall** — the vertical motion profile M2d's horizontal flythrough could not produce,
and the one that can arrive somewhere before the world does. `--fall` implies `--profile` and
forces walk mode, so the scripted flythrough stays off:

```bash
../GAME/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path Overburden.Game --headless -- --fall --radius=6 --vradius=3 --terminal=40 --fallheight=250 --measure=20
```

`--terminal=` sets terminal velocity and `--fallheight=` the drop, both so the constant this
profile exists to pick can be swept without a rebuild. **A fall height large enough not to land
inside the window measures nothing at rest** — the ground-flicker detector needs landings, and
the report says when it did not run.

If Godot has never opened the project, run once with `--headless --import` first.

Godot 4.7.1-stable **mono** is not vendored here; `start.cmd` falls back to the shared
copy in `../GAME/`.

## Conventions

- Append findings and decisions to WORKLOG.md; tick and add tasks in TODO.md. Both as you
  go, not at the end.
- Commit with a message that explains *why*, and end with the `Co-Authored-By:` line the harness
  gives the model that wrote it — `Claude Opus 5 <noreply@anthropic.com>` through 2026-09-03,
  `Claude Fable 5.1 <noreply@anthropic.com>` from then. Do not copy the line from an older commit.
- Matthew runs `git init` and `git push` himself — flag when either is needed. Remote is
  `github.com/MatthewCarven/MinerCraft`, **private**, default branch `main`.
- `reference/` is gitignored and holds third-party sources we **read but never build
  against** (currently godot_voxel). Nothing in it belongs in a `.csproj`.

## Where things stand

**The game is Overburden as of 2026-09-03, and stages 1–3 of the rename are done** — the
decision; the solution, projects, folders and namespaces; and the content namespace, the mod
folder, `config/name` and the `user://` folder. **Stage 4 is Matthew's**: the GitHub repo, the
local folder, the remote URL, and the assistant's memory directory (see TODO's name-check item).
**The played worlds now live under `app_userdata/Overburden/`; the `MinerCraft/` folder beside it
is the pre-rename backup and is Matthew's to delete.**

**Where the morning of 2026-09-06 left off. M8b-4a is built — the pause and the arrival — and M8b-4b, the store,
is next and goes alone, because a defect there is permanent data loss.** Read M8-PLAN's "M8b-4 —
the planning pass" and its "M8b-4a — built" section before 4b. A walk no longer deletes awake
water: a visited cell whose chunk is not loaded sleeps in place and is remembered per chunk
(`IFluidTerrain.IsLoaded`, asked only where `CanHold` has already said no), and the chunk's arrival
— the pipeline's one `onVoxelsReady` hook, all three paths, beside the portal tracker — wakes it
through `FluidSimulation.ChunkArrived` with the held cells on either side of the chunk's six faces.
**Measured in the same headless run with the pause taken out: `displaced` 188,866 L → 0 L of a
200,000 L pour, handed to Tier A 11,134 L → 195,981 L** (r4h/2v, 12 m/s, turning every 10 s).
The `M8b:` line prints `paused N cell(s) in M chunk(s), R resumed, F face wake(s)` beside
`displaced`; in play `displaced` at zero across a walk is the oracle, and `paused` should read zero
whenever every chunk is home. **A quit still loses paused water, and that is 4b.** The wire check,
verbatim — the flags go after the bare `--`:

```bash
../GAME/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path Overburden.Game --headless -- --profile --radius=4 --vradius=2 --speed=12 --turnaround=10 --measure=30 --injectlitres=200000
```

**Pick up here (2026-09-10, late). The water thread is three slices in and slice 3 is held on one
decision.** Built and played since the last note: **slice 1** (near water every tick, far water
deferred under a quota — `--fluidnear=2` from `start.cmd`), **the settle's per-chunk wait** (a
portal drawing into one cell no longer postpones every sheet in the world — *"I cannot fault it in
any way"*), **the surface walk** (28.4 → 12.1 ms, and it was never a threading problem — the walk
asked the grid per cell where Y being contiguous makes the cell above the next array index), and
**slice 2** (`FluidField`, the terrain a worker can read — *"Yeah I like it"*). **Slice 3a is
written, working and stashed** (`git stash list` → `slice-3a-wip`), **held because finishing it
knowingly reverses M8b's documented "water may be added, removed or disturbed between slices"** —
14 failures across 6 test names. Read WATER-THREAD-PLAN's "Slices 2 and 3 — the planning pass" and
"Slice 3a" before touching it; the latter is enough to redo the stash from nothing. **Four things
worth not rediscovering**: a sort could not have delivered slice 1, because a tick does not end
until its batch does; **slice 2 does not pay for itself** (the game's read is 1.47x a baked field's,
a third of a visit) and is slice 3's enabler only; **do not cache an answer whose meaning depends on
state the cache does not capture** — `CanHold` returning false means *rock or not loaded*, and
caching it froze the water for a whole play session; and **the roof-open case is battery-gated**, so
the worst load Tier B can see is an action that costs a charge, which is DESIGN's *slower than
realtime is accuracy* with a game rule behind it. The paragraph below is the older state, kept for
the reasoning.

**Earlier (2026-09-09, late). Two things are built and unplayed, and both want the same
session's play.** **The water thread's slice 1** — near water visited every tick, far water deferred
past the boundary under a quota of `near + max(--fluidfar, near)` — is on in `start.cmd` as
`--fluidnear=2`, 0 being the control; and **the settle's wait is per chunk now**, so a portal
drawing into one cell no longer postpones every sheet in the world (0 passes → 79 on one measured
invocation). **The play question is one sentence: pour with `R` and see whether the puddle flattens
instead of lingering**, which is what *"the bulk of the water disperses but the puddles linger"*
was asking for. Four things from building them, all with numbers in WORKLOG:
**a sort could not have delivered slice 1** — a tick does not end until its batch does, so far cells
have to be *deferred past the boundary*; **slice 1 cannot bound a near set that is itself large**,
because its quota scales with that set (the played world's near set was ~19,600 cells and a tick
still took ~57 frames); **slice 2 does not pay for itself** — `--terrain` prices the game's read at
1.47x Release / 1.59x Debug of a baked field's, a third of a visit rather than a multiple, so the
bitset is slice 3's enabler and both wait; and **the settle's per-chunk wait was moved out of the
sheets' merge because the recipe that broke it could not be caught** — the survivor of a merge is
whichever has more cells, so no fixture can put the fed half on the losing side. The paragraph
below is the state before all that, kept for the reasoning.

**Earlier (2026-09-07, late). The counted divisor is built as a setting and benched, the
default is still fixed, and the play check is `--spread=counted` passed to `start.cmd` — stab
`R` on the fresh world and judge the pool's edge; if it reads right, flip the default and let the
water thread (WATER-THREAD-PLAN.md, next to build) absorb the per-tick cost.** Read the
2026-09-07 "counted divisor, benched" worklog entry and the TODO item beside M8a's divisor
decision. Four things from it: **a pool's front is diffusive and a divisor moves it as the square
root** — a fifth more radius at a given tick, not twice; **the front the player sees is the sheet
solve's**, and the counted rule nearly doubles it once the sheet is level (radius 35 against 20
at tick 20 on `--front`), so the previous entry's "`--settleafter=0` does not move the front" was
wrong; **a spill with the hand-off rests in a third of the ticks at two thirds of the CPU, a
flood drains 5–27% faster at 1.6–1.7× the ms/tick**, which in Debug is a 2 Hz tick that holds at
fewer active cells; and **the wake M8a named was two cells too wide** — six neighbours of the
destination reordered a one-wide shaft's column and halved its drain with the rule untouched, the
four sides are the complete set, and only the bench could see it. The fixed arm reproduces the
M8b-1 table to the visit. **Judged 2026-09-09 — *"Well they both look like water?"* — one `R` on
flat grass under each rule on a fresh scratch world, both pours in the basin by tick 20 with the
settle firing, about 8 s against about 9 s by Matthew's count: the default stays fixed and the flag is
out of `start.cmd`; the narrowed trigger is a spill that reads as syrup, or the water thread making
the per-tick cost free.** The paragraphs below are the road there, kept for the reasoning.
**Played the evening of 2026-09-07, read off the log: the rule ran
2,210 ticks on the played world with every identity exact and `paused` 0, `displaced` only where
a chunk fill deleted what it filled — but `R` was never pressed, and the settle ran 0 passes,
because its quiet counter is global and a lake draining through a breach is inflow at every
boundary (TODO under M8b-3b).** So the counted front has been seen without the solve and the
verdict is a provisional *"I think its good?"*; the judgement the trigger names still wants `R` on
the fresh world. Matthew put `--spread=counted` into `start.cmd` himself the same evening, so every
play from here is the counted arm unless a later `--spread=fixed` says otherwise; the code's default
is unchanged. **The scratch-world play of 2026-09-09 (*"the bulk of the water disperses but the
puddles linger"*) gave the settle lever its number**: on a world with a breach a pour lingers 80 s at
`--settleafter=2` and 12 s at `=0`, because a hole in a lake's bank is a treadmill — drawn and handed
climb together at ~7 kL a tick forever — so the quiet counter never fires (TODO under M8b-3b and the
M8b-2c play items). The pour A/B was then made on a wiped `edge` and read the same under both rules. The M8b-4c play check is done — the quit's litres reached the next open's
`loaded` exactly across two sessions. The paragraph below is the
night's state, kept for the reasoning.

**Where the night of 2026-09-07 left off. M8b-4c is built and unplayed — the panel leaves RAM with
its chunk, one tick behind it, and comes back off the serial lane before the chunk meshes; the
play check is the first thing to do, on the fresh world (the chunk-bitten one is retired to
`worlds/retired-chunkbite-2026-09-07/`, Matthew's to delete). The water thread
(WATER-THREAD-PLAN.md) is next to build, its first slice — the near/far order — needing no thread
at all.** Read M8-PLAN's "M8b-4c — built" first. Three things from the build: **to everything
that is water, a chunk arrives when its water is home** — the portal tracker and the automaton
both take `onFluidReady`, which fires once the chunk's row has landed, because the first probe on
the played world had the drain pour into a pending chunk and throw; **the flag surviving the cache
is an identity**, since a revival re-derives it; and the slice's number is 364 panels (22.8 MiB)
→ 81 (5.1 MiB) at the end of a played-world turnaround, 256 rows off the lane in 122 ms, first
draw unchanged. `--evict` is its bench arm. The play oracle changed: `M8b: at quit` has a line
under it saying what the world file holds, which the next open's `loaded` reaches once every wet
chunk is home; `evicted` is on the identity. Two levers named and not built: `FluidGrid.Restore`
cell by cell (p99 frame 33 → 58 ms on the probe), and `viewer ms` at 60–80 ms a move on the played
world with water on in *both* arms, which is older than the slice. The paragraph below is the
evening's state, kept for the reasoning.

**Where the evening of 2026-09-07 left off.** Both play checks are done, and M8b-4c — the panel leaves
RAM with its chunk — is planned and not built: read M8-PLAN's "M8b-4c — the planning pass"
before it, and build it once Matthew has answered its six decisions (all recommended). The water
thread (WATER-THREAD-PLAN.md) follows 4c, its first slice — the near/far order — needing no
thread at all. The pass's two load-bearing findings: the panel lingers one tick after the
forget so the tick itself lists the chunk's queued cells (released at once, a flood front inside
a leaving chunk is stranded on every walk), and a chunk whose row is still to be read is *not
loaded, to the water* — a `FluidPending` flag, the restore as a chunk-slot job on the serial
lane, `ChunkArrived` moved to an `onFluidReady` after it. `evicted` joins the identity, and
`_storedAwake` must forget an evicted chunk or the next autosave deletes the lake. The adaptive radius is played and
accepted — Matthew: *"fly holding ctrl is the only way I can beat the loader/generator now, and
honestly I am happy with that"* — so the 30 m/s sprint outrunning the world is a chosen cliff
edge, not a defect; the logs show r6 from 35 s after an open, `grown` at 14.7–30.6 s, and the
deepest sampled shrink r5. The store is played on the played world: `at quit — 2,216,035 L in
220,333 cell(s)` and `restored 2,216,035 L … 212,621 cell(s)` at the next open, exact. **Two
stalls came out of the logs with numbers, neither reported by the player and neither built
against**: the autosave's sim-thread panel copy is 72–100 ms in Debug and 399 ms on the first
autosave after a restore (the `memcpy` lever, TODO under M8b-4b); and the Tier B surface mesh
rebuilds 144,559 quads in 119.7 ms on the main thread four times a second while a flood runs
(TODO under M8b-4b, wants its own measurement arm). The paragraph below is the morning's
state, kept for the reasoning.

**Where the morning of 2026-09-07 left off.** M8b-4b is built — water survives a quit — and unplayed, as is the
adaptive view radius; the play checks are the first thing to do, M8b-4c (the panel leaves RAM
with its chunk) is next to build, and the water thread (WATER-THREAD-PLAN.md) is planned to follow
4c, its first slice — the near/far order — needing no thread at all. Read M8-PLAN's "M8b-4b — built" before 4c. `FluidPanelBlob` on
the column's frame, a `fluid` table at schema 3 (a schema-2 world opens and gains it), panels
riding the chunk batch as a third list on `IWorldStore.Write`, snapshotted at unview / autosave /
quit and read whole at open into paused panels the arrivals wake; `loaded` is on the `M8b:` line
and in the identity, `M8b: at quit` prints the water at the instant it is written and `M8b:
restored` what the next open put back — **the two should agree to the litre and the cell, and did
(7,144 L / 1,894 awake) in the headless wire check**. `--blob` prices the panel blob: the dense form
is smaller at every shape and the sparse one cheaper only under ~1,000 cells, so the writer
switches at 1,024, an estimate; an all-lake panel would have been 6.4 ms as pairs. Suite 1,073 →
1,134; 26 recipes, 25 caught, one identity. **Unpriced: the sim-thread copy at an autosave on the
played lake's 1,254 panels** (estimate 50–75 ms at a quit, Release), which the play reads off the
autosave line, now naming fluid panels. **The adaptive view radius is also still unplayed** — `start.cmd`
passes `--adaptive=1`, and that play check is the other first thing to do. Fly, hold Ctrl, read
`view rNh/Nv` on the status line fall to r1 with the world still drawn; stop and read it climb a
ring every few seconds; walk and sprint on foot and it should never leave r6. Suite 1,058 → 1,073.
**The plan's grow rule was wrong and its own measurement arm said so on the first run**: "grow
while the viewer's chunk has not changed for two seconds" is *never while walking* — a chunk every
1.1 s at 7 m/s — and the box sat at the floor for the whole corridor, 20 drawn against 344. The
rule is now *grow when the backlog is empty and the light step's averaged share of its budget is
under 0.5*, with a grace of one cadence after any change; half is arithmetic (a ring adds at most
29% to a slab), and the load being a share of the budget means a bigger budget is a bigger box.
**Measured**: 30 m/s draws 25 of 27 at the floor against 0 fixed; 7 m/s ends identical to fixed
with first draw 0.81 s against 6.47; a standing start draws at 1.00 s against 6.65 and is grown at
14.5 s for +69% derives; sprint-and-stop is grown nine seconds after the stop. **Two costs stated
and not paid**: a ring's sweep is 22–29 ms in one frame, five per fill (stage a face per frame, or
seed columns on a worker); and the 0.5 threshold leaves the 0.5–0.78 load band unused at small
budgets. M8b-4b, the store, is still next after the play check.

**The sprint corridor is measured (2026-09-06), and an adaptive view radius is recommended and not
built.** Thirty chunks of on-foot sprint (7 m/s) at r6 is 152 generates, 260 derives, 52 meshes
and a 9.8 ms box sweep per chunk of travel, drawing throughout — nothing to adapt for. At the
flycam's 30 m/s **only the 27-chunk box (r1h/1v) draws, 25 of 27 under `start.cmd`'s 16 ms budget,
and nothing wider draws at any budget** — r3 is as dark as r6, so "halve the radius" buys nothing
and the floor is a cliff edge. **Every chunk is derived twice while moving, at every radius**
(1.7–1.95 derives per generated chunk against 1.0 standing): the leading slab derives against an
absent neighbour and again when it arrives. A third, voxels-only margin ring would make that once
for +37% generation on half-idle workers; **named in TODO under M5d-1's levers, and it wants a
planning pass against M5c-1's no-third-ring argument before it is believed.** The adaptive radius
itself is a Main-side policy on `ChunkStreamer.UpdateViewer`: shrink a ring a frame on derive
backlog, grow a ring on dwell. Its floor and its order against the third-ring lever are Matthew's.

**The queue's "200 ms re-sort" is measured (2026-09-06), and there is no timer. Suite 1,053 →
1,058.** The sweep runs inside a worker's `TryTake` once the interval has elapsed; it fired
4.6–5.0 times a second in every arm — standing, 12 m/s turning, 40 m/s — with the longest gap 304 ms,
because `IdleWaitMillis` has eight idle workers polling ~80 times a second. At rest 590 of 597
sweeps are over an empty list at 2–5 µs; the fill's first sweep is the only cost, 1,600–1,950
entries at 2.7–3.5 ms once. `JobScheduler.Sweeps` is the snapshot, `sweeps` the profile line,
`M2a:` the periodic one. **A five-milestone-old flake was diagnosed with a control and fixed the
same pass**: before a sweep the queue pops newest-first, so the scheduler's throwing-job test ran
its survivor first and disposed with the thrower still queued.

**M8b-4a is built (2026-09-06). Suite 1,040 → 1,053.** Five things worth keeping:

- **Nineteen litres in twenty, with the identity exact in both arms.** The pre-4a arm balances
  perfectly while destroying the water, which is the whole argument for `displaced` being a
  reported term rather than a tolerance. The last periodic line with the pause on reads `0 active,
  paused 1,629 cell(s) in 6 chunk(s)` — the automaton asleep with water waiting in chunks the
  player left, the state 4b stores.
- **Coordinate zero is a chunk face layer.** Three of the first run's failures were a corridor at
  y = 0, z = 0, which the face wake reached in every cell; the fixtures sit at y = 8, z = 5 now.
  And one `Step` drops a poured cell two voxels, not one, because a cell woken beside the source
  is visited later in the same tick.
- **Both halves of the face wake are load-bearing** and each has a test that fails without it:
  the neighbour's for water that rested against the absent chunk as a wall; the chunk's own for
  the sheet solve, which writes a levelled cell beside a non-holdable neighbour without waking
  across, because such a neighbour is not a front. 406 face wakes over 3,761 arrivals.
- **Paused is not rested** — the hand-off reads `Rested`, and a paused cell there would be handed
  to a basin under ground the world cannot answer for.
- **Fourteen recipes, thirteen caught; the miss is an identity.** `IsLoaded` before `CanHold`
  changes nothing because *not loaded implies cannot hold* everywhere; the order is a cost claim.
  `IsLoaded` defaults to `true` on the interface, so the eleven predicate terrains are untouched.

**Where 2026-09-05 left off. M8b-3 is built — the hand-off, the settle, and the settle's
share slice, which is the lever the settle's first play fired.** That play made the played world's
mine one 162,572-cell lake, the active set fell from 160,000 cells to 1,600, and then the lake was
re-levelled at every boundary for its creeping edge: ~130,000 cells written a pass, up to 275 ms in
Debug, twice a second, for as long as the edge crept. **The share slice is that lever, built and
measured the same evening**: a sheet with a move in it is re-levelled only when at least one cell in
*n* of it is unsettled or a step stands in it, `--settleshare=<n>` the sweep, 8 the default and an
estimate, 0 the control; on the play's shape (`--lake`) a pass is 20 ms → 0.08 ms Release and the
visits 144,194 → 2,911, and the flood is not paid to decide, because the wake rule lets it stop at
*n* times the disturbance. Read M8-PLAN's "M8b-3b — the share slice" before touching
`FluidSimulation.Sheets.cs` — the union-find flood, the freeze, the thaw and the step detector are
each one recipe from wrong, and the plan says which. **M8b-4 was planned the morning of
2026-09-06 and 4a built the same day — read M8-PLAN's "M8b-4 — the planning pass" before 4b.** Three of the part's assumptions
were not so: a walk deletes *awake* water only (the grid's panel stays in RAM; the awake cell is
displaced into "solid" unloaded neighbours), so the walk's fix is a **pause** at unload and a wake
at arrival, not a settle; the store is a mirror of RAM before it replaces it, read whole at open
into paused panels; and the played lake is 78 MiB of panels, over the 64 MiB trigger, so releasing
panels with their chunk is the third slice rather than a someday lever. **Split 4a pause / 4b
store / 4c release, in that order; the decisions are handed back with recommendations, and the
store slice is last and alone because a defect there is permanent data loss.** Two things
are Matthew's: whether a spill soaking into the ground reads right — **deferred to M8c, "when we
have something for it to seep into"; the hand-off and the settle ship as recommended meanwhile**
— and the settle's interval, `--settleafter=`, two ticks as an estimate and zero the arm that
levels a flowing stream, both to be judged in play. Two things the M8b-3 pass found that the
next session walks into: **the played world
floods the moment it opens** — 465,036 L into the mine in four seconds, 110,792 cells active,
ticks of 188 frames in Debug — which is the seam working on the world it was built for and is now
the play's first fact and M8b-4's first save (by that evening the play had drained the lakes over
the mine and copies of the world opened to 91 kL in the first 29 ticks, so the figure above is
the world as it was, not as it is); and **the r6 control must be invoked verbatim**, with
`--nosave` after the bare `--`, or it opens that world and reads as a flood. **And a walk deletes
the flood's awake water** — read off the play log the same evening: 2.9 ML `displaced` of 7.2 ML
drawn in one session, because an unloaded voxel is solid and an awake cell in a chunk that leaves
the box is drained on its next visit — which is M8b-4's case with a number and puts the settle's
unload method on M8b-4's path. How a spill's soak is measured in play is in TODO under M8b-3a
(`handed` on the `M8b:` line, `received` on `M7d:`), and the HUD shows neither. The M8b-2c play items
still stand: `Q` a chunk out from under the terrace pond, a tunnel into a lake bank, two surfaces
for one body of water; and now the relay's water on a hillside soaking away into the lake at the
pit. The gate on the entangled miner is a battery and waits for M10. `git log origin/main..main`
says what Matthew still has to push.

**M8b-3b is built (2026-09-05). Suite 1,006 → 1,032.** `FluidSimulation.LevelSheets()` levels every
sheet the active set touches — the wet, holdable, supported cells at one Y, sideways-connected —
to within a millilitre, or packs a sheet whose share is under a litre into as few of its cells as
hold it at 999 mL, bottom-up, through `FluidGrid.Transfer` only; `SheetSettle` fires it at a tick
boundary after *K* quiet ticks and then at every boundary until nothing is awake. **Measured
(`--fluidsolve both`, Release): the chamber's 1,915-tick settle is 38 ticks, the 16 m mine's 11,130
is 136, and the 96 m mine's *never* is 258 — every rest real, every litre exact, the visits down
50–200×.** Five things worth keeping:

- **The solve levels and the ticks after it are what it cannot do.** A front creeps a cell a tick
  and water falls a cell a tick, by the no-siphons rule, so "one solve and a handful of ticks" is
  one solve and hundreds; what collapsed is the visits and the world time. A pass per boundary is
  O(sheet) per tick while a front creeps, and at bore 9 on the 96 m mine that is the whole cost —
  the lever is named in TODO, not built.
- **The pass is atomic and 1–3 µs a cell in Release**, so the played world's ~1.3 million-cell
  floor is seconds in one frame once its flood stops. Not met yet; the slice is the lever.
- **A sheet with no move inside it is left alone** — the automaton's own rest within four
  millilitres, or a film under the threshold — so a pass is idempotent and a still puddle stays
  still. Packing is for a sheet that has a move.
- **The solve lists what it levelled as resting, and runs before the hand-off.** A levelled sheet
  is asleep without a visit and the hand-off reads visits: 10,558 L handed in the relay's window
  with the settle off against 4,178 with it on. Waking every written cell was the first fix and
  cost ten times the visits on the mine for a fact the solve already knows. With the list,
  `--spill`'s third arm rests in 29 ticks against 314 and the relay's whole 50,000 L is the basin's
  inside the profile window, Tier B empty: a spill soaks away in twelve seconds, not minutes.
- **The fixtures put water down asleep** through `FluidGrid.Fill`, the state diffusion leaves for
  thousands of ticks, so whatever is awake after a solve is what the solve woke; 23 recipes applied, 22
  caught, and both fixtures that were wrong first are recorded in the plan.

**M8b-3a is built (2026-09-05). Suite 989 → 1,006.** `FluidSimulation.Rested` is the cells a tick
visited and found nothing to do for; `SurfaceHandoff` walks it at the boundary and hands every cell
standing on the analytic ground to the basin under its sample, whole litres crossing and the
remainder carried per pit; `WaterSystem.Receive` is the Tier A half; `handed` and `received` are on
both tiers' lines with a `handoff` line between them. **Measured (`--spill`, Release): the hand-off
removes 99.3–99.5% of a surface spill's visits** — 10,000 L rests in 170 ticks instead of 9,111,
50,000 L in 314 instead of never, Tier B empty in both — **so M8a's coarser-tolerance fallback is
dead for the surface**, and it removes nothing from a mine, which is the settle's case. Five things
worth keeping:

- **Water on the analytic ground is already Tier A's, by Tier A's own model.** The hand-off is
  `BasinRainfall`'s delivery with a different source. An outlet cell leaves the world, counted on
  both terms; a tank on natural ground leaks as rain inside it would, a tank on a placed floor
  holds (the seam reads the *seed* surface) — pinned, revisit trigger the first play that reads
  the leak as wrong; only the cell on the plane is tested, the one above falls and follows.
- **Resting is the automaton's own no-move visit, and the automaton gained no opinion about the
  ground.** The list is a record of visits, cleared at the next tick; DESIGN's *stable* cells
  merge is that word, and a pour is seen spreading before anything is handed.
- **The one battery hole moved *when*, not *whether*.** Listing moving cells as rested passed every
  hand-off test, because a spill handed off mid-flow still ends in the basin with every identity
  exact. The tests assert a spreading cell is not listed and nothing is handed on a pour's first
  boundary. Eighteen recipes; five did not apply until the harness matched CRLF.
- **The settle is designed and not built (M8b-3b).** Sheets, wet cells only, packed under a litre
  a cell, on *K* ticks of no inflow with `--settleafter=` as the sweep; see the plan.
- **`--fluid` reproduces the M8b-1 table to the visit with the rested list collected**, and the r6
  control, invoked verbatim, is 2601 / 325 / 3446 at 411 with every new term zero.

**M8b-2c is built (2026-09-05). Suite 974 → 989.** `PortalDrain` draws whole litres out of Tier A
into Tier B at each tick boundary; `PortalTracker` feeds the index from the pipeline's three
arrival paths, the edit hook and the forget, and owns "a hole is only what was dug";
`StreamerSeedHeights` reads the seed surface off the loaded columns; `--fillbasin=` and
`--breach=` are the provocation. **First run: 200,000 L poured, a 3 × 3 shaft at the basin's pit,
7 portals covered, 1,117 L drawn, both identities exact and Tier A's drawn equal to Tier B's, the
r6 control unchanged to the digit.** Five things worth keeping:

- **The breach goes at the basin's pit, not under the player.** The first run bit under the
  spawn and drew nothing — "1 wet, 0 covered" — because the spawn's sample belongs to a basin
  whose pit is elsewhere and sits above the lake. A pour covers the pit first; a real breach
  drains a lake from its bed. `--fillbasin` exists because a breach under a dry basin measures
  nothing, and it is a reported term.
- **Decision 1 needed a rule the plan did not write.** An unedited chunk arriving beside a dug
  one re-scans the dug one, or a pit whose mouth is the top row of the dug chunk is never found;
  and a chunk's first edit scans it. The tracker's fuzz holds the index to *the definition over
  every pair touching a dug, loaded chunk*, with natural caves in the fixture so that "touches a
  dug chunk" and "was dug" differ.
- **A dead-end one-voxel shaft takes ~1 L a tick after the first**, because the mouth passes
  down only the room the cell under it had when visited. The automaton's throughput, not the
  seam's; M8d's waterfall question with a second number, recorded in TODO.
- **The water line's `held` double-counted every in-window basin since M7d-1** — window plus
  ledger, where the ledger is the truth. It banks and reads the ledger now. The periodic `M7d:`
  line was always right.
- **Two fixtures passed for the wrong reason before the battery.** The pipeline tests wrote air
  into air, which the pipeline correctly does not count as an edit; and the covered test's
  off-by-one hid behind a one-draw tolerance until a lake was constructed exactly at the face.

**M8b-2b is built (2026-09-05). Suite 963 → 974.** `PortalIndex` in `Overburden.Core/Fluid/`
holds every hole in the analytic ground as a pair — the holdable voxel at or below its seed
surface, and the face neighbour above its own surface it opens onto — filed under the *outside's*
4 m sample, which is where the lake is. `SkyColumn` keeps its seed surface. Four things worth
keeping:

- **A scan takes every pair touching the chunk from either side, and so does a forget.** A pit
  under a chunk boundary has its halves in two chunks; an unloaded voxel is solid, so it is a
  portal only while both are loaded, and whichever arrives second finds it. Tested in both orders.
- **The seed surface on `SkyColumn` is the difference between 1.2 ms and 10.3 ms a bite**, Release,
  `--portal`. The edit hook evaluates ten pairs with the five seed surfaces read once, geometry
  before terrain, so a deep bite is 32,768 exits at 0.9 ms. `SeedSurfaceY` throws when unseeded,
  is never persisted, and `CopyFrom` leaves it alone. **Lever, not built: a chunk wholly below its
  footprint's surface band can skip the hook.**
- **The edit path is deliberately a second copy of the geometry**, and that is what let the fuzz
  catch a mutation in the definition. The oracle is the definition over every voxel of every
  scanned chunk and its shell; if the hook called the definition, a wrong definition would agree
  with itself.
- **The natural-cave fixture had no caves.** At the shipped threshold the field never reaches it
  in the test region, so *a natural cave is never a pit* scanned solid rock and the margin recipe
  passed. It now asserts 2,005 columns with a cave directly under the margin. Fourteen recipes,
  fourteen caught. **The index does not know what an edit is**; "a hole is what was dug" is the
  caller's filter on which chunks it scans, and 2c must apply it.

**M8b-2a is built (2026-09-05). Suite 953 → 963.** `BasinWater.Remove` takes water off the top of
the lake a basin is part of, down the path to the leaf and never a sibling; `HeightField` samples
`SurfaceHeightAt + 1`, the plane the water stands on. Four things worth keeping:

- **`LevelAt` and `Remove` share one walk, `LakeTop`, and building the second found a defect in
  the first.** A composite closed at the same crossing height as its children has zero own
  capacity, so "the parent's slab is empty" said nothing about whether the water stood above the
  ridge, and three pits on one flat shelf reported one lake at two heights — since M7a-2, on the
  commonest terrain integer 4 m samples produce. The walk now climbs through a zero-capacity
  parent when both children are full. Caught by a three-pit fixture and by the random-terrain fuzz.
- **The plan's oracle was backwards.** `Remove(x)` then `Add(what it took)` is always the identity;
  `Add(x)` then `Remove(x)` only inside one lake, because water that crossed a ridge into a
  neighbour with room is the neighbour's — the no-siphons rule arriving in Tier A. Both are tests.
- **The plane is pinned to the sky column.** `SkyColumn.Seed` has called `SurfaceHeightAt + 1` the
  sky's start since M5b; a test asserts the field's sample equals it, because 2b's portal test asks
  the sky column and Tier A asks the field. A basin's first litre now draws above the ground, and
  **every played pond's surface is one voxel higher than before** — M7's level figures read one
  voxel low from here on, its litres and capacities do not.
- **Thirteen break-recipes, thirteen caught.** Take-from-sibling is invisible to
  capture-and-re-pour, since a drained sibling is a state a fill can reach; the ridge rule sees it.
  `WaterSystem.Remove` and its `drawn` counter are deliberately 2c's.

**The tick budget slice is built (2026-09-04). Suite 936 → 953.** `FluidSimulation.Step` is a
cursor over its batch: `Resume(maxVisits)` visits up to N cells and returns the tick's result only
when the cursor reaches the end, `Step(deadline)` resumes in granules of 64 visits against an
injected clock, checked after each granule and never before, and `Main` begins a tick at 2 Hz and
resumes an unfinished one every frame under **`--fluidbudget=<ms>`, 2 ms by default**. Five things
worth keeping:

- **Slicing changes nothing but when a tick ends, and that holds to the digit in both
  instruments.** Whole against sliced agree cell for cell after every one of 400 ticks at four
  slice sizes in the suite, and every bench figure — litres, peak, visits, held — is identical
  between the arms. The budget is a cost decision, not a physics one.
- **The roof-open tick is 38 frames at 2 ms instead of one 80 ms frame in Release**, the single
  bitten chunk 17, and **the ordinary widest breach needs two slices for most of its ticks**, so
  the slice is not a one-case instrument. In Debug a frame pays mean 2.00 / max 3.05 ms instead of
  14–23, the longest ordinary tick is 17 frames, and 2 Hz holds; the roof-open case there would be
  ~400 frames of water arriving, which is DESIGN's *slower than realtime* and an open play question.
- **The overshoot is one granule's *work*, not a constant**: 0.1 ms over on the mine, 0.9 ms on
  the 302,182-cell tick, because a tick's first granule clears a `HashSet` proportional to its
  capacity. Named lever in TODO, not built.
- **Zero is a setting, not off** — one granule a frame — and the zero-budget headless arm is the
  wire check the suite cannot run: nineteen completed ticks at ~1 Hz with the identity exact say
  `Main` resumes an unfinished tick every frame rather than waiting on a timer that cannot fire.
- **Three battery holes, three fixture shapes, and one was a hang in the subject.** Core's
  deadline loop under a *frozen* fake clock is an infinite loop against any mutation that stops
  the cursor, so the test clock counts now; a `Settle` recipe was unreachable by a falling fixture
  because falling cells always wake something; and a counter both arms of the comparison share
  was pinned directly. `VerifyQuiescent` refuses mid-tick rather than answering, and
  `ActiveCellCount` counts the unvisited half of a tick in progress. The whole-tick path's own
  overflow (`cursor + int.MaxValue`) was the first thing the new tests found.

**Cache slice 2 is built (2026-09-04). Suite 926 → 936.** A chunk leaves the box with its derived
light and comes back reseeded rather than derived: **derives 5,157 → 3,572 at 6 m/s, 8,454 → 5,611
at 20 m/s, and the 20 m/s turnaround arm draws where the control never does** (r6h/2v, Debug,
turning every 5 s). Two invalidations and nothing else: an edit at a cached chunk's face drops its
grid (`QueueLightBuild`, for an untracked position), and a column that moved under it — by an
edit, a restore, or being dropped and re-seeded as a new object — is caught at revival by
`SkyColumn.Revision`. The propagated values are never kept. Five things worth keeping:

- **The walk the plan asked for found three light defects that were not the cache's, and its cap-0
  control arm is what said so.** A grid that leaves now takes back the light it gave its neighbours
  (`LightPropagator.RetractFrom`, `SpreadInto`'s inverse — the whole loaded set was never a
  fixpoint before, by an accepted M5c-1 argument that stopped holding when the chunk came back with
  different sources); the un-light walk reseeds past a cell that held 1 (a hole since M5b); and
  **a face conducts only when both cells say so**, because a chunk derived against an absent
  neighbour reads air there and the stale bit leaked skylight into rock until the gain re-derive
  ran. Each has its own test.
- **A push cannot cover a replaced column.** The plan's second push walked the column; slice 1's
  "a dirty column is not cached" means the column a chunk was derived against can be gone on
  return, so the grid records the column's revision and the revival compares once. The sharp case
  is a voxel *placed* above a cached chunk — digging down from above stops at the unloaded chunk.
- **Test 7's oracle is derived data, not values.** M5b's fixpoint check takes sources and
  conductance as given, and stale derived data satisfies it perfectly; `AssertDerivedFresh` rebuilds
  every tracked chunk's derived data from the voxels and column as they are now, masking only faces
  toward an unloaded neighbour. Both it and the whole-set fixpoint hold in both arms.
- **The reseed is deferred to the light step**, because the changed-cell list is only collected
  there, and a seeded boundary cell nobody recorded leaves a drawn neighbour sampling darkness.
- **~3.4 KB per cached chunk with its grid** (4.4 KB in the played world), the plan's estimate
  measured; 64 MiB ≈ 19,000 chunks and stays Matthew's number.

**Rename stage 3 is built (2026-09-03). Suite 912 → 926.** `Identifier.RetiredNamespaces` folds
`minercraft` to `overburden` **in the constructor**, so the two spellings are one value wherever a
string enters — header, mod JSON, dependency list, command line — and no reader can forget the
alias. Tested against the played world's real v3 header, captured before the change; the constant
alone refuses that file, which is the failure the alias prevents. Four things worth keeping:

- **After any Core change, `dotnet build Overburden.Game` before a headless run.** Godot runs the
  DLL in `.godot/mono/temp/bin`, which `dotnet test` does not refresh. The stage's first run used
  the stale Core, loaded `mods/overburden/` as a stranger, played past the content error and wrote
  a header with two numbers for one material on quit. It hit the copy, not the original.
- **A table naming one material twice is refused at read, write and seed**, with the cause named:
  the only way a saved table repeats itself is a build older than the alias opening a newer world.
  The real damaged header is `Fixtures/downgraded-world-v3-header.bin`.
- **A content error refuses the run now.** M1 called air's absence a load error; `Main` had
  printed it and carried on since then.
- **A dependency on `minercraft` is satisfied by the base game; a mod's own ID is not aliased**,
  because a mod's folder is its namespace and a folder called `minercraft` would read from nowhere.

**The derive queue drains nearest-first (2026-09-03). Suite 910 → 912.** `ChunkPipeline`'s
`_lightBuilds` was a FIFO, the one queue here without the job queue's distance rule; it is now a
list sorted farthest-first when something was added or a viewer moved
(`ChunkStreamer.ViewerVersion`), popped from the end. **Measured on the played world at a standing
start, `near lit` (every chunk within two of the viewer derived): 5.14 s → 0.62 s windowed, 3.27 s
→ 0.56 s headless; the seed world 2.24 s → 1.04 s; `light settled` unchanged at 6.2–6.3 s**, because
an order moves no total. The r6 baseline is unchanged to the digit. Four break-recipes (FIFO again,
no re-sort on a move, the sort inverted, enqueue not marking the list stale), four caught, by a
zero-budget oracle: a budget of zero derives exactly one chunk per update, so *the chunk derived
this update is no farther than any chunk still waiting* is a boolean over the state once the queue
is full. **`first draw` could never have measured this** — inside a void nothing near the player
draws — which is why the `fill` line gained a milestone rather than the profile gaining a number.

**The retained-chunk cache, slice 1, is built (2026-09-03). Suite 895 → 910.** `ChunkStreamer`
keeps a forgotten chunk's voxels and a released column in a byte-capped, least-recently-forgotten
cache, and a chunk coming back enters the pipeline at `Generated` with its persistence flags intact.
Memory, then the cache, then the store's index, then the seed. Measured at r6h/2v, Debug, turning
every 5 s, cache 64 MiB against 0:

```
                          6 m/s, seed world   20 m/s, seed world   6 m/s, played world
generated                  4599 → 3213         9539 → 4590          2266 → 1580
loaded                        0 → 0               0 → 0             2332 → 1633
from the cache                0 → 1386            0 → 4960             0 → 1385
derives                    5233 → 5269         8958 → 8952          5395 → 5400
viewer ms  mean / p50   6.58 / 6.07 → 0.91 / 0.33   6.08 / 6.08 → 1.05 / 0.29   6.76 / 6.12 → 0.95 / 0.34
```

Five things worth keeping:

- **The loads and generates a turnaround costs went to the cache, to the chunk, and the derives
  did not move.** That is the plan's prediction exactly, and it is the argument for slice 2: the
  derive is now the whole of what a turnaround still costs on the sim thread, and at 20 m/s both
  arms still draw nothing — M5d-1's cliff is a derive cliff and the cache cannot touch it.
- **The box-difference sweep is mostly column seeding and record creation, and a hit skips both.**
  M6d-2 measured `viewer ms` at two to four times the pipeline update and deliberately did not
  chase it; a cache hit takes its mean from 6.6 ms to 0.9 ms and its median to 0.3. So the sweep's
  cost was never the difference itself — it was `SkyColumn.Seed`'s 1,024 surface evaluations per
  column and a record per chunk. **Recorded as a finding rather than acted on**: the p95 is
  unchanged, because the first fill has nothing to hit.
- **A dirty column is not cached, and the plan said every column was always valid.** A column
  released dirty is handed to the save with no copy-on-write — a chunk has
  `VoxelsSharedWithJob`, a column has nothing — so a revival could edit it under the serialiser.
  Only a column with nothing to write is kept; in play that is nearly all of them, because the
  autosave marks the live set clean every thirty seconds. **The one that is not is the column you
  just dug**, and it comes back through its restore exactly as before.
- **The dirty-on-evict rule is belt and braces, and the plan's premise for it was wrong.** It said
  `StepBack` restores `DirtySinceSave` on a dropped save; it does not — `ApplySave` puts the bytes
  back on the pending list and leaves the flag alone, so nothing today can dirty a cached chunk.
  The hook is kept, tested by setting the flag by hand, because the failure it guards is a deleted
  tunnel.
- **~2.0 KB per cached seed chunk, ~2.8 KB in the played world, no light.** The plan's 3.5 KB
  included 1.5 KB of grid; at 64 MiB that is on the order of 25,000 chunks. **The cap is an
  estimate and is Matthew's to move.** A profile reports the run's first draw, not the first draw
  after each turn, so the plan's "first draw after the turn" was not measured — the `sources` line
  is what the slice turns on.

**"The ground is gone" (2026-09-03) is not a renderer defect, and the first thing to check is
`first draw`.** Nothing meshes until its neighbourhood is lit, the derive queue is FIFO, and in a
played world the chunks around the player are the stored ones, which arrive last through the
serial lane — so a mined-out region lights from the far ring inward, at two to three derives a frame
in Debug (1.37 ms each at 1.0×; the 2.3× recorded that morning was the game itself running beside
the measurement). The "clouds" in
such a shot are Tier A lake surfaces seen from below. **Fixed the same evening: the derive queue
drains nearest-first** (see above — `near lit` 5.14 s → 0.62 s windowed), and `--lightbudget=16`
is still the lever on the total. See the worklog entries.

**M8b-1 is built (2026-09-03): Tier B is in the game loop, with the relay, and the water is on the
screen.** Suite 892 → 895. `R` puts 10,000 L down at the crosshair, Alt+R takes it back,
`--injectlitres=<n>` does it headlessly, and a 50,000 L pour spreads eighteen metres downhill in
five seconds against a `--nowater` arm that is all grass. Five things worth keeping:

- **The seam edits moved no digit — and the recorded M8a-2 table does not reproduce.** With the
  edits stashed the committed code gives the identical visits, to the visit; a 1,000-tick window
  reproduces the table's L/tick and not its peaks. **The committed code is the baseline**: peak
  4,096–20,737, **29 L/s** for a one-voxel hole, **51 h** for a `hand_pick` bore, **6.0%** of the
  light budget at the widest breach. *"9× the width, 9.0× the rate"* is window-dependent — 6.3×
  over 2,000 ticks, 10.5× over 1,000 — because **a mean rate is a property of its window.**
- **An 8 m floor settles where a 96 m corridor does not** (1,915 ticks, rest real; 16 m in
  4,936), so M8a's O(n²) is about length and a bitten chunk is wide. **But a roof falling in is
  the largest tick there is: 43,374 cells for one chunk, 302,182 for 2 × 2 × 2, in one tick, 73.5 ms
  in Release** — six times the 50,000-cell trigger, reached in a single step and collapsing after.
  **The tick budget is a slice, not a rate; `Step` has to be resumable across frames.**
- **The tick costs a frame and a half in the Debug build people play**: 17.7 ms mean / 25 max for
  ~6,000 active cells, 2.9 µs a visit against Release's 0.2–0.3 — the light derive's Debug cliff
  (M5d-1) a second time. Same slice question.
- **`Disturb` wakes only cells that hold water, never the opened voxel**, and returns at once on an
  empty grid — a chunk bite is 32,768 voxels, and waking each for an empty visit is 229,000 visits
  in one tick. The pipeline's `onVoxelChanged` fires after the write and not on a no-op; both
  tested, the hook on the call site (M5c-2's rule).
- **The relay's ball is sized to its litres because a fixed one is a hose.** 15,871 of 50,000 L
  delivered in mid-air, 11,837 on the ground: the automaton falls one cell a tick, **0.5 m/s at
  2 Hz — four metres in eight seconds**, which is M8d's waterfall question with a number, and
  spreads diffusively, so a source drains at its boundary's rate.

**The played world (2026-09-03): 4,584 stored chunks, and M6's load-path concern is retired for the
shape the chunk miner produces.** Matthew mined a 264 × 304 m region 64 m deep; 4,562 of its chunks
are uniform 52–63 B blobs, the inventory holds 1.77 GL of stone, and the world has 5.3 h on its
clock. Measured against a copy with its chunks deleted — same header, position and basins: **953
serial loads, light settled 5.97 s against 5.88 s generating; 1,528 loads under a 40 m/s fall and
`held 0` in both arms.** Caveat: uniform blobs are the cheapest deserialisation there is, so the
trigger moves to *thousands of non-uniform stored chunks*. Four things the play put outside the
plan, all recorded in TODO rather than built: **M8's cost model comes from tunnels and one bitten
chunk under a lake is 32,768 cells of void** (a second `--fluid` fixture shape before M8b's budget);
**M9 has no cost model and the chunk bite is its worst case** (a fill is 512 m³ in mid-air); **the
gate that makes you earn it is a battery** — Matthew: *"ima make you work to get this … an energy cost
and must be recharged … only once you have the latest tier battery will it be as effective as it is
now"* — so it attaches to M10's power model, not to a game mode, and that count stays at two; and **the hotbar's litres have outgrown their unit** — `1,767,903,281 L` in an 84 px slot.
Two instrument readings the played world breaks: `L per wall-clock minute` is a lifetime total over
this run's seconds (313 ML/min on a reopened world), and a profile taken inside the void reports
`first draw not yet` because 923 meshes were correctly empty — **`light settled` is the comparable
number inside a mine.** And the window-edge outlet, designed and zero until now, moved 4.0 ML on one
window move in a world with 92 ML banked. **The play answered the same day: *"lakes overhanging
yes"* — M7's unconfirmed shape, now M8b's second play-confirmed motivation; *"4s yeah feels fast
enough"* — the halving is closed; and no lake was seen to drop, which is silence rather than
evidence. And a new locked decision in DESIGN: *"ideally this game will never end, it's more artistic
endeavor"* — nothing is endgame, because there is no end.**

**The entangled miner fills a chunk as well as taking one (2026-09-03). Suite 869 → 888.**
`Place.Chunk` is a sixth shape sharing the same `Put`; while `Q` is on a bare **right** click fills
the 8 m chunk in front of the crosshair, at the same cooldown and the same reach as the bite.
**It was easy because `Place` and `Carve` have been deliberate twins since M6c-1 — and the one
place the mirror does not hold is the whole of getting it right.** Five things worth keeping:

- **The bite and the fill name different chunks from the same aim.** `Carve.Chunk` takes
  `hit.Voxel`'s chunk; `Place.Chunk` takes `hit.Previous`'s — the split that has separated
  `Place.TargetVoxel` from `Carve.Chip` since M6d-1, one scale up. **Mining names the thing you
  point at, building names the space in front of it.** Copying the mining half breaks the verb
  *precisely where it is needed*: stand in a chunk you bit out and look at its far wall, and the
  wall is the neighbouring chunk — so the fill would report **32,768 `Occupied` having written
  nothing**, broken in the one situation it exists for and fine in open air where nobody needs it.
  **Caught by 3, two of them scenario tests**, because a hit that does not straddle a boundary is a
  hit at something other than a wall.
- **Only air is replaceable, and at 512 m³ that stops being a tidiness rule.** M6c-1 argued it as a
  hardness-gate defence; a fill that overwrote would be a key deleting half a thousand cubic metres
  with no tool and no gate — **strictly worse than the bite, which at least still asks the tier.**
  Dropping it fails **6**, three of them pre-existing brush tests. **The entanglement clause is now
  symmetric across both verbs: the mode changes how far, how fast and how big, and never what a
  verb may overwrite.**
- **One genuine hole out of ten recipes, and the sibling battery had already pointed at it.**
  **`TargetChunk` by truncating division was green**: every other test names its chunk directly and
  both seam tests straddle a *positive* boundary, so **nothing exercised the one conversion that can
  round — voxel → chunk.** The bite's battery recorded that exact asymmetry and *its* version of the
  recipe was an identity; here the same reasoning found a live defect, **because the fill actually
  calls the conversion.**
- **`OutOfStock` stops being an edge case, and it is an unplanned balance mechanism.** A bite credits
  512,000 L of *mixed* stone, dirt and seam; a fill spends 512,000 L of *one*, so **a chunk generally
  cannot be put back with what taking it yielded.** No weight, no encumbrance — that ruling stands
  untouched — but the fill is still bounded by having mined enough of one thing. **Recorded as an
  observation rather than a decision, because nobody chose it. Revisit trigger: the first play that
  finds the limit tedious rather than motivating.**
- **The cost is measured (2026-09-03), and the prediction was right about the shape and wrong about
  the size.** A fill into a chunk 87% air: **8.2 ms**; a fill into solid, 32,768 `Occupied` reads:
  **2.6 ms**; against the bite's 16.9 mean / 35.6 max. The profile is inverted as predicted — into
  air is the fill's dear case — **but the dear case is half a bite**, because `Put` credits nothing
  per voxel and asks no hardness. **The fill's loop order is the pessimal one for the sky column**:
  Y ascending, a bite moves each column's open height once and a fill into air moves it 32 times,
  deduped at `QueueLightBuild` so it costs lookups rather than rebuilds. Descending Y is one line and
  is not done until a fill hitch is felt. It also **closes a recorded revisit trigger**: entangled
  placement reaches as far as the bite, which is *"a placement verb that takes a reach argument"* —
  the condition the `L`+`Q` note named.

**The entangled miner takes a whole chunk (2026-09-03), and the change found an instrument defect
six milestones old. Suite 850 → 869.** `Carve.Chunk` is a third shape beside `Sphere` and `Voxel`,
sharing the same `Take`; while `Q` is on a bare click removes the 8 m chunk the ray hit. Five
things worth keeping:

- **It reverses half of a rule recorded twice, and the surviving half is the one that mattered.**
  Entanglement "changes how far and how fast, **never how big a bite or what the bite is hard
  enough to take**" — the bite clause is gone, **the hardness clause is untouched**, so a chunk
  bite through bedrock leaves the bedrock standing and `Carve.Take` is still the only gate in the
  game. Dropping it fails **11** tests, most of them pre-existing sphere and chip ones.
- **No weight, recorded as a decision rather than left as an omission.** 512,000 L a click, no
  carry limit, no encumbrance. Matthew's argument is the good version: the real-world answer to a
  haulage limit is *"zip tie two shopping trolleys together and drag that sucker behind me"*, and
  a player who cannot just walks back and forth — **patience taxed and called difficulty.**
  **Revisit trigger: a survival mode**, already the trigger for shortening reach; one decision.
- **One bite costs `swing ms` mean 16.9 / max 35.6 on the sim thread — one to two whole frames —
  and no cooldown can bound it, because one bite is atomic.** M2d's mesh-apply finding a third
  time. **So the rate chooses the duty cycle**: 6.6% of wall clock at 4/s, 2.7% at 2/s, and the
  world streams either way. **Per voxel the chunk bite is 4.3x cheaper than the sphere** (0.70 µs
  against 3.0) and the whole-run main thread went *down*, 65.95% → 52.53%, because an emptied
  chunk meshes to nothing — **stated with its confound**, the arms removed 66,560 m³ and ~333 m³.
- **0.25 s ships, and the play settled it on 2026-09-03 — *"4s yeah feels fast enough."*** Matthew raised it and withdrew it —
  *"I don't want to adjust the goalpost mid run"* — so it takes `rainfallScale`'s shape: **the
  measurement bounds the range, a person holding the thing picks inside it.** `--chunkcooldown=`
  is the seam and is **not profile-only**, because `Q` is a play bind. The argument for halving
  corrects a claim the measurement invited: *"the rate chooses the duty cycle and nothing else"*
  is true of **cost** and false of **feel** — when one bite is a whole frame, **the duty cycle is
  the felt smoothness**, and 250 ms is inside human reaction time so stopping costs 1,000 m³ of
  overshoot.
- **The profiler has been inflating every per-second figure with a counter numerator, by 10% at a
  30 s window and 20% at the 15 s default.** Found by arithmetic, not by an assertion: the chunk
  arm reported **4.33 swings/s against a nominal 4.0**, and a cooldown that is *reset* rather than
  decremented can only deliver fewer than nominal — **a rate above its own ceiling has to be a
  denominator.** `MeasuredSeconds` excludes the 3 s warm-up; the pipeline's counters do not.
  **Per-swing figures were never affected and the histograms were always right**, so every
  recorded *conclusion* stands — the miner's "1.98x" is a ratio of two identically inflated
  numbers. Fixed as `StreamingProfiler.RunSeconds`. **Two kinds of number were sharing one
  denominator and only one of them wanted it.**

**M8a is built (2026-09-02) — Tier B's automaton and its measurement. Suite 814 → 850.** Water
moves, conserves volume exactly in integers, terminates by proof, and is affordable at the 0.25 m
voxel M8-PLAN settled on. **Nothing feeds it yet — that is M8b, and the measurement is what says
M8b is the milestone rather than the automaton.** Six things worth keeping:

- **The unit is the millilitre, which corrects DESIGN.** A 0.25 m cell is **15.625 litres — not a
  whole number** — so DESIGN's "amount 0-255" makes one unit 61.27 mL and every conversion across
  the Tier A ↔ Tier B seam rounds, which a no-tolerance conservation oracle cannot survive.
  Capacity is **15,625 mL, an exact integer division** of 1,000,000 mL/m³ by 4³ voxels; at three
  voxels per metre it would truncate. One byte per cell, and the seam is exact both ways.
- **The wake rule was wrong and `VerifyQuiescent` caught it on its first run.** Dividing the
  sideways surplus by *eligible + 1* — the obvious rule — makes a cell's move depend on its
  neighbours' amounts rather than on the pair, so **a neighbour *gaining* water can shrink the
  divisor and create a move with nothing awake to take it.** Five litres on an 8×8 floor settled
  with four cells one millilitre out of level and **every volume assertion green**. A fixed divisor
  of five makes the rule local to a pair. **Conservation is structurally blind to this class of
  defect; that is what the second oracle is for.**
- **Termination has a proof rather than a hope**, in `FluidRules`: a sideways move strictly
  decreases the sum of squares, a vertical move strictly decreases Σ(amount × Y) which is bounded
  below because an unloaded voxel is solid. **The proof also forbids the obvious tuning** — a
  minimum transfer of one millilitre oscillates forever, and it is in the break battery.
- **The play's question is answered with a number, and the answer is no.** *"Unless I make a hole
  4x4 it isn't going to drain into eventually?"* — **every** hole drains, down to one 0.25 m voxel,
  at **29 L/s** (re-measured from the committed code at M8b-1; M8a-2's 30); a `hand_pick` bore
  empties a 9,000,000 L lake in **51 hours of world time** as a 2,000-tick mean (M8a-2's 38 was a
  shorter window). And **the rate grows with the bore's width, nowhere near its area** — 9× the
  width, 6.3× the rate over 2,000 ticks and 10.5× over 1,000, 81× the cross-section — so past the
  first voxel the constraint is the 0.75 m tunnels below, which back up.
- **The flood is affordable and the settle is not.** Peak active **4,096–20,737 cells** against
  M8-PLAN's derived ~14,400, and **31,173 visits/s at 2 Hz against the light budget's ~520,000 —
  6.0%** (committed-code figures, M8b-1; M8a-2's table read 2,706–17,357 and 5.1% and does not
  reproduce).** But **every 96 m arm hit a 41-minute budget with 9,000–21,000 cells still awake, more
  than during the flood**, because water keeps spreading after the inflow stops. It terminates — a
  16 m mine reached real rest in 11,071 ticks — but **it stayed at ~1,700 active for 10,992 of
  those 11,071 ticks and collapsed in the last 79.** There is no cheap early exit. **DESIGN's
  *sleeping is load-bearing* is now measured, and M8b's handoff to Tier A is load-bearing rather
  than tidy.**
- **Eighteen break-recipes, seventeen caught, and all three holes were fixtures rather than missing
  assertions** — a chunk that empties and refills, water inside rock (unreachable by any run of the
  automaton), and a 1×1 shaft, which is the only shape where the "room below" clause fires without
  the sideways clause firing too. **The eighteenth is an identity and is named as one**: `Wake(to)`
  cannot be broken today because every destination is a face neighbour of the source — and it is
  kept because **the debug relay is a transfer between cells that are not adjacent.**

**Four of M7's five play questions are answered (2026-09-02), and the sharpest thing the play
produced is a question rather than an answer.** The pond pacing reads right — *"in sealed ponds
on the surface it works perfectly"* — with `rainfallScale` **halved from x40 to x20** on the
play's own wording; and the 4 m shoreline is **tolerable**. The question that came out with it:
*"unless I make a hole 4x4 it isn't going to drain into eventually?"* — **and it decides an M8
design.** The 4 m grid is Tier A's *analysis* resolution, not water's *physics* resolution, so a
0.75 m bore drains **only if M8 treats the breach at voxel resolution** rather than re-analysing
the watershed from edited heights, which is the shape `SkyColumn` invites and which would make
the minimum drainable hole 4 m across. **M5b solved the identical problem** — a 1 m light cell
against a 0.75 m bore, fixed by making opacity a property of the cell *face*. **Twice now the
answer has been that the tunnels this game is about are narrower than a grid chosen for another
reason; every coarse grid this engine adds will meet that.**

**The play has begun, and its first finding is a sixth one (2026-09-02): a pond above a dug
tunnel does not fall into it.** That is DESIGN's *"edits do not change the watershed at M7"*
met by a person — **not a defect, and the first play-confirmed motivation for M8's Tier B.**
An excavation is invisible to the analysis in both directions: water cannot get in, and a cut
rim does not drain either. **The shape still unconfirmed is the one that would read as broken**
— an open pit inside a basin below the solved level should show the surface hanging in air
over the hole, since the quad comes from the *unedited* height. The five play questions stand
unanswered.

**The quantum machine family is proposed and recorded (2026-09-02)** — a fluid pump that
teleports water to a wireless output, several outputs selected round-robin or programmatically, and
a **quantum machine programmer** that is a general config interface for any machine. See
[docs/M10-NOTES.md](docs/M10-NOTES.md). Three things from it worth keeping:

- **The structural reason it is good is not the one it looks like: teleport deletes pipes.** A
  conventional pump implies a network to solve, junctions, invalidation on every edit, and **fluid
  in transit** — more of exactly what M8 has to keep terminating. Teleport is a subtraction and an
  addition. **Fourth outing for *the cheap version is also the more accurate one*.**
- **A machine is the first *entity* this world has held.** Everything today is a voxel (a material
  ID, regenerable) or an aggregate number (litres per basin, a sky height). A machine has identity,
  position and **player-authored config** — so its own table, **header v3 → v4**, and M6’s
  permanent-data-loss warning applies harder than it did to chunks, because a chunk can be re-dug
  and a config cannot be guessed. **Bringing a pump forward to M8d drags v4 forward with it, and a
  machine that does not survive a quit is worse than no machine.**
- **It is the one subsystem whose worst case is knowable in advance**, being bounded by a placement
  count rather than by terrain — which is why it stands even if M9’s structural work proves
  unaffordable.

**M8 is planned (2026-09-02) and not started** — see [docs/M8-PLAN.md](docs/M8-PLAN.md), split
M8a–M8d. Four things from the planning pass that are expensive to rediscover:

- **Tier B is the first state here that is a *trajectory* rather than a value.** Worldgen is a
  function of the seed, light is a fixpoint, Tier A water is a function of volume and terrain. Where
  moving water *is* depends on where it came from, so **its correctness condition is that it
  terminates** — a CA that never sleeps is wrong rather than slow, and **a cell oscillating by one
  unit forever passes every volume-conservation assertion.**
- **SCALE’s open 64x question is answered: 0.25 m, and the cost is 16x rather than 64x**, because
  the active set is a **waterline, which is an area**. ~14,400 cells for SCALE’s generous cave,
  ~28,800 visits/s at 2 Hz, ~5.5% of what the light budget already licenses. *Derived, not
  measured.* **M8a takes the real number first**, on the case the play will actually produce — a
  9,000,000 L lake draining into a shaft, not a settling cave.
- **The headline is the seam, not the CA.** A perfect CA no basin ever hands water to changes
  nothing on screen. A breach is detected at voxel resolution on the cell **face** — M5b’s rule
  with “water” for “light” — and **Tier A never learns the hole exists**, it loses volume and
  re-solves its level.
- **M8 is the first milestone whose only copy of something is in RAM and nothing can recompute it.**
  An in-flight flood has no seed, no analytic form and no fixpoint. The planned answer is **settle
  rather than store**: integrate the transient to rest on unload and hand the volume to Tier A,
  whose ledger already persists.
- **M8’s one open decision is settled by Matthew: option 3, keep M8 clean and tune the seepage
  rate low.** No machines in M8; the family lands whole at M10. **The deciding cost was not the
  pump but what it drags** — a machine is the first *entity* this world holds, so bringing one
  forward would pull an entity table and **header v4** into the milestone whose oracle is exact
  volume conservation, and **two structural firsts in one milestone is how a defect hides behind a
  feature.** **But the relay comes back immediately as a *debug bind*** — Matthew: *“the
  teleport-level tech will be endgame, but for the purposes of testing? Invaluable.”* **The hardest
  part of testing M8 is not the CA, it is getting water to move at all**, and a keypress that moves
  litres is the provocation seam for the whole milestone and M8a’s measurement instrument.
  **No material, no JSON, no ID** — the entangled miner’s rule, second outing. **And it carries a
  trap that is the milestone’s own oracle**: a debug source creates litres from nothing, so
  injected and removed litres are *reported*, and the oracle becomes `held == rained + injected −
  spilled − removed`. Naming is settled too: **`relay_pump` / `relay_outlet` / `machine_programmer`** —
  industrial rather than physics, and *relay* carries the channel-number rule for free.

**M7 is DONE (2026-09-02): built and played — all four parts, eight sub-parts, five play questions.** The clock runs, it
rains, the rain stops at the ground and at a roof, the ground goes wet, the sky goes dark, basins
fill and survive a quit, **and the water is on the screen**. **The person has played it, and all
five questions are answered:** rain reads as rain; **it stops at the mine entrance** — DESIGN's
*the detail people notice*, noticed, and the one thing no screenshot could ever establish;
splashes were re-tested knowingly after the play asked *"what is a splash?"* and confirmed
absent and **not missed** — *"nah like it's a cosmetic"*, which is a far stronger answer than
the silence it replaced, and **the lesson generalises: a null from an observer without the
concept is not the null you think you have**; a pond rising
over minutes feels right, **which moved `rainfallScale` x40 → x20**; and the 4 m shoreline is
tolerable. **The play also produced two things the questions did not ask for** — that a pond
above a dug tunnel does not fall in (the first play-confirmed motivation for M8), and the
minimum-drainable-hole question that decides an M8 design — plus **a cosmetics pass with one
named home and one named gate** (headroom at the end of the DESIGN roadmap, and **it wants two
budgets rather than one**: splashes cost GPU, not CPU). **814 tests passing, and not one of
them could have answered any of the five.**

Every claim M7 makes is either exact or measured: **a 1 km watershed at 26.4 ms
reference-equivalent** so no incremental watershed is ever built; **an hour in one call equals 3,600
calls of one second, to the litre**; **a storm costs zero remeshes** and the rain costs **one draw
call**; **a lake survives a quit to the litre**. **The one thing cut is splashes** (M7c-2), with
four findings recorded against a second attempt.

**M7d-2 — the surface: built 2026-09-02.** `WaterSurface.cs` sweeps the analysis grid and emits a
horizontal quad wherever a terrain sample sits below its basin's solved level — **739 quads over
255 basins, 0.67 ms to rebuild**, four times a second. Three things worth keeping:

- **Nothing iterates water.** One number per basin against one height per sample, which is DESIGN's
  *solved, not simulated* reduced to a sweep and a comparison. That is why it is 0.67 ms.
- **Runs merge along X on `(basin, level)`, not on wetness** — two neighbouring basins at different
  levels are two lakes, and a quad spanning both is a surface at neither.
- **Depth tested but not written**: a ridge hides a pond behind it, and the terrain under the water
  still draws so a shallow pond reads as shallow. M8 owns the real transparent pass.

**A wide shot of a lake was not obtained, and it is this world rather than the code**: the spawn's
terrain is gently terraced so its basins are shallow, and the default view radius is 32 m against a
512 m analysis window. The play has neither limit.

**M7d-1 — the wiring and the measurement: built 2026-09-02.** Suite 804 → 814; the default headless
run is unchanged to the digit. Six things worth keeping:

- **A lake survives a quit, to the litre.** One session quits at 9,404,288 L, the next opens and
  reports 9,404,288 L across 346 banked basins. The in-memory twin is the suite's oracle,
  `WalkingAwayAndBackLosesNoWater` — a `BasinWater` only ever holds what is in the analysed window,
  so **every window move is a chance to lose a lake**.
- **M7a's analysis estimate is confirmed exactly**: 1024 m at 64.97 ms Release against a 2.46x
  machine control = 26.4 ms reference-equivalent, against M7a's recorded 27. The shipped window is
  **512 m — 13.63 ms Release**, on a worker once every ~128 m of travel. **New fact: Debug is ~3.9x
  slower** (53.1 ms in-game), so a profile figure is not a bench figure — the trap that has now
  caught worldgen, the light derive and this.
- **The water costs nothing measurable on the sim thread**, against `--nowater`: `update ms` 6.107
  against 6.064, `viewer ms` 10.845 against 11.125. A tick is one multiplication per basin.
- **`rainfallScale` shipped at x40 and the play halved it to x20 (2026-09-02)** — *"a little
  too fast, um like you could half the fill speed, it would still work as well I reckon."* The
  two measurements bounded it — the bench's 32-minute median fill at the heaviest possible
  weather, and the game's 10.2% of capacity in 40 minutes of ordinary weather — but only a
  person could place it inside that range. **A basin now fills in roughly twenty minutes.**
  **Revisit trigger: a world whose basins are deeper than the spawn's**, since the play that
  picked this was on shallow terraced ponds. `--rainscale=` stays the sweep.
- **A first-draw regression that was the machine, and the control is what said so.** 7.2 s against
  3.4 s earlier the same day reads exactly like the analysis stealing time from the fill — and the
  `--nowater` control ran *slower*, at 8.65 s. **First time the machine-variance finding has been
  applied to a streaming figure**: a fill time without a same-session control is not a measurement.
- **Ten break-recipes, eight caught until one fixture was rescaled.** Both misses were the same
  trap and both were real defects — the fixture filled every basin, so "dropped a step's rain" and
  "delivered it twice" gave the same brim-full number as correct code. Rescaling then exposed a
  genuine cost it had hidden: **a window move discards the pending sub-litre remainder**, bounded
  at under a litre per basin per move and asserted as a bound.

**M7c-2 — the rain itself: built 2026-09-02, and splashes were cut rather than shipped dead.**
Suite unchanged at 804; the default headless run is untouched to the digit, because the rain is not
created without a renderer. Five things worth keeping:

- **It costs one draw call, measured against `--norain`**: 85 draw calls against 84, and 112,332
  primitives against 64,332 — 48,000 more, which is exactly 24,000 drops × 2 triangles. Sim-thread
  arms are *lower* with rain on than off, which is noise around zero: everything a drop does is in
  the process shader.
- **The occlusion field's estimate was ten times out, and the fix was the window rather than the
  resolution.** M7c-2 planned ~0.25 ms per rebuild; at the recommended 128 m window it measured
  **2.5 ms mean / 3.6 ms worst** on the sim thread, roughly doubling the cost of a chunk crossing.
  **The window is now 64 m — 0.98 ms / 1.60 ms** — which still clears the 48 m rain box by 8 m a
  side, and **0.5 m resolution, the decision the plan actually argued, is untouched**. It also took
  the "unknown texel" share at r4 from 34% to 0%.
- **The field prints its own height range, and that check was not optional.** If the `Rf` texture
  were clamped anywhere the kill would be a flat plane — and *rain stopping at a flat plane looks
  exactly like rain stopping at the ground* from anywhere the ground is a metre below you. Every
  screenshot would have passed. `ground -3.5..5.0 m` is what says it is a terrain. **Third outing
  for "eyeballing a screenshot is a measurement too"**, and the first where the doubt came from an
  unrelated failure.
- **`ChunkPipeline.SkyHeightChanges` is the dig trigger and counts height *moves*, not writes** —
  so a tunnel dug underground, which is most of mining, rebuilds nothing. Rebuilds print split by
  cause, because a crossing is bookkeeping and a dig is the feature; the counter's own first
  reading was a false positive (it started at −1, so every run reported "1 dug" before anything was
  dug).
- **Splashes are not delivered, and the findings are worth more than the feature was.** The draw
  pass and process shader work (85 pixels self-emitting); the sub-emitter link suppresses
  self-emission and delivers nothing (0 pixels); re-parenting it as a child gets particles out but
  all at one point; and **a `texture()` call inside a particle `start()` silently yields particles
  that never appear** — nothing logged, nothing failing to compile. Every one of those states was
  established by counting pixels in a forced-colour screenshot; by eye they are identical.

**M7c-1 — the clock, the wet term and the overcast: built 2026-09-02.** Suite 797 → 804, and
streaming is untouched to the digit — 1521 viewed / 165 drawn / 1926 jobs at 404 quads. Six things
worth keeping:

- **A storm costs zero remeshes, and the profile prints it**: `weather 0 edit remesh(es) / 0
  relight remesh(es) during the measured window — wet ranged 0.00..0.67`. **This is where M5's bill
  gets collected** — wetness is `recent rainfall × skylight`, rainfall is one global uniform and
  skylight is already `COLOR.r`, so nothing enters the merge key. DESIGN's stated reason for
  keeping the two light channels separate was this exact term.
- **A difference rather than a total, and printed with its denominator.** The fill is still
  relighting through the warm-up, so a session total reads in the hundreds; and a zero from a run
  that stayed dry is a detector that never fired.
- **The clock advances only while the world is open, and that is now observed rather than
  asserted.** One session ends at t ≈ 6 s, the next resumes at t = 11 s five minutes of wall clock
  later. Core could never test it, because `WorldClock` only moves when something calls `Advance`.
- **`WetnessAt` and `OvercastAt` are box filters over `DepthNanometresOver`** — the one primitive
  the milestone's exactness rests on is the only arithmetic in either, so there is no second model
  of "recent rainfall" to drift. The soak window is **exactly one episode**, which makes wetness on
  an episode boundary *equal* `rate / maxRate` — a rational number a test asserts rather than
  approximates. The overcast window straddles now, so the sky gathers before the first drop.
  **This is also where the integer rule stops, deliberately**: these feed a uniform, are never
  stored or compared, and an `int` here would be exactness cargo-culted onto a look term.
- **The view-space `NORMAL` question is settled and it was a real bug.** `face_tint` read `NORMAL`
  in `fragment()`, which in Godot 4 is view space, so the fake directional shading rotated with the
  player. A `varying` from `vertex()`'s `NORMAL` fixes it for free — a chunk's transform is a pure
  translation, so the model normal **is** the world normal. Measured: the two arms are identical
  facing the default direction and diverge once you turn. **Which look Matthew prefers is still
  his**; `--viewnormal=1` is the old arm.
- **Three plausible ways to dim the sky all silently did nothing, and the cause was a note already
  in this file.** `Environment.BackgroundEnergyMultiplier`, `ProceduralSkyMaterial.EnergyMultiplier`
  and scaling the dome's colours were each confirmed *set* and each left the sky bit-identical to
  the clear arm. M5d left the environment fog on for the sky so the dome's ground hemisphere would
  not band the horizon — **so most of the sky you can see is the fog**, at a fixed colour.
  `Environment.FogLightColor` is the lever. **Eyeballing a screenshot is a measurement too, second
  outing**: M6d-3's was a false positive, this is the false negative of the same shape.

**Header v3** carries the world clock and the watershed lattice, a `basins` table
carries the water, and **`BasinLedger` holds the basins nobody is currently looking at** — which is
the structural point, because a `BasinWater` only ever holds what is inside the analysis window.
**A changed watershed lattice discards stored volumes rather than refusing the world**: a pit is a
sample on that lattice, so changing it renames every basin, and rain refills in minutes where a
refused world helps nobody.


`WorldClock`, `Weather` and `BasinRainfall` give the world time, a forecast that is a pure function
of the seed, and rain that lands on catchments. **The oracle passes with no tolerance: an hour in
one call equals 3,600 calls of one second**, basin for basin. Two things from it:
**the unit chain is the design** — a rate is nanometres per millisecond, depth is rate × ms, and a
nanometre of rain over a square metre is exactly one microlitre, so nothing rounds — and **at real
rainfall the median basin fills in 35 minutes of the heaviest possible weather**, which nobody can
watch, so `rainfallScale` exists and **M7d is where a person picks it**.

 `Overburden.Core/Hydrology/` holds the flood, the basins, the volume→level curves, the
merge tree and the water in it. **A 1,024 m window costs 66 ms measured / 27 ms reference-
equivalent**, on a worker, once per window move — **so no incremental watershed is ever built.**
Four things from it that are expensive to rediscover:

- **Flat ground is the defect that matters.** A flat shelf's corner has no strictly lower
  neighbour, so a per-cell sweep declares it a pit — and integer heights sampled every 4 m tie
  constantly, so real terrain fills with basins that hold nothing. Fixed by sweeping a *tier* of
  equal (filled, height) at a time and spreading the label across the flat.
- **The leaf spill graph has two-cycles by construction**, because a crossing costs the higher of
  the two samples it joins and is therefore symmetric. **The merge tree is what resolves it**, and
  1,336 of 1,376 basins in a 1 km window spill into another basin rather than out of the world.
- **The machine varies by 2.5x and nothing said so for six milestones.** The same binary reported
  a 1 km window at 19.9 ms and at 43 ms hours apart; the worldgen bench, whose figure has been
  recorded since M2d-2, was equally slower. **`--watershed` now prints a control line** — divide by
  it before comparing anything with the worklog.
- **The estimate was right in range and wrong about which half**: sampling is 55 ns/sample against
  an assumed 100–200, and the flood plus the tree are the rest.

**M7 was planned 2026-09-01** — watershed basins, Tier A rain accumulation, the
rain itself, the wet-surface term and a water surface you can watch rise. See
[docs/M7-PLAN.md](docs/M7-PLAN.md), split M7a–M7d, and read [docs/SCALE.md](docs/SCALE.md) before
it. Four things from the planning pass that are expensive to rediscover:

- **A watershed is the first thing here that cannot be made exact by choosing a big enough
  neighbourhood.** Light was the last thing that was not a function of one chunk and arithmetic
  saved it — 15 m of reach against a 16 m margin. Water has no attenuation distance at all.
- **But the input is a pure function rather than the world.** `ISurfaceHeights.SurfaceHeightAt` is
  analytic, seeded and already has a test double, so the analysis needs **no streaming, no margin,
  no barrier and no stage.** It is not a chunk-shaped computation and the instinct to make it one
  is the thing to resist.
- **The world gains a clock, and nothing here has ever needed time** — worldgen is timeless, light
  is a fixpoint, physics takes `dt` and remembers nothing. Header v2 → v3, additive, migrates.
- **A basin is identified by its pit in world coordinates, never by an array index**, because the
  window is player-centred and renumbers when it moves. Non-negotiable 2's argument turning up
  somewhere nobody expected to need it.

M0, all of M1, **all of M2, all of M3, all of M4, all of M5 and all four parts of M6** are built,
and M6 is played — **739 tests passing**. **M6's headline question is answered by play (2026-09-01):
dig a tunnel, fly away until it unloads, come back — it works; quit and reopen — it works.** That
is the thing the whole milestone exists for and the thing M5d-2's play kept running into, and it
is now yes from a person rather than from a headless proxy. **The ramp brush is also confirmed as
the right tool for stairs and the fast path** — *"using the dig tool to get those kind of stairs
would take me a lot longer"* — which validates M6d-1 going first in the split and leaves DESIGN's
carve-for-detail / place-for-bulk split unamended, with stairs on the *place* side. **And the
walls-and-floor half is answered too (2026-09-01): a room with walls, a floor, a ceiling and a
broad flight of stairs down into it, ~3,360 voxels placed** — so the 1 m lattice, the four shapes
and the carve/place divide are judged rather than argued. **All three of M6's play items are
ticked**, and the check the play surfaced is answered too: **the lamp lights the sealed room**,
which puts block light on the screen with the sky channel zero by construction. **Half of that
assertion is confounded by the fixture** — the room is dirt, so warm light on brown rock cannot be
told from white light on brown rock; the sharp version is the same room built in stone.

**M6d-3 — the first real UI: built 2026-09-01.** `Overburden.Game/Ui/Hud.cs` — a nine-slot hotbar
with a swatch, name and litres per slot, a crosshair, and a status line. Suite 734 → 739. Six
things worth keeping:

- **The number row moved to materials and the tool tiers moved to `T`.** M6d's TODO named the
  collision and left it open; both want 1–9 and only one is what a hand expects to find there.
  **The same muscle-memory argument that swapped Ctrl and Shift on 2026-08-31.** Two picks ship,
  so the tiers traded one press for one press — **and if a mod ever ships eight picks that trade
  inverts.** `SelectTier` also stopped being polled every frame and became an event on the key,
  which removed a `ReferenceEquals` guard that existed only to stop it announcing 60 times a
  second.
- **`VolumetricInventory.MaterialInSlot` is the row: carried materials in ascending ID order**, and
  **the cost is stated rather than hidden — acquiring a new material shifts every slot above it.**
  Accepted because the fix is a player-arranged slot assignment that has to survive a quit, which
  is a header field and a format version. **ID order beat first-acquired order on the one case
  that separates them**: acquisition order is stable within a session and loses across a quit,
  because `Reset` replays the header's entries and acquisition order is written down nowhere. A
  rule that is always the same beats one that is usually better and occasionally lies.
- **It gave `Entries()`' sort a second and louder dependant.** That sort exists so the same
  inventory writes the same bytes; it is now what a number key *means*. A shuffled save is
  unanswerable, a shuffled hotbar moves materials mid-build. Removing it breaks two unrelated-
  looking things.
- **`VoxelMaterial.LayerColour` is one definition of what stone looks like**, shared by the block
  textures and the hotbar swatches. A palette in the HUD would be two definitions that agree the
  day they are written — the `IsCaveAt`/`GenerateTerrain` argument.
- **A material's label is derived from its identifier's path and deliberately not a JSON field.**
  **Mods write against a format the moment it exists**, and an authorable name is a name that
  wants translating. This is the one part of M6d-3 that would have been expensive to get wrong.
  **Revisit trigger: the first material whose ID is not a sensible label, or the first request to
  run the game in another language.**
- **The screenshot found two defects the logs could not have, and manufactured a third.** Headless
  has no renderer, so a layout error looks identical to a correct layout in every log this project
  prints — the crosshair's vertical bar rendered as a line most of the screen tall
  (`SetAnchorsPreset` plus a `Size`, recomputed from the anchors next layout pass) and the litres
  label was clipped off the bottom edge. **A pale line at screen-centre then read as a third
  defect and was not**: a pixel sample gave (238, 229, 88), the yellow cell outline's own back
  edge misread against green. Eyeballing a screenshot is a measurement too.

**M6d-2 — measurement: done 2026-09-01.** Five questions, five answers, and **the two that
mattered were not the two the plan expected.** Suite unchanged; the default headless run unchanged
to the digit, which is what an instrumentation pass has to prove first. Five things worth keeping:

- **The headline dissolves: entering a dug world is indistinguishable from generating it.** At a
  standing start, `first draw` 3.48 / 3.43 s loading against 3.46 / 3.43 generating; moving at
  6 m/s with 71 chunks loaded and 44 columns restored, 3.84 / 3.82 against 3.82 / 3.80. **The
  load-versus-generate gap is smaller than the rep-to-rep gap.** But **176 stored chunks is the
  most this world could be dug to** against 1,521 tracked, so the predicted 2,505-chunk crossover
  is confirmed in *direction* and not in fact — the flythrough is a straight line, so passes
  deepen one trench (99 → 104 → 112 → 123 → 176, asymptotic). **Do not retire the concern; it has
  moved from "predicted to bite at 2,505" to "measured not to bite at 176".** **Retired 2026-09-03 for the uniform shape**: the played world has 4,584 stored chunks, 953 loaded at a standing start with light settled 5.97 s against 5.88 s generating; the trigger is now thousands of *non-uniform* stored chunks.
- **The box-difference sweep costs two to four times the whole pipeline update, and no profile
  since M2d has measured it.** `MoveViewer` is called from `_Process` *before* the bracket that
  produces `update ms`. Now timed as **`viewer ms`**: 7.1–9.0 ms mean and 15.3 max at r4h/2v,
  13.2 / 19.6 at r6h/3v, against `update ms` 2.3–4.1 mean and a 16.7 ms frame. **`main %`
  understates the main thread and always has**, being computed from `PipelineUpdate` alone.
  Sub-linear in radius — 2.09× the chunks for 1.47× the sweep. **What it is spending is
  deliberately not chased; light grids and sky columns are the suspect and it wants its own pass.**
- **`Forget` becoming a write is a non-event, and the design answer is why.** A 60 s dig costs the
  sim thread **2.4–3.6 ms in total** against a worker's 22.6–25.9; at 40 m/s, 2.8 ms against 37.4.
  **The burst the plan feared never forms** — largest batch across every run is **6**, not a 37×37
  shell, because `SubmitSaves` runs every frame with one batch in flight. That comment was written
  as a transaction argument and is the burst bound too. **The control is the load-bearing half:**
  `viewer ms` with no world open overlaps the figure with one open, so the sweep predates M6.
  `SaveQueueMillis` and `SaveWriteMillis` are printed side by side and **never summed** — if they
  converge, the write has come back onto the sim thread and nothing else in the report would say so.
- **`Compact()` stays off and the save file is half what the plan feared.** Re-measured: 1,276% of
  the write for 0.0% on a carved chunk, 888% for 7.5% on a stripped one; compression **22–31×**,
  clear of the ~3× revisit trigger. **480–515 B per chunk blob** against a predicted ~1 KB — the
  played world is 36 KiB, a four-pass dug world 96 KiB. **189 B and ~500 B are different
  quantities**: the bench's is the marginal cost of variedness, the game's a whole stored chunk
  with its sky column.
- **Two instrument defects, and one of them produced a wrong reading.** `PlacementLines` printed
  per-click voxel counts at `F0`, so an arm placing 0.40 voxels a click rendered as `0 placed` —
  the print-the-denominator habit failing by *absence* rather than by being unreadable. And the
  placement arms run at 6 m/s placed nothing at all, because `--place` aims along the look
  direction while the flythrough holds the camera level 4 m above the surface: **a moving placement
  profile is not a thing this instrument can produce.**

**M6d-1 — precision placement and the modifier keys: done 2026-08-31.** `Place.Voxel` mirrors
`Carve.Voxel`, and **Alt is the precision modifier for both verbs** — Alt+left takes one voxel,
Alt+right places one. Suite 724 → 727. Four things worth keeping:

- **It went first rather than last, because M6c's play answered M6d's own question early.** That
  play item asks *"whether four shapes are the right four"*, and cube stairs cannot be walked up:
  a riser is a metre against M4b's half-metre step. Judging feel on a knowingly incomplete verb
  spends the play re-finding something already written down.
- **The key swap closes a note recorded at M3d and left pending for two milestones.** Ctrl is also
  FlyCam's descend, so precision mining while flying sank the builder ~1.8 m between chips; Alt is
  *crawl* in fly mode, so "slow down to do fine work" pairs rather than fights. The third thing
  neither note anticipated: **one modifier meaning precision across both verbs.**
- **Corrected at M6d-2: precision does not cost more per click.** M6d-1 compared *means* — 0.077
  against 0.040 — and over seven reps precision's **median** click is 0.004–0.007 ms against the
  brush's 0.013–0.030, two to four times *cheaper*, as one voxel against sixty-four predicts. The
  mean is a tail: precision's worst click runs **200–300× its own median** against the brush's
  2–5×. **The inferred mechanism was right and is what the tail is made of** — 0.19 chunk clones
  and 0.06 relights per click while placing 0.40 voxels, against the brush's 0.00 and 0.00 for
  7.73. **So M3c's argument for the brushes staying the default survives, on amortised-clone
  grounds rather than the per-click ones it was restated on.**
- **`--preciseplace` exists to test the wire.** Every layer had a Core test and the join between
  the held key and `Place.Voxel` had none, which is M5c-2's hole exactly.
- **Amended the same day: Ctrl and Shift had been swapped from Minecraft since M2.** It is now
  Shift = sneak / descend and Ctrl = sprint / fly faster, which **removes the cause** of the M3d
  note rather than dodging it. **Sneak is a physics rule, not a keybinding**: a grounded move that
  would leave the body unsupported is refused, per axis so you slide along a lip rather than
  sticking to it, and "supported" is step-down's own probe called the same way — a stricter
  test would stop the player at the top of a staircase they could safely walk down. Two fixtures
  in its battery were wrong about the physics rather than the reverse: a sneaking body **hangs
  most of itself over the drop**, and **a gap exactly the body's width is walked across**. **M6 is planned (2026-08-31) and under way: SQLite save/load, brush placement,
volumetric inventory — v0.1 playable sandbox**, and it is what fixes the one thing play kept
running into, that an edited chunk which unloads loses its edits. See
[docs/M6-PLAN.md](docs/M6-PLAN.md), split M6a–M6d. **The game saves as of M6b-3 (2026-08-31),
played and confirmed** — closing the window and re-running loads the last world. **M6c is split
three ways and all three are built: the brushes, the inventory, and the binding. **Right mouse
places, B cycles the brush, V cycles the material, and what you mine is what you build with.**

**M6c-3 — the binding: built 2026-08-31.** `PlaceController` is Godot-free for the reason
`MiningController` is, so `--place` drives a whole profile window headlessly; `--stock=` seeds the
inventory and `--brush=` picks the shape. Suite 719 → 724. Four things worth keeping:

- **A brush costs `place ms` mean 0.038, max 0.186**, against a 16.7 ms frame and against mining's
  0.086 mean for a sphere half the size. **Do not build anything to make placement cheaper.**
  Streaming is untouched: 1521 / 165 / 1926 at 404 quads.
- **The round trip works end to end.** Build, quit, reopen: the inventory comes back to the litre,
  the structure loads, and the player is put back where they were. The reopened run spawned at
  **4.00 m instead of 1.25 m**, because the previous one had built a cube on the spawn column —
  nothing was asserting that, and it is the most convincing evidence in the set.
- **Two outlines, and the second one needs a screenshot to verify.** A brush lands on the 1 m
  lattice wherever inside a cell you pointed, so without an outline of the cell the snap reads as
  the game ignoring the aim. Headless has no renderer, so a highlight that scaled wrongly would
  look identical in the logs — the M5c-2 lesson one layer out.
- **`WriteWorldHeader` was a second sim-thread caller of the store with no drain.** Correct while
  the only call was at open, before a single job existed; wrong the moment it went on the autosave
  timer. **Third time this milestone that a safety argument turned out to be about the caller
  rather than the code.**

**M6c-2 — the inventory: done 2026-08-31.** `Overburden.Core/Inventory/VolumetricInventory` holds
what the player carries; `Carve` credits it per material as it removes, `Place` debits it per voxel
as it fills, and the world header carries it across a quit with the player's position. Suite 689 →
719. Four things worth keeping:

- **Stored as voxel counts, presented in litres, and 64 voxels is exactly 1,000 L.** A voxel is the
  only granularity anything can enter or leave the world in, so counting is exact; litres in a
  float would drift and "places what it can" would stop having a clean answer. **Revisit trigger:
  the first quantity that is not a whole number of voxels**, most likely M8's fluids.
- **The credit is per material inside `Carve.Take` and the debit is inside `Place.Put`, both after
  the write, with the stock consulted last.** A bite through dirt with a stone seam takes two
  materials and `Removed` is one number, so crediting from the result would have to guess; and
  charging before a voxel is known to be fillable would make a wall built against a cliff cost the
  volume of the cliff.
- **Header version 2, and version 1 is migrated rather than refused.** The policy: **an additive
  version migrates, a version that changes what a field means is refused.** It is not what M6a
  argued against — that was a reader defaulting a field the writer meant to write. **Verified
  against the v1 world Matthew had been playing**, which opens, migrates and loads its chunks.
- **Seventeen break-recipes, four of which needed the test fixed rather than the code, and three
  were one new variety: a corruption test that throws from a different check than the one it
  names.** The header validates in layers, so `Assert.Throws` is not evidence the layer you meant
  was reached — only a battery says which guard fired.

**M6c-1 — the brushes: done 2026-08-31.** `Overburden.Core/Building/` holds `BrushCell`,
`BrushFacing`, `PlaceResult` and `Place` — cube, slab, ramp and sphere on the world's 1 m lattice,
writing through `IVoxelEditor.SetVoxel` one voxel at a time exactly as `Carve` does. Suite 670 →
689. Five things worth keeping:

- **A placed ramp is walked up by the physics that already exists, and that is a test rather than
  an argument.** A step rises 0.25 m against M4b's 0.5 m step height, so a staircase needs no new
  collision shape — both halves are pure Core, so it costs a test rather than a play-test. **It
  failed first and the failure was the test**: the body climbed, walked off the end of the landing
  and fell back to the floor, reporting feet at 1.0 m, which reads exactly like "it never climbed".
- **Only air is replaceable, and the argument is the hardness gate rather than taste.** A brush
  that overwrote material would delete anything without mining it — place dirt into a bedrock
  voxel, mine the dirt with a `hand_pick`, and the bedrock is gone, which makes M3c's gate
  bypassable by the other verb. Placing air is refused outright for the same reason. **Consequence
  that corrects a source comment: placement never reaches `SetVoxel`'s "material is already there"
  early-out**, which that comment predicted it would, so the early-out now has no caller that fires
  it at all — M4a's zeroable-epsilon category, third outing.
- **`IsAccounted` was one line from being true by construction.** The first tally derived
  `Candidates` from the sum of its own buckets, so the invariant could never fail and every test
  asserting it would have passed forever. The shape counts a candidate, the gate decides about it,
  and a `continue` between them now fails eight tests.
- **Nine break-recipes, eight caught; the hole was a fixture.** The test that exists to catch a
  divergent copy of the sphere used an arbitrary centre and radius — under which no voxel centre
  lands at exactly the radius, so `<` and `<=` agree everywhere. **And mutating the shared
  `VoxelSphere` is invisible to that test by construction**: both sides move together and still
  agree, so the only recipe that means anything against a shared definition is the one that
  *un*-shares it.
- **`BrushCell` is deliberately not `LightCellPos`**, which is also 1 m and shares its origin.
  The 1 m light cell has a live M5 revisit trigger, and halving it would silently halve the brush
  lattice — moving where every brush in the game lands, from inside a change about lighting.

**M6b-3 — the session: done 2026-08-31.** `Main.cs` opens a world, seeds the registry from its ID
table, hands the store to the pipeline, autosaves every 30 s and writes on quit. Suite 668 → 670.
**It was wiring, and the wiring found two defects in Core** — both on the synchronous save path,
which nothing had run outside a test:

- **`IWorldStore`'s "one caller at a time" contract had a second caller.** M6b-1 justified the
  contract by *every call arriving on the serial lane*; M6b-2 added `SaveAll` on the sim thread and
  justified it by *quitting being when the scheduler stops*. Both are reasonable and together they
  are false the moment `SaveAll` runs anywhere else — which is what an autosave timer is. **The
  obvious hazard is a save in flight; the real one is a load**: three job types touch the store and
  `LoadChunkJob` is submitted once per stored chunk entering the box, so the lane is usually inside
  the store while the player moves. Against SQLite an overlap is two threads binding parameters on
  one shared prepared statement. `SaveAll` now drains the lane first, and **`_ExitTree`'s order —
  pipeline, then scheduler — is load-bearing twice**, because disposing the scheduler first would
  drop a queued save and a dropped save is a deleted tunnel.
- **The synchronous save had no fault path.** `ApplySave` has restored a failed batch since M6b-2;
  `SaveAll` had none of it while clearing the dirty flags *before* the write, so a store that threw
  left the world marked saved and never written. **The answer existed, in the twin of the method
  that needed it.**

The verification is a dig, a quit and a reopen, headless: **9 chunks loaded, 1 column restored,
414 quads per drawn chunk against a pristine 404** — the excavation comes back in the geometry
rather than only in a counter, and a longer dig moves `drawn` from 165 to 166 as well. The default
run is unchanged to the digit (**1521 viewed / 165 drawn / 1926 jobs** at 404 quads), because a
world with nothing saved takes the generate path throughout. Autosave measured against real SQLite
at **13.4 ms** for the first write of five chunks and a column, then **3.6–4.3 ms**. Two things stay open: **the windowed close is verified by proxy**
(`--quit-after` is `SceneTree::quit()`, the same teardown), and **the Godot export with
SQLitePCLRaw's natives is still unverified** — running resolves `e_sqlite3` through the generated
`.deps.json` under `EnableDynamicLoading`, which is not the same question.

**M6a — the blob and the world file: done 2026-08-31.** `Overburden.Core/Persistence/` holds
`ChunkBlob`, `SkyColumnBlob`, `WorldHeader` and the shared frame. No SQLite, no file, no pipeline.
Suite 559 → 614. Six things worth keeping:

- **A truncation test found a real defect, and it was non-monotonic.** Raw deflate carries **no
  integrity check at all**: cut a blob after the last byte the decompressor needed — the four-byte
  Adler and the byte holding the end-of-stream marker — and it inflates to exactly the declared
  length and reports a clean end, so every check passes. Removing *fewer* bytes is caught. The fix
  is two complementary things: **the frame now declares the stored length as well as the
  uncompressed one** (catches truncation, by arithmetic, before decompression), and **zlib rather
  than raw deflate** (catches bit-rot inside an intact blob).
- **`Compact()` does not run on serialisation, and the M1 comment saying it does is corrected.**
  Measured over 2,048 real chunks in Release: on a carved chunk it costs **13× the entire write and
  saves nothing**; on the case it exists for, **10× the write for 7.5%** of a blob already
  compressed 22×. `ChunkBlob.Write` defaults to `compact: false`. **The first measurement of this
  was wrong and the fixture was the trap** — carving air cannot create a dead palette entry, so the
  bench priced a fixture that could not benefit. Same family as M5c-1's one-voxel bore.
- **The save file is 5× smaller than the plan estimated: 189 B per varied chunk**, 18 MiB per
  100,000 stored chunks. And **deserialisation is 21 µs a chunk against the plan's assumed
  0.1–0.3 ms**, which half-answers M6's headline worry about entering a dug-in world — the other
  half is SQLite's per-query overhead and is M6b's. Do not retire the concern on one number.
- **`WorldGenSettings` is thirteen properties, not a seed, and all thirteen are written**, because
  an unedited chunk is *regenerated* rather than stored. **Explicit binary rather than JSON,
  because a JSON reader fills in a missing field's default and carries on** — which is exactly the
  silent world-reshaping this prevents. A reflection test counts the record's properties against
  the constant the serialiser was written to, since a forgotten fourteenth field would leave the
  round trip green.
- **Non-negotiables 2 and 3 are tested rather than promised.** `ExportIdTable`/`SeedIdTable`,
  built at M1 and never called, now round-trip through bytes across a mod being removed and
  reinstalled; entry 0 must be `minercraft:air`, refused at read *and* write. M3c's recorded
  "validate chunk contents at load: M6" is closed — `ChunkBlob.Read` takes the `MaterialTable` and
  validates all three storage kinds, **required rather than a skippable second call**.
- **The oracle is voxels, not representation, and its limit is stated: a round trip runs the same
  writer and reader, so a symmetric bug passes.** Two tests share no code with the half they check
  — a written blob decoded by hand, and a hand-built blob given to the reader. The first pins the
  one mistake the type system cannot prevent: writing the palette as per-chunk indices rather than
  world material IDs, which round-trips perfectly inside one session.

**M6's structural point: every chunk so far has been recoverable from nothing, so `Forget` could
delete a record inside a thousand-chunk sweep and nothing had to think about it. M6 is the first
milestone that creates state whose only copy is on disk — so the unload path becomes a write**, and
it runs on the sim thread inside `ChunkBox.ForEachDifference`, which at r16 hands it a 37×37 shell
in one frame. A persistence bug destroys work that cannot be recreated and usually surfaces a
session later, so this is the first milestone where the tests are the only thing between a defect
and permanent data loss. Second change: **a chunk gains a third source of voxels — memory, then
disk, then the seed — and the only failure that matters is consulting them in the wrong order.**

Six things the planning pass settled or found, each expensive to rediscover:

- **SQLite goes in a new `Overburden.Persistence` assembly, not in Core.** Core has **no NuGet
  dependency at all** after five milestones, which is what makes non-negotiable 1's multiplayer
  seam more than a slogan. `IChunkStore` in Core plus an in-memory implementation is M1's
  `IContentSource` shape exactly. **`ChunkPipeline` takes an `IChunkStore?`, null meaning no
  persistence, so all 559 existing tests keep passing unedited.**
- **Saving is a job on the serial lane M2a built for it and nothing has ever used** — and it is
  **the one job that must not be cancelled**. `OnChunkUnviewed` cancels every job whose chunk
  nobody wants, which is right for work we can rebuild; a cancelled save is a deleted tunnel.
  `JobResult` reports a job still queued at shutdown as Dropped, so **quitting with saves queued
  loses them, and that is one line of `Dispose`.** A save job must also set `VoxelsSharedWithJob`,
  because a chunk can be revived and mined while the serialiser is mid-copy.
- **`DirtySinceSave` is a second flag and it is the `NeedsRemesh` problem a third time.**
  `HasEdits` means "differs from the seed" and is never cleared; the new one means "differs from
  disk", clears at *submission*, and must be restored by `StepBack` on a drop. Conflating them
  loses exactly the last edits before an unload.
- **The ID table goes in the same database as the chunks, in the same transaction.** The obvious
  layout — a JSON world file beside the chunk DB — is the one arrangement that can desynchronise.
  M6 is where `ExportIdTable`/`SeedIdTable`, built at M1 and never called, get their first caller,
  and where **non-negotiable 2 gets its first real test in five milestones**.
- **The read side is the half most likely to have been costed wrong, and it is named for that
  reason.** Generation fills r4h/2v in ~89 ms of wall clock across eight workers; loads run **one
  at a time** on the serial lane, so at 0.1–0.3 ms a load the crossover is 300–890 chunks. **A
  world dug into heavily can be slower to enter than one generated from scratch.** Batched loads
  are the named lever and deliberately not built.
- **`Compact()` gets its first caller ever and nobody has costed it.** Its comment has said "this
  runs on serialisation" since M1 with nothing calling it — a **prediction, not a decision**.
  Measure it at M6a, then keep it or correct the comment.

The player walks, falls, and cannot outrun the streamer; voxels have ambient
occlusion; and **lighting is end to end — the sun is gone, the grid is the light, and a lamp
glows.** **M5d-2 is played and M5 is done (2026-08-31) — "happy with the current state": a mine reads
as a mine, and AO earns its quads** — one chamber runs from lit rock to *actual black* and the falloff reads as depth, which
is DESIGN's "single biggest visual payoff" confirmed by eye rather than argued. **The fog fix is
load-bearing for that verdict**: before it, the black was the fog's grey-blue and the gradient
was distance rather than light. **Torchlight works and is warm**, which is the only test in the milestone that
could prove the two light channels stay independent all the way to the screen — every other one
passes identically with both packed into one byte.

**The light cell stays 1 m, closed by default rather than probed.** The recorded trigger was a
`hand_pick` bore reading blobby, and that specific play never happened — the entangled miner digs
chambers, and a chamber is many cells wide, which is the case a 1 m grid is good at. So 1 m
stands because nobody reopened it, which is **silence rather than evidence**, and the trigger
stays live. M5d-1 measured everything that could be measured and found one thing the plan did not
predict — see below.
Terrain streams around the flycam: worldgen and greedy meshing run on worker threads over palette-compressed storage,
with materials loaded from JSON, and chunks load and unload as the viewer moves.

**The default view radius is 6 as of 2026-09-03 — 48 m, raised from 4 on Matthew's request.** The
new default headless baseline is **2601 viewed / 325 drawn / 3446 jobs at 411 quads**, settled in
about 8 s rather than 5, and 3446 = 2601 generates + 845 meshes where 845 is exactly the 13×13×5
visible box. **Every streaming figure recorded before that date is at r4h/2v and stays reproducible
with `--radius=4 --vradius=2`** — which is the only reason M2d's through M8's numbers remain
comparable with each other, so quote the radius with any figure from here on.

Headless from a standing start, r4h/2v + 2 margin on 8 workers — **the historical baseline, now
reached only by passing `--radius=4 --vradius=2`**:

```
1521 viewed (1521 tracked), 165 drawn, 0 in flight, 0 queued
1926 jobs done / 0 dropped / 0 faulted   — settled inside 5 s
quads 66704 (404 per drawn chunk, AO on, light on)
1521 derives / 1521 tracked (299 uniform, 20%), 585k cells visited,
169 sky columns, 0 relight remeshes, light settled
```

1926 = 1521 generates + 405 meshes, and 405 is exactly the 9×5×9 visible box. Drawn was 166
before M2d-2; the cave lattice carves marginally less, so one chunk now meshes to nothing.

**M5c-1 left every one of those numbers unchanged**, which was the point — light joined the
barrier without moving the streaming contract, so M5d stays comparable with M2d and M3d.
"Settled inside 5 s" is only still true because the light budget was raised from 2 ms to 4 ms;
at 2 ms the world had generated at 5 s and drawn nothing. One derive per chunk means the rebuild
rule is not thrashing, and 0 relight remeshes during the fill means the priority ordering is
intact.

**That paragraph is true of a standing start and false the moment the viewer moves, which is
M5d-1's finding.** Light derives on **one sim thread** against generation's eight workers, and a
chunk that is not lit does not mesh — so the symptom is not a frame spike, it is `drawn` going
to **zero**. At r4h/2v in Debug, which is the build `start.cmd` runs:

```
speed   drawn   first draw   main thread   derives/tracked
 6 m/s    160      3.83 s        35.5%          2.8x
12 m/s    188      5.21 s        60.5%          4.4x
20 m/s      0      never         76.6%          6.0x
40 m/s      0      never         76.2%          4.4x
```

**The cliff is 12–20 m/s and the flycam's sprint is 6 × 5 = 30 m/s**, so holding Shift in fly
mode stops the world drawing. M3d's streaming-only control at r6h/3v and 8 m/s was **0.80% of
one core**; the same configuration today is **69.5%**. Light being on the sim thread is a
deliberate and well-argued M5 decision, so the *kind* of cost is by design — the *size* is not,
because M5-PLAN costed the propagation and said nothing about the derive, and the derive is the
whole of it. **~12% in Release**, so the cliff is a Debug cliff in a build people play. **Two
levers are measured and neither is built** — see TODO.

M2 is split into four parts in [docs/M2-PLAN.md](docs/M2-PLAN.md). The prerequisite
reading of godot_voxel is done and paid for itself: it caught that the job queue had been
specified as `System.Threading.Channels`, and a FIFO can neither reprioritise queued work
as the player moves nor drop work that went stale before it ran. See
[docs/M2-REFERENCE-NOTES.md](docs/M2-REFERENCE-NOTES.md).

**M2a — job scheduler: done 2026-08-27.** `Overburden.Core/Jobs/` holds a flat list
re-sorted at most once per 200 ms by whichever worker takes next — **not a timer: measured
2026-09-06 at 4.6–5.0 sweeps/s in every arm with the longest gap under 305 ms, because idle workers
poll every 100 ms; the profile's `sweeps` line and the periodic `M2a:` line say so** — priority
packed into one `uint` of four byte-bands, real
cancellation tokens, and dedicated worker threads. Every submitted job reports exactly one
result — that invariant is fuzzed, and a job that vanishes silently would leave its chunk
flagged as work-in-progress forever. Nothing submits jobs yet; streaming at M2c feeds it.

**M2b — worldgen: done 2026-08-27.** `Overburden.Core/Worldgen/` holds splitmix64, gradient
noise, a data-driven strata table, and a generator that is a pure function of seed and
position. Measured over 2,048 chunks: 4.0 MiB against 128 MiB naive, 68% of chunks uniform.

**M2b's `6.55 ms/chunk` was a Debug number** and said so nowhere — M2d-2 re-measured the same
code at 2.23 ms/chunk in Release. The cave share it reported (80%) was right; only the scale
was wrong. The game project builds Debug, so the streaming profiles are Debug too and are
consistent with each other — but never quote a generation figure without its build.

**M2c-1 — streaming policy: done 2026-08-27.** `Overburden.Core/Streaming/` holds `ChunkBox`
(six-way box difference, fuzzed against brute force for disjointness *and* exactness),
`Viewer` with separate horizontal and vertical radii, a chunk state machine that throws on
an illegal transition, and `ChunkStreamer` — refcounts across viewers, box difference on
move, and the stage barrier. M2c was split up front rather than halfway through, so this
part has no threads and no jobs.

Two things from it worth knowing before touching streaming:

- **The loaded region exceeds the visible one by two chunks, not one.** The mesher's padded
  copy needs the first ring; the cross-chunk feature stage needs the second, because a chunk
  reaches stage 2 only once its own neighbours have terrain. ~41% more chunks at radius 16.
  It is **not** a tuning knob: M2d measured margin 1 at r4h/2v as identical to margin 2 at
  r3h/1v, because the barrier applies whether or not stage 2 places anything.
- **Meshability is derived from the barrier, not tracked.** A margin chunk fails it by
  construction, so the meshable set lands exactly on the visible box with one refcount and
  no shell bookkeeping. Don't add a second refcount to "fix" the margin.

**M2c-2 — wiring: done 2026-08-27.** `ChunkPipeline` owns the work list, job submission,
result application and cancellation; `GenerateChunkJob` and `MeshChunkJob` do the work;
`Main.cs` is now a thin Godot shell that reports where the camera is and turns finished
meshes into RIDs. Three things about it that are easy to undo by accident:

- **Advancing one chunk pushes its 26 neighbours.** A chunk finishing terrain may be the
  last thing its whole neighbourhood was waiting on. Miss that push and the pipeline stalls
  silently, everything idle, nothing obviously wrong.
- **`onChunkViewed` fires on revival, not just creation.** A chunk retained past its last
  viewer keeps whatever state its cancelled job left it in, so coming back has to announce
  itself or that chunk sits behind a dead job forever.
- **`ViewedCount` and `TrackedCount` are different numbers.** A chunk with a job in flight
  is retained after its last viewer leaves. A gap between the two that does not close means
  a job never reported.

**M2d-1 — unload path and measurement: done 2026-08-27.** `MeshFreeQueue` releases meshes a
few per frame with the budget scaling to the backlog, and `VoxelRenderInstance` now disposes
its `ArrayMesh` — it never did, so the expensive half was left to the GC. `StreamingProfiler`
plus a scripted flythrough and `--profile --radius --vradius --margin --speed` make a sweep
one shell loop.

What the numbers changed:

- **The load margin is not a tuning knob**, and three docs said it was. Margin 1 at r4h/2v is
  identical to margin 2 at r3h/1v on every metric, because the barrier applies whether or not
  stage 2 places anything. Keep margin 2; view distance is the knob.
- **Main-thread streaming cost plateaus near 3% of one core** from r6 to r12 — bounded by
  worker hand-back rate, not box size.
- **Memory is mesh-dominated, ~46 KiB per drawn chunk** against ~2 KB of voxel storage. So
  horizontal radius is expensive and vertical is nearly free, which is the measured argument
  for separate radii.
- **Caves cap sustainable view distance.** r12 draws 205 chunks at 40 m/s and 2,245 at 6 m/s
  — a speed effect, not a radius ceiling — and the constraint is generation throughput, of
  which caves are 80%. M2d-2 acted on this and confirmed it.

**M2d-2 — cave optimisation: done 2026-08-28.** Cave noise is sampled on a world-anchored
lattice every `CaveLatticeStep` voxels (default 4) and interpolated in between, so a chunk
costs 729 fBm evaluations instead of 32,768. Generation went **2.23 → 0.47 ms/chunk** in
Release, and caves fell from 83% of generation to 20%. `Overburden.Bench` is the repeatable
harness that measured it; `--cavestep=1` restores per-voxel evaluation for A/B runs.

Four things about it that are easy to undo by accident:

- **`IsCaveAt` and `GenerateTerrain` share one definition of the field.** The query
  interpolates eight fresh corners, the generator reads a buffered lattice, and they agree
  bit for bit — same operations in the same order, pinned by an equality test. Two
  definitions would drift, and the symptom is mining a wall that is already air.
- **The step must be a power of two dividing 32.** That is what makes a chunk boundary a
  lattice point, which is what makes independently generated neighbours agree. The
  constructor throws otherwise; a seam is invisible until it is in a screenshot.
- **Interpolation only ever removes caves, never invents one** — measured, not assumed.
  Linear interpolation cannot exceed its endpoints, so peaks inside a cell get clipped, and
  a threshold at p95 selects exactly those peaks. So **the step is a cave-volume knob**: step
  4 costs 10% of cave volume, step 8 costs 37%. Voxel disagreement alone hides this.
- **Linear interpolation, not the quintic fade.** The fade belongs inside gradient noise
  where flattening at lattice points is the point; as a resampling kernel it is a
  first-order error where plain linear is second-order.

**Mesh-apply budget: done 2026-08-29.** `ChunkPipeline.Update` applies finished jobs under a
2 ms time slice and leaves the rest for the next frame — the second of the two budgets
M2-PLAN specified. Time-sliced rather than counted, because unlike freeing, the cost is spent
inside our call and can be measured as we go.

Read the worklog before touching it, because the measurement said two awkward things:

- **A time slice cannot bound the tail below the cost of one item.** Update max stays at
  3.3–4.7 ms whether the budget is 0.2 ms or effectively infinite — one mesh conversion is
  atomic. The budget bounds the average and the p99, not the worst case.
- **The 27.6 ms frame that motivated it was not mesh application**, does not reproduce, and
  the worst frame measured came from the *most* tightly budgeted run. **Frame time in
  headless is the wrong instrument** — use `update ms`, which times the call directly. This
  is the second time that trap has been walked into.

**M3a — the edit path: done 2026-08-29.** `ChunkPipeline.SetVoxel` writes a voxel, clones the
chunk first if a job holds it, and marks every chunk whose mesh that invalidates. This closed
both recorded revisit triggers at once — M1b's "no concurrent writer until M3" and M2a's "the
worker-side padded copy is safe because nothing writes yet".

Three things about it that are easy to undo by accident:

- **A `VoxelChunk` handed to a job is immutable from then on.** `Neighbourhood()` marks all 27,
  and an edit finding the flag set clones instead of writing. Mutating in place would not show
  a worker a stale voxel — `VoxelChunk.Set` can promote the representation and swap the backing
  arrays under a reader mid-copy, which is a `NullReferenceException` on a worker thread.
- **`SetVoxel` returns early when the material is already there.** That is not a micro-
  optimisation: a sphere carve rewrites air as air across most of its bounding box, and each
  one would otherwise clone a chunk and dirty up to eight neighbours.
- **`NeedsRemesh` survives a mesh completing.** An edit that lands mid-job produces a mesh built
  from the pre-edit snapshot, so `Complete` pushes the chunk again — nothing else would, because
  `Advance` refuses a chunk with a job in flight.

**M3b — the raycast: done 2026-08-29.** `VoxelRay.Cast` (Amanatides & Woo) over `IVoxelWorld`,
which `ChunkStreamer` implements. Returns the hit voxel, the face normal, the entry distance
and the last empty voxel before it — the last one is where M6's placement goes.

- **`Blocked` is not `Missed`.** An unloaded voxel is an unknown, not an empty one. Collapsing
  them lets the player aim through the edge of the loaded world at a chunk that does not exist,
  where the edit vanishes with nothing to report.
- **Its fuzz is a ray/AABB slab test, not a sampler, and that was a correction.** A sampler is
  less exact than the DDA — a ray can clip a voxel corner for less than one sample step — so
  the first version failed on the code being right.

**A working rule this milestone earned twice: watch a fuzz test fail before trusting it.**
M3a's hung instead of failing; M3b's first oracle was wrong in the subject's favour. Neither
would have shown up in a green run. Both now say in a comment what breaks them and how fast.

**M3c — the carve: done 2026-08-29.** `Overburden.Core/Mining/Carve.cs` takes a sphere of
material out of the world, gated per voxel by hardness; `ToolDefinition` and
`mods/minercraft/tools/*.json` make a tier content rather than a constant; `MaterialTable`
finally projects `Hardness`, which `MaterialDefinition` has carried unread since M1.
`IVoxelEditor` is `IVoxelWorld` plus a write, and `ChunkPipeline` implements it.

Four things about it that are easy to undo by accident:

- **The distance test is the definition; the bounding box is only an optimisation.** The box
  is derived exactly and then widened by one voxel on each side, so float error in the
  derivation can never be what excludes a voxel the predicate would keep. Tightening it
  "because the slack is wasteful" trades a correctness guarantee for a handful of iterations.
- **The carve skips air before it ever calls `SetVoxel`.** Air's hardness is 0, so it passes
  every threshold there is; without the skip a swing would report roughly double what it took.
  This does **not** replace M3a's "material is already there" early-out — that one now
  protects the next writer, which is M6's placement. Both belong.
- **A tool radius is a denial-of-service vector, and a NaN threshold is worse.** The scan is
  cubic, so `"radiusMetres": 1000` in a mod file freezes the game inside a loop rather than
  carving a crater; `ToolDefinition.MaxRadiusMetres` bounds it at load *and* in `Carve.Sphere`,
  because the loader is not the only caller. 4 m, and **labelled an estimate** — 16 m was the
  first answer and did not bound the failure its own comment named. A NaN threshold fails the
  other way, *open*: every comparison with NaN is false, so `hardness > threshold` never holds
  and the gate inverts from "takes nothing" to "takes everything".
- **A viewer leaving mid-remesh used to strand the mesh on screen.** `Meshed → Meshing` is an
  M3 edge, and before it a chunk that was `Meshing` had never delivered anything — so
  `OnChunkUnviewed`'s "is it `Meshed`?" test was equivalent to "does it have a live mesh?" and
  stopped being. `StreamedChunk.HasLiveMesh` is now the flag, and the old geometry no longer
  keeps drawing for terrain that has been unloaded. Mine something, fly away, and it was
  still there.
- **`Unreachable` is not `TooHard`**, the same way `RayOutcome.Blocked` is not `Missed`. A
  swing at a wall and a swing off the edge of the loaded world leave the same-looking hole and
  are not the same event.

**The base game's own files are finally loaded by a test.** `ShippedContentTests` reads
`Overburden.Game/mods/` from disk, rather than the `InMemoryContentSource` fixture every other
content test builds — which had already drifted (it has no grass). It also checks the shipped
*numbers*: that some tier leaves the hardest material standing and some tier can take it,
because the hardness gate is a property of the content and no amount of testing the code
finds a JSON file that has been edited to make it meaningless.

**The working rule is three for three now: a green run is not evidence.** M3a's fuzz hung
instead of failing, M3b's first oracle was wrong in the subject's favour, and M3c had a
pipeline test whose *comment* claimed it caught the border rule — reducing `ChunkEdits` to
owner-only left it green, because the writes already span eight chunks by themselves. Break
the code on purpose, and check the comment too.

**M3d — binding, feel and measurement: done 2026-08-29.** Left mouse mines on the flycam,
number keys picked a tier (**they pick a material from M6d-3; the tier is `T`**), and a
wireframe `VoxelHighlight` outlines the target.
`MiningController` is Godot-free, so `--mine` drives a whole profile window headlessly.

**The milestone's real question is answered: yes, a pick swing fits inside a frame, with two
orders of magnitude to spare.** Mining continuously at five swings a second while flying
r6h/3v at 8 m/s:

```
                     streaming only     + continuous mining
update ms  p99            0.910               1.038
           max            2.036               2.118
main thread               0.80%               1.02%  of one core
```

Against a 16.7 ms frame. **Do not build** M3-PLAN's fallbacks — dirty-region meshing, smaller
mesh blocks, latency hiding — until something asks for them.

Three things about M3d that are easy to undo by accident:

- **A swing is centred on the hit voxel's *centre*, not on the ray's entry point.** The entry
  point lies exactly on a face, so half the sphere starts in air, and how much of it does
  depends on the angle of incidence — a radius written in a mod's JSON would mean something
  different on every swing. `Carve.SwingCentre` holds the decision, in Core, because where a
  swing lands is a game rule a multiplayer server would need.
- **M3a's clone estimate is settled: right per chunk, 7× pessimistic per swing.** Measured 1.2
  clones and 2.4 KiB per swing against a guessed 8 chunks and ~16 KB. Eight is the worst case
  at a chunk corner, not the typical one.
- **Reach is the player's, not the tool's.** 5 m, and a tier says how big a bite it takes and
  what it is hard enough to take, not how far an arm stretches — putting it in the tool JSON
  would guess a field into a format mods have written against. **M4c moved it off
  `MiningController` onto `PlayerController`**, where it now lives; the value is unchanged.

**The headless frame-time trap appeared a third time** and was not believed: a mining run
reported `frame ms` max 19.6 ms against a 6.9 ms control, while `update ms` max moved 0.13 ms
and the spike did not reproduce in two further runs. `update ms` times our own work directly.

**M4, the player, is done** — [docs/M4-PLAN.md](docs/M4-PLAN.md), all four parts: M4a the
collider, M4b motion, M4c binding, M4d feel and measurement. Five things
from the planning pass that are expensive to rediscover:

- **The viewer's position stops being an input.** Until now the flycam was told where to go
  and the streamer followed. From M4 the position is an *output* of a query against the very
  chunks the player decides to load. The cycle is broken by solving step *N* against the world
  as loaded at step *N*−1, and by treating **an unloaded voxel as solid**.
  `ChunkStreamer.TryGetMaterial` returns air *and* false, so ignoring the return value is the
  unrecoverable version of M3b's `Blocked`-is-not-`Missed`: the player leaves the loaded world
  and accelerates away from anything that could stop them.
- **Walking never tunnels and falling always does.** Move-then-resolve is exact below 15 m/s
  — one voxel, 0.25 m, over a 1/60 s step — so the bug cannot appear while movement is being
  tested. Substep at 0.2 m, capped on **displacement, not the box's dimensions**, because a
  0.25 m floor is thinner than the player.
- **Terminal velocity is a streaming parameter in a physics costume**, and **the player's
  width is coupled to the weakest tool's bite**. There is 16–24 m of loaded world below the
  player; `hand_pick` leaves a 0.75 m bore and a 0.5 m box passes with 0.125 m either side.
  Widen the player and the first tunnel a new player digs is one they cannot walk down.
- **Do not delete the flycam**, whatever its own doc comment says. `--profile` assigns
  `GlobalPosition` directly and `AdvanceFlythrough` snaps the camera every frame; a camera
  parented to a physics body does neither, and every measurement M2d and M3d built would go
  with it. Walk/fly toggle, `--profile` implies fly.
- **The trap is in M4c, not in the physics.** M4a and M4b are deterministic, headless and
  fuzzable — the collider's oracle is "seal the player in a room and try to shake them out",
  which is a boolean with no reference implementation and no tolerance.

**M4a — the collider: done 2026-08-29.** `Overburden.Core/Physics/` holds `Aabb` and
`VoxelCollider.Move(world, materials, box, displacement)` — axis-separated resolve, substepped
at 0.2 m, an unloaded voxel treated as solid, `grounded` falling out of the −Y resolve. A pure
function of a box, a displacement and a world.

Five things about it that are easy to undo by accident:

- **The voxel layer is taken from the *moved* box, never from "one past where the box was".**
  The two are the same layer while the box is outside a wall, so nothing distinguished them
  until a test was added for a box that *starts* inside one. The second form finds no new layer
  ahead of an already-overlapping box and waves the move through, walking it out the far side in
  about ten steps. It also cannot teleport the box forward past what the move asked for. This
  was a real hole — four break-recipes were caught by the suite and this fifth one was not.
- **`Aabb` stores a corner and a size, not two corners, and that is what makes standing still
  bitwise stable.** A resolve places one face from the blocking voxel's index and carries the
  size across untouched; recomputing the size as `Max − Min` every step rounds it differently
  each time and a resting player creeps. Same reason `ResolveAxis` returns early on a zero delta
  instead of translating by zero — adding `0.0f` is exact for every float *except* `-0.0f`.
- **The contact epsilon is belt and braces; the half-open interval is the load-bearing part.**
  Setting `ContactEpsilonMetres` to zero leaves the whole suite green, because a face exactly on
  a voxel plane is touching rather than inside and voxel planes are exact in binary. It is kept
  for a voxel size that is not a power of two, and its honest cost is a **knife edge at a gap
  exactly the box's width** — 0.1 mm of the trailing face inside the far wall. The box still
  cannot pass through, and a straight drop down such a shaft is exact.
- **The query-cost guess was pessimistic and needs no follow-up.** M4-PLAN guessed ~400
  `TryGetMaterial` calls per step; measured, a walking step costs **41**, and the worst
  alignment is 63 per substep — 252 at a four-substep fall. **Do not build** the chunk
  memoisation the plan named as the fix. One detail the plan's arithmetic missed: a 0.5 m box
  that is not grid-aligned straddles **three** voxel columns, not two.
- **Float positions cap the usable world at a few kilometres**, long before `VoxelPos`'s `int`
  does — one ULP passes the contact epsilon around ±800 m. **Revisit trigger:** a floating
  origin, not a wider float.

**M4b — motion: done 2026-08-29.** `PlayerMotion` and `PlayerMotionConfig` add gravity, jump,
terminal velocity, step-up and step-down on top of the collider. `Step(world, materials, state,
intent, config, dt)` is a pure function — no clock, no accumulator, no field — and the step
length is an argument, so 60 Hz is Godot's business rather than Core's. Suite 400 → 435.

Six things about it that are easy to undo by accident:

- **The lift is snapped to its exact target, and that is what keeps M4a's epsilon claim true.**
  Nothing caught removing it, because a resting body floats 0.1 mm up and that swamps a few ULP
  of substep accumulation. But M4a *recorded* that `ContactEpsilonMetres` can be set to zero
  with the suite green — and with the lift accumulated instead, zeroing it fails four tests. A
  finding in the previous milestone was one line from going stale with nothing failing to say
  so. Now pinned by a test that places the feet **exactly** on a voxel plane, which is what
  M4c's spawn and a fly-mode toggle both do.
- **The jump apex is 1.1875 m, not the plan's 1.125 m** — `v²/2g` is the continuous answer. A
  jump *replaces* its step's gravity rather than being reduced by it, so the rise overshoots by
  exactly half a step of launch speed. Clearance over a four-voxel ledge is 0.1875 m, and it is
  **rate-dependent**: twice as much at 30 Hz. The apex test asserts one-sided, because the
  direction is the design claim.
- **A step-up settles by exactly what it lifted**, so it is a climb and never a fall. What that
  protects is step-down's distance limit — a settle that could go further gives the same rule
  two answers depending on whether the body happened to climb something first. Also caught by
  nothing until a test was written for it, because walking cannot reach it: 0.075 m of travel
  against a body 0.5 m wide.
- **Removing step-down's non-upward guard breaks jumping entirely**, not subtly: the probe finds
  the floor the jump just left and snaps the body back onto it. Six tests fail. Ground state is
  read from the *previous* step throughout, which is M4a's stated cost, not a bug.
- **Step-up's "the re-attempt must fully succeed" guard has a recorded feel cost.** Hugging a
  wall while walking up a slope stops you dead, because the lifted re-attempt is still blocked
  on the wall. Specified behaviour, safe, and **if M4d says it reads as stuck the relaxation is
  one line** — require the re-attempt to be blocked on no axis the original move was not. **Not
  encountered in the 2026-08-30 play-test**, which walked up hills rather than into corners; the
  case needs provoking deliberately, so that is silence and not evidence.
- **M4d needed a signal M4b deliberately did not build, and the deferral was right.**
  `MoveResult` could not tell "stopped by rock" from "stopped by an unloaded chunk" — exactly
  *steps spent held*, the number that picks terminal velocity. It was left out because an unread
  flag with no profiler consuming it is a number waiting to be believed. **Built at M4d as
  `MoveResult.BlockedByUnloaded`, alongside the profiler that reads it**, and it was right first
  time — while the counter written *without* a consumer in mind, M4d's ground-flicker detector,
  was wrong twice.

Horizontal velocity is **replaced from intent every step rather than accelerated** — no friction,
no momentum, full air control — because acceleration, air control and ground friction are three
separate feel decisions and M4d is where a person makes them. Reach stayed on `MiningController`:
it is not a body dimension. Eye height *is* in the config, because it is the third dimension of
the same body.

**M4c — binding: done 2026-08-29.** `PlayerSpawn` in Core holds the three world rules — where a
spawn lands, whether the world is ready, what to do about a body inside rock — and
`PlayerController` in Game holds the session: which body is driving, and the gate the other one
waits behind. `FlyCam` gains `FlightEnabled`; there is no physics body node and no reparenting.
**F toggles walk and fly.** Suite 435 → 452.

**The prediction was that M4c would be where the surprises were, and it was not.** The binding
was mechanical; both things nothing caught were in the world rules that went into Core.

- **The flycam kept every number, which was the entire contract.** The default headless run
  reports **1521 viewed / 165 drawn / 1926 jobs** — M2d's recorded figures unchanged to the
  digit — so M4d's measurements stay comparable with M3d's. `--profile` and a non-zero
  `--speed` force fly mode *and refuse the F key mid-run*, because handing a scripted
  flythrough's camera to a body invalidates the run rather than making it interesting.
- **Gate, then unstick, then step. The reverse order is the unrecoverable one.** Against an
  unloaded world every unstick candidate is solid, the search runs to its 4 m bound, and a body
  standing in open sky is reported *stuck*. The gate checks every chunk the box overlaps **and
  the chunk beneath each of them** — the layer below because standing on an unloaded chunk is
  standing on something that will not be there. Measured: **13–14 steps, about 0.22 s, held
  for the world** at r4h/2v — it moves run to run with worker scheduling.
- **An unstick lifts and never drops, and that is a decision rather than a consequence.** A body
  with its head in a ceiling goes up through it onto the roof. **Caught by nothing** — every
  buried-body test placed the feet on a plane already, where the downward candidate is the box
  itself and overlaps by construction. `UnstickNeverMovesABodyDownward` closes it, and this is
  the rule M6's placement inherits, up-only included.
- **`Aabb.Translated` versus rebuilding the lifted box is undetectable, and is kept anyway.** No
  failing case exists inside the few-kilometre world M4a's float analysis allows. A
  documentation correction, not a caught bug — the same shape as M4a's zeroable epsilon.
- **Feet exactly on a voxel plane survives exactly one step.** The first −Y resolve places the
  body `ContactEpsilonMetres` clear, so the spawn's 1.25 m is 1.2501 m by the next frame. The
  state exists *only* at the spawn and at a mode toggle, which is why M4b's lift test constructs
  it by hand. Check this before writing any bitwise assertion about a resting body.
- **Order within a step came for free.** Player → viewer → pipeline holds because the step is in
  `_PhysicsProcess` and the rest in `_Process`, and Godot runs a frame's physics ticks first.
- **Reach moved to the player and deliberately did not change.** Still 5 m;
  `MiningController.ReachMetres` is now `required` and defaultless so there is no second literal.
  M4-PLAN's table said 3.5 m and the M3 play-test said 5 m read long — both were feel, and
  retuning in the same pass as the move would have confused the two changes. **Settled by play
  2026-08-30: 5 m stays**, generous on purpose, with **a survival mode as the recorded trigger**
  to shorten it to 3 m or 2 m. The plan's 3.5 m was an estimate and was never shipped.

**M4d — feel and measurement: done 2026-08-30. The milestone's question is answered: no, the
player cannot outrun the streamer.** `--fall` drops the body from height and measures it;
`--terminal` and `--fallheight` make the sweep a shell loop. Suite 461 → 468.

```
terminal 40 m/s, 250 m falls, 20 s     r4h/2v      r6h/3v
update ms p99 / max                 1.05 / 4.57  1.54 / 5.50
main thread                            1.14%        2.27%   of one core
step ms mean / max                 0.015 / 0.35 0.013 / 0.57
substeps / voxel reads per step      3.42 / 13.3  3.41 / 13.3
held on unloaded world                    0            0
```

Five things from it that are easy to undo by accident:

- **Terminal velocity 40 m/s stands, with 2.25× measured margin.** `held 0` at r4h/2v, r6h/1v,
  r6h/2v, r6h/3v and r12h/3v — and r6h/3v survives **90 m/s** with zero held steps. It first
  loses at r12h/3v @ 90 m/s (3 steps, longest 17 ms) and r16h/3v @ 120 m/s (12 steps, 83 ms).
  The arithmetic agrees: a chunk row at r6 is 169 chunks crossed every 0.2 s; at r16 it is 1,089
  crossed every 0.067 s.
- **A falling step costs 16 voxel reads, not M4a's predicted 252**, because `ResolveAxis` returns
  early on the two axes with zero displacement — an early-out added in M4a so a resting body
  would not creep by a rounding error. **A numerical-stability decision that turned out to be a
  performance one**; do not "simplify" it away. **Do not build** M4-PLAN's query memoisation.
- **The flicker detector was wrong twice before it was worth anything**, both times reporting a
  small non-zero number that read as a real defect: it counted **landings** (the landing step
  *is* the first at-rest step), then it counted **held steps** (a body on an unloaded chunk is
  grounded with velocity zeroed, so it looks like rest). `RestingSteps` is printed beside it now,
  and the report says outright when the detector never ran.
- **`MoveResult.BlockedByUnloaded` is a subset of `Blocked` by construction**, so it can never
  contradict it — recorded only on the face that actually stopped the box. Step-down writes the
  literal `NegativeY` rather than masking the probe's set, because a purely vertical probe cannot
  block horizontally at all: `ResolveAxis` returns early on a zero delta. A break-recipe proved
  the mask was dead code.
- **Recorded limitation: the fall profile measures generation, not meshing.** A falling player
  falls through air, so every run reports `0 drawn`. Falling down a shaft you dug — terrain on
  all sides — is not covered, and would need a dug shaft rather than a drop from altitude.

**The headless frame-time trap, a fourth time:** `frame ms` sat at mean 6.90 / max 7.48 across
every run while directly-timed `update ms` max ranged 1.1–9.3 ms. Unrelated numbers.

**M4 has been played, twice, and the headline question is answered: 0.25 m ground walks like
ground.** "Walking uphill occurred naturally, I did not need to jump" — which is DESIGN's claim
that step-up is not a convenience in a quarter-metre world but the difference between walking on
the ground and not, confirmed by a person rather than argued. **Step height stays 0.5 m and the
jump constants stand.** Two things came with it:

- **You "sortof" fit down a tunnel you carve**, and the hedge is exactly what M4-PLAN's
  arithmetic predicts. `hand_pick`'s 0.4 m radius leaves a 0.75 m bore and a 0.5 m box passes
  with **0.125 m either side — half a voxel** — while consecutive swings centred on different
  voxel centres wander the bore's axis. Fits on paper, scrapes in practice. **The cheap lever is
  the tool, not the player:** `radiusMetres` 0.4 → 0.5 gives a five-voxel bore and 0.375 m of
  clearance, and it is one number in one JSON file with no rebuild — which is what M3c made
  tiers content for. Widening the player is the move the plan warns against.
- **Two corner cases stayed unjudged and ordinary walking cannot reach them:** step-up guard 3's
  diagonal wall-hug, and step-down's 0.5 m instant drop. Neither was reported, which is silence
  rather than evidence.

**Mining has two shapes as of 2026-08-30: `Carve.Swing` spheres on a bare click, `Carve.Chip`
takes one voxel under Ctrl.** DESIGN's "mining is sculpting" line stands and now records why:
a single-voxel *default* was proposed and rejected, because `hand_pick`'s sphere is ~17 voxels
and the default would have been **~17× slower to excavate** with the starting tool — in a game
about a mining operation, and against DESIGN's own argument that volumetric inventory exists to
make big excavations productive. Precision is the exception, and an exception is worth a held
key. Four things about it that are easy to undo by accident:

- **`Chip` takes `hit.Voxel` itself, not a sphere centred on it.** So `VoxelHighlight`'s outline
  is literally the voxel about to be removed, rather than the centre of a bite extending past
  it. That is the whole reason precision reads as precise.
- **Same hardness gate, same cooldown.** Precision changes how much a swing takes, never what it
  is strong enough to take, and never how fast it fires. Either exemption would make the
  modifier the *efficient* way to mine as well as the accurate one, and the sphere would stop
  being the default in practice while still being it in the code.
- **One `Carve.Take` holds the gate for both shapes, and that is load-bearing.** Removing the
  hardness check from it fails **9** tests, **seven of them pre-existing sphere tests** — one
  edit breaking both shapes is exactly what a forked copy would not do.
- **`radiusMetres` did not change meaning**, which is the quiet dividend of keeping the sphere
  as the default: the tool JSON is untouched and no format mods write against moved.

**Known and unfixed: Ctrl is also FlyCam's descend.** Fine in walk mode where Ctrl is free; in
fly mode a builder sinks ~1.8 m between chips at the default speed and `hand_pick`'s cooldown.
**Alt is the better key** — free in walk, and *crawl* in fly, so "slow down to do fine work"
pairs rather than fights — and the swap is one line. Left on Ctrl pending a play-test.

**M3 has been played, and it feels good** — the verdict the milestone was actually waiting on,
since it is the first whose output was a feeling rather than a number. `VoxelHighlight` is
verified windowed. One note came out of it: **reach reads a little long at 5 m**, which is
twenty voxels. **M4d settled it 2026-08-30 and 5 m stayed** — generous, and generous is the
right way to be wrong in a sandbox. Unchanged, because it is fine for a creative-mode flycam and
because reach is
the *player's* rather than the tool's — M4 is where something exists to own it, which is the
same argument that kept it out of the tool JSON.

**The entangled miner is a debug bind, built 2026-08-31 to dig M5d-2's test volume.** **Q**
toggles it: point at anything, hold left click, and it eats what it is pointed at however far
away. **As of 2026-09-03 a bare click takes the whole 8 m chunk** — 32,768 voxels, 512 m³ — at
**4 a second**; Alt still chips one voxel, at 40. **And a bare *right* click fills a whole chunk,
same day, same rate, same reach** — so the bite has an undo at its own scale. Three things about it
that are easy to undo by accident:

- **It is deliberately not a tool tier, and that is the design decision rather than laziness.**
  *Reach is the player's, not the tool's* is locked twice — M3d, restated at M4c — because
  putting it in the tool JSON would guess a field into a format mods have written against. A
  tool whose defining property is unlimited reach is the case that decision did not anticipate,
  and for this one tool the reversal is arguably right. So it is a mode on `MiningController`
  with **no `reachMetres` in the JSON and no `ToolDefinition` field**: once that field exists,
  every mod writes against it and the decision is gone for *every* tool. **Making it real
  content is the moment to settle that**, and it wants its own planning pass.
- **"However far away" means "as far as the world exists", about 144 m at r16.** What stops the
  ray is `RayOutcome.Blocked` at the edge of the loaded region — entanglement is not
  distance-sensitive but **the streamer is**. The 512 m constant is a backstop bounding the DDA,
  the same shape as `MaxRadiusMetres` bounding the carve. Aiming at sky costs no swing.
- **It composed with the number keys, and half of that stopped being true on 2026-09-03.** It
  changed *how far* and *how fast*, **never how big a bite or what the bite is hard enough to
  take** — and the bite clause is now gone, because a bare click takes a whole chunk. **The
  hardness clause survives untouched and is the half that was load-bearing**: a chunk bite through
  bedrock leaves the bedrock standing, so `Carve.Take`'s gate stays the only gate in the game
  rather than something a keypress opts out of. Dropping it from `Take` fails **11** tests, most
  of them pre-existing sphere and chip ones — one edit breaking three shapes is what a forked gate
  would not do.
  Measured at **11.1 m mean / 18.9 m furthest reach**, 0.139 ms a swing against 0.083
  un-entangled, and **0.9 relight remeshes per swing against 1.0** — carving a hundred metres
  away is not a different cost from carving at arm's length. `MaxReachMetres` exists because
  swing and voxel counts are identical whether the cast ran 5 m or 90, which is M5c-2's shape of
  hole.
- **From 8/s onward the tier's cooldown no longer bounds anything while entangled**, which the
  4/s version's own comment said the opposite of — `steel_pick` cools in 0.20 s, so entangled was
  once the slower of the two. Nothing shipped is balanced by its rate today; the note is for when
  something is.
- **Raised to 40/s on 2026-09-02 (0.025 s), and the A/B took three fixtures to get right.**
  Same session, r4h/2v, 6 m/s, 30 s, Debug, **both arms drawing**: 587 swings against 1164 —
  **19.6/s against 38.8/s, a true 1.98x**, not the ~30/s the frame-boundary arithmetic predicts,
  because headless frames are ~7.5 ms rather than 16.7 (**expect nearer 30 windowed at 60 fps**).
  Remesh load doubled exactly — 31.5/s to 63.9/s at an unchanged 1.6 per swing — `update ms` mean
  3.808 to 4.815 with the **max unchanged at ~19.9 ms**, and *faster is cheaper per swing* held a
  fourth time (0.079 against the paired arm’s 0.089). **The number to watch has changed from remesh
  load to the main thread: 54.21% to 68.47% of one core**, and the next raise must be measured in
  Release before it is believed. **The named ceiling is now 1.5x away rather than 3x.**
- **Two fixtures failed first and neither looked like a failure.** r6h/3v @ 8 m/s measured the rate
  and **drew nothing**, so `remesh 0.0 per swing` was the absence of a measurement wearing the
  costume of one — it would have been quoted as "the raise costs no remeshes". And **r4h/2v standing
  reported 142 swings in *both* arms**: a stationary entangled miner bores one tunnel and then the
  ray stops hitting anything, so it is **rate-starved rather than cooldown-limited** and the fixture
  made both arms agree. **`--speed=0` exists because M5d-1 wanted the one deterministic arm, and
  that determinism is exactly what makes it useless here** — a seam built to remove one variable
  removed the variable under test.
- **`L` + `Q` is a protected pairing as of 2026-09-02**, reported load-bearing in play: *"hitting Q
  then being able to place a light far off into the distance has been useful during the debug."*
  The old comment said "superseded by the brushes" and **that is false at range** — right mouse is
  bounded by the player’s reach, and reach is deliberately the player’s rather than the tool’s.
  **Revisit trigger: a placement verb that takes a reach argument.**
- **Raised to 20/s on 2026-09-01, and re-measured rather than assumed for the third time.** The
  same surprising thing keeps happening: **faster is cheaper per swing** — 0.139 ms at 4/s, 0.072
  at 8/s, **0.070 at 20/s** — because consecutive swings chew the previous bite, so most of the
  sphere is already air. **What rises is the remesh load, which is per swing rather than per
  voxel**: 32.3 edit remeshes a second against 8/s's 13.9, `update ms` mean 3.651 against 3.245
  and its *max* lower at 10.651 against 11.725. Well inside a 16.7 ms frame. Relight remeshes per
  swing fell again, 1.4 → 1.2.
- **The next thing this rate runs into has a name: one swing per frame, at 0.0167 s.** Below that
  the cooldown stops pacing the tool and the frame rate becomes the balance, so the dig rate would
  depend on the machine. 0.05 s leaves a factor of three. **A 60 s run lands at ~18.9 swings/s
  rather than 20 because a swing can only fire on a frame boundary** — which is that ceiling
  already showing, harmlessly.

**M5, lighting — [docs/M5-PLAN.md](docs/M5-PLAN.md), split M5a–M5d. M5a, M5b, M5c and M5d-1
are built; M5d-2 is the played half and is Matthew's.** The structural point: **light is the first thing here that is not a function of one
chunk.** Worldgen is pure per chunk, meshing takes a padded copy, a carve is local — light
propagates arbitrarily far, and removing one voxel can change light in a chunk that is not a
neighbour. Five things from the planning pass that are expensive to rediscover:

- **4-bit light and the 2-chunk margin are exactly compatible, and that binds them.** Light
  reaches 15 m; the margin is 16 m; a source outside the loaded region contributes
  `max(0, 15 − 16) = 0` at the visible box. So light computed over the loaded region is **exact**
  inside the drawn one, and **the margin now has a third load-bearing reason** — the tightest of
  the three. **Revisit trigger: 5-bit light (31 m) needs a 4-chunk margin.**
- **Sampling light per vertex puts it in the merge key, and a merged quad only interpolates its
  four corners.** So two faces merge iff all four corner shades match — exact when they do, and
  where light varies the quads stop merging. That price is what M5a and M5c measure. The named
  fallback is a per-chunk 3D texture sampled in the fragment shader, where **a light change costs
  no remesh at all**; **do not build it** before the number says so.
- **The vertex carries two light channels, not one, and DESIGN is what forces it.** With the
  directional light gone (settled 2026-08-30) the grid is the only light, and DESIGN's M7 needs
  skylight *on its own* — "wet-surface shader term driven by skylight × recent rainfall" — which
  a pre-multiplied `sky × block × AO` cannot supply. So `shadeSky` and `shadeBlock` are separate,
  AO multiplies into both, and **a diurnal cycle is then a uniform costing no repropagation**.
  Default home is Godot's vertex `COLOR` array; packing both into UV2.y is exact in float32 and
  not in float16, and `AddSurfaceFromArrays` is called with no flags — **verify before relying
  on it.**
- **A 1 m light cell holds 64 voxels and `hand_pick`'s bore is 0.75 m** — the tunnels this game
  is about are narrower than one cell. So opacity is a property of the cell **face**, not the
  cell: light crosses iff some voxel pair across the shared 4×4 boundary is open on both sides.
  "Any opaque → opaque" means torches light nothing; "any air → open" means a 0.25 m wall leaks,
  and 0.25 m is a normal wall here.
- **The vertical radius means there is no top of the world to flood skylight down from.**
  Seeding is analytic off `WorldGenerator.SurfaceHeightAt`, plus a per-column `SkyColumn` map
  that edits lower — which is also exactly the structure M7's rain occlusion needs, and the one
  lighting structure M6 must persist.
- **The oracle is the sharpest available: incremental light == from-scratch light, cell for
  cell.** No reference implementation to get wrong; from-scratch *is* the definition, just too
  slow per frame. Same category as M4a's sealed room. Its fuzz must include edits that break
  through a column's topmost opaque voxel, or the sky-height path never runs and it passes for
  the wrong reason.

**All four of M5's open decisions were settled 2026-08-30, before any code.** Light cells stay at
DESIGN's **1 m** (revisit trigger: M5d plays a `hand_pick` tunnel and it reads blobby);
**`SkyColumn` is built at M5b** with its lifetime in M5c; **the directional light goes**; and
**emission stays 0..15** — which means the brightest lamp any mod can define reaches exactly
15 m for a storage reason rather than a design one.

**M5a — vertex AO and the merge key: done 2026-08-30.** Per-corner AO from the padded chunk,
two shade channels in the vertex, `CornerShade` in the merge key, the diagonal flip, and `--ao=0`
to turn it all off. Suite 468 → 496.

**The number M5a existed to take: AO costs 1.88× the quads**, against a 2–4× estimate — 262 → 492
per drawn chunk at r4h/2v from a standing start, 253 → 472 at r12h/3v, and static memory 94.8 →
150.4 MiB at r12. **So the AO-alone baseline for M5's fragment-shader trigger is 492 quads per
drawn chunk, and M5c must stay under ~984.** Streaming is untouched: 1521 viewed / 165 drawn /
1926 jobs, M2d's figures to the digit.

Six things about it that are easy to undo by accident:

- **The two shade bytes live in the vertex `COLOR` array, and packing them into UV2.y is not a
  precision question — it is structurally impossible.** Godot 4.7 leaves UV2 at float32
  (`AddSurfaceFromArrays` with no flags sets no `ARRAY_FLAG_COMPRESS_ATTRIBUTES`) and applies no
  sRGB conversion to `COLOR` under a `ShaderMaterial` — both probed, not assumed — so the plan's
  stated tie-break said to pack and save four bytes. **But the hardware interpolates the packed
  number**, and the fractional part of an interpolated sky channel lands in the block channel's
  byte: two corners one sky level apart sweep block light through its whole range in between.
  Per-vertex shade exists to be interpolated; a packed pair of independently varying channels
  cannot be.
- **Key equality does not permit a constant shade along a merged run, it forces one.** Adjacent
  faces *share* two corners, and an independently derived shade at a shared corner is the same
  number by definition — so `A.corner1 == B.corner0` for free, and requiring `A.corner0 ==
  B.corner0` therefore implies `A.corner0 == A.corner1`. That is why "merging is exact" is a
  testable property rather than an assertion, and it is what
  `AMergedQuadHasConstantShadeAlongEveryAxisItMergedOn` checks.
- **The diagonal flip compares the total shade, not AO.** Identical at M5a and different at M5c:
  a flat floor with a torch on it has constant AO and a varying gradient, so an AO-only test
  would never fire on the surface most likely to show the crease the flip removes.
- **`EveryQuadIsWoundConsistentlyWithItsNormal` cannot be broken by the flip**, and M5-PLAN asked
  for it as a guard. It reads vertex *positions*; the flip changes *indices*. Right answer for
  that test, hole for the flip — hence
  `BothTrianglesOfEveryQuadFaceOutwardsInEitherFlipState`, which walks the indices and asserts a
  flipped quad was actually seen.
- **The plan's "swap `side1`/`side2`" break-recipe is a no-op**, because
  `(s1 && s2) ? 0 : 3 − (s1 + s2 + c)` is symmetric in its two sides. The meaningful mutation is
  mislabelling *which corner* gets which sample, and that one fails 8 tests. Green run, third
  category: unreachable rather than untested.
- **`AmbientOcclusion.LevelShade` is a feel constant and its distinctness is not.** The ramp
  `[102, 153, 204, 255]` is taste and M5d judges it; the four values being *different* is the
  merge key, and collapsing two would silently merge visibly different faces.

**A test whose comment had never been true, found by M5a and not caused by it.**
`ChunkPipelineTests.AnEditIsWrittenAndTheChunkIsRemeshed` said its carve target was "somewhere
buried", but `FindSolidInterior` meant "not on the chunk boundary" and returned the first solid
voxel in scan order — which has air on three sides, so carving it removes three faces and exposes
three: **a change of exactly zero quads**. It passed anyway until M5a shifted the surrounding
merges and the two totals came out equal. Same shape as M3c's test whose comment claimed it
caught the border rule.

**M5b — the light grid, propagation and the un-light pass: done 2026-08-30.**
`Overburden.Core/Lighting/` holds `LightCellPos`, `ChunkLight` with cell-face conductance,
`SkyColumn`, and `LightPropagator`. `MaterialDefinition.LightEmission` is projected into
`MaterialTable`, `mods/minercraft/materials/lamp.json` ships a full-strength emitter, and **L**
places one. Suite 496 → 518. **Nothing is wired into the pipeline — a lamp placed today is a
pale block that does not glow. That is M5c.**

Seven things about it that are expensive to rediscover:

- **The oracle is better than the one M5-PLAN named, and by a category.** "Incremental equals
  from-scratch" runs the *same add pass* as the subject, so it can only independently catch the
  un-light pass. The propagation invariant
  `V = max(S, max over conducting neighbours (V(n) − attenuation))` has a **unique** fixpoint —
  a cycle of equal values would need zero attenuation all the way round, and the only
  zero-attenuation step is full skylight falling downward, which strictly decreases Y — so
  checking it cell by cell is complete correctness with no propagation in the check at all. Same
  category as M4a's sealed room. Both are asserted, plus a third that fell out free: the
  incrementally maintained sky heights must equal a fresh downward scan of the voxels.
- **The defect that cost seven tests was a missing half of a symmetry.** `Darken` reset a cell to
  its own source and queued the un-light walk when the value had *fallen*, and did nothing when
  it had *risen* — so a placed lamp wrote emission 15 into the derived data and left the cell's
  value at the zero it held as air. The rule is symmetric: **the cell's value ends at its own
  source whichever way it moved.**
- **`Invalidate` is one cell, not seven, and there is a contract behind that.** The caller must
  invalidate every cell `BuildDerived` reported as changed. The case that looks like it needs
  more is a face closing — a walk from A never examines B once the face stops conducting — but
  **conductance is recorded on both cells of a face**, so a changed face always reports both.
  An earlier six-neighbour expansion was provably dead work: removing it failed nothing and
  could not. `SealingAFaceReportsBothOfItsCells` pins the property, and breaking the reporting
  instead fails 11 tests.
- **The skylight free fall is restricted to full brightness, and only one test shape can tell.**
  A shaft straight down from the surface carries *full* skylight, which falls free under both
  rules. The shape that separates them is a horizontal branch off the shaft with a **second
  shaft dropping from its far end**, fed at 10 rather than 15. Without it, every dimly lit patch
  in the world would hang a column of its own light down to bedrock and nothing would fail.
- **A cell is a sky source only if *every* one of its sixteen voxel columns is open, not any.**
  Under-seeding is recoverable — the cell above is a source, the face between them conducts
  through the open half, and full skylight falls through for nothing, so a cliff face is lit by
  propagation anyway. Over-seeding is not: the value is per cell, so a mostly-solid cell declared
  a source pushes daylight into the rock through its own faces.
- **The derived side is 1.5 KB per chunk, not the planned 1 KB, and the extra is not optional.**
  The sky-source bit and the emission nibble are what let the un-light walk zero a cell without
  erasing the lamp it is walking away from.
- **The rebuild scope after an edit is two different shapes, and conflating them costs ~40×.**
  A voxel's *conductance* reaches only its own chunk plus a neighbour across whichever faces it
  actually touches — an interior voxel, 30 in 32, rebuilds exactly one chunk. A change to the
  column's *sky height* is not local at all: it moves the sky flag the whole way up and down that
  chunk column, and nowhere else. **M5c should implement exactly those two scopes**;
  `LightTestWorld` is deliberately written as the thing M5c has to build.

**Dropping the "only if greater" guard in the add pass hangs rather than fails**, as M5-PLAN
predicted. The M3a lesson again — a break-recipe battery has to treat a timeout as its own
outcome or it reads as infrastructure trouble.

**Three more M5c decisions were settled 2026-08-30, before its code**, all found by reading
`LightTestWorld` against `ChunkPipeline` — the pairing M5b wrote that fixture to invite. None is
a reversal; each is a place the plan stated something confidently and stopped one level above
where the wrong answer sat.

- **What sets the `Lit` stage: derived data built *and* the propagator drained.** A global BFS
  has no per-chunk completion event, so `Lit` is a per-chunk stage with one global term.
  **Rejected: a per-chunk count of queued cells** — the obvious way to localise it, and wrong in
  the direction testing does not show, because a chunk with no queued cells of its own can have a
  neighbour's frontier one step from spilling in. Safe to be this coarse only because the drain's
  changed-cell set marks a remesh, which streaming and mining then share.
- **The padded light grid is built on the sim thread at submission, not on the worker.** The
  27-reference gather is safe for voxels because **M3a made voxels copy-on-write**; light is not,
  and the propagator writes into those 512-byte arrays in place while a mesh job runs. One
  1,000-byte copy per job against thousands of clones per drain — 0.6% of the padded voxel copy.
  **It reads as an inconsistency with `PaddedChunk` and invites being "fixed" back.**
- **`NeedsRemesh` means both "stale" and "urgent", and a relight is the first thing that is one
  without the other.** `RemeshUrgent` splits them. A relight also only marks a chunk that has
  actually drawn something — otherwise the initial fill, where every chunk's light changes,
  arrives the whole distance-ordered queue at band3 and M2a's ordering is silently gone. Nothing
  looks broken; the world just loads in the wrong order.

**M5c is split M5c-1 / M5c-2**, on the grounds M2c and M2d were split: M5c-1 is verifiable
headlessly and M5c-2 — the trilinear sample, the shader, the sun going away — is not.

**M5c-2 — the sample, the shader and the sun: done 2026-08-30.** Suite **542 → 556**.
`PaddedLight` is gathered on the sim thread at submission, the mesher samples it trilinearly per
vertex and multiplies occlusion into both channels, `Voxel.gdshader` renders `unshaded` with a
`NORMAL`-derived face tint and a `sky_brightness` uniform, and the `DirectionalLight3D` is out of
the scene. Four things worth keeping:

- **The headline number is the opposite of what the plan expected.** Quads per drawn chunk at
  r4h/2v: **262** AO off + light off, **492** AO on + light off (M5a's baseline), **283** AO off
  + light on, **404** AO on + light on. **Light in the merge key does not cost quads, it saves
  18% of them.** The mechanism is the shade being `occlusion × light`: where light is zero the
  product is zero whatever the occlusion was, so faces that could not merge under AO alone merge
  freely in the dark — which is most of a mine. The merge stays exact. **So M5's fragment-shader
  fallback is retired rather than deferred** — its trigger was ">2× AO alone", i.e. ~984, and
  404 is 0.82×. Fifth named fallback dissolved by measurement here, and the first dissolved in
  the wrong direction. `InDarknessEveryOcclusionLevelCollapsesToTheSameShade` pins it, because an
  ambient floor added to the shade *byte* rather than to the shader would silently undo it.
- **The plan's 10³ padded light grid is one ring short — it is 11³ over −1…9.** The derivation is
  right for a sample taken *at* the vertex, and the sample is taken **half a cell along the face
  normal**, which pushes the far face to 8.5. **That offset is not a refinement:** a face sits on
  the boundary between solid and open, so without it a flat, fully lit floor renders at 7.5 of 15
  everywhere and looks like an art choice. Cell 9's weight there is exactly zero in binary;
  clamping instead would be right today and silently wrong the first time the cell size or the
  offset changes, for 331 bytes.
- **The sun going retired two long-standing open items**: the bright and dark slivers following
  terrain contours (the sun on the integer heightmap's risers) and the low ambient fill. The face
  tint lives **in the shader, not the vertex** — a quad has one normal so it is free, and baking
  it would put a look constant into the merge key, where retuning means remeshing the world.
- **The M5c-2 battery's two real holes were both *joins*, and that is the shape to watch.**
  Nothing checked that each corner samples its own vertex — per-vertex light versus per-*face*
  light, a smooth gradient versus flat-shaded facets — and nothing checked that the pipeline
  hands the mesher any light at all: replacing `GatherLight(...)` with `null` at the submission
  site left 556 tests green, because the vertex suite calls the mesher directly and the pipeline
  suite checks only states and counters. **Every part of the path was tested and the wire between
  them was not.** M5c-1 found the same shape one layer in, so the habit is: **put a recipe on
  every call site that passes data between layers**, not only on the logic at each end. Two other
  uncaught recipes are *not* holes — the grid's outermost ring is belt and braces (M4a's
  contact-epsilon category, and the ring and `Sample`'s zero-weight `continue` are safe only
  together), and rounding versus truncating in `Scale` is below the resolution of anything here.
- **Three screenshots, because one would have proved less.** Daylight; `--skybrightness=0`, where
  the terrain renders *pure black* (the sky channel is what lights the world, block light really
  is zero, and darkness has no accidental ambient); and `--skybrightness=0 --debuglamp`, a warm
  pool over ~15 m and nothing else. **Only the third can prove `COLOR.g` carries block light and
  is tinted separately** — every other test in the milestone would pass identically if both
  channels went into one byte. **Closed at M5d-1, and it was worse than this line said**: the
  environment fog blends the *final* colour toward one constant, so against correctly-black
  terrain the fog was 100% of what you saw — the whole landscape read as a legible grey-blue
  hillside in a world with no light in it. The voxel material now opts out with `fog_disabled`
  and fogs toward `fog_colour × sky_brightness × COLOR.r` instead. **Leave `fog_enabled` on in
  `Main.tscn`**: it still fogs the *sky*, and removing it exposes the procedural sky's dark
  ground hemisphere as a black band along the horizon.

**M5c-1 — the stage, the lifetime and the relight: done 2026-08-30.** Suite **518 → 542**.
`GenerationStage.Lit` is a third rung on the barrier, `ChunkStreamer` implements `ILightChunks`
and owns a `ChunkLight` per chunk plus a refcounted `SkyColumn` per (x, z), the propagation
drains on the sim thread under a 4 ms slice, and an edit updates the sky column, queues a derive
and marks exactly the drawn chunks whose sampled light moved. **A lamp still does not glow —
that is M5c-2.** Six things that are expensive to rediscover:

- **Light needs no third margin ring, and it was one decision away from needing one.** The
  mechanical ladder — mesh at R needs R+1 `Lit`, `Lit` at R+1 needs R+2 at `Features`, `Features`
  at R+2 needs R+3 — grows the margin from 2 to 3, **+17% chunks on top of M2c's +41%**. It is
  avoidable because **deriving a chunk's light asks for voxels, not for a stage**: the outermost
  loaded ring derives and joins the propagation while never reaching `Lit` and never meshing,
  which is exactly what the margin's third load-bearing reason already describes. **Revisit
  trigger: when `ApplyFeatures` starts placing anything it must route through the relight path
  the way `SetVoxel` does.**
- **An untracked neighbour is skipped and a tracked one without voxels blocks**, and the
  difference is whether it will ever contradict the assumption. Not loaded reads as air and has
  no grid, so propagation stops there anyway; loaded-but-generating *will* have voxels shortly,
  and a face recorded as conducting against air it is about to fill is a wall that leaks light.
- **The propagation pushes rather than pulls, so a push into a chunk with no grid is dropped and
  nothing remembers having tried.** `LightPropagator.SpreadInto` re-spreads the **six adjacent
  face layers** when a grid comes into existence — six, not the whole 488-cell shell, because
  light crosses faces only. **M5b could not have found this**: its fixtures build every grid
  before lighting any, which the streamer cannot do. It appeared as a one-in-six flake. **Its
  inverse, `RetractFrom`, arrived with cache slice 2 (2026-09-04)**: a grid that leaves takes back
  the light it was the only justification for, so the loaded set is a fixpoint with one grid fewer
  — which until then it was not, by design, and the design stopped covering it the moment a chunk
  came back with different sources.
- **The first deterministic test for that bug passed on broken code** — it dropped the chunk
  under a *sky shaft*, where every cell is its own full sky source and relights itself. A lamp in
  the neighbouring chunk, underground, is the shape that separates the arms. Fourth outing for
  the "watch it fail" rule and a new variety: **the trap was the fixture, not the assertion.**
- **M5-PLAN costed the wrong half of M5.** It estimated propagation (~779k cell visits, ~15 ms)
  and said nothing about deriving, which is the larger half. Measured in Release: **0.245 ms
  surface / 0.327 deep / 0.204 open sky per chunk, ~0.41 s for the whole 1,521-chunk fill** —
  against generation's 0.47 ms a chunk on eight workers, where this is one sim thread. Its
  propagation estimate was actually good — **585k visits measured against 779k predicted**, which
  sharpens the finding: the half it got right was not the half that mattered. **The lever is
  `PaddedChunk.CopyFrom`, which is 60–75% of it, and it is named and deliberately not built** —
  raising the budget bought back the whole regression. It would speed up meshing too.
- **The light budget is 4 ms and a standing-start sweep is what says so.** Light delays the
  *first draw*, since nothing meshes until its neighbourhood is lit. At the 5 s report: 1 ms →
  857/1521 derived and **0 drawn**; 2 ms → 1467/1521 and **0 drawn**; **4 ms → settled, 165
  drawn**; 8 ms identical. **2 ms was a real regression and invisible in the tests**, which pass
  their own generous budget — `--lightbudget=` is the seam that found it. A ceiling, not a
  reservation: once the world is lit nothing is queued and the step costs nothing.
- **A uniform neighbourhood has a closed form that skips the copy entirely**, pinned against the
  general path cell for cell — the `IsCaveAt`/`GenerateTerrain` shape of test. **Coverage is 20%
  in the real world** and 12% in the all-surface test world; M2b's 68% is about chunks alone and
  this needs the six face neighbours uniform *and equal*. Related and worth keeping: **light's
  dependency set is the six face neighbours, not the mesher's twenty-six** — nothing in the
  derivation looks at an edge or a corner.
- **The break-recipe battery found three holes and no two were the same kind.** Nothing asserted
  the two-flag split, only the counters' *sign*; the gain-only derive rule had a scenario where
  both arms agree (and weakening it is in fact **unreachable** — nothing pushes a chunk when a
  neighbour unloads, so the rule never runs, which is M4a's zeroable-epsilon category); and the
  shaft test dug a **one-voxel bore**, which is never a sky *source*, because a cell is open to
  the sky only when all sixteen of its columns are. Its light came by free fall, so the
  sky-height rebuild never mattered to it. **M5-PLAN warned about this exact case and the
  warning was not enough — a warning about which code path runs is not a warning about which
  property is checked.**

Two smaller ones: **a derive happens off the work list, so `BuildLight` has to push the chunk
back** or the pipeline stalls with every grid built and nothing meshing (M2c-2's rule again);
and **`IsQuiescent` moved onto the pipeline**, because a caller testing only jobs and the work
list saw quiescence while every chunk sat one stage short of meshing.

Three things stay open, all needing a windowed run rather than a headless one: whether the
free budget removes the stutter (headless has no deferred deallocation), and M1d's
`RenderingServer` vs `MeshInstance3D` and 2×2×2 mesh-block questions, which need the windowed
draw-call counter that `StreamingProfiler.RenderLine` already prints. **A first reading of that
counter came out of M5a's windowed check and was not acted on: 209 draw calls and 186,728
primitives for 193 drawn chunks at r4h/2v.**

The licence is settled: [PolyForm Noncommercial 1.0.0](LICENSE.md), source-available. It
can be loosened to MIT any time; it cannot be tightened after the first outside PR is
merged, which is the real freeze point.
