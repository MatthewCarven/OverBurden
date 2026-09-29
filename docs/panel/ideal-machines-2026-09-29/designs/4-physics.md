# Designer 4: physical plausibility. "The fold"

Every number in this design carries a label:

- **[real]**: a real-world figure with a rough source, from general knowledge and not looked up in this pass.
- **[derived]**: arithmetic, shown.
- **[invented]**: a game rule or number I made up.
- **[measured]**: recorded in the repo, with where.
- **[his]**: Matthew's number or word.

"The ladder" is M10-LADDER-PLAN. Taken 1–9 are the brief's; none is reopened.

---

## 1. The idea in five sentences

1. **The top tier is one fold**, a pocket of folded space that every quantum machine and tool opens onto, priced by one invented law that turns his two played prices (the miner's 50 J a voxel, the relay's 1,000 J a litre) into one rule.
2. **The law:** crossing the fold costs a **toll per coherent body** (a solid voxel is one body, 50 J; a litre of water has no lattice, 500 J, so a relay is two crossings); holding costs the toll again every **τ**, set by the emitter's grade; and an **uncontrolled release costs nothing**, which is why a starved vault dumps.
3. **Plausibility shapes ratios and behaviours, not magnitudes:** rock stays ~60,000× cheaper than physics (his generosity, kept) while water stays honestly priced, so a cubic metre of water costs 156× a cubic metre of rock to move, and **the hole, not the dig, is the bill**, as in every real mine where dewatering never stops.
4. **The vault** is a 1 m block whose inside is a cube 2–32 m on a side by its emitter's grade; the backpack is only a window onto the vaults on its channel; one pool pays every toll and hold; and a bankrupt pool makes each vault **vent like a burst tank**, as its own materials, water by Torricelli's law and solids by Beverloo's.
5. **The quarry** is a fold-cutter that benches a box top-down at the miner's toll, and the mine's own physics does the rest: its void is a basin the rain fills and a room the seepage finds, an underground box wider than 16 m brings its roof down (measured at M9a), and when bankruptcy stops the pumps the pit becomes a lake, as the Berkeley Pit did.

### What physics says, what the game does, and the bend

| Quantity | Physics **[real]**, rough | Game | Bend |
|---|---|---|---|
| Breaking and hauling rock | ~200 MJ/m³ (aggregate's embodied energy is ~0.08 MJ/kg, from the ICE database; the ladder's "200 MJ a bite"). Cutting alone is 10–50 MJ/m³ (tunnel boring). | 3.2 kJ/m³ (50 J a voxel **[his]**) | **~62,500× generous [derived]**. His, kept (ladder decision 16). The "coherent body" discount is its fiction. |
| Lifting water | 9.81 J/L per metre; ~14 at pump efficiency η 0.7 | 1,000 J/L flat (the relay) | **About 1×**: it buys 71 m of real lift **[derived]**. Honest. |
| Water against rock | Rock ≥ water per kg, because rock must also be broken | Water is 406× dearer per kg and 156× per m³ **[derived]** | **Inverted**, because rock is nearly free. |
| What a person carries | 20–40 kg; one stone voxel is 40.6 kg **[derived]** | Unbounded; TajMahal holds 4.6 Mt of stone **[derived]** | **~10⁸×**, ruled twice. Not reversed here. |
| Battery energy per volume | Lead-acid 60–90 Wh/L; Li-ion 250–700 Wh/L | Lead-acid bank 28 Wh/L; vacuum cell 1,389 Wh/L **[derived]** | Lead-acid ×0.3–0.5; "god" only 2–5× lithium. |
| Self-discharge | Lead-acid 3–20 %/month; Li-ion 1–3 %/month | None | Invisible at session scale (§6). |
| Wireless charge | ~30 kW over 1.5 km (JPL Goldstone, 1975) | Unlimited, at quantum | A bend at the top tier only. |
| Uncontrolled release | Torricelli (liquids); Beverloo (granular solids) | The same laws in integers | Solids ×2.6. |
| Pillar strength | Pillars crush at high extraction | Never (M9 has no load model) | M9's bend (§7). |
| Saturation after a flood | Hours in soil; days to weeks in hard rock | Tier A only | Fair for stone, wrong for dirt (D19). |

---

## 2. The extras rule

### The engine side

- **An extra is one of six capabilities in a closed C# set**, `MachineExtras`: a `[Flags]` field on `MachineDefinition` plus a small config record each. **Each is defined once**, as one static class in Core answering one hook (`Priority.Order(rows)` at `Rebuild`, `Recall.TryTake(row, pool)` at the collapse seam, `Telemetry.StateOf(row)` at the map's rebuild).
- **Extras dispatch by flag, never by `MachineKind`.** The kind enum widens only for new behaviours: this pass adds `container` (vault, backpack, crates) and `quarry`; f-1 adds `sensor`.
- **A machine's tier is derived from its power link's `via`**, with no new `tier` key: `channel` is quantum, a conductor class (`ac`, `dc`) that tier from M10e, `shaft` steam and `crank` hand from M10e/f. The physical reason: **a signal rides the medium you are connected by.**
- **Inheritance is one JSON table** mapping each extra to its reach per tier, shipped as `mods/overburden/extras.json` the way a mod would. A sixth tier is a row in a mod's own table, with no engine change.

**The loader refuses:** an unknown extra; an unknown key inside `extras`, `part` or `parts` (the new DTOs set `UnmappedMemberHandling.Disallow`, the loader's first unknown-key refusal, so reader A's trap 4 closes for the new grammar without touching old files); an extra the tier's row does not grant; a reach this build does not serve (`component` until M10e, `bell` and `dock` until M10f), naming the milestone as `via` is refused today; and an extra on a kind with no hook for it (`recall` on a carried item).

### The inheritance table (`mods/overburden/extras.json`)

```json
{
  "tiers": {
    "quantum": { "via": "channel" },
    "ac":      { "via": "ac" },
    "dc":      { "via": "dc" },
    "steam":   { "via": "shaft" },
    "hand":    { "via": "crank" }
  },
  "extras": {
    "sense":     { "quantum": "channel", "ac": "component", "dc": "component", "steam": "bell" },
    "recall":    { "quantum": "channel" },
    "remote":    { "quantum": "channel", "ac": "component" },
    "charge":    { "quantum": "window",  "ac": "dock", "dc": "dock" },
    "priority":  { "quantum": "channel", "ac": "component" },
    "telemetry": { "quantum": "channel", "ac": "component", "dc": "component" }
  }
}
```

A tier that is absent from an extra's row does not inherit it. Hand appears in no row: the tint is its sensor, and it is already built.

### Why each medium reaches what it reaches **[real]**

| Extra | Reach below quantum | Precedent |
|---|---|---|
| Sense | Sense over the component (AC, DC); a bell at steam; the tint by hand | Pilot wires (1880s); shaft bell codes; miners' tell-tales ("the roof talks") |
| Recall | None | Only an entangled body can be pulled back **[invented]** |
| Remote panel | Over the AC component | Supervisory control over power-line carrier (1920s); SCADA |
| Charge | Through a dock (M10f, not built) | Beamed power exists (1975), but is kept out to keep DC's dark half |
| Priority | AC only | Under-frequency load shedding is AC-only. A DC bus sags, so **below AC a shortage browns out pro rata** |
| Telemetry | Over the component (AC, DC) | Carrier-current telemetry |

### Two full examples

**The vault.** Its parts are empty at placement. The toll and the throat are the vault's; each grade's numbers live in its part file (§6).

```json
{
  "id": "quantum_vault",
  "hardness": 3.0,
  "supportDistance": 8,
  "permeability": 0.0,
  "density": 7800,
  "textureLayer": 13,
  "unit": "cells",
  "machine": {
    "kind": "container",
    "tollJoulesPerVoxel": 50,
    "tollJoulesPerLitre": 500,
    "throat": { "faces": "sides", "dischargeCoefficient": 0.61, "solidsCubicMetresPerFaceSecond": 1 },
    "parts": {
      "emitter": { "slot": "emitter" },
      "cell":    { "slot": "cell" }
    },
    "links": {
      "power": { "carries": "power", "role": "sink", "via": "channel", "port": "channel" },
      "water": { "carries": "fluid", "role": "sink", "via": "channel", "port": "channel" },
      "stock": { "carries": "stock", "role": "sink", "via": "channel", "port": "channel" }
    },
    "ports": {
      "channel":  { "default": 0, "min": 0, "max": 15 },
      "enabled":  { "default": 1, "min": 0, "max": 1 },
      "weight":   { "default": 1, "min": 0, "max": 100 },
      "priority": { "default": 9, "min": 0, "max": 9 }
    },
    "extras": { "recall": {}, "remote": {}, "priority": {}, "telemetry": {} }
  }
}
```

**The quarry.** The first reader of `Facing`.

```json
{
  "id": "quantum_quarry",
  "hardness": 3.0,
  "supportDistance": 8,
  "permeability": 0.0,
  "density": 7800,
  "textureLayer": 14,
  "unit": "cells",
  "machine": {
    "kind": "quarry",
    "joulesPerVoxel": 50,
    "hardnessThreshold": 5.0,
    "cellsPerBoundary": 1,
    "parts": { "emitter": { "slot": "emitter" } },
    "links": {
      "power": { "carries": "power", "role": "sink",   "via": "channel", "port": "channel" },
      "spoil": { "carries": "stock", "role": "source", "via": "channel", "port": "channel" }
    },
    "ports": {
      "channel":  { "default": 0, "min": 0, "max": 15 },
      "enabled":  { "default": 0, "min": 0, "max": 1 },
      "width":    { "default": 8, "min": 1, "max": 32 },
      "length":   { "default": 8, "min": 1, "max": 32 },
      "depth":    { "default": 8, "min": 1, "max": 32 },
      "room":     { "default": 0, "min": 0, "max": 32 },
      "onwet":    { "default": 0, "min": 0, "max": 1 },
      "onsignal": { "default": 1, "min": 0, "max": 2 },
      "priority": { "default": 3, "min": 0, "max": 9 }
    },
    "extras": { "sense": {}, "recall": {}, "remote": {}, "priority": {}, "telemetry": {} }
  }
}
```

The loader must learn the kinds `container` and `quarry`, `carries: stock`, the blocks `parts`, `throat` and `extras`, and the machine keys `tollJoules*`, `cellsPerBoundary` and `hardnessThreshold`. The quarry's `enabled` defaults to 0, so it never digs the moment it is placed, the habit of any real excavator.

---

## 3. The six extras

| Extra | At the top tier | Inherited down (waits for) | Cost a boundary | Persistence | Printed terms |
|---|---|---|---|---|---|
| **1 Sense** | f-1 exactly, plus `senses: power` (pool under a percentage, or time-to-empty under N min) and `senses: water` (depth or head over N cm). The quarry reads it (default `onsignal` 1). **The vault does not** (§5) | Over the component (AC, DC); a bell at steam; the tint by hand. Waits for M10e/f | Collapse: f-1's estimate of 250 lookups at r = 2. Power: O(1). Water: one `HeldIn`, 10 ns **[measured]** | None (f-1 decision 4) | f-1's `signal:` |
| **2 Recall** | When a fuse ends, the director asks a seam (`IRecallBodies`) which released voxels are a body. A paid row (64 × 50 J = 3.2 kJ **[derived]**) is lifted whole into the store, parts included, **before the piece is cut**. With the pool dry, it is destroyed as today. A recalled vault's share vents | None | Zero. Per fuse, a palette check plus a lookup per body voxel | None new; the charge leaves through `leaving` | `recalled` beside the director's unchanged identity; the toll in `PowerExact` |
| **3 Remote panel** | The computer lists and opens every machine on its channel, not only what is in sight. The vault's stock tab becomes remote inventory | Over the AC component (M10e) | Zero; the list is O(rows), built on open | None | None |
| **4 Remote charge** | The one pool's window (§4) | A dock at DC and AC (M10f; not built) | O(1) | The pack's header entry | Pack terms, plus `refill` |
| **5 Priority** | A `priority` port, 0–9, ordered by (priority, origin). **Containment first:** vault 9, pump 5, quarry 3 | AC. DC browns out pro rata (a later pass) | Only in `Rebuild` | A port | None new |
| **6 Telemetry** | `MapPin` gains a state byte: running, stalled, unpowered, venting, full. The pins themselves stay for every machine, ungated | Over the component (AC, DC) | Zero; gathered at the map's rebuild (3.5 ms **[measured]**) | None | None |

**Recall is at release, not at the drain**: crediting at the drain counts fallen voxels twice (reader C, trap 12).

**Priority arrives now.** M10d decision 4 was waiting for a play that saw starvation, and the vault's hold is that play: pumps that starve a vault flood you.

---

## 4. The one pool

### Energy

- **The window.** The carried quantum pack is `vacuum_pack`, 500 MJ, the ladder's carried vacuum cell **[ladder]**. With the `charge` extra at reach `window`, its charge is part of its channel's pool, and every boundary refills it from the channel with **no rate limit**. The shipped `lead_acid_pack` keeps its 250 W, so no oracle moves.
- **A tool's spend** comes out of the pack in the frame (today's `Pay`). At the boundary the channel pays the refill **first**: the spend has already happened, so it is first by physics, not by policy. The pack is a capacitor on a grid, and a window can overdraw its channel by at most one boundary's spend.
- **Bankruptcy** has no reserve floor and no refusal (taken 4). When the pool runs short:
  - the tool takes nothing (*"failed — insufficient charge"*, his sound);
  - spenders go by priority, and pumps and the quarry stall `unpowered`;
  - a vault whose hold goes unpaid starts to vent (§5).

  Physically that is brownout first, then load shedding.
- **The warning is a fuel gauge with range.** Time-to-empty = pool ÷ last boundary's net draw. It shows amber on the HUD under 10 min and red under 1 min **[invented]**, the same figure appears on the vault's panel, and a `senses: power` sensor can stop a quarry before the vaults go.
- **Printed:** `PowerExact` gains `held` (the hold bills), `tolls` (quarry, siphon, deposits, recall) and `refill`, and its printed string gains the `packInitial` it already omits (reader C, trap 15).

### Items

- **The store is per channel.** It is a ledger of material → voxels plus a litre count, kept in header v8 (§5 says why not in the rows). Its capacity is the sum of the inside volumes of the vaults on that channel.
- **The quantum backpack** (`quantum_backpack`, carried) is a **window with no fold of its own**, and its channel names the store you see. Physically it is a throat: the emitter is in the vault, so capacity lives only in placed blocks.
- **The inventory under it.** The pockets (today's `VolumetricInventory`) stay unbounded and free, as ruled twice. With the backpack worn:
  - the HUD shows pockets and store as one list;
  - placement draws from the pockets first, then from the store at the exit toll of 50 J a voxel (today's fill-at-range price);
  - the pick credits the pockets as today, so no oracle moves;
  - moving a stack between pockets and store is the backpack panel's verb, one toll per crossing.
- **Pushback: his *"emptying your pack stops being a trip"* presumes a bounded pack.** A person carries 20–40 kg, less than one stone voxel, so the unbounded carry is the game's largest bend, and it is ruled. When the survival pass bounds the pockets, "dig routes to the store when the pockets are full" is one rule, and his handover is already built under it. The demo does not need it.
- **Why a container has a gameplay reason in the demo:** not capacity, but three other things.
  1. **Water can only be held in the store** (taken 8).
  2. **A placed machine delivers only to a store.** The quarry's spoil and a pump's water need somewhere to go, and a machine is entangled with a channel, not with your pockets.
  3. **The store has a bill and a failure.** What you keep costs power, and it can be lost *where it stands*, so a vault's position is a decision: *"drop that discharging container somewhere near someone else's mine"* **[his]**.

### Water in the store (taken 8)

- **The siphon** (`quantum_siphon`, a tool on T) is the quantum door each way.
  - **Left-click takes** the water within 2 m of the crosshair, at range: Alt+R made honest, at most 33.5 m³ or 16.8 MJ **[derived]**.
  - **Right-click pours** 1,000 L at the crosshair (100,000 L with Ctrl): R made honest.
  - Both queue to the boundary and pay **500 J/L** from the pool **[invented: half the relay]**, so **a take plus a pour is exactly a relay pump**.
  - Putting his 10 ML in a store costs **5 GJ, one vacuum cell [derived]**.
  - R and Alt+R stay debug doors.
- **Pumps may emit into a vault** as an outlet on its channel, in the same weighted ring: **one crossing, 500 J/L [derived]**.
- **Terms:** `siphoned`, `poured` and `vented` on both tiers, each a twin of `Remove` or `Emit` with its own counter and **never through `Add`** (it would read as `injected`); `pumped == emitted + vaulted`; the store's `ledger + queue == loaded + siphoned + vaulted − poured − vented` in litres.
- **The rejections this amends, out loud:**

  | Rejection | Now |
  |---|---|
  | Ladder decision 6 (the bucket) | Amended by taken 8, quantum only |
  | Ladder decision 13 (a tank on the row) | The litres are in the header's ledger. A failing vault's queue is the one exception, and it is a printed term. **The row is never a tank.** |
  | M10-PLAN D5/D10 (*"nothing is ever held by a machine"*) | Amended. The store holds, and `vaulted` is its term |
  | M10-NOTES piece 3 (*"spill at the output"*) | Still rejected. A full store refuses (the pump stalls `full`), and only a failure vents |

---

## 5. The container ladder and the quantum vault

### The fold, stated once **[invented]**

1. **Toll.** Crossing the fold costs a toll per coherent body: **50 J** a solid voxel and **500 J** a litre of liquid. A relay is two crossings.
2. **Hold.** Every τ, the fold asks the toll again: **bill (W) = Σ toll value held ÷ τ**.
3. **Release.** A controlled release (a siphon's pour, placing from the store) pays the toll. **A vent costs nothing.**
4. **Lost capacity.** When capacity is lost (power failure, dismantle, destruction, recall), its share **springs out where that capacity was**.

Each rule has a physical reading:
- Rule 1 is the only story that reconciles his two played prices.
- Rule 2 makes water 156× dearer than rock to hold as well as to move: water finds its way out of the fold as it finds your excavation.
- Rule 3 is a dam break.
- Rule 4 is a pressure vessel losing volume.

**Corollary [derived]:** a cell whose joules equal a store's toll value holds that store for exactly τ.

### Size, capacity and the bill

- **The body** is one 1 m machine cell, 64 voxels, *"about the same size as the current machine blocks"* **[his]**.
- **The inside** is a cube of edge **2^g m** by the emitter's grade **[invented]**: ×8 the volume a grade, with τ doubling.
- **Voxels and litres share the volume**, a voxel taking 15.625 L.

| Grade | Edge | Inside | Voxels | Litres | τ | Full of stone: toll, bill | Full of water: toll, bill | Water vents in (4 faces) |
|---|---|---|---|---|---|---|---|---|
| low | 2 m | 8 m³ | 512 | 8,000 | 1.5 h | 25.6 kJ, 4.7 W | 4 MJ, 741 W | 1.0 s |
| medium | 4 m | 64 m³ | 4,096 | 64,000 | 3 h | 205 kJ, 19 W | 32 MJ, 2.96 kW | 5.9 s |
| high | 8 m | 512 m³ | 32,768 | 512,000 | 6 h | 1.64 MJ, 76 W | 256 MJ, 11.9 kW | 34 s |
| mythical | 16 m | 4,096 m³ | 262,144 | 4.1 M | 12 h | 13.1 MJ, 303 W | 2.05 GJ, 47.4 kW | 3.2 min |
| god | 32 m | 32,768 m³ | 2,097,152 | 32.8 M | 24 h | 105 MJ, 1.21 kW | 16.4 GJ, 190 kW | 17.9 min |

Edges, τ and the throat are **[invented]**. Tolls, bills and vent times are **[derived]** from them.

**Against the rest of the game's power** (a relay at full rate is 100 kW, a panel 500 W, a vacuum cell 5 GJ): a full god vault of water on one god cell lasts **7.3 h [derived]**, a session, which is his *"just don't let it run out of power like"* at play scale. **His 10 ML** costs 57.9 kW at god grade, and one god cell holds it **exactly 24 h** (the corollary). Stone is cheap to hold; **water is what the fold charges for.**

**The bill is an integer each boundary:** `ceil(toll_mJ × boundaryMs ÷ τ_ms)`. A full god water vault pays 94.8 kJ a boundary **[derived]**, and the product stays under 2 × 10¹⁶, well inside an int64.

**The ladder below: the same body with no emitter is a crate.**
- Inside equals outside: 1 m³, or 64 voxels. It has no bill and no vent.
- The ladder's "crate, bins, a vault" are this body in wood, iron and steel, with stock in the row.
- **They have no reason until survival bounds the pockets**, and they wait for that pass.

**The bound.** Ladder decision 21's `stockVoxels` ≤ 65,536 is plan only. It is amended for the fold to the god emitter's 2,097,152 voxels, enforced through the emitter part's `edgeMetres` ≤ 32, at load and at use.

### The dump: the vent

**Trigger.** At the boundary a vault's hold goes unpaid (after higher priorities), its share of the channel's ledger moves into the vault's **queue**, in its row, printed as `queued`. The share is pro rata by capacity, by material and by litres. Physically, each emitter holds its share of the fold.

**Water follows Torricelli [real]: Q = C_d · A · √(2gh).**
- C_d is 0.61, a sharp-edged orifice **[real]**.
- A is 1 m² per open side face, so four faces on a floor. Nothing vents upward.
- h is the queued volume ÷ the inside floor area. The inside is a room of water with the throat at its foot, so the flow **starts fast and slows**.
- A full god vault's first boundary is 30.6 m³ = **1,958 Tier B cells [derived]**. His 10 ML starts at 33.8 m³/s and is out in **9.9 min [derived]**.
- The cost is one integer square root per failing vault per boundary.

**Where the water goes** is decided by the outlet's own `RoomAt` rules at each face's mouth: a loaded dry mouth goes to Tier B, a lake or analytic ground in the window goes to the basin, and an unloaded underground mouth is owed.

**Solids follow Beverloo [real]: constant discharge, whatever the fill.**
- 0.25 m lumps through a 1 m opening, at bulk density 1,600 kg/m³, run ~990 kg/s, about 0.38 m³/s of solid rock a face **[derived]**. The game rounds that up to **1 m³/s a face [invented, ×2.6]**.
- They go as 1 m pieces through the collapse director, a second source of pieces beside the cut, and **land as their own material, never as rubble**. *"Swim or mine to get it back"* **[his]** needs stone to come back as stone.
- A god vault full of stone empties in **2.3 h [derived]**.
- **With no descending placement the vent waits.** A vault sealed in rock cannot spring into rock.
- A landing in an unloaded chunk is `lost`, printed.
- Real heaps stand at ~35–40° **[real]**; the director stacks pieces instead, which is deferred granular flow's problem.

**Power back stops the vent**, and the queue returns to the ledger: generous, since a burst tank does not reseal (D7).

**Unloaded: "observation collapses the fold" [invented].** A failing vault in an unloaded chunk keeps its queue until the chunk arrives. The exception is water whose mouth is on analytic ground in the window, which goes to the basin at once. This is exactly what the engine can do (reader C, §7). It is taken 9 in fiction: the fold, like the quarry, answers to someone *"on the job site"*.

**Undo.** A vent is the world's write (recording off) and clears the stack, as a fall does. Otherwise Ctrl+Z would credit the heap (reader C, trap 10).

**Dismantle, destruction and recall** lose capacity, so the share vents at the vault's cell (rule 4), and a dismantle returns the parts. A mis-aimed quantum bite on your own vault floods the valley: harsh and on theme, with D9's revisit trigger as the escape.

### The deliberate flood

- **The vault is not an `onsignal` reader**, so a sensor cannot vent it. Starving it by signalling its channel's cells off is emergent, not a feature. Collapse part f's decision 10 stands.
- **By hand, the vault's `enabled` 0 is its emitter switched off.** Its share vents at once, and its cell leaves the pool. That is his *"i am deliberately going to flood that entire valley"* **[his]**, as a valve only a person can open.
- **Physics:** a vent into the workings is Tier B, and saturation is Tier A only (reader E, trap 9).
  - For stone that is fair over a session, because hard rock's pore pressure answers in days to weeks.
  - For dirt and clay it is wrong: soil softens in hours (D19).

### Identity terms and persistence

- **Joules:** `held`, `tolls`, `refill`. The vault's own cell stays a storage row's charge (`entering`, `leaving`).
- **Litres:** `vented`, `poured` and `siphoned` on both tiers; `pumped == emitted + vaulted`; and the store line (§4).
- **Voxels:** **a new identity, built first** (constraint 14): `ledger + queue == loaded + deposited + quarried + recalled − withdrawn − vented`. The director's becomes `released + vented == landed + in flight + lost`.
- **Header v8** holds `mode`, the per-channel ledgers ((material, voxels) plus mL), and the equipped backpack.
- **`ContainerState`** is **one codec that owns the vault's whole blob**, so no second writer replaces it (reader A, trap 5): charge, emitter, cell, vent state, and the queue.
- **The ledger lives in the header** because the fold is one space across a channel's emitters, and no voxel belongs to one vault until that vault fails.
- **A restore never clamps a ledger.** An over-capacity store at open (say, a removed mod's emitter) queues its excess pro rata, printed `restored over capacity`.

### Stock UI

The backpack's panel and the computer on a vault show the same page:

```
store · channel 0 — 22,340 of 32,768 m³ (inside 32 m) · water 10.0 ML
hold 58 kW · pool 4.2 GJ · empty in 20 h at this draw
stone 11,902 m³ · dirt 402 m³ · clay 36 m³ · …
[vault only] emitter: entangler (god) · cell: lead-acid (low) · weight 1 · priority 9
[venting] queued 3.1 ML water, 210 m³ stone — 18.8 m³/s, water out in 5.5 min
```

The HUD line reads `store 68% · hold 58 kW · pool 20 h`. The figures are consistent **[derived]**: 10 ML is 57.9 kW, and the stone 0.46 kW. The `M10:` line gains a `store:` term.

---

## 6. Grades

**Where a grade lives: in the part's ID, installed in the row's part slot.**
- Parts are `unit: count` and `placeable: false`. A new top-level `part` block declares the slot, the grade and the numbers, so **a mod adds a grade by adding a file**.
- A vault keeps its parts in `ContainerState`.
- Right-click with a part on a machine installs it. The part's `slot` separates this from the computer's verb (reader E, trap 5).
- A dismantle returns the parts; a collapse loses them unless the machine is recalled.

```json
{ "id": "emitter/spark_gap", "hardness": 1.0, "supportDistance": 2, "density": 3000,
  "textureLayer": 15, "unit": "count", "placeable": false,
  "part": { "slot": "emitter", "grade": 1, "edgeMetres": 2, "holdSeconds": 5400 } }
```
```json
{ "id": "cell/lead_acid", "hardness": 1.0, "supportDistance": 2, "density": 4000,
  "textureLayer": 16, "unit": "count", "placeable": false,
  "part": { "slot": "cell", "grade": 1, "capacityJoules": 100000000 } }
```

**Rejected homes.**
- **A per-stack grade** (a `CarriedPacks`-shaped annotation) puts a second axis inside a count.
- **A grade in the machine's ID** needs 5 × 5 = 25 vault IDs to allow his *"battery tech maybe 19th century but your actual emitter/entangler could be a higher tech variation"*. Two slots make that mix free.

**What a grade changes: it is a real technology era.** His five words line up with five eras of each part, three real and two beyond.

| His grade | Cell, era **[real]** | Real Wh/L **[real]** | Game J per part | Emitter, era **[real]** | Edge | τ |
|---|---|---|---|---|---|---|
| low | lead-acid (Planté 1859): *"19th century"* | 60–90 | 100 MJ (the ladder's bank; ×0.3–0.5 of real) | spark gap (Hertz 1887, Marconi 1895) | 2 m | 1.5 h |
| medium | nickel-iron (Jungner 1899, Edison 1901) | at most lead-acid's | 300 MJ (the ladder's bank; ×1–3) | thermionic valve (Fleming 1904) | 4 m | 3 h |
| high | lithium-ion (1991) | 250–700 | 1 GJ (×0.4–1.1) | transistor (1947) | 8 m | 6 h |
| mythical | lithium-air (research only) | projected 1,000+ | 2.5 GJ | maser (1954) | 16 m | 12 h |
| god | vacuum, *"quantum vacuum and pressure based"* **[his]** | none | 5 GJ (the shipped cell) | entangler | 32 m | 24 h |

The high and mythical joules, the god emitter, and every edge and τ are **[invented]**.

**What each part sets.** The cell sets the joules its vault adds to the pool (the vault is also a storage row). The emitter sets the inside edge and τ. So capacity comes from the emitter and hold time from the cell, which is M10-NOTES' *"capacity from one part, hold time from another"*. On a quarry the emitter sets the box's largest edge: a cutter reaches as far as it folds.

**Two bends, named.** Real nickel-iron is no denser than lead-acid; its virtue is surviving deep discharge, so the ladder's 3× is a game number. And battery density barely moved for 130 years before lithium, so evenly spaced grades are the price of a ladder that reads.

**No self-discharge.** At a real 5 %/month a cell loses 0.007 % an hour **[derived]**, and the clock runs only while the world is open. To be visible it would have to be ~1,000× real: a tax, not physics, and a printed term for nothing.

**The cost in IDs:** 10 permanent part IDs, plus C# texture layers unless they share them. `ShippedContentTests`' hard counts move. Worldgen places none, so r6 holds, as it did for clay and glass.

**The word.** In mining, **"grade" means ore concentration [real]**: porphyry copper runs ~0.5 % Cu, and "cut-off grade" is everyday language. His word stays, for parts. Ore richness needs another word when ores ship ("assay"), or "high-grade ore" will read as well-made rather than rich.

---

## 7. The automated quarry

### What it digs, and the bound

- **The box sits in front of the body's `Facing`.** Facing comes from the look direction at placement, horizontal only, and the quarry is the first thing that reads it. The box's top is level with the body's top. It is width × length × depth 1 m cells, each clamped at use to the emitter's edge (32 m at god).
- **It digs top-down, one 1 m bench at a time**, in origin order within a bench, **one brush cell (64 voxels) a boundary [invented]**.
  - That is how every real pit is worked: dig under what you have not removed and you have made a stope.
  - It also exposes an underground box's whole roof on the first bench, so M9 judges the roof at once rather than at the end.
- **It skips:**
  - air, at no toll;
  - machine bodies, always, so it never eats your pumps;
  - voxels harder than 5.0, left standing (a "horse" of hard rock left in a pit is real **[real]**);
  - pillars. `room` = N leaves a 1 m pillar between rooms N m wide.
- **It digs only where its chunks are loaded.** An unloaded chunk stalls it `unloaded` (taken 9). Keep-loaded stays rejected, and the README says it in his framing: *"want automated work? We expect you to at least be on the job site for OH&S reasons"*. Real mining law says the same: a working mine has a competent person on site **[real]**.

### What M9a measured, with the same ratios as the shipped rule

The measurements (M9-PLAN §1) used K 240, r 8 and stone step 8, the same ratios as today's 120 / 4 / 4. A real hard-rock room-and-pillar mine runs rooms of ~5–15 m **[real, rough]**, so the arch is plausible.

| Room in stone | What falls |
|---|---|
| A gallery ≤ 8 m wide, any length | Never falls |
| 16 m square | A 20-voxel plug, then it arches |
| 24 m square | ~7,800 voxels (the plan's estimate) |
| 32 m square | **47,600 voxels [measured]** |
| 16 m square, with 2 / 5 / 10 m of head at the underside | 532 / 3,028 / 14,060 voxels **[measured]** |

**So the physics needs no new code.** A god quarry's 32 m underground box brings its roof down dry. `room` 16 keeps it to plugs. A storm that puts 10 m of head over that layout makes it shed 220 m³.

**M9 has no load model, so a pillar never crushes.** A real 1 m pillar at 99 % extraction under 50 m of cover would carry ~210 MPa and fail **[derived: 2,600 × 9.81 × 50 ÷ 0.006]**. That is M9's bend, noted, not this pass's to change.

### What it costs

- **50 J a voxel [his]**: the miner's toll, because the quarry is the miner without you. That is 3.2 kJ a boundary, or **6.4 kW [derived]**.
- A 16 m box is 262,144 voxels: **13.1 MJ over 34 min [derived]**.
- A god 32 m box is 105 MJ over 4.6 h, and **its spoil fills a god vault exactly**: both are 32,768 m³.
- 2 m³/s is about 450,000 t a day **[derived]**. That is the order of the largest open pits' total material movement (Bingham Canyon's peak, rough **[real]**), at about 1/24,000 of the energy of a real pit's ~150 MW **[derived, rough]**: the miner's generosity, unchanged.

**Spoil** goes into the store on the quarry's channel, credited as itself. A full store stalls it `full`; it never spills.

**What stops it:**

| Stall | Cause |
|---|---|
| `done` | The box is finished |
| `full` | The store is full |
| `unpowered` | The pool is short |
| `flooded` | `onwet` is 0 and there is water in the next cell |
| `signalled` | `onsignal` is 1 and the channel is signalled |
| `unloaded` | The next cell's chunk is not loaded |
| `no emitter` | No emitter is fitted |
| — | `enabled` is 0 |

### Water: the hole is the bill

- **An open pit is a basin.** Tier A fills it from its catchment. When the pumps stop it becomes a lake: **the Berkeley Pit** (Butte, Montana; the pumps stopped in 1982 and the lake has risen since **[real]**).
- **An underground box seeps.** A 16 m cube in stone under ~10 m of head has 1,280 m² of wall and roof, so it takes 0.02 × 10 × 1,280 × 0.06 = **15.4 L/s [derived]** at the shipped seep scale: a relay's 15.4 kW. **Digging it cost 13.1 MJ; keeping it dry costs that every 14 minutes, for ever [derived]**, and ×17.5 in dirt. Real mines pump continuously, often more tonnes of water than of ore **[real, rough]**. This is the design's central physical claim: an unattended quarry bankrupts you through the dewatering the dig creates, not through the dig.
- **`onwet` 0** (the default) stalls `flooded` on Tier B water in the next cell or a Tier A lake over it. Pumps become the price of depth.
- **`onwet` 1** siphons that water into the store at 500 J/L, then cuts. This is the bankruptcy arm, and it is off by default.

### The collapse and the undo

**The collapse.**
- The roof's 10–20 s fuse trips the sensor on the quarry's channel, and the default `onsignal` 1 stops it.
- The fall lands rubble in the box, and the quarry resumes, crediting rubble.
- **Ore in the roof is lost as ore.** Real mines report 5–20 % ore loss and dilution **[real, rough]**; here it is 100 % of what fell (collapse decision 8, kept). Block caving, where ore is collapsed on purpose and drawn from below, is where physics disagrees. That is a revisit when ores ship.
- The quarry's body is a 64-voxel pillar. A box that undermines its own footing takes the quarry down with it, unless it is recalled.

**The undo.**
- Its writes are the world's, with recording off.
- The undo drops only the steps whose chunks it wrote, using the existing `Forget`. It does not clear the whole stack, because a quarry runs for hours and clearing the stack every boundary would disable Ctrl+Z while it does.

### Its CPU per boundary

- **In the `machines` bracket:** up to 64 `SetVoxel` calls (~0.1 ms at ~1.5 µs each, TNT-NOTES' Debug estimate, unmeasured on Release) and up to 64 `HeldIn` calls (10 ns each **[measured]**).
- **Off the bracket, the real cost:** one chunk dirtied each boundary for remesh, relight and support. A support derive is 0.8–1.1 ms Release and 4.7–7.5 ms Debug **[measured, M9a and M9 play]**, on the workers. That is a player's bite in that chunk every boundary.
- **The lever** is M10-PLAN D3's second rate, one cell every N boundaries. Its trigger is D3's own: carving that reads as stutter.

---

## 8. Materials

**What the ideal machines are built from.** Plan only, for M10g; nothing here ships in this pass.
- **The vault shell and the quarry frame** are stainless steel (ladder decision 19). The fold is under pressure, so the shell is a pressure vessel.
- **The cells**, by era: lead + sulphuric acid + glass; nickel + iron + potash; lithium + nickel + copper + aluminium + graphite.
- **The emitters**, by era: brass + copper coil; glass + nickel + tungsten; silicon; ruby + copper.
- **Mythical and god parts** are **[invented]**, with lithium and uranium (the ladder's tier 4) as the fabricator's feedstock.

**What the quarry yields** is what is there, credited as itself: grass, dirt and stone. A real quarry's first job is stripping the **overburden**, the waste above the rock you want, so in the demo the quarry *is* an overburden machine.

**Where I cut:** ores and smelting go to M10h/M10g. Ores now would move every fixed-seed oracle (r6, the keystone's 18,718) unless off in the record, by the relief precedent.

**Physics notes for that pass:**
- **Bauxite and lateritic nickel are near-surface weathering deposits [real].** The ladder's "deeper than iron" is a game bend, and M10h should say so.
- Coal comes in stratiform seams. Porphyry copper is disseminated at low grade. Lithium comes from pegmatites or brines **[real]**.
- **Waste dominates real mines**: strip ratios of 2–5:1 are common **[real, rough]**. An ore field should be sparse, and the quarry is how the player moves the waste.

**What this pass adds:** 14 material IDs, all unplaced by worldgen, and one tool.

| Kind | IDs |
|---|---|
| Machines | `quantum_vault`, `quantum_quarry`, `quantum_backpack` (carried container), `vacuum_pack` (carried storage) |
| Parts | 10 |
| Tool | `quantum_siphon` |

---

## 9. The creative-mode demo

**What a mode is.**
- **In Core:** `GameMode { Sandbox = 0, Creative = 1 }`, with Survival reserved.
- **In the save:** a byte in header v8. Every existing world reads as **Sandbox, which is today's game exactly**.
- **On the command line:** `--mode=creative` applies only when a world is created, like `--seed`. `--profile`, `--nosave` and a headless run are always Sandbox.
- **Fly stays as it is.** It is the measurement instrument and the only escape from being buried.
- **The demo is `start.cmd --world=Demo --mode=creative`.** Bare `start.cmd` opens TajMahal (reader D, trap 1).

**What Creative unlocks:**
- **The catalogue:** every material and part, granted into the pockets through a printed `granted` term. It is a real count, so the undo holds (reader D, trap 8).
- **The kit, worn at open:** `vacuum_pack` and `quantum_backpack`.
- **A starter plant, granted once:** a god vault; a god cell charged through the printed `debug` term; 16 panels; two relays and an outlet; a quarry; a sensor.
- **Power is real.** Never `--nogate`, which hides all of it (reader D, trap 2).

**The first five minutes teach the ratios:**

| Time | What happens | The lesson |
|---|---|---|
| 0:00 | Spawn on a slope above a basin. The HUD reads `pool 5.0 GJ · store 0 of 32,768 m³`. | |
| 0:30 | Two excavator bites take a pit of 1,024 m³ for **3.3 MJ, 0.07 % of the pool [derived]** | **Rock is nearly free.** |
| 1:30 | Rain or seepage finds the pit. The siphon takes 100 m³ for **50 MJ [derived]**: a tenth of the volume at fifteen times the price. The HUD's hold figure appears. | **Water is what costs.** |
| 2:30 | The quarry goes on the rim with a 16 m box and starts benching at 1 m³ a boundary. Spoil climbs in the store. | |
| 3:30 | The pit reaches the basin's level and the quarry stalls `flooded`. Pumps go in, into the vault at 500 J/L or to the outlet on the hill at 1,000. | **Depth is a water bill.** |
| 4:30 | The map shows every machine with its state. The stope's roof tints; the sensor stops the quarry. | |
| 5:00 | Set the vault's `enabled` to 0. Its emitter lets go: the 100 m³ comes out as a spring over about a minute, and the spoil as a heap **[derived]**. Bankruptcy does this to every vault at once. | **Bankruptcy is a feature.** |

**What it must not break:**
- **r6** still reads `2601 / 325 / 3446 at 411`, because headless is Sandbox.
- **Persistence:** v8 is additive, and a v7 world opens as Sandbox with an empty store.
- **Survival:** it does not exist yet, and nothing here assumes it.
- **The ladder:** the lead-acid pack, `via: channel` and the five shipped machines are unchanged.
- **Every pump and power oracle** runs with `vaulted 0`.

---

## 10. The split

Each slice is a session or less, Core and tests cut from the wire; f-1 goes before or beside IM-6.

| Slice | What | Test or oracle |
|---|---|---|
| **IM-1** Core | Header v8 (mode, ledgers, backpack); the store; **the new voxel identity** | v7 → v8 migration; round trip; the identity exact over 1,000 random steps; an over-capacity restore queues and never clamps |
| **IM-2** Core | `part` blocks and slots; `ContainerState`; kind `container`; the hold bill; priority; the `held` term | §5's bill table to the mJ; a short pool fails vaults in priority order; a mutation battery on the bill |
| **IM-3** Core | The vent: queue; integer Torricelli and Beverloo; `vented` twins on both tiers; the director's second source; owed while unloaded; closes when power returns | 10 ML in a god vault drains in 592 s ± one boundary; both tiers exact; a sealed vault waits; an unloaded vault releases at arrival |
| **IM-4** Core | The siphon and its twins; `vaulted`; the cross-tier line | A take plus a pour costs exactly 1,000 J/L; `pumped == emitted + vaulted`; stall `full` |
| **IM-5** Core | Kind `quarry`: box, order, `room`, skips, stalls, `onwet`, writes with recording off, `Forget` | Dig order; pillars left standing; a 32 m box sheds M9a's trapezoid and a 16 m box a plug; stall `flooded`; never takes a machine |
| **IM-6** Core | Extras flags, `extras.json`, the refusals; recall's seam; `MapPin` state; the `power` and `water` senses | Every refusal named; recall keeps the director's identity exact; a bankrupt recall destroys |
| **IM-7** Game | HUD, the windows, the stock tab, the siphon on T, the vent's look, map state, the printed terms | **r6 to the digit**; a headless vent oracle (`--placeat=` a god vault, starve it, read `vented`) |
| **IM-8** Game | Creative mode, the catalogue, the starter kit | r6 unmoved; a creative world opens; a Sandbox header differs only in its version |
| **IM-9** | **His play** | τ, vent speed, the quarry's rate |

---

## 11. Numbered decisions

Each decision has its recommendation, then its cost, then the trigger that would revisit it.

1. **The fold law [invented].** A toll per coherent body (50 J a voxel, 500 J a litre); hold = toll ÷ τ; a vent is free; lost capacity springs out where it was.
   - *Cost:* water costs 156× rock by fiat.
   - *Revisit:* he re-prices a played number (the law follows it), or any water door comes in under 1,000 J/L for a round trip.
2. **A relay is two crossings; a pump into a vault is one (500 J/L).**
   - *Cost:* starving a filled vault turns it into a half-price relay whose outlet is an uncontrolled spring.
   - *Revisit:* that becomes the main way water is moved. Then charge 1,000 J/L on entry and make controlled exits free, which re-prices the fill at range (his call).
3. **The inside edge is 2^g m on a 1 m body; `stockVoxels` becomes 2,097,152.**
   - *Cost:* a full god vault vents 2.1 M cells, 60 % of the load test.
   - *Revisit:* the water engine cannot carry a god vent at 2 Hz. Then cap liquids at the mythical edge.
4. **τ runs from 1.5 h to 24 h by emitter grade [invented].**
   - *Cost:* a full god water vault draws 190 kW.
   - *Revisit:* in his play the hold reads as an unnoticed tax or as a treadmill. It is one number per part file.
5. **Vent rates: Torricelli for water, and a constant 1 m³/s a face for solids (Beverloo ×2.6).**
   - *Cost:* one integer square root per failing vault per boundary, and a curve to read.
   - *Revisit:* the curve is invisible in play. Then use a constant rate.
6. **Vented solids land as themselves through the director.** They wait with no descending placement and are printed `lost` if the landing is unloaded.
   - *Cost:* the director gets a second source of pieces.
   - *Revisit:* those pieces show on `boundary worst`.
7. **Power back stops a vent.**
   - *Cost:* generous; real tanks do not reseal.
   - *Revisit:* players meter floods by flickering the power. Then a vent, once started, finishes.
8. **Unloaded: "observation collapses the fold".** Water on analytic ground inside the window still goes to the basin. This is taken 9 applied to vaults.
   - *Cost:* a far-off vault fails with no consequence until you visit it.
   - *Revisit:* multiplayer.
9. **The store is per channel, in the header.** Capacity is the sum of the vaults. The pockets stay unbounded, the backpack is a window, and dismantling a vault vents its share.
   - *Cost:* two lists shown as one, and emptying the pack is not a demo feature.
   - *Revisit:* survival bounds the pockets, or accidental dismantles read as unfair (then the pick bounces off a vault that holds a share).
10. **Water lives only in the store.** The siphon takes a 2 m sphere or pours 1,000 / 100,000 L, at 500 J/L each way, quantum only.
    - *Cost:* a new bite, plus three twin counters on each tier.
    - *Revisit:* taken 8's limits.
11. **The one pool's window.** `vacuum_pack` refills with no rate limit, tools are paid first, there is no reserve, and a warning shows under 10 min. The lead-acid pack is unchanged.
    - *Cost:* unlimited wireless power, and one boundary of overdraw.
    - *Revisit:* bankruptcy arrives unwarned in play.
12. **Priority 0–9: vault 9, pump 5, quarry 3.** AC inherits it; DC browns out (a later pass).
    - *Cost:* ends M10d decision 4's wait.
    - *Revisit:* M10d's storm play prefers the pumps nearest the water.
13. **Grades are parts, with the grade in the part's ID and the part in a row slot.** The cell sets joules; the emitter sets the edge and τ; each grade is a real era.
    - *Cost:* 10 permanent IDs and an install verb.
    - *Revisit:* no one mixes grades. Then use one part per tier.
14. **No self-discharge.**
    - *Cost:* a little realism.
    - *Revisit:* the clock ever runs while the world is closed.
15. **The quarry.** A box no wider than the emitter's edge, in front of `Facing`; top-down 1 m benches at 64 voxels a boundary and 50 J a voxel; spoil to the store; `room`; eight stalls; `onwet`; loaded chunks only (taken 9).
    - *Cost:* the first machine that writes voxels, and one support derive a boundary per quarry.
    - *Revisit:* the D3 stutter, or `machines` over 1 ms.
16. **The quarry writes as the world does.** The undo drops only the steps whose chunks it wrote.
    - *Cost:* a step near a running quarry can vanish.
    - *Revisit:* that reads wrong. Then pause the quarry while a step touching its box is on the stack.
17. **Recall is quantum only, taken at release, whole and paid for.** A recalled vault's share vents.
    - *Cost:* the director asks a seam about machine voxels.
    - *Revisit:* his TNT decision 5.
18. **The extras rule.** Flags, the `extras.json` reach table, the tier taken from the power link's `via`, and unknown keys refused in the new blocks.
    - *Cost:* the loader's first unknown-key refusal.
    - *Revisit:* M12's scripting.
19. **Saturation stays Tier A.**
    - *Cost:* a flooded dirt room stands, where real soil softens in hours.
    - *Revisit:* that reads wrong in play. Then Tier B depth becomes a head input, in an M9 pass.
20. **Materials: 14 IDs, none placed by worldgen.** Ores and smelting go to M10h/M10g, with §8's notes.
    - *Cost:* the demo's quarry yields stone and dirt only.
    - *Revisit:* M10h.
21. **Creative is a header mode, for new worlds only,** with a `granted` catalogue and real power. Sandbox is today's game.
    - *Cost:* it contends with M10g for header v8.
    - *Revisit:* the survival pass.
22. **The vault is not an `onsignal` reader.**
    - *Cost:* collapse part f's decision 10 rests on the player's restraint.
    - *Revisit:* his word.

---

## 12. Must not ship

1. **An instantaneous dump.** 32 ML in one boundary is a load-test flood in a frame, and it is the implausible behaviour too. Vent at the throat.
2. **Vented solids as rubble.** Contents land as themselves, or *"mine to get it back"* is a lie.
3. **Water priced per kilogram like rock.** At 3.2 J/L the siphon is a free pump and the antagonist dies.
4. **A toll on the vent.** Charging it makes bankruptcy cheaper to survive than to cause.
5. **Siphon or vent through `FluidSimulation.Add`, or vault inflow through `Emit`.** The first misfiles as `injected`; the second breaks `pumped == emitted`. Use twins with their own counters.
6. **`StorageState.Write` on a vault's row.** It replaces the whole blob, parts and queue included. `ContainerState` owns it.
7. **A clamp on a store at restore.** It destroys volume. Queue the excess and print it.
8. **Journalled quarry or vent writes.** Ctrl+Z would credit the pit or the heap.
9. **Hardness as a price at the quantum tier.** The toll is per body. Hardness-priced cutting belongs to the mechanical drills' pass.
10. **A bottom-up or random dig order.** It undercuts, and hides the roof from M9 until late.
11. **A reserve floor "to be kind".** Taken 4 forbids it.
12. **Self-discharge.** A printed term for nothing.

---

## 13. My design's three weakest points

1. **The "coherent body" discount is a fig leaf.** It reconciles two played numbers that disagree by ~400× per kilogram, in the opposite direction to every real mine. A thoughtful player will find water 156× dearer than rock arbitrary. It keeps the antagonist alive and makes the hole the bill, but it is invented, and the whole design rests on it.
2. **The container's reason in the demo is not the one he asked for.** He asked that *"emptying your pack stops being a trip"*, and with the pockets unbounded as ruled it never is one. The vault's demo reasons are water, spoil and risk; capacity waits for survival. If he wants carry pressure in the demo, this design has none to offer without reversing the "no encumbrance" ruling.
3. **The vent may be more physics than play can see, and the top grade more water than the engine can carry.**
   - Torricelli, Beverloo and "observation collapses the fold" are a lot of rule for *"suddenly it has to find 10,000,000 litres"*.
   - A full god vault is 60 % of the load test. The cap at the mythical edge may be needed on day one, and I cannot price it without the bench.
   - With saturation Tier A only, the one flood a player can cause at will does not weaken the ground it floods, and that undercuts the keystone the design leans on.
