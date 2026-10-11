# Matthew's TODO

Your file. `TODO.md` is the project's open work and mine to maintain; this is the short list of
things only you can do. Tick things off, scribble in the notes section, delete anything you don't
care about. I keep it current at the end of each session.

**Quick reference**

- **A world by name:** `start.cmd "--world=Deep Mine"` — opened if it exists, created if not.
  Quote the whole flag if the name has spaces, or you land in `default`.
- **A new world's seed** shows in the upper right for a few seconds; `--seed=<n>` builds that
  landscape again, on a *new* world only. `--clock=<seconds>` starts a new world somewhere in its
  weather.
- **Relief, new worlds only:** `--relief=` (how tall the mountains, shipped 3), `--reliefbase=`
  (how flat the plains, 0.4), `--reliefrare=` (how rare the mountains, 0.25). Low = less of the
  thing named; nothing is inverted.
- **The log** is `%APPDATA%\Godot\app_userdata\Overburden\logs\godot.log`. Godot keeps five and
  rotates, so copy one aside if you want it kept. You never have to read it — a screenshot and a
  sentence is enough, and I'll find the numbers.

---

## Now

- [ ] **The music's MIDI** (your marimba render in FL Studio is yours, and the folk tune is public domain).
      The open piece is the MIDI: if it copies the Game Boy version, its bass and harmony are someone else's.
      Easiest fix: keep the melody and write your own bass and harmony under it. Or tell me which site it came
      from and I'll read its terms. (Renamed `korobeiniki-marimba.mp3`, so "tetris" is out of the file name.)

- [ ] **The quick start**, [QUICKSTART.md](QUICKSTART.md): every key, written for a stranger. Have a read and
      say what's wrong or missing; it becomes the in-game controls card and the zip's readme.

- [ ] **The pack's recharge, when you're near it anyway** (the pack itself is played, see Answered): place a
      vacuum cell (`P` gives them), `Alt+P` to fill it and the pack, drain the pack with Q, and watch it come
      back from the cell, a cube bite's worth every 1.6 s. Your play had no cell, so this part is untried.

- [ ] **Ctrl+Z on a fill that's still waiting** (IM-4c, built 2026-09-30): place a single block right into
      water (or fill a chunk at range) and press Ctrl+Z at once. *Nothing should land.* Before, it came
      back half a second later. The log says `undo — 1 placement(s) waiting for the boundary cancelled`.
      A shape half in water puts its dry half down at the click: the first press cancels the wet half,
      and a second takes back the dry half. (You said you'll test it.)

- [ ] **M10d-2, the play** — the recipes below. Nothing else is blocked on me.

---

## The play queue — one sentence back on each

Any order, any number of sessions; "it was fine" is a real answer. Flags go on the end of the
`start.cmd` line.

### Recent builds

- [ ] **The load test you wanted.** Same world, same spot, two short sessions back to back:
      `start3` then `start3 --fedadvance=0`. Play normally for a few minutes each and quit; copy
      `godot.log` aside between them. My read is that the flood's size is the cost, not the fed
      advance — this says whether I'm right.
- [ ] **The vacuum cell's switch.** Computer in hand, right-click a cell: set `enabled` to 0 and
      the status goes amber (`OFF — holding …`), pumps that only had that cell stall. *Is a 0/1
      box good enough, or do you want a real button?*
- [ ] **Glass, the one left.** Does water seen through glass ever draw in the wrong order?

### M10d — the seven

- [ ] **5. The pillar.** `--collapse=0`, the excavator (`--nogate`), a 2 × 2 bite three chunks
      down, wait for the tint, then a stone column floor to roof under the plug's centre. Then
      clay. *Did the pillar read as shoring, and did clay holding as well as stone read wrong?*
- [ ] **6. Collapse, four ways** (`--nogate`, the excavator): *settling* (a 2 × 2 of chunk bites
      24 m down; `--collapseafter=2,10`), *attributable* (`--collapse=0`, look up — could you say
      where it would go?), *the soil patch* (dirt falling, stone holding), *the water brought it
      down* (under a pond, then `--saturation=0`).
- [ ] **7. The tiers and the hold.** Fresh pack (`P`, right-click to equip), `Alt+P`, then `T`
      through the cycle holding Q on each. *Does the battery read as the gate, and the cycle as a
      ladder?*
- [ ] **8. The panel by hand.** Right-click a pump with the computer, type 99 into a box capped at
      15, press Esc once with the field live. *One Esc or two?*
- [ ] **9. The pack across a quit.** Quit wearing a pack, reopen. *Does Q costing charge while T
      doesn't read as the design?*
- [ ] **10. Undo's edges.** (a) Bite, spend the material, Ctrl+Z — refused whole? (b) Bite into a
      room, let it flood, Ctrl+Z — rock back, water not un-flooded. (c) Three presses in a row,
      and one for an edit in a chunk you've walked away from and back. *Did any read as broken?*

### Smaller ones, if you're near them anyway

- [ ] **The courtyard's fourth wall** on a lake's bank — the pool should stop being the lake's at
      the next boundary. Does a courtyard on a slope read right?
- [ ] **Clay as a lining** on the wettest wall of a room under the lake — the `seep` line's face
      count should fall by the wall's count. Does the colour read as clay?
- [ ] **A long session's tail:** standing still with calm water, `last rebuild N panel(s)` on the
      `M8b:` line should be a handful; `live … at the last gen2` on the `M2c:` line flat-ish over
      an hour.

---

## Waiting on your word

Nothing is blocked on these; each is a small change once you have a view.


- [ ] **The collapse: pieces that jump — I gave you the wrong size.** You said "leave it" when I
      said "at most a metre". On a copy of your tower, one piece jumped 4.5 m (about 20 pieces in
      800 jumped at all). Watch for it when you play: if you see a piece hop up, say "fix it" —
      the fix is costlier to run, but it is the right one if it shows.
- [ ] **TNT** — [docs/TNT-NOTES.md](docs/TNT-NOTES.md), six decisions still open. The biggest: a
      blast destroys the machines it reaches (5). "As recommended" or any one back.
- [ ] **Dust when a roof comes down** (M9c-2). Optional; only if falls read as too quiet.

---

## Yours to run

- [ ] **`git push`** whenever I say the tree is ahead.
- [ ] **Delete the scratch worlds** if you want the space back (under
      `%APPDATA%\Godot\app_userdata\Overburden\worlds\`): `edge`, `m6d2dug`, `m7c-clock`,
      `m7c-shaft`, `m7c-shot`, `m7d-pond`, `m7d-water`, and `Regions1`–`Regions9`. **Keep** the
      three `retired-*` worlds and `Regions16-pondlook-demolished-2026-09-26` — those are my
      measurement worlds.
- [ ] **Delete the pre-rename backup**: `%APPDATA%\Godot\app_userdata\MinerCraft\`.
- [ ] **Rename stage 4**, no hurry: the GitHub repo `MinerCraft` → `Overburden`, the local folder,
      the remote URL.

---

## Things I'd like your eye on — not scheduled

- [ ] **The pumps as a system** — building an operation, or chores?
- [ ] **A musical journey through everyone's folk music** — your idea (2026-10-12), *"next time I get bored"*.
      Korobeiniki started it: folk tunes old enough to be public domain, arranged your own way in FL Studio.
      Maybe more collapse tracks, or a tune per region one day.

---

## Notes to myself (fill in freely, I'll fold them into the plans)

-
-
-

---

## Answered lately

- **The public alpha's twenty-three decisions** (2026-10-12): *"AS recommended"*, with your changes. Far mode
  moved to **F10**, and **F11 (and Alt+Enter) is fullscreen**: built. Your email stays on the commits (*"they
  only get filtered"*), so that clean-up is dropped. The phonk is gone and the game plays your marimba track, renamed
  `korobeiniki-marimba.mp3` on your word (*"renames as you please"*). Next is session A, the export; it needs the Godot export templates, a big download I'll ask about first.

- **The lithium-ion pack, played** (2026-10-12): *"i am happy with it really thank you Claude"*. The log: no
  errors, the pack filled twice with Alt+P, 10.7 MJ through the quantum tools (about 3,300 cube bites), the
  energy total exact. One wart it showed: a right-click with a lithium-ion pack in hand swapped your 5.0 MJ
  pack for an empty one (the 5.0 MJ went to your inventory, and the next right-click would have taken it
  back). In TODO for the alpha.

- **The lithium-ion pack and `P`** (2026-10-12): *"can we do the lithium ion battery pack this session … Lead
  acid is so 20th century"*, and *"100 of each item"*. Your four picks were all the recommended ones: 14.4 MJ
  (four lead-acids), 2 kW (full in two hours), and `P` adding a hundred every press rather than topping up.
  Built and on main, the check is at the top.

- **The hotbar's unit** (2026-10-12): *"Yeah switch to cm3"*, read back as cubic metres and confirmed: litres
  under a cubic metre, m³ from one up. Your 1,767,903,281 L now reads `1,767,903 m³`. Built; IM-6's water slot
  uses the same rule.

- **IM-5b's seven choices** (2026-10-12): all stay as built, with your notes beside them in the plan. 3: *"its a
  quantum power failure event so i am happy with this or wierd stuff happening"*; 4: *"just trying to make it a
  pain to get close this should be enough"*. 5 had no note and I took your *"sweet"* as covering it; say if not.

- **The walled area beneath TajMahal2** (2026-10-12): *"two seperate areas that i just leveled no failure was
  just looking at rainfall"*. Closed, nothing for the lake plan to cover.

- **A pile resting on a machine** (2026-10-11): *"yes totally approve"*, so a pile can bury a pump's intake and
  stall it. Your aside is recorded too: an early item to breathe water, so nobody asks why you don't drown
  (M10-NOTES, beside your oxygen → rebreather → entangled-lungs ladder; it belongs to the first crafting tier).

- **The pile's shape** (2026-10-11): *"Cone, 1 up : 1 out"*. Each block goes to the free spot lowest in height
  above the vault's floor plus distance out, so a heap fills the hollows by its vault first, covers it, and grows
  at 45°. About 4.6 m across its radius for 100 m³, 8.7 m for the demo's 700 m³, 21 m for 10,000 m³. Built.

- **IM-5a's six build choices** (2026-10-11): *"yes as built please Claude, go on IM-5b"*. All six stay: the 2 m
  minimum ball, the 10 s rest when a ball finds no room, the 10 ML limit checked when a vault first owes, the
  chunk above the vault gating a pour, an empty owing vault ending its dump, and the release clock running while
  the water waits. IM-5b is under way.

- **The windowed run** (2026-10-11): *"go on the windowed run please Claude"*, then *"as recommended please
  Claude thank you!"*. Dropping all 10 ML at once on open ground froze the picture for 0.15–0.25 s about once a
  second while it spread, against 0.02 s average and 0.14 s worst in your own play that night. A real dump pours a
  little each half second, so **the 10 ML limit stays**, and we look again with a real vault burning out
  underground at IM-6. Your log from that session is kept as `play-LakeTest-2026-10-10-2140.log`.

- **The pond branch: merged** (2026-10-09): *"yes only that side rose, merge it unless you think otherwise or it
  requires further testing"*. Your play split three lakes as you built, R into one side raised only that side,
  your glass pit held, and your pumps moved 1.68 ML with every litre accounted for. Before main took it I ran the
  checks the plan set: all 2,247 tests, the r6 check, the courtyard, the collapse and pump checks and the dam,
  all as recorded and every water total exact. It's on main now. `water-column-test` is still on GitHub until you
  delete it (the commands are in the status below).

- **Walls that split a lake, seven decisions** (2026-10-01): *"as recommended please Claude"*. Taken, and
  the first half built the same day (L-1, below in the status) ([docs/LAKE-EDIT-PLAN.md](docs/LAKE-EDIT-PLAN.md)).
  In short:
  - turning the lake into Tier B can't keep two sides apart, because the water rejoins the one lake once it
    settles;
  - your pond branch already divides a lake at a wall, but it gives all the water to one side and leaves
    the other dry, a bug no test caught;
  - so the fix goes on the branch: each side keeps the water that stood over it, and the water the wall
    itself stands in goes to the side it drains to (usually one side), which rises a little;
  - then your play on `LakeTest` (a wall across a full lake, R into one side) becomes the branch's deciding
    test;
  - one small fix on main as well: whether a walled area counts as walled in can depend on which spot is
    checked first. That may be the sheet that "bled in and out of walls".

- **`start.cmd`'s settings as the game's defaults** (2026-10-01): *"as recommended"*. Not now; they
  flip when we make the first build someone else plays (in TODO under "Later", with the reasons).
  **And the `--adaptive=false` bug is fixed**: it and four other on/off flags now read 1/0, true/false,
  on/off or yes/no, and anything else is refused with an error instead of quietly meaning "on". Nothing
  you play changes, since `start.cmd` passes `--adaptive=1`.
- **The pond branch merge** (2026-10-01): *"go ahead and merge main into the pond branch"*. Done: 99
  commits, two small code conflicts (both sides had added things side by side), all 2,219 tests pass and
  the r6 check reads to the digit. The clay-roof drip you reported on 09-26 lived only on the branch; it's
  on main now too, beside the clay pond leak, since they may be the same thing.
- **TajMahal2 and the lakes** (2026-10-01): *"you can delete or retire tajmahal2 as you please"*. It is
  retired, `retired-TajMahal2-2026-10-01`, moved and not deleted, so it can come back. Next session's plan
  is the one you named, walls that split a lake, in LAKE-EDIT-NOTES with your words verbatim.
- **The test that failed at random** (2026-10-01): fixed. Its failures may all have been one race, which
  was fixed on 09-06, and the test was mostly not checking what its name says. Now it checks that every
  run. Nothing you play changes.
- **IM-5's sixteen decisions, the dump** (2026-09-30): *"As recommended please Claude uh just a plan"*.
  Taken, not built yet: the water first (5a), then the blocks (5b). You also asked what in the TODO
  should come first and what collides with planned work: that is TODO's new "Order and collisions" at
  the top, ten of each, and the four that need you are at the top of this list.
- **IM-4c and placing in a lake** (2026-09-30): you kept today's rule (*"Keep today's rule, plan
  displacement"*), since your notes said displace rather than delete. So a block in a lake keeps the
  lake's water, and nothing needs pricing. Built: Ctrl+Z cancels a fill that's still waiting (a bug, the
  check is above), and the water line in the log shows the machines' and the siphon's litres. IM-4a and
  IM-4b were built earlier the same day. Next on the plan is IM-5, the dump.
- **Your notes** (2026-09-30), folded:
  - *The lake over your quarry*: "draws nearly perfectly", a small gap filled by Tier B soon after.
    Closed; the rest is the lakes talk.
  - *The phonk*: "sounds good an want to leave it as is". Left as is.
  - *F11*: six steps, 6/2 up to 16/12, the loader's reach as the ceiling. In TODO for your session:
    16/12 is about five times 10/6's chunks, so the memory and limits get checked first.
  - *Glass*: fine in rain and storms. Your stone-strong, clay-tight material is concrete in real life
    (crushed stone, and cement burnt from limestone and clay): in MATERIALS-NOTES, a recipe chain for the
    crafting milestone.
  - *Dry (M10d 3)*: you could tell where the water came from and went; outlets read best on pillars
    with gaps. *The storm (M10d 4)*: you like the rain slowing the pumps. Both recorded.
  - *A rock placed in a lake*: displace it, or drop it from the sky. Into the lakes talk.

- **Your two answers** (2026-09-30): *"1 and 2 as recommended"*. **2 is built:** your computer's clock is
  at the end of the HUD's coordinates line; have a look next time you play. **1 is held**, for the reason
  above; it's IM-4's decision 10 now.

- **IM-3b's seven decisions** (2026-09-29): *"As recommended please Claude thanks Claude we seem to be in
  tune on this like"*. Built the same night, while you played. A vault now carries a minute of its bill as
  it stands, and its fuse waits while the bill is paid and goes out only when that minute is back. Before
  this, a channel paying 5% of a vault's bill held it for ever. Nothing you play changes until IM-6.
- **Your answers to "things I'd like your eye on"** (2026-09-29), folded:
  - *The mine as antagonist*: water is landing, and you have nothing to dig for yet. Ores (iron, coal)
    and a tree pass went into the worldgen notes.
  - *5 m reach*: right for the low tiers, growing with tech; bare hands too punishing to start with. Into
    the tools' plan (M10g).
  - *The excavator*: *"excellent tool"*, for hallways and big rooms, and faster than mining by hand.
    Noted; nothing changes.
  - *Twenty minutes free*: your wearable quantum battery is IM-2's backpack, already built. The water
    gun is IM-4's siphon, and your sizes (the miner's, the tunneller's and the excavator's) went into
    IM-4's plan. Lithium-ion and li-poly packs are the middle rungs, for M10g.
  - *Under the water*: Tier A should match Tier B's colour and see-through. Into TODO, buildable
    alongside IM-4 on your word; it'll need a few screenshots.
  - *Silence*: rain and wind, from your storm recordings first, PySynthRack if not. Into TODO's
    cosmetics pass.
  - *The HUD*: the time. Your question is above.

- **IM-3's twelve decisions** (2026-09-29): *"as recommended please Claude"*. Built the same night: the vault
  and the crate underneath (nothing you play changes until IM-6). Your warranty line is saved for the vault's
  panel. **Your three asks mid-build, taken as recommended:** a vault carries a minute of its own bill in
  energy (next, IM-3b); a lock on the vault panel's channel box; power shown as W/kW/MW on screen, the files
  staying in watts (both IM-6).

- **IM-1's eight decisions** (2026-09-29): *"as recommended please Claude"*. Built the same night: mod
  files are strict now (a misspelt key is refused with a "did you mean"), each mod declares a `format`, the
  six extras have their grammar and table, and only the computer opens panels. Nothing you play changes.
- **Your new world renamed** (2026-09-29): `TajMahal2`, and `start.cmd` opens it.

- **The quarry's bite** (2026-09-29): *"go with the 2m bite for the quarry"*. The measurement agreed:
  a 2 m bite costs about half a millisecond on the fixed build.
- **The ideal machines' ten forks and sixty-one decisions** (2026-09-29): *"Yes as recommended please
  Claude"*. **TajMahal retired** the same night (*"I borked the supports so want to restart"*): it is now
  `retired-TajMahal-2026-09-29`, and your next `start.cmd` makes a fresh TajMahal.
- **The ideal machines, during the pass** (2026-09-29): *"One pool please Claude you should be able to
  bankrupt yourself energy wise at the last tech"*; automated quarries and materials in; *"happy to have
  water as an inventory item"* through a tool or machine at the last tech; the quarry only where chunks
  are loaded, *"for OH&S reasons"* and so players can't lag a server; a per-player machine cap as a
  trigger only; each player their own channels on servers (in TODO, its own pass).
- **The collapse sensor's thirteen decisions** (2026-09-29): *"as recommended please Claude"*.
- **The fuse** (2026-09-27): *"ideally the time would be from 10-20 seconds but randomly"* — arm 2
  is built: 10–20 s, and the more overloaded the rock, the more it leans toward 10 (a gutted roof
  draws in 10–15 s). `--collapseafter=` still changes it.
- **The Tetris collapse, drawn** (2026-09-27): *"make it the default please"* — it is; the old
  one-sweep fall is `--collapsemode=sweep`. The cyan: *"like they turn cyan yeah like it"*. The
  black pieces stay: *"indicitave that you aint getting that back"*. The settler you saw was the
  pile tumbling — 65% of what fell fell again. There is no sound in the game yet; that was the tablet.
- **The collapse, part c's ten decisions** (2026-09-26): *"as recommended"*; c-1 is built.
- **The Tetris collapse, played on your tower** (2026-09-26): *"yes slow to fast … buildings unless
  in earthquakes usually give signs before they collapse"* — the pace table stays as built.
- **No rain under your roof** (2026-09-26): *"no rain inside now the roof is dry"* — the log
  agrees: 52,195 L rained, 2,098 drops onto what you built, 0 spawned under a roof.
- **The quantum tunneller** (2026-09-26): *"I like the 2m cube"* — the 2 m middle tier stays.
- **Glass's look** (2026-09-26): black at 0.20 — *"Glass looks GOOD!"*
- **The Tetris collapse's decisions** (2026-09-26): *"as recommended"*; part a is built.
- **Streams that spread** (2026-09-24): `--fedband=6` — *"i think its the one"*; the default.
- **Water that spreads faster** (2026-09-24): *"loving it"*.
- **The water solve's levers and pass cap** (2026-09-24): as recommended, built.
- **Relief, the map, the compass** (2026-09-24): the default shape stays, 2 km stays, *"love the
  compass"*.
- **Every quantum tool one click one bite** (2026-09-24), Q held to run.
- **Ctrl+Z** (2026-09-22): *"Ctrl z feels good, disallow if something falls yes"*.

---

## Status, kept current for you

Updated **2026-10-12**. **Main is ahead of GitHub by the last two sessions' commits since your last push: a
`git push` whenever suits.**

- **The lithium-ion pack is in, and `P` gives a hundred of everything** (2026-10-12). The pack holds 14.4 MJ
  and charges at 2 kW. Everything else about it works like the lead-acid: you wear it, swap it, and it keeps
  its charge in your inventory. A volume on the hotbar now reads in cubic metres from one up. All 2,302 tests
  pass, I broke the new code 25 deliberate ways and every one was caught, and the game's quick check reads
  exactly as recorded. **You can play this one now**: the check is at the top of this list.
- **IM-5b is built: the dump's blocks.** When a vault's fuse burns out, or a fall wrecks it, its blocks come back
  into the world as a heap round it, your cone, after its water.
  - It never builds into water, a lake, a machine, you, or where a falling piece is about to land; it waits a
    moment and tries again.
  - Machines, packs and anything that can't lie loose are lost, and the log names them. So is anything past
    10,000 m³.
  - Ctrl+Z can't touch a pile: a step you took where the pile lands is dropped, so it can't write over it.
  - **Nothing you play changes yet:** no vault ships until IM-6. The cyan glitch as blocks appear comes with
    IM-6 too.
  - First a speed test: 512 blocks every half second costs about 2 ms, but the more chunks a pile touches, the
    more relighting follows, so it writes into at most 8 chunks at a time.
  - All 2,278 tests pass, and I broke the code 42 deliberate ways: every one was caught (one only after I made
    its test stricter).
  - The game's quick check reads exactly as recorded.
- **The windowed run is done and your answer taken**: the 10 ML limit stays, and we look again at IM-6.
- **IM-5a is built: the dump's water** (2026-10-10). On the surface it goes into the lake that ground drains to,
  over about ten seconds; in the mine it pours out round the vault, a little each half second. An emptied vault
  works again; an emptied wreck disappears.
- **Next: IM-6**, the first thing you can play with a vault. Before it: the ten build warnings, and ideally the
  collapse sensor's first step.
- **The clay leak is closed, not reproduced** (your word): your glass rain pool filled and held, and glass and
  clay are the same to the water. The 09-26 clay-roof drip closes with it; that day's look had already found the
  roof sound and the drip running off the eave outside your glass.

- **Walls that split a lake are on main now** (L-1, L-2 and E-1). A wall across a full lake leaves each side the
  water that stood over it. The water the wall stands in goes to the side it drains to, which rises a little:
  about 18 cm on your 4.2 ML lake in my test. You played it, and R raised only the side you poured into.
- **Main against your pond branch on `LakeTest`**, three runs each before the merge: the same frame times
  (7.2 ms, worst ~141 ms) and the same water speed (2 ticks a second). The branch's extra work is about 9 ms a
  second, which you won't feel, and both held the same total water. Numbers in WORKLOG.
- **A walled area now counts as walled however the game looks at it** (E-1).
  Before, a walled area 16–32 m across could count as walled in when checked from near its middle and as open
  when checked from near a wall, and an edit could flip it. That may be the lake's sheet bleeding in and out of
  walls you saw; if it happens again, tell me where.

- **The ideal machines plan is taken**, [docs/IDEAL-MACHINES-PLAN.md](docs/IDEAL-MACHINES-PLAN.md): the
  bank, the vault and its dump, water in the inventory, the quarry (your 2 m bite), the six extras, and a
  creative mode with a kit. **IM-1 to IM-4c are built**, all underneath: the mod-file rules, the
  quantum backpack as a window onto your cells, a `priority` box on each pump's panel, the vault and
  its bill, the siphon, and a vault taking a pump's water. **IM-5, the dump, is planned and taken**;
  four things need you first (top of this list). The first thing you can play is IM-6.
- **The undo bug is fixed** (IM-0b): a machine a fall destroys can no longer come back free after
  Ctrl+Z. The build you started at 4:49 PM already has it. A Ctrl+Z right after a roof comes down now
  says *the roof came down*, as it should.
- **The first LakeTest is retired** (2026-10-11, your word), as `retired-LakeTest-2026-10-11`, so you can
  playtest fresh. Your new test world is **`LakeTest2`** (seed `0x3794C6C5`), and `start.cmd` opens it. The old
  one stays because the lake-split numbers were measured on it. TajMahal2 is `retired-TajMahal2-2026-10-01`, and
  the first TajMahal `retired-TajMahal-2026-09-29`.
- **The collapse sensor:** all thirteen decisions taken, the first build step is ready.
- **The Tetris collapse** is the default, with the cyan glitch, your phonk (left as is, your word) and a
  10–20 s fuse.
- **Far mode is on F10 now** (F11 is fullscreen, and Alt+Enter too): 6 → 8 → 10 chunks out; your six steps to
  16 are in TODO.
- **Water bodies are parked.** Your quarry does rest with the near/far setting off, but every cheap
  fix with it on failed on your biggest world. What you play is unchanged.
- **Backups:** the old TajMahal is `retired-TajMahal-2026-09-29`, with `backup-TajMahal-2026-09-28`
  beside it; your tower is `backup-Regions16-pondlook-2026-09-26`. Say "restore my tower", or "bring the
  old TajMahal back" (it would come back as `TajMahal`, and `TajMahal2` stays as it is).
- `start2.cmd` to `start5.cmd` and `pondlook.cmd` are yours and left out of git.
