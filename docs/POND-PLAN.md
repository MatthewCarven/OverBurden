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

**Not as built (found 2026-10-01, [LAKE-EDIT-PLAN.md](LAKE-EDIT-PLAN.md), fact 10).** The split
is right but the litres are not. The ledger keys water by pit, so after a same-origin
re-analysis all of the old lake's water goes to the side that holds its old pit. That side fills
toward the wall's top and the other side starts dry. No test built a wall across a filled lake.
The fix is LAKE-EDIT-PLAN's decision 2: share each old lake by the water that stood over each
cell, which is slice L-1 on this branch.

### 5. A pit that disappears hands its water to the basin now under it — *new, recommended; superseded 2026-10-01*

**Superseded by LAKE-EDIT-PLAN's decision 2, built as L-1** ("L-1 — built" there). A same-origin install now
shares each old lake by the water that stood over each cell, which covers a covered pit and also splits a lake
at a wall (this rule gave a split lake's every litre to the side holding the old pit). The rule below also took
rows the old window never held: a lake stored by an earlier window, whose pit lay inside this one but was not
its basin, was deleted or poured into another basin at the first raise. L-1 leaves those rows alone. The text
is kept as it was decided:

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
| **a — Core** | `RaisedSurface` in `Overburden.Core/Hydrology/`: per chunk column, 16 cells' raise off a `SkyColumn` and the seed, cached by revision; a version; an immutable `Snapshot(window)` for the worker. `HeightField.Sample` takes an optional snapshot and takes the max. `BasinLedger` re-homes a vanished pit (5; superseded 2026-10-01 by LAKE-EDIT-PLAN's L-1, which shares each lake by level). **`WaterGrid` in `Overburden.Core/Fluid/`** (10): the padded flood over a raised component, one water column per voxel column, replaced rather than mutated. | **No raise is byte-identical to today's field**, and the r6 control is to the digit. A ring of placed stone around a natural pond raises its saddle to the ring's lowest top, and the capacity grows by the ring's inside area × the rise. Removing the ring spills the excess. A pier over the pit re-homes its litres (the total is conserved). A roof over the pond reads as raised (the cost pinned). **The grid:** a one-voxel wall across a cell's middle is lake inside and dry outside; a gap at the water line floods through it; a wall on a chunk seam finds its seeds through the pad; an unraised chunk column builds no grid and its bands are unchanged to the column. |
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
7. **A wall across a full lake keeps both sides' water, and R into one side raises only that side**
   (added 2026-10-01 by LAKE-EDIT-PLAN's decision 5; L-1 built the Core half the same day). His play on
   `LakeTest` after L-2 is the test, and it is the branch's deciding one.

**Retired instead?** Then this section records which of the seven failed and why, the branch is
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
bowl fixture cannot make that happen without an edge-cell raise, and it is one line. *(2026-10-01: `Rehome`
is gone, replaced by L-1's share by level; its `left` is tested there, by a pond beside the window's edge
filled with a block, and the `outlet-uncounted` recipe goes red on it.)*

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

## The overhang pass (2026-09-26) — open air under the waterline, under something built

**His play on `start5` (a copy of `Regions16`, the million-litre pour).** The pond fills to his
wall and stays bound by the walls he built (criteria 1 and 2 on his word), and the glass is
answered. Then: *"I might have created a situation sheets might not be compatible with uh i built
above the water with overhangs"*. Then, looking under one: *"from the right it is filling into it
but I do not think we have a rule for where there is open air but below the waterline only that
it isn't falling in from the left side though"*.

**He is right, and it is decision 2's stated cost met in play.** The lake (left) cannot reach
under the overhang. The water coming in from the right is Tier B, from his diggings and pours,
which works voxel by voxel. The sheets are fine with overhangs; the missing rule is Tier A's.

### Facts from the code

1. **The raise and the grid both read a column's highest placed top.** The raise is
   `max(seed, OpenAboveY)` over a cell, and `WaterGrid.Build` sets a built column's floor to
   `OpenAboveY` where it is above the seed plane (`WaterGrid.cs`, the floor loop). `OpenAboveY` is
   the first open voxel above the *highest* solid, so an overhang reads as a wall up to its top.
   To the grid, the air under it is not there.
2. **The grid is built only for loaded chunk columns**, and only where a cell is built in. It
   reads `ISkyColumns` today, but the streamer holds the chunks' voxels and can answer them.
   **So reading voxels in the grid costs nothing at open**: the open-time derive (94 ms on
   `Regions16`) feeds the raise, which does not change here.
3. **A column's `Revision` moves only when its top moves** (`SkyColumn.cs`: an opaque write below
   the top returns early, and a clear below it is not the topmost). A block placed or taken under
   an overhang, or a doorway cut in a wall under a higher roof, moves nothing the grid's key
   reads. **Nothing in the streamer counts edits per chunk column today.**
4. **The band follows the grid.** `LakeCeiling` takes a built column's tops from the grid, and
   Tier B found inside a band is handed whole. So a column the grid gives a level is the lake's,
   and the Tier B water that ran in from the right is absorbed at the next boundary. No new
   hand-off rule is needed.
5. **A sealed room under a lake is why the raise must not simply drop.** If a roofed cell stopped
   raising, every 2 m cell wholly inside a walled, roofed room would read as lake. That is the
   courtyard problem in 3D, and the raise at 2 m cannot see the walls round it. The grid floods
   by connection, so it can tell an overhang (open to the lake at the side) from a room (walls
   all round).

### Decisions

1. **The rule lives in the grid, and the raise stays the highest top.** *Recommended.* Tier A
   keeps treating covered cells as land: a sealed room stays dry, and the open-time derive does
   not change. The grid decides what is actually reachable, voxel by voxel. **Stated cost**: the
   covered volume is not in the basin's curve, so a lake with overhangs stands slightly higher
   than its litres say, by the covered volume over the lake's area. The grid already makes that
   trade for a wall's inside strip. *Rejected*: the raise reading the lowest gap floods every
   sealed roofed room under a lake.
2. **A column's water room is its lowest open run above the natural ground: `[floor, ceil)`.**
   *Recommended.* `floor` is the first open voxel at or above the seed plane, and `ceil` the first
   solid above that, unbounded where nothing is placed above. A natural column is `[seed plane,
   ∞)`. Under an overhang the run is the ground up to the overhang's underside. For a wall the
   run starts at its top, as today. **A window's run starts at its sill, and a doorway's at the
   floor**, so water goes through each where it would really go. Only columns in built cells
   whose top is above the seed plane are walked, up to `OpenAboveY` at most. **Open means not
   `IsSolid`**, so glass is a wall, as it is to the sky.
3. **A neighbour is entered only where the two runs overlap below the level.** *Recommended.*
   The test is `max(floorA, floorB) < min(ceilA, ceilB, level)`, where today it is
   `floor[n] < level`. So two runs stacked one above the other with no overlap do not connect,
   and water never passes between them through a solid block. The flood's order, the lowest
   level winning, the pad and the strict wall rule are unchanged.
4. **The grid's key gains an edit count per chunk column.** *Recommended.* The grid now reads
   voxels below the top, which the column's revision never sees (fact 3), and a rebuild rule is
   dirtied by what a build reads, never by what it produced. `ChunkPipeline.SetVoxel` bumps a
   counter for the chunk column on any edit, and so does a chunk arriving. The key holds the
   counts of its nine columns beside their revisions. **Cost**: a bite anywhere in a built
   column, digging underground included, rebuilds its grid, at 0.07 ms a build on the table's
   numbers. *Rejected*: counting only edits above the seed plane, which saves nothing that
   matters and adds a place for the key to be wrong.
5. **Only the lowest run is modelled.** *Recommended, a stated cost.* In a column with two runs
   under the level — a submerged roof over a sealed room, with lake above the roof — the water
   above the roof is not drawn. The flood enters that column by the room's run, which the walls
   close off. It is water missing, never water inside a wall: the conservative failure, like the
   pad's. **Trigger**: a submerged building or an aquarium in play.
6. **An unloaded voxel inside a walk is solid.** *Recommended.* It ends the run, so water stops
   short where the world is not loaded. The fluid tier's rule, and the grid's own for an
   unloaded column.

### What it is measured against

- **Tests (Core)**:
  - an overhang beside a lake is wet under it at the lake's level, banded, and Tier B put there
    is handed;
  - a wall with a window holds to the sill;
  - a doorway at floor level lets the lake into the room behind;
  - a walled, roofed room under a lake stays dry;
  - two stacked, non-overlapping runs do not connect;
  - a block placed under an overhang (the top unchanged) rebuilds the grid, where today's key
    would not;
  - every existing grid, raise and courtyard test unchanged.
- **Oracles**: the r6 control to the digit, the courtyard's `handed 0 L`, and the pond pour's
  `0 L spilled, level 4.6` on a copy of `Regions16`.
- **Cost**: `--watch`'s grid rows on the table's workload. The build walks voxels now; the bar is
  the plan's 5 ms on the sim thread, with the first build's JIT noted as a one-off. *Estimate*: a
  few tenths of a millisecond a build, since only placed columns walk and each walks a wall's
  height.
- **His play**: `start5` at the same overhang. The lake should reach under it from the left.

### The split

One session: the Core change with its tests, then the wire (the grids get the world and the
material table), then the oracles and his play. Nothing here changes the world format.

### Not this pass: the clay roof

At the same quit he reported water *"leaking through my clay roof … might be residual or
horizontal travel"*. Clay's permeability is 0 and a Tier B cell cannot enter a solid voxel, so
the answer is where the water was born, not the roof. It is in TODO with the candidates and what
to capture next time.

### The overhang pass — built (2026-09-26, on `water-column-test`), all six as recommended

*"Yes please Claude."* `WaterGrid.Build` takes the world's voxels and the material table. A
placed column's run is walked (`Run`): up through solid from the seed plane, then up through
open. A neighbour is entered on `max(floorA, floorB) < min(ceilA, ceilB, level)`. `WaterGrids`
counts edits per chunk column through `VoxelChanged` / `ChunkArrived` / `ChunkForgotten` — wired
in `Main` beside the enclosure's three — and keeps the nine counts in the key. Given no voxels,
the rule is the one before the pass, which is how the old tests still read and how the new
overhang test proves it distinguishes the two.

**One rule the pass did not have: a walk stops at the highest level in reach.** The first
measurement on the pond workload read **15.2 ms for the worst build**, in the machine's fast
phase: his stone structures are ~30 m tall, and a solid column was walked from the ground to its
top. Above the highest table in the pad a run's start holds nothing, and its end changes no
overlap under the level, so both walks stop at `⌈highest⌉ + 1`. After it, in the slow phase:
worst build 4.6 ms, which is build #6, the first to walk (the JIT); every other build 1.2 ms or
less, and the mean 0.075 ms against 0.085 before the pass. **The reach is identical: 3,583
built-in columns under water, against 2,240 before the pass**, the lake now under his overhangs
and through his gaps. The pour reads `0 L spilled, level 4.61`, the courtyard `20,000 L at 6.89`,
and the r6 control is to the digit. Suite 1,940.

**A trap, noted.** The first run after the change read the water tick at 39 ms against 208 ms,
with the same water (407 kL drawn, as before). The machine-control rows said why: light derive
0.13 ms against 0.83, mesh 2.35 against 7.8. It was the fast phase, not the change. **Read the
generate / mesh / derive rows before any tick figure.**

**The battery:** 7 recipes, 7 caught after two fixtures were added. Ignoring the current
column's floor was missed by fixtures that only flooded upward, and now a roofed pocket beside
a raised floor catches it. Reading unloaded as open was missed because the fixture had no
unloaded voxel. **Not covered by a test: `Main`'s three hook lines**, which his play covers — a
block placed under an overhang changes the water under it at the next boundary.

### Water across a chunk column's edge — built (2026-09-26, on `water-column-test`), his option 2

**His next play showed two dry spots under the slab.** A temporary probe built every grid around
it twice, once with the shipped pad and once with a 64-voxel pad (not committed). The probe found
**116 columns dry only because of the pad**: a strip 2 m wide at the east edge of one chunk
column, where the water beside it belonged to the next grid and a grid never handed it on.
Nothing else in the area was wrong. All 1,718 other dry built columns were solid past the
waterline, with no stranded air pockets. So the dry grass corner in his third shot is the pad
limit, the stated cost in `WaterGrid`'s remarks. The fourth shot is not explained by the grid.

**What was built.** A built pad column that the neighbouring grid holds wet is a seed, at its
source's *live* level and one more hop. Within a level, lower hops flood first. The hop cap is
`WaterGrid.MaxHops` = 8 (an estimate: 256 m of built ground). `WaterGrids` keeps a version per
entry, moved only when a rebuild changed the water, and a grid's key holds its eight neighbours'
versions. A neighbour is read from the cache, never built, so asking never recurses round the
ring. **Three rules the option did not have, each found by a test or a measurement:**

1. **A handed seed takes the source's live table, not the neighbour's stored level.** Otherwise a
   drained lake lingers until the hop cap runs out. With it, the water goes within one round: the
   east grid's key sees the drain, and its version tells the west.
2. **The walk's height bound includes handed levels.** A window with no table of its own would
   otherwise walk nothing, and an overhang reached only through the seam would read as a wall.
3. **A version moves when the water moves by a whole voxel, not by a fraction.** The first
   measurement read **8,363 grid builds in 30 s against ~1,400**: a rising lake moved every
   handed level every boundary, and the ring of grids rebuilt each other. Every test the flood
   makes is an integer against the level, and the surface reads the live level, so floor and
   ceiling are what a neighbour needs to see.

**Measured** on the pond workload, slow phase (light derive 0.84 ms): **1,972 builds, 6.7 ms a
second of building** (3.1 before the change), lake ceiling refresh mean 10.1 ms (8.0), worst build
5.3 ms (the first walking build). **Built-in columns under water: 3,583 → 3,654** on `Regions16`,
and on the copy of `Regions16-pondlook` the probe's strip reads wet (4,338 → 4,454). The pour is
`0 L spilled`, the courtyard `20,000 L at 6.89`, r6 to the digit, suite 1,945. **Battery: 8
recipes, 8 caught.** The first run of it was void: the new test failed on the *unbroken* code,
because the edit it made was inside the west grid's own nine columns, so every recipe read as
caught. It was fixed and re-run from green.

## Re-measured on the optimised build (2026-09-29)

The 2026-09-26 comparison (the branch's tick 6× main's) ran on an unoptimised Core. Re-run with main
merged into a scratch worktree of the branch, both DLLs checked optimised, at the pond
(**`--at=-852,34,282`** on a copy of `Regions16`, recovered from `Regions16-pondlook`'s spawn), the play
flags and `--profile --speed=0 --fillbasin=1000000 --measure=30 --watch`, alternated:

| `--watch` mean, 65 ticks a run | runs | fluid tick | sheet solve, a pass | the pour |
|---|---|---|---|---|
| main | 5 | 16.7–18.9 ms | 11.1–12.2 ms | basin 342: all 1,000,000 L spilled, level 0.00 |
| pond branch (main merged) | 3 | **32.6–34.9 ms** | 19.1–20.0 ms | basin 512: **0 L spilled, level 4.71** |
| pond branch, `--raise=0` | 2 | 17.3–18.0 ms | 11.3–11.8 ms | basin 342: all spilled |

**1.9× now, and the reason is unchanged:** the raised pond keeps the pour, and with the raise off the
branch ticks as main. The pond's own watched cost nets about +10 ms/s (observing the box and columns
5.3 ms/s, the grid 9.3 ms/s, the enclosure down 4.2 ms/s), plus 92 ms once at open. Speed is not an
argument against the merge; the verdict is his look at the pond.

## His word (2026-09-30)

Not a verdict yet: *"not sure here um we could do further testing like put further testing and
optimisation on the branch for this test branch"*. So the branch stays, and the next work on it is testing
and optimising there. It is also the answer to displacement in Tier A's own terms: edits cut a lake's
capacity, so a block raises the level ([LAKE-EDIT-NOTES.md](LAKE-EDIT-NOTES.md)).
