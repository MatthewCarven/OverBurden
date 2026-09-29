# Reader B: the plans, and what is already decided

Scope: M10-LADDER-PLAN, M10-NOTES, M10-PLAN, COLLAPSE-PLAN part f, DESIGN, TNT / MATERIALS /
WORLDGEN notes, MAP-NOTES, UNDO-PLAN, and the history files grepped for the brief's terms. Doc paths
are relative to `docs/` unless given. **BUILT** means the code has it (spot-checked where it
matters). **PLAN** means it exists only in a document. "(inference)" marks my own reasoning.

---

## 1. The ladder's one idea, verbatim, and what it rules out

> **"Connection is membership; movement is a ledger transfer at the 2 Hz boundary; the five tiers
> differ only in how a machine is assigned its pool.** A wire, a shaft or a pipe is evidence that two
> machine rows share a pool — never a thing that carries anything. There is no current, no pressure,
> no water in a pipe, no steam in a boiler, no item on a belt." (M10-LADDER-PLAN.md:47-51)

> "This is what makes 'our solver consumes CPU' false, because there is no solver." (:62)

Membership per tier (:54-60): hand = the player at the crank (no bound, "you are the wire"); steam =
face-adjacent shaft voxels, 16 cells, 20 kW; DC = a copper-wire component, 24 cells, 10 kW; AC = a
cable component, no bound, 200 kW; quantum = a channel number 0–15, no bound.

**Rejected alternatives in the ladder, verbatim where short.** Don't re-propose any of these:
- "**Rejected: a flow solve** (pressure, current, pipe volume)"; index work inside `onVoxelChanged`;
  "a global union-find rerun over every wired chunk at every edited boundary" (:269-275).
- The hose as "a FIFO with contents". It was Matthew's proposal and is priced but not built. Its
  cost: "a third home for litres outside Tier A and B, so `hosed` joins every identity line"
  (:298-305).
- Run bound as a voxel count; run bound as a path-distance BFS (:324-328).
- "only what is loaded"; a keep-loaded streamer policy; "group ids cached on rows" (:346-350).
- "v5 before the first recipe"; "**water in the inventory** (a bucket) — a third home for litres
  that a keypress could break the M8 oracle through; fluids are never carried" (:377-380).
- A world-owned recipe ID table; the recipe's string in the row (:392-394).
- "**a second item inventory**" (:402-403).
- A cranked workbench; a workbench that advances only while you stand at it (:430-433).
- A pump pouring into the column above its own top (:443-445).
- "**a tank on the row** — in-flight fluid by another name, a persisted number that is nothing's
  truth"; returning drunk water as rain (:454-456).
- Physical specific energy for the miner; a 26-minute top cell (:508-510).
- Worldgen first; renumbering the milestone table (:575-576).
- **Decision 22, "Not in the ladder"** (:550-556): shoring materials; the bucket; a keep-loaded
  policy; "**conveyors, which are state in flight by definition and ship as inserters —
  stock-to-stock transfers at the boundary — or not at all**"; random distribution; a machine cap
  (printed, trigger `machines` > 1 ms); wading and oxygen; brine; chromium; day and night.

---

## 2. The twenty-three decisions, with build status

All were taken as recommended on 2026-09-15 (*"As recommended, lets do M10a's planning pass"*,
:248-249). Sub-passes amended several. Statuses are as of 2026-09-29.

| # | Decided (short) | Status |
|---|---|---|
| 1 | Connection is membership. `ConductorTracker` only marks dirty in the hook; `ConductorIndex` segments; `NetworkSet` union-find; component id = min voxel, position-derived, never stored (:252-279) | **PLAN** (M10e). Only `via: channel` and `solar` are served |
| 2 | Hose = a presentation fill (an "energised" bit) plus a live leak at a cut; no FIFO (:281-305) | **PLAN** (M10e) |
| 3 | Run bound = popcount of occupied 1 m brush cells; pipes unbounded, head is the bound (Matthew's numbers) (:307-330) | **PLAN** |
| 4 | Membership survives unloading via persisted per-chunk summaries (schema 5); rows tick unloaded when inputs are row-local; any grid read stalls `unloaded` (:332-353) | Summaries **PLAN**. "Rows tick unloaded / grid read stalls" **BUILT** for channel machines (M10a-3) |
| 5 | Header and machine table in one transaction (:355-362) | **BUILT as `WorldBatch`** (M10a-1). See §4 |
| 6 | Inventory stays in voxels; recipes in mL; fraction = remainder on the station row; `unit` litres / count / cells (:364-382) | `unit` **BUILT** (a-2). Recipes and remainders **PLAN** (M10g) |
| 7 | Recipes a dictionary by `Identifier` in `recipes/*.json`, second loader pass; a job persisted by value (:384-394) | **PLAN** (M10g) |
| 8 | Parts are materials with `unit: count`, `placeable: false`; one registry (:396-407) | `placeable` **BUILT** (M10b-1). Filter and parts page **PLAN** (M10g, amended by the inventory screen) |
| 9 | Pool = Σ storage rows on a component or channel, never a stored number (Matthew's) (:409-413) | **BUILT** for channels (M10c-1) |
| 10 | Every float quantised at its one entry site (:415-420) | **BUILT** for solar and overcast. Crank **PLAN** |
| 11 | Hand crafting instant; time only for powered stations; crank drives pumps, never recipes; `E` held = crank, `E` tapped = craft; right-click with the computer opens ports **and stock** (:422-433) | Computer verb **BUILT** (M10b). `E` **PLAN** (the `TapOrHold` helper exists) |
| 12 | Pumps pour to `pipe_outlet` within head; `J/L = ceil(9.8·Δh/η)`; conductor placement into water deferred and displacing (:435-445) | Flat `joulesPerLitre` **BUILT** (relay 1,000). Head and `pipe_outlet` **PLAN** (M10f) |
| 13 | Boiler and engine are two machines; the boiler drinks and holds nothing; `consumed` term (:447-456) | **PLAN** (M10f) |
| 14 | Active set, `hasMachines` bit, `IsLoaded` first, 27-cell intake (:458-468) | Amended by M10a d7: every row ticks every boundary, active set is a *lever* (trigger `machines` > 1 ms). `IsLoaded` and `HeldIn` **BUILT** |
| 15 | Carried cells **dock below quantum**; equipped pack rides the header; tool pays per voxel; no pack → Q takes nothing (:470-485) | Header v4 pack **BUILT**; v5 "keep its charge" **BUILT** (2026-09-18); gate **BUILT**. **Dock NOT built** (M10f) |
| 16 | Miner J/voxel and cell sizes chosen together (Matthew's): now 50 J/voxel, 250 W recharge (:487-510) | **BUILT**, played twice |
| 17 | dc/ac rows stall `flooded`; conductors and pipes `supportDistance` 8; a machine body is a 64-voxel pillar (:512-520) | **PLAN** (M10f) except the pillar, which is true by the rule (M10d-1 test) |
| 18 | Brass with zinc, not bronze with tin (Matthew's) | Content **PLAN** |
| 19 | Stainless = iron + nickel + coal (Matthew's) | **PLAN** |
| 20 | Lithium a deep solid ore; brine later (Matthew's) | **PLAN** |
| 21 | Media are free class strings; engine `via` names reserved (`channel`, `crank`, `surround`, `burner`, `solar`, `adjacent:<id>`); converters ordered at load; bounds refused, not clamped: L/s ≤ 54,000, intake 1..2, ≤ 16 ports, **`stockVoxels` ≤ 65,536**, `maxRunCells` ≤ 2²⁰, head ≤ 1,024 (:537-548) | L/s and 16-port bounds **BUILT** (`MachineDefinition.cs:86`, `PortDefinition.cs:26`). `stockVoxels` bound **PLAN** (no such field in Core) |
| 22 | Not in the ladder (list in §1) | — |
| 23 | Sequencing (Matthew's): four groups, M10a–d quantum rung, M10e–f framework, M10g crafting, M10h–j world and ladder; **"M11 shrinks to drills, inserters and health"** (:558-573) | — |

### The parts M10a–M10j (split :759-772)

| Part | What | Status |
|---|---|---|
| M10a | Store slice (`WorldBatch`, schema 4, `MachineRowBlob`), entity, relay pump and outlet | **BUILT** a-1/a-2/a-3 (:1191, :1263, :1342) |
| M10b | Computer item, `SetPort`, weighted ring, `MachinePanel` | **BUILT** b-1/b-2 (:1641, :1718). **No stock tab** (decision 7) |
| M10c | Pool per channel, solar, vacuum cell, pack at header v4, gate | **BUILT** c-1/2/3 plus three amendments: pack keeps charge v5 (:2376); quantum tiers on T (:2411); fill takes the bite's rule (:2435) |
| M10d | The play plus instruments | d-1 **BUILT** (:2815); d-2 is Matthew's, **unplayed**; d-3 measurement **BUILT** (:2856-3048) |
| M10e | Conductor index, schema 5, `via: <class>`, `adjacent:`, `surround`, energised bit, cut leak | **PLAN**, next after the play (TODO.md) |
| M10f | Crank, burner, boiler drink + `consumed`, engine, shaft, **dock**, flywheel, `pipe_outlet`, `flooded` | **PLAN** |
| M10g | Recipes, stations, parts, **the inventory screen** (header v6 hotbar assignment) | **PLAN** (inventory-screen section :2458-2615) |
| M10h | Worldgen for the ladder | **PLAN** |
| M10i | The five tiers' JSON; solar re-classed to `dc` | **PLAN** |
| M10j | The ladder climbed once, timed | **PLAN** |

Other built things since: the vacuum cell's `enabled` port (2026-09-23, WORKLOG.md:1320-1340); the
quantum tunneller (WORKLOG.md:1285-1318); map pins for every machine (MAP-NOTES.md:277-282).

---

## 3. What the ladder says about each medium, crafting and copper

- **Hand** (:115-137): the crank (75 W × ms held, estimate); wooden and brass pumps (head 4 / 7 m).
  Crafting: `workbench` is a place, recipes instant from your inventory, whole voxels only;
  `charcoal_pit` and `forge` are timed burner stations.
- **Steam** (:139-162): the `boiler` burns coal and **drinks Tier B water, 0.5 L/s from the 3×3×3
  around it**; `steam_engine` via `adjacent:boiler`; `iron_shaft` class `shaft`, **16 cells, 20
  kW**; the flywheel (100 kJ) is the class's only storage.
- **DC** (:164-188): dynamo, solar; `lead_acid_bank` 100 MJ placed, `lead_acid_pack` 3.6 MJ carried,
  **recharged only docked at a `charger`**; `copper_wire` class `dc`, **24 cells**. The 24 is
  derived: 48 V, 16 mm², 100 A, 10% drop, 23 m out and back. 32 is the alternative, via `--dcrun=`.
  `wire_mill`: 1 copper voxel → 64 wire.
- **AC** (:190-215): `ac_cable` **`maxRunCells: null`**, 200 kW; the turbine "wants M11's
  inserters" (:193); **`printer`: one feedstock prints any tier-3/4 part.**
- **Quantum** (:217-241): `quantum_link`, `reactor`; `vacuum_cell` **placed 5 GJ, carried 500 MJ,
  "the carried cell recharges from its channel from anywhere."** The `fabricator` makes **the relay
  pair, the computer, the vacuum cell, the link** (:232-233). "What pushes you on: nothing,
  structurally" (:238).
- **The format** (:580-667). A machine is a material with a `machine` block. `kind` comes from the
  engine's set: `pump`, `outlet`, `generator`, `converter`, **`storage`**, `station`. Loader rules
  (:660-667) include: "a `channel` link requires a `channel` port and a **conductor link forbids
  one**"; "a conductor material cannot carry a `machine` block"; and "Labels come from identifiers,
  never from a field (M6d-3)".
- **The tick** (:671-695): one `Machines.Tick` between `RunPendingChunkFills()` and `CheckTable()`.
  Named lever: move it onto the fluid job's tail when `machines` exceeds 1 ms on the played world.
- **Persistence** (:697-712): schema 4 `machines` table, one row per origin with a **versioned kind
  blob** holding "charge, openness byte, **stock entries with a remainder per output**, the job by
  value". The blob's version byte lets later parts add state with no schema bump. Schema 5 is
  conductors. Header v4 = pack, v5 = carried packs, **v6 = the hotbar assignment**, and the mL move
  takes the one after.
- **Risks named** (:776-791): "Cable to rubble on a fall loses the copper — the right punishment or
  a rage-quit"; the salvage recipe is the lever. Three new Godot Controls: the machine panel, the
  inventory screen, a crank indicator.

**Containers, storage, grades, automation, conveyors, miners in the ladder.** "Storage" in the
ladder always means **energy storage** (`kind: storage`, the battery). **There is no item
container anywhere in the ladder.** Grades: none. Automation: drills and inserters are M11
(:570, :799-800). Conveyors: decision 22. The miner in the ladder is the entangled/quantum *tool*,
not a placed machine.

---

## 4. Decision 5, the single transaction: BUILT, not owed

The ladder's decision 5 (:355-362) said "No inventory-to-row transfer — a recipe queue, a stock tab,
a fuel fill — ships before it." M10a decision 1 (:879-901) amended it into a record. **Built in
M10a-1** (:1191-1234): `WorldBatch` carries chunks, columns, fluid, plus `Machines`, `Basins`,
`Header`, all in **one SQLite transaction** (`Overburden.Core/Persistence/WorldBatch.cs:30`,
`SessionSnapshot` :94, built by `Main.cs:4174`). The first test is
`AWriteThatFailsOnItsMachinesLeavesTheHeaderBasinsAndChunksUnchanged`. M10b fact 6 says so outright:
"a-1's `WorldBatch` … is built. The transaction exists; the thing to transfer into does not"
(:1504-1508).

**So the brief's constraint 4 is stale.** The 2026-09-15 "Four facts" paragraph (:86-91) is where the
stale sentence comes from.

---

## 5. M10-NOTES: the machine family (Matthew, 2026-09-02 onward)

- **The proposal, verbatim** (M10-NOTES.md:10-16), in §9.
- **The structural argument** (:24-57): teleport "deletes the single most expensive subsystem",
  which is pipes and fluid in transit. "**a machine layer is bounded by a placement count**" (:56),
  the one subsystem whose worst case is known in advance.
- Pieces 1–2: pair by a channel number (rejected: a stored link); round-robin plus weights, random
  only if seeded (:65-93).
- **Piece 3, backpressure** (:95-111): "move `min(take, accept)` and nothing else happens. No
  buffering, no queue, no in-flight state." **"Rejected: a buffer inside the machine.** That is
  in-flight fluid by another name." **"Rejected: spill at the output.** Water appearing on the floor
  because a tank was full is a second, invisible source of Tier B activity that nobody asked for."
- Piece 4b: **quantum reach is "a visual lock", not a radius** (Matthew, :134-167). Bound the cast
  explicitly; nothing goes in `ToolDefinition`.
- Piece 6: **a machine is the first entity**, the first player-authored state (:184-207).
- **Piece 7: the programmer is a format, not a screen** (:209-225). Ports are declared in JSON; the
  panel renders what it finds; labels are derived; bound at load **and** at use.
- Piece 8: naming, "**Matthew chose a mining-operation tone over a physics one**" (:227-252).
- Underwater and wading (:292-335) go on the **survival-mode trigger**.
- **"The quantum storage container (Matthew, 2026-09-25)"** (:339-373). His words are in §9. Notes
  recorded then:
  - "It is a machine with stock, which M10b left room for … Its litres are a term in both tiers'
    identities from the first line of code: stored, loaded in, dumped out."
  - "The pack is a part, and **grades are a second axis** … a 19th-century cell can feed a quantum
    emitter. Capacity from one part, hold time from another, and the dump size is whatever the
    container held."
  - "Going flat is a boundary event … Into Tier A on the analytic ground it is one `Emit` into a
    basin. Into Tier B … **10 ML is about 640,000 cells, the load test's scale**, so the dump pours
    what fits and owes the rest every boundary, as Ctrl+R's million already does."
  - "'Someone else's mine' is multiplayer … nothing to decide until there is a second player."
  - The raised watershed (POND-PLAN, **branch only, not main**) is what holds a dump in his valley.

---

## 6. M10-PLAN: the first plan, now the ladder's last rung

Overtaken the same evening. The ladder amends decisions 1, 3, 4, 5, 6, 8, 9, 10; 2, 7, 11 stand
(M10-PLAN.md:10-20). Still load-bearing:
- **D1** (:93-120): "`kind` is an engine behaviour picked by name … **a new *kind* is M12's
  scripting.** That is the honest boundary of non-negotiable 5 today." Rejected: a
  `Registry<MachineDefinition>` beside materials; storing the ID string in the row.
- **D2** (:122-139): placement is atomic; into water it is deferred and displacing (**BUILT**).
- **D3** (:141-158): **machines tick at the fluid boundary; DESIGN's 20 Hz is retired.** Revisit
  trigger: "**M11's drill carving a voxel every half second reads as stuttering** … a second rate
  beside the boundary, not a replacement for it." Rates are L/s × 500 ms, so a late boundary makes a
  slower machine, never a wrong one.
- **D4** (:160-184): the rows are the truth, reconciled at arrival; "a machine in a roof that comes
  down is destroyed, which is the antagonist doing its job".
- **D5** (:202-205): "**nothing is ever held by a machine**". The lever for an underground unloaded
  outlet is "a per-chunk fluid credit released at arrival, and it is not built until a play wants
  it" (:211-213).
- **D10** (:292-304): `pumped == emitted` per boundary on every tier, "**with no in-flight term to
  hide a litre in**"; the world's water total is a constant of the machine layer.
- **Deliberately not in M10** (:347-365): shoring as a material; **drills, conveyors, recipes,
  crafting (M11)**; the inventory's mL move; random distribution; a machine cap; wading, oxygen.

---

## 7. COLLAPSE-PLAN part f: the sensor (13 decisions taken 2026-09-29)

**The original decision 13** (COLLAPSE-PLAN.md:209-223). His idea, verbatim: *"if collapse detected,
cut power to the water containers via quantum energy sharing and deliberately flood the place"*. The
flood half "fights a locked decision: here water weakens rock (the keystone: at risk 20 → 18,718
under a lake)". **What it deferred**: "**its design goes with the 'ideal machines' chat**, because a
signal on a channel is new to the channel class". The split row f (:239) says "designed in the
'ideal machines' chat". Part f's pass (:1057-1061) found "The 'ideal machines' chat never happened",
so it kept "the signal as small as it can be: one boolean a channel, recomputed at each boundary,
never stored. That leaves the chat free to design a real signal medium later."

**Six facts** (:1065-1094):
1. Fuses live only in `CollapseScheduler`'s private timers (O(1) via `TryGetTimer`).
   `CollapseDirector.Events` exist only after a fuse burns out.
2. A channel is only an integer port. There is no channel class; `MachineTick.ChannelOf` clamps to
   0–15.
3. `enabled` is the player's saved setting, so a signal cannot write it.
4. The machine tick runs *before* this boundary's collapse sweep, so it is one boundary (0.5 s) late.
5. **"A new machine kind is C#, not JSON. `MachineKind` is a closed enum (pump, outlet, converter,
   storage), with a string switch in the loader, a switch in `Rebuild` and one in the panel's
   status. `via` accepts `channel` and `solar`, and `carries` accepts power and fluid."**
   Confirmed at `Overburden.Core/Machines/MachineDefinition.cs:13-23`.
6. There is no headless way to put a machine at a spot.

**The thirteen decisions, one line each** (:1098-1151):
1. It trips on a fuse *or* an event in the radius. Revisit: a `trip` port if fuses read as noise.
2. The radius is in chunks, 3D Chebyshev, 0–4, default 1: (2r+1)³ × 2 O(1) lookups (estimate).
3. **The signal rides the power `channel` port.** Revisit "if the ideal-machines chat wants signals
   routed independently of power".
4. Readers take `onsignal`: 0 ignore (default), 1 off while signalled, 2 on only while signalled. An
   override, never saved; it bumps the tick's own rebuild, not `MachineSet.Version`.
5. Readers now: the vacuum cell (leaves the pool; its charge stays in `PowerExact`) and the pump
   (new stall `signalled`). **The outlet is not a reader.** TNT charges are the third reader later.
6. **Kind `sensor` with `"senses": "collapse"`**, so later sensors (water level, power low) reuse the
   kind. No `links`.
7. The panel status: `QUIET` / `ARMED — fuse 12 s, 1 chunk away` / `FALLING — …` + `signalling
   channel N`.
8. **Not powered**: a sensor that needs power goes quiet when a collapse is likeliest. Revisit with
   M10f.
9. `P` seeds one, with its own texture layer.
10. **"Deliberately flooding stays out"**, because water feeds the collapse (the keystone).
11. The one-boundary lag is accepted.
12. Instruments: a `signal:` term on `M10:`, `signalled N` stalls, one-off change lines.
13. `--placeat=<id>@x,y,z[,port=value…]`, a debug door into no conserved quantity.

Split: **f-1** Core (the `sensor` kind, `onsignal`, the per-channel signal, `ICollapseSense`), **f-2**
the wire and the oracle, **f-3** his play (:1155-1159). What would reverse it (:1164-1167): "The
ideal-machines chat designing a general signal medium first. Then f-1's per-channel boolean becomes
that medium's first user, and decisions 3 and 6 move with it."

---

## 8. DESIGN.md: machine sections, milestones, rejected, and what is stale

- **Automation** (DESIGN.md:346-367): "Every machine registers through the same public registry
  path"; "The first consumer is a hand tool" (the battery ladder); core set: pumps; shoring;
  **drills — "scheduled sub-voxel carving"**; **pipes and conveyors — "fluid and item transport"**;
  power — "a simple generation / consumption / storage network".
- **Milestones** (:416-447): M10 "In progress … Machine framework, power, pumps, shoring, the
  inventory screen". **M11 "Drills, conveyors, crafting, health … v1.0"**. M12 MoonSharp. M13 LOD,
  biomes, polish.
- **Mod support** (:369-383): Tier 1 data mods; Tier 2 Lua at M12; "Event bus: typed events …
  `BlockChanged`…". **No event bus exists in the code** (grep finds nothing).
- **Scope** (:33): "Survival, no mobs". **End state** (:38): "None — the game never ends … nothing
  here is 'endgame', because there is no end; a tool is *earned*, not unlocked at the end". No
  creative mode is mentioned anywhere in DESIGN.
- **Rejected alternatives** (:454-474) touch nothing about machines. The machine rejections live in
  the plans (§1, §6).

**Stale in DESIGN.md** (the ladder listed these as owed at M10-LADDER-PLAN.md:796-800, and they
were never applied):
- **:366-367 "Machines tick on a fixed 20 Hz sim step in a spatially-partitioned update list"**:
  confirmed stale (M10-PLAN D3). Machines tick at the fluid boundary, 2 Hz *nominal*. It measured
  0.52–0.80 Hz on the load test (M10-LADDER-PLAN.md:2866, :3037-3040) and 2.00 Hz at his quarry
  after the Core-optimisation fix (TODO.md).
- :363 "Pipes and conveyors — fluid and item transport": the ladder says pipes are membership and
  conveyors are inserters or nothing.
- :364 "a simple generation / consumption / storage network": it is a pool per channel / component.
- :361 "Shoring / supports" and :444 "shoring": shoring is the pillar M9 already supports (M10-PLAN
  :349-354, ladder d22).
- :445 M11 "Drills, conveyors, crafting": crafting moved to M10g; the ladder shrinks M11 to "drills,
  inserters and health".
- :3-5 "Status: M0–M6 built and played, M7 planned. Last updated: 2026-09-01".
- :371 "Registry … materials, items, tools": items are materials (ladder d8).

**Stale elsewhere, found on the way**:
- TODO.md:170 (M11 "Drills, pipes, conveyors — pipes as the deliberate cheap tier…"): pipes are
  M10e/f/i now.
- MATERIALS-NOTES.md:27 ("M11 crafting") and :214 ("rides header v4 with the machines"): M10-PLAN
  :356-359 moved the mL bump to its own version, and it now comes after v6.
- WORLDGEN-NOTES.md:16 ("bronze is copper plus tin … M11"): ladder d18 is brass with zinc, and it
  says WORLDGEN's line "is amended", but it is not. WORLDGEN's three questions (:47-54) were answered
  in the ladder brief (M10-LADDER-PLAN.md:27-32).
- M10-NOTES.md:197 ("header v3 → v4" for the table): it is schema 4, and header v4 is the pack's.
- **M10-LADDER-PLAN.md:2644-2648 and :2729-2735 (M10d), plus MATTHEW-TODO.md:85** say pumps draw "in
  the order they were placed" and warn of "first-placed pumps hogging the channel". **The code sorts
  by origin *position*, X then Y then Z** (`MachineTick.cs:784`, `CompareOrigin` :856-859). So it is
  the lowest-X pump that wins, not the first placed.

---

## 9. Matthew's own words on machines, dated

Verbatim. These are his, not mine.

- **2026-09-02**, the family (M10-NOTES.md:10-16): *"A quantum fluid pump, and a quantum output
  which 'teleports' water 'wirelessly' from quantum fluid pump to quantum pump output — just the
  one, or if you use more than one, randomly or programmatically select which output to dump into.
  Eventually we will have a 'quantum machine programmer' which is essentially the config interface
  for the unit, letting you specify input numbers to output numbers for that respective machine it
  is used on. Lots of things depending — like if it's proven that the structural additions cannot
  be met due to CPU load etc, it is still going to work out great, plus I can invent cheap ways to
  approximate the same outcomes."*
- **2026-09-02**, reach (:136-138): *"The endgame-level tech will have a lot of very generous
  limits, so they can reach everything from the quantum teleport miner if realistic — if you can
  get a visual lock on it you can entangle with it."*
- **2026-09-02**: *"keep M8 clean, tune seepage low."* (:282)
- **2026-09-03** (DESIGN.md:38): *"ideally this game will never end, it's more artistic
  endeavor."* And (DESIGN.md:353-354): *"only once you have the latest tier battery will it be as
  effective as it is now."* Also (M10-PLAN.md:286): *"10/second eventually this will cost a lot of
  power"*.
- **2026-09-10** (M10-NOTES.md:295-298): *"… carry oxygen or a rebreather in later tech followed by
  quantum entangle->teleportation into your like lungs for that total water invulnerability like
  feel but at the expense of quantum vacuum and pressure based energy storage."*
- **2026-09-11** (:315-318): *"… wade through water until you can get enough iron and other tools
  given you have first constructed the wooden versions then the brass version the iron version then
  the steel version"*.
- **2026-09-15**, the ladder brief (M10-LADDER-PLAN.md:15-23): *"… the first tech we make will be
  the last tech in the game, like plan with me some kind of crafting system that we will use to
  create the machines from components we have made previously like this is the fun part … hand
  cranked -> steam (needs a boiler and mechanical output) -> electrical (… dc not a lot of travel
  (to prevent like really long runs etc because our solver consumes cpu) -> bespoke electrical ac
  (no distance limit higher currents, 3d printed stainless steel parts (via a printer) -> quantum
  systems and functions (like there is no distance limit, inputs and outputs need not be physically
  connected)"*. The hose (:283-284): *"one output hose connected to one or more pump inputs that
  would fill the hose like a FIFO queue"*. Pipes (:314-317): *"… fluid hose output lengths to be a
  fair bit more forgiving distance wise, so you can pump from low to high easily enough like you
  might need more to go deeper."*
- **2026-09-17**, first power play (WORKLOG.md:2686-2688): *"I placed a pump and a solar panel and
  an output and an input and it was pumping — like how do I use my inventory? I added a second solar
  panel and it started pumping more :-{D"*; then *"They have no upper bound?"* (a units misread).
- **2026-09-18** (WORKLOG.md:2635-2643): *"… for the sake of the game be a little generous
  please"*, then *"I might have appealed to your generosity too much … I am having trouble fighting
  solar recharge on the channel I am on"*. And *"keep its charge as an item please"* (:2608). And
  *"When do we do player inventory, do I have that in the plan anywhere?"* (:2668). From M6d-3
  (M10-LADDER-PLAN.md:2462-2464): *"… so slots stay put … I was going to allow you to carry more
  than what is on the hotbar maybe?"*
- **2026-09-19** (WORKLOG.md:2576-2596): *"Well the pumps seem to work well"*; *"We will have a
  sound effect for the quantum miner: failed — insufficient charge"*; *"If you 'load up the system'
  too much you end up flooding your mineshaft"*. And (:2384-2386) *"i've got quite a few pump
  outputs but the water is building up on top of them …"*, then *"the mounds are draining now"*.
  Keeping the flooded world *"to highlight what happens in infinite growth systems"*. On tiers
  (:2478-2480): *"i want quantum (small scale) and quantum (large scale) to be tiers like in
  hand/steel … the automine function and left click and right click to operate on the tier
  selected"*; *"holding q is supposed to enable automine(tm)"* (:2528).
- **2026-09-22** (MATTHEW-TODO.md, answered): *"Ctrl z feels good, disallow if something falls
  yes"*.
- **2026-09-23** (WORKLOG.md:1322-1323): *"can we add a power toggle to the quantum vacuum cell, will
  make debugging some of it easier"*. The tunneller (:1287-1290): *"a quantum tunneller that does
  about 2x2 meters to go between the quantum miner, and quantum excavator"*. Then 2026-09-26:
  *"I like the 2m cube"*.
- **2026-09-24**, TNT (TNT-NOTES.md:9-10): *"tnt just destroys outright voxels and they go nowhere no
  cleanup, but you could fire whatever stress function you use around the place a little more
  pessimistically"*.
- **2026-09-25**, the container (M10-NOTES.md:341-350): *"imagine loading a quantum storage
  container … it requires power to operate and is bigger on the inside than the out and requires a
  quantum vacuum storage 'pack' … this is a component that goes into the quantum storage container,
  and should you let it go flat, suddenly it has to find 10,000,000 litres of water to dump or more
  depending on configuration … think building with variable components, like low grade, medium
  grade, and high grade, mythical grade, and god grade, and your battery tech maybe 19th century but
  your actual emitter/entangler could be a higher tech variation … and the dropping that
  discharging container somewhere near someone else's mine"*; *"i am deliberately going to flood
  that entire valley for spread and flood testing purposes"*.
- **2026-09-26**, the sensor (COLLAPSE-PLAN.md:210-211): *"if collapse detected, cut power to the
  water containers via quantum energy sharing and deliberately flood the place"*. And *"buildings
  unless in earthquakes usually give signs before they collapse"*.
- **2026-09-27**, black collapse pieces (MATTHEW-TODO.md:180): *"indicitave that you aint getting
  that back"*.
- **2026-09-29**: the brief's three quotes (IDEAL-MACHINES-BRIEF.md:13-30), not repeated here.

---

## 10. TNT, materials, worldgen

- **TNT-NOTES** (not scheduled; six decisions still his, MATTHEW-TODO.md:123-124). A charge is a
  machine row of new kind `charge`; a detonator is kind **`trigger`** on a channel that "fires every
  charge on channel N" (:63-71). That is a second per-channel signal alongside the sensor's.
  **Decision 5** (:133-135): "*Recommend destroyed, never credited*, whether taken whole or in part.
  The risk of blasting beside your own pumps is the point." Costs (:150-159): the first kind whose
  tick *writes the world* with voxels (pumps only write water), ~7,200 `SetVoxel` a blast, ~11 ms
  Debug (estimate from M9c-1b's 1.5 µs a voxel). "*An observer sees every writer*: the undo's journal
  must treat a blast as the world's write". A blast clears the undo stack. "There is no empty hand in
  this game (a right-click always places what is selected)" (:67-68).
- **MATERIALS-NOTES**: "Do not implement chemistry … let machine recipes be the chemistry set"
  (:20-27). The cliff (:36-43): two solids reacting in the world is off it, so route it through a
  machine. "Depth is the ladder" (:117-130). **"Weight … no encumbrance, ruled twice"** (:283-284).
- **WORLDGEN-NOTES**: no ores exist yet. The shipped materials are air, clay, dirt, glass, grass,
  lamp, stone, rubble, plus the machines (`Overburden.Game/mods/overburden/materials/`). The ore
  ladder is coal → iron → copper → lead / nickel → bauxite → uranium (:15-16). Veins are a second
  lattice field (:36-41). All of it is M10h's pass.

---

## 11. Creative mode, containers, automation, grades: what the record already says

- **No game-mode concept exists.** TODO.md:160-161 lists what waits on one: "reach, the fly-mode gate
  on precision, a 3 m or 2 m reach, swimming, drowning, buoyancy, wading and oxygen, jump height".
  From 2026-08-30 (WORKLOG.md:12858-12863): "**Fly mode is a measurement instrument, not a creative
  mode** … gating a gameplay privilege on it would tie a game rule to a profiling flag". TODO-HISTORY
  (:1149-1151) says two waiting items is "when the shape of the thing starts being knowable, **and a
  third would make it overdue**". There are now about eight. Reach: *"generous but I'll allow"*,
  5 m, "right for *this* mode" (TODO-HISTORY.md:1141-1148). Machines enter play only through `P`,
  `Alt+P` (fills cells and the pack), `--stock=`, `--nogate`.
- **Containers were the named trigger for inventory capacity.** M6c-2: "Unbounded at M6 … Revisit
  trigger: **M10 containers or M11 crafting**, whichever gives capacity somewhere to go"
  (TODO-HISTORY.md:2506-2510; WORKLOG.md:10924-10928). The inventory screen, decision 5 (M10-LADDER
  :2571-2578), kept it unbounded: "**Revisit trigger: a survival mode**, or a play that reads the
  total as meaningless".
- **Automation**: drills at M11 (DESIGN:362, ladder :570). The trigger for a faster machine rate is
  the drill (M10-PLAN D3). Conveyors are inserters or nothing (ladder d22). The turbine "wants M11's
  inserters" (:193). Stations are the only thing with time "you walk away from" (d11).
- **Priority**: M10d decision 4 (M10-LADDER :2724-2735). Under short power "the pumps placed first
  run and the rest stall `unpowered`". The lever is "**the outlets' own ring applied to a channel's
  pumps — the smooth weighted round-robin already in `MachineTick`** … **not built until a play sees
  the starvation**, because a play may prefer the pumps nearest the water". The storm question is
  still unplayed (MATTHEW-TODO.md:82-85). M10b dropped the pump's `weight` port because "nothing can
  read" it (:1498-1502, :1554-1565). The pack already recharges *last* and "is never a supply"
  (M10c d4).
- **Map telemetry**: **machine pins are BUILT for every machine at any range** (MAP-NOTES.md:44-45,
  :103-106, :277-282: "your machines as pins in their own material's colour", "127 pin(s)", "an
  operations board"). Only *state* on the map is new.
- **Remote charge**: the carried pack recharges from its channel from anywhere **today, because the
  lead-acid pack ships on the channel class until M10i re-classes it to `dc` with a dock**, printed
  at load (M10c d4, :1963-1965; d10 :2051). **The dock does not exist.** The vacuum *pack* (500 MJ
  carried) does not ship; only the placed `vacuum_cell` does.
- **Grades**: only in M10-NOTES:358-362. No mechanism is planned anywhere.

---

## Traps

1. **Constraint 4 is stale.** The single-transaction overload is built (`WorldBatch`,
   `WorldBatch.cs:30`, M10a-1). An inventory ↔ container transfer is two in-memory writes
   snapshotted into one batch. What is missing is *stock* in a row's kind blob, plus its UI.
2. **`storage` is taken.** `MachineKind.Storage` is the battery (`MachineDefinition.cs:21-22`). An
   item container needs its own kind (C#, closed enum). Calling it "storage" collides with the
   vacuum cell and the pack everywhere: the loader, `Rebuild`, the panel, the `power:` line.
3. **A container that holds water re-opens five recorded rejections**: M10-NOTES piece 3's buffer
   *and* its "spill at the output" (M10-NOTES.md:107-110); ladder d6's bucket (:377-380); d13's "tank
   on the row" (:454-456); d22's bucket; and M10-PLAN D5/D10's "nothing is ever held by a machine …
   no in-flight term to hide a litre in". His 2026-09-25 words ask for it, so it can be done, but as
   an explicit amendment. Every one of those needs a new identity term, and the cross-tier
   `pumped == emitted` per boundary (a built invariant) breaks unless a `stored` term joins it.
4. **The dump owes litres across boundaries, and Ctrl+R's precedent is not persisted.**
   `_relayPendingMillilitres` is a session-only `Main` field (`Main.cs:722-729`); only delivered
   litres count. A vault's owed dump is the player's water and must be a persisted term (inference).
   Also: **a vault that loses power while its chunk is unloaded cannot dump**, because a grid write
   unloaded stalls (ladder d4). The only named mechanism is M10-PLAN's unbuilt "per-chunk fluid
   credit released at arrival" (M10-PLAN.md:211-213). A Tier A dump is allowed only if the body is
   on the seed surface inside the watershed window (M10a d8).
5. **Sensor + vault dump = his rejected flood.** Extra 1 cutting power to a vault's cell makes it
   dump, which is exactly *"cut power to the water containers … deliberately flood the place"*.
   Collapse f decision 10 kept that out because water feeds the collapse (the keystone). Say
   whether a signalled-off cell makes a vault dump, or holds it.
6. **"Bigger on the inside" breaks a bound already decided.** Ladder d21 bounds `stockVoxels` ≤
   65,536 (1,024 m³). His 10 ML is 640,000 voxels. The bound is **PLAN only**, so amend it rather
   than hit it. There are also ≤ 16 ports a machine (`PortDefinition.cs:26`, BUILT). The vacuum cell
   already uses 2 (`channel`, `enabled`), and every extra delivered as a port spends one.
7. **Recall collides with the collapse's matter accounting.** A fall cuts a body's voxels into
   pieces that **land as rubble** ("rubble credits nothing", COLLAPSE-PLAN.md:171-172; the identity
   is released == landed + in flight + lost). The drain destroys on `Lost > Carved` (ladder
   :940-947). The reconcile also destroys a *partial* body at arrival (:953-954). Crediting 64 body
   voxels *and* landing their rubble mints matter, so Recall must name its term and cover both arms.
   His *"indicitave that you aint getting that back"* (2026-09-27) is the spirit it bends. TNT d5
   (destroyed, never credited) is still his to take, and Recall must say whether a blast recalls.
   Undo: no conflict found, since a fall already clears the stack (UNDO-PLAN fact 4; COLLAPSE b
   d10).
8. **The extras' sub-quantum reach has no substrate.** Membership below quantum (M10e/f) is PLAN.
   Only `channel` and `solar` are served. By loader rule "a conductor link **forbids**" a `channel`
   port (M10-LADDER :662-663), so a sub-quantum machine has no channel for a remote panel or a signal
   to address. Component ids are position-derived and **never cached on rows** (d1, d4 rejected), so
   "AC for its own cable component" needs a design. The creative demo can only show quantum.
9. **The brief's extras table has two wrong "exists".** (a) Remote charge "DC and AC dock only
   (exists)": **the dock is not built** (M10f); today *every* pack recharges from anywhere because the
   lead-acid pack sits on the channel class until M10i. (b) Map telemetry: **pins already exist for
   every machine, un-gated** (MAP-NOTES:277-282). Gating "F12 shows machines" to quantum would remove
   a built feature; only state is new.
10. **The computer's reach and its click-on-nothing are taken.** The panel opens on a body under the
    crosshair at `EffectiveReachMetres`: 5 m, or the entangled reach when a quantum tier is on T
    (`PlaceController.cs:821-822`). A click on *nothing* already opens the equipped pack's panel
    (M10c d4, amending M10b d3). A "remote panel from anywhere" needs a new verb or a list UI. Also
    the ladder has the computer crafted at the **fabricator, tier 4** (:232), while d11 says it opens
    *any* machine's ports and stock. So below quantum there is no planned way to open a container's
    stock (inference). The panel is one row per port plus a status line, and there is no stock tab.
11. **Priority's baseline is misdescribed in the docs.** It is position order (X, Y, Z), not
    placement order (`MachineTick.cs:784, 856-859`). M10d d4 already names the lever (the weighted
    ring applied to pumps), gated on a play that sees starvation, and that play is unplayed. A pump
    `priority` port revives a pump port M10b removed for doing nothing.
12. **A creative mode is a first, not a switch.** There is no mode concept, and about eight items
    wait on one (TODO.md:160). Fly is explicitly *not* creative. DESIGN:38 says "nothing here is
    'endgame' … a tool is *earned*, not unlocked", so word the demo as a mode, not an endgame.
    Creating survival-by-contrast fires the inventory-capacity revisit trigger (inventory d5), which
    is what gives containers a reason to exist: **with an unbounded inventory and "no encumbrance,
    ruled twice", a container has no gameplay reason today** (inference).
13. **Conveyors: the brief's "membership" is not the ladder's word.** Ladder d22/d23 says conveyors
    "ship as inserters — stock-to-stock transfers at the boundary — or not at all", and M11 is
    "drills, inserters". A conveyor as a conductor class (a line of membership) is compatible in
    spirit, but it is a new reading. An auto-miner would be the first machine whose tick **carves
    voxels** (TNT-NOTES costs: ~1.5 µs a voxel through the hook chain; the undo journal must see it as
    the world's write). It also fires M10-PLAN D3's trigger for a second machine rate (a 2 Hz carve
    stutters).
14. **Inheritance in JSON has no precedent in the loader.** No `extends` or template exists in
    `Overburden.Core/Content/`. The loader's first cross-reference is the unbuilt recipe second pass
    (ladder d7). Labels are never authored (M6d-3). M10-PLAN D1 says a new *kind* is M12's scripting,
    and part f already spent one exception (`sensor`).
15. **Grades cannot be per-item state without breaking "a count is the truth".** The inventory is one
    voxel count per material ID. The only per-item annotation is `CarriedPacks` (header v5), which
    annotates the count and never exceeds it. A grade as instance data is a second item inventory
    (ladder d8 rejected). A grade per material ID costs permanent IDs (non-negotiable 2): 5 grades ×
    each part (inference). Installing a part *into* a placed machine (his "pack goes into the
    container") has no mechanism. Rows have ports and a kind blob, and no part slots.
16. **Dismantling or losing a stocked machine is undecided.** Precedent: a dismantled vacuum cell
    credits its body and **forfeits its charge**, a printed term (M10c d3). A container's contents on
    pick, fall and blast each need a rule and, for water, a term.
17. **The boundary is not 2 Hz under load**: 0.52–0.80 Hz on the load test (M10-LADDER :2866,
    :3037). A "per boundary" extra costs per boundary, but its *rate* in seconds is a lie when the
    boundary is late. A late pump is slower, never wrong (M10-PLAN D3).
18. **Naming.** Matthew picked "a mining-operation tone over a physics one" (M10-NOTES:231) for
    machine IDs, yet his own tool IDs are `quantum_miner` / `quantum_excavator` / tunneller. IDs are
    permanent the moment they ship, so propose names for his word.
