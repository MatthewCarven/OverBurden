# Common instructions for every designer on the ideal-machines panel

You are one of **four designers** on a design panel for Overburden, a Godot 4 + C# voxel mining game
(repo `C:\Users\Admin\Desktop\-=Programming=-\MinerCraft`). "You are a mining operation fighting water
and gravity": rain fills basins, water finds your excavation, saturated rock loses cohesion and the
ceiling comes down, and you build pumps and power to fight back. No mobs, no combat; the mine is the
antagonist. Matthew is the designer and owner. He will read the final plan, not your design, but
three judges will score your design against the other three through one lens each, and a critic will
check the synthesis. **Your design is a hypothesis. Push back on the brief where the facts say it is
wrong**, and say so plainly.

## Read first, in this order

1. `docs/IDEAL-MACHINES-BRIEF.md`, whole. **His words are verbatim there.** "What is taken" lists
   seven decisions that are his and are not yours to reopen (all six extras; inheritance in JSON; the
   creative-mode demo first; **one pool, bankruptcy a feature**; the scope; **automated quarries in**;
   **materials in**). Everything else in the brief is a recommendation to test.
2. The five reader maps in `<scratchpad>\ideal-machines\readers\`: `A-engine.md` (the machine code),
   `B-plans.md` (what is decided and rejected, and Matthew's words, dated), `C-store.md` (inventory,
   persistence, the conserved quantities), `D-creative.md` (what a creative mode would touch, keys, UI),
   `E-materials.md` (materials, ores, grades). **Their "Traps" sections are the most valuable part.**
   Where a map and the brief disagree, the map read the code; believe the map.
3. Anything in the repo you need to check a claim. **Read-only**: do not edit, build, run Godot or run
   tests.

`<scratchpad>` is `C:\Users\Admin\AppData\Local\Temp\claude\C--Users-Admin-Desktop---Programming---MinerCraft\0dd9b260-cc4b-4515-9e45-424b25b5a28c\scratchpad`.

## The constraints, numbered (corrected against the code)

1. **The non-negotiables.** Core never references Godot. Content IDs are namespaced strings in a table
   the world owns, never renumbered or compacted. `overburden:air` is 0. Texture arrays, never an atlas.
   **The base game is a mod**: it loads through the identical loader a third-party mod uses.
2. **The ladder's one idea:** connection is membership; movement is a ledger transfer at the boundary;
   no solver, no flow solve, no item on a belt, nothing in a hook but marking dirty.
3. **Every door into a conserved quantity is a printed term** (litres, joules, voxels). A vault that
   holds or dumps litres or voxels adds terms to identities that are checked every boundary today
   (`pumped == emitted`, `PowerExact`).
4. **Persistence:** the header, the machine table and the basins **already save in one SQLite
   transaction** (`WorldBatch`, built M10a-1). The brief's constraint 4 is stale. What does not exist
   is stock in a row, or any inventory ↔ row transfer.
5. **Machines tick at the fluid boundary**, nominally 2 Hz (0.5 s), measured as slow as 0.52 Hz under
   load. A late boundary makes a slower machine, never a wrong one. DESIGN.md:366's "20 Hz" is stale.
6. **Water weakens rock** (the keystone). Flooding on purpose feeds a collapse. Collapse part f's
   decision 10 kept "deliberately flooding" out for that reason.
7. **CPU.** The boundary is about 20 ms on the main thread on his heaviest played world since the
   2026-09-28 build fix. The machine tick's named trigger is `machines` over 1 ms on the played world.
   The Release bench: 0.6 ms per 100 wet pumps, 0.02 ms per 500 dry. **Price every extra per boundary**,
   and label each figure as measured (with its source) or estimated.
8. **The collapse sensor** (COLLAPSE-PLAN "Part f", thirteen decisions taken, slice f-1 about to be
   built): kind `sensor`, `"senses": "collapse"`, an `onsignal` port on readers, a per-channel boolean
   recomputed each boundary and never stored. You may generalise it (its own reversal clause allows a
   general signal medium to take over), but you must not contradict what f-1 builds.
9. **Only the quantum tier's membership exists in code.** AC, DC, steam and hand membership (cable,
   copper, shaft, crank) are plan only (M10e/M10f). The loader refuses those `via` names today. An extra
   "inherited down" to a lower tier has nothing to read until M10e; price and sequence it honestly.
10. **Kinds are C#.** `MachineKind` is a closed enum of four (pump, outlet, converter, storage) with about
    twenty switch sites; `storage` already means the battery. Unknown JSON keys are **silently ignored**
    by the loader today. A row's `KindState` is one opaque blob whose writers replace it whole.
11. **Matthew's taken 4: one pool.** At the last tech, the carried battery and the quantum block storage
    are one energy pool, and the quantum backpack and the quantum container are one item store. You can
    bankrupt yourself; the design must not protect the player from it, though it may warn.
12. **Matthew's taken 3: the creative-mode demo is the target.** Design the ideal (top) tier first; the
    ladder below comes later. No game-mode concept exists yet, and "fly is a measurement instrument, not
    a creative mode".

13. **Water has never been in the inventory**, and there is no water material. The hotbar's
    `1,767,903,281 L` is stone. A container that holds litres **reverses recorded rejections**: the
    bucket (ladder decision 6), the tank on the row (ladder decision 13), and M10-PLAN D5/D10 ("nothing
    is ever held by a machine", "no in-flight term to hide a litre in"). His own words ask for litres in
    the vault (2026-09-25: *"suddenly it has to find 10,000,000 litres of water to dump"*; 2026-09-29:
    *"else you might have to swim or mine to get it back"*), so it may be done, but **as a named
    amendment with its terms**, never by working around the rejections.
14. **No identity covers voxels in the inventory.** The only printed voxel identity is the collapse
    director's `released == landed + in flight + lost`. A design that says "the voxel identity gains a
    term" must first build that identity, and say so.
13a. **Taken 8 (Matthew, mid-pass): water is an inventory item at the last tech.** *"Yeah I sm hsppy to
    have water as an inventory item but you should have some tool or machine that adds it at the last
    tech level"*, *"or removes it from inventory"*. The bucket rejection is amended for the quantum tier
    only; litres enter and leave the inventory only through a quantum tool or machine made for it, each
    door a printed term.
13b. **Taken 9 (Matthew, mid-pass): the quarry works only where chunks are loaded, and that is fine.**
    *"I don't really care about "The quarry only digs where chunks are loaded" like we will mention it in
    the readme and say, want automated work? We expect you to at least be on the job site for OH&S
    reasons"*. Keep-loaded stays rejected; the README carries the line. His second reason: *"If server
    admins want another good reason its because it prevents players lagging out the server by placing a
    machine in every chunk of it."* The work machines do is bounded by the players' loaded boxes, not
    by the count of machines placed.
15. **The inventory is unbounded and weightless today, deliberately** (its revisit trigger is "M10
    containers or M11 crafting"). A container therefore has no gameplay reason yet; your design has to
    give it one or say why it doesn't need one.

**Reader D's map says the header and the machine table are two store calls.** It is wrong on that one
point: Readers A, B and C each read `WorldBatch` and confirm one transaction (constraint 4 above).

## Your deliverable

Write your design to the file named in your task: markdown, **5,000–8,000 words**, in this order.

1. **The idea in five sentences.**
2. **The extras rule**: the engine side (how a capability is defined once and dispatched, given the
   closed enum) and the JSON grammar (how a machine opts in; how inheritance is declared in JSON; what
   the loader refuses). Quote a full example JSON for at least two machines.
3. **The six extras**, each with: what it does at the top tier, its inheritance down the ladder (and
   what that has to wait for), its cost per boundary, its persistence, and its printed terms.
4. **The one pool**: energy (the carried battery and the block storage as one pool; what a tool's
   spend does; bankruptcy and its warning) and items (the quantum backpack and the quantum container as
   one store). Say what the inventory is under it, and **why a container has a gameplay reason** when
   the inventory today is unbounded and weightless.
5. **The container ladder and the quantum vault**: size (his: about a machine block), capacity, the
   hold's power bill, what it holds (voxels? litres? both? and against which recorded rejections), the
   dump (what, where, how fast, into which tier, unloaded chunks), the identity terms, and the stock UI.
6. **Grades**: where a grade lives (ID, per-stack, row), what it changes, and what it costs in IDs.
7. **The automated quarry**: what it digs, how it is bounded, J per voxel, where the spoil goes, what
   stops it, what water and the collapse do to it, what the undo does, its CPU per boundary.
8. **Materials**: what the ideal machines are built from, what the quarry yields, and where you cut
   (ores in worldgen and smelting may be their own pass; if you put ores in, say what it does to every
   recorded oracle).
9. **The creative-mode demo**: what a mode is in code and in the save, what is unlocked, what the first
   five minutes show, what it must not break (survival, the ladder, the oracles, persistence).
10. **The split**: slices, each a session or less, Core-and-tests cut from the wire, each with its test
    or oracle.
11. **Numbered decisions**, each with your recommendation, its honest cost, and the revisit trigger that
    would reverse it.
12. **Must not ship**: the mistakes a builder of your design is most likely to make.
13. **Your design's three weakest points**, in your own judgement.

Return to the coordinator only the path you wrote and a summary of at most 150 words.
