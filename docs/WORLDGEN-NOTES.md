# Worldgen as content — ores, layers, trees — notes, 2026-09-11

Matthew, the evening M8c's seepage was played: *"maybe we should have a discussion about world gen
and what is permeable i think is the word? Like we are eventually going to have to revisit this to
get like iron, coal, trees, bronze and anything else like?"* — and, told the plan does not call for
it yet, *"thanks for reminding me that the plan does not call for that yet, but I do like thinking
ahead"*. Notes rather than a plan, in the shape of [MATERIALS-NOTES.md](MATERIALS-NOTES.md), which
already holds most of the *what*; this is about the *where*. Nothing here is scheduled. M8d is
next, then M9.

## What is already decided, so it is not re-litigated

- **The materials and why each exists** are MATERIALS-NOTES' element table: sandstone is the
  permeable rock (water walks *through* it — Tier C's real source), clay the natural dam, limestone
  the one water eats, shale the first thing to come down; the ore ladder is coal → iron → copper →
  lead / nickel → bauxite → uranium, each named for the machine it enables. Bronze is copper plus
  tin through a smelting **recipe**, which is M11's machine framework and not worldgen's.
- **Permeability is per material, in the JSON, read since M8c-1.** Every new rock arrives with its
  number and nothing in the engine changes. The per-material *mode* — a low-permeability rock
  springs through joints, a high one sweats through the face — is a threshold on that number and
  needs no new field; see M8-PLAN's decision 5 under M8c.
- **Strata are data but not content** — `StrataTable` is built in code (grass, three of dirt,
  stone). MATERIALS-NOTES says so and says it should move to JSON when there is a second strata
  set to express.
- **The cross-chunk feature stage has existed since M2c and has never placed anything.** Its rule
  is recorded at M5c-1: when `ApplyFeatures` starts placing, it must route through the relight
  path the way `SetVoxel` does.

## What is genuinely new: placement, and it is three different jobs

1. **Layers.** Strata as content: bands by depth, per band a material and a thickness with some
   noise on the boundary. Sandstone and clay bands are what make a mine *change* as you go down,
   and a clay band over a sandstone one is a perched water table for free — Tier C's table is
   derived from Tier A, so a sealed band under a lake does nothing today, and that is a revisit
   trigger on M8c's decision 1 once clay exists.
2. **Seams and veins.** Coal in bands, ores as blobs. The cave lattice is already this shape — a
   field sampled every four voxels and thresholded — so a vein is a second field with its own
   threshold, its own depth band and a material instead of air. Sampled on the same lattice so
   chunk boundaries agree (M2d-2's argument), and interpolation clips peaks the same way, so a
   threshold is also a *volume* knob. Cheap, and it makes ore findable rather than sprinkled:
   follow a seam, and the seam runs under the lake.
3. **Surface.** Trees, which need wood and leaves as materials, a canopy that is not a voxel of
   rock (the first material that is neither solid nor fluid — `VoxelFluidTerrain`'s recorded
   revisit trigger, and light's), and the feature stage placing a thing across chunk boundaries.
   Trees are that stage's first customer.

## Three things the planning pass wants from Matthew

- **How deep is deep?** If iron wants you under the water table, seepage is the tax on ore and
  the mine-versus-water loop closes by itself; if ore is shallow, water is a separate hazard.
- **Are trees a resource you farm or one you find?** Farming is growth, which is time and a rule
  the clock already exists for; finding is placement only.
- **Is the first age stone-and-wood before any metal?** That decides whether wooden tools exist
  at all, and the wooden → brass → iron → steel pump ladder (M10-NOTES, *Wading*) hangs off it.
