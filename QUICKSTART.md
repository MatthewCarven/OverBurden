# Overburden — quick start

You run a mine, and the mine is the enemy. Rain fills the low ground, water finds whatever you dig, wet
rock weakens, and an over-dug roof comes down. You fight back with pumps, power and what you build.
There are no monsters. **It's an early alpha: far from complete, and things will change.**

## The first five minutes

1. **Look around** with the mouse and **move** with W A S D. You start on foot (**F** toggles flying).
2. **Dig** with the left mouse button. Your bare-hands pick takes dirt, grass and clay, but not stone.
3. **Press P** to get a kit: pumps, outlets, solar panels, power cells, clay, glass, computers and
   battery packs, 100 of each. (P is a test key, and it's off in a creative world, which starts with a kit
   of its own: see *A creative world* below.)
4. **Wear a pack.** Press **V** until the **lithium ion pack** is in your hand (the kit's blocks and
   machines fill your hotbar; the computer and packs are a V away), then **right-click**. Press
   **Alt+P** to charge it.
5. **Press T** to switch to the quantum miner and dig stone at range. **Hold Q** to keep digging.
   The tunneller (2 m) and the excavator (a whole 8 m chunk) are further along T.
6. **Build** with the right mouse button, **B** to change the brush shape.
7. **Esc** opens the menu: Resume, Controls and Quit. **F1** shows every key. The world saves when you
   quit, and every 30 seconds. The game doesn't pause while the menu is open.

A line above the hotbar says what a key just did, or why it did nothing.

## A creative world

A creative world starts with a kit and has the test keys off. Make one by naming a world that doesn't exist yet
and adding `--mode=creative` after a bare `--`: from the source, `start.cmd "--world=My Creative" --mode=creative`;
from the alpha download, `Overburden.exe -- "--world=My Creative" --mode=creative` (a shortcut's Target can carry
it). A world's mode is chosen when it's made and never changes; `--mode` on a world that exists is ignored.

- **The kit**, once, in your pocket: two vacuum cells, eight solar panels, four pumps and four outlets, one
  quantum vault of each grade and a crate, the computer, and 4,096 m³ each of stone, clay and glass. The hotbar
  holds stone, clay, glass, the cell, the panel, the pump, the outlet, the high vault and the crate.
- **You wear the quantum backpack** on channel 0: a window onto that channel's bank and store.
- **The first vacuum cell you place arrives holding 1 GJ**, once. Undo that placement and the gift goes with it.
- **The test keys are off**: P, Ctrl+P, Alt+P, R, Alt+R, Alt+G, L, Ctrl+R, H and Alt+H each say so and do nothing,
  `--dev` or not. Power is real: everything you own runs off that one bank, so don't go broke.

## All the keys

### Moving

| Key | On foot | Flying |
|---|---|---|
| W A S D | walk | fly where you look |
| Space | jump | up |
| Shift | sneak (you won't walk off an edge) | down |
| Ctrl | sprint | fast (×5) |
| Alt | | slow (×0.2) |
| F | switch to flying | switch to walking |
| Mouse | look | look |

Flying passes through rock.

### Digging and tools

| Key | What it does |
|---|---|
| Left mouse | dig with the tool in hand |
| Alt + left mouse | dig a single small block |
| Hold Q | keep digging with the tool in hand, no clicking |
| T | next tool: hand pick, quantum miner (1 m cube), quantum tunneller (2 m cube), quantum excavator (whole 8 m chunk), then the three quantum siphons in the same sizes |

The hand pick is free and takes the soft ground. The quantum tools take anything, at range, but each
block costs energy from the pack you're wearing (50 J a small block). With no charged pack they take
nothing. A **siphon** takes water into your store with the left mouse (1 kJ a litre, from the bank) and pours
it back free with the right; it needs the quantum backpack.

### Building and the hotbar

| Key | What it does |
|---|---|
| Right mouse | place the selected material in the brush's shape (at range with a quantum tool) |
| Alt + right mouse | place a single small block |
| B | next brush: cube, slab, ramp, sphere |
| 1 – 9 | select a hotbar slot |
| V | step through everything you carry, including what's not on the hotbar |
| V held + 1 – 9 | put what's in your hand in that slot (it stays there, saved with the world) |
| Ctrl+Z | undo (the last three steps; a collapse clears it) |

Building materials show how much you hold: litres under a cubic metre, cubic metres (m³) above. Machines and
items show a count (× 100).

The hotbar's nine slots keep what you put in them. Something new goes into the first empty slot (blocks and
machines; the computer and packs only when you put them there), and a slot you've used up stays, dimmed, so
the next of it lands back where it was. When the row is full, new things are still carried: V reaches them.

### Power and packs

| Key | What it does |
|---|---|
| Right mouse, a pack in hand | wear it (the one you were wearing goes back in your pocket, with its charge) |
| P | a kit: 100 of every machine, material and pack (a test key) |
| Ctrl+P | one empty lithium-ion pack, in your hand, unless you're already wearing one (a test key) |
| Alt+P | fill every placed power cell, and the pack you're wearing, to full (a test key) |

The **lead-acid pack** holds 3.6 MJ, the **lithium-ion pack** 14.4 MJ. A worn pack recharges slowly
from charged **vacuum cells** on its channel (channel 0 unless you change it). **Solar panels** charge
cells under open sky (there's no night yet), less under cloud.

### Machines

| Key | What it does |
|---|---|
| Right mouse, computer in hand, aimed at a machine | open its panel: channel, on/off, priority |
| Right mouse, computer in hand, aimed at nothing | open your worn pack's panel |
| Esc | close a panel |

A **relay pump** takes water from where it stands, and a **relay outlet** on the same channel lets it out
somewhere else, with no pipe between them. Both run on the channel's power. Place them like any material.
To pick one back up, dig it (a quantum tool takes it; the hand pick can't).

### Water (test keys)

| Key | What it does |
|---|---|
| R | pour 10,000 litres where you're looking |
| Alt+R or Alt+G | take back the water within 2 m of where you're looking |
| L | place a free lamp |

### The view and the game

| Key | What it does |
|---|---|
| Esc | the menu: Resume, Controls, Quit (and it closes a panel or the map) |
| F1 | every key, on screen |
| F10 | see further: each press raises the view distance a step, then back to normal |
| F11 or Alt+Enter | fullscreen |
| F12 | the map (Esc closes it) |

### Developer keys

Only when the game is started with `--dev` (`start.cmd` passes it; the alpha download doesn't):

| Key | What it does |
|---|---|
| Ctrl+R | pour 1,000,000 litres (Ctrl is also sprint, and there's no undo) |
| H | hold all the water, and pumps, rain and roofs with it; H again releases. A line on screen says so |
| Alt+H | a full memory clean-up, its time in the log |

## What to watch for

- **Roofs fall.** Dig a wide room, or dig under water, and the rock over it can fail. Rock under strain
  shows a tint, and a roof that's going to fail comes down 10–20 seconds later, in pieces that glitch
  cyan, with music. Leave pillars. What falls is rubble.
- **Water goes where it can.** A hole under a lake fills. Pumps and outlets move it; glass and clay hold
  it back.
- **Alt changes what a key does:** Alt+R takes water back, Alt+P fills your power, and Alt with either
  mouse button works one small block. **So does Ctrl on P:** Ctrl+P gives one pack, not the kit, even if
  you were only holding Ctrl to sprint.

## Saves and logs

The alpha download keeps everything in `%APPDATA%\Overburden Alpha\` (paste that into File Explorer's address
bar): your worlds in `worlds\` (you start in `worlds\default\`) and the log in `logs\godot.log`. If something
goes wrong, the log is what helps. (Running from the source with `start.cmd` uses
`%APPDATA%\Godot\app_userdata\Overburden\` instead.)
