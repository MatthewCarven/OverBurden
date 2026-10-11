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
   the shaders were written for Forward+.
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
    (M10-LADDER-PLAN, "The inventory screen — folded into M10g").
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
