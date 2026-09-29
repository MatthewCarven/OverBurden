# Water bodies — the planning pass (2026-09-28)

*"plan the water bodies with me please Claude"* — after *"Am i right in thinking that when water
settles as tier b water it wont be reclaimed by tier a because it is no longer processed?"* (the
answer was: it is handed over at the moment it rests if it rests on ground Tier A owns, and
otherwise stays cells for good). Read [WATER-BODIES-NOTES.md](WATER-BODIES-NOTES.md) first: what a
body is, what it buys, and its six decisions, the first of them answered by him on 2026-09-26.

**Nothing is built but the measurement.** `WaterComponents` (Core) and `--bodyprobe=<seconds>`
split Tier B's held water into face-connected components once and print them (`M8e:` lines).

## The numbers, from his world

All runs were on copies of TajMahal (backed up as `backup-TajMahal-2026-09-28`), at the play arms
(`--fluidnear=2 --fluidjob=1`), on the build fixed the same night (Core optimised; the tick at
2.00 Hz), with zero ERROR lines.

| | 45 s into the session | 320 s into the session |
|---|---|---|
| held water | 1,324,640 cells, 19.93 ML | 1,351,880 cells, 20.51 ML |
| components | 1,246 | 838 |
| the largest | 1,304,557 cells, 19.81 ML, 50% full cells, Y −94..−28, all below the seed | 1,339,715 cells, 20.43 ML (**99.6%**), 44% full, Y −94..18 |
| components of 64 cells or more | 42 | 27 |
| components under 64 cells | 1,204 | 811 — mostly ~1 L films one voxel thick on his building's floors (Y 112–168) |
| **awake** | 1,257,809 | **1,215,567** |
| one whole flood | 382 ms | 285 ms |
| the tick | 205 ms wall, 2.00 Hz | 188 ms wall, 2.00 Hz |
| panels | 18.0 MiB | 9.7 MiB |

`fluidmove` over the 330 s run, for 145 M visits:

| Move per visit | Share of visits | Share of the volume moved |
|---|---|---|
| rested | 17.9% | — |
| under 10 mL | 21.9% | 0.9% |
| 10–99 mL | 36.5% | 11.1% |
| 0.1–1 L | 15.5% | 35.0% |
| 1–5 L | 1.7% | 35.6% |
| 5 L and over | 0.2% | 17.4% |

## Seven facts

1. **His water is one body.** 99.6% of it is a single face-connected component, the quarry
   pool, 28 m tall and all below the seed. The rest are rain films on what he built and puddles.
   A body mechanism would retire one component here, which is nearly all of it.
2. **The water never comes to rest.** 1.2 M of 1.35 M cells are still awake after five and a half
   minutes at full speed, and the pool is only 44% full cells.
   - 58% of the visits move under 100 mL and carry 12% of the volume. That is dithering.
   - 19% move 1 L or more and carry 53%. That is the lake still pouring in at the rim (WORKLOG
     2026-09-27).
   - **The notes' decision 2 forms a body from resting cells, so on this world no body would ever
     form.** Rest comes before bodies.
   - This corrects the 2026-09-27 reading, *"a sleep threshold would save a fifth"*. That was
     taken on the unoptimised build, where the water was still flowing at 41% speed. At full speed
     the pour has spread, and the small moves dominate the visits.
3. **A whole flood costs 285–382 ms** on the sim thread for 1.35 M cells (a `HashSet` flood, the
   probe's). Finding a body cannot be a sim-thread flood at a boundary. It wants a worker, or to
   be incremental from a region that has gone quiet.
4. **`VolumeCurve` cannot hold a 3D void.** It is a heightfield: each sample adds
   `(level − floor) × area` with no ceiling. A mine has overhangs and stacked pockets, so a body
   needs a new curve: holdable voxels counted per Y layer, cumulative, with the same
   sort / prefix / binary-search lookup.
5. **Saving is a known pattern.** The world file is at schema 4 (machines); a `bodies` table would
   be schema 5, `CREATE TABLE IF NOT EXISTS … STRICT`, keyed by the body's lowest voxel.
   Permanent-data-loss territory, so it ships alone, as the fluid table did.
6. **A body has more writers than the edit hook.** It must answer:
   - everything through `SetVoxel`: digging, placing, undo, collapse rubble, `MachineSet.Restore`,
     the debug bites;
   - five water writers that bypass it: pump intake and outlet, the collapse's and the fill's
     lift and return, the portal drain's add, the hand-off, and the lake ceiling's wake.
   The pump would get a third intake source beside a lake and cells.
7. **Speed no longer forces bodies.** On the fixed build the pool ticks at 2 Hz with 188 ms of
   worker wall (visits 86, solve 93). What a body buys now is:
   - the idle cost: 1.2 M cells visited forever;
   - memory and save size: 9.7–18 MiB of panels, a 3.8 MB world file;
   - the surface mesh;
   - pressure for M10's underwater ladder.

## The falsifier, run: twenty minutes does not rest it

1,200 s into a session on a fresh copy, with zero ERROR lines:
- **The water:** 1,393,750 cells and 21.28 ML in 822 components. The largest is 1,380,516 cells
  (99.6%), 52% full.
- **Still awake: 1,157,586.**
- **The lake's feed never stops.** Drawn climbs steadily (9 kL at tick 9, 1.67 ML at tick 2,412,
  ~1.4 kL/s), and Tier B's total rises from 20.4 to 21.3 ML.
- **The visits:** 59% of 450 M move under 100 mL and carry 9.8% of the volume. 2.3% move 1 L or
  more and carry 63%.

**So the pool does not rest on its own, and part a stands.** Two causes are mixed in it, and part
a must separate them:
- **real inflow:** the valley lake draining through the rim for as long as it holds water;
- **the interior's dithering:** most of the visits.

A rest rule that let the fed rim stay awake and the still interior sleep is the shape wanted. The
question part a's pass opens with is how far one rim feed keeps the whole sheet awake.

## Decisions, each with a recommendation

1. **Rest comes first, as its own sub-part, before any body.** Recommended. Find why a still pool
   stays awake. The levers to measure are:
   - a move under a threshold (say 100 mL) counting as no move, so the cell rests;
   - the sheet solve's "level" tolerance;
   - the lake's inflow at the rim, which is real flow and should keep the rim awake.
   It is priced by the rested share and the awake count on this same copy.
   - *Reverse if* the pool rests on its own given time: run the probe at 20 minutes first, which
     is the cheapest falsifier.
2. **A body forms from a quiet component of at least 64 cells.** Recommended. Every cell must have
   been asleep for K boundaries (10 s). Smaller components stay cells: his 811 films and puddles
   cost nothing asleep, and a body each would be bookkeeping for a litre.
3. **Found on a worker, from the region that went quiet.** Recommended. The flood runs over the
   captured grid on a worker, bounded by the component, which is his decision 1: water, not void.
   The boundary only applies the result. The whole flood's 285–382 ms is the ceiling this has to
   beat, and it only has to run when something goes quiet.
4. **Communicating vessels** — answered 2026-09-26: a body is a connected component of water.
   Still open was a body formed from arms already standing unequal. Recommended: it forms only if
   the component is level within the rest threshold. Otherwise it stays cells and the automaton
   levels it. So nothing ever teleports.
5. **A disturbance makes a mouth, not a release.** Recommended. Waking all 1.34 M cells because
   one voxel at the rim was dug is the cost bodies exist to avoid.
   - A write inside a body's void or on its skin makes that voxel a mouth.
   - The body drains into cells there at the portal drain's rate, re-solves its level from the
     curve, and re-derives its void lazily.
   - A write that splits the void (a dam placed across it) releases the body back to cells, the
     one case that must, since two levels are then possible.
6. **The drain rate at a mouth is today's room-below rule.** Recommended. Head-driven (Torricelli)
   is decision 6 of the notes and waits for M10's pressure rung, which is the first reader of a
   body's head.
7. **Drawn as one plane per body** at its level, over its top layer's footprint, with the
   mesher's existing no-top-under-a-lake rule extended to "inside a body". `IsUnderwater` asks
   bodies too. Recommended.
8. **Identity terms `absorbed` and `released`**, in Tier B's identity the way `pumped` and
   `emitted` are, and a body's own `held == absorbed − released − drained + received` on the
   `M8e:` line. Recommended.
9. **The split.** Recommended, each part with its own pass:
   - **a — rest.** Decision 1: measure, then the rule. Worth doing alone even if bodies never land.
   - **b — the body in Core.** The layer curve, formation from a quiet component, level, mouths,
     identity terms, tests against the cells they replace.
   - **c — the wire.** The worker flood, the boundary, the plane, `IsUnderwater`, the pump's
     third source.
   - **d — persistence.** Schema 5, shipped alone.

"Nothing" is still an arm: with the build fixed, a still pool costs ~190 ms of worker wall at
2 Hz and no frame. That is affordable for one quarry, and it grows with every pool a player
fills.

## Taken, and part a begun (2026-09-28)

**All nine decisions taken:** *"as recommended"*. Part a (rest) opened with a measurement before
any rule. The probe now sorts the awake cells (`M8e: kinds`, `surface cells`, `awake cells over
room`), and a runner alternates arms on fresh copies of the backup.

**What the awake set is**, on a copy of TajMahal, zero ERROR lines, identity exact:

| 120 s into the session | cells |
|---|---|
| awake | 1,298,709 of 1,330,726 held |
| full cells, awake (nothing to move while their sides are full) | 465,749 |
| surface cells, awake | 392,024, of which **288,066 are within 5 mL of every side — level by the rule** |
| surface cells within 5–99 mL (dithering) / 0.1–1 L / 1 L or more | 74,960 / 17,827 / 11,171 |
| partial cells over room, awake | 369,780 — **the simulation's own terrain says the voxel below has room for every one** |
| distinct Y levels the surface stands at | 235 |

The runs at 300 s agree (1.15 M awake: 403 k full, 223 k level surface, 349 k over room).

**Three readings:**
1. **More than half the awake set has nothing to do.** Full cells and level surface cells are
   awake because a neighbour moved: `Record` wakes the six neighbours of a move's source, and the
   solve wakes its front. Each costs a visit that finds nothing, and the carry holds them across
   ticks.
2. **The over-room cells are the pool levelling through its depth.** Water moves sideways at every
   layer, and the column above refills the cells that gave. It is not a stuck field: the field
   answers "room" for all 369,780.
3. **The surface is 235 levels, not one.** His quarry floor is terraced, and the pour stood in
   mounds, so water is still cascading between terraces. Some of the awake set is real flow, and
   no rest rule should stop it.

**One lever tested and ruled out:** levelling fed sheets too. With `--fedshare=100` against the
default at 120 s:
- awake 1,253,372 against 1,260,802;
- tick wall 163 against 173 ms.
Being fed is not what keeps it awake.

**What part a's rule turns on, now:** TajMahal mixes three things — a lake feeding it at
~1.4 kL/s, 235 terraces, and a still interior. So it cannot say whether the automaton's rest is
at fault. **Next step, recommended:** a clean fixture. A large flat-floored box, poured once, with
no lake and no terraces, timed to rest on the bench (`--lake`'s shape). If it rests in reasonable
time, TajMahal's awake set is its feed and its terraces, and the rule is about waking less (the
full and level cells above). If it does not, the automaton's rest itself is the defect. That run
decides the rule, and it is the start of the next session.

## The clean fixture, run (2026-09-28)

The bench's `--rest` arm (COMMANDS.md). A walled box, flat floor, half full, at the game's settle;
tick counts, which do not depend on the machine's speed.

| Fill | Size (wide × deep) | Rested at tick | Minutes |
|---|---|---|---|
| level | every size | 1 | 0 |
| block | 8 × 8 m | 149 | 1.2 |
| block | 16 × 8 m | 217 | 1.8 |
| block | 32 × 8 m | 452 | 3.8 |
| block | 16 × 28 m (his depth) | 410 | 3.4 |
| block, near/far on | 32 × 8 m | 908 | 7.6 |
| edge, fed then stopped | 32 × 8 m | 7,194 (533 after the feed) | 4.4 after |

**The automaton rests a pool, so its rest is not the defect.** While fed, about 28% of the pool
stays awake; it is under 1% about 200 ticks after the feed stops. The near/far order doubles a
slump's rest, and a few hundred near cells hold far water at the 512-cell floor. That is the
named cliff, and it slows rest without preventing it.

**At 120 s the order made no difference**, standing in the quarry (`--at=-268,-5,1284`; his saved
spawn is nine chunk layers above the pool, which then never loads): ~1 M awake either way. That
was too early to tell; the pool takes about 12 minutes to settle.

## His pool rests with the near/far order off (2026-09-28)

At 900 s, viewer in the quarry, `--rainshare=0`, the whole pool loaded, 0 ERROR lines:

| arm | awake | of held | worker wall mean |
|---|---|---|---|
| order off, unfed (`--portaldrain=0`) | 3,959 | 0.3% | 186 ms |
| order off, fed | 28,657 | 2.0% | 246 ms |
| order on (`--fluidnear=2`, the play arm), fed | 369,661 | 26% | 339 ms |

**So part a's defect is the near/far order, not the automaton, the feed or the terraces.** With
the order off, a fed rim stays awake and the interior sleeps without any new rule. With it on,
far cells are admitted at the near side's count a tick, visited late, and a lagged pool takes far
longer to level. It also costs the worker more wall, because the awake set it keeps is visited.

**The options, for a decision after one measurement:**
1. **The order off on the worker arm** (`start.cmd` drops `--fluidnear=2`). The cheapest by far:
   one flag. It reverses the reason the order exists, which is that a far flood must not slow the
   water beside you. On the worker, that is the tick's wall: 246 ms here against a 500 ms budget.
   *The measurement that decides it:* the load-test world (`retired-spread-2026-09-22`, 3.4 M
   cells active at open), order off against on, alternated. The near water's tick rate is the
   quantity. *Reverse if* the tick there falls under ~1.5 Hz with the order off.
2. **Keep the order and stop it throttling a tick with room to spare.** Far admission would come
   from the tick's measured wall, not the near count. More code, and a clock in admission, which
   the order's deterministic-slices property rules out on the sim-thread arm.
3. **A rest rule for far cells.** Not recommended: the automaton already rests them when it visits
   them in time.

Recommended: measure 1's load test first. If it holds, take 1, and pumps' near columns keep
their meaning only on the sim-thread arm.

**The load test, run (2026-09-28): option 1 fails its trigger.** On `retired-spread-2026-09-22`
(3.1 M cells loaded, 209 machines), alternated, at 480 s: order off ticks 1.33 and 1.16 Hz (worker
wall mean 692 / 821 ms), order on 1.91 and 1.83 Hz (452 / 477 ms). The order off rests more (49%
awake against 81–84%) but the water beside you updates ~35% slower, under 1.5 Hz.

**Option 4, now recommended: raise the far floor** (`--fluidfar=<cells>`, default 512). Far
admission is max(floor, near awake); the 512 floor is what starves far water once little near it is
awake. A floor in the hundreds of thousands is option 2's effect without a clock, since it is a
count. No code: a sweep of 250 k, 500 k and 1 M on both worlds, alternated. It passes if the load
test holds 1.5 Hz or more and TajMahal's awake at 900 s comes near the order-off 2%. The
decision is then his: the default floor, or `start.cmd` passing it.

**The floor sweep, run (2026-09-28): option 4 fails.** On the load test at 480 s, the floor 250 k
changes nothing (1.94 Hz; near admission is already ~250 k), and 500 k and 1 M halve the rate
(0.96 / 0.94 Hz, worker wall ~955 ms), bracketed by two 512 controls at 1.97 Hz.

**The throttle is not the whole of it.** On TajMahal with the order on, far admission (117–253 k a
tick) nearly covers the deferred set (157–187 k), yet the pool is 13× more awake than with the order
off. The same 900 s runs show the solve levelling **8.4× as many sheets** with the order on
(1,264,716 against 149,865, over the same number of passes), with twice the seeds a pass. The lead:
the solve, seeded with the far carry, levels far sheets the automaton has not finished, and its
writes keep waking them. **Next:** count under the order the levelled sheets from carry seeds and the
cells the solve's writes wake. If that is the loop, part a's fix is in the solve's seeds.

**The solve counts, run (2026-09-28): it is the loop.** At 900 s, fed, order on against off: 1,172,966
against 199,230 sheets levelled or packed, **95% of them far**; the solve woke 10.2 M cells against
6.0 M, **90% of them from far sheets**; 309,642 against 27,126 awake. The same far sheets are levelled
hundreds of times: each levelling wakes its front, the front goes to the far carry and is visited
late, and the next pass, seeded from the carry, finds the sheet unsettled again.

**Candidate fixes, each an arm on this same run before any is chosen:**
- **a. Leave the far carry out of the solve's seeds** (a flag). Cheapest. It gives back the
  2026-09-19 reason for them: a wholly far sheet is then levelled by the automaton alone.
- **b. Seed the solve from the carry only once its cells have been visited**, not while they wait.
  Keeps far sheets solvable; more bookkeeping in the carry.
- **c. Admit a levelled far sheet's woken front as near for one tick**, so the automaton settles
  it at once, as it does with the order off. Touches the order, not the solve.
Recommended: a first, as the measurement that says whether the seeds are the whole loop.

**Candidate a, run (2026-09-28): it fails.** `--solvecarry=0`, 900 s: 415,160 awake against a
control's 214,900; 1.34 M levellings by ~1,000 passes with no carry seed at all, against 0.96 M.
The far sheets reach the solve through the pending list anyway. The likelier loop is a sheet
straddling the near/far edge: its near half dithers against its stale far half, the solve
re-levels the whole sheet, and the far front it wakes waits again. **Candidate c is next.** The
order-on arm's awake at 900 s varies 215–370 k between runs, so a candidate has to land well outside
that range to count.

**Candidate c, run (2026-09-28): it fails too.** `--solvefrontnear=1`, 900 s: 338,638 awake against a
control's 217,876; far levellings unchanged (1.09 M); 4.59 M of 8.84 M solve-woken far cells promoted,
the rest already in the carry. The order-on arm reads 215–370 k over four runs; neither a nor c comes
near the order-off arm's 27 k.

**Two guesses at the loop have failed, so the next step is an observation, not a third fix.**
Decisions for him:
1. **Look before fixing (recommended).** One instrumented run logging, for the most often re-levelled
   far sheets, where they are, how big, whether they straddle the near edge, and what unsettled
   each one since its last levelling (a visit, the lake's feed, a wake from above). About an hour
   with the run. *Reverse if* the sheets turn out to be one kind the rules below would simply cover.
2. **Or sidestep: the order only when it is needed.** The order-off arm holds 2.0 Hz on TajMahal
   (1.35 M cells) and rests it; it fails only on the load test (3.2 M cells, 1.2–1.3 Hz). A switch
   that turns the order on above an awake count (say 2 M, with hysteresis) would give his pool the
   order-off behaviour and the load test the order's. Cheap to build, and measurable on both worlds,
   but the loop stays for anyone past the threshold, and the threshold is machine-dependent.
3. **Or leave it.** The order-on pool costs ~300 ms of worker at 2 Hz, no frame time. Part b (bodies)
   needs rest, so this shelves bodies too.

**Two more variations, run on his word (2026-09-29):**
- **c-2, `--solvefrontnear=2`** (the carried front pulled near too): 141 k awake against a
  control's 187 k, far levellings down a quarter, worker wall 211 against 322 ms. Partly the lag,
  not the whole loop.
- **His "settle down but not up", `--solvewakeup=0`:** 99% of the solve's wakes are the cell
  above a drained sheet cell. Off, the solve's wakes fall 13× but its far levellings rise (1.48 M
  against 1.02 M), and 7,091 cells hang asleep over room. The solve's wakes are not the loop.

**What they point at together:** the re-levelled far sheets are layers with water above them,
inside the pool's depth, and something in the automaton's own visits keeps unsettling them.
**The measurement needs fixing first:** the order-on control reads 187–668 k awake at 900 s over
six runs, so one probe cannot rank arms inside that spread. Next, recommended: (1) a steadier
quantity (the queued curve's mean over the last five minutes) with repeats, and (2) option 1's
observation, now aimed: what, sheet by sheet, unsettles an interior far layer between two passes.

**The second round, on the five-minute mean (2026-09-29).** The probe's single count is too noisy; the
`queued` term averaged over a run's last five minutes is steadier (controls 351–451 k where the probe
read 187–668 k):

| arm | runs | queued, mean over the last 5 min | worker wall mean |
|---|---|---|---|
| order off | 4 | 80 k, 109 k (unfed), 174 k, 180 k | 186–217 ms |
| **c-2, `--solvefrontnear=2`** | 2 | **198 k, 236 k** | 211–224 ms |
| candidate a, `--solvecarry=0` | 1 | 314 k | 243 ms |
| candidate c, `--solvefrontnear=1` | 1 | 331 k | 297 ms |
| order on, controls | 7 | 351–451 k | 285–322 ms |
| 4th, `--solvewakeup=0` | 2 | 434 k, 444 k | 335–340 ms |

**c-2 is the one variation that holds**, twice: two thirds of the way from the order on to the order
off, at the order off's worker cost. **Next, recommended:** c-2 on the load test against the order-on
control (the quantity is the tick rate, ~1.9 Hz there; c-2 adds an O(backlog) walk a tick), then tests
and a battery, then his decision on making it the default. The observation of what unsettles the
remaining third stays open.

**c-2 on the load test (2026-09-29): it fails as built.** At 480 s, alternated, c-2 reached ticks
397 / 395 against the control's 918 / 947 (~0.8 Hz against ~1.9), worker wall ~1,070 ms against
~425. Its promotion of an already-carried cell sets the carry's reclassify walk, O(backlog) — about 2 M
cells a tick here. **c-3, next, recommended:** the same promotion without the walk. A promoted carried
cell joins the near batch directly and is tombstoned in the carry (its `_queued` entry marks it
taken), so `AdmitFromCarry` skips it. O(promoted) a tick. It passes if the load test holds ~1.9 Hz
and TajMahal's five-minute mean stays near c-2's 198–236 k. *Reverse if* the promotion itself
(~64 k cells a tick on the load test) is the cost: then promote only a sheet's first N front cells.

## What would reverse this

- The pool resting on its own within minutes. Then part a is unneeded and b starts at once.
- A second world where no component dominates, say a hundred mid-sized pools. Then formation
  cost, not the flood, decides decision 3.
- His answer that big standing pools are rare in play. Then "nothing" wins, and part a alone is
  kept for the idle cost.
