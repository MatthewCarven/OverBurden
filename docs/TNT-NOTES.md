# TNT notes — blasting

> Notes, not a plan — the M10-NOTES and WORLDGEN-NOTES shape: the reasoning captured while it is
> fresh so the eventual planning pass starts from it. **Not scheduled.** Written 2026-09-24 on
> Matthew's ask, *"write up the TNT notes how you would"*, after *"also how do you feel about
> tnt?"* the evening before. Every number here is an estimate unless it says where it came from.
>
> **Amended the same day with Matthew's own design**, which replaces decision 3's muck pile:
> *"tnt just destroys outright voxels and they go nowhere no cleanup, but you could fire whatever
> stress function you use around the place a little more pessimistically"*. So a blast deletes,
> and the roof pays through a harsher support check at the moment of the blast (the shake, below).

## The short version

**Yes, and it is cheaper to build than it looks, because almost all of it already exists.** A
blast is a carve the game already has (`Carve.Sphere`, bounded at 4 m). What happens to the roof
afterwards is M9's job, and M9 already does most of it. A charge waiting to go off is a machine row on a
channel, which is M10a's entity and M10b's computer panel. The chain reaction is the collapse
cascade's rule, one step per boundary. What is genuinely new is small: a `charge` machine kind, a
fuse on it, and the shake — a one-off harsher support check around the blast.

**Why it earns its place.** The ladder has nothing between a pick and the quantum pack for moving
a lot of rock. Blasting is how real mines do it, and in this game it is the tool that most
directly argues with the antagonist: you get a big hole for no energy, and then you have to live
under it. **The price is paid in the mine's own currency** — a roof that may come down and water
that finds the new void — rather than in joules.

---

## What already exists, and what each piece gives for free

| Needed | Already there | What it means for blasting |
|---|---|---|
| Remove a sphere of rock | `Carve.Sphere`, `MaxRadiusMetres` 4 m, one `Take` gate | The blast is a carve with its own threshold. A 3 m blast is ~7,200 voxels, less than a quarter of a chunk bite (32,768), so its **cost is already measured and fits in a frame** |
| The roof responding | M9: `SupportRegion` re-derives the edited chunks, `CollapseScheduler` arms at-risk chunks | **The void itself needs no structural code** — the support field judges it exactly as it judges a quantum bite. The shake adds two small pieces to M9 (see Costs) |
| A harsher check | `SupportRule.DistanceScale` (the `--supportscale` seam), the fall's column run | The shake is one derive under a scaled rule; what it finds at risk falls by the fall's own rule |
| A charge waiting | `MachineSet` rows, `machines` table at schema 4, ports, the computer's panel | A charge is a material plus a row, persisted and reconciled for free. **Its fuse and delay are ports**, so the computer sets them with no new UI |
| Firing together | Channels 0–15, every row on a channel ticked at the boundary | **A detonator on channel N fires every charge on channel N** — a blasting circuit, which is how real mines fire a round |
| A cascade that terminates | The collapse's *one roof per boundary* | A charge caught in a blast arms with zero fuse and fires at the **next** boundary: sympathetic detonation, one step per boundary, no recursion |
| Machines in the blast | `IMachineBodies`: `Lost == Carved` dismantles with credit, `Lost > Carved` destroys | A blast takes a body whole or in part. **Recommend: anything a blast takes is destroyed, not credited** (decision 5) |
| The undo | A fall clears the stack (UNDO-PLAN decision 4) | A blast deletes voxels nothing can give back, so it clears the stack too |
| Water | Tier B `Disturb`, the portal drain, seepage | A blast into a lake bed is a breach. **TNT is the most dangerous way there is to open one**, and the water side needs nothing new |

---

## How I would build it

### The charge

`overburden:blasting_charge` is a material with a `machine` block of a new kind, `charge`. It is
placed as one 1 m body like every machine, sits on a channel, and declares two ports:

- `fuse` — boundaries from firing to detonation, 0–120 (a minute at 2 Hz), default 10 (5 s). An estimate.
- `delay` — extra boundaries after the round is fired, 0–20. **This is the real-world trick that
  makes blasting a craft rather than a bang**: delay detonators fire the cut holes first so that
  later holes break toward an opening that already exists. Here it lets a player sequence a round
  across several boundaries.

The fuse burns in **world time at the boundary** and only while the charge's chunk is loaded — a
charge in an unloaded chunk waits, the rule every grid-reading machine follows (ladder decision 4).
The remaining fuse is on the row, so a quit mid-fuse resumes where it stood.

### The detonator

`overburden:detonator` is a machine of kind `trigger` on a channel. Its computer panel has a
**Fire** button beside the channel box, which lights every charge on the channel with its
`fuse + delay`, and says how many charges that is before you press it. There is no empty hand in
this game (a right-click always places what is selected), so the computer is the one trigger
until M10g's `E`. A charge's own panel has the same button, for the one-hole case. **No wires**: the channel is membership, the ladder's one idea, and a detonator on the
surface fires a round 60 m down with no cable. That fits the channel class through M10i; if DC
wiring lands first, a detonator is its first consumer that is not power.

### The blast, at the boundary

In origin order, every charge whose fuse has run out:

1. **Carves a sphere** of radius `blastMetres` (3.0 in the JSON, under `MaxRadiusMetres`) centred
   on the body's centre, with the charge's own `hardnessThreshold` (3.5 in the JSON — takes stone,
   dirt, clay and rubble, not a harder rock if worldgen adds one). **The voxels go nowhere** —
   Matthew's rule: no inventory credit, no muck, no cleanup. The rock is simply gone.
2. **Shakes the roof** — Matthew's *"fire whatever stress function you use around the place a
   little more pessimistically"*, made concrete. Once, at the blast, every chunk within
   `shakeMetres` (8, one chunk) of the charge is derived under a **harsher rule**: every
   material's support distance × `shakeScale` (0.75, an estimate — the existing `--supportscale`
   seam, applied to this one derive). **Whatever is at risk under the harsh rule falls at the next
   boundary**, by the fall's own column run, landing as rubble like any fall. **Nothing is
   stored**: a roof that survives the shake is judged by the ordinary rule from then on. The
   shock is a moment; if it holds through it, it holds. Separately, any collapse timer already
   armed within 8 m is shortened ×0.25 through M9d's *a timer only shortens*.
3. **Arms its neighbours**: any charge whose body the sphere reached is lit with fuse 0 and fires
   at the next boundary. Sympathetic detonation, one step a boundary, so a packed magazine goes
   off as a visible ripple and the loop is bounded by the charge count.
4. **Clears the undo stack**, printed like a fall's.

A charge is consumed by its own blast.

### The numbers, as a starting point

| | Value | Where from |
|---|---|---|
| Blast radius | 3.0 m | Estimate. ~113 m³ a charge, about 14 tunneller bites or a fifth of a chunk |
| Threshold | 3.5 | `steel_pick`'s, so a charge takes what steel takes |
| Shake | support distances × 0.75 for one derive within 8 m; armed timers ×0.25 | Estimate; **0.75 is the lever for "blasting is risky"** — lower is harsher |
| Default fuse | 10 boundaries (5 s) | Estimate; enough to walk out of 3 m |
| Recipe | charcoal + sulfur + saltpetre → black powder; powder + clay → charge | Real black powder. **Needs sulfur and saltpetre from worldgen, which is WORLDGEN-NOTES' pass** |

Until worldgen and crafting exist, `P` seeds a stack of charges and a detonator, the way it seeds
the relay pair today.

---

## Decisions, each with a recommendation

1. **Where on the ladder.** *Recommend: the hand-to-steam boundary* — powder at the forge (tier 0),
   the charge after the furnace (tier 1). It is the bulk tool before quantum. **It stays useful
   after quantum** because it costs no joules: a 3 m charge is about 360 kJ of pack at 50 J a
   voxel. The trade is pack against roof.
2. **Fuse model: a lit charge, a detonator on a channel, or both.** *Recommend both*: a charge's
   panel lights that one charge, and a detonator's lights its channel. The detonator is the part that makes it a
   mining tool rather than a grenade, and it costs one small machine kind.
3. **Where the rock goes. — Matthew's: nowhere.** *"tnt just destroys outright voxels and they go
   nowhere no cleanup"*. No credit, so TNT never competes with the quantum tools on yield; no muck,
   so there is no landing to build or price. Ore in a blast later is simply lost — the cost of
   blasting through a seam instead of picking it. (My draft had a muck pile you dug out; his is
   simpler and loses nothing the game needs.)
4. **Does a blast weaken rock it did not remove? — Matthew's: yes, pessimistically, at the moment.**
   The shake above: one harsher derive, what fails it falls, **nothing stored**. My draft said no
   and leaned on shortening armed timers alone, which would do nothing to a roof that was not
   already failing — his version reaches the marginal roofs, which are the ones that make a blast
   feel dangerous. A stored per-voxel fracture state stays rejected: it is new persistent data
   under every chunk. **Revisit trigger: a blast that reads as too safe (lower `shakeScale`) or a
   roof that falls a long way from a small charge (raise it).**
5. **Machines in the blast.** *Recommend destroyed, never credited*, whether taken whole or in
   part. The risk of blasting beside your own pumps is the point. Cost: a mis-set channel can
   wreck an engine room, so the detonator's panel should list how many charges it will fire.
6. **The player in the blast.** Health is M11's, deferred (*"for debugging I don't need it
   really"*). *Recommend until then: nothing hurts you; the blast can take the floor you stand on,
   and you fall.* At M11 the damage is distance-based and comes with the respawn M11 already owns.
7. **Charges and the undo.** *Recommend: placing a charge is undoable like any placement, and a
   blast clears the stack* — UNDO-PLAN's decision 4: the blast deletes what nothing can give back,
   and the shake's falls mint rubble exactly as a fall does.
8. **Chain reactions and a bound.** *Recommend: sympathetic detonation one step a boundary, bounded
   by the charges placed* — nothing else. A thousand charges in one room is a thousand boundaries
   of ripple at the worst: slow, visible and finite. The collapse cascade already works this way.

---

## Costs, stated

- **A machine kind that acts on its own**, and the first one whose action is destructive. The row,
  the persistence and the reconcile are all M10a's. What is new is that a row's tick *writes the
  world*, which only pumps and outlets do today, and they only write water. The blast's writes go
  through `SetVoxel` like a swing's, so every hook — light, support, water, the undo journal —
  sees them. *An observer sees every writer*: the undo's journal must treat a blast as the world's
  write and not the player's.
- **A boundary-time carve.** The blast runs inside the boundary block like the collapse sweep, as
  its own `blast` term on the `boundary` line. A 3 m sphere is ~7,200 `SetVoxel` calls. **By the
  M9c-1b figure of 1.5 µs a voxel through the hook chain, that is ~11 ms Debug** — one frame's
  hitch per blast, and a round of ten on one boundary is ~110 ms. That is the measurement to take
  first. The lever if it bites is the one M9 already named: a per-chunk invalidation on a bulk edit.
- **The shake needs two things M9 does not have yet.** The region derives under one rule
  (`SupportRegion._rule`), so a derive under a scaled rule is a rule passed per derive. And a fall
  today follows the chunk's settled at-risk set, so the shake's falls need the fall to take an
  explicit set. Both are small; neither is free. The derive itself is a detached job, ~0.36 ms a
  chunk optimised (M9b) — up to 27 chunks in an 8 m shake, on workers and not the frame.
- **Nothing new to terminate.** No particles are required (dust is still M9c-2's optional
  cosmetic, and a blast is its obvious second customer). The chain is bounded by a count, and the
  water side is M8's existing seam.

## What would make this wrong

- **Worldgen never ships sulfur and saltpetre.** Then the recipe needs another route, or the
  charge stays a debug item. The mechanics above do not depend on it.
- **The shake drops too much or too little.** `shakeScale` is the one number, and it is a
  multiplier on distances every material already has, so it moves stone and dirt together. If
  dirt roofs vanish at every blast while stone ones never move, the scale wants to be per
  material — decide after the first round, on his word.
