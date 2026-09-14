# M10 — You fight back

> Planned 2026-09-15, before any code. Split M10a–M10d.
> **Read [M10-NOTES.md](M10-NOTES.md) first** — Matthew's proposal and its eight pieces, which this
> pass takes as decided where it can and re-opens only where the code says otherwise — then
> [MATERIALS-NOTES.md](MATERIALS-NOTES.md)'s battery ladder, and M8-PLAN's *"The open decision"*
> for why nothing came forward. Every number here is derived from the constants in the code or
> labelled an estimate; the first measurement is M10a's bench arm.

> **Overtaken the same evening, and kept as the ladder's last rung.** Matthew's brief that
> evening — *"the first tech we make will be the last tech in the game"*, a crafting system, and
> a five-tier ladder hand → steam → DC → AC → quantum — is planned in
> [M10-LADDER-PLAN.md](M10-LADDER-PLAN.md), which this plan's M10a–M10d are the first part of.
> That plan amends decisions 1, 3, 4, 5, 6, 8, 9 and 10 here (the `machine` block gains `links`
> beside `ports`; the pool is the sum of the storage rows, not a stored number; "no wires" is the
> quantum class's rule and below it wires are membership in an incremental index; the channel is
> 0–15 with a per-channel cursor; the programmer is a computer in the hand, not `E`; the header
> and the machine table are one transaction; `consumed` joins the identities) and leaves 2, 7 and
> 11 standing. Read it before building M10a — the format ships on its final shape so mods never
> see a key move.

M7 built the rain, M8 the water getting in, M9 the ceiling coming down. M10 is the first milestone
whose output is a *counter* — DESIGN's ***you build pumps and shoring to fight back → which needs
power → which needs more mining***. Everything before it was the antagonist; this is the first
thing the player builds that does something on its own after they walk away.

Three things ship: the **relay pump and outlet** — water taken here, put there, no pipe between;
the **machine programmer** — a config surface any machine declares in its JSON; and **power** — a
pool, a generator, a battery, and the gate on the entangled miner that has been promised since
2026-09-03. Shoring is sorted, not built: see *deliberately not in M10*.

---

## Three facts from the code that shape the whole milestone

**The boundary is the machine tick.** `FluidSimulation.Add`, `Remove` and `HandOff` refuse mid-tick
(`RefuseMidTick`), and under `--fluidjob=1` — `start.cmd`'s arm — the grid belongs to a worker for
up to a third of every second. Every pass that moves water today runs in `Main.UpdateFluid`'s
boundary block at 2 Hz: the settle, the hand-off, the portal and seep drains, the relay's pour, the
chunk fill, the table-move sweep, the collapse. A pump is a drain and an outlet is a pour, so a
machine that touches water can tick nowhere else. **DESIGN's *fixed 20 Hz sim step, spatially
partitioned update list* has no consumer that could use it**: a pump ticking at 20 Hz would be
refused nine times in ten, and a machine that does not touch water has nothing yet to do at any
rate. Decision 3 makes the boundary the tick and retires the 20 Hz line with a trigger.

**A machine is a material plus a row, and the material half is free.** M10-NOTES piece 6 says a
machine *cannot be a voxel* because a voxel has nowhere to put a channel number. True of the
config; not true of the body. A 1 m cube of `overburden:relay_pump` — 64 voxels on the brush
lattice, `BrushCell` — is meshed, lit, collided with, supported, mined by the hardness gate,
credited to the inventory per voxel by `Carve.Take`, shown on the hotbar with a swatch, placed by
the right button and **placed at range by `Q` + right click**, with no new code in any of those
paths. Only the row is new: position, kind, config. Decision 1 puts the machine block inside the
material JSON so there is one ID, one registry entry and no second ID table for non-negotiable 2
to worry about.

**An outlet is outside the loaded box on the first day.** `ViewDistanceVertical` is 2 — 16 m — and
the play this exists for is the bottom of a mine pumping to the surface: the played world is 64 m
deep. So *"does an output in an unloaded chunk still exist?"* (M10-NOTES piece 4) is not a later
lever, it is M10a's, and the answer is already built: the surface hand-off's rule — *water on the
analytic ground is Tier A's* — applied to an outlet whose cell is on or above the seed surface and
inside the watershed window. A `BasinLedger` credit, exactly as the notes recommended, with the
seed surface deciding whether the outlet is somewhere the ledger can answer for. Decision 5.

---

## The structural point

Every state so far has been one of four kinds: a pure function (worldgen), a fixpoint (light, the
watershed, support), a function of volume (Tier A), or a trajectory (Tier B). **A machine is the
first *authored* state** — a number the player typed, at a place the player chose, that nothing can
recompute. M6's line applies harder than it did to chunks: a chunk can be re-dug, a channel number
cannot be guessed. That is why the entity table goes first and alone (M10a), why it reconciles
against the world at every chunk arrival rather than trusting either side, and why the programmer
— the thing that creates authored state — is its own part with its own pass.

> **Teleport deletes pipes, and a pool deletes wires.** M10-NOTES made the first argument: a
> conventional pump implies a network, junctions, invalidation on every edit and fluid in transit —
> more of the thing M8 has to keep terminating — and a relay is a subtraction and an addition. The
> same argument applies to power, and it is taken here (decision 8): a power channel is a number,
> generators add to it, storage caps it, consumers draw from it. No graph, no wire material, no
> network solve, nothing new to keep terminating. **Fifth outing for *the cheap version is also the
> more accurate one*** — and here the fiction was named first, so the cheap version is the one the
> design already describes.

---

## Decisions to settle before any code

Each with a recommendation, because a recommendation is easier to argue with than a question. The
ones marked *Matthew's* need a word from him; the rest are recommended and built as recommended
unless he says otherwise.

### 1. A machine is a material with a `machine` block; the row holds only what the voxel cannot — *recommended*

`mods/overburden/materials/relay_pump.json` is an ordinary material — hardness, `supportDistance`,
`textureLayer`, `density`, all of it — with one more block:

```json
"machine": { "kind": "pump", "litresPerSecond": 100, "ports": { "channel": { "default": 0, "min": 0, "max": 255 } } }
```

`MaterialTable` projects it the way it projects `Hardness` and `LightEmission`; `MaterialDefinition`
carries a `MachineDefinition?`. The body is one `BrushCell` — 64 voxels of that material, atomic
(decision 2). The row, `MachineRow`, is the cell's origin voxel, the material's numeric ID (through
the world's table, so non-negotiable 2 holds identically), and the port values — integers, all of
them (decision 6). **`kind` is an engine behaviour picked by name — `pump`, `outlet`, later
`generator`, `battery` — with the JSON supplying its numbers**, which is exactly the Tier 1 data-mod
model DESIGN describes and the way `lightEmission` is an engine behaviour keyed by a number. A mod
can ship a faster pump or a cheaper outlet; a new *kind* is M12's scripting. That is the honest
boundary of non-negotiable 5 today and it is stated rather than hidden.

- **Rejected: a `Registry<MachineDefinition>` beside `Materials` and `Tools`.** It needs a second
  ID table in the header for rows to reference, a second seeding path at open, and a way for the
  body voxel to find its definition — three things the material already has. The one cost of the
  recommended shape is that a machine's ID *is* a material ID, so a removed mod's pump leaves a
  material hole and a body of `Unknown` material in the world, which is what non-negotiable 2 says
  should happen.
- **Rejected: the row stores the identifier string.** Rows are few, so it would cost nothing — but
  it would be the one place in the world file that resolves an ID without the table, and a second
  resolution path is how two spellings of one material become two materials.

### 2. Placement is atomic and, into water, deferred to the boundary and displacing — *recommended*

`Place.Machine(editor, cell, material)` writes all 64 voxels or none: a cell with anything but air
in it is refused whole (`PlaceResult` with 64 `Occupied` and 0 written), because a half-placed pump
is a body with no row or a row with no body, and the reconcile in decision 4 would delete it a
boundary later having debited the inventory. Whatever brush is selected, a machine material places
as one cell; the HUD says `MACHINE`.

**Into water it is deferred to the boundary and displaces rather than refuses.** M9d's fill
refusal (`Wet`) exists because a fill *deletes* the water it overwrites; the play case here is a
pump placed *in* a flooded room, and refusing it would refuse the milestone. So a machine
placement whose cell holds Tier B water queues to the boundary — the chunk fill's queue, built
2026-09-15 for the same reason — where `FluidSimulation.Lift` takes the 64 cells' water, the body
is written, and `Return` re-pours it bottom-up from the first cell above the body: M9c-1's own
routine, with `lifted == returned` as the oracle it already has. In dry air the placement is
instant, like the brush. **On the lake bed the cells are `LakeCeiling`'s and hold nothing**, so a
pump on a lake floor places at once; the lake is a number and a pier deletes nothing (M9d
decision 3's argument, which stands).

### 3. Machines tick at the fluid boundary, after the pour and before the table sweep; the 20 Hz line is retired with a trigger — *recommended*

`Machines.Tick(context)` runs in the boundary block between `RunPendingChunkFills()` and
`CheckTable()`: after every pass that moves water so a pump reads the lake as this boundary left
it, and before the table-move sweep so an outlet that raised a lake over a roof requeues that roof
in the same boundary. Its own bracket, `machines`, on the `boundary` line. Under `--nowater` there
is no boundary and no water to move, so a machine that touches water does nothing and says so
once; power (M10c) ticks in the same call.

**Rates are in litres a second in the JSON and millilitres a boundary in the code**: `L/s × 500 ms`
is an exact integer, and a boundary that comes late (a tick that outlasts its half-second is
finished, not skipped) moves the same litres later, which is DESIGN's *slower than realtime is
accuracy* — a late pump is a slower pump, never a wrong one.

**DESIGN's *fixed 20 Hz sim step* is amended to *machines tick at the fluid boundary*.** Revisit
trigger: a machine whose output the eye watches at 2 Hz — M11's drill carving a voxel every half
second reads as stuttering where a pump's litres do not. That is the moment for a faster machine
tick, and it will be a second rate beside the boundary, not a replacement for it.

### 4. The rows are the truth and the body follows; reconciled at every chunk arrival — *recommended*

`MachineSet` holds every row in the world in memory, read whole at open (they are hundreds, not
millions) and written whole at autosave and quit — `IWorldStore.WriteMachines`, the basins' shape,
a replacement rather than an upsert, at schema 4. The header does not change for this: the table
is a table. (Header v4 is M10c's, for the charge.)

**A crash can leave the body and the row disagreeing, and the rule is that the row wins.** The
chunk is written at unload, the rows at autosave; between them a crash produces a body with no row
or a row with no body. At `onVoxelsReady` — the pipeline's one arrival hook, all three paths — the
set checks the arriving chunk: every row in it has its 64 voxels rewritten to the body material if
any is not (a machine from an autosave whose chunk never landed), and any machine-material voxel
no row covers is written to air *without credit* (a body from a session that crashed before its
row was saved). The check is cheap by the palette: a chunk whose palette holds no machine material
is skipped in O(palette). The one lag this leaves is the inventory's — the header's debit against
the chunk's body — and that lag already exists for every brush placement since M6c and is accepted.

**Dismantling is a sweep over machines whose chunk changed.** `onVoxelChanged` marks the chunk;
at the boundary, and at once after a swing that took any body voxel, the set checks each marked
machine's 64 voxels: any not the body material and the machine is gone — the remaining body voxels
written to air and **credited to the inventory as voxels of the machine's material**, so a swing
that took 17 of a pump's 64 ends with the player carrying 64 and the world holding none. After a
fall the remaining voxels are rubble, not body, so nothing is credited: a machine in a roof that
comes down is destroyed, which is the antagonist doing its job. The fall never has to know a
machine exists.

### 5. A pump takes from the lake over its top or the cells around it; an outlet pours above itself; an unloaded outlet on the surface credits the ledger, an unloaded outlet underground accepts nothing — *recommended*

**Intake.** If the pump's sample's table (`IWaterTable`, M9d's read) stands above the pump's top,
the pump is under a lake and draws from Tier A — `WaterSystem.Draw(basin, litres)`, whole litres,
the portal drain's own call. Otherwise it takes Tier B water from the wet cells in the 3 × 3 × 3
brush cells around its body (1,728 cells, read off the grid at the boundary, its own body
excluded), nearest first. **The delivered rate is bounded by the automaton's throughput into that
box, not by the nominal rate** — the same physics as M8a's one-voxel hole at 29 L/s, where a wider
bore took 6–10× — so a pump in a big sheet is a sink the sheet refills diffusively. The bench
(below) takes that number before the nominal rate is believed; 100 L/s in the JSON is an estimate
between the hole's 29 and the widest bore's ~300.

**Outlet.** An outlet pours into the 1 m column directly above its top face — up to the 16 cells
there, `Add` cell by cell — and the water falls, spreads and hands off exactly as the relay's ball
does, which is the spectacle DESIGN wants (*a water surface you can watch*). Under a roof the
column is rock, accepts nothing, and the pump stalls; on a lake cell `Add` refuses (M8d-5) and the
litres go to the basin through `Receive`. **The pump moves `min(what its intake offers, what its
outlets accept)` in one boundary, and nothing is ever held by a machine** — M10-NOTES piece 3, with
the mechanism: the outlets' room is read first, the intake is drained by exactly that, so `pumped
== emitted` at every boundary and the world's total is unchanged by any number of machines.

**Unloaded outlet.** `IFluidTerrain.IsLoaded` says the outlet's chunk is not home. If the outlet's
cell is at or above the seed surface (`StreamerSeedHeights`, M8b-2c's reader) and inside the
watershed window, the litres go to `Receive(basin)` for the sample under it — the hand-off's rule
without the wait. Otherwise — underground and unloaded, or off the window — the outlet accepts
nothing and the pump stalls, printed as `stalled` on the `M10:` line. **Deterministic, exact, and
bounded by what the ledger can answer for**; the lever for an underground unloaded outlet is a
per-chunk fluid credit released at arrival, and it is not built until a play wants it.

**Several outlets on a channel share by round-robin in position order** — sorted by (x, y, z), a
cursor per pump — so two sessions with the same machines and the same water produce the same
litres in the same cells; weights are the programmer's (decision 6). `Random.Shared` is not used
anywhere, per M10-NOTES piece 2.

### 6. Ports are bounded integers declared in the JSON; the programmer is a verb, `E` on a machine, until crafting makes it an item — *recommended; the key is Matthew's*

A port is `{ default, min, max }`, all `long`, bounded at load *and* at use (M3c's rule: the loader
is not the only caller — a row from disk is a caller too). Integers because a NaN cannot happen and
a float port has no exact stored form; a rate that wants a fraction declares its unit smaller.
`channel` is the one port every machine has by default. The relay pump declares `channel`; the
outlet declares `channel` and `weight` (default 1, weighted round-robin in M10b); the battery
(M10c) declares `channel`.

The programmer is the first input UI the game has — `Hud` is display-only — and it goes in
`Overburden.Game/Ui/MachinePanel.cs`: aim at a machine, press `E`, a panel lists the machine's
ports by name with their bounds, edit, `Esc` closes. Every write goes through
`MachineSet.SetPort(row, name, value)` in Core, clamped, so a multiplayer server would apply the
same rule. **Labels are derived from the port name, never authored** — M6d-3's rule for materials,
for the same reason: mods write against a format the moment it exists.

*"Eventually we will have a quantum machine programmer"* reads as an item — something you have to
make before you can configure anything. **Recommend: the verb now, the item when M11's crafting
exists to gate it**, at which point holding the programmer is the condition on `E`. Building the
item now would build a gate with nothing behind it.

### 7. Ship the pump unpowered in M10a, and price it in materials — *recommended*

M10-NOTES piece 5, taken: the first relay pump draws no power, because power does not exist until
M10c, and a milestone whose first playable part is three sub-parts away is not split finely. **The
honest cost is the one the notes named**: adding power later is a nerf to a thing the player has
built around. Mitigation, the second of the two the notes offered: the body costs 64 voxels of the
machine's material, which the player cannot mine — so a pump costs what it costs to *make*, and
making is M11's. **Until then the material is seeded into the inventory by `--stock=` and by a
debug key beside `R`**, so the pump is placeable in play and is, honestly, a debug bind with a
body until crafting exists. Matthew's ladder — wooden, brass, iron, steel — is four JSON files
with four rates once the materials exist, and nothing here has to change for it.

### 8. Power is a pool per channel: generators add, storage caps, consumers draw; no wires — *recommended, M10c*

`PowerChannel` is a number of joules and a cap. Each boundary: every generator on the channel adds
its output for the interval; the pool is clamped to the sum of the channel's storage capacity (a
channel with no battery holds nothing across a boundary — generation is use-it-or-lose-it); every
consumer draws what it needs or does less, in position order. **The first generator is solar**:
`overburden:solar_panel` outputs `rated × skyFraction × (1 − overcast)`, where the sky fraction is
the top cell's `SkyColumn` openness and the overcast is `Weather.OvercastAt`, both of which exist
and neither of which needs a fuel material the world does not have. **The storm that floods the
mine also dims the panels that pump it** — DESIGN's loop closed with two existing inputs. Coal,
steam and the rest of MATERIALS-NOTES' power column arrive with their materials.

**A pump draws a fixed energy per litre**, declared in its JSON; with no channel charge it does
less, exactly as with no outlet room. **The first battery is `overburden:battery`, a placed
storage node with a capacity**; the pool persists in the machine table beside the rows (one number
per channel).

- **Rejected: a wire material and a connectivity solve.** It is M10-NOTES' pipe argument verbatim:
  a graph invalidated by every edit, and a network in the merge tree's category of cost. The
  fiction already says *wireless*; a channel number is the only thing it needs.
- **Rejected: day and night.** There is no time of day — sky brightness is a uniform — and a
  diurnal cycle is one more thing the panel would depend on that does not exist. When it exists it
  multiplies in, as the overcast does.

### 9. The entangled miner's gate is a carried battery: capacity and recharge by tier, energy per voxel taken, never the rate or the bite — *recommended, M10c; the numbers are Matthew's*

TODO's recorded recommendation, taken as written. The hand battery is a `MachineRow` the player
carries rather than places — a row with no position, stored in the header (**v4: charge, tier,
channel**) — with ports `channel` (which pool it recharges from) and a tier that is content: four
JSON files under `mods/overburden/batteries/` with `capacityJoules` and `rechargeWattsPerSecond`.
It recharges from its channel's pool each boundary, from anywhere — wireless is the fiction and
the rule. A bite costs `joulesPerVoxel × voxels actually taken`, which `CarveResult` already
counts; when the charge cannot cover the bite, the bite takes what the charge buys and stops,
printed on the HUD. **The rate stays 10/s and the bite stays the brush cell** — Matthew's *"10/second
eventually this will cost a lot of power"* is this decision's brief, and the rate was chosen for
what a charge will buy. Capacity, recharge and the joules per voxel are estimates until he sets
them; the ladder's top tier *"as effective as it is now"* means a capacity that outlasts a play
session at 10/s, and that is one number.

### 10. The oracles: `pumped == emitted` per boundary on every tier, and the world's water total is a constant of the machine layer — *recommended*

Tier B's identity gains two terms — `held == injected + drawn + seeped + loaded + emitted − removed
− displaced − handed − evicted − pumped` — and Tier A's gains the same two. Across tiers, at every
boundary, `pumped(A) + pumped(B) == emitted(A) + emitted(B)`, with no in-flight term to hide a
litre in. **The test that matters is M8's lifecycle battery with machines running** — pour, pump,
walk away, walk back, quit, reopen — asserting both identities and the cross-tier equality, because
the point of the design is that adding machines cannot change the global total. A machine test that
asserts a pump moved 50 L proves nothing about that.

Power's identity is the same shape: `pool == generated − drawn − clamped`, with `clamped` reported
rather than tolerated, because a pool that silently loses joules to a cap is the one defect a play
cannot see.

### 11. The split — *recommended*

| | What | Verifiable |
|---|---|---|
| **M10a** | **The entity, the pump and the outlet.** Decisions 1–5 and 7: the `machine` block, `MachineSet`, atomic and deferred placement, the reconcile, the dismantle, the boundary tick, the round-robin, the unloaded-outlet rule, schema 4, `--pump`, the `M10:` line, the bench arm. Channel 0 only, no programmer, no power. | Headless and on the bench: the identities, the world total, the delivered rate |
| **M10b** | **The programmer.** Decision 6: ports as a format, `MachinePanel`, `SetPort`, channels and weights, weighted round-robin. The first input UI. | The panel by screenshot; the ports headless through `--port=` |
| **M10c** | **Power.** Decisions 8 and 9: the pool, the solar panel, the placed battery, the carried battery, header v4, the entangled miner's gate. | Headless: the power identity; the gate's oracle is a bite that stops |
| **M10d** | **The play and the measurement.** Pump the played mine dry; the storm cutting the pumps; the gate felt at the bite. | Matthew's |

Each part wants its own planning pass against the built code before it, as M9's did. **M10a goes
first and alone, and the store slice inside it — schema 4 and the reconcile — is built and battery-
tested before the pump touches a litre**, because a defect there is a machine that vanishes with
its config, which is permanent data loss of authored state.

---

## The measurement to take first, on the bench, before any of it is trusted

`--pump` on `Overburden.Bench`: the chamber fixture (`--fluidshape chamber`) flooded to rest, one
pump on its floor and one outlet outside it, printing per boundary the litres offered by the intake
box, accepted by the outlet, moved, and the delivered L/s against the nominal — at intake boxes of
1 × 1 × 1, 3 × 3 × 3 and 5 × 5 × 5 m — then ticks to empty and the active-set peak with the pump
running. **The number that decides the JSON's rate is the delivered one**: if a 3 × 3 × 3 intake
delivers 60 L/s from a sheet at rest, 100 L/s nominal is a lie and the honest rate is written. The
same run with the outlet inside the chamber (a loop) is the identity's stress: the world's total
must not move by a millilitre over 10,000 boundaries.

**Then the reconcile, headless**: place a pump, quit before the autosave (`--autosave=100`), reopen
— the body is gone without credit; and the other way, `--autosave=1` with a chunk that never
unloads — the row rebuilds its body at arrival. Both read off the `M10:` line's `reconciled` term.

## Provoking it in the game

`--pump=<litres/s>`: at the measured window's open, a pump at the pit of the basin `--breach` bit
into and an outlet on the surface 24 m off, on channel 0 — so `--fillbasin=2000000 --breach=3
--pump=100` floods the shaft and pumps it back out in one run, and `--measure=60` reads the loop
settling. In play a key beside `R` seeds the inventory with a pump and an outlet (decision 7). The
periodic `M10:` line prints machines by kind, pumped / emitted L this window and in all, stalled
boundaries and why (no outlet, no room, unloaded), the cross-tier equality, reconciled bodies and
rows, and the tick's mean and max ms; the `boundary` line gains `machines`.

## Deliberately not in M10

- **Shoring as a material.** DESIGN's *raise local support distance* is already true of a placed
  pillar under M9's rule: `SupportRule.Rest` refunds `K/30` per voxel of rest, so a stone column
  from floor to roof carries a full budget to its top and the roof paths spread from there. A
  shoring *material* with a higher `supportDistance` is one JSON file whose only cost is that the
  player cannot mine it — which is a recipe, which is M11. **The play check is whether a pillar
  the player builds reads as holding the roof**; if it does not, the material is one file.
- **Drills, conveyors, recipes, crafting.** M11, per DESIGN. The programmer as an item goes with them.
- **The inventory's unit moving to millilitres.** MATERIALS-NOTES says it rides header v4 with the
  machines. **It does not**: a change in what a field means is a refusal under the format policy,
  it serves recipes and nothing in M10, and a version bump with permanent-data-loss consequences
  goes alone. v5, at M11, on its own.
- **Random distribution.** Round-robin and weights cover *"randomly or programmatically"*; a seeded
  random selector is one more mode with nothing to recommend it over weights.
- **A machine count cap.** Printed, not enforced. Trigger: `machines` on the `boundary` line over a
  millisecond on the played world.
- **Wading, oxygen, the pump ladder's tiers.** Survival-mode mechanics on the survival-mode trigger
  (M10-NOTES), and the ladder's rungs are JSON once the materials exist.

## What this pass changes in the other documents, once the decisions are taken

- DESIGN's *Automation*: *fixed 20 Hz sim step* → *machines tick at the fluid boundary*, with the
  drill trigger; *power — a simple generation / consumption / storage network* → *a pool per
  channel, no wires*; the M10 row of the roadmap gains *shoring is the pillar M9 already supports*.
- M10-NOTES piece 6's *header v3 → v4* → *schema 3 → 4 for the table; header v4 is the charge's*.
- MATERIALS-NOTES' *rides header v4 with the machines* → *v5, at M11, alone*.
- TODO's M10 section becomes this split as a checklist.
