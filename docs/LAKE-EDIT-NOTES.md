# Building in a lake — notes (2026-09-30)

Not scheduled. Opened when IM-4c kept today's rule at his word (*"keep today's rule, plan displacement"*):
a block placed in a lake is a pier, deleting nothing. His next water pass on building in lakes starts
here. Nothing below is decided.

## His words (verbatim, from his list's notes, 2026-09-30)

On IM-4c:

> uh i thought we had a function that displaced tier b water? we could upon edit collapse tier a water to
> tier b I have to talk to you about that, we need to get a little more aggressive with the conversion of
> tier b to tier a when heights are 95% or there abouts of what they should be, but I also want to break
> tier a water when someone places voxels there so that sheets do not bleed in and out of walls like in
> tajmahal2 do we collapse tier a water to be tier b knowing we will only convert it back again when it
> comes close to settling?

On the lake over his quarry:

> it draws nearly perfectly sometimes there is a small gap like but its usually filled with tier b water
> shortly after like, like I say teir a sheets not getting seperated when someone puts voxels across the
> center is something I want to address at some point but apart from deleting it, or collapsing it to
> tier b water then displacing it maybe?

On a rock placed in a lake:

> um if we can displace the water, or as a worst case just drop it from the sky into the same position
> like

## What he is asking for (my reading, to confirm with him)

1. A block placed in a lake **displaces** its water rather than deleting it: the water is conserved.
2. **A wall across a lake breaks its sheet**, so a lake does not draw on both sides of a wall (TajMahal2).
3. **Tier B hands back to Tier A sooner**, once a sheet is within about 95% of its level.
4. His idea for 1 and 2 together: on an edit in a lake, collapse the nearby Tier A water into Tier B,
   which respects walls, and let the hand-off give it back once it settles.

## What exists that bears on it (from IM-4c's reads, file and line)

- **A block in a lake is a pier today.** The lake's litres do not change, and neither does its level:
  the volume curves are built from the seed surface only (`VolumeCurve.Build`, `Watershed.cs:143`), and
  M9-PLAN locked "edits do not change the watershed". The sheet is one plane a sample at the basin's
  level (`WaterSurface.cs:180`, `:371`), and a placed block only hides it by the depth test.
- **The band already respects walls.** A column the enclosure finds walled in drops out of the lake band at
  the next refresh (`LakeCeiling.cs:340–348`, via `Enclosure.VoxelChanged` → `MarkDirty`). What draws on
  both sides of a wall is worth pinning first: the plane, which is per sample and not per band, or the
  band itself.
- **Tier B cannot hold a cell inside a lake band** (`LakeBoundedTerrain.CanHold`, `LakeCeiling.cs:544`), so
  water lifted out of a lake cell cannot be put back as Tier B in the lake's own cells; a fill that tries
  counts it `displaced`.
- **The unmerged `water-column-test` branch is displacement in Tier A's own terms.** Edits above the seed
  raise the watershed there ([POND-PLAN.md](POND-PLAN.md)), so a block in a lake cuts its capacity and the
  level rises by what the block took. His verdict on the branch is open; his note (2026-09-30) is to test
  and optimise further on the branch.
- **"Drop it from the sky" has doors already.** The siphon's pour (`FluidSimulation.Pour`, IM-4a) and the
  debug relay's pour put litres down as Tier B above a point; over a lake they land on the surface and the
  hand-off gives them back. Each is a counted door, so a drop from the sky would be one more term.
- **Sooner hand-back is the water bodies' question.** Part a (rest) is parked
  ([WATER-BODIES-PLAN.md](WATER-BODIES-PLAN.md)): the hand-off gives back only water at rest, and his pool
  never rests under the near/far order.
- **No drain either way.** Anything that conserves the water, whether displacement, a drop or an A → B
  collapse, needs no price, because a fill-then-dig loop removes nothing. That was the whole reason IM-4c's
  draft priced the delete.

## Questions for the pass

1. Displacement by capacity (the pond branch's raised watershed), or by moving litres (A → B at the edit,
   or a drop from the sky)?
2. Does a wall across a lake split it into two lakes? That is a watershed change, and the pond branch's
   ground.
3. The 95% hand-back: is it a threshold on the sheet's level, and what does it cost the load test?
4. Collapse A → B at an edit: how much of the lake converts, around what, and under what budget? A whole
   lake converted is IM-0's 10 ML pour (0.5–1.1 µs a cell).
