# Designer 2: gameplay and the demo

*The ideal machines, designed from the loop outward, for Matthew and for the person he hands the demo
to. "(estimate)" marks a figure of mine that nobody has measured; a measured figure names its source.
The whole design is a hypothesis for the judges.*

---

## 1. The idea in five sentences

1. **At the top tier your whole operation runs off one number and keeps everything it owns in one
   place.** The channel's bank pays for every bite, every litre moved and the vault's rent. The vault
   holds every voxel and every litre you own. The quantum backpack is a window onto both.
2. **Bankruptcy is one event, and you can see it coming.** The HUD's runway turns amber, then red.
   Then the machines stall, the tool in your hand fails with *"insufficient charge"*, and the vault's
   containment glitches cyan for 10–20 s. Then it lets go: your matter lands around it as a heap in
   its own colours, and your water goes into the lake beside it.
3. **The vault turns the mine from an antagonist into a creditor.** Every litre you fight out of the
   pit costs the bank, and every litre you bank instead of pouring away is a flood you have
   postponed.
4. **The quarry is greed that runs itself.** It deepens the pit the water fills, and it spends the
   bank the pumps need. Priority, the operations board and the bank sensor let the player choose
   which disaster to have. None of them stops the player going broke.
5. **The creative demo is a template world Matthew builds by playing it.** In it, machines and
   building blocks are free, but ore, energy and water never are. Its first five minutes run from
   gold under a lake to the lake in your pit.

---

## 2. The extras rule

**For the player, an extra is a promise a tier makes about its machines**: to recall them when the
roof comes down, report to the operations board, take a priority, show their state on the map, and
obey a sensor. You learn the promise once per tier, not once per machine. That is why the extras
belong to tiers, and why his *"extras that the previous tier may or may not inherit"* is the right
shape: a lower tier makes a smaller promise.

**Engine side.** `MachineKind` stays a closed C# enum.

- **Gameplay needs three more kinds**: `sensor` (f-1 builds it), `vault` and `quarry`. That goes
  against M10-PLAN D1 (*"a new kind is M12's scripting"*), which part f has already bent once.
- **The amendment, stated as one:** until M12, the base game adds kinds in C#, each declared in JSON
  by name the way a mod would declare it. Each carries a named-exception line in DESIGN.md and a
  test that JSON is the only way to get one. A kind is a verb, and mods get new verbs at M12.

An extra is **not** a kind. It is one flag in an `Extras` bitset on `MachineDefinition`, resolved at
load from the machine's tier file minus the machine's opt-outs. Each flag is read at exactly one
fixed site:

| Extra | The one site that reads it |
|---|---|
| sense (reader) | the tick's override pass (f-1's `onsignal`) |
| recall | the collapse director's release (3.2); never the drain |
| remote | the operations board's row list |
| charge | the bound backpack's membership of the pool |
| priority | `MachineTick.Rebuild`'s consumer sort |
| telemetry | `Main.MapOverlayNow`'s pin colour |

Each site is an `if (def.Has(Extra.X))`. There is no class hierarchy and no work in a hook. Every
site already iterates by channel, so an extra reaches as far as the tier's membership reaches, which
is the ladder's one idea.

**The JSON.** A tier is a file, `mods/overburden/tiers/<id>.json`. A lower tier inherits a named
subset of a higher tier's extras:

```json
{
  "id": "quantum",
  "membership": "channel",
  "extras": {
    "sense": {}, "recall": { "whileSolvent": true }, "remote": {},
    "charge": { "onePool": true }, "priority": { "default": 5 }, "telemetry": {}
  }
}
```

```json
{ "id": "ac", "membership": "cable",
  "inherits": { "from": "quantum", "extras": ["sense", "remote", "priority"] } }
```

A machine names its tier, and it can opt out of any of the tier's extras by name:

```json
{
  "id": "quantum_vault_high_grade",
  "hardness": 3.0,
  "supportDistance": 8,
  "permeability": 0.0,
  "density": 7800,
  "textureLayer": 13,
  "unit": "cells",
  "machine": {
    "kind": "vault",
    "tier": "quantum",
    "grade": "high",
    "holdsCubicMetres": 10000,
    "holdWatts": 4000,
    "holds": ["voxels", "litres"],
    "links": {
      "power": { "carries": "power", "role": "sink", "via": "channel", "port": "channel" },
      "store": { "carries": "items", "role": "sink", "via": "channel", "port": "channel" }
    },
    "ports": {
      "channel": { "default": 0, "min": 0, "max": 15 },
      "enabled": { "default": 1, "min": 0, "max": 1 }
    },
    "extras": { "recall": false, "priority": false }
  }
}
```

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
    "tier": "quantum",
    "bite": "tunnel",
    "joulesPerVoxel": 50,
    "hardnessThreshold": 5.0,
    "links": {
      "power": { "carries": "power", "role": "sink",   "via": "channel", "port": "channel" },
      "spoil": { "carries": "items", "role": "source", "via": "channel", "port": "channel" }
    },
    "ports": {
      "channel":  { "default": 0,  "min": 0, "max": 15 },
      "enabled":  { "default": 1,  "min": 0, "max": 1 },
      "width":    { "default": 4,  "min": 2, "max": 16 },
      "length":   { "default": 4,  "min": 2, "max": 16 },
      "depth":    { "default": 16, "min": 2, "max": 64 },
      "rate":     { "default": 1,  "min": 1, "max": 4 },
      "priority": { "default": 7,  "min": 1, "max": 9 },
      "onsignal": { "default": 1,  "min": 0, "max": 2 }
    }
  }
}
```

The siphon (3.7) is a tool:

```json
{ "id": "quantum_siphon", "bite": "tunnel", "cooldownSeconds": 0.25, "takes": "water",
  "power": { "via": "channel", "joulesPerLitre": 1000, "pourJoulesPerLitre": 0 } }
```

**The loader refuses, naming the file:**

- an unknown extra;
- an inherited extra the parent tier does not declare;
- a tier whose `membership` is not served (only `channel` is, so **only `quantum.json` ships** until
  M10e/f);
- an extra that cannot apply to the kind (`priority` on a vault);
- an unknown tier;
- **any unknown key inside `machine`, `extras` or a tier file.** The old blocks' silent ignore
  (Reader A, trap 4) is fatal here: a misspelt `"recal": false` would load with recall on.

---

## 3. The six extras, and the door taken 8 adds

The demo's question comes first: which of these earns a place in the first five minutes?

| # | Extra | First five minutes? | In the demo build? | Why |
|---|---|---|---|---|
| 3 | Remote panel (the operations board) | **yes**, beat 4 | yes | It is how you answer a crisis from the pit floor |
| 5 | Priority | as a column on the board | yes | Cheap, and it says the fight is triage |
| 4 | Remote charge | invisible; it *is* the one pool | yes | Nothing separate to show |
| 1 | Sense and respond | no | yes (f-1 and f-2, plus the bank sensor) | The lesson the demo teaches: *next time, build one* |
| 6 | Map telemetry | no | yes | Cheap; it reads the second ten minutes, once the operation spreads |
| 2 | Recall | no | second wave | The most delightful extra and the most expensive |
| — | The siphon (taken 8) | **yes**, beat 3 | yes | The one verb a newcomer understands at once: point at the flood, pull |

### 3.1 Sense and respond (extra 1)

**At the top tier.** f-1's collapse sensor, as built, plus one more value for `senses`: **`bank`**.
A bank sensor signals its channel while the bank is below a `belowPercent` port (0–100, default 20).
It raises f-1's per-channel boolean, so readers do not change.

**The loop it closes.** You went broke once. Now you place a bank sensor with the quarry's
`onsignal` at 1, and the quarry stops itself at 20%. That is the only protection the design
allows: protection the player built.

A water-level sensor waits for a play that wants one, because a dry pump already spends nothing.

**Inheritance**, as the brief proposed: AC on its cable component, DC within its run, steam a bell
that switches nothing, hand none. **All of it waits for M10e/f.**

**Cost, persistence, terms.** Each collapse sensor does (2r+1)³ × 2 O(1) lookups per boundary
(f's own estimate), and a bank sensor does one comparison. The row and its ports persist; the signal
never does. f-1's `signal:` term, and no conserved quantity.

**Part f's decision 10, under one pool.** A signal that switched off the cells paying a vault's rent
would start its fuse: his rejected *"cut power to the water containers … deliberately flood the
place"* by another road. **So a vault's rent is paid from `enabled` cells before signal overrides
apply**: a signal can starve a pump, never dump a vault. `enabled` 0 set on the vault **by hand** is a
deliberate release, his *"i am deliberately going to flood that entire valley"* (5.2).

### 3.2 Recall (extra 2)

**At the top tier, while the channel is solvent.** When a collapse is about to take a machine's
body, the body glitches cyan and blinks out of the falling piece into the store, as 64 voxels, just
before the rock lands. It is priced at 50 J/voxel, 3.2 kJ a body (estimate). The price is trivial;
the point is the condition.

**If the bank cannot pay, or the store is full, nothing is recalled.** The body falls with the piece
and lands as black rubble: his *"indicitave that you aint getting that back"*, back in force exactly
when you are broke. This is the design's hinge. **Bankruptcy strips your protections one at a time,
and recall is the last to go.**

**Vaults opt out** (a vault in a collapse dumps, 5.2). **Inheritance: deliberately none below
quantum**; losing a pump to the roof is the fight for four tiers.

**It hooks at release (Reader C's option b), never at the drain**, where crediting 64 would mint
voxels already landed as rubble. At release the player also sees the machine leave before the roof
lands, which is the point.

**Cost, persistence, terms.** Nothing at rest; a few hundred index lookups per release (estimate).
Nothing new persists. The director's line becomes `released == landed + in flight + lost +
recalled`; the store gains `recalled in` and the pool `recall`.

### 3.3 Remote panel (extra 3): the operations board

**At the top tier.** A tab of the site office (9.2): one row per machine on the bound channel, with
its name, bearing and distance, its state in words (`PUMPING 100 L/s`, `STALLED — unpowered`,
`VAULT — FUSE 12 s`), and `enabled` and `priority` where declared, other ports a click away. The
bank, income, spend, runway and rent sit across the top. **It opens from anywhere**: E, or the
computer's right-click on nothing, the door that opens the pack's panel today (M10c d4).

**Inheritance:** AC, for the cable component of a `control_station` you stand at (M10e); below
that, you walk. **Cost:** zero per boundary (it reads rows while open, at the 0.1 s refresh). Ports
already persist; port writes are counted as today.

### 3.4 Remote charge (extra 4)

**At the top tier it is the one pool itself (4.1).** The brief's "exists" is wrong twice (Readers A
and B): no dock is built, and the lead-acid pack recharges from anywhere only because it rides the
channel class until M10i. **Below quantum**, a carried cell recharges only when docked (ladder d15,
M10f), which is the right sting: *"only once you have the latest tier battery will it be as
effective as it is now"*. Cost, persistence and terms: 4.1.

### 3.5 Priority (extra 5)

**At the top tier:** a `priority` port on every consumer (pumps and the quarry), 1 to 9, where 1 is
paid first. **At each boundary the channel pays in this order:**

1. the hand's spend since the last boundary, already made;
2. every vault's rent;
3. consumers, by priority and then by origin order.

**The defaults are pumps 5 and the quarry 7**, so the quarry starves before the pumps, which is what
a sensible site manager picks. It is not protection: nothing is held back and no spend is refused. It
only decides who is paid last in the final second before zero, and what keeps running when income is
small but positive.

**In play, priority chooses your disaster.** Put the quarry above the pumps and the pit floods while
it digs.

**Inheritance:** AC within a component (M10e); below that, origin order. **It replaces an order,
not a vacuum** (Reader A, trap 8): today's order is origin position (X, Y, Z), which the docs
misname placement order. Correct them in the same slice.

**Cost, persistence, terms.** The sort lives inside `Rebuild`, which runs only when the version
moves, so it costs zero per boundary. It is a port, reviving the pump port M10b removed because
nothing read it. It adds no term; it only reorders `drawn`.

### 3.6 Map telemetry (extra 6)

**At the top tier:** state on F12's existing pins: running in the material's colour, stalled amber,
unpowered red, a vault in its fuse blinking red, and one legend line. **Pins stay at every tier**;
only the state colour is quantum's, and nothing below inherits it yet. **Cost:** O(rows) inside the
map's rebuild decision, zero with the map closed; it must pass the bearing oracle. No persistence,
no terms.

### 3.7 The door taken 8 adds: water in the store

His words: *"I am happy to have water as an inventory item but you should have some tool or machine
that adds it at the last tech level"*, and *"or removes it from inventory"*.

**The quantum siphon** is a tool on T, after the excavator.

- **Left-click takes the water in a 2 m cube** at the entangled reach, into the store, for **1,000
  J/L** (the relay pump's rate; estimate). That is 8,000 L for 8 MJ a bite. Holding Q, at 4 bites a
  second, draws **32 MW** (arithmetic).
- **Right-click pours from the store, free.**
- **It works on both tiers:** on a Tier B flood it takes the cells in the bite; on a Tier A lake it
  takes from the basin.

**Taking costs** so that fighting a flood by hand is a bank decision; the siphon on Q is the fastest
way to go broke, and the runway falls while you hold. **Pouring is free** so that, during the
vault's fuse, a quick player can pour banked water somewhere harmless and shrink the dump: a skill
save (*"you might be able to like fix it if you were quick"*) that needs no reserve.

**The machine door: a vault joins its channel's outlet ring**, with its own `weight` (default 1).
Litres a pump delivers to it are **`vaulted`**, not emitted. That is how you bank a mine's water
instead of pouring it over a divide, and the price is that the water is now yours to pay rent on.

**Water is a store quantity, not a material**: no ID, no hotbar slot, never placed by the brush
(*"never by an ordinary pick or placement"*).

**The identities.**

- **Machines:** `pumped == emitted + vaulted`, per boundary and across tiers. This deliberately
  amends M10-PLAN D10.
- **Tier B:** the printed line gains twin counters `siphoned` (out), `poured` (in) and `dumped` (in),
  in the shape of `Emit` and `Return`. They never go through `Add`, where they would read as
  "injected" (Reader C, trap 7).
- **Tier A:** gets the same three counters, printed. Tier A's own identity has never been evaluated,
  so until it is, the seam checks carry the weight.
- **The store:** `held == siphoned + vaulted − poured − dumped − owed`, printed on `M10:`.

**Amended out loud, for quantum only:**

- the bucket (ladder d6 and d22), via taken 8;
- the tank on the row (d13): the vault is one, with a term from its first line;
- M10-NOTES piece 3's buffer and spill: the spill is now the designed failure;
- M10-PLAN D5, *"nothing is ever held by a machine"*, for the vault kind only.

---

## 4. The one pool

### 4.1 Energy: one bank

**The bank is the channel's pool**: every enabled vacuum cell on the channel, plus the bound quantum
backpack's own 500 MJ cell (the carried vacuum cell the ladder planned). The HUD shows one number,
`BANK`.

Because the backpack's charge is in the pool, **the pumps at home drain the cell on your back**.
That is literally *"one pool"*. It reverses M10c d4 (*"a consumer and never a supply"*) for the
quantum backpack only; the lead-acid pack keeps its own charge.

**What the hand spends.** Between boundaries, the hand spends against the bank as it stood at the
last boundary, and it is refused only when a spend would pass zero. **There is no floor**, and none
is kept back for the rent: *"what you spend in the hand comes out of the bank, and you can run it
dry."*

**The HUD bank line:**

```
BANK 612 MJ   +3.8 kW sun   −104 kW   runway 1:39:40   rent 4 kW
```

- **Runway** appears only when the net flow is negative. It is the bank divided by a 10 s moving
  average of the net (estimate).
- It turns amber under 60 s and red, pulsing, under 15 s (levers).
- The tool's refusal plays his *"failed — insufficient charge"* sound (2026-09-19).

**The failure ladder, which is the moment his feature happens:**

1. The runway turns amber, then red.
2. **Machines stall, by priority.** The quarry's pin turns amber, then the pumps'.
3. **The tool fails**: `INSUFFICIENT CHARGE`, with his sound.
4. **The vault's rent goes unpaid.** Its body glitches cyan: `VAULT LOSING CONTAINMENT — 14 s`. The
   fuse runs 10–20 s, leaning toward 10 the larger the unpaid share, like his collapse fuse
   (*"ideally the time would be from 10-20 seconds but randomly"*), and disarms if paid in full.
5. **The dump** (5.2).

**Is going wrong fun? Every rung passes three tests.** The player caused it (the spend is on the
line, in kW). It was readable in advance (the runway gave a minute). It can be recovered from,
slowly: the sun is free income, the steel pick costs nothing, the heap is your own materials, and
the water is in a lake you can siphon back once solvent. That is not protection, since the vault
still goes. It makes bankruptcy a chapter rather than a game over, and it is why the free tools stay
useful at the top tier: **they are how you walk home.**

**Cost and terms.** Rent O(vaults), runway O(channels), per boundary (estimates, negligible).
`PowerExact` gains `rent`, and the pack becomes a member; print `packInitial` too, which the printed
formula omits today (Reader C §0).

### 4.2 Items: one store

**The store is the channel's vaults**: capacity their sum, contents held per vault row. **The
quantum backpack binds the player's inventory to its channel's store.** While bound, every credit (a
pick, the quarry, a recall, the palette) goes to the store, every placement debits it, and the hotbar
shows it. Credits fill vaults in origin order (the charge write-back's rule); debits take in the same
order.

**The inventory under it.** The header's `VolumetricInventory` is unchanged (unbounded, weightless,
voxels only) and becomes **the pockets** of a player who is not bound. **Binding moves the pockets
in** as far as they fit, one printed `bound in`; the rest stays, listed as `left in pockets`.
**Unbinding moves nothing**, and litres never enter the pockets, so taking the backpack off mid-fuse
saves nothing. The pack is a window onto the vault; the pockets are what you have when it is shut.

**Full** refuses credits: a hand bite takes what fits (as a power-short bite already does, M10c),
the HUD says `STORE FULL`, and a quarry stalls `full`. **Discard** is a button per material on the
store tab, printed `discarded`: his TNT rule (*"voxels go nowhere"*) where the player chooses it.
Water is never discarded; it is poured somewhere.

### 4.3 Why a container has a gameplay reason

1. **Water has no other home.** Since taken 8 the vault is the bucket, and the only one.
2. **Reach.** What you dig goes home, you build from home anywhere, and the quarry feeds it.
3. **The stake.** The store turns the bank into a hostage. His *"else you might have to swim or mine
   to get it back"* is only true if your things are in the vault. In creative the stake is the water
   and the mess, an 8 ML dump and a heap over your panels; in survival it is everything you own.
4. **Capacity, in survival.** The pockets' trigger (M6c-2; inventory d5, *"a survival mode"*) fires
   **with survival, not now**, since bounding pockets in a creative demo is a chore with no drama. The
   store is bounded now, because capacity is what a grade sells.

---

## 5. The container ladder and the quantum vault

**Below quantum, a container is a place, not a promise.** It holds voxels only, takes no power,
never spills, and holds only its outside volume. *"Bigger on the inside"* belongs to the top rung
alone.

| Tier | Container | Holds (estimate) | Opened by |
|---|---|---|---|
| hand | `crate` (1 m) | 1 m³ | right-click within reach |
| steam | `bin` (1 m) | 1 m³ | the same |
| DC | `locker` (1 m) | 1 m³ | the same |
| AC | `warehouse_bay` (1 m) | 1 m³; bays on one cable component share a list | the cable (M10e) |
| quantum | `quantum_vault_<grade>` (1 m) | 100 to 1,000,000 m³ | the backpack, from anywhere |

**The lower rungs wait for survival**: without bounded pockets they have no reason, and the design
says so rather than inventing one.

### 5.1 The quantum vault and its bill

One 1 m machine cell (*"about the same size as the current machine blocks"*), holding **voxels and
litres as one volume**: `8.2 / 10 ML — 7.9 ML water, 300 m³ stone, 12 m³ gold`. **Rent is flat by
grade**, like a storage unit: one number that does not move as you dig, where rent by contents would
change the runway on every bite. Contents change **the size of the dump**. The rent is paid second
(after the hand, before machines) from `enabled` cells before signal overrides. The fuse starts at
the first short boundary and disarms at the first full one. No reserve is kept.

### 5.2 The dump

**What starts it:** the fuse burning out; the vault ceasing to exist (pick or collapse); or its
`enabled` set to 0 by hand, a deliberate release on the same fuse, which setting it back to 1
cancels. A vault that ceases to exist **first hands its contents to its sister vaults** and dumps the
rest, so place the new vault before taking the old one. A bankrupt channel has no sister that can
hold.

**Litres.**

- **On the analytic ground**, the water goes to **Tier A as `dumped`**. It is released over about
  10 s so the lake **visibly rises** (estimate) instead of jumping. That costs one `Emit` twin per
  boundary, at any size.
- **Underground**, it goes to **Tier B** as a ball at the vault, owed and offered every boundary
  until it fits, on the relay pour's template (`PourPending`). **The owed litres persist in the row**,
  because Ctrl+R's precedent is session-only and this water is the player's (Reader B, trap 4).
- **Tier B's load is bounded by price, not by a cap.** Banking a litre costs 1,000 J, so 1 GJ buys
  about 1 ML (arithmetic). Only an authored vault holds more, and the demo's sits on the analytic
  ground.

**Voxels.**

- **A heap around the vault in their own materials**, rising out as pieces and tumbling, at 4,096
  voxels a boundary (about 6 ms at TNT-NOTES' 1.5 µs a voxel, estimate). Past the grade's heap limit
  the rest is `decohered`: gone, printed, announced.
- **The design's visual rule: cyan is entanglement at work, black is gone, colour is yours.**
  Collapse rubble is black (*"you aint getting that back"*); the dump is in colour (*"mine to get it
  back"*). It may bury your panels; the steel pick digs them out free.

**Unloaded chunks.** Rows tick while unloaded, so a vault can go broke with nobody near it. On the
analytic ground its litres still go to Tier A. Voxels, and any Tier B litres, are **owed and released
when the chunk arrives**, in the reconcile's shape. **You walk into your own dump.**

**The undo.** A dump runs with recording off and **clears the undo stack**, as a fall does.
Otherwise Ctrl+Z would credit the whole heap back (Reader C §6). A dismantle that dumps clears it too,
which closes Reader C's duplicate-on-undo hole.

### 5.3 The stock UI

- **The store tab (E):** a capacity bar; one row per material, with its amount in m³ or L and a
  discard button; water; each vault's fill and grade; rent; state (`HOLDING`, `FUSE 12 s`,
  `DUMPING — owes 3.1 ML`); and `left in pockets`.
- **The vault's panel:** `HOLDING 8.2 / 10 ML · rent 4 kW · channel 0`, plus its two ports.

### 5.4 The identities

**Voxels. No voxel identity covers the inventory today, so the store's has to be built first**
(Reader C §3.4):

`held == bound in + dug + quarried + recalled + granted − placed − discarded − dumped − decohered ± handed between vaults`

It is printed on `M10:`, and a test holds it over 1,000 random operations.

**Litres:** 3.7. **Joules:** 4.1.

**Rows.** Contents move with `KindState`, so they get an `entering`/`leaving` pair, like charge does.
**Restore refuses a vault whose contents exceed its capacity; it does not clamp.** The vault's codec
refuses an unknown version rather than reading it as empty (Reader C, trap 8).

---

## 6. Grades

**A grade lives in the ID, for the vault only, in the demo**: for example
`quantum_vault_high_grade`, labelled *quantum vault high grade* with no extra field.

- A per-stack grade breaks "a count is the truth".
- A grade stored in the row is lost when the vault is dismantled.
- **The ID is the only home that survives the pick.**

| Grade | Holds | Rent (estimate) | Heap limit (estimate) |
|---|---|---|---|
| low | 100 m³ | 250 W | all |
| medium | 1,000 m³ (1 ML) | 1 kW | all |
| high | 10,000 m³ (10 ML, his figure) | 4 kW | all (640,000 voxels) |
| mythical | 100,000 m³ | 16 kW | 10,000 m³; the rest decoheres |
| god | 1,000,000 m³ | 64 kW | 10,000 m³; the rest decoheres |

Each step up holds ×10 for ×4 the rent, so it is 2.5× more efficient per cubic metre and dearer to
keep. **The god grade's 64 kW is 128 panels in full sun** (arithmetic). In creative, *can you afford
the god vault?* is a goal the player sets themselves. **The names are half the joke**, so all five
ship.

**Cost:** five permanent IDs, and one texture layer tinted per grade later (a C# colour entry).

**Grades stay off every other machine until crafting.** *"Your battery tech maybe 19th century but
your actual emitter/entangler could be a higher tech variation"* is about parts, and parts are
M10g. When they land, an assembled machine carries the grade of the part that bounds it.

---

## 7. The automated quarry

**What it digs.** A box in front of the block, along its facing: `width` × `length` (2–16 m, in 2 m
steps), from the block's level down to `depth` (2–64 m).

- **The top layer goes first**, in a snake across each layer.
- **It takes one 2 m tunnel bite at a time** (*"I like the 2m cube"*), `rate` bites a boundary
  (1–4).
- **It never digs a machine body.** Your pump is left on a 1 m pillar, which is the lesson to put
  pumps in a sump beside the footprint, the way a real pit is dewatered.

**Joules per voxel.** 50, the quantum tools' rate. That is 25.6 kJ a bite, **51 kW at rate 1 and 205
kW at rate 4** (arithmetic), so it is a moderate spender on purpose.

**Its road to bankruptcy is indirect.** It opens a void; the water finds the void; the void needs
pumps at 100 kW each; the flood pulls you to the siphon, at megawatts. His words: *"If you 'load up
the system' too much you end up flooding your mineshaft"* (2026-09-19).

**The spoil goes to the store.** When the store is full the quarry stalls `full`: automation stops
where you can see it and never destroys anything silently.

**What stops it:**

| Stall | Cause |
|---|---|
| `done` | the footprint is dug to depth |
| `off` | `enabled` is 0 |
| `signalled` | `onsignal` 1 (the default) while a sensor trips its channel, for collapse or bank |
| `unpowered` | the bank cannot pay for the bite |
| `full` | the store has no room |
| `flooded` | the next bite has water over it: one `HeldIn` read, O(1) |
| `unloaded` | a chunk of the next bite is unloaded |

**`flooded` is the demo's loop**: the quarry needs a pump, and the pump needs the bank.

**`unloaded` is settled by taken 9 and not reopened**: *"want automated work? We expect you to at
least be on the job site for OH&S reasons"*. The design leans in: the board's row reads `OFF SITE —
waiting for the operator`, and the card and the README carry his line. The quarry digs while your
hands are busy fifty metres away, which answers his worry (*"anything from the tier of automatic
machines will appear useless"*) by a difference in kind: the excavator in hand is faster, and the
quarry works while you fight the flood.

**What water does.** It stalls `flooded` until the pumps clear it. **Tier B water does not weaken
rock; only Tier A's head does** (`Saturation.cs`: *"derived … from Tier A's one number per basin"*).
So a quarry beside a lake is safe until the lake is over it, which is why the demo puts the lure
under the lake, and my third weakest point.

**What a collapse does.** The block is a machine body: a fall destroys it, or recalls it while you
are solvent. Fallen walls land in the pit as rubble, and **the quarry digs the rubble out** as it
resumes, grinding on. Its default `onsignal` 1 lets a collapse sensor stop it while a roof is armed.

**Undo.** Its writes are the world's (*an observer sees every writer*) and run with recording off.
**Undo steps touching a chunk it wrote this boundary are dropped** (the `Forget(chunk)` shape), so
Ctrl+Z still works away from the quarry and can never un-dig a bite and credit the store twice.
Clearing the whole stack every boundary would throw away *"Ctrl z feels good"*.

**CPU per boundary.** About **0.8 ms a bite** at 1.5 µs a voxel (estimate, from TNT-NOTES' Debug-era
M9c-1b figure), plus the worker's remesh. **One quarry at rate 1 crosses the `machines > 1 ms`
trigger alone** (estimate); the lever is the carve after the tick, time-boxed. **It also fires
M10-PLAN D3's stutter trigger**; I read a 2 m bite every half second as a stamp mill's beat, but that
is his eye.

**Persistence and terms.** Its ports, plus a layer-and-index cursor in `KindState`. `quarried` on the
store's line; `drawn by quarries` in `PowerExact`.

**Chunk-sized quarrying is not a machine.** A bite of 32,768 voxels is about 49 ms a boundary
(estimate). His *"upgrade for chunk support"* is the excavator in your hand.

---

## 8. Materials

**What the ideal machines are built from.** In the demo, nothing: the palette grants them. The
survival recipes (M10g) and ores in worldgen (M10h) are **cut from this pass**. An ore that worldgen
places moves every oracle unless it follows the relief precedent, and that needs its own pass.

**What the quarry yields.** The voxel itself. There is no drop table (`Carve.cs:765`).

**One new material for the demo: `gold`.** MATERIALS-NOTES has gold as *"nothing but sparkle"*,
and that is its job here: **a lure with no use**. A newcomer needs a reason to dig *there*, under the
lake, in the first minute, and a glint at the shore that dives under the water is that reason in a
mining operation's own words.

- **Estimates for him to tune:** hardness 2.5 (the steel pick takes it); `supportDistance` 12 (the
  seam is a weakness); a steep wet curve (2 m head → 6); permeability 0.1 (it draws water);
  `lightEmission` 3 (it glints). It passes MATERIALS-NOTES' test: it does something to gravity and
  water.
- **Placed by hand in the template, never by worldgen**, so r6 and every fixed-seed oracle stay to
  the digit, as clay and glass did (Reader E §5.4).
- **Not in the palette.** A free palette of gold would make the lure worthless.

**Cost.** Eight permanent IDs: gold, five vaults, the quarry, the backpack. The siphon is a tool;
the sensor is f-1's. One colour entry, and three content-counting tests change.

---

## 9. The creative-mode demo

### 9.1 What a mode is

**In the save:** a `Mode` field in header v8, taken **together with** M10g's hotbar assignment, which
the demo needs anyway (a nine-slot row sorted by ID drops holdings off its end, Reader D). Values:
`sandbox` (every existing world, unchanged; a v7 world opens as this), `creative`, and `survival`
later. `--mode=creative` applies only at creation.

**In code:** a `GameMode` read at five Game sites (palette, debug-key gate, tier at open, card,
hints). **Nothing in Core reads it**; the physics is the same in every mode.

### 9.2 What creative unlocks

- **Unlocked:** the palette tab of the site office (E), which clicks any machine or building block
  into the store as a printed `granted` count, so the undo is untouched (Reader D, trap 8); every
  tool on T; the tunneller in hand at open.
- **Not unlocked: ore, energy and water.** A placed cell starts empty. **`--nogate` is not creative
  power**; it switches off the economy the demo exists to show.
- **Off in creative:** P, Alt+P, R, Ctrl+R, Alt+R, Alt+G, H and L. Alt+P alone would make bankruptcy
  impossible. Sandbox keeps them all.
- **Unchanged: F (fly)**, the only way out of burial until M11: *"F — fly (for when you're buried)"*.

### 9.3 The site is a world he builds by playing it

**The demo is a template world, shipped as a save** and copied to a fresh name by `--template=demo`:
his seed, a lake he filled, a gold seam he placed under its near edge, the vault on the shore
holding the last operator's water, eight panels, a charged cell, and his clock set so a storm arrives
mid-demo. **None of it is code**: persistence as built, migrating like any world. **He authors the
first five minutes by playing them**, which suits *"it's more artistic endeavor"* better than a
scenario format.

**The card's one fiction:** *"The last operator pumped this mine into a quantum vault — 8 million
litres, bigger on the inside — and left you the bank that holds it. Automated work runs only while
you're on the job site (OH&S)."* **The demo starts with a loaded gun on the table**: the rent is on
the bank line from the first frame.

### 9.4 The first five minutes, beat by beat

These are estimates for his template. **The bank is 1 GJ; 8 panels give 4 kW in sun; the vault is
high grade, holding 8.0 of 10 ML of water, at 4 kW rent.** Idle in sun, the net is about −0.2 kW, so
no runway shows.

| Time | Beat | What happens | In hand | Bank · store |
|---|---|---|---|---|
| 0:00–0:20 | **Arrival** | The card: *"OVERBURDEN. You run a mine. Water wants in; the rock wants down. Everything you own runs off one bank and sits in one vault. Don't go broke."* Then the keys. You stand on the bluff: the lake, gold glinting at its edge, the vault shimmering cyan. | tunneller; backpack on channel 0; computer; store has 4 pumps, 4 outlets, a quarry, a bank sensor, clay, glass | 1,000 MJ · 8.0 / 10 ML |
| 0:20–1:00 | **The glint** | Each bite of the gold outcrop flashes cyan and the store ticks up. The line shows `−102 kW` while Q is held; the runway reads hours. The seam dives under the lake, and you follow it. | tunneller | ~998 MJ · + gold |
| 1:00–1:45 | **The water finds you** | Under the lake's head, the roof takes the strain tint. Hint: *"Tinted rock is failing. Get out from under it."* The 10–20 s fuse runs, then the cyan Tetris pieces and the phonk, and **the lake breaks in**. The premise arrives through your own digging. | tunneller (F if buried) | ~995 MJ |
| 1:45–2:45 | **Fighting back** | Hint: *"Seal the breach, then drain."* Clay placed into water displaces it and seals the breach. Then either **the siphon** (hold Q: the pond shrinks in seconds, and the line reads `−32 MW · runway 0:21` in red, the lesson felt) or **pumps** at 100 L/s each (cheap by the second, and slow). Most players use both. | siphon, clay, pump | ~500 MJ after ~500 m³ siphoned · 8.5 ML |
| 2:45–3:30 | **The quarry** | Placed on the rim, it starts biting 2 m cubes on its own, each with a flash and a thunk; a pump goes in a sump. **The operations board** (the computer, right-click on nothing) lists the quarry, the pumps, the vault, the bank, and priority as a column. | computer, pump | ~490 MJ · spoil climbing |
| 3:30–4:30 | **The storm** | His clock brings overcast and rain. Sun drops from +3.8 to about +0.5 kW, and the runway appears. Rain and seepage fill the deepening pit; the quarry stalls `flooded` and resumes as the pumps catch up. | what he reaches for | ~420 MJ · runway ~20 min |
| 4:30–5:00+ | **The fork** | **Greedy:** a second breach or a second siphon job. Red. Pumps `unpowered`, the quarry stalls, *"failed — insufficient charge"*, then `VAULT LOSING CONTAINMENT — 12 s`. A quick player pours what he can over the ridge. **The dump:** the lake rises over ten seconds, spills over the rim and puts head on the walls, bringing a second collapse to the phonk. He stands on the bluff with a steel pick. **Careful:** at amber he switches the quarry off from the board, the vault holds through the storm on its rent, and the sun returns. | steel pick · or computer | 0 · 0; or ~300 MJ · full |

**Why the fork is the right ending.** Both paths show every system, and neither ends the game. The
greedy path shows his feature, with the player owning every link: *I spent too much; the vault let
go; the lake rose; the walls went.* The careful path shows an operation that works. After five
minutes come recovery (dig out the panels, seal, siphon the lake back once the sun has refilled the
bank) or expansion (a solar farm, a bank sensor on the quarry, the god vault).

**The pacing turns on one quantity: the siphon's J/L against the starting bank.** Siphoning a 500 m³
breach costs 500 MJ, so a 1 GJ bank survives one flood fought by hand, but not two. The storm's
timing is the second knob. His play sets both.

### 9.5 What it must not break

- **Sandbox worlds** are unchanged, TajMahal and the retired measurement worlds included.
- **The r6 control:** a plain `--nosave` run has no world and so no mode; prove it to the digit in
  the mode's slice.
- **The oracles:** gold is never generated, and the gate oracle (`removed 6, unpowered 57 of 64`,
  lead-acid pack) must read the same after the backpack slice.
- **Persistence:** v8 migrates v7 additively; stock and the quarry's cursor ride `KindState`
  versions; the owed dump and the fuse persist, **captured while alive**.
- **The ladder:** creative sits beside it and gates nothing below.

**Handing it over** is its own slice: an export preset, the SQLitePCLRaw natives verified, and the
phonk, his gitignored file, which he ships or the collapses are silent.

---

## 10. The split

Each slice is a session or less. Core is cut from the wire. Each slice names its test or oracle.

| Slice | What | Test or oracle |
|---|---|---|
| **V-1** Core | The `vault` kind; a strict stock codec (counts, litres, fuse, owed); the channel store; origin-order fill; full refuses; the store's voxel identity; entering/leaving; restore refuses overflow | `held ==` exact over 1,000 random operations; unknown version refused; over-capacity restore refused and counted |
| **V-2** Core | One pool: the backpack as a member; the hand settled first, then rent; the 10–20 s fuse; runway; rent before signal overrides | Bankruptcy fixture: spend → rent short → fuse → dump event; the fuse disarms when paid; a signal never starts a fuse; `PowerExact` exact with `rent` |
| **V-3** Core | The dump: Tier A `dumped` released over N boundaries; Tier B owed and persisted; the budgeted heap in its own materials, with its limit and `decohered`; sisters first; recording off; stack cleared | Litres exact across tiers; the unloaded arm owes and releases at arrival; the heap never writes into a body, the player or an unloaded cell |
| **V-4** wire | HUD bank line; store tab; vault panel; `M10:` store, rent and fuse terms; `--placeat=` for vaults | Headless: fill a vault, a small bank, a scripted spend → the log reads fuse then dump, every identity exact; r6 to the digit |
| **W-1** Core + wire | The siphon (take at J/L, pour free, both tiers); the vault as an outlet; `pumped == emitted + vaulted` | Exact per boundary; M8's oracle unmoved with the siphon unused |
| **C-1** Core + wire | Header v8 (mode and hotbar assignment); palette and `granted`; debug-key gate; tunneller at open; card and hints; `--template=`; `gold` | r6 to the digit; v7 opens as sandbox; undo across grants |
| **Q-1** Core | The `quarry` kind: footprint walk, bite, stalls, never a machine, cursor, store credit, chunk-scoped undo forget | Footprint order; a pump in the footprint survives on its pillar; every stall reachable |
| **Q-2** wire | The carve after the tick, time-boxed; panel; `quarried`; `OFF SITE` wording | Headless quarry on the fixed seed: bites, and the `machines` figure against the 1 ms trigger |
| **P-1** Core + wire | Priority and the operations board; the computer's right-click on nothing | Short-pool fixture pays hand, rent, priority, origin, in that order; docs corrected |
| **T** his | He builds the template by play | — |
| **D** his | He plays the five minutes: one sentence each on the siphon's price, the storm and the dump | — |
| after | S-1 bank sensor · M-1 telemetry · R-1 recall at release · X-1 export | each carries its own oracle |

---

## 11. Numbered decisions

Each is my recommendation, then its cost and the trigger that would revisit it.

1. **`vault` and `quarry` are C# kinds declared in JSON; D1 is amended until M12.**
   - *Cost:* two more exceptions, at about twenty switch sites each.
   - *Revisit:* M12, or a fourth kind in this family.
2. **Extras are a bitset read at one site each, declared per tier and opted out per machine; new
   blocks reject unknown keys.**
   - *Cost:* a `tiers/` loader pass, and a strictness rule unlike the old blocks'.
   - *Revisit:* an extra needs parameters per machine.
3. **One bank: the backpack's cell is a member; the hand is settled first, then rent, then
   priority; no floor.**
   - *Cost:* reverses M10c d4 for the quantum pack.
   - *Revisit:* he reads the order as the game protecting him.
4. **A runway on the HUD: amber at 60 s, red at 15 s** (estimates).
   - *Cost:* one HUD line.
   - *Revisit:* he went broke without noticing, or found the warning nagging.
5. **One store: binding moves the pockets in, as far as they fit; unbinding moves nothing; no
   litres in the pockets.**
   - *Cost:* two places for items, one active at a time; a legacy world binds only partly.
   - *Revisit:* he reads `left in pockets` as a second inventory.
6. **A full store refuses (the hand takes what fits; the quarry stalls); discard is a printed
   button.**
   - *Cost:* the excavator fills a high vault in about 20 bites.
   - *Revisit:* his play wants the overflow discarded silently.
7. **Rent is flat by grade and paid before signal overrides; a signal never dumps a vault.**
   - *Cost:* one special case in the pool's order.
   - *Revisit:* he asks for his original *"cut power … and flood the place"*.
8. **The vault's fuse is 10–20 s and leans with the unpaid share,** the collapse fuse's arm.
   - *Cost:* a persisted timer per vault.
   - *Revisit:* "suddenly" meant no fuse.
9. **Dump to Tier A over about 10 s, or to Tier B owed and persisted; voxels as a coloured heap at
   4,096 a boundary up to the grade's limit, the rest `decohered`.**
   - *Cost:* about 6 ms a boundary for up to about 78 s (estimate), and a third home for owed litres.
   - *Revisit:* the heap measures over 5 ms, or decoherence reads as unfair.
10. **A vault leaving the world hands its contents to its sisters first; `enabled` 0 by hand is a
    deliberate release.**
    - *Cost:* moving a vault needs its sister placed first.
    - *Revisit:* dismantling reads as a trap.
11. **The siphon: a 2 m bite, 1,000 J/L to take, free to pour, both tiers; the vault joins the ring
    as `vaulted`.**
    - *Cost:* amends d6, d13, piece 3 and D5/D10 for quantum; three counters on each tier.
    - *Revisit:* the free pour gets used as a weapon, or 1,000 J/L makes the second flood
      unwinnable.
12. **Water is a store quantity, not a material or a hotbar slot.**
    - *Cost:* reads "inventory item" narrowly. *Revisit:* he wants water on the hotbar.
13. **Priority defaults: pumps 5, the quarry 7; ties by origin.**
    - *Cost:* revives a pump port.
    - *Revisit:* M10d's storm play shows starvation reading wrong.
14. **The board is a tab of the site office on E, and also the computer's right-click on nothing.**
    - *Cost:* one new mouse-releasing screen with three tabs.
    - *Revisit:* he wants the board on the map.
15. **Telemetry is state colour on the existing pins, quantum only; pins stay at every tier.**
    - *Cost:* a legend line.
    - *Revisit:* it fails the bearing oracle, or reads as noise at 16 m a pixel.
16. **Recall happens at release, only while solvent and with room; vaults opt out; none below
    quantum.**
    - *Cost:* the director learns that machines exist.
    - *Revisit:* he finds the top tier too safe.
17. **The quarry: 2 m bites, rate 1–4, 50 J/voxel, spoil to the store; stalls `flooded` and
    `OFF SITE` (taken 9); never a machine; chunk-scoped undo.**
    - *Cost:* crosses the 1 ms trigger alone (estimate), and fires D3.
    - *Revisit:* his eye on the 2 Hz beat.
18. **Grades live in the ID, on the vault only, all five names.**
    - *Cost:* five permanent IDs.
    - *Revisit:* M10g gives parts grades.
19. **Creative is `Mode` in v8, alongside the hotbar assignment: palette free; ore, energy and
    water not; debug keys off; fly unchanged.**
    - *Cost:* the next header number, and a new concept.
    - *Revisit:* survival's pass needs the meaning changed.
20. **The demo is a template world he builds by play.**
    - *Cost:* a shipped save to keep current, and drama that depends on authorship.
    - *Revisit:* a second site, or a random-seed demo.
21. **`gold` is placed by hand, never generated, never in the palette.**
    - *Cost:* one ID, one colour, three tests.
    - *Revisit:* M10h places ores.
22. **The bank sensor generalises f-1's kind; the water sensor waits.**
    - *Cost:* a second `senses` value.
    - *Revisit:* a play wants a flood alarm.
23. **Tier B saturation (a flooded pit weakening its own walls) is named and not built here.**
    - *Cost:* the premise bites only under a lake.
    - *Revisit:* he says a flooded mine that never weakens reads wrong.

---

## 12. Must not ship

1. **A reserve floor under any name**: a percentage kept for the vault, an automatic stop at low
   bank, or a quarry that switches itself off without a sensor the player placed.
2. **The dump sent through `Add` or `Emit`.** Each door has its own twin counter.
3. **A dump or a quarry bite inside the undo's recording.**
4. **A vault codec that reads an unknown version as empty, or a restore that clamps contents.**
5. **Recall at the drain.**
6. **`--nogate` used as creative's power, or Alt+P left live in creative.**
7. **A creative default that reaches a plain headless run.**
8. **Gold from worldgen, or the demo's seed on the profile path.**
9. **Stale docs left standing**: "placement order" for priority, the 20 Hz tick, the brief's
   "exists" for the dock.
10. **A new key that loads silently when it is misspelt.**
11. **Pins gated to quantum.**
12. **The template probed with bare `start.cmd`, which opens TajMahal.** Probe copies only.

---

## 13. My design's three weakest points

1. **The demo's drama is authored, and nothing guarantees it.**
   - The first collapse needs the player to follow the gold under the lake. The finale needs a
     second flood, and the storm arriving on cue.
   - A player who digs on the ridge gets five quiet minutes. The hints are the only steering.
   - The template must be re-authored whenever an engine change shifts how the site behaves.
2. **The voxel half of the store earns little in creative and costs a lot.** Matter is free there,
   so the heap's stake is mess and buried panels, not loss.
   - It still needs a voxel identity built from nothing, a heap writer at about 6 ms a boundary,
     decoherence, the rules for binding the pockets, and a discard button.
   - The water half carries the demo. The voxel half is survival's feature, arriving before
     survival exists.
3. **The premise's core loop only bites under a Tier A lake.** Saturation reads only Tier A's basin
   number, so a flooded pit never weakens its own walls.
   - A quarry's flood is harmless to rock except under a lake.
   - A dump matters only where it raises a lake over your work.
   - So the demo site has to be built around the one place the keystone works.
   - Every pacing figure here (1 GJ, 1,000 J/L, 4 kW rent, 10–20 s) is an estimate that his first
     play will move.
