# Overburden — TODO

**Open work only.** Cut down on 2026-09-26 from 6,600 lines of mostly finished record; the whole
of it, every ticked task with the reasoning that produced it, is
[docs/TODO-HISTORY.md](docs/TODO-HISTORY.md), moved unchanged. When a WORKLOG entry or a plan
says *"in TODO under M…"* and is dated on or before that day, it means the history.

**How this file stays short:** when something here is done, tick it and write the finding into
WORKLOG and the plan it belongs to; at the session's close, delete the ticked line. The plans hold
the reasoning, the worklog holds what happened, and this file holds only what is still to do.
[MATTHEW-TODO.md](MATTHEW-TODO.md) is his short list — the play queue and the decisions waiting on
his word — and this file points at it rather than repeating it.

## Order and collisions — read before starting a slice (2026-09-30)

His ask, the day IM-5's decisions were taken: which open items **should be done first**, and which
**collide with planned work**. The planned order is the ideal machines' IM-5a, IM-5b, IM-6 (the first
playable), IM-7 and IM-8 (the quarry), IM-9, IM-10 (his play), with collapse f-1 alongside; then the ladder
(M10e–j). Each line names the slice it must come before.

**Do first, before the slice named:**
1. ~~**The flaky `JobSchedulerTests` test — before IM-5a.**~~ **Done 2026-10-01** (WORKLOG): one race, fixed on
   09-06, may account for every sighting; the test now submits the survivor after the thrower reports, so it
   tests its name every run instead of about one in twenty-five (none under load), and each failure says
   which cause it saw.
2. **The clay pond's suspected leak — before IM-5a.** IM-5a floods through the same Tier B engine, and a leak
   under it would read as the dump's. Blocked on his answer: which world, and where.
3. **IM-0's windowed surface install — before IM-5a fixes the 10 ML ceiling.** The ceiling rests on IM-0's
   headless half (385 ms of tick wall, about 16 ms of main thread a boundary). Drawing a 10 ML flood's
   surface is unmeasured, and a dump underground is exactly that. It takes his screen for about a minute.
   Re-read "For the load test" beside it: the hand-off's absorb at 32 ms a boundary from 530 k awake cells is
   the size of cost a 10 ML dump creates, and it predates the fixed build.
4. **The pond branch — at least merge main into it before IM-5a; better, his verdict.** IM-5a adds a Tier A
   door to `WaterSystem` while the branch rewrites the watershed, so each slice on main widens the merge. The
   verdict also decides where a surface dump into a walled pond goes. **The merge is done (2026-10-01, his
   word, `927daef`; WORKLOG): 99 commits, suite 2,219, r6 to the digit.** The verdict is still his.
5. **The hotbar's unit (his, in MATTHEW-TODO) — before IM-6.** IM-6 puts water in the hotbar's last slot
   (decision 23), so *"1,767,903,281 L doesn't fit"* arrives with IM-6, not M10g.
6. **The ten build warnings — before IM-6**, the big wire slice, so a new warning stands out.
7. **Collapse f-1 — before IM-9, ideally before IM-6.** IM-9's power sensor needs the `sense` extra f-1
   switches on, and f-1 carries the rule that a signalled-off cell never unpays a vault's bill (COLLAPSE-PLAN,
   the ideal-machines amendments), which IM-6's playable vault relies on.
8. **The engine-wide pin — before IM-7.** A chunk any door wrote stays loaded until the next autosave (IM-5's
   decision 8, its lever). IM-5 accepted the crash duplicate for the dump, as for placement; the quarry writes
   every boundary across many chunks and would multiply it. Its own pass (Levers).
9. **The `FluidField.Invalidate` lever and "a chunk-aware write path" — measured in IM-5b's bench first.**
   The pile writes up to 512 voxels a boundary, each cloning 4 KiB, and the quarry bites the same way. Design
   the bulk write once, for the pile, the quarry and the fill.
10. **The quantum rate that reads low on a profile — before IM-7's bench** quotes a rate.

**Collisions, two planned works on one code path or one question:**
1. **Water bodies part a and LAKE-EDIT's "hand back sooner at ~95%"** both change when Tier B hands back to
   Tier A (the hand-off and the solve). One pass, not two: LAKE-EDIT's question 3 is part a's.
2. **The pond branch, LAKE-EDIT's displacement, IM-4c's pier and IM-5a's surface dump.** On the branch a block
   cuts a lake's room and a wall becomes a basin's rim, so a surface dump into a walled pond goes where the
   branch's watershed says. Decide the branch before LAKE-EDIT's pass.
3. **F11's six rungs and "machines only where you are"** (taken 9: IM-7's quarry, and IM-5's release on
   arrival). 16h/12v loads 4.7 times 10h/6v's chunks, so quarries would run, and unloaded dumps release, over
   that much more ground. IM-7's pass decides whether machine work follows the view or a fixed radius, before
   the rungs go in.
4. **TNT's six decisions and IM-5.** A blast that destroys a vault makes a wreck, which dumps by IM-5's rules
   (the end of a dump, 5b's pile). TNT's pass reads IM-5 first.
5. **Header versions.** The header is v7 and IM-6 takes v8. M10g's line below said "v6; a v5 world" and
   M11's "v5 at M11"; both are corrected to "the next version after IM-6's".
6. **IM-6's water slot and M10g's nine-slot hotbar.** IM-6's pass reads the history's "An inventory screen,
   and slots that stay put", so the water slot fits the assignment M10g brings.
7. **Tier A seen from under it (Later) and LAKE-EDIT's wall breaking a sheet** both change how `WaterSurface`
   draws a lake. Do the look after LAKE-EDIT's decisions, or in the same pass.
8. **Each player's own channels (multiplayer) and IM-6's formats.** IM-6 fixes the header's mode byte and the
   kit on per-world channels. Per-player channels later is a row and save change; IM-6's pass should not make
   channels harder to key by player.
9. **A brush that defers whole when wet (Levers) and IM-4c's Ctrl+Z.** Built, it makes a wet fill's undo one
   press, not two. A synergy.
10. **The pile and a landing collapse's reserved cells.** A pile voxel in one forces a re-plot (collapse part
    c's decision 11, lifts off a reservation). 5b's refusal of reserved cells covers it.
11. **The commit-authorship rewrite and every branch.** A `filter-repo` rewrite has to take
    `water-column-test` too; do it with no branch in flight.

## Next, on my side

- [ ] **The ideal machines — all sixty-one decisions taken as recommended (2026-09-29)**
      ([docs/IDEAL-MACHINES-PLAN.md](docs/IDEAL-MACHINES-PLAN.md), "The split"). IM-0b is built and IM-0
      measured (the quarry takes the 2 m bite, his word). **IM-1 is built** (the plan, "IM-1 — built";
      suite 2,076, twelve mutations red, the r6 control to the digit). **IM-2 is built** (the plan,
      "IM-2 — built"; the backpack is a window, pumps are paid by priority; suite 2,088). **IM-3 is
      built** (the plan, "IM-3 — built"; the vault and the crate; suite 2,127, twelve mutations red;
      its r6 control read to the digit). **IM-3b is built** (the plan, "IM-3b — built"; the vault's own
      minute of its bill as it stands, and a fuse that waits on a paid boundary and goes out only when the
      minute is whole; suite 2,140, eleven mutations red; its six amendments propagated). **IM-4's pass is
      written, twelve decisions wait on his word** (the plan, "IM-4 — the code-level pass"): split into 4a
      (the siphon's queue, doors and twins, the water ray, the ID and the pocket's refusal), 4b (the vault
      in the ring) and 4c (the lake fill, priced at the siphon's 1,000 J a litre, his "delete" with the
      drain it would open). **All twelve taken 2026-09-30; IM-4a is built** (the plan, "IM-4a — built";
      suite 2,157, its r6 control to the digit). **Its mutation battery ran 2026-09-30: thirty-six red**, twelve of them
      only after six new tests closed the holes they found (the plan, "IM-4a — the mutation battery"; suite
      2,163). **IM-4b is built** (the plan, "IM-4b — built"; the vault in the ring, `pumped == emitted +
      vaulted`, the pump's own door; suite 2,175, sixteen mutations red; its r6 control read to the digit after
      the merge). **IM-4c is built, reduced at his word** (the plan, "IM-4c — built"): his answer kept
      today's lake rule (a pier) and asked for displacement as its own pass, so decision 10 is withdrawn.
      Built: Ctrl+Z cancels placements waiting for the boundary (a live bug), and `M7d:` prints Tier A's
      machine and siphon terms; r6 to the digit after the merge. **IM-5's code-level pass is written,
      sixteen decisions taken as recommended 2026-09-30** (the plan, "IM-5 — the code-level pass"; he
      asked for the plan only that day, and for the order and collisions above): split into 5a (the
      water, the end of a dump, the bracket) and 5b (the pile, with its bench first). Three amend the plan: a
      dump's end (an empty vault back to `Holding`, an empty wreck removed), decision 36's session carry
      replaced by accepting placement's crash lag (an unload batch is not a consistent cut), and machine
      materials decohered rather than piled. **Next: IM-5a**, after "Do first" 1–4 above. **Into IM-6:** the grade files at `ownChargeSeconds` 60, the
      runway counting the recharge and the return as transfers. **Into IM-6's panel:** his warranty line on the
      vault, **a lock on the vault's channel box**, and **power auto-scaled on screen** (W/kW/MW;
      files stay in watts). Plays after IM-6, IM-8 and IM-9. **Owed from IM-0:** the windowed surface install (his
      say-so, it takes his focus); a bite under a lake (IM-7's bench); a dump underground (IM-5's).
- [ ] **Building in a lake: displacement, a wall breaking a sheet, and a sooner hand-back** — his notes,
      2026-09-30, verbatim in [docs/LAKE-EDIT-NOTES.md](docs/LAKE-EDIT-NOTES.md) with what the code already
      offers and four questions. **His pick for the next session (2026-10-01): the plan for converting Tier
      A to Tier B where voxels are placed**, so a wall built through a filled basin separates its water and
      one walled area can be flooded on its own (the notes' "Chosen as the next session's topic"). His test
      world for it is `LakeTest` (`start.cmd` names it); TajMahal2 is retired. Read collisions 1, 2 and 7 above first: the pond
      branch and water bodies part a touch the same hand-off and watershed. (The
      lake over a quarry, his look 2026-09-30: *"draws nearly perfectly"*, a small gap sometimes, filled
      by Tier B soon after.)
- [ ] **F11: six rungs**, his ask (2026-09-30): 6h/2v, 8h/4v, 10h/6v, 12h/8v, 14h/10v, 16h/12v, and
      *"Its ok if the lazy loader never reaches the maximum like we will use that as an artificial
      limit"*. The rungs are one array, `FarRungs` (`Main.cs:320`, today `(8, 4), (10, 6)`). 16h/12v is
      33 × 33 × 25 = 27,225 chunks against 10h/6v's 5,733, 4.7 times as many: read
      [docs/SCALE.md](docs/SCALE.md) and check every cap sized to the radius, and the memory, before
      adding them.
- [ ] **The pond branch: test and optimise further on the branch**, his word (2026-09-30), not a
      verdict yet ([docs/POND-PLAN.md](docs/POND-PLAN.md)). It is also the displacement answer in Tier A's
      own terms (LAKE-EDIT-NOTES).
- [ ] **Water bodies — part a (rest)**
      ([docs/WATER-BODIES-PLAN.md](docs/WATER-BODIES-PLAN.md), "His pool rests with the near/far
      order off"). The clean box rests, and so does his pool with the order off (2% awake at 900 s,
      fed; 26% with `--fluidnear=2`). **Next:** the load-test world, order off against on,
      alternated — **done: the order off ticks 1.16–1.33 Hz there against 1.83–1.91, so option 1
      is out.** The far floor fails too (250 k does nothing, 500 k halves the rate). **The solve
      counts say it is a loop:** under the order 95% of levellings are far sheets, levelled over
      and over, and they cause 90% of the solve's wakes. Candidate a (`--solvecarry=0`)
      fails: far sheets reach the solve through the pending list anyway. Candidate c
      (`--solvefrontnear=1`) fails too; c-2 (`--solvefrontnear=2`) helps a little (141 k); his
      "settle down but not up" (`--solvewakeup=0`) shows 99% of the solve's wakes are upward, but
      they are not the loop. On a steadier measure (the five-minute queued mean), **c-2 holds in two
      rounds: 198 k and 236 k against controls' 351–451 k.** On the load test c-2 falls to ~0.8 Hz
      (its carry walk is O(backlog)), and c-3's tombstones leak duplicates (reverted). **Parked**
      on his word that limits are fine: a cheap c-2 needs a carry with O(1) removal, a planned
      piece of its own (plan, "c-3, run"). Bodies wait on it.
- [ ] **Tier B at scale — re-read on the fixed build.** On 2026-09-28 the cause was found: Core
      was built unoptimised under `dotnet build Overburden.Game` (WORKLOG). Fixed, his quarry's
      tick is 185 ms at 2.00 Hz (it was ~1 s at 0.9 Hz). Colour waves and bodies wait for a pour
      that is slow on the fixed build. **Owed:** his play on the fixed build, and a re-check of any
      headless oracle recorded since 2026-09-15 that reads a time.
- [ ] **For the load test:** TajMahal's 8 ML pour in motion costs `hand-off: absorb` 32 ms a
      boundary (65 worst), from 530k awake cells.
- [ ] **Collapse part c — two loose ends.** Played and the default flipped 2026-09-27
      ([docs/COLLAPSE-PLAN.md](docs/COLLAPSE-PLAN.md), "Part c — played"). Open: decision 11
      (lifts off a reservation, up to 4.5 m on a copy, 3.25 m in his play) on his "fix it"; the
      hand-off's cap (22–45% hit under a big collapse — the lever is a longer cap).
- [ ] **Collapse part d**, per the plan's split: the chimney, whichever arm he takes — a tunnel
      under a placed house, headless on a scratch world. (e, the phonk, is built and his ear is in:
      *"sounds good an want to leave it as is"*, 2026-09-30.)
- [ ] **Collapse part f — thirteen decisions taken as recommended (2026-09-29); f-1 next**
      ([docs/COLLAPSE-PLAN.md](docs/COLLAPSE-PLAN.md), "Part f, the collapse sensor"). Thirteen
      decisions; the sensor trips on a fuse, not only a fall, and its signal is an unsaved override,
      never a write to `enabled`. Slices f-1 (Core and tests), f-2 (wire, `--placeat=`, the
      oracle), f-3 (his play). Build f-1 in a worktree if he is playing.
- [ ] **M10e — the conductor index** (after the M10d play): `conductor` block, `ConductorTracker`
      marking a dirty set and nothing else in the hook, per-chunk summaries at schema 5 riding the
      batch write, `NetworkSet` with position-derived ids, `maxRunCells` as occupied brush cells,
      the rescan cap, the forget-path rescan, the energised bit, the cut's leak; `--network`.
      [docs/M10-LADDER-PLAN.md](docs/M10-LADDER-PLAN.md); each part wants its own pass against the
      code first.
- [ ] **M10f — generators, converters, the crank and the sink**: `E` held with millis sampled,
      `burner`, the boiler that drinks and `consumed` on every identity, the engine beside it,
      `shaft`, media order, the dock, `pipe_outlet`, `flooded`.
- [ ] **M10g — recipes, stations, parts and the inventory screen**: the loader's second pass,
      jobs by value, the remainder per output, `unit`; the inventory screen — one `Control` on
      `E` tapped: the grid of every holding, the hotbar as a nine-slot assignment in the header
      at the next version after IM-6's v8 (the header is v7 today; an older world opens with its first
      nine holdings in ID order), assign
      by number key and by drag, the grid's filter (all / blocks / parts / machines), the craft
      pane within reach of a station, litres → m³ from 1,000 L (the hotbar's unit, his to
      confirm), a total and no cap; `--assign=<slot>=<id>` and `--openinventory` the seams; the
      relay's own recipe retires M10a's seeding. Its pass reads the history's "An inventory
      screen, and slots that stay put" first and puts the inventory half in its first session.
- [ ] **M10h — worldgen for the ladder**: [docs/WORLDGEN-NOTES.md](docs/WORLDGEN-NOTES.md)'s pass
      first, its three questions answered (his); logs and saplings, layers, seams and veins by a
      second lattice field. Also where a seed stops moving only the landscape and starts moving
      its *kind* — the strata, cave and surface settings are still thirteen fixed fields.
- [ ] **M10i — the five tiers' JSON**; solar re-classed to `dc`.
- [ ] **M10j — the play**: the ladder climbed once, timed; five named moments as five sentences.

## Waiting on his word — the detail is in MATTHEW-TODO

- [ ] **The pond branch**: merge or retire `water-column-test` (the raised watershed and his water
      grid, [docs/POND-PLAN.md](docs/POND-PLAN.md), built there, not on main).
- [ ] **M10d-2, the play** — the seven recipes in the ladder plan's "M10d — the planning pass".
      Watch for origin-order draw starving later pumps under panels-only power (decision 4; the
      lever is the outlets' ring on the pumps, not built until seen). **Two answered (2026-09-30):**
      3, dry: *"could tell where it was getting pickuped up from and where it was going"*, and outlets
      read best up on pillars with gaps between them; 4, the storm: *"I kind of like that the rain slow
      the pumps"*. Five to go.
- [ ] **The fed-advance load test** (`start3` against `start3 --fedadvance=0`). Front advance
      slice 2c, the pro-rata advance for real streams, waits on it
      ([docs/FRONT-ADVANCE-PLAN.md](docs/FRONT-ADVANCE-PLAN.md), "Slice 2").
- [ ] **Glass**: water behind glass drawn in the wrong order, trigger noticed in play. (The storm
      gleam, his look 2026-09-30: *"it looks fine during rain and storms"*.) His wish the same day, for
      the materials pass: something as strong as stone and as waterproof as clay, manufactured
      ([docs/MATERIALS-NOTES.md](docs/MATERIALS-NOTES.md), "His wish").
- [ ] **TNT** — [docs/TNT-NOTES.md](docs/TNT-NOTES.md), six decisions. Needs from M9 before the
      shake: a rule per derive, and a fall from an explicit set.
- [ ] **Dust when a roof comes down** (M9c-2), optional, only if falls read as too quiet.
- [ ] **Precision mining's key: Ctrl or Alt.** Ctrl descends in fly mode (~1.8 m sunk between
      chips); Alt means *crawl* there, so it pairs. One line in `MiningController`.
- [ ] **Rename stage 4**, his: the GitHub repo, the local folder, the remote URL, the docs naming
      `github.com/MatthewCarven/MinerCraft`, and the assistant's memory directory keyed on the old
      folder path.

## Owed checks


- [ ] **Re-judge `start.cmd`'s `--lightbudget=16` on a play** — it answered a 1.4 ms unoptimised
      derive that is ~0.36 ms since the JIT optimiser (2026-09-15); and re-take the Debug bench
      figures quoted against game numbers (`-c Debug -- --support`'s 0.83 + 4.4 ms) before quoting
      them again.
- [ ] **The release export with SQLitePCLRaw's natives is unverified.** One run; not needed for
      play.
- [ ] **The quantum rate on a profile reads low**: `mining … (N/s)` under `--holdq` reads 4.2 for
      the miner's nominal 10/s and 2.1 for the tunneller's 4/s. Find the denominator or the frames
      the hold misses before quoting any quantum rate from a profile.
- [ ] **Give `--light` a machine-control line** (the other arms print one), and say whether
      another Godot process was found — a 2.3× reading once was his game running beside the bench.
- [ ] **The played world's open on a world with a shell** (M9b): the `support` line and
      `M9: at open` are ready for the first world dug big enough.
- [ ] **What drained a paused cell is unproved.** If the `M6:` count of awake indexes dropped for
      holding nothing keeps climbing on a world written entirely after the fix, look at the
      hand-off taking from a cell the worker levelled on its captured view after the chunk left.
- [x] **`JobSchedulerTests.AThrowingJobFaultsWithoutTakingTheWorkerDownWithIt` has failed
      intermittently** — not a flake until the reason is found. **Closed 2026-10-01** (WORKLOG).
- [ ] **The clay pond in the rain may leak** — his note (2026-09-30): *"IDK i think we have a leak
      bug"*, on the rain play's clay pond check. Ask which world and where before probing; then a copy,
      the `seep` line's face count and the pond's litres over a storm (`--clockscale=20`).
- [ ] **Water under his clay roof (2026-09-26, `start5` on `Regions16-pondlook`; found on the pond branch,
      brought over when main was merged into it, 2026-10-01)** — may be the clay pond above's — *"water seems
      to be leaking through my clay roof not sure why it might be residual or horizontal travel
      idk honestly"*. Clay is permeability 0 and a Tier B cell cannot enter a solid voxel, so the
      question is where the water is born, not the roof. Candidates, cheapest first:
      (1) **rain on placed tops lands on the roof as cells** and runs off an open edge or a gap
      into the room;
      (2) **Tier B travelling sideways** in from an opening — his pours and the diggings under
      the pond were moving 1.7 ML;
      (3) **sweat seepage from natural faces in a dug chunk** under the table (71 L that
      session);
      (4) water that was inside before the roof closed.
      **What to capture next time**: the coordinates under the drip (the HUD line), a
      screenshot looking up at it, and the `M8b:` line's `rained … onto the player's material`
      and `seeped` terms at that moment. Not the overhang pass's; see POND-PLAN's last section.
      **Looked at the same day on a copy, from his position (x -849 m, y 34 m, z 283 m), with
      temporary probes (not committed): the roof is sound (stone 148-151, clay 152-155, no gap).
      The drip he saw is rain running off the clay eave 2 m past his glass wall, falling outside
      and seen through the glass. Separately the south glass wall (z 1140-1143) has a one-voxel
      slot at y 134 through its whole thickness, found at x -3413, so the room's air reaches
      open sky. The floor holds a sealed 1,550 L film (1 L a cell, y 128) that touches no outside
      water, and nothing flowed through the slot while it rained, so the film most likely
      predates the roof or came in from a ledge elsewhere along the slot. Told him: fill the
      slot; a film that returns after a rainy session is a real path, traced the same way.**
- [ ] **The ten build warnings `start.cmd` prints** (his question, 2026-09-30; he took them for lint, and
      they are): eight nullable ones in `Main.cs` (CS8618, CS8602), `MapPanel.Scale` hiding Godot's
      `Control.Scale` (CS0108), and CA2014 on a `stackalloc` in a loop in `LightPropagator.ForEachCellAround`
      — checked, six passes of 12 bytes, no risk. A small chore: clear them so a real warning stands out.

## Levers named and not built

Each waits on its trigger; the history has the measurement behind each (grep it for "lever").

- [ ] `PaddedChunk.CopyFrom` through `VoxelChunk.Get` — 59% of the light derive, a fifth of a
      support derive.
- [ ] `FluidField.Invalidate` clones 4 KiB a voxel; a per-chunk invalidation on a bulk edit.
      Trigger: a fall under the played lake hitches.
- [ ] A brush that defers whole when its box is wet at all, so the click's dry path cannot
      displace by staleness (`displaced 176 L` seen). Trigger: a play that notices.
- [ ] A per-column reading cache for the support table's sweep. Trigger: `table` on the
      `boundary` line over a millisecond on the played world.
- [ ] Hold a chunk's support submission until none of its six is in flight (the parallel join
      re-derives 86–209 against 34). Trigger: `requeued stale` mattering.
- [ ] `--supportcadence=` — only if derives/s on the `support` line scales with swing rate until
      worker time shows on `sources`.
- [ ] The landing solver testing only each column's bottom profile down and the leading face
      sideways. Trigger: part b's frame budget.
- [ ] A chunk-aware write path for the entangled fill and bite (the palette filled per chunk).
- [ ] The water tick across workers; a quit that finishes the tick rather than cancelling it;
      bake fields as jobs.
- [ ] **The engine-wide pin**: a chunk any door wrote (a placement, an outlet, the siphon's pour, a dump,
      a quarry) stays loaded until the next autosave, at most 30 s, so the chunk and the session land
      together and a crash can't double what was written (IM-5's decision 8; M10-PLAN decision 4's
      accepted lag). Its own pass. Trigger: before IM-7, whose quarry writes every boundary.

## Later — not scheduled

- **`start.cmd`'s four flags become the code defaults with the first build someone else plays** — his
      question, 2026-10-01, taken as recommended the same night (*"as recommended"*). Not now: until that
      build (the creative demo's export) `start.cmd` is the only way anyone plays, so the defaults only serve
      measurement. **Trigger: the pass that makes an exported build.** The facts (Main.cs lines as of
      `b7d6fe7`):
      - **Tests are untouched either way.** Core keeps its own defaults: `ChunkPipeline.DefaultLightBudget`
        4 ms (`ChunkPipeline.cs:89`) and `_nearRings` 0 (`FluidSimulation.NearField.cs:101`). Adaptive and
        the fluid job are Main-only.
      - **The r6 control would break on adaptive alone.** The box starts at r1 and grows a ring per ~2 s at
        best, so a ~18 s run may not reach r6. fluidnear and fluidjob can't move it: the seed is dry for 40 s,
        and a detached job is not counted in `CompletedJobs`. A 16 ms light budget probably keeps viewed,
        drawn and quads, but jobs could move through relight remeshes; it has never been measured.
      - **Nearly every recorded recipe relies on today's defaults**: 18 fenced and ~33 of ~35 inline name none
        of the four.
      - **The light budget and adaptive are coupled.** Growth needs `LastLightStepMillis / budget` under 0.5
        (`Main.cs:3682`), so flipping the budget alone changes growth too. And 16 ms has an owed re-judge
        (Owed checks): it answered a 1.4 ms unoptimised derive that is ~0.36 ms now.
      - **`screenshot.gd` waits 8 s by the clock**, so with adaptive on it would shoot a partly grown box.

      The shape when it comes: flip all four, have the measurement modes (`--profile`, `--fall`, headless)
      imply the old arms unless a flag says otherwise, and re-judge the light budget first.

His answers of 2026-09-29 to MATTHEW-TODO's "things I'd like your eye on", as work (verbatim in WORKLOG):
- **Tier A water seen from under it** should match Tier B's colour and transparency: *"under tier b water
  looks real under tier A not so much"*, and *"you can do this now i think the engine for water is as good
  as its gonna get"*. It is a look question, so it wants screenshot pairs on a copy of a played world (a
  windowed run takes his focus). Buildable alongside IM-4 on his word.
- **Weather sound**, rain and wind: *"do you reckon you could pysynthrack to generate the sounds of wind and
  rain? I do have some prerecorded storm sounds"*. That is sound raised a second time (the phonk was the
  first), which is the cosmetics pass's trigger below. His recordings are the first source to try;
  PySynthRack is his other project.
- **Reach by tool tier**: *"yeah but only for the lower tier tools you can increase this depending on tech
  level like hands would be a bit punishing to start with"*. So 5 m stays for the low tiers, reach grows
  with the ladder, and bare hands are not the start. This goes into M10g's tools and the game-mode item
  below.
- **Something to dig for**: ores (iron and coal) and a tree pass,
  [docs/WORLDGEN-NOTES.md](docs/WORLDGEN-NOTES.md), "His word, 2026-09-29".

- **Hand mining one voxel at a time as the bare click** — deferred to tools and tiers (M10g, M11
  at the latest). Deferring is free: which carve a bare click calls is one line.
- **A cosmetics pass**, gated on CPU *and* GPU headroom at the end of the roadmap: splashes (cut
  at M7c-2, four findings on the shelf), the first sound (*"failed — insufficient charge"* on the
  quantum miner's three `M10: gate` prints; `AudioStreamPlayer` on `Main`), and anything deferred
  there. Revisit trigger: a cosmetic raised independently a second time.
- **Water bodies** — resting water as one number over a void,
  [docs/WATER-BODIES-NOTES.md](docs/WATER-BODIES-NOTES.md). Deferred on M8c's measurement.
- **Rivers** — likely an analytic channel found at worldgen, a chain of basins drawn as a Tier A
  body with a current, Tier B only where breached. A guess to start a pass from. Erosion stays
  out.
- **A game-mode concept** (creative / survival). Waiting on it: reach, the fly-mode gate on
  precision, a 3 m or 2 m reach, swimming, drowning, buoyancy, wading and oxygen, jump height.
- **Deliberately not in M10**: shoring as a material (a pillar already carries a full budget),
  random distribution, a machine cap (trigger: `machines` over a millisecond on the boundary
  line).
- **The seed after the banner fades** — no pause screen or world listing shows it. Trigger: the
  first time he wants the seed of a world he is standing in.

### Multiplayer / servers — its own full planning pass

Not scheduled; *"It will get a full planning pass too"* (2026-09-29). What it starts from:

- [ ] **Each player gets their own channels.** Today channels 0–15 are per world, so on a server a
      second player on channel 0 would spend your bank and fill your vault (found by the
      ideal-machines panel's gameplay judge, 2026-09-29). His word: *"give each player their own
      channels on servers"*. Deliberate sharing ("someone else's mine", M10-NOTES) is the pass's to
      design on top.
- [ ] **Machines work only inside the players' loaded boxes** (ideal-machines taken 9): *"it prevents
      players lagging out the server by placing a machine in every chunk of it."* Keep-loaded stays
      rejected.
- [ ] **A per-player machine cap is a revisit trigger, not built**: rows still tick unloaded for
      their row-local terms (about 0.02 ms per 500 dry rows, Release bench). It fires when the row
      tick alone passes 1 ms on a server, the same line as the machine cap above.

### M11 — v1.0

- [ ] Drills, pipes, conveyors — pipes as the deliberate cheap tier that quantum transport is the
      upgrade from, not the default way fluid moves.
- [ ] Crafting, health, hunger — health, damage and death came back here from M9 on 2026-09-13
      (a fall buries the body; the unstick frees it). Falling damage. Instant removal or
      progressive damage per tool.
      - [ ] **Recipes are the chemistry set** — litres in, litres out, with power and time, on
            M10's machine framework; the element table and the battery ladder are in
            [docs/MATERIALS-NOTES.md](docs/MATERIALS-NOTES.md).
      - [ ] **The periodic table as data underneath, never as blocks** — all 118 in
            `elements.json`; a material declares a composition in element mass-parts; recipes
            checked for element balance at load. Cost: the inventory leaves whole voxels for
            millilitres (a header version of its own at M11, after IM-6's v8 and M10g's).

### M12 — Modding tier 2

- [ ] MoonSharp host, sandboxed
- [ ] Event bus exposed: `BlockChanged`, `ChunkGenerated`, `PlayerTick`, `ToolUsed`,
      `RecipeCrafted`
- [ ] Mod loading order, dependency declaration (parsed and validated since M1a, resolved here),
      API versioning
- [ ] `res://mods` does not resolve inside an exported `.pck` — package the mods loose beside it
- [ ] Write a non-trivial example mod as the real test

### M13 — Polish

- [ ] Distance LOD (downsample distant chunks 2x/4x by majority vote) — also ADR-0001's second
      revisit trigger, where godot_voxel's advantage is largest
- [ ] Biomes
- [ ] Gas pockets, darkness as a hazard

## Before the repo goes public

Private at `github.com/MatthewCarven/MinerCraft`, default branch `main`.

- [ ] **Commit authorship** — every commit carries his gmail address; cheap to rewrite
      (`git filter-repo --mail-map`, one force-push) only while the repo is private and unshared.
- [ ] **The licence** (PolyForm Noncommercial 1.0.0) — revisit before merging the first outside PR
      (DCO or CLA then); at the first real art or audio assets (split code and content licences);
      and if distro packaging or a contributor community is ever wanted (MIT or Apache-2.0).
- [ ] Godot's MIT attribution — a credits screen from `Engine.GetLicenseText()` and
      `Engine.GetCopyrightInfo()`, due with the first distributed build.
- [ ] The name is protected by trademark and content, not by the engine's copyright.
