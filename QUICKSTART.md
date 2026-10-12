# Overburden — quick start

You run a mine, and the mine is the enemy. Rain fills the low ground, water finds whatever you dig, wet
rock weakens, and an over-dug roof comes down. You fight back with pumps, power and what you build.
There are no monsters. **It's an early alpha: far from complete, and things will change.**

## The first five minutes

1. **Look around** with the mouse and **move** with W A S D. You start on foot (**F** toggles flying).
2. **Dig** with the left mouse button. Your bare-hands pick takes dirt, grass and clay, but not stone.
3. **Press P** to get a kit: pumps, outlets, solar panels, power cells, clay, glass, computers and
   battery packs, 100 of each. (P is a test key; the alpha's creative mode will replace it.)
4. **Wear a pack.** Choose a **lithium ion pack** on the number keys (or press **V** until it's
   selected), then **right-click**. Press **Alt+P** to charge it.
5. **Press T** to switch to the quantum miner and dig stone at range. **Hold Q** to keep digging.
   The tunneller (2 m) and the excavator (a whole 8 m chunk) are further along T.
6. **Build** with the right mouse button, **B** to change the brush shape.
7. **Esc** frees the mouse; click the game to take it back. To quit, close the window (Alt+F4). The
   world saves on quit and every 30 seconds.

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
| T | next tool: hand pick, quantum miner (1 m cube), quantum tunneller (2 m cube), quantum excavator (whole 8 m chunk) |

The hand pick is free and takes the soft ground. The quantum tools take anything, at range, but each
block costs energy from the pack you're wearing (50 J a small block). With no charged pack they take
nothing.

### Building and the hotbar

| Key | What it does |
|---|---|
| Right mouse | place the selected material in the brush's shape (at range with a quantum tool) |
| Alt + right mouse | place a single small block |
| B | next brush: cube, slab, ramp, sphere |
| 1 – 9 | select a hotbar slot |
| V | step through everything you carry, including what's past slot 9 |
| Ctrl+Z | undo (the last three steps; a collapse clears it) |

Building materials show how much you hold: litres under a cubic metre, cubic metres (m³) above. Machines and
items show a count (× 100).

### Power and packs

| Key | What it does |
|---|---|
| Right mouse, a pack in hand | wear it (the one you were wearing goes back in your pocket, with its charge) |
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
| **Ctrl+R** | **pour 1,000,000 litres. Careful: Ctrl is also sprint, and there's no undo** |
| Alt+R or Alt+G | take back the water within 2 m of where you're looking |
| H | **freeze all water** (press again to release; nothing on screen says it's frozen) |
| L | place a free lamp |

### The view

| Key | What it does |
|---|---|
| F10 | see further: each press raises the view distance a step, then back to normal |
| F11 or Alt+Enter | fullscreen |
| F12 | the map (Esc closes it) |
| Esc | free the mouse |

## What to watch for

- **Roofs fall.** Dig a wide room, or dig under water, and the rock over it can fail. Rock under strain
  shows a tint, and a roof that's going to fail comes down 10–20 seconds later, in pieces that glitch
  cyan, with music. Leave pillars. What falls is rubble.
- **Water goes where it can.** A hole under a lake fills. Pumps and outlets move it; glass and clay hold
  it back.
- **Modifier keys change things:** Ctrl+R is the million-litre pour, and Alt changes what R, H and P do.

## Saves and logs

The alpha download keeps everything in `%APPDATA%\Overburden Alpha\` (paste that into File Explorer's address
bar): your worlds in `worlds\` (you start in `worlds\default\`) and the log in `logs\godot.log`. If something
goes wrong, the log is what helps. (Running from the source with `start.cmd` uses
`%APPDATA%\Godot\app_userdata\Overburden\` instead.)

---

*Coming next* ([docs/ALPHA-PLAN.md](docs/ALPHA-PLAN.md)): an Esc menu with Quit and this list on F1; the
riskiest test keys (Ctrl+R, H, and Alt+H, which forces a memory clean-up) moved behind a `--dev` option;
hotbar slots you arrange yourself.
