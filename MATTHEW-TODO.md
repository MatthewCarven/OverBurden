# Matthew's TODO

> **This file is public**: the repo is, so anyone can read it, "Notes to myself" included. Write nothing here you
> wouldn't post (ALPHA-PLAN D12, 2026-10-26).

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

**Still to test, queued at your word** (2026-10-19, and 2026-11-24: *"Keep my testing backlog open for updates
and i will get around to testing things"*). **Nothing here blocks development**, which carries on alongside; tell
me in the chat whenever you've tried something, and I'll fold it in.

**Your worlds open in the new save format only, from your next `start.cmd`** (2026-11-26, creative mode's step). The
first open on this build rewrites a world's header at version 9, and 0.1.3 won't open it after. Your two test worlds
were checked on copies first: both open as they were, row and machines included. **Backups, taken before the change**,
sit beside them in `%APPDATA%\Godot\app_userdata\Overburden\worlds\`: `backup-LakeTest2-2026-11-26` and
`backup-LakeTest3-2026-11-26`. A world's name is its folder, so a backup is restored by moving it back with the game
closed.

### The vault, your first play of it (about 15 minutes, `start.cmd`, LakeTest2)

New on 2026-11-25: the quantum vault in your own world, its bank on the HUD, the siphon, and the dump. **This is
the step's real test**; nothing else checks how it looks or feels. A line back on any step is plenty.

1. **P.** The line above the hotbar says a hundred of each, and that the crates and vaults are a V away. Your row
   doesn't change: the vaults, the crate and the backpack take a slot only when you pin one.
2. **Wear the quantum backpack**: V to it, right-click. Over the status line a new line reads `BANK 0 mJ ch 0 · +0 W
   sun · −0 W`, where the pack line used to read `0 mJ / 0 mJ`. A small *store / water* box sits right of slot 9.
3. **A vacuum cell and Alt+P**: place a cell, Alt+P. The bank reads 5,000.0 MJ.
4. **A vault**: V to *quantum vault (high)*, place it near you. The bank line gains `store 0%`. *Do the five grades'
   cyans, from dark to bright, and the crate's brown read as what they are?*
5. **Its panel**: computer in hand, right-click the vault. The top shows a bar, `bill 0 W on channel 0 · own …`,
   and `HOLDING`; then a row for each block you carry, with `→ pocket` and `→ vault`; a water row with no buttons;
   your warranty line at the foot. The channel box says *locked — click to unlock*.
6. **Fill it**: click `→ vault` on stone (a cubic metre a click), then Shift-click for all of it. The bar, the bill
   and the HUD's `hold` and `store` rise; `→ pocket` brings it back. *Does the panel read right, and is a cubic metre
   a click the right step?*
7. **The whole store**: right-click with the computer on nothing. The backpack's panel lists the store on channel 0,
   with `→ store`.
8. **The siphon**: T past the three quantum tools to the siphons (stops 5–7). The status line says SIPHON, and the
   water box lights cyan. Aim at water (or press R for 10,000 L first) and left-click: the box fills and the bank
   drops 1 MJ a cubic metre. Right-click pours it back, free; Q held keeps taking.
9. **The dump**: open the vault's panel, click the padlock, and set its channel to 9, where nothing pays. It spends its
   own minute, then the top of the screen reads `VAULT LOSING CONTAINMENT — 14 s` and its body glitches cyan. At zero
   its water floods out and your stone comes back as a pile round it, each block flashing cyan for a second
   (underground, once all the water is out). *Does it read as losing containment? Is the pile the right look?*
10. **(If you like) a fuse that pauses**: as 9 with a second vault, but set it back to 0 while the fuse burns. The line
    says `(paused)` and the glitch holds still, then it goes out once the bank refills its minute.

When this route was written no format had changed. **Since 2026-11-26 (creative) your next `start.cmd` moves
LakeTest2 to the new save format**, which 0.1.3 can't open; the backups are named above.

### Creative, your first world (about 10 minutes, a new world's name)

New on 2026-11-26: creative mode. A creative world starts with a kit, the quantum backpack on your back and 1 GJ in
the first power cell you place, and has the test keys off. **This is its real test**: nothing headless draws the card,
the banner or the HUD, or presses a key. Any name that doesn't exist yet:

```powershell
.\start.cmd "--world=CreativeTest" --mode=creative
```

1. **The card opens by itself**, headed *"You run a mine. Water wants in; the rock wants down. Everything you own
   runs off one bank. Don't go broke."*, with a note of the kit under it. It lists no P, R or H. The seed in the upper
   right reads `CreativeTest — creative`. *Does the card read right at the start?*
2. **Esc closes it.** The hotbar holds stone, clay, glass, a vacuum cell, a solar panel, a pump, an outlet, the high
   vault and a crate; V steps through the other four vaults and the computer. Over the status line, `BANK 0 mJ ch 0`:
   you're wearing the quantum backpack.
3. **Press P** (and R, H, L if you like). Each says once, above the hotbar, that it's a test key and off in a creative
   world, and does nothing.
4. **Place the vacuum cell.** A line says it arrived holding 1,000.0 MJ, and the bank line reads 1,000.0 MJ. Only the
   first one: a second cell arrives empty. (Ctrl+Z straight after placing that first cell takes it back with its
   1 GJ, and the gift is gone for good. That's the price we agreed.)
5. **Build something**: panels in the open, the high vault on channel 0, stone into it from its panel (`→ vault`). The
   bill shows on the bank line as `hold`; in sun the panels pay it. T to the quantum miner and dig: the bank drops
   50 J a block. *Is 4,096 m³ of stone, clay and glass the right size of kit? Is 1 GJ the right start?*
6. **Quit and reopen** with `.\start.cmd "--world=CreativeTest"`: no second kit, no card, the bank where you left it.

### The retest, in one sitting (about 20 minutes, `start.cmd`, LakeTest2)

It has been a month, so this strings every untested build into one route. A line back on any step is plenty, and
a step that reads wrong is the most useful kind. **★ marks what IM-6 builds on**: do those first if you only do some.

1. **Ctrl+P** with no lithium-ion pack on your back: the pack lands in your hand and a line above the hotbar says
   so. Right-click wears it. Ctrl+P again says you're wearing one and adds nothing. (Built 2026-11-04.) **Your
   logs already show it working**: across your five plays of 5 and 9 November it added a pack 3 times and
   refused 11 times while you wore one, with no errors. Only the feel is left: *did the HUD line read right?*
2. **Alt+P** charges it, and the line above the hotbar says so (it should for P, R and Ctrl+Z too).
3. ★ **Your row is as you left it.** Hold V and press a number: what's in your hand goes in that slot. A tap of V
   still steps through what you carry, but moves when you let go now. *Does the V chord feel right?*
4. ★ **Spend the last of something**: T to the quantum miner, dig some stone, then place it until it runs out.
   Your hand stays on stone and a click says you're out, instead of switching to whatever you have most of; the
   slot stays, dimmed, and the next bite lands back in it. Over a cubic metre reads in m³, not litres.
5. **The swap asks**: `P` for spare packs, then with the charged one on, right-click an empty one. It should say
   you're wearing your fullest and do nothing; a second right-click within 3 s swaps.
6. **The pack's recharge**: place a vacuum cell, `Alt+P`, drain the pack with Q, and watch it come back from the
   cell, a cube bite's worth every 1.6 s.
7. ★ **The panel by hand**: computer in hand, right-click a pump, type 99 into a box capped at 15, press Esc once
   with the field live. *One Esc or two?* (The new `onsignal` box is the collapse sensor's; leave it at 0 until
   the sensor itself is in the game.)
8. **Ctrl+Z on a fill that's still waiting**: place a block right into water and press Ctrl+Z at once. *Nothing
   should land* (before, it came back half a second later). A shape half in water: the first press cancels the
   wet half, a second takes back the dry half.
9. **H** shows an amber *WATER HELD* line until H again (`start.cmd` passes `--dev`).
10. **The screen**: F11 and Alt+Enter go fullscreen and back, F10 is far mode, and the version reads small in the
    upper-left corner.
11. **Esc** opens the menu (Resume, Controls, Quit) and **F1** the controls card; Esc closes a panel or the map
    first. *Is the card's text the right size?* Quit says *Saving…* and closes.

**Then a new world, five minutes** (`start.cmd "--world=HandTest"`):
- It starts on the hand pick: dirt digs, and stone is refused with a line saying it wants a quantum tool. *Does
  that read as a start, or as broken?*
- ★ `P` fills the row with the machines, clay and glass, your first stone lands in slot 9, and the computer and
  packs are a V away.
- If you bring a roof down anywhere, listen to your Korobeiniki through the fuse. *Does it fit; too loud or
  too quiet?*

### Also open

- [ ] **The quick start**, [QUICKSTART.md](QUICKSTART.md): every key, written for a stranger. Have a read and
      say what's wrong or missing; it becomes the in-game controls card and the zip's readme.
- [ ] **M10d-2, the play** — the recipes below.

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
- [ ] **8. The panel by hand** is step 7 of the retest, above.
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

- [ ] **Delete `C:\c\`**: a stray folder holding two empty log files, made by a path I got wrong on 2026-11-25.
      Nothing else is in it; the safety check stopped me removing a top-level folder myself.
- [ ] **The repo's description** on GitHub (the gear beside "About"): it reads *"Its voxel world based mining
      game…"*, which wants an apostrophe: "It's".
- [ ] **0.1.3's release notes, if you like**: GitHub wrote them itself, listing your two September pull requests
      and "@MatthewCarven made their first contribution". "Edit" on the release replaces them, e.g. with a line on
      what's new (the hotbar's slots, the Esc menu, F1's card). The zip's SHA-256 already shows beside it.
- [ ] **Delete the scratch worlds** if you want the space back (under
      `%APPDATA%\Godot\app_userdata\Overburden\worlds\`): `edge`, `m6d2dug`, `m7c-clock`,
      `m7c-shaft`, `m7c-shot`, `m7d-pond`, `m7d-water`, and `Regions1`–`Regions9`. **Keep** the
      three `retired-*` worlds and `Regions16-pondlook-demolished-2026-09-26` — those are my
      measurement worlds.
- [ ] **Delete the pre-rename backup**: `%APPDATA%\Godot\app_userdata\MinerCraft\`.
- [ ] **Rename stage 4**, no hurry. The GitHub repo (`OverBurden`, public) and the remote URL are done. Left:
      the local folder (tell me before you move it: my memory folder is keyed to its name, and I'll carry it
      across).

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

- **A vault's blocks in a wet mine** (2026-11-25): *"As recommended (c)"*, *"please Claude and thanks"*, then *"As
  recommended (A)"* and *"As recommended (A2)"*. All built the same day: underground, a vault's blocks wait until
  all of its water is out, then land in it and push it up.
- **IM-6's twenty-six decisions** (2026-11-24): *"As recommended"*. So the vault comes in three steps: the insides,
  then the vault in your own LakeTest2 (the bank on the HUD, the stock panel, the siphon on T, the cyan glitch), then
  creative mode. P will give the backpack, a crate and the five vaults, pinned only. A vault's channel box opens
  locked. Water shows as a gauge beside the hotbar. A creative world's first cell gets 1 GJ once. The test keys
  are off in creative even under `--dev`.

- **Pushing is mine now** (2026-11-03): *"Push commits as and when you like"*. The README's Releases link went up on
  2026-11-04 (`651a188`) with your YouTube link merged in. Force-pushing and rewriting pushed history stay yours.

- **Pushed and released** (2026-10-26): *"release generated and pushed"*. GitHub's main is the same commit as
  here, all ten of going public's files match, GitHub recognises CONTRIBUTING and the pull request template, and
  the `v0.1.3-alpha` pre-release carries the zip with the SHA-256 it was built with (`41af625f…4966cdd`).

- **Going public, fifteen decisions** (2026-10-26): *"Oh i ain't so worried about people learning what I put in the
  public spotlight like"*, then *"and as recommended please Claude"*. So: a README rewritten for visitors, with both
  your machines by part (no names); zips on GitHub's Releases page from 0.1.3, your Drive links kept as 0.1.1 and
  0.1.2; CONTRIBUTING with your rule; Harmony's standard contributor agreement, so you can relicense a stranger's
  work later (not legal advice); your hours, email and files left as they are, and only the worklog's one mention of
  your family reworded. 0.1.3 goes up as built. **The agreement's law is New South Wales'** (*"Australian"*, then
  *"NSW"*): US law could be named, but a US court can refuse it with no link to a US state, and it buys you nothing
  there. **Built the same day** (status, below); the push and the release are yours ("Yours to run").

- **The hotbar's slots, thirteen decisions** (2026-10-19): *"As recommended please Claude"*. Nine slots that keep
  what's in them, even at zero (dimmed); a new block goes in the first empty slot (the computer and packs by a
  pin); hold V and press 1–9 to pin what's in your hand; your hand stays on a block that runs out. Your worlds
  keep the row they show today. Built the same night as 0.1.3 (its check is at the top of Now); a world opened in
  0.1.3 won't open in 0.1.2 again.
  Your same-evening designs (the store as a box on E's screen, a cloth backpack, the player's slots, two charge
  slots) are in the ladder plan for M10g and change nothing in C.

- **Your tests, queued** (2026-10-19): *"I know i've still got stuff to test can we put that on my todo and keep
  going?"* They're at the top. Keeping going turned up session B already built in a side folder on 10-13 and never
  saved into the project; it's checked, finished and on main now, and zipped as 0.1.2.

- **Session B's thirteen decisions** (2026-10-13): *"as recommended please Claude, yes add --dev to mine too"*.
  Your `start2`–`start5` carry `--dev`. On a fresh world starting with a charged pack you added *"i was going to
  make you mine lead and get acid somehow"*: that's the ladder's tier 2 already (the furnace's sulphur and water
  make acid; lead-acid is lead, acid and a glass jar).

- **0.1.1 is on Drive** (2026-10-12): you renamed the old upload with " - old" and gave 0.1.1 the 0.1.0 file
  name so Drive's replace kept the README's link. The file says 0.1.0, but the game's corner and the folder
  inside say 0.1.1, and that's what a report will quote. All pushed.

- **The alpha zip, on another machine** (2026-10-12): *"copied the zip over to my desktop and ran it there and
  it runs fine, well my desktop is a bit older tech like but its working without errors"*. The first time
  Overburden has run anywhere but your dev machine. Rebuild it any time with `pwsh tools/make-alpha.ps1` (about
  20 seconds, the game closed).

- **The collapse music** (2026-10-12): your own `Korobeiniki.mp3`, the folk melody doubled an octave down,
  built on the melody-only MIDI. The game plays it now, and it's the track the zip carries, credited
  *"Korobeiniki (Russian folk song, public domain), arranged by Matthew Carven"*. Your two downloads stay
  out: `35149.mid` is Jim Paterson's Greensleeves (mfiles.co.uk forbids sharing it, even adapted, without
  their licence), and `100444.mid` has no name on it. A fuller beat can replace it any time; same file name.

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
  pile tumbling — 65% of what fell fell again. There is no sound in the game yet; that was another device in the room.
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

Updated **2026-11-26**. **0.1.3-alpha is released on GitHub** (a pre-release, the zip byte-identical to the one
built), going public's files and the README's Releases link are pushed, and **Ctrl+P** (one lithium-ion pack, in
your hand) is on main for your next `start.cmd`. **The vault is in your game** (IM-6's second step) and **so is
creative mode** (its third, built today): IM-6 is built. Their routes are at the top of Now, the vault first. **Your
worlds move to the new save format at your next `start.cmd`**; the backups are named there.

- **IM-6's third step, creative mode, is built** (2026-11-26):
  - **A creative world**: `start.cmd "--world=<a new name>" --mode=creative`. It starts with a kit in your pocket (two
    cells, eight panels, four pumps and outlets, one vault of each grade, a crate, the computer, and 4,096 m³ each of
    stone, clay and glass, the row set for you), the quantum backpack on your back on channel 0, and **1 GJ in the
    first power cell you place**, once.
  - **The test keys are off there** (P, Ctrl+P, Alt+P, R, Ctrl+R, Alt+R, Alt+G, H, Alt+H, L), whatever `--dev` says,
    and each says so once. Your sandbox worlds keep every key.
  - **The card** opens by itself at a creative world's first open, headed by your line, *"You run a mine…"*.
  - **The kit is a file** (`mods/overburden/kits/creative.json`), loaded as a mod's would be, so a mod can add to it.
  - **The save format moved to version 9** (a world's mode and two once-only flags). Every world crosses at its first
    open on this build; LakeTest2 and LakeTest3 were checked on copies and backed up first.
  - Ten small choices the build made are in the plan for your word; the first is that the kit's row pins the high
    vault and the crate, which P keeps off your row.
  - Checked: all 2,641 tests pass; I broke the new code 49 deliberate ways and 48 were caught (the 49th is
    held by an earlier check, so it can't slip), plus three breakages only the running game shows; a creative
    world's vault let go in a storm and held in sun, with every number exact; and the game's quick check reads
    exactly as recorded. **How it looks and feels is yours to judge**: nothing I can run draws the card or the HUD.

- **IM-6's second step, the vault in your own world, is built** (2026-11-25). In a sandbox world, through P and Alt+P:
  - **P** gives the quantum backpack, a crate and the five vaults (low, medium, high, mythical, god), a hundred each,
    a V away, so your row and slot 9 stay as they are.
  - **The bank on the HUD**: wear the backpack and a line reads `BANK … ch 0 · +… sun · −… · runway … · hold … ·
    store …%`, the runway amber under ten minutes and red under one. A small box right of slot 9 shows the store's
    water, lit while a siphon is in hand.
  - **A vault's panel**: a bar, the bill in watts with its own minute, its state, a row for each block with
    `→ pocket` and `→ vault` (Shift for all), your warranty line at the foot, and the channel box behind a padlock.
    The backpack's panel shows the whole store.
  - **The siphon**: three, after the quantum tools on T, in their sizes: left takes water into the store at
    1,000 J a litre, right pours it back free.
  - **The dump**: a vault whose bill goes unpaid spends its minute, then `VAULT LOSING CONTAINMENT — 14 s` and its
    body glitches cyan; at zero its blocks pile back, flashing cyan, and its water floods out.
  - Nothing in your saves changed: 0.1.3 still opens a world you play this in.
  - Ten small choices the build made are in the plan, for your word whenever you look; the first is that a right
    click with the computer in hand still opens a panel when a siphon is on T.
  - Checked: all 2,558 tests pass, I broke the new code 57 deliberate ways and every one was caught, plus eight
    breakages only the running game shows, and the game's quick check reads exactly as recorded. **How it looks is
    yours to judge**: nothing I can run draws the HUD or the panel.
  - **The windowed run you said yes to is done** (2026-11-25): a vault letting go in a copy of LakeTest3's big pit.
    The water it dumped is cheap to draw (well under the bar I set), so nothing needs changing there. Its blocks
    never came back, because they waited for dry ground. **Your answers are built**: underground, the blocks wait
    until all of the vault's water is out, then land in it and push it up, as a collapsing piece does. In a copy of your
    LakeTest3 pit all 500 m³ of water came out first, then all 32,000 blocks landed in it, about four minutes after the
    fuse, with nothing lost or stuck. (A side note: a vault left buried under its own pile can't empty again until you
    dig its top clear.)

- **IM-6's first step, the insides, is built** (2026-11-24). Nothing you play changes. It's the parts the next step
  puts on screen:
  - **The runway.** How long a channel's cells last at the rate they're going. It's worked out from what comes in
    (the sun) and what goes out (vault bills, pumps, your tools and the siphon). It is never worked out from the cells
    going up and down, so Alt+P or picking up a cell won't make it jump. It looks at the last ten seconds, and turns
    amber under ten minutes and red under one.
  - **Power in watts**, ready for the HUD and the panels: `3.8 kW`, `104 kW`.
  - **Moving blocks between your pocket, the store and a vault or crate**, ready for the vault's panel and, later,
    the inventory screen. A closed vault says why: switched off, dumping, or a wreck.
  - **Where the dump's heap put each block**, so the cyan glitch can appear on it.
  - Ten small choices the build made are listed in the plan, for your word whenever you look. None changes anything
    you'd see yet.
  - Checked: all 2,475 tests pass, I broke the new code 61 deliberate ways and every one was caught, and the game's
    quick check reads exactly as recorded.

- **Two things landed on 2026-11-24, neither one yours to test**:
  - **The ten warnings `start.cmd` printed are gone**: zero now, from every build. All were real fixes and none
    was hidden, so a new warning will stand out.
  - **The collapse sensor's first step** (f-1): the game's insides for a sensor that warns of a collapse 10–20 s
    before it falls, and switches pumps and vacuum cells on its channel. **Nothing senses yet**: the sensor
    itself, its texture and `P` are the next step. What you'll notice is a new `onsignal` box on a pump's and a
    cell's panel. Leave it at 0: with no sensor, 2 just holds the machine off, and the panel says so. The rule
    your vaults will need is in and tested: a cell switched off by a sensor still pays its vaults' bills, so a
    sensor can never make a vault dump.

- **Going public is built** (2026-10-26, the last of the four alpha sessions):
  - **The README** is written for a visitor: your pitch, the download (GitHub's Releases, your Drive links as the
    older builds), both machines, how to build it, how it's made, your licence paragraph.
  - **CONTRIBUTING.md** opens with your rule in your words, then four checks, and a pull request has a template
    with those checks and the agreement's tick box.
  - **CLA.md** is Harmony's contributor agreement, filled in under New South Wales law: a contributor keeps their
    copyright and lets you license their work any way you like, commercially too, so long as it also stays under
    the licence it came in under. A template, not legal advice.
  - **Your files**: the worklog's one family line is reworded, this file says at the top that it's public, and from
    now on worklog headings carry the date only. Nothing else about you changed, and the history is untouched.
  - **The zip script** now refuses a zip whose notices name a different .NET than the one it ships, which is how
    0.1.3's came to say 8.0.31 when it ships 8.0.32 (the licence texts are identical). I broke it on purpose once
    to see it refuse, in a copy, so the 0.1.3 you released is the zip as built. The fixed notices, P in the quick
    start and the menu's three items ride the next zip.
  - Nothing you play changes. No one else has opened a pull request or forked the repo yet.

- **0.1.3-alpha is built, session C** (2026-10-19): the hotbar's nine slots stay put and save with your world.
  New blocks and machines take the first empty slot (the computer and packs only when you pin them), a slot
  you've used up stays dimmed, **hold V and press a number to pin what's in your hand**, and your hand stays on
  a block that runs out. Your LakeTest2 opens with exactly the row it shows today (checked on a copy). All
  2,335 tests pass, I broke the new rules 24 deliberate ways (every one caught), and the zip ran clean from a
  fresh folder. **Your look, then the upload; 0.1.3 carries 0.1.2's changes too.**
- **0.1.2-alpha is built, session B** (2026-10-19): Esc's menu (Resume, Controls, Quit), the controls card on
  F1 (and once at a new world's first open, in the download only), Ctrl+R's million litres, H and Alt+H behind
  `--dev`, a message line above the hotbar, and the pack swap that asks before wearing an emptier pack. It was
  built on 10-13 and never saved into the project; today I read it through, fixed one missing space, found and
  closed a gap in its test, and broke the swap rule 9 deliberate ways (every one caught). All 2,305 tests pass,
  the quick check reads exactly as recorded, and the zip ran clean from a fresh folder. **Your look first, then
  the upload** (both on your list).
- **The alpha is public**: the repo is `OverBurden`, public, and 0.1.1 is on your Drive with its readme,
  credits and licence texts. It ran on your older desktop. **For each release from 0.1.3 on:** bump the version
  in `Overburden.Game\Overburden.Game.csproj`, run `pwsh tools/make-alpha.ps1` with the game closed (about 20
  seconds), and make a GitHub release of it as you did 0.1.3:
  `gh release create v<version> build/Overburden-<version>-win64.zip --prerelease`. The README links to the
  Releases page, where the newest is at the top.
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
- **IM-6's plan is taken** (2026-11-24, *"As recommended"*), the first thing you can play with a vault. It comes in
  three steps, with the vault in your own world before creative mode. The first, the insides, is built (above), and
  the vault in your world is next.
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
  playtest fresh. Your new test world is **`LakeTest2`** (seed `0xD8FF0AD`, as its world file reads), and `start.cmd` opens it. The old
  one stays because the lake-split numbers were measured on it. TajMahal2 is `retired-TajMahal2-2026-10-01`, and
  the first TajMahal `retired-TajMahal-2026-09-29`.
- **The collapse sensor:** its first step is built (2026-11-24); the wire, with the sensor you can place, is next.
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
