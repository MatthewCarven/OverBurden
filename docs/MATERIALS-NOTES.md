# Materials notes — elements, and whether to "just implement chemistry"

> Notes from a brainstorm with Matthew, 2026-09-03. **Not a milestone plan** — the M10-NOTES shape:
> the reasoning captured while it is fresh, so whichever milestone first touches it (M8c, M9, M11)
> starts from here rather than re-deriving it. Every number in it is a guess and is labelled one.

## The proposal, in Matthew's words

> *"Brainstorm with me, what elements could we have, I mean we could implement a chemistry set and
> just implement chemistry? IDK sounds too complex but it is something we could at least think
> about though?"*

Two questions: **what materials should exist**, and **could the world do chemistry**. The second
decides the first, so it goes first.

---

## The recommendation: geology that reacts to water, and recipes as the chemistry set

**Do not implement chemistry. Implement materials that respond to water, and let machine recipes be
the chemistry set.** The argument is the engine's own rule — **a voxel is one material** — which
forbids mixtures at voxel scale. That reads as a limitation and is the thing that makes the idea
tractable: it bounds "chemistry" to three shapes, each already on the plan or one property from it.

| Shape | Where it lives | What it is |
|---|---|---|
| **Recipes** — litres in → litres out, with power and time | M11 crafting, on M10's machine framework | The chemistry *set*. A refinery is a machine with a recipe table in JSON: smelting, cement, acid, batteries. **No new engine concept.** |
| **Materials respond to water** — per-material permeability, solubility, support when wet | M8c (Tier C), M9 | The antagonist getting smarter. Limestone dissolves, clay dams, sand flows when saturated. **Mostly planned already — see "what the format already carries".** |
| **The fluid carries scalars** — salinity, acidity, temperature — and materials respond to them | Later, if ever | The actual chemistry. The same integrate-forward machinery as Tier C, a byte or two per cell and per basin. Iron shoring corrodes in acid; brine is where lithium comes from. |

**The volumetric inventory gives stoichiometry for free.** 1,000 L of limestone → 560 L of quicklime
is a ratio, not a recipe of discrete items, because the inventory counts litres rather than blocks.
DESIGN chose that so that *"crafting costs are continuous"*; nobody planned for it to make a
chemistry set cheap, and it does.

### The complexity cliff, named

It is **arbitrary pairwise reaction** — an N² table, mixtures inside a voxel, "what happens if I pour
X on Y". Everything above stays on the safe side of it: a *solid* transforms only under a *field*
(wetness, acidity, heat); a *fluid* only carries scalars; reactions between *inventories* happen
inside a machine with a recipe. Three bounded lists instead of one unbounded one. **The moment a
design needs two solids to react with each other in the world, it has stepped off the cliff** — and
the fix is to route it through a machine.

---

## What the format already carries, and the trap that comes with it

`mods/minercraft/materials/stone.json` today:

```json
{ "id": "stone", "hardness": 3.0, "supportDistance": 30, "permeability": 0.02,
  "density": 2600, "textureLayer": 0 }
```

**`supportDistance`, `permeability` and `density` have been in the format since M1 and nothing
reads them** — exactly as `hardness` sat unread until M3c projected it into `MaterialTable`. So the
first "chemistry" costs no format change at all; it costs M8c reading `permeability` and M9 reading
`supportDistance`, **per material, from the JSON, rather than from a constant.** That is the trap to
name: a milestone in a hurry writes `const float Permeability = 0.02f`, ships green, and the field
stays decorative — and a global constant is a format too, because mods write against whatever the
engine actually honours. The open question for each is *scalar or curve*: a single permeability is
probably right; support against saturation is a curve (M9's keystone interaction), and DESIGN says
so.

**Strata are data but not yet content.** `StrataTable` is a data-driven table constructed in code;
loading it from mod JSON is an open TODO from M2b. Every "deeper strata carry new materials" idea
below assumes that TODO is done, and it is loader work rather than engine work.

---

## Elements that would earn their place

The test for a material: **it does something to water, gravity, power, or the ladder.** A material
that only looks different is a texture layer, not a material.

### Rock — what holds the roof up
| Material | Why it exists |
|---|---|
| **stone / granite** | The strong one: long support distance, slow to mine. Today's `stone`. |
| **limestone** | *The one water eats.* Dissolves slowly, so karst caves grow while you play. Source of lime → cement → **concrete shoring that cures over time.** |
| **sandstone** | Permeable: water walks *through* it. The seepage source Tier C needs. |
| **shale** | Weak and layered: the first thing to come down. |
| **bedrock** | The anchor for M9's support BFS. |

### Loose — what moves
| Material | Why it exists |
|---|---|
| **sand** | **Flows when saturated** — quicksand. Also glass. Cheap fill that holds no roof. |
| **clay** | **Impermeable**: the natural dam, and what a player builds a dam from. Swells when wet and loads the shoring. |
| **gravel, dirt** | As now; dirt's short support distance is already the reason it collapses first. |

### Water's salts — what water carries
| Material | Why it exists |
|---|---|
| **halite (rock salt)** | Dissolves fast → brine. Salt at depth is a hazard (real salt domes flow). |
| **gypsum / sulphur** | Plaster; sulphuric acid → **the first battery's electrolyte.** |
| **lithium brine** | A deep aquifer. **It has to be pumped**: the thing you fight becomes the thing you mine. |

### Ores — the ladder
| Material | Why it exists |
|---|---|
| **coal** | First power: a steam boiler. |
| **iron** | Picks, shoring, Edison cells. |
| **copper** | Wire — M10's power *network* is made of it. |
| **lead** | First battery. |
| **nickel** | Mid battery. |
| **bauxite → aluminium** | **Made of electricity.** The electric-age material, gated by power rather than by depth. |
| **uranium** | Deep, late power. |
| **gold** | Nothing but sparkle. Allowed one. |

Twenty-two, against five today. Each is a JSON file and a texture layer (non-negotiable 5); the
behaviours are the cost, and they are counted below.

---

## The battery ladder is the chemistry, and it closes the loop

Matthew, 2026-09-03: the entangled miner *"will eventually have an energy cost and must be recharged
and only once you have the latest tier battery will it be as effective as it is now."* The tiers:

1. **Lead–acid** — lead + sulphuric acid (sulphur + water). Shallow materials, cheap, low capacity.
2. **Nickel–iron (Edison cell)** — mid.
3. **Lithium** — brine pumped from depth. **The top tier requires mastering water.**

And it fits *"the game never ends"* structurally: **depth is the ladder.** Deeper strata carry the
next materials, and deeper is wetter and heavier — more head of water, more span under load — so
the antagonist scales *with* the ladder. Better materials → better tools → survive deeper → better
materials, with no finish line. That is progression by cost, which is the only kind DESIGN now
allows.

---

## What it costs — behaviours, not materials

| Behaviour | Status | Oracle |
|---|---|---|
| Permeability, per material | **Planned** — M8c Tier C; the field exists unread | Tier C's own |
| Support distance and its saturation curve | **Planned** — M9; the field exists unread | M9's own |
| **Solubility** | New. Same integrate-forward shape as Tier C | Dissolved mass in milligrams, conserved exactly; a dissolving voxel terminates as fluid or air |
| **Granular flow** — sand as a fluid when saturated | New, and **the expensive one**: a second CA rule with an angle of repose | Conservation plus termination, like M8a's — but a new rule set, not a parameter |
| Fluid scalars — salinity, acidity, heat | New, later | Conservation per scalar, exact integers again |

**Honest count: two new behaviours beyond the plan, one of them hard.** The rest is content. The
hard one is granular flow, and it is deferred by recommendation, not by cost alone: sand that moves
is a hazard only once M9 exists to be undermined by it.

---

## Two decisions that change it, with recommendations

1. **M8c and M9 read their numbers from the material JSON, per material — scalar for permeability,
   curve for support-against-saturation.** Recommended, and the only urgent item here: the fields
   exist, the trap is a constant shipping green. Costs nothing extra now; a retrofit costs a format
   version and every mod written against the constant.
2. **Solubility goes in its own pass after M9, not into M8c beside permeability.** Recommended,
   because limestone dissolving *under a roof the support BFS already models* is where it becomes a
   hazard rather than a curiosity, and because M8's oracle is exact volume — a second conserved
   quantity in the same milestone is two structural firsts at once, which is the rule M8's own
   planning pass wrote down.

## The periodic table — data underneath, never blocks

Matthew, later the same day: *"what if we used the periodic table for element types? Too many? A
lot would be useless and writing shaders for some of them … excluding any gasses until we work out
how to do volumetric 'gas', the reverse of rain, should be easier after we have stabilised water."*

**Recommendation: ship the whole table as data, and never let an element be a voxel.** Four
reasons, each of which also answers "too many?":

- **Elements are the wrong axis for voxels and the right axis underneath them.** Nothing you dig is
  an element except native gold, copper, sulphur and carbon. Granite is quartz, feldspar and mica;
  limestone is CaCO₃; sand is SiO₂; bauxite is aluminium hydroxides. A world of pure sodium and
  chlorine blocks is a chemistry set, not a mine. So **a material declares a composition** — element
  mass-parts — and the table is what *recipes conserve*, not what the world is made of.
- **All 118 cost nothing, because an element has no texture and no shader.** `elements.json`:
  symbol, name, number, molar mass, density, state at STP. An afternoon, and it never changes. The
  "useless" ones are not a cost as data; they would only be a cost as materials, and the material
  test above (*does it do something to water, gravity, power or the ladder*) is what keeps them out.
  `state at STP` also excludes gases from ever being a material for free.
- **Shaders are per appearance class, not per material, and there are about five classes.**
  Non-negotiable 4 puts every material through one voxel shader and a texture array; a new material
  is one layer. The only things that cost shader work are *kinds* of look — metallic sheen,
  translucency (glass, ice, crystal — which shares M8's transparent pass), emission (uranium glow,
  the lamp already), wet sheen (M7, done). Five classes for 118 elements or for 22 materials alike.
- **What the table actually buys is a conservation law for mods.** A recipe's inputs and outputs
  can be checked at load for element balance — a mod that smelts iron out of nothing is refused, the
  way `ShippedContentTests` already refuses a tool ladder with no top. **Stated limit: that check has
  a tolerance, because atomic masses are not integers.** Exactness stays where it is — volume in
  millilitres, mass in milligrams — and element balance is a content check like the hardness one,
  not the engine's oracle.

### The general extraction rule — the cheap version of "just implement chemistry"

With compositions declared, most refining recipes need no authoring at all: **put a material in,
name an element, take out its mass fraction times the machine's efficiency; the rest is slag.**
Hematite (Fe 70% by mass) into a furnace with iron named yields iron, exactly, from the same numbers
every mod's furnace would use. Alloys and compounds — bronze, cement, sulphuric acid, the three
batteries — are the recipes that still want writing, and they are few.

### The cost it carries, named: the inventory leaves whole voxels

Mass balance means a recipe's outputs are not whole voxels. That is **M6c-2's recorded revisit
trigger** — *"the first quantity that is not a whole number of voxels, most likely M8's fluids"* —
arriving from crafting instead. The clean unit is the one water already uses: **millilitres.** A
voxel is 15,625 mL exactly (M8a's cell capacity, the same division), so today's counts convert
without rounding; density in kg/m³ is mg/mL, so mass in milligrams is an integer product. One
header field changes meaning, which is the kind of change the format policy says is a refusal
rather than a migration — **so it rides header v4 with the machines rather than shipping alone.**

### Gases — agreed, after water, and the seam to leave open now

Matthew's instinct is right and the reason is better than "later": **a gas is the water automaton
with gravity's sign flipped**, so it costs almost nothing *after* M8 and would cost a second
automaton before it. Two kinds, and the mine's real hazard is the boring one:

- **Heavy gas** — CO₂, "blackdamp" — is water's rule verbatim: it pools at the bottom of a working,
  spreads sideways, and drowns you without a splash. Tier B unchanged, with a different fluid in it.
- **Light gas** — methane — is the rule inverted: push *up* where there is capacity, spread sideways
  under a roof, disperse at open sky. And "the reverse of rain" is literally that on screen: rising
  particles killed where the sky is open, which is `SkyHeightField` read the other way up.

**The one thing to decide before M8b closes: Tier B is single-fluid.** A cell holds millilitres of
*water*; a gas needs the cell to say millilitres of *what*. Not a build — a seam: one byte of fluid
kind per cell, or a layout in which a second grid is trivial. Named in TODO under M8 so the choice
is made knowingly rather than inherited.

## Deliberately not here

- Real reaction chemistry — stoichiometry between arbitrary inputs, mixtures in a voxel. Off the
  cliff; route through machines.
- Gases as *materials*. As fluids they are the section above: water's rule with the sign flipped,
  after M8, with the fluid-kind seam left open now.
- Temperature as a field. Geothermal power is a good late-game reason; nothing before M10 needs it.
- Weight. Density is in the format for M9's loads and for nothing to do with carrying — no
  encumbrance, ruled twice.
