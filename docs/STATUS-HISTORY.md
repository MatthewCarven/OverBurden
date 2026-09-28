# Status history

Every "where things stand" and "pick up here" paragraph `CLAUDE.md` carried from M2 to
2026-09-26, moved here verbatim on 2026-09-26 in a documentation consolidation pass, newest
roughly first (the order they sat in). `CLAUDE.md` now carries only the current state. Each
paragraph was written as a session's hand-over, so its *next* is long done; what is still worth
reading here is the **things not to rediscover** each one names and the milestone summaries at the
bottom (M2–M8), which are the shortest account of why the engine is shaped the way it is. The
plans in this folder and `WORKLOG.md` hold the full reasoning.

The appendix at the end is the orientation table as it stood, before its rows were cut to a line
or two each.

## Where things stood

**Moved from CLAUDE.md on 2026-09-28** (the 2026-09-26 hand-over, as it stood with its 2026-09-27 amendments):

The paragraphs below are 2026-09-26's; their collapse items are current up to part e. `start2.cmd`–`start5.cmd` and `pondlook.cmd` (his tower) are Matthew's and
deliberately untracked.

**The Tetris collapse is the default since 2026-09-27** — played on his tower with the pieces
drawn: *"make it the default please"*, and the cyan glitch *"like it"* (COLLAPSE-PLAN, "Part c —
played"). **Every M9 oracle recorded before then wants `--collapsemode=sweep`** (COMMANDS). **The phonk is built (part e)**: his track in `media/` (gitignored) plays while a collapse is
live, sped up with its pace — his ear is owed. Next are parts d and f; decision 11 (lifts off a reservation, up to 4.5 m) waits on his "fix it". One
`M9: event` line an event, a `pieces:` term on the `M9:` line. The re-cut runs on a worker (the
room's frame p99 went from ~260 ms to ~9 ms). **Played on his tower 2026-09-26: *"yes slow to
fast"*** — the pace stands; part c's open questions are in the plan's "Played". **His tower was restored 2026-09-27** from
`backup-Regions16-pondlook-2026-09-26`; the collapsed one is `Regions16-pondlook-pieces-2026-09-27`.
Restore the same way again when he asks — never a delete. **The keystone oracle is a race** (COMMANDS): read `at risk 0` and lifted == returned,
not its falls. Two-thirds of a big collapse is rubble tumbling off the pile (65% in his play). The collapse
sensor (part f) reads `CollapseDirector.Events`.


**The game is Overburden as of 2026-09-03, and stages 1–3 of the rename are done** — the
decision; the solution, projects, folders and namespaces; and the content namespace, the mod
folder, `config/name` and the `user://` folder. **Stage 4 is Matthew's**: the GitHub repo, the
local folder, the remote URL, and the assistant's memory directory (see TODO's name-check item).
**The played worlds now live under `app_userdata/Overburden/`; the `MinerCraft/` folder beside it
is the pre-rename backup and is Matthew's to delete.** **The flooded world of 2026-09-19 —
sixteen pumps, forty-three outlets, 21.1 ML of Tier B in 969 rows — is
`worlds/retired-flooded-2026-09-19/`**, retired on his word after a clean quit. **And the spread
world of 2026-09-22 — 209 machines, 66.6 ML of Tier B in 2,393 rows, a lake let into the mine
and spread thin over the plain, 3.4 M cells active at its open, the solve at 1.8 s a pass and the
surface at its worst case (359k quads, 294k of them sides) — is `worlds/retired-spread-2026-09-22/`**,
retired on his offer (*"do you want to retire this world as a load test?"*) after a clean quit:
**the load test**, the measurement world for the solve lever, the surface's settled-state read and
the memory `live` term, to be opened only as a copy with `--world=`; his `default` is fresh from
the next launch. **And the pool world of 2026-09-22 — 127 machines, 7.09 ML of Tier B in 1,467
rows, of which 6.27 ML is below the seed surface; a walled pool on the plain holding a 450 kL
pour with `enclosed 82,543 kept / 1,052 closed`, pumps on stone pedestals, and a mine opened
with the quantum excavator — is `worlds/retired-pool-2026-09-22/`**, retired on his offer
(*"retire the world if you want it for testing"*) after a clean quit: **the enclosure's
measurement world at scale, and the fixture for M10d's *dry* recipe**, since its underground and
surface water are both large. It is also the first world written by a build with the enclosure,
rain-on-placed and the undo in it. Open it only as a copy with `--world=`; his `default` is fresh
from the next launch.

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

**Also that night: the Debug build the game runs is JIT-optimised now** (`Directory.Build.props`,
`Optimize` on the Debug configuration — the editor host loads `bin/Debug` and every play from M2 to
2026-09-15 ran with the optimiser disabled). Bracketed by the r6 control (streaming to the digit,
derive 3,976 → 924 ms) and a played-world open (first draw 8.74 → 2.90 s, fluid job wall 312 → 66 ms,
boundary field sync 21.5 → 4.0 ms mean), both identities exact, suite 1,412 green. **Every figure
before that date is an unoptimised-Debug figure; do not compare a profile across it.** The release
export with SQLitePCLRaw's natives is still the unverified step and was not needed for this.

**Pick up here (2026-09-26, late night). The collapse's part a is built — a-1 and a-2, all Core:
`CollapsePace`, `Polycubes`, `PieceCutter`, `LandingSolver`, `CollapseEvent`; suite 1,937; the
bench's `--support --collapse --pieces` holds every oracle on every fixture. Next is part b, the
wire, which wants its own code-level pass first (TODO under M9 lists what it carries). Read the
plan's "a-2 — built": half of a big collapse is rubble tumbling off the pile, and whether that
reads as settling or churn is his. His next chat is the ideal machines, so b may wait.** The
paragraph below is the earlier state.

**Earlier (2026-09-26, night). All fourteen collapse decisions are taken (*"as
recommended"*), and a-1 is built: `CollapsePace`, `Polycubes`, `PieceCutter` in Core, suite 1,922.**

**Earlier (2026-09-26, evening). The collapse's planning pass is written —
[docs/COLLAPSE-PLAN.md](COLLAPSE-PLAN.md), twelve decisions, his three answered (the voxel
with a growing mino, 1 → 10 m/s, the chimney to the sky once serious); the other nine wait on
his word, then part a (Core: the cutter, the solver, the pace table) is built first. Decisions
13 (a collapse sensor on a channel, his *"cut power … deliberately flood the place"* with the
flood half answered no) and 14 (water slows a falling piece) were added on his yes. **His next
chat moves around the project to "ideal" machines — endgame tech first, for a creative-mode
demo people will want to test**; the sensor's design goes there.** The
paragraph below is the afternoon's state, kept for the reasoning.

**Earlier (2026-09-26, afternoon). The water waits; the Tetris collapse is next, in a
fresh chat on `main`: read [docs/COLLAPSE-NOTES.md](COLLAPSE-NOTES.md) and do its planning
pass.** The pond branch (`water-column-test`, overhangs and chunk-seam chaining) is built and
still waits on his verdict before it is merged or retired. Both branches need a push. His
`Regions16-pondlook` is restored from `backup-Regions16-pondlook-2026-09-26`. The demolished
tower is `Regions16-pondlook-demolished-2026-09-26` (open it only as a copy), and its log is
`logs/demolition-2026-09-26.log`. **Alt+G takes water back**, since NVIDIA's overlay owns
Alt+R on his machine. His clay-roof leak is in TODO under "Needs a human eye": the drip is the
eave's runoff outside, and a slot in the south glass wall that let air in is now filled.

**Pick up here (2026-09-24, late night — session close, *"pushed, see you in a fresh Code
Chat"*). Pushed through `aa487c6`; `start2/3/4.cmd` are his and untracked. Suite 1,888, r6 control
to the digit.** Built tonight, all on his word: the lift line's false alarm (`a618d81` — `lifted ==
returned + displaced by falls and fills`, chunk fills displace by design); **front-advance slice
2c** — its pass found the plan's pro-rata rule puts a ridge in front of every tapering stream, so a
fed sheet levels its tip band from the dry edge with the new cells and leaves its body alone, lists
nothing as resting, and is woken by what the write gave a move (`--fedadvance=`, `--fedband=`);
**played at `--fedband=6` and 6 ships** (*"that hits deep … i think its the one"*). **Next: his
load test** (in `MATTHEW-TODO.md` — `start3` against `start3 --fedadvance=0`, the log copied aside
between them): tonight's 6N session ran the water at ~1 Hz over 1.08 M active cells with the pass
at flood 166 / level 46 ms, so the read is that the flood's size is the cost, not the advance —
one run, no control, a lean. Then the older play queue. **Copy `godot.log` aside before any
headless probe** — tonight's four checks rotated his play log away after it had been read.

The paragraph below is the evening's state, kept for the reasoning.

**Pick up here (2026-09-24, evening — session close, *"see you in a fresh Code Chat"*). Everything
is committed; the push is Matthew's. Suite 1,873, r6 control 2601 / 325 / 3446 at 411 to the digit.
`start2.cmd` and `start3.cmd` are his and deliberately not committed.**

**Built this session, all on his word:** d-3's number and the solve's phase split; **the packed seed
sort** (228 → 57 ms a pass, same-session control); **the flood's index on his layered lookup table**
(`FloodIndex`, 0.90× the flood on `--solve`); **the pass cap** (`--solvecap=`, 500 k; the load test
0.52 → 0.73–0.80 Hz); **F11 far mode** (the adaptive radius's ceiling to r8h/4v); **the front
advance** (`--frontadvance=`, 4 — a 10,000 L pour to 12 m in ~5 s where it took ~25) with
`--fluidhz=` beside it; and **front-advance slice 2a/2b** (fed by volume, `--fedshare=`, 1%). Played
and kept: the map at 2 km, the compass, the relief default (`0.4 / 3 / 0.25`).

**Later the same night: the front advance was played and liked (*"loving it"*), the `lifted ==
returned` line's false alarm was fixed (`a618d81` — chunk fills displace by design), and 2c's pass
is written — "Slice 2c — the planning pass" at the end of the plan; build 2c-1 and 2c-2 on his
answer.** **Next, in order: front-advance slice 2c — a stream spreads pro rata — and it wants its code-level
pass first, not straight code.** Decision 3 of "Slice 2" in [docs/FRONT-ADVANCE-PLAN.md](FRONT-ADVANCE-PLAN.md)
is the design; read "Slices 2a and 2b — built" first — the count on a copy of `Regions13` in rain
says every fed sheet is fed by 10%+ of itself, so 2c is the lever for his breach pit and 2b is not.
**The trap found while closing: `ListRested` lists every written, non-front cell of a solved sheet
as resting because a *level* sheet has no move — a pro-rata sheet is not level, so its interior
would be listed as resting and, on natural ground, handed to Tier A mid-flow.** The pass must decide
what a pro-rata sheet lists and wakes (likely: lists nothing, wakes the cells whose gradient it
changed) before any code, and prove it with `VerifyQuiescent` after the settle. Then the tests the
plan names, the battery, the `--front` fed arm, and his look.

**Four things not to rediscover.** **This machine runs in two speeds about three times apart, minute
to minute** — the day's load-test runs split ~0.55 Hz / ~1.45 Hz with no code between them; alternate
the arms (A B A B) or use the interleaved bench, never compare two runs across a phase. **`arr[Call()]`
reads the array before the call** — `Discover` grows the record arrays, so the one-liner wrote into
the old one; a fixture must cross the scratch arrays' growth boundary (256) at least once. **In an
open fixture the tick's wake cascade repairs an unwoken seam from its ends** — close the corridor and
put the water down asleep, or a missing wake reads as correct. **And `sed -i` in Git Bash writes a
CRLF file back as LF** — edit with Python that keeps the line endings, or re-check-out the file after
committing.

The paragraph below is the morning's state, kept for the reasoning.

**Pick up here (2026-09-24 — session close, *"see you in a fresh Code Chat"*). Everything is
committed and pushed through `2afa9c8`; the tree is clean but for `start2.cmd`, which is
Matthew's (a copy of `start.cmd` he made mid-session) and deliberately not committed. Suite
1,829, r6 control 2601 / 325 / 3446 at 411 to the digit. Nothing is blocked on me.**

**Built this session, all on his word, all unplayed:** *a power switch on the vacuum cell* —
an `enabled` port (0/1), off takes the row out of its channel's pool with its charge frozen, the
panel's box is the switch, the `power:` term counts `(+N off)` (WORKLOG 2026-09-23); *the quantum
tunneller* — `tools/quantum_tunneller.json`, `ToolBite.Tunnel`, a 2 m cube on `TunnelLattice`
(8 voxels, nesting inside the chunk and around the brush cell), 0.25 s, 50 J a voxel, 140 bites a
lead-acid pack, the T cycle's middle quantum tier, `--entangletunnel` at open; and **every quantum
tier one click one bite and one click one fill** (`OneClickOneBite` is `Tool.AtRange`), Q held
the stream — the chunk too, on his *"yes make the chunk one click one bite too"*.

**Written, not built: [docs/TNT-NOTES.md](TNT-NOTES.md)** — a charge is a machine row with
`fuse`/`delay` ports, a detonator fires a channel, sympathetic detonation one step a boundary;
**his design folded in: blasted voxels go nowhere, and the shake** (one support derive around the
blast under distances × 0.75, what fails it falls at the next boundary, nothing stored). Six
decisions still his; machines destroyed by a blast (5) is the biggest.

**Next, in order:** his play checks in `MATTHEW-TODO.md` (the cell's switch, the tunneller, the
excavator's click) and the older M10d queue. **d-3 is taken (later the same day, the game
closed)**: the load test ticks at 0.55–0.60 Hz, the solve is 810 ms of a 1.5 s wall, and its
phase split — sort 220 / flood 436 / level 112 ms a pass over 683 k seeds — says the cost is
the active set's width, not the sheets. The levers' pass is "M10d-3" at the end of the ladder
plan; he took it as recommended and **decisions 1 and 2 are built the same day** — the packed
seed sort (228 → 57 ms a pass, same-session control) and the flood's index on his layered lookup
table (0.90× the flood on `--solve`, the water identical). The target (solve under a third of the
tick) is met by **the pass cap, his word the same afternoon** (`--solvecap=`, 500 k shipped, 0 the
control): 0.52 → 0.73–0.80 Hz on the load test, the solve 30–32% of the tick, a full sweep of its
heights ~6 s — below the cap a pass is exactly the old pass. **The machine
runs in two states ~3× apart minute to minute** — never compare two load-test runs without a
same-session control. **Two things not to
rediscover**: the profile's `/s` for a quantum tier reads well under its cooldown (the miner, 10/s
nominal, reads 4.2 under `--holdq`) — an instrument question, not a tier's, in TODO; and a Python
edit script that writes `utf-8-sig` adds a BOM to a file that had none (ContentLoader.cs, caught
and stripped) — read the first three bytes and write back what was there.

**Pick up here (2026-09-23, late — session close, *"see you in a fresh Code Chat"*). Everything
is committed through `3749b96`, the tree is clean, and the push is Matthew's — seven commits
(`c417d89..3749b96`). Suite 1,809, r6 control 2601 / 325 / 3446 at 411 to the digit. Nothing is
blocked on me; everything outstanding is a play.**

**Built this session, all on his word:** the pipeline harness's terrain pinned with the shipped
defaults asserted separately; **relief as a floor, a ceiling and a rarity** (`--relief`,
`--reliefbase`, `--reliefrare`, header **v7**) after a multiplier centred on 1 shipped and was
measured wrong the next day; **the relief field offset per seed**, because gradient noise is zero
at its lattice points and world origin is one, so four worlds read the identical amplitude at
spawn to the digit; **the HUD's position and heading line** with `Core/Navigation/Compass`; and
**the map** — planned in [docs/MAP-NOTES.md](MAP-NOTES.md) and built in four slices, a to d,
all four documented there with their findings.

**What is Matthew's, all of it play:** whether the shipped relief shape (`0.4 / 3 / 0.25`) reads
right now that the map lets him see it; whether 2 km is the right default map width (`--mapscale=4`
is the other end); and the older M10d recipes. `MATTHEW-TODO.md` has the three dials as a table
with which way each scale runs.

**Four things not to rediscover.** **An exception inside a Godot callback is caught and logged
rather than crashing**, so it looks like a feature that works once — the map built exactly one
picture for a whole slice while a stack trace sat in the log; grep a run for the exception type
before believing any theory about a UI that updates strangely. **A display's scale belongs to what
is on screen**, not to what the world could contain — and the fit needs a floor, or flat ground is
amplified into noise. **Coarser does not calm a sampling-limited picture, it aliases harder**, so
the map's zoom should stop around 16 m a pixel rather than pay N² for supersampling. And **the
bearing-independence oracle earned itself twice**: it caught a one-pixel grid shift the slice after
it was written, and it is the test to reach for on anything that draws the world from a viewpoint.

The paragraph below is the morning's state, kept for the reasoning.

**Pick up here (2026-09-23, session close — *"see you in a fresh Code Chat"*). Everything is
committed and pushed through `ac91996`, the tree is clean, and his `default` is gone: the next
launch is a fresh world. `start.cmd` builds the Game before it launches now, so the binary is the
tree by construction. Suite 1,735, r6 control 2601 / 325 / 3446 at 411 to the digit.**

**Built this session, all on his word:** `start.cmd`'s guarded `dotnet build`; **M10d planned**
(the pass at the end of [docs/M10-LADDER-PLAN.md](M10-LADDER-PLAN.md) — seven facts, nine
decisions, split d-1 / d-2 / d-3); **M10d-1 built** (`held` carries `Q L in R panel(s) below the
seed surface`, the *dry* oracle; three pillar tests — a brush-cell column floor-to-roof clears the
2 × 2 plug, one voxel short reads the plug's 20, **dirt shores as stone** because the refund is a
world constant); **the three-step undo planned and built** ([docs/UNDO-PLAN.md](UNDO-PLAN.md),
Ctrl+Z, `--undo=<n>`), **played and confirmed** — *"Ctrl z feels good, disallow if something falls
yes"*; and **the pool world retired** as `worlds/retired-pool-2026-09-22/`.

**Next, in order:** (1) his two sentences from the rain play, still owed — the clay pond filling
(`onto the player's material` climbing while `handed` does not) and no water born under his roof
(`skipped of which N under a roof`); (2) **M10d-2, the play**, which is his — the seven recipes at
the end of the M10d pass plus the undo's three, one sentence back on each; (3) **d-3**, the solve's
number on a copy of `retired-spread-2026-09-22` with `--profile --fluidnear=2 --fluidjob=1
--speed=0 --measure=60`, then its own pass over the three levers. **M10e (the conductor index) is
after the play**, per the ladder's decision 23.

**Four things not to rediscover.** **A hook on the one write path sees every writer** — the undo's
own inverse (the press after an undo redid the edit), the collapse sweep's rubble, and
`MachineSet.Restore`'s row-per-stored-machine at open; all three are suppressed and the built
section says how. **The a-3 pump oracle reads `0 below the seed surface`** and that is correct: a
three-wide `--breach` is eight voxels deep and its water sits in the mouth chunk, which straddles
the rim — the keystone is the term's oracle, not a-3. **The keystone's digits move run to run**
(18,698–18,728 at risk, a roof sometimes in two pieces) while its shape does not. And **Godot logs
rotate at five**, so a handful of headless probes erases the play log you meant to read — copy it
aside first.

The paragraph below is the morning's state, kept for the reasoning.**

**Earlier (2026-09-22, afternoon, later). M10d-1 is built and d-2 — the play, his seven
recipes at the end of "M10d — the planning pass" in [docs/M10-LADDER-PLAN.md](M10-LADDER-PLAN.md)
— is next and is Matthew's; d-3 (the solve's number on a copy of the load test) waits for a
session with the game closed. Read "M10d-1 — built" first: the `below the seed surface` term on
`held` (`UndergroundWater`, measured at `Publish`) reads the keystone's every litre as underground
and the a-3 shaft's none, because a three-wide breach is eight voxels deep and its water sits in
the mouth chunk, which straddles the rim — the a-3 oracle was the pass's wrong guess; the three
pillar tests are green and the gap-at-the-top one reads the plug's 20 exactly; and the keystone's
digits move run to run (18,698–18,728 at risk, a roof in two pieces) while its shape does not.
Suite 1,719, r6 to the digit. The paragraph below is the pass's state, kept for the reasoning.**

**Earlier (2026-09-22, afternoon). M10d is planned — "M10d — the planning
pass" at the end of [docs/M10-LADDER-PLAN.md](M10-LADDER-PLAN.md), written while Matthew
played: seven facts, nine decisions, all recommended, split d-1 / d-2 / d-3. Nothing is built for
it but two instruments and one test. Build d-1 first, once he answers — the `below the seed
surface` term on `held` (the "dry" oracle; a panel whose chunk top is under its footprint's least
seed height, off the loaded sky columns), the three pillar tests on the plug fixture (a column
floor to roof reads `at risk 0`; one short reads 20; clay reads 0 — `Rest` is material-independent
by the uniqueness proof, a stated cost), the M9d recipe's `--nogate` — then d-2 is his seven
recipes, and d-3 the solve's number on a copy of the load test after his session, never beside a
play. Three things not to rediscover: **his world is the storm arm already** (40 pumps, 44 panels,
0 cells, 94% of the generation clamped; the pool draws in origin order, so the first-placed wet
pumps run under panels-only power — watch for it, the lever is the outlets' ring on the pumps);
**"dry" needs the term** (`held` is every litre, `no water` is the pumps' view); and **a pillar
shores in any material** because the refund is a world constant. `start.cmd` builds the Game
before it launches since this morning (`e759421`); the r6 control read to the digit after it. His
two play sentences from the morning are still owed — the clay pond in rain, no water born under
his roof. The paragraph below is the session close's state, kept for the reasoning.**

**Earlier (2026-09-22, session close — *"see you in a fresh Code Chat"*). Everything is
committed and pushed through `3b70c7a` except that one commit's Game build: the roof fix (a random
droplet no longer spawns under a roof) was committed while he played, so his binary is
`dfc0518`'s. First thing next session, with the game closed: `dotnet build Overburden.Game`, the
r6 control (2601 / 325 / 3446 at 411), and then his two play sentences — the clay pond filling in
rain (`onto the player's material` climbing while `handed` does not), and no more water born under
his roof (`skipped of which N under a roof` non-zero where it used to leak). Built today, all on
his word: rain on the player's material (eight decisions as recommended — read "Rain lands on the
player's material — built" in [docs/M8-PLAN.md](M8-PLAN.md), the forfeit the one rule the
build added); the surface's previous-readers rule reading what each build read (`own 2, previous
26` → a handful on his settled world); the enclosure's *why*; the spread world retired as the load
test. Suite 1,713. M10d's play is still the next milestone thing; the solve lever under M10d has
its world now. Read the 2026-09-22 worklog entry first, then the paragraph below.** His clay pond was never leaking: its floor is
placed, so its water is never on the seed plane and holds (`enclosed 0 kept, 306 flood(s) / 0
closed` with 12,800 L held) — rain was not landing in it, because the watershed cannot see a
placed floor and the 2% drops are scattered over the whole box. The rule: a column whose top is
above its seed gets its rain as Tier B cells on that top, owed by the basin under it. The
`enclosed` bracket says why a flood opened since the same day (built, committed, the Game
rebuilt when his session ended). The spread world is retired as the load test
(`worlds/retired-spread-2026-09-22/`). The paragraph below is the evening's state.**

**Earlier (2026-09-21, evening). The Tier B surface is per panel and its walk reads only a
full layer's perimeter — built, tested (suite 1,703), benched, and measured on a copy of his
`default` once he quit: the walk was half the tick on that world (650 of 1,200 ms) and is ~265
now; the memory climb of his 85-minute play was garbage, not a leak (`dotnet-gcdump`: 159 MB live
in a 971 MB heap), and the `M2c:` line prints `live` now. `H` holds the water and `Alt+H` reads
the heap, on his offer. The Game is built and the r6 control is to the digit. Owed from his next
play, not a probe: the settled state's `last rebuild N panel(s)` and `install mean` on the `M8b:`
line and the `live` term's slope (TODO under M10d), and his one sentence each on the courtyard's
fourth wall and the clay-lined wall. Read the 2026-09-21 (afternoon) worklog entry — the settle
rule took three tries and the two wrong ones are in `FluidSurfaceMesher`'s remarks. The rest of
the tick is the sheet solve (6.5 billion cells visited over that play, the lake ceiling waking 12.1
million by rise) — the M10d lever with its number; M10d's play is still the next milestone thing.
The paragraph below is the small hours' state, kept for the reasoning.**

**Earlier (2026-09-21, small hours). Session closed with the three decisions built and the
memory crash fixed; nothing waits on his word. Next, in order: (1) read the next play's log —
`grep -a` it, count `ERROR` first, then the `memory` term on the `M2c:` line (flat working set is
the pass), then `enclosed … region(s) / … evicted` on the `M8b:` line, then the three play checks in
TODO under M8 (the courtyard's fourth wall, the clay-lined wall's `seep` faces, the excavator's
fill stopping mid-chunk) — one sentence back on each; (2) M10d's play, still the next milestone
thing, its recipe under M10d in TODO. Two levers recorded and not built, both from the crash log:
the Tier B surface install is 40 MiB and 100+ ms a rebuild four times a second on a 50 ML world
(TODO under M8b-4b, older, now with a number), and a `boundary` waited on 330 of 1,005 ticks
there. `../Claude-Notebook.md` (the parent folder's) has this environment's gotchas — read its
Code-tab section first. The paragraph below is the 20th's state, kept for the reasoning.**

**Earlier (2026-09-20). The fresh world's first night and day — six things built on his
word, two decisions waiting on it, and M10d's play still the next thing.** Built 2026-09-19/20,
each with its oracle in the worklog: *the machines are near* (`MachineTick.NearColumns` →
`FluidSimulation.SetNearColumns`; a far outlet's mound was visited once in ~260 ticks and its
pump stalled `no room` — *"the mounds are draining now"*); **Ctrl+R pours 1,000,000 L**; *rain
drops Tier B cells* (`--rainshare=2`, each cell owed to the sky and paid from its next rain,
`WaterSystem.DivertRain` — *"I loved seeing the tier b water blocks falling"*); **`Alt+P`'s pack
half goes through `MachineTick.FillPack`** (a `Give` on the pack alone broke the pool identity by
3,593 kJ for a session, 1,403 error lines); *the sheet solve is seeded from what changed* (a
carried cell offered once and again only when woken — solve 92.8 → 5.3 ms a pass on a copy of
the retired world, more sheets levelled); and **a fluid row naming an empty awake cell no longer
holes a chunk** (the unview and eviction writers list held cells; the reader drops and counts,
`M6:` lines). The flooded world is `worlds/retired-flooded-2026-09-19/`, the solve lever's
measurement world; his `default` is fresh — 64 pumps, 127 outlets and 34 ML by midnight. **His
word, given — *"1 and 3 as recommended"* — and all three built the same night**: (1) *the
courtyard* — a walled pool on grass is the lake outside, M8b-3a's pinned trigger fired; **arm B
built as `Enclosure` in Core** (*the player's material encloses, nature's does not*: a flood at
the level, the only obstacle a placed voxel above the seed or natural rock carrying one — **a
wall claims its footing, the rule the pass did not have**, because the headless courtyard on the
spawn's slope leaked at every level below its uphill wall's ground without it), one predicate for
the hand-off (`EnclosedKept`), `LakeCeiling` (an enclosed column has no band; `MarkDirty` rebuilds
a band whose top an edit may have moved) and `WaterSurface` (an enclosed sample draws no plane);
`--enclosure=0` the control, `--courtyard=<m>` the provocation, the oracle `handed 0 L` against
`handed 20,000 L`; **its first play ran out of memory — the cache was unbounded (5,884 regions
as 16k entries each, over 4 GB); a region is a 2 KB bitset and `MaxRegions` is 4,096 now,
measured flat windowed on the flooded world, and the `M2c:` line prints the process's memory**; read the 2026-09-20 amendment under M8b-3a's finding 1 in
[docs/M8-PLAN.md](M8-PLAN.md) and the play check in TODO under M8; (2) *clay* — **taken and built the same evening, *"2 please"*: `clay.json` at
permeability 0, dirt's hardness, a support curve a metre steeper than dirt's (an estimate), a
twelfth `LayerColour`, 64 cells a `P` press; the worldgen band is still WORLDGEN's; the play
check is the `seep` line's faces falling by a lined wall's count** (TODO beside the courtyard); (3) *the excavator's fill taking the bite's rule* — **built the same evening**:
`PlaceResult.Unpowered`, a voxel budget on every brush shape asked last in `Put` (after the
stock — the fixture that separates the orders has both run out at one voxel), the controller's
`VoxelBudget` read at the click and again at the boundary, the `fillgate` profile line; the fill
arm's oracle is `paid 3,200 J` for 64 placed at `--charge=3200` and 32 of the first cell at
`--charge=1600` where it refused the first whole; read the 2026-09-20 amendment at the end of
"M10c-3 — built". **Recorded, unbuilt**: the solve's pass is O(sheet) on a 34 ML world with 191 machines
(1.6 s mean, 4,282 of 4,295 boundaries waited — TODO under M10d, three levers); what drained a
paused cell is unproved (the `M6:` count on a post-fix world is the tell). Three things not to
rediscover: **every recorded oracle runs `--fluidnear=0`**, where the carry is empty and the
seeded solve is byte-for-byte the old path — measure the solve on a *copy of a played world*
with `--fluidnear=2 --fluidjob=1`; **`grep -a` the log**, and classify its `ERROR` lines before
counting them; and **a debug door into a conserved quantity is a term** or the identity lies
from then on. Suite 1,690 with clay, the fill's budget and the enclosure; the r6 control to the digit. The paragraph below is the 19th's
state, kept for the reasoning.

**Pick up here (2026-09-19). Three things landed on the played world's day, all Matthew's
words, all built and unplayed, and M10d's play is still the next thing.** *The modifier map
read back*: Shift and Ctrl are movement only; Alt is precision on both buttons, crawl in fly,
and the debug keys' inverse (Alt+P, Alt+R); Ctrl+Alt in fly cancels to base speed (recorded,
left). *Q held is automine*: `MiningController.Running`, a plain hold from the press, a swing at
the crosshair every cooldown on whatever tier is in hand — and Alt's held repeat on the cube arm
went with it, so a click is one click whatever it holds. *The quantum tiers are content on T*:
`tools/quantum_miner.json` (the 1 m cube, 0.10 s) and `tools/quantum_excavator.json` (the 8 m
chunk, 0.25 s), both 5.0 and 50 J a voxel, through a `"bite": sphere | cube | chunk` field on the
tool whose reach follows it (no `reachMetres`, still); `ToolTiers.Order` is (threshold, bite,
id) and `ToolTiers.Default` the strongest *free* tool — the rule M10c-3's decision 5 dodged by
keeping the costing tool off the cycle; the mode, its Q toggle, `EntangledBite`, `EntangledTool`
and `--entanglesphere` are gone, `--entangle` / `--entanglechunk` select a tier at open, and
both verbs follow the tier (`ApplyTier` in `Main`). The excavator is a two-chunks-a-pack tool
with lead-acid — the ladder's *"only once you have the latest tier battery"* as a number. Read
"M10c-3 — built"'s last amendment in [docs/M10-LADDER-PLAN.md](M10-LADDER-PLAN.md) and the
play checks under M10d in TODO. **And a fourth thing the same evening, from the play**: *the fill
into water displaces it* — M9d's `Wet` refusal still lands the dry part at the click, and the
boundary runs the same shape again with `FillWater` in hand (lift, write, return bottom-up over
the shape's box grown by a cell, the rest `displaced`), the chunk fill included; read "M9d —
built"'s last amendment in [docs/M9-PLAN.md](M9-PLAN.md). **And a fifth, from the same play an hour later**: *the machines are
near* — a far outlet's mound was visited once in ~260 ticks under the far quota and its pump
stalled `no room` for the session (*"the water processing cannot keep up with the 'dump' rate"*),
so the chunk columns under every pump's box and every outlet's mouth join the viewer's rings in
`IsNear`; read the 2026-09-19 amendment at the end of "Slice 1 — built" in
[docs/WATER-THREAD-PLAN.md](WATER-THREAD-PLAN.md), which also records the solve flooding
294k cells a boundary on that world — the tick at 1.2 Hz, a lever in TODO. Three things not to rediscover: **a fresh world must not start
on a costing tool** (the first swing would cost from an empty pack and read as a broken build —
`ToolTiers.Default` is the rule and `ShippedContentTests` asserts it); **`PlaceController` has
its own `TakesCube`**, because the fill follows the tier and not the hold; and **the bite's
oracle reads the same to the digit** (`removed 6, unpowered 57`, `6,840 over 120`) under
`--entangle`, which now means the miner in hand. Suite 1,627 → 1,638; r6 control to the digit.
The paragraph below is c-3's state, kept for the reasoning.

**Earlier (2026-09-17). M10c is built — all three sessions — and unplayed; M10d, the
play, is next, and the ladder's decision 23 puts M10e (the conductor index, the DC tier) after
it. Read "M10c-3 — built" in [docs/M10-LADDER-PLAN.md](M10-LADDER-PLAN.md) first, then the
seven-question play recipe under M10c in TODO — the equip, the swap (its forfeit became a
stow on the first play, 2026-09-18), the bite that
stops mid-cube and the fill refused whole are the four things this build could not judge, and
question 6 (whether a swapped-out pack should keep its charge as an item) is the one that would
move a ladder decision.** What c-3 built: `PackSave` on header v4 and `EquippedPack` through
`CurrentHeader`; `MachineTick.EquipPack` with `forfeited` in `PowerExact` (`stowed` since 2026-09-18, the
charge kept beside the count in `CarriedPacks` at header v5); `ToolDefinition.Power`
and `tools/entangled_miner.json`, loaded by ID and off the T cycle; `Carve`'s `voxelBudget` on
every shape with `CarveResult.Unpowered`; `MiningController.SwungTool`/`Pack`/`PowerOff` paying
`Removed × cost`; `PlaceController`'s all-or-nothing fill, `EquipRequest` and `PackPanelRequest`;
`MachinePanel.OpenPack`; the HUD's pack line; `--pack`, `--charge=`, `--openpanel=pack`,
`--nogate`'s miner half. **Three things M10d must not rediscover**: `--stock=` seeds an item at
one and `--place` selects a placeable material first — every `--place` profile from M10b-1 to
c-3 selected the computer and placed nothing; the bite-that-stops oracle reads `unpowered 57`
and not the pass's 58 because the spawn's cube holds an air voxel and air costs nothing; and the
tick's `Pack` is set through `EquipPack` only, because a swap that replaced the object silently
dropped what the pool had given the first pack — the identity has a `stowed` term for it.
Suite 1,617; the r6 control to the digit. The paragraph below is c-2's state, kept for the
reasoning.

**Earlier (2026-09-17, small hours). M10c-2 is built — the wire, the three JSON files and
the oracles — and M10c-3, the pack and the gate, is next, alone: header v4 (a presence byte, the
pack's material through the world's table, its charge in millijoules, its channel; v3 opens with
none) and `EquippedPack`'s persistence through `CurrentHeader`; the equip verb (a right-click edge
with a pack in hand, routed in `PlaceController` beside the computer's; a swap forfeits the
outgoing charge, printed); the pack's panel on the computer's click on nothing (`MachinePanel` over
a port view rather than a row — one `channel` box, the charge as its status); `tools/entangled_miner.json`
(threshold 5.0, cooldown 0.10 with `--cubecooldown=` as its sweep, `power { via: channel,
joulesPerVoxel: 100 }`) and `ToolDefinition.Power` with the loader's rules; `Carve`'s voxel
budget on every shape and `CarveResult.Unpowered` in `IsAccounted`; `MiningController` paying
`Removed × J` from the pack (the Alt chip too) and `PlaceController`'s fill at range refused whole
under `64 × J`; the HUD's `pack 2.1 / 3.6 MJ` and `Q takes nothing — no pack`; `--pack[=<id>]`,
`--charge=<J>`; `--nogate`'s miner half; `Alt+P` filling the pack too; the bite-that-stops oracle
(`--pack --charge=640 --mine --entangle --profile --speed=0` → `removed 6`, `unpowered 58`, the pack
at 40 J); the tick's `Pack` set from the header; and **every recorded `--mine --entangle` recipe
gaining `--nogate` in the same commit**. Read "M10c-2 — built" and the pass's decisions 4–6 in
[docs/M10-LADDER-PLAN.md](M10-LADDER-PLAN.md) first.** Three things c-3 must not rediscover:
`MachineSet.SetCharge` is the debug door for *placed* cells and the pack is not a row — the pack's
`loaded` is `EquippedPack.InitialMillijoules` and its tool draw `TakenMillijoules`, both already in
`PowerExact`; the pack recharges *last* and is never in `_available`, so a full pack never runs a
pump; and `PlaceController.SelectedIsItem` routes every non-placeable material to the item branch,
where the computer asks the machine under the crosshair — the pack's edge goes beside it, keyed on
the material's `Carried` storage block. Suite 1,592; the r6 control to the digit. The paragraph
below is c-1's state, kept for the reasoning.

**Earlier (2026-09-16, night). M10c-1 is built — the pool in Core — and M10c-2, the wire
and the oracles, is next: `Main`'s `ISolarSky` over `ChunkStreamer.ColumnAt` (the count of the
body's sixteen columns whose `OpenAboveY` is at or below its top plus one), the overcast byte at
the boundary as `min(255, ⌊(_forcedWeather ?? OvercastAt) × 256⌋)` into `MachineTick.OvercastByte`,
`PowerOff` off with `--nogate` turning it on (printed), `P` seeding a panel, a cell and a pack once,
`Alt+P` filling every loaded cell and the pack (printed), `--pump` placing a *charged* vacuum cell
beside the outlet (`--cell=0` omits it → `stalled 60 unpowered`), `--solar=<n>`, the `M10:` line's
`power:` term and the `machines` block, the panel's three lines (`charged … of …`, `generating … a
boundary (N/16 open, overcast B/256)`, `stalled: no power on channel N`), the bench's power row,
`solar_panel.json` / `vacuum_cell.json` / `lead_acid_pack.json` with `ShippedContentTests`, and
`--weather=1` reading 976 mJ a panel a boundary. Read "M10c-1 — built" in
[docs/M10-LADDER-PLAN.md](M10-LADDER-PLAN.md) first.** Three things c-2 must not rediscover:
the game runs `MachineTick.PowerOff = true` and prints it — every recipe reads as recorded because
of that line, and c-2 removes it in the same commit that places the charged cell; a pump takes all
it can pay for, so a panel on its channel buys a trickle and the relay never stalls `unpowered`
while the sun is up (the fixture needed a roofed sky) — `--cell=0` under `--weather=1` is the arm
that stalls; and `ChargeOf` goes through the set's table, so a headless oracle that reads charge
reads the set's, never the bytes. Suite 1,591; the r6 control to the digit. The paragraph below is
the pass's state, kept for the reasoning.

**Earlier (2026-09-16, evening). M10c is planned and not built — "M10c — the planning
pass" after b-2's built section in [docs/M10-LADDER-PLAN.md](M10-LADDER-PLAN.md): nine facts,
eleven decisions, three sessions c-1 the pool in Core / c-2 the wire and the oracles / c-3 the pack
and the gate; two things are Matthew's (the pack that ships — `lead_acid_pack` 3.6 MJ / 1 kW
recommended over the vacuum pack because the gate has to be felt in one play — and the entangled
tool's threshold, 5.0 recommended). Build c-1 once they are answered.** The pass's spine: the pool
is the tick's (Σ placed storage charge + the boundary's generation per channel; pumps draw in
origin order at `joulesPerLitre`, which is a millijoule a millilitre; the pack draws last and never
supplies; written back in origin order up to capacity; `clamped` reported); solar is a `converter`
whose output is one integer product (`ratedWatts × 500 × open/16 × (256 − byte)/256` — 976 mJ
under `--weather=1`, not zero) with the openness byte kept on the row for when its column is
unloaded; the pack rides header v4 and a right-click edge with a pack in hand equips it; the
entangled miner becomes `tools/entangled_miner.json` with the `power` block, reversing M3d's *never
what the bite is hard enough to take*; `Carve` shapes take a voxel budget with an `Unpowered`
bucket; `--nogate` is one flag for pumps and the miner and every recorded `--mine --entangle` recipe
gains it. **Three things c-1 must not rediscover**: the relay is 200 panels, so `--pump` places a
*charged* cell beside the outlet and `Alt+P` is the play's charge, or a-3's oracle stops reading;
the kind blob's version byte is where the charge and the openness go, with the restore clamping
the charge and no schema bump; and the overcast byte is taken once at the boundary from
`_forcedWeather ?? OvercastAt`, never the float in Core. **The pass also found and fixed a live
defect** (`2b26540`): the pick never passed the set to `Carve`, so every play since a-2 destroyed
a pump it mined without credit — `MiningController.Bodies`; a bite and a fill into one cell in one
frame still reads as a fall (TODO). M10b's seven-question play recipe is still Matthew's. The
paragraph below is b-2's state, kept for the reasoning.

**Earlier (2026-09-16, morning, later). M10b is built — both sessions — and unplayed;
M10c, power on the channel class, is next and wants its own planning pass against the ladder's
decisions for it (the pool per channel as Σ storage rows, `clamped`, the converter and storage
kinds, solar quantised off `SkyColumn` and `OvercastAt`, the equipped pack at header v4, the tool's
`power` block, `--nogate`) and against `MachineTick`, `MachineSet`, `WorldHeader` and the entangled
miner's rate. Read "M10b-2 — built" in [docs/M10-LADDER-PLAN.md](M10-LADDER-PLAN.md) first,
then the seven-question play recipe under M10b in TODO — the panel opened, edited and closed by
hand is the one thing this build could not verify, and question 3 (one Esc or two with a field
active) is a one-line change either way.** What b-2 built: `MachinePanel` over `PanelRequest`, one
`SpinBox` per declared port through `SetPort` at once, the stall line, `FlyCam.ReleaseMouse()` /
`CaptureMouse()`, Esc and the click outside and the row's departure as the three closes,
`--openpanel[=outlet]`, `--panelset=`. **Three things M10c must not rediscover**: the click that
re-captures the mouse is swallowed by the verbs until it is released (`FlyCam.ClickLive`, not
`MouseCaptured`, is what mining and placement read now — a new verb reads it too); flight and
walking are both gated on capture, so a panel or any future UI that releases the mouse stops the
body where it stands; and the panel is built with the HUD and skipped with it under `--profile`,
so a profile's `computer N open(s)` counts requests that opened nothing. Suite 1,569; r6 control to
the digit. The paragraph below is b-1's state, kept for the reasoning.

**Earlier (2026-09-16, morning). M10b-1 is built — Core and the headless seam — and M10b-2,
the panel, is next: `MachinePanel` in `Overburden.Game/Ui/` over the row `PlaceController.PanelRequest`
names (set for one frame on a right-click edge with the computer selected on a machine body; `Main`
prints it today), one `SpinBox` per port the material declares through `MachineSet.SetPort` (clamped,
counted, `Version` moved on a changed value — the tick rebuilds its rings at the next boundary), a
status line from `MachineRow.LastStall` / `LastMovedMillilitres`, `FlyCam.ReleaseMouse()` /
`CaptureMouse()`, `Main._UnhandledInput` ignoring its keys while the panel is open and closing it
when capture returns (a click outside re-captures through the camera's own rule), `--openpanel` for
the screenshot. Read "M10b-1 — built" in [docs/M10-LADDER-PLAN.md](M10-LADDER-PLAN.md) first.**
Three things b-2 must not rediscover: both input handlers are *unhandled* input, so a `Control`
with `MouseFilter.Stop` owns its clicks and a focused `LineEdit` its keys; the edge is the
controller's (it computes the click from the hold, so `--place`'s held button is one click and the
panel must not re-open every frame); and a port re-sent unchanged moves nothing, so the panel may
write on every change without a rebuild a boundary. Suite 1,569; 14 recipes, 14 caught; r6 control
to the digit. The paragraph below is the pass's state, kept for the reasoning.

**Earlier (2026-09-16, small hours, later). M10b is planned and not built — "M10b — the
planning pass" after a-3's built section in [docs/M10-LADDER-PLAN.md](M10-LADDER-PLAN.md):
six facts, nine decisions, two sessions; decision 1's ID (`overburden:computer`, his word, against
the notes' `machine_programmer`) is Matthew's and the rest are recommended. Build b-1 first — Core
and the headless seam: `placeable: false` through the loader, the table and `Place.Validate`
(refused as air is, a caller error); `computer.json` with its colour *and jitter*; `P` seeds it;
`MachineSet.SetPort` clamped and counted with a `Version` bump; the weighted ring with the smooth
interleave and the per-boundary skip; `MachineRow.LastStall`; the controller routing a right-click
edge with the computer selected to an open request on the hit voxel's cell; `--port=<pump|outlet>:<name>=<value>`;
`port writes` on the `M10:` line — then b-2, the panel.** Three things the pass found that the
build must not rediscover: a right-click with anything selected places it, so the first item that
is not a block needs `placeable` before M10g's parts do; `SetPort` must move `Version` or the tick
keeps the old ring; and both input handlers are *unhandled* input, so a `Control` that stops its
events owns its clicks and a click outside re-captures the mouse — that re-capture is the panel's
close. No stock tab at M10b (nothing declares stock); the quit oracle is a port write. The
paragraph below is a-3's state, kept for the reasoning.

**Earlier (2026-09-16, small hours). M10a is built — all three sessions — and M10b, the
computer, is next and wants its own planning pass against the ladder's decisions for it (an item;
right-click on a machine opens the ports and the stock tab; `SetPort` clamped in Core; the weights
the round-robin ignores) and against `MachineTick`, `MachineRow.SetPortUnchecked` and
`PlaceController`. Read "M10a-3 — built" in [docs/M10-LADDER-PLAN.md](M10-LADDER-PLAN.md)
first.** What a-3 built: `FluidSimulation.Pump`/`Emit` and `WaterSystem.Pump`/`Emit` with their
counters in both identities; `FluidGrid.HeldIn`; `WaterSystem.TryBasinAtSample` (false off the
window, true-with-`Outlet` for a sample that drains off the edge); `MachineRow.OwedMillilitres`;
`MachineTick` — every pump every boundary, the cheap read first, the outlets on its channel from
the channel's cursor, room read then exactly that moved, whole litres across a tier, the
unloaded-outlet rule on the *mouth*; the `machines` bracket in `Main.UpdateFluid` after the pour and
before the table check; `--pump=` and `--pumpoutlet=`; `--pump` on the bench. **Three things M10b
must not rediscover**: the tick reads the *live* simulation terrain (the field wrapped in the lake
ceiling), never a captured view, because `IsLoaded` and `InLake` are its two questions and a view
refuses what it does not cover; a body placed on a surface off the 1 m lattice sits up to three
voxels into it, so any rule about "on the ground" is about the mouth; and the rate override is a
seam, not a port — a row's rate is its definition's until a port says otherwise, which is M10b's
question to ask. Suite 1,551; 19 recipes, 19 caught; r6 control to the digit. **M10a is unplayed
end to end** — the relay pumping the mine is M10d's play. The paragraph below is a-2's state, kept
for the reasoning.

**Earlier (2026-09-15, the end of the night). M10a-2 is built — the entity — and M10a-3, the
pump, is next, against decision 12's list in the pass at the end of
[docs/M10-LADDER-PLAN.md](M10-LADDER-PLAN.md); read "M10a-2 — built" first.** What exists: the
`machine` block through the loader with every refusal naming the file and a `via` this build does
not serve naming the part; `relay_pump` and `relay_outlet` at hardness 3.0, `unit: cells`; `Place.Machine`
(all 64 voxels and the row in one call, or nothing, whole in one bucket) and `Place.MachineDisplacing`
(the boundary's path into water — lift, write, return bottom-up over the box, displace the rest);
`IMachineBodies` on every `Carve` entry point; `MachineSet.ChunkArrived` / `OnVoxelChanged` / `Taken`
that only queue and mark, and `Drain`, once a frame from `Main`, that reconciles and dismantles
(`Lost == Carved` credits the remainder, `Lost > Carved` destroys); `P` seeds the pair; the HUD says
`MACHINE` and `× N`; `--abort=<s>` ends the process with no save, `--standfor=<s>` stands before the
flythrough moves, `--material=<id>` picks what `--place` places. **Three things a-3 must not
rediscover**: `Main` wires `onVoxelsReady` now (it never did before) and nothing in any hook writes;
a machine material places as one body whatever the brush, `--entanglechunk` or Alt; and the jitter
table in `VoxelMaterial.Create` must match `LayerColour` or the game throws before its first frame —
it is asserted. The headless orphan check is in the built section, verbatim. Suite 1,519; r6 control
to the digit. The paragraph below is a-1's state, kept for the reasoning.

**Pick up here (2026-09-15, last of all). M10a-1 is built — the store slice, alone — and M10a-2,
the entity, is next, against decision 12's list in the pass at the end of
[docs/M10-LADDER-PLAN.md](M10-LADDER-PLAN.md); read its "M10a-1 — built" section first.**
What exists: `IWorldStore.Write(WorldBatch)` is the one write, the session (machines, basins,
header) riding the chunk transaction with null meaning *leave it* and empty meaning *clear it*;
`ChunkPipeline.SaveAll(session)` and `WriteSession` (the quit's fallback when a tick still owns the
grid); schema 4's `machines` table; `MachineRowBlob` v1; `PortDefinition`, `MachineRow` and
`MachineSet` in `Overburden.Core/Machines/` as rows only — `Snapshot()` in origin order,
`Restore()` dropping, defaulting and clamping with counts, an unknown kind kept verbatim; `Main`'s
`Session()`, `SetUpMachines` and the `M10:` lines. **Three things a-2 must not rediscover**: the
autosave prints every time now because the session is written every time (a quiet thirty seconds
reads `0 chunk(s)`, not silence); the quit writes the session *after* the tick's cancel, with the
chunks, and alone only when the cancel timed out; and `SessionSnapshot.Basins` is null under
`--nowater` so an autosave there leaves the stored basins alone. The first test —
`AWriteThatFailsOnItsMachinesLeavesTheHeaderBasinsAndChunksUnchanged` — runs on both stores.
Suite 1,469; r6 control to the digit. The paragraph below is the pass's state, kept for the reasoning.

**Pick up here (2026-09-15, last). M10a is planned and not built — the pass is the last section of
[docs/M10-LADDER-PLAN.md](M10-LADDER-PLAN.md); Matthew took all twenty-three of the ladder's
decisions as recommended (*"As recommended"*). Build M10a-1, the store slice, first and alone: a
`WorldBatch` record carrying an optional session (machines, basins, header) in one transaction,
`SaveAll(SessionSnapshot?)`, the `machines` table at schema 4, `MachineRowBlob` v1, `MachineSet`
as rows only — no placement, no tick — with the first test a store that throws inside the batch
after the header and leaves header, basins and chunks byte-identical on both stores.** Six things
the code said that neither plan had: an autosave is *three* store calls today (header, basins,
batch), so the basins join the transaction; `Dispose` saves too, so the session is handed to
`SaveAll` and the backstop carries none; the pipeline's hooks fire inside `Complete`, so hooks
queue and a per-frame drain writes — never `SetVoxel` from a hook; a fall and a swing write the
same thing into a body, so the ladder's rubble clause cannot tell them apart and `Carve.Take`
telling the set what it took can (`Lost == Carved` dismantles with credit, `Lost > Carved` destroys
without); the water table is a level, so a pump in a dry pit beside a lake would drink it through
the rock — `InLake` is the intake question, not `TableAt`; and `BasinAtSample` folds *off the
window* into `Outlet`, so `unanalysed` needs its own question. Whole litres cross a tier,
millilitres move within Tier B, `Pump`/`Emit` twins on both tiers carry their own counters into
both identities, every row ticks every boundary with the cheap read (≤ 8 `IsLoaded`, ≤ 8
`HeldIn`, then the 26 cells) and the sleep set is a lever. The relay bodies ship at hardness 3.0
(the ladder's 5.0 would make a body nobody can dismantle). **`--pump`'s delivered rate on the
bench is still the first number.** The paragraph below is the ladder's state, kept for the reasoning.**

**Pick up here (2026-09-15, late night). The ladder is planned and not built —
[docs/M10-LADDER-PLAN.md](M10-LADDER-PLAN.md), twenty-three decisions, twelve of them
Matthew's; M10a's own pass follows the answers and builds the store slice first and alone.** The
brief came in an evening: *"the first tech we make will be the last tech in the game"*, a crafting
system from parts made earlier, and five tiers — hand → steam (a boiler and mechanical output) →
DC (wires, short runs, *"our solver consumes cpu"*) → AC (no distance limit, printed stainless) →
quantum (nothing touches). The pass was four independent designs, three adversarial judges and a
critic (the workflow's journal is in the session's transcript dir); what survived: **connection is
membership, never transport, at every tier** — a wire, shaft or pipe is evidence that two rows share
a pool, an incremental per-chunk index of PortalIndex's shape answers *who is connected* at edit
time, the tick is O(awake machines) at every tier, and there is no solver to consume CPU; the DC
and AC tiers are one code path with `maxRunCells` 24 against null. Five things he added mid-pass
are taken in his words (iron under the table; stone and wood first; trees found and planted, seeds
from fire, growth on a worker after the tick; channel 0–15 with a per-channel cursor and every input
firing every tick; the channel set by right-clicking with a computer; faces on sub-quantum
machines), and his hose-as-FIFO is returned as decision 2 with a cheaper arm recommended. **Four
things the pass found that the morning's plan did not have**: the header and the machine table are
two store calls, so a recipe queue is a crash that duplicates inputs until a batch overload carries
the header (decision 5, the store slice's first test); `IsWetIn` is a dictionary lookup a voxel, so
the intake read is 27 cell lookups with a per-panel wet count and an active set, not a lever
(decision 14); a wire on a ceiling falls under M9's rule and `Wet` refuses a hose through a flooded
room (decisions 12, 17); and a chunk forgotten before its capped rescan would persist a stale
summary (decision 4). Every pump cap is fiction until `--pump` reports the automaton's throughput
into a 3 × 3 × 3 box. The paragraph below is the morning's state, kept for the reasoning.**

**Pick up here (2026-09-15, night). M10 is planned and not built — eleven decisions in
[docs/M10-PLAN.md](M10-PLAN.md), nine recommended and two wanting a word from Matthew (the
programmer's key, decision 6; the battery's numbers, decision 9); M10a's own planning pass follows
the answers, against `Place`, `ChunkPipeline`'s arrival hook, `IWorldStore` and the boundary block
in `Main.UpdateFluid`.** The pass's spine: **the boundary is the machine tick** — a pump is a drain
and an outlet is a pour, and neither can touch the grid anywhere else, so DESIGN's *fixed 20 Hz sim
step* is retired with M11's drill as the trigger to revisit; **a machine is a material plus a
row** — the body is one brush cell of `overburden:relay_pump`, so the mesher, the light, the
collider, the support field, the hardness gate, the per-voxel credit, the hotbar and `Q`'s
placement at range all work unchanged, and only the config (position, kind, ports) is new, in a
`machines` table at schema 4, read whole at open, written whole at autosave, **reconciled at every
chunk arrival with the row as the truth**; and **an outlet on the surface is outside the loaded
box the first time anyone pumps a mine out**, so the unloaded-outlet rule is M10a's — the
hand-off's *water on the analytic ground is Tier A's*, credited to the ledger, and an unloaded
outlet underground accepts nothing. `pumped == emitted` at every boundary on both tiers and across
them, with no in-flight term, is the oracle, and the M8 lifecycle battery with machines running is
the test. Power (M10c) is a pool per channel with no wires — the pipe argument a second time — and
the first generator is solar off `SkyColumn` and `OvercastAt`, so the storm that floods the mine
dims the panels that pump it. Shoring is not built: a placed pillar already carries a full budget
under `SupportRule.Rest`, and whether it *reads* as shoring is M10d's play. **Still Matthew's from
M9d: the four questions in TODO (one sentence each), decision 3's lake edge, and M9c-2's dust
verdict**; none blocks M10a. The paragraph below is the evening's state, kept for the reasoning.**

**Pick up here (2026-09-15, evening). M9d is built — the table-move trigger, a timer that only
shortens, the exponential jitter, the fill's refusal — and played once: *"Loving the collapse
functions"*, 14 falls with every identity exact, `table 1,297` requeues. The one finding was the
wet copy's staleness with a number (a chunk fill into a chunk mid-flood displaced 18,955 L past
3,516 refusals), and on his word **the chunk fill is deferred to the boundary** — a queue of clicks
run after the pour behind a fresh `Publish`, the brush still instant, `N chunk fill(s) at the
boundary` on the `M6c:` line. His per-chunk-thread question is answered and written into WATER-THREAD-PLAN's
revisit triggers (colour waves when `boundary(ies) waited` goes non-zero; the visit loop is worker time
nobody waits for). **After the play comes M9c-2's verdict (dust, optional) and then M10's planning pass
— read M10-NOTES first; the machine is the first entity and header v4 rides on it.** The four questions
are still his: the four questions
as recipes are in TODO under M9d, one sentence wanted back on each, and decision 3's edge (a pier in a
lake is allowed; *"not sure about 3 yet"*) waits on it. Read M9-PLAN's "M9d — built" first. Three
things not to rediscover: **the trigger is a comparison against what the derive read** — sixteen
readings on the cost table, memoised per sample (not per basin: `IWaterTable` hides basins), requeue
iff the rebuilt bytes differ, re-recorded either way or a flat move rebuilds every sweep; **the fill's
wet answer is `FluidGrid`'s per-panel bitset copied at `Publish` and read from the copy on one path,
never the grid** — the M8d-5b lesson as an assertion in `PlaceWetTests`; and **`--place` cannot reach a
pool headless** (it builds a column under the crosshair, M6d-2's finding), so the fill's wire is a
pipeline test with `GridOwnedByTick` true, and the play's `wet 64` is the first time it meets water in
the game. After the play: M9c-2 (dust) is judged, then M10. The paragraph below is the state at the
pass, kept for the reasoning.**

**Earlier (2026-09-15, later). M9d is planned and not built — ten decisions in
M9-PLAN's "M9d — the planning pass", eight recommended and two Matthew's (a fill under a Tier A
plane, and the jitter's widening before the play); build it once they are answered, Core first
against the tests the section names at its end, then the wire and the oracle.** The pass's spine:
**the trigger is a comparison, not a re-derive** — the cost table records sixteen readings per
sample at submission, the region compares fresh ones at the fluid boundary (memoised per basin)
and requeues under `Table` only when the rebuilt table's bytes differ, so a lake's fourteenth
metre over a roof already past the curve's last point costs nothing; **a timer only shortens**,
because `CollapseScheduler` arms once from the deficit it had and a dry plug's 60 s would
otherwise outlive the lake that rose over it; and **the fill's wet answer is a published per-panel
bitset read on one path**, never the grid — the M8d-5b lesson (a read of `Grid` mid-tick throws,
and under `--fluidjob=1` the grid is a worker's for up to a third of every second). `--saturation=0`
is the dry control the split's oracle row has named since the plan and is not yet a flag. The
morning's fix — a quit with a completed tick's surface in hand threw past the save, two sessions
lost their last 30 s — is in WORKLOG and commit `019177a`; read it before touching `_ExitTree`'s
order. The paragraph below is the state before the pass, kept for the reasoning.**

**Earlier (2026-09-15). M9d is next and wants its planning pass — read M9-PLAN's M9d
section against `CollapseWindow`, `SupportRegion` and `FluidSimulation` first. The night of
2026-09-14 ended with three things after the stall fix, all built, committed and played once:
the entangled bite is the brush cell (`Carve.Cube`, `CubeVoxels = BrushCell.Voxels`, so dig
and fill are one lattice by construction), the entangled fill is the brush at range, both at
10/s on Matthew's word (*"eventually this will cost a lot of power"* — the rate is an input to
M10's battery gate, not a frame ceiling), and **one click is one click** on that arm (an edge
in `Main`; Alt repeated until 2026-09-19, when the stream moved to Q held). `--entanglechunk` is the old chunk pair and every recorded bite
figure's arm. Three lessons that cost a pass each: a number in his message is what he sees on
screen (the brush cell is "4x4x4", and "8x8" was that cell, not eight voxels — ask which grid
before building a size); a hook on an arrival path runs mid-tick and may ask the automaton
nothing but `MayHoldWater`; and count a play log's `ERROR` lines before its reports. The
paragraph below is the state before all that.**

**Earlier (2026-09-14, night). M8d-5 is built, both halves — a lake is a body Tier B cannot
enter — and played: *"this version seems working now?"*, recorded on his word. A chunk bitten
out of a lake's centre reversed decision 7 first (a bite in a lake's bed fills only to the seed,
the water above is the lake's, so the plane draws over the bite and Tier B draws no top under a
lake cell), and a bisect by play put the original second sheet on M8d-3's bank rule, not M9c.
M9d is next and wants its planning pass. The silent pipeline stall that was at the top of TODO
is found and fixed (same night): the M8d-5b wake in `onFluidReady` read `_fluid.Grid`, which
refuses mid-tick, and the hook fires mid-tick for every dry arrival — the throw unwound
`Complete` before its neighbour push, M2c-2's stall to the letter; `MayHoldWater` is the read,
62 → 0 refusals on a played-world turnaround. **A hook on an arrival path runs mid-tick and may
ask the automaton nothing but `MayHoldWater`**, and a hook that throws there eats the push with
`faulted 0` (lever in TODO).** Read M8-PLAN's "M8d-5 — the planning pass", "M8d-5a — built" and "M8d-5b —
built" (its last paragraph is decision 7's reversal). Two things from 5b not to rediscover:
**the wrapper costs `--spill` 2.2× a tick and the game nothing measurable** (a second interface
dispatch over a 5 ns bitset; the game's visit is 2.9 µs Debug and read 16.4 ms with the ceiling
against 18.6 without), and **`--lakebody=0` is a measurement arm, not a play arm** — it is the
old world minus the bank rule, and a hole in a bank treadmills in it. The paragraph below is the
evening's state.

**Earlier (2026-09-14, evening). M8d-5a is built — a lake is a body Tier B cannot enter, in
Core — and M8d-5b, the wire, is next, alone, against the ten-step ordered brief in TODO under
M8d-5.** Matthew's afternoon play found M8d-3's bank rule producing two surfaces from the game's
own loop (a channel from a higher lake into a lower one: the Tier A plane at the lower lake's
level over a stepped Tier B sheet on its bed, `27,552 L left under a lake`); the rule is
replaced by `LakeCeiling` — the band of natural air over a lake's bed up to `floor(TableAt)` is
unholdable, water on the lake's top is handed like water on the ground, water found inside is
handed whole. Read M8-PLAN's "M8d-5 — the planning pass" (eight decisions, all taken) and
"M8d-5a — built" first. **Three things not to rediscover: the plane is one over the floor** (four
voxels of depth stand at 35 over a floor of 30); **`Add` refuses a lake cell**, so `R` over a lake
lands on its top and is handed at the next boundary; and **the treadmill's control arm never
rests** — it is the treadmill, and the test runs it for a fixed 600 ticks. The played `default`
was wiped at 14:01 and the new one holds 27,672 L of bed water that 5b's oracle expects handed at
its first boundaries. The paragraph below is the afternoon's state.

**Earlier (2026-09-14, afternoon). M9c-1 is built and played once: six falls in a steel-pick
tunnel, three of them under a pond with `lifted == returned` exact and `displaced 0` — the wet oracle
passed in play. Matthew's one note is the delay: *"I want it to be unpredictable … if you have left too
much overhang"* — the jitter `× U(0.5, 1.5)` makes a thin lip always go in about a minute; widening the
draw is one constant in `CollapseWindow.DelaySeconds` and waits on his word after the next play (TODO
under c-1b). He opened a fresh chat for something about the water; M9d is still next.** The paragraph
below is the morning's state.

**Earlier (2026-09-14). M9c-1 is built — c-1a's Core and c-1b's wire — and unplayed: the roof
comes down in the game, the oracle reads as planned (four `M9: fell` lines one boundary apart, `20
voxel(s) fallen`, `at risk 0`, the r6 control to the digit) and the bench's `--collapse` arm drops
all seven fixtures with both oracles holding. Read M9-PLAN's "M9c-1b — built" first. The play
checks are in TODO under c-1b (a `Q` 2 × 2 under stone on the played world with the window waited
out, the unstick on foot, a bite under the played lake for `displaced 0` and `lifted == returned`);
M9d — the table-move trigger, the fill's refusal, the four questions — is next and wants its
planning pass; M9c-2 (dust) is optional and judged after M9d's play. Three things not to
rediscover: **a fall's cost in the game is the per-voxel hook chain, not the fall** (3–13 ms Debug
for a plug's ten writes with water on, 0.5–0.9 under `--nowater`, 0.1 ms on the bench — the lever
is a per-chunk invalidation on a bulk edit, in TODO); **the 4 × 4 room sheds 54,740 voxels against
47,600 at risk** because the analysed set grows as centre roofs fall whole, and its last four stages
are the row above the roof (decision 3's paragraph is amended); and **the collapse is its own term
on the `boundary` line**, not part of the pour's.** The paragraph below is c-1a's state, kept for
the reasoning.

**Earlier (2026-09-13, evening). M9c-1a is built — the fall, the timer sweep and the water's
two terms, in Core with their battery — and M9c-1b, the wire, is next: alone, against the ordered
brief in TODO under M9c-1 (flags, the boundary block, the `M9:` lines, the unstick, the bench arm,
the headless oracle, then the docs). Read M9-PLAN's "M9c-1a — built" first: a plug falls one roof
per boundary because the barrier works, and the cascade is the analysed set growing, not the wedge
re-arming.** The paragraph below is the state at the pass, kept for the reasoning.

**Earlier (2026-09-13). M9c-1 is planned and not built — eleven decisions in M9-PLAN's
"M9c-1 — the planning pass", all taken as recommended the same day (*"as planned"*); build it
next, alone, as split, against that section and the tests it names at its end.** The pass's spine: **a fall is a column run** — the chunk's settled at-risk set
followed downward through at-risk voxels across chunk faces into settled chunks, waiting on an
unsettled one — because the voxel under an at-risk voxel is non-solid or at risk (derived from the
rule), and the plan's per-chunk timer over "every at-risk voxel" would turn a run straddling a
horizontal face to rubble *in place* and fire as a no-op every boundary; **the gate is `IsSettled`
now and the world**, because `SettledField` is the last settle's snapshot, kept until the next, and
a chunk mined since its timer armed would put rubble at a landing for a voxel that is air; **the
timer is a sweep at the fluid boundary** over settled at-risk counts, cleared at a fall and re-armed
from the new deficit, which is the whole of the cascade (a wedge's core is unreached, so stages
re-arm at the 1–3 s minimum; the plug's deficit of 8 gives 28–84 s); **the re-pour goes bottom-up
from the first cell above the new rubble**, through the traversed column into the cavity, and always
fits (a run of *n* lands in *n* cells and vacates *n*); **`lifted` / `returned` are Tier B's alone**,
the identity untouched, and a fire waits for fluid-ready chunks so `displaced 0` is an oracle. One
line of the split is taken: the unstick is c-1's (a body in rubble with no health is stuck on
every axis, `F` the only door) — **and damage, death and respawn went to M11 the same day** (*"for
debugging I dont need it really"*), so M9c-2 is dust alone and DESIGN's M9 and M11 rows say so. Priced by last night's bite —
131,072 voxels in 197 ms Debug, 1.5 µs a voxel through the hook chain — so a per-chunk fall is
milliseconds and the mid-air fill is two bites in a frame. Rubble: `rubble.json` (hardness 0.5,
`supportDistance` 2, permeability 0.6, no curve, not replaceable, `textureLayer` 4 — a fifth
`LayerColour`), an ID appended to an old world's table at open. Oracle: `--bitechunks=2
--collapseafter=1,2` reads `fell 20 voxel(s)` then `at risk 0`, r6 control to the digit; the bench's
`--collapse` arm drops every fixture and floods fixture 2. **Two M9b items are still owed and are
not c-1's**: the played world's open on a world with a shell, and the tint's look at the first play.
The paragraph below is M9b's built state, kept for the reasoning.

**Pick up here (2026-09-12, last). M9b is built — the support field is in the loop and the tint is
on the roof — and M9c-1, the fall, is next: alone, as split, and it wants its own planning pass
against `SupportRegion.SettledField` / `SettledAtRiskVoxels` (what a collapse may read, behind
decision 7's barrier already), `FluidSimulation` for decision 9's re-pour, and the `M9:` line.** Read
M9-PLAN's "M9b — the planning pass" and "M9b — built" first. M9b in one line: `SupportDeriveJob` is
the second detached job, pumped off the region's queue by `ChunkPipeline.Support` at most 32 a
frame; a trigger reaching a chunk in flight marks it stale and the landing requeues it (the whole
of the coalescing); a chunk is settled when it and its six are neither queued nor in flight, and
only settled fields reach the mesher, which puts one strain bit per face in the key on `UV2.y`.
**Three things not to rediscover: the parallel join re-derives 3–6× what the loop does (`requeued
stale`) and that is worker time nobody waits for; a Debug worker derive is ~5 ms with tiering on and
the Debug bench says the same; and the played world's open is still owed — the chunk-bitten void is
gone and `default` has one stored chunk in the spawn's box.** The paragraph below is the state at the
pass, kept for the reasoning.

**Earlier (2026-09-12, later). M9b is planned and not built — nine decisions in M9-PLAN's
"M9b — the planning pass", all recommended, waiting on Matthew; build it once they are answered.**
The spine: the derive is a *detached* job, not the chunk's slot (which would serialise it with the
urgent remesh); coalescing is the per-chunk in-flight mark and the plan's 250 ms timer is deferred
with a trigger; the tint reads only *settled* fields — a chunk and its six neither queued nor in
flight — and remeshes non-urgently only when the at-risk set changed; strain is one bit in the merge
key on `COLOR.b`; wired to `onVoxelsReady` / `onVoxelChanged` / `onChunkForgotten`, not
`onFluidReady`; `--bitechunks` bites three chunks down because the chunk under the player has a soil
roof; the first measurement is the played world's open in Debug with tiering on. The paragraph below
is the state before the pass, kept for the reasoning.

**Earlier (2026-09-12). M9a is built — the support field — and M9b, the wiring, is next and
wants its own planning pass against `SupportRegion` before any code: the join's queue becomes the
pipeline's, the derive a chunk job carrying a `PaddedChunk` and a `SupportFaces` gathered on the sim
thread, the strain tint behind decision 7's barrier.** Read M9-PLAN's "M9a — the planning pass" and
"M9a — built" first. M9a in one line: `Overburden.Core/Structure/` holds the rule (`K = 120`, not the
plan's 240 — the byte holds `[−K, K]` and the deficit's range is the budget's own), the per-voxel
byte field, the per-chunk cost table over the water table, a Dijkstra derive in a height potential
(0.8–1.1 ms a searched chunk, Release, tiering off; 0.2 ms to gather), and the join, which climbs
from unreached rather than descending from anchored (33 rounds → 2). **Three things not to
rediscover: a path dies at `−K` and a voxel no live path reaches is `Unreached` — a state, not a
floor, because the floor as a source held a mid-air chunk's top and then made a second fixpoint the
oracle accepted; an unloaded chunk anchors and a loaded one beside a dug one is analysed, which two
test fixtures got wrong before the code did; and the bench's per-derive figure is 3× at JIT tier 0,
so `--support` wants `DOTNET_TieredCompilation=0` and says which arm it ran.** The natural-cave row
sheds nothing on the first bite — the cave's ceiling is beyond the shell — and Matthew's verdict on
it is *"isn't an issue, feel free to make dangerous any old save"*: the shell stays, and the played
world gets no grace at open. The paragraph below is the state before the build, kept for the
reasoning.

**Earlier (2026-09-11, last). M9 is planned and all twelve decisions are taken — *"As
planned"* — and M9a, the field, is next: [docs/M9-PLAN.md](M9-PLAN.md) decisions 1–5 and the
bench section are M9a's brief. Read M9a against `ChunkLight` and `LightPropagator` first, because
support is skylight upside down — and against `FluidField`/`PaddedChunk` for the worker's copy. Its
first number is the per-voxel derive's ms on `--support`, before the rule is trusted; its oracle is
the unique fixpoint per voxel, and the seven cross-sections drawn.** Three things not to rediscover: **there is no bedrock** — the anchor is the edge of the
analysed set (dug chunks and their six face neighbours), M4a's *unloaded is solid* extended to *and
holds*; **the rule needs an arch or a 2 × 2 bite in stone is a shaft to the surface**, and the arch's
refund is a world constant with hanging at double cost *for the uniqueness proof*, not for physics
(a per-material refund lets dirt-on-stone pump itself full; hanging at single cost lets a column
across a chunk face stand forever); and **a path runs to the nearest wall**, so a gallery 8 m wide
never falls in stone and the smallest room that sheds is 2 × 2 bites — a 20-voxel plug dry, ~5,700
voxels under 5 m of lake. Two things are Matthew's beyond the numbers: health at M9 (DESIGN says M11)
and a fill refusing a voxel that holds water. DESIGN is amended after the decisions, not before. One
M8 check is still owed in play — the bank rule's `left under a lake` count on the `M8b:` line, on the
2026-09-09 scratch world's breach after a relaunch. The paragraph below is the state before the
pass, kept for the reasoning.**

**Earlier (2026-09-11, end of day). M8 is done and played — *"Look I love what we have, it
settles"* — and M9 is next, and it wants its planning pass first: read MATERIALS-NOTES before
`supportDistance` (unread in the JSON since M1), SCALE.md for what a support sweep iterates, and
`Saturation.HeadMetresAt` for the input M8c defined for it. One M8 check is still owed in play — the
bank rule's `left under a lake` count on the `M8b:` line, on the 2026-09-09 scratch world's breach
after a relaunch — and the M8d-4 verdicts are in TODO and WORKLOG. The paragraph below is the state
before the play, kept for the reasoning.**

**Earlier (2026-09-11, last of all). M8d-1, M8d-2 and M8d-3 were built — the Tier B mesh,
its look, the fall and the bank — with M8d-4, the play, left:
the four questions in M8-PLAN's M8d section, with the play recipes under M8d-2 and M8d-3 in TODO.**
Read M8-PLAN's three "built" sections first. M8d-3 in one line: a visit falls through vacant air up
to `--fallcells=` (16; 1 the old arm, `--fallcells <n>` on `--front` and `--fluid`), the mesher's
previous-height search reaches as far, and the hand-off leaves a rested cell standing when its
sample's lake stands above the cell's *top* — **the pass's "above the cell" was a voxel out and the
hand-off's own tests said so**; `Wake(to)` in `Record` is load-bearing now (the fall is the
non-adjacent transfer M8a kept it for); the lifecycle fixtures run `fallCells: 1` because they catch
water in mid-fall. The `M8b:` line prints `long fall(s)`, the `handoff` line `left under a lake`.
After M8d-4, M9 is next and wants its planning pass (MATERIALS-NOTES before `supportDistance`).
The paragraph below is M8d-2's state, kept for the reasoning.

**Earlier (2026-09-11, last). M8d-1 and M8d-2 were built — the Tier B mesh and its look —
with M8d-3, the fall and the bank, next.** Read M8-PLAN's
"M8d-1 — built" and "M8d-2 — built" first. M8d-2 in one line: `FluidWater.gdshader` — depth from
UV.y, the lerp from UV.x on `tick_phase`, depth written with back faces culled (**the mesh's winding
was the terrain's mirror and had to flip**), sides tinted by normal, an underwater wash asked of the
installed mesh; `--fluidsurface=debug` is the old geometry *and* the old shader. From inside the
water nothing of it draws, by design; wading wants the underside back. The paragraph below is
M8d-1's state, kept for the reasoning.

**Earlier (2026-09-11, later still). M8d-1 was built — the Tier B mesh — with M8d-2, the
look, next: M8-PLAN's "M8d-1 — built" first, then its "M8d — the planning pass"
decisions 2, 3, 4 and 6, which are M8d-2's.** `FluidSurfaceMesher` in Core builds merged tops
(within a 2 mm run tolerance, because a levelled sheet is level to a millilitre and a key on the
amount split the lake into 66,127 quads), sides between *drawn* heights wherever water meets air or
a lower fill, bottoms under falling cells, and per vertex the previous height and the wet depth in
UV — which the shared `Water.gdshader` does not read yet. The job carries the arrays; `FluidSurface`
only installs. **Three things not to rediscover: a covered cell is drawn full**, because the
automaton holds a falling cell part-full with air above and a falling column drawn as held is a
stack of slabs with the ground showing through the slots — the streaks in the first screenshot,
attributed with `--fluidsurface=notops|nosides` after two wrong hypotheses (a z-fight, a crack) had
been built and tested; **the previous surface is per panel in index order**, a cursor for the tops
and a search for the perimeter, because a dictionary cost more than the walk; and **the mesh is
opaque from every side** because every face blends and none writes depth — decision 4, M8d-2's
first item, now in a picture. Numbers: a lake at rest installs 26× fewer quads, a flood in
progress 1.5–5× more; the played world's open installs ~14,400 quads in 5–6 ms against slice 3c's 7.8 for 20,196. The
paragraph below is the planning state, kept for the reasoning.

**Earlier (2026-09-11, late night). M8d was planned and not built — ten decisions in
M8-PLAN's "M8d — the planning pass", all recommended; Matthew took them all the same night.** Sweat is the shipped seep mode now (*"I like this sweating"*; `--seepmode=spring`
is the arm), and [docs/WORLDGEN-NOTES.md](WORLDGEN-NOTES.md) holds the ores-layers-trees
discussion in Matthew's words with the three questions its own pass will want. M8d's spine: "more
3D" is three absences in the debug surface — no sides, no depth in the colour, no motion between
ticks; the surface install is already the largest main-thread term, so M8d-1 rebuilds the mesh as
merged tops plus sides on the worker before M8d-2 touches the look; the waterfall is the fall rule
from TODO (stateless, up to 16 a tick, `--fallcells=`); the lake-bank treadmill is the hand-off
refusing a cell under its own lake. The paragraph below is the night's state, kept for the
reasoning.

**Earlier (2026-09-11, night). M8c is built, played twice, and the water-bodies question is
answered: a drip does not force bodies, they stay after M8d.** The second play was a pit dug
*beside* the lake — decision 1 in a picture, the table is a level not a footprint — and it showed
the game's `awake at seeping boundaries` is the world's count, not the drip's (it reads the flood
and the loaded mine and would read the same with seepage off). `--seepawake` on the bench is the
clean measurement: at the shipped scale a 10 × 10 × 3 m room keeps 141 cells awake under a spring
and 18 under sweat, a tunnel-sized room is quiet 97% of ticks under either, and ×10 holds one to
two thousand. Sweat is the cheaper arm for the automaton, not the dearer. What is still play's is
the look A/B between the arms; M8d is next. The paragraph below is the evening's state, kept for
the reasoning.

**Earlier (2026-09-11, evening). M8c is built, played once, and has two delivery arms; the
play check that is still owed is the tunnel-only A/B.** The first play (scale ×10) read *"more like
a tap"* and Matthew kept it — hard rock lets water in through joints — then asked for the sweat
too, for wading: `--seepmode=spring|sweat`, spring the default. The awake count that decides
water bodies was confounded by a `Q` bite (419 mean while tunnelling, ~49,000 after the lake came
in through portals), so: one session tunnelling under the played lake without breaking through,
once per mode, reading `awake at seeping boundaries` off the `M8b:` line — the TODO has it as a
recipe. The wading and pump-ladder idea is in M10-NOTES. The paragraph below is the state at the
build, kept for the reasoning.

**Earlier (2026-09-11, later). M8c is built — all three parts — and unplayed; the play check
is the first thing to do, and its awake count is the water-bodies decision.** Read M8-PLAN's
"M8c-2 — the planning pass" and "M8c-2 — built" first, then the TODO's play recipe under M8c-2.
Matthew asked whether to back out of seepage because *"with infinite terrain, it will never
settle"*; the answer (WORKLOG, 2026-09-11) is that it is the design for the game, bounded by the
box for the engine, and a measurement for Tier B — and *"Ok lets continue"*. `SeepDrain` beside
`PortalDrain` at the boundary; `WaterSystem.Seep` with `seeped` on every line and in both
identities; the scan over a classified padded copy through the streamer's fluid gate (0.56 ms a
solid chunk against 2.5); the tracker re-scans a chunk whole past 64 edits in a boundary (the
bite); a full chunk is skipped until its faces change or sixteen passes pass; `--seepscale=`
(0.06 shipped, an estimate at the nuisance end, zero builds nothing); `Saturation.HeadMetresAt`
for M9. **Three things not to rediscover: the dug rule is on the inside's chunk (`SeepIndex.IsDug`)
because the portal rule's "every pair touching a dug chunk" wet a natural cave beside the first
shaft bitten; `--breach` bites the basin's *pit*, which on the profile seed holds 1,000 L and
drains through its own portals in a few ticks, so a seep provocation wants `--fillbasin=2000000
--seepscale=100` and reads `seeped` on the `seep` line; and the tracker's flush precedes the drain,
so anything reading the seep index mid-frame after a bite reads the first 64 edits.** Suite 1,257
→ 1,270; 30 break-recipes, 29 caught after three fixtures were fixed (nobody filled the room under
a low lake, nobody placed rock at a cave's face from the dug side, no cave sat on a chunk's side
face), the miss an identity — the tracker marks a chunk dug before it tells the index, so the
as-inside dug check on the edit path can never fail through it. The paragraph below is M8c-1's
state, kept for the reasoning.

**Earlier (2026-09-11). M8c-1 is built and M8c-2 — the draw — is next, and it wants its own
planning pass against the built index before any code.** Matthew took all eight of the pass's
decisions as recommended (*"M8c as planned"*). Read M8-PLAN's "M8c-1 — built" first: `SeepIndex`
beside `PortalIndex`, `ISeepTerrain` on `VoxelFluidTerrain`, `MaterialTable.Permeability` (the
JSON field's first reader since M1), `PortalTracker` feeding both indices under one dug filter,
`WaterSystem` as an `IWaterTable`, `--seep` on the bench; nothing in `Main` yet. Suite 1,234 →
1,257; 33 recipes, 32 caught, one identity. **Three numbers M8c-2's pass opens with, all Release
from `--seep`**: a chunk's seep scan is 3.5–7 ms and the floor is one 77 ns holdability read per
voxel (the plan said 1.5–2; the played mine's 4,584 dug chunks are 16–30 s of sim-thread scans at
open — the named lever is a scan over a padded copy, ~0.5 ms, workable on a worker); a chunk bite
through the edit hook is 14.4 ms against the portal hook's 0.9 (lever: a bulk edit that re-scans
the chunk at 3.5 ms); and the rate at the shipped 0.06 is 1.76 L/s through a 10 × 10 × 3 m stone
room under 5 m of head, ankle-deep in 3.9 h, dirt 17.5× that — the nuisance end, as decided,
`--seepscale=` M8c-2's sweep. **The table is not in the face**: the index holds every candidate
face and `Coefficient(chunk, table)` asks Tier A at the boundary, per (sample, Y) group. The
paragraph below is the pass's state, kept for the reasoning.

**Earlier (2026-09-11, small hours). M8c is planned — eight decisions in M8-PLAN's "M8c — the
planning pass", all recommended.** The pass's spine: **the water table under a lake is the lake**, so Tier C is derived
from Tier A's level and stored nowhere (decision 5's v4 question is answered: no), and **seepage is
the portal drain through intact rock with a permeability factor** — a seep face is a dug voxel
against rock below its column's table, filed per chunk the way portals are, drawn at the boundary
through `Remove` lowest-face-first. Three things not to rediscover: **the 388% figure is
`WindowCapacityLitres` summing leaf capacities by design**, so a merged lake reads over 100% and
the number is not evidence of a rim defect; **only dug chunks seep** (the portal rule); and
**M8c-2's awake count under steady seepage decides whether water bodies (WATER-BODIES-NOTES.md) go
before M8d**, because seepage is the first source with no end. The paragraph below is the state
before the pass, kept for the reasoning.

**Earlier (2026-09-10, late night, later). The shoreline is clipped by the terrain's depth,
not by the lattice — Matthew's rule, built and screenshotted the same night: a dry sample beside a
wet one draws the wet one's plane, only into the same lake, and the ground hides it wherever the
ground is above the level.** `WaterSurface.DilateShore`, `--shore=0` the control. Exact to the
voxel in the A/B on a copy of the played world, 1,667 → 2,389 quads, rebuild cost unchanged (3.4 ms
against 3.7 Debug). **The first version dilated across divides and hung a slab over a saddle** —
the played lakes stand at 108% of capacity, so the far slope was under the level — hence "same
lake". **This changes the 2 m decision below**: the look no longer depends on the lattice, so 2 m
buys only the curve at three times 4 m's analysis, and the honest A/B now is `--waterwindow=128`
with the cell back at 16 against the shipped 2 m, on the look alone. That A/B is Matthew's play;
water bodies (WATER-BODIES-NOTES.md) and the M8c pass are still what is next. **Played the same
night — *"looks awesome"* — so 2 m, the ring and the 1 cm plane bias (`SurfaceBiasMetres`, so a
flush voxel face wins the z-fight the ring exposed) ship; the 4 m arm was not run and is not a
flag, because the lattice check discards stored lakes on every change.** The paragraph below is
the 2 m state, kept for the reasoning.

**Earlier (2026-09-10, late night). The watershed cell is 2 m — `HeightField.DefaultCellVoxels`
16 → 8 on Matthew's request, because the sample is also the lake surface's quad and the shoreline
stepped at 4 m — built and unplayed.** One constant; `_waterPitch` is `512 m / cell` so the window
did not shrink; 25 tests were 4 m arithmetic (`PerVoxel = 4000`, `BasinAtSample(3, 3)`) and are
derived from the lattice now. The 512 m window analyses at 22.5 ms reference-equivalent (13.6 at
4 m), 130 ms Debug on a worker in the game, 391 basins at the spawn against 255. **Every world
written at 4 m opens with its lakes discarded once** (`M7d: the watershed lattice has changed`) and
refills; the mine's Tier B water is untouched. **The play check is the shoreline, and whether the
window's arrival is felt**; if 2 m is still coarse the next step is a render-only clip of the quad
against the seed height at 1 m inside a wet cell, not 1 m across the board. The paragraph below is
the state before it, kept for the reasoning.

**Earlier (2026-09-10, night). The water thread is built and played — slices 1, 2, 3a, 3b,
3c — and the next thing is Matthew's: M8c is the plan's next milestone, and it wants its own
planning pass.** 3c's play: worst frame 332 → 63.7 ms (field sync 43.9, hand-off 24.7, pour 17.5,
settle 0), one boundary waited over a 128,600-cell peak, identities exact. Two things that play
left in TODO: the surface install is the largest main-thread term now (7.8 ms for 20,196 quads a
tick — build the arrays on the worker), and Tier A stands at 388% of capacity on that world (the
hand-off feeds basins past their rims — look at a lake bank before M8c, since seepage is where
rims start to matter). Read WATER-THREAD-PLAN's "Slice 3c — built" first. Two things from 3c not
to rediscover:
**a chunk that leaves the box must lose its field at the next sync** (`Departed`), because its
panel keeps it reachable and a worker's view covering a departed chunk moves cells the eviction
then releases unlisted — the race battery found it on real threads and nothing else could have;
and **a recipe caught by a flaky test is not caught** — three of the first battery's catches were
the stress test failing on its own. `--fieldbakes=<n>` caps the bakes at a boundary (8) and a late
bake wakes the chunk as an arrival; the settle's solve runs at the job's tail. Generation does not
starve under a 2 ML moving flood (identical generates, first draw earlier), so the dedicated
thread's trigger has not fired. What is next is Matthew's — M8c is the plan's next milestone. The
paragraph below is 3c's planning state, kept for the reasoning.

**Earlier (2026-09-10, late evening). Slice 3b is built, played and accepted; 3c is planned
and not built — five decisions in WATER-THREAD-PLAN's "Slice 3c — the planning pass", all
recommended, waiting on Matthew.** The play: *"I got it to skip slightly but that was all"* — the log
says peak 153,250 active cells at a steady 2 Hz on the worker (172 ms mean / 319 max, 0 boundaries
waited), identities exact, 461 drawn; **the skip is the boundary's 332 ms frame — field sync 230,
settle 143 — and is 3c's first item**: bakes capped per boundary with a wake at landing, the
settle's solve on the job's tail with the order preserved, the awake count maintained; then the
race battery on real threads and the moving-flood generation measurement. The paragraph below is
3b's build state, kept for the reasoning.

**Earlier (2026-09-10, evening). Slice 3b is built and unplayed — the tick runs as a job on a
worker, `start.cmd` passes `--fluidjob=1`, 0 is the sim-thread arm.** Read WATER-THREAD-PLAN's
"Slice 3b — built" first. Suite 1,216 → 1,234; 25 recipes, 25 caught. **The play check: `Q` a
chunk out under a lake and read the `M8b:` line's `fluidjob` terms — worker wall, `boundary(ies)
waited`, refusals *for a chunk that had arrived* (the plain refusal count is the live terrain's own
"not loaded" and reads as an alarm that is not one) — and whether the world keeps drawing under
it; the `M2c:` line's generates against a `--fluidjob=0` run say whether generation starves, which
is the plan's trigger for a dedicated thread.** Three things not to rediscover: **ownership is the
batch or the job** (an empty batch left the flag false with a worker on the grid — the fuzz's job
arm found it); **`Wake` reads the visit terrain**, because a move wakes neighbours from the worker
(a two-counting-terrains test found the live one being asked); and **the open's two big frames are
the field sync (235–279 ms) and the settle's first pass (114–138 ms)**, both arms, never timed
before, now on the `boundary` profile line and `boundary worst` on the periodic line — the levers
are in TODO under 3b. 3c — the race battery on real threads and the moving-flood measurement — is
next and wants its own pass. The paragraph below is the planning state, kept for the reasoning.

**Earlier (2026-09-10, night, later). Slice 3b is planned and not built — nine decisions in
WATER-THREAD-PLAN's "Slice 3b — the planning pass", all recommended, waiting on Matthew; build it
once they are answered.** The pass's four findings: nothing changes what sets `GridOwnedByTick`
(`Begin` at submission, `Complete` at collection, only the visit loop moves to a worker); the worker
reads a captured `FluidField` view where `Unknown` refuses both questions, so the job arm requires
the field; the tick is the first *detached* job because `ChunkPipeline.Apply` is chunk-centric;
and a cancel must give the unvisited half of the batch back, so 3a's quit lever is built in 3b.
**3a is played and accepted** (*"Seems pretty good to me"*, 40 minutes on the played world: 10,031 deferred, 233 parked, `displaced 0`, identities exact). **An unknown game flag now prints `Overburden: unknown flag … — ignored`** — that play passed `--fluidjob=1` to a build without it and nothing said so. The paragraph below is 3a's state, kept for the reasoning.

**Earlier (2026-09-10, night). Slice 3a is built and unplayed; 3b — the tick as a job — is
next and wants its own planning pass against the built 3a.** Read WATER-THREAD-PLAN's "Slice 3a —
built" first: the stash's recipe was departed from three times, and each departure is a rule 3b
inherits. **The grid belongs to the tick** — mutations (`Disturb`, `ChunkArrived`, `Restore`,
`Evict`) queue and land, in arrival order, the instant the tick completes, by the tick's own
`Complete()` and not by a caller at the next boundary (a `Reclaim` in the gap could outrun a
deferred `Evict`); reads (`TakeAllDirty`, `TakeDirty`, `TakeEvicted`, `Reclaim`) and the
boundary calls (`Add`, `Remove`, `HandOff`) refuse. **The pipeline parks a wet arrival while the
grid is the tick's** (`FluidPending` stays set; `ReleaseFluidArrivals` runs it on the first frame
after; `IFluidPersistence.MayHoldWater` is the one question it asks mid-tick) **and a dry chunk is
ready at once** — the first battery run caught the dry branch falling through to a refusal, and
parking everything would have been the world not drawing under a flood. `SaveAll` refuses up
front; Main postpones the autosave a frame at a time, finishes a tick in flight before a quit
(printed), and queues Alt+R to the boundary like the pour. Suite 1,208 → 1,216; 24 recipes, 22
caught, both misses named. **The play check is one sentence: pour with `R`, bite chunks under it,
walk away and back — `displaced 0`, both identities exact, and the `M8b:` line's `deferred` and
`parked` terms non-zero.** Wire check headless: 1,166 deferred, 4 parked, 200,000 L handed. Three
things 3b must not rediscover: **`MayHoldWater` reads the panel dictionary the worker will be
writing** (publish a wet-chunk set at the boundary); **the reads still open mid-tick are listed in
`FluidSimulation.Ownership.cs`** (`AwakeHeldCells`, `TotalMillilitres`, `Rested`, `SurfaceCells`,
the `Grid`); and **a chunk that leaves and returns inside one tick waits for the boundary even when
dry**, because its mark is in the queue — rare, bounded, and named in TODO with its fix. The
paragraph below is the state before the build, kept for the reasoning.

**Earlier (2026-09-10, late). The water thread is three slices in and slice 3 is held on one
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
  game. Dropping it fails **11** tests, most of them pre-existing sphere and chip ones. (**The
  hardness clause went too at M10c-3** — Q swings a tool of its own with a threshold of 5.0 and
  a price a voxel; `Take` is still the one gate, reading that tool.)
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
[docs/M10-NOTES.md](M10-NOTES.md). Three things from it worth keeping:

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

**M8 is planned (2026-09-02) and not started** — see [docs/M8-PLAN.md](M8-PLAN.md), split
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
255 basins, 0.67 ms to rebuild**, four times a second (at the 4 m cell; the grid is 2 m since
2026-09-10 and those figures want re-reading from the `M7d:` line). **And since the same night a
dry sample beside a wet one draws the wet one's plane** — the terrain's depth test clips the shore
to the voxel, so the "4 m shoreline" below is history. Three things worth keeping:

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
[docs/M7-PLAN.md](M7-PLAN.md), split M7a–M7d, and read [docs/SCALE.md](SCALE.md) before
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
[docs/M6-PLAN.md](M6-PLAN.md), split M6a–M6d. **The game saves as of M6b-3 (2026-08-31),
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
- **`WorldGenSettings` is fourteen properties, not a seed, and all fourteen are written**, because
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

M2 is split into four parts in [docs/M2-PLAN.md](M2-PLAN.md). The prerequisite
reading of godot_voxel is done and paid for itself: it caught that the job queue had been
specified as `System.Threading.Channels`, and a FIFO can neither reprioritise queued work
as the player moves nor drop work that went stale before it ran. See
[docs/M2-REFERENCE-NOTES.md](M2-REFERENCE-NOTES.md).

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

**M4, the player, is done** — [docs/M4-PLAN.md](M4-PLAN.md), all four parts: M4a the
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

**The entangled miner is a debug bind, built 2026-08-31 to dig M5d-2's test volume — and
since 2026-09-19 it is two tiers on `T`, `quantum_miner` (the cube) and `quantum_excavator` (the
chunk), the mode and its Q toggle gone; Q held runs whatever tier is in hand** (*"holding q is
supposed to enable automine"*). What follows is the mode's history, kept for the reasoning; read
"Q" as "the quantum tier in hand". **Q** toggled it: point at anything, hold left click, and it eats what it is pointed at however far
away. **As of 2026-09-14 a bare click takes one brush cell — 4 × 4 × 4 voxels on the 1 m brush
lattice, `CubeVoxels = BrushCell.Voxels` so the two verbs cannot drift — at 10 a second (his
number, *"10/second eventually this will cost a lot of power"* — the rate is a design input to
M10's battery gate, not a frame ceiling; `--cubecooldown=`), and a bare *right* click places the selected brush at the same
reach and rate on the same lattice** — Matthew's *"make it all the same 4x4x4"* (first built at
eight voxels on a misreading of "8x8", corrected the same night). From 2026-09-03 until then a
click took **the whole 8 m chunk** — 32,768 voxels, 512 m³ — at **4 a second** and the right click
filled one, which `--entanglechunk` restores and which every bite figure below was measured on.
**And on the cube arm one click is one click** — a bare press fires once and never repeats while
held, **precise or not since 2026-09-19** (Alt kept a held repeat for fine work at range until the
modifier map was read back and it turned out to be a 40/s voxel stream whenever you crawled and
clicked in fly mode; the one way to stream is Q held now). **Since 2026-09-23/24 every quantum
tier is one click one bite, and its fill one click one fill** — the tunneller (a 2 m cube, the
T cycle's middle tier) on Matthew's word, then the chunk (*"yes make the chunk one click one bite
too"*); only the picks' sphere still repeats on a held button. Alt still chips one voxel. Three things about it that are easy to undo by accident:

- **It was deliberately not a tool tier until M10c-3, and the reach half of that still
  holds.** *Reach is the player's, not the tool's* is locked twice — M3d, restated at M4c —
  because putting it in the tool JSON would guess a field into a format mods have written
  against. **Since 2026-09-17 it is content — `tools/entangled_miner.json`, with a threshold, a
  cooldown and a `power` block — loaded by ID and kept off the T cycle, and the mode on
  `MiningController` swings it while Q is on. There is still no `reachMetres` in the JSON and no
  `ToolDefinition` field for it**: the reach is the mode's constant and the world's edge, which
  is the decision M5d protected; what the file carries is what the pass needed a file for, the
  price a voxel costs from the pack. **And since 2026-09-19 it is two tiers on the cycle with a
  `bite` field, and the reach follows the bite** — a cube or a chunk only exists at range, so
  the file says which reach and never how far; the lock holds a third time.
- **"However far away" means "as far as the world exists", about 144 m at r16.** What stops the
  ray is `RayOutcome.Blocked` at the edge of the loaded region — entanglement is not
  distance-sensitive but **the streamer is**. The 512 m constant is a backstop bounding the DDA,
  the same shape as `MaxRadiusMetres` bounding the carve. Aiming at sky costs no swing.
- **It composed with the number keys, and both halves of that have stopped being true.** It
  changed *how far* and *how fast*, **never how big a bite or what the bite is hard enough to
  take** — the bite clause went on 2026-09-03, because a bare click takes a whole chunk (a cell
  since 2026-09-14), **and the hardness clause went at M10c-3**: Q swings the entangled miner's
  own threshold, 5.0, so it takes a machine body at range exactly as a pick does — the ladder's
  rule, which was not true under a hand pick. **`Carve.Take` is still the only gate in the game**,
  reading the tool in hand's threshold and, since c-3, the budget the pack sets; what makes Q
  *earned* now is the pack and not the tier. Dropping the hardness check from `Take` still fails
  the sphere and chip tests — one edit breaking every shape is what a forked gate would not do.
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

**M5, lighting — [docs/M5-PLAN.md](M5-PLAN.md), split M5a–M5d. M5a, M5b, M5c and M5d-1
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

The licence is settled: [PolyForm Noncommercial 1.0.0](../LICENSE.md), source-available. It
can be loosened to MIT any time; it cannot be tightened after the first outside PR is
merged, which is the real freeze point.

## Appendix — the orientation table as it stood on 2026-09-26

| Document | What it is |
|---|---|
| [docs/DESIGN.md](DESIGN.md) | Locked decisions, the five non-negotiables, per-system architecture, milestones, **and a list of rejected alternatives so they don't get re-litigated** |
| [docs/M1-PLAN.md](M1-PLAN.md) | Worked example of how a milestone gets planned before it gets built |
| [docs/M2-REFERENCE-NOTES.md](M2-REFERENCE-NOTES.md) | What reading godot_voxel's scheduler and streaming taught us, and what it changed in the M2 plan. **Read before building the job queue.** |
| [docs/M2-PLAN.md](M2-PLAN.md) | M2, split into M2a–M2d with per-part design and tests. Done, kept for the reasoning. |
| [docs/M3-PLAN.md](M3-PLAN.md) | M3, mining, split into M3a–M3d. Done, kept for the reasoning. |
| [docs/M4-PLAN.md](M4-PLAN.md) | M4, the player, split into M4a–M4d. Done, kept for the reasoning. |
| [docs/M5-PLAN.md](M5-PLAN.md) | M5, lighting and AO, split into M5a–M5d. Done 2026-08-31, kept for the reasoning. |
| [docs/M6-PLAN.md](M6-PLAN.md) | M6, persistence, placement and inventory, split into M6a–M6d. Done and played 2026-09-01, kept for the reasoning. |
| [docs/M7-PLAN.md](M7-PLAN.md) | M7, rain, split into M7a–M7d. Planned 2026-09-01, **built and played 2026-09-02**. Kept for the reasoning; **read SCALE.md before it**. Two places it was overtaken: splashes were cut (M7c-2) and the occlusion field's cost estimate was ten times out (M7d-1). |
| [docs/M7-PLAY.md](M7-PLAY.md) | M7's play: five questions, **all five answered 2026-09-02**, the flags that provoke weather without waiting for it, and how to read the `M7c:`/`M7d:` log lines. Kept for the verdicts. |
| [docs/M10-NOTES.md](M10-NOTES.md) | **The quantum machine family** — Matthew’s proposal, 2026-09-02: a fluid pump that teleports to a wireless output, and a general machine programmer. Notes rather than a plan, on the deferred-collapse precedent. **Read before planning M10, and before shipping any machine — it names the entity-storage change that header v4 rides on.** |
| [docs/UNDO-PLAN.md](UNDO-PLAN.md) | **A three-step undo** — Matthew's ask of 2026-09-22, mid-play, after the quantum excavator: **planned and built the same evening**. Six facts, ten decisions, all recommended, and a "Built" section with five departures. **Read decision 4 and the built section first**: a fall clears the stack (bite → roof falls → undo would restore the roof *and* keep the rubble), the journal hangs off `ChunkPipeline.SetVoxel` and `MachineSet` rather than the verbs, and an undo must not journal its own writes. Nothing derived is undone — it all repairs itself from `SetVoxel`. |
| [docs/MAP-NOTES.md](MAP-NOTES.md) | **The map** — Matthew's ask, 2026-09-23: *"f12 opening a map 128x128 pixels facing your direction"*. A planning pass, not built: seven facts, twelve decisions, split a-d. **Read its facts first** — the terrain is analytic so a map needs no streaming and costs 1.2 ms whatever the zoom; rotation is free and loses no sharpness; where you have dug and where your machines are are both already in memory; **water is the only thing not available at range** (the 512 m analysis window). **Slice a is built (2026-09-23)** — `MapProjection` and `MapRenderer` in `Overburden.Core/Navigation/`, Godot-free; Matthew took 2 km across and no fog of war. **Read its "Slice a — built" section**: the oracle is that a place's colour does not depend on which way you face, the light is world-fixed for three multiplies, and **a map is 3.5 ms on a shaped world against the pass's 1.2 ms estimate** — flat across zoom as predicted, which makes sample-on-move required rather than tidy. **Slice b is built the same day** — `MapPanel`, F12, `--map` and `--mapscale=<metres>`. **Read its "Slice b — built" section**: the ramp is fitted to the ground in view and not to the world's amplitude (the first screenshot was one brown smear — 32% of the palette), turning is the expensive rebuild case the pass missed, and **the map aliases past about 16 m a pixel** — 4 m reads as landscape, 16 m as busy texture, 64 m as mush with the range collapsed. Coarser does not calm it, so slice c's zoom should stop rather than pay N² for supersampling. **Slice c is built the same day, with one thing open** — the mine from the stored-chunk set, machines as pins in their own material's colour, water with a rim where Tier A stops knowing. **The mine and the pins are verified in game** (`M12: map — 127 pin(s), mine drawn` on a copy of the pool world); **the water is not**, and the diagnostic says why it is not a water problem: **the panel builds exactly once** — `build 1` over 2,400 frames with a one-second maximum age that should have forced forty. **Slice d finished it the same day** and all three overlays are verified in game (`M12: map — 127 pin(s), mine drawn, water analysed here`; the analysed window draws as a diamond, being a world-aligned square under a rotation). **Read "Slice d — built"**: the one defect was `WaterSystem.LevelAt` walking into the merge tree with `Watershed.Outlet`, a label and not a node — **and because the throw was inside a Godot callback it was caught and logged rather than crashing, so it looked like a refresh-rule problem for a whole slice**. `LevelAt` answers null for an outlet now, as `TableAt` always did. **`grep` a run for the exception type before believing any theory about a UI that updates strangely.** The map also rebuilds once a second while you stand still, and gathers its overlay only when it rebuilds. **The map is the instrument the relief work is missing** — regions are 2 km and the view is 48 m. |
| [docs/TNT-NOTES.md](TNT-NOTES.md) | **Blasting** — Matthew's ask, 2026-09-23/24: *"how do you feel about tnt?"* then *"write up the TNT notes how you would"*. Notes rather than a plan, not scheduled, eight decisions with recommendations. **Read its table first**: almost all of it already exists — the blast is `Carve.Sphere`, the void is judged by M9 unchanged, a charge is a machine row with `fuse` and `delay` ports on a channel, a detonator fires the channel, and sympathetic detonation is the collapse cascade's one-step-a-boundary rule. **Blasted voxels go nowhere (Matthew's rule, the same day — no credit, no muck, no cleanup)**; the roof pays through *the shake*, one support derive under a harsher rule (distances × 0.75) around the blast, whatever fails it falling at the next boundary, nothing stored. The first number to take is the boundary-time carve, ~11 ms Debug a charge by M9's 1.5 µs a voxel. |
| [docs/COLLAPSE-NOTES.md](COLLAPSE-NOTES.md) | **The slow-then-fast collapse — "Tetris gone bad"**. Matthew's idea of 2026-09-26, after he demolished one of his own towers (four falls up one chunk column in 6.5 s, ~6,200 voxels; I had bet on "a few voxels or nothing"). Notes, not a plan; **the planning pass is a separate chat on `main`**. **Read its "What exists" first**: support is reach, not load; the cascade is the timer's re-arm; a fix inside the window already disarms; a timer only shortens; a fall is rubble in one sweep with nothing seen falling; Ctrl+Z is cleared by a fall. Four gaps against his brief (no pace that climbs, a chunk as the unit, no visible drop, all-or-nothing saves) and six leanings. |
| [docs/COLLAPSE-PLAN.md](COLLAPSE-PLAN.md) | **The collapse's planning pass, 2026-09-26, not built.** His second brief in his words (3D Tetris pieces, a release rate that climbs, *"height does not matter"* — the prank, the lowest point and spread out, pieces that separate, *"like digital corruption spreading"*). Seven facts, twelve decisions, split a–e. **Three were his and are answered**: the block is the **voxel**, shapes at my discretion (so the mino grows with the event's age, 0.25 → 0.5 → 1 m, or his tower is eight minutes of sand); the fall is **1 m/s climbing to 10 m/s**, stepping one mino a tick; the chimney goes **to the sky once serious** (30 s old). **Read its facts first**: the fall is pinned to the 2 Hz fluid boundary, so pieces step every frame and only a wet landing waits; one release unsettles the chunk, so a chunk's pieces are planned from one snapshot and pruned by the next settle (which is also the rescue); render-only keeps a falling piece out of the whole hook chain. The pace is by age: flat 15 s, doubling every 5. The other nine decisions wait on *as recommended*. |
| [docs/WORLDGEN-NOTES.md](WORLDGEN-NOTES.md) | **Worldgen as content** — Matthew's thinking-ahead, 2026-09-11: ores, layers, trees, and what is permeable. Notes rather than a plan; what MATERIALS-NOTES already decided is listed so it is not re-litigated, and the three questions its pass wants from him are at the end. **Not scheduled — M8d, then M9.** |
| [docs/WATER-BODIES-NOTES.md](WATER-BODIES-NOTES.md) | **Water bodies** — Matthew's question, 2026-09-10: resting water as one number over a connected void, DESIGN's *"stable cells merge into a Tier A body"* half that M8b never built. Notes rather than a plan, with the roof-open wake explained and six decisions. **Read before M8c's planning pass, which is where it belongs.** |
| [docs/MATERIALS-NOTES.md](MATERIALS-NOTES.md) | **Elements, and whether to "just implement chemistry"** — a brainstorm with Matthew, 2026-09-03. Notes rather than a plan. The recommendation is *geology that reacts to water, with machine recipes as the chemistry set*; the element table says why each material would exist. **Read before M8c reads `permeability` and before M9 reads `supportDistance` — both have been in the material JSON unread since M1, and the trap is a constant shipping green.** |
| [docs/M10-LADDER-PLAN.md](M10-LADDER-PLAN.md) | **The ladder — hand → steam → DC → AC → quantum, and the crafting under it; planned 2026-09-15 (evening) from Matthew's brief, twenty-three decisions, ten parts M10a–M10j, all taken as recommended the same night. M10a's own planning pass is its last section — six facts from the code, twelve decisions, three sessions a-1/a-2/a-3 — and all three are built (2026-09-15/16): the store slice, the entity, the pump; a-3's built section has the bench's first number, the relay delivering its nominal 100 L/s on a flooded chamber, and the read the ladder's decision 14 proposed retired by measurement. M10b's own pass follows it (2026-09-16): six facts, nine decisions, two sessions, the ID Matthew's — both built the same day, b-2's built section recording the two camera rules the pass did not have (the re-capture click is swallowed; flight is gated on capture). M10c's pass follows b-2's built section (2026-09-16, evening): nine facts, eleven decisions, three sessions — the pool is the tick's, the pack is the header's, the entangled miner becomes a tool; two things are Matthew's, both taken as recommended; all three built 2026-09-16/17 (the pool in Core; the wire, the three JSON files and the oracles; the pack on header v4, the entangled miner as a tool, the budget on every carve shape) — M10c is built and unplayed, the play recipe under M10c in TODO. **The inventory screen is folded into M10g** (2026-09-18, the last section — seven facts, eight decisions: one screen on `E` tapped, the hotbar a nine-slot assignment in the header at the next version, the placeable filter the grid's, no cap). **A swapped-out pack keeps its charge since the same day** — the first play's verdict, `CarriedPacks` at header v5, the amendment at the end of "M10c-3 — built".** Its one idea: connection is membership and movement is a ledger transfer at the boundary — no solver, nothing in flight at any tier. **Read its four facts first** (the 16 m vertical box against a 64 m mine; the header and the machine table are two store calls; `IsWetIn` is a dictionary lookup a voxel; a wire on a ceiling falls under M9's rule). M10-PLAN's a–d are its first rung, eight decisions amended. Read M10-PLAN and M10-NOTES before it. |
| [docs/M10-PLAN.md](M10-PLAN.md) | **M10, you fight back — planned 2026-09-15, eleven decisions with recommendations, waiting on Matthew; M10a's own pass follows the answers.** Split M10a–M10d: the entity, the relay pump and outlet / the programmer / power / the play. **Read its three facts first: the boundary is the machine tick (`Add`/`Remove` refuse mid-tick, so DESIGN's 20 Hz has no consumer), a machine is a material plus a row (the body is 64 voxels of a material, so every existing path is free and only the config is new), and an outlet is out of the loaded box on day one** (`ViewDistanceVertical` 2 against a 64 m mine). Read M10-NOTES before it. |
| [docs/M9-PLAN.md](M9-PLAN.md) | **M9, the mine kills you — planned 2026-09-11, all twelve decisions taken the same day; M9a and M9b built 2026-09-12 (read their "planning pass" and "built" sections — the byte moved `K` to 120, a dead path is a state, the join climbs; the derive is a detached job, the tint waits for the barrier); M9c-1 planned 2026-09-13 (a fall is a column run, the snapshot is not the gate) and built 2026-09-13/14 — c-1a Core, c-1b the wire; read "M9c-1b — built": the roof comes down in the game, the cascade is the analysed set growing, and a fall's cost is the hook chain. M9d next, unplayed.** Split M9a–M9d. **Read its "two facts" first: there is no bedrock, and DESIGN's literal distance rule opens a shaft to the surface over a 2 × 2 bite** — the plan's budget-with-an-arch rule and its uniqueness proof are decision 1. Read MATERIALS-NOTES and SCALE before it. |
| [docs/M8-PLAN.md](M8-PLAN.md) | M8, water gets in, split M8a–M8d. **M8c planned 2026-09-10, all eight decisions taken 2026-09-11, M8c-1 built the same day — read "M8c-1 — built" before M8c-2's pass.** **Planned 2026-09-02; M8a built the same day; M8b split four ways and M8b-1 built 2026-09-03; M8b-2 planned 2026-09-05 and all three parts built the same day, both open decisions taken as recommended — water crosses the seam in the game; M8b-3 planned and built the same day: the hand-off (3a) measured, the settle (3b) built and priced against the chamber, both mines and the played world, the spill's look deferred to M8c and the settle's interval an estimate with `--settleafter=` as the sweep.** Tier B’s cell size is settled at 0.25 m and SCALE’s open 64x question is answered in it. **Its M8b section records a gap the plan had**: a flooded mine has no Tier A body, so resting mine water is stored, and the settle is a solve. Read the tick-budget item in TODO before M8b-2. |
| [docs/FRONT-ADVANCE-PLAN.md](FRONT-ADVANCE-PLAN.md) | **The front advance** — Matthew's dream of 2026-09-24, *"a way water can spread laterally out faster"*. **Planned and built the same day (slices a–c; d, the look, is his): a 10,000 L pour reaches 12 m in ~5 s where it took ~25, `--frontadvance=` (4; 0 the control), `--fluidhz=` beside it.** **Slice 2a/2b built the same evening: "fed" is by volume now (`--fedshare=`, 1%; 0 the old any-inflow rule), and the `settle` line splits fed sheets by inflow share — on a copy of his world in rain every fed sheet was fed by 10%+, so 2b does not move his breach pit and 2c (streams pro rata) waits on his word.** **2c's pass is
written (2026-09-24, night) and not built: the plan's pro-rata rule puts a ridge in front of every
tapering stream (fact 1, arithmetic), so the recommendation is to level the stream's last 2N rings
with the new ones and leave its body alone; a fed sheet lists nothing as resting and is woken by
what the write gave a move. Eight decisions, all recommended, waiting on him.** **Built the same
night on *"As recommended"*: `--fedadvance=` (1; 0 the control) and `--fedband=` (6 rings of band
per ring of advance, **his play the same night at 6: *"that hits deep i like it a lot … i think its the one"*, with load testing still his** — the pass's 2 was shallow, the bench said so: a stream's tip is thin and the
band has to reach back into thicker water; open floor r17 → r24 at 60 s, a corridor's reach at
10 s 8 → 20 cells, nothing flowing back); the `settle` line prints `fed advance (band 4N) S
sheet(s): C cell(s), band B`. Read "Slice 2c — built". The look on his breach pit is his.** The plan: seven facts, eight decisions, split a–d. The sheet solve takes up to *N* rings of dry, holdable, supported cells at a levelled sheet's Y — the fall rule on its side. **Read its facts 6 and 7 first**: on natural ground the hand-off already takes a resting puddle, so this is for dug and placed floors; and the share and the quiet rule bound which sheets advance — a frozen or fed sheet does not at slice 1. |
| [docs/POND-PLAN.md](POND-PLAN.md) | **A pond you have shaped — the raised watershed.** Matthew's play of 2026-09-25: stone edging round a natural pond made its water vanish as the ring closed (the courtyard rule hid the lake inside it), and R into the raised pond spilled over the natural rim (the watershed reads the seed terrain). **Planned the same evening, taken as recommended, not built**: the player's material above the seed raises a 2 m cell to its highest placed top and nothing lowers it — **DESIGN's *edits do not change the watershed* is amended**. Ten decisions, split a (Core) / b (the wire). **Decision 10 is Matthew's water grid** (folded in the same evening): per voxel column in chunk columns he has built in, where the lake's level stands, derived by a flood from the unraised wet cells — so the shore is exact at a wall of any thickness, and the ceiling and the surface read it instead of `Enclosure`. **Read its seven facts first**: the sample is one corner point, so a raise is a max over the footprint; every edited column is already stored, so the map is derived at open and needs no table; and the ledger strands a pit a raise covers (decision 5 re-homes it). |
| [docs/GLASS-PLAN.md](GLASS-PLAN.md) | **Glass** — Matthew's ask, 2026-09-25, building windows into a reservoir wall: *"how do you reckon we get to glass from here?"* **A planning pass, not built, seven decisions all recommended, waiting on his word; built on main after the pond branch's verdict.** **Read its six facts first**: opacity already runs end to end (the mesher culls against it, draws nothing between two panes, routes non-opaque faces to a transparent buffer that nothing draws as transparent); solid and opaque are already two questions and every physical reader asks solid; **the sky column asks opaque, and one height feeds skylight, rain, the rain on placed tops, solar and the raised watershed**, so a glass roof would leak rain and a glass dam would not hold. The recommendation is one line: the sky column asks `IsSolid` — glass is a roof to the sky and a window to light (14 under a roof, a level a metre down). Sand waits for the furnace and never flows. |
| [docs/ADAPTIVE-RADIUS-PLAN.md](ADAPTIVE-RADIUS-PLAN.md) | **The adaptive view radius**, Matthew's proposal from the sprint corridor, planned and **built 2026-09-06**: fall to the 27-chunk floor while moving with a derive backlog, climb back a ring at a time while the derive is idle enough for one more. **Its "Built" section records that the plan's grow rule — a dwell — was wrong, and the 7 m/s corridor said so on its first run**; the rule is the light step's share of its budget now. Read the corridor worklog entry before it: the floor is a cliff edge, not a slope. |
| [docs/WATER-THREAD-PLAN.md](WATER-THREAD-PLAN.md) | **The water thread**, Matthew's question of 2026-09-07. **Slice 1 — the near/far order — was planned and built 2026-09-09** and its planning pass corrected the slice's own mechanism twice, which is why that pass is worth reading before the code; slice 2 is **priced and parked**, slice 3 unstarted. **Placed after M8b-4c and before M8c.** Read before touching the tick's order or its home. |
| [docs/CHUNK-CACHE-PLAN.md](CHUNK-CACHE-PLAN.md) | **The retained-chunk cache**, planned 2026-09-03, **slice 1 built the same day and slice 2 on 2026-09-04**: chunks leaving the box keep their voxels, column and derived light in a byte-capped cache, so a turnaround costs no load, no generate and no derive. **Both "built" sections record where the build departed from the plan** — a column rule the plan did not have, a column push that became a check at revival, and the three older light defects slice 2's walk found. Read the slice 2 section before touching `LightPropagator`. |
| [docs/SCALE.md](SCALE.md) | **How many voxels and cells exist at each view distance, what one full sweep costs, and what each system actually iterates.** Read before designing anything that wants to check the world periodically — M7, M8 and M9 all start here. |
| [docs/adr/](adr/) | ADR-0001: why we build our own voxel engine rather than adopt godot_voxel, **with named revisit triggers** |
| [TODO.md](../TODO.md) | Milestones as checkable tasks, and the decisions still open. **M6's section is the plan as a checklist.** |
| [MATTHEW-TODO.md](../MATTHEW-TODO.md) | **Matthew's own short list — the play queue, the decisions waiting on his word, and the chores only he can run.** Made 2026-09-23 on his ask (*"i'm burdening you with too much on memory when i should really have one"*). One screen or two, never a record: everything with reasoning behind it stays in TODO and the plans. **Keep it current at the end of a session** — move a played check to answered, add a new play check when one is built, refresh its status block. Its "Notes to myself" section is his; fold anything he writes there into the right plan and leave the section empty. |
| [WORKLOG.md](../WORKLOG.md) | What happened and why, newest first. Findings worth keeping live here. |
