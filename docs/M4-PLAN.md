# M4 — The player

> Planning doc. See [DESIGN.md](DESIGN.md) for the decisions this implements.
> Written 2026-08-29.

Every milestone so far has taken the viewer's position as an *input*. The flycam is told
where to go by the keyboard, the streamer follows it, and the world is whatever has finished
loading by the time the camera gets there. M4 inverts that: the player's position becomes an
**output of a query against the very chunks the player decides to load**.

That is a cycle, and how it is broken is the structural decision of this milestone:

- The player is solved at step *N* against the world **as it was loaded at step *N*−1**.
- An unloaded voxel is **solid**, so when the cycle cannot be closed the player stops at the
  edge of the loaded world rather than leaving it.

Get the second one backwards — read an unloaded voxel as air, which is what
`ChunkStreamer.TryGetMaterial` hands you if you ignore its return value — and the failure is
not a glitch. The player falls out of the world, accelerates away from the loaded region, and
every subsequent frame is further from any chunk that could stop them. It is the same
distinction M3b drew for the raycast, where `Blocked` is not `Missed`, at a layer where the
consequence is unrecoverable instead of merely confusing.

| Part | What it is | The risk it removes |
|---|---|---|
| **M4a** The collider ✅ | AABB vs voxel grid, axis-separated, substepped | Falling through the floor, snagging on flat walls, jittering at rest |
| **M4b** Motion ✅ | Gravity, jump, ground state, step-up and step-down | A controller that is correct and horrible to use |
| **M4c** Binding ✅ | Player node, camera, walk/fly toggle, spawn | Deleting the flycam and taking every measurement built in M2d and M3d with it |
| **M4d** Feel and measurement ✅ | Can the player outrun the streamer, and does 0.25 m ground walk like ground | Constants guessed once and never questioned |

**M4c is the part most likely to surprise**, which is not where the surprise usually is. M4a
and M4b are deterministic, headless and fuzzable — genuinely tractable. M4c is where the
player controller meets a streaming system, a profiler and a scripted flythrough that were
all built assuming the camera goes wherever it is put.

---

## M4a — The collider — done 2026-08-29

### The problem

Given a box in metres, a displacement in metres, and a voxel world, where does the box
actually end up.

### Axis-separated, because a combined sweep snags on flat walls

DESIGN already settled this: move X and resolve, then Y, then Z. It is worth restating *why*,
because the alternative looks better on paper. A combined sweep finds the earliest time of
impact across all three axes and stops there. Slide along a flat wall built out of voxels and
every voxel boundary is a separate impact — the sweep stops at each one, and the player
crawls along a wall that should be frictionless, or catches on a seam between two coplanar
voxels. Resolving one axis at a time makes sliding the default: the blocked axis is
cancelled, the free axes keep their full displacement, and there is no seam to catch on
because the wall is one flat constraint per axis.

The cost is that a diagonal move into an inside corner resolves both axes independently and
can end up in a slightly different place than a true sweep would. Nobody has ever noticed
this in a voxel game, and it is the trade DESIGN made deliberately.

### Ground state falls out of the solve rather than being probed for

`grounded` is "the −Y resolve fired this step". Gravity applies every step, so a standing
player always has a small downward displacement, which always resolves against the floor,
which always sets the flag. No second query, nothing to keep in sync, and it cannot disagree
with the collision that just happened.

The consequence, stated because M4b depends on it: **step-up reads the previous step's ground
state**, since X and Z resolve before Y in DESIGN's order. One step of latency at 60 Hz is
16 ms. The alternative is a separate downward probe before the horizontal move, which is an
extra ~40 voxel reads per step to buy back a sixtieth of a second.

### Substepping, because move-then-resolve tunnels and the maths says exactly when

Resolving overlap *after* a move only works if the move did not step clean over the obstacle.
The thinnest obstacle is one voxel, 0.25 m. A physics step is 1/60 s. So the move-then-resolve
scheme is exact below **15 m/s** and starts silently tunnelling above it.

That is not an abstract worry. A sprint is somewhere under 10 m/s and is fine; a fall reaches
terminal velocity in under two seconds and is not. **Walking never tunnels and falling always
does**, which is the worst possible shape for a bug because it never appears while you are
testing movement.

The fix is to split the step's displacement so that no substep exceeds **0.2 m** — under one
voxel, with margin for the resolve epsilon — and run the full X-Y-Z resolve per substep. At a
40 m/s terminal velocity that is 4 substeps in the worst case and 1 substep whenever the
player is walking. The cap is on displacement, **not** on the box's own dimensions: a 0.25 m
floor is thinner than the player, so sizing the substep to the box would still tunnel.

**Rejected, and why:**

- **A swept AABB against the grid.** Exact at any speed and unbounded in cost, but it is a
  different algorithm with its own corner cases, and the sweep is precisely what DESIGN
  rejected for snagging. Substepping keeps the axis-separated resolve — the thing that has to
  stay correct — completely untouched and bounds the cost at 4 iterations.
  **Revisit trigger:** anything that moves the player faster than the substep budget makes
  sensible. A mine cart at 100 m/s would want 20 substeps a frame and deserves a real sweep.
- **Clamping velocity so the step can never tunnel.** Solves it by making terminal velocity a
  physics-engine artefact rather than a design choice, and 15 m/s is a floaty, wrong-feeling
  fall.
- **Running the whole controller at 240 Hz.** Buys a 4× margin at 4× the cost of everything
  else in the step, including the voxel queries, to fix a problem that only exists on the
  small fraction of steps where the player is moving fast.

### Solidity is a predicate, not a comparison against air

`MaterialTable.IsFluid` already exists and is already projected. So the collision test is
`id != air && !IsFluid(id)` and it is correct *today* — fluids simply do not exist yet — and
still correct at M8 when they do. `VoxelRay` stops at the first non-air voxel and carries a
recorded trigger to revisit that at M8; M4 does not need the same trigger because the
predicate form costs nothing now.

### Resting contact is where hand-rolled colliders actually fail

Not tunnelling — that has a clean fix above. The bug that survives into shipped games is the
one-frame oscillation: the resolve places the box exactly on a voxel face, the next step's
overlap test finds it touching, pushes it out again, and the camera vibrates by a millimetre
forever while `grounded` flickers and jump misfires.

The rule: **resolve to the contact face minus an epsilon, and treat exact touching as not
overlapping.** Overlap is a strict inequality on the open interval. A box whose max exactly
equals a solid voxel's min is resting on it, not inside it. The test for this is not a fuzz
test — it is standing still for 10,000 steps and asserting the position never changes and
`grounded` never flickers.

### Where it lives

`MinerCraft.Core/Physics/` — an `Aabb` in metres and a `VoxelCollider.Move(world, box,
displacement)` returning the resolved box and which axes were blocked. Core, because where a
player may stand is a world rule and a multiplayer server has to agree about it, and because
Core is where it can be fuzzed against a dictionary of voxels with no Godot, no threads and no
world generator. `System.Numerics.Vector3`, as `MiningController` already uses.

### Query cost — guessed here, and measured while building it

The guess: a 0.5 × 1.8 × 0.5 m box spans 2 × 8 × 2 = 32 voxels, three axis resolves per
substep, up to four substeps — so up to ~400 `TryGetMaterial` calls per step and ~24,000 per
second, each a dictionary lookup on `ChunkPos` plus a `VoxelChunk.Get`. The named fix if it
mattered was memoising the chunk across a scan, and **not** a spatial structure.

**Measured: a walking step costs 41, and the worst alignment is 63 per substep** — 252 at a
four-substep terminal-velocity fall. The guess assumed each resolve scans the whole box; it
scans one voxel *layer*, the one the leading face ends in. **Do not build the memoisation**,
and M4d does not have to re-check this.

One detail the arithmetic above got wrong, recorded because it will come up again: a 0.5 m box
that is not grid-aligned straddles **three** voxel columns, not two. The layer costs are
16 + 1 + 24 for a walking step, not the 16 + 4 + 16 that "2 × 8 × 2" suggests — the first
horizontal resolve puts the box off-grid and the second one then costs half as much again.

### Tests

- **The postcondition every motion test shares:** the box never *ends* a step overlapping a
  solid voxel. Asserted in a helper, in all of them, not only in the fuzz.
- **Hand-built cases:** resting on a plane; driven into a wall on each of the six faces;
  wedged in a slot exactly one voxel wider than the box; dropped into a 2-voxel-wide pit.
- **The corner-snag test.** Slide diagonally along a flat wall for many steps and assert the
  tangential distance travelled equals the tangential component of the input. A combined
  sweep stops dead; this is the bug axis separation exists to prevent, so its absence needs
  a test that fails when someone "simplifies" the resolve back into one pass.
- **The tunnelling fuzz, and it has a sharp oracle: seal the player in a room.** Build a
  closed shell of solid voxels, put the box inside, and apply thousands of random
  displacements of random magnitude — including magnitudes far past terminal velocity.
  Assert the box is still inside the room, every step. No reference implementation, no
  sampling, no tolerance: escaping a sealed room is a boolean. **Watch it fail** — removing
  the substep cap should break it almost immediately, and if it does not, the displacements
  are not large enough.
- **Unloaded is solid.** A world with a deliberate hole in its coverage: the player falls onto
  it and stops, and the box never enters the unloaded region.
- **Determinism.** The same input sequence produces a bitwise-identical trajectory. Cheap now,
  and it is the whole of the multiplayer claim.
- **Standing still.** 10,000 steps on a flat floor: position unchanged, `grounded` never false.

---

## M4b — Motion — done 2026-08-29

### The problem

Turning intent into displacement, and doing it in numbers that feel like a person rather than
a physics demo.

### A fixed step, at 60 Hz, which is not the 20 Hz sim tick

DESIGN specifies a fixed 20 Hz sim step for machines, "decoupled from render framerate, keeps
it deterministic for possible multiplayer". The player does **not** run on it. 20 Hz is 50 ms
of input latency, which is the difference between a controller that feels tight and one that
feels like a laggy server. Two rates, and the reason they differ is that machines are
simulated and the player is *driven*.

60 Hz, via Godot's `_PhysicsProcess`, whose accumulator already does the fixed-step work.
Core's step takes a `dt` argument so it can be driven at any rate from a test or a headless
profile, which is what makes M4d's fall measurement possible.

### The constants, all of them estimates, and labelled as such

None of these can be derived. They are the starting numbers, chosen so the relationships
between them are right, and M4d replaces them by playing.

| | Value | Where it comes from |
|---|---|---|
| Box | 0.5 × 1.8 × 0.5 m | 2 × 7.2 × 2 voxels — see the tunnel check below |
| Eye height | 1.65 m | Human-ish, and it is the mining ray's origin |
| Gravity | 25 m/s² | Real 9.81 reads as floaty; games run 2–3× |
| Jump | 7.5 m/s initial | → 1.125 m analytic apex. **Measured: 1.1875 m** — see below |
| Walk | 4.5 m/s | 1.4 m/s is a real walking pace and is glacial in a game |
| Sprint | 7.0 m/s | Below the 15 m/s tunnelling threshold with room to spare |
| Terminal | 40 m/s | **Set by streaming, not by realism** — see below |
| Step height | 0.5 m | 2 voxels. **One constant, used by step-up and step-down both** |
| Reach | ~~3.5 m~~ → **5 m** | **Not M4b's after all** — not a body dimension, so it moved to `PlayerController` at M4c. **Settled by play 2026-08-30: 5 m stays.** The 3.5 m in this row was the estimate and was never shipped |

**Terminal velocity is a streaming parameter wearing a physics costume.** The vertical view
radius is 2–3 chunks, so there is 16–24 m of loaded world beneath the player. At 40 m/s that
is crossed in 0.4–0.6 s, and if the streamer cannot produce the next chunk in that time the
player stands on nothing until it does. The honest way to pick this number is to measure how
fast chunks arrive under a falling viewer, which is M4d. 40 is where to start.

**The player must fit down their own tunnel, and that is a check against M3's content.** The
weakest tool, `hand_pick`, has a 0.4 m radius. A swing centred on a voxel centre removes every
voxel whose centre is within 0.4 m, which along any axis through the centre is three voxels —
a 0.75 m free bore. A 0.5 m box passes with 0.125 m either side. Widen the player past 0.75 m
and the first thing a new player does, digging into a hillside with the starting pick,
produces a tunnel they cannot walk down. **The player's width and the weakest tool's bite are
coupled**, and neither can be changed alone.

**Measured while building it: the apex is 1.1875 m, not 1.125 m.** `v²/2g` is the continuous
answer and the discrete integration does not land on it. A jump *replaces* its step's gravity
rather than being reduced by it — the body leaves at exactly the launch speed — so the first
step travels `v·dt` where the continuous solution travels the average over that step, and the
rise overshoots by exactly half a step of launch speed, `v·dt/2` = 0.0625 m at 60 Hz. So the
clearance over a four-voxel ledge is **0.1875 m rather than the 0.125 m this table originally
claimed**, and it is **rate-dependent** — the same constants at 30 Hz overshoot twice as far.
Nothing depends on that today because the player runs at one rate; it is recorded because a
jump height that changes with the tick rate gets diagnosed as a physics bug.

### Step-up is what makes 0.25 m terrain walkable at all

Terrain generated from a heightmap steps by whole voxels. At 0.25 m that is a staircase with
7 steps per metre of slope, and without step-up every one of them is a wall you have to jump.
Step-up is not a convenience here the way it is in a 1 m-block game; it is the difference
between walking on the ground and not.

The mechanism, after the horizontal resolve reports blocked:

1. Only if **grounded** — otherwise it is a wall-climb, and the player scales cliffs by
   holding forward against them in mid-air.
2. Only if moving **non-upward** — the same exploit through a different door.
3. Lift the box by up to the step height, re-attempt the horizontal move, and require it to
   **fully succeed**. A partial success means squeezing into a gap that is not there.
4. Require **headroom**: the whole box must be clear at the lifted position, not just the
   feet. Otherwise the player stands up into a low ceiling and ends the step inside rock.
5. Settle back down onto whatever is beneath.

**Step-down is the half people forget.** Walking off a 1-voxel lip without it makes the player
airborne for two or three steps, which at 0.25 m granularity means the camera bobs continuously
on any downhill slope. The guard is the mirror of step-up's: only when grounded at the start
of the step, and only when ground is found within the step height — so walking off an actual
ledge still falls.

**Three things building it settled, and one it did not:**

- **Guard 4 is structural rather than a separate check.** The lift and the re-attempt are both
  real `VoxelCollider.Move` calls, and a `Move` that did not start overlapping cannot end
  overlapping — so a ceiling stops the lift short, and a low ceiling over the destination blocks
  the re-attempt against the ceiling voxel itself. Tested anyway, because a guarantee that comes
  from somewhere else is the kind that quietly stops holding.
- **The lift is placed at its exact target, not at accumulated substeps** — and *that* is what
  keeps M4a's recorded "`ContactEpsilonMetres` can be zeroed with the suite green" claim true.
  With the epsilon in place the snap changes no outcome; with the epsilon zeroed and the lift
  accumulated, four tests fail. A finding in the previous milestone was one line from going
  stale silently.
- **The settle drops by exactly what the lift achieved**, so a step-up is a climb and never a
  fall. What that protects is step-down's distance limit: a settle that could go further gives
  the same rule two answers depending on whether the body happened to climb something first.
- **Not settled — guard 3 has a feel cost.** Walking diagonally into a corner where one axis is
  a climbable lip and the other is a tall wall refuses the climb, because the lifted re-attempt
  is still blocked on the wall: a player hugging a wall while walking up a slope stops dead. If
  M4d finds that reads as stuck, the relaxation is one line — require the re-attempt to be
  blocked on no axis the original move was not already blocked on.

**One constant serves both directions.** Step-down triggers "within the step height", and
stepping down a lip you just stepped up should not behave differently, so there is a single
`StepHeightMetres` rather than two knobs. Revisit at M4d if the instant 0.5 m drop reads as a
jolt where the matching rise does not.

### Jump feel is deliberately not tuned here

Coyote time, jump buffering, and variable jump height from how long the button is held are all
real and all decided by playing, not by reasoning. They are M4d's, and they are additive — the
model above does not have to change to accept any of them.

### Tests

- Fall onto a floor from height: comes to rest exactly on top, vertical velocity zeroed,
  `grounded` true, and — the postcondition — not overlapping.
- Jump from rest reaches the predicted apex within one step's worth of tolerance, derived from
  `dt` rather than a magic number.
- Head-bonk: jumping under a 2-voxel ceiling zeroes upward velocity rather than sticking.
- Terminal velocity is reached and never exceeded, over a long fall.
- Step-up climbs a 2-voxel lip while walking and **refuses** a 3-voxel one.
- Step-up refuses in mid-air, refuses while moving upward, and refuses when the lifted box has
  no headroom. Three separate tests, because these are three separate ways to climb a wall.
- Step-down holds the player on a 1-voxel drop and lets them fall off a 10-voxel one.
- Determinism again, at this layer: an input sequence replays identically.

**All of the above exist, plus 27 more — and ten break-recipes were run against the finished
code.** Eight were caught. The two that were not are the interesting half:

- **Lift accumulated instead of snapped to its target** — caught by nothing, because the contact
  epsilon masks it. Chased down to the cross-milestone claim above and now pinned by a test that
  places the feet exactly on a voxel plane, which is what M4c's spawn and a fly toggle both do.
- **Step-up settles further than it lifted** — caught by nothing, because reaching it needs a
  body that clears what it just climbed in a single step. Walking cannot: 0.075 m of travel
  against a body 0.5 m wide. The test uses a 45 m/s body and says why.

The one worth noticing among the eight: removing step-down's non-upward guard fails **six**
tests, every jump test among them. That is the shape of the bug rather than redundancy — the
step-down probe finds the floor the jump just left and snaps the body back onto it, so jumping
does nothing at all.

**A signal M4d needs that does not exist yet:** `MoveResult` cannot distinguish "stopped by
rock" from "stopped by an unloaded chunk", which is exactly M4d's *steps spent held* — the
number meant to pick terminal velocity. It is a flag on `IsSolidUncounted` and a field on
`MoveResult`, deliberately not built at M4b because an unread flag with no profiler consuming it
is a number waiting to be believed.

---

## M4c — Binding — done 2026-08-29

### The flycam does not get deleted, and this is the part with the trap in it

`FlyCam`'s own doc comment says it is "replaced by a real player controller with AABB
collision at M4". That is wrong, and following it would quietly destroy the measurement
apparatus M2d and M3d were built to create. `--profile` drives the camera along a scripted
path by assigning `GlobalPosition` directly; `--mine` holds the button while it flies;
`AdvanceFlythrough` snaps the camera to a fixed height above the surface every frame. All of
that assumes a camera that goes where it is put, and a camera parented to a physics body does
not.

So: **walk mode and fly mode, toggled with a key, and `--profile` implies fly.** The player
body is inert in fly mode. Every existing measurement keeps working unchanged, which also
means M4d's new numbers are comparable against M3d's.

### Order of operations within a step

Player steps → viewer moves → pipeline updates. In that order, and it matters: if the pipeline
ran first, the player would be solved against a world one frame staler than necessary, on the
frame where being current is the whole point. This is the cycle from the top of the document,
resolved in favour of the player seeing the freshest world the streamer has managed.

### Reach becomes the player's, closing a recorded item

M3d put `ReachMetres` on `MiningController` with a comment saying it belongs to M4's player
when one exists. One exists now. The field moves, and the play-test's note — "reads a little
long at 5 m, which is twenty voxels" — becomes M4d's to settle.

**Settled 2026-08-30: 5 m stays.** Judged generous rather than wrong, and generous is the right
error for a sandbox. **Recorded revisit trigger: a survival mode, where it wants 3 m or 2 m** —
which is now the *second* thing waiting on a game-mode concept, alongside whether the precision
mining modifier should be gated. Two is worth noticing; it is the argument for when modes should
exist rather than a reason to build them now.

### Spawn, which is the cycle at its worst

At the first frame nothing is loaded, so every voxel is solid, so the player cannot move. Three
things:

- **Gate the player on the chunks under them existing.** Hold the body inert until the chunk
  containing the spawn point and the one below it have terrain. This is a handful of lines and
  it is the difference between spawning and spawning inside the void.
- **Place on the surface.** `WorldGenerator.SurfaceHeightAt` already exists and `Main` already
  uses it for the camera.
- **An unstick rule.** If the box starts overlapping solid — a cave mouth at the spawn column,
  or a mode toggle inside a wall — push it upward until it is clear rather than locking it.
  Locking is the failure that reads as the game being broken; a shove reads as a hiccup.

### Mining while walking

The carve can remove the floor under the player, who then falls into their own hole. This
works for free and is the correct behaviour, and it is the first time in the project's life
that the world changes under something that cares. What has **no** answer at M4 is material
appearing *inside* the box — M6's placement can do it deliberately and M9's collapse can do it
by accident. **Recorded revisit trigger:** the first system that can create solid material at
the player's position needs a policy, and the unstick rule above is the obvious seed of it.

### Walking into your own mine means walking into the dark

There is no block light until M5. This will be reported as a bug the first time somebody digs
a tunnel and walks in. It is not one, and writing it down here is cheaper than diagnosing it.

### What building it settled

**The part most likely to surprise did not.** The prediction at the top of this document was
that M4a and M4b would be tractable and M4c would be where the trouble was. It was the other
way round: the binding was mechanical, and the two things nothing caught were both in the
world rules that went into Core.

- **The flycam kept every number.** `FlyCam.FlightEnabled` is the whole toggle — movement off,
  mouse-look on, no reparent and no second camera. The default headless run reports **1521
  viewed / 165 drawn / 1926 jobs**, M2d's recorded figures unchanged to the digit, so M4d's
  numbers are comparable against M3d's exactly as intended. A profile run also refuses the
  toggle mid-run rather than merely starting in fly mode.
- **The order came for free.** Player → viewer → pipeline holds because the step is in
  `_PhysicsProcess` and the rest in `_Process`, and Godot runs every physics tick of a frame
  before that frame's `_Process`. Nothing had to be sequenced by hand.
- **Gate, then unstick, then step — and the reverse order is the unrecoverable one.** Against an
  unloaded world every unstick candidate is solid, the search runs to its 4 m bound, and a body
  standing in open sky is reported stuck. Measured: **13–14 steps, about 0.22 s, held for the
  world** at the default view distance — it moves run to run with worker scheduling.
- **An unstick lifts and never drops, and that is a decision rather than a consequence.** A body
  with its head in a ceiling is pushed up through it and onto the roof. Up is the only direction
  whose answer does not depend on which face was overlapping, and a body that can be pushed down
  by material appearing above it can be pushed into the floor. **Caught by nothing** — every
  buried-body test placed the feet on a plane already, where the downward candidate is the box
  itself. This is the seed M6's placement inherits, up-only decision included.
- **`Aabb.Translated` versus rebuilding the box is undetectable, and is kept anyway.** No
  failing case exists inside the few-kilometre world M4a's float analysis allows. Recorded as a
  documentation correction rather than as a caught bug — the same shape as M4a's zeroable
  contact epsilon.
- **Feet exactly on a voxel plane survives exactly one step.** The first −Y resolve places the
  body `ContactEpsilonMetres` clear, so the spawn's 1.25 m is 1.2501 m by the next frame. The
  state exists only at the spawn and at a mode toggle, which is why M4b's lift test constructs
  it by hand rather than walking a body into it.
- **Reach moved and deliberately did not change.** Still 5 m. The table below says 3.5 m and the
  play-test says 5 m reads long; both are feel, and settling them is M4d's. Moving the field and
  retuning the number in one pass would make it impossible to tell which change did what.

**What M4c did not verify, and could not:** whether any of it feels like anything. Headless has
no keyboard. That is M4d's half, unchanged.

---

## M4d — Feel and measurement — done 2026-08-30

> **Answered: no, the player cannot outrun the streamer at any shipped setting.** Terminal
> 40 m/s holds with `held 0` at every configuration tried up to r12h/3v, and r6h/3v survives
> 90 m/s. It first loses at r12h/3v @ 90 m/s (3 held steps, longest 17 ms) and r16h/3v @ 120 m/s
> (12 steps, longest 83 ms) — well past anything shipped. **The constant stands with 2.25× of
> measured margin.** A falling step costs **16 voxel reads against M4a's predicted 252**, because
> `ResolveAxis` returns early on the two axes with zero displacement. Collider cost is
> 0.013 ms mean against a 16.7 ms frame. Full numbers and the two flicker-counter artefacts are
> in the worklog.


### The question this milestone is really asking

**Can the player outrun the streamer, and what does it feel like when they do?**

M2d measured streaming against a viewer flying horizontally at a constant height — which is
the case the flythrough was designed to produce, and is nothing like a player falling down a
shaft. A fall is vertical, it is fast, and the vertical view radius is the small one. It is
the first motion profile that can genuinely arrive somewhere before the world does.

The answer is a measurement and the harness is nearly built. Add a fall mode to the profiler
the way `--mine` was added, and report:

- **Substeps per step.** Voxel queries per step is already answered — M4a measured 41 for a
  walking step against a guessed ~400 — so `MoveResult.VoxelQueries` is a regression
  detector here rather than an open question.
- **Steps spent held**, waiting on an unloaded chunk beneath the player, and the longest such
  stall. This is the number that picks terminal velocity.
- **Ground-state flicker count** — a resting-contact regression detector that costs one
  counter.
- **Collider cost per step**, timed directly.

**Time it directly, do not read `frame ms`.** The headless frame-time trap has been walked
into three times — M2d-1, the mesh-apply budget, and M3d — and each time the spike did not
reproduce and the directly-timed number moved by a tenth of a millisecond. There is no reason
to expect the fourth encounter to look different.

### The half that has to be played

- **Does 0.25 m ground walk like ground, or like a staircase?** Step-up and step-down decide
  this and neither can be judged from a number.
- **Is the jump right?** Height, gravity, and whether coyote time and jump buffering are
  needed at all.
- ~~Is 3.5 m the right reach~~ — **answered 2026-08-30: 5 m stays**, generous and deliberately so. Survival mode is the recorded trigger to revisit it at 3 m or 2 m.
- **Does the box fit the world?** Specifically: walking down a tunnel dug with the starting
  pick, which is the coupling M4b names.

---

## Order and rationale

**M4a → M4b → M4c → M4d.**

M4a first: it is a pure function of a box, a displacement and a world, it needs neither
gravity nor input nor Godot, and its sharpest test — the sealed room — can be written before
anything else exists. M4b sits directly on it and adds only state. M4c is where the surprises
are, and it wants both of the others finished so that a bug there is unambiguously a binding
bug. M4d last, because feel cannot be judged before the thing exists.

**Deliberately not in M4:**

- **Swimming, buoyancy, and fluid drag** — M8. The solidity predicate already reads
  `IsFluid`, so a fluid is non-solid from the first day one exists, and wading is the default
  rather than a special case.
- **Falling damage and health** — M11, which is where health is.
- **Crouching, sneaking, and ledge-stopping** — no current mechanic asks for them.
- **Dropped items** — DESIGN is explicit that Godot physics is fine for those. They are not
  this collider's problem, and M6 is where they appear.
- **Placement colliding with the player** — M6, using the unstick rule M4c establishes.
- **Non-cube collision shapes** — slabs, ramps and ladders are all "a voxel is one material or
  another" violations wearing different hats, and M3c already argued that one.
- **The `PlayerTick` event** — DESIGN lists it on the event bus, which does not exist yet.
  A fixed-step solve in Core is what makes it expressible later; nothing here has to change
  for it.
- **Multiplayer prediction and reconciliation** — not now. Putting the solve in Core with a
  fixed step and a determinism test is the whole of what M4 owes it.
