# A pond you have shaped — the raised watershed

Matthew's play, 2026-09-25, on `Regions16`. He was edging a natural pond with stone. Two
reports, twenty minutes apart:

- *"Twice now i've done these edges and both times as I approached completion the water
  disappears."*
- *"if I modify the landscape, it does not fill to the new hight?"* Then: *"raised the edge
  and then tried flooding it with the r key."*

He took the four decisions in chat as recommended: *"yeah write the pass, as recommended"*.
This is the pass. It changes one locked decision. DESIGN's *"Edits do not change the watershed
at M7"* becomes *edits above the seed surface raise it; nothing lowers it*. Its revisit trigger
was a real player-made pond, and this is one.

---

## What the play log said

Read from the running session's `godot.log` (`Regions16`, 2026-09-25, 17:00–17:10).

1. **The water that disappeared was not lost.** Tier A stood at 52% of the window's capacity
   and climbed through the whole stretch (52 → 61 → 65 → 76 → 86%). What happened is that the
   enclosure's `closed` count went `0 → 3 → 8 → 10` as his rings came round, and `enclosed 0
   kept` jumped to `46,914 kept`. `WaterSurface` leaves a sample undrawn when `Enclosure` says
   it is walled in at the lake's level
   ([WaterSurface.cs:182](../Overburden.Game/Rendering/WaterSurface.cs)). That is right for a
   courtyard beside a lake. It is wrong for a ring around the lake itself, because the lake is
   inside it.
2. **The R pours went over the natural rim.** 260,000 L injected; `handed 279,392 L to Tier A`
   (7,974 cells found inside the lake); `spilled` climbed from 2.3 M L to 7.25 M L. The lake
   takes the water and spills it at the rim of the land as generated. His wall is invisible to
   it.
3. **21,987 L `displaced`.** Placing the edging at the waterline took voxels that held water, and
   the water had nowhere to go. That is correct accounting and a small real loss. It is not
   this pass's problem.

**Instrument gap.** The `enclosed sample(s) undrawn` count prints only in a profile report,
never on a periodic line. So the play log could show the cause only indirectly. Slice b puts
the raise's own terms on the `M7d:` line.

---

## Seven facts from the code

1. **The watershed samples one point per 2 m cell, at the cell's corner voxel.**
   `HeightField.Sample` reads `SurfaceHeightAt(originX + sx × 8, …) + 1`, the analytic seed
   surface. A wall one metre thick misses the corner three times in four. So a raise has to be
   **the maximum over the cell's 8 × 8 footprint**, not another point sample.
2. **"Placed above the seed" is already one predicate.** `RainOnPlaced` finds a voxel column
   whose top is the player's material by `OpenAboveY > SeedSurfaceY + 1`, cached per chunk
   column against `SkyColumn.Revision`. `OpenAboveY` is the first open voxel, the same plane the
   field stores (`h + 1`). So a raised sample is
   `max(seed sample, max over the cell's placed columns of OpenAboveY)` with no conversion.
3. **Every edited column is already in the world file.** `IWorldStore.LoadColumnIndex()` and
   `ReadColumn(x, z)` give every stored `SkyColumn`, and a chunk column with anything placed in
   it is dirty and written. **The raise map can be derived at open, with no new table.** A second
   copy would be one that can drift.
4. **A re-analysis at the same origin is already safe.** `Install` absorbs the old window into
   the ledger by pit and seeds the new one from it. A raise that adds capacity keeps every
   litre. A lowered rim returns the excess as `SeedOverflowLitres`, where the water leaves the
   world.
5. **But the ledger strands a pit that disappears.** `Absorb` touches only pits the *new* window
   contains. If a raise covers a basin's pit (a pier, or a pond filled in), the old pit's
   litres stay in the ledger at a coordinate no basin has, counted in `held` and never drawn.
   Window moves have never had this problem, because they do not change the land. A raise does.
6. **The lake ceiling bands by the table, not by the footprint.** `LakeCeiling` makes a column
   lake from `seed < Y ≤ floor(TableAt(sample))`, and `TableAt` answers the level of the basin a
   sample drains to, wet or not. A cell half wall and half outside ground, draining into the
   pond, would band its outside half. Tier B water there would then be "inside the lake" and
   handed to the pond from the wrong side of the wall.
7. **The surface's shore ring draws a whole cell.** A dry sample beside a wet one draws the wet
   one's plane and lets the terrain's depth clip it. In a cell that is half wall, the wall clips
   its half and the outside ground, being below the new level, does not. So up to a cell of
   water would show outside the wall.

---

## Decisions

### 1. The raise survives walking away — *taken as recommended, amended by fact 3*

Chat said *a small table in the world file*. **The pass departs from that: the map is derived
at open from the stored sky columns**, then kept live from loaded columns by their revisions.
A chunk column that unloads keeps its cells' raises in memory until the process ends, because
what it had loaded is what the store holds. The spirit of the decision is kept: a pond does not
drop when you walk away. It needs no schema 5, has no migration, and has no second copy to drift.
**The cost is an open-time scan** of every stored column: decode, 1,024 seed evaluations each,
on the serial lane before the first analysis. It is unmeasured. Slice b measures it on
`Regions13`/`Regions16`, and past ~200 ms it moves to a worker with the first analysis waiting
on it.

### 2. A raise is the cell's maximum placed top — *taken as recommended*

A one-metre wall anywhere in a cell raises the whole cell to its top. So a wall holds whatever
its thickness. **Stated costs:** a gap narrower than a cell does not leak for Tier A, though
Tier B finds it. **A roof or a bridge raises the cells under it**, because `OpenAboveY` cannot
tell a wall from a roof, so a pond under a placed roof is not the lake's there. A roof over dry
ground is a plateau and harmless. Rubble counts as placed, the same as for `RainOnPlaced`.
Nothing lowers a sample below the seed. A dug hole is Tier B's and stays that way.

### 3. The courtyard's hide-the-lake half retires — *taken as recommended, the condition reworded*

Chat's condition was *"if the courtyard check still reads `handed 0 L`"*. **Under the raise it
cannot read 0**, because the courtyard becomes its own basin and the pour is handed into *that*
basin, correctly. The reworded condition: **the courtyard provocation's pour stays inside the
courtyard** — the basin under the player holds it, its level stands between floor and wall top,
and the outside lake is unchanged. On that, the surface and the ceiling stop asking `Enclosure`
(decision 6 keeps what the hand-off needs). `--enclosure=0` stays as the control.

### 4. A wall in a lake divides it — *taken as recommended*

A dam or a pier raises cells inside a lake, and the priority flood cuts the lake there. Each
side becomes its own basin, holding its own water. That is the dam you would want.

### 5. A pit that disappears hands its water to the basin now under it — *new, recommended*

Fact 5's fix. On a same-origin install, any ledger pit inside the new window that is not a pit
of the new window has its litres re-homed. They go to the basin whose catchment now holds that
sample (`BasinAtSample`), through `Add`, so an overflow spills and is counted. The ledger entry
is removed. **Oracle: the ledger's total before a raise install equals the total after, plus
what spilled.** Test it with a pier over a pond's pit.

### 6. The hand-off keeps enclosed Tier B water only where the watershed cannot see the pool — *new, recommended*

If the hand-off kept asking `Enclosure` everywhere, a large courtyard would draw two surfaces:
Tier B kept on its grass, and Tier A's own basin raining into the same place. If it stopped
asking, a pool narrower than a cell would teleport into whatever lake the wall cell drains to,
which is the original courtyard bug. So the rule is: **ask `Enclosure` only when the rested
cell's sample is raised** (the pool sits inside a wall cell, which the watershed cannot resolve).
An unraised sample's basin is trusted. One predicate, and it is asked less than today.

### 7. The ceiling bands only where the raised field is under the level — *new, recommended*

Fact 6's fix. A column is lake only if its cell is wet to the watershed — the raised sample
below the table — the same test `WaterSurface.IsWet` makes. A wall cell above the level has no
band, so neither of its halves is lake. **The stated cost:** a Tier B cell resting in a wall
cell's inside strip is not "inside the lake". It rests, the hand-off hands it to the pond (the
cell drains there), and the net is the same water. It is one boundary late at the rim, and
invisible.

### 8. Re-analyse on a raise, after a quiet second — *new, recommended*

The map carries a version. When it moves inside the installed window and has been quiet for
**1 s** (an estimate), Main submits the same-origin analysis it submits for a window move:
65 ms Release / ~250 ms Debug on a worker (M7d-1, the 2 m cell). A raise during an analysis
waits for the next one. **Stated cost:** `Install` discards the sub-litre rain remainder per
basin per install. It is bounded, and it happens a few times a building session. `Install`'s
own revisit trigger names *anything that re-analyses often*, and this is not that. If a play
shows a drizzle not accumulating, carry the remainder by pit as that trigger says.

### 9. Slice b draws raised shore cells whole; slice c clips them per voxel — *new, recommended*

Fact 7. The water reaching the wall matters more than a sliver past it: a dry strip inside the
wall reads as *"it does not fill to the new height"*, which is the complaint. So b draws the
ring into a raised cell whole, and the first screenshot says how much shows outside. **Slice c
is the lever:** in a raised ring cell, draw the plane only over the voxel columns reached from
the wet side without crossing a placed column. That is an 8 × 8 flood, on a handful of cells, on
the sim thread at rebuild. Slice c is built only if the screenshot shows the sliver.

---

## The split

| Slice | What | Oracle |
|---|---|---|
| **a — Core** | `RaisedSurface` in `Overburden.Core/Hydrology/`: per chunk column, 16 cells' raise off a `SkyColumn` and the seed, cached by revision; a version; an immutable `Snapshot(window)` for the worker. `HeightField.Sample` takes an optional snapshot and takes the max. `BasinLedger` re-homes a vanished pit (5). | **No raise is byte-identical to today's field**, and the r6 control is to the digit. A ring of placed stone around a natural pond raises its saddle to the ring's lowest top, and the capacity grows by the ring's inside area × the rise. Removing the ring spills the excess. A pier over the pit re-homes its litres (the total is conserved). A roof over the pond reads as raised (the cost pinned). |
| **b — the wire** | The open-time derive from the stored columns (1) with its ms printed; the live map fed where `RainOnPlaced` is fed; the re-analysis (8); the ceiling's raised-cell rule (7); the surface drawing raised ring cells (9) and dropping `Enclosure`; the hand-off's narrowed ask (6). `M7d:` gains `raised N sample(s) in M chunk column(s), K re-analysis(es) for an edit, derived at open in T ms`. | Headless: `--courtyard=8` with the pour lands in the courtyard's own basin, the outside unchanged (3); the r6 control to the digit; every identity exact. Then his play. |
| **c — the look** | The per-voxel ring clip (9), only if b's screenshot shows water outside the wall. | A screenshot on a copy of his world, before and after. |

**Read before a's code:** `RainOnPlaced`'s placed-set scan (the predicate is reused, not copied),
`Watershed.Analyse`'s packed key (the raised height must stay inside `MaxAbsoluteHeight`), and
`BasinLedger.Absorb`.

**What his world needs from b:** `Regions16`'s pond is full to its natural rim now. After b the
rim is his wall, and R fills it: 10,000 L a press, handed to the pond, now held. At a guessed
40 × 30 m inside the ring, a metre of rise is ~1.2 ML, about 120 presses of R, or Ctrl+R once.
Rain fills it on its own at the scaled rate.

---

## Built

*(Nothing yet.)*
