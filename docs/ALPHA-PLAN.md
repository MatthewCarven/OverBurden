# The public alpha — the planning pass (2026-10-12)

> Matthew, 2026-10-12: *"I'm thinking of releasing an alpha"* … *"public, like i was thinking of going public
> zipping all the resources into one zip so you can just extract and run it like, i was going to put in the
> readme that its far from complete, although i have enjoyed the alpha/beta testing and thought "you" might
> like a go too"*. And on forks: *"if you have to modify core do it in a way that everyone else can take
> advantage of it too"*. The same afternoon he retired the steel pick for it (built, WORKLOG) and will source
> royalty-free music.

Three read-only searches ran in parallel against `2f435f3`: export readiness, a stranger's first ten minutes,
and licences and provenance. The load-bearing facts were checked by hand. The searches disagreed once (on
seeds), and the code settled it: fact 9.

**Taken 2026-10-12, as recommended, with his changes** (*"AS recommended swap f11 (our view distance key) for
another f key that is available please"*):
- **Decision 17 takes its other arm: F11 is fullscreen (Alt+Enter too) and far mode moved to F10.** Built the
  same day; F1 stays free for the controls card.
- **Decision 3 drops the email rewrite**: *"i dont care if they get my email like they only get filtered"*. The
  rename and the docs read stay.
- **Decision 8's music exists**: he deleted the phonk and added `media/Almosttetris.mp3`; the code's default
  points at it (built). **Its provenance, his words:** *"came from a midi file of the approximate song, like
  there are some differences from what i heard and run through fruity loops studio with a marimba as the
  instrument type"*. So the recording is his and the instrument is FL Studio's stock marimba; Korobeiniki's
  melody is public domain. **The open piece is the MIDI**: a bare folk melody is clear, but a transcription
  of the Game Boy arrangement carries someone else's bass and harmony. Recommended: keep the melody, write his
  own accompaniment, credit *"Korobeiniki (Russian folk song, public domain), arranged by Matthew Carven"*,
  and ship it without "tetris" in its file name. **Renamed `media/korobeiniki-marimba.mp3`** on his word
  (*"renames as you please"*), with the code's default following. **Then the melody alone**: he chose to
  write his own bass (*"i can dream up a bass line i believe"*), so `media/korobeiniki-melody.mid` holds the
  folk tune's melody only, written out the same day (bars 5 and 7 in the plain folk rhythm, not the Game Boy
  version's). The two MIDIs he downloaded first can't ship: one is Jim Paterson's Greensleeves arrangement
  from mfiles.co.uk, whose terms forbid redistribution "as is or adapted" without their licence; the other
  carries no name. **Settled the same night: `media/Korobeiniki.mp3`**, his arrangement on that melody (*"i
  just copied the melody like and made it lower on the notes"*: the melody doubled an octave down), is the
  game's default and the track the zip carries. He removed the earlier render. The credit line above holds. Worst case if anyone objects: the track comes out, and the
  game runs silent without it (fact 3).
- **A quick start describing every key**, his ask: `QUICKSTART.md` at the root, written for a player against
  today's build. It is the source for the controls card (15) and the zip's README (22).

## Facts

**The export**
1. **There is no export.** No `export_presets.cfg` exists, although `.gitignore:11` says one "is committed",
   and `%APPDATA%\Godot\export_templates\` is empty: the 4.7.1 mono templates are not installed.
2. **Content is found relative to the working directory, not the exe.** `Main.cs:10159` passes
   `ProjectSettings.GlobalizePath("res://mods")` to `DirectoryContentSource` (System.IO). In an export
   `res://` is inside the .pck, and (by the engine's source, not checked here) the call returns the bare
   relative `mods`. A double-click usually starts in the exe's folder, so `mods\` beside the exe would be
   found, but a shortcut with another "Start in" would not. **If content is missing the window just closes**:
   no definition for `overburden:air`, `LoadContent` fails, `Main.cs:1499` quits with code 1.
3. **The music is looked for one folder above the working directory** (`Main.cs:6394-6396`,
   `GlobalizePath("res://")` + `..`). Missing, the game is silent and says so in the log (`Main.cs:6399`).
4. **SQLite in an export is unverified** (`TODO.md`'s owed check). `Microsoft.Data.Sqlite` 10.0.11 brings
   SQLitePCLRaw 2.1.12 and its native `e_sqlite3.dll`, found today through the build's `.deps.json`. A failed
   load throws in `SqliteWorldStore`, is caught at `Main.cs:9855`, and quits.
5. **An export would share his user folder.** `project.godot` sets no custom user dir, so an export run on
   his machine opens `%APPDATA%\Godot\app_userdata\Overburden\`: his `default` world, and his log rotation.
6. **A launch with no flags** opens world `default` with saving on and a 30 s autosave, collapse and music on,
   and `start.cmd`'s four flags at their measurement defaults (light budget 4 ms, adaptive off, fluidnear 0,
   fluidjob off). The flip to `start.cmd`'s values was taken on 2026-10-01 for *"the first build someone else
   plays"* (TODO, Later, with the reasons the r6 control and the recipes need the old arms).
7. **No version string anywhere**: no assembly version, no window title, nothing on screen or in the log.
8. **Forward+ only.** It needs a Vulkan- or D3D12-capable GPU; no Compatibility fallback is configured, and
   the shaders were written for Forward+. *(The zip is Vulkan only: session D's fact 14.)*
9. **New worlds get a random seed** (`Main.cs:9764-9766`, `RandomWorldSeed`); only a profile, or `--seed=`,
   uses a fixed one.

**A stranger's first ten minutes**
10. **No menu of any kind**: no main menu, pause menu, settings, world picker or help. The exe drops straight
    into `default`. **There is no quit key**: Esc releases the mouse, then the window's X or Alt+F4.
    Quitting saves (`_ExitTree`).
11. **The only controls list is a console print, and it is stale** (`FlyCam.cs:125-139`: "Q toggles the
    entangled miner… 40 swings/s"). `README.md` is stale too ("1 and 2 pick a tool tier"; status "M6 under
    way — 734 tests"). Undo results, "too hard", an empty pack and the tool descriptions go to the console only.
12. **Settings: none.** Godot's default 1152 × 648 window, vsync on, no fullscreen toggle, mouse sensitivity
    0.0025 hard-coded, FOV 75, no rebinding, music only by flag. **F11 is far mode, the key strangers press
    for fullscreen**, and without `--adaptive=1` it jumps the view box to 2,601 or 5,733 chunks at once.
    **F12 is Steam's screenshot key.**
13. **Debug binds a stranger can trip:** Ctrl+R pours 1 ML (Ctrl is also sprint, so sprinting and tapping R
    does it; no undo, the water is saved); H freezes the water with no sign on the HUD, so wet placements
    wait for ever; Alt+H forces a blocking full GC; L places a free lamp; P and Alt+P. Alt is also "one
    voxel", so Alt with H, P or R does the other thing.
14. **The starting kit:** an empty pocket and, since today, the hand pick (dirt, grass, clay, glass, rubble;
    not stone). Stone, and a pump's, outlet's or cell's body, take a quantum tool and a charged pack. The
    status line sends a newcomer to P (`Hud.cs:566`), whose packs arrive empty, and only Alt+P charges one.
15. **The hotbar overflows.** It is the first nine holdings in ID order (`Hud.Slots`); `P`'s nine items plus
    mined dirt, grass and stone push the newest past nine, reachable only by V. The fix is planned
    (M10-LADDER-PLAN, "The inventory screen — folded into M10g"). *(Half right: session C's fact 5.)*
16. **A same-kind pack swap hands you an emptier pack** (his play today; TODO).

**Licences, provenance, privacy**
17. **LICENSE.md is PolyForm Noncommercial 1.0.0**: free noncommercial use and redistribution, each copy
    carrying the licence and its `Required Notice: Copyright 2026 Matthew Carven` line. Source-available, not
    open source.
18. **No third-party notices exist.** A zip would carry Godot (MIT, with the engine's bundled-library
    notices), GodotSharp and the .NET runtime, Microsoft.Data.Sqlite (MIT), SQLitePCLRaw (Apache-2.0, which
    requires its licence text) and SQLite (public domain). TODO's "Before the repo goes public" already owes a
    Godot credit "with the first distributed build".
19. **The phonk has no recorded source or licence** (`media/TETRIS_PHONK.mp3`, 2.5 MB, gitignored;
    COLLAPSE-PLAN calls it "someone's recording" and "a licence question the day the game ships"). He will
    source royalty-free music. Korobeiniki itself is public domain; "Tetris" is a trademark.
20. **Personal data, if the repo goes public:** `matthewcarven@gmail.com` is author and committer on 538
    commits (TODO-HISTORY's open item: rewrite with `filter-repo --mail-map` first). The docs carry his
    Windows paths (`C:\Users\Admin\...`, in the panel's briefs and `C:/tmp` recipes), personal detail in the
    worklog, and `MATTHEW-TODO.md`, his own list. No secrets were found. The repo and folder are still named
    `MinerCraft`, close to another game's name.
21. **Forks need a public repo**, and under today's licence an outside contribution arrives on its author's
    terms. The licence can be loosened later and never tightened once an outside PR is merged (CLAUDE.md).

## Decisions

Each with a recommendation. "As recommended" takes them all; any one can come back.

### The shape

1. **What ships: the game as it stands, with `P` as its kit.** The water, the collapse, the quantum tools,
   pumps, glass and the lithium-ion pack are built and played; the vault (IM-6) need not wait. `P` and Alt+P
   are documented as the alpha's give-everything keys. *Note:* the ideal-machines plan says `P` must not
   become creative mode's source; creative's own kit replaces it later, so the README says P is temporary.
2. **Where: a public GitHub repo with the zip on its Releases page.** Forks need the public repo anyway, and
   Issues are the bug channel. itch.io can come later as a storefront. *Other arm:* itch.io only, with the
   repo private (no forks).
3. **The repo goes public with its history, after three clean-ups (his to run):** ~~the commit emails
   rewritten to his GitHub noreply address~~ (dropped, his word: the email stays);
   the rename to `Overburden`; and a read of the docs for anything personal (I list every hit first, he
   decides each). *Other arm:* a fresh public repo with one starting commit. Clean at once, but the worklog
   and plans lose their history.
4. **Keep PolyForm Noncommercial for the alpha.** It already allows free play and sharing.
5. **Contributions: a CONTRIBUTING.md with his rule, plus a rights clause before the first outside merge.**
   His rule is already non-negotiables 1 and 5: content arrives as a mod; a Core change must be a feature any
   mod can use, never a special case; it comes with tests; water and energy stay exact. **The clause** says a
   contribution licenses him to use and relicense it, commercially included, so the game can still earn
   later. It should come from a standard template or a lawyer; I'm not one. The zip needs none of this.

### The export (one session)

6. **Install the 4.7.1 mono export templates** (a download from godotengine.org, roughly a gigabyte from
   memory of earlier 4.x releases, his to run or mine with his say-so) and **add a Windows x86_64 preset**, committed as `.gitignore` already
   expects.
7. **Find `mods/` beside the exe, not the working directory**: in an export, resolve against
   `OS.GetExecutablePath()`'s folder; in the editor, unchanged. And **say why on screen and in a log if
   content is missing**, rather than closing the window.
8. **Music beside the exe too** (`media/` next to it), optional as today. The alpha ships silent until a
   track with a redistribution licence, or his own remix, exists.
9. **The export gets its own user folder** (`Overburden Alpha`, by a project setting that applies to exported
   builds only), so testing an export never opens his dev worlds or rotates his logs. The mechanism gets
   checked in that session; if Godot can't split it cleanly, the export picks its own world folder in code.
10. **Flip `start.cmd`'s four flags to the code defaults now** (taken 2026-10-01; this is the trigger), with
    the measurement modes (`--profile`, `--fall`, headless) keeping today's arms unless a flag says otherwise,
    so the r6 control and the recorded recipes still read. Re-judge the light budget first (owed).
11. **A version stamp**: `Overburden alpha 0.1 (<commit>)` in the HUD's corner and the log's first line.
12. **The SQLite check is the session's first run.** If the native DLL doesn't load from an export, that's
    the session's problem before anything else.
13. **A zip script** (`tools/make-alpha.ps1`): export, then lay out `Overburden.exe`, its .pck and .NET data
    folder, `mods/overburden/`, `README.txt`, `LICENSE.md`, `THIRD-PARTY-NOTICES.md`, and zip it as
    `Overburden-alpha-0.1-win64.zip`. One command, so the next alpha is the same zip.

### The first ten minutes (one session)

14. **An Esc menu: Resume, Controls, Quit.** Esc opens it. Today Esc only releases the mouse; panels and the
    map still close on Esc first.
15. **A controls card**, from the menu and on F1, shown once at a world's first open: every bind in plain
    words, the debug ones under their own heading. It replaces the stale console print.
16. **The risky debug binds behind `--dev`:** Ctrl+R (1 ML), H (freeze the water) and Alt+H (GC). R, Alt+R,
    Alt+G, L, P and Alt+P stay as the alpha's creative keys.
17. **Taken in its other arm, built: fullscreen on F11 and Alt+Enter, far mode on F10.** The recommendation
    was: **Fullscreen on Alt+Enter, and far mode keeps F11.** With decision 10's adaptive radius on, F11 grows the
    view a ring at a time instead of jumping, so a stranger's press is safe. *Other arm:* fullscreen on F11
    and far mode on another key, which means retraining his hands.
18. **The console-only messages that matter reach the HUD**: too hard for this tool, the pack is empty
    (press Alt+P), undo done or refused.
19. **The same-kind swap refuses an emptier pack** and says so (fact 16).
20. **A fresh world starts with a pack worn and charged?** *Recommended no:* it starts as today, and the
    controls card says P, then Alt+P. Kinder to a stranger is the other arm (worn and full at first open,
    alpha only), at the cost of a second starting state to test.

### Before the alpha, or after it

21. **The hotbar's slots before the alpha: the Core half of the planned inventory** (nine slots that stay
    put, saved with the world, new things into the first empty slot) with a key to pin what V has selected.
    The full inventory screen comes after the alpha. *Other arm:* the whole screen first, a session or two
    more.
22. **The README for the zip** (`README.txt`), written with him: far from complete, in his words, including
    *"I have enjoyed the alpha/beta testing and thought you might like a go too"*; the controls; Windows
    x86_64 and a Vulkan or DX12 GPU; where saves and logs live; that Windows SmartScreen will warn about an
    unsigned exe ("More info", then "Run anyway"); how to report a bug (Issues, with `godot.log`); the
    licence in one line. The repo's README is rewritten from the stale one at the same time.
23. **`THIRD-PARTY-NOTICES.md`** in the repo and the zip (fact 18), and a credits line on the controls card.

## The split, if taken as recommended

| Session | What | Whose |
|---|---|---|
| A — the export | templates (6), preset, mods and music beside the exe (7, 8), its own user folder (9), the defaults flip (10), version (11), SQLite (12), the zip script (13) | mine; the template download his say-so |
| B — the first ten minutes | the Esc menu and controls card (14, 15), debug binds behind `--dev` (16), Alt+Enter (17), HUD messages (18), the swap (19) | mine; a screenshot each |
| C — the hotbar's slots | decision 21, M10-LADDER-PLAN's inventory decisions 2, 3 and 7 for its Core half, a header version | mine |
| D — going public | the README, notices, CONTRIBUTING (22, 23, 5); the email rewrite, the rename and the docs read (3) | the files mine; the rewrite, force-push, rename and publish his |

**Order:** A first, because the export is the unknown; B and C in either order; D last. The ideal machines'
IM-6 waits behind it, and IM-6's header version moves up one if C takes the next.

**The repo went public the same night** (seen 2026-10-12): `MatthewCarven/OverBurden`, the old repo renamed,
public, decision 2 in effect. Session D's docs read now edits files going forward only; the history keeps the
old text. The stale README is the first thing a visitor sees, so D's README moves up.

**He released 0.1.0-alpha the same night**, a Google Drive link added to the README on GitHub, before the
notices existed. **Decisions 22 and 23 were then done early, as 0.1.1-alpha** (`000ee80`, 74.1 MB):
`THIRD-PARTY-NOTICES.md` (each bundled component, its licence and copyright line), the full texts in
`tools/alpha/licenses/` (written by the engine itself through `tools/alpha/godot_licenses.gd`: its licence,
its bundled components and 19 licence texts; plus the .NET 8.0.31 runtime's two files from its package),
and `tools/alpha/README.txt`, the player's readme, his words first. `make-alpha.ps1` now refuses a zip
missing any of them. **Each zip that goes out gets its own `<Version>`.** The repo's own README rewrite is
still session D's. **0.1.1 replaced 0.1.0 on Drive the same night**, uploaded under 0.1.0's file name so
Drive's replace kept the link (his: the old one renamed "- old"): the file name says 0.1.0, the game and
the folder inside say 0.1.1. **Then built, his word**: `make-alpha.ps1` also writes the same bytes as
`build/Overburden-alpha-win64.zip`, a name that never changes, to upload over the last one.

## Session A — built (2026-10-12)

In the worktree `alpha-export`, after he installed the 4.7.1 mono templates. **The export works, SQLite
included**, and `tools/make-alpha.ps1` makes the zip in one command: `Overburden-0.1.0-alpha-win64.zip`,
74 MB, one folder inside (`Overburden.exe` 104 MB, a 49 KB `.pck`, the .NET runtime in
`data_Overburden.Game_windows_x86_64/` at 80 MB and 193 files, `mods/`, `media/Korobeiniki.mp3`,
`LICENSE.md`, `QUICKSTART.md`).

- **6, the preset**: `Overburden.Game/export_presets.cfg`, "Windows Desktop", x86_64, the pck beside the exe,
  `application/modify_resources` off (no exe icon or version resources yet; nothing else needed for them).
  **Godot's export wants a classic `.sln` named for the assembly**, which the repo lacked (it has
  `Overburden.slnx`): the first export logged *"no solution file exists"* for every C# file and shipped no
  assemblies. A `.sln` beside the game's `.csproj` would make `start.cmd`'s `dotnet build Overburden.Game`
  refuse (two project files in one folder), so it lives in `tools/godot-sln/Overburden.Game.sln` and
  `project.godot` points there (`dotnet/project/solution_directory`).
- **7 and 8, beside the exe**: `ExportFolder` (the exe's folder when `OS.HasFeature("template")`, else null)
  roots `mods/` and a relative music path in an export; the editor binary is unchanged. **A failed start
  names its reason** (`_fatalReason`, first cause wins) in the log and, windowed only, in a message box.
  The first failure-path probe put a box on his screen from a headless run and named the world rather than
  the missing `mods/`; both fixed: no box headless, and the content's reason wins.
- **9, its own folder**: `config/use_custom_user_dir.template` and `custom_user_dir_name.template` in
  `project.godot`, so an export keeps its worlds and logs in `%APPDATA%\Overburden Alpha\` and the editor
  binary stays in `app_userdata\Overburden`. Checked both ways by the log's first line.
- **10, the defaults**, refined in the build: **an export applies `start.cmd`'s four settings** (16 ms light
  budget, adaptive radius, fluidnear 2, the fluid job) at the top of the flag parser, so a flag still wins;
  the editor binary keeps the old arms, so the r6 control and every recorded recipe read as before with no
  measurement-mode rule needed. The exported run grew its view r3 → r5 → r6. The light budget's owed
  re-judge stays owed; 16 ms is what every play since September used.
- **11, the version**: `<Version>0.1.0-alpha</Version>`; the SDK appends the commit, and `Hud.VersionText`
  reads `Overburden 0.1.0-alpha (<sha7>)`, small in the upper left, and as the log's first line with where
  saves go.
- **12, SQLite**: `e_sqlite3.dll` publishes flat into the data folder, and the exported game created and wrote
  a world. TODO's owed check is closed.
- **13, the zip**: `tools/make-alpha.ps1` (refuses while Godot runs; `-Godot <exe>` for a worktree) exports,
  copies `mods/`, the music and the papers, warns for each paper still missing (`THIRD-PARTY-NOTICES.md` and
  `tools/alpha/README.txt` are session D's), zips, and prints size, SHA-256 and commit.

**Checked:** the zip unpacked fresh and run headless from an unrelated folder: content from beside the exe, a
new world through SQLite, the music, no `ERROR`; with `mods/` hidden, exit 1 and the right reason, no box.
Suite 2,303; r6 to the digit on the editor binary, which still saves under `app_userdata\Overburden`.
**Not checked: a windowed run of the export** (his: fullscreen on F11, the version in the corner, how it
feels on first open), and any machine but his. **Then checked, the same night, on a second machine**: he
copied the zip to his older desktop and ran it there, *"it runs fine, well my desktop is a bit older tech like
but its working without errors"*. The first run of Overburden on a machine that isn't the dev one.

## Session B — the code-level pass (2026-10-12)

Three read-only searches in parallel against `74d3fec`: Esc and the card, the debug keys and `--dev`, and the
HUD's messages with the swap. The load-bearing facts were checked by hand. **Two findings change the plan as
taken**: decision 19's refusal would make a second pack of a kind impossible to charge (fact 9), and decision
18's "too hard" message has nothing behind it today (fact 7).

### Facts

**Esc and the menu**
1. **Two places own Esc.** `FlyCam._UnhandledInput` frees the mouse on every Esc (`FlyCam.cs:194`) and runs
   before `Main._UnhandledInput` (`Main.cs:2907`), which closes the panel (`2928`) or the map (`2960`). So the Esc
   that closes the map also frees the mouse. Nothing calls `SetInputAsHandled`.
2. **With the mouse free and nothing open, the game's keys still act**: B, V, T, 1–9, F, Ctrl+Z, F10 and F12
   (`Main.cs:2942-2998`). Only the panel's early return (`2928`) stops them. The polled keys (Q, R, H, P, L) and
   both mouse verbs already need the mouse captured.
3. **There is no pause, on purpose** (`Main.cs:2938-2941`). Pausing the tree would stop the world clock (and the
   collapse fuse with it), the autosave timer, the collection of a water tick already on a worker, and the
   player; the collapse's cuts on `Task.Run` would carry on, and the music times itself by its own stopwatch.
4. **A quit from code saves like the window's X**: `_ExitTree` (`Main.cs:10251`) is the one save path,
   `auto_accept_quit` is at Godot's default, and the worklog verified that `quit()` runs the same teardown. A
   worker tick is cancelled and waited for, up to 5 s.
5. **All UI is built in code**: Labels on the default font, no Theme, and nothing yet uses a Button,
   ScrollContainer or RichTextLabel. Nothing scales with the window. The HUD is a CanvasLayer at 10 and the
   panel at 11; the map has no CanvasLayer and draws under both (the comment at `Main.cs:3226` says otherwise).
6. **`_newWorld`** (`Main.cs:253`) is true only when the world had no header (`9812`). It is not saved, and the
   header is first written by the first autosave (30 s) or the quit. `--nosave` and profile runs never set it.
   `screenshot.gd` instantiates `Main.tscn` as a play does, so a card shown at a new world's first open would be
   in a screenshot of a new world.

**The messages and the swap**
7. **There is no "too hard" message.** A free tool on stone is counted (`MiningController.cs:577`) and said
   nowhere outside a profile report, because `PrintGate` returns when nothing went unpowered (`Main.cs:5629`).
   The hand pick swings about 3.3 times a second.
8. **The pack gate's three lines print once a session** (no pack, ran out mid-bite, spent: `Main.cs:5636-5658`,
   their flags at `192` never reset). The undo's outcomes print once a press (`4886-4923`), except
   `UndoOutcome.VaultHolds` (`EditHistory.cs:312`), which has no case and prints nothing. A placement refused as
   occupied, inside the player, wet or unreachable is counted and never printed (`PlaceController.cs:840-844`).
   The HUD's only timed text is a new world's seed (`Hud.cs:211`: 7 s, then a 3 s fade), and its bottom column
   reserves 132 px.
9. **Only the worn pack charges**: the channel's recharge (`MachineTick.cs:1081`) and Alt+P (`388-406`) fill
   `Pack` alone, and there is no take-off verb. **So today the same-kind swap is the only way to charge a second
   pack of a kind**, and decision 19's refusal as written would make that impossible while the worn one holds
   anything.
10. **The swap's choice is made in `Main.EquipPackFromInventory`** (`3370`), which no test can reach (the tests
    reference Core and Persistence only). `CarriedPacks.FullestMillijoules` (`CarriedPacks.cs:53`) is the peek a
    refusal needs, taken before `_inventory.Take` (`Main.cs:3384`); a refusal there touches no identity term.

**The debug keys**
11. **His logs hold 71 Ctrl+R pours across 7 plays** (the latest 2026-10-09) **and no H or Alt+H at all.** No
    recipe, test or bench presses any of the three; the headless pours have their own flags (`--injectlitres`,
    `--fillbasin`, `--dampour`).
12. **H holds more than the water**: `_fluidHeld` (`Main.cs:7248`) skips the whole boundary block, so pumps,
    power, rain, the dump and the collapse sweep wait too, with nothing on screen. **Ctrl+R's size is one
    ternary** (`8493`): gating only that branch leaves sprint + R as a plain 10,000 L pour. `--dev` is a private
    bool on `Main`, set in the flag switch beside `--nogate` (`2340`).

### Decisions

Each with a recommendation; "as recommended" takes them all. Decision 20 stays as taken: a fresh world starts as
today, with no pack worn.

**Taken 2026-10-13, all as recommended** (*"as recommended please Claude, yes add --dev to mine too"*): B7's
`--dev` goes on his untracked `start2`–`start5` and `pondlook.cmd` as well. On decision 20 he added *"i was going
to make you mine lead and get acid somehow"*: that is M10-LADDER-PLAN's Tier 2 already (the furnace's sulphur +
water → acid; lead-acid = lead + acid + a glass jar; lead and gypsum or sulphur mid-depth).

- **B1. Esc belongs to Main alone.** FlyCam stops freeing the mouse on Esc. Main's order: with the panel open,
  Esc closes it (as today); with the map open, Esc closes it and the mouse stays as it was; with the menu or the
  card open, Esc closes it and takes the mouse back; with nothing open, Esc opens the menu. *Cost:* Esc no longer
  frees the mouse by itself; the menu does.
- **B2. The menu does not pause** (fact 3): the world runs under it as it does under the map and the panel.
  *Cost:* a roof's fuse and the water carry on while a stranger reads the card. *Other arm:* a pause, its own
  pass. *Revisit:* a fall or a flood while the menu was open that reads as unfair.
- **B3. While the menu or the card is open, no game key acts** except F11, Alt+Enter, F1 and Esc: the panel's
  early return (fact 2), extended to both.
- **B4. The menu: Resume, Controls, Quit**, centred, on a CanvasLayer above the panel. Quit says it is saving and
  calls `GetTree().Quit()` (fact 4). No confirmation, since nothing is lost.
- **B5. The card's text lives in code** (one file, `Ui/ControlsCard.cs`), written from QUICKSTART's "All the
  keys", in a scrolling panel that F1 and Esc close. The developer keys appear only under `--dev`, under their
  own heading. Its last lines are the credits: the version, *by Matthew Carven*, the music's credit line, *Made
  with Godot Engine*, and where the notices are. *Cost:* two copies of the keys (the card and QUICKSTART), kept
  in step by hand. *Revisit:* rebindable keys, when the card would be generated from the binding table.
- **B6. At a world's first open the card shows in an export only**, when `_newWorld` is true (fact 6), once.
  The editor binary's plays, probes and screenshots stay as they are: the line session A drew for `start.cmd`'s
  settings. *Cost:* a crash in a new world's first 30 s shows it again.
- **B7. `--dev` gates Ctrl+R, H and Alt+H.** Without it, Ctrl+R pours R's 10,000 L (sprinting and pressing R is
  just R), and H and Alt+H do nothing, with one log line the first time. **`start.cmd` passes `--dev`**, and so
  do his untracked `start2`–`start5` and `pondlook.cmd` on his word.
- **B8. Under `--dev`, a held H shows on the HUD**: *"WATER HELD (H): pumps, rain and roofs wait too"* for as long
  as it holds (fact 12).
- **B9. One message line on the HUD**, centred above the hotbar: a few seconds, then a fade. A new message
  replaces the old one and a repeat restarts its clock, so a held Q, or the pick on stone, keeps one message up
  rather than flickering. Every `GD.Print` stays for the log. It carries:
  - **the pick on stone**, new (fact 7): *"The hand pick can't take stone. T for a quantum tool (it needs a
    charged pack)."*;
  - **an empty pack, or one that ran out mid-bite**: *"The pack is empty. Alt+P fills it."*, every time rather
    than once a session (no pack at all stays the status line's);
  - **every undo outcome**, with `VaultHolds` given its case and its words (fact 8);
  - **out of material**, and **the swap's result**;
  - **what P, Alt+P, R and Alt+R or Alt+G just did**, one line each.

  *Not in it:* a placement refused as occupied, inside you, wet or unreachable (counted and unprinted today).
  *Other arm:* those too, a line each.
- **B10. The swap asks rather than refuses** (fact 9). A right-click with a pack of the worn kind in hand, when
  every carried one holds less than the worn one, does nothing the first time and says *"You're wearing your
  fullest lithium-ion pack (5.0 MJ). Right-click again to wear an empty one."*; a second right-click within 3 s
  swaps, as today. The rule is a Core function with its tests (fact 10). *Other arms:* refuse outright (a second
  pack of a kind could never be charged while the first holds anything), or swap as today and only say so.
- **B11. The stale console list goes** (`FlyCam.cs:125-139`); the log's first lines say *F1 for the controls*.
  QUICKSTART gains the Esc menu and F1, and moves Ctrl+R and H into a short "Developer keys (`--dev`)" section.
- **B12. The new UI's text is a size up** from the HUD's default (fact 5), fixed. Scaling the whole UI with the
  window is a decision of its own, not this session's.
- **B13. B ends with 0.1.2-alpha built**: `<Version>` bumped, `make-alpha.ps1` run, the zip in `build/`, and a
  screenshot each of the menu and the card (through a flag that opens them, for `screenshot.gd`). The upload is
  his, after his look.

## Session B — built (2026-10-13, finished 2026-10-19)

**Built on 2026-10-13 in the worktree `alpha-b`, and left there uncommitted and unrecorded**: main's hand-over
still said B was next. Found on 2026-10-19, when he asked to queue his tests and keep going. The code was read
whole before anything was added to it, and it does what B1–B12 say:
- **B1–B4, the menu** (`Ui/EscMenu.cs`, a CanvasLayer at 12): Resume, Controls, Quit over a shade that stops
  every click. Esc is `Main._UnhandledInput`'s alone (FlyCam's Esc is gone): the panel first, then the menu or
  card, then F1, then the map, and with nothing open the menu. F11 and Alt+Enter stay above all of it. Quit
  shows *Saving…* for 0.1 s, then `GetTree().Quit()`, the window's X's save path.
- **B5, B6, the card** (`Ui/ControlsCard.cs`, BBCode tables in a scrolling `RichTextLabel`): QUICKSTART's keys,
  the developer heading only under `--dev`, then the version, the music's credit and the notices. At a new
  world's first open it shows in an export only.
- **B7, B8, `--dev`**: Ctrl+R's million litres, H and Alt+H; without it Ctrl+R is R and H prints once that it
  is a developer key. Under `--dev` a held H puts an amber line at the top: *"WATER HELD (H): pumps, rain and
  roofs wait too"*. `start.cmd` passes it, and `start2`–`start5` carry it (`pondlook.cmd` calls `start.cmd`).
- **B9, the message line** (`Hud.ShowMessage`, centred above the hotbar, 4 s then a 1 s fade, the same text
  again restarting its clock): the pick on stone (*"The hand pick can't take that. T for a quantum tool (it
  needs a charged pack)."*), an empty pack or bank, a refused fill at range, out of material, every undo
  outcome (`VaultHolds` given its case and its print), the swap's result, and what P, Alt+P, R and Alt+R just
  did. Every one keeps its `GD.Print`.
- **B10, the swap asks** (`CarriedPacks.WouldWearLess`, `PackSwapGuard`, both in Core): a same-kind swap that
  would wear less than the worn pack does nothing and says *"You're wearing your fullest lithium-ion pack (5.0
  MJ). Right-click again to wear an empty one."*; a second right-click within 3 s swaps.
- **B11, B12**: the stale console list is one line (*"Esc for the menu, F1 for the controls"*); QUICKSTART and
  the player's README name Esc's menu and F1 and move Ctrl+R and H under "Developer keys". The menu and card
  are a size up (18, buttons 20, titles 26).

**Added on 2026-10-19:** a space the undo's refusal lacked (*"1.00 m³ moreclay"*), and one assertion. Writing the
battery's recipes before running them found that nothing checked a yes being spent at once: a guard whose yes
stayed open for the rest of the window passed the test as written (run to confirm: green), so the test now
clicks a third time straight after a yes.

**Checks.** Suite **2,305** (two new tests). **The battery: 9 of 9 red**, plus one identity: dropping
`wornMillijoules > 0` can't change the answer, since a carried pack's charge is never below zero (kept, it says
what the rule means). The r6 control to the digit from the worktree (2601 / 325 / 3446 at 411, no `ERROR`).
Headless, `--openmenu` and `--openmenu=controls --dev` each print their line with no `ERROR`, and `--dev=maybe`
is refused. **Screenshots of the menu and the card** (`--openmenu`, `--openmenu=controls --dev`): both read, and
against a shot of the same scene without them the shade takes the sky from 199 to 90, so it covers the window
and stops the clicks beside the menu. **Not checked**: the message line and the held line on screen (no flag
puts them up), how the card reads in fullscreen, and his hands on any of it.

## Session C — the code-level pass (2026-10-19)

Three read-only searches in parallel against `c39bd8a`: the row and P's overflow, the world header and its
versions, and V, the number keys and the inventory's Core types. The load-bearing facts were checked by hand (2,
3, 4, 6, 7, 9, 12, 13, 14). **Three findings shape it**: a holding at zero is deleted, so a slot that keeps its
material needs its own record (fact 2); every credit reaches one door, `Add`, the restore included, so the
first-empty-slot rule can live there but must stay out of the restore (fact 3); and **on the alpha's own path
the row is full before the first stone arrives** (fact 6), which is C3's question.

**C takes header v8, so IM-6's mode byte moves to v9.** IDEAL-MACHINES-PLAN decision 51 says the opposite
(*"M10g's hotbar assignment moves to v9"*, fact 16). M10g's screen needs no version of its own for the row: it
builds on C's.

### Facts

**The row today**
1. **The row is a projection, sorted nine times a frame.** `MaterialInSlot(slot)` is `Entries()[slot]`, the
   carried materials in ascending ID (`VolumetricInventory.cs:195-244`). `Hud.Slots = 9` (`Hud.cs:48`) is read
   once, to build the row (`:427`); the refresh calls `MaterialInSlot` for every slot every frame (`:524-539`),
   each call a new list and a sort.
2. **A holding at zero is deleted, not kept.** `Take` removes the entry (`VolumetricInventory.cs:167-170`), an
   `Add` of zero returns early (`:133`), and the header refuses a zero row (`WorldHeader.cs:485-491`). So a slot
   that keeps its material at zero can't be read off the inventory.
3. **Every credit reaches `Add`** (`VolumetricInventory.cs:117-142`): the carve (`Carve.cs:772`), a dismantle
   (`MachineSet.cs:661`), a body returned or wrecked at a chunk's arrival (`:525, :535, :642`), the undo's refund
   (`EditHistory.cs:407`), P (`DebugSeed.cs:94`), the swap's return (`Main.cs:3563`), `--stock=` (`:5641`), and
   the restore: `Reset` clears and re-adds every holding (`VolumetricInventory.cs:247-257`, from
   `Main.cs:1651`). It is already the one site the fluids are refused at (`:57-75`, `Main.cs:1554`). **`Add`
   says nothing about whether a material is new, and a hook in it would fire for every holding at every open.**
4. **The swap goes 1 → 0 → 1 in one call**: `Take` at `Main.cs:3528` deletes the last pack's entry and `Add` at
   `:3563` brings the kind back. An undo refunds through `Add` and debits through `Take` (`EditHistory.cs:405-
   415`). Under today's rule each is a shift; with slots that keep their material, each lands back in its slot.
5. **Fact 15 is half right.** Numeric IDs are air's 0, then the base mod's files in file-name order, and a world
   appends what its table has never seen (`Registry.cs:137-154`, `ContentLoader.cs:264-267`,
   `GameContent.cs:56`). In a fresh world the order is alphabetical, so with P's nine plus dirt, grass and stone
   the three past nine are **the solar panel, stone and the vacuum cell**, not the newest. In his `LakeTest2`
   (made 2026-10-11; the lithium-ion pack's file added 2026-10-12, `fcec2bb`) the pack is appended and sits past
   nine with stone and the vacuum cell: derived from the append rule, not read from his world file. (Fact 14's
   `Hud.cs:566` is `:748` now.)
6. **P adds nine, a hundred each, in this order** (`DebugSeed.cs:39-50`): relay pump, relay outlet, solar panel,
   vacuum cell (`cells`), clay, glass (100 m³ each), computer, lead-acid pack, lithium-ion pack (`count`). Its
   comment, *"in the order they first land on the hotbar"*, is untrue today and comes true under first-empty-
   slot. `placeable: false`: the computer, both packs, the quantum backpack and water. **On the alpha's path**,
   the hand pick's grass and dirt take slots 1 and 2, P's first seven take 3–9, and the lead-acid pack, the
   lithium-ion pack and the first stone a quantum tool takes all find the row full.
7. **The hand is a material ID, never a slot** (`PlaceController.MaterialId`, `:158`), and isn't saved
   (`PlayerSave` is the feet and flying, `WorldHeader.cs:145`). When it runs out it jumps to the largest
   placeable holding (`PlaceController.cs:531-534, 964-990`); the same fill picks a hand at open and before a
   first bite. **One recorded recipe runs a material out**: COMMANDS.md's orphan check (`--place --stock=64
   --material=overburden:relay_pump`, reading 128 orphan voxels after a reopen), so it may be reading the jump.
8. **An off-row selection is invisible.** The highlight compares IDs (`Hud.cs:537-538`): a block V reached past
   slot nine lights no slot, and the status line names no block (`:714-807`). V and the number keys print to
   the log only (`Main.cs:3731-3775`). A material on two slots would light both.

**Keys**
9. **The digits and V ignore every modifier** (`Main.cs:3045-3061`; only Ctrl+Z checks one), and key echoes are
   dropped (`:2941`), so a held V cycles once. Shift is sneak and descend, Ctrl sprint, Alt one voxel and slow
   flight (`FlyCam.cs:238`; `Main.cs:3147, 5678, 5925`): all three are held while moving or building, and each
   with a digit selects today. Free bare letters: C, E (M10g's, `Main.cs:3783`), G (Alt+G is taken), I, J, K, M,
   N, O, U, X, Y, Z; and 0, F2–F9 and the wheel.
10. **The panel and the Esc menu swallow every key** but F11 and Alt+Enter (`Main.cs:2960-2985`); **the map
    swallows none**, so digits select under it.
11. **V cycles every holding by ID**, items and machines included, and wraps (`PlaceController.cs:905-919`).
    There is no back cycle.

**The header**
12. **v7's payload** (`WorldHeader.cs:365-435`): settings (61 B), the table, the inventory (an int32 count and
    12-byte rows by ascending ID), the player behind a presence byte, the clock and lattice, v4's pack behind a
    presence byte, v5's carried packs. Writer and reader share `ValidatePack` and `ValidateCarriedPacks`
    (`:513-584`); the reader checks shape and range against the table's count, so a removed mod's hole passes,
    and the opener decides what a pack is (`Main.RestoreCarriedPacks`, `:10209-10228`, drops a non-pack with a
    log line). `:178-179` already reserves the next version for this.
13. **The writer sums its length by hand** (`:336-347`) and never checks it wrote that many bytes. A slip writes
    trailing zeros the reader refuses, or throws.
14. **The tests count from the payload's end.** `TrailerBytes` and `CarriedAbsentBytes`
    (`WorldHeaderTests.cs:384-400`) and every migration test's cut (`:456, :477, :704, :818`) or relabel (`:840,
    :886, :914`) move with an appended section. The only golden blobs are two v3 headers (`Fixtures/`); there is
    no v7 one.
15. **One open moves a world forward for good.** Opening rewrites the header at the build's version before any
    play (`Main.cs:1660`); a v7 build then refuses the world with *"format version 8, but this build reads
    versions 1 to 7"* (`BlobFormat.cs:219-221`), in a box, and quits (`Main.cs:1535-1539`). Every export shares
    `%APPDATA%\Overburden Alpha\`, so 0.1.2 and 0.1.3 share worlds; the editor binary doesn't.
16. **IM-6 planned v8 for the mode byte** (IDEAL-MACHINES-PLAN decision 51 and its IM-6 row, `:576-582`, `:649`;
    TODO's collision 5), and its decision 23 puts water *"in the hotbar's last slot while the backpack is
    worn"*, though the pocket refuses water (`:348-351`).

**Tests and text**
17. **Five slot tests** (`InventoryTests.cs:292-379`), one asserting the shift C removes
    (`EmptyingAMaterialShiftsEverySlotAboveIt`), and the header's ID-order test (`WorldHeaderTests.cs:433`).
    `NextMaterial`, `SelectSlot`, the hand's fill and the key dispatch live in Game, which no test reaches.
18. **What a player reads**: QUICKSTART.md:13, 59-60 and 136 (*"Coming next … hotbar slots you arrange
    yourself"*); `ControlsCard.cs:59-60`, kept in step by hand; `tools/alpha/README.txt:25`; the HUD's status
    line, *"1-9 material, V cycle"* nine times (`Hud.cs:726-807`) and `:748`'s *"pick it on the number row"*;
    the prints at `Main.cs:5227` and `:5656`.

### Decisions

Each with a recommendation; "as recommended" takes them all. M10-LADDER-PLAN's inventory decision 2 is the
object (C1, C2, C8); decision 3's key path is the pin until the grid exists (C4); decision 7's Core first is
C11's order.

**Taken 2026-10-19, all as recommended** (*"As recommended please Claude"*, his answer to the parent session).
His player-slot design the same evening (M10-LADDER-PLAN, "His design for the player's slots") changes none of
them: the row stays nine slots, and "still carried" means in the backpack.

- **C1. The row is its own record, owned by the inventory.** `HotbarSlots` in `Overburden.Core/Inventory/`: nine
  material IDs, −1 for empty, no ID twice, held by `VolumetricInventory` so that `Add` reaches it (C2).
  `MaterialInSlot` reads it, an array read in place of nine sorts a frame (fact 1); `Entries()` keeps its sort
  for the save and for V. *Cost:* the five slot tests are rewritten, one inverted. *Other arm:* a row beside the
  inventory that the HUD reconciles each frame; rejected, because P's nine arrive in one frame and a reconcile
  sees them in dictionary order, not arrival order.
- **C2. A slot keeps its material at zero; a material not on the row takes the first empty slot when it is
  credited.** The offer is in `Add` (fact 3), so every door has it; `Reset` offers nothing and takes the row
  from the header. A slot at zero dims and reads `0 L` or `× 0`, and the undo's refund and the swap's return land
  back in their own slot (fact 4). The offer sees every writer, so a body returned at a chunk's arrival can
  fill an empty slot with no key pressed: the rule working. *Cost:* no slot ever frees itself, so a row fills
  with things you once carried and stays full until a pin replaces them. *Other arm:* when no slot is empty, a
  slot at zero goes to the newcomer (a slot then moves only when the row is full and something on it is
  spent). *Revisit:* his play wants dead slots back.
- **C3. Only placeable materials take a slot by themselves; an item (the computer, a pack, M10g's parts) goes on
  the row by a pin.** This narrows decision 21's "new things", so it is his. On the alpha's path (fact 6) the
  row then reads grass, dirt, pump, outlet, solar panel, cell, clay, glass, and the first stone in slot 9; the
  computer and both packs are a V away, and a pack is worn once a session. Ladder decision 8 wanted a placeable
  filter on the row so parts would not take it over, and this keeps that for an empty row. *Cost:* a stranger's
  first pack is reached by V (QUICKSTART:13 already offers it), and his computer wants one pin per world.
  *Other arm:* everything takes a slot, Minecraft's rule, with P reordered pack first; then the row is grass,
  dirt, lithium-ion pack, pump, outlet, cell, solar panel, computer, glass, and clay, the lead-acid pack and
  **stone** find it full. *Revisit:* his play finds the pack or the computer a chase.
- **C4. The pin: hold V and press 1–9**, and what's in your hand goes in that slot. Already on the row, the two
  slots swap; not on it, the slot's old material takes the first empty slot, or leaves the row (still carried,
  still reached by V). Pinning to its own slot, or with nothing in hand, does nothing. It is decision 21's *"a
  key to pin what V has selected"* in V itself, and spends no letter (fact 9). *Cost:* V let go before the digit
  selects instead, and a quick V then a digit pins where a select was meant; the message line says which
  happened (C6). *Other arms:* Alt+1–9, the house's "Alt does the other thing", but Alt is held for slow flight
  and one voxel, so a digit pressed while crawling would pin; or a prefix letter (G, then a digit within 3 s).
  Shift and Ctrl are out: sneak and sprint are held. *Revisit:* he presses V and a digit and gets the other one.
- **C5. The hand stays on a material that runs out.** The fill picks a hand only when there is none (at open,
  before a first bite), and no longer jumps to the largest holding when the hand's material is spent (fact 7).
  A number key on a slot at zero selects it and a click says *out of stone*; an empty slot is still a miss.
  Shift, Ctrl or Alt with a digit still selects, so sprinting while switching works. *Cost:* placing your last
  stone and clicking again says so rather than building in dirt; and the orphan check (fact 7) may read the
  jump, so the build runs it on both arms and re-records it, with the reason, if it moves. *Other arm:* today's
  jump, with a slot at zero a miss like an empty one.
- **C6. The message line says what V and the keys did**, which only the log hears today (fact 8): V's choice,
  with *"not on the row: hold V and press 1–9 to pin it"* when it's off it; a pin's result (*"Stone pinned to 4;
  glass moved to 7"*, or *"… glass left the row"*); an empty slot pressed; and, once a session for each
  material that finds the row full, *"Stone has no slot. V selects it; hold V and press 1–9 to pin it."* A
  select on the row stays silent: the highlight is the answer.
- **C7. V is unchanged**: every holding in ID order. *Other arm:* the row's slots first, then what's off it.
- **C8. Header v8 appends the row after the carried packs**: nine int32s, −1 for empty, no presence byte (a row
  always exists). One validator for writer and reader, as the pack has: each −1 or a non-air ID inside the
  table, no ID twice, each refusal naming the slot. **A slot may name any material the table has, held or
  not**: zero is C2's state, and water on a slot is IM-6's decision 23 (C never puts it there, since the pocket
  refuses water before the offer). At open, a slot naming a removed mod's hole is emptied with a log line, as a
  non-pack is (fact 12). **A v7 world opens with the first nine holdings in ID order**, the row it showed, so
  nothing moves on its first open. The writer gains the length check it lacks (fact 13). *Cost:* 36 bytes, and
  every tail constant in the header tests grows by them (fact 14). *Not C's:* a holding of a hole's material
  isn't filtered at the restore either (`Main.cs:1651`); that is the inventory's.
- **C9. The hand isn't saved**, as today: it is a material, and the section is the row. *Other arm:* four more
  bytes, the hand's material, so a reopen keeps it. *Revisit:* a reopen whose hand reads wrong.
- **C10. Worlds move forward, not back** (fact 15): 0.1.3's readme says a world opened in it won't open in
  0.1.2 again. Nothing in code.
- **C11. The tests, Core first.** The round trip; a v7 world from a real v7 blob
  (`Fixtures/played-world-v7-header.bin`, written by today's code before the bump) opening with its ID-order
  row; each refusal at write and at read; a slot at zero kept; the first empty slot, and none when full;
  `Reset` offering nothing; the undo's refund and the swap's 1 → 0 → 1 back in their slot; P's placeables
  landing in its order; an item not offered (C3); the pin's swap, its displacement to an empty slot, its
  leaving the row and its two no-ops. **The battery's recipes are written before it runs** (session B's
  lesson), among them: offer from `Reset`; drop the swap branch; clear the slot on a `Take` to zero; skip the
  duplicate check; offer items.
- **C12. The seams and the end.** `--pin=<slot>=<identifier>` (repeatable) after open, and one log line printing
  the row at open and after every pin or offer (`hotbar: 1 grass, 2 dirt, 3 —, …`), so a headless open, pin,
  quit and reopen proves the round trip; r6 to the digit. The player's text (fact 18), DebugSeed's comment,
  `MaterialInSlot`'s remarks and every "v8" in the plans (below) move in the same commit. C ends with
  **0.1.3-alpha** built (`<Version>`, `make-alpha.ps1`) and a screenshot of the row with a dim slot; the upload
  is his.
- **C13. One session**: Core and its battery, then the wire, then the zip; in a worktree if he may be playing
  (`start.cmd` builds the main folder).

### What this changes elsewhere, once taken
- **IDEAL-MACHINES-PLAN**: decision 51 and IM-6's row put the mode byte in **v9**, and IM-6's oracle *"a v7
  world opens as sandbox"* reads v8; decision 51's last sentence turns round. Decision 23's water on the row is
  a slot under C8.
- **M10-LADDER-PLAN's inventory section**: decision 2 is built by C (its *"v4 world"* is a v7 one, and fact 3's
  *"v6"* is v8); decision 3's grid assigns, and drags, through the call C4's pin uses; decision 4's filter stays
  the grid's, and C3 decides only what lands on an empty slot.
- **TODO** (collision 5, the M10g line and the millilitre line), CLAUDE.md's hand-over, and
  `WorldHeader.cs:178-179`'s comment. QUICKSTART's *"Coming next"* loses the hotbar.

## Session C — built (2026-10-19)

Taken and built the same evening in the worktree `alpha-c`, merged (`ee49769` the Core half, `9e249ad` the wire),
and zipped as **0.1.3-alpha** (`e786e07`). The version copies moved first (`e830544`): IM-6's mode byte is v9.

- **C1, C2, the row** (`Inventory/HotbarSlots.cs`): nine IDs, −1 for empty, no ID twice, owned by
  `VolumetricInventory`. Its `Add` offers each arrival the first empty slot, and its `Reset` offers nothing. A
  slot keeps its material at zero. `MaterialInSlot` reads the row: an array read where the HUD sorted nine lists
  a frame.
- **C3**: `MaterialTable.Items()` (the computer, both packs, the quantum backpack, water) goes to
  `KeepOffTheRow`, set beside the fluids' refusal. On the alpha's path P fills slots 3–8 and leaves 9 for the
  first stone, tested against the shipped content.
- **C4, the pin, and a finding of the build.** V cycled on its press, so the press that opened the chord moved
  the hand before the digit arrived, and the pin took the material after the one meant. **A tap of V now cycles
  when it is let go**, and V held with a digit pins the hand untouched. *Cost:* V's cycle answers on the release,
  not the press. The swap, the displacement to the first empty slot, leaving the row and the two no-ops are
  `PinKind`s.
- **C5**: the hand's fill only fills an empty hand. A slot at zero selects; an empty slot is a miss, now said on
  screen. A spent pack's right-click says there is none left to wear.
- **C6**: the message line for V (the pin named when it is off the row), every pin's result, an empty slot
  pressed, and once a session a material a full row turned away. That last skips a material a pin answered in
  the same frame, found by the round trip (`--pin=` after `--stock=` named the stone it had just pinned).
- **C7**: V's order is unchanged.
- **C8**: header v8 appends nine int32s. `HotbarSlots.Problem` is the writer's and the reader's one rule, each
  refusal naming the slot as the keys do (1–9). A v7 header reads as its first nine holdings in ID order, and the
  opener empties a slot naming a removed mod's material and says so. **The writer now checks it wrote the length
  it summed** (the pass's fact 13).
- **C10**: the zip's readme says worlds move forward, not back. **C12**: `--pin=<slot>=<identifier>` and the
  `hotbar:` line, in COMMANDS.md. QUICKSTART, the card, the readme, the HUD's status line and P's words name the
  pin; DebugSeed's comment is true at last.

**Checks.**
- Suite **2,335**: thirty new (`HotbarSlotsTests`; the hotbar block of `InventoryTests` rewritten, its shift
  test inverted; nine v8 header tests; two against the shipped content). Nineteen tail offsets in
  `WorldHeaderTests` step over the new 36 bytes, and one more needed a bigger count: the carried count past the
  bytes now read the hotbar as its second row and failed on something else.
- **The migration against a real v7 blob**: `Fixtures/v7-header-before-the-hotbar.bin` (321 bytes), written by
  the v7 writer before the bump with a one-file app (`#:project` on Core): LakeTest2's table shape, twelve
  holdings, a pack worn and one carried.
- **The battery: 24 of 24 red**, the recipes written before the run, and the one named in advance to survive
  did: the writer's length check, a guard on a sum that is correct.
- r6 to the digit (2601 / 325 / 3446 at 411) with no `ERROR`, twice; `--openmenu=controls --dev` opens.
- **The round trip** on a scratch world: `--stock=64`, two pins, quit, reopen, the same row `(at open)`.
- **A copy of LakeTest2** (v7, thirteen holdings) opened with the row its 0.1.2 showed (clay, computer, dirt,
  glass, grass, lamp, lead-acid pack, relay outlet, relay pump), and **0.1.2 then refused the copy**: *"format
  version 8, but this build reads versions 1 to 7"*. Deleted after.
- **The orphan check read the hand's jump** (C5's named cost): with C5's line reverted on the same build it
  drains four bodies of 64, the hand running on through every machine after the pump (the recorded 128 was the
  relay pair, when `--stock=` had only those); with C5, one body, 64. Re-recorded in COMMANDS.md.
- **A screenshot** on a copy of LakeTest2 with `--pin=6=overburden:rubble`: his row with slot 6 dimmed at
  `0 L`, and the status line naming the pin.
- **0.1.3-alpha**: `Overburden-0.1.3-alpha-win64.zip`, 74.1 MB, sha256 `41af625f…4966cdd`, and the version-free
  copy. Unpacked to the scratchpad and run headless: the stamp, a pin, a reopen with the same row, no `ERROR`.
  The probe was deleted; his alpha `default` is untouched.

**Not checked**: his hands on any of it (the V chord's feel, a tap now answering on release; the message line; a
dim slot in his own play), and no windowed run but the screenshot.

## Session D — the code-level pass (2026-10-26)

Three read-only searches in parallel against `0753193`: personal data in the three history files, personal data in
the other tracked text, and what the README, CONTRIBUTING and the notices stand on. By hand: the git state against
GitHub, the repo's settings there, both machines' parts, the contributor-agreement templates at their sources, and
the load-bearing facts (1, 2, 8, 9, 10, 13, 14, 15, 17, 19, 22). **Four findings shape it**: GitHub holds a README
commit main lacks, so D starts from his `git pull` (fact 1); an outside PR today would arrive under PolyForm
Noncommercial, which can't be sublicensed, and CONTRIBUTORS.md promises a DCO that keeps nothing open (facts 9, 10);
the most revealing personal data is no single line but the times of day, which together map his hours (fact 15);
and the notices name .NET 8.0.31 where 0.1.3 ships 8.0.32 (fact 22).

**The repo has been public with its whole history since 2026-10-12.** An edit changes the files from here on; every
earlier version of a file stays readable in the history, and any clone or cache keeps it. Only a rewrite (`git
filter-repo`, then a force-push) takes text out of the history, and a clone made before it keeps it anyway. Decision
3 kept the history; the email rewrite was dropped at his word, the GitHub rename is done, and the remote URL and the
folder rename are his.

### Facts

**Every line number is as at `0753193`.** This pass's own commit moves some: WORKLOG.md's by 28 (its new entry on
top), MATTHEW-TODO.md's by about 20, and a few in CLAUDE.md and TODO.md. `git show 0753193:<file>` reads them as
cited.

**Where things are**
1. **GitHub holds a commit main lacks.** `5386e1d` (2026-10-20, his edit on GitHub's site) labels the first Drive
   link "Alpha Edition 1.0.0" and adds a second, "1.0.2". Main is five commits ahead (`e830544` to `0753193`), and
   nothing on main since the fork point `c3bdaef` touches README.md, so they merge clean. `pull.rebase` is false
   here, so his `git pull` merges and leaves alone the five SHAs the docs cite. Until he pulls, his push is refused.
2. **The two Drive links** are the files `Overburden-0.1.0-alpha-win64.zip` and `Overburden-0.1.2-alpha-win64.zip`
   (each page's title, read 2026-10-26). The first holds 0.1.1 (uploaded over 0.1.0 by Drive's replace, above); the
   second is a new file with its own link, so 0.1.2 didn't go up under the version-free name MATTHEW-TODO:145-147
   expects. His "1.0.x" labels are the 0.1.x builds; DESIGN.md:449 keeps v1.0 for M11. **0.1.3 isn't up yet.**
3. **The README is stale throughout** (main's line numbers; his commit adds three above :13): *"M0 through M5 done,
   and M6 under way — 734 tests"* (:19; the suite is 2,335); *"Esc to release the mouse"* (:75; Esc opens the menu);
   *"V cycles the material, and 1 and 2 pick a tool tier"* (:78-79; T cycles the tools, 1–9 are slots, V held with a
   digit pins, `ControlsCard.cs:50, 59-61`); `worlds/default/` (:81; `start.cmd:49` opens `LakeTest2`, and an export
   saves under `%APPDATA%\Overburden Alpha\`); *"Lua scripting via MoonSharp later"* (:109; M12, DESIGN.md:34);
   *"Claude Opus 5"* (:127). It never mentions the water, the collapse, the machines, power or the packs, all built.
4. **On GitHub** (`gh`, 2026-10-26): public; Issues on, Wiki and Discussions off; no releases, forks or stars; the
   licence detected as "Other" (GitHub's detector doesn't know PolyForm); the description reads *"Its voxel world
   based mining game with approximated water and machines"*. **Two pull requests, both his** (#1 2026-09-26, #2
   2026-09-30): no outside contribution is merged, so the licence can still be tightened. No `.github/` folder: no
   templates, no CI.
5. **A build from source assumes his layout**: Godot 4.7.1 mono at `./Godot_v4.7.1-stable_mono_win64/` (gitignored)
   or `../GAME/` (`start.cmd:6-7`); a .NET SDK for `net8.0` (no `global.json`; his machine has 10.0.402 only); and
   `start.cmd:49` passes `--dev "--world=LakeTest2"`, the debug keys and his test world (a later flag wins, so
   `start.cmd "--world=default"` picks another). `media/` is gitignored, so a source build is silent. A Godot that has
   never opened the project wants one `--headless --import`.
6. **No page for a mod author.** DESIGN.md:372-383 describes the loader (`mods/` scanned, strict files that name the
   nearest key, `mod.json`'s `format`); the base mod is the only example.
7. **The non-negotiables are numbered two ways**: CLAUDE.md:61-75 and DESIGN.md:77-94 list different fives in
   different orders. Decision 5's "1 and 5" are CLAUDE.md's.
8. **"Exact" is named identities in code**: water's `held == rained + injected + received − spilled − drawn`
   (`WaterSystem.cs:157, 169`); the energy pool, in millijoules as longs (`EquippedPack.cs:17-20`); the machines'
   `pumped == emitted + vaulted` (`MachineTick.cs:83`); the vault store's (`MachineSet.Vaults.cs:27`). Tests hold
   them under load (`ThePoolIdentityHoldsOverAThousandBoundariesWithEverythingRunning`, `MachineTickTests.cs:1143`;
   `FluidJobTests.cs:599`). One test project; the bench is Release only. **Every text file is LF in the index**
   (`git ls-files --eol`: 498 LF, 3 binary), CRLF only in his working copy by `core.autocrlf`, and there is no
   `.gitattributes`, so a contributor on Windows without autocrlf would commit CRLF.

**Contributions and rights**
9. **An outside PR today arrives under PolyForm Noncommercial.** GitHub's Terms of Service (D.6, "Contributions Under
   Repository License") license what a contributor adds under the repository's own licence, unless a separate
   agreement such as a CLA says otherwise. PolyForm grants no right to sublicense (`LICENSE.md:51`). So without an
   agreement he could use a contributor's code only as anyone may, noncommercially, and a commercial edition or a
   relicence would need every contributor's consent.
10. **CONTRIBUTORS.md promises the wrong tool**: contributions *"accepted under those same terms"* (:32-33), and PR
    authors *"should expect to be asked for a DCO sign-off (`git commit -s`) so the option stays open"* (:38-39). The
    DCO (1.1, developercertificate.org) certifies where a contribution came from and that its author may submit it,
    and grants nothing, so it keeps no option open. `:9, :14` name "Claude Opus 5"; the trailers name Opus 5 (278
    commits), Opus 5.5 (232) and Fable 5.1 (59).
11. **The templates**, read at their sources 2026-10-26:
    - **Harmony** (harmonyagreements.org, version 1.0, 2011; written as templates for exactly this): individual or
      entity, licence or assignment. The project fills its name, how to submit, a governing law (§6.1,
      `[JURISDICTION]`) and one of five outbound options. **Option Five lets the project license a contribution
      under any licence, provided it also stays available under the licences in use when it was submitted**: a
      commercial edition allowed, the PolyForm copy kept. §2.2 adds a patent licence from the contributor. It binds
      on signing, or on a first contribution made before signing.
    - **Apache's ICLA** (apache.org/licenses/contributor-agreements.html): the usual base for companies' CLAs; a
      broad copyright and patent licence with the right to sublicense. Its text names the Apache Software
      Foundation, so using it means editing a legal text by hand; the page doesn't say others may adapt it.
    - **The DCO**: origin only (fact 10). Projects that want rights too pair it with a grant of their own writing.
    - Agreeing can be a tick in a pull-request template, or CLA Assistant, a GitHub app that asks each new
      contributor once and keeps the record (an install and a permission, his to grant).
12. **The licence's notice** is `LICENSE.md:1`, `Required Notice: Copyright 2026 Matthew Carven`; nothing in the
    licence speaks of contributions.

**The machines** (read 2026-10-26 with `Get-CimInstance` and `nvidia-smi`, the desktop's over SSH; read-only)
13. **Both machines' parts are read.** The dev machine, where the alpha is made and played: a laptop, Intel Core
    i9-11900H (8 cores, 16 threads), NVIDIA GeForce RTX 3070 Laptop GPU (8 GB), 16 GB RAM, Windows 11 Home 64-bit.
    His older desktop: Intel Core i7-3770 (4 cores, 8 threads, 2012), NVIDIA GeForce GTX 1660 (6 GB), 16 GB RAM,
    Windows 11 Home 64-bit. *(Kept out of the first commit of this pass until he had said; written in once D3 was
    taken.)*
14. **Only 0.1.0 has run on the desktop** (session A, *"without errors"*), and nothing read its frame rate. **Neither
    machine runs Windows 10**, which the player's readme names (`tools/alpha/README.txt:18`). The export ships Vulkan
    only (`export_presets.cfg:43`, `export_d3d12=0`), so that readme's "supports Vulkan" is right and the planning
    pass's fact 8 ("or D3D12") isn't, for the zip.

**The docs read** (decision 3; every tracked text file. No hostname, CPU or GPU model, or other drive anywhere)
15. **His hours, in aggregate.** No line says when he is at the machine; together they do. Headings tagged with a
    time of day ("just past midnight", "small hours", "late night", "morning"…), WORKLOG.md 57, 73, 95, 193, 236,
    282, 382, 410, 438, 472, 864, 899, 918, 947, 971, 988, 1018, 1025, 1055, 1063, 1102, 1125, 1144, 1157, 1189,
    2018, 2032, 2051, 2061, 2087, 2119, 2140, 2235, 2250, 2282, 2291, 2310, 2908, 2936, 2968, 2992, 3024, 3066, 3201,
    3236, 3294, 3344, 3369, 3401, 3440, 3480, 3514, 3554, 3602, 3648, 3696, 3739, 3825, 3904 (59). Clock times from
    his logs: WORKLOG.md 229, 239, 279, 339, 509, 577, 578, 1020, 1050, 1058, 1106, 1140, 3432, 3484, 3548, 3607,
    3798, 3908, 4607, 6614, 7189; STATUS-HISTORY.md 1165; TODO-HISTORY.md 5801, 6088; MATTHEW-TODO.md 455 (25). And
    the clock offset every commit carries, which says the same and **can't change without a rewrite**; the text can.
16. **Where he lives, by hint only**: nothing names a country or a city. His wording hints, most of all his quoted
    *"for OH&S reasons"* line on 13 lines (MATTHEW-TODO:358, IDEAL-MACHINES-BRIEF.md:111, IDEAL-MACHINES-PLAN.md:43,
    340, 549, 554, 658, the panel's `briefs/DESIGNER-COMMON.md:83`, `briefs/JUDGE-COMMON.md:17`,
    `designs/2-gameplay.md:561, 655`, `designs/4-physics.md:400`). What the hints add up to is in the session's
    report, not here.
17. **His life beside the game**: the one family mention, `WORKLOG.md:1777`, with MATTHEW-TODO:367 (the same
    evening's tablet); `WORKLOG.md:1480` *"going onto battery power"*; `:6063` *"my machine has 16 threads"*; his
    storm recordings (`WORKLOG.md:889`, `TODO.md:318`); *"next time I get bored"* (MATTHEW-TODO:163);
    FL Studio (ALPHA-PLAN:22-23, `Main.cs:1013`, MATTHEW-TODO:164, COLLAPSE-PLAN.md:1023); *"(Please I am trying
    to)"* after "Don't sell it." (README.md:115); NVIDIA's overlay on his machine (CLAUDE.md:363, `Main.cs:8848`,
    COMMANDS.md:636, STATUS-HISTORY.md:284, 493, the panel's `readers/D-creative.md:316-319, 545`); a .NET update he
    ran mid-session (`WORKLOG.md:4599-4601`); his older desktop (ALPHA-PLAN:260, MATTHEW-TODO:59, 200-202, 404,
    TODO.md:25, CLAUDE.md:228, WORKLOG.md:92, STATUS-HISTORY.md:39, 49, 68). Work, health, money, other family:
    nothing.
18. **His email in the text**: ALPHA-PLAN:102 (the planning pass's fact 20) and `TODO-HISTORY.md:179-186`, an item
    still open (`[ ]`) though decision 3 dropped it on his word (ALPHA-PLAN:18-19; TODO.md:406). It is the author of
    573 commits regardless; the five made on GitHub's site carry his GitHub noreply address.
19. **Paths and tooling**: his user folder (`C:\Users\Admin\…`, Windows' stock account name) on seven lines, all in
    the ideal-machines panel's papers, `briefs/DESIGNER-COMMON.md:4, 26`, `briefs/JUDGE-COMMON.md:4, 10`,
    `briefs/READER-COMMON.md:4` (two with a session's scratchpad and its id) and `im0/batch.py:5` (a script with the
    repo's path written in); and the assistant's memory folder, `TODO-HISTORY.md:153`. None in the game's code, the
    scripts it runs, or anything a player reads. His other work: the Python-Lookuptables and CSharp-Lookuptables
    repos (`WORKLOG.md:2202, 2453`, `FloodIndex.cs:13, 40`, M10-LADDER-PLAN:3076), PySynthRack (`TODO.md:317, 320`,
    `WORKLOG.md:889`), his chess tables (`WORKLOG.md:599`), `../GAME/` in eight files.
20. **No secrets**: no keys, tokens or passwords; `export_credentials.cfg` is gitignored (`.gitignore:11-12`) and
    signing is off; no tracked logs. The only link off GitHub is his Drive (README.md:12), public by intent.
21. **Personal by nature**: MATTHEW-TODO.md (his list: play times, his worlds, the tablet, his music and uploads,
    "Notes to myself"); CLAUDE.md (the assistant's orientation, whose `:197-198` still called the remote
    `MinerCraft`, private: fixed in this pass's commit); `docs/panel/**` (the assistant's prompts to its agents,
    verbatim); WORKLOG.md (1.17 MB).

**Decisions 22 and 23 for 0.1.3**
22. **The notices name .NET 8.0.31; 0.1.3 ships 8.0.32** (`THIRD-PARTY-NOTICES.md:12`, against the zip's
    `Overburden.Game.runtimeconfig.json`: the SDK moved between 0.1.1 and 0.1.3). The two runtime files in
    `tools/alpha/licenses/` are byte-identical to 8.0.32's (`cmp` against the NuGet package), so only the number is
    wrong. Godot 4.7.1, Microsoft.Data.Sqlite 10.0.11 and SQLitePCLRaw 2.1.12 hold. The table doesn't name
    `SQLitePCLRaw.lib.e_sqlite3`, the package `e_sqlite3.dll` comes from, or SQLite's version (3.53.3, the DLL's).
23. **The zip's papers hold**: `make-alpha.ps1:67-74` refuses a zip without `LICENSE.md`, `QUICKSTART.md`,
    `THIRD-PARTY-NOTICES.md`, the readme or `licenses/godot-engine.txt`, and 0.1.3's carries all of them, its
    README.txt byte-identical to the repo's. **Decision 23's credits line is on the card** (`ControlsCard.cs:107-109`).
    The zip carries neither the repo's README nor CONTRIBUTING, rightly. `make-alpha.ps1` writes only to `build/`, so
    any run overwrites the 0.1.3 zip waiting for his upload.
24. **Small drift in what a player reads**: README.txt:30 lists the menu as "resume, quit" (it has Controls too);
    QUICKSTART's key tables lack P, which the card has (`ControlsCard.cs:68`; QUICKSTART:11 only); QUICKSTART:142
    names the repo's readme and how to contribute as coming next.

### Decisions

Each with a recommendation; "as recommended" takes them all. D builds files only: nothing in it changes the game,
so it needs no version of its own (D14 says when the player's text goes out).

**Taken 2026-10-26, all as recommended**, his words straight after the pass: *"Oh i ain't so worried about people
learning what I put in the public spotlight like"*, then *"and as recommended please Claude"*. So D3's figures go
public, and are written into fact 13 and D3 now. D10's one line still changes, as recommended: his note is about
his own details, and that line is about someone else. **The governing law is New South Wales, Australia** (D6), his
answers the same day: *"Australian"*, then *"NSW"*. He had asked whether US law could govern it instead. It can be
named, but US contract law is a state's, a US court can refuse a choice with no link to that state, and it buys no
US remedies; his own state's law was recommended, and it is what he named. **One thing waits before the build**
(D15): his `git pull`.

**The README**
- **D1. The README is rewritten for a visitor, and says only what is true now.** In order: his pitch (README.md:3-9,
  his words, kept); the download (D2); what it runs on (D3); playing, one line pointing at the player's readme
  (`tools/alpha/README.txt`) and QUICKSTART.md rather than repeating them; a bug, as the player's readme says (Issues,
  `godot.log`, the version in the corner); building from source (D4); how it's made (DESIGN.md, the plans, WORKLOG,
  TODO, and CLAUDE.md, said to be the assistant's orientation file); contributing (a line to CONTRIBUTING.md); the
  licence (his paragraph, his aside included, fact 17); built with (Claude Code and the models the trailers name).
  The M3–M6 status and the early numbers (the 850 quads, the 0.47 ms chunk) leave: STATUS-HISTORY and WORKLOG keep
  them. *Cost:* the numbers some visitors enjoy go. *Other arm:* a short "numbers it is built on" list, each figure
  re-measured first, since several date from M1–M3.
- **D2. His links stay, labelled with the game's versions, and from 0.1.3 the zips go on GitHub Releases**, decision
  2's home, which Drive took over in practice. The README's link becomes `…/OverBurden/releases/latest`, which never
  changes; each release is a tag (`v0.1.3-alpha`), marked pre-release, carrying the zip (74 MB, under GitHub's 2 GiB
  a file) and the SHA-256 `make-alpha.ps1` prints. His Drive links stay under "older builds", relabelled 0.1.1 and
  0.1.2 (fact 2). The upload is his: `gh release create v0.1.3-alpha build/Overburden-0.1.3-alpha-win64.zip
  --prerelease`, or the Releases page. *Cost:* a new habit, and a release is public the moment it's made. *Other
  arm:* Drive only, as a new file and a new README line each time (his 0.1.2) or Drive's replace under one name and
  one link (his 0.1.1); the labels move to 0.1.x either way.
- **D3. Both machines, by part, nothing that names them** (facts 13, 14): *"Made and played on an Intel Core
  i9-11900H with an NVIDIA GeForce RTX 3070 Laptop GPU (8 GB) and 16 GB of RAM. An earlier alpha has also run without
  errors on an older desktop: an Intel Core i7-3770 with a GeForce GTX 1660 (6 GB) and 16 GB. Both run Windows 11,
  and they're all it has been tried on."* No hostname, maker or driver. The player's readme keeps "Windows 10 or 11 (64-bit) and a graphics card that supports Vulkan", adding "tried on
  Windows 11 only". *Cost:* his hardware is public, a little more than the NVIDIA overlay and the 16 threads already
  say. *Other arm:* the desktop only, as the floor, or no parts. *Revisit:* the first report from a card below the
  desktop's, the real floor. If he runs 0.1.3 on the desktop once (copy, unzip, dig, quit), "an earlier alpha"
  becomes "it".
- **D4. Building from source, as it is**: Windows; Godot 4.7.1-stable, the .NET ("mono") edition, unzipped into the
  repo's folder as `Godot_v4.7.1-stable_mono_win64/`; a .NET 8 SDK or newer; one `--headless --import`; `dotnet test
  Overburden.slnx`; `start.cmd`. **`start.cmd` stays his** (his world and `--dev`, fact 5), and the README says so:
  it opens a world named LakeTest2 with the debug keys on, `start.cmd "--world=<name>"` opens another, and a source
  build has no music. *Cost:* a builder's first world is named after his. *Other arm:* his two flags move to an
  untracked script of his own (as `start2`–`start5` are), and the tracked `start.cmd` opens `default`.

**CONTRIBUTING**
- **D5. CONTRIBUTING.md: his rule first, in his words** (ALPHA-PLAN:6, *"if you have to modify core do it in a way
  that everyone else can take advantage of it too"*), then as four checks: content arrives as a mod, through the
  loader the base game uses; Core never references Godot, and a Core change is a feature any mod can use, never a
  special case; every change comes with tests and the suite passes; water and energy stay exact (fact 8's identities
  by name, each with the test that holds it). Then the practical rules: namespaced IDs, never renumbered, and
  `minercraft:` an alias forever; the other non-negotiables by link, named by content rather than number (fact 7);
  benches Release only; an issue before a PR that touches Core or a save format; the rights clause (D6). **And a
  `.gitattributes` of `* text=auto`**, safe because every blob is already LF (fact 8), so a contributor's line
  endings come out as his do. The mod author's page (fact 6) is later work, in TODO; CONTRIBUTING points at DESIGN's
  loader section and the base mod meanwhile. *Cost:* a long-ish file for a project with no contributors yet. *Other
  arm:* the rule and the clause only, everything else by link.
- **D6. The rights clause: Harmony's individual contributor licence agreement, outbound Option Five, as `CLA.md`**,
  filled from the template with Matthew Carven as "Us", Overburden as the project, the pull request as the way to
  submit (D7), and **the governing law his to name** (§6.1: it says where he lives, which facts 15 and 16 only
  hint at). A contributor keeps their copyright and gives him a licence to use,
  change and relicense the work under any terms, commercial included, while it stays available under PolyForm; and
  a patent licence. An entity version only when a company wants to contribute. *Why this one:* a template written for
  this, whose Option Five says in its own words what decision 5 asks, where Apache's would need its legal text
  edited. **I'm not a lawyer, and this isn't legal advice**: a template beats a clause I'd write, and a lawyer's read
  before the first outside merge beats both, if the game is ever to earn. *Cost:* some people won't sign a CLA, and a
  typo fix becomes paperwork. *Other arms:* Apache's ICLA, adapted; the DCO with a grant paragraph in CONTRIBUTING
  (lightest, but a legal clause of my own writing, which decision 5 ruled out); or nothing, and contributions stay
  PolyForm-only (fact 9), fine if it will never be sold. *Revisit:* a lawyer's view, or a contributor worth having
  who won't sign.
- **D7. Agreeing is a tick box in a pull-request template**, `.github/pull_request_template.md`: *"I have read CLA.md
  and agree to it for this contribution"*, with his rule's four checks as boxes beside it, and he merges nothing
  unticked. The record is the PR itself; no service, no grant. *Cost:* a tick is a thinner record than a signature
  (Harmony also counts a first contribution as agreement). *Other arm:* CLA Assistant (fact 11), his to install.
  *Revisit:* more than a handful of outside PRs.
- **D8. CONTRIBUTORS.md says what's true**: `:31-39` point at CONTRIBUTING and CLA.md, with fact 9's reason in a line,
  and the DCO promise goes (fact 10); `:9, :14` name the models the trailers name. The rest (his ownership, how the
  collaboration works) is his and stays.

**The docs read** (decision 3). Each is his; the recommendation is the least that takes a thing out of today's files.
The history keeps the old text whatever he chooses (above).
- **D9. His hours stay** (fact 15): 84 lines, all but one in 26,000 lines of history, the commit clocks say as much and can't change,
  and the times are working data (a play log is named and found by its time). **From now on a worklog heading carries
  the date only.** *Other arm:* strip the 59 tags and 25 times now (a script; the history keeps them).
- **D10. One line changes, the family one**: `WORKLOG.md:1777` becomes *"so it was another device in the room"*, and
  MATTHEW-TODO:367's tablet goes with it. The rest of fact 17 stays: it is his voice about the game
  and his tools, and *"(Please I am trying to)"* is the reason D6 exists. *Other arm:* all of fact 17 kept, or more
  of it cut, line by line.
- **D11. The email stays, and its open item closes**: ALPHA-PLAN:102 is the record of the decision about it, and
  TODO-HISTORY:179's box is ticked as dropped, with his words and the date, so it stops reading as an open question.
  *Other arm:* the address out of both lines (the commits keep it).
- **D12. The panel's papers and the personal files stay tracked, as they are** (facts 19, 21): "Admin" is Windows'
  stock name, a scratchpad id opens nothing, and the papers are verbatim on purpose. MATTHEW-TODO.md, CLAUDE.md and
  WORKLOG are how the project is made, and a worktree or another session reads them from the tree. **MATTHEW-TODO
  gets a line at its top saying it is public**, so "Notes to myself" never holds what he wouldn't post. *Other arm:*
  `<repo>` and `<scratchpad>` for the seven paths; or MATTHEW-TODO.md untracked, private from then on and unseen by
  worktrees, its history still public.
- **D13. No history rewrite.** Nothing found is a secret, and decision 3 kept the history. If he wants a line out of
  it after all, it is `git filter-repo` and a force-push, his to run, and today is the cheapest day: no forks, no
  stars, no outside PRs (fact 4).

**22 and 23**
- **D14. The notices to 8.0.32, and the drift stopped at its source**: `THIRD-PARTY-NOTICES.md:12`'s runtime to
  8.0.32, `SQLitePCLRaw.lib.e_sqlite3` named beside the bundle, SQLite 3.53.3; and **`make-alpha.ps1` reads the
  runtime's version from the export's `runtimeconfig.json` and refuses a zip whose notices name another**, so an SDK
  update can't move it silently again. The player's text too (fact 24): the menu's three items, P in QUICKSTART's
  tables, QUICKSTART:142's "coming next" gone. **These ride the next zip; 0.1.3 goes up as built**, since each zip
  that goes out has its own version and none of this is the game. *Cost:* 0.1.3's notices name the wrong patch
  release of .NET (its texts are identical). *Other arm:* rebuild as 0.1.4 with D's text before his upload.
- **D15. The order.** His first: `git pull` (it merges `5386e1d`, README only, clean), then `git push`. Then one
  session, files only, nothing launched: the README, CONTRIBUTING.md, CLA.md, the PR template, `.gitattributes`,
  CONTRIBUTORS.md, the notices and the script's check, and the docs-read edits he takes. The check is proved in a
  worktree (`-Godot <exe>`), whose `build/` is its own, so 0.1.3's zip in the main folder stays the one he uploads
  (fact 23). Then his: the release (D2), the description's "It's" (fact 4), and whenever suits, the remote URL and
  the folder rename. *Other arm:* I merge `5386e1d` into main at that session's start, and he only pushes.

### What this changes elsewhere, once taken
- **The planning pass's fact 8**: the zip is Vulkan only (fact 14). **TODO**: a mod author's page (D5), and D's
  status. **MATTHEW-TODO**: the release in "Yours to run" (D2), the CLA's jurisdiction (D6), and the 0.1.3 upload's
  line (its README link). **QUICKSTART**, the card and README.txt as D14 says, kept in step by hand.
