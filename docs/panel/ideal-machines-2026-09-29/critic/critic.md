# Completeness critic: `docs/IDEAL-MACHINES-PLAN.md`

Reviewed against the brief, the three judges, the designs, the reader maps and the code at `71e6eed`.
Nothing was built or run. "Plan:N" is a line of `docs/IDEAL-MACHINES-PLAN.md`; "dN" is the plan's
decision N. Arithmetic is shown where it is mine.

**Severity:** **MUST FIX** before Matthew sees it; **SHOULD FIX**; **NOTE**.

## The must-fix list, first

| # | Finding | Where | Checklist |
|---|---|---|---|
| M1 | The five minutes cannot reach their climax: the sun pays a part-full vault's bill, so the vault never fuses; and the demo's times, water and runway colours are off by its own numbers | d54, d8, d15, d52 | 4, 11 |
| M2 | Three figures are 6.7–10× out. Two of them are in the forks he will read first | fork 31, d24, "the quarry's physics" | 4, 9 |
| M3 | The Tier B bound is per vault, and vaults are unlimited. So the cap does not bound Tier B, and J2's must-not-ship comes back | d31, d32, d13 | 3, 4 |
| M4 | The reconcile removes a vault row that still holds contents, at two sites the plan never lists | d16 | 2, 3 |
| M5 | The kit's `granted` term sits in the store's identity, but the kit goes to the pocket. The kit's 1 GJ has no home | d18, d52, d12 | 3, 11 |
| M6 | IM-5's oracle is a headless creative world, but d50 makes every headless run sandbox | IM-5, d50 | 6, 11 |
| M7 | The hand valve reopens collapse decision 10 without naming it, and quotes his words out of context | d26, fact 7 | 5, 10 |
| M8 | The window's mechanism is not pinned: the backpack's JSON, `RechargePack` and `Alt+P`. As written it builds the two-pool arm he declined (J3's must-not-ship 4-2) | d7 | 3, 12 |
| M9 | The hold bill has no `PowerExact` term | d8, d15, IM-2/3 | 3 |
| M10 | `extras.json` declares lower-tier rows that nothing reads, under a loader d4 and d48 make refuse exactly that. Its media names have no registry, so a typo loads silently (must-not-ship 14) | d3, d4, d48 | 11 |

---

## 1. Per-tier completeness

**1.1 SHOULD FIX: the extras table leaves three cells blank.** Plan:333-340.
- **Remote:** it says "AC over its component (M10e)" and says nothing for DC, steam or hand. The
  brief's table has "AC … perhaps". Write "none below AC", or give the DC form.
- **Charge:** "the dock at DC and AC (M10f, not built)". Steam and hand are silent.
- **Telemetry:** "AC and DC over their component". d40 says pins stay for every tier, so steam and hand
  get a stateless pin. Say that in the table's cell.
- **Fix:** fill every cell with a form, "none", or the part it waits on, so the table is complete.

**1.2 SHOULD FIX: the container ladder has no rungs.** d21, Plan:255-257.
- It names "a crate, a bin or a locker" and does not map them to hand, steam, DC and AC.
- It does not say whether a lower container joins a store over its tier's membership. That membership
  is the one-store property: is there a DC store over copper?
- It does not say whether grades exist below quantum. The brief says an older cell gives a shorter hold.
- It does not say whether a lower container ever holds litres. Taken 8 says quantum only, so write
  "never".
- **Fix:** a five-row table (hand, steam, DC, AC, quantum) × (holds, store view, hold bill, grade,
  litres, waits on).

**1.3 NOTE: the other new quantities get no tier line.** The siphon, water in the store, the item
window and the quarry are quantum-only by taken 8 and the scope, and d60 covers the quarry. Add one
line stating that all four are quantum-only.

---

## 2. Persistence

For every new piece of state: where it is saved, when it is captured, what restore does, and what an
unload, a quit, a crash and an undo do to it.

| State | Home in the plan | Verdict |
|---|---|---|
| Vault contents, water, fuse, phase, owed, wreck | The vault codec in `row.KindState` (d19) | Homed. **But d19 never says the row's bytes are the only copy.** If a builder keeps a live object and encodes it at save, J1's D1-1 comes back through any journal copy made between saves. Fix: *"the codec's bytes are the vault's state; there is no live object."* **SHOULD FIX.** |
| The fuse's random draw | Captured as remaining ms? | Unstated. A re-roll at restore lets quit-and-reopen reshape the fuse. M10-PLAN D5 bans `Random.Shared`. Say the draw is seeded and persisted as ms left. **NOTE.** |
| Quarry cursor, box width / length / depth | **Nowhere** | Unhomed. The box is "set on its panel" (d41), so presumably ports, and the cursor presumably `KindState`. Neither is written down. Across a crash the cursor lags the chunks that unload saved; its air skip makes that harmless, but only if the cursor is persisted at all. **SHOULD FIX:** name the ports and the codec. |
| Priority port | Row port, restore defaults a missing port (`MachineSet.cs:686-691`) | Homed implicitly. Say "a missing `priority` is defaulted, `defaulted` counted". **NOTE.** |
| Mode byte | Header v8 (d50) | Homed. |
| The backpack's equip and channel | `header.Pack` keeps a charge-0 equipped pack (`WorldHeader.cs:412`) | Homed while worn. **A stowed backpack loses its channel**: `CarriedPacks` stores no zero-charge pack (`CarriedPacks.cs:20-26`, "its channel goes with it"), and the window is always zero, so every stow resets it to channel 0. **NOTE:** say so, or annotate the window's channel. |
| The window's charge | Carries 0 (d7) | **Contradicted by the code paths it rides:** `RechargePack` every boundary and `Alt+P`'s `FillPack` (`MachineTick.cs:305-320`). See M8. |
| The store view | Derived | Fine. Say "never saved" (J1's D1 must-not-ship 2). **NOTE.** |
| Recall counters (`Recalled` per row) | A session field like `Lost` and `Carved` | Fine: release and drain happen in the same frame. |
| `decohered`, `granted`, `quarried`, `vaulted`, `siphoned`, `poured` | Session counters | Fine, as every identity counter is. |
| Siphon queue (click → boundary) | **Nowhere, and when joules and store litres move is unstated** | See 3.4. **SHOULD FIX.** |
| Tier A release in progress | Owed in the row (d30, d19) | Homed, and atomic with the basin in `WorldBatch`. |
| Heap site-search progress | In memory | Fine if a restart re-derives it. Say so. **NOTE.** |
| Kit granted once | Header at creation (`Main.cs:1518` writes the header alone at open) | Probably fine for the counts. **The 1 GJ charge has no home**: see M5. |
| The kit's charged cell | "Charged to 1 GJ through the printed `debug` term" (d52) | **Unhomed.** A cell in the pocket is a count with no charge; only a carried pack gets a `CarriedPacks` entry. `PackDebugMillijoules` is the pack's term, and a cell's debug fill goes through `SetCharge` into `entering` (`MachineSet.cs:151-161`). See M5. |

**2.1 MUST FIX (M4): two reconcile branches remove a vault row with contents.** d16 promises
*"Contents never leave the books with a row"* and lists the pick, at-range tools, the quarry, a fall,
the undo and restore. The code removes rows at four sites (`MachineSet.cs:463`, `:474`, `:546`, and
`EditHistory.cs:364`). Two of them are the arrival reconcile, which the plan never mentions:
- **`:458-467` "returned at arrival".** The cell is not air; the chunk regenerated as rock, or the heap
  filled it. It calls `Remove` and credits 64 body voxels to the pocket. A vault's contents vanish with
  no term.
- **A wreck is the dangerous case.** Its body went to air in a fall and was landed as rubble. If the
  dump's own heap then fills its cell ("dry, loaded air cells around the vault", d29, which does not
  exclude its own cell), the next arrival credits a vault body that no longer exists (a **mint**) and
  drops what the vault still owes.
- **`:472-476` "destroyed at arrival".** A partial body on disk: `Remove`, clear, no credit. Contents
  vanish.

The lesson in memory is *"an observer sees every writer; enumerate the writers from the code"*.
- **Fix:** add both reconcile branches to d16. A row holding anything becomes a wreck and is never
  removed or returned. The heap never writes into a machine cell, and never into a wreck's cell.
- Add a test: a vault with contents whose chunk arrives as rock, and one whose chunk arrives with a
  partial body. Contents are conserved, and nothing is credited to the pocket.

**2.2 SHOULD FIX: an opaque vault.** d19 keeps a vault codec of an unknown version *"opaque and
inert"*. An inert vault must also read as non-empty to the pick, the undo, at-range tools and the
reconcile, or its contents leave with the row. Say so in d16.

---

## 3. Identities

### 3.1 The doors, per quantity

**Joules (`PowerExact`, `MachineTick.cs:404-408`)**

| Door | Term in the plan | Verdict |
|---|---|---|
| Tool spend through the window | `packTaken` (d7) | Checked: holds, *if* the rows are written directly. **A builder who uses `MachineSet.SetCharge` double-counts**: it adds the difference to `leaving` (`MachineSet.cs:158-160`) while `packTaken` also rises, so `PowerExact` is off by X. Add to must-not-ship and to IM-2's mutations. **SHOULD FIX.** |
| Vault hold bill | **None named** | **MUST FIX (M9).** `DrawnByPumpsMillijoules` means pumps only (J1 D1-11). Design 1 had `heldByVaults` (`1-engine.md:377`); the synthesis dropped it. Without a term, `PowerExact` breaks every boundary a vault holds anything. Also state whether a partly paid bill takes the partial joules: d27's *"unpaid share"* implies it does. |
| Quarry | `quarried` (d43) | Named. |
| Recall | `recall`, 3.2 kJ a body (d37) | Named. **When it is paid, and from what, is unstated**: at release, which is off the boundary and needs a draw, or at the drain? *"Solvent at the last boundary"* does not guarantee 3.2 kJ at the drain; the hand may have spent it in between. **SHOULD FIX:** *"paid at the drain from the rows directly, like the window; recall only if the rows hold 3.2 kJ × bodies at release."* |
| Siphon, 1,000 J/L | **Unnamed**: `packTaken` at click, or a boundary draw? | **SHOULD FIX.** See 3.4. |
| Kit 1 GJ | *"the printed `debug` term"* | Wrong term for a cell (it is `entering` via `SetCharge`), and the pocket has no home for the charge. See M5. |
| Pumps into a vault | Existing `DrawnByPumps` | Fine. |
| A recalled vacuum cell's charge | `StoredMillijoulesLeaving` (`MachineSet.cs:211`) | Holds, but **a recall of a bank cell forfeits its charge** (a count has no charge). A fall onto your bank recalls empty cells. Say so; it is a gameplay fact he will hit. **NOTE.** |

**Litres**

| Door | Term | Verdict |
|---|---|---|
| Siphon take and pour | `siphoned`, `poured` on Tier B; *"Tier A prints the same three counters"* | **The source is unstated.** Does a take at a Tier A lake surface draw the basin, as design 1's siphon did (`InLake`, `1-engine.md:446`), and does a pour on a lake go to `Receive`? **SHOULD FIX:** say "the pump's intake rule, the outlet's delivery rule". |
| Pump into a vault | `pumped == emitted + vaulted` | Fine. |
| Dump | Tier B `+dumped`; store `−dumped` | **Two meanings of `dumped`.** The store's `dumped` includes Tier A releases, and Tier B's line must carry only Tier B's share. Name them `dumpedA` and `dumpedB`. **SHOULD FIX.** |
| Decoherence | `decohered` on the store's line | Fine for the store. **Unnamed amendment:** M10-PLAN D10's *"the world's water total is a constant of the machine layer"* (`M10-PLAN.md:293`) no longer holds. d23 names D10 only for *"nothing is ever held by a machine"*. **SHOULD FIX:** name both halves. |

**Voxels in the store (d18):**
`held + owed == entering − leaving + deposited − withdrawn + quarried + granted − dumped − decohered`.
- **`granted` is misplaced (M5).**
- **`entering − leaving` is undefined.** Contents never leave with a row, and an undo re-adds an empty
  vault. So only restore enters, and only an emptied wreck leaves, at zero.
- **Scope is undefined.** Is the identity per channel or global? If per channel, `SetPort(channel)` on
  a vault is an entering/leaving pair the plan never lists. **SHOULD FIX:** *"global; SetPort moves
  nothing out of the books."*

**Voxels in the world (the director):**
`released == landed + in flight + lost + recalled`.
- The heap blocks that fall enter as `released`. Fine.
- The quarry and the heap writer are not director doors, and there is no world voxel identity to break.
  Fine.

### 3.2 The failing sequences the coordinator named

| Sequence | Result under the plan as written |
|---|---|
| **Dismantle, then Ctrl+Z** | Safe: emptied before `Remove`, and an empty vault re-added. `OutOfStock` (`EditHistory.cs:21`, checked in the pre-flight at `:305-341`) refuses if the pocket no longer holds the vault body. **But the refusal must happen before the carve:** a pick carves body voxels into the pocket before the drain decides (`MachineSet.cs:542`). A refusal at the drain leaves a holed vault with `Carved` credited. d16 does not say where the refusal happens. **SHOULD FIX:** the non-empty-and-no-room check is a pre-carve body query (the body reads as too hard), and the move to sisters happens at the drain. |
| **A fall onto a full vault** | A wreck. Safe **only if** the reconcile honours the wreck (M4) and the heap never writes into its cell. **In sweep mode** (`--collapsemode=sweep`) the body becomes rubble in place (J1 F4), not air, so *"the body goes to air"* is false there and the wreck dumps *"from its cell"* full of rubble. Say how a wreck dumps from a rubble-filled cell (from its neighbours). **SHOULD FIX.** |
| **A dump into an unloaded chunk, then a crash** | d34 holds it owed, and d35 carries the session. Safe for the heap and the Tier B ball. **d35 names only "the machine layer"**: the siphon's pour is a tool, and it has the same duplication class (store debited, chunk saved with the water at unload, crash, row reverts). **SHOULD FIX:** name every writer: the heap, the dump ball, the quarry, outlets, the siphon's pour. |
| **A save during a fuse** | Fuse ms in the codec. Safe. The random draw: see the table above. |
| **A siphon take, then quit before the boundary** | **Unspecified.** The plan never says whether joules move at the click (window, `packTaken`) and the litres at the boundary. If the pour debits the store at the click and the queue dies with the process, litres are destroyed with no term. **SHOULD FIX:** *"a click only enqueues; joules, store and tier all move in one step at the boundary, where the ownership flag clears"* (memory: *drain deferred where the flag clears*). |
| **A vault moved to another channel with `SetPort`** | Safe structurally: contents ride the row. A fusing vault moved to a solvent channel is disarmed at its next paid boundary; say that this is intended ("power returning during the fuse saves it"). A `DUMPING` or wreck row keeps dumping. **NOTE:** write it down, since J1 found this hole in D4. |
| **A pick on a vault whose sisters are on another channel** | Refused (no sisters). Safe, with the pre-carve caveat above. |
| **Recall when the pocket is "full"** | **Vacuous.** d37 requires *"room in the pocket"*, and fact 4 says the pocket is unbounded. **SHOULD FIX:** delete the clause, or define it as "always true until survival bounds the pocket". Otherwise a builder invents a capacity. |
| **The heap falling as rubble** | Printed by the director. But it turns *"colour is yours"* into black: the player's own blocks become rubble. The fork's other arm (pieces landing in their own materials, a flag) could apply to heap blocks that fall. **NOTE:** offer it. Also, d29's *"bottom-up, nearest first"* is two orders; say which dominates. |
| **Decoherence** | Printed and announced. Fine. Amends M10-PLAN D10 (above). |
| **`--stock=` and `P` in sandbox** | `--stock=` refuses water (d22). **`P` is silent**: does it seed the new machines in sandbox (every world he has)? **`Alt+P` fills the equipped pack** (`MachineTick.cs:305-320`), so a worn backpack acquires a charge the design says it cannot hold. See M8. |

**3.3 MUST FIX (M5): the kit's `granted` has no consistent home.**
- d18 puts `+ granted` in the store's voxel identity.
- d52 grants the kit *"as real counts"*, and d12 says *"placement draws from the pocket only"*. The kit is
  machines and 4,096 cells each of stone, clay and glass, so it must land in the pocket to be placeable.
  The pocket has no identity (fact 4).
- So either the kit's blocks go to the store (then say so, and the player must withdraw before
  placing), or `granted` leaves d18 and is a pocket-side counter.
- The 1 GJ has no home at all: the pocket cannot carry a cell's charge, and *"the printed `debug`
  term"* is the pack's.
- **Fix:** *"the kit's counts go to the pocket (no store term); its first cell is placed by the
  creation step at spawn, charged through `SetCharge`'s `entering`"*, or keep one charged cell as a
  `CarriedPacks`-style annotation.

**3.4 MUST FIX (M8): the window is not pinned, and the code's defaults build two pools.**
- d7 says the backpack *"has no charge of its own"*.
- The brief's taken 4 and J3's must-not-ship 4-2 turn on this being true. Nothing says how the backpack
  is declared, and every existing path would give it a charge:
  - A `storage` must declare `capacityJoules > 0` (`MachineDefinition.cs:258-263`, J1 D1-10). The plan
    never changes that rule.
  - The tick recharges the equipped pack from its channel every boundary (*"the pack recharges last"*,
    `Main.cs:4226`).
  - `Alt+P` fills it through `FillPack`.
- A builder who declares the backpack like `lead_acid_pack.json` gets a pack that the bank tops up
  every boundary. That is the *"separate pool with an automatic top-up"* J3 4-2 says he declined.
- **Fix:**
  - Declare the backpack's JSON in the plan, for example `kind: storage, carried: true, window: true`
    and no capacity, with the validator amended.
  - Make `RechargePack`, `FillPack` and `Give` refuse a window.
  - Add a test: worn backpack, a hundred boundaries, `Alt+P`, save; the pack slot reads 0 every time.

**3.5 MUST FIX (M3): the Tier B bound is per vault, and vaults are unlimited.**
- d31: *"Underground, at most 10 ML of water to Tier B … per dump."* d13: *"There is no limit on
  vaults."*
- A hundred high vaults, each at its own 10 ML, dump 1 GL into Tier B underground, and none of it
  decoheres. That is 64 M cells, 19× the load test's at-open set.
- d32's rate budget spreads it over 10,000 boundaries (83 minutes). It does not bound the total.
- J2's must-not-ship D2-1 (an uncapped Tier B dump) is back through splitting. It also makes the
  physics depend on how the contents were split: one god vault loses 99%, a hundred high vaults lose
  nothing.
- **The surface route is also uncapped in effect.** *"All of it to Tier A"* for a god vault is 1 GL
  into a basin. That is a 100 m rise over a 100 × 100 m lake (arithmetic). A risen basin reaches
  workings through portals at 30 L/s a portal voxel (`PortalDrain.cs:24-26`), so Tier B's load is then
  bounded only by the mine's volume.
- **Fix:**
  - Make the underground cap a global owed-to-Tier-B ceiling across every dumping vault; the excess
    decoheres.
  - Say plainly that a surface dump raises the lake, and the lake then floods workings at portal rates.
    That is the physics, and it is not bounded by d31.
  - Also bound the per-vault site search globally (J2 priced one at R 24 as 35–60 ms), since vaults are
    unlimited.

---

## 4. CPU arithmetic, every figure redone

| Figure | Plan's value | Redone | Verdict |
|---|---|---|---|
| Grade table, inside | 100 → 10⁶ m³, ×10 a step | ✓ | Right. |
| Grade table, water held | 100 kL, 1, 10, 100 ML, 1 GL | ✓ (1 m³ = 1,000 L) | Right. |
| Grade table, bills | 250 W ×4 → 64 kW; stone ÷10 | ✓ | Right. |
| *"A god vault full of water is 128 panels"* | 64 kW / 500 W | 128 ✓ | Right. |
| **Fork 31: *"a god vault holds a thousand times a low one"*** | 1,000× | 10⁶ / 10² = **10,000×** | **MUST FIX (M2).** In the forks he reads first. |
| 51 kW quarry ceiling | 512 × 50 J × 2 /s | 51.2 kW ✓ | Right, and labelled arithmetic. |
| 4 MW siphon on Q | 1,000 L × 1,000 J/L × 4 /s | 4 MW ✓ | Right **given a 0.25 s cooldown the plan never states.** Design 1 set it (`1-engine.md:175`); say so, and label it an estimate. The measured Q hold rate is 4.2/s against a nominal 10 for the miner (TODO.md:111-113), so a held rate is not the nominal rate. **SHOULD FIX.** |
| **d24: *"the gameplay judge put it at 4–8% of a full high vault"*** | 4–8% | J3's figure was for design 2's **8 m³** pour. The plan pours **1 m³** a click: 4 m³/s × 10–20 s = 40–80 m³ of 10,000 m³ = **0.4–0.8%** | **MUST FIX (M2).** Carried from another design's numbers. |
| **"15 L/s … a relay's worth of pumping"** (Plan:405-406) | 15 L/s = a relay | 16 m open box: floor 256 + walls 1,024 = 1,280 m² × 0.02 × 10 m × 0.06 = 15.4 L/s ✓ (`SeepIndex.cs:124`, `:313`), under a uniform 10 m head. **A relay is 100 L/s** (`relay_pump.json`), so 15 L/s is **15% of one**. The head's placement moves it from 9 to 30 L/s. And seepage exists **only under a lake** (`SeepDrain.cs:7-8`): a dry pit seeps nothing. | **MUST FIX (M2)** for "a relay's worth". Also state "under a lake". |
| 1.6 MJ excavator pit | 32,768 × 50 J | 1.64 MJ ✓ | Right. |
| 500 MJ siphon beat | 500 m³ × 1 MJ/m³ | 500 MJ ✓ | The joules are right. **The beat is not**: see 4.1. |
| Dump budgets | 1,024 voxels, 6,400 cells ≈ 100 kL | 6,400 × 15.625 L = 100 kL ✓ | Right, labelled estimates. **Unpriced**: 6,400 cells at G7's ~1.5 µs a cell is ~10 ms a boundary, and 1,024 heap voxels are 1.5–6.7 ms (J2). Together about +12–17 ms on a 20 ms boundary, for as long as a dump runs (below). **No dump bracket is named** (J2 D1-8). **SHOULD FIX:** price it, and give the dump its own term on the `boundary` line. |
| Dump durations | Not stated | Blocks: 10,000 m³ × 64 / 1,024 = 625 boundaries = **5.2 min**. Water: 10 ML / 100 kL = 100 boundaries = **50 s**. | **SHOULD FIX:** state them. A 5-minute heap is his drama moment. |
| *"at radius 4 … about 1,460 a sensor a boundary"* | 9³ × 2 = 1,458 ✓ | | Right (COLLAPSE-PLAN's own estimate). |
| d10's HUD example `612 MJ … −104 kW … runway 1:39` | | 612 MJ / ~104 kW ≈ 5,880 s ≈ 1 h 38 m ✓ if the format is h:mm | **NOTE:** beside thresholds of "10 minutes" and "60 s", `1:39` reads as m:ss. State the format. |

**4.1 MUST FIX (M1): the five minutes (d54) do not add up, and the climax cannot fire.**
- **The water at 1:45.** 500 m³ has to arrive in 45 s. The plan says *"rain or seepage"*.
  - Rain at the shipped maximum is 400 mm/h (`M8-PLAN.md:2590`, 20 mm/h × 20). Over the 8 × 8 m pit
    that is 25.6 m³/h, so about 20 hours.
  - Seepage into an 8 m pit under a lake: 320 m² × 0.02 × ~5 m × 0.06 ≈ 1.9 L/s, so about 73 hours.
  - Only a breach of the lake does it. Portals pass 30 L/s a voxel (`PortalDrain.cs:24-26`), so roughly
    250 portal voxels fill it in about a minute.
  - The beat must say *"dig into the lake's side"*, or it will not happen.
- **The siphon at 1:45–2:45.** 500 m³ at 4 m³/s is **125 s**, against a 60 s window.
- **"The runway turns red while Q is held".** At 1 GJ and a net −4 MW the runway is 250 s = 4:10, which
  is **amber** (under 10 minutes). Red needs a bank under 240 MJ.
- **The greedy fork at 4:30.**
  - The bank is still about 480 MJ after the first job, the quarry and two pumps. A second siphon job
    has to drain 480 m³ at 4 m³/s, which is 2 minutes of held Q. That ends near 6:30, not inside five
    minutes.
  - **Even at zero, the vault never fuses.** d8 pays the hold before consumers, and d15 bills by fill.
    The demo's high vault holds about 500 m³ of water (5% → 200 W) and about 700 m³ of spoil (7% → 28 W),
    so roughly 230 W. Eight panels give 4 kW in sun and about 0.5 kW in a storm (J3 2-7). Either pays the
    bill.
  - There is no night: *"day and night"* is in ladder decision 22's *not in the ladder*
    (`M10-LADDER-PLAN.md:550-556`).
  - So a bankrupt channel's vault is paid by the sun every boundary, and *"the vault glitches cyan …
    the lake rises"* is unreachable. The only fuse in the demo is the hand valve.
- **J3's defect 1-6** named exactly this for design 1 (*"with the cell off, solar still feeds
  `_available`, and the vault is paid first"*). The synthesis carried it over.
- **Fix, choosing one or more:**
  - Recompute the five minutes.
  - Cut the kit's panels (or grant them for a later lesson).
  - Start the demo vault part-full of water, so its bill exceeds storm sun: above about 12% of a high
    vault's water.
  - State that the greedy climax needs a storm and a vault over X% water.
  - Also say that sun-paid holds make the fuse a storm event. That is a real gameplay fact, and he
    should decide whether he likes it.

**4.2 SHOULD FIX: the quarry fork's fallback has the wrong budget.**
- d44 keeps the budget at *"one 2 m bite, 512 voxels"* in both arms.
- In the 1 m arm, 512 voxels is 8 cubes a boundary: about 8 × 0.42 ms = 3.4 ms, Debug and pre-fix
  (G3). That is worse than the 2 m arm.
- J2's rule was *"≤ 256 voxels … the 1 m cube fits; take the 2 m bite only if a measured budget allows
  ≥ 512"*, and *"default the budget to 2 until the bench reads"*.
- Every 2 m bite also exceeds the 64-edit seep burst, so it rescans the whole chunk at 0.56–1 ms under a
  lake (G5). The plan does not mention it.
- The criterion *"a 2 m cut reads under a millisecond"* does not say whether that is the mean or the
  max. G3 reads 1.3 mean and 7.1 max.
- **Fix:** give each arm its own budget, and name mean or max.

**4.3 SHOULD FIX: IM-0 does not measure what d31 and d32 say it sets.**
- d31 and d32 say the 10,000 m³ and 1,024-voxel caps are *"set from IM-0's measurement"*. IM-0
  (Plan:486) has no heap-writer or site-search run. J2's owed list #5 was *"a heap-writer arm, voxels a
  boundary against ms, plus the site search at R 24 and R 32"*.
- J2 #6 (*sensors × radius against `machines`*) is also missing.
- **The IM-0 recipe will not load TajMahal or dig**:
  - it has no `--world=<copy>` and no `--at=-268,-5,1284` (CLAUDE.md: *"a measurement does not open the
    played world"*; *"where a probe must stand"*);
  - it has no `--nogate`, and without it the quantum tools take nothing with no pack. The recorded run
    was `--entangletunnel --nogate --pitch=-60 --holdq` (`WORKLOG.md:1311`).
  - `--entangletunnel` is also missing from `docs/COMMANDS.md`.
- **Fix:** write IM-0's commands verbatim, and add the heap and sensor arms.

**4.4 NOTE: a per-boundary cost is labelled zero.** The extras table prices remote at *"zero;
refreshed at the boundary while open"*. That is O(rows on the channel) of label writes a boundary while
open (J2 D2-6). Write that instead.

**4.5 SHOULD FIX: d35's cost is unpriced.** *"A chunk the machine layer wrote carries the session when
it unloads"* means a header, basins and machine-table write on every such unload.
- A quarry writes continuously at the box's edge, so its chunks unload often as the player walks.
- Autosave is 17–100 ms (G12).
- **Fix:** carry the session once per unload batch that holds any machine-written chunk, and price it.

---

## 5. Re-litigation

| Settled thing | Reopened? | Named? | Severity |
|---|---|---|---|
| Collapse f decision 5 | Yes (d9) | **Yes** | Fine. |
| **Collapse f decision 10, *"Deliberately flooding stays out"*** (`COLLAPSE-PLAN.md:1138-1141`) | **Yes, by d26's hand valve**: a vault's `enabled` 0 starts a fuse and dumps, in order to flood the valley | **No.** Fact 7 cites decision 10 against the sensor road, and d26 opens the hand road beside it | **MUST FIX (M7)** |
| M10-PLAN D1 (*"a new kind is M12's scripting"*) | Yes, **for every milestone** (d1) | Yes | **SHOULD FIX:** it decides M12's scripting scope from this pass. Mark it his, as the ladder marked its M10-PLAN amendments ("Matthew's, because it amends…"). |
| M10-PLAN D3 (the stutter trigger) | Fired by construction (d46) | Yes | Fine. |
| M10-PLAN D5 and D10 (*"nothing is ever held by a machine"*) | Yes (d23) | Yes | Fine. |
| **M10-PLAN D10, *"the world's water total is a constant of the machine layer"*** (`M10-PLAN.md:293`) | **Yes**: decoherence destroys water, and the store holds water outside the world | **No** | **SHOULD FIX** |
| Ladder 6 (the bucket), 13 (the tank), 22 (the bucket) | Yes (d23) | Yes, for quantum | Fine. |
| Ladder 21 (a row read from disk is clamped) | Vault contents are never clamped (d16) | Implicitly | **NOTE:** name it as a deliberate difference. Joules clamp at restore (`MachineSet.cs:706-716`); voxels and litres go `overfull`. |
| Ladder 22, *"random distribution"* and M10-PLAN D5's *"`Random.Shared` is not used"* | The fuse's 10–20 s draw (d27) | No | **NOTE:** say it is seeded. |
| Collapse f decision 2 and f-1's reader | d36's *"a sensor whose own chunk is unloaded reads false without looking"* changes f-1's reader | No | **NOTE:** name it a small amendment, landing in IM-9. |
| Collapse f decision 3 (*revisit if the chat wants signals routed independently*) | The power sensor ORs into the same per-channel boolean as the collapse sensor | Not discussed | **SHOULD FIX.** A pump with `onsignal` 2 (on while signalled: a collapse emergency pump) now also runs on low power, draining the bank faster. Either give each `senses` its own boolean, or state the conflation as a cost. |
| No encumbrance, ruled twice (`MATERIALS-NOTES.md:284`, `WORKLOG.md:7568-7575`, revisit trigger "a survival mode") | Fork 12 argues *"When survival bounds them …"* as the expected future | Half. It is the recorded trigger, but he has ruled against a carry limit twice with the trolley argument | **SHOULD FIX:** *"if survival ever bounds the pocket (the recorded trigger; he has ruled against it twice)"*. As written, the fork's case for the pocket leans on an outcome he has twice declined. |
| Keep-loaded | Not reopened (d34 and d35 are owed state and a save rule) | n/a | Fine. |
| Taken 1–9 | See §10 | | |

**M7 in detail.**
- d26: *"or the vault's own `enabled` set to 0 by hand, the valve for his 'i am deliberately going to
  flood that entire valley'"*.
- In `M10-NOTES.md:347-349` that sentence is about his `Regions16` pond: *"i am deliberately going to
  flood that entire valley **for spread and flood testing purposes**"*. It is a test he meant to run in
  sandbox, where he already has R and Ctrl+R. It was not a feature request.
- His feature request for deliberate flooding was the collapse one, and collapse decision 10 turned it
  down on the keystone argument (flooding feeds the collapse).
- The hand valve re-admits deliberate flooding by a third road, and cites words that do not ask for it.
- **Fix:** make d26 a ◆ fork, and name the collapse decision 10 amendment. Quote the words in full.
- **The other arm:** a vault's `enabled` 0 means it stops being a store (no deposits or withdrawals)
  and keeps billing, with no fuse.

---

## 6. Tests owed

The split names about forty tests and **five** mutation batteries, but only **three** mutations are
named: drop `packTaken` (IM-2), journal a full row (IM-3), and route the dump through `Add` (IM-6).
Each must-not-ship line should map to a mutation. The missing ones:

**IM-0.** Heap-writer and site-search arms; sensors × radius; verbatim commands (4.3). **SHOULD FIX.**

**IM-1.**
- Two mods giving the same (extra, medium) pair are refused, naming both files.
- An unknown `$`-less key inside `extras` and inside `extras.json` is refused.
- The `format` number: a mod with a newer format fails in one line.
- `programmer: true`: water and parts no longer open a panel.
- **A misspelt medium in `extras.json`** (see M10).
- Mutation: disable the unknown-key refusal, and the misspelt-key fixture must go red. **SHOULD FIX.**

**IM-2.**
- A window draw written through `SetCharge` must go red (the double count, 3.1).
- A draw attempted inside `Run()` throws (must-not-ship 7).
- The worn backpack's saved charge is 0 after `RechargePack`, `Alt+P` and a save (M8).
- **Sequencing:** *"a signalled cell never unpays a hold"* is tested before the vault kind exists
  (IM-3). Move it to IM-3, or test against a fake hold consumer. **SHOULD FIX.**

**IM-3.**
- Save during a fuse, reopen: the fuse resumes at the saved ms.
- A wreck survives a reopen, and **the reconcile does not rebuild it** (F2).
- Both reconcile branches keep contents (M4).
- An unknown codec version is kept opaque, and refuses pick and undo.
- `overfull` at restore.
- **The pick's refusal leaves no carved voxel** (pre-carve).
- **The undo refusal is a pre-flight**, with no partial `Apply`. `Apply` removes machines first
  (`EditHistory.cs:362-367`), so a refusal inside `Apply` half-applies.
- Two vaults both bill (J1 D1-3's shelter).
- `SetPort(channel)` moves contents with the row.
- Mutation: bracket the pick's dismantle with recording off, and a pick's Ctrl+Z must go red. The
  bracket must cover only the destroy and recall branches (§12).
- Mutation: bill a vault with no `PowerExact` term. **SHOULD FIX.**

**IM-4.** The plan calls this the riskiest slice, and its tests miss its riskiest named failure.
- **d35's duplication:** write a heap into a chunk, unload it, kill the process without a session save,
  reopen. Every voxel is counted once.
- The heap with no room within its search radius (the rule is unstated, §12).
- Water and heap order: dry cells after the water lands.
- The global budget across N vaults, exact, with remainders.
- A dumping vault excluded from the outlet ring and from deposits.
- Power returning after the fuse does nothing (d33).
- The global Tier B ceiling (M3).
- **Sequencing:** IM-4 dumps water that cannot enter a vault until IM-6 (the siphon and the pump ring),
  and the store's water identity is IM-6's. Build IM-6's store-water half before IM-4, or make IM-4
  blocks-only.
- *"Both tiers exact"* overclaims for Tier A, whose identity d23 says *"has never been evaluated"*.
  Write *"Tier B exact; Tier A's counters seam-checked"*. **SHOULD FIX.**

**IM-5.**
- The headless creative oracle contradicts d50 (M6).
- How does a headless run bankrupt channel 0 with the debug keys off? It needs `--placeat=` (collapse
  f-2) or a flag. Name it.
- Missing: every creative-off key does nothing (d51 is not in IM-5's *What* column at all).
- The kit is granted once, and a reopen does not regrant.
- `--mode=creative` is ignored on an existing world. **MUST FIX (M6)** for the contradiction;
  **SHOULD FIX** for the rest.

**IM-6.**
- The pocket refuses water by every path, including `--stock=`, the to-pocket verb and `P`.
- A siphon click then a quit before the boundary moves nothing.
- A siphon take on a Tier A lake surface.
- The pump stalls `full` only when every ring entry refuses.
- Mutation: drop `vaulted` from `pumped == emitted + vaulted`, and it must go red. **SHOULD FIX.**

**IM-7.**
- **Ctrl+Z on a hand dig in a chunk the quarry wrote is dropped, and in an untouched chunk still works**
  (d46's rule has no test).
- The quarry skips a non-empty vault.
- The scan cap: a dug-out box costs at most 64 reads a boundary.
- The cursor survives a save.
- The `flooded` stall.
- Mutation: quarry writes with recording on, and the test must go red. **SHOULD FIX.**

**IM-9.**
- *"Ctrl+Z after a recall refuses"* passes vacuously: a fall already clears the stack
  (`EmptyAfterAFall`). The mint J1 found lives **after the drain** (F1 + F2). Test: a destroyed and a
  recalled machine, drain, then Ctrl+Z re-adds nothing, and the next arrival rebuilds nothing.
- Recall in **sweep mode** and under `--bitechunks` (J1 D1-2's mint cases).
- A part-picked machine credits `cleared + Recalled`, not 64.
- An insolvent channel recalls nothing.
- Mutation: credit 64. **SHOULD FIX.**

**6.1 SHOULD FIX: a live bug is left unscheduled.** Fact 3 correctly says the undo can mint **today**:
the drain's destroy is journalled (`Main.cs:4465-4483`), and the reconcile rebuilds an all-air body
from a re-added row (`MachineSet.cs:446-457`). So in his shipped game, a pump destroyed by a fall, then
Ctrl+Z before the next edit, then an arrival, comes back free.
- The plan fixes it only inside d16's vault wreck bullet and d37's recall.
- No slice brackets the destroy branch for **every** machine.
- **Fix:** make it the first slice, or a fix outside this plan now, with the IM-9 test above.

---

## 7. Scope widened

| Addition | Inside the brief's scope (as extended by taken 5–9)? | Severity |
|---|---|---|
| The strict loader for **every** file, and `format` in `mod.json` (d4) | Wider than *"the extras rule … the JSON grammar"*. It changes every mod's loading. It is justified (a misspelt ceiling key), but say it is a scope extension | **NOTE** |
| M10-PLAN D1 amended for every milestone (d1) | Beyond this pass: it sets M12's scripting scope | **SHOULD FIX** (make it his; see §5) |
| The card at spawn (d54, Plan:449) | Not in any decision or slice; a new UI element | **NOTE:** give it a slice or cut it |
| The drain bracket for every machine | A fix to shipped code (§6.1) | **SHOULD FIX:** schedule it |
| d35, the unload path's session | A persistence change for all machine-layer writes | Necessary; say it is engine-wide |
| The power sensor (d36) | Inside: the brief's extra 1, *"later … power low"* | Fine |
| `programmer: true` (d6) | A bug fix | Fine |
| Multiplayer notes (d59) | Notes only | Fine |

---

## 8. UI count

**New HUD lines:**
1. The bank line (d10).
2. The store line (d20): `store 68% · hold 4 kW`. **It duplicates `hold 4 kW` from the bank line.**
3. `VAULT LOSING CONTAINMENT — 14 s` (d27).
4. The decoherence announcement (d31).
5. The card (d54).

**New panel sections and panels:**
6. The vault's stock section (d20).
7. The backpack panel with its stock section, pocket/store verbs, the operations board and the bank line
   (d20, d38).
8. Board rows opening full panels at any range (d38).
9. The quarry panel: width, length, depth, `enabled`, `priority`, `onsignal`, status (d41, d45).
10. The power sensor's `below` (d36).
11. The vault's `enabled`, `priority` and `weight` (d26, d23).
12. `priority` on the pump and on every consumer (d39).

**Map:**
13. Pin states and a legend line (d40).

**Hotbar:**
14. A water slot while the backpack is worn (d22). Which slot, and how it is assigned (M10g's
    assignment is v9), is unstated. Pressing its number does nothing without the siphon.

**Keys and tools:**
15. The siphon on T (d25). `ToolTiers.Order` sorts by (threshold, bite, id) (reader D, `ToolTiers.cs:30-38`).
    The siphon has no rock threshold, so *"after the excavator"* needs a sort rule. And in every
    sandbox world, T from the excavator now goes to the siphon, not back to the hand.
16. No new key binding. No Alt+letter. No E. No F-key.

**Verdict:**
- (2) duplicates (1). Drop `hold` from one of them. **NOTE.**
- (5) has no slice. **NOTE.**
- (14) needs a stated slot rule. **NOTE.**
- **The creative-off list omits H.** **SHOULD FIX.**
  - H stops every boundary: `_fluidHeld` gates `begin` (`Main.cs:6724`), and `RunMachineTick` runs
    inside it (`:6802`).
  - So in creative, H pauses the hold bill, a burning fuse, a dump and the quarry, while the hand's
    window draws between boundaries carry on. That makes a fuse a keypress to freeze, which is the
    reason fork 51 gives for turning Alt+P off.
  - J3 listed H among the keys to turn off. The synthesis dropped it without a reason.
  - Also say whether `--stock=` and `--nogate` are honoured on a creative world. **NOTE.**

---

## 9. Estimates stated as facts

| Claim | Status | Severity |
|---|---|---|
| *"An underground box wider than 16 m brings its roof down (M9a measured it)"* (Plan:406-407) | M9a measured a 16 m square dry room: **a 20-voxel plug, and the roof then holds** (`M9-PLAN.md:103`). 32 m drops 47,600. Nothing between was measured. **The quarry's own box is capped at 16 m** (d41), so it can never make the room the sentence describes. Wet, 16 m falls (3,028 under 5 m). | **SHOULD FIX:** *"a quarry's box (≤ 16 m) drops a plug dry, and falls under a lake"* |
| *"a stone gallery 8 m wide never falls"*, listed as **measured** (Plan:525) | A derivation from the distance rule (`M9-PLAN.md:97`: *"every roof voxel is within 4 m of a long wall"*) | **NOTE:** move it to arithmetic |
| *"a relay's worth"*, *"a thousand times"*, *"4–8%"* | Wrong (§4) | **MUST FIX (M2)** |
| *"four clicks a second"* | An assumed 0.25 s cooldown, never stated | **SHOULD FIX** |
| *"Rain or seepage finds the pit"* | False at shipped rates (§4.1) | Part of M1 |
| *"It revives the pump port M10b dropped for having no reader"* (d39) | The dropped pump port was **`weight` 1..100** (`M10-LADDER-PLAN.md:1498-1502`), not a priority | **NOTE** |
| *"Every shipped file already passes"* (d4) | Plausible: I listed every key in the 20 shipped files, and each has a matching DTO name in `Overburden.Core/Content`. Not verified against the loader's exact binding | **NOTE:** label it "checked by name" |
| *"Tier A prints the same three counters … (its own identity has never been evaluated)"* vs IM-4's *"both tiers exact"* | IM-4 overclaims | **SHOULD FIX** (§6) |
| *"Three tests that hard-count content change by stated amounts"* (d49) | The amounts are not stated. J1 grafted *"the explicit list of three hard-counted tests"* from design 3 | **NOTE:** list them |

---

## 10. Fidelity to Matthew

**10.1 SHOULD FIX: fork 17 quotes half his sentence.**
- It quotes *"battery tech maybe 19th century but your actual emitter/entangler could be a higher tech
  variation"* and answers *"already there under one pool"*.
- His 09-25 words (`M10-NOTES.md:341-346`) begin: *"requires a quantum vacuum storage 'pack' … this
  is a component that goes into the quantum storage container, and should you let it go flat"*, and
  *"think building with variable components"*.
- The plan replaces the pack inside the vault with the channel's bank. That may be right, and the undo
  argument is real. But the fork must quote the part it replaces.
- J3 2-3 called this *"likely re-litigated"*.

**10.2 MUST FIX (M7): d26 cites *"deliberately going to flood that entire valley"* out of context.**
See §5.

**10.3 SHOULD FIX: the lower containers narrow his explicit words without a fork.**
- d21 defers them: *"With unbounded pockets they have no reason yet."*
- His words: *"i'd like the ability at lower levels to construct storage containers for your stuff like
  up to the high level"*.
- That is a direct ask. Deferring it is a defensible recommendation, but it is a fork, not
  engineering.
- **Fix:** mark it ◆, with the other arm: crates now, placeable in creative, no gameplay reason until
  survival.

**10.4 SHOULD FIX: taken 7 (materials, *"like iron steel etc"*) is cut to flavour text, not marked a
fork.**
- d47 and d48 ship no material.
- The brief lets the pass decide how far to go, but his only material words named iron and steel.
- Put it in the forks list with the other arm: stainless or steel as the vault shell's material now,
  placed by nothing, one ID.

**10.5 SHOULD FIX: fork 29 omits the grain he chose.**
- The heap is *"each cell of one material"*: 1 m cells.
- He chose voxel pieces over a 1 m cube for the collapse (memory: *keep his choice, reconcile the
  arithmetic*). J3 1-8 flagged the box-fill heap as *"likely rejected"* for that reason.
- The fork's other arm is described only as *"more dramatic"*. Say the heap is at 1 m grain against
  his voxel choice, and offer a voxel-grain heap as a third arm (still direct writes, no solves).

**10.6 SHOULD FIX: fork 12's argument leans on bounded pockets** (§5).

**10.7 NOTE: fork 31 omits the third arm.**
- The third arm is "return all of it underground, slowly".
- It is CPU-infeasible: the cost is the total cells, not the rate (1 GL is 64 M cells).
- Say so in one line, so he sees why only two arms are offered.

**10.8 NOTE: the README line is not mentioned.** Taken 9 says *"we will mention it in the readme"*.
Add a line saying where it lands.

**10.9 The other taken decisions are honoured.**
- Taken 1: all six extras.
- Taken 2: inheritance declared in JSON.
- Taken 3: the creative demo.
- Taken 4: one pool, with the pocket fork honestly flagged.
- Taken 6: quarries.
- Taken 8: water in the inventory, with the hotbar fork.
- Taken 9: the loaded box.

---

## 11. Internal consistency

1. **MUST FIX (M10): d3 declares lower-tier inheritance rows *"now … inert"*, but d4 and d48 refuse data
   nothing reads.**
   - d48: *"a bill nothing reads is exactly what the strict loader exists to refuse"*.
   - Fact 1: the loader refuses `via` names below quantum.
   - The media `overburden:cable`, `copper`, `shaft` and `crank` have no registry until M10e/f, so the
     loader cannot check them. A typo `overburden:cabel` loads silently. That is must-not-ship 14, *"a
     new key that loads silently when misspelt"*.
   - **Fix, either:**
     - declare only the quantum rows now, and add the lower rows with M10e/f; or
     - ship a closed list of reserved media names that the loader checks against.
2. **M5**: `granted` (d18 against d52 and d12).
3. **M6**: IM-5's headless creative against d50.
4. **Fork 31's *"thousand"*** against the grade table (M2).
5. **d24's *"4–8%"*** against d23's 1,000 L pour (M2).
6. **d54** against d8, d15, d10's colour thresholds and d52's panels (M1).
7. **d45: *"`onsignal` 1 (its default)"* for the quarry**, against IM-1's *"without changing its name,
   bounds or default"*. f-1's default is 0 (collapse decision 4). It is presumably per machine
   definition. Say so. **NOTE.**
8. **d36 and d52: the kit's power sensor signals from spawn.** **SHOULD FIX.**
   - Default *"below 20%"*. The kit has two 5 GJ cells (`vacuum_cell.json`: `capacityJoules` 5×10⁹),
     one charged to 1 GJ: 1 / 10 GJ = **10%**.
   - So once both cells and the sensor are placed on channel 0, channel 0 is signalled.
   - The quarry (`onsignal` 1 by default) then stalls `signalled` at 2:45, and the demo's quarry beat
     fails.
   - Also define *"a percentage"* of what: the capacity of the channel's `enabled` cells.
9. **d30 against d34.**
   - Tier A water goes *"over about 10 s"* in d30 and *"to the basin at once"* in d34.
   - *"Its mouth"*: a vault has no mouth. Define it (the cell above its top face, as an outlet's), or
     the Tier A / Tier B routing is unbuildable.
   - A vault that is unloaded *and off the watershed window* has no basin. Say it is owed. **SHOULD FIX.**
10. **d31: *"All of it to Tier A"*.** The blocks' 10,000 m³ cap is stated only for underground. Does a
    surface god vault heap 990,000 m³? **SHOULD FIX.**
11. **d2 against d37.** The recall bit is *"per material … so the collapse can ask about recall without
    learning that machines exist"*, but d37's seam must read the row's channel's solvency anyway. The
    rationale is moot. The seam is the real boundary. **NOTE.**
12. **d13 against d15: deposits fill in position order, while the bill per m³ differs 39× between
    grades.**
    - Water per m³: low 2.5 W, god 0.064 W.
    - So which vault is at lower X decides a channel's bill. A player will discover that moving a low
      vault east quarters his bill.
    - **Fix:** fill the lowest bill per m³ first (highest grade), or say it is deliberate. **SHOULD
      FIX.**
13. **d8: *"then every vault's hold bill"*.** When the pool can pay three of five vaults, which ones
    fuse? The order is unstated. **SHOULD FIX.**
14. **d32: *"the budget spreads them"*.** The remainder rule is unstated (J1 D4-8's drift). **SHOULD
    FIX:** *"whole voxels and whole litres, round-robin by position, remainder to the first"*.
15. **Naming collisions.** `quarried` is joules in `PowerExact` (d43) and voxels in d18. `dumped` has
    three meanings. `recall` (joules) sits beside `recalled` (voxels). **NOTE:** give each quantity
    its own name.
16. **d43 against d46.** It pays at the boundary and carves on the next frame. A chunk that unloads,
    or a machine placed in the bite in between, means the joules paid ≠ voxels × 50 J, and IM-7's
    *"pays exactly bites × voxels × 50 J"* would fail. **SHOULD FIX:** pay for what was carved, from the
    rows directly (the window's mechanism). Or refund the uncarved remainder under `quarried`.
17. **d16's *"At-range tools … skip a non-empty vault"*, and d51's key gating, are in no slice.**
    **SHOULD FIX:** put them in IM-3 and IM-5.
18. **d12's *"the undo sees only the pocket"*, and the Ctrl+Z of a dig after its voxels were
    deposited.** This is safe: `Apply` refuses `OutOfStock` (`EditHistory.cs:21`, checked in the pre-flight at `:305-341`), or takes the same
    material from other stock, which conserves. No finding. Recorded because the coordinator's
    checklist asked.

---

## 12. What a builder would get wrong (IM-1 to IM-9)

In order of how likely each is to become a bug:

1. **The window built as a `storage, carried: true` pack with a capacity.** `RechargePack` and
   `Alt+P` then make it a second pool (M8).
2. **The window draw written through `SetCharge`.** Double-counted in `PowerExact` (3.1).
3. **The hold charged with no term,** or under `DrawnByPumps` (M9).
4. **The recording-off bracket put around the whole drain.**
   - The plan says *"the drain's destroy branch"* (d16) and *"at the drain, with recording off"* (d37).
   - A builder who brackets `Drain` wholesale makes the hand pick's dismantle (the `credit` branch
     with `Recalled == 0`) un-undoable, which breaks *"Ctrl z feels good"*.
   - **State:** off for `Lost > Carved + Recalled` (destroy) and for `Recalled > 0`; on for a pure pick.
5. **The vault refusal at the drain instead of before the carve** (3.2).
6. **The undo refusal inside `Apply`.**
   - `Apply` removes machine rows before it writes voxels (`EditHistory.cs:362-367`), so a refusal found
     there half-applies a step.
   - It must be a pre-flight, beside the writability check (`:330-341`), with a new `UndoOutcome`.
   - Say what happens to the stack: the step stays, like `OutOfStock`, so every older step is
     unreachable until the vault is empty.
7. **The reconcile left alone** (M4).
8. **The Tier B dump ball sized like `PourPending`**, to everything owed. It must be sized to the
   boundary's budget (J2 D2-1's walk of (2r+1)³). The plan says *"never the relay's `PourPending`"*,
   not *"sized to the budget"*. **SHOULD FIX.**
9. **The heap:**
   - its order against the water (a heap written first displaces the dump's own water; dry cells only
     after);
   - no rule when no dry loaded air lies within its radius (wait, widen or decohere?);
   - no exclusion of machine cells or the wreck's cell;
   - *"nearest first"* against *"bottom-up"*.
10. **The siphon:**
    - which packs enable it: with a worn `lead_acid_pack` on channel 0, does the siphon reach channel
      0's store, and who pays? d25 says *"with no backpack it takes nothing"*;
    - Tier A or Tier B source;
    - when joules and litres move;
    - its cooldown;
    - its place in T's sort.
11. **The store identity's scope** and the meaning of `entering` and `leaving` (3.1).
12. **The vault's mixed-contents bill formula.** Presumably water fraction × water rate + block fraction
    × block rate. Unstated.
13. **The kit:**
    - the charged cell;
    - where `granted` counts;
    - what *"4,096 cells"* means (cells of 64 voxels, per `unit: cells`; say it);
    - that the kit grows slice by slice: IM-5's kit cannot contain the quarry (IM-7), water (IM-6) or
      the power sensor (IM-9).
14. **The quarry's box origin.** *"In front, along the facing"*: can `Facing` be up or down? Is the box
    adjacent at the quarry's top face and dug downward? Where is the first layer?
15. **A full store and the pump.** *"The pump stalls `full`"*. With an outlet and a vault in the same
    ring, the builder must stall only when every ring entry refuses, as today's `StalledNoRoom` does.
16. **Priority and the ring.** Priority orders consumers, and the ring orders a pump's outlets. Say they
    are unrelated.

---

## 13. The judges' must-not-ship findings, one by one

**Judge 1 (Core)**

| # | Finding | Status |
|---|---|---|
| D1-1 | Vault state a live object; the journal copies stale bytes | **CLOSED** by d16 (emptied before `Remove`), d16 (undo refusal) and d19 (one codec). Caveat: d19 must say the row bytes are the only copy (§2). |
| D1-2 | Recall credit at the drain with no per-row signal | **CLOSED** by d37 (`Lost == Carved + Recalled`, seam per voxel). Its tests miss sweep mode and `--bitechunks` (§6). |
| D1-3 | A duplicate vault inert: a bankruptcy shelter | **CLOSED** by d13 (every vault bills and fuses). It introduces M3 (unlimited vaults defeat the per-vault cap). |
| D1-4 | A sensor switching off the cells that pay a vault dumps it | **CLOSED** by d9, named as an amendment of f decision 5. d26 opens a hand road beside it, unnamed (M7). |
| D1-5 | Wreck flag, phase and fuse must persist | **CLOSED** by d19. **The reconcile's remove branches reopen it (M4).** |
| D2-1 | Recall at release with the drain journalled mints a machine | **CLOSED** for vaults and recall (d16, d37, must-not-ship 3). The same live bug for every destroyed machine is unscheduled (§6.1). |
| D2-2 | Recall credits 64 whatever was carved | **CLOSED** (d37, must-not-ship 3). |
| D2-3 | A hand-off to sisters leaves contents in the journal copy | **CLOSED** (d16, emptied before `Remove`). |
| D2-4 | A destroyed vault's owed dump has no home | **CLOSED** by the wreck (d16). Reconcile caveat (M4). |
| D2-5 | Restore refuses an over-capacity vault | **CLOSED** by `overfull` (d16, must-not-ship 5). |
| D3-1 | A carried vault holding contents | **CLOSED**: the window holds nothing (d7, shape 1). |
| D3-2 | A rebalance's owed dump has no home | **CLOSED**: pick refused without room; wreck on a fall. |
| D3-3 | Parts through an unjournalled install door | **CLOSED** by grades in the ID (d17), with the revisit condition named. |
| D3-4 | Recall credits 64, not `cleared + Recalled` | **CLOSED** (d37). |
| D3-5 | *"A sensor can never empty a vault"*, a false claim | **CLOSED** (d9). |
| D4-1 | Recall of whole bodies at release | **CLOSED** (d37). |
| D4-2 | The vent's queue in a row that three paths remove | **CLOSED** by the wreck and the refusals (d16). Reconcile caveat (M4). |
| D4-3 | `SetPort(channel)` leaves a ledger with no vault | **CLOSED structurally**: contents ride the row. Not stated in the plan (§3.2). |
| D4-4 | The vault as a storage row: six sites, `PowerExact` breaks | **CLOSED** by a new `vault` kind (d1). The hold's own term is missing (M9). |
| D4-5 | `refill` printed as a `PowerExact` term | **CLOSED**: the window has no refill. **The mechanism that would refill it is unpinned (M8).** |
| D4-6 | Parts | **CLOSED** (d17). |
| D4-7 | Accepts a flood by sensor | **CLOSED** (d9). |

**Judge 2 (CPU)**

| # | Finding | Status |
|---|---|---|
| D1-1 | CPU rates in mod JSON with no ceiling | **CLOSED** (d5, must-not-ship 10). |
| D2-1 | An uncapped Tier B dump | **OPEN.** Capped per vault only, with vaults unlimited (d13), and the surface route floods through portals uncapped (M3). |
| D3-1 | No global quarry budget | **CLOSED** (d44). |
| D4-1 | No global quarry budget | **CLOSED** (d44). |

**Judge 3 (gameplay)**

| # | Finding | Status |
|---|---|---|
| X-1 | Collapse decision 10 reopened by the cell road | **CLOSED** for signals (d9). d26's hand valve reopens decision 10 by another road, unnamed, on a misread quote (M7). Treat it as **OPEN** until named. |
| 1-1 | Water never enters any inventory (narrows taken 8) | **CLOSED** (d22, offered as fork 22). |
| 4-2 | The pack refilled first becomes the two-pool arm; must be pinned | **CLOSED in intent** (d7), **OPEN in mechanism**: the backpack's JSON, the validator, `RechargePack` and `Alt+P` are unpinned, and today's code gives it a charge (M8). |

**Cost-level judge findings still open** (not must-not-ship, listed so they are not lost):
- J1 D1-10: the backpack's JSON fails validation (part of M8).
- J2 D1-8: the dump has no bracket (§4).
- J2's owed #5 and #6: the heap-writer and sensor measurements (§4.3).
- J3 1-6: the climax does not fire in daylight (M1).
- J3 2-8: H is dropped from the creative-off list (§8).
- J3 1-8: the heap is at 1 m grain (§10.5).
- J3 2-3: the pack inside the vault (§10.1).
