# Judge 1: Core, the non-negotiables and the identities

Lens: is each design correct against the engine as it stands at `71e6eed`? I checked every load-bearing
code claim against the repo and built the failing sequence for each suspect path. "Verified" means I read
the line. Paths are relative to the repo root.

**Five code facts carry most of the findings below.** I read each one myself.

- **F1. The machine drain journals.** `DrainMachines` (`Overburden.Game/Main.cs:4465-4483`) has no
  recording-off bracket. The frame's order is:
  1. `UpdateSupport`, then `FrameCollapse` (`:3654`, `:6239`). This is where the director releases, and
     `AfterCollapseStep` clears the stack (`:6093-6104`).
  2. The pipeline update.
  3. `DrainMachines` (`:3704`).
  4. `EndStep` (`:3709`).

  `EditHistory.MachineChanged` sets `FellSinceLastEdit = false` and opens a fresh step
  (`Overburden.Core/Editing/EditHistory.cs:190-200`). So the destroy that follows a release lands in an
  undoable step, which Reader C inferred and I confirm.
- **F2. The reconcile rebuilds a body that is absent over air.** At arrival, a row whose 64 voxels are
  all air gets its body written back from the row (`Overburden.Core/Machines/MachineSet.cs:446-457`),
  with no inventory debit. A row re-added by Ctrl+Z over an empty cell therefore becomes a whole machine
  the next time its chunk arrives.
- **F3. The journal copies the row's bytes.** `Copy(row)` clones `row.KindState`
  (`EditHistory.cs:427-428`), and `Apply` re-adds that copy through `MachineSet.Add` (`:397-404`). Any
  contents held outside `row.KindState`, or stale inside it, go into the journal as those bytes.
- **F4. The sweep writes rubble in place over whatever it moves** (`Overburden.Core/Structure/Collapse.cs:298-302`).
  The director's release writes air, launches with the materials captured
  (`Overburden.Core/Structure/CollapseDirector.cs:757-776`), and lands as `_rubbleId` (`:939-943`).
  `LandingPlan.VoxelCells` already maps each voxel of a piece to its rest cell
  (`Overburden.Core/Structure/LandingSolver.cs:22-27`), so D3 and D4 are right that landing a piece in
  its own materials is a flag.
- **F5. `PourPending` calls `_fluid.Add`** (`Main.cs:7614`), which is the `injected` door. Its ball is a
  template, never a call to reuse. Tier B's `Add`, `Remove` and `Emit` throw mid-tick
  (`Overburden.Core/Fluid/FluidSimulation.Ownership.cs:163-172`).

**Code claims confirmed:**
- `Carve.Cube` has a named-origin overload taking `bodies` and a budget (`Overburden.Core/Mining/Carve.cs:416-439`).
  It credits `into` before it calls `Taken` (`:765-774`).
- `IMachineBodies` has only `Taken` (`Overburden.Core/Machines/IMachineBodies.cs:23-27`).
- `MachineTick.Draw` does not exist.
- `EditHistory.Forget(ChunkPos)` drops steps by voxel chunk only, so a step that only touched machines
  survives (`EditHistory.cs:248-268`).
- `UndoOutcome` has no `Refused` member (`EditHistory.cs:9-25`).
- The header is v7 (`Overburden.Core/Persistence/WorldHeader.cs:187`), and `PackSave` is
  `(material, charge, channel)` (`:118`).
- A carried pack at zero charge is not stored (`Overburden.Core/Inventory/CarriedPacks.cs:21-26`).
- A storage must declare a capacity above 0 (`Overburden.Core/Machines/MachineDefinition.cs:258-263`).
- `VolumetricInventory` has 46 non-test references, which is D1's figure.

---

## 1. Scores

**D1 (engine): 7 / 10.**
- **What it gets right:**
  - The window is the only one-pool design with no new joule term, and I checked why. `Draw` lowers
    `stored` by X, and `PackTakenMillijoules` rises by X on the right of `PowerExact`
    (`Overburden.Core/Machines/MachineTick.cs:404-408`), so the identity holds with nothing in flight.
  - The wreck row is the only rule in the four designs under which contents never leave the books with a
    row.
  - Refusing the pick and the undo on a non-empty vault keeps taken 8 literally true (no litres leave
    through an ordinary pick), and it removes the dismantle case entirely.
  - Its must-not-ship list matches the code.
- **It loses three points to four holes, each closable with a small graft:**
  - Its live-object vault state is copied stale into the journal (F3), which mints on dismantle then
    Ctrl+Z.
  - Its recall credit has no per-row signal, which mints in sweep mode (F4).
  - An inert duplicate vault holds contents with no bill.
  - It never addresses a sensor switching off the cells that pay the vault.

**D2 (gameplay): 4 / 10.**
- **What it gets right:**
  - It is the only design that keeps collapse decision 10 whole: the rent is paid before signal
    overrides.
  - It persists the fuse and the owed dump, captured while alive.
- **Where it fails:**
  - It credits recall at release and leaves the drain journalled (F1 + F2), which mints a whole machine.
  - Its vault contents live in rows, and a dismantle that hands the contents to a sister vault leaves
    the row's contents in the journal (F3), which duplicates them.
  - A destroyed vault's owed dump has no home once its row is gone.
  - "Restore refuses an over-capacity vault" either destroys the contents or bricks the world.
  - Binding swaps the undo's inventory with no clear and no undo term.

**D3 (format): 6 / 10.**
- **What it gets right, the best Core work in the set:**
  - The recall rule `Lost − Carved == Recalled`, credited at the drain with recording off.
  - "The row is emptied before `Remove` journals its copy".
  - Contents are never clamped.
  - Media are namespaced through `dependencies`, with no bare `via` special-cased to the base mod.
  - The loader is strict everywhere, with diagnostics.
  - A patch is a commutative union.
  - Litres are confined to channel members by structure.
- **What costs it the win:**
  - It holds contents in a *carried* vault. The backpack's contents have no persisted home, no stow rule
    and no dump rule.
  - It installs parts through a door the undo cannot see, so undoing a placement loses the part.
  - A destroyed vault's owed dump has no home once its row is gone.
  - It claims decision 10 stands while the cell path still dumps.

**D4 (physics): 4 / 10.**
- **What it gets right:**
  - The ledger in the header keeps contents out of the rows the journal copies.
  - It says honestly that starving the vault through the cells is possible.
  - Its derived figures check.
- **Where it fails:**
  - It recalls whole bodies at release with the drain journalled (F1 + F2), which mints.
  - It puts the vent's queue in a row that dismantle, destruction and recall all remove.
  - `SetPort(channel)` moves a vault's capacity away and leaves a ledger with no vault and no bill.
  - It puts the vault's cell in `ContainerState` without the six storage-kind sites learning it.
  - It adds `refill` to `PowerExact`, and refill is an internal transfer, not a door.
  - Parts carry D3's undo hole.
  - It accepts a flood by a sensor, which is the thing collapse decision 10 was taken to prevent.

D2 and D4 tie. D2 is ahead only on decision 10.

---

## 2. Defects, per design

### D1: engine

1. **§5.3 and decision 10: vault state is "a live object encoded at save", and the journal copies the
   stale bytes. Must not ship.**
   - `Snapshot` writes `row.KindState` through `MachineRowBlob.Write(row)` (`MachineSet.cs:602-607`),
     and `Remove` journals `Copy(row)` with a clone of those bytes (F3).
   - The sequence:
     1. Fill a vault.
     2. Autosave, so the row's bytes now say N.
     3. Take everything back out: the façade's `Take` "spends the vault first".
     4. Pick the now-empty vault. The refusal lets it through.
     5. Press Ctrl+Z. `Apply` re-adds the copy, and D1 says contents "enter at restore and at an undo's
        re-add".
   - N is minted under a printed `entering`, so the identity holds while matter is created. This is the
     class the panel was warned about.
   - D1's own revisit trigger ("a second reader of `KindState` appears") has already fired: the journal
     is that reader.
   - **Fix:** encode the live store into `row.KindState` inside `MachineSet.Remove` before the journal
     copy, or have `Add` never decode contents from a journal copy.
2. **§3.2: the recall credit at the drain has no per-row signal. Must not ship as written.**
   - D1 counts `recalled` in the director and credits `64 − Carved` at the drain, but never says how the
     drain tells a recall from any other non-pick loss.
   - A builder who keys the branch on the material's recall flag mints 64 in each of these cases:
     - in sweep mode, where the body voxels become rubble in place (F4);
     - under `--bitechunks`, which writes air with no credit (`Main.cs:6304`);
     - under any future blast.
   - **Fix:** D3's rule. The director reports each excluded voxel through a seam (`IMachineBodies.Recalled`),
     which increments a per-row `Recalled`. The drain then credits `cleared + Recalled` only when
     `Lost == Carved + Recalled`.
3. **§5.2 "One per channel": a duplicate vault is "inert, stall `Duplicate`", and "first" is decided by
   origin order. Must not ship.**
   - The sequence:
     1. Fill vault A.
     2. Place vault B at a lower X on the same channel. B becomes the account, and A goes inert, holding
        its contents with no bill and no fuse.
     3. Go bankrupt. Nothing dumps.
     4. Pick B, which is empty and so allowed. A is the account again, full.
   - That is a bankruptcy shelter built by the rules, against taken 4.
   - **Fix:** every vault bills and fuses whether or not it is the account, or a second vault is refused
     at placement.
4. **§3.1 and §5: a sensor switching off the cells that pay a vault dumps it. Must not ship.**
   - f-1's readers include the vacuum cell, and "a signalled-off cell leaves its channel's pool"
     (`docs/COLLAPSE-PLAN.md:1118-1119`).
   - f-3's own play sets "a cell on its channel set to go off" (`:1159`).
   - Under one pool, that composition is *"cut power to the water containers … and flood the place"*,
     which decision 10 kept out (`:1138-1141`).
   - D1 guards only the vault-as-reader case.
   - D1's own `senses: power` makes it worse:
     1. The pool goes low, so the channel is signalled.
     2. The cells (`onsignal` 1) leave the pool.
     3. The vault's bill goes unpaid, and the vault fuses.
   - **Fix:** graft D2's rule (below).
5. **§5.3: the wreck flag's persistence is unstated. Must not ship if it lands in a session field.**
   - A wreck's body is air. If the flag is a session field like `Lost`, the next arrival's reconcile
     rebuilds the body (F2), and a destroyed vault stands up again full.
   - It must be in `VaultState`, and so must the phase (`Dumping` is the terminal state) and the fuse's
     milliseconds. Otherwise quitting and reopening escapes the dead state.
6. **§9: the kit is a literal path, `mods/overburden/kits/creative.json`.** That is a privileged path to
   the base mod (non-negotiable 5). **Cost.** Make `kits/*.json` a content section merged across mods, as
   D3 does with `palette/*.json`.
7. **§5.3: "to Tier B through the relay pour's ball (`Main.PourPending`)".** Taken literally this calls
   `Add` (F5), which D1's own must-not-ship 3 forbids.
   - The dump needs a Core ball that calls the `Released` twin, and it keeps `LakeCeiling.Cover` before
     the pour (`Main.cs:7599`).
   - **Cost, and a trap for the builder.**
8. **No voxel identity for the vault.** D1 prints `debited == written` for the dump and a denominator for
   entering and leaving, but no line of the form `held == …`.
   - Constraint 14 required any design adding a voxel home to build one first. The vault is a new home
     with five doors: the façade's `Add`, its `Take`, the quarry, the heap and the undo.
   - **Cost:** it is the weakest voxel story of the four.
9. **§4.1: `Draw` debits rows between boundaries.**
   - The siphon "pays … from the window at the boundary". If that request runs inside `Run()`, between
     `BeginPower` and `WriteBack`, then `WriteBack` overwrites the debited rows from `_available`
     (`MachineTick.cs:710-733`). `packTaken` rises while `stored` does not, and joules are minted.
   - The siphon's queue must run outside `Run()`.
   - **Cost, and a trap for the builder.**
10. **The JSON in §2.3 does not load today.** `quantum_pack` is a storage with no `capacityJoules`, and
    the validator refuses that (`MachineDefinition.cs:258-263`). D1 never says the rule changes for the
    `account` form. **Cost.**
11. **The quarry's joule term is not named.**
    - `DrawnByPumpsMillijoules` means pumps only (Reader C §3.3).
    - D1 names `heldByVaults` and nothing for the quarry. The quarry's `Removed × J` needs its own
      `PowerExact` term, or `PowerExact` breaks every boundary.
    - **Cost.**

### D2: gameplay

1. **§3.2: recall credits at release, and the drain stays journalled. Must not ship.**
   - The sequence:
     1. The director writes all 64 body voxels to air, recording off, and credits 64 to the store.
     2. The same frame, the drain destroys the row: `Remove` is journalled in a fresh step (F1), and
        `ClearBodyVoxels` finds nothing to clear.
     3. Ctrl+Z during the flight, before the landing clears the stack again, re-adds the row with no
        voxel cost.
     4. The chunk's next arrival rebuilds the body (F2).
   - The store holds 64 and the world holds a working machine.
   - D2's reason for avoiding the drain ("crediting 64 would mint") inverts the fix: the drain credit is
     the safe one *if* release excludes the voxels, and the drain is bracketed.
2. **§3.2: "into the store, as 64 voxels".** Recall credits 64 whatever the pick already carved
   (`Carved` credited earlier, `MachineSet.cs:542`), which mints `Carved`. **Must not ship.** Credit
   `64 − Carved`.
3. **§5.2: a vault that "ceases to exist first hands its contents to its sister vaults", and the stack is
   cleared only "a dismantle that dumps". Must not ship.**
   - When the handing places everything, nothing dumps and the stack stays.
   - The journal copy of the removed row still holds the contents in `KindState` (F3), because D2 never
     empties the row before `Remove`.
   - Ctrl+Z re-adds it, and the contents exist twice under a printed `entering`.
4. **§5.2: the destroyed vault's owed dump has no home. Must not ship.**
   - "The owed litres persist in the row", and the voxels "owed and released when the chunk arrives". A
     fall destroys the row at the drain (`MachineSet.cs:557-561`).
   - The owed contents leave the books with the row, or come back through Ctrl+Z (defect 1).
   - D1's wreck row is the answer.
5. **§5.4: "Restore refuses a vault whose contents exceed its capacity". Must not ship.**
   - Refusing in `Restore` means throwing (`MachineSet.cs:720-721`), which refuses the world, or dropping
     the row. Dropping it deletes every litre and voxel the vault held, and the body is then cleared as an
     orphan at arrival without credit (`:501-526`).
   - A retune of a grade's capacity in JSON would do either. That is worse than the clamp D2's own
     must-not-ship 4 forbids.
   - D3's `overfull` is right: keep the contents, refuse deposits, and count.
6. **§4.2: binding makes the store the undo's inventory, with no clear and no term. Cost, close to must
   not ship.**
   - `Undo` debits and credits whatever inventory it is handed (`EditHistory.cs:383-393`). The store's
     identity (§5.4) has no undo term, so any Ctrl+Z while bound breaks it, unless its counters live
     inside `Add` and `Take`, where they mislabel.
   - Unbinding moves nothing, so an undo after unbinding debits the pocket for the store's credit.
   - Clear the stack on bind and on unbind, as D1 does on a link change.
7. **§4.1: "the hand spends against the bank as it stood at the last boundary", "settled first".**
   - Read literally, this is an owed joule in flight between boundaries. An autosave lands on any frame,
     so a save and quit before the boundary mints up to one boundary of spend (the siphon's 32 MW is 16 MJ).
   - `PowerExact` is documented "No term is in flight" (`MachineTick.cs:399-403`).
   - **Cost:** pay from the backpack cell's own charge, as D3 and D4 do.
8. **§3.7: the siphon is not queued to the boundary.** A click-time `Remove` throws mid-tick (F5), and
   Godot logs it as a feature that worked once. **Cost, and a trap for the builder.**
9. **§5.2: the heap does not say "dry cells".** A solid written into a wet cell is `displaced`: printed,
   but the player's water is destroyed by a machine door. **Cost.**
10. **§3.2 and §2: "the director learns that machines exist"**, both to find the row's channel and store
    and to pay 3.2 kJ.
    - This breaks the rule recorded at `IMachineBodies.cs:10-17`.
    - The payment happens in `FrameCollapse`, every frame and off the boundary, so it needs a `Draw` that
      D2 never names.
    - **Cost.**
11. **§2: `"tier": "quantum"` in a machine file, and `inherits.from`.**
    - Tier IDs are never namespaced. If a third-party mod's bare `quantum` resolves to
      `overburden:quantum`, that is a privileged path (non-negotiable 5). If it resolves in the mod's own
      namespace, every mod must spell the base's tier out, and D2 does not say which.
    - `inherits.from` is a cross-file relation that depends on order.
    - **Cost.**

### D3: format

1. **§4: the quantum backpack is `kind: vault, carried: true`, and withdrawals take "from the backpack
   first", so it holds contents. Must not ship.**
   - A carried item has no row, and `CarriedPacks` annotates charge only (`CarriedPacks.cs:5-44`).
   - Nothing says where the equipped backpack's 64,000 L are saved (only "the equip slot goes in header
     v8"), what happens to them on stow, or where its own hold bill's dump goes. D3's dump table has no
     carried row.
   - Stow or unequip loses or mints litres and voxels.
   - **Fix:** a window with no contents, as D1 and D4 have.
2. **§5, when a vault stops existing: "rebalance … whatever doesn't fit is dumped", with "owed quantities
   persisted in the row". Must not ship.** The row is removed at that moment, so the owed dump has no home.
   Same as D2 defect 4. D1's wreck is the fix.
3. **§6: parts install through an unjournalled door, and the placement undo removes the row. Must not
   ship.**
   - The sequence:
     1. Place a vault. The step's copy has empty parts.
     2. Next frame, install an entangler. It is "not journalled"; the pocket loses 1 and the row gains it.
     3. Press Ctrl+Z. `Apply` calls `machines.Remove(row.Cell)` on the live row (`EditHistory.cs:362-367`).
   - The part is in neither the pocket nor a row: a permanent part destroyed with no term.
   - A dismantle whose journal copy still lists the parts, then Ctrl+Z, mints them the other way.
   - **Fix:** the undo refuses to remove a row with installed parts, and `Remove` journals a copy with
     the parts cleared after they are credited.
4. **§3.2: "`Lost − Carved == Recalled` credits the body (64 voxels)".** The condition is right, but the
   amount must be `64 − Carved`, which is `cleared + Recalled`. **Must not ship:** it mints `Carved` on
   a part-picked machine.
5. **§3.1 and §5: "On a vault it gates transfers, never the hold … a sensor can never empty a vault into
   the mine". Must not ship (the claim is false).** The cell reader still starves the hold (D1 defect 4).
6. **§7: the quarry "never cuts its own body or the column under it", and says nothing about other
   machines.**
   - With `bodies: null` it destroys them. With the set as `bodies` it dismantles them into the store.
   - D1 and D4 skip machine cells explicitly.
   - **Cost, and a trap for the builder.**
7. **§5: dump voxels "launch as collapse pieces".** Pieces in flight are not persisted (Reader C §2.3),
   so an autosave mid-flight followed by a crash loses the player's contents, not rubble. D1's direct,
   dry, recording-off heap has no in-flight state. **Cost.**
8. **§4: `priority` on the carried pack (default 15, "a setting").** A carried item has no row, and
   `PackSave` has no port. **Cost:** a header field D3 never lists.

### D4: physics

1. **§3, recall: "lifted whole into the store … before the piece is cut". Must not ship.** The mechanism
   is D2's defect 1 exactly, plus parts: the store holds 64 voxels and the parts, and the reconcile
   rebuilds the body from the re-added row (F1, F2). It also credits 64 whatever `Carved` is.
2. **§5, rule 4: dismantle, destruction and recall "vent at the vault's cell" from a queue "in its row".
   Must not ship.**
   - All three remove the row, so the queue leaves the books.
   - The voxel identity `ledger + queue == loaded + …` has no row pair for entering and leaving, so it
     breaks.
   - A Ctrl+Z after a dismantle, which is journalled with its `ContainerState`, re-adds the queue and
     vents it twice.
3. **§4, the store is "per channel, in the header; capacity the sum of the vaults". `SetPort(channel)`
   has no rule. Must not ship.**
   - `SetPort` is not journalled and only bumps `Version` (`MachineSet.cs:245-271`).
   - Move the only vault from channel 0 to channel 1, and channel 0's ledger keeps its contents with no
     vault: no τ, so no bill, and no cell to vent from. The backpack on channel 0 still withdraws from it.
   - That is a free, dump-proof store.
   - Rule 4 must cover a port change, and so must a restore that finds a ledger with no vault.
4. **§5, persistence: the vault is a storage row whose charge lives in `ContainerState`. Cost, and a
   must-not-ship trap.** Six sites assume `{Kind: Storage, Carried: false}` and `StorageState`:
   - `ChargeOf`, `SetCharge`, `CapacityOf` (`MachineSet.cs:142-170`);
   - the restore clamp (`:708-716`);
   - `BeginPower` (`MachineTick.cs:628`);
   - `WriteBack`'s `StorageState.Write` with the capacity from the *definition* (`:719-723`), where a
     vault's capacity comes from a part on the row.

   D4's must-not-ship 6 names the write. It misses the read: if `ChargeOf` does not know containers,
   `StoredMillijoules` (`:384-396`) omits the vault's charge while `_available` includes it, and
   `PowerExact` breaks every boundary.
5. **§4: "`PowerExact` gains … `refill`". Must not ship as written.** The pack is on the left side
   already (`stored + pack`). A refill is a transfer from rows to the pack inside that sum, not a door.
   Printed as a term, it is off by the refill every boundary: the 1,403-line precedent.
6. **§6: parts.** D3 defect 3 applies unchanged. **Must not ship.**
7. **§5, "the deliberate flood": the design accepts that a sensor starving the cells vents the vault**
   (decision 22, "rests on the player's restraint"). That is his rejected idea, reached by configuring
   f-1 as f-3 plays it. **Must not ship:** it contradicts collapse decision 10.
8. **§5, the vent's share is pro rata "by capacity, by material and by litres".** Integer division
   leaves remainders. With no stated rule for them, the identity drifts by rounding across several
   vaults. **Cost.**
9. **§5: vented solids that land in an unloaded chunk are printed `lost`.** The identity holds, but
   *"mine to get it back"* is broken at the box edge. The pieces are also in flight across an autosave,
   as in D3 defect 7. **Cost.** Recall's toll is paid in `FrameCollapse`, off the boundary, which needs
   an unnamed `Draw` (as D2 defect 10).

---

## 3. Where the designs disagree, and my lens decides it

1. **Does mining by hand credit the pocket or the vault? The pocket (D3, D4).**
   - The undo's inventory stays one object with one home.
   - No oracle moves.
   - There is no façade whose merged view might be saved.
   - D1's façade is sound only with its two must-not-ships and a link-change clear. It is the one piece
     of the winner I would not take.
   - D2's binding is the worst arm (defect 6).
2. **Is the vault's bill flat or by fill? By fill (D1, D3, D4), weakly.**
   - The two are identity-neutral.
   - By fill, an empty vault cannot fuse, so the terminal state is reachable only with contents to owe.
   - The bill must read the vault's own contents, never a row's charge, which is a write-back artefact
     (`MachineTick.cs:710-733`).
3. **One carried item or two? One (D1, D2), or a second that holds nothing (D4).** Never a carried item
   that holds contents (D3). A carried store needs a persisted home, a stow rule and a dump rule, and has
   none.
4. **Grades on the vault's ID or on parts? The ID (D1, D2).**
   - IDs are permanent but inert.
   - Parts add an inventory-to-row door (install) that the undo cannot see, and a second credit (the
     parts on dismantle and on recall). Both are live holes in D3 and D4.
   - Parts can come later behind the refusal D1 already uses: the undo refuses a row that holds
     anything.
5. **A strict loader? Yes, on every DTO (D1, D3), with D3's diagnostics.**
   - That means `$` keys allowed, a did-you-mean, and `format`.
   - f-1's `senses` and every new key land in the old blocks, where a misspelling loads silently today
     (Reader A trap 4).
6. **A template world or a kit? A kit.** It must be a content section merged across mods, with a printed
   `granted` term. D2's template is a shipped save, and every future header change that is not additive
   refuses it (v7 already was one, `WorldHeader.cs:203`).
7. **Recall at release or at the drain? Both halves, split as D3 splits them.**
   - Exclude the body voxels from the piece at release, with a material bit (the director stays ignorant
     of rows) and a per-voxel seam call that increments a row's `Recalled`.
   - Credit `cleared + Recalled` at the drain when `Lost == Carved + Recalled`.
   - Bracket the drain with recording off (D1).
   - A credit at release with the drain journalled mints (F1 + F2).
8. **Does a signal ever dump a vault? No (D2).**
   - The hold is paid from the channel's `enabled` cells before signal overrides apply. This amends f-1
     decision 5's "leaves the pool as `enabled = 0` does" for one consumer, and it must be named as an
     amendment.
   - Any power sense must read Σ stored on the channel's `enabled` cells from the last boundary, never
     `_available`. Otherwise a power-low signal that switches the cells off latches itself.
9. **Where contents live: in the row, with a wreck (D1).**
   - The row must be encoded at every `Remove` and every journal copy (D1 defect 1), or emptied first
     (D3).
   - D4's header ledger avoids the journal, but it creates a store that can exist with no vault
     (defect 3).
10. **Is the pick on a non-empty vault refused? Yes (D1).**
    - It keeps taken 8 literal: *"never by an ordinary pick"*. In D2, D3 and D4, a steel pick on a full
      vault releases its litres.
    - It deletes the owed-dump-at-dismantle problem.

---

## 4. The winner through my lens, and the grafts

**Winner: D1.** It rests on the fewest new homes and the fewest terms in flight:
- a window with no joule term;
- one item;
- grades in IDs;
- a wreck that outlives a fall;
- refusals where the undo and the pick would otherwise need a door.

Each of its must-not-ships closes with a graft or a one-line rule. D3's holes need redesign: a carried
store, and parts behind an unjournalled door.

**Grafts:**
- **From D3:**
  - The recall rule (`Lost == Carved + Recalled`, per-voxel seam, credit `cleared + Recalled`) in both
    collapse modes.
  - "Empty the row before `Remove` journals it", applied to D1 as "encode the live store at `Remove`".
  - `overfull` at restore: kept, deposits refused, counted.
  - The strict loader's diagnostics, `$` keys and `format`.
  - Namespaced media resolved through `dependencies`, with no bare `via` special-cased.
  - Kits and palettes as unions across mods.
  - Litres refused on any vault that is not a channel member.
  - The explicit list of three hard-counted tests.
- **From D2:**
  - The hold paid before signal overrides.
  - The fuse, the phase and the owed dump persisted, captured while alive.
  - A voxel identity for the store, built first and held over 1,000 random operations. The undo must be
    in those operations, which is what D2's version misses.
- **From D4:**
  - The vent waits while unloaded, except water on analytic ground inside the window. D1 already has
    this; D4 states it best.
  - The quarry's `enabled` defaults to 0.
  - "Skip machine bodies always", as a test rather than a sentence.
  - Label every figure as D4 does.

**Across the designs:**
- **A heap written at unload.** Any chunk the machine layer wrote since the last session save should
  carry the session in its unload batch. `SubmitSaves` writes chunks with no session today
  (Reader C §2.3). That closes the duplication D1 names, which no other design mentions.
- **A dump ball** must call a twin, never `PourPending`, and must keep `LakeCeiling.Cover`.

---

## 5. Estimates stated as facts

- **D1, "Leaving is provably 0 by the three rules above".** Not provable: defect 1's re-add enters stale
  contents. It should say "0 once `Remove` encodes the live store".
- **D1, "the same class as mining's"** (the heap at unload, then a crash). The direction is opposite:
  mining loses the credit, because the chunk saved and the header did not, while a heap duplicates,
  because the chunk saved and the row still owes. It should say "a new class: duplication at unload".
- **D1, "`PowerExact` needs no new term".** **Verified true** for `Draw`. It is false for the quarry,
  which needs one (defect 11).
- **D2, "It hooks at release … never at the drain, where crediting 64 would mint".** Stated as the fix,
  and it is the mint (defect 1). It should say "credit at the drain, bracketed, after excluding at
  release".
- **D2, "A dismantle that dumps clears it too, which closes Reader C's duplicate-on-undo hole".** It
  closes only the branch that dumps. It should say "closes it when a dump occurs; the handing branch
  needs the row emptied".
- **D2, "One quarry at rate 1 crosses the `machines > 1 ms` trigger alone".** Its own figure is 512 × 1.5
  µs = 0.77 ms, and the remesh it adds runs on workers outside the bracket. It should say "about 0.8 ms,
  under the trigger (estimate, Debug figure)".
- **D2, "Tier B's load is bounded by price".** Sandbox keeps Alt+P, a free printed fill, and every
  existing world is sandbox. It should say "bounded by price in creative only".
- **D3, "That closes Reader C's two duplication paths".** It does for contents, not for parts (defect 3).
- **D3, "a sensor can never empty a vault into the mine".** False through the cell reader (defect 5).
- **D3, "The single transaction is built, so a pocket ↔ store transfer is two in-memory writes in one
  batch".** True at the next session save. Not atomic against the unload path, which it should add.
- **D4, "Recall … keeps the director's identity exact".** True for the director's line, while the matter
  is minted through the drain and the reconcile. It should say "the director's line holds; the drain must
  be bracketed".
- **D4, "`PowerExact` gains … `refill`".** Refill is not a term (defect 5).
- **Every design** prices the quarry and the heap at "1.5 µs a voxel" and labels it an estimate from a
  Debug figure taken before the 2026-09-28 Core fix. The label is correct. Only D1 says the figure is
  pessimistic, and none says it predates that fix.
