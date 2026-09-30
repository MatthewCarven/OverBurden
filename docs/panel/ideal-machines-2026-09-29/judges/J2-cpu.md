# Judge 2: CPU

The lens: what each design costs per boundary and per frame, and whether its arithmetic is right.
Every design's figures were recomputed. Code claims were checked against `main` at `71e6eed`.
Nothing was built or run.

## Ground figures (read first; every defect below cites one)

"Pre-fix" means before 2026-09-28. Until then Core was built unoptimised under `start.cmd`
(WORKLOG.md:433-476), and before 2026-09-15 the Debug JIT was unoptimised as well.

| # | Figure | Source, build |
|---|---|---|
| G1 | The boundary on the main thread is **20 ms** on a copy of TajMahal | WORKLOG.md:468, fixed build |
| G2 | Fluid tick: TajMahal, 1.2 M cells, 185 ms wall, 2.00 Hz. The load test's 0.52–0.80 Hz was measured 2026-09-24 over 0.74–1.0 M active cells (its **3.4 M was at open**) and has not been re-read | WORKLOG.md:463-467; M10-LADDER-PLAN.md:2885, :3037; TODO.md:38-42. Pre-fix |
| G3 | **In-game bite cost**, `swing ms` = raycast + carve on the sim thread: **1 m cube 0.42 ms mean / 4.3 max for 47 voxels, 1.7 remeshes; 2 m cut 1.3 / 7.1 ms for 379 voxels, 3.3 remeshes**; chunk bite 16.9 / 35.6 ms (0.70 µs a voxel) | WORKLOG.md:3460-3462; MiningController.cs:272-281. Debug, pre-fix |
| G4 | 1.5 µs a voxel is `--bitechunks=2`: 131,072 voxels in 197 ms. **Per-voxel cost falls about 12× from a cube to a chunk** (G3), so a 64-voxel bite should be priced from G3 | M9-PLAN.md:987. Debug, pre-fix |
| G5 | Each voxel written into a chunk with a fluid field clones a 4 KiB array. The seep hook runs per voxel up to 64 edits in a chunk, then **re-scans the whole chunk at 0.56–1 ms** Release. The 14.4 ms "chunk bite through the edit hook" came before that burst rule and no longer applies | FluidField.cs:315-319; M8-PLAN.md:1827-1834 (built M8c-2) |
| G6 | Worker time per call: mesh **7.63 ms**, light 0.83, support 1.68 (mean). Mesh install is atomic, 3.3–4.7 ms at worst. An edit remesh is urgent and jumps streaming | WORKLOG.md:951 (`--watch`, pre-fix; worldgen read 2.66 against Release 0.47), :13961-13965 |
| G7 | Tier B insertion: a 2 ML injection cost **198.7 ms once** on the main thread, about 1.5 µs a cell. `PourPending` walks (2r+1)³ over a ball sized to *everything owed*, every boundary until it fits | WATER-THREAD-PLAN.md:1046 (pre-fix); Main.cs:7587-7622, :7684 |
| G8 | **Main-thread terms that scale with a moving Tier B front**: the load test's sim thread paid ~110 ms a boundary (hand-off 36, capture 37, field sync 27); TajMahal's 8 ML pour in motion pays `hand-off: absorb` 32 ms mean / 65 max; the windowed surface install was **92 ms mean** on the load test's spreading front. Headless builds no surface, so no headless probe sees that last one | M10-LADDER-PLAN.md:2868-2869; M8-PLAN.md:2805; WORKLOG.md:1966-1968 |
| G9 | An outlet's mouth takes at most the 16 voxels of the layer above it: **≤ 250 L a boundary** | MachineTick.cs:1068-1085 |
| G10 | Landing solve 0.51–0.95 ms mean, 7.7–22 ms at worst, Release. The director spends at most one solve a frame once the frame's 2 ms is spent | COLLAPSE-PLAN.md:542-548, :309 |
| G11 | Release bench: 0.6 ms per 100 wet pumps, 0.02 ms per 500 dry (40 ns a dry row). `machines` read 1.5 / 8.7 ms on his 09-19 play and has **never been re-read on the fixed build** | M10-LADDER-PLAN.md:1399-1412; WORKLOG.md:2584. Pre-fix |
| G12 | Autosave is 17–100 ms, dominated by fluid panels at ~0.5 ms each. The machine table is rewritten whole every 30 s | Reader C §2.5 |

---

## 1. Scores

**Design 1, engine: 7.** It is the only design with a **global quarry budget** (`--quarrybites=4`),
and so the only one that keeps taken 9's promise that the loaded box bounds a server's CPU. Its
other bounds are also real:
- a scan cap of 64 cells a boundary;
- a 10 ML litre cap argued from the load test;
- one vault per channel, so at most 16 dumps run at once;
- contents that *are* the owed amount, so no pending counter needs persisting.

It names the bench arm it owes, which is the best discipline in the panel. It loses marks for four
things:
- its dump pours **1 ML a boundary into Tier B**, the largest per-boundary insertion in the panel;
- its CPU rates live in mod JSON with no ceiling;
- it routes the excavator into the vault, which makes every bankruptcy a full-size heap;
- it quotes two stale figures as measured, and never prices the heap per boundary or the Tier B
  front's main-thread cost.

**Design 2, gameplay: 3.** It has the worst CPU hazard in the panel:
- the underground dump is an **uncapped** relay ball;
- the god grade is 1 GL (64 M cells);
- the only bound offered is "price", which sandbox's Alt+P removes.

Its heap estimate (6 ms a boundary) exceeds its own revisit trigger (5 ms). Written as director
pieces, the heap is 64 solves a boundary against a budget of about 2 ms a frame. Its time-boxed
carve and its Tier A release spread over ~10 s are good ideas worth grafting.

**Design 3, format: 5.** It holds the best single CPU principle: *"CPU bounds are never JSON"*,
meaning pour and piece caps are engine constants. But it never gives the constants, so its dump is
unpriced. Its other weaknesses:
- it sets a 100 ML engine bound (6.4 M cells) with no measurement behind it;
- it has **no global quarry budget**;
- its demo's quarry arithmetic fails.

Its siphon is the cheapest in the panel (64 cells a click). Mining credits the pocket, which keeps
dumps small.

**Design 4, physics: 6.** It has the gentlest dump:
- a Torricelli vent, small per boundary and falling;
- solids at two pieces a boundary;
- a water-heavy hold bill that prices exactly what is expensive to dump.

It keeps mining in the pocket, and it keeps the ledger in the header, which keeps stock out of the
rows. Against it:
- **no global quarry budget**;
- an unbounded air skip in the quarry;
- the largest siphon bite (2,145 cells);
- a vent schedule that is self-consistent and wrong. The throat's rates cannot pass the `RoomAt`
  mouths the design routes them through (G9), so every vent time and the IM-3 oracle are out by
  8–80×. That error runs in the CPU's favour.

### Price sheet, recomputed

All figures are estimates unless a G-row is cited.

| | D1 engine | D2 gameplay | D3 format | D4 physics |
|---|---|---|---|---|
| Quarry bite | 1 m cube, 64 voxels | 2 m tunnel, 512 | 1 m cell | 1 m cell |
| Bites a boundary | **4 global** | 1–4 **per quarry** (time box in Q-2) | ≤ 4 per quarry | 1 per quarry |
| One bite, at 1.5 µs / by G3 | 0.10 / ~0.5 ms | 0.77 / 1.3 ms (7.1 max) | 0.10 / ~0.5 ms | 0.10 / ~0.5 ms |
| Quarries to cross 1 ms, at 1.5 µs / by G3 | never (capped) / budget 4 crosses at ~2 ms | 2 bites / 1 bite | 11 / ~3 | 11 / ~3 |
| Seep rescan (G5) | when 2+ cubes land in one chunk | **every bite** (512 > 64) | no | no |
| Worker time per bite (G6: remeshes × mesh + light + support) | ~17 ms | ~31 ms | ~17 ms | ~17 ms |
| Heap written a boundary | 1,024 voxels, direct: 1.5–6.7 ms | 4,096 as pieces: 6 ms of writes + 64 solves | pieces, cap unstated | 2 pieces: ~0.2 ms + 1–2 ms of solve |
| Tier B litres a boundary | **1 ML = 64,000 cells** (~100 ms by G7) | **uncapped ball** | engine cap, unstated | ≤ 1,958 cells by throat, ≤ 64 by mouth |
| Largest dump into Tier B | 10 ML = 640 k cells | 1 GL = 64 M cells | 100 ML = 6.4 M | 32.8 ML = 2.1 M |
| Siphon, cells a click / a boundary on Q | 512 / 1,024 | 512 / 1,024 | 64 / 128 | 2,145 / ~4,300 |
| One-pool draw a bite | O(cells on the channel) | O(1) against the last boundary's bank | O(1), refilled at the boundary | O(1), refilled at the boundary |
| Store credit (Carve credits **per voxel**, Carve.cs:765) | O(1) façade route | **O(vaults) per voxel** unless batched | pocket | pocket |
| Stock persisted in | row | row, plus owed | row, sectioned | header ledger |

---

## 2. Defects, per design

### Design 1 (engine)

1. **Must not ship: CPU rates are free mod JSON** (§2.3). `dumpVoxelsPerSecond`,
   `dumpLitresPerSecond` and `bitesPerSecond` are machine keys with no stated ceiling. A mod's
   `"dumpLitresPerSecond": 1e9` is a 500 ML ball a boundary.
   - Taken 9 is a server CPU bound, and a rate a mod can raise without limit defeats it.
   - Fix: D3's rule (the engine's constant), or a maximum refused at load (ladder decision 21's
     "refused, never clamped").
2. **Cost, severe: 1 ML a boundary into Tier B** (§5.3: *"Ctrl+R's 1 ML, a size he has played"*).
   That is 64,000 cells a boundary.
   - G7 prices it at ~100 ms on the main thread per boundary on the pre-fix build. A god-grade
     10 ML pays that for ten boundaries running.
   - The ball's r ≈ 25 voxels, so the walk is ~130,000 iterations each boundary while anything is
     owed.
   - He played Ctrl+R as one keypress, not ten in a row.
   - After that, G8's front terms run on the main thread for minutes.
   - The per-boundary cap should come from a re-take of `--injectlitres` on the fixed build. My
     estimate is ≤ ~100 kL (6,400 cells), and the cap should be global across vaults.
3. **Cost: the excavator is routed into the vault** (§4.2, the façade's `Add`).
   - A chunk bite is 512 m³, so 20 bites fill a god vault, and every bankruptcy becomes a
     640,000-voxel heap: 625 boundaries, 5.2 min.
   - At 1,024 voxels a boundary (16 cells), that costs 1.5 ms a boundary at 1.5 µs, or ~6.7 ms at
     G3's rate of 0.42 ms a cell. It also queues up to 16 urgent remeshes a boundary.
   - §5.3 never states the heap's milliseconds.
4. **Cost: the heap's site search is a one-off main-thread hitch.**
   - For 10 ML of blocks, R = ⌈∛10,000⌉ + 2 = 24: 49³ = 117,649 cells × 64 reads × 3.2 ns ≈
     24 ms, plus a sort at 81–320 ns a key (M10-LADDER-PLAN.md:2944). That is ~35–60 ms once.
   - At R 32 it is 274,625 cells, ~80–145 ms.
   - The design quotes only R 16 (36 K cells). Spread the search across boundaries.
5. **Cost: the quarry's main-thread price is understated.** The design says *"≤ 4 × 64 × 1.5 µs ≈
   0.4 ms … so it is pessimistic"*.
   - G3 prices the same `Carve.Cube` at 0.42 ms for 47 voxels on the same era's build, so four
     bites are ~1.7–2.2 ms. **The default budget crosses the trigger.**
   - Four cubes in one chunk make 256 edits, which triggers a seep rescan of 0.56–1 ms (G5).
   - In a chunk with a fluid field that is 256 × 4 KiB, 1 MiB of clones a boundary.
   - "Pessimistic" has no evidence behind it. Default the budget to 2 until the bench arm reads.
6. **Cost: the pipeline line rests on a figure not to quote.** It reads *"8 cube bites a second …
   about 2× a held Q (4.2 bites/s measured, TODO)"*, and TODO.md:112-114 says not to quote a
   profile's quantum rate until its denominator is found.
   - By G3 and G6: 1.7 chunks a cube × (7.6 + 0.8 + 1.7 ms) ≈ 17 ms of worker time a bite.
   - Eight a second is ~14% of one worker. All of it is urgent, ahead of streaming.
7. **Cost: the HUD's arithmetic.** `VAULT ch 0 — 1,204 of 2,500 m³ · 6,410 m³ water · hold 4.8
   kW` puts 7,614 m³ in a 2,500 m³ vault.
   - 4.8 kW = 1,204 / 2,500 × 10 kW, which counts blocks only.
   - That contradicts §5.2's *"one number covers blocks … and water"*.
   - If the bill ignores litres, the water, which is the costly thing to dump, holds for free.
8. **Cost: the dump has no bracket.** IM-5 says only *"the dump at Main's boundary"*. Give it its
   own term on the `boundary` line (the `collapse` precedent, WORKLOG.md:3662-3665), so that a heap
   is never read as `machines` or `pour`.

### Design 2 (gameplay)

1. **Must not ship: the underground dump is an uncapped ball** (§5.2: *"owed and offered every
   boundary until it fits, on the relay pour's template"*).
   - `PourPending` sizes the ball to everything owed (Main.cs:7684) and walks (2r+1)³ each
     boundary.
   - The high grade's 10 ML gives r ≈ 54 and 1.3 M iterations a boundary.
   - The god grade's 1,000,000 m³ (1 GL, 64 M cells, **19× the load test's at-open set**) gives
     r ≈ 248 and 1.2 × 10⁸ iterations.
   - *"Bounded by price, not by a cap"* is not a CPU bound:
     - sandbox keeps Alt+P (§9.2), which makes the joules free;
     - a pump pays 1,000 J/L whether it vaults or emits, so vaulting adds no price.
2. **Cost: the heap overshoots its own trigger.** It writes 4,096 voxels a boundary, *"about 6
   ms"*, against the revisit *"the heap measures over 5 ms"* (decision 9), so the trigger fires on
   the design's own estimate.
   - *"Rising out as pieces and tumbling"* means 64 one-metre pieces a boundary. At G10's
     0.5–0.9 ms a solve that is 32–58 ms of solving a boundary, against a budget of about 2 ms a
     frame.
   - Either the *"up to about 78 s"* becomes several minutes, or the budget is overridden and every
     frame pays.
3. **Cost: the quarry.** Each 2 m bite costs:
   - 1.3 ms mean / 7.1 max on the sim thread (G3);
   - a whole-chunk seep rescan (512 > 64 edits, G5);
   - 3.3 remeshes, about 31 ms of worker time.

   At rate 4 that is 5+ ms. Q-2's time box saves it, but its size is unstated, and *"the carve after
   the tick"* moves the cost out of the `machines` bracket without moving it off the boundary frame.
   That hides it from the trigger rather than paying it down. Give the carve its own bracket.
4. **Cost: no global dump budget.** *"A bankrupt channel has no sister that can hold"*, so every
   vault on the channel dumps in the same boundaries, and the number of vaults on a channel is
   unbounded.
5. **Cost: binding sends every credit into the vaults** *"in origin order"* (§4.2).
   - `Carve` credits per voxel (Carve.cs:765), so the fill walk is O(vaults) per voxel: 32,768
     walks an excavator bite, unless it is batched per bite.
   - Like D1's façade, it also makes every bankruptcy a full heap.
6. **Cost: the operations board.** It refreshes every row on the channel at 10 Hz (§3.3):
   O(rows) of `Label` writes on the main thread while it is open, unpriced. Refresh at the boundary,
   and only the rows that changed.
7. **Cost: the siphon is not said to queue** (§3.7). `Remove` refuses mid-tick
   (FluidSimulation.cs:449), so it must queue to the boundary. Queued, it lands 1,024 cells a
   boundary on Q (2 bites × 512) on the heaviest frame, about 1.5 ms by G7's rate. Price it there.

### Design 3 (format)

1. **Must not ship: no global quarry budget.** Each quarry has `voxelsPerSecond` ≤ 512 (≤ 4 cells a
   boundary), and nothing bounds the count except the loaded box (§7).
   - The loaded box bounds *where* quarries dig, not *how many*: r6h/3v tracks 3,179 chunks
     (SCALE.md:35).
   - *"Ten quarries cross the `machines` > 1 ms trigger"* becomes about three by G3, and the
     server bound his taken 9 names is gone.
2. **Cost, severe: the dump is unpriced and its bound unmeasured.**
   - The 100 ML engine bound (§5, decision 9) is 6.4 M cells. Against the load test that is 1.9×
     its at-open set or 6–9× its measured minute, measured on a build that no longer runs (G2).
   - *"One pour cap and one piece cap per boundary are C# constants, printed"*, but no numbers are
     given.
3. **Cost: the voxels dump as director pieces with no stated cap.** Each is a landing solve (G10),
   and a 100 ML vault of stone is 100,000 one-metre pieces.
4. **Arithmetic: the demo's quarry cannot dig.** Step 3 has *"a medium entangler, … an 8³ box"*.
   - The medium factor 0.01 × `reachCells` 32 = 0.32 cells, so the demo's quarry reaches nothing.
   - An 8-cell box needs a factor ≥ 0.25, which is mythical (16 cells) or god.
   - The vault line beside it (0.1 × 100 ML = 10 ML) is right.
5. **Cost: remesh is marked *"unpriced, and likely larger"*** (§7). That is honest, but G3 and G6
   were there to price it: ~17 ms of worker time a cell step.
6. **Cost, small: the support cost table.** 16 new IDs take its material axis from 14 to 30: +8
   KiB and about twice the build per wet derive (SupportCostTable.cs:120-151). The lever is named.

### Design 4 (physics)

1. **Must not ship: no global quarry budget** (`cellsPerBoundary` per quarry, §7). This is D3's
   defect 1, with the same arithmetic.
2. **Arithmetic, self-consistent and wrong: the vent cannot pass its own throat.**
   - The Torricelli table is right to the digit. I rederived it: god full 61.1 m³/s, 1,958 cells
     in the first boundary; his 10 ML 33.8 m³/s and 592 s; low 1.0 s, medium 5.9 s, high 34 s,
     mythical 3.2 min, god 17.9 min.
   - But §5 routes the water through *"the outlet's own `RoomAt` rules at each face's mouth"*, and
     a mouth takes the 16 voxels of one layer, ≤ 250 L a boundary (G9). Four faces take ≤ 1 m³ a
     boundary.
   - So 10 ML needs ≥ 10,000 boundaries (≥ 83 min), not 9.9 min, and longer once the front stops
     clearing the mouths.
   - IM-3's *"10 ML in a god vault drains in 592 s ± one boundary"* would fail as written.
   - The CPU gains from this. But if the fix is a ball at each mouth, G7's cost comes back.
3. **Cost: the air skip is unbounded.** *"It skips … air, at no toll"* (§7), with no scan cap.
   - Over a box already dug, the search for the next solid cell can walk the whole box: 32³ cells ×
     64 reads × 3.2 ns ≈ 6.7 ms a boundary per quarry.
   - Take D1's 64-cell cap.
4. **Cost: the siphon's bite is large.** A 2 m-radius sphere is 2,145 cells a click (§4), 4.2× a
   2 m cube. The cooldown is unstated; at 0.25 s on Q it is ~4,300 removals and wakes a boundary,
   ~6 ms by G7. Take a 2 m cube, or D3's 1 m.
5. **Cost: the solids vent is unpriced.** It sends two director pieces a boundary for as long as
   2.3 h (god stone): ~1–2 ms of solving a boundary (G10). The only price the design gives is
   *"one integer square root per failing vault"*.
6. **Cost: every vault vents at once.** Bankruptcy vents *"every vault at once"* (§9 5:00), and
   vaults per channel are unbounded. The throat limits each one; a global budget should limit them
   all.

---

## 3. Where the designs disagree, and the CPU lens decides

- **Does mining by hand credit the pocket or the vault? The pocket (D3, D4).** A dump costs in
  proportion to what the vault holds, and the excavator is the game's largest door: 32,768 voxels a
  bite. Routing it into the vault (D1's façade, D2's binding) makes the worst-case heap routine.
  With the pocket taking the hand, a vault fills only at the quarry's budgeted rate and through the
  water doors.
- **Is the hold bill flat or by fill? By fill (D1, D3, D4), and D4's water-heavy toll is best.**
  Both are O(1). But a bill that follows contents, and charges water 156× what it charges rock,
  prices the one thing that is expensive to release: litres bound for Tier B.
- **Where do vented litres go? Tier A wherever the vault is on the analytic ground.** That costs
  one `Emit` twin per boundary at any size, and D2's ~10 s release spreads the level change (still
  O(1) a boundary). Tier B only underground, and capped.
- **How many litres may a vault hold? D1's 10 ML over D3's 100 ML and D2's 1 GL**, until the fixed
  build's load-test re-read says otherwise. The cap matters only for litres that could reach Tier
  B.
- **How fast does a dump run? D4's shape under a global engine constant.** Release small amounts
  per boundary, falling with the store, capped across all vaults, with the cap set by a measured
  `--injectlitres` on the fixed build. D1's 1 ML is 10–60× too much for one boundary.
- **Is the heap direct writes or director pieces? Direct writes, bottom-up (D1).** A piece costs a
  landing solve (G10) and takes the director's frame budget. That is the same budget a real
  collapse needs, and a dump into the pit makes a real collapse likely.
- **Is the quarry budget global or per quarry? Global (D1).** It is the only arm that bounds work
  by something other than the count of quarries a player places in his box. His taken 9 is a
  server CPU bound, so the budget is what makes it true.
- **Is the bite 1 m or 2 m?** Per voxel the 2 m bite is ~2.6× cheaper and needs ~4× fewer remeshes
  (G3). But every 2 m bite pays a whole-chunk seep rescan (G5) and is one atomic 1.3–7.1 ms
  on the boundary.

  Under a global budget of ≤ 256 voxels a boundary, the 1 m cube fits. Take the 2 m bite only if a
  measured budget allows ≥ 512 voxels.
- **Where does the bite land in the frame? Not on the boundary frame.** Pay at the boundary and
  carve on the following frame, in the quarry's own bracket.

  This is D3's stutter lever seen from the CPU side, with one caution. Per-swing remeshes rise with
  the number of bites, not the volume (WORKLOG.md:9383-9385). So spread the work by chunk across
  frames, never by voxel.
- **Does a 2 m bite every half second fire M10-PLAN D3's stutter trigger? Yes, by construction**,
  and so does a 1 m cube. The trigger is the eye watching a 2 Hz carve (M10-PLAN.md:155-158). The
  CPU makes the beat worse:
  - the boundary runs late under load (G2), so the gaps are irregular, 0.5–2 s;
  - a remesh lands frames after its edit (WORKLOG.md:647).
- **Can a signal dump a vault? Never (all four agree).** A signal flips a whole channel in one
  boundary, so a signal-driven dump is N dumps at once.
- **Strict loader or not? Strict.** It costs nothing at load (under a millisecond for ~40 files, in
  every design), and a misspelt ceiling key that loads silently is a ceiling that isn't there.
- **Template world or kit? The kit (D1, D3, D4)**, or a template saved with no Tier B water. A
  template ships a world whose water all wakes at open, and the open's first boundary is already the
  worst frame (Reader A §4.5).
- **These forks are neutral on CPU:** grades on the vault ID or on parts (5 IDs against 10; both
  are noise, provided no grade raises an engine ceiling); one carried item or two; recall at release
  or at the drain (both O(body voxels) per fall).

**What grows with machines placed rather than with players present (taken 9's residue):**
- **The row tick:** 40 ns a dry row (G11), so ~25,000 rows reach 1 ms. That is the per-player cap's
  trigger, and far off.
- **Vault bills:** O(vaults). D1 bounds active vaults at 16.
- **Owed dumps:** O(1) a vault until its chunk arrives. Then the arrival frame pays, which is one
  more reason the dump budget must be global.
- **Sensors, the one that costs real time.** f-1's (2r+1)³ × 2 lookups run every boundary whether
  the sensor is loaded or not: 54 at r 1, **1,458 at r 4**. At an estimated 20–50 ns a lookup,
  14–34 sensors at r 4 cross 1 ms. **No design notices.** A cheap guard that does not contradict
  f-1: a sensor whose own chunk is unloaded reads false without looking.
- **Rebuild:** O(rows log rows) on every port write and signal edge. The priority sort rides inside
  it, so priority is free per boundary in all four designs.
- **Autosave:** the machine table is rewritten whole every 30 s. Stock at ~12 B a material is under
  0.5 KB a vault, which is noise against G12's 0.5 ms a wet panel. D4's header ledger keeps even
  that out of the rows. **What a dump adds to autosave is Tier B panels, not vault bytes.**
- **Telemetry:** O(rows) per map rebuild, per player viewing. The rebuild itself is ~3.5 ms
  (MapPanel.cs:40-44). D2's board adds O(rows) at 10 Hz per player viewing.

---

## 4. The winner through the CPU lens, and the grafts

**Design 1.** It is the only design whose work is bounded by engine rules rather than by the
player's restraint:
- a global quarry budget;
- a scan cap;
- a litre cap;
- at most 16 concurrent dumps;
- no persisted pending state.

Its defects are rates and prices to correct, not structure to rebuild.

**Grafts:**
1. **From D3: CPU ceilings are engine constants, never mod JSON** (D3 §5, *"CPU bounds are never
   JSON"*). This fixes D1's defect 1. Where a JSON rate is kept for flavour, it is bounded by an
   engine maximum and refused above it.
2. **From D3 and D4: hand mining credits the pocket.** The vault takes the quarry's spoil, pumped
   water and the siphon. This shrinks the worst heap from excavator-seconds to quarry-hours.
3. **From D4: the vent's shape.** A per-boundary release that is small and falls as the store
   empties, under one global cap across all vaults. Take the shape, not its numbers (D4 defect 2).
4. **From D4: the by-fill bill with water dearer than rock**, so that holding Tier B-scale water is
   the expensive choice.
5. **From D2: the Tier A dump over ~10 s** for vaults on the analytic ground (O(1) a boundary).
   Also D2's time-boxed carve, but in its own `quarry` bracket and on the frame after the boundary.
6. **From D3: the siphon's 1 m take** (64 cells, 1,000 L a click). It is the smallest Tier B touch
   in the panel.
7. **From D2 and D4: the `flooded` stall.** One `HeldIn` a boundary (10 ns, measured) is the
   cheapest way to stop a quarry.
8. **Mine:**
   - a global dump budget, in voxels and in cells, across every vault;
   - D1's default quarry budget lowered to 2 until the bench reads;
   - the unloaded-sensor guard;
   - the heap's site search spread across boundaries.

**Owed before any number in the synthesis is believed**, in this order:
1. **Re-read `machines` and `boundary worst` on a copy of TajMahal on the fixed build**, with no
   new code. That gives the headroom under the 1 ms trigger. The last reading (1.5 ms) is pre-fix.
2. **Re-take `--profile --entangle --holdq` and `--entangletunnel --holdq` on the fixed build**, dry
   and with `--fillbasin` so that a fluid field exists. `swing ms` and `remesh per swing` give the
   quarry's bite to first order, with no new code.
3. **`--injectlitres=1000000` and `=10000000` on the fixed build.** Read `pour` on the boundary
   line, then a minute of tick wall. In a windowed run, read `fluidsurf` install. These set the dump
   cap and the litre cap.
4. **A Release `--quarry` bench arm** (D1 names it) at 1, 4 and 16 quarries through the game's own
   hook chain. Report per-bite ms, seep rescans, field clones and derives queued.
5. **Per design:**
   - D1: a heap-writer arm, voxels a boundary against ms, plus the site search at R 24 and R 32;
   - D2: `--pieces` with a 64-pieces-a-boundary heap fixture;
   - D3: its constants, chosen by step 3, and a 100 ML dump before that bound stands;
   - D4: a vent fixture that counts the litres four mouths actually accept, and the 2 m-sphere
     siphon on Q.
6. **Sensors × radius** against `machines`.

---

## 5. Estimates stated as facts

- **All four: "1.5 µs a voxel through the hook chain."**
  - It is a Debug, pre-fix figure from a 131,072-voxel bite.
  - Small bites cost more per voxel, not less: the same era's in-game cube is ~8.9 µs a voxel
    removed (G3), and a ten-voxel fall with water on cost 3–13 ms Debug (WORKLOG.md:3656-3658).
  - D1 calls it *"pessimistic"*, which is unsupported. It should say: *"a Debug-era per-voxel
    figure from chunk-scale bites; the in-game cube read 0.42 ms a bite on the same build; the
    fixed-build figure is owed."*
- **D1: "A chunk bite a boundary is 14.4 ms Release through the edit hook (measured)."** That was
  the seep tracker's per-voxel hook, replaced the same day by M8c-2's 64-edit burst and chunk rescan
  (M8-PLAN.md:1832-1834). It measures a build that no longer runs.
- **D1: "4.2 bites/s measured, TODO"**, and the "2× a held Q" built on it. TODO.md:112-114 says not
  to quote this profile rate until its denominator is found.
- **D1, and the brief itself: "The load test ran 3.4 M active cells at 0.52–0.80 Hz."**
  - The 3.4 M was at open; the rate was taken over 0.74–1.0 M active cells (M10-LADDER-PLAN.md:2885)
    on the pre-fix build, and has not been re-read (TODO.md:38-42).
  - The designs compare a dump against three different denominators: D1 says 100 ML is *"twice"*
    the load test, D3 says *"about ten times"*, D4 says 2.1 M is *"60%"*. Each should name which one
    it uses.
- **D1: "10 ML (640,000 cells) is the CPU-honest top."** This is asserted, not measured. It should
  say: *"an estimate pending step 3."*
- **D2: "Chunk-sized quarrying … about 49 ms a boundary (estimate)."** A measurement exists and
  reads 16.9 ms mean / 35.6 max (Debug, MiningController.cs:272). The estimate is 3× the record.
- **D2: "One quarry at rate 1 crosses the `machines` > 1 ms trigger alone."** By the design's own
  0.77 ms it does not. By G3's 1.3 ms it does. The design should cite G3.
- **D3: "Ten quarries cross."** By G3 it is about three. The figure is an estimate on the 1.5 µs
  basis, and should say so beside the in-game one.
- **D4: the vent times, labelled [derived].** They are derived correctly from a throat the stated
  mouths cannot carry (G9). They should say: *"the throat's rate; the delivered rate is bounded by
  four mouths at ≤ 250 L a boundary each."*
- **D4: "A support derive is … 4.7–7.5 ms Debug [measured]."** It is pre-fix, and TODO.md:106-109
  says to re-take it before quoting. The design also omits the mesh (7.6 ms, G6), which is the
  larger worker cost.
- **D4: "the map's rebuild (3.5 ms [measured])."** This is slice a, 2026-09-23, with Core
  unoptimised. It is still a fine upper bound.
- **D2: "Tier B's load is bounded by price."** This is a design claim presented as a bound. Sandbox
  Alt+P and pump-into-vault parity both void it.
- **Every design: "`machines` > 1 ms" as the yardstick.** No one knows the bracket's current value
  on the fixed build (G11), so every "crosses at N quarries" figure is relative to an unknown
  baseline. Step 1 is owed first.
