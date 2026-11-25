# The ideal machines — plan (2026-09-29)

The top tier of the machine ladder, designed first, for the creative-mode demo. His brief, his data
and his nine taken decisions are in [IDEAL-MACHINES-BRIEF.md](IDEAL-MACHINES-BRIEF.md) and are not
repeated here. **Nothing is built.** This is the planning pass; it ended with the decisions handed back.

**Taken as recommended, all sixty-one and the ten forks (2026-09-29):** *"Yes as recommended please
Claude"*. **IM-0b is built** (2026-09-29, [UNDO-PLAN.md](UNDO-PLAN.md), "The machine drain was
journalled"). It is wider than the split's row: the reconcile's four branches were the same mint, so
the rule is that only a pick's dismantle is journalled. **IM-0 is measured** (the same night, "IM-0 —
measured" below), and he took the 2 m quarry bite. **IM-1 is built** ("IM-1 — the code-level pass" and
"IM-1 — built" below; its eight decisions taken as recommended). **IM-2 is built** ("IM-2 — built"
below): the backpack is a window and consumers are paid by priority. **IM-3 to IM-5b are built, IM-6's pass is
taken, IM-6a is built (2026-11-24), and IM-6b is built (2026-11-25)**: each slice has its row in "The split" and its own sections below.

**How it was made.** As a design panel (the ladder's method):
- five readers mapped the machine code, the plans, the inventory and the store, the creative-mode
  surface, and the materials;
- four designers each wrote the whole design from one angle: engine cost, gameplay and the demo, the
  mod format, physical plausibility;
- three judges scored all four, each through one lens: Core and the identities, CPU, and gameplay and
  Matthew;
- the synthesis (this plan) went to a completeness critic, whose ten must-fix findings are folded in.

The panel's papers (the briefs, the five reader maps, the four designs, the three verdicts and the
critique) are kept as dated snapshots in [panel/ideal-machines-2026-09-29/](panel/ideal-machines-2026-09-29/).
The reader maps are the place to start when building a slice.

The engine design won two lenses and the gameplay design the third. This plan is the engine design's
structure with the gameplay design's loop, plus a graft from each of the other two where a judge showed
it was better. Every figure says where it came from; **most are estimates**, and the ones the plan leans
on are owed a measurement (IM-0) before they are believed.

## His words, the short version

- The data: *"a handover crossover … between your mobile batter and quantum storage … so recharging
  is a non issue same with quantum backpacks and quantum storage containers about the same size as the
  current machine blocks"*.
- *"One pool please Claude you should be able to bankrupt yourself energy wise at the last tech"*.
- *"automated quarries if you can dream up something"*, *"materials … like iron steel etc"*.
- *"happy to have water as an inventory item but you should have some tool or machine that adds it at
  the last tech level"*, *"or removes it from inventory"*.
- *"want automated work? We expect you to at least be on the job site for OH&S reasons"*.

## Eight facts from the code that shaped it

1. **Only the quantum tier's membership exists.** A channel is an integer port. AC, DC, steam and hand
   membership are plan only (M10e/f), and the loader refuses their `via` names. Every machine has one
   `channel` port for power, water and any signal. A machine's `links` are validated and never read by
   the tick. So an extra "inherited down the ladder" has nothing to read yet; the brief's "cheap, since
   membership is already computed" is true for quantum only.
2. **Kinds are C#, and unknown JSON keys vanish.** `MachineKind` is a closed enum of four, with about
   twenty sites that switch on it. `storage` already means joules. Until IM-1, the loader ignored an unknown key
   without a word, so a misspelt `"recal": false` would have loaded with recall on.
3. **The undo can mint, today.** Three facts combine:
   - The machine drain runs with recording on (`Main.cs:4465`), so the destroy that follows a fall
     lands in a fresh undoable step.
   - The journal copies a row's bytes whole (`EditHistory.cs:427`).
   - The reconcile rebuilds a machine whose body is all air from its row, with no debit
     (`MachineSet.cs:446-457`).

   So in the shipped game, a pump destroyed by a fall, then Ctrl+Z before the next edit, then its chunk
   arriving again, comes back free. Anything a design credits at a fall, or holds outside the row's bytes,
   can be doubled the same way. Every design on the panel had one of these holes.
4. **The inventory is voxel counts: unbounded, weightless, and never water.** The `1,767,903,281 L` on
   his hotbar is stone. No identity covers voxels in the inventory; the only printed voxel identity is
   the collapse director's.
5. **Persistence is closer than the brief thought.** The header, the machine table and the basins already
   save in one transaction (`WorldBatch`, M10a-1), so the brief's constraint 4 was stale. What is not
   atomic is the unload path: a chunk leaving the box is written without the session. The header is v7.
6. **Some of the extras half-exist.** The lead-acid pack already recharges from its channel from anywhere
   (there is no dock; the brief's "exists" was wrong). The computer already opens a machine up to 512 m
   away in line of sight with a quantum tool held. F12 already pins every machine, without state.
7. **A sensor can starve a vault.** Collapse part f makes the vacuum cell a signal reader: a signalled-off
   cell leaves its pool. Under one pool, that cell stops paying a vault's bill and the vault dumps. That is
   his *"cut power to the water containers … and deliberately flood the place"*, which collapse decision
   10 kept out on the keystone argument, arriving by another road. Three of the four designs missed it.
8. **The CPU figures are older than the build, and the sun never sets.**
   - The boundary is about 20 ms on the main thread on TajMahal since the 2026-09-28 fix, and no machine
     figure has been re-read since.
   - The panel's per-voxel price (1.5 µs) came from chunk-sized bites; the in-game 1 m cube read 0.42 ms
     a bite (Debug, before the fix).
   - The relay pour walks a ball sized to everything owed, and goes through the `injected` door.
   - Rock weakens under Tier A's head only, and seepage exists only under a lake, so a flooded pit never
     weakens its own walls.
   - There is no night (ladder decision 22), so the only fall in solar income is the weather.

## The shape, in six sentences

1. **At the top tier a channel is an account.** Its cells are one bank and its vaults one store. The
   quantum backpack is a window onto both and holds nothing itself.
2. **What you spend in the hand comes straight out of the bank.** There is no floor. The HUD shows a
   runway, and the rest is yours.
3. **A quantum vault is a machine block that is bigger on the inside, by grade.** It pays a hold bill
   for what it holds, and water costs ten times stone to hold. When the bill goes unpaid it glitches cyan
   through a 10–20 s fuse and dumps: water back into the lake or the workings, blocks as a heap in their
   own colours. While the sun covers the bills a broke channel only stalls; **a storm is when vaults let
   go.**
4. **The quarry digs a box in front of it, into the store, paid from the bank**, and only where you are.
5. **Water is an inventory item at the top.** The siphon takes it and pours it, a vault can take a
   pump's water, and every litre is a printed term.
6. **The six extras are capabilities defined once in C#**, opted into in JSON, and reach as far as each
   tier's membership. The demo is a creative mode: a header byte, a kit and a charged bank.

---

## The forks worth your eye

Ten of the decisions below are real choices, where the judges split or your words can be read two ways.
The rest are engineering, and "as recommended" takes them. **Each fork is marked ◆ in the list.**

- **◆ 13. What you dig by hand goes to your pocket, not the vault.** The vault fills from the quarry,
  from water, and from what you move into it. Both engine judges argued for this: the undo stays one
  inventory, and a bankruptcy dump stays quarry-sized, not excavator-sized. The gameplay judge wanted digs
  to go to the vault, for the stake. Your *"emptying your pack stops being a trip"* presumes a pack that
  fills, and yours never does while pockets are unbounded (you have ruled against a carry limit twice). If
  a survival mode ever bounds them, the rule becomes "what doesn't fit goes to the store", and the
  handover is already built underneath it.
- **◆ 18. Grades live in the vault's ID now**: `quantum_vault/low` to `/god`, labelled "quantum vault
  (god)". Your 2026-09-25 picture put a part inside the vault: *"requires a quantum vacuum storage 'pack'
  … this is a component that goes into the quantum storage container, and should you let it go flat"*,
  with *"your battery tech maybe 19th century but your actual emitter/entangler could be a higher tech
  variation"*. Under one pool the vault has no private pack: **the channel's cells are its pack**, and the
  vault's grade is the emitter. So your mix is a god vault run off old cells, and it lets go when they go
  flat. *(Amended by IM-3b, his word 2026-09-29: for its bill only, a vault carries a minute of it in its own
  charge, and lets go a minute after its cells go flat.)* Parts you install wait for crafting (M10g). Adding them now would open an install door the undo
  cannot see, and the Core judge found it minting in both designs that tried.
- **◆ 23. Water shows on the hotbar**, as its own ID, while the quantum backpack is worn. It is never
  placed by an ordinary right-click; the siphon is its only door. That is taken 8 read literally. The
  other arm keeps water off the hotbar, in the store's list only. *(Amended by IM-6's pass, decision 12, taken
  2026-11-24: water shows as a gauge beside the row, not in one of its nine slots.)*
- **◆ 27. No hand valve on a vault.** Your line *"i am deliberately going to flood that entire valley"*
  was about your pond on `Regions16`, in full: *"… for spread and flood testing purposes"*. R and Ctrl+R
  do that in sandbox. So a vault's `enabled` 0 closes it to deposits and withdrawals and it keeps paying.
  Only an unpaid bill starts a fuse, and collapse decision 10 (deliberate flooding stays out) stands whole.
  *The other arm:* `enabled` 0 starts the fuse and dumps, a flood by hand, which amends decision 10 for the
  hand.
- **◆ 30. The dump's blocks are written straight into the world as a pile at voxel grain**, bottom-up,
  in their own colours, glitching cyan as they appear. That is the grain you chose for the collapse
  pieces, and it costs no landing solves. *The other arm* tumbles them out as collapse pieces landing in
  their own materials. That is more dramatic, but each piece costs a solve from the same frame budget a
  real collapse needs, and pieces in flight are not saved.
- **◆ 32. A big dump loses what the engine cannot carry.** Water that reaches the lake always goes back
  to the lake. Underground, all the world's dumps together may owe at most 10 ML to the water engine at
  once, and any one dump heaps at most 10,000 m³ of blocks (both estimates, owed a measurement); the rest
  **decoheres**, gone, printed and announced. A god vault holds ten thousand times a low one, and a god
  vault's failure underground loses the most. *The other arm* caps every grade at 10 ML, which makes "god"
  mean a long fuse rather than a big vault. Returning everything slowly is not an arm: the cost is the
  total cells, not the rate (1 GL is 64 million cells).
- **◆ 34. Once the fuse burns out, the dump runs to the end.** Power coming back during the fuse saves
  the vault; after it, nothing does. *The other arm* lets power stop a dump half-way, which lets a player
  meter a flood by flickering the power.
- **◆ 45. The quarry's bite is your 2 m cube, if the fixed build can afford it**, otherwise 1 m cubes.
  IM-0's measurement decides it, and all quarries share one budget a boundary either way.
- **◆ 48. No ore this pass.** Ores and smelting are M10h's pass, because an ore field moves every
  recorded measurement and nothing uses ore until crafting. The quarry digs what the world holds. *The
  other arm:* an ore field now (iron and coal first), switched on only in new creative worlds, so no record
  moves.
- **◆ 52. In creative, the debug keys are off**: P, Alt+P, R, Ctrl+R, Alt+R, Alt+G, H and L. Alt+P alone
  would make bankruptcy a keypress, and H would freeze a burning fuse. Every world you have is sandbox and
  keeps them all. *(Amended by IM-6's pass, decision 24, taken 2026-11-24: Ctrl+P and Alt+H are off too, and
  creative turns them off whatever `--dev` says.)*

---

## Decisions

Each one is a recommendation, then its cost, then the trigger that would reverse it. **◆ marks a fork.**

### A. Kinds, extras and the format

1. **Kinds stay C# until M12's pass says otherwise.** A kind is a home for a conserved quantity or a writer
   of the world; everything else is an extra. This pass adds two kinds, `vault` and `quarry`, as named
   exceptions to M10-PLAN D1 (*"a new kind is M12's scripting"*), as collapse part f added `sensor`.
   Whether scripts may ever add a home is M12's to decide, and this pass does not decide it. *Cost:* two
   kinds through the twenty switch sites, each with its own panel branch (the status line's fall-through is
   pump text).
2. **The six extras are one closed C# flag set**, stored as a bit per material and read at one fixed site
   each. A machine opts in with an `extras` object. No extra adds a case to a kind switch. *Cost:* mods
   cannot invent an extra. *Revisit:* a seventh extra asked for twice.
3. **Inheritance is declared in one table per mod, `extras.json`**, mapping each extra to the form it
   takes on each medium. Tables are unioned across mods; the same (extra, medium) pair given two forms is
   refused, naming both files. Medium names are namespaced content IDs and are checked against the media
   the build serves, so a misspelt medium is refused. **The base game ships the quantum row now, and each
   lower tier's row lands with that tier's membership (M10e/f)**: a row that nothing can read would be
   exactly the silent data the strict loader exists to refuse. The rows it will ship are the table in F.
   *Cost:* taken 2 is exercised only at the top until M10e, which would be true either way.
4. **The loader becomes strict.** An unknown key at any level is refused, naming the file and the key,
   with a did-you-mean. Keys starting `$` are allowed (`$comment`). `mod.json` gains a `format` number, so
   a mod written for a newer build fails with one line, not forty. Every key in the shipped files matches a
   loader field by name (checked by the critic; the loader's exact binding is the slice's test). This
   changes how every mod loads, which is wider than the brief's scope, and it is here because a misspelt
   ceiling key that loads silently is a ceiling that isn't there. *Cost:* the ladder plan's example keys
   (`stockVoxels`, `dock`, `conductor`) refuse until the part that reads them lands. *Revisit:* the first
   third-party mod whose players hit version skew; then warn, never ignore.
5. **CPU limits are engine constants, never JSON.** A mod sets game numbers within engine maxima, refused
   above them, never clamped (ladder decision 21's rule). Dump rates, the quarry's budget and the pour
   ball's size are engine constants, printed. *Cost:* no mod can make a faster dump than the engine allows.
6. **The computer's verb is keyed by `"programmer": true`**, not by "non-placeable and not a pack". Today
   any non-placeable item opens machine panels, so water, parts and ingots would. *Cost:* one key.

### B. The one pool (energy)

7. **The quantum backpack is a window with no charge of its own, pinned in its JSON:**
   `"kind": "storage", "carried": true, "placeable": false, "programmer": false` and
   `"extras": { "charge": {} }`, whose quantum form is `window`, with no `capacityJoules`. The validator's
   rule that a storage declares a capacity above 0 exempts the window form. `RechargePack`, Alt+P's
   `FillPack` and any `Give` refuse a window. It rides today's pack slot with charge 0, so it needs no
   header field. A stowed backpack forgets its channel, as a flat lead-acid pack does; the panel sets it
   again. The lead-acid pack is unchanged. *Cost:* a dead bank is a dead tool. *Revisit:* he wants a
   carried buffer, which is two pools again.
   **His note, 2026-09-29** (verbatim): *"dreaming of a lithium ion, li poly or quantum portable(wearable)
   battery/storage backpack"*. The quantum one is this window, built in IM-2. Lithium-ion and lithium-polymer
   are the middle rungs of the carried pack, between lead-acid and the window. **His player slots (2026-10-19,
   M10-LADDER-PLAN, "His design for the player's slots")** split this slot in two at M10g: a backpack slot
   for things and charge slots for energy, **taken the same day**: one on the player for a battery (today's
   worn pack), and one inside the quantum backpack for a quantum battery, which becomes the bank's window. This
   decision's revisit trigger, a carried buffer, fires there in a contained form. Each would be a storage like
   lead-acid, with its own `capacityJoules` and `rechargeWatts`, and would wait on M10g's recipes. They are
   carried buffers, but on the lower rungs, where there is no one pool, so this revisit does not fire.
   **The lithium-ion pack shipped early, 2026-10-12, on his ask** (*"Lead acid is so 20th century"*), seeded by
   `P` as the lead-acid pack is, without waiting for a recipe: `overburden:lithium_ion_pack`, 14.4 MJ (four
   lead-acids, the real density ratio at its cautious end) and 2 kW (full in two hours where lead-acid takes
   four), on the channel class like lead-acid until M10i. The record is in M10-LADDER-PLAN's storage row and
   the worklog. Li-poly is still a rung to come.
8. **A tool's spend debits its channel's cells directly**, last-filled first, between boundaries and never
   inside the tick's run. `PowerExact` needs no new term for it: the rows fall by X and `packTaken` rises
   by X (the Core judge checked this against `MachineTick.cs:404-408`). The debit writes the cells' charge
   directly, never through `MachineSet.SetCharge`, which would also count it as `leaving`. The same draw
   serves the quarry's carve (44) and recall (39), each with its own counter.
9. **At each boundary the bank pays in this order:** every vault's hold bill, in fill order (14), each all
   or nothing; then consumers (pumps, quarries) by priority, then by position. The hand's spend has already
   happened. Nothing is held back and nothing is refused except by an empty bank. A vault whose bill cannot
   be paid in full starts its fuse; the bills paid first are the vaults filled first. *Revisit:* a play
   where the hand starving a vault reads as unfair.
10. **The hold is paid from the channel's `enabled` cells before signal overrides apply.** A sensor can
    starve a pump or stop a quarry; it can never dump a vault, by either road (fact 7). This amends
    collapse f-1's decision 5 (*"a signalled-off cell leaves the pool as `enabled = 0` does"*) for one
    consumer, the hold. f-1 builds as planned, and the amendment lands with the vault. *Cost:* one special
    case in the pool's order. *Revisit:* he asks for his original *"cut power … and flood the place"*,
    which reopens collapse decision 10.
11. **Bankruptcy has no floor, and a runway.** The HUD line reads `BANK 612 MJ · +3.8 kW sun · −104 kW ·
    runway 1 h 38 m · hold 2 kW`. The runway is the bank over a 10 s moving average of the net outflow,
    and shows only while the net is negative; it is amber under 10 minutes and red under 60 s. The tool's
    refusal plays his *"failed — insufficient charge"* when there is sound. `M10:` prints each crossing of
    zero, both ways. *Revisit:* he goes broke without noticing, or finds it nagging.
12. **The failure is a sequence you can watch, and the weather finishes it.** The runway goes amber, then
    red. Consumers stall by priority (the quarry's pin goes amber, then the pumps'). The tool fails. While
    the sun still covers the vaults' bills, that is all: a broke channel stalls and waits. When a storm cuts
    the sun below the bills, each vault spends the minute of its bill it carries (IM-3b), then its fuse
    starts (28), then the dump (30–37). That is a real property of
    the design, and his to like or not: **the mine's weather, not the player's last click, lets the vault
    go.** Every rung is caused by the player, readable in advance, and recoverable slowly: the sun returns,
    the steel pick costs nothing, the heap is your own materials, and the lake can be siphoned back once you
    are solvent. *Revisit:* he wants a broke vault to go in sunshine too; then the hold is paid from stored
    charge only, never from this boundary's sun.

### C. The one store and the vault

13. **◆ What you dig by hand goes to your pocket.** The pocket is today's inventory, unchanged. The store
    fills from the quarry, from water (the siphon and pumps), and from what you move into it. The backpack's
    panel moves stacks between pocket and store; placement draws from the pocket only; the undo sees only
    the pocket. *Cost:* the store holds little of what you dug, so the stake of a dump is the quarry's
    spoil, your water and what you banked. *The other arm:* while the backpack is worn, digs credit the
    store. It needs the undo cleared on every bind and unbind, and makes every bankruptcy an
    excavator-sized heap. *Revisit:* survival bounds the pocket, if it ever does.
14. **The store is every vault on the channel.** There is no limit on vaults. Each holds its own contents in
    its own row, bills for them and fuses on its own; the store is a view over them, never saved. Deposits
    fill the vault with the lowest bill a cubic metre first (the highest grade), then by position, and
    withdrawals take from the last filled. So the store's bill is the cheapest it can be, and moving a vault
    changes nothing. *Revisit:* a play wanting one heap.
15. **A vault is one 1 m machine cell**, 64 voxels (*"about the same size as the current machine
    blocks"*). Blocks and water share its inside volume, a voxel taking 15.625 L.
16. **The hold bill follows what it holds, and water costs ten times stone** (an estimate): water m³ × the
    grade's water rate + block m³ × a tenth of it. An empty vault costs nothing and cannot fuse. Paid all or
    nothing per vault, as a new printed `PowerExact` term, `held`. Holding litres bound for the water engine
    is what costs; the CPU and Core judges both preferred this to a flat rent. *Revisit:* the bill reads as
    an unnoticed tax or as a treadmill; it is one number per grade.
17. **Contents never leave the books with a row.** Every path that removes a row, enumerated from the code:
    - **The pick.** A vault body that holds anything reads as too hard to every tool unless its sisters on
      the channel have room for its contents, asked before the carve, never at the drain (a refusal at the
      drain would leave a holed vault with its voxels credited). With room, the contents move to the
      sisters at the drain, the row is emptied, and **only then** does `Remove` journal its copy, so Ctrl+Z
      re-adds an empty vault.
    - **At-range tools and the quarry** skip a vault that holds anything, so a stray excavator bite 500 m
      away cannot flood the lake.
    - **A fall.** A vault that holds anything becomes a **wreck**: a flag in its saved state. Its body goes
      to air (or rubble in the sweep mode), and it keeps dumping from its cell's neighbours until empty;
      then the row goes.
    - **The reconcile at arrival.** Its two remove branches, "returned at arrival" (`MachineSet.cs:458-467`)
      and "destroyed at arrival" (`:472-476`), never remove a vault that holds anything or a wreck. Such a
      row becomes a wreck instead, and the reconcile never rebuilds a wreck's body.
    - **The undo** refuses to remove a vault that holds anything, as a pre-flight check with a new
      `UndoOutcome`, never inside `Apply` (which removes rows before writing voxels, and would half-apply).
      The step stays, as an out-of-stock step does.
    - **Restore** never clamps contents. A vault over capacity at open (a grade retuned in JSON) keeps what
      it holds, refuses deposits and prints `overfull`. This differs from joules, which clamp at restore,
      and deliberately.
    - **A vault whose codec version is unknown** is kept opaque and inert, and reads as holding something
      to every rule above.
    - **`SetPort` to another channel** moves the row, contents and all, and moves nothing out of the books.
      A fusing vault moved to a solvent channel waits at its next paid boundary and goes out once the new
      channel's spare has refilled its minute (IM-3b); a dumping vault or a wreck keeps dumping.
18. **◆ Grades live in the vault's ID**: five files, `quantum_vault/low` to `/god`, labelled with his words
    in brackets. See the fork above. *Cost:* five permanent IDs and one texture layer (a tint each later).
    *The other arm:* an entangler part and a cell part in slots (ten IDs, an install verb, and an undo
    refusal for rows with parts that must land first). *Revisit:* M10g's parts pass.

The grade table. **Every number is an estimate for his tuning.**

| Grade | Inside | Water held full | Hold, full of water | Hold, full of stone |
|---|---|---|---|---|
| low | 100 m³ | 100,000 L | 2.5 kW | 250 W |
| medium | 1,000 m³ | 1 ML | 10 kW | 1 kW |
| high | 10,000 m³ | 10 ML (his figure) | 40 kW | 4 kW |
| mythical | 100,000 m³ | 100 ML | 160 kW | 16 kW |
| god | 1,000,000 m³ | 1 GL | 640 kW | 64 kW |

Each step holds ten times more for four times the bill: dearer to keep, cheaper per cubic metre. For scale:
a solar panel is 500 W, a vacuum cell 5 GJ, and a relay pump at full rate 100 kW. A high vault holding 500 m³
of water costs 2 kW, half of eight panels in sun and four times what they give in a storm. A 5 GJ cell holds a
full high vault of water for about 35 hours with no sun; a full god vault for about 2 hours (arithmetic).

19. **The store's voxel identity is built first**, because none exists, and it is global across channels:
    `held + owed == entering − leaving + deposited − withdrawn + quarried − heaped − decohered`.
    `entering` and `leaving` are restore and the removal of an emptied row, and are zero in play by 17. It
    holds over a thousand random operations **with the undo among them**. Water has its own line (24).
20. **The vault's codec bytes are its state; there is no live object** that is encoded at save. One codec
    owns the whole blob: version, contents, water, fuse milliseconds left, phase, owed amounts, wreck. It is
    strict: an unknown version is kept opaque and never read as empty. The fuse's length is drawn from a
    seeded source and saved as milliseconds left, so quitting and reopening cannot reshape it.
21. **The stock UI is a section of the machine panel**: a capacity bar, one row a material with its amount,
    water, the bill, the state (`HOLDING`, `FUSE 12 s`, `DUMPING — owes 3.1 ML`), and the to-pocket and
    to-store verbs. It opens on a vault, and on the backpack (the computer's right-click on nothing). It is
    M10b's deferred stock tab, with something to show at last. The HUD adds `store 68%` beside the bank line.
    **His design for M10g's screen (2026-10-19)** puts the store's stock in `E`'s inventory screen as a second
    box, and a container aimed at as a box beside pocket-and-store; IM-6 builds this section as planned and
    the screen uses the same moves (M10-LADDER-PLAN, "His design for the screen's boxes").
    **His warranty line goes on the vault** (2026-09-29, during IM-3's build, verbatim): *"While all care is
    taken please understand that warranty in case of battery discharge or negligence, is not implied or
    otherwise available under these circumstances."* It lands with IM-6's stock section, at the foot of a
    vault's panel. The quarry's README gets his OH&S line the same way (decision 47).
22. **The ladder's bottom rung ships: a plain `crate`**, his *"the ability at lower levels to construct
    storage containers for your stuff"*. It is the vault kind with no hold, no channel, no fuse and no dump,
    holding its outside volume in blocks only, opened with the computer. Once the vault exists it costs a
    JSON file and a texture. The middle rungs wait:

| Tier | Container | Holds | Store view | Bill | Grades | Litres | Waits on |
|---|---|---|---|---|---|---|---|
| hand | `crate` | 1 m³, blocks | none | none | none | never | ships now |
| steam | bin | 1 m³, blocks | none | none | none | never | survival and M10g |
| DC | locker | 1 m³, blocks | none | none | none | never | survival and M10g |
| AC | bay | 1 m³, blocks | its cable component | none | none | never | M10e, survival |
| quantum | vault | by grade, blocks and water | the channel | by fill | five | yes | this plan |

### D. Water in the inventory (taken 8)

23. **◆ Water is an ID used only by the store**, `overburden:water`: `placeable: false`, not a programmer, a
    fluid. It shows in the hotbar's last slot while the backpack is worn, and the pocket refuses it by
    every path: `--stock=`, `P`, the to-pocket verb. *(Amended by IM-6's pass, decision 12, taken 2026-11-24:
    it shows as a gauge beside the row, never selected and taking none of its nine slots, because a slot shows
    the pocket's amount and the hand can't hold water to pin it.)* Tier B still holds anonymous
    millilitres, and nothing in the fluid engine reads this file. *Cost:* one permanent ID, the one
    MATERIALS-NOTES option B would need anyway. *Revisit:* he wants water in the pocket.
24. **Four doors, each a twin counter, never through `Add`, `Emit` or the relay's `PourPending`.**
    - **The siphon's left click** takes up to a 1 m cube of water at the crosshair, at range: at most 1,000 L,
      64 cells.
    - **The siphon's right click** pours up to 1,000 L from the store at the crosshair.
    - **A pump into a vault.** A vault joins its channel's outlet ring with a `weight`, and litres a pump
      delivers to it are `vaulted`, not emitted. The pump stalls `full` only when every entry in its ring
      refuses. *(Superseded by IM-4's pass, decision 11: there is no `full`; the stall is the first refusal
      met, and a full vault reads `NoRoom`. Built in IM-4b.)* Priority orders consumers; the ring orders a
      pump's outlets; the two are unrelated.
    - **The dump** (30–37).

    The lines: `pumped == emitted + vaulted` across both tiers every boundary. Tier B's line gains
    `+ poured + dumpedB − siphoned`. Tier A prints `siphonedA`, `pouredA` and `dumpedA`, seam-checked from
    both ends (its own identity has never been evaluated, and this plan does not claim more). The store's
    water: `held + owed == entering − leaving + vaulted + siphoned − poured − dumpedA − dumpedB − decohered`.

    **This amends, out loud and for quantum only:** ladder decisions 6 and 22 (the bucket) and 13 (the tank
    on the row); M10-PLAN D5 (*"nothing is ever held by a machine"*); and M10-PLAN D10 in both halves
    (*"no in-flight term to hide a litre in"*, and *"the world's water total is a constant of the machine
    layer"*, which the store and decoherence both break, each with a printed term). The spill at an outlet
    stays rejected: a full store refuses.
25. **Taking costs 1,000 J a litre; pouring is free.** A round trip is exactly one relay pump, so the siphon
    is never a cheaper pump. It is the fastest way to go broke: at an assumed 0.25 s cooldown, held on Q it
    takes 4 m³ a second and draws 4 MW (the measured Q rate for the miner was 4.2 a second against a nominal
    10, so a held rate is an estimate). A free pour lets a quick player shrink a dump during the fuse, but
    only by 0.4–0.8% of a full high vault (4 m³ a second over 10–20 s against 10,000 m³). *Revisit:* the free
    pour gets used as a weapon, or 1,000 J/L makes a second flood unwinnable.
26. **The siphon's mechanics.** A tool on T, sorted after the rock tools (a tool with `takes` sorts last).
    It needs the quantum backpack; with none, or with the lead-acid pack, it takes nothing. A click only
    enqueues. The joules, the store and the tier all move together at the next boundary, with the relay's
    pending pours and before the machine tick, where the fluid's ownership has cleared; a quit before then
    moves nothing. It takes by the pump's
    intake rule (a Tier A lake's basin in whole litres, otherwise Tier B cells) and pours by the outlet's
    delivery rule.
    **His note for IM-4's pass** (2026-09-29, verbatim): *"some sort of fluid capture gun that can take out
    chunks of water at sizes to match the quantum miner/tunneler/excavator"*. So the siphon's take comes in
    the quantum tools' sizes: the miner's 1 m cube (24's 1,000 L), the tunneller's 2 m cube (8,000 L) and
    the excavator's bite. Priced at 1,000 J/L, the excavator's size is where 25's 4 MW on Q grows, so IM-4's
    pass prices each size.

### E. The fuse and the dump

27. **◆ Only an unpaid bill starts a fuse.** The vault is never a signal reader, and its `enabled` 0 closes
    it to deposits and withdrawals while it keeps paying. Collapse decision 10 stands whole. *The other
    arm:* `enabled` 0 set by hand starts the fuse, a flood valve, amending decision 10 for the hand.
28. **The fuse is 10–20 s, leaning toward 10 the further the channel falls short**, like his collapse fuse
    (*"ideally the time would be from 10-20 seconds but randomly"*). The body glitches cyan, and the HUD reads
    `VAULT LOSING CONTAINMENT — 14 s`. ~~A boundary that pays in full disarms it.~~ **Amended by IM-3b (his
    word, 2026-09-29):** a fuse lights once the vault's own minute is spent, waits on each boundary the bill
    is paid, and goes out only when the spare has refilled the minute. As first written, a cell holding the
    unpaid pool put it out one boundary in twenty, and a vault held on 5% of its bill for ever. It counts
    boundary milliseconds, so a late boundary is a slower fuse, never a wrong one.
29. **The visual rule: cyan is entanglement at work, black is gone, colour is yours.** Collapse rubble stays
    black (*"you aint getting that back"*). A dump comes back in colour (*"mine to get it back"*).
30. **◆ The dump's blocks are a pile written straight into the world at voxel grain**: bottom-up first, then
    nearest, into dry loaded air around the vault, never into a machine cell or a wreck's cell, in their own
    colours, glitching cyan as they appear. It is written after the dump's water has landed, so it never
    displaces the player's own water. Recording is off, and the undo drops the steps that touched each chunk
    written. The site search is spread over boundaries and bounded across all vaults. With no room within 32
    m, it waits, printed `no room`. A block written with nothing under it may fall and land as rubble, which
    the director's line prints; bottom-up order makes that rare. *The other arm:* collapse pieces landing in
    their own materials. *Revisit:* his look at the pile.
31. **The dump's water goes where an outlet's would.** A vault's mouth is the cell above its top face, as an
    outlet's is. A mouth in a lake, or on the analytic ground inside the watershed window, sends the water to
    Tier A over about 10 s (one twin counter a boundary, at any size), so the lake visibly rises. That applies
    loaded or not. Otherwise the water goes to Tier B through a Core pour ball that calls its own twin, sized
    to the boundary's budget (never to everything owed), with the lake ceiling covered first. A vault that is
    unloaded underground, or off the window, owes its water until it can pour.
32. **◆ Ceilings, and decoherence.** Water to Tier A has no ceiling: a lake is one number. But a risen lake
    reaches the workings through portals at 30 L/s a portal voxel, so a big surface dump floods a mine by the
    ordinary physics, bounded by the mine's volume. Underground, **all dumping vaults together may owe at
    most 10 ML to Tier B at once**, and **any one dump heaps at most 10,000 m³ of blocks**, on the surface or
    below. Whatever is over a ceiling is `decohered`: gone, printed on the store's line, and announced on the
    HUD. The 10 ML is measured (IM-0: a 10 ML pour held 2 Hz on the fixed build, at 385 ms of tick wall
    and about 16 ms of main thread a boundary, so heavily); the 10,000 m³ is still an estimate, for IM-5's
    bench. *Revisit:* the fixed build is measured able to carry more.
33. **One budget across every dump, and its own bracket.** At most about 3,200 Tier B cells (50 kL) and 512
    voxels a boundary across all vaults together, whole units, round-robin by position with the remainder to
    the first. They aim at 5–8 ms of added boundary while a dump runs. The water half is now measured:
    IM-0 read insertion at 0.5–1.1 µs a cell, so 3,200 cells is about 2–4 ms. The heap half waits for
    IM-5's bench. At those rates, 10 ML of water pours in about 100 s and a
    10,000 m³ pile takes about 10 minutes; the demo's 700 m³ of spoil takes about 45 s (arithmetic). The dump
    gets its own `dump` term on the boundary line, never folded into `machines` or `pour`.
34. **◆ Once the fuse burns out, the dump runs to the end.** While dumping, the vault is not a store:
    deposits go elsewhere, and the backpack sees its sisters only. Power returning does nothing. That is the
    dead state a bounded failure needs, and nobody can meter a flood by flickering the power.
35. **An unloaded vault's dump waits.** Rows tick unloaded, so a vault can go broke with nobody near it.
    Water whose mouth is on the analytic ground still goes to the basin. Everything else is owed in the row
    and released when the chunk arrives: you walk into your own dump. That is taken 9 applied to vaults.
36. **A chunk written by the heap, the dump's ball, the quarry, an outlet or the siphon's pour carries the
    session when it unloads**, once per unload batch that holds such a chunk. Otherwise a pile written into a
    chunk that unloads, then a crash before the next autosave, leaves the pile on disk and the vault still
    owing it: a duplicate. This is a new class, the opposite of mining's (mining loses the credit; this
    doubles it), and it is engine-wide. *Cost:* a session write (17–100 ms, the autosave's measured cost)
    on some unloads; the slice prices it.
37. **The dump's order of work at a boundary:** Tier A first (O(1)), then the Tier B ball, then the pile into
    whatever is still dry.

### F. The extras, one by one

| # | Extra | At the top | AC | DC | Steam | Hand |
|---|---|---|---|---|---|---|
| 1 | sense | f-1's collapse sensor, plus `senses: "power"` | over its cable component | over its copper | a bell down the shaft that switches nothing | none; the tint is the warning |
| 2 | recall | a machine a fall takes returns to the pocket while its channel is solvent | none | none | none | none, deliberately |
| 3 | remote | the operations board | its own cable component, from a station | none | none | none |
| 4 | charge | the window (7) | a dock (M10f) | a dock (M10f) | none | none |
| 5 | priority | a port, higher paid first | over its component | position order | position order | none |
| 6 | telemetry | state on F12's pins | state on pins | state on pins | a pin without state | a pin without state |

Everything below quantum waits on that tier's membership (M10e/f). **The siphon, water in the store, the
item window and the quarry are quantum only.** Costs a boundary: sense, f-1's lookups plus one comparison a
power sensor; recall, zero, plus a lookup a released voxel; remote, O(rows on the channel) of label writes a
boundary while the board is open; charge, zero; priority, zero (sorted when rows change); telemetry, zero,
plus O(rows) at the map's rebuild.

38. **Sense.** f-1 exactly, plus one `senses` value, `power`: it signals while its channel's stored charge on
    `enabled` cells, read at the last boundary (never the live pool, so a signal cannot latch itself), is
    below a `below` port, a percentage of those cells' capacity, default 10. It is the player's own cut-off
    before bankruptcy, and the only protection the design allows. The quarry and the pump are readers; the
    quarry's `onsignal` defaults to 1 in its own JSON (f-1's 0 is the shipped readers' default). **Both
    sensors raise the one boolean a channel**, so a pump set to `onsignal` 2 (run only while signalled, an
    emergency pump) also runs while the bank is low. That is a stated cost, not a feature. *Revisit:* a play
    wants them apart; then a `listens` port chooses. **A small amendment to f-1:** a sensor whose own chunk is
    unloaded reads false without looking, which bounds what sensors cost on a server (at radius 4, f-1's
    lookups reach about 1,460 a sensor a boundary). It lands in IM-9. A water-level sensor waits for a play
    that wants one.
39. **Recall, the rule that doesn't mint.** At a fall, the director asks a seam about each released voxel.
    The seam answers yes when the material has the recall bit and a draw of 3.2 kJ from the row's channel
    succeeds, taken at once by the window's mechanism (8) and counted `recallPaid`. A yes is left out of the
    piece and counted `recalled`. At the drain, a row where `Lost == Carved + Recalled` credits
    `cleared + Recalled` to the pocket, which is 64 minus what a pick already took. **Recording is off only
    for the drain's destroy branch (`Lost > Carved + Recalled`) and its recall branch (`Recalled > 0`)**; a pure
    pick's dismantle stays undoable. It works in both collapse modes. The director's line becomes
    `released == landed + in flight + lost + recalled`. A vault does not recall; it wrecks (17). A recalled
    vacuum cell comes back as a count, and its charge leaves as today's dismantle's does. A bankrupt channel
    recalls nothing, so **bankruptcy strips your protections one at a time, and recall is the last to go.**
    Whether a blast recalls is his call, with TNT's six.
40. **Remote: the operations board.** The computer's right-click on nothing opens the backpack's panel, which
    gains a list: one row per machine on its channel, with name, bearing and distance, state in words
    (`PUMPING 100 L/s`, `STALLED — unpowered`, `VAULT — FUSE 12 s`, `OFF SITE`), and `enabled` and `priority`
    where declared. Click a row for its full panel, at any range, loaded or not. The bank line sits on top.
    It refreshes at the boundary, only the rows that changed.
41. **Priority: a port 0–9, higher paid first, ties by position.** Every shipped machine defaults to 5, so
    equal priorities reproduce today's order and no M10 oracle moves; the quarry defaults to 3, so it starves
    before the pumps. Vault bills are not in it; they are paid first (9). The docs' "placement order" is
    corrected to position order (X, then Y, then Z) in the same slice. *Revisit:* M10d's storm play prefers
    the pumps nearest the water.
42. **Telemetry: F12's pins gain a state**: running in the material's colour, stalled amber, unpowered red,
    a fusing vault blinking red, plus one legend line. Pins stay for every machine at every tier; only the
    state is new. It must pass the map's bearing oracle.

### G. The automated quarry

43. **A placed machine that digs a box in front of it.** Its facing is horizontal, taken from the look
    direction at placement (the first reader of `Facing`). The box starts at the cell in front of its front
    face, its top layer level with the quarry's top, and is dug downward, top layer first, in a snake: the
    way a real pit is benched. The box's `width`, `length` and `depth` are ports (2–16 m, 2–16 m, 2–64 m),
    and its cursor is in its codec. `enabled` defaults to 0, so it never digs the moment it lands.
44. **It pays for what it carves.** At the boundary, after the vaults' bills and higher priorities, the tick
    allots it the bites the bank can afford. The carve runs on the frame after the boundary, in its own
    `quarry` bracket, and pays for exactly the voxels removed, drawn at once by the window's mechanism and
    printed as `dug` in `PowerExact`. A bite that found its chunk gone, or the bank spent in between, is
    skipped and costs nothing. 50 J a voxel, the quantum tools' price. Its spoil goes to the store as
    `quarried`, credited as itself; a full store stalls it, and it never spills or destroys.
45. **◆ The bite is your 2 m cube if the fixed build can afford it**, and all quarries share one budget a
    boundary, round-robin, as an engine constant. The 2 m arm, if IM-0 reads a 2 m cut under 1 ms mean and 5
    ms at worst: one 2 m bite (512 voxels) a boundary across every quarry, at most 51 kW. The 1 m arm
    otherwise: four 1 m cubes (256 voxels) a boundary, at most 26 kW. Under a lake, every 2 m bite also
    exceeds the seep tracker's 64-edit burst and rescans its chunk (0.56–1 ms, Release). The budget is what
    makes taken 9 a server bound: the loaded box bounds where quarries dig, and the budget bounds how many can.
    A second quarry makes each slower, never the game. **Settled: the 2 m bite** (2026-09-29, *"go with the
    2m bite for the quarry"*). IM-0 read a 2 m cut at 0.42–0.53 ms mean on the fixed build (~294 voxels a
    bite, 4.2 remeshes). Its worst, 7.4–8.0 ms, failed the 5 ms line, but the 1 m cube's worst was as
    high (8.5–8.8 ms), so the worst is an outlier both arms share and cannot tell them apart. The budget
    is one 2 m bite a boundary across every quarry.
46. **It never digs a machine body**, its own or another's; a pump in the footprint is left on a pillar. It
    skips a vault that holds anything, skips air with a scan cap of 64 cells a boundary, and leaves anything
    harder than its threshold standing.
47. **It stops, and says why:**

| Stall | Cause |
|---|---|
| `done` | the box is dug to depth |
| `off` | `enabled` is 0 |
| `signalled` | a sensor on its channel, with `onsignal` 1 (its default) |
| `unpowered` | the bank cannot pay for a bite |
| `full` | the store has no room |
| `flooded` | water over the next bite: one read, and the demo's loop (the quarry needs a pump, the pump needs the bank) |
| `OFF SITE` | the next bite's chunk is unloaded: *"want automated work? We expect you to at least be on the job site for OH&S reasons"* |

**Its writes are the world's.** They run with recording off, and the undo drops only the steps that touched a
chunk it wrote, so Ctrl+Z still works away from the quarry. **It fires M10-PLAN D3's trigger by
construction**: a bite every half second, at a boundary that runs late under load. The gameplay design reads
it as a stamp mill's beat; it is his eye. The README gets his OH&S line when the quarry ships.

**The quarry and the mine's physics.** Its box is a basin the rain fills, and under a lake a room the seepage
finds: a 16 m box in stone under 10 m of head takes about 15 L/s (arithmetic on the shipped seep scale), 15% of
one relay pump, for as long as it stands. **The hole, not the dig, is the bill**, as in every real mine. A
quarry placed underground makes a room: a dry 16 m room's roof drops a plug and holds (M9a measured a
20-voxel plug); under a lake it falls (M9a: 3,028 voxels under 5 m of head). A collapse sensor on its channel stops it. Rubble that falls into the pit is dug and credited as
rubble.

### H. Materials

48. **◆ No ore this pass.** An ore field moves every fixed-seed oracle (r6's quads, the keystone's 18,718),
    seams against old worlds, and needs C# texture entries and three hard-counted tests changed. It is M10h's
    own pass, and when it comes it follows the relief precedent: a header setting, off in every record, on
    only for new worlds. The quarry yields what the world holds: grass, dirt, stone, clay and rubble. In a real
    mine the waste above the ore is called the overburden, so **the quarry is an overburden machine**, a line
    for the card. *The other arm:* an ore field now, iron and coal first, on only in new creative worlds.
49. **No bill of materials in JSON this pass.** Recipes are M10g, and a bill nothing reads is exactly what
    the strict loader exists to refuse. For that pass: the vault shell and the quarry frame are stainless
    (ladder decision 19), and the physics design's eras are flavour text for the grade names, never the IDs
    (cells: lead-acid 1859, nickel-iron 1901, lithium-ion 1991; emitters: spark gap, valve, transistor,
    maser, entangler). "Grade" in mining means ore richness, so ores will want "assay" when they ship.
50. **Ten new material IDs, none placed by worldgen**, so r6 holds as it did for clay and glass: the
    backpack, five vault grades, the crate, the quarry, water, and a power sensor. The siphon is a tool. Three
    tests hard-count content and change: `ShippedContentTests.cs:209` (machine materials), `:242`
    (non-placeable materials) and `CollapseTests.cs:1044` (IDs appended to the v3 fixture).

### I. The creative mode and the demo

51. **A mode is a byte in header v9** (v8 until 2026-10-19, when alpha session C took v8 for the hotbar's slots): sandbox (every existing world, today's game exactly), creative, and
    survival reserved. `--mode=creative` applies only when a world is created, like `--seed`, and is warned
    and ignored on an existing world. A profile and a `--nosave` run have no world, so they are always sandbox
    and r6 holds; a headless run that creates a named world with `--mode=creative` is creative, which is how
    the oracles reach it. *(Amended by IM-6's pass, decision 21, taken 2026-11-24: a profile with `--world=`
    does make a world (`Main.cs:2540-2542`), so `--mode=creative` applies to any run that creates one, profile
    or not; `--nosave`, and a profile without `--world=`, are the runs with no world.)* **Core never reads the mode**; no rule of water, power or support differs by mode.
    Command-line flags (`--stock=`, `--nogate`) are his developer doors and are honoured in any mode. ~~M10g's
    hotbar assignment moves to v9.~~ The hotbar's slots shipped first, at v8 (ALPHA-PLAN, session C).
52. **◆ Creative turns the debug keys off; sandbox keeps them all.** P, Alt+P, R, Ctrl+R, Alt+R, Alt+G, H and
    L are off in creative. *(Amended by IM-6's pass, decision 24, taken 2026-11-24: Ctrl+P, built 2026-11-04, and
    Alt+H are off too, and creative turns them off whatever `--dev` says, since `start.cmd` passes it.)* Fly stays in every mode: it is the only way out of burial until M11. In sandbox, P
    also seeds the new machines.
53. **A creative world starts with a kit**, from `kits/*.json`, a content section unioned across mods (not a
    path special to the base game), granted once at creation into the pocket as real counts, printed as
    `granted` on the `M6c:` line so the undo holds. The kit (estimates): two vacuum cells, eight solar panels,
    four pumps and four outlets, one vault of each grade and a crate, two quarries, a collapse sensor and a
    power sensor, the computer, the quantum backpack worn on channel 0, and 4,096 brush cells (64 voxels
    each) of stone, clay and glass. **The first vacuum cell placed in a creative world arrives holding 1 GJ**,
    once, printed as a `granted` term in `PowerExact`; a header flag records that it happened. Power stays
    real: `--nogate` switches the economy off and is never creative's source. The kit grows slice by slice:
    each slice adds what it builds.
54. **The palette, a screen that grants anything, waits for M10g's inventory screen on E.** A kit needs no
    new screen, and gets the demo playable a slice sooner. *Revisit:* he wants more than the kit in play.
55. **The first minutes**, on a new creative world by a lake, started with `--clock=` set so a storm arrives
    around minute five. Every figure is an estimate for his first play to move. The bank starts at 1 GJ;
    eight panels give about 4 kW in sun and 0.5 kW in a storm.

| Time | Beat | The lesson |
|---|---|---|
| 0:00 | Spawn walking. The card: *"You run a mine. Water wants in; the rock wants down. Everything you own runs off one bank. Don't go broke."* | |
| 0:30 | Place the cell, the panels and a high vault on channel 0. `BANK 1.0 GJ`, no runway in sun. | |
| 1:00 | The excavator takes a pit at the lake's edge: 512 m³ for 1.6 MJ, and the bank barely moves. | Rock is nearly free. |
| 1:30 | Digging toward the lake, the wall under its head tints and gives, and the lake breaks in. | The water finds you. |
| 2:00 | Seal the breach with clay. The siphon drinks the pit into the vault, about 500 m³ in two minutes for 500 MJ; the runway goes amber while Q is held. The vault now bills 2 kW, and the sun pays it. | Water is what costs. |
| 4:00 | The quarry goes on the rim with an 8 m box and digs every half second. The board lists everything. The pit floods again; the quarry stalls `flooded`; pumps go in, into the vault or over the hill. | Depth is a water bill. |
| 5:00 | **The storm, and the fork.** Greedy: hold the siphon or the excavator, and the bank hits zero in about two minutes. Pumps and the quarry stall, the tool fails, and with the sun at 0.5 kW against a 2 kW bill the vault spends its own minute (120 kJ, about 80 s at that deficit; IM-3b), then glitches cyan, `LOSING CONTAINMENT — 12 s`, about a minute and a half after the bank hit zero (arithmetic). The lake rises over ten seconds, and the spoil piles up around the vault in colour. Careful: at amber he turns the quarry off from the board, and the vault holds through the storm. | Bankruptcy is a feature. |

**The honest limit:** the drama needs a lake and a storm. Rock weakens under Tier A's head only, and a vault
lets go only when the sun can't pay it. A player who digs on a dry ridge in sunshine gets a quiet five
minutes. The gameplay design's answer is a template world he authors by playing, with a gold seam under the
lake as the lure; it is a later slice (58).

### J. What waits

56. **The lower tiers' inheritance** waits for their membership (M10e/f), and lands in `extras.json` with it.
57. **Parts, crafting, recipes, the lower containers past the crate** wait for M10g and survival. **Ores**
    wait for M10h.
58. **The demo template world** (the gameplay design's: a lake, a gold seam placed by hand under its edge, a
    storm on the clock) waits until the kit world has been played, and needs a way to seal a sandbox world as
    a creative template. **Handing the demo over** needs an export preset, the SQLite natives verified, and
    his phonk shipped or the collapses silent. Its own slice.
59. **Tier B saturation**, a flooded pit weakening its own walls, is named and not built. It is an M9 pass.
    *Trigger:* he says a flooded mine that never weakens reads wrong.
60. **Multiplayer** is in TODO with its own full pass: each player gets their own channels (today channels
    0–15 are per world, so a second player on channel 0 spends your bank), machines work only inside the
    players' loaded boxes, and a per-player machine cap is a trigger at 1 ms of row tick.
61. **Lower-tier miners and conveyors** keep their own pass; the brief's reasoning stands for them.

---

## The split

Each slice is a session or less, Core and tests cut from the wire. **Collapse f-1 and f-2 land first or
alongside**; nothing here blocks them. IM-1 moves f-1's `onsignal` under the sense extra without changing its
name, bounds or default.

| Slice | What | Test or oracle |
|---|---|---|
| **IM-0** | **Measurements on the fixed build, no code.** On a dated copy of `retired-TajMahal-2026-09-29` (`--world=probe-…`, `--at=-268,-5,1284`, `--fluidnear=2 --fluidjob=1`): `machines` and `boundary worst`. Then `--profile --entangle --nogate --pitch=-60 --holdq` and `--entangletunnel --nogate --pitch=-60 --holdq`, dry and with `--fillbasin`: `swing ms` and remeshes a swing. Then `--injectlitres=1000000` and `=10000000`: `pour`, a minute of tick wall, and the surface install in a windowed run. Also: add `--entangletunnel` to COMMANDS.md, where it is missing. | The numbers decisions 32, 33 and 45 wait on. |
| **IM-0b** | **The live bug (fact 3), a fix to shipped code.** The drain's destroy branch runs with recording off, for every machine. | A pump destroyed by a fall, drained, then Ctrl+Z re-adds nothing, and the next arrival rebuilds nothing; a pure pick's dismantle is still undoable. |
| **IM-1** Core (amended by its pass, below; **built 2026-09-29**) | The strict loader; `extras`, the flag set, the per-material bit, `extras.json` (quantum row), its union and media check; `programmer`. | Every shipped file loads; a misspelt key at each level is refused, naming it; two mods giving one (extra, medium) pair are refused; a misspelt medium is refused; a mod with a newer `format` fails in one line; water and parts no longer open a panel; a three-mod fixture loads the same in every order; rows saved under f-1 restore unchanged. Mutation: disable the refusal, and the misspelt-key fixture goes red. |
| **IM-2** Core (**built 2026-09-29**) | The window (7, 8): the backpack's JSON, the validator's exemption, `RechargePack`, `FillPack` and `Give` refusing a window, the direct draw; the priority consumer list; `packInitial` printed. | `PowerExact` exact over a thousand random bites and boundaries; the worn backpack's saved charge is 0 after a hundred boundaries, Alt+P and a save; a draw inside `Run` throws. Mutations: drop the `packTaken` credit, red; write the draw through `SetCharge`, red. Equal priorities reproduce position order. |
| **IM-3** Core (amended by its pass, below) | The `vault` kind and the crate; the codec; the store view and fill order; the bill (`held`) and its order before signal overrides; the fuse; every row-removal rule of 17; the store's voxel identity. | Identity exact over a thousand random operations, the undo included; a signalled cell never unpays a hold; two vaults both bill; a save during a fuse resumes at the saved milliseconds; a wreck survives a reopen and is never rebuilt; both reconcile branches keep contents; an unknown codec version refuses the pick and the undo; `overfull` at restore; a refused pick carves nothing; the undo refusal is a pre-flight; `SetPort` moves contents with the row. Mutations: journal a full row, red; bill with no `held` term, red. |
| **IM-4** Core (its pass, 2026-09-30, splits it into 4a–4c, twelve decisions taken; **4a, 4b and 4c built**; 4c kept today's lake rule at his word, displacement is its own pass) | Water's doors: the ID, the pocket's refusal, the siphon's queue and rules, the vault in the outlet ring, the twins, `pumped == emitted + vaulted`, the store's water line. | M8's oracle unmoved with no vault; a siphon take then pour restores a basin exactly; a click then a quit moves nothing; a take on a Tier A lake draws the basin; ~~the pump stalls `full` only when every ring entry refuses~~ a full vault reads `NoRoom`, the first refusal met (the pass's decision 11). Mutation: drop `vaulted`, red. |
| **IM-5** Core (its pass, 2026-09-30, splits it into 5a, the water, and 5b, the pile; sixteen decisions taken as recommended) | The dump: the pile writer, the Tier A release, the Tier B ball with its twin sized to the budget, the ceilings and decoherence, the global budgets, owed while unloaded, the unload batch's session, the dead state; a heap-writer bench arm. | A fixture room receives every voxel once; a pile into a chunk that unloads, then a kill with no session save, then a reopen, counts every voxel once; the unloaded arm releases at arrival; N vaults dumping share the budget exactly; the Tier B ceiling decoheres the excess; power returning after the fuse does nothing; Ctrl+Z cannot touch the pile; Tier B exact, Tier A's counters seam-checked. Mutation: route the dump through `Add`, and the seam goes red. |
| **IM-6** Wire (its pass, 2026-11-24, splits it into 6a, Core; 6b, the wire and the vault in sandbox; and 6c, creative; twenty-six decisions taken as recommended 2026-11-24; **6a built 2026-11-24, 6b built 2026-11-25**) | Header v9, the mode and `--mode=creative`; kits and the first cell's grant; the debug keys off in creative; the card; the HUD bank line, runway and store percentage; the panel's stock section; the siphon on T; the vault and crate JSON and textures; `M10:` store terms. | r6 to the digit; a v8 world opens as sandbox; the kit is granted once and not on reopen; every creative-off key does nothing; headless: a new creative world (`--world=probe-creative --mode=creative`) with `--placeat=` vaults and cells bankrupts channel 0 under a storm clock, and the log reads own charge spent, then fuse, then dump (IM-3b), every identity exact. **Playable: the bank, the vault, water and the dump.** |
| **IM-7** Core | The `quarry` kind: the box, `Facing`, the cursor, the skips, the scan cap, the stalls, the allotment and the carve's draw (`dug`), recording off with the chunk forget. | Digs a fixture box once, top down; never a machine or a full vault; pays exactly the voxels removed × 50 J; `OFF SITE` on an unloaded chunk; `flooded`; the cursor survives a save; Ctrl+Z on a hand dig in a chunk the quarry wrote is dropped, and in an untouched chunk still works; a Release bench arm at 1, 4 and 16 quarries. Mutation: quarry writes with recording on, red. |
| **IM-8** Wire | Quarry wiring: the `quarry` bracket, the panel, the pins, the README's OH&S line. | Headless on the fixed seed: bites, and `quarry` against the 1 ms line; r6. **Playable: quarries.** |
| **IM-9** Core+wire | Recall, the board, pin states, the power sensor, the unloaded-sensor guard. | `released == landed + in flight + lost + recalled` in both collapse modes and under `--bitechunks`; a part-picked machine credits `cleared + Recalled`, not 64; a bankrupt channel recalls nothing; a recalled pump is 64 in the pocket and none of it rubble; after the drain, Ctrl+Z re-adds nothing and the next arrival rebuilds nothing. Mutation: credit 64, red. A sensors × radius arm against `machines`. |
| **IM-10** | **His play of the first minutes.** | His sentence on the bill, the siphon's price, the storm and the dump. |

**Plays after IM-6, IM-8 and IM-9.** About sixty tests and ten mutation batteries (estimate). **The riskiest
slice is IM-5**: four destinations, owed amounts across unload, the undo and a crash, all at once.

## IM-0 — measured (2026-09-29)

On the fixed build, headless, the r6 control first (`2601 / 325 / 3446 at 411`, to the digit, 0 `ERROR`).
Every arm ran twice, alternated; the rounds agree to within a few percent (the fluid arms to the visit),
so the machine's two speeds did not move them. 0 `ERROR` in all 22 runs. Played worlds ran on fresh
copies, deleted after. The batch script and its readers are kept in
[panel/ideal-machines-2026-09-29/im0/](panel/ideal-machines-2026-09-29/im0/); the outputs were not kept,
and the tables below are what they said.

**The machine bracket, on his retired machine worlds** (played-style, `start.cmd`'s four flags, 5,400
frames, about 70–80 boundaries each; he has no machines in TajMahal):

| World | Rows | `machines` mean | max |
|---|---|---|---|
| `retired-pool-2026-09-22` | 127 (48 pumps) | 0.880 / 0.882 ms | 16.6 / 15.5 ms |
| `retired-spread-2026-09-22` (the load test) | 209 (82 pumps) | 0.682 / 0.626 ms | 11.9 / 10.8 ms |

Both means sit under the 1 ms trigger, with 0.1–0.4 ms of room. The max is the opening boundary, the
rebuild and the first intake reads; the mean without it is about 0.65 ms on the pool world (arithmetic).
So vault bills and the priority sort have little room inside `machines`, and **the quarry's own `quarry`
bracket (46) is confirmed as the right call**.

**TajMahal's boundary, on `retired-TajMahal-2026-09-29` at the pool** (30.2 ML loaded, the subject):
the worst passes over a run are hand-off 33–35 ms, field sync 26 ms, lake 24 ms, collapse 11–12 ms,
table 10 ms, pour 1.4 ms, machines 0.5 ms.

**The bite** (`--profile --speed=6 --radius=4 --vradius=2 --mine --nogate --measure=20`, the recorded
cube figure's flythrough):

| Arm | Swings/s | Voxels removed a swing | `swing ms` mean / p95 / max | Remeshes a swing |
|---|---|---|---|---|
| 1 m cube (`--entangle`) | 9.5 | 41–42 of 64 | 0.16–0.20 / 0.48–0.52 / 8.5–8.8 | 2.4 |
| 2 m tunneller (`--entangletunnel`) | 3.9 | 290–298 of 512 | 0.42–0.53 / 0.69–1.07 / 7.4–8.0 | 4.2–4.3 |

Per voxel that is 4.1 µs (cube) and 1.6 µs (2 m), both about 2–2.5× cheaper than the pre-fix figures
this plan quoted (8.9 and 3.4). A solid 2 m bite of 512 voxels projects to about 0.85 ms (arithmetic).
**He took the 2 m bite** (decision 45). **The "wet" arms were not wet**: the flythrough left
`--fillbasin`'s basin behind, with no fluid field in any run, so a bite under a lake is still unmeasured
(it is IM-7's bench).

**Tier B** (`--profile --speed=0 --fluidnear=2 --fluidjob=1 --measure=60 --injectlitres=`):

| Pour | Insertion, once | Tick wall mean / max | Main thread a boundary, mean | At 60 s |
|---|---|---|---|---|
| 1 ML | 72 ms (≈1.1 µs a cell) | 20 ms / 214 ms | ≈4 ms | all handed to the lake |
| 10 ML | 305–309 ms (≈0.5 µs a cell) | 385 ms / 534 ms | ≈16 ms (hand-off 5.6, compose 4.5, field sync 3.0, pour 2.6) | 3.2 ML in 371,000 cells, 449,000 awake |

The 10 ML arm held 2.00 Hz, with 15 of 120 boundaries waiting on a slow tick. **Two decisions are now
measured rather than guessed:**
- **32's 10 ML ceiling stands.** 10 ML is carried at 2 Hz, but heavily, and more would not be.
- **33's per-boundary pour budget stands.** 3,200 cells is about 2–4 ms at the measured rate. The insertion
  hitch comes only from pouring everything at once, which the budget exists to prevent.

The caveat: both pours landed on open ground, where two thirds became lake. A dump underground stays in
Tier B whole and costs more, and there is no headless fixture for that yet (IM-5's).

**The surface install, windowed (run 2026-10-11 on his yes).** Headless builds no surface. The same
recipe at 10 ML, windowed, then headless five minutes later as the control:
- the install read **147.82 ms mean / 249.29 ms max** on the main thread, over 63 rebuilds of 77.6 panels and
  38 MiB each; 227,515 quads, 84% of them sides (a spreading front is all steps);
- the tick ran at 0.97 Hz with a wall of 850 ms, 185 ms of it the surface walk on the worker. The headless
  control read 1.67 Hz and 551 ms on a slow clock (its insertion took 599 ms against the 305–309 above);
- his own play the same night, 16.9 ML moving on `LakeTest`, read install 23 ms mean / 137 max.

**What it decides (taken 2026-10-11, *"as recommended"*).** The arm is the worst case: `--injectlitres` lands 10 ML at once on open
ground, while a dump in the workings pours within 3,200 cells a boundary, at most about 50,000 L (arithmetic),
so 10 ML takes at least 100 s at 2 Hz. Recommended: **32's ceiling stands**, the render half recorded at its
worst, and a real dump read windowed at IM-6, once a fuse can burn out underground. If that read is over about
50 ms mean (roughly twice his heaviest play, a judgement) or 250 ms worst, the lever is the render, not the
ceiling, since his own floods make the same front. WORKLOG, 2026-10-11.

## IM-1 — the code-level pass (2026-09-29)

Decisions 1–6 read against the loader (`ContentLoader.cs`), the validator (`MachineDefinition.cs`), the
material table and the computer's verb. Five facts from the code, then eight decisions, each with a
recommendation. **All eight taken as recommended (2026-09-29): *"as recommended please Claude"*.**

**Five facts.**

1. **Collapse f-1 is not built.** Nothing in the C# reads or declares `onsignal`. The split assumed f-1
   lands first: *"IM-1 moves f-1's `onsignal` under the sense extra"*, tested by *"rows saved under f-1
   restore unchanged"*.
2. **Every extra's reader lands in a later slice**: sense in collapse f-1; charge and priority in IM-2;
   recall, remote and telemetry in IM-9. In IM-1, a machine that opted into any extra would load and do
   nothing. That is the state decision 4, and M10a's refusal of an unserved `via`, exist to refuse.
3. **There is no medium registry, and the one served medium is an engine word.** A link's `via` is
   `channel` (served), `solar` (served, but the sky feeds a converter and reaches no one), an engine word
   (`crank`, `burner`, `surround`, `adjacent:`; refused naming M10f), or any other identifier, a conductor
   class (refused naming M10e). Ladder decision 21 made conductor classes free class strings; decision 3
   here calls media "namespaced content IDs". Both cannot be literal.
4. **System.Text.Json on this build** (a throwaway probe, .NET 8.0.31):
   - a key given twice loads the second silently, `"kind"` against `"Kind"` included;
   - a `$comment` inside a name-keyed map (`links`, `ports`) throws, because it is read as a link;
   - the built-in strict mode (`UnmappedMemberHandling.Disallow`) stops at the first unknown key, names
     the loader's private class, refuses `$comment` too, and suggests nothing.
5. **The computer's verb is on the Game side.** `PlaceController.cs:524-552` opens a panel for any
   non-placeable material that is not a pack, and the HUD calls every such item `COMPUTER`
   (`Hud.cs:536-541`). Only the computer ships as an item today, so the fix is invisible in play.

**Decisions.**

1. **IM-1 lands before f-1, and f-1 builds on it.** IM-1 builds the sense extra's load-time half: a
   machine that opts into `sense` must declare `onsignal` at f-1's bounds (min 0, max 2; the default is
   its own, 0 for f-1's readers and 1 for the quarry, decision 38). f-1 then gives its two readers
   `"extras": { "sense": {} }` and the port, and switches `sense` on (decision 2 below). f-1's name, bounds
   and defaults are unchanged; its restore test moves to f-1, since nothing is saved under f-1 before it
   exists. *Cost:* one JSON line a reader in f-1. *Revisit:* he wants f-1 in his hands first; then f-1
   builds `onsignal` plain, and IM-1 moves it, as the split said.
2. **An extra is refused on a machine until its reader lands, naming the slice**, as an unserved `via` is.
   The whole grammar is read and checked first (names, forms, the machine's medium, the table, required
   ports); the last check refuses `sense` as collapse f-1's, `charge` and `priority` as IM-2's, and
   `recall`, `remote` and `telemetry` as IM-9's. Each of those slices switches its own on, so no shipped
   machine opts into an extra in IM-1. **The table's rows are not refused**: a row states reach, as the
   `via` rules the build knows do, and decision 3 ships the quantum row now. Tests reach the served path
   through the loader's served set, a constructor argument the game never passes. *Cost:* IM-1 changes
   nothing a player sees; its shipped effects are the strict loader, `format`, `programmer` and the
   table. *Revisit:* none; each slice flips its own.
3. **A medium is named as a `via` is, and checked by the same rule.** `channel` is the one medium an
   extra can reach today. `solar` is refused (the sky reaches no one), an engine word names M10f, and any
   other identifier is a conductor class naming M10e. So a misspelt medium is refused today by the rule
   that already refuses a misspelt `via`, with no registry to add, and when M10e decides how a conductor
   is spelt, the table follows. **This replaces decision 3's "namespaced content IDs".** A machine's
   medium is its links' `via`, `solar` aside; a machine with extras and no such link is refused (f-1's
   sensor, with no links, takes none). *Cost:* none. *Revisit:* M10e's conductor names.
4. **The grammar.** A machine opts in inside its `machine` block, `"extras": { "sense": {} }`; each inner
   object takes no key yet. `charge` is a carried storage's extra and the other five a placed machine's,
   since a pack has no body, no row and no pin. `sense` needs `onsignal` 0..2 (decision 1 above);
   `priority` needs `priority` 0..9 (decision 41). `mods/<mod>/extras.json` maps extra → medium → form,
   and every mod's is read before any material. The base game's:

   ```json
   {
     "$comment": "How far each extra reaches on each medium. The lower tiers' rows land with their membership (M10e/f).",
     "sense":     { "channel": "switch" },
     "recall":    { "channel": "return" },
     "remote":    { "channel": "board" },
     "charge":    { "channel": "window" },
     "priority":  { "channel": "port" },
     "telemetry": { "channel": "state" }
   }
   ```

   Forms are a closed C# set per extra, named from section F's table. The lower tiers' forms (`bell`,
   `station`, `dock`, `position`, `pin`) are reserved and refused naming M10e or M10f, as reserved `via`
   names are; any other form is unknown, with a did-you-mean. An extra with no row for a machine's medium
   is refused on that machine. *Cost:* five reserved names. *Revisit:* M10e/f.
5. **One (extra, medium) pair, one owner**, as one ID has one definition. A second mod giving a pair
   already given is refused naming both files, whatever form it gives, and the first in load order keeps
   it. Load order is sorted (the base game, then by ID), so a fixture loads the same whatever order its
   mods are listed in. This follows the split's test (*"two mods giving one (extra, medium) pair are
   refused"*) rather than decision 3's *"given two forms"*, and it is the only reading reachable today:
   over `channel` each extra has one form, so two different valid forms cannot be written until a medium
   has two. *Cost:* a mod cannot restate the base's row. *Revisit:* M10e, if mods want to add forms over
   one another's media.
6. **The strict walk, not `Disallow`** (fact 4). Every file (`mod.json`, materials, tools,
   `extras.json`) is parsed once and walked against the loader's own classes, their properties read by
   reflection, so the walk and the binding cannot disagree. It reports every unknown key in the file, each
   with its path (`machine.links.power.carrys`) and the nearest known name within two edits (one for a
   name of four letters or fewer), case ignored. It refuses a key given twice at one level, the same
   silent-data class. It allows and drops `$` keys at every level, maps included. Binding stays
   case-insensitive, strict about names and not their case, so no mod that loads today on a capitalised
   key breaks. A refused file is skipped whole, as today. *Cost:* one more parse a file at startup, a few
   dozen small files. *Revisit:* none.
7. **`format`: absent reads as 1, and this build reads 1.** The base game declares `"format": 1`. A mod
   whose format is higher is skipped with one line (*"written for content format 2; this build reads
   1"*), checked before the manifest's own keys, so none of its files is read; a mod that depends on it
   says it was skipped, not that it is missing. Zero, negative or fractional is refused. *Cost:* none;
   format 1 is this grammar, the strict walk included. *Revisit:* the first format 2.
8. **`programmer` is a material key, and the table answers it.** `"programmer": true` goes on
   `computer.json`, and is refused on a placeable material or a machine (a programmer is an item in the
   hand). `MaterialTable.IsProgrammer(id)` projects it. `PlaceController`'s item branch opens panels only
   for a programmer; any other item's click does nothing, and the HUD's `COMPUTER` line names the item
   instead. This is IM-1's one Game edit, so the slice closes on the r6 control. *Cost:* a Game build,
   invisible in play.

**IM-1's tests, restated for the above.** Every shipped file loads. A misspelt key at each of nine levels
(manifest, material, machine, link, port, tool, tool power, an extra's object, the table) is refused
naming the file, the path and the suggestion. `$comment` at every level, maps included, loads. A key given
twice is refused. Two mods giving one pair are refused naming both, and a three-mod fixture gives the same
table, IDs and errors in every listing order. A misspelt medium, `solar`, an engine word and a conductor
class are each refused with their reason; so are an unknown form and a reserved one, and `onsignal` or
`priority` missing or mis-bounded. Each extra is refused on a machine naming its slice, and with the
served set its bit and form land on the table. A newer format fails in one line with its files unread and
its dependant told. The computer is a programmer and a part-like item is not. **Mutations, one a rule:**
the unknown-key refusal, the `$` skip, the duplicate check, the pair's owner, the medium check, the format
check, the programmer gate. *Estimate:* one session, about 45 tests.

## IM-1 — built (2026-09-29)

All eight as recommended, the same night. **Suite 2,076** (2,006 + 70). **Twelve mutations, twelve red**,
each on the test named for its rule, every file restored byte-identical: the unknown-key refusal, the `$`
skip, the duplicate check, the pair's owner, the medium check, the format check, the table's programmer bit
(set back to "not placeable", the old rule), the programmer-is-an-item rule, the served check, the required
port, the carried rule, and the missing-row refusal.

What it is, by file:
- **`Content/StrictJson.cs`**, the walk. It parses once, checks every object against the loader class it
  binds to (public settable properties by reflection, camelCase for messages, case ignored), and returns the
  original text when no `$` key was dropped, so the serialiser's messages keep their line numbers. Maps
  (`links`, `ports`, `extras`, the table) accept any name, and refuse only notes and exact repeats.
- **`Machines/MachineExtras.cs`**: `MachineExtra` (six bits), `ExtraForm` (the six quantum forms), the
  names, reserved lower forms, required ports and serving slice in one table; `ExtrasTable` and
  `ExtraReach`. `MachineExtras.Served` is `None`; f-1 sets `Sense`, IM-2 `Charge | Priority`, IM-9 the rest.
- **`MachineDefinition`**: `ExtraForms`, `Extras`, `Has`, `FormOf`, `MediumOf`; `TryValidate` holds a
  definition built without the loader to the same extras rules. `ViaIsServed` takes a noun, so a medium is
  refused in a medium's words.
- **`ContentLoader`**: the format read first and alone; every file through the walk; every mod's table
  before any material; `programmer`; the served check last. `LoadResult` gains `ExtrasLoaded`, so the
  `Content:` line reads `… 6 extra reach(es) …`. `GameContent` gains `Extras` and a served-set argument the
  game never passes.
- **`MaterialTable`**: `IsProgrammer(id)` and `HasExtra(id, extra)`, one array read each.
- **The Game's one edit**: `PlaceController.SelectedIsProgrammer` gates the computer's verb, and the HUD
  reads `ITEM (in hand) · nothing to place or open` for any other item. No such item ships, so nothing a
  player sees changes.
- **Shipped**: `mod.json` declares `"format": 1`, `computer.json` `"programmer": true`, and
  `extras.json` the quantum row. Every key the shipped files use was already bound, so the walk refused none.

**The r6 control, to the digit** (run once his game closed; he had played through the build on a binary with
no IM-1 loader in it): `2601 viewed / 325 drawn / 3446 jobs` at 411 quads a drawn chunk, 0 `ERROR`, and the
game's own content line reads `1 mod(s), 14 material(s), 5 tool(s), 6 extra reach(es), 0 error(s)`.

## IM-2 — built (2026-09-29)

*"start on IM-2 please Claude"*. Decisions 7, 8, 9 and 41 read against `EquippedPack`, `MachineTick`,
`CarriedPacks` and the two tool call sites, and they fit as written, so there was no pass to hand back:
- **The tools pay only through the pack.** `MiningController` and `PlaceController` call
  `VoxelsAffordable` and `Pay`, so a window that answers both from its channel's cells changes no call site.
- **The pool is re-summed from the cells every boundary**, and the write-back fills them in position order.
  So "last-filled first" is the write-back read backwards, and it only matters to a cell removed before
  the next boundary.
- **`SetCharge` counts a fall as `leaving`**, so the draw writes `StorageState` directly, as decision 8 says.
- **A window's capacity is 0**, so the recharge, the fill and `Give` already give it nothing by arithmetic.
  The explicit refusals say so out loud; a mutation that removes one stays green, as predicted.

The one choice taken without a pass: **`P` does not seed the backpack until IM-6**, when the HUD has a bank
line to show; `--pack=overburden:quantum_backpack` and `--stock=` reach it meanwhile.

What it is:
- **`EquippedPack`**: `IsWindow`, an internal `Bank`, and `SpendableMillijoules` (the bank for a window,
  the charge otherwise). `Take` draws through the bank and counts `taken`; `Give` refuses a window.
- **`IChannelBank`** (internal, new): `BankMillijoules` and `Draw`, implemented explicitly by `MachineTick`,
  so the one way to debit the cells from outside the tick is through a pack that counts what it takes.
  `EquipPack` attaches it to an incoming window and detaches it from an outgoing one.
- **`MachineTick`**: `Run` sets a running flag, and `Draw` throws inside it. `RechargePack` and `FillPack`
  skip a window. Pumps are sorted by `PriorityOf` (the port, 0–9, where the material has the extra; 5
  otherwise), then position. An internal `PumpOrder` is kept for tests.
- **`MachineDefinition`**: `IsWindow`. The validator refuses a window with a `capacityJoules` or a
  `rechargeWatts`, and every other storage still needs a capacity.
- **`MachineExtras.Served`** is `Charge | Priority`.
- **Shipped**: `quantum_backpack.json` (decision 7's JSON, texture layer 13, a cyan swatch) and the relay
  pump's `"extras": { "priority": {} }` with `priority` 0–9 at 5. An existing world's pumps take the new
  port at its default when they open (`defaulted` on the restore line). The three hard counts moved as
  decision 50 said: machine materials 5 → 6, non-placeable 2 → 3, and IDs appended to the v3 fixture 9 → 10.
- **The prints**: the open line, the gate's lines and the profile's `gate` line say `the bank on channel N`
  for a window, and the `power:` term prints `pack initial` with the full formula.

**Suite 2,088** (+12). **Ten mutations**: nine red, each on its test, and the tenth (the fill's explicit
refusal) green as predicted, the capacity rule being the one that bites. The nine:
- the `taken` credit dropped;
- the draw written through `SetCharge`;
- the run guard removed;
- the priority sort removed;
- the draw's order reversed;
- the bank not attached;
- the bank not detached;
- `Charge` unserved;
- the window's capacity rule removed.

**Headless:**
- **The r6 control to the digit** (`2601 / 325 / 3446` at 411, 15 materials, 0 `ERROR`).
- **M10a-3's loaded arm holds** with the priority port on the shipped pump: `drawn 1,000 == pumped 1,000 ==
  emitted 1,000`, every identity exact.
- **The window's oracle** (COMMANDS.md, "IM-2 adds") reads `paid 306,650,000 mJ` straight out of the
  `--pump` cell, `… + 306,650,000 by the tool … exact`, identically on two runs.

## IM-3 — the code-level pass (2026-09-29)

Decisions 13–22, 27, 28 and 34 read against `MachineSet` (add, remove, the drain, the reconcile,
restore), the carve's gate, `EditHistory`'s undo, `MachineTick`'s pool and the collapse scheduler's fuse.
Seven facts from the code, then twelve decisions. **Nothing is built; the twelve wait on his word.**

**Seven facts.**

1. **The carve tells the set about a body voxel only after taking it.** `Carve.Take` checks hardness,
   writes air, credits, then calls `IMachineBodies.Taken` (`Carve.cs:739-773`). 17's "too hard unless its
   sisters have room, asked before the carve" needs a question before the hardness check, and the seam
   has none. Nor does the carve know whether its tool is at range; `MiningController` does
   (`tier.AtRange`).
2. **One picked voxel dismantles the whole machine at the drain** (`Lost == Carved`,
   `MachineSet.cs:566-576`), and one swing can take voxels of two vaults. If the contents move at the
   drain, as 17 says, two full vaults picked in one swing can each have been allowed on the other's room.
   The second move then fails with its body already credited: the holed-and-credited state 17 exists to
   prevent.
3. **A wreck is a row with no body, and the set is keyed by cell** (`MachineSet.cs:51`, `Place.cs:593`).
   So a wreck keeps its cell, and no machine can be placed there. A block placed in that cell marks it
   damaged (`OnVoxelChanged`), and today's drain would then destroy it. The reconcile's "all air" branch
   rebuilds a body from its row (`:468-479`), which would rebuild a wreck.
4. **"Returned at arrival" credits all 64 body voxels** (`:480-489`): the cell regenerated as rock, and
   the row says the player owns the machine. 17 makes a vault holding anything a wreck there, and its
   body still can't stand in rock.
5. **The dump is IM-5.** In IM-3, a fuse that burns out leaves the vault `dumping` with its contents owed,
   and nothing releases them until IM-5; a wreck is the same. No vault ships until IM-6, after IM-5, so
   nothing a player can reach strands.
6. **A small bill rounds to nothing, and a big one overflows.** A god vault's block rate is 64 mW/m³; one
   voxel (1/64 m³) for half a second is 0.5 mJ, and a floor makes it free. At the top,
   rate × millilitres × milliseconds is 10^6 × 10^12 × 500, past a long.
7. **Two borrowed pieces.** "A signalled cell never unpays a hold" has no signal to test, since f-1 is
   not built. The collapse scheduler's fuse, `min + (max − min) × u × (1 − share / 2)` seeded through
   `Hash64`, is the shape 28 names, and the tick has no seed of its own.

**Decisions.**

1. **The vault's grammar.** `"kind": "vault"` with:
   - `insideCubicMetres`, 1 to 1,000,000, an engine maximum refused above (decision 5);
   - `holdWattsPerCubicMetre: { "water": …, "blocks": … }`, read as whole milliwatts (three decimals at
     most, as `efficiency` is);
   - `holdsWater`, true for a vault (IM-4 reads it);
   - one power sink via `channel`, the bill's channel;
   - an `enabled` port 0–1: at 0 it is closed to deposits and withdrawals, and keeps paying (27).

   The five grade files and their textures ship in IM-6, as the split says; IM-3's tests use fixtures.
   *Cost:* one nested key. *Revisit:* his tuning.
2. **The crate is the vault kind with nothing else** (22): `insideCubicMetres: 1`, no hold,
   `holdsWater: false`, no links and no channel. So it is in no store, never bills and never fuses, and
   deposits and withdrawals name its row. Its JSON ships in IM-6 too.
3. **The pick asks first, through the bodies seam.** `IMachineBodies` gains `Refuses(voxel, material)`,
   asked before the hardness check for a machine material. The set answers yes for a vault or crate
   holding anything whose sisters on its channel can't take its contents. A sister is a vault on the same
   channel that isn't dumping, isn't a wreck, isn't already damaged, and has the room; a crate has no
   sisters. At-range tools skip a holding vault whatever the room: `MiningController` hands an at-range
   swing a wrapper that answers yes for any holding vault, so no carve signature changes. *Cost:* one
   dictionary read a machine voxel.
4. **The contents move at the first `Taken`, not at the drain** (fact 2). The carve's own call moves
   them to the sisters in fill order, at the same instant as the ask. The drain then dismantles an empty
   vault, and `Remove` journals the empty copy, so Ctrl+Z re-adds an empty vault: 17's rule, one level
   down. Two vaults in one swing can't count on each other, because the first is marked damaged and stops
   being a sister. *Cost:* a row's bytes written from the carve's call. The hooks' "queue, never write"
   rule is about voxels written from the pipeline, and this is neither.
5. **A wreck is a flag in the codec, and every remove path yields to it.** The drain's destroy branch and
   the reconcile's two remove branches turn a vault holding anything into a wreck, never a removal.
   Returned-at-arrival still credits the body, which can't stand in rock (fact 4); destroyed-at-arrival
   credits nothing, as today. A wreck is skipped by the drain and the reconcile (never rebuilt), never
   bills, holds its cell (fact 3) and owes its contents. It is removed only once empty, which is IM-5's
   work. *Cost:* a cell nobody can build a machine in until the dump is done.
6. **The bill: all or nothing a vault, in fill order, before the consumers**, from the channel's pool
   (solar and enabled cells). It is `ceil(rate × volume × time)` in 128-bit arithmetic (fact 6), so a
   vault holding anything always costs something, and it is a new `PowerExact` term, `held`.
7. **The fuse, by the collapse's own formula**: 10–20 s, as `10 + 10 × u × (1 − shortfall ÷ bill ÷ 2)`
   with `u` from `Hash64(fuse seed ^ boundary, cell)`. The tick takes a `FuseSeed` (Main passes the
   world's seed). The fuse is saved as milliseconds left and counted down by each boundary's
   milliseconds; a boundary that pays in full disarms it (*amended by IM-3b: it waits, and goes out only
   when the vault's own minute is whole again; the shortfall is past the pool and the own charge both*).
   At zero the vault is `dumping` and its contents are owed (fact 5). An empty vault can't fuse.
8. **The codec, one owner of the blob**, version 1:
   - flags (wreck);
   - phase (holding, fusing, dumping);
   - fuse milliseconds left;
   - water in millilitres (0 until IM-4);
   - the contents as (material, voxels) pairs sorted by material.

   An unknown version is kept opaque and inert, and reads as holding something to every rule: it refuses
   the pick and the undo, is out of the store, and never bills.
9. **The store: a view, and the pocket's two doors.** It is the channel's vaults in fill order (the
   lowest block rate first, then position), leaving out `enabled` 0, dumping vaults, wrecks and unknown
   codecs. Core gets `Deposit` and `Withdraw` between the pocket and the store, and a crate's by its row:
   the only doors in IM-3, before the quarry and water add theirs. The identity is global,
   `held + owed == entering − leaving + deposited − withdrawn`, with `entering` and `leaving` counted by
   `Add` and `Remove` (zero in play, by decision 5).
10. **The undo refuses a step that would remove a vault holding anything**, a wreck or an unknown codec.
    It is a pre-flight beside the cost check, with a new `UndoOutcome.VaultHolds`, and the step stays, as
    an out-of-stock step does.
11. **Restore never clamps contents.** An overfull vault keeps what it holds, refuses deposits, and is
    counted `overfull` on the restore line. `SetPort` to another channel carries the contents on the row
    with nothing to do; a fusing vault moved to a solvent channel disarms at its next paid boundary
    (*amended by IM-3b: once that channel's spare has refilled its minute, usually the same boundary*).
12. **A minimal panel branch lands now**: `VAULT — holds N of M (p%) · bill X · HOLDING | FUSE 12 s |
    DUMPING`, one Game edit. A third-party mod could ship a vault the day IM-3 lands, and the panel's
    fall-through is pump text (must-not-ship 14). The full stock section stays IM-6's. *Cost:* the r6
    control closes the slice.

**Moved to f-1:** *"a signalled cell never unpays a hold"* (fact 7). The bill is paid from the pool before
any override exists, and f-1's override must keep it so.

**IM-3's tests, restated.**
- The store's identity exact over a thousand random operations, among them deposits, withdrawals, picks
  with a move, falls into wrecks, arrivals, restores and Ctrl+Z.
- Two vaults both bill, and an unpaid one fuses.
- A save during a fuse resumes at the saved milliseconds.
- A wreck survives a reopen and is never rebuilt.
- Both reconcile branches keep contents.
- An unknown codec version refuses the pick and the undo.
- `overfull` at restore.
- A refused pick carves nothing.
- The undo's refusal is a pre-flight.
- Two full vaults in one swing never strand a body.
- At range skips a holding vault.
- A crate refuses the pick while it holds anything.

**Mutations:** journal a full row (red); bill with no `held` term (red); move the contents at the drain
instead of at `Taken` (red on the two-vault swing); rebuild a wreck (red). *Estimate:* one long session,
about 50 tests. **The riskiest part is 4, 5 and 10**, the three places a vault's contents could leave the
books.

## IM-3 — built (2026-09-29)

All twelve of the pass's decisions as recommended (*"as recommended please Claude"*). Built in a worktree while
he might play, then merged. **Suite 2,127** (+39 in `VaultTests`). **Twelve mutations, twelve red**, each on its
own test:
- the move at the drain instead of at `Taken`;
- no `held` term;
- rebuilding a wreck;
- no undo pre-flight;
- the carve not asking;
- at range answered as the pick is;
- a fall removing;
- the returned branch removing;
- a lenient codec;
- no disarm;
- fill by position;
- a floored bill.

What it is:
- **`MachineKind.Vault`** (the crate is a vault with no links), with `insideCubicMetres`,
  `holdWattsPerCubicMetre { water, blocks }` and `holdsWater`.
- **`VaultState`**: the strict codec.
- **`MachineSet.Vaults.cs`**: the store view, `Deposit`/`Withdraw`/`DepositInto`/`WithdrawFrom`, `Refuses`,
  `MoveAtPick`, the wreck, `StoreExact`, and `AtRange`.
- **The drain and reconcile** yield to a vault holding anything; the hook ignores a wreck; the drain walks a
  snapshot.
- **`IMachineBodies.Refuses`**, asked by `Carve.Take` before the hardness check.
- **`MachineTick.PayHolds`**, `BillOf` (Int128, ceiled), the fuse by the collapse formula, and the `held`
  term.
- **`UndoOutcome.VaultHolds`**, and `RestoreReport.Overfull`.
- **Game**: the miner hands at-range swings `MachineSet.AtRange`; the panel's vault line; the fuse seed from
  the world's; `held` and a `vaults` line on the log.

Two things the build found:
- **The world's write hook marks a row damaged before the carve's `Taken`**, so the move can't be guarded
  by the damage mark. It names the row instead, and does nothing once the vault is empty.
- **A wreck cleared with its row standing re-marks itself**, which would have modified the damaged list
  mid-walk. The fix is to flag the wreck first, have the hook skip wrecks, and walk a snapshot.

**The r6 control on this build** read `2601 / 325 / 3446 at 411`, 0 `ERROR`, the next session (before IM-3b's
pass).

**Taken during the build, for later slices** (his words 2026-09-29, both options as recommended):
- **The vault carries its own energy: one minute of its own full bill, a knob in each grade's file** (low
  150 kJ, medium 600 kJ, high 2.4 MJ, god 38 MJ). It pays the bill when the channel can't, recharges from
  the channel's spare, and the fuse lights only once both are empty. This amends decision 18 ("the
  channel's cells are its pack") for the bill only. No vault's bytes are saved before IM-6, so the codec
  can take the field without a version. *IM-3b's pass (taken) made it a minute of the bill **as it
  stands**, not when full, and built it; see "IM-3b — built".*
- **A lock on the vault panel's channel box**, so a vault can't be moved to an unpowered channel by accident.
  It lands in IM-6's panel.
- **Power auto-scales on screen** (W, kW, MW), as energy already does; the mod files stay in watts.

## IM-3b — the code-level pass (2026-09-29)

The vault's own energy, as taken during IM-3's build, read against `MachineTick`'s pool (`BeginPower`,
`PayHolds`, the pack, `WriteBack`), `VaultState`, `MachineSet.ChargeOf` and the vault half of the set. The
r6 control on the IM-3 build ran first: `2601 / 325 / 3446 at 411`, 0 `ERROR`. Five facts, then seven
decisions. **All seven taken as recommended the same night** (*"As recommended please Claude thanks Claude
we seem to be in tune on this like"*), **and built: "IM-3b — built", below.**

**Five facts.**

1. **IM-3's fuse is put out by any cell, and a storm never lets a vault go.** The unpaid pool is not spent:
   `WriteBack` puts it back into the channel's cells, so it builds up and pays the whole bill one boundary
   in N. One paid boundary puts the fuse out (28), and a broke channel's fuse is 10–15 s (20–30
   boundaries). So any income above about a twentieth of the bill holds a vault for ever. Measured with a
   throwaway test (a small vault, a 1,250 mJ bill, an empty cell, income a share of the bill each
   boundary; reverted after):

   | Income, share of the bill | 1% | 3% | 5% | 6% | 10% | 25% | 50% | 99% |
   |---|---|---|---|---|---|---|---|---|
   | Dumped after (boundaries) | 25 | 25 | never in 4,000 | never | never | never | never | never |
   | Bills paid in full | 0 | 0 | 198 | 240 | 400 | 998 | 2,000 | 3,958 |

   At 5% the vault paid a twentieth of its bill for 33 minutes and held. So decision 12's storm, which
   leaves eight panels 0.5 kW against a 2 kW bill (25%), never lets the demo's vault go (55). No player can
   reach this: no vault ships before IM-6. **The vault's own energy does not close it by itself.** An own
   charge refilled from the spare is the same store the cell was, and it pays one boundary in N the same
   way.
2. **"A minute of its full bill" is a capacity, and the time it buys depends on how full the vault is.**
   The own charge is a number of joules: one minute of the bill the vault would have if it were full of water.
   It drains at the deficit, which is the bill the vault actually has, less what the channel pays. So a
   full vault gets one minute, and one a twentieth full gets twenty. The demo's vault is a high vault holding
   500 m³ (a 2 kW bill, 5% of full). With the bank at zero and 0.5 kW of storm sun, 2.4 MJ lasts 1,600 s,
   about **27 minutes**. A weather episode is two minutes (`Weather.EpisodeMillis`), with the cloud a minute
   ahead of it and two behind, so a storm's dark is a few minutes (an estimate). The demo's vault would hold
   through every storm. And spreading a store thin over many vaults multiplies the grace, since there is no
   limit on vaults (14).
3. **The identity already carries a row's stored energy, so no new term is needed.** `PowerExact` sums
   `MachineSet.ChargeOf` over every row, and `Add`, `Remove` and `Restore` count it as `entering` and
   `leaving`. If `ChargeOf` reads a vault's own charge, then:
   - a bill paid from it is still `held`;
   - a recharge or a return is a transfer inside `stored`;
   - a pick, Ctrl+Z and a reopen count it as they count a cell's charge.

   TODO said IM-3b needs a `PowerExact` term. It doesn't.
4. **One write drops everything but the contents.** `MoveAtPick` writes a fresh `VaultContents()` onto the
   picked vault (`MachineSet.Vaults.cs:391`). With an own charge in the codec, that write would take the
   charge out of the books with no term, and the identity would go red at the next check. It is the only
   such write (grep); every other write decodes, changes and writes back.
5. **The pool's spare includes the cells.** `_available` is this boundary's sun plus every enabled cell's
   charge, and what is left after the consumers and the pack is written back. So "recharges from the
   channel's spare" can mean the pool's remainder, cells included, or the sun's surplus alone. It only
   matters after the channel has recovered. Sun only would leave a vault that spent its minute in a storm
   empty until the sky clears, even beside a cell he has since charged. (There is no night: the panel's
   sum has no time of day.)

**Decisions.**

1. **The knob: `ownChargeSeconds`**, whole seconds of the vault's bill. It is 60 when absent, 1–3,600 on a
   vault with a channel, and refused on a crate, which has no bill. Zero is refused because decision 5's
   rule needs a charge that can be full or not. The five grade files ship 60 (IM-6). It is called a
   charge, the code's word for stored energy, and never a reserve, so nobody reads it as must-not-ship 1's
   floor. The bank never refuses a spend for it.
   *Cost:* no vault can have no grace at all; the least is one second, two boundaries.
2. **◆ A minute of its bill as it stands, not its bill when full** (fact 2). The own charge's capacity is
   `BillOf(its contents, ownChargeSeconds)`, the same rounded-up 128-bit formula as the bill. It is
   recomputed at each boundary, and a charge over it goes back to the pool before the bills are paid. So
   every vault on a dead channel holds for exactly a minute, full or nearly empty. The demo's vault
   (120 kJ against a 1.5 kW deficit) lasts about 80 s, then its fuse, and goes about a minute and a half into
   the storm (arithmetic). An empty vault has a capacity of 0 and holds no charge.
   *Cost:*
   - Each deposit raises the capacity, and the next spare fills it, so a deposit into a store on a broke
     channel has less than a minute's grace.
   - A withdrawal gives joules back to the pool at the next boundary.

   *The other arm, as taken:* a fixed capacity, a minute of the full bill (low 150 kJ, medium 600 kJ, high
   2.4 MJ, mythical 9.6 MJ, god 38.4 MJ). It is simpler: nothing moves back. But its grace scales with how
   empty the vault is, and 55's storm beat needs a vault at least a third full to fire (2.4 MJ spent within
   three minutes needs a 13 kW deficit).
   *Revisit:* his play of the storm, or he wants a nearly empty vault to be safe.
3. **Where it lives.** An int64 in the codec's version 1, after the fuse, with no version bump: no vault's
   bytes have been saved (no vault ships before IM-6, and none is in `mods/`).
   - `ChargeOf` reads it, so the identity needs no new term (fact 3).
   - `MoveAtPick` keeps it (fact 4).
   - A wreck keeps it and never bills or recharges. It leaves with the row, in IM-5.
   - A picked vault's charge leaves with the row, as a cell's does, and Ctrl+Z brings it back.
   - The window never draws on it: the bank is the enabled cells.
   - Restore clamps nothing; the next boundary returns any excess (2).
   - An unreadable codec reads as no charge, as it reads as no contents.
4. **Who pays: the pool as far as it goes, then the vault's own charge for the rest, all or nothing.** This
   stays in fill order and before any consumer (decision 9), so in a shortage the first-filled vaults take
   the pool and the later ones spend their own minute. A consumer never drinks the pool while a vault is
   spending its own charge. A bill is unpaid only when the pool and the own charge together fall short of
   it, which is "both empty" to within one boundary's bill. The fuse's lean reads the shortfall past both.
   `held` stays one term, and `of which own` is printed beside it.
   *The other arm:* the own charge first, then the pool. It lets pumps drink the pool while vaults burn
   their minute.
5. **◆ The fuse (fact 1): a paid boundary pauses a lit fuse, and only a full own charge puts it out.** The
   fuse lights when the vault's minute is spent. It burns on each boundary the bill goes unpaid and waits on
   each one it is paid. It goes out when the spare has refilled the vault's minute. This makes the fuse
   follow the channel's average, not its best boundary (arithmetic, under 2):
   - at 50% of the bill the minute lasts two minutes, then the fuse burns every other boundary and the
     vault goes 20–35 s later;
   - at 95% the minute lasts twenty minutes, then the fuse burns one boundary in twenty, and the vault goes
     3–7 minutes later;
   - at 100% it never lights;
   - a solvent channel with a little spare pauses it at once and puts it out once the minute is back.

   "Recoverable slowly" (12) holds, and the HUD's `LOSING CONTAINMENT — 14 s` counts unpaid time. This
   amends decision 28's *"a boundary that pays in full disarms it"* and IM-3's pass decisions 7 and 11.
   *Cost:* a fuse that is paused but not out still reads `FUSE`, so the panel says `FUSE 12 s (paused)`.
   *The other arms:*
   - Keep 28 as built. Any channel paying a twentieth of the bill holds a vault for ever, and 12's storm
     never happens.
   - Burn through once lit. Nothing but refilling the whole minute within 10–20 s saves it; a high vault
     would need about 240 kW of spare.

   *Revisit:* a play where a fuse that pauses reads as a bug.
6. **The recharge: after the consumers, before the lead-acid pack and the write-back, from what the pool
   has left, cells included** (fact 5). It goes in fill order, each vault to its capacity, with no rate
   cap. Every vault with a channel is charged except a dumping one, a wreck and an unreadable one; one
   switched off is charged too (it keeps paying, 27). The pack still draws last.
   *Cost:* a vault takes its minute from the bank at the first boundary after a deposit: 2.4 MJ for a full
   high vault and 38 MJ for a full god one, against a 5 GJ cell or the kit's 1 GJ. IM-6's runway should
   count the recharge and the return as transfers, not outflow, or a full god vault's first boundary puts
   38 MJ into the 10 s average: 3.8 MW of outflow for ten seconds.
7. **What shows, now:**
   - IM-3's panel line gains `own 118 kJ of 120 kJ` and `(paused)` on a waiting fuse.
   - The `vaults` log line gains the own charge summed, and `held … of which own …`.
   - The `M10:` power line prints nothing new: the own charge is inside `stored`.

   IM-6's stock section shows the same.

**IM-3b's tests.**
- The codec round-trips the own charge; the loader's default, bounds, zero refused and crate refused.
- The capacity is `BillOf` for 60 s, exact for a full low vault (150 kJ) and a full high one (2.4 MJ).
- The pool pays as far as it goes and the own charge the rest; unpaid only past both; `held` covers both.
- A consumer on the channel is paid before the recharge; the pack after it; the write-back gets the rest.
- A full vault, full own charge, no power: paid for 120 boundaries, then the fuse, then the dump.
- **The hole closed:** fact 1's table re-run as a test. At 5, 10, 50 and 95% the vault dumps, later the higher
  the share; at 100% it never lights.
- A paused fuse holds its milliseconds, and the spare refilling the minute puts it out.
- A withdrawal lowers the capacity, and the next boundary returns the excess to the cells.
- A pick keeps the books with a charged vault; Ctrl+Z brings its charge back.
- A wreck and a dumping vault are never charged.
- The window never draws the own charge.
- `PowerExact` and `StoreExact` exact over a thousand random operations with charged vaults among them.

**Mutations:**
- a paid boundary puts the fuse out (red on the hole);
- the own charge pays before the pool (red on the consumer's test);
- the recharge before the consumers (red on the same);
- `MoveAtPick` writing a fresh `VaultContents` (red on `PowerExact`);
- no return of the excess (red on the withdrawal);
- `ChargeOf` blind to vaults (red on the identity).

*Estimate:* half a session, about fifteen tests. **The riskiest part is 5**, because it changes a rule IM-3's
tests pin, and they must be re-read, not just re-greened.

**Taken with it, to propagate:**
- decision 12's sequence (the vaults' minutes drain first, then the fuses light);
- 28's disarm;
- IM-3's pass decisions 7 and 11;
- 55's timings;
- the storm oracle in IM-6's row ("own charge, then fuse, then dump");
- TODO's "a `PowerExact` term".

## IM-3b — built (2026-09-29)

All seven decisions as recommended, built the same night in a worktree (he was playing TajMahal2 at the start),
then merged. **Suite 2,140** (+13). **Eleven mutations, eleven red**, each on its own test:
- a paid boundary puts the fuse out;
- the own charge pays before the pool;
- the recharge before the consumers;
- the recharge after the write-back;
- the pick's move writing a fresh `VaultContents`;
- no return of the excess;
- `ChargeOf` blind to vaults;
- a minute of the full bill, not the bill as it stands;
- a bill both cannot meet taking the pool anyway;
- a lenient codec for a negative own charge;
- zero seconds allowed.

What it is:
- **`ownChargeSeconds`** on a vault with a channel, 1–3,600, 60 when absent, refused on a crate and on any
  other kind. `MachineDefinition.OwnChargeMillis` is what the tick reads.
- **`VaultContents.OwnChargeMillijoules`**, in the codec's version 1 after the fuse.
- **`MachineSet.ChargeOf` reads it**, so `stored`, `entering` and `leaving` carry it, and `PowerExact` has
  no new term. `MoveAtPick` keeps it on the row.
- **`MachineTick.OwnChargeCapacity`**: `BillOf` for the minute, from the contents as they stand.
- **`PayHolds`** returns an own charge's excess to the pool first, then pays each bill from the pool as far
  as it goes and the own charge for the rest, all or nothing. A paid bill pauses a lit fuse.
- **`RechargeVaults`**, after the consumers and before the pack, fills the minutes in fill order, and is
  the one place a fuse goes out.
- **Counters:** `HeldFromOwnMillijoules`, `OwnChargeGivenMillijoules`, `OwnChargeReturnedMillijoules`,
  `FusesPaused`; the report carries the boundary's three.
- **Game:** the panel's vault line gains `own X of Y (p%)` and `FUSE 12.0 s (paused)` (from a session-only
  `MachineRow.LastHoldPaid`); the `M10:` power line gains the own charges' share of `stored`, what they
  paid, what was given and returned, and the paused count.

**The hole, closed and measured** (the pass's fact 1 re-run as a test, a small vault with no minute yet,
income a share of its 1.25 J bill each boundary, fixed fuse seed):

| Share of the bill | 5% | 50% | 95% | 99% | 100% |
|---|---|---|---|---|---|
| Boundaries to the dump | 28 | 57 | 596 | 2,885 | never lights |

Under IM-3's rule, 5% held for 4,000 boundaries and 6% to 99% never dumped. The four fall inside the pass's
arithmetic (21–32, 40–70, 380–760 and 1,900–3,850).

One thing the build found: **a test that changes what a random stream is asked for changes the whole run
after it.** The thousand-operation identity test's bigger charges first came from its one stream. The identity
held, but no pick in the new path moved contents, so a coverage assertion failed. The bigger charges now come
from a second stream, and the first is asked exactly what IM-3 asked it.

**The r6 control after the merge** read `2601 / 325 / 3446 at 411`, 0 `ERROR`. **Owed:** nothing on my side.
IM-6 ships the grade files at 60 s and counts the recharge and the return as transfers on the runway.

## IM-4 — the code-level pass (2026-09-30)

Decisions 23–26 and IM-4's row, read against the water engine (`WaterSystem`, `BasinWater`, `LakeCeiling`,
`FluidSimulation` and its partials), `MachineTick`'s outlet ring, the tools and their loader, the pocket, and
the boundary in `Main`. The reading was done by three read-only searches, each reporting file and line; the
facts below are theirs, checked where they turned a decision. His two notes from 2026-09-29 are folded in:
- the siphon in the quantum tools' sizes;
- *"Is deleteing tier a water when placing voxels on the todo?"* (he took "delete" the next morning, and
  the cost below was found before building it).

Ten facts, then twelve decisions. **Nothing is built; the twelve wait on his word.**

**Ten facts.**

1. **`PourPending` is not the relay pump's.** It belongs to the debug relay, the R keys (`Main.cs:7595`).
   - It is one global slot: each R press overwrites it, and Alt+R zeroes it.
   - It pours through `FluidSimulation.Add`, which the identity counts as `injected`.

   So decision 26's "with the relay's pending pours" names the wrong thing. The right place is the
   boundary's safe window, `Main.cs:6734–6876`: after the hand-off and the portal drain, where `TakePending`
   and `PourPending` already run (6800–6801), and before the fills, the machine tick and `Publish`.
2. **The obvious counters are taken.** `MachineTick`'s pumped and emitted counters are documented equal to
   the growth of `FluidSimulation.Pumped`/`Emitted` and `WaterSystem.PumpedLitres`/`EmittedLitres`
   (`MachineTick.cs:519, 525`). Tests pin that (`MachineTickTests.cs:479, 2223`). So a siphon going through
   `Pump` or `Emit` would break those equalities. "Twin counters" has to mean new counters on both
   engines, the way `Lift` and `HandOff` got theirs.
3. **Tier A's identity is never evaluated in the game.** It lives in comments (`WaterSystem.cs:157–179`) and
   one test (`WaterSystemTests.cs:144`). The `M7d:` line prints Tier A's terms and checks nothing. Tier B's
   is checked, as `FluidPublished.IdentityExact`, `FluidSimulation.Job.cs:404`:
   `Total == Added + Loaded + Emitted − Removed − Displaced − Handed − Evicted − Pumped`.
4. **The crosshair passes through water.** `VoxelRay.Cast` stops at the first non-air material, and water
   isn't one (`VoxelRay.cs:126`), so nothing reports water at the crosshair. On top of that,
   `LakeCeiling.InLake` answers false wherever no band exists. Bands exist only for reachable columns or
   ones passed to `Cover()`, which the relay and the rain call first (`LakeCeiling.cs:146, 217`). The
   machine tick's lake intake does not call it.
5. **The tool format can't say "siphon".** The loader reads six keys: `id`, `radiusMetres`,
   `hardnessThreshold`, `cooldownSeconds`, `power {via, joulesPerVoxel}` and `bite`. It is strict, so a
   `takes` key fails to load (`ContentLoader.cs:1136–1150`). There is no "a tool with takes sorts last" rule:
   T sorts by threshold, then bite, then ID (`ToolTiers.cs:30–38`), so a siphon at threshold 0 would come
   first. Power is per voxel only, so 1,000 J a litre can't be written today.
6. **The store holds water as a field, not an ID.** It is `VaultContents.WaterMillilitres`, and nothing
   writes it in play; `overburden:water` does not exist. The pocket, meanwhile, has nine doors, all
   listed. One of them, `--stock=`, seeds every material the table has (`Main.cs:5089`), so an ID with no
   refusal would put water in the pocket the first time the flag ran.
7. **A vault may not declare a fluid link.** It has exactly one link, a power sink
   (`MachineDefinition.cs:598`), and `VaultTests` pins a vault with a fluid sink as refused. The ring holds
   bare `MachineRow`s, and `RoomAt` knows outlets only: a mouth, `IsLoaded`, `InLake` and the grid.
   `VaultState.Write` bumps no version (`VaultState.cs:218`), so a vault that fuses, dumps or is switched off
   is never noticed by a rebuild. Its room has to be read every boundary.
8. **The pump's stall is the first refusal it meets, not "full".** With an unloaded outlet first in the ring
   and a full vault second, it reports `Unloaded` (`MachineTick.cs:1420–1423, 1492`). The `NoRoom` comment
   (`:23`) claims more than that. And `Unpowered` is decided before any room is read (`:1398`).
9. **Placing into a lake: deleting the water can be exploited.** A fill-then-dig loop would become a lake drain. The excavator fills an
   8 m cube into a lake, deleting 512 kL, and digs it back out; the pair costs about 3.3 MJ at 50 J a voxel,
   which is 6 J a litre against a pump's 1,000. The same loop exists today in a flooded room with no room
   around it, where `FillWater` counts the rest `displaced`. Today placement into a lake takes no water at
   all: the lake is one number, and `HoldsWater` answers for Tier B only.
10. **Two doors lack the mid-tick guard.** `RefuseMidTick` guards `Add`, `Remove`, `Pump`, `Emit`, `Lift` and
    `Return`, but not `HandOff` or `DisplaceLifted` (`FluidSimulation.Ownership.cs:163`). Both are only called
    at the boundary today, so it is latent.

**Decisions.**

1. **Split IM-4 into three.**
   - **IM-4a (Core):** the siphon's queue, its two doors and their twins, the water ray, the ID and the
     pocket's refusal, and the guards.
   - **IM-4b (Core):** the vault in the ring.
   - **IM-4c (Core and a little wire):** the lake fill, decision 10.

   The siphon on T, its right click and the hotbar slot stay IM-6's, as the split says.
   *Cost:* three sessions, not one. *Estimate:* 4a is a long session of about thirty tests, 4b and 4c
   half a session each.
2. **The queue lives in Core, in a `Siphon` class, and drains in the safe window** (fact 1), right after
   `TakePending` and `PourPending` and before the fills and the machine tick. A click only enqueues. The
   joules, the store and the water all move at the boundary. A quit before it moves nothing and pays
   nothing. This is decision 26's rule, placed where the code has room for it.
3. **New twins on both engines, never `Add`, `Emit`, `Pump` or `Remove`** (fact 2).
   - Tier B gets `SiphonedMillilitres` and `PouredMillilitres`, both mid-tick guarded, and its checked
     identity gains `+ Poured − Siphoned`.
   - Tier A gets `SiphonedLitres` and `PouredLitres`, seam-checked against the siphon's own counters (fact 3:
     there is nothing else to check them against). A pour into a lake whose basin spills counts the spill
     through `SpilledLitres`, as an outlet's does.
   - The store's water line is `held + owed == entering − leaving + vaulted + siphoned − poured`; the dump
     adds its terms in IM-5.
4. **The aim: a Core water ray** (fact 4). It walks the view and stops at the first voxel that holds Tier B
   water or lies in a lake, calling `Cover` for the columns it crosses. It gives up at the first solid.
   - **A take** anchors its shape on that voxel.
   - **A pour** anchors on `Hit.Previous`, the air before the surface, as the fill does.

   Both snap to the tool's lattice.
   *Cost:* one ray walk a click; `Cover` is the relay's own call.
5. **His sizes: three siphon files**, one each in the miner's, the tunneller's and the excavator's bite:
   - the 1 m cube, 1,000 L, 1 MJ;
   - the 2 m cube, 8,000 L, 8 MJ;
   - the 8 m chunk, 512,000 L, 512 MJ.

   The tool format gains two keys, `takes: "water"` and `power.joulesPerLitre`, in place of
   `joulesPerVoxel`. T sorts every water tool after every rock tool (fact 5's missing rule). Cooldowns match
   the rock tools'.
   *Cost:* T has eight stops, not five. *The other arm:* one siphon whose size B cycles, which puts a
   second meaning on a key.
   *Revisit:* he finds eight presses too many.
6. **A budget per boundary, and one boundary's worth of waiting.** A Tier B take or pour touches at most
   3,200 cells a boundary. That is the dump's figure (decision 33), measured at 2–4 ms by IM-0; the siphon
   gets its own budget until IM-5 decides whether the two share one. A Tier A op is one number and lands
   whole. So a chunk-sized Tier B take spreads over up to eleven boundaries, about 5 s, and pays as it
   moves. A click is refused `busy` only while a whole boundary's budget is already queued, so the 1 m
   siphon held on Q is never slowed. An estimate: 3,200 cells is the dump's budget, not a siphon
   measurement.
7. **Never take what the store can't hold, and never take on credit.**
   - A take is cut to the free room of the backpack's channel. Water goes to vaults that hold water, in
     fill order.
   - It is cut again to what the bank can pay at 1,000 J a litre, as the fill at range is: a partial move,
     never a refusal of the whole.
   - A pour takes from the last-filled vault first. What doesn't fit where it lands stays in the store.
   - The bank is drawn before the machine tick runs, so decision 8's window draw is legal there.
8. **The water ID ships in 4a, and the pocket refuses fluids at one door** (fact 6).
   - `water.json` is fluid and not placeable.
   - `VolumetricInventory` is handed the table's fluid IDs and refuses them in `Add`, with a counter.
     That one site covers all nine doors, `--stock=` included, and is decision 23's "by every path" made a
     single test.
   - The ID's other reader, the hotbar slot, is IM-6's.
   - The store keeps water as millilitres on the row, not as the ID.
9. **A vault joins the ring by kind and `holdsWater`, not by a link** (fact 7). The validator and its test
   stand.
   - The ring's entries are tagged, outlet or vault.
   - A vault's room is `FreeMillilitres`, read every boundary. A vault that is switched off, dumping, a
     wreck or unreadable refuses.
   - It weighs its `weight` port if it declares one, else 1.
   - What a pump puts in it is `vaulted`, never `emitted`, so `pumped == emitted + vaulted`.

   *Cost:* a mod can't make a water vault that pumps skip. *The other arm:* let a vault declare a fluid
   sink, which changes the validator and its test.
10. *(Withdrawn at IM-4c, 2026-09-30: he kept today's rule, a pier that deletes nothing, and asked for
    displacement as its own pass; see "IM-4c — the code-level pass", his answer.)*
    **◆ The lake fill: a placement pays 1,000 J for every litre it deletes** (fact 9; this is his "delete",
    with its price). A voxel placed in a lake deletes 15.625 L, so a 1 m cube costs 1 MJ. The deleted litres
    are a printed Tier A term, with a millilitre carry per basin. Tier B's `displaced` remainder, in a full
    room, pays the same. With no bank to pay, the deleting voxels are refused `Wet` and the rest of the shape
    lands. So no door removes water for less than the siphon's price.
    - `HoldsWater` answers for lake voxels too, after `Cover`, which is the one read TODO named. So a
      placement in a lake queues to the boundary, as a wet one does.
    - Water only moved around the shape stays free.

    *Cost:*
    - Building in a lake needs a charged bank, or a lead-acid pack, before quantum.
    - Every placement near a lake takes the boundary path.
    - The full-room rule changes a played behaviour, from free to priced.

    *The other arms:*
    - Delete for free, as taken. The drain stays, printed.
    - Keep today's rule: nothing moves, and the lake doesn't see the rock.

    *Revisit:* building in lakes reads as blocked in play.
11. **The stall stays `NoRoom`, the first reason met** (fact 8). A full vault in the ring reads `NoRoom`.
    The enum's comment is corrected. No `Full` is added.
    *The other arm:* a `Full` stall, which every switch on `PumpStall` would have to learn.
12. **Guard `HandOff` and `DisplaceLifted`** in 4a (fact 10). They are one line each, and the rule becomes
    total.

**IM-4's tests.**
- M8's oracle and the machine tests unmoved with no vault and no siphon.
- A take then a pour on a Tier A lake restores the basin exactly.
- A Tier B take then a pour: `IdentityExact` holds with the new terms.
- A click then a quit moves nothing and pays nothing.
- A take is cut to the store's room and to the bank; a chunk take spreads over boundaries and pays as it moves.
- The water ray finds a lake with no band until `Cover`.
- The pocket refuses water from `--stock=`, `Add`, a withdrawal and an undo.
- A pump fills a vault in the ring, and `pumped == emitted + vaulted` holds.
- A switched-off or dumping vault refuses the ring.
- (IM-4c) A cube in a lake costs 1 MJ and deletes 1,000 L. With no bank it is refused `Wet`. A full room's
  displaced water pays too.

**Mutations:**
- the siphon through `Add` (red on the Tier B identity; *run, it is red on the siphon's `Exact`, not the identity,
  because `Add` counts `injected` and the identity balances*);
- `vaulted` counted as `emitted` (red);
- a take with no room check (red on the store's line);
- the pocket's refusal removed (red on `--stock=`);
- `Cover` skipped in the ray (red on the band test);
- the lake fill unpriced (red on 4c).

**The riskiest part is 4a's twins across two engines inside the boundary window.** The second riskiest is
4c's change to `HoldsWater`, which moves every placement near a lake onto the boundary path.

**Found alongside, fixed:** the fill-stopped-short line printed a window's own charge, always 0 mJ.

**Taken as recommended, all twelve, 2026-09-30** (*"As recommended please Claude, build IM-4a"*).

## IM-4a — built (2026-09-30)

Built in a worktree, then merged. **Suite 2,157** (+17 in `SiphonTests`, plus the four guards in `FluidJobTests`),
green first time but for one fixture that overfilled the bowl. **Closed early at his word.** The r6 control after the merge
read `2601 / 325 / 3446 at 411`, 0 `ERROR`. **Its mutation battery ran the next session: thirty-six, all red**, twelve of
them only after six new tests closed the holes they found ("IM-4a — the mutation battery", below).

What it is:
- **`Siphon`** (`Overburden.Core/Machines/Siphon.cs`):
  - `Enqueue` refuses a rock tool, no quantum backpack, or `Busy` (a boundary's worth already waiting).
  - `Apply` runs at the boundary and resolves each op's ray there. **The aim is resolved at the boundary,
    not the click**, one level below the pass's "a click only enqueues": the Tier B grid refuses reads
    while a worker owns it, so the op keeps the ray.
  - A lake op is whole litres, done in one go. A room op walks the shape's cells within
    `CellsPerBoundary` (3,200), top down for a take and bottom up for a pour.
  - A take is cut to the store's room and the bank. `Exact` checks every twin against the siphon's own
    counters.
- **The twins.**
  - `FluidSimulation.Siphon`, `Pour`, `RoomFor` and `HeldAt`, mid-tick guarded.
  - `FluidPublished` gains `Siphoned` and `Poured`, and `IdentityExact` gains `+ poured − siphoned`.
  - `WaterSystem.Siphon` and `Pour` count `SiphonedLitres` and `PouredLitres`; a spill counts in
    `SpilledLitres`.
- **The store's water.** `MachineSet.DepositWater`, `WithdrawWater`, `WaterRoomOf` and `StoredWaterOf`,
  and a water line, `StoreWaterExact`, with water counted at add, remove and restore. IM-4b's pump into a
  vault must use a door of its own, not `DepositWater`, or the siphon's seam reads it as a siphon.
- **The bank.** `MachineTick.PaySiphon`, a new `PowerExact` term, `siphonPaid`, which is `IChannelBank`'s
  rule that every drawer counts its own.
- **The aim.** `VoxelRay.Cast` gains a stop predicate, the ray's own recorded revisit trigger.
- **The tool grammar.** `takes: "water"` and `power.joulesPerLitre`: a water tool refuses the sphere and
  `joulesPerVoxel`, and a rock tool refuses `joulesPerLitre`. `ToolTiers.Order` puts water tools after
  every rock tool, and `Default` never starts a world on a siphon.
- **The pocket.** `VolumetricInventory.RefuseMaterials` and `MaterialTable.Fluids()`. `Main` wires them
  before the restore, so `Reset`, `--stock=` and `P` all meet the one refusal.
- **`water.json`**: fluid, not placeable.
- **The guards** on `HandOff` and `DisplaceLifted`.
- **Game.** `Main` builds the siphon beside the tick and drains it after `PourPending`, before the rain and
  the machine tick. `M10: siphon — …` is printed when it did anything, and `PushError` fires if a twin
  disagrees. The M8b line gains `siphoned`, `poured` and the terms; M7d gains the litres; the power line
  gains `siphonPaid`. **Nothing enqueues in the game yet**: the three tool files, T, the clicks and the
  hotbar slot are IM-6's, so no dead tool sits on T.

**Figures from the tests** (the bowl fixture):
- A 1 m take from a lake: 1,000 L and 1 MJ. The pour back restores the basin to the litre.
- The excavator's chunk take on a flooded floor: 11 boundaries, never over 3,200 cells, paid as it moved.
- The tunneller's size into a 1 m³ store: cut to 1,000 L. A 500 L bank: cut to 500 L.
- Two hundred random takes and pours on both tiers: every line exact after each, and the lake's litres
  equal to its start less taken, plus poured, less spilled.

## IM-4a — the mutation battery (2026-09-30)

Owed from the build and run the next session, in a worktree because he was playing TajMahal2. **Thirty-six
mutations, each with its colour predicted before the run, and every prediction held.** Twenty-four went red
first time, each on a named test. Twelve stayed green on all 2,157 tests, so each was a hole; six new tests
and two asserts close them, and all twelve are red now. **Suite 2,163** (+6). Every file was restored
byte-identical (hash-checked). No Core or Game source changed, so no r6 control is owed.

**Red first time:**
- the pass's list: the siphon's pour through `Add` and its take through `Remove`, a lake take through `Pump`,
  a lake take with no room check, the pocket's refusal removed, `Cover` skipped;
- TODO's additions: the sort rule; `HandOff` and `DisplaceLifted` unguarded; the identity without
  `+ poured`, without `− siphoned`, and with the two swapped in the snapshot;
- the bank: `PowerExact` without `siphonPaid`, and `PaySiphon` uncounted;
- the cuts: a room take not cut to the bank, a lake pour not cut to the store;
- the shape and the queue: a pour filled top down, never `Busy`, a take that ignores the budget, a take
  anchored on the air before the surface, the lead-acid pack opening the store;
- the ID and the grammar: `Fluids()` listing every material, a water tool taking the sphere, `IsFree` blind
  to the litre price.

**Green on the whole suite, and what catches each now:**

| Mutation | Why nothing caught it | Caught now by |
|---|---|---|
| a room take with no room check | no Tier B take had met a full store | `ARoomTakeIsCutToTheStoresRoom` |
| a fresh world may start on a free siphon | the only siphon in a test costs, so `IsFree` ruled it out first | `AFreeSiphonSortedLastIsNeverTheDefault` |
| `HeldAt` unguarded; `RoomFor` unguarded | the guard test listed the siphon's doors, not its reads | two asserts in `ReadsRefuseWhileAJobIsInFlight` |
| a pour from the first-filled vault; a take filling the store in reverse | no test had two vaults with room | `ATakeFillsTheStoreInFillOrderAndAPourEmptiesTheLastFilledFirst` |
| the store's water line forgetting water entering at `Add`, leaving at a restore, or leaving at `Remove` | every vault was added empty | `TheStoresWaterLineHoldsAcrossAReopenAndARemoveWithWaterInAVault` |
| a take emptying its shape bottom up | every take emptied its whole shape, and either order does that | `APartialTakeLowersTheLevelFromTheTop` |
| a pour's spill left uncounted, by Tier A or by the siphon | the random battery's lake never filled, so nothing spilled | `APourIntoABrimmingLakeSpillsAndBothEnginesCountTheSpill` |

**What it found:**
- **The siphon through `Add` or `Remove` leaves Tier B's identity exact.** `Add` counts `injected` and
  `Remove` counts `removed`, so the identity balances whichever door the water takes. Only the siphon's own
  `Exact` names the wrong door; in the game, that is the `PushError` on `!_siphon.Exact`. The pass predicted
  "red on the Tier B identity", but the twin check is the line that catches it.
- **Two rules were caught only by the random battery**: a room take cut to the bank, and a lake pour cut to
  the store. By IM-3b's lesson, a random stream is a claim about the stream, and the next op added to it
  could move them. Each has a dedicated assertion now (`APartialTakeLowersTheLevelFromTheTop`, and a
  tunneller's pour at the end of the fill-order test).
- **The spill, measured:** a 1,000 L pour into a brimming bowl spills all 1,000 L, both engines count it, and
  Tier A's line with the siphon's terms (`held == injected + poured − spilled − siphoned`, the rig neither
  raining nor drawing) holds. It is the first place Tier A's identity is checked with them.

The pass's other two mutations, `vaulted` counted as `emitted` and the lake fill unpriced, belong to IM-4b
and IM-4c and run with them.

## IM-4b — built (2026-09-30)

The pass's decisions 9 and 11 fitted the code as written, so no new pass was needed; built the same day in a
worktree, on his *"yes please Claude, start IM-4b"*. **Suite 2,175** (+12 in `MachineTickTests`). Every test
that existed stayed green with the change in and no test touched, which is the pass's first test (M8's oracle
and the machine tests unmoved with no vault).

What it is:
- **Membership.** A channel vault with `holdsWater` joins its channel's ring in `Rebuild`, among the outlets
  in origin order, weighing its `weight` port (one when it declares none). A dictionary is the ring's tag,
  vault or outlet. A channel vault that holds blocks only, and a crate, never join. `RingOn` lists the vaults,
  `RingVaults` counts them, and `NoOutlet` now means an empty ring.
- **Room, every boundary.** `MachineSet.VaultWaterRoom`: its free millilitres, and none when it is switched
  off, dumping, a wreck or unreadable, which are the store view's own refusals. It is read off the bytes each
  time a pump reaches the vault, because a vault's writes move no version; two pumps on one vault each read it
  afresh.
- **The door.** `MachineSet.VaultWater`, the pump's own, counted `WaterVaultedMillilitres`. It throws on a
  shortfall, the outlet's rule. `DepositWater` stays the siphon's alone, so the siphon's seam never reads a
  pump's water as a take.
- **The lines.** `MachineTick.VaultedMillilitres`. `CrossTierExact` and the report's `Exact` are
  `pumped == emitted + vaulted`, and the store's water line gains `+ vaulted`. The equalities the pass found
  pinned (the tick's pumped and emitted against both engines') are unmoved.
- **Decision 11.** No `Full`; `NoRoom`'s comment now says it is the first refusal met.
- **Game.** The `M10:` tick line and the profile's `machtick` line print `vaulted` and the identity as
  `pumped == emitted + vaulted`; the profile's vaults line gains the store's water line. Nothing places a
  water vault in play until IM-6 ships the vault files.

**The tests:** a pump fills a vault alone in its ring; a lake pump fills one in whole litres; a vault with less
room than the offer passes the rest on; a full vault alone stalls `NoRoom`, and first in a ring lets the pump
move; switched off, dumping, a wreck and unreadable each refuse, noticed with no rebuild; a vault weighs its
port and a block-only vault is not in the ring; two pumps read one vault's room afresh; a lake pump passes over
a vault with under a litre of room; and a hundred random boundaries of the pump and the siphon sharing one
store, the vault switched on and off, with every line exact after each. The fixture's whole-world identity
counts the vaults now: the world is the tiers plus what the vaults hold, less what entered in restored rows.

**The mutation battery: sixteen, all red now.** Thirteen went red on the first run. Three did not, and each
found a weak or missing test:
- **A dumping or wrecked vault taking water, and an unreadable one read as empty, passed the whole suite**,
  though the refusal test had a row for each. After its first boundary the channel's cursor moved on to the
  outlet after the vault, which took the whole offer, so the second boundary never asked the vault. The test
  now stands the vault alone in its ring, and both are red. A switched-off vault had been caught only by the
  random run, for the same reason.
- **A lake pump's take into a vault not rounded to whole litres** I predicted green as equivalent, since the
  lake path recomputes the take from the litres `Pump` hands back. It is not equivalent. With under a litre of
  room, the pump asks the lake for no litres, reads that as a dry lake, and stops walking the ring, so the
  outlet after the vault gets nothing. A vault's room can be a fraction of a litre after Tier B water, so this
  is reachable. `ALakePumpPassesOverAVaultWithLessThanALitreOfRoom` pins it.
- One prediction was wrong the safe way: the outlet count not cleared on a rebuild, predicted green, was
  already caught by the ring's rebuild test.

The others: `vaulted` counted as `emitted` (the pass's mutation); the pump's door through `DepositWater`; the
refusals of a switched-off vault; a vault joining by kind alone; a vault-only ring stalling `NoOutlet`; the
store's line, `CrossTierExact` and the report's `Exact` each without `vaulted`; the door uncounted; the door
never writing the vault; the ring's tag never cleared; the room read only once a boundary.

**The r6 control after the merge** read `2601 / 325 / 3446 at 411`, 0 `ERROR`, and its `M10:` line prints
`0 vault(s)` and `pumped == emitted + vaulted across the tiers: exact`.

## IM-4c — the code-level pass (2026-09-30)

His *"start IM-4c please Claude"*. IM-4's pass took the lake fill as its decision 10: a placement pays 1,000 J
for every litre it deletes; `HoldsWater` answers for lake voxels after `Cover`; a full room's displaced water
pays the same; with no bank the deleting voxels are refused `Wet`. The placement paths, the payment paths and
Tier A were read before building, by three read-only searches, each reporting file and line; the facts that
turn a decision were checked. **Decision 10 does not fit the code as written**, in four places, so this pass
holds the build. Nothing is built; the decisions below wait on his word.

**Eleven facts.**

1. **The collapse asks `HoldsWater` too** (`CollapseDirector.cs:901`, `:1059`). Rubble landing on a wet voxel
   waits for the boundary, and a piece falling through water slows. So `HoldsWater` answering for lakes would
   change the collapse in every lake, and a test pins the pipeline's count of the question
   (`FluidPersistenceTests.cs:505`, 64).
2. **A lake is decided before the write, and needs a band.** `LakeBoundedTerrain.InLake` is false for a solid
   voxel (`LakeCeiling.cs:549`), and `InLake` is false wherever no band is built. `Cover` wakes Tier B cells,
   which the worker may own at the click, so the exact answer belongs at the boundary, as the siphon's does.
3. **Placement has one budget, a count of voxels at one price, and only a fill at range pays**
   (`PlaceController.cs:413`, `FillCostMillijoules` is zero unless entangled). A hand placement has no price
   path, and `Place.Put`'s budget cannot mix a 50 J voxel with a 15,625 J one.
4. **`Pack.Pay` covers both packs with no new term.** A lead-acid pack pays from its own charge and a window
   from its channel's cells, and both are counted in `packTaken` (`EquippedPack.cs:93–103`). The siphon's
   path, `PaySiphon`, refuses a lead-acid pack at the click, and would break `Siphon.Exact` if shared.
5. **Tier B's displaced water is known only after the write.** `FillWater.Settle` returns lifted water over
   the shape's box grown by four voxels and counts the rest `displaced` (`Place.cs:162–185`). Pricing it after
   the write is payment on credit.
6. **The band's top voxel is only part under water**: a band runs from seed + 1 to `floor(level)`
   (`LakeCeiling.cs:358`), so a flat 15.625 L there over-deletes by up to all of it.
7. **No carry of part-litres survives a save.** Tier A counts whole litres; the nearest precedent is the
   hand-off's per-pit millilitres, lost at quit (`SurfaceHandoff.cs:85`).
8. **Machines, rubble and five other writers bypass placement's checks**: the lamp, the courtyard, `--pump`
   bodies, the reconcile rebuild and the undo write raw. A machine cell with one wet voxel defers whole, and
   machine placement has no budget at all (`PlaceController.cs:800`).
9. **A live bug, in shipped code: Ctrl+Z does not drop the pending fills.** The boundary replays a queued
   shape whole (`PlaceController.cs:686`), so an undo pressed in the half second before it is silently redone,
   dry voxels and all. Today only wet and chunk fills take that path; under this slice every placement under a
   lake would.
10. **The periodic `M7d:` line never printed the pump's, the outlet's or the siphon's Tier A terms**; only
    the profile does (`Main.cs:5714`, `:5849`). Tier A's identity cannot be closed in the game (no rain
    counter, and the counters are the session's while `held` is saved).
11. **The watershed is locked against edits** (M9-PLAN), and `IVoxelEditor`'s remarks say a fill in a lake
    "is a pier and deletes nothing". Deleting litres leaves the watershed alone; the remark is what changes.
    The unmerged `water-column-test` makes edits raise the watershed, so there a block would also cut the
    lake's capacity.

**What the lead-acid pack pays** (its file: 3.6 MJ, 250 W recharge): a full pack fills 3.6 m³ of lake, and
each cubic metre after that takes 67 minutes of recharge. With the quantum backpack, the channel's bank pays.

**Ten decisions.** Four amend decision 10 (marked).

1. **◆ The lake is its own question, not `HoldsWater`** (amends decision 10). `IVoxelEditor` gains a lake read
   that placement asks before the write, after `HoldsWater`. `HoldsWater` stays Tier B's, so the collapse and
   its test do not move.
   *Cost:* one interface member and its pipeline forwarder. *The other arm:* the pass's `HoldsWater`, which
   makes rubble wait in lakes and pieces slow through them. *Revisit:* he wants rubble to delete lake water.
2. **A placement under a lake's level waits for the boundary** (as taken). At the click, a pure read (the
   basin's level against the seed surface, no `Cover`) decides "might be in a lake" and queues it with the wet
   fills. At the boundary the shape's columns are covered and the answer is exact.
   *Cost:* up to half a second before a block lands under a lake.
3. **The price: 1,000 J for each litre deleted, at arm's reach too.** A voxel wholly under the level deletes
   15.625 L (15,625 J); the band's top voxel deletes the part of it under the level. `Place.Put`'s budget
   becomes millijoules, the at-range price and the lake price added per voxel.
   *Cost:* `Put`'s budget changes shape, and every place call passes it. *The other arm:* whole voxels at
   15.625 L, over-charging the top one.
4. **Paid from the pack that is worn, lead-acid or window, through `Pack.Pay`'s path.** It is counted in
   `packTaken`, so `PowerExact` gains no term, and the lake's share is printed apart as `lakeFillPaid`. Never
   on credit: lake voxels past what the pack can pay are refused, and the rest of the shape lands.
   *Cost:* before quantum, 3.6 m³ a full pack. *The other arm:* the bank only, the siphon's path, which
   refuses a lead-acid pack and makes building in a lake impossible before quantum.
5. **◆ The refusal is `Unpowered`, not `Wet`** (amends decision 10). `Unpowered` already means "couldn't pay",
   and the log explains it. `Wet` would say there is water there, which a player can see, and not why.
6. **Tier A's door is `WaterSystem.Delete(basin, litres)`, counted `DeletedLitres`**, guarding the outlet as
   `Pump` does. Part-litres ride a per-lake carry keyed by pit, as the hand-off's does, for the session only:
   a quit leaves under a litre per lake in it, already paid for. `M7d:` prints the new term, and gains the
   pump's, the outlet's and the siphon's, which it never had (fact 10).
7. **◆ Tier B's full-room `displaced` stays unpriced in 4c** (amends decision 10). It is known only after the
   write, so pricing it needs a pre-count of the grown box's room, a piece of its own. The flooded-room drain
   is today's and stays, printed as `displaced`; the lake drain is the one this slice's rule would have opened,
   and it is priced.
   *Cost:* a player can still delete Tier B water by filling a sealed flooded room, for the fill's price
   (nothing at arm's reach). *Lever:* the pre-count. *Trigger:* `displaced` climbs in a play log.
8. **Only the player's blocks delete. Machines, rubble and the debug writers stay piers**, deleting nothing.
   A pump stands in a lake on purpose (M10a's lake intake), and machines have no budget. A pier moves no water,
   so no free door opens.
   *The other arm:* a machine pays 1 MJ a cell placed in a lake.
9. **Ctrl+Z gives back neither the litres nor the joules**, as for a fill at range today; the lake shows
   through at the level it has. So a fill and its undo cost the siphon's price, never less. **And Ctrl+Z drops
   the pending fills**, fixing fact 9's live bug, in this slice because this slice sends many more placements
   down that path.
10. **The remark and the branch.** `IVoxelEditor`'s "a pier" becomes "a block placed in a lake deletes what it
    fills, priced"; M9's locked watershed stands. *Revisit:* if `water-column-test` merges, a placed block cuts
    capacity too, and deleting as well would count it twice.

**Every door, priced after 4c** (the "price a door before recommending" rule):

| Door | Deletes | Costs |
|---|---|---|
| A block in a lake, then dug out | the litres under the level | 1,000 J a litre, the siphon's |
| A block in a lake, then Ctrl+Z | the same | the same; nothing refunded |
| A machine placed in a lake, then picked | nothing (a pier) | nothing |
| Rubble landing in a lake | nothing (a pier) | nothing |
| A block filling a sealed flooded room | Tier B's `displaced` | the fill's price, as today (decision 7) |

**IM-4c's tests** (as the pass had them, adjusted): a 1 m cube in a lake costs 1 MJ and deletes 1,000 L; one
voxel deletes 15 L and carries 625 mL; the top voxel is charged its fraction; with nothing to pay the lake
voxels are refused `Unpowered` and the dry ones land; a lead-acid pack pays and so does a window; arm's reach
pays; a collapse and a machine in a lake move no water; Ctrl+Z drops a pending fill; `PowerExact` exact
throughout. **Mutations:** the lake fill unpriced (the pass's), the top voxel charged whole, the carry
dropped, the pending fills kept on undo.

**The riskiest part is decision 3**: `Put`'s budget changes shape under every placement. *Estimate:* one
session, Core (`Place`, `WaterSystem`, the lake read) and the wire (`PlaceController`, `Main`'s prints).

**His answer (2026-09-30): today's rule stays, and displacement gets its own pass.** He wrote "as
recommended", and in the same breath, in his list's notes, that he would rather the water were displaced
than deleted: *"A rock placed in a lake. - um if we can displace the water, or as a worst case just drop it
from the sky into the same position like"*, and that he wants a placed wall to break a lake's sheet. Asked
which he meant, he took **"keep today's rule, plan displacement"**. So:
- **Decision 10 of IM-4's pass is withdrawn**, and with it this pass's decisions 1–8 and 10. A block
  placed in a lake stays a pier: it deletes nothing, so the fill-then-dig loop moves no water and needs no
  price. His notes, verbatim, and what the code already offers toward displacement are in
  [LAKE-EDIT-NOTES.md](LAKE-EDIT-NOTES.md).
- **Decision 9's bug fix is built** (below), and fact 10's `M7d:` terms, since both stand whatever the lake
  does. The lake read (decision 1) is not built: nothing would call it until the displacement pass says
  what it asks.

## IM-4c — built (2026-09-30)

What every answer needed, built in a worktree and merged:
- **Ctrl+Z while placements wait for the boundary cancels them, and that press does nothing else**
  (`PlaceController.CancelPending`, called first in `Main.Undo`). The boundary would otherwise replay a
  queued shape whole, redoing a wet fill's dry part the undo had taken back, or land a chunk fill after
  the press spent itself on an older step. A wet fill's dry part, placed at the click, stays: it is the next
  press's step. Nothing queued has been paid for or taken from the pocket yet (both happen when the boundary
  places it), so a cancel refunds nothing. The press prints `M10: undo — N placement(s) waiting for the
  boundary cancelled`.
- **The periodic `M7d:` line** prints Tier A's pumped, emitted, siphoned and poured litres, which only the
  profile had.

**How it was checked.** `PlaceController` lives in the Game assembly, which the test suite does not
reference, so this is the one piece of the slice with no unit test; it is ten lines. The solution builds, and
the r6 control after the merge read `2601 / 325 / 3446 at 411`, 0 `ERROR`, its `M7d:` line printing the new
terms. The hand check is his: click a fill into water, press Ctrl+Z at
once, and nothing should land.

## IM-5 — the code-level pass (2026-09-30)

His *"start IM-5 please Claude"*. Decisions 27–37 and IM-5's row, read against the vault's end of life
(`MachineTick.PayHolds`, the wrecks, the store's lines), the world's write path (`ChunkPipeline.SetVoxel` and
its hook, the undo, the support field and the collapse), and the water's doors and the disk (the relay's ball,
`RoomAt`, Tier A's doors, `SaveWorldJob`, the session). The reading was three read-only searches, each
reporting file and line; the facts that turn a decision were checked. **Nothing is built; the decisions below
wait on his word.** The slice is the plan's riskiest, and the reading bears that out.

**Fourteen facts.**

1. **A dumping vault has no way out.** The fuse burns to `Dumping` and leaves `FuseMillisLeft` at 0
   (`MachineTick.cs:990–995`); from then on the vault is skipped by the bill, the return and the recharge, its
   own charge stranded in `stored`. Once emptied it can be picked (`MachineSet.Vaults.cs:454`), and the
   journalled copy keeps `Dumping`, so Ctrl+Z would bring back a dead, empty dumping vault.
2. **Nothing removes a wreck.** `HoldsAnything` is true for any wreck, even an empty one (`:243`). The plan's
   "removed only once empty" was never built. Removing one through `Remove` journals it, so an unpaused
   removal would let Ctrl+Z re-add it and re-mint its own charge through `entering`.
3. **Vaults bill, fuse and dump unloaded.** `MachineSet` holds every row whether or not its chunk is loaded,
   and `PayHolds` has no loaded check. Arrivals are handled every frame, outside the boundary, and water is
   loaded later than voxels (`onFluidReady`), so a release at arrival has to wait for a boundary with the
   water in.
4. **The siphon owns the pour twins.** `Siphon.Exact` reads every millilitre of `PouredMillilitres`,
   `PouredLitres` and the store's water doors as its own (`Siphon.cs:215–222`). A dump through any of them
   would light the siphon's error every boundary.
5. **`RoomAt` can't be reused as it is** (`MachineTick.cs:1595–1668`, private). Its dry room is one 4 × 4
   voxel layer, `BrushCell.Voxels` square (250 L at most; this read "8 × 8, 1,000 L" until the 2026-10-09
   re-read), not a ball, it never calls `Cover`, so a lake with no band reads dry, and it asks
   `IsLoaded` of one voxel. Its classification (lake, ground, underground, off the window) can be lifted into
   a helper; it reads only the row's cell and origin.
6. **Decision 31 contradicts itself.** "Where an outlet's would" sends a loaded dry mouth on the ground to
   Tier B's layer; "loaded or not" sends it to Tier A. The two differ exactly there.
7. **Tier A's release needs a clock**, and the row has one to spare. `FuseMillisLeft` is 0 once burnt, and
   owed water is already on the row and saved (`VaultContents.WaterMillilitres` under `Owes`). A new field
   would mean a codec version, which `TryRead` treats as unreadable.
8. **Decision 36 is not a consistent cut.** An unload batch holds only the chunks unloading. The session
   holds the header's pocket and every machine row, while other loaded, edited chunks and all Tier B panels
   (which leave a tick after their voxels) are not in it. Carried together, a crash would mint mining credit
   for a hole never saved, lose a placement's debit, or lose a pile in a second chunk. `SaveAll`, the one
   consistent write, throws while a worker owns the grid, and unloads happen then.
9. **The duplicate decision 36 guards against is not new.** M10-PLAN decision 4 records it: the chunk is
   written at unload and the header at autosave, so every brush placement since M6c can be doubled by a
   crash between the two, "accepted". A dump's pile after a crash is that lag again.
10. **The world's write path checks no material.** `SetVoxel` writes any ID; only `Place.Validate` refuses
    air, unplaceable IDs and IDs past the table (`Place.cs:1077–1107`). Vaults hold whatever the pocket held,
    pumps, cells, panels and packs included. A loose voxel of a machine material is cleared to air **with no
    credit** at its chunk's next arrival (`MachineSet.cs:581–606`).
11. **Every write drives the whole hook**: the field's 4 KiB clone, `Disturb`, the portal tracker's full
    chunk scan on its first edit and a rescan after 64, the enclosure, the support field (the chunk and six
    neighbours re-derived, so natural overhangs beside a pile can arm), light, and a remesh of up to eight
    chunks. A chunk written every boundary never settles, so collapses in and beside it wait. The only cost
    figure is 1.5 µs a voxel, Debug, from before the build fix. **The heap half of decision 33 is unmeasured.**
12. **The undo writes each step's old material back blindly** (`EditHistory.cs:395–402`), so a pile needs
    `Forget` on every chunk it writes; its only caller today prints "left the loaded box".
13. **`HoldsWater` is a tick stale at the dump's place in the boundary** (the publish follows the machine
    tick), and a chunk whose water hasn't loaded reads dry; the collapse waits on `IsReady` for the same
    reason. Wreck cells are not protected from block placement, so the pile has to ask `MachineSet.TryGet`.
14. **The collapse's cyan glitch is one mesh node per piece** (`CollapsePictures.cs:337–386`), 11.7–21 ms a
    frame when it was tried at scale. Glitching a pile's voxels as they appear needs a MultiMesh with its own
    per-instance data: wire work.

**Sixteen decisions.** Three amend the plan (marked ◆).

1. **Split IM-5 in two.** **IM-5a (Core):** the water, the end of a dump, the bracket. **IM-5b (Core and a
   bench arm):** the pile. The water mirrors IM-4a's twins; the pile runs under every hook in fact 11 and needs
   its bench before its budget is real.
   *Cost:* two sessions, not one.

   **IM-5a**
2. **Where the water goes** (decision 31, made exact). The mouth is the cell above the vault's top, as an
   outlet's. It goes to **Tier A** if the mouth is in a lake (asked after `Cover`), or if it is above the seed
   surface inside the window, loaded or not: decision 31's letter. It goes to **Tier B** if the mouth is at
   or under the seed surface (a vault in the workings) and its chunk is loaded to the water. Otherwise it is
   **owed**. `RoomAt`'s classification moves into a helper both use; its room does not.
   *Cost:* a vault on the surface raises its basin's lake, which may be some way off. *The other arm:* the
   outlet's rule, a loaded surface mouth pouring Tier B at the vault, a local flood through the automaton at
   up to the 10 ML ceiling.
3. **Tier A's door: `WaterSystem.Dump(basin, litres)`, counted `DumpedLitres`**, a spill counted in
   `SpilledLitres` as `Pour`'s is, the outlet guarded. **The release clock is `FuseMillisLeft`**, set to
   10,000 when the fuse burns out (and when a wreck is made). Each boundary pays `ceil(owed litres ÷ boundaries
   left)`, so any size lands in about 10 s. What is under a litre at the end is decohered. No codec change.
4. **Tier B's door: `FluidSimulation.Dump(voxel, millilitres)`, counted `DumpedMillilitres`**, mid-tick
   guarded, and `IdentityExact` gains `+ dumped`. **The ball**: around the mouth, the lake ceiling covered
   first, filled bottom-up layer by layer through `RoomFor`, visiting at most the boundary's budget of cells,
   as the siphon's `PourB` does. **Its own budget**, 3,200 cells a boundary across all dumping vaults,
   round-robin by position with the remainder to the first, apart from the siphon's.
   *Cost:* both at once bound the worst at about 8 ms. *Revisit:* the boundary line's `dump` and `pour` terms
   together pass 8 ms.
5. **The ceilings, applied when the fuse burns out.** All dumping vaults together owe Tier B at most 10 ML;
   what a new dump would add past that is decohered at once, whole. Tier A has no ceiling. (The heap's
   10,000 m³ is 5b's.)
6. **◆ The end of a dump** (fact 1; amends decision 34, which never said what "the end" is). When the last of
   its contents has left, **a standing vault goes back to `Holding`**: empty, billing nothing, its own charge
   back to the pool at the next boundary, as any empty vault's is (an empty vault's own-charge capacity is
   zero; corrected at the 2026-10-09 re-read, which found "kept and refilled" was not what the code does). **A wreck with nothing left is removed with the journal paused**, its
   stranded own charge leaving through `leaving`. So Ctrl+Z can't revive a dead vault, and a wreck doesn't
   linger for ever.
   *The other arm:* the dump destroys the vault at its end.
7. **Owed while unloaded, released at a boundary** (decision 35, made exact). Water bound for Tier B waits
   until the mouth's chunk is loaded to the water, then goes at the next boundary. It never goes at the
   arrival, which runs every frame outside the boundary. Surface water goes to the basin either way.
8. **◆ The crash lag is accepted, as placement's is** (facts 8 and 9; amends decision 36). A pile, or a ball's
   water, in a chunk that unloads, followed by a crash before the next autosave, reopens with the pile and a
   vault still owing it. That is M10-PLAN's accepted lag, which every brush placement has had since M6c.
   **The lever is engine-wide and its own slice:** a chunk any door wrote (a placement, an outlet, the
   siphon's pour, a dump, a quarry) stays loaded until the next autosave, at most 30 s, so the chunk and the
   session land together.
   *Cost:* a game killed within 30 s of walking away from a dump reopens with the pile and a full vault, as
   it would with a wall just built. *The other arm:* decision 36 as written, which trades this duplicate for
   new mints and losses elsewhere (fact 8).
9. **The bracket: `dump`, after `RunMachineTick` and before the table check**, so a fuse that burns out
   releases in the same boundary, and a lake's rise lands before the ceiling's refresh. Its own term on the
   boundary line, never folded into `machines`, `pour` or `collapse`. Inside it: Tier A, then the ball, then
   (5b) the pile.
10. **The dump's own `Exact`**, its counters against every twin, with a `PushError` on a miss, as the
    siphon's. The store's water line gains `− dumpedA − dumpedB − decohered`; the voxel line gains its terms
    in 5b.

    **IM-5b**
11. **The heap: each voxel lands on the lowest dry air voxel resting on something solid, in the nearest
    column, ring by ring out from the vault, within 32 m** (decision 30's "bottom-up, then nearest", made a
    heap). A heap is supported as it grows, so the director rarely finds a pile voxel at risk. A voxel is
    refused if it is unloaded or its water hasn't loaded, wet (the grid's own answer, not the published one),
    in a lake band (after `Cover`), in a machine's or a wreck's cell, inside the player, or reserved by a
    landing collapse. Each dump keeps a cursor per column. With no room left inside 32 m, it waits, printed
    `no room`.
12. **◆ Only what can lie loose is piled** (fact 10; amends decision 30's "in their own colours"). Machine
    materials, items that aren't placeable (the packs, the computer), fluids and air are decohered, printed by
    material. A hole left by a removed mod piles as a hole. A loose pump voxel would be cleared without credit
    at its chunk's next arrival anyway.
    *The other arm:* they stay owed, and a dump holding a pump never ends.
13. **The undo forgets every chunk the pile writes**, with its own line and counter, and recording is off.
14. **The heap's ceiling: 10,000 m³ (640,000 voxels) a dump**, the excess decohered when the fuse burns out.
    Still the plan's estimate, and the bench checks it.
15. **The bench arm first**: voxels a boundary against decision 33's 5–8 ms, the field's clone and the seep
    rescan included, on the fixed build. The 512 a boundary stands until the bench says otherwise.
16. **The cyan glitch as blocks appear is IM-6's** (fact 14): a MultiMesh with per-instance data, drawn by the
    wire. 5b's pile appears plain.

**Every door, priced** (the rule from IM-4c: run each round trip before recommending):

| Door | What moves | Mints or loses |
|---|---|---|
| A fuse burns out, the dump lands | the vault's contents into the world | neither; every term counted |
| Power comes back mid-dump | nothing (decision 34) | neither |
| Ctrl+Z after a dump | nothing (5b forgets the chunks) | neither |
| The emptied vault picked, then Ctrl+Z | an empty `Holding` vault (decision 6) | neither |
| Over a ceiling, a pump in the vault | decohered, printed | loses, by design |
| A crash after a pile's chunk unloads | the pile twice | mints, as placement's lag does (decision 8) |

**Tests, 5a:** a Tier A release lands every litre in about 20 boundaries, the rest decohered; a Tier B dump
stays inside the budget and `IdentityExact` holds with `dumped`; two vaults share the budget round-robin
exactly; the 10 ML ceiling decoheres the excess; power returning after the burnout does nothing; an unloaded
vault in the workings owes until its chunk's water is in, then pours at a boundary; an emptied vault is
`Holding` again, and an empty wreck is gone with nothing for Ctrl+Z to bring back; the store's water line
exact throughout. **Mutations:** the dump through `Add` (red on the dump's `Exact`, IM-4a's lesson being
that the identity itself stays exact); the release clock never set; the ceiling skipped; the end state left
`Dumping`; the wreck removed with the journal on.
**Tests, 5b:** a fixture room takes every voxel once, as a supported heap; each refusal; a pump in the vault
decohered; Ctrl+Z can't touch the pile; the heap's ceiling; the bench.

**The riskiest part is 5b**: writes under every hook, beside the collapse and the support field.

**Taken as recommended, all sixteen, 2026-09-30** (*"As recommended please Claude uh just a plan"*). He
asked for the plan only that day, and for TODO to be read for what should come first and what collides;
that is TODO's "Order and collisions". Before IM-5a: the flaky scheduler test, the clay pond's suspected
leak, IM-0's windowed surface install (the 10 ML ceiling's render half), and main merged into the pond
branch.

### IM-5a — the re-read against the merged code (2026-10-09)

The pass predates the raised watershed's merge (`aadf38a`). Three read-only searches re-checked it at
`0d6e4ef`: Tier A, Tier B, and the vault with the bracket. The load-bearing facts were checked by hand.
**Every fact holds, every IM-5a decision is still buildable, and nothing new needs his word.** Nothing
under `Overburden.Core/Machines` has changed since the pass, the merge touched no `FluidSimulation*.cs`,
and the boundary's order is as it was (`Main.cs:7204–7281`). What moved:

1. **Decision 2 is simpler than it reads.** A lake band's floor is the seed surface plus one
   (`LakeCeiling.cs:344`, `:463`), so "in a lake" already means "above the seed surface". The tier is chosen
   by the mouth against the seed surface at the origin, as `RoomAt`'s unloaded branch already does
   (`MachineTick.cs:1653`): above it, inside the window, Tier A. `Cover` matters only to the ball.
2. **Where Tier A's litres land moved, and the tier did not.** A vault is solid, so it raises its own 2 m sample
   (`RaisedSurface.cs:116–119`, `SkyColumn.cs:400`), and the basin `TryBasinAtSample` gives is where that bump
   drains. A walled pond at least one sample (2 m) wide is its own basin, and the dump fills it. Under a roof,
   every sample is raised to the roof (the raise can't tell a roof from a wall, the pond pass's stated cost), so
   a vault inside a roofed building on the surface dumps outside it, where the roof's rain runs. Before the
   merge, the analytic ground sent it outside too, so decision 2's cost ("may be some way off") stands as
   written.
3. **Look the basin up every boundary.** Basins are renumbered at every `Install`, once a frame
   (`Main.cs:3948`, `:4157`), so a release over 10 s never stores one on the row. `WaterSystem.Pour` throws
   with no window installed (`WaterSystem.cs:663–666`), so the dump asks first. A dump in the 1.1–1.2 s between
   a wall and its re-analysis goes to the old lake, and `ShareByLevel` spreads it by level (`BasinLedger.cs:153`).
   Nothing is lost, and a share that leaves the window goes out through `SeedOverflowLitres`, which is the
   lake's term, not the dump's.
4. **The ball covers its whole box, and asks `IsReady` of every chunk it touches.** `PourB` never covers: the
   siphon's lattice shapes never cross a chunk column, and a ball can cross four (the relay covers its box,
   `Main.cs:8289`). `RoomFor` can't stand in for "loaded", because on the field arm it answers from baked bits
   for a chunk that has gone (`FluidField.cs:140–165`). Only `PourB`'s ten-line loop (`Siphon.cs:458–469`) is
   worth sharing. The siphon's budget is first come, first served (`:282–303`); the dump's own is round-robin,
   as decision 4 says. *(Built a little differently: the mouth's chunk gates the pour, and a cell in a chunk
   not loaded to the water is passed over, so a big ball near the loaded box's edge still pours. See "IM-5a —
   built", choice 4.)*
5. **`+ dumped` reaches five copies of the identity**, not one: `IdentityExact` (`FluidSimulation.Job.cs:411`),
   `MachineTickTests.cs:495–497`, `PumpBench.cs:332–334`, and the `M8b:` and quit prints (`Main.cs:8488`, `:8762`).
6. **The store's water line has no alarm.** `StoreWaterExact` (`MachineSet.Vaults.cs:76–78`, whose comment
   already waits for IM-5's terms) prints only in the profile's machines block. The siphon's `Exact` runs only
   while its queue is non-empty (`Main.cs:4472–4475`), so the dump's `Exact` gets its own gate, and checks the
   store's line beside its own counters.
7. **A wreck is removed after the tick, never during it.** `MachineSet.Add` and `Remove` have no tick guard
   (`MachineSet.cs:197–241`), and `Remove` changes `_rows`. A spent wreck goes from a collected list after
   `Run`, through a `MachineSet` method that uses its own `PauseJournal` (`:686–704`).
8. **`MakeWreck` must not restart a dump's clock.** When a dumping vault falls, `MakeWreck` runs on it
   (`MachineSet.Vaults.cs:583–604`). The 10,000 ms clock is set only for a vault not already dumping.
9. **The bracket re-stamps `t3`.** `collapse` is timed from `t3` (`Main.cs:7286`), which only the table block
   re-stamps (`:7232`). With the table check off, a dump that doesn't re-stamp it is billed to `collapse`.
10. **Tests run with the pool on.** Under `PowerOff` `PayHolds` never runs (`MachineTick.cs:718`), so no fuse
    burns.

**For 5b:**
- **Crates are outside the tick's vault list.** `_vaults` holds only channel vaults (`MachineTick.cs:1279`), and
  a crate can be wrecked. A crate holds no water, so 5a never meets one; 5b's pile walks every dumping row.
- **A vault row this build can't read never dumps.** It is counted in `VaultsWrecked`, and it stays out of
  every sum (`MachineSet.Vaults.cs:606–609`), as IM-3's decision 8 meant.

**The hand-off, checked.** Water under the seed surface is never handed (`SurfaceHandoff.cs:179`), so a vault in
the workings stays Tier B's whole. The merge's one change: ball water that reaches seed + 1 on an unraised
sample is handed even when walled in.

## IM-5a — built (2026-10-10)

Built in a worktree (`im5a`), so `start.cmd` kept building the clean main folder. **Nothing he plays changes
yet:** no vault ships until IM-6, so no fuse burns and no wreck is made in his worlds.

**What is there** (decisions 2–10, with the re-read):
- **`VaultDump`** (`Core/Machines/VaultDump.cs`), in the siphon's shape. It keeps its own counters, and its
  `Exact` checks four twins against them: Tier A's `DumpedLitres`, Tier B's `DumpedMillilitres`, and the
  store's `WaterDumpedMillilitres` and `WaterDecoheredMillilitres`. The store's water line is checked beside
  them.
- **The doors.** Tier A: `WaterSystem.Dump(basin, litres)`, `Pour`'s shape with its own counter; the outlet
  basin spills. Tier B: `FluidSimulation.Dump(voxel, millilitres)`, which shares `Pour`'s path through a new
  private `FillFromOutside`.
- **The identity.** `FluidPublished` gains `Dumped`, and `IdentityExact` gains `+ dumped`, as do the other
  four copies the re-read found.
- **The store.** `MachineSet.ReleaseWater` takes what landed and what was decohered, and sets the clock, in
  one read and one write. A standing vault left empty goes back to `Holding` there; an empty wreck is
  returned to the caller, which removes it after its walk through `RemoveSpentWreck`, with the journal
  paused. `StoreWaterExact` gains `− dumped − decohered`.
- **The clock.** `PayHolds` sets 10,000 ms when a fuse burns out. `MakeWreck` sets it too, unless the vault
  was already dumping.
- **The game** (decision 9). The `dump` bracket runs after the machine tick and stamps `t3` again. `dump` is
  on the boundary line's mean and worst. `M10: dump —` is printed when a vault starts or ends its dump, and
  every tenth boundary in between. A `PushError` fires when `Exact` fails. Tier B's `dumped` is on the `M8b:`
  and quit lines and Tier A's on the `M7d:` lines, and the profile's machines block has the store's full
  line and a `dump` line.

**Six choices the pass left open, made in the build. Taken as built, 2026-10-11** (*"yes as built please
Claude, go on IM-5b"*):
1. **The ball's radius holds what is owed, and is never under 2 m (8 voxels).** At 2 m a small dump's ball
   reaches the floor around its own cell. A 10 ML ball is about 13 m.
2. **A whole pass that lands nothing rests the ball for 10 s.** Without it, a vault in a full room would spend
   its share of the budget every boundary for nothing. The plan named "no room" only for 5b's pile.
3. **The ceiling is checked the first boundary a vault is seen owing**, in the dump's bracket, not at the
   burnout in `PayHolds`. For a fuse that is the same boundary, and it covers a wreck too, since a fall starts
   a dump as well. The check is held in memory and repeats harmlessly after a reopen: what was admitted
   fitted, and owed water only falls.
4. **The mouth's chunk gates a pour; a cell in a chunk not loaded to the water is passed over.** The re-read
   said "every chunk the ball touches", which would leave a big ball near the loaded box's edge waiting for
   ever.
5. **An owing vault with nothing in it ends at the next boundary.** No path makes one; this heals a row read
   from disk.
6. **The release clock runs down every boundary a vault owes, whichever tier.** So a lake's share that waited
   off the window lands at once when the window reaches it.

**Tests:**
- Thirteen in `VaultDumpTests`, over the machine tick's bowl and a room dug under the rim:
  - a lake's share in exactly 20 boundaries, with 678 mL decohered;
  - a vault off the window owing;
  - a ball within the budget until its vault is empty;
  - the round-robin's shares (1,068 / 1,066 / 1,066);
  - a cell in an unloaded chunk passed over though its field says it holds, with the loaded arm beside it;
  - a ball covering the lake's bands before it pours, from a pit under the bowl;
  - a rest and its waking;
  - the ceiling, decohering 2 ML once and nothing after a reopen;
  - a vault owing while unloaded, pouring only at the boundary;
  - power coming back changing nothing;
  - the ends: a vault holding again, and a wreck gone with nothing for Ctrl+Z, its own charge leaving
    through `leaving`;
  - an empty owing row healed;
  - a fuse burning out through the tick and releasing in the same boundary.
- One more in `VaultTests`: a fall starts a wreck's clock, but not mid-release. The existing burn test now
  asserts the clock.
- Every test checks both tiers' identities, the published one, the store's line, the dump's `Exact`, the
  pool, and the whole world's water. **Suite 2,261.**

**The mutation battery: 28 of 28 red.** It covered the plan's five (the dump through `Add`, the release
clock never set, the ceiling skipped, the end state left `Dumping`, the wreck removed with the journal on) and
twenty-three more:
- each door and counter skipped;
- the identity and the publish without `dumped`;
- the store's line without `decohered`;
- the tier forced either way;
- the pacing all at once, and the sub-litre rest kept;
- the mouth's gate and the cell's gate;
- the cover skipped;
- the budget ignored or not shared, and the remainder to the last;
- the rest never set;
- the admissions forgotten;
- `MakeWreck` restarting the clock;
- the heal skipped, and the ceiling's excess counted but not released.

The cover would have gone green: the fixture refreshes every loaded column's band, so nothing depended on
it. That was seen while the battery was being written, so its test was added first. The test takes the
bowl's columns out of the reach.

**On main** (fast-forwarded the same night): the r6 control to the digit, 2601 / 325 / 3446 at 411, no
`ERROR`. **Owed:** IM-0's windowed surface install, which sets the render half of the 10 ML ceiling.

## IM-5b — the re-read against the code (2026-10-11)

His *"yes as built please Claude, go on IM-5b"*. Decisions 11–16 re-read at `fd3d695` (main with IM-5a) by three
read-only searches: the write path and its cost, the heap's site search and refusals, and the undo, the collapse
and the boundary's writes. The load-bearing facts were checked by hand. **Every decision still builds except
decision 11's rule, which needed a shape; he gave it.** What moved:

1. **Decision 11, read literally, builds a tower or a pancake.** Machine voxels are solid and only the cell is
   refused, so "nearest column first" fills the vault's own roof upward; "lowest first" lays one voxel over
   the whole 32 m disc before the second. **Taken 2026-10-11, his word: the cone, one up for one out.** Each
   voxel goes to the free slot with the least *height above the vault's floor plus horizontal distance from the
   vault*. It fills hollows by the vault first, then covers it, and grows a round heap at 45°. A cone with
   height equal to radius puts 100 m³ in about 4.6 m of radius, the demo's 700 m³ in 8.7 m and the ceiling's
   10,000 m³ in 21 m (arithmetic), all inside decision 11's 32 m. The other arms were two up for one out
   (17 m for 10,000 m³, fewer chunks a boundary) and one up for two out (27 m, more). His look at the pile
   is still the revisit.
2. **A dump that owes only blocks is invisible today.** `Run` collects a row only if it owes water
   (`VaultDump.cs:270–280`), and `DidAnything`, `Exact` and the profile's gate ignore voxels. A wrecked crate
   holds no water ever (`MachineDefinition.cs:579–586`), so it is seen and skipped for ever. 5b collects every
   owing row.
3. **The store's voxel line has no dumped or decohered term.** `StoreExact` is `held + owed == entering −
   leaving + deposited − withdrawn` (`MachineSet.Vaults.cs:354–355`), and `VaultDump.Exact` doesn't read it.
   5b adds both terms, a per-material record of what was decohered, and the check.
4. **The pile's writes would be journalled.** `RunDump` (`Main.cs:4538–4560`) has no recording-off bracket,
   unlike `SweepCollapse` (`:6422–6432`). `Forget` is on `EditHistory`, not on `IEditJournal`, so Core can't
   call it: `VaultDump` reports the chunks it wrote and Main forgets them, with a counter of its own
   (`ClearedByForget` is shared with "left the loaded box").
5. **There is one write path and no batch.** `ChunkPipeline.SetVoxel` (`ChunkPipeline.cs:926–988`, Core), and
   every writer calls it a voxel at a time. Per voxel: the field's 4 KiB clone, only in a chunk that has a
   field (`FluidField.cs:297–319`); the seep index, up to 64 a chunk, then one whole-chunk rescan at the next
   `Flush` (`PortalTracker.cs:149–184`); the portal index; `Disturb`, cheap. **Once per chunk per batch:**
   copy-on-write; light, run on the sim thread under its 4 ms budget, and a whole tracked chunk column when a
   pile raises the sky (`:1044–1048`); a remesh of up to eight chunks (a worker job); a support derive of the
   chunk and its six neighbours (a worker job). **So a boundary's cost is mostly chunks touched, not
   voxels.** The bench sweeps both.
6. **Its costs land in two boundaries.** The seep `Flush` runs before `RunDump` (`Main.cs:7234`), so boundary
   k's rescan is paid at k + 1. `Field.Sync` runs after it (`:7321`).
7. **The lake bands are dropped every boundary** for columns with no Tier B water near
   (`LakeCeiling.cs:200–212`, refreshed at `Main.cs:7302–7306`). So the pile `Cover`s the box it writes into
   every boundary it writes, where the ball covers once a pass.
8. **Each refusal has a predicate callable at the bracket:**
   - loaded to the water: `Ready` (`VaultDump.cs:618–627`);
   - wet: the live grid's `FluidSimulation.HeldAt` (`FluidSimulation.Siphon.cs:92–96`). `HoldsWater` reads the
     published copy and misses this boundary's ball;
   - in a lake band: `LakeCeiling.InLake` after `Cover`;
   - a machine's or a wreck's cell: `MachineSet.TryGet` (wrecks are rows). A write into a standing machine's
     cell marks it damaged (`MachineSet.cs:359–360`), so this refusal is what keeps a pile from killing a pump;
   - reserved by a landing collapse: `CollapseDirector.Reserved` (`CollapseDirector.cs:127`; there is none in
     sweep mode);
   - inside the player: only Main holds the body, and only while walking (`Main.cs:8122–8123`), so `Run` takes
     the box as a parameter.
9. **"Can lie loose" has no predicate today.** `Place.Validate` refuses air, unplaceable and out-of-table IDs
   (`Place.cs:1077–1108`), not fluids or machines. 5b's is `id != air && (uint)id < Count && IsPlaceable(id) &&
   !IsFluid(id) && !IsMachine(id)` (`MaterialTable.cs:73, 85, 98`). The range guard matters because the codec
   reads any int32 (`VaultState.cs:183–191`). A removed mod's stand-in reads solid, placeable and not a
   machine, so it piles, as decision 12 says.
10. **A fuse in or beside a pile waits while the pile writes.** Every write queues its chunk and six
    neighbours (`SupportRegion.cs:277–309`), and `SweepCollapse` runs right after `RunDump` (`Main.cs:7295`)
    and reads `TryReadSettled`. A live collapse event does not wait; it replots from the last settled snapshot
    (`CollapseDirector.cs:570, 728`). A solid voxel in a reserved rest makes a flight replot (`:895–898`), so
    the reservation refusal is a courtesy, not a guard.
11. **A dumping vault can't be picked** (`MachineSet.Vaults.cs:555–566`), so a dump waiting on `no room` keeps
    its vault until the player digs room. That is decision 11's "it waits".

**The bench (decision 15), as it can be built.** `Overburden.Bench` has never built a `ChunkPipeline`; every
arm uses a hand-made world. But every type the real one needs is public, and the bench references Core. So
`--pile` copies the tests' `PipelineHarness` (`ChunkPipelineTests.cs:646–762`) and Main's observer wiring
(`Main.cs:1532–1547`, `:6893–7009`), and writes N voxels a boundary over K chunks, dry and wet (a dry pile pays
no clone). It times the write loop, the pipeline's update after it (light), the seep flush and the field's
sync, and counts the remesh and support jobs. **It can't see** the game's mesh installs on the main thread, so
the in-game probe owes the `dump` term and the frame line beside it.

**The bench, run (2026-10-11):** `--pile`, Release, two full runs at a machine control of 1.08× and 1.09×,
twelve boundaries an arm, a fresh world each. The two runs agree arm for arm. The boundary is flush + write
+ sync, ms mean / worst over boundaries 2–12:

| Voxels a boundary | 1 chunk column, dry / wet | 4 (6 chunks) | 16 (17–21 chunks) |
|---|---|---|---|
| 128 | 0.8–1.0 / 3.9 · 0.6–0.8 / 2.4 | 0.2 / 0.4 · 0.3–0.4 / 0.8 | 0.4 / 2.1 · 0.6–0.8 / 2.1 |
| **512** | **0.7–0.9 / 1.9 · 1.2 / 2.3** | **1.7–2.0 / 4.2 · 2.0–2.3 / 4.0** | **0.6–0.8 / 1.5 · 1.2–1.6 / 3.3** |
| 2,048 | 2.7–3.0 / 10.3 · 2.9–3.8 / 7.8 | 2.5–3.2 / 5.6 · 4.0 / 6.0 | 7.3–7.4 / 15.9 · 8.1–9.3 / 17.8 |

- **512 a boundary stands** (decision 33's 5–8 ms): 2.3 ms mean and 4.2 worst at most, however it spreads,
  dry or wet. At 2,048 over sixteen chunk columns the seep rescan alone is 5.6–5.7 ms mean, and the bracket
  passes the budget.
- **The first boundary in fresh chunks pays the portal tracker's first-edit scans:** 1–16 ms, once a chunk a
  load.
- **The frames after a boundary scale with chunk columns, not voxels.** Light and the mesh apply on the sim
  thread: about 3–5 ms a boundary at one chunk column, 11–12 ms at four, 35–39 ms at sixteen. Light builds:
  12–18, 41–45 and 148–154 a boundary, because a pile raising the sky rebuilds its whole tracked chunk column.
  From four columns up the worst frame reaches the 16 ms light budget. Support derives: 10, 30 and 75–90 a
  boundary, 6–66 ms of worker.
- **Wet costs the clone:** 2 MiB allocated a boundary at 512 voxels (8 MiB at 2,048), and the write goes from
  0.6–1.8 to 1.3–2.3 µs a voxel.

**What the build takes from it** (build choices, for his word at "built"):
- **512 voxels a boundary across every dump, and at most 8 chunks written a boundary.** The chunk cap is what
  bounds the frames after, since chunk columns are the cost; 8 sits between the four-column arm (6 chunks) and
  the sixteen (17–21).
- **Each one-voxel shell of the cone is filled by angle around the vault**, so a boundary's 512 voxels work
  a wedge from the top of the heap to its foot, a few chunk columns, rather than a ring round the whole
  heap. The heap is still the cone at every shell.

## IM-5b — built (2026-10-11)

Built in a worktree (`im5b`), so `start.cmd` kept building the clean main folder. **Nothing he plays changes
yet:** no vault ships until IM-6.

**What is there** (decisions 11–14 and 16, with the re-read and the bench):
- **The pile, `VaultDump.Pile.cs`**, the other half of `VaultDump`, run last in the dump's bracket, after the
  balls (decision 37). The heap is kept by column: a column's slot is the air voxel its next block takes, one
  resting on something solid. The queue is ordered by the cone's shell, `floor(height above the vault's floor
  + distance)`, then the angle round the vault. A heap is seeded from the vault's cell and the ring round it,
  each opened at the first height from the mouth's layer down to the floor where it is open: the vault's top
  for its own columns, the floor beside it.
- **A column joins the heap when a neighbour's block lands at a height the column is open at**, and the block
  falls to what it rests on there. So a heap never passes through rock, climbs a wall only once it is piled as
  high, and spills over a sill. A drop past `DropDepth` (8 m) under the vault's floor is no slot, and nothing
  past `HeapRadius` (32 m) is reached. A machine's or a wreck's cell is solid to the heap, never a slot.
- **Refusals**, each waiting for the next boundary: the slot's chunk not loaded to the water, the live grid's
  `HeldAt`, a lake band (each chunk column covered the first time a boundary asks), the player's box (passed
  by Main, walking only), and `CollapseDirector.Reserved`. **A slot the world changed under is looked for
  again**: filled, up to a metre above; dug from under, it falls. The vault's own chunk gates its heap, so an
  unloaded vault waits rather than reading as "no room".
- **Admission** (decisions 12 and 14), when a vault is first seen owing: every material that cannot lie loose
  (`CanLieLoose`: placeable, not air, not a fluid, not a machine, inside the table) is decohered whole, then
  whatever is past `HeapCeilingVoxels` (640,000), from the highest material ID down.
- **The doors.** `MachineSet.ReleaseVoxels(row, material, dumped, decohered)` beside the water's, with
  `VoxelsDumped`, `VoxelsDecohered` and `VoxelsDecoheredByMaterial`. The end of a dump is one rule for both
  doors (`EndIfEmpty`). `StoreExact` gains `− dumped − decohered`, and the dump's `Exact` checks both twins and
  the voxel line.
- **The undo** (decision 13): the dump turns `EditHistory.Recording` off around its writes and forgets every
  chunk it wrote, counted apart (`PileStepsForgotten`), in Core, so a test can break it. Main hands it the
  history.
- **The walk.** `Run` collects every vault owing anything, so a vault owing only blocks, and a wrecked crate,
  are seen at last (the re-read's fact 2). `Run` takes the player's box.
- **The game.** `RunDump` passes the box; the `M10: dump —` line carries piling, voxels, chunks, waiting, no
  room, decohered and steps forgotten, and a second line names what was decohered by material. Decision 16:
  the pile appears plain; the cyan glitch is IM-6's.

**Choices the pass left open, made in the build. All seven taken as built** (6 on 2026-10-11, the rest in
his list's notes on 2026-10-12, his words beside each; 5 had no note, and his *"sweet"* closing the list is
read as taking it):
1. **512 voxels and at most 8 chunks a boundary** (the bench's), shared round-robin by position. *"Yes agreed
   anything can get lost or something"*.
2. **Each shell filled by angle**, so a boundary works a wedge (the bench's). *"Sounds good"*.
3. **A heap joins a column only through the air**: it never passes through rock, climbs a wall only once it
   is piled as high, and spills over a sill. *"Like its a quantum power failure event so i am happy with this
   or wierd stuff happening"*: a stranger heap would be fine too, so this is a floor on the physics, not a
   promise.
4. **A shaft deeper than 8 m beside a vault is left open**: no slot, and the heap stands round its mouth.
   *"Nah just trying to make it a pain to get close this should be enough"*: the heap's job is to make a
   burnt-out vault awkward to reach, and an open shaft beside it doesn't defeat that.
5. **The materials are piled mixed**, each voxel the one with the most left.
6. **A pile can rest on a machine**: a machine's cell is solid to it. So a pile can cover an outlet's mouth or
   a pump's intake, which then stalls as it would under any block. **Taken 2026-10-11, his word:** *"yes
   totally approve"*.
7. **The excess past 10,000 m³ goes from the highest material ID down.** *"sweet"*.

**Tests:** seventeen in `VaultPileTests`, on VaultDumpTests' fixture with the terrain as a world too:
- a room takes every voxel once, resting on something solid, as a cone (no free slot in a shell below the
  highest filled), and the vault holds again;
- three piles share the budget, the remainder to the first; nine piles in nine chunks, eight written;
- what cannot lie loose decohered by material; the ceiling from the highest material down, once, idempotent
  across a reopen;
- never into water (the vault's own ball pouring first), never into a lake (the pile's own cover, the bowl out
  of the reach), never into a machine's cell, the player or a reserved rest; a chunk with its voxels but not
  its water passed over; a column that waited asked again;
- an unloaded vault waits; no slot rests the heap and starts it again once the cell beside is dug;
- a hollow by the vault fills first and a shaft past 8 m is no slot;
- a slot filled between boundaries moves up, never overwritten, and one dug from under falls;
- the undo: a step in a chunk the pile wrote is forgotten; a player's open step elsewhere survives the
  pile's writes, and its undo restores the player's voxel;
- a wreck owing only blocks removed once its last voxel lands, with nothing for Ctrl+Z.
Every test checks the fluid identity, both store lines, the dump's `Exact`, that every voxel the store says was
dumped is in the world, and that no write went into water. **Suite 2,278.**

**The mutation battery: 42 of 42 red**, in three chunks of about 2.5 minutes, each mutation restored from the
file's bytes and the tree checked clean after. It covered:
- the undo: recording left on, the forget skipped;
- each refusal, the cover skipped, a machine's cell read as open, the vault's chunk gate, a slot whose water is
  not home;
- the budget: the chunk cap off, the share not split, the remainder to the last;
- the cone: the shell ignored, the distance ignored, the height ignored;
- the heap: no fall at discovery, the 8 m drop unbounded, a filled slot overwritten, a floating slot written,
  neighbours never joined, a waiting column never asked again, the rest never set, no room never noticed;
- what can lie loose: the check skipped, and each of its five clauses;
- the ceiling skipped, and taken from the lowest material;
- the doors: piled and not released, not counted, decohered and not counted, the voxel line without either term,
  a release that never ends a dump, the store's counter and its by-material record skipped;
- the walk: a vault owing only voxels skipped, the admission skipped, the pile never run.

**Seven holes were found before or during it, each closed with a test first.** Six came from planning the
recipes: the remainder (two piles split 512 evenly), the 8 m drop (no shaft in any fixture), a slot the world
changed (never exercised), a chunk whose water is not home (the test world could not say so), a column asked
again, and the fluid clause (shadowed by placeable in every fixture). The seventh survived the run:
`waiting-not-asked-again` went green, because a heap that lost its waiting columns found its queue empty,
rested as "no room", and reached the floor ten seconds later from a new seed. A fallback route hid the hole.
The test now insists the floor is reached the very next boundary with no rest, and the recipe went red.

**On main** (fast-forwarded the same night): the r6 control to the digit, 2601 / 325 / 3446 at 411, no
`ERROR`. **Owed at IM-6:** the in-game read of a real dump (the `dump` term, the frame line, and the remeshes
against the bench), with the pile's cyan glitch (decision 16).

## IM-6 — the code-level pass (2026-11-24)

Everything TODO put before IM-6 landed on 2026-11-24: the build warnings and collapse f-1. This pass reads IM-6's row
and the decisions it wires (11, 12, 21–23, 26, 28, 51–55, IM-3b's 6, IM-5's 16) against main at `4a2895c`. It
covers:
- the header, the world's open, the loader, the pocket and the row, the debug keys and the card;
- the HUD, the machine panel and the store's doors;
- the tools and T, the siphon, the content and its textures, the dump's wire and the glitch.

The reading was three read-only searches, each reporting file and line, with the load-bearing facts checked by hand.
The reader maps describe `71e6eed`; what moved since is folded into the facts. **Nothing is built; the decisions below
wait on his word.**

**Twenty-four facts.**

*The vault in play*
1. **No vault, crate or siphon file ships, and the grammar is ready.**
   - Seventeen materials ship, and four tools: the hand pick and the quantum miner, tunneller and excavator (the steel
     pick retired 2026-10-12).
   - The loader takes `"kind": "vault"` (`ContentLoader.cs:832-833`), with its rules at `MachineDefinition.cs:645-700`.
   - An ID's path may hold `/` (`Identifier.cs:143-144`). A material's ID is its `id` key, not its file name, and
     files are listed one folder deep (`ContentLoader.cs:403-425`, `IContentSource.cs:52`). So `quantum_vault/high`
     loads from `quantum_vault_high.json`.
   - **There is no label key.** A material's name on screen is its path with `_` read as a space (`Hud.cs:666`,
     `Main.cs:6084`), so `quantum_vault/high` would read "quantum vault/high".
2. **A texture is a JSON layer plus two Game tables.** `textureLayer` indexes an array built from
   `VoxelMaterial.LayerColour` (`VoxelMaterial.cs:47-98`) and a parallel `jitter` array (`:130`). A length mismatch is
   an error (`:131-134`). Layers 0–14 are taken, the backpack's cyan at 13 and lithium at 14, so new layers start at
   15. The HUD's swatch reads the same colour (`Hud.cs:617`). `water.json` has no layer.
3. **Six assertions, in four test files, hard-count what IM-6 adds:**
   - `ShippedContentTests.cs:236`, 7 machine materials;
   - `:270`, 5 materials that can't be placed;
   - `:113-115`, the exact four-tool cycle;
   - `:187-189`, the last three on T being the quantum rock tools (water tools sort after rock tools,
     `ToolTiers.cs:38-47`, so three siphons become the last three);
   - `CollapseTests.cs:1044`, 12 IDs appended to the v3 fixture;
   - `DebugSeedTests.cs:297`, the list of items.
4. **P can't seed a placeable machine without breaking the alpha's row.**
   - P credits the pump, outlet, panel, cell, clay and glass in that order, and leaves slot 9 for the first stone.
     `DebugSeedTests.cs:131-147` pins this.
   - Five vaults and a crate are placeable, so on the alpha's path the row would fill and stone would get no slot.
   - The backpack is an item, so it stays off the row (`MaterialTable.cs:113-119`, `Main.cs:1578`).
   - P still doesn't seed the backpack (`DebugSeed.cs:68-79`), as IM-2 left it.

*The bank on screen*
5. **The HUD shows the quantum backpack as an empty pack.** The pack line prints the pack's own charge and capacity
   (`Hud.cs:713-721`), so a window reads `pack 0 mJ / 0 mJ (–)`. Yet `EquippedPack.SpendableMillijoules` is documented
   as the number the HUD reads (`EquippedPack.cs:71-75`). The backpack's panel says it recharges (`MachinePanel.cs:238`,
   `:467-470`). Today only `--pack=` and `--stock=` reach a backpack, so nobody has seen either.
6. **None of the figures a runway needs is both per channel and public.**
   - The boundary report sums all sixteen channels (`MachineTick.cs:48-94`).
   - The hand's and the siphon's spend are lifetime counters outside the report (`:498`, `:523`), since they happen
     between boundaries.
   - The per-channel bank, `IChannelBank.BankMillijoules`, is internal (`IChannelBank.cs:9`,
     `MachineTick.cs:1384-1409`), and Core is visible to the tests only.
   - The HUD is pushed every frame, and is handed no machine set and no tick (`Main.cs:3194-3223`).
7. **The bank's own change is the wrong signal for a runway.**
   - Alt+P fills cells through `SetCharge`, counted as `entering` (`Main.cs:5420-5430`, `MachineSet.cs:168-183`).
   - A picked cell leaves through `leaving`.
   - A vault's recharge and return move charge between the cells and its own minute (`MachineTick.cs:1166-1182`,
     `:1257-1302`).

   Averaged over 10 s, picking up one full 5 GJ cell would read as 500 MW out (arithmetic).
8. **No watts formatter exists.** Energy is formatted by `MachinePanel.Joules`, in the Game (`MachinePanel.cs:669-679`).
   The panel prints power as energy a boundary: the bill (`:638`), a panel's output (`:551`), a pack's recharge
   (`:467-468`). A pump's power is never shown.

*The panel and the store's doors*
9. **The panel's vault line is IM-3's minimal branch** (`MachinePanel.cs:603-639`).
   - Its volumes have their own format, not `VolumeText`.
   - `DUMPING` doesn't say what the vault owes.
   - Ports are generic boxes written through `SetPort` (`:321-358`, `:443`), so the channel box has no lock.
   - **It never logs a vault.** The log line prints only when the stall kind changes (`:488-495`), and a vault's stall
     is always null.
10. **The store's doors exist, and the Game calls none of them.**
    - `Deposit` and `Withdraw` work on a channel: fill order going in, last-filled first coming out.
    - `DepositInto` and `WithdrawFrom` work on one row, refused unless its box is open (`MachineSet.Vaults.cs:501-534`).
    - There is no channel total or capacity for blocks. Water has `WaterRoomOf` and `StoredWaterOf` (`:287-315`).
11. **A move to the pocket could lose voxels without a word.** `WithdrawFrom` takes from the vault, then credits the
    pocket. The pocket drops a material it refuses into `RefusedVoxels` (`VolumetricInventory.cs:165-169`), a counter
    only a test reads. This can't happen today: water is held as millilitres on the row, and fluids are the only
    materials the pocket refuses.
12. **The computer reaches 512 m only with an at-range tier on T** (`PlaceController.cs:862-863`,
    `MiningController.cs:194`); otherwise it reaches 5 m. A right-click on nothing with a pack worn opens the pack's
    panel (`PlaceController.cs:582-586`, `Main.cs:3640-3657`).

*The siphon and water*
13. **Nothing in the Game reads a tool's `Takes`.** Only the loader, `ToolTiers` and `Siphon.Enqueue` read it.
    - A water tool on T today would carve and fill as a rock tool, and free, since its `joulesPerVoxel` is 0.
    - Both clicks need a water branch before the controllers are called (`Main.cs:6092-6155`, `:5856-5908`).
    - `Enqueue` (`Siphon.cs:231`) refuses a rock tool, no window, and `Busy`.
    - The siphon is drained at `Main.cs:7746`, inside the `pour` term, and nothing enqueues.
14. **A water slot would read `0 L`.** Every slot shows the pocket's amount (`Hud.cs:598-648`), and the pocket never
    holds water.
    - A saved row may name water (`HotbarSlots.cs:262-264`).
    - But water is an item, so it takes a slot only by a pin, and the hand can't hold water to pin it.
    - Selecting an item does nothing (`PlaceController.cs:568-571`).

*The dump's wire and the glitch*
15. **The dump runs and prints, and nothing on screen shows a fuse or a dump.**
    - `RunDump` (`Main.cs:4894-4926`) prints `M10: dump —` when a dump starts, when it ends, and every tenth boundary
      between. `dump` is on the boundary line.
    - Nothing glitches a fusing vault's body. The HUD has no vault or fuse text. The panel's line is the only fuse
      display.
16. **The pile reports chunks, not voxels** (`VaultDump.Pile.cs:127`, cleared each boundary at `:257`). So the Game
    can't put a glitch where a block appeared. The collapse's glitch is one node per mino with a per-node seed
    (`CollapsePictures.cs:337-386`, `CollapseGlitch.gdshader:11`), and the Game has no MultiMesh.
17. **The store's lines print only in a profile**, and the voxel line prints a shorter formula than the one it
    checks (`Main.cs:9594-9595` against `MachineSet.Vaults.cs:411-413`). The profile's power line prints an old
    formula too (`Main.cs:9652`).
    - The periodic `M10:` line carries the bills, the own charges and the fuse counts (`Main.cs:9561-9575`), but none
      of the store's voxel terms.
    - Nothing prints a crossing of zero.
    - `VaultState.cs:11` still says a paid boundary disarms a fuse.
18. **The `fluidsurf` trigger is a judgement, not code** (`WORKLOG.md:386-388`). The `M8b:` line prints the install's
    mean and worst (`Main.cs:8989-8995`), windowed only.

*The header and creative*
19. **The header is in Core and checks its own length.**
    - v8 appends nine int32 slots (`WorldHeader.cs:461-466`).
    - The writer throws on a length mismatch (`:468-476`), and the reader refuses leftover bytes (`:802-806`).
    - Every open rewrites the header at the build's version (`Main.cs:1685`), so a world a v9 build opens is refused
      by 0.1.3 by name.
    - The golden v7 fixture is embedded (`Fixtures/v7-header-before-the-hotbar.bin`, read at
      `WorldHeaderTests.cs:1056-1089`). The program that wrote it isn't in the repo, and there is no v8 fixture.
20. **A profile can make a world.**
    - `--world=` turns persistence back on for a profile (`Main.cs:2540-2542`, `:2853-2856`), so decision 51's "a
      profile has no world" is false then.
    - `--nosave` never sets `_newWorld` (`:10216-10226`).
    - Creation-only flags are warned and ignored on an existing world (`:10298-10332`).
    - `--mode=` is an unknown flag today.
21. **A placed cell starts empty** (`Place.cs:677`). A 1 GJ gift set through `SetCharge` would hide inside `entering`,
    beside restores and Alt+P. `PowerExact` (`MachineTick.cs:667-672`) has no `granted` term, and neither has the
    `M6c:` line (`Main.cs:10148-10161`).
22. **The debug keys have no mode gate, and `--dev` gates three of them.**
    - P, Alt+P, Ctrl+P, R, Alt+R, Alt+G and L always answer (`Main.cs:5333-5450`, `:8885-8932`, `:6273-6289`).
    - Ctrl+R's million litres, H and Alt+H answer only under `--dev` (`:5273-5313`, `:8924`).
    - `start.cmd` passes `--dev` (`start.cmd:49`). So if creative honoured `--dev`, every creative world he opened from
      it would have H.
    - The card is built once with the `--dev` flag (`Main.cs:3370`), and lists P, Ctrl+P and Alt+P as test keys
      (`ControlsCard.cs:68-70`). It opens itself at a new world's first open, in an export only (`Main.cs:3393-3398`).
23. **A loader section is a loop, and a missing one is silent.** Extras load before materials
    (`ContentLoader.cs:111-114`), and materials and tools are read by folder (`:403`, `:577`). A build that doesn't
    read `kits/` ignores it without a word, because unknown folders are never listed.
24. **The oracle has no doors yet.**
    - `--placeat=` is collapse f-2's (COLLAPSE-PLAN decision 13) and isn't built. f-2 is "not before IM-6", yet
      IM-6's oracle needs it.
    - Nothing headless can fill a vault, because the Game calls no store door.
    - `--pump`, `--cell=0`, `--solar=`, `--weather=1` and `--clock=` do exist (COMMANDS.md).

**f-1's finding, narrowed by hand** (the hand-over asked whether the playable vault needs more).
- A lit fuse goes out only in `RechargeVaults`, from what the pool holds after the consumers (`MachineTick.cs:1257-1302`).
- The held cells leave the pool before the consumers (`:975-983`), and get back at most what they held (`:1362-1367`),
  so they never gain charge.
- **Sun, and cells still on, still refill a vault's minute and put its fuse out.**
- So a fuse stays paused under a signal only when both hold:
  - every charged cell on the channel is held, and the sun covers no more than the bills;
  - charge reached a held cell after the fuse lit, through Alt+P, a cell switched on or moved onto the channel, or
    Ctrl+Z of a pick. When a fuse lights, the channel's enabled cells hold less than one boundary's bill between them.
- It burns again when those cells run dry. So the vault waits; it never dumps because of the signal.

**Twenty-six decisions.** Six are forks (◆). Three amend the plan: 12 amends decision 23, 21 decision 51, and 24
decision 52.

1. **◆ Split IM-6 in three.**
   - **IM-6a (Core, and nothing changes in play):** the runway's numbers, power and energy text, the store's moves and
     fill, and the pile's positions.
   - **IM-6b (wire and content):** the vault, crate and siphon files, P, the HUD, the panel, the siphon on T, the
     glitches, the lines, and `--placeat=`. **Playable: the bank, the vault, water and the dump**, in his sandbox
     world through P and Alt+P, with no save-format change.
   - **IM-6c (Core and wire):** the v8 capture, header v9, `--mode=creative`, the kit, the first cell's grant, the
     keys, the card, and the storm oracle. **Playable: a creative world.**

   This follows the Core/wire seam and puts the save-format bump last. The vault plays before creative exists, because
   sandbox already has P and Alt+P, and his worlds move to v9 only when creative is there to use.
   *Cost:* three sessions, not one, and the creative world arrives third. *The other arms:*
   - two slices: the vault in sandbox, Core and wire in one long session, then creative;
   - IM-6 whole, as the split row has it.

   *Revisit:* 6a proves small at its re-read; then it folds into 6b.

   **IM-6a (Core)**
2. **The runway is built from flows on each channel, never from the bank's change** (facts 6 and 7).
   - The tick keeps, for each channel, each boundary's generation, the bills paid from the pool, the bills paid from
     own charges, the pumps' draw, and the own charges given and returned.
   - Every draw between boundaries is counted per channel inside the bank's `Draw`: the window's today, the siphon's,
     and later the quarry's `dug` and recall's. A new drawer is then outflow by construction.
   - A Core `Runway` keeps twenty boundaries (10 s) for each channel:
     - in = generation;
     - out = bills from the pool + pumps + draws;
     - left out: own charges given and returned (IM-3b's decision 6), `entering`, `leaving` and Alt+P.
   - The runway is the bank ÷ (out − in) while out > in. It is amber under 10 minutes and red under 60 s.
   - The bank and the ledger get public reads. The draw itself stays internal.

   *Price:* it prints only, and no conserved quantity moves. Its test is an identity: each per-channel term sums to the
   global term. *Cost:* six arrays of sixteen. *The other arm:* the bank's change, less every door that isn't outflow,
   which each new door would have to remember. *Revisit:* the runway reads wrong in play.
3. **Power and energy text in Core** (fact 8).
   - `PowerText` sits beside `VolumeText`: W under 1 kW, then kW, MW and GW, one decimal under 100. It converts
     millijoules a boundary through the 500 ms boundary.
   - `Joules` moves to Core as `EnergyText`, with the same output, so both have tests.
   - The mod files stay in watts.
   - Readers: the HUD's bank line, a vault's bill, a panel's output, a pack's recharge, and a pump's draw, shown for
     the first time.
4. **The store's moves: one Core seam that the panel and M10g's screen both call.**
   - `ToStore` and `ToPocket` work on the backpack's channel. `IntoBox` and `OutOfBox` work on one aimed vault or
     crate. `OutOfCombined` takes from the pocket first, then the store, for the screen's combined box
     (M10-LADDER-PLAN, "His design for the screen's boxes", its point 2).
   - Each returns what moved. A move refused because a box is closed says why: switched off, dumping, a wreck.
   - **None of them moves a material the pocket would refuse** (fact 11), and `Withdraw` and `WithdrawFrom` get the
     same guard.
   - A store fill (used and inside, blocks and water together, per channel) feeds the HUD's `store 68%`.

   *Price:* a deposit and its withdrawal round-trip exactly, since both are counted in `StoreExact`. The guard closes
   the one silent loss. *Cost:* a small class the Game could have done without. It is there because the screen is
   coming, and because the Game has no tests.
5. **The pile's positions.** The pile records the voxels it wrote this boundary, at most the 512 of its budget, beside
   `PileChunksLast`, for the glitch (decision 14).
   *Cost:* 512 positions a boundary.

   **IM-6b (wire and content)**
6. **The vault and crate files** (decisions 15, 18 and 22, the grade table, and IM-3b's 1).
   - **Five grades**, `quantum_vault/low`, `/medium`, `/high`, `/mythical` and `/god`, with `insideCubicMetres` 100 up
     to 1,000,000.
   - **The water rates** are 25, 10, 4, 1.6 and 0.64 W/m³, and blocks a tenth of each (2.5 down to 0.064 W/m³). That
     is the grade table divided by volume (arithmetic): every number is still his to tune.
   - `holdsWater` true, `ownChargeSeconds` 60, a `channel` port 0–15 and an `enabled` port 0–1.
   - **No `weight` port**, so a vault weighs 1 in a pump's ring (IM-4b).
   - **The crate:** `insideCubicMetres` 1, `holdsWater` false, no links and no channel.
   - **The label is a name rule, not a key:** a path `a/b` reads "a (b)", so his "quantum vault (god)" comes from the
     ID (fact 1).
   - **Textures:** layers 15 to 20. The vaults are the entanglement's cyan on a dark body, brighter by grade, so the
     grades tell apart on the row. The crate is a timber brown. These are look calls, unscreenshotted.

   *Cost:* five permanent IDs, and a sixth for the crate; fact 3's six counts move. *The other arm:* one shared layer,
     which decision 18 allowed and which makes the five swatches identical.
7. **◆ P seeds the backpack, the crate and the five vaults**, a hundred each as it does everything else (decision 52's
   *"in sandbox, P also seeds the new machines"*). **Containers take a slot only by a pin**: the set kept off the row
   grows from items to items and containers (fact 4). So the alpha's path keeps slot 9 for stone, and its test stands.
   *Cost:* a vault is a V away, as the computer is. *The other arms:*
   - a key of its own for the store's set;
   - the high vault and the crate on the row, which leaves stone without a slot on the alpha's path.

   *Revisit:* he wants a vault on the row by itself.
8. **The HUD's bank line replaces the pack line while a window is worn** (decision 11, fact 5):
   `BANK 612 MJ ch 0 · +3.8 kW sun · −104 kW · runway 1 h 38 m · hold 2 kW · store 68%`.
   - The runway shows only while the net is negative, amber and red as decision 2 says. `store` shows when the channel
     has a store.
   - Main reads the ledger at each boundary and hands the HUD a record, so the HUD is still told, never asked.
   - `M10: bank —` prints each crossing of zero, both ways.
   - The backpack's panel says what a window is: the bank on its channel, nothing of its own, never recharged.
   - The lead-acid and lithium-ion packs keep today's line.
   - The tool's refusal stays a line on the HUD: there is no sound for his *"failed — insufficient charge"* yet.
9. **The stock section** (decision 21): on a vault's panel, a crate's, and the backpack's (the computer's right-click on
   nothing, which opens the whole store).
   - **At the top:** a capacity bar in `VolumeText`; the bill in watts with `own 118 kJ of 120 kJ`; and the state,
     one of `HOLDING`, `FUSE 12.0 s`, `FUSE 12.0 s (paused)`, `DUMPING — owes 3.1 ML and 120 m³` or `WRECK`.
   - **One row a material:** its swatch, name and amount, then **`→ pocket`** and **`→ store`** (`→ vault` on a vault's
     panel, `→ crate` on a crate's). A click moves a cubic metre (a piece, for a counted item), and Shift-click moves
     all of it, through decision 4's moves.
   - **Water** is a row with no verbs: the siphon is its door.
   - **His warranty line** goes at the foot of a vault's panel, verbatim. The crate has none.
   - It opens within the computer's reach as today (fact 12).
   - A move isn't an undo step. A later Ctrl+Z short of stock is refused, as it is today.
10. **◆ The lock on the channel box is the panel's, for one opening.** A vault's channel box opens locked each time its
    panel opens, and a click on the padlock unlocks it until the panel closes. Nothing is saved and nothing is in JSON.
    Moving a vault that holds anything is logged.
    *The other arms:*
    - a saved `locked` port, which every mod's vault would carry;
    - a confirmation only when the new channel can't pay the bill.

    *Revisit:* he moves one by accident anyway.
11. **The siphon on T** (decisions 24–26, IM-4's 5, fact 13).
    - **Three files** in the quantum tools' bites, `quantum_siphon_cube`, `_tunnel` and `_chunk`: 1,000 L, 8,000 L and
      512,000 L, at `joulesPerLitre` 1,000, with the rock tools' cooldowns.
    - T has seven stops.
    - **On a water tier,** Q and the left click enqueue a take, and the right click a pour, each before the
      controllers are called. A refusal is told on the message line: no quantum backpack, or busy.
    - The HUD's status line names the siphon and the store's water.
    - The two tool-cycle tests change (fact 3).

    *Cost:* the chunk size moves 512 MJ a click, half of creative's 1 GJ.
12. **◆ Water shows beside the row, not on it** (amends decision 23; fact 14).
    - While the backpack is worn, a tenth cell right of the nine shows the store's water through
      `VolumeText.OfMillilitres`, lit while a siphon is on T.
    - It has no number, is never selected, takes none of his nine slots, and saves nothing.

    *The other arm:* decision 23 as written, a slot named water, which alpha C's format allows. It would read the
    store's amount, but it needs a pin route the hand can't take, and selecting it gives the hand an item that does
    nothing. *Revisit:* he wants it on the row.
13. **The fuse shows** (decision 28).
    - **The body:** a fusing vault's body glitches cyan, one node per fusing vault with the collapse's shader, windowed
      only. A paused fuse holds steady.
    - **The HUD's top line:** `VAULT LOSING CONTAINMENT — 14 s` for the soonest fuse among the vaults in the loaded box
      and on the backpack's channel, with `(paused)` when it is paused.

    *Cost:* a fuse on another channel and out of the box shows only on its panel, until IM-9's pins.
14. **The pile's cyan glitch** (IM-5's decision 16; fact 16).
    - The pile's positions (decision 5) are drawn as one MultiMesh with the glitch shader.
    - Each instance is seeded from its position through the instance's custom data, fading over about a second,
      windowed only.
    - *Not checked in the repo:* that Godot's MultiMesh shares one instance uniform, hence the custom data. The build
      checks it.

    *The other arm:* one node per voxel, the collapse's way, which IM-5's fact 14 priced at 11.7–21 ms a frame at
    scale. *Revisit:* the frame line in decision 17's read.
15. **The lines** (fact 17).
    - The periodic `M10:` line gains the store: both lines, each term, `exact`.
    - One-off `M10: vault —` lines print when a fuse lights, goes out and burns down. These are the oracle's
      instrument, since the panel never logs a vault.
    - The profile's voxel and power lines print the formulas they check.
    - `VaultState.cs:11`'s comment is corrected, and the two stray summaries are mended.
16. **`--placeat=` lands here**, to collapse decision 13's grammar, so f-2 reuses it (fact 24). **And `--deposit=`**
    moves a material from the pocket into a placed vault through the store's own door, after the restore and
    `--stock=`.
    *Cost:* f-2 is lighter by one flag.
17. **The reads owed since IM-0, at 6b's close, windowed and on his yes** (a windowed run takes his focus):
    - **The real dump:** a high vault on a channel with no cells, in the workings of a copy of his world, filled by the
      panel's verbs and the siphon. Read the `dump` term, the frame line and the remeshes against the `--pile` bench.
    - **A fuse burning out underground:** the same run, read from the fuse lighting to the dump's end.
    - **The install:** the `M8b:` line's surface install against about 50 ms mean or 250 ms worst. If it is over,
      the lever is the render, not the ceiling (IM-0's answer).
18. **f-1's finding: no rule change** (narrowed above). The panel's `(paused)` and the HUD's say it, and a paused fuse
    whose bills are paid by held cells adds *"paid by cells held off by a signal"*. The vault never dumps because of
    the signal, which is all decision 10 asked.
    *The other arm:* held cells refill the minutes too, which decision 10 never asked for, and which makes a held cell
    feed vaults while its consumers are held off. *Revisit:* IM-3b's decision 5 trigger, a paused fuse that reads as
    a bug in a play with f-2's sensor.

    **IM-6c (Core and wire)**
19. **The v8 capture comes first, and this time its program is kept.**
    - Before the writer is touched, a one-file app on the unmodified Core writes a v8 header: a row that differs from
      ID order, a slot at zero, a pack worn and one carried. It refuses to run unless the version is still 8.
    - The header is committed as a fixture, and its program as `tools/make-v8-header.cs`, outside every project, so
      the next bump can re-run it (fact 19).
    - The real-world arm opens a copy of his world on the new build, and checks that 0.1.3 then refuses the copy.
20. **Header v9 appends two bytes** (decision 51).
    - **A mode byte:** sandbox 0, creative 1. 2 is reserved for survival and refused at read until it exists.
    - **A flags byte:** bit 0, the kit was granted; bit 1, the first cell's charge was granted. Any other bit is
      refused.
    - One validator serves the writer and the reader. A v8 world reads as sandbox, with no flags set.
    - The mode and the flags are the world's, as the inventory is. Nothing in IM-6 is keyed by player (TODO's
      collision 8).

    *Cost:* two bytes, and the tail constants in the header tests grow by them. **His first start on a v9 build moves
    each world it opens to v9, and 0.1.3 refuses it after.** The next zip carries v9.
21. **`--mode=creative` applies to any run that creates a world, profile or not** (amends decision 51's "a profile has
    no world"; fact 20).
    - It is warned and ignored on an existing world, as `--seed` is.
    - `--nosave`, and a profile without `--world=`, have no world, so they are sandbox.
    - A world's mode never changes after it is made. The open line and the seed banner name it.
    - Core never reads it. No default reaches r6 (must-not-ship 12).
22. **Kits** (decision 53, fact 23).
    - **The section:** `kits/*.json`, read after the materials and strict. A kit names materials and counts in the
      hotbar's units (a body, a cubic metre, a piece), and may set the row (nine IDs) and the worn pack with its
      channel.
    - **The union:** a creative world gets every kit's materials, summed. Two kits that both set the row or the pack
      are refused, naming both files. The same kit ID twice is refused, as a material is.
    - **The grant:** once, at creation, into the pocket, printed `granted` on that session's `M6c:` line, with the
      header's bit 0 set in the same write.
    - **The base game's kit, as IM-6 can fill it:**
      - two cells, eight panels, four pumps and four outlets;
      - one vault of each grade and a crate;
      - the computer, and the quantum backpack worn on channel 0;
      - 4,096 m³ each of stone, clay and glass.
    - **The kit's row:** stone, clay, glass, the cell, the panel, the pump, the outlet, the high vault, the crate.
    - Sensors and quarries join the kit with their slices (f-2, IM-8, IM-9).

    *Cost:* a new content section, and a door into the pocket printed as decision 53 says.
23. **◆ The first cell's 1 GJ arrives through a door of its own.**
    - When the first storage row is placed in a creative world whose bit 1 is clear, a Core `Grant` writes 1 GJ into
      it. The grant is counted `granted`, a new `PowerExact` term, and bit 1 is set in the same header write.
    - **Priced:**
      - a grant happens once;
      - picking that cell takes its charge out through `leaving`, as picking any cell does, and Ctrl+Z of the pick
        brings it back through `entering`;
      - Ctrl+Z of the placement removes the row and its 1 GJ, and the bit stays set, so the gift is gone.

    *Cost:* moving the first cell by picking it up loses the gift, as it would lose any cell's charge. *The other arm:*
    the bit clears when the granted row leaves with its charge whole. That is a door the undo can see twice, which
    IM-0b's lesson says not to open. *Revisit:* his first creative play.
24. **◆ The keys: off in creative, whatever `--dev` says** (amends decision 52; fact 22).
    - Off: P, Alt+P, **Ctrl+P** (a door into the pocket, built after the plan), R, Ctrl+R, Alt+R, Alt+G, H, **Alt+H**,
      and L.
    - Each says so once a session on the message line.
    - The gate is a Core table (key, mode, `--dev` → allowed), so "every creative-off key does nothing" is a test.
      Main asks it at each handler.
    - `--stock=`, `--nogate` and `--pack=` stay honoured (decision 51). Fly stays.

    *The other arm:* `--dev` re-opens them in creative. Since `start.cmd` passes `--dev`, every creative world he
    opened would then keep H, which can freeze a burning fuse.
25. **The card.** At a creative world's first open, the controls card opens in any binary, not only the export, with
    decision 55's line at its head. In creative it lists none of the sandbox keys.
    *The other arm:* the line in the seed banner, smaller and gone in ten seconds.
26. **The storm oracle** (IM-6's row).
    - A new creative world headless, on a fresh name deleted after. `--placeat=` a high vault and panels with no cell
      on channel 0, `--deposit=` stone into the vault, a pump's ring for its water, and `--clock=` or `--weather=1` for
      the storm.
    - The log reads, in order: the own charge paying (`held … of which own` rising), `M10: vault — fuse lit`, `fuse
      burnt`, then `M10: dump —` starting, with every identity exact.
    - A reopen grants no second kit.
    - r6 to the digit, and the v8 capture opening as sandbox.

**Every door, priced** (IM-4c's rule: run each round trip before recommending):

| Door | What moves | Mints or loses |
|---|---|---|
| A move to the store, then back to the pocket | voxels, pocket ↔ store | neither; both counted (`deposited`, `withdrawn`) |
| A move to the pocket of a material it refuses | nothing (decision 4's guard) | neither; without the guard, it loses |
| A siphon take, then a pour | litres and joules (IM-4a) | neither; a take costs 1,000 J a litre |
| A vault moved to another channel | the row with its contents (IM-3) | neither |
| The kit at creation | voxels into the pocket | mints, by design, printed `granted`, once (bit 0) |
| The first cell's 1 GJ | joules into a row | mints, by design, printed `granted`, once (bit 1) |
| The first cell picked, then Ctrl+Z | its charge out through `leaving`, back through `entering` | neither (today's rule for any cell) |
| The first cell's placement undone | 1 GJ out through `leaving`; bit 1 stays | loses the gift, by design (23) |
| `--mode=creative` on an existing world | nothing | neither |
| P, Alt+P or Ctrl+P in creative | nothing | neither |

**Tests and batteries, planned before building** (the memory's rule: write the recipes first).
- **6a:**
  - the per-channel terms sum to the global ones over random boundaries;
  - a picked cell, Alt+P, a deposit's recharge and a return leave the runway alone;
  - the window's and the siphon's draws count on their own channel;
  - the 10 s window and the two thresholds;
  - power and energy text against a table;
  - each move in its order, a closed box refused with its reason, a refused material never moved, and `StoreExact`
    exact over random moves;
  - the fill;
  - the pile's positions match its writes and its budget.

  Mutations: a draw left off the ledger, given counted as outflow, `entering` counted, the combined order reversed,
  the guard removed, the positions never cleared.
- **6b:**
  - every shipped file loads, and fact 3's counts move as predicted;
  - P's row keeps slot 9 for stone with the containers pinned only;
  - the seven-stop cycle, with `Default` still the hand pick;
  - the r6 control;
  - decision 17's reads;
  - **his play**.

  Most of 6b is Game code, which the suite doesn't reach, so the play is its test.
- **6c:**
  - the v9 round trip, and the v8 capture reading as sandbox with no flags;
  - each refusal of the mode and the flags;
  - the kit section: strict keys, the union, and the conflicts;
  - the grant: exact in `PowerExact`, once, and every round trip in the table;
  - the keys' table;
  - the oracle.

  Mutations: a grant with no term, the bit never set, a second kit at reopen, a v8 world read as creative, the mode
  honoured on an existing world, a creative key allowed, `--dev` overriding the gate.

*Estimates:* 6a is half a session and about thirty tests. 6b is a long session, mostly wire, closing on the windowed
reads. 6c is a session and about thirty-five tests. **The riskiest part is 6c's header**, the save format every world
crosses. In 6b it is the clicks' water branch, the one place a free tool could slip onto T (fact 13).

**Taken as recommended, all twenty-six, 2026-11-24** (*"As recommended"*), the same evening. The three amendments
are carried into decisions 23, 51 and 52 and the fork list above. Decision 16 moves `--placeat=` into IM-6b, and
COLLAPSE-PLAN's decision 13 and its f-2 row say so. **IM-6a is built (2026-11-24, below), and IM-6b (2026-11-25,
below); IM-6c is next.**

## IM-6a — built (2026-11-24)

Decisions 2–5 of the pass above, in a worktree, Core only. **Nothing changes in play**: no Game code calls any of it
yet, and the one Game line touched (`MachinePanel.Joules`) prints what it printed. Re-read first: nothing under
`Overburden.Core/Machines`, `Overburden.Core/Inventory` or `Main.cs` had moved since `4a2895c`, so facts 6, 7, 8, 10,
11 and 16 stood as written.

**What it is:**
- **Decision 2, the runway's ledger.** The tick keeps each boundary's flows for each channel, counted on the line where
  each global term is counted: generation, bills paid from the pool, bills paid from own charges, the pumps' draw, own
  charges given and returned. `IChannelBank.Draw` counts every draw on its channel between boundaries, and the next
  boundary takes them over, so a new drawer (the quarry's `dug`, recall's) is outflow without a line of its own.
  - **Public reads:** `MachineTick.BankOf(channel)` (was internal), `FlowsOn(channel)`, a `ChannelFlows` record, and
    `BankDrawnMillijoules`, the draws' lifetime sum (`windows' taken + siphonPaid` while every pack worn is a window).
    The draw stays internal.
  - **`ChannelFlows.In`** is the generation. **`Out`** is pool bills + pumps + draws. Own charges given, returned and
    spent are left out, and so are `entering`, `leaving` and Alt+P, which are not in the record at all.
  - **`Runway`** (Core) keeps twenty boundaries of in and out for each channel, and the bank at the last. A reading is
    the bank, in and out as milliwatts over the window, and `bank × window ÷ (out − in)` while out > in. Its level is
    red under 60 s, amber under 10 minutes, plain from there, and none while not draining. It averages what it has
    before twenty boundaries. `Record(tick)` refuses a tick whose boundary isn't the runway's.
- **Decision 3.** `PowerText` beside `VolumeText`: `OfMilliwatts`, `OfWatts`, `OfMillijoulesPerBoundary` (500 ms by
  default). `EnergyText.Of` is the panel's `Joules`, moved verbatim with its stray summary.
- **Decision 4, the store's moves.** `StoreMoves` has five moves over the set's own doors, each returning a
  `StoreMove(voxels, refusal)`:
  - `ToStore` and `ToPocket` on the worn backpack's channel;
  - `IntoBox` and `OutOfBox` on one vault or crate;
  - `OutOfCombined` into an aimed box: the pocket first, then the store.

  `MachineSet.ClosedReason(cell)` says why a box is closed, and the set's own `IsOpenBox` now reads it.
  `MachineSet.StoreFillOf(channel)` gives used and inside, blocks and water together, over the vaults `StoreOf` lists.
  **The guard:** `Withdraw` and `WithdrawFrom` refuse a material the pocket refuses and count the voxels asked in
  `WithdrawRefusedVoxels`. Every move refuses it as `PocketRefuses`.
- **Decision 5.** `VaultDump.PileVoxelsLast`: the voxels the piles wrote at the last boundary, in the order written,
  at most 512, cleared with `PileChunksLast`.

**Ten build choices the pass left open**, each under a taken decision, listed for his word:
1. **`MachinePanel.Joules` stays, as a one-line forwarder** to `EnergyText.Of`. Its fifty-odd callers move with 6b's
   panel, so 6a touches one Game line.
2. **Power is floored**, as a volume is, in integer milliwatts: 99.96 kW reads `99.9 kW`, never `100.0`. A non-zero
   power under a tenth of a watt reads `<0.1 W`, as the panel's share reads `<0.1%`, because a god vault holding one
   cubic metre of blocks bills 64 mW. Zero reads `0 W`. One decimal is always shown under 100 (`1.0 kW`), as the energy
   text does.
3. **The store's moves take the worn pack.** No quantum backpack, or a lead-acid or lithium-ion one, refuses
   `NoBackpack`, as the siphon refuses. Without one, `OutOfCombined` is the pocket alone, and refuses nothing for it.
4. **The combined box's store half goes vault to vault** (`MachineSet.MoveFromStoreInto`, internal): last-filled
   first, as a withdrawal is, up to the box's room, never out of the aimed box. It never passes through the pocket,
   which would offer the material a hotbar slot. It is held to held, so it moves no term of `StoreExact`, and it is
   counted in `VoxelsMovedBetweenBoxes`.
5. **A closed box's reasons, first that holds:** not a box, unreadable, wreck, dumping, switched off. A wreck is
   dumping too, and may be switched off; it reads `Wreck`.
6. **The runway reads the bank at each boundary it records**, not live. Decision 8 hands the HUD a record at each
   boundary, so the bank and the runway on the line agree with each other.
7. **The packs' recharge isn't in the runway's out.** A lead-acid or lithium-ion pack draws from its channel's pool,
   but decision 8 shows the runway only while a window is worn, and a window and a recharging pack are never worn
   together.
8. **Eight arrays of sixteen, not six:** the six terms, the draws handed to the boundary, and the draws since it.
9. **An out-of-range channel is clamped** by `FlowsOn` and `Read`, as the bank's read clamps one.
10. **Under `PowerOff` only the draws are flows**, since nothing else is counted then.

**Tests** (new: `RunwayTests`, `PowerTextTests`, `StoreMovesTests`; one each in `MachineTickTests`, `SiphonTests` and
`VaultPileTests`; the per-channel identity added to two random runs in `MachineTickTests`):
- the per-channel terms sum to the report's over 400 random boundaries on four channels, with the window, the
  siphon, deposits, withdrawals and Alt+P between them, and land on their own channel: each channel's generation
  from its panel, its paid bills from its vaults, and its draws from what was drawn on it;
- a draw counts at the next boundary only, on its own channel;
- Alt+P, a picked cell and a cell placed leave the flows and the rates alone;
- a deposit's recharge isn't out, and a withdrawal's return isn't in;
- a bill paid from the vault's own minute isn't out;
- under `PowerOff`, only draws;
- the pumps' term on channel 4, and the real siphon's draw on the backpack's channel 3;
- the twenty-boundary window, the thresholds at both edges, no runway at level or rising flows, the rates as power
  (his line's `3.8 kW` and `104 kW`), and `Record(tick)`;
- power and energy against tables, the culture, the extremes;
- each move in its order, a closed box refused with its reason by all three box moves, the backpack's refusal, the
  combined box pocket first and never the box itself, last-filled first, a refused material by every door, the
  fill, and 2,000 random moves with no voxel made or lost and `StoreExact` exact;
- the pile's positions against its writes, its budget and its chunks, and empty at the boundary after.

Planning the battery found two holes before it ran, and each got its test first: a bill paid from the own minute
counted as out, and the combined box's store half taking the first-filled vault first.

**The battery: 61 recipes, 61 red.** Sixty were written before any ran, and ran in three chunks of twenty. Each
mutated file was restored from its saved bytes and compared byte for byte at each chunk's end.
- **The pass's six**, each red on the tests written for it:
  - a draw left off the ledger (`draw-off-ledger`);
  - own charges given counted as out (`given-as-outflow`);
  - `entering` counted (`entering-counted`);
  - the combined order reversed;
  - the guard removed;
  - the positions never cleared.
- **`entering-counted` was first caught at the first boundary**, where a bank recorded from nothing reads as a rise,
  not at Alt+P. So a sixty-first, written after the run, counts only rises after the first boundary
  (`entering-counted-after-first`). It went red on the Alt+P test alone.
- **`pumps-on-channel-zero` was caught by one test only**, the pumps on channel 4: every sum identity stays exact
  when a term lands on the wrong channel. That is why the random run asserts each channel's own terms.
- **The rest**, by area:
  - the ledger: each term uncounted, left uncleared, or on channel 0; the whole bill counted from the pool; own paid
    into the pool's term; draws read live;
  - the runway: each of out's three terms left out; own paid as out; returned as in; the bank's-change arm; the bank
    not recorded; a window of forty; a ring that never wraps; both thresholds moved; amber inclusive; a runway at
    level flows; the runway and the rates over one boundary;
  - the text: power rounded, kW late, a decimal to 1,000, GW late, a trickle reading zero, the sign dropped, the
    boundary fixed; energy's kJ early, GJ to one place, its sign dropped;
  - the moves: both of `StoreMoves`' guards removed; a wreck reported as dumping; off checked before wreck; off not
    closed; the combined half taking the box itself, ignoring the room, or first-filled first; `ToPocket` on channel
    0; a lead-acid pack reaching the store, or the combined store;
  - the fill without water, or over closed vaults;
  - the positions one voxel up, or not recorded.

**Checked:** suite **2,475** (2,396 + 79). Zero warnings from the Game's build and the solution's, both from scratch.
The r6 control on the worktree's own build reads `2601 / 325 / 3446 at 411`, with no `ERROR` line.

**What 6b reads off it:** `Runway` for the bank line (decision 8), `PowerText` for the bill, a panel's output, a pack's
recharge and a pump's draw (3), `StoreMoves` for the stock section's verbs (9), `StoreFillOf` for `store 68%`, and
`PileVoxelsLast` for the pile's glitch (14).

## IM-6b — built (2026-11-25)

Decisions 6–18 of the pass above, in the worktree `im-6b`, on IM-6a's Core. **The vault, the bank, water and the dump
are playable in a sandbox world through P and Alt+P, with no save change.** Re-read first, as CLAUDE.md asks: since
the pass's `4a2895c`, the Game had moved only by IM-6a's one-line `MachinePanel.Joules` forwarder, and every line the
facts cite in `Main.cs`, `Hud.cs` and `MachinePanel.cs` still read as written. Nothing changed a decision.

**The order the plan asked for.** The clicks' water branch landed first, in its own commit (`c85d76b`), with no water
tool in the content; the siphon files came in the next.

**What it is:**
- **The water branch (decision 11, fact 13).** Core's `SiphonClicks` decides what the buttons do on a water tier: the
  left click's edge or Q held queues a take, the right click's edge a pour, one cooldown (the tool's) for both; a rock
  tool never reaches it. Main asks it before each controller, after the profile's overrides, and forces the carve and
  the fill off. `MiningController` and `PlaceController` each carry a second guard of their own (the miner refuses a
  water tool's swing; the brush's `WaterTier` fills nothing), so a water tool carves and fills nothing whoever calls
  them. A refusal (no quantum backpack, busy, `--nowater`) is told on the message line and printed once a streak.
- **The content (decisions 6, 11).** `quantum_vault/low` to `/god` at the grade table over their volumes (water 25,
  10, 4, 1.6 and 0.64 W/m³, blocks a tenth), `holdsWater`, `ownChargeSeconds` 60, a channel and an enabled port, no
  weight; hardness 3.0 like the relay pair. The crate: 1 m³, no links, hardness 1.0 so the hand pick takes it back.
  Three siphons, `quantum_siphon_cube`, `_tunnel` and `_chunk`, in the quantum tools' bites and cooldowns at 1,000 J a
  litre. Textures at layers 15–20: the five vaults a dark teal up to near-white cyan, the crate a timber brown, all
  unscreenshotted. **The name rule** is Core's `ContentLabel`: a path `a/b` reads "a (b)", so a grade reads
  "quantum vault (god)" with no label key. T has seven stops.
- **P (decision 7)** seeds the quantum backpack, the crate and the five vaults, a hundred each.
  `MaterialTable.OffTheRow` (the items and every vault-kind body) is what Main keeps off the row, so the alpha's path
  still leaves slot 9 for stone.
- **The HUD (decisions 8, 12, 13).** The bank line replaces a window's `pack 0 mJ / 0 mJ` line: `BANK 612.0 MJ ch 0 ·
  +3.8 kW sun · −104 kW · runway 1 h 38 m · hold 2.0 kW · store 68%`. It is a label of its own, amber or red with
  the runway. Core's `BankLine` builds the view and the text, so what shows when is tested; Main records the runway
  after each boundary's tick and hands the HUD the view. The fuse's top line names the soonest lit fuse in the loaded
  box or on the backpack's channel, `(paused)` while it waits. The water gauge is a tenth cell right of the nine, with
  a gap, showing the store's water through `VolumeText`, lit cyan while a siphon is on T. On a water tier the status
  line names the siphon, its size, both verbs and their price, and the store's water.
- **The panel (decisions 9, 10, 3, 18).** A vault's, a crate's and the quantum backpack's panel carry a stock section
  (`MachinePanel.Stock.cs`): a capacity bar in `VolumeText`; the bill in watts with the vault's own minute; the state
  — `HOLDING`, `FUSE 12.0 s`, `FUSE 12.0 s (paused)`, `DUMPING — owes …` or `WRECK — owes …`. One row a material with
  its amounts in the box and in the pocket, `→ pocket` and `→ vault` / `→ crate` / `→ store`, a cubic metre (a piece)
  a click and all of it with Shift, through `StoreMoves`. Two Core reads were added for it: `StockOfStore`,
  `StockOfBox`. Water is a row with no verbs. His warranty line, verbatim, at the foot of a vault's. A vault's channel
  box opens locked each time, a click on the padlock unlocks it until the panel closes (`--panelset=unlock=1`
  headless), and moving a vault that holds anything is logged. Power through `PowerText`: the bill, a panel's
  output, a pack's recharge, and a pump's draw, shown for the first time. A paused fuse whose bill is paid while the
  channel's cells are held off by a signal adds *"paid by cells held off by a signal"*. The panel logs a vault's
  state when its phase word changes (fact 9: it never logged one).
- **The glitches (decisions 13, 14), windowed only.** `VaultGlitch`: one node a fusing vault, a 1 m shell with the
  collapse's shader and a seed from its cell, on a rate-zero copy of the material while paused; the pile's blocks as
  one `MultiMesh` with `PileGlitch.gdshader`, each instance's seed and fade in its custom data, a second each, at most
  2,048 at once.
- **The lines (decision 15).** The periodic `M10:` line carries the store, both lines and every term, each `exact`,
  the fuses the one-offs told, and the HUD's bank and fuse lines while they show (`| hud:`), so headless can read
  them. One-off `M10: vault —` lines: fuse lit, out, burnt, dump ended, read between the tick and the dump so a
  burnt fuse's line says everything it owes. `M10: bank —` at each crossing of zero, both ways. The profile's voxel
  line now prints `− dumped − decohered` (it read `held 0 + owed 0 == … deposited 6,400 − withdrawn 0: exact`), and
  its power line the whole formula `PowerExact` checks. `VaultState`'s comment is IM-3b's rule. Three stacked doc
  summaries in the machine code are mended (two in `Main.cs`, one in `MachineTick.cs`).
- **The doors (decision 16).** `--placeat=<id>@x,y,z[,port=value…]` to collapse decision 13's grammar, a voxel in the
  brush cell the body fills, once a profile warms or three seconds into a plain run, through `PlaceBody` (or a row
  alone where the chunk isn't loaded) and the ports through `SetPort`. `--deposit=<id>:<voxels>[@x,y,z]` through
  `StoreMoves.IntoBox`, the last `--placeat=` when no cell is named. `--tool=<id>` puts any tool on T at open. Both
  parsers are Core's (`PlaceAt`, `DepositAt`), so f-2 reuses them.

**Build choices the pass left open**, each under a taken decision, listed for his word:
1. **The right click keeps the hand's item verbs on a water tier.** With the computer or a pack in hand, the right
   click opens a panel or wears a pack, as on any tier; with a material in hand (or nothing) it pours. The pour is the
   fill's twin, and the fill answers to the hand's material.
2. **Two guards behind the branch.** Main's branch, and a guard inside each controller. A recipe that removes one
   guard leaves the other holding, by design; the battery shows each alone, and both together, below.
3. **The highlight still follows the rock cast on a water tier.** The miner only casts, so the outline sits on the
   solid the crosshair meets; the siphon finds its own water along the same ray at the boundary.
4. **The stock rows are what the box holds plus the pocket's blocks and machines.** The pocket's items (the computer,
   the packs) are not offered, since a vault holds blocks and water (decision 15). Core's `StoreMoves` would move
   them.
5. **The hold on the bank line is every billing vault on the channel**, switched off or not (a vault switched off
   still pays), and none that is dumping or a wreck. The store shows only while a vault is open to it.
6. **A runway time reads in its two largest units, floored**: `45 s`, `12 m 05 s`, `1 h 38 m`, `3 d 04 h`. The store's
   share is a whole percent, floored, `<1%` for a trickle.
7. **The fuse line rounds its seconds up**, so it never reads `0 s` while the fuse still burns.
8. **The gauge is beside the row with a 10 px gap**, labelled "store / water", dimmed when no siphon is on T.
9. **`--placeat=` takes voxel coordinates**, collapse decision 13's "at a voxel", and fills the brush cell holding
   it, carving it first, as `--pump` places its bodies.
10. **The vault's window panel says what it is**: *"a window onto its channel's bank and store: it holds nothing itself
    and is never recharged"*, and its status names the bank there.

**Tests** (new: `SiphonClicksTests`, `StoreContentTests`, `BankLineTests`, `HeadlessDoorsTests`; added to
`DebugSeedTests` and `StoreMovesTests`; fact 3's counts moved in `ShippedContentTests` and `CollapseTests`):
- the branch: only a water tool is routed, a rock tool never queues an op whatever is pressed, the left click takes
  and the right pours, Q held takes once a cooldown, one cooldown for both verbs, an item in hand keeps the right
  click, and every shipped tool is routed by what it takes;
- the content: each grade at the table (inside, both rates, blocks a tenth, the minute, the ports, no weight), ten
  times more for four times the bill, the crate, the six layers each their own, each siphon at 1,000 J a litre in
  its rock tool's bite and cooldown, the seven-stop cycle starting on the hand pick, and the label rule;
- P: a hundred of the backpack, the crate and each grade, the containers kept off the row, a pin putting one on,
  and the off-row set exactly the items and the containers;
- the bank line: his line verbatim in the HUD's units, the runway only while draining, the backpack's channel and
  no other, the hold (each grade's rates; switched off still bills; dumping, wrecks, crates and other channels not),
  the hold matching the tick's own bill, the store and its water, the durations, the percentages, and the fuse line
  (the box and the backpack's channel, the soonest, no wreck, seconds up);
- the stock reads, and both headless parsers with their refusals.

Fact 3's six counts moved as predicted but for one: the five materials that can't be placed stay five, since vaults
and crates are placeable.

**The battery: 57 recipes, 57 red**, all written before any ran, run in three chunks (20, 20, 17), each mutated file
restored from its saved bytes and compared byte for byte.
- **One survived the first run, as its recipe said it might**: `container-is-any-machine`, because `IsContainer` was
  read only by its own test, and only for placeables. `OffTheRow` is now defined through it. That recipe, and the two
  off-row recipes against the new text, then went red on the alpha's row.
- **The recipes**, by area:
  - the branch: `clicks-every-tool-water`, `clicks-no-tool-is-water`, `clicks-rock-takes`, `clicks-rock-pours`,
    `clicks-q-ignored`, `clicks-take-no-cooldown`, `clicks-pour-ignores-cooldown`, `clicks-item-pours`,
    `clicks-negative-frame`;
  - the name rule: `label-slash-kept`, `label-empty-brackets`, `label-deep-slashes`;
  - P and the row: `offrow-items-only`, `offrow-every-machine`, `container-is-any-machine`, `p-no-store`,
    `p-no-backpack`, `p-no-god`;
  - the files: `vault-high-rate`, `vault-low-blocks`, `vault-no-enabled`, `vault-weight-port`, `vault-own-90`,
    `vault-inside-wrong`, `vault-layer-shared`, `crate-layer-backpack`, `crate-too-hard`, `siphon-free`,
    `siphon-cheap`, `siphon-cooldown`, `siphon-bite`;
  - the bank line: `hold-counts-owing`, `hold-any-channel`, `hold-water-dropped`, `runway-always`, `hold-always`,
    `store-always`, `hasstore-any-vault`, `duration-rounds-up`, `duration-hours-late`, `percent-rounds`,
    `percent-trickle-zero`, `read-no-clamp`;
  - the fuse line: `fuse-intersection`, `fuse-box-only`, `fuse-wreck-shown`, `fuse-latest-wins`, `fuse-text-floor`,
    `fuse-paused-dropped`;
  - the stock reads: `stock-store-channel-zero`, `stock-box-hides-closed`, `click-always-metre`;
  - the doors: `placeat-ports-skipped`, `placeat-two-coords`, `placeat-bad-port-ok`, `deposit-first-colon`,
    `deposit-zero-ok`.

**And eight Game-side recipes, which only a headless run can catch**, each built and run on the worktree's binary:
- **the control**: the cube siphon on T under `--place --stock=64000 --pitch=-60` fills nothing (no `building` line);
- **`both-fill-guards`** (Main's and the brush's `WaterTier`): the same run fills, free — **caught**;
- **`both-take-guards`** (Main's and the miner's): the siphon oracle swings the miner (a `mining` block) — **caught**;
- **`outer-fill-guard-only`**, **`outer-take-guard-only`** and **`inner-take-guard-only` survive, by design**: each
  guard alone holds (build choice 2). That is the kind of fallback route that can hide a survivor, written down here
  so it reads as chosen;
- **`take-never-enqueued`** (the branch swallows the click): no `M10: siphon —` op — **caught**;
- **`vault-scan-after-dump`** (the scan back after the dump): the burnt line no longer owes 6,400 — **caught**.

**The headless oracles**, all on the worktree's own build, no `ERROR` line in any:
- **The vault** (the pass's storm oracle without creative): `--profile --speed=0 --stock=64000
  --placeat=quantum_vault/high@68,4,60 --deposit=stone:6400 --measure=40` reads the deposit `6,400 moved … exact`,
  `M10: vault — fuse lit … FUSE 13.9 s; its bill of 40.0 W went unpaid with its minute spent (own 0 mJ of 2,400 J)`,
  `fuse burnt … owes 6,400 voxel(s)`, the pile at 512 a boundary, `dump ended … a store again`, and the store line
  `deposited 6,400 − withdrawn 0 − dumped 6,400 − decohered 0: exact`.
- **The HUD**, the same vault in a plain headless run with the backpack worn (`--nosave --seed=84977241117251
  --pack=overburden:quantum_backpack`, the profile's terrain): `| hud: BANK 0 mJ ch 0 · +0 W sun · −0 W · hold
  40.0 W · store 1% · VAULT LOSING CONTAINMENT — 13 s`, the hold and the store gone while it dumps (a dumping vault
  bills nothing and is out of the store), then `store 0%`.
- **The siphon through the branch**: `--profile --speed=0 --pitch=-89 --injectlitres=200000 --fillbasin=2000000
  --breach=3 --pump=100 --pack=overburden:quantum_backpack --tool=quantum_siphon_cube --mine
  --placeat=quantum_vault/high@68,4,60 --measure=15`. The ball of Tier B water under the camera is taken into the
  vault at 1,000 J a litre (`took 250,000 mL … paid 250,000,000 mJ`), the miner never swings (no mining block), and
  every line is exact: Tier B's with `siphoned 8,606 L`, the store's water with `siphoned 8,605,642` mL, the pool's
  with `siphon paid 8,605,642,000` mJ.

**Checked:** suite **2,558**; zero warnings from both builds from scratch; r6 `2601 / 325 / 3446 at 411`.

**Only his play can check** (headless draws no HUD, panel or glitch): the bank line's look and colour, the gauge, the
fuse line, the stock section's layout and verbs, the padlock, the two glitches (and that `INSTANCE_CUSTOM` reaches the
pile's shader, decision 14's unchecked point), the vaults' and crate's swatches, and the pour, which no headless flag
presses. **Decision 17's windowed reads ran the same day**: below.

### Decision 17's windowed reads — run 2026-11-25

Run on his yes the same day (*"Run them now"*). **The install stands; the heap does not come back in a wet working.**

**Where, and the recipe** (verbatim in COMMANDS.md):
- **The world** was a copy of `LakeTest3`, his windowed play of 2026-11-09, which his `start - Copy.cmd` opens. Its
  workings are a pit about 72 × 88 m, dug to −24 m by the excavator, at about (292, −444) m. Every saved chunk is air,
  and the floor already held about 213,000 L of his water.
- **The setup:** the profile stands 4 m over the floor at (268, −20, −436) m, looking down. A vacuum cell and a high
  vault go on a dry chunk of the floor 46 m east (`--placeat=`). The cell is charged to 500 MJ by `--fillcells=`,
  new: Alt+P's door, which a scripted run lacked. 32,000 voxels of stone go in through the panel's door
  (`--deposit=`). Then 2 ML of Tier B water is poured under the camera, with the chunk siphon held on it (`--mine`).
- **The flags:** `--fluidnear=2 --fluidjob=1`, as IM-0's read.
- **Three headless rehearsals came first**, so his screen was taken once:
  - the first refused both bodies as wet (the pour lands before the doors run);
  - the second's 1 GJ bank outlasted its 180 s window;
  - the third ran the whole chain.

**What happened**, windowed, at two water ticks a second (the third rehearsal agreed at every step):
- **The siphon** took 499.84 m³ for 499.84 MJ, and the bank ran dry at tick 89. In the second rehearsal, takes ran
  550 m³ in their first 30 s, then slowed to about 3 m³ a second as the ball spread thin.
- **The fuse** lit at tick 200, with the vault's own minute spent: `its bill of 2.1 kW went unpaid … FUSE 14.6 s` (10.5
  s headless). It burnt out at tick 230, owing 32,000 voxels and 499.83 m³.
- **The water** went into cells at 2.3 m³ a second: 427.6 m³ in the 185 s left, and 72.2 m³ still owed when the window
  closed. Every line was exact: the `dump` line, the store's water line and the pool's.
- **The blocks never came back.** Every one of the 38 dump lines reads `0 voxel(s) into 0 chunk(s), 36 column(s)
  waiting`. A new line names why: `pile waited 0 not loaded to the water, 13,248 wet, 0 in a lake, 0 the player, 0
  reserved (column-boundaries)`. Headless, the first boundary laid 156 voxels before the water reached the heap, then
  the same.
  - The heap refuses a wet slot (`VaultDump.Pile.cs:586`, IM-5b, built as its plan said), and the dump's own water,
    poured on the same floor, wets every slot in reach.
  - In a working with no drain the water never leaves, so the 32,000 voxels stay owed and the vault stays dumping.
  - A pump is no cure: one millilitre in a slot refuses it, and rain re-wets a floor.
- **The install: 28.62 ms mean / 221.01 ms max**, on the main thread, over 605 rebuilds of 151.7 panels (9.5 MiB a
  rebuild).
  - The surface at the end: 34,310 quads over 126 panels (9,564 tops, 24,659 sides, 87 bottoms), with 2.45 ML moving
    in 190,000 cells. That is the poured 2 ML and his own water as well as the dump's.
  - **It is under both of the bars** (about 50 ms mean, 250 ms worst), the worst within 12% of its bar.
  - The tick held 2 Hz, 600 ticks in 300 s, where IM-0's 10 ML read fell to 0.97 Hz.
- **The frame line**: mean 7.14 ms, p50 6.94, p95 7.14, p99 8.33, max 148.6. The update's max, 221 ms, is the install's.
  He may have moved the mouse during the run. The camera never changed chunk (`the viewer never changed chunk`) and
  the player saved back unmoved, so at most the aim turned. That can touch the frame line, not the install, which
  builds over all the loaded water wherever the camera faces.
- **The remeshes against the `--pile` bench were not read**: nothing was piled, so the pile made none.
- **Checked:** zero `ERROR` and zero `*** NO ***` in every run. Zero warnings from the Game's build from scratch with
  both additions. r6 `2601 / 325 / 3446 at 411`.

**What it decides:**
- **Decision 32's 10 ML ceiling and IM-0's answer stand.** A real dump underground installs at 28.6 ms mean, under the
  bar, so there is no render lever to pull.
- **New, and his: the heap and a wet slot** (question 1 below).
- **Still unread**, because both want a heap that lands dry: the pile's remeshes in a real world, and that
  `INSTANCE_CUSTOM` reaches the pile's shader (decision 14). His play can show both with a vault of blocks only, or a
  rerun can, once question 1 is answered.
- **A side note, not a question:** the ball's pass starts at its bottom layer, which in a working is rock, so about
  half of each pass's 3,200-cell budget visits cells with no room. The pour still ran at 2.3 m³ a second.

**Question 1, the heap and a wet slot.** Today the blocks of a dump in any wet working wait for every slot to be
bone dry, which a working that has seen water rarely is. Even a dry one is wetted by the dump's own water first.
- **(a) Keep it.** *Cost:* a vault that lets go in a wet working keeps its blocks owed for good, and the body stays
  dumping.
- **(b) The heap first, the water after**, within one dump. *Cost:* the water waits about 30 s longer at 32,000 voxels,
  and water that was already there still blocks. On his pit's floor (213,000 L before the dump) the blocks still never
  land.
- **(c) A wet slot takes its block and lifts its water**, by the door a collapse piece already uses (`ICollapseWater`:
  lift, return up the column, displaced when there is no room). Those terms are already on Tier B's line, and a
  machine placement lifts the same way. *Cost:* the pile's slot check, the dump's `Exact` reading the lifted term, and a
  battery. A lake's band still refuses, since that is Tier A.
- **(d) Blocks that wait wet decohere after a while**, through the dump's existing decohered term. *Cost:* the blocks
  are lost. Cheap, and in keeping with bankruptcy being a feature, but a vault in a flooded mine then loses everything.
- **Recommended: (c).** It returns the blocks, the dump ends, and it reuses a door whose terms are already printed and
  tested. *Revisit:* if (c)'s lifts at 512 a boundary measure over the `--pile` bench's frame budget, fall back to (d)
  for slots still wet after the release clock.

## Must not ship

1. A reserve floor under any name: a percentage kept back, a quarry that stops itself without a sensor he
   placed, a refusal to spend before the bank is empty.
2. A dump, a siphon or a vault inflow through `FluidSimulation.Add`, `WaterSystem.Emit` or the relay's
   `PourPending`, or a pour ball sized to everything owed. Twins only, sized to the budget.
3. Any credit at a fall with the drain's destroy branch journalled, recall crediting 64 whatever a pick
   already took, or the recording-off bracket put around the whole drain (a pick's dismantle must stay
   undoable).
4. A vault's row journalled while it holds anything, or a reconcile branch that removes one.
5. A codec that reads an unknown version as empty, a live vault object encoded at save, or a restore that
   clamps contents.
6. The backpack declared with a capacity, recharged, filled by Alt+P, or its bank saved into the pack slot.
7. A window draw inside the tick's run, or written through `SetCharge`.
8. The vault as a signal reader, its hold paid after signal overrides, or a hold with no `held` term.
9. A quarry or dump write with recording on; a quarry passing no body check over a machine cell; a pile
   written into a machine cell, a wreck's cell, or a wet cell.
10. The undo's vault refusal inside `Apply` rather than as a pre-flight.
11. A CPU rate in mod JSON with no engine maximum.
12. A creative default reaching a profile, a `--nosave` run or the r6 control.
13. `--nogate`, `P` or `--stock=` as creative's source.
14. A new kind with no panel branch.
15. A new key, or a medium name, that loads silently when misspelt.
16. Water in the pocket by any path.

## Figures this plan leans on, and where they come from

- **Measured:** the Release bench's 0.6 ms a hundred wet pumps and 0.02 ms five hundred dry; the 20 ms
  boundary on TajMahal (fixed build); the in-game 1 m cube at 0.42 ms and 2 m cut at 1.3 ms mean and 7.1 ms
  at worst a bite (Debug, before the fix); M9a's 16 m square (a 20-voxel plug dry, 3,028 voxels under 5 m of
  head) and 32 m square (47,600 voxels); one `HeldIn` at 10 ns; the autosave at 17–100 ms; the seep rescan at
  0.56–1 ms (Release).
- **Derived from a rule:** a stone gallery 8 m wide never falls (every roof voxel within 4 m of a wall).
- **Measured by IM-0 (fixed build):** the `machines` bracket at 0.63–0.88 ms mean on 127 and 209 rows; the
  1 m and 2 m bites at 0.16–0.20 and 0.42–0.53 ms mean; Tier B insertion at 0.5–1.1 µs a cell; a 10 ML
  pour carried at 2 Hz. See "IM-0 — measured".
- **Measured by IM-3b (a Core test):** under IM-3's fuse rule a vault held on 5% of its bill for 4,000
  boundaries; under IM-3b's it dumps after 28, 57, 596 and 2,885 boundaries at 5, 50, 95 and 99%.
- **Estimated, owed a slice's bench:** the heap's ceiling and budget (10,000 m³, 512 voxels a boundary), a
  bite under a lake, a dump underground, and every "crosses the 1 ms line at N" figure.
- **Invented, his to tune:** the grade table, the hold rates and the ten-to-one water ratio, the fuse's lean,
  the runway's thresholds, the siphon's 1,000 J/L, free pour and 0.25 s cooldown, the kit and its 1 GJ, the
  power sensor's default.
- **Arithmetic:** the quarry's 51 kW and 26 kW ceilings, the siphon's 4 MW on Q, the 15 L/s seep into a 16 m
  box under a lake, the dump durations, and the demo's beats.

## What would reverse this

- **The pocket (13):** a survival mode that bounds it; the overflow then goes to the store.
- **Grades in the ID (18):** M10g's parts, once the undo refuses a row with installed parts.
- **The hold before signals (10) and no hand valve (27):** his word to reopen collapse decision 10.
- **The weather lets vaults go (12):** he wants a broke vault to go in sunshine; then holds are paid from
  stored charge only.
- **Decoherence (32):** the fixed build measured able to carry a bigger dump into Tier B.
- **The dead state (34):** a play where it reads as too harsh.
- **The vault's minute (IM-3b, 2):** his play of the storm, or he wants a nearly empty vault to be safe;
  then the other arm, a minute of the full bill.
- **The fuse that waits (IM-3b, 5):** a play where a paused fuse reads as a bug.
- **One boolean a channel (38):** a play wanting power and collapse signals apart.
- **The quarry's beat (47):** his eye on the half-second bite; the lever is a second rate beside the
  boundary.
- **Tier A only (59):** a flooded mine that never weakens reads wrong in play.
- **One set of channels a world (60):** the first server.
