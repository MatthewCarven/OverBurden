# Reader C: inventory, persistence and the conserved quantities

Read-only map for the ideal-machines panel, 2026-09-29, against `main` at `71e6eed`. Paths are
relative to the repo root. **BUILT** means it is in the code. **PLAN** means it is only in a doc.
**INFERENCE** means I read it off the code and did not run it. Line numbers are from today's tree.

---

## 0. Five things that change the brief

1. **Constraint 4 is stale. The single-transaction overload is built.** M10a-1 (2026-09-15) added
   `WorldBatch` (`Overburden.Core/Persistence/WorldBatch.cs:30-77`). The header, the basins and the
   machine table ride one SQLite transaction with the dirty chunks
   (`Overburden.Persistence/SqliteWorldStore.cs:254-358`). The ladder's own M10b pass already says
   so: *"a-1's `WorldBatch` … is built. The transaction exists; the thing to transfer into does
   not"* (`docs/M10-LADDER-PLAN.md:1504-1507`). The brief quotes the older ladder paragraph
   (`M10-LADDER-PLAN.md:86-91`). What is and is not atomic today is in §2.3.
2. **Water is never in the inventory.** The hotbar's `1,767,903,281 L` is **stone**
   (`WORKLOG.md:7224`: *"stone 1,767,903,281 L (3,453 chunks' worth)"*). The ladder **rejected**
   both "water in the inventory (a bucket)" (`M10-LADDER-PLAN.md:379-381`) and "a tank on the row"
   (`M10-LADDER-PLAN.md:454-456`, decision 13). There is no water material
   (`Overburden.Game/mods/overburden/materials/` has none). A vault that holds litres overturns two
   recorded rejections. M10-NOTES (2026-09-25, later than the ladder) sketches the vault's litres
   as terms in both tiers (`docs/M10-NOTES.md:357-360`). The panel has to amend those two decisions
   out loud, not work around them.
3. **No identity covers voxels in the inventory.** "Every door into a conserved quantity is a
   printed term" holds for litres and joules. For voxels, the only printed identity is the
   collapse director's `released == landed + in flight + lost`. Nothing checks the inventory
   against the world, and several debug doors (`P`, `--stock=`) mint voxels with no term (§3.4).
4. **The machine drain is journalled, so a fall that destroys a machine can leave an undoable
   step (INFERENCE).** The collapse runs with recording off and clears the stack. The drain that
   then destroys the machine runs with recording on, after the clear (§5, §6). Recall would hook
   exactly here.
5. **The joule identity has a term it does not print.** `PackInitialMillijoules` is in
   `PowerExact` (`Overburden.Core/Machines/MachineTick.cs:404-408`). The printed formula
   (`Overburden.Game/Main.cs:8316`, `:8374`) leaves it out.

---

## 1. The inventory

### 1.1 What a stack is

- `VolumetricInventory` (`Overburden.Core/Inventory/VolumetricInventory.cs:47-229`) is a
  `Dictionary<int, long>`: numeric material ID → **voxel count** (`:55`). It has no stacks, no
  slots and no item objects.
- **It is stored in voxels and shown in litres.** `LitresPerVoxel` = 0.25³ m³ × 1000 = **15.625 L**
  (`:52-53`). One 1 m brush cell is 64 voxels, or 1,000 L. The class documents the revisit
  trigger: the first quantity that is not a whole number of voxels (`:27-31`).
- `Add` refuses air and negative counts (`:91-110`). `Take` takes what it can and returns how much
  (`:122-145`). `Entries()` is sorted by ID, and that order is both the save order and the hotbar
  order (`:163-174`). `Reset` is the load path (`:215-225`).
- **Capacity is unbounded, deliberately** (`:34-41`). The recorded trigger to bound it is M10's
  containers or M11's crafting. The inventory-screen plan keeps it unbounded (PLAN,
  `M10-LADDER-PLAN.md:2571-2578`, decision 5: *"a total and no limit"*).
- The class is not thread-safe, and every caller is on the sim thread (`:42-45`).
- **A reusable fact:** `Place.*` and `Carve.*` take a `VolumetricInventory? from` / `into`
  (`Overburden.Core/Building/Place.cs:304-973`, `Overburden.Core/Mining/Carve.cs:117-722`). A second
  instance could serve as a container's stock and plug straight into both verbs.

### 1.2 Units, machines and parts

- A material's `unit` is `litres` (the default), `count` or `cells` (`Overburden.Game/Ui/Hud.cs:440-455`).
  `count` prints `× N`. `cells` prints `× N`, plus a fraction when a body's 64 voxels are not all
  there (`:449-451`).
- **A machine in the inventory is 64 voxels of its material.** No object, no state. `Place.Machine`
  checks for 64 before it writes (`Place.cs:637`). `WriteBody` debits 64 and creates the row
  (`:676-677`). Pump, outlet, solar panel and vacuum cell all declare `"unit": "cells"` (for
  example `mods/overburden/materials/relay_pump.json`).
- **A placed storage body starts empty, and a dismantled one forfeits its charge.** This is M10c
  decision 3 (`M10-LADDER-PLAN.md:1934-1943`). In code, the charge leaves through
  `StoredMillijoulesLeaving` when the row is removed (`Overburden.Core/Machines/MachineSet.cs:212`).
  This is the precedent for what happens today to a container's contents when its row goes.
- **Parts** are `unit: count, placeable: false`: the computer and the lead-acid pack
  (`materials/computer.json`, `lead_acid_pack.json`).
- **`CarriedPacks` is the only case of state carried beside the count**
  (`Overburden.Core/Inventory/CarriedPacks.cs:33-128`). It came from Matthew's *"keep its charge as
  an item please"* (2026-09-18, `:11-15`). The count stays the truth, and a list annotates some
  packs with a charge and a channel. It is saved in header v5, and the reader refuses a header whose
  entries outnumber the count (`Overburden.Core/Persistence/WorldHeader.cs:103-111`, `:541-560`).
  **This is the shape a "container item with contents" would copy.**

### 1.3 Every writer of the inventory

| Writer | Where | Direction |
|---|---|---|
| Mining credits each removed voxel, after the write | `Carve.cs:757-763` | + |
| Placement debits the brush, and 64 for a machine | `Place.cs:1045`, `:676` | − |
| Dismantle by pick credits the remainder | `MachineSet.cs:549-556` | + |
| Reconcile credits 64 when the cell regenerated as rock | `MachineSet.cs:458-467` | + |
| Undo gives the exact inverse, derived from voxel entries | `Overburden.Core/Editing/EditHistory.cs:383-393` | ± |
| Pack equip and stow (one count) | `Main.cs:3179`, `:3207` | ± |
| `P` debug seed: 64 each of 4 machines, 64 cells of clay and of glass, 1 computer, 1 pack | `Main.cs:4653-4677` | + (no term) |
| `--stock=` seeds every material | `Main.cs:5078-5087` | + (no term) |
| `--pump` / `PlaceSurfaceBody` add then place | `Main.cs:4297-4323`, `:4431-4440` | net 0 |
| Load | `Main.cs:1509` | reset |

### 1.4 UI: none for stock

- The hotbar is `Hud.Slots = 9` (`Hud.cs:40-47`). It projects `MaterialInSlot` (`VolumetricInventory.cs:203-212`).
  A tenth material is only reachable with `V`.
- **No inventory screen and no stock UI exist.** A grep for `openinventory`, `HotbarAssignment`
  and `--assign` finds nothing, and `E` is unbound. `MachinePanel` shows ports and the last stall
  only (`Overburden.Game/Ui/MachinePanel.cs:6-20`).
- The inventory screen is a PLAN folded into M10g (`M10-LADDER-PLAN.md:2458-2616`): one `Control`
  on `E` tapped; the hotbar as a nine-slot assignment in the header; the unit scaled to m³.
  **Header version:** the plan says "v6", but the header is now v7 (`WorldHeader.cs:187`), so the
  assignment takes **v8**.
- **Where "no stock" was said:** M10b fact 6 (`M10-LADDER-PLAN.md:1504-1507`) and decision 7
  (`:1581-1587`, *"a gate with nothing behind it"*).
- **What a stock declaration was to look like (PLAN only):** a `stockVoxels` field on the
  `generator` and `station` kinds, e.g. `"kind": "generator", "ratedWatts": 10000, "stockVoxels": 64`
  (`:611`). It is bounded `≤ 65,536` at load and at use, refused and never clamped (`:546`), and
  persisted in the kind blob as *"stock entries with a remainder per output"* (`:701`).
  65,536 voxels is 1,024 m³ (my arithmetic). A vault that is "bigger on the inside" would need a
  larger bound or a different unit.
- **None of it is in code.** `MachineKind` is a closed enum: Pump, Outlet, Converter, Storage
  (`Overburden.Core/Machines/MachineDefinition.cs:13-23`). `Storage` means **joules**. No `stock`
  field exists anywhere in Core.

---

## 2. Persistence

### 2.1 The file (schema 4)

`SqliteWorldStore.cs:476-549`. Every table is `STRICT`:

| Table | Key | Content |
|---|---|---|
| `world` | `key` (`"header"`) | the header blob |
| `chunks` | cx, cy, cz | voxel blob |
| `columns` | cx, cz | sky column |
| `fluid` | cx, cy, cz | Tier B panel |
| `basins` | pit_x, pit_z | `litres` (int) |
| `machines` | ox, oy, oz | `material` (int), `blob` |

- The schema is 4 (`:49`). Tables are `IF NOT EXISTS`, so an older file gains them on open, and a
  newer file is refused (`:482-487`).
- The ladder plans a schema 5 for conductor summaries (`M10-LADDER-PLAN.md:701-703`); it is not built.
- The machines table is **deleted and re-inserted whole** at every session save (`:331-345`).
  The basin table is handled the same way.

### 2.2 The header

`WorldHeader` (`WorldHeader.cs:31-112`) holds:
- the settings;
- the material ID table;
- **`Inventory`** as `(MaterialId, Voxels)` entries, 12 bytes each (`:226-227`);
- `ElapsedMillis`;
- the watershed lattice;
- `Player`;
- `Pack`, the equipped pack (v4);
- `CarriedPacks` (v5).

The version is 7 (`:187`). Additive versions migrate and a meaning change is refused (`:192-225`).
The inventory is validated at write and at read: no air, no ID past the table (`:447-480`). It is
**rebuilt whole on every save** (`Main.cs:9102-9115`, `CurrentHeader`), and the ID table travels
with it on purpose (`:9099-9101`).

### 2.3 Transaction boundaries: what is atomic today

**Atomic (one SQLite transaction):**
- **Autosave**, every 30 s (`Main.cs:1318`, `:4113-4153`): `Session()` (`:4165-4175`) builds the
  header, the banked basins and `MachineSet.Snapshot()`. `SaveAll(session)` puts them on the same
  batch as every dirty loaded chunk, column and fluid panel (`Overburden.Core/Streaming/ChunkPipeline.cs:2867-2970`).
- **Quit** does the same (`Main.cs:9448-9468`). If the fluid tick still owns the grid,
  `WriteSession` writes the session **alone** and the chunks' last interval is lost
  (`ChunkPipeline.cs:2982-3016`).
- A contract test pins the rollback: `AWriteThatFailsOnItsMachinesLeavesTheHeaderBasinsAndChunksUnchanged`
  (`Overburden.Core.Tests/WorldStoreContractTests.cs:617`).

So **an inventory ↔ container transfer whose two halves are the header and a machine row is
already atomic at the next save.** No new overload is needed.

**Not atomic with the session:**
- **The unload path.** A chunk leaving the box is written by `SubmitSaves` as a batch with
  **no session** (`ChunkPipeline.cs:2643-2658`). A chunk's voxels can reach disk up to 30 s before
  the inventory credit or debit that goes with them. The existing guard is specific to machine
  bodies: at arrival, a body voxel with no row is cleared without credit, "the header's debit was
  never written either" (`MachineSet.cs:501-526`). **Ordinary material in a dumped heap has no
  such reconcile.** A heap written at unload, followed by a crash before the vault row's next save,
  would exist on disk with the vault still full (INFERENCE).
- **The header at open** is written alone (`Main.cs:1518`), before any chunk.
- **Collapse pieces in flight** are not persisted. They land only at quit (`Main.cs:9424-9430`,
  `LandAll`). An autosave mid-flight saves the released cells as air with no piece.
- **The undo stack** lives in RAM and is not persisted (UNDO-PLAN decision 2).

### 2.4 How a machine row is stored and versioned

- **`MachineRowBlob` v1** (`Overburden.Core/Persistence/MachineRowBlob.cs:28-189`): `BlobFormat`'s
  10-byte frame, the origin as a key check, facing (1 byte), port count (1 byte), each port as a
  name and an int64, then `KindState` as a length-prefixed byte string (`:36-81`).
  The reader refuses any trailing bytes (`:172-176`).
- **`KindState` carries its own version byte** (`Overburden.Core/Machines/MachineRow.cs:20-25`, `:88`),
  so a kind can add fields with no blob bump and no schema bump.
- An unknown kind's state is carried opaque. A removed mod's row sits inert and keeps its bytes
  (`MachineSet.cs:670-674`, `MachineTick.cs:750-755`).
- Today's kind states are `StorageState` (version 1, then an int64 in mJ) and `ConverterState`
  (version 1, then an openness byte) (`Overburden.Core/Machines/KindState.cs:21-111`).
- **The kind helpers are lenient where the row reader is strict.** `StorageState.Read` returns
  **0** for an empty state or a version it does not know (`KindState.cs:29-40`). For a vault, that
  leniency would read unknown contents as empty, silently.
- **At restore, a value over capacity is clamped and counted** (`MachineSet.cs:704-716`), following
  the ladder's "a row is a caller" rule. For a vault's contents, a clamp destroys volume. The
  panel needs a different rule here, or a term.

### 2.5 What a container's contents would cost in a row

- Contents as counts per material cost about 12 bytes per material (my estimate, from
  `InventoryEntryBytes`, `WorldHeader.cs:226-227`). A vault holding 30 materials is under 0.5 KB.
  Litres as one int64 is 8 bytes. Both are cheap.
- **What scales is the rewrite.** Every row is snapshotted, serialised, zlib-tried
  (`MachineRowBlob.cs:67-70`) and re-inserted at every autosave (`MachineSet.cs:602-619`,
  `SqliteWorldStore.cs:331-345`). The ladder's premise is that rows are hundreds, not millions
  (`IWorldStore.cs:127-134`).
- Measured autosaves are **17–100 ms, dominated by fluid panels at about 0.5 ms each**
  (`WORKLOG.md:5418`, `:4543`, `:3052`). There is no measured machine-table figure, so any number
  there is an estimate.
- Storing a heap's **shape** (voxel positions) instead of counts would be the first row whose
  size grows with play.

---

## 3. The conserved quantities and their printed identities

The periodic block prints every 5 s (`Main.cs:8723-8807`): `M6c:` (inventory, no identity),
`M7d:` (Tier A), `M8b:` (Tier B), `M9:`, and `M10:` (machines and power).

### 3.1 Litres, Tier B: the `M8b:` line

**Evaluated in Core:** `FluidPublished.IdentityExact` (`Overburden.Core/Fluid/FluidSimulation.Job.cs:404`):
`held == Added + Loaded + Emitted − Removed − Displaced − Handed − Evicted − Pumped`.

**Printed** at `Main.cs:7797` (M8b) and `:8070` (profile):
`held == injected + rained + drawn + seeped + loaded + emitted − removed − displaced − handed − evicted − pumped`.

| Term | Counter and door | Where |
|---|---|---|
| injected | `Added` minus the three below. Main derives it: `p.Added − drawn − seeped − rained` | `Main.cs:7784` |
| rained | `FluidSimulation.Add` through `LandRainCell` | `Main.cs:7572-7584` |
| drawn | portal drain → `Add` | `Overburden.Core/Fluid/PortalDrain.cs:118` |
| seeped | seep drain → `Add` | `Overburden.Core/Fluid/SeepDrain.cs:392` |
| loaded | a panel restored from disk | `FluidSimulation.cs:1261` |
| emitted | `Emit`, the machine outlet | `Overburden.Core/Fluid/FluidSimulation.Collapse.cs:143-167` |
| removed | `Remove`, the relay take (Alt+R / Alt+G) | `FluidSimulation.cs:447-453` |
| displaced | the automaton (`:1144`) plus `DisplaceLifted` | `FluidSimulation.Collapse.cs:88-93` |
| handed | `HandOff`, the B → A seam | `FluidSimulation.cs:466-471`, `SurfaceHandoff.cs:229` |
| evicted | a panel leaving RAM with its chunk | `FluidSimulation.Eviction.cs:162` |
| pumped | `Pump` | `FluidSimulation.Collapse.cs:126-133` |

- **A second line:** `lifted == returned + lift-displaced` (`FluidSimulation.Job.cs:401`), used by
  falls and by displacing fills (`FluidSimulation.Collapse.cs:50-93`).
- **Two gaps in the printing:**
  - `Added` bundles four doors, and the split exists only in Main's string. A new door through
    `Add` reads as "injected" unless Main subtracts it the way it subtracts `rained`.
  - The profile's line (`Main.cs:8065`) subtracts drawn and seeped but not rained, so rain sits
    inside "injected" there, and its formula omits "rained".
- **The relay's pending pour is not a term.** It is printed as "still to pour", because litres not
  yet delivered do not exist (`Main.cs:718-726`).

### 3.2 Litres, Tier A: the `M7d:` line

- Counters: `SpilledLitres`, `DrawnLitres`, `SeepedLitres`, `ReceivedLitres`, `PumpedLitres`,
  `EmittedLitres`, `InjectedLitres`, `SeedOverflowLitres` (`Overburden.Core/Hydrology/WaterSystem.cs:153-267`).
- **Tier A's own identity is documented and never evaluated or printed.**
  `held == rained + injected + received + emitted − spilled − drawn − seeped − pumped` appears in a
  comment only (`WaterSystem.cs:179-181`). `WaterSummary` (`Main.cs:5642-5690`) prints the counters
  with no rained counter and no exact/NO. A grep finds no `rained` counter anywhere.
- **The seams are checked from both ends:**
  - drawn A == drawn B (`Main.cs:7998`);
  - seeped A == seeped B (`:8014`);
  - received == handed (`:7722-7732`).
- `SeedOverflowLitres` is water leaving at a window move. It is printed, but is not a term of
  anything.

### 3.3 The machine layer, and joules

- **`pumped == emitted` across the tiers** (`MachineTick.cs:462-464`). It is also checked per
  boundary: `report.Exact` raises a `PushError` (`Main.cs:4263-4266`). It is printed at `:8291` and
  `:8351`. **A vault fed by a pump breaks this line unless a "vaulted" term joins it.**
- **The pool** (`MachineTick.cs:404-408`):
  `stored + pack == entering − leaving + generated − drawnByPumps − clamped + packInitial + packDebug − packTaken − packStowed`.
  - A break raises a `PushError` (`Main.cs:4268-4272`).
  - It is printed as *"pool == entering − leaving + generated − drawn − clamped − stowed"*
    (`Main.cs:8316`, `:8374`). **`packInitial` is not printed.** `packTaken` is printed as
    "drawn … by the tool".
  - **`entering` and `leaving` bundle several doors** (`MachineSet.cs:131-134`, `:192`, `:212`,
    `:151-166`, `:651-654`): placement (always 0 J), restore at open, an undo re-adding a row, the
    `Alt+P` debug fill through `SetCharge`, dismantle, destroy by a fall (forfeited), and a restore
    replacing a live set.
  - Rows switched off still count in `stored` (`MachineTick.cs:765-777`).

### 3.4 Voxels

- **The only printed voxel identity:** `released == landed + in flight + lost` for collapse pieces
  (`Overburden.Core/Structure/CollapseDirector.cs:77-79`, printed `Main.cs:6197-6201`).
  A **lost** piece is one whose rest cell is unloaded. Its voxels vanish (`CollapseDirector.cs:1079-1084`).
- `PlaceResult` partitions candidates into buckets (`Place.cs:74`). It is a partition, not a
  conservation.
- **Doors into or out of the world's voxels with no identity:**
  - `P` and `--stock=` (above);
  - the debug lamp, which writes with no inventory (`Overburden.Game/Player/MiningController.cs:596-602`);
  - `--bitechunks`, which writes air with no credit (`Main.cs:6304`);
  - the courtyard, which writes stone from nothing (`:7157`, `:7170`);
  - the breach (`:7288`);
  - a fall, which turns every material it moves into rubble (COLLAPSE-PLAN decision 8, `docs/COLLAPSE-PLAN.md:171-172`);
  - a destroyed machine's remainder, cleared without credit (`MachineSet.cs:557-561`);
  - "returned at arrival", which credits 64 voxels for a body that is no longer in the world (`:458-467`).
- **A vault holding voxels has no identity to join.** It would need its own:
  `held == loaded in − taken out − dumped`, with the dump printed beside the director's `lost`.

### 3.5 Where a vault's terms would slot (the existing precedents)

- **Tier B dump.** Add a twin on `FluidSimulation` with its own counter: the `Emit`, `Return` and
  `HandOff` shape, *"each with its own counter and nothing else"* (`FluidSimulation.Collapse.cs:100-104`;
  M10a decision 6, `M10-LADDER-PLAN.md:975`). It then goes into `IdentityExact` (Core) and both
  printed strings. Going through `Add` instead would make it "injected" unless Main subtracts it.
- **Tier A dump.** `WaterSystem.Emit` (`WaterSystem.cs:549-561`) counts into `EmittedLitres`, which
  the machine layer's cross-tier line reads. A dump there breaks `pumped == emitted`. The dump wants
  its own counter, in the shape of `Inject` (`:564-575`). A dump into a full basin spills out of the
  world, and that is counted in `SpilledLitres`.
- **The hold's bill.** `DrawnByPumpsMillijoules` means pumps only. A vault's draw needs a new
  `PowerExact` term, printed. The brief's "power-low" warning would read the same pool.
- **The contents entering and leaving with the row.** `StoredMillijoulesEntering` and `Leaving` are
  the joule precedent (`MachineSet.cs:127-134`). Litres and voxels in a row would need the same
  pair on the tiers and on the voxel identity. Without it, placing, dismantling, restoring or
  destroying a vault moves contents off the books.

---

## 4. Putting water and voxels into the world by code

### 4.1 Water

- **Tier B.** `Add`, `Emit` and `Return` all call `RefuseMidTick`, so they run only at the boundary
  (`FluidSimulation.cs:420-444`, `FluidSimulation.Collapse.cs:63-86`, `:143-167`).
  - Each returns what did not fit.
  - `CanHold` is **false for an unloaded voxel** (`Overburden.Core/Fluid/IFluidTerrain.cs:212-221`).
  - **The relay pour is the dump's template.** Litres are owed in `_relayPendingMillilitres`
    (`Main.cs:726`) and offered to a ball every boundary until they fit (`PourPending`, `:7587-7622`).
    The ball's radius is sized to the litres, and `LakeCeiling.Cover` runs before the pour (`:7606`).
  - Scale: M10-NOTES prices a 10 ML dump at about 640,000 cells, *"the load test's scale"*
    (`M10-NOTES.md:372-376`). At 15.625 L a cell that is 640,000 exactly.
- **Tier A.** `WaterSystem.Emit`, `Inject` and `Receive` go into a basin by its sample (`:511-575`).
- **The unloaded-outlet rule** (`MachineTick.cs:1088-1110`) is the precedent for a vault nobody is
  standing near. An outlet on the analytic ground inside the watershed window goes to the basin
  under it. One underground and unloaded takes nothing (`unloaded`). One off the window takes
  nothing (`unanalysed`).

### 4.2 Voxels as a heap

- **The brush** (`Place.*`) writes from a `VolumetricInventory`. Its refusals are occupied,
  unreachable (unloaded), inside the player, wet, and out of stock (`Place.cs:1006-1031`). A wet
  fill is deferred to the boundary and displaces its water (`FillWater`).
- **Collapse pieces:**
  - `Release` reads each voxel's material, writes air and launches the piece (`CollapseDirector.cs:740-783`).
  - `LandingSolver` searches every orientation and steer within 2 m and takes the lowest rest
    (`Overburden.Core/Structure/LandingSolver.cs:39-90`). **Only a descending placement counts**,
    judged by ΣY against where the piece was cut. An unloaded voxel is solid, and reserved rests are
    solid. With no descending placement the answer is null and the piece waits.
  - `TryLand` (`CollapseDirector.cs:883-948`): a rest cell that is unloaded **loses** the piece
    (counted); a solid one **re-plots**; a wet one waits for the boundary, then lifts the water,
    writes rubble and returns the water "up to the room" (`:950-1010`).
  - **It always lands as rubble.** The material is lost (`:941`; COLLAPSE-PLAN decision 8).
- **The sweep** (`--collapsemode=sweep`) drops column runs and writes rubble (`Overburden.Core/Structure/Collapse.cs:283-330`).
- **Applying these to a dump (INFERENCE):**
  - A dump is new matter with no "where it was cut". The solver's termination rule needs a start
    anchor to descend from.
  - Reusing the director would land rubble, not the vault's materials, unless it learns to land a
    piece in its own `materials[]`. The piece already carries them for the picture (`VoxelCells`,
    `LandingSolver.cs:21-26`).
  - If the space is **occupied**, the piece re-plots. If it is **unloaded**, the piece is lost,
    printed. If no placement descends, it waits.

---

## 5. The fall, the rubble, and where Recall hooks

### 5.1 What happens today when a collapse takes a machine (BUILT)

Machines are ordinary solid materials to the support field (`supportDistance` 8, for example
`relay_pump.json`). Nothing in `Overburden.Core/Structure/` mentions machines. The chain:

1. The collapse writes air into the body's voxels. The pieces path does this at release
   (`CollapseDirector.cs:767`); the sweep path in its runs (`Collapse.cs:301`). Recording is off
   for both (`Main.cs:5974-5998`, `:6053-6062`).
2. `ChunkPipeline.SetVoxel` fires `_onVoxelChanged` whatever the recording state (`ChunkPipeline.cs:985`).
   That calls `MachineSet.OnVoxelChanged`, which increments `Lost` and marks the row damaged
   (`MachineSet.cs:315-329`, wired at `Main.cs:1446`).
3. `DrainMachines` runs the same frame (`Main.cs:3704`, after `UpdateSupport` at `:3653`).
   `Dismantle` (`MachineSet.cs:533-562`) applies the credit rule `Lost == Carved`: only a pick
   credits, because `Carve.Take` reports what it took through `IMachineBodies.Taken`
   (`Carve.cs:765-771`, `IMachineBodies.cs:5-27`). A fall has `Lost > Carved`, so the row is
   removed first, the remainder is cleared to air, **nothing is credited**, and it counts
   `Destroyed` (`:557-561`). A storage row's charge leaves through `leaving`.
4. The body voxels that fell land elsewhere **as rubble**. Voxels a pick took earlier stay in the
   inventory, so "half a pump after a fall" is possible (`Hud.cs:437-439`).

**At arrival** (`MachineSet.cs:418-527`):
- a whole body is left alone;
- an absent body over air is rebuilt from the row;
- an absent body in a cell that regenerated as rock is **returned with credit of 64**;
- a partial body is **destroyed without credit** ("only a fall leaves a partial body on disk");
- a machine voxel with no row is cleared without credit.

**Superseded plan text:** the ladder's Persistence section still says *"dismantled and credited
unless any of its 64 is rubble"* (`M10-LADDER-PLAN.md:706-711`). M10a decision 4 replaced it
with `Lost == Carved` (`:935`). "Rubble credits nothing" is a stated rule (`COLLAPSE-PLAN.md:171-172`),
and Recall is an exception to it.

### 5.2 Where Recall could hook

- **(a) The drain's credit rule** (`MachineSet.cs:542`, `bool credit = row.Lost == row.Carved`).
  The set has the row, the material table (`_materials`) and the inventory. This keeps *"the fall
  keeps not knowing a machine exists"* (`IMachineBodies.cs:14-15`). **The cost:** by the drain,
  the fallen voxels are already rubble or in flight. Crediting 64 counts them twice. Crediting only
  the cleared remainder is what a pick-dismantle does, and leaves a fraction.
- **(b) At release** (`CollapseDirector.cs:757-768`) and in the sweep's runs (`Collapse.cs:299-302`).
  Body voxels could be taken out of the piece before it falls. That changes the director's identity
  to `released == landed + in flight + lost + recalled`, and it teaches both collapse paths about
  machines, which both avoid today.
- **Contents.** A recalled container's contents go where the vacuum cell's charge goes today:
  forfeited through `leaving`, unless an annotation beside the count (the `CarriedPacks` shape)
  carries them.

### 5.3 Every writer on the two write paths

**Voxels** (all through `ChunkPipeline.SetVoxel`, `ChunkPipeline.cs:926-988`, journalled at `:961`):

| Writer | File:line | Journalled? |
|---|---|---|
| Carve (pick, excavator) | `Carve.cs:756` | yes |
| Place brush / `Place.Machine` (+ rollback) | `Place.cs:1045`, `:660`, `:667` | yes |
| Undo's inverse | `EditHistory.cs:377` | no (recording off, `:347-356`) |
| Machine drain: rebuild, clear, orphan | `MachineSet.cs:570`, `:584`, `:521` | **yes** |
| Collapse sweep | `Collapse.cs:301` | no |
| Director release and land | `CollapseDirector.cs:767`, `:941` | no |
| Debug lamp | `MiningController.cs:602` | yes |
| `--pump` / `PlaceBody` carve | `Main.cs:4454` | yes |
| `--bitechunks` | `Main.cs:6304` | yes |
| Courtyard | `Main.cs:7157`, `:7170` | yes |
| Breach | `Main.cs:7288` | yes |

**Rows** (`MachineSet.Add`/`Remove`, journalled at `MachineSet.cs:194`, `:214`):
- `Place.Machine` → `NotePlaced` (`Place.cs:677`);
- `Restore` at open (`MachineSet.cs:641-728`), which runs before the journal is attached
  (`Main.cs:4193-4197`);
- the drain's reconcile and dismantle (`MachineSet.cs:463`, `:474`, `:546`);
- the undo (`EditHistory.cs:364`, `:401`);
- `--pump`'s unloaded arm, a row alone (`Main.cs:4322`, `:4436`).

**Not journalled at all:** `SetPort` (`MachineSet.cs:245-271`); `SetCharge` (`:151-166`); the tick's
`KindState` writes (`MachineTick.cs:644`, `:722`); every inventory write that is not derived from a
voxel entry.

---

## 6. The undo journal

- **What it journals** (`IEditJournal.cs:26-33`, `EditHistory.cs:87-429`): each voxel's first
  `was` and latest `now`, per chunk; row adds and removes, with a **copy of the row including a
  clone of `KindState`** (`:427-428`). It keeps three steps (`:90`). A step is a frame, closed after
  the drain (`Main.cs:3709`).
- **The inventory reversal is derived only from voxel entries** (`EditHistory.cs:297-327`,
  `:383-393`). Credits and debits that are not voxel writes are invisible to it.
- **What clears the stack:**
  - a fall clears it (`Main.cs:6017-6020`, `:6100-6103`, and `FellSinceLastEdit`);
  - a chunk leaving the box drops the steps that touch it (`Main.cs:1471`).
- **Consequences for the vault (INFERENCE):**
  - **A transfer is not journalled** by construction, since neither half goes through the two
    hooks. That is consistent with the pack's equip.
  - **Undoing a container's dismantle duplicates its contents** if the dismantle credited them to
    the inventory. `Apply` re-adds the row copy with its `KindState` (`:397-404`) and debits only
    the body voxels.
  - **A dump must run with recording off** and should clear the stack, as the collapse does.
    Otherwise Ctrl+Z would write air over the heap and **credit every voxel of it to the inventory**
    (`:383-387`).
  - **The drain gap.** The drain is not inside a recording-off bracket (`Main.cs:4465-4485`). After
    a fall clears the stack, the destroy's `Remove` and `ClearBodyVoxels` land in a **fresh step**,
    and `MachineChanged` resets `FellSinceLastEdit` (`EditHistory.cs:190-200`). Ctrl+Z can then
    re-add the row (and its charge) and restore the remaining body, paying only for the voxels the
    drain cleared.
    - In pieces mode, that piece's landing usually clears the stack again, so the window lasts about
      one flight.
    - In sweep mode it persists.
    - No test covers it (`Overburden.Core.Tests/EditHistoryTests.cs:236-258` has no machine). Recall
      hooked at the drain inherits this gap.

---

## 7. Unloaded chunks

- **Every row stays in `MachineSet` whether or not its chunk is loaded** (`MachineSet.cs:9-22`).
  It is read whole at open and written whole at each save (`IWorldStore.cs:127-134`). The forget
  path touches no machine (`Main.cs:1463-1475`).
- **Every row ticks every boundary.** Only `--nowater` stops the tick, and it needs at least one
  machine (`Main.cs:4247-4250`). What that means per kind:
  - **Storage rows** join their channel's pool with no load check (`BeginPower`, `MachineTick.cs:604-634`).
  - **Converters** use their last-sampled openness when the column is unloaded (`:639-657`,
    `ConverterState`).
  - **The overcast** is world-wide: `Weather.OvercastAt(seed, time)` (`Main.cs:4254-4255`).
  - **Pumps** stall `unloaded` if any chunk of the intake box is unloaded (`MachineTick.cs:905-907`).
  - This is ladder decision 4: row-local inputs tick, and any grid read stalls (`M10-LADDER-PLAN.md:332-345`).
- **So a far-away vault can lose power with nobody near.** A storm that dims its panels is global,
  and its channel's cells can run down. But this only happens while the world is open: the clock
  advances only then (`WorldHeader.cs:61-67`).
- **When unloaded, a dump has nowhere to go.**
  - Tier B refuses, because `CanHold` is false.
  - Tier A takes it only on analytic ground inside the player-centred window.
  - Voxels cannot be written: `SetVoxel` returns false (`ChunkPipeline.cs:930-933`), the solver
    treats unloaded cells as solid, and the director loses them.
  - So the dump must be owed. The nearest precedent is **reconcile at arrival**: `ChunkArrived`
    queues, and the drain writes (`MachineSet.cs:281-309`, `:370-411`).

---

## Traps

1. **"The header and the machine table are two store calls" is false today.** They have been one
   transaction since M10a-1 (§2.3). What is *not* atomic is the unload path's chunk writes against
   the session.
2. **Water is not in the inventory.** `1,767,903,281 L` is stone. A vault holding litres re-opens
   two rejected alternatives: the bucket (ladder decision 6) and the tank on a row (decision 13).
   Amend them out loud.
3. **`MachineKind.Storage` means joules.** A container is a new kind, and a new kind is C#: the
   enum, the loader's string switch, and `Rebuild`'s switch (COLLAPSE-PLAN fact 5, `docs/COLLAPSE-PLAN.md:1087-1091`).
4. **`stockVoxels` is only a plan**, and the planned bound is 65,536 voxels (1,024 m³). "Bigger
   on the inside" breaks that bound.
5. **There is no voxel identity for the inventory.** Do not write "the voxel identity gains a
   term" without first saying the identity has to be built.
6. **Tier A's own identity is never checked.** Only the seams are. Adding a Tier A term to a
   comment is not printing it.
7. **Going through `FluidSimulation.Add` or `WaterSystem.Emit` misfiles the dump.** `Add` reads
   as "injected". `Emit` breaks `pumped == emitted`. Each needs a twin counter.
8. **A kind helper reads an unknown version as empty** (`StorageState.Read` → 0), and a restore
   clamps anything over capacity. Both are silent volume loss for a vault.
9. **Every placement, dismantle, restore, undo re-add and destroy moves a row's `KindState` with
   it.** Contents therefore need an entering/leaving pair in their identity, the way joules have
   (`MachineSet.cs:127-134`).
10. **The undo derives the inventory from voxels only.** A dismantle that credits contents,
    followed by Ctrl+Z, duplicates them. A dump not bracketed recording-off turns into an inventory
    credit.
11. **The drain is journalled** (§6). Recall hooked there needs the bracket or a test.
12. **The collapse lands rubble, never the original material,** and "rubble credits nothing" is a
    rule on the record. Recall is an exception to both. Crediting 64 at the drain double-counts the
    voxels that already fell.
13. **Every voxel door (the landing solver, `SetVoxel`, the brush) refuses or loses at an
    unloaded chunk.** A remote dump needs an owed state and a place to settle it: arrival, or
    Tier A's unloaded-outlet rule.
14. **A dump's heap written at unload can reach disk before the vault row's autosave.** Nothing
    like the machine-body orphan reconcile exists for ordinary material.
15. **The printed pool formula omits `initial`**, and `entering` hides the `Alt+P` debug fill.
    Copying the printed string as the identity copies the omission.
