# Designer 3: the mod-format angle ("the JSON is the design")

Written 2026-09-29 against `main` at `71e6eed`, from the brief, the five reader maps, and the loader
itself (`ContentLoader.cs`, `Registry.cs`, `ModManifest.cs`, `MachineDefinition.cs`, all 19 shipped JSON
files). It folds in **taken 8** (water in the inventory at the last tech, through a tool or machine made
for it). Figures are labelled **measured** (with a source) or **estimate**.

---

## 1. The idea in five sentences

1. **A kind is a home; an extra is a verb.** Anything that *holds* a conserved quantity (litres, joules,
   voxels) or *writes the world* is a kind, in C#, because the engine must print its terms. Anything that
   only reads, gates, orders, or moves a quantity between homes that already exist is an extra. Extras are a
   closed C# set of six, and a machine opts in with an `extras` object in its JSON.
2. **Inheritance is reach, and reach is data.** Each medium (`channel`, later `ac`, `dc`, `shaft`, `crank`)
   gets a `media/<id>.json` naming the extras that travel over it. A machine's effective extras are what it
   declares intersected with what its medium carries. So *"the previous tier may or may not inherit"* is one
   line per tier, written by whichever mod owns that tier, the base game included (non-negotiable 5).
3. **The loader becomes strict and order-free.**
   - Unknown keys are refused, naming the file and key; today they are silently ignored.
   - Engine-owned port names belong to their owner.
   - Every cross-file relation is either one-owner or a commutative union, so nothing but numeric ID
     assignment (which already has its rule) depends on mod order.
4. **Grades live in part IDs, never machine IDs.** A part's ID ends in `/grade`. A machine's slots *scale*
   its god-grade numbers by the installed part's factor, so the machine JSON *is* the ideal machine
   (taken 3). IDs grow as parts × grades, not machines × grade combinations.
5. **Two new kinds carry the pass**:
   - `vault`: the first stock, and after taken 8 the only home for litres outside the world;
   - `quarry`: the first machine that writes voxels.
   
   A third medium value, `carries: "items"`, makes the one store a channel's membership exactly as the one
   pool is. It also makes a future conveyor one media file rather than an engine change.

---

## 2. The extras rule

### 2.1 The line between a kind and an extra

`MachineKind`'s twenty-odd switch sites (Reader A §1.7) exist because each kind has its own slot in the
tick and its own identity terms. Made explicit:

| If the feature… | it is… | examples |
|---|---|---|
| holds a conserved quantity in a new place | a **kind** (C#) | `storage` (joules), `vault` (voxels, litres) |
| writes voxels into or out of the world | a **kind** (C#) | `quarry`; later TNT's `charge` |
| emits a signal from world state | a **kind** with a `senses` value, as f-1 builds | `sensor` |
| reads, gates, orders, or moves a quantity between existing homes | an **extra** (C#, closed) | the six |

**Capabilities do not replace kinds.** "A pump that also stores" would need every combination's tick
order and identity defined. **Amend M10-PLAN D1** (*"a new kind is M12's scripting"*), which f-1 already
breaks once and this pass twice more, to: *kinds are C# in every milestone, M12 included; a script may
compose extras, never a home.*

### 2.2 The engine side: defined once, dispatched by a bit

```csharp
[Flags] public enum MachineExtra : byte
{ None = 0, Respond = 1, Recall = 2, Remote = 4, Window = 8, Priority = 16, Telemetry = 32 }
```

**Computed once, stored per material.**
- `MachineDefinition.Extras` is computed at load: declared ∩ reach (2.4), after patches (2.5).
- `MaterialTable.ExtrasOf(id)` projects it as one byte per material ID: one array read, like `IsMachine`.
- **It is per material, never per row.** That is what lets recall work: the collapse director already reads
  material properties, so a material bit lets the fall skip a recallable body's voxels without learning that
  machines exist. `IMachineBodies.cs:10-17`'s rule holds. A per-row flag would make the director hold
  `MachineSet`.

**One class per extra, a fixed set of call sites, a bit test at each.** No extra adds a case to the kind
switches.

| Extra | Where it runs |
|---|---|
| `respond` | f-1's override in `MachineTick` |
| `recall` | `CollapseDirector.Release`, via `MaterialTable`, and the drain |
| `remote` | the computer's list verb (Game) |
| `window` | `BeginPower`, `WriteBack`, the store view |
| `priority` | `Rebuild`'s sort key |
| `telemetry` | `MapOverlayNow` |

**An applicability table (C#)** says which kinds an extra may attach to:
- `respond`: pump, placed storage, placed vault, quarry;
- `window`: carried storage and carried vault;
- `priority`: pump, vault, quarry, carried storage;
- `recall`, `remote`, `telemetry`: every placed kind.

**Engine-owned port names**, each registered with its owner and its bounds:

| Port | Owner | Bounds |
|---|---|---|
| `channel` | medium `channel` | 0..15 |
| `enabled` | kind `storage` (placed) | 0..1 |
| `weight` | kind `outlet` | 1..100 |
| `onsignal` | extra `respond` | 0..2 |
| `priority` | extra `priority` | 0..15 |
| `radius` | sense `collapse` | 0..4 |
| `below` | sense `powerlow` | 0..100 |
| `width`, `length`, `depth` | kind `quarry` | 1..`reachCells` |

The loader **materialises** an owner's ports, so a mod never needs to write them. This closes a live hole:
today a mod may declare `channel` up to 255, and the tick clamps it to 15 without a word.

### 2.3 The grammar

Only new keys are shown; every existing key keeps its meaning.

```
machine.extras   : { <extra>: { "default"?: int } }                 // seeds the extra's owned port
machine.slots    : { <slot>: { "family": <id>, "scales": <number> } }
machine.capacityLitres, machine.hold { "watts", "wattsPerMegalitre" }     // kind vault
machine.reachCells, .voxelsPerSecond, .hardnessThreshold, .joulesPerVoxel // kind quarry
machine.senses   : "collapse" | "powerlow"                            // kind sensor (f-1)
material.part    : { "family": <id> }
material.grades  : { <grade>: { "factor": number } }                  // one file → N IDs
material.programmer : true                                            // the computer's verb
link.carries     : "power" | "fluid" | "items"
tool.takes       : "voxels" (default) | <fluid material id>;  tool.power.joulesPerLitre
media/<id>.json  : { "id", "engine"?, "carries", "extras", "maxRunCells"?, "maxWatts"? }
patches/*.json   : { "target": <id>, "addExtras": ... }
palette/*.json   : { "sections": { <section>: [ <id>, ... ] } }
mod.json         : + "format"?, "description"?, "authors"?
any object       : keys starting "$" are allowed and ignored ($comment, $schema)
```

**The shipped relay pump under the new grammar.** Its `channel` port is now materialised; writing it by
hand is accepted if identical, so today's file loads unchanged.

```json
{
  "id": "relay_pump",
  "hardness": 3.0,
  "supportDistance": 8,
  "permeability": 0.0,
  "density": 7800,
  "textureLayer": 5,
  "unit": "cells",
  "machine": {
    "kind": "pump",
    "litresPerSecond": 100,
    "headMetres": null,
    "joulesPerLitre": 1000,
    "intakeRadiusCells": 1,
    "links": {
      "power": { "carries": "power", "role": "sink",   "via": "channel", "port": "channel" },
      "water": { "carries": "fluid", "role": "source", "via": "channel", "port": "channel" }
    },
    "extras": {
      "respond":   { "default": 0 },
      "priority":  { "default": 8 },
      "recall":    {},
      "remote":    {},
      "telemetry": {}
    }
  }
}
```

Its ports come out as `channel` (0..15), `onsignal` (0..2) and `priority` (0..15, default 8). That is 3 of
the 16 allowed; materialised ports count toward the bound.

### 2.4 Inheritance is reach, declared per medium

A tier *is* its membership rule (the ladder: *"the five tiers differ only in how a machine is assigned its
pool"*), so what a tier inherits belongs to its medium:

```json
// mods/overburden/media/channel.json — the quantum tier
{ "id": "channel", "engine": true,
  "carries": ["power", "fluid", "items"],
  "extras": ["respond", "recall", "remote", "window", "priority", "telemetry"] }
```

```json
// mods/overburden/media/solar.json
{ "id": "solar", "engine": true, "carries": ["power"], "extras": [] }
```

**The lower tiers are recommended now and shipped only with their membership (M10e/f).** A media file for a
medium the build does not serve is refused, as `"via": "dc"` is today.

| Medium | carries | Run bound, watts | Extras that travel |
|---|---|---|---|
| `ac` | power | null, 200 kW | respond, remote, priority, telemetry |
| `dc` | power | 24 cells, 10 kW | respond |
| `shaft` | power | 16 cells, 20 kW | none |
| `crank` (engine) | power | — | none |

**How it resolves.**
- All of a machine's links name one medium. A sensor has no links (f-1 decision 6), and in this build its
  kind puts it on `channel`: f-1 requires the channel port, which is now materialised.
- **Effective = declared ∩ reach.** A declared extra the medium does not carry is **refused**, naming the
  media file, never silently dropped.
- A `via` with no media file is refused.
- **There are no parents, so there are no cycles and no missing parents**, by construction.

**Rejected: per-machine `extends`.**
- Tier machines differ in nearly every number, so an `ac_pump extends relay_pump` overrides almost
  everything and still couples the two: a retune of the quantum pump would move the AC pump.
- It needs a merge rule for every nested object and every future key, cycle detection, and a rule for a
  parent in an optional mod.
- The one place duplication hurts, grades, is served by `grades` (section 6), which references no other
  file.

*Revisit* at the first content with three or more machines differing in two numbers or fewer, after grades
exist.

**Media are namespaced content IDs.** Two mods defining `pipe` must not collide.
- A bare `via` resolves in the referencing mod's own namespace, then through its declared `dependencies` in
  order. An ambiguity is refused, naming both.
- Third-party mods therefore declare `"dependencies": ["overburden"]`. The shipped `"via": "channel"`
  still loads.
- Media never enter a row or the ID table, so this costs no persistence.

**Conductor numbers move from the conductor material to the media file.** The ladder's `copper_wire.json`
carried them, and two dc materials with different run bounds would leave a component's bound undefined.
That is plan-only, so the move is free.

### 2.5 Patches: adding an extra to a machine you do not own

Today a second definition of `overburden:relay_pump` fails in `Registry.Register` (*"already has a
definition"*). The explicit `ns:path` form reaches only **new** IDs, so a mod cannot give the base pump
recall.

```json
// mods/pumpsafe/patches/relay_pump_recall.json
{ "target": "overburden:relay_pump", "addExtras": { "recall": {} } }
```

- **A patch may only add extras**, to a material or to a medium's reach (`"target": "media:overburden:ac"`).
  That is a union: commutative and idempotent.
- **Refused:** any number, any removal, any change of kind or link. Extras are re-validated against kind
  and reach after all patches, so a reach patch in one mod can license a material patch in another, in
  either order.
- **Missing targets:** a target in an unloaded namespace is a **warning** (compatibility patches are
  legal). A target missing from a loaded namespace is an **error**.
- **Uninstalling the mod removes the extra.** Its port (say `priority`) goes through `Restore`'s existing
  path for unknown ports: dropped and counted.

### 2.6 Load order, and why it cannot matter

The passes, in order:
1. manifests, as today;
2. **media**, from every mod;
3. **materials**, in today's order (the base game first, then by mod ID, then by file name). The `grades`
   expansion runs here, in *document* order via `JsonElement.EnumerateObject`, since a `Dictionary`
   promises no order;
4. tools;
5. **cross-references**: slot families to parts, `takes` to a fluid, palette IDs, `carries` against the
   medium;
6. **patches**, then re-validation;
7. freeze.

Every cross-file relation is one-owner (a second declaration is refused whichever loads first) or a union.
**Only numeric ID assignment depends on order**, and it already has its rule.

**Test:** a three-mod fixture loaded in all six orders gives identical `ExtrasOf`, reach and palette.

### 2.7 What the loader refuses

**Unknown keys, at every level.** Each DTO gains a `[JsonExtensionData]` bag, and any key not starting
with `$` is an error with a did-you-mean (edit distance ≤ 2):

> `pumpsafe/materials/big_vault.json: unknown key 'machine.capacityLiters' — did you mean 'capacityLitres'?`

.NET 8's `UnmappedMemberHandling.Disallow` is rejected because it throws on the first key, with no
suggestion, and on `$comment`.

**What it does to existing mods:**
- No third-party mod exists.
- **Every key in the 19 shipped files is known**, and so are the keys in the five content test files; I read
  or grepped all of them.
- The only content it breaks is the ladder's example JSON (`stockVoxels`, `watts`, `dock`, `conductor`,
  `fuel`), which today loads with those keys silently absent. That is the point.
- **The real cost:** a mod written for a newer build fails on an older one instead of half-loading. That
  is M10a decision 11 (*"the lamp that did not glow"*) carried to keys. `mod.json`'s `format` turns forty
  unknown-key errors into one: *"targets content format 3; this build reads 2"*.

**The other new refusals:**

| Area | Refused |
|---|---|
| Extras | a name not in the six; a kind or medium that cannot take it |
| Owned ports | a hand-written owned port that differs from its owner's declaration; `onsignal` without `respond`; any port nothing reads (revisit at M12) |
| Links | a `port` that is not the medium's own (today `"port": "freq"` validates, then ticks on channel 0); `carries` the medium does not |
| Media | a second owner; a reserved name without `"engine": true`, or `engine` on any other name (so a typo `chanel` cannot become a conductor class); a medium the build does not serve |
| Slots | a family with no parts; `scales` on a number the kind lists as unscalable; slots on a carried machine; a number both declared and scaled |
| Kinds | a vault with `hold` and no power link; a quarry with no `items` source link; `hardnessThreshold` on a fluid tool |
| Patches | any non-additive operation |

One bad file still skips one material, not the game. **A skipped material that a world already holds
becomes a stone-like placeholder**, which is the sharpest edge of the strictness (section 13).

### 2.8 The ladder's format rules: kept and amended

| Rule | Verdict |
|---|---|
| Bounds refused at load and at use, never clamped; `null` is unbounded (d21) | **Kept**, including part factors. |
| A row read from disk is clamped to bounds, `clamped` counted (d21) | **Amended**: a setting is clamped; **contents never are**. Over capacity, a vault keeps what it holds, refuses deposits and counts `overfull`. |
| Labels from identifiers, never a field (M6d-3) | **Kept**, plus one derivation: a `/` segment renders in parentheses, "entangler (mythical)". |
| Engine `via` names reserved, other strings are classes (d21) | **Kept and tightened**: `"engine": true` required, media namespaced. |
| "A third-party sixth tier is one conductor JSON and one converter JSON" (d21) | **Kept and made literal**: one media file plus its machines, and its extras reach comes with it. |
| `stockVoxels` ≤ 65,536 (d21, plan only) | **Amended**: unpowered containers keep it as `capacityLitres` ≤ 1,024,000; a vault with `hold` may declare up to 100,000,000 L. |
| ≤ 16 ports; a channel link needs the channel port | **Kept**; materialised ports count, and the channel port is materialised. |
| Conductor numbers on the material | **Amended**: they go on the media file. |
| Unknown keys ignored (default, never decided) | **Reversed.** |

---

## 3. The six extras

Per-boundary costs are **estimates** from the Release bench's 0.6 ms per 100 wet pumps and 0.02 ms per 500
dry (M10-LADDER :1399-1412, measured).

**1. `respond`.**
- **At quantum:** this is f-1, re-expressed and not changed: kind `sensor`, `senses: "collapse"`, no links,
  a per-channel boolean recomputed every boundary and never stored.
- **What x-1 changes:** it moves `onsignal` from the hand-written port on `vacuum_cell.json` and
  `relay_pump.json` into `extras.respond`. The name, bounds and default are identical, so rows saved under
  f-1 restore unchanged.
- **This pass adds** the sense `powerlow`, which trips while the pool is below `below`% of capacity: the
  warning before a dump. The vault and the quarry become readers.

```json
// materials/power_sensor.json — f-1's kind with a second sense; its ports are materialised
{ "id": "power_sensor", "hardness": 1.0, "supportDistance": 2, "permeability": 0.0, "density": 2000,
  "textureLayer": 23, "unit": "cells",
  "machine": { "kind": "sensor", "senses": "powerlow", "extras": { "remote": {}, "telemetry": {} } } }
```

It gets `channel` (from the medium, via the sensor's rule) and `below` (0..100, from the sense).
- **On a vault it gates transfers, never the hold**, so a sensor can never empty a vault into the mine.
  Collapse f decision 10 stands.
- **Down the ladder:** `ac` and `dc` (within the component), not steam or hand. The brief's steam "bell"
  is presentation, not a capability, so it is cut. **Nothing to read until M10e.**
- **Cost:** f-1's (2r+1)³ × 2 lookups per sensor (250 at r = 2, estimate) plus a bit test per reader.
- **Persistence:** the port. **Terms:** none, since it is a gate; f-1's `signal:` line prints it.

**2. `recall`.**
- **At quantum:** at `Release` the director skips recallable voxels. They go to air, stay out of the
  piece, and are counted `recalled`. The director's identity becomes `released == landed + in flight + lost
  + recalled`.
- **At the drain,** `Lost − Carved == Recalled` credits the body (64 voxels) and its parts, recording-off.
  That closes Reader C's double count and its undo gap.
- **Down the ladder: deliberately not.** No lower media file lists it.
- A blast (TNT) should not recall: recall answers the mine, not you. That is his call, with TNT's six.
- **Cost:** zero per boundary; one byte read per released voxel.
- **Terms:** `recalled` on the director's line, and `recalled N` on `M10:`.

**3. `remote`.**
- **At quantum:** today the computer's click on nothing opens the pack's panel. It gains a list of the
  machines on the pack's channel that carry `remote`, and picking one opens its panel at any range, loaded
  or not. Rows tick unloaded, so ports and stock are readable anywhere.
- **Down the ladder:** `ac`, within the component. That waits on M10e's component ids.
- **Cost:** zero. **Persistence and terms:** none.

**4. `window`** (remote charge, at its top; section 4).
- **At quantum:** a carried storage or vault becomes a **member** of its channel's pool or store, not a
  consumer of it.
- **The brief's "DC and AC dock only (exists)" is false.** No dock exists, and the lead-acid pack recharges
  over `channel` from anywhere at 250 W (Reader A trap 12).
- **Down the ladder:** below quantum, recharge at `rechargeWatts` is simply what a carried storage does on
  its medium (docked on `dc`, M10f). It is not an extra.
- **Cost:** O(1) per boundary. **Terms:** the pack moves from `pack` into `stored` in `PowerExact`.

**5. `priority`.**
- **At quantum:** a port 0..15; higher draws first; ties by origin *position* (X, Y, Z), which is today's
  rule and not placement order (Reader B §8).
- It orders pumps, vault holds, quarries and the pack.
- It is strict order, not M10d decision 4's weighted fairness, which still waits on the storm play.
- **Down the ladder:** `ac`.
- **Cost:** a sort key at `Rebuild`, which runs only on a version change. **Persistence:** the port.

**6. `telemetry`.**
- **At quantum:** F12's pins, which already show for every machine, gain state by colour and rim:
  stalled, unpowered, signalled, dumping, overfull.
- It is gathered at the map's rebuild only, and must pass the bearing-independence oracle.
- **Down the ladder:** `ac`.
- **Cost:** zero per boundary; microseconds per map rebuild (estimate).

---

## 4. The one pool

Taken 4 in format terms: **a quantum carried item is a member of its channel, not a customer of it.**

### Energy

```json
// materials/quantum_pack.json
{
  "id": "quantum_pack",
  "hardness": 1.0,
  "supportDistance": 2,
  "permeability": 0.0,
  "density": 3000,
  "textureLayer": 16,
  "unit": "count",
  "placeable": false,
  "machine": {
    "kind": "storage",
    "carried": true,
    "capacityJoules": 500000000,
    "links": {
      "charge": { "carries": "power", "role": "sink", "via": "channel", "port": "channel" }
    },
    "extras": { "window": {}, "priority": { "default": 15 } }
  }
}
```

It passes `storage`'s built validation: one power sink link, capacity above 0, carried. It declares no
`rechargeWatts`, because a member is written back, not recharged.

**How the hand spends from the bank.**
- Between boundaries the tool pays from the pack's charge, as `EquippedPack.Pay` does today.
- At each boundary, `BeginPower` pools the pack's remainder, and `WriteBack` refills members in priority
  order. The pack defaults to 15, so **the hand is refilled from the bank first, with no watt limit.**
- 500 MJ covers about 4.3 h at the miner's nominal 32 kW (arithmetic on Reader D's figure, estimate), and
  the pack is topped up every boundary. So within half a second, every joule the hand spends is a placed
  cell's joule.

**Bankruptcy.**
- There is no floor and no refusal to spend. Priority can move the pack below the holds, but that is a
  setting, not a guard.
- The warnings are a HUD line (*bank 12% — holds 4 min at this draw*, estimated each boundary) and
  `powerlow`.
- `PowerExact` moves the pack into `stored`. Reader C's two print gaps are fixed in the same slice: print
  `packInitial`, and split `entering` from Alt+P's debug fill.

### Items

**The store is Σ vault members on a channel**: the placed vaults plus the equipped backpack. That is ladder
d9's pool pattern, with one deliberate difference: **contents live in rows, not in a redistributed sum**,
because the dump, recall and unload all need a litre or voxel to have a *place*.
- Deposits fill in priority order, then origin order.
- Withdrawals take from the backpack first, then from the lowest-priority vault.

```json
// materials/quantum_backpack.json
{
  "id": "quantum_backpack",
  "hardness": 1.0,
  "supportDistance": 2,
  "permeability": 0.0,
  "density": 1500,
  "textureLayer": 17,
  "unit": "count",
  "placeable": false,
  "machine": {
    "kind": "vault",
    "carried": true,
    "capacityLitres": 64000,
    "hold": { "watts": 20, "wattsPerMegalitre": 1000 },
    "links": {
      "stock": { "carries": "items", "role": "sink", "via": "channel", "port": "channel" },
      "power": { "carries": "power", "role": "sink", "via": "channel", "port": "channel" }
    },
    "extras": { "window": {} }
  }
}
```

**Two items rather than one**, because he named two pairings (*mobile battery ↔ quantum storage*;
*backpacks ↔ containers*), and each kind then keeps one quantity. `storage` goes on meaning joules (Reader
B trap 2). The other arm, one combined item, is his word.

**The pocket under it.** `VolumetricInventory` is unchanged: unbounded, weightless, voxel counts. It gains
one refusal: fluid IDs. The crossovers:
- **Placement** debits the pocket, then falls through to the store.
- **The stock UI** moves stacks between pocket and store.
- **The quarry** credits the store.
- **The siphon** moves water between the world and the store.
- **Mining** credits the pocket, because a dig should not start a hold bill the player did not choose.

**Why a container has a reason while the pocket is unbounded:**
1. The store is **the only home for litres** (taken 8).
2. It is **where machines deliver while you are away**.
3. It is **what the mine can take back.**

Bounding the pocket is survival's trigger, not the demo's.

---

## 5. The container ladder and the quantum vault

**Below quantum.** Hand-to-AC containers are `vault`s with no links and no `hold`, `capacityLitres` ≤
1,024,000, and no store membership.
- Their contents are **voxels only**. The loader refuses a fluid-capable vault that is not a `channel`
  member, which confines taken 8 to quantum *structurally*.
- They ship with M10g's screen and a survival pocket bound, not in this pass. Until then they have no
  reason to exist (constraint 15).
- Shape: `{ "id": "iron_bin", ..., "machine": { "kind": "vault", "capacityLitres": 64000 } }`.

**The quantum vault** is one brush cell, 64 voxels, 1 m (*"about the same size as the current machine
blocks"*):

```json
// materials/quantum_vault.json
{
  "id": "quantum_vault",
  "hardness": 3.0,
  "supportDistance": 8,
  "permeability": 0.0,
  "density": 7800,
  "textureLayer": 18,
  "unit": "cells",
  "machine": {
    "kind": "vault",
    "capacityLitres": 100000000,
    "hold": { "watts": 50, "wattsPerMegalitre": 1000 },
    "slots": {
      "entangler": { "family": "entangler", "scales": "capacityLitres" },
      "core":      { "family": "cell",      "scales": "hold" }
    },
    "links": {
      "stock": { "carries": "items", "role": "sink", "via": "channel", "port": "channel" },
      "power": { "carries": "power", "role": "sink", "via": "channel", "port": "channel" },
      "water": { "carries": "fluid", "role": "sink", "via": "channel", "port": "channel" }
    },
    "extras": {
      "respond": {}, "priority": { "default": 12 }, "recall": {}, "remote": {}, "telemetry": {}
    }
  }
}
```

**Capacity and the hold.** The numbers are the god grade; the installed parts scale them down. All
**estimates**:
- **God grade: 100 ML.** The engine bounds a vault there, since a full dump is 6.4 M cells, about ten times
  the load test (M10-NOTES' arithmetic).
- **With a high entangler: 10 ML**, his figure.
- **The hold is 50 W idle plus 1 kW per ML held, with a god core.**
  - 10 ML held costs 10 kW, about 20 of today's 500 W solar panels. A 5 GJ vacuum cell carries that about
    5.8 days with no sun.
  - A full god vault, 100 kW, runs the same cell dry in about 14 hours.
  - A low core multiplies the bill by ten.
- **The bill follows what is held, not capacity.** It is charged each boundary, in priority order, as the
  new `PowerExact` term `hold`.

**What it holds.** Voxel counts per material, about 12 bytes a material (Reader C's estimate), and one
int64 of millilitres under a new water ID:

```json
// materials/water.json — for the store and the hotbar; never a voxel in the world
{ "id": "water", "hardness": 0, "supportDistance": 0, "permeability": 1.0, "density": 1000,
  "textureLayer": 19, "opaque": false, "fluid": true, "placeable": false, "unit": "litres" }
```

Tier B still holds anonymous millilitres; nothing in the fluid engine reads this file. It is one permanent
ID, and the same ID MATERIALS-NOTES option B would need anyway, when each fluid gets its own grid.

**The amendment, stated out loud.** Litres in a row amend ladder d6 and d22 (the bucket), d13 (the tank on
the row), and M10-PLAN D5 and D10, **for the quantum tier only**, on his word (taken 8). Those rejections
feared a litre that was nothing's truth. Here the row's litres are a named home with its own identity, not
fluid in flight.

**The doors taken 8 asks for.**

1. **The machine that adds: the vault's `water` sink link.** A vault joins its channel's outlet ring as a
   sink that *banks* litres instead of emitting them.
   - It is the first link the tick *reads* (at `Rebuild`). Links are decoration today, so "a declared link
     is read or refused" becomes the rule.
   - `pumped == emitted` becomes `pumped == emitted + banked`. A flooding mine can be pumped into the bank.
2. **The tool that adds and removes: the siphon.**

```json
// tools/quantum_siphon.json
{ "id": "quantum_siphon", "bite": "cube", "cooldownSeconds": 0.25,
  "takes": "overburden:water",
  "power": { "via": "channel", "joulesPerLitre": 1000 } }
```

- **Left-click** takes up to a cell's water (1,000 L) into the store. **Right-click** pours from the store,
  at range, as the fill does.
- **With no backpack equipped it does nothing** (*"no store"*), so there is never water in the pocket.
- It pays per litre moved, from the pack and so from the bank. The 1,000 J/L is the relay pump's figure
  (estimate): a 10 ML siphoning costs 10 GJ.
- A vault `water` *source* link is refused in this build, naming its slice.

**The dump.** A vault dumps when a boundary cannot pay its hold. **Only an unpaid bill causes it; the
respond gate never does.**

**What:** everything it holds, not a fraction (*"suddenly it has to find 10,000,000 litres of water to
dump"*).

**Where the litres go**, from rules that already exist:

| Where the vault is | The litres go |
|---|---|
| Mouth in a lake | Tier A |
| On the analytic ground inside the watershed window | Tier A, the basin under it (the unloaded outlet's rule) |
| Loaded and dry | a Tier B ball, poured through the relay's `PourPending` template, owing the rest across boundaries |
| Unloaded underground | **owed** in the row until arrival |

**The voxels** launch as collapse pieces from the cell above the vault. They land **as their own
materials, not rubble**: the piece already carries its materials for drawing, so a flag is enough.
Launching from above gives the landing solver the anchor Reader C noted a dump lacks. Unloaded, the voxels
are owed.

**How fast is the engine's.** A mod sets game numbers within bounds, and CPU bounds are never JSON. So one
pour cap and one piece cap per boundary are C# constants, printed. **Owed quantities are persisted in the
row**, unlike Ctrl+R's session-only pending pour, because they are the player's matter.

**When a vault stops existing** (dismantle, destroy, recall), its contents rebalance into the store's
other members, and whatever doesn't fit is dumped.
- The dump runs recording-off and clears the undo stack, as a fall does.
- The row is emptied **before** `Remove` journals its copy.

That closes Reader C's two duplication paths: an undo re-adding a full row, and an undone dump crediting
the heap.

**Identity terms**, printed from the first line:
- **Tier B:** twin counters, never folded into `Added`: `+ poured + dumped − siphoned`.
- **Tier A:** its own identity is never evaluated, so print each seam from both ends: `siphonedA`,
  `dumpedA`.
- **Machine layer:** `pumped == emitted + banked`.
- **The store's water, new:** `held + owed == banked + siphoned − poured − dumped + entering − leaving`.
- **The store's voxels.** This identity is new, built rather than "gaining a term": `held + owed ==
  deposited − withdrawn + quarried − dumped + entering − leaving`. Its `dumped` is printed beside the
  director's `lost`.
- **`PowerExact`:** gains `hold` and `dig`; the siphon's draw sits inside `packTaken`.

**Persistence.**
- **Sectioned `KindState`** (tag, length, value): `stock`, `water`, `parts`, `owed`, and the quarry's
  `cursor`. Each codec touches only its own section, which answers Reader A trap 5 (a writer that
  replaces the whole array). v1 states read as one legacy section.
- **No schema bump.** The kind state's version byte absorbs it.
- **The backpack's equip slot goes in header v8**, with the mode byte (section 9). M10g's hotbar moves to
  v9.
- **The single transaction is built** (`WorldBatch`), so a pocket ↔ store transfer is two in-memory writes
  in one batch.

**The stock UI.** The machine panel gains a stock section for vaults: materials with labels and amounts, a
capacity bar, the hold in watts, the owed dump, and "to pocket / from pocket" amounts. It also opens from
`remote`'s list and from the backpack's panel. It is one section, not a screen. It is M10b's deferred stock
tab, at last with something to show.

---

## 6. Grades

**A grade lives in a part's ID.**

| Home | IDs this pass | Cost |
|---|---|---|
| Machine ID (`quantum_vault/mythical`) | 25 per two-slot machine, 50 in all | His mix (*"battery tech 19th century … emitter a higher tech variation"*) needs every combination as a permanent ID. |
| Per-stack data (the `CarriedPacks` shape) | 1 per family | A header bump; stacks split in a count-keyed inventory; a grade table inside the part's JSON; a third-party grade would need a number-patch, which is non-commutative. |
| **Part ID, held in the row's slots** (recommended) | **10** | 10 IDs, an install verb, and a `parts` section in the row. |

**A grade is a spelling, not a type.** There is no grade enum and no grade table. One file expands, in
document order, into N IDs:

```json
// materials/entangler.json → overburden:entangler/low … overburden:entangler/god
{
  "id": "entangler",
  "hardness": 1.0,
  "supportDistance": 2,
  "permeability": 0.0,
  "density": 5000,
  "textureLayer": 20,
  "unit": "count",
  "placeable": false,
  "part": { "family": "entangler" },
  "grades": {
    "low": { "factor": 0.001 }, "medium": { "factor": 0.01 }, "high": { "factor": 0.1 },
    "mythical": { "factor": 0.5 }, "god": { "factor": 1.0 }
  }
}
```

```json
// materials/vacuum_core.json → overburden:vacuum_core/low … /god, in family "cell"
{
  "id": "vacuum_core", "hardness": 1.0, "supportDistance": 2, "permeability": 0.0, "density": 4000,
  "textureLayer": 21, "unit": "count", "placeable": false,
  "part": { "family": "cell" },
  "grades": {
    "low": { "factor": 0.1 }, "medium": { "factor": 0.25 }, "high": { "factor": 0.5 },
    "mythical": { "factor": 0.8 }, "god": { "factor": 1.0 }
  }
}
```

**Factors.**
- A factor is in thousandths, 0.001..1.0, refused outside, and quantised once at load (ladder d10).
- **The kind's C# table says whether a scaled number is a capacity or a cost**, so a mod never writes the
  direction:
  - a capacity is multiplied (`capacityLitres`, `reachCells`, `voxelsPerSecond`);
  - a cost is divided, rounded up (`hold`, `joulesPerVoxel`).
- **An empty slot has factor 0.** A vault with no entangler holds nothing; with no core it cannot pay, and
  reads `no core`.
- **The god grade is the machine's own JSON; every other grade is less.** That is "design the ideal
  first" taken literally.
- Grades change only scalable numbers, never a kind, an extra or a medium.

**His 19th-century battery in a quantum vault** is a low-factor part of family `cell` in the `core` slot. A
future `lead_acid_cell` part is one file.

**Verbs.**
- **Install:** hold a part and right-click a machine with a matching slot. The old part, if any, returns to
  the pocket.
- **A swap that would leave a vault over capacity is refused**, never dumped, because a swap is not a door.
- **The computer's verb is keyed by `"programmer": true`**, not by "non-placeable and not a pack".
  Otherwise every part and the water ID would open machine panels (Reader E trap 5).
- **Dismantle returns the parts, a fall destroys them, recall returns them.** An install is not journalled,
  as the pack's equip is not.

**Cost in IDs:** 10 parts, plus the vault, backpack, pack, quarry, power sensor and water: **16 new
material IDs.**
- The support cost table grows 512 bytes per ID per wet derive: +8 KiB (Reader E's arithmetic, estimate).
- **The lever:** give non-placeable materials no row in the table. Trigger: the table past 32 KiB.
- Texture layers stay C#. One layer per family is enough, because the label carries the grade.

### A third-party mod, in JSON only

```json
// mods/coldstore/mod.json
{ "id": "coldstore", "name": "Cold Store", "version": "1.0.0", "format": 2,
  "dependencies": ["overburden"] }
```

```json
// mods/coldstore/materials/cold_vault.json
{
  "$comment": "A big dumb vault: no slots, a heavy idle bill, 20 ML fixed.",
  "id": "cold_vault",
  "hardness": 3.0,
  "supportDistance": 8,
  "permeability": 0.0,
  "density": 7800,
  "textureLayer": 18,
  "unit": "cells",
  "machine": {
    "kind": "vault",
    "capacityLitres": 20000000,
    "hold": { "watts": 2000, "wattsPerMegalitre": 400 },
    "links": {
      "stock": { "carries": "items", "role": "sink", "via": "channel", "port": "channel" },
      "power": { "carries": "power", "role": "sink", "via": "channel", "port": "channel" }
    },
    "extras": { "priority": { "default": 4 }, "telemetry": {} }
  }
}
```

```json
// mods/coldstore/materials/salvaged_entangler.json → coldstore:salvaged_entangler/cracked
{ "id": "salvaged_entangler", "hardness": 1.0, "supportDistance": 2, "density": 5000, "textureLayer": 20,
  "unit": "count", "placeable": false,
  "part": { "family": "overburden:entangler" },
  "grades": { "cracked": { "factor": 0.05 } } }
```

```json
// mods/coldstore/patches/relay_pump.json
{ "target": "overburden:relay_pump", "addExtras": { "recall": {} } }
```

```json
// mods/coldstore/palette/coldstore.json
{ "sections": { "machines": ["cold_vault"], "parts": ["salvaged_entangler/cracked"] } }
```

**With JSON only, it adds:**
- a new vault;
- a part in a base family, which the base vault's slot then accepts;
- an extra on a machine it does not own;
- palette entries.

**It cannot add:**
- **a look of its own.** It reuses layers 18 and 20 until textures load from mods (M10i).
- **a kind or an extra**, which are C# (2.1).

It loads identically in any mod order.

---

## 7. The automated quarry

```json
// materials/quantum_quarry.json
{
  "id": "quantum_quarry",
  "hardness": 3.0,
  "supportDistance": 8,
  "permeability": 0.0,
  "density": 7800,
  "textureLayer": 22,
  "unit": "cells",
  "machine": {
    "kind": "quarry",
    "reachCells": 32,
    "voxelsPerSecond": 128,
    "hardnessThreshold": 5.0,
    "joulesPerVoxel": 50,
    "slots": {
      "entangler": { "family": "entangler", "scales": "reachCells" },
      "core":      { "family": "cell",      "scales": "joulesPerVoxel" }
    },
    "links": {
      "power": { "carries": "power", "role": "sink",   "via": "channel", "port": "channel" },
      "spoil": { "carries": "items", "role": "source", "via": "channel", "port": "channel" }
    },
    "extras": {
      "respond": { "default": 1 }, "priority": { "default": 4 },
      "recall": {}, "remote": {}, "telemetry": {}
    }
  }
}
```

**What it digs.**
- A box of `width` × `length` × `depth` 1 m cells. The ports are materialised by the kind, with a static
  maximum at the god grade, clamped at use to the installed entangler.
- The box sits one cell out from the body, on the side its `Facing` names. That makes the quarry **the
  first reader of `Facing`**.
- Top layer first, row by row, **one whole cell (64 voxels) a step**, with its cursor in the row. It never
  cuts its own body or the column under it.

**Bounds.**
- Refused at load: `reachCells` ≤ 32, `voxelsPerSecond` ≤ 512, `joulesPerVoxel` ≤
  `ToolPower.MaxJoulesPerVoxel`.
- A god box is 32³ = 32,768 cells, 2.1 M voxels, 32.8 ML of spoil (arithmetic), which fits a god vault
  three times.

**Energy.** 50 J a voxel at god grade, the quantum tools' figure; 500 with a low core. At 128 voxels a
second that is 6.4 kW (estimate), the `PowerExact` term `dig`.

**Spoil** goes only to its channel's store, credited as itself (no drop tables). A quarry that cannot
deposit a cell does not dig it: no heap, nothing destroyed.

**What stops it**, each a stall on the panel and the `M10:` list:

| Stall | Why |
|---|---|
| `storefull` | the store has no room |
| `unpowered` | the pool cannot pay a whole cell |
| `signalled` | `respond` defaults to 1, so **a collapse sensor on its channel stops it** |
| `unloaded` | a grid write stalls unloaded (ladder d4) |
| `done` | the box is finished |
| `nocutter` | an empty slot |

Voxels harder than its threshold stay standing, as pillars.

**Water and the collapse.** The box is a void the existing rules fight: seepage finds it, rain fills it,
a dump can too. **But Tier B water in the pit does not weaken rock until it becomes a Tier A level**
(Reader E trap 9). A fall onto the quarry destroys it unless it recalls. Rubble falling into the box is dug
and credited as rubble.

**Undo.** Its writes are the world's, like a collapse's. It runs recording-off, and each chunk it writes
calls the existing `Forget(chunk)`, so no undo step restores a dug voxel and credits the pocket for it. The
player's steps elsewhere survive.

**CPU per boundary**, all estimates, none measured:
- 64 `SetVoxel` a cell step, at TNT-NOTES' 1.5 µs a voxel (a Debug figure): about 0.1 ms per quarry.
- Remesh and relight of each touched chunk are unpriced, and likely larger.
- Ten quarries cross the `machines` > 1 ms trigger.
- It also fires M10-PLAN D3's second-rate trigger (a drill that stutters at 2 Hz), which is his to judge in
  play.

---

## 8. Materials

**What the machines are built from: nothing yet.** Recipes are M10g (`station` is refused). The format
reserves `recipes/*.json` as ladder d7 has it. **No bill of materials ships in this pass**: a bill with
no crafting behind it is JSON nothing reads, which is exactly what the new strictness exists to refuse.

**What the quarry yields** is whatever its box holds: grass, dirt, stone, clay, glass, rubble. It is
material-agnostic by construction, with a flat cost per voxel. Hardness is a gate, not a price (Reader E
trap 7), and I recommend not giving it a second meaning.

**Where I cut: ores and smelting are M10h's own pass.**
- An ore field that is on by default moves r6's quads and jobs, the keystone's 18,718, and every M8c and M9
  figure where the rock's support or permeability differs. It also seams against old worlds' stored
  chunks.
- If one ships, it follows the relief precedent: a header setting, off in the record, on only for newly
  created played worlds.
- A quarry is worth watching without ore.

**The 16 new IDs are not placed by worldgen, so r6 holds** (the clay-and-glass precedent). Three
hard-counting tests need their counts raised: `ShippedContentTests.cs:209` and `:242`, and
`CollapseTests.cs:1044`.

---

## 9. The creative-mode demo

**The mode, in the save and in code.**
- **The save:** a header byte, v8. 0 is `open`, which is every existing world and every measurement. 1 is
  `creative`.
- **Set at creation only**, by `--mode=creative`; warned and ignored on an existing world, as `--seed` is.
- `--profile`, `--nosave` and the r6 control never create one.
- **In code:** a read-only `WorldMode` consulted by Game-side input. **Core never reads it.** No rule of
  water, power or support differs by mode.

**What is unlocked is content.** The palette is `palette/*.json`, a union across mods:

```json
// mods/overburden/palette/overburden.json
{ "sections": {
    "machines": ["quantum_vault", "quantum_quarry", "relay_pump", "relay_outlet", "solar_panel",
                 "vacuum_cell", "collapse_sensor", "power_sensor"],
    "carried":  ["quantum_pack", "quantum_backpack", "computer"],
    "parts":    ["entangler/low", "entangler/medium", "entangler/high", "entangler/mythical", "entangler/god",
                 "vacuum_core/low", "vacuum_core/medium", "vacuum_core/high", "vacuum_core/mythical", "vacuum_core/god"],
    "blocks":   ["stone", "dirt", "clay", "glass", "lamp"]
} }
```

- Rubble and water are deliberately absent. `--stock=` today hands out rubble and the lamp, and after this
  pass it must skip fluid IDs.
- **A palette draw is a real count with a printed `palette` term on `M6c:`**, which keeps the undo's
  `OutOfStock` rule sound (Reader D trap 8).
- The palette is M10g's inventory screen in its first form, on **E tapped** (Matthew's key, already
  assigned to that screen). In `open` worlds, `P` stays.
- **Power, water and the roof stay real.** `--nogate` zeroes generation (Reader D trap 2), and nothing
  replaces it. Creative gives you the machines, not the joules.

**The first five minutes**, on a new creative world with the shipped relief:

1. **0:00.** You spawn walking. A HUD hint says E opens the palette.
2. **0:20. The bank.** Take a vacuum cell, eight solar panels, the pack and the backpack, and equip both on
   channel 0. Place a vault and right-click a high entangler and a mythical core into it. The panel reads
   *capacity 10 ML, hold 63 W idle* (50 W ÷ 0.8).
3. **1:30. The quarry.** Place a quarry facing a hill with a medium entangler, and set an 8³ box on the
   panel. A cell goes every half second, the vault's stock rises, and F12's pins show green.
4. **2:30. Water into the store.** Siphon a pond, 1,000 L a swing, then pour some back at range. `M8b:`
   reads `siphoned` and `poured`, exact.
5. **3:30. The mine answers.** The pit undercuts a slope. A collapse sensor on channel 0 arms, the quarry
   stalls `signalled`, and the roof comes down in pieces to the phonk. Recall returns the quarry.
6. **4:30. Bankruptcy.** Clouds come in and you keep swinging the miner. The bank line falls, `powerlow`
   trips, and the bank goes dry. The vault dumps: the water pours out and your stone lands around it in its
   own colours. *"Swim or mine to get it back."*

**What it must not break.**
- **Survival:** it does not exist yet; the mode byte leaves room for it.
- **The ladder:** the palette lists only quantum content, because that is all there is.
- **r6 and the oracles:** creative is never the default. r6 is read to the digit in every slice.
- **Persistence:** v8 is additive; a v7 world opens `open`.
- **The undo:** palette draws are real counts.
- **Fly** stays allowed in every mode: it is the only escape from being buried until M11 (Reader D trap 4).

---

## 10. The split

Each slice is a session or less; Core and tests are cut from the wire.

| Slice | What | Verified by |
|---|---|---|
| **x-0** Core | The strict loader: extension bags, `$` keys, did-you-mean, `format`, the owned-port registry. | Every shipped file and fixture loads. A mutation battery, one misspelt key per DTO level, each refused naming file and key. |
| **x-1** Core | Media files, reach, `extras` and `MachineExtra`, patches, namespaced vias, the passes. `onsignal` moves under `respond` (after f-1). | The six-order fixture. Each refusal in 2.7. Rows saved under f-1 restore unchanged. |
| **x-2a** Core | `priority`, `powerlow`, the vault and quarry as readers (after v-1 and q-1). | A short channel is ordered by priority. `PowerExact` is exact through a signal. A signalled vault holds. |
| **x-2b** Wire | `remote`'s list, `telemetry` pins, the bank HUD line. | A headless remote pick; the map's bearing oracle with state; r6. |
| **v-1** Core | `water`, the pocket's fluid refusal, `vault`, sectioned `KindState`, the store view, the hold, `overfull`, both store identities, `banked`. | Identities exact over a thousand random transfers. An over-capacity restore keeps its contents. |
| **v-2** Core | The dump: Tier A/B counters, persisted owed quantities, release at arrival, own-material pieces, the exit rule, recording-off. | Each destination counted. An undo after a dump credits nothing. |
| **v-3** Core | `window`: the pack and the backpack as members; the placement fall-through. | `PowerExact` with the pack as a member. A bank drained to zero. A mutation adding a floor fails. |
| **v-4** Wire | The stock section, the siphon, header v8, the printed terms. | **Oracle:** a scratch world, siphon a take and a pour, force a dump with the sun off. `M8b:` and `M10:` exact. r6. |
| **g-1** Core, **g-2** Wire | Parts, the `grades` expansion, slots, factors, the `parts` section, install and swap, `programmer`; then the verb and labels. | Stable expansion order. An empty slot reads factor 0. An over-capacity swap is refused. Parts return on dismantle. |
| **q-1** Core, **q-2** Wire | The `quarry`: box, `Facing`, cursor, stalls, `dig`, `Forget`; then Main's wiring and the panel. | An 8³ box dug exactly; `storefull` digs nothing; the voxel identity is exact. **Oracle:** `boundary worst` against the 1 ms trigger. |
| **r-1** Core | `recall` in both collapse modes. | `released == landed + in flight + lost + recalled`, with no double credit. |
| **c-1** Core, **c-2** Wire | The palette and its term, the mode byte, E's palette tab. | A creative world opens; a v7 world opens `open`; r6. |
| **d** | His play of the five minutes. | His sentence. |

**The riskiest slice is v-2**: four destinations, owed quantities across unload, and the undo, all at once.

---

## 11. Numbered decisions

1. **Kinds stay closed C#. A kind is a home or a world-writer; everything else is an extra. Amend M10-PLAN
   D1.**
   - *Cost:* two new kinds touching the switch sites.
   - *Revisit:* M12's pass, if scripts need a home.
2. **Six extras, closed, declared in an `extras` object, stored as a per-material bit.**
   - *Cost:* a C# site per extra, and mods cannot invent one.
   - *Revisit:* a seventh extra asked for twice.
3. **Inheritance is reach per medium (`media/*.json`): effective = declared ∩ reach, an over-reach is
   refused, no `extends`.**
   - *Cost:* inheritance is mostly paper until M10e.
   - *Revisit:* three or more near-duplicate machine files after grades.
4. **Media are one-owner, namespaced content; conductor numbers go on the medium; reserved names need
   `engine: true`.**
   - *Cost:* mods must declare `overburden` as a dependency, and the ladder's examples are rewritten.
   - *Revisit:* none. This is free now and expensive after M10e.
5. **Unknown keys are refused, with `$` keys allowed, a did-you-mean, and `mod.json` `format`.**
   - *Cost:* version skew fails loudly, and a typo placeholders a world's placed material.
   - *Revisit:* the first third-party mod whose users hit skew. The lever is then a per-file "load anyway".
6. **Engine-owned ports are materialised; a hand-written one must match; an unread port is refused.**
   - *Cost:* fixtures with free port names (`"a"`) need an owner, or the rule stays loader-only.
   - *Revisit:* M12.
7. **Patches may only add extras (to materials or to reach), applied as a union after all content.**
   - *Cost:* a mod can change base gameplay.
   - *Revisit:* a request to patch a number. The answer is a new material.
8. **Water gets an ID used only by the store; the pocket refuses fluids; litres are mL in the row.** This
   amends ladder d6, d13 and d22 and M10-PLAN D5 and D10, for quantum, per taken 8.
   - *Cost:* one ID and five amended rejections.
   - *Revisit:* a bucket requested below quantum.
9. **`vault` is a kind. Capacity is in litres: ≤ 100 ML with a hold, ≤ 1,024,000 L without. Contents are
   never clamped.**
   - *Cost:* a god dump is ten times the load test.
   - *Revisit:* a measured dump over budget. Lower the engine bound.
10. **The store is Σ vault members; contents live in rows, filled by priority, then origin.**
    - *Cost:* a withdrawal order to learn.
    - *Revisit:* a play wanting one heap.
11. **`carries: "items"`; a future conveyor is a media file carrying items**, which reconciles ladder d22.
    - *Cost:* a validator value.
    - *Revisit:* the conveyor pass.
12. **Two carried items, both with `window`; the pack is written back first by default.**
    - *Cost:* a header slot.
    - *Revisit:* his word for one item.
13. **Grades are a `/grade` suffix on part IDs, expanded in document order; slots scale the god-grade
    numbers; machine IDs are never graded.**
    - *Cost:* 10 IDs, a verb, a row section.
    - *Revisit:* grades reading in play as "just a bigger vault".
14. **The computer's verb is keyed by `"programmer": true`.**
    - *Cost:* one key.
    - *Revisit:* none.
15. **`quarry` is a kind: a facing box, cell steps, spoil to the store only, `respond` default 1,
    recording-off with `Forget`.**
    - *Cost:* the first voxel-writing machine, with remesh unpriced.
    - *Revisit:* `machines` > 1 ms, or D3's stutter trigger.
16. **The siphon: `takes: "overburden:water"`, paid per litre, store only.**
    - *Cost:* two new Tier B doors.
    - *Revisit:* a play wanting water without a backpack.
17. **`respond` on a vault gates transfers, never the hold; `powerlow` is added.**
    - *Cost:* a sensor cannot flood.
    - *Revisit:* his *"cut power to the water containers"* asked again, which reopens collapse f decision
      10.
18. **Dump rates and caps are engine constants.**
    - *Cost:* no gentle modded vault.
    - *Revisit:* a mod asking; then add an engine-bounded key.
19. **A `/` segment renders in parentheses; palette sections are identifier keys.**
    - *Cost:* none.
    - *Revisit:* M6d-3's language trigger.
20. **Creative is a v8 mode byte set at creation; the palette is content with a printed draw term; power,
    water and the roof stay real.**
    - *Cost:* v8 is contended, so M10g's hotbar moves to v9.
    - *Revisit:* a demo play where the power economy reads as a wall.

---

## 12. Must not ship

1. **A key the loader reads and the tick ignores.** Every new key is read at a named site or refused.
2. **Extras resolved per row.** The director must read recall from `MaterialTable`.
3. **A patch that sets a number**, or patches applied in load order rather than as a union.
4. **Grade expansion over a `Dictionary`.** It assigns permanent IDs in an order it does not promise.
5. **Grades in machine IDs** "just for the demo". IDs are forever.
6. **A clamp on contents at restore**, or a kind helper reading an unknown section as empty.
7. **Water in the pocket by any path**, `--stock=` included.
8. **A dump through `Add` or `Emit`.** Twin counters only; either misfiles it.
9. **An `ac`, `dc` or `shaft` media file before M10e.** The lamp that did not glow.
10. **Renaming a materialised port.** Rows persist port names, so a rename silently drops every saved
    value.
11. **The vault as `storage`, or a window pack outside `PowerExact`.** Alt+P's bare `Give` once cost 1,403
    error lines.
12. **A creative default reaching `--nosave` or `--profile`.**
13. **A bare `via` special-cased to `overburden`.** That is a privileged path; it goes through
    `dependencies`.

---

## 13. This design's three weakest points

1. **Slots and parts are heavy for a demo.** A cross-file pass, a row section, an install verb, a swap rule
   and 10 IDs, all to show "a better entangler". If grades play as "a bigger vault", machine-level IDs would
   have been simpler. This design bets that his *"19th-century battery, higher-tech emitter"* mix is the
   point.
2. **Inheritance is mostly paper.** Only `channel` exists, so the demo exercises only the top tier's reach,
   where everything is inherited. The lower media files cannot be tested until M10e and M10f build
   membership. The brief's "membership is already computed" holds for quantum only.
3. **Strictness trades graceful degradation for loud failure.** A refused material in an existing world
   becomes a stone-like placeholder wherever it was placed, and a mod for a newer build loads nothing rather
   than most things. For a solo project with no third-party mods, that is the right trade today. It is the
   decision most likely to be revisited the week someone else ships a mod.
