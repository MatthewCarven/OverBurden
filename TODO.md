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

## Next, on my side

- [ ] **The ideal machines — all sixty-one decisions taken as recommended (2026-09-29)**
      ([docs/IDEAL-MACHINES-PLAN.md](docs/IDEAL-MACHINES-PLAN.md), "The split"). IM-0b is built and IM-0
      measured (the quarry takes the 2 m bite, his word). **IM-1 is built** (the plan, "IM-1 — built";
      suite 2,076, twelve mutations red). **Owed from IM-1: the r6 control**, once his game is closed.
      **Next: IM-2** (the window, decisions 7–9 and 41; it switches `Charge` and `Priority` on in
      `MachineExtras.Served`). Read its decisions against `MachineTick`, `EquippedPack` and `CarriedPacks`
      first, and hold a short code-level pass if anything doesn't fit. Plays after IM-6, IM-8 and IM-9. **Owed from IM-0:** the windowed surface install (his
      say-so, it takes his focus); a bite under a lake (IM-7's bench); a dump underground (IM-5's).
- [ ] **The lake over a quarry — built 2026-09-27, his look owed.** The plane draws no sheet over
      a pit that is not yet full ([docs/M8-PLAN.md](docs/M8-PLAN.md), "built"). Open: his eye on the
      old TajMahal's quarry (now `retired-TajMahal-2026-09-29`), or on any pit beside a lake in his new
      world; the named lever if the rim reads badly is clipping the quad against the seed.
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
      under a placed house, headless on a scratch world. (e, the phonk, is built; his ear is owed.)
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
      at the next version (v6; a v5 world opens with its first nine holdings in ID order), assign
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
      lever is the outlets' ring on the pumps, not built until seen).
- [ ] **The fed-advance load test** (`start3` against `start3 --fedadvance=0`). Front advance
      slice 2c, the pro-rata advance for real streams, waits on it
      ([docs/FRONT-ADVANCE-PLAN.md](docs/FRONT-ADVANCE-PLAN.md), "Slice 2").
- [ ] **Glass**: a floor under a glass roof gleaming in a storm; water behind glass drawn in the
      wrong order. Triggers: either noticed in play.
- [ ] **TNT** — [docs/TNT-NOTES.md](docs/TNT-NOTES.md), six decisions. Needs from M9 before the
      shake: a rule per derive, and a fall from an explicit set.
- [ ] **A rock placed in a lake** (M9-PLAN decision 3's edge). If it reads wrong, the refusal is
      one read: `LakeCeiling.InLake` beside `HoldsWater` in `ChunkPipeline`.
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
- [ ] **`JobSchedulerTests.AThrowingJobFaultsWithoutTakingTheWorkerDownWithIt` has failed
      intermittently** — not a flake until the reason is found.

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

## Later — not scheduled

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
            millilitres (v5 at M11, alone).

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
