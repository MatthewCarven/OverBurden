# Scale — how big the world is, in the units each system iterates

> Reference sheet. Written 2026-09-01, from measured runs plus arithmetic that is reproducible
> from the constants below. **Every figure here is either measured and labelled as such, or
> derived from the four constants and therefore recomputable — nothing in this file is a guess.**

The question this exists to answer is the one M7, M8 and M9 all have to start from: *how many
things are there, and how many of them can we afford to touch per second?* It is written down
because the answer is counter-intuitive in both directions — the totals are enormous and the
amounts actually iterated are tiny, and confusing the two is how a subsystem gets designed
around a sweep it cannot afford.

## The four constants everything derives from

| | value | note |
|---|---|---|
| Voxel | **0.25 m** | the atom; nothing is finer |
| Chunk | **32³ voxels** = 8 m cube | 32,768 voxels |
| Light / brush cell | **1 m** = 4³ voxels | 64 voxels; 8³ = 512 cells per chunk |
| Load margin | **2 chunks** beyond the visible box | not a tuning knob — see M2c-1 |

A useful pair to keep in the head: **a 1 m cell is 64 voxels, and a chunk is 512 cells.**

## How many there are

Tracked chunks at horizontal radius *h*, vertical radius *v*, margin 2:

```
chunks = ((h + 2) * 2 + 1)^2  *  ((v + 2) * 2 + 1)
```

| config | chunks | voxels | 1 m cells | visible chunks |
|---|---:|---:|---:|---:|
| **r4h/2v** (default) | 1,521 | 49,840,128 | 778,752 | 405 |
| r6h/3v | 3,179 | 104,169,472 | 1,627,648 | 1,183 |
| r12h/3v | 9,251 | 303,136,768 | 4,736,512 | 4,375 |
| r16h/3v | 15,059 | 493,453,312 | 7,710,208 | 7,623 |

**The chunk column is confirmed against measurement, not just derived**: the streaming profiler
reports exactly `1521 tracked` at r4h/2v and `3179 tracked` at r6h/3v.

*Drawn* chunks are far fewer than visible ones — 165 of 405 at r4h/2v — because a chunk that
meshes to nothing is not drawn. Mesh memory follows drawn chunks at ~46 KiB each; voxel storage
follows tracked chunks at ~2 KB each. **That asymmetry is why horizontal radius is expensive and
vertical radius is nearly free.**

## What it costs to touch them

Measured: light propagation runs at **462 ns per cell visit in Debug** (`start.cmd` builds
Debug). M5d-1 measured the Debug penalty on the propagation walk at roughly **10×**, so treat
Release as ~46 ns/cell — and never quote either without its build, per M2b's recorded mistake.

One **complete** pass over every 1 m cell, at the Debug rate:

| config | cells | one full sweep |
|---|---:|---:|
| r4h/2v | 778,752 | **0.36 s** of one thread |
| r6h/3v | 1,627,648 | 0.75 s |
| r12h/3v | 4,736,512 | 2.19 s |
| r16h/3v | 7,710,208 | **3.56 s** |

**This table is the argument against any design that periodically checks everything.** At r16 a
single full pass is three and a half seconds. There is no tick rate at which that is acceptable,
which is why every system here is built around an active set rather than a sweep.

## What is actually iterated, which is the point

| system | what it touches | measured |
|---|---|---|
| Worldgen | each voxel of a chunk, **once** | 0.47 ms/chunk Release; caves are 729 noise evaluations per chunk, not 32,768, since M2d-2 |
| Meshing | one chunk + its 26-neighbour padding, per remesh | ~404 quads per drawn chunk |
| Light derive | one chunk's cells, on build or rebuild | 0.245–0.327 ms/chunk Release |
| Light propagate | the changed frontier only | **585,700 cell visits for a whole 1,521-chunk world fill — once**, ~75% of the tracked cell count |
| Physics step | voxels the moved box overlaps | **13 falling, 41 walking**, 63 worst substep |
| A mining swing | the bounded sphere | ~19 voxels removed, 1.3 chunk clones |

**The light figure is the one to reason from.** Lighting an entire world from scratch costs
about three-quarters of one full sweep, *once*, and then goes quiet. Nothing in this engine
re-walks the world on a timer.

## The per-second budget

The light step is capped at **4 ms per frame** (swept at M5c-1; below it the world generates but
cannot draw). At 60 Hz and the Debug rate that licenses:

- **~520,000 cell visits per second**
- **~10.4 million over 20 seconds**

That is the yardstick for anything new: a subsystem wanting a fraction of what lighting already
spends is affordable, and one wanting multiples of it is not.

## Sizing the systems that do not exist yet

**M8's water — measured at M8a-2, 2026-09-02, and this section is the re-derivation it asked for.**

The estimate below stood as written: *the active set of a filling body is its surface, not its
volume*. A generous cave at 30 × 30 × 10 m is 9,000 cells of volume but roughly **900 cells of
waterline** at 1 m, and M8-PLAN carried that to voxels as **16× rather than 64×, ~14,400 cells**,
because a waterline is an area.

**Measured, on a 96 m × 96 m mine flooding from a lake through breaches of four widths** —
re-taken 2026-09-03 from the committed code, which the M8a-2 table did not reproduce (2,000-tick
flow window; the original was shorter and pre-commit):

| breach | peak active cells | cell visits/s at 2 Hz | share of the light budget |
|---|---|---|---|
| 0.25 m | 4,096 | 4,370 | 0.8% |
| 0.75 m | 6,046 | 7,323 | 1.4% |
| 1.25 m | 12,531 | 15,930 | 3.1% |
| 2.25 m | 20,737 | 31,173 | **6.0%** |

**And on bitten chunks, the shape the play made normal (M8b-1):** an 8 m void fed through a 0.75 m
bore peaks at 4,914 cells and *settles*, in 1,915 ticks; **a chunk whose whole roof opens under a
lake is 43,374 cells active in one tick, and a 2 × 2 × 2 block is 302,182 — 73.5 ms in Release**,
six times the trigger below, in one tick. That is why the tick budget is a slice rather than a rate
— **built 2026-09-04: the tick is a cursor over its batch, resumed each frame under a 2 ms budget
(`--fluidbudget=`), and a tick that does not fit ends late rather than stalling the frame.**

**So the cell size is settled at 0.25 m and it is affordable**, the estimate was the right order,
and the ~50,000-cell revisit trigger is not reached. The 64× caveat is closed.

**But the estimate was right about the wrong phase, and that is the finding to carry forward.**
Every figure above is the *flood*. When the inflow stops the water keeps spreading, the active set
keeps **growing**, and a 96 m mine was still holding 9,000–21,000 cells awake after 41 minutes of
world time. Levelling is diffusive — O(n²) in the length of a flooded corridor — and a 16 m mine
that did run to completion took **11,071 ticks, 92 minutes of world time**, staying at ~1,700
active for all but the last 79 of them.

> **The rule this adds to the file: an active set can be small and still never empty.** Every
> earlier system here goes quiet on its own — light reaches a fixpoint, streaming finishes a box
> difference, a carve ends. Tier B is the first whose active set has no natural end, and the answer
> is not a smaller set but a **handoff**: a settled body stops being Tier B's and becomes Tier A's.
> That is M8b, and this measurement is why it is the milestone rather than the automaton.

**M9's collapse — planned 2026-09-11, M9-PLAN.** The field is per voxel over dug chunks and their
six face neighbours only, derived from scratch per chunk on a worker (*estimate: 1–3 ms Release,
32 KiB a chunk*; M9a's bench takes the number), and the count that answers the question below is
the `M9:` line's *at-risk chunks*. The paragraph that framed it:

Deferred rather than continuous (Matthew's proposal, recorded in TODO), so the
eligibility test is allowed to be late — which means it can run coarse and sampled rather than
per-voxel on every edit. The sizing question there is not visits per second but **how many
regions are at risk at once**, which is a different quantity and wants its own measurement.

## The rule this file exists to state

> **The totals are in the tens of millions of voxels and the hundreds of thousands to millions of
> cells. Nothing here has ever iterated more than about half a million of anything in one
> second, and nothing should start.** Every system is an active set: a frontier, a dirty list, a
> box difference, an overlap. When a new subsystem's design contains the words "check all", that
> is the moment to come back to the second table.
