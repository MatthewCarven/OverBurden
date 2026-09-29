# Reader E: materials, ores and what the machines are made of

Mapped 2026-09-29 against `main` (71e6eed plus the uncommitted brief). Read-only. Paths are relative to
the repo root. "Plan" means it exists only in a document. "Code" means it is built.

## The short version

- **Fourteen materials ship, and none of them is an ore or a metal.** Five are rock or soil (stone, dirt,
  grass, clay, rubble), two are blocks (glass, lamp), air, and six are machines or items (computer,
  lead_acid_pack, relay_pump, relay_outlet, solar_panel, vacuum_cell). **No iron, steel, copper, lead or
  coal exists as a material.** "Steel" is the name of a tool tier (`steel_pick`) plus a density of 7800
  on three machine bodies. "Lead" is only in the pack's ID.
- **There is no crafting in code.** The loader refuses `kind: "station"` with *"crafting is M10g"*
  (`Overburden.Core/Content/ContentLoader.cs:591-593`). No recipe loader exists and no `recipes/`
  folder. Every machine arrives through `P` or `--stock=`.
- **Adding a material is not JSON-only today.** Its look is a C# colour table
  (`Overburden.Game/Rendering/VoxelMaterial.cs:47-88`) plus a parallel jitter table (`:120`). Worldgen
  strata are C# (`Overburden.Game/Main.cs:8701-8707`). Three tests hard-count the shipped materials.
- **Worldgen places nothing but grass, dirt and stone.** `ApplyFeatures` is empty
  (`Overburden.Core/Worldgen/WorldGenerator.cs:501-505`). Any ore field moves the fixed-seed oracles
  unless it follows the relief precedent: off in the record, on only for a newly created played world.

---

## 1. What exists

### 1.1 Every shipped material (`Overburden.Game/mods/overburden/materials/*.json`)

The "default" column shows a key the file omits and the value the loader silently gives it.

| id | hardness | supportDistance | supportUnderHead | permeability | density | textureLayer | other |
|---|---|---|---|---|---|---|---|
| air | 0 | 0 | — | 1.0 | 1.2 | (0) | opaque false, replaceable true |
| stone | 3.0 | 30 | [[2,24],[10,12]] | 0.02 | 2600 | 0 | |
| dirt | 0.8 | 8 | [[1,4],[3,1]] | 0.35 | 1500 | 1 | |
| grass | 0.9 | 8 | [[1,4],[3,1]] | 0.30 | 1500 | 2 | |
| lamp | 0.5 | 8 | — | 0.0 | 900 | 3 | lightEmission 15 |
| rubble | 0.5 | 2 | — | 0.6 | 1800 | 4 | |
| relay_pump | 3.0 | 8 | — | 0.0 | 7800 | 5 | unit cells, machine pump |
| relay_outlet | 3.0 | 8 | — | 0.0 | 7800 | 6 | unit cells, machine outlet |
| computer | 1.0 | 2 | — | 0.0 | 2000 | 7 | unit count, placeable false, no machine block |
| solar_panel | 1.0 | 2 | — | 0.0 | 2500 | 8 | unit cells, machine converter |
| vacuum_cell | 3.0 | 8 | — | 0.0 | 7800 | 9 | unit cells, machine storage 5 GJ |
| lead_acid_pack | 1.0 | 2 | — | 0.0 | 4000 | 10 | unit count, placeable false, carried storage 3.6 MJ |
| clay | 0.8 | 8 | [[1,4],[2,1]] | 0.0 | 1800 | 11 | |
| glass | 0.4 | 6 | — | 0.0 | 2500 | 12 | opaque false |

**Stone, verbatim** (`materials/stone.json:1-9`):

```json
{
  "id": "stone",
  "hardness": 3.0,
  "supportDistance": 30,
  "supportUnderHead": [[2, 24], [10, 12]],
  "permeability": 0.02,
  "density": 2600,
  "textureLayer": 0
}
```

**A machine, verbatim** (`materials/relay_pump.json:1-23`):

```json
{
  "id": "relay_pump",
  "hardness": 3.0,
  "supportDistance": 8,
  "permeability": 0.0,
  "density": 7800,
  "textureLayer": 5,
  "unit": "cells",
  "machine": {
    "kind": "pump",
    "litresPerSecond": 100,
    "headMetres": null,
    "joulesPerLitre": 1000,
    "intakeRadiusCells": 1,
    "links": {
      "power": { "carries": "power", "role": "sink",   "via": "channel", "port": "channel" },
      "water": { "carries": "fluid", "role": "source", "via": "channel", "port": "channel" }
    },
    "ports": {
      "channel": { "default": 0, "min": 0, "max": 15 }
    }
  }
}
```

A machine is an ordinary material with a `machine` block. Nothing in the block says what the machine is
made of: no parts list, no recipe, no grade. The material half (hardness, support, permeability,
density) is what the body does as a voxel.

### 1.2 Tools (`mods/overburden/tools/*.json`)

| id | bite | radius | hardnessThreshold | cooldown | power |
|---|---|---|---|---|---|
| hand_pick | sphere | 0.4 m | 1.0 | 0.30 s | free |
| steel_pick | sphere | 0.5 m | 3.5 | 0.20 s | free |
| quantum_miner | cube (1 m) | — | 5.0 | 0.10 s | channel, 50 J/voxel |
| quantum_tunneller | tunnel (2 m) | — | 5.0 | 0.25 s | channel, 50 J/voxel |
| quantum_excavator | chunk (8 m) | — | 5.0 | 0.25 s | channel, 50 J/voxel |

`ToolTiers.Order` sorts tools by (threshold, bite, id), with no tier number
(`Overburden.Core/Content/ToolTiers.cs:30-38`). A fresh world starts on the strongest free tool (`:41-58`).

### 1.3 The loader and every key it accepts

`ContentLoader.LoadMaterials` (`Overburden.Core/Content/ContentLoader.cs:196-348`) deserialises into
`MaterialJson` (`:785-801`). This table lists every key it reads.

| key | default if omitted | bound at load | source |
|---|---|---|---|
| `id` | refused | identifier charset: lowercase a-z 0-9 `_ - .`, and `/` in the path | `:220-232`; `Identifier.cs:140-149` |
| `hardness` | **1.0** | none | `:321` |
| `supportDistance` | **0** | 0..48, refused outside | `:260-271`; `MaterialDefinition.cs:44` |
| `supportUnderHead` | none (dry everywhere) | heads strictly increasing; distances non-increasing and ≤ supportDistance | `:273-277`, `:502-550` |
| `permeability` | **0** (sealed) | 0..1, refused outside | `:248-258` |
| `density` | 1000 | none | `:325` |
| `textureLayer` | **0** (stone's look) | none | `:326` |
| `lightEmission` | 0 | 0..15 | `:234-246` |
| `opaque` | true | | `:328` |
| `fluid` | false | a fluid with a machine block is refused | `:288-292`, `:329` |
| `replaceable` | false | | `:330` |
| `placeable` | true | a machine must be placeable unless it is carried; a carried one must not be | `:304-315` |
| `unit` | litres | litres, count or cells | `:552-565` |
| `machine` | null | kind ∈ pump, outlet, converter, storage; `generator` and `station` refused by name; the via must be served | `:577-743` |

The JSON options set no `UnmappedMemberHandling` (`:47-52`), and no file in the repo sets it. So
**System.Text.Json's default applies and an unknown key is skipped without a word** (my inference from
the framework default). A typo such as `"permeabilty"` loads as permeability 0.

Tools accept `id`, `radiusMetres` (default 0.5, at most 4 m), `hardnessThreshold` (default 0, must be
finite), `cooldownSeconds` (default 0.25), `bite`, and `power {via, joulesPerVoxel}` (`:395-468`).

The loader bounds values at load, and `MaterialTable` bounds them again at use: permeability is clamped
with NaN taken as 0, supportDistance is clamped to 0..48, and a machine block that fails validation is
dropped (`Overburden.Core/Content/MaterialTable.cs:234-246`).

### 1.4 How a new material gets added today

1. **A JSON file** in `materials/`. Nothing else is needed for it to load, credit, place and count.
2. **A texture layer, which is C#.** Textures are generated in code: one `LayerColour` entry per layer
   (`Overburden.Game/Rendering/VoxelMaterial.cs:47-88`) and a matching entry in the `jitter` array
   (`:120`). The two are checked against each other at first frame, and a mismatch is a
   `GD.PushError`, not an assert (`:121-124`). The remark that textures *"become registry-driven at
   M1"* is stale (`:5-8`, `:23-24`). Loading textures from the mod is planned for M10i
   (`docs/M10-LADDER-PLAN.md:771`) and is not built. A material may reuse an existing layer: nothing
   forbids two materials naming the same `textureLayer`.
3. **A source in the world.** Worldgen places grass, dirt and stone only (`Main.cs:8701-8707`). Clay and
   glass exist only because `P` seeds 64 cells of each (`Main.cs:4658-4665`).
4. **Three tests hard-count shipped content** and fail on a new material:
   - `CollapseTests.cs:1044` expects 9 IDs appended to the v3 fixture.
   - `ShippedContentTests.cs:209` expects exactly 5 machine materials.
   - `ShippedContentTests.cs:242` expects exactly 2 non-placeable materials.
   
   `EveryFileOnDiskIsAccountedForInTheLoadResult` counts the directory, so it adapts
   (`ShippedContentTests.cs:37-53`). `TheShippedTiersStraddle…` requires some shipped tool to reach the
   hardest material (`:87-89`), so any material above 5.0 fails it.

---

## 2. What each property does to the physics

### 2.1 Hardness: a gate, not a time

`Carve.Take` removes a voxel only when `Hardness(material) <= threshold` (`Overburden.Core/Mining/Carve.cs:739-742`).
- **No per-material mining time exists.** Speed comes from the tool's cooldown and bite.
- **No per-material energy cost exists.** A costing tool pays its flat `joulesPerVoxel` for every voxel
  removed (`ToolDefinition.cs:25-37`; `Carve.cs:744-766`). The ladder plan says so in its own words:
  *"the tier is the shape and the voxel is the price"* (`docs/M10-LADDER-PLAN.md:2429-2430`).
- **The dug voxel is credited as itself.** `into?.Add(material, 1)` (`Carve.cs:765`) means there is no
  drop table and no ore-to-item mapping: digging `iron_ore` yields `iron_ore` litres.

What each shipped threshold takes:
- **Hand pick (1.0):** air, glass, lamp, rubble, clay, dirt, grass, and the three 1.0 items.
- **Steel pick (3.5):** everything shipped.
- **Quantum tiers (5.0):** everything shipped.

So a new ore's hardness picks which tier can dig it. For a quarry priced "per voxel of ore" to vary by
material, hardness would need a second meaning, such as a cost multiplier. That meaning does not exist.

### 2.2 Support (M9): `supportDistance`, the cost table, the arch

The rule is `SupportRule` (`Overburden.Core/Structure/SupportRule.cs`):
- The budget is K = 120 (`:53`). **M9-PLAN decision 1's K = 240 and its "stone 8, dirt 30" costs are
  superseded** (`docs/M9-PLAN.md:124-126`).
- A sideways step into material *m* costs `round(120 / supportDistance)`. A distance of 0 costs 240,
  which kills the path in one step (`SupportRule.cs:107-116`).
- Resting on the voxel below refunds `120 / 30 = 4` (`:61`, `:95`). The refund is a **world constant,
  not a per-material value**, because the uniqueness proof needs it (`:32-39`).
- Hanging from the voxel above costs `2 × max(4, cost)` (`:120`).

What that gives the shipped values:

| supportDistance | shipped materials | step cost | hang cost |
|---|---|---|---|
| 30 | stone | 4 | 8 |
| 8 | dirt, grass, clay, lamp, relay pair, vacuum cell | 15 | 30 |
| 6 | glass | 20 | 40 |
| 2 | rubble, solar panel, computer, pack | 60 | 120 |
| 0 (the default when omitted) | none shipped | 240 | 480 |

`SupportCostTable` is one byte per (2 m sample × voxel row × **material**): 512 rows × material count
(`Overburden.Core/Structure/SupportCostTable.cs:35`, `:82-157`).
- A dry chunk shares one table.
- A chunk under water rebuilds it every derive, calling `SupportDistanceAt` 512 × materials times
  (`:131-153`).
- **Its size and build cost grow linearly with the number of material IDs, including items and parts
  that are never placed.** Today that is 14 × 512 = 7 KiB. At the ladder's estimate of ~45 new IDs
  (`M10-LADDER-PLAN.md:745`) it would be about 30 KiB per wet derive (my arithmetic).

Three consequences for materials:
- **Vertical support ignores the material.** A column of anything rests at full budget, so a clay
  pillar shores a roof exactly as well as a stone one. This is recipe question 3 in M10d:
  *"did clay holding the roof as well as stone read wrong?"* (`M10-LADDER-PLAN.md:2798-2802`). A
  machine body is a 64-voxel pillar (`:518-520`).
- **Material only matters for spans and overhangs.** For example, a 0.25 m wire run more than two
  voxels from a wall is at risk at supportDistance 2 (`:100-107`).
- **A collapse turns every fallen voxel into `overburden:rubble`**, whatever it was
  (`Overburden.Core/Structure/CollapseDirector.cs:939-943`). The piece remembers its materials only to
  draw them (`Overburden.Core/Structure/CollapseEvent.cs:30-35`). **Ore in a roof that falls is lost as
  ore.** This applies to machines too: "rubble credits nothing".

There is no bedrock. The anchor is the edge of the analysed set (`docs/M9-PLAN.md:24-30`, `:158-187`).

### 2.3 Saturation: `supportUnderHead`

The curve is piecewise linear from head in metres to support distance, with the dry distance at head 0
and the last value held past the end (`MaterialTable.cs:146-170`). The shipped values are all
estimates "at the nuisance end" (`docs/M9-PLAN.md:239-245`):
- stone: 2 m → 24, 10 m → 12
- dirt and grass: 1 m → 4, 3 m → 1
- clay: dirt's curve steepened by a metre (WORKLOG 2026-09-20)

**The head comes from Tier A only.** It is the basin level over the column's 2 m sample
(`Overburden.Core/Fluid/Saturation.cs:30-42`; `Overburden.Core/Hydrology/WaterSystem.cs:456-460`).
- Tier B water standing in a dug room is not a head term.
- Permeability does not gate saturation. A sealed material under a lake loses support by its own curve
  like any other.
- A material with no curve never weakens, however deep the water. Every machine body and glass ship
  with no curve.

### 2.4 Permeability: seepage, clay, sweat and spring

`permeability` is read in two places: `VoxelFluidTerrain.TryGetRock` and `SeepNeighbourhood.Classify`
(`Overburden.Core/Fluid/IFluidTerrain.cs:150-180`, `:238-250`). The rules:
- **A seep face exists only against rock with permeability above 0**, and only inside a dug chunk
  (`Overburden.Core/Fluid/SeepIndex.cs:63-73`, `:101-106`, `:363`, `:683`).
- **The rate is Σ permeability × head × face area × 0.06 L/s** (`:117-124`, `:313`). The 0.06 scale is
  an estimate. Dirt seeps 17.5× faster than stone.
- **Clay at 0 is "the lining".** It grows no face, so a clay-lined wall stops sweating
  (`WORKLOG.md:2124-2146`). Every machine body also ships at 0, so every machine is a seal.
- **Sweat or spring is one global mode, not a per-material one.** `SeepMode` is a constructor argument
  and a flag, defaulting to sweat since 2026-09-11. *"The eventual rule is per material and needs no
  field"* (`Overburden.Core/Fluid/SeepMode.cs:12-13`) is plan, not code.
- **A sealed band under a lake does nothing today.** Tier C's table is derived from Tier A
  (`docs/WORLDGEN-NOTES.md:33-35`). Permeability only shapes seepage into dug voids; it does not perch
  a water table.
- **Water is not a material.** No `water.json` exists, and Tier B holds millilitres with *"no material,
  no JSON, no ID"* (`docs/COMMANDS.md:561-562`).

### 2.5 Density: read by nothing

`MaterialDefinition.Density` claims to *"feed collapse and volumetric inventory"*
(`MaterialDefinition.cs:73`). A grep finds only the loader, the placeholder and one Bench copy
(`Overburden.Bench/Program.cs:611`). It has no reader because:
- M9 rejected a load and density model (`docs/M9-PLAN.md:134`).
- The inventory counts voxels, not mass.
- MATERIALS-NOTES' use for density (ordering fluid grids by density) is not built.

### 2.6 Solid, opaque, fluid

`IsSolid` means neither air nor fluid (`MaterialTable.cs:125`). Any solid material:
- collides,
- carries and needs support,
- is a dam to Tier B,
- is a roof to rain (glass: the sky column asks `IsSolid`).

A solid with `opaque: false` is meshed into the transparent pass with the glass shader
(`Overburden.Core/Meshing/GreedyMesher.cs:192`, `:283`).

### 2.7 What a new ore must declare

| field | what it decides | if omitted |
|---|---|---|
| `hardness` | which tier can dig it (≤1 hand, ≤3.5 steel, ≤5 quantum) | 1.0: the hand pick takes it |
| `supportDistance` | span and overhang reach, and whether the roof above it holds | **0: every voxel of it that does not rest on something falls, and it cuts sideways paths through the stone around it** |
| `supportUnderHead` | how it weakens under a lake | dry forever: water never brings it down |
| `permeability` | whether a room dug beside it seeps | 0: sealed, so a vein is a dam |
| `textureLayer` + a C# colour | its look and its HUD swatch | 0: looks exactly like stone |
| `density` | nothing today | 1000 |
| `unit` | hotbar presentation | litres |

The one ore fixture in the suite is test-only: `copper_ore` with hardness 4.0, supportDistance 25 and no
permeability (`Overburden.Core.Tests/TestContent.cs:38-39`). At 4.0 the steel pick could not dig it.

---

## 3. The notes

### 3.1 MATERIALS-NOTES.md (2026-09-03), in full

**His proposal, verbatim** (`docs/MATERIALS-NOTES.md:9-11`, Matthew, 2026-09-03):
> *"Brainstorm with me, what elements could we have, I mean we could implement a chemistry set and just
> implement chemistry? IDK sounds too complex but it is something we could at least think about though?"*

**The recommendation** (`:18-43`): *"Do not implement chemistry. Implement materials that respond to
water, and let machine recipes be the chemistry set."* A voxel is one material, so mixtures are
forbidden, and "chemistry" is bounded to three shapes:
1. **Recipes:** litres in, litres out, with power and time, on the machine framework.
2. **Materials that respond to water:** permeability, solubility, support when wet.
3. **Fluid scalars:** salinity, acidity, heat. Later, if ever.

The volumetric inventory makes stoichiometry a ratio (`:31-34`). **The complexity cliff** is
arbitrary pairwise reaction: *"The moment a design needs two solids to react with each other in the
world, it has stepped off the cliff — and the fix is to route it through a machine"* (`:38-43`).

**The format trap** (`:47-64`): a milestone in a hurry writes a constant, and the JSON field stays
decorative. **Stale in part:**
- The path it quotes is `mods/minercraft/`.
- Permeability has been read since M8c-1 and supportDistance since M9a.
- Density is still unread.

Strata are *"data but not yet content"* (`:66-68`).

**The element list** (`:72-110`). The test for each: *"it does something to water, gravity, power, or
the ladder."*
- Rock: stone/granite, limestone (dissolves; lime → concrete), sandstone (permeable), shale (weak),
  bedrock (the anchor, superseded by M9: no bedrock).
- Loose: sand (flows when saturated; glass), clay (**shipped 2026-09-20**), gravel and dirt.
- Salts: halite, gypsum or sulphur (acid for the first battery), lithium brine.
- Ores: coal (steam), iron (picks, shoring, Edison cells), copper (wire), lead (first battery), nickel,
  bauxite → aluminium (*"made of electricity"*), uranium, gold (*"nothing but sparkle"*).

That is *"Twenty-two, against five today. Each is a JSON file and a texture layer"* (`:112-113`). The
texture layer half is untrue today (§1.4).

**The battery ladder is the chemistry** (`:117-130`). His words, 2026-09-03: the entangled miner *"will
eventually have an energy cost and must be recharged and only once you have the latest tier battery will
it be as effective as it is now."* The ladder runs lead–acid → nickel–iron → lithium. **Depth is the
ladder**: deeper strata carry the next materials and are wetter and heavier.

**Costs** (`:134-146`): two new behaviours beyond the plan.
- Solubility.
- Granular flow, *"the expensive one"*, which is deferred.

**Decisions** (`:150-165`):
1. Numbers are per material, read from JSON. Done for permeability; done for the support curve at M9a.
2. Solubility is its own pass after M9.

**The periodic table** (`:167-196`). His words, 2026-09-03: *"what if we used the periodic table for
element types? Too many? A lot would be useless and writing shaders for some of them … excluding any
gasses until we work out how to do volumetric 'gas', the reverse of rain, should be easier after we have
stabilised water."* The recommendation:
- Ship all 118 elements as data in `elements.json`, *"never let an element be a voxel"*.
- A material declares a composition in element mass-parts.
- A recipe is checked for element balance at load, as a warning with a tolerance and never as the
  engine's oracle.
- Shaders are per appearance class (about five), not per material.

**The general extraction rule** (`:198-204`): a material goes in, you name an element, and you take out
its mass fraction × efficiency; the rest is slag.

**The millilitre move** (`:206-214`): mass balance means outputs are not whole voxels. A voxel is
15,625 mL. The move is a header change. Its timing was later moved by ladder decision 6.

**Gases** (`:216-274`): option B, one grid per fluid kind keyed by material ID and ordered by
`density`. Taken the same day: *"Ok lets continue."* Only the internal seam was built.

**Not here** (`:276-284`): reaction chemistry, gases as materials, temperature, and weight (*"no
encumbrance, ruled twice"*).

### 3.2 WORLDGEN-NOTES.md (2026-09-11), in full

**His words, verbatim** (`docs/WORLDGEN-NOTES.md:3-7`):
> *"maybe we should have a discussion about world gen and what is permeable i think is the word? Like we
> are eventually going to have to revisit this to get like iron, coal, trees, bronze and anything else
> like?"*

And: *"thanks for reminding me that the plan does not call for that yet, but I do like thinking ahead"*.

**Already decided** (`:11-27`):
- The materials come from MATERIALS-NOTES.
- Bronze is a recipe. It is now brass, by ladder decision 18.
- Permeability is per material and read since M8c-1. Spring or sweat is *"a threshold on that number"*.
- Strata are data but not content.
- The cross-chunk feature stage has existed since M2c and placed nothing. When it starts placing, it
  must route through the relight path the way `SetVoxel` does.

**Placement is three jobs** (`:29-45`):
1. **Layers:** bands by depth with a noisy boundary. Clay over sandstone gives a perched table, but that
   is a revisit trigger on M8c, since a sealed band does nothing today.
2. **Seams and veins:** *"a vein is a second field with its own threshold, its own depth band and a
   material instead of air. Sampled on the same lattice so chunk boundaries agree"*. The threshold
   doubles as a volume knob.
3. **Surface:** trees. These need wood and leaves, and the first material that is neither solid nor
   fluid.

**Its three questions** (`:47-55`) still read as open there, **but the ladder answered all three**
(`docs/M10-LADDER-PLAN.md:27-32`, the plan's paraphrase of his additions, "taken as decided"):
- Iron is under the water table.
- The first age is stone and wood.
- Trees are found and planted, and grow on a worker as a pure function of time.

The ladder's instruction to update WORLDGEN-NOTES (`:803`) was never done. **The notes are stale.**

### 3.3 Related words of his

- **Wading, 2026-09-11** (`docs/M10-NOTES.md:315-318`): *"…i kind like the idea of having to wade
  through water until you can get enough iron and other tools given you have first constructed the wooden
  versions then the brass version the iron version then the steel version"*.
- **Ladder decision 18, Matthew's:** brass is copper plus zinc, not bronze.
- **Ladder decision 19, Matthew's:** stainless is iron + nickel + coal, with no chromium.
- **Ladder decision 20, Matthew's:** lithium is a deep solid ore, and brine is a later engine pass
  (`M10-LADDER-PLAN.md:522-535`).

---

## 4. Crafting and recipes in the ladder

### 4.1 In code: none

- The `station` and `generator` kinds are refused (`ContentLoader.cs:588-593`).
- `GameContent` says recipes *"land here as they arrive"* (`Overburden.Core/Content/GameContent.cs:7`)
  and has no recipe registry.
- `P` is *"retired when M10g's recipe exists"* (`Main.cs:4609-4612`).
- M10g (recipes, stations, parts, the inventory screen) is open (`TODO.md:65-73`).
- M11 carries the chemistry-set and periodic-table items (`TODO.md:175-181`).

### 4.2 What the ladder says each tier is made of (plan only; `docs/M10-LADDER-PLAN.md:115-241`)

| tier | materials | stations and recipes | needs from worldgen |
|---|---|---|---|
| 0 Hand | wood, brass (copper + zinc) | `workbench` (instant, whole voxels); `charcoal_pit` (wood → charcoal); `forge` (copper + zinc → brass; iron ore → iron). Example recipe: 48 wood + 16 stone → 64 `wooden_pump` (`:654-657`) | logs; shallow copper and zinc above the table; a clay band |
| 1 Steam | iron, coal | `furnace`: iron ore → iron, **iron + coal → steel**, sand → glass, limestone → lime. `machine_shop` makes cylinder, piston, gear, valve, rod. Boiler = 12 iron plate + firebox + gauge. Iron pump = 40 iron + cylinder + 2 brass valves + rod (`:643-647`). *"a machine is 2–4 voxels of ore per part."* Smelting: 15,625 mL iron ore → 5,000 mL iron + 10,625 mL slag (`:649-652`) | coal in bands; iron in blobs under the table; limestone, sand, clay |
| 2 DC | copper, lead, glass | `wire_mill`: 1 copper → 64 wire. Furnace: sulphur + water → acid; sand + coal → silicon. Lead-acid = lead + acid + glass jar; solar = glass + silicon + frame + wire | lead, gypsum or sulphur mid-depth; sand |
| 3 AC | steel, nickel, stainless | `arc_furnace`: iron + nickel + coal → stainless; bauxite → aluminium. `printer`: one feedstock, `stainless_powder`, makes every tier-3/4 part | nickel and bauxite deeper than iron |
| 4 Quantum | lithium, uranium | `fabricator` (channel, 500 kW, *"tens of minutes"*) makes the relay pair, computer, vacuum cell and link. The first relay pump costs *"a session of tier-3 generation (estimate)"* | lithium solid in the deepest band; uranium deepest |

The design rules around this table:
- **Parts are materials** with `unit: count` and `placeable: false`, one registry and ID table. About 30
  new IDs are expected (decision 8, `:396-407`).
- **The inventory stays in voxels.** Recipes are in mL, with a remainder kept on the station row
  (decision 6, `:364-382`).
- **Recipes are keyed by `Identifier`**, and there is no recipe ID table in the world (decision 7,
  `:384-394`).
- **Hand crafting is instant.** Only powered stations take time (decision 11, `:422-433`).
- **Conductors and pipes declare supportDistance 8** (decision 17, `:512-520`).
- All counts are estimates (`:723-745`).

### 4.3 What "steel" and "lead" mean today

- **Steel.** `steel_pick.json` is a tool tier (0.5 m, 3.5, 0.20 s). It exists because *"one tier cannot
  demonstrate a gate"*: its threshold sits between dirt and stone (`docs/TODO-HISTORY.md:689-694`).
  Steel as a material is plan only (tier 1's furnace). The only other trace is density 7800 on
  relay_pump, relay_outlet and vacuum_cell.
- **Lead.** `lead_acid_pack` is a finished item: count, not placeable, a carried storage machine. **No
  lead material exists.**

### 4.4 The plan's example JSON does not load as written

`M10-LADDER-PLAN.md:588-658` uses keys and values the loader either ignores or refuses.
- **Silently ignored** (my inference, §1.3):
  - `conductor` (`:629`, `:633`) and `fuel` (`:637`) at the material level.
  - `stockVoxels` (`:611`) in the machine block.
  - `dock` (`:625`), `watts` (`:606`) and `litresPerSecond` (`:613`) inside a link.
- **Refused:**
  - `via` values `shaft`, `pipe`, `crank`, `burner`, `surround`, `dc` and `adjacent:*`, because the
    via must be served (`ContentLoader.cs:571-576`).
  - `kind: "generator"`.

Its `textureLayer` numbers collide with shipped ones: coal 7 is the computer's, and 9, 10 and 12 are
also taken. The shipped relay is hardness 3.0, not the plan's 5.0, *"a body nobody can mine is a body
nobody can dismantle"* (`:1092-1095`).

---

## 5. Worldgen

### 5.1 How a column's material is chosen (code)

- `WorldGenerator.GenerateTerrain` is a pure function of (seed, settings, position)
  (`Overburden.Core/Worldgen/WorldGenerator.cs:13-31`, `:373-489`).
- Per column it computes the surface height. Per voxel it tests the cave field and then sets
  `_strata.MaterialAtDepth(surface - worldY)` (`:473`, `:479`).
- `StrataTable` is a flat per-depth lookup with a bottom material forever
  (`Overburden.Core/Worldgen/StrataTable.cs:26-67`).
- The shipped strata are 1 grass, 3 dirt, then stone, **built in C#**: *"Data already — moving it into
  mod JSON is on the TODO"* (`Main.cs:8701-8707`).
- Depth is measured below the **local** surface. The generator cannot know the water table, which is
  Tier A's, analysed after generation, so *"iron under the water table"* has to be approximated by depth
  or absolute Y (my inference). Sea level is Y 0 (`WorldGenSettings.cs:80`).
- There is no clay, sand or ore band.

### 5.2 Where an ore field hooks in

- **In `GenerateTerrain`**, as WORLDGEN-NOTES recommends: a second lattice field thresholded like the
  cave field, replacing the strata material at `:473` and `:479`.
  - The cave lattice is 4 voxels (729 fBm3D samples a chunk, `docs/SCALE.md:70`) held in a stack span
    (`:415-424`).
  - A second field is another 729 samples. Caves were 80% of generation cost before the lattice
    (`WorldGenSettings.cs:203-207`), and the recorded cost is 0.47 ms a chunk Release (`SCALE.md:70`).
    An estimate of the cost with a second field: about 1.5–2× that. Unmeasured.
  - An ore band can skip chunks above it, the way the sky and cave-ceiling fast paths do (`:398-413`).
  - It needs its own seed, XOR-derived like `_caveSeed` (`:168-172`), and its own query function that
    agrees bit for bit, the way `IsCaveAt` and `GenerateTerrain` agree (`:310-315`).
- **In `ApplyFeatures`** (`:491-505`): empty. *"Ore veins and cross-chunk cave systems land here."* It
  needs the stage barrier (`WorldGenSettings.cs:3-37`) and the relight path.

### 5.3 Special coordinates

Gradient noise is exactly zero at its lattice points, and the world origin is one of them.
- The relief field offsets its origin by a seed-derived fraction, `UnitFraction(Hash64.Mix(...))`
  (`WorldGenerator.cs:183-198`, `:510`). Without it, *"four worlds on four seeds all measured an
  amplitude at spawn of 34.6 voxels"*.
- The surface noise is left un-offset on purpose: the spawn column is at sea level in every world
  (`:193-196`).
- The cave field is not offset.

An ore field without an offset would put the same ore, or the same absence of ore, at spawn in every
world.

### 5.4 What it does to the oracles and to old worlds

- **The recorded figures are all on one landscape.** The r6 control is *"2601 viewed / 325 drawn / 3446
  jobs at 411 quads"* (`WORKLOG.md:6843`; `docs/COMMANDS.md:564-575`). Others are the keystone's 18,718
  at risk and *"the first forty seconds of the profile seed are dry"* (`COMMANDS.md:1018-1029`). All
  are taken with no world file, at the fixed seed `0x4D494E455243` (`Main.cs:1325`) and default
  settings.
- **An ore field that is on by default moves them.** Quads move where ore shows in cave walls. Jobs and
  drawn may move too, because an ore voxel breaks a uniform stone chunk:
  - storage goes from 0 bytes to about 4 KiB (my estimate) (`Overburden.Core/Voxels/VoxelChunk.cs:17-23`);
  - the uniform-neighbourhood light fast path is lost (`Overburden.Core/Streaming/ChunkPipeline.cs:1273-1298`).
  
  Every M9 and M8c oracle moves if the ore's support or permeability differs from stone's. **A worldgen
  change moves every oracle; say so in the plan.**
- **A new material that worldgen does not place does not move the r6 control.** Clay and glass shipped
  with the control holding to the digit, even though clay renumbered dirt and stone in a new world
  (`WORKLOG.md:2143-2144`).
- **The precedent that keeps figures still is relief.**
  - The default equals the uniform shape, so old worlds and profile runs are bit-identical
    (`WorldGenSettings.cs:108-116`).
  - The shipped shape applies only to a newly created played world (`Main.cs:9240-9312`).
  - The setting lives in the header, which is at v7 (`Overburden.Core/Persistence/WorldHeader.cs:187`,
    `:990-994`, `:1041-1059`).
  
  An ore setting would be a header field and a version bump.
- **Old worlds are the other trap.** An existing world regenerates every unstored chunk from its header
  and the current code (`COMMANDS.md:1020-1022`).
  - Strata are not in the header, so a strata change rewrites every old world's undug ground.
  - An unconditional ore field puts ore into old worlds, with a seam against their stored chunks.
  - The ladder *wants* this. M10h's check is *"a played world opens with new strata below its dug
    region and its dug region unchanged"* (`M10-LADDER-PLAN.md:770`). That contradicts the relief
    precedent, and the pass must choose.

---

## 6. The numeric ID table

The world owns the table (non-negotiable 2).
- `GameContent.Load` seeds the saved table first (`GameContent.cs:36-44`;
  `Overburden.Core/Content/Registry.cs:70-103`).
- Content the table has never seen is **appended** (`Registry.cs:137-154`). This is tested: rubble,
  clay, glass and the machines were appended to the played v3 world (`CollapseTests.cs:1024-1056`).
- A saved ID with no loaded definition becomes a placeholder that keeps its identifier (`Registry.cs:161-174`).
  The placeholder looks and acts like stone: hardness 5, support 8, sealed, density 2000, texture 0
  (`MaterialDefinition.cs:149-165`; holes `MaterialTable.cs:206-222`).
- Air is reserved as 0 before anything loads (`ContentLoader.cs:61-75`).

**A new world numbers materials in load order.** The base mod comes first and other mods follow by ID
(`ContentLoader.cs:174-177`). Within a mod, files are sorted by **file name**, not by id
(`Overburden.Core/Content/IContentSource.cs:47-55`). As a result:
- The same material has different numbers in different worlds.
- A new file such as `copper_ore.json` renumbers every later-sorting material in new worlds only.

Things that depend on ID order:
- **The hotbar** is carried materials in ascending numeric ID. Picking up a new material, such as the
  first ore dug, shifts every slot after it (`Overburden.Core/Inventory/VolumetricInventory.cs:176-212`).
  M10g's player-assigned hotbar is plan only.
- **Save bytes** are sorted by ID (`:147-174`; `CarriedPacks.cs:37-44`).

**A materials change must not:**
- rename or delete a shipped ID, because that leaves a hole forever and its voxels turn stone-like;
- reuse an ID for something else;
- remove `Identifier.RetiredNamespaces["minercraft"]` (`Identifier.cs:36-40`);
- renumber or compact the table;
- depend on a numeric ID. Use `TryGetNumericId`: `Main.MaterialId` falls back to **air** silently
  (`Main.cs:9348-9351`; the lamp avoids it on purpose, `:1556-1560`);
- retune stone or dirt without saying which oracles move.

---

## 7. Grades

- **His words:** 2026-09-25, `docs/M10-NOTES.md:341-350`; quoted in the brief (`docs/IDEAL-MACHINES-BRIEF.md:76-79`).
- **The notes' reading** (`M10-NOTES.md:358-362`): *"a grade (low → medium → high → mythical → god) is
  the quality of a part within its tier, so a 19th-century cell can feed a quantum emitter. Capacity from
  one part, hold time from another."*
- **What exists: nothing.** No grade, quality, part or component concept appears anywhere in Core (grep).

The nearest things in code:
- **Items as materials.** `unit: count` with `placeable: false`: the computer and the pack.
- **Per-instance data on a carried item.** `CarriedPacks` stores charge and channel beside a count, as
  an annotation that never outnumbers the count, and rides header v5
  (`Overburden.Core/Inventory/CarriedPacks.cs:5-44`). It is the only per-stack data in the game. The
  inventory is otherwise `Dictionary<materialId, long voxels>` (`VolumetricInventory.cs:55`).
- **Per-row state on a placed machine.** `MachineRow.KindState` is a versioned blob that later parts may
  extend with no schema bump (`Overburden.Core/Machines/MachineRow.cs:88`;
  `M10-LADDER-PLAN.md:699-704`). Ports are player-set integers, so they are unsuitable for a grade.

Where a grade could live, and what each option costs:

| home | works today? | cost |
|---|---|---|
| **In the ID** (`vacuum_cell_mythical`, or `vacuum_cell/mythical`, since the path allows `/`) | yes, JSON only | parts × grades IDs forever; a colour each (or a shared layer); the label shows it (`Hud.cs:471-472`: path with `_` → space) |
| **Per-stack data** (a CarriedPacks-shaped annotation) | no | a header bump; stacks split by grade; every door into it is a term |
| **In the machine row** | the blob can carry it | **lost on dismantle.** A dug body credits its material to the count (`Carve.cs:765`), and a body placed from the inventory is empty (ladder M10c decision 3). The grade survives only if it is also in the ID or in per-stack data |

---

## Traps

1. **No ore, metal or coal exists.** "Steel" is a pick and a density, and "lead" is an ID fragment. A
   design that assumes iron can be mined today is wrong.
2. **Crafting is not built** (`station` is refused). Any bill of materials is plan only (M10g), and so is
   the `fabricator` that makes the quantum machines.
3. **A new material needs C# edits:** a `LayerColour` entry and a jitter entry (`VoxelMaterial.cs:47-120`).
   Strata are C#. Three tests hard-count content (`CollapseTests.cs:1044`, `ShippedContentTests.cs:209`,
   `:242`). "Just a JSON file" is not true yet.
4. **Omitted keys default silently, and one default is dangerous:**
   - `supportDistance` → 0: the material cannot span or hang, and it cuts support paths through
     surrounding stone.
   - `permeability` → 0: sealed, so the material is a dam.
   - `hardness` → 1.0: the hand pick takes it.
   - `textureLayer` → 0: it looks like stone.
   - No curve: water never weakens it.
   - **Unknown or misspelt keys are ignored without error.**
5. **Any `placeable: false` material that is not a carried pack behaves as the computer.** Right-clicking
   with it opens a machine's panel (`Overburden.Game/Player/PlaceController.cs:335`, `:524-552`). Making
   ingots and parts `placeable: false`, as ladder decision 8 does, turns every ingot into a programmer
   unless the item verb is keyed differently.
6. **A collapse turns ore into rubble** (`CollapseDirector.cs:939-943`), so a quarry that brings a roof
   down loses the ore in it. Mining yields the voxel itself. There are no drop tables.
7. **Hardness is a threshold, not a time or a price.** Per-voxel cost is the tool's flat J/voxel. A
   quarry priced per material needs a new meaning somewhere.
8. **Vertical support ignores the material** (the refund is a world constant). Any pillar holds, and
   stone versus clay matters only for spans.
9. **Saturation is Tier A only.** A Tier B flood, such as the vault's dump, does not weaken rock until it
   becomes a lake level. Permeability does not gate saturation.
10. **Density is unread.** Weight and load are rejected alternatives (`M9-PLAN.md:134`; MATERIALS-NOTES
    `:283-284`).
11. **Worldgen moves oracles and old worlds.** Only an ore setting in the header, off in the record,
    keeps the r6 control and the profile seed's figures. M10h's plan wants ore in old worlds, against
    the relief precedent. Offset any new noise field's origin, or spawn is identical in every world.
12. **Numeric IDs differ per world**, and a new world numbers by file name. The hotbar shifts when a new
    material is picked up. Never rely on a numeric ID. `Main.MaterialId` returns air on a miss.
13. **The support cost table scales with material count**, including never-placed parts: about 7 KiB now
    and about 30 KiB per wet derive at the ladder's ~45 new IDs (my estimate).
14. **Stale documents:**
    - WORLDGEN-NOTES' three "open" questions are answered in the ladder (`M10-LADDER-PLAN.md:27-32`).
    - MATERIALS-NOTES says the fields are unread and uses the `minercraft` path.
    - M9-PLAN decision 1 uses K = 240.
    - VoxelMaterial says textures load from the registry "at M1".
    - The ladder's example JSON uses keys this loader ignores or refuses, and texture layers that are
      already taken.
