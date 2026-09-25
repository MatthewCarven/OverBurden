# Glass — the planning pass

Matthew, 2026-09-25, building a walled reservoir with window openings on `Regions16`: *"what do I
have to do to get glass? Oven bake some rock? Sand as a material, like I don't really want to do a
disturbable surface like but then it is just moving a voxel on a condition like IDK? anyways how
do you reckon we get to glass from here?"* Then: *"write the glass pass please"*.

**Already decided, so not re-argued here:**
- **Glass comes from sand in a furnace.** The ladder's tier 1 (M10-LADDER-PLAN, "Tier 1 —
  steam") lists `furnace` (burner): *sand → glass*, beside iron, steel and lime.
- **Sand flowing is the one expensive behaviour in MATERIALS-NOTES** (granular flow: a second rule
  set with an angle of repose), deferred by recommendation. Nothing here needs it.
- **Translucency "shares M8's transparent pass"** (MATERIALS-NOTES' element table).

This pass is the decisions that stand between that and a pane in his window.

---

## Six facts from the code

1. **Opacity already runs end to end.** `"opaque": false` in a material's JSON reaches
   `MaterialDefinition.IsOpaque` and `MaterialTable.IsOpaque`. The mesher draws a face only
   against a non-opaque neighbour and **never between two voxels of one material**
   (`GreedyMesher`: `here == there` emits nothing). So a wall of glass draws only its outside, and
   stone behind glass draws its face. A non-opaque material's faces go to `ChunkMesh.Transparent`.
   The only shipped non-opaque material is air.
2. **Nothing draws the transparent buffer as transparent.** `ChunkMeshConverter.ToArrayMesh`
   takes an opaque and a transparent material, and `Main` passes `_material` for both. A glass
   block today would render as a solid pale block. The drawing is the one piece of new rendering.
3. **Solid and opaque are already two questions.** `MaterialTable.IsSolid` is *not air and not a
   fluid*, and it is what the collider, Tier B's holdability, support, the collapse and the
   enclosure ask. Glass is solid: you stand on it, water rests on it, it carries a roof. **None of
   those readers changes.**
4. **The sky column asks opacity.** `SkyColumn` holds one height per voxel column, the top of the
   topmost *opaque* voxel, and `ChunkPipeline` moves it through `OnVoxelChanged` with the same
   test. Every one of these reads that single height: the skylight seed (a light cell is a sky
   source only when all sixteen of its columns are open), the rain particles' occlusion field
   (`SkyHeightField`), the rain on the player's material (`RainOnPlaced`), a rain drop's "under a
   roof" skip, a solar panel's openness (`StreamerSolarSky`), and since the pond branch the raised
   watershed and the water grid. **As it stands, a glass roof would let the rain through as well
   as the light, and a glass dam would not hold the valley.**
5. **Light crosses a cell face by opacity, and occlusion is opacity.** `ChunkLight`'s conductance
   and `AmbientOcclusion` both ask `IsOpaque`, so glass passes light sideways and casts no
   ambient occlusion, which is what a window should do, with no change.
6. **The wet sheen is gated on skylight** (`Voxel.gdshader`: `wet = wetness × COLOR.r`). Any rule
   that lets full skylight under a roof also lets the sheen under it: a dry floor under a glass
   roof would gleam in a storm.

---

## Decisions

### 1. Glass is a roof to the sky column, and a window to light — *recommended*

**The sky column's test becomes `IsSolid` instead of `IsOpaque`.** One line in `SkyColumn` and its
twin in the pipeline. The consequences, all from fact 4:
- rain stops on a glass roof (particles, drops, the rain on placed tops lands *on* the glass);
- a glass wall raises the watershed, so a glass dam holds the valley;
- a solar panel under glass is under a roof;
- the wet sheen stays out from under it (fact 6).

**Light still comes through, by propagation rather than by the sky.** The cell under a glass roof
is not a sky source, so it takes its light from the lit cell above through the conducting face:
15 − 1 = 14, then a level a metre downward. A 3 m glass-roofed room reads 12–14 at the floor
against 15 outside: bright, and a touch under a roof. **Windows in walls are exact**, since
sideways light through a pane is the conductance that an opening already gives.

**Nothing moves for existing content.** No shipped material is solid and non-opaque (fact 1), so
every stored sky column stays valid and the r6 control cannot move. That is also the oracle.

**The other arm, recorded:** two heights a column, *open to light* and *open to rain*. That gives
exact sunlight under glass and a working greenhouse solar farm. The price is a `SkyColumnBlob`
format change, every rain reader switched to the second height, and a third shade channel, or an
accepted sheen, for fact 6. **Revisit trigger: a glass roof that reads too dark in play**, a
greenhouse being the likely one.

### 2. Glass draws in its own transparent material — *recommended*

A second `ShaderMaterial` for the transparent surface: the voxel shader's lighting and fog with
`blend_mix` and a fixed alpha (0.35, an estimate), a pale blue-grey layer colour, no wet sheen.
Its shared code goes in a `.gdshaderinc`, so the two shaders cannot drift. `Main` hands it to the
converter's transparent slot. **Nothing else is transparent in that buffer today** (the water has
its own meshes), so this changes no existing picture.

**Stated cost:** Godot sorts transparent objects per instance, not per face. Water seen through
glass, both being transparent, can sort wrong at some angles; so can glass seen through glass
within one chunk (rare, since faces between panes are culled). **Revisit trigger: an aquarium or a
flooded glass room that reads wrong.**

### 3. The numbers — *estimates, his to move*

`materials/glass.json`: `opaque: false`, hardness **0.4** (a hand pick takes it, and it breaks
easier than dirt at 0.8), `supportDistance` **6** (it spans a metre and a half, less than dirt's
8), `supportUnderHead` none, permeability **0**, density **2500**, `textureLayer` **12** (a
thirteenth `LayerColour` and jitter entry; the two tables are asserted to match). It credits and
places like any material. Mined glass comes back whole; nothing about glass breaks into shards
until someone asks for it.

### 4. How you get it for now: `P`, like clay — *recommended*

`P` seeds 64 cells of glass beside the relay pair, the panel, the cell, the pack and the clay,
until the furnace exists. **When the ladder's tier 1 lands, the recipe (sand + coal → glass) is
the source and the key retires**, exactly as clay's did not yet.

### 5. Sand waits for the furnace, and does not flow when it comes — *recommended*

Sand ships **with the furnace or with WORLDGEN-NOTES' sand band, whichever is first**, as a plain
material, dug and placed and falling only under M9's collapse like any other. *"I don't really
want to do a disturbable surface"* is the plan: granular flow stays MATERIALS-NOTES' deferred
item, with its own trigger there.

### 6. Glass lands past the ninth hotbar slot, and V reaches it — *recorded, not fixed*

The hotbar is carried materials in ascending ID order (`VolumetricInventory.MaterialInSlot`). A
new material's ID is appended to the world's table, so on a world carrying nine or more (his
does) glass is reached by `V`, not a number. The fix is M10g's inventory screen and hotbar
assignment, which is already planned. **Revisit trigger: glass being awkward to build with.**

### 7. Built on main, after the pond branch's verdict — *recommended*

Decision 1 changes the height the raised watershed reads. On main it is a one-line change that
the branch inherits on merge; on the branch it would tie glass to the branch's fate. Glass is
independent of the pond work, so it waits for his verdict on `water-column-test` (hours, not days)
and is built on main.

---

## The split

| Slice | What | Oracle |
|---|---|---|
| **g-1 — Core** | `SkyColumn` and `ChunkPipeline`'s sky test on `IsSolid`; `glass.json`; `ShippedContentTests` for its numbers. Tests: a glass voxel closes a column; a glass roof's cell below derives 14 and is not a sky source; glass beside glass emits no face, stone behind glass does; glass holds Tier B water and supports as its number says. | Suite green; the r6 control to the digit (no shipped material moved). |
| **g-2 — the look** | The transparent material and its `.gdshaderinc`; the thirteenth layer colour and jitter; `P` seeds glass. | A screenshot of a glass wall and a glass roof on a copy of his world: the room lit under it, the rain stopping on it. Then his play. |

**Read before g-1's code:** `SkyColumn.OnVoxelChanged` and `Seed` (the scan's predicate is in two
places), `ChunkLight`'s sky-source rule (to confirm the 14), and `GreedyMesherTests`'
`FluidsGoToTheTransparentSurface`, which is the template for glass's routing test.

---

## Built (2026-09-25, on main, the same night — *"glass as recommended please"*)

He took all seven as recommended, and asked for it on main at once rather than after the pond
verdict, which decision 7 had only been waiting on for tidiness. It is independent of the branch.

**g-1.** `SkyColumn.BlocksSky` asks `IsSolid` (both call sites), `LightTestWorld`'s from-scratch
oracle the same; `glass.json` (opaque false, hardness 0.4, support 6, permeability 0, density
2500, layer 12); `ShippedContentTests.GlassIsTheOneSolidBlockYouCanSeeThrough` pins its numbers
**and that it is the only shipped solid that is not opaque** — the fact the switch rests on; the
old-world ID test counts one more appended material. `GlassTests`: a glass roof closes the column
as stone does; the edit that closes a roof agrees with a rebuild; two panes draw no face between
them and stone behind a pane draws its face. **Five recipes, five caught.** Suite 1,888 → 1,893;
the r6 control to the digit.

**The pass's light prediction was wrong, and better than predicted.** It said the room under a glass roof would
read 14, then a level a metre down. **It reads 15 all the way down**: M5b's rule lets full
skylight fall free through any face that conducts, and a pane conducts. Glass is a true skylight,
which retires the recorded other arm's main reason (the dark greenhouse). **Two costs change with
it and are stated:** the wet sheen is gated on skylight, so **a floor under a glass roof gleams
in a storm** (fact 6, now true under glass); and **a solar panel under glass gets no sun**,
because its openness reads the sky column. Triggers: either one noticed in play.

**g-2.** `Voxel.gdshader`'s body moved into `Voxel.gdshaderinc`; `VoxelGlass.gdshader` includes
it under `blend_mix` with `VOXEL_GLASS` defining `glass_alpha` (0.35, an estimate; **0.15 since 2026-09-26, his play: *"Loving the glass … can we make it about 15% transparent?"*, read as 15% opaque**, `--glassalpha=` the sweep); the thirteenth
layer colour (a pale blue-grey) and jitter; `VoxelMaterial.CreateGlass` shares the rock array;
`Main.SetVoxelParameter` keeps sky brightness, wetness and the tint's space in step on both; `P`
seeds 64 cells of glass. `--courtyardmaterial=<id>` and `--courtyardroof=1` build the screenshot's
glass box. **Looked at**: from inside a roofed glass courtyard the hills show through the walls
with a pale tint and the grass inside is at full daylight; looking up, the roof is a faint pane
against the sky. **The glass reads a little milky at 0.35** — the alpha is his to move.
