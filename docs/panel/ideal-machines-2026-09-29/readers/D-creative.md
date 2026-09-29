# Reader D: what a creative mode would touch, and the demo's surface

Read-only map, 2026-09-29, against `main` at `71e6eed`. Paths are relative to the repo root.
`Main.cs` means `Overburden.Game/Main.cs` (9,536 lines). Figures marked *arithmetic* are mine,
computed from the numbers cited beside them. Figures marked *inference* are my reading of the code
and have not been run.

## The short version

- **No creative mode exists, by that name or any other.** There is no mode enum, no menu, no world
  picker, no pause screen, and no field in the world header for a mode. What exists is a set of debug
  doors that together add up to an accidental creative kit: `P` (machines and blocks into the
  inventory), `Alt+P` (every cell and the pack filled), `--stock=` (every material into the
  inventory), `--nogate` (power off), `F` (fly, which is noclip), `L` (a lamp anywhere), `R` (water
  from nothing), and `T` (all five tools, the quantum ones included). None of them is a mode, and
  several are traps (see "Traps").
- **The code has been waiting for a game-mode concept since 2026-08-30.** `TODO.md:160-161` lists
  what waits on it: reach, the fly-mode gate on precision, a 3 m or 2 m reach, swimming, drowning,
  buoyancy, wading, oxygen and jump height. `PlayerController.cs:104-108` records the reach trigger.
  Core's `Place` already takes a null inventory and documents that as "what creative building is"
  (`Overburden.Core/Building/Place.cs:30`), but the Game never passes one.
- **Survival progression does not exist either.** Every machine and tool arrives by a key or a flag.
  The world holds grass, dirt and stone only (`Main.cs:8702-8707`); there are no recipes, no ore, no
  crafting and no health. The demo is not pulling creative out of a survival game: it is choosing
  what the one game there is shows first.

---

## 1. How the game starts

### There is one scene and one entry point

`Main.tscn` is four nodes: `Main` (the script), `WorldEnvironment`, and `FlyCam` placed at
(15, 10, 15) m. Everything else is built in code in `Main._Ready` (`Main.cs:1329-1574`). The order
is: parse flags (`:1351`), open the world (`:1405`; a failure quits with code 1), water, pipeline,
fluid, support, machines, restore the inventory (`:1509`), write the header (`:1518`), then the
player (`:1520`), mining (`:1551`), placement (`:1552`), HUD, panel and map (`:1553`), and the pack
(`:1554`). **There is no main menu, no world picker, no mode choice and no settings screen**
(grep for menu, pause, world list: nothing in code or docs beyond `TODO.md:165`, which notes that
"no pause screen or world listing" shows the seed). Esc only releases the mouse
(`Overburden.Game/Player/FlyCam.cs:194-196`). Closing the window saves (`Main.cs:9353` onwards),
and an autosave runs every 30 s (`Main.cs:1318`).

`Main.cs:2743-2746` explains why the map is an overlay and not a pause: *"A pause would be the first
in this game and the water, the collapse timers and the machine tick would all have to agree what it
meant."* A creative menu that pauses the world would be the first pause, with that cost.

### `start.cmd` opens his world, not a new one

`start.cmd` builds `Overburden.Game`, then launches with
`-- --lightbudget=16 --adaptive=1 --fluidnear=2 --fluidjob=1 "--world=TajMahal" %*` (last line of
`start.cmd`). **The `--world=TajMahal` was added in commit `ba6d87b` on 2026-09-27, and CLAUDE.md's
Commands section does not mention it.** A later `--world=` wins, because the parser just overwrites
`_worldName` (`Main.cs:2304-2318`), so `start.cmd --world=Demo` makes a new world. The launcher needs
the .NET SDK and the Godot mono editor build. There is no `export_presets.cfg`, and "the release
export with SQLitePCLRaw's natives is unverified" (`TODO.md:109-110`). **So nobody but Matthew can
run the game today.** A demo people download needs an export, which is its own piece of work.

### Opening or creating a world

- `--world=<name>` picks `user://worlds/<name>/world.db`. The default name is `default`
  (`Main.cs:1309`). A missing world is created. A null header is how "new world" is detected
  (`Main.cs:8964-8966`).
- A new played world gets a random nine-digit seed unless `--seed=` is given or the run is a profile
  (`Main.cs:8978-8981`). It also gets the shipped relief (base 0.4×, peak 3×, rarity 0.25, per
  `docs/COMMANDS.md:1059-1061`; applied at `Main.cs:8987-8990`).
- On an existing world, `--seed`, `--relief*` and `--cavestep` are warned and ignored
  (`Main.cs:9019-9053`), and the world's saved clock replaces `--clock` (`Main.cs:9017`).
- **Persistence switches off** for `--profile`, `--fall`, `--debuglamp` and `--openpanel` unless
  `--world=` names a world (`Main.cs:2591-2594`). `--nosave` turns it off outright (`:2412-2414`).

### The flag parser

`ApplyCommandLineOverrides`, `Main.cs:1681-2611`, is one `switch` over `OS.GetCmdlineUserArgs()`
(`:1702`). The rules that bite:

- **A game flag must come after a bare `--`.** Before it, Godot drops it silently. `Main.cs:1692-1700`
  warns only about a `--name=value` shape before the `--`. **A bare flag like `--nogate`, `--nosave`
  or `--map` before the `--` gets no warning and simply does nothing.**
- **A value that fails to parse falls through to `default` and prints `unknown flag … — ignored`**
  (`:2576-2582`), because most cases carry a `when TryParse` guard. So `--stock=lots` is "unknown",
  not an error. `--world`, `--seed` and `--relief*` refuse loudly instead (`:2304-2392`).
- The last occurrence of a flag wins.

**Flags that matter to a creative mode.** All of these exist; none of them is profile-only unless
the table says so.

| Flag | What it does | Where |
|---|---|---|
| `--world=<name>` | Which world opens, or is created | `Main.cs:2304` |
| `--seed=<n>`, `--relief=`, `--reliefbase=`, `--reliefrare=` | A new world's terrain | `:2326-2392` |
| `--nosave` | No world file | `:2412` |
| `--stock=<voxels>` | Adds N voxels of **every** placeable material, and one of every item, **after** the saved inventory is restored | `:2568`, applied `:5074-5087` |
| `--material=<id>` | Selects a material at open | `:2521`, `:5089-5095` |
| `--nogate` | Turns the power pool off: pumps, the quantum bite and the fill at range all run free | `:2158`, applied `:4216`, `:4985`, `:5060` |
| `--pack[=<id>]`, `--charge=<J>` | Equips a pack at open, with a charge | `:2186-2192`, `:3082-3106` |
| `--entangle` / `--entangletunnel` / `--entanglechunk` | Opens holding the quantum miner, tunneller or excavator | `:2227-2257` |
| `--holdq` | Q held for the whole run | `:2234` |
| `--at=x,y,z` | Spawns flying at a point; beats the saved position | `:1874-1879`, `:1623-1630` |
| `--speed=<m/s>` | Scripted flythrough. **Also runs in a played session**, not only a profile, and then refuses walk mode | `:1722`, `:3641`, `:2810-2815` |
| `--map` | Opens F12's map at start | `:2398` |
| `--openpanel[=pump\|outlet\|cell\|panel\|pack]`, `--panelset=` | The panel as a screenshot prop | `:2146-2152`, `:2196-2209` |
| `--pump=`, `--pumpoutlet=`, `--cell=`, `--solar=`, `--port=` | Places a pump rig at open (with `--breach`) | `:2172-2180`, `:2428-2451` |
| `--rainscale=` (default 20), `--weather=`, `--clock=` | Weather and rain | `:1847`, `:1801`, `:1790`; default `:1185` |
| `--collapse=0`, `--support=0`, `--saturation=0`, `--collapsemode=sweep\|pieces` | The mine as antagonist, switched off or changed | `:1922-1957` |
| `--far=`, `--adaptive=` | View radius | `:2478-2489` |
| `--debuglamp` | A lamp where the crosshair points (and persistence off) | `:2138` |

**Nothing is called creative, fly, god, infinite, noclip or peaceful**, in the parser or anywhere
else in the code. Three flags exist only in plans and are not parsed: `--placeat=<id>@x,y,z`
(COLLAPSE-PLAN decision 13 and slice f-2, `docs/COLLAPSE-PLAN.md:1149`, `:1158`), `--assign=`
and `--openinventory` (M10g, `docs/M10-LADDER-PLAN.md` "The inventory screen", decisions 3 and 7).
`--holdafter=` is named in `TapOrHold.cs:24` as the tap threshold's sweep, but it is not parsed.

---

## 2. How machines and tools arrive today

### Tools: five JSON files, all on T from the first frame

`Overburden.Game/mods/overburden/tools/`:

| Tool | Bite | Hardness it takes | Cooldown | Power |
|---|---|---|---|---|
| `hand_pick` | sphere r 0.4 m | ≤ 1.0 | 0.30 s | free |
| `steel_pick` | sphere r 0.5 m | ≤ 3.5 | 0.20 s | free |
| `quantum_miner` | `cube` (1 m brush cell) | ≤ 5.0 | 0.10 s | `via: channel`, 50 J/voxel |
| `quantum_tunneller` | `tunnel` (2 m cube) | ≤ 5.0 | 0.25 s | `via: channel`, 50 J/voxel |
| `quantum_excavator` | `chunk` (8 m, 32,768 voxels) | ≤ 5.0 | 0.25 s | `via: channel`, 50 J/voxel |

`ToolTiers.Order` sorts by (threshold, bite, id) (`Overburden.Core/Content/ToolTiers.cs:30-38`), so
T cycles hand → steel → miner → tunneller → excavator (`Main.cs:3397-3403`; the bite enum's order is
`ToolDefinition.cs:192-205`). **A fresh session starts on the strongest free tool: the steel pick**
(`ToolTiers.cs:41-58`, `Main.cs:4952`). The held tier is not saved: `PlayerSave` deliberately
excludes it (`Overburden.Core/Persistence/WorldHeader.cs:140-145`), so every open starts on the
steel pick. **The hand pick cannot take stone** (stone's hardness is 3.0 in `stone.json`; the hand
pick's threshold is 1.0). Every tool is reachable in the first second. Nothing is locked.

### Machines and blocks: `P`, `Alt+P`, `--stock=`

- **`P`** (edge-polled, only while the mouse is captured; `Main.cs:4613-4681`) adds one body each
  (64 voxels, a 1 m cell) of relay pump, relay outlet, solar panel and vacuum cell; 64 cells each of
  clay and glass; and one computer and one lead-acid pack, **once only** (`:4670-4678`). It can be
  pressed any number of times. The machines are the only ones in the content:
  `relay_pump`, `relay_outlet`, `solar_panel`, `vacuum_cell`, `lead_acid_pack` (carried), and the
  `computer` (an item). **All of them are quantum-tier (`via: channel`). No steam, DC or AC machine
  exists in content or code**, although M10a-c of the ladder are built.
- **`Alt+P`** fills every placed storage row to capacity and the equipped pack
  (`Main.cs:4684-4716`). It goes through `MachineTick.FillPack` so that it is a printed `debug` term in
  the pool identity (`Overburden.Core/Machines/MachineTick.cs:305-320`, `:408`).
- **`--stock=N`** walks every material in the registry. Placeables get N voxels and items get one
  (`Main.cs:5080-5086`). That includes rubble and the lamp, and machines at N/64 bodies. It runs
  **after** the world's inventory is restored (`:1509` comes before `:1552`), so on a played world it
  adds to what is carried, and the next save keeps it.
- **`L`** places a lamp at the crosshair without touching the inventory (`Main.cs:5477-5493`). With
  a quantum tier in hand that means anywhere in the loaded world.
- **`--pump=`** with `--breach` places a pump, an outlet and optionally a charged cell and solar
  panels at open (`:2428-2433`, `:2172-2180`).

The HUD tells a player about `P`: *"P seeds one, then pick it on the number row and right-click to
equip it"* (`Overburden.Game/Ui/Hud.cs:547`).

### The gate, and what `--nogate` really does

The gate is M10c-3's rule: an at-range tier pays `joulesPerVoxel` from the equipped pack, and with
no pack it takes nothing (`Hud.cs:545-548`, `Main.cs:3110-3115`). `--nogate` sets `PowerOff` on
three objects: the machine tick (`Main.cs:4216`), the mining controller (`:4985`), and the placement
controller (`:5060`). **In the tick, `PowerOff` means generation is zero** (`MachineTick.cs:534`),
and no charge flows (`:584`, `:931`, `:1023`). **So `--nogate` is not "infinite power". It turns
the power system off**: solar produces nothing, cells never charge, the pack never recharges, and
every pump runs free. It is the pre-M10c arm (`Main.cs:4221`), and it is not saved.

### Some numbers the demo will run into (*arithmetic*, from the JSON)

- The lead-acid pack holds 3.6 MJ (`lead_acid_pack.json`). At 50 J a voxel that is 72,000 voxels:
  about 1,125 miner cubes, 140 tunneller bites, or 2.2 excavator chunks (the chunk is 1.64 MJ, as
  `Place.cs` says).
- The quantum miner held on Q, at its nominal 10 bites/s × 64 voxels × 50 J, draws 32 kW, so it
  empties a full pack in about 112 s of solid rock. **The measured hold rate is lower**: 4.2/s against
  the nominal 10 (`TODO.md:111-113`, unexplained).
- The pack recharges at 250 W at most (`rechargeWatts`), which is 4 hours from empty. A solar panel
  is rated at 500 W; the vacuum cell holds 5 GJ.

**A fresh demo player therefore meets the gate within two minutes of finding Q, and `Alt+P` is the
only quick refill.**

### Survival progression: none

No recipe, station, ore or crafting code exists (M10g, M10h and M10i are planned in
`docs/M10-LADDER-PLAN.md`, "The split"). Worldgen makes grass (1 voxel), dirt (3), stone below, and
caves (`Main.cs:8702-8707`). **A player cannot earn anything**: every machine comes from `P` or a
flag, and every tool from `T`. DESIGN's locked table still says *"Scope target: Survival, no mobs"*
and *"a tool is earned, not unlocked"* (`docs/DESIGN.md:33`, `:38`). A creative mode sits beside
that goal; it does not replace it.

---

## 3. The player

### Movement: walk and fly, and fly is noclip

- There are two modes, `Walk` and `Fly` (`Overburden.Game/Player/PlayerController.cs:10-17`). **F
  toggles between them at any time** outside a scripted flythrough (`Main.cs:2805-2826`). The mode is
  saved in the header as `PlayerSave.Flying` (`WorldHeader.cs:145`).
- **Walk**: a physics body 0.5 × 1.8 m with the eye at 1.65 m. Walk speed is 4.5 m/s, sprint (Ctrl)
  7.0, sneak (Shift) 0.3×, jump 7.5 m/s, gravity 25 m/s², terminal speed 40, step 0.5 m
  (`Overburden.Core/Physics/PlayerMotionConfig.cs:49-174`). Sneak refuses to walk off an edge
  (`Main.cs:2704-2708`).
- **Fly is `FlyCam` moving `GlobalPosition` directly, with no collision at all**
  (`FlyCam.cs:226-249`). So fly is noclip through rock. It moves at 6 m/s, ×5 with Ctrl, ×0.2 with
  Alt, with Space and Shift for up and down (`FlyCam.cs:21-24`, `:240-247`). The body stays inert in
  fly mode (`PlayerController.cs:154-157`).
- A new played world spawns **walking** on the surface of the scene camera's column, x 15 m, z 15 m
  (`Main.cs:1604-1611`). A restored world uses its saved position and mode (`:1616-1621`). `--at=`
  spawns flying (`:1623-1630`), and so does a profile.
- **Water does not touch the body** (*inference*). The collider counts only non-air, non-fluid
  voxels (`Overburden.Core/Physics/VoxelCollider.cs:469`). Tier B water is a separate grid, not
  voxels, and swimming, drowning, buoyancy and wading are all listed as waiting on a game mode
  (`TODO.md:160-161`).

### Reach

- **5 m, for the player, in both modes** (`PlayerController.cs:50`, `:97-110`). Placement uses the
  same number (`Main.cs:5043-5045`). The recorded revisit trigger is *"a survival mode, which wants
  3 m or 2 m"* (`PlayerController.cs:104-108`).
- **The at-range tiers** (cube, tunnel, chunk) cast to `EntangledReachMetres = 512`
  (`Overburden.Game/Player/MiningController.cs:194`, `:364`). The real bound is the loaded world
  (`:185-193`). At the default r6h that is about 48 m of drawn chunks (*arithmetic*: 6 × 8 m); the
  code comment says about 144 m at r16. Placing with an at-range tier fills at range too
  (`Main.cs:5040-5066`).

### Damage and death: none

There is **no health, damage, death, respawn, drowning or fall damage** (grep: nothing). M9 ("the
mine kills you") shipped without them. Matthew sent them to M11 on 2026-09-13 (*"for debugging I
dont need it really"*; `docs/M9-PLAN.md:322`, `docs/DESIGN.md` M9 row). What M9 does to the player
today:

- A collapse whose rubble lands in the body's box triggers the unstick, which lifts the body at most
  4 m, up only (`Overburden.Core/Physics/PlayerSpawn.cs:42`). If that fails, the log says
  *"the player is buried … F is the door until M11"* (`Main.cs:6115-6154`). **Fly (noclip) is the
  escape.**
- A fall clears the undo stack (`Main.cs:4516-4520`, `EditHistory`).
- The collapse plays Matthew's phonk from `media/TETRIS_PHONK.mp3` (`Main.cs:936`). `media/` is
  gitignored (`.gitignore:32-34`), so the track exists only on his machine.

### The HUD shows

`Hud.cs` (a CanvasLayer at layer 10, `:97`) shows:

- A crosshair.
- The world name and `--seed=` in the upper right, with *"new world — note it down"*. It holds 7 s
  and fades over 3 (`Hud.cs:66-69`, `:156-166`).
- A "where" line: x, y, z, a compass bearing, and the relief in metres (`:496-507`).
- A status line naming the brush, the tier (T), the hotbar keys, Alt, Q and the pack's charge
  (`:509-607`).
- **A nine-slot hotbar that is a projection of the inventory in numeric-ID order**
  (`Hud.cs:41-47`, `VolumetricInventory.cs:203-211`). A tenth holding is reachable only with V.

There is no health, no power bar beyond the pack's term, and no machine readout.

---

## 4. The input map

**`project.godot` has no `[input]` section.** Every binding is a hard-coded `Key.` poll or event in
`Main.cs` or `FlyCam.cs`, and there is no rebinding. There are two styles:

- **Edges** in `Main._UnhandledInput` (`Main.cs:2721-2826`). These are suppressed wholesale while the
  machine panel is open (`:2733-2741`).
- **Polls** each frame, which act only while the mouse is captured: Q (`:3434-3443`), P (`:4613`),
  L (`:5477`), R and G (`:7645`), H (`:4568`).

The camera's own handler is `FlyCam.cs:182-206`.

| Key | Does | Where |
|---|---|---|
| W A S D | Walk or fly | `Main.cs:2699-2702`, `FlyCam.cs:230-233` |
| Space | Jump; fly up | `Main.cs:2711`, `FlyCam.cs:240` |
| Shift | Sneak; fly down | `Main.cs:2713`, `FlyCam.cs:241` |
| Ctrl | Sprint; fly ×5; with R, a 1 ML pour | `Main.cs:2712`, `FlyCam.cs:246`, `:7681` |
| Ctrl+Z | Undo one step | `Main.cs:2775` |
| Alt | One-voxel precision on both buttons; fly ×0.2 | `:2881`, `:5121`, `:5324`, `FlyCam.cs:247` |
| Left mouse | Mine | `Main.cs:5307` |
| Right mouse | Place; with the computer, open a machine's panel (or the pack's on empty space); with a pack, equip it | `:5115`, `:5167` |
| Middle mouse | Nothing (only part of the re-capture swallow) | `FlyCam.cs:216` |
| Mouse wheel | Nothing | — |
| Esc | Release the mouse; close the panel or the map | `FlyCam.cs:194`, `Main.cs:2735`, `:2765` |
| F | Walk / fly | `:2805` |
| T | Cycle the tool tier | `:2793` |
| B | Cycle the brush (cube, slab, ramp, sphere) | `:2781` |
| V | Cycle the held material | `:2787` |
| 1–9 | Hotbar slot | `:2799` |
| Q (hold) | Run the tier in hand, no click | `:3441` |
| P / Alt+P | Seed machines / fill every cell and the pack | `:4620`, `:4630` |
| L | Lamp at the crosshair | `:5483` |
| R / Ctrl+R | Pour 10,000 L / 1,000,000 L of water at the crosshair | `:7652`, `:708-715` |
| Alt+R, Alt+G | Take back all water within 2 m | `:7656-7676`, `:718` |
| H / Alt+H | Hold the water tick / full GC and a memory line | `:4575-4606` |
| F11 | Far mode: off → r8h/4v → r10h/6v | `:2759`, `:320` |
| F12 | The map | `:2747` |

**Free:** E, I, Tab, C, X, Y, U, O, J, K, M, N, Z (without Ctrl), Enter, the arrow keys, F1–F10,
the mouse wheel, and the middle button.

**Already claimed by plans:**

- **E**: tapped, the M10g inventory screen; held, the crank (M10-LADDER decision 11 and "The
  inventory screen", decision 1, `docs/M10-LADDER-PLAN.md:422-434`, `:2512-2533`; *"the key is
  Matthew's"*). `TapOrHold.cs` was built for this and currently has no caller.
- **The mouse wheel**: map zoom, recommended in MAP-NOTES decision 2 and not built.
- **I and Tab**: considered for the inventory screen and rejected (`M10-LADDER-PLAN.md:2521`).

**Known clashes:**

- **NVIDIA's overlay owns Alt+R on his machine.** His logs held eight R pours and no Alt+R take, which
  is why Alt+G exists (`Main.cs:7656-7658`).
- NVIDIA's shipped defaults also use Alt+Z, Alt+F1-F3 and Alt+F9/F10. That is general knowledge, not
  verified on his machine, so avoid Alt+letter for new binds.
- F12 is also the Steam overlay's screenshot key (`Main.cs:2844`).
- Godot's `LineEdit` consumes `ui_cancel`, so Esc can take two presses with a panel field focused
  (WORKLOG 2026-09-16, M10b-2).
- The Match-Minecraft rule (`docs/DESIGN.md:36`) argues for E as the inventory and creative palette,
  which is where M10g already puts the screen.

---

## 5. The UI that exists

Four files in `Overburden.Game/Ui/`:

- `Hud.cs` (608 lines).
- `MachinePanel.cs` (604): a CanvasLayer at layer 11 (`:98`), one machine's ports as `SpinBox`es,
  plus a status line. It is opened with the computer in hand (`Main.cs:3269-3300`), and also shows
  the equipped pack (`Main.cs:3219-3236`).
- `MapPanel.cs` (345): a plain `Control` added to `Main`, not a CanvasLayer (`Main.cs:3025-3033`).
- `MapWater.cs` (62): an adaptor from `WaterSystem` to Core's `IMapWater`.

**How UI is built:**

- **All of it is built in code.** There are no `.tscn` panels and no `Theme` resource: per-control
  colour overrides, `StyleBoxFlat`, and Godot's default font. The reasons are recorded at
  `Hud.cs:16-21` and `MachinePanel.cs:26-29`: the rows are per-material, and a panel in `Main.tscn`
  is one more thing a headless profile must instantiate.
- **The HUD reads state and never owns any** (`Hud.cs:23-29`).
- **A panel skips under `--profile` but builds in a plain headless run**, deliberately, as a smoke
  test (`Main.cs:2989-3008`). So any new screen is instantiated by the r6 control run.
- **There is exactly one mouse-releasing UI, and its handshake is the pattern.**
  `FlyCam.ReleaseMouse()` / `CaptureMouse()`; `MouseFilter.Stop` on the frame; a click outside
  re-captures, and `FlyCam.ClickLive` swallows that click (`FlyCam.cs:50-64`). Esc closes. Walking
  and flight are both gated on capture, because a key typed into a field still reads as pressed
  (`FlyCam.cs:37-47`). M10-LADDER's inventory-screen fact 4 says a full-screen inventory *"inherits
  all of it"*.
- **There is no stock tab, no inventory screen, no multi-machine list, no pause screen and no
  settings.** M10b shipped no stock tab because nothing declared stock (M10b decision 7).

**How much work the last two panels were** (commit times, so wall clock, *inference*):

- **MachinePanel**: M10b-1 committed 20:48 and M10b-2 at 22:36 on 2026-09-16. The panel's commit
  (`65b472a`) is 389 new lines in `MachinePanel.cs`, 204 in `Main.cs` and 57 in `FlyCam.cs`, for the
  capture rules. No Core change.
- **The map**: planning pass at 21:00 on 2026-09-23, slice d at 22:50: four slices in under two
  hours. It had a Core half (`MapProjection`, `MapRenderer`, `MapOverlay`, with tests) and a Game
  half. Slice b's commit (`f453628`) is 297 lines of `MapPanel.cs`. Slice c took 283 lines of
  overlay tests.
- **The expensive part both times was a silent failure, not the widgets.** The map "built once" was
  an exception caught inside a Godot callback (`docs/MAP-NOTES.md:350-372`).

### F12's map today, and how it would take telemetry

- **What it draws** (`MapPanel.cs`):
  - 128 × 128 px of analytic terrain at 16 m a pixel (2 km, one region; `--mapscale=` sweeps it),
    shown at 3× and turned facing-up.
  - A north tick and the player arrow (`:310-344`).
  - Three overlays from `Main.MapOverlayNow` (`Main.cs:2946-2987`): dug chunk columns, water with a
    rim where Tier A stops knowing, and **one pin per machine in the material's texture colour**.
- **When it rebuilds**: at most 15 times a second on movement or turning, and at least once a
  second while open (`MapPanel.cs:53-71`). A build is about 3.5 ms (`:40-44`, measured in slice a).
- **It is an overlay, not a mode.** The world keeps running and the mouse stays captured.
- **The pins are already the machine half of "map telemetry" without the telemetry.**
  - `MapPin` is `(VoxelX, VoxelZ, Colour)` and nothing else (`Overburden.Core/Navigation/MapOverlay.cs:7`).
  - `MachineSet.Rows` is every row in the world, restored whole at open (`docs/MAP-NOTES.md:44-45`),
    so pins are free at any range.
  - At 16 m a pixel one pin is one pixel. A 127-machine yard renders as a patch of colour with one
    dark rim, drawn in two passes so that pins don't black each other out
    (`Overburden.Core/Navigation/MapRenderer.cs:466-515`).
- **To show state** (*inference*), there are two routes. Either `MapPin` gains a state field, or
  `MapOverlayNow` picks the colour by state (stalled or unpowered, say) instead of by material.
  Labels, a legend or a hover readout would go in `MapPanel._Draw`, which has the projection
  (`_shown`) to place them. Two things must hold:
  - Every overlay must pass the bearing-independence oracle
    (`TheSameGroundIsTheSameColourWhicheverWayYouFace`, `MapOverlay.cs:40-44`).
  - The overlay is gathered only inside the rebuild decision, never per frame (MAP-NOTES slice d).
- **A per-channel filter or a clickable machine list** needs the map to take the mouse. That turns it
  from an overlay into a second mouse-releasing screen.

---

## 6. What a creative mode must not break

### Persistence

- **The header is v7** (`WorldHeader.cs:187`). It holds: settings, the material ID table, the
  inventory in voxels, the clock, the watershed lattice, the player (position and flying), the
  equipped pack, and the carried packs' charges (`:31-112`, written by `Main.cs:9104-9116`). **There
  is no mode field.**
- Adding one is a v8 bump. The policy is that an additive version migrates and a version that changes
  a field's meaning is refused (`:189-225`). A test asserts the settings byte count, so a forgotten
  field goes red (`:159-165`).
- The M10g hotbar assignment is also queued for "the next version" (`M10-LADDER-PLAN.md:2535-2551`).
  **Two features want the next header number.**
- The machine table and the header are two store calls. A-1 put them in one batch, and constraint 4
  of the brief needs decision 5's overload before any inventory-to-container transfer.
- **The inventory is saved as a count, with no provenance.** A creative stock opened later in survival
  keeps every voxel. `--stock=` already does exactly this to a played world today.
- **Not saved**: the held tier, the brush, the selected material, `--nogate`, and every other flag.
- **Survival world opened in creative**: nothing in the header would know. Its inventory, pack and
  machines are just there.

### The undo

- The journal hangs off `SetVoxel` and `MachineSet` (CLAUDE.md, "An observer sees every writer";
  `Main.cs:4197`). Any creative writer is one more writer the journal sees, and it must not journal
  the undo's own writes.
- **The undo moves inventory both ways.** Undoing a dig debits what the dig credited, and it is
  refused whole when the inventory is short (`UndoOutcome.OutOfStock`, `Main.cs:4521-4525`). So a
  creative mode that stops mining from crediting, or places without debiting, breaks the undo's
  bookkeeping. **Infinite stock has to be a real count or a counted term, not a skipped debit.**
- A fall clears the stack.

### Conserved quantities

These are the doors a creative power would add to:

- **Water**: `held == injected − removed − displaced`. R and Alt+R are the printed `injected` and
  `removed` terms (`Main.cs:7638-7642`).
- **Power**: the pool identity has a `debug` term for `Alt+P`. The precedent is `COMMANDS.md:954-959`:
  a pack filled with a bare `Give` *"broke the pool identity by 3,593 kJ on every boundary for a
  session (1,403 error lines)"*. **Creative infinite power must be a printed term in that identity.
  `--nogate` is not the model**, because it zeroes generation and would hide every power feature the
  demo is meant to show.
- **Collapse**: voxels are conserved through falls. Rubble is a real material, and a fall credits
  nothing.
- **Inventory**: no conservation identity is printed today. `P` and `--stock` are unprinted doors.
  M10g's planned *"player + stocks constant over a thousand random steps"* will need creative's source
  to be a term the day it lands (*inference*).

### The r6 control and the oracles

- **The r6 control** (`--headless --quit-after 2400 -- --nosave`, reading `2601 / 325 / 3446 at 411`)
  is a plain headless run. It is not a profile, so it builds the HUD, the panel and the map, spawns
  **walking**, and uses the fixed seed with the default r6h/2v box. 2601 is the 17 × 17 × 9 chunk box
  at margin 2 (*arithmetic*).
  - Any default a creative mode changes for a plain run could move it: spawning flying (which places
    the camera 2 m above the surface, `Main.cs:8686-8699`, against the walk eye at 1.65 m), a
    different view radius, or a new node doing work at open.
  - **The house rule is that a measurement keeps the constant** (`Main.cs:8971-8981`). A creative
    default must be off under `--profile` and `--nosave` too, or be proved not to move r6.
- **Oracles in COMMANDS.md that lean on today's defaults:**
  - Gate on: `--pack --charge=320 --mine --entangle` reads `removed 6, unpowered 57 of 64`
    (`COMMANDS.md:959-970`).
  - Empty inventory at open, with `--place` picking the largest holding (`:806-808`, `:987-989`).
  - Steel pick in hand for a bare `--mine`.
  - The pump rigs' pool identity with the pool on (`:908-911`).
  - The collapse oracles with pieces as default, and `--collapsemode=sweep` for anything recorded
    before 2026-09-27.
- **Measure a played world only on a copy** (CLAUDE.md). CLAUDE.md's `--quit-after 900` verify run
  has no `--nosave` and **writes into the `default` world**.

---

## 7. The first five minutes today, reconstructed

On a **new world** (`start.cmd --world=Demo`, since bare `start.cmd` opens TajMahal):

1. **0–2 s.** The console prints the startup lines. `FlyCam._Ready` prints a key list
   (`FlyCam.cs:125-139`). **That list is stale**: it says Q "toggles the entangled miner … 40
   swings/s" and that P gives "a relay pump, a relay outlet and a computer". It is the only help text
   in the game, and only in the console window.
2. **About 1.5 s.** The world draws at the adaptive floor and grows ring by ring to r6h. The body is
   held until its chunks exist, then stands on grass at x 15, z 15.
3. **The seed banner** sits top right for about 8.5 s, then fades (COMMANDS `:1030-1036`).
4. **The bottom of the screen** shows the where-line, `cube brush (B) · steel pick (T) · 1-9
   material, V cycle · Alt: one voxel · hold Q: run`, and nine empty slots showing "—".
5. **The player has** an empty inventory, the steel pick, no pack and no machines, and no hint of P
   except in the console.
6. **Left mouse** carves 0.5 m spheres of grass, dirt and stone at 5/s, and the hotbar fills in ID
   order. The first stone shows up in some slot after dirt. **Right mouse** places the selected
   material with the cube brush at 5 m.
7. **Water.** It rains or not, by seed and clock, at 20× depth. Basins fill as it rains; a new world's
   stored basins start empty (*inference*: the header stores volumes, and "the first forty seconds
   of the profile seed are dry", `Main.cs:8974`). R pours water on demand.
8. **T to a quantum tier.** The status line says `QUANTUM MINER TAKES NOTHING — no pack · P seeds
   one…`. Press P and the hotbar fills and overflows. In a fresh world's ID order, **stone and the
   vacuum cell fall off the row** (*inference*: 11 holdings over 9 slots, IDs assigned in ordinal
   file order `IContentSource.cs:54`; the same overflow is recorded on his world in the M10g
   section, fact 2). Then: select the pack, right-click to equip it (it is empty), Alt+P to fill it,
   hold Q, and dig at 1 m cubes out to the loaded edge for about two minutes.
9. **Machines.** A pump placed in a hole and an outlet on the surface, both on channel 0, move water
   once a cell or panel feeds the channel. The computer in hand, right-clicked on a machine, opens its
   ports.
10. **Nothing in the first five minutes can hurt the player**, and nothing says what the game is
    about. A roof can come down: it is tinted first, then falls as cyan-glitched Tetris pieces to the
    phonk, but only where the digging undermined it.

---

## Traps

1. **"The game starts with `start.cmd`" means TajMahal**, Matthew's played world, since
   2026-09-27. A demo flow tested with `start.cmd` tests his world, and a probe of it without a copy
   writes to it.
2. **`--nogate` is not creative power.** It zeroes generation and stops all charging
   (`MachineTick.cs:534`), so every power feature, and the one-pool vault the brief asks for, would be
   invisible under it.
3. **`--stock=` is a permanent gift, not a mode.** It is applied after the restore, saved with the
   inventory, and it accumulates on every launch that passes it. It also stocks rubble and the lamp.
4. **Fly is noclip, not creative flight.** It is the measurement instrument (`--profile` implies it;
   WORKLOG 2026-08-30, *"Fly mode is a measurement instrument, not a creative mode"*). It is also
   today's only escape from being buried. Gating fly on a mode takes that door away until M11's death
   exists.
5. **A bare flag before the `--` does nothing and says nothing.** Only `name=value` shapes are
   warned (`Main.cs:1692-1700`).
6. **Changing a default moves oracles.** The r6 control is a plain headless run that builds the UI
   and spawns walking. A creative default (spawn flying, stock at open, quantum tier in hand, gate
   off) must be off in measurements, or r6 must be shown unmoved.
7. **Core's null-inventory `Place` is not a Game path.** `PlaceController` requires an inventory
   (`PlaceController.cs:50`) and will not select a material the inventory lacks (`:498`, `:908`).
   The hotbar is the inventory. **A creative palette of everything needs a selection that is not the
   inventory**, or a real stock.
8. **The undo refuses when the inventory is short.** A creative mode that skips the debit or the
   credit desynchronises the undo's `Debited`/`Credited` and its OutOfStock rule.
9. **Every door into a conserved quantity is a term.** `Alt+P`'s `Give` once cost 1,403 error lines.
   Creative water, power or stock goes through the existing terms (`injected`, `debug`) or adds new
   printed ones.
10. **The header's next version is contended.** The M10g hotbar assignment wants it, a mode field
    would want it, and the one-pool vault may too. The number is a counter, but the migrations stack.
11. **The console help text and a doc comment are stale.** `FlyCam.cs:125-139` describes retired Q
    and P behaviour, and `Main.cs:2853-2858` still says the number keys are the tool tiers. Don't
    design against them.
12. **`--speed=` in a played run hijacks the camera.** It starts a scripted flythrough and refuses
    walk mode, even without `--profile` (`Main.cs:3641`, `:2810-2815`). That is useful for an attract
    reel, and surprising otherwise.
13. **Keys that look free but are spoken for**: E (M10g's screen and crank, Matthew's key), the mouse
    wheel (the map zoom recommendation), Alt+letter (NVIDIA). T, B, V, P, L, R, H, Q, F, G, 1–9,
    F11 and F12 are taken. Q is a hold.
14. **Nothing machine-shaped exists below quantum.** Every shipped machine is `via: channel`. A demo
    "working down the ladder" has no steam, DC or AC content to show yet.
15. **The demo cannot be handed to anyone yet.** There is no export preset, the SQLite natives in an
    export are unverified, and the collapse music is his gitignored file at a relative path
    (`Main.cs:936`).
16. **Machine pins carry no state.** "Map telemetry exists" is half true: the pins show where
    machines are, but not whether they work.
