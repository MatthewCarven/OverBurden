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

---

## Slice a — built, 2026-09-23

**Matthew took the two open decisions: 2 km across, and no fog of war.** The rest went as
recommended. `Overburden.Core/Navigation/` now holds `MapProjection` (pixel ↔ world under a
rotation) and `MapRenderer` (the sample grid, the hypsometric ramp, the hillshade), both
Godot-free. Suite 1,772 → 1,796.

**The oracle is that a place's colour does not depend on which way you are facing.** That is what
"this is a picture of the world" means, and it catches a mirrored axis, a rotation applied twice,
a rotation applied to the heights but not the shading, and — the one that would have survived every
screenshot — a hillshade lit from the screen rather than from the ground, which would move every
shadow as you turned. It is an *equality* rather than a tolerance because quarter turns permute the
pixel grid exactly, and a second test pins that so the first cannot quietly become approximate.

**The light is world-fixed and it cost nothing.** The map is a rigid rotation of the world, so the
world-space gradient is the image-space gradient rotated by the same amount: three multiplies, no
extra samples. A one-pixel apron gives the edge pixels the neighbours the gradient wants, for 3%.

**Ten break-recipes, ten caught — but only after three of them exposed real gaps and two of the
recipes turned out to be the thing that was wrong.** The first run read three greens. One was a
recipe that changed nothing (a comment, not code). One was an *identity in practice* — `Render`
clamps before `Ramp`, so `Ramp`'s own clamp is unreachable from there — closed by testing `Ramp`
directly, which the overlays slice will do for its legend. The third was a genuine hole: every test
passed a sea level of zero, so dropping the term entirely was invisible. And correcting the apron
recipe surfaced a fourth thing nobody had tested — **a constant one-pixel offset between the render
and the projection**, which every other test is blind to because they all compare the render
against itself; `TheCentrePixelShowsTheGroundThePlayerIsStandingOn` closes it with the shading
turned off so the boundary is exact.

**The stale-build trap caught this pass too.** `shutil.copy2` preserves mtimes, so restoring the
files after the battery left the incremental build holding the *last mutant* — and the sea-level
test then failed against code that was already correct. The battery touches what it restores now.

**Cost, measured on the real generator, and the estimate was three times out.** A 128 × 128 map is
**3.5 ms on a shaped world** and 1.8–2.0 ms on a uniform one; the pass said 1.2 ms, from the old
55 ns/sample figure taken before the relief field added a second noise evaluation. **Fact 2 holds
exactly as claimed**: 3.46 ms at 2 km, 3.58 ms at 8 km, 2.17 ms at 131 km — the cost really does
not change with the zoom.

**What that moves: decision 9 stops being tidy and becomes required.** 3.5 ms is nothing once, and
21% of a frame at 60 Hz, so "sample on open and when the player has moved a pixel's worth" is now
the thing standing between the map and a visible cost rather than a preference. **Named lever, not
built**: the renderer is pure and Godot-free precisely so it can go on a worker if that is ever not
enough.

**Slice b next** — `MapPanel`, F12, the ×3 nearest-neighbour scale, the north tick, the facing
wedge, the sample-on-move rule and a `--map` flag for the screenshot.

---

## Slice b — built, 2026-09-23

`MapPanel` in `Overburden.Game/Ui/`: F12, x3 nearest-neighbour, the north tick, the facing arrow,
the rebuild rule, and `--map` / `--mapscale=<metres>` for the screenshot. Esc closes it. Suite
1,796 -> 1,799; r6 control to the digit.

**Three things the pass did not know, all found by looking at the picture.**

**1. The ramp belongs to the map, not to the world.** The first screenshot was a uniform brown
smear. Handing the ramp the world's peak amplitude is the obvious thing and it is wrong: a shaped
world runs 19..144 voxels of amplitude, so an ordinary patch of it uses about a third of the
palette, centred -- measured at 32%. `MapRamp.Auto` fits the ends to the ground in view, which is
how every physical atlas has always done it. The panel prints the range it settled on, because the
honest cost of an auto ramp is that two maps are not comparable by colour.

**2. Turning is the expensive case, and the pass never considered it.** A facing-up map is a
different picture at every yaw, and yaw moves continuously whenever anybody looks around. So the
rebuild rule is not just "when you have moved a pixel" but also "when you have turned a degree" --
one degree moves the outermost pixel by about one -- with a 15 Hz cap over both, which is what
bounds the cost while somebody spins on the spot.

**3. The map aliases, and the fix is a filter it was already paying for.** The terrain's four
octaves have periods of 62, 31, 16 and 8 m, so at the shipped 16 m a pixel the top two fold
straight into pixel hash. The second screenshot was noise. Sampling a two-pixel apron and
box-filtering it down to one costs 6% more samples and removes most of it.

**And the scale is bounded by that, which is the finding worth keeping.** Measured by screenshot at
three scales on `Regions10`:

| scale | across | reads as |
|---|---|---|
| 4 m a pixel | 0.5 km | **landscape** -- hills and valleys, unmistakable |
| 16 m a pixel | 2 km | busy texture; structure and region-scale banding visible |
| 64 m a pixel | 8 km | aliased mush, and the fitted range collapses to +/-4 m |

**Coarser does not calm the map, it aliases harder** -- the box filter averages neighbouring pixel
samples and cannot recover detail finer than the pitch it samples at. So **zooming out is not free
after all**, and slice c's zoom should either stop at about 16 m a pixel or pay for supersampling,
which is N squared in the factor and would cost 56 ms at 64 m. **Stopping is the recommendation**:
past that the picture shows features that are not there, and a map that lies is worse than a map
that does not go that far.

Matthew's 2 km is right at the edge of legible and it is his call whether to keep it;
`--mapscale=` is the seam, and it is deliberately not profile-only because the only way to answer
the question is to look.

**The bearing-independence oracle caught a real defect during this slice**, which is the best thing
that happened here. Adding the filter shifted the sample grid by one pixel -- and a shift in
*image* space is a different shift in the *world* at every bearing, so the test went red
immediately. A constant offset is invisible to every other test in the file, because they all
compare the render against itself.

**Slice c next** -- the mine from the chunk index, the machines from `MachineSet`, the water and
its window edge.

---

## Slice c — built, 2026-09-23, with one thing left open

The overlays: your mine from the stored-chunk set, your machines as pins in their own material's
colour, and the water with a rim where Tier A stops knowing. `MapOverlay` and `IMapWater` in Core,
`MapWater` over `WaterSystem` in the Game. Suite 1,799 -> 1,809; r6 control to the digit.

**Two of the three are verified in the game.** A copy of the retired pool world reads
`M12: map — 127 pin(s), mine drawn`, and the screenshot shows what this feature was for: the base
as a patch of solar-panel blue and outlet orange, sitting in darkened dug ground with a corridor
running north-west out of it. That is an operations board rather than a terrain map.

**The water is not verified in the game and that is where this slice stops.** It reads
`water not analysed here` at the player's own position on a world whose Tier A plainly has a window
(`388/396 basin(s)`, `window 512 m`). The Core tests cover the drawing, and two real defects were
found and fixed on the way to that line -- but the line is still wrong, and the last diagnostic
says why it is not a water problem at all:

> **The map is built exactly once.** `build 1`, and no more, across 2,400 frames -- with a
> one-second maximum age in place that should have forced about forty rebuilds.

So the panel's `Refresh` is not driving rebuilds the way it reads as doing, and the water overlay
is almost certainly fine behind it. **Everything downstream of that was chasing a symptom**, which
is where Matthew called it: *"Just a plan if things work out too hard"*.

### What slice d should do, in order

**1. Find out why the panel builds once.** `RefreshHud` is called unconditionally from `_Process`
and `MapPanel.Refresh` accumulates `delta` into `_sinceBuild`, so one of those three is not true.
The cheapest probe is a counter on `Refresh` itself printed beside `Builds` -- *is it being called,
and is `_sinceBuild` growing* -- which separates "never called" from "called and refusing" in one
run. **Do this before touching the water**, because a map that only ever draws its first frame is
wrong for every overlay and every moving player, not just for the water.

**2. Then re-check the water**, with the same `M12:` line. If it reads `analysed here` once the
map rebuilds, nothing else is wrong and the rim can be judged by eye at 16 m a pixel, where the
512 m window is a quarter of the map.

**3. The overlay is rebuilt every frame and should not be.** `MapOverlayNow()` walks the whole
stored-chunk set into a fresh `HashSet` and allocates a pin list, and it is passed as an argument
-- so it runs at 60 Hz whether or not the map is open or rebuilding. It should be built inside the
"is this worth rebuilding" decision, not outside it. On the played world that is 4,584 chunks a
frame for a picture that changes once a second.

**4. Then the play**: can you find the hills, and does the shipped relief shape read right now that
you can see it.

### Two defects the wire found, both fixed

**The sample conversion belonged to the water system, not to the caller.** `MapWater` divided world
voxels by `HeightField.DefaultCellVoxels` to reach the watershed's lattice -- guessing at a number
the installed window owns and a world header can change. `WaterSystem.TryBasinAtVoxel` does the
conversion now, with the window's own cell size and a proper floor for negative coordinates. **The
guess failed silently, as a basin that is never found**, which reads as unanalysed ground rather
than as a bug.

**A map that only rebuilds when you move is stale about the world.** Water rises, a hillside comes
down, an autosave grows the mine -- and standing still is exactly when somebody is reading the map.
There is a one-second maximum age now. It is not yet doing anything, which is item 1.

### And one the screenshot found

**Pins drawn one at a time black each other out.** A mining operation's machines stand in a yard:
127 of them drew their dark outlines over one another's centres and the whole base came out as a
blob. Centres first, then outlines around what is left -- a cluster now reads as a patch of machine
colours with one rim around the whole of it, which is what a map of a base should look like.

---

## Slice d — built, 2026-09-23. The map is finished.

All three overlays verified in the game. On a copy of the retired pool world the log reads
`M12: map — 127 pin(s), mine drawn, water analysed here`, and the screenshot shows the analysed
window as a **diamond** — a world-aligned square seen through a 315 degree rotation, which is what
a facing-up map should do to it — with Tier A's ponds inside it and nothing claimed outside.

### The one defect, and why it looked like three

Slice c stopped at "the map builds exactly once", which read like a refresh-rule problem. It was
not. The probe slice c's plan asked for settled it in one run: `Refresh` was being called every
frame, `_sinceBuild` was growing to fifty-four seconds, and `ShouldRebuild` plainly had a
one-second maximum age in it. All three facts were true and the map still built once.

**`Build` was throwing.** `WaterSystem.LevelAt` walked into the merge tree's arrays with
`Watershed.Outlet` — a label, not a node — and threw `IndexOutOfRangeException`. And because the
throw happened inside a Godot callback, **it was caught and logged rather than crashing**: no
failing test, no stopped process, just a map that drew its first picture and then silently never
updated again.

**It is fixed at the source rather than at the caller, and that asymmetry was the whole cost.**
`TableAt` had guarded the outlet case for milestones — the author knew — but `LevelAt` itself
threw, so every *other* caller had to know too, and the map did not. `LevelAt` answers null for an
outlet now, which is the true answer: water there leaves the world, so it has no level.

**The lesson worth keeping: an exception inside a Godot callback does not look like an
exception.** It looks like a feature that works once. `grep` the run for the exception type before
believing any behavioural theory about a UI that updates strangely — the whole of slice c's
"water" investigation was chasing a symptom three layers downstream of a stack trace that was
sitting in the log the entire time.

### The other two items

**The map now rebuilds while you stand still**, at most once a second, because the world changes
on its own: water rises, a hillside comes down, an autosave grows the mine. Standing still is
exactly when somebody is reading the map.

**And the overlay is gathered only when the picture is rebuilt.** It was passed as an argument, so
it ran at 60 Hz whether the map was open or not — walking every stored chunk into a fresh set and
allocating a pin list, 4,584 chunks a frame on a played world for a picture that changes once a
second. `Refresh` takes a factory now and calls it inside the rebuild decision.

Suite 1,809; r6 control 2601 / 325 / 3446 at 411 to the digit.

### What is left, and it is all Matthew's

Nothing is blocked. The play checks are in `MATTHEW-TODO.md`: whether 2 km is the right default
width, whether the shipped relief shape reads right now that it can be seen, and whether the map
earns its key.
