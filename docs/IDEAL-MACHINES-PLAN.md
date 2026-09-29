# The ideal machines — plan (2026-09-29)

The top tier of the machine ladder, designed first, for the creative-mode demo. His brief, his data
and his nine taken decisions are in [IDEAL-MACHINES-BRIEF.md](IDEAL-MACHINES-BRIEF.md) and are not
repeated here. **Nothing is built.** This is the planning pass; it ended with the decisions handed back.

**Taken as recommended, all sixty-one and the ten forks (2026-09-29):** *"Yes as recommended please
Claude"*. **IM-0b is built** (2026-09-29, [UNDO-PLAN.md](UNDO-PLAN.md), "The machine drain was
journalled"). It is wider than the split's row: the reconcile's four branches were the same mint, so
the rule is that only a pick's dismantle is journalled. **IM-0 is measured** (the same night, "IM-0 —
measured" below), and he took the 2 m quarry bite. **IM-1 is built** ("IM-1 — the code-level pass" and
"IM-1 — built" below; its eight decisions taken as recommended). IM-2 is next.

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
  flat. Parts you install wait for crafting (M10g). Adding them now would open an install door the undo
  cannot see, and the Core judge found it minting in both designs that tried.
- **◆ 23. Water shows on the hotbar**, as its own ID, while the quantum backpack is worn. It is never
  placed by an ordinary right-click; the siphon is its only door. That is taken 8 read literally. The
  other arm keeps water off the hotbar, in the store's list only.
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
  keeps them all.

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
    the sun below the bills, a vault's fuse starts (28), then the dump (30–37). That is a real property of
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
      A fusing vault moved to a solvent channel is disarmed at its next paid boundary; a dumping vault or a
      wreck keeps dumping.
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
    every path: `--stock=`, `P`, the to-pocket verb. Tier B still holds anonymous
    millilitres, and nothing in the fluid engine reads this file. *Cost:* one permanent ID, the one
    MATERIALS-NOTES option B would need anyway. *Revisit:* he wants water in the pocket.
24. **Four doors, each a twin counter, never through `Add`, `Emit` or the relay's `PourPending`.**
    - **The siphon's left click** takes up to a 1 m cube of water at the crosshair, at range: at most 1,000 L,
      64 cells.
    - **The siphon's right click** pours up to 1,000 L from the store at the crosshair.
    - **A pump into a vault.** A vault joins its channel's outlet ring with a `weight`, and litres a pump
      delivers to it are `vaulted`, not emitted. The pump stalls `full` only when every entry in its ring
      refuses. Priority orders consumers; the ring orders a pump's outlets; the two are unrelated.
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

### E. The fuse and the dump

27. **◆ Only an unpaid bill starts a fuse.** The vault is never a signal reader, and its `enabled` 0 closes
    it to deposits and withdrawals while it keeps paying. Collapse decision 10 stands whole. *The other
    arm:* `enabled` 0 set by hand starts the fuse, a flood valve, amending decision 10 for the hand.
28. **The fuse is 10–20 s, leaning toward 10 the further the channel falls short**, like his collapse fuse
    (*"ideally the time would be from 10-20 seconds but randomly"*). The body glitches cyan, and the HUD reads
    `VAULT LOSING CONTAINMENT — 14 s`. A boundary that pays in full disarms it. It counts boundary
    milliseconds, so a late boundary is a slower fuse, never a wrong one.
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

51. **A mode is a byte in header v8**: sandbox (every existing world, today's game exactly), creative, and
    survival reserved. `--mode=creative` applies only when a world is created, like `--seed`, and is warned
    and ignored on an existing world. A profile and a `--nosave` run have no world, so they are always sandbox
    and r6 holds; a headless run that creates a named world with `--mode=creative` is creative, which is how
    the oracles reach it. **Core never reads the mode**; no rule of water, power or support differs by mode.
    Command-line flags (`--stock=`, `--nogate`) are his developer doors and are honoured in any mode. M10g's
    hotbar assignment moves to v9.
52. **◆ Creative turns the debug keys off; sandbox keeps them all.** P, Alt+P, R, Ctrl+R, Alt+R, Alt+G, H and
    L are off in creative. Fly stays in every mode: it is the only way out of burial until M11. In sandbox, P
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
| 5:00 | **The storm, and the fork.** Greedy: hold the siphon or the excavator, and the bank hits zero in about two minutes. Pumps and the quarry stall, the tool fails, and with the sun at 0.5 kW against a 2 kW bill the vault glitches cyan, `LOSING CONTAINMENT — 12 s`. The lake rises over ten seconds, and the spoil piles up around the vault in colour. Careful: at amber he turns the quarry off from the board, and the vault holds through the storm. | Bankruptcy is a feature. |

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
| **IM-2** Core | The window (7, 8): the backpack's JSON, the validator's exemption, `RechargePack`, `FillPack` and `Give` refusing a window, the direct draw; the priority consumer list; `packInitial` printed. | `PowerExact` exact over a thousand random bites and boundaries; the worn backpack's saved charge is 0 after a hundred boundaries, Alt+P and a save; a draw inside `Run` throws. Mutations: drop the `packTaken` credit, red; write the draw through `SetCharge`, red. Equal priorities reproduce position order. |
| **IM-3** Core | The `vault` kind and the crate; the codec; the store view and fill order; the bill (`held`) and its order before signal overrides; the fuse; every row-removal rule of 17; the store's voxel identity. | Identity exact over a thousand random operations, the undo included; a signalled cell never unpays a hold; two vaults both bill; a save during a fuse resumes at the saved milliseconds; a wreck survives a reopen and is never rebuilt; both reconcile branches keep contents; an unknown codec version refuses the pick and the undo; `overfull` at restore; a refused pick carves nothing; the undo refusal is a pre-flight; `SetPort` moves contents with the row. Mutations: journal a full row, red; bill with no `held` term, red. |
| **IM-4** Core | Water's doors: the ID, the pocket's refusal, the siphon's queue and rules, the vault in the outlet ring, the twins, `pumped == emitted + vaulted`, the store's water line. | M8's oracle unmoved with no vault; a siphon take then pour restores a basin exactly; a click then a quit moves nothing; a take on a Tier A lake draws the basin; the pump stalls `full` only when every ring entry refuses. Mutation: drop `vaulted`, red. |
| **IM-5** Core | The dump: the pile writer, the Tier A release, the Tier B ball with its twin sized to the budget, the ceilings and decoherence, the global budgets, owed while unloaded, the unload batch's session, the dead state; a heap-writer bench arm. | A fixture room receives every voxel once; a pile into a chunk that unloads, then a kill with no session save, then a reopen, counts every voxel once; the unloaded arm releases at arrival; N vaults dumping share the budget exactly; the Tier B ceiling decoheres the excess; power returning after the fuse does nothing; Ctrl+Z cannot touch the pile; Tier B exact, Tier A's counters seam-checked. Mutation: route the dump through `Add`, and the seam goes red. |
| **IM-6** Wire | Header v8, the mode and `--mode=creative`; kits and the first cell's grant; the debug keys off in creative; the card; the HUD bank line, runway and store percentage; the panel's stock section; the siphon on T; the vault and crate JSON and textures; `M10:` store terms. | r6 to the digit; a v7 world opens as sandbox; the kit is granted once and not on reopen; every creative-off key does nothing; headless: a new creative world (`--world=probe-creative --mode=creative`) with `--placeat=` vaults and cells bankrupts channel 0 under a storm clock, and the log reads fuse, then dump, every identity exact. **Playable: the bank, the vault, water and the dump.** |
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

**Not run: the surface install in a windowed run.** Headless builds no surface. A windowed run takes
his focus, so it waits for his say-so; the install was 92 ms mean on the load test's front (pre-fix).

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

**Owed: the r6 control.** He was playing through the build (started 7:17 PM on `TajMahal2`, on a binary with
no IM-1 loader in it: his `Content:` line has no extras term and 0 `ERROR`). A headless run beside him
steals his CPU and rotates the log he is writing, so it waits for the game to close. The Game builds clean.

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
- **One boolean a channel (38):** a play wanting power and collapse signals apart.
- **The quarry's beat (47):** his eye on the half-second bite; the lever is a second rate beside the
  boundary.
- **Tier A only (59):** a flooded mine that never weakens reads wrong in play.
- **One set of channels a world (60):** the first server.
