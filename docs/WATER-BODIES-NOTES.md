# Water bodies — resting water as one number over a void

Matthew's question, 2026-09-10, late: *"could we derive a speed improvement through joining these
sheets into more like 3d blobs? … I'm looking for more of a way for water to be static 3d when at
rest."* Notes rather than a plan. **Not started; Matthew said "not yet."** Read before M8c's
planning pass, which is where this belongs.

## What it is

A body of water at rest is **one number — litres — over a void**, the connected set of holdable
voxels under its surface. Everything else is derived, the way a basin's level is derived today:

- **Level** from a volume→level curve over the void: holdable voxels per Y layer. `VolumeCurve`
  does this for basins over 2 m samples; here it is over 0.25 m voxels, so the shore is exact to
  the voxel and no lattice question exists.
- **Surface** = the level plane clipped to the void's cross-section at that Y. One mesh, cached
  until the level or the void changes.
- **Identity** = the void's lowest holdable voxel, in world coordinates — the basin rule (a pit,
  never an index), so it survives window moves and quits the same way.

It is not a new idea. DESIGN's Tier B line has said since week one: *"Stable cells sleep and merge
into a Tier A body."* M8-PLAN's answer to "the only copy is in RAM" was *settle rather than store:
integrate the transient to rest and hand the volume to Tier A, whose ledger already persists.*
M8b recorded the gap — **a flooded mine has no Tier A body, so resting mine water is stored** —
and built panels (M8b-4b) instead. A body is that gap closed: **the mine gets a Tier A body.** The
seams already exist because they were built for surface lakes — `SurfaceHandoff` (resting cells →
body litres), `PortalDrain` (body litres → cells at a hole), `PortalIndex` (where the holes are),
`VolumeCurve` (volume → level). A body is a basin whose terrain is the dug void rather than the
seed surface.

## What it buys, honestly

**At rest the automaton already costs nothing** — sleeping cells are not visited — so the tick
does not get faster. What gets cheaper is everything that touches *the stored shape* of resting
water, and one thing that touches its waking:

| today | with bodies |
|---|---|
| 78 MiB of panels for the played lake; the eviction slice; the 64 MiB trigger | a few bytes per body |
| autosave panel copy 72–100 ms Debug, 399 ms on the first after a restore | nothing to copy |
| `FluidGrid.Restore` cell by cell, p99 frame 33 → 58 ms on the probe | re-derive the void from chunks that load anyway |
| Tier B surface mesh, 144,559 quads in 119.7 ms; 7.8 ms install a tick under flow | one cached plane per body |
| settle passes O(sheet) while a front creeps (the share slice bounds it) | no sheet — nothing to level |
| **a hole under a mine lake wakes the front; a roof-open is 302,182 cells in one tick** | a hole is a **portal drain from the body** — see below |

## The roof-open case, and what a body changes about it

How the wake works today, so the number is legible. A resting lake has no awake cells. A chunk
bitten out from under it fires `Disturb` for each of the 32,768 opened voxels, and `Disturb` wakes
only cells that *hold water* among the voxel and its six face neighbours — so the bite wakes
exactly the lake's bottom layer over the chunk's 32 × 32 footprint, 1,024 cells, and nothing
else. Then the first tick runs:

1. Each of those 1,024 cells finds a whole chunk of room below and falls one cell. A move wakes
   the source, the destination and the source's six neighbours.
2. **A cell woken during a tick is visited later in the same tick.** So the destination is
   visited, finds room below, falls again — and the chain runs to the floor of the void *inside
   one tick*. That is the fact M8b-4a recorded as "one `Step` drops a poured cell two voxels, not
   one," at chamber scale.
3. Each fall also woke the cell *above* its source, which now has room below and falls into the
   vacated slot; and the sides, which spread sideways into it. So the column over the footprint
   collapses and the lake around the footprint starts drawing inward, all in the same tick.

The visited set of that one tick is therefore close to **the whole void plus a ring**: 43,374 cells
for one bitten chunk (void 41,480), 302,182 for a 2 × 2 × 2 block (void 295,944) — the chamber
fixture, `--fluidshape chamber`, M8b-1. 73.5 ms in Release, ~0.7–0.9 s in Debug, in one tick; the
block is full in two ticks, which is the automaton being *right* — a column of water over an
opened void does come down at once. Since the budget slice it is 38 frames at 2 ms rather than one
frame; since slice 3b it is worker wall, and a tick over half a second makes the next boundary
wait (one waited in 3c's play, at a 128,600-cell peak from six chunks taken out under a lake).
**The battery gate is what bounds it in play** — the only way to wake 300,000 cells at once is an
action that will cost a charge (WORKLOG 2026-09-10).

A hole that is a bore rather than a roof is the same mechanism at a different width: `Disturb`
wakes the one bottom cell over the bore, the chain falls down the bore, the cells above and beside
fall and spread into the depression, and the awake region grows outward from the hole a ring per
chain per tick as the surface dimples — that is "the front." What eventually flattens the far
side of the lake is the sheet solve, one O(sheet) pass whenever a move disturbs one cell in eight.

**With a body, no cell inside the lake exists.** A bore is a portal filed under the body; each
boundary `PortalDrain` draws into the cell under the mouth what room it has, the body's litres
fall and its level is re-solved from the curve. The active set is the falling stream and the
spread on the floor — a few hundred cells — and the lake's far shore moves by arithmetic. A
roof-open is 1,024 mouths: 1,024 cells drawn a boundary, ~16 kL a tick, so an 8 m chamber fills
over ~40 ticks (20 s) with the active set the column and the floor rather than the void. **That
is cheaper, and it is not automatically more accurate** — the first version of these notes said
it was, and the arithmetic disagrees: real water under 8 m of head comes through a chunk-wide
hole in a second or two, which the automaton's one-tick collapse is nearer to than a 20 s pour.
What a body *can* do that the automaton cannot is **draw at a rate set by the head over the
mouth**, because it knows its level and the automaton has no pressure — Torricelli at each mouth,
delivered as cells at the drain's rate. That is the genuinely more accurate version, and it is a
decision for the pass, not a given.

## The decisions a planning pass would have to take

1. **Communicating vessels.** A void is 3D-connected, so a body has one level across a U-tube —
   real hydrostatics. The automaton never rises, so today a U-tube does not equalise, and the
   sheet solve levels *sheets* (sideways-connected at one Y), not voids. A body formed from resting
   cells with an unequal arm would level instantly on formation — bounded by the arm's volume,
   rare, and a physics change that is arguably a fix. Decide, and say which.
2. **When cells become a body.** Rested *K* ticks with the sheet solved → absorb the connected
   void under the surface. A puddle on a ledge above the surface is its own body or stays cells.
   The transition must be exact in millilitres, both ways — the M8a oracle.
3. **Void discovery and invalidation.** The curve is a flood over holdable voxels bounded by the
   component — 32,768 for a chunk bite, ~1.3 million for the played mine. Price this first: it
   decides eager-per-edit against lazy-per-level-query. Every edit inside a body's void, and every
   chunk arriving or leaving at its edge, re-derives it. **An unloaded voxel is solid, so a body
   straddling the box edge has a truncated curve** — litres are the truth and the level reads high
   until the chunk lands, which is the ledger's own rule. The trap is a bite that *splits* a void:
   two bodies, litres apportioned by what each side's curve holds below the old level.
4. **Persistence.** A `bodies` table at schema 4, litres keyed by pit voxel; panels shrink to
   transient cells only. Permanent-data-loss territory, so it ships alone, as 4b did.
5. **What a cell rests on.** The hand-off's "resting on seed ground" becomes "resting on seed
   ground, or on a body's surface, or on ground inside a body's void below its level." Water
   falling onto a mine lake then lands and joins it with no cell ever existing inside.
6. **The drain rate at a mouth**: the automaton's room-below rule (today's), or head-driven. See
   above.

## Pressure is the body's — a second argument that is not about speed

A cell has no pressure; the automaton moves on room and level difference only. A body knows its
level, so *head* at any voxel inside its void is one subtraction — which is what a head-driven
drain (decision 6), crush depth, a rebreather's cost curve and pressure-based energy storage all
read. Matthew's underwater ladder (M10-NOTES, *Being underwater*) needs it from its second rung
up, so bodies are on that ladder's path as well as on the speed's.

## Further reach, deliberately not taken now

Surface lakes could become bodies too, wherever their bed is loaded — a curve from real voxels
instead of 2 m samples — which is what would properly fix Tier A feeding basins past their rims
(388% of capacity in 3c's play) and retire the shoreline question altogether. Tier A's window
would then own only the far lakes. It is the same mechanism a second time and wants the first to
be built and played before it is argued.

## Where it goes

M8c's planning pass. M8c is *water gets in*, and a body is what it gets into: seepage delivers to
a body's litres the way rain delivers to a basin's. The pass should open with decision 3's number.
