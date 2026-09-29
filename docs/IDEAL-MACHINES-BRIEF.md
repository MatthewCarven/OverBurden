# The ideal machines — the brief for the planning pass (2026-09-29)

The chat the collapse sensor's design once waited for (COLLAPSE-PLAN decision 13). It was held
at the end of 2026-09-29's long session, and the pass itself is for a **fresh chat**. This file is
what that chat starts from: his words, his answers, the recommendations he agreed to, the scope,
and how to run the pass. **Nothing is decided beyond his three answers below.** Everything else
is a recommendation for the panel to test.

## His words

The opening, verbatim:

> *"lets have that ideal machine tier chat now, what would you recommend, I have not fully thought
> this part through yet like i want there to be a few "extras" that the previous tier may or may not
> inherit what do you recommend plan with me please Claude?"*

His answers to the three questions, verbatim:

> *"I like all your suggestions 1 through to inclusive of 6, 1. i will submit some data below, 2. json
> ideally but up to you here, 3. creative mode demo."*

Then, on automation and containers:

> *"Like at the dc age is was going to start introducing elements that mine on there own so to speak
> that might go onto conveyors like :-{D How automated do we make mining, like once you have build
> the quantum miner like anything from the tier of automatic machines will appear useless especially
> when you upgrade for chunk support like so I may just drop this idea based upon suggestion, but i'd
> like the ability at lower levels to construct storage containers for your stuff like up to the high
> level where it consumes power and is bigger on the inside than the outside just don't let it run out
> of power like, else you might have to swim or mine to get it back possible :-{D"*

**He said he would submit some data. It had not arrived when the session closed.** Ask for it
first, and fold it in before the panel runs.

His earlier words on the container (2026-09-25) are in [M10-NOTES.md](M10-NOTES.md), "The quantum
storage container". That is where the **grades** come from: *"low grade, medium grade, and high
grade, mythical grade, and god grade, and your battery tech maybe 19th century but your actual
emitter/entangler could be a higher tech variation"*.

## What is taken

1. **All six extras** (listed below) are in.
2. **Inheritance is declared in JSON** ("ideally, but up to you"). The base game declares it the way
   a mod would (non-negotiable 5).
3. **The target is the creative-mode demo.** Design the ideal machines first, then work down the ladder.

## The recommendations he agreed to, for the panel to test

**The extras rule.** An extra is a capability defined once in the engine. A machine opts in through
its JSON, and an inherited extra reaches only as far as that tier's membership reaches: quantum a
channel, AC a cable component, DC 24 cells of copper, steam a shaft, hand nothing. It is the ladder's
one idea ("tiers differ only in how a machine is assigned its pool", [M10-LADDER-PLAN.md](M10-LADDER-PLAN.md))
applied to capabilities, and it is cheap, since membership is already computed.

**The six extras,** with the inheritance first proposed (the panel should test each):

| # | Extra | At quantum | Inherited down? |
|---|---|---|---|
| 1 | **Sense and respond**: the collapse sensor and its signal; later water level, power low | switches anything on its channel | AC on its cable, DC within 24 cells, steam a bell down the shaft (no switching), hand none |
| 2 | **Recall**: a machine caught in a collapse returns to the inventory instead of becoming rubble that credits nothing | yes | **deliberately not**: losing a pump to the roof is the fight |
| 3 | **Remote panel**: the computer reads and sets any machine on its channel from anywhere | yes | AC for its own cable component, perhaps |
| 4 | **Remote charge**: the carried cell recharges from anywhere | exists already | DC and AC dock only (exists) |
| 5 | **Priority**: when power is short, which pumps get it first | yes | AC perhaps |
| 6 | **Map telemetry**: F12 shows machines and their state | yes | open |

**Storage containers, a ladder.** Plain containers at hand → AC (a crate, bins, a vault) with no
power and no spill. At quantum, bigger on the inside: it holds far more than its size and pays power
every boundary for the hold. If that power fails it **dumps**: litres come out as Tier B water
("swim"), voxels as a heap around it ("mine"), and both are terms in the identities from the
first line (*every door into an identity is a term*). **Grades** are a second axis on its parts: an
older cell gives a shorter hold. The extras stack on it: the remote panel is remote inventory (3), and
a power-low sensor warns before the dump (1).

**Automated mining: keep it, defer it past the demo.** A placed auto-miner (it works while you are
away) and the quantum tunneller (a tool in the hand) do different jobs, so lower miners are not made
useless by it, and higher auto-miners replacing lower ones is just the ladder. An unattended miner
also makes the mine more of an antagonist: it opens voids the water finds and roofs that fall, and a
sensor that stops it is the loop. **Conveyors are membership, like wire.** The ladder rejected
items riding belts (*no item on a belt*), so a conveyor is evidence that a miner and a container
share a line, and ore is credited straight into the container. It may be animated; nothing rides it.
For the demo it is not needed, so it goes on the survival ladder at DC afterwards.

## The scope of the pass

1. The extras rule: the engine side, the JSON grammar (today `via` accepts `channel` and `solar`,
   `carries` power and fluid, and `MachineKind` is a closed enum; see COLLAPSE-PLAN "Part f", fact 5).
2. The six extras, each with its inheritance and its cost.
3. The container ladder and the quantum vault: the hold's power bill, the dump, the identity terms,
   and the stock UI. M10b shipped no stock tab because nothing declared stock; this is the first thing that
   does.
4. Grades on parts, the second axis.
5. **The creative-mode demo itself.** Does a creative mode exist? Today machines arrive through `P`
   and `--stock=`. What is unlocked, what the demo shows in its first five minutes, and what it must
   not break (survival, the ladder).
6. What waits: automated mining and conveyors (DC, after the demo), multiplayer ("someone else's
   mine", M10-NOTES).

## Constraints the panel must hold (numbered, for the agents)

1. Non-negotiables 1–5 in CLAUDE.md. Core never references Godot. The base game is a mod.
2. The ladder's one idea: connection is membership, movement a ledger transfer at the 2 Hz boundary,
   no solver, no item on a belt, no flow solve, nothing in a hook but marking dirty.
3. Every door into a conserved quantity is a printed term (litres, joules, voxels).
4. The header and the machine table are two store calls. An inventory ↔ container transfer needs
   M10-LADDER decision 5's single-transaction overload first.
5. Machines tick at the fluid boundary (DESIGN.md:366 still says 20 Hz; that line is stale).
6. The water weakens rock (the keystone). Flooding on purpose feeds a collapse.
7. CPU: a boundary is already the played build's heaviest frame. Price every extra per boundary.
8. The collapse sensor (COLLAPSE-PLAN "Part f", thirteen decisions taken) is extra 1's quantum case.
   Its signal is a per-channel boolean, recomputed each boundary and never stored. The pass may
   generalise it, and must not contradict what f-1 builds.

## How to run it

As the design panel ([[design-shaping-pass-runs-a-panel]] in memory; the ladder's pass is the worked
example, M10-LADDER-PLAN's header):
- **Readers** map the code and notes. At least: the machine system (`MachineTick`, `MachineSet`, ports,
  the loader, `MachinePanel`); the ladder plan and M10-NOTES; the inventory and persistence (the
  header, `IWorldStore`); what a creative mode would touch.
- **Four designers** from named angles: engine cost, gameplay and the demo, mod format, physical
  plausibility.
- **Three judges**, each scoring all four through one lens: Core and the non-negotiables; CPU;
  gameplay and Matthew.
- **A synthesis, then a completeness critic** with a numbered checklist: per-tier completeness,
  persistence, CPU arithmetic, re-litigation, tests owed, scope widened, UI count, estimates stated as facts.

Give every agent the constraints above as a numbered list and his words verbatim. Then write the plan
yourself, in house style, as `docs/IDEAL-MACHINES-PLAN.md`: numbered decisions with recommendations,
costs and revisit triggers, and a split. End the session at the pass, with the decisions handed back.

## In parallel, not blocked

**Collapse part f, slice f-1** (Core and tests), in a worktree if he is playing. Its thirteen decisions
are taken (COLLAPSE-PLAN, "Part f"). It is extra 1's quantum case, and nothing in this brief undoes it.
