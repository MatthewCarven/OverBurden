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
| [docs/M2-PLAN.md](docs/M2-PLAN.md) | M2, split into M2a–M2d with per-part design and tests. Done, kept for the reasoning. |
| [docs/M3-PLAN.md](docs/M3-PLAN.md) | M3, mining, split into M3a–M3d. Done, kept for the reasoning. |
| [docs/M4-PLAN.md](docs/M4-PLAN.md) | M4, the player, split into M4a–M4d. Done, kept for the reasoning. |
| [docs/M5-PLAN.md](docs/M5-PLAN.md) | M5, lighting and AO, split into M5a–M5d. Done 2026-08-31, kept for the reasoning. |
| [docs/M6-PLAN.md](docs/M6-PLAN.md) | M6, persistence, placement and inventory, split into M6a–M6d. Done and played 2026-09-01, kept for the reasoning. |
| [docs/M7-PLAN.md](docs/M7-PLAN.md) | **M7, rain, split into M7a–M7d. Planned 2026-09-01, not started. Read before writing any of it — and read SCALE.md before it.** |
| [docs/SCALE.md](docs/SCALE.md) | **How many voxels and cells exist at each view distance, what one full sweep costs, and what each system actually iterates.** Read before designing anything that wants to check the world periodically — M7, M8 and M9 all start here. |
| [docs/adr/](docs/adr/) | ADR-0001: why we build our own voxel engine rather than adopt godot_voxel, **with named revisit triggers** |
| [TODO.md](TODO.md) | Milestones as checkable tasks, and the decisions still open. **M6's section is the plan as a checklist.** |
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

Measure worldgen — **Release, always; Debug is ~3× slower and reads like a real number**:

```bash
dotnet run --project MinerCraft.Bench -c Release
```

Measure **lighting** on the same harness. Also Release-only, and here the Debug penalty is worse
— **5.9× on the derive, ~10× on the walk** — so a Debug figure is not a measurement at all:

```bash
dotnet run --project MinerCraft.Bench -c Release -- --light
```

Measure **chunk serialisation**, which is what settled whether `Compact()` runs on save. Release
only, same as the others:

```bash
dotnet run --project MinerCraft.Bench -c Release -- --blob
```

Measure the **SQLite read path** against the generation it replaces — the number M6's crossover
worry turned on:

```bash
dotnet run --project MinerCraft.Bench -c Release -- --store
```

It reports write, read, raw and stored bytes across six arms — generated, carved and *stripped*
chunks, each with and without `Compact`. **The stripped arm exists because the carved one cannot
benefit**: carving air into a chunk that already holds air leaves the palette untouched, so
measuring only that prices `Compact` against a fixture with nothing to gain.

It loads `mods/minercraft/` through the real loader, because conductance is a function of which
materials are opaque and a fixture would measure a made-up world.

Measure the **M7 watershed** — the number that decided whether an incremental watershed ever has to
be built. Release only, and it sweeps window sizes at the 4 m cell:

```bash
dotnet run --project MinerCraft.Bench -c Release -- --watershed
```

**Sampling, flooding and the merge tree are timed apart**, because M7-PLAN's estimate assumed
sampling dominated and it does not — it is about an eighth of the cost. **It warms up by call count
rather than by one pass**, because one warm-up pass leaves a small window in tier-0 code while a
large one is promoted mid-flood by OSR, which reported the 1 km window at 47 ms instead of 20 and
looked exactly like a memory-locality finding.

**Read the machine-control line before quoting anything from it.** It times worldgen over 256
chunks and prints the result against the 0.47 ms/chunk recorded at M2d-2 — **this machine has been
measured at 2.5x that, hours apart, with identical binaries**, which is enough to move a figure
across a decision threshold. Divide by the stated factor before comparing with any number in the
worklog.

Verify headlessly — no display needed, catches scene and script errors and prints the
diagnostics:

```bash
../GAME/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path MinerCraft.Game --headless --quit-after 900
```

**`--quit-after` counts frames, not seconds** — 900 is about 15 s, and the streaming report
only prints every 5 s, so a small number exits before there is anything to see.

Profile a flythrough. The flags go after a bare `--`, as `name=value`, and the profiler
quits by itself when it is done, so no `--quit-after` is needed:

```bash
../GAME/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path MinerCraft.Game --headless -- --profile --radius=12 --vradius=6 --speed=40
```

Also `--margin=`, `--warmup=`, `--measure=`, `--cavestep=` (1 restores per-voxel cave
evaluation, for A/B against the lattice), `--applybudget=<ms>` (a large value such as 1000
is the unbudgeted arm; there is no "off"), `--lightbudget=<ms>` (how much of a frame light
derivation and propagation may take; **swept at M5c-1 and 4 ms is the knee** — below it the
world has generated but cannot draw, because nothing meshes until its neighbourhood is lit),
`--ao=0` and `--light=0` (the two halves of the shade, switchable independently — see the quad
table below), `--skybrightness=<0..2>` (scales the sky channel alone; 0 leaves only block light)
`--debuglamp` (places one lamp where the crosshair points, so a screenshot can show block
light without a keypress) and `--entangle` (the entangled miner, which with `--mine` aims along
the flight path rather than straight down — see below).

**Building is bound to the right mouse button**, `B` cycles the brush (cube, slab, ramp, sphere),
and **`1`–`9` pick a material from the hotbar** (`V` still cycles to the next one). **`T` cycles
the tool tier** — it was the number row until M6d-3, and the row went to materials because that is
where a hand expects to find what it builds with. `--place` holds the button for a whole
profile window, `--stock=<voxels>` seeds the inventory so such a run measures the brush rather
than the carve, and `--brush=<name>` picks the shape.

**The world file lives at `user://worlds/<name>/world.db`** — on Windows,
`%APPDATA%/Godot/app_userdata/MinerCraft/worlds/`. `--world=<name>` picks one, `--nosave` opens
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
../GAME/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path MinerCraft.Game --script res://screenshot.gd -- C:/tmp/shot.png
```

It instances `Main.tscn`, waits 8 s for terrain, saves a PNG and quits. Extra arguments after
the path are passed through to the game, so `-- shot.png --ao=0` gives the other arm of an A/B.

Profile a **fall** — the vertical motion profile M2d's horizontal flythrough could not produce,
and the one that can arrive somewhere before the world does. `--fall` implies `--profile` and
forces walk mode, so the scripted flythrough stays off:

```bash
../GAME/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path MinerCraft.Game --headless -- --fall --radius=6 --vradius=3 --terminal=40 --fallheight=250 --measure=20
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
- Commit with a message that explains *why*, and end with
  `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`.
- Matthew runs `git init` and `git push` himself — flag when either is needed. Remote is
  `github.com/MatthewCarven/MinerCraft`, **private**, default branch `main`.
- `reference/` is gitignored and holds third-party sources we **read but never build
  against** (currently godot_voxel). Nothing in it belongs in a `.csproj`.

## Where things stand

**M7 is under way: all of M7a and M7b-1 are built (2026-09-01) — M7b-2, persistence, is next.**
`WorldClock`, `Weather` and `BasinRainfall` give the world time, a forecast that is a pure function
of the seed, and rain that lands on catchments. **The oracle passes with no tolerance: an hour in
one call equals 3,600 calls of one second**, basin for basin. Two things from it:
**the unit chain is the design** — a rate is nanometres per millisecond, depth is rate × ms, and a
nanometre of rain over a square metre is exactly one microlitre, so nothing rounds — and **at real
rainfall the median basin fills in 35 minutes of the heaviest possible weather**, which nobody can
watch, so `rainfallScale` exists and **M7d is where a person picks it**.

 `MinerCraft.Core/Hydrology/` holds the flood, the basins, the volume→level curves, the
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

**M6d-3 — the first real UI: built 2026-09-01.** `MinerCraft.Game/Ui/Hud.cs` — a nine-slot hotbar
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
  moved from "predicted to bite at 2,505" to "measured not to bite at 176".**
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

**M6c-2 — the inventory: done 2026-08-31.** `MinerCraft.Core/Inventory/VolumetricInventory` holds
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

**M6c-1 — the brushes: done 2026-08-31.** `MinerCraft.Core/Building/` holds `BrushCell`,
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

**M6a — the blob and the world file: done 2026-08-31.** `MinerCraft.Core/Persistence/` holds
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

- **SQLite goes in a new `MinerCraft.Persistence` assembly, not in Core.** Core has **no NuGet
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

Headless from a standing start, r4h/2v + 2 margin on 8 workers:

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

**M2a — job scheduler: done 2026-08-27.** `MinerCraft.Core/Jobs/` holds a flat list
re-sorted on a 200 ms timer, priority packed into one `uint` of four byte-bands, real
cancellation tokens, and dedicated worker threads. Every submitted job reports exactly one
result — that invariant is fuzzed, and a job that vanishes silently would leave its chunk
flagged as work-in-progress forever. Nothing submits jobs yet; streaming at M2c feeds it.

**M2b — worldgen: done 2026-08-27.** `MinerCraft.Core/Worldgen/` holds splitmix64, gradient
noise, a data-driven strata table, and a generator that is a pure function of seed and
position. Measured over 2,048 chunks: 4.0 MiB against 128 MiB naive, 68% of chunks uniform.

**M2b's `6.55 ms/chunk` was a Debug number** and said so nowhere — M2d-2 re-measured the same
code at 2.23 ms/chunk in Release. The cave share it reported (80%) was right; only the scale
was wrong. The game project builds Debug, so the streaming profiles are Debug too and are
consistent with each other — but never quote a generation figure without its build.

**M2c-1 — streaming policy: done 2026-08-27.** `MinerCraft.Core/Streaming/` holds `ChunkBox`
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
Release, and caves fell from 83% of generation to 20%. `MinerCraft.Bench` is the repeatable
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

**M3c — the carve: done 2026-08-29.** `MinerCraft.Core/Mining/Carve.cs` takes a sphere of
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
`MinerCraft.Game/mods/` from disk, rather than the `InMemoryContentSource` fixture every other
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

**M4a — the collider: done 2026-08-29.** `MinerCraft.Core/Physics/` holds `Aabb` and
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
away, at **20 swings a second**. Three things about it that are easy to undo by accident:

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
- **It composes with the number keys** — it changes how far and how fast, never how big a bite
  or what the bite is hard enough to take, so `Carve.Take`'s hardness gate stays the only gate.
  Measured at **11.1 m mean / 18.9 m furthest reach**, 0.139 ms a swing against 0.083
  un-entangled, and **0.9 relight remeshes per swing against 1.0** — carving a hundred metres
  away is not a different cost from carving at arm's length. `MaxReachMetres` exists because
  swing and voxel counts are identical whether the cast ran 5 m or 90, which is M5c-2's shape of
  hole.
- **From 8/s onward the tier's cooldown no longer bounds anything while entangled**, which the
  4/s version's own comment said the opposite of — `steel_pick` cools in 0.20 s, so entangled was
  once the slower of the two. Nothing shipped is balanced by its rate today; the note is for when
  something is.
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
`MinerCraft.Core/Lighting/` holds `LightCellPos`, `ChunkLight` with cell-face conductance,
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
  before lighting any, which the streamer cannot do. It appeared as a one-in-six flake.
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
