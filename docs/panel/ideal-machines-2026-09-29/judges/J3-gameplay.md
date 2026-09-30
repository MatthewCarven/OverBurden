# Judge 3: gameplay and Matthew

The lens: which design Matthew would enjoy playing and building, and which gives the person he hands
the demo to a good first five minutes. Every claim below about his words is checked against the repo
(`docs/M10-NOTES.md`, `MATTHEW-TODO.md` "Answered lately", `docs/COLLAPSE-PLAN.md`), and every claim
about the engine against a reader map or the code. "Arithmetic" marks my own sums.

---

## 1. Scores

**Design 2 (gameplay and the demo): 8/10.** The only design whose demo can produce his feature,
bankruptcy, through play rather than by a switch. It is built from his delights. The vault's fuse is
his collapse fuse (10–20 s, leaning with the deficit, `MATTHEW-TODO.md:175-177`), with his cyan
glitch (*"like it"*, :179) and his phonk. Its visual rule, *cyan is entanglement at work, black is
gone, colour is yours*, extends his *"indicitave that you aint getting that back"* (:180) into a
grammar a newcomer can read. Its quarry bites his 2 m cube (:187). It is also the only design that
closes collapse part f's decision 10 on every road (see defect X-1) while still giving him his
2026-09-25 hand valve (*"i am deliberately going to flood that entire valley"*). Its register is
his: site office, operations board, `OFF SITE — waiting for the operator`, *"the last operator
pumped this mine"*. That is *"a mining-operation tone over a physics one"* (`M10-NOTES.md:231`).
It loses points on four things: water is kept off the hotbar, which narrows taken 8 (it flags this
itself); it has no cell inside the vault, against his 09-25 image; its rent is flat, which
contradicts its own "the water is yours to pay rent on"; and its drama is authored in a template he
must maintain.

**Design 4 (physical plausibility): 6/10.** It has the best ideas about the quarry. *"The hole is
the bill"*: seepage makes depth a standing water bill, and the arithmetic checks (stone permeability
0.02 in `stone.json`, `SeepDrain.cs:13`'s `permeability × head × area × seepageScale`, 0.06). It
also has the best line for the card: the quarry *is* an overburden machine. Its starter plant hands
the newcomer a charged 5 GJ cell through the printed `debug` term, so tools work in the first
second. Its grades read his *"battery tech maybe 19th century"* literally (lead-acid, Planté 1859).
But the fiction ("the fold", tolls, throats, Torricelli, Beverloo, the discharge coefficient in
JSON) is the physics register he declined. Its part IDs name eras (`cell/lead_acid`,
`emitter/spark_gap`) where he named grades. Its solids vent as 1 m pieces over up to 2.3 h. Its
demo shows the dump by a valve and only tells you about bankruptcy.

**Design 3 (mod format): 5/10.** It is the most faithful to three of his words: water gets an
inventory ID (taken 8), grades are parts that can mix (*"battery 19th century … emitter a higher
tech variation"*), and the two pairings he named become two carried items. As play, though, it is
thin. Its demo cannot run on its own numbers (defect 3-1). Its dump fires on the first unpaid
boundary with no fuse. Hand mining credits the pocket, so the one store holds nothing of yours that
you dug. Its twenty decisions are mostly format (patches, media files, `$` keys, did-you-mean), which
he will wave through without a play to answer them.

**Design 1 (engine cost): 4/10.** Its window is the purest one pool, and its recall is exact. As a
game it is a set of traps:
- A non-empty vault refuses the pick and the undo. The façade routes every dig into the vault, so
  it is non-empty from the first bite.
- One vault per channel: a second vault is inert.
- There is no upgrade path between grades.
- The god grade is capped at his 10 ML, against his *"or more depending on configuration"*.
- The siphon costs 100 J/L, a tenth of the pump's price.
- The demo fills the bank with Alt+P and stages bankruptcy by switching a cell off.

---

## 2. Defects, per design

### Across designs

**X-1. Collapse part f's decision 10 reopened by the cell road (D1, D3, D4). Must not ship.**
Decision 10 (`COLLAPSE-PLAN.md:1138-1141`): *"Deliberately flooding stays out … The sensor can
switch pumps off, and what follows is left to the physics."* f-1 makes the vacuum cell an `onsignal`
reader (decision 5; Reader A §7). A signalled-off cell leaves the pool, so a vault's hold goes
unpaid and the vault dumps. That is his rejected *"cut power to the water containers via quantum
energy sharing and deliberately flood the place"*, rebuilt from shipped parts.
- **D1** closes only the vault-as-reader road (§3.1, must-not-ship 9) and never mentions cells.
- **D3** says *"Only an unpaid bill causes it; the respond gate never does"* (§5). But the respond
  gate on cells is what causes the unpaid bill.
- **D4** sees it and leaves it "emergent" (§5 "The deliberate flood", decision 22: *"rests on the
  player's restraint"*). That reopens the decision by default.
- **D2** alone closes it structurally: rent is paid from `enabled` cells before signal overrides
  apply (§3.1, decision 7). It still gives him the valve by hand, which is his 09-25 wish and not
  the sensor's.

He accepted decision 10 on the keystone argument. A design that lets it back in unnoticed will
surface in a play as *"why did my valley flood when the roof went?"*

**X-2. Saturation is Tier A only, so the mine fights back only under a lake (all four). Cost.**
`Saturation.cs:15`: the head is *"derived when asked, from Tier A's one number per basin"*. A
flooded pit (Tier B) never weakens its own walls. An open-pit quarry has no roof. M9 measured that
a room *"8 m wide of any length never falls in stone"* (`M9-PLAN.md:97`). So in dry uplands, none
of the four quarries brings anything down. D2 admits this (weakest point 3, decision 23) and builds
its demo around the one place the keystone works. D3's beat 5 and D4's beat 4:30 assume collapses
their own geometry cannot produce (3-2, 4-5).

### Design 2

**2-1. Water is kept off the hotbar, which narrows taken 8. Cost, and a question to ask him.**
Decision 12: *"Water is a store quantity, not a material or a hotbar slot. Cost: reads 'inventory
item' narrowly."* His words are *"happy to have water as an inventory item"*. D2 does put water in
the bound store, where the store tab lists it, so this is a narrowing and not an omission. D3's
display-only water ID, which is `placeable: false`, keyed off the computer's verb, and moved only
by the siphon, gives him the literal reading with the same doors (see the grafts).

**2-2. Flat rent contradicts the design's own creditor idea. Cost.** §3.7 says banking a litre
means *"the water is now yours to pay rent on"*, and §1.3 calls the vault *"a creditor"*. §5.1 then
makes rent *"flat by grade"*, so a banked litre adds no rent at all. It costs only the 1,000 J/L
door and a larger dump, and "bank everything" becomes the dominant move. Either the rent follows
the fill, or the creditor sentence goes.

**2-3. There is no cell inside the vault, against his 2026-09-25 image. Cost, likely
re-litigated.** His words (`M10-NOTES.md:341-344`): *"requires a quantum vacuum storage 'pack' …
this is a component that goes into the quantum storage container, and should you let it go flat"*.
D2's vault has rent and no part at all, and its grades are an ID axis only (§6). He described the
pack inside the vault and the mix of grades as the fun. D3 and D4 each honour it, D4 most directly
(its cell part adds joules to the pool).

**2-4. Binding is a one-way door on his played worlds. Cost.** §4.2: *"Binding moves the pockets
in … Unbinding moves nothing."* On TajMahal (113 M voxels of stone, D1 §4.2), his first bind
silently moves up to a vault's capacity of his stock into something that can dump. D1's rule, that
what the pocket held before stays in the pocket, is the fix.

**2-5. The demo store starts 80% full, so a newcomer's excavator stops. Cost.** The template vault
is high grade, 10 ML, and holds 8.0 ML at open (§9.4), leaving 2,000 m³. An excavator bite is
512 m³ (Reader D §2). So about four bites fill the store, and then every bite is refused with
`STORE FULL` (§4.2: *"a hand bite takes what fits"*) (arithmetic). The quarry at rate 1 takes
8 m³ a boundary and fills the remaining room in about 250 boundaries, roughly two minutes. A
newcomer whose pick stops working because of the last operator's water will not read that as the
mine fighting back. Start the vault at about 5 ML, or give the demo a second empty vault.

**2-6. A mis-aimed excavator bite dumps the vault. Cost.** The excavator's 8 m bite at 512 m range
dismantles any machine it covers (`Lost == Carved`, Reader A §6). In D2, a vault that ceases to
exist hands its contents to sister vaults, then dumps (§5.2). The demo has one vault, on the shore,
holding 8 ML. A newcomer can flood the lake with one stray bite in the first minute. D1's `Refuses`
for at-range tools fixes this (see the grafts); a deliberate removal stays the hand pick's job.

**2-7. The pour "skill save" is about 6% of the dump. Cost.** §3.7 makes pouring free so that *"a
quick player can pour banked water somewhere harmless and shrink the dump"*, quoting his collapse
line (*"you might be able to like fix it if you were quick"*, `COLLAPSE-NOTES.md:26`). The siphon
pours 8 m³ a click at four clicks a second, 32 m³/s. Over a 10–20 s fuse that is 320–640 m³ of an
8.5 ML vault, about 4–8% (arithmetic). The real save is the board: switching the quarry off at
amber. And that save fails in the demo's own storm: sun is 0.5 kW against 4 kW rent, so the fuse
can never disarm once the bank reaches zero. The fuse is spectacle there, which is fine, but the
text should say so.

**2-8. Debug keys are off in creative. Cost, likely queried.** P, Alt+P, R, Ctrl+R, Alt+R, Alt+G,
H and L are removed in creative (§9.2). This is right for a newcomer, since Alt+P would make
bankruptcy a keypress. He uses these keys every session, though. Sandbox keeps them, and that should
be said to him in one line.

**2-9. The template has no way to be authored. Cost.** The mode is set only at creation, and
`--template` copies a save (§9.1, §9.3). To fill 8 ML into the vault he needs Alt+P or R, which are
off in creative. So he must build in sandbox, and nothing flips the template's mode byte. It needs
a `--template-seal` step, or the mode taken from the template at copy time.

**2-10. A collapse eats the `gold` lure. Cost.** Gold is the one thing the palette cannot replace,
which gives *"mine to get it back"* a stake in creative. But a collapse turns ore to rubble (Reader E
trap 6, `CollapseDirector.cs:939-943`), and the lure's first beat is a roof coming down on it. That
is on theme (black is gone), but D2 never says it.

### Design 1

**1-1. Water never enters any inventory the player holds, which narrows taken 8. Must not ship as
worded.** §1.5: *"litres live only in vault rows, never in the header"*. §4.2: *"Water is not a
material, so it never enters the pocket."* The façade's merged view is materials only, and water
shows only on the HUD's vault line. This is exactly the reading "water lives only in the vault". D2
at least puts water in the bound store's list and asks him about it.

**1-2. A vault becomes immovable from the first dig. Cost, severe.**
- §4.2: while linked, the façade routes every block to the vault.
- §5.3: *"A pick is refused … a non-empty vault reads as too hard to every tool"*, and *"The undo
  refuses to remove a non-empty vault."*
- So you place a vault, dig once, and can neither move it nor Ctrl+Z it.

Link changes also clear the undo stack (§4.2), and a link changes every time a pack is equipped or
a vault is placed. He said *"Ctrl z feels good"* (`MATTHEW-TODO.md:196`).

**1-3. There is no upgrade path between grades. Cost, severe.** The design allows one vault per
channel, and a second is inert with the stall `Duplicate` (§5.2). A non-empty vault refuses the
pick. So a god vault cannot replace a medium one on the same channel, the medium one cannot be
removed, and its contents can leave only by placement, by the siphon's pour (8 m³ a click), or by a
dump. Grades are the ladder a creative player climbs, and D1's only way up it is to bankrupt the old
vault. The creative kit grants *"2 of each vault grade"* that the rules make useless.

**1-4. The god grade is capped at 10 ML, and "god means a long fuse, not a bigger heap". Cost,
likely rejected.** His words are *"10,000,000 litres of water to dump or more depending on
configuration"*. Decision 14 turns five grade names into one capacity ceiling. The CPU reason is real
(a dump into Tier B), but D2 and D4 answer it by where the vault sits (Tier A on the analytic
ground) and by throttled release, not by capping the grade.

**1-5. The siphon costs 100 J/L, a tenth of the pump. Cost.** The siphon JSON (§2.3, line 176)
sets `joulesPerLitre: 100`, with no reasoning anywhere in the design. The relay pump is 1,000 J/L.
Fighting a flood by hand becomes ten times cheaper than building pumps, and banking by siphon ten
times cheaper than banking by pump (§5.4). D4 must-not-ship 3 names this class: *"the siphon is a
free pump and the antagonist dies."*

**1-6. The demo teaches a debug key and stages bankruptcy with a switch. Cost, severe for taken 3.**
- Step 1: `BANK ch 0 — 0 J`. Step 2: *"Alt+P: BANK 5 GJ"*.
- Step 6: set the cell to `enabled = 0` and *"the bank is empty"*.
- A newcomer does not know Alt+P, so every quantum tool refuses from the first click. Eight panels
  give at most 4 kW against the miner's 32 kW (Reader D §2).
- Alt+P stays live in creative (§9), a free refill that turns bankruptcy into a keypress.
- The climax may not fire in daylight: with the cell off, solar still feeds `_available`, and the
  vault is paid first at priority 12 (arithmetic, Reader A §4.2).

**1-7. The quarry stands on a pillar in its own pit. Cost.** Its box hangs under the body with
*"the body's own column excluded"* (§7). A player cannot reach or see the machine they are
watching, and a pit dug under the operator's feet is the reverse of every design that faces the
box.

**1-8. The heap is a box filled from the floor, one whole 1 m cell at a time. Cost, likely
rejected.** §5.3. He chose voxel collapse pieces over a 1 m cube (memory: *keep his choice*). The
dump is his drama moment, and D2 and D3 give it the director's pieces.

**1-9. A global quarry budget of 4 bites a boundary, set by a flag. Cost.** A second quarry adds
nothing once the budget binds, and nothing on the HUD says why. On a server one player's quarries
starve another's, which is not taken 9's *"bounded by the loaded boxes of the players present"*.

### Design 3

**3-1. The demo cannot run on its own numbers. Cost, severe for taken 3.**
- *"Creative gives you the machines, not the joules."* Palette draws are counts, and a placed cell
  starts at zero (Reader A §6).
- Eight panels give at most 4 kW. By 1:30 the bank holds at most 4 kW × 90 s ≈ 360 kJ
  (arithmetic).
- Beat 3's quarry draws 6.4 kW, more than the panels make.
- Beat 4's siphon costs 1 MJ a swing (1,000 L at 1,000 J/L), and the bank cannot afford one swing.
- Beat 6, *"the bank line falls"*, falls from roughly zero.

It needs D4's charged starter cell or D2's template.

**3-2. Beat 5's collapse cannot happen. Cost.** An 8³ box *"undercuts a slope"* and *"the roof
comes down in pieces"*. But *"8 m wide of any length never falls in stone"* (`M9-PLAN.md:97`), and a
dry box has no head.

**3-3. The dump fires on the first unpaid boundary, with no warning in the world. Cost, likely
re-litigated.** §5: *"A vault dumps when a boundary cannot pay its hold."* One excavator bite that
empties the bank for half a second dumps 10 ML. His own principle is *"buildings unless in
earthquakes usually give signs before they collapse"* (`MATTHEW-TODO.md:183-184`), and his collapse
fuse is 10–20 s. *"Suddenly"* in his 09-25 words is the best case for no fuse, and D2 names it as a
revisit trigger. Through play, a warning plus a fuse is the only version that is readable and
recoverable.

**3-4. Hand mining credits the pocket, so the one store holds nothing you dug. Cost.** §4: *"a dig
should not start a hold bill the player did not choose."* Then *"swim or mine to get it back"* has
nothing of yours to give back except quarry spoil and water.

**3-5. A vault holds nothing until parts are installed. Cost.** *"An empty slot has factor 0. A
vault with no entangler holds nothing."* The newcomer's first vault reads zero until they
right-click two parts into it (§6, the demo's step 2). That is survival's crafting arriving as
friction.

**3-6. The pack is "refilled first". Cost, and it must be pinned (see 4-2).**

**3-7. The steam bell is cut as "presentation" (§3.1). Cost, minor.** The brief's inheritance
table, which he agreed to, has it. It is a sound effect, but that is the kind of thing he enjoys.

### Design 4

**4-1. The physics register he declined. Cost, likely re-litigated.** The design calls itself "The
fold". Its JSON carries `tollJoulesPerVoxel`, a `throat` block and `dischargeCoefficient`; its HUD
speaks of τ; the Berkeley Pit is its reference. He chose *"a mining-operation tone over a physics
one"* (`M10-NOTES.md:227-231`). The invented law can stay underneath, but its vocabulary should
not reach the player.

**4-2. The carried pack: "the channel pays the refill first". Must not ship in one reading, and
must be pinned.** §4 says the pack's charge *"is part of its channel's pool"*, then *"the channel
pays the refill first"*, and *"a window can overdraw its channel by at most one boundary's spend"*.
- If the refill is a write-back after consumers, which is the tick's shape (consumers draw, then
  `RechargePack`, then `WriteBack`; Reader A §4.2), this is one pool with a buffer. That is fine.
- If the refill is ordered before the vault holds, the last 500 MJ of the bank sits in the pack
  where the rent cannot reach it. The vault then fuses while the HUD reads 500 MJ. That is a
  separate pool with an automatic top-up each boundary, **the two-pool arm he declined when he
  chose one pool** (brief: *"One pool please Claude"*).

The test is: a bank of 400 MJ, a full 500 MJ pack and a vault billing 1 MJ. Is the bill paid? D3
has the same ambiguity (pack priority 15, *"refilled from the bank first"*).

**4-3. The part IDs replace his grade names with eras. Cost, likely rejected.** The example IDs are
`emitter/spark_gap` and `cell/lead_acid`, with `"grade": 1`. Labels come from identifiers (M6d-3),
so his *"low grade, medium grade, high grade, mythical grade, god grade"* never reaches the screen.
The eras are lovely flavour text. The names are his.

**4-4. Vented solids are 1 m pieces and can take 2.3 h. Cost, likely rejected.** *"They go as 1 m
pieces through the collapse director"*, and *"A god vault full of stone empties in 2.3 h."* He chose
voxel pieces over 1 m. A dump that trickles for hours is a chore, not an event.

**4-5. The demo's collapse beat does not match its geometry. Cost.** At 2:30 the quarry benches
from the rim, an open pit. At 4:30 *"the stope's roof tints"*. An open pit has no stope roof.

**4-6. Bankruptcy is told, not shown. Cost.** With 5 GJ granted and costs in MJ, the newcomer cannot
go broke in five minutes. The climax is `enabled = 0` on the vault, and *"Bankruptcy does this to
every vault at once"* is a caption. It is honest, but taken 4's feature never happens in the demo.

**4-7. The quarry has nine ports, and a placed vault is empty until fitted. Cost.** `room`,
`onwet` and `enabled` defaulting to 0 are all good ideas. Together they are a panel of nine
spinboxes and an emitter slot that must be filled before anything works.

---

## 3. Where the designs disagree, and my lens decides it

1. **Does hand mining credit the pocket or the vault?** The vault, while bound (D2, D1's façade),
   because *"swim or mine to get it back"* needs your things in it. But binding must not pour the
   existing pockets in (D1's rule, not D2's). D3's pocket-only credit (3-4) empties the stake.
2. **Is the vault's bill flat or by fill?** By fill (D1, D3, D4). It makes D2's creditor sentence
   true and gives banking a litre a price over time. Keep D2's 10 s moving average on the runway, so
   the number does not jitter with each bite.
3. **One carried item or two?** One, the backpack carrying the cell (D2), for a newcomer who should
   equip one thing. Ask him in one line, quoting his two pairings. D3's *"the other arm is his word"*
   is right that it is his to take.
4. **Grades on the vault ID, or on parts?** Parts (D3, D4), because his quote is about building with
   mixed components, and IDs are forever (D3 must-not-ship 5). But the palette grants vaults already
   fitted, so no newcomer meets an empty slot (3-5), and his five names are the suffix (`/god`, D3),
   not eras (4-3).
5. **Strict loader or not?** Strict, at least for the new blocks. A misspelt `"recal": false` that
   loads with recall on is a play bug he would hit (D2 §2). D3's did-you-mean is a kindness to him.
6. **A template world or a kit?** Both. D2's template is the demo he hands over. A kit on new
   creative worlds needs D4's charged starter cell through the printed `debug` term, or it
   reproduces 1-6 and 3-1.
7. **Recall at release, or at the drain?** At release (all four agree), and **only while solvent**
   (D2, D4). Recall is the most protective extra, and D2's hinge, *"bankruptcy strips your
   protections one at a time, and recall is the last to go"*, keeps the top tier from being safe.
8. **Does a signal ever dump a vault?** Never, by either road (X-1, D2's rule). A hand valve on
   `enabled` with the fuse and a cancel (D2) is his 09-25 wish and stays.
9. **Fuse or immediate dump?** A 10–20 s fuse leaning with the deficit, in cyan (D2). Once the fuse
   burns out, the dump runs to the end (D1's dead state), so no one meters a flood by flickering
   the power (D4's own revisit, decision 7).
10. **What shape is the dump's solids?** Pieces through the director, at the grain he chose, landing
    in their own colours (D2, D3). Not D1's box fill, not D4's 1 m pieces.
11. **What does the siphon cost?** A round trip at relay parity: take plus pour is exactly one relay
    pump (D4's rule; D2's 1,000 + 0 also meets it). Never D1's 100 J/L.
12. **Which quarry bite, and where does the box go?** The 2 m bite, in front along `Facing` (D2).
    Its progress is visible inside five minutes, where 1 m³ a boundary takes 34 minutes for a 16 m
    box (D4's own figure). D1's pillar pit is out.
13. **What happens when a vault leaves?** Its contents go to its sisters first (D2, D3). At-range
    tools and the quarry skip a non-empty vault (D1's `Refuses`, narrowed). A deliberate removal is
    the hand pick's.
14. **When does the runway warn?** Amber at 10 minutes and red at 60 s (D4), smoothed (D2). A
    60 s amber (D2, D1) is too late for a slow drain.
15. **Debug keys in creative?** Off (D2), and he is told sandbox keeps them.

---

## 4. The winner through my lens, and the grafts

**Winner: Design 2.** It is the one design whose loop, demo, register and decisions are shaped for
him and for a stranger's first five minutes. It leaves the mine the antagonist: the siphon is the
fastest way to go broke, recall fails when you are broke, and the dump raises the lake that brings
the walls down (Tier A, so the keystone applies). Its 23 decisions are ones he can answer from a
play, and its revisit triggers quote him (*"'suddenly' meant no fuse"*, *"he finds the top tier too
safe"*).

**Grafts.**
- **From D1:**
  - The pure window: the pack holds nothing, and the hand debits rows directly, with no new
    `PowerExact` term. It is the exact mechanism behind D2's *"spends against the bank as it stood"*.
  - No pour at link (fixes 2-4).
  - `Refuses` for at-range tools and the quarry on a non-empty vault (fixes 2-6).
  - Dumping is terminal once it starts.
  - Must-not-ship 1 and 2: never save the window's balance, or the façade's merged view.
- **From D3:**
  - A display-only water ID, `placeable: false`, keyed off the computer's verb by
    `"programmer": true`, and moved only by the siphon (fixes 2-1).
  - Grades as parts with his five names as the ID suffix, and the god-grade machine JSON as the
    ideal (fixes 2-3).
  - Unknown-key refusal with did-you-mean.
- **From D4:**
  - *"The hole is the bill."* Seepage makes depth a standing water bill, which gives the quarry an
    indirect road to bankruptcy that D2 names but does not price.
  - The cell part that adds joules to the pool, which is his *"pack … goes into the container"*.
  - The charged starter cell through the printed `debug` term, for kit worlds.
  - Relay parity for the siphon.
  - The 10-minute amber.
  - The line *"the quarry is an overburden machine"* for the card.
  - The eras as flavour text on the grade names.

---

## 5. Estimates stated as facts

- **D2 §7, decision 17:** *"One quarry at rate 1 crosses the `machines > 1 ms` trigger alone."* Its
  own figure is 0.8 ms a bite (512 voxels × 1.5 µs), which is under 1 ms. The 1.5 µs is TNT-NOTES'
  Debug figure from before the 2026-09-28 Core fix (Reader A trap 18). It should say: estimated
  0.8 ms at rate 1 on a pessimistic Debug figure; a Release bench is owed.
- **D2 §3.7:** a quick player can *"shrink the dump"*. That is 4–8% of the demo vault (2-7).
- **D2 §9.4:** *"Idle in sun, the net is about −0.2 kW, so no runway shows."* Its own rule shows the
  runway whenever the net is negative. It would read about 58 days (1 GJ ÷ 200 W, arithmetic).
- **D2 decision 6:** *"the excavator fills a high vault in about 20 bites."* In its own demo it is
  about 4 (2-5).
- **D1 §5.2:** *"10 ML (640,000 cells) is the CPU-honest top."* No dump has been measured. The
  3.4 M figure is active cells on the load test, not a dump rate. It should say: a cap chosen by
  analogy with the load test, owed a bench.
- **D1 §9, step 6:** *"the bank is empty"* after the cell is switched off. Solar still feeds the
  pool (1-6).
- **D3 §5:** a 100 ML god dump is *"about ten times the load test"*. The load test was 3.4 M active
  cells (66.6 ML), and 100 ML is 6.4 M cells, about 1.9× (arithmetic). The "ten times" takes
  M10-NOTES' loose *"640,000 cells, the load test's scale"* as the load test itself.
- **D3 §9, beat 5:** the quarry pit *"undercuts a slope"* and the roof falls (3-2, against
  `M9-PLAN.md:97`).
- **D4 §9, 4:30:** *"The stope's roof tints"* over an open pit (4-5).
- **D4 §5 grade table:** the vent times (1.0 s to 17.9 min, and *"10 ML … out in 9.9 min"*) are
  derived from an invented throat and C_d. The labels say so, but the IM-3 oracle, *"drains in
  592 s ± one boundary"*, will make them read as fact once tested.

---

## 6. What Matthew is likely to reject or re-litigate, given his record

His record: he took decision 10 on the keystone argument and did not look back; he kept voxel
pieces over a 1 m cube; he kept black pieces as *"you aint getting that back"*; he answers "as
recommended" when the recommendation reads like his words, and he re-opens a decision after a play.

- **D1:**
  - One vault per channel (he will place two).
  - The 10 ML cap on god (*"or more depending on configuration"*).
  - No cell in the vault (his 09-25 image).
  - The immovable vault and the cleared undo (*"Ctrl z feels good"*).
  - The box-fill heap (he chose voxel pieces).
  - The pillar quarry.
- **D2:**
  - Water off the hotbar (taken 8's wording).
  - Grades on the vault ID only (his parts mix).
  - No cell in the vault.
  - Debug keys off in creative, until he is told sandbox keeps them.
  - The template chore, perhaps.
- **D3:**
  - The instant dump (*"give signs before they collapse"*).
  - Empty-slot vaults in the demo.
  - Pocket-only hand credit.
  - The cut steam bell.
  - The format decisions: he will hand these back unread, and that is fine.
- **D4:**
  - "The fold" and its physics vocabulary.
  - Era IDs in place of his grade names.
  - 1 m vent pieces and a 2.3 h trickle.
  - Bankruptcy shown only as a caption.

One seam none of them raises, for his long-term goal: **channels are per world, 0–15**. On a
server, a second player on channel 0 spends your bank and fills your vault. That is *"someone else's
mine"* arriving by accident, not by design, and the first multiplayer pass should know it.
