# Collapse notes — the slow-then-fast fall ("Tetris gone bad")

Matthew's idea, 2026-09-26, after demolishing one of his own towers on the pond world. **Notes
rather than a plan, not built; the design and the build are a separate chat, on `main`.** Read
this, then M9-PLAN's "M9c-1 — the planning pass" and "M9d — built", then `CollapseScheduler`
and `Collapse` in `Overburden.Core/Structure/`.

## In his words

Before the demolition, looking at his building over the pond:

> "funny thing is about the place i've built, like if i go knock out one of the ground supports
> like the whole thing should eventually come down like :-{D"

Carving a stone pillar into a checkerboard of 1 m cubes with the quantum miner:

> "i almost want to make it look like a game of tetris gone bad like?"

And the brief:

> "we're gonna write in a separate chat a tetris solver that uses compatible world data and let
> it collapse slowly to quickly like the initial march — you might be able to like fix it if you
> were quick, but if it was serious and/or critical well good luck saving it, like especially as
> time goes on. *tetris phonk plays in the background*"

Read as three requirements:

1. **Slow, then quick.** A failing structure starts slowly and speeds up the longer it keeps
   failing. That is Tetris's level curve: the opening march is slow, and the pace climbs.
2. **A window to save it that closes.** Early on, a quick player can shore it up. A serious or
   critical failure is very hard to save, and gets harder as time goes on.
3. **Compatible world data.** It reads and writes the world as it is — chunks, voxels, the
   support field, rubble. No new format and no second copy of the world.

"Tetris gone bad" is also a look: pieces of the structure coming away and dropping as blocks,
rather than a chunk's whole at-risk set turning to rubble in one frame.

## The first data point — his demolition, 2026-09-26

On `Regions16-pondlook` he knocked out a support under a ~40 m placed stone tower. From the
log (`M9: fell` lines), one chunk column, Chunk(−108, 1..4, 35):

| world time | chunk (height) | voxels | columns | deepest drop |
|---|---|---|---|---|
| 22667.8 s | 8–16 m | 768 | 64 | 4 |
| 22668.8 s | 16–24 m | 2,048 | 64 | 4 |
| 22673.3 s | 24–32 m | 2,452 | 168 | 80 |
| 22674.3 s | 32–40 m | 969 | 191 | 164 (41 m) |

**About 6,200 voxels in 6.5 s, climbing a chunk at a time.** Each stage's fall took the path
the stage above was hanging on. Water was exact (115 L lifted, 115 returned), and the region
settled at `at risk 0`. **I had bet on "a few voxels or nothing", and was wrong**: the reach rule
has no load, but a tower is one path, and cutting one path drops everything that depended on it.
Stated so nobody quotes the bet.

That run already has requirement 1's shape backwards. The first stages went 1 s apart, the next
4.5 s, the last 1 s. The pace comes from each chunk's deficit, not from how long the structure
has been failing.

## What exists (do not rediscover)

- **The support field** (M9a/b): each voxel carries a budget in `[−K, K]` (`K = 120`) along a
  path to an anchor. The reach per material is `supportDistance` in the JSON: stone 30 voxels,
  clay/dirt 8, glass 6, rubble 2. Hanging costs double; there is an arch refund. It is a
  Dijkstra per chunk on a worker. The join climbs across faces. **It is reach, not load. There
  is no weight anywhere.** A unique fixpoint, proven (M9-PLAN decision 1). A per-voxel load model
  would lose that proof, and the tint and the collapse both lean on it.
- **The analysed set**: every dug (edited) chunk and its six face neighbours. Anything outside
  it, or unloaded, anchors. A placed building is edited, so it is analysed.
- **The tint**: at-risk voxels are drawn strained, from settled fields only (decision 7's
  barrier).
- **The timer** (`CollapseScheduler`): one per analysed chunk. It arms when the chunk's settled
  field first marks something at risk, with delay `lerp(60 s, 2 s, deficit / K) × Exp(1)`,
  seeded from world time. It fires when due if settled; it is cleared at a fall and re-armed
  from the new deficit. **The cascade is that re-arm**; there is no cascade rule. **It disarms
  when the at-risk set empties**, so a fix inside the window already saves a roof. **A timer
  only shortens** (M9d): a partial fix that lowers the deficit does not buy time back.
  `--collapseafter=<min>,<max>` sets the window; `--collapse=0` arms and never fires.
- **The fall** (`Collapse.TryFall`): a chunk's settled at-risk voxels, followed down as column
  runs across chunk faces, land as **rubble in one sweep** (7–14 ms here). Lift, write, return
  for any water in the way, `lifted == returned + displaced`. **Nothing is seen falling**: the
  voxels are rock in one frame and rubble on the ground in the next.
- **Ctrl+Z is cleared by any fall** (UNDO-PLAN decision 4). Undo is no way out of a collapse.
- **The instruments**: the `M9:` line, `M9: fell` per fall, the `support` profile line; the
  bench's `--support --collapse` drops seven fixtures stage by stage with ASCII cross-sections;
  the headless oracle in CLAUDE.md (`--bitechunks=2 --collapseafter=1,2`).

## The gap between what exists and his idea

1. **The pace does not accelerate.** A chunk's delay is its deficit's, drawn fresh at each
   re-arm. Nothing knows that this chunk is stage five of a failing tower. Tetris needs a
   notion of *the structure's failure* with an age — a level that climbs.
2. **The unit is a chunk's whole at-risk set.** A 2,452-voxel stage falls as one event.
   Tetris-gone-bad wants pieces: parts of the failing set coming away one after another, within
   a chunk as well as between chunks.
3. **Nothing is seen falling.** The world is written at the landing at once. A visible drop —
   a piece's mesh moving down over a second or two, the world written when it lands — is a
   render question plus a rule for what the in-flight piece is (an entity, or voxels in transit
   the way nothing else in this game is).
4. **The save window is all or nothing.** Disarm needs the set to empty; a shore that halves the
   deficit changes nothing (the timer only shortens). "You might be able to fix it if you were
   quick" wants partial credit early, and less as the level climbs.

## Questions for the separate chat's planning pass (leanings, not decisions)

1. **What is "a structure" for the level?** Leaning: the connected set of armed chunks, joined
   across faces. Its age starts at its first arm, and it resets when every chunk in it is at
   risk 0. Stored nowhere, like the timers (decision 7): a quit forgets the level. That is a
   cost to state; a player could quit to reset a collapse.
2. **What does the level do?** Leaning: it shortens each re-arm's mean by a factor per stage
   (Tetris's gravity table), so stage *n* arms at `mean × rⁿ` — one constant, `--collapsepace=`.
3. **What is a piece?** Leaning: a face-connected component of the at-risk set, split to at most
   *N* voxels by a deterministic rule (seeded, per non-negotiable 1). Pieces fall one per sweep,
   bottom first. The ones above then re-derive and may fall faster.
4. **The visible drop.** Leaning: render-only. The piece's voxels leave the world at release and
   a mesh drops at gravity; the world gets rubble at the landing when the mesh arrives. The
   in-flight piece is Tier B's falling water in another costume. What the player's body does if
   they stand in the path is M9c-1's unstick. Worth its own sub-part, and last.
5. **Partial credit.** Leaning: let a re-derive that lowers the deficit re-draw the timer
   *longer* only while the level is low (stages 0–1). The M9d rule stays for the rest. That is
   "fix it if quick; good luck later" as one condition.
6. **Does rubble stay rubble?** Today, yes, and it credits nothing (M9c-1). Tetris would clear
   lines. **Not recommended**: the mine is the antagonist, and a cleared line is a reward.
   Recorded because the metaphor invites it.

## Where it goes

`main`, independent of the pond branch (`water-column-test`, whose verdict is still his). The
demolition world is kept as `worlds/Regions16-pondlook-demolished-2026-09-26/`, the measurement
world for this. His building is restored from `worlds/backup-Regions16-pondlook-2026-09-26/`.
The log of the demolition is `logs/demolition-2026-09-26.log` beside the play logs.
