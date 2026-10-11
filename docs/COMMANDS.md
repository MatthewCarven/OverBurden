# Commands, flags and recipes

Moved here verbatim from `CLAUDE.md` on 2026-09-26, in a documentation consolidation pass: it was
two thirds of a file every session loads whole. `CLAUDE.md` keeps the everyday commands and the
rules that bite; **this file is the reference — every bench arm, every game flag with its control
arm, and every headless oracle verbatim.** Grep it for a flag's name before running anything that
takes flags, and copy an oracle's invocation from here rather than retyping it. The text is as it
stood, so a flag's paragraph says when it was added and why.

Build and test:

```bash
dotnet test Overburden.slnx
```

Run the game:

```bash
start.cmd
```

**`start.cmd` passes `-- --lightbudget=16 --adaptive=1 --fluidnear=2 --fluidjob=1` itself, then
`"--world=<his world>"`** (`LakeTest2` since 2026-10-11; `LakeTest` from 2026-10-01, now retired) — the first since 2026-09-03, Matthew's "lighting
switch", the second since 2026-09-06, the adaptive view radius, and the two water flags since the water
thread's slices 1 and 3b (2026-09-09 and 09-10). (It carried
`--spread=counted` from 2026-09-07 to 2026-09-09 for the counted divisor's play; the A/B read the same
under both rules and the flag came out — pass it yourself to see that arm.) It launches the Debug build — **which
since the evening of 2026-09-15 is JIT-optimised: `Directory.Build.props` sets `Optimize` for the
Debug configuration, because the editor host loads `bin/Debug` and nothing else and every play until
then ran with the optimiser disabled.** Before that a derive was ~1.4 ms and the 4 ms default gave two or
three a frame, which is what lit a mined-out room over tens of seconds and why the 16 ms budget exists;
it is ~0.36 ms now and the budget stays until a play says otherwise. **Every play-log and headless
profile figure recorded before 2026-09-15 is an unoptimised-Debug figure** — the r6 control's streaming
counts are unchanged to the digit, its light step is 4.3× faster, and a played-world open's first draw is
8.7 → 2.9 s; the worklog entry has the table. Anything you give
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

Measure **M8c's seep index** — a 10 × 10 × 3 m room dug under a lake in the real strata, read
through the game's own `VoxelFluidTerrain`, swept by head and by rock: faces, the scan at arrival
over a classified block against the per-voxel read (M8c-1's arm) and against the edit hook under a
chunk bite, the coefficient's cost, litres a second at the shipped scale and hours to ankle-deep,
and the delivery order per chunk. **The per-voxel scan's floor is one holdability read per voxel at
77 ns**, which is where the plan's estimate was three times out and why the block path exists.
Release only:

```bash
dotnet run --project Overburden.Bench -c Release -- --seep
```

Measure **M8d-1's Tier B mesh against the debug surface it replaced** — the played lake's shape at
rest with the settle's millilitre of jitter (`--lakechambers`), the same lake stepped, the relay's ball
spreading and at rest (`--frontlitres`), and the roof-open flood: surface cells, the debug arm's quads
and walk ms, then tops / sides / bottoms / quads / KiB / terrain asks and the mesh's first and steady
build ms. **The first lake row is slow in both arms and the second is the representative one**; the
3,262 bottoms under the lake are the fixture's caves. Release only:

```bash
dotnet run --project Overburden.Bench -c Release -- --surface
```

M8d-3 (2026-09-11) adds **`--fallcells=<n>`**: how many cells a visit may fall through vacant air,
16 by default (8 m/s from the first tick, stateless) and 1 the pre-M8d-3 arm every recorded figure
was taken in — which the bench says reproduces to within 1–3% on `--fluid` and does not move the
front's shape on `--front`, both of which take `--fallcells <n>` (space-separated). The `M8b:` line
prints `long fall(s) over N cell(s) at up to F`; the `handoff` line prints `left under a lake`, the
bank rule's count.

M8d-5 (2026-09-14) adds one flag and one bench change. **`--lakebody=0` builds no `LakeCeiling`**
— the pre-M8d-5 arm minus the bank rule, in which a hole in a lake's bank treadmills (`drawn` and
`handed` climbing together), kept as the measurement arm for the wrapper's cost and not for play;
and on the bench `--spill` builds a ceiling with its hand-off, at 2.2× the tick of the bare bitset
— the bench's figure, not the game's. The `M8b:` line's `handed` term carries `N cell(s) of it
found inside a lake; lake columns / rebuilt / woken`, its surface term `N under a lake` (Tier B
cells drawn full with no top because the lake's cell is above them — a bite in a lake's bed
reads as the lake), and the `boundary` line a `lake` bracket.

**The courtyard (2026-09-20) adds two.** **`--enclosure=0` builds no `Enclosure`** — the
pre-2026-09-20 arm in which a walled pool on natural ground is the lake outside (the hand-off
hands it, the ceiling puts its air in the lake, the plane draws through the walls), the control
and not a play arm; on, the player's material encloses and nature's does not (a flood at the level,
open once the area spans more than 128 voxels in x or z, since E-1 (2026-10-01; 64 voxels from the asker
before) — an estimate; a wall claims its footing). **`--courtyard=<metres>`** builds
a square ring of stone walls that wide, `--courtyardwalls=<metres>` (2) tall, on the ground round
the player when the window opens, before `--injectlitres` pours inside it — the provocation. The
`M8b:` line's `handed` bracket carries `enclosed N kept, F flood(s) / C closed (O open at the
bound, G of them out through natural ground; U at an unloaded chunk; widest open / closed across of
128), R region(s), B band(s) rebuilt for it` (`farthest … of 64` before E-1) — the why since 2026-09-22, because a pond that
would not fill read `116 flood(s) / 0 closed` and the log could not say which rule — the
profile has an `enclosure` line under `handoff`, and the `surface` line prints `N enclosed
sample(s) undrawn`. **Since the pond branch merged (2026-10-09) the raise is on, so the courtyard is its own
basin and the oracle, verbatim, reads `courtyard basin … holding 20,000 L, level 6.89 against a floor of 5 and a
wall top of 13` with `handed 20,000 L` and no flood. `--raise=0` is the pre-merge arm and reads `handed 0 L`
with `6 flood(s), 6 closed`, resting by tick ~85; with `--enclosure=0` as well it reads `handed 20,000 L`:**

```bash
../GAME/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path Overburden.Game --headless -- --profile --speed=0 --courtyard=8 --injectlitres=20000 --measure=25
```

**The dam (2026-10-01, L-2; it needs the raise, so it is on main since the pond branch merged, 2026-10-09).** **`--dam=<metres>`**
builds a straight stone wall a metre thick, at most that long, across the lake: through the player
when they stand over a lake holding 10,000 L or more, else through the pit `--fillbasin` filled.
`--waterprobe=x,z` (voxels) stands it elsewhere. Its top is **`--damheight=<metres>`** (1) over the
water. Each end runs two cells past where the bank tops it, and the print says `tied into the bank`
or `OPEN, the length ran out`. **`--damaxis=x`** runs it east–west (default north–south),
**`--damafter=<seconds>`** holds it past its due moment for a windowed run whose chunks are still
loading, and **`--dampour=<litres>`** pours into side A once the split is read. Over a puddle it
builds nothing and lists the five fullest lakes in the window, each as a `--waterprobe=x,z`.

Its `M8: dam —` lines read both sides three cells off the line:
- before the build;
- after the window has caught up with the raise map that holds the dam, with the split terms, the
  ledger's held, and what would not fit back or spilled;
- after the pour.

The `M7d:` line carries `split N lake(s), M L shared by level, D L displaced`. **The seeded oracle,
verbatim:** basin 292 at the profile seed, 1.2 ML. It reads `ONE lake` at -5.828, then `TWO lakes`
with side A at -5.828 and side B at -5.112, `held 1,200,000 → 1,200,000 L, +0 L would not fit back,
+0 L spilled`, `117,573 L displaced`. After the pour, A is at -4.249 and B at -5.112:

```bash
../GAME/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path Overburden.Game --headless -- --profile --speed=0 --at=37,2,57 --fillbasin=1200000 --dam=80 --dampour=100000 --measure=20
```

On a **copy** of `LakeTest` (since 2026-10-11 `retired-LakeTest-2026-10-11`), his 4.2 ML lake 33 m from his spawn, add `--world=<copy>
--waterprobe=140,164` to the same flags without `--at` and `--fillbasin`. Rain and the lake's own
draining move its figures between runs. On 2026-10-01 it read A -29.339 → -29.219 and
B → -28.612, the sides summing exactly to the litres shared, then 100,000 L into A gave A -28.630 and
left B at -28.612.

In the game **`--fluidsurface=debug`** is the pre-M8d-1 arm — one quad a surface cell *in Tier A's
shader*, the geometry and the look every M8b and M8c screenshot had, the control for every M8d-2
screenshot — and `nosides`, `nobottoms`, `notops`, `nomerge` leave one thing out of the mesh (in the
new shader), which is
how the ball's streaks were attributed to the slots in a falling column. **Since 2026-09-21 the
mesh is one `ArrayMesh` a wet panel, and a tick rebuilds and reinstalls only the panels whose
own cells, a face neighbour's, the column under them, the lake band over them or a previous
surface they lerp from moved** — the lever M8b-4b recorded with the crash log's 40 MiB /
100+ ms; `--fluidsurface=full` is the pre-2026-09-21 arm (every panel every tick), the control.
**Since M10d-1 (2026-09-22) the `M8b:` line's `held` bracket and the profile's `fluid` block carry
`Q L in R panel(s) below the seed surface`** — a panel whose chunk top is strictly under the least
seed height over its footprint (`UndergroundWater`, off the loaded sky columns, measured at
`Publish`); the mine's share, the number *pumped dry* reads at zero. A panel that straddles the
surface counts as above, so a three-wide `--breach` reads `0 below` (its shaft is eight voxels
deep) and the keystone reads every litre below. The `M8b:` line's `surface` term and the profile's `fluidsurf` line print quads `over N panel(s)`,
tops / sides / bottoms / KiB of the whole surface, `last rebuild N panel(s) (own, new, neighbour,
column, lake, previous; KiB) and N freed` with mesh + install ms, and `install mean / max over N
rebuild(s)`. On the bench (`--surface`) a lake at rest went 837 KiB / 40 ms a build to 0 / 0.09;
a flood rebuilds every panel it moves through and costs what it did. **The walk's sides pass reads
only a full layer's perimeter** (the same evening — the walk was 650 ms of a 1,200 ms tick on the
played world, ~265 after), **and a moved previous surface rebuilds only the panels whose build
read it** (2026-09-22 — a settled world read `own 2, previous 26` under the geometric rule; the
readers were a handful). **`H` holds the water** — no tick begins until `H` again, a state
stays one state — and **`Alt+H` runs a full collection and prints what survived it**; the `M2c:`
line's `managed` is garbage included (a snapshot read 159 MB live in a 971 MB heap) and `live …
at the last gen2` is the number a leak moves. `dotnet-gcdump collect -p <pid>` is the heap by
type, one second on the running game.

Measure **what a drip does to the automaton with nothing else awake** — the bodies number M8c's
pass asked for, which the game's `awake at seeping boundaries` cannot give on a played world
because it counts every awake cell (the flood, the loaded mine, a fill's displacement) and reads
the same with seepage off. A room under a bowl fed by seepage alone, both arms, the shipped scale
and ×10, 600 ticks with the settle: awake mean / p50 / max and quiet ticks. **Debug-safe — it is a
count, not a time.** `--seepticks <n>` sizes it:

```bash
dotnet run --project Overburden.Bench -c Release -- --seepawake
```

Measure **M9a's support field** — the plan's seven fixtures on real strata through the game's own
analysed-set rule, the derive's ms with the gather and the search apart, rounds and derives for the
join from nothing, the at-risk count, and **an ASCII cross-section of each wedge**, because
decision 1's table is arithmetic and the eye has to agree before the rule is believed. **Run it with
`DOTNET_TieredCompilation=0`**: with tiering on the search reads up to 3× while `Derive` sits at
tier 0, promotion postponed for as long as the bench keeps jitting new methods, and no warm-up by
call count moves it; the line under the table says which arm ran. `--restrefund <n>` (30) and
`--supportscale <x>` (1) are the sweeps, space-separated like the bench's other flags. Release only:

```bash
DOTNET_TieredCompilation=0 dotnet run --project Overburden.Bench -c Release -- --support
```

M9b (2026-09-12) adds the support field's seams. **`--support=0` builds no region** — no derives, no
tint, the control arm; **`--supporttint=0` keeps every derive and hands the mesher nothing**, the
tint's control and the arm that prices its remeshes apart. **`--bitechunks=<n>` bites an n × n square
of chunks, one deep, `--bitedepth=` (3) chunks under the player's when the window opens** — three
because the chunk under the player's has a soil roof, and the oracle is the stone plug: `at risk 20
voxel(s) in 4 settled chunk(s)` at n = 2 on the seed world, 0 at n = 1. **`--inbite` drops the camera
into the bite** for the screenshot, because a plug is an underside — with `--pitch=85`. The periodic
`M9:` line and the profile's `support` line print analysed / dug / fields and KiB, derives split
searched / closed-form / requeued-stale / dropped-departed and by cause, in flight / pending /
unsettled, worker mean and max, sim gather and apply, at-risk voxels and chunks **over settled fields**,
and tint remeshes; `M9: at open` prints once when the open's derives have all landed. **Read the worker
figure against the Debug bench with tiering on** (`dotnet run --project Overburden.Bench -c Debug --
--support`, 0.83 + 4.4 ms), not against M9a's Release number.

M9c-1 (2026-09-14) adds the fall's two. **`--collapse=0` arms every timer and fires none** — the
warning-only arm, for walking under a marked roof to judge the marking; **`--collapseafter=<min>,<max>`**
is the delay window in world seconds (**10, 20 since 2026-09-27**, his *"from 10-20 seconds but
randomly"*; a settled chunk with something at risk arms `min + (max − min) × u × (1 − deficit / 2K)`,
seeded — **bounded inside the window, and a deficit of K pulls the top halfway in**, so the plug's
thin lip draws in 10–20 s and a gutted roof in 10–15 s. From M9d until then it was 2–60 s with an
exponential draw, so **a figure recorded before 2026-09-27 at `--collapseafter=1,2` came from a
different draw**; the plug still reads its four falls, 20 voxels and `at risk 0`. `--clockscale`
shortens it the way it shortens the weather; **a timer only shortens** — a lake rising over an armed roof redraws it earlier). The sweep runs
at each fluid boundary — its own `collapse` term on the `boundary` line — or every frame under
`--nowater`. **Each fall prints one `M9: fell` line** (chunk, voxels, columns, deepest and mean drop,
chunks fell from and written into, litres lifted / returned, `cascade`, the world time, the sweep's ms),
and the `M9:` line and the `support` profile line carry `collapse on: armed / due / waiting (unsettled,
fluid-pending), fired, voxels fallen, stale skipped, lifted / returned L, freed / buried, sweeps`. A
walking body with rubble in its box is lifted through `TryUnstick` (`M9: the player was freed N m up`)
or left (`M9: the player is buried`, `F` the door until M11). Both identity lines carry `lifted L ==
returned L by falls`. **The headless oracle, verbatim, reads four `M9: fell` lines one boundary apart,
`20 voxel(s) fallen` and `at risk 0`, with the r6 control unchanged to the digit.** **Since
2026-09-27 `pieces` is the default, so every M9 oracle recorded before then wants
`--collapsemode=sweep` to reproduce** — it is on the two below; older records elsewhere omit it:

```bash
../GAME/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path Overburden.Game --headless -- --profile --speed=0 --bitechunks=2 --collapseafter=1,2 --measure=20 --collapsemode=sweep
```

M9d (2026-09-15) adds the keystone's three. **`--saturation=0` derives every field dry with the water
still on** — the control the plan's oracle row names (*the same chamber dry and under the lake*);
**`--tablemove=0` keeps the table and drops the sweep** that re-derives a settled roof under a lake that
moved — M9b's arm, a roof under a rising lake keeps its dry field; **`--fillbasinafter=<seconds>` lands
`--fillbasin`'s pour that far into the measured window** rather than at its open, because the trigger's
oracle needs the bite settled dry before the lake stands over it (0 is every earlier figure's arm — the
pour the frame before the bite). The `M9:` line's `table N` gains `(S sweep(s), C compared, R rebuilt,
U unchanged)` and its `armed` a `(M shortened)`; the `boundary` line gains `table`; the `M6c:` and
`building` lines gain `wet` — the fill's refusal, a voxel holding Tier B water left alone **at the
click; since 2026-09-19 the boundary fills it displacing** (`FillWater` — the machine's lift /
write / return rule on every shape, `lifted == returned + displaced`; the `M6c:` line's `wet`
carries `(N filled displacing at the boundary: lifted / returned / displaced)`, and **`--placeafter=<seconds>`**
holds `--place` off so a pour can arrive first: `--profile --speed=0 --place --placeafter=6
--stock=64000 --injectlitres=200000 --pitch=-60 --measure=12` reads `171 wet (93 filled displacing
at the boundary: lifted 1,082.0 L, returned 1,082.0 L, displaced 0.0 L)`). **The keystone is a race, not a fixed script** (found 2026-09-26, collapse b-2): whether three or four
of the plug's falls land before the pour at 5 s depends on when the worker derives settle, and that
decides the roof the lake stands over — five runs of the same binary read 15,985 to 24,973 voxels
fallen and 0 to 31 L lifted. The figures below are one draw; `at risk 0` at the end and lifted ==
returned hold in every run, and those are the oracle. **The keystone,
verbatim, reads `at risk 20` at open, `table 20`, `at risk 18,718 voxel(s) in 7 settled chunk(s)` after the
pour, four falls of ~6,200 voxels with `lifted 30 L == returned 30 L` and `displaced 0`, then `at risk 0`;
`--saturation=0` sheds the plug's 20 and nothing more, `--tablemove=0` reads `table 0 (0 sweep(s))` and
stays at 20, and the r6 control is unchanged:**

```bash
../GAME/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path Overburden.Game --headless -- --profile --speed=0 --bitechunks=2 --fillbasin=2000000 --fillbasinafter=5 --collapseafter=1,2 --measure=25 --collapsemode=sweep
```

On the bench **`--support --collapse`** drops every fixture stage by stage with a zero window —
voxels a stage, `Σ(Y)` before and after, the count identity, the fixpoint after each settle, and the
picture after beside the picture before (`o` is held rubble) — and floods fixture 2 first, printing
lifted / returned / displaced and the automaton's ticks to rest. Same `DOTNET_TieredCompilation=0`
rule as `--support`:

```bash
DOTNET_TieredCompilation=0 dotnet run --project Overburden.Bench -c Release -- --support --collapse
```

**`--collapsemode=pieces`** (COLLAPSE-PLAN b-2) is the Tetris collapse: a due fuse starts an event,
pieces release from a spreading front, and each is air at its release and rubble at its landing,
drawn falling between since part c. **It is the default since 2026-09-27**, on his play of part c
(*"make it the default please"*); **`--collapsemode=sweep`** is M9c-1's one-sweep fall, the arm
every earlier M9 figure was taken at. Its knobs: **`--collapsegrace=`** (15 s flat), **`--collapsedouble=`** (5 s a doubling),
**`--collapsecap=`** (×16, the pace's ceiling, reached at 35 s — his *"cap the top please"*),
**`--collapsepictures=0`** (part c's control: nothing drawn in the air; a windowed run only, since
headless draws nothing),
**`--collapsespread=`** (2 m of steer) and **`--collapsebudget=`** (2 ms of releases a frame).
**The phonk (part e, 2026-09-27):** **`--collapsemusic=<path>`** (default `media/TETRIS_PHONK.mp3`,
relative to the repo's root, gitignored; `0` silences it), **`--collapsemusicmax=`** (1.25, the
speed at the pace's cap) and **`--collapsevolume=`** (−6 dB). It prints `M9: music` lines — setup,
start, a speed step at most once a second, the fade — and a `music` term on the `M9:` line, headless
too (the dummy driver plays nothing). A missing file prints `M9: music off — no file at …`. Each
event ends with one `M9: event N — …` line, and the `M9:` and `support` lines carry a `pieces:` term
with the identity `released == landed + in flight + lost`. The plug in pieces reads one event of about
six pieces and 20 voxels (eight on 2026-09-27, at the default); the keystone in pieces wants
`--measure=60`:

```bash
../GAME/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path Overburden.Game --headless -- --profile --speed=0 --bitechunks=2 --collapseafter=1,2 --measure=20 --collapsemode=pieces
```

**`--pieces` with `--support --collapse` drops them in Tetris pieces instead** (COLLAPSE-PLAN a-2;
**since b-1 it drives the game's own `CollapseDirector`** through the scheduler at a zero window, a
boundary every 500 ms and a settle every 250 ms): the cutter, the solver and the events on a simulated
60 Hz clock. It prints events started and chunks joined, pieces by mino, the solve's ms, the re-plan's
gather and cut ms apart, **the frame's sim-thread ms (mean / p99 / max)**, and with **`--cutworker`** (b-2)
the re-cut on a real worker thread as the game runs it, each simulated frame taking a real 16 ms while
a cut is out — interleave it with the plain arm, because this machine's two speeds move the figures, rubble that fell again, rescued voxels, the four oracles (solid conserved, Σ(Y) down at
every landing, the solver never above its own lowest, nothing landing on solid), and the picture
before and after. Same tiering rule:

```bash
DOTNET_TieredCompilation=0 dotnet run --project Overburden.Bench -c Release -- --support --collapse --pieces
```

**`--watch` prints a table of watched functions under a profile's report** (2026-09-26, Matthew's
ask): calls and min / mean / max ms a call for each function `CallWatch` names in Core and Game.
That is 21 coarse ones on main (the tick job, the solve and its flood and apply, the lake ceiling,
the hand-off, the enclosure, both surfaces, the watershed's four stages, light derive and
propagate, mesh, generate, support, the pipeline update, machines) and six more since
the pond branch merged (the grid, the raise). A time is wall and inclusive, over the whole run, the
open included. Off, a watch is one flag read a call. The flood's inner reads are never watched,
because they are tens of nanoseconds and a stopwatch would time itself; those are the sampler's.
**`--at=x,y,z`** (metres, flying) puts a profile's camera at a subject, on both branches. **The
sampler: attach by PID, never launch.** Run `dotnet-trace collect -p <pid> --profile
dotnet-sampled-thread-time,dotnet-common --format Speedscope --duration 00:00:00:30`. The launch
form cannot connect to Godot's hosted runtime. The duration must end before the game does, or
every frame reads `?!?`. **Set samples ending in `PollGCWorker` apart**: they are threads parked
at a safepoint and land on whatever loop polled. The recipe and both tables are the 2026-09-26
(morning) worklog entry.

Measure **the sheet solve against a wide active set** — M10d-3's arm (2026-09-24): a chamber
block's floor full, a noisy layer over it with one cell in `--solveholes` (7) dry, an unsupported
noisy layer over that, every wet cell a shuffled seed; the flood's two-layer index against the
dictionary it replaced (`FluidSimulation.HashedFloodIndex`), **arms interleaved in one process**
because this machine sits in one of two speeds three to four times apart from minute to minute, and
the run refuses to print a ratio unless both arms land the same water. Per pass: seeds, visited,
written, sheets, and the solve's four phases. `--solvechambers` (12), `--solverounds` (6). The
game's `settle` line carries the same phases (`per pass N seed(s), gather / sort / flood / level`).
Release only:

```bash
dotnet run --project Overburden.Bench -c Release -- --solve
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

**A profile never restores the saved position** (`Main.cs`, the spawn: *"never during a profile, where
the spawn is a measurement input"*), so a played world under `--profile` stands at the scene's spawn
unless `--at=` names a spot. To measure a world where its player stood, run it played-style instead:
`--world=<copy>` with `start.cmd`'s four flags and `--quit-after`, reading the periodic `M10:` line's
`tick:` mean and max and the `M8b:` line's `boundary worst` (IM-0, 2026-09-29). **Headless runs at about
130 frames a second there, so 5,400 frames is about 40 s**, 70–80 boundaries. And **a `--mine` flythrough
leaves `--fillbasin`'s basin behind**: IM-0's "wet" bite arms read no fluid field at all, so a bite
under a lake needs a fixture that stands in it.

Verify headlessly — no display needed, catches scene and script errors and prints the
diagnostics:

```bash
../GAME/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path Overburden.Game --headless --quit-after 900
```

**`--quit-after` counts frames, not seconds** — 900 is about 15 s, and the streaming report
only prints every 5 s, so a small number exits before there is anything to see.

**The export (2026-10-12, ALPHA-PLAN session A).** One command makes the alpha zip in `build/` (Godot closed;
the 4.7.1 mono export templates installed; `-Godot <console exe>` from a worktree):

```bash
pwsh tools/make-alpha.ps1
```

Its export step alone, verbatim (the preset is `Overburden.Game/export_presets.cfg`; it builds the C# through
`tools/godot-sln/Overburden.Game.sln`, and logs `no solution file exists` without it):

```bash
../GAME/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path Overburden.Game --headless --export-release "Windows Desktop" <absolute path>/Overburden.exe
```

**An exported build differs from the editor binary in four ways**: it reads `mods/` and `media/` beside the
exe; it saves and logs under `%APPDATA%\Overburden Alpha\` (read its log there, not in `app_userdata`); it
applies `start.cmd`'s four settings unless a flag overrides them; and on a failed start it shows a box, unless
headless. **Probe it on a scratch world and delete the world after.** The exe is a windowed program, so start
it with `Start-Process -Wait` (PowerShell returns at once otherwise), from any folder:
`Overburden.exe --headless --quit-after 900 -- --world=probe-zip`. The first run of the zip on 2026-10-12 read
`Overburden 0.1.0-alpha (85f2859) — exported, from …` and `Content: 1 mod(s), 17 material(s), 4 tool(s) …`,
a new world, the music, no `ERROR`.

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
light without a keypress) and `--entangle` (**selects the quantum miner at open** — since
2026-09-19 the quantum tiers are tools on the T cycle, `overburden:quantum_miner` the 1 m cube
and `overburden:quantum_excavator` the 8 m chunk, both at range, both 50 J a voxel from the
equipped pack (100 until the first play, 2026-09-18; 20 for an hour of it), and the flag keeps
its name because every recipe carries it; with `--mine` it aims along the flight path rather
than straight down — see below; **a `--mine --entangle` run takes nothing unless it carries
`--nogate` or `--pack`**). **The miner's bite is the brush cell — a 1 m cube on the 1 m brush
lattice** (`Carve.Cube`, `CubeVoxels = BrushCell.Voxels`, 64 voxels, 10/s on Matthew's word),
**and its fill is the brush at range on the same lattice at the same rate**; **`--entanglechunk`
selects the excavator** — the whole-chunk bite and fill that was the default from 2026-09-03 to
2026-09-14, the arm every recorded bite figure was taken in; **`--entangletunnel` selects the
quantum tunneller**, the 2 m cube (2026-09-23), at its file's 0.25 s — IM-0 (2026-09-29, the fixed
build, two rounds) read `--profile --speed=6 --radius=4 --vradius=2 --mine --entangletunnel --nogate
--measure=20` at 3.9 swings/s, ~294 voxels removed a swing (the rest already air), `swing ms`
0.42–0.53 mean and 7.4–8.0 max, 4.2 remeshes a swing, against the cube's `--entangle` arm on the same
recipe at 9.5/s, 41 voxels, 0.16–0.20 ms mean and 8.5–8.8 max, 2.4 remeshes; **`--entanglesphere` is retired**
(2026-09-19 — the pick's sphere at range is no tier's shape; its figures are in the worklog, and
it prints that it is retired); **`--chunkcooldown=<seconds>` and `--cubecooldown=<seconds>` sweep
the two files' `cooldownSeconds` and are deliberately not profile-only**, because `T` is a play
bind and a rate that only reached a profile run would be a seam for the one arm that cannot
judge it. **Q held runs the tier in hand** — *"holding q is supposed to enable automine"* — a
swing at the crosshair every cooldown with no click, one voxel with Alt, a plain hold from the
press (`MiningController.Running`; `TapOrHold` exists for E at M10g and nothing reads it yet).
**`--holdq` holds it for a whole run** — the hold's wire, past the profile's refusal by its own
hand — and on the excavator with `--entanglechunk --nogate --pitch=-60 --profile --speed=0
--measure=6` reads `quantum_excavator took everything` at 25,859 removed a swing; from the
default pick it swings `steel_pick` at 29 a swing until the bore is past reach. **The steel pick
is retired since 2026-10-12** (his word, for the alpha): the default pick is now `hand_pick`, which
takes the soft ground and not stone, so that second reading no longer reproduces; the
excavator's does.

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

**`--bodyprobe=<seconds>`** (2026-09-28, WATER-BODIES-PLAN) splits Tier B's held water into
face-connected components once, that many seconds into the session, not the world's age. It prints
`M8e:` lines: the count, single cells, small and large components, the awake count, the flood's ms,
then the twelve largest with litres, full share, Y span, share below the seed and lowest voxel.

**The bench's `--rest` arm** (2026-09-28, water bodies part a) is the clean fixture. A walled box
with a flat floor is filled once and ticked at the game's own settle until nothing is awake, with
no near/far order and no worker. The volume is the same in three fills: `level` (put down level,
the control, which must rest in a tick), `block` (a block over the centre, half the width, that
slumps), and `edge` (poured along one wall, x 0..3, at 700 L/tick, his lake's rate, then stopped;
a 4 × 4 corner was the first inlet and choked at ~120 L/tick). `--restnear <rings>` turns on the
game's near/far order with the viewer in the box's corner chunk, and each checkpoint then prints
the far queue.
Each checkpoint prints the awake cells by kind with the same `WaterComponents.Classify` the
`--bodyprobe` lines use, and the window's visits by move. The summary gives the tick the awake set
fell under 10% and 1% of held, and the tick it rested. Tick counts are deterministic, so they
compare across the machine's two speeds; the ms do not.

```bash
dotnet run --project Overburden.Bench -c Release -- --rest --restwidths 32,64,128 --restdepth 32 --restticks 7200
```

`--restwidths` (32,64), `--restdepth` (32), `--restfill <percent>` (50), `--restfills level,block,corner`,
`--restticks` (7200, an hour of world time), `--restnear` (0, off).

**A probe of his TajMahal needs `--at=-268,-5,1284`** (2026-09-28). His saved spawn is at y 62 m,
chunk layer 7, and the quarry pool is at layer −2, beyond the view's vertical reach: without it a
run loads 77 kL of films on his building and reads them as his water. Check the `loaded … L from N
panel(s)` term on the first `M8b:` line: 20,075,433 L from 255 panels is the pool.

**`--portaldrain=0`** (2026-09-28) stops the lakes draining into Tier B through dug portals: his
quarry pool unfed. A measurement arm; the drain stays built, so the identity's `drawn` term reads 0.
`--nowater` is not that arm, because it removes Tier B too. The periodic `M8b:` line now carries
`N queued (awake cells held or not, plus paused)` after the tick, from the published snapshot and
without the walk the quit line's `awake or paused` pays, so a long run has an awake curve.

**`--solvecarry=0`** (2026-09-28) seeds the sheet solve from the pending list only, not the near/far
order's far carry: water bodies part a's candidate a, which did not break the far-sheet loop. The
settle term's `woke N cell(s) (F far sheet(s) levelled or packed woke W; P promoted near)` counts the
solve's own wakes. **`--solvefrontnear=1`** (2026-09-28) classes a far cell the solve newly wakes as
near at the next tick: candidate c, which did not break the loop either. **`--solvefrontnear=2`** also
pulls front cells already in the carry near (through the carry's reclassify walk, O(backlog) a tick).
**`--solvewakeup=0`** stops the solve waking the cell above one it drained (his "settle down but not
up"); water can then hang over room. The wake term's `N of all above a drained cell` counts those wakes.

**The `fluidmove` profile line** (2026-09-27) sorts the window's cell visits by how much each
moved: rested, under 10 mL, 10–99 mL, 0.1–1 L, 1–5 L, 5 L and over, and empty or other. Beside each
it prints the litres that size carried and its share of the volume. It says whether a big awake set
is flowing or dithering. It is on every profile report and needs no flag.

The lake over a quarry (2026-09-27) adds **`--lakeoverbite=1`**, the control arm: the plane draws
over every bite as before. By default a sample that is mostly a dug pit not yet full to the seed
draws no plane (`BiteFill`, M8-PLAN "The lake over a quarry — built"). The `M7d:` line carries
`N sample(s) over an unfilled bite (asked, dry of lake, seed cells read)`, and its surface term
carries `N undrawn over an unfilled bite`. The A/B wants a windowed screenshot on two identical
copies.

The shore's ring dilation (2026-09-10) adds **`--shore=<0|1>`**: a dry sample beside a wet one draws
the wet one's plane and the terrain's depth clips it to the voxel; 0 is the stair-stepped arm
every M7 screenshot was taken in. Not profile-only. The `M7d:` line's `surface` term prints the
ring's `shore sample(s)`.

M7c-2 adds **`--pitch=<degrees>`**, the vertical half of `--yaw` (negative looks down), because a
shot of rain going into a shaft has to look down it; and **`--norain`**, which leaves the whole of
the weather in place and removes only the particles. That is the arm that priced them, and the
particles are the only part of M7c that costs GPU time.

**Rain on the player's material lands on it since 2026-09-22** — Matthew: *"rain on my stuff
should land in it"*, after his clay pond would not fill: every column whose top is above its
seed surface (a placed floor, a wall's top, rubble on the ground — `OpenAboveY > SeedSurfaceY + 1`)
gets the rain over it as 15.6 L cells on that top, accrued per chunk column against the sky
column's revision (`RainOnPlaced` in Core), owed by the basin under it (`BasinRainfall.Divert(basin,
µL)`, paid from that basin's own next rain, folded into the global debt at a window move); a dug
column is nature's bowl and Tier A still delivers there through the portals; a column out of the
vertical box waits up to 64 cells a chunk column and then forfeits to the basin, counted. A
10 × 10 m floor takes ~40 kL an hour at the heaviest scaled weather. The `M8b:` line's `rained`
term carries `P onto the player's material over C column(s)` and, when non-zero, `owed to placed
columns / forfeited`; the profile's `water` line the same with rescans. **The oracle, verbatim**:
`--profile --speed=0 --place --stock=64000 --clockscale=20 --measure=40` builds a column under
the crosshair and reads `21 onto the player's material over 144 column(s)` on the `M8b:` line
(`642 cell(s) + 26 onto the player's material`, `0 owed, 0 forfeited` on the profile's `water`
line) with both identities exact, where `--rainshare=0` on the same run reads `0 cell(s) + 26
onto the player's material` — the placed rain alone.

**Rain drops Tier B cells since 2026-09-19** — Matthew: *"not many but just to make you think
about the water runoff"*. **`--rainshare=<percent>`** is the share of the rain over the drawn
box that falls as 15.6 L cells from the top row of the drawn vertical box at a random column
(2 by default, an estimate — a drop every second or so at the heaviest scaled weather; 0 is off,
the arm every figure before that date was taken in); a drop is skipped where the box's top is
inside the ground (underground, no sky), **under a roof (2026-09-22 — the adaptive box's top row
was inside his building and the drops fell from the ceiling like a leak; `skipped of which N
under a roof` on the rained terms)**, or the cell cannot hold. **A drop is rain, not water from
nothing**: each landed cell is owed to the sky through `WaterSystem.DivertRain` and
`BasinRainfall` pays it in microlitres out of the next rain before a litre reaches a basin, the
debt carried across a window move and the first window (the sub-litre remainder's one stated
cost does not apply to it), so an hour in one call still equals an hour in steps with a debt in
it. The `M8b:` line's `injected` becomes `injected … rained N L (D drop(s), S skipped)` and the
identity reads `+ rained`; the `M7d:` line and the profile's `water` line print `rained into it
as cells (N L still owed)`. Headless: `--profile --speed=0 --clockscale=20 --measure=40` reads
`rained 7,953 L (509 drop(s))`, `handed 5,206 L`, `0.0 L still owed`, identities exact; the first
forty seconds of the profile seed are dry, so every recorded oracle reads as before and the r6
control is to the digit.

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
at the crosshair, Ctrl+R 1,000,000 L (2026-09-19, *"100x … as a test"* — a 6.5 m ball, its top
half at once and the rest offered every boundary until it fits), and Alt+R — or Alt+G, since NVIDIA's overlay owns Alt+R on his machine (2026-09-26) — takes back everything
within 2 m**; no material, no JSON, no ID. The
profile's `fluid` block prints every term of `held == injected − removed − displaced`, and **the
bare default headless run is not a streaming control any more**: it opens the played world and
stands in its void (198 drawn at 100 quads a chunk). **The r6 control is, verbatim,**

```bash
../GAME/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path Overburden.Game --headless --quit-after 2400 -- --nosave
```

— 2601 / 325 / 3446 at 411 — because 900 frames is ~6 s and the r6 fill settles at ~7. **`--nosave`
goes after the bare `--` or the engine drops it without a word** (Main warns only about a
`name=value` flag placed before the `--`; a flag after it that nothing reads is printed as unknown since 2026-09-10), and the run then opens the played world, which as of M8b-3a floods on open and
reads nothing like a control.

M8c-2 adds **`--seepmode=spring|sweat`** — every group handing litres round its faces so the wall
weeps everywhere (the default since 2026-09-11, played and liked, and the cheaper arm for the
automaton) or one running spring at a chunk's lowest face with room, the first arm played — and **`--seepscale=<x>`**, the seepage rate in litres a second per square metre of face per
metre of head — 0.06 shipped, an estimate at the nuisance end (1.76 L/s into a 10 × 10 × 3 m stone
room under 5 m, ankle-deep in 3.9 h; dirt 17.5× that), zero builds no index and no drain. The
profile's `seep` line prints faces and chunks, seeping / delivered / skipped-full on the last pass,
litres in the window and in all, burst rescans, the pass's mean and max, **the awake count at
seeping boundaries min/mean/max — the water-bodies decision** — and whether Tier A's `seeped` equals
Tier B's; the `M8b:` and `M7d:` lines carry `seeped`. **A seep provocation is `--fillbasin=2000000
--breach=3 --seepscale=100`**: the smaller pour drains through the shaft's own portals in a few ticks
because the pit holds 1,000 L at its saddle, and the `breach` line now says what the pit holds.

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

The water thread's slice 3b (2026-09-10) adds **`--fluidjob=<0|1>`**: the tick as a scheduler job on a
worker, one in flight at a time, composed and collected on the sim thread — `start.cmd` passes `1`, and
0 is the sim-thread arm every figure before it was taken in. It needs the field (`--fluidfield=0` puts
the tick back on the sim thread and says so), and `--fluidbudget` means nothing on it. The profile gains
**`fluidjob`** — ticks collected in the window against 2 a second, worker wall mean/max, visit and walk
ms, boundaries that waited for a slow tick, refusals inside a tick *and how many were for a chunk that
had arrived* (the only ones that cost anything) — and **`boundary`**, the sim thread's cost at a tick
boundary by pass (settle, hand-off and drain, pour, field sync, publish, capture and compose), mean in
the window and worst over the run; **`fluid ms` on this arm is the boundary's cost**, not a slice's.
The periodic `M8b:` line carries `job tick(s)`, `worker wall`, `boundary(ies) waited` and `boundary
worst`. A quit cancels the job and waits for it (`M8b: cancelled the tick in flight before the quit`);
a fault freezes the water for the session, printed once. Slice 3c adds **`--fieldbakes=<n>`** — at
most that many chunk fields baked at one boundary, 8 by default and an estimate, a very large value
the pre-3c arm that baked a restore's dozens in one frame; the `fluidfield` line prints how many went
past the cap, are still owed, and were baked late and woken. On the job arm the settle's solve runs at
the tail of the job (`solve mean` on the `fluidjob` line) and the `boundary` line's settle reads 0.

The near/far order (slice 1, 2026-09-09) is **`--fluidnear=<rings>`** — the viewer's chunk column
and that many rings, horizontal, visited every tick; `start.cmd` passes 2 and 0 is no order, the
control arm every fixture runs — and **`--fluidfar=<cells>`**, the least far cells admitted a tick
while anything near is awake (512; the quota is `max(--fluidfar, near)`). **Since 2026-09-19 the
machines are near too**: the chunk columns under every pump's intake box and every outlet's mouth
layer (`MachineTick.NearColumns` → `FluidSimulation.SetNearColumns`, once a frame beside the
centre) are near wherever the player stands, because a far outlet's 250 L mouth layer was visited
once in ~260 ticks and its pump stalled `no room` for a session; the `M8b:` line's near/far
bracket prints `N machine column(s) near`, the profile's `fluidnear` line `2 ring(s) + N machine
column(s)`. The wire, against the same run with the hand-over out: `--profile --speed=0
--fillbasin=2000000 --breach=3 --pump=100 --pumpoutlet=48 --fluidnear=2 --measure=40` reads
`4 machine column(s), 11,462 near visit(s) against 0 far` where the control read `0 machine
column(s), 1,126 near visit(s) against 10,336 far`, the same 1,000 L pumped and handed in both.
**A fluid row that names an awake cell holding nothing is read with the index dropped and
counted since 2026-09-20** (`M6:` lines, `N awake index(es) dropped for holding nothing`) — the
reader refused such a row until then, the load job faulted, and a faulted chunk job draws as a
hole; the two writers that produced it (the unview and eviction paths listing every paused cell
as awake) filter to held cells now. **And the solve is seeded from what changed since the same night**: a carried cell is offered to
the sheet solve once, when it enters the carry, and again only when woken again (the bit in
`_queued`, `Wake` clears it, a solve marks what it took, a re-classification keeps the prefix)
— the played worlds had the solve flooding 294k cells a boundary from a 456k-cell carry to level
twenty sheets, the tick at 1.2 Hz; on a copy of the retired world the solve went 92.8 → 5.3 ms
a pass and levelled more, not fewer. `--fluidnear=0` has no carry and is unchanged; the
`settle` term prints `last pass seeded N (M from the carry)`.

The adaptive view radius (2026-09-06) adds six. **`--adaptive=1` turns it on; off is the default
and the arm every recorded streaming figure was taken in, and `start.cmd` passes it on** the way
it passes the light budget. **An on/off flag reads 1/0, true/false, on/off or yes/no, and the bare flag
is on** (`--adaptive`, `--solvewakeup`, `--solvefrontnear`, `--solvecarry`, `--portaldrain`); anything
else is refused with an error and changes nothing. Until 2026-10-01 they read "anything but 0 is on",
so `--adaptive=false` turned it *on*: a headless run started at r3 and grew. The box starts at the floor, falls a ring at a time while the viewer
is moving with a derive backlog over `--shrinkslabs=` (2) slabs of the current box, and climbs a
ring at a time while the backlog is empty and the light step's averaged share of its budget is
under `--growbelow=` (0.5); `--cadence=` (2 s) is the least time between changes, the grace after a
grow and the load's time constant; `--minradius=` (1) is the floor. **`--sprintfor=<s>` moves the
flythrough for that many seconds and then stops** — the hanging-around arm, which `--turnaround`
(reverses) and `--speed=0` (never moves) cannot produce. The profile's `radius` line prints shrinks
and grows, seconds at each radius, the backlog and load it read, and the `fill` line gains `grown`,
the first moment the box stood at its ceiling with light settled — **the figure comparable with a
fixed arm's `light settled`; under a growth the other milestones read the floor's box.**

**F11 was far mode (2026-09-24); since 2026-10-12 far mode is F10, and F11 and Alt+Enter toggle fullscreen**
(ALPHA-PLAN decision 17, his word). What follows names F11 as it was. Matthew: *"just enable processing that far dont change the way
we lazily load"*. It raises the adaptive radius's ceiling to r8h/4v and nothing else: the box
climbs toward it a ring a cadence only while it can afford one, and falls to the floor while
moving exactly as before. Off, the box is trimmed to the configured ceiling at once (the policy
never shrinks from above its ceiling by itself). **`--far`** starts in it. Printed as `M2c: far
mode on/off`, and the HUD's view term reads `r7h/4v (far, to r8h/4v)`. Standing start headless,
`--adaptive=1`: at r8h/4v 22 s in, **5,733 tracked / 756 drawn against 2,601 / 325, working set
254 against 203 MiB, the light step's mean load 0.32 against 0.13**; first draw unchanged. The
profile's `grown` milestone still reads the configured ceiling (r6h), not far mode's.

**A second rung, r10h/6v (2026-09-28)** — Matthew: *"6 horizontal to 8 and 10 with the vertical
distances set in increasing values respectively"*. F11 now steps 6h/2v → 8h/4v → 10h/6v → 6h/2v:
up a rung climbs lazily as before, and back to the start trims at once. **`--far` or `--far=1`**
starts on the first rung, **`--far=2`** on the second, `--far=0` is off. The `M2c:` line reads
`far mode on, rung 2 of 2`, the HUD `(far 2, to r10h/6v)`. Headless, `--nosave --adaptive=1
--lightbudget=16`, 4,000 frames, one run each: **rung 2 at r10h/6v is 10,625 tracked / 1,486 drawn
/ 499 k quads, working set 375 MiB, against rung 1's 5,733 / 756 / 272 k and 281 MiB**. 0 `ERROR`;
the r6 control unchanged to the digit.

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

**Ctrl+Z undoes the last of three edits** (2026-09-22): the voxels, the inventory exactly
inverted and the machine rows, written back through the same `SetVoxel` so light, support, the
surface and the mesh repair themselves. **A fall clears the stack** (rubble is minted, so undoing
the bite would restore the roof and keep the rubble) and **so does a chunk leaving the box** (it
reloads its edited state), both printed — and a press after a fall says so rather than saying
nothing, his word on the first play (*"Ctrl z feels good, disallow if something falls yes"*); a step whose credit has been spent is refused whole and
kept; no energy is charged or refunded, and the water is not undone. `--undo=<n>` presses it
headless from `--undoafter=<seconds>` (3), and the `M10:` line carries `undo N step(s) / M
voxel(s) held`. **Building is bound to the right mouse button**, `B` cycles the brush (cube, slab, ramp, sphere),
and **`1`–`9` pick a material from the hotbar** (`V` still cycles to the next one). **`T` cycles
the tool tier** — it was the number row until M6d-3, and the row went to materials because that is
where a hand expects to find what it builds with. `--place` holds the button for a whole
profile window, `--stock=<voxels>` seeds the inventory so such a run measures the brush rather
than the carve, and `--brush=<name>` picks the shape. **M10a-2 adds three beside them**:
`--material=<id>` picks what `--place` places (`--stock=` seeds every material alike, and the
controller's largest-holding pick lands on grass), `--standfor=<seconds>` stands at spawn that long
before the flythrough moves (`--place` aims level while moving and places nothing — M6d-2 — so a body
placed and then unloaded needs the stand first), and `--abort=<seconds>` ends the process with no
save at all, the crash arm the machine reconcile is checked against. **A machine material places as
one 1 m body whatever the brush, mode or modifier; `P` puts a relay pump and a relay outlet in the
inventory** until M10g's recipe exists. The orphan check, verbatim, on a scratch world:

```bash
../GAME/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path Overburden.Game --headless -- --profile --speed=12 --standfor=5 --pitch=-60 --place --stock=64 --material=overburden:relay_pump --world=probe --autosave=100 --abort=30 --measure=40
```

then reopen `--world=probe` and read `M10: drain — … 128 orphan voxel(s) cleared`; delete the world after.

**M10b-1 adds the computer and one flag.** `overburden:computer` is a material with `unit: count`
and `placeable: false` — carried, counted and on the hotbar, never a block: `Place` refuses an item
as it refuses air, and the controller routes the right-click before the brush, so with the computer
selected a right-click *edge* on a machine body under the crosshair (the hit voxel's cell — mining's
aim, not building's) raises the panel request that M10b-2's panel opens, and on anything else does
nothing. `P` seeds one beside the relay pair. **`--port=<pump|outlet>:<name>=<value>`** writes a port on
one of `--pump`'s bodies through `MachineSet.SetPort` — clamped, counted, printed (`clamped to 15`) —
and the tick reads the ring the write made at the next boundary; the `M10:` line and the `machines`
profile block print `port writes N (M clamped), computer O open(s) / M miss(es)`. Weights are the
ring (the smooth weighted round-robin; all-ones is the plain round-robin), and the shipped pump
declares no `weight` — only an outlet's is read. The port-write oracle, verbatim, reads `pump's
channel := 3: set to 3`, `outlet's weight := 99999: clamped to 100` and `stalled 60 no outlet`:

```bash
../GAME/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path Overburden.Game --headless -- --profile --speed=0 --fillbasin=2000000 --breach=3 --pump=100 --port=pump:channel=3 --port=outlet:weight=99999 --measure=30
```

With `--world=probe` on that run and a reopen of `--world=probe`, the reopen reads `2 machine
row(s) read` and `stalled … no outlet` — the write survived the quit; delete the world after.

**M10b-2 adds the panel and two flags.** With the computer in hand a right-click on a machine body
opens `MachinePanel` — the label, the cell, one `SpinBox` per declared port with its bounds, a status
line from the row's last stall and move (`moved 50 L at the last boundary`, `stalled: no outlet on
channel 3`) — and hands the mouse to it; every change is one `SetPort`, printed as `M10: panel — …
channel := 3: set to 3`. Esc closes it (`Main`'s while a panel is open; every other key is ignored
there), so does a click outside (the camera's own capture) or the row leaving the set, **and the
click that takes the mouse back is swallowed by both verbs until it is released** (`FlyCam.ClickLive`
— before b-2 the re-capture click swung at the crosshair, which for a panel is the machine it was open
on); **flight is gated on capture now**, as walking has been since M4c. **`--openpanel[=outlet]`**
opens the panel on `--pump`'s pump (or outlet) as it is placed — a screenshot prop, so like
`--debuglamp` it leaves the played world alone, and built with the HUD so a `--profile` run has none —
and **`--panelset=<name>=<value>`** types into the open box, through the box, so the widget's wire
runs headless (`--port=` writes past it). The panel prints its status line on every stall-kind
change, which is what a headless run reads. The screenshot, verbatim — the pump's panel with
`moved 50 L at the last boundary`; add `--port=pump:channel=3` for the amber `stalled: no outlet on
channel 3`, or `--openpanel=outlet --port=outlet:weight=7` for the two-row panel:

```bash
../GAME/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path Overburden.Game --script res://screenshot.gd -- C:/tmp/panel.png --fillbasin=2000000 --breach=3 --pump=100 --openpanel --settle=9000
```

The widget's wire, headless and verbatim, reads `typed 99 into channel's box`, `channel := 15: set to
15`, `port writes 1 (0 clamped)` and then `stalled: no outlet on channel 15`:

```bash
../GAME/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path Overburden.Game --headless --quit-after 1200 -- --nosave --fillbasin=2000000 --breach=3 --pump=100 --openpanel --panelset=channel=99
```

**M10a-3 adds the pump's two.** `--pump=<L/s>` places a relay pump at the bottom of the shaft
`--breach` bites and a relay outlet on the surface `--pumpoutlet=<metres>` along +X (24 by default),
both on channel 0, at the window's open — through `Place.Machine` where the outlet's chunk is loaded
(the cell carved first: a body is 64 air voxels and the shaft is three wide) and as a row alone where
it is not. **The rate is every pump's, a sweep seam over the content's 100 L/s** (the shipped relay
pump says 100 and a row's definition is the content's; `MachineTick.RateOverrideMillilitresPerSecond`,
the shape `--seepscale` has over the JSON's permeability). The `M10:` line's `tick:` prints both
tiers' pumped and emitted, `pumped == emitted across the tiers`, the five stalls (`no outlet`, `no
water`, `no room`, `unloaded`, `unanalysed`), the intake voxels read and the tick's mean and max; the
`boundary` line carries `machines`; the profile has `machines` and `machtick`; the `M8b:` and
`fluid` identities read `+ emitted − pumped` and the `water` line prints `pumped out of lakes` and
`emitted into basins`. **The loaded arm, verbatim — the lake drains through the portals into the
shaft, the pump takes it, the outlet pours it on the surface, the hand-off returns it to the basin;
`drawn 1,000 == pumped 1,000 == emitted 1,000`, every identity exact:**

```bash
../GAME/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path Overburden.Game --headless -- --profile --speed=0 --fillbasin=2000000 --breach=3 --pump=100 --measure=40
```

**The unloaded arm is `--pumpoutlet=96`, not 64**: the loaded box is r6 plus the two-chunk margin,
64 m from the *viewer*, and the shaft stands at the basin's pit rather than under the viewer, so 64 m
from the shaft is still loaded. At 96 the run prints `a row alone (its chunk is not loaded — the
unloaded arm)` and the profile's `machtick` reads `emitted 0 L into Tier B + 1,000 L into Tier A`
with `handed 0` — the litres never rested anywhere. Add `--fluidjob=1` to read the `boundary` line.

**M10c-2 adds the pool's wire and three flags (2026-09-17).** The pool is on: a pump pays its
`joulesPerLitre` from the placed cells and panels on its channel, stalls `unpowered` — the sixth
stall, after `no water` — with a litre's worth short, and what no cell holds is `clamped`.
`overburden:solar_panel` (500 W, a converter: `sky` via `solar`, `out` via `channel`),
`overburden:vacuum_cell` (5 GJ, placed) and `overburden:lead_acid_pack` (3.6 MJ, 250 W recharge — 1 kW until the first play, 2026-09-18,
carried — an item like the computer; equipping it is c-3's) ship; the panel and the pack ride the
channel class until M10i, printed at open. A panel's boundary is `ratedWatts × 500 × open/16 ×
(256 − overcast)/256` — the openness counted off the streamer's sky columns
(`StreamerSolarSky`, the light's array counted rather than all-or-nothing) and kept on the row when
its column is unloaded, the overcast a byte taken once a boundary from the same `--weather`-or-forecast
expression the shader reads — **so `--weather=1` dims a panel to 976 mJ a boundary, never zero.**
**`--pump` places a charged vacuum cell beside the outlet** (the relay is 200 panels, so a-3's
oracle keeps reading; `--cell=0` omits it and that run reads `59 unpowered`); **`--solar=<n>`** places
panels west of the outlet a metre apart; **`--nogate`** is the pre-M10c arm — the pool off, every
pump free (and, from c-3, the miner free), printed. `P` seeds a panel and a cell beside the relay
pair and a pack once; **`Alt+P` fills every placed cell to capacity**, printed — the debug charge
M10d's relay runs on. `--openpanel=cell|panel` opens the panel on those bodies. The `M10:` line and
the `machines` block carry `unpowered` and a `power:` term ending `pool == entering − leaving +
generated − drawn − clamped: exact`; the profile has a `power` line with the window's terms. **The
storm's oracle, verbatim — ten panels, no cell, a clear sky: the profile's `power` line reads
`generated 150,000,000 mJ` over the window's 60 boundaries with `open 16.00/16`, `sky answered 600`;
with `--weather=1` in place of `--weather=0` it reads `generated 585,600 mJ`:**

```bash
../GAME/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path Overburden.Game --headless -- --profile --speed=0 --fillbasin=2000000 --breach=3 --pump=100 --cell=0 --solar=10 --weather=0 --measure=30
```

On the bench `--pump` gains a powered row — 500 pumps with 500 charged cells against the free
arm, 3.03–3.11 ms against 3.00–3.01 mean over two runs, the pool inside the bench's own spread — and its loop runs
powered, the cell paying exactly the bill over 10,000 boundaries with both identities exact.

**M10c-3 adds the pack, the gate and three flags (2026-09-17).** The entangled miner is content:
`tools/entangled_miner.json` (**`quantum_miner.json` since 2026-09-19, with `quantum_excavator.json`
beside it and a `bite` field on the tool — the quantum tiers on the T cycle; see the pick-up
paragraph of that date**; threshold 5.0 — it takes a machine body at range as a pick does,
which reverses M3d's *entanglement never changes what the bite is hard enough to take*; 0.10 s a
cube with `--cubecooldown=` a sweep over the file's number; `power { via: channel, joulesPerVoxel:
50 }` — **100 until M10c's first play read a 56 s pack as *"you don't get far"*, then 20 for an
hour until *"I might have appealed to your generosity too much … I am having trouble fighting
solar recharge on the channel I am on"*; 50 J and the pack's recharge 1 kW → 250 W together make
it 1,125 cubes (2.2 chunks, 1.9 min of non-stop bites) and a cube every 12.8 s sustained on a
full channel, four hours to refill — the price is the burst and the recharge is what you fight,
and both are the game's numbers where the pack's 3.6 MJ is a real battery's**), loaded by ID and
**off the T cycle** until 2026-09-19 (so that `T` cycled the two free picks and only Q cost; on
the cycle since, with `ToolTiers.Default` starting a world on the strongest *free* tool). **The
gate is a voxel budget on every `Carve` shape** — what the equipped pack can pay for
at the tool's price, asked after air and after `TooHard` so neither costs, the rest of the shape
in `CarveResult.Unpowered` — and the pack pays for exactly what came away: *takes what the charge
buys and stops*, a bite that ends mid-cube. The fill at range was all or nothing on the shape's
cost and refused whole under it **until 2026-09-20, when it took the bite's rule** — lands what
the charge buys, the rest `PlaceResult.Unpowered`. **The pack rides header v4** (a presence byte, its material
through the table, its charge, its channel; a v3 world opens with none); a right-click edge with
a pack in hand equips one from the count — the fullest carried, since 2026-09-18 — and swaps it
for the one worn, **whose charge is stowed beside the count with it** (`CarriedPacks`, header v5;
it was *forfeited* until the first play's swap threw 2,650 kJ away and Matthew said *"keep its
charge as an item"*) — a term in the identity (`… − stowed: exact`), printed with the swap
count; an empty pack has no row, so a pack swapped out at 0 J loses its channel. The
computer's click on nothing opens the pack's panel (one `channel` box). The HUD reads `pack
2,100.0 kJ / 3,600.0 kJ (58.3%)` while one is worn — **the share since the first play, because
`3,994 J of 3,600.0 kJ` read as over the cap** — and `Q TAKES NOTHING — no pack` under Q without. `P`
seeds a pack once; `Alt+P` fills it with the cells — **through `MachineTick.FillPack` since
2026-09-19, because a `Give` on the pack alone is the pool's recharge path and a fresh world's
7 kJ pack filled that way broke the pool identity by 3,593 kJ on every boundary for a session
(1,403 error lines; the `power:` term prints `debug-filled into the pack` and the identity
`+ debug` when it has)**. **`--pack[=<id>]`** equips one at open,
**`--charge=<J>`** sets it, `--openpanel=pack` opens its panel, **`--nogate`** frees the miner
and the fill with the pumps. **The bite-that-stops oracle, verbatim, reads `removed 6, unpowered
57 of 64; paid 300 J, the pack at 20 J` on the first swing** (the spawn's cube holds one air voxel,
which costs nothing — the pass wrote 58 for a solid one; `--charge=640` and `paid 600 J, the pack
at 40 J` at the 100 J price), then `removed 0, unpowered 57`, and the profile's `gate` line
`6,840 voxel(s) unpowered over 120 swing(s)`; without `--pack` it reads `gate swung 120, took
nothing (no pack …)`; with `--nogate` in place of `--pack --charge=320`, 63 removed a swing and
`gate off`:

```bash
../GAME/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path Overburden.Game --headless -- --profile --speed=0 --pack --charge=320 --mine --entangle --measure=10
```

The fill's arm — `--place --entangle --stock=64000 --pack --charge=3200 --pitch=-15` — places one
cell and refuses the next (`the fill at range needs 3,200 J in the pack and it holds 0 mJ`); at
`--charge=1600` it refuses the first. The swap, headless — the flag's pack worn, the count's
equipped over it by the held right-click — reads `the outgoing pack is back in the inventory
holding 1,000 J … 1,000 J stowed by 1 swap(s)` and the identity exact; with `--world=probe` a
reopen reads `header v5` and `1 carried pack(s) restored … holding 1,000 J`, and a second swap on
it `1,000 J of 3,600.0 kJ (<0.1%) from the inventory`:

```bash
../GAME/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path Overburden.Game --headless -- --profile --speed=0 --place --material=overburden:lead_acid_pack --stock=64 --pack --charge=1000 --measure=5
```

With `--world=probe --pack --charge=1000` and a reopen of `--world=probe`, the reopen reads `a
pack equipped (header v4)` and `equipped from the world file: 1,000 J`; delete the world after.
**`--stock=` seeds an item at one rather than at the stock since c-3**, and `--place` selects a
placeable material first — every `--place` profile since M10b-1 had selected the computer and
placed nothing (`computer 0 open(s) / 1 miss(es)` where its placements should have been).

**IM-2 adds the quantum backpack, a window, and the pumps' priority (2026-09-29,
IDEAL-MACHINES-PLAN decisions 7–9 and 41).** `overburden:quantum_backpack` holds nothing: a tool
paying through it draws straight out of its channel's enabled cells, last-filled first, between
boundaries. The recharge and `Alt+P` give it nothing, and the header saves its charge as 0. `P` does
not seed one until IM-6 gives the HUD a bank line; **`--pack=overburden:quantum_backpack`** wears one
at open, and `--stock=` carries one. The open line reads `a window onto channel 0's cells, … in the
bank`, the gate's lines say `the bank on channel 0`, and the `power:` term prints `pack initial` and
ends `pool == entering − leaving + generated − drawn − clamped + initial − taken − stowed`. The relay
pump's panel gains a **`priority`** box, 0–9 at 5, higher paid first and ties by position (X, then Y,
then Z); `--port=pump:priority=9` sets it headless. **The window's oracle, verbatim, reads `paid
306,650,000 mJ` and `drawn 921,426,000 by pumps + 306,650,000 by the tool … exact`, twice alike**:
the swings before `--pump` places its cell meet an empty bank (`the bank on channel 0 is spent`,
1,638 voxels unpowered over 26 swings), and every swing after it pays from the cell:

```bash
../GAME/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path Overburden.Game --headless -- --profile --speed=0 --fillbasin=2000000 --breach=3 --pump=100 --pack=overburden:quantum_backpack --mine --entangle --measure=10
```

**2026-10-12: the lithium-ion pack, `P`'s hundred, and the hotbar in cubic metres** (his asks).
**`P` adds a hundred of each item on every press** (`DebugSeed`): relay pumps, relay outlets, solar panels
and vacuum cells (100 bodies, `× 100`), 100 m³ of clay and of glass, and 100 computers, lead-acid packs and
lithium-ion packs; a content missing any of them seeds nothing and names it. The quantum backpack is still
not seeded (IM-6). **`overburden:lithium_ion_pack`** is a carried pack like lead-acid: 14.4 MJ, 2 kW,
channel class. **A volume on the hotbar reads in litres under a cubic metre and in cubic metres from one
up**, floored (`VolumeText`). Three probes, each read 2026-10-12 with no `ERROR`. Worn at open, an over-full
`--charge` capped: `lithium ion pack equipped --pack: 14.4 MJ of 14.4 MJ (100.0%) on channel 0 — 288,000
voxel(s) at 50 J each`, and after ten seconds of the miner `the pack at 14,016,550,000 mJ (14.0 MJ)`:

```bash
../GAME/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path Overburden.Game --headless -- --profile --speed=0 --pack=overburden:lithium_ion_pack --charge=20000000 --mine --entangle --measure=10
```

The swap, the flag's lead-acid pack for the count's lithium-ion: `lithium ion pack equipped in place of lead
acid pack (right-click with a pack in hand): empty, 14.4 MJ capacity, channel 0; … the outgoing pack is back in
the inventory holding 1,000 J`:

```bash
../GAME/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path Overburden.Game --headless -- --profile --speed=0 --place --material=overburden:lithium_ion_pack --stock=64 --pack --charge=1000 --measure=5
```

The recharge off `--pump`'s charged cell, empty at open: `pack given 54.0 kJ` over 54 boundaries (1,000 J a
boundary, 2 kW) and the identity `exact`; with `--pack=overburden:lead_acid_pack` the same run reads `pack given
6,750 J` (125 J a boundary, 250 W), exactly an eighth:

```bash
../GAME/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path Overburden.Game --headless -- --profile --speed=0 --fillbasin=2000000 --breach=3 --pump=100 --pack=overburden:lithium_ion_pack --charge=0 --measure=30
```

Measure **the relay pump** — the number every pump cap in the ladder was fiction until, and the read
priced both ways. The chamber fixture flooded and settled, a relay pump on its floor's centre, an
outlet on the surface with a sink under it standing in for the hand-off: the delivered rate against
the nominal at intake radius 1 and 2 (100.0 L/s at both — the settle refills the box from the whole
sheet every boundary, so the radius buys nothing on a sheet and costs 4.8× the read), the box through
`AmountAt` against the published bitset walk (3.2 ns a voxel against 19.1), the tick at 1 / 100 / 500
pumps wet and 500 dry (0.02 / 0.6 / 3.0 ms; 0.02 ms dry off `HeldIn`), and the loop — the outlet
inside the chamber, 10,000 boundaries, the world's total unchanged to the millilitre. Release only;
`--pumplitres`, `--pumprate` and `--pumpboundaries` (space-separated) size it:

```bash
dotnet run --project Overburden.Bench -c Release -- --pump
```

**IM-5b adds `--pile` (2026-10-11), decision 15's bench, and the first arm to build a real
`ChunkPipeline`** (a scheduler, streaming and light, with Main's hook wired: the field's invalidation,
`Disturb`, the portal tracker with its seep index, the machine set and the enclosure, and the support
region). It writes N stone voxels a boundary over K chunk columns on generated ground, a layer at a
time, dry and then wet (one cell of water in each chunk column, so the field holds them and every
write clones). A boundary is timed in the game's order: the tracker's `Flush`, the writes, the field's
`Sync`. The frames between boundaries are timed apart: 500 ms of `Update`, under the 16 ms light budget
`start.cmd` passes. Each arm prints chunks written, the boundary's mean and worst (and its first, which
pays the tracker's first-edit scans), µs a voxel, KiB allocated, and the frames' update ms and worst
frame, with light builds, remeshes, meshes, support derives and their worker ms a boundary. **It cannot
see the game's mesh installs on the main thread**; the remesh count is there to read the in-game probe
against. `--pilevoxels a,b` (128,512,2048), `--pilecolumns a,b` (squares; 1,4,16) and
`--pileboundaries n` (12) size it. About three minutes:

```bash
dotnet run --project Overburden.Bench -c Release -- --pile
```

**The world file lives at `user://worlds/<name>/world.db`** — on Windows,
`%APPDATA%/Godot/app_userdata/Overburden/worlds/`. `--world=<name>` picks one, `--nosave` opens
none at all, and `--autosave=<seconds>` moves the 30 s flush. **A run that is a measurement or a
screenshot prop does not open the played world**: `--profile`, `--fall` and `--debuglamp` leave
persistence off unless `--world=` names one, because `--mine` would carve up the world somebody
plays and because a run that *loads* what an earlier one saved is not comparable with M2d's
figures. Naming a world is the explicit opt back in, and it is how M6d measures the load path.
**A name with spaces works and has to be quoted whole** — `start.cmd "--world=Deep Mine"`, because
`start.cmd` forwards with `%*` and cmd splits on spaces; unquoted, `--world` arrives with an empty
value, is refused out loud and the run opens `default`. Banned characters are Windows' own
(`\ / : * ? " < > |`), the name is case-insensitive, and a world that does not exist is created.

**A new world's seed is random since 2026-09-23, and `--seed=<n>` asks for one** — decimal or
`0x` hex, refused out loud rather than as an unknown flag, because a mistyped seed falling through
to the default would hand you a *random* world and the two are indistinguishable from inside.
**It applies only to a world that does not exist yet**: an existing world regenerates its unstored
chunks from its header, so a seed there would rebuild the terrain *around* the chunks it has saved
— ignored with a warning, the `--cavestep` rule and its reason exactly. **A measurement keeps the
fixed `0x4D494E455243` constant**: a new world created under `--profile` (and so `--fall`) is
seeded with it, and so is every run with no world file, because the r6 control's 2601 / 325 / 3446
at 411, the keystone's 18,718 at risk and *"the first forty seconds of the profile seed are dry"*
are all statements about one landscape, and a random seed under `--profile` would retire every
recorded figure in this repo silently. `--seed=` still wins there, so a measurement on other
terrain can be asked for; it just has to be asked for. The draw is nine digits rather than
sixty-four bits because the point of a seed is being written down, and splitmix64 decorrelates a
small number as happily as a large one. The `M6: new world` line carries both forms
(`seed 0x292EC410 (--seed=690930704 builds this landscape again)`), and **the HUD shows the world's
name and seed in the upper right for the first few seconds** — full for ~8.5 s from launch, fading
over ~3, gone by ~12.5 (measured by screenshot at seven settle times; the offset from his "ten
seconds" is the ~1.5 s the world takes to open, and the whole thing is deliberately lossy). It is
driven off the frame delta `Hud.Refresh` already takes, so **a profile run has no banner**, the HUD
being skipped there.

**`--speed=0` means a standing start and is distinct from omitting `--speed`**, which defaults a
profile to 40 m/s. That distinction was added at M5d-1, because the standing start is the only
arm in which the chunk set at a fixed radius is deterministic — so AO on against AO off compares
two numbers rather than two samples of a moving camera — and it was the one arm that could not
be asked for.

**How mountainous a world is, and where — set when a world is created, three dials, since
2026-09-23.** `SurfaceAmplitudeVoxels` is the amplitude **in the most mountainous places** now;
**`ReliefBaseVoxels`** is the amplitude in the flattest, and **`ReliefRarity`** (0..1) is how much
of the map is mountain — fields fourteen and fifteen, at **header v7**. A column's amplitude is
`base + (peak − base) × share(n)`, where `n` is one octave of a second noise field at
**`WorldGenerator.ReliefFrequencyRatio`** (32) times the surface's wavelength — about **2 km a
region** — and `share` is a square ramp above a threshold that rarity slides. **Equal floor and
ceiling is a uniform world and skips the sample entirely**, which is the record's default, what
every older world reads back, and what a `--profile` run gets: the r6 control is 2601 / 325 / 3446
at 411 to the digit with regions shipped. The flags are multiples of the record's own 48:
**`--relief=<x>`** the ceiling, **`--reliefbase=<x>`** the floor, **`--reliefrare=<0..1>`**, all
three new-world-only under the `--seed=` rule — refused out loud when mistyped, warned and ignored
on a world that exists (an unedited chunk regenerates from the header, so honouring one would
rebuild the landscape around the chunks a world has saved). A floor above its own ceiling warns
once and builds a uniform world; the generator refuses it outright, as it refuses a NaN rarity.
**A new world played rather than measured gets base 0.4×, peak 3×, rarity 0.25** — a measured row,
53% flat / 37% rolling / 10% hilly over 1,024 windows in 32 km, peaks to 36 m. **Judged in play
2026-09-24 and kept** — *"Leave the default … hills seem common enough even if you dont directly
see them from the start"* — after a world at rarity 0.125 read well too; the map's 2 km default
and the compass were confirmed the same day.

**The HUD says where you are since 2026-09-23** — `x 108  y 11  z 6 · facing 315° NW · relief
8.1 m`, on its own line over the status line, at the status line's own 10 Hz. **The bearing is
taken from the camera's forward vector through `Overburden.Core/Navigation/Compass.cs`, never from
its yaw** (a yaw needs a sign convention, and a compass pointing consistently the wrong way looks
exactly like a compass); **north is −Z and east is +X**, pinned by `CompassTests` and shared with
every map this project prints. The relief term is `SurfaceAmplitudeAt` in metres — how you tell a
plain from a highland while standing in one, which matters because a region is 2 km and the view is
48 m. Built because a play check that said *"the hills are north-east"* could not be carried out:
there was no position readout, no heading, and no sun since M5c-2.

**Every world's origin sits somewhere different inside its own region, and before 2026-09-23 it did
not.** Gradient noise is *exactly* zero at its lattice points and world origin is one, so the relief
sample at (0, 0) was 0 whatever the seed: four worlds on four seeds all read an amplitude at spawn of
**34.6 voxels, to the digit**, and the one place a player judges a world from was the one place that
could not differ — which is what *"I tried a few worlds"* turned out to mean. A seed now offsets its
own origin by up to a whole region (`_reliefOffsetX` / `_reliefOffsetZ`, a hash as a fraction of a
lattice cell). Measured after: **30 distinct spawn amplitudes of 32 seeds, spanning 81% of the
floor-to-ceiling range**, from a world that starts on a plain to one that starts in hills;
`TwoWorldsDoNotLookTheSameWhereEveryoneStarts` reads `only 1 of 32` without it. **It cannot move a
uniform world, which is why it needed no care**: a uniform world never reaches the sample, so the r6
control is untouched by construction. **The same is true of the surface noise and is deliberately
left alone** — the spawn column has been exactly sea level in every world this project has ever
made, and offsetting that would move every landscape including the fixed profile seed's.

**Two shapes were tried and the first was wrong, which is why the second is written down with its
sweep.** v6 (one day, 2026-09-23) scaled the amplitude by `1 + variation × n` — a multiplier
*centred on 1*, so the typical place gets the typical amplitude and turning the relief up makes
everywhere bigger together. Matthew's verdict was *"massive hills idk, like I kind of want them
somewhere but not everywhere"*, and the sweep agreed: at `--relief=3 --reliefvary=0.5` the
flattest 200 m window in 32 km still held 21.8 m of relief, and **at variation 1 nothing anywhere
was flat**, because Perlin rarely reaches its extremes so the factor sits near 1. A floor and a
ceiling cannot have that problem. **The mask that replaced it has its own trap and the table
caught it**: the obvious `max(0, n)^k` puts the threshold outside the noise field's own range once
k ≥ 2 — 2D gradient noise spans about ±0.7, not ±1 — so a sweep reads "0% hilly" for what looks
like a plausible rarity and is the feature switched off. `ReliefLowestSample` / `ReliefReachableSample`
are calibrated against that; moving them moves what every rarity means. **Cost, measured headless
at r6, shaped against uniform**: the fill's derive +2.5% and all-settled 4.03 → 4.05 s, invisible
end to end; the box-difference sweep — `SkyColumn.Seed`'s 1,024 surface evaluations a column —
goes **`viewer ms` mean 6.53 → 7.82, p50 6.49 → 7.80**, about 1.3 ms against a 16.7 ms frame.
**Named lever, not built**: the amplitude is near-constant over 2 km, so it could ride a coarse
lattice and be interpolated the way cave noise already is.

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
