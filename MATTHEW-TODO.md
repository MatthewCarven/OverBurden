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

- [ ] **The pond branch's verdict.** `water-column-test` has the raised watershed and your water
      grid built (walls you place raise the lake's rim; the lake reaches under what you build).
      To play it: `git checkout water-column-test`, `start.cmd "--world=Regions16"` (or a copy),
      R or Ctrl+R into your stone ring. *Does it fill to the wall, and does the water stay when
      the ring closes?* Then `git checkout main`. "Merge it" or "retire it" is the verdict.
      *Speed, re-measured 2026-09-29 on the optimised build:* the branch's water costs 1.9× main
      at your pond, not 6×, and only because the pond keeps the water main spills. With the
      raise off it's as fast as main. So speed isn't a reason to retire it.
- [ ] **M10d-2, the play** — the recipes below. Nothing else is blocked on me.

---

## The play queue — one sentence back on each

Any order, any number of sessions; "it was fine" is a real answer. Flags go on the end of the
`start.cmd` line.

### Recent builds

- [ ] **The lake over your quarry.** A pit the lake hasn't filled yet no longer gets the lake's
      sheet over it — you see the pit's own water at its real height instead. Your old quarry is in
      the retired TajMahal (`start.cmd "--world=retired-TajMahal-2026-09-29"`), or dig a pit beside a
      lake in your new world. *Does the lake's edge at the pit's rim read right, or does it stop short
      or overhang?* (For comparison, `--lakeoverbite=1` draws it the old way.)

- [ ] **The phonk.** Your track now plays while a collapse runs. It starts from the top, speeds up
      with the collapse (up to 25% faster at the top), and fades out over 2 s when the last piece
      lands. Knock your tower down with the sound on:
      `start.cmd "--world=Regions16-pondlook"` (the fuse is 10–20 s by default now)
      *Is the top speed right — too tame, or too chipmunk? And is the volume right?* Say "faster",
      "slower", "louder" or "quieter" (the settings are `--collapsemusicmax=` and
      `--collapsevolume=`). When your remix is ready, drop it in `media/` under the same name, or
      point `--collapsemusic=` at it.

- [ ] **The load test you wanted.** Same world, same spot, two short sessions back to back:
      `start3` then `start3 --fedadvance=0`. Play normally for a few minutes each and quit; copy
      `godot.log` aside between them. My read is that the flood's size is the cost, not the fed
      advance — this says whether I'm right.
- [ ] **F11, far mode — now three steps.** Each press goes up a step: 6 out and 2 up/down, then
      8 and 4, then 10 and 6, then back to 6 and 2 at once. The view climbs to each step while the
      loader is idle. The 10/6 step is about twice the drawing of 8/4 and about 95 MiB more.
      *Is 10/6 worth it, does it stutter while it climbs, and does the drop back feel instant?*
      (`start.cmd --far=2` starts on the top step.)
- [ ] **The vacuum cell's switch.** Computer in hand, right-click a cell: set `enabled` to 0 and
      the status goes amber (`OFF — holding …`), pumps that only had that cell stall. *Is a 0/1
      box good enough, or do you want a real button?*
- [ ] **Glass, the two left.** Does a floor under a glass roof gleaming in a storm bother you, and
      does water seen through glass ever draw in the wrong order?

### Owed from the rain play

- [ ] **1. The clay pond in the rain** (`--clockscale=20` if you don't want to wait). *Does it fill
      at a rate that reads as rain?*

### M10d — the seven

- [ ] **3. Dry.** Outlets past a divide, or into a walled tank on a placed floor; let the pumps
      run. *Did the mine read as pumped out, and could you tell where the water went?*
- [ ] **4. The storm.** No `Alt+P`. Wait for rain or `--clockscale=20`. *Did the rain read as
      cutting the pumps, and did the ones that kept running make sense?* (Watch for the
      first-placed pumps hogging the channel.)
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
- [ ] **A rock placed in a lake.** A pier into a lake deletes no water today. If that reads wrong,
      I refuse it. (*"not sure about 3 yet"*, from M9d.)
- [ ] **The hotbar's unit.** `1,767,903,281 L` doesn't fit a slot. My lean: litres under a cubic
      metre, m³ above. Due with the inventory screen (M10g).
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

- [ ] **Does the mine read as the antagonist yet?** Or as a pleasant sandbox with weather?
- [ ] **Twenty minutes with nobody asking you to test anything.** What did you reach for that
      isn't there?
- [ ] **Is 5 m reach still right**, now that the quantum tiers reach as far as the world exists?
- [ ] **The excavator** — a powerful tool, or the game switched off?
- [ ] **Under the water**, looking up — does it read as underwater or as a bug?
- [ ] **Silence.** No audio at all. Noticeable yet?
- [ ] **What's missing from the HUD deep in a mine?** Depth, the water above you, the time?
- [ ] **The pumps as a system** — building an operation, or chores?

---

## Notes to myself (fill in freely, I'll fold them into the plans)

-
-
-

---

## Answered lately

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

Updated **2026-09-29, night**. Commits to push: `git fetch`, then `git log origin/main..main` lists them.

- **The ideal machines plan is taken**, [docs/IDEAL-MACHINES-PLAN.md](docs/IDEAL-MACHINES-PLAN.md): the
  bank, the vault and its dump, water in the inventory, the quarry (your 2 m bite), the six extras, and a
  creative mode with a kit. The measurements are done; the build starts at IM-1.
- **The undo bug is fixed** (IM-0b): a machine a fall destroys can no longer come back free after
  Ctrl+Z. The build you started at 4:49 PM already has it. A Ctrl+Z right after a roof comes down now
  says *the roof came down*, as it should.
- **TajMahal is retired** as `retired-TajMahal-2026-09-29`; `start.cmd` makes a fresh one next launch,
  with a new seed.
- **The collapse sensor:** all thirteen decisions taken, the first build step is ready.
- **The Tetris collapse** is the default, with the cyan glitch, your phonk and a 10–20 s fuse. Your
  ear on the phonk is still owed.
- **F11** now goes 6 → 8 → 10 chunks out; you've played it to the top.
- **Your pond branch** re-measured: 1.9× main's water cost at your pond, only because it keeps the
  water main spills. Speed isn't a reason to retire it; your look decides.
- **Water bodies are parked.** Your quarry does rest with the near/far setting off, but every cheap
  fix with it on failed on your biggest world. What you play is unchanged.
- **Backups:** the old TajMahal is `retired-TajMahal-2026-09-29`, with `backup-TajMahal-2026-09-28`
  beside it; your tower is `backup-Regions16-pondlook-2026-09-26`. Say "restore my tower", or "bring the
  old TajMahal back" (the fresh one would be kept, dated).
- `start2.cmd` to `start5.cmd` and `pondlook.cmd` are yours and left out of git.
