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

- [ ] **Four things need you before the next builds**, three before IM-5 and one before IM-6 (TODO's
      "Order and collisions" has the whole list of what should come first and what collides):
      - **The clay pond leak: which world, and where?** The dump floods through the same water, so a leak
        under it would look like the dump's fault.
      - **A minute of your screen** for the windowed run (below, under "Waiting on your word"). It
        measures what drawing a big flood costs, which the dump's 10 ML limit rests on.
      - **The pond branch**: at least let me merge main into it, since the dump changes the same water
        code; better, your verdict on it.
      - **The hotbar's unit** is due sooner now: IM-6 shows water in the hotbar, so the "L doesn't fit"
        question arrives with IM-6, not the inventory screen.

- [ ] **Ctrl+Z on a fill that's still waiting** (IM-4c, built 2026-09-30): place a single block right into
      water (or fill a chunk at range) and press Ctrl+Z at once. *Nothing should land.* Before, it came
      back half a second later. The log says `undo — 1 placement(s) waiting for the boundary cancelled`.
      A shape half in water puts its dry half down at the click: the first press cancels the wet half,
      and a second takes back the dry half.

- [ ] **Building in lakes, the talk you wanted** ([docs/LAKE-EDIT-NOTES.md](docs/LAKE-EDIT-NOTES.md)):
      your three notes verbatim (displace the water, a wall breaking a lake's sheet, handing Tier B back
      sooner at ~95%), what the code already has toward each, and four questions. Today a block in a
      lake keeps the lake's water, as you chose. Say when you want to talk it through.

- [ ] **The pond branch's verdict.** `water-column-test` has the raised watershed and your water
      grid built (walls you place raise the lake's rim; the lake reaches under what you build).
      To play it: `git checkout water-column-test`, `start.cmd "--world=Regions16"` (or a copy),
      R or Ctrl+R into your stone ring. *Does it fill to the wall, and does the water stay when
      the ring closes?* Then `git checkout main`. "Merge it" or "retire it" is the verdict.
      *Speed, re-measured 2026-09-29 on the optimised build:* the branch's water costs 1.9× main
      at your pond, not 6×, and only because the pond keeps the water main spills. With the
      raise off it's as fast as main. So speed isn't a reason to retire it. *Your note (2026-09-30):
      more testing and optimising on the branch first* — noted in POND-PLAN and TODO. It's also
      displacement done Tier A's way: a block cuts the lake's room, so the level rises.
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

### Owed from the rain play

- [ ] **1. The clay pond in the rain.** You think it leaks. *Which world, and where's the pond?* Tell me
      and I'll probe a copy (it's in TODO).

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


- [ ] **A minute of your screen, whenever suits.** The last IM-0 measurement (what drawing a big flood's
      surface costs) only shows in a windowed run, which takes over the screen for about a minute. Say
      "run the windowed one" when you're not playing.

- [ ] **The collapse: pieces that jump — I gave you the wrong size.** You said "leave it" when I
      said "at most a metre". On a copy of your tower, one piece jumped 4.5 m (about 20 pieces in
      800 jumped at all). Watch for it when you play: if you see a piece hop up, say "fix it" —
      the fix is costlier to run, but it is the right one if it shows.
- [ ] **TNT** — [docs/TNT-NOTES.md](docs/TNT-NOTES.md), six decisions still open. The biggest: a
      blast destroys the machines it reaches (5). "As recommended" or any one back.
- [ ] **The hotbar's unit.** `1,767,903,281 L` doesn't fit a slot. My lean: litres under a cubic
      metre, m³ above. **Due before IM-6 now**, which puts water in the hotbar's last slot.
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
- [ ] **Commit authorship.** Every commit carries your gmail address. Cheap to rewrite while the
      repo is private, permanent once it's public — `users.noreply.github.com` is the usual swap.

---

## Things I'd like your eye on — not scheduled

- [ ] **The pumps as a system** — building an operation, or chores?

---

## Notes to myself (fill in freely, I'll fold them into the plans)

-
-
-

---

## Answered lately

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

Updated **2026-09-30, at the session's close**. One commit to push (the close); `git fetch`, then
`git log origin/main..main` lists it. **Branches:** `main` and `water-column-test` only. Pull request #2 is
merged, and `im5-plan` and the old `todo-cleanup` are deleted.

- **The ideal machines plan is taken**, [docs/IDEAL-MACHINES-PLAN.md](docs/IDEAL-MACHINES-PLAN.md): the
  bank, the vault and its dump, water in the inventory, the quarry (your 2 m bite), the six extras, and a
  creative mode with a kit. **IM-1 to IM-4c are built**, all underneath: the mod-file rules, the
  quantum backpack as a window onto your cells, a `priority` box on each pump's panel, the vault and
  its bill, the siphon, and a vault taking a pump's water. **IM-5, the dump, is planned and taken**;
  four things need you first (top of this list). The first thing you can play is IM-6.
- **The undo bug is fixed** (IM-0b): a machine a fall destroys can no longer come back free after
  Ctrl+Z. The build you started at 4:49 PM already has it. A Ctrl+Z right after a roof comes down now
  says *the roof came down*, as it should.
- **Your new world is `TajMahal2` now** (renamed at your ask), and `start.cmd` opens it. The old one is
  retired as `retired-TajMahal-2026-09-29`.
- **The collapse sensor:** all thirteen decisions taken, the first build step is ready.
- **The Tetris collapse** is the default, with the cyan glitch, your phonk (left as is, your word) and a
  10–20 s fuse.
- **F11** goes 6 → 8 → 10 chunks out; your six steps to 16 are in TODO.
- **Your pond branch** re-measured: 1.9× main's water cost at your pond, only because it keeps the
  water main spills. Speed isn't a reason to retire it; your look decides.
- **Water bodies are parked.** Your quarry does rest with the near/far setting off, but every cheap
  fix with it on failed on your biggest world. What you play is unchanged.
- **Backups:** the old TajMahal is `retired-TajMahal-2026-09-29`, with `backup-TajMahal-2026-09-28`
  beside it; your tower is `backup-Regions16-pondlook-2026-09-26`. Say "restore my tower", or "bring the
  old TajMahal back" (it would come back as `TajMahal`, and `TajMahal2` stays as it is).
- `start2.cmd` to `start5.cmd` and `pondlook.cmd` are yours and left out of git.
