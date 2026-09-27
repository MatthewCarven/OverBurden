# Water bodies — the planning pass (2026-09-28)

*"plan the water bodies with me please Claude"* — after *"Am i right in thinking that when water
settles as tier b water it wont be reclaimed by tier a because it is no longer processed?"* (the
answer was: it is handed over at the moment it rests if it rests on ground Tier A owns, and
otherwise stays cells for good). Read [WATER-BODIES-NOTES.md](WATER-BODIES-NOTES.md) first: what a
body is, what it buys, and its six decisions, the first of them answered by him on 2026-09-26.

**Nothing is built but the measurement.** `WaterComponents` (Core) and `--bodyprobe=<seconds>`
split Tier B's held water into face-connected components once and print them (`M8e:` lines).

## The numbers, from his world

All runs were on copies of TajMahal (backed up as `backup-TajMahal-2026-09-28`), at the play arms
(`--fluidnear=2 --fluidjob=1`), on the build fixed the same night (Core optimised; the tick at
2.00 Hz), with zero ERROR lines.

| | 45 s into the session | 320 s into the session |
|---|---|---|
| held water | 1,324,640 cells, 19.93 ML | 1,351,880 cells, 20.51 ML |
| components | 1,246 | 838 |
| the largest | 1,304,557 cells, 19.81 ML, 50% full cells, Y −94..−28, all below the seed | 1,339,715 cells, 20.43 ML (**99.6%**), 44% full, Y −94..18 |
| components of 64 cells or more | 42 | 27 |
| components under 64 cells | 1,204 | 811 — mostly ~1 L films one voxel thick on his building's floors (Y 112–168) |
| **awake** | 1,257,809 | **1,215,567** |
| one whole flood | 382 ms | 285 ms |
| the tick | 205 ms wall, 2.00 Hz | 188 ms wall, 2.00 Hz |
| panels | 18.0 MiB | 9.7 MiB |

`fluidmove` over the 330 s run, for 145 M visits:

| Move per visit | Share of visits | Share of the volume moved |
|---|---|---|
| rested | 17.9% | — |
| under 10 mL | 21.9% | 0.9% |
| 10–99 mL | 36.5% | 11.1% |
| 0.1–1 L | 15.5% | 35.0% |
| 1–5 L | 1.7% | 35.6% |
| 5 L and over | 0.2% | 17.4% |

## Seven facts

1. **His water is one body.** 99.6% of it is a single face-connected component, the quarry
   pool, 28 m tall and all below the seed. The rest are rain films on what he built and puddles.
   A body mechanism would retire one component here, which is nearly all of it.
2. **The water never comes to rest.** 1.2 M of 1.35 M cells are still awake after five and a half
   minutes at full speed, and the pool is only 44% full cells.
   - 58% of the visits move under 100 mL and carry 12% of the volume. That is dithering.
   - 19% move 1 L or more and carry 53%. That is the lake still pouring in at the rim (WORKLOG
     2026-09-27).
   - **The notes' decision 2 forms a body from resting cells, so on this world no body would ever
     form.** Rest comes before bodies.
   - This corrects the 2026-09-27 reading, *"a sleep threshold would save a fifth"*. That was
     taken on the unoptimised build, where the water was still flowing at 41% speed. At full speed
     the pour has spread, and the small moves dominate the visits.
3. **A whole flood costs 285–382 ms** on the sim thread for 1.35 M cells (a `HashSet` flood, the
   probe's). Finding a body cannot be a sim-thread flood at a boundary. It wants a worker, or to
   be incremental from a region that has gone quiet.
4. **`VolumeCurve` cannot hold a 3D void.** It is a heightfield: each sample adds
   `(level − floor) × area` with no ceiling. A mine has overhangs and stacked pockets, so a body
   needs a new curve: holdable voxels counted per Y layer, cumulative, with the same
   sort / prefix / binary-search lookup.
5. **Saving is a known pattern.** The world file is at schema 4 (machines); a `bodies` table would
   be schema 5, `CREATE TABLE IF NOT EXISTS … STRICT`, keyed by the body's lowest voxel.
   Permanent-data-loss territory, so it ships alone, as the fluid table did.
6. **A body has more writers than the edit hook.** It must answer:
   - everything through `SetVoxel`: digging, placing, undo, collapse rubble, `MachineSet.Restore`,
     the debug bites;
   - five water writers that bypass it: pump intake and outlet, the collapse's and the fill's
     lift and return, the portal drain's add, the hand-off, and the lake ceiling's wake.
   The pump would get a third intake source beside a lake and cells.
7. **Speed no longer forces bodies.** On the fixed build the pool ticks at 2 Hz with 188 ms of
   worker wall (visits 86, solve 93). What a body buys now is:
   - the idle cost: 1.2 M cells visited forever;
   - memory and save size: 9.7–18 MiB of panels, a 3.8 MB world file;
   - the surface mesh;
   - pressure for M10's underwater ladder.

## Decisions, each with a recommendation

1. **Rest comes first, as its own sub-part, before any body.** Recommended. Find why a still pool
   stays awake. The levers to measure are:
   - a move under a threshold (say 100 mL) counting as no move, so the cell rests;
   - the sheet solve's "level" tolerance;
   - the lake's inflow at the rim, which is real flow and should keep the rim awake.
   It is priced by the rested share and the awake count on this same copy.
   - *Reverse if* the pool rests on its own given time: run the probe at 20 minutes first, which
     is the cheapest falsifier.
2. **A body forms from a quiet component of at least 64 cells.** Recommended. Every cell must have
   been asleep for K boundaries (10 s). Smaller components stay cells: his 811 films and puddles
   cost nothing asleep, and a body each would be bookkeeping for a litre.
3. **Found on a worker, from the region that went quiet.** Recommended. The flood runs over the
   captured grid on a worker, bounded by the component, which is his decision 1: water, not void.
   The boundary only applies the result. The whole flood's 285–382 ms is the ceiling this has to
   beat, and it only has to run when something goes quiet.
4. **Communicating vessels** — answered 2026-09-26: a body is a connected component of water.
   Still open was a body formed from arms already standing unequal. Recommended: it forms only if
   the component is level within the rest threshold. Otherwise it stays cells and the automaton
   levels it. So nothing ever teleports.
5. **A disturbance makes a mouth, not a release.** Recommended. Waking all 1.34 M cells because
   one voxel at the rim was dug is the cost bodies exist to avoid.
   - A write inside a body's void or on its skin makes that voxel a mouth.
   - The body drains into cells there at the portal drain's rate, re-solves its level from the
     curve, and re-derives its void lazily.
   - A write that splits the void (a dam placed across it) releases the body back to cells, the
     one case that must, since two levels are then possible.
6. **The drain rate at a mouth is today's room-below rule.** Recommended. Head-driven (Torricelli)
   is decision 6 of the notes and waits for M10's pressure rung, which is the first reader of a
   body's head.
7. **Drawn as one plane per body** at its level, over its top layer's footprint, with the
   mesher's existing no-top-under-a-lake rule extended to "inside a body". `IsUnderwater` asks
   bodies too. Recommended.
8. **Identity terms `absorbed` and `released`**, in Tier B's identity the way `pumped` and
   `emitted` are, and a body's own `held == absorbed − released − drained + received` on the
   `M8e:` line. Recommended.
9. **The split.** Recommended, each part with its own pass:
   - **a — rest.** Decision 1: measure, then the rule. Worth doing alone even if bodies never land.
   - **b — the body in Core.** The layer curve, formation from a quiet component, level, mouths,
     identity terms, tests against the cells they replace.
   - **c — the wire.** The worker flood, the boundary, the plane, `IsUnderwater`, the pump's
     third source.
   - **d — persistence.** Schema 5, shipped alone.

"Nothing" is still an arm: with the build fixed, a still pool costs ~190 ms of worker wall at
2 Hz and no frame. That is affordable for one quarry, and it grows with every pool a player
fills.

## What would reverse this

- The pool resting on its own within minutes. Then part a is unneeded and b starts at once.
- A second world where no component dominates, say a hundred mid-sized pools. Then formation
  cost, not the flood, decides decision 3.
- His answer that big standing pools are rare in play. Then "nothing" wins, and part a alone is
  kept for the idle cost.
