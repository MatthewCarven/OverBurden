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
- [ ] **F11, far mode.** The view climbs toward 8 chunks out and 4 up/down while the loader is
      idle; F11 again drops back to 6h/2v at once. About 50 MiB more and twice the drawing. *Worth
      it, and does turning it off feel instant?*
- [ ] **The vacuum cell's switch.** Computer in hand, right-click a cell: set `enabled` to 0 and
      the status goes amber (`OFF — holding …`), pumps that only had that cell stall. *Is a 0/1
      box good enough, or do you want a real button?*
- [ ] **Glass, the two left.** Does a floor under a glass roof gleaming in a storm bother you, and
      does water seen through glass ever draw in the wrong order?

### Owed from the rain play

- [ ] **1. The clay pond in the rain** (`--clockscale=20` if you don't want to wait). *Does it fill
      at a rate that reads as rain?*
- [ ] **2. No rain under your roof.** Stand inside a roofed building while it rains. *Does water
      still appear indoors?*

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

- [ ] **TNT** — [docs/TNT-NOTES.md](docs/TNT-NOTES.md), six decisions still open. The biggest: a
      blast destroys the machines it reaches (5). "As recommended" or any one back.
- [ ] **A rock placed in a lake.** A pier into a lake deletes no water today. If that reads wrong,
      I refuse it. (*"not sure about 3 yet"*, from M9d.)
- [ ] **The collapse delay's spread.** A thin lip goes in about a minute, occasionally three. Say
      "wider" or "tighter" after a session with a few falls.
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

Updated **2026-09-26, late night**. Everything is committed; `main` and `water-column-test` both
match GitHub.

- **The Tetris collapse:** planned, all fourteen decisions taken, and part a built in the engine's
  core — **not in the game yet**, so nothing to see. Part b puts it in the game and starts with a
  look at the game code. Suite **1,937** green.
- **The pond branch** waits on your verdict (Now, above).
- **Your next chat:** the ideal machines, endgame tech first, for a creative-mode demo. The
  collapse sensor waits on it.
- **The docs were consolidated** the same night: `CLAUDE.md` is short again, the flag reference is
  `docs/COMMANDS.md`, and the old session hand-overs are `docs/STATUS-HISTORY.md` — moved, nothing
  deleted.
- `start2.cmd` to `start5.cmd` are yours and left out of git.
