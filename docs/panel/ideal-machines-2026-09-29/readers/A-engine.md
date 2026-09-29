# Reader A — the machine engine, as it stands

Read-only map of `main` at `71e6eed` (2026-09-29). Every code claim carries `file:line`. "Plan only"
means the words exist in a doc and nothing in the code. Paths are relative to the repo root.

## The short version

- A machine is **a material with a `machine` block, plus a row** (cell, material ID, facing, ports,
  one opaque `KindState` byte string). There is no machine registry of its own.
- **`MachineKind` is a closed C# enum of four**, and behaviour is dispatched by it in about twenty
  places across Core, Game and the panel. The JSON `links` block is **validated at load and never read
  by the tick**. The tick reads the kind, and a port literally named `channel`.
- **Only the quantum tier's membership exists.** A channel is a port value that is regrouped when
  `MachineSet.Version` moves. Hand, steam, DC and AC membership (crank, shaft, copper, cable) are
  plan only. The loader refuses every one of those `via` names today.
- The tick runs once per fluid boundary (2 Hz), after the pour and before the collapse sweep. It
  moves water and joules as ledger transfers with exact identities. Measured cost: 0.6 ms per 100 wet
  pumps in the Release bench, 0.02 ms for 500 dry ones.

---

## 1. The machine model

### 1.1 What a machine is

- **The definition lives inside the material.** `MaterialDefinition.Machine` (`Overburden.Core/Content/MaterialDefinition.cs:117`), `IsMachine` (`:120`). `MaterialTable` projects it per numeric ID. `Machine(id)` (`Overburden.Core/Content/MaterialTable.cs:69`), `IsMachine(id)` is one array read (`:66`), and `MachineIds` is the list for palette checks (`:72`). A block that fails `TryValidate` is not projected, so the body is "a voxel and nothing more" (`MaterialTable.cs:243-246`). M10-PLAN decision 1 rejected a separate `Registry<MachineDefinition>` (`docs/M10-PLAN.md:111-118`).
- **The body is one brush cell**: 1 m, 4 × 4 × 4 = 64 voxels of the machine's material, placed atomically by `Place.Machine` (`Overburden.Core/Building/Place.cs:467-488`). It also has a displacing variant for water (`:505-574`).
- **The row** holds only what the voxel cannot (`Overburden.Core/Machines/MachineRow.cs:6-26`).
  - Persisted: `Cell` (`:70`), `MaterialId` (`:79`), `Facing` (`:82`, "unread by M10a's kinds"), `Ports` as a `SortedDictionary<string,long>` by name (`:29`, `:85`), and `KindState` (`byte[]`, `:88`).
  - Session only, never persisted: `Lost` and `Carved` (damage counters, `:95-98`), `OwedMillilitres` (a slow lake pump's sub-litre want, `:105`), `LastStall` and `LastMovedMillilitres` (written by the tick, read by the panel, `:108-111`).
- **The row blob** (`Overburden.Core/Persistence/MachineRowBlob.cs:14-20`) is v1: facing byte, port count, each port as a name and an int64, then `KindState` length-prefixed and opaque. A row whose material is a hole (a removed mod) still reads and sits inert (`:22-25`).

### 1.2 `MachineDefinition` (per material, from JSON)

`Overburden.Core/Machines/MachineDefinition.cs:83-150`. Fields:

- `Kind` (`:119`)
- `RatedWatts` (`:122`) and `EfficiencyPerMille` (`:125`), for converters
- `CapacityMillijoules` (`:128`), `Carried` (`:131`) and `RechargeWatts` (`:134`), for storage
- `MillilitresPerSecond` (`:137`), `HeadMetres` (`:140`), `JoulesPerLitre` (`:143`) and `IntakeRadiusCells` (`:146`, 1..2), for pumps
- `Links` (`:148`) and `Ports` (`:150`)

Bounds are refused at load, never clamped:

| Constant | Value | Source |
|---|---|---|
| `MaxMillilitresPerSecond` | 54,000 L/s | `:86` |
| `MaxHeadMetres` | 1,024 | `:89` |
| `MaxRatedWatts` | 1 GW | `:108` |
| `MaxCapacityJoules` | 1 TJ | `:111` |
| `MaxJoulesPerLitre` | 1 GJ | `:114` |
| Ports per machine | at most 16 | `PortDefinition.cs:26` |
| Port name length | at most 32 characters, identifier charset | `PortDefinition.cs:29`, `:65-68` |

A port is an integer with a default, a min and a max (`Overburden.Core/Machines/PortDefinition.cs:23-62`). It is clamped at use, never refused (`:10-16`).

`MachineLink(Name, Carries, Role, Via, Face, Port)` is at `MachineDefinition.cs:52-64`. `MachineMedium` is `{Power, Fluid}` (`:26-30`) and `MachineRole` is `{Source, Sink}` (`:33-37`).

### 1.3 Per-kind state: `KindState`

One opaque byte array per row, whose first byte is its own version (`MachineRow.cs:21-25`). There are two codecs, both static classes with no registry (`Overburden.Core/Machines/KindState.cs`):

- **`StorageState`** (`:21-58`) is a version byte of 1 plus an int64 of millijoules, 9 bytes. `Write` **replaces the whole array** if its length is not 9 or its version byte is not 1 (`:48-54`).
- **`ConverterState`** (`:71-111`) is a version byte of 1 plus the open sky columns, 0..16, in 2 bytes. It also replaces the array on a mismatch (`:101-107`).

Which codec reads a row is decided by the material's kind through the table, never by the bytes. `MachineSet.ChargeOf` explains why: another kind's state could also start with a 1 (`MachineSet.cs:136-143`).

### 1.4 `MachineSet`: every row, in memory

`Overburden.Core/Machines/MachineSet.cs`. The rows are read whole at open and written whole at every session save. The class notes say "rows are hundreds, not millions" (`:16-22`).

- **Indexes:** keyed by `BrushCell` (`:42`) and indexed by chunk (`:43`).
- **`Version`** is bumped on every `Add` (`:193`), every `Remove` (`:213`), every `SetPort` that changes a value (`:264-268`), and every `Restore` (`:662`). **This is the only dirty flag membership has.**
- **Counters** feed the `M10:` line: placed, rebuilt, returned at arrival, destroyed at arrival, orphans cleared, dismantled, destroyed, port writes and clamps (`:77-114`).
- **The pool's entering and leaving terms:** `StoredMillijoulesEntering` and `StoredMillijoulesLeaving` (`:131-134`). A storage row's charge enters on `Add` (`:192`) and leaves on `Remove` (`:212`).
- **Storage-only helpers:** `ChargeOf`, `SetCharge` (the debug door) and `CapacityOf` (`:142-170`).
- **`SetPort(cell, name, value)`** is the computer's only write (`:245-271`). It clamps to the declaration and refuses an undeclared name or a material with no ports.
- **The undo is told** through `Journal?.MachineChanged` on `Add` and `Remove` (`:174`, `:194`, `:214`).
- **The hooks queue and mark, never write** (`:24-30`):
  - `ChunkArrived` does an O(machine materials) palette check, then queues the chunk (`:281-309`).
  - `OnVoxelChanged` does two lookups, then `Lost++` and marks the row damaged (`:315-329`).
  - `Taken` is the carve telling it a pick credited a voxel: `Carved++`, then mark (`:332-346`).
- **`Drain`** runs once a frame on the sim thread (`:370-411`). It does two things:
  - It reconciles arrived chunks (`:418-527`). All 64 body voxels present: nothing. All absent and air: the body is rebuilt. All absent and rock: the row is removed and all 64 voxels are credited ("returned"). Partly present: destroyed, no credit. A machine voxel with no row: written to air, no credit.
  - It dismantles damaged rows (`:533-562`), described in section 6.
- **Persistence:** `Snapshot()` in origin order (`:602-619`). `Restore()` drops unknown ports, defaults missing ones, clamps out-of-bounds values and clamps a charge over capacity, counting each. It keeps unknown-kind rows verbatim (`:641-728`).

### 1.5 The carried pack: `EquippedPack` and `CarriedPacks`

- **`EquippedPack`** (`Overburden.Core/Machines/EquippedPack.cs:23-120`) is **not a row**. It has no cell and rides the world header. It has a single `Channel` (`:51`), a charge, and the session's taken and given counters for the identity (`:56-62`).
  - Its constructor demands `Kind == Storage && Carried` (`:29`).
  - Tools pay from it per voxel: `VoxelsAffordable` and `Pay` (`:94-116`).
  - It is "a consumer and never a supply": its charge is not in any channel's pool (`:10-14`).
- **`CarriedPacks`** (`Overburden.Core/Inventory/CarriedPacks.cs:33-128`) annotates the inventory's count of a pack material with the charges of packs that were worn and then stowed (Matthew, 2026-09-18: *"keep its charge as an item please"*, `:13-14`).
  - A zero charge is never stored, so a pack on channel 3 stowed at 0 J comes back on channel 0 (`:21-26`).
  - Equipping takes the fullest (`:28-31`, `:92-116`).
  - **This is the only place an item in the inventory carries state beyond a voxel count.**

### 1.6 The two seams: `IMachineBodies` and `ISolarSky`

- **`IMachineBodies.Taken(voxel, materialId)`** (`Overburden.Core/Machines/IMachineBodies.cs:23-27`) is the one fact that separates a swing from a fall. `Carve.Take` credits and says so (`Overburden.Core/Mining/Carve.cs:118` and following, an optional `bodies` parameter on every shape). The fall credits nothing and never learns a machine exists (`IMachineBodies.cs:10-17`).
- **`ISolarSky.TryOpenColumnsAbove(cell, out open)`** (`Overburden.Core/Machines/ISolarSky.cs:17-21`) counts the 16 columns over a body. It returns false when unloaded, and the row's last sample stands. It is implemented Game-side by `Overburden.Game/Machines/StreamerSolarSky.cs`.

### 1.7 `MachineKind`: confirmed closed, and every place that tests it

`public enum MachineKind : byte { Pump, Outlet, Converter, Storage }` (`MachineDefinition.cs:13-23`).

- The enum's own doc comment is stale. It says "Two in M10a; the rest are reserved — generator, converter, storage, station" (`:5-12`), but converter and storage are built (M10c).
- `Carried` splits `Storage` into two behaviours: a placed cell and a carried pack. In practice there are **five** behaviours.
- M10-PLAN decision 1 says "a new *kind* is M12's scripting" (`docs/M10-PLAN.md:106-110`).

Every non-test site keyed by the kind:

| Site | What it does |
|---|---|
| `Overburden.Core/Content/ContentLoader.cs:582-600` | String switch: `pump`, `outlet`, `converter`, `storage`. Named refusals for `generator` ("M10f") and `station` ("M10g"). Anything else is "not a machine kind". |
| `MachineDefinition.cs:194-199` | `Enum.IsDefined(Kind)` |
| `MachineDefinition.cs:216-229`, `243-256`, `258-276`, `325`, `352-403` | Per-kind validation: a pump needs a rate, a radius and a fluid source; an outlet a fluid sink; a converter power in and out plus a `solar` sink; a storage exactly one power sink. Only a storage may be `carried`. |
| `Overburden.Core/Machines/MachineTick.cs:754-781` | **The dispatch.** `Rebuild`'s switch puts each row in `_pumps`, `_outlets[ch]`, `_converters` or `_storage[ch]`. Any other kind is silently skipped. |
| `MachineTick.cs:664` | `SolarOutputOf` checks the converter kind. |
| `MachineSet.cs:143`, `155`, `170`, `708` | `{ Kind: Storage, Carried: false }`: charge, capacity, the debug fill, and the restore clamp. |
| `EquippedPack.cs:29` | Constructor guard. |
| `Overburden.Game/Ui/MachinePanel.cs:68`, `183`, `232`, `504-547` | The status line is an if-chain on `_kind`. The pump's stall text is the fall-through. |
| `Overburden.Game/Player/PlaceController.cs:366` | `SelectedIsPack` |
| `Overburden.Game/Main.cs:3064`, `3086`, `3173`, `4695`, `9135` | Pack restore and equip; the Alt+P fill. |

The tests use it too: 21 uses across `MachineContentTests` (6), `MachineTickTests` (6), `ShippedContentTests` (5), `MachineSetTests` (3) and `ChunkPipelineTests` (1).

Two more closed sets hang off the kinds:

- **`PumpStall`** (`MachineTick.cs:12-33`): None, NoOutlet, NoWater, NoRoom, Unloaded, Unanalysed, Unpowered.
- **`MachineTickReport`**'s fixed fields (`:40-73`).

The tick also keeps **bespoke per-kind structures**: `_pumps`, `_outlets` rings with cursors, `_converters`, `_storage`, `_available` (`:185-198`). Each kind's code is a different method.

---

## 2. The JSON grammar

### 2.1 The loader

`ContentLoader.LoadMaterials` (`Overburden.Core/Content/ContentLoader.cs:196-348`) reads `mods/<mod>/materials/*.json`. `LoadTools` (`:359-491`) reads `tools/*.json`. The machine block goes through `TryReadMachine` (`:577-743`), then `MachineDefinition.TryValidate`.

The options (`:47-52`) are case-insensitive property names, comments skipped and trailing commas allowed. **No `UnmappedMemberHandling` is set**, and the target is `net8.0`, so System.Text.Json's default applies.

**Unknown keys are silently ignored at every level: material, machine block, link, port and tool.** No error, no warning, and no test covers it (I grepped the content tests). A typo'd or future key loads as if absent.

Unknown **values** are refused with the file named. These are:
- `kind` (`:582-600`)
- `carries` (`:663-670`)
- `role` (`:673-680`)
- a null `via` (`:682-686`)
- `bite` (`:403-415`)
- `unit` (`:552-565`)
- any `via` the build does not serve (`MachineDefinition.cs:410-438`)

One bad file does not stop the game. Errors are collected and that material is skipped (`ContentLoader.cs:6-11`).

### 2.2 Every key accepted

- **Material** (`MaterialJson`, `:785-801`): `id`, `hardness`, `supportDistance`, `supportUnderHead`, `permeability`, `density`, `textureLayer`, `lightEmission`, `opaque`, `fluid`, `replaceable`, `placeable`, `unit` (`litres` | `count` | `cells`), `machine`.
- **Machine block** (`MachineJson`, `:753-767`): `kind`, `litresPerSecond` (whole mL/s), `headMetres`, `joulesPerLitre`, `intakeRadiusCells`, `ratedWatts`, `efficiency` (whole thousandths), `capacityJoules`, `carried`, `rechargeWatts`, `links` (a dictionary by name), `ports` (a dictionary by name).
- **Link** (`MachineLinkJson`, `:769-776`): `carries` (`power` | `fluid`), `role` (`source` | `sink`), `via`, `face`, `port`.
- **Port** (`MachinePortJson`, `:778-783`): `default` (defaults to min), `min`, `max`. Min and max are required.
- **Tool** (`ToolJson`, `:803-817`): `id`, `radiusMetres`, `hardnessThreshold`, `cooldownSeconds`, `bite` (`sphere` | `cube` | `tunnel` | `chunk`), `power: { via, joulesPerVoxel }`.

Other cross-rules:
- A fluid may not carry a machine block (`:288-292`).
- A machine must be placeable unless it is a carried storage, and a carried storage must be `placeable: false` (`:300-315`).

### 2.3 `via` and `carries`

- **Served `via`:** `channel` and `solar` (`ServedVia`, `MachineDefinition.cs:105`).
- **Reserved `via` names** are refused as "an engine rule this build does not serve yet (M10f)": `channel`, `crank`, `burner`, `solar`, `surround`, plus the prefix `adjacent:` (`:96-99`, `:424-428`).
- **Any other identifier-shaped string is a conductor class**, refused as "the conductor index is M10e" (`:436`). So `"via": "dc"`, `"shaft"`, `"pipe"` or `"ac"` all fail to load today.
- **`solar`** must be a power sink on a converter, with no face (`:315-338`). A converter *must* have a solar sink in this build (`:370-376`).
- **`channel`** needs `port` to name a declared port and forbids `face` (`:300-313`).
- **Tool `power.via`** goes through the same `ViaIsServed` (`ContentLoader.cs:454`). Tools pay from the equipped pack, never from a channel directly (`Overburden.Core/Content/ToolDefinition.cs:5-24`).

### 2.4 One shipped machine, verbatim

`Overburden.Game/mods/overburden/materials/relay_pump.json`:

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
    "ports": {
      "channel": { "default": 0, "min": 0, "max": 15 }
    }
  }
}
```

### 2.5 The shipped set

- **`relay_outlet`**: an outlet with ports `channel` 0–15 and `weight` 1–100.
- **`solar_panel`**: a converter at 500 W, efficiency 1.0, with links `sky` (via `solar`) and `out` (via `channel`).
- **`vacuum_cell`**: a placed storage of 5,000,000,000 J (5 GJ) with ports `channel` and `enabled` 0–1.
- **`lead_acid_pack`**: a storage with `carried: true`, 3.6 MJ, `rechargeWatts` 250, `placeable: false`, and a `charge` link via `channel`.
- **`computer`** and **`lamp`** are **not machines**. The computer is `unit: count, placeable: false` with no machine block. The lamp is only `lightEmission: 15`.
- **Tools:** `hand_pick` and `steel_pick` have no power block. `quantum_miner` (cube), `quantum_tunneller` (tunnel) and `quantum_excavator` (chunk) each have `power: { via: channel, joulesPerVoxel: 50 }`.

### 2.6 Parsed, validated, never read

- **`links`**: the only reads of `.Links`, `.Via`, `.Face`, `.Carries` or `.Link()` outside the loader and validator are none. My grep found one hit, a log string at `Main.cs:5016`.
- **A link's `port` name**: the tick reads the port literally named `channel` (`MachineTick.ChannelPort`, `:151`; `ChannelOf`, `:863-864`). A link declaring `"port": "freq"` would validate and then tick on channel 0.
- **`headMetres`** and **`Facing`** are never read by the tick.
- **`enabled`** is read only for placed storage (`MachineTick.cs:773`). A pump that declared it would load, and the tick would ignore it.
- **`weight`** is read only for outlets (`MachineTick.cs:851-852`).

---

## 3. Membership per tier: built against plan

The ladder's one idea is "the five tiers differ only in how a machine is assigned its pool" (`docs/M10-LADDER-PLAN.md:47-60`).

| Tier | Rule (plan) | In code today | Plan only |
|---|---|---|---|
| **Quantum** | a channel number 0–15 | **Built**: `via: channel`, the `channel` port | — |
| **AC** | a cable component, no run bound, 200 kW | nothing; `"via": "ac"` is refused at load | M10e (conductor index) |
| **DC** | a copper-wire component, 24 cells, 10 kW | nothing; the shipped `lead_acid_pack` and `solar_panel` ride `channel` ("wireless for now", `Main.cs:4226-4227`) | M10e, then re-classed in M10i |
| **Steam** | face-adjacent `shaft` voxels, 16 cells, 20 kW; boiler `adjacent:steam_engine` | nothing; `adjacent:` is reserved and refused | M10e/M10f |
| **Hand** | the player holding `E` within reach (`via: crank`, 75 W) | nothing; `crank` is reserved and refused. `TapOrHold` exists for Q and names E's future (`Overburden.Game/Player/TapOrHold.cs:9-13`), but E is unbound | M10f/M10g |

**Quantum membership, exactly.**
- The channel is the row's `channel` port, clamped 0..15 in the tick (`MachineTick.cs:863-864`). The array size is hard-coded: `Channels = 16` (`:154`).
- `Rebuild()` (`:736-809`) walks every row. It puts pumps and converters into lists, outlets and enabled storage into per-channel lists, and sorts everything in origin order.
- It expands each channel's outlets into a weighted ring by smooth weighted round-robin (`:817-844`). That costs O(outlets × Σweight) per channel, and a weight can be up to 100.
- It recomputes the near-field columns that feed `FluidSimulation.SetNearColumns` (`:492-503`; `Main.cs:6717-6720`).
- **When it runs:** only when `MachineSet.Version` has moved, checked at `Run()` (`:517-520`), `RingOn` (`:472`) and `NearColumns` (`:496`).
- **Cost:** O(rows log rows) per add, remove or changed port.
- **No measured figure for `Rebuild` alone exists** that I found. The bench figures in section 4.6 include it once.

**The plan for physical tiers, all estimates, none built** (ladder decision 1, `M10-LADDER-PLAN.md:252-279`):
- A `ConductorTracker` hook only adds a chunk to a dirty set.
- A `ConductorIndex` holds per-chunk segments.
- A `NetworkSet` does union-find, with a component id derived from its minimum position.
- Rescans are capped by `--conductorbakes=` (8) per boundary. A machine on a still-dirty chunk is `pending`.
- The plan's own cost estimates are 0.05–0.5 ms Release per rescanned chunk and ~0.5 ms Release to relabel 10,000 nodes after a cut.
- Membership survives unloading through persisted per-chunk summaries at schema 5 (decision 4, `:332-353`), estimated at ~0.9 KB per wired chunk.
- Run bounds count occupied 1 m cells (decision 3, `:307-330`).
- The M10e row of the split is at `:767`.
- **Grep confirms no `ConductorTracker`, `ConductorIndex`, `NetworkSet` or `conductor` block exists in code.**

**The pool** (ladder decision 9, `:409-413`, built for channels) is the sum of that channel's storage rows' charges plus this boundary's generation. It is never a stored number. Nothing persists per channel. The cursor array is memory-only and reset at open (`MachineTick.cs:117-121`).

**Docking** (ladder decision 15, `:470-478`: "below quantum a carried cell recharges only while docked at a charger") is **plan only**. The shipped pack recharges from its channel from anywhere.

---

## 4. The tick

### 4.1 Where it runs

On the fluid boundary, when `_fluidSinceTick >= _fluidTickSeconds` (`Main.cs:6723-6729`). The default is 0.5 s (`Main.cs:669`). `--fluidhz=` changes it, and the tick is handed the same length (`Main.cs:4212-4214`; `MachineTick.DefaultBoundaryMillis = 500`, `:160`).

The boundary's order is:
1. settle (sim-thread arm only)
2. hand-off, portal drain, seep drain
3. `TakePending`, `PourPending`, `DropRain`, `RunPendingFills`
4. **the `machines` bracket**: `RunPendingMachinePlacements` (deferred wet placements), then `RunMachineTick` (`Main.cs:6798-6806`)
5. `CheckTable` (`:6812-6820`)
6. `SweepCollapse` (`:6826`)
7. the lake ceiling refresh (`:6833-6837`)

DESIGN.md:366 still says machines tick on a "fixed 20 Hz sim step", which is stale.

**`Drain` is not on the boundary.** It runs every frame after `_pipeline.Update` (`Main.cs:3704`). The undo step closes right after it (`:3709`). `RunMachineTick` returns early with zero rows (`:4247`).

### 4.2 What `Run()` does, in order

`MachineTick.cs:513-596`:

1. **Rebuild** if the version moved.
2. **`BeginPower`** (`:604-633`). `_available[ch]` = Σ this boundary's solar output on that channel + Σ `StorageState` of every enabled placed storage row on it. Solar (`:639-682`) is one integer product: `ratedWatts × boundaryMs × open/16 × (256−overcast)/256 × efficiency/1000`. The openness is sampled if the column is loaded and otherwise read from the row. The overcast byte is set once per boundary by Main (`Main.cs:4254-4255`).
3. **Each pump in origin order** (`TickPump`, `:866-1036`):
   1. Its channel. No outlets on it: `NoOutlet`.
   2. Budget = rate × boundary.
   3. The intake. If the mouth (the cell above the body) is `InLake`, draw from Tier A in whole litres, with `OwedMillilitres` carrying the remainder. Otherwise read the box at `IntakeRadiusCells`: up to 8 `IsLoaded` checks (`Unloaded`), then up to 8 `HeldIn` checks (all dry: `NoWater`, no cell read), then 26 (radius 1) or 124 (radius 2) cells of 64 voxels, nearest first.
   4. The bill. `affordable = _available[ch] / joulesPerLitre`, since a J/L is an mJ/mL. Zero: `Unpowered`.
   5. Walk the channel's ring from its cursor, skipping an outlet already reached this boundary. Read each outlet's room (`RoomAt`, `:1044-1117`). A loaded, dry mouth takes free mL in the layer above the outlet's top. A mouth in a lake, or unloaded on the analytic ground, goes to the basin as whole litres. Unloaded underground is `Unloaded`; off the window is `Unanalysed`.
   6. Move exactly `take`: `DrainBox` (`:1191-1228`) or `WaterSystem.Pump`, then `EmitLayer` (`:1231-1268`) or `WaterSystem.Emit`.
   7. After moving, pay `moved × bill` from `_available[ch]` (`:1019-1029`) and advance the cursor.
   8. Write `row.LastStall` and `LastMovedMillilitres` (`:546-547`).
4. **`RechargePack`** (`:685-704`). The equipped pack draws last from its channel, bounded by `rechargeWatts × boundaryMs`, which is 125 J a boundary for the shipped pack (the JSON's 250 W × 0.5 s).
5. **`WriteBack`** (`:710-733`). Each channel's remaining joules are written into its storage rows **in origin order, each filled to capacity**. What doesn't fit is `clamped`. So the per-row charge is a redistribution, not a history: the first cells in origin order fill first.

**Identities.** `Exact` per boundary is `pumped == emitted` across both tiers (`:66`). `CrossTierExact` is the same over the lifetime (`:462-464`). `PowerExact` (`:404-408`) is: stored + pack == entering − leaving + generated − drawn − clamped + pack initial + debug − taken − stowed. Main raises `GD.PushError` on either break, every boundary (`Main.cs:4264-4272`).

**Where the order is load-bearing:**
- **Power priority is origin order.** The first pump in (x, y, z) order spends the channel's joules first (`:536`, `:933-944`). There is no priority port. Extra 5 would replace this ordering.
- The tick throws `InvalidOperationException` if a cell hands back less than the box read, or refuses an emit it had room for (`:1211-1227`, `:1252-1267`). These are wiring asserts.

**Unloaded rows tick.** A converter uses its last sample, and a storage row is row-local. Any grid read stalls, per ladder decision 4 (`M10-LADDER-PLAN.md:340-344`). The set lives whole in memory whether or not its chunks are loaded (`MachineSet.cs:9-22`).

### 4.3 What is printed

**Every boundary: nothing** unless an identity breaks.

**The periodic status block** prints an `M10:` line (`Main.cs:8794-8806`) with:
- rows, placed, deferred, dismantled, destroyed and reconcile counts
- the drain's mean and max
- port writes and computer opens and misses
- `tick:` (`MachineTickSummary`, `:8277-8298`): pumps and outlets, pumped and emitted on each tier, `exact`, stalls by cause, intake voxels read, the tick's mean and max ms
- `power:` (`:8301-8317`): cells and off count, stored, pack, generated, drawn, clamped, stowed, `exact`

The `M8b:` line carries `boundary worst: … machines X ms …` (`:7824`). The profile has `machines`, `machtick` and `power` blocks (`:8320-8375`).

### 4.4 Measured costs

**Build caveat:**
- The Debug build was unoptimised until 2026-09-15.
- `Overburden.Core` was built unoptimised under `start.cmd` until 2026-09-28. `WORKLOG.md:433-476` records a 5.5× difference on water.
- The tick lives in Core, so **every in-game machine figure before 2026-09-28 is suspect as too slow.**
- The bench figures are Release.

| Figure | Conditions | Source |
|---|---|---|
| 0.017 / 0.602 / 2.962 ms mean for 1 / 100 / 500 wet pumps; **0.020 ms for 500 dry** | bench `--pump`, Release, "1.04× the reference" | `M10-LADDER-PLAN.md:1399-1412` |
| Read of 1,664 voxels: 5.3 µs (3.2 ns a voxel); one `HeldIn`: 10 ns | same | `:1406` |
| Pool over 500 charged cells: +0.02–0.11 ms on top of 3.00 ms (500 pumps, powered against free) | bench, Release, two runs | `:2240-2241` |
| `machines 0.15 ms` a boundary, 1 pump | headless game, Debug (Core unoptimised, my inference from the date 2026-09-16) | `:1391-1394` |
| **Tick 1.5 ms mean / 8.7 ms max**: 16 pumps, 20 outlets, 201 panels, 13 cells (250 rows) | his play 2026-09-19, Debug with Core unoptimised (my inference from CLAUDE.md's dates) | `WORKLOG.md:2584` |

**The named lever:** a sleep set, or the machine pass moved onto the fluid job's tail. Its trigger is **`machines` over 1 ms on the played world** (`MachineTick.cs:98-100`; `Main.cs:657-659`; `M10-LADDER-PLAN.md:694-695`, `1428-1429`; the machine cap, `TODO.md:163`). The 09-19 play's 1.5 ms mean crossed it, on an unoptimised Core. I found no record that anyone read the trigger against that figure or re-measured on the fixed build.

**A small unmeasured term:** `PowerExact` iterates every row through `StoredMillijoules` (`MachineTick.cs:384-396`), once per boundary from Main (`Main.cs:4269`). It sits outside the tick's own stopwatch but inside the `machines` bracket.

### 4.5 "A boundary is already the played build's heaviest frame" (brief constraint 7)

The source is the ladder's four facts: "the boundary frame that is already the played build's worst (63.7 ms at slice 3c)" (`M10-LADDER-PLAN.md:93-95`, restated at `:780-781`). The 63.7 ms is the worst frame after slice 3c, played 2026-09-10 (`WORKLOG.md:4443-4449`: field sync 43.9, hand-off 24.7, pour 17.5).

**It has been revised twice since:**
- **2026-09-15**, once Debug was optimised: the claim "shrinks to *the open's first boundary*, and the machine tick lands on a frame that is now ~12 ms mean rather than ~36" (`WORKLOG.md:3158-3161`).
- **2026-09-28**, Core optimised: on his TajMahal copy, the boundary on the main thread fell **79/73 → 20/20 ms** (`WORKLOG.md:468`). The fluid tick itself runs on the worker under `--fluidjob=1`.

So the honest current reading is: the boundary is ~20 ms on the heaviest played world, its worst is the open's first pass, and the machine bracket is priced separately on `boundary worst`.

---

## 5. The panel and the computer

**Picking a machine.**
- `PlaceController.Update` casts a ray every frame at `EffectiveReachMetres` (`Overburden.Game/Player/PlaceController.cs:482-483`).
- With an *item* selected (any non-placeable material that is not a pack, `:335`, `:524-552`), a right-click **edge** on a voxel whose material `IsMachine` and whose cell has a row sets `PanelRequest` (`:532-540`). A click on nothing with a pack equipped opens the pack's panel (`:541-545`). Anything else is a counted miss (`:546-549`).
- There is no check that the item is `overburden:computer`. The HUD reads "COMPUTER (in hand)" (`Overburden.Game/Ui/Hud.cs:539-540`).
- Main opens it: `OpenPanel` (`Main.cs:3269-3300`) or `OpenPackPanel` (`:3219-3236`).

**Reach.**
- `EffectiveReachMetres` is the arm, 5.0 m (`Overburden.Game/Player/PlayerController.cs:50`), **or 512 m when the selected tool tier is at range** (`PlaceController.cs:821-822`; `MiningController.EntangledReachMetres = 512f`, `Overburden.Game/Player/MiningController.cs:194`; `_place.Entangled = tier.AtRange`, `Main.cs:3463`).
- So with a quantum tool tier selected, the computer already opens a machine up to 512 m away **in line of sight through loaded chunks**. This is my reading of the code. No doc records it as played.
- Nothing opens a machine that is out of sight or unloaded.

**What it shows** (`Overburden.Game/Ui/MachinePanel.cs`):
- The title (the label derived from the ID), the position and facing (`:188-190`), and **one `SpinBox` per declared port** (`:200-203`, `:321-358`).
- A status line refreshed every 0.1 s (`:47`, `:304-319`), per kind (`:500-548`):
  - outlet: "receives from every pump on channel N · weight W"
  - storage: charge of capacity and share, or "OFF — holding …" when `enabled` is 0
  - converter: J a boundary, open/16, overcast
  - pump: `LastStall` and `LastMovedMillilitres` as text

**What it sets.** Only ports, through `MachineSet.SetPort` on every change (`:436-458`). The landed value is shown back.
- The pack's panel sets `channel` straight onto the object, clamped 0..15 (`:401-428`). Any other pack port is "ignored".
- **The only on/off field is `enabled`, on the vacuum cell alone.** There are no caps, priorities or stock fields.
- Every change prints `M10: panel — …` (`:445`).

**Headless doors:**
- `--openpanel[=pump|pack]` (`Main.cs:4410-4412`)
- `--panelset=name=value`, which types into the open box (`:3239-3258`; `MachinePanel.TypeInto`, `:270-282`)
- `--port=`, which calls `SetPort` directly (`Main.cs:4346`)

**The panel closes** on Esc, on a click outside (the camera takes the mouse back), or when its row leaves the set (`Main.cs:3303-3345`).

**Remote today:**
- The pack recharges from its channel from anywhere (4.2 step 4).
- F12 already draws **every machine row as a pin** at its x/z in its material's colour (`Main.cs:2959-2972`; `MapPin`, `Overburden.Core/Navigation/MapOverlay.cs:7`, `:65`; MAP-NOTES decision 7, `docs/MAP-NOTES.md:103`). **The pins carry no state**: no stall, no charge, no channel.

---

## 6. What happens to a machine

**Mined by a pick.**
- `Carve.Take` credits each voxel it removes and calls `Taken`, so `Carved++`. The write also fires `OnVoxelChanged`, so `Lost++`.
- At the frame's drain, `Lost == Carved` means it was **dismantled**: the row is removed first, the remaining body voxels are written to air, and they are credited to the inventory (`MachineSet.cs:533-556`).
- **Ports, facing and `KindState` are lost.** The inventory holds voxels of the material, not a row. A dismantled vacuum cell's charge leaves the pool as the `leaving` term (`:212`). Placing it again gives default ports and zero charge. (The pack is the exception, via `CarriedPacks`.)

**Collapsed on (rubble).**
- In the default piece mode, the director writes air over a released piece's voxels (`Overburden.Core/Structure/CollapseDirector.cs:757-768`), then writes `_rubbleId` at the landing (`:939-943`).
- The legacy sweep writes rubble in place (`Overburden.Core/Structure/Collapse.cs:301`).
- Either way the hook sees non-body material, so `Lost > Carved`, and the row is **destroyed, with no credit** (`MachineSet.cs:557-561`). Its remaining body voxels are **written to air** and not left as rubble (`:547`). Its charge leaves as `leaving`.
- **Rubble credits nothing, and the voxels the player mined off it first stay theirs** (`:32-38`).
- **A fall clears the undo stack** (`Main.cs:6014-6021`, `6100-6104`; `Overburden.Core/Editing/EditHistory.cs:231-242`), so a destroyed machine cannot be undone.
- Recall (extra 2) would be a new branch at this drain.
- A credit path with no pick already exists: the reconcile's `ReturnedAtArrival`, `inventory.Add(material, 64)` (`MachineSet.cs:463-466`). It fires when a cell regenerated as rock.

**Undone (Ctrl+Z).**
- `EditHistory` records `MachineChanged(row, added)` with a copy of the row, `KindState` included (`EditHistory.cs:190-200`, `427-428`).
- `Undo` removes the rows the step added, restores the voxels, then re-adds the rows it removed (`:359-404`). A dismantle undone brings the cell back with its charge, through `Add`, so the charge counts as `entering`.
- An undo charges and refunds no energy (`:83-85`). It is refused whole if the inventory cannot pay (`:297-327`). It is not journalled itself (`:343-356`).
- Three steps are kept (`:90`).
- `Forget(chunk)` drops steps whose *voxels* touched a chunk that is leaving (`:248-268`). A machine-only step is not checked.

**Blasted.** No code exists. My grep for TNT or blast in the C# found nothing machine-related.
- TNT-NOTES is notes only: a `charge` kind and a `trigger` detonator as machine rows (`docs/TNT-NOTES.md:17-19`, `50-65`).
- It recommends anything a blast takes is destroyed, not credited (`:40`, `:133`). That is one of his six open decisions.

**Arrival and unload.**
- Rows tick while unloaded. The body follows the row at arrival (the reconcile, above).
- Orphan body voxels with no row are air, with no credit (`MachineSet.cs:501-526`).
- The machine table rides the batch write in one SQLite transaction with the header and the basins (`Overburden.Core/Persistence/IWorldStore.cs:83-105`; `WorldBatch.Machines`/`Header`; built M10a-1, `M10-LADDER-PLAN.md:1197-1201`).

---

## 7. The collapse sensor: COLLAPSE-PLAN Part f (map only)

`docs/COLLAPSE-PLAN.md:1051-1167`. Thirteen decisions, taken 2026-09-29 (`:1063`). **Nothing is built**: my grep for `onsignal`, `ICollapseSense`, `"sensor"` or `placeat` finds nothing, and git has only the planning commits.

**Its facts:**
1. An event exists only once the fuse burns down. Fuses are private scheduler timers, reachable per chunk via `TryGetTimer`.
2. A channel is only an integer port.
3. `enabled` is the player's saved setting, so the signal must be an unsaved override.
4. The tick runs before `SweepCollapse`, so the sensor reads one boundary late.
5. **"A new machine kind is C#, not JSON"**: the closed enum, the loader's string switch, `Rebuild`'s switch, the panel's status. `via` is `channel` or `solar`, and `carries` is power or fluid (`:1087-1091`). The mod rule holds as long as the JSON declares it the way a mod would (`"kind": "sensor"`).
6. There is no headless way to place a machine at a spot.

**What f-1 adds to the machine system** (`:1157`), Core and tests:
- A fifth enum member, the kind `sensor`, with `"senses": "collapse"` and **no `links`** (decision 6). This is a new key on the machine block. It needs a `channel` port and a known `senses` value.
- An `onsignal` port on readers: 0 ignore (default), 1 off while signalled, 2 on only while signalled (decision 4). Readers now are the vacuum cell and the pump (decision 5).
- A per-channel boolean, recomputed each boundary and never stored or written to a port or blob. **A signal change bumps the tick's own rebuild, not `MachineSet.Version`** (decision 4).
- The override inside `MachineTick`. A signalled-off cell leaves the pool as `enabled = 0` does, and still counts in `PowerExact`. A signalled-off pump stalls with a new `PumpStall.Signalled`.
- An `ICollapseSense` interface (`Armed(chunk)`, `Member(chunk)`, `FuseLeft(chunk)`), so the tick never holds the director.
- The radius is in chunks, 3D Chebyshev, 0–4, default 1 (decision 2). The cost is (2r+1)³ × 2 O(1) lookups per sensor per boundary: 250 at r = 2, ~1,460 at the maximum (the plan's estimate). The signal rides the power channel number (decision 3). The sensor is unpowered (decision 8).

**f-2** is the wiring: Main's sense, the panel status, the `M10:` terms, `P` seeding, the texture and `--placeat=`. **f-3** is his play.

**Its own reversal clause:** if "the ideal-machines chat design[s] a general signal medium first", f-1's boolean becomes that medium's first user, and decisions 3 and 6 move (`:1164-1166`).

---

## Traps

1. **"Membership is already computed" is true for quantum only.** Channel grouping is a sort on version change. AC, DC, steam and hand membership have no code at all, only estimates in the plan (M10e/f). An extra inherited "within 24 cells of copper" has nothing to read yet.
2. **Links are decoration today.** The tick dispatches on `Kind` and reads the port literally named `channel` (`MachineTick.cs:863-864`). A new capability declared as a link, or a link naming a different port, validates and then does nothing.
3. **One channel per machine.** A pump's power and water share one `channel` port, and the pack has one `Channel`. The pool arrays are fixed at 16 (`MachineTick.cs:154`, `198`). "Power from channel 3, water to channel 5" or "signal on 3, power on 5" needs new ports and new code.
4. **Unknown JSON keys load silently.** A designer's new `extras`, `inherits`, `grade` or `senses` key will be **ignored**, not rejected, until the loader's DTO learns it (`ContentLoader.cs:47-52`, `753-783`). Unknown *values* for existing keys are refused.
5. **`KindState` has one owner.** `StorageState.Write` and `ConverterState.Write` *replace* the array when its length differs (`KindState.cs:48-54`, `101-107`). A container that is also a storage, or any second piece of state on a row, needs a composed layout, or the next write-back erases it.
6. **Kinds are C#.** About twenty sites key on `MachineKind` (the table in §1.7), plus `PumpStall` and `MachineTickReport`'s fixed fields. The panel's pump status is the if-chain's *fall-through*: an unknown kind would display pump stall text.
7. **Per-row charge is not a history.** The write-back refills storage rows in origin order to capacity every boundary (`MachineTick.cs:710-733`). "This vault holds X J" is an artefact of origin order. A "power fails, vault dumps" rule must read the channel pool, not one row's history.
8. **Power priority already exists implicitly: origin order** (`MachineTick.cs:536`). Extra 5 replaces an order, not a vacuum.
9. **Dismantle loses ports and charge.** The inventory is voxel counts, and only the pack keeps a charge (`CarriedPacks`). A Recall that "returns to the inventory" returns 64 voxels and nothing else, unless something like `CarriedPacks` is added for machines. Charge lost this way is the `leaving` term.
10. **The destroyed body's un-fallen voxels become air, not rubble** (`MachineSet.cs:547`). Any voxel identity for the demo has to account for that, and today it is counted only as `destroyed` rows.
11. **The single transaction exists.** Brief constraint 4 says the header and machine table are two store calls. Since M10a-1 (2026-09-15) `WorldBatch` writes machines, basins and header in one transaction (`IWorldStore.cs:83-105`). What doesn't exist is any inventory-to-row transfer that uses it.
12. **Docking does not exist.** The brief's table says "DC and AC dock only (exists)". The only carried pack is `lead_acid_pack`, and it recharges **wirelessly** over `channel` ("until M10i", `Main.cs:4226-4227`). The carried vacuum cell (500 MB in the plan) does not exist either.
13. **The computer is "any non-placeable, non-pack item."** It already reaches 512 m in line of sight with a quantum tool tier selected (§5). A "remote panel" extends that; it does not start from zero.
14. **F12 already has machine pins** (position and colour only). Map telemetry adds state to existing pins.
15. **`enabled` means storage only.** On a pump it is ignored. f-1's `onsignal` is the first per-pump on/off.
16. **Hooks conform.** `ChunkArrived`, `OnVoxelChanged` and `Taken` only queue and count (`MachineSet.cs:281-346`). All writes are in `Drain`, once a frame, not at the boundary.
17. **Two stale comments.** `Main.cs:4489-4490` claims "the set refuses `Add` and `Remove` while its tick runs", and no such guard exists in `MachineSet.cs:176-216`. The `MachineKind` doc comment lists storage and converter as reserved (`MachineDefinition.cs:5-12`).
18. **Every in-game machine timing predates the 2026-09-28 Core fix.** Quote the Release bench, or re-measure. Don't use the 1.5 ms play figure as the current cost.
