# The map — a planning pass

Matthew, 2026-09-23, after being handed a compass bearing to a mountain he had no way to follow:

> *"um but I might make this part of the game el official like how do you feel about f12 opening a
> map 128x128 pixels facing your direction like? How do you feel about this? Plan with me please?"*

**How I feel about it: it is the right feature and it arrives at the right moment, and it is more
than a convenience — it is the instrument the relief work is currently missing.** Regions are 2 km
across and the view is 48 m. That is a factor of forty between the scale of the thing and the scale
of the window you look at it through, so *nothing about regions can be judged from inside the game
today*. The position readout fixed navigation; a map fixes perception. Without one, the honest
description of the shipped shape is "a sweep says it is 53% flat", which is not a thing anybody can
agree or disagree with.

The second reason it is right is that **this game's map has something to draw that most games' maps
do not**: your mine. A terrain map is a nicety. A map showing where you have dug, where your pumps
are, and which way the water went is the operations board for a mining company, and that is what
this game is about.

---

## Seven facts, from the code

**1. The terrain is analytic, so a map needs no streaming, no margin and no barrier.**
`ISurfaceHeights.SurfaceHeightAt` is a pure function of two coordinates and answers anywhere in the
world, loaded or not. This is M7a's finding a second time — the watershed needed no chunks either —
and it is the single thing that makes a whole-world map cheap. **A 128 × 128 map is 16,384 samples
at roughly 70 ns each: about 1.2 ms**, on the sim thread, once.

**2. That cost does not change with zoom.** 128 × 128 samples is 128 × 128 samples whether each
pixel is 8 m or 8 km. A map of the whole continent costs exactly what a map of the next valley
costs, which is an unusual and pleasant property to have for free.

**3. Rotation is free, and better than free.** Because the function takes arbitrary coordinates,
sampling along a rotated axis costs nothing — no texture rotation, no resampling, no blurring, no
black corners. A facing-up map is *exactly as sharp* as a north-up one. Most engines cannot say
this.

**4. Where you have dug is already in memory.** `IWorldStore.LoadChunkIndex()` and
`LoadColumnIndex()` are read once at open and kept, so the set of edited chunk columns is a
dictionary lookup away at any distance. **The mine's footprint costs nothing to draw.**

**5. So are the machines.** `MachineSet.Rows` is every row in the world, restored whole at open,
each with an origin cell. Pins for pumps and outlets are free at any range.

**6. Water is the one thing that is not available at range, and this is the real constraint.**
Tier A analyses a 512 m window centred on the player; outside it no basin exists and no lake level
is known. A map wider than 512 m therefore *cannot* draw lakes without re-running the watershed —
13.6 ms Release per window at the 2 m cell, on a worker. **The map can show terrain to the horizon
and water only nearby, and it has to say which.**

**7. `F12` is free and the HUD is already skipped under `--profile`.** Only `Key.F` is bound today,
and a map built with the HUD is automatically absent from every measurement, the way
`MachinePanel` is.

---

## Twelve decisions

Each has a recommendation. Ten of them I would take as recommended; **2 and 4 are yours**, because
one is a look you have to see and the other is about what kind of game this is.

**1. What the picture is made of.** Terrain *height* as a hypsometric ramp (low green → high
brown → highest pale), with a cheap hillshade from the height difference to the north-west
neighbour. *Recommended.* The alternative, colouring by local relief, sounds more apt for finding
mountains and is worse: a high flat plateau and a low flat plain come out the same colour, and you
cannot tell a valley you can walk out of from one you cannot. Height plus shading reads as
landscape at a glance and costs one extra sample per pixel.

**2. Scale, and whether it zooms.** 1 px = 16 m, so 128 px is **2 km — exactly one region
across**. *Recommended*, with zoom on the mouse wheel between 4 m/px (512 m, the water window) and
64 m/px (8 km, several regions). **The default is yours**: one region across is the fit for "which
way are the hills", but you may find you want the wider view more often. Cost is flat across the
range (fact 2), so this is purely about what reads well.

**3. Facing-up or north-up.** **Facing-up, with a north tick on the rim**, which is what you asked
for and I think you are right: for walking to something, "it is up and a bit left" is an
instruction, where north-up makes you do the rotation in your head every time. The tick and the
coordinate readout keep the absolute frame available. *Recommended.*

**4. Fog of war — does the map show only where you have been?** **No: draw the terrain
everywhere.** *Recommended, and this one is yours to overrule.* The argument: this game's antagonist
is the mine, not the wilderness — no mobs, no combat, and DESIGN's *"ideally this game will never
end"* — so there is nothing out there to be ambushed by and nothing gained by hiding it. Against
that, exploration-gating needs per-world stored state (a visited bitset per chunk column, a new
table, a header bump) for tension the game does not otherwise have. **Revisit trigger, named: the
first time the world contains something worth *finding* rather than worth *reaching*** — ores and
the rest of WORLDGEN-NOTES. Then a map that shows you where the copper is has given away the game,
and fog earns its storage.

**5. Free, or crafted on the ladder?** **Free on F12 now.** *Recommended.* M10's tiers are about
capability and a surveyor's map is a plausible tier reward — but navigation is *impossible* today,
and gating the fix behind a tech tree blocks the play checks the map exists to enable. **Revisit
trigger: M10g's crafting exists and the map is the kind of thing you want to earn.** It would
become an item like `overburden:computer`, which M10b already built the shape for.

**6. Your mine, drawn.** **Yes — one mark per edited chunk column, from fact 4.** *Recommended.*
This is the most useful thing a map in *this* game can show and it costs a lookup. Drawn as a
darkening rather than a colour, so it reads as "disturbed ground" over the terrain rather than
replacing it.

**7. Machines as pins.** **Yes, from fact 5.** *Recommended.* A pump you cannot find is a pump you
cannot fix, and you have already lost one outlet to that (*"the water processing cannot keep up"*,
2026-09-19, a far outlet nobody could see). This is what turns a terrain map into an operations
board.

**8. Water.** **Draw it where Tier A knows about it and mark where that stops** — a subtle ring at
the analysis window's edge — rather than drawing nothing or re-analysing. *Recommended.* The
alternative makes the map a simulation input: a watershed run to draw a picture is 13.6 ms and a
worker, and it would mean opening the map changed what the water does.

**9. When it samples.** **On open, and again when you have moved one pixel's worth**, never per
frame. *Recommended.* 1.2 ms occasionally is free; 1.2 ms at 60 Hz is 7% of a frame for a picture
that has not changed.

**10. Where the code lives.** **`Overburden.Core/Navigation/` for the projection and the sampling,
`Overburden.Game/Ui/MapPanel.cs` for the texture and the key.** *Recommended.* Same split as
`MachinePanel`, and it means the interesting half — which world coordinate a pixel is, which pixel
a machine lands on, what happens at a rotation of exactly 45° — is testable without Godot.
`Compass` is already there and already holds the convention.

**11. Does it pause?** **No.** *Recommended.* It is an overlay; the world keeps running and Esc or
F12 closes it. A pause would be the first in this game, and the water, the collapse timers and the
machine tick would all have to agree about what it means — which is a decision about the
simulation, dressed up as a decision about a window.

**12. Size on screen.** **×3 nearest-neighbour, 384 × 384 centred.** *Recommended.* 128 real pixels
is a postage stamp on a 1152-wide window, and nearest-neighbour keeps it crisp rather than turning
a deliberate pixel map into a blurry one.

---

## What I would build, and in what order

**a — the projection, in Core.** Pixel ↔ world coordinate under a rotation, the sample grid, the
hypsometric ramp and the hillshade as pure functions over `ISurfaceHeights`. Tested: a pixel round
trips to itself, the centre pixel is the player, north-up and facing-up agree when facing north,
rotation by 90° maps east to up, and a map of a uniform world is flat everywhere. **No Godot, no
texture, no key.**

**b — the panel.** `MapPanel`, the `ImageTexture`, F12, the ×3 scale, the north tick, the facing
wedge, the sample-on-move rule, and the `--map` flag for a screenshot.

**c — the overlays.** The mine from the chunk index, the machines from `MachineSet`, the water and
its edge ring.

**d — the play.** One sentence: can you find the hills in `Regions10` now, and does the shipped
relief shape read right once you can see it?

Three sessions, and **a** is worth doing alone because everything visual rests on the projection
being right — a map that is subtly rotated the wrong way, or off by half a pixel at the centre,
looks exactly like a map.

---

## The one thing I would watch

**A map is an instrument before it is a feature, and instruments change what you measure.** Once
you can see 2 km of terrain at a glance, the relief shape will read differently than it does from
inside it — probably *better*, because the regions become visible. That is a real improvement and
also a reason to take the map's verdict on `--reliefbase` / `--relief` / `--reliefrare` as a
*second* opinion rather than the final one. **The thing being tuned is how the world feels to walk
through, not how it looks from above.**
