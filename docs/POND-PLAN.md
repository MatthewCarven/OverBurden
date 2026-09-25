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

### 7. The ceiling reads the water grid — *amended by decision 10*

Fact 6's fix. The pass first proposed banding a column only where its 2 m cell is wet to the
watershed, which leaves a wall cell's inside strip out of the lake. **Superseded the same evening
by decision 10**: in a raised cell the band is the water grid's column, so the inside strip is
lake and the outside strip is not, to the voxel. An unraised cell bands exactly as today.

### 8. Re-analyse on a raise, after a quiet second — *new, recommended*

The map carries a version. When it moves inside the installed window and has been quiet for
**1 s** (an estimate), Main submits the same-origin analysis it submits for a window move:
65 ms Release / ~250 ms Debug on a worker (M7d-1, the 2 m cell). A raise during an analysis
waits for the next one. **Stated cost:** `Install` discards the sub-litre rain remainder per
basin per install. It is bounded, and it happens a few times a building session. `Install`'s
own revisit trigger names *anything that re-analyses often*, and this is not that. If a play
shows a drizzle not accumulating, carry the remainder by pit as that trigger says.

### 9. The surface draws raised cells from the water grid — *amended by decision 10*

Fact 7. The pass first proposed drawing a raised ring cell whole (a sliver past the wall), with
a per-voxel clip as a later slice c. **Superseded by decision 10**: in the loaded box a raised
cell draws one quad per run of water-grid columns, so the water meets the wall on the inside and
stops at it on the outside. Slice c is gone. An unraised cell draws as today (one quad a cell,
the terrain's depth clipping the natural shore), and so does a raised cell outside the loaded
box, where there is no grid and a sliver at a kilometre does not matter.

### 10. The water grid — *Matthew's, 2026-09-25, folded in on his word*

*"What do you think of the idea of water_column, and water_grid? its only an idea and it might be
a bad one"*, and on the reply, *"lets continue and fold it into the plan please"*. The reading
taken is the **derived** one, not the simulated one: Tier A keeps one level per basin, and a
**water grid** records, per voxel column in the loaded box, where that level stands as a
**water column**, or that there is none. A simulated per-column water sheet (a heightfield that
flows) was the other reading. It is rejected because one column holds one sheet, so it cannot
put a lake over a flooded tunnel. It also exists only where the world is loaded, and it would
make Tier A a transient that must terminate. The reply to him gives the three reasons.

- **Where it is built: only in chunk columns with a raised cell.** An unraised cell's answer is
  already exact per column (`LakeCeiling`: `seed < Y ≤ floor(level)`), so the grid there *is*
  today's band and nothing is computed. A raised cell is where the 2 m answer is ambiguous, so
  that is the only place the grid does work.
- **How: a 4-neighbour flood over voxel columns, per raised component.** Seeds are the lake
  columns of the unraised wet cells bordering the component, each carrying its basin and level.
  The flood enters a column whose top (seed, or its placed top where it has one) is below the
  level, and stops at one at or above it. So a wall of any thickness stops it, a gap at the water
  line lets it through (truthfully, since the water would go there), and a submerged placed
  block is water over a block. **Padded one cell (8 voxels) round the chunk column** so a wall
  zone on a chunk seam finds its seeds.
- **Two lakes reaching one column take the lower level.** This can happen only where the 2 m
  analysis could not see that the lakes join. The lower level cannot draw water above where it
  can stand.
- **Rebuilt where `LakeCeiling` already rebuilds**: at the tick boundary, for a chunk column whose
  sample tops moved, or whose raise or placed columns moved. The arrays are replaced rather than
  mutated, so a worker's view keeps reading one consistent grid (M3a's copy-on-write, as the
  bands are).
- **Readers:** the ceiling (7) and the surface (9). **Not the hand-off**: a rested Tier B cell
  in a wall cell that the grid calls dry is either a pool narrower than a cell or the outside
  strip, and "is it closed off" is a question the grid cannot answer. So decision 6 stands
  (`Enclosure`, asked on a raised sample only).
- **Stated costs.** A wall zone wider than the pad, with no unraised wet cell within a cell of
  the chunk column, finds no seeds and reads dry. That is the conservative failure: water
  missing, never water outside a wall. A pond whose whole floor is placed has no unraised wet
  cell and no grid water, and that is right, because its rain is Tier B's (`RainOnPlaced`) and
  it holds no Tier A.
- **Performance: roughly even, one modest win.** The flood is 48 × 48 columns at most, and only
  in chunk columns you have built in. Against that, `Enclosure` stops being asked by the surface
  (every wet sample within 16 m of an edit, up to 16,641 cells an open flood; 1,432 floods in his
  session) and by the ceiling. The honest headline is accuracy: the shore is exact wherever you
  have built.

---

## The split

| Slice | What | Oracle |
|---|---|---|
| **a — Core** | `RaisedSurface` in `Overburden.Core/Hydrology/`: per chunk column, 16 cells' raise off a `SkyColumn` and the seed, cached by revision; a version; an immutable `Snapshot(window)` for the worker. `HeightField.Sample` takes an optional snapshot and takes the max. `BasinLedger` re-homes a vanished pit (5). **`WaterGrid` in `Overburden.Core/Fluid/`** (10): the padded flood over a raised component, one water column per voxel column, replaced rather than mutated. | **No raise is byte-identical to today's field**, and the r6 control is to the digit. A ring of placed stone around a natural pond raises its saddle to the ring's lowest top, and the capacity grows by the ring's inside area × the rise. Removing the ring spills the excess. A pier over the pit re-homes its litres (the total is conserved). A roof over the pond reads as raised (the cost pinned). **The grid:** a one-voxel wall across a cell's middle is lake inside and dry outside; a gap at the water line floods through it; a wall on a chunk seam finds its seeds through the pad; an unraised chunk column builds no grid and its bands are unchanged to the column. |
| **b — the wire** | The open-time derive from the stored columns (1) with its ms printed; the live map fed where `RainOnPlaced` is fed; the re-analysis (8); `LakeCeiling` reading the grid in raised cells (7); the surface drawing raised cells from the grid and dropping `Enclosure` (9); the hand-off's narrowed ask (6). `M7d:` gains `raised N sample(s) in M chunk column(s), K re-analysis(es) for an edit, derived at open in T ms`, and the ceiling's line `grid G chunk column(s), C water column(s), F ms`. | Headless: `--courtyard=8` with the pour lands in the courtyard's own basin, the outside unchanged (3); the r6 control to the digit; every identity exact. A screenshot on a copy of his world: water to the wall inside, none outside. Then his play. |

**Read before a's code:** `RainOnPlaced`'s placed-set scan (the predicate is reused, not copied),
`Watershed.Analyse`'s packed key (the raised height must stay inside `MaxAbsoluteHeight`), and
`BasinLedger.Absorb`.

**What his world needs from b:** `Regions16`'s pond is full to its natural rim now. After b the
rim is his wall, and R fills it: 10,000 L a press, handed to the pond, now held. At a guessed
40 × 30 m inside the ring, a metre of rise is ~1.2 ML, about 120 presses of R, or Ctrl+R once.
Rain fills it on its own at the scaled rate.

---

## Built on a branch: `water-column-test`

Matthew, 2026-09-25: *"lets fork here to water column test branch, because I'm not sure and the
only to really know is to build … if it works out better we will merge it with the main if it
works out worse we can retire the branch with the reason why"*. The branch forks from main at
this commit. Slices a and b are built there, and main does not move for them.

**Why the branch is cheap: no world-format change.** The raise map is derived from columns the
store already holds (decision 1), so the branch writes nothing that main cannot read. A world
played on the branch and reopened on main loses only the water standing above a natural rim. The
ledger seeds it, it does not fit, and it leaves as `SeedOverflowLitres`: printed, not a fault.

**"Better", fixed before the build so the verdict is made against it:**

1. **His pond fills to his wall.** R into the stone ring on `Regions16` (or a copy) rises past
   the natural rim and stops at the wall's lowest top; `spilled` does not climb while it is below
   it.
2. **The water stays when a ring closes**, and the shore meets the wall inside and stops at it
   outside: a screenshot on a copy of his world.
3. **The courtyard still holds**: `--courtyard=8` with a pour keeps the water inside it and
   leaves the outside lake unchanged.
4. **Nothing else moved**: the r6 control to the digit, the suite green, every identity exact,
   the keystone and the a-3 pump oracles as recorded.
5. **The costs are printed and acceptable**: the open-time derive, the grid's ms a rebuild, and
   the re-analyses an editing session triggers. The bar is nothing he notices in play; a number
   past ~5 ms on the sim thread gets its lever named before the merge.
   **Measured 2026-09-26** (WORKLOG, the table of watched functions: main against the branch
   three runs each, alternated, on a copy of `Regions16` at his pond): the derive at open is
   94.5 ms once; observing the box is 0.050 ms a frame (2.4 max); the grid build is 0.073 ms a
   chunk column and its get 0.014 ms; a re-analysis is ~130 ms on a worker. **The enclosure
   falls from 13.3 ms a second to 3.3**, so the net is about +1 ms a second. **One call read past
   the bar, the worst grid build at 4.0–6.0 ms in every run, and it is the JIT, not a column.** A
   probe printed every build over 1 ms: build #1 at 5.4 ms (a light column, nothing built in
   reached), then one at 1.3 ms, and the other 1,040 under a millisecond. It is paid once a launch,
   and a per-boundary cap could not bound one call, so none was built. **Criterion 5 is met.** The
   tick is
   6× main's (200 against 33 ms), and that is his pond holding the pour**: 406 kL of it drains
   into Tier B through the dug ground under it. The branch with `--raise=0` ticks as main does.
6. **His word.** The play verdict decides. The five above are what a *"no"* would be read against.

**Retired instead?** Then this section records which of the six failed and why, the branch is
deleted, and main's DESIGN amendment is reverted to *not built, and why*.

## Built

### a-1 — the raise and the ledger (2026-09-25, on `water-column-test`)

`RaisedSurface` in `Overburden.Core/Hydrology/`: `Observe(SkyColumn)` rescans a chunk column's
sixteen cells when its revision moves (the cell's max over `RainOnPlaced`'s placed predicate),
`Version` moves only when a cell's answer does, `DeriveFrom(IWorldStore, ISurfaceHeights)` seeds
each stored column and copies its heights over as the streamer's restore does, and `Snapshot()`
freezes a copy for the worker behind `IRaisedSamples`. `HeightField.Sample` and
`WaterSystem.Analyse`/`AnalyseFor` take an optional snapshot. `BasinLedger.Rehome` runs in
`Install` on a same-origin re-analysis only, counted as `RehomedPits`/`RehomedLitres`, with water
that now drains out folded into `SeedOverflowLitres`.

**Seventeen tests, sixteen recipes, all caught after one fixture fix.** The miss was an x/z swap
in the field's lookup, because the fixture's raised samples sat on the diagonal. **The window-move
control is load-bearing**: *rehome on every install* fails only
`AWindowMoveNeverRehomesAPitThatFallsOnItsEdge`, which is why the rehome is gated on the origin
and not on "a pit went missing". Suite 1,888 → 1,905. The game passes no raise yet, so its
behaviour is unchanged by construction; the r6 control is b's to read.

**One thing not tested:** a re-homed pit whose sample now drains out of the window (`left`). The
bowl fixture cannot make that happen without an edge-cell raise, and it is one line.

### a-2 — the water grid (2026-09-25, on `water-column-test`)

`WaterGrid.Build(chunkX, chunkZ, ISkyColumns, IWaterTable, cellVoxels)` in
`Overburden.Core/Fluid/`, returning a frozen `WaterColumns` (per voxel column a level or none)
or **null for a chunk column with nothing placed in it**. An unbuilt cell's columns answer their
sample's table exactly, as the band does. A built-in cell's columns are flooded 4-neighbour from
the unbuilt columns under their table in the padded window (one cell round the chunk column),
levels ascending, the first to reach a column keeping it, entering only a column whose floor
(placed top, else the seed plane) is strictly under the level. **It reads "built in" off the
sky columns themselves, not off `RaisedSurface`**, so the grid and the column it describes cannot
disagree. The analysis's levels can lag a raise by the re-analysis delay; that is the table's
business, not the grid's.

**Eleven tests, thirteen recipes, all caught after one fixture fix and one new test.** The fixture
fix: unloading the neighbour chunk column alone still let the pond reach the strip through the
diagonal ones, and that is the truthful answer, so the test unloads the whole west side of the pad.
The new test: *seeds at equal floor* passed until a natural ridge standing exactly at the lake's
level was built (`AnalyticSurface` under the fake columns). A flat world cannot make that shape.
Suite 1,905 → 1,916.

**Slice a is done.** b wires both halves into the game and needs the game closed.

### b — the wire, first pass (2026-09-25, on `water-column-test`), and a finding that stops it

Wired: `RaisedSurface` derived at open (`M7d: the player's material raises the watershed — N
sample(s) raised from C stored column(s) in T ms`), observed each frame over the loaded box,
passed as a snapshot to every analysis, and a same-origin re-analysis after a quiet second;
`WaterGrids` (the cache: nine sky revisions and 36 samples' floor/ceil as the key) read by
`LakeCeiling` (the band's tops from the grid where one exists) and by `WaterSurface` (built-in
samples drawn per voxel column at the source's live level, the ring kept out of them); the
enclosure off the ceiling and the surface under the raise, and asked by the hand-off only on a
raised sample. `--raise=0` is the control. `--waterprobe=x,z` points the `M7d:` line's new
`under …` term, `--fillbasin` and the analysis window at a column, and prints a watershed map
and the raises' heights in the profile report — **built because the first two probes measured a
basin near the world origin**: a profile's camera stands at the origin, so the window was 870 m
from the pond and the pour fell back to the nearest basin there.

**Measured.** The r6 control is to the digit. The courtyard: the basin under its centre has its
pit inside the walls and holds the whole 20,000 L at 6.89 between floor 5 and the lowest wall top
11, nothing else wet — decision 3's condition met. On a copy of `Regions16`: 297 samples raised
from 169 stored columns in 88 ms Debug at open; his pond (pit (−3496, 1024)) sits in a 6 × 4 m
ring of his edging, and a pour into it spills every litre at level 0.00 under `--raise=0`.

**The finding.** With the raise on, the same pour holds — 100 kL, 1 ML, 3 ML, 10 ML, **30 ML at
level 16.67, 0 L spilled**. His edging cannot hold that: **132 of the 297 raises stand more than
16 voxels (4 m) over their seed plane, many at exactly plane 40** (a line at x = −3144,
z = 1208…1248; another at (−3408…−3416, 1320)). Tall structures of his are damming the valley
four metres deep. That is decision 2's stated cost — *an open height cannot tell a wall from a
roof* — at a size the pass did not foresee on a played world. **Held here, on the working
agreement's rule, with the fix proposed to him rather than built**: a raise is the top of the
first solid run from the seed surface up (the first open voxel above the ground), so a roof or a
platform over air raises nothing and a wall still does. It needs voxel reads per placed column
(live from the streamer; at open from the stored chunk blobs), and it changes the grid's floor
the same way.

**Answered the same evening — *"then damming it is honest, like i am deliberately going to flood
that entire valley for spread and flood testing purposes"*.** The tall raises are his dam walls,
built for it. Decision 2 stands as built and the first-solid-run rule is **not** built. Its
revisit trigger: the first roof, platform or bridge over water that reads as a dam in play. The
30 ML held behind them is the rule working. b continues with the look (a screenshot on a copy of
his world) and then his play.

**The look, the same evening (screenshots on copies of `Regions16`, camera at (−874, 8, 266) m
looking NW by the new `--at=x,y,z`).** With no pour the two arms are the same picture, and that
is right: the lake stands where it stood and already met his stone band. The grid drew 190 of
his samples as 428 quads, the whole surface rebuilding in 5.3 ms. **With 1 ML poured at the
pond, `--raise=0` spills every litre and the picture does not move; with the raise the lake
holds it and stands 0.77 voxel higher, over the grass ridge and over the stone band except where
the band's top is above the water**. The shore there is sawtoothed to the voxel, and no water
shows outside a top that stands above it. The pond is part of the valley lake his dam walls
hold, so its edge is the dam and not the edging. b is done bar his play.
