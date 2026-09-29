# Designer 1 — the engine-cost design: the account, the vault, the quarry

The cheapest honest design that reaches everything Matthew took. Every figure is labelled
**measured** (with its source) or **estimate**. Code claims were checked against `main` at `71e6eed`.

---

## 1. The idea in five sentences

1. **A channel is an account.** At the quantum tier the carried pack is a window, not a battery:
   the tool pays straight out of its channel's storage rows, and the pocket routes mined blocks
   into that channel's vault. That is his one pool and his one store, built on the pack slot the
   header already has.
2. **One new kind for every item container (`container`) and one for the quarry (`quarry`).**
   Everything else is an *extra*: a closed C# flag set that a machine opts into with an `extras`
   list. Its reach per medium is declared once in the base mod's `extras.json`.
3. **The vault pays a hold bill at the boundary.** An unpaid bill lights a fuse. A burned fuse is
   terminal until the vault is empty: blocks come out as a heap written bottom-up around it,
   litres through the relay pour's ball, and both wait for an unloaded chunk to arrive. Its
   contents are the owed amount, so nothing is ever in flight.
4. **The quarry is a quantum miner on a stand.** It calls the existing `Carve.Cube` overload
   with a named origin, bites into the channel's vault, and pays the pool at the boundary under a
   global bite budget. Water and the roof do the rest.
5. **Water enters and leaves the inventory only through a quantum door** (taken 8): a pump into a
   vault, the siphon tool in the hand, or the dump. Each door is a twin counter on the tier it
   touches, and litres live only in vault rows, never in the header.

---

## 2. The extras rule

### 2.1 Engine side

`MachineKind` stays closed. Extras are a second closed set:

```csharp
[Flags] public enum MachineExtra : ushort
{ None = 0, Sense = 1, Recall = 2, Remote = 4, Charge = 8, Priority = 16, Telemetry = 32 }
```

- `MachineDefinition` gains `Extras` (the flags) and `ExtraForm(extra)`, a byte per extra resolved
  at load from the machine's pool medium. That is six bytes on a definition and nothing on a row.
- **Each extra has exactly one reading site**, named in §3. Sites test `definition.Has(extra)`,
  never the kind. That is how a new kind stays out of twenty switch sites: sense, priority and
  recall are written once and reach any kind that opts in.
- **The reach is membership.** A per-pool extra (sense, priority, the charge window) keys on the
  pool id: the channel number today, and a component id when M10e exists. It needs no code per
  tier, only a pool id per tier, which is the ladder's one idea applied to capabilities. **The
  brief's "cheap, since membership is already computed" is true for channels only.** Below
  quantum there is no pool id to key on until M10e and M10f.

### 2.2 The JSON grammar

**Opt-in.** A machine block lists its extras by name: `"extras": ["sense", "priority"]`.

**Inheritance** is declared once, in `mods/<mod>/extras.json`, as a medium → form map per extra.
It loads before materials, so a machine's list is checked against it. Forms are a closed C# set
per extra. The base game's file states the whole ladder:

```json
{
  "sense":     { "over": { "channel": "switch", "ac": "switch", "dc": "switch", "shaft": "bell" } },
  "recall":    { "over": { "channel": "return" } },
  "remote":    { "over": { "channel": "anywhere", "ac": "component" } },
  "charge":    { "over": { "channel": "account", "ac": "dock", "dc": "dock" } },
  "priority":  { "over": { "channel": "order", "ac": "order", "dc": "order", "shaft": "order" } },
  "telemetry": { "over": { "channel": "state", "ac": "state", "dc": "state", "shaft": "state", "crank": "state" } }
}
```

- **Media are free class strings** (ladder decision 21). A lower medium's entry loads as a
  declaration and stays inert, because no machine on `ac`, `dc` or `shaft` can load until M10e.
  So the base game states its inheritance today, as a mod would, and the engine serves it as each
  tier's membership lands.
- A third-party mod's `extras.json` may add its own medium to an extra (say `"fibre": "switch"`).
  A merge conflict, meaning the same (extra, medium) pair given two forms by two mods, is refused
  with both files named.

**What the loader refuses** (each with the file named):
1. An unknown extra name, or an unknown form for that extra.
2. An extra whose merged `over` lacks the machine's pool medium.
3. An extra on a kind outside its C# kind set, per the §3 table. In particular:
   - `sense` on a container, to keep collapse f's decision 10;
   - `recall` on a container or a carried storage;
   - `charge` on anything but a carried storage.
4. An extra whose required port is missing or mis-bounded: `sense` needs `onsignal` 0..2, and
   `priority` needs `priority` 0..15.
5. `charge` in the `account` form together with a `capacityJoules`, since a window holds nothing.
6. **Any unknown key.** Today a typo'd `extras` loads as absent without a word (Reader A trap 4).
   Every DTO gets `[JsonUnmappedMemberHandling(Disallow)]`, which is available on `net8.0`, and
   one test asserts that every shipped file loads under it. The ladder plan's example keys
   (`stockVoxels`, `conductor`, `fuel`, `dock`) will then refuse, which is correct: they are plan,
   and each lands with the part that reads it.

### 2.3 Four machines in full

The **quantum vault** (medium grade). Names are Matthew's to give; the IDs here are placeholders.

```json
{
  "id": "quantum_vault_medium",
  "hardness": 3.0, "supportDistance": 8, "permeability": 0.0, "density": 7800,
  "textureLayer": 13, "unit": "cells",
  "machine": {
    "kind": "container",
    "capacityLitres": 2500000,
    "holdWatts": 10000,
    "fuseSeconds": 10,
    "dumpVoxelsPerSecond": 2048,
    "dumpLitresPerSecond": 2000000,
    "extras": ["priority", "remote", "telemetry"],
    "links": {
      "power": { "carries": "power", "role": "sink", "via": "channel", "port": "channel" },
      "store": { "carries": "stock", "role": "sink", "via": "channel", "port": "channel" }
    },
    "ports": {
      "channel":  { "default": 0,  "min": 0, "max": 15 },
      "priority": { "default": 12, "min": 0, "max": 15 },
      "water":    { "default": 0,  "min": 0, "max": 1 }
    }
  }
}
```

The **quarry**:

```json
{
  "id": "quantum_quarry",
  "hardness": 3.0, "supportDistance": 8, "permeability": 0.0, "density": 7800,
  "textureLayer": 14, "unit": "cells",
  "machine": {
    "kind": "quarry",
    "bite": "cube",
    "bitesPerSecond": 2,
    "hardnessThreshold": 5.0,
    "joulesPerVoxel": 50,
    "extras": ["sense", "recall", "priority", "remote", "telemetry"],
    "links": {
      "power": { "carries": "power", "role": "sink",   "via": "channel", "port": "channel" },
      "spoil": { "carries": "stock", "role": "source", "via": "channel", "port": "channel" }
    },
    "ports": {
      "channel":  { "default": 0, "min": 0, "max": 15 },
      "onsignal": { "default": 0, "min": 0, "max": 2 },
      "priority": { "default": 4, "min": 0, "max": 15 },
      "enabled":  { "default": 1, "min": 0, "max": 1 },
      "width":    { "default": 4, "min": 1, "max": 16 },
      "length":   { "default": 4, "min": 1, "max": 16 },
      "depth":    { "default": 8, "min": 1, "max": 64 }
    }
  }
}
```

The **quantum pack**, the account's window:

```json
{
  "id": "quantum_pack",
  "hardness": 1.0, "supportDistance": 2, "permeability": 0.0, "density": 4000,
  "textureLayer": 10, "unit": "count", "placeable": false,
  "machine": {
    "kind": "storage", "carried": true,
    "extras": ["charge"],
    "links": { "charge": { "carries": "power", "role": "sink", "via": "channel", "port": "channel" } },
    "ports": { "channel": { "default": 0, "min": 0, "max": 15 } }
  }
}
```

The **siphon** (a tool, so it takes no material ID):

```json
{ "id": "quantum_siphon", "bite": "tunnel", "takes": "water", "cooldownSeconds": 0.25,
  "power": { "via": "channel", "joulesPerLitre": 100 } }
```

`relay_pump.json` and `vacuum_cell.json` gain `"extras": ["sense", "recall", "priority", "remote",
"telemetry"]` (the cell omits priority) and the ports those extras need. `carries: "stock"` is one
new value on an existing enum. Links stay what they are today: validated and not read by the tick.

---

## 3. The six extras

| # | Extra | Kinds | Reading site | Per boundary | Persists |
|---|---|---|---|---|---|
| 1 | sense | pump, placed storage, quarry | `MachineTick`'s override (f-1's) | f-1's (2r+1)³ × 2 lookups a sensor (estimate, COLLAPSE-PLAN d2) | nothing (f-1 d4) |
| 2 | recall | pump, outlet, converter, placed storage, quarry | `MachineSet.Dismantle` destroy branch + `CollapseDirector` release | 0; one `bool[]` read per released voxel at a fall | nothing |
| 3 | remote | any placed | `MachinePanel` ◀ ▶ | 0 | nothing |
| 4 | charge | carried storage | `EquippedPack` source | 0; O(cells on channel) per bite | the pack slot, as today |
| 5 | priority | pump, quarry, container | the consumer list's sort | 0 (sorted at rebuild) | the `priority` port |
| 6 | telemetry | any placed | `Main.MapOverlayNow` | 0; O(rows) per map rebuild while open | nothing |

**1. Sense.**
- **At quantum:** exactly collapse f-1: a per-channel boolean, recomputed each boundary and never
  stored, with readers taking `onsignal` 0/1/2.
- **What this design changes:** only *who is a reader*. f-1 decides that by kind (decision 5);
  here it becomes the `sense` extra. The shipped pump and cell declare the extra, so the reader
  set is identical, and a test pins it. That is f-1's own reversal clause at the smallest scale.
- **Not built here: a separate signal medium.** Routing signals independently needs a second
  channel port on every reader and a second 16-wide array, and nothing in this pass needs it.
- **Added in slice IM-10:** `"senses": "power"` on f-1's `sensor` kind. It signals while its
  channel's pool is below its `belowMegajoules` port. Cost: one comparison a sensor a boundary.
  It is the player's own protection against bankruptcy. The engine's protection is none.
- **The vault is never a reader.** A signalled-off vault would dump, which is his rejected flood.
- **Down the ladder:** the switch form for AC and DC needs M10e's component id; the bell at steam
  is shaft membership (M10e/f); hand has none.

**2. Recall.**
- **At quantum:** a recall machine destroyed by a fall returns whole to the pocket (machines
  never route to a vault; see §4).
- **The matter must not double.** Reader C's option (b) is the only exact one:
  - At release, `CollapseDirector` writes air over voxels whose material recalls, as it does
    today, but **leaves them out of the piece** and counts them `recalled`. The director learns a
    material predicate (`bool[]`), not that a machine exists.
  - Its identity becomes `released == landed + in flight + lost + recalled`.
  - At the drain, the destroy branch credits `64 − Carved`, the body minus what a pick already
    took, and counts `Recalled`.
  - A recalled cell's charge leaves through `leaving`, as a dismantle's does.
- **Pieces mode only.** The legacy sweep keeps destroying, and the `M9:` line says so once.
- **It needs the drain's destroy branch bracketed recording-off.** That is the gap Reader C §6
  found, and it is owed anyway. Without it, Ctrl+Z re-adds the recalled row and the pocket keeps
  the 64 as well.
- **Down the ladder:** none, deliberately (the brief's table). Losing a pump to the roof is the
  fight.

**3. Remote panel.**
- The computer already reaches 512 m in line of sight with a quantum tier held
  (`PlaceController.cs:821-822`). A click on nothing already opens the equipped pack's panel.
- **The extra adds two buttons to the existing `MachinePanel`: ◀ and ▶.** They step through rows
  with the extra on the pack's channel, in origin order.
- It works with the machine unloaded, because rows tick unloaded and `SetPort` is row-local.
- Cost: no new Control and no new key, about 80 lines of Game (estimate).
- **Down the ladder:** AC's `component` form lists the pack's cable component, M10e. Nothing below.

**4. Charge.** At quantum this is **the account**, the §4 window, the brief's "extra 4 made the
defining property". Below quantum the form is `dock`. The dock is M10f and **does not exist**
(Reader A trap 12): the shipped lead-acid pack recharges wirelessly over its channel until M10i,
and this design leaves that untouched.

**5. Priority.**
- **At quantum:** one consumer list per boundary holds pumps, quarries and container bills,
  sorted by (`priority` descending, origin) at rebuild. `Run()`'s pump loop becomes a loop over
  it with a switch on the kind, so the order lives at one site rather than three.
- **The shipped pumps' defaults are equal**, so equal priorities reproduce today's origin order
  and **no M10 oracle moves**. The container defaults to 12 and the quarry to 4, both new kinds
  that no oracle measured.
- The tool is not in the list. It draws between boundaries, so the hand is always first. That is
  bankruptcy's path, and it is on purpose.
- **Down the ladder:** every medium with a pool (AC, DC, shaft). The list is per pool, so the cost
  per medium is zero once M10e gives the pool id. Hand has none.
- This revives a pump port that M10b dropped for having no reader. Now it has one.

**6. Telemetry.**
- `MapPin` gains a `State` byte: ok, stalled, unpowered, fuse, dumping. The map draws it as the
  pin's rim.
- **Pins stay for every machine.** They are built un-gated, and gating them would be code written
  to remove a feature.
- State is a property of the place, so the bearing oracle (`TheSameGroundIsTheSameColour…`) still
  holds.
- **Down the ladder:** declared over every medium. It reads `row.LastStall`, which every kind
  writes and which needs no membership.

---

## 4. The one pool

### 4.1 Energy: the account's window

**The window.**
- `EquippedPack` gains an optional `IChannelPool Window`, set when its definition has
  `charge:account`.
- `ChargeMillijoules` stays the pack's **own** charge, and for a window it is always 0.
- A new `AvailableMillijoules` reads `Window.StoredOn(Channel)`, the Σ of the channel's in-pool
  storage rows.
- `Take` routes to `MachineTick.Draw(channel, mJ)`, which debits those rows in **reverse** origin
  order (last filled, first spent, so the write-back's shape survives) through
  `StorageState.Write`, and still adds to `TakenMillijoules`.
- **Both tool sites are unchanged.** `MiningController.cs:503/519` and `PlaceController.cs:420/430`
  already call `VoxelsAffordable` and `Pay`.

**PowerExact needs no new term.** The rows drop by X, and `packTaken` rises by X on the right.
Written out: `stored + 0 == entering − leaving + generated − drawn − clamped + initial + debug −
(taken + X)`, with stored reduced by X on the left.

**Header change: none.** `PackSave(material, charge 0, channel)` rides v4's slot.
`CarriedPacks` never stores a zero charge, so a stowed window forgets its channel. That is
acceptable, and the panel sets it again.

**What a tool's spend does:**
- The excavator's chunk bite is 32,768 voxels × 50 J = 1.64 MJ (Reader D, arithmetic). It comes
  out of the bank the instant it bites.
- Pumps, quarries and vault bills pay at the next boundary from what is left.
- Draw skips cells that are off or signalled off, exactly as `BeginPower` does, so what the hand
  can reach is what the pool holds.
- Cost per bite is O(cells on the channel): at 13 cells, well under a microsecond (estimate).

**Bankruptcy, and its warning.**
- **No floor. No refusal beyond "the rows are empty".**
- The HUD's pack term becomes `BANK ch 0 — 4.21 GJ · −12 kW · 97 h`.
- It turns red under 60 s of the last boundary's draw.
- `M10: bank on channel N empty` prints once per crossing, both ways.
- `senses: power` (§3.1) lets the player build his own cut-off. The engine builds none.

### 4.2 Items: the pocket as a façade

**What the inventory is under it.** The pocket is today's header `VolumetricInventory`: unbounded,
in voxels, 46 non-test references. **It keeps being the one object every caller holds.** It gains
a `Link`:
- While the equipped pack is a window whose channel has a holding vault, `pocket.Link` is that
  vault's stock (another `VolumetricInventory`) with a route predicate.
- The predicate is **placeable, not a machine, and not `unit: count`**: the mine's output, meaning
  blocks.
- **`Add`** routes blocks to the vault up to its room. The rest stays local, so nothing is ever
  lost or refused.
- **`Take`** spends the vault first, then the pocket.
- **`Voxels`, `Entries` and `MaterialInSlot`** return the merged view.
- **The header saves `LocalEntries()` only.** Saving the merged view would duplicate the vault
  into the pocket at the next open (see Must not ship).

So `Carve`, `Place`, the HUD, the drain and the undo are all unchanged: every caller already
passes that object. **No reference swaps, no pour at link, and no transfer term.** Blocks mined
while linked go to the vault, at risk. What the pocket held before stays safe in the pocket.
TajMahal's 113 M voxels of stone never touch a vault.

**Machines and parts stay in the pocket** (the route predicate). That is what removes the dump's
hardest case: a heap cannot write a computer, and a partial pump body is destroyed at the next
reconcile. Revisit trigger: he wants machines banked, which needs the dump to place whole bodies
through `Place.Machine`.

**Link changes clear the undo stack** (Game, one call). An undo spanning a link change would debit
the store that did not take the credit.

**Water is not a material**, so it never enters the pocket. It lives only on vault rows (§5.4).

### 4.3 Why a container has a gameplay reason

The pocket is unbounded and weightless, so the vault's reason cannot be room for the hand. **In this
pass it has three reasons, all engine-real:**
1. **The quarry needs a sink that is not your pocket.** A placed machine working while your hands
   are busy credits the channel's vault, and stalls `NoRoom` without one.
2. **Pumps need a sink that is not the surface.** His 2026-09-19 mounds (*"the water is building up
   on top of them"*) end in a vault that swallows water, until the bank fails and it comes back.
3. **The account puts that output in your hand anywhere**: the quarry's stone on your hotbar
   2 km away, and the pond you siphoned poured into a pit.

**The honest gap:** routing your own hand-mined blocks into the vault buys nothing while the pocket
is unbounded. The vault's full reason arrives when a survival mode bounds the pocket, which is the
recorded trigger (inventory-screen decision 5). The creative demo keeps the pocket unbounded.

---

## 5. The container ladder and the quantum vault

### 5.1 The ladder

**One kind, `container`, for every rung.** Plain containers (hand to AC: crate, bins, vault) are the
same kind with `holdWatts: 0`, no fuse, no dump, and capacity equal to their interior.
**This design ships none of them.** Two reasons:
- with an unbounded pocket they have no reason to exist;
- the only way to open a stock below quantum is M10g's inventory screen (Reader B trap 10: the
  computer is fabricated at tier 4).

They wait for survival's pocket bound and M10g. When they come, the kind already serves them.

### 5.2 The quantum vault

| Property | Value |
|---|---|
| Size | One 1 m brush cell, 64 voxels, placed by `Place.Machine` (his *"about the size of the current machine blocks"*). |
| Capacity | `capacityLitres`, from 1 ML to 10 ML across grades (§6). One number covers blocks at 15,625 mL a voxel and water in mL. |
| Capacity ceiling | **10 ML (640,000 cells) is the CPU-honest top.** The load test ran 3.4 M active cells at 0.52–0.80 Hz (measured, M10-LADDER-PLAN :2866). A god vault of 100 ML would dump twice the load test. Ladder decision 21's planned `stockVoxels ≤ 65,536` is amended to this. |
| One per channel | A second vault on the channel is inert, stall `Duplicate`, and the panel names the first. The channel *is* the account. |
| Hold bill | `holdWatts × fill ÷ capacity × boundaryMs`. An empty vault costs nothing and cannot fuse. |
| Paying | All or nothing, at its priority in the consumer list, from `_available[ch]`. A new PowerExact term, `heldByVaults`, is printed. |
| Fuse | An unpaid boundary starts or continues it, and a paid one resets it. It counts boundary milliseconds, so a late boundary is a slower fuse, never a wrong one. |

**Why a fuse and not a cell inside the vault.** His 2026-09-25 words put a pack inside the vault. A
cell inside means a second joule store with its own entering/leaving and recharge, and a charge the
hand cannot reach. The fuse gives the same grace (*"an older cell gives a shorter hold"*: the grade
sets `fuseSeconds`) with no joule term. Revisit trigger: he wants to see or feed the vault's own
cell.

### 5.3 The dump

**State machine:** Holding → Fuse → Dumping → Holding (empty).

**Dumping is terminal until empty.** Power returning does not stop it: the entanglement has
collapsed. A bounded fixpoint needs a dead state, and this is it. While dumping, the vault is not a
store: the link drops and the pocket takes new blocks.

**Blocks first: the heap.**
- At the dump's start, collect the air, dry, loaded 1 m cells in a box of radius R cells around
  the vault. R starts at ⌈∛(cells to write)⌉ + 2 and grows to 32 at most.
- Sort by (Y ascending, then distance). A heap fills the room from the floor.
- Each boundary writes up to `dumpVoxelsPerSecond × 0.5` voxels, a whole cell of one material at a
  time, materials in ID order, through `SetVoxel`.
- Recording is off, and `EditHistory.Forget(chunk)` runs for each chunk written. The sort cost
  (a (2R+1)³ box, 36 K cells at R 16) is paid once.
- **Blocks written with no support may fall and land as rubble**, and rubble credits nothing.
  Bottom-up order makes that rare. It is printed through the director's own identity.

**Then litres.**
- If the vault's mouth (the cell above it) is `InLake`, or unloaded on analytic ground inside the
  watershed window, the litres go to that basin through a `WaterSystem` twin counter. That is the
  outlet's unloaded rule (`MachineTick.cs:1088-1110`).
- Otherwise they go to Tier B through **the relay pour's ball** (`Main.PourPending`), offered up to
  `dumpLitresPerSecond × 0.5` a boundary, which is Ctrl+R's 1 ML, a size he has played.

**Unloaded chunks.**
- A cell in an unloaded chunk is skipped. With nothing writable, the dump stalls `Unloaded`.
- The vault's remaining contents *are* the owed amount. There is no pending counter and no
  in-flight term.
- It resumes at the first boundary after arrival: you walk up and it dumps on you.
- With no room within R 32, it stalls `NoRoom` and keeps its contents, printed.

**Destroyed by a fall while holding.**
- The drain turns the row into a **wreck**, a flag, instead of removing it.
- Its remaining body voxels clear to air, as today. The reconcile skips wrecks.
- It keeps dumping from its cell and is removed when empty. **Contents never leave with a row.**

**A pick is refused.** `IMachineBodies` gains `Refuses(voxel, material)`, asked in `Carve.Take`
only for machine voxels, one call. A non-empty vault reads as too hard to every tool and to the
quarry. **The undo refuses to remove a non-empty vault** (`UndoOutcome.Refused`). So contents can
leave a vault by only three routes: take, dump, or the §5.4 doors.

**Contents' entering and leaving:**
- They enter at restore and at an undo's re-add.
- Leaving is provably 0 by the three rules above, and printed anyway as a denominator.

### 5.4 Water (taken 8)

**The named amendment.** For the quantum tier only, this reverses:
- ladder decision 6's bucket;
- decision 13's tank on the row;
- M10-PLAN D5 (*"nothing is ever held by a machine"*) and D10 (*"no in-flight term"*);
- M10-NOTES piece 3's machine buffer.

It is justified by his 2026-09-29 words, and each door is a term.

| Door | What | Counter, and where it lands |
|---|---|---|
| Pump → vault | A pump's channel vault with `water = 1` is taken first, ahead of the outlet ring. When full, the ring takes over. | `vaulted`. The per-boundary machine identity becomes **`pumped == emitted + vaulted`**. |
| Siphon, left click | Takes the water in the bite at the crosshair into the linked vault, at the next boundary. The intake rule is the pump's: Tier A whole litres `InLake`, otherwise Tier B cells. | Tier B `Siphoned` (a twin of `Remove`); Tier A `SiphonedLitres` |
| Siphon, right click | Pours up to the bite's volume from the vault through the relay ball, owed as a *request*, never as litres. | Tier B `Released` (a twin of `Emit`); Tier A `ReleasedLitres` |
| Dump | §5.3 | the same `Released` twins |

**The store line**, one new printed identity, in mL:
`vault water == entering − leaving + vaulted + siphoned − poured − dumped`.

**The seams, both ends, checked:**
- `siphoned == Siphoned(B) + 1000 × SiphonedLitres(A)`
- `poured + dumped == Released(B) + 1000 × ReleasedLitres(A)`

**Tier B.** `IdentityExact` gains `− siphoned + released`, in both printed strings. Going through
`Add` or `Emit` instead would misfile the door as `injected`, or break `pumped == emitted` (Reader C
trap 7).

**Tier A.** Its own identity is documented and never evaluated. **These counters are printed and
seam-checked, and this design does not claim more.**

**Payment.**
- The siphon pays `joulesPerLitre × litres moved` from the window at the boundary. Paying for what
  moved is the tool's rule.
- A pump into a vault pays its J/L, as today.

**The siphon without a linked vault takes nothing**, as a quantum tool with no pack does today.

### 5.5 The stock UI

**No new screen.**
- The hotbar shows the façade's merged view.
- The HUD adds `VAULT ch 0 — 1,204 of 2,500 m³ · 6,410 m³ water · hold 4.8 kW`, red with `FUSE 7 s`
  while one burns.
- The panel's status line gets a container branch.
- `M10:` gains a `store:` term.

A list of what a vault holds is M10g's inventory screen.

---

## 6. Grades

**Where a grade lives: in the ID, one axis, now.**
- Each grade is its own JSON file: `quantum_vault_low` … `_god`, names his.
- All five share one texture layer, which the loader allows. That is one C# colour entry.
- **Cost:** 5 permanent IDs for the vault family. The support cost table grows 512 B per ID.
- **No engine code.**

**What a grade changes** (all numbers are estimates for his tuning):

| Grade | Capacity | holdWatts full | Fuse |
|---|---|---|---|
| low | 1 ML | 5 kW | 5 s |
| medium | 2.5 ML | 10 kW | 10 s |
| high | 5 ML | 15 kW | 20 s |
| mythical | 7.5 ML | 18 kW | 40 s |
| god | 10 ML | 20 kW | 80 s |

**The second axis** (*"your battery tech maybe 19th century but your actual emitter/entangler could
be a higher tech variation"*) is a grade per *part*, and parts do not exist until M10g. Reader E's
table leaves two homes:
- product IDs, 25 per family;
- a per-stack annotation in `CarriedPacks`' shape, which is a header version.

The choice belongs to M10g's pass. **A grade on the row alone is wrong**, because a dismantle loses
it (Reader E §7).

---

## 7. The automated quarry

**What it digs.**
- A box hung under its body: `width × length` cells centred on it, from one cell below it down
  `depth` cells.
- **The body's own column is excluded.** The quarry stands on a 1 m pillar in its pit, and M9's
  vertical support holds any pillar at full budget.
- The cursor goes top-down, x then z. It is persisted as an int32 in `KindState`, 9 bytes with the
  version and a pass count.
- **It wraps**, so a later pass picks up fall-ins and rubble.
- Bounds: 16 × 16 × 64 cells, 1 M voxels.

**How it bites.** Reuse, with no Carve change:
`Carve.Cube(world, materials, origin, threshold, into: vault.Stock, bodies: null, budget)`.
- The budget is `min(_available[ch] ÷ J/voxel, vault room)`.
- **A cube whose cell holds a machine row is skipped** (`MachineSet.TryGet`, O(1)). Passing
  `bodies: null` over a machine voxel would destroy it.
- Scans stop after 64 empty cells a boundary.

**The price.** 50 J a voxel, the tool's, so a cube is 3.2 kJ and a quarry at 2 bites/s is a 6.4 kW
load (arithmetic). Paid after the carve for `Removed`, like the tool.

**Where the spoil goes.** The channel's vault, directly. There is no pocket fallback, because a
remote machine has no pocket. **Rubble credits as rubble.**

**What stops it:**
- `Unpowered`: the pool is dry.
- `NoRoom`: no vault, a full vault, a duplicate, or a dumping vault.
- `Unloaded`: the bite's chunk is unloaded. **This means it digs only within the loaded box** (r6h,
  about 48 m). Rows tick unloaded and grid reads stall, per ladder decision 4. A keep-loaded policy
  was rejected, and this design does not reopen it.
- `Signalled`: the collapse sensor.
- `enabled 0`.
- `Exhausted`: a full pass took nothing.

These are new `PumpStall` values. Renaming the enum is cosmetic and waits.

**Water.** It digs through wet ground, and the pit fills. Under a lake, the pit feeds the keystone:
Tier A head weakens the rock, and the roof comes down. A vault dump into Tier B does not saturate
rock until it becomes a lake level (Reader E trap 9).

**The collapse.** The quarry undermines itself and its neighbours. Caught by a fall, it is recalled
(extra 2) or destroyed.

**The undo.** Recording is off for its writes, and `Forget(chunk)` drops player steps that touched
a chunk it bit. Otherwise Ctrl+Z restores a placed voxel the quarry already banked, and mints it.

**CPU per boundary:**

| Item | Figure |
|---|---|
| A global budget | `--quarrybites=` 4 bites a boundary across all quarries, round-robin. A rate budget, not a machine cap: quarries get slower, never wrong. |
| Main thread | ≤ 4 × 64 voxels × 1.5 µs ≈ **0.4 ms**. The 1.5 µs is **M9c-1b's measured Debug figure** through the hook chain, taken before the 2026-09-28 Core fix, so it is pessimistic. |
| Scan | ≤ 64 cells × 64 voxels × 3.2 ns ≈ 13 µs a quarry (the voxel read is **measured**, bench `--pump`). |
| Pipeline | 8 cube bites a second of remesh and relight work, about **2× a held Q** (4.2 bites/s **measured**, TODO). |

Owed: a Release `--quarry` bench arm at 1, 8 and 32 quarries before the budget's default is
believed.

**Pushback.** It cannot out-dig the hand. A chunk bite a boundary is 14.4 ms Release through the
edit hook (measured, WORKLOG :4295). Its worth is that it works while you don't.
- **M10-PLAN D3's trigger** (*"a drill carving … every half second reads as stuttering"*) fires on
  it: one cube pops each 0.5 s. **Keep the boundary rate and ask his eye.** The lever is a second
  rate, and it is not built.

---

## 8. Materials

**What the ideal machines are built from: nothing yet, and this pass writes no bill into JSON.**
- There are no recipes, no stations and no parts (station is refused, M10g).
- A `madeOf` key is decoration that nothing reads, which is the format trap. Disallow would refuse
  it anyway.
- Creative grants the machines. The bills are M10g's, written against its recipe loader.

**What the quarry yields: what the world holds.** That is grass, dirt, stone, clay you placed, and
rubble. **No ore ships in this pass.**

An ore field is its own pass (M10h). Its costs:
- a second lattice field (an estimate of 1.5–2× generation, Reader E);
- a texture entry per ore in C#;
- three hard-counted tests;
- **it moves every fixed-seed oracle**, unless it follows the relief precedent: a header setting,
  off in the record.

The cheapest honest opening, **not recommended now**: gate the ore field on the v8 mode byte, so
creative worlds get ore and every measurement stays mode 0. Revisit trigger: his first quarry play
reads *"it only digs stone"* as dull.

Also note: a collapse turns ore to rubble (`CollapseDirector.cs:939-943`), so a quarry that drops
its roof loses its ore.

---

## 9. The creative-mode demo

### What a mode is

**In the save:** header v8 adds `Mode : byte`.
- 0 is unset: every world today and every measurement.
- 1 is creative. Survival will be 2.
- It is set only at creation, by `--creative`, and warned and ignored on an existing world, like
  `--seed`.
- `--creative` with `--profile` is refused.
- M10g's hotbar assignment takes v9.

**In code, creative does three things:**
1. At creation it grants `mods/overburden/kits/creative.json`:
   - 4,096 cells of each block;
   - 1 vacuum cell, 8 solar panels, 2 of each vault grade, 4 quarries, 4 pumps and outlets;
   - the computer and the quantum pack, equipped on channel 0.

   These are real counts, so the undo works (Reader D trap 8). They are printed once as `granted`
   on the `store:` line. **Not `--stock=`, and not `P`**: both are unprinted doors.
2. The HUD tag `CREATIVE`.
3. Nothing else. Fly, reach and the gate are unchanged, because the pool is what the demo shows.
   **`--nogate` is not creative power** (it zeroes generation), and Alt+P stays the printed bank
   top-up.

### The first five minutes

1. **0:00.** `start.cmd --world=Demo --creative`. The player walks on grass. `BANK ch 0 — 0 J`, and
   the hotbar holds the kit.
2. **0:40.** Place the cell and the panels, then Alt+P: `BANK 5 GJ`. Place a medium vault on
   channel 0. The HUD adds the vault line. The hotbar looks the same (the façade).
3. **1:30.** T to the excavator and take a chunk: the bank drops 1.64 MJ, and the stone lands in the
   vault (`VAULT 512 m³`, `hold 2.0 kW`).
4. **2:30.** Place two quarries over a hill. Cubes pop every half second, the vault climbs, and the
   bank line shows the draw.
5. **3:30.** The siphon drinks a basin, then pours it into the quarry pit. A pump on channel 0 with
   the vault's `water = 1` hides the rest.
6. **4:15.** The computer ◀ ▶ to the cell, `enabled = 0`, and the bank is empty. The vault goes red:
   `FUSE 10 s`. Turn it back on and it is saved. Or don't: stone rises around the vault from the
   floor up, then 2.5 ML floods the pit. *"Swim or mine to get it back."*
7. **5:00.** F12: the pins show a dumping vault and stalled quarries.

### What it must not break

- **Survival:** it doesn't exist. Mode 0 is unchanged, and mode 2 is reserved.
- **The ladder:** the lead-acid pack is untouched, and lower media only gain declarations.
- **The oracles:**
  - No default changes, and the priority defaults are equal.
  - New materials that worldgen does not place do not move r6 (clay and glass shipped with the
    control to the digit). The r6 control `2601 / 325 / 3446 at 411` is every slice's gate.
  - The machine identity's new term is 0 with no vault.
  - The three hard-counted content tests are updated by hand, with their counts stated.
- **Persistence:**
  - v8 migrates additively.
  - Vault and quarry state rides `KindState` in the existing machine table, one transaction with
    the header (`WorldBatch`).
  - **One risk class stays open:** a heap written into a chunk that unloads and saves before the
    next autosave, followed by a crash, duplicates. It is the same class as mining's (Reader C
    §2.3), and it is named, not fixed.

---

## 10. The split

Each slice is a session or less. Core comes before wire. **Collapse f-1 and f-2 land first**, in
parallel, and nothing here waits on anything else.

| Slice | Contents | Test or oracle |
|---|---|---|
| **IM-1** Core | `MachineExtra`, the `extras` key, the `extras.json` section and its merge, Disallow on every DTO; f-1's reader moved to `sense`; the window (`IChannelPool`, `MachineTick.Draw`, `AvailableMillijoules`) | Every shipped file loads under Disallow. The reader set equals f-1's. **PowerExact holds over 1,000 random bites and boundaries.** Mutation: drop `TakenMillijoules += x`, and the identity must go red. The save writes own charge 0. |
| **IM-2** Wire | `quantum_pack.json`; v8 `Mode`; `--creative`; `kits/*.json`; the HUD bank line, the warning and the crossings | r6 to the digit. A headless `--creative --entangle --holdq` probe on a new world bankrupts ch 0, reads `power: exact` and bank drop == `packTaken`. **Playable: the bank.** |
| **IM-3** Core | the `container` kind; `VaultStore` and a strict `VaultState` codec; live stores in `MachineSet`, encoded at `Snapshot`, with no clamp at `Restore`; one per channel; bill and fuse (vaults pay first until IM-6); the pocket façade and `LocalEntries`; `Refuses`; the undo refusal; store counters | The fuse burns at exactly `fuseSeconds` unpaid. The bill is all or nothing. Codec round trip; an unknown version kept opaque, never read as empty; over capacity kept and counted. The façade routes blocks and not machines, and takes vault first. Mutation: save the merged view, and the reopen test must catch the duplication. |
| **IM-4** Core | the dump: the state machine, the sorted box, the rate, recording off with `Forget`, unloaded waits, wrecks, the per-boundary `debited == written` | A fixture room receives every voxel exactly. A half-unloaded box finishes at arrival. A destroyed vault keeps dumping. Ctrl+Z cannot touch the heap. |
| **IM-5** Wire | vault JSON × 5 grades, a texture entry, the HUD store line, the panel branch, the dump at Main's boundary, the `store:` line | Headless: `--placeat=` (f-2's door) a vault, fill it, `--port=enabled=0` on the cell, read fuse → heap → `store: exact`. **Playable: the one store.** |
| **IM-6** Core | the `quarry` kind; the box, cursor and wrap; `Carve.Cube` into the vault; the machine skip; `--quarrybites`; the consumer list sorted by priority | Digs a fixture box exactly once, top down. Pays exactly `Removed × J`. Never bites a machine. **Equal priorities reproduce origin order** (every M10 oracle). A low priority starves first. Release `--quarry` bench arm. |
| **IM-7** Wire | quarry JSON, texture, panel, `M10:` quarry terms | Headless quarry run all `exact`; r6. **Playable: quarries.** |
| **IM-8** Core+wire | litres: the amendment in DESIGN and the ladder first; the `water` port; `pumped == emitted + vaulted`; Tier B and A twins; the water dump | The M8b oracle is unchanged with no vault. Mutation: route the dump through `Add`, and the seam must go red. |
| **IM-9** Core+wire | the siphon: `takes: water`, `joulesPerLitre`, a boundary request queue, the ToolTiers order (water tools last) | The siphon then the pour of a fixture basin restores `held` exactly. **Playable: water in the inventory, and the flood.** |
| **IM-10** Core+wire | recall (the director's `recalled`, the drain bracket), remote ◀ ▶, telemetry pin state, `senses: power` | Collapse oracle `released == landed + in flight + lost + recalled`. A recalled pump is 64 in the pocket and 0 rubble of it. Ctrl+Z after a recall refuses. |
| **IM-11** | his demo play, the full five minutes | — |

**Plays: after IM-2, IM-5, IM-7, IM-9 and IM-10.** Tests owed: about 40, plus four mutation
batteries (estimate).

---

## 11. Numbered decisions

1. **Extras are a closed C# flag set, opted into with `extras`.**
   - Cost: one enum, six definition bytes, one reading site each.
   - Revisit: M12 scripting wants a mod to define an extra.
2. **Inheritance is `extras.json`'s medium → form map; lower media are declared and inert.**
   - Cost: a loader section (about 100 lines and tests); a table that does nothing below quantum
     until M10e.
   - Revisit: M10e lands and a form needs per-machine data.
3. **Refuse unknown keys on every DTO.**
   - Cost: plan-only keys refuse, and a mod with a typo fails loudly.
   - Revisit: a mod ecosystem needs forward-compatible files. Then warn, never ignore.
4. **Extra 4 at quantum is the account: the pack is a window onto its channel's pool and vault.**
   - Cost: the pack holds nothing, so a dead bank is a dead tool.
   - Revisit: he wants a carried buffer, which is two pools again.
5. **The window debits rows directly between boundaries, in reverse origin order; no new
   PowerExact term.**
   - Cost: the hand is always first in line.
   - Revisit: a play where the hand starving a vault reads as unfair. Then the tool joins the
     consumer list and is owed to the boundary.
6. **Bankruptcy: no floor. A HUD warning, printed crossings, and `senses: power` as the player's
   own cut-off.**
   - Cost: a first-time player can lose a vault.
   - Revisit: none. It is his rule.
7. **One kind `container` for the ladder; only the quantum grades ship.**
   - Cost: the lower rungs are absent.
   - Revisit: survival bounds the pocket, or M10g's screen lands.
8. **One vault per channel.**
   - Cost: 16 accounts at most; no capacity by stacking.
   - Revisit: he places a second vault to grow one account. Then stores union in origin order.
9. **The pocket is a façade; the vault holds blocks only; machines and parts stay local; no pour at
   link; a link change clears the undo.**
   - Cost: the vault's own reason is thin while the pocket is unbounded.
   - Revisit: he wants machines banked, or survival arrives.
10. **Vault state is a live object encoded at save; the codec is strict; restore never clamps.**
    - Cost: the row's bytes are stale between saves.
    - Revisit: a second reader of `KindState` appears.
11. **The hold bill scales with fill, is paid all or nothing, and an unpaid bill lights a fuse by
    grade.**
    - Cost: no internal cell, against his 09-25 image.
    - Revisit: he asks for the vault's own cell.
12. **The dump is terminal until empty: the heap bottom-up, then litres by the relay ball;
    unloaded waits.**
    - Cost: a 10 ML heap takes about 5 minutes to write at 1,024 voxels a boundary (arithmetic).
      Unsupported blocks may become rubble.
    - Revisit: his look at the heap.
13. **A non-empty vault refuses the pick and the undo; a fallen one becomes a dumping wreck.**
    - Cost: two small refusals and a flag.
    - Revisit: none foreseen.
14. **Capacity is capped at 10 ML.**
    - Cost: "god grade" means a long fuse, not a bigger heap.
    - Revisit: water bodies' part a rest lands and the load test's cost falls.
15. **Water doors: pump→vault, siphon take and pour, dump; twin counters on both tiers;
    `pumped == emitted + vaulted`.**
    - Cost: four counters, two printed lines, one amended identity.
    - Revisit: none. It is his rule, taken 8.
16. **The siphon is one tool: left takes, right pours, at the boundary, paid per litre, needing a
    linked vault.**
    - Cost: a new tool key and a request queue.
    - Revisit: he wants water on the hotbar as an item.
17. **Grades are IDs, one axis, now; the second axis waits for parts.**
    - Cost: 5 permanent IDs per family.
    - Revisit: M10g's pass.
18. **The quarry is a box under its body with its column excluded, a wrapping cursor, and
    `Carve.Cube` into the vault; machine cells are skipped.**
    - Cost: it digs only within the loaded box.
    - Revisit: he wants remote digging. That reopens keep-loaded, which was rejected.
19. **A global `--quarrybites=` 4.**
    - Cost: many quarries share one rate.
    - Revisit: the Release bench says 16 costs under 1 ms.
20. **Quarry and dump writes are the world's: recording off, with `Forget(chunk)`.**
    - Cost: a player step near a quarry is dropped.
    - Revisit: he misses an undo next to a quarry.
21. **Priority is one consumer list sorted by (priority, origin); the shipped pumps are equal.**
    - Cost: a refactor of `Run()`'s loop.
    - Revisit: none. It is taken 1.
22. **Recall is pieces mode only: the director's `recalled` term, a credit of 64 − Carved, and the
    drain bracketed.**
    - Cost: the director learns a material predicate.
    - Revisit: the sweep mode returns as a default.
23. **The remote panel is ◀ ▶ in the existing panel.**
    - Cost: no list, no search.
    - Revisit: more than about 30 machines on a channel in play.
24. **Telemetry is a state byte on `MapPin`; pins stay un-gated.**
    - Cost: none measurable.
    - Revisit: a legend is wanted.
25. **Creative is header v8 `Mode`, set at creation, plus a JSON kit, and nothing else.**
    - Cost: a version, and a migration test.
    - Revisit: the first survival rule.
26. **No ore, and no bill of materials in JSON, this pass.**
    - Cost: the quarry yields stone.
    - Revisit: his first quarry play.

---

## 12. Must not ship

1. **Saving the window's `AvailableMillijoules` into `PackSave`.** At the next open it mints the
   whole bank as `packInitial`.
2. **Saving the façade's merged entries into the header.** It duplicates the vault into the pocket.
3. **Routing the dump or siphon through `FluidSimulation.Add` or `WaterSystem.Emit`.** It reads as
   `injected`, or breaks `pumped == emitted`.
4. **A `VaultState` that reads an unknown version as empty, or a restore that clamps contents.**
   That is silent volume loss.
5. **Tier B writes off the boundary.** Siphon clicks and pours are *requests*.
6. **The quarry passing `bodies: null` over a machine cell**, which destroys it. Skip machine cells
   first.
7. **Any machine world-write (quarry, heap) with recording on, or without `Forget`.** Ctrl+Z then
   credits the heap, or restores a banked voxel.
8. **The drain's destroy branch journalled.** With Recall, the undo duplicates the machine.
9. **The vault as an `onsignal` reader.** That is deliberate flooding (collapse f decision 10).
10. **Unequal shipped pump priorities.** They move every M10 oracle.
11. **A new kind without an explicit panel branch.** The status if-chain falls through to pump text.
12. **Reading "power failed" off one row's charge.** Per-row charge is a write-back artefact; the
    bill's payment is the fact.
13. **`--nogate`, `P` or `--stock=` as creative's source.** None of them is printed.
14. **A creative default leaking into a plain run.** Mode 0 must be bit-identical, and r6 is every
    slice's gate.

---

## 13. My design's three weakest points

1. **The vault's reason is thin until survival.** With an unbounded pocket, routing hand-mined
   blocks into a vault adds risk and no benefit. The quarry, water and the siphon carry the demo.
   Keeping machines and parts out of the vault is my cost-saving cut, and it is a partial reading
   of *"one store"*, not the whole one.
2. **The heap is crude and every cost of it is estimated.** Bottom-up in a box is not a pile. A
   10 ML heap takes minutes. Unsupported blocks can fall and turn to rubble. The 1.5 µs a voxel is
   a Debug figure from before the Core fix. The unload-path crash window stays open.
3. **The quarry's cost and feel are unmeasured.** The global bite budget may make quarries feel
   sluggish. It digs only inside the loaded box, which is not quite *"works while you are away"*.
   And D3's stutter trigger fires on it by construction.
