# M10 — The ladder: hand → steam → DC → AC → quantum, and the crafting under it

> Planned 2026-09-15, evening, before any code, from Matthew's brief (below) and five things he
> added while the pass ran. **Read [M10-PLAN.md](M10-PLAN.md) first** — its M10a–M10d are the
> ladder's *last* rung and are still built first — then [M10-NOTES.md](M10-NOTES.md),
> [MATERIALS-NOTES.md](MATERIALS-NOTES.md) and [WORLDGEN-NOTES.md](WORLDGEN-NOTES.md). Every number
> here is derived from a constant in the code, from a physical fact, or labelled an estimate; the
> first measurement is `--pump`. **Taken as recommended, all twenty-three, the same night; M10a's
> planning pass is the last section.** The pass was run as four independent designs (engine cost first,
> gameplay first, mod format first, physical plausibility first), three adversarial judges and a
> completeness critic; what survived is here, and what they killed is listed under each decision.

## The brief

> *"m10 is going to be a lot longer than this but we will split it into pieces like we always do,
> OK so the first tech we make will be the last tech in the game, like plan with me some kind of
> crafting system that we will use to create the machines from components we have made previously
> like this is the fun part like you can drag this out with the first level tech being like hand
> cranked -> steam (needs a boiler and mechanical output) -> electrical (needs wires/solarpanels/
> boilers with generator outputs dc not a lot of travel (to prevent like really long runs etc
> because our solver consumes cpu) -> bespoke electrical ac (no distance limit higher currents, 3d
> printed stainless steel parts (via a printer) -> quantum systems and functions (like there is no
> distance limit, inputs and outputs need not be physically connected)"*

And, while the pass ran, five more things — **taken as decided, not returned as questions**:

- **Iron is under the water table; the first age is stone and wood.** WORLDGEN-NOTES' first and
  third questions, answered: seepage is the tax on ore, and wooden tools exist.
- **Trees are found and planted; fire drops seeds from foliage, cheaply; growth runs like the
  water does** — a detached job on a worker, composed at a boundary, collected after the tick.
  Growth is a pure function of (planting time, world time), the M7 trick, so an hour away
  integrates in one call.
- **The quantum channel is 0–15.** Several outputs on a channel cycle; several inputs all fire in
  the same tick. The cursor is per channel, not per pump, so two pumps on one channel go to two
  outlets in the same boundary rather than both to the first.
- **The channel is set by right-clicking the machine with a computer.** The programmer is an item
  in the hand, not a key.
- **Sub-quantum machines have faces** — an input face and an output face — and a pump's output
  connects to a hose that one or more pumps can feed. (He proposed the hose as a FIFO queue that
  fills; that one is returned as decision 2, because the pass found a cheaper way to give the same
  two things.)

---

## The one idea, and why it is the CPU answer

**Connection is membership; movement is a ledger transfer at the 2 Hz boundary; the five tiers
differ only in how a machine is assigned its pool.** A wire, a shaft or a pipe is evidence that two
machine rows share a pool — never a thing that carries anything. There is no current, no pressure,
no water in a pipe, no steam in a boiler, no item on a belt. Every tier moves litres and joules
with the relay pump's three lines from M10-PLAN decision 5 — read the room, take the minimum,
credit — so `pumped == emitted` holds at every tier and the M8 oracle is never touched.

| Tier | The membership rule | Bound |
|---|---|---|
| Hand | the player at the crank | none — you are the wire |
| Steam | face-adjacent shaft voxels of one class | 16 cells, 20 kW |
| DC | a copper-wire component | 24 cells, 10 kW |
| AC | a cable component | none, 200 kW |
| Quantum | a channel number, 0–15 | none |

**This is what makes "our solver consumes CPU" false, because there is no solver.** *Who is
connected to whom* is answered at edit time — a wire placed or mined marks its chunk dirty, and at
the next boundary that chunk's conductor runs are re-summarised and only the component touched is
relabelled — and the tick is O(awake machines) at every tier, never O(wire). A physical tier's
per-boundary cost equals the quantum tier's; the difference is paid once per conductor edit,
capped per boundary. The DC tier's short run and the AC tier's unlimited one are the same code
with one number different, and the number is a cell count, not a search.

**The fiction it buys and the fiction it forgoes.** No voltage drop, no pipe volume, no burst, no
current: a network in bound delivers in full and a network over bound delivers nothing, printed.
A cut network stops at the next boundary. That is a caricature, and it is the same caricature Tier
A already is for lakes — *solved, not simulated*, sixth outing.

---

## Four facts from the code that shaped it

**The vertical box is 16 m and the played mine is 64 m** (`ViewDistanceVertical` 2). A surface
turbine and a pump at the face are never loaded together, so *"no distance limit"* at the AC tier
is only true if membership survives a chunk unloading. Decision 4: per-chunk conductor summaries
persist as the chunk's own derived data, the union-find spans loaded and summarised chunks, and an
unloaded voxel cannot change so the summary cannot be stale — provided the forget path rescans a
dirty chunk before writing it.

**The header and the machine table are two store calls.** `IWorldStore.WriteWorldHeader` is
separate from `Write(chunks, columns, fluid)`, so a recipe start that moves voxels from the
player's inventory (the header) into a station's stock (a row) is two writes, and a crash between
them duplicates or deletes authored state. Three of the four designs asserted atomicity the API
does not have. Decision 5: one overload, one transaction, the store slice's first test, and no
inventory-to-row transfer ships before it.

**`FluidGrid.IsWetIn` is a dictionary lookup per voxel** (~25 ns, estimate), so a pump's 1,728-cell
intake read the obvious way is ~45 µs, and 500 awake pumps are ~22 ms Release / 70–200 ms Debug on
the boundary frame that is already the played build's worst (63.7 ms at slice 3c). Decision 14: the
read is 27 cell lookups then bit reads off the panel bitsets, `Publish` caches a per-panel wet
count so a dry pump costs eight lookups and sleeps, and a machine active set with a per-chunk
`hasMachines` bit is the design rather than a lever.

**A wire on a ceiling falls.** `SupportRule.Cost` for `supportDistance` 2 is 60 a step and
`HangCost` 120, so a 0.25 m wire run more than two voxels from a wall is at risk under M9's rule
and comes down in the collapse window. Decision 17: conductors and pipes declare `supportDistance`
8 (a cable stapled to rock), so a run along a roof holds while the roof holds and falls with it —
which is the tier-3 moment, not a defect. And `Place.Put` refuses `Wet` (M9d), so a hose cannot be
laid through the flooded room the hand tier is about; decision 12 puts conductor placement into
water on decision 2's deferred, displacing path with the machine bodies.

---

## The five tiers

Numbers are estimates unless a source is named; every one is a JSON field with no rebuild.
Duration targets are for feel and are unplayed.

### Tier 0 — Hand: wood, then brass. *"The first stage."*

- **Power**: the player. A machine with a `crank` link runs only in boundaries during which a
  body within reach held `E` on it; `Main` accumulates milliseconds held per frame and the boundary
  credits `75 W × millis` millijoules, exactly (75 W is sustained human output — physiology,
  estimate). Nothing stores it; walk away and it stops.
- **Fluid**: `wooden_pipe` (class `pipe`, no run bound — the head is the bound, decision 3) to a
  `pipe_outlet`. `wooden_pump`: head 4 m
  (a bucket-and-rope lift), efficiency 0.5, cap 1 L/s. The litre costs `ceil(9.8 × Δh ÷ η)` J
  (physics, integer): 79 J at 4 m, so 75 W buys **0.95 L/s** — less than a 10 × 10 × 3 m stone
  room's 1.76 L/s seep under 5 m (M8c bench), **so you wade**, which is the tier he asked for.
  `brass_pump` (copper + zinc, forge): head 7 m — the practical suction lift, physical — η 0.6,
  cap 3 L/s: 0.66 L/s at 7 m, 3 L/s at 2 m. Beats stone at a short lift; loses to dirt's 30.8 L/s
  (1.76 × 17.5) everywhere. Lift in short stages or lose.
- **Crafting**: a `workbench` is a *place* — `E` on it opens the craft panel, recipes are instant
  from your own inventory, whole voxels only. `charcoal_pit` and `forge` are burner stations, timed
  at the boundary: wood → charcoal; copper + zinc → brass; iron ore → iron.
- **Needs from worldgen**: logs at the surface (the feature stage's first customer, through the
  relight path per M5c-1), shallow copper and zinc above the water table, a clay band. None exists
  today; until the worldgen pass lands, this tier is `--stock=`-seeded like the relay.
- **What pushes you on**: you are the engine, so the room drains only while you stand there and
  floods while you dig; every 4–7 m of lift is another pump on another ledge under a roof M9
  tints. The way out is a machine that runs when you are not there. Target 30–60 min.

### Tier 1 — Steam: iron and coal. *"The boiler drinks the sump."*

- **Power**: `boiler` (burner: coal from its own stock at 3 MJ a voxel — a game number, ~1/8 of
  real, so a seam is finite) **drinks Tier B water at the boundary from the 3 × 3 × 3 cells around
  it, 0.5 L/s at rated load** (real is ~0.13; ×4 so the sump visibly drops). The litres leave the
  world as `consumed` — a `Watershed.Outlet`-shaped term on both identities. It holds no tank.
  `steam_engine` beside it (`via: adjacent:boiler`) is the shaft source, 10 kW (between Newcomen's
  5 hp and Watt's 20). No fuel, no water or no consumer this boundary → nothing; a `flywheel` is
  the class's only storage (100 kJ). A voxel of coal is 300 s at full load; a brush cell of it is
  5.3 h, hauled by hand.
- **Transmission**: `iron_shaft`, class `shaft`, 16 cells, 20 kW — *"mechanical output"* is short.
  `iron_pipe`, no run bound.
- **Pump**: `iron_pump`, head 20 m (three stages for the played mine with one spare), η 0.5, cap
  30 L/s — just over a one-voxel breach's 29 L/s (M8a) and a hair under dirt's 30.8, on purpose.
  At 20 m the litre is 392 J, so 10 kW delivers 25 L/s.
- **Crafting**: `furnace` (burner): iron ore → iron, iron + coal → steel, sand → glass, limestone
  → lime. `machine_shop` (shaft, 2 kW): cylinder, piston, gear, valve, rod. Chains: 12 iron plate
  + firebox + gauge → boiler; cylinder + piston + flywheel + beam + iron → engine; cylinder + 2
  brass valves + rod → iron pump. Counts chosen so a machine is 2–4 voxels of ore per part.
- **Needs**: coal in bands, iron in blobs **under the water table** (taken), limestone, sand, clay.
- **What pushes you on**: an engine every 16 m of shaft, a boiler beside it eating the water it
  fights, coal walked down every five hours, 20 m a stage; the engine room is a chamber under a
  roof, and a fall turns the boiler to rubble that credits nothing. A dynamo turns the shaft into
  something that goes round corners. Target 2–4 h.

### Tier 2 — DC: copper, lead, glass. *"The dark half."*

- **Power**: `dynamo` (shaft → `dc`, 0.9) on the engine; `solar_panel` (`solar` → `dc`): 500 W a
  1 m cell (real ~200 W/m² peak, ×2.5 because there is no night) × the panel's top cell's open sky
  columns ÷ 16 (exact, off `SkyColumn`) × (256 − overcast byte) ÷ 256 — the only two floats that
  ever enter a pool, quantised at that one site. **The storm that floods the mine dims the panels
  that pump it.** Storage `lead_acid_bank` (100 MJ placed) and `lead_acid_pack` (carried, 3.6 MJ —
  a car battery): the entangled miner's first gate, recharged only docked at a `charger` on a live
  dc component within reach.
- **Transmission**: `copper_wire`, class `dc`, **24 cells** — derived: 48 V, 16 mm² copper at
  1.05 mΩ/m, 100 A, 10% drop → 46 m of conductor, 23 m out and back (each of those is a choice;
  `--dcrun=` sweeps it; 32, half the played mine, is the alternative) — and 10 kW. `steel_pipe`,
  no run bound.
- **Pump**: `electric_pump`, head 30 m, η 0.6, cap 60 L/s. At 30 m the litre is 490 J, so 60 L/s
  wants 29 kW and a 10 kW circuit delivers 20: **head is a bill, not a wall** — the deep face is
  the expensive face.
- **Crafting**: `wire_mill` (dc, 5 kW): one voxel of copper → 64 voxels of `copper_wire` (a wire
  voxel is cable in conduit; its `density` 400 says so — whole-voxel recipes without an inventory
  unit change). Furnace: sulphur + water → acid (water is a sink on the station's intake,
  `consumed`), sand + coal → silicon. Lead-acid = lead + acid + glass jar; solar = glass + silicon
  + frame + wire.
- **Needs**: lead and gypsum or sulphur mid-depth, a sand layer.
- **What pushes you on**: 24 m of copper and 10 kW — the pump at the face runs at a third of its
  rate; the pack runs the entangled miner for ~56 s and recharges only at the dock; a wire through
  a roof is a wire through a roof that falls, and rubble credits nothing. Target 4–8 h.

### Tier 3 — AC: steel, nickel, the printer. *"The roof takes the cable."*

- **Power**: `alternator` (shaft → `ac`, 0.95); `turbine` (burner → ac, 200 kW, drinks 5 L/s — a
  coal hog at a voxel every 15 s, on purpose: it wants M11's inserters); `inverter` (dc → ac) so a
  solar field feeds the grid. Storage `nickel_iron_bank` (300 MJ) and `nickel_iron_pack` (carried,
  10 MJ), docked recharge as before.
- **Transmission**: `ac_cable`, class `ac`, `maxRunCells: null` — **null, not a large number, so
  "no limit" cannot be mistyped as a limit** — 200 kW. Membership through unloaded chunks holds
  (decision 4): a surface turbine powers a pump 64 m down with the surface unloaded.
  `stainless_pipe`, unbounded.
- **Pump**: `steel_pump` (multistage, printed), head 100 m, η 0.7, cap 300 L/s — M8a's widest-bore
  throughput, the last tier where the rate is the pump's rather than the automaton's. At 64 m the
  litre is 896 J; 200 kW delivers 223 L/s: **the played mine empties in one stage.**
- **Crafting**: `arc_furnace` (ac, 100 kW): iron + nickel + coal → `stainless` (decision 19),
  bauxite → aluminium (MATERIALS-NOTES' *made of electricity*). **`printer` (ac, 20 kW): one
  feedstock, `stainless_powder`, prints any tier-3/4 part by recipe** — impeller stages, turbine
  blades, cable armour, cell housings. One station, one feedstock, every part: the simplification
  the word *bespoke* buys.
- **Needs**: nickel and bauxite deeper than iron — under the table, so tier 2's pumps are the tax
  on tier 3's ore.
- **What pushes you on**: everything still has to touch. The cable is laid up your own shaft one
  cell at a time, every cell is one M9 can turn to rubble — *a fall re-summarises one chunk, the
  component splits at the next boundary, and every pump below it stalls at once*, printed as
  `unpowered by edit` — deeper is wetter and heavier (seepage ∝ head, the support deficit grows
  under a lake), so the plant that holds 64 m is inadequate at 120, and the far basins you must
  empty are outside the box, which only a channel number reaches. Target 8–15 h.

### Tier 4 — Quantum: lithium and uranium. *"The outlet on the hill."*

- **Power**: any ac component through a `quantum_link` (ac → `channel`) feeds a channel's pool;
  `reactor` (burner: uranium → ac, 2 MW). Storage `vacuum_cell` — *"quantum vacuum and pressure
  based energy storage"*, his words — placed 5 GJ, carried 500 MJ; **the carried cell recharges
  from its channel from anywhere.** At 100 J a voxel (decision 16) a bite is 6.4 kJ and 10/s is
  64 kW, so 500 MJ is ~2.2 h of continuous full-rate mining — *"as effective as it is now"*, a
  session.
- **Transmission**: none. `via: channel`, 0–15, exactly M10-PLAN decisions 5, 6 and 8 as amended
  here. An unloaded outlet on the seed surface inside the watershed window credits the ledger;
  outside the window it stalls `unanalysed`; underground it stalls `unloaded`.
- **Pump**: `relay_pump` / `relay_outlet` as M10-PLAN builds them first, gaining a `joulesPerLitre`
  of 1,000 flat (estimate — the relay is the first pump whose bill is the constraint rather than
  its head): 100 kW at full rate, which is 200 panels — **solar runs the DC tier's pumps, not the
  relay**; the relay runs off the AC and quantum tiers' generation.
- **Crafting**: `fabricator` (channel, 500 kW, tens of minutes): the relay pair, the computer, the
  vacuum cell, the link. The first relay pump costs a session of tier-3 generation (estimate —
  the ladder's last gate, his number). The rebreather → entangled-lungs ladder sits here on the
  survival trigger, not built.
- **Needs**: lithium as a solid ore in the deepest band under the deepest table (decision 20),
  uranium deepest of all.
- **What pushes you on**: nothing, structurally — DESIGN's *no finish line*. Every cost scales with
  depth and never plateaus: seepage with head, the support deficit under saturation, joules per
  litre lifted, uranium hauled from the bottom of a wet mine; more channels, more cells, deeper
  water, and the rain never stops.

---

## Decisions

Recommended and built as recommended unless marked *Matthew's*; the rejected arms are named so
they are not re-argued. **All twenty-three taken as recommended the same night** — *"As
recommended, lets do M10a's planning pass"* — and that pass is at the end of this document; it
amends decisions 5, 12 and 14 and the reconcile rule under *Persistence* from the code.

### 1. Connection is membership, never transport, at every tier below quantum — *recommended; Matthew's, because it narrows M10-PLAN decision 8*

A conductor is a material with a `conductor` block (`class`, `maxRunCells`, `maxWatts`); otherwise
an ordinary voxel — meshed, lit, collided, mined and credited per voxel, turned to rubble by a
fall. It holds no state and has no row. `ConductorTracker` on `PortalTracker`'s three hooks does
one thing in the hook: if the old or new material's `MaterialTable` byte says *conductor* or
*machine*, add the chunk to a dirty set. **Nothing else happens in a hook, ever** — it fires
mid-tick inside `Complete` (a throw there is the M2c-2 stall) and 32,768 times a bite.
`ConductorIndex` holds per chunk the maximal 6-connected runs of one class (*segments*) with their
class, strictest bounds, a 512-bit set of the 1 m cells they occupy, six face bitsets and the
machine faces they touch; `NetworkSet` is a union-find over segments, edges from ANDing
neighbouring chunks' face bitsets and from bodies bridging (a body joins every component of its
link's class touching its declared face — two wires screwed to one pump are one circuit).
Components join only segments of the same class, so a dc wire never becomes one circuit with an
ac line. A component's id is the (x, y, z) of its minimum voxel — position-derived, so two
sessions label identically and nothing about connectivity is ever stored.

- **Rejected: a flow solve** (pressure, current, pipe volume) — M10-NOTES' pipe argument, which
  stands verbatim against a *per-tick* solve. **Rejected: index work inside `onVoxelChanged`** —
  the burst rule only takes over after 64 edits a chunk, so a bite through a 10,000-voxel line
  would pay up to 64 O(component) re-floods in one frame (~64 ms Release, ~640 ms Debug), and a
  throw in `Complete` eats the neighbour push. **Rejected: a global union-find rerun over every
  wired chunk at every edited boundary** — O(wired chunks ever placed), which in a game with no
  end only grows.
- **Cost**: a third per-chunk index of PortalIndex's size (about an M8c-1-sized part); 0.05–0.5 ms
  Release per rescanned chunk (estimate, scaled from the seep scan's 0.56 ms over a padded copy);
  a 10,000-node relabel ~0.5 ms Release once on a cut; rescans capped at `--conductorbakes=` (8,
  `--fieldbakes`' shape) a boundary, a machine on a still-dirty chunk `pending`.

### 2. The hose: a presentation fill and a live leak at a cut, not a FIFO with contents — *recommended; Matthew's, because the FIFO was his proposal*

He asked for *"one output hose connected to one or more pump inputs that would fill the hose like
a FIFO queue"*, and it was answered in the chat as a shift register of segments holding litres. The
design pass found a version that delivers the two things the FIFO was for with nothing stored:

- **The fill is a look.** A conductor voxel carries one bit in the merge key — *energised* — on
  `UV2.y` beside M9b's strain bit; a component whose pump moved water this boundary is energised
  from the pump end outward at *k* cells a boundary (a per-component counter, cosmetic), and a
  remesh is non-urgent and happens only when the bit flips. The hose visibly fills from the pump
  over a few seconds and drains when the pump stops.
- **The leak is the pump's own flow.** A pump whose component was cut this boundary — a mined or
  fallen hose voxel — pours this boundary's litres at the cut's cell instead of at its outlet, an
  `Add` on the near side of the break, until the player turns it off or the hose is mended. A
  severed line sprays because the pump behind it is still running, which is what a real one does,
  and it is real water from the intake, counted on the identity like any pour.

**The FIFO with contents** — each hose voxel holding up to *C* mL, shifted outlet-first each
boundary, merging in position order — is honestly cheap (O(segments), trivially terminating,
bounded by the run cap) and is the arm to build if the fill has to be *physically* delayed. Its
costs: a third home for litres outside Tier A and B, so `hosed` joins every identity line and the
cross-tier check; per-segment contents persisted in the chunk's summary (schema 5's blob grows);
the "unloaded voxel cannot change" argument of decision 4 becomes "unloaded contents cannot move",
which is a second invariant to test; and a hose that leaks its *stored* litres when cut needs the
cut to find them.

### 3. Run bounds are a count of occupied 1 m brush cells — *recommended; the numbers are Matthew's*

A component's run is the popcount of the union of its segments' cell sets; over the strictest
material's bound it is `overlength` and delivers nothing. Shaft 16, dc 24, ac null; `--dcrun=` and
`--shaftrun=` sweep them.

**Pipes have no run bound at any tier — the pump's head is the fluid tier's bound.** Matthew, on
reading the first draft (which had wooden pipe 8, iron 64, steel 256): *"I may want to change the
fluid hose output lengths to be a fair bit more forgiving distance wise, so you can pump from low
to high easily enough like you might need more to go deeper."* So a hose runs as far as you can
lay it, a long horizontal run costs the wood or iron it is made of and nothing else, and going
*deeper* is what needs more: another pump and another outlet every head's worth of lift — 4, 7,
20, 30, 100 m by tier, none for the relay. A second bound on the same tier would have been a
second thing to explain, and the wire's cap exists for reasons (the dark half, the cost model) a
hose does not share. `maxRunCells` stays a field on the conductor block, so a mod that wants a
short hose can have one.

- **Rejected: a voxel count.** A brush cell is 64 voxels, so the default right-click brush would
  give a 2 m dc run and Alt+right 32 m for the same copper — three of the four designs had this.
- **Rejected: a path-distance BFS.** O(sources × component) per relabel, its depth bound does not
  bound its breadth (a 100 × 100 m copper mesh is inside 96 hops of every point on it), and it
  needs loaded voxels where a summary has none.
- **Cost**: a fat junction wastes cells (copper spent, which is the point); a coil inside one cell
  is free, which nobody will notice.

### 4. Membership survives unloading by persisted per-chunk summaries; rows tick unloaded when their inputs are row-local; any grid read stalls — *recommended*

Summaries are the chunk's derived data at schema 5, written as a **fourth list on the batch
`Write`** (the fluid panel's lifecycle, M8b-4b: a chunk in one transaction and its summary in
another is a wire in rock after a crash), rebuilt and compared at every `onVoxelsReady`
(`M10: N summary(ies) rebuilt at arrival`, the chunk wins). **The forget path rescans a dirty
chunk before writing its summary** — the critic's catch: a chunk dirtied and unloaded before its
capped rescan would otherwise persist a stale one, and "an unloaded voxel cannot change" is the
whole of the freshness argument. No row ever caches a group id. Every row ticks every boundary;
generators and stations run row-local (a boiler from its stock — but its drink is a grid read, so
an unloaded boiler makes nothing, which is right; solar off a cached openness byte refreshed at
arrival, at any edit in its chunk and at any sky-height move in its column; a station advances its
job); a pump, outlet or drink stalls `unloaded` on any grid read.

- **Rejected: "only what is loaded"** — every physical tier dies at the 16 m box and the AC tier
  reads broken on day one. **Rejected: a keep-loaded streamer policy** — a third always-loaded set
  that generates, lights, ticks water and support with nobody near, SCALE's sweep by another name.
  **Rejected: group ids cached on rows** — a label that changes on every relabel orphans every
  unloaded member at the first loaded edit.
- **Cost**: schema 5 and the fourth list; ~0.9 KB per wired chunk (estimate); the union-find's open
  cost grows with the wired world (single ms at thousands, once); three refresh triggers on the
  openness byte, each a test.

### 5. The header and the machine table are one transaction — a new store overload, the store slice's first test — *recommended*

`IWorldStore.Write(chunks, columns, fluid, conductors, machines, header)`, one SQLite transaction,
the header optional; autosave and quit use it; `SaveAll` drains the lane first as it does. The
test: a store that throws after the header leaves the world byte-identical. **No inventory-to-row
transfer — a recipe queue, a stock tab, a fuel fill — ships before it.** This overrides M10-PLAN
decision 4's "the header does not change for this": the header's *format* does not, but its write
moves into the batch.

### 6. The inventory stays in voxels; recipes are in millilitres; the fraction is a remainder on the station row — *Matthew's, because MATERIALS-NOTES recorded the mL move and this changes its timing*

A station's output accrues millilitres in a per-row remainder and emits a voxel into its stock at
15,625 — the hand-off's remainder-per-pit shape a third time. Hand recipes are whole-voxel by
loader rule. Header v3's inventory is untouched; the millilitre bump stays on the narrowed trigger —
*a hand recipe wants a fraction, or a play finds 15.6 L parts absurd* — and goes alone (it was
"v5" here until 2026-09-18; the hotbar assignment takes v5 and the version number is a counter,
so the millilitre move takes whichever is next).
`unit` on a material is `litres` (default), `count` (one voxel is one part; the hotbar prints
`× 3`) or `cells` (64 voxels is one; a machine body prints `× 1`). Energy's unit is the
millijoule for the reason water's is the millilitre: `watts × 500` mJ a boundary is exact, and
`joulesPerLitre` equals mJ per mL.

- **Rejected: v5 before the first recipe** — a data-loss-shaped bump adjacent to the milestone
  whose oracle is exact conservation. **Rejected: water in the inventory** (a bucket) — a third home
  for litres that a keypress could break the M8 oracle through; fluids are never carried, a
  recipe's water is a sink on the station's intake.
- **Cost**: a part is at least 15.625 L (a gear is 15.6 L of brass; a 1 m³ machine is eight tonnes,
  which is what one weighs); a smelter shows 0 L until a voxel accrues, printed on its panel.

### 7. Recipes are a dictionary by `Identifier`; a station's job is persisted by value — *recommended*

`mods/<mod>/recipes/*.json`, loaded in a second pass after every mod's materials — the loader's
first cross-kind reference; a recipe naming a material or station tag nobody defines is a load
error naming the file. No numeric recipe ID exists and none reaches the world file: a row holds
remaining millis, the outputs owed as (material, mL), the inputs template for `repeat` and the
joule remainder, inputs consumed at queue time. A mod removed mid-job finishes what it started.

- **Rejected: a world-owned recipe ID table** — a second thing that can never be renumbered, a
  second hole story, a second reconcile. **Rejected: the recipe's string in the row** — a per-row
  resolve at open and the one bend of non-negotiable 2 for no gain.

### 8. Parts are materials with `unit: count` and `placeable: false`; the hotbar gains a placeable filter and a parts page — *Matthew's, because it is the hotbar he plays with*

One registry, one ID table, one header section, one save form, one reconcile, and `Q` places a
part nowhere. ~30 new IDs for the ladder; the filter ships with the recipes, not after, or the
number row is unusable after the first piston.

- **Rejected: a second item inventory** — a second registry, header section, hotbar and thing a
  mod must learn.
- **Cost**: every acquired part shifts the ID-ordered hotbar (M6d-3's known cost, fixed for
  placeables by the filter); a removed mod leaves that many holes. **Amended 2026-09-18**: the
  hotbar becomes a player-authored assignment and nothing shifts it; the filter is the inventory
  grid's — see "The inventory screen — folded into M10g" at the end of this document.

### 9. The pool is the sum of the storage rows on a component or channel, never a stored number — *Matthew's, because it amends M10-PLAN decision 8*

Generation with no storage is use-it-or-lose-it, `clamped` reported; a cut splits a circuit with
each half keeping the joules physically in its batteries; nothing persists per network. A channel
with no battery reads `clamped == generated`.

### 10. Every float is quantised at the one site it enters a pool — *recommended*

Overcast to a byte, openness to open columns ÷ 16, the crank to integer milliseconds held per
boundary; the tick consumes integers only, so `pool == generated − drawn − clamped` is integer
arithmetic end to end and the multiplayer seam holds exactly. Three of four designs multiplied by
the raw float `OvercastAt`.

### 11. Hand crafting is instant; time belongs only to powered stations you walk away from; the crank drives pumps, never recipes — *recommended; the `E` tap/hold split is Matthew's*

A `hand` station holds nothing and ticks nothing. Every other station queues a job that advances
500 ms per boundary while its pool delivers `joules × 500 ÷ (seconds × 1000)` mJ (integer, the
remainder on the row) and pauses unpowered, never losing progress. The keys: **`E` held** within
reach of a crank machine cranks it; **`E` tapped** at a workbench opens the craft panel;
**right-click with the computer** on any machine opens its ports and stock. `E` is unbound today.

- **Rejected: a cranked workbench** where a recipe's time is joules ÷ 75 W of held `E` (five
  minutes a pump body) and **a workbench that advances only while you stand at it** — held-key or
  standing-still time with nothing to do is the opposite of *"the fun part"*. The hand tier's gate
  is the part chain and the ore, not time.

### 12. Pumps pour to `pipe_outlet` rows within head; the litre's cost is the head's; conductor placement into water is deferred and displacing — *recommended*

`joulesPerLitre(Δh) = ceil(9.8 × Δh ÷ η)`, integer, cached per outlet link at relabel; delivered
= `min(cap, watts ÷ J/L, offer, room)`. A Tier A intake (`Draw` is whole litres) keeps a litre
remainder per row so a 0.47 L/s pump under a lake delivers it. A pipe, wire or shaft placed into a
wet voxel takes decision 2's path from M10-PLAN — queued to the boundary, `Lift`/`Return` around
it — so a hose *can* be laid through the flooded room.

- **Rejected: a pump pouring into the column above its own top** — that column is inside its own
  3 × 3 × 3 intake, so it pumps its outlet back and the room never dries while `pumped == emitted`
  holds perfectly (one design shipped this).

### 13. The boiler and the engine are two machines; the boiler drinks at the boundary and holds nothing; litres drunk leave the world — *recommended*

His words are *"a boiler and mechanical output"*: `boiler` (burner, drinks) and `steam_engine`
(`via: adjacent:boiler`, the shaft source). `consumed` is a `Watershed.Outlet`-shaped term on both
tiers' identities and the cross-tier check; a fluid recipe input is the same sink on a station's
intake. M10-PLAN decision 10's sentence gains *minus consumed*.

- **Rejected: a tank on the row** — in-flight fluid by another name, a persisted number that is
  nothing's truth (M10-NOTES piece 3). **Rejected: returning it as rain** — rain is a pure function
  of seed and time and cannot be added to.

### 14. The machine active set, the `hasMachines` bit on every chunk an intake box touches, `IsLoaded` before the wet read, and the 27-cell intake — *recommended*

A machine sleeps when it stalled with nothing changed and wakes on a `Disturb` or `ChunkArrived`
in any chunk whose `hasMachines` bit is set — the bit is set on every chunk the intake box
touches, up to eight, not the body's alone — a relabel, a port write, a crank, a transfer, or its
pool going non-zero. The intake asks `IsLoaded` per chunk first (an unloaded panel reads *dry*,
the "conditional no" that froze the water once) and stalls `unloaded` rather than sleeping. The
read is ≤ 8 chunk lookups then bit reads off the panel bitsets, and `Publish` caches a per-panel
wet count so a pump whose panels are all dry costs the lookups and sleeps. 500 awake pumps ≈ 2–5
ms Release (the `[z][x][y]` order strides a 1 m cell across sixteen 4-bit groups — `--pump`
decides), against ~22 ms the naive way.

### 15. Carried cells dock below quantum; the equipped pack rides the header; the tool pays per voxel; `Q` with no pack takes nothing once the gate exists — *recommended; the numbers are Matthew's*

Batteries are materials with `kind: storage, carried: true` and a class; a `dock` field names the
charger kind; below quantum a carried cell recharges only while docked at a charger within reach
on a live component — without it the DC tier's *dark half* and the AC tier's cable-to-the-face
never bite and the battery ladder is a capacity slider. The vacuum cell recharges from its channel
anywhere. Header v4 carries the *equipped* pack (material, `charge_mJ`, channel); a second pack in
the inventory is a count with no charge — **amended 2026-09-18 on the first play's swap, which
threw 2,650 kJ away: *"keep its charge as an item please"*. A carried pack keeps its charge and
channel beside the count (`CarriedPacks`, header v5), a right-click equips the fullest one
carried, and the swap's `forfeited` term is `stowed`; the count is still the truth of how many
and the charges annotate it, so an empty pack has no row.** The entangled miner's `joulesPerVoxel` sits on the tool's
`power` block and the bite, the fill at range and the Alt chip all pay it; with the gate on and no
pack, `Q` takes nothing; `--nogate` keeps the debug arm for measurement runs, printed. `Q`
reclaims a machine at range exactly as a pick does up close — dismantle credits it — which is not
a defect.

### 16. The miner's joules per voxel and the vacuum cell are picked together so every pack digs something and only the top lasts a session — *Matthew's*

100 J a voxel (6.4 kJ a bite, 64 kW at 10/s); packs 3.6 / 10 / 500 MJ — 56 s, 2.6 min, 2.2 h of
full-rate mining; capacity and recharge scale by tier, never the rate or the bite.

**Amended 2026-09-18, on the first play, twice in an afternoon.** *"You don't get far … for the
sake of the game be a little generous"* moved the price to 20 J (2,812 cubes, 4.7 min); an hour
later, with `Alt+P`'s 5 GJ cell on the pack's channel, *"I might have appealed to your generosity
too much … nearly half a chunk I reckon … I am having trouble fighting solar recharge on the
channel I am on"* — which named the second number. **The price is the burst and the recharge is
what you fight**: at 20 J a 1 kW recharge sustains a cube every 1.3 s off any charged channel,
which is faster than a person digs, so the pack never drained. Now **50 J a voxel and 250 W**
(`lead_acid_pack.rechargeWatts`, 1 kW until then): a bite is 3.2 kJ, so the same three packs are
**1,125 / 3,125 / 156,250 cubes — 1.9 min, 5.2 min and 4.3 h** of non-stop bites, half a chunk
is 23% of the lead-acid pack on the HUD, and on a full channel the pack sustains **a cube every
12.8 s** and refills in **four hours**. The pack's 3.6 MJ stays, because it is a real battery's;
the price and the charge rate are the game's (250 W is C/4 for a 1 kWh lead-acid, which is at
least honest). The arms: 1 kW / 500 W (a cube every 3.2 / 6.4 s sustained), 20 J / 100 J (5.5 /
1.1 chunks a pack); two numbers in two JSON files, no rebuild. The top cell past a session is
*"as effective as it is now"* by another name.

- **Rejected: physical specific energy** (200 MJ a bite, a lead-acid pack digs one voxel) — he
  asked for less effective, not inert. **Rejected: a 26-minute top cell** — under *"as effective as
  it is now"*.

### 17. Water and electricity, cables and roofs — *recommended*

A `dc` or `ac` row whose body face touches a wet published cell stalls `flooded` (the 27-cell read
already exists); crank, shaft and channel machines do not care — the flood that stops your pump
is the tier-2 moment. Conductors and pipes declare `supportDistance` 8 so a run along a roof
holds while the roof holds and falls with it; a wire voxel is solid, so it is a dam cell to the
automaton and a step to the collider, named. A machine body is a 64-voxel pillar under
`SupportRule.Rest`, so every boiler shores the roof it sits under; whether that reads right is
M10d's pillar verdict, and a machine `supportDistance` is one JSON number either way.

### 18. Brass with zinc, not bronze with tin — *Matthew's*

His word; one ore either way; WORLDGEN-NOTES' line is amended.

### 19. Stainless is iron + nickel + coal; no chromium material — *Matthew's*

Nickel is already on the ladder for the battery; chromite is one JSON later if it bothers him. A
metallurgist would object.

### 20. Lithium is a deep solid ore under the deepest table; brine is a later engine pass — *Matthew's*

*"Must be pumped"* is the water over it. MATERIALS-NOTES' option B — a second Tier B fluid kind —
is a second automaton, a second panel store and a second set of identities: an engine milestone,
not a worldgen prerequisite, and two designs put it on the tier's critical path.

### 21. Media are free class strings; the engine's `via` names are reserved; converters are ordered at load; back edges draw from storage only — *recommended*

`channel`, `crank`, `surround`, `burner`, `solar` and `adjacent:<id>` are the engine's; any other
string is a conductor class. A dc ↔ ac cycle (an inverter and a rectifier) is legal because the
back-edge converter reads only storage rows on its input, with a load-time warning naming it. **A
third-party sixth tier is one conductor JSON and one converter JSON with no engine change** —
non-negotiable 5's test for the ladder. Every mod number that sizes a loop is bounded at load
*and* at use, refused never clamped, `null` meaning unbounded: `litresPerSecond` ≤ 54,000 (a
3 × 3 × 3 box holds 27,000 L, twice a second), `intakeRadiusCells` 1..2, ≤ 16 ports,
`stockVoxels` ≤ 65,536, `maxRunCells` ≤ 2²⁰, `headMetres` ≤ 1,024 (M4a's float world). A row read
from disk is clamped to its material's bounds with `clamped` counted — the loader is not the only
caller, a row is one too.

### 22. Not in the ladder — *recommended*

Shoring materials (a placed pillar already carries a full budget; M10d's pillar verdict first);
the bucket; a keep-loaded policy; conveyors, which are state in flight by definition and ship as
inserters — stock-to-stock transfers at the boundary — or not at all; random distribution; a
machine cap (printed; trigger `machines` over a millisecond on the `boundary` line); wading and
oxygen (the survival trigger); brine; chromium; day and night.

### 23. Sequencing and the roadmap — *Matthew's*

**M10 stays one milestone, ten parts in four groups**, on his *"a lot longer … split it into
pieces like we always do"* — but each group is the size of a milestone and the plan says so
rather than hiding it in a letter: **the quantum rung** (M10a–d, M10-PLAN as amended), **the
network framework** (M10e–f: the conductor index; generators, converters, the crank and the
sink), **crafting** (M10g: recipes, stations, parts, the computer), and **the world and the ladder**
(M10h: worldgen for the ladder, WORLDGEN-NOTES' own pass first; M10i: the five tiers' JSON; M10j:
the play, the ladder climbed once, timed). Framework before worldgen, debug-seeded, because the
framework is headless-testable and the worldgen pass wants a built recipe format to plan against
— at the cost that the wading tier he asked for most explicitly is the last thing he touches, and
the first play may send numbers back through three built parts (every one a JSON edit). M11
shrinks to drills, inserters and health; the milestone names stay. Solar ships on the `channel`
class in M10c as the pool's first generator and is re-classed to `dc` in M10i — a nerf later,
M10-PLAN decision 7's shape a second time, printed on the `M10:` line so nobody builds a base on
wireless solar.

- **Rejected: worldgen first** — the feel earlier, the format guessed. **Rejected: renumbering the
  table** (M11a–f, health to M12) — his naming stands.

---

## The format, in the shapes a mod would write

A machine is a material with a `machine` block (M10-PLAN decision 1, amended): `kind` from the
engine's set — `pump`, `outlet`, `generator`, `converter`, `storage`, `station` — `links` saying
how each medium reaches it, `ports` the programmable integers. `via` is the membership rule; `face`
is which body face, since sub-quantum machines have faces and the row carries a facing
(`BrushFacing`, the ramp's).

```json
// materials/iron_pump.json — a tier-1 pump: shaft in on any face, water out through the top
{ "id": "iron_pump", "hardness": 3.0, "supportDistance": 8, "density": 7800, "textureLayer": 9, "unit": "cells",
  "machine": { "kind": "pump", "litresPerSecond": 30, "headMetres": 20, "efficiency": 0.5, "intakeRadiusCells": 1,
    "links": { "power": { "carries": "power", "role": "sink",   "via": "shaft", "face": "any" },
               "water": { "carries": "fluid", "role": "source", "via": "pipe",  "face": "top" } },
    "ports": { "weight": { "default": 1, "min": 1, "max": 100 } } } }

// materials/relay_pump.json — M10a's machine on the same format; `via: channel` is the only rule M10a implements
{ "id": "relay_pump", "hardness": 5.0, "supportDistance": 8, "density": 7800, "textureLayer": 5, "unit": "cells",
  "machine": { "kind": "pump", "litresPerSecond": 100, "headMetres": null, "joulesPerLitre": 1000, "intakeRadiusCells": 1,
    "links": { "power": { "carries": "power", "role": "sink",   "via": "channel", "port": "channel" },
               "water": { "carries": "fluid", "role": "source", "via": "channel", "port": "channel" } },
    "ports": { "channel": { "default": 0, "min": 0, "max": 15 }, "weight": { "default": 1, "min": 1, "max": 100 } } } }

// materials/wooden_pump.json — a crank machine: runs only in boundaries a player within reach held E on it
{ "id": "wooden_pump", "hardness": 1.0, "supportDistance": 12, "density": 700, "textureLayer": 6, "unit": "cells",
  "machine": { "kind": "pump", "litresPerSecond": 1, "headMetres": 4, "efficiency": 0.5, "intakeRadiusCells": 1,
    "links": { "power": { "carries": "power", "role": "sink",   "via": "crank", "watts": 75 },
               "water": { "carries": "fluid", "role": "source", "via": "pipe",  "face": "top" } } } }

// materials/boiler.json — a burner that drinks; the engine beside it is the shaft source
{ "id": "boiler", "hardness": 3.0, "supportDistance": 8, "density": 7800, "textureLayer": 10, "unit": "cells",
  "machine": { "kind": "generator", "ratedWatts": 10000, "stockVoxels": 64,
    "links": { "fuel":  { "carries": "power", "role": "sink",   "via": "burner" },
               "feed":  { "carries": "fluid", "role": "sink",   "via": "surround", "litresPerSecond": 0.5 },
               "steam": { "carries": "power", "role": "source", "via": "adjacent:steam_engine", "face": "any" } } } }

// materials/solar_panel.json — a converter from the engine-known medium `solar` onto the dc class
{ "id": "solar_panel", "hardness": 1.0, "supportDistance": 2, "density": 2500, "textureLayer": 11, "unit": "cells",
  "machine": { "kind": "converter", "ratedWatts": 500, "efficiency": 1.0,
    "links": { "sky": { "carries": "power", "role": "sink",   "via": "solar" },
               "out": { "carries": "power", "role": "source", "via": "dc", "face": "any" } } } }

// materials/lead_acid_pack.json — a carried storage on the dc class: recharges only docked
{ "id": "lead_acid_pack", "hardness": 1.0, "supportDistance": 2, "density": 4000, "textureLayer": 13, "unit": "count", "placeable": false,
  "machine": { "kind": "storage", "carried": true, "capacityJoules": 3600000,
    "links": { "charge": { "carries": "power", "role": "sink", "via": "dc", "dock": "charger" } } } }

// materials/copper_wire.json — a conductor is a voxel with a conductor block, nothing else
{ "id": "copper_wire", "hardness": 0.5, "supportDistance": 8, "density": 400, "textureLayer": 12, "opaque": false,
  "conductor": { "class": "dc", "maxRunCells": 24, "maxWatts": 10000 } }

// materials/ac_cable.json — null, not a large number
{ "id": "ac_cable", "hardness": 1.5, "supportDistance": 8, "density": 600, "textureLayer": 15, "opaque": false,
  "conductor": { "class": "ac", "maxRunCells": null, "maxWatts": 200000 } }

// materials/coal.json — a fuel is a flag, the way a conductor is
{ "id": "coal", "hardness": 1.5, "supportDistance": 6, "permeability": 0.1, "density": 1350, "textureLayer": 7,
  "fuel": { "joulesPerVoxel": 3000000 } }

// tools/entangled_miner.json — the tool side of the gate; a tool with no power block is free
{ "id": "entangled_miner", "radiusMetres": 0.5, "hardnessThreshold": 5.0, "cooldownSeconds": 0.10,
  "power": { "via": "channel", "joulesPerVoxel": 100 } }

// recipes/iron_pump.json — assembly at the machine shop; a body is one brush cell
{ "id": "iron_pump", "station": "machine_shop", "seconds": 60, "joules": 120000,
  "inputs":  [ { "material": "iron", "voxels": 40 }, { "material": "cylinder", "voxels": 1 },
               { "material": "brass_valve", "voxels": 2 }, { "material": "rod", "voxels": 1 } ],
  "outputs": [ { "material": "iron_pump", "voxels": 64 } ] }

// recipes/smelt_iron.json — the remainder case: five smelts yield one voxel and 9,375 mL toward the next
{ "id": "smelt_iron", "station": "furnace", "seconds": 30, "joules": 3000000,
  "inputs":  [ { "material": "iron_ore", "mL": 15625 } ],
  "outputs": [ { "material": "iron", "mL": 5000 }, { "material": "slag", "mL": 10625 } ] }

// recipes/wooden_pump.json — a hand recipe: instant, whole voxels, no joules
{ "id": "wooden_pump", "station": "hand", "seconds": 0, "joules": 0,
  "inputs":  [ { "material": "wood", "voxels": 48 }, { "material": "stone", "voxels": 16 } ],
  "outputs": [ { "material": "wooden_pump", "voxels": 64 } ] }
```

Rules the loader enforces, refusing with the file named: a `class` is in the identifier charset
and not an engine `via` name; components join only one class; a conductor material cannot carry a
`machine` block (a body bridges, it does not conduct); a `channel` link requires a `channel` port
and a conductor link forbids one; a `carried` storage has one power sink link and no face; a
`hand` recipe has `seconds` 0, `joules` 0 and whole-voxel outputs; `seconds` is a multiple of 0.5;
no material is repeated on a side; a station tag no station declares is a warning. Labels come
from identifiers, never from a field (M6d-3). MATERIALS-NOTES' element-balance check is a
load-time warning, never the engine's oracle.

---

## The tick, at M10-PLAN decision 3's slot

Between `RunPendingChunkFills()` and `CheckTable()`, one `Machines.Tick`, its own bracket
`machines` on the `boundary` line:

1. **The dirty set**: rescan at most `--conductorbakes=` chunks; relabel only the components that
   had a segment in a rescanned chunk (union-find cannot delete, so a cut is one re-flood over
   that component's segments, never a global rerun); a machine on a still-dirty chunk is `pending`.
2. **Media, in load-time order**: `crank` (millis held × watts), `burner` (fuel from stock ×
   `joulesPerVoxel`, only while a consumer wants joules), `solar` (the integer product above),
   then the conductor classes and `channel` in the converters' topological order. Per component
   or channel: pool = Σ storage rows' charge + this boundary's generation; consumers draw in
   (x, y, z) order — a pump's draw is `joulesPerLitre(Δh) × mL it will move`, resolved after room
   is read and before water moves; converters `ratedWatts × 500` mJ; stations `joules × 500 ÷
   (seconds × 1000)` with the remainder on the row; `maxWatts × 500` bounds the boundary's
   transfer; what remains is written back into storage rows in position order up to capacity, the
   excess `clamped`.
3. **Fluid per pump**: outlets' room read (the column above for `channel` and `surround`;
   `pipe_outlet` rows on the component within head), the intake offer read, `moved = min(offer,
   room, rate × 500 ms, pool ÷ J/L)`, then `Remove`/`Draw` and `Add`/`Receive` for exactly that.
   A boiler's drink is a `Remove` with `consumed`.

Steady state with nobody editing wire: the index costs nothing and the tick is the awake set.
**Named lever with its trigger**: the pass moves onto the fluid job's tail (the settle's solve's
precedent) when `machines` on the `boundary` line exceeds 1 ms on the played world.

## Persistence

**Schema 4** (M10a): `machines` — origin, material ID through the world's table, ports, and a
length-prefixed **versioned kind blob** in `BlobFormat`'s frame from the first row: charge,
openness byte, stock entries with a remainder per output, the job by value. The blob's version
byte lets every later part add state with no schema bump; an unknown kind's blob is carried
opaque so a removed mod's row survives the way its material hole does. **Schema 5** (M10e):
`conductors` — one summary per chunk that holds any, riding the batch write. **No `networks`
table.** Header **v4** (M10c): the equipped pack. Header **v5** (2026-09-18): the carried packs' charges. The hotbar assignment (M10g) takes the next.

**Reconcile at arrival, amending M10-PLAN decision 4**: a body with fewer than 64 of its voxels is
dismantled and credited *unless any of its 64 is rubble*, in which case it is destroyed without
credit (a fall's un-fallen voxels are still body, and the plan's "rubble credits nothing" needs
this rule to be true); a wholly absent body is rewritten from the row; a body with no row is
cleared without credit. A v3 build opening a v4 world refuses, `downgraded-world-v3-header.bin`'s
shape, and the fixture is named.

## What a play will feel and where the antagonists press

The water: a hand pump that cannot keep up with the seep; a boiler that drinks the sump it
defends; a dc pump that stalls `flooded` when the water reaches its terminals; a turbine that
drinks 5 L/s. The roof: every machine is a pillar and every cable a thing on a roof; a fall turns
a boiler to rubble that credits nothing and splits a cable so every pump below stalls in the same
boundary. The depth: iron under the table, nickel and bauxite deeper, lithium and uranium deepest,
each under more head, and every litre lifted costs its height.

## The numbers, and where each came from

| Quantity | Value | Source |
|---|---|---|
| Vertical box | 16 m | `ViewDistanceVertical` 2 |
| Seep, 10 × 10 × 3 m stone room under 5 m; dirt | 1.76 L/s; 30.8 | M8c bench at the shipped scale (itself an estimate) |
| One-voxel breach; widest-bore throughput | 29 L/s; ~300 | M8a measured; the 300 is M10-PLAN's estimate |
| Naive intake read; 27-cell read | ~45 µs; ~2–5 µs | estimates against `IsWetIn`; `--pump` takes both |
| 500 awake pumps a boundary | 2–5 ms Release | estimate; naive ~22 ms |
| Rescan per dirty chunk; 10,000-node relabel | 0.05–0.5 ms; ~0.5 ms once | estimates from the seep scan and the light walk |
| Crank | 75 W | physiology, estimate |
| Litre cost | `ceil(9.8 × Δh ÷ η)` J | physics, integer |
| Pumps: head / η / cap — wood, brass, iron, electric, steel, relay | 4/0.5/1; 7/0.6/3; 20/0.5/30; 30/0.6/60; 100/0.7/300; none/–/100 | estimates; 7 m physical; 30 L/s sits between 29 and 30.8 on purpose |
| Boiler / turbine / reactor | 10 kW, 3 MJ a coal voxel, 0.5 L/s; 200 kW, 5 L/s; 2 MW | estimates (real boiler feed ~0.13 L/s, ×4) |
| Run bounds: shaft / dc / ac; pipes | 16 / 24 / null cells; none (head is the bound) | dc derived from voltage drop (its inputs are choices); shaft an estimate; pipes Matthew's |
| Circuit caps: shaft / dc / ac | 20 / 10 / 200 kW | estimates |
| Solar | 500 W a cell × openness/16 × (256 − byte)/256 | estimate (real ~200 W/m² peak, ×2.5 for no night) |
| Storage: placed / carried, by tier | 100 MJ, 300 MJ, 5 GJ / 3.6, 10, 500 MJ | estimates (lead-acid real ~250 MJ/m³); Matthew's |
| Miner | 50 J a voxel → 1.9 min / 5.2 min / 4.3 h a pack; the lead-acid pack's recharge 250 W → a cube every 12.8 s sustained, 4 h to refill (100 J and 1 kW → 56 s / 2.6 min / 2.2 h and a cube every 6.4 s until the first play, 2026-09-18) | played once; Matthew's |
| Wire mill | 1 voxel copper → 64 wire | the conduit fiction |
| Bounds | intake 1..2 cells; L/s ≤ 54,000; ≤ 16 ports; stock ≤ 65,536 | derived / choices |
| Duration targets, tiers 0–3 | 30–60 min / 2–4 h / 4–8 h / 8–15 h | estimates for feel, unplayed |
| New IDs | ~30 parts, ~15 ores and conductors | estimate |

## The measurement to take first

`--pump` on the bench, Release, before any rung's rate is believed: the relay pump's delivered
litres per boundary against its nominal 100 L/s into three intakes — a sheet under a lake (`Draw`),
a flooded 3 × 3 × 3 of Tier B cells, a one-voxel trickle — over 200 boundaries with the loop closed
so `pumped == emitted` is checked to the millilitre on both tiers and across them; beside it the
intake read priced both ways on the same grid, and `machines` at 1 / 100 / 500 pumps awake and
at 500 with every panel dry. The delivered figure is the number every pump cap in the ladder is
fiction until; the read's cost is what decides whether the active set is a design or a trigger
that fires on day one. Then `--network` at M10e: a 10,000-voxel line cut, a wired chunk bitten,
200 wired chunks arriving, a run at the bound ± 1 cell.

## The split

| Part | What | Verifiable |
|---|---|---|
| **M10a** | The store slice first and alone (schema 4, the versioned row blob, the batch write with the header), then the entity, the relay pump and outlet — M10-PLAN decisions 1–5 and 7 as amended, channel 0–15, the per-channel cursor, `links` beside `ports`, the 27-cell read, the active set | `pumped == emitted`; the world total constant over 10,000 boundaries; the reconcile both ways; a store that throws leaves the world byte-identical; `--pump`; the r6 control to the digit |
| **M10b** | The computer as an item — right-click on a machine opens ports and the stock tab; `SetPort` clamped in Core; weights | panel by screenshot; ports headless through `--port=`; a transfer round-trips a quit |
| **M10c** | Power on the channel class: pool = Σ storage, `clamped`, converters and storage kinds, solar quantised, the equipped pack at header v4, the tool's `power` block, `--nogate` | `pool == generated − drawn − clamped`; a bite that stops; `--weather=1` dims the panel by exactly (256 − byte)/256 |
| **M10d** | The play: the played mine pumped dry by relay; the storm; the gate at the bite; the pillar verdict; M9d's four questions | Matthew's sentence |
| **M10e** | The conductor index: `conductor` block, `ConductorTracker`, summaries, `NetworkSet`, position-derived ids, bounds → `overlength`/`overcap`, the rescan cap, the forget-path rescan, schema 5, `via: <class>`, `adjacent:`, `surround`; the energised bit and the cut's leak | labels equal a from-scratch flood after random edits, arrivals, forgets and reloads in both hook orders; a cut splits the pool into its rows' charges exactly; a cable through a chunk unloaded under `--vradius=1` keeps its circuit across a reload; `--network` |
| **M10f** | Generators, converters, the crank and the sink: `E` held with millis sampled, `burner`, the boiler's drink and `consumed` on every identity, the engine, `shaft`, media order, the dock, the flywheel, `pipe_outlet`, `flooded` | a boiler → engine → dynamo chain's identity with one-boundary lag per hop; the world total moves by exactly the consumed litres; a cranked pump moves `min(cap, 75 W × millis ÷ J/L)` to the millilitre at two outlet heights; a pack undocked gains nothing |
| **M10g** | Recipes, stations and parts, **and the inventory screen** (folded in 2026-09-18 — the section at the end of this document): the loader's second pass, `station` kind, stock, jobs by value, remainders, `unit`; one screen on `E` tapped holding the inventory grid, the hotbar as a nine-slot assignment in the header at v5, the placeable filter and the parts page as the grid's filter, the craft pane, the scaled unit; the relay's own recipe so M10a's seeding retires | a job survives a quit mid-way; a removed mod's job completes; five smelts yield one voxel and a printed remainder; player + stocks constant over a thousand random steps; `ShippedContentTests` finds every material and tag |
| **M10h** | Worldgen for the ladder — WORLDGEN-NOTES' own planning pass first, with its three questions answered: logs at the surface and planted saplings on a worker; layers; seams and veins by a second lattice field; iron under the table; ordered so tier 0 is playable first | generation ms against 0.47; volume per material per depth band; ore bit-identical across chunk faces; a log across a chunk face lights correctly; a played world opens with new strata below its dug region and its dug region unchanged |
| **M10i** | The content: five tiers of JSON and textures through the loader a mod uses; solar re-classed to `dc` | every station tag has a station, every class a conductor, some tier lifts 64 m, some pump beats dirt; a 25-cell copper run is dead and a 24 is live |
| **M10j** | The play: the ladder climbed once on a fresh world, timed against the targets; the five named moments as five sentences | Matthew's |

Each part wants its own planning pass against the code before it, as M9's did.

## Risks, stated

The feel ships last. Every pump's rate is fiction until `--pump` reports the automaton's
throughput — if a 3 × 3 × 3 intake delivers nearer 29 L/s than 300, the rate ladder collapses onto
one number and the ladder is head and power alone (which it mostly already is). The Debug boundary
is already the played build's worst frame and the tick lands on it. The active set's wake rule is
one recipe from a pump that never restarts — the fuzz carries the wake sources with a from-scratch
*would this pump have moved water* oracle. `consumed` is a new term on every identity and a boiler
defect hides in a count exactly as `displaced` once did. `E` carries two meanings and the computer
a third verb on right-click. Whole-voxel recipes are coarse and the wire mill's 1 : 64 hides it;
the first recipe that cannot fires the millilitre bump. Cable to rubble on a fall loses the copper — the right
punishment or a rage-quit, and only play says which; a salvage recipe is the lever. Laying 96
voxels of cable with Alt+right is tedium that can read as difficulty the wrong way; a one-cell
`line` brush is the lever. Three new Godot Controls (the machine panel; the inventory screen with
the craft pane and the parts filter inside it — four until 2026-09-18 folded the screen in; a crank
indicator) and the mouse-capture mode none can open without, against one today.

## What this changes in the other documents, once the decisions are taken

- M10-PLAN: decisions 1, 3, 4, 5, 6, 8, 9, 10 amended as above; 2, 7, 11 stand; its "deliberately
  not in M10" loses the pump ladder and crafting.
- DESIGN's *Automation*: *transmission is membership, never transport; a physical tier's cost is
  paid on the edit and capped per boundary*; the *pipes and conveyors* row splits — pipes are
  membership, conveyors are inserters; the M10 row becomes the ladder; the M11 row shrinks to
  drills, inserters and health.
- MATERIALS-NOTES: *copper — M10's power network is made of it* is true again at the dc tier as
  membership; brass with zinc; lithium as rock first; the mL move's timing.
- WORLDGEN-NOTES: its three questions answered, in Matthew's words; its pass is M10h's.
- M10-NOTES: header v4 is the pack's; the machine table is a schema bump; "no wires" is the
  quantum class's rule, and below it wires are membership.

---

## M10a — the planning pass

> 2026-09-15, night, after Matthew took the ladder's twenty-three decisions as recommended
> (*"As recommended, lets do M10a's planning pass"*). Read against `IWorldStore` and
> `SqliteWorldStore`; `ChunkPipeline.SaveAll`, `SubmitSaves`, `WriteWorldHeader`, `Dispose` and
> the three hooks; `Main.Autosave`, `_ExitTree` and the boundary block; `Place.Put` and the chunk
> fill's queue; `Carve.Take`; `Collapse`'s lift-and-return loop; `PortalDrain` and `SurfaceHandoff`
> (the two passes that already move litres between the tiers); `FluidSimulation.Add` / `Remove` /
> `Lift` / `Return`, `FluidGrid`'s panels and `Publish`; `LakeCeiling.InLake`;
> `WaterSystem.Draw` / `Receive` / `BasinAtSample`; `ContentLoader.LoadMaterials`; `VoxelChunk`'s
> palette surface. Six things the code said that neither plan had, twelve decisions, the format as
> it ships, the split into three sessions, and the tests by part.

### Six facts from the code

1. **An autosave is three store calls, not two.** `Main.Autosave` writes the header, then
   `WriteBasins` — its own delete-and-insert transaction — then `SaveAll`, which is chunks, columns
   and fluid panels in one transaction through `SaveWorldJob`. The ladder's decision 5 counted the
   header and the machine table; the basins are a third call, and a crash between the basin write
   and the batch already leaves whatever the portal drain moved in that interval in neither tier —
   bounded by the autosave interval, never priced, and about to gain a second path in each
   direction (a pump drawing a lake into Tier B; an outlet crediting the ledger). The quit is the
   same three calls in the same order with the fluid job's cancel between the second and the third.

2. **`Dispose` saves too.** `ChunkPipeline.Dispose` calls `SaveAll()` as the backstop before it
   cancels anything, and `_ExitTree` writes the session *before* disposing. So a session that rides
   the batch has to be handed in by the caller that has it — the pipeline knows nothing of headers,
   basins or rows — and the backstop must be able to run with none.

3. **The hooks are the wrong place for a write, and the two plans put writes in two of them.**
   `onVoxelsReady` fires inside `Complete` before the neighbour push — a throw there is M2c-2's
   stall, it runs mid-tick under `--fluidjob=1` (the M8d-5b lesson), and `onVoxelChanged` fires
   32,768 times a bite. M10-PLAN decision 4 has the reconcile *rewriting 64 voxels* at
   `onVoxelsReady` and the dismantle running "at once after a swing" from `onVoxelChanged`; both
   are `SetVoxel`, which re-enters the hook chain. Everything that writes today — the brush, the
   chunk fill, the fall — writes from the frame or the boundary, never from a hook.

4. **A fall and a swing write the same thing into a body.** `Collapse` writes air into a run's
   cells and rubble at the landing; `Carve.Take` writes air. The `onVoxelChanged` hook sees a body
   voxel become air in both cases and cannot tell which. So the ladder's reconcile clause — *credited
   unless any of its 64 is rubble* — is right for a short fall (rubble lands inside the cell) and
   wrong for a long one (the rubble ten metres down, the cell all air, the machine credited as if it
   had been mined). And a body falls *partly*: the at-risk set is per voxel and a fall is a column
   run, so a machine on a roof's edge loses the columns nearest the void and keeps the rest. What
   does distinguish the two is who credited: `Carve.Take` credits the inventory per voxel it
   removes; the fall credits nothing.

5. **The water table is a level, not a footprint** (M9d decision 1), so M10-PLAN decision 5's
   intake rule — *if the pump's sample's table stands above its top, draw from Tier A* — lets a pump
   in a dry pit beside a lake drink that lake through the rock at 100 L/s, where the seep index lets
   it in at 1.76. The question that means *the lake physically stands here* is
   `LakeCeiling.InLake(voxel)`, the hand-off's own rule for a submerged cell, and the live sim
   terrain has answered it since M8d-5.

6. **`BasinAtSample` folds "off the window" into `Watershed.Outlet`.** `Receive(Outlet, litres)`
   sends the litres out of the world — right for a sample that drains off the window's edge, wrong
   for one the window does not cover, which the ladder says stalls `unanalysed`. The unloaded-outlet
   rule needs a question `WaterSystem` does not yet ask.

Two smaller ones. `VoxelChunk`'s palette is `internal`, so the reconcile's O(palette) skip wants a
public `MayContain(materialId)` — an over-approximation, because `Compact` never runs and a
palette can name a material the chunk no longer holds, which costs a 32,768-voxel scan and can
never miss a body. And `FluidGrid.Panel.Held` is the per-panel wet count the ladder's decision 14
wanted `Publish` to cache: it exists, is maintained on every zero crossing, and wants a public
`HeldIn(chunk)`.

### Decisions

Each recommended and built as recommended unless Matthew says otherwise; each names what it amends.

#### 1. The batch is a record and the session rides it — *recommended; amends ladder decision 5*

`IWorldStore.Write(WorldBatch batch)`, where `WorldBatch` carries `Chunks`, `Columns`, `Fluid`
and three optional session parts — `Machines`, `Basins`, `Header` — null meaning *leave the table
alone*, an empty list meaning *clear it*. One SQLite transaction over all six. The two existing
`Write` overloads become sugar over it, so every caller written before this compiles.
`ChunkPipeline.SaveAll(SessionSnapshot? session = null)` puts the session on the batch when given
one; `SubmitSaves` (the unload path's asynchronous batch, one in flight) never carries one, and
`Dispose`'s backstop passes none. `Main.Autosave` and `_ExitTree` become one call each: bank the
ledger, then `SaveAll(new SessionSnapshot(headerBlob, ledgerEntries, machineRows))`, after the
fluid job's cancel at a quit because `SaveAll` refuses mid-tick. `WriteWorldHeader` stays for the
open, before any chunk, where the ordering argument holds; `WriteBasins` stays for the
lattice-changed clear at open. **The store slice's first test is a store that throws inside the
batch after the header** — a null blob among the machines — **leaving the header, the basins and
every chunk byte-identical**: SQLite by its rollback, the in-memory store by the pre-validation it
already does for the fluid list, and the contract suite runs both.

- **Rejected:** a fourth positional list and a seventh overload (the next part adds a fifth);
  machines alone in the transaction with the basins left out (they are the same pair — a debit on
  one side and a credit on the other — with litres instead of voxels, and a pump is the first
  thing to move litres across that crash window at a rate).
- **Cost:** the basin rewrite (tens of thousands of rows on a travelled world, M7b-2's stated cost)
  moves inside the chunk transaction; the autosave line prints `session` when it wrote one.

#### 2. The machines table: keyed by origin voxel, the material as a column, one versioned blob; replaced whole — *recommended*

Schema 4: `machines (ox, oy, oz, material, blob) PRIMARY KEY (ox, oy, oz)`, STRICT, gained by an
older file on open as the fluid table was. Written as the basins are — delete-and-insert inside
the batch — because the set in memory is the truth and a dismantled machine has to leave the
disk. `MachineRowBlob` v1 in `BlobFormat`'s frame: a facing byte (`BrushFacing`, from
`Place.FacingFrom` at placement — unused by either M10a kind, carried because sub-quantum machines
have faces and the format ships whole); a port count; `(name, int64)` pairs **by name, never by
declaration order**, because a mod that reorders its ports would rebind every stored value
silently; then a length-prefixed kind-state with its own version byte — empty for both M10a kinds,
carried opaque for a kind this build does not know, so a removed mod's row survives the way its
material hole does. At read: a port the definition does not declare is dropped and printed; a
declared port missing takes its default; a value outside its bounds is clamped and `clamped`
counted — decision 21's rule, a row is a caller. A material ID that is a hole keeps its row inert
and its body `Unknown`, exactly as a chunk's palette hole does. Rows are hundreds; a name is eight
bytes. `IWorldStore.ReadMachines()` reads the table whole at open beside the basins.

#### 3. Hooks queue and frames write — *recommended; amends M10-PLAN decision 4*

`onVoxelsReady(chunk)`: if the set has rows in the chunk, or `MayContain` says a machine material
might be in it, add the chunk to an arrivals list — O(palette), no voxel read, no write.
`onVoxelChanged(voxel, material)`: `if (rowsByChunk.Count == 0) return;` then one dictionary
lookup on the chunk and one on the cell; a voxel inside a row's cell whose new material is not the
body's bumps that row's `Lost` — two lookups, 32,768 times a bite is under a millisecond, and a
world with no machine pays a count check. `Machines.Drain()` runs in `_Process` after
`_pipeline.Update`, every frame, O(1) when both lists are empty: for each queued chunk still
loaded — a chunk can leave between the hook and the frame, so it is asked again, and one that
left is dropped and queued afresh by its next arrival — the reconcile; for each row with `Lost >
0`, the dismantle. Both write through `SetVoxel` from the frame, where the brush writes; neither
needs the boundary, so both run under `--nowater`. A body rebuilt at arrival lands one frame after
the chunk does, which is before it can mesh, since nothing meshes until its neighbourhood is lit.

#### 4. The credit rule is `Lost == Carved`; `Carve.Take` tells the set what it took — *recommended; replaces the ladder's rubble clause and amends M10-PLAN decision 4*

`IMachineBodies.Taken(voxel, material)`, an optional parameter on `Carve`'s four entry points
threaded to `Take`, called after the inventory credit when `materials.IsMachine(material)` — one
`bool[]` read per removed voxel for ordinary mining. A row counts `Lost` from the hook and `Carved`
from `Take`. At the drain, **`Lost == Carved`**: a pick dismantled it — the remaining `64 − Lost`
body voxels are written to air and credited, the row removed, and a swing that took 17 ends with
64 in the inventory; **`Lost > Carved`**: something other than a pick took voxels — in M10a only a
fall can — and the remainder is written to air *without* credit and the row removed: a machine on a
roof that comes down is destroyed whether it fell whole or half and however far the rubble went,
and the voxels the player mined off it first are theirs. `Q`'s cube through `Take` is 64 carved in
one click; Alt's chip is 1 carved and 63 credited by the drain. The fall keeps not knowing a
machine exists.

**The reconcile at arrival follows the same logic without the counters**, because a chunk written
at an unload never holds a *partial* body from a swing — the dismantle runs the same frame: all 64
present → nothing; none present → rebuilt from the row into air, or, if the cell is not all air
(the chunk regenerated as rock because its write was lost), dismantled *with* credit, because the
row says the player owns it; some present → destroyed without credit, since only a fall leaves a
partial body on disk; a machine-material voxel no row covers → air without credit, the orphan of a
crash before the row's autosave. A rebuild into a cell the water has since entered displaces it,
counted, the fall's stale case. Every arm prints on the `M10:` line.

- **Rejected:** a flag around `SweepCollapse()` (a bracket the bench's `--collapse` arm would not
  set, and the next fall path would not either); the fall telling the set (it can stay ignorant
  because the counters carry what it would have said); the rubble clause (fact 4).

#### 5. The lake intake is `InLake`; whole litres cross a tier; a slow pump keeps an owed counter that is not water — *recommended; amends M10-PLAN decision 5 and ladder decision 12*

The pump asks `terrain.InLake(the cell above its top)` on the live sim terrain: true → Tier A,
`WaterSystem.Pump(basin, litres)` on the basin under its sample; false → Tier B, the box. Across
tiers only whole litres move — a Tier B cell holds 15,625 mL, so the portal drain's rule holds
here and a pump into an unloaded surface outlet or a lake column takes `floor(mL ÷ 1,000)` litres
with no pending anywhere; within Tier B millilitres move. A pump under 2 L/s on a lake accumulates
`owed += rate × 500` mL a boundary and draws a litre when owed reaches 1,000 — a *want*, not
water, so it is not persisted and a quit loses a fraction of one boundary's pumping, never a litre.
The relay never uses it; a 0.47 L/s fixture does, for the test. `WaterSystem.TryBasinAtSample(sx,
sz, out basin)` answers false off the window (fact 6), so `unanalysed` and *drains off the edge*
are two answers.

#### 6. Two twins on each tier — *recommended; M10-PLAN decision 10's terms on `HandOff`'s shape*

`FluidSimulation.Pump(voxel, mL)` is `Take` with `PumpedMillilitres`; `Emit(voxel, mL)` is `Add`'s
path with `EmittedMillilitres`; `FluidPublished` gains both and `IdentityExact` reads them.
`WaterSystem.Pump(basin, litres)` / `Emit(basin, litres)` beside `Draw` / `Receive` with
`PumpedLitres` / `EmittedLitres`, so `PortalDrain`'s *drawn mL equals `DrawnLitres` × 1,000, always*
keeps being true. Cross-tier, on the `M10:` line: `pumpedB + 1,000 × pumpedA == emittedB + 1,000 ×
emittedA`. Rejected: `Remove` / `Add` with the lines decomposed — Alt+R and M10f's boiler would
share `removed`, and `drawn` would stop meaning the portal.

#### 7. Every row ticks every boundary with the cheap read; the active set is a lever — *recommended; amends ladder decision 14*

Per pump: up to eight `IsLoaded` on the box's chunks — any unloaded and it stalls `unloaded`,
nothing partial (an unloaded panel reads *dry*, the conditional no that froze the water once); up
to eight `HeldIn` — all zero and it stalls `no water` having read nothing else; then `AmountAt`
over the 26 cells' voxels in `[z][x][y]` order, face cells before edges before corners, which is
nearest-first at no sort. Five hundred pumps over dry panels are 8,000 dictionary lookups, ~0.2 ms
Release (estimate; `--pump` prices it at 1 / 100 / 500). The sleep-and-wake set — whose wake rule
the ladder itself called *one recipe from a pump that never restarts* — is not built; the trigger
is `machines` over 1 ms on the `boundary` line on the played world.

#### 8. The tick, exactly — *recommended*

In the `machines` bracket between `RunPendingChunkFills()` and `CheckTable()`, the grid the frame's:

1. **The deferred wet placements** (decision 10): `Lift` the cell's wet voxels, write the body
   through `Place.Machine`'s write pass, `Return` bottom-up over the 26 cells' voxels — Y ascending,
   then x, then z, so the water goes where a submerged body sends it, up — and `DisplaceLifted` the
   remainder, printed; `lifted == returned + displaced` on the line. Into a full box under a roof
   that is 64 × 15,625 mL displaced, exactly, which is the honest case and a test.
2. **Per channel**, the outlets sorted (x, y, z), rebuilt when the set's version moved.
3. **Per pump** in (x, y, z) order: budget `L/s × 500` mL; the intake as decision 7 (or the lake as
   decision 5); offer = min(budget, what is there). Then from the channel's cursor over its
   outlets: room is — loaded and not in a lake — Σ `FreeAt` over the 16 cells of the layer above
   the top face where `CanHold`; — in a lake, or unloaded with the outlet's origin at or above the
   seed surface and its sample inside the window — whole litres to the basin; — unloaded
   underground, or off the window — zero. `take = min(room, offer − moved)`; the intake is drained
   by exactly `take` (Tier A may hand back fewer whole litres, and then it is exactly what it
   handed); the outlet is given exactly that; an `Emit` refused for what `FreeAt` promised throws,
   the portal drain's rule — the wiring would be the thing that is wrong. The cursor advances once
   per pump that moved anything. Stalls are named: `no outlet`, `no water`, `no room`, `unloaded`,
   `unanalysed`. Two pumps on one channel reach two outlets in one boundary because the cursor is
   the channel's; an outlet whose layer sits inside a pump's own box recirculates, which is a loop
   the player laid and not a rule's defect; the round-robin is plain and `weight` waits for M10b.

`pumped == emitted` holds per pump per boundary by construction — the room is read, then exactly
that much is moved end to end — and the world's total is a constant of the machine layer.

#### 9. The cursor is the channel's, in memory, reset at open — *recommended*

Sixteen integers nobody persists. Determinism is *within a session from the same open*, which is
what the tests and the bench assert; after a reopen the first pour goes to the first outlet in
position order. Cost: one boundary's litres, once, to a different outlet on the same channel.
Rejected: a seventeenth kind of thing in the machines table.

#### 10. A machine material places as one cell whatever the brush, mode or modifier; `P` seeds; the hotbar prints `× N` — *recommended; M10-PLAN decisions 2 and 7 made concrete*

`PlaceController`: `materials.IsMachine(MaterialId)` → `Place.Machine(world, materials, cell,
material, facing, machines, body, inventory)` at `TargetCell` — the brush, `--entanglechunk`'s
chunk and Alt's voxel all mean one cell, the HUD says `MACHINE`, and `Q` places at range.
`Place.Machine` checks all 64 (air, not wet on the published copy, not inside the body, 64 in
stock), then writes all or none and creates the row on success — one Core call, so no caller can
split the body from the row, and a `SetVoxel` that fails mid-way rolls the written voxels back and
reports `Unreachable`. A wet answer returns 64 `Wet` and the controller queues `(cell, material,
facing)` to the boundary — the chunk fill's list, decision 8's step 1. A cell under a lake's plane
is `LakeCeiling`'s and holds nothing, so a pump on a lake bed places at once. `P` puts 64 voxels
of `relay_pump` and 64 of `relay_outlet` in the inventory, printed — the debug bind beside `R`,
retired by M10g's recipe. `unit: cells` prints `× 1`; a holding under 64 (half a body after a
fall) prints `× 0 (17/64)` and places nothing.

#### 11. The loader takes the whole `machine` grammar and refuses a `via` this build does not serve — *recommended*

`kind` ∈ {`pump`, `outlet`} here; `generator`, `converter`, `storage`, `station` are refused as
*not in this build — M10c / M10g*, naming the part. `via: channel` is served; `crank`, `burner`,
`solar`, `surround`, `adjacent:` are refused the same way (M10f), and any other string as *a
conductor class — M10e*. Every rule the format section lists holds: a `channel` link needs a
`channel` port, ≤ 16 ports, `min ≤ default ≤ max`, port names in the identifier charset, a pump
declares a fluid `source` link and an outlet a fluid `sink`, `intakeRadiusCells` 1..2,
`litresPerSecond` > 0 and ≤ 54,000, `headMetres` null or ≤ 1,024, `joulesPerLitre` ≥ 0 (carried,
unread until M10c), a conductor block on a machine refused. `unit` is `litres` (default), `count`
or `cells`. `MaterialTable` projects `IsMachine` and `Machine(id)`. Refusal rather than a lamp
that does not glow: M5b shipped one for a day inside one milestone; a mod written for M10f that
loads on M10a and ticks nothing is the same state against a format mods will write to.

#### 12. Three sessions: a-1 the store, a-2 the entity, a-3 the pump — *recommended*

**M10a-1, the store slice, first and alone:** `WorldBatch` and `SessionSnapshot`, the six-list
write on both stores, `ReadMachines`, schema 4, `MachineRowBlob`, `MachineSet` as a set of rows
with `Snapshot()` and `Restore()`, `SaveAll(session)`, `Main`'s autosave and quit on one call, the
`M10:` line's `machines 0` and `session` on the autosave line. No placement, no tick; rows exist
only through tests. **M10a-2, the entity:** the loader's `machine` block and `unit`, the two JSON
files and two `LayerColour` entries, `MaterialTable.IsMachine`, `VoxelChunk.MayContain`,
`Place.Machine` instant and deferred, `IMachineBodies` on `Carve`, the three hooks and the drain,
the reconcile and the dismantle, `P`, the hotbar's `× N`, `MACHINE` on the HUD, `--abort=`. **M10a-3,
the pump:** `Pump` / `Emit` on both tiers, `HeldIn`, `TryBasinAtSample`, `MachineTick`, the
cursor, the unloaded-outlet rule, `--pump=` and `--pumpoutlet=`, the `machines` bracket, the full
`M10:` line and the profile block, `--pump` on the bench, the lifecycle battery with machines
running. Each is Core first against the tests named below, then the wire; a-1 alone because a
defect there is a machine that vanishes with its config.

### The format, as M10a ships it

```json
// mods/overburden/materials/relay_pump.json
{ "id": "relay_pump", "hardness": 3.0, "supportDistance": 8, "density": 7800, "textureLayer": 5, "unit": "cells",
  "machine": { "kind": "pump", "litresPerSecond": 100, "headMetres": null, "joulesPerLitre": 1000, "intakeRadiusCells": 1,
    "links": { "power": { "carries": "power", "role": "sink",   "via": "channel", "port": "channel" },
               "water": { "carries": "fluid", "role": "source", "via": "channel", "port": "channel" } },
    "ports": { "channel": { "default": 0, "min": 0, "max": 15 }, "weight": { "default": 1, "min": 1, "max": 100 } } } }

// mods/overburden/materials/relay_outlet.json
{ "id": "relay_outlet", "hardness": 3.0, "supportDistance": 8, "density": 7800, "textureLayer": 6, "unit": "cells",
  "machine": { "kind": "outlet",
    "links": { "water": { "carries": "fluid", "role": "sink", "via": "channel", "port": "channel" } },
    "ports": { "channel": { "default": 0, "min": 0, "max": 15 }, "weight": { "default": 1, "min": 1, "max": 100 } } } }
```

Hardness 3.0, not the ladder's 5.0: `steel_pick`'s threshold is 3.5 and a body nobody can mine is
a body nobody can dismantle — the 5.0 assumed the entangled miner's own threshold, which is M10c's
tool block. The `power` link is parsed, validated and unread until M10c. `joulesPerLitre` likewise.
`weight` is stored on the row and unread until M10b.

### Persistence, exactly

Schema 3 → 4, the `machines` table above; header v3 unchanged (v4 is M10c's pack). `MachineRowBlob`
v1: frame; facing (byte); port count (byte); ports as (length-prefixed UTF-8 name, int64); kind
state as (int32 length, bytes) whose first byte is its own version, empty here. Read at open, whole,
into `MachineSet`; written whole on every session save. The world the played `default` is holds no
rows; opening it stamps schema 4 and reads an empty table, and a v3 build opening a schema-4 file
refuses (`user_version` 4 > 3), which is `downgraded-world-v3-header.bin`'s shape a second time and
gets its own fixture, `downgraded-world-schema-4.db`.

### The `M10:` line, the profile, and the flags

The periodic `M10:` line: `machines N (P pump(s), O outlet(s))`; `pumped B / A` and `emitted B / A`
in the window and in all; `cross-tier exact`; stalls by cause; `placed / deferred / lifted /
returned / displaced`; `dismantled (V credited) / destroyed`; `reconciled: rebuilt / returned to
the inventory / destroyed / orphans cleared / chunks skipped by palette`; `clamped port(s)`; the
tick's mean and max ms. The `boundary` line gains `machines`; the profile gains a `machines` block
with the same terms and both tiers' identities read with the new terms. `M10: at open` prints the
rows read and any clamped. Flags: **`--pump=<L/s>`** places a pump at the bottom of the shaft
`--breach` bites at the basin's pit and an outlet on the surface **`--pumpoutlet=<metres>`** off
(24 by default, inside the r6 box; 64 is outside it, the unloaded arm), on channel 0, at the
window's open, so `--fillbasin=2000000 --breach=3 --pump=100 --measure=60` floods the shaft and
pumps it back out in one run; **`--abort=<seconds>`** ends the process with no save at all, the
crash arm — the reconcile's orphan direction headless is `--pump=100 --speed=12 --autosave=100
--abort=12 --world=probe`: the chunk unloads and is written with the body, the row never lands,
and the reopen prints `orphans cleared 128`. The other direction (row on disk, body gone) needs a
dismantle mid-run and is the pipeline test's alone.

### The bench: `--pump`

The chamber fixture flooded to rest, a pump on its floor's centre and an outlet on the surface
above (`DugTerrain.CanHold` answers for air above the seed), both bodies solid to the terrain; per
boundary: offered, room, moved, delivered L/s against the nominal, at `intakeRadiusCells` 1 and 2;
then boundaries to empty and the active-set peak with the pump running. **The delivered figure is
the number every pump cap in the ladder is fiction until.** Beside it the read priced both ways on
the same grid (the box through `AmountAt`; the naive published-copy walk), and `machines` at 1 /
100 / 500 pumps and at 500 with every panel dry. The loop arm — the outlet inside the chamber —
asserts the world's total unchanged to the millilitre over 10,000 boundaries. Release only, like
its siblings; `--pumplitres`, `--pumprate`, `--pumpboundaries` size it.

### The tests, by part

**a-1.** Contract suite, both stores: `MachinesRoundTripWholeAndKeyedByOrigin`;
`WritingTheMachinesReplacesTheTable` (fewer rows the second time deletes);
`ASessionlessBatchLeavesTheMachinesBasinsAndHeaderAlone`; `AllSixListsLandInOneWrite`;
**`AWriteThatFailsOnItsMachinesLeavesTheHeaderBasinsAndChunksUnchanged`** — the first test.
`MachineRowBlobTests`: round trip; truncation refused by the frame; an unknown version refused; an
undeclared port dropped and counted; a missing port defaulted; an out-of-bounds value clamped and
counted; an unknown kind's state byte-identical through a round trip. `PersistenceTests`:
`SaveAllWithASessionWritesItInTheSameTransaction`; `TheDisposeBackstopCarriesNoSession`;
`AFailedSessionSaveKeepsThePanelsAndTheChunksPending` (the `ThrowingStore` fixture, which the
`WorldBatch` change touches — every `InMemoryWorldStoreBase` override moves to the record). The r6
control to the digit.

**a-2.** `ContentLoaderTests`: one refusal per rule in decision 11, each naming the file, and a
`via: crank` refused as *M10f*; `ShippedContentTests` finds both bodies, `IsMachine`, `unit:
cells`, and still finds a tier that leaves the hardest material standing. `PlaceMachineTests`: a
rock voxel among 64 → nothing written, no row, no debit; inside the player → the same; 63 in stock
→ `OutOfStock` 64; a wet voxel → `Wet` 64 and nothing written; 64 placed ⇔ a row exists; the
deferred path lifts, writes, returns bottom-up with `lifted == returned + displaced` and the
identity exact; into a full box under a roof `displaced == 64 × 15,625`; a lake column places at
once. `DismantleTests`: swing 17 → 64 in the inventory, row gone, cell air; chip 1 → 64; `Q`'s cube
→ 64 by the carve and 0 by the drain; a fall of half → row gone, credit 0 past the carve's; a fall
then a swing in one frame → only the swing's voxels; the hook never writes (a counting editor
asserts zero `SetVoxel` inside `Complete`). `ReconcileTests` through the pipeline harness: absent
→ rebuilt; absent into rock → dismantled with credit; partial → destroyed uncredited; orphan →
cleared uncredited; a chunk with no machine in its palette never scanned (counted); a chunk that
left before the drain not written and queued again at its next arrival; the cache revival path
reconciles too (the third arrival path).

**a-3.** `MachineTickTests`: B → B `pumped == emitted` per boundary and the world's total constant
over 10,000 boundaries with a loop; A → B whole litres, `pumpedA`, and `PortalDrain`'s equality
still true beside it; B → A to an unloaded surface outlet inside the window → `emittedA`, whole
litres, nothing pending; unloaded underground → `unloaded`; off the window → `unanalysed` where
`BasinAtSample` reads `Outlet` (the test that separates fact 6's two answers); `no outlet`, `no
room` (a roof), `no water`; two outlets cycle one per boundary; two pumps on one channel reach two
outlets in one boundary; a pump on channel 1 ignores channel 0's outlets; the 0.47 L/s fixture
draws a litre every fourth or fifth boundary and never more than owed; **the pit beside the lake
pumps nothing** where `TableAt` stands over it (fact 5); a box straddling an unloaded chunk stalls
whole; the same set from the same open twice lands the same millilitres in the same cells.
`FluidLifecycleTests` with machines running — pour, pump, walk away, walk back, quit, reopen —
both identities exact and the cross-tier equality at every boundary. Headless: `--pump=100` with
the pour and the breach, `--pumpoutlet=64`, the `--abort` orphan, and the r6 control.

### What this pass amends

- Ladder decision 5 (the basins join the transaction; the batch is a record), 12 and 14 (`InLake`;
  the active set is a lever), and the reconcile rule under *Persistence* (the counters replace the
  rubble clause). Decision 4's *the forget path rescans* is untouched — it is M10e's.
- M10-PLAN decision 2 (one cell whatever the brush; the deferred path returns bottom-up over the
  box), 4 (hooks queue, frames write; `Lost == Carved`), 5 (`InLake`; whole litres across tiers;
  `unanalysed`), 7 (`P`; hardness 3.0), 10 (the twins), 11 (the split into three).
- TODO's M10a item becomes the three-part checklist; CLAUDE.md's pick-up points at a-1.

### M10a-1 — built

> 2026-09-15, night, alone as planned. Suite 1,412 → 1,469; fifteen break-recipes, fifteen caught
> once one fixture was fixed; the r6 control unchanged to the digit; a copy of the played world
> opened at schema 3, gained the table and lost nothing.

**What is there.** `WorldBatch` is the store's one write: chunks, columns, fluid, and three
optional session parts — `Machines`, `Basins`, `Header` — null meaning *leave it*, empty meaning
*clear it*, one SQLite transaction over all six with the header upserted and the two tables
deleted and re-inserted inside it. The list-form `Write`s are sugar over it on the interface and
on both stores, and `WriteWorldHeader` and `WriteBasins` are the batch underneath, so a store
cannot grow two header paths that disagree. `ReadMachines` reads the table whole. Schema 4:
`machines (ox, oy, oz, material, blob)`, gained on open by a schema-3 file. `MachineRowBlob` v1 —
facing, ports by name in ordinal order, a length-prefixed kind state carried opaque — with a
reader that refuses a wrong key, an origin off the brush lattice, a facing that is not one, a
seventeenth port, a name outside the charset, a name twice, and bytes after the state inside an
honest frame. `PortDefinition`, `MachineRow` and `MachineSet` in `Overburden.Core/Machines/`: the
set is rows only, keyed by cell and indexed by chunk, `Snapshot()` in origin order so an unchanged
world writes the same table twice, `Restore()` dropping, defaulting and clamping against the ports
a material declares — each counted, none refused — and keeping a row of unknown kind verbatim.
`SaveWorldJob` carries a `SessionSnapshot`; `ChunkPipeline.SaveAll(session)` writes it in the
batch and writes even when nothing else is dirty; `Dispose`'s backstop carries none; `SubmitSaves`
never does. `Main`: `Session()` banks the ledger and builds the three, `SetUpMachines` reads the
table at open (`M10: at open — …`), the autosave is one call and prints every time with what it
carried, the quit writes the session with the chunks after the tick in flight is dealt with, and
the periodic `M10:` line prints the count and the session saves.

**The first test, named in the pass, is
`AWriteThatFailsOnItsMachinesLeavesTheHeaderBasinsAndChunksUnchanged`**, in the contract suite so
both stores run it: a batch carrying a header, basins, a chunk and a machine list whose last blob
is null must leave everything the previous batch wrote — SQLite by rollback, the in-memory store
by validating every entry before keeping any. Committing the header outside the transaction fails
it; skipping the in-memory validation fails it.

**Four departures from the pass, each small.** `SessionSnapshot.Basins` is nullable, because a
`--nowater` run has no ledger and an empty list would have cleared the stored basins on its first
autosave. `ChunkPipeline.WriteSession` exists: fact 2 said `Dispose` saves too, and the quit path
has a case fact 2 did not reach — a fluid tick that did not report within its cancel timeout
leaves the grid a worker's and `SaveAll` refuses, so the session goes alone there, as the quit
wrote it before the two were joined, printed. The quit's order moved: the session was written
before the tick's cancel and is written after it now, so the tick's collection throwing on the
way out (caught, printed) no longer separates the two halves. And `StoredMachine` compares its
blob by bytes, because a record struct over an array compares references and two reads of one row
are one row.

**One recipe was an identity against its fixture and the fixture was wrong.** *Trailing bytes
accepted* — the reader's check that nothing follows the kind state — stayed green with the check
removed, because the test appended a byte to the *blob* and the frame's stored-length check
refused it first: the corruption test that trips an earlier guard than the one it names. The
test now hand-builds a payload with a byte after the state and a frame that declares it honestly,
and the recipe is caught. The other fourteen — the header outside the transaction, no
pre-validation, machines merged rather than replaced, basins left alone on a session write, a
quiet `SaveAll` swallowing the session, the machines never reaching the batch, the header in a
second call, `Dispose` clearing the machines, no clamp, a merging restore, an unsorted snapshot,
an unknown kind's ports dropped, duplicate names accepted, ports by insertion order — were caught
first time.

**Measured.** The r6 control, verbatim, is 2601 / 325 / 3446 at 411 with `M10: 0 machine(s), 0
session save(s)` between its lines. A fresh world's autosave with the session is 14.3 ms; a copy
of the played world (schema 3, 119 chunks, 615 basins, 207 fluid rows) opens, reads
`0 machine row(s)`, autosaves `48 fluid panel(s) and the session (615 basin(s), 0 machine(s))`
in 99.5 ms — the basin rewrite was inside the old bracket too, so the figure is comparable, and
it is the cost M7b-2 stated — quits, and reads back at `user_version 4` with every count intact.
A schema-4 world opened by a schema-3 build refuses on `user_version`, the mechanism
`ANewerSchemaVersionIsRefusedRatherThanOpened` pins; no fixture file was made, because a file
this build writes and an older build refuses is that test.

**Next: M10a-2, the entity**, against decision 12's list — the loader's `machine` block, the two
JSON files, `Place.Machine`, `IMachineBodies` on `Carve`, the three hooks and the per-frame
drain, `P`, the hotbar, `--abort=`. `MachineSet` gains the placement, the damage counters and
the drain there; `SetUpMachines` hands `Restore` the material table's declarations.

### M10a-2 — built

> 2026-09-15, night, after a-1. Suite 1,469 → 1,519; seventeen break-recipes, seventeen caught
> once one fixture was fixed; the r6 control unchanged to the digit; the orphan direction of the
> reconcile seen headless on a scratch world through `--abort`.

**What is there.** The loader reads the whole `machine` grammar (`MachineDefinition`,
`MachineLink`, `MachineKind` in `Overburden.Core/Machines/`; `MaterialDefinition.Machine` and
`Unit`; `MaterialTable.IsMachine`, `Machine(id)`, `MachineIds`, `Unit(id)`) and refuses every rule
in decision 11 naming the file — a `kind` this build does not serve naming M10c or M10g, a `via`
it does not serve naming M10f or M10e, a channel link with no declared port or with a face, a
pump with no fluid source or an outlet with no fluid sink, every number outside its bound, a port
named outside the charset, a seventeenth port, a fluid with a machine block, a `unit` that is not
one of three. `MachineDefinition.TryValidate` holds the rules in one place and the table refuses
to project a block that fails them, so a definition built without the loader is held to the same
ones. `relay_pump.json` and `relay_outlet.json` ship at hardness 3.0 with `unit: cells`, two
`LayerColour` entries and — found by the r6 control throwing on the first run — two jitter entries,
now asserted against each other. `VoxelChunk.MayContain`. `Place.Machine`: the whole cell checked
first in the brush's order of refusals, whole in one bucket, then 64 writes with a rollback and the
row made in the same call; `Place.MachineDisplacing`: the water lifted, the body written, the
water returned bottom-up over the box, the rest displaced, `lifted == returned + displaced` on
the result. `IMachineBodies` threaded through `Carve`'s nine entry points to `Take`, called after
the credit for a machine material. `MachineSet` gained the hooks and the drain: `ChunkArrived`
queues (rows in the chunk, or a palette that might hold a machine material, edited chunks only),
`OnVoxelChanged` bumps `Lost`, `Taken` bumps `Carved`, and `Drain` reconciles every queued chunk
(whole → nothing; absent → rebuilt into air or returned to the inventory when the cell is rock;
partial → destroyed; an orphan voxel → air) and dismantles every damaged row (`Lost == Carved`
credits the remainder, `Lost > Carved` destroys). `PlaceController` places a machine material as
one body whatever the brush, mode or modifier, queues a wet refusal to the boundary and runs it
there on the displacing path; `Main` wires `onVoxelsReady` for the first time, drains once a
frame after the pipeline's update, runs the deferred placements beside the chunk fills, seeds on
`P`, prints `MACHINE` and `× N` on the HUD, and carries `--abort=`, `--standfor=` and
`--material=`.

**Three departures from the pass.** `--standfor=<s>` and `--material=<id>` exist: the pass
named `--abort=` as the crash arm, and the first run said the arm could not be reached — `--place`
aims level while the flythrough moves and places nothing (M6d-2), so a run that wants a body
placed *and then* its chunk unloaded has to stand first, and `--stock=` seeds every material alike
so the controller's largest-holding pick lands on grass. Both are `--sprintfor=`'s and `--brush=`'s
shape. The jitter table is the third: not a departure from the pass but from the plan of never
having to think about it — `LayerColour` had a sixth and seventh entry and `Create` indexed a
five-entry jitter array by layer, and the r6 control threw `IndexOutOfRange` before its first
frame. Asserted now.

**Two recipes were identities against their fixtures.** *No rollback on a failed write* stayed
green because the fixture refused every write in the chunk, so the *first* write failed and there
was nothing to roll back; `TestWorld.RefuseWrite(voxel)` refuses one voxel deep inside the cell
now and the test asserts more than one write happened before the refusal. And the drain test's
fall-then-swing fixture carved `Cell.VoxelAt(4, …)` — the next cell over — for its fifth voxel,
which was air already: a fixture error the assertion caught on the first run, not a hole. The
other sixteen — a fall credited, a swing not credited, a partial body rebuilt, orphans unscanned,
every edited arrival queued, a body write counted as a loss, damaged rows before arrivals, the
carve silent, occupied not refused, the row before the writes, the water returned top-down, the
remainder undisplaced, the wet check dropped, an invalid block projected, every via served, a pump
with no source — were caught first time.

**Measured.** The r6 control, verbatim, 2601 / 325 / 3446 at 411. The orphan direction headless:
`--profile --speed=12 --standfor=5 --pitch=-60 --place --stock=64 --material=overburden:relay_pump
--world=probe --autosave=100 --abort=30` stands, places a pump and — `--stock=` having seeded the
outlet too — an outlet on top of it, flies off, writes four edited chunks at the unload and no row,
and ends at 30 s; the reopen reads `0 machine row(s)` and its first drain prints `2 reconciled,
128 orphan voxel(s) cleared, 2 skipped by palette` in 22.8 ms — two chunks scanned at 32,768
voxels and 128 writes through the hook chain, once. The other direction (a row on disk, the body
gone) is the pipeline test's alone, as the pass said: nothing headless dismantles a body without
`--mine` also digging the ground from under the next placement.

**What the tests say.** `MachineContentTests` (10), `PlaceMachineTests` (15, one against the real
`FluidSimulation` with `FluidAssert.Conserves`), `MachineDrainTests` (19), `MachinePipelineTests`
(4: the generate path rebuilds, the cache path leaves whole, the swing through the pipeline
dismantles, an orphan on the generate path clears), `ShippedContentTests` gained the pair, and
`CollapseTests.AnOldWorldsTableGainsRubbleAsTheNextIdAtOpen` was amended — the played world's v3
header gains three IDs now, in directory order, so *rubble is next* became *the saved prefix is
untouched and every new material follows it*.

**Next: M10a-3, the pump** — `Pump`/`Emit` twins on both tiers, `HeldIn`, `TryBasinAtSample`,
`MachineTick` in its `machines` bracket, the cursor, the unloaded-outlet rule, `--pump=` and
`--pumpoutlet=`, the full `M10:` line and profile block, `--pump` on the bench, the lifecycle
battery with machines running.

### M10a-3 — built

> 2026-09-16, small hours, after a-2 (*"As recommended, lets build a-3"*). Suite 1,519 → 1,551;
> nineteen break-recipes, nineteen caught first time; the r6 control unchanged to the digit; the
> shaft pumped out headless in both arms, the loaded outlet and the unloaded one; the bench's
> `--pump` has its first number.

**What is there.** `FluidSimulation.Pump` / `Emit` beside `Lift` / `Return` in the collapse
partial — `Take`'s and `Add`'s paths with their own counters, `PumpedMillilitres` and
`EmittedMillilitres` — and `FluidPublished` carries both, so the published identity reads `held ==
injected + drawn + seeped + loaded + emitted − removed − displaced − handed − evicted − pumped` and
`FluidAssert.Conserves` reads the same. `WaterSystem.Pump` / `Emit` beside `Draw` / `Receive` with
`PumpedLitres` / `EmittedLitres` (an emit to `Outlet` spills, as a receive does), and
`TryBasinAtSample`, which answers false off the window and true-with-`Outlet` for a sample the
window says drains off its edge — fact 6's two answers, kept apart. `FluidGrid.HeldIn(chunk)` is the
panel's own count. `MachineRow.OwedMillilitres` is the slow lake pump's want, never persisted.
`MachineTick` in `Overburden.Core/Machines/`: built over the set, the table, the simulation, the
*live* simulation terrain, the seed surface and Tier A; `Run()` once a boundary walks the pumps in
origin order, each against the outlets on its channel in origin order from the channel's cursor,
and returns a `MachineTickReport` — pumps, outlets, moves, pumped and emitted on each tier, the
five stalls — whose `Exact` is `pumped == emitted` for the boundary; `CrossTierExact` is the same
over the lifetime. `Main`: the `machines` bracket between the pour and the table check holds the
deferred wet placements and the tick, its own term on the `boundary` line and `boundary worst`;
`--pump=<L/s>` places a relay pump at the bottom of `--breach`'s shaft and a relay outlet
`--pumpoutlet=<m>` along +X on the surface — through `Place.Machine` where the outlet's chunk is
loaded, the cell carved first because a body is 64 air voxels and the shaft is three wide, and as
a row alone where it is not, printed either way; the periodic `M10:` line gained `tick:` with both
tiers' pumped and emitted, the cross-tier identity, the stalls by cause, the intake voxels read and
the tick's mean and max; the profile gained `machines` and `machtick`; the Tier A water line prints
`pumped out of lakes` and `emitted into basins`. The bench's `--pump`: the chamber fixture flooded
and settled, a relay pump on its floor's centre, a relay outlet on the surface with a sink under
it standing in for the hand-off; the delivered rate at radius 1 and 2, the read priced both ways,
the tick at 1 / 100 / 500 pumps wet and 500 dry, and the loop; `--pumplitres`, `--pumprate`,
`--pumpboundaries`.

**The tick, as built, where it departed from decision 8.** Two things. The unloaded-outlet rule
reads the *mouth*, not the origin: a body placed on a surface that is not on the 1 m lattice sits
up to three voxels into it (the `--pump` arm carves its cell; a brush refuses one and lands the cell
above), and the first unloaded run stalled every boundary on an outlet whose origin was at −4 over
a surface at −2 with its mouth at 0 — the rule is *the layer the pour lands in is natural air over
the seed*, `mouth.Y > SurfaceHeightAt`, and `AnUnloadedOutletHalfBuriedInTheGroundStillStandsOnIt`
is its test. And a pump's rate can be overridden: `MachineTick.RateOverrideMillilitresPerSecond`,
the game's `--pump=<L/s>` and the bench's `--pumprate`, a sweep seam over the content's number the
way `--seepscale` is over the JSON's — the shipped relay says 100 and nothing in the tick can change
what a row's definition says. `--pumpoutlet`'s unloaded arm is **96 m, not the pass's 64**: the
loaded box is r6 plus the two-chunk margin, 64 m from the *viewer*, and the shaft stands at the
basin's pit rather than under the viewer, so 64 m from the shaft landed inside it and the outlet
was placed; 96 reads `a row alone (its chunk is not loaded — the unloaded arm)`.

**What the numbers say.** Headless, `--profile --speed=0 --fillbasin=2000000 --breach=3 --pump=100
--measure=40`: the lake drains 1,000 L through the portals into the shaft, the pump takes 1,000 L
from Tier B in 20 moves of 50 and emits 1,000 L over the outlet on the surface, the hand-off returns
986 L of it to the basin — a loop through Tier A, every identity exact, `machines 0.15 ms` a
boundary. With `--pumpoutlet=96 --fluidjob=1`: `emitted 0 L into Tier B + 1,000 L into Tier A`,
Tier B empty at the end, Tier A back at 2,000,000 L, `handed 0` because nothing ever rested — the
litres went straight to the basin under a chunk the box never loaded. The `--abort` arm on a
scratch world writes both bodies at the unload and no row; the reopen reads `0 machine row(s)` and
clears 64 + 64 orphan voxels over two drains. On the bench (Release, 1.04× the reference):

```
radius   boundaries   delivered   nominal   moves   stalled   active peak   tick ms mean / max   empty after
1             3,984      100.0        100    3,983    1/0          6,221      0.012 / 1.14       3,984 (879 L left)
2             3,989      100.0        100    3,988    1/0          6,221      0.027 / 1.17       3,989 (623 L left)

the read, 26 cells = 1,664 voxels: AmountAt 5.3 µs (3.2 ns a voxel) against the published bitset walk 31.8 µs (19.1 ns); one HeldIn 10 ns

pumps   water             tick ms mean / max   moves/boundary   intake voxels/boundary
    1   flooded               0.017 / 1.21          1.0                    1,664
  100   flooded               0.602 / 2.69        100.0                  166,400
  500   flooded               2.962 / 4.41        500.0                  832,000
  500   every panel dry       0.020 / 0.02          0.0                        0

the loop: 10,000 boundaries, 500,000 L moved at 100.0 L/s, the total 200,000 L unchanged at every boundary, displaced 0
```

**The relay delivers its nominal rate on a flooded chamber at either radius** — the settle levels
the sheet every boundary, so the eight floor cells around the body are refilled from the whole
floor and the 3.2 cells a boundary come out of them every time; 200,000 L is pumped out in 3,984
boundaries (33 minutes of world time), the last 879 L a film the pack rule left in cells outside
the box. So the intake radius buys nothing here and costs 4.8× the read (124 cells against 26);
its case is a pump whose box the sheet cannot refill in a tick — a pit narrower than the box, or a
pump that outruns the automaton's spread at a higher rate — and that is `--pumprate`'s sweep, not
taken. **The read is 3.2 ns a voxel through `AmountAt` and 19 ns through the published bitset**,
which retires the ladder's decision 14 read rather than confirming it: the copy is the wrong
structure for a box (a panel lookup and a bit test per voxel, against a panel lookup per box and
an array read per voxel). **Five hundred wet pumps are 3 ms a boundary; five hundred dry ones are
0.02 ms** — `HeldIn` answers a dry box in 10 ns, and the sleep set stays a lever with the trigger
the pass named (`machines` over 1 ms on the played world). The loop moves 100 L/s round and round
and the world's total does not move by a millilitre in 10,000 boundaries.

**The tests.** `MachineTickTests` (32): the fixture (one basin, the ramp draining into it, the two
rooms, fact 6's two answers), the intake order, B → B in one boundary, nearest cells first, the
loop over 10,000 boundaries, the five stalls, the box straddling an unloaded chunk, two outlets
alternating, two pumps reaching two outlets, the remainder passing to the next outlet, A → B with
the drain's equality beside it, the 0.47 L/s pump drawing a litre every fourth or fifth boundary,
the pit beside the lake pumping nothing under a table that stands over it, whole litres across a
tier with the remainder left behind, the unloaded outlet on the ground / half-buried / underground
/ off the window / draining off the edge, an outlet and a pump in the lake, the wider intake, the
same set twice landing the same millilitres, a shrunk ring wrapping the cursor, an unknown kind
ticking nothing, and the lifecycle over four seeds with quits — panels and rows written in one
batch, a fresh set restored, both identities and the cross-tier one exact at every round. The
nineteen recipes: the whole-litre floor, the cursor (never, and per outlet), the box's loaded check
(dropped, and reading only the body's chunk), the panel-count shortcut, the lake intake (never, and
by the table — M10-PLAN decision 5's rule, caught by the pit), the underground outlet, the fold into
`Outlet`, the order reversed, the owed counter, the ring not rebuilt, the channel ignored, rock as
room, the sim's pump as `removed`, the sim's emit not waking, the published identity without
`pumped`, Tier A's emit to `Outlet` not spilled. **The whole-litre floor was an identity against
the first thirty-one tests** — every offer and every room happened to be whole — and got its own
fixture (20,500 mL in the box, 15,125 mL of room) before the battery ran.

**Next: M10b, the computer** — an item; right-click on a machine opens the ports and the stock
tab; `SetPort` clamped in Core; the weights the round-robin ignores. M10a is done and unplayed;
M10d's play has the relay pumping the mine.

## M10b — the planning pass

> 2026-09-16, small hours, after a-3 (*"Ok lets continue"* after *"M10b wants its planning pass
> first"*). Read against the ladder's decisions 8 and 11 and its split row for M10b; M10-PLAN
> decision 6 as the ladder amended it (*the programmer is an item in the hand, not a key*);
> M10-NOTES piece 7 (the programmer is a format); `PlaceController.Update` and `Main.UpdatePlacement`;
> `FlyCam._UnhandledInput` and `Main._UnhandledInput`; `Hud`; `MachineRow.SetPortUnchecked`,
> `MachineSet.Version` and `MachineTick.Rebuild`; `Place.Validate`; `ContentLoader`'s material
> DTO; the shipped `relay_pump.json`. Six facts, nine decisions, two sessions, the tests by part.

### Six facts from the code

1. **A right-click with anything selected places it.** `Main.UpdatePlacement` reads the right
   button and hands `PlaceController.Update` a `placing` flag; the controller places the selected
   material through the brush unless `SelectedIsMachine`, which is the one branch that is not a
   brush. A computer in the hotbar — a material, because parts are materials (ladder decision 8) —
   would be placed as 64 voxels of computer on the first click. So the ladder's `placeable: false`
   cannot wait for M10g's parts: the first item that is not a block needs it, and the computer is
   that item. The rest of decision 8 — the hotbar filter, the parts page — is still M10g's; one
   item in a slot is not the crowding the filter answers.

2. **Building names the space in front, mining names the thing you point at, and the computer is
   a mining-side aim.** `Place.TargetCell(hit)` is the cell of `hit.Previous`, the air before the
   face; the machine under the crosshair is `BrushCell.FromVoxel(hit.Voxel)`, whose material
   `IsMachine` and whose cell `MachineSet.TryGet` answers. The controller casts every frame and
   keeps `Hit`, so the row under the crosshair is one lookup on a hit it already has.

3. **The mouse-capture handshake is the whole of the UI risk, and it is smaller than the ladder
   feared.** `FlyCam._UnhandledInput` releases the mouse on Escape and captures it on any mouse
   button pressed while released; `Main._UnhandledInput` reads Q, B, V, T and the number row.
   Both are *unhandled* input — a `Control` with `MouseFilter.Stop` and a focused `LineEdit`
   consume their events first — so a panel takes its own clicks and keys, and a click *outside*
   it reaches the camera and re-captures the mouse. Every existing `Hud` control is
   `MouseFilter.Ignore`, which is why the HUD has never had this question. What the panel needs
   from the camera is two calls — release, capture — and a rule that a re-capture closes it.

4. **The tick reads a row's channel at rebuild, and rebuilds on `MachineSet.Version`, which only
   `Add`, `Remove` and `Restore` move.** A port written through `SetPortUnchecked` today would
   leave a pump on its old ring until something else changed the set. `SetPort` has to move the
   version, and the cursor's wrap on a shrunk ring (a-3's `Rebuild`) already handles the outlet
   that leaves one channel for another.

5. **The shipped pump declares a `weight` port that nothing can read.** Only a ring has weights;
   a pump is never in one. `relay_pump.json` carries `weight 1..100` beside `channel`, so the
   panel would list a number that does nothing — the thing decision 11 refuses a `via` for. No
   played world has a row, and a row with an undeclared port is dropped and counted at restore
   (a-1), so removing it costs nothing.

6. **Nothing declares stock.** The stock tab in M10b's split row is a tab over a field no row has:
   `stockVoxels` is the `station` and `generator` kinds' (M10g, M10f), the row blob's kind state is
   empty for both M10a kinds, and a-1's `WorldBatch` — the transaction the ladder's decision 5
   said no inventory-to-row transfer ships before — is built. The transaction exists; the thing
   to transfer into does not. A row *does* already hold something a quit must keep: its ports.

### Decisions

Each recommended unless marked, and built as recommended unless Matthew says otherwise.

#### 1. The computer is a material with `unit: count` and `placeable: false`, its ID `overburden:computer`; `P` seeds one — *the ID is Matthew's*

One registry, one ID table, one hotbar, one save form — ladder decision 8's whole argument.
`{ "id": "computer", "hardness": 1.0, "supportDistance": 2, "density": 2000, "textureLayer": 7,
"unit": "count", "placeable": false }`, a seventh `LayerColour` and jitter entry (asserted since
a-2), the label derived from the id. **The ID**: M10-NOTES settled `machine_programmer` on
2026-09-02 as *one word from Matthew to change until the first machine ships*; the ladder's brief
says *computer* in his words, three times, and the fabricator recipe lists *the computer*. Nothing
has shipped under either name. **Recommend `overburden:computer`** — his word, and general the way
the notes wanted it (it configures any machine). `P` puts one in the inventory beside the relay
pair until M10g's recipe retires the seeding; using it never consumes it.

#### 2. `placeable: false` comes forward in its minimal form — the loader, the table, and `Place` refusing it as it refuses air — *recommended; amends the ladder's decision 8 by timing only*

`MaterialJson.Placeable` (default true), `MaterialTable.IsPlaceable(id)`, and `Place.Validate`
throwing for a non-placeable material with the message it gives air: a caller error, not a
bucket, because a controller that reaches `Place` with a part in hand is the wiring being wrong,
and a new `PlaceResult` bucket would touch `IsAccounted` and every test that sums it for a case
no correct caller produces. `PlaceController` checks `IsPlaceable` before the brush and routes the
right-click to the computer's verb instead. The hotbar filter and the parts page stay M10g's.

#### 3. The verb is a right-click edge with the computer selected, on a machine body under the crosshair; anywhere else it does nothing — *recommended*

Fact 2: the row is `Machines.TryGet(BrushCell.FromVoxel(Hit.Voxel))` when the hit voxel's
material `IsMachine`. An edge, not a hold, because opening a panel sixty times a second is the
toggle-key bug M4c named. Aimed at rock, air or a non-machine block, the click is a no-op — no
brush, no swing, no message beyond the status line's `COMPUTER — right-click a machine`. `E` stays
unbound (decision 11's crank and workbench).

#### 4. `MachineSet.SetPort(cell, name, value)` in Core, clamped, counted, and a version bump — *recommended*

Returns a `PortWrite` — `Set`, `Clamped` (with the value that landed), `NoSuchRow`, `NoSuchPort` —
never throws for a value, because the panel and `--port=` are both callers and a row is a caller
(decision 21's rule): the clamp is `PortDefinition.Clamp` against the material's declaration, an
undeclared name is refused by name, a row whose material declares no ports (a hole) refuses every
write. `PortWrites` and `PortWritesClamped` on the set for the `M10:` line. **`Version` moves on
every write that changed a value** (fact 4), so the tick rebuilds its rings at the next boundary
and a pump whose outlet just left its channel stalls `no outlet` there rather than a boundary
later. A multiplayer server would apply the same call.

#### 5. Weighted round-robin: the ring is the outlets expanded by weight with the smooth interleave; the pump's `weight` port is dropped — *recommended*

Per channel, at rebuild: the outlets in origin order, then a ring of Σ`weight` entries built by
the smooth weighted round-robin (each turn picks the outlet with the largest running credit,
credit += weight each turn, the pick's credit −= Σweight), so weights 3 : 1 read A B A A rather
than A A A B, and weights all one are today's ring entry for entry — the plain round-robin is the
all-equal case, M10-NOTES piece 2. The cursor indexes the ring; a pump walking it from the cursor
skips an outlet it already reached this boundary (a per-boundary stamp on the row, session-only),
so a weight of 100 does not cost a hundred room reads. Σweight ≤ 100 × outlets, rebuilt only when
the set's version moves. `relay_pump.json` loses its `weight` port (fact 5); `relay_outlet.json`
keeps it. Rejected: a random pick under a seed — one more mode with nothing over weights (M10-PLAN
"deliberately not").

#### 6. The panel: `MachinePanel`, one row per declared port, the row's last stall on it, Esc or a click outside closes — *recommended*

`Overburden.Game/Ui/MachinePanel.cs`, a `CanvasLayer` over a `PanelContainer` with
`MouseFilter.Stop`: a title from the material's label, one `HBoxContainer` per declared port —
the name as its label, a `SpinBox` bounded by the declaration — and a status line reading the
row's **last stall** (`MachineRow.LastStall`, session-only like `OwedMillilitres`, written by the
tick: `stalled: no outlet on channel 3`, `moved 50 L last boundary`), because the number the
player types is only legible beside what the machine did with it. Every change goes through
decision 4's `SetPort` at once, and the `SpinBox` shows the clamped value back. Opening releases
the mouse through `FlyCam.ReleaseMouse()`; Esc or a click outside re-captures through the
camera's own rule and `Main` closes the panel on seeing capture return; `Main._UnhandledInput`
ignores its keys while a panel is open. Labels are derived from the port name, never authored
(M6d-3's rule). No stock tab (decision 7).

#### 7. No stock tab at M10b; the split row's *a transfer round-trips a quit* becomes *a port write round-trips a quit* — *recommended; amends the ladder's split*

Fact 6. The tab is M10g's with the `station` kind that gives it a field; a-1's row blob already
carries the ports, so the quit oracle is: write a port, quit, reopen, the row reads it back and
the tick's ring reflects it — through `Snapshot` / `Restore` in the suite and `--world=probe`
headless. Building the tab now would be M10-PLAN decision 6's own argument against the item —
a gate with nothing behind it — one tab over.

#### 8. Headless: `--port=<pump|outlet>:<name>=<value>` on `--pump`'s bodies; `--openpanel` for the screenshot; `port writes` on the `M10:` line — *recommended*

`--port=` is applied to the body `--pump` places, printed with the clamp (`--port=pump:channel=99`
reads `clamped to 15`), and the oracle is the tick's: the pump moved to channel 3 alone stalls
`no outlet`; both moved pump as before; `--world=probe` and a reopen read the value back.
`--openpanel` opens the panel on the pump when the window opens, so `screenshot.gd` can shoot a
control nobody can click headless — `--debuglamp`'s shape. The periodic `M10:` line and the
`machines` profile block print `port writes N (M clamped)`.

#### 9. Two sessions: b-1 Core and the headless seam, b-2 the panel — *recommended*

**b-1**: `placeable` through the loader and the table, `Place.Validate`'s refusal, `computer.json`
with its colour and jitter, `P`, `MachineSet.SetPort` with its counts and the version bump, the
weighted ring and the per-boundary skip, `MachineRow.LastStall`, the controller's routing (the
verb as a no-op that opens nothing yet — `OpenPanelRequested` on the controller for b-2 to read),
`--port=`, the `M10:` line. Headless-verifiable end to end. **b-2**: `MachinePanel`, the capture
handshake, `--openpanel`, the screenshot, the play note. Cut at the Core/wire seam as M9's
sub-parts were; b-1 alone because the weight ring and the version rule are the two things a test
can hold exactly and a panel cannot.

### The tests, by part

**b-1.** `MachineContentTests`: `placeable: false` read and projected, default true, the shipped
computer is `count` and not placeable, the shipped pump declares no `weight` and the outlet
does. `PlaceTests`: a non-placeable material throws from `Validate` on every shape and
`Place.Machine`. `MachineSetTests`: `SetPort` in bounds → `Set` and the value; out of bounds →
`Clamped` with the bound; an undeclared name → `NoSuchPort`; no row → `NoSuchRow`; a hole's row
→ refused; `Version` moves on a changed value and not on an unchanged one; `PortWrites` counts.
`MachineTickTests`: an outlet moved to channel 3 by `SetPort` leaves channel 0's pump `no outlet`
at the next boundary and not the one after; weights 3 : 1 on two outlets deliver 3 boundaries to
A per 1 to B in the smooth order (A B A A); weights all 1 land the same millilitres in the same
cells as a-3's ring over 60 boundaries (the identity that says the plain ring is the all-equal
case); a weight of 100 costs one room read per outlet per pump; `LastStall` on a row reads the
stall the report counted; a port written, snapshotted, restored and ticked lands on the new
channel. `PlaceControllerTests` or the pipeline harness: the computer selected and a right-click
edge on a machine raises the open request with that row and places nothing; on rock it does
nothing; a hold raises once. Headless: `--pump=100 --port=pump:channel=3` reads `stalled … no
outlet`; `--port=pump:channel=99` reads `clamped to 15`; `--world=probe` then a reopen reads the
row's channel back; the r6 control to the digit.

**b-2.** The panel by screenshot with `--openpanel`: the title, two port rows with their bounds,
the stall line; Esc closes and the mouse is captured again; a click outside closes; a `SpinBox`
past its bound shows the clamped value. Q, B, V, T and the number row do nothing while it is open.

### What this pass amends

- The ladder's split row for M10b: the stock tab is M10g's, and the quit oracle is a port write.
- The ladder's decision 8: `placeable: false` and its loader/table/`Place` half arrive at M10b;
  the hotbar filter and the parts page stay at M10g.
- M10-NOTES piece 8's `machine_programmer`: superseded by `computer` if Matthew takes decision 1.
- `relay_pump.json`: the `weight` port comes out.

### M10b-1 — built

> 2026-09-16, after the pass (*"As recommended, lets build b-1"* — decision 1 taken as
> recommended, so the ID is `overburden:computer`). Suite 1,551 → 1,569; fourteen break-recipes,
> fourteen caught first time; the r6 control unchanged to the digit; every headless oracle the
> pass named read as written.

**What is there.** `placeable` through the loader (`MaterialJson.Placeable`, default true; a
machine with `placeable: false` refused naming the file — a body the brush cannot place is a row
nothing can create), `MaterialDefinition.IsPlaceable`, `MaterialTable.IsPlaceable` (a hole is a
block), and `Place.Validate` throwing for an item exactly as it throws for air — every shape and
`Place.Machine`, before a voxel is written. `computer.json` at `unit: count`, `placeable: false`,
`textureLayer 7`, an eighth `LayerColour` and jitter; `relay_pump.json` without its `weight` port.
`MachineSet.SetPort(cell, name, value) → PortWrite` — `Set`, `Clamped` with the value that
landed, `NoSuchRow`, `NoSuchPort` (an undeclared name, a hole's row, or a set built without a
table) — never throwing for a value, `PortWrites` and `PortWritesClamped` counted, and `Version`
moved on every write that changed a value and on none that did not. `MachineRow.LastStall` and
`LastMovedMillilitres`, session-only like `OwedMillilitres`, written by the tick every boundary.
`MachineTick`: the ring per channel is the outlets in origin order expanded by `weight` through
the smooth weighted round-robin (`ExpandByWeight`, ties to the first in origin order), the cursor
indexes the ring, a pump walking it skips an outlet it already reached this boundary,
`OutletRoomReads` counts what that saves, `RingOn(channel)` exposes the ring, `Outlets` on the
report is the distinct count. `PlaceController.SelectedIsItem` routes a right-click before the
brush: with an item selected nothing is ever placed; a right-click *edge* — the controller
computes it from the hold, so `--place`'s held button is one click — on a machine body under the
crosshair (the hit voxel's cell, mining's aim) sets `PanelRequest` for one frame and counts
`ComputerOpens`, anything else counts `ComputerMisses`. The HUD's status line reads `COMPUTER (in
hand) · right-click a machine to open it`. `P` seeds one computer beside the pair, once. `Main`
prints the request (`M10: computer — … the panel is M10b-2's`), applies `--port=<pump|outlet>:<name>=<value>`
to `--pump`'s bodies as they are placed and prints each write with its clamp, and carries `port
writes N (M clamped), computer O open(s) / M miss(es)` on the `M10:` line and the `machines`
profile block.

**Two things the build found.** The 3 : 1 fixture's first version split 5 : 3 — not the ring's
doing but the mouth layer's: sixteen cells hold five boundaries' worth, and the sixth went to the
other outlet for *room*, which is a-3's rule working. The fixture drains both mouths between
boundaries now and asserts the sequence `AABAAABA` outright. And the hundred-weight fixture poured
four cells against a 3.2-cell budget, so the second boundary read the box's remainder and the
"no read" assertion failed for the right reason; it pours exactly one budget now. Neither was a
hole; both were fixtures degenerate in a way the assertion could see.

**What the numbers say.** Headless, `--pump=100 --port=pump:channel=3 --port=outlet:weight=99999`:
`pump's channel := 3: set to 3`, `outlet's weight := 99999: clamped to 100`, `port writes 2 (1
clamped)`, and the tick reads `stalled 60 no outlet` for the window — the pump on channel 3 with the
outlet on 0. On a scratch world with the pump alone moved to channel 3, the reopen reads `2 machine
row(s) read` and `stalled 36 no outlet`: the write survived the quit and the ring was rebuilt from
it at the first boundary. With the computer selected under `--place`, `0 placement(s), 0 voxel(s)
placed` and `computer 0 open(s) / 1 miss(es)` — the routing refused the brush and the one edge the
held button makes landed on the ground. The r6 control, verbatim, 2601 / 325 / 3446 at 411.

**The tests.** `PlaceItemTests` (3): the table projects and defaults, every shape and
`Place.Machine` refuse an item before writing, a block beside the item still places.
`MachineContentTests` gained `placeable` read, projected and defaulted, and the non-placeable
machine refused naming the file; `ShippedContentTests` finds the computer as the one shipped
material that is not a block, the pump without `weight` and the outlet with it.
`MachineSetTests` (5 more): in bounds sets and moves the version; out of bounds lands the bound and
counts; an undeclared name, an absent row, a hole's row and a bare set refuse; an unchanged write
counts and moves nothing; a written port round-trips a snapshot and a restore. `MachineTickTests`
(7 more): an outlet moved off the channel by `SetPort` leaves the pump `no outlet` at the very
next boundary; 5 : 1 : 1 reads `A A B A C A A`; 3 : 1 delivers `AABAAABA`; all-ones is the outlets
in origin order once each; a weight of a hundred is one room read per outlet per pump, a roofed
heavy outlet read once and passed over; a row reads back the stall and the move the report
counted, a lake pump's litres in millilitres; a port written, snapshotted, restored and ticked
lands on its new channel with the clamped weight. `CollapseTests`' old-world table gains a fourth
appended ID. The fourteen recipes: the loader ignoring `placeable`, accepting a non-placeable
machine, the table projecting always-true, `Place` not refusing, `SetPort` not clamping, never
moving the version, moving it on an unchanged write, writing an undeclared name, weights ignored,
the ring blockwise, the tie to the later outlet, the walk not skipping, `LastStall` and
`LastMoved` not written — all caught first time. The tie recipe was caught by six tests, four of
them a-3's: an equal-weight ring that starts with the later outlet is the plain round-robin
running backwards.

**Next: M10b-2, the panel** — `MachinePanel` over the ports `PanelRequest` names, one `SpinBox`
per declared port through `SetPort`, the stall line from `LastStall` and `LastMovedMillilitres`,
`FlyCam.ReleaseMouse()` / `CaptureMouse()`, `Main` ignoring its keys while the panel is open and
closing it when capture returns, `--openpanel` for the screenshot.

### M10b-2 — built

> 2026-09-16, morning (*"M10b-2 please"*). No Core change: the suite is b-1's 1,569, the r6
> control is unchanged to the digit, and the thing built is judged by three screenshots and two
> headless lines.

**What is there.** `Overburden.Game/Ui/MachinePanel.cs`, a `CanvasLayer` at layer 11 over a
full-rect root that ignores the mouse and a centred `PanelContainer` that stops it: the material's
label (`Hud.DisplayName`, now shared — one definition of what a material is called) in the HUD's
selection yellow, the cell's metres and the facing, one `HBoxContainer` per port the definition
declares — the name with its underscores opened, a `SpinBox` bounded by the declaration, the
bounds beside it as `0 … 15` — a status line, and a hint. Every change goes through
`MachineSet.SetPort` the moment the box's value changes, printed
(`M10: panel — relay pump at Cell(15, -4, 3): channel := 15: set to 15`), the landed value shown
back; the box's own range clamps what is typed before the write sees it, so `Clamped` is
`--port=`'s to exercise. The status line is the row's `LastStall` and `LastMovedMillilitres` in
the player's words — `moved 50 L at the last boundary` in green, `stalled: no outlet on channel 3`
in amber, the five stalls each with a sentence — refreshed on the HUD's tenth-of-a-second
throttle, and an outlet's line says what its ports mean (`receives from every pump on channel 0 ·
weight 7 in the ring`), because the tick writes only the pump's row. **The panel prints its
status line whenever the stall kind changes**, never when the number does, so headless — where
nobody can read the label — the log carries `stalled: no water at the intake` → `moved 50 L at the
last boundary` → `stalled: no water at the intake` as the lake drains, three lines for a run.

`Main.OpenPanel` opens it on `PanelRequest` (b-1's edge, so once per click): the panel shows,
*then* `FlyCam.ReleaseMouse()`, so the cursor appears — at the screen centre, which is why the
panel is centred — over a panel that exists. Three things close it, all through `Main.ClosePanel`:
Esc, which `Main._UnhandledInput` owns while a panel is open (every other key is left alone — a
digit is a port value on its way to a field, not a hotbar slot — and the camera, a child, saw the
same Esc first and released a mouse that was already released, so the order comes out captured
through `FlyCam.CaptureMouse()`); a click outside, which the frame does not stop and so reaches the
camera as unhandled input and captures by the camera's own rule, seen by `UpdatePanel` the same
frame; and the row leaving the set (a pick, a fall, a reconcile — the panel is a view of one row
and goes when it does). `--openpanel` opens on `--pump`'s pump as it is placed, `--openpanel=outlet`
on the outlet (the two-row one), and `--panelset=<name>=<value>` puts a value into the open box as a
hand would — through the box, so its range and its signal run — the widget's wire headless.

**Two rules the pass did not have, both the camera's.** *The click that takes the mouse back is
swallowed by the verbs until it is released* (`FlyCam.ClickLive`, read by mining and placement in
place of `MouseCaptured`). Before this the press captured the mouse in the input phase and the same
frame's `_Process` read the button as held and swung at whatever the crosshair was on — a bite out
of the ground for clicking back into the window, which nobody minded, and a bite out of the pump you
had just configured for closing its panel, which nobody would have: nothing turns the camera while
the mouse is released, so the crosshair is still on the machine when the panel closes. The swallow
is the camera's rather than the panel's so the two cases are one rule, and it ends when every button
is up, read as state rather than as the release event, because a release over a Control that stops
the mouse is that Control's. And *flight is gated on capture*, as walking has been since M4c: a key
typed into a field is a key `Input.IsKeyPressed` still reports — the field consumes the event, not
the state — and a panel that flew the camera while you typed `1` and `w` into it would leave the
machine behind you. Both are recorded in the FlyCam's remarks; neither moves a profile figure,
because a profile captures at `_Ready` and never by a click.

**One thing the build cannot verify and the play must.** Godot 4.4+'s `LineEdit` consumes Esc
while it is *editing* (it un-edits and accepts the event), so with a box's field still active the
first Esc leaves the field and the second closes the panel; with no field active one Esc closes.
Whether two is what a hand expects is the play check's question 3; if one, the panel takes Esc
itself in `_UnhandledKeyInput` and marks it handled, the camera's rule untouched.

**What the numbers say.** Headless, no `--profile` (the panel is built with the HUD and skipped
with it): `--fillbasin=2000000 --breach=3 --pump=100 --openpanel` reads `panel — opened on relay
pump at Cell(15, -4, 3) (--openpanel): channel = 0 [0..15]` and the three status lines above;
`--port=pump:channel=3` beside it reads `channel = 3 [0..15]` at the open and `stalled: no outlet on
channel 3`; `--panelset=channel=99 --panelset=weight=5` reads `typed 99 into channel's box`,
`channel := 15: set to 15`, `the open panel has no box called 'weight'`, `port writes 1 (0 clamped)`
on the `M10:` line and `stalled: no outlet on channel 15` at the next boundary — the box clamped,
the set counted, the ring rebuilt. Three screenshots through `screenshot.gd` with `--settle=9000`:
the pump's panel with its one row and `moved 50 L at the last boundary`; the same pump on channel
3 with the amber stall; the outlet's with two rows and `weight 7 … 1 … 100` after `--port=outlet:weight=7`.
The r6 control, verbatim, 2601 / 325 / 3446 at 411; the suite 1,569.

**M10b is built.** What is not: the play — the panel opened, edited and closed by hand, the
seven-question recipe in TODO — and M10d's, where the relay pumps the played mine. Next is M10c,
power on the channel class, and it wants its own planning pass against the ladder's decisions for
it, `MachineTick`, `MachineSet` and the header.

---

## M10c — the planning pass

> 2026-09-16, evening, after b-2 (*"Ok lets do the M10c planning pass"*). Read against the
> ladder's decisions 9, 10, 15, 16, 21 and 23, its tier-2 and tier-4 numbers, its format block and
> its tick; M10-PLAN decisions 8 and 9 as the ladder amended them; `MachineTick.Run`, `Rebuild`
> and `TickPump`; `MachineDefinition.TryValidate` and `ContentLoader.TryReadMachine`;
> `MachineRow.KindState` and `MachineRowBlob`; `MachineSet.Restore`; `WorldHeaderBlob` v3 and its
> migration policy; `SkyColumn.OpenAboveY` / `IsCellOpenToSky` and `ChunkStreamer.ColumnAt`;
> `Weather.OvercastAt` and `Main`'s `_forcedWeather`; `Carve.Take`, `CarveResult.IsAccounted`
> and the four shapes; `MiningController`'s dispatch and cooldowns; `PlaceController`'s item
> route; `ToolDefinition`; `Main.SeedMachines`, `CurrentHeader` and `PlacePumpAndOutlet`.
> Nine facts, eleven decisions, three sessions, the tests by part. **One thing the pass found
> was a defect, fixed before it was written** (commit `2b26540`): the pick never told the set
> what it took — fact 6.

### Nine facts from the code

1. **The format is here and the tick's half is not.** `MachineDefinition.JoulesPerLitre` is
   parsed, validated non-negative, and read by nothing; the shipped relay carries `1000`.
   `ReservedVia` holds `solar` and `ServedVia` holds `channel` alone; `TryReadMachine` refuses
   `generator`, `converter` and `storage` by name, saying *power is M10c*. So c-1 adds two kinds
   and serves one more `via`, and every refusal it lifts is a test that exists today.

2. **The kind blob is the row's own versioned bytes, carried verbatim and clamped nowhere.**
   `MachineRow.KindState` is a `byte[]` both M10a kinds leave empty; `MachineRowBlob` v1
   length-prefixes it and the ladder's persistence section says its first byte is the kind's
   version. `Restore` drops, defaults and clamps *ports* against the material's declaration and
   counts each, and carries the state opaque — right for an unknown kind, wrong for a storage row
   whose charge must not exceed its capacity (decision 21: *a row read from disk is clamped to its
   material's bounds*). The charge needs a typed reader and its own clamp at restore, and no
   schema bump: the blob's version byte is what the ladder put there for this.

3. **The sky column already answers openness at 0.25 m, and the panel's rule is a count over the
   array the light's rule reads all-or-nothing.** `SkyColumn.OpenAboveY(lx, lz)` is the first open
   Y of every column of a 32 × 32 footprint; `IsCellOpenToSky` demands all sixteen of a cell's
   columns open (M5b's under-seeding argument). A panel's openness — *open columns ÷ 16*, the
   ladder's decision 10 — is the same sixteen reads with a count, and `ChunkStreamer.ColumnAt`
   hands the column per (x, z) chunk column. A chunk column nobody has loaded has no column, and
   a row ticks unloaded (ladder decision 4) — so the openness is sampled when the column is there
   and kept on the row when it is not, which is the *openness byte* the ladder's kind blob lists.

4. **The overcast is a float per frame and the forced one is the shader's.** `Main` reads
   `_forcedWeather ?? Weather.OvercastAt(seed, now)` every frame for the uniforms; nothing reads
   it at the boundary. Decision 10 quantises it once at the site it enters the pool — a byte,
   taken at the boundary from the same `?? ` expression, or `--weather=1` dims the sky and not
   the panel and the split row's oracle reads the forecast instead of the flag.

5. **The relay is 200 panels.** 100 L/s × 1,000 J/L is 50 kJ a boundary; a panel is 500 W × 0.5 s
   = 250 J. The ladder said so (*solar runs the DC tier's pumps, not the relay*) and then put *the
   played mine pumped dry by relay; the storm* in M10d's row, whose generation is M10f's. With
   the pool in and no debug charge, `--pump` stalls `unpowered` and a-3's recipe stops reading
   `drawn 1,000 == pumped 1,000`. The relay's M10c and M10d arms need a charged cell, and a cell
   placed from the inventory is empty (a count has no charge) — so the charge is a debug seed,
   on `--pump`'s path and on a key, the way the pump itself is seeded.

6. **The pick never told the set what it took — fixed.** Every `Carve` entry point has taken an
   `IMachineBodies` since a-2 and `MachineDrainTests` proves `Lost == Carved` with the set
   passed; `MiningController` passed nothing, so in every play since a-2 a pick on a pump read a
   fall's signature and destroyed it without credit. `MiningController.Bodies` now (`2b26540`);
   the same fixture reads `carved 0` unwired and `carved 64` wired. **The same shape waits for
   the gate**: the pack can only be debited from the controllers, which are Game-side, so the
   budget that makes *takes what the charge buys and stops* true must be a parameter of Core's
   shapes with a bucket in `CarveResult`, and the wire from the pack to that parameter wants its
   own headless recipe on day one, not a Core test that passes the pack in.

7. **There is no tool for the `power` block to sit on.** The ladder's decision 15 puts
   `joulesPerVoxel` on *the tool's* `power` block and shows `tools/entangled_miner.json`; in the
   code the entangled miner is a mode on `MiningController` with no file — `Tool` is the tier for
   every shape, the cube's rate is `DefaultEntangledCubeCooldownSeconds` with `--cubecooldown=`
   as its sweep, and M5d recorded *deliberately not a tool tier* so that no `reachMetres` field
   enters the format. A `power` block on the tiers makes Q with a steel pick free; a block the
   mode reads from a file that is not a tool is a third kind of thing. The file exists or the
   gate has no content to be.

8. **Header v4 is one more presence-byte section.** v3's `Player` is written as a presence byte
   and a fixed payload after the inventory, v1 and v2 are read by the same reader with documented
   defaults, and the policy is *an additive version migrates, a version that changes what a field
   means is refused*. The pack — material through the world's table, charge in millijoules, a
   channel — is exactly that shape after the lattice, and a v3 world opens with no pack equipped,
   which is a true statement about it. `SessionSnapshot` already carries the header bytes, so the
   pack rides the chunk transaction for free (a-1).

9. **An item's right-click is routed once and answered by material.** `PlaceController` routes
   `SelectedIsItem` before the brush and, for the computer, asks the machine under the crosshair;
   a pack is an item by the same `placeable: false` and needs a verb of its own on the same edge,
   and the `ComputerMisses` count is a click with the computer on nothing — which is free to
   mean something.

### Decisions

Each recommended unless marked, and built as recommended unless Matthew says otherwise.

#### 1. The pool is the tick's: per channel, Σ placed storage charge plus this boundary's generation; pumps draw in origin order at `joulesPerLitre`; the pack last; written back in origin order up to capacity; the excess `clamped` — *recommended*

Inside `MachineTick.Run`, before the pumps: per channel, `stored = Σ` charge of every storage row
on it (loaded or not — a row is the truth) and `generated = Σ` every converter's output this
boundary (decision 2); `available = stored + generated`. Then every pump in origin order, as
today, with one more term where the offer is read: `affordable = available ÷ joulesPerLitre`
millilitres — **a joule a litre is a millijoule a millilitre, so the bill is one integer division
with no unit conversion** — and `moved = min(offer, room, affordable)`; after the move
`available −= moved × joulesPerLitre`. A pump with an outlet, water and room and nothing to pay
with stalls **`unpowered`**, a sixth `PumpStall`, and `no water` beats it (a dry pump is not a
starved one). Then the equipped pack (decision 4) draws last. Then what remains is written back
into the channel's storage rows in origin order up to each capacity, and what no row holds is
`clamped` — counted, printed, never tolerated (M10-PLAN decision 10's sentence). The identity,
per channel per boundary and over the session:
`Σ charge + pack == loaded + generated − drawn − clamped`, with `loaded` the charge read at open
(rows and header) and `drawn` split `by pumps` / `by the tool` — the fluid identity's shape, a
`loaded` term included for the reason `M8b:` has one.

- **Rejected: draining rows in origin order and leaving the rest** — a cell at the far end of a
  channel never charges and the first one takes every storm; the ladder's rule is the sum and a
  redistribution, and a redistribution is what makes *a cut splits the pool into its rows'
  charges* (M10e) a statement about rows rather than about order.
- **Rejected: a stored pool number per channel** (M10-PLAN decision 8's first form) — the ladder's
  decision 9 killed it and this pass keeps it dead: nothing persists per channel, so nothing can
  disagree with the rows.

#### 2. Solar is a `converter` — sink via `solar`, source via `channel` — and its boundary output is one integer product — *recommended*

`ratedWatts × boundaryMillis` millijoules × `open ÷ 16` × `(256 − byte) ÷ 256`, integer division
last, so a 500 W panel gives 250,000 mJ open under a clear sky and **976 mJ under `--weather=1`**
(byte 255), which is the split row's oracle to the millijoule. `open` is the count of the top
cell's sixteen columns whose `OpenAboveY` is at or below the body's top plus one, read off
`ChunkStreamer.ColumnAt` through an `ISolarSky` the tick takes (`TryOpenColumnsAbove(cell, out
count)` — false when the column is not loaded), sampled at the boundary when the column is
there and written into the row's kind state as a byte; when it is not, the byte stands (fact 3),
so a surface field keeps charging a cell while its owner is 64 m down. The overcast byte is
`min(255, ⌊overcast × 256⌋)` from `_forcedWeather ?? OvercastAt`, taken once at the boundary and
handed to the tick (`OvercastByte`), never a float inside Core (ladder decision 10). `efficiency`
is read as an integer per-mille at load and multiplied in; solar ships at 1,000. **Shipped on the
channel class until M10i re-classes it to `dc`, printed at load** — ladder decision 23's nerf
later, so nobody builds a base on wireless solar.

- **Rejected: `IsCellOpenToSky`** — all sixteen or nothing, so a panel beside a wall is dark; the
  count is the same reads.
- **Rejected: sampling the openness when the chunk arrives** — a lamp placed over the panel would
  not dim it until the next arrival; the boundary is where every other input is read.

#### 3. Storage is a kind with `capacityJoules` and `carried`; a placed row's charge is its kind state, clamped at restore; a body placed from the inventory is empty — *recommended*

Kind blob v1 for `storage`: a version byte and an int64 of millijoules, read and written by a
`StorageState` helper in Core so the tick never parses bytes inline; `Restore` clamps it to the
material's capacity through the set's own table and counts it under `Clamped` (fact 2; the ports
lookup a test hands in gains a capacity lookup). An unknown kind's blob is still carried opaque.
A count in the inventory has no charge (ladder decision 15), so a body placed from it starts at
zero, and a cell dismantled credits its material and forfeits its charge — `forfeited` is a
printed term, not a tolerance. **`overburden:vacuum_cell` ships**: placed, 5 GJ, channel — the one
storage the ladder never re-classes, final on day one.

#### 4. The equipped pack rides header v4; a right-click edge with a pack in hand equips it; it recharges last, bounded by `rechargeWatts`, and is never a supply; its channel is the computer's panel on nothing — *recommended; the pack's ID and numbers are Matthew's*

Header v4, additive: after the lattice, a presence byte, the pack's material through the world's
table, its charge in millijoules, its channel — v3 migrates to *no pack* (fact 8), and the row of
the old-version fixture is named. `EquippedPack` in Core holds it live; `Main` writes it into
`CurrentHeader`, so it rides the session's transaction. **Equipping**: with a pack selected (an
item by `placeable: false`, counted `× N`), a right-click edge anywhere equips one from the count;
with a pack already equipped the click swaps, and the outgoing pack goes back to the count with
its charge forfeited and printed — the ladder's *a second pack in the inventory is a count with
no charge*, the only rule under which a swap is exact (**reversed on the first play, 2026-09-18:
the charge is kept beside the count, `stowed` in the identity — see decision 15's amendment**).
**Recharging**: the pack draws from its
channel *after* every placed consumer, `min(available, capacity − charge, rechargeWatts ×
boundaryMillis)`, and its charge is never part of `available` — a backpack is not a battery bank,
or the relay would drain the miner's pack in one boundary. **Its channel**: the computer's
right-click on *nothing* — sky, rock, a block, the misses b-1 counts — opens the pack's panel when
one is equipped (`MachinePanel` over a small port view rather than a row: one `channel` box, the
charge as its status), amending M10b's decision 3 by one sentence; with none equipped the miss
stays a miss. **The pack**: `overburden:lead_acid_pack`, 3.6 MJ, `rechargeWatts` 1,000 — a car
battery on a car charger — on the channel class until M10i re-classes it to `dc` with its dock,
printed at load beside the panel's line. It ships rather than the vacuum pack because M10d's
row is *the gate felt at the bite*, and by decision 16 the top pack lasts a session on purpose:
56 s of full-rate mining and twelve minutes on ten panels is a gate you can feel in one play.

- **Rejected: the pack as a storage row on its channel** — the relay drinks it (above); and a row
  with no position has no origin order.
- **Rejected: `E` to equip, or a slot on the HUD** — a third key for a verb the right-click edge
  already carries for items.
- **Rejected: the pack's channel as a flag** — a port nothing in play can write is decision 11's
  refusal one layer out.

#### 5. The entangled miner becomes a tool: `tools/entangled_miner.json` with its threshold, its cooldown and its `power` block; Q swings it and T's tiers stay free — *recommended; the threshold is Matthew's*

Fact 7. `ToolDefinition.Power` — `{ "via": "channel", "joulesPerVoxel": 100 }`, `via` held to the
served set as a link's is, the joules bounded at load (0 ≤ J ≤ 10⁹, refused past it) — on any
tool; the shipped picks declare none and a tool with none is free. `overburden:entangled_miner`
is loaded by ID, off the T cycle, and is what `MiningController` swings while Q is on:
`hardnessThreshold` 5.0 (the ladder's — it takes anything, machine bodies at 3.0 included, which
is decision 15's *Q reclaims a machine at range exactly as a pick does* and was not true under a
hand pick), `cooldownSeconds` 0.10 with `--cubecooldown=` now a sweep over the file's number the
way `--pump=` is over the relay's, `radiusMetres` unread by the cube and named so. **What this
reverses**: *entanglement never changes what the bite is hard enough to take* (M3d, restated at
M5d and 2026-09-03) — Q composed with the tier's threshold so `Carve.Take` stayed the only gate.
It still is the only gate; the threshold it reads is the entangled tool's, and the thing that
makes Q *earned* is the pack, which is the brief. **What this keeps**: no `reachMetres` in the
format — the entangled reach is still the mode's constant and the world's edge, and that was the
decision M5d protected. Without the file (a mod that removes it), Q falls back to the tier and is
free, printed.

- **Rejected: the `power` block on the tiers** — Q with a steel pick would be free, and the gate
  would gate the hand pick's holder only.
- **Rejected: a mode-only file that is not a tool** — a third content shape for one number.

#### 6. The gate: a voxel budget on every `Carve` shape with an `Unpowered` bucket; the controller pays `Removed × J`; the fill at range is all-or-nothing on a cell's cost; the Alt chip pays one; no pack, Q takes nothing; `--nogate` is one flag for the pumps and the miner — *recommended*

`Carve.Cube`, `Sphere`, `Chip`, `Voxel` and `Chunk` take `int voxelBudget = int.MaxValue`;
`Take` lands a voxel it would have removed in **`CarveResult.Unpowered`** once the budget is
spent — after `TooHard`, so a voxel the tool cannot take costs nothing — and `IsAccounted` gains
the term (the M6c argument against a bucket does not hold here: an empty pack is a case every
correct caller produces). `MiningController` computes the budget as `charge ÷ (J × 1000)` from
the equipped pack and debits `Removed × J` after the carve — *takes what the charge buys and
stops*, M10-PLAN decision 9's words; the Alt chip is the same path with a budget of one voxel's
worth; **the fill at range** (`PlaceController`, entangled) is refused whole when the charge is
under `64 × J` and pays `Placed × J` after, so `Place` needs no bucket. With the gate on and no
pack equipped, an entangled swing carves nothing and the HUD's `Q` line says `no pack — P seeds
one`; `Swings` still count, so a run reads *swung and took nothing* rather than *did not swing*.
**`--nogate`** is the pre-M10c arm for both draws at once — pumps free, the miner free, printed
on the `M10:` setup line — and every recorded `--mine --entangle` recipe gains it, because a
profile run equips nothing (CLAUDE.md's commands say so in the same commit).

- **Rejected: the bite refused whole under `64 × J`** — the pack's last 6.3 kJ are stranded and the
  gate reads as a cliff rather than a running-down.
- **Rejected: two flags** (`--nogate` for the miner, `--nopower` for the pumps) — one arm, one
  name; a run that frees one and not the other measures a world that cannot exist.

#### 7. Debug seeding: `P` adds a panel, a cell and a pack; `Alt+P` fills every loaded storage row and the pack; `--pump` places a charged cell beside the outlet; `--solar=<n>`, `--pack`, `--charge=` — *recommended*

Fact 5. `P` seeds a solar panel and a vacuum cell beside the relay pair, and one pack once, the
way it seeds one computer. **`Alt+P` fills every storage row in the loaded set and the equipped
pack to capacity**, printed with the joules — Alt+R's shape, the debug arm that lets M10d's relay
run — and is the only way a placed cell is ever full without generation. `--pump` places a
**charged vacuum cell** in the cell beside the outlet along +X as it places the outlet, so a-3's
recipes read `drawn 1,000 == pumped 1,000` unchanged with `drawn by pumps 1,000,000 J` beside them
(`--cell=0` omits it, and that run reads `stalled 60 unpowered`). `--solar=<n>` places n panels on
the surface in a row along −X from the outlet; `--pack[=<id>]` equips one at open, `--charge=<J>`
sets its charge — the gate's headless oracle.

#### 8. The lines: a `power:` term on `M10:` and the `machines` block; the panel's three new lines; the HUD's pack — *recommended*

`M10:` gains `power: N cell(s) hold J, pack J / J (ch C), generated J (P panel(s), open X/16
mean, overcast B/256), drawn J by pumps + J by the tool, clamped J, forfeited J — pool == loaded +
generated − drawn − clamped: exact`; the `machines` profile block the same, and `boundary`'s
`machines` bracket is unchanged (the pool is inside it). The panel (b-2) reads a storage row as
`charged 2.1 GJ of 5 GJ`, a converter as `generating 250 J a boundary (16/16 open, overcast
0/256)`, and a pump's sixth stall as `stalled: no power on channel 3`. The HUD's status line
carries `pack 2.1 / 3.6 MJ` whenever one is equipped and `Q takes nothing — no pack` under Q
without one.

#### 9. The bench: `--pump` gains a power row — *recommended*

The loop closed with a charged cell on the channel: 500 pumps with 500 cells against a-3's 500
free, so the pool's cost on the boundary is a number beside the read's; and the identity checked
to the millijoule over the 10,000-boundary loop with the cell draining. No new arm.

#### 10. Not in M10c — *recommended*

Docking (M10f: the pack on the channel class recharges anywhere until then, printed); `crank`,
`burner`, `surround`, `adjacent:` and any class (M10e/f); the `Flooded` stall (M10f); the machine
sleep set (still a lever); the hotbar's parts page (M10g); a placed cell's charge on the swatch;
day and night.

#### 11. Three sessions: c-1 the pool in Core, c-2 the wire and the oracles, c-3 the pack and the gate — *recommended*

**c-1**: the two kinds through the loader (`ratedWatts`, `efficiency` per-mille, `capacityJoules`,
`carried`, `rechargeWatts`; `solar` served as a sink `via`; the refusals it lifts), `StorageState`
and the converter's openness byte in the kind blob, the restore's clamp, `ISolarSky` and
`OvercastByte` on the tick, the pool per channel with the pump's bill and `unpowered`, the
write-back and `clamped`, the pack's recharge against an `EquippedPack` the tick is handed, the
counters and the identity, `PowerOff` (the `--nogate` seam's tick half). Headless-invisible on
its own. **c-2**: `Main`'s `ISolarSky` over the sky columns, the overcast byte at the boundary,
`P` and `Alt+P`, `--pump`'s cell, `--solar=`, `--cell=`, `--nogate`'s tick half, the `M10:` and
profile lines, the panel's lines, the bench row, `--weather=1`'s oracle; the three JSON files
(`solar_panel`, `vacuum_cell`, `lead_acid_pack`) and `ShippedContentTests` on them. **c-3**:
header v4 and `EquippedPack`'s persistence, the equip verb and the swap, the pack's panel on
nothing, `tools/entangled_miner.json` and `ToolDefinition.Power`, `Carve`'s budget and
`Unpowered`, the controllers paying, the HUD, `--pack`, `--charge=`, `--nogate`'s miner half, the
bite-that-stops oracle, the recorded recipes amended. Cut at the Core/wire seam as before; c-3
alone because it is the header bump and the first change to a recorded play rule.

### The tests, by part

**c-1.** `MachineContentTests`: a `converter` and a `storage` load with every field read; `solar`
is served as a sink and refused as a source; `ratedWatts`, `capacityJoules` and `rechargeWatts`
bounded and refused past the bound; `efficiency` outside 0..1 refused; a `carried` storage with a
face or two links refused; a pump with `joulesPerLitre` 0 is free. `MachineRowBlob` /
`MachineSetTests`: a storage row's charge round-trips the blob; a charge over capacity restores
clamped and counted; an unknown kind's blob is still carried verbatim. `MachineTickTests` (a
`Twin` with a fake sky): a channel with one panel and no cell reads `clamped == generated` every
boundary; one panel and one cell reads the cell's charge climbing by exactly 250,000 mJ a boundary
open under byte 0 and by 976 under byte 255; openness 8 of 16 halves it; an unloaded panel
generates from its stored byte; two cells on a channel are filled in origin order and the second
starts only when the first is full; a relay pump with water, room and a full cell moves its whole
budget and the cell falls by `moved × 1000` mJ; with 25,000,000 mJ it moves exactly 25 L and
stalls `unpowered` at the next boundary with `no water` beating it on a dry box; two pumps in
origin order share a cell first-come; the pack recharges last and never supplies; the identity
holds per boundary and over 1,000 boundaries with the twin's `loaded` term; `PowerOff` reproduces
a-3's every fixture to the millilitre. `CollapseTests`' old-world table gains three IDs.

**c-2.** Headless: `--pump=100` reads a-3's oracle with `drawn by pumps 1,000,000 J` and the cell
fallen by exactly that; `--cell=0` reads `stalled 60 unpowered`; `--solar=10 --cell=0` reads
`generated 150,000,000 mJ, clamped 150,000,000` over 60 boundaries (no store, `--weather=0`) and
`--weather=1` reads `585,600` for both; `--solar=10` with the cell reads the cell climbing; the r6
control to the digit; `P` then a screenshot of a panel's and a cell's panel lines. The bench's
power row.

**c-3.** `WorldHeaderBlob`: v4 round-trips the pack; a v3 header opens with none; a pack naming a
material outside the table is refused naming it; the settings count test is untouched.
`CarveTests`: a budget of 6 on a 64-voxel cube removes 6 and lands 58 in `Unpowered` with
`IsAccounted` true on every shape; a budget of 0 removes nothing; `TooHard` voxels cost nothing
against the budget. `ToolContentTests`: `power` read, `via` held to the served set, the joules
bounded; the shipped miner declares it and the picks do not; `ShippedContentTests` still finds a
tier that leaves the hardest material standing with the miner off the cycle. Headless, verbatim:
`--pack --charge=640 --mine --entangle --profile --speed=0` reads `removed 6`, `unpowered 58` on the
first swing and `unpowered 64` after, the pack at `40 J`; without `--pack` it reads `swung N, took
nothing (no pack)`; with `--nogate` it reads a-3's figures; `--world=probe --pack --charge=1000`
then a reopen reads `pack 1,000 J`; the r6 control to the digit. By hand: equip, swap and read
`forfeited`; the pack's panel on the sky.

### What this pass amends

- M10-PLAN decision 8's stored pool and decision 9's `batteries/` folder: superseded by the
  ladder's decisions 9 and 15, and now by the row's kind state and the pack's header section.
- M10b's decision 3: the computer's click on nothing opens the equipped pack's panel.
- M3d's and M5d's *entanglement never changes what the bite is hard enough to take*: reversed by
  decision 5; the pack is the gate and the entangled tool's threshold is content.
- CLAUDE.md's recorded `--mine --entangle` recipes: every one gains `--nogate`.
- The ladder's M10d row: the relay's power at M10d is `Alt+P`'s debug charge, stated as such.

### M10c-1 — built

> 2026-09-16, evening (*"As recommended, lets build c-1"* — the pack and the threshold taken as
> recommended). Suite 1,569 → 1,591; twenty-two break-recipes, nineteen caught first time and three after the fixture each named was added — a converter with the sky as its source and the channel as its sink (the converter's own shape rule made the first recipe an identity), the pack's own capacity bound (the tick caps before it calls, so only the object's test sees it), and a lake pump under a litre of joules (the floor changes the stall's name, not the water); the r6 control unchanged to the
> digit. Core only; the game turns the pool off and says so until c-2 wires it.

**What is there.** `MachineKind.Converter` and `MachineKind.Storage` through the loader —
`ratedWatts`, `efficiency` (read as whole thousandths by `litresPerSecond`'s rule, refused
otherwise), `capacityJoules` (kept in millijoules), `carried`, `rechargeWatts` — with every rule
refusing naming the file: `solar` is served as a `via` and is a power sink on a converter and
nothing else; a converter declares a solar sink and a power source; a storage exactly one power
sink; a carried storage is `placeable: false` and no other machine may be; the numbers are
bounded (a gigawatt, a terajoule, a gigajoule a litre) and refused past the bound; `generator`
now names M10f. `StorageState` and `ConverterState` in Core are the kind blob's two layouts —
version 1, an int64 of millijoules and a byte of open columns — read and written by no one but
them, in place once the bytes are this version's, a foreign version reading as empty; `Restore`
clamps a charge to its material's capacity through the set's own table and counts it, a
table-less set clamping nothing. `MachineSet.ChargeOf`, `StoredMillijoulesEntering` and
`StoredMillijoulesLeaving` are counted at every door — `Add`, `Remove`, and a restore over a live
set, which nets to zero — so the pool identity has its `loaded` and `forfeited` terms in one pair
with no new counter on any caller. `ISolarSky` is the one question a panel asks; `EquippedPack`
is the pack as an object — capacity and recharge bound from its definition, `Take` and `Give`
with their counters, never a row.

**The tick.** `BeginPower` before the pumps: every converter's `SolarOutput` — the openness
sampled off the sky and written to the row when the column is loaded, the row's byte when it is
not; then `ratedWatts × boundary`, `× open / 16`, `× (256 − byte) / 256`, `× efficiency / 1000`,
multiply-then-divide each in turn so a byte of 255 leaves 976 mJ of 250,000 and not 0 — summed
per channel with every placed storage row's charge into `_available`. In `TickPump`, after the
offer and after `no water`: `affordable = available ÷ joulesPerLitre` (a millijoule a
millilitre), floored to whole litres for a lake pump, `unpowered` at zero, the offer capped at it;
after the move the bill for exactly what moved comes off the channel. Then `RechargePack` — last,
`min(available, room, rechargeWatts × boundary)` — and `WriteBack`, every channel's remainder
into its storage rows in origin order to capacity, the rest `clamped`. `PowerOff` skips all three
and the bill: the pre-M10c arm, which the game runs now. The report carries `Unpowered`,
`Generated`, `Drawn`, `Clamped` and `PackGiven`; the tick carries their lifetimes, the solar
sample counts and the open-column sum for a mean, `StoredMillijoules` through the set's
definition, and **`PowerExact`**: `Σ rows + pack == entering − leaving + generated − drawn −
clamped + pack initial − pack taken`.

**Two things the fixtures said.** A pump takes all it can pay for, so with a panel on its channel
it buys a trickle every boundary and never starves: the thousand-boundary fixture wanted a roofed
sky over a re-poured room before `unpowered` fired, and a pack that starts full before a cell ever
overflowed — a 3.6 MJ pack drinks nine thousand boundaries of one panel. And a 60 L cell left 10 L
after one boundary, which is what the reopen fixture then read — the fixture's arithmetic, not the
tick's. Neither was a hole.

**The tests.** `MachineContentTests` (+5, and the backwards-converter case): a converter, a cell and a pack load with every field
read; solar served as a sink on a converter and refused as a source, on a fluid link, with a
face, on a pump; a converter without its solar sink or its power source, a storage with a source
or two sinks, a carried storage with a face; every power number bounded and refused; carried is a
storage that is not placeable and nothing else. `MachineSetTests` (+5): the charge round-trips the
blob and a snapshot; the pack gives to its capacity, takes to empty and counts both; an empty or foreign state reads as none and is rewritten in this version in
place; a charge over capacity restores clamped and counted, and a bare set clamps nothing; entering
and leaving are counted at every door and a carried row counts none. `MachineTickTests` (+12): a
panel with no cell clamps everything; a panel and a cell charge by exactly the product, 976 under
the storm, half at eight columns, nothing at none, 225,000 at 90%; an unloaded panel generates from
its last sample and one never sampled is dark; two cells fill in origin order and the second only
once the first is full; a billed pump moves its budget and the cell falls by exactly the bill; 25 L
of joules moves exactly 25 L, then `unpowered` with water and room, and `no water` beats it dry;
two pumps share a cell first-come in origin order and both stall when it is spent; a lake pump
under a litre of joules stalls `unpowered` and with one moves exactly one; the pack
recharges last at its bound and never supplies; `PowerOff` moves no joule and the pump is free; a
charge restored ticks from where it stood; and a thousand boundaries with everything running — the
tool drawing on the pack between them, the overcast walking, the room drained and re-poured, the
sky roofed for a hundred — hold `PowerExact` and `pumped == emitted` at every one, with moves,
stalls and clamps all non-zero.

**Next: M10c-2, the wire and the oracles** — `Main`'s `ISolarSky` over `ChunkStreamer.ColumnAt`,
the overcast byte at the boundary from `_forcedWeather ?? OvercastAt`, `PowerOff` off and
`--nogate` on, `P` and `Alt+P`, `--pump`'s charged cell and `--cell=0`, `--solar=<n>`, the `M10:`
`power:` term, the panel's three lines, the bench's power row, the three JSON files and
`ShippedContentTests`, `--weather=1` reading 976 mJ a panel a boundary.

### M10c-2 — built

> 2026-09-17, small hours (*"c-2 please"*). Suite 1,591 → 1,592; every headless oracle the pass
> named read to the millijoule; the r6 control unchanged to the digit; two screenshots; the
> bench's power row. No Core rule changed — two Core doors were added for the wire (`SetCharge`,
> `SolarOutputOf`) and nothing else.

**What is there.** `Overburden.Game/Machines/StreamerSolarSky.cs` — the light's own array, counted:
a body's sixteen columns off `ChunkStreamer.ColumnAt`, open when `OpenAboveY` is at or below the
body's top plus one, `false` with no seeded column so the row's last sample stands. `Main` sets
`MachineTick.OvercastByte` once a boundary in `RunMachineTick` from the same `_forcedWeather ??
OvercastAt` the shader reads, `min(255, ⌊overcast × 256⌋)`, and pushes an error on the pool
identity as it does on the water's. `PowerOff` is `--nogate`'s now — printed either way, and the
pool's line at open says the panel and the pack ride the channel class until M10i. **`--pump`
places a charged vacuum cell in the cell beside the outlet** through `PlaceSurfaceBody` (the
outlet's own path: through the brush where the chunk is loaded, a row alone where it is not) and
`MachineSet.SetCharge`, the set's one debug door — clamped to capacity, the difference counted as
entering or leaving so the identity carries it without a term of its own; `--cell=0` omits it.
`--solar=<n>` places panels west of the outlet a metre apart the same way. `P` seeds a panel and
a cell beside the pair and a lead-acid pack once (equipping it is c-3's); **`Alt+P` fills every
placed cell to capacity**, printed — Alt+R's shape, the charge M10d's relay runs on.
`--openpanel=cell|panel` opens on those bodies. The `M10:` line carries a `power:` term — cells and
what they hold, the pack, generated with the panel count, the open-column mean, the unsampled
count and the overcast byte, drawn by pumps, clamped, pack given, and `pool == entering − leaving +
generated − drawn − clamped: exact` — beside a sixth stall; the profile has a `power` line with
the window's terms and the sky's answers. The panel reads a cell as `charged 4,999.5 MJ of
5,000.0 MJ · pooled on channel 0`, a converter as `generating 125 J a boundary onto channel 0 ·
16/16 open, overcast 128/256` through `MachineTick.SolarOutputOf` (one definition of the product),
and a pump's sixth stall as `stalled: no power on channel N`. `MachinePanel.Joules` is the one
formatter (mJ, J, kJ, MJ, GJ). Three JSON files: `solar_panel` (500 W, `efficiency` 1.0, layer 8),
`vacuum_cell` (5 GJ, layer 9), `lead_acid_pack` (3.6 MJ, 1 kW — 250 W since 2026-09-18, `carried`, `placeable: false`,
`unit: count`, layer 10 — a swatch only); three colours and jitters, asserted; the bench's `--pump`
gains a powered row and a powered loop.

**What the numbers say.** Headless, `--fillbasin=2000000 --breach=3 --pump=100`: a-3's oracle
unchanged — `drawn 1,000 == pumped 1,000 == emitted 1,000` — with the cell at `4,999.0 MJ` of
5,000 and `drawn 1,000.0 kJ by pumps`, the pool exact. `--cell=0`: `59 unpowered` of 60 ticks in
the window (the first is `no water`, before the lake reaches the shaft), 0 moves. `--solar=10
--cell=0 --weather=0`: **`generated 150,000,000 mJ`** over the window's 60 boundaries, ten panels
`open 16.00/16`, the sky answered 600 of 600, `drawn 147,500,000 by pumps` — the trickle, 2.5 L a
boundary — and `clamped 2,500,000` from the one boundary before the water came; **`--weather=1`:
`generated 585,600 mJ`**, 976 a panel a boundary, `drawn 531,000` (9 mL a boundary), `clamped
54,600`, the identity exact. `--nogate --cell=0` reads a-3's 20 moves and `0 unpowered`. The bench,
Release: **500 pumps with 500 charged cells tick at 3.03–3.11 ms mean against 3.00–3.01 free over two runs** — the pool
over five hundred rows is 0.02–0.11 ms a boundary, inside the bench's own spread, summed, billed and written back — `drawn 110,000 kJ`
over 220 boundaries at 2 L/s, 2,499.890 GJ still stored, clamped 0; the loop, powered, moves
500,000 L over 10,000 boundaries with the world's total unchanged, the cell paying exactly
`500,000 L × 1,000 J/L` of its 5 GJ and the pool identity exact at every one. The r6 control,
verbatim, 2601 / 325 / 3446 at 411. Two screenshots: the cell's panel with its charge line, and a
panel's under `--weather=0.5` reading `125 J a boundary`, `overcast 128/256`, with the rain on.

**Two things the wire said.** The bench's powered arm could not hold its flood: five hundred cell
bodies displace five hundred cubic metres, so the arm pours a thousand litres less per cell — a
fixture fact, not a hole. And the pool's cost on the boundary is a rounding error against the
read: the ladder's *the tick is O(awake machines) at every tier* holds for the power half at the
first number it was given.

**The tests.** `ShippedContentTests` (+1): the trio loads with every field read, the pack is the
second non-placeable material, the relay's boundary bill is exactly 200 panels', every shipped
tier can take a panel and a cell. `CollapseTests`' old-world table gains three appended IDs. The
rest is the wire's, headless and on the bench, as above.

**Next: M10c-3, the pack and the gate** — header v4 and `EquippedPack`'s persistence, the equip
verb and the swap with `forfeited`, the pack's panel on the computer's click on nothing,
`tools/entangled_miner.json` (threshold 5.0, cooldown 0.10 with `--cubecooldown=` as its sweep,
`power { via: channel, joulesPerVoxel: 100 }`) and `ToolDefinition.Power`, `Carve`'s voxel budget
and `CarveResult.Unpowered` in `IsAccounted`, the controllers paying (the bite and the Alt chip;
the fill at range all-or-nothing on `64 × J`), the HUD's pack line, `--pack[=<id>]`,
`--charge=<J>`, `--nogate`'s miner half, the bite-that-stops oracle, and every recorded `--mine
--entangle` recipe gaining `--nogate` in the same commit.

### M10c-3 — built

> 2026-09-17 (*"c-3 please"*). Suite 1,592 → 1,617; twenty-six break-recipes, twenty-six caught; every headless
> oracle the pass named read — one of them a voxel off the pass's arithmetic for a reason the
> aim explains; the r6 control unchanged to the digit; two screenshots. **M10c is built, all
> three sessions, and unplayed** — the play recipe is in TODO under M10c.

**What is there.** Header v4: after the lattice a presence byte and, present, the pack's
material through the world's table, its charge in millijoules and its channel — `PackSave` on
`WorldHeader`, the player's shape, with the inventory's three checks at write and at read and
v3 opening with none (the real played-world fixture reads with `Pack == null`). `EquippedPack`
gains the one definition of what a joule buys — `VoxelsAffordable(cost)`, floored, and
`Pay(voxels, cost)` for exactly what came away — and the tick gains `EquipPack`, the door a
swap goes through: the outgoing pack's `initial` and `taken` move into the session's sums and
its charge into `PackForfeitedMillijoules`, so `PowerExact` reads `Σ rows + pack == entering −
leaving + generated − drawn − clamped + Σ initial − Σ taken − forfeited` across any number of
swaps. `tools/entangled_miner.json` ships — threshold 5.0, `cooldownSeconds` 0.10, `power {
via: channel, joulesPerVoxel: 100 }`, a radius for the sphere arm the cube never reads —
through `ToolDefinition.Power` (`ToolPower`: the `via` held to the served set as a link's is,
the joules bounded at a gigajoule) with the picks free by absence and `IsFree` telling the two
apart; `Main` loads it by ID and keeps it off the T cycle, because on the cycle its threshold
would make it the strongest tier and the default pick. **`Carve`'s five shapes take `int
voxelBudget = int.MaxValue`**, one gate in `Take` asks it after air and after `TooHard` and
spends it after the write, and the rest of the shape lands in `CarveResult.Unpowered`, in
`IsAccounted`. `MiningController` swings `SwungTool` — the entangled tool while Q is on and it
exists, the tier otherwise — with a budget of `Pack.VoxelsAffordable(cost)` (unbounded free or
under `PowerOff`, nothing with a costing tool and no pack) and pays `Removed × cost` after; the
Alt chip is the same path with one candidate. `PlaceController`'s fill at range is all or nothing
on the shape's cost — a voxel, a cell, a chunk — refused whole under it (`UnpoweredPlacements`,
printed once) and paid for what landed, the deferred chunk fill asked again at the boundary. The
equip verb: with a pack selected (`SelectedIsPack`, a carried storage), a right-click edge
anywhere raises `EquipRequest` and `Main` takes one off the count, equips it empty and, with one
already there, puts that one back as a count with its charge forfeited and printed (**since
2026-09-18 the fullest carried one is equipped and the outgoing one keeps its charge, `stowed` —
the amendment at the end of this section**). The
computer's click on nothing opens the pack's panel when one is equipped — `MachinePanel.OpenPack`,
one `channel` box landing on the object, `charged 2,100.0 kJ of 3,600.0 kJ (58.3%) · recharges on
channel 3, up to 500 J a boundary` — and stays a miss with none. The HUD carries `pack 2,100.0 kJ /
3,600.0 kJ (58.3%)` on every line while one is equipped and `Q TAKES NOTHING — no pack` under Q
without one. (The share on both, and on a cell's panel, is from the first play the same evening:
a pack at `3,994 J of 3,600.0 kJ` — a joule figure beside a kilojoule one — was read as over its
cap, *"they have no upper bound?"*; each number keeps its own unit because a 7 kJ cell in its
5 GJ capacity's unit would read `0.00 GJ`, and the share is what makes the pair comparable.) `--pack[=<id>]` equips at open, `--charge=<J>` sets it, `--openpanel=pack` opens its panel,
`--nogate` frees the miner and the fill with the pumps, `Alt+P` fills the pack too, `P`'s line
says the right-click equips. The `M10:` line's `power:` term gains `by the tool` and `forfeited
J by N swap(s)` and the identity's last term; the profile's `mining` block gains a `gate` line.

**What the numbers say** (at the 100 J price and 1 kW recharge this was built at; 50 J and 250 W
since 2026-09-18 — the recipe carries `--charge=320` now and reads `paid 300 J, the pack at 20 J`,
the fill's arms 1,600 / 3,200 / 6,400 J with 750 J left after the two placements, the relay gives
the empty pack `125 J × 40 boundaries = 5 kJ` and the cell reads `4,999.0 MJ`, everything else
to the digit). `--pack --charge=640 --mine --entangle
--profile --speed=0`: **`removed
6, unpowered 57 of 64` on the first swing, `paid 600 J, the pack at 40 J`**, then `removed 0,
unpowered 57` — the pass wrote 58, and the cube on the hillside at the spawn holds one air voxel,
which is the aim's fact and not the gate's (air costs nothing, so 64 − 1 − 6). Without `--pack`:
`swung 120, took nothing (no pack)`, every candidate but the air in `Unpowered`, `Swings`
counting. With `--nogate`: 63 removed a swing, `gate off`. On a scratch world `--pack
--charge=1000` then a reopen reads `equipped from the world file: 1,000 J` with `header v4`; after
the 640 J bite the reopen reads `40 J`. The relay with an empty pack on its channel gives it
exactly `500 J × 40 boundaries = 20 kJ` off the cell (`4,998.98 MJ`), the pool exact. The fill at
range: 3,200 J refuses whole (`needs 6,400 J … holds 3,200 J`), 6,400 J places one cell and
refuses the next at 0, 12,800 J places two (113 voxels, 15 occupied) and refuses at the 1,500 J
left. The swap, headless — `--pack --charge=1000 --place --material=overburden:lead_acid_pack
--stock=64` — reads `the outgoing pack's 1,000 J forfeited … 1 swap(s)` and the identity exact
(`… holding 1,000 J … 1,000 J stowed by 1 swap(s)` since the amendment).
The c-2 storm oracle and the r6 control unchanged to the digit.

**Three things the wire said.** *`--place` has placed nothing since M10b-1*: `--stock=` seeded
every material alike, the largest holding at a tie was the lowest ID, and from b-1 that was the
computer — every `--place` profile since read `computer 0 open(s) / 1 miss(es)` where its
placements should have been. `LargestHolding` prefers a placeable material now and `--stock=`
seeds an item at one, which is also the equip verb's headless wire. *Two header tests had passed
on the wrong refusal since M7b-2*: their tail offsets counted back over the inventory rows
without the clock and the lattice, and `ClockAndLatticeBytes` equals an inventory row's twelve,
so `AnInventoryHoldingAMaterialTheTableLacksIsRefusedOnRead` wrote 9 into the player flag and
passed on *the player-present flag is 9*, and the duplicate-row test read a player out of the
clock's bytes and ran out; the v4 byte moved both onto the clock and they failed, which is what
a version bump is for. Every tail offset goes through one `TrailerBytes` now and both assert the
refusal they name. And *the swap is a term*: the identity with one pack held, and a swap that
replaced the object dropped what the pool had given the first — `EquipPack` on the tick, not a
setter, and the forfeit printed with its count.

**The tests.** `WorldHeaderTests` (+9): the pack round-trips; none reads none; a v3 payload and
the played world's real v3 header open with no pack; a pack naming a material past the table is
refused at write and at read naming it; air, a negative charge, a channel off the ladder and a
flag of 2 refused. `MachineTickTests` (+1): a swap forfeits the outgoing charge and the identity
holds across two swaps and an unequip with the tool drawing on each. `MachineSetTests` (+1): 640 J
at 100 J a voxel buys six, the seventh nothing, a cost of nothing everything, and a short pay
returns short. `CarveCubeTests` (+5): six of a solid cube in storage order with 58 unpowered
and accounted; zero takes nothing; stone and air cost nothing against the budget; the budget on
the sphere, the chunk, the voxel, the chip and the swing; the default unbounded with `Unpowered`
zero. `ToolContentTests` (+8): the block read and the picks free by absence; six refusals naming
`via`, M10f, M10e, the charset and the bound; a block of nothing is a tool on the pool that
draws nothing. `ShippedContentTests` (+1, +1 amended): the miner ships with its numbers and takes
every shipped solid, a full pack is 562 cube bites; the T cycle without it still straddles the
materials and every tier on it is free. **Twenty-six break-recipes, twenty-six caught first
time** — the budget before `TooHard` or before air, never spent, spent twice, `Unpowered` filed
as `TooHard` or left out of the total, the cube or the tool overload dropping it, affordable
rounding up, a cost of nothing affording nothing, one voxel's bill for any count, a swap
forgetting the forfeit or the outgoing pack's initial or taken, the identity without
`forfeited`, the header writing the channel as zero or reading the pack at v3 or not at v4 or
skipping any of its four checks, the loader ignoring an unserved via or the bound or dropping
the block, a block of nothing reading as none. One recipe is named for what it is: *the budget
spent before the write rather than after* is an identity through an editor whose writes never
fail (M4a's zeroable-epsilon category), so the recipe run was the spend doubled, which the six-
voxel cube caught.

**Amended 2026-09-18 — a swapped-out pack keeps its charge.** The first play's swap (play recipe
step 6) threw a 2,650.6 kJ pack away and Matthew's verdict was *"keep its charge as an item
please"*, which reverses decision 15's *a count has no charge* and c-3's `forfeited`. What
changed: `CarriedPacks` in `Overburden.Core/Inventory/` — the charges of the packs carried, an
annotation on the inventory's count and never a second count (`Stow` keeps nothing for an empty
pack, `TakeFullest` is what a right-click equips, `Entries` in the save form's order); header
**v5** appends a count and a `PackSave` row per charged carried pack after the equipped pack's
section, validated at write and read by the pack's three rules naming *which* entry and by a
fourth — no material's rows outnumber its voxels in the inventory (`… holds 1 of it — a count is
the truth and cannot be exceeded`) — and a v4 world opens with none, which is true of it;
`MachineTick.EquipPack` returns what the outgoing pack held as `PackStowedMillijoules`, the
identity's last term renamed (`… − stowed: exact`), and a stowed pack equipped again comes back
through `initial` so the round trip is two terms that cancel; `Main` stows the outgoing pack
beside the count and equips the fullest carried one (`equipped in place of …: 1,000 J of 3,600.0
kJ (<0.1%) from the inventory … the outgoing pack is back in the inventory holding N J on channel
C; N J stowed by N swap(s), N carried pack(s) charged`), restores the carried charges at open
(`N carried pack(s) restored from the world file holding N J between them`, a material that is
not a pack dropped and said so as the equipped pack's is) and writes them in `CurrentHeader`; the
HUD reads `PACK (in hand, 2,650.6 kJ) · right-click to swap — the worn one keeps its charge`. The
cost stated: a pack swapped out at 0 J loses its channel (no row), which is the price of the count
staying the truth. **The oracle, headless on a scratch world**: the swap recipe reads `the
outgoing pack is back in the inventory holding 1,000 J … 1,000 J stowed by 1 swap(s)`; a reopen
reads `header v5`, `1 carried pack(s) restored … holding 1,000 J`; a second swap on that world
reads `1,000 J of 3,600.0 kJ (<0.1%) from the inventory` with `0 mJ stowed`; and the reopen after
that reads the worn pack at `1,000 J`. `WorldHeaderTests` (+6, every tail offset through a
`CarriedAbsentBytes` of four), `CarriedPacksTests` (+3), the swap test asserting the re-equip;
suite 1,617 → 1,627. The inventory screen's hotbar assignment, written down as "v5" the same
morning, takes v6 — the number is a counter.

**What M10d's play should feel, and where the numbers put it.** A full lead-acid pack is 1,125 cube
bites — 2.2 chunks, 1.9 min at ten a second (562 and 56 s until the first play; decision 16's
amendment) — and recharges at 125 J a boundary, a cube every 12.8 s on any channel with a charged
cell, four hours to full; a swap is a full pack thrown away, said on the HUD before the click.
The seven-question recipe is in TODO.

## The inventory screen — folded into M10g (2026-09-18)

> Matthew, the morning after M10c's first play: *"when do we do player inventory, do I have that
> in the plan anywhere?"* It was in it twice and scheduled nowhere — TODO's "An inventory screen,
> and slots that stay put" under M6d (his words from M6d-3: *"do want to adjust it so slots stay
> put, when we do the inventory screen etc, I was going to allow you to carry more than what is on
> the hotbar maybe?"*), and decision 8's *the hotbar gains a placeable filter and a parts page* at
> M10g. This section folds the first into the second on his word (*"yes please"*), as one screen.
> **It is a plan, not M10g's planning pass** — that pass runs when M10g is next, after M10d, M10e
> and M10f, against the code as it stands then, and splits M10g into its sessions. What this
> section settles is the shape, so that pass starts from decisions rather than from a question.

### Seven facts from the code

1. **The hotbar is a projection of the inventory and nothing else.** `VolumetricInventory.
   MaterialInSlot(slot)` is `Entries()[slot]` — the carried materials in ascending ID order —
   and `Hud.Slots` is nine *"because the keyboard has nine, not because the inventory has a
   limit"*. A tenth holding is carried and reachable only by `V` (`PlaceController.NextMaterial`
   cycles the whole inventory, not the row). M6d-3 chose ID order over first-acquired order
   because acquisition order is written down nowhere and would shuffle across a quit; it recorded
   the cost — *acquiring a new material shifts every slot above it* — and named the fix: **a
   player-arranged slot assignment that survives a quit, a header field and a version**.
2. **M10c's first play hit the limit before M10g's parts exist.** `P` twice put eleven materials
   in the inventory over nine keys; the solar panel and the vacuum cell fell off the end of the
   row and were reached with `V`. Decision 8's ~30 part IDs make every acquired part shift the
   row again, which is why it ships the filter *with* the recipes rather than after.
3. **The header carries holdings and nothing about slots.** `WorldHeader.Inventory` is a list of
   `(MaterialId, Voxels)` at `InventoryEntryBytes = 12`, written in ID order; v4 added the pack's
   section behind a presence byte with `ValidatePack` shared by the writer and the reader. A slot
   assignment is a new section of the same shape, at the next version, under M6c-2's policy that
   an additive version migrates. **Decision 6 reserved "v5" for the inventory's millilitre move;
   the number is a counter, not a name** — the assignment takes the next number after whatever
   ships before it (v5 went to the carried packs' charges the same day this was written, so the
   assignment is v6 as things stand) and the millilitre bump, still on its narrowed trigger,
   takes the one after.
4. **The game has one mouse-releasing UI and every rule a second one needs.** `MachinePanel`
   (M10b-2): a `Control` with `MouseFilter.Stop` owns its clicks and a focused field its keys;
   `FlyCam.ReleaseMouse()` / `CaptureMouse()`; Esc closes, a click outside re-captures and the
   verbs swallow that click through `FlyCam.ClickLive`; flight and walking are gated on capture,
   so a body stops where it stands while any screen is open; `--openpanel` is the screenshot's
   prop and `--panelset=` types into it headless. A full-screen inventory inherits all of it.
5. **`E` is unbound, and decision 11 gives it two meanings**: held within reach of a crank
   machine it cranks; tapped at a workbench it opens the craft panel. A tap *away from* a
   workbench means nothing today.
6. **The row's unit is outgrown and Matthew's to pick.** `Hud.Holding` prints litres `N0`
   (`1,767,903,281 L` in an 84 px slot, a TODO item since 2026-09-03), `× N` for `count`, `× N`
   with a fraction for `cells`. A screen with room for a number is where the unit gets chosen.
7. **Capacity is unbounded, deliberately, and every credit path assumes it.** `Carve.Take`
   credits after the write; a fill's `OutOfStock` is the only refusal the inventory makes.
   M6's recorded triggers for a limit were M10 containers, M11 crafting and *an inventory screen
   as a third and earlier candidate*. The screen arrives; the limit does not have to.

### Eight decisions, each with a recommendation

#### 1. The inventory is the screen; the craft panel and the parts page are panes of it, on `E` tapped — *recommended; the key is Matthew's*

One `Control`, opened by a tap of `E` anywhere: the inventory grid (every holding — swatch, name,
amount in its unit, the pack's charge on its own tile while one is worn), the hotbar row along
its foot, a filter across the top (**all / blocks / parts / machines**, by `placeable` and
`unit`), and a recipes pane that is present when a station is within reach — the `hand` station
always, so hand recipes craft from anywhere, as decision 11 says. Esc closes it, the click
outside does not (a grid is where clicks go), so the close is Esc or `E` again.

- **Rejected: a separate key (`I`, `Tab`)** — a tap of `E` away from a workbench is free, the
  crank is `E` *held* and the two do not collide (decision 11's split is a tap against a hold,
  not a place against a place), and one key that means *open the screen* is one thing to learn.
- **Rejected: three Controls** — the craft panel, the parts page and an inventory screen with
  three ways to open and close. The risks paragraph counted four new Controls; this is three (the
  screen, the machine panel, a crank indicator).
- **Cost**: the screen is the first UI whose content changes while it is open (a bite credits the
  inventory at ten a second; a station's job lands a voxel at a boundary) — the HUD's tenth-second
  throttle is the pattern.

#### 2. The hotbar becomes an assignment — nine slots naming a material or nothing — stored in the header at the next version — *recommended*

`HotbarAssignment` in `Overburden.Core/Inventory/`: nine `int`s, −1 for empty, no duplicates;
`MaterialInSlot(slot)` moves onto it, and `VolumetricInventory.Entries()` keeps its ID-ordered
sort for the save form and the grid's default order. A slot keeps its material when the holding
goes to zero (the tile dims and reads `0 L`; the next bite of stone lands back in *your* stone
slot); a material acquired for the first time takes the first empty slot, or none if there is
none (it is in the grid). Written as a header section behind the carried packs', validated as the pack
is — every ID in the table, no ID twice, exactly nine — and **a v4 world opens with the first
nine holdings in ID order**, which is exactly what its row showed, so nothing moves on the first
open of an old world.

- **Rejected: first-acquired order** — M6d-3's reason stands (nothing records it), and the
  assignment *is* the record that fix needed.
- **Rejected: ID order with pinned slots** — the same object with a worse name.
- **Cost**: a header version, a migration test, and `SelectSlot` reading the assignment rather
  than the inventory; `P`'s seeded materials land in empty slots the way a bite's do.

#### 3. Assign by number key from the grid, and by drag — *recommended*

Click a tile in the grid, press `1`–`9`: that slot now holds it, and whatever the slot held goes
back to the grid (the tile is never lost — a holding is always in the grid; the row is a view
onto nine of them). Drag a tile onto a slot for the same thing; drag a slot onto another slot to
swap. The key path is the one a headless run can drive — `--assign=<slot>=<id>` — so the wire
is testable the way `--panelset=` made the panel's.

- **Rejected: drag only** — one input path that headless cannot exercise, M5c-2's shape of hole.

#### 4. The placeable filter moves from the row to the grid; the row needs none — *recommended; amends decision 8's mechanism, not its substance*

Decision 8 gave the hotbar a placeable filter so that thirty parts would not shift the row. With
an assignment nothing shifts the row, so the filter is the grid's — **all / blocks / parts /
machines** — and the row shows whatever the player put there, a part included (a computer on
`8` is the play tonight). The parts page is the *parts* filter. Decision 8's argument — one
registry, one ID table, one header section, one save form — is untouched.

#### 5. Capacity stays unbounded; the screen shows a total and no limit — *recommended*

A total row under the grid (litres carried across every material, a number the HUD has never
shown) and nothing that refuses. A cap needs a rule for a bite that does not fit — a refused
credit is a deleted voxel or a refused bite, and either is a survival-mode decision with the
reach and the haulage limit (M4d's and 2026-09-03's recorded triggers). **Revisit trigger: a
survival mode, or a play that reads the total as meaningless** — the number on screen is what
makes the second possible.

#### 6. The unit scales: litres under a cubic metre, cubic metres above, on the row and in the grid — *Matthew's; recommended*

`L` under 1,000 L; `m³` from 1,000 L, one decimal under 100 m³ and whole above with separators
(`1,768 m³` for the played world's stone, `15.6 L` for a part's voxel is `count`'s business and
prints `× 1`); `cells` unchanged. One formatter in `Hud`, shared by the row and the grid, which
closes the 2026-09-03 TODO item. The alternative Matthew named was the chunk (512 m³ a click);
recommended against because a chunk is a debug bite's size and not a unit.

#### 7. Core first, the screen second, the header in between — *recommended; the split is M10g's pass's*

The assignment is Core with its battery before any Control exists: the round trip through the
header; a v4 world opening with its ID-order default; a duplicate ID, an unknown ID, a wrong count
refused at read and write naming which; an assignment surviving a holding at zero; a first
acquisition landing in the first empty slot and in none when full; `SelectSlot` reading the
assignment; `--assign=` headless. The screen is a screenshot — `--openinventory` the prop — and
the same three closes the panel has. **M10g's own pass splits its sessions; the recommendation to
it is that the inventory half goes first**, because it depends on no recipe and it is the frame
the craft pane and the parts filter land in.

#### 8. Nothing moves ahead of M10d — *recommended*

The play is next, then M10e and M10f, and M10g's pass after them reads this section first. The
row's overflow is a nuisance tonight, not a blocker: `V` reaches every holding, and the pack sits
on the number row while fewer than ten materials are carried after it.

### What this changes elsewhere

- The M10g row in the split names the screen; decision 8's note points here; the risks
  paragraph's *four new Godot Controls* is three.
- TODO's M6d item "An inventory screen, and slots that stay put" points at M10g and stays ticked
  off there; TODO's M10g line gains the screen and the header version; the outgrown-unit item
  points at decision 6.
- DESIGN's M10 row gains *the inventory screen*.
- Decision 6's "v5 (millilitres)" reads as *the next version after the assignment's* — and v5
  itself went to the carried packs' charges the same day (the amendment at the end of "M10c-3 —
  built"), so the assignment is v6.
