# Matthew's TODO

Your file. `TODO.md` is the project's record — 6,400 lines of reasoning, mine to maintain;
this one is the short list of things only you can do, and it should never outgrow a screen or
two. Tick things off, scribble in the parking lot, delete anything you don't care about. I'll
keep it current at the end of each session and I'll add to the feedback section when something
new wants a person's eye.

**Starting a world by name.** `start.cmd --world=deepmine` — if no world of that name exists it
is created, otherwise it is opened, and the `M6:` line says which (`new world at …` or
`opened …`). **Spaces are fine, but the whole flag has to be quoted**, because otherwise the
shell hands the game four separate words:

```bash
start.cmd "--world=Deep Mine"
```

`--world=Deep Mine` without quotes gives you errors in the log and lands you in `default`.

**Every new world now gets a random landscape**, and its seed is shown in the upper right for the
first few seconds after launch, in the form you'd type back in — note it down if you like the
place. `start.cmd "--world=Deep Mine" --seed=690930704` builds that landscape again, in a world
of any name; the flag takes decimal or `0x` hex and only means anything for a world that does not
exist yet (an existing one keeps its own seed, and says so if you pass one). Measurement runs
(`--profile`, `--fall`) still get the old fixed seed, so my recorded numbers keep reproducing.
Banned characters are Windows' usual `\ / : * ? " < > |`; everything else — spaces, dashes,
apostrophes — is allowed, and the name is case-insensitive, so `Deep Mine` and `deep mine` are
one world. Avoid a trailing space or dot; Windows trims those. Worlds live in
`%APPDATA%\Godot\app_userdata\Overburden\worlds\<name>\`, one folder each, and deleting the
folder deletes the world. **A brand-new named world is also the only place `--clock=<seconds>`
works** — it sets where in the forecast the world starts, and an existing world overrides it
with what it remembers, so `start.cmd "--world=storm test" --clock=900` is how you open straight
into weather rather than waiting for it.

**How mountainous your world is, and where.** Three dials. All set when a world is created and
none changeable afterwards — an unedited chunk is rebuilt from the world's own header, so a world
that changed its relief would rebuild the landscape *around* whatever you had already dug, with
your mine hanging in it. The game says so if you pass one to a world that exists.

```bash
start.cmd "--world=Big Country" --relief=4 --reliefbase=0.3 --reliefrare=0.4
```

| flag | what it sets | low end | shipped | high end |
|---|---|---|---|---|
| `--relief=<x>` | **how tall the mountains get** | `0.5` — nowhere rises much | `3` | `6` — dramatic peaks |
| `--reliefbase=<x>` | **how flat the flat places are** | `0.1` — real plains | `0.4` | `1` — nowhere is flat |
| `--reliefrare=<0..1>` | **how much of the map is mountain** | `0` — hills almost everywhere | `0.25` | `1` — only rare summits |

**All three scales run low = less, high = more of the thing named.** Nothing is inverted.

- `--relief` and `--reliefbase` are **multiples of the same base number**, so `--reliefbase` wants
  to be the *smaller* of the two. If you pass a base above the relief the game warns once and
  builds a uniform world rather than guessing which you meant.
- The two of them are the **ceiling and the floor** of the same quantity: how far the ground
  deviates up and down. `--relief=6 --reliefbase=0.1` is "plains and mountains"; `--relief=1
  --reliefbase=1` is the old world, one lumpiness everywhere.
- `--reliefrare` only decides **how the map is shared out between those two ends**. At 0 nearly
  everywhere gets some height; at 1 the floor is nearly everywhere and the ceiling is reached only
  on rare summits. It changes nothing about how tall or how flat the two ends are.

A region — one journey from flat to mountainous and back — is about **2 km**, a few minutes of
walking. The `M6: new world` line prints what you got, as `relief 19.2..144 voxel(s), mountains
25% rare`, and the HUD's `relief` term tells you which kind of country you are standing in right
now: near the floor is a plain, near the ceiling is a highland.

**How to read a play afterwards.** The log is
`%APPDATA%\Godot\app_userdata\Overburden\logs\godot.log` (the live or last session; the
timestamped files beside it are older). Read it with `grep -a`, not plain grep — it has stray
binary in it and grep will otherwise call it a binary file and print nothing. **Godot keeps five
logs and then rotates**, so if you want a specific session kept, copy it aside before launching
again. You don't have to read any of it — paste me a screenshot and say what felt wrong, and
I'll go and find the numbers.

---

## Now

- [x] **Push** — done 2026-09-23, `ac91996..5f338fd`. Nothing outstanding.
- [ ] **M10d-2, the play** — the ten recipes below. This is the next milestone thing and it is
      entirely yours; nothing else is blocked on me.

---

## The play queue — one sentence back on each

Ten recipes. Do them in any order, over as many sessions as you like; a sentence each is
plenty, and "it was fine" is a real answer. Run `start.cmd` on `default` unless a recipe says
otherwise — flags go on the end of the `start.cmd` line.



### The quantum tunneller (2026-09-23)

- [ ] `T` to `quantum_tunneller` (between the miner and the excavator). Click: one 2 m cube
      gone, with a 2 m outline showing which. Hold Q: one every 0.25 s. Right click: fills the
      2 m cube in front of the crosshair. One sentence: does 2 m feel like the right middle?
- [ ] **The excavator still repeats on a held left button** — you wrote "one bite like the
      chunk", so if you expected a click to be one chunk, say so and it becomes one click one
      bite like the other two (one line).

### The vacuum cell's switch (2026-09-23)

- [ ] Computer in hand, right-click a vacuum cell: the panel has an `enabled` box under
      `channel`. Set it to 0 and the status line turns amber (`OFF — holding …`); pumps that
      only had that cell stall `unpowered`, and panels on the channel charge the other cells
      instead. Set it back to 1 and it picks up where it stood. One sentence: is a 0/1 box
      good enough, or do you want a real on/off button?

### Owed from the relief change (2026-09-23)

- [ ] **Does a world have somewhere flat and somewhere mountainous?** Launch a new world —
      `start.cmd "--world=Shaped"` — and travel in one direction for a few minutes. The shipped
      shape is my estimate off a sweep, not anybody's judgement. One sentence: are the flat places
      flat enough, are the mountains big enough, and do you hit one often enough? The dials to
      reach for are `--reliefbase` (lower = flatter plains), `--relief` (higher = bigger peaks)
      and `--reliefrare` (higher = rarer). All on a *new* world.
- [ ] **Press F12.** The map draws your mine as darkened ground, your machines as coloured dots
      in their own material's colour, and water where Tier A knows about it — with a rim showing
      where that knowledge stops. It updates about once a second whether or not you move. The
      rimmed shape looks like a diamond because it is a square in the world and the map is turned
      to face the way you are.
- [ ] **Press F12.** The map is in. It faces the way you do, the red tick on the rim is north,
      the arrow in the middle is you, and the caption says how much ground it covers and what
      heights its colours mean. Esc or F12 closes it.
- [ ] **Is 2 km the right width?** Add `--mapscale=4` to the launch line for a half-kilometre map
      and compare. 4 m a pixel reads as proper landscape; your 2 km is busier because the terrain's
      own hills are only four pixels across at that scale. Going *wider* than 2 km makes it worse,
      not calmer — the map starts aliasing and shows hills that are not there — so the useful range
      is roughly 4 to 16 m a pixel. One sentence on which you would rather have as the default.
- [ ] **You have a compass now.** The line above the status line reads `x 108  y 11  z 6 ·
      facing 315° NW · relief 8.1 m`. North is −Z, east is +X — so walking north makes z go *down*
      and walking east makes x go *up*. The relief number is how tall the terrain gets where you
      are standing: near 4.8 m you are on a plain, near 36 m you are in the mountains.
- [ ] **Worlds made before 2026-09-23 evening look different now.** Every world used to start at
      exactly the same relief, whatever its seed — the noise is zero at the world's origin, and
      that is where you spawn — so a few new worlds in a row genuinely were the same where you
      landed. Fixed: a seed now offsets its own origin, and of 32 seeds 30 start somewhere
      different, from a plain to hills. **Your Regions worlds will have changed shape**, because
      terrain is regenerated rather than stored. They were throwaway anyway.
- [ ] **Regions1–9 and `default` are all still on disk** in
      `%APPDATA%\Godot\app_userdata\Overburden\worlds\`. The Regions ones were built while
      settling the shape and are throwaway — delete them whenever. `default` is your old game and
      is untouched.

### Owed from the rain play (two sentences, still outstanding)

- [ ] **1. The clay pond in the rain.** Stand by your clay pond through a shower
      (`--clockscale=20` if you don't want to wait). *Does it fill at a rate that reads as rain?*
      — the number behind it is `onto the player's material` climbing on the `M8b:` line while
      `handed` doesn't.
- [ ] **2. No more rain under your roof.** Stand inside a building you've roofed while it
      rains. *Does water still appear indoors?* — should be none, and the log's rain line carries
      `skipped of which N under a roof`.

### M10d — the seven

- [ ] **3. Dry.** Put the outlets past a divide (a valley whose lake isn't the one over your
      shaft) or into a walled tank on a placed floor, and let the pumps run. *Did the mine read
      as being pumped out, and could you tell where the water went?* — `below the seed surface`
      on the `M8b:` line falls while `pumped` climbs.
- [ ] **4. The storm.** No `Alt+P` (no free charge). Wait for rain or force it with
      `--clockscale=20`. *Did the rain read as cutting the pumps — and if several were wet, did
      the ones that kept running make sense?* Watch for the first-placed pumps hogging the
      channel and starving later ones; if that's what you see, say so and I'll build the fix.
- [ ] **5. The pillar.** `--collapse=0`. `T` to the excavator (`--nogate`, or bring two packs),
      a 2 × 2 bite three chunks down, walk back under it and wait for the tint. Then stand under
      the plug's centre and build a stone column floor to roof, eight cubes. The tint should go.
      Then the same in clay. *Did the pillar read as shoring, and did clay holding the roof as
      well as stone read wrong?*
- [ ] **6. Collapse, four ways** (`--nogate`, on the excavator):
      - *Settling* — four chunk bites in a 2 × 2, 24 m down in stone; wait the minute out, or
        `--collapseafter=2,10`. *Did it read as the roof settling, or as a glitch?*
      - *Attributable* — `--collapse=0`, same room, look up. *Could you say where it would go
        before it went?*
      - *The soil patch* — one chunk bite whose top face is in the surface soil, from beside it.
        *Did dirt falling and stone holding read as two materials?*
      - *The water brought it down* — the same 2 × 2 under a pond, then again with
        `--saturation=0`. *Did the wet roof going and the dry one holding read as the lake's
        doing?*
- [ ] **7. The tiers and the hold.** Fresh pack (`P`, then right-click to equip), `Alt+P`, then
      `T` through hand → steel → quantum miner → quantum excavator, holding Q on each. *Does the
      excavator's two-chunks-a-pack read as the battery being the gate, and does the cycle's
      order read as a ladder?*
- [ ] **8. The panel by hand.** Computer in hand, right-click a pump. Use the arrows, type into
      a box, put 99 in a field capped at 15, press Esc once with the field still live. *Is one
      Esc or two what you expect?* (Either is a one-line change.)
- [ ] **9. The pack across a quit.** Quit with a pack worn, reopen. The log says `a pack
      equipped (header v4)` with its charge to the joule. Then `T` through the cycle. *Does Q
      costing charge while T doesn't read as the design?*

### The undo's edges

Ctrl+Z itself is confirmed — *"Ctrl z feels good"* — so this is just the three cases the first
press didn't reach.

- [ ] **10. Undo's three.** (a) Bite something, spend the material building, then Ctrl+Z — it
      should refuse the whole step and keep it, saying which material is missing, rather than
      half-undoing. (b) Bite into a room, let it flood, Ctrl+Z — the rock comes back and the
      water is *not* un-flooded; it says so once. (c) Three presses in a row, and a press for an
      edit in a chunk you've since walked away from and back. *Did any of the three read as the
      undo being broken rather than as a rule?*

### Smaller ones, if you're near them anyway

- [ ] **The courtyard's fourth wall.** Build the fourth wall of a walled pool on a lake's bank.
      The pool should stop being the lake's at the next boundary — the plane gone from inside
      the walls, `enclosed N kept` on the `M8b:` line. Also: does a courtyard on a slope read
      right with its uphill wall on the higher ground?
- [ ] **Clay as a lining.** Line the wettest wall of a room under the lake with clay. The `seep`
      line's face count should fall by roughly the wall's count. *And does the colour read as
      clay next to dirt?*
- [ ] **The settled world's surface.** Any long session: near the end, when you're standing
      still and the water is calm, the `M8b:` line's `last rebuild N panel(s)` should be a
      handful and `install mean` single-digit milliseconds. If most panels still rebuild every
      tick at rest, tell me — the lever is somewhere else and I know where.
- [ ] **Memory.** Same session: the `M2c:` line's `live … at the last gen2` should be flat-ish
      over an hour. A steadily climbing `live` is a real leak; a climbing working set with flat
      `live` is just garbage and fine. (One 85-minute play looked alarming and was fine.)

---

## Waiting on your word

Nothing is blocked on these; they're all one-line changes when you have a view.

- [ ] **A rock placed in a lake.** Right now you can build a pier out into a lake and it deletes
      no water. If that reads as wrong, I refuse it — one read. Your call after the play.
      (*"not sure about 3 yet"*, from M9d.)
- [ ] **The collapse delay's spread.** You said you wanted it unpredictable — *"if you have left
      too much overhang"*. It's an exponential now, so a thin lip is usually about a minute and
      occasionally three. Say "wider" or "tighter" after a session with a few falls and I'll move
      the one constant.
- [ ] **The hotbar's unit.** `1,767,903,281 L` does not fit in an 84 px slot. Litres under a
      cubic metre and m³ above is my lean; "chunks" is the other candidate. Scheduled for the
      inventory screen (M10g), your pick.
- [ ] **Dust when a roof comes down** (M9c-2). Optional, never scheduled, and only worth doing if
      the falls read as too quiet. Judged after the play above.

---

## Yours to run — I can't do these from the sandbox

- [ ] **`git push`** when the tree is ahead (see Now).
- [ ] **Delete the old world folders** if you want the space back. Under
      `%APPDATA%\Godot\app_userdata\Overburden\worlds\`, these are scratch and safe to delete:
      `edge`, `m6d2dug`, `m7c-clock`, `m7c-shaft`, `m7c-shot`, `m7d-pond`, `m7d-water`.
      **Keep** the three `retired-*` ones — those are my measurement worlds and I open them as
      copies. `default` is yours.
- [ ] **Delete the pre-rename backup**: `%APPDATA%\Godot\app_userdata\MinerCraft\` — that's the
      old folder from before the Overburden rename, superseded.
- [ ] **Rename stage 4** (whenever you like, no hurry): the GitHub repo `MinerCraft` →
      `Overburden`, the local folder, the remote URL. Stages 1–3 are done; nothing breaks while
      this waits.
- [ ] **Commit authorship.** Every commit carries `matthewcarven@gmail.com`. While the repo is
      private this is cheap to rewrite; the moment it's public or anyone clones it, that address
      is permanent public record. GitHub's `users.noreply.github.com` is the usual swap. Decide
      before it goes public, not after.

---

## Things I'd like your eye on — not scheduled, just wanted

These aren't checks against a build; they're the questions the tests can't answer. Take them one
at a time whenever you feel like it, and ignore the rest.

- [ ] **Does the mine read as the antagonist yet?** That's the whole premise — water and gravity
      as the enemy, no mobs. Nobody has asked since the rain landed. If it currently reads as a
      pleasant sandbox with weather, that's a design finding and I'd rather hear it now than
      after another five milestones.
- [ ] **Twenty minutes with nobody asking you to test anything.** Open a fresh world and just
      play. What did you reach for that isn't there? That question finds a missing verb faster
      than any recipe above.
- [ ] **Is 5 m reach still right?** It was set when a pick was the only tool. Now Q reaches as
      far as the world exists, so the short reach only binds the brush and the pick. Does that
      split feel deliberate or accidental?
- [ ] **The excavator at a chunk every 0.25 s** — does that read as a powerful tool or as the
      game being switched off? The pack is meant to be the thing that makes it feel earned.
- [ ] **Water from underneath.** Swim or wade under a surface and look up. There's an underwater
      wash and the underside of the sheet doesn't draw. Does it read as being underwater, or as a
      bug?
- [ ] **Silence.** There's no audio at all and there never has been. Is that noticeable yet, or
      is it fine until much later? (It's not on any roadmap — I'd want your word before it went
      on one.)
- [ ] **What's missing from the HUD when you're deep in a mine?** Depth, the water level above
      you, the pack, the time of day — I can add a line cheaply, but I'd be guessing which.
- [ ] **The pumps as a system.** Once you've had sixteen of them running: does placing and
      channelling them read as building an operation, or as chores? That's the question that
      decides how much of the ladder (M10e onward) is worth building.

---

## Notes to myself (fill in freely, I'll fold them into the plans)

*Anything you notice mid-play and don't want to lose. Half a sentence is fine — I'll chase down
what it means.*

-
-
-

---

## Status, kept current for you

Updated **2026-09-23**.

- Suite **1,735** green. The r6 streaming control reads to the digit. No errors in any of
  today's four play logs.
- `start.cmd` rebuilds the game before launching, so what you play is always the current tree.
- Built and unplayed: the M10d instruments (the mine's own water is now a number on the `M8b:`
  line), and the undo.
- Next on my side, after your play: **d-3**, the water solve's cost measured on the retired
  spread world — that one needs the game *closed*, so it waits for a session where you're not
  playing.
