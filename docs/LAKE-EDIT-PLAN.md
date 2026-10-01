# Walls that split a lake — the code-level pass (2026-10-01)

**All seven decisions taken as recommended** (2026-10-01, *"as recommended please Claude"*). Nothing is
built yet. The next session builds L-1 on the pond branch, in a worktree of the branch; E-1 on main can go
alongside. His question about the walled area beneath TajMahal2 is still open.

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
nowhere to stand in Tier A: deleted, or converted.

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
| **E-1** | main | The enclosure's bound as the area's extent (6) | Every cell of one area answers alike; both bound tests unchanged; r6 to the digit |
| **L-1** | branch | The split by level, displacement included (2, 3) | Decision 2's five; a mutation battery |
| **L-2** | branch | `M7d:` terms, `--dam=`, headless oracle, screenshot (5) | Both levels kept, one side poured; r6 to the digit on the branch |
| **His play** | branch | `LakeTest`: a wall across a filled lake, R into one side | POND-PLAN's seventh criterion, and the branch's verdict |

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
