# Three-step undo — the planning pass

> Matthew, 2026-09-22, after breaking most of a world with the quantum excavator: *"how hard to
> implement 3 step undo Claude? :-{D Just plan um if its like too difficult i might skip it,
> ignoring water"*. Planned, not built. **Verdict: one session, and the reason is that almost
> nothing has to be undone** — light, support, the fluid surface, the mesh, the sky columns and
> the collider are all derived from the voxels and rebuild themselves from the same `SetVoxel`
> the undo writes through. What must actually be reversed is four things: the voxels, the
> inventory, the machine rows, and nothing else.
>
> **The two traps are gravity and unloading, and both are answered by clearing the stack**, which
> is cheap and honest because three steps is seconds of play.

## Six facts from the code

1. **Every edit in the game goes through one gate each way.** `Carve.Take` is the only place a
   voxel is removed and `Place.Put` the only place one is written, for every shape — sphere,
   voxel, cube, chunk, and the four brushes. Both already take three optional collaborators
   (`VolumetricInventory? into`, `IMachineBodies? bodies`, `ref int voxelBudget`), so a journal
   is a fourth parameter of exactly the shape that is already threaded through: no new call
   sites, no second copy of the geometry, and a shape that forgets to journal is a shape that
   forgets to credit — which the accounting tests already catch.
2. **The prior material is in hand at the moment of the write, in both directions.** `Take` has
   read `material` before it writes air; `Put` has read `existing` (and refused unless it is air).
   So a journal entry is free — no second read, no bounding-box scan, no chunk clone.
3. **Everything derived repairs itself.** `ChunkPipeline.SetVoxel` is what drives the remesh, the
   sky column, the light rebuild, the support region's dirty mark, the portal and seep trackers,
   the machine set's damage marks and the automaton's `Disturb`. An undo that writes through it
   is indistinguishable from a player writing the same voxels by hand — which is the whole reason
   this is a small feature and not a milestone.
4. **A fall is the one thing that writes without a click.** `Collapse` moves voxels down and mints
   rubble; so bite → the roof comes down → undo the bite would restore the roof *and* leave the
   rubble on the floor. That is matter from nothing, and no amount of journalling the fall fixes
   it cheaply: a fall's own undo would have to un-mint rubble that may since have been mined,
   walked on or built with.
5. **An unloaded chunk cannot be written and reloads its edited state.** A chunk that leaves the
   box is persisted as it now is; coming back it reads the *edited* voxels from the store. A
   journal entry for a chunk that is gone can neither be applied (`SetVoxel` returns false) nor
   deferred, because the chunk that returns is not the chunk that left.
6. **`MachineSet.Add` / `Remove` refuse mid-tick, and `MachineRow` is a value.** So a machine's
   row is trivially recorded and restored — with its ports and its charge — but the undo has to
   run where the machine drain runs (once a frame, outside the boundary), not inside the click.
   This is already how a dismantle works.

## Decisions, each with a recommendation

### 1. A step is the click, and the journal is a fourth collaborator — *recommended*

`IEditJournal` in `Overburden.Core/Editing/`, one method — `Wrote(VoxelPos voxel, int was, int now)`
— passed to `Carve.Take` and `Place.Put` beside `into` and `bodies`, null everywhere that does not
want one. `EditHistory` implements it, opens a step at the click and closes it when the verb
returns. A held `Q` is one step *per swing*, not per hold: a swing is what the player sees happen.

The alternative — snapshot each touched chunk before the edit — is rejected: it is more memory for
a brush (eight chunks cloned to change four voxels) and it silently reverts anything else that
wrote into those chunks in between, which is the fall problem wearing a disguise.

### 2. The stack is three steps, per chunk, in RAM, and not persisted — *recommended*

Entries grouped by chunk as `(ushort index, ushort was, ushort now)` — six bytes — because the
grouping is what decisions 4 and 5 need anyway. An excavator chunk bite is 32,768 entries = 192 KB;
three of them 576 KB, the worst case there is. A brush is a few dozen bytes.

**Not persisted**: a quit clears it. Undo is a hand-slip fix, not a save feature, and persisting it
means a header version, a blob format and a migration for something whose value expires in seconds.
*Stated cost: you cannot undo across a quit.* Revisit trigger: the first time someone quits, reopens
and asks for it.

### 3. Ctrl+Z, on the edge, printed — *recommended*

`Z` is unbound; Ctrl is sprint but a chord with an unbound key does not collide, and Ctrl+Z is
what every hand in the world already knows. One press is one step. `M10: undo — 32,768 voxel(s)
restored in 1 chunk(s), 1 machine row(s) back, 512,000 L of stone taken back; 2 step(s) left`.
Headless seam: `--undo=<n>` at the end of a profile window, for the oracle.

### 4. A fall clears the stack — *recommended*

Fact 4's answer. `CollapseScheduler` firing anything clears every step, printed
(`M10: undo — the stack is cleared: the roof came down`). The alternative arms — journalling the
fall, or refusing an undo whose chunks a fall touched — are both more code for a case the player
reads as "the mine moved, that is the game". **Stated cost: dig something unstable and your undo
is gone the moment it goes.** That is defensible in a game whose antagonist is gravity, and it is
the honest version of *ignoring water*: water is the cheap half, gravity is the coupled one.

### 5. A chunk leaving drops the steps that touch it — *recommended*

Fact 5's answer, on the streamer's `onChunkForgotten`, printed. Three steps is seconds, and a
chunk you edited leaves the box only if you fly away from it — at which point the undo was not
what you wanted anyway. The alternative, keeping the step and refusing at the press, is worse:
you press Ctrl+Z and nothing happens, with the step still listed.

### 6. The inventory is reversed exactly, and a step that cannot pay is refused whole — *recommended*

Each step records its own per-material delta (the journal already sees every material). Undoing a
bite takes back what it credited; if you have since spent it, the step is **refused whole and kept**
— `M10: undo — refused: 512,000 L of stone is no longer in the inventory` — rather than clamped,
because clamping is a quiet way to mint or destroy volume and this game's oracles are exact
totals. Undoing a fill gives the material back, which can never fail.

### 7. The pack refunds nothing, and the undo costs nothing — *recommended*

An undo is not a bite and not a fill: it charges no joules and refunds none. Any other rule makes
Ctrl+Z an energy exploit (bite, undo, bite again) or a punishment for a slip. One line in the
built section, and it is the sort of thing a player asks about once.

### 8. Water: the undo writes through the boundary's displacing path — *recommended*

*Ignoring water* is cheap because M9d already built the path: a fill into a wet voxel is deferred
to the fluid boundary and run with `FillWater` in hand — lift, write, return around the shape, the
rest `displaced`. An undo restoring rock where water has since flowed uses it unchanged, so every
Tier B identity stays exact and no new water rule exists. **What is *not* undone is the water
itself**: undoing the bite that flooded a room does not un-flood it — the water is where it is, and
it drains or is pumped like any other water. Say so once in the log line the first time a step
displaces (`… 1,204 L displaced; the water is not undone`).

### 9. The oracle: a step undone leaves the world byte-identical, on every shape — *recommended*

The test is a fuzz, and it is the cheapest sharp one available: random clicks of every shape
(sphere, voxel, cube, chunk, the four brushes) into a fixture world, each followed by an undo, with
the voxels, the inventory and the machine rows compared against a copy taken before the click.
Add the three-deep case (three clicks, three undos, in order), the refusal cases (spent inventory,
forgotten chunk, fired collapse), and a lifecycle case: edit, walk away, walk back, and the step is
gone rather than wrong. **The break-recipes that matter**: journalling `now` instead of `was`,
undoing in forward order rather than reverse, an undo that does not clear the redone step, and a
step left open across two clicks.

### 10. No redo — *recommended*

He asked for undo. Redo doubles the state, needs its own invalidation rule (any new edit clears it),
and nobody has asked. Named so it is not built by reflex.

## The cost

| | |
|---|---|
| Core | `IEditJournal`, `EditHistory` (~150 lines), the fourth parameter on `Take` and `Put` and the eight shapes that call them |
| Game | the Ctrl+Z bind, the undo at the frame's drain (with the machine reconcile), the three clears (fall, forget, quit), the `M10: undo` lines, `--undo=<n>` |
| Tests | the fuzz, the three-deep case, three refusals, the lifecycle — ~10 |
| Memory | 192 KB a chunk bite, 576 KB worst case, nothing at rest |
| Risk | low: the undo writes through the one path every edit already uses, and everything derived repairs itself |

**One session, built and tested in one go** — it does not want splitting, because the Core half
alone is untestable in the way that matters (the clears are all in the wire).

## What this would change elsewhere

- TODO gains an item under M10d's play with this document as its brief.
- DESIGN's M6 row (the verbs) gains a line: *an undo of the last three edits, in RAM, cleared by a
  fall or an unload*.
- The `M10:` line gains `undo N step(s) / M voxel(s) held, K cleared (fall / forget / quit)`.

## Built — 2026-09-22 (evening)

Built the same session as the pass, on *"Build it please Claude"*. Suite 1,719 → 1,735; the r6
control 2601 / 325 / 3446 at 411 to the digit; the a-3 pump oracle and the keystone unchanged.
Ten break-recipes, ten caught. **Five departures from the plan, four of them found by the code
and one by the battery.**

**The journal hangs off the two owners of state, not off seventeen verb signatures** (decision 1
rewritten). The plan's shape was a fourth collaborator on `Carve.Take` and `Place.Put`, threaded
through every public entry point; the code offered a cheaper one — `ChunkPipeline.SetVoxel` is the
single write and **already reads the previous material for its own early-out**, and `MachineSet`
owns every row change. So `IEditJournal` has two methods, there are two call sites, no verb
signature moved, and a shape that forgets to journal cannot be written. The plan's own fact 1 —
"one gate each way" — was right about the principle and one level too high about where to put it.

**A step is a frame in which something was written, not a click** (decision 1's other half). A
click is not where all of a click's writing happens: a fill into water is deferred to the fluid
boundary, a dismantle lands at the frame's machine drain, a chunk fill is queued. `EndStep()` runs
once a frame after `DrainMachines()`, so a bite and the dismantle it caused are one step, and the
one cost is that a click whose work is deferred to a later frame is two steps.

**The battery's catch: an undo writes through the journal it feeds.** Every `SetVoxel` an undo
makes was recorded as a fresh step, so the press after an undo *redid* the edit and three steps
bounced between two states. Two tests failed on it, both on the whole-world oracle and neither on
a counter. `Undo` turns recording off around its own writes — which is also decision 10, *no redo*,
falling out for free.

**Two things at the edges that would have shipped as defects.** `MachineSet.Restore` calls `Add`
per stored row, so wiring the journal before the restore would hand a player who opened a world
with 141 machines an undo whose first press deletes one — the journal is attached *after* the
restore. And the collapse sweep writes through `SetVoxel` too, so the fall's own rubble landed in
a step and the clear then reported the fall's step as the player's; recording is off for the
duration of the sweep, and the clear's message now fires only when the player had something.

**The memory is four times the plan's estimate and still small.** The plan said six bytes an
entry; the dedupe wants a dictionary per chunk, so an excavator chunk bite is ~800 KB and the
headless oracle reads `undo 3 step(s) / 94,112 voxel(s) held` for three of them — about 2.5 MB at
the worst case there is, nothing at rest. A flat array per touched chunk would beat it for bites
and lose for brushes; not built. **`EditHistory.Recording` is the off switch and has no flag** —
named as the control arm if the memory is ever worth measuring.

**What shipped as planned**: three steps; Ctrl+Z on the edge; the fall's clear and the forget's
clear, both printed; the inventory as the exact inverse derived from the entries with a step that
cannot pay refused *whole and kept*; no energy charged or refunded; the water not undone; no redo.

**The seams.** `--undo=<n>` presses it that many times, one a frame, from `--undoafter=<seconds>`
(3) — headless has no Ctrl+Z, and a verb tested only through Core is M5c-2's shape of hole. The
`M10:` line carries `undo N step(s) / M voxel(s) held, P press(es), R refused, cleared F by a fall
/ G by a forget / D off the end`. **The oracle, verbatim** — two chunk bites put back, 29,371
voxels each, 458,922 L out of the inventory:

```bash
../GAME/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path Overburden.Game --headless -- --profile --speed=0 --mine --entangle --entanglechunk --nogate --pitch=-60 --undo=2 --undoafter=4 --measure=8
```

and the fall's clear, which reads `M10: undo — the stack is cleared (1 step(s)): the roof came
down` exactly once before the first `M9: fell`:

```bash
../GAME/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path Overburden.Game --headless -- --profile --speed=0 --bitechunks=2 --collapseafter=1,2 --measure=20
```

**The play check, one sentence back on each**: (1) take a chunk with the excavator, Ctrl+Z — does
the world come back, and does the inventory going *down* read right? (2) Three presses in a row
after three bites, then a fourth — does "nothing to undo" arrive where you expect? (3) Bite
something unstable, let the roof come down, Ctrl+Z — the log says the stack was cleared; does
losing the undo to a fall read as fair? **Answered the same evening — see below.**

### Played, the same evening — decision 4 confirmed, and *disallow* is not *silently empty*

Matthew, first press: **"Ctrl z feels good, disallow if something falls yes"**. Decision 4 —
a fall clears the stack — is settled by play rather than by argument, and decision 3's bind and
the three-step depth go with it.

**One thing his wording changed.** The build cleared the stack and printed *why* at the moment of
the fall; a press after that read `nothing to undo`, which is the *silently empty* version, and
the fall's own line has scrolled past while you are running out of the way. *Disallow* implies
being told, so `EditHistory.FellSinceLastEdit` carries the reason until the next edit clears it,
`UndoOutcome.EmptyAfterAFall` reports it, and the press reads:

```
M10: undo — nothing to undo: the roof came down since your last edit, and rubble cannot be put back
```

The flag is cleared by the first journalled write, so an ordinary empty stack still reads as
`nothing to undo`. One test asserts both halves in sequence; suite unchanged at 1,735.
