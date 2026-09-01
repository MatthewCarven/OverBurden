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

**M8's water.** The active set of a filling body is its **surface, not its volume**. A generous
cave at 30 × 30 × 10 m is 9,000 cells of volume but roughly **900 cells of waterline**, and at a
slow tick — 2 Hz, which [is accurate rather than approximate](../TODO.md) for a process that takes
minutes in the world — that is **~1,800 cell visits per second**, about **0.3%** of what lighting
is already licensed for.

**Caveat: water's cell size is not settled.** 1 m is used above because it is the light lattice,
but M8's Tier B granularity is genuinely open and it is a **64× swing** against voxels. Re-derive
this section once that is decided.

**M9's collapse.** Deferred rather than continuous (Matthew's proposal, recorded in TODO), so the
eligibility test is allowed to be late — which means it can run coarse and sampled rather than
per-voxel on every edit. The sizing question there is not visits per second but **how many
regions are at risk at once**, which is a different quantity and wants its own measurement.

## The rule this file exists to state

> **The totals are in the tens of millions of voxels and the hundreds of thousands to millions of
> cells. Nothing here has ever iterated more than about half a million of anything in one
> second, and nothing should start.** Every system is an active set: a frontier, a dirty list, a
> box difference, an overlap. When a new subsystem's design contains the words "check all", that
> is the moment to come back to the second table.
