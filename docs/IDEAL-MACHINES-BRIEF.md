# The ideal machines — the brief for the planning pass (2026-09-29)

The chat the collapse sensor's design once waited for (COLLAPSE-PLAN decision 13). It was held
at the end of 2026-09-29's long session, and the pass itself is for a **fresh chat**. This file is
what that chat starts from: his words, his answers, the recommendations he agreed to, the scope,
and how to run the pass. **Nothing is decided beyond his three answers below.** Everything else
is a recommendation for the panel to test.

**The pass ran on 2026-09-29, and its plan is [IDEAL-MACHINES-PLAN.md](IDEAL-MACHINES-PLAN.md)**, with
sixty-one decisions and ten forks, all taken as recommended the same night. Two of this brief's statements were wrong against the
code: constraint 4 (the header and machine table already save in one transaction) and extra 4's
"exists" (there is no dock; the lead-acid pack recharges wirelessly). The plan's "Eight facts" has the
corrections.

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

**The data, given at the start of the planning chat (2026-09-29), verbatim:**

> *"Ok so we have pumps, energy storage both mobile and block, we need a handover crossover for them
> between your mobile batter and quantum storage (when you get to the end tech level) so recharging is
> a non issue same with quantum backpacks and quantum storage containers about the same size as the
> current machine blocks"*

Read as three asks (the panel must test the reading, not assume it):
1. **Energy: a handover between the carried battery and quantum block storage**, at the top tier only,
   so that recharging stops being a trip. This is extra 4 (remote charge) made the defining property
   of the quantum tier rather than one extra among six.
2. **Items: the same handover between a quantum backpack and a quantum storage container**, so that
   emptying your pack stops being a trip either. The quantum backpack is new; nothing carried holds
   items remotely today.
3. **The quantum container is about the size of a current machine block.** Bigger on the inside is the
   point; it is not a multi-block structure.

Asked back: whether "handover crossover" means **one pool** (the pack is a window onto the vault) or
**two pools with an automatic transfer** at each boundary. His answer, verbatim:

> *"One pool please Claude you should be able to bankrupt yourself energy wise at the last tech we will
> cover other machines at a later time, and they will get their own seperate passes like"*

So, taken:
4. **One pool.** At the last tech the carried battery and the quantum block storage are one pool, and
   the quantum backpack and the quantum container are one store. **Bankruptcy is a feature**: what you
   spend in the hand comes out of the bank, and you can run it dry. The design must not protect the
   player from it (no reserve floor, no refusal to drain), though it may warn.
5. **The pass covers pumps, energy storage (carried and block), and the containers and backpacks.**
   Other machines get their own passes later. The six extras stay in, as they apply to these. He added,
   mid-turn: *"or whatever you care to include is up to you please and thank you claude"*, so the
   edges of the scope are the pass's call. It is drawn at: the extras rule and the six extras, the
   one-pool handover, the container ladder, grades, and the demo; everything else (conveyors, other
   machine families) waits for its own pass.
6. **Automated quarries are in.** His words, verbatim: *"ok um automated quarries if you can dream up
   something"*. This reverses the "defer past the demo" recommendation below for the quarry itself.
   The ideal-tier quarry is designed in this pass: a placed machine that digs on its own, paid from the
   one energy pool and crediting the one item store, so an unattended quarry can bankrupt you, and it
   opens voids the water finds and roofs that fall. Lower-tier miners and conveyors stay on the survival
   ladder for their own pass, unless the design needs them.

His earlier words on the container (2026-09-25) are in [M10-NOTES.md](M10-NOTES.md), "The quantum
storage container". That is where the **grades** come from: *"low grade, medium grade, and high
grade, mythical grade, and god grade, and your battery tech maybe 19th century but your actual
emitter/entangler could be a higher tech variation"*.

## What is taken

1. **All six extras** (listed below) are in.
2. **Inheritance is declared in JSON** ("ideally, but up to you"). The base game declares it the way
   a mod would (non-negotiable 5).
3. **The target is the creative-mode demo.** Design the ideal machines first, then work down the ladder.
4. **One pool** (planning chat, 2026-09-29): carried and placed share one energy pool and one item store
   at the last tech, and you can bankrupt yourself. See "The data" above.
5. **Scope:** pumps, energy storage and containers; other machines get separate passes. The edges are
   the pass's call (*"whatever you care to include is up to you"*).
6. **Automated quarries are in** (*"ok um automated quarries if you can dream up something"*), the
   ideal-tier quarry designed in this pass. See "The data" above.
7. **Materials are in** (*"materials if you want to run another agent like iron steel etc"*): what the
   machines are built from and what the quarry digs. A fifth reader maps what exists. The pass decides how
   far to go; ore placement in worldgen and smelting chains may be cut to their own pass.
8. **Water is an inventory item at the last tech** (planning chat, 2026-09-29, told that litres in the
   inventory reverse the ladder's rejected bucket and tank): *"Yeah I sm hsppy to have water as an
   inventory item but you should have some tool or machine that adds it at the last tech level"*, then
   *"or removes it from inventory"*. So the bucket rejection (ladder decision 6) is amended **for the
   quantum tier only**: litres enter and leave the inventory only through a quantum tool or machine made
   for it, never by an ordinary pick or placement, and every such door is a printed term.
9. **The quarry works only where chunks are loaded, and that is fine** (planning chat, 2026-09-29, told
   that a quarry stalls `unloaded` beyond the loaded box because a keep-loaded policy was rejected): *"I
   don't really care about "The quarry only digs where chunks are loaded" like we will mention it in the
   readme and say, want automated work? We expect you to at least be on the job site for OH&S reasons"*.
   No keep-loaded policy is reopened; the README carries the line in his framing. His second reason, for
   multiplayer: *"If server admins want another good reason its because it prevents players lagging out
   the server by placing a machine in every chunk of it."* So the rule is also a server's CPU bound: the
   work machines do is bounded by the loaded boxes of the players present, not by the machines placed.
   **Not quite all of it:** rows still tick unloaded for their row-local terms (the power pool, solar's
   last sample), about 0.02 ms per 500 dry rows on the Release bench, so that part grows with machines
   placed. **A per-player machine cap is a revisit trigger, not built** (his word, *"yeah note the cap as
   a trigger"*): it fires when the row tick alone passes 1 ms on a played world or a server, the same
   `machines` > 1 ms line ladder decision 22 already names for a machine cap.

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

**Superseded in part (planning chat, 2026-09-29):** he asked for automated quarries in this pass (taken
6). The ideal-tier quarry is designed now; the reasoning above still holds for the lower-tier miners and
conveyors, which keep their own later pass.

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
6. **The automated quarry at the ideal tier** (taken 6, added in the planning chat): what it digs, how
   it is bounded, what it costs per voxel, where the spoil goes, what stops it, and what the water and
   the collapse do to it.
7. What waits: lower-tier miners and conveyors (DC, their own pass), other machine families,
   multiplayer ("someone else's mine", M10-NOTES).

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
