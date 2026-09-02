# M10 notes — the quantum machine family

> Notes toward M10, started 2026-09-02 from Matthew's proposal. **Not a milestone plan** — this is
> the M2-REFERENCE-NOTES shape: the reasoning captured while it is fresh, so the eventual planning
> pass starts from it rather than re-deriving it. Precedent for the format: *deferred collapse*,
> raised a milestone before M9 and now the starting point of M9's plan rather than a fallback in it.

## The proposal, in Matthew's words

> *"A quantum fluid pump, and a quantum output which 'teleports' water 'wirelessly' from quantum
> fluid pump to quantum pump output — just the one, or if you use more than one, randomly or
> programmatically select which output to dump into. Eventually we will have a 'quantum machine
> programmer' which is essentially the config interface for the unit, letting you specify input
> numbers to output numbers for that respective machine it is used on. Lots of things depending —
> like if it's proven that the structural additions cannot be met due to CPU load etc, it is still
> going to work out great, plus I can invent cheap ways to approximate the same outcomes."*

Three parts: a **pump** that removes fluid, an **output** that emits it somewhere else, and a
**programmer** that configures the mapping. The programmer is deliberately general — it is a
config interface for machines, not a fluid feature.

---

## Why this is structurally excellent, and it is not the reason it looks like

The obvious reading is that teleporting is a fictional convenience. **The real point is that it
deletes the single most expensive subsystem a machine layer would otherwise need.**

A conventional pump implies **pipes**: a network of connected segments, a graph to solve, flow
rates, junction splitting, a network that changes every time the player places a block, and — worst
here — **fluid in transit**, which is more of exactly the thing M8 already has to keep terminating.
Pipes are the part of a fluid machine layer that is genuinely hard, and every voxel game that has
them has paid for them.

**Teleport is a subtraction and an addition.** Take *n* litres from the source body, add *n* litres
to the destination. There is no transport, no graph, no in-flight state, no network invalidation on
every edit, and **nothing new to keep terminating**.

That is the same move this project has already made three times, and it has been right every time:

- **Tier A water is solved, not simulated** — one number per basin against one height per sample.
- **Skylight is seeded analytically**, not flooded from a top of the world that does not exist.
- **Rainfall is a pure function of (seed, time)**, so an hour integrates in one call.

So the recurring finding applies again: **the cheap version is also the more accurate one.** A pipe
network approximates transport badly and expensively; a teleport does not approximate transport at
all, it declines to have any — which is a *coherent* rule rather than a lossy one. **It also fits
the fiction better than pipes would**, since the thing that needs explaining is the teleport, and
the game has already named it.

**And Matthew's own framing is the strongest argument for building this layer early rather than
last:** *"if it's proven that the structural additions cannot be met due to CPU load, it is still
going to work out great."* This layer's cost is bounded by **how many machines the player places**,
which is a number the player controls and the engine can cap. Nothing else in the project has that
property. M8's active set is bounded by terrain; M9's at-risk set is bounded by the mine's shape;
**a machine layer is bounded by a placement count.** It is the one subsystem whose worst case is
knowable in advance.

---

## The missing pieces

Each with a recommendation, because a recommendation is easier to argue with than a question.

### 1. Pairing — how does an output know which pump feeds it?

**Recommend: a channel number, set by the programmer, defaulting to 0.** A pump sends on a channel;
every output on the same channel in the same world is a candidate destination.

- It is *already* the "input numbers to output numbers" idea, so the programmer has a job on day one
  rather than being a later addition to a hard-wired system.
- It needs no pairing UI, no line-of-sight, no linking tool, no stored pair list — **just an integer
  on each machine**, which is one field in whatever a machine's config already is.
- **The alternative rejected: storing an explicit link from pump to output.** It creates a reference
  that must survive one end being mined, a world reload, and a chunk unload — which is three
  failure modes a plain number does not have.

### 2. Distribution across several outputs

Matthew's words: *"randomly or programmatically."*

**Recommend: round-robin as the default, weights via the programmer, and random only if it is
seeded.** The reasoning is not taste:

- **Random is a multiplayer and determinism hazard**, and this project has already recorded the rule
  once — M9's deferred collapse says the roll *must be server-authoritative and seeded*, per
  non-negotiable 1, because a per-client roll means two players see different worlds. A fluid split
  is the same shape. **Seeded random is fine; `Random.Shared` is not**, and the difference is
  invisible until the day it is a protocol change.
- **Round-robin is deterministic, needs one integer of state, and is what a player actually wants**
  from a splitter — "share it evenly" rather than "share it evenly on average, eventually".
- **Weights are the programmer's whole purpose**, and round-robin is just the all-equal case, so
  there is one mechanism rather than two.

### 3. Backpressure — what happens when the destination cannot take it

**This is the piece most likely to be got wrong, and it is not a detail: M8's oracle is that volume
is conserved exactly, with no tolerance.** A machine that quietly drops litres breaks the property
the entire fluid milestone is verified against — and it would break it *silently*, since a missing
litre looks exactly like water that has not arrived yet.

**Recommend: the pump moves `min(what it can take, what the destination can accept)` and nothing
else happens.** No buffering, no queue, no in-flight state.

- The pump simply does less work this tick, which is the same shape as `Place` consulting the
  inventory last and `Carve` crediting after the write.
- **Rejected: a buffer inside the machine.** That is in-flight fluid by another name, it needs
  persisting, and it reintroduces exactly what teleport was chosen to avoid.
- **Rejected: spill at the output.** Water appearing on the floor because a tank was full is a
  second, invisible source of Tier B activity that nobody asked for.
- **The test this needs is the M8 volume oracle with machines running**, not a machine test — the
  point is that adding machines cannot change the global total.

### 4. Range, and the thing that makes "wireless" non-trivial

**"However far away" runs straight into the analysis window.** Tier A only holds the basins inside
the current window; everything else lives in `BasinLedger`, banked by pit coordinate. An output
5 km away is not in memory, and neither is the basin it would pour into.

**Recommend: a teleport into an unloaded region credits the ledger, exactly as a departing window
does.** The machinery exists, it is already persisted, and it is already the answer to "water in a
place nobody is looking at". **This is the second time `BasinLedger`'s design has paid off somewhere
it was not written for** — the first was surviving a window move.

**The genuinely open sub-question: does an output in an unloaded chunk still exist?** A machine is
placed state, and placed state currently lives in voxels, which unload. See piece 6.

**Precedent worth knowing about: the entangled miner.** M5d built a debug tool whose defining
property is unlimited reach, and the recorded finding was that *"however far away" means "as far as
the world exists"* — what stops it is the edge of the loaded region, not the mechanic. **The
quantum machines are the same claim made into content**, and that entry says the moment to settle
reach-as-content is when it stops being a debug bind.

### 5. Power, and whether the first version has any

DESIGN's loop is that pumps need power, which needs mining. **But the first quantum pump does not
have to be the finished one.**

**Recommend: ship it unpowered first, and add power when power exists.** This is exactly option 2
of M8's open decision — *bring a hand-placed unpowered pump forward into M8d* — and this proposal is
a better version of it, because it is a real machine rather than a stopgap that gets deleted.

**The honest cost of shipping it unpowered: it is strictly better than the powered version, so
adding power later is a nerf**, and a nerf to a thing players have built around is a worse moment
than an initial limit. **Two cheap mitigations, either of which is enough:** make the unpowered one
slow enough that the powered one is an upgrade rather than a replacement, or make it expensive in
materials — the volumetric inventory already prices things in litres of rock.

### 6. The structural change nobody has named yet: a machine is the first *entity*

**This is the biggest thing in the proposal and it is worth separating from the fluid idea.**

Everything the world currently contains is one of two things: **voxels** (a material ID per 0.25 m
cell, palette-compressed, regenerable from the seed) or **aggregate numbers** (litres per basin, a
sky height per column). A machine is neither. It has an **identity**, a **position**, and
**configuration that the player authored** — a channel number, weights, a rate.

Consequences, all of which want deciding at M10's planning pass rather than during it:

- **It cannot be a voxel.** A voxel is a material ID; there is nowhere to put a channel number. So
  machines need their own storage — a table in the world database beside `chunks` and `basins`,
  keyed by position. **That is a schema addition, so header v3 → v4, additive, and it migrates
  under M6c-2's policy.**
- **Player-authored config is the first state that is not derivable and not aggregate.** A lost
  basin refills in minutes; a lost machine configuration is work the player did. **M6's "the tests
  are the only thing between a defect and permanent data loss" applies here more sharply than it
  did to chunks**, because a chunk can be re-dug and a config cannot be guessed.
- **Machine ticking is a third active set**, alongside Tier B's cells and M9's at-risk regions —
  and, per the note above, the only one bounded by a number the player chooses.
- **`VolumetricInventory` already knows how to carry things and `Place` already knows how to put
  them down.** A machine is a placement whose `BrushCell` carries a payload, which is a smaller
  change than it sounds.

### 7. The programmer is a *format*, not a screen — and that is non-negotiable 5

*"Essentially the config interface for the unit… lots of things depending."* The generality is the
valuable part, and it lands directly on the base-game-is-a-mod rule.

**Recommend: a machine declares its configurable ports in its own JSON, and the programmer renders
whatever it finds.** `hardness`, `lightEmission` and (at M8c) `permeability` all took this route.

- **A third-party machine then gets a config UI for free**, with no privileged path — which is
  precisely the test non-negotiable 5 states: *if it can't be expressed the way a mod would express
  it, the format is wrong.*
- **The trap M6d-3 already recorded applies**: a machine's *label* should be derived from its
  identifier rather than authored, because mods write against a format the moment it exists and an
  authorable name is a name that wants translating.
- **And the trap M3c recorded applies to every number in it**: a mod-authored rate is a
  denial-of-service vector and a NaN fails *open*. Bound at load **and** at use, because the loader
  is not the only caller.

### 8. Naming — settled 2026-09-02: industrial, not physics

`minercraft:quantum_fluid_pump` is a content ID, and **non-negotiable 2 says IDs are never
renumbered and a removed mod leaves a hole that stays** — so this was raised as the last free
moment, and it was taken. **Matthew chose a mining-operation tone over a physics one.**

**Recommended set, and it is one word from Matthew to change until the first machine ships:**

| | ID | why |
|---|---|---|
| Pump | `minercraft:relay_pump` | **A relay already means "sends onward with no physical path"**, which is the whole mechanic, and it is infrastructure vocabulary rather than physics vocabulary. |
| Output | `minercraft:relay_outlet` | Pairs by reading rather than by convention. |
| Programmer | `minercraft:machine_programmer` | **Deliberately not part of the family.** It configures *any* machine, so tying its name to the fluid pair would misdescribe the one component whose generality is the point. |

**The strongest argument for `relay` is the channel number.** Radio relays have always had
channels, so decision 1's pairing rule stops needing an explanation: a player who has seen the word
knows what the number does before reading anything.

**Alternates considered and why they lost:** `induction_pump` is the best real-engineering word for
wireless transfer but implies *short* range, which is the opposite of the mechanic;
`transfer_pump` is accurate and says nothing; `flux_pump` drifts back toward the physics register
that was just rejected. **`entangled_pump` was the near miss** — it would have echoed the
entangled miner, giving the game one coherent word for *acts at a distance* — but that is a
physics word, and the entangled miner is a **debug bind** whose promotion to content is explicitly
still an open decision. Naming shipped content after an unshipped debug tool binds the two before
either decision is made.

---

## What could come forward, and what should not

| | Where | Why |
|---|---|---|
| **One pump, one output, fixed rate, no power, channel 0** | **M8d, as the answer to M8's open decision** | Small: a `Place` verb, a subtraction and an addition. Makes M8's play ask *"is fighting the water fun?"* rather than *"is losing to it tolerable?"* |
| Several outputs, round-robin | M10 | Wants the programmer to be worth using |
| The programmer, and machine config as a format | M10 | This is the general feature and deserves its own planning pass |
| Power | M10 | Needs power to exist |
| Machine storage table, header v4 | **Whenever the first machine ships** | Cannot be retrofitted cheaply; if a pump lands in M8d, this lands with it |

**The one that is not free is the last row.** A machine in M8d means the entity-storage question
above has to be answered in M8d, and that is the piece with permanent-data-loss consequences. **So
the honest version of "bring a pump forward" is: bring the pump *and* its persistence forward, or
do not bring it forward at all.** A machine that does not survive a quit is worse than no machine.

---

## What this changes about M8's open decision

M8-PLAN records that **M8 makes the mine floodable and nothing fights back until M10**, with three
options and a recommendation of *(3) tune the seepage rate low, then (2) a pump if the play wants
one*.

**It was argued here that this made option 2 more attractive**, since the pump would no longer be
a throwaway stopgap but the first member of a family that was going to be built anyway.

**Settled the other way, 2026-09-02: *"keep M8 clean, tune seepage low."*** Option 3, and the whole
machine family lands together at M10. **The deciding cost is the one in the table above**: a pump in
M8d drags an entity table and header v4 into the milestone whose oracle is exact volume
conservation, and **two structural firsts in one milestone is how a defect hides behind a
feature**. It also keeps M8d's play honest — *"is losing to the water tolerable?"* is the real
question for a milestone with no counter in it.

**So nothing in this document comes forward.** The table above is kept as the record of what was
considered, and its M8d row is now the thing that was *not* done, and why.
