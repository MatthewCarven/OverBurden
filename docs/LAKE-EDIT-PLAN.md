# Walls that split a lake — the code-level pass (2026-10-01)

**All seven decisions taken as recommended** (2026-10-01, *"as recommended please Claude"*). **L-1 and L-2 are
built** (2026-10-01, on the pond branch; "L-1 — built" and "L-2 — built" below), **and E-1** (2026-10-01, on
main, merged into the branch; "E-1 — built" below). Next is his play on `LakeTest`, the branch's deciding test.
His question about the walled area beneath TajMahal2 is still open.

His ask, verbatim in [LAKE-EDIT-NOTES.md](LAKE-EDIT-NOTES.md):

> next session we can cover the plan for the tier a to tier b conversion upon voxels being placed I just
> want a way to seperate tier a water sections from each other after the basin has filled a bit like other
> wise you have to make to big a wall/it isn't possible to flood only a specific area like the walled area
> beneath tajmahal2

**The answer in one paragraph.** His end is separation: a wall built through a filled lake should leave
one lake on each side, each holding what it held, each filling and draining on its own. His means was to
turn the lake's water near the wall into Tier B, which respects walls, and let it come back to Tier A once it
settles. **On this engine the means cannot reach the end.** Water handed back to Tier A goes to the one
basin it stands in, and Tier A has no wall in it, so the two sides are one lake again within seconds (facts
2 and 3). The wall has to be in Tier A's own model. **The pond branch, `water-column-test`, already puts it
there**: walls you place raise the watershed, and its plan's decision 4 was *"a wall in a lake divides it"*.
**But the branch splits the litres wrongly**. All of the old lake's water goes to the side that holds its old
pit, and the other side starts dry (fact 10). So the plan is: fix the split on the branch, so each side keeps
the water that stood over it, and let that be the branch's deciding test. Three read-only searches mapped
Tier A, the edit path and Tier B; the load-bearing facts were checked by hand.

---

## Facts from the code

1. **Tier A is one level per basin, built on the seed surface.** The watershed reads only the generated
   surface (`HeightField.cs:181`). A volume curve counts every sample below the level, with no connectivity
   (`VolumeCurve.cs:109–147`). The surface solves one level per basin (`WaterSurface.cs:178–183`). On main a
   wall changes no basin, no level and no litre: a block in a lake is a pier.
2. **Tier B cannot hold a cell inside a lake's band** (`LakeBoundedTerrain.CanHold`, `LakeCeiling.cs:544`).
   `Add`, `Return` and `Emit` refuse there. Any Tier B found inside a band is listed submerged and handed whole
   within about two boundaries (`SurfaceHandoff.cs:186–205`). There is no per-column override. One would be
   read in three places: the band's constructor (`LakeCeiling.cs:340–350`), the hand-off (`SurfaceHandoff.cs:165`)
   and the surface (`WaterSurface.cs:199`, `:271`).
3. **Hand-back goes to the basin under the sample** (`SurfaceHandoff.cs:217–243`), which on main is the one
   unsplit lake. Cells the enclosure says are walled in are never handed at all (`:165–173`). So converted
   water either rejoins the whole lake once it rests, or, inside an enclosure, stays Tier B for good. By
   reading the code (not measured), an 80 × 80 × 8-cell body at rest on open ground goes back in about 16
   ticks, 8 s.
4. **The enclosure walks through natural ground.** It asks only at the lake's top (`LakeCeiling.cs:345`), and
   reads open once a flood is 64 voxels from where it started (`Enclosure.cs:352–357`). A dam between natural
   banks never encloses; only a closed loop of placed material does (`Enclosure.cs:20–34`). That is his
   "too big a wall".
5. **The enclosure's answer depends on which cell asks first.** A flood that reaches its bound reads open,
   and that answer is cached for every cell it visited (`Enclosure.cs:395–406`). The bound is measured from
   the asking cell, deliberately ("a distance rule, not a size rule", `EnclosureTests.cs:206`). So a walled
   area 16–32 m across reads enclosed when it is first asked near its middle, and open when it is first asked
   near a wall. The answer can flip when an edit drops the cache. That is a candidate for the sheet that
   "bleeds in and out of walls", not a confirmed one. His LakeTest session (2026-10-01, 00:10) logged 1,389
   enclosed floods against 18,685 open at the bound, 17,278 of them out through natural ground.
6. **Portals ignore walls.** The portal drain draws by the sample's basin and the lake's level alone
   (`PortalDrain.cs:84–111`). A hole dug inside walls under a lake drains the whole lake into it.
7. **The courtyard hides area, not litres.** A walled-in column leaves the band and is drawn dry, but its
   water stays counted in the lake (`LakeCeilingTests.cs:384`, the courtyard test, asserts the litres
   unchanged). The outside level does not rise, so that water is held and drawn nowhere.
8. **The edit path asks nothing about lakes.** `Place.Put` asks only Tier B's `HoldsWater`
   (`Place.cs:1013–1018`); a block in a lake is written at the click and never queued. The undo writes raw
   (`EditHistory.cs:399`) and touches no water. The collapse has no lake read. Every write goes through
   `ChunkPipeline.SetVoxel` (`:926–988`), whose last hook reaches the enclosure (`Main.cs:1447–1459`).
9. **On the branch, a wall is in Tier A.** A cell's raise is the highest placed top in its 2 m footprint
   (`RaisedSurface`). The field samples the max of seed and raise, and the same ground is re-analysed after
   the raise has been quiet for a second (branch `Main.cs:1126`, `:4136`). The watershed's flood is
   4-neighbour (`Watershed.cs:192`), so a diagonal wall, whose raised cells touch at corners, still holds.
   Each side of a wall tied into higher ground becomes its own basin, and portals then draw by that basin,
   which also answers fact 6.
10. **But a split gives all the water to the side holding the old pit.** `BasinWater.CaptureByBasin`
    attributes by leaf, and `BasinLedger.Seed` pours by pit. The branch's `Rehome` moves only pits that
    vanished (branch `BasinLedger.cs:126–170`). `Add` fills the pit's side up to the wall's top before
    anything reaches the other side (`BasinWater.cs:98–124`, `:311–336`). So a wall across a full lake raises
    the pit's side toward the wall's top and drains the other side. Only what the pit's side cannot hold
    reaches the other side, and only what neither can hold leaves the world. **No test on the branch builds
    a wall across a filled lake.**
11. **There is no bulk door into Tier B.** IM-0 measured, for scale: a 10 ML pour inserts in 305 ms with a
    385 ms mean tick wall; 1 ML in 72 ms (IDEAL-MACHINES-PLAN, IM-0). `Restore` is the only put that lands
    water asleep, and it refuses a chunk already holding water.
12. **Tier A's identity is not evaluated in the game** (`WaterSystem.cs:567–569`). A new door is checked
    against its own counters, as the siphon's are.

---

## Decisions

### 1. The wall is seen by Tier A, through the pond branch's raise, not by converting water to Tier B — *taken as recommended (2026-10-01)*

- **(a) Tier A sees the wall.** The branch's raise with decision 2's fix. Separation is permanent, and its
  running cost is the branch's own (below), nothing more. **Recommended.**
- **(b) His conversion, on main.** Take the edited area out of the band (a new override, read in three
  places), move the lake's litres near the edit into Tier B, and let the hand-off give them back. By facts 2
  and 3 the sides are one lake again once the water rests, unless the area is enclosed, which is today's
  courtyard plus a pour.
- (c) Both. Under decision 2 a wall changes nothing visible at the moment it closes, so the conversion has
  nothing to do (decision 4 keeps it as a lever).

**Cost of (a):** the lake plan goes through the pond branch, so the branch's merge becomes this plan's route
to main. As measured (POND-PLAN, "Re-measured", 2026-09-29):
- his pond's tick is 1.9× main's, because the pond keeps the water main spills;
- about 10 ms a second of watched work and 92 ms once at open;
- a re-analysis is about 130 ms on a worker, after every quiet second of building.

And as stated by its own decision 2, a wall raises its whole 2 m cell, and a roof or bridge raises the cells
under it.

**Revisit:** if he retires the branch, (b) inside an enclosure is the only route left, and it separates
only closed loops of placed material under 32 m.

### 2. A split keeps each side's water: re-home by the old level, cell by cell, not by pit — *taken as recommended (2026-10-01)*

On a same-origin re-analysis (a raise, never a window move), each old lake's litres are shared among the
new basins **in proportion to the water that stood over each cell at the old level**: `(old level − old
height)` for every cell under it, counted to whichever new basin now holds the cell. Shares are whole
litres by largest remainder, so each old lake's total is exact. Each new basin then takes its share through
`Add`, so anything that no longer fits fills and spills by today's rules, counted. A window move stays by
pit, since the land did not change. For an unchanged basin this gives back exactly what it held, and for a
merge (a wall taken down) it gives what by-pit gives today, so the rule replaces `Rehome` for pits inside
the window.

- **Oracle (Core):**
  - a wall across a full lake leaves both levels where they stood, to the litre plus decision 3's rise;
  - pouring on one side raises that side only;
  - taking the wall down (or Ctrl+Z) gives one level again, the total conserved;
  - a ring round a dry area and a raise elsewhere move no litre;
  - the ledger's total before == after + spilled.

  Then a mutation battery: by pit, by area, the old height swapped for the new one, the remainder dropped.
- **Cost:** a pass over the window's 65,536 samples at each same-origin install, on the sim thread. That is
  an estimate (a few ms), measured in the slice.

### 3. Displacement: a raised cell's water goes to the basin that now holds the cell — *taken as recommended (2026-10-01)*

This is his *"displace the water"*. It falls out of decision 2's weights, because the old height is used.
A raised cell holds no Tier A water any more (its height is the wall's top), so the water Tier A had over
it raises the level of the basin that cell joins.

**Stated cost, at Tier A's grain:** a thin wall displaces its whole 2 m strip. Take a 25 cm wall across a
40 m lake, 2 m deep. It displaces 160 m³ where its stone is 20 m³, so a 40 × 30 m lake rises about 14 cm
instead of 2 cm (arithmetic). Counting only the stone would leave the rest of the strip's water with
nowhere to stand in Tier A: deleted, or converted. **Corrected at L-1:** the 14 cm spreads the strip over the
whole lake, which is right for a wall under the water. A wall that splits the lake sends each strip cell's
water to the side that cell now drains to, and the flood usually drains a whole wall one way (to the side with
the lower rim, then the lower floor). So that side, about half the area, rises by up to twice as much: about
28 cm in the example, and the other side not at all.

**Revisit:** a play where the rise reads too big. The lever is a finer raise, the branch's own question.

### 4. The moment between the wall and the re-analysis is accepted, and the conversion is its lever — *taken as recommended (2026-10-01)*

Until the raise has been quiet for a second and the re-analysis lands (about 130 ms on a worker), the lake
is still one. In that window, a pour on one side joins the whole lake, and a hole inside the walls draws the
whole lake through its portals.

**Revisit:** a play where a wall's first seconds read wrong. His conversion, scoped to the wall's cells, is
the named lever for that moment.

### 5. Built on the branch, in two slices, and it becomes the branch's deciding test — *taken as recommended (2026-10-01)*

- **L-1, Core:** the split by level (decision 2), its tests and battery, on `water-column-test`.
- **L-2, the wire:**
  - `M7d:` prints `split N lake(s), M L shared by level, D L displaced` beside the raise's terms;
  - a debug writer `--dam=` (a wall across the lake under the player, as `--courtyard` builds a ring);
  - a headless oracle with both levels before and after, then a pour on one side;
  - a screenshot on a copy of `LakeTest`.

POND-PLAN's "better" list gains a seventh criterion: **a wall across a full lake keeps both sides' water,
and R into one side raises only that side.** Then his play on the branch: on `LakeTest`, a wall across a
filled lake, and R into one side. Built in a worktree of the branch, since `start.cmd` builds whatever the
main folder holds.

### 6. On main, the enclosure's answer stops depending on which cell asks first — *taken as recommended (2026-10-01), a small slice of its own*

Today's rule is deliberate (fact 5). The proposal: a walled area reads open when **its own extent** passes
128 voxels in x or z, so every cell of one area gives one answer. That is the comment's *"an enclosure up to
32 m across is seen whole"*, made true from every cell. Today it holds only from near the middle.

- **Cost:**
  - the flood's visited bits grow to cover any 129-wide box holding its start (257 × 257 bits, 8 KB of
    scratch);
  - a flood still visits at most about 16,600 cells before it decides (arithmetic), and regions stay stored
    over their own box.
- **Changes:** walled areas 16–32 m across read enclosed from every cell. Both existing bound tests keep
  their answers.
- **Oracle:** every cell of a 30 m courtyard answers enclosed, and every cell of a 40 m one open, whichever
  asks first.

**Slice E-1, on main**, independent of the branch. If the branch merges, only the hand-off still asks, on
raised samples, so the fix narrows but still applies.

### 7. The ~95% hand-back stays with water bodies part a — *taken as recommended (2026-10-01)*

Under the raise, separation does not wait on the hand-back, so this plan does not need it. The question is
part a's (TODO's collision 1), and part a is parked on a far carry with O(1) removal. Out of scope here.

---

## The split

| Slice | Where | What | Oracle |
|---|---|---|---|
| **E-1** ✓ | main | The enclosure's bound as the area's extent (6). **Built 2026-10-01** | Every cell of one area answers alike; both bound tests unchanged; r6 to the digit |
| **L-1** ✓ | branch | The split by level, displacement included (2, 3). **Built 2026-10-01** | Decision 2's five; a mutation battery |
| **L-2** ✓ | branch | ~~`M7d:` terms~~ (done in L-1), `--dam=`, headless oracle, screenshot (5). **Built 2026-10-01** | Both levels kept, one side poured; r6 to the digit on the branch |
| **His play** | branch | `LakeTest`: a wall across a filled lake, R into one side | POND-PLAN's seventh criterion, and the branch's verdict |

## L-1 — built (2026-10-01, on `water-column-test`)

**The test written first went red as fact 10 said.** A trough of 32 samples at plane 7, then a wall across it.
The west side, which held the old pit, took 84,000 L and filled to its bank at plane 10. The east kept only the
12,000 L that overflowed into it. Now the west holds 64,000 L and the east 32,000 L, the east still at 7.

**What was built:**
- `BasinLedger.ShareByLevel(old, oldWater, next)` replaces `Rehome`. It removes the old window's rows, then shares
  each old lake (the node `BasinWater.LakeTopOf` names) by `(old level − old height)` over every cell, counted to
  the new basin holding the cell. Shares are whole litres by largest remainder and join the new pits' rows as
  additions. `Seed` then pours them through `Add`. A share counted to a cell that now drains off the window
  leaves the world, folded into `SeedOverflowLitres`.
- `WaterSystem.Install` uses it on a same-origin re-analysis instead of `Absorb`; a window move is unchanged.
- `WaterSystem.SplitLakes`, `SplitLitres` and `DisplacedLitres`. A lake counts as split when its shares, once
  poured, stand under two or more lake tops, so a wall under the water splits nothing. Displaced litres are the
  material under the old level: under each raised cell, the lower of the old level and the new top, less the
  old floor.
- `M7d:` prints `split N lake(s), M L shared by level, D L displaced` in place of the rehome term. That was L-2's
  first bullet, done here because the line read `RehomedPits`.

**Found on the way: `Rehome` deleted stored lakes.** It moved every ledger row inside the window that the new
window had no pit for. A row an earlier window had stored, whose pit lay inside this window but was not one of its
basins, went at the first raise. On the edge ring (where a window move puts it) it was deleted, counted as
water that would not fit back. Inside another basin's catchment it was poured into that basin. The test was red
on the old code (30,000 L gone). Rows the old window did not hold are now left alone, as a window move leaves them.

**Tests: twelve in `LakeSplitTests`,** four `RaisedSurfaceTests` rewritten. Decision 2's five oracles:
- a wall across a full lake: east exactly at 7, west at 3 + 64/12 with the strip, the banked row replaced, not
  added to;
- R into one side: that side to 8, the other unchanged;
- the wall taken down (Ctrl+Z): one level, 7.5, the total conserved;
- a ring round a dry area and a raise elsewhere: no litre moved, nothing split or displaced;
- conservation to the litre when the shares are not whole.

And the cases the battery needed:
- a ring built in a lake keeps the water that stood inside it, as its own pond at the old level;
- the remainder goes to the largest fraction;
- a wall under the water splits nothing and displaces its own height;
- a wall raised up through the surface splits the lake at the level it had above both pits;
- a pond filled with a block sends its water off the window, counted;
- a stored lake whose pit falls on a moved window's edge survives a raise.

**Thirteen mutations, all red.** The plan's four were by pit, by area, the new height for the old, and the
remainder dropped. The other nine:
- the old rows kept;
- the leaf's level for the lake's;
- displacement without its minimum;
- splits counted by destinations;
- outlet shares uncounted;
- the remainder to the smallest fraction;
- splits not counted;
- a share replacing the row;
- one lake per basin.

`leaf-level` and `lake-per-basin` are caught only by the wall raised through the surface, the one fixture whose
old lake spans two pits.

**Cost**, on the game's 512 m window at the profile seed (391 basins, a file-based bench, tiering off, 55 rounds
alternated): the share alone is 0.26 ms median with every basin full and 0.58 ms at a quarter full. By pit it was
0.03 ms. Decision 2 estimated "a few ms"; it is under one. With tiering on, the first arm's median was 3.2 ms. By
the arm order, that is code the JIT had not yet optimised, which the first re-analyses of a play will pay (an
inference, not measured separately). It runs once per re-analysis, after a quiet second of building.

**Suite 2,219 → 2,231. The r6 control on the branch build: 2601 / 325 / 3446 at 411**, to the digit, no `ERROR`.

**Stated consequences, none a decision:**
- **A ring built in a lake keeps its water as a pond of its own** at the old level. That is decision 2 applied;
  main's courtyard draws a walled-in column dry instead (fact 7).
- **A raised cell's water goes wherever the cell now drains**, off the window included. On the shore of a lake
  the lake is always the lower neighbour, so this needs a pond whose only rim was the window's edge.
- **A lake made of several pits has its ledger rows re-spread among them at every raise.** No litre moves between
  lakes, only which pit's row holds it. Each pit's row is now the water over its own catchment, where `Absorb`
  credited a shared slab to the leftmost pit. It matters only to a later window move that cuts the lake.
- **The fallback** for a lake whose cells weigh nothing is unreachable while `VolumeCurve.LevelAt` keeps any
  positive volume strictly above the floor, so no recipe can reach it. It moves the lake by its first pit rather
  than deleting it.

## L-2 — built (2026-10-01, on `water-column-test`)

**`--dam=<metres>`** builds a straight stone wall a metre thick across the lake. It goes through the player when
they stand over a lake holding 10,000 L or more, else through the pit `--fillbasin` filled, and
`--waterprobe=x,z` stands it elsewhere. It is written through the pipeline's edit path, air voxels only, as the
courtyard is. The details:
- **The top** is `--damheight=` (1) m over the water.
- **The ends.** Every column gets at least one block, so a column on a bank still raises its cell. Each end runs
  two cells past where the bank tops the wall, and the print says `tied into the bank` or
  `OPEN, the length ran out`.
- **The options.** `--damaxis=x` turns it east–west, `--damafter=` holds it for a windowed run whose chunks are
  still loading, and `--dampour=` pours into side A once the split is read.
- **Over a puddle** it builds nothing and lists the five fullest lakes in the window, each as a `--waterprobe=`.
  The first `LakeTest` run stood over 7 L, because his saved position had moved off the lake.

The `M8: dam —` lines read both sides, three cells off the line, at three points:
- before the build;
- once the installed window has caught up with the raise map that holds the dam;
- after the pour.

`M7d:`'s split terms came with L-1. The flags and both recipes are in COMMANDS.md, verbatim.

**The seeded oracle** (profile seed, basin 292, 1.2 ML):

| | Side A | Side B |
|---|---|---|
| Before the dam | one lake at −5.828 | the same lake |
| After the re-analysis (1.1 s) | −5.828, unchanged | −5.112, the wall's strip (+18 cm) |
| After 100,000 L into A | −4.249 | −5.112, unchanged |

Held 1,200,000 → 1,200,000 L, nothing would not fit back or spilled, 117,573 L displaced. The north end ran out of
length on ground below the wall's top (`OPEN`). It still splits the lake at this level, but a fuller lake could go
round it.

**On a copy of `LakeTest`**, aimed at his 4,175,582 L lake 33 m from his spawn (`--waterprobe=140,164`), both ends
tied into the bank:

| | Side A | Side B |
|---|---|---|
| Before the dam | one lake at −29.339 | the same lake |
| After the re-analysis (1.2 s) | −29.219, 1,104,810 L | −28.612, 3,066,336 L |
| After 100,000 L into A | −28.630 | −28.612, unchanged |

The sides sum exactly to the 4,171,146 L shared. That is 4,436 L under the "before" reading, because the lake was
draining and being rained on in the 1.2 s between. Here the strip went to both sides, cell by cell, most of it
east. **Decision 4's moment between the wall and the split was 1.1–1.2 s in every run.**

**The screenshot** (a windowed run on a fresh copy, `--damafter=13`, shots at 14 s and 26 s, his saved heading
of 312° NW): the wall stands out of the lake, with the water drawn on both sides and no sheet over the wall.
Its west end reads reddish. That may be the support tint (a stretch at risk), but no fuse or fall came in the
run's 26 s, and it is not checked. The two levels, 0.6 voxel apart, cannot be told apart at that distance.

**The r6 control on this build: 2601 / 325 / 3446 at 411**, to the digit, no `ERROR`. Core is untouched; the
suite was run anyway and reads L-1's 2,231.

**An incident, mine.** I checked for his game at the slice's start and not before each launch. He launched at
20:19:39, and my seeded run at 20:24:32 rotated the shared `godot.log`. His session's log stops there, and the rest
of it, to 20:28, is lost. His world was not touched, because a `--profile` run opens none. The check now goes in
the same command as every launch.

## E-1 — built (2026-10-01, on main, merged into the pond branch)

**The test written first went red as fact 5 said.** A 30 m courtyard (120 voxels inside), asked first from a
corner, an edge or its far side, read open. The flood had reached 64 voxels from the asker, and that answer was
cached for every cell it visited, the middle included. Asked first from the middle, it read enclosed. A courtyard
128 wide read open from everywhere, because the old bound effectively stopped at 127 inside.

**What was built**, in `Enclosure`:
- **`ExtentVoxels` (128).** A flood reads open once the area it has walked spans more than that in x or in z, or
  at an unloaded voxel. Every cell of one area now gives one answer, whichever asks first.
- **A 257 × 257-bit scratch** (8 KB, once), so an area that wide fits round any start.
- **`BoundVoxels` (64)** is now only how far round its start a flood keeps filling once it knows it is open. That
  is the box every open answer was cached over before, so open regions cover what they did and `MaxRegions` holds.
- **A region is stored over its own cells' box**, not the scratch. A courtyard's region is its courtyard.
- **The no-edits shortcut stays at two chunk columns.** In an area at most 128 across, the straight line east and
  west from any cell meets walls whose distances add to 129 or less, so one is within 64 voxels.
- **`FarthestOpen/Closed` (reach from the asker) became `WidestOpen/Closed`** (the area's width), printed on `M8b:`
  and the profile as `widest open / closed … across of 128`.

**Tests**: sixteen new cases.
- A 30 m courtyard is enclosed from every cell, whichever of four asks first, in one flood, its region the
  courtyard's 120 × 120.
- A 40 m one is open from every cell, opening at 129 across.
- An area 120 one way and 160 the other is open.
- At exactly 128 across it is enclosed and at 129 open, from a corner, the middle (64 voxels and two chunk columns
  from the nearest wall) and the far corner.
- An open flood still fills only the box round its start: 16,638 cells against 16,636 by the same count before.
- A pillar inside an area is answered by its own column.
- An L-shaped courtyard's notch, inside its region's box, reads open by its own flood.
- Both existing bound tests keep their answers.

**Twelve mutations, all red**:
- the old reach rule;
- the extent off by one;
- the extent on x only;
- an open flood filling the whole scratch, or stopping at once;
- walls left in a region;
- a region answering its whole box;
- the shortcut at one chunk or at four;
- both width counters short by one;
- the box bits counted at the scratch's size.

The "whole box" recipe is also caught by an existing ceiling test.

**Suite 2,175 → 2,191 on main, 2,247 on the merged branch. r6 to the digit on both**, no `ERROR`. The courtyard
oracle reads as recorded, `handed 0 L` with `6 flood(s), 6 closed`. On the merged branch the dam oracle is
unchanged to the litre (−5.828 / −5.112, then −4.249 / −5.112).

**Cost, on a copy of `LakeTest`** (headless at his spawn, `--profile --speed=0 --measure=30 --watch`, a fresh copy
each run, pre-E-1 and E-1 alternated twice):

| Run | Floods | Enclosed | Open at the bound / at an unloaded chunk | Cells visited | Enclosure |
|---|---|---|---|---|---|
| pre-E-1 | 95 | 2 | 93 / 0 | 834,443 | 3.24 ms/s |
| E-1 | 106 | 2 | 70 / 34 | 903,006 | 4.13 ms/s |
| pre-E-1 | 88 | 2 | 86 / 0 | 783,992 | 2.88 ms/s |
| E-1 | 96 | 2 | 66 / 28 | 805,616 | 3.92 ms/s |

The same enclosures in both arms, and about 1 ms a second more, with the worst single call 15–17 ms in both. The
extra cost has one cause. A flood started beside one of his walls now walks up to 128 voxels the other way before
deciding, and near the loaded ground's edge it meets an unloaded chunk first. That is still open, but its region is
dropped when the chunk arrives, so the flood is run again. It is under the pond plan's 5 ms bar. The lever, if it
shows in a play, is a bound on how far a flood walks from its start before it decides.

## For him, not a decision

- **The walled area beneath TajMahal2.** Was it built above the ground or dug below it? About how wide? And
  what went wrong: the lake's sheet showing inside, the lake getting in, or not being able to flood it on
  its own? Facts 5 and 6 are both candidates. If the walls stand above the ground, the raise answers both. If
  the area was dug below grade with walls only inside the pit, the walls are Tier B's, which already keeps
  them, and a hole there draws the lake above until the raise sees a rim.

## Not in this plan

- The ~95% hand-back (decision 7).
- Displacement on main without the branch: a block in a lake stays a pier.
- Seeing Tier A from under it (TODO's collision 7). Under the raise, raised cells draw from the water grid,
  so that look comes after L-2.
- IM-5a's surface dump into a walled pond. Under the raise, it lands in the pond's own basin (collision 2).
